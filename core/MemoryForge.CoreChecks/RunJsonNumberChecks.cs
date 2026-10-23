// §8 receipt equality / §16.2: independent JS exact-bit vectors and an actual public receipt replay.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MemoryForge.Core;

static class RunJsonNumberChecks
{
    static int checks, failures;
    static void Check(bool value, string label) { checks++; if (!value) { failures++; Console.WriteLine("FAIL RunJsonNumber " + label); } }
    public static int Run(string root)
    {
        checks = failures = 0;
        try
        {
            using var document = RunCreateChecks.Decode(File.ReadAllBytes(Path.Combine(root, "engine/tests/vectors/run_json_number_vectors.json.gz")));
            var fixture = document.RootElement; RunCreateChecks.UniqueKeys(fixture);
            Check(fixture.GetProperty("schema").GetString() == "mf-run-json-number-v1" && fixture.GetProperty("status").GetString() == "JS_ORACLE_ONLY", "source-only schema/status");
            var source = fixture.GetProperty("source");
            const string path = "tools/gen_run_json_number_vectors.mjs";
            if (source.GetProperty("path").GetString() != path) throw new InvalidDataException("Substituted numeric source path");
            using var sha = SHA256.Create();
            string fingerprint = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(root, path)))).Replace("-", "").ToLowerInvariant();
            Check(source.GetProperty("sha256").GetString() == fingerprint, "current generator fingerprint");
            var rows = fixture.GetProperty("cases");
            Check(fixture.GetProperty("count").GetInt32() == 2176 && rows.GetArrayLength() == 2176, "independent 2176-case denominator");
            var method = typeof(MemoryForge.Core.Run).GetMethod("JsonText", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException("Run.JsonText");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows.EnumerateArray())
            {
                string bits = row.GetProperty("bits").GetString() ?? "";
                if (bits.Length != 16 || !ulong.TryParse(bits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong raw) || !seen.Add(bits))
                    throw new InvalidDataException("Invalid/duplicate binary64 input");
                var value = BitConverter.Int64BitsToDouble(unchecked((long)raw));
                string? actual = method.Invoke(null, new object[] { value }) as string;
                Check(actual == row.GetProperty("json").GetString(), "actual JS string for bits " + bits);
            }
            Check(seen.Count == 2176, "all exact-bit cases executed");
            PublicReceipt(root);
        }
        catch (Exception error) { Check(false, error.GetType().Name + ": " + error.Message); }
        Console.WriteLine($"RunJsonNumberChecks: {checks - failures}/{checks} passed; exact JS binary64 cases 2176; controlled public receipt replay, no account/disk writes");
        return failures;
    }

    static void PublicReceipt(string root)
    {
        var data = RunDataLoader.Load(root);
        var setup = new RunSetup { Seed = 1234, Length = "short", MemoryPolicy = "v2", RunId = "numeric-public-witness", MemoryVersion = 0L };
        foreach (string cid in new[] { "sera", "harin", "nia" }) setup.Party.Add(new RunPartyEntry { CharId = cid, Res = 0 });
        var run = MemoryForge.Core.Run.Create(data, setup);
        var current = MemoryV2.Migrate(data.Memory, new Dictionary<string, object?>(), new Dictionary<string, object?>()).ToJsonGraph();
        const string body = "{\"runId\":\"numeric-public-witness\",\"expectedMemoryVersion\":9.999999999999997e+22,\"selectedChangeIds\":[]}";
        var response = new Dictionary<string, object?> { ["memory"] = new Dictionary<string, object?>(), ["used"] = new Dictionary<string, object?>(), ["version"] = 0L };
        ((IDictionary<string, object?>)current["receipts"]!)["numeric-replay"] = new Dictionary<string, object?> { ["body"] = body, ["response"] = response };
        var request = new Dictionary<string, object?> { ["requestId"] = "numeric-replay", ["runId"] = "numeric-public-witness",
            ["expectedMemoryVersion"] = BitConverter.Int64BitsToDouble(0x44b52d02c7e14af5L), ["selectedChangeIds"] = new List<object?>() };
        string before = JsonSerializer.Serialize(current);
        var result = run.CommitMemory(request, current);
        Check(run.State.Phase == "DONE", "original JS matching own receipt replays before phase/version checks");
        Check(MemoryV2DataLoader.Difference(result["response"], response) == null, "same recorded response returned");
        Check(JsonSerializer.Serialize(current) == before, "caller store unchanged");
        foreach (string mode in new[] { "next-double", "wrong-body" })
        {
            var fresh = MemoryForge.Core.Run.Create(data, setup);
            var store = MemoryV2.Migrate(data.Memory, new Dictionary<string, object?>(), new Dictionary<string, object?>()).ToJsonGraph();
            ((IDictionary<string, object?>)store["receipts"]!)["numeric-replay"] = new Dictionary<string, object?>
            { ["body"] = mode == "wrong-body" ? body.Replace("999999999999997", "999999999999998") : body, ["response"] = response };
            var candidate = new Dictionary<string, object?>(request);
            if (mode == "next-double") candidate["expectedMemoryVersion"] = BitConverter.Int64BitsToDouble(0x44b52d02c7e14af6L);
            string stateBefore = CanonicalState.Hash(fresh.State.ToJsonGraph()), storeBefore = JsonSerializer.Serialize(store);
            string? code = null;
            try { fresh.CommitMemory(candidate, store); } catch (RuleException error) { code = error.Code; }
            Check(code == "REQUEST_CONFLICT", mode + " remains an exact-string conflict");
            Check(CanonicalState.Hash(fresh.State.ToJsonGraph()) == stateBefore && fresh.State.Phase == "MAP", mode + " does not change the Run");
            Check(JsonSerializer.Serialize(store) == storeBefore, mode + " does not change the store");
        }
    }
}
