// M0 pure memory rules; this does not execute Run commands or persist account data.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MemoryForge.Core;

static class MemoryV2Checks
{
    const string Scope = "Pure Memory inspect/baseline/listChanges/cost/applyChanges/apply/recover/migrate and current capacityOf only; actual Run command replay, persistence, server and account migration NOT_IMPLEMENTED";
    const int ExpectedCases = 1072;
    static int checks, failures, cases;
    static void Check(bool ok, string label) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL MemoryV2 " + label); } }
    static void Require(bool ok, string label) { if (!ok) throw new InvalidDataException(label); }
    public static int Run(string root)
    {
        checks = failures = cases = 0;
        try
        {
            using var file = File.OpenRead(Path.Combine(root, "engine", "tests", "vectors", "memory_rule_vectors.json.gz"));
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            var fixture = document.RootElement;
            ValidateFixture(fixture, root);
            var data = MemoryV2DataLoader.Load(root);
            NativeRegressionAssertions(data);
            var originalData = MemoryV2DataLoader.Catalog(data);
            foreach (var row in fixture.GetProperty("cases").EnumerateArray())
            {
                string id = row.GetProperty("id").GetString()!;
                var input = (Dictionary<string, object?>)MemoryV2DataLoader.Graph(row.GetProperty("input"))!;
                var outcome = row.GetProperty("outcome");
                object? result = null; string? errorName = null, errorCode = null;
                try { result = MemoryV2DataLoader.Invoke(data, row.GetProperty("method").GetString()!, input); }
                catch (RuleException error) { errorName = "Error"; errorCode = error.Code; }
                catch (MemoryBoundaryException error) { errorName = error.JavaScriptName; }
                if (outcome.TryGetProperty("error", out var expectedError))
                {
                    Check(errorName == expectedError.GetProperty("name").GetString(), id + " error name");
                    Check(errorCode == (expectedError.TryGetProperty("code", out var code) ? code.GetString() : null), id + " error code");
                }
                else
                {
                    Check(errorName == null, id + " committed");
                    if (errorName == null) Same(outcome.GetProperty("result"), result, id + " complete result");
                }
                Same(row.GetProperty("input"), input, id + " unchanged complete input");
                Same(row.GetProperty("inputAfter"), input, id + " JS unchanged input");
                Check(MemoryV2DataLoader.Difference(originalData, MemoryV2DataLoader.Catalog(data)) == null, id + " unchanged authored definitions");
                cases++;
            }
            IndependentAssertions(data);
            CopyAssertions(data);
            FixtureRejections(fixture, root);
        }
        catch (Exception error) { Check(false, "fixture/execution: " + error); }
        Console.WriteLine($"MemoryV2Checks: {checks - failures}/{checks} passed; {cases} actual pure-memory oracle calls");
        Console.WriteLine("MemoryV2Scope: pure inspect/diff/full-cost/apply/recover/migrate/current-capacity only; Run replay, raw-byte backup, persistence and server/device acceptance NOT_IMPLEMENTED here");
        return failures;
    }

    public static int RunNativeRegressions(string root)
    {
        checks = failures = cases = 0;
        try { NativeRegressionAssertions(MemoryV2DataLoader.Load(root)); }
        catch (Exception error) { Check(false, "native regression execution: " + error); }
        Console.WriteLine($"MemoryV2NativeRegressionChecks: {checks - failures}/{checks} passed");
        return failures;
    }
    static void NativeRegressionAssertions(MemoryRulesData data)
    {
        string? ErrorCode(Action action)
        {
            try { action(); return null; }
            catch (RuleException error) { return error.Code; }
            catch (MemoryBoundaryException error) { return error.JavaScriptName; }
        }
        foreach (var pair in new[] { (Key: "0", Code: "INVALID_SNAPSHOT"), (Key: "4294967294", Code: "INVALID_SNAPSHOT"),
            (Key: "01", Code: "INVALID_CARD"), (Key: "4294967295", Code: "INVALID_CARD"), (Key: "-0", Code: "INVALID_CARD") })
        {
            // Parse actual raw text: JSON.stringify would reorder the integer key before a fixture reaches C#.
            using var raw = JsonDocument.Parse("{\"sera\":[null],\"" + pair.Key + "\":[]}");
            var graph = (Dictionary<string, object?>)MemoryV2DataLoader.Graph(raw.RootElement)!;
            Check(graph.Keys.First() == "sera", "native raw JSON keeps transport insertion order " + pair.Key);
            Check(ErrorCode(() => MemoryV2.Inspect(data, graph)) == pair.Code, "JS Object.entries index-key error precedence " + pair.Key);
            Same(raw.RootElement, graph, "native rejection leaves raw input unchanged " + pair.Key);
        }
        var empty = new List<object?>(); var nullBase = new List<object?> { null };
        long? emptyCost = null; string? emptyError = ErrorCode(() => emptyCost = MemoryV2.Cost(data, "sera", empty, nullBase));
        Check(emptyError == null && emptyCost == 1, "independent empty-pool null baseline costs removal1 without evaluating a predicate");
        Check(empty.Count == 0 && nullBase.Count == 1 && nullBase[0] == null, "empty-pool cost preserves its inputs");
        var one = new List<object?> { new Dictionary<string, object?> { ["id"] = "sera_round", ["key"] = "one" } };
        Check(ErrorCode(() => MemoryV2.Cost(data, "sera", one, nullBase)) == "TypeError", "nonempty pool still evaluates null baseline property access");
        var drainedBase = new List<object?> { new Dictionary<string, object?> { ["key"] = "one" }, null };
        long? drainedCost = null; string? drainedError = ErrorCode(() => drainedCost = MemoryV2.Cost(data, "sera", one, drainedBase));
        Check(drainedError == null && drainedCost == 1, "pool emptied by an earlier match skips later null baseline predicate");
        foreach (var pair in new[]
        {
            (Json: "{\"sera\":[{\"id\":\"sera_round\",\"plus\":1},[]]}", Code: "INVALID_UPGRADE"),
            (Json: "{\"sera\":[{\"key\":\"legacy:sera:1\",\"id\":\"sera_round\"},[]]}", Code: "IDENTITY_REQUIRED")
        })
        {
            using var raw = JsonDocument.Parse(pair.Json);
            var input = MemoryV2DataLoader.Graph(raw.RootElement);
            var migrated = MemoryV2.Migrate(data, input, new Dictionary<string, object?> { ["sera"] = 6L });
            Check(migrated.Quarantine["sera"].Code == pair.Code, "migration validates earlier rows before an array card: " + pair.Code);
            Check(migrated.Memory.CharacterIds.Count == 0, "invalid array migration never saves a partial character " + pair.Code);
            Same(raw.RootElement, migrated.LegacyBackup, "array migration preserves complete backup " + pair.Code);
            Same(raw.RootElement.GetProperty("sera"), migrated.Quarantine["sera"].Original, "array migration preserves complete quarantine original " + pair.Code);
            Same(raw.RootElement, input, "array migration preserves input " + pair.Code);
        }
    }

    static void ValidateFixture(JsonElement fixture, string root)
    {
        Require(fixture.GetProperty("schema").GetString() == "mf-memory-rules-v1", "Wrong memory fixture schema");
        Require(fixture.GetProperty("scope").GetString() == Scope, "Wrong memory fixture scope");
        using var sourceDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
        Require(fixture.GetProperty("dataVersion").GetString() == sourceDocument.RootElement.GetProperty("meta").GetProperty("version").GetString(), "Memory data version mismatch");
        var paths = new Dictionary<string, string>(StringComparer.Ordinal) { ["data"] = "data/game_data.json", ["engine"] = "engine/rules-core.js", ["memory"] = "proposals/memory_v2.cjs", ["generator"] = "tools/gen_memory_rule_vectors.mjs" };
        var sources = fixture.GetProperty("sources");
        Require(sources.EnumerateObject().Count() == paths.Count, "Memory source count mismatch");
        foreach (var path in paths)
        {
            var entry = sources.GetProperty(path.Key);
            Require(entry.EnumerateObject().Count() == 2 && entry.GetProperty("path").GetString() == path.Value && entry.GetProperty("sha256").GetString() == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, path.Value)))), "Memory source provenance mismatch: " + path.Key);
        }
        Require(fixture.GetProperty("cases").GetArrayLength() == ExpectedCases, "Memory case count mismatch");
        var ids = new HashSet<string>(StringComparer.Ordinal); var methods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in fixture.GetProperty("cases").EnumerateArray())
        {
            Require(ids.Add(row.GetProperty("id").GetString()!), "Duplicate memory case id");
            methods.Add(row.GetProperty("method").GetString()!);
            var outcome = row.GetProperty("outcome");
            Require(outcome.EnumerateObject().Count() == 1 && (outcome.TryGetProperty("result", out _) || outcome.TryGetProperty("error", out _)), "Malformed memory outcome");
            if (outcome.TryGetProperty("error", out var error))
            {
                string? name = error.GetProperty("name").GetString();
                Require(name == "Error" || name == "TypeError" || name == "URIError", "Unexpected memory oracle error name");
                Require(name != "Error" || error.TryGetProperty("code", out var code) && !string.IsNullOrEmpty(code.GetString()), "Missing memory rejection code");
            }
            Require(row.GetProperty("dataUnchanged").GetBoolean(), "JS oracle changed source data");
            Require(MemoryV2DataLoader.Difference(MemoryV2DataLoader.Graph(row.GetProperty("input")), MemoryV2DataLoader.Graph(row.GetProperty("inputAfter"))) == null, "JS oracle changed input");
        }
        Require(methods.SetEquals(new[] { "inspect", "baseline", "listChanges", "cost", "applyChanges", "apply", "recover", "migrate", "capacityOf" }), "Incomplete memory method coverage");
        foreach (var cid in sourceDocument.RootElement.GetProperty("characters").EnumerateArray().Select(c => c.GetProperty("id").GetString()!))
        {
            Require(ids.Contains("baseline-" + cid), "Missing authored character baseline " + cid);
            foreach (int asc in new[] { 0, 2, 4 }) for (int res = 0; res <= 6; res++)
                Require(ids.Contains($"capacity-{cid}-asc{asc}-res{res}"), "Missing authored character capacity " + cid);
        }
    }

    static void IndependentAssertions(MemoryRulesData data)
    {
        foreach (var character in data.Battle.Characters)
        {
            var baseline = MemoryV2.Baseline(data, character.Id).Select(c => (object?)c.ToJsonGraph()).ToList();
            Check(MemoryV2.Cost(data, character.Id, baseline) == 0, character.Id + " independent baseline zero cost");
            Check(MemoryV2.CapacityOf(data, new MemoryCapacityEntry { CharId = character.Id }) == 6, character.Id + " independent current capacity base6");
            Check(MemoryV2.CapacityOf(data, new MemoryCapacityEntry { CharId = character.Id, Res = 4, Asc = 4 }) == 9, character.Id + " current ascension/resonance capacity9");
        }
        var original = MemoryV2.Baseline(data, "sera").Select(c => (object?)c.ToJsonGraph()).ToList();
        var replacement = original.Skip(1).ToList();
        replacement.Add(new Dictionary<string, object?> { ["id"] = "sera_round", ["key"] = "new-copy" });
        Check(MemoryV2.Cost(data, "sera", replacement, original) == 2, "independent original removal plus same-kind addition costs2");
        var snapshot = new Dictionary<string, object?> { ["sera"] = original };
        var changed = MemoryV2.Baseline(data, "sera").Select(c => (object?)c.ToJsonGraph()).ToList();
        changed.Add(new Dictionary<string, object?> { ["key"] = "rare", ["id"] = "sera_phase", ["plus"] = true, ["mods"] = new Dictionary<string, object?> { ["c"] = "c_echo" } });
        Check(MemoryV2.Cost(data, "sera", changed) == 6, "independent rare3 plus upgrade1 plus rare-part2 costs6");
        var after = new Dictionary<string, object?> { ["sera"] = changed };
        var output = MemoryV2.Apply(data, snapshot, after, new List<object?>(), new Dictionary<string, object?> { ["sera"] = 6 });
        Check(MemoryV2DataLoader.Difference(output.Memory.ToJsonGraph(), snapshot) == null, "independent no selection preserves before");
        var cyclic = new List<object?>(); cyclic.Add(cyclic);
        object deep = new List<object?>(); for (int i = 0; i < 257; i++) deep = new List<object?> { deep };
        foreach (var invalid in new object[] { new object(), cyclic, deep, new int[1, 1], double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { MemoryV2.Migrate(data, new Dictionary<string, object?> { ["unknown"] = invalid }, new Dictionary<string, object?>()); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "non-JSON/cyclic/nonfinite metadata rejected");
        }
    }
    static void CopyAssertions(MemoryRulesData data)
    {
        var rows = MemoryV2.Baseline(data, "sera").Select(c => (object?)c.ToJsonGraph()).ToList();
        var card = (Dictionary<string, object?>)rows[0]!;
        card["plus"] = false; card["mods"] = null;
        card["metadata"] = new Dictionary<string, object?> { ["ratio"] = 0.125, ["flags"] = new List<object?> { false, null, 1.5 } };
        var before = new Dictionary<string, object?> { ["sera"] = rows };
        var inspection = MemoryV2.Inspect(data, before);
        card["id"] = "sera_cover";
        Check(inspection.Snapshot.Cards("sera")[0].Id == "sera_round", "inspect result is detached from later caller input mutations");
        card["id"] = "sera_round";
        var applied = MemoryV2.Apply(data, before, before, new List<object?>(), new Dictionary<string, object?> { ["sera"] = 6 });
        var graph = applied.Memory.ToJsonGraph();
        var returnedCard = (Dictionary<string, object?>)((IList)graph["sera"]!)[0]!;
        ((Dictionary<string, object?>)returnedCard["metadata"]!)["ratio"] = 0.75;
        Check((double)((Dictionary<string, object?>)card["metadata"]!)["ratio"]! == 0.125, "result metadata mutation cannot change input");
        var fresh = applied.Memory.ToJsonGraph();
        var freshCard = (Dictionary<string, object?>)((IList)fresh["sera"]!)[0]!;
        Check((double)((Dictionary<string, object?>)freshCard["metadata"]!)["ratio"]! == 0.125 && freshCard.ContainsKey("plus") && Equals(freshCard["plus"], false) && freshCard.ContainsKey("mods") && freshCard["mods"] == null, "detached result retains fractional/null/false presence");
        var afterRows = MemoryV2.Baseline(data, "sera").Select(c => (object?)c.ToJsonGraph()).ToList();
        afterRows.Add(new Dictionary<string, object?> { ["id"] = "sera_round", ["key"] = "copy", ["metadata"] = new Dictionary<string, object?> { ["ratio"] = 0.25 } });
        var after = new Dictionary<string, object?> { ["sera"] = afterRows };
        var add = MemoryV2.ListChanges(data, before, after).Single(c => c.Kind == "add");
        ((Dictionary<string, object?>)((Dictionary<string, object?>)add.Value!)["metadata"]!)["ratio"] = 0.5;
        Check((double)((Dictionary<string, object?>)((Dictionary<string, object?>)afterRows[^1]!)["metadata"]!)["ratio"]! == 0.25, "change add payload is detached from after");
        var invalid = new Dictionary<string, object?> { ["sera"] = new List<object?> { new Dictionary<string, object?> { ["id"] = "sera_round", ["plus"] = "invalid", ["metadata"] = new Dictionary<string, object?> { ["ratio"] = 0.25 } } } };
        var migrated = MemoryV2.Migrate(data, invalid, new Dictionary<string, object?> { ["sera"] = 6 });
        var original = (IList)migrated.Quarantine["sera"].Original!;
        ((Dictionary<string, object?>)((Dictionary<string, object?>)original[0]!)["metadata"]!)["ratio"] = 0.75;
        var untouched = (IList)migrated.Quarantine["sera"].Original!;
        Check((double)((Dictionary<string, object?>)((Dictionary<string, object?>)untouched[0]!)["metadata"]!)["ratio"]! == 0.25, "quarantine original is detached and never truncates metadata");
        var backup = (Dictionary<string, object?>)migrated.LegacyBackup!; backup.Clear();
        Check(((Dictionary<string, object?>)migrated.LegacyBackup!).Count == 1, "legacyBackup returns independent complete copies");
        var specialOwners = new Dictionary<string, object?> { ["__proto__"] = new List<object?> { new Dictionary<string, object?> { ["id"] = "legacy_unknown", ["note"] = "keep complete original" } },
            ["constructor"] = new List<object?> { false }, ["toString"] = new List<object?> { null } };
        var preserved = MemoryV2.Migrate(data, specialOwners, new Dictionary<string, object?>());
        Check(specialOwners.Keys.All(key => preserved.Quarantine.ContainsKey(key)) && preserved.Quarantine.Count == 3, "independent own-key quarantine preserves prototype-like character names");
        Check(MemoryV2DataLoader.Difference(preserved.LegacyBackup, specialOwners) == null, "special-name legacyBackup preserves complete original");
        foreach (var pair in specialOwners) Check(MemoryV2DataLoader.Difference(preserved.Quarantine[pair.Key].Original, pair.Value) == null, "special-name quarantine preserves complete original " + pair.Key);
    }
    static void FixtureRejections(JsonElement fixture, string root)
    {
        void Reject(Action<Dictionary<string, object?>> mutate, string label)
        {
            var graph = (Dictionary<string, object?>)MemoryV2DataLoader.Graph(fixture)!; mutate(graph);
            using var changed = JsonDocument.Parse(JsonSerializer.Serialize(graph)); bool rejected = false;
            try { ValidateFixture(changed.RootElement, root); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "fixture tampering rejected: " + label);
        }
        foreach (string field in new[] { "schema", "scope", "dataVersion" }) Reject(g => g[field] = "tampered", field);
        foreach (string field in new[] { "path", "sha256" }) Reject(g => ((Dictionary<string, object?>)((Dictionary<string, object?>)g["sources"]!)["engine"]!)[field] = "tampered", "source " + field);
        Reject(g => ((IList)g["cases"]!).RemoveAt(0), "missing case");
        Reject(g => ((IList)g["cases"]!)[1] = ((IList)g["cases"]!)[0], "duplicate id");
        Reject(g => ((Dictionary<string, object?>)((IList)g["cases"]!)[0]!)["dataUnchanged"] = false, "changed source data");
        Reject(g => ((Dictionary<string, object?>)((IList)g["cases"]!)[0]!)["inputAfter"] = null, "mutated input");
        Reject(g => ((Dictionary<string, object?>)((IList)g["cases"]!)[0]!)["method"] = "unknown", "unknown method");
        using var fractional = JsonDocument.Parse("{\"value\":0.5}");
        bool integerReaderRejected = false;
        try { BattleDataLoader.Graph(fractional.RootElement); } catch (InvalidDataException) { integerReaderRejected = true; }
        Check(integerReaderRejected, "shared Battle integer reader still rejects fractional input");
        bool canonicalRejected = false;
        try { CanonicalState.Serialize(MemoryV2DataLoader.Graph(fractional.RootElement)); } catch (ArgumentException) { canonicalRejected = true; }
        Check(canonicalRejected, "CanonicalState integer scope remains unchanged");
    }
    static void Same(JsonElement expected, object? actual, string label)
    {
        string? diff = MemoryV2DataLoader.Difference(MemoryV2DataLoader.Graph(expected), actual);
        Check(diff == null, label + (diff == null ? "" : ": " + diff));
    }
}
