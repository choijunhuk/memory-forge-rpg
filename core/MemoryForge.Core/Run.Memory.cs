// R1c §8.9-8.14/§16.2/§17.5/§20.9: memory save queries and the terminal commit (legacy base-kit rebuild, v2 stable identity).
// Source: engine/rules-core.js Run memoryChanges/commitMemory/previewMemory/commitMemoryV2, same checks, order, labels and costs,
// linked to the reviewed M0 MemoryV2. Queries draw no RNG and change nothing. A commit changes the Run only after every check and
// the whole rebuild passed. Nothing here writes disk, a server or an account store: v2 returns the next store as a detached copy.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace MemoryForge.Core
{
    public sealed partial class Run
    {
        /// <summary>Source memoryChanges, a pure query with no phase check. Legacy: {cid:{capacity,changes}} against each base kit
        /// (per base card a remove, or evo/plus/c-e part of its matched card; then add per remaining non-curse card). v2:
        /// {cid:{capacity,before,after,usedBefore,changes}} from M0 listChanges(memoryBefore, memoryAfter or the live snapshot).</summary>
        public Dictionary<string, object?> MemoryChanges() => Guarded(() => state.MemoryPolicy == "v2" ? V2MemoryChanges()
            : LegacyMemoryChanges().ToDictionary(p => p.Key, p => (object?)p.Value.ToJsonGraph(), StringComparer.Ordinal));

        /// <summary>Source previewMemory: WRONG_PHASE unless v2 with a save pending; M0 apply over the party capacities with
        /// {defeat, baseline, preview:true}, so DEFEAT_KEEP_ONE/OVER_CAPACITY come back as error. Returns {memory,used,error,capacities}.</summary>
        public Dictionary<string, object?> PreviewMemory(object? selectedIds) => Guarded(() =>
        {
            var result = PreviewCore(selectedIds, out var capacities);
            var graph = result.ToJsonGraph(); graph.Add("capacities", capacities); return graph;
        });

        /// <summary>Source commitMemory by the Run's policy; success sets savedMemory, pending {type:'done'} and DONE.
        /// Legacy selection {cid:[change index]}: WRONG_PHASE, BAD_CHOICE, DEFEAT_KEEP_ONE, OVER_CAPACITY, then every party deck is
        /// rebuilt from its base kit; returns the new memory. v2 request {requestId,expectedMemoryVersion,runId,selectedChangeIds}
        /// with the current store: INVALID_SELECTION, RUN_MISMATCH, INVALID_SNAPSHOT/inspect, own receipt replay or REQUEST_CONFLICT,
        /// WRONG_PHASE, VERSION_CONFLICT, preview errors; returns {state,response}. Returned graphs never alias Run state.</summary>
        public Dictionary<string, object?> CommitMemory(object? selection, object? currentMemory = null) => Guarded(() =>
            state.MemoryPolicy == "v2" ? CommitMemoryV2(selection, currentMemory) : CommitLegacyMemory(selection));

        sealed class LegacyMemoryInfo
        {
            public int Capacity;
            public readonly List<Dictionary<string, object?>> Changes = new List<Dictionary<string, object?>>();
            public Dictionary<string, object?> ToJsonGraph() => MemoryJson.Object("capacity", Capacity, "changes", Changes.Select(c => (object?)RunValues.Fields(c)).ToList());
        }
        // A legacy rebuild row: the source spec object and its _touched/_added marks, which never reach the result.
        sealed class LegacySpec
        {
            public readonly Dictionary<string, object?> Fields;
            public bool Touched, Added;
            public LegacySpec(Dictionary<string, object?> fields) { Fields = fields; }
        }

        Dictionary<string, LegacyMemoryInfo> LegacyMemoryChanges()
        {
            var s = state; var costs = data.Memory.Costs; var output = new Dictionary<string, LegacyMemoryInfo>(StringComparer.Ordinal);
            foreach (var party in s.Party)
            {
                string cid = party.CharId; var info = new LegacyMemoryInfo(); var changes = info.Changes;
                var pool = s.Deck.Where(c => !c.Weapon && (c.Owner == cid || c.Owner == "common" && c.Assigned == cid)).ToList();
                // Each base card takes the first pool card with its id or evolved from it (strict ===: undefined matches undefined).
                foreach (var kit in BaseKit(data, cid))
                {
                    object? baseId = MemoryJson.Get(kit.Fields, "id");
                    int index = pool.FindIndex(c => MemoryJson.Equal(MemoryJson.Get(c.Fields, "id"), baseId) || MemoryJson.Equal(MemoryJson.Get(c.Fields, "from"), baseId));
                    if (index < 0)
                    {
                        var removal = MemoryJson.Object("type", "remove"); Put(removal, "id", baseId);
                        removal.Add("cost", costs.Remove); removal.Add("label", CardOf(baseId).Name + " 제거"); changes.Add(removal); continue;
                    }
                    var card = pool[index]; pool.RemoveAt(index);
                    object? key = MemoryJson.Get(card.Fields, "key"), id = MemoryJson.Get(card.Fields, "id"), mods = MemoryJson.Get(card.Fields, "mods");
                    if (MemoryJson.Equal(MemoryJson.Get(card.Fields, "from"), baseId))
                    {
                        var evolution = MemoryJson.Object("type", "evo"); Put(evolution, "key", key); Put(evolution, "id", id); Put(evolution, "from", baseId);
                        evolution.Add("cost", costs.Evolution); evolution.Add("label", CardOf(baseId).Name + " → " + CardOf(id).Name); changes.Add(evolution);
                    }
                    if (MemoryJson.Truthy(MemoryJson.Get(card.Fields, "plus")))
                    {
                        var upgrade = MemoryJson.Object("type", "plus"); Put(upgrade, "key", key); Put(upgrade, "id", id);
                        upgrade.Add("cost", costs.Upgrade); upgrade.Add("label", CardOf(id).Name + " 강화(+)"); changes.Add(upgrade);
                    }
                    foreach (string slot in new[] { "c", "e" })
                    {
                        object? partId = MemoryJson.Get(mods, slot);
                        if (!MemoryJson.Truthy(partId)) continue;
                        var part = PartOf(partId); var fitted = MemoryJson.Object("type", "part"); Put(fitted, "key", key);
                        fitted.Add("slot", slot); fitted.Add("part", part.Id); fitted.Add("cost", part.Rarity == "rare" ? costs.PartRare : costs.PartCommon);
                        fitted.Add("label", CardOf(id).Name + "에 「" + part.Name + "」"); changes.Add(fitted);
                    }
                }
                // Gained cards: the original card's rarity (from || id) plus upgrade, evolution and both part slots.
                foreach (var card in pool)
                {
                    if (card.Owner == "curse") continue;
                    object? from = MemoryJson.Get(card.Fields, "from"), id = MemoryJson.Get(card.Fields, "id"), mods = MemoryJson.Get(card.Fields, "mods");
                    bool plus = MemoryJson.Truthy(MemoryJson.Get(card.Fields, "plus"));
                    int cost = card.Owner == "common" ? costs.AddCommon : AddCost(CardOf(MemoryJson.Truthy(from) ? from : id).Rarity);
                    cost = checked(cost + (plus ? costs.Upgrade : 0) + (MemoryJson.Truthy(from) ? costs.Evolution : 0));
                    foreach (string slot in new[] { "c", "e" })
                    {
                        object? partId = MemoryJson.Get(mods, slot);
                        if (MemoryJson.Truthy(partId)) cost = checked(cost + (PartOf(partId).Rarity == "rare" ? costs.PartRare : costs.PartCommon));
                    }
                    var addition = MemoryJson.Object("type", "add"); Put(addition, "key", MemoryJson.Get(card.Fields, "key")); Put(addition, "id", id);
                    addition.Add("cost", cost); addition.Add("label", CardOf(id).Name + (plus ? "+" : "") + " 추가"); changes.Add(addition);
                }
                // A repeated party character overwrites its entry in place, as the source object assignment does.
                info.Capacity = CapacityOf(party); output[cid] = info;
            }
            return output;
        }
        // Source `{B: addB, A: addA, S: addS}[rar] || addA`: any other rarity, or a zero cost, falls back to addA.
        int AddCost(string rarity)
        {
            var costs = data.Memory.Costs; int cost = rarity == "B" ? costs.AddB : rarity == "A" ? costs.AddA : rarity == "S" ? costs.AddS : 0;
            return cost != 0 ? cost : costs.AddA;
        }

        Dictionary<string, object?> V2MemoryChanges()
        {
            var s = state; var before = V2Before(); var after = s.MemoryAfter ?? TerminalMemory();
            var changes = MemoryV2.ListChanges(data.Memory, before.ToJsonGraph(), after.ToJsonGraph()); var output = MemoryJson.Object();
            foreach (var party in s.Party)
            {
                string cid = party.CharId;
                var info = MemoryJson.Object("capacity", CapacityOf(party), "before", Rows(before, cid), "after", Rows(after, cid));
                info.Add("usedBefore", MemoryV2.Cost(data.Memory, cid, Rows(before, cid), Rows(V2Baseline(), cid)));
                info.Add("changes", changes.Where(change => change.Cid == cid).Select(change => (object?)Labeled(change, after, before)).ToList());
                output[cid] = info;
            }
            return output;
        }
        // Source label: the card's name by key from after, else before; a part change names the slot and replacement/removal.
        Dictionary<string, object?> Labeled(MemoryChange change, MemorySnapshot after, MemorySnapshot before)
        {
            var card = after.Cards(change.Cid).Concat(before.Cards(change.Cid)).FirstOrDefault(c => c.Key == change.Key)
                ?? throw new MemoryBoundaryException("TypeError", "Cannot read properties of undefined (reading 'id')");
            string name = CardOf(card.Id).Name, label;
            switch (change.Kind)
            {
                case "add": label = name + " 추가"; break;
                case "remove": label = name + " 제거"; break;
                case "evolve": label = name + " 진화"; break;
                case "upgrade": label = name + " 강화 변경"; break;
                default:
                    label = name + " " + (MemoryJson.Get(change.Value, "slot") is string slot && slot == "c" ? "회로" : "각인") + " " + (MemoryJson.Truthy(MemoryJson.Get(change.Value, "part")) ? "교체" : "제거");
                    break;
            }
            var graph = change.ToJsonGraph(); graph.Add("label", label); return graph;
        }

        MemoryApplyResult PreviewCore(object? selectedIds, out Dictionary<string, object?> capacities)
        {
            var s = state;
            if (s.MemoryPolicy != "v2" || PendingKind(s.Pending) != "save") throw new RuleException("WRONG_PHASE");
            capacities = MemoryJson.Object();
            foreach (var party in s.Party) capacities[party.CharId] = CapacityOf(party);
            return MemoryV2.Apply(data.Memory, V2Before().ToJsonGraph(), (s.MemoryAfter ?? TerminalMemory()).ToJsonGraph(), selectedIds, capacities,
                MemoryJson.Object("defeat", MemoryJson.Truthy(MemoryJson.Get(s.Pending, "defeat")), "baseline", V2Baseline().ToJsonGraph(), "preview", true));
        }

        Dictionary<string, object?> CommitLegacyMemory(object? selection)
        {
            var s = state;
            if (PendingKind(s.Pending) != "save") throw new RuleException("WRONG_PHASE");
            var all = LegacyMemoryChanges();
            bool defeat = MemoryJson.Truthy(MemoryJson.Get(s.Pending, "defeat"));
            if (!(selection is IDictionary<string, object?> chosen)) throw new RuleException("BAD_CHOICE");
            foreach (var pair in MemoryJson.Entries(chosen))
            {
                // Source `all[cid]` is a plain-object read: an Object.prototype name is truthy and fails only once an index is checked.
                LegacyMemoryInfo? info = all.TryGetValue(pair.Key, out var found) ? found : null;
                if ((info == null && !ObjectPrototypeMembers.Contains(pair.Key)) || !(pair.Value is IList indices) || !Unique(indices)) throw new RuleException("BAD_CHOICE");
                foreach (object? index in indices)
                {
                    if (!MemoryJson.Integer(index) || MemoryJson.Number(index) < 0) throw new RuleException("BAD_CHOICE");
                    if (info == null) throw new MemoryBoundaryException("TypeError", "Cannot read properties of undefined (reading 'length')");
                    if (MemoryJson.Number(index) >= info.Changes.Count) throw new RuleException("BAD_CHOICE");
                }
            }
            long kept = 0;
            foreach (object? value in chosen.Values) if (value is IList list) kept += list.Count;
            if (defeat && kept > 1) throw new RuleException("DEFEAT_KEEP_ONE");
            var byKey = new Dictionary<string, RunCardSpec>(StringComparer.Ordinal);
            foreach (var card in s.Deck) byKey[PropertyKey(MemoryJson.Get(card.Fields, "key"))] = card;
            var memory = MemoryJson.Object();
            foreach (var party in s.Party)
            {
                string cid = party.CharId; var info = all[cid]; var keep = new HashSet<long>();
                if (MemoryJson.Get(chosen, cid) is IList listed) foreach (object? index in listed) keep.Add((long)MemoryJson.Number(index));
                long cost = 0;
                foreach (long index in keep) cost = checked(cost + ChangeCost(info.Changes[(int)index]));
                if (cost > info.Capacity) throw new RuleException("OVER_CAPACITY");
                // Rebuild from the base kit, applying the kept changes in change order with the source _touched/_added matching.
                var specs = BaseKit(data, cid).Select(kit => new LegacySpec(kit.Fields)).ToList();
                for (int index = 0; index < info.Changes.Count; index++)
                {
                    if (!keep.Contains(index)) continue;
                    var change = info.Changes[index]; string? type = MemoryJson.Get(change, "type") as string;
                    if (type == "remove")
                    {
                        object? id = MemoryJson.Get(change, "id");
                        int at = specs.FindIndex(x => MemoryJson.Equal(MemoryJson.Get(x.Fields, "id"), id) && !x.Touched);
                        if (at >= 0) specs.RemoveAt(at);
                    }
                    if (type == "add")
                    {
                        var card = DeckCard(byKey, MemoryJson.Get(change, "key"), "plus");
                        object? plus = MemoryJson.Get(card.Fields, "plus"), mods = MemoryJson.Get(card.Fields, "mods"), from = MemoryJson.Get(card.Fields, "from");
                        var added = MemoryJson.Object(); Put(added, "id", MemoryJson.Get(card.Fields, "id"));
                        // keepBits: all three once any is truthy; plus becomes boolean, an absent mods/from stays absent and null stays null.
                        if (MemoryJson.Truthy(plus) || MemoryJson.Truthy(mods) || MemoryJson.Truthy(from)) { added.Add("plus", MemoryJson.Truthy(plus)); Put(added, "mods", mods); Put(added, "from", from); }
                        if (card.Owner == "common") added.Add("assigned", cid);
                        specs.Add(new LegacySpec(added) { Touched = true, Added = true });
                    }
                    if (type == "evo" || type == "plus" || type == "part")
                    {
                        object? baseId = MemoryJson.Get(change, "from");
                        if (type != "evo")
                        {
                            var card = DeckCard(byKey, MemoryJson.Get(change, "key"), "from"); object? from = MemoryJson.Get(card.Fields, "from");
                            baseId = MemoryJson.Truthy(from) ? from : MemoryJson.Get(card.Fields, "id");
                        }
                        var spec = specs.FirstOrDefault(x => (MemoryJson.Equal(MemoryJson.Get(x.Fields, "id"), baseId) || MemoryJson.Equal(MemoryJson.Get(x.Fields, "from"), baseId)) && !x.Added);
                        if (spec == null) continue;
                        if (type == "evo") { SetMember(spec.Fields, "from", MemoryJson.Get(change, "from")); SetMember(spec.Fields, "id", MemoryJson.Get(change, "id")); }
                        if (type == "plus") spec.Fields["plus"] = true;
                        if (type == "part")
                        {
                            var mods = MemoryJson.Get(spec.Fields, "mods") is IDictionary<string, object?> old ? RunValues.Fields(old) : MemoryJson.Object();
                            SetMember(mods, MemoryJson.Get(change, "slot") as string ?? "undefined", MemoryJson.Get(change, "part"));
                            spec.Fields["mods"] = mods;
                        }
                    }
                }
                memory[cid] = specs.Select(x => (object?)x.Fields).ToList();
            }
            // Source stores and returns one shared object; here state keeps its own copy and the caller gets the built graph.
            s.SavedMemory = MemoryJson.Copy(memory); s.Pending = MemoryJson.Object("type", "done"); s.Phase = "DONE";
            return memory;
        }

        Dictionary<string, object?> CommitMemoryV2(object? input, object? store)
        {
            var s = state; var request = input as IDictionary<string, object?>; object? expected = MemoryJson.Get(request, "expectedMemoryVersion");
            if (request == null || !(MemoryJson.Get(request, "requestId") is string requestId) || requestId.Length == 0 || !MemoryJson.Integer(expected) || MemoryJson.Number(expected) < 0
                || !(MemoryJson.Get(request, "selectedChangeIds") is IList selected) || selected.Cast<object?>().Any(id => !(id is string))
                || selected.Cast<string>().Distinct(StringComparer.Ordinal).Count() != selected.Count) throw new RuleException("INVALID_SELECTION");
            if (!(MemoryJson.Get(request, "runId") is string runId) || runId != s.RunId) throw new RuleException("RUN_MISMATCH");
            object? version = MemoryJson.Get(store, "version");
            if (!(store is IDictionary<string, object?> raw) || !(MemoryJson.Get(raw, "schema") is string schema) || schema != "mf-memory-v2" || !MemoryJson.Integer(version)
                || MemoryJson.Number(version) < 0 || !(MemoryJson.Get(raw, "history") is IList) || !(MemoryJson.Get(raw, "receipts") is IDictionary<string, object?>)) throw new RuleException("INVALID_SNAPSHOT");
            object? saved = MemoryJson.Get(raw, "memory");
            MemoryV2.Inspect(data.Memory, ReferenceEquals(saved, MemoryJson.Missing) ? null : saved);
            var current = RunValues.Fields(raw);
            // The receipt body is source JSON.stringify text in this member order; only the change IDs are sorted (UTF-16 ordinal).
            string body = JsonText(MemoryJson.Object("runId", runId, "expectedMemoryVersion", expected,
                "selectedChangeIds", selected.Cast<string>().OrderBy(id => id, StringComparer.Ordinal).Cast<object?>().ToList()));
            var receipts = (IDictionary<string, object?>)current["receipts"]!;
            if (receipts.TryGetValue(requestId, out object? receipt))
            {
                // An own receipt replays before any phase/version check: same body → its response, the Run finished, the store unchanged.
                if (receipt == null) throw new MemoryBoundaryException("TypeError", "Cannot read properties of null (reading 'body')");
                if (!(MemoryJson.Get(receipt, "body") is string recorded) || recorded != body) throw new RuleException("REQUEST_CONFLICT");
                object? recordedResponse = MemoryJson.Get(receipt, "response");
                if (ReferenceEquals(recordedResponse, MemoryJson.Missing)) throw new MemoryBoundaryException("SyntaxError", "\"undefined\" is not valid JSON");
                var replayed = MemoryJson.Copy(recordedResponse);
                FinishMemory(replayed);
                return MemoryJson.Object("state", current, "response", replayed);
            }
            if (PendingKind(s.Pending) != "save" || (s.Phase != "VICTORY" && s.Phase != "DEFEAT")) throw new RuleException("WRONG_PHASE");
            if (!(s.MemoryVersion is long runVersion) || MemoryJson.Number(expected) != runVersion || MemoryJson.Number(version) != MemoryJson.Number(expected)) throw new RuleException("VERSION_CONFLICT");
            // The run must start from exactly the stored deck: source compares JSON.stringify text, member and row order included.
            var before = V2Before();
            foreach (var party in s.Party)
            {
                object? stored = MemoryJson.Get(current["memory"], party.CharId);
                if (JsonText(MemoryJson.Truthy(stored) ? stored : Rows(V2Baseline(), party.CharId)) != JsonText(Rows(before, party.CharId))) throw new RuleException("VERSION_CONFLICT");
            }
            var result = PreviewCore(selected, out _);
            if (result.Error != null) throw new RuleException(result.Error);
            // `current` is this call's own copy (source clone(current)): merge the party memory, version + 1, inspect, history, receipt.
            var merged = (Dictionary<string, object?>)current["memory"]!;
            foreach (var pair in result.Memory.ToJsonGraph()) merged[pair.Key] = pair.Value;
            long next = checked(runVersion + 1);
            if (next > CanonicalState.MaxSafeInteger) throw new RuleException("RUN_NUMBER_OUT_OF_RANGE");
            current["version"] = next;
            MemoryV2.Inspect(data.Memory, merged);
            ((IList)current["history"]!).Add(MemoryJson.Object("runId", s.RunId, "requestId", requestId, "version", next, "before", before.ToJsonGraph(),
                "memory", result.Memory.ToJsonGraph(), "selectedChangeIds", selected.Cast<object?>().ToList()));
            var response = MemoryJson.Object("memory", result.Memory.ToJsonGraph(), "used", result.Used.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal), "version", next);
            receipts[requestId] = MemoryJson.Object("body", body, "response", MemoryJson.Copy(response));
            FinishMemory(response);
            return MemoryJson.Object("state", current, "response", response);
        }
        // Source finish(response): savedMemory = clone(response.memory), then pending {type:'done'} and DONE. A null response is the
        // TypeError and an undefined memory the clone(undefined) SyntaxError, both before any change.
        void FinishMemory(object? response)
        {
            if (response == null) throw new MemoryBoundaryException("TypeError", "Cannot read properties of null (reading 'memory')");
            object? memory = MemoryJson.Get(response, "memory");
            if (ReferenceEquals(memory, MemoryJson.Missing)) throw new MemoryBoundaryException("SyntaxError", "\"undefined\" is not valid JSON");
            state.SavedMemory = MemoryJson.Copy(memory); state.Pending = MemoryJson.Object("type", "done"); state.Phase = "DONE";
        }

        BattleCardDef CardOf(object? id) => CardDefinition(id as string);
        BattlePartDef PartOf(object? id) => id is string text ? PartDefinition(text) : throw new MemoryBoundaryException("TypeError", "Unknown expedition part definition");
        // Source Run.capacityOf over the party entry (`res || 0`, `asc || 0`): the M0 rule Create already checks against.
        int CapacityOf(RunPartyEntry party) => MemoryV2.CapacityOf(data.Memory, new MemoryCapacityEntry { CharId = party.CharId, Res = party.Res ?? 0, Asc = party.Asc ?? 0 });
        MemorySnapshot V2Before() => state.MemoryBefore ?? throw new InvalidOperationException("v2 run has no starting memory");
        MemorySnapshot V2Baseline() => state.MemoryBaseline ?? throw new InvalidOperationException("v2 run has no memory baseline");
        // clone(snapshot[cid]) as a fresh row list; an absent character is the clone(undefined) SyntaxError.
        static List<object?> Rows(MemorySnapshot snapshot, string cid) => snapshot.CharacterIds.Contains(cid)
            ? snapshot.Cards(cid).Select(c => (object?)c.ToJsonGraph()).ToList() : throw new MemoryBoundaryException("SyntaxError", "\"undefined\" is not valid JSON");
        // Source byKey[key]: Object.fromEntries over the deck by String(key), so the later card wins; a missing card is the TypeError.
        static RunCardSpec DeckCard(Dictionary<string, RunCardSpec> byKey, object? key, string member) => byKey.TryGetValue(PropertyKey(key), out var card) ? card
            : throw new MemoryBoundaryException("TypeError", "Cannot read properties of undefined (reading '" + member + "')");
        static string PropertyKey(object? key) => key is string text ? text : ReferenceEquals(key, MemoryJson.Missing) ? "undefined" : JsKey(key);
        // Source `changes[i]?.cost || 0`.
        static long ChangeCost(IDictionary<string, object?> change) => RunValues.TryInteger(MemoryJson.Get(change, "cost"), out long cost) ? cost : 0;
        // Object literal member: undefined (Missing) is omitted; any JSON value, null included, is copied.
        static void Put(IDictionary<string, object?> target, string name, object? value)
        {
            if (!ReferenceEquals(value, MemoryJson.Missing)) target[name] = MemoryJson.Copy(value);
        }
        // Member assignment keeps the member's position; assigning undefined leaves nothing JSON-visible, so the member is dropped.
        static void SetMember(IDictionary<string, object?> target, string name, object? value)
        {
            if (ReferenceEquals(value, MemoryJson.Missing)) target.Remove(name); else target[name] = MemoryJson.Copy(value);
        }
        // Source `new Set(list).size === list.length`, SameValueZero: numbers by value (±0 and NaN alike), strings/booleans by value.
        static bool Unique(IList list)
        {
            for (int i = 1; i < list.Count; i++)
                for (int j = 0; j < i; j++)
                    if (MemoryJson.Equal(list[i], list[j]) || (list[i] is double a && list[j] is double b && double.IsNaN(a) && double.IsNaN(b))) return false;
            return true;
        }

        // Source JSON.stringify of a JSON graph: object members in JS own-key order (array indices first), undefined members omitted.
        // CanonicalState sorts keys and drops log/ev, so it is not this contract.
        static string JsonText(object? value) { var text = new StringBuilder(); AppendJson(text, value); return text.ToString(); }
        static void AppendJson(StringBuilder text, object? value)
        {
            if (value == null) text.Append("null");
            else if (value is string str) CanonicalState.AppendString(text, str);
            else if (value is bool flag) text.Append(flag ? "true" : "false");
            else if (value is IDictionary<string, object?> members)
            {
                text.Append('{'); bool first = true;
                foreach (var pair in MemoryJson.Entries(members))
                {
                    if (ReferenceEquals(pair.Value, MemoryJson.Missing)) continue;
                    if (!first) text.Append(','); first = false;
                    CanonicalState.AppendString(text, pair.Key); text.Append(':'); AppendJson(text, pair.Value);
                }
                text.Append('}');
            }
            else if (value is IList list)
            {
                text.Append('[');
                for (int i = 0; i < list.Count; i++) { if (i != 0) text.Append(','); AppendJson(text, ReferenceEquals(list[i], MemoryJson.Missing) ? null : list[i]); }
                text.Append(']');
            }
            else if (MemoryJson.Integer(value) || value is double) text.Append(JsNumber(value));
            else throw new ArgumentException("Memory JSON requires primitives, finite numbers, string-keyed dictionaries and lists");
        }
        // Source Number#toString: safe integers exactly; any other finite number as its shortest round-trip digits in the JS layout
        // (plain below 1e21, exponent from 1e21 or below 1e-6). NaN/Infinity are null, as JSON.stringify writes them.
        static string JsNumber(object value)
        {
            if (RunValues.TryInteger(value, out long integer)) return integer.ToString(CultureInfo.InvariantCulture);
            double x = MemoryJson.Number(value);
            if (double.IsNaN(x) || double.IsInfinity(x)) return "null";
            if (x == 0) return "0";
            string sign = x < 0 ? "-" : "";
            ShortestDigits(Math.Abs(x), out string digits, out int point);
            int k = digits.Length;
            if (k <= point && point <= 21) return sign + digits + new string('0', point - k);
            if (0 < point && point <= 21) return sign + digits.Substring(0, point) + "." + digits.Substring(point);
            if (-6 < point && point <= 0) return sign + "0." + new string('0', -point) + digits;
            return sign + digits.Substring(0, 1) + (k == 1 ? "" : "." + digits.Substring(1)) + "e" + (point - 1 < 0 ? "-" : "+") + Math.Abs(point - 1).ToString(CultureInfo.InvariantCulture);
        }
        // Number::toString's digits s and point n of a positive finite double by exact integer arithmetic on its IEEE-754 bits, as
        // runtime double formatting/parsing is not ECMAScript-exact (Unity Mono misses last digits). With x = 4m·2^b, the decimals
        // that read back as x lie between the midpoints to its neighbours, 4m-2 and 4m+2: 4m-1 at a power of two above the least
        // normal (its predecessor is half as far), 4m+2 at the maximum toward 2^1024, both ends only for even m (ties to even).
        // The fewest digits k win, then the closer of the two k-digit decimals around x (a farther one is inside only if the nearer
        // on its side is), a tie to even s. 17 digits always fit: their spacing, at most x·10^-16, is below the interval width.
        static void ShortestDigits(double x, out string digits, out int point)
        {
            long bits = BitConverter.DoubleToInt64Bits(x), fraction = bits & ((1L << 52) - 1);
            int biased = (int)(bits >> 52 & 0x7FF), binary = Math.Max(biased, 1) - 1077;
            long significand = biased == 0 ? fraction : fraction | 1L << 52;
            bool inclusive = (significand & 1) == 0;
            BigInteger value = 4 * (BigInteger)significand, below = value - (fraction == 0 && biased > 1 ? 1 : 2), above = value + 2;
            // x/10^q is exactly value·Scale(q)/Unit(q), and the midpoints/10^q are below and above over the same ratio.
            BigInteger Scale(int q) => BigInteger.Pow(10, Math.Max(-q, 0)) << Math.Max(binary, 0);
            BigInteger Unit(int q) => BigInteger.Pow(10, Math.Max(q, 0)) << Math.Max(-binary, 0);
            // n with 10^(n-1) <= x < 10^n: estimated from the binary exponent (1233/4096 ≈ log10 2), settled by exact comparisons.
            int n = ((binary + 54) * 1233 >> 12) + 1;
            while (value * Scale(n) >= Unit(n)) n++;
            while (value * Scale(n - 1) < Unit(n - 1)) n--;
            for (int k = 1; k <= 17; k++)
            {
                // The k-digit decimals around x: s·10^q with s from 10^(k-1), and (s+1)·10^q unless x is s·10^q itself.
                int q = n - k; BigInteger scale = Scale(q), unit = Unit(q);
                BigInteger s = BigInteger.DivRem(value * scale, unit, out BigInteger rest);
                BigInteger lower = s * unit, upper = lower + unit, low = below * scale, high = above * scale;
                bool down = inclusive ? lower >= low : lower > low;
                bool up = !rest.IsZero && (inclusive ? upper <= high : upper < high);
                if (!down && !up) continue;
                int nearer = (2 * rest).CompareTo(unit);
                if (up && (!down || nearer > 0 || nearer == 0 && !s.IsEven)) s++;
                // s+1 may be 10^k (x rounds up to 10^n): the point counts its digits before the trailing zeros go.
                digits = ((long)s).ToString(CultureInfo.InvariantCulture); point = digits.Length + q; digits = digits.TrimEnd('0');
                return;
            }
            throw new InvalidOperationException("A finite double always reads back from a decimal of at most 17 digits");
        }
    }
}
