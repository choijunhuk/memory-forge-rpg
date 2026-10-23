// BCL loopback protocol tests; they reuse the run transport fixture, not an alternative HTTP implementation.
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Codes = MemoryForge.Client.PracticeSessionCodes;
using MutationStatus = MemoryForge.Client.PracticeMutationStatus;

namespace MemoryForge.Client.Tests
{
    public partial class PracticeServerSessionPlayModeTests
    {
        const string RecruitmentRoute = "/dev/practice/recruitment";
        JObject recruitment = null!;

        void RecruitmentFixture()
        {
            recruitment = RecruitmentJson();
            additionalReply = seen => seen.Method == "GET" && seen.Path == RecruitmentRoute ? Reply.Json(200, recruitment) : null;
        }

        static JObject RecruitmentJson(long revision = 3) => new JObject
        {
            ["accountId"] = Account, ["revision"] = revision,
            ["wallet"] = new JObject { ["freeCrystals"] = 1600L, ["paidCrystals"] = 0L,
                ["tickets"] = new JObject { ["limitedTicket"] = 0, ["weaponTicket"] = 0, ["standardTicket"] = 10 }, ["shards"] = 0L, ["afterglow"] = 0L },
            ["ownedChars"] = new JObject(), ["ownedWeapons"] = new JObject(), ["launchClaimed"] = false,
            ["pity"] = new JObject(new[] { "character", "weapon", "standard", "beginner" }.Select(kind => new JProperty(kind,
                new JObject { ["n"] = 0, ["srN"] = 0, ["guar"] = false, ["srGuar"] = false, ["total"] = 0, ["done"] = false, ["gotSsr"] = false, ["selectorUsed"] = false }))),
            ["banners"] = new JArray(new JObject { ["id"] = "standard", ["name"] = "상시 모집", ["kind"] = "standard", ["ticket"] = "standardTicket", ["costPerPull"] = 160,
                ["tenPullTickets"] = 10, ["curve"] = new JObject { ["ssrBase"] = 80, ["softStart"] = 65, ["softStep"] = 600, ["hard"] = 80, ["srBase"] = 600, ["srGuarantee"] = 10 },
                ["ssrPool"] = new JArray("sera"), ["srChars"] = new JArray("nia"), ["srWeapons"] = new JArray("w_iron_tonfa"), ["rItems"] = new JArray("잔향 기록: 경험치") }),
            ["history"] = new JArray()
        };

        static JObject RecruitmentPullJson() => new JObject
        {
            ["rarity"] = "SSR", ["kind"] = "char", ["id"] = "sera", ["featured"] = false, ["lost"] = false, ["pityAt"] = 1,
            ["isNew"] = true, ["resonance"] = 0, ["refine"] = 0, ["shards"] = 0, ["afterglow"] = 0
        };

        JObject RecruitmentReceiptJson(Seen seen, string kind = "pull", int count = 1, long revision = 4) => new JObject
        {
            ["requestId"] = JObject.Parse(seen.Text)["requestId"], ["accountId"] = Account, ["revision"] = revision, ["kind"] = kind,
            ["results"] = new JArray(Enumerable.Range(0, count).Select(_ => RecruitmentPullJson())), ["snapshot"] = RecruitmentJson(revision)
        };

        [Test]
        public void BeginnerZeroCapCannotReachProbabilityDisclosure()
        {
            var json = RecruitmentJson();
            json["banners"]![0]!["kind"] = "beginner";
            json["banners"]![0]!["curve"]!["maxPulls"] = 0;
            Assert.Throws<PracticeProtocolException>(() => RecruitmentSnapshot.Parse(Utf8(json), Account));
        }

        [Test]
        public void RecruitmentParserRequiresTypedCompletePublicMembersAndRetainsOldPrivateGuard()
        {
            var good = RecruitmentJson(2147483648L);
            Assert.That(RecruitmentSnapshot.Parse(Utf8(good), Account).Revision, Is.EqualTo(2147483648L));
            foreach (string field in new[] { "seed", "rng", "rolls", "credential", "authorization", "growth" })
            {
                var bad = (JObject)good.DeepClone(); bad["wallet"]![field] = 1;
                Assert.Throws<PracticeProtocolException>(() => RecruitmentSnapshot.Parse(Utf8(bad), Account), field);
            }
            foreach (string field in new[] { "requestId", "revision" })
            {
                var bad = (JObject)good.DeepClone(); bad["ownedChars"]![field] = 1;
                Assert.Throws<PracticeProtocolException>(() => RecruitmentSnapshot.Parse(Utf8(bad), Account), "contextual " + field);
            }
            foreach (string body in new[] { "{}", "{\"revision\":0,\"revision\":0}", "{\"revision\":NaN}" })
                Assert.Throws<PracticeProtocolException>(() => RecruitmentSnapshot.Parse(Utf8Text(body), Account));
            var fractional = (JObject)good.DeepClone(); fractional["wallet"]!["freeCrystals"] = 1.5;
            Assert.Throws<PracticeProtocolException>(() => RecruitmentSnapshot.Parse(Utf8(fractional), Account));
            var named = RecruitmentPullJson(); named["kind"] = "item"; named["id"] = "잔향 기록: 경험치";
            good["history"] = new JArray(new JObject { ["requestId"] = "item-request", ["bannerId"] = "standard", ["results"] = new JArray(named) });
            Assert.That(RecruitmentSnapshot.Parse(Utf8(good), Account).History.Single().Results.Single().Id, Is.EqualTo("잔향 기록: 경험치"));
            Assert.Throws<PracticeProtocolException>(() => PracticePublicJson.ParseObject(Utf8(good)));
            Assert.Throws<PracticeProtocolException>(() => RecruitmentSnapshot.Parse(Utf8(good), OtherAccount));
        }

        [Test]
        public void RecruitmentCurveBoundsCapDisclosureWithoutRejectingBeginnerSoftStart()
        {
            var snapshot = RecruitmentJson();
            var curve = (JObject)snapshot["banners"]![0]!["curve"]!;
            curve["softStart"] = 66; curve["hard"] = 50;
            Assert.That(RecruitmentSnapshot.Parse(Utf8(snapshot), Account).Banners.Single().Curve.SoftStart, Is.EqualTo(66));
            foreach (string field in new[] { "softStart", "hard", "srGuarantee" })
            {
                var bad = (JObject)snapshot.DeepClone(); bad["banners"]![0]!["curve"]![field] = int.MaxValue;
                Assert.Throws<PracticeProtocolException>(() => RecruitmentSnapshot.Parse(Utf8(bad), Account), field);
            }
        }

        [Test]
        public void RecruitmentCurveSupportsOptionalEligibilityMetadataAndKeepsLegacyReplies()
        {
            var snapshot = RecruitmentJson();
            var legacy = RecruitmentSnapshot.Parse(Utf8(snapshot), Account).Banners.Single().Curve;
            Assert.That(legacy.MaxPulls, Is.Null); Assert.That(legacy.SelectorAt, Is.Null);
            snapshot["banners"]![0]!["curve"]!["maxPulls"] = 40;
            snapshot["banners"]![0]!["curve"]!["selectorAt"] = 300;
            var curve = RecruitmentSnapshot.Parse(Utf8(snapshot), Account).Banners.Single().Curve;
            Assert.That(curve.MaxPulls, Is.EqualTo(40)); Assert.That(curve.SelectorAt, Is.EqualTo(300));
            foreach (var badField in new[] { new JProperty("unknown", 1), new JProperty("maxPulls", 1001), new JProperty("selectorAt", 100001),
                new JProperty("maxPulls", -1), new JProperty("selectorAt", 1.5), new JProperty("maxPulls", JValue.CreateNull()) })
            {
                var bad = (JObject)snapshot.DeepClone(); bad["banners"]![0]!["curve"]![badField.Name] = badField.Value;
                Assert.Throws<PracticeProtocolException>(() => RecruitmentSnapshot.Parse(Utf8(bad), Account), badField.Name);
            }
        }

        [TestCase("character")]
        [TestCase("weapon")]
        public void RecruitmentBannerRefusesKindsWithoutFeaturedAndRotationDisclosure(string kind)
        {
            var snapshot = RecruitmentJson(); snapshot["banners"]![0]!["kind"] = kind;
            Assert.Throws<PracticeProtocolException>(() => RecruitmentSnapshot.Parse(Utf8(snapshot), Account));
        }

        [Test]
        public void RecruitmentWalletRejectsAnOverflowingCrystalTotal()
        {
            var snapshot = RecruitmentJson();
            snapshot["wallet"]!["freeCrystals"] = long.MaxValue;
            Assert.That(RecruitmentSnapshot.Parse(Utf8(snapshot), Account).FreeCrystals, Is.EqualTo(long.MaxValue));
            snapshot["wallet"]!["paidCrystals"] = 1;
            Assert.Throws<PracticeProtocolException>(() => RecruitmentSnapshot.Parse(Utf8(snapshot), Account));
        }

        [TestCase(3L, 103L)]
        [TestCase(long.MaxValue, long.MaxValue)]
        public void RecruitmentReceiptRequiresExactlyOneRepresentableRevisionAdvance(long expected, long receiptRevision)
        {
            RecruitmentFixture(); recruitment = RecruitmentJson(expected);
            var pending = Outbox().Stage(PracticeCommandOutbox.RecruitmentPullRoute,
                "{\"requestId\":\"revision-proof\",\"expectedRevision\":" + expected.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"bannerId\":\"standard\",\"count\":1}");
            post = seen => Reply.Json(200, RecruitmentReceiptJson(seen, revision: receiptRevision));
            var result = Run(() => Session().RetryRecruitmentPendingAsync());
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Pending));
            Assert.That(result.Code, Is.EqualTo(PracticeProtocolException.Mismatch));
            Assert.That(Outbox().Load()!.Body, Is.EqualTo(pending.Body));
        }

        [Test]
        public void RecruitmentExactPendingSurvivesLostReplyRestartAndNeverStoresCredentialsOrRollsBack()
        {
            RecruitmentFixture();
            var first = Session();
            Assert.That(Run(() => first.RefreshRecruitmentAsync()).Code, Is.EqualTo(Codes.Ok));
            byte[]? committed = null;
            post = seen =>
            {
                committed = Utf8(RecruitmentReceiptJson(seen)); recruitment = RecruitmentJson(4);
                return new Reply(200, committed) { Drop = true };
            };
            Assert.That(Run(() => first.PullRecruitmentAsync(3, "standard", 1)).Status, Is.EqualTo(MutationStatus.Pending));
            var original = Outbox().Load()!;
            string disk = File.ReadAllText(RecordPath);
            Assert.That(disk.Contains(Credential), Is.False);
            first.Dispose();
            recruitment = RecruitmentJson(8);
            post = seen => new Reply(200, committed!);
            var second = Session();
            Assert.That(Run(() => second.RefreshRecruitmentAsync()).Snapshot!.Revision, Is.EqualTo(8));
            Assert.That(Run(() => second.RetryPendingAsync()).Code, Is.EqualTo(Codes.PendingUnresolved));
            Assert.That(host.Posts.Length, Is.EqualTo(1), "run retry must not post a recruitment request");
            var retried = Run(() => second.RetryRecruitmentPendingAsync());
            Assert.That(retried.Status, Is.EqualTo(MutationStatus.Committed));
            Assert.That(retried.Receipt!.Results.Single().Id, Is.EqualTo("sera"));
            Assert.That(retried.Snapshot!.Revision, Is.EqualTo(8));
            Assert.That(host.Posts.Last().Text, Is.EqualTo(original.Body));
            Assert.That(Outbox().Load(), Is.Null);
            recruitment = RecruitmentJson(7);
            Assert.That(Run(() => second.RefreshRecruitmentAsync()).Code, Is.EqualTo(Codes.RollbackRefused));
            Assert.That(second.Recruitment!.Revision, Is.EqualTo(8));
        }

        [TestCase("accountId")]
        [TestCase("requestId")]
        [TestCase("kind")]
        [TestCase("count")]
        [TestCase("snapshot-owner")]
        [TestCase("snapshot-revision")]
        public void RecruitmentWrongReceiptCannotResolvePending(string mismatch)
        {
            RecruitmentFixture(); var session = Session(); Run(() => session.RefreshRecruitmentAsync());
            post = seen =>
            {
                var receipt = RecruitmentReceiptJson(seen);
                if (mismatch == "accountId") receipt["accountId"] = OtherAccount;
                if (mismatch == "requestId") receipt["requestId"] = "other-request";
                if (mismatch == "kind") receipt["kind"] = "selector";
                if (mismatch == "count") receipt["results"] = new JArray();
                if (mismatch == "snapshot-owner") receipt["snapshot"]!["accountId"] = OtherAccount;
                if (mismatch == "snapshot-revision") receipt["snapshot"]!["revision"] = 5;
                return Reply.Json(200, receipt);
            };
            var refused = Run(() => session.PullRecruitmentAsync(3, "standard", 1));
            Assert.That(refused.Status, Is.EqualTo(MutationStatus.Pending));
            Assert.That(Outbox().Load(), Is.Not.Null);
        }

        [Test]
        public void RecruitmentAndRunMutationsShareOneDurablePendingSlot()
        {
            RecruitmentFixture(); var session = Session(); var run = Refreshed(session);
            Run(() => session.RefreshRecruitmentAsync());
            Outbox().Stage(PracticeCommandOutbox.CommandRoute(RunA), "{\"requestId\":\"pending-run\",\"expectedVersion\":3,\"kind\":\"end\",\"args\":{}}");
            Assert.That(Run(() => session.PullRecruitmentAsync(3, "standard", 1)).Code, Is.EqualTo(Codes.PendingUnresolved));
            Assert.That(Run(() => session.RetryRecruitmentPendingAsync()).Code, Is.EqualTo(Codes.PendingUnresolved));
            Assert.That(host.Posts, Is.Empty);
            Outbox().Confirm(Outbox().Load()!);
            Outbox().Stage(PracticeCommandOutbox.RecruitmentPullRoute, "{\"requestId\":\"pending-pull\",\"expectedRevision\":3,\"bannerId\":\"standard\",\"count\":1}");
            Assert.That(Run(() => session.CommandAsync(run.Context, "end", null)).Code, Is.EqualTo(Codes.PendingUnresolved));
            Assert.That(host.Posts, Is.Empty);
        }

        [TestCase(409, "STALE_REVISION", 3, MutationStatus.NotCommitted)]
        [TestCase(422, "NOT_ENOUGH_CURRENCY", 3, MutationStatus.NotCommitted)]
        [TestCase(422, "NOT_ENOUGH_CURRENCY", 2, MutationStatus.Pending)]
        [TestCase(409, "IDEMPOTENCY_CONFLICT", 3, MutationStatus.Pending)]
        [TestCase(503, "STORE_COMMIT_UNKNOWN", 3, MutationStatus.Pending)]
        public void RecruitmentRefusalClearsOnlyWithSameOwnerRevisionProof(int status, string code, long revision, MutationStatus expected)
        {
            RecruitmentFixture(); recruitment = RecruitmentJson(revision);
            Outbox().Stage(PracticeCommandOutbox.RecruitmentPullRoute, "{\"requestId\":\"pending-pull\",\"expectedRevision\":3,\"bannerId\":\"standard\",\"count\":1}");
            post = _ => Error(status, code);
            Assert.That(Run(() => Session().RetryRecruitmentPendingAsync()).Status, Is.EqualTo(expected));
            Assert.That(Outbox().Load() == null, Is.EqualTo(expected == MutationStatus.NotCommitted));
        }

        [Test]
        public void RecruitmentOwnerHandshakeAndStaleRevisionStageNothing()
        {
            RecruitmentFixture(); var session = Session(); Run(() => session.RefreshRecruitmentAsync());
            recruitment["accountId"] = OtherAccount;
            Assert.That(Run(() => session.PullRecruitmentAsync(3, "standard", 1)).Code, Is.EqualTo(PracticeProtocolException.Mismatch));
            Assert.That(Outbox().Load(), Is.Null); Assert.That(host.Posts, Is.Empty);
            recruitment = RecruitmentJson(4); Run(() => session.RefreshRecruitmentAsync()); recruitment = RecruitmentJson(5);
            Assert.That(Run(() => session.PullRecruitmentAsync(4, "standard", 1)).Code, Is.EqualTo(Codes.RefreshRequired));
            Assert.That(Outbox().Load(), Is.Null); Assert.That(host.Posts, Is.Empty);
        }

        [TestCase("launch")]
        [TestCase("selector")]
        public void RecruitmentClaimsValidateTheirOwnReceiptsAndUseTheSharedTransport(string kind)
        {
            RecruitmentFixture(); var session = Session(); Run(() => session.RefreshRecruitmentAsync());
            post = seen =>
            {
                Assert.That(seen.Path, Is.EqualTo(kind == "launch" ? PracticeCommandOutbox.RecruitmentLaunchRoute : PracticeCommandOutbox.RecruitmentSelectorRoute));
                Assert.That(JObject.Parse(seen.Text)["expectedRevision"]!.Value<long>(), Is.EqualTo(3));
                Assert.That(seen.Authorization, Is.EqualTo("Bearer " + Credential));
                var receipt = RecruitmentReceiptJson(seen, kind, kind == "launch" ? 0 : 1); recruitment = RecruitmentJson(4);
                return Reply.Json(200, receipt);
            };
            var result = kind == "launch" ? Run(() => session.ClaimRecruitmentLaunchAsync(3)) : Run(() => session.ClaimRecruitmentSelectorAsync(3, "sera"));
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(Outbox().Load(), Is.Null);
            Assert.That(session.RecruitmentNeedsRefresh, Is.False);
        }

        [Test]
        public void RecruitmentFreshReadCannotLagTheCommittedReceiptAndGateIsSharedWithRuns()
        {
            RecruitmentFixture(); var session = Session(); var context = Refreshed(session).Context; Run(() => session.RefreshRecruitmentAsync());
            var entered = new TaskCompletionSource<bool>(); var release = new TaskCompletionSource<bool>();
            post = seen => { entered.TrySetResult(true); return new Reply(200, Utf8(RecruitmentReceiptJson(seen))) { Hold = release.Task }; };
            var mutation = Task.Run(() => session.PullRecruitmentAsync(3, "standard", 1));
            try
            {
                Assert.That(entered.Task.Wait(Bound), Is.True);
                Assert.That(Run(() => session.CommandAsync(context, "end", null)).Code, Is.EqualTo(Codes.Busy));
            }
            finally { release.TrySetResult(true); }
            var result = Wait(mutation);
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed));
            Assert.That(result.RefreshCode, Is.EqualTo(Codes.RollbackRefused)); Assert.That(result.Snapshot, Is.Null);
            Assert.That(session.Recruitment!.Revision, Is.EqualTo(3)); Assert.That(session.RecruitmentNeedsRefresh, Is.True);
        }

        [Test]
        public void RecruitmentTimeoutKeepsExactPendingAndOutboxRejectsAuthorityFields()
        {
            RecruitmentFixture(); var session = Session(TimeSpan.FromSeconds(1)); Run(() => session.RefreshRecruitmentAsync());
            var release = new TaskCompletionSource<bool>();
            post = seen => new Reply(200, Utf8(RecruitmentReceiptJson(seen))) { Hold = release.Task };
            try
            {
                var result = Run(() => session.PullRecruitmentAsync(3, "standard", 1));
                Assert.That(result.Code, Is.EqualTo(Codes.Timeout));
                Assert.That(Outbox().Load()!.Body, Is.EqualTo(host.Posts.Single().Text));
            }
            finally { release.TrySetResult(true); }
            foreach (string extra in new[] { "seed", "result", "growth", "credential" })
                Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(PracticeCommandOutbox.RecruitmentPullRoute,
                    "{\"requestId\":\"other-request\",\"expectedRevision\":3,\"bannerId\":\"standard\",\"count\":1,\"" + extra + "\":1}"));
        }
    }
}
