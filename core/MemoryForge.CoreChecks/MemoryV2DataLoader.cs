// Host-only Memory JSON boundary: keeps fractional metadata without widening combat readers.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using MemoryForge.Core;

static class MemoryV2DataLoader
{
    public static MemoryRulesData Load(string root)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
        var json = document.RootElement; var rules = json.GetProperty("rules"); var c = rules.GetProperty("memoryCost");
        var data = new MemoryRulesData { Battle = BattleDataLoader.Load(root), CapacityBase = rules.GetProperty("capacityBase").GetInt32(),
            StartingCommon = json.GetProperty("startingCommon").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal),
            Costs = new MemoryCostConfig { AddB = I(c, "addB"), AddA = I(c, "addA"), AddS = I(c, "addS"), AddCommon = I(c, "addCommon"),
                Upgrade = I(c, "upgrade"), PartCommon = I(c, "partCommon"), PartRare = I(c, "partRare"), Evolution = I(c, "evolution"), Remove = I(c, "remove") } };
        foreach (var rule in json.GetProperty("progression").GetProperty("stats").GetProperty("capacity").GetProperty("ascension").EnumerateArray())
            data.AscensionCapacity.Add(new MemoryAscensionCapacity { Asc = I(rule, "asc"), Add = I(rule, "add") });
        return data;
    }
    static int I(JsonElement json, string key) => json.GetProperty(key).GetInt32();

    public static object? Graph(JsonElement json)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.Object:
                var graph = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var property in json.EnumerateObject())
                {
                    string name = BattleDataLoader.Name(property);
                    if (!graph.TryAdd(name, Graph(property.Value))) throw new InvalidDataException("Duplicate decoded memory JSON property");
                }
                return graph;
            case JsonValueKind.Array: return json.EnumerateArray().Select(Graph).ToList();
            case JsonValueKind.Number:
                if (json.TryGetInt64(out long integer)) return integer;
                double number = json.GetDouble();
                if (double.IsNaN(number) || double.IsInfinity(number)) throw new InvalidDataException("Memory metadata number must be finite");
                return number;
            default: return BattleDataLoader.Graph(json);
        }
    }

    public static object? Invoke(MemoryRulesData data, string method, Dictionary<string, object?> input)
    {
        object? Arg(string name) => input.TryGetValue(name, out var value) ? value : null;
        string Cid() => Arg("cid") as string ?? "";
        switch (method)
        {
            case "inspect": return MemoryV2.Inspect(data, Arg("snapshot")).ToJsonGraph();
            case "baseline": return MemoryV2.Baseline(data, Cid()).Select(c => (object?)c.ToJsonGraph()).ToList();
            case "listChanges": return MemoryV2.ListChanges(data, Arg("before"), Arg("after")).Select(c => (object?)c.ToJsonGraph()).ToList();
            case "cost": return MemoryV2.Cost(data, Cid(), Arg("specs"), Arg("baseSpecs"));
            case "applyChanges": return MemoryV2.ApplyChanges(Arg("before"), Arg("changes"));
            case "apply": return (input.ContainsKey("options")
                ? MemoryV2.Apply(data, Arg("before"), Arg("after"), Arg("selectedIds"), Arg("capacities"), Arg("options"))
                : MemoryV2.Apply(data, Arg("before"), Arg("after"), Arg("selectedIds"), Arg("capacities"))).ToJsonGraph();
            case "recover": return MemoryV2.Recover(data, Arg("before"), Arg("after"), Arg("selectedIds"), Arg("capacities")).ToJsonGraph();
            case "migrate": return MemoryV2.Migrate(data, Arg("legacy"), Arg("capacities")).ToJsonGraph();
            case "capacityOf":
                if (!(Arg("partyEntry") is IDictionary<string, object?> party)) throw new MemoryBoundaryException("TypeError", "Missing capacity entry");
                party.TryGetValue("charId", out var cid); party.TryGetValue("res", out var res); party.TryGetValue("asc", out var asc);
                return MemoryV2.CapacityOf(data, new MemoryCapacityEntry { CharId = cid as string ?? "", Res = CapacityInteger(res), Asc = CapacityInteger(asc) });
            default: throw new InvalidDataException("Unknown memory oracle method: " + method);
        }
    }
    // The JS capacity entry uses || and slice/relational coercion; account-side Core inputs are already integers.
    static int CapacityInteger(object? value)
    {
        double number;
        if (value == null) number = 0;
        else if (value is bool flag) number = flag ? 1 : 0;
        else if (value is string text) number = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0;
        else if (value is long || value is int || value is double) number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        else number = 0;
        if (double.IsNaN(number)) return 0;
        if (number >= int.MaxValue) return int.MaxValue;
        if (number <= int.MinValue) return int.MinValue;
        return (int)Math.Truncate(number);
    }

    public static Dictionary<string, object?> Catalog(MemoryRulesData data) => new(StringComparer.Ordinal)
    {
        ["cards"] = data.Battle.Cards.Select(c => (object?)c.ToJsonGraph()).ToList(),
        ["parts"] = data.Battle.Parts.Select(p => (object?)p.ToJsonGraph()).ToList(),
        ["characters"] = data.Battle.Characters.Select(c => (object?)c.ToJsonGraph()).ToList(),
        ["startingCommon"] = data.StartingCommon.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal),
        ["capacityBase"] = data.CapacityBase,
        ["ascension"] = data.AscensionCapacity.Select(c => (object?)new Dictionary<string, object?> { ["asc"] = c.Asc, ["add"] = c.Add }).ToList(),
        ["costs"] = new Dictionary<string, object?> { ["addB"] = data.Costs.AddB, ["addA"] = data.Costs.AddA, ["addS"] = data.Costs.AddS, ["addCommon"] = data.Costs.AddCommon,
            ["upgrade"] = data.Costs.Upgrade, ["partCommon"] = data.Costs.PartCommon, ["partRare"] = data.Costs.PartRare, ["evolution"] = data.Costs.Evolution, ["remove"] = data.Costs.Remove }
    };
    public static string? Difference(object? expected, object? actual, string path = "$")
    {
        if (expected is Dictionary<string, object?> obj)
        {
            if (actual is not IDictionary<string, object?> got || obj.Count != got.Count) return path + " object keys differ";
            foreach (var pair in obj)
            {
                if (!got.TryGetValue(pair.Key, out var value)) return path + " missing " + pair.Key;
                string? diff = Difference(pair.Value, value, path + "." + pair.Key); if (diff != null) return diff;
            }
            return null;
        }
        if (expected is IList array)
        {
            if (actual is not IList got || array.Count != got.Count) return path + " array shape differs";
            for (int i = 0; i < array.Count; i++) { string? diff = Difference(array[i], got[i], path + "[" + i + "]"); if (diff != null) return diff; }
            return null;
        }
        if ((expected is double || actual is double) && (expected is long || expected is int || expected is uint || expected is double) && (actual is long || actual is int || actual is uint || actual is double))
            return Convert.ToDouble(expected, CultureInfo.InvariantCulture) == Convert.ToDouble(actual, CultureInfo.InvariantCulture) ? null : path + " metadata number differs";
        return JsonGraphComparison.Difference(expected, actual, path, 0);
    }
}
