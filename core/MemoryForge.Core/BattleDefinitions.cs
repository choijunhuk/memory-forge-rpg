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
        public int? Pct;
        internal EffectConditionDef Copy() => (EffectConditionDef)MemberwiseClone();
        internal Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object("c", Condition);
            if (Pct.HasValue) graph.Add("pct", Pct.Value);
            return graph;
        }
    }

    // This is the authored resonance effect schema, not a general DSL interpreter.
    // Unported commands/effect shapes are rejected by Battle rather than silently skipped.
    public sealed class EffectDef
    {
        public string Op = "";
        public int? V, N, Hits, Brk, Turns;
        public string? Target, Status, Part, Pick;
        public EffectScaleDef? Scale;
        public EffectConditionDef? Condition;
        public List<EffectDef>? Then, Else;
        internal EffectDef Copy()
        {
            var copy = (EffectDef)MemberwiseClone();
            copy.Scale = Scale?.Copy(); copy.Condition = Condition?.Copy();
            copy.Then = Then?.Select(e => e.Copy()).ToList(); copy.Else = Else?.Select(e => e.Copy()).ToList();
            return copy;
        }
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object("op", Op);
            BattleGraph.Optional(graph, "v", V); BattleGraph.Optional(graph, "n", N);
            BattleGraph.Optional(graph, "hits", Hits); BattleGraph.Optional(graph, "brk", Brk); BattleGraph.Optional(graph, "turns", Turns);
            BattleGraph.Optional(graph, "tgt", Target); BattleGraph.Optional(graph, "s", Status);
            BattleGraph.Optional(graph, "part", Part); BattleGraph.Optional(graph, "pick", Pick);
            if (Scale != null) graph.Add("scale", Scale.ToJsonGraph());
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
        public List<ResonanceFx> Resonance = new List<ResonanceFx>();
    }

    public sealed class BattleCardDef
    {
        public string Id = "", Owner = "", Rarity = "";
        public bool Growth;
    }

    public sealed class WeaponPassiveDef
    {
        public string Id = "", Name = "", Scope = "", Text = "";
        public List<int> Values = new List<int>();
        public List<int>? RefineAlt;
    }

    public sealed class BattleWeaponDef
    {
        public string Id = "";
        public WeaponPassiveDef Passive = new WeaponPassiveDef();
    }

    public sealed class BattlePartDef { public string Id = ""; }
    public sealed class BattleRelicDef { public string Id = ""; }

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

    public sealed class BattleEnemyDef
    {
        public string Id = "", Name = "", Tier = "";
        public int Hp, Brk;
        public List<string> Weak = new List<string>();
        public List<EnemyMoveDef> Pattern = new List<EnemyMoveDef>();
        public EnemyPassiveDef? Passive;
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
        internal Dictionary<string, BattleEnemyDef> EnemyIndex = new Dictionary<string, BattleEnemyDef>(StringComparer.Ordinal);
        public void Index()
        {
            CharacterIndex = Characters.ToDictionary(x => x.Id, StringComparer.Ordinal);
            CardIndex = Cards.ToDictionary(x => x.Id, StringComparer.Ordinal);
            WeaponIndex = Weapons.ToDictionary(x => x.Id, StringComparer.Ordinal);
            EnemyIndex = Enemies.ToDictionary(x => x.Id, StringComparer.Ordinal);
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
        // Run assigns persistent keys after Battle.Create; combat UIDs are never memory keys.
    }

    public sealed class BattleSetup
    {
        public List<BattleActorSetup> Party = new List<BattleActorSetup>();
        public List<BattleCardSpec> Deck = new List<BattleCardSpec>();
        public List<string> Enemies = new List<string>(), Relics = new List<string>();
        public int Hp, MaxHp, NextApBonus, DifficultyHp, DifficultyDamage;
        public uint? Seed;
        public Dictionary<string, object?> RunFlags = new Dictionary<string, object?>(StringComparer.Ordinal);
    }
}
