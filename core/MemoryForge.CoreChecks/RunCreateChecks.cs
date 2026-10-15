// R0 §5/7/8/16.2/20.9: actual C# Run.Create + pure Available/NodeById against the JS oracle.
// Expected states/hashes are comparison data only; nothing is injected into Run. Run commands remain0.
using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MemoryForge.Core;
using ExpeditionRun = MemoryForge.Core.Run;

static class RunCreateChecks
{
    const string Schema = "mf-run-create-v1", Status = "COMPLETE_CURRENT_SOURCE_R0_FACTORY_QUERIES", TypedCode = "RUN_NUMBER_OUT_OF_RANGE";
    const string Scope = "Actual Run.create and pure available/nodeById from fresh creation only; Run commands, saving, restoration, server and Unity acceptance remain pending";
    static readonly Dictionary<string, string> Sources = new(StringComparer.Ordinal)
    {
        ["data"] = "data/game_data.json", ["engine"] = "engine/rules-core.js", ["memory"] = "proposals/memory_v2.cjs", ["canonical"] = "tools/gen_traces.mjs",
        ["fuzzPolicy"] = "engine/tests/fuzz_runs.mjs", ["manifest"] = "MANIFEST_AIO.json", ["manifestSha"] = "MANIFEST.sha256", ["generator"] = "tools/gen_run_create_vectors.mjs"
    };
    // Current tools/gen_run_create_vectors.mjs casePlan totals; a shortened, stale or reclassified fixture is rejected.
    static readonly Dictionary<string, long> Counts = new(StringComparer.Ordinal)
    {
        ["cases"] = 2131, ["factories"] = 2131, ["created"] = 2028, ["rejected"] = 103, ["queries"] = 45238, ["supportedFactories"] = 2121,
        ["supportedCreated"] = 2023, ["supportedRejected"] = 98, ["supportedQueries"] = 45130, ["sourceOnlyFactories"] = 5, ["typedDomainProbes"] = 5, ["runCommands"] = 0
    };
    static readonly string[] TopFields = { "schema", "status", "scope", "dataVersion", "sources", "inputManifestAudit", "nativeDiagnosticsRuntime", "domains", "reachability", "coverage", "counts", "cases" };
    static readonly string[] QueryFields = { "method", "args", "result", "resultHash", "before", "after" }, ResultFields = { "kind", "value" };
    static readonly JsonSerializerOptions PublicFields = new() { IncludeFields = true };
    static readonly UTF8Encoding Utf8 = new(false, true);
    static readonly string[] RowFields = { "id", "setup", "inputHash", "outcome", "created", "queries", "afterQueries", "error", "anchors" };
    static readonly Dictionary<string, object?> Domains = O(
        "arithmetic", "Party/stage arithmetic inputs and derived fields use signedInt32; seed/version use JS safe-integer long",
        "opaqueMetadata", "Copied JSON values retain fractions; json-only rows preserve full graphs and JSON hashes while explicitly withholding unsupported integer canonical hashes",
        "sourceOnlyNativeErrors", "Native SyntaxError/TypeError are diagnostic rows, not invented C# RuleError equivalence",
        "typedDomainRejections", "Actual JS creation evidence retained; C# must reject these explicitly with RUN_NUMBER_OUT_OF_RANGE",
        "undefined", "Source own-undefined paths are listed in fieldShape.undefined; JSON graph omits those fields. Query undefined is a distinct result kind, never null");
    static readonly Dictionary<string, object?> Reachability = O("factoryOnly", true, "runCommands", 0, "safePublicView", false,
        "available", "Fresh MAP/pos:null only; no state injection to exercise later node/phase paths",
        "nodeByIdMissing", "Explicit JSON null; no normal fresh query returns undefined",
        "memorySnapshots", "Legitimate departure inputs only; no saved-result or afterBattle injection",
        "boss", "Provided authored IDs are retained; encounter selection happens after creation and is not claimed", "savingAndRestoration", "PENDING");
    const string Dimensions = "Exact sets above; no full Cartesian claim outside character/resonance/policy/length and weapon/refine matrices";
    static int checks, failures, factories, created, rejected, queries, recreations, typedRejections, sourceOnly, jsOnlyQueries, adapterRejections, probes, identityProbes, boundaryProbes, probeFailures;
    static bool probing;
    // While probing a deliberately corrupted input, assertion failures are the expected rejection signal, not suite failures.
    static void Check(bool ok, string label)
    {
        if (probing) { if (!ok) probeFailures++; return; }
        checks++; if (!ok) { failures++; Console.WriteLine("FAIL RunCreate " + label); }
    }
    static void Require(bool ok, string label) { if (!ok) throw new InvalidDataException(label); }
    static void Count(ref int counter) { if (!probing) counter++; }

    sealed class PlanCase
    {
        public string Id = "";
        public Dictionary<string, object?> Setup = new(), Metadata = new();
        public string? Code, Name;
    }

    sealed class Point
    {
        public Dictionary<string, object?> State = new(), Diagnostics = new(), Shape = new();
        public List<object?> Undefined = new();
        public string? StateHash;
        public string RngHash = "", MapHash = "", LogHash = "";
    }

    public static int Run(string root)
    {
        checks = failures = factories = created = rejected = queries = recreations = typedRejections = sourceOnly = jsOnlyQueries = adapterRejections = probes = identityProbes = boundaryProbes = probeFailures = 0;
        probing = false; manifestSummary = "NOT_COMPUTED";
        var diagnostics = new List<string>();
        try
        {
            using var document = Decode(File.ReadAllBytes(Path.Combine(root, "engine", "tests", "vectors", "run_create_vectors.json.gz")));
            using var content = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
            UniqueKeys(document.RootElement);
            var plan = CasePlan(content.RootElement); var audit = ManifestAudit(root);
            Check(plan.Count == Counts["cases"] && plan.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() == plan.Count, "independent source plan reconstructs 2131 unique ordered cases");
            var rows = ValidateFixture(document.RootElement, content.RootElement, root, plan, audit);
            Check(true, "gzip/UTF-8/unique-key fixture: exact schema/status/scope/sources/audit, ordered plan membership/setup/metadata, coverage, literal anchors and counts");
            var data = RunDataLoader.Load(root); string definitions = DataPrint(data);
            foreach (var row in rows)
            {
                string id = Text(row, "id");
                try { RunRow(row, data, definitions, diagnostics); }
                catch (Exception error) { Check(false, id + " execution: " + (error is InvalidDataException ? error.Message : error.ToString())); }
            }
            Check(factories == Counts["supportedFactories"] && created == Counts["supportedCreated"] && rejected == Counts["supportedRejected"], "every supported factory executed: 2023 created + 98 exact rejections");
            Check(queries == Counts["supportedQueries"] && queries + jsOnlyQueries == Counts["queries"], "every supported pure query executed (45130); typed-domain JS-only queries counted separately");
            Check(typedRejections == Counts["typedDomainProbes"], "all typed-domain rows rejected with " + TypedCode);
            Check(sourceOnly == Counts["sourceOnlyFactories"], "all JS-native rows remain source-only diagnostics");
            var byId = rows.ToDictionary(r => Text(r, "id"), StringComparer.Ordinal);
            Section("identity", () => IdentityProbes(byId, data, definitions));
            Section("typed boundary", () => BoundaryProbes(data, definitions, content.RootElement));
            Section("fixture corruption", () => FixtureProbes(document.RootElement, content.RootElement, root, rows, byId, plan, audit, data, definitions));
        }
        catch (Exception error) { Check(false, "fixture/execution: " + (error is InvalidDataException ? error.Message : error.ToString())); }
        Section("gzip/UTF-8/JSON container", GzipProbes);
        foreach (string line in diagnostics) Console.WriteLine("RunCreateSourceOnly: " + line);
        Console.WriteLine($"RunCreateChecks: {checks - failures}/{checks} passed; supported factories {factories}/2121 ({created} created, {rejected} exact rejections; 2023/98 expected), supported pure queries {queries}/45130, deterministic recreation rows {recreations}");
        Console.WriteLine($"RunCreateBoundary: typed-domain {typedRejections}/5 {TypedCode} rejections (JS-created evidence, {jsOnlyQueries} JS-only queries, not parity); JS-native source-only diagnostics {sourceOnly}/5 ({adapterRejections} host-adapter rejections), never counted as C# parity");
        Console.WriteLine($"RunCreateGuards: {probes} corruption/forgery probes, {identityProbes} reflection identity cases, {boundaryProbes} direct typed boundary probes; manifest audit {manifestSummary}");
        Console.WriteLine("RunCreateScope: fresh Run.Create + Available/NodeById only; Run commands 0; saving/restoration/server/Unity PENDING");
        Console.WriteLine("RunCreateFollowUp: R1 must pin available() at a boss position (JS undefined vs C# []) and future unknown weapon types (JS defaultWeapon undefined vs C# TypeError); both are unreachable in current fresh creation");
        return failures;
    }

    // Negative probes: a probe passes only when validation records a Check failure or raises the declared rejection class.
    // Counters are guarded by Count()/probing so corrupted inputs never inflate the actual factory/query denominators.
    static bool Rejects(Action validate, Func<Exception, bool>? expected = null)
    {
        probing = true; probeFailures = 0;
        try { validate(); return probeFailures > 0; }
        catch (Exception error) when (error is InvalidDataException || expected?.Invoke(error) == true) { return true; }
        finally { probing = false; }
    }
    static void Probe(string label, Action validate, Func<Exception, bool>? expected = null) { probes++; Check(Rejects(validate, expected), "corruption rejected: " + label); }
    static void Control(string label, Action validate, Func<Exception, bool>? expected = null) => Check(!Rejects(validate, expected), "untampered control accepted: " + label);
    static JsonElement Element(object? graph) { using var document = JsonDocument.Parse(JsonSerializer.Serialize(graph)); return document.RootElement.Clone(); }
    static Dictionary<string, object?> Shallow(JsonElement json) => json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value, StringComparer.Ordinal);
    static Dictionary<string, object?> Edit(JsonElement json, string key, object? value, bool remove = false)
    {
        var fields = Shallow(json); if (remove) fields.Remove(key); else fields[key] = value; return fields;
    }
    static JsonElement Changed(JsonElement row, Action<Dictionary<string, object?>> change) { var fields = Shallow(row); change(fields); return Element(fields); }

    // Rebuilds created/afterQueries/query diagnostics with self-consistent recomputed hashes, so only actual C# comparison can reject the forgery.
    static JsonElement Forge(JsonElement row, Action<Dictionary<string, object?>> editState, Action<Dictionary<string, object?>>? editShape = null)
    {
        var graph = Shallow(row); var point = row.GetProperty("created");
        var state = MemoryV2DataLoader.Graph(point.GetProperty("state")) as Dictionary<string, object?> ?? throw new InvalidDataException("Forge state");
        editState(state);
        var stateElement = Element(state);
        Dictionary<string, object?> shape;
        if (editShape == null) shape = ShapeOf(stateElement);
        else { shape = MemoryV2DataLoader.Graph(point.GetProperty("fieldShape")) as Dictionary<string, object?> ?? throw new InvalidDataException("Forge shape"); editShape(shape); }
        string? hash = TryHash(state);
        var forged = O("state", stateElement, "stateHash", hash);
        if (hash == null) forged["canonicalError"] = "NON_CANONICAL_NUMBER";
        forged["jsonHash"] = Sha(stateElement.GetRawText()); forged["rngHash"] = TryHash(state.GetValueOrDefault("rng")); forged["mapHash"] = TryHash(state.GetValueOrDefault("map"));
        forged["logHash"] = TryHash(state.GetValueOrDefault("log")); forged["fieldShape"] = shape; forged["shapeHash"] = TryHash(shape);
        var diagnostics = forged.Where(p => p.Key != "state" && p.Key != "fieldShape").ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        graph["created"] = forged; graph["afterQueries"] = forged;
        graph["queries"] = row.GetProperty("queries").EnumerateArray().Select(q => { var query = Shallow(q); query["before"] = diagnostics; query["after"] = diagnostics; return (object?)query; }).ToList();
        return Element(graph);
    }
    static void RoundFractions(object? node)
    {
        if (node is Dictionary<string, object?> fields)
        {
            foreach (string key in fields.Keys.ToList()) { if (fields[key] is double value) fields[key] = (long)Math.Round(value); else RoundFractions(fields[key]); }
        }
        else if (node is List<object?> list)
        {
            for (int i = 0; i < list.Count; i++) { if (list[i] is double item) list[i] = (long)Math.Round(item); else RoundFractions(list[i]); }
        }
    }

    static void FixtureProbes(JsonElement fixture, JsonElement content, string root, List<JsonElement> rows, Dictionary<string, JsonElement> byId, List<PlanCase> plan,
        Dictionary<string, object?> audit, RunData data, string definitions)
    {
        string zeros = new string('0', 64);
        // Header: the case array is irrelevant to ValidateHeader, so probes stay small and never touch the checked-in fixture.
        var header = Shallow(fixture); header["cases"] = L();
        Control("header", () => ValidateHeader(Element(header), content, root, audit));
        void HeaderProbe(string label, string key, object? value, bool remove = false)
        {
            var bad = new Dictionary<string, object?>(header, StringComparer.Ordinal); if (remove) bad.Remove(key); else bad[key] = value;
            Probe("header " + label, () => ValidateHeader(Element(bad), content, root, audit));
        }
        HeaderProbe("schema", "schema", "mf-run-create-v0"); HeaderProbe("status", "status", "INCOMPLETE"); HeaderProbe("scope", "scope", Scope + " plus restoration");
        HeaderProbe("data version", "dataVersion", "0.0.0"); HeaderProbe("extra top-level field", "restoredState", true); HeaderProbe("missing coverage", "coverage", null, true);
        var sources = fixture.GetProperty("sources");
        HeaderProbe("stale engine source", "sources", Edit(sources, "engine", Edit(sources.GetProperty("engine"), "sha256", zeros)));
        HeaderProbe("substituted generator path", "sources", Edit(sources, "generator", Edit(sources.GetProperty("generator"), "path", "../tools/gen_run_create_vectors.mjs")));
        HeaderProbe("extra source", "sources", Edit(sources, "test", sources.GetProperty("engine")));
        HeaderProbe("missing manifest pair source", "sources", Edit(sources, "manifestSha", null, true));
        var auditJson = fixture.GetProperty("inputManifestAudit");
        var changedPaths = audit.GetValueOrDefault("preExistingChangedPaths") as List<object?> ?? throw new InvalidDataException("Audit paths");
        HeaderProbe("forged manifest match", "inputManifestAudit", Edit(auditJson, "manifestMatch", !true.Equals(audit.GetValueOrDefault("manifestMatch"))));
        HeaderProbe("forged manifest entry count", "inputManifestAudit", Edit(auditJson, "entries", Convert.ToInt64(audit.GetValueOrDefault("entries"), CultureInfo.InvariantCulture) + 1));
        HeaderProbe("invented historical change", "inputManifestAudit", Edit(auditJson, "preExistingChangedPaths", changedPaths.Append("forged/path.md").ToList()));
        if (changedPaths.Count != 0) HeaderProbe("hidden historical changes", "inputManifestAudit", Edit(auditJson, "preExistingChangedPaths", L()));
        HeaderProbe("structural error", "inputManifestAudit", Edit(auditJson, "structuralErrors", L("forged")));
        HeaderProbe("regenerated manifest claim", "inputManifestAudit", Edit(auditJson, "regenerated", true));
        var reach = fixture.GetProperty("reachability");
        HeaderProbe("run commands claimed", "reachability", Edit(reach, "runCommands", 1)); HeaderProbe("restoration claimed", "reachability", Edit(reach, "savingAndRestoration", "COMPLETE"));
        HeaderProbe("undefined laundered into null claim", "domains", Edit(fixture.GetProperty("domains"), "undefined", "Query undefined is JSON null"));
        HeaderProbe("native runtime extra field", "nativeDiagnosticsRuntime", Edit(fixture.GetProperty("nativeDiagnosticsRuntime"), "restored", true));
        // Generating Node release is provenance only: another valid release is accepted, any other shape/spelling is rejected.
        var runtimeJson = fixture.GetProperty("nativeDiagnosticsRuntime"); string generatingNode = Text(runtimeJson, "node");
        var foreignRuntime = new Dictionary<string, object?>(header, StringComparer.Ordinal) { ["nativeDiagnosticsRuntime"] = Edit(runtimeJson, "node", generatingNode == "22.14.0" ? "25.9.0" : "22.14.0") };
        Control("header foreign valid Node provenance", () => ValidateHeader(Element(foreignRuntime), content, root, audit));
        foreach (object? node in new object?[] { "forged", "", "v22.14.0", "22.14", "22.14.0.1", "22.14.0-pre", "022.14.0", "22.14.0\n", new string((char)0xff12, 2) + ".14.0", 22L, null, L("22.14.0") })
            HeaderProbe("native runtime node " + Full(node), "nativeDiagnosticsRuntime", Edit(runtimeJson, "node", node));
        HeaderProbe("native runtime missing node", "nativeDiagnosticsRuntime", Edit(runtimeJson, "node", null, true));
        HeaderProbe("native runtime not an object", "nativeDiagnosticsRuntime", generatingNode);

        // Membership/setup/metadata/anchors against the independent ordered source plan.
        Control("membership", () => ValidateMembership(rows, plan));
        void Members(string label, List<JsonElement> variant) => Probe("membership " + label, () => ValidateMembership(variant, plan));
        List<JsonElement> Replace(string id, JsonElement row) { int index = rows.FindIndex(r => Text(r, "id") == id); return rows.Select((r, i) => i == index ? row : r).ToList(); }
        void RowProbe(string label, string id, Action<Dictionary<string, object?>> change) => Members(label, Replace(id, Changed(byId[id], change)));
        void Reseed(Dictionary<string, object?> row, string key, object? value, bool remove = false)
        {
            var setup = Element(Edit((JsonElement)row["setup"]!, key, value, remove)); row["setup"] = setup; row["inputHash"] = Sha(setup.GetRawText());
        }
        Members("missing last case", rows.Take(rows.Count - 1).ToList());
        Members("duplicate case appended", rows.Append(rows[0]).ToList());
        Members("duplicate case replacing last", rows.Take(rows.Count - 1).Append(rows[0]).ToList());
        Members("reordered cases", rows.Skip(1).Take(1).Concat(rows.Take(1)).Concat(rows.Skip(2)).ToList());
        RowProbe("unknown case id", "edge-solo-legacy", r => r["id"] = "edge-solo-restored");
        RowProbe("setup changed with recomputed input hash", "edge-defaults-legacy", r => Reseed(r, "seed", 2));
        RowProbe("v2 branch dropped with recomputed input hash", "character-sera-r0-v2-short", r => Reseed(r, "memoryPolicy", null, true));
        RowProbe("forged input hash", "edge-solo-legacy", r => r["inputHash"] = zeros);
        RowProbe("native row relabeled supported", "native-null-party", r => r["parity"] = "supported");
        RowProbe("unknown parity", "edge-solo-v2", r => r["parity"] = "maybe");
        RowProbe("group changed", "capacity-a0-r0-cost6", r => r["group"] = "edge");
        RowProbe("policy metadata changed", "edge-solo-v2", r => r["policy"] = "legacy");
        RowProbe("extra metadata", "edge-solo-legacy", r => r["restored"] = true);
        RowProbe("typed expected code changed", "domain-level-outside-i32", r => r["typedExpectedCode"] = "INVALID_SNAPSHOT");
        RowProbe("json-only hash domain removed", "edge-opaque-fractional-metadata", r => r.Remove("hashDomain"));
        RowProbe("rejection code changed", "error-zone-0", r => r["error"] = Edit(byId["error-zone-0"].GetProperty("error"), "code", "INVALID_SNAPSHOT"));
        RowProbe("memory error reclassified", "error-memory-empty-key", r => r["error"] = Edit(byId["error-memory-empty-key"].GetProperty("error"), "kind", "RuleError"));
        RowProbe("native class changed", "native-null-party", r => r["error"] = Edit(byId["native-null-party"].GetProperty("error"), "name", "SyntaxError"));
        RowProbe("native diagnostic laundered into code", "native-null-party", r => r["error"] = Edit(byId["native-null-party"].GetProperty("error"), "code", "TYPE_ERROR"));
        RowProbe("created row marked rejected", "edge-solo-legacy", r => r["outcome"] = "rejected");
        RowProbe("rejected row marked created", "error-zone-0", r => r["outcome"] = "created");
        RowProbe("literal anchor removed", "map-seed-0", r => r.Remove("anchors"));
        RowProbe("literal anchor value changed", "map-seed-0", r =>
        {
            var anchors = MemoryV2DataLoader.Graph(byId["map-seed-0"].GetProperty("anchors")) as List<object?> ?? throw new InvalidDataException("Anchor list");
            var facts = Fields(anchors[0]).GetValueOrDefault("facts") as List<object?> ?? throw new InvalidDataException("Anchor facts");
            Fields(facts[0])["value"] = L(0, 0, 0, 0); r["anchors"] = anchors;
        });
        RowProbe("unknown anchor added", "edge-solo-legacy", r => r["anchors"] = byId["map-seed-0"].GetProperty("anchors"));

        var coverage = fixture.GetProperty("coverage");
        Control("coverage", () => ValidateCoverage(coverage, plan));
        void CoverageProbe(string label, string key, object? value) => Probe("coverage " + label, () => ValidateCoverage(Element(Edit(coverage, key, value)), plan));
        CoverageProbe("map seeds truncated", "mapSeeds", Enumerable.Range(0, 299).Select(n => (object?)(long)n).ToList());
        CoverageProbe("legacy card omitted", "legacyCardIds", coverage.GetProperty("legacyCardIds").EnumerateArray().Skip(1).Select(e => (object?)Text(e)).ToList());
        CoverageProbe("boss order changed", "declaredBossIds", coverage.GetProperty("declaredBossIds").EnumerateArray().Reverse().Select(e => (object?)Text(e)).ToList());
        var coverageAnchors = MemoryV2DataLoader.Graph(coverage.GetProperty("anchors")) as List<object?> ?? throw new InvalidDataException("Coverage anchors");
        Fields(coverageAnchors[0])["tag"] = "forged"; CoverageProbe("anchor tag changed", "anchors", coverageAnchors);
        CoverageProbe("full Cartesian claim", "dimensions", "Full Cartesian product");
        CoverageProbe("boss encounter selection claimed", "bossEncounterSelections", 1);
        CoverageProbe("group membership emptied", "groups", Edit(coverage.GetProperty("groups"), "edge", L()));

        var counts = fixture.GetProperty("counts");
        Control("counts", () => ValidateCounts(counts, rows));
        foreach (string key in new[] { "queries", "supportedCreated", "runCommands", "sourceOnlyFactories" })
            Probe("count " + key, () => ValidateCounts(Element(Edit(counts, key, counts.GetProperty(key).GetInt64() + 1)), rows));
        Probe("counts over a shortened case list", () => ValidateCounts(counts, rows.Take(rows.Count - 1).ToList()));

        // Evidence: fixture-integrity and actual C# comparison on single rows, including self-consistent recomputed-hash forgeries.
        void Evidence(string label, JsonElement row) => Probe("evidence " + label, () => RunRow(row, data, definitions));
        var basic = byId["edge-defaults-legacy"]; var zones = byId["edge-empty-zones-legacy"]; var opaque = byId["edge-opaque-fractional-metadata"];
        Control("evidence real row", () => RunRow(basic, data, definitions));
        Control("evidence identity forgery is self-consistent", () => RunRow(Forge(basic, s => { }), data, definitions));
        Control("evidence json-only identity forgery is self-consistent", () => RunRow(Forge(opaque, s => { }), data, definitions));
        foreach (string key in new[] { "stateHash", "jsonHash", "rngHash", "mapHash", "logHash", "shapeHash" })
            Evidence("created " + key + " zeroed", Changed(basic, r => { var bad = Edit(basic.GetProperty("created"), key, zeros); r["created"] = bad; r["afterQueries"] = bad; }));
        Evidence("afterQueries diverges", Changed(basic, r => r["afterQueries"] = Edit(basic.GetProperty("afterQueries"), "logHash", zeros)));
        JsonElement EditQueries(JsonElement row, Action<List<object?>> edit) => Changed(row, r =>
        {
            var list = row.GetProperty("queries").EnumerateArray().Select(q => (object?)Shallow(q)).ToList(); edit(list); r["queries"] = list;
        });
        Evidence("pure query omitted", EditQueries(basic, list => list.RemoveAt(list.Count - 1)));
        Evidence("pure queries reordered", EditQueries(basic, list => { var first = list[1]; list[1] = list[2]; list[2] = first; }));
        Evidence("query result forged with recomputed hash", EditQueries(basic, list =>
        {
            var query = Fields(list[1]); var value = MemoryV2DataLoader.Graph(((JsonElement)query["result"]!).GetProperty("value")) as Dictionary<string, object?> ?? throw new InvalidDataException("Node result");
            value["type"] = "forged"; var result = O("kind", "json", "value", value); query["result"] = result; query["resultHash"] = TryHash(result);
        }));
        Evidence("missing node null laundered into undefined", EditQueries(basic, list =>
        {
            var query = Fields(list[^3]); var result = O("kind", "undefined"); query["result"] = result; query["resultHash"] = TryHash(result);
        }));
        Evidence("query diagnostics drift", EditQueries(basic, list => { var query = Fields(list[0]); query["before"] = Edit((JsonElement)query["before"]!, "stateHash", zeros); }));
        Evidence("party HP forged with recomputed hashes", Forge(basic, s => s["hp"] = 244L));
        Evidence("private RNG forged with recomputed hashes", Forge(basic, s => s["rng"] = L(1L, 2L, 3L, 4L)));
        Evidence("map node type forged with recomputed hashes", Forge(basic, s =>
        {
            var map = Fields(s["map"]); var floors = map.GetValueOrDefault("floors") as List<object?> ?? throw new InvalidDataException("Floors");
            Fields((floors[3] as List<object?> ?? throw new InvalidDataException("Floor"))[0])["type"] = "elite";
        }));
        Evidence("deck key forged with recomputed hashes", Forge(basic, s => Fields((s["deck"] as List<object?> ?? throw new InvalidDataException("Deck"))[0])["key"] = "k99"));
        Evidence("absent legacy field laundered into null", Forge(basic, s => s["memoryPolicy"] = null));
        Evidence("own-undefined zone laundered into null", Forge(zones, s => s["zone"] = null));
        Evidence("own-undefined list dropped from field shape", Forge(zones, s => { }, shape => shape["undefined"] = L()));
        Evidence("fractions rounded with recomputed canonical hash", Forge(opaque, RoundFractions));
        Evidence("fabricated canonical hash for non-canonical state", Changed(opaque, r =>
        {
            var bad = Edit(opaque.GetProperty("created"), "stateHash", zeros); bad.Remove("canonicalError"); r["created"] = bad; r["afterQueries"] = bad;
        }));
    }

    static void GzipProbes()
    {
        // Native .NET DEFLATE payload and footer; the exporter's fixed MTIME0/XFL2 header is pinned so each probe fails only for its own reason.
        byte[] Gzip(byte[] json, CompressionLevel level = CompressionLevel.SmallestSize)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, level, true)) gzip.Write(json);
            var bytes = output.ToArray(); Require(bytes.Length >= 20 && bytes[3] == 0, "Native .NET gzip member has optional header fields");
            Array.Clear(bytes, 4, 4); bytes[8] = 2; return bytes;
        }
        byte[] With(byte[] bytes, int index, byte value) { var copy = (byte[])bytes.Clone(); copy[index] = value; return copy; }
        void Decodes(byte[] bytes) { using var document = Decode(bytes); UniqueKeys(document.RootElement); }
        bool Container(Exception error) => error is JsonException || error is DecoderFallbackException;
        void Bad(string label, byte[] bytes) => Probe("container " + label, () => Decodes(bytes), Container);
        var valid = Encoding.UTF8.GetBytes("{\"ok\":[1,1.25,-0.5,\"\\ud800\"],\"\\ud800\":true}\n"); var packed = Gzip(valid);
        Control("synthetic gzip/UTF-8/JSON with fractions and lone-surrogate escapes", () => Decodes(packed), Container);
        // Compressed bytes are never compared: other valid DEFLATE encodings and writer OS codes of the same JSON are accepted.
        Control("stored DEFLATE encoding of the same JSON", () => Decodes(Gzip(valid, CompressionLevel.NoCompression)), Container);
        Control("fastest DEFLATE encoding of the same JSON", () => Decodes(Gzip(valid, CompressionLevel.Fastest)), Container);
        foreach (byte os in new byte[] { 3, 10, 19, 255 }) Control("gzip writer OS byte " + os, () => Decodes(With(packed, 9, os)), Container);
        Bad("truncated gzip footer", packed[..^8]);
        Bad("truncated inside gzip footer", packed[..^3]);
        Bad("truncated last DEFLATE byte", packed[..^9]);
        Bad("truncated mid-DEFLATE", packed[..(packed.Length / 2)]);
        var crc = (byte[])packed.Clone(); crc[^8] ^= 1; Bad("corrupt gzip CRC", crc);
        var size = (byte[])packed.Clone(); size[^1] ^= 1; Bad("corrupt gzip ISIZE", size);
        Bad("corrupt DEFLATE block type", With(packed, 10, (byte)(packed[10] ^ 2)));
        var flags = (byte[])packed.Clone(); flags[3] = 8; Bad("gzip header name flag", flags);
        Bad("gzip header MTIME", With(packed, 4, 1));
        Bad("gzip header XFL fastest", With(packed, 8, 4));
        Bad("gzip header XFL default", With(packed, 8, 0));
        // .NET GZipStream writes no member for empty input, so the RFC 1952 empty member (exporter header, final empty fixed block 03 00,
        // CRC32 0, ISIZE 0) is a literal; native GZipStream must accept it as a real member or these concatenation probes are void.
        byte[] empty = { 0x1f, 0x8b, 8, 0, 0, 0, 0, 0, 2, 3, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        using (var member = new GZipStream(new MemoryStream(empty, false), CompressionMode.Decompress)) Require(member.Read(new byte[1]) == 0, "Empty gzip member literal is not a valid native member");
        Bad("empty gzip member before valid JSON", empty.Concat(packed).ToArray());
        Bad("empty gzip member after valid JSON", packed.Concat(empty).ToArray());
        Bad("concatenated gzip members", packed.Concat(Gzip(Encoding.UTF8.GetBytes("{}"))).ToArray());
        Bad("trailing bytes after gzip member", packed.Concat(new byte[] { 0, 1, 2, 3 }).ToArray());
        Bad("single trailing zero byte", packed.Concat(new byte[1]).ToArray());
        Bad("eight trailing zero bytes", packed.Concat(new byte[8]).ToArray());
        Bad("UTF-8 BOM", Gzip(new byte[] { 0xef, 0xbb, 0xbf }.Concat(valid).ToArray()));
        Bad("invalid UTF-8", Gzip(new byte[] { (byte)'"', 0xc3, 0x28, (byte)'"' }));
        Bad("duplicate JSON key", Gzip(Encoding.UTF8.GetBytes("{\"a\":1,\"a\":2}")));
        Bad("escape-equivalent duplicate JSON key", Gzip(Encoding.UTF8.GetBytes("{\"a\":1,\"\\u0061\":2}")));
        Bad("trailing JSON value", Gzip(Encoding.UTF8.GetBytes("{} {}")));
        Bad("truncated JSON", Gzip(Encoding.UTF8.GetBytes("{\"a\":[1,")));
        Bad("empty container", new byte[0]);
    }

    static void IdentityProbes(Dictionary<string, JsonElement> byId, RunData data, string definitions)
    {
        var field = typeof(ExpeditionRun).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidDataException("Run live state field changed; identity probe cannot inspect it");
        HashSet<object> Live(ExpeditionRun run) => References(field.GetValue(run));
        var dataReferences = References(data);
        foreach (string id in new[] { "edge-all10-party-v2", "edge-run-key-collisions", "edge-use-memory-false-evolved", "edge-sparse-card-metadata-v2", "edge-sparse-card-metadata-legacy",
            "edge-opaque-fractional-metadata", "map-seed-4", "edge-boss-b_bell-v2", "stage-60-hard", "edge-nonparty-memory-preserved", "edge-legacy-common-assigned-is-preserved", "edge-empty-zones-v2" })
        {
            Require(byId.TryGetValue(id, out var row), "Identity probe case missing: " + id);
            var setup = RunDataLoader.Setup(row.GetProperty("setup")); string setupBefore = SetupPrint(setup);
            var first = ExpeditionRun.Create(data, setup); var second = ExpeditionRun.Create(data, setup);
            var live = Live(first); identityProbes++;
            Check(live.Count > 0 && !live.Overlaps(Live(second)), id + " independent runs share no mutable state/collection/DTO identity");
            Check(!live.Overlaps(References(setup)), id + " live run shares no mutable object with its setup");
            Check(!live.Overlaps(dataReferences), id + " live run shares no mutable object with source definitions");
            var snapshot = first.State; Check(!live.Overlaps(References(snapshot)), id + " State snapshot is fully detached");
            var map = snapshot.Map ?? throw new InvalidDataException(id + " created run has no map");
            var nodes = map.Floors.SelectMany(r => r).Select(n => n.Id).Append(map.Boss.Id).ToList();
            Check(!live.Overlaps(References(first.Available())) && nodes.All(n => !live.Overlaps(References(first.NodeById(n)))), id + " Available/NodeById results are detached");
            string before = Full(first.State.ToJsonGraph()), other = Full(second.State.ToJsonGraph()); var available = first.Available();
            MutateSnapshot(snapshot); MutateSetup(setup);
            foreach (string node in nodes) { var returned = first.NodeById(node); if (returned != null) { returned.Type = "caller-mutation"; returned.Next?.Clear(); } }
            first.Available().Clear();
            Check(Full(first.State.ToJsonGraph()) == before && Full(second.State.ToJsonGraph()) == other && first.Available().SequenceEqual(available),
                id + " snapshot/setup/returned-node mutation cannot reach either live run");
            Check(SetupPrint(setup) != setupBefore, id + " setup mutation probe actually changed the caller setup");
            WithMutatedData(data, () => Check(Full(first.State.ToJsonGraph()) == before, id + " definition mutation cannot reach a created run"));
            Check(DataPrint(data) == definitions, id + " source definitions restored after mutation probe");
        }
    }
    // Bounded identity walk: Core DTO fields, dictionary values and list items. Strings/boxed values are immutable and skipped.
    static HashSet<object> References(object? root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance); var pending = new Stack<object?>(); pending.Push(root);
        var core = typeof(RunState).Assembly;
        while (pending.Count != 0)
        {
            object? value = pending.Pop();
            if (value == null || value is string || value.GetType().IsValueType || value.GetType() == typeof(object) || !seen.Add(value)) continue;
            if (value is IDictionary dictionary) { foreach (DictionaryEntry entry in dictionary) pending.Push(entry.Value); }
            else if (value is IEnumerable sequence) { foreach (object? item in sequence) pending.Push(item); }
            else if (value.GetType().Assembly == core)
                foreach (var member in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) pending.Push(member.GetValue(value));
        }
        return seen;
    }
    static void MutateSnapshot(RunState s)
    {
        s.Rng[0] ^= 1; s.Seed = -1; s.Phase = "DONE"; s.Position = "caller-mutation"; s.Zones.Add(99); s.Relics.Add("caller-mutation"); s.PartsFound.Add("caller-mutation"); s.Log.Add("caller-mutation");
        s.Offered["caller-mutation"] = 1; s.RunFlags["envelopeUsed"] = true; s.Statistics.Battles = 9; s.Difficulty.Hp = -1;
        if (s.Map != null) { foreach (var node in s.Map.Floors.SelectMany(r => r)) { node.Type = "caller-mutation"; node.Next?.Add("caller-mutation"); } s.Map.Boss.Id = "caller-mutation"; s.Map.Floors.Clear(); }
        foreach (var party in s.Party) { party.Hp = -1; party.CharId = "caller-mutation"; }
        foreach (var card in s.Deck) { card.Key = "caller-mutation"; card.Owner = "caller-mutation"; card.Weapon = !card.Weapon; }
        s.Deck.Clear(); s.Party.Clear(); ClearLists(s.StartMemory); ClearLists(s.Bosses); s.MemoryBefore = null; s.MemoryBaseline = null; s.PlayBefore = null;
    }
    static void MutateSetup(RunSetup s)
    {
        foreach (var party in s.Party) { party.Level = 59; party.Hp = -1; party.WeaponId = "caller-mutation"; }
        ClearLists(s.Memory); ClearLists(s.Bosses); s.Relics.Add("caller-mutation"); s.Zones?.Add(99); if (s.Difficulty != null) s.Difficulty.Hp = -1;
    }
    static void ClearLists(object? node)
    {
        if (node is IDictionary<string, object?> fields) { foreach (object? value in fields.Values.ToList()) ClearLists(value); fields["caller-mutation"] = true; }
        else if (node is IList list) { foreach (object? item in list.Cast<object?>().ToList()) ClearLists(item); if (!list.IsFixedSize) list.Clear(); }
    }
    static void WithMutatedData(RunData data, Action check)
    {
        var card = data.Memory.Battle.Cards[0]; var weapon = data.Memory.Battle.Weapons[0]; var character = data.Characters[0];
        string owner = card.Owner, weaponCard = weapon.Card, weaponType = character.WeaponType; int hpBase = data.Stats.HpBase;
        var common = new Dictionary<string, string>(data.Memory.StartingCommon, StringComparer.Ordinal);
        try
        {
            card.Owner = "caller-mutation"; weapon.Card = "caller-mutation"; character.WeaponType = "caller-mutation"; data.Stats.HpBase += 1000;
            foreach (string key in common.Keys) data.Memory.StartingCommon[key] = "caller-mutation";
            check();
        }
        finally
        {
            card.Owner = owner; weapon.Card = weaponCard; character.WeaponType = weaponType; data.Stats.HpBase = hpBase;
            foreach (var pair in common) data.Memory.StartingCommon[pair.Key] = pair.Value;
        }
    }

    static void BoundaryProbes(RunData data, string definitions, JsonElement content)
    {
        RunSetup Setup(params object?[] fields)
        {
            var setup = new RunSetup(); var actor = O("charId", "sera"); foreach (var pair in O(fields)) actor[pair.Key] = pair.Value;
            setup.Party.Add(RunPartyEntry.FromJsonGraph(actor)); return setup;
        }
        RunSetup V2(string runId, params object?[] fields) { var setup = Setup(fields); setup.MemoryPolicy = "v2"; setup.RunId = runId; return setup; }
        void Expect(string label, RunSetup setup, string expected)
        {
            string before = SetupPrint(setup), outcome = Outcome(() => ExpeditionRun.Create(data, setup)); boundaryProbes++;
            Check(outcome == expected && SetupPrint(setup) == before && DataPrint(data) == definitions,
                "typed boundary " + label + " -> " + expected + (outcome == expected ? "" : " (C# " + outcome + ")") + "; setup/definitions preserved");
        }
        string N(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
        foreach (long seed in new[] { CanonicalState.MaxSafeInteger + 1, -CanonicalState.MaxSafeInteger - 1, long.MaxValue, long.MinValue })
        { var s = Setup(); s.Seed = seed; Expect("seed " + I(seed) + " outside JS safe integers", s, TypedCode); }
        foreach (long seed in new[] { CanonicalState.MaxSafeInteger, -CanonicalState.MaxSafeInteger, 0 }) { var s = Setup(); s.Seed = seed; Expect("seed " + I(seed), s, "created"); }
        foreach (string key in new[] { "level", "weaponLevel", "armor", "accessory", "skillLv", "ultLv" })
            foreach (object value in new object[] { 2147483648L, -2147483649L, 20.5, "20" })
                Expect($"party {key}={N(value)} (typed int32 domain; JS would coerce/floor, not parity)", Setup("level", 20, key, value), TypedCode);
        Expect("v2 capacity resonance outside int32", V2("boundary-res", "res", 2147483648L), TypedCode);
        Expect("legacy unread resonance stays preserved metadata", Setup("res", 2147483648L), "created");
        foreach (var (label, hp, dmg) in new (string, object, object)[] { ("int32 max overflow", int.MaxValue, int.MaxValue), ("int32 min overflow", int.MinValue, int.MinValue), ("fraction", 1.5, 100), ("string", "120", 100) })
        { var s = Setup(); s.StageLevel = 60; s.Difficulty = RunDifficulty.FromJsonGraph(O("hp", hp, "dmg", dmg)); Expect("stage60 difficulty " + label, s, TypedCode); }
        var preserved = Setup(); preserved.Difficulty = RunDifficulty.FromJsonGraph(O("hp", 1.5, "dmg", -0.5)); boundaryProbes++;
        var graph = ExpeditionRun.Create(data, preserved).State.ToJsonGraph(); var difficulty = graph.GetValueOrDefault("difficulty") as Dictionary<string, object?>;
        Check(difficulty != null && difficulty.GetValueOrDefault("hp") is double hpValue && hpValue == 1.5 && difficulty.GetValueOrDefault("dmg") is double dmgValue && dmgValue == -0.5 && TryHash(graph) == null,
            "stage-less fractional difficulty is copied unrounded and stays outside the integer canonical hash");
        foreach (var (label, runId) in new (string, object?)[] { ("missing", null), ("empty", ""), ("number", 5L) }) { var s = V2("unused"); s.RunId = runId; Expect("v2 runId " + label, s, "RUN_ID_REQUIRED"); }
        foreach (var (label, version) in new (string, object?)[] { ("negative", -1L), ("fraction", 1.5), ("bool", true), ("string", "1") })
        { var s = V2("boundary-version"); s.MemoryVersion = version; Expect("v2 memoryVersion " + label, s, "INVALID_SNAPSHOT"); }
        foreach (object version in new object[] { 5, 5u, 5L, 5.0, CanonicalState.MaxSafeInteger })
        { var s = V2("boundary-version"); s.MemoryVersion = version; Expect("v2 integral memoryVersion " + version.GetType().Name, s, "created"); }
        { var s = V2("unsafe-version"); s.MemoryVersion = CanonicalState.MaxSafeInteger + 1; Expect("v2 memoryVersion 2^53 outside the documented typed domain (JS accepts; not parity)", s, "INVALID_SNAPSHOT"); }
        { var s = V2("empty-party"); s.Party.Clear(); Expect("v2 empty party", s, "INVALID_SNAPSHOT"); }
        { var s = V2("duplicate-party"); s.Party.Add(RunPartyEntry.FromJsonGraph(O("charId", "sera"))); Expect("v2 duplicate party", s, "INVALID_SNAPSHOT"); }
        { var s = V2("unknown-party"); s.Party[0].CharId = "missing"; Expect("v2 unknown character", s, "INVALID_SNAPSHOT"); }
        { var s = V2("bad-memory"); s.Memory = O("sera", L(O("key", "", "id", "sera_round"))); Expect("v2 empty stable key", s, "IDENTITY_REQUIRED"); }
        { var s = Setup(); s.Zones = new List<int> { 1, 3 }; Expect("later unauthored zone", s, "ZONE_NOT_AUTHORED"); }
        foreach (var (label, json) in new[] { ("seed fraction", "\"seed\":1.5"), ("seed string", "\"seed\":\"1\""), ("stage fraction", "\"stageLevel\":1.5"), ("stage outside int32", "\"stageLevel\":2147483648"),
            ("zone fraction", "\"zones\":[1.5]"), ("zone outside int32", "\"zones\":[2147483648]"), ("difficulty fraction", "\"difficulty\":{\"hp\":1.5}") })
        {
            using var document = JsonDocument.Parse("{\"party\":[{\"charId\":\"sera\"}]," + json + "}"); boundaryProbes++;
            string outcome; try { RunDataLoader.Setup(document.RootElement); outcome = "loaded"; } catch (RuleException error) { outcome = error.Code; }
            Check(outcome == TypedCode, "host adapter typed boundary " + label + " -> " + TypedCode + (outcome == TypedCode ? "" : " (" + outcome + ")"));
        }
        // Public pure helpers against raw authored data: BaseKit has no stable keys; DefaultWeapon follows the authored weapon type.
        var types = O("ranged", "w_basic_ranged", "melee", "w_basic_melee", "device", "w_basic_device", "ritual", "w_basic_ritual");
        var startingCommon = content.GetProperty("startingCommon");
        foreach (var character in content.GetProperty("characters").EnumerateArray())
        {
            string cid = Text(character, "id"); boundaryProbes++;
            var kit = content.GetProperty("cards").EnumerateArray().Where(c => Text(c, "owner") == cid && Truthy(c, "start")).Select(c => (object?)O("id", Text(c, "id"))).ToList();
            var common = O(); if (startingCommon.TryGetProperty(cid, out var commonId)) common["id"] = Text(commonId); common["assigned"] = cid; kit.Add(common);
            Check(MemoryV2DataLoader.Difference(kit, ExpeditionRun.BaseKit(data, cid).Select(spec => (object?)spec.ToJsonGraph()).ToList()) == null, cid + " BaseKit equals authored start cards + startingCommon without stable keys");
            Check(ExpeditionRun.DefaultWeapon(data, cid) == types.GetValueOrDefault(Text(character, "weapon")) as string, cid + " DefaultWeapon follows authored weapon type");
        }
        string Native(Action action) { try { action(); return "none"; } catch (MemoryBoundaryException error) { return error.JavaScriptName; } }
        Check(Native(() => ExpeditionRun.DefaultWeapon(data, "missing")) == "TypeError", "DefaultWeapon of an unknown character is a TypeError like JS");
        var first = data.Characters[0]; string type = first.WeaponType;
        try { first.WeaponType = "future"; Check(Native(() => ExpeditionRun.DefaultWeapon(data, first.Id)) == "TypeError", "documented R1/domain follow-up: future weapon type throws TypeError in C# while JS defaultWeapon returns undefined"); }
        finally { first.WeaponType = type; }
        Check(DataPrint(data) == definitions, "typed boundary probes preserve source definitions");
    }

    static void RunRow(JsonElement row, RunData data, string definitions, List<string>? diagnostics = null)
    {
        string id = Text(row, "id");
        switch (Text(row, "parity"))
        {
            case "js-source-only": string line = SourceOnly(row, data, definitions, id); diagnostics?.Add(line); break;
            case "typed-domain-rejection": TypedDomain(row, data, definitions, id); break;
            default: Supported(row, data, definitions, id); break;
        }
    }
    static void Section(string label, Action action)
    {
        try { action(); }
        catch (Exception error) { probing = false; Check(false, label + " guard section: " + (error is InvalidDataException ? error.Message : error.ToString())); }
    }

    static List<JsonElement> ValidateFixture(JsonElement fixture, JsonElement content, string root, List<PlanCase> plan, Dictionary<string, object?> audit)
    {
        ValidateHeader(fixture, content, root, audit);
        var rows = fixture.GetProperty("cases").EnumerateArray().ToList();
        ValidateMembership(rows, plan);
        ValidateCoverage(fixture.GetProperty("coverage"), plan);
        ValidateCounts(fixture.GetProperty("counts"), rows);
        return rows;
    }

    static void ValidateHeader(JsonElement fixture, JsonElement content, string root, Dictionary<string, object?> audit)
    {
        RequireKeys(fixture, TopFields, "Run-create fixture top-level fields");
        Require(Text(fixture, "schema") == Schema && Text(fixture, "status") == Status && Text(fixture, "scope") == Scope, "Wrong run-create schema/status/scope");
        Require(Text(fixture, "dataVersion") == Text(content.GetProperty("meta"), "version"), "Wrong run-create data version");
        var sources = fixture.GetProperty("sources"); RequireKeys(sources, Sources.Keys.ToArray(), "Run-create source membership");
        foreach (var source in Sources)
        {
            var record = sources.GetProperty(source.Key); RequireKeys(record, new[] { "path", "sha256" }, "Run-create source record " + source.Key);
            Require(Text(record, "path") == source.Value && HashText(record.GetProperty("sha256")) == FileHash(Path.Combine(root, source.Value)), "Stale/substituted run-create source: " + source.Key);
        }
        // Same contract as the JS generator: exactly { node: "major.minor.patch" } (ASCII digits, no leading zeros); provenance only.
        var runtime = fixture.GetProperty("nativeDiagnosticsRuntime");
        Require(runtime.ValueKind == JsonValueKind.Object, "Native diagnostic runtime record must be an object"); RequireKeys(runtime, new[] { "node" }, "Native diagnostic runtime record");
        var node = runtime.GetProperty("node");
        Require(node.ValueKind == JsonValueKind.String && Regex.IsMatch(node.GetString()!, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z"), "Native diagnostic runtime node must be a numeric semver");
        RequireGraph(fixture.GetProperty("domains"), Domains, "Run-create numeric/undefined domain claims changed");
        RequireGraph(fixture.GetProperty("reachability"), Reachability, "Run-create reachability claims changed");
        RequireGraph(fixture.GetProperty("inputManifestAudit"), audit, "Run-create input manifest audit differs from the recomputed repository audit");
    }

    static void ValidateMembership(List<JsonElement> rows, List<PlanCase> plan)
    {
        Require(plan.Count == Counts["cases"] && rows.Count == plan.Count, "Run-create case count differs from the reconstructed source plan");
        var ids = new HashSet<string>(StringComparer.Ordinal); var anchors = AnchorTable();
        var memoryCodes = new HashSet<string>(new[] { "IDENTITY_REQUIRED", "INVALID_CARD", "INVALID_ASSIGNMENT", "INVALID_UPGRADE", "INVALID_PART", "INVALID_EVOLUTION" }, StringComparer.Ordinal);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; var expected = plan[i]; string id = Text(row, "id");
            Require(id == expected.Id && ids.Add(id), "Run-create case " + I(i) + " is " + id + ", source plan expects " + expected.Id + " (missing/duplicate/reordered membership)");
            var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in row.EnumerateObject()) if (!RowFields.Contains(property.Name)) metadata.Add(property.Name, MemoryV2DataLoader.Graph(property.Value));
            Require(MemoryV2DataLoader.Difference(metadata, expected.Metadata) == null, id + " group/parity/policy/branch metadata differs from the source plan");
            Require(MemoryV2DataLoader.Difference(MemoryV2DataLoader.Graph(row.GetProperty("setup")), expected.Setup) == null, id + " setup differs from the authored source recipe");
            Require(HashText(row.GetProperty("inputHash")) == Sha(row.GetProperty("setup").GetRawText()), id + " input hash differs from exact setup JSON");
            string parity = Text(row, "parity"), group = Text(row, "group"), outcome = Text(row, "outcome");
            Require(parity is "supported" or "supported-json-metadata" or "typed-domain-rejection" or "js-source-only", id + " unknown parity classification " + parity);
            Require((group == "domain") == (parity == "typed-domain-rejection") && (group == "native-error") == (parity == "js-source-only"), id + " parity/group classification mismatch");
            Require(parity != "supported-json-metadata" || OptionalText(row, "hashDomain") == "json-only", id + " JSON metadata row lost its json-only hash domain");
            if (expected.Code == null && expected.Name == null)
                Require(outcome == "created" && row.TryGetProperty("created", out _) && row.TryGetProperty("queries", out _) && row.TryGetProperty("afterQueries", out _) && !row.TryGetProperty("error", out _), id + " source plan creates this run");
            else
            {
                Require(outcome == "rejected" && row.TryGetProperty("error", out _) && !row.TryGetProperty("created", out _) && !row.TryGetProperty("queries", out _) && !row.TryGetProperty("afterQueries", out _), id + " source plan rejects this input");
                var error = row.GetProperty("error");
                if (expected.Code != null)
                {
                    RequireKeys(error, new[] { "kind", "name", "code", "message" }, id + " coded rejection fields");
                    Require(Text(error, "code") == expected.Code && Text(error, "kind") == (memoryCodes.Contains(expected.Code) ? "MemoryError" : "RuleError") && Text(error, "name") == "Error" && Text(error, "message") == expected.Code,
                        id + " rejection code/class differs from the source plan");
                }
                else
                {
                    RequireKeys(error, new[] { "kind", "name", "message" }, id + " native diagnostic fields");
                    Require(Text(error, "kind") == "NativeError" && Text(error, "name") == expected.Name && Text(error, "message").Length != 0, id + " JS-native diagnostic class differs from the source plan");
                }
            }
            if (anchors.TryGetValue(id, out var anchor))
                Require(row.TryGetProperty("anchors", out var actual) && MemoryV2DataLoader.Difference(MemoryV2DataLoader.Graph(actual), L(anchor)) == null, id + " literal anchor differs from the independent anchor table");
            else Require(!row.TryGetProperty("anchors", out _), id + " carries an anchor absent from the independent anchor table");
        }
        Require(anchors.Count == 26 && anchors.Keys.All(ids.Contains), "Independent literal anchor table incomplete");
    }

    static void ValidateCoverage(JsonElement coverage, List<PlanCase> plan)
    {
        string M(PlanCase c, string key) => Convert.ToString(c.Metadata.GetValueOrDefault(key), CultureInfo.InvariantCulture) ?? "";
        List<object?> Of(string group, Func<PlanCase, object?> value) => plan.Where(c => M(c, "group") == group).Select(value).ToList();
        var groups = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var item in plan) { string g = M(item, "group"); if (!groups.ContainsKey(g)) groups.Add(g, Of(g, c => c.Id)); }
        var anchors = AnchorTable();
        var expected = O("caseIds", plan.Select(c => (object?)c.Id).ToList(), "groups", groups,
            "characterResonancePolicyLengths", Of("character", c => M(c, "characterId") + ":" + M(c, "resonance") + ":" + M(c, "policy") + ":" + (c.Setup.GetValueOrDefault("length") as string)),
            "weaponRefinements", Of("weapon", c => M(c, "weaponId") + ":r" + M(c, "refine")), "levels", Of("level", c => c.Metadata.GetValueOrDefault("level")),
            "weaponRarityLevels", Of("weapon-level", c => M(c, "rarity") + ":" + M(c, "weaponLevel")), "gearEnhancementSlots", Of("gear", c => M(c, "slot") + ":" + M(c, "enhance")),
            "skills", Of("skill", c => M(c, "characterId") + ":" + M(c, "skillLv")),
            "stages", Of("stage", c => M(c, "stageLevel") + ":" + (true.Equals(c.Metadata.GetValueOrDefault("hard")) ? "hard" : "normal")),
            "mapSeeds", Of("map", c => c.Metadata.GetValueOrDefault("seed")), "legacyCardIds", Of("legacy-card", c => c.Metadata.GetValueOrDefault("cardId")),
            "stableMemoryCardVariants", Of("memory-card", c => M(c, "cardId") + ":" + (true.Equals(c.Metadata.GetValueOrDefault("plus")) ? "plus" : "base")),
            "permanentPartPolicies", Of("part", c => M(c, "partId") + ":" + M(c, "policy")), "relicIds", Of("relic", c => c.Metadata.GetValueOrDefault("relicId")),
            "declaredBossIds", Of("edge", c => c.Metadata.GetValueOrDefault("bossId")).Where(b => b != null).Distinct().ToList(),
            "anchors", plan.Where(c => anchors.ContainsKey(c.Id)).Select(c => (object?)O("caseId", c.Id, "anchorIndex", 0, "tag", anchors[c.Id]["tag"])).ToList(),
            "dimensions", Dimensions, "bossEncounterSelections", 0);
        RequireGraph(coverage, expected, "Run-create coverage arrays differ from the reconstructed source plan");
    }

    static void ValidateCounts(JsonElement counts, List<JsonElement> rows)
    {
        var computed = Counts.Keys.ToDictionary(k => k, k => 0L, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            string parity = Text(row, "parity"); bool supported = parity.StartsWith("supported", StringComparison.Ordinal), made = Text(row, "outcome") == "created";
            long count = made ? row.GetProperty("queries").GetArrayLength() : 0;
            computed["cases"]++; computed["factories"]++; computed[made ? "created" : "rejected"]++; computed["queries"] += count;
            if (supported) { computed["supportedFactories"]++; computed[made ? "supportedCreated" : "supportedRejected"]++; computed["supportedQueries"] += count; }
            if (parity == "js-source-only") computed["sourceOnlyFactories"]++;
            if (Text(row, "group") == "domain") computed["typedDomainProbes"]++;
        }
        RequireKeys(counts, Counts.Keys.ToArray(), "Run-create count fields");
        foreach (var pair in Counts)
            Require(counts.GetProperty(pair.Key).GetInt64() == pair.Value && computed[pair.Key] == pair.Value, "Run-create count " + pair.Key + " differs from the current source plan");
    }

    // The generator's 26 literal anchors, transcribed independently; fixture anchors must equal this table exactly.
    static Dictionary<string, Dictionary<string, object?>> AnchorTable()
    {
        var table = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        void Put(string id, string tag, params Dictionary<string, object?>[] facts) => table.Add(id, O("tag", tag, "facts", facts.Select(f => (object?)f).ToList()));
        List<object?> StatePath(object?[] path) { var result = L("state"); result.AddRange(path); return result; }
        Dictionary<string, object?> F(object? value, params object?[] path) => O("path", StatePath(path), "value", value);
        Dictionary<string, object?> Gone(params object?[] path) => O("path", StatePath(path), "absent", true);
        Put("edge-defaults-legacy", "normal-source-party243", F(243, "hp"), F(243, "maxHp"), F(243, "hpRef"), F(60, "coins"), F(18, "deck", "length"), F(18, "seqCard"), F("MAP", "phase"), F(null, "battle"), Gone("memoryPolicy"));
        Put("stats-sera-default-level20", "default-weapon-does-not-invent-stat-bonus", F(96, "party", 0, "power4"), F(80, "party", 0, "hp"), F("wc_basic_ranged", "deck", 5, "id"), Gone("party", 0, "weaponId"));
        Put("weapon-w_sera-r2", "supplied-SSR-level20", F(112, "party", 0, "power4"), F(80, "party", 0, "hp"), F(2, "party", 0, "refine"));
        Put("stats-harin-maximum", "level60-armor15-skill10", F(136, "party", 0, "power4"), F(151, "party", 0, "hp"), F(1000, "party", 0, "gearBps"), F(10, "party", 0, "skillLv"), F(10, "party", 0, "ultLv"));
        Put("gear-both-0", "zero-enhance-is-equipped", F(84, "party", 0, "hp"), F(250, "party", 0, "gearBps"));
        Put("gear-both-15", "gear-floor-boundary", F(106, "party", 0, "hp"), F(1000, "party", 0, "gearBps"));
        Put("stats-sera-supplied-derived-ignored", "derived-inputs-overwritten", F(160, "party", 0, "power4"), F(81, "party", 0, "hp"), F(0, "party", 0, "gearBps"));
        foreach (var (id, hp, dmg) in new[] { ("stage-1-normal", 53, 75), ("stage-1-hard", 64, 94), ("stage-60-normal", 100, 148), ("edge-difficulty-negative-stage1", -26, -1), ("edge-difficulty-zeros-stage1", 53, 75), ("edge-difficulty-zeros-stage0", 0, 0) })
            Put(id, "stage-rounding-and-source-zero-fallback", F(O("hp", hp, "dmg", dmg), "difficulty"));
        Put("edge-difficulty-empty-stage0", "empty-difficulty-preserves-absence", F(O(), "difficulty"), F(null, "stageLevel"));
        Put("edge-empty-zones-v2", "source-own-undefined-is-not-null", Gone("zone"), Gone("map", "zone"), O("path", L("fieldShape", "undefined"), "value", L(L("zone"), L("map", "zone"))), F("zundefinedboss", "map", "boss", "id"));
        Put("edge-run-key-collisions", "shared-key-sequence-skips-before-and-deck", F(6, "seqCard"), F("run:collision:3", "deck", 8, "key"), F("run:collision:5", "deck", 14, "key"), F("run:collision:6", "deck", 20, "key"));
        Put("edge-use-memory-false-evolved", "before-baseline-playBefore-are-distinct", F("yuri_cocoon_guard", "memoryBefore", "yuri", 0, "id"), F("yuri_cocoon", "memoryBaseline", "yuri", 0, "id"),
            F("yuri_cocoon", "playBefore", "yuri", 0, "id"), F("run:base-collision:1", "playBefore", "yuri", 0, "key"), F(2, "seqCard"));
        Put("edge-use-memory-false-rekeyed", "base-play-rekeys-around-saved-identity", F("run:base-collision:1", "memoryBefore", "yuri", 0, "key"), F("run:base-collision:2", "playBefore", "yuri", 0, "key"), F(3, "seqCard"));
        Put("edge-use-memory-false-empty", "empty-before-stays-empty-during-base-play", F(L(), "memoryBefore", "yuri"), F(5, "playBefore", "yuri", "length"), F(6, "seqCard"));
        Put("edge-duplicate-party-legacy", "legacy-does-not-invent-party-deduplication", F(162, "hp"), F(12, "deck", "length"), F(12, "seqCard"), F(O("sera", 0), "offered"));
        Put("edge-empty-party-legacy", "legacy-empty-party-source-creation", F(0, "hp"), F(L(), "deck"), F(0, "seqCard"), F("MAP", "phase"));
        Put("edge-all10-party-v2", "v2-has-no-source-upper-party-limit", F(810, "hp"), F(60, "deck", "length"), F(10, "seqCard"));
        Put("map-seed-0", "map-seed0-lazy-left-and-rest-guarantee", F(L(4058753769L, 2789522146L, 1627861374L, 3282270325L), "rng"), F("event", "map", "floors", 1, 0, "type"),
            F(L("z1f2n1", "z1f2n0"), "map", "floors", 1, 2, "next"), F("rest", "map", "floors", 3, 0, "type"), Gone("map", "boss", "next"));
        Put("map-seed-1", "map-seed1-right-and-width-order", F(L(1519060595L, 2189352444L, 1311632316L, 3393262978L), "rng"), F(L("z1f1n0", "z1f1n1"), "map", "floors", 0, 0, "next"), F(3, "map", "floors", 6, "length"));
        Put("map-seed-3", "elite-cap-preserves-first-two", F("elite", "map", "floors", 3, 0, "type"), F("elite", "map", "floors", 4, 1, "type"), F("battle", "map", "floors", 5, 0, "type"));
        Put("map-seed-4", "adjacent-elite-and-unparented-edge-repair", F(L(1105816929L, 3057896731L, 831829349L, 2938419454L), "rng"), F("elite", "map", "floors", 3, 2, "type"),
            F("battle", "map", "floors", 4, 1, "type"), F(L("z1f6n2", "z1f6n1"), "map", "floors", 5, 1, "next"));
        return table;
    }

    // Recomputes tools/gen_run_create_vectors.mjs inputAudit from the repository manifest pair; changed paths stay explicit.
    static Dictionary<string, object?> ManifestAudit(string root)
    {
        string full = Path.GetFullPath(root), aio = Path.Combine(full, "MANIFEST_AIO.json");
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllText(Path.Combine(full, "MANIFEST.sha256"), Utf8).Trim().Split('\n'))
        {
            var match = Regex.Match(line, "^([a-f0-9]{64})  ([^\\n\\r\\u2028\\u2029]+)$");
            Require(match.Success, "Malformed manifest SHA row"); Require(index.TryAdd(match.Groups[2].Value, match.Groups[1].Value), "Duplicate manifest SHA path");
        }
        Require(index.GetValueOrDefault("MANIFEST_AIO.json") == FileHash(aio), "Manifest pair hash differs");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(aio));
        var seen = new HashSet<string>(StringComparer.Ordinal); var changed = new List<object?>(); long entries = 0;
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            string path = Text(file, "path"), target = Path.GetFullPath(Path.Combine(full, path));
            Require(!path.StartsWith('/') && !Path.IsPathRooted(path) && !path.Split('/').Contains("..") && target.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Unsafe manifest path " + path);
            Require(seen.Add(path), "Duplicate manifest input " + path);
            string sha = Text(file, "sha256"); Require(index.GetValueOrDefault(path) == sha, "Manifest SHA pair differs for " + path);
            byte[] bytes = File.ReadAllBytes(target);
            if (bytes.LongLength != file.GetProperty("bytes").GetInt64() || Convert.ToHexStringLower(SHA256.HashData(bytes)) != sha) changed.Add(path);
            entries++;
        }
        Require(index.Count == seen.Count + 1, "Manifest index differs");
        manifestSummary = $"entries {I(entries)}, preExistingChangedPaths {I(changed.Count)} (historical development changes stay explicit, manifests are never regenerated)";
        return O("entries", entries, "manifestMatch", changed.Count == 0, "preExistingChangedPaths", changed, "structuralErrors", L(), "regenerated", false);
    }
    static string manifestSummary = "";
    static void RequireGraph(JsonElement expected, object? actual, string label) => Require(MemoryV2DataLoader.Difference(MemoryV2DataLoader.Graph(expected), actual) == null, label);

    // Independent C# reconstruction of tools/gen_run_create_vectors.mjs casePlan from authored data and its literal boundary recipes.
    static List<PlanCase> CasePlan(JsonElement content)
    {
        var plan = new List<PlanCase>();
        var characters = content.GetProperty("characters").EnumerateArray().ToList(); var charIds = characters.Select(c => Text(c, "id")).ToList();
        var cards = content.GetProperty("cards").EnumerateArray().ToList(); var weapons = content.GetProperty("weapons").EnumerateArray().ToList();
        var startingCommon = content.GetProperty("startingCommon").EnumerateObject().ToDictionary(p => p.Name, p => Text(p.Value), StringComparer.Ordinal);
        var evolvedFrom = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var card in cards) if (card.TryGetProperty("evo", out var evo)) foreach (var target in evo.EnumerateArray()) evolvedFrom[Text(target)] = Text(card, "id");
        List<object?> Baseline(string cid)
        {
            var rows = cards.Where(c => Text(c, "owner") == cid && Truthy(c, "start")).Select(c => (object?)O("id", Text(c, "id"))).ToList();
            var common = O(); if (startingCommon.TryGetValue(cid, out var commonId)) common["id"] = commonId; common["assigned"] = cid; rows.Add(common);
            for (int i = 0; i < rows.Count; i++) Row(rows, i)["key"] = "base:" + cid + ":" + I(i);
            return rows;
        }
        List<object?> Party(Dictionary<string, object?> first, int n = 3)
        {
            int index = charIds.IndexOf(first["charId"] as string ?? ""); var result = L(first);
            for (int i = 0; i < n - 1; i++) result.Add(O("charId", charIds[(index + i + 1) % charIds.Count]));
            return result;
        }
        JsonElement Compatible(JsonElement weapon)
        {
            string? signature = OptionalText(weapon, "signature");
            foreach (var c in characters) if (Text(c, "id") == signature) return c;
            foreach (var c in characters) if (Text(c, "weapon") == Text(weapon, "type")) return c;
            throw new InvalidDataException("Weapon without compatible authored character: " + Text(weapon, "id"));
        }
        void Add(string id, List<object?> actors, string policy, Dictionary<string, object?>? extra, Dictionary<string, object?> metadata, string? code = null)
        {
            var setup = O("party", actors);
            if (policy == "v2") { setup["memoryPolicy"] = "v2"; setup["runId"] = id; }
            if (extra != null) foreach (var pair in extra) setup[pair.Key] = pair.Value;
            var meta = O("parity", "supported", "policy", policy); foreach (var pair in metadata) meta[pair.Key] = pair.Value;
            plan.Add(new PlanCase { Id = id, Setup = setup, Metadata = meta, Code = code });
        }
        void Edge(string id, List<object?> actors, string policy, Dictionary<string, object?>? extra = null, string? code = null, Dictionary<string, object?>? metadata = null)
        {
            var meta = O("group", "edge"); if (metadata != null) foreach (var pair in metadata) meta[pair.Key] = pair.Value;
            Add(id, actors, policy, extra, meta, code);
        }
        List<object?> Sera() => L(O("charId", "sera"));
        List<object?> Extras(int count, string prefix) { var rows = Baseline("sera"); for (int n = 0; n < count; n++) rows.Add(O("id", "sera_round", "key", prefix + I(n))); return rows; }
        object?[] Same(int n) => new object?[] { "level", n, "weaponLevel", n, "armor", n, "accessory", n, "skillLv", n, "ultLv", n };

        foreach (var c in characters) for (int res = 0; res <= 6; res++) foreach (string policy in new[] { "legacy", "v2" }) foreach (string length in new[] { "short", "full" })
        {
            string cid = Text(c, "id");
            Add($"character-{cid}-r{I(res)}-{policy}-{length}", Party(O("charId", cid, "res", res)), policy, O("length", length, "seed", res * 17 + charIds.IndexOf(cid)),
                O("group", "character", "characterId", cid, "resonance", res));
        }
        foreach (var w in weapons) for (int refine = 1; refine <= 5; refine++)
        {
            var actor = Compatible(w); int level = new[] { 1, 20, 59, 60, 61 }[refine - 1]; string weaponId = Text(w, "id");
            Add($"weapon-{weaponId}-r{I(refine)}", L(O("charId", Text(actor, "id"), "weaponId", weaponId, "refine", refine, "level", level, "weaponLevel", level)), "v2", null,
                O("group", "weapon", "weaponId", weaponId, "refine", refine));
        }
        for (int level = 1; level <= 60; level++) Add("level-" + I(level), L(O("charId", "sera", "level", level)), level % 2 != 0 ? "legacy" : "v2", null, O("group", "level", "level", level));
        var profiles = new (string Name, object?[] Fields)[]
        {
            ("reference", new object?[0]), ("reference-null", new object?[] { "level", null, "armor", 15, "accessory", 15, "hp", 999, "power4", 999, "gearBps", 999 }),
            ("minimum", new object?[] { "level", 0, "weaponLevel", 0, "armor", 0, "accessory", 0, "skillLv", 0, "ultLv", 0 }),
            ("negative", new object?[] { "level", -1, "weaponLevel", -1, "armor", -1, "accessory", -1, "skillLv", -1, "ultLv", -1, "asc", -1, "res", -1, "refine", 0 }),
            ("maximum", new object?[] { "level", 60, "weaponLevel", 60, "armor", 15, "accessory", 15, "skillLv", 10, "ultLv", 10, "asc", 4, "res", 6, "refine", 5 }),
            ("over-cap", new object?[] { "level", 61, "weaponLevel", 61, "armor", 16, "accessory", 16, "skillLv", 11, "ultLv", 11, "asc", 5, "res", 7, "refine", 6 }),
            ("i32-max", Same(int.MaxValue)), ("i32-min", Same(int.MinValue)),
            ("supplied-derived-ignored", new object?[] { "hp", 1, "power4", 1, "gearBps", 9000, "skillLv", 4, "ultLv", 7 }),
            ("default-level20", new object?[] { "level", 20 }), ("armor-null", new object?[] { "level", 20, "armor", null, "accessory", null }),
            ("gear-zero", new object?[] { "level", 20, "armor", 0, "accessory", 0 })
        };
        foreach (var c in characters) foreach (var (name, fields) in profiles)
        {
            string cid = Text(c, "id"); var actor = O("charId", cid); foreach (var pair in O(fields)) actor[pair.Key] = pair.Value;
            Add($"stats-{cid}-{name}", L(actor), "v2", null, O("group", "stats", "characterId", cid, "profile", name));
        }
        foreach (string rarity in new[] { "R", "SR", "SSR" })
        {
            var w = weapons.First(x => Text(x, "rarity") == rarity); var actor = Compatible(w);
            for (int weaponLevel = 1; weaponLevel <= 60; weaponLevel++)
                Add($"weapon-level-{rarity}-{I(weaponLevel)}", L(O("charId", Text(actor, "id"), "level", 20, "weaponId", Text(w, "id"), "weaponLevel", weaponLevel)), "legacy", null,
                    O("group", "weapon-level", "rarity", rarity, "weaponLevel", weaponLevel));
        }
        for (int enhance = -1; enhance <= 16; enhance++) foreach (string slot in new[] { "armor", "accessory", "both" })
        {
            var actor = O("charId", "aila", "level", 20);
            if (slot == "both") { actor["armor"] = enhance; actor["accessory"] = enhance; } else actor[slot] = enhance;
            Add($"gear-{slot}-{I(enhance)}", L(actor), "v2", null, O("group", "gear", "slot", slot, "enhance", enhance));
        }
        foreach (var c in characters) for (int skillLv = 1; skillLv <= 10; skillLv++)
        {
            string cid = Text(c, "id");
            Add($"skill-{cid}-{I(skillLv)}", L(O("charId", cid, "skillLv", skillLv, "ultLv", 11 - skillLv, "res", 6)), "legacy", null, O("group", "skill", "characterId", cid, "skillLv", skillLv));
        }
        for (int level = 1; level <= 60; level++) foreach (bool hard in new[] { false, true })
            Add($"stage-{I(level)}-{(hard ? "hard" : "normal")}", Party(O("charId", "sera", "level", level, "weaponId", "w_star_bow", "weaponLevel", level)), "v2",
                O("stageLevel", level, "difficulty", hard ? O("hp", 120, "dmg", 125) : O("hp", 100, "dmg", 100)), O("group", "stage", "stageLevel", level, "hard", hard));
        for (int seed = 0; seed < 300; seed++)
            Add("map-seed-" + I(seed), Party(O("charId", "sera")), seed % 2 != 0 ? "v2" : "legacy", O("seed", seed, "length", seed % 2 != 0 ? "full" : "short"), O("group", "map", "seed", seed));
        foreach (var card in cards)
        {
            string cardId = Text(card, "id"), owner = Text(card, "owner"), cid = charIds.Contains(owner) ? owner : "sera";
            Dictionary<string, object?> Raw()
            {
                var raw = O("id", cardId, "plus", false);
                if (evolvedFrom.TryGetValue(cardId, out var from)) raw["from"] = from;
                if (owner == "common") raw["assigned"] = cid;
                if (owner == "weapon") raw["wielder"] = cid;
                return raw;
            }
            Add("legacy-card-" + cardId, L(O("charId", cid)), "legacy", O("memory", O(cid, L(Raw()))), O("group", "legacy-card", "cardId", cardId));
            if (!charIds.Contains(owner) && owner != "common") continue;
            foreach (bool plus in new[] { false, true })
            {
                var spec = Raw(); spec["key"] = "extra:" + cardId; spec["plus"] = plus; var rows = Baseline(cid); rows.Add(spec);
                Add($"memory-card-{cardId}-{(plus ? "plus" : "base")}", L(O("charId", cid)), "v2", O("memory", O(cid, rows)), O("group", "memory-card", "cardId", cardId, "plus", plus));
            }
        }
        foreach (var part in content.GetProperty("parts").EnumerateArray().Where(p => Text(p, "slot") != "temp")) foreach (string policy in new[] { "legacy", "v2" })
        {
            var specs = Baseline("sera"); Row(specs, 0)["mods"] = O(Text(part, "slot") == "circuit" ? "c" : "e", Text(part, "id"));
            Add($"part-{Text(part, "id")}-{policy}", Sera(), policy, O("memory", O("sera", specs)), O("group", "part", "partId", Text(part, "id")));
        }
        foreach (var relic in content.GetProperty("relics").EnumerateArray())
            Add("relic-" + Text(relic, "id"), Party(O("charId", "sera")), "v2", O("relics", L(Text(relic, "id"))), O("group", "relic", "relicId", Text(relic, "id")));
        for (int asc = 0; asc <= 4; asc++) for (int res = 0; res <= 6; res++) for (int cost = 6; cost <= 9; cost++)
        {
            int capacity = 6 + (asc >= 2 ? 1 : 0) + (asc >= 4 ? 1 : 0) + (res >= 4 ? 1 : 0);
            Add($"capacity-a{I(asc)}-r{I(res)}-cost{I(cost)}", L(O("charId", "sera", "asc", asc, "res", res)), "v2", O("memory", O("sera", Extras(cost, "extra:"))),
                O("group", "capacity", "asc", asc, "resonance", res, "memoryCost", cost, "capacity", capacity), cost > capacity ? "OVER_CAPACITY" : null);
        }
        foreach (long res in new[] { -2147483648L, -7, -6, -5, -3, -2, -1, 7, 2147483647 })
        {
            int capacity = res is -2 or -1 or 7 or 2147483647 ? 7 : 6;
            Add("edge-capacity-signed-res-" + I(res), L(O("charId", "sera", "res", res)), "v2", O("memory", O("sera", Extras(7, "signed:"))),
                O("group", "edge", "memoryCost", 7, "capacity", capacity), capacity < 7 ? "OVER_CAPACITY" : null);
        }
        foreach (string policy in new[] { "legacy", "v2" })
        {
            Edge("edge-defaults-" + policy, Party(O("charId", "sera")), policy);
            Edge("edge-solo-" + policy, Sera(), policy);
            Edge("edge-all10-party-" + policy, charIds.Select(c => (object?)O("charId", c)).ToList(), policy);
            Edge("edge-empty-party-" + policy, L(), policy, null, policy == "v2" ? "INVALID_SNAPSHOT" : null);
            Edge("edge-duplicate-party-" + policy, L(O("charId", "sera"), O("charId", "sera")), policy, null, policy == "v2" ? "INVALID_SNAPSHOT" : null);
            Edge("edge-empty-zones-" + policy, Sera(), policy, O("zones", L()));
            Edge("edge-null-optionals-" + policy, L(O("charId", "sera", "weaponId", null, "level", null, "skillLv", null, "ultLv", null)), policy,
                O("memory", null, "relics", null, "seed", null, "zones", null, "bosses", null, "stageLevel", null, "difficulty", null));
            Edge("edge-explicit-empty-memory-" + policy, Sera(), policy, O("memory", O("sera", L())));
            Edge("edge-character-null-memory-" + policy, Sera(), policy, O("memory", O("sera", null)));
            Edge("edge-zone2-first-" + policy, Party(O("charId", "sera")), policy, O("zones", L(2, 1, 2), "seed", 42));
            Edge("edge-unknown-weapon-fallback-" + policy, L(O("charId", "sera", "level", 20, "weaponId", "missing")), policy);
            Edge("edge-incompatible-authored-weapon-" + policy, L(O("charId", "sera", "level", 20, "weaponId", "w_sion", "weaponLevel", 20)), policy);
            foreach (string zone in new[] { "1", "2" }) foreach (var group in content.GetProperty("encounters").GetProperty(zone).GetProperty("boss").EnumerateArray())
            {
                string boss = Text(group[0]);
                Edge($"edge-boss-{boss}-{policy}", Party(O("charId", "sera")), policy, O("zones", L(long.Parse(zone, CultureInfo.InvariantCulture)), "bosses", O(zone, boss)), null, O("bossId", boss));
            }
        }
        foreach (long seed in new[] { -9007199254740991L, -4294967296, -1, 0, 2147483647, 2147483648, 4294967295, 4294967296, 9007199254740991 }) Edge("edge-seed-" + I(seed), Sera(), "v2", O("seed", seed));
        foreach (long stage in new[] { -2147483648L, -1, 0, 61, 2147483647 }) Edge("edge-stage-boundary-" + I(stage), Sera(), "v2", O("stageLevel", stage));
        foreach (var (name, difficulty) in new (string, object?[])[] { ("empty", new object?[0]), ("partial", new object?[] { "hp", 120 }), ("zeros", new object?[] { "hp", 0, "dmg", 0 }), ("negative", new object?[] { "hp", -50, "dmg", -2 }) })
            foreach (int stage in new[] { 0, 1 }) Edge($"edge-difficulty-{name}-stage{I(stage)}", Sera(), "legacy", O("stageLevel", stage, "difficulty", O(difficulty)));
        Edge("edge-explicit-legacy-policy", Sera(), "legacy", O("memoryPolicy", "legacy"));
        Edge("edge-unknown-policy-is-legacy", Sera(), "legacy", O("memoryPolicy", "future-policy", "runId", "ignored-legacy-run-id", "memoryVersion", 99));
        var colliding = Baseline("sera"); foreach (int n in new[] { 1, 2, 4 }) colliding.Add(O("id", "sera_round", "key", "run:collision:" + I(n)));
        Edge("edge-run-key-collisions", Party(O("charId", "sera")), "v2", O("runId", "collision", "memory", O("sera", colliding)));
        Edge("edge-nonparty-memory-preserved", Sera(), "v2", O("runId", "outside", "memory", O("nia", L(O("key", "run:outside:1", "id", "nia_wrench", "plus", true)))));
        Edge("edge-special-run-id", Sera(), "v2", O("runId", "한글:/😀%\u0000"));
        Edge("edge-memory-version-max-safe", Sera(), "v2", O("memoryVersion", CanonicalState.MaxSafeInteger));
        foreach (string policy in new[] { "legacy", "v2" })
        {
            var specs = Baseline("sera"); var first = Row(specs, 0);
            first["plus"] = false; first["mods"] = O("c", null, "e", null); first["growth"] = 0; first["gained"] = false; first["evolved"] = false;
            first["opaque"] = O("zero", 0, "false", false, "nil", null, "list", L(0, false, null));
            Edge("edge-sparse-card-metadata-" + policy, Sera(), policy, O("memory", O("sera", specs)));
        }
        foreach (var root in cards.Where(c => Truthy(c, "growth"))) foreach (var evolvedJson in root.GetProperty("evo").EnumerateArray()) foreach (bool useMemory in new[] { false, true })
        {
            string evolved = Text(evolvedJson), rootId = Text(root, "id"); var specs = Baseline("yuri");
            int existing = specs.FindIndex(sp => (sp as Dictionary<string, object?>)?.GetValueOrDefault("id") as string == rootId);
            var spec = O("id", evolved, "from", rootId, "key", existing >= 0 ? Row(specs, existing)["key"] : "saved:" + evolved, "plus", true, "mods", O("e", "e_guard"));
            if (existing >= 0) specs[existing] = spec; else specs.Add(spec);
            Edge($"edge-evolved-{evolved}-{(useMemory ? "memory" : "base")}", L(O("charId", "yuri", "asc", 2)), "v2", O("memory", O("yuri", specs), "useMemory", useMemory), null,
                O("evolvedId", evolved, "useMemory", useMemory, "inputDomain", "Authored ascension2 gives capacity7 needed by S-root evolution+upgrade+permanent part"));
        }
        foreach (string mode in new[] { "removed", "evolved", "rekeyed", "empty" })
        {
            var specs = mode == "empty" ? L() : Baseline("yuri");
            if (mode == "removed") specs.RemoveAt(0);
            if (mode == "evolved") { Row(specs, 0)["id"] = "yuri_cocoon_guard"; Row(specs, 0)["from"] = "yuri_cocoon"; }
            if (mode == "rekeyed") Row(specs, 0)["key"] = "run:base-collision:1";
            Edge("edge-use-memory-false-" + mode, L(O("charId", "yuri")), "v2", O("runId", "base-collision", "memory", O("yuri", specs), "useMemory", false));
        }
        var mismatched = Baseline("sera"); Row(mismatched, -1)["assigned"] = "nia";
        Edge("edge-legacy-common-assigned-is-preserved", Sera(), "legacy", O("memory", O("sera", mismatched)));
        void Bad(string id, Dictionary<string, object?> extra, string code, List<object?>? actors = null) => Add(id, actors ?? Sera(), "v2", extra, O("group", "error"), code);
        foreach (var (name, runId) in new (string, object?)[] { ("empty", ""), ("null", null), ("number", 0) }) Bad("error-run-id-" + name, O("runId", runId), "RUN_ID_REQUIRED");
        foreach (var (name, version) in new (string, object?)[] { ("negative", -1), ("fraction", 1.5), ("bool", true), ("string", "1") }) Bad("error-memory-version-" + name, O("memoryVersion", version), "INVALID_SNAPSHOT");
        foreach (int zone in new[] { 0, -1, 3 }) Bad("error-zone-" + I(zone), O("zones", L(zone)), "ZONE_NOT_AUTHORED");
        Bad("error-zone-later-unauthored", O("zones", L(1, 3)), "ZONE_NOT_AUTHORED");
        Bad("error-v2-unknown-character", O(), "INVALID_SNAPSHOT", L(O("charId", "missing")));
        void Corrupt(string id, Action<List<object?>> mutate, string code) { var rows = Baseline("sera"); mutate(rows); Bad(id, O("memory", O("sera", rows)), code); }
        Corrupt("error-memory-empty-key", r => Row(r, 0)["key"] = "", "IDENTITY_REQUIRED");
        Corrupt("error-memory-duplicate-key", r => Row(r, 1)["key"] = Row(r, 0)["key"], "IDENTITY_REQUIRED");
        Corrupt("error-memory-unknown-card", r => Row(r, 0)["id"] = "missing", "INVALID_CARD");
        Corrupt("error-memory-foreign-card", r => Row(r, 0)["id"] = "nia_wrench", "INVALID_CARD");
        Corrupt("error-memory-weapon", r => Row(r, 0)["id"] = "wc_sera", "INVALID_CARD");
        Corrupt("error-memory-token-flag", r => Row(r, 0)["token"] = true, "INVALID_CARD");
        Corrupt("error-memory-weapon-flag", r => Row(r, 0)["weapon"] = true, "INVALID_CARD");
        Corrupt("error-memory-common-assignment", r => Row(r, -1)["assigned"] = "nia", "INVALID_ASSIGNMENT");
        Corrupt("error-memory-plus-null", r => Row(r, 0)["plus"] = null, "INVALID_UPGRADE");
        Corrupt("error-memory-part-temp", r => Row(r, 0)["mods"] = O("c", "amp"), "INVALID_PART");
        Corrupt("error-memory-part-wrong-slot", r => Row(r, 0)["mods"] = O("e", "c_echo"), "INVALID_PART");
        Corrupt("error-memory-part-unknown", r => Row(r, 0)["mods"] = O("c", "missing"), "INVALID_PART");
        Corrupt("error-memory-part-unknown-field", r => Row(r, 0)["mods"] = O("temp", "amp"), "INVALID_PART");
        Corrupt("error-memory-part-array", r => Row(r, 0)["mods"] = L(), "INVALID_PART");
        var evoBad = new[] { O("key", "evo", "id", "yuri_cocoon_guard"), O("key", "evo", "id", "yuri_cocoon_guard", "from", "yuri_sprout") };
        for (int n = 0; n < evoBad.Length; n++) Bad("error-memory-evolution-" + I(n), O("memory", O("yuri", L(evoBad[n]))), "INVALID_EVOLUTION", L(O("charId", "yuri")));
        var nia = Baseline("nia"); Row(nia, 0)["key"] = "base:sera:0";
        Bad("error-memory-cross-character-key", O("memory", O("sera", Baseline("sera"), "nia", nia)), "IDENTITY_REQUIRED", L(O("charId", "sera"), O("charId", "nia")));
        foreach (var (name, setup, error) in new (string, Dictionary<string, object?>, string)[]
        {
            ("missing-party", O(), "SyntaxError"), ("null-party", O("party", null), "TypeError"), ("unknown-character", O("party", L(O("charId", "missing"))), "TypeError"),
            ("unknown-card", O("party", Sera(), "memory", O("sera", L(O("id", "missing")))), "TypeError"), ("string-card", O("party", Sera(), "memory", O("sera", L("sera_round"))), "TypeError")
        }) plan.Add(new PlanCase { Id = "native-" + name, Setup = setup, Metadata = O("group", "native-error", "parity", "js-source-only", "policy", "legacy"), Name = error });
        foreach (var (name, actor, extra, domain) in new (string, Dictionary<string, object?>, Dictionary<string, object?>, string)[]
        {
            ("unsafe-seed-positive", O("charId", "sera"), O("seed", CanonicalState.MaxSafeInteger + 1), "json-only"),
            ("unsafe-seed-negative", O("charId", "sera"), O("seed", -CanonicalState.MaxSafeInteger - 1), "json-only"),
            ("level-outside-i32", O("charId", "sera", "level", 2147483648L), O(), "integer"), ("stage-outside-i32", O("charId", "sera"), O("stageLevel", 2147483648L), "integer"),
            ("difficulty-derived-overflow", O("charId", "sera"), O("stageLevel", 60, "difficulty", O("hp", int.MaxValue, "dmg", int.MaxValue)), "integer")
        }) Add("domain-" + name, L(actor), "legacy", extra, O("group", "domain", "parity", "typed-domain-rejection", "typedExpectedCode", TypedCode, "hashDomain", domain));
        var opaque = Baseline("sera"); Row(opaque, 0)["opaque"] = O("fraction", 1.25, "negativeHalf", -0.5);
        Add("edge-opaque-fractional-metadata", Sera(), "v2", O("memory", O("sera", opaque), "useMemory", false), O("group", "edge", "parity", "supported-json-metadata", "hashDomain", "json-only"));
        return plan;
    }
    static Dictionary<string, object?> Row(List<object?> rows, int index) => rows[index < 0 ? rows.Count + index : index] as Dictionary<string, object?> ?? throw new InvalidDataException("Plan spec is not an object");
    static bool Truthy(JsonElement json, string key) => json.TryGetProperty(key, out var value) && (value.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.Object or JsonValueKind.Array => true,
        JsonValueKind.Number => value.GetDouble() != 0,
        JsonValueKind.String => (value.GetString() ?? "").Length != 0,
        _ => false
    });
    static Dictionary<string, object?> O(params object?[] pairs)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (int i = 0; i < pairs.Length; i += 2) result[pairs[i] as string ?? throw new InvalidDataException("Plan key")] = pairs[i + 1];
        return result;
    }
    static List<object?> L(params object?[] items) => new List<object?>(items);
    static string I(long value) => value.ToString(CultureInfo.InvariantCulture);

    static void Supported(JsonElement row, RunData data, string definitions, string id)
    {
        var input = row.GetProperty("setup"); var setup = RunDataLoader.Setup(input); string before = SetupPrint(setup);
        Count(ref factories);
        if (Text(row, "outcome") == "rejected")
        {
            var error = row.GetProperty("error"); string code = Text(error, "code"), actual = Outcome(() => ExpeditionRun.Create(data, setup));
            if (actual == code) Count(ref rejected);
            Check(actual == code, id + " exact " + Text(error, "kind") + " " + code + (actual == code ? "" : " (C# " + actual + ")"));
        }
        else
        {
            var run = ExpeditionRun.Create(data, setup); Count(ref created);
            var point = row.GetProperty("created"); var expected = ExpectedPoint(point, row, id + " oracle created");
            var graph = Compare(expected, run, id + " created");
            string full = Full(graph);
            Queries(row.GetProperty("queries"), expected, run, full, id);
            Check(row.GetProperty("afterQueries").GetRawText() == point.GetRawText(), id + " oracle afterQueries is the unchanged created checkpoint");
            Check(Full(run.State.ToJsonGraph()) == full, id + " C# state after every pure query equals created state");
            Anchors(row, graph, expected, id);
            Count(ref recreations);
            Check(Full(ExpeditionRun.Create(data, setup).State.ToJsonGraph()) == full, id + " same typed setup recreates identical state/map/RNG/keys");
            Check(Full(ExpeditionRun.Create(data, RunDataLoader.Setup(input)).State.ToJsonGraph()) == full, id + " freshly loaded setup recreates identical state");
        }
        Check(SetupPrint(setup) == before, id + " Create preserves the typed setup");
        Check(DataPrint(data) == definitions, id + " Create preserves source definitions");
    }

    static void TypedDomain(JsonElement row, RunData data, string definitions, string id)
    {
        Require(Text(row, "typedExpectedCode") == TypedCode && Text(row, "outcome") == "created" && OptionalText(row, "hashDomain") is "integer" or "json-only", id + " typed-domain row metadata");
        ExpectedPoint(row.GetProperty("created"), row, id + " JS typed-domain evidence");
        Check(row.GetProperty("afterQueries").GetRawText() == row.GetProperty("created").GetRawText(), id + " JS evidence queries left state unchanged");
        if (!probing) jsOnlyQueries += row.GetProperty("queries").GetArrayLength();
        string layer = "host adapter", outcome;
        try
        {
            var setup = RunDataLoader.Setup(row.GetProperty("setup")); string before = SetupPrint(setup); layer = "Core Run.Create";
            outcome = Outcome(() => ExpeditionRun.Create(data, setup));
            Check(SetupPrint(setup) == before, id + " typed rejection preserves the setup");
        }
        catch (RuleException error) { outcome = error.Code; }
        bool ok = outcome == TypedCode; if (ok) Count(ref typedRejections);
        Check(ok, id + " typed boundary rejects with " + TypedCode + " at " + layer + (ok ? "" : " (C# " + outcome + ")"));
        Check(DataPrint(data) == definitions, id + " typed rejection preserves source definitions");
    }

    static string SourceOnly(JsonElement row, RunData data, string definitions, string id)
    {
        var error = row.GetProperty("error"); string name = Text(error, "name"), message = Text(error, "message");
        Require(Text(row, "outcome") == "rejected" && Text(error, "kind") == "NativeError" && name is "SyntaxError" or "TypeError" && !error.TryGetProperty("code", out _) && message.Length != 0,
            id + " JS-native diagnostic lost its source-only label");
        Count(ref sourceOnly);
        RunSetup setup;
        try { setup = RunDataLoader.Setup(row.GetProperty("setup")); }
        catch (Exception adapter) when (adapter is KeyNotFoundException || adapter is InvalidOperationException || adapter is InvalidDataException || adapter is RuleException)
        {
            Count(ref adapterRejections); Check(DataPrint(data) == definitions, id + " adapter rejection preserves source definitions");
            return $"{id}: JS {name} \"{message}\" | C# host adapter {adapter.GetType().Name}";
        }
        string before = SetupPrint(setup), observed;
        try { ExpeditionRun.Create(data, setup); observed = "created"; }
        catch (MemoryBoundaryException boundary)
        {
            observed = "Core " + boundary.JavaScriptName;
            Check(boundary.JavaScriptName == name, id + " Core boundary keeps documented JS class " + name + " (diagnostic, not parity)");
        }
        catch (RuleException rule) { observed = "Core RuleException " + rule.Code; }
        Check(observed != "created", id + " JS-native " + name + " input is never silently created in C#");
        Check(SetupPrint(setup) == before && DataPrint(data) == definitions, id + " source-only rejection preserves setup/definitions");
        return $"{id}: JS {name} \"{message}\" | C# {observed}";
    }

    static Point ExpectedPoint(JsonElement point, JsonElement row, string label)
    {
        var stateHash = point.GetProperty("stateHash");
        var fields = new List<string> { "state", "stateHash", "jsonHash", "rngHash", "mapHash", "logHash", "fieldShape", "shapeHash" };
        if (stateHash.ValueKind == JsonValueKind.Null) fields.Add("canonicalError");
        RequireKeys(point, fields, label + " checkpoint fields");
        var stateJson = point.GetProperty("state");
        var state = MemoryV2DataLoader.Graph(stateJson) as Dictionary<string, object?> ?? throw new InvalidDataException(label + " state object");
        var shape = MemoryV2DataLoader.Graph(point.GetProperty("fieldShape")) as Dictionary<string, object?> ?? throw new InvalidDataException(label + " field shape");
        var undefined = shape.GetValueOrDefault("undefined") as List<object?> ?? throw new InvalidDataException(label + " own-undefined paths");
        var result = new Point { State = state, Undefined = undefined, StateHash = stateHash.ValueKind == JsonValueKind.Null ? null : HashText(stateHash),
            RngHash = HashText(point.GetProperty("rngHash")), MapHash = HashText(point.GetProperty("mapHash")), LogHash = HashText(point.GetProperty("logHash")) };
        string? canonical = TryHash(state);
        if (result.StateHash == null)
            Check(canonical == null && OptionalText(row, "hashDomain") == "json-only" && Text(point, "canonicalError") == "NON_CANONICAL_NUMBER", label + " canonical hash withheld only for declared json-only numbers");
        else Check(canonical == result.StateHash, label + " oracle canonical state hash");
        Check(HashText(point.GetProperty("jsonHash")) == Sha(stateJson.GetRawText()), label + " ordered JSON hash matches exact oracle text");
        Check(result.RngHash == TryHash(state.GetValueOrDefault("rng")) && result.MapHash == TryHash(state.GetValueOrDefault("map")) && result.LogHash == TryHash(state.GetValueOrDefault("log")), label + " oracle rng/map/log hashes");
        Check(HashText(point.GetProperty("shapeHash")) == TryHash(shape), label + " oracle field-shape hash");
        Require(shape.Count == 4 && new[] { "objects", "arrays", "nulls", "undefined" }.All(shape.ContainsKey), label + " field-shape sections");
        var projected = Projected(shape);
        string? ordered = MemoryV2DataLoader.Difference(projected, ShapeOf(stateJson));
        Check(ordered == null, label + " ordered field-shape projection (own-undefined keys removed) equals exact oracle JSON" + (ordered == null ? "" : ": " + ordered));
        foreach (object? path in undefined) Check(Absent(state, path), label + " oracle own-undefined " + CanonicalState.Serialize(path) + " is omitted from JSON, never null");
        result.Shape = Normalized(projected);
        foreach (var property in point.EnumerateObject())
            if (property.Name != "state" && property.Name != "fieldShape") result.Diagnostics.Add(property.Name, MemoryV2DataLoader.Graph(property.Value));
        return result;
    }

    static Dictionary<string, object?> Compare(Point expected, ExpeditionRun run, string label)
    {
        var state = run.State; var graph = state.ToJsonGraph();
        string? diff = MemoryV2DataLoader.Difference(expected.State, graph);
        Check(diff == null, label + " complete state/field presence" + (diff == null ? "" : ": " + diff));
        Check(TryHash(graph) == expected.StateHash, label + (expected.StateHash == null ? " json-only state stays outside the integer canonical hash" : " canonical state hash"));
        Check(CanonicalState.Hash(state.Rng) == expected.RngHash, label + " private RNG hash");
        Check(CanonicalState.Hash(state.Map?.ToJsonGraph()) == expected.MapHash, label + " map hash");
        Check(CanonicalState.Hash(state.Log) == expected.LogHash, label + " log hash");
        foreach (object? path in expected.Undefined) Check(Absent(graph, path), label + " JS own-undefined " + CanonicalState.Serialize(path) + " stays absent, never null");
        // C# dictionaries do not promise JS insertion order, so the serialized C# graph is compared path-by-path: same objects/keys, array lengths and nulls.
        using (var serialized = JsonDocument.Parse(Full(graph)))
        {
            string? shape = MemoryV2DataLoader.Difference(expected.Shape, Normalized(ShapeOf(serialized.RootElement)));
            Check(shape == null, label + " serialized C# field presence/null/array shape equals source projection" + (shape == null ? "" : ": " + shape));
        }
        return graph;
    }

    // JS shape(): objects {path, keys}, arrays {path, length} and null paths in exact JSON document order.
    static Dictionary<string, object?> ShapeOf(JsonElement root)
    {
        var objects = new List<object?>(); var arrays = new List<object?>(); var nulls = new List<object?>();
        void Visit(JsonElement node, List<object?> path)
        {
            if (node.ValueKind == JsonValueKind.Null) nulls.Add(new List<object?>(path));
            else if (node.ValueKind == JsonValueKind.Array)
            {
                arrays.Add(O("path", new List<object?>(path), "length", (long)node.GetArrayLength())); long index = 0;
                foreach (var item in node.EnumerateArray()) { path.Add(index++); Visit(item, path); path.RemoveAt(path.Count - 1); }
            }
            else if (node.ValueKind == JsonValueKind.Object)
            {
                var properties = node.EnumerateObject().ToList();
                objects.Add(O("path", new List<object?>(path), "keys", properties.Select(p => (object?)BattleDataLoader.Name(p)).ToList()));
                foreach (var property in properties) { path.Add(BattleDataLoader.Name(property)); Visit(property.Value, path); path.RemoveAt(path.Count - 1); }
            }
        }
        Visit(root, new List<object?>());
        return O("objects", objects, "arrays", arrays, "nulls", nulls, "undefined", L());
    }
    // Generator validatePoint projection: keys of own-undefined paths are dropped and the undefined list becomes empty.
    static Dictionary<string, object?> Projected(Dictionary<string, object?> shape)
    {
        var undefined = new HashSet<string>(Items(shape, "undefined").Select(p => CanonicalState.Serialize(p)), StringComparer.Ordinal);
        var objects = new List<object?>();
        foreach (object? entry in Items(shape, "objects"))
        {
            var fields = Fields(entry); var path = fields.GetValueOrDefault("path") as List<object?> ?? throw new InvalidDataException("Field shape path");
            objects.Add(O("path", path, "keys", Items(fields, "keys").Where(k => !undefined.Contains(CanonicalState.Serialize(new List<object?>(path) { k }))).ToList()));
        }
        return O("objects", objects, "arrays", Items(shape, "arrays"), "nulls", Items(shape, "nulls"), "undefined", L());
    }
    static Dictionary<string, object?> Normalized(Dictionary<string, object?> shape)
    {
        var objects = new Dictionary<string, object?>(StringComparer.Ordinal); var arrays = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (object? entry in Items(shape, "objects"))
        {
            var fields = Fields(entry);
            objects.Add(CanonicalState.Serialize(fields.GetValueOrDefault("path")), Items(fields, "keys").Select(k => k as string ?? throw new InvalidDataException("Field shape key")).OrderBy(k => k, StringComparer.Ordinal).Select(k => (object?)k).ToList());
        }
        foreach (object? entry in Items(shape, "arrays")) { var fields = Fields(entry); arrays.Add(CanonicalState.Serialize(fields.GetValueOrDefault("path")), fields.GetValueOrDefault("length")); }
        var nulls = Items(shape, "nulls").Select(p => CanonicalState.Serialize(p)).OrderBy(p => p, StringComparer.Ordinal).Select(p => (object?)p).ToList();
        return O("objects", objects, "arrays", arrays, "nulls", nulls);
    }
    static List<object?> Items(Dictionary<string, object?> fields, string key) => fields.GetValueOrDefault(key) as List<object?> ?? throw new InvalidDataException("Field shape list " + key);
    static Dictionary<string, object?> Fields(object? value) => value as Dictionary<string, object?> ?? throw new InvalidDataException("Field shape entry");

    static void Queries(JsonElement oracle, Point expected, ExpeditionRun run, string full, string id)
    {
        var map = expected.State.GetValueOrDefault("map") as Dictionary<string, object?> ?? throw new InvalidDataException(id + " oracle map");
        var calls = new List<(string Method, string? Arg)> { ("available", null) };
        foreach (object? floor in map.GetValueOrDefault("floors") as List<object?> ?? throw new InvalidDataException(id + " oracle floors"))
            foreach (object? node in floor as List<object?> ?? throw new InvalidDataException(id + " oracle floor")) calls.Add(("nodeById", NodeId(node)));
        calls.Add(("nodeById", NodeId(map.GetValueOrDefault("boss")))); calls.Add(("nodeById", "missing")); calls.Add(("nodeById", "")); calls.Add(("available", null));
        var rows = oracle.EnumerateArray().ToList();
        Require(rows.Count == calls.Count, id + " oracle query list omits/adds pure queries");
        for (int i = 0; i < rows.Count; i++)
        {
            var q = rows[i]; var (method, arg) = calls[i]; string label = id + " query " + i + " " + method + "(" + (arg ?? "") + ")";
            RequireKeys(q, QueryFields, label + " fields");
            var args = q.GetProperty("args");
            Require(Text(q, "method") == method && args.GetArrayLength() == (arg == null ? 0 : 1) && (arg == null || Text(args[0]) == arg), label + " order/arguments");
            var result = q.GetProperty("result"); RequireKeys(result, ResultFields, label + " result");
            Require(Text(result, "kind") == "json", label + " JS undefined result is not representable as C# null");
            Check(HashText(q.GetProperty("resultHash")) == TryHash(MemoryV2DataLoader.Graph(result)), label + " oracle result hash");
            Check(MemoryV2DataLoader.Difference(expected.Diagnostics, MemoryV2DataLoader.Graph(q.GetProperty("before"))) == null &&
                MemoryV2DataLoader.Difference(expected.Diagnostics, MemoryV2DataLoader.Graph(q.GetProperty("after"))) == null, label + " oracle before/after diagnostics equal created");
            List<string>? list = null; RunNode? node = null; object? actual;
            if (arg == null) actual = list = run.Available();
            else { node = run.NodeById(arg); actual = node?.ToJsonGraph(); }
            Count(ref queries);
            string? diff = MemoryV2DataLoader.Difference(MemoryV2DataLoader.Graph(result.GetProperty("value")), actual);
            Check(diff == null, label + " exact result" + (diff == null ? "" : ": " + diff));
            list?.Add("caller-mutation");
            if (node != null) { node.Id = "caller-mutation"; node.Type = "caller-mutation"; node.Next?.Add("caller-mutation"); }
            Check(Full(run.State.ToJsonGraph()) == full, label + " preserves complete private state/RNG and stays detached after caller mutation");
        }
    }

    static void Anchors(JsonElement row, Dictionary<string, object?> graph, Point expected, string id)
    {
        if (!row.TryGetProperty("anchors", out var anchors)) return;
        foreach (var anchor in anchors.EnumerateArray())
        {
            string tag = Text(anchor, "tag");
            foreach (var fact in anchor.GetProperty("facts").EnumerateArray())
            {
                var path = MemoryV2DataLoader.Graph(fact.GetProperty("path")) as List<object?> ?? throw new InvalidDataException(id + " anchor path");
                string label = id + " anchor " + tag + " " + CanonicalState.Serialize(path);
                Require(path.Count > 1 && path[0] is "state" or "fieldShape", label + " root");
                bool absent = fact.TryGetProperty("absent", out var flag) && flag.ValueKind == JsonValueKind.True;
                if (path[0] is "fieldShape")
                {
                    Require(!absent && path.Count == 2 && path[1] is "undefined", label + " unsupported shape anchor");
                    var paths = MemoryV2DataLoader.Graph(fact.GetProperty("value")) as List<object?> ?? throw new InvalidDataException(label + " paths");
                    Check(MemoryV2DataLoader.Difference(paths, expected.Undefined) == null && paths.All(p => Absent(graph, p)), label + " C# omits every JS own-undefined field");
                    continue;
                }
                bool found = Find(graph, path.Skip(1), out object? actual);
                if (absent) Check(!found, label + " absent in C#");
                else
                {
                    string? diff = found ? MemoryV2DataLoader.Difference(MemoryV2DataLoader.Graph(fact.GetProperty("value")), actual) : "missing";
                    Check(diff == null, label + " C# value" + (diff == null ? "" : ": " + diff));
                }
            }
        }
    }

    static bool Find(object? node, IEnumerable<object?> path, out object? value)
    {
        value = node;
        foreach (object? step in path)
        {
            if (value is IDictionary<string, object?> fields && step is string key && fields.TryGetValue(key, out var next)) value = next;
            else if (value is IList list && step is "length") value = (long)list.Count;
            else if (value is IList items && step is long index && index >= 0 && index < items.Count) value = items[(int)index];
            else { value = null; return false; }
        }
        return true;
    }
    static bool Absent(object? graph, object? path)
    {
        if (path is not List<object?> steps || steps.Count == 0 || steps[^1] is not string key) return false;
        return Find(graph, steps.Take(steps.Count - 1), out object? parent) && parent is IDictionary<string, object?> fields && !fields.ContainsKey(key);
    }
    static string NodeId(object? node) => node is Dictionary<string, object?> fields && fields.GetValueOrDefault("id") is string id ? id : throw new InvalidDataException("Oracle map node without string id");

    static string Outcome(Action create)
    {
        try { create(); return "created"; }
        catch (RuleException error) { return error.Code; }
    }
    static string? TryHash(object? graph)
    {
        try { return CanonicalState.Hash(graph); }
        catch (ArgumentException) { return null; }
    }
    internal static string SetupPrint(RunSetup setup) => Full(new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["party"] = setup.Party.Select(p => (object?)p.ToJsonGraph()).ToList(), ["memory"] = setup.Memory, ["bosses"] = setup.Bosses, ["runId"] = setup.RunId,
        ["memoryVersion"] = setup.MemoryVersion, ["relics"] = setup.Relics, ["seed"] = setup.Seed, ["length"] = setup.Length, ["memoryPolicy"] = setup.MemoryPolicy,
        ["zones"] = setup.Zones, ["stageLevel"] = setup.StageLevel, ["difficulty"] = setup.Difficulty?.ToJsonGraph(), ["useMemory"] = setup.UseMemory
    });
    // Enemies have no public graph and are unused by R0 creation; every definition Run.Create reads is included.
    internal static string DataPrint(RunData data) => Full(new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["memory"] = MemoryV2DataLoader.Catalog(data.Memory), ["weapons"] = data.Memory.Battle.Weapons.Select(w => (object?)w.ToJsonGraph()).ToList(),
        ["relics"] = data.Memory.Battle.Relics.Select(r => (object?)r.ToJsonGraph()).ToList(), ["rules"] = JsonSerializer.Serialize(data.Memory.Battle.Rules, PublicFields),
        ["stats"] = JsonSerializer.Serialize(data.Stats), ["characters"] = data.Characters.Select(c => (object?)c.ToJsonGraph()).ToList(),
        ["encounters"] = data.Encounters.Select(e => (object?)new object?[] { e.Zone, e.ToJsonGraph() }).ToList(), ["events"] = data.Events.Select(e => (object?)e.ToJsonGraph()).ToList()
    });

    // Standard-library gzip/UTF-8/JSON only. Fraction metadata stays in the M0 graph path, never the integer-only trace parser.
    // Exporter contract: one RFC 1952 member, fixed CM8/FLG0/MTIME0/XFL2 header (only the OS byte may vary), exactly one raw DEFLATE
    // stream ending at the 8-byte CRC32/ISIZE footer. GZipStream alone concatenates members, so gzip("")+gzip(json) matched its last ISIZE.
    static readonly byte[] GzipHeader = { 0x1f, 0x8b, 8, 0, 0, 0, 0, 0, 2 };
    internal static JsonDocument Decode(byte[] compressed) => JsonDocument.Parse(Gunzip(compressed, GzipHeader), new JsonDocumentOptions { MaxDepth = 256 });
    // Shared strict byte reader (gzipSync fixtures here and in RunProgressionChecks, the legacy JSONL corpus in TraceHashChecks): the header starts
    // with the caller's exporter prefix (always FLG0, so the DEFLATE stream starts at byte 10), one DEFLATE stream ends exactly at the footer,
    // native CRC32 plus explicit ISIZE, no BOM and fatal UTF-8. Callers parse the returned bytes with their own JSON rules.
    internal static ReadOnlyMemory<byte> Gunzip(byte[] compressed, ReadOnlySpan<byte> header)
    {
        Require(header.Length >= 4 && header[3] == 0 && compressed.Length >= 20 && compressed.AsSpan(0, header.Length).SequenceEqual(header),
            "Malformed gzip header: exporter prefix " + Convert.ToHexString(header) + " required (FLG0; only unpinned bytes such as OS may vary)");
        int footer = compressed.Length - 8; var boundary = new DeflateBoundary(compressed, 10, footer); var sink = new byte[1 << 16];
        using (var deflate = new DeflateStream(boundary, CompressionMode.Decompress)) while (deflate.Read(sink, 0, sink.Length) != 0) { }
        Require(boundary.Next == footer, "Gzip must hold exactly one DEFLATE stream followed by exactly the 8-byte CRC32/ISIZE footer (truncated, trailing or concatenated member)");
        // Exactly one member is proven above, so native GZipStream now validates that member's CRC32/ISIZE only.
        var output = new MemoryStream();
        using (var input = new MemoryStream(compressed, false))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress)) gzip.CopyTo(output);
        Require(unchecked((uint)output.Length) == BinaryPrimitives.ReadUInt32LittleEndian(compressed.AsSpan(compressed.Length - 4)), "Truncated or corrupt gzip footer");
        var json = new ReadOnlyMemory<byte>(output.GetBuffer(), 0, checked((int)output.Length));
        Require(!(json.Length >= 3 && json.Span[0] == 0xef && json.Span[1] == 0xbb && json.Span[2] == 0xbf), "JSON BOM is outside the exporter contract");
        Utf8.GetCharCount(json.Span);
        return json;
    }
    // Non-seekable source for native DeflateStream: large reads up to the last claimed DEFLATE byte, then one byte per read for that byte
    // and everything after it. DeflateStream reads only while its inflater needs input, so Next equals the footer offset only when the
    // stream ends exactly there: an earlier end leaves Next short, a truncated stream pulls footer bytes. Read the DeflateStream with Read
    // only; its CopyTo drains the source to EOF and would hide the boundary.
    sealed class DeflateBoundary : Stream
    {
        readonly byte[] bytes; readonly int last;
        public int Next { get; private set; }
        public DeflateBoundary(byte[] bytes, int start, int footer) { this.bytes = bytes; Next = start; last = footer - 1; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int n = Math.Min(buffer.Length, Math.Min(Next < last ? last - Next : 1, bytes.Length - Next));
            bytes.AsSpan(Next, n).CopyTo(buffer); Next += n; return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    internal static void UniqueKeys(JsonElement root)
    {
        var pending = new Stack<JsonElement>(); pending.Push(root);
        while (pending.Count != 0)
        {
            var node = pending.Pop();
            if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) pending.Push(item);
            else if (node.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in node.EnumerateObject())
                {
                    string name = BattleDataLoader.Name(property);
                    Require(names.Add(name), "Duplicate decoded JSON key " + name); pending.Push(property.Value);
                }
            }
        }
    }
    static void RequireKeys(JsonElement json, IReadOnlyCollection<string> names, string label)
    {
        var actual = json.EnumerateObject().Select(p => p.Name).ToList();
        Require(actual.Count == names.Count && actual.Distinct(StringComparer.Ordinal).Count() == actual.Count && names.All(actual.Contains), label);
    }
    static string Full(object? graph) => JsonSerializer.Serialize(graph);
    static string Text(JsonElement json) => json.GetString() ?? throw new InvalidDataException("Expected JSON string");
    static string Text(JsonElement json, string key) => json.GetProperty(key).GetString() ?? throw new InvalidDataException("Expected JSON string " + key);
    static string? OptionalText(JsonElement json, string key) => json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    static string Sha(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    static string FileHash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    static string HashText(JsonElement json)
    {
        string hash = Text(json);
        Require(hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Malformed run-create SHA-256");
        return hash;
    }
}
