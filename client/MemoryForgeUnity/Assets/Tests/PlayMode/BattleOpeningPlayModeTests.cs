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
        static Battle Fresh(params string[] relics)
        {
            var data = new BattleData
            {
                Rules = new BattleRules { ApPerTurn = 4, ApMax = 9, HandDraw = 5, HandMax = 10,
                    DebtCap = 4, GrowthCapPerBattle = 3, GaugeMax = 100, WeakBps = 2500,
                    NextSlots = 2, AfterSlots = 1, NextSlotsMax = 3, AfterSlotsMax = 2 }
            };
            data.Characters.Add(new BattleCharacterDef
            {
                Id = "harin", Name = "하린", Attr = "frost",
                Passive = new PassiveDef { Id = "harin_stance", Params = new Dictionary<string, int> { ["n"] = 1 } },
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
            data.Cards.Add(new BattleCardDef { Id = "harin_shield", Owner = "harin", Rarity = "B" });
            data.Weapons.Add(new BattleWeaponDef { Id = "w_ledger_lock", Passive = new WeaponPassiveDef
            { Id = "w_startBlock", Scope = "wielder", Values = new List<int> { 4, 5, 6, 7, 8 } } });
            data.Enemies.Add(new BattleEnemyDef { Id = "e_shelf", Name = "무너진 서가", Tier = "normal", Hp = 46, Brk = 5,
                Weak = new List<string> { "flame", "void" }, Pattern = new List<EnemyMoveDef>
                { new EnemyMoveDef { Type = "def", Block = 10 }, new EnemyMoveDef { Type = "atk", Damage = 25 }, new EnemyMoveDef { Type = "blockAll", Block = 6 } } });
            return Battle.Create(data, new BattleSetup
            {
                Party = new List<BattleActorSetup> { new BattleActorSetup { CharId = "harin", Res = 4 },
                    new BattleActorSetup { CharId = "nia", WeaponId = "w_ledger_lock", Refine = 5 } },
                Deck = new List<BattleCardSpec> { new BattleCardSpec { Id = "harin_shield" } },
                Enemies = new List<string> { "e_shelf" }, Relics = new List<string>(relics),
                Hp = relics.Contains("infinite_circuit") ? 1 : 200, MaxHp = 243, Seed = 20261014
            });
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
