// R1c §8.9-8.14/§16.2/§17.5/§20.9 P1: actual C# public Run memory boundary (MemoryChanges/PreviewMemory/CommitMemory) against the
// gen_run_memory_vectors JS oracle. Each scenario opens a fresh Run.Create from its recorded public setup and, at a terminal save, replays the
// base's own recorded public prefix; v2 starts from the actual M0 empty-account migration and afterwards only from actual returned stores.
// Recorded points, returns and stores are comparison data only: nothing recorded is restored into a Run or used as the live store.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MemoryForge.Core;
using ExpeditionRun = MemoryForge.Core.Run;
using MemoryOpPlan = (string Label, string Method, string Expect, string Store, string When);

static partial class RunProgressionChecks
{
    const string MemorySchema = "mf-run-memory-v1", MemoryStatus = "JS_ORACLE_ONLY";
    const string MemoryScope = "Actual public Run memoryChanges/previewMemory/commitMemory on fresh Creates and on four real gen_traces terminal saves replayed from a fresh Run.create: complete JS Run/store points, arguments, returns and errors. C# Run memory replay 0; account storage, disk, server, Unity and device acceptance PENDING";
    const string NoOverCapacity = "No party character on the selected clear base has a full change set above its capacity; nothing is injected to force OVER_CAPACITY";
    const string NoSecondChange = "The defeat base has fewer than two memory changes; nothing is injected to force DEFEAT_KEEP_ONE";
    // Measured current-source denominators (RULES_NOW_PLAY regeneration, defeat base 3561 full with 8 changes): 88 planned ops = 88 executed
    // (27 returned, 61 rejected of which 3 native boundaries) + 0 NOT_COVERED; 15 scenarios open 2 fresh Creates and 13 terminal saves.
    const int MemoryPlanned = 88, MemoryCovered = 88, MemoryReturned = 27, MemoryRejected = 61, MemoryNativeOps = 3, MemoryUncovered = 0,
        MemoryScenarioCount = 15, MemoryFreshScenarios = 2, MemorySaveScenarios = 13;
    // Exact allowlist in source order (the generator's seven slots), compared before any file is hashed.
    static readonly (string Key, string Path)[] MemorySources = { ("data", "data/game_data.json"), ("engine", "engine/rules-core.js"), ("memory", "proposals/memory_v2.cjs"),
        ("canonical", "tools/gen_traces.mjs"), ("fuzzPolicy", "engine/tests/fuzz_runs.mjs"), ("runCreateDecoder", "tools/gen_run_create_vectors.mjs"), ("generator", "tools/gen_run_memory_vectors.mjs") };
    static readonly Dictionary<string, object?> MemoryContract = O(
        "bases", "A base is one fixed gen_traces input whose recorded commands, except its own final commitMemory, are applied from a fresh Run.create through public Run methods or run.battleCmd (v2 starts from the actual MemoryV2.migrate(data, {}, {}) store, as gen_traces does). prefix holds those command envelopes with the recorded outcome/errorCode (inputs); created and terminal are comparison points. Candidates are scanned in order under selection.rule and every scanned one is recorded",
        "points", "A point is {run: Run.s, memory: live v2 store or null} as ordered JSON including log, battle and RNG. stateHash is the gen_traces canonical hash (log/ev omitted), jsonHash SHA-256 of the ordered JSON text, undefinedPaths the own members or array items holding undefined (JSON omits the member or writes null)",
        "scenarios", "Each scenario opens its own Run at created (fresh Create) or terminal (fresh Create + prefix, asserted equal to the base point) and applies ops in order on that one Run. before/after reference the current point: ref -1 is the base point named by at, else points[ref]. Only a returned commitMemory may append a full point; every other op references the unchanged point, asserted JSON-identical",
        "store", "currentStoreMode live: the store starts as the actual MemoryV2.migrate(data, {}, {}) result and every returned v2 commitMemory replaces it with that return's state, never with recorded data. store.mode live passes that live store itself; live-patch passes a copy with the listed top-level members removed, then set (an existing member keeps its position, a new one appends); literal passes value. Patches and literals are declared public controlled inputs, never Core outputs; jsonHash is SHA-256 of the argument's ordered JSON text",
        "calls", "args are the public JSON arguments before the store. Every op asserts its arguments, store included, are JSON-identical after the call; memoryChanges, previewMemory and every rejection must leave the complete point unchanged",
        "results", "Return captured immediately at call time as {kind:\"json\",value,jsonHash,undefinedPaths}. sourceAliases lists result subgraphs that are the identical JS object as live Run state or an argument (legacy commitMemory returns run.savedMemory itself). C# returns detached graphs, so mutating a return is a C# consumer probe, not a source promise",
        "errors", "RuleError {name,code,message,detail}. MemoryError is memory_v2 fail(code), a plain Error carrying code, which C# raises as RuleException with that code. NativeError is a JS TypeError/SyntaxError at a declared native boundary, recorded with its V8 name and message; it must not become a RuleError. Any other exception, or any outcome other than the declared expect, aborts generation and preserves prior output",
        "notCovered", "An op whose role is not naturally reachable on the selected base is recorded NOT_COVERED with its reason; no phase, number or state is injected to reach it",
        "visibility", "Private test oracle: full states expose draw order and RNG",
        "separate", "The 1000-trace hash corpus, the five-scenario progression oracle and the M0 memory rule vectors are separate evidence; nothing here performs account, disk or server storage");
    static readonly Dictionary<string, string> MemorySelectionRules = new(StringComparer.Ordinal)
    {
        ["clear"] = "Scan in order and stop at the first VICTORY save that also has a natural OVER_CAPACITY selection (one character's full change set); else take the first VICTORY save with a committable selection (legacy: in-capacity greedy keep of at least one change; v2: a jointly previewable pair of change IDs)",
        ["defeat"] = "The single listed input must reach a DEFEAT save; it is complete when at least two changes exist for DEFEAT_KEEP_ONE",
    };
    static readonly (long Seed, string Length)[] MemoryClearCandidates = { (1000, "short"), (1000, "full"), (1001, "full"), (1002, "full") };
    static readonly (string Id, string Policy, string Role, (long Seed, string Length)[] Candidates)[] MemoryBasePlan =
    {
        // RULES_NOW_PLAY (2026-10-19): seed 1102 full now clears, so the defeat input is 3561 full.
        ("legacy-clear", "legacy", "clear", MemoryClearCandidates), ("legacy-defeat", "legacy", "defeat", new[] { (3561L, "full") }),
        ("v2-clear", "v2", "clear", MemoryClearCandidates), ("v2-defeat", "v2", "defeat", new[] { (3561L, "full") }),
    };
    static readonly Dictionary<string, object?> MemoryDenominators = O("bases", 4L, "scenarios", 15L, "plannedOps", 88L, "policies", O("legacy", 2L, "v2", 2L),
        "candidates", MemoryBasePlan.ToDictionary(b => b.Id, b => (object?)b.Candidates.Select(c => (object?)O("seed", c.Seed, "length", c.Length)).ToList(), StringComparer.Ordinal));
    static readonly Dictionary<string, object?> MemoryTotals = O("bases", 4L, "coveredBases", 4L, "scenarios", 15L, "coveredScenarios", 15L, "ops", 88L, "returned", 27L, "rejected", 61L,
        "notCovered", 0L, "csharpMemoryReplay", 0L);
    // The trace's own public Run surface minus its final memory commit (gen_run_memory prefixMethods).
    static readonly HashSet<string> MemoryPrefixMethods = new(new[] { "enter", "evolve", "takeCard", "skipCard", "takeRelic", "takePart", "applyFoundPart", "proceed", "rest",
        "forgeOffer", "forgeApply", "forgeRemove", "buy", "eventChoose", "archive" }, StringComparer.Ordinal);
    static readonly string[] MemoryTopFields = { "schema", "status", "scope", "dataVersion", "sources", "nativeDiagnosticsRuntime", "contract", "denominators", "coverage", "bases", "scenarios" };
    static readonly string[] MemoryBaseFields = { "id", "memoryPolicy", "role", "status", "selection", "seed", "length", "trace", "prefix", "created", "terminal", "facts" };
    static readonly string[] MemoryCandidateFields = { "seed", "length", "phase", "outcome", "prefixCommands", "eligible", "complete" };
    static readonly string[] MemoryTraceFields = { "schema", "policySeed", "setup", "initialStateHash", "commandCount", "finalStateHash", "summary" };
    static readonly string[] MemoryLegacyFactFields = { "charIds", "nonPartyCharId", "defeat", "changeCounts", "capacities", "keep", "kept", "overCapacity", "keepTwo" };
    static readonly string[] MemoryV2FactFields = { "charIds", "nonPartyCharId", "defeat", "changeIds", "single", "pair", "two", "overCapacity" };
    static readonly string[] MemoryScenarioFields = { "id", "base", "at", "role", "currentStoreMode", "status", "points", "ops" };
    static readonly string[] MemoryUncoveredFields = { "index", "label", "method", "status", "reason" };
    static readonly string[] MemoryOpFields = { "index", "label", "method", "status", "args", "expect", "outcome", "result", "error", "sourceAliases", "before", "after" };
    static readonly string[] MemoryStoredOpFields = { "index", "label", "method", "status", "args", "store", "expect", "outcome", "result", "error", "sourceAliases", "before", "after" };
    static MemoryOpPlan Planned(string label, string method, string expect = "returned", string store = "", string fact = "") => (label, method, expect, store, fact);
    // Independent literal of the generator's scenarioPlan: identity, op label/method, declared expectation, store mode, and the base fact
    // whose natural absence alone may turn an op NOT_COVERED.
    static readonly (string Id, string Base, string At, string Role, MemoryOpPlan[] Ops)[] MemoryScenarioPlan =
    {
        ("legacy-fresh", "legacy-clear", "created", "fresh query; legacy policy and phase before selection shape", new[]
        {
            Planned("changes-fresh", "memoryChanges"), Planned("preview-legacy-policy", "previewMemory", "RuleError:WRONG_PHASE"),
            Planned("commit-phase-before-shape", "commitMemory", "RuleError:WRONG_PHASE"),
        }),
        ("legacy-clear-selection-rejections", "legacy-clear", "terminal", "terminal query and BAD_CHOICE selection shapes", new[]
        {
            Planned("changes-terminal", "memoryChanges"), Planned("preview-legacy-policy", "previewMemory", "RuleError:WRONG_PHASE"),
            Planned("selection-null", "commitMemory", "RuleError:BAD_CHOICE"), Planned("selection-array", "commitMemory", "RuleError:BAD_CHOICE"),
            Planned("selection-string", "commitMemory", "RuleError:BAD_CHOICE"), Planned("selection-nonparty-character", "commitMemory", "RuleError:BAD_CHOICE"),
            Planned("indices-not-array", "commitMemory", "RuleError:BAD_CHOICE"), Planned("indices-duplicate", "commitMemory", "RuleError:BAD_CHOICE"),
            Planned("index-fraction", "commitMemory", "RuleError:BAD_CHOICE"), Planned("index-string", "commitMemory", "RuleError:BAD_CHOICE"),
            Planned("index-negative", "commitMemory", "RuleError:BAD_CHOICE"), Planned("index-out-of-bounds", "commitMemory", "RuleError:BAD_CHOICE"),
        }),
        ("legacy-clear-commit", "legacy-clear", "terminal", "OVER_CAPACITY, then a valid base-kit rebuild, then DONE", new[]
        {
            Planned("over-capacity", "commitMemory", "RuleError:OVER_CAPACITY", fact: "overCapacity"), Planned("commit-valid", "commitMemory"),
            Planned("commit-after-done", "commitMemory", "RuleError:WRONG_PHASE"), Planned("changes-after-done", "memoryChanges"),
        }),
        ("legacy-clear-prototype-key", "legacy-clear", "terminal", "native Object.prototype member name as a selection key", new[]
        {
            Planned("prototype-key-index", "commitMemory", "NativeError:TypeError"), Planned("prototype-key-empty", "commitMemory"),
        }),
        ("legacy-defeat", "legacy-defeat", "terminal", "DEFEAT_KEEP_ONE, then a one-change rebuild", new[]
        {
            Planned("changes-terminal", "memoryChanges"), Planned("keep-two", "commitMemory", "RuleError:DEFEAT_KEEP_ONE", fact: "keepTwo"),
            Planned("commit-valid", "commitMemory"), Planned("commit-after-done", "commitMemory", "RuleError:WRONG_PHASE"),
        }),
        ("v2-fresh", "v2-clear", "created", "fresh query; request shape before the phase", new[]
        {
            Planned("changes-fresh", "memoryChanges"), Planned("preview-before-save", "previewMemory", "RuleError:WRONG_PHASE"),
            Planned("request-null-before-phase", "commitMemory", "RuleError:INVALID_SELECTION", "live"), Planned("new-request-before-save", "commitMemory", "RuleError:WRONG_PHASE", "live"),
        }),
        ("v2-clear-previews", "v2-clear", "terminal", "terminal query and previews: valid, invalid IDs, natural OVER_CAPACITY", new[]
        {
            Planned("changes-terminal", "memoryChanges"), Planned("preview-empty", "previewMemory"), Planned("preview-one", "previewMemory"), Planned("preview-pair-reversed", "previewMemory"),
            Planned("preview-duplicate", "previewMemory", "MemoryError:INVALID_SELECTION"), Planned("preview-unknown-id", "previewMemory", "MemoryError:INVALID_SELECTION"),
            Planned("preview-non-string-id", "previewMemory", "MemoryError:INVALID_SELECTION"), Planned("preview-not-array", "previewMemory", "MemoryError:INVALID_SELECTION"),
            Planned("preview-over-capacity", "previewMemory", fact: "overCapacity"),
        }),
        ("v2-clear-request-order", "v2-clear", "terminal", "request shape before runId before the store", new[]
        {
            Planned("request-null", "commitMemory", "RuleError:INVALID_SELECTION", "literal"), Planned("request-id-empty", "commitMemory", "RuleError:INVALID_SELECTION", "literal"),
            Planned("expected-negative", "commitMemory", "RuleError:INVALID_SELECTION", "literal"), Planned("expected-fraction", "commitMemory", "RuleError:INVALID_SELECTION", "literal"),
            Planned("expected-string", "commitMemory", "RuleError:INVALID_SELECTION", "literal"), Planned("selection-not-array", "commitMemory", "RuleError:INVALID_SELECTION", "literal"),
            Planned("selection-duplicate", "commitMemory", "RuleError:INVALID_SELECTION", "literal"), Planned("selection-non-string", "commitMemory", "RuleError:INVALID_SELECTION", "literal"),
            Planned("run-mismatch-before-store", "commitMemory", "RuleError:RUN_MISMATCH", "literal"), Planned("run-id-absent", "commitMemory", "RuleError:RUN_MISMATCH", "live"),
        }),
        ("v2-clear-store-order", "v2-clear", "terminal", "store shape and inspect, then own receipt before phase/version", new[]
        {
            Planned("store-null", "commitMemory", "RuleError:INVALID_SNAPSHOT", "literal"), Planned("store-schema", "commitMemory", "RuleError:INVALID_SNAPSHOT", "live-patch"),
            Planned("store-version-negative", "commitMemory", "RuleError:INVALID_SNAPSHOT", "live-patch"), Planned("store-version-string", "commitMemory", "RuleError:INVALID_SNAPSHOT", "live-patch"),
            Planned("store-history-null", "commitMemory", "RuleError:INVALID_SNAPSHOT", "live-patch"), Planned("store-receipts-array", "commitMemory", "RuleError:INVALID_SNAPSHOT", "live-patch"),
            Planned("store-memory-absent", "commitMemory", "MemoryError:INVALID_SNAPSHOT", "live-patch"), Planned("store-memory-duplicate-key", "commitMemory", "MemoryError:IDENTITY_REQUIRED", "live-patch"),
            Planned("receipt-null", "commitMemory", "NativeError:TypeError", "live-patch"), Planned("receipt-without-response", "commitMemory", "NativeError:SyntaxError", "live-patch"),
            Planned("receipt-conflict-before-version", "commitMemory", "RuleError:REQUEST_CONFLICT", "live-patch"),
        }),
        ("v2-clear-version-conflicts", "v2-clear", "terminal", "versions, then ordered stored rows, then the selection", new[]
        {
            Planned("stale-request-version", "commitMemory", "RuleError:VERSION_CONFLICT", "live"), Planned("stale-run-version", "commitMemory", "RuleError:VERSION_CONFLICT", "live-patch"),
            Planned("store-moved-on", "commitMemory", "RuleError:VERSION_CONFLICT", "live-patch"), Planned("stored-rows-reordered", "commitMemory", "RuleError:VERSION_CONFLICT", "live-patch"),
            Planned("stored-members-reordered", "commitMemory", "RuleError:VERSION_CONFLICT", "live-patch"), Planned("unknown-change-id", "commitMemory", "MemoryError:INVALID_SELECTION", "live"),
            Planned("over-capacity", "commitMemory", "RuleError:OVER_CAPACITY", "live", "overCapacity"),
        }),
        ("v2-clear-commit-replay", "v2-clear", "terminal", "commit, own-receipt replays, REQUEST_CONFLICT before phase, new ID WRONG_PHASE", new[]
        {
            Planned("commit-pair", "commitMemory", store: "live"), Planned("replay-reordered", "commitMemory", store: "live"), Planned("replay-extra-member", "commitMemory", store: "live"),
            Planned("changed-selection-conflict", "commitMemory", "RuleError:REQUEST_CONFLICT", "live"), Planned("changed-version-conflict", "commitMemory", "RuleError:REQUEST_CONFLICT", "live"),
            Planned("new-request-after-done", "commitMemory", "RuleError:WRONG_PHASE", "live"), Planned("preview-after-done", "previewMemory", "RuleError:WRONG_PHASE"),
            Planned("changes-after-done", "memoryChanges"),
        }),
        ("v2-clear-store-metadata", "v2-clear", "terminal", "explicit party rows accepted; non-party memory and store metadata preserved", new[]
        {
            Planned("commit-with-metadata", "commitMemory", store: "live-patch"), Planned("replay-live", "commitMemory", store: "live"),
        }),
        ("v2-clear-proto-request-id", "v2-clear", "terminal", "own \"__proto__\" receipt", new[]
        {
            Planned("commit-proto-id", "commitMemory", store: "live"), Planned("replay-proto-id", "commitMemory", store: "live"),
            Planned("proto-id-conflict", "commitMemory", "RuleError:REQUEST_CONFLICT", "live"),
        }),
        ("v2-clear-max-safe-receipt", "v2-clear", "terminal", "controlled own receipt whose body carries a max-safe version", new[]
        {
            Planned("preview-pair", "previewMemory"), Planned("max-safe-body-conflict", "commitMemory", "RuleError:REQUEST_CONFLICT", "live-patch"),
            Planned("max-safe-replay", "commitMemory", store: "live-patch"),
        }),
        ("v2-defeat", "v2-defeat", "terminal", "DEFEAT_KEEP_ONE preview/commit, rejected ID reused, replay", new[]
        {
            Planned("changes-terminal", "memoryChanges"), Planned("preview-two", "previewMemory", fact: "two"),
            Planned("commit-two", "commitMemory", "RuleError:DEFEAT_KEEP_ONE", "live", "two"), Planned("commit-one", "commitMemory", store: "live"),
            Planned("replay-one", "commitMemory", store: "live"), Planned("new-request-after-done", "commitMemory", "RuleError:WRONG_PHASE", "live"),
        }),
    };

    sealed class MemoryBaseRecord
    {
        public string Id = "", Policy = "", Role = "", Length = "", Phase = "", Outcome = "";
        public long Seed;
        public JsonElement Row;
        public Dictionary<string, object?> Facts = new(StringComparer.Ordinal);
    }
    sealed class MemoryScenarioRecord
    {
        public string Id = "", At = "";
        public int Ordinal;
        public MemoryBaseRecord Base = new();
        public JsonElement Row;
        public readonly List<JsonElement> Points = new(), Ops = new();
    }

    // Separate memory counters and summary; every Check still enters the shared RunProgression aggregate and exit status.
    static int memoryAttempted, memoryMatched, memoryReturned, memoryRejected, memoryNative, memoryNotCovered, memoryReasons, memoryCompleted, memoryFresh, memorySaves,
        memoryComparisons, memoryStoreArguments, memoryDetached, memoryProbes, memoryPoints, memoryAliases, memoryChecksStart, memoryFailuresStart;
    static long memoryPrefixCommands;
    static readonly Dictionary<string, long> memoryObserved = new(StringComparer.Ordinal);
    static readonly List<string> memoryNativeNotes = new(), memoryUncoveredNotes = new(), memoryStops = new();
    static string memoryFixtureSummary = "NOT_COMPUTED";

    static void MemoryBoundary(string root, RunData data, string definitions)
    {
        memoryAttempted = memoryMatched = memoryReturned = memoryRejected = memoryNative = memoryNotCovered = memoryReasons = memoryCompleted = memoryFresh = memorySaves = 0;
        memoryComparisons = memoryStoreArguments = memoryDetached = memoryProbes = memoryPoints = memoryAliases = 0; memoryPrefixCommands = 0;
        memoryChecksStart = checks; memoryFailuresStart = failures;
        memoryObserved.Clear(); memoryNativeNotes.Clear(); memoryUncoveredNotes.Clear(); memoryStops.Clear(); memoryFixtureSummary = "NOT_COMPUTED";
        try { MemoryOracle(root, data, definitions); }
        finally { MemoryReport(); }
    }

    static void MemoryReport()
    {
        int memoryChecks = checks - memoryChecksStart, memoryFailures = failures - memoryFailuresStart;
        Console.WriteLine($"RunMemoryChecks: {memoryChecks - memoryFailures}/{memoryChecks} passed (also counted in the RunProgressionChecks aggregate)");
        Console.WriteLine("RunMemoryFixture: " + memoryFixtureSummary);
        Console.WriteLine($"RunMemoryCore: actual C# public MemoryChanges/PreviewMemory/CommitMemory matched {memoryMatched}/{MemoryCovered} executed ops of {MemoryPlanned} planned ({memoryAttempted} attempted; "
            + $"returned {memoryReturned}/{MemoryReturned}, rejected {memoryRejected}/{MemoryRejected} incl. native-boundary category {memoryNative}/{MemoryNativeOps}) in {memoryCompleted}/{MemoryScenarioCount} complete scenarios; "
            + $"NOT_COVERED {memoryNotCovered}/{MemoryUncovered}, never counted as passes (source reason holds under actual C# queries {memoryReasons}/{MemoryUncovered})");
        Console.WriteLine($"RunMemoryOpenings: {memoryFresh}/{MemoryFreshScenarios} fresh-Create openings, {memorySaves}/{MemorySaveScenarios} terminal-save openings through {memoryPrefixCommands} actual public prefix commands "
            + $"(battle rid/v, outcome/RuleError code, complete terminal point incl. log/RNG); v2 store = actual M0 migration, then each actual returned state; {memoryComparisons} complete point comparisons, "
            + $"{memoryStoreArguments} actual store arguments hash-checked");
        Console.WriteLine("RunMemoryObserved: " + (memoryObserved.Count == 0 ? "none" : Join(memoryObserved)));
        Console.WriteLine("RunMemoryNotCovered: " + (memoryUncoveredNotes.Count == 0 ? "none reached" : string.Join("; ", memoryUncoveredNotes)));
        foreach (string note in memoryNativeNotes) Console.WriteLine("RunMemoryNativeOnly: " + note + " (native category only; the V8 message is provenance, never claimed as C# parity)");
        Console.WriteLine("RunMemoryStops: " + (memoryStops.Count == 0 ? "none executed" : string.Join("; ", memoryStops)));
        Console.WriteLine($"RunMemoryGuards: {memoryProbes} fixture corruption/forgery probes; {memoryDetached} detached-return mutation probes (C# detachment is deliberately stronger than the {memoryAliases} recorded JS live aliases)");
        Console.WriteLine("RunMemoryScope: JS_ORACLE_ONLY oracle; RuleError and MemoryError compared by RuleException.Code only (name/message/detail unclaimed), NativeError by native category only; "
            + "no account storage, disk, server, Unity Play Mode or Android device acceptance");
    }

    static void MemoryOracle(string root, RunData data, string definitions)
    {
        Require(MemoryScenarioPlan.Length == MemoryScenarioCount && MemoryScenarioPlan.Sum(s => s.Ops.Length) == MemoryPlanned, "Run-memory host plan literal is not 15 scenarios/88 ops");
        using var document = RunCreateChecks.Decode(File.ReadAllBytes(Path.Combine(root, "engine", "tests", "vectors", "run_memory_vectors.json.gz")));
        using var content = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
        var fixture = document.RootElement; RunCreateChecks.UniqueKeys(fixture);
        string version = Text(content.RootElement.GetProperty("meta"), "version");
        var characters = Items(content.RootElement.GetProperty("characters"), "source characters").Select(c => Text(c, "id")).ToList();
        string node = ValidateMemoryHeader(fixture, root, version);
        var bases = ValidateMemoryBases(Items(fixture.GetProperty("bases"), "Run-memory bases"), characters);
        var scenarios = ValidateMemoryScenarios(Items(fixture.GetProperty("scenarios"), "Run-memory scenarios"), bases);
        ValidateMemoryCoverage(fixture.GetProperty("coverage"), bases, scenarios);
        var ops = scenarios.SelectMany(s => s.Ops).ToList();
        int uncovered = ops.Count(o => Text(o, "status") == "NOT_COVERED"), returned = ops.Count(o => Text(o, "status") == "COVERED" && Text(o, "outcome") == "returned");
        int rejected = ops.Count - uncovered - returned;
        Check(ops.Count == MemoryPlanned && ops.Count - uncovered == MemoryCovered && returned == MemoryReturned && rejected == MemoryRejected && uncovered == MemoryUncovered,
            "fixture records " + I(MemoryPlanned) + " planned ops: " + I(MemoryCovered) + " covered (" + I(MemoryReturned) + " returned, " + I(MemoryRejected) + " rejected) and " + I(MemoryUncovered) + " NOT_COVERED (" + I(ops.Count) + "/" + I(returned) + "/" + I(rejected) + "/" + I(uncovered) + ")");
        memoryFixtureSummary = $"strict one-member gzip/fatal UTF-8/unique keys; exact schema/status {MemoryStatus}/scope/7 allowlisted source pins/contract/denominators; native diagnostics provenance Node {node} only; "
            + $"4 fixed bases ({string.Join(", ", bases.Select(b => b.Id + " seed" + I(b.Seed) + "-" + b.Length + " " + b.Phase))}; {I(bases.Sum(b => Items(b.Row.GetProperty("prefix"), b.Id).Count))} prefix envelopes; candidate scans validated), "
            + $"15 fixed scenarios, {I(ops.Count)} planned ops ({I(ops.Count - uncovered)} covered: {I(returned)} returned/{I(rejected)} rejected; {I(uncovered)} NOT_COVERED with source reasons); "
            + $"{I(memoryPoints)} points hashed; coverage recomputed — oracle structure only, not C# execution";
        foreach (var sc in scenarios)
        {
            try { ExecuteMemoryScenario(sc, data, definitions); }
            catch (Exception error)
            {
                Check(false, sc.Id + " C# memory execution: " + (error is InvalidDataException ? error.Message : error.ToString()));
                memoryStops.Add(sc.Id + " aborted by " + error.GetType().Name);
            }
        }
        Check(memoryAttempted == MemoryCovered && memoryMatched == MemoryCovered && memoryReturned == MemoryReturned && memoryRejected == MemoryRejected && memoryNative == MemoryNativeOps,
            "actual C# public memory calls matched all " + I(MemoryCovered) + " executed ops (" + I(MemoryReturned) + " returned, " + I(MemoryRejected) + " rejected incl. " + I(MemoryNativeOps) + " native-boundary) from fresh Creates");
        Check(memoryNotCovered == MemoryUncovered && memoryReasons == MemoryUncovered, "the " + I(MemoryUncovered) + " NOT_COVERED ops stay uncovered and each source reason holds under actual C# public queries");
        Check(memoryCompleted == MemoryScenarioCount && memoryFresh == MemoryFreshScenarios && memorySaves == MemorySaveScenarios, "all 15 scenarios complete from 2 fresh-Create and 13 terminal-save openings");
        Check(RunCreateChecks.DataPrint(data) == definitions, "public memory replay preserves source definitions");
        Section("run memory fixture corruption", () => MemoryFixtureProbes(fixture, root, version, characters, bases, scenarios, data, definitions));
    }

    static string ValidateMemoryHeader(JsonElement fixture, string root, string version)
    {
        Fields(fixture, MemoryTopFields, "Run-memory top level");
        Require(Text(fixture, "schema") == MemorySchema && Text(fixture, "status") == MemoryStatus && Text(fixture, "scope") == MemoryScope, "Wrong run-memory schema/status/scope (JS_ORACLE_ONLY, C# Run memory replay 0)");
        Require(Text(fixture, "dataVersion") == version, "Run-memory data version differs from data/game_data.json");
        var sources = fixture.GetProperty("sources"); Fields(sources, MemorySources.Select(s => s.Key).ToArray(), "Run-memory source allowlist");
        // Every recorded path must equal the allowlist before any file is opened; only allowlisted constants are ever hashed.
        foreach (var (key, path) in MemorySources)
        {
            var record = sources.GetProperty(key); Fields(record, new[] { "path", "sha256" }, "Run-memory source " + key);
            Require(Text(record, "path") == path, "Substituted run-memory source path: " + key); HashText(record.GetProperty("sha256"));
        }
        foreach (var (key, path) in MemorySources) Require(HashText(sources.GetProperty(key).GetProperty("sha256")) == FileHash(Path.Combine(root, path)), "Stale run-memory source: " + key);
        // Generating Node release: provenance for the recorded V8 messages only, exactly { node: "major.minor.patch" }.
        var runtime = fixture.GetProperty("nativeDiagnosticsRuntime"); Fields(runtime, new[] { "node" }, "Run-memory native diagnostics runtime");
        string node = Text(runtime, "node");
        Require(Regex.IsMatch(node, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z"), "Run-memory native diagnostics runtime must be exactly {node: numeric semver}");
        var contract = fixture.GetProperty("contract"); Fields(contract, MemoryContract.Keys.ToArray(), "Run-memory contract");
        Require(MemoryV2DataLoader.Difference(MemoryContract, Graph(contract)) == null, "Run-memory contract claims changed");
        var denominators = fixture.GetProperty("denominators"); Fields(denominators, new[] { "bases", "scenarios", "plannedOps", "policies", "candidates" }, "Run-memory denominators");
        Require(MemoryV2DataLoader.Difference(MemoryDenominators, Graph(denominators)) == null, "Run-memory denominators changed");
        return node;
    }

    static List<MemoryBaseRecord> ValidateMemoryBases(List<JsonElement> rows, List<string> characters)
    {
        Require(rows.Count == MemoryBasePlan.Length, "Run-memory base count differs from the four fixed identities");
        return rows.Select((row, i) => ValidateMemoryBase(row, i, characters)).ToList();
    }

    static MemoryBaseRecord ValidateMemoryBase(JsonElement row, int ordinal, List<string> characters)
    {
        var plan = MemoryBasePlan[ordinal]; bool defeat = plan.Role == "defeat", v2 = plan.Policy == "v2";
        Fields(row, MemoryBaseFields, "Run-memory base " + I(ordinal));
        string id = Text(row, "id");
        Require(id == plan.Id && Text(row, "memoryPolicy") == plan.Policy && Text(row, "role") == plan.Role && Text(row, "status") == "COVERED",
            "Run-memory base " + I(ordinal) + " is " + id + "; the fixed plan expects COVERED " + plan.Id + " (omitted, reordered, changed or uncovered identity)");
        // The candidate scan under its rule: fixed order, stop at the first complete candidate, choose it or else the first eligible one.
        var selection = row.GetProperty("selection"); Fields(selection, new[] { "rule", "candidates", "chosen" }, id + " selection");
        Require(Text(selection, "rule") == MemorySelectionRules[plan.Role], id + " selection rule changed");
        var candidates = Items(selection.GetProperty("candidates"), id + " candidates");
        Require(candidates.Count >= 1 && candidates.Count <= plan.Candidates.Length, id + " candidate scan is empty or longer than the fixed list");
        int complete = -1, eligible = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i]; Fields(candidate, MemoryCandidateFields, id + " candidate " + I(i));
            Require(Integer(candidate.GetProperty("seed")) == plan.Candidates[i].Seed && Text(candidate, "length") == plan.Candidates[i].Length, id + " candidate scan order differs from the fixed list");
            bool isEligible = Flag(candidate, "eligible"), isComplete = Flag(candidate, "complete");
            Require((!isComplete || isEligible) && Integer(candidate.GetProperty("prefixCommands")) >= 0 && Text(candidate, "phase") is "VICTORY" or "DEFEAT" && Text(candidate, "outcome").Length != 0,
                id + " candidate " + I(i) + " is not a terminal save, or complete without meeting the role");
            if (isEligible && eligible < 0) eligible = i;
            if (isComplete && complete < 0) complete = i;
        }
        Require(candidates.Count == (complete >= 0 ? complete + 1 : plan.Candidates.Length), id + " scan must stop exactly at the first complete candidate");
        long chosen = Integer(selection.GetProperty("chosen"));
        Require(chosen >= 0 && chosen == (complete >= 0 ? complete : eligible), id + " chosen candidate differs from the selection rule");
        var pick = candidates[(int)chosen];
        long seed = Integer(row.GetProperty("seed")); string length = Text(row, "length");
        Require(seed == Integer(pick.GetProperty("seed")) && length == Text(pick, "length"), id + " seed/length differ from the chosen candidate");
        var trace = row.GetProperty("trace"); Fields(trace, MemoryTraceFields, id + " trace");
        Require(Text(trace, "schema") == TraceSchema && Integer(trace.GetProperty("policySeed")) == ((uint)seed ^ 0xc0decafeu), id + " trace schema/policy seed ((seed ^ 0xc0decafe) >>> 0)");
        var setup = AsObject(Graph(trace.GetProperty("setup")), id + " trace setup");
        Require(setup.GetValueOrDefault("seed") as long? == seed && setup.GetValueOrDefault("length") as string == length && setup.GetValueOrDefault("memoryPolicy") as string == plan.Policy
            && (!v2 || setup.GetValueOrDefault("runId") as string == "trace-" + I(seed) + "-" + length + "-v2" && setup.GetValueOrDefault("memoryVersion") as long? == 0 && setup.GetValueOrDefault("useMemory") is true),
            id + " trace setup is not the gen_traces input of the chosen candidate");
        // Prefix envelopes are inputs: the trace's public commands except its own final commitMemory.
        var prefix = Items(row.GetProperty("prefix"), id + " prefix"); long committed = 0, refused = 0;
        for (int i = 0; i < prefix.Count; i++)
        {
            var step = prefix[i]; string at = id + " prefix#" + I(i);
            Fields(step, new[] { "command", "outcome", "errorCode" }, at);
            var command = step.GetProperty("command");
            Require(command.ValueKind == JsonValueKind.Object, at + " command envelope");
            Require(command.TryGetProperty("scope", out var scope), at + " command scope");
            string kind = Text(command, "kind");
            if (Text(scope) == "run")
            {
                Fields(command, RunCommandFields, at);
                Require(MemoryPrefixMethods.Contains(kind) && command.GetProperty("args").ValueKind == JsonValueKind.Array, at + " prefix holds a memory commit or an unknown Run command " + kind);
            }
            else
            {
                Require(Text(scope) == "battle", at + " unknown command scope"); Fields(command, BattleCommandFields, at);
                long v = Integer(command.GetProperty("v"));
                Require(kind is "play" or "ult" or "end" && Text(command, "rid") == "r" + I(v) + "-" + kind && command.GetProperty("args").ValueKind == JsonValueKind.Object, at + " battle request envelope");
            }
            string outcome = Text(step, "outcome"); var code = step.GetProperty("errorCode");
            if (outcome == "committed") { Require(code.ValueKind == JsonValueKind.Null, at + " committed step carries an error code"); committed++; }
            else { Require(outcome == "rejected" && code.ValueKind == JsonValueKind.String && Text(code).Length != 0, at + " rejected step code"); refused++; }
        }
        Require(prefix.Count == Integer(pick.GetProperty("prefixCommands")) && Integer(trace.GetProperty("commandCount")) == prefix.Count + 1, id + " prefix must be the trace minus only its final commitMemory");
        var summary = trace.GetProperty("summary"); Fields(summary, SummaryFields, id + " trace summary");
        Require(Integer(summary.GetProperty("committed")) == committed + 1 && Integer(summary.GetProperty("rejected")) == refused, id + " prefix outcomes plus the committed final save differ from the trace summary");
        var createdJson = row.GetProperty("created"); var created = MemoryPoint(createdJson, id + " created"); var terminal = MemoryPoint(row.GetProperty("terminal"), id + " terminal");
        Require(HashText(createdJson.GetProperty("stateHash")) == HashText(trace.GetProperty("initialStateHash")), id + " created context hash differs from the trace initial hash");
        var fresh = AsObject(created["run"], id + " created run");
        Require(fresh.GetValueOrDefault("phase") as string == "MAP" && fresh.ContainsKey("pos") && fresh["pos"] == null && fresh.ContainsKey("battle") && fresh["battle"] == null, id + " created point is not a fresh MAP state");
        var run = AsObject(terminal["run"], id + " terminal run"); var pending = AsObject(run.GetValueOrDefault("pending"), id + " terminal pending");
        string phase = defeat ? "DEFEAT" : "VICTORY", result = Get(run.GetValueOrDefault("result"), "outcome") as string ?? "";
        Require(pending.GetValueOrDefault("type") as string == "save" && (pending.GetValueOrDefault("defeat") is true) == defeat && run.GetValueOrDefault("phase") as string == phase
            && Text(pick, "phase") == phase && result.Length != 0 && Text(pick, "outcome") == result && Text(summary, "outcome") == result, id + " terminal point is not the chosen candidate's " + phase + " save");
        bool stores = created["memory"] is Dictionary<string, object?> && terminal["memory"] is Dictionary<string, object?>, empty = created["memory"] == null && terminal["memory"] == null;
        Require(v2 ? stores : empty, id + " point store presence differs from the policy");
        // Facts only choose op inputs and NOT_COVERED; their party/defeat identity must be the terminal point's and the source data's.
        var factsJson = row.GetProperty("facts"); Fields(factsJson, v2 ? MemoryV2FactFields : MemoryLegacyFactFields, id + " facts");
        var facts = AsObject(Graph(factsJson), id + " facts");
        var party = AsList(run.GetValueOrDefault("party"), id + " terminal party").Select(p => Get(p, "charId") as string).ToList();
        var charIds = AsList(facts["charIds"], id + " facts charIds").Select(c => c as string).ToList();
        Require(charIds.SequenceEqual(party) && facts["defeat"] is bool flag && flag == defeat && facts["nonPartyCharId"] as string == characters.FirstOrDefault(c => !party.Contains(c)),
            id + " facts party/defeat/first non-party character differ from the terminal point and source data");
        bool pair = facts.GetValueOrDefault("pair") is List<object?> { Count: 2 }, kept = facts.GetValueOrDefault("kept") is long count && count > 0;
        Require(defeat || (v2 ? pair : kept), id + " clear base lacks the committable selection its role requires");
        return new MemoryBaseRecord { Id = id, Policy = plan.Policy, Role = plan.Role, Seed = seed, Length = length, Phase = phase, Outcome = result, Row = row, Facts = facts };
    }

    static List<MemoryScenarioRecord> ValidateMemoryScenarios(List<JsonElement> rows, List<MemoryBaseRecord> bases)
    {
        Require(rows.Count == MemoryScenarioPlan.Length, "Run-memory scenario count differs from the fifteen fixed identities");
        return rows.Select((row, i) => ValidateMemoryScenario(row, i, bases)).ToList();
    }

    static MemoryScenarioRecord ValidateMemoryScenario(JsonElement row, int ordinal, List<MemoryBaseRecord> bases)
    {
        var plan = MemoryScenarioPlan[ordinal];
        Fields(row, MemoryScenarioFields, "Run-memory scenario " + I(ordinal));
        string id = Text(row, "id");
        Require(id == plan.Id && Text(row, "base") == plan.Base && Text(row, "at") == plan.At && Text(row, "role") == plan.Role,
            "Run-memory scenario " + I(ordinal) + " is " + id + "; the fixed plan expects " + plan.Id + " (omitted, reordered or changed identity)");
        var b = bases.Single(x => x.Id == plan.Base);
        Require(Text(row, "currentStoreMode") == (b.Policy == "v2" ? "live" : "none") && Text(row, "status") == "COVERED", id + " store mode/status");
        var sc = new MemoryScenarioRecord { Id = id, At = plan.At, Ordinal = ordinal, Base = b, Row = row };
        var points = Items(row.GetProperty("points"), id + " points");
        for (int i = 0; i < points.Count; i++) { MemoryPoint(points[i], id + " point " + I(i)); sc.Points.Add(points[i]); }
        var ops = Items(row.GetProperty("ops"), id + " ops");
        Require(ops.Count == plan.Ops.Length, id + " op omitted or added");
        var current = b.Row.GetProperty(plan.At); long currentRef = -1; int used = 0;
        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i]; var p = plan.Ops[i]; string where = id + "#" + I(i) + " " + p.Label;
            Require(op.ValueKind == JsonValueKind.Object, where + " op record");
            Require(op.TryGetProperty("status", out var status), where + " op status");
            if (Text(status) == "NOT_COVERED")
            {
                // Only an op whose base fact is naturally absent may stay uncovered, with the source reason; nothing is injected to reach it.
                Fields(op, MemoryUncoveredFields, where);
                Require(Integer(op.GetProperty("index")) == i && Text(op, "label") == p.Label && Text(op, "method") == p.Method, where + " index/label/method");
                Require(p.When.Length != 0 && Get(b.Facts, p.When) == null && Text(op, "reason") == (p.When == "overCapacity" ? NoOverCapacity : NoSecondChange),
                    where + " NOT_COVERED only where its " + (p.When.Length == 0 ? "unconditional op" : p.When + " fact") + " is naturally absent, with the source reason");
                sc.Ops.Add(op); continue;
            }
            Require(Text(status) == "COVERED", where + " op status");
            Fields(op, p.Store.Length != 0 ? MemoryStoredOpFields : MemoryOpFields, where);
            Require(Integer(op.GetProperty("index")) == i && Text(op, "label") == p.Label && Text(op, "method") == p.Method, where + " index/label/method");
            Require(p.When.Length == 0 || Get(b.Facts, p.When) != null, where + " covered although its " + p.When + " fact is absent");
            Require(Items(op.GetProperty("args"), where + " args").Count == (p.Method == "memoryChanges" ? 0 : 1), where + " public argument count");
            if (p.Store.Length != 0) ValidateMemoryStore(op.GetProperty("store"), p.Store, current, where);
            var expect = op.GetProperty("expect");
            if (p.Expect == "returned") Require(expect.ValueKind == JsonValueKind.String && Text(expect) == "returned", where + " declared expectation differs from the fixed plan");
            else
            {
                var (kind, value) = Expected(p.Expect); string field = kind == "NativeError" ? "name" : "code";
                Fields(expect, new[] { "kind", field }, where + " declared expectation");
                Require(Text(expect, "kind") == kind && Text(expect, field) == value, where + " declared expectation differs from the fixed plan " + p.Expect);
            }
            string outcome = Text(op, "outcome"); var result = op.GetProperty("result"); var recordedError = op.GetProperty("error");
            var aliases = Items(op.GetProperty("sourceAliases"), where + " sourceAliases");
            if (p.Expect == "returned")
            {
                Require(outcome == "returned" && recordedError.ValueKind == JsonValueKind.Null, where + " returned op error/outcome");
                Result(result, where + " result"); Require(Text(result, "kind") == "json", where + " public memory methods return JSON objects");
                foreach (var alias in aliases)
                {
                    Fields(alias, new[] { "result", "shares" }, where + " source alias"); AsList(Graph(alias.GetProperty("result")), where + " alias path");
                    var owner = AsList(Graph(alias.GetProperty("shares")), where + " alias owner");
                    Require(owner.Count != 0 && owner[0] is "run" or "arguments", where + " alias owner is live Run state or an argument");
                    Count(ref memoryAliases);
                }
            }
            else
            {
                var (kind, value) = Expected(p.Expect);
                Require(outcome == "rejected" && result.ValueKind == JsonValueKind.Null && aliases.Count == 0, where + " rejected op result/aliases");
                Fields(recordedError, kind == "RuleError" ? new[] { "kind", "name", "code", "message", "detail" } : kind == "MemoryError" ? new[] { "kind", "name", "code", "message" } : new[] { "kind", "name", "message" },
                    where + " " + kind + " record");
                Require(Text(recordedError, "kind") == kind && Text(recordedError, "name").Length != 0 && Text(recordedError, "message").Length != 0
                    && Text(recordedError, kind == "NativeError" ? "name" : "code") == value && (kind != "MemoryError" || Text(recordedError, "message") == value), where + " recorded error differs from its declared expectation");
                if (kind == "RuleError") Result(recordedError.GetProperty("detail"), where + " RuleError detail");
            }
            var before = op.GetProperty("before"); Fields(before, RefFields, where + " before");
            Require(SameRef(before, currentRef, current), where + " before reference differs from the current point");
            var after = op.GetProperty("after"); Fields(after, RefFields, where + " after");
            if (!SameRef(after, currentRef, current))
            {
                Require(outcome == "returned" && p.Method == "commitMemory" && used < sc.Points.Count, where + " only a returned commit may append a point");
                current = sc.Points[used]; currentRef = used++;
                Require(SameRef(after, currentRef, current), where + " after reference");
            }
            sc.Ops.Add(op);
        }
        Require(used == sc.Points.Count, id + " has unreferenced points");
        return sc;
    }

    // Source integrity only: the declared argument rebuilt from the recorded before point. Execution rebuilds it from the actual C# store.
    static void ValidateMemoryStore(JsonElement record, string mode, JsonElement current, string where)
    {
        Require(record.ValueKind == JsonValueKind.Object && record.TryGetProperty("mode", out var declared) && Text(declared) == mode, where + " store mode differs from the declared " + mode + " input");
        Fields(record, mode == "live" ? new[] { "mode", "jsonHash" } : mode == "live-patch" ? new[] { "mode", "set", "remove", "jsonHash" } : new[] { "mode", "value", "jsonHash" }, where + " store record");
        if (mode == "live-patch")
            Require(record.GetProperty("set").ValueKind == JsonValueKind.Object && Items(record.GetProperty("remove"), where + " removed members").All(k => k.ValueKind == JsonValueKind.String), where + " live-patch set/remove");
        string? text = OrderedText(MemoryStoreArgument(Graph(current.GetProperty("state").GetProperty("memory")) as Dictionary<string, object?>, record, where));
        Check(text != null && Sha(text) == HashText(record.GetProperty("jsonHash")), where + " store argument hash recomputed from the before point");
    }

    // live passes the live store itself (as JS does); live-patch a copy with members removed, then set in source order; literal its declared value.
    static object? MemoryStoreArgument(Dictionary<string, object?>? live, JsonElement record, string where)
    {
        string mode = Text(record, "mode");
        if (mode == "live") return live;
        if (mode == "literal") return Graph(record.GetProperty("value"));
        Require(mode == "live-patch", where + " unknown store mode " + mode);
        var removed = Items(record.GetProperty("remove"), where + " removed members").Select(k => Text(k)).ToHashSet(StringComparer.Ordinal);
        // A fresh dictionary never reuses a freed slot: kept members stay in place, a set member keeps its position, a new one appends (Object.assign).
        var patched = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in AsObject(Graph(Element(live)), where + " live store to patch")) if (!removed.Contains(pair.Key)) patched[pair.Key] = pair.Value;
        foreach (var pair in AsObject(Graph(record.GetProperty("set")), where + " patch set")) patched[pair.Key] = pair.Value;
        return patched;
    }
    // JSON.stringify text of a plain JSON round trip of the graph; null when it holds a non-integer number.
    static string? OrderedText(object? graph) { try { return Ordered(Graph(Element(graph))); } catch (ArgumentException) { return null; } }

    // gen_run_memory summarize(), recomputed only from validated records; the recorded totals must also be the measured literals.
    static void ValidateMemoryCoverage(JsonElement coverage, List<MemoryBaseRecord> bases, List<MemoryScenarioRecord> scenarios)
    {
        Fields(coverage, new[] { "totals", "bases", "perScenario", "methods", "outcomes", "notCovered" }, "Run-memory coverage");
        var methods = new Dictionary<string, object?>(StringComparer.Ordinal); var outcomes = new Dictionary<string, object?>(StringComparer.Ordinal);
        var uncovered = new List<object?>(); var perScenario = new List<object?>();
        foreach (var sc in scenarios)
        {
            long returned = 0, refused = 0, skipped = 0;
            foreach (var op in sc.Ops)
            {
                string method = Text(op, "method");
                if (Text(op, "status") == "NOT_COVERED") { skipped++; uncovered.Add(O("scenario", sc.Id, "label", Text(op, "label"), "reason", Text(op, "reason"))); continue; }
                string outcome = Text(op, "outcome"); var error = op.GetProperty("error");
                if (outcome == "returned") returned++; else refused++;
                if (methods.GetValueOrDefault(method) is not Dictionary<string, object?> tally) methods[method] = tally = O("returned", 0L, "rejected", 0L);
                tally[outcome] = (tally.GetValueOrDefault(outcome) as long? ?? 0) + 1;
                Coverage.Add(outcomes, method + ":" + (error.ValueKind == JsonValueKind.Null ? "returned" : Text(error, "kind") + ":" + Text(error, error.TryGetProperty("code", out _) ? "code" : "name")));
            }
            var last = AsObject(Graph((sc.Points.Count != 0 ? sc.Points[^1] : sc.Base.Row.GetProperty(sc.At)).GetProperty("state")), sc.Id + " final point");
            perScenario.Add(O("id", sc.Id, "base", sc.Base.Id, "at", sc.At, "status", "COVERED", "ops", (long)sc.Ops.Count, "returned", returned, "rejected", refused, "notCovered", skipped,
                "finalPhase", Get(last["run"], "phase"), "storeVersion", last["memory"] is Dictionary<string, object?> memory ? memory.GetValueOrDefault("version") : null));
        }
        long Sum(string key) => perScenario.Sum(entry => Get(entry, key) as long? ?? 0);
        var recomputed = O("totals", O("bases", (long)bases.Count, "coveredBases", (long)bases.Count, "scenarios", (long)scenarios.Count, "coveredScenarios", (long)scenarios.Count,
                "ops", Sum("ops"), "returned", Sum("returned"), "rejected", Sum("rejected"), "notCovered", (long)uncovered.Count, "csharpMemoryReplay", 0L),
            "bases", bases.Select(b => (object?)O("id", b.Id, "status", "COVERED", "seed", b.Seed, "length", b.Length, "phase", b.Phase, "outcome", b.Outcome)).ToList(),
            "perScenario", perScenario, "methods", methods, "outcomes", outcomes, "notCovered", uncovered);
        var recorded = Graph(coverage);
        string? drift = MemoryV2DataLoader.Difference(recorded, recomputed);
        Check(drift == null, "run memory coverage/count summary equals the recomputation from every recorded op" + (drift == null ? "" : ": " + drift));
        string? totals = MemoryV2DataLoader.Difference(MemoryTotals, Get(recorded, "totals"));
        Check(totals == null, "run memory totals are the measured 4 bases/15 scenarios/88 planned ops (27 returned, 61 rejected, 0 NOT_COVERED)/C# replay 0" + (totals == null ? "" : ": " + totals));
    }

    static Dictionary<string, object?> MemoryPoint(JsonElement point, string label)
    {
        Fields(point, PointFields, label);
        var json = point.GetProperty("state"); var state = AsObject(Graph(json), label + " state");
        Require(state.Keys.SequenceEqual(new[] { "run", "memory" }) && state["run"] is Dictionary<string, object?> && state["memory"] is null or Dictionary<string, object?>, label + " point is not {run, memory}");
        Check(TryHash(state) == HashText(point.GetProperty("stateHash")), label + " canonical context hash");
        Check(Sha(json.GetRawText()) == HashText(point.GetProperty("jsonHash")), label + " ordered source JSON hash");
        Omissions(state, point.GetProperty("undefinedPaths"), label);
        Count(ref memoryPoints); return state;
    }
    // Path-by-path complete comparison (C# dictionaries promise no JS member order) plus the canonical context hash and own-undefined omissions.
    static void MemoryCompare(JsonElement point, Dictionary<string, object?> actual, string label)
    {
        Count(ref memoryComparisons);
        string? diff = MemoryV2DataLoader.Difference(Graph(point.GetProperty("state")), actual);
        Check(diff == null, label + " complete {run, memory} incl. log/battle/RNG/null/absence" + (diff == null ? "" : ": " + diff));
        Check(TryHash(actual) == HashText(point.GetProperty("stateHash")), label + " canonical context hash");
        foreach (object? path in AsList(Graph(point.GetProperty("undefinedPaths")), label)) Check(Omitted(actual, path), label + " JS own-undefined " + CanonicalState.Serialize(path) + " stays absent, never null");
    }
    static bool SameRef(JsonElement reference, long index, JsonElement point) => Integer(reference.GetProperty("ref")) == index
        && HashText(reference.GetProperty("stateHash")) == HashText(point.GetProperty("stateHash")) && HashText(reference.GetProperty("jsonHash")) == HashText(point.GetProperty("jsonHash"));
    static (string Kind, string Value) Expected(string expect) { int colon = expect.IndexOf(':'); return (expect[..colon], expect[(colon + 1)..]); }
    static bool Flag(JsonElement json, string key)
    {
        Require(json.TryGetProperty(key, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False), "Expected JSON boolean " + key);
        return value.GetBoolean();
    }

    // Actual C#: fresh Create, the base's recorded public prefix, the real M0 store, then each public memory call on that one Run. Returns the op
    // index of the first divergence (Ops.Count when complete, -1 at the opening), so the forgery probes can assert where it stops.
    static int ExecuteMemoryScenario(MemoryScenarioRecord sc, RunData data, string definitions)
    {
        var b = sc.Base; var plan = MemoryScenarioPlan[sc.Ordinal];
        var setup = RunDataLoader.Setup(b.Row.GetProperty("trace").GetProperty("setup")); string setupBefore = RunCreateChecks.SetupPrint(setup);
        var run = ExpeditionRun.Create(data, setup);
        // gen_traces' v2 store is the real M0 migration of an empty account; afterwards only actual returned states replace it.
        Dictionary<string, object?>? store = b.Policy == "v2"
            ? MemoryV2.Migrate(data.Memory, new Dictionary<string, object?>(StringComparer.Ordinal), new Dictionary<string, object?>(StringComparer.Ordinal)).ToJsonGraph() : null;
        int start = Failed(), reached = -1; string stopped = "opening mismatch";
        MemoryCompare(b.Row.GetProperty("created"), Context(run.State, store), sc.Id + " fresh Create");
        if (Failed() == start && sc.At == "terminal")
        {
            int replayed = ReplayMemoryPrefix(run, b, sc.Id);
            MemoryCompare(b.Row.GetProperty("terminal"), Context(run.State, store), sc.Id + " terminal save after " + I(replayed) + " actual prefix commands");
            if (Failed() == start) { Count(ref memorySaves); if (!probing) memoryPrefixCommands += replayed; }
        }
        else if (Failed() == start) Count(ref memoryFresh);
        if (Failed() == start) { reached = 0; stopped = "complete"; }
        for (; reached >= 0 && reached < sc.Ops.Count; reached++)
        {
            var op = sc.Ops[reached]; var p = plan.Ops[reached]; string where = sc.Id + "#" + I(reached) + " " + p.Label;
            if (Text(op, "status") == "NOT_COVERED") { ConfirmUncovered(run, store, b, p.When, Text(op, "reason"), where); continue; }
            int failed = Failed(); Count(ref memoryAttempted);
            var args = AsList(Graph(op.GetProperty("args")), where + " args");
            bool stored = op.TryGetProperty("store", out var record);
            object? argument = stored ? MemoryStoreArgument(store, record, where) : null;
            if (stored)
            {
                Count(ref memoryStoreArguments); string? text = OrderedText(argument);
                Check(text != null && Sha(text) == HashText(record.GetProperty("jsonHash")), where + " actual store argument (the live C# store or its declared patch/literal) has the source ordered JSON hash");
            }
            string live = Full(Context(run.State, store)), argsText = Full(args), argumentText = Full(argument);
            Dictionary<string, object?>? returned = null; string? code = null; MemoryBoundaryException? native = null;
            try
            {
                returned = p.Method switch
                {
                    "memoryChanges" => run.MemoryChanges(),
                    "previewMemory" => run.PreviewMemory(args[0]),
                    "commitMemory" => stored ? run.CommitMemory(args[0], argument) : run.CommitMemory(args[0]),
                    _ => throw new InvalidDataException(where + " is not a public memory method"),
                };
            }
            catch (RuleException error) { code = error.Code; }
            catch (MemoryBoundaryException error) { native = error; }
            catch (Exception error) when (error is not InvalidDataException) { Check(false, where + " unexpected " + Describe(error)); stopped = "exception at #" + I(reached); break; }
            // RuleError and MemoryError are both the semantic RuleException.Code; a NativeError must stay a native boundary, never a RuleException.
            var recordedError = op.GetProperty("error"); bool expectReturn = Text(op, "outcome") == "returned";
            string kind = expectReturn ? "returned" : Text(recordedError, "kind"), expected = expectReturn ? "" : Text(recordedError, kind == "NativeError" ? "name" : "code");
            bool match = kind == "returned" ? returned != null && code == null && native == null
                : kind == "NativeError" ? native != null && native.JavaScriptName == expected && code == null
                : code == expected && native == null;
            string observed = native != null ? "native " + native.JavaScriptName : code != null ? "RuleException " + code : "returned";
            Check(match, where + " outcome/class/code (C# " + observed + ", JS " + (expectReturn ? "returned" : kind + " " + expected) + ")");
            Check(Full(args) == argsText && Full(argument) == argumentText, where + " caller arguments, store argument included, stay JSON-identical");
            if (match && returned != null)
            {
                var result = op.GetProperty("result");
                string? diff = MemoryV2DataLoader.Difference(Graph(result.GetProperty("value")), returned);
                Check(diff == null, where + " complete returned graph" + (diff == null ? "" : ": " + diff));
                foreach (object? path in AsList(Graph(result.GetProperty("undefinedPaths")), where + " return undefinedPaths"))
                    Check(Omitted(returned, path), where + " return own-undefined " + CanonicalState.Serialize(path) + " stays absent, never null");
                // The actual returned store becomes host-owned before the return is mutated as a probe; no recorded store is ever an input.
                if (p.Method == "commitMemory" && store != null) store = AsObject(Graph(Element(Get(returned, "state"))), where + " actual committed store");
                string liveAfter = Full(Context(run.State, store)), original = Full(returned);
                Scribble(returned); Count(ref memoryDetached);
                Check(Full(returned) != original, where + " positive control: caller mutation changes the returned graph");
                Check(Full(Context(run.State, store)) == liveAfter && Full(args) == argsText && Full(argument) == argumentText,
                    where + " mutating the returned graph reaches neither the live Run/store nor the caller arguments");
            }
            if (match && native != null && !probing)
                memoryNativeNotes.Add(where + ": JS " + expected + " \"" + Text(recordedError, "message") + "\"; C# MemoryBoundaryException " + native.JavaScriptName + " \"" + native.Message + "\"");
            long reference = Integer(op.GetProperty("after").GetProperty("ref"));
            var point = reference < 0 ? b.Row.GetProperty(sc.At) : sc.Points[(int)reference];
            var context = Context(run.State, store);
            MemoryCompare(point, context, where + " after");
            if (store != null)
                Check(OrderedText(store) is string actualStore && actualStore == OrderedText(Graph(point.GetProperty("state").GetProperty("memory"))), where + " live store keeps the source member order (ordered JSON text)");
            if (!(expectReturn && p.Method == "commitMemory")) Check(Full(context) == live, where + " leaves the complete live Run/store incl. log/RNG unchanged");
            if (Failed() != failed) { stopped = "mismatch at #" + I(reached) + " " + p.Label; break; }
            Count(ref memoryMatched); if (expectReturn) Count(ref memoryReturned); else Count(ref memoryRejected);
            if (native != null) Count(ref memoryNative);
            if (!probing) Bump(memoryObserved, p.Method + ":" + observed);
        }
        Check(stopped == "complete", sc.Id + " actual C# matched every covered public memory op (" + I(sc.Ops.Count(o => Text(o, "status") == "COVERED")) + " covered; stopped " + stopped + ")");
        Check(RunCreateChecks.SetupPrint(setup) == setupBefore && RunCreateChecks.DataPrint(data) == definitions, sc.Id + " Create/prefix/memory calls preserve the caller setup and source definitions");
        if (!probing) memoryStops.Add(sc.Id + " " + stopped);
        if (stopped == "complete" && Failed() == start) Count(ref memoryCompleted);
        return stopped == "complete" ? sc.Ops.Count : reached;
    }

    // The recorded envelopes through the existing public command binders: rid/v must equal the live battle and only the recorded RuleError code is
    // a rejection. Returns the number of commands matched.
    static int ReplayMemoryPrefix(ExpeditionRun run, MemoryBaseRecord b, string label)
    {
        var prefix = Items(b.Row.GetProperty("prefix"), b.Id + " prefix");
        for (int i = 0; i < prefix.Count; i++)
        {
            var step = prefix[i]; var command = step.GetProperty("command");
            string scope = Text(command, "scope"), kind = Text(command, "kind"), where = label + " prefix#" + I(i) + " " + scope + ":" + kind;
            var errorCode = step.GetProperty("errorCode"); string? expectedCode = errorCode.ValueKind == JsonValueKind.Null ? null : Text(errorCode);
            string? code = null;
            try
            {
                var call = scope == "battle" ? BattleCall(run, run.State, kind, Text(command, "rid"), Integer(command.GetProperty("v")), command.GetProperty("args").GetRawText())
                    : RunCall(run, kind, AsList(Graph(command.GetProperty("args")), where + " args"));
                try { call(); } catch (RuleException error) { code = error.Code; }
            }
            catch (Exception error) when (error is not InvalidDataException) { Check(false, where + " " + Describe(error)); return i; }
            string outcome = code == null ? "committed" : "rejected";
            if (outcome != Text(step, "outcome") || code != expectedCode)
            {
                Check(false, where + " outcome/RuleError code (C# " + outcome + (code == null ? "" : " " + code) + ", JS " + Text(step, "outcome") + (expectedCode == null ? "" : " " + expectedCode) + ")");
                return i;
            }
        }
        return prefix.Count;
    }

    // NOT_COVERED is never a pass; the source reason must still hold for the actual C# Run at that point, through pure public queries.
    static void ConfirmUncovered(ExpeditionRun run, object? store, MemoryBaseRecord b, string fact, string reason, string where)
    {
        Count(ref memoryNotCovered);
        string live = Full(Context(run.State, store)); bool holds;
        try
        {
            var changes = run.MemoryChanges(); var party = run.State.Party.Select(p => p.CharId).ToList();
            List<object?> Of(string cid) => AsList(Get(changes.GetValueOrDefault(cid), "changes"), where + " " + cid + " changes");
            long Number(object? value) => Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (fact != "overCapacity") holds = party.Sum(cid => Of(cid).Count) < 2;
            else if (b.Policy == "legacy") holds = party.All(cid => Of(cid).Sum(change => Number(Get(change, "cost"))) <= Number(Get(changes.GetValueOrDefault(cid), "capacity")));
            else holds = party.All(cid => Of(cid).Count == 0 || run.PreviewMemory(Of(cid).Select(change => Get(change, "id")).ToList()).GetValueOrDefault("error") as string != "OVER_CAPACITY");
        }
        catch (Exception error) when (error is RuleException or MemoryBoundaryException) { Check(false, where + " NOT_COVERED reason queries: " + Describe(error)); return; }
        Check(holds, where + " NOT_COVERED source reason holds under actual C# public queries: " + reason);
        Check(Full(Context(run.State, store)) == live, where + " NOT_COVERED reason queries leave the live Run/store unchanged");
        if (holds) Count(ref memoryReasons);
        if (!probing) memoryUncoveredNotes.Add(where + " (" + reason + ")");
    }

    static void MemoryProbe(string label, Action validate) { memoryProbes++; Check(Rejects(validate), "corruption rejected: run memory " + label); }

    static void MemoryFixtureProbes(JsonElement fixture, string root, string version, List<string> characters, List<MemoryBaseRecord> bases, List<MemoryScenarioRecord> scenarios,
        RunData data, string definitions)
    {
        string zeros = new string('0', 64);
        // Header: bases/scenarios/coverage are irrelevant to ValidateMemoryHeader, so each probe edits exactly one claim.
        var header = Shallow(fixture); header["coverage"] = O(); header["bases"] = L(); header["scenarios"] = L();
        Control("run memory header", () => ValidateMemoryHeader(Element(header), root, version));
        void HeaderProbe(string label, string key, object? value, bool remove = false)
        {
            var bad = new Dictionary<string, object?>(header, StringComparer.Ordinal); if (remove) bad.Remove(key); else bad[key] = value;
            MemoryProbe("header " + label, () => ValidateMemoryHeader(Element(bad), root, version));
        }
        var sources = fixture.GetProperty("sources"); var runtime = fixture.GetProperty("nativeDiagnosticsRuntime");
        // The substituted path does not exist: reading it before the allowlist check would surface as a file error, not as this rejection.
        HeaderProbe("substituted generator path", "sources", Edit(sources, "generator", Edit(sources.GetProperty("generator"), "path", "../outside/gen_run_memory_vectors.mjs")));
        // Another real producer with its true hash: the allowlist, never the file content, decides which file may fill a slot.
        HeaderProbe("generator slot pinned to another real producer", "sources",
            Edit(sources, "generator", O("path", "tools/gen_run_progression_vectors.mjs", "sha256", FileHash(Path.Combine(root, "tools", "gen_run_progression_vectors.mjs")))));
        HeaderProbe("stale engine source", "sources", Edit(sources, "engine", Edit(sources.GetProperty("engine"), "sha256", zeros)));
        HeaderProbe("missing generator source", "sources", Edit(sources, "generator", null, true));
        HeaderProbe("status claims C# replay", "status", "CSHARP_REPLAY_COMPLETE");
        HeaderProbe("scope claims C# replay", "scope", MemoryScope.Replace("C# Run memory replay 0", "C# Run memory replay complete"));
        HeaderProbe("contract launders NativeError into RuleError", "contract", Edit(fixture.GetProperty("contract"), "errors", "NativeError is recorded as a RuleError"));
        HeaderProbe("planned ops shortened to the covered ones", "denominators", Edit(fixture.GetProperty("denominators"), "plannedOps", 85L));
        HeaderProbe("native runtime not a numeric semver", "nativeDiagnosticsRuntime", Edit(runtime, "node", "v" + Text(runtime, "node")));
        var foreign = new Dictionary<string, object?>(header, StringComparer.Ordinal) { ["nativeDiagnosticsRuntime"] = O("node", "0.0.1") };
        Control("run memory header with another valid Node release (provenance only)", () => ValidateMemoryHeader(Element(foreign), root, version));

        var baseRows = Items(fixture.GetProperty("bases"), "Run-memory bases"); var scenarioRows = Items(fixture.GetProperty("scenarios"), "Run-memory scenarios");
        MemoryProbe("base list missing its last base", () => ValidateMemoryBases(baseRows.Take(baseRows.Count - 1).ToList(), characters));
        MemoryProbe("scenario dropped", () => ValidateMemoryScenarios(scenarioRows.Where((_, i) => i != 4).ToList(), bases));
        MemoryProbe("scenarios reordered", () => ValidateMemoryScenarios(scenarioRows.Skip(1).Take(1).Concat(scenarioRows.Take(1)).Concat(scenarioRows.Skip(2)).ToList(), bases));

        // Row probes re-serialize with JSON.stringify-equivalent text, so untampered hashes survive and only the edited claim differs.
        const int probeBase = 1;
        using (var roundTrip = JsonDocument.Parse(Ordered(Graph(baseRows[probeBase]))))
        { var element = roundTrip.RootElement; Control("base ordered round trip keeps every hash", () => ValidateMemoryBase(element, probeBase, characters)); }
        void BaseProbe(string label, Action<Dictionary<string, object?>> change)
        {
            var copy = AsObject(Graph(baseRows[probeBase]), "probe base"); change(copy);
            using var edited = JsonDocument.Parse(Ordered(copy)); var element = edited.RootElement;
            MemoryProbe("base " + label, () => ValidateMemoryBase(element, probeBase, characters));
        }
        BaseProbe("prefix envelope omitted", copy => { var prefix = AsList(copy["prefix"], "prefix"); prefix.RemoveAt(prefix.Count - 1); });
        BaseProbe("terminal state edited without recomputed hashes", copy => { var run = AsObject(Get(Get(copy["terminal"], "state"), "run"), "terminal run"); run["coins"] = (long)run["coins"]! + 1; });
        BaseProbe("candidate scan seed changed", copy => AsObject(AsList(Get(copy["selection"], "candidates"), "candidates")[0], "candidate")["seed"] = 1103L);
        BaseProbe("facts party reordered", copy => AsList(AsObject(copy["facts"], "facts")["charIds"], "charIds").Reverse());

        int Ordinal(string id) => Array.FindIndex(MemoryScenarioPlan, s => s.Id == id);
        Dictionary<string, object?> OpOf(Dictionary<string, object?> copy, int index) => AsObject(AsList(copy["ops"], "ops")[index], "op");
        using (var roundTrip = JsonDocument.Parse(Ordered(Graph(scenarioRows[Ordinal("v2-clear-commit-replay")]))))
        { var element = roundTrip.RootElement; Control("scenario ordered round trip keeps every hash", () => ValidateMemoryScenario(element, Ordinal("v2-clear-commit-replay"), bases)); }
        void ScenarioProbe(string label, string id, Action<Dictionary<string, object?>> change)
        {
            int ordinal = Ordinal(id); var copy = AsObject(Graph(scenarioRows[ordinal]), "probe scenario"); change(copy);
            using var edited = JsonDocument.Parse(Ordered(copy)); var element = edited.RootElement;
            MemoryProbe("scenario " + label, () => ValidateMemoryScenario(element, ordinal, bases));
        }
        ScenarioProbe("before reference re-pointed", "legacy-clear-commit", copy => AsObject(OpOf(copy, 2)["before"], "before")["stateHash"] = zeros);
        ScenarioProbe("native TypeError reclassified as RuleError", "legacy-clear-prototype-key", copy =>
        {
            var op = OpOf(copy, 0); op["expect"] = O("kind", "RuleError", "code", "TYPE_ERROR");
            op["error"] = O("kind", "RuleError", "name", "RuleError", "code", "TYPE_ERROR", "message", "TYPE_ERROR", "detail", O("kind", "undefined"));
        });
        ScenarioProbe("live store laundered into a literal of the expected committed store", "v2-clear-commit-replay", copy =>
        {
            var memory = Get(Get(AsList(copy["points"], "points")[0], "state"), "memory");
            OpOf(copy, 1)["store"] = O("mode", "literal", "value", memory, "jsonHash", Sha(Ordered(memory)));
        });
        ScenarioProbe("returned commit relabeled rejected", "v2-clear-commit-replay", copy => OpOf(copy, 0)["outcome"] = "rejected");
        ScenarioProbe("unconditional commit laundered into NOT_COVERED", "legacy-clear-commit", copy =>
            AsList(copy["ops"], "ops")[1] = O("index", 1L, "label", "commit-valid", "method", "commitMemory", "status", "NOT_COVERED", "reason", NoOverCapacity));
        foreach (var sc in scenarios)
        {
            int uncoveredAt = sc.Ops.FindIndex(o => Text(o, "status") == "NOT_COVERED");
            if (uncoveredAt < 0) continue;
            ScenarioProbe("NOT_COVERED reason rewritten", sc.Id, copy => OpOf(copy, uncoveredAt)["reason"] = "Skipped for convenience");
            break;
        }
        var coverage = fixture.GetProperty("coverage");
        Control("coverage recomputation", () => ValidateMemoryCoverage(coverage, bases, scenarios));
        MemoryProbe("coverage claims one more returned op", () => ValidateMemoryCoverage(Element(Edit(coverage, "totals", Edit(coverage.GetProperty("totals"), "returned", (long)MemoryReturned + 1))), bases, scenarios));

        // Self-consistent forgeries: every hash/reference naming the edit is recomputed, so structure accepts it and only actual C# execution can
        // reject it, after matching every earlier op and exactly at the forged one.
        void Forged(string label, string id, string opLabel, Action<Dictionary<string, object?>, int> forge)
        {
            int ordinal = Ordinal(id), index = Array.FindIndex(MemoryScenarioPlan[ordinal].Ops, o => o.Label == opLabel);
            var copy = AsObject(Graph(scenarioRows[ordinal]), "forged scenario"); forge(copy, index);
            using var forgedDocument = JsonDocument.Parse(Ordered(copy)); var element = forgedDocument.RootElement;
            MemoryScenarioRecord? parsed = null; int reached = -2;
            Control("self-consistent forged " + label + " passes structural validation", () => parsed = ValidateMemoryScenario(element, ordinal, bases));
            if (parsed is not MemoryScenarioRecord candidate) { Check(false, "self-consistent forged " + label + " could not be parsed for actual execution"); return; }
            MemoryProbe("self-consistent forged " + label + " is rejected by actual C# execution", () => reached = ExecuteMemoryScenario(candidate, data, definitions));
            Check(reached == index, "actual C# matched every op before the forged " + label + " and diverged exactly there (reached " + I(reached) + ", forged #" + I(index) + ")");
        }
        void ForgeResult(Dictionary<string, object?> copy, int index, Action<Dictionary<string, object?>> edit)
        {
            var result = AsObject(OpOf(copy, index)["result"], "forged result"); var value = AsObject(result["value"], "forged return");
            edit(value); result["jsonHash"] = Sha(Ordered(value));
        }
        Forged("fresh legacy memoryChanges return", "legacy-fresh", "changes-fresh", (copy, index) => ForgeResult(copy, index, value =>
        {
            var first = AsObject(value.Values.First(), "first character"); first["capacity"] = Convert.ToInt64(first["capacity"], CultureInfo.InvariantCulture) + 1;
        }));
        Forged("terminal v2 preview return", "v2-clear-previews", "preview-empty", (copy, index) => ForgeResult(copy, index, value => value["error"] = "OVER_CAPACITY"));
        Forged("committed legacy point", "legacy-clear-commit", "commit-valid", (copy, index) =>
        {
            int point = (int)(Get(OpOf(copy, index)["after"], "ref") as long? ?? -1);
            var record = AsObject(AsList(copy["points"], "points")[point], "forged point"); var state = AsObject(record["state"], "forged state");
            var run = AsObject(state["run"], "forged run"); run["coins"] = (long)run["coins"]! + 1;
            string stateHash = CanonicalState.Hash(state), jsonHash = Sha(Ordered(state)); record["stateHash"] = stateHash; record["jsonHash"] = jsonHash;
            foreach (object? item in AsList(copy["ops"], "ops"))
                foreach (string field in new[] { "before", "after" })
                    if (Get(item, field) is Dictionary<string, object?> reference && Get(reference, "ref") is long at && at == point) { reference["stateHash"] = stateHash; reference["jsonHash"] = jsonHash; }
        });
    }
}
