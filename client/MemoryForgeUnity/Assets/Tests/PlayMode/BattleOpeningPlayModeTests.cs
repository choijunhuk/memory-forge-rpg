using System.Collections.Generic;
using System.Linq;
using MemoryForge.Core;
using NUnit.Framework;
using UnityEngine;

namespace MemoryForge.PlayModeTests
{
    public class BattleOpeningPlayModeTests
    {
        // Authored Harin R4 + Nia/ledger R5 + bronze opening from the JS oracle.
        // This small typed fixture checks Unity runtime execution, not all-content import.
        static Battle Fresh(params string[] relics) => FreshPlayer("harin_shield", 0, relics);

        static BattleRules AuthoredRules() => new BattleRules { ApPerTurn = 4, ApMax = 9, HandDraw = 5, HandMax = 10,
                    DebtCap = 4, GrowthCapPerBattle = 3, GaugeMax = 100, WeakBps = 2500,
                    GaugePerCard = 10, GaugeTurnCap = 30, BreakApBonus = 1, BreakApBonusMaxPerTurn = 2,
                    VulnerableBps = 2500, BrokenDamageBps = 2500,
                    NextSlots = 2, AfterSlots = 1, NextSlotsMax = 3, AfterSlotsMax = 2 };

        static Battle FreshPlayer(string cardId, int gauge, params string[] relics)
        {
            var data = new BattleData { Rules = AuthoredRules() };
            data.Characters.Add(new BattleCharacterDef
            {
                Id = "harin", Name = "하린", Attr = "frost",
                Passive = new PassiveDef { Id = "harin_stance", Params = new Dictionary<string, int> { ["n"] = 1 } },
                Ult = new BattleUltDef { Name = "빙벽", Effects = new List<EffectDef>
                { new EffectDef { Op = "blk", V = 30 }, new EffectDef { Op = "counter", N = 6 } } },
                Resonance = new List<ResonanceFx>
                {
                    new ResonanceFx { Type = "cardMod", Cards = new List<string> { "harin_counter" }, Prop = "n", Add = 2 },
                    new ResonanceFx { Type = "passiveParam", Param = "n", IntValue = 2 },
                    new ResonanceFx { Type = "skillLv", Delta = 2 },
                    new ResonanceFx { Type = "multi", List = new List<ResonanceFx>
                    {
                        new ResonanceFx { Type = "battleStart", Effects = new List<EffectDef> { new EffectDef { Op = "blk", V = 10 } } },
                        new ResonanceFx { Type = "capacity", N = 1 }
                    } }
                }
            });
            data.Characters.Add(new BattleCharacterDef { Id = "nia", Name = "니아", Attr = "void", Passive = new PassiveDef { Id = "nia_handiwork", Params = new Dictionary<string, int> { ["bps"] = 1500 } } });
            data.Cards.Add(new BattleCardDef { Id = "harin_shield", Name = "경계 방패", Owner = "harin", Rarity = "B",
                Cost = 1, Type = "skl", Reservation = "y", Attr = "frost", Start = true,
                Effects = new List<EffectDef> { new EffectDef { Op = "blk", V = 8, Rv = 11 } } });
            data.Cards.Add(new BattleCardDef { Id = "harin_bulwark", Name = "불굴", Owner = "harin", Rarity = "S",
                Cost = 2, Type = "skl", Reservation = "n", Attr = "frost", Keywords = new List<string> { "exhaust" },
                Effects = new List<EffectDef> { new EffectDef { Op = "power", Id = "bulwark", Keep = 10 } } });
            data.Weapons.Add(new BattleWeaponDef { Id = "w_ledger_lock", Passive = new WeaponPassiveDef
            { Id = "w_startBlock", Scope = "wielder", Values = new List<int> { 4, 5, 6, 7, 8 } } });
            data.Enemies.Add(new BattleEnemyDef { Id = "e_shelf", Name = "무너진 서가", Tier = "normal", Hp = 46, Brk = 5,
                Weak = new List<string> { "flame", "void" }, Pattern = new List<EnemyMoveDef>
                { new EnemyMoveDef { Type = "def", Block = 10 }, new EnemyMoveDef { Type = "atk", Damage = 25 }, new EnemyMoveDef { Type = "blockAll", Block = 6 } } });
            return Battle.Create(data, new BattleSetup
            {
                Party = new List<BattleActorSetup> { new BattleActorSetup { CharId = "harin", Res = 4, Gauge = gauge },
                    new BattleActorSetup { CharId = "nia", WeaponId = "w_ledger_lock", Refine = 5 } },
                Deck = new List<BattleCardSpec> { new BattleCardSpec { Id = cardId } },
                Enemies = new List<string> { "e_shelf" }, Relics = new List<string>(relics),
                Hp = relics.Contains("infinite_circuit") ? 1 : 200, MaxHp = 243, Seed = 20261014
            });
        }

        static BattleRequest Play(string rid, int version, string mode) => new BattleRequest(rid, version, "play",
            BattleArgument.Object(new[] { new KeyValuePair<string, BattleArgument>("uid", BattleArgument.String("c1")),
                new KeyValuePair<string, BattleArgument>("mode", BattleArgument.String(mode)) }));

        [Test]
        public void ExhaustKillReshuffleUsesTheCurrentDiscardPile()
        {
            Assert.That(Application.isPlaying, Is.True);
            var data = new BattleData { Rules = AuthoredRules() };
            data.Characters.Add(new BattleCharacterDef { Id = "sion", Attr = "void", Passive = new PassiveDef
                { Id = "sion_shelf", Params = new Dictionary<string, int> { ["perBps"] = 400, ["maxBps"] = 4000 } } });
            data.Characters.Add(new BattleCharacterDef { Id = "nia", Attr = "void", Passive = new PassiveDef
                { Id = "nia_handiwork", Params = new Dictionary<string, int> { ["bps"] = 1500 } } });
            data.Characters.Add(new BattleCharacterDef { Id = "kaya", Attr = "flame", Passive = new PassiveDef { Id = "kaya_deduction" },
                Resonance = new List<ResonanceFx>
                {
                    new ResonanceFx { Type = "cardMod", Cards = new List<string> { "kaya_clue" }, Prop = "n", Add = 1 },
                    new ResonanceFx { Type = "hook", Id = "kaya_r2" },
                    new ResonanceFx { Type = "skillLv", Delta = 2 },
                    new ResonanceFx { Type = "multi", List = new List<ResonanceFx>
                    {
                        new ResonanceFx { Type = "battleStart", Effects = new List<EffectDef>
                            { new EffectDef { Op = "st", Status = "mark", N = 2, Target = "rand" } } },
                        new ResonanceFx { Type = "capacity", N = 1 }
                    } }
                } });
            data.Cards.Add(new BattleCardDef { Id = "sion_forget", Owner = "sion", Rarity = "A", Cost = 1, Type = "skl", Reservation = "n", Attr = "void",
                Effects = new List<EffectDef> { new EffectDef { Op = "exhaustPick", From = "discard", N = 2 },
                    new EffectDef { Op = "blk", V = 5, Base0 = true, Scale = new EffectScaleDef { Per = "lastCount", K = 5, Max = 10 } } } });
            data.Cards.Add(new BattleCardDef { Id = "com_sort", Owner = "common", Rarity = "B", Cost = 0, Type = "skl", Reservation = "n", Attr = "none", Uses = 2,
                Effects = new List<EffectDef> { new EffectDef { Op = "draw", N = 1 }, new EffectDef { Op = "discard", N = 1 } } });
            data.Cards.Add(new BattleCardDef { Id = "kaya_clue", Owner = "kaya", Rarity = "B", Cost = 0, Type = "skl", Reservation = "n", Attr = "flame", Start = true,
                Effects = new List<EffectDef> { new EffectDef { Op = "st", Status = "mark", N = 2, Target = "one" } } });
            data.Enemies.Add(new BattleEnemyDef { Id = "e_seed", Tier = "minion", Hp = 6, Brk = 1,
                Weak = new List<string> { "flame", "frost", "volt", "void" }, Pattern = new List<EnemyMoveDef> { new EnemyMoveDef { Type = "def", Block = 2 } } });
            var battle = Battle.Create(data, new BattleSetup
            {
                Party = new List<BattleActorSetup> { new BattleActorSetup { CharId = "sion" }, new BattleActorSetup { CharId = "kaya", Res = 4 }, new BattleActorSetup { CharId = "nia" } },
                Deck = new[] { "sion_forget", "com_sort", "kaya_clue", "kaya_clue", "kaya_clue", "kaya_clue" }.Select(id => new BattleCardSpec { Id = id }).ToList(),
                Enemies = new List<string> { "e_seed", "e_seed" }, Relics = new List<string> { "void_shard", "detective_notebook" },
                Hp = 243, MaxHp = 243, DifficultyHp = 50, Seed = 1
            });
            battle.Command(new BattleRequest("begin", 0, "begin"));
            var actions = new[] { (uid: "c2", target: "", pick: "c3"), (uid: "c6", target: "e7", pick: ""),
                (uid: "c4", target: "e8", pick: ""), (uid: "c1", target: "", pick: "c3") };
            foreach (var action in actions)
            {
                var args = new Dictionary<string, BattleArgument> { ["uid"] = BattleArgument.String(action.uid) };
                if (action.target != "") args.Add("target", BattleArgument.String(action.target));
                if (action.pick != "") args.Add("picks", BattleArgument.Array(new[] { BattleArgument.String(action.pick) }));
                int version = battle.State.V;
                battle.Command(new BattleRequest("play-" + version, version, "play", BattleArgument.Object(args)));
            }
            Assert.That(battle.State.Phase, Is.EqualTo("PLAYER"));
            Assert.That(battle.State.Block, Is.EqualTo(10));
            Assert.That(battle.State.KilledMarked, Is.EqualTo(1));
            Assert.That(battle.State.Zones.Exhaust, Is.EqualTo(new[] { "c3" }));
            Assert.That(battle.State.Zones.Draw, Is.EqualTo(new[] { "c6" }));
            Assert.That(battle.State.Events.Count(e => e.E == "kill"), Is.EqualTo(1));
            Assert.That(battle.State.Events.Any(e => e.E == "end"), Is.False);
        }

        [Test]
        public void AuthoredBulwarkRetainsTenBlockOnTheNextTurn()
        {
            Assert.That(Application.isPlaying, Is.True);
            var battle = FreshPlayer("harin_bulwark", 0, "bronze_cog");
            battle.Command(new BattleRequest("begin", 0, "begin"));
            var request = Play("bulwark", 1, "now");
            battle.Command(request);
            Assert.That(battle.State.Block, Is.EqualTo(26));
            Assert.That(battle.State.Ap, Is.EqualTo(2));
            Assert.That(battle.State.Actors[0].Gauge, Is.EqualTo(10));
            Assert.That(battle.State.Cards["c1"].Used, Is.EqualTo(1));
            Assert.That(battle.State.Zones.Exhaust, Is.EqualTo(new[] { "c1" }));
            var hash = CanonicalState.Hash(battle.State.ToJsonGraph());
            battle.Command(request).Events.Clear();
            Assert.That(CanonicalState.Hash(battle.State.ToJsonGraph()), Is.EqualTo(hash));
            battle.Command(new BattleRequest("end", 2, "end"));
            Assert.That(battle.State.Turn, Is.EqualTo(2));
            Assert.That(battle.State.Block, Is.EqualTo(10));
            Assert.That(battle.State.Counter, Is.Zero);
            Assert.That(battle.State.Cards["c1"].Used, Is.EqualTo(1));
        }

        [Test]
        public void ReservedShieldPaysNowAndEarnsGaugeOnlyWhenResolved()
        {
            Assert.That(Application.isPlaying, Is.True);
            var battle = Fresh("bronze_cog");
            battle.Command(new BattleRequest("begin", 0, "begin"));
            battle.Command(Play("reserve", 1, "next"));
            Assert.That(battle.State.Ap, Is.EqualTo(3));
            Assert.That(battle.State.Actors[0].Gauge, Is.Zero);
            Assert.That(battle.State.Cards["c1"].Used, Is.Zero);
            Assert.That(battle.State.Zones.Hand, Is.Empty);
            Assert.That(battle.State.Reservations.Count, Is.EqualTo(1));
            battle.Command(new BattleRequest("end", 2, "end"));
            Assert.That(battle.State.Turn, Is.EqualTo(2));
            Assert.That(battle.State.Block, Is.EqualTo(11));
            Assert.That(battle.State.Counter, Is.EqualTo(2));
            Assert.That(battle.State.Actors[0].Gauge, Is.EqualTo(10));
            Assert.That(battle.State.Cards["c1"].Used, Is.EqualTo(1));
            Assert.That(battle.State.Reservations, Is.Empty);
            Assert.That(battle.State.Zones.Hand, Is.EqualTo(new[] { "c1" }));
            Assert.That(battle.State.Events.Select(e => e.E), Is.EqualTo(new[] { "block", "resolved", "reshuffle", "turn" }));
        }

        [Test]
        public void AuthoredUltimateSpendsGaugeWithoutPlayingTheCardInHand()
        {
            Assert.That(Application.isPlaying, Is.True);
            var battle = FreshPlayer("harin_shield", 100, "bronze_cog");
            battle.Command(new BattleRequest("begin", 0, "begin"));
            var request = new BattleRequest("ultimate", 1, "ult", BattleArgument.Object(new[]
            { new KeyValuePair<string, BattleArgument>("actor", BattleArgument.String("harin")) }));
            battle.Command(request);
            Assert.That(battle.State.Block, Is.EqualTo(56));
            Assert.That(battle.State.Counter, Is.EqualTo(10));
            Assert.That(battle.State.Ap, Is.EqualTo(4));
            Assert.That(battle.State.Actors[0].Gauge, Is.Zero);
            Assert.That(battle.State.Cards["c1"].Used, Is.Zero);
            Assert.That(battle.State.Zones.Hand, Is.EqualTo(new[] { "c1" }));
            Assert.That(battle.State.Events.Select(e => e.E), Is.EqualTo(new[] { "ult", "block" }));
        }

        [Test]
        public void AuthoredOpeningAndIdempotentRetryExecuteInsidePlayMode()
        {
            Assert.That(Application.isPlaying, Is.True);
            var battle = Fresh("bronze_cog");
            var request = new BattleRequest("unity-opening", 0, "begin");
            var first = battle.Command(request);
            Assert.That(battle.State.Phase, Is.EqualTo("PLAYER"));
            Assert.That(battle.State.Block, Is.EqualTo(26));
            Assert.That(battle.State.Counter, Is.EqualTo(2));
            Assert.That(battle.State.Ap, Is.EqualTo(4));
            Assert.That(battle.State.Zones.Hand.Count, Is.EqualTo(1));
            var hash = CanonicalState.Hash(battle.State.ToJsonGraph());
            first.Events.Clear();
            Assert.That(battle.Command(request).Events.Count, Is.GreaterThan(0));
            Assert.That(CanonicalState.Hash(battle.State.ToJsonGraph()), Is.EqualTo(hash));
        }

        [Test]
        public void PureCardQueriesReturnDetachedDefinitionsWithoutChangingTheRunningBattle()
        {
            Assert.That(Application.isPlaying, Is.True);
            var battle = Fresh("bronze_cog");
            battle.Command(new BattleRequest("begin", 0, "begin"));
            var graph = battle.State.ToJsonGraph();
            var stateHash = CanonicalState.Hash(graph);
            var logHash = CanonicalState.Hash(graph["log"]);
            var eventsHash = CanonicalState.Hash(graph["ev"]);
            var effective = battle.EffectiveCard("c1");
            Assert.That(effective.Cost, Is.EqualTo(1));
            Assert.That(effective.Effects[0].V, Is.EqualTo(8));
            Assert.That(effective.Effects[0].Rv, Is.EqualTo(11));
            Assert.That(battle.AllowedModes("c1"), Is.EqualTo(new[] { "now", "next", "after" }));
            Assert.That(battle.CostOf("c1"), Is.EqualTo(1));
            Assert.That(battle.CostOf("c1", "after"), Is.EqualTo(1));
            Assert.That(battle.NeedsTarget("c1"), Is.Null);
            effective.Effects[0].V = 999;
            var modes = battle.AllowedModes("c1"); modes.Clear();
            Assert.That(battle.EffectiveCard("c1").Effects[0].V, Is.EqualTo(8));
            Assert.That(battle.AllowedModes("c1").Count, Is.EqualTo(3));
            graph = battle.State.ToJsonGraph();
            Assert.That(CanonicalState.Hash(graph), Is.EqualTo(stateHash));
            Assert.That(CanonicalState.Hash(graph["log"]), Is.EqualTo(logHash));
            Assert.That(CanonicalState.Hash(graph["ev"]), Is.EqualTo(eventsHash));
        }

        [Test]
        public void EnemyPhaseAndNextPlayerTurnReuseTheAuthoredOpening()
        {
            Assert.That(Application.isPlaying, Is.True);
            var battle = Fresh("bronze_cog");
            battle.Command(new BattleRequest("begin", 0, "begin"));
            battle.Command(new BattleRequest("end-one", 1, "end"));
            Assert.That(battle.State.Turn, Is.EqualTo(2));
            Assert.That(battle.State.Phase, Is.EqualTo("PLAYER"));
            Assert.That(battle.State.Block, Is.Zero);
            Assert.That(battle.State.Counter, Is.Zero);
            Assert.That(battle.State.Enemies[0].Block, Is.EqualTo(10));
            Assert.That(battle.State.Enemies[0].Intent.Type, Is.EqualTo("atk"));
            battle.Command(new BattleRequest("end-two", 2, "end"));
            Assert.That(battle.State.Turn, Is.EqualTo(3));
            Assert.That(battle.State.Hp, Is.EqualTo(175));
            Assert.That(battle.State.Enemies[0].Hp, Is.EqualTo(46));
            Assert.That(battle.State.Log.Count(e => e.E == "block"), Is.EqualTo(3));
        }

        [Test]
        public void UsedEnvelopeDoesNotHideAFatalSecondTurnCircuitCost()
        {
            Assert.That(Application.isPlaying, Is.True);
            var battle = Fresh("infinite_circuit", "memory_envelope");
            battle.Command(new BattleRequest("begin", 0, "begin"));
            Assert.That(battle.State.Hp, Is.EqualTo(1));
            battle.Command(new BattleRequest("end", 1, "end"));
            Assert.That(battle.State.Turn, Is.EqualTo(2));
            Assert.That(battle.State.Phase, Is.EqualTo("DEFEAT"));
            Assert.That(battle.State.Hp, Is.Zero);
            Assert.That(battle.State.Zones.Hand, Is.Empty);
            Assert.That(battle.State.Events.Count(e => e.E == "end"), Is.EqualTo(1));
            Assert.That(battle.State.Events.Any(e => e.E == "turn"), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CircuitTerminationAndEnvelopeSurvivalExecuteInsidePlayMode(bool envelope)
        {
            Assert.That(Application.isPlaying, Is.True);
            var battle = envelope ? Fresh("infinite_circuit", "memory_envelope") : Fresh("infinite_circuit");
            battle.Command(new BattleRequest("unity-circuit", 0, "begin"));
            Assert.That(battle.State.Phase, Is.EqualTo(envelope ? "PLAYER" : "DEFEAT"));
            Assert.That(battle.State.Hp, Is.EqualTo(envelope ? 1 : 0));
            Assert.That(battle.State.Events.Count(e => e.E == "end"), Is.EqualTo(envelope ? 0 : 1));
            Assert.That(battle.State.Zones.Hand.Count, Is.EqualTo(envelope ? 1 : 0));
        }
    }
}
