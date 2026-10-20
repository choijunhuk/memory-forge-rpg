// R1c §7/8/16.2/20.9 P1-1: actual C# Run.Create plus the supported public Run/battle commands, now including the node services
// rest/forgeOffer/forgeApply/forgeRemove/buy/eventChoose, against the five-case JS full-state oracle and the supported prefixes of the 1000
// legacy traces. Expected states are comparison data only: nothing is injected into Run, the v2 store is the real M0 migration, and every
// case now executes actual commitMemory and derives fresh reload input from its C# saved result. Unfinished-Run restoration is separate.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoryForge.Core;
using ExpeditionRun = MemoryForge.Core.Run;

static partial class RunProgressionChecks
{
    const string Schema = "mf-run-progression-v1", Status = "JS_ORACLE_ONLY", TraceSchema = "mf-run-traces-v1";
    const string Scope = "Actual Run.create and the recorded public Run/battle commands of seven fixed gen_traces scenarios: complete JS states, returns, errors and pure queries. C# Run command replay 0; restoration, abandonment, persistence, server, Unity and device acceptance PENDING";
    // Independent literals from the unchanged sources: all commands through each terminal commit, battle attempts and battle commits among them,
    // committed/rejected attempts, the recorded pure queries inside those prefixes including each Create boundary, and the JS-only suffix.
    const int PrefixAttempts = 1621, PrefixBattle = 1302, PrefixBattleCommitted = 978, PrefixQueries = 1261, PrefixCommitted = 1291, PrefixRejected = 330, Suffix = 0;
    // Committed legacy forgeOffer attempts: each return gets one value/ownership probe (the five full-state cases never call forgeOffer).
    const long LegacyAttempts = 202224, LegacyOfferReturns = 595; // RULES_NOW_PLAY §7 gauge 20/50 regeneration of the 1000-trace corpus
    // Exact allowlist in source order, compared before any file is hashed: a fixture can never steer the host to read another path.
    static readonly (string Key, string Path)[] Sources = { ("data", "data/game_data.json"), ("engine", "engine/rules-core.js"), ("memory", "proposals/memory_v2.cjs"),
        ("canonical", "tools/gen_traces.mjs"), ("fuzzPolicy", "engine/tests/fuzz_runs.mjs"), ("runCreateDecoder", "tools/gen_run_create_vectors.mjs"), ("generator", "tools/gen_run_progression_vectors.mjs") };
    static readonly Dictionary<string, object?> Contract = O(
        "context", "Each full point is {run: Run.s, memory: v2 store or null} as ordered JSON including log, ev, battle and RNG. stateHash is the gen_traces canonical hash (log/ev omitted) and equals the trace hash; jsonHash is SHA-256 of the ordered JSON text; undefinedPaths lists own members or array items holding undefined (JSON omits the member or writes null)",
        "steps", "Trace commands applied from a fresh Run.create through public Run methods or run.battleCmd; a v2 commitMemory return supplies the next store. before references the current point (ref -1 = created, else steps[ref].after); committed steps carry the full after point; rejected steps reference the unchanged point, asserted JSON-identical",
        "results", "Return captured at call time: {kind:\"undefined\"} or {kind:\"json\",value,jsonHash,undefinedPaths}. Rejected steps have result null and error {kind:\"RuleError\",name,code,message,detail}; only the RuleError code the trace recorded is accepted and any other exception aborts generation",
        "queries", "available(), nodeById(pos, or the first floor-0 node while pos is null) and, during battle, battle.publicView() after Create, after every committed Run command and after each battle command that starts a turn or ends the battle; each is asserted to leave the complete JSON and undefinedPaths unchanged",
        "visibility", "Private test oracle: full states expose draw order and RNG. battle.publicView() is the actual source API return and is not certified here as a safe client view",
        "runtimeOnly", "Battle receipts live outside Run.s and are not captured; battle responses are recorded as results",
        "reload", "gen_traces saveMemory rebuild check recorded as Run.create only: saved memory, seed+99, v2 reload runId and committed store version",
        "separate", "The existing 1000-trace hash corpus and the R0 factory/query matrix are separate evidence; this fixture does not claim full C# replay");
    // RULES_NOW_PLAY (2026-10-19): seed 1102 no longer ends a battle in begin (begin-defeat moved to 3561) and 2654 earns ev_rift once
    // (earned-ap-bonus moved to 2992); 1102 stays as full exploration. Gauge 20/50 (§7, 2026-10-20): 3561 no longer ends a battle in begin
    // and 2992 earns ev_rift once, so begin-defeat moved to 4488 and earned-ap-bonus to 5748.
    static readonly Dictionary<string, object?> Denominators = O("cases", 7L, "seeds", L(1000L, 1102L, 4488L, 5748L), "policies", O("legacy", 5L, "v2", 2L), "lengths", O("short", 1L, "full", 6L));
    static readonly Dictionary<string, object?> Totals = O("cases", 7L, "attempted", 1621L, "committed", 1291L, "rejected", 330L, "queries", 1261L, "csharpCommandReplay", 0L);
    // Per case before Stop: attempts, battle attempts, battle commits, pure queries (Create included), committed and rejected attempts.
    static readonly (string Id, long Seed, string Length, string Policy, string Role, int Prefix, int Battle, int BattleCommitted, int Queries, int Committed, int Rejected, string Stop)[] Plan =
    {
        ("seed1000-short-legacy", 1000, "short", "legacy", "ordinary-exploration", 160, 132, 105, 115, 133, 27, "DONE"),
        ("seed1000-full-legacy", 1000, "full", "legacy", "ordinary-exploration", 161, 132, 105, 117, 134, 27, "DONE"),
        ("seed1102-full-legacy", 1102, "full", "legacy", "ordinary-exploration", 253, 194, 162, 226, 220, 33, "DONE"),
        ("seed1102-full-v2", 1102, "full", "v2", "ordinary-exploration", 253, 194, 162, 226, 220, 33, "DONE"),
        ("seed4488-full-legacy", 4488, "full", "legacy", "begin-defeat", 280, 236, 151, 188, 193, 87, "DONE"),
        ("seed4488-full-v2", 4488, "full", "v2", "begin-defeat", 280, 236, 151, 188, 193, 87, "DONE"),
        ("seed5748-full-legacy", 5748, "full", "legacy", "earned-ap-bonus", 234, 178, 142, 201, 198, 36, "DONE"),
    };
    // R1a commands plus the R1c actual terminal saving; commitMemory alone still stops a case before it is attempted. Assign is not in the
    // gen_traces vocabulary, so this corpus neither calls nor counts it.
    static readonly HashSet<string> Services = new(StringComparer.Ordinal) { "rest", "forgeOffer", "forgeApply", "forgeRemove", "buy", "eventChoose" };
    static readonly HashSet<string> Implemented = new(new[] { "enter", "evolve", "takeCard", "skipCard", "takeRelic", "takePart", "applyFoundPart", "proceed", "archive", "commitMemory" }.Concat(Services), StringComparer.Ordinal);
    static readonly HashSet<string> Pending = new(StringComparer.Ordinal);
    static readonly Dictionary<string, long> LegacyMethods = new(StringComparer.Ordinal)
    {
        ["enter"] = 9622, ["play"] = 138590, ["end"] = 19519, ["takeCard"] = 5166, ["proceed"] = 9601, ["takePart"] = 1272, ["eventChoose"] = 584,
        ["skipCard"] = 1730, ["applyFoundPart"] = 1224, ["forgeRemove"] = 390, ["takeRelic"] = 1750, ["ult"] = 7095, ["forgeOffer"] = 595,
        ["forgeApply"] = 595, ["rest"] = 663, ["buy"] = 1524, ["evolve"] = 1005, ["archive"] = 299, ["commitMemory"] = 1000
    };
    static readonly string[] TopFields = { "schema", "status", "scope", "dataVersion", "sources", "contract", "denominators", "coverage", "cases" };
    static readonly string[] CaseFields = { "id", "seed", "length", "memoryPolicy", "role", "trace", "created", "steps", "reload" };
    static readonly string[] TraceFields = { "schema", "policy", "setup", "initialStateHash", "finalStateHash", "commandCount", "summary" };
    static readonly string[] SetupFields = { "party", "seed", "length", "memoryPolicy", "memory", "relics", "difficulty", "zones" };
    static readonly string[] SummaryFields = { "outcome", "committed", "rejected", "visitedZones" };
    static readonly string[] PointFields = { "state", "stateHash", "jsonHash", "undefinedPaths" };
    static readonly string[] CreatedFields = { "state", "stateHash", "jsonHash", "undefinedPaths", "queries" };
    static readonly string[] StepFields = { "index", "command", "outcome", "errorCode", "error", "result", "before", "after", "queries" };
    static readonly string[] RefFields = { "ref", "stateHash", "jsonHash" };
    static readonly string[] ErrorFields = { "kind", "name", "code", "message", "detail" };
    static readonly string[] QueryFields = { "method", "args", "result" };
    static readonly string[] ResultFields = { "kind", "value", "jsonHash", "undefinedPaths" };
    static readonly string[] RunCommandFields = { "scope", "kind", "args" }, BattleCommandFields = { "scope", "rid", "v", "kind", "args" };
    static readonly object Undefined = new object();

    sealed class Divergence : Exception { public Divergence(string message) : base(message) { } }
    sealed class Case
    {
        public string Id = "", Policy = "", Stop = "";
        public JsonElement Row;
        // Prefix-only tallies: attempts before Stop, battle attempts/commits, queries, committed and rejected attempts.
        public int Prefix, Battle, BattleCommitted, PrefixQueries, Committed, Rejected;
        // Steps[i] and the point step i resolves to: its own committed after point, or the unchanged point a rejection references.
        public readonly List<JsonElement> Steps = new(), Points = new();
    }
    // gen_run_progression summarize(), recomputed only from recorded steps.
    sealed class Coverage
    {
        public readonly Dictionary<string, object?> Methods = new(StringComparer.Ordinal), Rejections = new(StringComparer.Ordinal), Phases = new(StringComparer.Ordinal),
            QueryCounts = new(StringComparer.Ordinal), Outcomes = new(StringComparer.Ordinal);
        public readonly List<object?> PerCase = new(), Begin = new(), Earned = new(), Consumed = new();
        public static void Add(Dictionary<string, object?> map, string key) => map[key] = (map.GetValueOrDefault(key) as long? ?? 0) + 1;
        public void Method(string key, string outcome)
        {
            if (Methods.GetValueOrDefault(key) is not Dictionary<string, object?> tally) Methods[key] = tally = O("attempted", 0L, "committed", 0L, "rejected", 0L);
            tally["attempted"] = (tally["attempted"] as long? ?? 0) + 1; tally[outcome] = (tally[outcome] as long? ?? 0) + 1;
        }
        public void AddCase(string id, long attempted, long committed, long rejected, long battle, long maxTurn, Dictionary<string, object?> state)
        {
            var run = AsObject(state.GetValueOrDefault("run"), id + " final run"); var result = AsObject(run.GetValueOrDefault("result"), id + " final result");
            Add(Outcomes, result.GetValueOrDefault("outcome") as string ?? "");
            var entry = O("id", id, "attempted", attempted, "committed", committed, "rejected", rejected, "battleCommands", battle, "finalPhase", run.GetValueOrDefault("phase"), "outcome", result.GetValueOrDefault("outcome"));
            if (result.TryGetValue("floor", out var floor)) entry["floor"] = floor;
            entry["zonesVisited"] = (run.GetValueOrDefault("zoneIdx") as long? ?? 0) + 1; entry["battles"] = Get(run.GetValueOrDefault("stats"), "battles"); entry["maxBattleTurn"] = maxTurn;
            entry["storeVersion"] = state.GetValueOrDefault("memory") is Dictionary<string, object?> memory ? memory.GetValueOrDefault("version") : null;
            PerCase.Add(entry);
        }
        public Dictionary<string, object?> ToGraph()
        {
            long Sum(string field) => PerCase.Sum(entry => Get(entry, field) as long? ?? 0);
            return O("totals", O("cases", (long)PerCase.Count, "attempted", Sum("attempted"), "committed", Sum("committed"), "rejected", Sum("rejected"), "queries", QueryCounts.Values.Sum(n => n as long? ?? 0), "csharpCommandReplay", 0L),
                "perCase", PerCase, "methods", Methods, "rejections", Rejections, "phases", Phases, "queries", QueryCounts, "terminalOutcomes", Outcomes,
                "events", O("beginEndedBattles", Begin, "apBonusEarned", Earned, "apBonusConsumed", Consumed));
        }
    }

    static int checks, failures, probes, probeFailures, hashedPoints, attempted, matched, matchedCommitted, matchedRejected, matchedBattle, matchedBattleCommitted,
        askedQueries, stateComparisons, detachedProbes, pendingProbes, prefixQueries, suffix, legacyMismatches;
    static long legacyScenarios, legacyMatched, legacyCommitted, legacyRejected, legacyPrefixes, battlesStarted, battlesFinished, archives, legacyOfferProbes;
    static readonly Dictionary<string, long> legacyDenominators = new(StringComparer.Ordinal), legacyExecuted = new(StringComparer.Ordinal), legacyStops = new(StringComparer.Ordinal),
        oracleExecuted = new(StringComparer.Ordinal);
    static readonly SortedSet<string> pendingPhases = new(StringComparer.Ordinal);
    static readonly List<string> stops = new();
    static int completedCases, actualReloads;
    static long legacySaveProbes;
    static List<string>? seed1000;
    static string fixtureSummary = "NOT_COMPUTED";
    static bool probing;

    static void Check(bool ok, string label)
    {
        if (probing) { if (!ok) probeFailures++; return; }
        checks++; if (!ok) { failures++; Console.WriteLine("FAIL RunProgression " + label); }
    }
    static void Require(bool ok, string label) { if (!ok) throw new InvalidDataException(label); }
    static void Count(ref int counter) { if (!probing) counter++; }
    static int Failed() => probing ? probeFailures : failures;
    // One failure per mismatching legacy scenario; details print for the first 20 so a systematic Core gap stays readable.
    static void LegacyMismatch(string message) { checks++; failures++; if (++legacyMismatches <= 20) Console.WriteLine("FAIL RunProgression legacy " + message); }

    public static int Run(string root)
    {
        checks = failures = probes = probeFailures = hashedPoints = attempted = matched = matchedCommitted = matchedRejected = matchedBattle = matchedBattleCommitted = 0;
        askedQueries = stateComparisons = detachedProbes = pendingProbes = prefixQueries = suffix = legacyMismatches = 0;
        legacyScenarios = legacyMatched = legacyCommitted = legacyRejected = legacyPrefixes = battlesStarted = battlesFinished = archives = legacyOfferProbes = 0;
        legacyDenominators.Clear(); legacyExecuted.Clear(); legacyStops.Clear(); oracleExecuted.Clear(); pendingPhases.Clear(); stops.Clear(); seed1000 = null; completedCases = actualReloads = 0; legacySaveProbes = 0; probing = false; fixtureSummary = "NOT_COMPUTED";
        RunData data; string definitions;
        try { data = RunDataLoader.Load(root); definitions = RunCreateChecks.DataPrint(data); }
        catch (Exception error) { Check(false, "source definitions: " + error); return Report(); }
        Section("full-state oracle", () => Oracle(root, data, definitions));
        Section("legacy trace prefixes", () => Legacy(root, data, definitions));
        Section("public Run memory boundaries", () => MemoryBoundary(root, data, definitions));
        return Report();
    }

    static int Report()
    {
        Console.WriteLine($"RunProgressionChecks: {checks - failures}/{checks} passed");
        Console.WriteLine("RunProgressionFixture: " + fixtureSummary);
        Console.WriteLine($"RunProgressionCore: R1c actual C# matched {matched}/{PrefixAttempts} complete attempts (battle, node services and terminal memory commit) from fresh Create ({attempted} attempted; "
            + $"committed {matchedCommitted}/{PrefixCommitted}, rejected {matchedRejected}/{PrefixRejected}; battle attempts {matchedBattle}/{PrefixBattle}, battle commits {matchedBattleCommitted}/{PrefixBattleCommitted}; {Join(oracleExecuted)}), "
            + $"{askedQueries}/{PrefixQueries} pure queries, {stateComparisons} full-state comparisons, {detachedProbes} detached return/snapshot mutation probes ({pendingProbes} at pending dictionaries in {(pendingPhases.Count == 0 ? "none" : string.Join("/", pendingPhases))})");
        Console.WriteLine("RunProgressionStops: " + (stops.Count == 0 ? "none executed" : string.Join("; ", stops)));
        Console.WriteLine($"RunProgressionScope: actual full source cases completed {completedCases}/5; actual save-derived fresh reloads {actualReloads}/5; JS-only suffix {suffix}; "
            + "Assign 0 attempts in this host (separate Unity evidence); RuleError name/message/detail unclaimed (code only); raw unfinished-Run restoration/server/device acceptance remain separate");
        Console.WriteLine($"RunProgressionGuards: {probes} fixture corruption/forgery probes");
        Console.WriteLine($"RunTracePrefixReplay: legacy scenarios {legacyScenarios}/1000; actual C# matched supported attempts {legacyMatched}/{LegacyAttempts} ({legacyCommitted} committed, {legacyRejected} rejected) in {legacyPrefixes}/1000 complete prefixes ({Join(legacyExecuted)}); "
            + $"battles started {battlesStarted}, finished {battlesFinished}; archives {archives}; forgeOffer return value/ownership probes {legacyOfferProbes}/{LegacyOfferReturns}; sourceTerminals {{{Join(legacyStops)}}}; full completions {legacyPrefixes}/1000; save ownership probes {legacySaveProbes}/1000" + (legacyMismatches > 20 ? $"; {legacyMismatches - 20} further mismatch details suppressed" : ""));
        return failures;
    }

    static void Section(string label, Action action)
    {
        try { action(); }
        catch (Exception error) { probing = false; Check(false, label + ": " + (error is InvalidDataException ? error.Message : error.ToString())); }
    }
    // Negative probes: a probe passes only when validation records a Check failure or raises InvalidDataException; counters stay guarded.
    static bool Rejects(Action validate)
    {
        probing = true; probeFailures = 0;
        try { validate(); return probeFailures > 0; }
        catch (InvalidDataException) { return true; }
        finally { probing = false; }
    }
    static void Probe(string label, Action validate) { probes++; Check(Rejects(validate), "corruption rejected: " + label); }
    static void Control(string label, Action validate) => Check(!Rejects(validate), "untampered control accepted: " + label);

    // ───────── five-case full-state oracle ─────────
    static void Oracle(string root, RunData data, string definitions)
    {
        using var document = RunCreateChecks.Decode(File.ReadAllBytes(Path.Combine(root, "engine", "tests", "vectors", "run_progression_vectors.json.gz")));
        using var content = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
        var fixture = document.RootElement; RunCreateChecks.UniqueKeys(fixture);
        string version = Text(content.RootElement.GetProperty("meta"), "version");
        ValidateHeader(fixture, root, version);
        var rows = Items(fixture.GetProperty("cases"), "Run-progression cases");
        Require(rows.Count == Plan.Length, "Run-progression case count differs from the seven fixed identities");
        var coverage = new Coverage(); var cases = new List<Case>();
        for (int i = 0; i < rows.Count; i++) cases.Add(ValidateCase(rows[i], i, coverage));
        var recorded = Graph(fixture.GetProperty("coverage"));
        string? drift = MemoryV2DataLoader.Difference(recorded, coverage.ToGraph());
        Check(drift == null, "fixture coverage/count summary equals the recomputation from all recorded steps" + (drift == null ? "" : ": " + drift));
        Check(MemoryV2DataLoader.Difference(Totals, Get(recorded, "totals")) == null, "fixture totals are the measured 7 cases/1899 attempts (1501 committed, 398 rejected)/1400 queries/C# replay 0");
        foreach (var plan in Plan)
        {
            Func<object?, bool> mine = e => Get(e, "caseId") as string == plan.Id;
            if (plan.Role == "begin-defeat") Check(coverage.Begin.Any(e => mine(e) && Get(e, "phase") as string == "DEFEAT"), plan.Id + " records the begin-defeat its role names");
            if (plan.Role == "earned-ap-bonus") Check(coverage.Earned.Count(mine) == 2 && coverage.Consumed.Count(mine) == 2, plan.Id + " records two earned and two consumed AP bonuses");
        }
        Check(cases.Select(c => { var policy = AsObject(Graph(c.Row.GetProperty("trace").GetProperty("policy")), c.Id + " policy"); policy.Remove("seed"); return CanonicalState.Serialize(policy); }).Distinct().Count() == 1,
            "all five traces share one generator policy apart from the derived seed");
        seed1000 = Envelopes(cases[0]);
        int prefix = cases.Sum(c => c.Prefix), battle = cases.Sum(c => c.Battle); suffix = cases.Sum(c => c.Steps.Count - c.Prefix); prefixQueries = cases.Sum(c => c.PrefixQueries);
        Check(prefix == PrefixAttempts && battle == PrefixBattle && cases.Sum(c => c.BattleCommitted) == PrefixBattleCommitted && prefixQueries == PrefixQueries
            && cases.Sum(c => c.Committed) == PrefixCommitted && cases.Sum(c => c.Rejected) == PrefixRejected,
            "source prefixes total 1899 attempts (1563 battle, 1165 battle commits; 1501 committed, 398 rejected) and 1400 queries through terminal commitMemory");
        Check(suffix == Suffix && cases.All(c => c.Steps.Count == c.Prefix), "all five source cases execute every command, including commitMemory; JS-only suffix 0");
        fixtureSummary = $"strict one-member gzip/fatal UTF-8/unique keys; exact schema/status {Status}/scope/7 allowlisted source pins/contract/denominators; 5 fixed cases; all {I(cases.Sum(c => c.Steps.Count))} records integrity-checked "
            + $"({I(hashedPoints)} points hashed; coverage recomputed); source prefixes {I(prefix)} attempts ({I(battle)} battle), {I(prefixQueries)} queries; JS-only suffix {I(suffix)} — oracle structure only, not C# execution";
        foreach (var c in cases)
        {
            try { Execute(c, data, definitions); }
            catch (Exception error) { Check(false, c.Id + " C# execution: " + (error is InvalidDataException ? error.Message : error.ToString())); }
        }
        Check(attempted == PrefixAttempts && matched == PrefixAttempts && matchedBattle == PrefixBattle && matchedBattleCommitted == PrefixBattleCommitted
            && matchedCommitted == PrefixCommitted && matchedRejected == PrefixRejected,
            "actual C# commands incl. R1c actual terminal saving from fresh Create matched all 1899 complete attempts (1501 committed, 398 rejected; 1563 battle attempts, 1165 battle commits)");
        Check(askedQueries == PrefixQueries, "every recorded pure query inside the supported prefixes was asked of the C# Run (" + I(askedQueries) + "/" + I(PrefixQueries) + ")");
        Check(completedCases == Plan.Length && actualReloads == Plan.Length, "all seven actual full source cases save and restart from their own returned memory");
        Section("progression fixture corruption", () => FixtureProbes(fixture, root, version, cases, data, definitions));
    }

    static void ValidateHeader(JsonElement fixture, string root, string version)
    {
        Fields(fixture, TopFields, "Run-progression top level");
        Require(Text(fixture, "schema") == Schema && Text(fixture, "status") == Status && Text(fixture, "scope") == Scope, "Wrong run-progression schema/status/scope (JS_ORACLE_ONLY, C# replay 0)");
        Require(Text(fixture, "dataVersion") == version, "Run-progression data version differs from data/game_data.json");
        var sources = fixture.GetProperty("sources"); Fields(sources, Sources.Select(s => s.Key).ToArray(), "Run-progression source allowlist");
        // Every recorded path must equal the allowlist before any file is opened; only allowlisted constants are ever hashed.
        foreach (var (key, path) in Sources)
        {
            var record = sources.GetProperty(key); Fields(record, new[] { "path", "sha256" }, "Run-progression source " + key);
            Require(Text(record, "path") == path, "Substituted run-progression source path: " + key); HashText(record.GetProperty("sha256"));
        }
        foreach (var (key, path) in Sources) Require(HashText(sources.GetProperty(key).GetProperty("sha256")) == FileHash(Path.Combine(root, path)), "Stale run-progression source: " + key);
        var contract = fixture.GetProperty("contract"); Fields(contract, Contract.Keys.ToArray(), "Run-progression contract");
        Require(MemoryV2DataLoader.Difference(Contract, Graph(contract)) == null, "Run-progression contract claims changed");
        Require(MemoryV2DataLoader.Difference(Denominators, Graph(fixture.GetProperty("denominators"))) == null, "Run-progression denominators changed");
    }

    static Case ValidateCase(JsonElement row, int ordinal, Coverage coverage)
    {
        var plan = Plan[ordinal];
        Fields(row, CaseFields, "Run-progression case " + I(ordinal));
        string id = Text(row, "id");
        Require(id == plan.Id && Integer(row.GetProperty("seed")) == plan.Seed && Text(row, "length") == plan.Length && Text(row, "memoryPolicy") == plan.Policy && Text(row, "role") == plan.Role,
            "Run-progression case " + I(ordinal) + " is " + id + "; the fixed plan expects " + plan.Id + " (omitted, reordered or changed identity)");
        var trace = row.GetProperty("trace"); Fields(trace, TraceFields, id + " trace");
        var policy = trace.GetProperty("policy");
        Require(Text(trace, "schema") == TraceSchema && policy.ValueKind == JsonValueKind.Object && policy.TryGetProperty("seed", out var policySeed) && Integer(policySeed) == ((uint)plan.Seed ^ 0xc0decafeu),
            id + " trace schema/policy seed ((seed ^ 0xc0decafe) >>> 0)");
        var setup = trace.GetProperty("setup"); ValidateSetup(setup, ordinal, id);
        var c = new Case { Id = id, Policy = plan.Policy, Row = row };
        var created = row.GetProperty("created"); Fields(created, CreatedFields, id + " created");
        var current = Point(created, id + " created"); var run = AsObject(current["run"], id + " created run");
        Require(HashText(created.GetProperty("stateHash")) == HashText(trace.GetProperty("initialStateHash")), id + " created context hash differs from the trace initial hash");
        Require(run.GetValueOrDefault("phase") as string == "MAP" && run.ContainsKey("pos") && run["pos"] == null && run.ContainsKey("battle") && run["battle"] == null, id + " created point is not a fresh MAP state");
        c.PrefixQueries = ValidateQueries(created.GetProperty("queries"), current, id + " created", coverage);
        var steps = Items(row.GetProperty("steps"), id + " steps");
        Require(steps.Count == Integer(trace.GetProperty("commandCount")), id + " step omitted or added");
        JsonElement point = created; long reference = -1, committedCount = 0, rejectedCount = 0, battleCommands = 0, maxTurn = 0;
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i]; string where = id + "#" + I(i);
            Fields(step, StepFields, where);
            Require(Integer(step.GetProperty("index")) == i, where + " index");
            var (scope, kind, args) = Envelope(step.GetProperty("command"), run, where);
            var before = step.GetProperty("before"); Fields(before, RefFields, where + " before");
            Require(Integer(before.GetProperty("ref")) == reference && HashText(before.GetProperty("stateHash")) == HashText(point.GetProperty("stateHash")) && HashText(before.GetProperty("jsonHash")) == HashText(point.GetProperty("jsonHash")),
                where + " before reference differs from the current point");
            string outcome = Text(step, "outcome"), key = scope + ":" + kind;
            Require(outcome is "committed" or "rejected", where + " unknown outcome " + outcome);
            coverage.Method(key, outcome); Coverage.Add(coverage.Phases, run.GetValueOrDefault("phase") as string ?? "");
            bool inPrefix = c.Stop.Length == 0;
            if (inPrefix && scope == "run" && !Implemented.Contains(kind)) { c.Prefix = i; c.Stop = kind; inPrefix = false; }
            if (inPrefix && scope == "battle") c.Battle++;
            if (scope == "battle") battleCommands++;
            if (inPrefix && outcome == "committed") { c.Committed++; if (scope == "battle") c.BattleCommitted++; }
            else if (inPrefix) c.Rejected++;
            int asked;
            if (outcome == "committed")
            {
                Require(step.GetProperty("errorCode").ValueKind == JsonValueKind.Null && step.GetProperty("error").ValueKind == JsonValueKind.Null, where + " committed step carries an error");
                var result = step.GetProperty("result"); Result(result, where + " result");
                Require(Text(result, "kind") == ((scope == "battle" || kind is "forgeOffer" or "commitMemory") ? "json" : "undefined"), where + " return kind differs from the source method (battle/forgeOffer/commitMemory JSON, other Run methods undefined)");
                var after = step.GetProperty("after"); Fields(after, PointFields, where + " after");
                var next = Point(after, where); var nextRun = AsObject(next["run"], where + " run"); var nextBattle = nextRun.GetValueOrDefault("battle");
                // Queries follow every committed Run command and every battle command that starts a turn or ends the battle.
                bool boundary = scope == "run" || nextBattle == null || Get(nextBattle, "turn") as long? != Get(run.GetValueOrDefault("battle"), "turn") as long?;
                asked = ValidateQueries(step.GetProperty("queries"), boundary ? next : null, where, coverage);
                maxTurn = Math.Max(maxTurn, Get(nextBattle, "turn") as long? ?? 0);
                Events(coverage, id, i, kind, args, run, nextRun);
                current = next; run = nextRun; point = after; reference = i; committedCount++;
            }
            else
            {
                var errorCode = step.GetProperty("errorCode"); var error = step.GetProperty("error");
                Require(errorCode.ValueKind == JsonValueKind.String && Text(errorCode).Length != 0 && step.GetProperty("result").ValueKind == JsonValueKind.Null, where + " rejected step code/result");
                Fields(error, ErrorFields, where + " error");
                Require(Text(error, "kind") == "RuleError" && Text(error, "code") == Text(errorCode) && error.GetProperty("name").ValueKind == JsonValueKind.String && error.GetProperty("message").ValueKind == JsonValueKind.String,
                    where + " rejection must be the recorded RuleError");
                Result(error.GetProperty("detail"), where + " error detail");
                Require(step.GetProperty("after").GetRawText() == before.GetRawText(), where + " rejected step must reference the unchanged point");
                asked = ValidateQueries(step.GetProperty("queries"), null, where, coverage);
                Coverage.Add(coverage.Rejections, key + ":" + Text(errorCode)); rejectedCount++;
            }
            if (inPrefix) c.PrefixQueries += asked;
            c.Steps.Add(step); c.Points.Add(point);
        }
        if (c.Stop.Length == 0) { c.Prefix = c.Steps.Count; c.Stop = "DONE"; }
        Require(c.Stop == "DONE", id + " contains an unsupported command; completion cannot be claimed");
        Check(c.Prefix == plan.Prefix && c.Battle == plan.Battle && c.BattleCommitted == plan.BattleCommitted && c.PrefixQueries == plan.Queries && c.Committed == plan.Committed && c.Rejected == plan.Rejected && c.Stop == plan.Stop,
            id + " source prefix " + I(c.Prefix) + " attempts (" + I(c.Battle) + " battle/" + I(c.BattleCommitted) + " battle commits; " + I(c.Committed) + " committed/" + I(c.Rejected) + " rejected; " + I(c.PrefixQueries) + " queries) through " + c.Stop
            + " equals the independent literal " + I(plan.Prefix) + " (" + I(plan.Battle) + "/" + I(plan.BattleCommitted) + "; " + I(plan.Committed) + "/" + I(plan.Rejected) + "; " + I(plan.Queries) + ") before " + plan.Stop);
        Require(HashText(point.GetProperty("stateHash")) == HashText(trace.GetProperty("finalStateHash")) && run.GetValueOrDefault("phase") as string == "DONE", id + " final point is not the trace final DONE state");
        var summary = trace.GetProperty("summary"); Fields(summary, SummaryFields, id + " trace summary");
        var final = AsObject(run.GetValueOrDefault("result"), id + " final result");
        Require(Text(summary, "outcome") == final.GetValueOrDefault("outcome") as string && Integer(summary.GetProperty("committed")) == committedCount && Integer(summary.GetProperty("rejected")) == rejectedCount
            && Integer(summary.GetProperty("visitedZones")) == (run.GetValueOrDefault("zoneIdx") as long? ?? -1) + 1, id + " recorded totals differ from the trace summary");
        ValidateReload(row.GetProperty("reload"), setup, current, ordinal, id);
        coverage.AddCase(id, steps.Count, committedCount, rejectedCount, battleCommands, maxTurn, current);
        return c;
    }

    static void ValidateSetup(JsonElement setup, int ordinal, string id)
    {
        var plan = Plan[ordinal]; bool v2 = plan.Policy == "v2";
        Fields(setup, v2 ? SetupFields.Concat(new[] { "runId", "memoryVersion", "useMemory" }).ToArray() : SetupFields, id + " trace setup");
        var graph = AsObject(Graph(setup), id + " trace setup"); var party = AsList(graph["party"], id + " trace party");
        Require(party.Count == 3 && party.Select(p => Get(p, "charId") as string).Distinct().Count() == 3 && party.All(p => p is Dictionary<string, object?> member && member.Keys.SequenceEqual(new[] { "charId", "res", "weaponId", "refine" })),
            id + " trace party is not three distinct generated members");
        Require(graph["seed"] as long? == plan.Seed && graph["length"] as string == plan.Length && graph["memoryPolicy"] as string == plan.Policy, id + " trace setup seed/length/policy differ from the fixed identity");
        Require(MemoryV2DataLoader.Difference(O("relics", L(), "difficulty", O("hp", 100L, "dmg", 100L), "zones", plan.Length == "full" ? L(1L, 2L) : L(1L)),
            O("relics", graph["relics"], "difficulty", graph["difficulty"], "zones", graph["zones"])) == null, id + " trace setup relics/difficulty/zones differ from the gen_traces defaults");
        if (v2) Require(graph["runId"] as string == "trace-" + I(plan.Seed) + "-" + plan.Length + "-v2" && graph["memoryVersion"] as long? == 0 && graph["useMemory"] is true && graph["memory"] is Dictionary<string, object?>,
            id + " v2 setup runId/memoryVersion/useMemory/memory");
        else Require(graph["memory"] is Dictionary<string, object?> { Count: 0 }, id + " legacy setup memory is the empty generator default");
    }

    static void ValidateReload(JsonElement reload, JsonElement setup, Dictionary<string, object?> final, int ordinal, string id)
    {
        var plan = Plan[ordinal];
        Fields(reload, new[] { "setup", "created" }, id + " reload"); var created = reload.GetProperty("created"); Fields(created, PointFields, id + " reload created");
        Point(created, id + " reload created");
        // Validate the source reload record only here; Execute separately derives a fresh input from the actual C# saved memory.
        var expected = AsObject(Graph(setup), id + " setup"); var run = AsObject(final.GetValueOrDefault("run"), id + " final run");
        expected["memory"] = run.GetValueOrDefault("savedMemory"); expected["seed"] = (plan.Seed + 99) & 0xffffffffL;
        if (plan.Policy == "v2") { expected["runId"] = (expected.GetValueOrDefault("runId") as string) + "-reload"; expected["memoryVersion"] = Get(final.GetValueOrDefault("memory"), "version"); }
        string? diff = MemoryV2DataLoader.Difference(expected, Graph(reload.GetProperty("setup")));
        Require(diff == null, id + " reload setup is not the recorded saveMemory rebuild input" + (diff == null ? "" : ": " + diff));
    }

    static (string Scope, string Kind, List<object?> Args) Envelope(JsonElement command, Dictionary<string, object?> run, string label)
    {
        Require(command.ValueKind == JsonValueKind.Object, label + " command envelope");
        Require(command.TryGetProperty("scope", out var scopeJson), label + " command scope");
        string scope = Text(scopeJson);
        if (scope == "run")
        {
            Fields(command, RunCommandFields, label + " Run command");
            string kind = Text(command, "kind"); var args = AsList(Graph(command.GetProperty("args")), label + " Run arguments");
            Require(Implemented.Contains(kind) || Pending.Contains(kind), label + " unknown Run command " + kind);
            return (scope, kind, args);
        }
        Require(scope == "battle", label + " unknown command scope " + scope);
        Fields(command, BattleCommandFields, label + " battle command");
        string battleKind = Text(command, "kind"); long v = Integer(command.GetProperty("v"));
        Require(battleKind is "play" or "ult" or "end" && Text(command, "rid") == "r" + I(v) + "-" + battleKind && command.GetProperty("args").ValueKind == JsonValueKind.Object, label + " battle request envelope");
        Require(Get(run.GetValueOrDefault("battle"), "v") as long? == v, label + " battle envelope version differs from the recorded live battle");
        return (scope, battleKind, L());
    }

    static Dictionary<string, object?> Point(JsonElement point, string label)
    {
        var json = point.GetProperty("state"); var state = AsObject(Graph(json), label + " state");
        Require(state.Keys.SequenceEqual(new[] { "run", "memory" }) && state["run"] is Dictionary<string, object?> && state["memory"] is null or Dictionary<string, object?>, label + " point is not {run, memory}");
        Check(TryHash(state) == HashText(point.GetProperty("stateHash")), label + " canonical context hash");
        Check(Sha(json.GetRawText()) == HashText(point.GetProperty("jsonHash")), label + " ordered source JSON hash");
        Omissions(state, point.GetProperty("undefinedPaths"), label);
        Count(ref hashedPoints); return state;
    }

    static void Result(JsonElement result, string label)
    {
        Require(result.ValueKind == JsonValueKind.Object, label + " is not an encoded return");
        if (Text(result, "kind") == "undefined") { Fields(result, new[] { "kind" }, label); return; }
        Fields(result, ResultFields, label); Require(Text(result, "kind") == "json", label + " kind");
        var value = result.GetProperty("value");
        Check(Sha(value.GetRawText()) == HashText(result.GetProperty("jsonHash")), label + " ordered JSON return hash");
        Omissions(Graph(value), result.GetProperty("undefinedPaths"), label);
    }

    static int ValidateQueries(JsonElement list, Dictionary<string, object?>? state, string label, Coverage coverage)
    {
        var rows = Items(list, label + " queries"); var expected = new List<(string Method, List<object?> Args)>();
        if (state != null)
        {
            var run = AsObject(state.GetValueOrDefault("run"), label + " run"); var floors = AsList(Get(run.GetValueOrDefault("map"), "floors"), label + " map floors");
            if (floors.Count == 0 || floors[0] is not List<object?> first || first.Count == 0) throw new InvalidDataException(label + " has no floor-0 node");
            string node = run.GetValueOrDefault("pos") as string ?? Get(first[0], "id") as string ?? throw new InvalidDataException(label + " first floor-0 node id");
            expected.Add(("available", L())); expected.Add(("nodeById", L(node)));
            if (run.GetValueOrDefault("battle") != null) expected.Add(("battle.publicView", L()));
        }
        Require(rows.Count == expected.Count, label + " pure query boundary omits/adds queries");
        for (int i = 0; i < rows.Count; i++)
        {
            var query = rows[i]; string at = label + " query " + expected[i].Method; Fields(query, QueryFields, at);
            Require(Text(query, "method") == expected[i].Method && MemoryV2DataLoader.Difference(expected[i].Args, Graph(query.GetProperty("args"))) == null, at + " method/arguments");
            var result = query.GetProperty("result"); Result(result, at); Require(Text(result, "kind") == "json", at + " returns JSON");
            Coverage.Add(coverage.QueryCounts, expected[i].Method);
        }
        return rows.Count;
    }

    static void Events(Coverage coverage, string id, int index, string kind, List<object?> args, Dictionary<string, object?> before, Dictionary<string, object?> after)
    {
        long? was = ApBonus(before), now = ApBonus(after);
        if (kind == "eventChoose" && now > was)
            coverage.Earned.Add(O("caseId", id, "step", (long)index, "eventId", Get(before.GetValueOrDefault("pending"), "id"), "choice", args.Count != 0 ? args[0] : null, "nextApBonus", now));
        var node = kind == "enter" && args.Count != 0 ? NodeOf(before, args[0]) : null;
        if (node == null || node.GetValueOrDefault("type") is not ("battle" or "elite" or "boss")) return;
        if (was is long carried && carried != 0)
            coverage.Consumed.Add(O("caseId", id, "step", (long)index, "carried", carried, "firstTurnAp", after.GetValueOrDefault("battle") is Dictionary<string, object?> battle ? battle.GetValueOrDefault("ap") : null));
        if (after.GetValueOrDefault("phase") as string != "BATTLE")
            coverage.Begin.Add(O("caseId", id, "step", (long)index, "nodeId", node.GetValueOrDefault("id"), "type", node.GetValueOrDefault("type"), "phase", after.GetValueOrDefault("phase")));
    }
    static long? ApBonus(Dictionary<string, object?> run) => Get(run.GetValueOrDefault("runFlags"), "nextApBonus") as long?;
    static Dictionary<string, object?>? NodeOf(Dictionary<string, object?> run, object? id)
    {
        var map = run.GetValueOrDefault("map");
        foreach (object? floor in Get(map, "floors") as List<object?> ?? new List<object?>())
            foreach (object? node in floor as List<object?> ?? new List<object?>())
                if (node is Dictionary<string, object?> found && Equals(found.GetValueOrDefault("id"), id)) return found;
        return Get(map, "boss") is Dictionary<string, object?> boss && Equals(boss.GetValueOrDefault("id"), id) ? boss : null;
    }

    static List<string> Envelopes(Case c)
    {
        var trace = c.Row.GetProperty("trace");
        var list = new List<string> { CanonicalState.Serialize(Graph(trace.GetProperty("setup"))), CanonicalState.Serialize(Graph(trace.GetProperty("policy"))),
            HashText(trace.GetProperty("initialStateHash")), HashText(trace.GetProperty("finalStateHash")) };
        foreach (var step in c.Steps)
        {
            var code = step.GetProperty("errorCode");
            list.Add(CanonicalState.Serialize(Graph(step.GetProperty("command"))) + "|" + Text(step, "outcome") + "|" + (code.ValueKind == JsonValueKind.Null ? "null" : Text(code)) + "|" + HashText(step.GetProperty("after").GetProperty("stateHash")));
        }
        return list;
    }

    // Actual C#: fresh Create, real M0 store, every public command, and a new Create from the actual committed memory.
    // Returns the number of attempts matched before the first divergence (the forgery probes assert where it stops).
    static int Execute(Case c, RunData data, string definitions)
    {
        var setupJson = c.Row.GetProperty("trace").GetProperty("setup"); var created = c.Row.GetProperty("created");
        var setup = RunDataLoader.Setup(setupJson); string setupBefore = RunCreateChecks.SetupPrint(setup);
        var run = ExpeditionRun.Create(data, setup);
        Dictionary<string, object?>? store = null;
        if (c.Policy == "v2")
        {
            // gen_traces' v2 store is the real M0 migration of an empty account; the recorded store is comparison data only.
            store = MemoryV2.Migrate(data.Memory, new Dictionary<string, object?>(StringComparer.Ordinal), new Dictionary<string, object?>(StringComparer.Ordinal)).ToJsonGraph();
            Check(MemoryV2DataLoader.Difference(Graph(setupJson.GetProperty("memory")), store.GetValueOrDefault("memory")) == null
                && Integer(setupJson.GetProperty("memoryVersion")) == Convert.ToInt64(store.GetValueOrDefault("version"), CultureInfo.InvariantCulture), c.Id + " v2 setup memory/version are the real M0 empty-account migration");
        }
        var state = run.State; var graph = Context(state, store); string live = Full(graph);
        int failed = Failed(), executed = 0; string stopped = "through " + c.Stop;
        Compare(created, graph, c.Id + " created");
        Ask(created.GetProperty("queries"), run, store, live, c.Id + " created");
        // The first active battle, and each pending dictionary at its first (phase, command) pair, e.g. FORGE after forgeOffer or SHOP after buy.
        bool battleProbe = false; var pendingProbed = new HashSet<string>(StringComparer.Ordinal);
        if (Failed() != failed) stopped = "created mismatch";
        else for (int i = 0; i < c.Prefix; i++)
        {
            var step = c.Steps[i]; var command = step.GetProperty("command");
            string scope = Text(command, "scope"), kind = Text(command, "kind"), outcome = Text(step, "outcome"), where = c.Id + "#" + I(i) + " " + scope + ":" + kind;
            var errorCode = step.GetProperty("errorCode"); string? expectedCode = errorCode.ValueKind == JsonValueKind.Null ? null : Text(errorCode);
            failed = Failed(); Count(ref attempted);
            string? storeBefore = kind == "commitMemory" && c.Policy == "v2" ? Full(store) : null;
            object returned = Undefined; string? code = null;
            try
            {
                // Arguments are bound first, so host decoding errors can never pass as Core RuleErrors.
                var call = scope == "battle" ? BattleCall(run, state, kind, Text(command, "rid"), Integer(command.GetProperty("v")), command.GetProperty("args").GetRawText())
                    : RunCall(run, kind, AsList(Graph(command.GetProperty("args")), where + " args"), store);
                try { returned = call(); } catch (RuleException error) { code = error.Code; }
            }
            catch (Exception error) when (error is not InvalidDataException) { Check(false, where + " " + Describe(error)); stopped = "error at #" + I(i); break; }
            string actual = code == null ? "committed" : "rejected";
            Check(actual == outcome && code == expectedCode, where + " outcome/RuleError code (C# " + actual + (code == null ? "" : " " + code) + ", JS " + outcome + (expectedCode == null ? "" : " " + expectedCode) + ")");
            if (storeBefore != null) Check(Full(store) == storeBefore, where + " commit never mutates the caller's input store");
            if (actual == "committed" && kind == "commitMemory" && c.Policy == "v2")
                // Keep the application's actual returned store independently owned before mutating the return as a probe.
                store = AsObject(Graph(Element(Get(returned, "state"))), where + " actual committed store");
            var after = run.State; graph = Context(after, store); live = Full(graph);
            if (actual == "committed" && outcome == "committed") Return(step.GetProperty("result"), returned, run, store, live, where);
            Compare(c.Points[i], graph, where);
            Ask(step.GetProperty("queries"), run, store, live, where);
            if (Failed() == failed && !battleProbe && after.Battle != null) { battleProbe = true; Detachment(run, store, live, where + " active battle"); }
            if (Failed() == failed && after.Pending is IDictionary<string, object?> && pendingProbed.Add(after.Phase + " after " + kind))
            {
                Count(ref pendingProbes); if (!probing) pendingPhases.Add(after.Phase);
                Detachment(run, store, live, where + " pending " + after.Phase);
            }
            if (Failed() != failed) { stopped = "mismatch at #" + I(i); break; }
            executed++; Count(ref matched); if (!probing) Bump(oracleExecuted, kind);
            if (scope == "battle") { Count(ref matchedBattle); if (code == null) Count(ref matchedBattleCommitted); }
            if (code == null) Count(ref matchedCommitted); else Count(ref matchedRejected);
            state = after;
        }
        Check(executed == c.Prefix, c.Id + " actual C# matched every supported attempt before " + c.Stop + " (" + I(executed) + "/" + I(c.Prefix) + ", stopped " + stopped + ")");
        Check(battleProbe || executed != c.Prefix, c.Id + " detached State/public-view probes ran on an active battle");
        Check(RunCreateChecks.SetupPrint(setup) == setupBefore && RunCreateChecks.DataPrint(data) == definitions, c.Id + " Create/commands/queries preserve the caller setup and source definitions");
        if (!probing) stops.Add(c.Id + " " + I(executed) + "/" + I(c.Prefix) + " (" + I(c.Battle) + " battle) " + stopped);
        if (executed == c.Prefix && Failed() == failed)
        {
            Check(run.State.Phase == "DONE" && run.State.SavedMemory != null, c.Id + " actual terminal commit produced DONE and savedMemory");
            if (Failed() == failed) { Count(ref completedCases); ReloadActual(c, run, store, data, definitions); }
        }
        return executed;
    }

    // The original JS setup is input; its expected save is never used as C# input. Only actual Core output supplies memory/version.
    static void ReloadActual(Case c, ExpeditionRun finished, Dictionary<string, object?>? store, RunData data, string definitions)
    {
        var original = c.Row.GetProperty("trace").GetProperty("setup");
        var input = AsObject(Graph(original), c.Id + " original setup");
        input["memory"] = Graph(Element(finished.State.SavedMemory));
        input["seed"] = (Integer(original.GetProperty("seed")) + 99) & 0xffffffffL;
        if (c.Policy == "v2")
        {
            input["runId"] = (input.GetValueOrDefault("runId") as string) + "-reload";
            input["memoryVersion"] = store?.GetValueOrDefault("version");
        }
        var reload = c.Row.GetProperty("reload");
        int prior = Failed();
        Check(MemoryV2DataLoader.Difference(Graph(reload.GetProperty("setup")), input) == null, c.Id + " actual save-derived reload input equals the original JS reload input");
        var setup = RunDataLoader.Setup(Element(input)); string before = RunCreateChecks.SetupPrint(setup);
        var run = ExpeditionRun.Create(data, setup);
        Compare(reload.GetProperty("created"), Context(run.State, store), c.Id + " actual save-derived fresh Create");
        Check(RunCreateChecks.SetupPrint(setup) == before && RunCreateChecks.DataPrint(data) == definitions, c.Id + " reload keeps source/setup unchanged");
        if (Failed() == prior) Count(ref actualReloads);
    }

    static Func<object> RunCall(ExpeditionRun run, string kind, List<object?> args, object? currentMemory = null)
    {
        string Arg(int index, int count) => args.Count == count && args[index] is string text ? text : throw new InvalidDataException(kind + " expects " + I(count) + " string argument(s)");
        // A recorded trailing deck key: absent (JS undefined, passed as null) or exactly one more string argument.
        string? Key(int index) => args.Count == index ? null : Arg(index, index + 1);
        void None() { if (args.Count != 0) throw new InvalidDataException(kind + " expects no arguments"); }
        switch (kind)
        {
            case "enter": { string node = Arg(0, 1); return () => { run.Enter(node); return Undefined; }; }
            case "evolve": { string key = Arg(0, 2), branch = Arg(1, 2); return () => { run.Evolve(key, branch); return Undefined; }; }
            case "takeCard": { string card = Arg(0, 1); return () => { run.TakeCard(card); return Undefined; }; }
            case "skipCard": None(); return () => { run.SkipCard(); return Undefined; };
            case "takeRelic": { string relic = Arg(0, 1); return () => { run.TakeRelic(relic); return Undefined; }; }
            case "takePart": None(); return () => { run.TakePart(); return Undefined; };
            case "applyFoundPart": { string part = Arg(0, 2), key = Arg(1, 2); return () => { run.ApplyFoundPart(part, key); return Undefined; }; }
            case "proceed": None(); return () => { run.Proceed(); return Undefined; };
            case "archive": { string choice = Arg(0, 1); return () => { run.Archive(choice); return Undefined; }; }
            // R1c actual terminal saving in the gen_traces shapes only: rest(choice[, key]), buy(shelf, id) or buy(kind, null, key), eventChoose(index[, key]).
            case "rest": { string? card = Key(1); string choice = Arg(0, args.Count); return () => { run.Rest(choice, card); return Undefined; }; }
            case "forgeOffer": { string card = Arg(0, 1); return () => run.ForgeOffer(card); }
            case "forgeApply": { string part = Arg(0, 1); return () => { run.ForgeApply(part); return Undefined; }; }
            case "forgeRemove": { string card = Arg(0, 1); return () => { run.ForgeRemove(card); return Undefined; }; }
            case "buy":
                {
                    if (args.Count == 3 && args[1] == null) { string removal = Arg(0, 3), card = Arg(2, 3); return () => { run.Buy(removal, null, card); return Undefined; }; }
                    string shelf = Arg(0, 2), item = Arg(1, 2); return () => { run.Buy(shelf, item); return Undefined; };
                }
            case "eventChoose":
                {
                    string? card = Key(1);
                    int choice = args[0] is long index && index >= int.MinValue && index <= int.MaxValue ? (int)index : throw new InvalidDataException("eventChoose expects an integer choice index");
                    return () => { run.EventChoose(choice, card); return Undefined; };
                }
            case "commitMemory":
                if (run.State.MemoryPolicy == "v2")
                {
                    // The recorded second argument is comparison data. Pass the independently created/current actual Core store.
                    if (args.Count != 2 || MemoryV2DataLoader.Difference(args[1], currentMemory) != null)
                        throw new InvalidDataException("v2 commitMemory current-store argument differs from the actual store");
                }
                else if (args.Count != 1) throw new InvalidDataException("legacy commitMemory expects one selection object");
                return () => run.CommitMemory(args[0], currentMemory);
            default: throw new InvalidDataException("Completion host never calls unknown Run command " + kind);
        }
    }
    // The recorded rid/v must equal the live C# battle before the call (JS dispatch asserts the same); args use the existing transport parser.
    static Func<object> BattleCall(ExpeditionRun run, RunState current, string kind, string rid, long v, string args)
    {
        if (current.Battle == null || current.Battle.V != v || rid != "r" + I(v) + "-" + kind)
            throw new Divergence("recorded battle request " + rid + "/v" + I(v) + " does not match the live C# battle (" + (current.Battle == null ? "none" : "v" + I(current.Battle.V)) + ")");
        var request = BattleDataLoader.Request("{\"rid\":" + CanonicalState.Serialize(rid) + ",\"v\":" + I(v) + ",\"kind\":" + CanonicalState.Serialize(kind) + ",\"args\":" + args + "}");
        return () => run.BattleCommand(kind, request.Args);
    }
    static string Describe(Exception error) => error is MemoryBoundaryException boundary ? "MemoryBoundaryException (JS " + boundary.JavaScriptName + "): " + boundary.Message
        : error is Divergence ? error.Message : error.GetType().Name + ": " + error.Message;

    // Path-by-path complete comparison (C# dictionaries promise no JS insertion order) plus the canonical context hash and own-undefined omissions.
    static void Compare(JsonElement point, Dictionary<string, object?> actual, string label)
    {
        Count(ref stateComparisons);
        string? diff = MemoryV2DataLoader.Difference(Graph(point.GetProperty("state")), actual);
        Check(diff == null, label + " complete {run, memory} state incl. log/ev/battle/RNG/null/absence" + (diff == null ? "" : ": " + diff));
        Check(TryHash(actual) == HashText(point.GetProperty("stateHash")), label + " canonical context hash");
        foreach (object? path in AsList(Graph(point.GetProperty("undefinedPaths")), label)) Check(Omitted(actual, path), label + " JS own-undefined " + CanonicalState.Serialize(path) + " stays absent, never null");
    }

    static void Return(JsonElement result, object returned, ExpeditionRun run, object? store, string live, string label)
    {
        string kind = Text(result, "kind");
        // Graph returns compare to JS by value. Detachment deliberately strengthens C# ownership beyond the source JS live returns.
        if (returned is IDictionary<string, object?> offers)
        {
            Check(kind == "json", label + " graph returns are recorded as a JSON return");
            if (kind != "json") return;
            string? offerDiff = MemoryV2DataLoader.Difference(Graph(result.GetProperty("value")), offers);
            Check(offerDiff == null, label + " complete returned graph" + (offerDiff == null ? "" : ": " + offerDiff));
            foreach (object? path in AsList(Graph(result.GetProperty("undefinedPaths")), label)) Check(Omitted(offers, path), label + " graph return own-undefined " + CanonicalState.Serialize(path) + " stays absent, never null");
            string original = Full(offers); Scribble(offers); Count(ref detachedProbes);
            Check(Full(offers) != original, label + " positive control: caller mutation changes the returned graph");
            Check(Full(Context(run.State, store)) == live, label + " mutating the returned graph cannot reach the live pending or Run");
            return;
        }
        if (returned is not BattleResponse response) { Check(ReferenceEquals(returned, Undefined) && kind == "undefined", label + " void Run method returns JS undefined (never null/JSON)"); return; }
        Check(kind == "json", label + " battle response is recorded as a JSON return");
        if (kind != "json") return;
        var graph = response.ToJsonGraph(); string? diff = MemoryV2DataLoader.Difference(Graph(result.GetProperty("value")), graph);
        Check(diff == null, label + " complete returned battle response v/phase/ev incl. required nulls" + (diff == null ? "" : ": " + diff));
        foreach (object? path in AsList(Graph(result.GetProperty("undefinedPaths")), label)) Check(Omitted(graph, path), label + " response own-undefined " + CanonicalState.Serialize(path) + " stays absent, never null");
        response.V = -1; response.Phase = "caller-mutation"; if (response.Events.Count != 0) response.Events[0].E = "caller-mutation"; response.Events.Clear(); Count(ref detachedProbes);
        Check(Full(Context(run.State, store)) == live, label + " mutating the returned battle response cannot reach the live Run");
    }

    static void Ask(JsonElement list, ExpeditionRun run, object? store, string live, string label)
    {
        foreach (var query in list.EnumerateArray())
        {
            string method = Text(query, "method"), at = label + " " + method; var result = query.GetProperty("result");
            object? actual; Action vandalize;
            if (method == "available") { var ids = run.Available(); actual = ids; vandalize = () => ids.Add("caller-mutation"); }
            else if (method == "nodeById")
            {
                var node = run.NodeById(Text(query.GetProperty("args")[0])); actual = node?.ToJsonGraph();
                vandalize = () => { if (node != null) { node.Type = "caller-mutation"; node.Next?.Add("caller-mutation"); } };
            }
            else if (method == "battle.publicView") { var view = run.BattlePublicView(); actual = view; vandalize = () => Scribble(view); }
            else throw new InvalidDataException(at + " is not a recorded pure query");
            Count(ref askedQueries);
            string? diff = MemoryV2DataLoader.Difference(Graph(result.GetProperty("value")), actual);
            Check(diff == null, at + " exact pure query result" + (diff == null ? "" : ": " + diff));
            Omissions(actual, result.GetProperty("undefinedPaths"), at);
            vandalize();
            Check(Full(Context(run.State, store)) == live, at + " leaves the complete state/RNG/log unchanged and its mutated return stays detached");
        }
    }

    // Returned State snapshots and public views are detached copies: each mutation must change the copy (positive control) and never the live Run.
    static void Detachment(ExpeditionRun run, object? store, string live, string label)
    {
        var mutations = new (string Name, Func<RunState, bool> Mutate)[]
        {
            ("hp", s => { s.Hp += 7; return true; }),
            ("rng", s => { s.Rng[0] ^= 1; return true; }),
            ("log", s => { s.Log.Add("caller-mutation"); return true; }),
            ("deck", s => { if (s.Deck.Count == 0) return false; s.Deck[0].Key = "caller-mutation"; s.Deck.RemoveAt(s.Deck.Count - 1); return true; }),
            ("party/map/runFlags", s => { if (s.Party.Count == 0 || s.Map == null) return false; s.Party[0].Hp = -1; s.Map.Floors[0][0].Type = "caller-mutation"; s.RunFlags["caller-mutation"] = true; return true; }),
            ("pending", s => { if (s.Pending is not IDictionary<string, object?> pending) return false; Scribble(pending); return true; }),
            ("battle hp/rng/log", s => { if (s.Battle == null) return false; s.Battle.Hp += 7; s.Battle.Rng[0] ^= 1; s.Battle.Log.Add(new BattleEvent { E = "caller-mutation" }); return true; }),
            ("battle zones/cards", s => { if (s.Battle == null) return false; s.Battle.Zones.Hand.Add("caller-mutation"); foreach (var card in s.Battle.Cards.Values) card.Growth += 1; return true; }),
            ("battle enemies/actors/flags", s => { if (s.Battle == null || s.Battle.Enemies.Count == 0 || s.Battle.Actors.Count == 0) return false; s.Battle.Enemies[0].Hp += 1; s.Battle.Actors[0].Gauge += 1; s.Battle.RunFlags["caller-mutation"] = true; return true; }),
        };
        foreach (var (name, mutate) in mutations)
        {
            var snapshot = run.State; if (!mutate(snapshot)) continue;
            Count(ref detachedProbes);
            Check(Full(Context(snapshot, store)) != live, label + " positive control: " + name + " mutation changes the returned State");
            Check(Full(Context(run.State, store)) == live, label + " " + name + " mutation of a returned State cannot reach the live Run");
        }
        var view = run.BattlePublicView();
        if (view == null) return;
        string original = Full(view); Scribble(view); Count(ref detachedProbes);
        Check(Full(view) != original, label + " positive control: caller mutation changes the returned BattlePublicView");
        Check(Full(run.BattlePublicView()) == original && Full(Context(run.State, store)) == live, label + " mutating a returned BattlePublicView cannot reach the live battle or Run");
    }
    static void Scribble(object? node)
    {
        if (node is IDictionary<string, object?> fields) { foreach (object? value in fields.Values.ToList()) Scribble(value); fields["caller-mutation"] = true; }
        else if (node is IList list && !list.IsFixedSize) { foreach (object? item in list.Cast<object?>().ToList()) Scribble(item); list.Clear(); }
    }

    static void FixtureProbes(JsonElement fixture, string root, string version, List<Case> cases, RunData data, string definitions)
    {
        string zeros = new string('0', 64);
        // Header: cases/coverage are irrelevant to ValidateHeader, so probes stay small; each edits exactly one claim.
        var header = Shallow(fixture); header["coverage"] = O(); header["cases"] = L();
        Control("header", () => ValidateHeader(Element(header), root, version));
        void HeaderProbe(string label, string key, object? value, bool remove = false)
        {
            var bad = new Dictionary<string, object?>(header, StringComparer.Ordinal); if (remove) bad.Remove(key); else bad[key] = value;
            Probe("header " + label, () => ValidateHeader(Element(bad), root, version));
        }
        HeaderProbe("schema", "schema", "mf-run-progression-v0"); HeaderProbe("status claims C# replay", "status", "COMPLETE_CSHARP_REPLAY");
        HeaderProbe("scope claims C# replay", "scope", Scope.Replace("C# Run command replay 0", "C# Run command replay complete"));
        HeaderProbe("data version", "dataVersion", "0.0.0"); HeaderProbe("extra top-level field", "restoredState", true); HeaderProbe("missing contract", "contract", null, true);
        var reordered = new Dictionary<string, object?>(StringComparer.Ordinal) { ["status"] = header["status"] }; foreach (var pair in header) reordered.TryAdd(pair.Key, pair.Value);
        Probe("header top-level member order", () => ValidateHeader(Element(reordered), root, version));
        var sources = fixture.GetProperty("sources");
        // The substituted path does not exist: reading it before the allowlist check would surface as a different exception, not a rejection.
        HeaderProbe("substituted source path", "sources", Edit(sources, "data", Edit(sources.GetProperty("data"), "path", "../outside/game_data.json")));
        HeaderProbe("stale engine source", "sources", Edit(sources, "engine", Edit(sources.GetProperty("engine"), "sha256", zeros)));
        HeaderProbe("extra source", "sources", Edit(sources, "test", sources.GetProperty("engine")));
        HeaderProbe("missing generator source", "sources", Edit(sources, "generator", null, true));
        HeaderProbe("contract claims C# replay", "contract", Edit(fixture.GetProperty("contract"), "separate", "This fixture proves full C# replay"));
        HeaderProbe("denominators shortened", "denominators", Edit(fixture.GetProperty("denominators"), "cases", 4));

        // Case probes on the shortest case, re-serialized with JSON.stringify-equivalent text so untampered hashes survive the round trip.
        int smallest = Enumerable.Range(0, cases.Count).OrderBy(i => cases[i].Steps.Count).First(); var target = cases[smallest]; var row = target.Row;
        using (var document = JsonDocument.Parse(Ordered(Graph(row))))
        { var element = document.RootElement; Control("case ordered round trip keeps every hash", () => ValidateCase(element, smallest, new Coverage())); }
        void CaseProbe(string label, Action<Dictionary<string, object?>> change)
        {
            var copy = AsObject(Graph(row), "probe case"); change(copy);
            using var document = JsonDocument.Parse(Ordered(copy)); var element = document.RootElement;
            Probe("case " + label, () => ValidateCase(element, smallest, new Coverage()));
        }
        Dictionary<string, object?> Step(Dictionary<string, object?> copy, int index) => AsObject(AsList(copy["steps"], "steps")[index], "step");
        Dictionary<string, object?> AfterRun(Dictionary<string, object?> copy, int index) => AsObject(AsObject(AsObject(Step(copy, index)["after"], "after")["state"], "state")["run"], "run");
        int committedAt = target.Steps.FindIndex(s => Text(s, "outcome") == "committed"), rejectedAt = target.Steps.FindIndex(s => Text(s, "outcome") == "rejected"),
            battleAt = target.Steps.FindIndex(s => Text(s, "outcome") == "committed" && Text(s.GetProperty("command"), "scope") == "battle");
        Require(committedAt >= 0 && committedAt + 1 < target.Prefix && rejectedAt >= 0 && battleAt >= 0, target.Id + " lacks the committed/rejected/battle records the probes edit");
        CaseProbe("committed state edited without recomputed hashes", copy => { var run = AfterRun(copy, committedAt); run["coins"] = (long)run["coins"]! + 1; });
        CaseProbe("rejected step re-pointed to another state", copy => AsObject(Step(copy, rejectedAt)["after"], "after")["stateHash"] = zeros);
        CaseProbe("final step omitted", copy => { var steps = AsList(copy["steps"], "steps"); steps.RemoveAt(steps.Count - 1); });
        CaseProbe("created pure query omitted", copy => { var list = AsList(AsObject(copy["created"], "created")["queries"], "queries"); list.RemoveAt(list.Count - 1); });
        CaseProbe("present member claimed own-undefined", copy => AsList(AsObject(Step(copy, committedAt)["after"], "after")["undefinedPaths"], "paths").Add(L("run", "coins")));
        CaseProbe("battle response laundered into undefined", copy => Step(copy, battleAt)["result"] = O("kind", "undefined"));
        CaseProbe("battle envelope version drift", copy => { var command = AsObject(Step(copy, battleAt)["command"], "command"); long v = (long)command["v"]! + 1; command["v"] = v; command["rid"] = "r" + I(v) + "-" + command["kind"]; });

        // Self-consistent forgery: one committed state inside the supported prefix changes and every hash/reference naming it is recomputed,
        // so structure accepts it and only actual C# execution can reject it, after matching every earlier attempt and exactly at the forged one.
        void Forgery(int forgedAt, string label)
        {
            var forged = AsObject(Graph(row), "forged case");
            var point = AsObject(Step(forged, forgedAt)["after"], "after"); var state = AsObject(point["state"], "state"); var forgedRun = AsObject(state["run"], "run");
            forgedRun["coins"] = (long)forgedRun["coins"]! + 1;
            string stateHash = CanonicalState.Hash(state), jsonHash = Sha(Ordered(state)); point["stateHash"] = stateHash; point["jsonHash"] = jsonHash;
            foreach (object? item in AsList(forged["steps"], "steps").Skip(forgedAt + 1))
                foreach (string field in new[] { "before", "after" })
                    if (Get(item, field) is Dictionary<string, object?> reference && reference.Count == RefFields.Length && Get(reference, "ref") is long at && at == forgedAt) { reference["stateHash"] = stateHash; reference["jsonHash"] = jsonHash; }
            using var document = JsonDocument.Parse(Ordered(forged));
            var element = document.RootElement; Case? parsed = null; int reached = -1;
            Control("self-consistent forged " + label + " passes structural validation", () => parsed = ValidateCase(element, smallest, new Coverage()));
            if (parsed is not Case forgedCase) { Check(false, "self-consistent forged " + label + " could not be parsed for the C# replay probe"); return; }
            Probe("self-consistent forged " + label + " is rejected by actual C# replay", () => reached = Execute(forgedCase, data, definitions));
            Check(reached == forgedAt, "actual C# replay matched all " + I(forgedAt) + " attempts before the forged " + label + " and diverged exactly there (reached " + I(reached) + ")");
        }
        Forgery(committedAt, "committed state");
        // The same forgery after the first committed R1b node service: the replay must execute that service and compare its complete state.
        int serviceAt = target.Steps.FindIndex(s => Text(s, "outcome") == "committed" && Text(s.GetProperty("command"), "scope") == "run" && Services.Contains(Text(s.GetProperty("command"), "kind")));
        Require(serviceAt > committedAt && serviceAt < target.Prefix, target.Id + " has no committed node service inside its supported prefix to forge");
        Forgery(serviceAt, "state after node service " + Text(target.Steps[serviceAt].GetProperty("command"), "kind") + " #" + I(serviceAt));

        void CompletionForgery(string label, bool reload)
        {
            var forged = AsObject(Graph(row), "completion forgery");
            if (reload)
            {
                var point = AsObject(Get(forged["reload"], "created"), "reload point");
                var context = AsObject(point["state"], "reload context"); var reloaded = AsObject(context["run"], "reload Run");
                reloaded["coins"] = (long)reloaded["coins"]! + 1;
                point["stateHash"] = CanonicalState.Hash(context); point["jsonHash"] = Sha(Ordered(context));
            }
            else
            {
                var result = AsObject(Step(forged, target.Prefix - 1)["result"], "terminal return");
                var value = AsObject(result["value"], "terminal memory graph"); value["forgedProbe"] = true;
                result["jsonHash"] = Sha(Ordered(value));
            }
            using var document = JsonDocument.Parse(Ordered(forged)); Case? parsed = null;
            Control(label + " with recomputed hashes passes structural validation", () => parsed = ValidateCase(document.RootElement, smallest, new Coverage()));
            if (parsed is Case candidate) Probe(label + " is rejected by actual save/reload", () => Execute(candidate, data, definitions));
            else Check(false, label + " could not be parsed for actual execution");
        }
        CompletionForgery("forged terminal memory return", false);
        CompletionForgery("forged save-derived reload state", true);
    }

    // JSON.stringify text of a parsed graph in its source member order (CanonicalState string escaping), so an untampered round trip keeps every jsonHash.
    static string Ordered(object? value)
    {
        var text = new StringBuilder(); Write(value); return text.ToString();
        void Write(object? node)
        {
            if (node is Dictionary<string, object?> fields)
            {
                text.Append('{'); bool first = true;
                foreach (var pair in fields) { if (!first) text.Append(','); first = false; text.Append(CanonicalState.Serialize(pair.Key)).Append(':'); Write(pair.Value); }
                text.Append('}');
            }
            else if (node is List<object?> items)
            {
                text.Append('['); for (int i = 0; i < items.Count; i++) { if (i != 0) text.Append(','); Write(items[i]); } text.Append(']');
            }
            else text.Append(CanonicalState.Serialize(node));
        }
    }

    // ───────── 1000 legacy trace supported prefixes ─────────
    static void Legacy(string root, RunData data, string definitions)
    {
        var stats = new TraceHashChecks.SnapshotStats();
        TraceHashChecks.ReadGzip(Path.Combine(root, "engine", "tests", "vectors", "traces.jsonl.gz"),
            reader => TraceHashChecks.ReadSnapshots(reader, root, stats, out _, out _, trace => LegacyTrace(trace, data)));
        Check(stats.Scenarios == 1000 && stats.Short == 700 && stats.Full == 300 && stats.Hashes == 2000 && legacyScenarios == 1000,
            "legacy corpus: strict one-member gzip JSONL, five current source pins, 1000 sequential scenarios (700 short, 300 full) with verified snapshot hashes");
        Check(Same(legacyDenominators, LegacyMethods) && legacyDenominators.Values.Sum() == LegacyAttempts,
            "source complete trace denominators including commitMemory: " + I(LegacyAttempts) + " attempts with the literal per-method counts (" + Join(legacyDenominators) + ")");
        Check(legacyMatched == LegacyAttempts && Same(legacyExecuted, LegacyMethods) && legacyPrefixes == 1000,
            "actual C# Create + public commands incl. R1c actual terminal saving matched every supported attempt of all 1000 legacy prefixes (outcome, RuleError code, canonical context hash)");
        Check(legacyOfferProbes == LegacyOfferReturns, "every committed legacy forgeOffer return equals the hash-checked live pending.offers and stays a detached copy ("
            + I(legacyOfferProbes) + "/" + I(LegacyOfferReturns) + " probes)");
        Check(legacyStops.Count == 1 && legacyStops.GetValueOrDefault("DONE") == 1000 && legacySaveProbes == 1000,
            "all 1000 legacy traces complete their actual memory commit and detached-save probe, without skipping a command");
        Check(RunCreateChecks.DataPrint(data) == definitions, "legacy prefix replay preserves source definitions");
    }

    static void LegacyTrace(Dictionary<string, object?> trace, RunData data)
    {
        long index = legacyScenarios++, seed = trace.GetValueOrDefault("seed") as long? ?? -1; string length = trace.GetValueOrDefault("length") as string ?? "";
        string id = "legacy-" + I(seed) + "-" + length;
        try
        {
            Require(seed == 1000 + index && trace.GetValueOrDefault("memoryPolicy") as string == "legacy", id + " is not the next sequential legacy trace from seed 1000");
            var commands = AsList(trace.GetValueOrDefault("commands"), id + " commands"); int stop = -1; string stopKind = "";
            for (int i = 0; i < commands.Count && stop < 0; i++)
            {
                var command = AsObject(Get(commands[i], "command"), id + " command"); string kind = command.GetValueOrDefault("kind") as string ?? "";
                if (command.GetValueOrDefault("scope") as string == "run" && !Implemented.Contains(kind)) { stop = i; stopKind = kind; }
                else Bump(legacyDenominators, kind);
            }
            Require(stop < 0 && commands.Count > 0 && Get(Get(commands[commands.Count - 1], "command"), "kind") as string == "commitMemory",
                id + " contains an unknown command or lacks its terminal memory commit");
            stop = commands.Count; stopKind = "DONE";
            Bump(legacyStops, stopKind);
            if (seed == 1000 && length == "short") CrossCheck(trace, commands, id);
            // Fresh Create from the recorded setup only; afterwards nothing from the trace enters the Run except command envelopes.
            var setup = RunDataLoader.Setup(Element(trace.GetValueOrDefault("setup"))); string setupBefore = RunCreateChecks.SetupPrint(setup);
            var run = ExpeditionRun.Create(data, setup); var state = run.State;
            if (CanonicalState.Hash(Context(state, null)) != trace.GetValueOrDefault("initialStateHash") as string) { LegacyMismatch(id + " C# Create initial context hash differs from the trace"); return; }
            for (int i = 0; i < stop; i++)
            {
                var step = AsObject(commands[i], id + " step"); var command = AsObject(step.GetValueOrDefault("command"), id + " command");
                string scope = command.GetValueOrDefault("scope") as string ?? "", kind = command.GetValueOrDefault("kind") as string ?? "", where = id + "#" + I(i) + " " + scope + ":" + kind;
                var args = scope == "battle" ? null : AsList(command.GetValueOrDefault("args"), where + " args");
                string? code = null; object? returned = null;
                try
                {
                    var call = args == null ? BattleCall(run, state, kind, command.GetValueOrDefault("rid") as string ?? "", command.GetValueOrDefault("v") as long? ?? -1, Full(command.GetValueOrDefault("args")))
                        : RunCall(run, kind, args);
                    try { returned = call(); } catch (RuleException error) { code = error.Code; }
                }
                catch (Exception error) when (error is not InvalidDataException) { LegacyMismatch(where + " " + Describe(error)); return; }
                var after = run.State; string hash = CanonicalState.Hash(Context(after, null)), outcome = code == null ? "committed" : "rejected";
                string? expectedOutcome = step.GetValueOrDefault("outcome") as string, expectedCode = step.GetValueOrDefault("errorCode") as string, expectedHash = step.GetValueOrDefault("stateHash") as string;
                if (outcome != expectedOutcome || code != expectedCode || hash != expectedHash)
                {
                    LegacyMismatch(where + " C# " + outcome + (code == null ? "" : " " + code) + " " + Short(hash) + " vs JS " + expectedOutcome + (expectedCode == null ? "" : " " + expectedCode) + " " + Short(expectedHash)); return;
                }
                legacyMatched++; Bump(legacyExecuted, kind); if (code == null) legacyCommitted++; else legacyRejected++;
                // The trace records no return, so a committed forgeOffer return is compared with after.pending.offers, which the JS whole-state
                // hash above already fixes; then a caller mutation must change the copy and leave the complete live Run (incl. log) unchanged.
                if (code == null && kind == "forgeOffer")
                {
                    string live = Full(Context(after, null)), probe = where + " returned graph";
                    if (returned is not IDictionary<string, object?> offers) { LegacyMismatch(probe + " are not a dictionary"); return; }
                    string? offerDiff = MemoryV2DataLoader.Difference(Get(after.Pending, "offers"), offers);
                    if (offerDiff != null) { LegacyMismatch(probe + " differ from the hash-checked pending.offers: " + offerDiff); return; }
                    string original = Full(offers); Scribble(offers);
                    if (Full(offers) == original) { LegacyMismatch(probe + " positive control: caller mutation left the returned copy unchanged"); return; }
                    if (Full(Context(run.State, null)) != live) { LegacyMismatch(probe + " caller mutation reached the live pending or Run"); return; }
                    legacyOfferProbes++;
                }
                if (code == null && kind == "commitMemory")
                {
                    string live = Full(Context(after, null));
                    if (MemoryV2DataLoader.Difference(after.SavedMemory, returned) != null) { LegacyMismatch(where + " returned memory differs from actual saved memory"); return; }
                    if (returned is not IDictionary<string, object?> memory) { LegacyMismatch(where + " returned memory is not an object"); return; }
                    string beforeMutation = Full(memory); Scribble(memory);
                    if (Full(memory) == beforeMutation || Full(Context(run.State, null)) != live) { LegacyMismatch(where + " returned memory mutation reaches Run or positive control failed"); return; }
                    legacySaveProbes++;
                }
                bool started = code == null && kind == "enter" && args != null && args.Count == 1 && NodeType(state, args[0] as string) is "battle" or "elite" or "boss";
                if (started) battlesStarted++;
                if (code == null && after.Battle == null && (state.Battle != null || started)) battlesFinished++;
                if (code == null && kind == "archive") archives++;
                state = after;
            }
            var final = Context(run.State, null);
            // The legacy corpus stores canonical snapshots (log/ev omitted); the five-case oracle above compares full graphs including them.
            using var normalized = JsonDocument.Parse(CanonicalState.Serialize(final));
            string? finalDiff = MemoryV2DataLoader.Difference(trace.GetValueOrDefault("finalState"), Graph(normalized.RootElement));
            if (run.State.Phase != "DONE" || run.State.SavedMemory == null || finalDiff != null)
            { LegacyMismatch(id + " canonical final state/actual saved memory differs: " + finalDiff); return; }
            legacyPrefixes++;
            Check(RunCreateChecks.SetupPrint(setup) == setupBefore, id + " complete commands preserve the caller setup");
        }
        catch (Exception error) { LegacyMismatch(id + " " + (error is InvalidDataException ? error.Message : error.ToString())); }
    }
    static string? NodeType(RunState state, string? id)
    {
        if (state.Map == null || id == null) return null;
        if (state.Map.Boss.Id == id) return state.Map.Boss.Type;
        return state.Map.Floors.SelectMany(row => row).FirstOrDefault(node => node.Id == id)?.Type;
    }
    // The fixture's seed1000-short-legacy case and corpus scenario 1000 come from the same generator input and must be the same trace.
    static void CrossCheck(Dictionary<string, object?> trace, List<object?> commands, string id)
    {
        var corpus = new List<string> { CanonicalState.Serialize(trace.GetValueOrDefault("setup")), CanonicalState.Serialize(trace.GetValueOrDefault("policy")),
            trace.GetValueOrDefault("initialStateHash") as string ?? "", trace.GetValueOrDefault("finalStateHash") as string ?? "" };
        foreach (object? item in commands)
            corpus.Add(CanonicalState.Serialize(Get(item, "command")) + "|" + (Get(item, "outcome") as string) + "|" + (Get(item, "errorCode") as string ?? "null") + "|" + (Get(item, "stateHash") as string));
        Check(seed1000 is List<string> expected && corpus.SequenceEqual(expected), id + " equals fixture case seed1000-short-legacy: setup, policy, initial/final hash and every envelope/outcome/code/hash ("
            + (seed1000 == null ? "fixture record unavailable" : I(corpus.Count) + " entries") + ")");
    }

    // ───────── helpers ─────────
    static void Fields(JsonElement json, string[] names, string label) =>
        Require(json.ValueKind == JsonValueKind.Object && json.EnumerateObject().Select(BattleDataLoader.Name).SequenceEqual(names), label + " fields differ from the exact ordered source record");
    static List<JsonElement> Items(JsonElement json, string label) { Require(json.ValueKind == JsonValueKind.Array, label + " is not an array"); return json.EnumerateArray().ToList(); }
    static string Text(JsonElement json) => json.ValueKind == JsonValueKind.String ? BattleDataLoader.Graph(json) as string ?? "" : throw new InvalidDataException("Expected JSON string");
    static string Text(JsonElement json, string key) => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(key, out var value) ? Text(value) : throw new InvalidDataException("Missing JSON string " + key);
    static long Integer(JsonElement json) => json.ValueKind == JsonValueKind.Number && json.TryGetInt64(out long n) ? n : throw new InvalidDataException("Expected JSON integer");
    static object? Graph(JsonElement json) => MemoryV2DataLoader.Graph(json);
    static Dictionary<string, object?> AsObject(object? value, string label) => value as Dictionary<string, object?> ?? throw new InvalidDataException(label + " is not a JSON object");
    static List<object?> AsList(object? value, string label) => value as List<object?> ?? throw new InvalidDataException(label + " is not a JSON array");
    static object? Get(object? value, string key) => value is IDictionary<string, object?> fields && fields.TryGetValue(key, out var found) ? found : null;
    static void Omissions(object? graph, JsonElement paths, string label)
    {
        foreach (object? path in AsList(Graph(paths), label + " undefinedPaths"))
            Check(Omitted(graph, path), label + " own-undefined " + CanonicalState.Serialize(path) + " is omitted (array item null), never a present value");
    }
    // JS validateJSON: an undefined object member is absent; an undefined array item is written as null.
    static bool Omitted(object? graph, object? path)
    {
        if (path is not List<object?> steps || steps.Count == 0) return false;
        object? parent = graph;
        foreach (object? step in steps.Take(steps.Count - 1))
        {
            if (parent is IDictionary<string, object?> fields && step is string key && fields.TryGetValue(key, out var next)) parent = next;
            else if (parent is IList items && step is long index && index >= 0 && index < items.Count) parent = items[(int)index];
            else return false;
        }
        object? last = steps[^1];
        if (parent is IList list) return last is long at && at >= 0 && at < list.Count && list[(int)at] == null;
        return parent is IDictionary<string, object?> members && last is string name && !members.ContainsKey(name);
    }
    static Dictionary<string, object?> Context(RunState state, object? store) => new(StringComparer.Ordinal) { ["run"] = state.ToJsonGraph(), ["memory"] = store };
    static string Full(object? graph) => JsonSerializer.Serialize(graph);
    static string? TryHash(object? graph) { try { return CanonicalState.Hash(graph); } catch (ArgumentException) { return null; } }
    static string Sha(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    static string FileHash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    static string HashText(JsonElement json)
    {
        string hash = Text(json);
        Require(hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Malformed run-progression SHA-256");
        return hash;
    }
    static JsonElement Element(object? graph) { using var document = JsonDocument.Parse(JsonSerializer.Serialize(graph)); return document.RootElement.Clone(); }
    static Dictionary<string, object?> Shallow(JsonElement json) => json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value, StringComparer.Ordinal);
    static Dictionary<string, object?> Edit(JsonElement json, string key, object? value, bool remove = false)
    {
        var fields = Shallow(json); if (remove) fields.Remove(key); else fields[key] = value; return fields;
    }
    static Dictionary<string, object?> O(params object?[] pairs)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (int i = 0; i < pairs.Length; i += 2) result[pairs[i] as string ?? throw new InvalidDataException("Graph key")] = pairs[i + 1];
        return result;
    }
    static List<object?> L(params object?[] items) => new List<object?>(items);
    static void Bump(Dictionary<string, long> map, string key) => map[key] = map.GetValueOrDefault(key) + 1;
    static bool Same(Dictionary<string, long> actual, Dictionary<string, long> expected) => actual.Count == expected.Count && expected.All(p => actual.TryGetValue(p.Key, out long n) && n == p.Value);
    static string Join(Dictionary<string, long> counts) => string.Join(", ", counts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + " " + I(p.Value)));
    static string Short(string? hash) => hash == null ? "null" : hash.Length > 12 ? hash[..12] : hash;
    static string I(long value) => value.ToString(CultureInfo.InvariantCulture);
}
