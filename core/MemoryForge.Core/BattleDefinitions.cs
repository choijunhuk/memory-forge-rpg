// §5/6/16.2: typed inputs for fresh Battle creation, opening and skipped-player turns.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoryForge.Core
{
    public sealed class BattleRules
    {
        public int ApPerTurn, ApMax, HandDraw, HandMax, DebtCap, GrowthCapPerBattle, GaugeMax;
        public int NextSlots, AfterSlots, NextSlotsMax, AfterSlotsMax, WeakBps;
        public int GaugePerCard, GaugeTurnCap, BreakApBonus, BreakApBonusMaxPerTurn, VulnerableBps, BrokenDamageBps;
        public int UpgradeNumerator = 13, UpgradeDenominator = 10;
        internal BattleRules Copy() => (BattleRules)MemberwiseClone();
    }

    public sealed class PassiveDef
    {
        public string Id = "", Name = "", Text = "";
        public Dictionary<string, int> Params = new Dictionary<string, int>(StringComparer.Ordinal);
        internal PassiveDef Copy() => new PassiveDef { Id = Id, Name = Name, Text = Text, Params = new Dictionary<string, int>(Params, StringComparer.Ordinal) };
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("id", Id, "name", Name,
            "params", Params.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal), "text", Text);
    }

    public sealed class EffectScaleDef
    {
        public string Per = "";
        public int K;
        public int? Max;
        internal EffectScaleDef Copy() => (EffectScaleDef)MemberwiseClone();
        internal Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object("per", Per, "k", K);
            if (Max.HasValue) graph.Add("max", Max.Value);
            return graph;
        }
    }

    public sealed class EffectConditionDef
    {
        public string Condition = "";
        public int? Pct, N;
        internal EffectConditionDef Copy() => (EffectConditionDef)MemberwiseClone();
        internal Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object("c", Condition);
            if (Pct.HasValue) graph.Add("pct", Pct.Value);
            if (N.HasValue) graph.Add("n", N.Value);
            return graph;
        }
    }

    public sealed class EffectMultiplierDef
    {
        public EffectConditionDef Condition = new EffectConditionDef();
        public int Multiplier;
        internal EffectMultiplierDef Copy() => new EffectMultiplierDef { Condition = Condition.Copy(), Multiplier = Multiplier };
        internal Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("if", Condition.ToJsonGraph(), "x", Multiplier);
    }

    // Typed source definitions; carrying an op does not claim its execution is ported.
    public sealed class EffectDef
    {
        public string Op = "";
        public int? V, Rv, Ra, N, Rn, Hits, Brk, Shock, Pierce, Turns, Next, After, Bps, Block, Keep, Heal, OverflowBlock;
        public string? Target, Status, Part, Pick, Who, Card, Zone, From, Filter, Id;
        public bool? Base0;
        public EffectScaleDef? Scale, HitsScale;
        public EffectConditionDef? Condition;
        public List<EffectMultiplierDef>? Multipliers;
        public List<EffectDef>? Then, Else;
        internal EffectDef Copy()
        {
            var copy = (EffectDef)MemberwiseClone();
            copy.Scale = Scale?.Copy(); copy.HitsScale = HitsScale?.Copy(); copy.Condition = Condition?.Copy();
            copy.Multipliers = Multipliers?.Select(m => m.Copy()).ToList();
            copy.Then = Then?.Select(e => e.Copy()).ToList(); copy.Else = Else?.Select(e => e.Copy()).ToList();
            return copy;
        }
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object("op", Op);
            BattleGraph.Optional(graph, "v", V); BattleGraph.Optional(graph, "rv", Rv); BattleGraph.Optional(graph, "ra", Ra);
            BattleGraph.Optional(graph, "n", N); BattleGraph.Optional(graph, "rn", Rn);
            BattleGraph.Optional(graph, "hits", Hits); BattleGraph.Optional(graph, "brk", Brk); BattleGraph.Optional(graph, "turns", Turns);
            BattleGraph.Optional(graph, "shock", Shock); BattleGraph.Optional(graph, "pierce", Pierce);
            BattleGraph.Optional(graph, "tgt", Target); BattleGraph.Optional(graph, "s", Status);
            BattleGraph.Optional(graph, "part", Part); BattleGraph.Optional(graph, "pick", Pick);
            BattleGraph.Optional(graph, "who", Who); BattleGraph.Optional(graph, "card", Card); BattleGraph.Optional(graph, "zone", Zone);
            BattleGraph.Optional(graph, "next", Next); BattleGraph.Optional(graph, "after", After);
            BattleGraph.Optional(graph, "from", From); BattleGraph.Optional(graph, "filter", Filter); BattleGraph.Optional(graph, "id", Id);
            BattleGraph.Optional(graph, "bps", Bps); BattleGraph.Optional(graph, "blk", Block); BattleGraph.Optional(graph, "keep", Keep);
            BattleGraph.Optional(graph, "heal", Heal); BattleGraph.Optional(graph, "overflowBlock", OverflowBlock); BattleGraph.Optional(graph, "base0", Base0);
            if (Scale != null) graph.Add("scale", Scale.ToJsonGraph());
            if (HitsScale != null) graph.Add("hitsScale", HitsScale.ToJsonGraph());
            if (Multipliers != null) graph.Add("mult", Multipliers.Select(m => m.ToJsonGraph()).ToList());
            if (Condition != null) graph.Add("cond", Condition.ToJsonGraph());
            if (Then != null) graph.Add("then", Then.Select(e => e.ToJsonGraph()).ToList());
            if (Else != null) graph.Add("else", Else.Select(e => e.ToJsonGraph()).ToList());
            return graph;
        }
    }

    public sealed class ResonanceFx
    {
        public string Type = "";
        public string? Id, Prop, Param, Key;
        public int? Add, Delta, N, Bps, IntValue;
        public bool? BoolValue, Once;
        public List<string>? Cards;
        public List<ResonanceFx>? List;
        public List<EffectDef>? Effects;
        internal ResonanceFx Copy()
        {
            var copy = (ResonanceFx)MemberwiseClone();
            copy.Cards = Cards == null ? null : new List<string>(Cards);
            copy.List = List?.Select(f => f.Copy()).ToList(); copy.Effects = Effects?.Select(e => e.Copy()).ToList();
            return copy;
        }
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object("type", Type);
            BattleGraph.Optional(graph, "id", Id); BattleGraph.Optional(graph, "prop", Prop);
            BattleGraph.Optional(graph, "param", Param); BattleGraph.Optional(graph, "key", Key);
            BattleGraph.Optional(graph, "add", Add); BattleGraph.Optional(graph, "delta", Delta);
            BattleGraph.Optional(graph, "n", N); BattleGraph.Optional(graph, "bps", Bps);
            if (IntValue.HasValue) graph.Add("value", IntValue.Value);
            else if (BoolValue.HasValue) graph.Add("value", BoolValue.Value);
            if (Once.HasValue) graph.Add("once", Once.Value);
            if (Cards != null) graph.Add("cards", new List<string>(Cards));
            if (List != null) graph.Add("list", List.Select(f => f.ToJsonGraph()).ToList());
            if (Effects != null) graph.Add("eff", Effects.Select(e => e.ToJsonGraph()).ToList());
            return graph;
        }
    }

    public sealed class BattleCharacterDef
    {
        public string Id = "", Name = "", Attr = "";
        public PassiveDef Passive = new PassiveDef();
        public BattleUltDef Ult = new BattleUltDef();
        public List<ResonanceFx> Resonance = new List<ResonanceFx>();
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("id", Id, "name", Name, "attr", Attr, "passive", Passive.ToJsonGraph(),
            "ult", Ult.ToJsonGraph(), "resonance", Resonance.Select(f => f.ToJsonGraph()).ToList());
    }

    public sealed class BattleUltDef
    {
        public string Name = "", Text = "";
        public List<EffectDef> Effects = new List<EffectDef>();
        internal BattleUltDef Copy() => new BattleUltDef { Name = Name, Text = Text, Effects = Effects.Select(e => e.Copy()).ToList() };
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("name", Name, "eff", Effects.Select(e => e.ToJsonGraph()).ToList(), "text", Text);
    }

    public sealed class BattleCardPlusDef
    {
        public List<EffectDef>? Effects;
        public bool EffectsPresent;
        internal BattleCardPlusDef Copy() => new BattleCardPlusDef { Effects = Effects?.Select(e => e.Copy()).ToList(), EffectsPresent = EffectsPresent };
        internal Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object(); if (EffectsPresent || Effects != null) graph.Add("eff", Effects?.Select(e => e.ToJsonGraph()).ToList()); return graph;
        }
    }
    public sealed class BattleCardCostIfDef
    {
        public EffectConditionDef Condition = new EffectConditionDef();
        public int Delta;
        internal BattleCardCostIfDef Copy() => new BattleCardCostIfDef { Condition = Condition.Copy(), Delta = Delta };
        internal Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("cond", Condition.ToJsonGraph(), "delta", Delta);
    }

    public sealed class BattleCardDef
    {
        public string Id = "", Name = "", Owner = "", Rarity = "", Type = "", Reservation = "", Attr = "";
        public int Cost;
        public int? Uses;
        public bool? Growth, Start, Pool;
        public string? Flavor;
        public List<string> Keywords = new List<string>();
        public List<string>? Evolutions;
        public List<EffectDef> Effects = new List<EffectDef>();
        public BattleCardPlusDef? Plus;
        public BattleCardCostIfDef? CostIf;
        public bool PlusPresent, CostIfPresent;
        internal BattleCardDef Copy()
        {
            var copy = (BattleCardDef)MemberwiseClone(); copy.Keywords = new List<string>(Keywords);
            copy.Evolutions = Evolutions == null ? null : new List<string>(Evolutions); copy.Effects = Effects.Select(e => e.Copy()).ToList();
            copy.Plus = Plus?.Copy(); copy.CostIf = CostIf?.Copy(); return copy;
        }
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object("id", Id, "name", Name, "owner", Owner, "rar", Rarity, "cost", Cost, "type", Type, "res", Reservation,
                "eff", Effects.Select(e => e.ToJsonGraph()).ToList(), "kw", new List<string>(Keywords), "attr", Attr);
            BattleGraph.Optional(graph, "uses", Uses); BattleGraph.Optional(graph, "growth", Growth); BattleGraph.Optional(graph, "start", Start);
            BattleGraph.Optional(graph, "pool", Pool); BattleGraph.Optional(graph, "flavor", Flavor);
            if (Evolutions != null) graph.Add("evo", new List<string>(Evolutions));
            if (PlusPresent || Plus != null) graph.Add("plus", Plus?.ToJsonGraph());
            if (CostIfPresent || CostIf != null) graph.Add("costIf", CostIf?.ToJsonGraph());
            return graph;
        }
    }

    public sealed class WeaponPassiveDef
    {
        public string Id = "", Name = "", Scope = "", Text = "";
        public List<int> Values = new List<int>();
        public List<int>? RefineAlt;
        internal WeaponPassiveDef Copy() => new WeaponPassiveDef { Id = Id, Name = Name, Scope = Scope, Text = Text, Values = new List<int>(Values), RefineAlt = RefineAlt == null ? null : new List<int>(RefineAlt) };
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object("id", Id, "name", Name, "values", new List<int>(Values), "scope", Scope, "text", Text);
            if (RefineAlt != null) graph.Add("refineAlt", new List<int>(RefineAlt)); return graph;
        }
    }

    public sealed class BattleWeaponDef
    {
        public string Id = "", Name = "", Type = "", Rarity = "", Pool = "", Card = "";
        public string? Signature;
        public WeaponPassiveDef Passive = new WeaponPassiveDef();
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object("id", Id, "name", Name, "type", Type, "rarity", Rarity, "pool", Pool, "passive", Passive.ToJsonGraph(), "card", Card);
            BattleGraph.Optional(graph, "signature", Signature); return graph;
        }
    }

    public sealed class BattlePartFx
    {
        public int? AddBps, AllBps, ResBps, ImmBps, AfterBps, Cost, Brk, Pierce, ExtraBlock, DrawOnce, Gauge, MarkOnHit, BurnOnHit, WeakOnHit, EchoCopy;
        public bool? Exhaust, NoReserve, Retain;
        internal BattlePartFx Copy() => (BattlePartFx)MemberwiseClone();
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object();
            BattleGraph.Optional(graph, "addBps", AddBps); BattleGraph.Optional(graph, "allBps", AllBps); BattleGraph.Optional(graph, "resBps", ResBps);
            BattleGraph.Optional(graph, "immBps", ImmBps); BattleGraph.Optional(graph, "afterBps", AfterBps); BattleGraph.Optional(graph, "cost", Cost);
            BattleGraph.Optional(graph, "brk", Brk); BattleGraph.Optional(graph, "pierce", Pierce); BattleGraph.Optional(graph, "extraBlock", ExtraBlock);
            BattleGraph.Optional(graph, "drawOnce", DrawOnce); BattleGraph.Optional(graph, "gauge", Gauge); BattleGraph.Optional(graph, "markOnHit", MarkOnHit);
            BattleGraph.Optional(graph, "burnOnHit", BurnOnHit); BattleGraph.Optional(graph, "weakOnHit", WeakOnHit); BattleGraph.Optional(graph, "echoCopy", EchoCopy);
            BattleGraph.Optional(graph, "exhaust", Exhaust); BattleGraph.Optional(graph, "noReserve", NoReserve);
            BattleGraph.Optional(graph, "retain", Retain); return graph;
        }
    }
    public sealed class BattlePartDef
    {
        public string Id = "", Slot = "", Name = "", Rarity = "", Text = "";
        public BattlePartFx Fx = new BattlePartFx();
        internal BattlePartDef Copy() => new BattlePartDef { Id = Id, Slot = Slot, Name = Name, Rarity = Rarity, Text = Text, Fx = Fx.Copy() };
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("id", Id, "slot", Slot, "name", Name, "rarity", Rarity, "text", Text, "fx", Fx.ToJsonGraph());
    }
    public sealed class BattleRelicDef
    {
        public string Id = "", Name = "", Rarity = "", Text = "";
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("id", Id, "name", Name, "rarity", Rarity, "text", Text);
    }

    public sealed class EnemyPassiveDef
    {
        public string Id = "";
        public int N;
        internal EnemyPassiveDef Copy() => (EnemyPassiveDef)MemberwiseClone();
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("id", Id, "n", N);
    }

    public sealed class EnemyMoveDef
    {
        public string Type = "";
        public string? Card, Status, Who, Label, Enemy;
        public int? Damage, Hits, Strength, Block, N;
        public EnemyMoveDef? Then;
        internal EnemyMoveDef Copy()
        {
            var copy = (EnemyMoveDef)MemberwiseClone(); copy.Then = Then?.Copy(); return copy;
        }
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object("t", Type);
            BattleGraph.Optional(graph, "d", Damage); BattleGraph.Optional(graph, "h", Hits);
            BattleGraph.Optional(graph, "str", Strength); BattleGraph.Optional(graph, "b", Block); BattleGraph.Optional(graph, "n", N);
            BattleGraph.Optional(graph, "card", Card); BattleGraph.Optional(graph, "s", Status); BattleGraph.Optional(graph, "who", Who);
            BattleGraph.Optional(graph, "label", Label); BattleGraph.Optional(graph, "e", Enemy);
            if (Then != null) graph.Add("then", Then.ToJsonGraph());
            return graph;
        }
    }

    /// <summary>Original enemy "phase" descriptor (§10.6), typed as written. It is descriptor data, never a pattern move: the engine
    /// validates it only at the boss's first HP 0, with the original INVALID_ENEMY_MOVE/WRONG_PHASE codes.</summary>
    public sealed class EnemyPhaseDef
    {
        public string? Type;
        public int? Hp, Brk;
        public List<string>? Weak;
        public List<EnemyMoveDef>? Pattern;
    }

    public sealed class BattleEnemyDef
    {
        public string Id = "", Name = "", Tier = "";
        public int Hp, Brk;
        public List<string> Weak = new List<string>();
        public List<EnemyMoveDef> Pattern = new List<EnemyMoveDef>();
        public EnemyPassiveDef? Passive;
        EnemyPhaseDef? phase;
        // Methods rather than a public field: the exported definition contract (Unity content round trip) keeps its shape,
        // and live content has no phase descriptor.
        public EnemyPhaseDef? PhaseDescriptor() => phase;
        public void SetPhaseDescriptor(EnemyPhaseDef? descriptor) => phase = descriptor;
    }

    public sealed class BattleData
    {
        public BattleRules Rules = new BattleRules();
        public List<BattleCharacterDef> Characters = new List<BattleCharacterDef>();
        public List<BattleCardDef> Cards = new List<BattleCardDef>();
        public List<BattleWeaponDef> Weapons = new List<BattleWeaponDef>();
        public List<BattlePartDef> Parts = new List<BattlePartDef>();
        public List<BattleRelicDef> Relics = new List<BattleRelicDef>();
        public List<BattleEnemyDef> Enemies = new List<BattleEnemyDef>();
        internal Dictionary<string, BattleCharacterDef> CharacterIndex = new Dictionary<string, BattleCharacterDef>(StringComparer.Ordinal);
        internal Dictionary<string, BattleCardDef> CardIndex = new Dictionary<string, BattleCardDef>(StringComparer.Ordinal);
        internal Dictionary<string, BattleWeaponDef> WeaponIndex = new Dictionary<string, BattleWeaponDef>(StringComparer.Ordinal);
        internal Dictionary<string, BattlePartDef> PartIndex = new Dictionary<string, BattlePartDef>(StringComparer.Ordinal);
        internal Dictionary<string, BattleEnemyDef> EnemyIndex = new Dictionary<string, BattleEnemyDef>(StringComparer.Ordinal);
        HashSet<string> attributeIds = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Original data.attributes IDs; only a phase descriptor's weaknesses are checked against them. Undeclared means none,
        /// so a phase change then rejects as the original does with no attributes. Not a public field: exported content keeps its shape.</summary>
        public void SetAttributeIds(IEnumerable<string> ids) => attributeIds = new HashSet<string>(ids, StringComparer.Ordinal);
        internal bool IsAttribute(string id) => attributeIds.Contains(id);
        public void Index()
        {
            CharacterIndex = Characters.ToDictionary(x => x.Id, StringComparer.Ordinal);
            CardIndex = Cards.ToDictionary(x => x.Id, StringComparer.Ordinal);
            WeaponIndex = Weapons.ToDictionary(x => x.Id, StringComparer.Ordinal);
            PartIndex = Parts.ToDictionary(x => x.Id, StringComparer.Ordinal);
            EnemyIndex = Enemies.ToDictionary(x => x.Id, StringComparer.Ordinal);
        }

        /// <summary>content-modes.js modeData for rift_break (§11.1): a separate data copy where every enemy break gauge, boss phase
        /// included, is max(1, Math.round(brk × gaugeBps / 10000)) and the broken bonus is replaced. This instance is never changed.</summary>
        internal BattleData WithRiftBreak(int gaugeBps, int brokenDamageBps)
        {
            // Math.round(x) = floor(x + 1/2) on the exact rational brk·gaugeBps/10000
            int Up(int brk)
            {
                var twice = 2 * (System.Numerics.BigInteger)brk * gaugeBps + 10000;
                var quotient = System.Numerics.BigInteger.DivRem(twice, 20000, out var remainder);
                if (remainder.Sign < 0) quotient -= 1;
                var value = System.Numerics.BigInteger.Max(1, quotient);
                if (value > int.MaxValue) throw new RuleException("BATTLE_NUMBER_OUT_OF_RANGE");
                return (int)value;
            }
            // a detached deep copy, as the source's JSON clone: changing it never reaches this data
            var copy = new BattleData
            {
                Rules = Rules.Copy(), attributeIds = new HashSet<string>(attributeIds, StringComparer.Ordinal),
                Characters = Characters.Select(c => new BattleCharacterDef { Id = c.Id, Name = c.Name, Attr = c.Attr, Passive = c.Passive.Copy(), Ult = c.Ult.Copy(), Resonance = c.Resonance.Select(f => f.Copy()).ToList() }).ToList(),
                Cards = Cards.Select(c => c.Copy()).ToList(), Parts = Parts.Select(p => p.Copy()).ToList(),
                Weapons = Weapons.Select(w => new BattleWeaponDef { Id = w.Id, Name = w.Name, Type = w.Type, Rarity = w.Rarity, Pool = w.Pool, Card = w.Card, Signature = w.Signature, Passive = w.Passive.Copy() }).ToList(),
                Relics = Relics.Select(r => new BattleRelicDef { Id = r.Id, Name = r.Name, Rarity = r.Rarity, Text = r.Text }).ToList()
            };
            copy.Rules.BrokenDamageBps = brokenDamageBps;
            foreach (var enemy in Enemies)
            {
                var changed = new BattleEnemyDef { Id = enemy.Id, Name = enemy.Name, Tier = enemy.Tier, Hp = enemy.Hp, Brk = Up(enemy.Brk), Weak = new List<string>(enemy.Weak),
                    Pattern = enemy.Pattern.Select(m => m.Copy()).ToList(), Passive = enemy.Passive?.Copy() };
                var phase = enemy.PhaseDescriptor();
                // A phase without brk stays without it, so the engine still rejects it at the boss's first HP 0.
                if (phase != null) changed.SetPhaseDescriptor(new EnemyPhaseDef { Type = phase.Type, Hp = phase.Hp, Brk = phase.Brk.HasValue ? Up(phase.Brk.Value) : (int?)null,
                    Weak = phase.Weak == null ? null : new List<string>(phase.Weak), Pattern = phase.Pattern?.Select(m => m.Copy()).ToList() });
                copy.Enemies.Add(changed);
            }
            copy.Index();
            return copy;
        }
    }

    public sealed class BattleActorSetup
    {
        public string CharId = "";
        public string? WeaponId;
        public int Res, Refine, Gauge, Power4, Hp, GearBps, SkillLv, UltLv;
    }

    public sealed class BattleCardSpec
    {
        public string Id = "";
        public string? Wielder, Circuit, Edge, Assigned, From;
        public bool Plus;
        public int Growth;
        internal string? SupportSourceId;
        // Run assigns persistent keys after Battle.Create; combat UIDs are never memory keys.
    }

    public sealed class BattleSetup
    {
        public BattleSupportSetup? Support;
        public List<BattleActorSetup> Party = new List<BattleActorSetup>();
        public List<BattleCardSpec> Deck = new List<BattleCardSpec>();
        public List<string> Enemies = new List<string>(), Relics = new List<string>();
        public int Hp, MaxHp, NextApBonus, DifficultyHp, DifficultyDamage;
        public uint? Seed;
        public Dictionary<string, object?> RunFlags = new Dictionary<string, object?>(StringComparer.Ordinal);
    }
}
