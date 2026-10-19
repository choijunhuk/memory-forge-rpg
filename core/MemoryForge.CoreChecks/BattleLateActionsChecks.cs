// P1-CSHARP-LATE-ENEMY-ACTIONS §5.3–5.7/§10.4–10.6/§16.2: actual C# Battle.Create and Command(begin/play/ult/end) replays of
// apDrain/sealSlot/delayRes/silence/eatName/phase against the original-JS oracle. Late behavior comes only from the unchanged candidate
// overlay (proposals/late_enemies.cjs) and fixed test-local boundary enemies; recorded states are comparison data, never engine input.
// Production Load(root) stays the live 20 enemies; Run/Unity export and activation, balance and server acceptance are NOT approved here.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MemoryForge.Core;

static class BattleLateActionsChecks
{
    const string Schema = "mf-battle-late-actions-v1", Status = "JS_ORACLE_CANDIDATE_ONLY";
    const string Scope = "Fresh Battle.create and real begin/play/ult/end commands for apDrain/sealSlot/delayRes/silence/eatName/phase on the unchanged candidate overlay and fixed test-local boundary enemies; live content, Run/Unity activation, balance and server acceptance NOT_APPROVED";
    // Exact allowlist in source order; only these fixed paths are ever hashed, so a fixture can never steer the host to another file.
    static readonly (string Key, string Path)[] Sources = { ("data", "data/game_data.json"), ("engine", "engine/rules-core.js"), ("memory", "proposals/memory_v2.cjs"),
        ("canonical", "tools/gen_traces.mjs"), ("proposal", "proposals/late_enemies.cjs"), ("generator", "tools/gen_battle_late_actions_vectors.mjs") };
    static readonly string[] Behaviors = { "apDrain", "sealSlot", "delayRes", "silence", "eatName", "phase" };
    static readonly string[] Candidates = ("e_debtwraith e_scaleknight e_sealgolem e_ratpack e_overdue e_collector e_notarytwin b_usurer e_choir e_stagerg e_shadowlamp " +
        "e_ghostguest e_prompter e_baton e_primadonna b_overture e_springguard e_pendulum e_rustgear e_timeshard e_echo e_archbell b_nameless").Split(' ');
    // Fixed denominators measured from this producer (RULES_NOW_PLAY regeneration: five reservation-only seal/delay edges dropped, the rest replay
    // as now plays): 23 duels + 23 encounter groups + 29 boundaries + 1 protocol = 76 cases / 437 commands.
    static readonly (string Key, int Value)[] Counts = { ("cases", 76), ("commands", 437), ("committed", 415), ("replayed", 2), ("rejected", 20) };
    static readonly (string Key, int Value)[] Coverage = { ("candidates", 23), ("encounters", 23), ("edges", 29), ("protocols", 1) };
    // RULES_NOW_PLAY: no slot exists any more, so SLOT_FULL and NO_EMPTY_SLOT are unreachable.
    static readonly string[] Errors = { "AP_LOW", "WRONG_PHASE", "INVALID_ENEMY_MOVE", "IDEMPOTENCY_CONFLICT", "STALE_VERSION" };
    // Boundary cases in producer order: dataset, exact enemy list, request count and real rejections.
    static readonly (string Id, string Dataset, string Enemies, int Commands, int Rejected)[] Edges =
    {
        ("drain-debt-clamp", "boundary", "t_drain", 6, 1), ("silence-free-next", "candidate", "e_choir e_choir", 10, 0),
        ("silence-free-instance", "candidate", "e_choir", 8, 0), ("seal-no-reservation", "candidate", "e_sealgolem", 8, 0),
        ("seal-after-collateral-debt", "candidate", "e_sealgolem", 5, 0), ("seal-two-sealers", "candidate", "e_sealgolem e_timeshard", 5, 0),
        ("delay-no-reservation", "candidate", "e_prompter", 6, 0), ("eat-token-priority", "candidate", "e_overdue e_echo", 5, 0),
        ("eat-empty-discard", "candidate", "e_echo", 3, 0), ("eat-exhaust-hooks", "candidate", "e_echo", 4, 0),
        ("nameless-kill-hooks", "candidate", "b_nameless", 6, 1), ("nameless-multihit", "candidate", "b_nameless", 3, 1),
        ("nameless-phase2-cycles", "candidate", "b_nameless", 14, 0), ("phase-counter-multihit", "boundary", "t_phase_multihit", 4, 0),
        ("phase-reflect-defeat", "boundary", "t_phase_reflect", 4, 1), ("phase-invalid-bad-t", "boundary", "t_phase_bad_t", 3, 1),
        ("phase-invalid-bad-hp", "boundary", "t_phase_bad_hp", 3, 1), ("phase-invalid-zero-brk", "boundary", "t_phase_zero_brk", 3, 1),
        ("phase-invalid-dup-weak", "boundary", "t_phase_dup_weak", 3, 1), ("phase-invalid-unknown-weak", "boundary", "t_phase_unknown_weak", 3, 1),
        ("phase-invalid-empty-weak", "boundary", "t_phase_empty_weak", 3, 1), ("phase-invalid-missing-weak", "boundary", "t_phase_missing_weak", 3, 1),
        ("phase-invalid-empty-pattern", "boundary", "t_phase_empty_pattern", 3, 1), ("phase-invalid-nested-phase", "boundary", "t_phase_nested_phase", 3, 1),
        ("phase-invalid-not-boss", "boundary", "t_phase_not_boss", 3, 1), ("move-invalid-drain-negative", "boundary", "t_bad_drain_negative", 2, 1),
        ("move-invalid-drain-missing", "boundary", "t_bad_drain_missing", 2, 1), ("move-invalid-silence-count", "boundary", "t_bad_silence", 2, 1),
        ("late-terminal-defeat", "boundary", "e_choir e_collector e_sealgolem e_echo t_smasher", 3, 1)
    };
    static readonly (string Edge, string Code)[] InvalidPhases = { ("phase-invalid-bad-t", "INVALID_ENEMY_MOVE"), ("phase-invalid-bad-hp", "INVALID_ENEMY_MOVE"),
        ("phase-invalid-zero-brk", "INVALID_ENEMY_MOVE"), ("phase-invalid-dup-weak", "INVALID_ENEMY_MOVE"), ("phase-invalid-unknown-weak", "INVALID_ENEMY_MOVE"),
        ("phase-invalid-empty-weak", "INVALID_ENEMY_MOVE"), ("phase-invalid-missing-weak", "INVALID_ENEMY_MOVE"), ("phase-invalid-empty-pattern", "INVALID_ENEMY_MOVE"),
        ("phase-invalid-nested-phase", "INVALID_ENEMY_MOVE"), ("phase-invalid-not-boss", "WRONG_PHASE") };

    // Independent transcriptions of the pinned proposal (proposals/late_enemies.cjs) and of the pinned producer's test-local enemies.
    const string ProvenanceJson = """
        {"status":"candidate-only","profiles":"docs/design_v0.8.1.md §10.4","encounterGroups":"review/sources/external_review_v0.7.md §6.1 (unverified proposal)",
         "notes":"No narrative/client readiness or balance approval. Existing lowest-HP heal semantics retained; phase2 descriptor normalized to phase without changing numbers."}
        """;
    const string CandidateJson = """
        [
         {"id":"e_debtwraith","name":"채무 망령","zone":3,"tier":"normal","hp":38,"weak":["frost","void"],"brk":4,"pattern":[{"t":"atk","d":12},{"t":"apDrain","n":1},{"t":"atk","d":16}]},
         {"id":"e_scaleknight","name":"저울 기사","zone":3,"tier":"normal","hp":52,"weak":["volt","flame"],"brk":5,"pattern":[{"t":"def","b":12},{"t":"atk","d":22},{"t":"atk","d":10,"h":2}]},
         {"id":"e_sealgolem","name":"인장 골렘","zone":3,"tier":"normal","hp":46,"weak":["flame","frost"],"brk":4,"pattern":[{"t":"sealSlot"},{"t":"atk","d":24}]},
         {"id":"e_ratpack","name":"이자 쥐떼","zone":3,"tier":"normal","hp":18,"weak":["flame"],"brk":2,"pattern":[{"t":"atk","d":6,"h":2},{"t":"buff","str":1}]},
         {"id":"e_overdue","name":"연체 서류","zone":3,"tier":"normal","hp":35,"weak":["volt","void"],"brk":3,"pattern":[{"t":"insert","card":"penalty_note","n":1},{"t":"atk","d":18}]},
         {"id":"e_collector","name":"추심인","zone":3,"tier":"elite","hp":110,"weak":["frost","volt"],"brk":8,
          "pattern":[{"t":"apDrain","n":2},{"t":"charge","label":"추심 집행 준비"},{"t":"atk","d":52,"label":"추심 집행"},{"t":"def","b":15}]},
         {"id":"e_notarytwin","name":"공증 거울 쌍","zone":3,"tier":"elite","hp":55,"weak":["void","flame"],"brk":4,"pattern":[{"t":"reflect","n":4,"label":"쌍둥이 거울"},{"t":"atk","d":14,"h":2},{"t":"heal","n":8}]},
         {"id":"b_usurer","name":"이자를 먹는 자","zone":3,"tier":"boss","hp":260,"weak":["void","frost"],"brk":13,
          "pattern":[{"t":"sealSlot"},{"t":"atk","d":22},{"t":"apDrain","n":2},{"t":"charge","label":"파산 선고 준비"},{"t":"atk","d":58,"label":"파산 선고"}]},
         {"id":"e_choir","name":"침묵 합창단원","zone":4,"tier":"normal","hp":40,"weak":["volt","flame"],"brk":4,"pattern":[{"t":"silence","n":1},{"t":"atk","d":18}]},
         {"id":"e_stagerg","name":"무대 장치","zone":4,"tier":"normal","hp":58,"weak":["frost"],"brk":5,"pattern":[{"t":"def","b":15},{"t":"atk","d":26}]},
         {"id":"e_shadowlamp","name":"그림자 조명","zone":4,"tier":"normal","hp":42,"weak":["void","volt"],"brk":4,"pattern":[{"t":"debuff","s":"weak","n":1},{"t":"atk","d":12,"h":2}]},
         {"id":"e_ghostguest","name":"객석 유령","zone":4,"tier":"normal","hp":22,"weak":["flame","void"],"brk":2,"pattern":[{"t":"atk","d":9,"h":2}]},
         {"id":"e_prompter","name":"프롬프터","zone":4,"tier":"normal","hp":45,"weak":["frost","flame"],"brk":4,"pattern":[{"t":"delayRes"},{"t":"atk","d":20}]},
         {"id":"e_baton","name":"지휘봉 그림자","zone":4,"tier":"elite","hp":125,"weak":["flame","volt"],"brk":9,
          "pattern":[{"t":"silence","n":1},{"t":"atk","d":16,"h":3},{"t":"charge","label":"종막 지휘"},{"t":"atk","d":56,"label":"포르티시모"}]},
         {"id":"e_primadonna","name":"프리마돈나의 잔상","zone":4,"tier":"elite","hp":120,"weak":["frost","void"],"brk":9,"pattern":[{"t":"reflect","n":5,"label":"유리 아리아"},{"t":"atk","d":28},{"t":"heal","n":15}]},
         {"id":"b_overture","name":"끝나지 않는 서곡","zone":4,"tier":"boss","hp":290,"weak":["volt","frost"],"brk":14,
          "pattern":[{"t":"silence","n":1},{"t":"atk","d":24},{"t":"delayRes"},{"t":"charge","label":"무음의 절정"},{"t":"atk","d":62,"label":"종언의 화음"}]},
         {"id":"e_springguard","name":"태엽 파수꾼","zone":5,"tier":"normal","hp":55,"weak":["void","flame"],"brk":5,"pattern":[{"t":"def","b":18},{"t":"atk","d":28}]},
         {"id":"e_pendulum","name":"진자 칼날","zone":5,"tier":"normal","hp":48,"weak":["frost"],"brk":4,"pattern":[{"t":"atk","d":14,"h":2},{"t":"atk","d":24}]},
         {"id":"e_rustgear","name":"녹슨 톱니 떼","zone":5,"tier":"normal","hp":20,"weak":["volt"],"brk":2,"pattern":[{"t":"atk","d":10,"h":2}]},
         {"id":"e_timeshard","name":"시간 조각","zone":5,"tier":"normal","hp":45,"weak":["flame","void"],"brk":5,"pattern":[{"t":"sealSlot"},{"t":"atk","d":26}]},
         {"id":"e_echo","name":"이름 없는 메아리","zone":5,"tier":"normal","hp":50,"weak":["volt","frost"],"brk":4,"pattern":[{"t":"eatName"},{"t":"atk","d":22}]},
         {"id":"e_archbell","name":"종지기 대장","zone":5,"tier":"elite","hp":140,"weak":["frost","flame"],"brk":10,
          "pattern":[{"t":"charge","label":"자정의 타종 준비"},{"t":"atk","d":60,"label":"자정의 타종"},{"t":"sealSlot"},{"t":"atk","d":18,"h":2}]},
         {"id":"b_nameless","name":"무명","zone":5,"tier":"boss","hp":170,"weak":["void","volt"],"brk":15,
          "pattern":[{"t":"eatName"},{"t":"atk","d":26},{"t":"apDrain","n":2},{"t":"charge","label":"공백 집적"},{"t":"atk","d":70,"label":"망각의 낙인"}],
          "phase":{"t":"phase","hp":170,"brk":15,"weak":["flame","frost"],
           "pattern":[{"t":"sealSlot"},{"t":"atk","d":18,"h":3},{"t":"delayRes"},{"t":"silence","n":1},{"t":"charge","label":"자정의 침묵"},{"t":"atk","d":70,"label":"종언의 자정"}]}}
        ]
        """;
    const string EncounterJson = """
        {"3":{"normal":[["e_debtwraith","e_overdue"],["e_scaleknight"],["e_sealgolem","e_ratpack"],["e_ratpack","e_ratpack","e_ratpack"],["e_debtwraith","e_sealgolem"]],
              "elite":[["e_collector"],["e_notarytwin","e_notarytwin"]],"boss":[["b_usurer"]]},
         "4":{"normal":[["e_choir","e_shadowlamp"],["e_stagerg","e_ghostguest"],["e_prompter","e_choir"],["e_ghostguest","e_ghostguest","e_ghostguest"],["e_stagerg","e_prompter"]],
              "elite":[["e_baton"],["e_primadonna"]],"boss":[["b_overture"]]},
         "5":{"normal":[["e_springguard","e_rustgear"],["e_pendulum","e_timeshard"],["e_echo","e_timeshard"],["e_rustgear","e_rustgear","e_rustgear"],["e_springguard","e_echo"]],
              "elite":[["e_archbell"]],"boss":[["b_nameless"]]}}
        """;
    const string BoundaryJson = """
        [
         {"id":"t_drain","name":"시험 적","tier":"normal","hp":200,"brk":20,"weak":["frost"],"pattern":[{"t":"apDrain","n":1},{"t":"apDrain","n":6},{"t":"charge"}]},
         {"id":"t_smasher","name":"시험 적","tier":"normal","hp":200,"brk":20,"weak":["frost"],"pattern":[{"t":"atk","d":99}]},
         {"id":"t_phase_multihit","name":"시험 적","tier":"boss","hp":1,"brk":20,"weak":["frost"],"pattern":[{"t":"atk","d":10,"h":3}],
          "phase":{"t":"phase","hp":100,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}]}},
         {"id":"t_phase_reflect","name":"시험 적","tier":"boss","hp":1,"brk":20,"weak":["frost"],"pattern":[{"t":"reflect","n":50},{"t":"charge"}],
          "phase":{"t":"phase","hp":30,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}]}},
         {"id":"t_phase_bad_t","name":"시험 적","tier":"boss","hp":1,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}],
          "phase":{"t":"charge","hp":30,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}]}},
         {"id":"t_phase_bad_hp","name":"시험 적","tier":"boss","hp":1,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}],
          "phase":{"t":"phase","hp":-1,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}]}},
         {"id":"t_phase_zero_brk","name":"시험 적","tier":"boss","hp":1,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}],
          "phase":{"t":"phase","hp":30,"brk":0,"weak":["frost"],"pattern":[{"t":"charge"}]}},
         {"id":"t_phase_dup_weak","name":"시험 적","tier":"boss","hp":1,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}],
          "phase":{"t":"phase","hp":30,"brk":20,"weak":["volt","volt"],"pattern":[{"t":"charge"}]}},
         {"id":"t_phase_unknown_weak","name":"시험 적","tier":"boss","hp":1,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}],
          "phase":{"t":"phase","hp":30,"brk":20,"weak":["shadow"],"pattern":[{"t":"charge"}]}},
         {"id":"t_phase_empty_weak","name":"시험 적","tier":"boss","hp":1,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}],
          "phase":{"t":"phase","hp":30,"brk":20,"weak":[],"pattern":[{"t":"charge"}]}},
         {"id":"t_phase_missing_weak","name":"시험 적","tier":"boss","hp":1,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}],
          "phase":{"t":"phase","hp":30,"brk":20,"pattern":[{"t":"charge"}]}},
         {"id":"t_phase_empty_pattern","name":"시험 적","tier":"boss","hp":1,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}],
          "phase":{"t":"phase","hp":30,"brk":20,"weak":["frost"],"pattern":[]}},
         {"id":"t_phase_nested_phase","name":"시험 적","tier":"boss","hp":1,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}],
          "phase":{"t":"phase","hp":30,"brk":20,"weak":["frost"],"pattern":[{"t":"phase"}]}},
         {"id":"t_phase_not_boss","name":"시험 적","tier":"normal","hp":1,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}],
          "phase":{"t":"phase","hp":30,"brk":20,"weak":["frost"],"pattern":[{"t":"charge"}]}},
         {"id":"t_bad_drain_negative","name":"시험 적","tier":"normal","hp":200,"brk":20,"weak":["frost"],"pattern":[{"t":"apDrain","n":-1}]},
         {"id":"t_bad_drain_missing","name":"시험 적","tier":"normal","hp":200,"brk":20,"weak":["frost"],"pattern":[{"t":"apDrain"}]},
         {"id":"t_bad_silence","name":"시험 적","tier":"normal","hp":200,"brk":20,"weak":["frost"],"pattern":[{"t":"silence","n":2}]}
        ]
        """;
    const string ProtocolJson = """
        [{"rid":"protocol-late-begin","v":0,"kind":"begin","args":{}},{"rid":"protocol-late-end","v":1,"kind":"end","args":{}},
         {"rid":"protocol-late-end","v":1,"kind":"end","args":{}},{"rid":"protocol-late-end","v":1,"kind":"end","args":{"changed":true}},
         {"rid":"protocol-late-stale","v":1,"kind":"end","args":{}},{"rid":"protocol-late-next","v":2,"kind":"end","args":{}},
         {"rid":"protocol-late-begin","v":0,"kind":"begin","args":{}}]
        """;

    sealed class StepResult
    {
        public string Outcome = "";
        public string? Code;
        public BattleState State = new BattleState();
        public Dictionary<string, object?> View = new Dictionary<string, object?>();
    }

    static int checks, failures, requests, committed, replayed, rejected, receiptProbes, mutationProbes;
    static void Check(bool ok, string label) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL BattleLateActions " + label); } }
    static void Require(bool ok, string label) { if (!ok) throw new InvalidDataException(label); }

    public static int Run(string root)
    {
        checks = failures = requests = committed = replayed = rejected = receiptProbes = mutationProbes = 0;
        try
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, "engine", "tests", "vectors", "battle_late_actions_vectors.json.gz"));
            using var document = RunCreateChecks.Decode(bytes);
            var fixture = document.RootElement; RunCreateChecks.UniqueKeys(fixture);
            using var content = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
            ValidateFixture(fixture, content.RootElement, root);
            Check(true, "strict single-member gzip/fatal UTF-8/unique-key fixture, six pinned sources, exact overlay/test-local transcriptions, case identity, setups, request sequences, checkpoint hashes and fixed denominators");
            DecoderControls(bytes);
            var overlay = fixture.GetProperty("overlay").GetProperty("enemies"); var boundary = fixture.GetProperty("boundaryEnemies");
            var live = BattleDataLoader.Load(root);
            var sets = new Dictionary<string, BattleData>(StringComparer.Ordinal)
            { ["candidate"] = BattleDataLoader.LoadWithEnemies(root, overlay), ["boundary"] = BattleDataLoader.LoadWithEnemies(root, overlay, boundary) };
            TypedDefinitions(live, sets, overlay, boundary);
            var prints = sets.ToDictionary(p => p.Key, p => DefinitionPrint(p.Value), StringComparer.Ordinal);
            var actual = new Dictionary<string, List<StepResult>>(StringComparer.Ordinal);
            foreach (var row in fixture.GetProperty("cases").EnumerateArray())
            {
                string id = Text(row, "id"); actual.Add(id, Replay(row, sets[Text(row, "dataset")], id));
            }
            Check(sets.All(p => DefinitionPrint(p.Value) == prints[p.Key]), "every replay leaves the typed candidate and boundary definitions unchanged");
            Check(actual.Count == 76 && requests == 437 && committed == 415 && replayed == 2 && rejected == 20,
                "exactly 76 cases / 437 actual C# requests: 415 commits, 2 receipt replays, 20 real rejections");
            IndependentAssertions(actual);
            LocalProbes(root, overlay, boundary);
            FixtureRejections(fixture, content.RootElement, root);
        }
        catch (Exception error) { Check(false, "fixture/execution: " + error); }
        Console.WriteLine($"BattleLateActionsChecks: {checks - failures}/{checks} passed; {requests} actual requests, {committed} committed, {replayed} replayed, {rejected} rejected; {receiptProbes} receipt probes, {mutationProbes} detached-copy probes");
        Console.WriteLine("BattleLateActionsScope: six late behaviors on the unchanged candidate overlay + fixed test-local boundaries only; production Load(root) stays live 20; Run/Unity export/activation, balance, device and server acceptance NOT_APPROVED/PENDING");
        Console.WriteLine("BattleLateActionsNotShared: C#-only BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED (pattern-held phase, unknown or extra-member moves) and BATTLE_NUMBER_OUT_OF_RANGE (Int32 drain overflow) are local probes outside the JS oracle");
        return failures;
    }

    // ───────── actual execution ─────────
    static List<StepResult> Replay(JsonElement row, BattleData data, string id)
    {
        var battle = Battle.Create(data, BattleDataLoader.Setup(row.GetProperty("setup")));
        CompareCheckpoint(row.GetProperty("created"), battle, id + " created");
        var results = new List<StepResult>(); var stored = new List<(BattleRequest Request, string Response)>();
        int index = 0;
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            string label = id + " step " + index++; requests++;
            var request = BattleDataLoader.Request(step.GetProperty("request"));
            var before = battle.State; string beforeState = Full(before.ToJsonGraph()), beforeView = Full(battle.PublicView());
            string outcome = Text(step, "outcome"); string? code = null;
            if (outcome == "rejected")
            {
                code = Text(step, "errorCode"); rejected++;
                Check(Throws(() => battle.Command(request), code), label + " real command rejects with exactly " + code);
                Check(Full(battle.State.ToJsonGraph()) == beforeState && Full(battle.PublicView()) == beforeView, label + " rejection is atomic (state/RNG/log/events/version/public view)");
                if (!string.IsNullOrEmpty(request.Rid) && !stored.Any(s => s.Request.Rid == request.Rid))
                {
                    receiptProbes++;
                    Check(Throws(() => battle.Command(new BattleRequest(request.Rid, before.V, "unknown-receipt-probe")), "UNKNOWN_COMMAND") && Full(battle.State.ToJsonGraph()) == beforeState,
                        label + " the rejected request stored no receipt under its ID");
                }
            }
            else
            {
                var response = battle.Command(request); var graph = response.ToJsonGraph();
                Same(step.GetProperty("response"), graph, label + " complete response/phase/version/events");
                Check(CanonicalState.Hash(graph["ev"]) == HashText(step.GetProperty("responseEventsHash")), label + " response event hash");
                if (outcome == "replayed") { replayed++; Check(Full(battle.State.ToJsonGraph()) == beforeState, label + " receipt replay executes nothing"); }
                else
                {
                    committed++;
                    Check(response.V == before.V + 1 && battle.State.V == response.V && battle.State.Phase == response.Phase, label + " commits exactly one new version");
                    if (!string.IsNullOrEmpty(request.Rid)) stored.Add((request, Full(graph)));
                }
                response.Events.Clear(); response.Phase = "caller-mutation"; // the stored receipt must not alias the returned response
            }
            CompareCheckpoint(step, battle, label);
            MutationProbe(battle, label);
            results.Add(new StepResult { Outcome = outcome, Code = code, State = battle.State, View = battle.PublicView() });
        }
        string final = Full(battle.State.ToJsonGraph());
        foreach (var (request, response) in stored)
        { receiptProbes++; Check(Full(battle.Command(request).ToJsonGraph()) == response, id + " stored receipt " + request.Rid + " replays its original response"); }
        Check(Full(battle.State.ToJsonGraph()) == final, id + " receipt replays execute nothing");
        return results;
    }

    static void CompareCheckpoint(JsonElement point, Battle battle, string label)
    {
        var state = battle.State; var graph = state.ToJsonGraph(); var view = battle.PublicView();
        Same(point.GetProperty("state"), graph, label + " complete state incl. log/events/RNG/version/second-body keys");
        Same(point.GetProperty("publicView"), view, label + " complete public view");
        foreach (var (value, hash) in new (object?, string)[] { (graph, "stateHash"), (state.Rng, "rngHash"), (graph["ev"], "eventsHash"), (graph["log"], "logHash"), (view, "publicViewHash") })
            Check(CanonicalState.Hash(value) == HashText(point.GetProperty(hash)), label + " " + hash);
        Check(!view.Keys.Intersect(new[] { "rng", "cards", "zones", "draw" }).Any(), label + " public view hides RNG/cards/zones/draw order");
        var zones = state.Zones.Draw.Concat(state.Zones.Hand).Concat(state.Zones.Discard).Concat(state.Zones.Exhaust).Concat(state.Reservations.Select(r => r.Uid)).ToList();
        Check(zones.Count == state.Cards.Count && zones.Distinct(StringComparer.Ordinal).Count() == zones.Count && zones.All(state.Cards.ContainsKey),
            label + " card conservation incl. delayed, discarded and eaten cards");
    }

    // Every returned snapshot and public graph, including the second body's pattern copies, must be detached from live state.
    static void MutationProbe(Battle battle, string label)
    {
        string state = Full(battle.State.ToJsonGraph()), publicView = Full(battle.PublicView());
        var snapshot = battle.State;
        snapshot.ApDrainNext = 99; snapshot.SilenceUntil = 99; snapshot.SealedUntil = 99; snapshot.Rng[0] ^= 1; snapshot.Log.Clear(); snapshot.Events.Clear();
        foreach (var enemy in snapshot.Enemies)
        {
            enemy.PhaseIndex = 7; enemy.Hp = -1; enemy.Weak.Clear(); enemy.Status.Reflect = 99;
            if (enemy.Intent != null) enemy.Intent.Type = "caller-mutation";
            if (enemy.Pattern != null) { foreach (var move in enemy.Pattern) move.Type = "caller-mutation"; enemy.Pattern.Clear(); }
        }
        foreach (var reservation in snapshot.Reservations) { reservation.Delayed = false; reservation.Due = -1; reservation.Kind = "caller-mutation"; }
        var view = battle.PublicView(); view["apDrainNext"] = 99; view["silenceUntil"] = 99;
        foreach (var item in (IList)view["enemies"]!)
        {
            var enemy = (Dictionary<string, object?>)item!; enemy["phaseIndex"] = 9;
            if (enemy["intent"] is Dictionary<string, object?> intent) intent["t"] = "caller-mutation";
            if (enemy.TryGetValue("pattern", out var pattern) && pattern is IList moves)
            {
                foreach (var move in moves) ((Dictionary<string, object?>)move!)["t"] = "caller-mutation";
                moves.Clear();
            }
        }
        ((IList)view["res"]!).Clear();
        mutationProbes++;
        Check(Full(battle.State.ToJsonGraph()) == state && Full(battle.PublicView()) == publicView, label + " mutated snapshot/public graphs (incl. phase pattern copies) leave live state unchanged");
    }

    // Typed definitions keep every source member and every absence; "zone" is the one named typed limit (overlay metadata Battle never reads).
    static void TypedDefinitions(BattleData live, Dictionary<string, BattleData> sets, JsonElement overlay, JsonElement boundary)
    {
        var late = new HashSet<string>(Behaviors, StringComparer.Ordinal);
        bool Late(EnemyMoveDef move) => late.Contains(move.Type) || move.Then != null && Late(move.Then);
        Check(live.Enemies.Count == 20 && live.Enemies.All(e => e.PhaseDescriptor() == null && !e.Pattern.Any(Late))
            && !live.Enemies.Any(e => Candidates.Contains(e.Id) || e.Id.StartsWith("t_", StringComparison.Ordinal)),
            "production Load(root) is still the live 20 enemies with no phase descriptor or late move (no candidate activation)");
        var liveIds = live.Enemies.Select(e => e.Id).ToList();
        foreach (var (key, sources) in new[] { ("candidate", new[] { overlay }), ("boundary", new[] { overlay, boundary }) })
        {
            var data = sets[key]; var added = data.Enemies.Skip(liveIds.Count).ToList(); var source = sources.SelectMany(s => s.EnumerateArray()).ToList();
            Check(data.Enemies.Take(liveIds.Count).Select(e => e.Id).SequenceEqual(liveIds) && added.Count == source.Count && added.Count == (key == "candidate" ? 23 : 40),
                key + " data appends exactly its overlay after the unchanged live definitions");
            for (int i = 0; i < Math.Min(added.Count, source.Count); i++)
            {
                var expected = (Dictionary<string, object?>)BattleDataLoader.Graph(source[i])!; expected.Remove("zone");
                SameGraph(expected, TypedEnemy(added[i]), key + " typed " + added[i].Id + " keeps every number, label, weakness, pattern and phase member/absence");
            }
        }
    }
    static Dictionary<string, object?> TypedEnemy(BattleEnemyDef enemy)
    {
        var graph = new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = enemy.Id, ["name"] = enemy.Name, ["tier"] = enemy.Tier, ["hp"] = enemy.Hp,
            ["brk"] = enemy.Brk, ["weak"] = new List<string>(enemy.Weak), ["pattern"] = enemy.Pattern.Select(m => (object?)m.ToJsonGraph()).ToList() };
        if (enemy.Passive != null) graph["passive"] = enemy.Passive.ToJsonGraph();
        var phase = enemy.PhaseDescriptor();
        if (phase == null) return graph;
        var descriptor = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (phase.Type != null) descriptor["t"] = phase.Type;
        if (phase.Hp is int hp) descriptor["hp"] = hp;
        if (phase.Brk is int brk) descriptor["brk"] = brk;
        if (phase.Weak != null) descriptor["weak"] = new List<string>(phase.Weak);
        if (phase.Pattern != null) descriptor["pattern"] = phase.Pattern.Select(m => (object?)m.ToJsonGraph()).ToList();
        graph["phase"] = descriptor; return graph;
    }
    static string DefinitionPrint(BattleData data) => Full(data.Enemies.Select(TypedEnemy).ToList());

    // ───────── independent literals derived from the unchanged original sources ─────────
    static void IndependentAssertions(Dictionary<string, List<StepResult>> actual)
    {
        BattleState S(string edge, int step) => actual["edge-" + edge][step].State;
        string? Code(string edge, int step) => actual["edge-" + edge][step].Code;
        List<BattleEvent> Of(string edge, int step, string kind) => S(edge, step).Events.Where(e => e.E == kind).ToList();
        int? Cost(string edge, int step) => Of(edge, step, "play").First().Cost;
        string Played(string edge, int step) => Of(edge, step, "play").First().Uid ?? "";
        (int?, bool?)[] Seals(string edge, int step) => Of(edge, step, "sealSlot").Select(e => (e.Until, e.Fallback)).ToArray();

        // apDrain: loan AP6; drain1 + due debt2 -> AP1; drain6 clamps AP0 (a 1-cost play is AP_LOW); the drain is consumed once -> AP4.
        var drained = S("drain-debt-clamp", 2);
        Check(new[] { 0, 1, 2, 3, 5 }.Select(i => S("drain-debt-clamp", i).Ap).SequenceEqual(new[] { 4, 6, 1, 0, 4 }) && Code("drain-debt-clamp", 4) == "AP_LOW"
            && drained.Turn == 2 && drained.ApDrainNext == 0 && drained.Debts.Count == 0 && Of("drain-debt-clamp", 2, "apDrain").Select(e => e.N).SequenceEqual(new int?[] { 1 })
            && Of("drain-debt-clamp", 2, "repay").Select(e => e.N).SequenceEqual(new int?[] { 2 }) && Of("drain-debt-clamp", 3, "apDrain").Select(e => e.N).SequenceEqual(new int?[] { 6 })
            && S("drain-debt-clamp", 5).ApDrainNext == 0, "independent apDrain apart from debt, one-time consumption and clamp");
        // silence: non-stacking, expiry after the silenced turn, then reapplication. RULES_NOW_PLAY makes every play now, so its next/after
        // surcharge never applies, while freeNext and free instances stay 0.
        Check(new[] { 2, 4, 5, 7, 9 }.Select(i => Cost("silence-free-next", i)).SequenceEqual(new int?[] { 1, 0, 0, 1, 1 })
            && new[] { 1, 6, 8 }.Select(i => S("silence-free-next", i).SilenceUntil).SequenceEqual(new[] { 2, 0, 4 })
            && Of("silence-free-next", 1, "silence").Select(e => e.Until).SequenceEqual(new int?[] { 2, 2 }) && S("silence-free-next", 3).FreeNext == 2 && S("silence-free-next", 5).FreeNext == 0,
            "independent silence non-stacking, no surcharge on now plays, freeNext precedence, expiry and reapplication");
        string copy = Played("silence-free-instance", 3); var copied = S("silence-free-instance", 3).Cards[copy];
        Check(new[] { 1, 3, 4, 5, 6 }.Select(i => Cost("silence-free-instance", i)).SequenceEqual(new int?[] { 1, 0, 1, 1, 1 })
            && S("silence-free-instance", 2).SilenceUntil == 2 && S("silence-free-instance", 7).SilenceUntil == 0
            && copied.Free && copied.Token && copied.EchoCopy == true && S("silence-free-instance", 7).Zones.Exhaust.Contains(copy),
            "independent free copy costs 0 and now plays pay their authored cost under silence");
        // sealSlot: the following player turn's next column; an existing seal gives block 10. With no reservation it seals nothing a now play uses.
        Check(Seals("seal-no-reservation", 3).SequenceEqual(new (int?, bool?)[] { (2, false) }) && Seals("seal-no-reservation", 7).SequenceEqual(new (int?, bool?)[] { (4, false) })
            && Cost("seal-no-reservation", 4) == 1 && S("seal-no-reservation", 3).Reservations.Count == 0 && S("seal-no-reservation", 3).Enemies[0].Block == 0,
            "independent seal of the following turn changes no now-play cost and holds no reservation");
        var booked = S("seal-after-collateral-debt", 1);
        Check(booked.SealedUntil == 0 && booked.Debts.Select(d => (d.N, d.Due)).SequenceEqual(new[] { (1, 2) }) && Seals("seal-after-collateral-debt", 2).SequenceEqual(new (int?, bool?)[] { (2, false) })
            && S("seal-after-collateral-debt", 2).Ap == 3 && S("seal-after-collateral-debt", 2).Enemies[0].Block == 0,
            "independent collateral books debt1 instead of a seal, so the enemy seal is not a fallback and the debt repays next turn (AP3)");
        Check(Seals("seal-two-sealers", 1).SequenceEqual(new (int?, bool?)[] { (2, false), (2, true) }) && S("seal-two-sealers", 1).Enemies.Select(e => e.Block).SequenceEqual(new[] { 0, 10 }),
            "independent second same-phase sealer gains block 10");
        // delayRes with no reservation: a null event and no state change; the now-played sera_timed already resolved at its authored v20.
        var noDelay = Of("delay-no-reservation", 3, "delayRes");
        Check(noDelay.Count == 1 && noDelay[0].Uid == null && noDelay[0].ToJsonGraph().TryGetValue("uid", out var noUid) && noUid == null && noDelay[0].Discarded == false
            && S("delay-no-reservation", 3).Reservations.Count == 0 && Of("delay-no-reservation", 2, "dmg").Select(e => e.N).SequenceEqual(new int?[] { 20 }),
            "independent delayRes without a reservation emits a null-uid event and the now card resolved at v20");
        // eatName: tokens first through the replayed RNG; empty discard (an all-보존 hand discards nothing) -> explicit null uid and no RNG.
        var nothing = Of("eat-empty-discard", 1, "eatName");
        Check(nothing.Count == 1 && nothing[0].Uid == null && nothing[0].ToJsonGraph().TryGetValue("uid", out var none) && none == null
            && S("eat-empty-discard", 1).Rng.SequenceEqual(S("eat-empty-discard", 0).Rng) && S("eat-empty-discard", 1).Exhausted == 0 && S("eat-empty-discard", 1).Zones.Hand.Count == 2,
            "independent empty eatName: null uid, RNG untouched, the retained hand stays");
        var eaten = Enumerable.Range(1, 4).SelectMany(i => Of("eat-token-priority", i, "eatName").Select(e => S("eat-token-priority", i).Cards[e.Uid ?? ""])).ToList();
        Check(eaten.Count == 2 && eaten.All(c => c.Token && c.Id == "penalty_note") && S("eat-token-priority", 4).Exhausted == 2, "independent eatName takes the inserted penalty-note tokens first");
        var hooks = S("eat-exhaust-hooks", 2).Events;
        Check(S("eat-exhaust-hooks", 1).Ap == 4 && hooks.Count > 3 && hooks.Take(4).Select(e => (e.E, e.Source)).SequenceEqual(new (string, string?)[] { ("exhaust", null), ("block", "oblivion"), ("dmg", "relic"), ("eatName", null) })
            && S("eat-exhaust-hooks", 2).Exhausted == 2, "independent eaten card runs oblivion block and void-shard damage; ink AP only in a player phase");
        // phase: the first HP 0 is no kill; the final body's death runs the kill hooks.
        var body = S("nameless-kill-hooks", 2).Enemies[0];
        Check(S("nameless-kill-hooks", 0).Enemies[0].Hp == 2 && body.Alive && body.PhaseIndex == 1 && body.Hp == 2 && body.MaxHp == 2 && body.Brk == 15 && body.BrkMax == 15 && body.Status.Mark == 0
            && body.Weak.SequenceEqual(new[] { "flame", "frost" }) && body.Pattern?.Select(m => m.Type).SequenceEqual(new[] { "sealSlot", "atk", "delayRes", "silence", "charge", "atk" }) == true
            && body.Intent?.Type == "sealSlot" && body.Next?.Type == "atk" && body.Next?.Hits == 3 && S("nameless-kill-hooks", 2).KilledMarked == 0 && Of("nameless-kill-hooks", 2, "kill").Count == 0
            && Of("nameless-kill-hooks", 2, "phase").Select(e => (e.Uid, e.Index, e.Hp, e.Brk)).SequenceEqual(new (string?, int?, int?, int?)[] { (body.Uid, 1, 2, 15) }),
            "independent first nameless HP0: same UID second body, mark reset, no kill/deduction, new pattern/intent");
        var won = S("nameless-kill-hooks", 4);
        Check(won.Phase == "VICTORY" && won.KilledMarked == 1 && won.RunFlags.TryGetValue("coinsBonus", out var coins) && Convert.ToInt64(coins, CultureInfo.InvariantCulture) == 10
            && won.TurnFlags.TryGetValue("deduction", out var deduced) && deduced is bool deduction && deduction && Of("nameless-kill-hooks", 4, "kill").Count == 1
            && Code("nameless-kill-hooks", 5) == "WRONG_PHASE", "independent final death runs marked-kill hooks (Kaya deduction, notebook coins)");
        Check(S("nameless-multihit", 1).Events.Select(e => e.E).Where(e => e == "dmg" || e == "phase" || e == "kill" || e == "end").SequenceEqual(new[] { "dmg", "phase", "dmg", "kill", "end" })
            && S("nameless-multihit", 1).Phase == "VICTORY" && Code("nameless-multihit", 2) == "WRONG_PHASE", "independent later hit of the same card damages the second body");
        var second = S("nameless-phase2-cycles", 1).Enemies[0]; var last = S("nameless-phase2-cycles", 13);
        Check(second.Alive && second.PhaseIndex == 1 && second.Hp == 2 && second.Status.Burn == 0 && second.PatternIndex == 0 && second.Intent?.Type == "sealSlot"
            && last.Phase == "PLAYER" && last.Turn == 14 && last.Hp == 252, "independent burn opens the second body (burn not carried); its pattern runs two cycles: 500-248 HP");
        var countered = S("phase-counter-multihit", 2);
        Check(S("phase-counter-multihit", 1).Counter == 1 && countered.Events.Count(e => e.E == "hit") == 1 && countered.Hp == 79 && countered.Enemies[0].PhaseIndex == 1
            && countered.Enemies[0].Hp == 100 && countered.Enemies[0].Intent?.Type == "charge", "independent counter-opened phase ends the old body's 3-hit attack after one hit");
        var reflected = S("phase-reflect-defeat", 2);
        Check(S("phase-reflect-defeat", 1).Enemies[0].Status.Reflect == 50 && reflected.Phase == "DEFEAT" && reflected.Hp == 0 && reflected.Enemies[0].Alive && reflected.Enemies[0].PhaseIndex == 1
            && reflected.Enemies[0].Hp == 30 && Code("phase-reflect-defeat", 3) == "WRONG_PHASE", "independent reflect from the lethal hit defeats the party after the transition");
        foreach (var (edge, code) in InvalidPhases)
        {
            var survivor = S(edge, 2).Enemies[0];
            Check(Code(edge, 1) == code && survivor.Hp == 1 && survivor.PhaseIndex == null && survivor.Pattern == null && !survivor.ToJsonGraph().ContainsKey("phaseIndex"),
                "independent " + edge + " rejects the lethal command with " + code + " and leaves no phase keys");
        }
        foreach (string edge in new[] { "move-invalid-drain-negative", "move-invalid-drain-missing", "move-invalid-silence-count" })
            Check(Code(edge, 1) == "INVALID_ENEMY_MOVE", "independent " + edge + " rejects with INVALID_ENEMY_MOVE");
        var terminal = actual["edge-late-terminal-defeat"][1];
        Check(terminal.State.Phase == "DEFEAT" && terminal.State.ApDrainNext == 2 && terminal.State.SilenceUntil == 2 && terminal.State.SealedUntil == 2 && terminal.State.Exhausted == 1
            && Equals(terminal.View["apDrainNext"], 2) && Equals(terminal.View["silenceUntil"], 2) && Code("late-terminal-defeat", 2) == "WRONG_PHASE",
            "independent terminal stop keeps unconsumed drain/silence/seal visible");
        var protocol = actual["protocol-late-receipts"];
        Check(protocol.Select(s => s.Outcome).SequenceEqual(new[] { "committed", "committed", "replayed", "rejected", "rejected", "committed", "replayed" }) && protocol[1].State.Ap == 2
            && protocol[3].Code == "IDEMPOTENCY_CONFLICT" && protocol[4].Code == "STALE_VERSION", "independent receipts across a drain command");

        using var candidates = JsonDocument.Parse(CandidateJson);
        foreach (var enemy in candidates.RootElement.EnumerateArray())
        {
            string id = Text(enemy, "id"); int length = enemy.GetProperty("pattern").GetArrayLength(); var states = actual["duel-" + id]; var final = states[^1].State;
            var moves = new HashSet<string>(states.Where(s => s.Outcome == "committed").SelectMany(s => s.State.Events.Select(e => e.E)), StringComparer.Ordinal);
            var late = enemy.GetProperty("pattern").EnumerateArray().Select(m => Text(m, "t")).Where(Behaviors.Contains).ToList();
            Check(states.Count == 1 + 2 * length && final.Phase == "PLAYER" && final.Turn == 1 + 2 * length && final.Enemies.All(e => e.Alive && e.PhaseIndex == null && e.Pattern == null)
                && late.All(moves.Contains), "independent two full alive cycles of candidate " + id + " with every late move executed");
        }
        var all = actual.Values.SelectMany(list => list.Where(s => s.Outcome == "committed").SelectMany(s => s.State.Events)).ToList();
        // RULES_NOW_PLAY: with no reservation a delayRes can neither move nor discard one, so only its null-uid outcome is reachable (and no other).
        Check(Behaviors.All(b => all.Any(e => e.E == b)) && all.Where(e => e.E == "delayRes").All(e => e.Uid == null && e.Discarded == false)
            && all.Any(e => e.E == "delayRes" && e.Uid == null) && all.Any(e => e.E == "sealSlot" && e.Fallback == true) && all.Any(e => e.E == "sealSlot" && e.Fallback == false)
            && all.Any(e => e.E == "eatName" && e.Uid == null) && all.Any(e => e.E == "eatName" && e.Uid != null), "all six behaviors and both outcomes of every reachable branching behavior actually ran in C#");
    }

    // ───────── C#-only local probes (typed limits outside the shared oracle) ─────────
    static void LocalProbes(string root, JsonElement overlay, JsonElement boundary)
    {
        BattleData Fresh() => BattleDataLoader.LoadWithEnemies(root, overlay, boundary);
        static Battle Start(BattleData data, int difficultyHp, params string[] enemies)
        {
            var setup = new BattleSetup { Hp = 81, MaxHp = 81, Seed = 0, DifficultyHp = difficultyHp, DifficultyDamage = difficultyHp == 0 ? 0 : 100 };
            setup.Party.Add(new BattleActorSetup { CharId = "sera" });
            for (int i = 0; i < 3; i++) setup.Deck.Add(new BattleCardSpec { Id = "sera_round" });
            setup.Enemies.AddRange(enemies);
            var battle = Battle.Create(data, setup); battle.Command(new BattleRequest("probe-begin", 0, "begin")); return battle;
        }
        static BattleRequest Lethal(string rid, int v) => BattleDataLoader.Request("{\"rid\":\"" + rid + "\",\"v\":" + v.ToString(CultureInfo.InvariantCulture)
            + ",\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"mode\":\"now\",\"target\":null,\"picks\":[]}}");
        static bool Atomic(Battle battle, BattleRequest request, string code)
        {
            string before = Full(battle.State.ToJsonGraph()), view = Full(battle.PublicView());
            return Throws(() => battle.Command(request), code) && Full(battle.State.ToJsonGraph()) == before && Full(battle.PublicView()) == view;
        }

        var data = Fresh(); var smasher = data.Enemies.Single(e => e.Id == "t_smasher"); var hit = smasher.Pattern[0];
        foreach (var (label, move) in new (string, EnemyMoveDef)[]
        {
            ("a pattern-held phase move (JS WRONG_PHASE)", new EnemyMoveDef { Type = "phase" }), ("apDrain with an extra member", new EnemyMoveDef { Type = "apDrain", N = 1, Damage = 1 }),
            ("sealSlot with a count", new EnemyMoveDef { Type = "sealSlot", N = 1 }), ("delayRes with block", new EnemyMoveDef { Type = "delayRes", Block = 1 }),
            ("eatName with a card", new EnemyMoveDef { Type = "eatName", Card = "smudge" }), ("silence with damage", new EnemyMoveDef { Type = "silence", N = 1, Damage = 1 })
        })
        {
            var battle = Start(data, 0, "t_smasher"); smasher.Pattern[0] = move;
            Check(Atomic(battle, new BattleRequest("probe-shape", 1, "end"), "BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED"), "C#-only trust boundary: " + label + " still rejects atomically");
            smasher.Pattern[0] = hit;
        }
        {
            var battle = Start(data, 0, "t_smasher"); smasher.Pattern[0] = new EnemyMoveDef { Type = "silence" };
            var response = battle.Command(new BattleRequest("probe-silence", 1, "end")); smasher.Pattern[0] = hit;
            Check(battle.State.SilenceUntil == 2 && response.Events.Any(e => e.E == "silence" && e.Until == 2), "the original silence without n is the same +1 silence");
        }
        {
            var drainData = Fresh(); drainData.Enemies.Single(e => e.Id == "t_drain").Pattern[0] = new EnemyMoveDef { Type = "apDrain", N = int.MaxValue };
            Check(Atomic(Start(drainData, 0, "t_drain", "t_drain"), new BattleRequest("probe-overflow", 1, "end"), "BATTLE_NUMBER_OUT_OF_RANGE"),
                "C#-only Int32 limit: accumulated drain overflow rejects atomically (JS keeps a larger double)");
        }
        {
            var stealData = Fresh();
            stealData.Enemies.Single(e => e.Id == "t_phase_bad_hp").SetPhaseDescriptor(new EnemyPhaseDef { Type = "phase", Hp = 30, Brk = 20,
                Weak = new List<string> { "frost" }, Pattern = new List<EnemyMoveDef> { new EnemyMoveDef { Type = "steal" } } });
            var battle = Start(stealData, 0, "t_phase_bad_hp"); var opened = battle.Command(Lethal("probe-steal", 1)); var body = battle.State.Enemies[0];
            Check(opened.Events.Any(e => e.E == "phase") && body.PhaseIndex == 1 && body.Pattern != null && body.Pattern.Count == 1 && body.Pattern[0].Type == "steal"
                && Atomic(battle, new BattleRequest("probe-steal-end", 2, "end"), "BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED"),
                "C#-only: an unknown second-body move passes the original descriptor check, then rejects atomically at its action (JS UNSUPPORTED_EFFECT)");
        }
        {
            var attributeData = Fresh(); attributeData.SetAttributeIds(Array.Empty<string>());
            var battle = Start(attributeData, 1, "b_nameless"); var lethal = Lethal("probe-attributes", 1);
            Check(battle.State.Enemies[0].Hp == 2 && Atomic(battle, lethal, "INVALID_ENEMY_MOVE"), "undeclared attribute IDs fail closed: the phase change rejects atomically");
            attributeData.SetAttributeIds(new[] { "volt", "flame", "frost", "void", "none" });
            var opened = battle.Command(lethal); var body = battle.State.Enemies[0];
            Check(opened.V == 2 && body.PhaseIndex == 1 && body.Hp == 2 && body.Pattern?.Count == 6 && opened.Events.Count(e => e.E == "phase") == 1 && opened.Events.All(e => e.E != "kill"),
                "the failed request ID stayed free; declared attributes open the real second body");
            var descriptor = attributeData.Enemies.Single(e => e.Id == "b_nameless").PhaseDescriptor();
            string live = Full(battle.State.ToJsonGraph()), view = Full(battle.PublicView());
            bool edited = descriptor != null && descriptor.Pattern != null && descriptor.Weak != null;
            if (descriptor != null && descriptor.Pattern != null && descriptor.Weak != null)
            { descriptor.Pattern[0].Type = "steal"; descriptor.Pattern.Add(new EnemyMoveDef { Type = "charge" }); descriptor.Weak.Add("none"); }
            Check(edited && Full(battle.State.ToJsonGraph()) == live && Full(battle.PublicView()) == view, "the second body owns its pattern/weakness copies: later definition edits never reach live state");
        }
        string Enemy(string tail) => "[{\"id\":\"t_probe\",\"name\":\"x\",\"tier\":\"boss\",\"hp\":1,\"brk\":1,\"weak\":[\"frost\"],\"pattern\":[{\"t\":\"charge\"}]" + tail + "}]";
        foreach (var (label, json) in new[] { ("a non-object phase", Enemy(",\"phase\":1")), ("an unknown phase member", Enemy(",\"phase\":{\"t\":\"phase\",\"phases\":2}")),
            ("an unknown enemy member", Enemy(",\"phases\":{}")), ("a non-array overlay", "{}"),
            ("an overlay replacing live content", "[{\"id\":\"e_seed\",\"name\":\"x\",\"tier\":\"minion\",\"hp\":1,\"brk\":1,\"weak\":[\"frost\"],\"pattern\":[{\"t\":\"charge\"}]}]") })
        {
            using var document = JsonDocument.Parse(json); bool rejectedLoad = false;
            try { BattleDataLoader.LoadWithEnemies(root, document.RootElement); } catch (InvalidDataException) { rejectedLoad = true; }
            Check(rejectedLoad, "host loader rejects " + label);
        }
        bool twice = false;
        try { BattleDataLoader.LoadWithEnemies(root, overlay, overlay); } catch (InvalidDataException) { twice = true; }
        Check(twice, "the candidate overlay can never be appended twice or replace an ID");
        using (var nulled = JsonDocument.Parse(Enemy(",\"phase\":null")))
            Check(BattleDataLoader.LoadWithEnemies(root, nulled.RootElement).Enemies.Single(e => e.Id == "t_probe").PhaseDescriptor() == null, "an explicit null phase is no descriptor, as the original falsy check");
        Check(BattleDataLoader.Load(root).Enemies.Count == 20, "production Load(root) still returns only the live 20 after every overlay load");
    }

    // ───────── fixture validation (structure/provenance only; values are proven by actual execution above) ─────────
    static void ValidateFixture(JsonElement fixture, JsonElement content, string root)
    {
        RequireKeys(fixture, new[] { "schema", "scope", "status", "dataVersion", "sources", "overlay", "boundaryEnemies", "behaviors", "coverage", "counts", "cases" }, "Late-action fixture top-level members");
        Require(Text(fixture, "schema") == Schema && Text(fixture, "scope") == Scope && Text(fixture, "status") == Status, "Wrong late-action schema/scope/status");
        Require(Text(fixture, "dataVersion") == Text(content.GetProperty("meta"), "version"), "Wrong late-action data version");
        var sources = fixture.GetProperty("sources");
        Require(sources.ValueKind == JsonValueKind.Object && sources.EnumerateObject().Select(p => p.Name).SequenceEqual(Sources.Select(s => s.Key)), "Late-action source allowlist membership/order differs");
        foreach (var (key, path) in Sources)
        {
            var record = sources.GetProperty(key); RequireKeys(record, new[] { "path", "sha256" }, "Late-action source record " + key);
            Require(Text(record, "path") == path && HashText(record.GetProperty("sha256")) == FileHash(Path.Combine(root, path)), "Stale/substituted late-action source: " + key);
        }
        var overlay = fixture.GetProperty("overlay"); RequireKeys(overlay, new[] { "provenance", "enemies", "encounters" }, "Overlay members");
        RequireSameJson(ProvenanceJson, overlay.GetProperty("provenance"), "Overlay provenance is not the candidate-only proposal record");
        RequireSameJson(CandidateJson, overlay.GetProperty("enemies"), "Overlay enemies differ from the independent transcription of the pinned proposal");
        RequireSameJson(EncounterJson, overlay.GetProperty("encounters"), "Overlay encounter groups differ from the pinned proposal");
        RequireSameJson(BoundaryJson, fixture.GetProperty("boundaryEnemies"), "Test-local boundary enemies differ from the pinned producer");
        RequireSameJson(JsonSerializer.Serialize(Behaviors), fixture.GetProperty("behaviors"), "Late behavior list hidden or changed");
        foreach (var (name, table) in new[] { ("counts", Counts), ("coverage", Coverage) })
        {
            var record = fixture.GetProperty(name);
            Require(record.ValueKind == JsonValueKind.Object && record.EnumerateObject().Select(p => p.Name).SequenceEqual(table.Select(t => t.Key)) && table.All(t => Int(record.GetProperty(t.Key)) == t.Value),
                "Late-action " + name + " differ from the fixed measured denominators");
        }

        using var candidates = JsonDocument.Parse(CandidateJson); using var encounters = JsonDocument.Parse(EncounterJson);
        var lengths = candidates.RootElement.EnumerateArray().ToDictionary(e => Text(e, "id"), e => e.GetProperty("pattern").GetArrayLength(), StringComparer.Ordinal);
        var expected = new List<(string Id, string Group, string Dataset)>(); var groups = new Dictionary<string, List<object?>>(StringComparer.Ordinal);
        foreach (string id in Candidates) expected.Add(("duel-" + id, "duel", "candidate"));
        foreach (var zone in encounters.RootElement.EnumerateObject())
            foreach (var tier in zone.Value.EnumerateObject())
                foreach (var (group, index) in tier.Value.EnumerateArray().Select((group, index) => (group, index)))
                {
                    string id = $"encounter-{zone.Name}-{tier.Name}-{index}"; expected.Add((id, "encounter", "candidate"));
                    groups.Add(id, group.EnumerateArray().Select(e => (object?)Text(e)).ToList());
                }
        foreach (var edge in Edges) expected.Add(("edge-" + edge.Id, "edge", edge.Dataset));
        expected.Add(("protocol-late-receipts", "protocol", "candidate"));
        var rows = Items(fixture.GetProperty("cases"), "Late-action cases");
        Require(rows.Count == expected.Count, "Late-action case set incomplete or duplicated");
        for (int i = 0; i < rows.Count; i++)
        {
            RequireKeys(rows[i], new[] { "id", "group", "dataset", "setup", "created", "steps" }, "Late-action case members");
            Require(Text(rows[i], "id") == expected[i].Id && Text(rows[i], "group") == expected[i].Group && Text(rows[i], "dataset") == expected[i].Dataset, "Late-action case identity/order/dataset changed");
        }

        var baseDeck = new List<object?>();
        foreach (string character in new[] { "sera", "nia", "harin" })
        {
            foreach (var card in content.GetProperty("cards").EnumerateArray().Where(c => Text(c, "owner") == character && c.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.True))
                baseDeck.Add(new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = Text(card, "id") });
            baseDeck.Add(new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = Text(content.GetProperty("startingCommon"), character), ["assigned"] = character });
        }
        var baseParty = new[] { "sera", "nia", "harin" }.Select(c => (object?)new Dictionary<string, object?>(StringComparer.Ordinal) { ["charId"] = c }).ToList();
        using var protocol = JsonDocument.Parse(ProtocolJson); var protocolRequests = protocol.RootElement.EnumerateArray().ToList();
        string[] pointKeys = { "state", "stateHash", "rngHash", "eventsHash", "logHash", "publicView", "publicViewHash" };
        int requestCount = 0, commitCount = 0, replayCount = 0, rejectCount = 0;
        foreach (var row in rows)
        {
            string id = Text(row, "id"), group = Text(row, "group"); var setup = row.GetProperty("setup");
            var edge = group == "edge" ? Edges.Single(e => "edge-" + e.Id == id) : default;
            if (group != "edge")
            {
                var enemies = group == "duel" ? new List<object?> { id.Substring(5) } : group == "encounter" ? groups[id] : new List<object?> { "e_collector" };
                var expectedSetup = new Dictionary<string, object?>(StringComparer.Ordinal) { ["party"] = baseParty, ["deck"] = baseDeck, ["hp"] = 243, ["maxHp"] = 243, ["seed"] = 0,
                    ["relics"] = new List<object?>(), ["enemies"] = enemies };
                Require(JsonGraphComparison.Difference(BattleDataLoader.Graph(setup), expectedSetup, "$", 0) == null, id + " is not the live-matrix setup (base kits, 243 HP, seed 0, no relics)");
            }
            else
            {
                var keys = setup.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
                Require(new[] { "party", "deck", "hp", "maxHp", "seed", "relics", "enemies" }.All(keys.Contains)
                    && keys.All(k => k == "party" || k == "deck" || k == "hp" || k == "maxHp" || k == "seed" || k == "relics" || k == "enemies" || k == "difficulty"), id + " setup must hold only Battle.create inputs");
                foreach (var actor in Items(setup.GetProperty("party"), id + " party"))
                    Require(actor.ValueKind == JsonValueKind.Object && actor.TryGetProperty("charId", out _) && actor.EnumerateObject().All(p => p.Name == "charId" || p.Name == "gauge"), id + " party injects actor state");
                foreach (var card in Items(setup.GetProperty("deck"), id + " deck")) DeckCard(card, id);
                if (keys.Contains("difficulty")) RequireKeys(setup.GetProperty("difficulty"), new[] { "hp", "dmg" }, id + " difficulty members");
                Require(Items(setup.GetProperty("enemies"), id + " enemies").Select(e => e.GetString()).SequenceEqual(edge.Enemies.Split(' ')), id + " enemy list changed");
            }
            var created = row.GetProperty("created"); RequireKeys(created, pointKeys, id + " created point members"); ValidatePoint(created, id + " created");
            Require(Int(created.GetProperty("state").GetProperty("v")) == 0 && Text(created.GetProperty("state"), "phase") == "CREATED", id + " does not start from a fresh Battle.create");
            var steps = Items(row.GetProperty("steps"), id + " steps");
            int expectedSteps = group == "duel" ? 1 + 2 * lengths[id.Substring(5)] : group == "encounter" ? 1 + 2 * groups[id].Max(e => lengths[(string)e!]) : group == "edge" ? edge.Commands : protocolRequests.Count;
            Require(steps.Count == expectedSteps, id + " request count differs from its fixed denominator");
            int v = 0, caseRejected = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i]; requestCount++;
                RequireKeys(step, pointKeys.Concat(new[] { "request", "outcome", "errorCode", "response", "responseEventsHash" }).ToArray(), id + " step members");
                var request = step.GetProperty("request"); RequireKeys(request, new[] { "rid", "v", "kind", "args" }, id + " request members");
                _ = BattleDataLoader.Request(request);
                string kind = Text(request, "kind"), outcome = Text(step, "outcome");
                Require(kind == "begin" || kind == "play" || kind == "ult" || kind == "end", id + " request kind");
                if (group == "protocol") Require(JsonGraphComparison.Difference(BattleDataLoader.Graph(protocolRequests[i]), BattleDataLoader.Graph(request), "$", 0) == null, id + " protocol request sequence changed");
                else
                {
                    Require(Text(request, "rid") == $"{id}-{i}-{kind}" && Int(request.GetProperty("v")) == v && (i != 0 || kind == "begin") && outcome != "replayed", id + " request identity/version continuity");
                    if (group != "edge")
                        Require(kind == (i == 0 ? "begin" : "end") && outcome == "committed" && request.GetProperty("args").ValueKind == JsonValueKind.Object && !request.GetProperty("args").EnumerateObject().Any(),
                            id + " skipped-turn cycle must be begin then committed ends");
                }
                if (outcome == "rejected")
                {
                    rejectCount++; caseRejected++;
                    Require(Errors.Contains(Text(step, "errorCode")) && step.GetProperty("response").ValueKind == JsonValueKind.Null && step.GetProperty("responseEventsHash").ValueKind == JsonValueKind.Null,
                        id + " rejected request has an unexpected code or a response");
                }
                else
                {
                    Require(outcome == "committed" || outcome == "replayed", id + " unknown outcome");
                    var response = step.GetProperty("response"); RequireKeys(response, new[] { "v", "phase", "ev" }, id + " response members");
                    Require(step.GetProperty("errorCode").ValueKind == JsonValueKind.Null && CanonicalState.Hash(BattleDataLoader.Graph(response.GetProperty("ev"))) == HashText(step.GetProperty("responseEventsHash")),
                        id + " stale response event hash or error on success");
                    if (outcome == "committed") { commitCount++; Require(Int(response.GetProperty("v")) == ++v, id + " commit version continuity"); } else replayCount++;
                }
                ValidatePoint(step, id + " step " + i);
                Require(Int(step.GetProperty("state").GetProperty("v")) == v, id + " checkpoint version");
            }
            if (group == "edge") Require(caseRejected == edge.Rejected, id + " rejection count differs from its fixed denominator");
        }
        var measured = new[] { rows.Count, requestCount, commitCount, replayCount, rejectCount };
        Require(measured.SequenceEqual(Counts.Select(c => c.Value)), "Late-action measured counts differ from the fixed denominators");
    }
    // Public Battle.create card input in this corpus: a string id plus the optional string "assigned" of the unchanged base kits'
    // starting commons (BattleCardSpec.Assigned). Other members are outside this bounded corpus; uid/used/token/temp are produced state.
    static void DeckCard(JsonElement card, string id)
    {
        Require(card.ValueKind == JsonValueKind.Object && card.TryGetProperty("id", out var cardId) && cardId.ValueKind == JsonValueKind.String
            && card.EnumerateObject().All(p => p.Name == "id" || p.Name == "assigned" && p.Value.ValueKind == JsonValueKind.String), id + " deck injects produced card state");
    }
    static void ValidatePoint(JsonElement point, string label)
    {
        var state = point.GetProperty("state"); var view = point.GetProperty("publicView");
        foreach (string key in new[] { "rng", "cards", "zones", "draw" }) Require(!view.TryGetProperty(key, out _), label + " public view exposes " + key);
        foreach (var (value, hash) in new[] { (state, "stateHash"), (state.GetProperty("rng"), "rngHash"), (state.GetProperty("ev"), "eventsHash"), (state.GetProperty("log"), "logHash"), (view, "publicViewHash") })
            Require(CanonicalState.Hash(BattleDataLoader.Graph(value)) == HashText(point.GetProperty(hash)), "Stale late-action checkpoint " + hash + " at " + label);
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
            bool rejectedInput = false;
            try { RunCreateChecks.Decode(input).Dispose(); } catch (Exception) { rejectedInput = true; }
            Check(rejectedInput, "strict fixture decoder rejects " + label);
        }
    }
    static void FixtureRejections(JsonElement fixture, JsonElement content, string root)
    {
        var original = JsonNode.Parse(fixture.GetRawText()) ?? throw new InvalidDataException("Fixture parsed to null");
        void Reject(Action<JsonNode> tamper, string label)
        {
            var bad = original.DeepClone(); tamper(bad); bool rejectedFixture = false;
            using (var json = JsonDocument.Parse(bad.ToJsonString()))
            {
                try { ValidateFixture(json.RootElement, content, root); } catch (InvalidDataException) { rejectedFixture = true; }
            }
            Check(rejectedFixture, "fixture tamper rejected: " + label);
        }
        static JsonNode Node(JsonNode? node) => node ?? throw new InvalidDataException("Missing fixture node");
        JsonNode Case(JsonNode f, int index) => Node(Node(f["cases"])[index]);
        JsonNode Step(JsonNode f, int index, int step) => Node(Node(Case(f, index)["steps"])[step]);
        foreach (string field in new[] { "schema", "scope", "status", "dataVersion" }) Reject(f => f[field] = "tampered", field);
        Reject(f => Node(Node(f["sources"])["proposal"])["path"] = "../proposals/late_enemies.cjs", "substituted proposal path");
        Reject(f => Node(Node(f["sources"])["proposal"])["sha256"] = new string('0', 64), "stale proposal hash");
        Reject(f => Node(f["sources"])["extra"] = Node(Node(f["sources"])["engine"]).DeepClone(), "unlisted source");
        Reject(f => Node(Node(Node(Node(f["overlay"])["enemies"])[22])["phase"])["hp"] = 171, "candidate second-body HP retuned");
        Reject(f => Node(Node(Node(Node(f["overlay"])["enemies"])[0])["pattern"])[1]!["n"] = 2, "candidate drain retuned");
        Reject(f => Node(Node(f["overlay"])["provenance"])["status"] = "authored", "candidate activation claimed");
        Reject(f => Node(Node(Node(f["overlay"])["encounters"])["5"])["boss"] = new JsonArray(), "candidate encounter dropped");
        Reject(f => Node(Node(Node(f["boundaryEnemies"])[0])["pattern"])[1]!["n"] = 5, "test-local enemy changed");
        Reject(f => Node(f["behaviors"]).AsArray().RemoveAt(5), "phase behavior hidden");
        Reject(f => Node(f["counts"])["commands"] = 472, "changed denominator");
        Reject(f => Node(f["coverage"])["edges"] = 33, "changed coverage");
        Reject(f => Node(f["cases"]).AsArray().RemoveAt(0), "missing case");
        Reject(f => Node(f["cases"]).AsArray().Add(Case(f, 0).DeepClone()), "duplicate case");
        Reject(f => Case(f, 0)["dataset"] = "boundary", "candidate duel moved to the boundary dataset");
        Reject(f => Node(Case(f, 0)["setup"])["hp"] = 244, "duel setup HP changed");
        Reject(f => Step(f, 0, 0)["stateHash"] = new string('0', 64), "stale state hash");
        Reject(f => Node(Step(f, 0, 0)["state"])["log"] = new JsonArray(), "edited state log");
        Reject(f => Node(Step(f, 0, 0)["publicView"])["rng"] = new JsonArray(), "public RNG leak");
        Reject(f => Node(Step(f, 0, 1)["request"])["kind"] = "play", "changed request kind");
        Reject(f => Step(f, 0, 1)["outcome"] = "replayed", "changed outcome");
        int edge = Candidates.Length + 23;
        Reject(f => Node(Node(Case(f, edge)["setup"])["deck"])[0]!["growth"] = 3, "produced card state injected into a boundary deck");
        Reject(f => Node(Node(Case(f, edge)["setup"])["enemies"])[0] = "t_smasher", "boundary enemy substituted");
        // edge-seal-two-sealers uses the unchanged base kits: their starting commons carry the legal public "assigned" input.
        int sealers = edge + Array.FindIndex(Edges, e => e.Id == "seal-two-sealers");
        var assigned = fixture.GetProperty("cases")[sealers].GetProperty("setup").GetProperty("deck").EnumerateArray().Where(c => c.TryGetProperty("assigned", out _)).ToList();
        bool accepted = assigned.Count == 3;
        foreach (var card in assigned) { try { DeckCard(card, "seal-two-sealers"); } catch (InvalidDataException) { accepted = false; } }
        Check(accepted, "the actual base-kit {id, assigned} Create input is accepted structurally (3 starting commons)");
        JsonNode AssignedCard(JsonNode f)
        {
            foreach (var card in Node(Node(Case(f, sealers)["setup"])["deck"]).AsArray()) if (Node(card)["assigned"] != null) return Node(card);
            throw new InvalidDataException("No assigned base-kit card");
        }
        Reject(f => AssignedCard(f)["uid"] = "c1", "produced uid injected beside a legal assigned card");
        Reject(f => AssignedCard(f)["used"] = 1, "produced use count injected beside a legal assigned card");
        Reject(f => AssignedCard(f)["temp"] = new JsonArray(), "produced temporary parts injected beside a legal assigned card");
        Reject(f => AssignedCard(f)["token"] = true, "produced token flag injected beside a legal assigned card");
        Reject(f => AssignedCard(f)["assigned"] = 1, "non-string assigned input");
        Reject(f => AssignedCard(f).AsObject().Remove("id"), "card input without an id");
    }

    // ───────── helpers ─────────
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
    static void RequireSameJson(string expected, JsonElement actual, string label)
    {
        using var json = JsonDocument.Parse(expected);
        Require(JsonGraphComparison.Difference(BattleDataLoader.Graph(json.RootElement), BattleDataLoader.Graph(actual), "$", 0) == null, label);
    }
    static string Text(JsonElement json) => json.ValueKind == JsonValueKind.String ? (string)BattleDataLoader.Graph(json)! : throw new InvalidDataException("Expected string");
    static string Text(JsonElement json, string key) => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(key, out var value) ? Text(value) : throw new InvalidDataException("Expected string " + key);
    static int Int(JsonElement json) => json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out int value) ? value : throw new InvalidDataException("Expected Int32");
    static string Full(object? graph) => JsonSerializer.Serialize(graph);
    static string FileHash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    static string HashText(JsonElement json)
    {
        string hash = json.ValueKind == JsonValueKind.String ? json.GetString() ?? "" : "";
        Require(hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Malformed late-action hash"); return hash;
    }
}
