// A0 / §5.4-6, §6, §16.2: actual Create/begin and pure queries; no expected-state restoration or play execution.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MemoryForge.Core;

static class BattlePlayerQueryChecks
{
    const string Scope = "Fresh Battle.create/begin pure EffectiveCard/AllowedModes/CostOf/NeedsTarget only; play/ult and full Battle/Run replay NOT_IMPLEMENTED";
    static readonly Dictionary<string, string> Sources = new(StringComparer.Ordinal)
    {
        ["data"] = "data/game_data.json", ["engine"] = "engine/rules-core.js", ["memory"] = "proposals/memory_v2.cjs",
        ["canonical"] = "tools/gen_traces.mjs", ["openingGenerator"] = "tools/gen_battle_opening_vectors.mjs", ["generator"] = "tools/gen_battle_player_query_vectors.mjs"
    };
    static int checks, failures, queryRows, commands, receiptProbes;
    static void Check(bool ok, string label) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL BattlePlayerQuery " + label); } }
    static void Require(bool ok, string label) { if (!ok) throw new InvalidDataException(label); }

    public static int Run(string root)
    {
        checks = failures = queryRows = commands = receiptProbes = 0;
        try
        {
            using var fixtureDocument = ReadFixture(root, "battle_player_query_vectors.json.gz");
            using var openingDocument = ReadFixture(root, "battle_opening_vectors.json.gz");
            using var sourceDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
            var fixture = fixtureDocument.RootElement; var source = sourceDocument.RootElement;
            ValidateFixture(fixture, openingDocument.RootElement, source, root);
            Check(true, "source hashes, complete case/query sets and independently projected authored catalog");
            var data = BattleDataLoader.Load(root); var catalog = Catalog(data);
            Same(fixture.GetProperty("catalog"), catalog, "full typed combat catalog including nested ult/effects/parts/passives and optional presence");
            string originalDefinitions = Full(catalog);
            Check(CanonicalState.Hash(catalog) == HashText(fixture.GetProperty("catalogHash")), "typed authored catalog hash");
            RuleAssertions(source, data.Rules);
            var cases = fixture.GetProperty("cases").EnumerateArray().ToDictionary(c => Text(c, "id"), StringComparer.Ordinal);
            foreach (var pair in cases)
            {
                string id = pair.Key; var row = pair.Value;
                var battle = Battle.Create(data, BattleDataLoader.Setup(row.GetProperty("setup")));
                CompareCheckpoint(row.GetProperty("created"), battle, id + " created");
                QueryBatch(row.GetProperty("createdQueries"), battle, id + " created queries");
                CompareCheckpoint(row.GetProperty("afterCreatedQueries"), battle, id + " after created queries");
                var request = BattleDataLoader.Request(row.GetProperty("request"));
                var response = battle.Command(request); commands++;
                Same(row.GetProperty("response"), response.ToJsonGraph(), id + " real begin response");
                CompareCheckpoint(row.GetProperty("begun"), battle, id + " begun");
                QueryBatch(row.GetProperty("queries"), battle, id + " begun queries");
                CompareCheckpoint(row.GetProperty("afterQueries"), battle, id + " after begun queries");
                string beforeReplay = Full(battle.State.ToJsonGraph());
                receiptProbes++; Same(row.GetProperty("response"), battle.Command(request).ToJsonGraph(), id + " begin receipt unchanged by every query");
                Check(Full(battle.State.ToJsonGraph()) == beforeReplay, id + " receipt probe preserves complete live state");
                CompareReceipts(row.GetProperty("created").GetProperty("receipts"), null, null, id + " created receipts");
                CompareReceipts(row.GetProperty("afterCreatedQueries").GetProperty("receipts"), null, null, id + " after created query receipts");
                CompareReceipts(row.GetProperty("begun").GetProperty("receipts"), row.GetProperty("request"), response, id + " begun receipts");
                CompareReceipts(row.GetProperty("afterQueries").GetProperty("receipts"), row.GetProperty("request"), response, id + " after query receipts");
                Check(Full(Catalog(data)) == originalDefinitions && CanonicalState.Hash(Catalog(data)) == HashText(row.GetProperty("definitionsBeforeHash")) && CanonicalState.Hash(Catalog(data)) == HashText(row.GetProperty("definitionsAfterHash")), id + " all original nested definitions remain unchanged");
            }
            Check(commands == 702 && queryRows == 8340 && receiptProbes == 702, "702 real begin commands / 8340 complete oracle query rows / 702 unchanged receipt probes");
            IndependentAssertions(data, cases);
            BoundaryChecks(root, cases);
            FixtureRejections(fixture, openingDocument.RootElement, source, root);
        }
        catch (Exception error) { Check(false, "fixture/execution: " + error); }
        Console.WriteLine($"BattlePlayerQueryChecks: {checks - failures}/{checks} passed; {commands} actual begin commands, {queryRows} oracle query rows, {receiptProbes} begin receipt probes");
        Console.WriteLine("BattlePlayerQueryScope: A0 authored definitions and pure effective-card/mode/cost/target queries on actual Create/begin instances");
        Console.WriteLine("A0QueryExclusion: play/ult and producing command paths are checked separately; base0 generic conformance UNRESOLVED");
        return failures;
    }

    static Dictionary<string, object?> Catalog(BattleData data) => new(StringComparer.Ordinal)
    {
        ["cards"] = data.Cards.Select(c => c.ToJsonGraph()).ToList(), ["characters"] = data.Characters.Select(c => c.ToJsonGraph()).ToList(),
        ["weapons"] = data.Weapons.Select(w => w.ToJsonGraph()).ToList(), ["parts"] = data.Parts.Select(p => p.ToJsonGraph()).ToList(), ["relics"] = data.Relics.Select(r => r.ToJsonGraph()).ToList()
    };
    static Dictionary<string, object?> SourceCatalog(JsonElement source) => new(StringComparer.Ordinal)
    {
        ["cards"] = BattleDataLoader.Graph(source.GetProperty("cards")), ["weapons"] = BattleDataLoader.Graph(source.GetProperty("weapons")),
        ["parts"] = BattleDataLoader.Graph(source.GetProperty("parts")), ["relics"] = BattleDataLoader.Graph(source.GetProperty("relics")),
        ["characters"] = source.GetProperty("characters").EnumerateArray().Select(c => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = Text(c, "id"), ["name"] = Text(c, "name"), ["attr"] = Text(c, "attr"), ["passive"] = BattleDataLoader.Graph(c.GetProperty("passive")),
            ["ult"] = BattleDataLoader.Graph(c.GetProperty("ult")), ["resonance"] = c.GetProperty("resonance").EnumerateArray().Select(r => BattleDataLoader.Graph(r.GetProperty("fx"))).ToList()
        }).ToList()
    };

    static void ValidateFixture(JsonElement fixture, JsonElement opening, JsonElement source, string root)
    {
        Require(Text(fixture, "schema") == "mf-battle-player-queries-v1" && Text(fixture, "scope") == Scope, "Wrong player-query schema/scope");
        string version = Text(source.GetProperty("meta"), "version");
        Require(Text(fixture, "dataVersion") == version && Text(opening, "dataVersion") == version && Text(opening, "schema") == "mf-battle-opening-v1", "Wrong query/opening version");
        ValidateSources(fixture.GetProperty("sources"), root, false); ValidateSources(opening.GetProperty("sources"), root, true);
        var rawCatalog = SourceCatalog(source);
        RequireSame(fixture.GetProperty("catalog"), rawCatalog, "Player-query catalog differs from complete source projection");
        string catalogHash = HashText(fixture.GetProperty("catalogHash")); Require(CanonicalState.Hash(rawCatalog) == catalogHash, "Stale player-query catalog hash");
        RequireSameJson("{\"literal\":\"1.3\",\"numerator\":13,\"denominator\":10}", fixture.GetProperty("upgradeRatio"), "Wrong exact upgrade ratio");
        var conflict = fixture.GetProperty("unresolved").GetProperty("base0");
        Require(Text(conflict, "cardId") == "sion_forget" && Text(conflict, "status") == "ORIGINAL_SOURCE_CONFLICT" && Text(conflict, "retained") == "Current JS/live-card base5 +5*lastCount (added cap10); generic base0 conformance UNRESOLVED", "Base0 conflict was hidden/resolved without authority");
        RequireSameJson("[\"docs/source_v0.8.1/06_chapter.md:42\",\"docs/source_v0.8.1/09_chapter.md:833\",\"content/characters.mjs:310\",\"engine/rules-core.js:1064-1095\"]", conflict.GetProperty("anchors"), "Base0 source anchors changed");
        var cards = source.GetProperty("cards").EnumerateArray().ToList(); var characters = source.GetProperty("characters").EnumerateArray().ToList();
        var weapons = source.GetProperty("weapons").EnumerateArray().ToList(); var parts = source.GetProperty("parts").EnumerateArray().ToList(); var relics = source.GetProperty("relics").EnumerateArray().ToList();
        var permanent = parts.Where(p => Text(p, "slot") != "temp").ToList();
        var evolvedFrom = cards.Where(c => c.TryGetProperty("evo", out _)).SelectMany(c => c.GetProperty("evo").EnumerateArray().Select(e => (Id: e.GetString()!, From: Text(c, "id")))).ToDictionary(p => p.Id, p => p.From, StringComparer.Ordinal);
        var reachability = fixture.GetProperty("reachability");
        RequireSame(reachability.GetProperty("setupPartIds"), permanent.Select(p => Text(p, "id")).ToList(), "Permanent part reachability changed");
        RequireSameJson("[\"amp\"]", reachability.GetProperty("beginTempPartIds"), "Unproduced temporary part claimed");
        RequireSameJson("[\"overheat temporary attachment\",\"positive silenceUntil\",\"positive freeNext\",\"nonzero costMod\",\"free/echoCopy tokens\"]", reachability.GetProperty("pendingA1"), "A1 query branches no longer explicitly pending");
        RequireSameJson("[\"explicit plus.eff override\",\"costIf\"]", reachability.GetProperty("notAuthored"), "Non-authored query branches misrepresented");
        var openingIds = new HashSet<string>(("seed-zero seed-uint-max seed-default-omitted seed-default-null zero-fallback-values full-starting-gauges " +
            "harin-opening-preserved start-block-per-actor mixed-derived-vitality-gear aila-overflow-strongest-heal aila-partial-overflow aila-before-circuit-cost " +
            "brazier-before-kaya brazier-kaya-uint-max draw-max-strongest-only draw-stronger-weapon-later tuning-only-one-eligible tuning-only-curses empty-deck " +
            "seed-moss-single seed-no-eligible seed-mixed-eligibility seed-creation-order-duplicates card-fields-and-lineage circuit-fatal circuit-exact-fatal " +
            "circuit-survives circuit-envelope-used circuit-envelope-unused ap-cap-high ap-cap-low ap-crown-hourglass ap-carried-bonus echo-and-rule-interactions three-opening-actors").Split(' '), StringComparer.Ordinal);
        foreach (var character in characters) for (int r = 0; r <= 6; r++) Require(openingIds.Add($"character-{Text(character, "id")}-res{r}"), "Duplicate expected opening character");
        foreach (var weapon in weapons) for (int r = 1; r <= 5; r++) Require(openingIds.Add($"weapon-{Text(weapon, "id")}-r{r}"), "Duplicate expected opening weapon");
        foreach (var relic in relics) Require(openingIds.Add("relic-" + Text(relic, "id")), "Duplicate expected opening relic");
        foreach (var enemy in source.GetProperty("enemies").EnumerateArray()) Require(openingIds.Add("enemy-" + Text(enemy, "id")), "Duplicate expected opening enemy");
        var openings = opening.GetProperty("cases").EnumerateArray().ToDictionary(c => Text(c, "id"), StringComparer.Ordinal);
        Require(openingIds.Count == 267 && openingIds.SetEquals(openings.Keys), "Prerequisite opening membership incomplete");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var card in cards) foreach (string variant in new[] { "base", "plus" }) expected.Add($"card-{Text(card, "id")}-{variant}", "card");
        foreach (string id in openingIds) expected.Add("opening-" + id, "opening");
        foreach (var character in characters) for (int r = 0; r <= 6; r++) expected.Add($"resonance-{Text(character, "id")}-r{r}", "resonance");
        foreach (var part in permanent) expected.Add("part-" + Text(part, "id"), "part");
        expected.Add("temp-amp-begin", "temp-part");
        foreach (string edge in new[] { "empty-clock-lite", "empty-clock-no-reserve", "character-weapon-common-owner", "unknown-part-ignored" }) expected.Add("edge-" + edge, "edge");
        var rows = fixture.GetProperty("cases").EnumerateArray().ToList(); var ids = rows.Select(c => Text(c, "id")).ToList();
        Require(ids.Count == expected.Count && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count && expected.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(ids), "Player-query case set missing/duplicate");
        int records = 0, modePairs = 0;
        foreach (var row in rows)
        {
            string id = Text(row, "id"), group = Text(row, "group"); Require(group == expected[id], "Query case group changed");
            var setup = row.GetProperty("setup"); var request = row.GetProperty("request");
            Require(Text(request, "rid") == id + "-begin" && Text(request, "kind") == "begin" && request.GetProperty("v").GetInt32() == 0 && !request.GetProperty("args").EnumerateObject().Any(), "Query case no longer executes a fresh begin");
            if (group == "opening")
            { string openingId = Text(row, "openingId"); Require(id == "opening-" + openingId && openings.ContainsKey(openingId), "Wrong opening linkage"); RequireSame(openings[openingId].GetProperty("setup"), BattleDataLoader.Graph(setup), "Query opening setup changed"); }
            else
            {
                Require(setup.GetProperty("seed").GetUInt32() == 0 && setup.GetProperty("hp").GetInt32() == 81 * setup.GetProperty("party").GetArrayLength() && setup.GetProperty("hp").GetInt32() == setup.GetProperty("maxHp").GetInt32(), "Query source setup HP/seed changed");
                RequireSameJson("[\"e_seed\"]", setup.GetProperty("enemies"), "Query source setup enemies changed");
                Require(setup.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "party", "deck", "hp", "maxHp", "seed", "enemies", "relics" }), "Unexpected non-opening setup input");
                if (id is "edge-empty-clock-lite" or "edge-empty-clock-no-reserve") RequireSameJson("[\"empty_clock\"]", setup.GetProperty("relics"), "Missing cost relic modifier");
                else Require(setup.GetProperty("relics").GetArrayLength() == 0, "Unexpected modifier in a source query case");
            }
            if (group == "card")
            {
                string cardId = Text(row, "cardId"), variant = Text(row, "variant");
                Require(id == $"card-{cardId}-{variant}" && variant is "base" or "plus", "Wrong card variant linkage");
                var card = cards.Single(c => Text(c, "id") == cardId); var spec = setup.GetProperty("deck");
                Require(spec.GetArrayLength() == 1 && Text(spec[0], "id") == cardId && spec[0].GetProperty("plus").GetBoolean() == (variant == "plus"), "Card case does not instantiate its authored variant");
                var weapon = weapons.Where(w => Text(w, "card") == cardId).ToList();
                var actor = weapon.Count == 0 ? characters.FirstOrDefault(c => Text(c, "id") == Text(card, "owner")) : characters.FirstOrDefault(c => weapon[0].TryGetProperty("signature", out var signature) && Text(c, "id") == signature.GetString());
                if (actor.ValueKind == JsonValueKind.Undefined) actor = weapon.Count == 0 ? characters[0] : characters.First(c => Text(c, "weapon") == Text(weapon[0], "type"));
                var expectedActor = new Dictionary<string, object?> { ["charId"] = Text(actor, "id") };
                var expectedSpec = new Dictionary<string, object?> { ["id"] = cardId, ["plus"] = variant == "plus" };
                if (evolvedFrom.TryGetValue(cardId, out var from)) expectedSpec["from"] = from;
                if (weapon.Count != 0) { expectedActor["weaponId"] = Text(weapon[0], "id"); expectedActor["refine"] = 1; expectedSpec["wielder"] = Text(actor, "id"); }
                RequireSame(setup.GetProperty("party"), new List<object?> { expectedActor }, "Card case changed authored owner/wielder setup");
                RequireSame(spec, new List<object?> { expectedSpec }, "Card case changed source lineage/variant");
                if (variant == "base") { var modes = BaseModes(card); modePairs += modes.Count; RequireSame(row.GetProperty("createdQueries")[0].GetProperty("allowedModes"), modes, "Base mode membership differs from source"); }
            }
            if (group == "resonance")
            {
                string characterId = Text(row, "characterId"); int resonance = row.GetProperty("resonance").GetInt32();
                Require(id == $"resonance-{characterId}-r{resonance}" && resonance is >= 0 and <= 6 && setup.GetProperty("party").GetArrayLength() == 1 && Text(setup.GetProperty("party")[0], "charId") == characterId && setup.GetProperty("party")[0].GetProperty("res").GetInt32() == resonance, "Wrong resonance setup");
                var expectedDeck = cards.Where(c => Text(c, "owner") == characterId).SelectMany(c => new[] { (Text(c, "id"), false), (Text(c, "id"), true) }).ToList();
                var deck = setup.GetProperty("deck").EnumerateArray().Select(c => (Text(c, "id"), c.GetProperty("plus").GetBoolean())).ToList();
                Require(deck.SequenceEqual(expectedDeck), "Resonance case omitted/reordered owned base/plus cards");
                RequireSameJson("[{\"charId\":\"" + characterId + "\",\"res\":" + resonance + "}]", setup.GetProperty("party"), "Unexpected resonance actor modifiers");
            }
            if (group == "part")
            {
                string partId = Text(row, "partId"); var part = permanent.Single(p => Text(p, "id") == partId); string slot = Text(part, "slot") == "circuit" ? "c" : "e";
                Require(id == "part-" + partId && setup.GetProperty("deck").GetArrayLength() == 4 && setup.GetProperty("deck").EnumerateArray().All(c => Text(c.GetProperty("mods"), slot) == partId), "Permanent part case does not attach the correct source part");
                Require(setup.GetProperty("deck").EnumerateArray().Select(c => Text(c, "id")).SequenceEqual(new[] { "sera_round", "sera_timed", "sera_last", "nia_tune" }), "Part source card coverage changed");
                RequireSameJson("[{\"charId\":\"sera\"},{\"charId\":\"nia\"}]", setup.GetProperty("party"), "Part actor modifiers changed");
                foreach (var card in setup.GetProperty("deck").EnumerateArray())
                { Require(!card.GetProperty("plus").GetBoolean(), "Part case changed upgrade variant"); RequireSame(card.GetProperty("mods"), new Dictionary<string, object?> { [slot] = partId }, "Part case is not a single authored attachment"); }
            }
            if (group == "temp-part") { Require(Text(row, "partId") == "amp", "Wrong produced temporary part"); RequireSameJson("[{\"charId\":\"nia\",\"res\":4},{\"charId\":\"sera\"}]", setup.GetProperty("party"), "Temp part must be produced by actual NiaR4 begin"); }
            foreach (string point in new[] { "created", "afterCreatedQueries", "begun", "afterQueries" }) ValidateCheckpoint(row.GetProperty(point));
            RequireSame(row.GetProperty("created"), BattleDataLoader.Graph(row.GetProperty("afterCreatedQueries")), "Created queries mutated fixture state/receipts");
            RequireSame(row.GetProperty("begun"), BattleDataLoader.Graph(row.GetProperty("afterQueries")), "Begun queries mutated fixture state/receipts");
            Require(Text(row.GetProperty("created").GetProperty("state"), "phase") == "CREATED" && row.GetProperty("created").GetProperty("state").GetProperty("v").GetInt32() == 0 && row.GetProperty("begun").GetProperty("state").GetProperty("v").GetInt32() == 1, "Query fixture is not actual Create/begin");
            ValidateQueryRows(row.GetProperty("createdQueries"), row.GetProperty("created").GetProperty("state")); ValidateQueryRows(row.GetProperty("queries"), row.GetProperty("begun").GetProperty("state"));
            records += row.GetProperty("createdQueries").GetArrayLength() + row.GetProperty("queries").GetArrayLength();
            Require(HashText(row.GetProperty("definitionsBeforeHash")) == catalogHash && HashText(row.GetProperty("definitionsAfterHash")) == catalogHash, "Definition mutation/stale hash in query fixture");
        }
        Require(cards.Count == 173 && modePairs == 378 && expected.Count == 702, "Current complete A0 inventory changed");
        var coverage = fixture.GetProperty("coverage");
        foreach (var (key, count) in new[] { ("cards", cards.Count), ("baseModePairs", modePairs), ("characterResonances", characters.Count * 7), ("weaponRefinements", weapons.Count * 5), ("relics", relics.Count), ("partDefinitions", parts.Count), ("permanentPartCases", permanent.Count), ("beginTempPartCases", 1) })
            Require(coverage.GetProperty(key).GetInt32() == count, "Query coverage mismatch " + key);
        var counts = fixture.GetProperty("counts");
        Require(counts.GetProperty("cases").GetInt32() == expected.Count && counts.GetProperty("beginCommands").GetInt32() == expected.Count && counts.GetProperty("queryRecords").GetInt32() == records && counts.GetProperty("queryCalls").GetInt32() == records * 6, "Query counts mismatch");
    }
    static void ValidateSources(JsonElement sourceRecords, string root, bool opening)
    {
        var expected = opening ? Sources.Where(p => p.Key != "generator" && p.Key != "openingGenerator").ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) : Sources;
        if (opening) expected.Add("generator", Sources["openingGenerator"]);
        var records = sourceRecords.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        Require(records.Count == expected.Count && expected.Keys.All(records.ContainsKey), "Query source set mismatch");
        foreach (var source in expected)
        { var record = records[source.Key]; Require(record.EnumerateObject().Count() == 2 && Text(record, "path") == source.Value && HashText(record.GetProperty("sha256")) == FileHash(Path.Combine(root, source.Value)), "Stale/substituted query source: " + source.Key); }
    }
    static List<string> BaseModes(JsonElement card)
    {
        if (card.GetProperty("kw").EnumerateArray().Any(k => k.GetString() == "contract")) return new() { "now" };
        return Text(card, "res") switch { "y" => new() { "now", "next", "after" }, "o" => new() { "next", "after" }, "after" => new() { "after" }, _ => new() { "now" } };
    }
    static void ValidateQueryRows(JsonElement rows, JsonElement state)
    {
        var cards = state.GetProperty("cards").EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        var ids = rows.EnumerateArray().Select(q => Text(q, "uid")).ToList();
        Require(ids.Count == cards.Count && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count && cards.Keys.SequenceEqual(ids), "Query instance membership/order incomplete/duplicate");
        foreach (var row in rows.EnumerateArray())
        {
            Require(Text(row, "cardId") == Text(cards[Text(row, "uid")], "id"), "Query instance/card linkage changed");
            Require(row.GetProperty("needsTarget").ValueKind == JsonValueKind.Null || row.GetProperty("needsTarget").GetString() is "one" or "fixed", "Unknown target query result");
            var modes = row.GetProperty("allowedModes").EnumerateArray().Select(v => v.GetString()).ToList();
            Require(modes.Distinct(StringComparer.Ordinal).Count() == modes.Count && modes.All(m => m is "now" or "next" or "after"), "Unknown/duplicate allowed modes");
            Require(row.GetProperty("costs").EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "now", "next", "after" }) && row.GetProperty("costs").EnumerateObject().All(p => p.Value.TryGetInt32(out int n) && n >= 0), "Malformed payable costs");
        }
    }

    static void QueryBatch(JsonElement rows, Battle battle, string label)
    {
        var instances = battle.State.Cards;
        foreach (var row in rows.EnumerateArray())
        {
            queryRows++; string uid = Text(row, "uid"); string before = Full(battle.State.ToJsonGraph());
            T Pure<T>(Func<T> query, string name) { T result = query(); Check(Full(battle.State.ToJsonGraph()) == before, label + " " + uid + " " + name + " preserves full state/RNG/log/ev/version"); return result; }
            var definition = Pure(() => battle.EffectiveCard(uid), "EffectiveCard"); var modes = Pure(() => battle.AllowedModes(uid), "AllowedModes");
            var costs = new Dictionary<string, object?> { ["now"] = Pure(() => battle.CostOf(uid, "now"), "CostOf now"), ["next"] = Pure(() => battle.CostOf(uid, "next"), "CostOf next"), ["after"] = Pure(() => battle.CostOf(uid, "after"), "CostOf after") };
            var actual = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["uid"] = uid, ["cardId"] = instances[uid].Id, ["effective"] = definition.ToJsonGraph(), ["allowedModes"] = modes,
                ["needsTarget"] = Pure(() => battle.NeedsTarget(uid), "NeedsTarget"), ["costs"] = costs
            };
            Same(row, actual, label + " " + uid + " full query projection");
            Check(Pure(() => battle.CostOf(uid), "default CostOf") == (int)costs["now"]!, label + " " + uid + " default mode is now");
            MutateDefinition(definition); modes.Clear();
            Same(row.GetProperty("effective"), battle.EffectiveCard(uid).ToJsonGraph(), label + " " + uid + " effective definition deeply detached");
            Same(row.GetProperty("allowedModes"), battle.AllowedModes(uid), label + " " + uid + " allowed modes detached");
            Check(Full(battle.State.ToJsonGraph()) == before, label + " " + uid + " mutation cannot affect live instance/actor/state");
        }
    }
    static void MutateDefinition(BattleCardDef definition)
    {
        definition.Id = "caller-mutation"; definition.Cost = 999; definition.Uses = 999; definition.Keywords.Add("caller-mutation"); definition.Evolutions?.Clear();
        MutateEffects(definition.Effects); if (definition.Plus?.Effects != null) MutateEffects(definition.Plus.Effects);
        if (definition.CostIf != null) definition.CostIf.Condition.Condition = "caller-mutation";
    }
    static void MutateEffects(List<EffectDef> effects)
    {
        foreach (var effect in effects)
        {
            effect.V = 999; effect.Base0 = false;
            if (effect.Scale != null) effect.Scale.K = 999;
            if (effect.HitsScale != null) effect.HitsScale.Per = "caller-mutation";
            if (effect.Condition != null) effect.Condition.Condition = "caller-mutation";
            if (effect.Multipliers != null) { foreach (var multiplier in effect.Multipliers) { multiplier.Multiplier = 999; multiplier.Condition.Condition = "caller-mutation"; } effect.Multipliers.Clear(); }
            if (effect.Then != null) MutateEffects(effect.Then); if (effect.Else != null) MutateEffects(effect.Else);
        }
        effects.Clear();
    }
    static void ValidateCheckpoint(JsonElement point)
    {
        var state = point.GetProperty("state"); var view = point.GetProperty("publicView");
        foreach (string key in new[] { "rng", "cards", "zones", "draw" }) Require(!view.TryGetProperty(key, out _), "Query public view exposes " + key);
        foreach (var (value, hash) in new[] { (state, "stateHash"), (state.GetProperty("rng"), "rngHash"), (state.GetProperty("ev"), "eventsHash"), (state.GetProperty("log"), "logHash"), (view, "publicViewHash"), (point.GetProperty("receipts"), "receiptsHash") })
            Require(CanonicalState.Hash(BattleDataLoader.Graph(value)) == HashText(point.GetProperty(hash)), "Stale player-query checkpoint " + hash);
    }
    static void CompareCheckpoint(JsonElement point, Battle battle, string label)
    {
        var state = battle.State; var graph = state.ToJsonGraph(); var view = battle.PublicView();
        Same(point.GetProperty("state"), graph, label + " complete state including diagnostics"); Same(point.GetProperty("publicView"), view, label + " full public view");
        foreach (var (value, hash) in new (object?, string)[] { (graph, "stateHash"), (state.Rng, "rngHash"), (state.Events.Select(e => e.ToJsonGraph()).ToList(), "eventsHash"), (state.Log.Select(e => e.ToJsonGraph()).ToList(), "logHash"), (view, "publicViewHash") })
            Check(CanonicalState.Hash(value) == HashText(point.GetProperty(hash)), label + " " + hash);
    }
    static void CompareReceipts(JsonElement expected, JsonElement? request, BattleResponse? response, string label)
    {
        var records = expected.EnumerateObject().ToList();
        if (request == null) { Check(records.Count == 0, label + " remain empty"); return; }
        var input = request.Value; string rid = Text(input, "rid");
        Check(records.Count == 1 && records[0].Name == rid, label + " exact begin receipt membership");
        var receipt = expected.GetProperty(rid);
        string digest = JsonSerializer.Serialize(new { v = input.GetProperty("v").GetInt32(), kind = Text(input, "kind"), args = BattleDataLoader.Graph(input.GetProperty("args")) });
        Check(Text(receipt, "digest") == digest, label + " exact begin receipt identity"); Same(receipt.GetProperty("resp"), response!.ToJsonGraph(), label + " complete receipt response/events");
    }
    static void RuleAssertions(JsonElement source, BattleRules rules)
    {
        var raw = source.GetProperty("rules");
        foreach (var (name, actual) in new[] { ("gaugePerCard", rules.GaugePerCard), ("gaugeTurnCap", rules.GaugeTurnCap), ("breakApBonus", rules.BreakApBonus), ("breakApBonusMaxPerTurn", rules.BreakApBonusMaxPerTurn), ("vulnerableBps", rules.VulnerableBps), ("brokenDamageBps", rules.BrokenDamageBps) })
            Check(raw.GetProperty(name).GetInt32() == actual, "new typed rule " + name);
        Check(raw.GetProperty("upgradeMult").GetDecimal() == 1.3m && rules.UpgradeNumerator == 13 && rules.UpgradeDenominator == 10, "upgrade multiplier parsed exactly as13/10 without float state");
    }

    static void IndependentAssertions(BattleData data, Dictionary<string, JsonElement> cases)
    {
        Battle Start(string id) { var row = cases[id]; var battle = Battle.Create(data, BattleDataLoader.Setup(row.GetProperty("setup"))); battle.Command(BattleDataLoader.Request(row.GetProperty("request"))); return battle; }
        string Uid(Battle battle, string card, bool plus = false) => battle.State.Cards.Values.Single(c => c.Id == card && c.Plus == plus).Uid;
        BattleCardDef Card(Battle battle, string card, bool plus = false) => battle.EffectiveCard(Uid(battle, card, plus));
        var battle = Start("card-sera_round-plus"); var definition = Card(battle, "sera_round", true);
        Check(definition.Effects[0].V == 11 && definition.Effects[0].Rv == 16, "independent ceiling13/10 upgrade8→11/12→16");
        battle = Start("resonance-sera-r4"); definition = Card(battle, "sera_timed", true);
        Check(definition.Effects[0].V == 21 && definition.Effects[0].Rv == 29 && definition.Effects[0].Ra == 39 && definition.Effects[0].Brk == 5, "independent automatic upgrade precedes SeraR4 break+2");
        battle = Start("resonance-harin-r1"); definition = Card(battle, "harin_counter", true);
        Check(definition.Effects[0].V == 7 && definition.Effects[0].Rv == 10 && definition.Effects[1].N == 6, "independent upgraded counterceil3.9=4 then resonance+2=6");
        battle = Start("resonance-kaya-r1"); Check(Card(battle, "kaya_clue", true).Effects[0].N == 4, "independent status upgrade+1 precedes Kaya+1");
        battle = Start("resonance-rien-r1"); Check(Card(battle, "rien_note", true).Effects[0].V == 9, "independent Rienceil5.2=6 then+3=9");
        battle = Start("resonance-aila-r1"); definition = Card(battle, "aila_bell", true);
        Check(definition.Effects[0].V == 9 && definition.Effects[0].Rv == 12, "independent Aila upgraded heal7/10 then+2=9/12");
        battle = Start("resonance-nia-r1"); Check(Card(battle, "nia_tune").Uses == 3 && Card(battle, "nia_tune", true).Uses == 3, "independent Nia uses2+1 for both upgrade variants");
        battle = Start("resonance-ravien-r1"); Check(Card(battle, "ravien_advance", true).Uses == 2 && battle.AllowedModes(Uid(battle, "ravien_advance", true)).SequenceEqual(new[] { "now" }), "independent contract uses1+1 remains immediate only");
        battle = Start("resonance-sion-r1"); Check(Card(battle, "sion_erase", true).Effects[1].N == 2, "independent draw resonance adds1 without automatic draw upgrade");
        battle = Start("resonance-lumi-r1"); Check(Card(battle, "lumi_dash", true).Effects[0].V == 1 && Card(battle, "lumi_dash", true).Effects[1].V == 10, "independent selfdmg3-2=1 while damage7 upgrades to10");
        battle = Start("card-sera_double-plus"); definition = Card(battle, "sera_double", true);
        Check(definition.Effects[0].V == 6 && definition.Effects[0].Rv == 8 && definition.Effects[1].Then![0].V == 6 && definition.Effects[1].Then![0].Rv == 8 && definition.Effects[1].Else!.Count == 0 && definition.Effects[1].ToJsonGraph().ContainsKey("else"), "independent recursive upgrade preserves explicit empty else");
        var zeroCard = data.Cards.Single(c => c.Effects.Any(e => e.Hits == 0)); battle = Start("card-" + zeroCard.Id + "-plus");
        Check(Card(battle, zeroCard.Id, true).Effects.Any(e => e.Hits == 0 && e.ToJsonGraph().ContainsKey("hits")), "independent authored hits0 stays present and zero");
        battle = Start("card-sion_forget-base"); definition = Card(battle, "sion_forget");
        Check(definition.Effects[1].V == 5 && definition.Effects[1].Base0 == true && definition.Effects[1].Scale!.K == 5 && definition.Effects[1].Scale!.Max == 10, "independent current base0:true metadata retains authored base5 without claiming generic conformance");
        var evolved = data.Cards.Single(c => c.Id == "yuri_cocoon_guard");
        Check(evolved.Pool == false && evolved.ToJsonGraph().ContainsKey("pool") && evolved.ToJsonGraph()["pool"] is false && !evolved.ToJsonGraph().ContainsKey("growth"), "independent false pool distinct from absent growth");
        Check(!data.Cards.Single(c => c.Id == "sera_round").ToJsonGraph().ContainsKey("uses") && !data.Cards.Single(c => c.Id == "sera_round").ToJsonGraph().ContainsKey("plus"), "independent absent optional uses/plus remain absent");

        battle = Start("edge-empty-clock-no-reserve");
        Check(battle.AllowedModes(Uid(battle, "sera_round")).SequenceEqual(new[] { "now" }) && battle.AllowedModes(Uid(battle, "sera_timed")).Count == 0 && battle.AllowedModes(Uid(battle, "sera_last")).Count == 0, "independent noReserve retains now for y but removes all o/after-only modes");
        Check(battle.CostOf(Uid(battle, "sera_timed"), "now") == 2 && battle.CostOf(Uid(battle, "sera_timed"), "next") == 1 && battle.CostOf(Uid(battle, "sera_timed"), "after") == 1, "independent pure cost query does not enforce allowed-mode precheck");
        battle = Start("edge-empty-clock-lite");
        Check(battle.CostOf(Uid(battle, "sera_timed")) == 1 && battle.CostOf(Uid(battle, "sera_timed"), "next") == 0 && battle.CostOf(Uid(battle, "sera_timed"), "after") == 0 && battle.CostOf(Uid(battle, "sera_round"), "after") == 0, "independent lite and unused clock discount then clamp at0");
        battle = Start("edge-unknown-part-ignored"); Check(battle.CostOf("c1") == 1 && battle.AllowedModes("c1").SequenceEqual(new[] { "now", "next", "after" }), "independent source ignores unknown setup part IDs");
        battle = Start("edge-character-weapon-common-owner");
        Check(Card(battle, "aila_bell", true).Effects[0].V == 9 && Full(Card(battle, "wc_choir_bell").ToJsonGraph()) == Full(data.Cards.Single(c => c.Id == "wc_choir_bell").ToJsonGraph()) && Full(Card(battle, "com_patch").ToJsonGraph()) == Full(data.Cards.Single(c => c.Id == "com_patch").ToJsonGraph()), "independent weapon wielder/common assigned metadata do not apply Aila's owned-card resonance");
        battle = Start("temp-amp-begin"); Check(battle.State.TempParts == 1 && battle.State.Cards.Values.Sum(c => c.Temp.Count(p => p == "amp")) == 1, "independent actual Nia begin produces one temporary amp");
        battle = Start("card-kaya_deduce-base"); Check(battle.NeedsTarget("c1") == "fixed", "independent fixed target");
        battle = Start("card-sera_round-base"); Check(battle.NeedsTarget("c1") == "one", "independent implicit damage target");
        battle = Start("card-harin_bulwark-base"); Check(battle.NeedsTarget("c1") == null && Card(battle, "harin_bulwark").Effects[0].Keep == 10, "independent targetless bulwark definition keep10; no power execution claim");
        Check(data.Characters.Count == 10 && data.Weapons.Select(w => w.Passive.Id).Distinct(StringComparer.Ordinal).Count() == 17 && data.Parts.Count == 16 && data.Parts.Single(p => p.Id == "overheat").Fx.Exhaust == true, "independent ten ultimate definitions/seventeen passive IDs/all16parts retained");
    }

    static void BoundaryChecks(string root, Dictionary<string, JsonElement> cases)
    {
        var data = BattleDataLoader.Load(root); var row = cases["card-sera_round-plus"];
        var battle = Battle.Create(data, BattleDataLoader.Setup(row.GetProperty("setup")));
        foreach (bool begun in new[] { false, true })
        {
            if (begun) battle.Command(BattleDataLoader.Request(row.GetProperty("request")));
            string before = Full(battle.State.ToJsonGraph()), definitions = Full(Catalog(data));
            foreach (var query in new Action[] { () => battle.EffectiveCard("missing"), () => battle.AllowedModes("missing"), () => battle.CostOf("missing"), () => battle.NeedsTarget("missing") })
                Check(Throws(query, "UNKNOWN_CARD_INSTANCE") && Full(battle.State.ToJsonGraph()) == before && Full(Catalog(data)) == definitions, "unknown UID query explicitly rejects without state/definition changes, begun=" + begun);
        }
        // Negative local-definition faults, never inputs to a positive oracle case.
        var card = data.Cards.Single(c => c.Id == "sera_round"); int? original = card.Effects[0].V; card.Effects[0].V = int.MaxValue;
        string originalState = Full(battle.State.ToJsonGraph()), faultyDefinitions = Full(Catalog(data));
        foreach (var query in new Action[] { () => battle.EffectiveCard("c1"), () => battle.AllowedModes("c1"), () => battle.CostOf("c1"), () => battle.NeedsTarget("c1") })
            Check(Throws(query, "BATTLE_NUMBER_OUT_OF_RANGE") && Full(battle.State.ToJsonGraph()) == originalState && Full(Catalog(data)) == faultyDefinitions, "checked effective upgrade overflow is pure and keeps the original faulty definition");
        card.Effects[0].V = original; Check(battle.EffectiveCard("c1").Effects[0].V == 11, "corrected definition query retries without poisoned state/cache");
        var beginRequest = BattleDataLoader.Request(row.GetProperty("request"));
        Same(row.GetProperty("response"), battle.Command(beginRequest).ToJsonGraph(), "query errors preserve committed begin receipt");
        data = BattleDataLoader.Load(root); row = cases["part-c_lite"]; battle = Battle.Create(data, BattleDataLoader.Setup(row.GetProperty("setup"))); battle.Command(BattleDataLoader.Request(row.GetProperty("request")));
        var part = data.Parts.Single(p => p.Id == "c_lite"); int? cost = part.Fx.Cost; part.Fx.Cost = int.MaxValue; originalState = Full(battle.State.ToJsonGraph()); faultyDefinitions = Full(Catalog(data));
        Check(Throws(() => battle.CostOf("c1"), "BATTLE_NUMBER_OUT_OF_RANGE") && Full(battle.State.ToJsonGraph()) == originalState && Full(Catalog(data)) == faultyDefinitions, "checked cost overflow preserves all state/definitions");
        part.Fx.Cost = cost; Check(battle.CostOf("c1") == 0, "corrected cost query succeeds without a consuming side effect");
        foreach (string kind in new[] { "play", "ult" }) Check(Throws(() => battle.Command(new BattleRequest("a0-invalid-player", 1, kind)), kind == "play" ? "NOT_IN_HAND" : "UNKNOWN_ACTOR") && Full(battle.State.ToJsonGraph()) == originalState, "missing " + kind + " argument leaves pure-query state unchanged");

        string temporary = Path.Combine(Path.GetTempPath(), "mf-player-query-loader-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(temporary, "data"));
            foreach (var (label, change) in new (string, Action<JsonNode>)[]
            {
                ("card field", n => n["cards"]![0]!["unported"] = 1),
                ("effect tag", n => n["cards"]![0]!["eff"]![0]!["op"] = "unported"),
                ("effect field", n => n["cards"]![0]!["eff"]![0]!["unported"] = 1),
                ("effect scale counter", n => n["cards"]![0]!["eff"]![0]!["scale"] = new JsonObject { ["per"] = "unported", ["k"] = 1 }),
                ("conditional tag", n => n["cards"]![0]!["costIf"] = new JsonObject { ["cond"] = new JsonObject { ["c"] = "unported" }, ["delta"] = -1 }),
                ("multiplier condition", n => n["cards"]![0]!["eff"]![0]!["mult"] = new JsonArray(new JsonObject { ["if"] = new JsonObject { ["c"] = "unported" }, ["x"] = 2 })),
                ("ultimate field", n => n["characters"]![0]!["ult"]!["unported"] = 1),
                ("part fx field", n => n["parts"]![0]!["fx"]!["unported"] = 1),
                ("weapon passive field", n => n["weapons"]![0]!["passive"]!["unported"] = 1),
                ("missing required effect value", n => n["cards"]![0]!["eff"]![0]!.AsObject().Remove("v")),
                ("boolean type", n => n["cards"]![0]!["pool"] = "false"),
                ("numeric type", n => n["cards"]![0]!["eff"]![0]!["hits"] = false)
            })
            {
                var content = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")))!; change(content);
                File.WriteAllText(Path.Combine(temporary, "data", "game_data.json"), content.ToJsonString());
                bool rejected = false;
                try { _ = BattleDataLoader.Load(temporary); }
                catch (Exception error) when (error is InvalidDataException or InvalidOperationException or FormatException) { rejected = true; }
                Check(rejected, "typed loader rejects malformed " + label + " from a temp data file");
            }
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    static void FixtureRejections(JsonElement fixture, JsonElement opening, JsonElement source, string root)
    {
        void Reject(Dictionary<string, object?> graph, string label)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(graph)); bool rejected = false;
            try { ValidateFixture(json.RootElement, opening, source, root); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "fixture tamper rejected: " + label);
        }
        foreach (string field in new[] { "schema", "scope", "dataVersion", "catalogHash" }) { var bad = Shallow(fixture); bad[field] = "tampered"; Reject(bad, field); }
        foreach (string field in new[] { "path", "sha256" })
        {
            var bad = Shallow(fixture); var sources = Shallow(fixture.GetProperty("sources")); var engine = Shallow(fixture.GetProperty("sources").GetProperty("engine"));
            engine[field] = field == "path" ? "../engine/rules-core.js" : new string('0', 64); sources["engine"] = engine; bad["sources"] = sources; Reject(bad, "source " + field);
        }
        var rows = fixture.GetProperty("cases").EnumerateArray().Select(c => (object?)c).ToList();
        var missing = Shallow(fixture); missing["cases"] = rows.Skip(1).ToList(); Reject(missing, "missing case");
        var duplicate = Shallow(fixture); duplicate["cases"] = rows.Append(rows[0]).ToList(); Reject(duplicate, "duplicate case");
        foreach (string target in new[] { "rngHash", "eventsHash", "logHash", "receiptsHash", "state-log", "state-ev", "public-leak", "query-member", "definition-hash" })
        {
            var bad = Shallow(fixture); var changedRows = rows.ToList(); var row = (JsonElement)rows[0]!; var changedRow = Shallow(row);
            var point = row.GetProperty("begun"); var changedPoint = Shallow(point);
            if (target.StartsWith("state-", StringComparison.Ordinal)) { var state = Shallow(point.GetProperty("state")); state[target[6..]] = Array.Empty<object>(); changedPoint["state"] = state; }
            else if (target == "public-leak") { var view = Shallow(point.GetProperty("publicView")); view["rng"] = Array.Empty<object>(); changedPoint["publicView"] = view; }
            else if (target == "query-member") changedRow["queries"] = Array.Empty<object>();
            else if (target == "definition-hash") changedRow["definitionsAfterHash"] = new string('0', 64);
            else changedPoint[target] = new string('0', 64);
            changedRow["begun"] = changedPoint; changedRows[0] = changedRow; bad["cases"] = changedRows; Reject(bad, target);
        }
        var omittedFalse = Shallow(fixture); var catalog = Shallow(fixture.GetProperty("catalog")); var cards = fixture.GetProperty("catalog").GetProperty("cards").EnumerateArray().Select(c => (object?)c).ToList();
        int index = cards.FindIndex(c => ((JsonElement)c!).TryGetProperty("pool", out var pool) && pool.ValueKind == JsonValueKind.False);
        var cardWithoutFalse = Shallow((JsonElement)cards[index]!); cardWithoutFalse.Remove("pool"); cards[index] = cardWithoutFalse; catalog["cards"] = cards; omittedFalse["catalog"] = catalog; Reject(omittedFalse, "catalog omitted authored false");
    }

    static JsonDocument ReadFixture(string root, string name)
    {
        using var file = File.OpenRead(Path.Combine(root, "engine", "tests", "vectors", name)); using var gzip = new GZipStream(file, CompressionMode.Decompress); return JsonDocument.Parse(gzip);
    }
    static bool Throws(Action action, string code) { try { action(); return false; } catch (RuleException error) { return error.Code == code; } }
    static void Same(JsonElement expected, object? actual, string label) { string? diff = JsonGraphComparison.Difference(BattleDataLoader.Graph(expected), actual, "$", 0); Check(diff == null, label + (diff == null ? "" : ": " + diff)); }
    static void RequireSame(JsonElement expected, object? actual, string label) => Require(JsonGraphComparison.Difference(BattleDataLoader.Graph(expected), actual, "$", 0) == null, label);
    static void RequireSameJson(string expected, JsonElement actual, string label) { using var json = JsonDocument.Parse(expected); RequireSame(json.RootElement, BattleDataLoader.Graph(actual), label); }
    static Dictionary<string, object?> Shallow(JsonElement json) => json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value, StringComparer.Ordinal);
    static string Text(JsonElement json, string key) => json.GetProperty(key).GetString() ?? throw new InvalidDataException("Expected string " + key);
    static string Full(object? graph) => JsonSerializer.Serialize(graph);
    static string FileHash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    static string HashText(JsonElement json) { string hash = json.GetString()!; Require(hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Malformed query hash"); return hash; }
}
