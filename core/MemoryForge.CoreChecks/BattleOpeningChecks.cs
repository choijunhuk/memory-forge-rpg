// Real C# creation/command execution against the JS oracle; expected states are never inputs to Battle.
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

static class BattleOpeningChecks
{
    static readonly Dictionary<string, string> Sources = new(StringComparer.Ordinal)
    {
        ["data"] = "data/game_data.json", ["engine"] = "engine/rules-core.js", ["memory"] = "proposals/memory_v2.cjs",
        ["canonical"] = "tools/gen_traces.mjs", ["generator"] = "tools/gen_battle_opening_vectors.mjs"
    };
    static readonly string[] Edges = ("seed-zero seed-uint-max seed-default-omitted seed-default-null zero-fallback-values full-starting-gauges " +
        "harin-opening-preserved start-block-per-actor mixed-derived-vitality-gear aila-overflow-strongest-heal aila-partial-overflow aila-before-circuit-cost " +
        "brazier-before-kaya brazier-kaya-uint-max draw-max-strongest-only draw-stronger-weapon-later tuning-only-one-eligible tuning-only-curses empty-deck " +
        "seed-moss-single seed-no-eligible seed-mixed-eligibility seed-creation-order-duplicates card-fields-and-lineage circuit-fatal circuit-exact-fatal " +
        "circuit-survives circuit-envelope-used circuit-envelope-unused ap-cap-high ap-cap-low ap-crown-hourglass ap-carried-bonus echo-and-rule-interactions three-opening-actors").Split(' ');
    static int checks, failures, commands;
    static void Check(bool ok, string message) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL BattleOpening " + message); } }
    static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }

    public static int Run(string root)
    {
        checks = failures = commands = 0;
        try
        {
            var data = BattleDataLoader.Load(root);
            using var file = File.OpenRead(Path.Combine(root, "engine", "tests", "vectors", "battle_opening_vectors.json.gz"));
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            var fixture = document.RootElement;
            ValidateFixture(fixture, root, data); Check(true, "source provenance and complete unique case set");
            var cases = fixture.GetProperty("cases").EnumerateArray().ToDictionary(c => c.GetProperty("id").GetString()!, StringComparer.Ordinal);
            var actual = new Dictionary<string, BattleState>(StringComparer.Ordinal);
            foreach (var pair in cases)
            {
                string id = pair.Key; var vector = pair.Value;
                try
                {
                    var battle = Battle.Create(data, BattleDataLoader.Setup(vector.GetProperty("setup")));
                    var created = battle.State.ToJsonGraph();
                    Check(created.Count == 59, id + " created root has all 59 fields");
                    Same(vector.GetProperty("createdState"), created, id + " created full state");
                    Check(CanonicalState.Hash(created) == HashText(vector.GetProperty("createdStateHash")), id + " created hash");
                    var response = battle.Command(BattleDataLoader.Request(vector.GetProperty("request"))); commands++;
                    Same(vector.GetProperty("response"), response.ToJsonGraph(), id + " response/event order");
                    var final = battle.State;
                    Same(vector.GetProperty("finalState"), final.ToJsonGraph(), id + " final full state including log/ev");
                    Check(CanonicalState.Hash(final.ToJsonGraph()) == HashText(vector.GetProperty("finalStateHash")), id + " final hash");
                    var view = battle.PublicView();
                    Same(vector.GetProperty("publicView"), view, id + " public view");
                    Check(!view.ContainsKey("rng") && !view.ContainsKey("cards") && !view.ContainsKey("zones") && !view.ContainsKey("draw"), id + " public secrets excluded");
                    actual.Add(id, final);
                }
                catch (Exception error) { Check(false, id + " execution: " + error); }
            }
            Check(commands == 267, "267 real committed begin commands");
            IndependentAssertions(actual);
            FixtureRejections(root, data, fixture);
            BoundaryChecks(root, cases);
        }
        catch (Exception error) { Check(false, "fixture/adapter: " + error); }
        Console.WriteLine($"BattleOpeningChecks: {checks - failures}/{checks} passed; {commands} actual C# begin commands (267 expected)");
        Console.WriteLine("BattleOpeningScope: fresh Battle.Create + begin; integer JSON args with JS receipt ordering; int32 state outputs except widened actor levels; fractional/oversized numbers explicitly unsupported");
        Console.WriteLine("OpeningVectorScope: begin only; end/later-turn and player commands are covered separately; full Run/restoration remains unported");
        return failures;
    }

    static void ValidateFixture(JsonElement fixture, string root, BattleData data)
    {
        Require(fixture.GetProperty("schema").GetString() == "mf-battle-opening-v1", "Wrong opening fixture schema");
        Require(fixture.GetProperty("scope").GetString() == "Battle.create and atomic begin only; full Battle/Run replay NOT_IMPLEMENTED", "Wrong opening fixture scope");
        using var content = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
        Require(fixture.GetProperty("dataVersion").GetString() == content.RootElement.GetProperty("meta").GetProperty("version").GetString(), "Wrong opening data version");
        var sourceRecords = fixture.GetProperty("sources").EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        Require(sourceRecords.Count == Sources.Count && Sources.Keys.All(sourceRecords.ContainsKey), "Opening source set mismatch");
        foreach (var source in Sources)
        {
            var record = sourceRecords[source.Key];
            Require(record.GetProperty("path").GetString() == source.Value, "Opening source path mismatch: " + source.Key);
            Require(HashText(record.GetProperty("sha256")) == FileHash(Path.Combine(root, source.Value)), "Stale opening source: " + source.Key);
        }
        var expected = new HashSet<string>(Edges, StringComparer.Ordinal);
        foreach (var character in data.Characters) for (int r = 0; r <= 6; r++) Require(expected.Add($"character-{character.Id}-res{r}"), "Duplicate expected character case");
        foreach (var weapon in data.Weapons) for (int r = 1; r <= 5; r++) Require(expected.Add($"weapon-{weapon.Id}-r{r}"), "Duplicate expected weapon case");
        foreach (var relic in data.Relics) Require(expected.Add("relic-" + relic.Id), "Duplicate expected relic case");
        foreach (var enemy in data.Enemies) Require(expected.Add("enemy-" + enemy.Id), "Duplicate expected enemy case");
        var ids = fixture.GetProperty("cases").EnumerateArray().Select(c => c.GetProperty("id").GetString()!).ToList();
        Require(expected.Count == 267 && ids.Count == expected.Count && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count && expected.SetEquals(ids), "Opening case set incomplete/duplicate");
    }

    static void IndependentAssertions(Dictionary<string, BattleState> actual)
    {
        var harin = actual["harin-opening-preserved"];
        Check(harin.Block == 26 && harin.Counter == 2, "independent Harin R4 + bronze + ledger R5 = block26/counter2");
        var moss = actual["seed-moss-single"];
        Check(moss.Cards["c1"].Growth == 1 && moss.Block == 3 && moss.GrowthCount == 0 && !moss.Cards["c1"].Grown, "independent eternal_seed+moss = growth1/block3 without capped/once budget");
        Check(actual["aila-overflow-strongest-heal"].Block == 17, "independent strongest party heal: floor(10*1.2)*80% + bronze = 17");
        Check(actual["aila-partial-overflow"].Hp == 81 && actual["aila-partial-overflow"].Block == 8, "independent partial heal overflow floor((12-1)*.8)=8");
        Check(actual["aila-before-circuit-cost"].Hp == 9, "independent opening heal precedes circuit HP cost");
        var maxDraw = actual["draw-max-strongest-only"];
        Check(maxDraw.Zones.Hand.Count == 9 && maxDraw.HandMax == 9 && maxDraw.Zones.Discard.Count == 0, "independent authored max opening draw9/strongest weapon once");
        Check(actual["tuning-only-one-eligible"].TempParts == 1 && actual["tuning-only-curses"].TempParts == 0, "independent tuning excludes curses and duplicate amp");
        Check(actual["seed-no-eligible"].Block == 0 && actual["seed-no-eligible"].Events.All(e => e.E != "grow"), "saturated/evolved seed candidates never grow");
        Check(actual["card-fields-and-lineage"].Cards.Values.All(c => !c.ToJsonGraph().ContainsKey("key")), "Battle.create ignores Run-only deck keys");
        Check(actual["circuit-fatal"].Phase == "DEFEAT" && actual["circuit-fatal"].Events.Count(e => e.E == "end") == 1 && actual["circuit-fatal"].Zones.Hand.Count == 0, "fatal circuit ends once before draw");
        Check(actual["circuit-envelope-unused"].Hp == 1 && actual["circuit-envelope-unused"].Phase == "PLAYER" && (bool)actual["circuit-envelope-unused"].RunFlags["envelopeUsed"]!, "unused envelope saves circuit exactly once");
        Check(actual["circuit-envelope-used"].Phase == "DEFEAT", "used envelope does not save circuit");
        Check(actual["ap-cap-high"].Ap == 9 && actual["ap-cap-low"].Ap == 0, "AP clamps at both authored bounds");
    }

    static void FixtureRejections(string root, BattleData data, JsonElement fixture)
    {
        var graph = (Dictionary<string, object?>)BattleDataLoader.Graph(fixture)!;
        var sources = (Dictionary<string, object?>)graph["sources"]!;
        var engine = (Dictionary<string, object?>)sources["engine"]!;
        object? original = engine["sha256"]; engine["sha256"] = new string('0', 64);
        Check(RejectsFixture(graph, root, data), "stale engine source rejected"); engine["sha256"] = original;
        engine["path"] = "../engine/rules-core.js";
        Check(RejectsFixture(graph, root, data), "source path substitution rejected"); engine["path"] = Sources["engine"];
        var vectors = (List<object?>)graph["cases"]!;
        object? last = vectors[^1]; vectors.RemoveAt(vectors.Count - 1);
        Check(RejectsFixture(graph, root, data), "missing edge case rejected"); vectors.Add(last);
        object? first = vectors[0]; vectors[0] = vectors[1];
        Check(RejectsFixture(graph, root, data), "duplicate/missing case IDs rejected"); vectors[0] = first;
        graph["schema"] = "mf-battle-opening-v0";
        Check(RejectsFixture(graph, root, data), "wrong opening schema rejected");
    }
    static bool RejectsFixture(object graph, string root, BattleData data)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(graph));
        try { ValidateFixture(json.RootElement, root, data); return false; }
        catch (InvalidDataException) { return true; }
    }

    static void BoundaryChecks(string root, Dictionary<string, JsonElement> cases)
    {
        var data = BattleDataLoader.Load(root);
        BattleSetup Setup(string id = "harin-opening-preserved") => BattleDataLoader.Setup(cases[id].GetProperty("setup"));
        Battle Fresh(string id = "harin-opening-preserved") => Battle.Create(data, Setup(id));
        var bad = Setup(); bad.Party.Clear(); Check(Throws(() => Battle.Create(data, bad), "PARTY_SIZE"), "empty party rejected");
        bad = Setup(); bad.Party.AddRange(new[] { bad.Party[0], bad.Party[0] }); Check(Throws(() => Battle.Create(data, bad), "PARTY_SIZE"), "party over three rejected");
        bad = Setup(); bad.Party[0].CharId = "missing"; Check(Throws(() => Battle.Create(data, bad), "UNKNOWN_CHARACTER"), "unknown character rejected");
        bad = Setup(); bad.Deck[0].Id = "missing"; Check(Throws(() => Battle.Create(data, bad), "UNKNOWN_CARD"), "unknown card rejected");
        bad = Setup(); bad.Enemies[0] = "missing"; Check(Throws(() => Battle.Create(data, bad), "UNKNOWN_ENEMY"), "unknown enemy rejected");
        bad = Setup(); bad.Hp = -1; Check(Throws(() => Battle.Create(data, bad), "STATE_RANGE"), "negative HP rejected");
        bad = Setup(); bad.Hp = bad.MaxHp + 1; Check(Throws(() => Battle.Create(data, bad), "STATE_RANGE"), "HP over maximum rejected");
        bad = Setup(); bad.Party[0].Gauge = 101; Check(Throws(() => Battle.Create(data, bad), "GAUGE_RANGE"), "gauge over maximum rejected");
        foreach (int growth in new[] { -1, 4 })
        { bad = Setup(); bad.Deck[0].Growth = growth; Check(Throws(() => Battle.Create(data, bad), "GROWTH_INVARIANT"), "invalid card growth " + growth); }

        var battle = Fresh(); string before = Full(battle.State.ToJsonGraph());
        foreach (string kind in new[] { "play", "ult" })
        {
            Check(Throws(() => battle.Command(new BattleRequest("failed", 0, kind)), "WRONG_PHASE"), kind + " before begin rejects phase first");
            Check(Full(battle.State.ToJsonGraph()) == before, "rejected " + kind + " preserves all state");
        }
        Check(Throws(() => battle.Command(new BattleRequest("failed-end", 0, "end")), "WRONG_PHASE"), "end before begin rejected");
        Check(Full(battle.State.ToJsonGraph()) == before, "end before begin preserves complete state");
        Check(Throws(() => battle.Command(new BattleRequest("failed", 0, "unknown")), "UNKNOWN_COMMAND"), "unknown command rejected");
        Check(Throws(() => battle.Command(new BattleRequest("failed", 1, "play")), "STALE_VERSION"), "stale version precedes player-command validation");
        var req = new BattleRequest("failed", 0, "begin");
        var response = battle.Command(req); string expectedResponse = Full(response.ToJsonGraph());
        Check(response.V == 1, "rejected rid reusable with corrected request");
        before = Full(battle.State.ToJsonGraph());
        response.Phase = "tampered"; response.Events[0].N = 999; response.Events.Clear();
        Check(Full(battle.Command(req).ToJsonGraph()) == expectedResponse && Full(battle.State.ToJsonGraph()) == before, "receipt and live events detached from returned response");
        var retry = battle.Command(req); retry.Events.Clear();
        Check(Full(battle.Command(req).ToJsonGraph()) == expectedResponse, "every retry returns a fresh copied receipt");
        Check(Throws(() => battle.Command(new BattleRequest("failed", 999, "play")), "IDEMPOTENCY_CONFLICT"), "receipt conflict precedes stale and player-command checks");
        Check(Throws(() => battle.Command(new BattleRequest("new", 0, "begin")), "STALE_VERSION"), "new rid with old version rejected");
        Check(Throws(() => battle.Command(new BattleRequest("new", 1, "begin")), "WRONG_PHASE"), "second begin rejected without a receipt");
        foreach (string kind in new[] { "play", "ult" }) Check(Throws(() => battle.Command(new BattleRequest("new", 1, kind)), kind == "play" ? "NOT_IN_HAND" : "UNKNOWN_ACTOR"), "missing " + kind + " argument rejects without consuming rid");
        Check(Full(battle.State.ToJsonGraph()) == before, "all rejection priorities preserve RNG/v/log/ev");
        var detached = battle.State; detached.Phase = "CREATED"; detached.Rng[0] = 0; detached.Actors[0].Passive.Params.Clear(); detached.Cards["c1"].Temp.Add("tampered"); detached.Events.Clear();
        Check(Full(battle.State.ToJsonGraph()) == before && Throws(() => battle.Command(new BattleRequest(null, 1, "begin")), "WRONG_PHASE"), "snapshot mutation cannot restore or corrupt the live battle");
        var view = battle.PublicView(); ((IList)view["hand"]!).Clear(); ((IList)view["enemies"]!).Clear();
        Check(Full(battle.State.ToJsonGraph()) == before, "public view mutation does not corrupt state");
        var setup = Setup(); var nested = new Dictionary<string, object?> { ["items"] = new List<object?> { true } }; setup.RunFlags["nested"] = nested;
        battle = Battle.Create(data, setup); before = Full(battle.State.ToJsonGraph());
        ((List<object?>)nested["items"]!).Clear(); setup.Relics.Clear(); setup.Deck.Clear();
        Check(Full(battle.State.ToJsonGraph()) == before, "creation copies nested setup flags and lists");

        setup = Setup(); setup.RunFlags["self"] = setup.RunFlags;
        Check(Throws(() => Battle.Create(data, setup), "UNSUPPORTED_STATE_VALUE"), "cyclic setup flag rejected before copying");
        setup = Setup(); var deep = setup.RunFlags;
        for (int i = 0; i < 256; i++) { var child = new Dictionary<string, object?>(); deep["next"] = child; deep = child; }
        Check(Throws(() => Battle.Create(data, setup), "UNSUPPORTED_STATE_VALUE"), "excessively deep setup flags rejected");
        setup = Setup("circuit-envelope-unused"); setup.RunFlags["envelopeUsed"] = (uint)0;
        battle = Battle.Create(data, setup); battle.Command(new BattleRequest("uint-zero", 0, "begin"));
        Check(battle.State.Hp == 1 && battle.State.Phase == "PLAYER", "uint zero flag remains JS-falsy for envelope survival");
        Check(Throws(() => BattleArgument.Array(new BattleArgument[] { null! }), "INVALID_REQUEST_VALUE"), "null argument array member rejected at construction");
        Check(Throws(() => BattleArgument.Object(new[] { new KeyValuePair<string, BattleArgument>("x", null!) }), "INVALID_REQUEST_VALUE"), "null argument object member rejected at construction");
        Check(Throws(() => BattleArgument.Object(new[] { new KeyValuePair<string, BattleArgument>(null!, BattleArgument.Null()) }), "INVALID_REQUEST_VALUE"), "null argument property name rejected at construction");

        setup = Setup(); setup.Party[0].CharId = "sera"; setup.Party[0].Res = 3; setup.Party[0].SkillLv = int.MaxValue;
        battle = Battle.Create(data, setup); battle.Command(new BattleRequest("max-skill", 0, "begin"));
        Check(battle.State.Actors[0].SkillLv == 2147483649L && battle.State.Phase == "PLAYER", "authored resonance adds to max int skill without wrap");
        setup = Setup(); setup.Party[0].CharId = "sera"; setup.Party[0].Res = 5; setup.Party[0].UltLv = int.MaxValue;
        battle = Battle.Create(data, setup); battle.Command(new BattleRequest("max-ult", 0, "begin"));
        Check(battle.State.Actors[0].UltLv == 2147483649L && battle.State.Phase == "PLAYER", "authored resonance adds to max int ultimate without wrap");
        setup = Setup(); setup.Enemies.Clear(); setup.Enemies.Add("b_bell"); setup.DifficultyHp = int.MinValue;
        battle = Battle.Create(data, setup); battle.Command(new BattleRequest("negative-difficulty", 0, "begin"));
        Check(battle.State.Enemies[0].Hp == 1 && battle.State.Phase == "PLAYER", "enemy HP rounds and clamps before numeric narrowing");
        setup = Setup(); setup.Party[0].Hp = int.MaxValue; setup.Party[0].GearBps = 100000;
        battle = Battle.Create(data, setup); before = Full(battle.State.ToJsonGraph());
        Check(Throws(() => battle.Command(new BattleRequest("large-block", 0, "begin")), "BATTLE_NUMBER_OUT_OF_RANGE") && Full(battle.State.ToJsonGraph()) == before,
            "out-of-int32 opening result explicitly rejected with complete rollback");
        setup = Setup(); setup.Enemies.Clear(); setup.Enemies.Add("b_bell"); setup.DifficultyHp = int.MaxValue;
        Check(Throws(() => Battle.Create(data, setup), "BATTLE_NUMBER_OUT_OF_RANGE"), "out-of-int32 enemy HP explicitly rejected during creation");

        void ReceiptPair(string firstArgs, string secondArgs, bool same, string label)
        {
            var test = Fresh();
            BattleRequest Request(string args) => BattleDataLoader.Request("{\"rid\":\"receipt\",\"v\":0,\"kind\":\"begin\",\"args\":" + args + "}");
            var first = test.Command(Request(firstArgs)); string committed = Full(test.State.ToJsonGraph());
            bool matches = same ? Full(test.Command(Request(secondArgs)).ToJsonGraph()) == Full(first.ToJsonGraph()) : Throws(() => test.Command(Request(secondArgs)), "IDEMPOTENCY_CONFLICT");
            Check(matches && Full(test.State.ToJsonGraph()) == committed, label);
        }
        ReceiptPair("{\"a\":1,\"b\":2}", "{\"b\":2,\"a\":1}", false, "receipt preserves non-index object property order");
        ReceiptPair("{\"log\":1,\"ev\":[]}", "{\"log\":2,\"ev\":[]}", false, "receipt retains log keys");
        ReceiptPair("{\"nested\":{\"ev\":[1]}}", "{\"nested\":{\"ev\":[2]}}", false, "receipt retains nested ev keys");
        ReceiptPair("{\"2\":2,\"1\":1,\"z\":0}", "{\"1\":1,\"2\":2,\"z\":0}", true, "receipt matches JS integer-index enumeration");
        ReceiptPair("false", "{}", true, "false args normalize through JS args||{}");
        ReceiptPair("null", "0", true, "null/zero args normalize through JS args||{}");
        ReceiptPair("\"\"", "{}", true, "empty string args normalize through JS args||{}");
        ReceiptPair("[]", "{}", false, "empty array is truthy in receipt identity");
        ReceiptPair("{\"text\":\"\\u0041\\n\\ud800\",\"n\":1e0}", "{\"text\":\"A\\n\\ud800\",\"n\":1.0}", true, "receipt normalizes JSON escapes, lone surrogate and integral exponent syntax");
        ReceiptPair("{\"\\ud800\":1}", "{\"\\uD800\":1}", true, "receipt preserves lone surrogate property names and normalizes escape syntax");
        ReceiptPair("{\"\\ud800\":1}", "{\"\\ufffd\":1}", false, "receipt never replaces lone surrogate property names");
        using (var keyJson = JsonDocument.Parse("{\"\\ud800\":7}"))
        {
            var keys = (Dictionary<string, object?>)BattleDataLoader.Graph(keyJson.RootElement)!;
            Check(keys.ContainsKey("\ud800") && (long)keys["\ud800"]! == 7, "snapshot flag reader preserves lone surrogate property names");
        }
        var loneRid = BattleDataLoader.Request("{\"rid\":\"\\ud800\",\"v\":0,\"kind\":\"begin\"}");
        battle = Fresh(); var loneResponse = battle.Command(loneRid);
        Check(loneRid.Rid == "\ud800" && Full(battle.Command(loneRid).ToJsonGraph()) == Full(loneResponse.ToJsonGraph()), "request rid retains exact lone surrogate identity");
        Check(Throws(() => BattleDataLoader.Request("{\"v\":0,\"kind\":\"begin\",\"args\":{\"n\":1.3}}"), "UNSUPPORTED_REQUEST_NUMBER"), "fractional request args explicitly rejected");
        Check(Throws(() => BattleDataLoader.Request("{\"v\":0,\"kind\":\"begin\",\"args\":{\"a\":1,\"a\":2}}"), "DUPLICATE_REQUEST_KEY"), "duplicate argument keys rejected at adapter boundary");
        Check(Throws(() => BattleArgument.Integer(CanonicalState.MaxSafeInteger + 1), "UNSUPPORTED_REQUEST_NUMBER"), "unsafe request integer rejected");

        // Fail after shuffle, bronze and random status events. Both rule and unexpected
        // failures must discard the candidate, including its advanced RNG and receipt.
        setup = Setup("three-opening-actors");
        var harin = data.Characters.Single(c => c.Id == "harin");
        var opening = harin.Resonance[3].List!.Single(f => f.Type == "battleStart").Effects!;
        opening.Add(new EffectDef { Op = "unported" });
        battle = Battle.Create(data, setup); before = Full(battle.State.ToJsonGraph());
        Check(Throws(() => battle.Command(new BattleRequest("unsupported-effect", 0, "begin")), "UNSUPPORTED_EFFECT"), "unknown opening effect explicitly rejected after RNG/events");
        Check(Full(battle.State.ToJsonGraph()) == before, "unsupported effect rolls back complete state and RNG"); opening.RemoveAt(opening.Count - 1);

        setup = Setup("three-opening-actors"); setup.Party[0].WeaponId = "w_ledger_lock"; setup.Party[0].Refine = 5;
        battle = Battle.Create(data, setup); before = Full(battle.State.ToJsonGraph());
        var values = data.Weapons.Single(w => w.Id == "w_ledger_lock").Passive.Values; var saved = values.ToList(); values.Clear();
        bool unexpected = false;
        try { battle.Command(new BattleRequest("unexpected", 0, "begin")); }
        catch (ArgumentOutOfRangeException) { unexpected = true; }
        Check(unexpected && Full(battle.State.ToJsonGraph()) == before, "unexpected error after shuffled/random events atomically preserves state/RNG/v/log/ev");
        values.AddRange(saved);
        response = battle.Command(new BattleRequest("unexpected", 0, "begin"));
        var reference = Battle.Create(data, setup); var referenceResponse = reference.Command(new BattleRequest("reference", 0, "begin"));
        Check(response.V == 1 && Full(battle.State.ToJsonGraph()) == Full(reference.State.ToJsonGraph()) && Full(response.ToJsonGraph()) == Full(referenceResponse.ToJsonGraph()), "unexpected failure leaves no receipt/RNG leak; corrected same-rid retry equals fresh execution");

        battle = Fresh(); before = Full(battle.State.ToJsonGraph()); int apMax = data.Rules.ApMax; data.Rules.ApMax = -1;
        Check(Throws(() => battle.Command(new BattleRequest("invariant", 0, "begin")), "STATE_RANGE") && Full(battle.State.ToJsonGraph()) == before, "post-execution invariant rejection rolls back all state");
        data.Rules.ApMax = apMax;
        Check(battle.Command(new BattleRequest("invariant", 0, "begin")).V == 1, "invariant failure creates no receipt; same-rid retry succeeds");
        battle = Fresh(); battle.Command(new BattleRequest("opening", 0, "begin"));
        var passed = battle.Command(new BattleRequest("pass", 1, "end"));
        Check(passed.V == 2 && battle.State.Turn == 2 && battle.State.Phase == "PLAYER", "real end proceeds to the second player turn");
        Check(battle.State.Block == 0 && battle.State.Counter == 0, "opening-only ordinary block and counter clear on second turn");

    }

    static bool Throws(Action action, string code)
    {
        try { action(); return false; }
        catch (RuleException error) { return error.Code == code; }
    }
    static void Same(JsonElement expected, object? actual, string label)
    {
        string? difference = JsonGraphComparison.Difference(BattleDataLoader.Graph(expected), actual, "$", 0);
        Check(difference == null, label + (difference == null ? "" : ": " + difference));
    }
    static string Full(object? graph) => JsonSerializer.Serialize(graph);
    static string FileHash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    static string HashText(JsonElement json)
    {
        string hash = json.GetString()!;
        Require(hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Malformed opening hash"); return hash;
    }
}
