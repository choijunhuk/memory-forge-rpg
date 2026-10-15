// Host-only JSON adapter. Core has typed definitions and no JSON/package dependency.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        // Read the actual opening fields. Unused authored decimals (e.g. upgradeMult=1.3)
        // never pass through the deliberately integer-only snapshot/transport readers.
        var data = new BattleData { Rules = new BattleRules { ApPerTurn = I(rules, "apPerTurn"), ApMax = I(rules, "apMax"),
            HandDraw = I(rules, "handDraw"), HandMax = I(rules, "handMax"), DebtCap = I(rules, "debtCap"),
            GrowthCapPerBattle = I(rules, "growthCapPerBattle"), GaugeMax = I(rules, "gaugeMax"), WeakBps = I(rules, "weakBps"),
            NextSlots = I(slots, "next"), AfterSlots = I(slots, "after"), NextSlotsMax = I(slots, "nextMax"), AfterSlotsMax = I(slots, "afterMax") } };
        foreach (var ch in json.GetProperty("characters").EnumerateArray())
        {
            var p = ch.GetProperty("passive"); Fields(p, "id", "name", "params", "text");
            data.Characters.Add(new BattleCharacterDef { Id = S(ch, "id"), Name = S(ch, "name"), Attr = S(ch, "attr"),
                Passive = new PassiveDef { Id = S(p, "id"), Name = S(p, "name"), Text = S(p, "text"),
                    Params = p.GetProperty("params").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetInt32(), StringComparer.Ordinal) },
                Resonance = ch.GetProperty("resonance").EnumerateArray().Select(n => Fx(n.GetProperty("fx"))).ToList() });
        }
        foreach (var c in json.GetProperty("cards").EnumerateArray())
            data.Cards.Add(new BattleCardDef { Id = S(c, "id"), Owner = S(c, "owner"), Rarity = S(c, "rar"), Growth = B(c, "growth") });
        foreach (var w in json.GetProperty("weapons").EnumerateArray())
        {
            var p = w.GetProperty("passive"); Fields(p, "id", "name", "scope", "text", "values", "refineAlt");
            data.Weapons.Add(new BattleWeaponDef { Id = S(w, "id"), Passive = new WeaponPassiveDef { Id = S(p, "id"), Name = S(p, "name"), Scope = S(p, "scope"), Text = S(p, "text"),
                Values = Ints(p.GetProperty("values")), RefineAlt = p.TryGetProperty("refineAlt", out var alt) ? Ints(alt) : null } });
        }
        foreach (var part in json.GetProperty("parts").EnumerateArray()) data.Parts.Add(new BattlePartDef { Id = S(part, "id") });
        foreach (var relic in json.GetProperty("relics").EnumerateArray()) data.Relics.Add(new BattleRelicDef { Id = S(relic, "id") });
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

    static ResonanceFx Fx(JsonElement json)
    {
        Fields(json, "type", "id", "prop", "param", "key", "add", "delta", "n", "bps", "value", "once", "cards", "list", "eff");
        var fx = new ResonanceFx { Type = S(json, "type"), Id = O(json, "id"), Prop = O(json, "prop"), Param = O(json, "param"), Key = O(json, "key"),
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
        Fields(json, "op", "v", "n", "hits", "brk", "turns", "tgt", "s", "part", "pick", "scale", "cond", "then", "else");
        var effect = new EffectDef { Op = S(json, "op"), V = NI(json, "v"), N = NI(json, "n"), Hits = NI(json, "hits"), Brk = NI(json, "brk"), Turns = NI(json, "turns"),
            Target = O(json, "tgt"), Status = O(json, "s"), Part = O(json, "part"), Pick = O(json, "pick"),
            Then = json.TryGetProperty("then", out var then) ? then.EnumerateArray().Select(Effect).ToList() : null,
            Else = json.TryGetProperty("else", out var other) ? other.EnumerateArray().Select(Effect).ToList() : null };
        if (json.TryGetProperty("scale", out var scale))
        {
            Fields(scale, "per", "k", "max"); effect.Scale = new EffectScaleDef { Per = S(scale, "per"), K = I(scale, "k"), Max = NI(scale, "max") };
        }
        if (json.TryGetProperty("cond", out var cond))
        {
            Fields(cond, "c", "pct"); effect.Condition = new EffectConditionDef { Condition = S(cond, "c"), Pct = NI(cond, "pct") };
        }
        return effect;
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
    static string Name(JsonProperty property)
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
    static void Fields(JsonElement json, params string[] names)
    {
        var allowed = new HashSet<string>(names, StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in json.EnumerateObject())
            if (!allowed.Contains(field.Name) || !seen.Add(field.Name)) throw new InvalidDataException("Unsupported or duplicate field: " + field.Name);
    }
    static string S(JsonElement json, string name) => String(json.GetProperty(name));
    static string? O(JsonElement json, string name) => json.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? String(value) : null;
    static int I(JsonElement json, string name) => json.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetInt32() : 0;
    static int? NI(JsonElement json, string name) => json.TryGetProperty(name, out var value) ? value.GetInt32() : null;
    static bool B(JsonElement json, string name) => json.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null && value.GetBoolean();
    static List<int> Ints(JsonElement json) => json.EnumerateArray().Select(n => n.GetInt32()).ToList();
    static List<string> Strings(JsonElement json) => json.EnumerateArray().Select(s => s.GetString()!).ToList();
}
