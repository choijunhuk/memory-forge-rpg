// R0 selected authored runtime subset: actual Run.Create + Available/NodeById in Unity Play mode.
// Expected literals come from the executed JS reference (reports/run_create_20261015/unity_reference_actual_js.json).
// This is not full-content import, game UI or Run command acceptance; nothing is injected into Run state.
using System.Collections.Generic;
using System.Linq;
using MemoryForge.Core;
using NUnit.Framework;
using UnityEngine;

namespace MemoryForge.PlayModeTests
{
    public class RunCreatePlayModeTests
    {
        // Reuses the authored Sera starter/cost rules and adds the source w_basic_ranged weapon, its card and the zone1 encounter record.
        static RunData SeraRunData()
        {
            var memory = MemoryRulesPlayModeTests.SeraRules();
            memory.Battle.Cards.Add(new BattleCardDef { Id = "wc_basic_ranged", Owner = "weapon", Rarity = "B" });
            memory.Battle.Weapons.Add(new BattleWeaponDef { Id = "w_basic_ranged", Type = "ranged", Rarity = "R", Pool = "craft", Card = "wc_basic_ranged" });
            var data = new RunData { Memory = memory, Stats = new StatsConfig() };
            data.Characters.Add(new RunCharacterMetadata { Id = "sera", WeaponType = "ranged" });
            List<List<string>> Rows(params string[][] rows) => rows.Select(r => r.ToList()).ToList();
            data.Encounters.Add(new RunEncounterDefinition { Zone = "1",
                Normal = Rows(new[] { "e_moth", "e_moth" }, new[] { "e_cog", "e_smudge" }, new[] { "e_hound" }, new[] { "e_lantern", "e_page", "e_page" },
                    new[] { "e_cog", "e_page" }, new[] { "e_smudge", "e_smudge", "e_moth" }, new[] { "e_hound", "e_page" }),
                Elite = Rows(new[] { "e_bellwarden" }, new[] { "e_mirrorclerk" }, new[] { "e_gardener" }),
                Boss = Rows(new[] { "b_bell" }, new[] { "b_notary" }, new[] { "b_garden" }) });
            return data;
        }

        static readonly uint[] Seed0Rng = { 4058753769u, 2789522146u, 1627861374u, 3282270325u };
        static readonly string[] FirstChoices = { "z1f0n0", "z1f0n1", "z1f0n2" };

        static Dictionary<string, object> Node(int f, int i, string type, params string[] next) => new Dictionary<string, object>
            { ["id"] = "z1f" + f + "n" + i, ["f"] = f, ["i"] = i, ["type"] = type, ["next"] = next.ToList() };
        // Exact JS seed0 zone1 map: widths 3/3/2/2/2/2/2, event at f1n0, rest guarantee at f3n0, one elite at f5n0.
        static Dictionary<string, object> Seed0Map() => new Dictionary<string, object>
        {
            ["zone"] = 1,
            ["floors"] = new List<object>
            {
                new List<object> { Node(0, 0, "battle", "z1f1n0"), Node(0, 1, "battle", "z1f1n1"), Node(0, 2, "battle", "z1f1n2") },
                new List<object> { Node(1, 0, "event", "z1f2n0"), Node(1, 1, "battle", "z1f2n1"), Node(1, 2, "battle", "z1f2n1", "z1f2n0") },
                new List<object> { Node(2, 0, "battle", "z1f3n0"), Node(2, 1, "battle", "z1f3n1") },
                new List<object> { Node(3, 0, "rest", "z1f4n0"), Node(3, 1, "battle", "z1f4n1") },
                new List<object> { Node(4, 0, "battle", "z1f5n0"), Node(4, 1, "battle", "z1f5n1") },
                new List<object> { Node(5, 0, "elite", "z1f6n0"), Node(5, 1, "battle", "z1f6n1", "z1f6n0") },
                new List<object> { Node(6, 0, "rest", "z1boss"), Node(6, 1, "forge", "z1boss") }
            },
            ["boss"] = new Dictionary<string, object> { ["id"] = "z1boss", ["type"] = "boss", ["f"] = 7, ["i"] = 0 }
        };
        static Dictionary<string, object> Card(string id, string key, string owner, string assigned) => new Dictionary<string, object>
            { ["id"] = id, ["key"] = key, ["owner"] = owner, ["assigned"] = assigned };
        static Dictionary<string, object> WeaponCard(string key) => new Dictionary<string, object>
            { ["id"] = "wc_basic_ranged", ["key"] = key, ["owner"] = "weapon", ["wielder"] = "sera", ["weapon"] = true };
        static Dictionary<string, object> Spec(string id, string key) => new Dictionary<string, object> { ["id"] = id, ["key"] = key };
        static List<object> SeraSpecs(string firstKey) => new List<object> { Spec("sera_round", firstKey), Spec("sera_cover", "base:sera:1"), Spec("sera_aim", "base:sera:2"),
            Spec("sera_timed", "base:sera:3"), new Dictionary<string, object> { ["id"] = "com_guard", ["assigned"] = "sera", ["key"] = "base:sera:4" } };
        static string Json(object graph) => CanonicalState.Serialize(graph);

        [Test]
        public void LegacySeraLevel20CreatesSourceDeckMapAndDetachedQueries()
        {
            Assert.That(Application.isPlaying, Is.True);
            var data = SeraRunData();
            var setup = new RunSetup { Seed = 0 };
            setup.Party.Add(RunPartyEntry.FromJsonGraph(new Dictionary<string, object> { ["charId"] = "sera", ["level"] = 20 }));
            string setupJson = Json(setup.Party.Select(p => (object)p.ToJsonGraph()).ToList());

            var run = Run.Create(data, setup);
            var state = run.State;
            // Level20 with no supplied weapon: charPower 20+floor(14*20/60)=24 -> power4 96; HP 60+20=80. The default weapon adds no stat power.
            Assert.That(Json(state.Party[0].ToJsonGraph()), Is.EqualTo(Json(new Dictionary<string, object>
                { ["charId"] = "sera", ["level"] = 20, ["power4"] = 96, ["hp"] = 80, ["gearBps"] = 0, ["skillLv"] = 1, ["ultLv"] = 1 })));
            Assert.That(state.Party[0].WeaponId, Is.Null);
            Assert.That(new[] { state.Hp, state.MaxHp, state.HpReference, state.Coins, state.SequenceCard }, Is.EqualTo(new[] { 80, 80, 80, 60, 6 }));
            Assert.That(state.Schema, Is.EqualTo("mf-run-0.7"));
            Assert.That(state.Phase, Is.EqualTo("MAP"));
            Assert.That(state.Position, Is.Null);
            Assert.That(state.MemoryPolicy, Is.Null);
            Assert.That(Json(state.Deck.Select(c => (object)c.ToJsonGraph()).ToList()), Is.EqualTo(Json(new List<object>
            {
                Card("sera_round", "k1", "sera", null), Card("sera_cover", "k2", "sera", null), Card("sera_aim", "k3", "sera", null),
                Card("sera_timed", "k4", "sera", null), Card("com_guard", "k5", "common", "sera"), WeaponCard("k6")
            })));
            Assert.That(state.Rng, Is.EqualTo(Seed0Rng));
            Assert.That(state.Map.Floors.Count, Is.EqualTo(7));
            Assert.That(Json(state.Map.ToJsonGraph()), Is.EqualTo(Json(Seed0Map())));
            Assert.That(run.Available(), Is.EqualTo(FirstChoices));

            string full = Json(state.ToJsonGraph());
            Assert.That(Json(Run.Create(data, setup).State.ToJsonGraph()), Is.EqualTo(full), "same setup recreates identical state/map/RNG/keys");

            // Returned choices, nodes and snapshots are detached copies.
            var choices = run.Available(); choices.Clear(); choices.Add("caller-mutation");
            var node = run.NodeById("z1f1n2"); node.Type = "caller-mutation"; node.Next.Add("caller-mutation");
            var boss = run.NodeById("z1boss"); boss.Id = "caller-mutation";
            state.Map.Floors[0][0].Next.Clear(); state.Map.Floors.Clear(); state.Rng[0] = 0; state.Party[0].Hp = 1; state.Statistics.Battles = 9; state.Deck.Clear();
            Assert.That(Json(run.State.ToJsonGraph()), Is.EqualTo(full));
            Assert.That(run.Available(), Is.EqualTo(FirstChoices));
            Assert.That(Json(run.NodeById("z1f1n2").ToJsonGraph()), Is.EqualTo(Json(Node(1, 2, "battle", "z1f2n1", "z1f2n0"))));
            Assert.That(Json(run.NodeById("z1boss").ToJsonGraph()), Is.EqualTo("{\"f\":7,\"i\":0,\"id\":\"z1boss\",\"type\":\"boss\"}"));
            Assert.That(run.NodeById("missing"), Is.Null);
            Assert.That(run.NodeById(""), Is.Null);
            Assert.That(Json(setup.Party.Select(p => (object)p.ToJsonGraph()).ToList()), Is.EqualTo(setupJson), "creation does not write derived stats into the caller setup");
        }

        [Test]
        public void V2BasePlaySkipsOccupiedSavedIdentityAndKeepsInputsDetached()
        {
            Assert.That(Application.isPlaying, Is.True);
            var data = SeraRunData();
            // Legitimate departure memory: the source baseline whose first card was saved under run:unity:1.
            var memory = new Dictionary<string, object> { ["sera"] = SeraSpecs("run:unity:1") };
            RunSetup Setup()
            {
                var setup = new RunSetup { Seed = 0, MemoryPolicy = "v2", RunId = "unity", MemoryVersion = 4, UseMemory = false, Memory = memory };
                setup.Party.Add(RunPartyEntry.FromJsonGraph(new Dictionary<string, object> { ["charId"] = "sera" }));
                return setup;
            }
            string memoryJson = Json(memory);

            var run = Run.Create(data, Setup());
            var state = run.State;
            Assert.That(state.Schema, Is.EqualTo("mf-run-memory-v2"));
            Assert.That(state.MemoryPolicy, Is.EqualTo("v2"));
            Assert.That(state.RunId, Is.EqualTo("unity"));
            Assert.That(state.MemoryVersion, Is.EqualTo(4));
            Assert.That(state.Hp, Is.EqualTo(81), "no level supplied: reference HP 81");
            Assert.That(state.SequenceCard, Is.EqualTo(3));
            Assert.That(Json(state.MemoryBefore.ToJsonGraph()), Is.EqualTo(Json(new Dictionary<string, object> { ["sera"] = SeraSpecs("run:unity:1") })), "saved identity stays in memoryBefore");
            Assert.That(Json(state.MemoryBaseline.ToJsonGraph()), Is.EqualTo(Json(new Dictionary<string, object> { ["sera"] = SeraSpecs("base:sera:0") })), "source baseline keeps base:sera:0");
            // Base play rekeys the changed starter: run:unity:1 is occupied by memoryBefore, so the next free key is run:unity:2.
            Assert.That(Json(state.PlayBefore.ToJsonGraph()), Is.EqualTo(Json(new Dictionary<string, object> { ["sera"] = SeraSpecs("run:unity:2") })));
            Assert.That(Json(state.Deck.Select(c => (object)c.ToJsonGraph()).ToList()), Is.EqualTo(Json(new List<object>
            {
                Card("sera_round", "run:unity:2", "sera", null), Card("sera_cover", "base:sera:1", "sera", null), Card("sera_aim", "base:sera:2", "sera", null),
                Card("sera_timed", "base:sera:3", "sera", null), Card("com_guard", "base:sera:4", "common", "sera"), WeaponCard("run:unity:3")
            })));
            Assert.That(state.Rng, Is.EqualTo(Seed0Rng));
            Assert.That(Json(state.Map.ToJsonGraph()), Is.EqualTo(Json(Seed0Map())));
            Assert.That(run.Available(), Is.EqualTo(FirstChoices));
            Assert.That(Json(memory), Is.EqualTo(memoryJson), "creation leaves the whole input memory unchanged");

            // Later caller mutation of the input or a snapshot never reaches the live run.
            string full = Json(run.State.ToJsonGraph());
            ((Dictionary<string, object>)((List<object>)memory["sera"])[1])["key"] = "caller-mutation";
            ((List<object>)memory["sera"]).Clear();
            state.MemoryBefore = null; state.Deck[0].Key = "caller-mutation"; state.Rng[3] = 0;
            Assert.That(Json(run.State.ToJsonGraph()), Is.EqualTo(full));

            // Invalid departure inputs fail with the source codes and leave their inputs untouched.
            void Rejects(string code, System.Action<RunSetup> change, Dictionary<string, object> saved)
            {
                var setup = Setup(); setup.Memory = saved; change(setup);
                string before = Json(saved);
                Assert.That(Assert.Throws<RuleException>(() => Run.Create(data, setup)).Code, Is.EqualTo(code));
                Assert.That(Json(saved), Is.EqualTo(before));
            }
            var duplicate = new Dictionary<string, object> { ["sera"] = SeraSpecs("base:sera:1") };
            Rejects("IDENTITY_REQUIRED", s => { }, duplicate);
            Rejects("INVALID_SNAPSHOT", s => s.MemoryVersion = -1, new Dictionary<string, object> { ["sera"] = SeraSpecs("run:unity:1") });
            Rejects("RUN_ID_REQUIRED", s => s.RunId = "", new Dictionary<string, object> { ["sera"] = SeraSpecs("run:unity:1") });
        }
    }
}
