// Host-only JSON adapter. Core has typed definitions and no JSON/package dependency.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using MemoryForge.Core;

static class BattleDataLoader
{
    public static BattleData Load(string root)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
        var json = document.RootElement;
        var rules = json.GetProperty("rules"); var slots = rules.GetProperty("slots");
        // Content decimals are parsed only at their typed boundary, never by the
        // deliberately integer-only snapshot/transport readers.
        var data = new BattleData { Rules = new BattleRules { ApPerTurn = I(rules, "apPerTurn"), ApMax = I(rules, "apMax"),
            HandDraw = I(rules, "handDraw"), HandMax = I(rules, "handMax"), DebtCap = I(rules, "debtCap"),
            GrowthCapPerBattle = I(rules, "growthCapPerBattle"), GaugeMax = I(rules, "gaugeMax"), WeakBps = I(rules, "weakBps"),
            GaugePerCard = I(rules, "gaugePerCard"), GaugeTurnCap = I(rules, "gaugeTurnCap"), BreakApBonus = I(rules, "breakApBonus"),
            BreakApBonusMaxPerTurn = I(rules, "breakApBonusMaxPerTurn"), VulnerableBps = I(rules, "vulnerableBps"), BrokenDamageBps = I(rules, "brokenDamageBps"),
            NextSlots = I(slots, "next"), AfterSlots = I(slots, "after"), NextSlotsMax = I(slots, "nextMax"), AfterSlotsMax = I(slots, "afterMax") } };
        (data.Rules.UpgradeNumerator, data.Rules.UpgradeDenominator) = Ratio(rules.GetProperty("upgradeMult"));
        foreach (var ch in json.GetProperty("characters").EnumerateArray())
        {
            var p = ch.GetProperty("passive"); Fields(p, "id", "name", "params", "text");
            data.Characters.Add(new BattleCharacterDef { Id = S(ch, "id"), Name = S(ch, "name"), Attr = S(ch, "attr"),
                Passive = new PassiveDef { Id = S(p, "id"), Name = S(p, "name"), Text = S(p, "text"),
                    Params = p.GetProperty("params").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetInt32(), StringComparer.Ordinal) },
                Ult = Ult(ch.GetProperty("ult")), Resonance = ch.GetProperty("resonance").EnumerateArray().Select(n => Fx(n.GetProperty("fx"))).ToList() });
        }
        foreach (var c in json.GetProperty("cards").EnumerateArray())
            data.Cards.Add(Card(c));
        foreach (var w in json.GetProperty("weapons").EnumerateArray())
        {
            var p = w.GetProperty("passive"); Fields(p, "id", "name", "scope", "text", "values", "refineAlt");
            Fields(w, "id", "name", "type", "rarity", "pool", "card", "signature", "passive");
            data.Weapons.Add(new BattleWeaponDef { Id = S(w, "id"), Name = S(w, "name"), Type = S(w, "type"), Rarity = S(w, "rarity"), Pool = S(w, "pool"), Card = S(w, "card"), Signature = ContentString(w, "signature"),
                Passive = new WeaponPassiveDef { Id = S(p, "id"), Name = S(p, "name"), Scope = S(p, "scope"), Text = S(p, "text"),
                Values = Ints(p.GetProperty("values")), RefineAlt = p.TryGetProperty("refineAlt", out var alt) ? Ints(alt) : null } });
        }
        foreach (var part in json.GetProperty("parts").EnumerateArray())
        {
            Fields(part, "id", "slot", "name", "rarity", "text", "fx");
            data.Parts.Add(new BattlePartDef { Id = S(part, "id"), Slot = S(part, "slot"), Name = S(part, "name"), Rarity = S(part, "rarity"), Text = S(part, "text"), Fx = PartFx(part.GetProperty("fx")) });
        }
        foreach (var relic in json.GetProperty("relics").EnumerateArray())
        {
            Fields(relic, "id", "name", "rarity", "text");
            data.Relics.Add(new BattleRelicDef { Id = S(relic, "id"), Name = S(relic, "name"), Rarity = S(relic, "rarity"), Text = S(relic, "text") });
        }
        foreach (var enemy in json.GetProperty("enemies").EnumerateArray())
        {
            Fields(enemy, "id", "name", "zone", "tier", "hp", "brk", "weak", "passive", "pattern");
            EnemyPassiveDef? passive = null;
            if (enemy.TryGetProperty("passive", out var p) && p.ValueKind != JsonValueKind.Null)
            {
                Fields(p, "id", "n"); passive = new EnemyPassiveDef { Id = S(p, "id"), N = I(p, "n") };
            }
            data.Enemies.Add(new BattleEnemyDef { Id = S(enemy, "id"), Name = S(enemy, "name"), Tier = S(enemy, "tier"), Hp = I(enemy, "hp"), Brk = I(enemy, "brk"),
                Weak = Strings(enemy.GetProperty("weak")), Passive = passive, Pattern = enemy.GetProperty("pattern").EnumerateArray().Select(Move).ToList() });
        }
        data.Index(); return data;
    }

    static (int Numerator, int Denominator) Ratio(JsonElement json)
    {
        int[] bits = decimal.GetBits(json.GetDecimal());
        BigInteger numerator = (uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
        if (bits[3] < 0) numerator = -numerator;
        BigInteger denominator = BigInteger.Pow(10, (bits[3] >> 16) & 255);
        BigInteger gcd = BigInteger.GreatestCommonDivisor(numerator, denominator); numerator /= gcd; denominator /= gcd;
        if (numerator <= 0 || numerator > int.MaxValue || denominator > int.MaxValue) throw new InvalidDataException("Unsupported upgrade multiplier ratio");
        return ((int)numerator, (int)denominator);
    }
    static BattleUltDef Ult(JsonElement json)
    {
        Fields(json, "name", "eff", "text");
        return new BattleUltDef { Name = S(json, "name"), Text = S(json, "text"), Effects = json.GetProperty("eff").EnumerateArray().Select(Effect).ToList() };
    }
    static BattleCardDef Card(JsonElement json)
    {
        Fields(json, "id", "name", "owner", "rar", "cost", "type", "res", "eff", "kw", "attr", "uses", "growth", "start", "pool", "flavor", "evo", "plus", "costIf");
        var card = new BattleCardDef { Id = S(json, "id"), Name = S(json, "name"), Owner = S(json, "owner"), Rarity = S(json, "rar"), Cost = json.GetProperty("cost").GetInt32(),
            Type = S(json, "type"), Reservation = S(json, "res"), Attr = S(json, "attr"), Effects = json.GetProperty("eff").EnumerateArray().Select(Effect).ToList(),
            Keywords = Strings(json.GetProperty("kw")), Uses = NI(json, "uses"), Growth = NB(json, "growth"), Start = NB(json, "start"), Pool = NB(json, "pool"),
            Flavor = ContentString(json, "flavor"), Evolutions = json.TryGetProperty("evo", out var evo) ? Strings(evo) : null };
        if (json.TryGetProperty("plus", out var plus))
        {
            card.PlusPresent = true;
            if (plus.ValueKind != JsonValueKind.Null)
            {
                Fields(plus, "eff"); card.Plus = new BattleCardPlusDef();
                if (plus.TryGetProperty("eff", out var eff))
                {
                    card.Plus.EffectsPresent = true;
                    if (eff.ValueKind != JsonValueKind.Null) card.Plus.Effects = eff.EnumerateArray().Select(Effect).ToList();
                }
            }
        }
        if (json.TryGetProperty("costIf", out var costIf))
        {
            card.CostIfPresent = true;
            if (costIf.ValueKind != JsonValueKind.Null)
            {
                Fields(costIf, "cond", "delta"); card.CostIf = new BattleCardCostIfDef { Condition = Condition(costIf.GetProperty("cond")), Delta = costIf.GetProperty("delta").GetInt32() };
            }
        }
        return card;
    }
    static BattlePartFx PartFx(JsonElement json)
    {
        Fields(json, "addBps", "allBps", "resBps", "immBps", "afterBps", "cost", "brk", "pierce", "extraBlock", "drawOnce", "gauge", "markOnHit", "burnOnHit", "weakOnHit", "echoCopy", "exhaust", "noReserve");
        return new BattlePartFx { AddBps = NI(json, "addBps"), AllBps = NI(json, "allBps"), ResBps = NI(json, "resBps"), ImmBps = NI(json, "immBps"), AfterBps = NI(json, "afterBps"),
            Cost = NI(json, "cost"), Brk = NI(json, "brk"), Pierce = NI(json, "pierce"), ExtraBlock = NI(json, "extraBlock"), DrawOnce = NI(json, "drawOnce"), Gauge = NI(json, "gauge"),
            MarkOnHit = NI(json, "markOnHit"), BurnOnHit = NI(json, "burnOnHit"), WeakOnHit = NI(json, "weakOnHit"), EchoCopy = NI(json, "echoCopy"), Exhaust = NB(json, "exhaust"), NoReserve = NB(json, "noReserve") };
    }

    static ResonanceFx Fx(JsonElement json)
    {
        Fields(json, "type", "id", "prop", "param", "key", "add", "delta", "n", "bps", "value", "once", "cards", "list", "eff");
        var fx = new ResonanceFx { Type = S(json, "type"), Id = ContentString(json, "id"), Prop = ContentString(json, "prop"), Param = ContentString(json, "param"), Key = ContentString(json, "key"),
            Add = NI(json, "add"), Delta = NI(json, "delta"), N = NI(json, "n"), Bps = NI(json, "bps"),
            Once = json.TryGetProperty("once", out var once) ? once.GetBoolean() : null,
            Cards = json.TryGetProperty("cards", out var cards) ? Strings(cards) : null,
            List = json.TryGetProperty("list", out var list) ? list.EnumerateArray().Select(Fx).ToList() : null,
            Effects = json.TryGetProperty("eff", out var effects) ? effects.EnumerateArray().Select(Effect).ToList() : null };
        if (json.TryGetProperty("value", out var v))
        {
            if (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) fx.BoolValue = v.GetBoolean();
            else fx.IntValue = v.GetInt32();
        }
        return fx;
    }
    static EffectDef Effect(JsonElement json)
    {
        string op = S(json, "op"); ValidateEffectShape(json, op);
        var effect = new EffectDef { Op = op, V = NI(json, "v"), Rv = NI(json, "rv"), Ra = NI(json, "ra"), N = NI(json, "n"), Rn = NI(json, "rn"),
            Hits = NI(json, "hits"), Brk = NI(json, "brk"), Shock = NI(json, "shock"), Pierce = NI(json, "pierce"), Turns = NI(json, "turns"),
            Next = NI(json, "next"), After = NI(json, "after"), Bps = NI(json, "bps"), Block = NI(json, "blk"), Keep = NI(json, "keep"), Heal = NI(json, "heal"), OverflowBlock = NI(json, "overflowBlock"), Base0 = NB(json, "base0"),
            Target = ContentString(json, "tgt"), Status = ContentString(json, "s"), Part = ContentString(json, "part"), Pick = ContentString(json, "pick"), Who = ContentString(json, "who"), Card = ContentString(json, "card"),
            Zone = ContentString(json, "zone"), From = ContentString(json, "from"), Filter = ContentString(json, "filter"), Id = ContentString(json, "id"),
            Then = json.TryGetProperty("then", out var then) ? then.EnumerateArray().Select(Effect).ToList() : null,
            Else = json.TryGetProperty("else", out var other) ? other.EnumerateArray().Select(Effect).ToList() : null };
        if (json.TryGetProperty("scale", out var scale)) effect.Scale = Scale(scale);
        if (json.TryGetProperty("hitsScale", out var hitsScale)) effect.HitsScale = Scale(hitsScale);
        if (json.TryGetProperty("cond", out var cond)) effect.Condition = Condition(cond);
        if (json.TryGetProperty("mult", out var mult)) effect.Multipliers = mult.EnumerateArray().Select(m =>
        { Fields(m, "if", "x"); return new EffectMultiplierDef { Condition = Condition(m.GetProperty("if")), Multiplier = m.GetProperty("x").GetInt32() }; }).ToList();
        return effect;
    }
    static EffectScaleDef Scale(JsonElement json)
    {
        Fields(json, "per", "k", "max");
        string per = S(json, "per");
        if (!new[] { "echo", "exhausted", "debt", "growthSum", "growthCards", "counter", "missingHp10", "contracts", "tempParts", "block", "lastCount", "targetBurn" }.Contains(per, StringComparer.Ordinal))
            throw new InvalidDataException("Unknown scale counter: " + per);
        return new EffectScaleDef { Per = per, K = json.GetProperty("k").GetInt32(), Max = NI(json, "max") };
    }
    static EffectConditionDef Condition(JsonElement json)
    {
        string condition = S(json, "c");
        switch (condition)
        {
            case "echo": case "growthInHand": case "exhaustedAtLeast": Fields(json, "c", "n"); Required(json, "n"); break;
            case "hpBelow": case "targetHpBelow": Fields(json, "c", "pct"); Required(json, "pct"); break;
            case "marked": case "burning": case "broken": case "debt": case "modded": case "reserved": case "intentAttack": case "killedThis": Fields(json, "c"); break;
            default: throw new InvalidDataException("Unknown effect condition: " + condition);
        }
        return new EffectConditionDef { Condition = condition, Pct = NI(json, "pct"), N = NI(json, "n") };
    }
    static void ValidateEffectShape(JsonElement json, string op)
    {
        string[] fields;
        string[] required = Array.Empty<string>();
        switch (op)
        {
            case "dmg": fields = new[] { "v", "rv", "ra", "hits", "hitsScale", "tgt", "brk", "shock", "pierce", "scale", "mult" }; required = new[] { "v" }; break;
            case "blk": fields = new[] { "v", "rv", "ra", "scale", "base0" }; required = new[] { "v" }; break;
            case "heal": fields = new[] { "v", "rv", "ra", "scale", "overflowBlock" }; required = new[] { "v" }; break;
            case "st": fields = new[] { "s", "n", "rn", "tgt" }; required = new[] { "s", "n" }; break;
            case "buff": fields = new[] { "s", "n", "turns" }; required = new[] { "s", "n" }; break;
            case "counter": fields = new[] { "n", "rn" }; required = new[] { "n" }; break;
            case "draw": fields = new[] { "n", "rn", "scale" }; break;
            case "discard": case "ap": case "apNext": case "growPick": case "growLowest": case "loan": case "echo": case "freeNext": fields = new[] { "n" }; required = fields; break;
            case "gauge": fields = new[] { "n", "who" }; required = new[] { "n" }; break;
            case "create": fields = new[] { "card", "n", "zone" }; required = new[] { "card", "n" }; break;
            case "slot": fields = new[] { "next", "after" }; break;
            case "collateral": case "selfdmg": fields = new[] { "v" }; required = fields; break;
            case "cleanseOne": fields = new[] { "s" }; required = fields; break;
            case "exhaustPick": fields = new[] { "from", "n" }; required = new[] { "n" }; break;
            case "tune": fields = new[] { "part", "pick" }; required = new[] { "part" }; break;
            case "costdown": fields = new[] { "n", "pick" }; required = new[] { "n" }; break;
            case "copyPick": fields = new[] { "filter" }; break;
            case "power": fields = new[] { "id", "bps", "blk", "keep", "heal" }; required = new[] { "id" }; break;
            case "nextBoost": fields = new[] { "bps" }; required = fields; break;
            case "breakBonus": case "breakBonusAll": fields = new[] { "n", "scale" }; break;
            case "if": fields = new[] { "cond", "then", "else" }; required = new[] { "cond", "then" }; break;
            case "pull": case "grow": case "defer": case "restraint": case "cleanse": case "exhaustHand": case "recall": case "growAll": case "clearDebt": fields = Array.Empty<string>(); break;
            default: throw new InvalidDataException("Unknown effect op: " + op);
        }
        Fields(json, fields.Concat(new[] { "op" }).ToArray()); Required(json, required);
        if (json.TryGetProperty("tgt", out var target) && !new[] { "one", "same", "all", "rand", "fixed" }.Contains(String(target), StringComparer.Ordinal))
            throw new InvalidDataException("Unknown effect target");
        if (json.TryGetProperty("pick", out var pick) && !new[] { "one", "all", "same", "random" }.Contains(String(pick), StringComparer.Ordinal))
            throw new InvalidDataException("Unknown effect pick");
        if (op == "power" && !new[] { "blueprint", "greenhouse", "bulwark", "rebirth", "oblivion", "casefile", "echochamber" }.Contains(S(json, "id"), StringComparer.Ordinal))
            throw new InvalidDataException("Unknown power ID");
    }
    static void Required(JsonElement json, params string[] names)
    {
        foreach (string name in names) if (!json.TryGetProperty(name, out _)) throw new InvalidDataException("Missing required field: " + name);
    }
    static EnemyMoveDef Move(JsonElement json)
    {
        Fields(json, "t", "card", "s", "who", "label", "e", "d", "h", "str", "b", "n", "then");
        return new EnemyMoveDef { Type = S(json, "t"), Card = O(json, "card"), Status = O(json, "s"), Who = O(json, "who"), Label = O(json, "label"), Enemy = O(json, "e"),
            Damage = NI(json, "d"), Hits = NI(json, "h"), Strength = NI(json, "str"), Block = NI(json, "b"), N = NI(json, "n"),
            Then = json.TryGetProperty("then", out var then) ? Move(then) : null };
    }

    public static BattleSetup Setup(JsonElement json)
    {
        var setup = new BattleSetup { Hp = json.GetProperty("hp").GetInt32(), MaxHp = json.GetProperty("maxHp").GetInt32(), NextApBonus = I(json, "nextApBonus"),
            Seed = json.TryGetProperty("seed", out var seed) && seed.ValueKind != JsonValueKind.Null ? seed.GetUInt32() : null,
            Relics = json.TryGetProperty("relics", out var relics) ? Strings(relics) : new List<string>(), Enemies = Strings(json.GetProperty("enemies")),
            RunFlags = json.TryGetProperty("runFlags", out var flags) ? (Dictionary<string, object?>)Graph(flags)! : new Dictionary<string, object?>(StringComparer.Ordinal) };
        if (json.TryGetProperty("difficulty", out var diff) && diff.ValueKind != JsonValueKind.Null)
        { setup.DifficultyHp = I(diff, "hp"); setup.DifficultyDamage = I(diff, "dmg"); }
        foreach (var actor in json.GetProperty("party").EnumerateArray())
            setup.Party.Add(new BattleActorSetup { CharId = S(actor, "charId"), WeaponId = O(actor, "weaponId"), Res = I(actor, "res"), Refine = I(actor, "refine"), Gauge = I(actor, "gauge"),
                Power4 = I(actor, "power4"), Hp = I(actor, "hp"), GearBps = I(actor, "gearBps"), SkillLv = I(actor, "skillLv"), UltLv = I(actor, "ultLv") });
        foreach (var card in json.GetProperty("deck").EnumerateArray())
        {
            var spec = new BattleCardSpec { Id = S(card, "id"), Wielder = O(card, "wielder"), Plus = B(card, "plus"), Growth = I(card, "growth"), Assigned = O(card, "assigned"), From = O(card, "from") };
            if (card.TryGetProperty("mods", out var mods) && mods.ValueKind != JsonValueKind.Null) { spec.Circuit = O(mods, "c"); spec.Edge = O(mods, "e"); }
            setup.Deck.Add(spec); // Battle.create ignores deck.key; a later Run bridge owns it.
        }
        return setup;
    }

    public static BattleRequest Request(string json)
    {
        using var document = JsonDocument.Parse(json); return Request(document.RootElement);
    }
    public static BattleRequest Request(JsonElement json)
    {
        Fields(json, "rid", "v", "kind", "args");
        return new BattleRequest(O(json, "rid"), json.GetProperty("v").GetInt32(), S(json, "kind"), json.TryGetProperty("args", out var args) ? Argument(args) : null);
    }
    static BattleArgument Argument(JsonElement json)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.Object:
                return BattleArgument.Object(json.EnumerateObject().Select(p => new KeyValuePair<string, BattleArgument>(Name(p), Argument(p.Value))));
            case JsonValueKind.Array: return BattleArgument.Array(json.EnumerateArray().Select(Argument));
            case JsonValueKind.String: return BattleArgument.String(DecodeString(json.GetRawText()));
            case JsonValueKind.True: return BattleArgument.Boolean(true);
            case JsonValueKind.False: return BattleArgument.Boolean(false);
            case JsonValueKind.Null: return BattleArgument.Null();
            case JsonValueKind.Number:
                if (json.TryGetDecimal(out decimal number) && decimal.Truncate(number) == number && number >= -CanonicalState.MaxSafeInteger && number <= CanonicalState.MaxSafeInteger)
                    return BattleArgument.Integer((long)number);
                throw new RuleException("UNSUPPORTED_REQUEST_NUMBER");
            default: throw new RuleException("INVALID_REQUEST_VALUE");
        }
    }

    // This reader is for integer snapshots/flags, never the full content document.
    public static object? Graph(JsonElement json)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.Object:
                var graph = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var property in json.EnumerateObject()) graph.Add(Name(property), Graph(property.Value));
                return graph;
            case JsonValueKind.Array: return json.EnumerateArray().Select(Graph).ToList();
            case JsonValueKind.String: return DecodeString(json.GetRawText());
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Null: return null;
            case JsonValueKind.Number:
                if (json.TryGetInt64(out long value) && value >= -CanonicalState.MaxSafeInteger && value <= CanonicalState.MaxSafeInteger) return value;
                throw new InvalidDataException("Snapshot numbers must be JS-safe integers");
            default: throw new InvalidDataException("Unsupported snapshot token");
        }
    }
    internal static string Name(JsonProperty property)
    {
        try { return property.Name; }
        catch (InvalidOperationException)
        {
            // JsonProperty.Name rejects lone surrogates; raw validated JSON retains them.
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("{" + property.ToString() + "}"));
            if (!reader.Read() || !reader.Read() || reader.TokenType != JsonTokenType.PropertyName)
                throw new InvalidDataException("Expected JSON property name");
            return DecodeString("\"" + Encoding.UTF8.GetString(reader.ValueSpan) + "\"");
        }
    }
    static string String(JsonElement json) => json.ValueKind == JsonValueKind.String
        ? DecodeString(json.GetRawText()) : throw new InvalidDataException("Expected JSON string");
    static string DecodeString(string raw)
    {
        var result = new StringBuilder(raw.Length - 2);
        for (int i = 1; i < raw.Length - 1; i++)
        {
            char c = raw[i];
            if (c != '\\') { result.Append(c); continue; }
            switch (raw[++i])
            {
                case '"': result.Append('"'); break; case '\\': result.Append('\\'); break; case '/': result.Append('/'); break;
                case 'b': result.Append('\b'); break; case 'f': result.Append('\f'); break; case 'n': result.Append('\n'); break;
                case 'r': result.Append('\r'); break; case 't': result.Append('\t'); break;
                case 'u': result.Append((char)ushort.Parse(raw.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                default: throw new InvalidDataException("Unsupported JSON escape");
            }
        }
        return result.ToString();
    }
    internal static void Fields(JsonElement json, params string[] names)
    {
        var allowed = new HashSet<string>(names, StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in json.EnumerateObject())
            if (!allowed.Contains(field.Name) || !seen.Add(field.Name)) throw new InvalidDataException("Unsupported or duplicate field: " + field.Name);
    }
    static string S(JsonElement json, string name) => String(json.GetProperty(name));
    static string? O(JsonElement json, string name) => json.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? String(value) : null;
    static int I(JsonElement json, string name) => json.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetInt32() : 0;
    static int? NI(JsonElement json, string name) => json.TryGetProperty(name, out var value) ? value.GetInt32() : null;
    static bool? NB(JsonElement json, string name) => json.TryGetProperty(name, out var value) ? value.GetBoolean() : null;
    static string? ContentString(JsonElement json, string name) => json.TryGetProperty(name, out var value) ? String(value) : null;
    static bool B(JsonElement json, string name) => json.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null && value.GetBoolean();
    static List<int> Ints(JsonElement json) => json.EnumerateArray().Select(n => n.GetInt32()).ToList();
    static List<string> Strings(JsonElement json) => json.EnumerateArray().Select(s => s.GetString()!).ToList();
}
