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
        const string EquipmentWeapon = "basic:w_basic_device";
        const string WeeklyPath = "/dev/practice/weekly-bosses";
        static string EquipmentPath(string scope, string target, string action) => "/dev/practice/progression/" + scope + "/" + target + "/" + action;
        [Test]
        public void EquipmentRoutesUseTheExistingSingleSlotAndPurePreviewsNeverStage()
        {
            var cases = new[] {
                (EquipmentPath("characters", "nia", "ascend"), ""),
                (EquipmentPath("characters", "nia", "skill-up"), ",\"line\":\"card\""),
                (EquipmentPath("characters", "nia", "gear-craft"), ",\"slot\":\"armor\""),
                (EquipmentPath("characters", "nia", "gear-enhance"), ",\"slot\":\"accessory\""),
                (EquipmentPath("weapons", EquipmentWeapon, "level-up"), ",\"targetLevel\":2"),
                (EquipmentPath("weapons", EquipmentWeapon, "ascend"), ""),
                (EquipmentPath("weapons", EquipmentWeapon, "refine"), ""),
                (EquipmentPath("weapons", EquipmentWeapon, "equip"), ",\"charId\":null"),
                ("/dev/practice/progression/boss-shop/exchange", ",\"itemId\":\"refine_core\",\"quantity\":2"),
                (WeeklyPath, ",\"bossId\":\"b_bell\",\"difficulty\":1,\"practice\":false,\"party\":[\"nia\",\"harin\",\"rien\"]")
            };
            foreach (var row in cases)
            {
                string body = "{\"requestId\":\"equipment-route-proof\",\"expectedRevision\":3" + row.Item2 + "}";
                PendingPracticeCommand? pending = null;
                Assert.DoesNotThrow(() => pending = Outbox().Stage(row.Item1, body), row.Item1);
                Assert.That(pending!.Body, Is.EqualTo(body)); var bytes = File.ReadAllBytes(RecordPath);
                Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(PracticeCommandOutbox.StartEncounterRoute, "{\"requestId\":\"other-route-proof\",\"expectedRevision\":3,\"dungeonId\":\"dg_ink\",\"tier\":1,\"party\":[\"nia\",\"harin\",\"rien\"]}"));
                Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(bytes)); Outbox().Confirm(pending);
                Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(row.Item1 + "-preview", body));
            }
        }
        static JObject EquipmentProfileJson(long revision = 3)
        {
            var profile = GrowthProfileJson(revision); profile["policyVersion"] = "account-progression-20261021-v3";
            profile["resources"]!["boss_token"] = 120L;
            foreach (var c in profile["characters"]!.OfType<JObject>())
            {
                string id = c["charId"]!.Value<string>()!;
                string type = id == "nia" ? "device" : id == "harin" ? "melee" : "ritual";
                c["weaponInstanceId"] = "basic:w_basic_" + type; c["cap"] = 20; c["effectiveSkillLv"] = 1; c["effectiveUltLv"] = 1;
                c["capacity"] = 6; c["weaponType"] = type; c["deployable"] = true; c["eligibleWeapons"] = new JArray("basic:w_basic_" + type);
                c["stats"] = GrowthLevelJson(1, 0, 61)["stats"]!.DeepClone();
            }
            foreach (var w in profile["weapons"]!.OfType<JObject>())
            {
                string id = w["weaponId"]!.Value<string>()!; string type = id.Substring("w_basic_".Length);
                w["instanceId"] = "basic:" + id; w["type"] = type; w["rarity"] = "R"; w["copies"] = 1; w["refineMode"] = "core"; w["cap"] = 20;
            }
            profile["bossShop"] = new JObject { ["weekKey"] = "2026-10-19", ["resetsAtMs"] = 1792958400000L,
                ["items"] = new JArray(new JObject { ["itemId"] = "refine_core", ["unitCost"] = 60L, ["limit"] = 2, ["bought"] = 0, ["remaining"] = 2 },
                    new JObject { ["itemId"] = "book", ["unitCost"] = 10L, ["limit"] = 6, ["bought"] = 0, ["remaining"] = 6 },
                    new JObject { ["itemId"] = "polish", ["unitCost"] = 5L, ["limit"] = 20, ["bought"] = 0, ["remaining"] = 20 }) };
            profile["weeklyBoss"] = new JObject { ["weekKey"] = "2026-10-19", ["resetsAtMs"] = 1792958400000L, ["claimed"] = 0, ["remaining"] = 3,
                ["bosses"] = new JArray(new[] { "b_bell", "b_notary", "b_garden", "b_archivist" }.Select(id => new JObject { ["id"] = id, ["unlockLevel"] = 15, ["available"] = false,
                    ["difficulties"] = new JArray(Enumerable.Range(1, 5).Select(n => new JObject { ["difficulty"] = n, ["stage"] = n == 5 ? 60 : n * 10 + 20, ["hpBps"] = n == 5 ? 13000 : 0, ["dmgBps"] = n == 5 ? 12000 : 0 })) })) };
            return profile;
        }
        void EquipmentFixture(bool active = false)
        {
            GrowthFixture(active); progressionProfile = EquipmentProfileJson();
        }
        static JObject EquipmentCharacter(JObject profile, string id = "nia") => profile["characters"]!.OfType<JObject>().Single(c => c["charId"]!.Value<string>() == id);
        static JObject EquipmentWeaponRow(JObject profile, string id = EquipmentWeapon) => profile["weapons"]!.OfType<JObject>().Single(w => w["instanceId"]!.Value<string>() == id);
        static JObject EquipmentChange(JObject profile, string kind, bool preview, string? requestId = null)
        {
            bool weapon = kind.StartsWith("weapon", StringComparison.Ordinal);
            var before = (JObject)(weapon ? EquipmentWeaponRow(profile) : EquipmentCharacter(profile)).DeepClone(); var after = (JObject)before.DeepClone(); var cost = new JObject();
            if (kind == "weaponRefine") { after["refine"] = 2; cost["refine_core"] = 1L; }
            else if (kind == "weaponLevelUp") { after["level"] = 2; cost["oil"] = 100L; cost["credits"] = 50L; }
            else if (kind == "weaponAscend") { before["level"] = after["level"] = 20; after["asc"] = 1; after["cap"] = 30; cost["alloy"] = 2L; cost["credits"] = 5000L; }
            else if (kind == "characterAscend") { before["level"] = after["level"] = 20; after["asc"] = 1; after["cap"] = 30; cost["seal"] = 4L; cost["credits"] = 10000L; }
            else if (kind == "skillUp") { after["skillLv"] = 2; after["effectiveSkillLv"] = 2; after["stats"]!["skillLv"] = 2; cost["book"] = 2L; cost["credits"] = 5000L; }
            else if (kind == "gearCraft") { after["armor"] = 0; after["stats"]!["hp"] = 64; cost["polish"] = 20L; cost["credits"] = 10000L; }
            else if (kind == "gearEnhance") { before["armor"] = 0; before["stats"]!["hp"] = 64; after["armor"] = 1; after["stats"]!["hp"] = 66; cost["polish"] = 1L; cost["credits"] = 1000L; }
            var json = new JObject { ["revision"] = preview ? 3L : 4L, ["kind"] = kind, ["target"] = new JObject { [weapon ? "instanceId" : "charId"] = weapon ? EquipmentWeapon : "nia" }, ["before"] = before, ["after"] = after, ["cost"] = cost };
            if (preview) json["canCommit"] = true;
            else
            {
                json["requestId"] = requestId; var next = (JObject)profile.DeepClone(); next["revision"] = 4L;
                (weapon ? EquipmentWeaponRow(next) : EquipmentCharacter(next)).Replace(after.DeepClone());
                foreach (var p in cost.Properties()) next["resources"]![p.Name] = Math.Max(0, next["resources"]![p.Name]!.Value<long>() - p.Value.Value<long>());
                json["profile"] = next;
            }
            return json;
        }
        static JObject EquipmentIntentJson(string kind) => kind == "skillUp" ? new JObject { ["line"] = "card" } : kind == "gearCraft" || kind == "gearEnhance" ? new JObject { ["slot"] = "armor" }
            : kind == "weaponLevelUp" ? new JObject { ["targetLevel"] = 2 } : new JObject();

        [Test]
        public void EquipmentProfileHasActualNullableGearAndNoV3ValuesAppearInHistoricV2()
        {
            var old = GrowthProfileJson(); var legacy = PracticeProgressionSnapshot.Parse(Utf8(old), Account);
            Assert.That(legacy.Characters[0].Cap, Is.Null); Assert.That(legacy.Characters[0].Deployable, Is.Null); Assert.That(legacy.BossShop, Is.Null); Assert.That(legacy.WeeklyBoss, Is.Null);
            Assert.That(JToken.DeepEquals(legacy.ToJObject(), old), Is.True);
            var json = EquipmentProfileJson(); var profile = PracticeProgressionSnapshot.Parse(Utf8(json), Account);
            Assert.That(profile.Resources!["boss_token"], Is.EqualTo(120)); Assert.That(profile.Resources["oil"], Is.EqualTo(4800));
            var nia = profile.Characters.Single(c => c.CharId == "nia"); Assert.That(nia.Armor, Is.Null); Assert.That(nia.Accessory, Is.Null);
            Assert.That(nia.Stats!.Hp, Is.EqualTo(61)); Assert.That(nia.Deployable, Is.True); Assert.That(nia.Capacity, Is.EqualTo(6));
            Assert.That(nia.EligibleWeapons, Is.EqualTo(new[] { EquipmentWeapon })); Assert.That(profile.Weapons[0].RefineMode, Is.EqualTo("core"));
            Assert.That(profile.BossShop!.Items.Single(i => i.ItemId == "refine_core").Remaining, Is.EqualTo(2)); Assert.That(profile.WeeklyBoss!.Remaining, Is.EqualTo(3));
            Assert.That(profile.WeeklyBoss.Bosses.Select(b => b.Id), Is.EquivalentTo(new[] { "b_bell", "b_notary", "b_garden", "b_archivist" }));
            var detached = profile.ToJObject(); detached["resources"]!["boss_token"] = 0; Assert.That(profile.Resources["boss_token"], Is.EqualTo(120));
            old["resources"]!["boss_token"] = 0; Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(old), Account));
        }
        [TestCase("resources.boss_token", -1L)]
        [TestCase("characters[0].cap", 30L)]
        [TestCase("characters[0].effectiveSkillLv", 13L)]
        [TestCase("characters[0].capacity", 10L)]
        [TestCase("weapons[0].copies", 0L)]
        [TestCase("weeklyBoss.remaining", 4L)]
        [TestCase("weeklyBoss.resetsAtMs", -1L)]
        [TestCase("bossShop.items[0].remaining", 3L)]
        public void EquipmentTypedProfileRefusesUnrepresentableOrInconsistentPublicValues(string path, long value)
        {
            var json = EquipmentProfileJson(); json.SelectToken(path)!.Replace(value);
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(json), Account));
        }
        [Test]
        public void EquipmentOwnershipAndExplicitPrivateFieldContextsRemainBound()
        {
            foreach (string hidden in new[] { "seed", "binding", "credential", "revision", "serverNowMs", "ledger" })
            {
                var json = EquipmentProfileJson(); json["characters"]![0]![hidden] = 1;
                Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(json), Account), hidden);
            }
            var wrong = EquipmentProfileJson(); wrong["characters"]![0]!["eligibleWeapons"] = new JArray("basic:w_basic_melee");
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(wrong), Account));
            wrong = EquipmentProfileJson(); wrong["weapons"]![0]!["equippedBy"] = "harin";
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(wrong), Account));
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(EquipmentProfileJson()), OtherAccount));
            var json2 = EquipmentProfileJson(); json2["characters"]![0]!["weaponInstanceId"] = JValue.CreateNull(); json2["characters"]![0]!["deployable"] = false;
            json2["weapons"]![0]!["equippedBy"] = JValue.CreateNull();
            Assert.That(PracticeProgressionSnapshot.Parse(Utf8(json2), Account).Characters[0].Deployable, Is.False, "uncrafted/unassigned isn't a fallback weapon");
        }
        [TestCase("characterAscend")]
        [TestCase("skillUp")]
        [TestCase("gearCraft")]
        [TestCase("gearEnhance")]
        [TestCase("weaponLevelUp")]
        [TestCase("weaponAscend")]
        [TestCase("weaponRefine")]
        public void EquipmentPreviewsArePureExactAndCarryTheActualTargetProjection(string kind)
        {
            EquipmentFixture(); var intent = EquipmentIntentJson(kind); string target = kind.StartsWith("weapon", StringComparison.Ordinal) ? EquipmentWeapon : "nia";
            var pending = Outbox().Stage(PracticeCommandOutbox.CharacterLevelUpRoute("nia"), "{\"requestId\":\"existing-uncertain\",\"expectedRevision\":3,\"targetLevel\":2}");
            var bytes = File.ReadAllBytes(RecordPath);
            post = seen =>
            {
                Assert.That(seen.Path, Is.EqualTo(PracticeCommandOutbox.EquipmentActionRoute(kind, target, true))); Assert.That(JObject.Parse(seen.Text).Property("requestId"), Is.Null);
                return Reply.Json(200, EquipmentChange(progressionProfile, kind, true));
            };
            using (var session = Session())
            {
                var result = Wait(session.PreviewEquipmentAsync(3, kind, target, intent)); Assert.That(result.Code, Is.EqualTo(Codes.Ok));
                Assert.That(result.View!.Kind, Is.EqualTo(kind)); Assert.That(result.View.Revision, Is.EqualTo(3)); Assert.That(result.View.TargetId, Is.EqualTo(target));
                if (kind == "weaponLevelUp") { Assert.That(result.View.Cost["oil"], Is.EqualTo(100)); Assert.That(result.View.Cost["credits"], Is.EqualTo(50)); }
                if (kind == "gearCraft") { Assert.That(result.View.CharacterBefore!.Armor, Is.Null); Assert.That(result.View.CharacterAfter!.Armor, Is.Zero); Assert.That(result.View.CharacterAfter.Stats!.Hp, Is.EqualTo(64)); }
                if (kind == "weaponRefine") { Assert.That(result.View.Cost.Keys, Is.EqualTo(new[] { "refine_core" })); Assert.That(result.View.Cost["refine_core"], Is.EqualTo(1)); }
                Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(bytes)); Assert.That(session.Progression, Is.Null, "pure preview doesn't install historical account data");
            }
            Assert.That(Outbox().Load()!.Body, Is.EqualTo(pending.Body));
        }
        [TestCase("seed")]
        [TestCase("cost")]
        [TestCase("stats")]
        [TestCase("credential")]
        [TestCase("targetLevel")]
        public void EquipmentForgedFieldsNeverStageOrReachTheServer(string field)
        {
            EquipmentFixture(); var intent = new JObject { [field] = 1 };
            using (var session = Session())
            {
                Assert.That(Wait(session.CommitEquipmentAsync(3, "weaponRefine", EquipmentWeapon, intent)).Status, Is.EqualTo(MutationStatus.NotSent));
                Assert.That(Wait(session.PreviewEquipmentAsync(3, "weaponRefine", EquipmentWeapon, intent)).View, Is.Null);
            }
            Assert.That(host.Posts, Is.Empty); Assert.That(Outbox().Load(), Is.Null);
            var body = new JObject { ["requestId"] = "forged-equipment-proof", ["expectedRevision"] = 3, [field] = 1 };
            Assert.Throws<PracticeOutboxException>(() => Outbox().Stage(PracticeCommandOutbox.EquipmentActionRoute("weaponRefine", EquipmentWeapon), body.ToString(Newtonsoft.Json.Formatting.None)));
        }
        [TestCase("drop")]
        [TestCase("truncate")]
        public void EquipmentLostReplySurvivesViewRecreationAndRetriesOneExactReceipt(string failure)
        {
            EquipmentFixture(true); string frozenBattle = growthEncounters[GrowthEncounter].ToString(Newtonsoft.Json.Formatting.None); byte[]? saved = null; int writes = 0;
            post = seen =>
            {
                var request = JObject.Parse(seen.Text); Assert.That(request.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "requestId", "expectedRevision" }));
                Assert.That(Outbox().Load()!.Body, Is.EqualTo(seen.Text));
                if (saved == null)
                {
                    writes++; var receipt = EquipmentChange(GrowthCurrentProfile(), "weaponRefine", false, request["requestId"]!.Value<string>()!);
                    progressionProfile = (JObject)receipt["profile"]!.DeepClone(); saved = Utf8(receipt);
                    return failure == "drop" ? new Reply(200, saved) { Drop = true } : new Reply(200, saved.Take(saved.Length / 2).ToArray());
                }
                return new Reply(200, saved);
            };
            string firstBody;
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                using (var session = owner.CreateSession())
                {
                    Assert.That(Wait(session.RefreshProgressionAsync()).Code, Is.EqualTo(Codes.Ok));
                    var result = Wait(session.CommitEquipmentAsync(3, "weaponRefine", EquipmentWeapon)); Assert.That(result.Status, Is.EqualTo(MutationStatus.Pending));
                    firstBody = result.Request!.Body;
                }
                using (var fresh = owner.CreateSession())
                {
                    Assert.That(Wait(fresh.RefreshProgressionAsync()).Code, Is.EqualTo(Codes.Ok)); Assert.That(host.Posts.Length, Is.EqualTo(1));
                    var result = Wait(fresh.RetryEquipmentPendingAsync()); Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.RefreshCode, Is.Null);
                    Assert.That(result.Request!.Body, Is.EqualTo(firstBody)); Assert.That(result.Progression!.Weapons.Single(w => w.InstanceId == EquipmentWeapon).Refine, Is.EqualTo(2));
                    Assert.That(result.Progression.ActiveEncounter!.EncounterId, Is.EqualTo(GrowthEncounter)); Assert.That(result.Receipt!.Change.Cost.ContainsKey("core"), Is.False);
                }
                Assert.That(owner.IsAuthenticated, Is.True);
            }
            Assert.That(writes, Is.EqualTo(1)); Assert.That(host.Posts.Select(s => s.Text).Distinct().Count(), Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
            Assert.That(growthEncounters[GrowthEncounter].ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo(frozenBattle));
        }
        [TestCase(422, "AUTO_REFINED", false)]
        [TestCase(422, "REFINE_CAP", false)]
        [TestCase(422, "WEAPON_NOT_OWNED", false)]
        [TestCase(422, "INSUFFICIENT_RESOURCES", false)]
        [TestCase(409, "STALE_REVISION", false)]
        [TestCase(409, "IDEMPOTENCY_CONFLICT", true)]
        [TestCase(422, "SKILL_CAP", true)]
        [TestCase(422, "UNRECOGNIZED_REJECTION", true)]
        [TestCase(409, "RUN_ACTIVE", true)]
        [TestCase(400, "INVALID_REQUEST", true)]
        [TestCase(404, "NOT_FOUND", true)]
        [TestCase(503, "ACCOUNT_BUSY", true)]
        public void EquipmentRefusalsRequireTheExactRouteStatusAndFreshOwnerProof(int status, string code, bool keeps)
        {
            EquipmentFixture(); post = _ => Error(status, code);
            using (var session = Session())
            {
                Wait(session.RefreshProgressionAsync()); var result = Wait(session.CommitEquipmentAsync(3, "weaponRefine", EquipmentWeapon));
                Assert.That(result.Status, Is.EqualTo(keeps ? MutationStatus.Pending : MutationStatus.NotCommitted)); Assert.That(result.Code, Is.EqualTo(code));
                Assert.That(Outbox().Load() != null, Is.EqualTo(keeps));
            }
        }
        [Test]
        public void EquipmentOlderOrForeignPreflightCannotProveARefusalOrSendACommand()
        {
            EquipmentFixture(); var pending = Outbox().Stage(PracticeCommandOutbox.EquipmentActionRoute("weaponRefine", EquipmentWeapon), "{\"requestId\":\"preflight-proof\",\"expectedRevision\":4}");
            post = _ => Error(422, "REFINE_CAP");
            using (var session = Session()) Assert.That(Wait(session.RetryEquipmentPendingAsync()).Status, Is.EqualTo(MutationStatus.Pending));
            Assert.That(Outbox().Load()!.Body, Is.EqualTo(pending.Body));
            progressionProfile["accountId"] = OtherAccount; int before = host.Posts.Length;
            using (var session = Session()) Assert.That(Wait(session.RetryEquipmentPendingAsync()).Status, Is.EqualTo(MutationStatus.Pending));
            Assert.That(host.Posts.Length, Is.EqualTo(before)); Assert.That(Outbox().Load()!.Body, Is.EqualTo(pending.Body));
            using (var foreign = new PracticeServerSession(host.Endpoint, OtherAccount, () => Credential, new PracticeCommandOutbox(RecordPath, host.Endpoint, OtherAccount))) Assert.That(Wait(foreign.RetryEquipmentPendingAsync()).Status, Is.EqualTo(MutationStatus.NotSent));
        }
        [TestCase("requestId")]
        [TestCase("revision")]
        [TestCase("target")]
        [TestCase("after")]
        [TestCase("private")]
        public void EquipmentWrongOrMalformedSuccessNeverConfirmsPending(string tamper)
        {
            EquipmentFixture(); post = seen =>
            {
                var reply = EquipmentChange(progressionProfile, "weaponRefine", false, JObject.Parse(seen.Text)["requestId"]!.Value<string>()!);
                if (tamper == "requestId") reply["requestId"] = "different-request";
                if (tamper == "revision") reply["revision"] = 5;
                if (tamper == "target") reply["target"]!["instanceId"] = "basic:foreign";
                if (tamper == "after") reply["after"]!["refine"] = 3;
                if (tamper == "private") reply["after"]!["credential"] = "never-display";
                return Reply.Json(200, reply);
            };
            using (var session = Session())
            {
                Wait(session.RefreshProgressionAsync()); var result = Wait(session.CommitEquipmentAsync(3, "weaponRefine", EquipmentWeapon));
                Assert.That(result.Status, Is.EqualTo(MutationStatus.Pending)); Assert.That(result.Receipt, Is.Null); Assert.That(Outbox().Load(), Is.Not.Null);
            }
        }
        [Test]
        public void WeeklyUsesTheExistingBattleAndStrictTenResourceTerminalVariant()
        {
            var normal = GrowthEncounterJson(); Assert.That(PracticeEncounterSnapshot.Parse(Utf8(normal), Account).Kind, Is.EqualTo("dungeon"));
            var weekly = WeeklyEncounterJson(); var snapshot = PracticeEncounterSnapshot.Parse(Utf8(weekly), Account);
            Assert.That(snapshot.Kind, Is.EqualTo("weeklyBoss")); Assert.That(snapshot.BossId, Is.EqualTo("b_bell")); Assert.That(snapshot.Difficulty, Is.EqualTo(5)); Assert.That(snapshot.Practice, Is.False);
            foreach (var tuple in new[] { ("VICTORY", (string?)null), ("VICTORY", "PRACTICE"), ("VICTORY", "WEEKLY_LIMIT"), ("DEFEAT", "DEFEAT") })
            {
                var ended = WeeklyEncounterJson(tuple.Item1, tuple.Item2);
                var settlement = PracticeEncounterSnapshot.Parse(Utf8(ended), Account).Settlement!;
                Assert.That(settlement.FocusCost, Is.Zero); Assert.That(settlement.Stars, Is.Zero); Assert.That(settlement.Rewarded, Is.EqualTo(tuple.Item2 == null));
                Assert.That(settlement.Grant.Resources["boss_token"], Is.EqualTo(tuple.Item2 == null ? 40 : 0));
                Assert.That(settlement.Grant.AccountXp, Is.EqualTo(tuple.Item2 == null ? 100 : 0));
            }
            weekly = WeeklyEncounterJson("VICTORY"); weekly["settlement"]!["grant"]!["resources"]!["bossToken"] = 40;
            Assert.Throws<PracticeProtocolException>(() => PracticeEncounterSnapshot.Parse(Utf8(weekly), Account));
            weekly = WeeklyEncounterJson("VICTORY", "PRACTICE"); weekly["settlement"]!["grant"]!["resources"]!["boss_token"] = 40;
            Assert.Throws<PracticeProtocolException>(() => PracticeEncounterSnapshot.Parse(Utf8(weekly), Account));
        }
        static JObject WeeklyEncounterJson(string phase = "PLAYER", string? reason = null, int version = 3, long revision = 3)
        {
            var json = GrowthEncounterJson(version, revision, phase); json["kind"] = "weeklyBoss"; json["bossId"] = "b_bell"; json["difficulty"] = 5; json["practice"] = reason == "PRACTICE";
            if (phase != "PLAYER")
            {
                var grant = GrowthResourcesJson(); grant["boss_token"] = 0L;
                if (reason == null) { grant["core"] = 2L; grant["credits"] = 20000L; grant["boss_token"] = 40L; }
                json["settlement"] = new JObject { ["outcome"] = phase, ["stars"] = 0, ["cost"] = new JObject { ["focus"] = 0 }, ["grant"] = new JObject { ["resources"] = grant, ["accountXp"] = reason == null ? 100L : 0L },
                    ["kind"] = "weeklyBoss", ["rewarded"] = reason == null, ["reason"] = reason == null ? (JToken)JValue.CreateNull() : reason, ["weekKey"] = "2026-10-19", ["claimed"] = reason == "WEEKLY_LIMIT" ? 3 : 1, ["remaining"] = reason == "WEEKLY_LIMIT" ? 0 : 2 };
            }
            return json;
        }
        [Test]
        public void WeeklyStartIsExplicitAndBindsPracticeAndBossWithoutSendingAClockOrGrant()
        {
            EquipmentFixture(); post = seen =>
            {
                Assert.That(seen.Path, Is.EqualTo(WeeklyPath)); var body = JObject.Parse(seen.Text);
                Assert.That(body.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "requestId", "expectedRevision", "bossId", "difficulty", "practice", "party" }));
                Assert.That(body["practice"]!.Type, Is.EqualTo(JTokenType.Boolean));
                progressionProfile["revision"] = 4L; var current = WeeklyEncounterJson(version: 0, revision: 4); growthActive = GrowthEncounter; growthEncounters[GrowthEncounter] = current;
                return Reply.Json(200, current);
            };
            var previous = additionalReply;
            additionalReply = seen =>
            {
                if (seen.Method == "GET" && seen.Path == ProgressionPath && growthActive != null)
                {
                    var current = (JObject)progressionProfile.DeepClone(); current["activeEncounter"] = new JObject { ["encounterId"] = GrowthEncounter, ["kind"] = "weeklyBoss", ["bossId"] = "b_bell", ["difficulty"] = 5, ["practice"] = false, ["phase"] = "PLAYER", ["version"] = 0 };
                    return Reply.Json(200, current);
                }
                return previous!(seen);
            };
            using (var session = Session())
            {
                Assert.That(Wait(session.RefreshProgressionAsync()).Code, Is.EqualTo(Codes.Ok)); Assert.That(host.Posts, Is.Empty);
                var result = Wait(session.StartWeeklyBossAsync(3, "b_bell", 5, false, new[] { "nia", "harin", "rien" }));
                Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.RefreshCode, Is.Null); Assert.That(result.Snapshot!.Kind, Is.EqualTo("weeklyBoss"));
                Assert.That(session.CanMutateEncounter, Is.True); Assert.That(Outbox().Load(), Is.Null);
            }
        }
        static JObject EquipmentEquipJson(JObject profile, bool preview, string? requestId = null)
        {
            var json = new JObject { ["revision"] = preview ? 3L : 4L, ["kind"] = "weaponEquip", ["target"] = new JObject { ["instanceId"] = EquipmentWeapon, ["charId"] = JValue.CreateNull() },
                ["before"] = new JObject { ["instanceId"] = EquipmentWeapon, ["equippedBy"] = "nia", ["charId"] = JValue.CreateNull(), ["charWeaponInstanceId"] = JValue.CreateNull() },
                ["after"] = new JObject { ["instanceId"] = EquipmentWeapon, ["equippedBy"] = JValue.CreateNull(), ["charId"] = JValue.CreateNull(), ["charWeaponInstanceId"] = JValue.CreateNull(),
                    ["displaced"] = new JObject { ["charId"] = "nia", ["instanceId"] = JValue.CreateNull() } }, ["cost"] = new JObject() };
            if (preview) json["canCommit"] = true;
            else
            {
                var next = (JObject)profile.DeepClone(); next["revision"] = 4L; EquipmentCharacter(next)["weaponInstanceId"] = JValue.CreateNull(); EquipmentCharacter(next)["deployable"] = false; EquipmentWeaponRow(next)["equippedBy"] = JValue.CreateNull();
                json["requestId"] = requestId; json["profile"] = next;
            }
            return json;
        }
        [Test]
        public void EquipmentUnequipIsExplicitNullZeroCostAndNeverBorrowsABasicWeapon()
        {
            EquipmentFixture(); bool preview = true;
            post = seen =>
            {
                var body = JObject.Parse(seen.Text); Assert.That(body["charId"]!.Type, Is.EqualTo(JTokenType.Null));
                var reply = EquipmentEquipJson(progressionProfile, preview, body["requestId"]?.Value<string>());
                if (!preview) progressionProfile = (JObject)reply["profile"]!.DeepClone(); return Reply.Json(200, reply);
            };
            using (var session = Session())
            {
                Wait(session.RefreshProgressionAsync()); var intent = new JObject { ["charId"] = JValue.CreateNull() };
                var view = Wait(session.PreviewEquipmentAsync(3, "weaponEquip", EquipmentWeapon, intent)).View!;
                Assert.That(view.Cost, Is.Empty); Assert.That(view.AssignmentBefore!.EquippedBy, Is.EqualTo("nia")); Assert.That(view.AssignmentAfter!.EquippedBy, Is.Null);
                preview = false; var result = Wait(session.CommitEquipmentAsync(3, "weaponEquip", EquipmentWeapon, intent));
                Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.RefreshCode, Is.Null);
                Assert.That(result.Progression!.Characters.Single(c => c.CharId == "nia").Deployable, Is.False); Assert.That(result.Progression.Weapons.Single(w => w.InstanceId == EquipmentWeapon).EquippedBy, Is.Null);
                Assert.That(result.Progression.Weapons.Count, Is.EqualTo(3)); Assert.That(Outbox().Load(), Is.Null);
            }
        }
        static JObject EquipmentShopJson(bool preview, bool enough = true, string? requestId = null)
        {
            JObject Balance(int bought, long tokens, long held) => new JObject { ["itemId"] = "refine_core", ["weekKey"] = "2026-10-19", ["resetsAtMs"] = 1792958400000L, ["bought"] = bought, ["remaining"] = 2 - bought, ["boss_token"] = tokens, ["balance"] = held };
            var json = new JObject { ["revision"] = preview ? 3L : 4L, ["kind"] = "bossExchange", ["target"] = new JObject { ["itemId"] = "refine_core", ["quantity"] = 2 },
                ["before"] = Balance(0, enough ? 120 : 40, 0), ["after"] = Balance(2, enough ? 0 : -80, 2), ["cost"] = new JObject { ["boss_token"] = 120L } };
            if (preview) { json["canCommit"] = enough; if (!enough) json["code"] = "INSUFFICIENT_RESOURCES"; }
            else
            {
                var next = EquipmentProfileJson(4); next["resources"]!["boss_token"] = 0L; next["resources"]!["refine_core"] = 2L;
                next["bossShop"]!["items"]![0]!["bought"] = 2; next["bossShop"]!["items"]![0]!["remaining"] = 0;
                json["requestId"] = requestId; json["profile"] = next;
            }
            return json;
        }
        [Test]
        public void EquipmentShopShowsSignedInsufficientProjectionButNeverAcceptsANegativeActualWallet()
        {
            var intent = new JObject { ["itemId"] = "refine_core", ["quantity"] = 2 };
            var view = PracticeEquipmentPreview.Parse(Utf8(EquipmentShopJson(true, false)), 3, "bossExchange", null, intent);
            Assert.That(view.CanCommit, Is.False); Assert.That(view.Cost["boss_token"], Is.EqualTo(120)); Assert.That(view.ShopAfter!.BossTokens, Is.EqualTo(-80));
            var bad = EquipmentProfileJson(); bad["resources"]!["boss_token"] = -80L;
            Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(bad), Account));
        }
        [Test]
        public void EquipmentExchangeRetryAcrossAWeekResetKeepsOneHistoricalPurchaseAndInstallsFreshProfile()
        {
            EquipmentFixture(); byte[]? receipt = null; int writes = 0;
            post = seen =>
            {
                if (receipt == null)
                {
                    writes++; var reply = EquipmentShopJson(false, requestId: JObject.Parse(seen.Text)["requestId"]!.Value<string>()!); receipt = Utf8(reply);
                    progressionProfile = (JObject)reply["profile"]!.DeepClone();
                    foreach (string field in new[] { "bossShop", "weeklyBoss" }) { progressionProfile[field]!["weekKey"] = "2026-10-26"; progressionProfile[field]!["resetsAtMs"] = 1793563200000L; }
                    progressionProfile["bossShop"]!["items"]![0]!["bought"] = 0; progressionProfile["bossShop"]!["items"]![0]!["remaining"] = 2;
                    return new Reply(200, receipt) { Drop = true };
                }
                return new Reply(200, receipt);
            };
            using (var first = Session())
            {
                Wait(first.RefreshProgressionAsync()); Assert.That(Wait(first.CommitEquipmentAsync(3, "bossExchange", null, new JObject { ["itemId"] = "refine_core", ["quantity"] = 2 })).Status, Is.EqualTo(MutationStatus.Pending));
            }
            using (var next = Session())
            {
                var result = Wait(next.RetryEquipmentPendingAsync()); Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.RefreshCode, Is.Null);
                Assert.That(result.Receipt!.Change.ShopAfter!.WeekKey, Is.EqualTo("2026-10-19")); Assert.That(result.Progression!.BossShop!.WeekKey, Is.EqualTo("2026-10-26"));
                Assert.That(result.Progression.Resources!["boss_token"], Is.Zero); Assert.That(result.Progression.Resources["refine_core"], Is.EqualTo(2));
                Assert.That(result.Progression.BossShop.Items[0].Bought, Is.Zero, "cached receipt never replaces the current week's view");
            }
            Assert.That(writes, Is.EqualTo(1)); Assert.That(host.Posts.Select(p => p.Text).Distinct().Count(), Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
        }
        [TestCase("characterAscend")]
        [TestCase("skillUp")]
        [TestCase("gearCraft")]
        [TestCase("gearEnhance")]
        [TestCase("weaponLevelUp")]
        [TestCase("weaponAscend")]
        public void EquipmentCommitsValidateExactReceiptsAndInstallOnlyFreshCurrentProfiles(string kind)
        {
            EquipmentFixture(); var intent = EquipmentIntentJson(kind); string target = kind.StartsWith("weapon", StringComparison.Ordinal) ? EquipmentWeapon : "nia";
            post = seen =>
            {
                var reply = EquipmentChange(progressionProfile, kind, false, JObject.Parse(seen.Text)["requestId"]!.Value<string>()!);
                progressionProfile = (JObject)reply["profile"]!.DeepClone(); return Reply.Json(200, reply);
            };
            using (var session = Session())
            {
                Wait(session.RefreshProgressionAsync()); var result = Wait(session.CommitEquipmentAsync(3, kind, target, intent));
                Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.RefreshCode, Is.Null); Assert.That(result.Receipt!.Kind, Is.EqualTo(kind)); Assert.That(result.Progression!.Revision, Is.EqualTo(4));
                Assert.That(Outbox().Load(), Is.Null); Assert.That(session.ProgressionNeedsRefresh, Is.False);
            }
        }
        [TestCase(false, MutationStatus.Pending)]
        [TestCase(true, MutationStatus.NotCommitted)]
        public void EquipmentNoEligibleWeaponExtendsOnlyFreshV3OwnedDepartureRefusals(bool v3, MutationStatus expected)
        {
            if (v3) EquipmentFixture(); else GrowthFixture(); post = _ => Error(422, "NO_ELIGIBLE_WEAPON");
            using (var session = Session())
            {
                Wait(session.RefreshProgressionAsync()); var result = Wait(session.LaunchExpeditionAsync(3, "story", 1, new[] { "nia", "harin", "rien" }, true));
                Assert.That(result.Status, Is.EqualTo(expected)); Assert.That(result.Code, Is.EqualTo("NO_ELIGIBLE_WEAPON"));
            }
        }
        [TestCase(200)]
        [TestCase(201)]
        public void EquipmentACommittedReplyWithALaggingOrNon200ProfileNeverBecomesCurrent(int status)
        {
            EquipmentFixture(); post = seen => Reply.Json(status, EquipmentChange(progressionProfile, "weaponRefine", false, JObject.Parse(seen.Text)["requestId"]!.Value<string>()!));
            using (var session = Session())
            {
                Wait(session.RefreshProgressionAsync()); var result = Wait(session.CommitEquipmentAsync(3, "weaponRefine", EquipmentWeapon));
                Assert.That(result.Status, Is.EqualTo(status == 200 ? MutationStatus.Committed : MutationStatus.Pending));
                if (status == 200) { Assert.That(result.RefreshCode, Is.EqualTo(Codes.RollbackRefused)); Assert.That(session.Progression!.Revision, Is.EqualTo(3)); Assert.That(session.ProgressionNeedsRefresh, Is.True); }
                Assert.That(Outbox().Load() != null, Is.EqualTo(status != 200));
            }
        }
        void WeeklyFixture()
        {
            EquipmentFixture(true); growthEncounters[GrowthEncounter] = WeeklyEncounterJson(); var fallback = additionalReply;
            additionalReply = seen =>
            {
                if (seen.Method == "GET" && seen.Path == ProgressionPath)
                {
                    var json = (JObject)progressionProfile.DeepClone();
                    json["activeEncounter"] = growthActive == null ? (JToken)JValue.CreateNull() : new JObject { ["encounterId"] = growthActive, ["kind"] = "weeklyBoss", ["bossId"] = "b_bell", ["difficulty"] = 5, ["practice"] = false,
                        ["phase"] = growthEncounters[growthActive]["phase"]!.DeepClone(), ["version"] = growthEncounters[growthActive]["version"]!.DeepClone() };
                    return Reply.Json(200, json);
                }
                return fallback!(seen);
            };
        }
        [Test]
        public void WeeklyLethalLostReplyAcrossResetReturnsOneOriginalWeekWithoutReplayingEvents()
        {
            WeeklyFixture(); byte[]? receipt = null; int writes = 0;
            post = seen =>
            {
                if (receipt == null)
                {
                    writes++; var ended = WeeklyEncounterJson("VICTORY", version: 4, revision: 4); ended["events"] = Events(); receipt = Utf8(ended);
                    growthEncounters[GrowthEncounter] = ended; growthActive = null; progressionProfile["revision"] = 4L;
                    progressionProfile["resources"]!["boss_token"] = 160L; progressionProfile["resources"]!["core"] = 2L;
                    foreach (string field in new[] { "bossShop", "weeklyBoss" }) { progressionProfile[field]!["weekKey"] = "2026-10-26"; progressionProfile[field]!["resetsAtMs"] = 1793563200000L; }
                    return new Reply(200, receipt) { Drop = true };
                }
                return new Reply(200, receipt);
            };
            using (var session = Session())
            {
                Assert.That(Wait(session.RefreshEncounterAsync(GrowthEncounter)).Code, Is.EqualTo(Codes.Ok));
                Assert.That(Wait(session.EncounterCommandAsync(session.Encounter!.Context, "end")).Status, Is.EqualTo(MutationStatus.Pending));
            }
            using (var retry = Session())
            {
                var result = Wait(retry.RetryGrowthPendingAsync()); Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(result.RefreshCode, Is.Null);
                Assert.That(result.Receipt!.Encounter!.Settlement!.WeekKey, Is.EqualTo("2026-10-19")); Assert.That(result.Progression!.WeeklyBoss!.WeekKey, Is.EqualTo("2026-10-26"));
                Assert.That(result.PresentableEvents(), Is.Null); Assert.That(retry.CanMutateEncounter, Is.False); Assert.That(result.Progression.Resources!["boss_token"], Is.EqualTo(160));
            }
            Assert.That(writes, Is.EqualTo(1)); Assert.That(host.Posts.Select(s => s.Text).Distinct().Count(), Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
        }
        [Test]
        public void WeeklyMetadataCannotChangeUnderAnOtherwiseMatchingJournalReply()
        {
            WeeklyFixture(); post = _ =>
            {
                var reply = WeeklyEncounterJson(version: 4, revision: 4); reply["events"] = Events(); reply["practice"] = true; return Reply.Json(200, reply);
            };
            using (var session = Session())
            {
                Wait(session.RefreshEncounterAsync(GrowthEncounter)); var result = Wait(session.EncounterCommandAsync(session.Encounter!.Context, "end"));
                Assert.That(result.Status, Is.EqualTo(MutationStatus.Pending)); Assert.That(result.Receipt, Is.Null); Assert.That(Outbox().Load(), Is.Not.Null);
            }
        }
        [Test]
        public void EquipmentResetClockIsPublicOnlyAtItsExplicitNewSchemaPaths()
        {
            Assert.DoesNotThrow(() => PracticeProgressionSnapshot.Parse(Utf8(EquipmentProfileJson()), Account));
            var intent = new JObject { ["itemId"] = "refine_core", ["quantity"] = 2 };
            Assert.DoesNotThrow(() => PracticeEquipmentPreview.Parse(Utf8(EquipmentShopJson(true)), 3, "bossExchange", null, intent));
            foreach (var json in new[] { RunJson(world, 3), RecruitmentJson(), GrowthEncounterJson() })
            {
                json["resetsAtMs"] = 1000L;
                Assert.That(Assert.Throws<PracticeProtocolException>(() => PracticePublicJson.ParseObject(Utf8(json)))!.Code, Is.EqualTo(PracticeProtocolException.PrivateField));
            }
            var wrong = EquipmentProfileJson(); wrong["characters"]![0]!["resetsAtMs"] = 1000L;
            Assert.That(Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(wrong), Account))!.Code, Is.EqualTo(PracticeProtocolException.PrivateField));
            wrong = EquipmentChange(EquipmentProfileJson(), "weaponRefine", true); wrong["before"]!["resetsAtMs"] = 1000L;
            Assert.That(Assert.Throws<PracticeProtocolException>(() => PracticeEquipmentPreview.Parse(Utf8(wrong), 3, "weaponRefine", EquipmentWeapon, new JObject()))!.Code, Is.EqualTo(PracticeProtocolException.PrivateField));
        }
        [Test]
        public void EquipmentCopyRefinementHasOneInstanceAndNeverAllowsABoughtEffectiveRank()
        {
            var json = EquipmentProfileJson(); var weapon = EquipmentWeaponRow(json); weapon["rarity"] = "SSR"; weapon["copies"] = 6; weapon["refineMode"] = "copy"; weapon["refine"] = 5;
            var character = EquipmentCharacter(json); character["copies"] = 7; character["res"] = 6; character["skillLv"] = 10; character["ultLv"] = 10;
            character["effectiveSkillLv"] = 12; character["effectiveUltLv"] = 12; character["stats"]!["skillLv"] = 10; character["stats"]!["ultLv"] = 10;
            var profile = PracticeProgressionSnapshot.Parse(Utf8(json), Account);
            Assert.That(profile.Weapons.Count(w => w.InstanceId == EquipmentWeapon), Is.EqualTo(1)); Assert.That(profile.Weapons[0].Refine, Is.EqualTo(5));
            Assert.That(profile.Characters[0].SkillLv, Is.EqualTo(10)); Assert.That(profile.Characters[0].EffectiveSkillLv, Is.EqualTo(12));
            weapon["refine"] = 6; Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(json), Account)); weapon["refine"] = 5;
            character["skillLv"] = 11; Assert.Throws<PracticeProtocolException>(() => PracticeProgressionSnapshot.Parse(Utf8(json), Account));
        }
        [Test]
        public void EquipmentTransferNamesBothDisplacedAssignmentsAndNeverInventsASwap()
        {
            var json = EquipmentEquipJson(EquipmentProfileJson(), true);
            json["target"]!["charId"] = "harin"; json["before"]!["charId"] = "harin"; json["before"]!["charWeaponInstanceId"] = "other-owned-device";
            json["after"]!["charId"] = "harin"; json["after"]!["equippedBy"] = "harin"; json["after"]!["charWeaponInstanceId"] = EquipmentWeapon;
            json["after"]!["displaced"]!["instanceId"] = "other-owned-device";
            var intent = new JObject { ["charId"] = "harin" };
            var view = PracticeEquipmentPreview.Parse(Utf8(json), 3, "weaponEquip", EquipmentWeapon, intent);
            Assert.That(view.AssignmentBefore!.EquippedBy, Is.EqualTo("nia")); Assert.That(view.AssignmentAfter!.DisplacedCharId, Is.EqualTo("nia"));
            Assert.That(view.AssignmentAfter.DisplacedInstanceId, Is.EqualTo("other-owned-device")); Assert.That(view.Cost, Is.Empty);
            json["after"]!["displaced"]!["charId"] = "foreign";
            Assert.Throws<PracticeProtocolException>(() => PracticeEquipmentPreview.Parse(Utf8(json), 3, "weaponEquip", EquipmentWeapon, intent));
        }
        [TestCase("characterAscend", "ACCOUNT_LEVEL_REQUIRED")]
        [TestCase("skillUp", "SKILL_CAP")]
        [TestCase("gearCraft", "GEAR_ALREADY_CRAFTED")]
        [TestCase("gearEnhance", "GEAR_NOT_CRAFTED")]
        [TestCase("weaponLevelUp", "LEVEL_CAP")]
        [TestCase("weaponAscend", "ASCENSION_LEVEL_REQUIRED")]
        [TestCase("weaponEquip", "WEAPON_TYPE_MISMATCH")]
        [TestCase("bossExchange", "EXCHANGE_LIMIT")]
        public void EquipmentEachActionClearsOnlyItsKnownDefinitiveRefusal(string kind, string code)
        {
            EquipmentFixture(); post = _ => Error(422, code);
            var intent = kind == "bossExchange" ? new JObject { ["itemId"] = "polish", ["quantity"] = 20 }
                : kind == "weaponEquip" ? new JObject { ["charId"] = "harin" } : EquipmentIntentJson(kind);
            string? target = kind == "bossExchange" ? null : kind.StartsWith("weapon", StringComparison.Ordinal) ? EquipmentWeapon : "nia";
            using (var session = Session())
            {
                Wait(session.RefreshProgressionAsync()); var result = Wait(session.CommitEquipmentAsync(3, kind, target, intent));
                Assert.That(result.Status, Is.EqualTo(MutationStatus.NotCommitted)); Assert.That(result.Code, Is.EqualTo(code)); Assert.That(Outbox().Load(), Is.Null);
            }
        }
        [Test]
        public void EquipmentAttemptCapturesOneCredentialAndClonesCallerIntent()
        {
            EquipmentFixture(); int captures = 0; string supplied = Credential; source = () => { captures++; return supplied; };
            var args = new JObject { ["targetLevel"] = 2 };
            post = seen =>
            {
                Assert.That(JObject.Parse(seen.Text)["targetLevel"]!.Value<int>(), Is.EqualTo(2));
                var reply = EquipmentChange(progressionProfile, "weaponLevelUp", false, JObject.Parse(seen.Text)["requestId"]!.Value<string>()!); progressionProfile = (JObject)reply["profile"]!.DeepClone(); return Reply.Json(200, reply);
            };
            using (var session = Session())
            {
                Wait(session.RefreshProgressionAsync()); captures = 0; var callback = additionalReply;
                additionalReply = seen => { supplied = RotatedCredential; args["targetLevel"] = 3; return callback!(seen); };
                var result = Wait(session.CommitEquipmentAsync(3, "weaponLevelUp", EquipmentWeapon, args));
                Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed)); Assert.That(captures, Is.EqualTo(1));
            }
            Assert.That(host.Posts.Length, Is.EqualTo(1));
        }
        [TestCase("NO_ELIGIBLE_WEAPON", MutationStatus.NotCommitted)]
        [TestCase("UNKNOWN_TIER", MutationStatus.NotCommitted)]
        [TestCase("IDEMPOTENCY_CONFLICT", MutationStatus.Pending)]
        public void WeeklyStartRefusalHasItsOwnExactNoCommitTable(string code, MutationStatus expected)
        {
            EquipmentFixture(); post = _ => Error(code == "IDEMPOTENCY_CONFLICT" ? 409 : 422, code);
            using (var session = Session())
            {
                Wait(session.RefreshProgressionAsync()); var result = Wait(session.StartWeeklyBossAsync(3, "b_bell", 1, true, new[] { "nia", "harin", "rien" }));
                Assert.That(result.Status, Is.EqualTo(expected)); Assert.That(Outbox().Load() != null, Is.EqualTo(expected == MutationStatus.Pending));
            }
        }
    }
}
