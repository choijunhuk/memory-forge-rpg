// Host adapter for R0; reuses the reviewed memory graph/UTF-16 boundary and existing Stats loader.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MemoryForge.Core;

static class RunDataLoader
{
    public static RunData Load(string root)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
        var json = document.RootElement;
        var data = new RunData { Memory = MemoryV2DataLoader.Load(root), Stats = StatsDataLoader.Load(json.GetProperty("progression")) };
        foreach (var character in json.GetProperty("characters").EnumerateArray())
            data.Characters.Add(new RunCharacterMetadata { Id = Text(character.GetProperty("id")), WeaponType = Text(character.GetProperty("weapon")) });
        foreach (var zone in json.GetProperty("encounters").EnumerateObject())
        {
            BattleDataLoader.Fields(zone.Value, "normal", "elite", "boss");
            List<List<string>> Rows(string key) => zone.Value.GetProperty(key).EnumerateArray().Select(row => row.EnumerateArray().Select(Text).ToList()).ToList();
            data.Encounters.Add(new RunEncounterDefinition { Zone = BattleDataLoader.Name(zone), Normal = Rows("normal"), Elite = Rows("elite"), Boss = Rows("boss") });
        }
        foreach (var item in json.GetProperty("events").EnumerateArray())
        {
            BattleDataLoader.Fields(item, "id", "name", "text", "choices");
            var definition = new RunEventDefinition { Id = Text(item.GetProperty("id")), Name = Text(item.GetProperty("name")), Text = Text(item.GetProperty("text")) };
            foreach (var choice in item.GetProperty("choices").EnumerateArray())
            {
                BattleDataLoader.Fields(choice, "label", "text", "fx", "need");
                var row = new RunEventChoice { Label = Text(choice.GetProperty("label")), Text = Text(choice.GetProperty("text")), Effects = Effects(choice.GetProperty("fx")) };
                if (choice.TryGetProperty("need", out var need)) { BattleDataLoader.Fields(need, "coins"); row.RequiredCoins = Int(need.GetProperty("coins")); }
                definition.Choices.Add(row);
            }
            data.Events.Add(definition);
        }
        return data;
    }
    static RunEventEffects Effects(JsonElement json)
    {
        BattleDataLoader.Fields(json, "coins", "hp", "heal", "maxHp", "maxHpPct", "nextApBonus", "curse", "relic", "card", "part", "partRare", "variation", "growth", "cardCopy", "cardRemove");
        int? Number(string key) => json.TryGetProperty(key, out var value) ? Int(value) : (int?)null;
        string? String(string key) => json.TryGetProperty(key, out var value) ? Text(value) : null;
        return new RunEventEffects { Coins = Number("coins"), Hp = Number("hp"), Heal = Number("heal"), MaxHp = Number("maxHp"), MaxHpPct = Number("maxHpPct"), NextApBonus = Number("nextApBonus"),
            Curse = String("curse"), Relic = String("relic"), Card = String("card"), Part = Number("part"), PartRare = Number("partRare"), Variation = Number("variation"), Growth = Number("growth"), CardCopy = Number("cardCopy"), CardRemove = Number("cardRemove") };
    }

    public static RunSetup Setup(JsonElement json)
    {
        var setup = new RunSetup();
        foreach (var party in json.GetProperty("party").EnumerateArray())
        {
            var fields = MemoryV2DataLoader.Graph(party) as Dictionary<string, object?> ?? throw new InvalidDataException("Expected expedition party object");
            foreach (string key in new[] { "res", "refine", "level", "asc", "weaponLevel", "armor", "accessory", "skillLv", "ultLv", "power4", "hp", "gearBps" })
                if (party.TryGetProperty(key, out var number) && number.ValueKind == JsonValueKind.Number && TryInteger(number, out long integer)) fields[key] = integer;
            setup.Party.Add(RunPartyEntry.FromJsonGraph(fields));
        }
        if (json.TryGetProperty("memory", out var memory)) setup.Memory = MemoryV2DataLoader.Graph(memory);
        if (json.TryGetProperty("bosses", out var bosses)) setup.Bosses = MemoryV2DataLoader.Graph(bosses);
        if (json.TryGetProperty("relics", out var relics) && relics.ValueKind != JsonValueKind.Null) setup.Relics = relics.EnumerateArray().Select(Text).ToList();
        if (json.TryGetProperty("seed", out var seed) && seed.ValueKind != JsonValueKind.Null) setup.Seed = Integer(seed);
        if (json.TryGetProperty("length", out var length) && length.ValueKind != JsonValueKind.Null) setup.Length = Text(length);
        if (json.TryGetProperty("zones", out var zones) && zones.ValueKind != JsonValueKind.Null) setup.Zones = zones.EnumerateArray().Select(Int).ToList();
        if (json.TryGetProperty("stageLevel", out var stage) && stage.ValueKind != JsonValueKind.Null) setup.StageLevel = Int(stage);
        if (json.TryGetProperty("difficulty", out var difficulty) && difficulty.ValueKind != JsonValueKind.Null)
        {
            var fields = MemoryV2DataLoader.Graph(difficulty) as Dictionary<string, object?> ?? throw new InvalidDataException("Expected expedition difficulty object");
            foreach (string key in new[] { "hp", "dmg" }) if (difficulty.TryGetProperty(key, out var number) && number.ValueKind == JsonValueKind.Number) fields[key] = Integer(number);
            setup.Difficulty = RunDifficulty.FromJsonGraph(fields);
        }
        if (json.TryGetProperty("memoryPolicy", out var policy) && policy.ValueKind != JsonValueKind.Null) setup.MemoryPolicy = Text(policy);
        if (json.TryGetProperty("runId", out var runId)) setup.RunId = MemoryV2DataLoader.Graph(runId);
        if (json.TryGetProperty("memoryVersion", out var version)) setup.MemoryVersion = version.ValueKind == JsonValueKind.Number && TryInteger(version, out long v) ? v : MemoryV2DataLoader.Graph(version);
        if (json.TryGetProperty("useMemory", out var useMemory) && useMemory.ValueKind != JsonValueKind.Null) setup.UseMemory = useMemory.GetBoolean();
        return setup;
    }
    static string Text(JsonElement json) => MemoryV2DataLoader.Graph(json) as string ?? throw new InvalidDataException("Expected expedition string");
    static bool TryInteger(JsonElement json, out long value)
    {
        if (json.ValueKind != JsonValueKind.Number) { value = 0; return false; }
        if (json.TryGetInt64(out value) && value >= -CanonicalState.MaxSafeInteger && value <= CanonicalState.MaxSafeInteger) return true;
        if (json.TryGetDecimal(out decimal number) && decimal.Truncate(number) == number && number >= -CanonicalState.MaxSafeInteger && number <= CanonicalState.MaxSafeInteger) { value = (long)number; return true; }
        value = 0; return false;
    }
    static long Integer(JsonElement json) => TryInteger(json, out long value) ? value : throw new RuleException("RUN_NUMBER_OUT_OF_RANGE");
    static int Int(JsonElement json)
    {
        long value = Integer(json); if (value < int.MinValue || value > int.MaxValue) throw new RuleException("RUN_NUMBER_OUT_OF_RANGE"); return (int)value;
    }
}
