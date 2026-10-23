// §8.9-14 / M0: pure stable-identity memory rules, never Run execution or persistent storage.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MemoryForge.Core
{
    public sealed class MemoryCostConfig
    {
        public int AddB, AddA, AddS, AddCommon, Upgrade, PartCommon, PartRare, Evolution, Remove;
    }
    public sealed class MemoryAscensionCapacity { public int Asc, Add; }
    public sealed class MemoryCapacityEntry { public string CharId = ""; public int Res, Asc; }
    public sealed class MemoryRulesData
    {
        public BattleData Battle = new BattleData();
        public Dictionary<string, string> StartingCommon = new Dictionary<string, string>(StringComparer.Ordinal);
        public MemoryCostConfig Costs = new MemoryCostConfig();
        public int CapacityBase;
        public List<MemoryAscensionCapacity> AscensionCapacity = new List<MemoryAscensionCapacity>();
    }
    public sealed class MemoryBoundaryException : Exception
    {
        public string JavaScriptName { get; }
        public MemoryBoundaryException(string name, string message) : base(message) { JavaScriptName = name; }
    }
    public sealed class MemoryCardSpec
    {
        internal readonly IDictionary<string, object?> Fields;
        internal MemoryCardSpec(IDictionary<string, object?> fields) { Fields = fields; }
        public string Key => (string)Fields["key"]!;
        public string? Id => MemoryJson.Get(Fields, "id") as string;
        public Dictionary<string, object?> ToJsonGraph() => (Dictionary<string, object?>)MemoryJson.Copy(Fields)!;
    }
    public sealed class MemorySnapshot
    {
        internal readonly List<string> Ids = new List<string>();
        internal readonly Dictionary<string, List<MemoryCardSpec>> Rows = new Dictionary<string, List<MemoryCardSpec>>(StringComparer.Ordinal);
        internal void Add(string cid, List<MemoryCardSpec> cards) { Ids.Add(cid); Rows.Add(cid, cards); }
        public IReadOnlyList<string> CharacterIds => Ids.AsReadOnly();
        public IReadOnlyList<MemoryCardSpec> Cards(string cid) => Rows[cid].AsReadOnly();
        internal MemorySnapshot Copy()
        {
            var copy = new MemorySnapshot();
            foreach (string cid in Ids) copy.Add(cid, Rows[cid].Select(c => new MemoryCardSpec(c.ToJsonGraph())).ToList());
            return copy;
        }
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (string cid in Ids) graph.Add(cid, Rows[cid].Select(c => (object?)c.ToJsonGraph()).ToList());
            return graph;
        }
    }
    public sealed class MemoryInspection
    {
        readonly Dictionary<string, object?> definitions;
        public MemorySnapshot Snapshot { get; }
        internal MemoryInspection(MemoryRulesData data, MemorySnapshot snapshot)
        {
            Snapshot = snapshot.Copy();
            definitions = MemoryJson.Object("cards", data.Battle.Cards.ToDictionary(c => c.Id, c => (object?)c.ToJsonGraph(), StringComparer.Ordinal),
                "parts", data.Battle.Parts.ToDictionary(p => p.Id, p => (object?)p.ToJsonGraph(), StringComparer.Ordinal));
        }
        public Dictionary<string, object?> ToJsonGraph() => (Dictionary<string, object?>)MemoryJson.Copy(definitions)!;
    }
    public sealed class MemoryChange
    {
        public string Id = "", Cid = "", Key = "", Kind = "";
        public object? Value;
        public bool ValuePresent;
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = MemoryJson.Object("id", Id, "cid", Cid, "key", Key, "kind", Kind);
            if (ValuePresent) graph.Add("value", MemoryJson.Copy(Value));
            return graph;
        }
    }
    public sealed class MemoryApplyResult
    {
        public MemorySnapshot Memory { get; }
        public Dictionary<string, long> Used { get; }
        public string? Error { get; }
        internal MemoryApplyResult(MemorySnapshot memory, Dictionary<string, long> used, string? error) { Memory = memory; Used = used; Error = error; }
        public Dictionary<string, object?> ToJsonGraph() => MemoryJson.Object("memory", Memory.ToJsonGraph(), "used", Used.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal), "error", Error);
    }
    public sealed class MemoryRecoveryResult
    {
        public MemorySnapshot Memory { get; }
        public Dictionary<string, long> Used { get; }
        internal MemoryRecoveryResult(MemoryApplyResult result) { Memory = result.Memory; Used = result.Used; }
        public Dictionary<string, object?> ToJsonGraph() => MemoryJson.Object("memory", Memory.ToJsonGraph(), "used", Used.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal));
    }
    public sealed class MemoryQuarantine
    {
        public string Code { get; }
        readonly object? original;
        internal MemoryQuarantine(string code, object? value) { Code = code; original = MemoryJson.Copy(value); }
        public object? Original => MemoryJson.Copy(original);
        internal Dictionary<string, object?> ToJsonGraph() => MemoryJson.Object("code", Code, "original", Original);
    }
    public sealed class MemoryMigrationResult
    {
        public MemorySnapshot Memory { get; } = new MemorySnapshot();
        public Dictionary<string, MemoryQuarantine> Quarantine { get; } = new Dictionary<string, MemoryQuarantine>(StringComparer.Ordinal);
        readonly object? legacyBackup;
        internal MemoryMigrationResult(object? legacy) { legacyBackup = MemoryJson.Copy(legacy); }
        public object? LegacyBackup => MemoryJson.Copy(legacyBackup);
        public Dictionary<string, object?> ToJsonGraph() => MemoryJson.Object("schema", "mf-memory-v2", "version", 0,
            "memory", Memory.ToJsonGraph(), "quarantine", Quarantine.ToDictionary(p => p.Key, p => (object?)p.Value.ToJsonGraph(), StringComparer.Ordinal),
            "legacyBackup", LegacyBackup, "history", new List<object?>(), "receipts", MemoryJson.Object());
    }

    public static class MemoryV2
    {
        public static MemoryInspection Inspect(MemoryRulesData data, object? snapshot)
            => new MemoryInspection(data, Validate(data, snapshot));
        static MemorySnapshot Validate(MemoryRulesData data, object? snapshot)
        {
            // Validate opaque metadata as JSON too; it is never routed through integer-only combat graphs.
            MemoryJson.Copy(snapshot);
            if (!(snapshot is IDictionary<string, object?> source)) throw new RuleException("INVALID_SNAPSHOT");
            var result = new MemorySnapshot(); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in MemoryJson.Entries(source))
            {
                string cid = pair.Key;
                if (!data.Battle.Characters.Any(c => c.Id == cid) || !(pair.Value is IList rows)) throw new RuleException("INVALID_SNAPSHOT");
                var cards = new List<MemoryCardSpec>();
                foreach (object? raw in rows)
                {
                    if (raw == null || !(raw is IDictionary<string, object?>) && !(raw is IList)) throw new RuleException("INVALID_CARD");
                    object? key = MemoryJson.Get(raw, "key");
                    if (!(key is string text) || text.Length == 0 || !seen.Add(text)) throw new RuleException("IDENTITY_REQUIRED");
                    var fields = (IDictionary<string, object?>)raw;
                    var definition = Card(data, MemoryJson.Get(fields, "id"));
                    if (definition == null || definition.Owner != "common" && definition.Owner != cid || MemoryJson.Truthy(MemoryJson.Get(fields, "token")) || MemoryJson.Truthy(MemoryJson.Get(fields, "weapon"))) throw new RuleException("INVALID_CARD");
                    if (definition.Owner == "common" && !MemoryJson.Equal(MemoryJson.Get(fields, "assigned"), cid)) throw new RuleException("INVALID_ASSIGNMENT");
                    object? from = MemoryJson.Get(fields, "from");
                    if (MemoryJson.Truthy(from))
                    {
                        var origin = Card(data, from);
                        if (origin?.Growth != true || origin.Owner != cid || origin.Evolutions?.Contains(definition.Id) != true) throw new RuleException("INVALID_EVOLUTION");
                    }
                    if (data.Battle.Cards.Any(c => c.Evolutions?.Contains(definition.Id) == true) && !MemoryJson.Truthy(from)) throw new RuleException("INVALID_EVOLUTION");
                    object? plus = MemoryJson.Get(fields, "plus");
                    if (!ReferenceEquals(plus, MemoryJson.Missing) && !(plus is bool)) throw new RuleException("INVALID_UPGRADE");
                    object? mods = MemoryJson.Get(fields, "mods");
                    if (mods != null && !ReferenceEquals(mods, MemoryJson.Missing) && (!(mods is IDictionary<string, object?> map) || map.Keys.Any(k => k != "c" && k != "e"))) throw new RuleException("INVALID_PART");
                    foreach (string slot in new[] { "c", "e" })
                    {
                        object? id = MemoryJson.Get(mods, slot);
                        if (!MemoryJson.Truthy(id)) continue;
                        var part = Part(data, id);
                        if (part == null || part.Slot != (slot == "c" ? "circuit" : "engraving")) throw new RuleException("INVALID_PART");
                    }
                    cards.Add(new MemoryCardSpec(fields));
                }
                result.Add(cid, cards);
            }
            return result;
        }

        public static List<MemoryCardSpec> Baseline(MemoryRulesData data, string cid)
        {
            var rows = data.Battle.Cards.Where(c => c.Owner == cid && c.Start == true).Select(c => MemoryJson.Object("id", c.Id)).ToList();
            var common = MemoryJson.Object();
            if (data.StartingCommon.TryGetValue(cid, out string id)) common.Add("id", id);
            common.Add("assigned", cid); rows.Add(common);
            for (int i = 0; i < rows.Count; i++) rows[i].Add("key", "base:" + cid + ":" + i.ToString(CultureInfo.InvariantCulture));
            return rows.Select(r => new MemoryCardSpec(r)).ToList();
        }

        public static List<MemoryChange> ListChanges(MemoryRulesData data, object? before, object? after)
        {
            var old = Validate(data, before); var now = Validate(data, after);
            var ids = old.Ids.OrderBy(id => id, StringComparer.Ordinal).ToList();
            if (!ids.SequenceEqual(now.Ids.OrderBy(id => id, StringComparer.Ordinal))) throw new RuleException("CHARACTER_SET_MISMATCH");
            var owners = old.Ids.SelectMany(cid => old.Rows[cid].Select(c => new { c.Key, Cid = cid })).ToDictionary(x => x.Key, x => x.Cid, StringComparer.Ordinal);
            foreach (string cid in now.Ids) foreach (var card in now.Rows[cid])
                if (owners.TryGetValue(card.Key, out string owner) && owner != cid) throw new RuleException("REASSIGNMENT_REQUIRES_SEPARATE_FLOW");
            var result = new List<MemoryChange>();
            void Add(string cid, string key, string kind, object? value = null, bool present = true)
            {
                result.Add(new MemoryChange { Id = "delta:" + UriComponent(cid) + ":" + UriComponent(key) + ":" + kind + (kind == "part" ? ":" + MemoryJson.Get(value, "slot") : ""),
                    Cid = cid, Key = key, Kind = kind, Value = MemoryJson.Copy(value), ValuePresent = present });
            }
            foreach (string cid in ids)
            {
                var current = now.Rows[cid].ToDictionary(c => c.Key, StringComparer.Ordinal);
                var previous = old.Rows[cid].ToDictionary(c => c.Key, StringComparer.Ordinal);
                foreach (var a in old.Rows[cid])
                {
                    if (!current.TryGetValue(a.Key, out var b)) { Add(cid, a.Key, "remove", present: false); continue; }
                    if (!MemoryJson.Equal(MemoryJson.Get(a.Fields, "assigned"), MemoryJson.Get(b.Fields, "assigned"))) throw new RuleException("REASSIGNMENT_REQUIRES_SEPARATE_FLOW");
                    object? af = MemoryJson.Get(a.Fields, "from"), bf = MemoryJson.Get(b.Fields, "from");
                    if (!MemoryJson.Equal(MemoryJson.Get(a.Fields, "id"), MemoryJson.Get(b.Fields, "id")) || !MemoryJson.Equal(af, bf))
                    {
                        var origin = Card(data, MemoryJson.Truthy(af) ? af : MemoryJson.Get(a.Fields, "id"));
                        if (origin?.Growth != true || MemoryJson.Truthy(af) || !MemoryJson.Equal(bf, MemoryJson.Get(a.Fields, "id")) || origin.Evolutions?.Contains(b.Id ?? "") != true) throw new RuleException("UNSUPPORTED_TRANSFORMATION");
                        Add(cid, a.Key, "evolve", MemoryJson.Object("id", b.Id, "from", bf));
                    }
                    bool ap = MemoryJson.Truthy(MemoryJson.Get(a.Fields, "plus")), bp = MemoryJson.Truthy(MemoryJson.Get(b.Fields, "plus"));
                    if (ap != bp) Add(cid, a.Key, "upgrade", bp);
                    foreach (string slot in new[] { "c", "e" })
                    {
                        object? av = MemoryJson.Get(MemoryJson.Get(a.Fields, "mods"), slot), bv = MemoryJson.Get(MemoryJson.Get(b.Fields, "mods"), slot);
                        av = MemoryJson.Truthy(av) ? av : null; bv = MemoryJson.Truthy(bv) ? bv : null;
                        if (!MemoryJson.Equal(av, bv)) Add(cid, a.Key, "part", MemoryJson.Object("slot", slot, "part", bv));
                    }
                }
                foreach (var b in now.Rows[cid]) if (!previous.ContainsKey(b.Key)) Add(cid, b.Key, "add", b.ToJsonGraph());
            }
            return result;
        }

        public static long Cost(MemoryRulesData data, string cid, object? specs, object? baseSpecs = null)
        {
            var rows = Validate(data, MemoryJson.Object(cid, specs)).Rows[cid];
            var pool = new List<MemoryCardSpec>(rows); var costs = data.Costs;
            var baseline = MemoryJson.Truthy(baseSpecs) ? MemoryJson.Array(baseSpecs) : Baseline(data, cid).Select(c => (object?)c.ToJsonGraph()).ToList();
            long total = 0;
            foreach (object? raw in baseline)
            {
                int index = pool.FindIndex(c =>
                {
                    MemoryJson.RequirePropertyTarget(raw);
                    return MemoryJson.Truthy(baseSpecs)
                        ? MemoryJson.Equal(MemoryJson.Get(c.Fields, "key"), MemoryJson.Get(raw, "key"))
                        : MemoryJson.Equal(MemoryJson.Truthy(MemoryJson.Get(c.Fields, "from")) ? MemoryJson.Get(c.Fields, "from") : MemoryJson.Get(c.Fields, "id"), MemoryJson.Get(raw, "id"));
                });
                if (index < 0) total = checked(total + costs.Remove);
                else { total = checked(total + TraitCost(data, pool[index])); pool.RemoveAt(index); }
            }
            foreach (var card in pool)
            {
                object? from = MemoryJson.Get(card.Fields, "from"); var definition = Card(data, MemoryJson.Truthy(from) ? from : card.Id)!;
                int add = definition.Owner == "common" ? costs.AddCommon : definition.Rarity == "B" ? costs.AddB : definition.Rarity == "A" ? costs.AddA : definition.Rarity == "S" ? costs.AddS : costs.AddA;
                total = checked(total + add + TraitCost(data, card));
            }
            return total;
        }
        static long TraitCost(MemoryRulesData data, MemoryCardSpec card)
        {
            long total = MemoryJson.Truthy(MemoryJson.Get(card.Fields, "plus")) ? data.Costs.Upgrade : 0;
            if (MemoryJson.Truthy(MemoryJson.Get(card.Fields, "from"))) total += data.Costs.Evolution;
            foreach (string slot in new[] { "c", "e" })
            {
                object? id = MemoryJson.Get(MemoryJson.Get(card.Fields, "mods"), slot);
                if (MemoryJson.Truthy(id)) total += Part(data, id)!.Rarity == "rare" ? data.Costs.PartRare : data.Costs.PartCommon;
            }
            return total;
        }

        public static object? ApplyChanges(object? before, object? changes)
        {
            object? output = MemoryJson.Copy(before);
            foreach (object? raw in MemoryJson.Array(changes))
            {
                MemoryJson.RequirePropertyTarget(raw); string cid = MemoryJson.Get(raw, "cid") as string ?? "";
                var rows = MemoryJson.Array(MemoryJson.Get(output, cid));
                object? key = MemoryJson.Get(raw, "key"); int index = -1;
                for (int i = 0; i < rows.Count; i++) if (MemoryJson.Equal(MemoryJson.Get(rows[i], "key"), key)) { index = i; break; }
                string? kind = MemoryJson.Get(raw, "kind") as string; object? value = MemoryJson.Get(raw, "value");
                if (kind == "add")
                {
                    if (ReferenceEquals(value, MemoryJson.Missing)) throw new MemoryBoundaryException("SyntaxError", "Value is not JSON");
                    rows.Add(MemoryJson.Copy(value));
                }
                else if (kind == "remove") { if (rows.Count > 0) rows.RemoveAt(index < 0 ? rows.Count - 1 : index); }
                else if (kind == "evolve" || kind == "upgrade" || kind == "part")
                {
                    if (index < 0 || !(rows[index] is IDictionary<string, object?> card)) throw new MemoryBoundaryException("TypeError", "Missing memory card");
                    if (kind == "evolve") { MemoryJson.RequirePropertyTarget(value); SetOptional(card, "id", MemoryJson.Get(value, "id")); SetOptional(card, "from", MemoryJson.Get(value, "from")); }
                    if (kind == "upgrade") SetOptional(card, "plus", value);
                    if (kind == "part")
                    {
                        MemoryJson.RequirePropertyTarget(value);
                        var mods = MemoryJson.Get(card, "mods") is IDictionary<string, object?> old ? (Dictionary<string, object?>)MemoryJson.Copy(old)! : MemoryJson.Object();
                        SetOptional(mods, MemoryJson.Get(value, "slot") as string ?? "undefined", MemoryJson.Get(value, "part"));
                        card["mods"] = mods;
                    }
                }
            }
            return output;
        }
        static void SetOptional(IDictionary<string, object?> card, string key, object? value)
        {
            if (ReferenceEquals(value, MemoryJson.Missing)) card.Remove(key); else card[key] = MemoryJson.Copy(value);
        }

        public static MemoryApplyResult Apply(MemoryRulesData data, object? before, object? after, object? selectedIds, object? capacities)
            => Apply(data, before, after, selectedIds, capacities, MemoryJson.Object());
        public static MemoryApplyResult Apply(MemoryRulesData data, object? before, object? after, object? selectedIds, object? capacities, object? options)
        {
            if (!(selectedIds is IList selected) || selected.Cast<object?>().Any(id => !(id is string)) || selected.Cast<string>().Distinct(StringComparer.Ordinal).Count() != selected.Count) throw new RuleException("INVALID_SELECTION");
            var changes = ListChanges(data, before, after); var chosen = new List<object?>();
            foreach (string id in selected)
            {
                var change = changes.FirstOrDefault(c => c.Id == id); if (change == null) throw new RuleException("INVALID_SELECTION");
                chosen.Add(change.ToJsonGraph());
            }
            var memory = Validate(data, ApplyChanges(before, chosen));
            MemoryJson.RequirePropertyTarget(options);
            string? error = MemoryJson.Truthy(MemoryJson.Get(options, "defeat")) && selected.Count > 1 ? "DEFEAT_KEEP_ONE" : null;
            var used = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (string cid in memory.Ids)
            {
                object? baseline = MemoryJson.Get(MemoryJson.Get(options, "baseline"), cid);
                long cost = Cost(data, cid, memory.Rows[cid].Select(c => (object?)c.ToJsonGraph()).ToList(), ReferenceEquals(baseline, MemoryJson.Missing) ? null : baseline);
                used.Add(cid, cost); MemoryJson.RequirePropertyTarget(capacities); object? cap = MemoryJson.Get(capacities, cid);
                if (!MemoryJson.Integer(cap) || cost > MemoryJson.Number(cap)) error = error ?? "OVER_CAPACITY";
            }
            if (error != null && !MemoryJson.Truthy(MemoryJson.Get(options, "preview"))) throw new RuleException(error);
            return new MemoryApplyResult(memory, used, error);
        }
        public static MemoryRecoveryResult Recover(MemoryRulesData data, object? before, object? after, object? selectedIds, object? capacities)
        {
            if (!(selectedIds is IList selected) || selected.Count > 1) throw new RuleException("DEFEAT_KEEP_ONE");
            return new MemoryRecoveryResult(Apply(data, before, after, selectedIds, capacities, MemoryJson.Object("defeat", true)));
        }

        public static MemoryMigrationResult Migrate(MemoryRulesData data, object? legacy, object? capacities)
        {
            if (!(legacy is IDictionary<string, object?> source)) throw new RuleException("INVALID_SNAPSHOT");
            var state = new MemoryMigrationResult(legacy); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string cid in source.Keys.OrderBy(id => id, StringComparer.Ordinal))
            {
                try
                {
                    if (!data.Battle.Characters.Any(c => c.Id == cid) || !(source[cid] is IList rawRows)) throw new RuleException("INVALID_SNAPSHOT");
                    var remaining = Baseline(data, cid); var rows = new List<object?>();
                    for (int i = 0; i < rawRows.Count; i++)
                    {
                        object? raw = rawRows[i]; var value = raw is string id ? MemoryJson.Object("id", id) : MemoryJson.Copy(raw);
                        // JS maps an array row as a keyed object so Validate reports rows in source order; it can never save, and quarantine keeps source[cid].
                        if (value is IList) value = MemoryJson.Object();
                        if (!(value is IDictionary<string, object?> card)) throw new RuleException("INVALID_CARD");
                        object? from = MemoryJson.Get(card, "from"), currentId = MemoryJson.Get(card, "id");
                        int pos = remaining.FindIndex(b => MemoryJson.Equal(b.Id, MemoryJson.Truthy(from) ? from : currentId));
                        MemoryCardSpec? original = pos < 0 ? null : remaining[pos]; if (pos >= 0) remaining.RemoveAt(pos);
                        if (!MemoryJson.Truthy(MemoryJson.Get(card, "key"))) card["key"] = original?.Key ?? ("legacy:" + cid + ":" + i.ToString(CultureInfo.InvariantCulture));
                        if (Card(data, currentId)?.Owner == "common" && !MemoryJson.Truthy(MemoryJson.Get(card, "assigned"))) card["assigned"] = cid;
                        rows.Add(card);
                    }
                    var inspected = Validate(data, MemoryJson.Object(cid, rows)).Rows[cid];
                    if (inspected.Any(c => seen.Contains(c.Key))) throw new RuleException("IDENTITY_REQUIRED");
                    MemoryJson.RequirePropertyTarget(capacities); object? cap = MemoryJson.Get(capacities, cid);
                    if (!MemoryJson.Integer(cap) || Cost(data, cid, rows, Baseline(data, cid).Select(c => (object?)c.ToJsonGraph()).ToList()) > MemoryJson.Number(cap)) throw new RuleException("OVER_CAPACITY");
                    state.Memory.Add(cid, inspected); foreach (var card in inspected) seen.Add(card.Key);
                }
                catch (RuleException error) { state.Quarantine.Add(cid, new MemoryQuarantine(error.Code, source[cid])); }
                catch (MemoryBoundaryException) { state.Quarantine.Add(cid, new MemoryQuarantine("INVALID_SNAPSHOT", source[cid])); }
            }
            return state;
        }

        public static int CapacityOf(MemoryRulesData data, MemoryCapacityEntry entry)
        {
            var character = data.Battle.Characters.FirstOrDefault(c => c.Id == entry.CharId);
            if (character == null) throw new MemoryBoundaryException("TypeError", "Unknown capacity character");
            int result = data.CapacityBase;
            foreach (var rule in data.AscensionCapacity) if (entry.Asc >= rule.Asc) result = checked(result + rule.Add);
            int count = entry.Res >= 0 ? Math.Min(character.Resonance.Count, entry.Res) : (int)Math.Max(0L, character.Resonance.Count + (long)entry.Res);
            foreach (var fx in character.Resonance.Take(count))
                foreach (var effect in fx.Type == "multi" ? fx.List! : new List<ResonanceFx> { fx })
                    if (effect.Type == "capacity") result = checked(result + effect.N.GetValueOrDefault());
            return result;
        }
        static BattleCardDef? Card(MemoryRulesData data, object? id) => id is string text ? data.Battle.Cards.FirstOrDefault(c => c.Id == text) : null;
        static BattlePartDef? Part(MemoryRulesData data, object? id) => id is string text ? data.Battle.Parts.FirstOrDefault(p => p.Id == text) : null;
        static string UriComponent(string value)
        {
            byte[] bytes;
            try { bytes = new UTF8Encoding(false, true).GetBytes(value); }
            catch (EncoderFallbackException) { throw new MemoryBoundaryException("URIError", "URI malformed"); }
            var result = new StringBuilder();
            foreach (byte b in bytes)
            {
                char c = (char)b;
                if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || "-_.!~*'()".IndexOf(c) >= 0) result.Append(c);
                else result.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }
    }

    // JSON at the preservation boundary; finite doubles are opaque metadata, never combat operands.
    internal static class MemoryJson
    {
        internal static readonly object Missing = new object();
        internal static Dictionary<string, object?> Object(params object?[] fields)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (int i = 0; i < fields.Length; i += 2) result.Add((string)fields[i]!, fields[i + 1]);
            return result;
        }
        internal static object? Get(object? node, string key) => node is IDictionary<string, object?> obj && obj.TryGetValue(key, out object? value) ? value : Missing;
        // JS own enumeration puts array-index keys before other keys even when raw JSON arrived in a different order.
        internal static IEnumerable<KeyValuePair<string, object?>> Entries(IDictionary<string, object?> source) => source.OrderBy(p => ArrayIndex(p.Key) ?? uint.MaxValue);
        static uint? ArrayIndex(string key) => uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out uint index) && index != uint.MaxValue && key == index.ToString(CultureInfo.InvariantCulture) ? (uint?)index : null;
        internal static void RequirePropertyTarget(object? value) { if (value == null || ReferenceEquals(value, Missing)) throw new MemoryBoundaryException("TypeError", "Missing property target"); }
        internal static IList Array(object? value) => value as IList ?? throw new MemoryBoundaryException("TypeError", "Expected memory array");
        internal static bool Truthy(object? value) => value != null && !ReferenceEquals(value, Missing) && (!(value is bool b) || b) && (!(value is string s) || s.Length != 0) && (!Numeric(value) || Number(value) != 0);
        internal static bool Equal(object? a, object? b) => Numeric(a) && Numeric(b) ? Number(a) == Number(b) : a is string x && b is string y ? x == y : a is bool ab && b is bool bb ? ab == bb : ReferenceEquals(a, b);
        static bool Numeric(object? value) => value is int || value is uint || value is long || value is ulong || value is short || value is ushort || value is byte || value is sbyte || value is double;
        internal static double Number(object? value) => Convert.ToDouble(value, CultureInfo.InvariantCulture);
        internal static bool Integer(object? value) => Numeric(value) && !double.IsNaN(Number(value)) && !double.IsInfinity(Number(value)) && Math.Truncate(Number(value)) == Number(value);
        internal static object? Copy(object? value) => Copy(value, new List<object>());
        static object? Copy(object? value, List<object> ancestors)
        {
            if (value == null || value is string || value is bool || Numeric(value) && (!(value is double n) || !double.IsNaN(n) && !double.IsInfinity(n))) return value;
            if (value is Array array && array.Rank != 1) throw new ArgumentException("Memory JSON requires one-dimensional lists");
            if (ancestors.Count >= 256 || ancestors.Any(a => ReferenceEquals(a, value))) throw new ArgumentException("Memory JSON contains a cycle or exceeds depth256");
            ancestors.Add(value);
            try
            {
                if (value is IDictionary<string, object?> obj)
                {
                    var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var pair in obj) result.Add(pair.Key, Copy(pair.Value, ancestors));
                    return result;
                }
                if (value is IList list) { var result = new List<object?>(); foreach (object? item in list) result.Add(Copy(item, ancestors)); return result; }
                throw new ArgumentException("Memory JSON requires primitives, finite numbers, string-keyed dictionaries and lists");
            }
            finally { ancestors.RemoveAt(ancestors.Count - 1); }
        }
    }
}
