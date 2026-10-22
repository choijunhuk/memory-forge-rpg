#nullable enable
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Codes = MemoryForge.Client.PracticeSessionCodes;
using MutationStatus = MemoryForge.Client.PracticeMutationStatus;

namespace MemoryForge.Client.Tests
{
    public partial class PracticeServerSessionPlayModeTests
    {
        const string ProgressionPath = "/dev/practice/progression", ExpeditionPath = "/dev/practice/expeditions";
        JObject progressionProfile = null!;

        static JObject ProgressionProfileJson(long revision = 3) => new JObject
        {
            ["accountId"] = Account, ["revision"] = revision, ["policyVersion"] = "account-policy-1", ["initialized"] = true,
            ["account"] = new JObject { ["level"] = 3, ["xp"] = 320L, ["next"] = 400L },
            ["tutorial"] = new JObject { ["completed"] = true },
            ["chapters"] = new JArray(new JObject { ["id"] = 1, ["stageLevel"] = 8, ["zones"] = new JArray(1), ["bosses"] = new JArray("b_bell"),
                ["unlockLevel"] = 3, ["firstClear"] = false, ["available"] = true }),
            ["characters"] = new JArray(new[] { "nia", "harin", "rien" }.Select(id => new JObject
            {
                ["charId"] = id, ["copies"] = 1, ["res"] = 0, ["level"] = 1, ["xp"] = 0L, ["asc"] = 0, ["skillLv"] = 1, ["ultLv"] = 1,
                ["weaponInstanceId"] = "starter-" + id, ["armor"] = JValue.CreateNull(), ["accessory"] = JValue.CreateNull()
            })),
            ["weapons"] = new JArray(new[] { ("nia", "device"), ("harin", "melee"), ("rien", "ritual") }.Select(pair => new JObject
            {
                ["instanceId"] = "starter-" + pair.Item1, ["weaponId"] = "w_basic_" + pair.Item2, ["level"] = 1, ["xp"] = 0L, ["asc"] = 0, ["refine"] = 1, ["equippedBy"] = pair.Item1
            })),
            ["lastParty"] = new JArray("nia", "harin", "rien"), ["activeSession"] = JValue.CreateNull()
        };

        void ProgressionFixture(bool noRun = true)
        {
            progressionProfile = ProgressionProfileJson();
            if (noRun) world.RunId = null;
            additionalReply = seen =>
            {
                if (seen.Method != "GET" || seen.Path != ProgressionPath) return null;
                var profile = (JObject)progressionProfile.DeepClone();
                profile["activeSession"] = world.RunId == null ? (JToken)JValue.CreateNull() : new JObject
                {
                    ["runId"] = world.RunId, ["mode"] = "story", ["chapter"] = 1, ["phase"] = world.Phase, ["version"] = (long)world.Version
                };
                return Reply.Json(200, profile);
            };
        }

        [Test]
        public void ProgressionRoutesStageOnlyExplicitIntentAndPreserveTheSinglePendingSlot()
        {
            string expedition = "{\"requestId\":\"owned-route-proof\",\"expectedRevision\":3,\"mode\":\"story\",\"chapter\":1,\"party\":[\"nia\",\"harin\",\"rien\"],\"useMemory\":true}";
            PendingPracticeCommand? staged = null;
            Assert.DoesNotThrow(() => staged = Outbox().Stage(ExpeditionPath, expedition));
            Assert.That(staged!.ExpectedRevision, Is.EqualTo(3));
            Assert.That(staged.Body, Is.EqualTo(expedition));
            byte[] pending = File.ReadAllBytes(RecordPath);
            Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(PracticeCommandOutbox.CommandRoute(RunA), Body("command", "another-command", 3)));
            Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(pending));
            Outbox().Confirm(staged);
            string abandon = "{\"requestId\":\"abandon-route-proof\",\"expectedVersion\":3}";
            Assert.DoesNotThrow(() => staged = Outbox().Stage("/dev/practice/runs/" + RunA + "/abandon", abandon));
            Assert.That(staged!.ExpectedVersion, Is.EqualTo(3)); Assert.That(staged.ExpectedRevision, Is.Null);
            Outbox().Confirm(staged);
            foreach (var forged in new[] { new JProperty("seed", 1), new JProperty("stats", new JObject()), new JProperty("credential", "never-stored"), new JProperty("length", "full") })
            {
                var body = JObject.Parse(expedition); body.Add(forged);
                Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(ExpeditionPath, body.ToString(Newtonsoft.Json.Formatting.None)), forged.Name);
                Assert.That(Outbox().Load(), Is.Null);
            }
        }

        [Test]
        public void ProgressionProfileHasOnlyItsExplicitRevisionContextAndKeepsDetachedTypedValues()
        {
            var json = ProgressionProfileJson(2147483648L); json["account"]!["xp"] = long.MaxValue; json["account"]!["next"] = 0L;
            var profile = PracticeProgressionSnapshot.Parse(Utf8(json), Account);
            Assert.That(profile.Revision, Is.EqualTo(2147483648L)); Assert.That(profile.Account.Xp, Is.EqualTo(long.MaxValue));
            Assert.That(profile.Account.Next, Is.Zero); Assert.That(profile.Characters.Single(character => character.CharId == "harin").WeaponInstanceId, Is.EqualTo("starter-harin"));
            Assert.That(profile.Weapons.Single(weapon => weapon.InstanceId == "starter-harin").EquippedBy, Is.EqualTo("harin"));
            CollectionAssert.AreEqual(new[] { "nia", "harin", "rien" }, profile.LastParty);
            var copy = profile.ToJObject(); copy["account"]!["xp"] = 0;
            Assert.That(profile.ToJObject()["account"]!["xp"]!.Value<long>(), Is.EqualTo(long.MaxValue));
            Assert.Throws<PracticeProtocolException>(() => PracticePublicJson.ParseObject(Utf8(json)), "ordinary run JSON still rejects account revision");
            Assert.Throws<PracticeProtocolException>(() => RecruitmentSnapshot.Parse(Utf8(json), Account), "profile is not a recruitment reply");
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(json), OtherAccount));
            foreach (string field in new[] { "seed", "rng", "draw", "ledger", "receipt", "binding", "credential", "futureField", "revision" })
            {
                var bad = (JObject)json.DeepClone(); bad["characters"]![0]![field] = 1;
                Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(bad), Account), field);
            }
            var missing = (JObject)json.DeepClone(); missing.Remove("initialized");
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(missing), Account));
        }

        [TestCase("revision", -1L)]
        [TestCase("account.level", 0L)]
        [TestCase("account.xp", -1L)]
        [TestCase("characters[0].copies", 0L)]
        [TestCase("characters[0].res", 7L)]
        [TestCase("characters[0].level", 61L)]
        [TestCase("characters[0].asc", 5L)]
        [TestCase("characters[0].skillLv", 11L)]
        [TestCase("characters[0].ultLv", 0L)]
        [TestCase("characters[0].armor", 16L)]
        [TestCase("characters[0].accessory", -1L)]
        [TestCase("weapons[0].refine", 6L)]
        public void ProgressionNumbersRejectMalformedOrOutOfBoundsValues(string path, long value)
        {
            var json = ProgressionProfileJson(); json.SelectToken(path)!.Replace(value);
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(json), Account));
            json = ProgressionProfileJson(); json.SelectToken(path)!.Replace(1.5);
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(json), Account));
        }

        [Test]
        public void ProgressionReadIsPureMonotonicAndRejectsWrongOwnersWithoutInventingAProfile()
        {
            ProgressionFixture(); var session = Session();
            Assert.That(Run(() => session.RefreshProgressionAsync()).Code, Is.EqualTo(Codes.Ok));
            Assert.That(host.Seen.Select(seen => seen.Path), Is.EqualTo(new[] { ProgressionPath })); Assert.That(host.Posts, Is.Empty); Assert.That(File.Exists(RecordPath), Is.False);
            progressionProfile["revision"] = 2L;
            Assert.That(Run(() => session.RefreshProgressionAsync()).Code, Is.EqualTo(Codes.RollbackRefused));
            Assert.That(session.Progression!.Revision, Is.EqualTo(3)); Assert.That(session.ProgressionNeedsRefresh, Is.True);
            progressionProfile["accountId"] = OtherAccount;
            var other = Session(); Assert.That(Run(() => other.RefreshProgressionAsync()).Code, Is.EqualTo(PracticeProtocolException.WrongAccount));
            Assert.That(other.Progression, Is.Null); Assert.That(host.Posts, Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AnExplicitOwnedExpeditionSendsOrderedIdsAndABooleanWithoutClientAuthority(bool useMemory)
        {
            ProgressionFixture(); var session = Session(); Run(() => session.RefreshProgressionAsync());
            post = seen =>
            {
                Assert.That(seen.Path, Is.EqualTo(ExpeditionPath)); var body = JObject.Parse(seen.Text);
                CollectionAssert.AreEquivalent(new[] { "requestId", "expectedRevision", "mode", "chapter", "party", "useMemory" }, body.Properties().Select(property => property.Name));
                Assert.That(body["mode"]!.Value<string>(), Is.EqualTo("story")); Assert.That(body["chapter"]!.Value<int>(), Is.EqualTo(1));
                CollectionAssert.AreEqual(new[] { "rien", "nia", "harin" }, body["party"]!.Values<string>());
                Assert.That(body["useMemory"]!.Type, Is.EqualTo(JTokenType.Boolean)); Assert.That(body["useMemory"]!.Value<bool>(), Is.EqualTo(useMemory));
                world.RunId = RunB; world.Version = 0; world.Phase = "MAP"; world.InBattle = false; progressionProfile["revision"] = 4L;
                return Reply.Json(200, RunJson(world, 0));
            };
            var result = Run(() => session.LaunchExpeditionAsync(3, "story", 1, new[] { "rien", "nia", "harin" }, useMemory));
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.Receipt!.ServerVersion, Is.Zero);
            Assert.That(result.Snapshot!.Run!.RunId, Is.EqualTo(RunB)); Assert.That(result.Progression!.Revision, Is.EqualTo(4));
            Assert.That(host.Posts.Length, Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
        }

        [TestCase(409, "STALE_REVISION", true)]
        [TestCase(409, "RUN_ACTIVE", true)]
        [TestCase(422, "TUTORIAL_REQUIRED", true)]
        [TestCase(422, "TUTORIAL_DONE", true)]
        [TestCase(422, "CHAPTER_LOCKED", true)]
        [TestCase(422, "NOT_OWNED", true)]
        [TestCase(422, "PARTY_SIZE", true)]
        [TestCase(422, "PARTY_DUPLICATE", true)]
        [TestCase(422, "OVER_CAPACITY", true)]
        [TestCase(422, "BAD_COUNT", false)]
        [TestCase(409, "STALE_VERSION", false)]
        [TestCase(409, "IDEMPOTENCY_CONFLICT", false)]
        [TestCase(400, "INVALID_REQUEST", false)]
        [TestCase(404, "RUN_NOT_FOUND", false)]
        [TestCase(503, "STORE_COMMIT_UNKNOWN", false)]
        public void ExpeditionRefusalsUseOnlyTheFrozenStatusCodePairs(int status, string code, bool clear)
        {
            ProgressionFixture(); var session = Session(); Run(() => session.RefreshProgressionAsync()); post = _ => Error(status, code);
            var result = Run(() => session.LaunchExpeditionAsync(3, "tutorial"));
            Assert.That(result.Status, Is.EqualTo(clear ? MutationStatus.NotCommitted : MutationStatus.Pending));
            Assert.That(result.Code, Is.EqualTo(code)); Assert.That(Outbox().Load() == null, Is.EqualTo(clear));
            Assert.That(world.RunId, Is.Null); Assert.That(world.MemoryVersion, Is.EqualTo(2));
        }

        [Test]
        public void ARevisionRefusalOfAFutureExpeditionStaysPendingUntilTheReadProvesItCannotCommit()
        {
            ProgressionFixture(); var pending = Outbox().Stage(ExpeditionPath, "{\"requestId\":\"future-expedition\",\"expectedRevision\":9,\"mode\":\"tutorial\",\"useMemory\":false}");
            post = _ => Error(409, "STALE_REVISION"); var session = Session();
            Assert.That(Run(() => session.RetryProgressionPendingAsync()).Status, Is.EqualTo(MutationStatus.Pending));
            Assert.That(Outbox().Load()!.Body, Is.EqualTo(pending.Body)); progressionProfile["revision"] = 9L;
            Assert.That(Run(() => session.RetryProgressionPendingAsync()).Status, Is.EqualTo(MutationStatus.NotCommitted)); Assert.That(Outbox().Load(), Is.Null);
        }

        [Test]
        public void LostExpeditionReplySurvivesSessionRecreationAndRetriesExactlyTheOriginalRequest()
        {
            ProgressionFixture(); var first = Session(); Run(() => first.RefreshProgressionAsync()); byte[]? receipt = null;
            post = _ =>
            {
                world.RunId = RunB; world.Version = 0; world.Phase = "MAP"; world.InBattle = false; progressionProfile["revision"] = 4L;
                receipt = Utf8(RunJson(world, 0)); return new Reply(200, receipt) { Drop = true };
            };
            Assert.That(Run(() => first.LaunchExpeditionAsync(3, "tutorial")).Status, Is.EqualTo(MutationStatus.Pending));
            var original = Outbox().Load()!; Assert.That(File.ReadAllText(RecordPath), Does.Not.Contain(Credential)); first.Dispose();
            var next = Session(); Run(() => next.RefreshProgressionAsync()); Run(() => next.RefreshAsync());
            Assert.That(host.Posts.Length, Is.EqualTo(1), "constructing and refreshing the next view never retries or creates");
            Assert.That(Run(() => next.RetryPendingAsync()).Code, Is.EqualTo(Codes.PendingUnresolved), "legacy retry cannot reinterpret the new route");
            post = _ => new Reply(200, receipt!);
            var retried = Run(() => next.RetryProgressionPendingAsync());
            Assert.That(retried.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(host.Posts.Last().Text, Is.EqualTo(original.Body));
            Assert.That(retried.Snapshot!.Run!.RunId, Is.EqualTo(RunB)); Assert.That(Outbox().Load(), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StateLessAbandonUsesOnlyTheFreshAccountPointerIncludingAnotherClientsNewRun(bool anotherRun)
        {
            ProgressionFixture(false); var session = Session(); var before = Refreshed(session);
            post = seen =>
            {
                Assert.That(seen.Path, Is.EqualTo(PracticeCommandOutbox.AbandonRoute(RunA)));
                var request = JObject.Parse(seen.Text); Assert.That(request.Count, Is.EqualTo(2)); Assert.That(request["expectedVersion"]!.Value<int>(), Is.EqualTo(3));
                world.Finished++; world.RunId = anotherRun ? RunB : null; world.Version = 0; world.Phase = "MAP"; world.InBattle = false; progressionProfile["revision"] = 4L;
                return Reply.Json(200, new JObject { ["runId"] = RunA, ["version"] = 4L, ["memoryVersion"] = 2 });
            };
            var result = Run(() => session.AbandonRunAsync(before.Context));
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.Receipt, Is.Null);
            Assert.That(result.AbandonReceipt!.RunId, Is.EqualTo(RunA)); Assert.That(result.AbandonReceipt.Version, Is.EqualTo(4));
            Assert.That(result.Snapshot!.Run?.RunId, Is.EqualTo(anotherRun ? RunB : null)); Assert.That(result.Snapshot.Account.MemoryVersion, Is.EqualTo(2));
            Assert.That(session.State.Snapshot!.Run?.RunId, Is.EqualTo(anotherRun ? RunB : null)); Assert.That(Outbox().Load(), Is.Null);
            int postAt = Array.FindIndex(host.Seen, seen => seen.Method == "POST");
            Assert.That(host.Seen.Skip(postAt + 1).Any(seen => seen.Method == "GET" && seen.Path == PracticeCommandOutbox.CreateRunRoute + "/" + RunA), Is.False, "a closed run is not fetched or reactivated");
        }

        [TestCase("extra-state")]
        [TestCase("wrong-version")]
        [TestCase("wrong-run")]
        [TestCase("future-memory")]
        [TestCase("non200")]
        public void AStateLessAbandonRejectsAnythingBeyondItsExactAcknowledgement(string fault)
        {
            ProgressionFixture(false); var session = Session(); var before = Refreshed(session);
            post = _ =>
            {
                var ack = new JObject { ["runId"] = RunA, ["version"] = 4L, ["memoryVersion"] = 2 };
                if (fault == "extra-state") ack["state"] = new JObject();
                if (fault == "wrong-version") ack["version"] = 5L;
                if (fault == "wrong-run") ack["runId"] = RunB;
                if (fault == "future-memory") ack["memoryVersion"] = 3;
                return Reply.Json(fault == "non200" ? 201 : 200, ack);
            };
            Assert.That(Run(() => session.AbandonRunAsync(before.Context)).Status, Is.EqualTo(MutationStatus.Pending));
            Assert.That(Outbox().Load(), Is.Not.Null); Assert.That(session.State.Snapshot!.Run!.RunId, Is.EqualTo(RunA));
        }

        [Test]
        public void APreviouslyCommittedOldCommandRetryCannotReactivateTheAbandonedRun()
        {
            ProgressionFixture(false); var session = Session(); Refreshed(session);
            var oldReceipt = Utf8(RunJson(world, 4, Events()));
            Outbox().Stage(PracticeCommandOutbox.CommandRoute(RunA), Body("commands", "old-committed-command", 3));
            world.RunId = RunB; world.Version = 0; world.Finished = 1; world.Phase = "MAP"; world.InBattle = false;
            post = _ => new Reply(200, oldReceipt);
            var retried = Run(() => session.RetryPendingAsync());
            Assert.That(retried.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(retried.Receipt!.RunId, Is.EqualTo(RunA));
            Assert.That(retried.Snapshot!.Run!.RunId, Is.EqualTo(RunB)); Assert.That(retried.PresentableEvents(), Is.Null); Assert.That(Outbox().Load(), Is.Null);
        }

        [Test]
        public void AppAuthenticationSurvivesViewDisposalButLogoutInvalidatesEverySessionAndNeverStoresTheSecret()
        {
            ProgressionFixture(); using (var owner = new MemoryForgeAccountConnection(host.Endpoint + "/", " " + Account + " ", Credential, RecordPath))
            {
                MemoryForgeAccountConnection.SetCurrent(owner); MemoryForgeAccountConnection.SetCurrent(owner);
                var first = owner.CreateSession(); Assert.That(host.Seen, Is.Empty, "creating a connection and view sends nothing");
                Assert.That(Run(() => first.RefreshProgressionAsync()).Code, Is.EqualTo(Codes.Ok)); first.Dispose();
                var second = owner.CreateSession(); Assert.That(Run(() => second.RefreshProgressionAsync()).Code, Is.EqualTo(Codes.Ok));
                Assert.That(owner.IsAuthenticated, Is.True); Assert.That(host.Posts, Is.Empty);
                Outbox().Stage(ExpeditionPath, "{\"requestId\":\"logout-pending-proof\",\"expectedRevision\":3,\"mode\":\"tutorial\",\"useMemory\":true}");
                byte[] original = File.ReadAllBytes(RecordPath); Assert.That(File.ReadAllText(RecordPath), Does.Not.Contain(Credential));
                owner.Logout(); owner.Logout(); Assert.That(owner.IsAuthenticated, Is.False); Assert.That(MemoryForgeAccountConnection.Current, Is.Null);
                Assert.That(Run(() => second.RefreshProgressionAsync()).Code, Is.EqualTo(Codes.Disposed));
                Assert.Throws<ObjectDisposedException>(() => owner.CreateSession()); Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(original));
                Assert.That(host.Posts, Is.Empty);
            }
        }

        [Test]
        public void SwitchingTheAppOwnerDisposesOldSessionsAndPreservesForeignPendingBytes()
        {
            ProgressionFixture(); using (var first = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            using (var other = new MemoryForgeAccountConnection(host.Endpoint, OtherAccount, Credential, RecordPath))
            {
                MemoryForgeAccountConnection.SetCurrent(first); var oldView = first.CreateSession();
                Outbox().Stage(ExpeditionPath, "{\"requestId\":\"foreign-owner-proof\",\"expectedRevision\":3,\"mode\":\"tutorial\",\"useMemory\":true}");
                byte[] original = File.ReadAllBytes(RecordPath); MemoryForgeAccountConnection.SetCurrent(other);
                Assert.That(first.IsAuthenticated, Is.False); Assert.That(Run(() => oldView.RefreshAsync()).Code, Is.EqualTo(Codes.Disposed));
                var newView = other.CreateSession(); Run(() => newView.RefreshProgressionAsync());
                Assert.That(newView.State.OutboxCode, Is.EqualTo(PracticeOutboxException.BindingMismatch));
                Assert.That(Run(() => newView.LaunchExpeditionAsync(3, "tutorial")).Code, Is.EqualTo(PracticeOutboxException.BindingMismatch));
                Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(original)); Assert.That(host.Posts, Is.Empty);
            }
        }

        [TestCase(409, "STALE_VERSION", "same", 5, true)]
        [TestCase(409, "STALE_VERSION", "same", 3, false)]
        [TestCase(409, "STALE_VERSION", "other", 9, false)]
        [TestCase(409, "RUN_NOT_ACTIVE", "none", 0, true)]
        [TestCase(409, "RUN_NOT_ACTIVE", "other", 0, true)]
        [TestCase(409, "IDEMPOTENCY_CONFLICT", "same", 5, false)]
        [TestCase(404, "RUN_NOT_FOUND", "none", 0, false)]
        [TestCase(422, "PARTY_SIZE", "same", 5, false)]
        public void AbandonRefusalsRequireTheirExactRouteAndVersionProof(int status, string code, string current, int version, bool clears)
        {
            ProgressionFixture(); world.RunId = current == "same" ? RunA : current == "other" ? RunB : null; world.Version = version;
            var pending = Outbox().Stage(PracticeCommandOutbox.AbandonRoute(RunA), "{\"requestId\":\"abandon-proof-retry\",\"expectedVersion\":5}");
            post = _ => Error(status, code); var session = Session();
            var result = Run(() => session.RetryProgressionPendingAsync());
            Assert.That(result.Status, Is.EqualTo(clears ? MutationStatus.NotCommitted : MutationStatus.Pending));
            Assert.That(result.Code, Is.EqualTo(code)); Assert.That(Outbox().Load() == null, Is.EqualTo(clears));
            if (!clears) Assert.That(Outbox().Load()!.Body, Is.EqualTo(pending.Body));
            Assert.That(world.MemoryVersion, Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LostOrTruncatedAbandonAcknowledgementRetriesTheSameRequestAfterTheRunClosed(bool truncated)
        {
            ProgressionFixture(false); var first = Session(); var before = Refreshed(first); byte[]? receipt = null;
            post = _ =>
            {
                world.RunId = null; world.Finished++; progressionProfile["revision"] = 4L;
                receipt = Utf8(new JObject { ["runId"] = RunA, ["version"] = 4L, ["memoryVersion"] = 2 });
                return truncated ? new Reply(200, receipt) { DeclaredLength = receipt.Length + 16 } : new Reply(200, receipt) { Drop = true };
            };
            Assert.That(Run(() => first.AbandonRunAsync(before.Context)).Status, Is.EqualTo(MutationStatus.Pending));
            var original = Outbox().Load()!; first.Dispose();
            var next = Session(); Assert.That(Run(() => next.RefreshAsync()).Snapshot!.Run, Is.Null);
            Assert.That(host.Posts.Length, Is.EqualTo(1), "a closed-account refresh never retries");
            post = _ => new Reply(200, receipt!);
            var retried = Run(() => next.RetryProgressionPendingAsync());
            Assert.That(retried.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(retried.Snapshot!.Run, Is.Null);
            Assert.That(retried.AbandonReceipt!.RunId, Is.EqualTo(RunA));
            Assert.That(host.Posts.Last().Text, Is.EqualTo(original.Body)); Assert.That(Outbox().Load(), Is.Null);
        }

        [Test]
        public void ARunShapedNon200ExpeditionReplyNeverConfirmsThePendingDeparture()
        {
            ProgressionFixture(); var session = Session(); Run(() => session.RefreshProgressionAsync());
            post = _ =>
            {
                world.RunId = RunB; world.Version = 0; world.Phase = "MAP"; world.InBattle = false;
                return Reply.Json(201, RunJson(world, 0));
            };
            var result = Run(() => session.LaunchExpeditionAsync(3, "tutorial"));
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Pending)); Assert.That(result.Receipt, Is.Null);
            Assert.That(result.Snapshot, Is.Null); Assert.That(session.State.Snapshot, Is.Null); Assert.That(Outbox().Load(), Is.Not.Null);
        }

        [Test]
        public void TutorialLaunchOmitsPartyAndChapterAndTheOutboxRejectsOtherRequestShapes()
        {
            ProgressionFixture(); var session = Session(); Run(() => session.RefreshProgressionAsync());
            post = seen =>
            {
                var body = JObject.Parse(seen.Text);
                CollectionAssert.AreEquivalent(new[] { "requestId", "expectedRevision", "mode", "useMemory" }, body.Properties().Select(property => property.Name));
                Assert.That(body["mode"]!.Value<string>(), Is.EqualTo("tutorial")); Assert.That(body["useMemory"]!.Value<bool>(), Is.True);
                return Error(422, "OVER_CAPACITY");
            };
            Assert.That(Run(() => session.LaunchExpeditionAsync(3, "tutorial")).Status, Is.EqualTo(MutationStatus.NotCommitted));
            const string valid = "{\"requestId\":\"invalid-shape-proof\",\"expectedRevision\":3,\"mode\":\"tutorial\",\"useMemory\":true}";
            foreach (var extra in new[] { new JProperty("chapter", 0), new JProperty("party", new JArray()), new JProperty("expectedVersion", 0) })
            {
                var invalid = JObject.Parse(valid); invalid.Add(extra);
                Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(ExpeditionPath, invalid.ToString(Newtonsoft.Json.Formatting.None)));
            }
            var nullable = JObject.Parse(valid); nullable["useMemory"] = JValue.CreateNull();
            Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(ExpeditionPath, nullable.ToString(Newtonsoft.Json.Formatting.None)));
            Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(PracticeCommandOutbox.AbandonRoute(RunA), "{\"requestId\":\"invalid-abandon-proof\",\"expectedVersion\":3,\"state\":{}}"));
            Assert.That(Outbox().Load(), Is.Null);
        }
    }
}
