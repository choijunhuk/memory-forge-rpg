// P1-LOCAL-AUTHORITATIVE-PERSISTENCE §17.4/§14.3/§8.12/§16.3: explicit public projections of the real Core Run/Battle state. Every member
// is listed here; nothing copies Run.State.ToJsonGraph, Battle.PublicView, a pending graph or a memory store wholesale. Never shown: seed,
// RNG, draw order (piles are counts), enemy pattern/pattern index/passive, receipts, journal, digests, run flags, offer counters or card
// cost modifiers. Card costs, modes, targets and usability come from the Core queries (CostOf/AllowedModes/NeedsTarget/Check with the
// "auto" target), never re-derived here. Slots keep the Core-held raw base/temp/max/sealedUntil members and add, per column, Core's own
// Run.BattleReservationSlots cap/used/sealed/due (P1-PUBLIC-BATTLE-QUERY §17.4); per-card SLOT_FULL still comes from Check. The battle
// query reply (BattleQuery) projects Core's forecast and pick sheet the same explicit way.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MemoryForge.Core;

namespace MemoryForge.Server
{
    static partial class PublicView
    {
        static readonly string[] DeckFields = { "key", "id", "owner", "assigned", "wielder", "weapon", "plus", "growth", "from", "evolved", "gained" };
        static readonly string[] SpecFields = { "key", "id", "plus", "from", "assigned" };
        static readonly HashSet<string> EventFields = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "e", "t", "n", "growth", "turn", "ap", "uid", "src", "s", "part", "phase", "d", "blocked", "id", "card", "mode", "cost", "due",
            "kind", "actor", "fizzled", "target", "reserved", "until", "fallback", "from", "to", "discarded", "index", "hp", "brk"
        };

        /// <summary>The run as its owner may see it. The caller passes the server run version (committed journal length).</summary>
        public static Dictionary<string, object?> Run(Run run, string runId, int version, BattleRules rules)
        {
            var s = run.State;
            var view = Obj("runId", runId, "version", version, "phase", s.Phase, "memoryPolicy", s.MemoryPolicy, "memoryVersion", s.MemoryVersion,
                "length", s.Length, "zones", s.Zones.Select(z => (object?)z).ToList(), "zoneIndex", s.ZoneIndex, "zone", s.Zone, "floor", s.Floor,
                "position", s.Position, "available", run.Available().Select(id => (object?)id).ToList(), "hp", s.Hp, "maxHp", s.MaxHp, "coins", s.Coins,
                "party", s.Party.Select(p => (object?)Obj("charId", p.CharId, "res", p.Res ?? 0)).ToList(),
                "deck", s.Deck.Select(c => (object?)Pick(c.ToJsonGraph(), DeckFields)).ToList(),
                "relics", s.Relics.Select(r => (object?)r).ToList(), "partsFound", s.PartsFound.Select(p => (object?)p).ToList(),
                "pending", Pending(s.Pending), "result", s.Result == null ? null : Obj("outcome", Scalar(Member(s.Result, "outcome")), "floor", Scalar(Member(s.Result, "floor"))),
                "stats", Obj("battles", s.Statistics.Battles, "elites", s.Statistics.Elites, "kills", s.Statistics.Kills),
                "log", s.Log.Select(line => (object?)line).ToList(), "map", Map(s.Map),
                "battle", s.Battle == null ? null : Battle(run, s.Battle, rules));
            if (Member(s.Pending, "type") is string type && type == "save") view.Add("memory", Changes(run.MemoryChanges()));
            return view;
        }

        public static List<object?> Events(IEnumerable<BattleEvent> events) => events.Select(e =>
        {
            var view = Obj();
            foreach (var pair in e.ToJsonGraph()) if (EventFields.Contains(pair.Key)) view[pair.Key] = Scalar(pair.Value);
            if(e.E=="support"&&e.Caller!=null)view.Add("caller",e.Caller);
            return (object?)view;
        }).ToList();

        /// <summary>Core ForgeOffer result {key, parts}.</summary>
        public static object? Offer(object? offer) => offer == null ? null : Obj("key", Scalar(Member(offer, "key")), "parts", Texts(Member(offer, "parts")));

        /// <summary>Core v2 commit response {memory, used, version}.</summary>
        public static Dictionary<string, object?> Commit(object? response) =>
            Obj("memory", Decks(Member(response, "memory")), "used", Numbers(Member(response, "used")), "version", Scalar(Member(response, "version")));

        /// <summary>Core PreviewMemory {memory, used, error, capacities}.</summary>
        public static Dictionary<string, object?> Preview(object? preview) => Obj("memory", Decks(Member(preview, "memory")), "used", Numbers(Member(preview, "used")),
            "error", Scalar(Member(preview, "error")), "capacities", Numbers(Member(preview, "capacities")));

        /// <summary>The account: saved v2 decks, version and history summaries (no receipts, request IDs or snapshots), plus the current run.</summary>
        public static Dictionary<string, object?> Account(string accountId, IDictionary<string, object?> store, Dictionary<string, object?>? run, int finishedRuns) =>
            Obj("accountId", accountId, "memory", Obj("version", Scalar(Member(store, "version")), "memory", Decks(Member(store, "memory")),
                "history", Member(store, "history") is IList history ? history.Cast<object?>().Select(h => (object?)Obj("runId", Scalar(Member(h, "runId")),
                    "version", Scalar(Member(h, "version")), "selectedChangeIds", Texts(Member(h, "selectedChangeIds")))).ToList() : new List<object?>()),
                "run", run, "finishedRuns", finishedRuns);

        /// <summary>P1-PUBLIC-BATTLE-QUERY §17.4/§14.3/§14.7: the battle-query reply. The caller has already refused a source that is not a
        /// hand card (Core's fixed NOT_IN_HAND), so every member is public: the server run version and the node/turn/battle version the answer
        /// belongs to, the requested source (its effective name and BattleText) and request, the reservation board, Core's forecast of exactly
        /// that play (with Core's per-enemy HP loss of the enemies the reply's battle already shows) and the source's pick sheet. Not a
        /// promise about later states and not a replay of earlier ones.</summary>
        public static Dictionary<string, object?> BattleQuery(Run run, string runId, int version, BattleQueryRequest request, BattleChoiceView choices,
            BattlePlayForecast forecast, BattleData content)
        {
            var s = run.State; var b = s.Battle ?? throw new System.InvalidOperationException("a battle query reply needs the live battle");
            // Candidates exist only for the request at Index; their zone is that request's own From (hand, discard or res).
            string? zone = choices.Index < choices.Requests.Count ? choices.Requests[choices.Index].From : null;
            string? name = null, text = null;
            try { var effective = run.BattleEffectiveCard(request.Uid); name = effective.Name; text = BattleText.Card(effective, content); }
            catch (RuleException) { }
            return Obj("runId", runId, "version", version, "phase", s.Phase,
                "context", Obj("node", s.Position, "turn", b.Turn, "battleVersion", b.V, "battlePhase", b.Phase),
                "source", Obj("uid", request.Uid, "id", b.Cards[request.Uid].Id, "name", name, "text", text, "mode", request.Mode, "target", request.Target,
                    "picks", request.Picks.Select(p => (object?)p).ToList()),
                "slots", Slots(run.BattleReservationSlots(), b, content.Rules),
                "forecast", Obj("error", forecast.Error, "mode", forecast.Mode, "cost", forecast.Cost, "due", forecast.Due, "hidden", forecast.Hidden,
                    "enemyHpLoss", forecast.EnemyHpLoss, "enemyDamage", forecast.EnemyDamage, "newlyBroken", forecast.NewlyBroken,
                    "blockDelta", forecast.BlockDelta, "hpDelta", forecast.HpDelta, "defeat", forecast.Defeat,
                    // Core's own per-enemy split (BattlePlayForecast.EnemyHpLosses), copied entry by entry: empty with an error or hidden.
                    "enemyHpLosses", forecast.EnemyHpLosses.Select(l => (object?)Obj("uid", l.Uid, "hpLoss", l.HpLoss)).ToList()),
                "choices", Obj("error", choices.Error,
                    "requests", choices.Requests.Select(r => (object?)Obj("label", r.Label, "from", r.From, "count", r.Count, "growthOnly", r.GrowthOnly,
                        "op", r.Op, "part", r.Part, "leading", r.Leading)).ToList(),
                    "accepted", choices.Accepted, "index", choices.Index, "left", choices.Left, "blocked", choices.Blocked,
                    "candidates", choices.Candidates.Select(uid => (object?)Candidate(b.Cards[uid], zone)).ToList()));
        }

        static Dictionary<string, object?> Battle(Run run, BattleState b, BattleRules rules) => WithSupport(Obj(
            "v", b.V, "phase", b.Phase, "turn", b.Turn, "hp", b.Hp, "maxHp", b.MaxHp, "block", b.Block, "counter", b.Counter,
            "ap", b.Ap, "apNext", b.ApNext, "apDrainNext", b.ApDrainNext, "silenceUntil", b.SilenceUntil, "weak", b.Weak,
            "debts", b.Debts.Select(d => (object?)Obj("n", d.N, "due", d.Due, "deferred", d.Deferred)).ToList(),
            "echo", b.Echo, "echoMax", b.EchoMax, "echoActive", b.EchoActive, "freeNext", b.FreeNext, "played", b.Played,
            "restraint", Obj("active", b.RestraintActive, "usedTurn", b.RestraintUsedTurn), "bonus", Obj("turn", b.BonusTurn, "available", b.BonusAvailable),
            "growth", Obj("count", b.GrowthCount, "cap", b.GrowthCap),
            "buffs", b.Buffs.Select(x => (object?)Obj("s", x.Status, "n", x.N, "until", x.Until)).ToList(),
            "powers", b.Powers.Select(p => (object?)Obj("id", p.Id, "bps", p.Bps, "blk", p.Block, "keep", p.Keep, "heal", p.Heal)).ToList(),
            "piles", Obj("draw", b.Zones.Draw.Count, "hand", b.Zones.Hand.Count, "discard", b.Zones.Discard.Count, "exhaust", b.Zones.Exhaust.Count),
            "hand", b.Zones.Hand.Select(uid => (object?)HandCard(run, b, uid)).ToList(),
            "reservations", b.Reservations.Select(r => (object?)Obj("uid", r.Uid, "id", b.Cards.TryGetValue(r.Uid, out var card) ? card.Id : null,
                "due", r.Due, "order", r.Order, "kind", r.Kind, "target", r.Target, "delayed", r.Delayed)).ToList(),
            "slots", Slots(run.BattleReservationSlots(), b, rules),
            "gaugeMax", rules.GaugeMax,
            "actors", b.Actors.Select(a => (object?)Obj("id", a.Id, "name", a.Name, "attr", a.Attr, "gauge", a.Gauge, "res", a.Res)).ToList(),
            "enemies", b.Enemies.Select(e => (object?)Enemy(e)).ToList(),
            "result", b.Result == null ? null : Obj("phase", b.Result.Phase, "turn", b.Result.Turn, "hp", b.Result.Hp)),b);

        static Dictionary<string, object?> HandCard(Run run, BattleState b, string uid)
        {
            var card = b.Cards[uid];
            var view = Obj("uid", uid, "id", card.Id, "key", Scalar(Member(card.ToJsonGraph(), "key")), "owner", card.Owner, "wielder", card.Wielder,
                "assigned", card.Assigned, "from", card.From, "plus", card.Plus, "mods", Obj("c", card.Circuit, "e", card.Edge),
                "temp", card.Temp.Select(t => (object?)t).ToList(), "growth", card.Growth, "grown", card.Grown, "used", card.Used,
                "token", card.Token, "free", card.Free, "echoCopy", card.EchoCopy, "held", card.Held, "retain", run.BattleRetains(uid));
            List<string> modes;
            try { modes = run.BattleAllowedModes(uid); view["needsTarget"] = run.BattleNeedsTarget(uid); view["cost"] = run.BattleCostOf(uid, "now"); }
            catch (RuleException error) { view["modes"] = new List<object?>(); view["error"] = error.Code; return view; }
            var costs = Obj(); var blocked = Obj();
            foreach (string mode in modes)
            {
                try { costs[mode] = run.BattleCostOf(uid, mode); }
                catch (RuleException error) { costs[mode] = null; blocked[mode] = error.Code; continue; }
                blocked[mode] = run.BattleCheck(uid, mode, "auto");
            }
            view["modes"] = modes.Select(m => (object?)m).ToList(); view["costs"] = costs; view["blocked"] = blocked;
            return view;
        }

        // The Core-held raw base/temp/max and sealedUntil exactly as before, plus Core's ReservationSlots cap/used/sealed/due of each column.
        static Dictionary<string, object?> Slots(BattleSlotView slots, BattleState b, BattleRules rules) => Obj(
            "next", Obj("base", b.SlotsBase.Next, "temp", b.SlotsTemp.Next, "max", rules.NextSlotsMax,
                "cap", slots.Next.Cap, "used", slots.Next.Used, "sealed", slots.Next.Sealed, "due", slots.Next.Due),
            "after", Obj("base", b.SlotsBase.After, "temp", b.SlotsTemp.After, "max", rules.AfterSlotsMax,
                "cap", slots.After.Cap, "used", slots.After.Used, "sealed", slots.After.Sealed, "due", slots.After.Due),
            "sealedUntil", b.SealedUntil);
        // A pick candidate (a hand, discard or reservation card the request offers): its zone (the current request's From) and the same
        // public instance members as a hand card shows.
        static Dictionary<string, object?> Candidate(BattleCardState card, string? zone) => Obj("uid", card.Uid, "id", card.Id, "zone", zone, "plus", card.Plus,
            "mods", Obj("c", card.Circuit, "e", card.Edge), "temp", card.Temp.Select(t => (object?)t).ToList(), "growth", card.Growth);

        static Dictionary<string, object?> Enemy(BattleEnemyState e)
        {
            var view = Obj("uid", e.Uid, "id", e.Id, "name", e.Name, "tier", e.Tier, "hp", e.Hp, "maxHp", e.MaxHp, "block", e.Block, "brk", e.Brk,
                "brkMax", e.BrkMax, "brokenUntil", e.BrokenUntil, "weak", e.Weak.Select(w => (object?)w).ToList(),
                "st", Obj("vul", e.Status.Vul, "weak", e.Status.Weak, "mark", e.Status.Mark, "burn", e.Status.Burn, "str", e.Status.Strength, "reflect", e.Status.Reflect),
                "stunned", e.Stunned, "alive", e.Alive, "intent", Move(e.Intent, 0), "next", Move(e.Next, 0));
            if (e.PhaseIndex.HasValue) view.Add("phaseIndex", e.PhaseIndex.Value);
            return view;
        }
        // Only the current and next intent (§17.4); a second phase's pattern stays hidden.
        static object? Move(EnemyMoveDef? move, int depth)
        {
            if (move == null || depth > 3) return null;
            var view = Obj("t", move.Type);
            Optional(view, "d", move.Damage); Optional(view, "h", move.Hits); Optional(view, "str", move.Strength); Optional(view, "b", move.Block); Optional(view, "n", move.N);
            Optional(view, "card", move.Card); Optional(view, "s", move.Status); Optional(view, "who", move.Who); Optional(view, "label", move.Label); Optional(view, "e", move.Enemy);
            if (move.Then != null) view.Add("then", Move(move.Then, depth + 1));
            return view;
        }

        static object? Map(RunMap? map) => map == null ? null : Obj("zone", map.Zone,
            "floors", map.Floors.Select(row => (object?)row.Select(n => (object?)Node(n)).ToList()).ToList(), "boss", Node(map.Boss));
        static Dictionary<string, object?> Node(RunNode node) =>
            Obj("id", node.Id, "type", node.Type, "floor", node.Floor, "index", node.Index, "next", node.Next?.Select(n => (object?)n).ToList());

        // Each pending type's own public members only.
        static object? Pending(object? pending)
        {
            if (!(pending is IDictionary<string, object?> p)) return null;
            string? type = Member(p, "type") as string;
            var view = Obj("type", type);
            switch (type)
            {
                case "reward":
                    view["coins"] = Scalar(Member(p, "coins")); view["cards"] = Texts(Member(p, "cards"));
                    view["relic"] = Member(p, "relic") is IList ? Texts(Member(p, "relic")) : null;
                    if (p.ContainsKey("part")) view["part"] = Scalar(Member(p, "part"));
                    view["evolutions"] = Member(p, "evolutions") is IList evolutions ? evolutions.Cast<object?>()
                        .Select(x => (object?)Obj("key", Scalar(Member(x, "key")), "options", Texts(Member(x, "options")))).ToList() : new List<object?>();
                    break;
                case "rest": view["uses"] = Scalar(Member(p, "uses")); break;
                case "forge":
                    view["uses"] = Scalar(Member(p, "uses")); view["offers"] = Offer(Member(p, "offers"));
                    if (p.ContainsKey("fromEvent")) view["fromEvent"] = Scalar(Member(p, "fromEvent"));
                    break;
                case "shop":
                    foreach (string kind in new[] { "cards", "parts", "relics" })
                        view[kind] = Member(p, kind) is IList items ? items.Cast<object?>().Select(x => (object?)Obj("id", Scalar(Member(x, "id")),
                            "price", Scalar(Member(x, "price")), "sold", Member(x, "sold") is bool sold && sold)).ToList() : new List<object?>();
                    view["removeCost"] = Scalar(Member(p, "removeCost"));
                    break;
                case "event":
                    view["id"] = Scalar(Member(p, "id"));
                    if (p.ContainsKey("done")) view["done"] = Scalar(Member(p, "done"));
                    if (p.ContainsKey("choice")) view["choice"] = Scalar(Member(p, "choice"));
                    break;
                case "save": view["defeat"] = Scalar(Member(p, "defeat")); break;
            }
            return view;
        }

        // Core v2 MemoryChanges: {cid: {capacity, before, after, usedBefore, changes}} for the save screen (§8.12).
        static Dictionary<string, object?> Changes(Dictionary<string, object?> changes)
        {
            var view = Obj();
            foreach (var pair in changes)
                view[pair.Key] = Obj("capacity", Scalar(Member(pair.Value, "capacity")), "usedBefore", Scalar(Member(pair.Value, "usedBefore")),
                    "before", Specs(Member(pair.Value, "before")), "after", Specs(Member(pair.Value, "after")),
                    "changes", Member(pair.Value, "changes") is IList list ? list.Cast<object?>().Select(c => (object?)Change(c)).ToList() : new List<object?>());
            return view;
        }
        static Dictionary<string, object?> Change(object? change)
        {
            var view = Obj("id", Scalar(Member(change, "id")), "cid", Scalar(Member(change, "cid")), "key", Scalar(Member(change, "key")),
                "kind", Scalar(Member(change, "kind")), "label", Scalar(Member(change, "label")));
            if (change is IDictionary<string, object?> map && map.TryGetValue("value", out object? value))
                view["value"] = value is bool flag ? (object?)flag : value is IDictionary<string, object?> fields
                    ? fields.ContainsKey("slot") ? Obj("slot", Scalar(Member(fields, "slot")), "part", Scalar(Member(fields, "part"))) : Pick(fields, SpecFields)
                    : null;
            return view;
        }
        static Dictionary<string, object?> Decks(object? memory)
        {
            var view = Obj();
            if (memory is IDictionary<string, object?> decks) foreach (var pair in decks) view[pair.Key] = Specs(pair.Value);
            return view;
        }
        static List<object?> Specs(object? rows) => rows is IList list
            ? list.Cast<object?>().Select(c => (object?)(c is IDictionary<string, object?> card ? Pick(card, SpecFields) : null)).ToList() : new List<object?>();
        static Dictionary<string, object?> Numbers(object? values)
        {
            var view = Obj();
            if (values is IDictionary<string, object?> map) foreach (var pair in map) view[pair.Key] = Scalar(pair.Value);
            return view;
        }

        // Allowlisted scalar members plus the {c, e} part slots.
        static Dictionary<string, object?> Pick(IDictionary<string, object?> source, string[] fields)
        {
            var view = Obj();
            foreach (string field in fields) if (source.TryGetValue(field, out object? value)) view[field] = Scalar(value);
            if (source.TryGetValue("mods", out object? mods))
                view["mods"] = mods is IDictionary<string, object?> slots ? Obj("c", Scalar(Member(slots, "c")), "e", Scalar(Member(slots, "e"))) : null;
            return view;
        }
        static Dictionary<string, object?> Obj(params object?[] pairs) => GraphJson.Object(pairs);
        static object? Member(object? node, string key) => node is IDictionary<string, object?> map && map.TryGetValue(key, out object? value) ? value : null;
        static object? Scalar(object? value) => value is string || value is bool || value is int || value is long || value is uint ? value : null;
        static List<object?> Texts(object? value) => value is IList list ? list.Cast<object?>().Where(x => x is string).ToList() : new List<object?>();
        static void Optional(Dictionary<string, object?> view, string key, int? value) { if (value.HasValue) view.Add(key, value.Value); }
        static void Optional(Dictionary<string, object?> view, string key, string? value) { if (value != null) view.Add(key, value); }
    }
}
