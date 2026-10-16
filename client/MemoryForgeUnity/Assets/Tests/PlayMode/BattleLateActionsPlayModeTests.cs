// P1-LATE-ACTIONS-MONO-PARITY §10.6/§16.2/§20.9: the existing original-JS late-action corpus (candidate overlay + fixed test-local
// boundaries) replayed by the shared Core inside actual PlayMode/Mono. Every case starts from a fresh Battle.Create on freshly loaded
// Resources definitions plus the fixture's own unchanged overlay; recorded states are comparison data only and never enter the engine.
// Test-only: shipping Resources/export are not changed and the 23 candidates are not activated in Run/Unity content.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MemoryForge.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MemoryForge.Client.Tests
{
    public class BattleLateActionsPlayModeTests
    {
        const string Schema = "mf-battle-late-actions-v1", Status = "JS_ORACLE_CANDIDATE_ONLY";
        const string Scope = "Fresh Battle.create and real begin/play/ult/end commands for apDrain/sealSlot/delayRes/silence/eatName/phase on the unchanged candidate overlay and fixed test-local boundary enemies; live content, Run/Unity activation, balance and server acceptance NOT_APPROVED";
        const string FixturePath = "engine/tests/vectors/battle_late_actions_vectors.json.gz";
        // Exact allowlist in source order: only these fixed repository paths are hashed; a fixture path can never steer the reader.
        static readonly string[,] Sources = { { "data", "data/game_data.json" }, { "engine", "engine/rules-core.js" }, { "memory", "proposals/memory_v2.cjs" },
            { "canonical", "tools/gen_traces.mjs" }, { "proposal", "proposals/late_enemies.cjs" }, { "generator", "tools/gen_battle_late_actions_vectors.mjs" } };
        static readonly string[] Behaviors = { "apDrain", "sealSlot", "delayRes", "silence", "eatName", "phase" };

        readonly List<string> failures = new List<string>();
        int checks, creates, requests, committed, replayed, rejected, points;

        void Expect(bool ok, string label) { checks++; if (!ok && failures.Count < 200) failures.Add(label); }

        [UnityTest, Timeout(900000)]
        public IEnumerator CandidateLateCorpusReplaysThroughMonoCore()
        {
            Assert.That(Application.isPlaying, Is.True, "the corpus must run inside actual PlayMode");
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            var fixture = (JObject)ParseStrict(Gunzip(File.ReadAllBytes(Path.Combine(repo, FixturePath))));
            ValidateFixture(fixture, repo);

            // Live definitions come from the exported Resources content, which must be exported from the same pinned data file.
            var resource = ClientBattleContent.Load();
            byte[] sourceBytes = File.ReadAllBytes(Path.Combine(repo, "data", "game_data.json"));
            Assert.That(resource.SourceSha256, Is.EqualTo(Sha(sourceBytes)).And.EqualTo((string?)fixture["sources"]?["data"]?["sha256"]),
                "Resources battle content, data/game_data.json and the fixture's data pin must be the same source");
            var attributes = Arr(ParseStrict(new UTF8Encoding(false, true).GetString(sourceBytes))["attributes"], "attributes").Select(a => Str(a["id"], "attribute id")).ToList();
            var overlay = Arr(fixture["overlay"]?["enemies"], "overlay enemies"); var boundary = Arr(fixture["boundaryEnemies"], "boundary enemies");
            AssertLiveUnactivated(resource.Battle, overlay, "before the corpus");
            CheckTypedMapping(overlay.Concat(boundary));

            foreach (var row in Arr(fixture["cases"], "cases"))
            {
                ReplayCase((JObject)row, overlay, boundary, attributes);
                yield return null; // let actual frames run between cases
            }

            AssertLiveUnactivated(ClientBattleContent.Load().Battle, overlay, "after the corpus");
            Debug.Log($"BattleLateActionsPlayMode: {checks - failures.Count}/{checks} checks; {creates} actual Battle.Create, {requests} actual requests " +
                $"({committed} committed, {replayed} replayed, {rejected} rejected), {points} compared checkpoints");
            Assert.That(failures, Is.Empty, "late-action Mono parity failures (first " + failures.Count + "):\n" + string.Join("\n", failures.Take(20)));
            Assert.That(new[] { creates, requests, committed, replayed, rejected }, Is.EqualTo(new[] { 81, 473, 445, 2, 26 }),
                "all 81 fresh creations and 473 recorded requests must actually execute in Mono");
        }

        // ───────── one recorded case through the real public Core API ─────────
        void ReplayCase(JObject row, JArray overlay, JArray boundary, List<string> attributes)
        {
            string id = Str(row["id"], "case id"), dataset = Str(row["dataset"], id + " dataset");
            try
            {
                var data = Dataset(dataset, overlay, boundary, attributes);
                var setup = Obj(row["setup"], id + " setup");
                if (dataset == "candidate") Expect(Arr(setup["enemies"], id + " enemies").All(e => !Str(e, "enemy").StartsWith("t_", StringComparison.Ordinal)), id + " candidate case uses no test-local enemy");
                var battle = Battle.Create(data, Setup(setup, id)); creates++;
                if (!ComparePoint(Obj(row["created"], id + " created"), battle, id + " created")) return;
                int index = 0;
                foreach (var token in Arr(row["steps"], id + " steps"))
                {
                    var step = Obj(token, id + " step"); string label = id + " step " + index++;
                    var request = Request(step["request"], label);
                    var before = battle.State; string beforeState = PrintState(before.ToJsonGraph()), beforeView = CanonicalState.Serialize(battle.PublicView());
                    string outcome = Str(step["outcome"], label + " outcome"); requests++;
                    bool ok;
                    if (outcome == "rejected")
                    {
                        string code = Str(step["errorCode"], label + " errorCode"); string? actual = null;
                        try { battle.Command(request); actual = "<committed>"; } catch (RuleException error) { actual = error.Code; }
                        ok = actual == code && PrintState(battle.State.ToJsonGraph()) == beforeState && CanonicalState.Serialize(battle.PublicView()) == beforeView;
                        Expect(ok, label + " expected real RuleException " + code + " with untouched prestate; got " + actual);
                        if (ok) rejected++;
                    }
                    else
                    {
                        var response = battle.Command(request).ToJsonGraph(); var expected = Obj(step["response"], label + " response");
                        var events = (IList)response["ev"]!;
                        Members(expected, label + " response", "v", "phase", "ev");
                        ok = Same(Graph(expected["v"]), response["v"], label + " response version") & Same(Graph(expected["phase"]), response["phase"], label + " response phase")
                            & Same(Graph(expected["ev"]), events, label + " response events") & Hash(events, step["responseEventsHash"], label + " response event hash");
                        if (outcome == "replayed")
                        {
                            ok &= PrintState(battle.State.ToJsonGraph()) == beforeState;
                            Expect(ok, label + " receipt replay executes nothing"); if (ok) replayed++;
                        }
                        else
                        {
                            ok &= outcome == "committed" && battle.State.V == before.V + 1;
                            Expect(ok, label + " commits exactly one version"); if (ok) committed++;
                        }
                    }
                    if (!ComparePoint(step, battle, label) || !ok) { Expect(false, id + " stopped at its first mismatch (" + label + ")"); return; }
                }
            }
            catch (Exception error) { Expect(false, id + " threw outside the recorded contract: " + error.GetType().Name + ": " + error.Message); }
        }

        bool ComparePoint(JObject point, Battle battle, string label)
        {
            points++;
            var state = battle.State; var graph = state.ToJsonGraph(); var view = battle.PublicView();
            var expectedState = Graph(point["state"]); var expectedView = Graph(point["publicView"]);
            bool ok = Same(expectedState, graph, label + " complete state") & Same(Graph(Obj(point["state"], label)["log"]), graph["log"], label + " log")
                & Same(Graph(Obj(point["state"], label)["ev"]), graph["ev"], label + " events") & Same(expectedView, view, label + " public view");
            // Actual hashes against the oracle's recorded hashes; the recorded snapshot must hash to its own recorded value.
            ok &= Hash(graph, point["stateHash"], label + " state hash") & Hash(expectedState, point["stateHash"], label + " recorded state hash")
                & Hash(state.Rng, point["rngHash"], label + " RNG hash") & Hash(graph["ev"], point["eventsHash"], label + " events hash")
                & Hash(graph["log"], point["logHash"], label + " log hash") & Hash(view, point["publicViewHash"], label + " public view hash");
            bool hidden = !view.ContainsKey("rng") && !view.ContainsKey("cards") && !view.ContainsKey("zones") && !view.ContainsKey("draw");
            Expect(hidden, label + " public view hides rng/cards/zones/draw");
            return ok && hidden;
        }

        // ───────── fixture provenance (the strict gzip/UTF-8/unique-key decoder stays CoreChecks' responsibility) ─────────
        void ValidateFixture(JObject fixture, string repo)
        {
            Assert.That(fixture.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "schema", "scope", "status", "dataVersion", "sources", "overlay", "boundaryEnemies", "behaviors", "coverage", "counts", "cases" }));
            Assert.That(new[] { (string?)fixture["schema"], (string?)fixture["scope"], (string?)fixture["status"] }, Is.EqualTo(new[] { Schema, Scope, Status }));
            var sources = Obj(fixture["sources"], "sources");
            Assert.That(sources.Properties().Select(p => p.Name), Is.EqualTo(Enumerable.Range(0, Sources.GetLength(0)).Select(i => Sources[i, 0])), "source allowlist membership/order");
            for (int i = 0; i < Sources.GetLength(0); i++)
            {
                var record = Obj(sources[Sources[i, 0]], "source " + Sources[i, 0]);
                Assert.That(record.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "path", "sha256" }));
                Assert.That((string?)record["path"], Is.EqualTo(Sources[i, 1]), "substituted source path " + Sources[i, 0]);
                Assert.That((string?)record["sha256"], Is.EqualTo(Sha(File.ReadAllBytes(Path.Combine(repo, Sources[i, 1])))), "stale source " + Sources[i, 0]);
            }
            Assert.That(CanonicalState.Serialize(Graph(fixture["counts"])), Is.EqualTo("{\"cases\":81,\"commands\":473,\"committed\":445,\"rejected\":26,\"replayed\":2}"), "fixed denominators");
            Assert.That(CanonicalState.Serialize(Graph(fixture["coverage"])), Is.EqualTo("{\"candidates\":23,\"edges\":34,\"encounters\":23,\"protocols\":1}"), "fixed coverage");
            Assert.That(Arr(fixture["behaviors"], "behaviors").Select(b => (string?)b), Is.EqualTo(Behaviors));
            Assert.That((string?)fixture["overlay"]?["provenance"]?["status"], Is.EqualTo("candidate-only"));
            Assert.That(new[] { Arr(fixture["overlay"]?["enemies"], "overlay").Count, Arr(fixture["boundaryEnemies"], "boundary").Count, Arr(fixture["cases"], "cases").Count },
                Is.EqualTo(new[] { 23, 17, 81 }));
            Assert.That(Arr(fixture["boundaryEnemies"], "boundary").All(e => Str(e["id"], "boundary id").StartsWith("t_", StringComparison.Ordinal)), Is.True, "test-local IDs stay separate");
        }

        // ───────── test-only definitions: fresh Resources data + the fixture's unchanged overlay ─────────
        static BattleData Dataset(string name, JArray overlay, JArray boundary, List<string> attributes)
        {
            if (name != "candidate" && name != "boundary") throw new InvalidDataException("Unknown dataset " + name);
            var data = ClientBattleContent.Load().Battle; // a fresh, untouched live definition set per case
            var live = new HashSet<string>(data.Enemies.Select(e => e.Id), StringComparer.Ordinal);
            foreach (var enemy in name == "boundary" ? overlay.Concat(boundary) : overlay)
            {
                var definition = Enemy(enemy);
                if (!live.Add(definition.Id)) throw new InvalidDataException("Overlay enemy would overwrite existing content: " + definition.Id);
                data.Enemies.Add(definition);
            }
            // Explicit Mono-test bridge: phase descriptors (set by Enemy) and attribute IDs are not part of the exported Resources.
            data.SetAttributeIds(attributes); data.Index(); return data;
        }
        void AssertLiveUnactivated(BattleData live, JArray overlay, string label)
        {
            var candidates = new HashSet<string>(overlay.Select(e => Str(e["id"], "overlay id")), StringComparer.Ordinal);
            Assert.That(live.Enemies.Count, Is.EqualTo(20), "Resources still hold only the live 20 enemies " + label);
            Assert.That(live.Enemies.Any(e => candidates.Contains(e.Id) || e.Id.StartsWith("t_", StringComparison.Ordinal) || e.PhaseDescriptor() != null
                || e.Pattern.Any(m => Behaviors.Contains(m.Type) || m.Then != null && Behaviors.Contains(m.Then.Type))), Is.False, "no candidate behavior is active in Resources " + label);
        }
        // Every mapped member and absence must equal the source; "zone" is the one named typed limit (overlay metadata Battle never reads).
        void CheckTypedMapping(IEnumerable<JToken> sources)
        {
            foreach (var token in sources)
            {
                var expected = (Dictionary<string, object?>)Graph(token)!; expected.Remove("zone");
                var definition = Enemy(token);
                Expect(CanonicalState.Serialize(Typed(definition)) == CanonicalState.Serialize(expected), "typed mapping keeps every member/absence of " + definition.Id);
            }
        }
        static Dictionary<string, object?> Typed(BattleEnemyDef enemy)
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
        static BattleEnemyDef Enemy(JToken token)
        {
            var o = Obj(token, "enemy"); string id = Str(o["id"], "enemy id");
            Members(o, id, "id", "name", "zone", "tier", "hp", "brk", "weak", "passive", "pattern", "phase");
            var enemy = new BattleEnemyDef { Id = id, Name = Str(o["name"], id), Tier = Str(o["tier"], id), Hp = Int(o["hp"], id), Brk = Int(o["brk"], id),
                Weak = Strings(o["weak"], id), Pattern = Arr(o["pattern"], id).Select(m => Move(m, id)).ToList() };
            if (Present(o["passive"]))
            {
                var passive = Obj(o["passive"], id + " passive"); Members(passive, id + " passive", "id", "n");
                enemy.Passive = new EnemyPassiveDef { Id = Str(passive["id"], id), N = OptInt(passive, "n", id) ?? 0 };
            }
            if (Present(o["phase"]))
            {
                var phase = Obj(o["phase"], id + " phase"); Members(phase, id + " phase", "t", "hp", "brk", "weak", "pattern");
                enemy.SetPhaseDescriptor(new EnemyPhaseDef { Type = OptStr(phase, "t", id), Hp = OptInt(phase, "hp", id), Brk = OptInt(phase, "brk", id),
                    Weak = phase["weak"] == null ? null : Strings(phase["weak"], id), Pattern = phase["pattern"] == null ? null : Arr(phase["pattern"], id).Select(m => Move(m, id)).ToList() });
            }
            return enemy;
        }
        static EnemyMoveDef Move(JToken token, string id)
        {
            var o = Obj(token, id + " move"); Members(o, id + " move", "t", "card", "s", "who", "label", "e", "d", "h", "str", "b", "n", "then");
            return new EnemyMoveDef { Type = Str(o["t"], id + " move"), Card = OptStr(o, "card", id), Status = OptStr(o, "s", id), Who = OptStr(o, "who", id),
                Label = OptStr(o, "label", id), Enemy = OptStr(o, "e", id), Damage = OptInt(o, "d", id), Hits = OptInt(o, "h", id), Strength = OptInt(o, "str", id),
                Block = OptInt(o, "b", id), N = OptInt(o, "n", id), Then = o["then"] == null ? null : Move(o["then"]!, id) };
        }

        // Legal public Battle.create inputs of this corpus only; any other member fails instead of being skipped.
        static BattleSetup Setup(JObject o, string id)
        {
            Members(o, id + " setup", "party", "deck", "hp", "maxHp", "seed", "relics", "enemies", "difficulty");
            var setup = new BattleSetup { Hp = Int(o["hp"], id), MaxHp = Int(o["maxHp"], id), Seed = Present(o["seed"]) ? checked((uint)Long(o["seed"], id)) : (uint?)null,
                Relics = Strings(o["relics"], id), Enemies = Strings(o["enemies"], id) };
            if (Present(o["difficulty"]))
            {
                var difficulty = Obj(o["difficulty"], id + " difficulty"); Members(difficulty, id + " difficulty", "hp", "dmg");
                setup.DifficultyHp = OptInt(difficulty, "hp", id) ?? 0; setup.DifficultyDamage = OptInt(difficulty, "dmg", id) ?? 0;
            }
            foreach (var token in Arr(o["party"], id + " party"))
            {
                var actor = Obj(token, id + " actor"); Members(actor, id + " actor", "charId", "gauge");
                setup.Party.Add(new BattleActorSetup { CharId = Str(actor["charId"], id), Gauge = OptInt(actor, "gauge", id) ?? 0 });
            }
            foreach (var token in Arr(o["deck"], id + " deck"))
            {
                var card = Obj(token, id + " card"); Members(card, id + " card", "id", "assigned");
                setup.Deck.Add(new BattleCardSpec { Id = Str(card["id"], id), Assigned = OptStr(card, "assigned", id) });
            }
            return setup;
        }
        // Source RID/version/kind/args are passed unchanged; args become the public ordered BattleArgument tree.
        static BattleRequest Request(JToken? token, string label)
        {
            var o = Obj(token, label + " request"); Members(o, label + " request", "rid", "v", "kind", "args");
            return new BattleRequest(Present(o["rid"]) ? Str(o["rid"], label) : null, Int(o["v"], label), Str(o["kind"], label), o["args"] == null ? null : Argument(o["args"]!));
        }
        static BattleArgument Argument(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object: return BattleArgument.Object(((JObject)token).Properties().Select(p => new KeyValuePair<string, BattleArgument>(p.Name, Argument(p.Value))));
                case JTokenType.Array: return BattleArgument.Array(((JArray)token).Select(Argument));
                case JTokenType.String: return BattleArgument.String((string?)token ?? "");
                case JTokenType.Boolean: return BattleArgument.Boolean((bool)token);
                case JTokenType.Integer: return BattleArgument.Integer((long)token);
                case JTokenType.Null: return BattleArgument.Null();
                default: throw new InvalidDataException("Unsupported request token " + token.Type);
            }
        }

        // ───────── comparison helpers (expected JSON is converted for comparison only) ─────────
        bool Same(object? expected, object? actual, string label)
        {
            string left = expected is Dictionary<string, object?> state && state.ContainsKey("log") ? PrintState(state) : CanonicalState.Serialize(expected);
            string right = actual is Dictionary<string, object?> live && live.ContainsKey("log") ? PrintState(live) : CanonicalState.Serialize(actual);
            if (left == right) { checks++; return true; }
            int at = 0; while (at < left.Length && at < right.Length && left[at] == right[at]) at++;
            int from = Math.Max(0, at - 60);
            Expect(false, label + " differs at " + at + ": expected …" + Slice(left, from) + " actual …" + Slice(right, from));
            return false;
        }
        static string Slice(string text, int from) => from >= text.Length ? "<end>" : text.Substring(from, Math.Min(160, text.Length - from));
        // CanonicalState omits "log"/"ev" members, so a complete state print appends them explicitly.
        static string PrintState(Dictionary<string, object?> state) => CanonicalState.Serialize(state)
            + "|log=" + CanonicalState.Serialize(state.TryGetValue("log", out var log) ? log : "<missing>")
            + "|ev=" + CanonicalState.Serialize(state.TryGetValue("ev", out var ev) ? ev : "<missing>");
        bool Hash(object? value, JToken? recorded, string label)
        {
            bool ok = CanonicalState.Hash(value) == (string?)recorded;
            Expect(ok, label); return ok;
        }
        static object? Graph(JToken? token)
        {
            if (token == null) throw new InvalidDataException("Missing fixture value");
            switch (token.Type)
            {
                case JTokenType.Object:
                    var graph = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var p in ((JObject)token).Properties()) graph.Add(p.Name, Graph(p.Value));
                    return graph;
                case JTokenType.Array: return ((JArray)token).Select(Graph).ToList();
                case JTokenType.Integer: return (long)token;
                case JTokenType.String: return (string?)token;
                case JTokenType.Boolean: return (bool)token;
                case JTokenType.Null: return null;
                default: throw new InvalidDataException("Unsupported fixture token " + token.Type);
            }
        }

        // ───────── JSON helpers ─────────
        static string Gunzip(byte[] compressed)
        {
            using (var input = new MemoryStream(compressed))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gzip.CopyTo(output);
                return new UTF8Encoding(false, true).GetString(output.ToArray());
            }
        }
        static JToken ParseStrict(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal, MaxDepth = 256 })
            {
                var token = JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                Assert.That(reader.Read(), Is.False, "unexpected content after the JSON document");
                return token;
            }
        }
        static string Sha(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(bytes).Select(x => x.ToString("x2", CultureInfo.InvariantCulture)));
        }
        static bool Present(JToken? token) => token != null && token.Type != JTokenType.Null;
        static void Members(JObject o, string label, params string[] allowed)
        {
            foreach (var p in o.Properties()) if (Array.IndexOf(allowed, p.Name) < 0) throw new InvalidDataException(label + " has unsupported member " + p.Name);
        }
        static JObject Obj(JToken? token, string label) => token as JObject ?? throw new InvalidDataException(label + " must be an object");
        static JArray Arr(JToken? token, string label) => token as JArray ?? throw new InvalidDataException(label + " must be an array");
        static string Str(JToken? token, string label) => token != null && token.Type == JTokenType.String ? (string?)token ?? "" : throw new InvalidDataException(label + " must be a string");
        static string? OptStr(JObject o, string key, string label) => o[key] == null ? null : Str(o[key], label + " " + key);
        static long Long(JToken? token, string label) => token != null && token.Type == JTokenType.Integer ? (long)token : throw new InvalidDataException(label + " must be an integer");
        static int Int(JToken? token, string label) => checked((int)Long(token, label));
        static int? OptInt(JObject o, string key, string label) => o[key] == null ? (int?)null : Int(o[key], label + " " + key);
        static List<string> Strings(JToken? token, string label) => Arr(token, label).Select(s => Str(s, label)).ToList();
    }
}
