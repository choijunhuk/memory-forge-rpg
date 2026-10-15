// A1 / §5.4-6/5.10-18, §6, §16.2: real Create/begin/play/ult/end. Expected states are comparison data only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MemoryForge.Core;

static class BattlePlayerActionChecks
{
    const string Scope = "Actual Battle.create and ordered begin/play/ult/end commands for all current authored player actions; FullRun acceptance remains pending";
    static readonly Dictionary<string, string> Sources = new(StringComparer.Ordinal)
    {
        ["data"] = "data/game_data.json", ["engine"] = "engine/rules-core.js", ["memory"] = "proposals/memory_v2.cjs",
        ["canonical"] = "tools/gen_traces.mjs", ["generator"] = "tools/gen_battle_player_action_vectors.mjs", ["fuzzPolicy"] = "engine/tests/fuzz_runs.mjs"
    };
    static readonly string[] Operations = ("dmg blk heal st buff counter draw discard ap apNext gauge create pull slot loan defer restraint collateral clearDebt selfdmg " +
        "grow growPick growLowest growAll copyPick cleanse cleanseOne exhaustPick exhaustHand tune costdown recall power echo nextBoost freeNext breakBonus breakBonusAll if").Split(' ');
    static readonly string[] EdgeIds = ("edge-bulwark-retention edge-bulwark-stacks edge-pull-after-next edge-pull-immediate-original-kind " +
        "edge-defer-debt edge-debt-reservation-shared-sequence edge-echo-evolved-lineage edge-copy-pick-lineage " +
        "edge-yuri-r6-growth-ult edge-lethal-reflect edge-player-same-hit-defeat-reward edge-reserved-void-shard-kill " +
        "edge-exhaust-discard-reshuffle-live-zone edge-heal-overflow-rebirth edge-oblivion-exhaust-hooks edge-greenhouse-growth " +
        "edge-blueprint-modded-counter edge-casefile-marked edge-echo-chamber-owner-change edge-next-boost-charge " +
        "edge-free-next-empty-clock edge-recall-engraving-once edge-gauge-earned-cap-bypass edge-mark-on-lethal-hit " +
        "edge-ember-ring edge-strongest-party-heal edge-marked-conditional-true edge-intent-attack-conditional-true " +
        "edge-burning-break-bonus edge-debt-positive-effects edge-debt-break-bonus-all edge-modded-conditional-true " +
        "edge-target-low-hp-conditional-true edge-killed-this-conditional-true edge-lumi-earned-low-hp-r0 edge-lumi-earned-low-hp-r2 " +
        "edge-growth-counters-and-picks edge-growth-in-hand-false edge-exhausted-counter edge-counter-scaling " +
        "edge-block-scaling edge-missing-hp-counter edge-contract-counter-collateral edge-echo-counter-conditional " +
        "edge-aila_veil-real-debuff edge-aila_cleanse-real-debuff edge-restraint-kept-bonus edge-restraint-broken-penalty " +
        "edge-yuri-r6-once-recharge edge-growth-cap-once-and-saturation edge-growth-saturation-three edge-sera-r6-follow-up " +
        "edge-kaya-r6-refund edge-harin-r6-counter-double edge-buff-free-overload-consume edge-ult-reflect-does-not-consume-card-charge " +
        "edge-volt-first-damage-op edge-first-attack-and-owner-attribution edge-reservation-dead-retarget edge-fixed-target-mark-expiry-fizzle " +
        "edge-terminal-reservation-conservation edge-envelope-multihit-continues edge-lethal-reflect-aoe-stops-targets edge-lethal-reflect-random-stops-hits " +
        "edge-score-three-owners edge-silver-needle-real-injury edge-silver-needle-no-revival edge-silver-needle-envelope-survival " +
        "edge-frost-first-break-once edge-reserved-break-relics edge-foresight-broken-damage edge-vulnerable-weak-and-gear-math " +
        "edge-pierce-split-rounding edge-heal-up-weapon-floor-order edge-party-average-common-attribution edge-buff-max-not-add " +
        "edge-hand-overflow-generated edge-copy-delivery-after-draw-overflow edge-log400-after-real-commands edge-kaya_crossfire-marked-branch " +
        "edge-kaya_ignite-marked-branch edge-close-case-kill-refunds edge-feedback-echo-three edge-contract-seal-real-cap5 " +
        "edge-tuning-fork-real-echo4 edge-forged-lineage-does-not-grow edge-growth-once-below-cap edge-copy-pick-real-grown-root " +
        "edge-echo-resets-earned-growth-upgrade-mods edge-overheat-block-excludes-exhaust-add edge-pull-multiple-invalid-pick edge-pull-multiple-explicit-pick " +
        "edge-nia-tune-used-up-r0 edge-nia-tune-used-up-r1 edge-grow-lowest-ordinal-uid edge-selection-definition-cost-before-payable " +
        "edge-selection-curse-before-cost edge-temporary-extra-slot-resolves-after-reset edge-after-reservations-use-next-bucket edge-collateral-sealed-slot-expiry").Split(' ');
    static readonly string[] ProtocolIds = ("protocol-created-play-picks-priority protocol-created-ult-picks-priority protocol-invalid-mode-picks-priority protocol-not-in-hand " +
        "protocol-reserve-only protocol-now-only protocol-after-only protocol-ap-low " +
        "protocol-bad-target protocol-fixed-mark-required protocol-debt-cap protocol-loan-overflow " +
        "protocol-not-deferrable protocol-restraint-unavailable protocol-no-empty-slot protocol-reservation-required " +
        "protocol-pull-destination-full protocol-slot-full protocol-overload-would-kill-earned-hp1 protocol-ult-validation " +
        "protocol-ult-bad-target protocol-quick-circuit-reserve-only-has-no-mode protocol-receipts-play-replay-order").Split(' ');
    sealed class ActualStep
    {
        public JsonElement Request;
        public string Outcome = "";
        public BattleState Before = null!, State = null!;
        public Dictionary<string, object?>? Response;
    }
    sealed class ActualCase
    {
        public BattleState Created = null!;
        public List<ActualStep> Steps = new();
    }
    static int checks, failures, commands, committed, replayed, rejected, receiptProbes, checkProbes;
    static void Check(bool ok, string label) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL BattlePlayerAction " + label); } }
    static void Require(bool ok, string label) { if (!ok) throw new InvalidDataException(label); }

    public static int Run(string root)
    {
        checks = failures = commands = committed = replayed = rejected = receiptProbes = checkProbes = 0;
        try
        {
            using var document = ReadFixture(root);
            using var content = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
            var fixture = document.RootElement;
            ValidateFixture(fixture, content.RootElement, root);
            Check(true, "exact complete source-pinned action scope, request corpus and observable coverage witnesses");
            var data = BattleDataLoader.Load(root); var originalDefinitions = Catalog(data);
            var vectors = fixture.GetProperty("cases").EnumerateArray().ToDictionary(c => Text(c, "id"), StringComparer.Ordinal);
            var actual = new Dictionary<string, ActualCase>(StringComparer.Ordinal);
            foreach (var pair in vectors)
            {
                string id = pair.Key; var row = pair.Value;
                var battle = Battle.Create(data, BattleDataLoader.Setup(row.GetProperty("setup")));
                var observed = new ActualCase { Created = battle.State }; actual.Add(id, observed);
                CompareCheckpoint(row.GetProperty("created"), battle, id + " created");
                var receipts = new Dictionary<string, (BattleRequest Request, JsonElement Input, Dictionary<string, object?> Response)>(StringComparer.Ordinal);
                if (row.GetProperty("created").TryGetProperty("receipts", out var createdReceipts)) CompareReceipts(createdReceipts, receipts, battle, id + " created receipts");
                foreach (var step in row.GetProperty("steps").EnumerateArray())
                {
                    var before = observed.Steps.Count == 0 ? observed.Created : observed.Steps[^1].State;
                    string label = id + " request " + commands;
                    if (step.TryGetProperty("check", out var query) && query.ValueKind != JsonValueKind.Null) CompareCheck(query, battle, receipts, label);
                    var input = step.GetProperty("request"); var request = BattleDataLoader.Request(input);
                    string outcome = Text(step, "outcome"); commands++;
                    Dictionary<string, object?>? responseGraph = null;
                    if (outcome == "rejected")
                    {
                        string code = Text(step, "errorCode");
                        Check(Throws(() => battle.Command(request), code), label + " exact RuleError " + code); rejected++;
                        SameGraph(before.ToJsonGraph(), battle.State.ToJsonGraph(), label + " rejection preserves complete state/RNG/v/log/ev");
                        if (!string.IsNullOrEmpty(request.Rid) && !receipts.ContainsKey(request.Rid!))
                        {
                            receiptProbes++;
                            Check(Throws(() => battle.Command(new BattleRequest(request.Rid, before.V, "unknown-receipt-probe")), "UNKNOWN_COMMAND"), label + " failed request did not create a hidden receipt");
                        }
                    }
                    else
                    {
                        var response = battle.Command(request); responseGraph = response.ToJsonGraph();
                        Same(step.GetProperty("response"), responseGraph, label + " full response/version/phase/events including required nulls");
                        Check(CanonicalState.Hash(responseGraph["ev"]) == HashText(step.GetProperty("responseEventsHash")), label + " separate response event hash");
                        if (outcome == "replayed")
                        { replayed++; SameGraph(before.ToJsonGraph(), battle.State.ToJsonGraph(), label + " replay retains current complete state"); }
                        else
                        {
                            committed++;
                            Check(response.V == before.V + 1 && battle.State.V == response.V && response.Phase == battle.State.Phase, label + " one successful new version");
                            if (!string.IsNullOrEmpty(request.Rid)) receipts.Add(request.Rid!, (request, input, responseGraph));
                        }
                        var detachedState = battle.State.ToJsonGraph();
                        response.V = -1; response.Phase = "caller-mutation"; if (response.Events.Count != 0) response.Events[0].E = "caller-mutation"; response.Events.Clear();
                        SameGraph(detachedState, battle.State.ToJsonGraph(), label + " returned response/event mutation cannot corrupt live state");
                    }
                    var after = battle.State;
                    observed.Steps.Add(new ActualStep { Request = input, Outcome = outcome, Before = before, State = after, Response = responseGraph });
                    CompareCheckpoint(step, battle, label);
                    if (step.TryGetProperty("receipts", out var expectedReceipts)) CompareReceipts(expectedReceipts, receipts, battle, label + " receipts");
                }
                ProbeReceipts(receipts, battle, id + " final receipts");
                SameGraph(originalDefinitions, Catalog(data), id + " source definitions unchanged by every command");
                VerifyMainOutcome(row, observed);
            }
            VerifyCoverage(fixture, content.RootElement, actual);
            IndependentAssertions(actual);
            BoundaryChecks(root);
            FixtureRejections(fixture, content.RootElement, root);
        }
        catch (Exception error) { Check(false, "fixture/execution: " + error); }
        Console.WriteLine($"BattlePlayerActionChecks: {checks - failures}/{checks} passed; {commands} oracle requests, {committed} committed, {replayed} replayed, {rejected} rejected; {checkProbes} pure Check probes and {receiptProbes} receipt probes");
        Console.WriteLine("BattlePlayerActionScope: real fresh Create/begin/play/ult/end; full authored card/mode/effect closure is separately validated by mandatory observable witnesses");
        Console.WriteLine("BattleRunCommandReplay: NOT_IMPLEMENTED (actual Run/memory replay and server/device acceptance remain separate; generic base0 conformance UNRESOLVED)");
        return failures;
    }

    static void CompareCheck(JsonElement query, Battle battle, Dictionary<string, (BattleRequest Request, JsonElement Input, Dictionary<string, object?> Response)> receipts, string label)
    {
        var args = query.GetProperty("args"); var before = battle.State.ToJsonGraph();
        var picks = args.TryGetProperty("picks", out var list) && list.ValueKind != JsonValueKind.Null ? list.EnumerateArray().Select(v => (string)BattleDataLoader.Graph(v)!).ToList() : new List<string>();
        var supplied = picks.ToList();
        string? code = battle.Check(OptionalText(args, "uid") ?? "", OptionalText(args, "mode") ?? "now", OptionalText(args, "target"), picks);
        checkProbes++; Check(code == OptionalText(query, "code"), label + " exact pure Check code/auto target semantics");
        SameGraph(before, battle.State.ToJsonGraph(), label + " Check preserves complete state/RNG/log/ev/v");
        Check(picks.SequenceEqual(supplied), label + " Check does not consume supplied picks");
        if (receipts.Count != 0) ProbeReceipt(receipts.Last().Value, battle, label + " receipt after Check");
    }
    static void ValidateFixture(JsonElement fixture, JsonElement source, string root)
    {
        Require(Text(fixture, "schema") == "mf-battle-player-actions-v1" && Text(fixture, "scope") == Scope, "Wrong action fixture schema/scope");
        Require(Text(fixture, "status") == "COMPLETE_CURRENT_SOURCE_A1", "Action oracle closure is incomplete");
        Require(Text(fixture, "dataVersion") == Text(source.GetProperty("meta"), "version"), "Wrong action data version");
        RequireSame(fixture.GetProperty("unresolved").GetProperty("base0"), new Dictionary<string, object?>
        {
            ["cardId"] = "sion_forget", ["status"] = "ORIGINAL_SOURCE_CONFLICT",
            ["retained"] = "Current JS/live-card base5 +5*lastCount (added cap10); generic base0 conformance UNRESOLVED",
            ["anchors"] = new List<string> { "docs/source_v0.8.1/06_chapter.md:42", "docs/source_v0.8.1/09_chapter.md:833", "content/characters.mjs:310", "engine/rules-core.js:1064-1095" }
        }, "Base0 source conflict was hidden or resolved without authority");
        var records = fixture.GetProperty("sources").EnumerateObject().ToList();
        Require(records.Count == Sources.Count && records.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == records.Count && Sources.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(records.Select(p => p.Name)), "Action source membership mismatch");
        foreach (var sourceRecord in Sources)
        {
            var record = fixture.GetProperty("sources").GetProperty(sourceRecord.Key);
            Require(record.EnumerateObject().Count() == 2 && Text(record, "path") == sourceRecord.Value && HashText(record.GetProperty("sha256")) == FileHash(Path.Combine(root, sourceRecord.Value)), "Stale/substituted action source: " + sourceRecord.Key);
        }
        var cards = source.GetProperty("cards").EnumerateArray().ToList(); var characters = source.GetProperty("characters").EnumerateArray().ToList();
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var card in cards)
        {
            var modes = BaseModes(card);
            foreach (string mode in modes) expected.Add("card-" + Text(card, "id") + "-" + mode, "card");
            expected.Add("plus-" + Text(card, "id") + "-plus-" + modes[^1], "plus");
        }
        foreach (var character in characters) for (int r = 0; r <= 6; r++)
        { expected.Add($"resonance-{Text(character, "id")}-r{r}", "resonance"); expected.Add($"ult-{Text(character, "id")}-r{r}", "ult"); }
        foreach (var weapon in source.GetProperty("weapons").EnumerateArray()) for (int r = 1; r <= 5; r++) expected.Add($"weapon-{Text(weapon, "id")}-r{r}", "weapon");
        foreach (var part in source.GetProperty("parts").EnumerateArray()) expected.Add("part-" + Text(part, "id"), "part");
        foreach (var relic in source.GetProperty("relics").EnumerateArray()) expected.Add("relic-" + Text(relic, "id"), "relic");
        Require(cards.Count == 173 && cards.Sum(c => BaseModes(c).Count) == 378 && expected.Count == 849, "Current full authored action matrix changed");
        foreach (var enemy in source.GetProperty("enemies").EnumerateArray()) expected.Add("encounter-" + Text(enemy, "id") + "-player-cycle", "encounter");
        var positiveMatrixIds = expected.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (string id in EdgeIds) expected.Add(id, "edge");
        foreach (string id in ProtocolIds) expected.Add(id, "protocol");
        var rows = fixture.GetProperty("cases").EnumerateArray().ToList(); var ids = rows.Select(r => Text(r, "id")).ToList();
        Require(expected.Count == 992 && ids.Count == expected.Count && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count && expected.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(ids), "Action case membership missing/duplicate/unknown");
        var coverage = SourceCoverage(source); int requestCount = 0, commits = 0, replays = 0, rejections = 0;
        var witnessed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) { ["op"] = new(StringComparer.Ordinal), ["counter"] = new(StringComparer.Ordinal), ["condition"] = new(StringComparer.Ordinal) };
        foreach (var row in rows)
        {
            string id = Text(row, "id"), group = Text(row, "group");
            Require(expected.TryGetValue(id, out var requiredGroup) ? group == requiredGroup : group is "edge" or "protocol", "Unexpected action case/group");
            var setup = row.GetProperty("setup");
            Require(setup.EnumerateObject().All(p => new[] { "party", "deck", "hp", "maxHp", "seed", "enemies", "relics", "difficulty", "nextApBonus", "runFlags" }.Contains(p.Name, StringComparer.Ordinal)), "Execution setup contains restored state");
            foreach (var spec in setup.GetProperty("deck").EnumerateArray())
                Require(spec.EnumerateObject().All(p => new[] { "id", "plus", "mods", "growth", "from", "wielder", "assigned", "key" }.Contains(p.Name, StringComparer.Ordinal)), "Execution setup injects produced card state");
            if (group is "card" or "plus" or "resonance" or "ult" or "relic" or "part" or "encounter")
                Require(setup.GetProperty("hp").GetInt32() == 243 && setup.GetProperty("maxHp").GetInt32() == 243, "Ordinary action matrix must retain normal three-actor HP243");
            ValidateMatrixLinkage(row, source);
            var created = row.GetProperty("created"); ValidateCheckpoint(created);
            Require(created.GetProperty("state").EnumerateObject().Count() == 59 && Text(created.GetProperty("state"), "phase") == "CREATED" && created.GetProperty("state").GetProperty("v").GetInt32() == 0 && !created.GetProperty("state").TryGetProperty("pendingCopies", out _), "Action case does not begin with genuine fresh state");
            var steps = row.GetProperty("steps").EnumerateArray().ToList(); Require(steps.Count != 0, "Missing actual action requests");
            if (group != "protocol") Require(Text(steps[0].GetProperty("request"), "kind") == "begin" && Text(steps[0], "outcome") == "committed", "Positive action case does not really begin");
            JsonElement before = created;
            foreach (var step in steps)
            {
                requestCount++; ValidateCheckpoint(step); _ = BattleDataLoader.Request(step.GetProperty("request"));
                string outcome = Text(step, "outcome");
                Require(!positiveMatrixIds.Contains(id) || outcome == "committed", "Mandatory positive matrix swallowed a rejection/replay");
                if (outcome == "committed") commits++; else if (outcome == "replayed") replays++; else if (outcome == "rejected") rejections++; else throw new InvalidDataException("Unknown action outcome");
                if (outcome == "rejected")
                {
                    Require(step.GetProperty("errorCode").ValueKind == JsonValueKind.String && step.GetProperty("response").ValueKind == JsonValueKind.Null && step.GetProperty("responseEventsHash").ValueKind == JsonValueKind.Null, "Rejected action has invalid error/response evidence");
                    RequireSame(before.GetProperty("state"), BattleDataLoader.Graph(step.GetProperty("state")), "Rejected action changed expected full state");
                }
                else
                {
                    Require(step.GetProperty("errorCode").ValueKind == JsonValueKind.Null, "Successful action has an error");
                    Require(CanonicalState.Hash(BattleDataLoader.Graph(step.GetProperty("response").GetProperty("ev"))) == HashText(step.GetProperty("responseEventsHash")), "Stale response event hash");
                    if (outcome == "replayed") RequireSame(before.GetProperty("state"), BattleDataLoader.Graph(step.GetProperty("state")), "Replayed receipt changed expected current state");
                    else
                    {
                        Require(step.GetProperty("state").GetProperty("v").GetInt32() == before.GetProperty("state").GetProperty("v").GetInt32() + 1 && step.GetProperty("response").GetProperty("v").GetInt32() == step.GetProperty("state").GetProperty("v").GetInt32() && Text(step.GetProperty("response"), "phase") == Text(step.GetProperty("state"), "phase"), "Committed response/version/phase mismatch");
                        RequireSame(step.GetProperty("state").GetProperty("ev"), BattleDataLoader.Graph(step.GetProperty("response").GetProperty("ev")), "Committed response omitted/reordered state events");
                    }
                }
                Require(step.TryGetProperty("receipts", out _) == (group == "protocol"), "Protocol receipt evidence missing/unexpected");
                before = step;
            }
            if (row.TryGetProperty("witnesses", out var witnesses)) foreach (var witness in witnesses.EnumerateArray())
            {
                string kind = Text(witness, "kind"), tag = Text(witness, "tag"); int index = witness.GetProperty("stepIndex").GetInt32();
                Require(kind is "op" or "counter" or "condition" or "interaction", "Unknown coverage witness kind");
                Require(index >= 0 && index < steps.Count && (kind == "interaction" || Text(steps[index], "outcome") == "committed"), "Effect coverage witness is not an actually committed command");
                if (kind != "interaction") Require(coverage[kind].Contains(tag), "Coverage witness tag is not authored: " + kind + ":" + tag);
                var facts = witness.GetProperty("facts").EnumerateArray().ToList(); Require(facts.Count != 0, "Empty coverage witness");
                Require(kind == "interaction" || facts.Any(IsGameplayFact), "Effect coverage witness only counts generic lifecycle metadata");
                foreach (var fact in facts)
                {
                    var checkpoint = Text(fact, "at") switch { "before" => index == 0 ? created.GetProperty("state") : steps[index - 1].GetProperty("state"), "after" => steps[index].GetProperty("state"), "response" => steps[index].GetProperty("response"), _ => throw new InvalidDataException("Unknown witness checkpoint") };
                    RequireSame(fact.GetProperty("value"), FactValue(BattleDataLoader.Graph(checkpoint), fact.GetProperty("path")), "Witness literal contradicts the oracle trajectory");
                }
                if (kind != "interaction") witnessed[kind].Add(tag);
            }
        }
        foreach (string kind in new[] { "op", "counter", "condition" }) Require(witnessed[kind].SetEquals(coverage[kind]), "Incomplete observable " + kind + " coverage");
        ValidateCoverageInventory(fixture, source, rows, coverage);
        var counts = fixture.GetProperty("counts");
        Require(counts.GetProperty("cases").GetInt32() == rows.Count && counts.GetProperty("commands").GetInt32() == requestCount && counts.GetProperty("committed").GetInt32() == commits && counts.GetProperty("replayed").GetInt32() == replays && counts.GetProperty("rejected").GetInt32() == rejections, "Action corpus counts differ from concrete requests");
    }
    static void ValidateMatrixLinkage(JsonElement row, JsonElement source)
    {
        string id = Text(row, "id"), group = Text(row, "group"); var setup = row.GetProperty("setup");
        if (group is "card" or "plus" or "resonance" or "relic" || group == "part" && row.TryGetProperty("main", out _))
        {
            var main = row.GetProperty("main"); string cardId = Text(main, "cardId"), mode = Text(main, "mode"); bool plus = main.GetProperty("plus").GetBoolean();
            var card = source.GetProperty("cards").EnumerateArray().Single(c => Text(c, "id") == cardId);
            Require(BaseModes(card).Contains(mode, StringComparer.Ordinal) && setup.GetProperty("deck").EnumerateArray().Any(c => Text(c, "id") == cardId && (c.TryGetProperty("plus", out var upgraded) && upgraded.GetBoolean()) == plus), "Main card/mode/upgrade absent from actual setup");
            if (group == "card") Require(!plus && id == "card-" + cardId + "-" + mode, "Wrong base card/mode linkage");
            if (group == "plus") Require(plus && id == "plus-" + cardId + "-plus-" + BaseModes(card)[^1], "Wrong mandatory upgrade case");
        }
        if (group is "resonance" or "ult")
        {
            string character = Text(row, "characterId"); int r = row.GetProperty("resonance").GetInt32();
            Require(id == $"{group}-{character}-r{r}" && r is >= 0 and <= 6 && setup.GetProperty("party").EnumerateArray().Any(a => Text(a, "charId") == character && a.GetProperty("res").GetInt32() == r), "Wrong character/resonance linkage");
            if (group == "resonance") Require(source.GetProperty("cards").EnumerateArray().Single(c => Text(c, "id") == Text(row.GetProperty("main"), "cardId")).GetProperty("owner").GetString() == character, "Resonance case does not execute its owned card");
            if (group == "ult") Require(setup.GetProperty("party").EnumerateArray().Any(a => Text(a, "charId") == character && a.GetProperty("gauge").GetInt32() == 100), "Explicit input ultimate matrix lost gauge100 domain");
        }
        if (group == "weapon")
        {
            string weapon = Text(row, "weaponId"); int r = row.GetProperty("refine").GetInt32();
            Require(id == $"weapon-{weapon}-r{r}" && r is >= 1 and <= 5 && setup.GetProperty("party").EnumerateArray().Any(a => a.TryGetProperty("weaponId", out var w) && w.GetString() == weapon && a.GetProperty("refine").GetInt32() == r), "Wrong weapon/refinement source setup");
        }
        if (group == "part") Require(id == "part-" + Text(row, "partId"), "Wrong part case linkage");
        if (group == "relic") Require(id == "relic-" + Text(row, "relicId") && setup.GetProperty("relics").GetArrayLength() == 1 && setup.GetProperty("relics")[0].GetString() == Text(row, "relicId"), "Wrong single relic setup");
        if (group == "encounter") Require(id == "encounter-" + Text(row, "enemyId") + "-player-cycle" && setup.GetProperty("enemies").GetArrayLength() == 1 && setup.GetProperty("enemies")[0].GetString() == Text(row, "enemyId"), "Wrong live-enemy player cycle linkage");
    }
    static void ValidateCheckpoint(JsonElement point)
    {
        var state = point.GetProperty("state"); var view = point.GetProperty("publicView");
        Require(!new[] { "rng", "cards", "zones", "draw" }.Any(k => view.TryGetProperty(k, out _)), "Action public view exposes hidden state");
        foreach (var (value, hash) in new[] { (state, "stateHash"), (state.GetProperty("rng"), "rngHash"), (state.GetProperty("ev"), "eventsHash"), (state.GetProperty("log"), "logHash"), (view, "publicViewHash") })
            Require(CanonicalState.Hash(BattleDataLoader.Graph(value)) == HashText(point.GetProperty(hash)), "Stale action checkpoint " + hash);
    }
    static Dictionary<string, HashSet<string>> SourceCoverage(JsonElement source)
    {
        var ops = new HashSet<string>(StringComparer.Ordinal); var counters = new HashSet<string>(StringComparer.Ordinal); var conditions = new HashSet<string>(StringComparer.Ordinal); var fields = new HashSet<string>(StringComparer.Ordinal);
        void Walk(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array) { foreach (var child in value.EnumerateArray()) Walk(child); }
            if (value.ValueKind != JsonValueKind.Object) return;
            if (value.TryGetProperty("op", out var op)) { ops.Add(op.GetString()!); foreach (var property in value.EnumerateObject()) fields.Add(property.Name); }
            if (value.TryGetProperty("per", out var per) && value.TryGetProperty("k", out _)) counters.Add(per.GetString()!);
            if (value.TryGetProperty("c", out var condition) && condition.ValueKind == JsonValueKind.String) { conditions.Add(condition.GetString()! + ":true"); conditions.Add(condition.GetString()! + ":false"); }
            foreach (var property in value.EnumerateObject()) Walk(property.Value);
        }
        Walk(source.GetProperty("cards")); Walk(source.GetProperty("characters"));
        Require(ops.SetEquals(Operations) && counters.Count == 12 && conditions.Count == 20 && fields.Count == 35, "Authored effect/counter/condition/field inventory changed");
        return new(StringComparer.Ordinal) { ["op"] = ops, ["counter"] = counters, ["condition"] = conditions, ["field"] = fields };
    }
    static void ValidateCoverageInventory(JsonElement fixture, JsonElement source, List<JsonElement> rows, Dictionary<string, HashSet<string>> authored)
    {
        var coverage = fixture.GetProperty("coverage");
        void Set(string key, IEnumerable<string> values)
        {
            var actual = coverage.GetProperty(key).EnumerateArray().Select(v => (string)BattleDataLoader.Graph(v)!).ToList(); var expected = values.ToHashSet(StringComparer.Ordinal);
            Require(actual.Count == actual.Distinct(StringComparer.Ordinal).Count() && expected.SetEquals(actual), "Exact action coverage set differs: " + key);
        }
        var cards = source.GetProperty("cards").EnumerateArray().ToList(); var characters = source.GetProperty("characters").EnumerateArray().ToList();
        Set("cards", cards.Select(c => Text(c, "id"))); Set("plusCards", cards.Select(c => Text(c, "id")));
        Set("baseModePairs", cards.SelectMany(c => BaseModes(c).Select(m => Text(c, "id") + ":" + m)));
        foreach (string key in new[] { "characterResonances", "ultimateResonances" }) Set(key, characters.SelectMany(c => Enumerable.Range(0, 7).Select(r => Text(c, "id") + ":r" + r)));
        Set("weaponRefinements", source.GetProperty("weapons").EnumerateArray().SelectMany(w => Enumerable.Range(1, 5).Select(r => Text(w, "id") + ":r" + r)));
        foreach (string key in new[] { "parts", "relics", "enemies" }) Set(key, source.GetProperty(key).EnumerateArray().Select(d => Text(d, "id")));
        Set("ops", authored["op"]); Set("counters", authored["counter"]); Set("conditionBranches", authored["condition"]); Set("conditions", authored["condition"].Select(c => c[..c.LastIndexOf(':')])); Set("effectFields", authored["field"]);
        foreach (var (group, key) in new[] { ("edge", "edgeIds"), ("protocol", "protocolIds"), ("encounter", "encounterIds") }) Set(key, rows.Where(r => Text(r, "group") == group).Select(r => Text(r, "id")));
        var references = new List<object?>();
        foreach (var row in rows) if (row.TryGetProperty("witnesses", out var witnesses))
        {
            int index = 0;
            foreach (var witness in witnesses.EnumerateArray()) references.Add(new Dictionary<string, object?> { ["caseId"] = Text(row, "id"), ["witnessIndex"] = index++, ["kind"] = Text(witness, "kind"), ["tag"] = Text(witness, "tag"), ["stepIndex"] = witness.GetProperty("stepIndex").GetInt32() });
        }
        Require(references.Count == 214, "Mandatory independent literal witness inventory changed"); RequireSame(coverage.GetProperty("witnesses"), references, "Observable witness reference inventory differs from actual rows");
        var effects = new List<object?>();
        void Visit(JsonElement value, string path, string? cardId, string? characterId)
        {
            if (value.ValueKind == JsonValueKind.Array) { int i = 0; foreach (var child in value.EnumerateArray()) Visit(child, path + "/" + i++, cardId, characterId); return; }
            if (value.ValueKind != JsonValueKind.Object) return;
            if (value.TryGetProperty("op", out var op))
            {
                var entry = new Dictionary<string, object?> { ["source"] = "data/game_data.json#/" + path, ["op"] = op.GetString(), ["shapeHash"] = CanonicalState.Hash(BattleDataLoader.Graph(value)) };
                if (cardId != null) entry["cardId"] = cardId;
                entry["caseIds"] = cardId == null ? new List<string> { "ult-" + characterId + "-r6" } : BaseModes(cards.Single(c => Text(c, "id") == cardId)).Select(m => "card-" + cardId + "-" + m).ToList();
                effects.Add(entry);
            }
            foreach (var property in value.EnumerateObject()) Visit(property.Value, path + "/" + property.Name, cardId, characterId);
        }
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i]; string cardId = Text(card, "id"); Visit(card.GetProperty("eff"), "cards/" + i + "/eff", cardId, null);
            foreach (string optional in new[] { "plus", "costIf" }) if (card.TryGetProperty(optional, out var value)) Visit(value, "cards/" + i + "/" + optional, cardId, null);
        }
        for (int i = 0; i < characters.Count; i++)
        {
            var character = characters[i]; string characterId = Text(character, "id");
            Visit(character.GetProperty("ult"), "characters/" + i + "/ult", null, characterId); Visit(character.GetProperty("resonance"), "characters/" + i + "/resonance", null, characterId);
        }
        Require(effects.Count == 295, "Authored current effect-path inventory changed"); RequireSame(fixture.GetProperty("authoredEffects"), effects, "Authored effect source paths/shape hashes/case links are incomplete");
        Require(fixture.GetProperty("reachability").GetProperty("battleOnly").GetBoolean() && Text(fixture.GetProperty("reachability"), "runAcceptance") == "PENDING" && Text(fixture.GetProperty("reachability"), "positiveSilenceUntil").StartsWith("UNREACHABLE:", StringComparison.Ordinal), "Unsupported Run/silence reachability is misrepresented");
    }
    static bool IsGameplayFact(JsonElement fact)
    {
        var path = fact.GetProperty("path"); if (path.GetArrayLength() == 0 || path[0].ValueKind != JsonValueKind.String) return false;
        string head = path[0].GetString()!;
        return Text(fact, "at") == "response" ? head == "ev" : !new[] { "schema", "v", "turn", "phase", "rng", "log", "ev", "result" }.Contains(head, StringComparer.Ordinal);
    }
    static object? FactValue(object? value, JsonElement path)
    {
        foreach (var part in path.EnumerateArray())
        {
            if (value is IList list)
            {
                if (part.ValueKind == JsonValueKind.String && part.GetString() == "length") { value = list.Count; continue; }
                Require(part.TryGetInt32(out int index) && index >= 0 && index < list.Count, "Invalid witness array index"); value = list[index];
            }
            else { var obj = Object(value); string key = (string)BattleDataLoader.Graph(part)!; Require(obj.TryGetValue(key, out value), "Missing witness object path " + key); }
        }
        return value;
    }
    static List<string> BaseModes(JsonElement card) => Text(card, "res") switch { "y" => new() { "now", "next", "after" }, "o" => new() { "next", "after" }, "after" => new() { "after" }, _ => new() { "now" } };
    static void VerifyCoverage(JsonElement fixture, JsonElement source, Dictionary<string, ActualCase> actual)
    {
        var coverage = SourceCoverage(source);
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) { ["op"] = new(StringComparer.Ordinal), ["counter"] = new(StringComparer.Ordinal), ["condition"] = new(StringComparer.Ordinal) };
        int baseCases = 0, witnessedFacts = 0, ultCases = 0;
        foreach (var row in fixture.GetProperty("cases").EnumerateArray())
        {
            string id = Text(row, "id"), group = Text(row, "group"); var trace = actual[id];
            if (group == "card") baseCases++;
            if (group == "ult")
            {
                ultCases++; string actor = Text(row, "characterId");
                var ult = trace.Steps.Where(s => s.Outcome == "committed" && Text(s.Request, "kind") == "ult").ToList();
                Check(ult.Count == 1 && OptionalText(ult[0].Request.GetProperty("args"), "actor") == actor && ult[0].Before.Actors.Single(a => a.Id == actor).Gauge == 100, id + " actual ready actor ultimate executes once");
            }
            if (group == "weapon")
            {
                string weaponCard = Text(source.GetProperty("weapons").EnumerateArray().Single(w => Text(w, "id") == Text(row, "weaponId")), "card");
                Check(trace.Steps.Where(s => s.Response != null).SelectMany(s => Array(s.Response!["ev"]).Cast<Dictionary<string, object?>>()).Any(e => e["e"] is "resolved" && e["id"] is string resolvedId && resolvedId == weaponCard && e["fizzled"] is false), id + " equipped weapon card actually resolves without fizzle");
            }
            if (group == "part")
            {
                string part = Text(row, "partId");
                var attached = trace.Steps.SelectMany(s => s.Before.Cards.Values).Where(c => c.Circuit == part || c.Edge == part || c.Temp.Contains(part)).Select(c => c.Uid).ToHashSet(StringComparer.Ordinal);
                Check(attached.Count != 0 && trace.Steps.Where(s => s.Response != null).SelectMany(s => Array(s.Response!["ev"]).Cast<Dictionary<string, object?>>()).Any(e => e["e"] is "resolved" && attached.Contains((string)e["uid"]!) && e["fizzled"] is false), id + " actual source attachment is carried by a resolved card");
            }
            if (!row.TryGetProperty("witnesses", out var witnesses)) continue;
            foreach (var witness in witnesses.EnumerateArray())
            {
                string kind = Text(witness, "kind"), tag = Text(witness, "tag"); var step = trace.Steps[witness.GetProperty("stepIndex").GetInt32()];
                Check(kind == "interaction" || step.Outcome == "committed", id + " " + kind + ":" + tag + " is a real new command execution");
                foreach (var fact in witness.GetProperty("facts").EnumerateArray())
                {
                    object? graph = Text(fact, "at") switch { "before" => step.Before.ToJsonGraph(), "after" => step.State.ToJsonGraph(), "response" => step.Response, _ => throw new InvalidDataException("Unknown actual witness point") };
                    Same(fact.GetProperty("value"), FactValue(graph, fact.GetProperty("path")), id + " independent " + kind + ":" + tag + " literal"); witnessedFacts++;
                }
                if (kind != "interaction") seen[kind].Add(tag);
            }
        }
        Check(baseCases == 378 && ultCases == 70 && witnessedFacts > 0, "all378 mandatory base card/mode paths and70 actual character/resonance ultimates plus independent facts");
        foreach (string kind in new[] { "op", "counter", "condition" }) Check(seen[kind].SetEquals(coverage[kind]), "actual C# observable complete " + kind + " coverage");
        var counts = fixture.GetProperty("counts");
        Check(commands == counts.GetProperty("commands").GetInt32() && committed == counts.GetProperty("committed").GetInt32() && replayed == counts.GetProperty("replayed").GetInt32() && rejected == counts.GetProperty("rejected").GetInt32(), "actual C# request/outcome counts equal complete concrete corpus");
        Check(commands == 3870 && committed == 3837 && replayed == 1 && rejected == 32 && checkProbes == 1430, "final992-case corpus actually executes3870 requests and1430 pure Check records");
        Console.WriteLine($"BattlePlayerActionCoverage: {actual.Count} actual cases; {baseCases} base pairs; {seen["op"].Count} ops, {seen["counter"].Count} counters, {seen["condition"].Count} condition outcomes; {witnessedFacts} observable facts");
    }
    static void IndependentAssertions(Dictionary<string, ActualCase> actual)
    {
        ActualStep Play(string id, string card) => actual[id].Steps.Single(s => s.Outcome == "committed" && Text(s.Request, "kind") == "play" && s.Response != null && Array(s.Response["ev"]).Cast<Dictionary<string, object?>>().Any(e => e["e"] is "play" && e["id"] is string cardId && cardId == card));
        List<ActualStep> Ends(string id) => actual[id].Steps.Where(s => s.Outcome == "committed" && Text(s.Request, "kind") == "end").ToList();
        var bulwark = Play("edge-bulwark-retention", "harin_bulwark");
        Check(bulwark.Before.Block == 26 && bulwark.Before.Counter == 2 && bulwark.State.Events.Single(e => e.E == "play").Cost == 2 && bulwark.State.Powers.Single(p => p.Id == "bulwark").Keep == 10 && bulwark.State.Cards["c1"].Used == 1 && bulwark.State.Zones.Exhaust.SequenceEqual(new[] { "c1" }) && bulwark.State.Actors.Single(a => a.Id == "harin").Gauge == 10, "independent actual bulwark costs2/resolves/exhausts with authored keep10");
        var kept = Ends("edge-bulwark-retention")[0].State;
        Check(kept.Turn == 2 && kept.Block == 10 && kept.Counter == 0, "independent real play→end retains bulwark10 and clears ordinary counter");
        var pull = Play("edge-pull-after-next", "sera_pull").State; var reservation = pull.Reservations.Single();
        Check(reservation.Kind == "next" && reservation.Due == 2 && reservation.Order == 1 && reservation.Delayed == null, "independent pull converts after→next/due2 without stale delayed metadata");
        var pulled = Ends("edge-pull-after-next")[0];
        Check(pulled.State.Enemies[0].Hp == 34 && pulled.State.Actors.Single(a => a.Id == "sera").Gauge == 20 && pulled.State.Reservations.Count == 0 && pulled.State.Events.Any(e => e.E == "dmg" && e.N == 22 && e.Blocked == 10), "independent converted reservation loses ra30/c_far70% and deals22 against block10");
        var deferred = Play("edge-defer-debt", "ravien_defer").State; var debt = deferred.Debts.Single();
        Check(deferred.Ap == 6 && debt.Id == "d1" && debt.N == 3 && debt.Due == 3 && debt.Deferred, "independent actual loan/defer debt d1/n3/due3/deferred true/AP6");
        var repaid = Ends("edge-defer-debt");
        Check(repaid[0].State.Turn == 2 && repaid[0].State.Ap == 4 && repaid[1].State.Turn == 3 && repaid[1].State.Ap == 1 && repaid[1].State.Debts.Count == 0 && repaid[1].State.Events.Any(e => e.E == "repay" && e.N == 3), "independent debt persists through turn2 then actual repay3 leaves turn3 AP1");
        var echo = Ends("edge-echo-evolved-lineage");
        Check(echo[0].State.Turn == 2 && echo[0].State.Block == 19 && echo[0].State.PendingCopies!.Single().Due == 3, "independent evolved reserved block13+Yuri6 and copy pendingdue3");
        var delivered = echo[1].State; var copy = delivered.Cards["c3"];
        Check(delivered.Turn == 3 && delivered.Zones.Hand.SequenceEqual(new[] { "c1", "c3" }) && delivered.PendingCopies != null && delivered.PendingCopies.Count == 0 && delivered.ToJsonGraph().ContainsKey("pendingCopies"), "independent normal draw precedes copy delivery and pendingCopies retains present-empty");
        Check(copy.Id == "yuri_cocoon_guard" && copy.From == "yuri_cocoon" && copy.Wielder == null && copy.Token && copy.Free && copy.EchoCopy == true && !copy.Plus && copy.Circuit == null && copy.Edge == null && copy.Growth == 0 && !copy.Grown && copy.Used == 0 && copy.Temp.SequenceEqual(new[] { "echoHalf" }), "independent c_echo delivery resets upgrade/mod/growth metadata and carries explicit lineage/echoHalf/free token");
        var growth = actual["edge-yuri-r6-growth-ult"].Steps.Single(s => s.Outcome == "committed" && Text(s.Request, "kind") == "ult").State;
        Check(growth.Block == 24 && growth.GrowthCount == 0 && growth.Actors.Single(a => a.Id == "yuri").Gauge == 0 && growth.Events.Count(e => e.E == "grow") == 4, "independent real YuriR6 ultimate produces four uncapped grows/block24/gauge0");
        var reflected = Play("edge-lethal-reflect", "sera_double").State;
        Check(reflected.Phase == "DEFEAT" && reflected.Hp == 0 && reflected.Enemies[0].Hp == 48 && reflected.Events.Count(e => e.E == "dmg") == 1 && reflected.Events.Any(e => e.E == "dmg" && e.N == 4) && reflected.Events.Any(e => e.E == "hpLoss" && e.N == 3 && e.Source == "reflect") && reflected.Cards["c1"].Used == 1 && reflected.Zones.Discard.Count(c => c == "c1") == 1 && reflected.Events.FindIndex(e => e.E == "end") < reflected.Events.FindIndex(e => e.E == "resolved"), "independent first lethal reflect stops later hits but preserves post-terminal used/discard/resolved");
        var shard = Ends("edge-reserved-void-shard-kill").Last().State;
        Check(shard.Phase == "VICTORY" && shard.Turn == 3 && shard.Hp == 57 && shard.Enemies[0].Hp == 0 && shard.Zones.Exhaust.Contains("c1") && shard.Events.Count(e => e.E == "end") == 1 && shard.Events.All(e => e.E != "turn"), "independent reserved45 plus actual exhaust relic3 kills before turn3 normal draw/intents/event");
        var live = Play("edge-exhaust-discard-reshuffle-live-zone", "sion_forget");
        Check(live.Before.Zones.Draw.Count == 0 && live.Before.Zones.Hand.SequenceEqual(new[] { "c1", "c5" }) && live.Before.Zones.Discard.SequenceEqual(new[] { "c3", "c2", "c6", "c4" }), "independent live-discard regression preparation uses actual card commands/order");
        Check(live.State.Phase == "PLAYER" && live.State.Block == 10 && live.State.KilledMarked == 1 && live.State.Zones.Draw.SequenceEqual(new[] { "c6" }) && live.State.Zones.Exhaust.SequenceEqual(new[] { "c3" }) && live.State.Events.Where(e => e.E == "exhaust").Select(e => e.Uid).SequenceEqual(new[] { "c3" }), "independent notebook reshuffle changes the live discard list; only c3 exhausts, c6 remains draw, no false victory");
        var copied = Play("edge-copy-pick-lineage", "yuri_metamorph");
        var original = copied.Before.Cards.Values.Single(c => c.Id == "yuri_cocoon_guard" && c.Plus);
        var pickedCopy = copied.State.Cards.Where(p => !copied.Before.Cards.ContainsKey(p.Key)).Select(p => p.Value).Single();
        Check(pickedCopy.Id == original.Id && pickedCopy.Plus == original.Plus && pickedCopy.Circuit == original.Circuit && pickedCopy.Edge == original.Edge && pickedCopy.From == original.From && pickedCopy.Growth == original.Growth && pickedCopy.Token && pickedCopy.Free && pickedCopy.EchoCopy == true && pickedCopy.Temp.Count == 0 && pickedCopy.Used == 0 && pickedCopy.Wielder == null && pickedCopy.Assigned == null, "independent copyPick preserves source plus/mods/from/growth and resets temp/owner metadata without echoHalf");
        var sameHit = actual["edge-player-same-hit-defeat-reward"].Steps.Last(s => s.Outcome == "committed" && Text(s.Request, "kind") == "play").State;
        Check(sameHit.Phase == "DEFEAT" && sameHit.Hp == 0 && sameHit.Ap == 4 && sameHit.KilledMarked == 1 && Convert.ToInt64(sameHit.RunFlags["coinsBonus"], CultureInfo.InvariantCulture) == 10 && sameHit.Cards["c1"].Used == 1 && sameHit.Zones.Discard.Count(c => c == "c1") == 1 && sameHit.Events.Select(e => e.E).SequenceEqual(new[] { "play", "dmg", "hpLoss", "kill", "end", "resolved" }), "independent PLAYER same-hit reflect keeps marked refund/notebook reward then defeat wins before afterResolve cleanup");
        Check(sameHit.Events[0].Cost == 1 && sameHit.Events[1].N == 8 && sameHit.Events[1].Blocked == 0 && sameHit.Events[2].N == 3 && sameHit.Events[2].Source == "reflect", "independent same-hit exact card8/reflect3/cost1 values");
    }
    static void CompareCheckpoint(JsonElement point, Battle battle, string label)
    {
        var state = battle.State; var graph = state.ToJsonGraph(); var view = battle.PublicView();
        Same(point.GetProperty("state"), graph, label + " complete state including diagnostics and optional/null fields");
        Same(point.GetProperty("publicView"), view, label + " complete public view");
        foreach (var (value, hash) in new (object?, string)[] { (graph, "stateHash"), (state.Rng, "rngHash"), (state.Events.Select(e => e.ToJsonGraph()).ToList(), "eventsHash"), (state.Log.Select(e => e.ToJsonGraph()).ToList(), "logHash"), (view, "publicViewHash") })
            Check(CanonicalState.Hash(value) == HashText(point.GetProperty(hash)), label + " " + hash);
        Check(!view.Keys.Intersect(new[] { "rng", "cards", "zones", "draw" }).Any(), label + " public secrets excluded");
        Check(point.GetProperty("state").GetProperty("cards").EnumerateObject().Select(p => p.Name).SequenceEqual(Object(graph["cards"]).Keys), label + " card creation/enumeration order");
        var seen = state.Zones.Draw.Concat(state.Zones.Hand).Concat(state.Zones.Discard).Concat(state.Zones.Exhaust).Concat(state.Reservations.Select(r => r.Uid)).ToList();
        Check(seen.Count == state.Cards.Count && seen.Distinct(StringComparer.Ordinal).Count() == seen.Count && seen.All(state.Cards.ContainsKey), label + " every card occupies exactly one zone/reservation");
        foreach (var e in state.Events.Select(e => e.ToJsonGraph())) ValidateEventPresence(e, label);
    }
    static void ValidateEventPresence(Dictionary<string, object?> e, string label)
    {
        string kind = (string)e["e"]!;
        if (kind == "play") Check(e.ContainsKey("target") && e["cost"] is int, label + " play retains nullable target and integer cost");
        if (kind == "resolved") Check(e.ContainsKey("reserved") && e["fizzled"] is bool, label + " resolved retains nullable reserved and false fizzled");
        if (kind == "dmg")
        {
            string source = (string)e["src"]!;
            if (source == "ult") Check(e.ContainsKey("card") && e["card"] == null, label + " ultimate damage explicitly retains card:null");
            else if (source == "card") Check(e.TryGetValue("card", out var card) && card is string, label + " card damage retains source card");
            else Check(!e.ContainsKey("card"), label + " raw damage omits unrelated card field");
        }
    }
    static void CompareReceipts(JsonElement expected, Dictionary<string, (BattleRequest Request, JsonElement Input, Dictionary<string, object?> Response)> receipts, Battle battle, string label)
    {
        var records = expected.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        Check(records.Count == receipts.Count && records.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(receipts.Keys), label + " exact receipt membership");
        foreach (var pair in receipts)
        {
            if (!records.TryGetValue(pair.Key, out var record)) continue;
            Check(Text(record, "digest") == RequestIdentity(pair.Value.Input), label + " exact ordered receipt body including log/ev");
            Same(record.GetProperty("resp"), pair.Value.Response, label + " full stored response/event evidence");
        }
        ProbeReceipts(receipts, battle, label);
    }
    static void ProbeReceipts(Dictionary<string, (BattleRequest Request, JsonElement Input, Dictionary<string, object?> Response)> receipts, Battle battle, string label)
    { foreach (var receipt in receipts.Values) ProbeReceipt(receipt, battle, label); }
    static void ProbeReceipt((BattleRequest Request, JsonElement Input, Dictionary<string, object?> Response) receipt, Battle battle, string label)
    {
        var before = battle.State.ToJsonGraph(); var response = battle.Command(receipt.Request); receiptProbes++;
        SameGraph(receipt.Response, response.ToJsonGraph(), label + " detached black-box replay after later commands");
        response.Events.Clear(); response.Phase = "caller-mutation";
        SameGraph(before, battle.State.ToJsonGraph(), label + " replay/mutation preserves full current state");
    }
    static void VerifyMainOutcome(JsonElement row, ActualCase actual)
    {
        if (!row.TryGetProperty("main", out var main)) return;
        string cardId = Text(main, "cardId"), mode = Text(main, "mode"); bool plus = main.GetProperty("plus").GetBoolean();
        var plays = actual.Steps.Where(s => s.Outcome == "committed" && Text(s.Request, "kind") == "play")
            .Where(s => s.Response != null && Array(s.Response["ev"]).Cast<Dictionary<string, object?>>().Any(e => e["e"] is "play" && e["id"] is string id && id == cardId && e["mode"] is string playedMode && playedMode == mode))
            .ToList();
        Check(plays.Count == 1, Text(row, "id") + " main card/mode actually played once");
        if (plays.Count != 1) return;
        string uid = (string)BattleDataLoader.Graph(plays[0].Request.GetProperty("args").GetProperty("uid"))!;
        var events = actual.Steps.Where(s => s.Response != null).SelectMany(s => Array(s.Response!["ev"]).Cast<Dictionary<string, object?>>()).ToList();
        var resolved = events.Where(e => e["e"] is "resolved" && e["uid"] is string resolvedUid && resolvedUid == uid).ToList();
        var final = actual.Steps[^1].State;
        Check(plays[0].Before.Cards[uid].Id == cardId && plays[0].Before.Cards[uid].Plus == plus && resolved.Count == 1 && resolved[0]["fizzled"] is false && final.Cards[uid].Used == 1 && final.Reservations.All(r => r.Uid != uid), Text(row, "id") + " mandatory actual resolved nonfizzle/used1/closed reservation path");
        var expected = row.GetProperty("mainOutcome");
        Check(Text(expected, "uid") == uid && expected.GetProperty("resolved").GetBoolean() && !expected.GetProperty("fizzled").GetBoolean() && expected.GetProperty("used").GetInt32() == final.Cards[uid].Used, Text(row, "id") + " main outcome evidence matches actual producing command");
    }

    static void BoundaryChecks(string root)
    {
        BattleSetup Setup(string card = "sera_round", bool faultParty = false) => new()
        {
            Party = new List<BattleActorSetup> { new() { CharId = faultParty ? "lumi" : "sera" }, new() { CharId = "harin", Res = faultParty ? 4 : 0 }, new() { CharId = faultParty ? "sera" : "nia" } },
            Deck = new List<BattleCardSpec> { new() { Id = card } }, Enemies = new List<string> { "b_archivist" }, Hp = 243, MaxHp = 243, Seed = 0
        };
        var data = BattleDataLoader.Load(root);
        foreach (string kind in new[] { "play", "ult" })
        {
            var battle = Battle.Create(data, Setup()); var before = battle.State.ToJsonGraph();
            var invalid = BattleDataLoader.Request("{\"rid\":\"deferred-picks\",\"v\":0,\"kind\":\"" + kind + "\",\"args\":{\"uid\":\"c1\",\"actor\":\"sera\",\"mode\":\"now\",\"picks\":true}}");
            Check(Throws(() => battle.Command(invalid), "WRONG_PHASE"), "CREATED " + kind + " phase error precedes malformed picks decoding");
            SameGraph(before, battle.State.ToJsonGraph(), "CREATED malformed-picks rejection preserves complete state/RNG/log/ev/v");
            var retry = new BattleRequest("deferred-picks", 0, "begin"); var response = battle.Command(retry);
            var reference = Battle.Create(data, Setup()); var expected = reference.Command(new BattleRequest("reference", 0, "begin"));
            SameGraph(expected.ToJsonGraph(), response.ToJsonGraph(), "CREATED rejected " + kind + " request ID reusable for real begin");
            SameGraph(reference.State.ToJsonGraph(), battle.State.ToJsonGraph(), "CREATED rejected " + kind + " leaves no state/receipt/RNG residue");
            SameGraph(response.ToJsonGraph(), battle.Command(retry).ToJsonGraph(), "corrected begin receipt replay after malformed picks");
        }
        {
            var battle = Battle.Create(data, Setup()); battle.Command(new BattleRequest("begin", 0, "begin")); var before = battle.State.ToJsonGraph();
            var invalid = BattleDataLoader.Request("{\"rid\":\"bad-mode-picks\",\"v\":1,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"mode\":\"bad\",\"picks\":true}}");
            Check(Throws(() => battle.Command(invalid), "INVALID_MODE"), "PLAYER invalid mode precedes malformed picks decoding");
            SameGraph(before, battle.State.ToJsonGraph(), "PLAYER invalid-mode/picks rejection preserves complete state");
            var corrected = BattleDataLoader.Request("{\"rid\":\"bad-mode-picks\",\"v\":1,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"mode\":\"now\",\"target\":\"e2\",\"picks\":[]}}");
            var response = battle.Command(corrected); var reference = Battle.Create(data, Setup()); reference.Command(new BattleRequest("reference", 0, "begin"));
            var expected = reference.Command(BattleDataLoader.Request("{\"rid\":\"reference-play\",\"v\":1,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"mode\":\"now\",\"target\":\"e2\",\"picks\":[]}}"));
            SameGraph(expected.ToJsonGraph(), response.ToJsonGraph(), "invalid-mode failed request ID reusable for actual play");
            SameGraph(reference.State.ToJsonGraph(), battle.State.ToJsonGraph(), "invalid-mode failed play leaks no state/RNG/receipt");
        }
        {
            var battle = Battle.Create(data, Setup()); battle.Command(new BattleRequest("begin", 0, "begin")); var before = battle.State.ToJsonGraph();
            var picks = new List<string> { "invalid-pick" };
            Check(battle.Check("c1", "now", "auto", picks) == null && picks.SequenceEqual(new[] { "invalid-pick" }), "pure Check auto-target leaves supplied picks unchanged");
            SameGraph(before, battle.State.ToJsonGraph(), "pure Check auto consumes no state or RNG");
            var directAuto = BattleDataLoader.Request("{\"rid\":\"auto\",\"v\":1,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"mode\":\"now\",\"target\":\"auto\",\"picks\":[]}}");
            Check(Throws(() => battle.Command(directAuto), "BAD_TARGET"), "direct Command does not apply Check's auto-target helper coercion");
            SameGraph(before, battle.State.ToJsonGraph(), "direct auto-target rejection is atomic");
            var request = BattleDataLoader.Request("{\"rid\":\"auto\",\"v\":1,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"mode\":\"now\",\"target\":null,\"picks\":[],\"log\":[\"request\"],\"ev\":[]}}");
            var response = battle.Command(request); var originalResponse = response.ToJsonGraph();
            battle.Command(new BattleRequest("later-end", 2, "end")); before = battle.State.ToJsonGraph();
            response.Events.Clear(); response.Phase = "caller-mutation";
            SameGraph(originalResponse, battle.Command(request).ToJsonGraph(), "play receipt survives returned mutation and a later real end");
            foreach (string args in new[] {
                "{\"mode\":\"now\",\"uid\":\"c1\",\"target\":null,\"picks\":[],\"log\":[\"request\"],\"ev\":[]}",
                "{\"uid\":\"c1\",\"mode\":\"now\",\"target\":null,\"picks\":[],\"log\":[],\"ev\":[]}",
                "{\"uid\":\"c1\",\"mode\":\"now\",\"target\":null,\"picks\":[],\"log\":[\"request\"],\"ev\":[1]}" })
                Check(Throws(() => battle.Command(BattleDataLoader.Request("{\"rid\":\"auto\",\"v\":1,\"kind\":\"play\",\"args\":" + args + "}")), "IDEMPOTENCY_CONFLICT"), "play receipt retains body order/log/ev before stale/hand checks");
            SameGraph(before, battle.State.ToJsonGraph(), "all play receipt conflicts preserve complete later state");
        }
        foreach (bool unsupported in new[] { false, true })
        {
            // Faults modify only local typed definitions after real Create/begin.
            data = BattleDataLoader.Load(root); var setup = Setup("lumi_storm", true); var battle = Battle.Create(data, setup);
            var begin = new BattleRequest("fault-begin", 0, "begin"); var beginResponse = battle.Command(begin).ToJsonGraph();
            var before = battle.State.ToJsonGraph(); var effects = data.Cards.Single(c => c.Id == "lumi_storm").Effects;
            effects.Add(unsupported ? new EffectDef { Op = "unported" } : new EffectDef { Op = "blk", V = int.MaxValue });
            var faultyDefinitions = Catalog(data); string code = unsupported ? "UNSUPPORTED_EFFECT" : "BATTLE_NUMBER_OUT_OF_RANGE";
            var play = BattleDataLoader.Request("{\"rid\":\"fault-play\",\"v\":1,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"mode\":\"now\",\"target\":\"e2\",\"picks\":[]}}");
            Check(Throws(() => battle.Command(play), code), "fault after real selfdmg/six random hits rejects with " + code);
            SameGraph(before, battle.State.ToJsonGraph(), "late effect fault rolls back AP/hand/gauge/HP/enemy/RNG/log/ev/version");
            SameGraph(faultyDefinitions, Catalog(data), "late effect fault leaves original local definitions unchanged");
            SameGraph(beginResponse, battle.Command(begin).ToJsonGraph(), "late effect fault preserves the prior committed receipt");
            var changed = BattleDataLoader.Request("{\"rid\":\"fault-play\",\"v\":1,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"mode\":\"now\",\"target\":\"e2\",\"picks\":[],\"changed\":true}}");
            Check(Throws(() => battle.Command(changed), code), "late failed request did not reserve its ID or conflict with a changed body");
            effects.RemoveAt(effects.Count - 1);
            var response = battle.Command(play); var reference = Battle.Create(data, setup); reference.Command(new BattleRequest("reference-begin", 0, "begin"));
            var expected = reference.Command(BattleDataLoader.Request("{\"rid\":\"reference-play\",\"v\":1,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"mode\":\"now\",\"target\":\"e2\",\"picks\":[]}}"));
            SameGraph(expected.ToJsonGraph(), response.ToJsonGraph(), "corrected same-ID retry matches fresh random-hit execution");
            SameGraph(reference.State.ToJsonGraph(), battle.State.ToJsonGraph(), "corrected retry restores exact state/RNG with no candidate leak");
        }
        {
            data = BattleDataLoader.Load(root);
            var setup = new BattleSetup { Party = new List<BattleActorSetup> { new() { CharId = "sion" } }, Deck = new List<BattleCardSpec> { new() { Id = "sion_forget" } }, Enemies = new List<string> { "e_seed" }, Hp = 81, MaxHp = 81, Seed = 0 };
            var battle = Battle.Create(data, setup); battle.Command(new BattleRequest("base0-begin", 0, "begin"));
            battle.Command(BattleDataLoader.Request("{\"rid\":\"base0-play\",\"v\":1,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"mode\":\"now\",\"target\":null,\"picks\":[]}}"));
            Check(battle.State.Block == 5 && battle.State.Exhausted == 0 && battle.State.Cards["c1"].Used == 1 && battle.EffectiveCard("c1").Effects[1].Base0 == true, "independent real empty-discard sion_forget keeps current base5/base0 metadata; generic rule conflict remains unresolved");
        }
        using var ordered = JsonDocument.Parse("{\"rid\":\"identity\",\"v\":1,\"kind\":\"play\",\"args\":{\"log\":[1],\"ev\":[],\"2\":2,\"1\":1,\"text\":\"\\ud800\"}}");
        Check(RequestIdentity(ordered.RootElement) == "{\"v\":1,\"kind\":\"play\",\"args\":{\"1\":1,\"2\":2,\"log\":[1],\"ev\":[],\"text\":\"\\ud800\"}}", "independent receipt-evidence encoder retains diagnostic keys/UTF16 and JS integer-index order");
    }

    static void FixtureRejections(JsonElement fixture, JsonElement source, string root)
    {
        void Reject(Dictionary<string, object?> graph, string label)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(graph)); bool rejectedFixture = false;
            try { ValidateFixture(json.RootElement, source, root); } catch (InvalidDataException) { rejectedFixture = true; }
            Check(rejectedFixture, "fixture tamper rejected: " + label);
        }
        foreach (string field in new[] { "schema", "scope", "status", "dataVersion" }) { var bad = Shallow(fixture); bad[field] = "tampered"; Reject(bad, field); }
        foreach (string field in new[] { "path", "sha256" })
        {
            var bad = Shallow(fixture); var sources = Shallow(fixture.GetProperty("sources")); var engine = Shallow(fixture.GetProperty("sources").GetProperty("engine"));
            engine[field] = field == "path" ? "../engine/rules-core.js" : new string('0', 64); sources["engine"] = engine; bad["sources"] = sources; Reject(bad, "source " + field);
        }
        var rows = fixture.GetProperty("cases").EnumerateArray().Select(c => (object?)c).ToList();
        var missing = Shallow(fixture); missing["cases"] = rows.Skip(1).ToList(); Reject(missing, "missing required matrix case");
        var duplicate = Shallow(fixture); duplicate["cases"] = rows.Append(rows[0]).ToList(); Reject(duplicate, "duplicate case");
        foreach (string target in new[] { "rngHash", "eventsHash", "logHash", "responseEventsHash", "state-log", "state-ev", "response-ev", "response-v", "public-leak", "restored-setup" })
        {
            var bad = Shallow(fixture); var changedRows = rows.ToList(); var row = (JsonElement)rows[0]!; var changedRow = Shallow(row);
            var steps = row.GetProperty("steps").EnumerateArray().Select(s => (object?)s).ToList(); var step = (JsonElement)steps[0]!; var changedStep = Shallow(step);
            if (target.StartsWith("state-", StringComparison.Ordinal)) { var state = Shallow(step.GetProperty("state")); state[target[6..]] = System.Array.Empty<object>(); changedStep["state"] = state; }
            else if (target.StartsWith("response-", StringComparison.Ordinal)) { var response = Shallow(step.GetProperty("response")); response[target[9..]] = target == "response-ev" ? System.Array.Empty<object>() : 999; changedStep["response"] = response; }
            else if (target == "public-leak") { var view = Shallow(step.GetProperty("publicView")); view["rng"] = System.Array.Empty<object>(); changedStep["publicView"] = view; }
            else if (target == "restored-setup") { var setup = Shallow(row.GetProperty("setup")); setup["pendingCopies"] = System.Array.Empty<object>(); changedRow["setup"] = setup; }
            else changedStep[target] = new string('0', 64);
            steps[0] = changedStep; changedRow["steps"] = steps; changedRows[0] = changedRow; bad["cases"] = changedRows; Reject(bad, target);
        }
        int witnessRow = rows.FindIndex(r => ((JsonElement)r!).TryGetProperty("witnesses", out var witnesses) && witnesses.GetArrayLength() != 0);
        var originalRow = (JsonElement)rows[witnessRow]!;
        foreach (bool removeAll in new[] { false, true })
        {
            var bad = Shallow(fixture); var changedRows = rows.ToList(); var changedRow = Shallow(originalRow);
            if (removeAll)
            {
                foreach (int i in Enumerable.Range(0, rows.Count))
                { var noWitnesses = Shallow((JsonElement)rows[i]!); noWitnesses.Remove("witnesses"); changedRows[i] = noWitnesses; }
            }
            else
            {
                var witnesses = originalRow.GetProperty("witnesses").EnumerateArray().Select(w => (object?)w).ToList(); var witness = (JsonElement)witnesses[0]!; var changedWitness = Shallow(witness);
                var facts = witness.GetProperty("facts").EnumerateArray().Select(f => (object?)f).ToList(); var fact = Shallow((JsonElement)facts[0]!); fact["value"] = "forged-observable-literal"; facts[0] = fact;
                changedWitness["facts"] = facts; witnesses[0] = changedWitness; changedRow["witnesses"] = witnesses; changedRows[witnessRow] = changedRow;
            }
            bad["cases"] = changedRows; Reject(bad, removeAll ? "all observable witnesses removed" : "forged observable witness literal");
        }
    }

    static string RequestIdentity(JsonElement request)
    {
        object? args = request.TryGetProperty("args", out var value) ? BattleDataLoader.Graph(value) : null;
        return OrderedJson(new Dictionary<string, object?> { ["v"] = BattleDataLoader.Graph(request.GetProperty("v")), ["kind"] = BattleDataLoader.Graph(request.GetProperty("kind")), ["args"] = Truthy(args) ? args : new Dictionary<string, object?>() });
    }
    static string OrderedJson(object? value)
    {
        if (value is Dictionary<string, object?> obj)
        {
            uint? Index(string key) => uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out uint n) && n != uint.MaxValue && n.ToString(CultureInfo.InvariantCulture) == key ? n : null;
            var keys = obj.Keys.Where(k => Index(k).HasValue).OrderBy(k => Index(k)).Concat(obj.Keys.Where(k => !Index(k).HasValue));
            return "{" + string.Join(",", keys.Select(k => CanonicalState.Serialize(k) + ":" + OrderedJson(obj[k]))) + "}";
        }
        if (value is IList list) return "[" + string.Join(",", list.Cast<object?>().Select(OrderedJson)) + "]";
        return CanonicalState.Serialize(value);
    }
    static bool Truthy(object? value) => value != null && value is not false && (value is not long n || n != 0) && (value is not string s || s.Length != 0);
    static Dictionary<string, object?> Catalog(BattleData data) => new(StringComparer.Ordinal)
    {
        ["cards"] = data.Cards.Select(c => c.ToJsonGraph()).ToList(), ["characters"] = data.Characters.Select(c => c.ToJsonGraph()).ToList(),
        ["weapons"] = data.Weapons.Select(w => w.ToJsonGraph()).ToList(), ["parts"] = data.Parts.Select(p => p.ToJsonGraph()).ToList(), ["relics"] = data.Relics.Select(r => r.ToJsonGraph()).ToList()
    };
    static JsonDocument ReadFixture(string root)
    { using var file = File.OpenRead(Path.Combine(root, "engine", "tests", "vectors", "battle_player_action_vectors.json.gz")); using var gzip = new GZipStream(file, CompressionMode.Decompress); return JsonDocument.Parse(gzip); }
    static bool Throws(Action action, string code) { try { action(); return false; } catch (RuleException error) { return error.Code == code; } }
    static void Same(JsonElement expected, object? actual, string label) => SameGraph(BattleDataLoader.Graph(expected), actual, label);
    static void SameGraph(object? expected, object? actual, string label) { string? diff = JsonGraphComparison.Difference(expected, actual, "$", 0); Check(diff == null, label + (diff == null ? "" : ": " + diff)); }
    static void RequireSame(JsonElement expected, object? actual, string label) => Require(JsonGraphComparison.Difference(BattleDataLoader.Graph(expected), actual, "$", 0) == null, label);
    static Dictionary<string, object?> Shallow(JsonElement json) => json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value, StringComparer.Ordinal);
    static Dictionary<string, object?> Object(object? value) => value as Dictionary<string, object?> ?? throw new InvalidDataException("Expected graph object");
    static IList Array(object? value) => value as IList ?? throw new InvalidDataException("Expected graph array");
    static string Text(JsonElement json, string key) => (string?)BattleDataLoader.Graph(json.GetProperty(key)) ?? throw new InvalidDataException("Expected string " + key);
    static string? OptionalText(JsonElement json, string key) => json.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? (string?)BattleDataLoader.Graph(value) : null;
    static string FileHash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    static string HashText(JsonElement json) { string hash = (string)BattleDataLoader.Graph(json)!; Require(hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Malformed action hash"); return hash; }
}
