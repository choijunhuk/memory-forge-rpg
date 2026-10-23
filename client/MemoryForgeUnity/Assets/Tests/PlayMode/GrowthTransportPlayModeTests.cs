#nullable enable
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Codes = MemoryForge.Client.PracticeSessionCodes;
using MutationStatus = MemoryForge.Client.PracticeMutationStatus;

namespace MemoryForge.Client.Tests
{
    public partial class PracticeServerSessionPlayModeTests
    {
        const string EncountersPath = "/dev/practice/encounters", GrowthEncounter = "ec-0123456789abcdef0123456789abcdef";
        const string OtherGrowthEncounter = "ec-fedcba9876543210fedcba9876543210";
        readonly Dictionary<string, JObject> growthEncounters = new Dictionary<string, JObject>(StringComparer.Ordinal);
        string? growthActive;

        static JObject GrowthResourcesJson(long ink = 0, long credits = 0, long oil = 0) => new JObject
        {
            ["credits"] = credits, ["ink"] = ink, ["oil"] = oil, ["seal"] = 0L, ["alloy"] = 0L, ["book"] = 0L, ["polish"] = 0L, ["core"] = 0L, ["refine_core"] = 0L
        };
        static JObject GrowthGrantJson(long ink = 0, long accountXp = 0) => new JObject { ["resources"] = GrowthResourcesJson(ink), ["accountXp"] = accountXp };
        static JObject GrowthProfileJson(long revision = 3)
        {
            var profile = ProgressionProfileJson(revision);
            profile["resources"] = GrowthResourcesJson(6000, 10000, 4800);
            profile["resourceUnits"] = new JObject { ["ink"] = "XP", ["oil"] = "XP" };
            profile["focus"] = new JObject { ["value"] = 240, ["cap"] = 240, ["overflowCap"] = 999, ["regenSeconds"] = 360, ["nextRegenAtMs"] = JValue.CreateNull(), ["serverNowMs"] = 1000L };
            profile["dungeons"] = new JArray(new JObject { ["id"] = "dg_ink", ["unlockLevel"] = 5, ["cost"] = 20, ["available"] = true,
                ["tiers"] = new JArray(new JObject { ["tier"] = 1, ["stage"] = 10, ["percent"] = 25, ["reward"] = GrowthGrantJson(6000, 20), ["bestStars"] = 3, ["sweepAvailable"] = true }) });
            profile["activeEncounter"] = JValue.CreateNull(); return profile;
        }

        static JObject GrowthEncounterJson(int version = 3, long revision = 3, string phase = "PLAYER", string id = GrowthEncounter, bool events = false)
        {
            var battle = Battle(new World()); battle["phase"] = phase;
            foreach (string number in new[] { "block", "counter", "apNext", "apDrainNext", "silenceUntil", "weak", "echo", "freeNext", "played" }) battle[number] = 0;
            battle["echoMax"] = 3; battle["echoActive"] = false; battle["gaugeMax"] = 100;
            foreach (string list in new[] { "debts", "buffs", "powers" }) battle[list] = new JArray();
            foreach (string item in new[] { "restraint", "bonus", "growth", "slots" }) battle[item] = new JObject();
            battle["result"] = phase == "PLAYER" ? (JToken)JValue.CreateNull() : new JObject { ["phase"] = phase, ["turn"] = 2, ["hp"] = phase == "DEFEAT" ? 0 : 40 };
            var reply = new JObject { ["encounterId"] = id, ["version"] = version, ["phase"] = phase, ["revision"] = revision, ["state"] = battle };
            if (events) reply["events"] = Events();
            if (phase != "PLAYER") reply["settlement"] = new JObject { ["outcome"] = phase, ["stars"] = phase == "VICTORY" ? 3 : 0,
                ["cost"] = new JObject { ["focus"] = phase == "VICTORY" ? 20 : 0 }, ["grant"] = phase == "VICTORY" ? GrowthGrantJson(6000, 20) : GrowthGrantJson() };
            return reply;
        }

        void GrowthFixture(bool active = false)
        {
            ProgressionFixture(); progressionProfile = GrowthProfileJson(); growthEncounters.Clear(); growthActive = active ? GrowthEncounter : null;
            growthEncounters[GrowthEncounter] = GrowthEncounterJson();
            additionalReply = seen =>
            {
                if (seen.Method == "GET" && seen.Path == ProgressionPath) return Reply.Json(200, GrowthCurrentProfile());
                if (seen.Method == "GET" && seen.Path.StartsWith(EncountersPath + "/", StringComparison.Ordinal))
                    return growthEncounters.TryGetValue(seen.Path.Substring(EncountersPath.Length + 1), out var view) ? Reply.Json(200, view) : Error(404, "ENCOUNTER_NOT_FOUND");
                return null;
            };
        }

        JObject GrowthCurrentProfile()
        {
            var profile = (JObject)progressionProfile.DeepClone();
            profile["activeEncounter"] = growthActive == null ? (JToken)JValue.CreateNull() : new JObject { ["encounterId"] = growthActive, ["dungeonId"] = "dg_ink", ["tier"] = 1,
                ["phase"] = growthEncounters[growthActive]["phase"]!.DeepClone(), ["version"] = growthEncounters[growthActive]["version"]!.DeepClone() };
            return profile;
        }

        static JObject GrowthLevelJson(int level, long xp, int hp) => new JObject { ["level"] = level, ["xp"] = xp, ["cap"] = 20,
            ["stats"] = new JObject { ["power4"] = 84, ["power"] = 21, ["hp"] = hp, ["gearBps"] = 0, ["skillLv"] = 1, ["ultLv"] = 1, ["reference"] = false } };

        static JObject GrowthPreviewJson() => new JObject { ["revision"] = 3L, ["charId"] = "nia", ["before"] = GrowthLevelJson(1, 80, 61),
            ["after"] = GrowthLevelJson(2, 0, 62), ["cost"] = new JObject { ["ink"] = 120L, ["credits"] = 60L }, ["canCommit"] = true };

        [Test]
        public void GrowthRoutesShareTheExistingExactPendingSlotWithoutStagingPureQueries()
        {
            var mutations = new[]
            {
                (EncountersPath, "{\"requestId\":\"growth-start-proof\",\"expectedRevision\":3,\"dungeonId\":\"dg_ink\",\"tier\":1,\"party\":[\"nia\",\"harin\",\"rien\"]}"),
                (EncountersPath + "/" + GrowthEncounter + "/commands", "{\"requestId\":\"growth-command-proof\",\"expectedVersion\":0,\"kind\":\"end\",\"args\":{}}"),
                ("/dev/practice/dungeons/sweep", "{\"requestId\":\"growth-sweep-proof\",\"expectedRevision\":3,\"dungeonId\":\"dg_ink\",\"tier\":1,\"times\":2}"),
                ("/dev/practice/progression/characters/nia/level-up", "{\"requestId\":\"growth-level-proof\",\"expectedRevision\":3,\"targetLevel\":2}")
            };
            foreach (var mutation in mutations)
            {
                PendingPracticeCommand? staged = null;
                Assert.DoesNotThrow(() => staged = Outbox().Stage(mutation.Item1, mutation.Item2));
                Assert.That(staged!.Body, Is.EqualTo(mutation.Item2)); byte[] original = File.ReadAllBytes(RecordPath);
                Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(PracticeCommandOutbox.CreateRunRoute, "{\"requestId\":\"other-pending-proof\",\"useMemory\":true}"));
                Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(original)); Outbox().Confirm(staged);
            }
            Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(EncountersPath + "/" + GrowthEncounter + "/battle-query", "{\"expectedVersion\":0,\"uid\":\"c1\",\"mode\":\"now\"}"));
            Assert.Throws<PracticeOutboxException>(() => Outbox().Stage("/dev/practice/progression/characters/nia/level-preview", "{\"expectedRevision\":3,\"targetLevel\":2}"));
            Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(EncountersPath + "/" + GrowthEncounter + "/commands", "{\"requestId\":\"forged-settle-proof\",\"expectedVersion\":0,\"kind\":\"settle\",\"args\":{}}"));
            Assert.That(Outbox().Load(), Is.Null);
        }

        [Test]
        public void GrowthProfilePreservesS1AbsenceAndExactXpUnitsWithoutGrantingAnything()
        {
            var old = ProgressionProfileJson(); var legacy = PracticeProgressionSnapshot.Parse(Utf8(old), Account);
            Assert.That(legacy.Resources, Is.Null); Assert.That(legacy.Focus, Is.Null); Assert.That(legacy.Dungeons, Is.Null); Assert.That(legacy.ActiveEncounter, Is.Null);
            Assert.That(JToken.DeepEquals(legacy.ToJObject(), old), Is.True);
            var json = GrowthProfileJson(); var profile = PracticeProgressionSnapshot.Parse(Utf8(json), Account);
            Assert.That(profile.Resources!["oil"], Is.EqualTo(4800)); Assert.That(profile.Resources["ink"], Is.EqualTo(6000));
            Assert.That(profile.ResourceUnits!["oil"], Is.EqualTo("XP")); Assert.That(profile.Focus!.NextRegenAtMs, Is.Null);
            Assert.That(profile.Dungeons, Is.Not.Null, "the v2 wire supplies the dungeon catalogue");
            Assert.That(profile.Dungeons!.Single().Tiers.Single().Reward.Resources["ink"], Is.EqualTo(6000));
            Assert.That(profile.Dungeons!.Single().Tiers.Single().Reward.AccountXp, Is.EqualTo(20));
            json["activeSession"] = new JObject { ["runId"] = RunA, ["mode"] = "legacy", ["chapter"] = JValue.CreateNull(), ["phase"] = "MAP", ["version"] = 3L };
            Assert.That(PracticeProgressionSnapshot.Parse(Utf8(json), Account).ActiveSession!.RunId, Is.EqualTo(RunA));
            json["resources"]!["ink"] = long.MaxValue;
            Assert.That(PracticeProgressionSnapshot.Parse(Utf8(json), Account).Resources!["ink"], Is.EqualTo(long.MaxValue));
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void GrowthProfileAllowsCompletedStoryAlongsideSeparateEncounter(int profileVersion)
        {
            var json = profileVersion == 4 ? SupportProfileJson() : profileVersion == 3 ? EquipmentProfileJson() : GrowthProfileJson();
            json["activeSession"] = new JObject { ["runId"] = RunA, ["mode"] = "story", ["chapter"] = 1, ["phase"] = "DONE", ["version"] = 162L };
            json["activeEncounter"] = new JObject { ["encounterId"] = GrowthEncounter, ["dungeonId"] = "dg_ink", ["tier"] = 1, ["phase"] = "PLAYER", ["version"] = 0 };

            var profile = PracticeProgressionSnapshot.Parse(Utf8(json), Account);

            Assert.That(profile.ActiveSession!.RunId, Is.EqualTo(RunA));
            Assert.That(profile.ActiveSession.Phase, Is.EqualTo("DONE"));
            Assert.That(profile.ActiveSession.Version, Is.EqualTo(162));
            Assert.That(profile.ActiveEncounter!.EncounterId, Is.EqualTo(GrowthEncounter));
            Assert.That(profile.ActiveEncounter.Phase, Is.EqualTo("PLAYER"));
            Assert.That(profile.ActiveEncounter.Version, Is.Zero);
            Assert.That(JToken.DeepEquals(profile.ToJObject(), json), Is.True, "both independent public pointers must be preserved exactly");
        }

        [TestCase("MAP")]
        [TestCase("BATTLE")]
        [TestCase("SAVE")]
        [TestCase("UNKNOWN_PHASE")]
        public void GrowthProfileRejectsLiveOrUnknownRunAlongsideEncounter(string phase)
        {
            var json = SupportProfileJson();
            json["activeSession"] = new JObject { ["runId"] = RunA, ["mode"] = "story", ["chapter"] = 1, ["phase"] = phase, ["version"] = 162L };
            json["activeEncounter"] = new JObject { ["encounterId"] = GrowthEncounter, ["dungeonId"] = "dg_ink", ["tier"] = 1, ["phase"] = "PLAYER", ["version"] = 0 };

            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(json), Account));
        }

        [TestCase("resources.ink", -1L)]
        [TestCase("focus.value", 1000L)]
        [TestCase("focus.cap", 241L)]
        [TestCase("focus.regenSeconds", 0L)]
        [TestCase("focus.serverNowMs", -1L)]
        [TestCase("dungeons[0].tiers[0].bestStars", 4L)]
        public void GrowthNumbersAndPublicClockStayInsideTheirExplicitSchema(string path, long value)
        {
            var json = GrowthProfileJson(); json.SelectToken(path)!.Replace(value);
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(json), Account));
            var old = RunJson(new World(), 3); old["serverNowMs"] = 1000L;
            Assert.Throws<PracticeProtocolException>(() => PracticePublicJson.ParseObject(Utf8(old)), "clock exception is not global");
        }

        [Test]
        public void GrowthProfileRejectsPartialUnitsPrivateClockAndUnknownResources()
        {
            var json = GrowthProfileJson(); ((JObject)json["resources"]!).Remove("refine_core");
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(json), Account));
            json = GrowthProfileJson(); json["resourceUnits"]!["oil"] = "items";
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(json), Account));
            json = GrowthProfileJson(); json.Remove("focus");
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(json), Account));
            json = GrowthProfileJson(); json["focus"]!["regenAtMs"] = 0L;
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(json), Account));
            json = GrowthProfileJson(); json["resources"]!["coins"] = 999L;
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(json), Account));
        }

        [Test]
        public void StandaloneBattleHasNoRunAndSeparatesItsJournalAndCoreVersions()
        {
            var json = GrowthEncounterJson(); var snapshot = PracticeEncounterSnapshot.Parse(Utf8(json), Account, GrowthEncounter);
            Assert.That(snapshot.Version, Is.EqualTo(3)); Assert.That(snapshot.Context.BattleVersion, Is.EqualTo(7));
            Assert.That(snapshot.StateJObject().ContainsKey("battle"), Is.False); Assert.That(snapshot.StateJObject().ContainsKey("runId"), Is.False);
            var copy = snapshot.StateJObject(); copy["hp"] = 0; Assert.That(snapshot.StateJObject()["hp"]!.Value<int>(), Is.EqualTo(40));
            foreach (string hidden in new[] { "zones", "cards", "seed", "revision", "runId" })
            {
                var bad = (JObject)json.DeepClone(); bad["state"]![hidden] = new JObject();
                Assert.Throws<PracticeProtocolException>(() => PracticeEncounterSnapshot.Parse(Utf8(bad), Account), hidden);
            }
            var terminal = GrowthEncounterJson(4, 4, "DEFEAT");
            Assert.That(PracticeEncounterSnapshot.Parse(Utf8(terminal), Account).Settlement!.FocusCost, Is.Zero);
            terminal["settlement"]!["grant"]!["resources"]!["ink"] = 1L;
            Assert.Throws<PracticeProtocolException>(() => PracticeEncounterSnapshot.Parse(Utf8(terminal), Account));
        }

        [Test]
        public void GrowthConnectRefreshAndRecreatedAuthenticatedViewNeverPostAutomatically()
        {
            GrowthFixture(true); using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                var first = owner.CreateSession(); Assert.That(host.Seen, Is.Empty);
                Assert.That(Run(() => first.RefreshEncounterAsync(GrowthEncounter)).Code, Is.EqualTo(Codes.Ok)); Assert.That(first.CanMutateEncounter, Is.True);
                Assert.That(first.State.Snapshot, Is.Null, "an encounter never needs a synthetic Run snapshot"); first.Dispose();
                var next = owner.CreateSession(); Assert.That(Run(() => next.RefreshEncounterAsync(GrowthEncounter)).Snapshot!.Version, Is.EqualTo(3));
                Assert.That(host.Posts, Is.Empty); owner.Logout();
                Assert.That(Run(() => next.EncounterCommandAsync(next.Encounter!.Context, "end")).Code, Is.EqualTo(Codes.Disposed));
                Assert.That(host.Posts, Is.Empty);
            }
        }

        [Test]
        public void AnExplicitDungeonStartSendsOnlyItsOrderedOwnedIntentAndInstallsFreshBattleData()
        {
            GrowthFixture(); var session = Session(); Run(() => session.RefreshProgressionAsync());
            post = seen =>
            {
                Assert.That(seen.Path, Is.EqualTo(EncountersPath)); var body = JObject.Parse(seen.Text);
                CollectionAssert.AreEquivalent(new[] { "requestId", "expectedRevision", "dungeonId", "tier", "party" }, body.Properties().Select(property => property.Name));
                CollectionAssert.AreEqual(new[] { "rien", "nia", "harin" }, body["party"]!.Values<string>());
                growthActive = GrowthEncounter; progressionProfile["revision"] = 4L; growthEncounters[GrowthEncounter] = GrowthEncounterJson(0, 4);
                return Reply.Json(200, growthEncounters[GrowthEncounter]);
            };
            var result = Run(() => session.StartEncounterAsync(3, "dg_ink", 1, new[] { "rien", "nia", "harin" }));
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.Snapshot!.Version, Is.Zero);
            Assert.That(result.Progression!.ActiveEncounter!.EncounterId, Is.EqualTo(GrowthEncounter)); Assert.That(session.State.Snapshot, Is.Null);
            Assert.That(session.CanMutateEncounter, Is.True); Assert.That(Outbox().Load(), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LostLethalReplyKeepsOneExactRequestAndNeverReplaysAnAwardOnRetry(bool truncated)
        {
            GrowthFixture(true); var first = Session(); var before = Run(() => first.RefreshEncounterAsync(GrowthEncounter)).Snapshot!; byte[]? receipt = null;
            post = _ =>
            {
                growthActive = null; progressionProfile["revision"] = 4L; progressionProfile["resources"]!["ink"] = 12000L;
                progressionProfile["focus"]!["value"] = 220; progressionProfile["focus"]!["nextRegenAtMs"] = 361000L;
                growthEncounters[GrowthEncounter] = GrowthEncounterJson(4, 4, "VICTORY", events: true); receipt = Utf8(growthEncounters[GrowthEncounter]);
                return truncated ? new Reply(200, receipt) { DeclaredLength = receipt.Length + 20 } : new Reply(200, receipt) { Drop = true };
            };
            Assert.That(Run(() => first.EncounterCommandAsync(before.Context, "end")).Status, Is.EqualTo(MutationStatus.Pending));
            var original = Outbox().Load()!; first.Dispose(); var next = Session();
            Assert.That(Run(() => next.RefreshEncounterAsync(GrowthEncounter)).Snapshot!.Phase, Is.EqualTo("VICTORY"));
            Assert.That(host.Posts.Length, Is.EqualTo(1), "refreshing a terminal result never repeats its command");
            Assert.That(Run(() => next.RetryPendingAsync()).Code, Is.EqualTo(Codes.PendingUnresolved));
            post = _ => new Reply(200, receipt!);
            var result = Run(() => next.RetryGrowthPendingAsync());
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(host.Posts.Last().Text, Is.EqualTo(original.Body));
            Assert.That(result.Progression!.Resources!["ink"], Is.EqualTo(12000)); Assert.That(result.Progression.Focus!.Value, Is.EqualTo(220));
            Assert.That(result.PresentableEvents(), Is.Null, "a cached lethal receipt does not replay the animation or award");
            Assert.That(result.Progression.ActiveEncounter, Is.Null); Assert.That(next.CanMutateEncounter, Is.False); Assert.That(Outbox().Load(), Is.Null);
        }

        [Test]
        public void AnOldLethalReceiptCannotReplaceAnotherClientsNewActiveEncounter()
        {
            GrowthFixture(true); var session = Session(); Run(() => session.RefreshEncounterAsync(GrowthEncounter));
            Outbox().Stage(EncountersPath + "/" + GrowthEncounter + "/commands", "{\"requestId\":\"old-lethal-retry\",\"expectedVersion\":3,\"kind\":\"end\",\"args\":{}}");
            var receipt = GrowthEncounterJson(4, 4, "VICTORY", events: true);
            growthEncounters[GrowthEncounter] = GrowthEncounterJson(4, 5, "VICTORY", events: true);
            growthEncounters[OtherGrowthEncounter] = GrowthEncounterJson(0, 5, "PLAYER", OtherGrowthEncounter); growthActive = OtherGrowthEncounter; progressionProfile["revision"] = 5L;
            post = _ => Reply.Json(200, receipt);
            var result = Run(() => session.RetryGrowthPendingAsync());
            Assert.That(result.Receipt!.Encounter!.EncounterId, Is.EqualTo(GrowthEncounter));
            Assert.That(result.Snapshot!.EncounterId, Is.EqualTo(OtherGrowthEncounter)); Assert.That(result.Progression!.ActiveEncounter!.EncounterId, Is.EqualTo(OtherGrowthEncounter));
            Assert.That(result.PresentableEvents(), Is.Null); Assert.That(host.Posts.Length, Is.EqualTo(1));
        }

        [TestCase(409, "STALE_REVISION", true)]
        [TestCase(409, "RUN_ACTIVE", true)]
        [TestCase(422, "OVER_CAPACITY", true)]
        [TestCase(422, "NOT_ENOUGH_STAMINA", true)]
        [TestCase(422, "LOCKED", true)]
        [TestCase(422, "BAD_TARGET", false)]
        [TestCase(409, "IDEMPOTENCY_CONFLICT", false)]
        [TestCase(400, "INVALID_REQUEST", false)]
        [TestCase(404, "ENCOUNTER_NOT_FOUND", false)]
        [TestCase(503, "STORE_COMMIT_UNKNOWN", false)]
        public void RevisionGrowthRefusalsNeverBroadenTheDocumentedNoCommitTable(int status, string code, bool clears)
        {
            GrowthFixture(); var session = Session(); Run(() => session.RefreshProgressionAsync()); post = _ => Error(status, code);
            var result = Run(() => session.StartEncounterAsync(3, "dg_ink", 1, new[] { "nia", "harin", "rien" }));
            Assert.That(result.Status, Is.EqualTo(clears ? MutationStatus.NotCommitted : MutationStatus.Pending)); Assert.That(Outbox().Load() == null, Is.EqualTo(clears));
        }

        [TestCase(422, "BAD_TARGET", 3, true)]
        [TestCase(422, "NOT_ENOUGH_AP", 3, true)]
        [TestCase(422, "BAD_TARGET", 2, false)]
        [TestCase(409, "STALE_VERSION", 3, true)]
        [TestCase(409, "ENCOUNTER_FINISHED", 3, true)]
        [TestCase(409, "IDEMPOTENCY_CONFLICT", 3, false)]
        [TestCase(404, "ENCOUNTER_NOT_FOUND", 3, false)]
        public void EncounterCommandRefusalsRequireThatExactOwnedBattleVersion(int status, string code, int version, bool clears)
        {
            GrowthFixture(true); growthEncounters[GrowthEncounter] = GrowthEncounterJson(version);
            Outbox().Stage(EncountersPath + "/" + GrowthEncounter + "/commands", "{\"requestId\":\"command-refusal-proof\",\"expectedVersion\":3,\"kind\":\"end\",\"args\":{}}");
            post = _ => Error(status, code); var result = Run(() => Session().RetryGrowthPendingAsync());
            Assert.That(result.Status, Is.EqualTo(clears ? MutationStatus.NotCommitted : MutationStatus.Pending)); Assert.That(Outbox().Load() == null, Is.EqualTo(clears));
        }

        [Test]
        public void AForeignEncounterReplyCannotProveAnOtherwiseDefinitiveCommandRefusal()
        {
            GrowthFixture(true);
            Outbox().Stage(EncountersPath + "/" + GrowthEncounter + "/commands", "{\"requestId\":\"wrong-encounter-proof\",\"expectedVersion\":3,\"kind\":\"end\",\"args\":{}}");
            growthEncounters[GrowthEncounter] = GrowthEncounterJson(3, 3, "PLAYER", OtherGrowthEncounter); post = _ => Error(422, "BAD_TARGET");
            var result = Run(() => Session().RetryGrowthPendingAsync());
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Pending)); Assert.That(host.Posts, Is.Empty); Assert.That(Outbox().Load(), Is.Not.Null);
        }

        [Test]
        public void PureEncounterQueryAndLevelPreviewKeepAnUncertainRequestByteForByte()
        {
            GrowthFixture(true); var session = Session(); var shown = Run(() => session.RefreshEncounterAsync(GrowthEncounter)).Snapshot!;
            var pending = Outbox().Stage(PracticeCommandOutbox.CreateRunRoute, "{\"requestId\":\"pure-growth-pending\",\"useMemory\":true}"); byte[] original = File.ReadAllBytes(RecordPath);
            post = seen =>
            {
                if (seen.Path.EndsWith("/level-preview", StringComparison.Ordinal)) return Reply.Json(200, GrowthPreviewJson());
                var query = QueryJson(world, "h1", "now", null, new string[0]); query.Remove("runId"); query["encounterId"] = GrowthEncounter; query["version"] = 3; query["phase"] = "PLAYER";
                ((JObject)query["context"]!).Remove("node"); return Reply.Json(200, query);
            };
            var queryResult = Run(() => session.EncounterBattleQueryAsync(shown.Context, "h1"));
            Assert.That(queryResult.Code, Is.EqualTo(Codes.Ok)); Assert.That(queryResult.View!.Context.BattleVersion, Is.EqualTo(7));
            var preview = Run(() => session.PreviewCharacterLevelAsync(3, "nia", 2));
            Assert.That(preview.Code, Is.EqualTo(Codes.Ok)); Assert.That(preview.View!.CostInk, Is.EqualTo(120)); Assert.That(preview.View.CostCredits, Is.EqualTo(60));
            Assert.That(preview.View.After.Stats.Hp, Is.EqualTo(62)); Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(original));
            Assert.That(Run(() => session.LevelUpCharacterAsync(3, "nia", 2)).Code, Is.EqualTo(Codes.PendingUnresolved));
            Assert.That(Outbox().Load()!.Body, Is.EqualTo(pending.Body));
        }

        [Test]
        public void LevelCommitUsesTheExactTargetCostAndFreshProfileWithoutChangingTheActiveBattle()
        {
            GrowthFixture(true); var session = Session(); var shown = Run(() => session.RefreshEncounterAsync(GrowthEncounter)).Snapshot!; var state = shown.StateJObject();
            post = seen =>
            {
                Assert.That(seen.Path, Is.EqualTo(PracticeCommandOutbox.CharacterLevelUpRoute("nia"))); var request = JObject.Parse(seen.Text);
                CollectionAssert.AreEquivalent(new[] { "requestId", "expectedRevision", "targetLevel" }, request.Properties().Select(property => property.Name));
                progressionProfile["revision"] = 4L; progressionProfile["resources"]!["ink"] = 5880L; progressionProfile["resources"]!["credits"] = 9940L;
                progressionProfile["characters"]![0]!["level"] = 2; growthEncounters[GrowthEncounter]["revision"] = 4L;
                var reply = GrowthPreviewJson(); reply.Remove("canCommit"); reply["revision"] = 4L; reply["requestId"] = request["requestId"]!.DeepClone();
                reply["kind"] = "characterLevelUp"; reply["profile"] = GrowthCurrentProfile(); return Reply.Json(200, reply);
            };
            var result = Run(() => session.LevelUpCharacterAsync(3, "nia", 2));
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.Receipt!.CostInk, Is.EqualTo(120));
            Assert.That(result.Progression!.Resources!["ink"], Is.EqualTo(5880)); Assert.That(result.Progression.Characters[0].Level, Is.EqualTo(2));
            Assert.That(JToken.DeepEquals(state, result.Snapshot!.StateJObject()), Is.True, "the account level never rewrites this frozen Battle");
            Assert.That(result.PresentableEvents(), Is.Null);
        }

        [Test]
        public void SweepReceiptCarriesOneCostAndGrantAndNeverCreatesABattle()
        {
            GrowthFixture(); var session = Session(); Run(() => session.RefreshProgressionAsync());
            post = seen =>
            {
                var body = JObject.Parse(seen.Text); progressionProfile["revision"] = 4L; progressionProfile["resources"]!["ink"] = 18000L;
                progressionProfile["focus"]!["value"] = 200; progressionProfile["focus"]!["nextRegenAtMs"] = 361000L;
                return Reply.Json(200, new JObject { ["requestId"] = body["requestId"]!.DeepClone(), ["revision"] = 4L, ["kind"] = "dungeonSweep", ["dungeonId"] = "dg_ink", ["tier"] = 1, ["times"] = 2,
                    ["cost"] = new JObject { ["focus"] = 40 }, ["grant"] = GrowthGrantJson(12000, 40), ["profile"] = GrowthCurrentProfile() });
            };
            var result = Run(() => session.SweepDungeonAsync(3, "dg_ink", 1, 2));
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.Receipt!.FocusCost, Is.EqualTo(40));
            Assert.That(result.Receipt.Grant!.Resources["ink"], Is.EqualTo(12000)); Assert.That(result.Progression!.Focus!.Value, Is.EqualTo(200));
            Assert.That(result.Snapshot, Is.Null); Assert.That(session.Encounter, Is.Null); Assert.That(host.Posts.Length, Is.EqualTo(1));
        }

        [Test]
        public void EncounterReadsRemainMonotonicAndAnUncertainAccountSwitchPreservesTheRecord()
        {
            GrowthFixture(true); var session = Session(); Run(() => session.RefreshEncounterAsync(GrowthEncounter));
            growthEncounters[GrowthEncounter] = GrowthEncounterJson(2);
            Assert.That(Run(() => session.RefreshEncounterAsync(GrowthEncounter)).Code, Is.EqualTo(Codes.RollbackRefused));
            Assert.That(session.Encounter!.Version, Is.EqualTo(3)); Assert.That(session.EncounterNeedsRefresh, Is.True);
            var pending = Outbox().Stage(EncountersPath, "{\"requestId\":\"foreign-growth-proof\",\"expectedRevision\":3,\"dungeonId\":\"dg_ink\",\"tier\":1,\"party\":[\"nia\",\"harin\",\"rien\"]}");
            byte[] original = File.ReadAllBytes(RecordPath);
            using (var other = new PracticeServerSession(host.Endpoint, OtherAccount, () => Credential, new PracticeCommandOutbox(RecordPath, host.Endpoint, OtherAccount)))
                Assert.That(Run(() => other.RetryGrowthPendingAsync()).Code, Is.EqualTo(PracticeOutboxException.BindingMismatch));
            Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(original)); Assert.That(Outbox().Load()!.Body, Is.EqualTo(pending.Body)); Assert.That(host.Posts, Is.Empty);
        }

        [Test]
        public void AnEncounterAttemptCapturesItsCredentialOnceAcrossEveryPreflightPostAndFreshRead()
        {
            GrowthFixture(true); var session = Session(); var before = Run(() => session.RefreshEncounterAsync(GrowthEncounter)).Snapshot!;
            int captures = 0, seenBefore = host.Seen.Length; source = () => ++captures == 1 ? Credential : RotatedCredential;
            post = _ =>
            {
                progressionProfile["revision"] = 4L; growthEncounters[GrowthEncounter] = GrowthEncounterJson(4, 4, events: true);
                return Reply.Json(200, growthEncounters[GrowthEncounter]);
            };
            var result = Run(() => session.EncounterCommandAsync(before.Context, "end"));
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(captures, Is.EqualTo(1));
            Assert.That(host.Seen.Skip(seenBefore).All(seen => seen.Authorization == "Bearer " + Credential), Is.True);
            Assert.That(result.Snapshot!.Version, Is.EqualTo(4));
        }

        [TestCase(201)]
        [TestCase(202)]
        public void ANewEncounterShapedNon200ReplyNeverResolvesTheDurableDeparture(int status)
        {
            GrowthFixture(); var session = Session(); Run(() => session.RefreshProgressionAsync());
            post = _ => Reply.Json(status, GrowthEncounterJson(0, 4));
            var result = Run(() => session.StartEncounterAsync(3, "dg_ink", 1, new[] { "nia", "harin", "rien" }));
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Pending)); Assert.That(result.Receipt, Is.Null); Assert.That(result.Snapshot, Is.Null);
            Assert.That(session.Encounter, Is.Null); Assert.That(Outbox().Load(), Is.Not.Null); Assert.That(host.Posts.Length, Is.EqualTo(1));
        }
        [Test]
        public void EncounterMutationGateRequiresFreshProgressionAndTheSameActiveEncounterVersion()
        {
            GrowthFixture(true);
            using (var session = Session())
            {
                Assert.That(Wait(session.RefreshEncounterAsync(GrowthEncounter)).Code, Is.EqualTo(Codes.Ok));
                Assert.That(session.CanMutateEncounter, Is.True);
                var healthy = additionalReply;
                additionalReply = seen => seen.Method == "GET" && seen.Path == ProgressionPath ? Error(503, "ACCOUNT_BUSY") : healthy!(seen);
                Assert.That(Wait(session.RefreshProgressionAsync()).Snapshot, Is.Null);
                Assert.That(session.ProgressionNeedsRefresh, Is.True);
                Assert.That(session.EncounterNeedsRefresh, Is.False, "the last Battle itself is still retained");
                Assert.That(session.CanMutateEncounter, Is.False, "a retained pointer is not fresh account authority");

                additionalReply = healthy;
                Assert.That(Wait(session.RefreshProgressionAsync()).Code, Is.EqualTo(Codes.Ok));
                Assert.That(session.ProgressionNeedsRefresh, Is.False);
                Assert.That(session.CanMutateEncounter, Is.True, "the same fresh active ID and version restore the gate");
                progressionProfile["revision"] = 4L;
                growthEncounters[GrowthEncounter]["version"] = 4;
                Assert.That(Wait(session.RefreshProgressionAsync()).Code, Is.EqualTo(Codes.Ok));
                Assert.That(session.CanMutateEncounter, Is.False, "a newer active version requires its Battle read");
                progressionProfile["revision"] = 5L;
                growthEncounters[OtherGrowthEncounter] = GrowthEncounterJson(revision: 5, id: OtherGrowthEncounter);
                growthActive = OtherGrowthEncounter;
                Assert.That(Wait(session.RefreshProgressionAsync()).Code, Is.EqualTo(Codes.Ok));
                Assert.That(session.CanMutateEncounter, Is.False, "another active ID cannot enable the displayed Battle");
                Assert.That(host.Posts, Is.Empty, "recovery reads never send an action");
            }
        }
    }
}
