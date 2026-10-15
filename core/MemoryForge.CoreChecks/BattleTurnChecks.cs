// §5.3/5.7-9/5.13-16, §10.2, §16.2: actual Create/begin/repeated end; expected snapshots are never execution inputs.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MemoryForge.Core;

static class BattleTurnChecks
{
    const string Scope = "Real Battle.create/begin/repeated end with skipped player turns; play/ult and full Battle/Run replay NOT_IMPLEMENTED";
    static readonly Dictionary<string, string> Sources = new(StringComparer.Ordinal)
    {
        ["data"] = "data/game_data.json", ["engine"] = "engine/rules-core.js", ["memory"] = "proposals/memory_v2.cjs",
        ["canonical"] = "tools/gen_traces.mjs", ["openingGenerator"] = "tools/gen_battle_opening_vectors.mjs", ["generator"] = "tools/gen_battle_turn_vectors.mjs"
    };
    static readonly string[] OpeningEdges = ("seed-zero seed-uint-max seed-default-omitted seed-default-null zero-fallback-values full-starting-gauges " +
        "harin-opening-preserved start-block-per-actor mixed-derived-vitality-gear aila-overflow-strongest-heal aila-partial-overflow aila-before-circuit-cost " +
        "brazier-before-kaya brazier-kaya-uint-max draw-max-strongest-only draw-stronger-weapon-later tuning-only-one-eligible tuning-only-curses empty-deck " +
        "seed-moss-single seed-no-eligible seed-mixed-eligibility seed-creation-order-duplicates card-fields-and-lineage circuit-fatal circuit-exact-fatal " +
        "circuit-survives circuit-envelope-used circuit-envelope-unused ap-cap-high ap-cap-low ap-crown-hourglass ap-carried-bonus echo-and-rule-interactions three-opening-actors").Split(' ');
    static readonly Dictionary<string, int> Edges = new(StringComparer.Ordinal)
    {
        ["summon-cap"] = 7, ["summon-initial-cap"] = 2, ["summon-before-block-all"] = 1, ["summon-after-block-all"] = 1,
        ["summon-refill-dead-slot"] = 4, ["heal-lowest-tie"] = 3, ["heal-ratio-not-hp"] = 3, ["heal-full"] = 3,
        ["heal-js-rounded-tie"] = 6, ["notebook-continuing"] = 2, ["notebook-victory"] = 2, ["burn-reflect"] = 2,
        ["raw-scaling"] = 1, ["party-power-rounding"] = 1, ["curse-scaled"] = 2, ["smudge-fatal-loop"] = 2,
        ["smudge-fatal-no-envelope"] = 2, ["penalty-note-inert"] = 3, ["eye-of-rift-cycle"] = 8, ["insert-cycle"] = 8,
        ["small-deck"] = 4, ["first-attack-fatal"] = 2, ["envelope-multihit"] = 2, ["envelope-used"] = 2,
        ["same-hit-defeat-reward"] = 2, ["envelope-counter-kill"] = 2, ["counter-kill-stops-hits"] = 2,
        ["attack-debuff-fatal"] = 2, ["multiple-enemy-fatal"] = 2, ["circuit-next-fatal"] = 2,
        ["circuit-envelope-next"] = 3, ["circuit-envelope-already-used"] = 2, ["carried-ap-once"] = 3, ["log-cap-400"] = 425
    };
    static readonly string[] ReceiptKeys = ("constructor __defineGetter__ __defineSetter__ hasOwnProperty __lookupGetter__ __lookupSetter__ " +
        "isPrototypeOf propertyIsEnumerable toString valueOf __proto__ toLocaleString").Split(' ');
    static int checks, failures, commands, committed, replayed, rejected, receiptProbes;
    static void Check(bool ok, string message) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL BattleTurn " + message); } }
    static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }

    public static int Run(string root)
    {
        checks = failures = commands = committed = replayed = rejected = receiptProbes = 0;
        try
        {
            var data = BattleDataLoader.Load(root);
            using var document = ReadFixture(root, "battle_turn_vectors.json.gz");
            using var opening = ReadFixture(root, "battle_opening_vectors.json.gz");
            var fixture = document.RootElement;
            ValidateFixture(fixture, opening.RootElement, root, data);
            Check(true, "source hashes, exact unique coverage, request sequences and all expected diagnostic hashes");
            var cases = fixture.GetProperty("cases").EnumerateArray().ToDictionary(c => Text(c, "id"), StringComparer.Ordinal);
            var actual = new Dictionary<string, List<BattleState>>(StringComparer.Ordinal);
            foreach (var pair in cases)
            {
                string id = pair.Key; var row = pair.Value;
                var battle = Battle.Create(data, BattleDataLoader.Setup(row.GetProperty("setup")));
                CompareCheckpoint(row.GetProperty("created"), battle, id + " created");
                var states = new List<BattleState>(); actual.Add(id, states);
                var receipts = new Dictionary<string, (BattleRequest Request, JsonElement RequestJson, JsonElement Response)>(StringComparer.Ordinal);
                foreach (var step in row.GetProperty("steps").EnumerateArray())
                {
                    var requestJson = step.GetProperty("request"); var request = BattleDataLoader.Request(requestJson);
                    var before = battle.State; string beforeGraph = Full(before.ToJsonGraph());
                    string outcome = Text(step, "outcome"), label = id + " request " + commands++;
                    BattleResponse? response = null;
                    if (outcome == "rejected")
                    {
                        Check(Throws(() => battle.Command(request), Text(step, "errorCode")), label + " exact RuleError"); rejected++;
                        Check(Full(battle.State.ToJsonGraph()) == beforeGraph, label + " rejection preserves full RNG/v/log/ev state");
                        if (Text(row, "group") == "protocol" && !string.IsNullOrEmpty(request.Rid) && !receipts.ContainsKey(request.Rid!))
                        {
                            receiptProbes++;
                            Check(Throws(() => battle.Command(new BattleRequest(request.Rid, before.V, "unknown-receipt-probe")), "UNKNOWN_COMMAND"), label + " rejected request left no hidden receipt");
                        }
                    }
                    else
                    {
                        response = battle.Command(request);
                        Same(step.GetProperty("response"), response.ToJsonGraph(), label + " complete response/phase/version/events");
                        Check(CanonicalState.Hash(response.Events.Select(e => e.ToJsonGraph()).ToList()) == HashText(step.GetProperty("responseEventsHash")), label + " response event hash");
                        if (outcome == "replayed")
                        {
                            replayed++; Check(Full(battle.State.ToJsonGraph()) == beforeGraph, label + " replay preserves current complete state");
                        }
                        else
                        {
                            committed++; states.Add(battle.State);
                            Check(response.V == before.V + 1 && battle.State.V == response.V && battle.State.Phase == response.Phase, label + " commits exactly one version");
                            if (!string.IsNullOrEmpty(request.Rid)) receipts.Add(request.Rid!, (request, requestJson, step.GetProperty("response")));
                        }
                    }
                    CompareCheckpoint(step, battle, label);
                    if (step.TryGetProperty("receipts", out var expectedReceipts)) CompareReceipts(expectedReceipts, receipts, battle, label);
                }
                VerifyTrajectory(row, states, data);
            }
            Check(commands == 1740 && committed == 1673 && replayed == 14 && rejected == 53, "1740 actual oracle requests: 1673 commits / 14 replays / 53 rejections");
            IndependentAssertions(actual);
            FixtureRejections(fixture, opening.RootElement, root, data);
            BoundaryChecks(root, cases);
        }
        catch (Exception error) { Check(false, "fixture/execution: " + error); }
        Console.WriteLine($"BattleTurnChecks: {checks - failures}/{checks} passed; {commands} oracle requests, {committed} committed, {replayed} replayed, {rejected} rejected; {receiptProbes} additional black-box receipt probes");
        Console.WriteLine("BattleTurnScope: real fresh Create + begin/end, all 267 openings, 20 live two-cycle duels, 21 authored encounters and targeted boundaries");
        Console.WriteLine("BattleRunCommandReplay: NOT_IMPLEMENTED (play/ult, Run/restoration, producing powers including bulwark, Unity/device/server acceptance remain separate)");
        return failures;
    }

    static JsonDocument ReadFixture(string root, string name)
    {
        using var file = File.OpenRead(Path.Combine(root, "engine", "tests", "vectors", name));
        using var gzip = new GZipStream(file, CompressionMode.Decompress); return JsonDocument.Parse(gzip);
    }
    static void ValidateSources(JsonElement sources, string root, bool opening)
    {
        var expected = opening ? Sources.Where(p => p.Key != "generator" && p.Key != "openingGenerator").ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) : Sources;
        if (opening) expected.Add("generator", Sources["openingGenerator"]);
        var records = sources.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        Require(records.Count == expected.Count && expected.Keys.All(records.ContainsKey), "Turn source set mismatch");
        foreach (var source in expected)
        {
            var record = records[source.Key];
            Require(record.EnumerateObject().Count() == 2 && Text(record, "path") == source.Value, "Turn source path mismatch: " + source.Key);
            Require(HashText(record.GetProperty("sha256")) == FileHash(Path.Combine(root, source.Value)), "Stale turn source: " + source.Key);
        }
    }
    static void ValidateFixture(JsonElement fixture, JsonElement opening, string root, BattleData data)
    {
        Require(Text(fixture, "schema") == "mf-battle-turns-v1" && Text(fixture, "scope") == Scope, "Wrong turn schema/scope");
        using var content = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
        string version = Text(content.RootElement.GetProperty("meta"), "version");
        Require(Text(fixture, "dataVersion") == version && Text(opening, "dataVersion") == version, "Wrong turn/opening data version");
        Require(Text(opening, "schema") == "mf-battle-opening-v1", "Wrong prerequisite opening schema");
        ValidateSources(fixture.GetProperty("sources"), root, false); ValidateSources(opening.GetProperty("sources"), root, true);
        var openingIds = new HashSet<string>(OpeningEdges, StringComparer.Ordinal);
        foreach (var c in data.Characters) for (int r = 0; r <= 6; r++) Require(openingIds.Add($"character-{c.Id}-res{r}"), "Duplicate expected character");
        foreach (var w in data.Weapons) for (int r = 1; r <= 5; r++) Require(openingIds.Add($"weapon-{w.Id}-r{r}"), "Duplicate expected weapon");
        foreach (var r in data.Relics) Require(openingIds.Add("relic-" + r.Id), "Duplicate expected relic");
        foreach (var e in data.Enemies) Require(openingIds.Add("enemy-" + e.Id), "Duplicate expected enemy");
        var openings = opening.GetProperty("cases").EnumerateArray().ToDictionary(c => Text(c, "id"), StringComparer.Ordinal);
        Require(openingIds.Count == 267 && openingIds.SetEquals(openings.Keys), "Opening membership differs from current content");
        var baseDeck = new List<object?>();
        foreach (string character in new[] { "sera", "nia", "harin" })
        {
            foreach (var card in content.RootElement.GetProperty("cards").EnumerateArray().Where(c => Text(c, "owner") == character && c.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.True))
                baseDeck.Add(new Dictionary<string, object?> { ["id"] = Text(card, "id") });
            baseDeck.Add(new Dictionary<string, object?> { ["id"] = Text(content.RootElement.GetProperty("startingCommon"), character), ["assigned"] = character });
        }
        var expected = openingIds.ToDictionary(id => "opening-" + id, _ => "opening", StringComparer.Ordinal);
        foreach (var e in data.Enemies) expected.Add("enemy-" + e.Id + "-two-cycles", "enemy");
        var encounters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var zone in content.RootElement.GetProperty("encounters").EnumerateObject())
            foreach (var tier in zone.Value.EnumerateObject())
                foreach (var (group, index) in tier.Value.EnumerateArray().Select((group, index) => (group, index)))
                { string id = $"encounter-{zone.Name}-{tier.Name}-{index}"; expected.Add(id, "encounter"); encounters.Add(id, group); }
        foreach (var edge in Edges) expected.Add("edge-" + edge.Key, "edge");
        expected.Add("protocol-receipts", "protocol"); expected.Add("protocol-terminal-replay", "protocol");
        for (int i = 0; i < ReceiptKeys.Length; i++) expected.Add("protocol-own-rid-" + i, "protocol");
        var rows = fixture.GetProperty("cases").EnumerateArray().ToList();
        var ids = rows.Select(c => Text(c, "id")).ToList();
        Require(ids.Count == expected.Count && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count && expected.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(ids), "Turn case set incomplete/duplicate");
        int requestCount = 0, commitCount = 0, replayCount = 0, rejectCount = 0;
        foreach (var row in rows)
        {
            string id = Text(row, "id"), group = Text(row, "group"); Require(group == expected[id], "Turn case group mismatch: " + id);
            if (group == "opening")
            {
                string openingId = Text(row, "openingId"); Require(id == "opening-" + openingId && openings.ContainsKey(openingId), "Wrong opening linkage");
                RequireSame(openings[openingId].GetProperty("setup"), BattleDataLoader.Graph(row.GetProperty("setup")), id + " opening setup changed");
            }
            if (group == "enemy" || group == "encounter")
            {
                var setup = row.GetProperty("setup"); Require(setup.GetProperty("hp").GetInt32() == 243 && setup.GetProperty("maxHp").GetInt32() == 243 && setup.GetProperty("seed").GetUInt32() == 0, "Live content matrix setup changed");
                RequireSameJson("[{\"charId\":\"sera\"},{\"charId\":\"nia\"},{\"charId\":\"harin\"}]", setup.GetProperty("party"), "Live matrix party changed");
                RequireSame(setup.GetProperty("deck"), baseDeck, "Live matrix source base kits changed");
                Require(setup.GetProperty("relics").GetArrayLength() == 0 && !setup.TryGetProperty("difficulty", out _), "Live matrix endurance/source difficulty changed");
                if (group == "enemy")
                {
                    string enemyId = Text(row, "enemyId"); Require(id == "enemy-" + enemyId + "-two-cycles", "Wrong enemy linkage");
                    Require(setup.GetProperty("enemies").GetArrayLength() == 1 && setup.GetProperty("enemies")[0].GetString() == enemyId, "Wrong duel enemy");
                }
                else
                {
                    Require(id == $"encounter-{Text(row, "zone")}-{Text(row, "tier")}-{row.GetProperty("index").GetInt32()}", "Authored encounter metadata changed");
                    RequireSame(encounters[id], BattleDataLoader.Graph(setup.GetProperty("enemies")), "Authored encounter changed");
                }
            }
            if (id.StartsWith("protocol-own-rid-", StringComparison.Ordinal))
                Require(Text(row, "receiptKey") == ReceiptKeys[int.Parse(id[17..])], "Prototype-named receipt key changed");
            ValidateCheckpoint(row.GetProperty("created"));
            Require(row.GetProperty("created").TryGetProperty("receipts", out var createdReceipts) == (group == "protocol") && (group != "protocol" || !createdReceipts.EnumerateObject().Any()), "Fresh protocol receipts missing/nonempty");
            Require(row.GetProperty("created").GetProperty("state").GetProperty("v").GetInt32() == 0 && Text(row.GetProperty("created").GetProperty("state"), "phase") == "CREATED", "Fixture is not fresh creation");
            var steps = row.GetProperty("steps").EnumerateArray().ToList(); Require(steps.Count > 0, "Missing turn requests");
            int v = 0, ends = 0, terminalRejections = 0; bool terminal = false;
            foreach (var step in steps)
            {
                requestCount++; ValidateCheckpoint(step); var request = step.GetProperty("request");
                _ = BattleDataLoader.Request(request);
                string outcome = Text(step, "outcome");
                if (outcome == "committed") commitCount++; else if (outcome == "replayed") replayCount++; else if (outcome == "rejected") rejectCount++; else throw new InvalidDataException("Unknown turn outcome");
                if (outcome == "rejected")
                {
                    Require(new[] { "WRONG_PHASE", "STALE_VERSION", "UNKNOWN_COMMAND", "IDEMPOTENCY_CONFLICT" }.Contains(Text(step, "errorCode")), "Unexpected oracle rejection");
                    Require(step.GetProperty("response").ValueKind == JsonValueKind.Null && step.GetProperty("responseEventsHash").ValueKind == JsonValueKind.Null, "Rejected request has a response");
                }
                else
                {
                    Require(step.GetProperty("errorCode").ValueKind == JsonValueKind.Null, "Successful request has an error");
                    var response = step.GetProperty("response");
                    Require(CanonicalState.Hash(BattleDataLoader.Graph(response.GetProperty("ev"))) == HashText(step.GetProperty("responseEventsHash")), "Stale response event hash");
                }
                if (group != "protocol")
                {
                    string kind = v == 0 ? "begin" : "end";
                    Require(Text(request, "kind") == kind && request.GetProperty("v").GetInt32() == v, "Turn request sequence changed");
                    Require(Text(request, "rid") == id + (v == 0 ? "-begin" : terminal ? "-after-terminal" : "-end" + (ends + 1)), "Turn request ID changed");
                    Require(request.GetProperty("args").ValueKind == JsonValueKind.Object && !request.GetProperty("args").EnumerateObject().Any(), "Positive turn request args changed");
                    Require(outcome == (terminal ? "rejected" : "committed") && (!terminal || Text(step, "errorCode") == "WRONG_PHASE"), "Positive turn request was skipped/replayed");
                    if (terminal) Require(++terminalRejections == 1 && step.Equals(steps[^1]), "Duplicate/nonfinal terminal rejection");
                    if (!terminal) { if (v > 0) ends++; v++; }
                    terminal = Text(step.GetProperty("state"), "phase") is "VICTORY" or "DEFEAT";
                }
                Require(step.TryGetProperty("receipts", out _) == (group == "protocol"), "Protocol receipt evidence missing/unexpected");
            }
        }
        foreach (var (group, key) in new[] { ("opening", "openings"), ("enemy", "enemies"), ("encounter", "encounters"), ("edge", "edges"), ("protocol", "protocols") })
            Require(fixture.GetProperty("coverage").GetProperty(key).GetInt32() == expected.Values.Count(g => g == group), "Turn coverage metadata mismatch: " + key);
        var counts = fixture.GetProperty("counts");
        Require(counts.GetProperty("cases").GetInt32() == rows.Count && counts.GetProperty("commands").GetInt32() == requestCount && counts.GetProperty("committed").GetInt32() == commitCount && counts.GetProperty("replayed").GetInt32() == replayCount && counts.GetProperty("rejected").GetInt32() == rejectCount, "Turn count metadata mismatch");
    }

    static void ValidateCheckpoint(JsonElement point)
    {
        var state = point.GetProperty("state"); var view = point.GetProperty("publicView");
        foreach (string key in new[] { "rng", "cards", "zones", "draw" }) Require(!view.TryGetProperty(key, out _), "Public view exposes " + key);
        foreach (var (value, hash) in new[] { (state, "stateHash"), (state.GetProperty("rng"), "rngHash"), (state.GetProperty("ev"), "eventsHash"), (state.GetProperty("log"), "logHash"), (view, "publicViewHash") })
            Require(CanonicalState.Hash(BattleDataLoader.Graph(value)) == HashText(point.GetProperty(hash)), "Stale checkpoint " + hash);
    }
    static void CompareCheckpoint(JsonElement point, Battle battle, string label)
    {
        var state = battle.State; var graph = state.ToJsonGraph(); var view = battle.PublicView();
        Check(graph.Count == 59, label + " all 59 state fields");
        Same(point.GetProperty("state"), graph, label + " complete state including log/ev");
        Same(point.GetProperty("publicView"), view, label + " complete public view");
        foreach (var (value, hash) in new (object?, string)[] { (graph, "stateHash"), (state.Rng, "rngHash"), (state.Events.Select(e => e.ToJsonGraph()).ToList(), "eventsHash"), (state.Log.Select(e => e.ToJsonGraph()).ToList(), "logHash"), (view, "publicViewHash") })
            Check(CanonicalState.Hash(value) == HashText(point.GetProperty(hash)), label + " " + hash);
        Check(!view.Keys.Intersect(new[] { "rng", "cards", "zones", "draw" }).Any(), label + " public secrets excluded");
        Check(state.Reservations.Count == 0 && state.Debts.Count == 0 && state.Powers.Count == 0 && state.Buffs.Count == 0 && state.Exhausted == 0 && state.Zones.Exhaust.Count == 0, label + " no unproduced systems/exhaust hooks");
        var zones = state.Zones.Draw.Concat(state.Zones.Hand).Concat(state.Zones.Discard).Concat(state.Zones.Exhaust).ToList();
        Check(zones.Count == state.Cards.Count && zones.Distinct(StringComparer.Ordinal).Count() == zones.Count && zones.All(state.Cards.ContainsKey), label + " card conservation/unique zones");
    }
    static void CompareReceipts(JsonElement expected, Dictionary<string, (BattleRequest Request, JsonElement RequestJson, JsonElement Response)> receipts, Battle battle, string label)
    {
        var records = expected.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        Check(records.Count == receipts.Count && records.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(receipts.Keys), label + " exact receipt membership");
        string before = Full(battle.State.ToJsonGraph());
        foreach (var pair in receipts)
        {
            if (!records.TryGetValue(pair.Key, out var record)) continue;
            var stored = pair.Value;
            // These protocol vectors use integer/ASCII object args in their authored order.
            string digest = JsonSerializer.Serialize(new { v = stored.Request.V, kind = stored.Request.Kind, args = BattleDataLoader.Graph(stored.RequestJson.GetProperty("args")) });
            Check(Text(record, "digest") == digest, label + " receipt identity including args/log/ev order");
            Same(record.GetProperty("resp"), BattleDataLoader.Graph(stored.Response), label + " receipt evidence");
            receiptProbes++; Same(record.GetProperty("resp"), battle.Command(stored.Request).ToJsonGraph(), label + " black-box stored receipt replay");
        }
        Check(Full(battle.State.ToJsonGraph()) == before, label + " all receipt probes preserve complete state");
    }
    static void VerifyTrajectory(JsonElement row, List<BattleState> states, BattleData data)
    {
        string id = Text(row, "id"), group = Text(row, "group"); if (group == "protocol") return;
        int requiredEnds = group == "opening" ? 2 : group == "edge" ? Edges[id[5..]] : group == "enemy"
            ? 2 * data.Enemies.Single(e => e.Id == Text(row, "enemyId")).Pattern.Count
            : 2 * row.GetProperty("setup").GetProperty("enemies").EnumerateArray().Max(e => data.Enemies.Single(d => d.Id == e.GetString()).Pattern.Count);
        Check(states.Count >= 1 && states.Count - 1 <= requiredEnds && (states.Count - 1 == requiredEnds || states[^1].Phase is "VICTORY" or "DEFEAT"), id + " entire required end trajectory or actual terminal boundary");
        if (group == "enemy") Check(states.Count == requiredEnds + 1 && states[^1].Phase == "PLAYER" && states[^1].Turn == requiredEnds + 1, id + " two complete live pattern cycles at normal HP");
        if (states[^1].Phase is "VICTORY" or "DEFEAT")
        {
            var last = row.GetProperty("steps").EnumerateArray().Last();
            Check(Text(last, "outcome") == "rejected" && Text(last, "errorCode") == "WRONG_PHASE", id + " truthful post-terminal end rejection");
        }
    }

    static void IndependentAssertions(Dictionary<string, List<BattleState>> actual)
    {
        BattleState At(string id, int end = 0) => actual[id][end];
        IEnumerable<BattleState> Ends(string id) => actual[id].Skip(1);
        var first = At("opening-harin-opening-preserved"); var next = At("opening-harin-opening-preserved", 1);
        Check(first.Block == 26 && first.Counter == 2 && next.Turn == 2 && next.Block == 0 && next.Counter == 0, "independent Harin26/counter2 survive opening then clear on turn2");
        Check(next.Enemies[0].Block == 10 && next.Enemies[0].PatternIndex == 1 && next.Enemies[0].Intent!.Type == "atk" && next.Enemies[0].Intent!.Damage == 25, "independent shelf block10/next atk25 retained");
        Check(At("opening-seed-moss-single").Block == 3 && At("opening-seed-moss-single", 1).Block == 0 && At("opening-seed-moss-single", 1).Cards["c1"].Growth == 1 && Ends("opening-seed-moss-single").All(s => s.Events.All(e => e.E != "grow")), "independent opening moss clears block without repeating growth");
        Check(At("opening-draw-max-strongest-only").Zones.Hand.Count == 9 && Ends("opening-draw-max-strongest-only").All(s => s.Zones.Hand.Count == 5 && s.TempParts == 3 && s.Events.All(e => e.E != "tune")), "independent first-only draw9/tuning persist as draw5/tempParts3");
        Check(At("opening-ap-crown-hourglass").Ap == 4 && At("opening-ap-crown-hourglass", 1).Ap == 5 && actual["edge-carried-ap-once"].Select(s => s.Ap).SequenceEqual(new[] { 6, 5, 5, 5 }), "independent crown/hourglass/carried AP consumption");
        Check(Ends("opening-echo-and-rule-interactions").All(s => s.Echo == 1), "independent recurring echo reset");

        var garden = At("enemy-e_gardener-two-cycles", 1);
        Check(garden.Enemies.Select(e => (e.Id, e.PatternIndex, e.Block)).SequenceEqual(new[] { ("e_gardener", 1, 0), ("e_seed", 0, 0), ("e_seed", 0, 0) }), "independent new summons wait with pi0/block0");
        Events(garden, "[{\"e\":\"enemyHeal\",\"uid\":\"e16\",\"n\":10},{\"e\":\"turn\",\"turn\":2,\"ap\":4}]", "full gardener still requests seed heal10; no summon event");
        Check(At("enemy-e_gardener-two-cycles", 2).Enemies.Skip(1).All(e => e.Block == 2) && At("enemy-b_garden-two-cycles", 1).Events[0].N == 14, "independent seeds first act next phase; boss seed heal14");
        Check(actual["edge-summon-cap"][^1].Enemies.Count == 5 && actual["edge-summon-initial-cap"][^1].Enemies.Count == 5, "independent alive summon cap5");
        Check(At("edge-summon-before-block-all", 1).Enemies.Select(e => e.Block).SequenceEqual(new[] { 20, 20, 20, 20 }) && At("edge-summon-after-block-all", 1).Enemies.Select(e => e.Block).SequenceEqual(new[] { 20, 0, 0, 0 }), "independent appearance-order blockAll includes only existing summons");
        var refill = actual["edge-summon-refill-dead-slot"][^1];
        Check(refill.Enemies.Count == 6 && refill.Enemies.Count(e => e.Alive) == 5 && refill.Seq == 21, "independent dead seed leaves capacity but remains recorded/shared seq21");

        Check(At("edge-heal-lowest-tie", 1).Enemies.Skip(1).Select(e => e.Hp).SequenceEqual(new[] { 8, 8 }) && At("edge-heal-lowest-tie", 3).Enemies.Skip(1).Select(e => e.Hp).SequenceEqual(new[] { 12, 8 }), "independent stable equal-ratio healing selects first page");
        var heal = At("edge-heal-lowest-tie", 3).Events[0]; Check(heal.E == "enemyHeal" && heal.Uid == "e17" && heal.N == 6, "independent requested heal6 despite effective4");
        Check(At("edge-heal-ratio-not-hp", 3).Enemies.Skip(1).Select(e => e.Hp).SequenceEqual(new[] { 6, 12 }), "independent lowest ratio differs from lowest absolute HP");
        var largeBefore = At("edge-heal-js-rounded-tie", 5); var moth = largeBefore.Enemies[1]; var page = largeBefore.Enemies[2];
        Check(largeBefore.Hp == 96 && moth.Hp == 359999996 && moth.MaxHp == 360000002 && page.Hp == 239999997 && page.MaxHp == 240000001 && (BigInteger)moth.Hp * page.MaxHp - (BigInteger)page.Hp * moth.MaxHp == 2, "independent reachable large ratios differ by exact cross-product2");
        var largeAfter = At("edge-heal-js-rounded-tie", 6);
        Check(largeAfter.Hp == 84 && largeAfter.Enemies[1].Hp == 360000002 && largeAfter.Enemies[2].Hp == 239999997 && largeAfter.Events[0].Uid == "e17" && largeAfter.Events[0].N == 6, "independent actual end6 follows JS rounded tie and heals moth");

        var notebook = At("edge-notebook-continuing", 2);
        Check(At("edge-notebook-continuing", 1).Enemies[0].Hp == 2 && At("edge-notebook-continuing", 1).Enemies[0].Block == 2 && notebook.Phase == "PLAYER" && notebook.Turn == 3 && notebook.Zones.Hand.Count == 6 && notebook.KilledMarked == 1 && Convert.ToInt64(notebook.RunFlags["coinsBonus"]) == 10 && notebook.Ap == 4, "independent raw burn bypasses block then marked notebook kill draws6/coins10/AP4");
        Check(!notebook.TurnFlags.ContainsKey("deduction") && notebook.Enemies.All(e => !e.Alive || e.Status.Burn == 0), "independent enemy-phase kill has no Kaya deduction/follow-up burn");
        Check(At("edge-notebook-victory", 2).Phase == "VICTORY" && Convert.ToInt64(At("edge-notebook-victory", 2).RunFlags["coinsBonus"]) == 17, "independent final marked kill accumulates existing7 coins to17");
        Check(At("edge-burn-reflect", 1).Enemies[0].Status.Reflect == 3 && At("edge-burn-reflect", 1).Enemies[0].Hp == 48 && At("edge-burn-reflect", 2).Enemies[0].Status.Reflect == 0 && Ends("edge-burn-reflect").All(s => s.Events.All(e => e.Source != "reflect")), "independent raw burn ignores reflect; reflect clears at own action");
        var raw = At("edge-raw-scaling", 1);
        Check(raw.Events.Where(e => e.E == "dmg").Select(e => (e.N, e.Source)).SequenceEqual(new (int?, string?)[] { (3, "counter"), (3, "counter"), (6, "burn") }) && raw.Enemies[0].Brk == 2 && raw.Events.All(e => e.E != "break" && e.E != "heal"), "independent raw scaling excludes card multipliers/reflect/break");
        Check(At("edge-party-power-rounding", 1).Events.Where(e => e.E == "dmg").Select(e => e.N).SequenceEqual(new int?[] { 2, 2, 4 }), "independent rounded party power then one scale floor");

        var insert = At("edge-insert-cycle", 1); var token = insert.Cards["c17"];
        SameJson("{\"uid\":\"c17\",\"id\":\"smudge\",\"owner\":\"curse\",\"wielder\":null,\"plus\":false,\"mods\":{\"c\":null,\"e\":null},\"temp\":[],\"growth\":0,\"grown\":false,\"used\":0,\"costMod\":0,\"token\":true,\"free\":false,\"assigned\":null,\"from\":null}", token.ToJsonGraph(), "independent full inserted token/uid/defaults");
        Check(insert.Events[0].E == "insert" && insert.Events[0].Card == "smudge" && insert.Events[0].N == 1 && Ends("edge-insert-cycle").Any(s => s.Events.Any(e => e.E == "reshuffle")) && Ends("edge-insert-cycle").Any(s => s.Events.Any(e => e.E == "hpLoss" && e.Source == "smudge")), "independent insertion reaches actual reshuffle/draw/curse loss");
        Check(At("edge-curse-scaled", 1).Events.Where(e => e.E == "hpLoss").Select(e => e.N).SequenceEqual(new int?[] { 3, 3, 3, 3, 3 }), "independent difficulty scales all five smudge losses");
        Check(Ends("edge-penalty-note-inert").All(s => s.Hp == 1 && s.Zones.Exhaust.Count == 0) && Ends("edge-eye-of-rift-cycle").Any(s => s.Events.Any(e => e.Source == "smudge")), "independent penalty note inert; eye-of-rift curse really cycles");
        Check(Ends("edge-small-deck").All(s => s.Zones.Hand.Count == 1 && s.Events[0].E == "reshuffle") && actual["edge-small-deck"].All(s => s.Rng.SequenceEqual(At("edge-small-deck").Rng)), "independent one-card recycle preserves RNG");
        Check(At("enemy-e_moth-two-cycles", 3).Enemies[0].Status.Strength == 1 && At("enemy-e_moth-two-cycles", 4).Events[0].Damage == 13 && At("enemy-e_moth-two-cycles", 6).Enemies[0].Status.Strength == 2, "independent strength persists across pattern cycles");
        Check(Ends("enemy-e_scribe-two-cycles").Take(3).Select(s => s.Weak).SequenceEqual(new[] { 0, 1, 0 }) && At("enemy-e_scribe-two-cycles", 2).Events.All(e => e.E != "partyDebuff"), "independent weak decrements before new atkdebuff without extra event");
        var bell = At("enemy-b_bell-two-cycles", 3); SameJson("[{\"e\":\"expose\",\"uid\":\"e16\"},{\"e\":\"hit\",\"uid\":\"e16\",\"d\":52,\"blocked\":0}]", bell.Events.Take(2).Select(e => e.ToJsonGraph()).ToList(), "independent expose-before-hit exact event order");
        Check(bell.Enemies[0].Status.Vul == 1 && At("enemy-b_bell-two-cycles", 4).Enemies[0].Status.Vul == 0, "independent vulnerability decays after round end");

        foreach (var (id, hits, envelopes) in new[] { ("first-attack-fatal", 1, 0), ("envelope-multihit", 2, 1), ("envelope-used", 1, 0) })
        {
            var end = At("edge-" + id, 1);
            Check(end.Phase == "DEFEAT" && end.Turn == 1 && end.Events.Count(e => e.E == "hit") == hits && end.Events.Count(e => e.E == "envelope") == envelopes && end.Events.Count(e => e.E == "end") == 1, "independent per-hit terminal boundary " + id);
        }
        foreach (string id in new[] { "smudge-fatal-loop", "smudge-fatal-no-envelope" })
        {
            var end = At("edge-" + id, 1);
            Check(end.Events.Count(e => e.E == "hpLoss") == 5 && end.Zones.Hand.Count == 0 && end.Zones.Discard.Count == 5 && end.Events.All(e => e.E != "hit"), "independent complete fatal smudge loop before terminal check " + id);
        }
        foreach (string id in new[] { "circuit-next-fatal", "circuit-envelope-already-used" })
        {
            var end = At("edge-" + id, 1);
            Check(end.Turn == 2 && end.Phase == "DEFEAT" && end.Zones.Hand.Count == 0, "independent fatal next-turn cost before draw " + id);
            Events(end, "[{\"e\":\"hpLoss\",\"n\":2,\"src\":\"relic\"},{\"e\":\"end\",\"phase\":\"DEFEAT\"}]", "independent no turn/intents event after fatal circuit");
        }
        Check(Ends("edge-circuit-envelope-next").Select(s => (s.Turn, s.Hp, s.Phase)).SequenceEqual(new[] { (2, 1, "PLAYER"), (3, 0, "DEFEAT") }), "independent envelope saves next-turn cost once then later cost defeats");
        var fatal = At("edge-same-hit-defeat-reward", 1);
        Check(fatal.Phase == "DEFEAT" && fatal.Hp == 0 && fatal.Zones.Hand.Count == 1 && Convert.ToInt64(fatal.RunFlags["coinsBonus"]) == 10, "independent fatal hit preserves counter/notebook reward then defeat priority");
        Events(fatal, "[{\"e\":\"hit\",\"uid\":\"e16\",\"d\":14,\"blocked\":10},{\"e\":\"hpLoss\",\"n\":4,\"src\":\"enemy\"},{\"e\":\"dmg\",\"uid\":\"e16\",\"n\":2,\"blocked\":0,\"src\":\"counter\"},{\"e\":\"kill\",\"uid\":\"e16\",\"id\":\"e_page\"},{\"e\":\"end\",\"phase\":\"DEFEAT\"}]", "independent same-hit complete event order");
        foreach (var (id, hp) in new[] { ("envelope-counter-kill", 1), ("counter-kill-stops-hits", 239) })
        { var end = At("edge-" + id, 1); Check(end.Phase == "VICTORY" && end.Hp == hp && end.Events.Count(e => e.E == "hit") == 1 && end.Events.Count(e => e.E == "end") == 1, "independent counter victory stops remaining hits " + id); }
        var longStates = actual["edge-log-cap-400"]; var final = longStates[^1];
        var log = longStates.SelectMany(s => s.Events.Select(e => { var graph = e.ToJsonGraph(); graph["t"] = s.Turn; return graph; })).ToList();
        Check(longStates.Count == 426 && final.Turn == 426 && final.V == 426 && final.Hp == 243 && final.Log.Count == 400 && log.Count > 400 && final.Cards.Count == 15, "independent 425 ends exceed log cap with conserved cards/normal HP");
        Check(JsonGraphComparison.Difference(log.TakeLast(400).ToList(), final.Log.Select(e => e.ToJsonGraph()).ToList(), "$", 0) == null, "independent exact latest400 timestamped log entries");
        Check(longStates.Select(s => string.Join(",", s.Rng)).Distinct(StringComparer.Ordinal).Count() > 1 && final.Enemies[0].Intent!.Type == "def" && final.Enemies[0].Intent!.Block == 2, "independent long recycle advances RNG with stable public intent");
    }

    static void FixtureRejections(JsonElement fixture, JsonElement opening, string root, BattleData data)
    {
        void Reject(Dictionary<string, object?> graph, string label)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(graph));
            bool rejectedFixture = false;
            try { ValidateFixture(json.RootElement, opening, root, data); }
            catch (InvalidDataException) { rejectedFixture = true; }
            Check(rejectedFixture, "fixture tamper rejected: " + label);
        }
        foreach (string field in new[] { "schema", "scope", "dataVersion" }) { var bad = Shallow(fixture); bad[field] = "tampered"; Reject(bad, field); }
        foreach (string field in new[] { "path", "sha256" })
        {
            var bad = Shallow(fixture); var sources = Shallow(fixture.GetProperty("sources")); var engine = Shallow(fixture.GetProperty("sources").GetProperty("engine"));
            engine[field] = field == "path" ? "../engine/rules-core.js" : new string('0', 64); sources["engine"] = engine; bad["sources"] = sources; Reject(bad, "source " + field);
        }
        var rows = fixture.GetProperty("cases").EnumerateArray().Select(c => (object?)c).ToList();
        var missing = Shallow(fixture); missing["cases"] = rows.Skip(1).ToList(); Reject(missing, "missing case");
        var duplicate = Shallow(fixture); duplicate["cases"] = rows.Append(rows[0]).ToList(); Reject(duplicate, "duplicate case");
        foreach (string target in new[] { "rngHash", "eventsHash", "logHash", "responseEventsHash", "state-ev", "state-log", "public-leak", "request" })
        {
            var bad = Shallow(fixture); var changedRows = rows.ToList(); var row = (JsonElement)rows[0]!; var changedRow = Shallow(row);
            var steps = row.GetProperty("steps").EnumerateArray().Select(s => (object?)s).ToList(); var step = (JsonElement)steps[0]!; var changedStep = Shallow(step);
            if (target.StartsWith("state-", StringComparison.Ordinal)) { var state = Shallow(step.GetProperty("state")); state[target[6..]] = Array.Empty<object>(); changedStep["state"] = state; }
            else if (target == "public-leak") { var view = Shallow(step.GetProperty("publicView")); view["rng"] = Array.Empty<object>(); changedStep["publicView"] = view; }
            else if (target == "request") { var request = Shallow(step.GetProperty("request")); request["kind"] = "play"; changedStep["request"] = request; }
            else changedStep[target] = new string('0', 64);
            steps[0] = changedStep; changedRow["steps"] = steps; changedRows[0] = changedRow; bad["cases"] = changedRows; Reject(bad, target);
        }
    }

    static void BoundaryChecks(string root, Dictionary<string, JsonElement> cases)
    {
        BattleSetup Setup(string id = "edge-small-deck") => BattleDataLoader.Setup(cases[id].GetProperty("setup"));
        var data = BattleDataLoader.Load(root); var battle = Battle.Create(data, Setup());
        var begin = new BattleRequest("detached-begin", 0, "begin"); var beginResponse = battle.Command(begin);
        var end = new BattleRequest("detached-end", 1, "end"); var response = battle.Command(end); string expected = Full(response.ToJsonGraph()), before = Full(battle.State.ToJsonGraph());
        response.Phase = "tampered"; response.Events[0].E = "tampered"; response.Events.Clear();
        var replay = battle.Command(end); replay.Events[0].N = 999; replay.Events.Clear();
        Check(Full(battle.Command(end).ToJsonGraph()) == expected && Full(battle.State.ToJsonGraph()) == before, "every returned end receipt/event array detached");
        var snapshot = battle.State; snapshot.Reservations.Add(new Dictionary<string, object?>()); snapshot.Powers.Add(new Dictionary<string, object?>()); snapshot.Debts.Add(new BattleDebt { N = 1, Due = 1 }); snapshot.Rng[0] = 0; snapshot.Log.Clear();
        Check(Full(battle.State.ToJsonGraph()) == before && battle.Command(new BattleRequest("detached-next", 2, "end")).V == 3, "unported fields in detached snapshots cannot inject state or bypass real end");
        before = Full(battle.State.ToJsonGraph());
        foreach (string kind in new[] { "play", "ult" }) Check(Throws(() => battle.Command(new BattleRequest("unsupported", 3, kind)), "BATTLE_COMMAND_NOT_IMPLEMENTED") && Full(battle.State.ToJsonGraph()) == before, "explicit unsupported " + kind + " remains atomic after repeated end");
        SameJson(Full(beginResponse.ToJsonGraph()), battle.Command(begin).ToJsonGraph(), "old begin receipt remains stable after later end commands");

        // Faults affect local test definitions only; every positive vector uses unchanged source content.
        foreach (var (label, mutation, code) in new (string, Action<EnemyMoveDef>, string)[]
        {
            ("unknown move", m => m.Type = "unported", "BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED"),
            ("proposal steal", m => m.Type = "steal", "BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED"),
            ("proposal eatName", m => m.Type = "eatName", "BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED"),
            ("proposal phase", m => m.Type = "phase", "BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED"),
            ("unknown move shape", m => m.Damage = 1, "BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED"),
            ("checked damage overflow", m => { m.Type = "atk"; m.Block = null; m.Damage = int.MaxValue; }, "BATTLE_NUMBER_OUT_OF_RANGE")
        })
        {
            data = BattleDataLoader.Load(root); var setup = Setup(); setup.Enemies = new List<string> { "e_smudge", "e_seed" }; setup.DifficultyDamage = 200;
            battle = Battle.Create(data, setup); var committedBegin = battle.Command(new BattleRequest("fault-begin", 0, "begin")); before = Full(battle.State.ToJsonGraph());
            var enemy = data.Enemies.Single(e => e.Id == "e_seed"); var original = enemy.Pattern[0];
            enemy.Pattern[0] = new EnemyMoveDef { Type = original.Type, Block = original.Block }; mutation(enemy.Pattern[0]);
            var request = new BattleRequest("fault-end", 1, "end");
            Check(Throws(() => battle.Command(request), code) && Full(battle.State.ToJsonGraph()) == before, label + " after prior insert rolls back seq/cards/RNG/v/log/ev");
            SameJson(Full(committedBegin.ToJsonGraph()), battle.Command(new BattleRequest("fault-begin", 0, "begin")).ToJsonGraph(), label + " preserves existing receipt");
            enemy.Pattern[0] = original;
            var corrected = battle.Command(request); var reference = Battle.Create(data, setup); reference.Command(new BattleRequest("reference-begin", 0, "begin")); var expectedEnd = reference.Command(new BattleRequest("reference-end", 1, "end"));
            Check(Full(corrected.ToJsonGraph()) == Full(expectedEnd.ToJsonGraph()) && Full(battle.State.ToJsonGraph()) == Full(reference.State.ToJsonGraph()), label + " corrected same-rid retry equals fresh real execution");
        }
        data = BattleDataLoader.Load(root); var seed = data.Enemies.Single(e => e.Id == "e_seed");
        seed.Passive = new EnemyPassiveDef { Id = "future-passive", N = 1 };
        Check(Throws(() => Battle.Create(data, Setup()), "BATTLE_ENEMY_PASSIVE_NOT_IMPLEMENTED"), "unknown enemy passive rejects creation");
        var summonSetup = Setup(); summonSetup.Enemies = new List<string> { "e_smudge", "e_gardener" };
        battle = Battle.Create(data, summonSetup); battle.Command(new BattleRequest("summon-begin", 0, "begin")); before = Full(battle.State.ToJsonGraph());
        var summonEnd = new BattleRequest("summon-end", 1, "end");
        Check(Throws(() => battle.Command(summonEnd), "BATTLE_ENEMY_PASSIVE_NOT_IMPLEMENTED") && Full(battle.State.ToJsonGraph()) == before, "unported summoned passive after insert atomically rejects");
        seed.Passive = null; var fixedSummon = battle.Command(summonEnd); var fresh = Battle.Create(data, summonSetup); fresh.Command(new BattleRequest("fresh", 0, "begin"));
        Check(Full(fixedSummon.ToJsonGraph()) == Full(fresh.Command(new BattleRequest("fresh-end", 1, "end")).ToJsonGraph()) && Full(battle.State.ToJsonGraph()) == Full(fresh.State.ToJsonGraph()), "corrected summoned passive same-rid retry has no leaked receipt or seq");

        foreach (var (value, code) in new (object, string)[] { (new Dictionary<string, object?> { ["unsupported"] = 1 }, "BATTLE_STATE_NOT_IMPLEMENTED"), (CanonicalState.MaxSafeInteger, "BATTLE_NUMBER_OUT_OF_RANGE") })
        {
            data = BattleDataLoader.Load(root); var setup = Setup("edge-notebook-continuing"); setup.RunFlags["coinsBonus"] = value;
            battle = Battle.Create(data, setup); battle.Command(new BattleRequest("coin-begin", 0, "begin")); battle.Command(new BattleRequest("coin-end1", 1, "end")); before = Full(battle.State.ToJsonGraph());
            Check(Throws(() => battle.Command(new BattleRequest("coin-end2", 2, "end")), code) && Full(battle.State.ToJsonGraph()) == before, "unsupported/overflow notebook state after kill/draw rolls back all state: " + code);
            Check(Throws(() => battle.Command(BattleDataLoader.Request("{\"rid\":\"coin-end2\",\"v\":2,\"kind\":\"end\",\"args\":{\"changed\":true}}")), code) && Full(battle.State.ToJsonGraph()) == before, "failed notebook state creates no conflicting receipt: " + code);
        }
        string temporary = Path.Combine(Path.GetTempPath(), "mf-battle-turn-loader-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(temporary, "data"));
            foreach (string key in new[] { "phase", "phases" })
            {
                var content = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")))!;
                content["enemies"]![0]![key] = new JsonObject { ["unsupported"] = true };
                File.WriteAllText(Path.Combine(temporary, "data", "game_data.json"), content.ToJsonString());
                bool rejectedData = false; try { _ = BattleDataLoader.Load(temporary); } catch (InvalidDataException) { rejectedData = true; }
                Check(rejectedData, "host loader explicitly rejects unknown enemy " + key + " metadata from temp data file");
            }
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    static bool Throws(Action action, string code) { try { action(); return false; } catch (RuleException error) { return error.Code == code; } }
    static void Same(JsonElement expected, object? actual, string label) { string? difference = JsonGraphComparison.Difference(BattleDataLoader.Graph(expected), actual, "$", 0); Check(difference == null, label + (difference == null ? "" : ": " + difference)); }
    static void RequireSame(JsonElement expected, object? actual, string label) => Require(JsonGraphComparison.Difference(BattleDataLoader.Graph(expected), actual, "$", 0) == null, label);
    static void SameJson(string expected, object? actual, string label) { using var json = JsonDocument.Parse(expected); Same(json.RootElement, actual, label); }
    static void RequireSameJson(string expected, JsonElement actual, string label) { using var json = JsonDocument.Parse(expected); RequireSame(json.RootElement, BattleDataLoader.Graph(actual), label); }
    static void Events(BattleState state, string expected, string label) => SameJson(expected, state.Events.Select(e => e.ToJsonGraph()).ToList(), label);
    static Dictionary<string, object?> Shallow(JsonElement json) => json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value, StringComparer.Ordinal);
    static string Text(JsonElement json, string key) => json.GetProperty(key).GetString() ?? throw new InvalidDataException("Expected string: " + key);
    static string Full(object? graph) => JsonSerializer.Serialize(graph);
    static string FileHash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    static string HashText(JsonElement json) { string hash = json.GetString()!; Require(hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Malformed turn hash"); return hash; }
}
