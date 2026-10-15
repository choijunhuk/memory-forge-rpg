using System.Collections.Generic;
using System.Linq;
using MemoryForge.Core;
using NUnit.Framework;
using UnityEngine;

namespace MemoryForge.PlayModeTests
{
    public class MemoryRulesPlayModeTests
    {
        // Authored Sera starters, com_guard, sera_phase, c_echo and current memory costs from data/game_data.json.
        // Only fields MemoryV2 reads are typed; this checks Unity runtime execution, not all-content import or Run replay.
        static MemoryRulesData SeraRules()
        {
            var data = new MemoryRulesData { CapacityBase = 6, Costs = new MemoryCostConfig { AddB = 1, AddA = 2, AddS = 3, AddCommon = 1,
                Upgrade = 1, PartCommon = 1, PartRare = 2, Evolution = 2, Remove = 1 } };
            data.StartingCommon.Add("sera", "com_guard");
            data.AscensionCapacity.Add(new MemoryAscensionCapacity { Asc = 2, Add = 1 });
            data.AscensionCapacity.Add(new MemoryAscensionCapacity { Asc = 4, Add = 1 });
            data.Battle.Characters.Add(new BattleCharacterDef { Id = "sera", Attr = "volt" });
            foreach (var (id, rarity, start) in new[] { ("sera_round", "B", true), ("sera_cover", "B", true), ("sera_aim", "B", true),
                ("sera_timed", "A", true), ("sera_phase", "S", false) })
                data.Battle.Cards.Add(new BattleCardDef { Id = id, Owner = "sera", Rarity = rarity, Start = start ? true : (bool?)null });
            data.Battle.Cards.Add(new BattleCardDef { Id = "com_guard", Owner = "common", Rarity = "B" });
            data.Battle.Parts.Add(new BattlePartDef { Id = "c_echo", Slot = "circuit", Rarity = "rare" });
            return data;
        }

        const string Baseline = "{\"id\":\"sera_round\",\"key\":\"base:sera:0\"},{\"id\":\"sera_cover\",\"key\":\"base:sera:1\"},"
            + "{\"id\":\"sera_aim\",\"key\":\"base:sera:2\"},{\"id\":\"sera_timed\",\"key\":\"base:sera:3\"},"
            + "{\"assigned\":\"sera\",\"id\":\"com_guard\",\"key\":\"base:sera:4\"}";

        static List<object> Kit(MemoryRulesData data) => MemoryV2.Baseline(data, "sera").Select(c => (object)c.ToJsonGraph()).ToList();
        static Dictionary<string, object> Caps(int sera) => new Dictionary<string, object> { ["sera"] = sera };

        [Test]
        public void CompleteResultCostDecidesSelectedChangesWithDetachedGraphs()
        {
            Assert.That(Application.isPlaying, Is.True);
            var data = SeraRules();
            Assert.That(CanonicalState.Serialize(Kit(data)), Is.EqualTo("[" + Baseline + "]"));
            Assert.That(MemoryV2.Cost(data, "sera", Kit(data)), Is.EqualTo(0));

            var before = new Dictionary<string, object> { ["sera"] = Kit(data) };
            var afterRows = Kit(data);
            ((Dictionary<string, object>)afterRows[0])["plus"] = true;
            afterRows.Add(new Dictionary<string, object> { ["key"] = "run:phase", ["id"] = "sera_phase",
                ["mods"] = new Dictionary<string, object> { ["c"] = "c_echo" } });
            var after = new Dictionary<string, object> { ["sera"] = afterRows };
            string beforeJson = CanonicalState.Serialize(before), afterJson = CanonicalState.Serialize(after);

            var changes = MemoryV2.ListChanges(data, before, after);
            var ids = changes.Select(c => c.Id).ToList();
            Assert.That(ids, Is.EqualTo(new[] { "delta:sera:base%3Asera%3A0:upgrade", "delta:sera:run%3Aphase:add" }));
            Assert.That(changes[0].Value, Is.EqualTo(true));
            // Upgrade1 + S add3 + rare circuit2 on the complete result.
            Assert.That(MemoryV2.Cost(data, "sera", afterRows), Is.EqualTo(6));

            Assert.That(Assert.Throws<RuleException>(() => MemoryV2.Apply(data, before, after, ids, Caps(5))).Code, Is.EqualTo("OVER_CAPACITY"));
            var preview = MemoryV2.Apply(data, before, after, ids, Caps(5), new Dictionary<string, object> { ["preview"] = true });
            Assert.That(preview.Error, Is.EqualTo("OVER_CAPACITY"));
            Assert.That(preview.Used["sera"], Is.EqualTo(6));

            var committed = MemoryV2.Apply(data, before, after, ids, Caps(6));
            string saved = "{\"sera\":[{\"id\":\"sera_round\",\"key\":\"base:sera:0\",\"plus\":true}," + Baseline.Substring(Baseline.IndexOf("{\"id\":\"sera_cover\""))
                + ",{\"id\":\"sera_phase\",\"key\":\"run:phase\",\"mods\":{\"c\":\"c_echo\"}}]}";
            Assert.That(committed.Error, Is.Null);
            Assert.That(committed.Used["sera"], Is.EqualTo(6));
            Assert.That(CanonicalState.Serialize(committed.Memory.ToJsonGraph()), Is.EqualTo(saved));
            Assert.That(committed.Memory.Cards("sera").Select(c => c.Key), Is.EqualTo(new[] { "base:sera:0", "base:sera:1", "base:sera:2", "base:sera:3", "base:sera:4", "run:phase" }));

            Assert.That(Assert.Throws<RuleException>(() => MemoryV2.Recover(data, before, after, ids, Caps(6))).Code, Is.EqualTo("DEFEAT_KEEP_ONE"));
            var recovered = MemoryV2.Recover(data, before, after, new List<object> { ids[0] }, Caps(6));
            Assert.That(recovered.Used["sera"], Is.EqualTo(1));
            Assert.That(recovered.Memory.Cards("sera").Count, Is.EqualTo(5));

            // Failed commits never touched inputs; later input/result mutations never reach the returned memory.
            Assert.That(CanonicalState.Serialize(before), Is.EqualTo(beforeJson));
            Assert.That(CanonicalState.Serialize(after), Is.EqualTo(afterJson));
            ((Dictionary<string, object>)afterRows[5])["id"] = "sera_round";
            var graph = committed.Memory.ToJsonGraph();
            ((Dictionary<string, object>)((List<object>)graph["sera"])[0])["plus"] = false;
            Assert.That(CanonicalState.Serialize(committed.Memory.ToJsonGraph()), Is.EqualTo(saved));
        }

        [TestCase(false, "INVALID_UPGRADE")]
        [TestCase(true, "IDENTITY_REQUIRED")]
        public void MigrationArrayRowsDoNotHideEarlierValidationErrors(bool duplicateKey, string expectedCode)
        {
            Assert.That(Application.isPlaying, Is.True);
            var first = new Dictionary<string, object> { ["id"] = "sera_round" };
            if (duplicateKey) first["key"] = "legacy:sera:1"; else first["plus"] = 1;
            var legacy = new Dictionary<string, object> { ["sera"] = new List<object> { first, new List<object>() } };
            string original = CanonicalState.Serialize(legacy);
            var migrated = MemoryV2.Migrate(SeraRules(), legacy, Caps(6));
            Assert.That(migrated.Quarantine["sera"].Code, Is.EqualTo(expectedCode));
            Assert.That(migrated.Memory.CharacterIds, Is.Empty);
            Assert.That(CanonicalState.Serialize(migrated.LegacyBackup), Is.EqualTo(original));
            Assert.That(CanonicalState.Serialize(legacy), Is.EqualTo(original));
        }

        [Test]
        public void MigrationQuarantinesAnOwnProtoOwnerWithItsCompleteOriginal()
        {
            Assert.That(Application.isPlaying, Is.True);
            var data = SeraRules();
            var unknown = new Dictionary<string, object> { ["id"] = "legacy_unknown", ["note"] = "keep complete original" };
            var legacy = new Dictionary<string, object>
            {
                ["__proto__"] = new List<object> { unknown },
                ["sera"] = new List<object> { "sera_round", "sera_cover", "sera_aim", "sera_timed", "com_guard" }
            };
            const string original = "[{\"id\":\"legacy_unknown\",\"note\":\"keep complete original\"}]";

            var migrated = MemoryV2.Migrate(data, legacy, Caps(6));
            Assert.That(migrated.Quarantine.Keys, Is.EqualTo(new[] { "__proto__" }));
            Assert.That(migrated.Quarantine["__proto__"].Code, Is.EqualTo("INVALID_SNAPSHOT"));
            Assert.That(migrated.Memory.CharacterIds, Is.EqualTo(new[] { "sera" }));
            Assert.That(CanonicalState.Serialize(migrated.ToJsonGraph()), Is.EqualTo("{\"history\":[],\"legacyBackup\":{\"__proto__\":" + original
                + ",\"sera\":[\"sera_round\",\"sera_cover\",\"sera_aim\",\"sera_timed\",\"com_guard\"]},\"memory\":{\"sera\":[" + Baseline
                + "]},\"quarantine\":{\"__proto__\":{\"code\":\"INVALID_SNAPSHOT\",\"original\":" + original
                + "}},\"receipts\":{},\"schema\":\"mf-memory-v2\",\"version\":0}"));

            // Input and returned copies are detached from the stored quarantine/backup.
            unknown["note"] = "changed";
            ((List<object>)migrated.Quarantine["__proto__"].Original).Clear();
            ((Dictionary<string, object>)migrated.LegacyBackup).Clear();
            Assert.That(CanonicalState.Serialize(migrated.Quarantine["__proto__"].Original), Is.EqualTo(original));
            Assert.That(CanonicalState.Serialize(((Dictionary<string, object>)migrated.LegacyBackup)["__proto__"]), Is.EqualTo(original));

            Assert.That(MemoryV2.Migrate(data, legacy, Caps(-1)).Quarantine["sera"].Code, Is.EqualTo("OVER_CAPACITY"));
        }
    }
}
