// P1-EXPEDITION-BATTLE-FORECAST §14.3/§6/§16.2: actual C# Battle.Forecast and Run.BattleForecast against the independent original-JS oracle.
// Every case starts from its recorded setup through a fresh Battle.Create or Run.Create and replays the recorded real commands; recorded
// states and forecasts are comparison data only and are never injected. Late command rejection is a C#-only local-definition probe.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MemoryForge.Core;
using ExpeditionRun = MemoryForge.Core.Run;

static class BattlePreviewChecks
{
    const string Schema = "mf-battle-preview-v1", Status = "JS_ORACLE";
    const string Scope = "Fresh Battle.create/command and Run.create/enter/battleCmd replays; each play forecast is the original JS check() code or one independent fresh-candidate play; C# Battle.Forecast/Run.BattleForecast comparison only. Unity view, device and server acceptance PENDING";
    // Exact allowlist in source order, compared before any file is hashed: a fixture can never steer the host to read another path.
    static readonly (string Key, string Path)[] Sources = { ("data", "data/game_data.json"), ("engine", "engine/rules-core.js"), ("memory", "proposals/memory_v2.cjs"),
        ("canonical", "tools/gen_traces.mjs"), ("runCreateDecoder", "tools/gen_run_create_vectors.mjs"), ("generator", "tools/gen_battle_preview_vectors.mjs") };
    static readonly string[] Fields = { "error", "mode", "cost", "due", "hidden", "enemyHpLoss", "blockDelta", "hpDelta", "defeat" };
    static readonly string[] Reasons = { "definition", "rng", "draw" };
    static readonly string[] Errors = { "AP_LOW", "BAD_TARGET", "INVALID_MODE", "NOT_IN_HAND", "NOT_RESERVABLE", "NO_BATTLE", "OVERLOAD_WOULD_KILL", "RESERVE_ONLY", "SLOT_FULL", "WRONG_PHASE" };
    static readonly (string Id, string Group, int Forecasts, int Commands)[] Plan =
    {
        ("damage-block-targets", "battle", 12, 3), ("heal-reservation-slots", "battle", 11, 2), ("reflect-defeat", "battle", 5, 3), ("overload-self-damage", "battle", 5, 2),
        ("hidden-definition", "battle", 7, 3), ("hidden-hook-draw", "battle", 3, 4), ("hidden-hook-rng", "battle", 4, 2), ("picks-pull", "battle", 4, 4), ("run-expedition", "run", 19, 17)
    };
    // Fixed denominators: 8 authored battles 51 forecasts/23 commands; the expedition 6 fixed probes + 13 policy forecasts over 17 Run commands (measured).
    static readonly (string Key, int Value)[] Counts = { ("cases", 9), ("battleForecasts", 51), ("battleCommands", 23), ("runFixedForecasts", 6), ("runPolicyForecasts", 13),
        ("runCommands", 17), ("forecasts", 70), ("commands", 40) };
    const string ExpeditionSetup = "{\"party\":[{\"charId\":\"sera\"},{\"charId\":\"harin\"},{\"charId\":\"nia\"}],\"seed\":4242,\"length\":\"short\",\"memoryPolicy\":\"v2\",\"memory\":{},"
        + "\"relics\":[],\"difficulty\":{\"hp\":100,\"dmg\":100},\"zones\":[1],\"runId\":\"preview-run\",\"memoryVersion\":0,\"useMemory\":true}";
    const string PolicyLabel = "run policy play ";
    static readonly Dictionary<string, Dictionary<string, object?>> Actual = new(StringComparer.Ordinal);
    static int checks, failures, forecasts, commands, receiptProbes;
    static void Check(bool ok, string label) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL BattlePreview " + label); } }
    static void Require(bool ok, string label) { if (!ok) throw new InvalidDataException(label); }

    public static int Run(string root)
    {
        checks = failures = forecasts = commands = receiptProbes = 0; Actual.Clear();
        try
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, "engine", "tests", "vectors", "battle_preview_vectors.json.gz"));
            using var document = RunCreateChecks.Decode(bytes);
            using var content = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
            var fixture = document.RootElement; RunCreateChecks.UniqueKeys(fixture);
            ValidateFixture(fixture, content.RootElement, root);
            Check(true, "strict single-member gzip/UTF-8/unique-key fixture, allowlisted source pins, contract, fixed cases/denominators and setup-only inputs");
            DecoderControls(bytes);
            var data = BattleDataLoader.Load(root); var runData = RunDataLoader.Load(root); string definitions = Full(Catalog(data));
            string runDefinitions = Full(Catalog(runData.Memory.Battle));
            foreach (var row in fixture.GetProperty("cases").EnumerateArray())
            {
                if (Text(row, "group") == "battle") BattleCase(row, data); else RunCase(row, runData);
                Check(Full(Catalog(data)) == definitions, Text(row, "id") + " original combat definitions unchanged by every forecast and command");
            }
            Check(Full(Catalog(runData.Memory.Battle)) == runDefinitions, "actual Run-owned card/character/weapon/part/relic definitions remain unchanged");
            Check(forecasts == 70 && commands == 40, "exactly 70 actual C# forecasts (each repeated after caller mutation) and 40 real commands");
            IndependentAssertions();
            LateRejection(root);
            FixtureRejections(fixture, content.RootElement, root);
        }
        catch (Exception error) { Check(false, "fixture/execution: " + error); }
        Console.WriteLine($"BattlePreviewChecks: {checks - failures}/{checks} passed; {forecasts} actual forecasts, {commands} real commands, {receiptProbes} receipt probes");
        Console.WriteLine("BattlePreviewScope: actual Battle.Forecast/Run.BattleForecast vs fresh original-JS replays; Unity view integration, device and server acceptance PENDING");
        Console.WriteLine("BattlePreviewNotCovered: the pick:\"random\" definition branch has no authored card; late command rejection is a C#-only local-definition probe");
        return failures;
    }

    // ───────── actual execution ─────────
    static void BattleCase(JsonElement row, BattleData data)
    {
        string id = Text(row, "id");
        var battle = Battle.Create(data, BattleDataLoader.Setup(row.GetProperty("setup")));
        CompareState(row.GetProperty("created"), battle.State, id + " created");
        var committed = new List<(BattleRequest Request, Dictionary<string, object?> Response)>();
        int index = 0;
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            string label = id + " step " + index++;
            var results = new List<(JsonElement Record, Dictionary<string, object?> Result)>();
            foreach (var record in step.GetProperty("forecasts").EnumerateArray())
                results.Add((record, Probe(id, record, label, (uid, mode, target, picks) => battle.Forecast(uid, mode, target, picks),
                    () => Full(battle.State.ToJsonGraph()) + "|" + Full(battle.PublicView()), uid => battle.EffectiveCard(uid))));
            string point = Full(battle.State.ToJsonGraph());
            foreach (var (prior, stored) in committed)
            { receiptProbes++; SameGraph(stored, battle.Command(prior).ToJsonGraph(), label + " " + prior.Rid + " stored receipt replays unchanged after the forecasts"); }
            Check(Full(battle.State.ToJsonGraph()) == point, label + " receipt replays execute nothing");
            var input = step.GetProperty("request");
            if (input.ValueKind == JsonValueKind.Null) continue;
            var request = BattleDataLoader.Request(input); var before = battle.State;
            receiptProbes++;
            Check(Throws(() => battle.Command(new BattleRequest(request.Rid, before.V, "unknown-receipt-probe")), "UNKNOWN_COMMAND") && Full(battle.State.ToJsonGraph()) == point,
                label + " no forecast stored a receipt under the real request ID");
            var response = battle.Command(request); commands++; var graph = response.ToJsonGraph();
            Check(response.V == before.V + 1 && battle.State.V == response.V, label + " real " + request.Kind + " executes once as one new version");
            Same(step.GetProperty("response"), graph, label + " real response/events equal the original JS command");
            CompareState(step.GetProperty("after"), battle.State, label + " after");
            Consistency(results, request.Kind, input.GetProperty("args"), before, battle.State, response, label);
            string after = Full(battle.State.ToJsonGraph());
            receiptProbes++; SameGraph(graph, battle.Command(request).ToJsonGraph(), label + " the same request replays its stored response");
            Check(Full(battle.State.ToJsonGraph()) == after, label + " receipt replay executes nothing more");
            committed.Add((request, graph));
        }
    }

    static void RunCase(JsonElement row, RunData data)
    {
        string id = Text(row, "id");
        var run = ExpeditionRun.Create(data, RunDataLoader.Setup(row.GetProperty("setup")));
        CompareRunPoint(row.GetProperty("created"), run, id + " created");
        int index = 0;
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            string label = id + " step " + index++;
            var results = new List<(JsonElement Record, Dictionary<string, object?> Result)>();
            foreach (var record in step.GetProperty("forecasts").EnumerateArray())
                results.Add((record, Probe(id, record, label, (uid, mode, target, picks) => run.BattleForecast(uid, mode, target, picks),
                    () => Full(run.State.ToJsonGraph()) + "|" + Full(run.BattlePublicView()), uid => run.BattleEffectiveCard(uid))));
            var input = step.GetProperty("request");
            if (input.ValueKind == JsonValueKind.Null) continue;
            string kind = Text(input, "kind"); var args = input.GetProperty("args");
            if (Text(input, "scope") == "run") { run.Enter(Text(args, "node")); commands++; }
            else
            {
                var before = run.State.Battle ?? throw new InvalidDataException(label + " battle command without a live C# battle");
                var request = BattleDataLoader.Request("{\"rid\":null,\"v\":" + before.V.ToString(CultureInfo.InvariantCulture) + ",\"kind\":" + CanonicalState.Serialize(kind) + ",\"args\":" + args.GetRawText() + "}");
                var response = run.BattleCommand(kind, request.Args); commands++;
                Check(response.V == before.V + 1, label + " the next real Run battle command executes once as one new battle version");
                Same(step.GetProperty("response"), response.ToJsonGraph(), label + " real Run battle response/events equal the original JS command");
                var after = run.State.Battle;
                if (after != null) Consistency(results, kind, args, before, after, response, label);
            }
            CompareRunPoint(step.GetProperty("after"), run, label + " after");
        }
    }

    // One recorded request: all nine fields, complete live-state purity, caller picks untouched, then a repeat after mutating the returned object.
    static Dictionary<string, object?> Probe(string id, JsonElement record, string label, Func<string, string, string?, IReadOnlyList<string>?, BattlePlayForecast> forecast,
        Func<string> print, Func<string, BattleCardDef> card)
    {
        string name = Text(record, "label"), where = label + " " + name; var args = record.GetProperty("args");
        string uid = Text(args, "uid"), mode = Text(args, "mode"); string? target = OptionalText(args, "target");
        var supplied = args.GetProperty("picks").EnumerateArray().Select(p => (string)BattleDataLoader.Graph(p)!).ToList(); var picks = supplied.ToList();
        string before = print();
        var first = forecast(uid, mode, target, picks); forecasts++;
        var result = Project(first);
        Same(record.GetProperty("expected"), result, where + " all nine fields equal the original JS forecast");
        Check(print() == before, where + " forecast leaves complete live state/RNG/log/events/version/public view unchanged");
        Check(picks.SequenceEqual(supplied), where + " forecast neither consumes nor reorders the caller's picks");
        Mutate(first); picks.Reverse(); picks.Add("caller-mutation");
        var again = forecast(uid, mode, target, supplied);
        SameGraph(result, Project(again), where + " repeated forecast after returned-object and caller-picks mutation is identical");
        if (supplied.Count == 0) SameGraph(result, Project(forecast(uid, mode, target, null)), where + " omitted picks equal empty picks");
        Check(print() == before, where + " repeated and error forecasts still leave the live state unchanged");
        if (again.Hidden)
        {
            var reasons = record.GetProperty("hiddenBy").EnumerateArray().Select(r => r.GetString()).ToList();
            Check(HiddenDefinition(card(uid).Effects) == reasons.Contains("definition"), where + " C# effective definition agrees with the oracle's definition-or-hook reason");
        }
        Require(Actual.TryAdd(id + "/" + name, result), "Duplicate preview forecast label " + id + "/" + name);
        return result;
    }
    // When a step's real play is one of its forecast requests, the actual C# outcome must be exactly that forecast.
    static void Consistency(List<(JsonElement Record, Dictionary<string, object?> Result)> results, string kind, JsonElement args, BattleState before, BattleState after,
        BattleResponse response, string label)
    {
        if (kind != "play") return;
        var request = BattleDataLoader.Graph(args);
        int at = results.FindIndex(r => JsonGraphComparison.Difference(BattleDataLoader.Graph(r.Record.GetProperty("args")), request, "$", 0) == null);
        if (at < 0 || results[at].Result["error"] != null) return;
        var result = results[at].Result; var play = response.Events.First(e => e.E == "play");
        Check((int)result["cost"]! == play.Cost, label + " forecast cost equals the real play's AP");
        if ((string)result["mode"]! != "now") { Check(Equals(result["due"], response.Events.First(e => e.E == "reserve").Due), label + " forecast due equals the real reservation turn"); return; }
        if ((bool)result["hidden"]!)
        {
            var reasons = results[at].Record.GetProperty("hiddenBy").EnumerateArray().Select(r => r.GetString()).ToList();
            Check(reasons.Contains("rng") == !before.Rng.SequenceEqual(after.Rng) && reasons.Contains("draw") == !before.Zones.Draw.SequenceEqual(after.Zones.Draw),
                label + " the real play moved exactly the RNG/draw pile its hidden forecast named");
            return;
        }
        long loss = before.Enemies.Sum(e => (long)Math.Max(0, e.Hp - (after.Enemies.FirstOrDefault(a => a.Uid == e.Uid)?.Hp ?? 0)));
        Check(loss == (int)result["enemyHpLoss"]! && after.Block - before.Block == (int)result["blockDelta"]! && after.Hp - before.Hp == (int)result["hpDelta"]!
            && (after.Phase == "DEFEAT") == (bool)result["defeat"]!, label + " visible forecast equals the real play's enemy HP loss, block, HP and defeat");
    }
    static void CompareState(JsonElement point, BattleState state, string label)
    {
        var graph = state.ToJsonGraph();
        Same(point.GetProperty("state"), graph, label + " complete battle state incl. log/events/RNG/version");
        Check(CanonicalState.Hash(graph) == HashText(point.GetProperty("stateHash")), label + " canonical battle state hash");
    }
    static void CompareRunPoint(JsonElement point, ExpeditionRun run, string label)
    {
        var state = run.State; var battle = point.GetProperty("battle");
        Check(state.Phase == Text(point, "phase"), label + " Run phase");
        if (battle.ValueKind == JsonValueKind.Null) { Check(state.Battle == null, label + " no live battle"); return; }
        if (state.Battle == null) { Check(false, label + " live battle missing"); return; }
        var graph = state.Battle.ToJsonGraph();
        Same(battle, graph, label + " complete Run battle state incl. keys/log/events/RNG/version");
        Check(CanonicalState.Hash(graph) == HashText(point.GetProperty("battleHash")), label + " canonical Run battle hash");
    }
    static Dictionary<string, object?> Project(BattlePlayForecast f) => new(StringComparer.Ordinal)
    {
        ["error"] = f.Error, ["mode"] = f.Mode, ["cost"] = f.Cost, ["due"] = f.Due, ["hidden"] = f.Hidden,
        ["enemyHpLoss"] = f.EnemyHpLoss, ["blockDelta"] = f.BlockDelta, ["hpDelta"] = f.HpDelta, ["defeat"] = f.Defeat
    };
    static void Mutate(BattlePlayForecast f)
    {
        f.Error = "caller-mutation"; f.Mode = "caller-mutation"; f.Cost = -1; f.Due = -1; f.Hidden = !f.Hidden;
        f.EnemyHpLoss = -1; f.BlockDelta = int.MinValue; f.HpDelta = int.MaxValue; f.Defeat = !f.Defeat;
    }
    // Independent of Core: the accepted practice-view rule (random target/pick or draw anywhere in if/then/else).
    static bool HiddenDefinition(IEnumerable<EffectDef> effects) => effects.Any(e => e.Target == "rand" || e.Pick == "random" || e.Op == "draw"
        || e.Then != null && HiddenDefinition(e.Then) || e.Else != null && HiddenDefinition(e.Else));

    // ───────── independent literals from the unchanged original sources ─────────
    static void Expect(string key, string? error, string mode, int cost, int? due = null, bool hidden = false, int loss = 0, int block = 0, int hp = 0, bool defeat = false)
    {
        var expected = new Dictionary<string, object?>(StringComparer.Ordinal)
        { ["error"] = error, ["mode"] = mode, ["cost"] = cost, ["due"] = due, ["hidden"] = hidden, ["enemyHpLoss"] = loss, ["blockDelta"] = block, ["hpDelta"] = hp, ["defeat"] = defeat };
        Check(Actual.TryGetValue(key, out var actual) && JsonGraphComparison.Difference(expected, actual, "$", 0) == null, "independent original-source literal " + key);
    }
    static void IndependentAssertions()
    {
        const string D = "damage-block-targets/", H = "heal-reservation-slots/", R = "reflect-defeat/", O = "overload-self-damage/", F = "hidden-definition/", P = "picks-pull/";
        // power4 160/vit 81/level 1: sera_round 8, harin_shield 8, sera_cover blk5+dmg4, harin_bash 6; e_cog def b6 on its first enemy turn.
        Expect(D + "turn1 sera_round cog", null, "now", 1, loss: 8); Expect(D + "turn1 harin_shield", null, "now", 1, block: 8);
        Expect(D + "turn2 sera_round cog block6", null, "now", 1, loss: 2); Expect(D + "turn2 sera_cover cog block6", null, "now", 1, block: 5);
        Expect(D + "turn2 harin_bash moth", null, "now", 1, loss: 6); Expect(D + "turn2 sera_cover cog after hit", null, "now", 1, loss: 4, block: 5);
        Expect(D + "turn1 sera_round next", null, "next", 1, due: 2);
        foreach (var (label, code, mode) in new[] { ("created phase", "WRONG_PHASE", "now"), ("turn1 missing target", "BAD_TARGET", "now"), ("turn1 invalid mode", "INVALID_MODE", "later"),
            ("turn1 unknown uid", "NOT_IN_HAND", "now"), ("turn2 played card", "NOT_IN_HAND", "now") }) Expect(D + label, code, mode, 0);
        // aila_bell heal5 at HP150/162; empty_clock -1 only before the first reservation; base after slot 1.
        Expect(H + "heal", null, "now", 1, hp: 5); Expect(H + "heal again", null, "now", 1, hp: 5); Expect(H + "page damage", null, "now", 1, loss: 8);
        Expect(H + "first reservation next", null, "next", 1, due: 2); Expect(H + "first reservation after", null, "after", 1, due: 3);
        Expect(H + "after-only after", null, "after", 2, due: 3); Expect(H + "discounted zero cost after", null, "after", 0, due: 3); Expect(H + "clock used next", null, "next", 1, due: 2);
        Expect(H + "reserve-only now", "RESERVE_ONLY", "now", 0); Expect(H + "after-only next", "NOT_RESERVABLE", "next", 0); Expect(H + "after slot full", "SLOT_FULL", "after", 0);
        // e_mirrorclerk reflect3 from its first turn; HP3: the first reflected hit is lethal and stops later hits.
        Expect(R + "turn1 no reflect yet", null, "now", 1, loss: 8); Expect(R + "reflect lethal multi-hit", null, "now", 1, loss: 4, hp: -3, defeat: true);
        Expect(R + "reflect lethal single hit", null, "now", 1, loss: 8, hp: -3, defeat: true); Expect(R + "reflect reservation", null, "next", 1, due: 3);
        Expect(R + "after defeat", "WRONG_PHASE", "now", 0);
        // HP5/81 <= 50%: lumi_critical +30% (18 -> 23, 8 -> 10); overdrive self-damage 5 would leave HP below 1.
        Expect(O + "selfdmg critical damage", null, "now", 1, loss: 23, hp: -4); Expect(O + "critical jab", null, "now", 1, loss: 10); Expect(O + "hp1 critical jab", null, "now", 1, loss: 10);
        Expect(O + "overload would kill", "OVERLOAD_WOULD_KILL", "now", 0); Expect(O + "hp1 overload would kill", "OVERLOAD_WOULD_KILL", "now", 0);
        Expect(F + "draw definition", null, "now", 0, hidden: true); Expect(F + "random target definition", null, "now", 3, hidden: true);
        Expect(F + "nested draw definition", null, "now", 1, hidden: true); Expect(F + "nested draw definition after owner change", null, "now", 1, hidden: true);
        Expect(F + "visible control", null, "now", 1, loss: 8); Expect(F + "nested draw reserved", null, "next", 1, due: 2); Expect(F + "ap low", "AP_LOW", "now", 0);
        Expect("hidden-hook-draw/third owner hook draw", null, "now", 1, hidden: true); Expect("hidden-hook-draw/third owner reserve", null, "next", 1, due: 2);
        Check(Actual.TryGetValue("hidden-hook-draw/earlier owner control", out var control) && control["error"] == null && !(bool)control["hidden"]! && (int)control["cost"]! == 1
            && ((int)control["enemyHpLoss"]! > 0 || (int)control["blockDelta"]! > 0), "independent: an earlier owner's card stays visible while the third owner's hook draw hides");
        Expect("hidden-hook-rng/exhaust hook random damage", null, "now", 3, hidden: true); Expect("hidden-hook-rng/harin_shield control", null, "now", 1, block: 8);
        Expect("hidden-hook-rng/after hook harin_shield", null, "now", 1, block: 8); Expect("hidden-hook-rng/after hook harin_bash page", null, "now", 1, loss: 6);
        // sera_pull: no pick/after pick/invalid pick move sera_timed; picking the next-turn sera_round resolves it now at rv12.
        Expect(P + "pull default", null, "now", 0); Expect(P + "pull next reservation", null, "now", 0, loss: 12); Expect(P + "pull after reservation", null, "now", 0); Expect(P + "pull invalid pick", null, "now", 0);
        Expect("run-expedition/map now", "NO_BATTLE", "now", 0); Expect("run-expedition/map after with picks", "NO_BATTLE", "after", 0);
        Expect("run-expedition/after battle", "NO_BATTLE", "now", 0); Expect("run-expedition/run unknown uid", "NOT_IN_HAND", "now", 0);
    }

    // C#-only: authored plays that pass Check always commit, so a negative local-definition fault after real Create/begin shows the late path.
    static void LateRejection(string root)
    {
        var data = BattleDataLoader.Load(root);
        var setup = new BattleSetup { Party = new List<BattleActorSetup> { new() { CharId = "harin" } }, Deck = new List<BattleCardSpec> { new() { Id = "harin_shield" } },
            Enemies = new List<string> { "e_cog" }, Hp = 81, MaxHp = 81, Seed = 0 };
        var battle = Battle.Create(data, setup); battle.Command(new BattleRequest("late-begin", 0, "begin"));
        var play = BattleDataLoader.Request("{\"rid\":\"late-play\",\"v\":1,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"mode\":\"now\",\"target\":null,\"picks\":[]}}");
        var effects = data.Cards.Single(c => c.Id == "harin_shield").Effects;
        effects.Add(new EffectDef { Op = "blk", V = int.MaxValue }); // block 8 + Int32.MaxValue overflows inside the real play
        string before = Full(battle.State.ToJsonGraph()), faulty = Full(Catalog(data));
        Check(battle.Check("c1") == null, "C#-only late rejection: Check passes before the faulty effect runs");
        SameGraph(new Dictionary<string, object?>(StringComparer.Ordinal) { ["error"] = "BATTLE_NUMBER_OUT_OF_RANGE", ["mode"] = "now", ["cost"] = 0, ["due"] = null, ["hidden"] = false,
            ["enemyHpLoss"] = 0, ["blockDelta"] = 0, ["hpDelta"] = 0, ["defeat"] = false }, Project(battle.Forecast("c1")), "C#-only late rejection: the forecast returns the real command's code with default fields");
        Check(Full(battle.State.ToJsonGraph()) == before && Full(Catalog(data)) == faulty, "C#-only late rejection leaves live state and the local definitions unchanged");
        Check(Throws(() => battle.Command(play), "BATTLE_NUMBER_OUT_OF_RANGE") && Full(battle.State.ToJsonGraph()) == before, "C#-only late rejection: the real play rejects with the same code atomically");
        effects.RemoveAt(effects.Count - 1);
        var corrected = battle.Forecast("c1");
        Check(corrected.Error == null && corrected.Cost == 1 && corrected.BlockDelta == 8 && !corrected.Hidden && corrected.EnemyHpLoss == 0 && corrected.HpDelta == 0 && !corrected.Defeat,
            "corrected definition forecasts block +8 without poisoned state");
        var response = battle.Command(play);
        Check(response.V == 2 && battle.State.Block == 8, "the failed request ID stays free and the corrected real play gains exactly the forecast block");
    }

    // ───────── fixture validation (structure only; values are proven by actual execution above) ─────────
    static void ValidateFixture(JsonElement fixture, JsonElement source, string root)
    {
        RequireKeys(fixture, new[] { "schema", "scope", "status", "dataVersion", "sources", "fields", "contract", "reachability", "coverage", "counts", "cases" }, "Preview fixture top-level fields");
        Require(Text(fixture, "schema") == Schema && Text(fixture, "scope") == Scope && Text(fixture, "status") == Status, "Wrong preview schema/scope/status");
        Require(Text(fixture, "dataVersion") == Text(source.GetProperty("meta"), "version"), "Wrong preview data version");
        var sources = fixture.GetProperty("sources");
        Require(sources.ValueKind == JsonValueKind.Object && sources.EnumerateObject().Select(p => p.Name).SequenceEqual(Sources.Select(s => s.Key)), "Preview source allowlist membership/order differs");
        foreach (var (key, path) in Sources)
        {
            var record = sources.GetProperty(key); RequireKeys(record, new[] { "path", "sha256" }, "Preview source record " + key);
            Require(Text(record, "path") == path && HashText(record.GetProperty("sha256")) == FileHash(Path.Combine(root, path)), "Stale/substituted preview source: " + key);
        }
        Require(fixture.GetProperty("fields").ValueKind == JsonValueKind.Array && fixture.GetProperty("fields").EnumerateArray().Select(f => f.ValueKind == JsonValueKind.String ? f.GetString() : null).SequenceEqual(Fields), "Preview forecast field list differs");
        var contract = fixture.GetProperty("contract");
        RequireKeys(contract, new[] { "request", "error", "reservation", "hidden", "outcome", "replay" }, "Preview contract members");
        Require(contract.EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.String && p.Value.GetString()!.Length != 0), "Preview contract text missing");
        var reach = fixture.GetProperty("reachability");
        RequireKeys(reach, new[] { "notCovered", "csharpOnly" }, "Preview reachability categories");
        RequireKeys(reach.GetProperty("notCovered"), new[] { "definitionPickRandom" }, "Preview NOT_COVERED category hidden or changed");
        RequireKeys(reach.GetProperty("csharpOnly"), new[] { "lateCommandRejection" }, "Preview C#-only category hidden or changed");
        Require(!AuthoredPickRandom(source), "An authored pick:\"random\" card exists; the NOT_COVERED claim is stale");
        var rows = Items(fixture.GetProperty("cases"), "Preview cases");
        Require(rows.Count == Plan.Length, "Preview case count differs");
        int battleForecasts = 0, battleCommands = 0, runFixed = 0, runPolicy = 0, runCommands = 0;
        var errors = new SortedSet<string>(StringComparer.Ordinal); var hidden = new SortedSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; var plan = Plan[i];
            RequireKeys(row, new[] { "id", "group", "role", "setup", "created", "steps" }, "Preview case fields");
            Require(Text(row, "id") == plan.Id && Text(row, "group") == plan.Group && Text(row, "role").Length != 0, "Preview case identity/order changed");
            if (plan.Group == "battle")
            {
                var (f, c) = ValidateBattleCase(row, errors, hidden);
                Require(f == plan.Forecasts && c == plan.Commands, "Preview per-case forecast/command denominator changed: " + plan.Id);
                battleForecasts += f; battleCommands += c;
            }
            else
            {
                var (f, c, p) = ValidateRunCase(row, errors, hidden);
                Require(f == plan.Forecasts && c == plan.Commands, "Preview per-case forecast/command denominator changed: " + plan.Id);
                runFixed += f - p; runPolicy += p; runCommands += c;
            }
        }
        var measured = new Dictionary<string, int>(StringComparer.Ordinal) { ["cases"] = rows.Count, ["battleForecasts"] = battleForecasts, ["battleCommands"] = battleCommands,
            ["runFixedForecasts"] = runFixed, ["runPolicyForecasts"] = runPolicy, ["runCommands"] = runCommands, ["forecasts"] = battleForecasts + runFixed + runPolicy, ["commands"] = battleCommands + runCommands };
        var counts = fixture.GetProperty("counts");
        Require(counts.ValueKind == JsonValueKind.Object && counts.EnumerateObject().Select(p => p.Name).SequenceEqual(Counts.Select(c => c.Key)), "Preview count members differ");
        foreach (var (key, value) in Counts) Require(Int(counts.GetProperty(key)) == value && measured[key] == value, "Preview denominator differs from the fixed measured count: " + key);
        var coverage = fixture.GetProperty("coverage"); RequireKeys(coverage, new[] { "errors", "hidden" }, "Preview coverage fields");
        Require(Items(coverage.GetProperty("errors"), "errors").Select(e => e.GetString()).SequenceEqual(errors) && errors.SequenceEqual(Errors), "Preview error-code coverage differs");
        Require(Items(coverage.GetProperty("hidden"), "hidden").Select(e => e.GetString()).SequenceEqual(hidden) && Reasons.All(hidden.Contains),
            "Preview hidden coverage differs or lacks a forecast hidden only by definition, rng or draw");
    }
    static (int Forecasts, int Commands) ValidateBattleCase(JsonElement row, SortedSet<string> errors, SortedSet<string> hidden)
    {
        string id = Text(row, "id"); var setup = row.GetProperty("setup");
        RequireKeys(setup, new[] { "party", "deck", "hp", "maxHp", "seed", "enemies", "relics" }, id + " setup must hold only Battle.create inputs");
        foreach (var actor in Items(setup.GetProperty("party"), id + " party")) RequireKeys(actor, new[] { "charId" }, id + " party injects actor state");
        foreach (var card in Items(setup.GetProperty("deck"), id + " deck")) RequireKeys(card, new[] { "id" }, id + " deck injects produced card state");
        var created = row.GetProperty("created"); RequireKeys(created, new[] { "state", "stateHash" }, id + " created point");
        ValidateHash(created.GetProperty("state"), created.GetProperty("stateHash"), id + " created");
        Require(Text(created.GetProperty("state"), "phase") == "CREATED" && Int(created.GetProperty("state").GetProperty("v")) == 0, id + " does not start from a fresh Battle.create");
        int version = 0, forecastCount = 0, commandCount = 0, index = 0;
        foreach (var step in Items(row.GetProperty("steps"), id + " steps"))
        {
            foreach (var record in Items(step.GetProperty("forecasts"), id + " forecasts")) { ValidateRecord(record, false, errors, hidden, id); forecastCount++; }
            var request = step.GetProperty("request");
            if (request.ValueKind == JsonValueKind.Null) { RequireKeys(step, new[] { "forecasts", "request" }, id + " forecast-only step"); index++; continue; }
            RequireKeys(step, new[] { "forecasts", "request", "response", "after" }, id + " command step fields");
            RequireKeys(request, new[] { "rid", "v", "kind", "args" }, id + " request fields");
            string kind = Text(request, "kind"); var args = request.GetProperty("args");
            Require(kind is "begin" or "end" or "play" && Text(request, "rid") == id + "-s" + index.ToString(CultureInfo.InvariantCulture) + "-" + kind && Int(request.GetProperty("v")) == version,
                id + " request identity/version continuity");
            if (kind == "play") RequireKeys(args, new[] { "uid", "mode", "target", "picks" }, id + " play args");
            else Require(args.ValueKind == JsonValueKind.Object && !args.EnumerateObject().Any(), id + " begin/end args");
            var response = step.GetProperty("response"); RequireKeys(response, new[] { "v", "phase", "ev" }, id + " response fields");
            var after = step.GetProperty("after"); RequireKeys(after, new[] { "state", "stateHash" }, id + " after point");
            ValidateHash(after.GetProperty("state"), after.GetProperty("stateHash"), id + " step " + index.ToString(CultureInfo.InvariantCulture));
            Require(Int(response.GetProperty("v")) == version + 1 && Int(after.GetProperty("state").GetProperty("v")) == version + 1 && Text(response, "phase") == Text(after.GetProperty("state"), "phase"),
                id + " committed response/version/phase");
            version++; commandCount++; index++;
        }
        return (forecastCount, commandCount);
    }
    static (int Forecasts, int Commands, int Policy) ValidateRunCase(JsonElement row, SortedSet<string> errors, SortedSet<string> hidden)
    {
        string id = Text(row, "id");
        using (var expected = JsonDocument.Parse(ExpeditionSetup)) RequireSame(expected.RootElement, BattleDataLoader.Graph(row.GetProperty("setup")), id + " expedition setup changed or injects state");
        var created = row.GetProperty("created"); ValidateRunPoint(created, id + " created");
        Require(Text(created, "phase") == "MAP" && created.GetProperty("battle").ValueKind == JsonValueKind.Null, id + " must start on the map without a battle");
        var steps = Items(row.GetProperty("steps"), id + " steps");
        int forecastCount = 0, commandCount = 0, policy = 0; int? version = null; var point = created;
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i]; bool outside = point.GetProperty("battle").ValueKind == JsonValueKind.Null;
            var records = Items(step.GetProperty("forecasts"), id + " forecasts");
            var plays = records.Where(r => Text(r, "label").StartsWith(PolicyLabel, StringComparison.Ordinal)).ToList();
            foreach (var record in records) { ValidateRecord(record, outside, errors, hidden, id); forecastCount++; }
            foreach (var record in plays) Require(Text(record, "label") == PolicyLabel + (policy++).ToString(CultureInfo.InvariantCulture), id + " policy forecast labels must be sequential");
            var request = step.GetProperty("request");
            if (request.ValueKind == JsonValueKind.Null)
            {
                RequireKeys(step, new[] { "forecasts", "request" }, id + " final forecast-only step");
                Require(i == steps.Count - 1 && outside && records.Count == 1 && Text(point, "phase") is "REWARD" or "DEFEAT", id + " only the final point probes outside the finished battle");
                continue;
            }
            RequireKeys(step, new[] { "forecasts", "request", "response", "after" }, id + " command step fields");
            RequireKeys(request, new[] { "scope", "kind", "args" }, id + " Run request fields");
            string scope = Text(request, "scope"), kind = Text(request, "kind"); var args = request.GetProperty("args");
            var after = step.GetProperty("after"); ValidateRunPoint(after, id + " step " + i.ToString(CultureInfo.InvariantCulture));
            if (i == 0)
            {
                Require(scope == "run" && kind == "enter" && outside && records.Count == 2 && step.GetProperty("response").ValueKind == JsonValueKind.Null, id + " the first step enters a node after two map probes");
                RequireKeys(args, new[] { "node" }, id + " enter args"); _ = Text(args, "node");
                Require(Text(after, "phase") == "BATTLE" && after.GetProperty("battle").ValueKind == JsonValueKind.Object, id + " entering the first node opens a battle");
                version = Int(after.GetProperty("battle").GetProperty("v"));
            }
            else
            {
                Require(scope == "battle" && !outside && version.HasValue && records.Count == plays.Count + (i == 1 ? 3 : 0), id + " later steps are battle commands; fixed probes only at the first battle point");
                if (kind == "play") { Require(plays.Count == 1, id + " a policy play has exactly one forecast"); RequireSame(plays[0].GetProperty("args"), BattleDataLoader.Graph(args), id + " a policy play executes exactly its forecast request"); }
                else Require(kind == "end" && plays.Count == 0 && args.ValueKind == JsonValueKind.Object && !args.EnumerateObject().Any(), id + " end steps carry no play forecast");
                var response = step.GetProperty("response"); RequireKeys(response, new[] { "v", "phase", "ev" }, id + " Run battle response fields");
                Require(Int(response.GetProperty("v")) == version + 1, id + " each Run battle command commits one new battle version");
                var battle = after.GetProperty("battle");
                if (battle.ValueKind == JsonValueKind.Null) { Require(i == steps.Count - 2 && Text(after, "phase") is "REWARD" or "DEFEAT", id + " only the last command ends the battle"); version = null; }
                else { Require(Text(after, "phase") == "BATTLE" && Int(battle.GetProperty("v")) == version + 1, id + " Run battle version continuity"); version++; }
            }
            commandCount++; point = after;
        }
        return (forecastCount, commandCount, policy);
    }
    static void ValidateRunPoint(JsonElement point, string label)
    {
        RequireKeys(point, new[] { "phase", "battle", "battleHash" }, label + " Run point fields"); _ = Text(point, "phase");
        if (point.GetProperty("battle").ValueKind == JsonValueKind.Null) Require(point.GetProperty("battleHash").ValueKind == JsonValueKind.Null, label + " battleHash without a battle");
        else ValidateHash(point.GetProperty("battle"), point.GetProperty("battleHash"), label);
    }
    static void ValidateRecord(JsonElement record, bool outside, SortedSet<string> errors, SortedSet<string> hidden, string id)
    {
        RequireKeys(record, new[] { "label", "args", "expected", "hiddenBy" }, id + " forecast record fields");
        string label = id + " " + Text(record, "label"); var args = record.GetProperty("args");
        RequireKeys(args, new[] { "uid", "mode", "target", "picks" }, label + " forecast args");
        _ = Text(args, "uid"); string mode = Text(args, "mode");
        Require(args.GetProperty("target").ValueKind is JsonValueKind.Null or JsonValueKind.String && Items(args.GetProperty("picks"), label + " picks").All(p => p.ValueKind == JsonValueKind.String), label + " forecast arg types");
        var expected = record.GetProperty("expected");
        Require(expected.ValueKind == JsonValueKind.Object && expected.EnumerateObject().Select(p => p.Name).SequenceEqual(Fields), label + " expected must be exactly the nine ordered forecast fields");
        var error = expected.GetProperty("error"); var due = expected.GetProperty("due");
        Require(error.ValueKind is JsonValueKind.Null or JsonValueKind.String && due.ValueKind is JsonValueKind.Null or JsonValueKind.Number && Text(expected, "mode") == mode, label + " expected types/mode");
        int cost = Int(expected.GetProperty("cost")), loss = Int(expected.GetProperty("enemyHpLoss")), block = Int(expected.GetProperty("blockDelta")), hp = Int(expected.GetProperty("hpDelta"));
        bool isHidden = Bool(expected.GetProperty("hidden")), defeat = Bool(expected.GetProperty("defeat"));
        var reasons = Items(record.GetProperty("hiddenBy"), label + " hiddenBy").Select(r => r.ValueKind == JsonValueKind.String ? r.GetString()! : throw new InvalidDataException(label + " hidden reason type")).ToList();
        Require(reasons.Distinct(StringComparer.Ordinal).Count() == reasons.Count && reasons.SequenceEqual(Reasons.Where(reasons.Contains)), label + " hidden reasons must be distinct definition/rng/draw in order");
        bool numbers = loss == 0 && block == 0 && hp == 0 && !defeat;
        if (error.ValueKind == JsonValueKind.String)
        {
            Require(cost == 0 && due.ValueKind == JsonValueKind.Null && !isHidden && numbers && reasons.Count == 0, label + " a rejected forecast keeps every other field at its default");
            Require(outside == (error.GetString() == "NO_BATTLE"), label + " NO_BATTLE exactly outside battle"); errors.Add(error.GetString()!); return;
        }
        Require(!outside && mode is "now" or "next" or "after" && cost >= 0 && loss >= 0, label + " accepted forecast inside battle with a valid mode/cost/loss");
        if (mode != "now") { Require(due.ValueKind == JsonValueKind.Number && Int(due) >= 1 && !isHidden && numbers && reasons.Count == 0, label + " a reservation reports only cost and due"); return; }
        Require(due.ValueKind == JsonValueKind.Null && isHidden == (reasons.Count != 0), label + " a now forecast has no due and is hidden exactly when a reason exists");
        if (isHidden) { Require(numbers, label + " hidden numbers stay zero"); hidden.Add(string.Join("+", reasons)); }
    }
    static void ValidateHash(JsonElement state, JsonElement hash, string label) =>
        Require(CanonicalState.Hash(BattleDataLoader.Graph(state)) == HashText(hash), "Stale preview state hash " + label);
    static bool AuthoredPickRandom(JsonElement source)
    {
        static bool Walk(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Any(Walk),
            JsonValueKind.Object => value.TryGetProperty("pick", out var pick) && pick.ValueKind == JsonValueKind.String && pick.GetString() == "random" || value.EnumerateObject().Any(p => Walk(p.Value)),
            _ => false
        };
        return source.GetProperty("cards").EnumerateArray().Any(card => Walk(card.GetProperty("eff")) || card.TryGetProperty("plus", out var plus) && Walk(plus));
    }

    // ───────── corruption controls ─────────
    static void DecoderControls(byte[] bytes)
    {
        // RFC 1952 empty member with the exporter header: final empty fixed block 03 00, CRC32 0, ISIZE 0.
        byte[] empty = { 0x1f, 0x8b, 8, 0, 0, 0, 0, 0, 2, 3, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var crc = (byte[])bytes.Clone(); crc[^8] ^= 1;
        foreach (var (label, input) in new (string, byte[])[] { ("empty gzip member prefix", empty.Concat(bytes).ToArray()), ("empty gzip member suffix", bytes.Concat(empty).ToArray()),
            ("truncated footer", bytes[..^1]), ("CRC32 bit flip", crc) })
        {
            bool rejected = false;
            try { RunCreateChecks.Decode(input).Dispose(); } catch (Exception) { rejected = true; }
            Check(rejected, "strict fixture decoder rejects " + label);
        }
    }
    static void FixtureRejections(JsonElement fixture, JsonElement source, string root)
    {
        void Reject(Dictionary<string, object?> graph, string label)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(graph)); bool rejected = false;
            try { ValidateFixture(json.RootElement, source, root); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "fixture tamper rejected: " + label);
        }
        Dictionary<string, object?> Fresh() => O(BattleDataLoader.Graph(fixture));
        Dictionary<string, object?> Case(Dictionary<string, object?> graph, int index) => O(L(graph["cases"])[index]);
        Dictionary<string, object?> Step(Dictionary<string, object?> graph, int index, int step) => O(L(Case(graph, index)["steps"])[step]);
        Dictionary<string, object?> Forecast(Dictionary<string, object?> graph, int index, int step, int at) => O(L(Step(graph, index, step)["forecasts"])[at]);
        foreach (string field in new[] { "schema", "scope", "status", "dataVersion" }) { var bad = Fresh(); bad[field] = "tampered"; Reject(bad, field); }
        { var bad = Fresh(); O(O(bad["sources"])["engine"])["path"] = "../engine/rules-core.js"; Reject(bad, "substituted source path"); }
        { var bad = Fresh(); O(O(bad["sources"])["engine"])["sha256"] = new string('0', 64); Reject(bad, "stale source hash"); }
        { var bad = Fresh(); O(bad["sources"])["extra"] = O(O(bad["sources"])["engine"]); Reject(bad, "unlisted source"); }
        { var bad = Fresh(); L(bad["fields"]).Reverse(); Reject(bad, "forecast field order"); }
        { var bad = Fresh(); L(bad["cases"]).RemoveAt(0); Reject(bad, "missing case"); }
        { var bad = Fresh(); var rows = L(bad["cases"]); rows.Add(rows[0]); Reject(bad, "duplicate case"); }
        { var bad = Fresh(); O(Case(bad, 0)["setup"])["state"] = new Dictionary<string, object?>(); Reject(bad, "restored state in setup"); }
        { var bad = Fresh(); O(L(O(Case(bad, 0)["setup"])["deck"])[0])["growth"] = 3L; Reject(bad, "produced card state in deck"); }
        { var bad = Fresh(); O(Case(bad, 0)["created"])["stateHash"] = new string('0', 64); Reject(bad, "stale created hash"); }
        { var bad = Fresh(); O(Step(bad, 0, 1)["after"])["stateHash"] = new string('0', 64); Reject(bad, "stale after hash"); }
        { var bad = Fresh(); O(Forecast(bad, 0, 0, 0)["expected"])["cost"] = 1L; Reject(bad, "rejected forecast with a cost"); }
        { var bad = Fresh(); Forecast(bad, 0, 1, 1)["hiddenBy"] = new List<object?> { "rng" }; Reject(bad, "visible forecast claiming a hidden reason"); }
        { var bad = Fresh(); O(Forecast(bad, 0, 1, 1)["args"])["state"] = "c1"; Reject(bad, "extra forecast argument"); }
        { var bad = Fresh(); O(bad["counts"])["forecasts"] = 69L; Reject(bad, "changed denominator"); }
        { var bad = Fresh(); L(O(bad["coverage"])["errors"]).RemoveAt(0); Reject(bad, "dropped error coverage"); }
        { var bad = Fresh(); O(O(bad["reachability"])["notCovered"]).Clear(); Reject(bad, "hidden NOT_COVERED category"); }
        { var bad = Fresh(); Case(bad, 8)["setup"] = new Dictionary<string, object?>(O(Case(bad, 8)["setup"]), StringComparer.Ordinal) { ["seed"] = 4243L }; Reject(bad, "changed expedition setup"); }
        { var bad = Fresh(); L(Case(bad, 8)["steps"]).RemoveAt(5); Reject(bad, "truncated expedition policy"); }
        var actual = Actual["damage-block-targets/turn1 sera_round cog"];
        Check(JsonGraphComparison.Difference(new Dictionary<string, object?>(actual, StringComparer.Ordinal) { ["enemyHpLoss"] = 9 }, actual, "$", 0) != null,
            "a forged expected number is reported against the actual C# forecast");
    }

    // ───────── helpers ─────────
    static Dictionary<string, object?> Catalog(BattleData data) => new(StringComparer.Ordinal)
    {
        ["cards"] = data.Cards.Select(c => c.ToJsonGraph()).ToList(), ["characters"] = data.Characters.Select(c => c.ToJsonGraph()).ToList(),
        ["weapons"] = data.Weapons.Select(w => w.ToJsonGraph()).ToList(), ["parts"] = data.Parts.Select(p => p.ToJsonGraph()).ToList(), ["relics"] = data.Relics.Select(r => r.ToJsonGraph()).ToList()
    };
    static void RequireKeys(JsonElement json, string[] names, string label)
    {
        Require(json.ValueKind == JsonValueKind.Object, label + " (object required)");
        var actual = json.EnumerateObject().Select(p => p.Name).ToList();
        Require(actual.Count == names.Length && actual.ToHashSet(StringComparer.Ordinal).SetEquals(names), label);
    }
    static List<JsonElement> Items(JsonElement json, string label)
    { Require(json.ValueKind == JsonValueKind.Array, label + " must be an array"); return json.EnumerateArray().ToList(); }
    static bool Throws(Action action, string code) { try { action(); return false; } catch (RuleException error) { return error.Code == code; } }
    static void Same(JsonElement expected, object? actual, string label) => SameGraph(BattleDataLoader.Graph(expected), actual, label);
    static void SameGraph(object? expected, object? actual, string label) { string? diff = JsonGraphComparison.Difference(expected, actual, "$", 0); Check(diff == null, label + (diff == null ? "" : ": " + diff)); }
    static void RequireSame(JsonElement expected, object? actual, string label) => Require(JsonGraphComparison.Difference(BattleDataLoader.Graph(expected), actual, "$", 0) == null, label);
    static Dictionary<string, object?> O(object? value) => value as Dictionary<string, object?> ?? throw new InvalidDataException("Expected graph object");
    static List<object?> L(object? value) => value as List<object?> ?? throw new InvalidDataException("Expected graph array");
    static string Text(JsonElement json, string key) => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
        ? (string)BattleDataLoader.Graph(value)! : throw new InvalidDataException("Expected string " + key);
    static string? OptionalText(JsonElement json, string key) => json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? (string)BattleDataLoader.Graph(value)! : null;
    static int Int(JsonElement json) => json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out int value) ? value : throw new InvalidDataException("Expected Int32");
    static bool Bool(JsonElement json) => json.ValueKind is JsonValueKind.True or JsonValueKind.False ? json.GetBoolean() : throw new InvalidDataException("Expected boolean");
    static string Full(object? graph) => JsonSerializer.Serialize(graph);
    static string FileHash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    static string HashText(JsonElement json)
    {
        string hash = json.ValueKind == JsonValueKind.String ? json.GetString()! : "";
        Require(hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Malformed preview hash"); return hash;
    }
}
