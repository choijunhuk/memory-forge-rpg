using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MemoryForge.Core
{
    internal static class BattleGraph
    {
        internal static Dictionary<string, object?> Object(params object?[] pairs)
        {
            var graph = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) graph.Add((string)pairs[i]!, pairs[i + 1]);
            return graph;
        }
        internal static void Optional(Dictionary<string, object?> graph, string name, int? value) { if (value.HasValue) graph.Add(name, value.Value); }
        internal static void Optional(Dictionary<string, object?> graph, string name, string? value) { if (value != null) graph.Add(name, value); }
        internal static Dictionary<string, object?> CopyDictionary(Dictionary<string, object?> source) => (Dictionary<string, object?>)Copy(source)!;
        internal static object? Copy(object? value) => Copy(value, new List<object>());
        static object? Copy(object? value, List<object> ancestors)
        {
            if (value == null || value is string || value is bool || value is int || value is uint) return value;
            if (value is long n && n >= -CanonicalState.MaxSafeInteger && n <= CanonicalState.MaxSafeInteger) return n;
            if (ancestors.Count >= 256 || ancestors.Any(a => ReferenceEquals(a, value))) throw new RuleException("UNSUPPORTED_STATE_VALUE");
            ancestors.Add(value);
            try
            {
                if (value is Dictionary<string, object?> obj) return obj.ToDictionary(p => p.Key, p => Copy(p.Value, ancestors), StringComparer.Ordinal);
                if (value is IList list) { var copy = new List<object?>(); foreach (var item in list) copy.Add(Copy(item, ancestors)); return copy; }
                throw new RuleException("UNSUPPORTED_STATE_VALUE");
            }
            finally { ancestors.RemoveAt(ancestors.Count - 1); }
        }
        internal static bool Truthy(object? value) => value != null && (!(value is bool b) || b) && (!(value is int i) || i != 0)
            && (!(value is uint u) || u != 0) && (!(value is long n) || n != 0) && (!(value is string text) || text.Length != 0);
    }

    public sealed class BattleWeaponState
    {
        public string Id = "";
        public int Refine;
        internal BattleWeaponState Copy() => (BattleWeaponState)MemberwiseClone();
        internal Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("id", Id, "refine", Refine);
    }

    public sealed class BattleUltExtra
    {
        public List<EffectDef> Effects = new List<EffectDef>();
        public bool Once;
        internal BattleUltExtra Copy() => new BattleUltExtra { Effects = Effects.Select(e => e.Copy()).ToList(), Once = Once };
        internal Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("eff", Effects.Select(e => e.ToJsonGraph()).ToList(), "once", Once);
    }

    public sealed class BattleActorState
    {
        public string Id = "", Name = "", Attr = "";
        public int Gauge, GaugeTurn, Res, Power4, Vit, GearBps, Capacity;
        public long SkillLv, UltLv;
        public PassiveDef Passive = new PassiveDef();
        public BattleWeaponState? Weapon;
        public List<ResonanceFx> CardMods = new List<ResonanceFx>(), Hooks = new List<ResonanceFx>();
        public List<EffectDef> BattleStart = new List<EffectDef>();
        public List<BattleUltExtra> UltExtra = new List<BattleUltExtra>();
        public List<EffectDef>? UltReplace;
        internal BattleActorState Copy()
        {
            var copy = (BattleActorState)MemberwiseClone();
            copy.Passive = Passive.Copy(); copy.Weapon = Weapon?.Copy();
            copy.CardMods = CardMods.Select(f => f.Copy()).ToList(); copy.Hooks = Hooks.Select(f => f.Copy()).ToList();
            copy.BattleStart = BattleStart.Select(e => e.Copy()).ToList(); copy.UltExtra = UltExtra.Select(e => e.Copy()).ToList();
            copy.UltReplace = UltReplace?.Select(e => e.Copy()).ToList();
            return copy;
        }
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("id", Id, "name", Name, "attr", Attr,
            "gauge", Gauge, "gaugeTurn", GaugeTurn, "res", Res, "power4", Power4, "vit", Vit, "gearBps", GearBps,
            "skillLv", SkillLv, "ultLv", UltLv, "passive", Passive.ToJsonGraph(), "weapon", Weapon?.ToJsonGraph(),
            "cardMods", CardMods.Select(f => f.ToJsonGraph()).ToList(), "battleStart", BattleStart.Select(e => e.ToJsonGraph()).ToList(),
            "ultExtra", UltExtra.Select(e => e.ToJsonGraph()).ToList(), "ultReplace", UltReplace?.Select(e => e.ToJsonGraph()).ToList(),
            "hooks", Hooks.Select(f => f.ToJsonGraph()).ToList(), "capacity", Capacity);
    }

    public sealed class BattleCardState
    {
        public string Uid = "", Id = "", Owner = "";
        public string? Wielder, Circuit, Edge, Assigned, From;
        public bool Plus, Grown, Token, Free;
        public int Growth, Used, CostMod;
        public List<string> Temp = new List<string>();
        internal string? Key { get; set; }
        internal BattleCardState Copy()
        {
            var copy = (BattleCardState)MemberwiseClone(); copy.Temp = new List<string>(Temp); return copy;
        }
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object("uid", Uid, "id", Id, "owner", Owner, "wielder", Wielder, "plus", Plus,
                "mods", BattleGraph.Object("c", Circuit, "e", Edge), "temp", new List<string>(Temp), "growth", Growth,
                "grown", Grown, "used", Used, "costMod", CostMod, "token", Token, "free", Free, "assigned", Assigned, "from", From);
            if (Key != null) graph.Add("key", Key);
            return graph;
        }
    }

    public sealed class BattleEnemyStatus
    {
        public int Vul, Weak, Mark, Burn, Strength, Reflect;
        internal BattleEnemyStatus Copy() => (BattleEnemyStatus)MemberwiseClone();
        internal Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("vul", Vul, "weak", Weak, "mark", Mark, "burn", Burn, "str", Strength, "reflect", Reflect);
    }

    public sealed class BattleEnemyState
    {
        public string Uid = "", Id = "", Name = "", Tier = "";
        public int Hp, MaxHp, Block, BrkMax, Brk, BrokenUntil, PatternIndex;
        public bool Stunned, Alive = true;
        public List<string> Weak = new List<string>();
        public BattleEnemyStatus Status = new BattleEnemyStatus();
        public EnemyMoveDef? Intent, Next;
        public EnemyPassiveDef? Passive;
        internal BattleEnemyState Copy()
        {
            var copy = (BattleEnemyState)MemberwiseClone();
            copy.Weak = new List<string>(Weak); copy.Status = Status.Copy(); copy.Passive = Passive?.Copy();
            copy.Intent = Intent?.Copy(); copy.Next = Next?.Copy(); return copy;
        }
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("uid", Uid, "id", Id, "name", Name, "tier", Tier,
            "hp", Hp, "maxHp", MaxHp, "block", Block, "brkMax", BrkMax, "brk", Brk, "weak", new List<string>(Weak), "st", Status.ToJsonGraph(),
            "brokenUntil", BrokenUntil, "stunned", Stunned, "pi", PatternIndex, "intent", Intent?.ToJsonGraph(), "next", Next?.ToJsonGraph(), "alive", Alive, "passive", Passive?.ToJsonGraph());
    }

    public sealed class BattleZones
    {
        public List<string> Draw = new List<string>(), Hand = new List<string>(), Discard = new List<string>(), Exhaust = new List<string>();
        internal BattleZones Copy() => new BattleZones { Draw = new List<string>(Draw), Hand = new List<string>(Hand), Discard = new List<string>(Discard), Exhaust = new List<string>(Exhaust) };
        internal Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("draw", new List<string>(Draw), "hand", new List<string>(Hand), "discard", new List<string>(Discard), "exhaust", new List<string>(Exhaust));
    }
    public sealed class BattleSlots
    {
        public int Next, After;
        internal BattleSlots Copy() => (BattleSlots)MemberwiseClone();
        internal Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("next", Next, "after", After);
    }
    public sealed class BattleBuff
    {
        public string Status = "";
        public int N, Until;
        internal BattleBuff Copy() => (BattleBuff)MemberwiseClone();
        internal Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("s", Status, "n", N, "until", Until);
    }
    public sealed class BattleDebt
    {
        public int N, Due;
        internal BattleDebt Copy() => (BattleDebt)MemberwiseClone();
        internal Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("n", N, "due", Due);
    }
    public sealed class BattleEvent
    {
        public string E = "";
        public int? T, N, Growth, Turn, Ap;
        public string? Uid, Source, Status, Part, Phase;
        internal BattleEvent Copy() => (BattleEvent)MemberwiseClone();
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object("e", E);
            BattleGraph.Optional(graph, "t", T); BattleGraph.Optional(graph, "n", N); BattleGraph.Optional(graph, "growth", Growth);
            BattleGraph.Optional(graph, "turn", Turn); BattleGraph.Optional(graph, "ap", Ap);
            BattleGraph.Optional(graph, "uid", Uid); BattleGraph.Optional(graph, "src", Source); BattleGraph.Optional(graph, "s", Status);
            BattleGraph.Optional(graph, "part", Part); BattleGraph.Optional(graph, "phase", Phase); return graph;
        }
    }
    public sealed class BattleResult
    {
        public string Phase = "";
        public int Turn, Hp;
        internal BattleResult Copy() => (BattleResult)MemberwiseClone();
        internal Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("phase", Phase, "turn", Turn, "hp", Hp);
    }

    public sealed class BattleState
    {
        public string Schema = "mf-battle-0.7", Phase = "CREATED";
        public int V, Turn, Seq, Hp, MaxHp, Block, Counter, Ap, ApNext, ApDrainNext, SilenceUntil, BreakApTurn, Weak;
        public uint[] Rng = new uint[4];
        public List<BattleBuff> Buffs = new List<BattleBuff>();
        public List<BattleDebt> Debts = new List<BattleDebt>();
        public int DebtCap, RestraintUsedTurn, BonusTurn;
        public bool RestraintActive, BonusAvailable;
        public int DrawPenalty, SealedUntil, GrowthCount, GrowthCap, Played;
        public BattleSlots SlotsBase = new BattleSlots(), SlotsTemp = new BattleSlots();
        public string? LastOwner;
        public List<string> OwnersThisTurn = new List<string>();
        public int Echo, EchoMax = 3, EchoStart, Contracts, Exhausted, ExhaustStart, TempParts, FreeNext;
        public bool EchoActive, FirstAttackDone, OverloadFreeUsed;
        public Dictionary<string, object?>? NextBoost;
        public List<BattleActorState> Actors = new List<BattleActorState>();
        public List<BattleEnemyState> Enemies = new List<BattleEnemyState>();
        public Dictionary<string, BattleCardState> Cards = new Dictionary<string, BattleCardState>(StringComparer.Ordinal);
        internal List<string> CardOrder = new List<string>();
        public BattleZones Zones = new BattleZones();
        // These remain empty in the fresh-opening API; no restored-state execution is exposed.
        public List<Dictionary<string, object?>> Reservations = new List<Dictionary<string, object?>>(), Powers = new List<Dictionary<string, object?>>();
        public int ReservationSeq, HandMax, KilledMarked, DifficultyHp = 100, DifficultyDamage = 100;
        public List<string> Relics = new List<string>();
        public Dictionary<string, object?> TurnFlags = new Dictionary<string, object?>(StringComparer.Ordinal),
            BattleFlags = new Dictionary<string, object?>(StringComparer.Ordinal), RunFlags = new Dictionary<string, object?>(StringComparer.Ordinal);
        public List<BattleEvent> Log = new List<BattleEvent>(), Events = new List<BattleEvent>();
        public BattleResult? Result;

        internal BattleState Copy()
        {
            var copy = (BattleState)MemberwiseClone();
            copy.Rng = (uint[])Rng.Clone(); copy.Buffs = Buffs.Select(b => b.Copy()).ToList(); copy.Debts = Debts.Select(d => d.Copy()).ToList();
            copy.SlotsBase = SlotsBase.Copy(); copy.SlotsTemp = SlotsTemp.Copy(); copy.OwnersThisTurn = new List<string>(OwnersThisTurn);
            copy.NextBoost = NextBoost == null ? null : BattleGraph.CopyDictionary(NextBoost);
            copy.Actors = Actors.Select(a => a.Copy()).ToList(); copy.Enemies = Enemies.Select(e => e.Copy()).ToList();
            copy.Cards = Cards.ToDictionary(p => p.Key, p => p.Value.Copy(), StringComparer.Ordinal); copy.CardOrder = new List<string>(CardOrder);
            copy.Zones = Zones.Copy(); copy.Reservations = Reservations.Select(BattleGraph.CopyDictionary).ToList(); copy.Powers = Powers.Select(BattleGraph.CopyDictionary).ToList();
            copy.Relics = new List<string>(Relics); copy.TurnFlags = BattleGraph.CopyDictionary(TurnFlags);
            copy.BattleFlags = BattleGraph.CopyDictionary(BattleFlags); copy.RunFlags = BattleGraph.CopyDictionary(RunFlags);
            copy.Log = Log.Select(e => e.Copy()).ToList(); copy.Events = Events.Select(e => e.Copy()).ToList(); copy.Result = Result?.Copy();
            return copy;
        }

        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object(
            "schema", Schema, "v", V, "turn", Turn, "phase", Phase, "rng", (uint[])Rng.Clone(), "seq", Seq,
            "hp", Hp, "maxHp", MaxHp, "block", Block, "counter", Counter, "ap", Ap, "apNext", ApNext, "apDrainNext", ApDrainNext, "silenceUntil", SilenceUntil, "breakApTurn", BreakApTurn,
            "pst", BattleGraph.Object("weak", Weak), "buffs", Buffs.Select(b => b.ToJsonGraph()).ToList(), "debts", Debts.Select(d => d.ToJsonGraph()).ToList(), "debtCap", DebtCap,
            "restraint", BattleGraph.Object("active", RestraintActive, "usedTurn", RestraintUsedTurn), "bonus", BattleGraph.Object("turn", BonusTurn, "avail", BonusAvailable),
            "drawPenalty", DrawPenalty, "sealedUntil", SealedUntil, "slotsBase", SlotsBase.ToJsonGraph(), "slotsTemp", SlotsTemp.ToJsonGraph(), "growthCount", GrowthCount, "growthCap", GrowthCap,
            "played", Played, "lastOwner", LastOwner, "ownersThisTurn", new List<string>(OwnersThisTurn), "echo", Echo, "echoMax", EchoMax, "echoActive", EchoActive, "echoStart", EchoStart,
            "contracts", Contracts, "exhausted", Exhausted, "exhaustStart", ExhaustStart, "tempParts", TempParts, "freeNext", FreeNext, "nextBoost", NextBoost == null ? null : BattleGraph.CopyDictionary(NextBoost),
            "firstAtkDone", FirstAttackDone, "overloadFreeUsed", OverloadFreeUsed, "actors", Actors.Select(a => a.ToJsonGraph()).ToList(), "enemies", Enemies.Select(e => e.ToJsonGraph()).ToList(),
            "cards", CardOrder.ToDictionary(uid => uid, uid => (object?)Cards[uid].ToJsonGraph(), StringComparer.Ordinal), "zones", Zones.ToJsonGraph(), "res", Reservations.Select(BattleGraph.CopyDictionary).ToList(),
            "resSeq", ReservationSeq, "relics", new List<string>(Relics), "powers", Powers.Select(BattleGraph.CopyDictionary).ToList(), "turnFlags", BattleGraph.CopyDictionary(TurnFlags),
            "battleFlags", BattleGraph.CopyDictionary(BattleFlags), "runFlags", BattleGraph.CopyDictionary(RunFlags), "handMax", HandMax, "log", Log.Select(e => e.ToJsonGraph()).ToList(),
            "ev", Events.Select(e => e.ToJsonGraph()).ToList(), "result", Result?.ToJsonGraph(), "killedMarked", KilledMarked, "diff", BattleGraph.Object("hp", DifficultyHp, "dmg", DifficultyDamage));
    }

    /// <summary>Ordered JSON transport values, independent of state canonicalization. Numbers are JS-safe integers; fractional request args are explicitly unsupported.</summary>
    public sealed class BattleArgument
    {
        readonly object? scalar;
        readonly IReadOnlyList<BattleArgument>? array;
        readonly IReadOnlyList<KeyValuePair<string, BattleArgument>>? properties;
        BattleArgument(object? value) { scalar = value; }
        BattleArgument(IEnumerable<BattleArgument> values)
        {
            var list = values.ToList();
            if (list.Any(value => value == null)) throw new RuleException("INVALID_REQUEST_VALUE");
            array = list.AsReadOnly();
        }
        BattleArgument(IEnumerable<KeyValuePair<string, BattleArgument>> values)
        {
            var list = values.ToList();
            if (list.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count() != list.Count) throw new RuleException("DUPLICATE_REQUEST_KEY");
            if (list.Any(p => p.Key == null || p.Value == null)) throw new RuleException("INVALID_REQUEST_VALUE");
            properties = list.AsReadOnly();
        }
        public static BattleArgument Null() => new BattleArgument((object?)null);
        public static BattleArgument String(string value) => new BattleArgument(value ?? throw new ArgumentNullException(nameof(value)));
        public static BattleArgument Boolean(bool value) => new BattleArgument(value);
        public static BattleArgument Integer(long value)
        {
            if (value < -CanonicalState.MaxSafeInteger || value > CanonicalState.MaxSafeInteger) throw new RuleException("UNSUPPORTED_REQUEST_NUMBER");
            return new BattleArgument(value);
        }
        public static BattleArgument Array(IEnumerable<BattleArgument> values) => new BattleArgument(values);
        public static BattleArgument Object(IEnumerable<KeyValuePair<string, BattleArgument>> values) => new BattleArgument(values);
        internal bool Truthy => array != null || properties != null || BattleGraph.Truthy(scalar);
        internal void Append(StringBuilder text)
        {
            if (properties != null)
            {
                text.Append('{'); bool first = true;
                // JSON.stringify enumerates integer-index keys first, then other keys in insertion order.
                var ordered = properties.Where(p => ArrayIndex(p.Key).HasValue).OrderBy(p => ArrayIndex(p.Key).GetValueOrDefault())
                    .Concat(properties.Where(p => !ArrayIndex(p.Key).HasValue));
                foreach (var p in ordered)
                {
                    if (!first) text.Append(','); first = false;
                    CanonicalState.AppendString(text, p.Key); text.Append(':'); p.Value.Append(text);
                }
                text.Append('}');
            }
            else if (array != null)
            {
                text.Append('['); bool first = true;
                foreach (var item in array) { if (!first) text.Append(','); first = false; item.Append(text); }
                text.Append(']');
            }
            else if (scalar is string s) CanonicalState.AppendString(text, s);
            else if (scalar is bool b) text.Append(b ? "true" : "false");
            else if (scalar is long n) text.Append(n.ToString(CultureInfo.InvariantCulture));
            else text.Append("null");
        }
        static uint? ArrayIndex(string key) => uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out uint n)
            && n != uint.MaxValue && n.ToString(CultureInfo.InvariantCulture) == key ? n : (uint?)null;
    }

    public sealed class BattleRequest
    {
        public string? Rid { get; }
        public int V { get; }
        public string Kind { get; }
        public BattleArgument? Args { get; }
        public BattleRequest(string? rid, int v, string kind, BattleArgument? args = null) { Rid = rid; V = v; Kind = kind ?? throw new ArgumentNullException(nameof(kind)); Args = args; }
        internal string ReceiptIdentity()
        {
            var text = new StringBuilder("{\"v\":"); text.Append(V.ToString(CultureInfo.InvariantCulture)); text.Append(",\"kind\":");
            CanonicalState.AppendString(text, Kind); text.Append(",\"args\":");
            if (Args != null && Args.Truthy) Args.Append(text); else text.Append("{}");
            return text.Append('}').ToString();
        }
    }

    public sealed class BattleResponse
    {
        public int V;
        public string Phase = "";
        public List<BattleEvent> Events = new List<BattleEvent>();
        internal BattleResponse Copy() => new BattleResponse { V = V, Phase = Phase, Events = Events.Select(e => e.Copy()).ToList() };
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("v", V, "phase", Phase, "ev", Events.Select(e => e.ToJsonGraph()).ToList());
    }
}
