#nullable enable
using System;
using System.Collections.Generic;
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
        const string SupportSelectionPath = "/dev/practice/progression/support";
        [Test]
        public void SupportSelectionUsesTheSameExactPendingSlotAndQueriesNeverStage()
        {
            string body = "{\"requestId\":\"support-selection-proof\",\"expectedRevision\":3,\"charId\":null}";
            PendingPracticeCommand? pending = null;
            Assert.DoesNotThrow(() => pending = Outbox().Stage(SupportSelectionPath, body));
            Assert.That(pending!.ExpectedRevision, Is.EqualTo(3)); Assert.That(pending.Body, Is.EqualTo(body)); byte[] bytes = File.ReadAllBytes(RecordPath);
            Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(PracticeCommandOutbox.CharacterLevelUpRoute("nia"), "{\"requestId\":\"other-command\",\"expectedRevision\":3,\"targetLevel\":2}"));
            Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(bytes)); Outbox().Confirm(pending);
            foreach (string route in new[] { "/dev/practice/runs/" + RunA + "/support-query", EncountersPath + "/" + GrowthEncounter + "/support-query" })
                Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(route, "{\"expectedVersion\":3,\"caller\":\"nia\"}"));
            foreach (string route in new[] { PracticeCommandOutbox.CommandRoute(RunA), PracticeCommandOutbox.EncounterCommandRoute(GrowthEncounter) })
            {
                string command = "{\"requestId\":\"support-command-proof\",\"expectedVersion\":3,\"kind\":\"support\",\"args\":{\"caller\":\"nia\",\"target\":\"enemy1\",\"picks\":[\"p1\"]}}";
                Assert.DoesNotThrow(() => pending = Outbox().Stage(route, command)); Outbox().Confirm(pending!);
            }
        }
        static JObject SupportProfileJson(long revision = 3, string? selected = "nia")
        {
            var profile = EquipmentProfileJson(revision); profile["policyVersion"] = "account-progression-20261021-v4";
            profile["supportSelection"] = selected == null ? (JToken)JValue.CreateNull() : selected; return profile;
        }
        static JObject SupportSummaryJson(bool used = false) => new JObject { ["charId"] = "nia", ["used"] = used, ["gaugeCost"] = 100 };
        void SupportFixture(bool encounter)
        {
            EquipmentFixture(encounter); progressionProfile = SupportProfileJson();
            if (encounter) growthEncounters[GrowthEncounter]["state"]!["support"] = SupportSummaryJson();
            else
            {
                world.RunId = RunA; world.InBattle = true; world.Phase = "BATTLE";
                var battle = Battle(world); battle["support"] = SupportSummaryJson(); battle["actors"]![0]!["gauge"] = 100;
                world.Extra = new JObject { ["battle"] = battle };
            }
            var fallback = additionalReply;
            additionalReply = seen =>
            {
                if (!encounter && seen.Method == "GET" && seen.Path == ProgressionPath)
                {
                    var profile = GrowthCurrentProfile(); profile["activeSession"] = new JObject { ["runId"] = RunA, ["mode"] = "story", ["chapter"] = 1, ["phase"] = world.Phase, ["version"] = world.Version };
                    return Reply.Json(200, profile);
                }
                return fallback!(seen);
            };
        }
        JObject SupportQueryJson(bool encounter, string caller = "sera", string? target = null, string[]? picks = null, bool hidden = false)
        {
            var reply = QueryJson(world, "unused", "now", target, picks ?? Array.Empty<string>());
            if (encounter)
            {
                reply.Remove("runId"); reply["encounterId"] = GrowthEncounter; reply["version"] = growthEncounters[GrowthEncounter]["version"]!.DeepClone(); reply["phase"] = "PLAYER";
                ((JObject)reply["context"]!).Remove("node");
            }
            reply["source"] = new JObject { ["kind"] = "support", ["caller"] = caller, ["charId"] = "nia", ["name"] = "니아", ["ultName"] = "지원 필살기", ["text"] = "순수 지원 예측", ["attr"] = "spark",
                ["gaugeCost"] = 100, ["used"] = false, ["ready"] = true, ["code"] = JValue.CreateNull(), ["needsTarget"] = "one", ["target"] = target == null ? (JToken)JValue.CreateNull() : target, ["picks"] = new JArray(picks ?? Array.Empty<string>()) };
            JObject Slot(bool next) => new JObject { ["base"] = 1, ["temp"] = 0, ["max"] = 3, ["cap"] = 1, ["used"] = next ? 1 : 0, ["sealed"] = false, ["due"] = next ? 3 : 4 };
            reply["slots"] = new JObject { ["next"] = Slot(true), ["after"] = Slot(false), ["sealedUntil"] = 0 };
            reply["forecast"] = new JObject { ["error"] = JValue.CreateNull(), ["mode"] = "now", ["cost"] = 100, ["due"] = JValue.CreateNull(), ["hidden"] = hidden,
                ["enemyHpLoss"] = hidden ? (JToken)JValue.CreateNull() : 7, ["enemyDamage"] = hidden ? (JToken)JValue.CreateNull() : 7, ["newlyBroken"] = hidden ? (JToken)JValue.CreateNull() : 0,
                ["blockDelta"] = hidden ? (JToken)JValue.CreateNull() : 0, ["hpDelta"] = hidden ? (JToken)JValue.CreateNull() : 0, ["defeat"] = hidden ? (JToken)JValue.CreateNull() : false,
                ["enemyHpLosses"] = hidden ? new JArray() : new JArray(new JObject { ["uid"] = "e1", ["hpLoss"] = 7 }) };
            return reply;
        }
        static JObject SupportSelectionReply(JObject profile, string? before, string? after, string requestId)
        {
            var next = (JObject)profile.DeepClone(); next["policyVersion"] = "account-progression-20261021-v4"; next["revision"] = 4L; next["supportSelection"] = after == null ? (JToken)JValue.CreateNull() : after;
            JObject Selection(string? value) => new JObject { ["charId"] = value == null ? (JToken)JValue.CreateNull() : value };
            return new JObject { ["requestId"] = requestId, ["revision"] = 4L, ["kind"] = "supportSelection", ["target"] = Selection(after), ["before"] = Selection(before), ["after"] = Selection(after), ["cost"] = new JObject(), ["profile"] = next };
        }
        [Test]
        public void SupportProfileIsExplicitV4AndNeverRewritesHistoricSelectionAbsence()
        {
            var old = EquipmentProfileJson(); var v3 = PracticeProgressionSnapshot.Parse(Utf8(old), Account);
            Assert.That(v3.HasSupportSelection, Is.False); Assert.That(v3.SupportSelection, Is.Null); Assert.That(JToken.DeepEquals(v3.ToJObject(), old), Is.True);
            var current = SupportProfileJson(); var v4 = PracticeProgressionSnapshot.Parse(Utf8(current), Account);
            Assert.That(v4.HasSupportSelection, Is.True); Assert.That(v4.SupportSelection, Is.EqualTo("nia")); Assert.That(v4.Characters[0].Stats!.Hp, Is.EqualTo(v3.Characters[0].Stats!.Hp));
            current["supportSelection"] = JValue.CreateNull(); Assert.That(PracticeProgressionSnapshot.Parse(Utf8(current), Account).HasSupportSelection, Is.True);
            current.Remove("supportSelection"); Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(current), Account));
            old["supportSelection"] = JValue.CreateNull(); Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(old), Account));
            current = SupportProfileJson(selected: "not-owned"); Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(current), Account));
            current = SupportProfileJson(); current["supportSelection"] = 1; Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(current), Account));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void SupportSummaryAndCallerEventsAreNarrowPublicProjectionsOnly(bool encounter)
        {
            SupportFixture(encounter);
            var reply = encounter ? (JObject)growthEncounters[GrowthEncounter].DeepClone() : RunJson(world, 3);
            reply["events"] = new JArray(new JObject { ["e"] = "support", ["actor"] = "nia", ["caller"] = "sera" });
            if (encounter) Assert.That(PracticeEncounterSnapshot.Parse(Utf8(reply), Account).Support!.GaugeCost, Is.EqualTo(100));
            else Assert.That(PracticeRunView.Parse(Utf8(reply), RunA).Support!.CharId, Is.EqualTo("nia"));
            var state = encounter ? (JObject)reply["state"]! : (JObject)reply["state"]!["battle"]!;
            foreach (string field in new[] { "source", "supportSourceId", "weapon", "stats", "rng" })
            {
                state["support"]![field] = new JObject();
                Assert.Throws<PracticeProtocolException>(() => { if (encounter) PracticeEncounterSnapshot.Parse(Utf8(reply), Account); else PracticeRunView.Parse(Utf8(reply), RunA); }, field);
                ((JObject)state["support"]!).Remove(field);
            }
            reply["events"]![0]!["e"] = "ult";
            Assert.Throws<PracticeProtocolException>(() => { if (encounter) PracticeEncounterSnapshot.Parse(Utf8(reply), Account); else PracticeRunView.Parse(Utf8(reply), RunA); });
            reply["events"]![0]!["e"] = "support"; ((JObject)reply["events"]![0]!).Remove("caller");
            Assert.Throws<PracticeProtocolException>(() => { if (encounter) PracticeEncounterSnapshot.Parse(Utf8(reply), Account); else PracticeRunView.Parse(Utf8(reply), RunA); });
            state["support"] = JValue.CreateNull();
            Assert.Throws<PracticeProtocolException>(() => { if (encounter) PracticeEncounterSnapshot.Parse(Utf8(reply), Account); else PracticeRunView.Parse(Utf8(reply), RunA); });
        }
        [TestCase("uid")]
        [TestCase("mode")]
        [TestCase("charId")]
        [TestCase("stats")]
        [TestCase("gauge")]
        [TestCase("supportSourceId")]
        [TestCase("credential")]
        public void SupportForgedActionFieldsNeverStageOrReachEitherCommandPipeline(string field)
        {
            var args = new JObject { ["caller"] = "sera", [field] = "forged" };
            foreach (string route in new[] { PracticeCommandOutbox.CommandRoute(RunA), PracticeCommandOutbox.EncounterCommandRoute(GrowthEncounter) })
            {
                var body = new JObject { ["requestId"] = "forged-support-proof", ["expectedVersion"] = 3, ["kind"] = "support", ["args"] = args };
                Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(route, body.ToString(Newtonsoft.Json.Formatting.None)));
            }
            SupportFixture(false); using (var run = Session()) { Refreshed(run); Assert.That(Wait(run.CommandAsync(run.State.Snapshot!.Context, "support", args)).Status, Is.EqualTo(MutationStatus.NotSent)); }
            SupportFixture(true); using (var battle = Session()) { Wait(battle.RefreshEncounterAsync(GrowthEncounter)); Assert.That(Wait(battle.EncounterCommandAsync(battle.Encounter!.Context, "support", args)).Status, Is.EqualTo(MutationStatus.NotSent)); }
            Assert.That(host.Posts, Is.Empty); Assert.That(Outbox().Load(), Is.Null);
        }
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void SupportQueriesRemainPureAndBoundToFrozenSupportInsteadOfNextSelection(bool encounter, bool hidden)
        {
            SupportFixture(encounter); progressionProfile["supportSelection"] = "rien";
            var pending = Outbox().Stage(PracticeCommandOutbox.CharacterLevelUpRoute("nia"), "{\"requestId\":\"unresolved-before-support-query\",\"expectedRevision\":3,\"targetLevel\":2}");
            byte[] bytes = File.ReadAllBytes(RecordPath); post = seen =>
            {
                Assert.That(seen.Path, Is.EqualTo(encounter ? EncountersPath + "/" + GrowthEncounter + "/support-query" : "/dev/practice/runs/" + RunA + "/support-query"));
                var body = JObject.Parse(seen.Text); Assert.That(body.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "expectedVersion", "caller", "target", "picks" }));
                Assert.That(body.Property("requestId"), Is.Null); Assert.That(body.Property("revision"), Is.Null);
                return Reply.Json(200, SupportQueryJson(encounter, target: "e1", picks: new[] { "r1" }, hidden: hidden));
            };
            using (var session = Session())
            {
                SupportQueryResult answer;
                if (encounter) { Wait(session.RefreshEncounterAsync(GrowthEncounter)); answer = Wait(session.EncounterSupportQueryAsync(session.Encounter!.Context, "sera", "e1", new[] { "r1" })); }
                else { var shown = Refreshed(session); answer = Wait(session.SupportQueryAsync(shown.Context, "sera", "e1", new[] { "r1" })); }
                Assert.That(answer.Code, Is.EqualTo(Codes.Ok)); Assert.That(answer.View!.Source.CharId, Is.EqualTo("nia")); Assert.That(answer.View.Source.Caller, Is.EqualTo("sera"));
                Assert.That(answer.View.Source.GaugeCost, Is.EqualTo(100)); Assert.That(answer.View.CanAct, Is.True); Assert.That(session.IsCurrent(answer), Is.True);
                Assert.That(answer.View.ForecastHidden, Is.EqualTo(hidden)); Assert.That(answer.View.EnemyHpLosses.Count, Is.EqualTo(hidden ? 0 : 1));
                Assert.That(answer.RunContext != null, Is.EqualTo(!encounter)); Assert.That(answer.EncounterContext != null, Is.EqualTo(encounter));
                Assert.That(answer.View.ToJObject().Property("revision"), Is.Null); Assert.That(((JObject)answer.View.ToJObject()["source"]!).Property("uid"), Is.Null);
                Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(bytes)); Assert.That(Outbox().Load()!.Body, Is.EqualTo(pending.Body));
            }
            Assert.That(host.Posts.Length, Is.EqualTo(1), "only the explicitly requested pure query was sent");
        }
        [TestCase("caller")]
        [TestCase("target")]
        [TestCase("picks")]
        [TestCase("charId")]
        [TestCase("version")]
        [TestCase("battleVersion")]
        [TestCase("node")]
        [TestCase("revision")]
        [TestCase("gaugeCost")]
        [TestCase("sourceUid")]
        [TestCase("privateSource")]
        public void SupportQueryRejectsAnotherRequestContextOrPrivateSource(string tamper)
        {
            SupportFixture(true); using (var session = Session())
            {
                Wait(session.RefreshEncounterAsync(GrowthEncounter)); var context = session.Encounter!.Context;
                post = _ =>
                {
                    var reply = SupportQueryJson(true, target: "e1", picks: new[] { "r1", "h1" });
                    if (tamper == "caller") reply["source"]!["caller"] = "harin";
                    if (tamper == "target") reply["source"]!["target"] = "e2";
                    if (tamper == "picks") reply["source"]!["picks"] = new JArray("h1", "r1");
                    if (tamper == "charId") reply["source"]!["charId"] = "rien";
                    if (tamper == "version") reply["version"] = 4;
                    if (tamper == "battleVersion") reply["context"]!["battleVersion"] = 8;
                    if (tamper == "node") reply["context"]!["node"] = "fake-node";
                    if (tamper == "revision") reply["revision"] = 3L;
                    if (tamper == "gaugeCost") reply["source"]!["gaugeCost"] = 99;
                    if (tamper == "sourceUid") reply["source"]!["uid"] = "fake-hand-card";
                    if (tamper == "privateSource") reply["source"]!["supportSource"] = new JObject { ["power4"] = 9999 };
                    return Reply.Json(200, reply);
                };
                var result = Wait(session.EncounterSupportQueryAsync(context, "sera", "e1", new[] { "r1", "h1" }));
                Assert.That(result.View, Is.Null); Assert.That(session.IsCurrent(result), Is.False); Assert.That(Outbox().Load(), Is.Null);
            }
        }
        [TestCase("enemyHpLoss")]
        [TestCase("enemyDamage")]
        [TestCase("newlyBroken")]
        [TestCase("blockDelta")]
        [TestCase("hpDelta")]
        [TestCase("defeat")]
        [TestCase("enemyHpLosses")]
        public void SupportHiddenForecastRefusesEveryLeakedOutcomeNumber(string field)
        {
            SupportFixture(false); using (var session = Session())
            {
                var context = Refreshed(session).Context; post = _ =>
                {
                    var reply = SupportQueryJson(false, hidden: true); reply["forecast"]![field] = field == "enemyHpLosses" ? new JArray(new JObject { ["uid"] = "e1", ["hpLoss"] = 7 }) : field == "defeat" ? (JToken)false : 0;
                    return Reply.Json(200, reply);
                };
                Assert.That(Wait(session.SupportQueryAsync(context, "sera")).View, Is.Null); Assert.That(Outbox().Load(), Is.Null);
            }
        }
        [Test]
        public void SupportIncompleteChoicesAndUnreadyCallerNeverBecomeAnActableView()
        {
            SupportFixture(false); using (var session = Session())
            {
                var context = Refreshed(session).Context; var reply = SupportQueryJson(false);
                reply["source"]!["ready"] = false; reply["source"]!["code"] = "ULT_NOT_READY"; reply["forecast"]!["error"] = "ULT_NOT_READY"; reply["forecast"]!["cost"] = 0;
                reply["forecast"]!["enemyHpLosses"] = new JArray(); post = _ => Reply.Json(200, reply);
                var result = Wait(session.SupportQueryAsync(context, "sera")); Assert.That(result.Code, Is.EqualTo(Codes.Ok)); Assert.That(result.View!.CanAct, Is.False);
                reply = SupportQueryJson(false); reply["choices"]!["requests"] = new JArray(new JObject { ["label"] = "예약 선택", ["from"] = "res", ["count"] = 1, ["growthOnly"] = false, ["op"] = "pull", ["part"] = JValue.CreateNull(), ["leading"] = true });
                reply["choices"]!["left"] = 1;
                result = Wait(session.SupportQueryAsync(context, "sera")); Assert.That(result.View!.CanAct, Is.False); Assert.That(result.View.ChoicesCount, Is.EqualTo(1));
                reply["choices"]!["index"] = 1; reply["choices"]!["left"] = 0;
                result = Wait(session.SupportQueryAsync(context, "sera")); Assert.That(result.View!.CanAct, Is.True); Assert.That(Outbox().Load(), Is.Null);
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void SupportStaleQueryResponseCannotRemainCurrentAfterTheBattleContextAdvances(bool encounter)
        {
            SupportFixture(encounter); using (var session = Session())
            {
                post = _ => Reply.Json(200, SupportQueryJson(encounter)); SupportQueryResult first;
                if (encounter) { Wait(session.RefreshEncounterAsync(GrowthEncounter)); first = Wait(session.EncounterSupportQueryAsync(session.Encounter!.Context, "sera")); }
                else first = Wait(session.SupportQueryAsync(Refreshed(session).Context, "sera"));
                Assert.That(session.IsCurrent(first), Is.True);
                if (encounter)
                {
                    progressionProfile["revision"] = 4L; growthEncounters[GrowthEncounter]["version"] = 4; growthEncounters[GrowthEncounter]["revision"] = 4L;
                    Wait(session.RefreshEncounterAsync(GrowthEncounter));
                }
                else { world.Version = 4; world.Extra!["battle"]!["v"] = 8; Wait(session.RefreshAsync()); }
                Assert.That(session.IsCurrent(first), Is.False); Assert.That(host.Posts.Length, Is.EqualTo(1));
            }
        }
        [TestCase("drop")]
        [TestCase("truncate")]
        public void SupportSelectionLostReplyIsExactlyRetriedAfterViewDisposalAndNeverChangesTheFrozenBattle(string failure)
        {
            SupportFixture(false); string frozen = world.Extra!.ToString(Newtonsoft.Json.Formatting.None); byte[]? receipt = null; int writes = 0;
            post = seen =>
            {
                var body = JObject.Parse(seen.Text); Assert.That(body.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "requestId", "expectedRevision", "charId" }));
                Assert.That(Outbox().Load()!.Body, Is.EqualTo(seen.Text));
                if (receipt == null)
                {
                    writes++; var committed = SupportSelectionReply(progressionProfile, "nia", "rien", body["requestId"]!.Value<string>()!); receipt = Utf8(committed); progressionProfile = (JObject)committed["profile"]!.DeepClone();
                    return failure == "drop" ? new Reply(200, receipt) { Drop = true } : new Reply(200, receipt.Take(receipt.Length / 2).ToArray());
                }
                return new Reply(200, receipt);
            };
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                using (var first = owner.CreateSession()) { Wait(first.RefreshProgressionAsync()); Assert.That(Wait(first.SelectSupportAsync(3, "rien")).Status, Is.EqualTo(MutationStatus.Pending)); }
                using (var second = owner.CreateSession())
                {
                    Wait(second.RefreshProgressionAsync()); Assert.That(host.Posts.Length, Is.EqualTo(1));
                    var result = Wait(second.RetrySupportPendingAsync()); Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.RefreshCode, Is.Null);
                    Assert.That(result.Receipt!.BeforeCharId, Is.EqualTo("nia")); Assert.That(result.Receipt.AfterCharId, Is.EqualTo("rien")); Assert.That(result.Progression!.SupportSelection, Is.EqualTo("rien"));
                    Assert.That(Refreshed(second).Run!.Support!.CharId, Is.EqualTo("nia"));
                }
                Assert.That(owner.IsAuthenticated, Is.True);
            }
            Assert.That(writes, Is.EqualTo(1)); Assert.That(host.Posts.Select(s => s.Text).Distinct().Count(), Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
            Assert.That(world.Extra!.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo(frozen));
        }
        [TestCase(422, "ALREADY_SELECTED", false)]
        [TestCase(422, "NOT_OWNED", false)]
        [TestCase(422, "NO_ELIGIBLE_WEAPON", false)]
        [TestCase(422, "TUTORIAL_REQUIRED", false)]
        [TestCase(409, "STALE_REVISION", false)]
        [TestCase(409, "IDEMPOTENCY_CONFLICT", true)]
        [TestCase(422, "SUPPORT_IN_PARTY", true)]
        [TestCase(422, "UNKNOWN_FUTURE_CODE", true)]
        [TestCase(400, "INVALID_REQUEST", true)]
        [TestCase(404, "NOT_FOUND", true)]
        public void SupportSelectionRefusalsUseOnlyTheirOwnNarrowFreshRevisionTable(int status, string code, bool pending)
        {
            SupportFixture(false); post = _ => Error(status, code); using (var session = Session())
            {
                Wait(session.RefreshProgressionAsync()); var result = Wait(session.SelectSupportAsync(3, "rien"));
                Assert.That(result.Status, Is.EqualTo(pending ? MutationStatus.Pending : MutationStatus.NotCommitted)); Assert.That(result.Code, Is.EqualTo(code)); Assert.That(Outbox().Load() != null, Is.EqualTo(pending));
            }
        }
        [TestCase(false, "drop")]
        [TestCase(false, "truncate")]
        [TestCase(true, "drop")]
        [TestCase(true, "truncate")]
        public void SupportLethalReplyRetryReusesOneRequestAndNeverReplaysSupportOrSettlement(bool encounter, string failure)
        {
            SupportFixture(encounter); byte[]? receipt = null; int writes = 0;
            post = seen =>
            {
                Assert.That(JObject.Parse(seen.Text)["kind"]!.Value<string>(), Is.EqualTo("support"));
                if (receipt == null)
                {
                    writes++; var events = new JArray(new JObject { ["e"] = "support", ["actor"] = "nia", ["caller"] = "sera" }); JObject result;
                    if (encounter)
                    {
                        result = GrowthEncounterJson(4, 4, "VICTORY", events: true); result["events"] = events; result["state"]!["support"] = SupportSummaryJson(true);
                        growthEncounters[GrowthEncounter] = result; growthActive = null; progressionProfile["revision"] = 4L;
                    }
                    else
                    {
                        world.Version = 4; world.Phase = "REWARD"; world.InBattle = false; world.Extra = null;
                        result = RunJson(world, 4, events);
                    }
                    receipt = Utf8(result); return failure == "drop" ? new Reply(200, receipt) { Drop = true } : new Reply(200, receipt.Take(receipt.Length / 2).ToArray());
                }
                return new Reply(200, receipt);
            };
            using (var session = Session())
            {
                if (encounter) { Wait(session.RefreshEncounterAsync(GrowthEncounter)); Assert.That(Wait(session.EncounterCommandAsync(session.Encounter!.Context, "support", new JObject { ["caller"] = "sera" })).Status, Is.EqualTo(MutationStatus.Pending)); }
                else Assert.That(Wait(session.CommandAsync(Refreshed(session).Context, "support", new JObject { ["caller"] = "sera" })).Status, Is.EqualTo(MutationStatus.Pending));
            }
            using (var retry = Session())
            {
                if (encounter)
                {
                    var result = Wait(retry.RetryGrowthPendingAsync()); Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.RefreshCode, Is.Null); Assert.That(result.PresentableEvents(), Is.Null); Assert.That(retry.CanMutateEncounter, Is.False);
                }
                else
                {
                    var result = Wait(retry.RetryPendingAsync()); Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.RefreshCode, Is.Null); Assert.That(result.PresentableEvents(), Is.Null);
                }
            }
            Assert.That(writes, Is.EqualTo(1)); Assert.That(host.Posts.Select(s => s.Text).Distinct().Count(), Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
        }
        [TestCase(false, "SUPPORT_ALREADY_USED", false)]
        [TestCase(false, "ULT_NOT_READY", false)]
        [TestCase(false, "BAD_TARGET", false)]
        [TestCase(false, "PICK_INVALID", false)]
        [TestCase(false, "UNKNOWN_FUTURE_CODE", true)]
        [TestCase(true, "SUPPORT_NOT_SELECTED", false)]
        [TestCase(true, "UNKNOWN_ACTOR", false)]
        [TestCase(true, "PULL_DESTINATION_FULL", false)]
        [TestCase(true, "BATTLE_NUMBER_OUT_OF_RANGE", false)]
        [TestCase(true, "UNKNOWN_FUTURE_CODE", true)]
        public void SupportCommandNoCommitProofIsExactAndNeverBroadensToArbitrary422(bool encounter, string code, bool pending)
        {
            SupportFixture(encounter); post = _ => Error(422, code); using (var session = Session())
            {
                if (encounter)
                {
                    Wait(session.RefreshEncounterAsync(GrowthEncounter)); var result = Wait(session.EncounterCommandAsync(session.Encounter!.Context, "support", new JObject { ["caller"] = "sera" }));
                    Assert.That(result.Status, Is.EqualTo(pending ? MutationStatus.Pending : MutationStatus.NotCommitted)); Assert.That(result.Code, Is.EqualTo(code));
                }
                else
                {
                    var result = Wait(session.CommandAsync(Refreshed(session).Context, "support", new JObject { ["caller"] = "sera" }));
                    Assert.That(result.Status, Is.EqualTo(pending ? MutationStatus.Pending : MutationStatus.NotCommitted)); Assert.That(result.Code, Is.EqualTo(code));
                }
                Assert.That(Outbox().Load() != null, Is.EqualTo(pending));
            }
        }
        [TestCase("seed")]
        [TestCase("stats")]
        [TestCase("caller")]
        [TestCase("expectedVersion")]
        [TestCase("credential")]
        public void SupportSelectionNeverStoresPostedBuildsClocksOrCredentials(string field)
        {
            var body = new JObject { ["requestId"] = "support-forged-selection", ["expectedRevision"] = 3, ["charId"] = "nia", [field] = 1 };
            Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(SupportSelectionPath, body.ToString(Newtonsoft.Json.Formatting.None)));
            Assert.That(Outbox().Load(), Is.Null);
        }
        [TestCase("target")]
        [TestCase("requestId")]
        [TestCase("revision")]
        [TestCase("cost")]
        [TestCase("sameSelection")]
        [TestCase("profile")]
        public void SupportMalformedSelectionReceiptNeverResolvesThePendingIntent(string tamper)
        {
            SupportFixture(false); post = seen =>
            {
                var reply = SupportSelectionReply(progressionProfile, "nia", "rien", JObject.Parse(seen.Text)["requestId"]!.Value<string>()!);
                if (tamper == "target") reply["target"]!["charId"] = "nia";
                if (tamper == "requestId") reply["requestId"] = "different-selection-request";
                if (tamper == "revision") reply["revision"] = 9L;
                if (tamper == "cost") reply["cost"]!["credits"] = 1;
                if (tamper == "sameSelection") reply["before"]!["charId"] = "rien";
                if (tamper == "profile") reply["profile"]!["supportSelection"] = "nia";
                return Reply.Json(200, reply);
            };
            using (var session = Session())
            {
                Wait(session.RefreshProgressionAsync()); var result = Wait(session.SelectSupportAsync(3, "rien"));
                Assert.That(result.Status, Is.EqualTo(MutationStatus.Pending)); Assert.That(result.Receipt, Is.Null); Assert.That(Outbox().Load(), Is.Not.Null);
            }
        }
        [Test]
        public void SupportSelectionRetryNeverReplacesANewerCurrentChoiceWithTheHistoricalReceipt()
        {
            SupportFixture(false); byte[]? receipt = null; post = seen =>
            {
                if (receipt == null)
                {
                    var reply = SupportSelectionReply(progressionProfile, "nia", "rien", JObject.Parse(seen.Text)["requestId"]!.Value<string>()!); receipt = Utf8(reply);
                    progressionProfile = SupportProfileJson(5, "harin"); return new Reply(200, receipt) { Drop = true };
                }
                return new Reply(200, receipt);
            };
            using (var first = Session()) { Wait(first.RefreshProgressionAsync()); Wait(first.SelectSupportAsync(3, "rien")); }
            using (var second = Session())
            {
                var result = Wait(second.RetrySupportPendingAsync()); Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.RefreshCode, Is.Null);
                Assert.That(result.Receipt!.AfterCharId, Is.EqualTo("rien")); Assert.That(result.Progression!.SupportSelection, Is.EqualTo("harin")); Assert.That(result.Progression.Revision, Is.EqualTo(5));
            }
            Assert.That(host.Posts.Length, Is.EqualTo(2)); Assert.That(host.Posts[0].Text, Is.EqualTo(host.Posts[1].Text));
        }
        [Test]
        public void SupportSelectionForeignOrOlderProfileDoesNotProveAnyRefusal()
        {
            SupportFixture(false); var pending = Outbox().Stage(SupportSelectionPath, "{\"requestId\":\"support-proof-selection\",\"expectedRevision\":4,\"charId\":null}");
            post = _ => Error(422, "ALREADY_SELECTED");
            using (var session = Session()) Assert.That(Wait(session.RetrySupportPendingAsync()).Status, Is.EqualTo(MutationStatus.Pending));
            Assert.That(Outbox().Load()!.Body, Is.EqualTo(pending.Body));
            progressionProfile["accountId"] = OtherAccount; int before = host.Posts.Length;
            using (var session = Session()) Assert.That(Wait(session.RetrySupportPendingAsync()).Status, Is.EqualTo(MutationStatus.Pending));
            Assert.That(host.Posts.Length, Is.EqualTo(before)); Assert.That(Outbox().Load()!.Body, Is.EqualTo(pending.Body));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void SupportCommandWrongSessionOrOlderVersionCannotClearTheExactPendingBody(bool encounter)
        {
            SupportFixture(encounter); string route = encounter ? PracticeCommandOutbox.EncounterCommandRoute(GrowthEncounter) : PracticeCommandOutbox.CommandRoute(RunA);
            var pending = Outbox().Stage(route, "{\"requestId\":\"support-proof-command\",\"expectedVersion\":3,\"kind\":\"support\",\"args\":{\"caller\":\"sera\"}}");
            post = _ => Error(422, "BAD_TARGET");
            if (encounter) growthEncounters[GrowthEncounter]["version"] = 2; else world.RunId = RunB;
            using (var session = Session())
            {
                var result = encounter ? Wait(session.RetryGrowthPendingAsync()).Status : Wait(session.RetryPendingAsync()).Status;
                Assert.That(result, Is.EqualTo(MutationStatus.Pending)); Assert.That(Outbox().Load()!.Body, Is.EqualTo(pending.Body));
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void SupportQueryClonesOrderedPicksAndKeepsOneCapturedCredential(bool encounter)
        {
            SupportFixture(encounter); var picks = new List<string> { "r1", "h1" }; string supplied = Credential; int captures = 0; source = () => { captures++; return supplied; };
            post = seen =>
            {
                Assert.That(seen.Authorization, Is.EqualTo("Bearer " + Credential)); Assert.That(JObject.Parse(seen.Text)["picks"]!.Values<string>(), Is.EqualTo(new[] { "r1", "h1" }));
                return Reply.Json(200, SupportQueryJson(encounter, picks: new[] { "r1", "h1" }));
            };
            using (var session = Session())
            {
                if (encounter) Wait(session.RefreshEncounterAsync(GrowthEncounter)); else Refreshed(session);
                captures = 0; var original = additionalReply; additionalReply = seen => { supplied = RotatedCredential; picks.Reverse(); return original!(seen); };
                var result = encounter ? Wait(session.EncounterSupportQueryAsync(session.Encounter!.Context, "sera", picks: picks)) : Wait(session.SupportQueryAsync(session.State.Snapshot!.Context, "sera", picks: picks));
                Assert.That(result.Code, Is.EqualTo(Codes.Ok)); Assert.That(captures, Is.EqualTo(1)); Assert.That(result.View!.Source.Picks, Is.EqualTo(new[] { "r1", "h1" }));
            }
            Assert.That(Outbox().Load(), Is.Null);
        }
        [Test]
        public void SupportV4EquipmentReceiptsAndOlderNoSupportProjectorsStayCompatible()
        {
            var pending = Outbox().Stage(PracticeCommandOutbox.EquipmentActionRoute("weaponRefine", EquipmentWeapon), "{\"requestId\":\"equipment-after-support\",\"expectedRevision\":3}");
            var old = EquipmentChange(EquipmentProfileJson(), "weaponRefine", false, pending.RequestId);
            Assert.That(PracticeEquipmentReceipt.Parse(pending, Utf8(old)).Profile.HasSupportSelection, Is.False);
            var current = EquipmentChange(SupportProfileJson(), "weaponRefine", false, pending.RequestId);
            Assert.That(PracticeEquipmentReceipt.Parse(pending, Utf8(current)).Profile.SupportSelection, Is.EqualTo("nia"));
            Outbox().Confirm(pending);
            var run = RunJson(world, 3); var parsed = PracticeRunView.Parse(Utf8(run), RunA);
            Assert.That(parsed.Support, Is.Null); Assert.That(JToken.DeepEquals(parsed.ToJObject(), run), Is.True);
            var battle = GrowthEncounterJson(); var isolated = PracticeEncounterSnapshot.Parse(Utf8(battle), Account);
            Assert.That(isolated.Support, Is.Null); Assert.That(JToken.DeepEquals(isolated.ToJObject(), battle), Is.True);
        }
        [TestCase(false, "caller")]
        [TestCase(false, "actor")]
        [TestCase(false, "duplicate")]
        [TestCase(true, "caller")]
        [TestCase(true, "actor")]
        [TestCase(true, "used")]
        [TestCase(true, "missing")]
        public void SupportCommittedAttributionAndUseMustMatchTheExactSupportCommand(bool encounter, string tamper)
        {
            SupportFixture(encounter); post = _ =>
            {
                var events = new JArray(new JObject { ["e"] = "support", ["actor"] = tamper == "actor" ? "rien" : "nia", ["caller"] = tamper == "caller" ? "harin" : "sera" });
                if (tamper == "duplicate") events.Add(events[0].DeepClone()); if (tamper == "missing") events.Clear();
                if (encounter)
                {
                    var result = GrowthEncounterJson(4, 4, "PLAYER", events: true); result["events"] = events; result["state"]!["support"] = SupportSummaryJson(tamper != "used"); return Reply.Json(200, result);
                }
                world.Version = 4; var reply = RunJson(world, 4, events); reply["state"]!["battle"]!["support"] = SupportSummaryJson(true); return Reply.Json(200, reply);
            };
            using (var session = Session())
            {
                MutationStatus status;
                if (encounter) { Wait(session.RefreshEncounterAsync(GrowthEncounter)); status = Wait(session.EncounterCommandAsync(session.Encounter!.Context, "support", new JObject { ["caller"] = "sera" })).Status; }
                else status = Wait(session.CommandAsync(Refreshed(session).Context, "support", new JObject { ["caller"] = "sera" })).Status;
                Assert.That(status, Is.EqualTo(MutationStatus.Pending)); Assert.That(Outbox().Load(), Is.Not.Null);
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void SupportQueryCannotAdvertiseAUsedFlagDifferentFromTheShownBattle(bool encounter)
        {
            SupportFixture(encounter); post = _ =>
            {
                var query = SupportQueryJson(encounter); query["source"]!["used"] = true; query["source"]!["ready"] = false; query["source"]!["code"] = "SUPPORT_ALREADY_USED";
                return Reply.Json(200, query);
            };
            using (var session = Session())
            {
                SupportQueryResult result;
                if (encounter) { Wait(session.RefreshEncounterAsync(GrowthEncounter)); result = Wait(session.EncounterSupportQueryAsync(session.Encounter!.Context, "sera")); }
                else result = Wait(session.SupportQueryAsync(Refreshed(session).Context, "sera"));
                Assert.That(result.View, Is.Null); Assert.That(result.Code, Is.EqualTo(PracticeProtocolException.Mismatch)); Assert.That(session.IsCurrent(result), Is.False);
            }
        }
        [TestCase("story")]
        [TestCase("dungeon")]
        [TestCase("weekly")]
        public void SupportInPartyLaunchRefusalIsOnlyDefinitiveForFreshV4Profiles(string kind)
        {
            SupportFixture(false); post = _ => Error(422, "SUPPORT_IN_PARTY");
            using (var session = Session())
            {
                Wait(session.RefreshProgressionAsync()); MutationStatus status;
                if (kind == "story") status = Wait(session.LaunchExpeditionAsync(3, "story", 1, new[] { "nia", "harin", "rien" })).Status;
                else if (kind == "dungeon") status = Wait(session.StartEncounterAsync(3, "dg_ink", 1, new[] { "nia", "harin", "rien" })).Status;
                else status = Wait(session.StartWeeklyBossAsync(3, "b_bell", 1, true, new[] { "nia", "harin", "rien" })).Status;
                Assert.That(status, Is.EqualTo(MutationStatus.NotCommitted)); Assert.That(Outbox().Load(), Is.Null);
            }
        }
        [TestCase("same")]
        [TestCase("older")]
        [TestCase("different")]
        [TestCase("unknown")]
        [TestCase("encounter")]
        public void SupportNoBattleIsDefinitiveOnlyForTheFreshExactRunCommand(string proof)
        {
            bool encounter = proof == "encounter"; SupportFixture(encounter);
            if (!encounter) { world.InBattle = false; world.Phase = "MAP"; world.Extra = null; }
            post = _ => Error(422, proof == "unknown" ? "UNRECOGNIZED_SUPPORT_ERROR" : "NO_BATTLE");
            int memory = world.MemoryVersion;
            using (var session = Session())
            {
                if (proof == "same")
                {
                    var shown = Refreshed(session); Assert.That(shown.Run!.Phase, Is.EqualTo("MAP"));
                    var result = Wait(session.CommandAsync(shown.Context, "support", new JObject { ["caller"] = "sera" }));
                    Assert.That(result.Status, Is.EqualTo(MutationStatus.NotCommitted)); Assert.That(result.Code, Is.EqualTo("NO_BATTLE"));
                    Assert.That(result.Request!.Route, Is.EqualTo(PracticeCommandOutbox.CommandRoute(RunA))); Assert.That(Outbox().Load(), Is.Null);
                }
                else
                {
                    string route = encounter ? PracticeCommandOutbox.EncounterCommandRoute(GrowthEncounter) : PracticeCommandOutbox.CommandRoute(RunA);
                    var pending = Outbox().Stage(route, "{\"requestId\":\"support-no-battle-proof\",\"expectedVersion\":3,\"kind\":\"support\",\"args\":{\"caller\":\"sera\"}}");
                    byte[] bytes = File.ReadAllBytes(RecordPath);
                    if (proof == "older") world.Version = 2;
                    if (proof == "different") world.RunId = RunB;
                    var status = encounter ? Wait(session.RetryGrowthPendingAsync()).Status : Wait(session.RetryPendingAsync()).Status;
                    Assert.That(status, Is.EqualTo(MutationStatus.Pending)); Assert.That(Outbox().Load()!.Body, Is.EqualTo(pending.Body)); Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(bytes));
                }
                Assert.That(host.Posts.Length, Is.EqualTo(1)); Assert.That(world.MemoryVersion, Is.EqualTo(memory)); Assert.That(world.Version, Is.EqualTo(proof == "older" ? 2 : 3));
            }
        }
        [TestCase("object")]
        [TestCase("array")]
        public void SupportMalformedOtherEventKeepsThe200ReceiptUnresolvedInsteadOfFaulting(string malformed)
        {
            SupportFixture(false); byte[]? pendingBytes = null;
            post = _ =>
            {
                pendingBytes = File.ReadAllBytes(RecordPath);
                var events = new JArray(new JObject { ["e"] = "support", ["actor"] = "nia", ["caller"] = "sera" },
                    new JObject { ["e"] = malformed == "object" ? (JToken)new JObject { ["unexpected"] = "event" } : new JArray("unexpected") });
                var reply = RunJson(world, 4, events); reply["state"]!["battle"]!["support"] = SupportSummaryJson(true); return Reply.Json(200, reply);
            };
            using (var session = Session())
            {
                var shown = Refreshed(session); var task = session.CommandAsync(shown.Context, "support", new JObject { ["caller"] = "sera" }); PracticeMutationResult? result = null;
                Assert.DoesNotThrow(() => result = Wait(task)); Assert.That(task.IsFaulted, Is.False);
                Assert.That(result!.Status, Is.EqualTo(MutationStatus.Pending)); Assert.That(result.Code, Is.EqualTo(PracticeProtocolException.Invalid)); Assert.That(result.Receipt, Is.Null);
                Assert.That(Outbox().Load(), Is.Not.Null); Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(pendingBytes));
            }
        }
        [Test]
        public void SupportEncounterReplyCannotReplaceTheKnownPriorFrozenSupportEvenWhenInternallyConsistent()
        {
            SupportFixture(true); byte[]? pendingBytes = null;
            post = _ =>
            {
                pendingBytes = File.ReadAllBytes(RecordPath);
                var reply = GrowthEncounterJson(4, 4, "PLAYER", events: true); reply["state"]!["support"] = new JObject { ["charId"] = "rien", ["used"] = true, ["gaugeCost"] = 100 };
                reply["events"] = new JArray(new JObject { ["e"] = "support", ["actor"] = "rien", ["caller"] = "sera" });
                // It satisfies the receipt's own public summary/event checks, but is not this encounter's frozen source.
                growthEncounters[GrowthEncounter] = reply; progressionProfile["revision"] = 4L; return Reply.Json(200, reply);
            };
            using (var session = Session())
            {
                Assert.That(Wait(session.RefreshEncounterAsync(GrowthEncounter)).Code, Is.EqualTo(Codes.Ok)); Assert.That(session.Encounter!.Support!.CharId, Is.EqualTo("nia"));
                var result = Wait(session.EncounterCommandAsync(session.Encounter.Context, "support", new JObject { ["caller"] = "sera" }));
                Assert.That(result.Status, Is.EqualTo(MutationStatus.Pending)); Assert.That(result.Code, Is.EqualTo(PracticeProtocolException.Mismatch)); Assert.That(result.Receipt, Is.Null);
                Assert.That(session.Encounter.Support.CharId, Is.EqualTo("nia")); Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(pendingBytes));
            }
        }
    }
}
