// §7/8/16.2: expedition creation inputs; memory metadata uses the reviewed M0 graph boundary.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MemoryForge.Core
{
    internal static class RunValues
    {
        internal static Dictionary<string, object?> Fields(object? value) => MemoryJson.Copy(value) as Dictionary<string, object?> ?? throw new MemoryBoundaryException("TypeError", "Expected expedition object");
        internal static bool TryInteger(object? value, out long number)
        {
            switch (value)
            {
                case int n: number = n; return true;
                case uint n: number = n; return true;
                case long n when n >= -CanonicalState.MaxSafeInteger && n <= CanonicalState.MaxSafeInteger: number = n; return true;
                case double n when MemoryJson.Integer(value) && n >= -CanonicalState.MaxSafeInteger && n <= CanonicalState.MaxSafeInteger: number = (long)n; return true;
                default: number = 0; return false;
            }
        }
        internal static int? OptionalInt(object? value)
        {
            if (value == null || ReferenceEquals(value, MemoryJson.Missing)) return null;
            if (!TryInteger(value, out long n) || n < int.MinValue || n > int.MaxValue) throw new RuleException("RUN_NUMBER_OUT_OF_RANGE");
            return (int)n;
        }
        internal static string? Text(object? value) => value as string;
    }

    public sealed class RunPartyEntry
    {
        internal readonly Dictionary<string, object?> Fields;
        public RunPartyEntry() { Fields = new Dictionary<string, object?>(StringComparer.Ordinal); }
        RunPartyEntry(object? source) { Fields = RunValues.Fields(source); }
        public static RunPartyEntry FromJsonGraph(object? source) => new RunPartyEntry(source);
        internal RunPartyEntry Copy() => new RunPartyEntry(Fields);
        public string CharId { get => RunValues.Text(MemoryJson.Get(Fields, "charId")) ?? ""; set => Fields["charId"] = value; }
        public string? WeaponId { get => RunValues.Text(MemoryJson.Get(Fields, "weaponId")); set => Fields["weaponId"] = value; }
        public int? Res { get => Number("res"); set => Fields["res"] = value; }
        public int? Refine { get => Number("refine"); set => Fields["refine"] = value; }
        public int? Level { get => Number("level"); set => Fields["level"] = value; }
        public int? Asc { get => Number("asc"); set => Fields["asc"] = value; }
        public int? WeaponLevel { get => Number("weaponLevel"); set => Fields["weaponLevel"] = value; }
        public int? Armor { get => Number("armor"); set => Fields["armor"] = value; }
        public int? Accessory { get => Number("accessory"); set => Fields["accessory"] = value; }
        public int? SkillLv { get => Number("skillLv"); set => Fields["skillLv"] = value; }
        public int? UltLv { get => Number("ultLv"); set => Fields["ultLv"] = value; }
        public int? Power4 { get => Number("power4"); set => Fields["power4"] = value; }
        public int? Hp { get => Number("hp"); set => Fields["hp"] = value; }
        public int? GearBps { get => Number("gearBps"); set => Fields["gearBps"] = value; }
        int? Number(string key) => RunValues.OptionalInt(MemoryJson.Get(Fields, key));
        public Dictionary<string, object?> ToJsonGraph() => RunValues.Fields(Fields);
    }

    public sealed class RunCardSpec
    {
        internal readonly Dictionary<string, object?> Fields;
        public RunCardSpec() { Fields = new Dictionary<string, object?>(StringComparer.Ordinal); }
        RunCardSpec(object? source) { Fields = RunValues.Fields(source); }
        public static RunCardSpec FromJsonGraph(object? source) => new RunCardSpec(source);
        internal RunCardSpec Copy() => new RunCardSpec(Fields);
        public string? Id { get => RunValues.Text(MemoryJson.Get(Fields, "id")); set => Fields["id"] = value; }
        public string? Key { get => RunValues.Text(MemoryJson.Get(Fields, "key")); set => Fields["key"] = value; }
        public string? Owner { get => RunValues.Text(MemoryJson.Get(Fields, "owner")); set => Fields["owner"] = value; }
        public string? Assigned { get => RunValues.Text(MemoryJson.Get(Fields, "assigned")); set => Fields["assigned"] = value; }
        public string? Wielder { get => RunValues.Text(MemoryJson.Get(Fields, "wielder")); set => Fields["wielder"] = value; }
        public bool Weapon { get => MemoryJson.Truthy(MemoryJson.Get(Fields, "weapon")); set => Fields["weapon"] = value; }
        public Dictionary<string, object?> ToJsonGraph() => RunValues.Fields(Fields);
    }
    public sealed class RunDifficulty
    {
        internal readonly Dictionary<string, object?> Fields;
        public RunDifficulty() { Fields = new Dictionary<string, object?>(StringComparer.Ordinal); }
        RunDifficulty(object? source) { Fields = RunValues.Fields(source); }
        public static RunDifficulty FromJsonGraph(object? source) => new RunDifficulty(source);
        public int? Hp { get => RunValues.OptionalInt(MemoryJson.Get(Fields, "hp")); set => Fields["hp"] = value; }
        public int? Damage { get => RunValues.OptionalInt(MemoryJson.Get(Fields, "dmg")); set => Fields["dmg"] = value; }
        internal RunDifficulty Copy() => new RunDifficulty(Fields);
        public Dictionary<string, object?> ToJsonGraph() => RunValues.Fields(Fields);
    }
    public sealed class RunSetup
    {
        public List<RunPartyEntry> Party = new List<RunPartyEntry>();
        public object? Memory, Bosses, RunId, MemoryVersion;
        public List<string> Relics = new List<string>();
        public long? Seed;
        public string? Length, MemoryPolicy;
        public List<int>? Zones;
        public int? StageLevel;
        public RunDifficulty? Difficulty;
        public bool? UseMemory;
    }

    public sealed class RunCharacterMetadata
    {
        public string Id = "", WeaponType = "";
        public Dictionary<string, object?> ToJsonGraph() => MemoryJson.Object("id", Id, "weapon", WeaponType);
    }
    public sealed class RunEncounterDefinition
    {
        public string Zone = "";
        public List<List<string>> Normal = new List<List<string>>(), Elite = new List<List<string>>(), Boss = new List<List<string>>();
        public Dictionary<string, object?> ToJsonGraph() => MemoryJson.Object("normal", Normal.Select(r => new List<string>(r)).ToList(), "elite", Elite.Select(r => new List<string>(r)).ToList(), "boss", Boss.Select(r => new List<string>(r)).ToList());
    }
    public sealed class RunEventEffects
    {
        public int? Coins, Hp, Heal, MaxHp, MaxHpPct, NextApBonus, Part, PartRare, Variation, Growth, CardCopy, CardRemove;
        public string? Curse, Relic, Card;
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = MemoryJson.Object();
            BattleGraph.Optional(graph, "coins", Coins); BattleGraph.Optional(graph, "hp", Hp); BattleGraph.Optional(graph, "heal", Heal);
            BattleGraph.Optional(graph, "maxHp", MaxHp); BattleGraph.Optional(graph, "maxHpPct", MaxHpPct); BattleGraph.Optional(graph, "nextApBonus", NextApBonus);
            BattleGraph.Optional(graph, "part", Part); BattleGraph.Optional(graph, "partRare", PartRare); BattleGraph.Optional(graph, "variation", Variation);
            BattleGraph.Optional(graph, "growth", Growth); BattleGraph.Optional(graph, "cardCopy", CardCopy); BattleGraph.Optional(graph, "cardRemove", CardRemove);
            BattleGraph.Optional(graph, "curse", Curse); BattleGraph.Optional(graph, "relic", Relic); BattleGraph.Optional(graph, "card", Card); return graph;
        }
    }
    public sealed class RunEventChoice
    {
        public string Label = "", Text = "";
        public int? RequiredCoins;
        public RunEventEffects Effects = new RunEventEffects();
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = MemoryJson.Object("label", Label, "text", Text, "fx", Effects.ToJsonGraph());
            if (RequiredCoins.HasValue) graph.Add("need", MemoryJson.Object("coins", RequiredCoins.Value)); return graph;
        }
    }
    public sealed class RunEventDefinition
    {
        public string Id = "", Name = "", Text = "";
        public List<RunEventChoice> Choices = new List<RunEventChoice>();
        public Dictionary<string, object?> ToJsonGraph() => MemoryJson.Object("id", Id, "name", Name, "text", Text, "choices", Choices.Select(c => c.ToJsonGraph()).ToList());
    }
    public sealed class RunData
    {
        public MemoryRulesData Memory = new MemoryRulesData();
        public StatsConfig Stats = new StatsConfig();
        public List<RunCharacterMetadata> Characters = new List<RunCharacterMetadata>();
        public List<RunEncounterDefinition> Encounters = new List<RunEncounterDefinition>();
        public List<RunEventDefinition> Events = new List<RunEventDefinition>();
        internal Dictionary<string, RunCharacterMetadata> CharacterIndex = new Dictionary<string, RunCharacterMetadata>(StringComparer.Ordinal);
        internal Dictionary<string, RunEncounterDefinition> EncounterIndex = new Dictionary<string, RunEncounterDefinition>(StringComparer.Ordinal);
        internal void Index()
        {
            Memory.Battle.Index(); CharacterIndex = Characters.ToDictionary(c => c.Id, StringComparer.Ordinal); EncounterIndex = Encounters.ToDictionary(c => c.Zone, StringComparer.Ordinal);
        }
    }
}
