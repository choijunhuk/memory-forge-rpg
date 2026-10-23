// P1-PUBLIC-BATTLE-QUERY §17.4/§14.7/§14.3: Battle.ReservationSlots, Battle.HandChoices and the shared BattleChoicePolicy, checked through
// actual Battle.Create/Command on ONE explicitly TEST-ONLY BattleData variant: the real data/game_data.json loaded unchanged, then
// test-local "t_" cards, one test part and three test enemies appended in memory, and the hand draw raised to 10 so every small deck is
// drawn whole and hand membership never depends on the shuffle (only the order inside the hand does, which is read from the snapshot).
// Static policy calls use explicit BattleState fixtures. Expected values are literals worked out by hand from the rules, never the
// implementation's own expressions; nothing is injected into a live battle and no production restore path is used.
// Not covered: Unity Play/device, the server query, live authored cards, Turn near Int32.MaxValue (unreachable through commands; only a
// static type bound), and a reservation due outside both columns during a player turn (unreachable through commands, see the scope line).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MemoryForge.Core;

static class BattlePresentationQueryChecks
{
    const string Part = "t_part", PartName = "시험 부품", Tuned = "「시험 부품」을 붙일 카드";
    static readonly string[] None = new string[0];
    static int checks, failures, commands, queries;
    static void Check(bool ok, string label) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL BattlePresentationQuery " + label); } }

    sealed class Session
    {
        public Battle Battle = null!;
        public BattleData Data = null!;
        public readonly List<(BattleRequest Request, string Response)> Receipts = new List<(BattleRequest, string)>();
    }

    public static int Run(string root)
    {
        checks = failures = commands = queries = 0;
        foreach (var (name, scenario) in new (string, Action<string>)[]
        {
            ("temp slots/expiry board", TempSlotsAndExpiry), ("delayed reservations", DelayKeepsKindAndExactDue), ("enemy seal", EnemySeal),
            ("hand-source gate", HandSourceGate), ("tune/curse/recall", TuneCurseRecall), ("costdown/FreeNext", CostdownFreeNext),
            ("growth/evolution/cap", GrowthEvolutionCap), ("ordered multi-request picks", OrderedPicks), ("static policy", StaticPolicy)
        })
        {
            try { scenario(root); } catch (Exception error) { Check(false, name + ": " + error); }
        }
        Console.WriteLine($"BattlePresentationQueryChecks: {checks - failures}/{checks} passed; {commands} actual commands, {queries} pure queries");
        Console.WriteLine("BattlePresentationQueryScope: TEST-ONLY t_ cards/part/enemies appended in memory to the unchanged live content; Unity Play, device and server acceptance NOT_RUN here");
        Console.WriteLine("BattlePresentationQueryNotCovered: Turn near Int32.MaxValue (long Due is a static type bound only); a reservation due outside both columns in a player turn " +
            "(delayRes moves one turn and discards beyond the board, so every live reservation is due Turn+1 or Turn+2 when a player turn starts)");
        return failures;
    }

    // ───────── TEST-ONLY content ─────────
    static BattleData Variant(string root)
    {
        var data = BattleDataLoader.Load(root);
        data.Rules.HandDraw = 10; // TEST-ONLY: the whole deck (≤ 10) is drawn every turn
        data.Parts.Add(new BattlePartDef { Id = Part, Slot = "circuit", Name = PartName, Rarity = "R", Text = "TEST-ONLY" });
        BattleCardDef Card(string id, int cost, string reservation, params EffectDef[] effects)
        {
            var card = new BattleCardDef { Id = id, Name = "시험 " + id, Owner = "t_owner", Rarity = "R", Cost = cost, Type = "skill", Reservation = reservation, Attr = "none", Effects = effects.ToList() };
            data.Cards.Add(card); return card;
        }
        EffectDef Block() => new EffectDef { Op = "blk", V = 1 };
        Card("t_plain", 1, "y", Block()); Card("t_cheap", 0, "y", Block());
        Card("t_curse", 1, "n", Block()).Rarity = "C"; Card("t_solo", 1, "n", Block()).Rarity = "C";
        var conditional = Card("t_cond", 2, "n", Block());
        conditional.CostIf = new BattleCardCostIfDef { Condition = new EffectConditionDef { Condition = "debt" }, Delta = -1 }; conditional.CostIfPresent = true;
        Card("t_exhaust", 0, "n", Block()).Keywords.Add("exhaust");
        var growth = Card("t_grow", 1, "y", Block()); growth.Growth = true; growth.Evolutions = new List<string> { "t_evo" };
        Card("t_evo", 1, "y", Block());
        Card("t_tune", 0, "n", new EffectDef { Op = "tune", Part = Part, Pick = "one" });
        Card("t_costdown", 0, "n", new EffectDef { Op = "costdown", N = 1, Pick = "one" });
        Card("t_costdown_late", 0, "n", Block(), new EffectDef { Op = "costdown", N = 1, Pick = "one" });
        Card("t_recall", 0, "n", new EffectDef { Op = "recall" });
        Card("t_growpick", 0, "n", new EffectDef { Op = "growPick", N = 1 });
        Card("t_copypick", 0, "n", new EffectDef { Op = "copyPick" });
        Card("t_multi", 0, "n", new EffectDef { Op = "discard", N = 2 }, new EffectDef { Op = "exhaustPick", From = "discard", N = 1 }, new EffectDef { Op = "pull" });
        Card("t_free1", 0, "n", new EffectDef { Op = "freeNext", N = 1 }); Card("t_free2", 0, "n", new EffectDef { Op = "freeNext", N = 2 });
        Card("t_slot", 0, "n", new EffectDef { Op = "slot", Next = 1, After = 1 });
        void Enemy(string id, params string[] moves) => data.Enemies.Add(new BattleEnemyDef { Id = id, Name = "시험 적", Tier = "normal", Hp = 500, Brk = 50,
            Weak = new List<string> { "frost" }, Pattern = moves.Select(m => new EnemyMoveDef { Type = m }).ToList() });
        Enemy("t_dummy", "charge"); Enemy("t_delay", "delayRes"); Enemy("t_sealer", "sealSlot", "charge");
        data.Index(); return data;
    }
    static BattleCardSpec C(string id, int growth = 0, string? from = null) => new BattleCardSpec { Id = id, Growth = growth, From = from };
    // Deck cards get c1.. in deck order and the one enemy the next sequence number (Battle.Create's MakeCard/AddEnemy).
    static Session Open(string root, string enemy, params BattleCardSpec[] deck)
    {
        var data = Variant(root);
        var setup = new BattleSetup { Hp = 81, MaxHp = 81, Seed = 11 };
        setup.Party.Add(new BattleActorSetup { CharId = "sera" }); setup.Deck.AddRange(deck); setup.Enemies.Add(enemy);
        return new Session { Battle = Battle.Create(data, setup), Data = data };
    }

    // ───────── actual commands ─────────
    static BattleArgument Args(string uid, string mode, string[] picks) => BattleArgument.Object(new[]
    {
        new KeyValuePair<string, BattleArgument>("uid", BattleArgument.String(uid)), new KeyValuePair<string, BattleArgument>("mode", BattleArgument.String(mode)),
        new KeyValuePair<string, BattleArgument>("target", BattleArgument.Null()), new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array(picks.Select(p => BattleArgument.String(p))))
    });
    static BattleResponse Send(Session s, string kind, BattleArgument? args = null)
    {
        var request = new BattleRequest("pq-" + ++commands, s.Battle.State.V, kind, args);
        var response = s.Battle.Command(request); s.Receipts.Add((request, Full(response.ToJsonGraph()))); return response;
    }
    static BattleResponse Begin(Session s) => Send(s, "begin");
    static BattleResponse End(Session s) => Send(s, "end");
    static BattleResponse Play(Session s, string uid, string mode, params string[] picks) => Send(s, "play", Args(uid, mode, picks));
    static void Rejects(Session s, string uid, string mode, string code, string label)
    {
        string before = Full(s.Battle.State.ToJsonGraph()), receipts = ReceiptMap(s.Battle); bool thrown = false; commands++;
        try { s.Battle.Command(new BattleRequest("pq-reject-" + commands, s.Battle.State.V, "play", Args(uid, mode, None))); }
        catch (RuleException error) { thrown = error.Code == code; }
        Check(thrown && Full(s.Battle.State.ToJsonGraph()) == before && ReceiptMap(s.Battle) == receipts, label + ": the real play rejects with exactly " + code + " and changes nothing (no receipt stored)");
    }
    // After the queries every stored receipt still replays its original response and executes nothing; the reflected private map holds
    // exactly the receipts this test stored (so ReceiptMap reads the real map), and the replays add none.
    static void Receipts(Session s, string label)
    {
        string state = Full(s.Battle.State.ToJsonGraph());
        var keys = ReceiptTable(s.Battle).Keys.Cast<string>().ToList();
        Check(keys.Count == s.Receipts.Count && keys.ToHashSet(StringComparer.Ordinal).SetEquals(s.Receipts.Select(r => r.Request.Rid ?? "")),
            label + ": the private receipt map holds exactly the " + s.Receipts.Count + " stored receipts (got " + keys.Count + ")");
        string map = ReceiptMap(s.Battle);
        Check(s.Receipts.Count > 0 && s.Receipts.All(r => Full(s.Battle.Command(r.Request).ToJsonGraph()) == r.Response) && Full(s.Battle.State.ToJsonGraph()) == state,
            label + ": every stored receipt replays its original response after the queries");
        Check(ReceiptMap(s.Battle) == map, label + ": the replays add, drop or change no receipt");
    }
    // TEST-ONLY read-only reflection over Battle's private receipt map, which lives outside the state graph: nothing is written, restored
    // or injected. Resolved per call so a renamed field fails this scenario instead of the whole suite.
    static IDictionary ReceiptTable(Battle battle) =>
        typeof(Battle).GetField("receipts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(battle) as IDictionary ?? throw new InvalidDataException("Battle.receipts map not readable");
    // The entire map: count, then every key with its stored identity and response, in map order.
    static string ReceiptMap(Battle battle)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var map = ReceiptTable(battle); var rows = new List<string> { map.Count.ToString(CultureInfo.InvariantCulture) };
        foreach (DictionaryEntry entry in map)
        {
            object receipt = entry.Value ?? throw new InvalidDataException("null receipt");
            var type = receipt.GetType();
            string identity = type.GetField("Identity", flags)?.GetValue(receipt) as string ?? throw new InvalidDataException("receipt identity not readable");
            var response = type.GetField("Response", flags)?.GetValue(receipt) as BattleResponse ?? throw new InvalidDataException("receipt response not readable");
            rows.Add((string)entry.Key + "␟" + identity + "␟" + Full(response.ToJsonGraph()));
        }
        return string.Join("\n", rows);
    }

    // ───────── purity and detachment ─────────
    static string Full(object? graph) => JsonSerializer.Serialize(graph);
    static string Definitions(BattleData data) => Full(data.Cards.Select(c => c.ToJsonGraph()).Concat(data.Parts.Select(p => p.ToJsonGraph())).ToList());
    // The complete state graph (version, RNG, log, events, zones, cards, reservations), the public view, the definitions and the entire
    // private receipt map (keys, identities, responses) are identical before and after the query.
    static T Pure<T>(Session s, Func<Battle, T> query, string label)
    {
        string state = Full(s.Battle.State.ToJsonGraph()), view = Full(s.Battle.PublicView()), definitions = Definitions(s.Data), receipts = ReceiptMap(s.Battle);
        var result = query(s.Battle); queries++;
        Check(Full(s.Battle.State.ToJsonGraph()) == state && Full(s.Battle.PublicView()) == view && Definitions(s.Data) == definitions && ReceiptMap(s.Battle) == receipts,
            label + " is pure (state/version/RNG/log/events/public view/definitions/receipt map unchanged)");
        return result;
    }
    static string Print(BattleSlotColumn c) => $"{c.Kind}/{c.Due}/{c.Base}/{c.Temp}/{c.Max}/{c.Cap}/{c.Used}/{c.Sealed}";
    static string Print(BattleSlotView v) => $"{v.Turn}|{v.SealedUntil}|{Print(v.Next)}|{Print(v.After)}|" +
        string.Join(";", v.Reservations.Select(r => $"{r.Uid},{r.Due},{r.Order},{r.Target},{r.Kind},{r.Delayed}"));
    static string Print(BattleChoiceView v) => $"{v.Error}|{v.Uid}|{v.Mode}|{v.Accepted}|{v.Index}|{v.Left}|{v.Blocked}|[{string.Join(",", v.Candidates)}]|" +
        string.Join(";", v.Requests.Select(r => $"{r.Label},{r.From},{r.Count},{r.GrowthOnly},{r.Op},{r.Part},{r.Leading}"));

    // ───────── board assertions ─────────
    static BattleSlotView Slots(Session s, int turn, int sealedUntil, string label, params (string Uid, int Due, int Order, string Kind, bool? Delayed)[] reservations)
    {
        var view = Pure(s, b => b.ReservationSlots(), label + " ReservationSlots"); string printed = Print(view);
        Check(view.Turn == turn && view.SealedUntil == sealedUntil, label + $": turn {turn}, sealedUntil {sealedUntil} (got {view.Turn}, {view.SealedUntil})");
        Check(view.Reservations.Select(r => (r.Uid, r.Due, r.Order, r.Kind, r.Delayed)).SequenceEqual(reservations) && view.Reservations.All(r => r.Target == null),
            label + ": every reservation in state order with its own due/order/kind/delay flag (got " + printed + ")");
        string state = Full(s.Battle.State.ToJsonGraph()); var probe = s.Battle.ReservationSlots();
        bool fresh = !ReferenceEquals(probe.Reservations, view.Reservations) && !ReferenceEquals(probe.Next, view.Next) && !ReferenceEquals(probe.After, view.After)
            && probe.Reservations.Count == view.Reservations.Count && probe.Reservations.Select((r, i) => !ReferenceEquals(r, view.Reservations[i])).All(x => x);
        Check(Print(probe) == printed && fresh, label + ": a repeated answer is an equal fresh object (list, both columns, every reservation)");
        probe.Turn = -1; probe.SealedUntil = -1; probe.Next.Cap = -9; probe.Next.Sealed = !probe.Next.Sealed; probe.After.Used = 99; probe.After.Kind = "caller-mutation";
        foreach (var r in probe.Reservations) { r.Uid = "caller-mutation"; r.Due = -1; r.Order = -1; r.Kind = "caller-mutation"; r.Target = "caller-mutation"; r.Delayed = false; }
        // Before any further query: a shared or cached column/reservation object would show here.
        Check(Print(view) == printed, label + ": mutating a second answer's columns and reservations leaves the first answer unchanged");
        probe.Reservations.Clear();
        Check(Print(view) == printed && Print(s.Battle.ReservationSlots()) == printed && Full(s.Battle.State.ToJsonGraph()) == state,
            label + ": the answer is detached (a mutated copy reaches neither state nor the next answer)");
        return view;
    }
    static void Column(BattleSlotColumn c, string kind, long due, int @base, int temp, int max, int cap, int used, bool @sealed, string label) =>
        Check(c.Kind == kind && c.Due == due && c.Base == @base && c.Temp == temp && c.Max == max && c.Cap == cap && c.Used == used && c.Sealed == @sealed,
            label + $" {kind}: due {due}, base {@base}, temp {temp}, max {max}, cap {cap}, used {used}, sealed {@sealed} (got {Print(c)})");

    // ───────── pick-sheet assertions ─────────
    static BattleChoiceView Choices(Session s, string uid, string mode, string[] picks, string label)
    {
        var supplied = new List<string>(picks);
        var view = Pure(s, b => b.HandChoices(uid, mode, supplied), label + " HandChoices"); string printed = Print(view);
        bool untouched = supplied.SequenceEqual(picks); supplied.Clear(); supplied.Add("caller-mutation");
        var probe = s.Battle.HandChoices(uid, mode, picks);
        bool fresh = !ReferenceEquals(view.Candidates, probe.Candidates) && !ReferenceEquals(view.Requests, probe.Requests)
            && probe.Requests.Count == view.Requests.Count && probe.Requests.Select((r, i) => !ReferenceEquals(r, view.Requests[i])).All(x => x);
        Check(untouched && Print(view) == printed && Print(probe) == printed && fresh,
            label + ": the caller's picks are neither changed nor retained; a repeated answer is an equal fresh object (lists and every request)");
        probe.Error = "caller-mutation"; probe.Blocked = "caller-mutation"; probe.Accepted = -1; probe.Index = -1; probe.Left = -1; probe.Candidates.Add("caller-mutation");
        foreach (var r in probe.Requests)
        {
            r.Label = "caller-mutation"; r.From = "caller-mutation"; r.Op = "caller-mutation"; r.Part = "caller-mutation";
            r.Count = -1; r.GrowthOnly = !r.GrowthOnly; r.Leading = !r.Leading;
        }
        // Before any further query: a shared or cached request/list object would show here.
        Check(Print(view) == printed, label + ": mutating a second answer's lists and requests leaves the first answer unchanged");
        probe.Requests.Add(new BattleChoiceRequest { Label = "caller-mutation" });
        Check(Print(view) == printed && Print(s.Battle.HandChoices(uid, mode, picks)) == printed, label + ": the answer is detached");
        return view;
    }
    static void Expect(BattleChoiceView v, string? error, int accepted, int index, int left, string? blocked, IEnumerable<string> candidates, string label)
    {
        var expected = candidates.ToList();
        Check(v.Error == error && v.Accepted == accepted && v.Index == index && v.Left == left && v.Blocked == blocked && v.Candidates.SequenceEqual(expected),
            label + $": error {error ?? "-"}, accepted {accepted}, index {index}, left {left}, blocked {blocked ?? "-"}, candidates [{string.Join(",", expected)}] (got {Print(v)})");
    }
    static void Requests(BattleChoiceView v, string label, params (string Label, string From, int Count, bool GrowthOnly, string Op, string? Part, bool Leading)[] expected) =>
        Check(v.Requests.Select(r => (r.Label, r.From, r.Count, r.GrowthOnly, r.Op, r.Part, r.Leading)).SequenceEqual(expected), label + ": exact requests (got " + Print(v) + ")");
    static void Refused(BattleChoiceView v, string uid, string mode, string code, string label) =>
        Check(v.Error == code && v.Uid == uid && v.Mode == mode && v.Requests.Count == 0 && v.Accepted == 0 && v.Index == 0 && v.Left == 0 && v.Blocked == null && v.Candidates.Count == 0,
            label + ": exactly " + code + " with every other field default (got " + Print(v) + ")");
    // The literal members in the live hand's own order (the order is the shuffle's; membership is the expectation).
    static List<string> Hand(Session s, params string[] members)
    {
        var hand = s.Battle.State.Zones.Hand;
        Check(members.All(hand.Contains), "fixture: [" + string.Join(",", members) + "] are all in hand [" + string.Join(",", hand) + "]");
        return hand.Where(members.Contains).ToList();
    }
    static readonly (string, string, int, bool, string, string?, bool) TuneRequest = (Tuned, "hand", 1, false, "tune", Part, true);
    static readonly (string, string, int, bool, string, string?, bool) CostdownRequest = ("비용을 낮출 카드", "hand", 1, false, "costdown", null, true);
    static readonly (string, string, int, bool, string, string?, bool) GrowRequest = ("성장시킬 카드", "hand", 1, true, "growPick", null, true);
    static readonly (string, string, int, bool, string, string?, bool) CopyRequest = ("사본을 만들 성장 카드", "hand", 1, true, "copyPick", null, true);

    // ───────── reservation board ─────────
    // RULES_NOW_PLAY (2026-10-19): collateral is block + debt 1 and seals nothing, so this board no longer plays a seal card. The seal
    // itself (a sealed next column, its expiry and reapplication) is covered by the EnemySeal scenario through the t_sealer enemy's sealSlot.
    // Rules used for every literal: next base 2 / max 3, after base 1 / max 2, Cap = min(Max, Base + Temp), minus 1 on next while
    // SealedUntil >= Turn; slot temp is clamped to Max - Base; Used counts reservations due exactly on the column's Due (Turn + 1 / + 2);
    // AP 4 per turn; t_plain costs 1, t_slot and t_cheap 0; the TEST-ONLY hand draw 10 draws the whole six-card deck.
    static void TempSlotsAndExpiry(string root)
    {
        var s = Open(root, "t_dummy", C("t_plain"), C("t_plain"), C("t_plain"), C("t_plain"), C("t_slot"), C("t_cheap"));
        // Turn 0 (CREATED): SealedUntil 0 >= Turn 0 already holds, so the next column is one short (min(3, 2) - 1 = 1) before the first turn.
        var created = Slots(s, 0, 0, "created");
        Column(created.Next, "next", 1, 2, 0, 3, 1, 0, true, "created (turn-0 seal predicate)"); Column(created.After, "after", 2, 1, 0, 2, 1, 0, false, "created");
        Begin(s);
        // Turn 1: SealedUntil 0 < 1, so the base columns are open: next 2, after 1.
        var opened = Slots(s, 1, 0, "turn 1 opening");
        Column(opened.Next, "next", 2, 2, 0, 3, 2, 0, false, "turn 1 opening"); Column(opened.After, "after", 3, 1, 0, 2, 1, 0, false, "turn 1 opening");
        Play(s, "c5", "now"); // slot next +1 (clamped to 3 - 2 = 1), after +1 (clamped to 2 - 1 = 1)
        var widened = Slots(s, 1, 0, "turn 1 temp slots");
        Column(widened.Next, "next", 2, 2, 1, 3, 3, 0, false, "turn 1 temp slots"); Column(widened.After, "after", 3, 1, 1, 2, 2, 0, false, "turn 1 temp slots");
        // Fill the widened next column (AP 4 - 1 - 1 - 0 = 2 left), then the fourth next reservation is refused although AP remains.
        Play(s, "c1", "next"); Play(s, "c2", "next"); Play(s, "c6", "next");
        Rejects(s, "c3", "next", "SLOT_FULL", "turn 1 next used 3 of widened cap 3");
        // The widened after column takes two (AP 2 - 1 - 1 = 0): one more than its base 1.
        Play(s, "c3", "after"); Play(s, "c4", "after");
        var full = Slots(s, 1, 0, "turn 1 full", ("c1", 2, 1, "next", null), ("c2", 2, 2, "next", null), ("c6", 2, 3, "next", null), ("c3", 3, 4, "after", null), ("c4", 3, 5, "after", null));
        Column(full.Next, "next", 2, 2, 1, 3, 3, 3, false, "turn 1 full"); Column(full.After, "after", 3, 1, 1, 2, 2, 2, false, "turn 1 full");
        End(s);
        // Turn 2: c1/c2/c6 (due 2) resolved at turn start and the temp slots expired. Both after reservations are now due next turn, kind
        // kept, and fill the base next column exactly (Used 2 = Cap 2): the slots they were made with no longer exist.
        var carried = Slots(s, 2, 0, "turn 2", ("c3", 3, 4, "after", null), ("c4", 3, 5, "after", null));
        Column(carried.Next, "next", 3, 2, 0, 3, 2, 2, false, "turn 2 expired temp + carried after reservations"); Column(carried.After, "after", 4, 1, 0, 2, 1, 0, false, "turn 2");
        Rejects(s, "c1", "next", "SLOT_FULL", "turn 2 carried after reservations fill next used 2 of cap 2");
        Play(s, "c1", "after");
        End(s);
        // Turn 3: c3/c4 (due 3) resolved; the turn-2 after reservation c1 (due 4) now counts in the next column.
        var expired = Slots(s, 3, 0, "turn 3", ("c1", 4, 6, "after", null));
        Column(expired.Next, "next", 4, 2, 0, 3, 2, 1, false, "turn 3 carried after reservation"); Column(expired.After, "after", 5, 1, 0, 2, 1, 0, false, "turn 3");
        Check(typeof(BattleSlotColumn).GetField("Due")?.FieldType == typeof(long), "STATIC_BOUND: column Due is a long, so Turn + 2 cannot wrap at Int32.MaxValue (runtime case NOT_COVERED)");
        Receipts(s, "board temp slots/expiry");
    }

    static void DelayKeepsKindAndExactDue(string root)
    {
        var s = Open(root, "t_delay", C("t_plain"), C("t_plain"), C("t_plain"));
        Begin(s); Play(s, "c1", "next"); Play(s, "c2", "after");
        var made = Slots(s, 1, 0, "delay turn 1", ("c1", 2, 1, "next", null), ("c2", 3, 2, "after", null));
        Column(made.Next, "next", 2, 2, 0, 3, 2, 1, false, "delay turn 1"); Column(made.After, "after", 3, 1, 0, 2, 1, 1, false, "delay turn 1");
        var first = End(s);
        Check(first.Events.Any(e => e.E == "delayRes" && e.Uid == "c2" && e.From == 3 && e.To == 4 && e.Kind == "after" && e.Discarded == false), "the real enemy delayRes moved the newest reservation c2 from 3 to 4");
        // c2 would have been due next turn; delayed it is due 4 = Turn + 2 and counts in the after column only.
        var delayed = Slots(s, 2, 0, "delay turn 2", ("c2", 4, 2, "after", true));
        Column(delayed.Next, "next", 3, 2, 0, 3, 2, 0, false, "delay turn 2"); Column(delayed.After, "after", 4, 1, 0, 2, 1, 1, false, "delay turn 2");
        Play(s, "c3", "next");
        var second = End(s);
        Check(second.Events.Any(e => e.E == "delayRes" && e.Uid == "c3" && e.From == 3 && e.To == 4 && e.Kind == "next" && e.Discarded == false), "the second delayRes moved c3 from 3 to 4");
        // Both due 4 = Turn + 1: Used counts them whatever their kinds, which stay after and next.
        var mixed = Slots(s, 3, 0, "delay turn 3", ("c2", 4, 2, "after", true), ("c3", 4, 3, "next", true));
        Column(mixed.Next, "next", 4, 2, 0, 3, 2, 2, false, "delay turn 3 mixed kinds"); Column(mixed.After, "after", 5, 1, 0, 2, 1, 0, false, "delay turn 3");
        Rejects(s, "c1", "next", "SLOT_FULL", "delay turn 3 next used 2 of cap 2");
        Receipts(s, "delayed reservations");
    }

    static void EnemySeal(string root)
    {
        var s = Open(root, "t_sealer", C("t_plain"));
        Begin(s);
        Column(Slots(s, 1, 0, "enemy seal turn 1").Next, "next", 2, 2, 0, 3, 2, 0, false, "enemy seal turn 1");
        End(s); // sealSlot: the following player turn's next column
        Column(Slots(s, 2, 2, "enemy seal turn 2").Next, "next", 3, 2, 0, 3, 1, 0, true, "enemy seal turn 2");
        Play(s, "c1", "next");
        Column(Slots(s, 2, 2, "enemy seal turn 2 used", ("c1", 3, 1, "next", null)).Next, "next", 3, 2, 0, 3, 1, 1, true, "enemy seal turn 2 used");
        End(s); // charge
        Column(Slots(s, 3, 2, "enemy seal turn 3").Next, "next", 4, 2, 0, 3, 2, 0, false, "enemy seal turn 3 expired");
        End(s); // sealSlot again
        Column(Slots(s, 4, 4, "enemy seal turn 4").Next, "next", 5, 2, 0, 3, 1, 0, true, "enemy seal turn 4 reapplied");
        Receipts(s, "enemy seal");
    }

    // ───────── pick sheets ─────────
    static void HandSourceGate(string root)
    {
        var s = Open(root, "t_dummy", C("t_plain"), C("t_cheap"), C("t_exhaust"), C("t_tune"), C("t_solo"));
        foreach (string uid in new[] { "c1", "c4", "c5" })
            Refused(Choices(s, uid, "now", new[] { "c2" }, "created " + uid), uid, "now", "NOT_IN_HAND", "created: draw-pile " + uid);
        Begin(s); Play(s, "c1", "next"); Play(s, "c2", "now"); Play(s, "c3", "now");
        var zones = s.Battle.State;
        Check(zones.Reservations.Select(r => r.Uid).SequenceEqual(new[] { "c1" }) && zones.Zones.Discard.SequenceEqual(new[] { "c2" }) && zones.Zones.Exhaust.SequenceEqual(new[] { "c3" })
            && zones.Zones.Hand.OrderBy(u => u, StringComparer.Ordinal).SequenceEqual(new[] { "c4", "c5" }) && zones.Enemies.Single().Uid == "e6", "gate fixture zones");
        foreach (var (uid, zone) in new[] { ("c1", "reservation"), ("c2", "discard pile"), ("c3", "exhaust pile"), ("e6", "enemy uid"), ("c99", "unknown uid"), ("", "empty uid") })
            foreach (string mode in new[] { "now", "next", "sideways" })
                Refused(Choices(s, uid, mode, new[] { "c4" }, zone + " " + mode), uid, mode, "NOT_IN_HAND", zone + " " + mode);
        Refused(Choices(s, "c4", "sideways", None, "hand bad mode"), "c4", "sideways", "INVALID_MODE", "a hand card in an unknown mode");
        Expect(Choices(s, "c4", "now", None, "tune before removal"), null, 0, 0, 1, null, None, "tune: the only other hand card is a curse, so nothing is offered");
        // TEST-ONLY: drop the definitions of the discarded c2 and the hand curse c5 from this variant after the commands.
        s.Data.Cards.RemoveAll(c => c.Id == "t_cheap" || c.Id == "t_solo"); s.Data.Index();
        Refused(Choices(s, "c2", "now", None, "removed non-hand"), "c2", "now", "NOT_IN_HAND", "a non-hand card without a definition: membership is decided before any definition is read");
        Refused(Choices(s, "c5", "now", None, "removed hand"), "c5", "now", "UNKNOWN_CARD", "a hand card without a definition reports Core's rule code");
        Refused(Choices(s, "c5", "sideways", None, "removed hand bad mode"), "c5", "sideways", "INVALID_MODE", "the mode is checked before the definition");
        Expect(Choices(s, "c4", "now", None, "tune after removal"), null, 0, 0, 1, null, new[] { "c5" }, "a card the definitions no longer hold cannot be told and stays offered");

        // A real draw-pile card during a player turn: 11 cards, at most 10 drawn.
        var draw = Open(root, "t_dummy", Enumerable.Range(0, 11).Select(_ => C("t_plain")).ToArray());
        Begin(draw); string hidden = draw.Battle.State.Zones.Draw.Single();
        Refused(Choices(draw, hidden, "now", None, "draw pile"), hidden, "now", "NOT_IN_HAND", "a draw-pile card in a player turn");
        Receipts(s, "gate"); Receipts(draw, "gate draw pile");
    }

    static void TuneCurseRecall(string root)
    {
        var s = Open(root, "t_dummy", C("t_tune"), C("t_tune"), C("t_plain"), C("t_curse"), C("t_recall"), C("t_plain"), C("t_curse"));
        Begin(s);
        var first = Choices(s, "c1", "now", None, "tune opening");
        Requests(first, "tune opening", TuneRequest);
        Expect(first, null, 0, 0, 1, null, Hand(s, "c2", "c3", "c5", "c6"), "tune offers the other non-curse hand cards without the part");
        var unruled = BattleChoicePolicy.PickCandidates(s.Battle.State, "c1", (Tuned, "hand", 1, false), None, s.Data.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal));
        Check(unruled.SequenceEqual(Hand(s, "c2", "c3", "c4", "c5", "c6", "c7")), "without a rule the sheet is the whole zone minus the source (curses included)");
        Play(s, "c2", "now", "c3");
        Check(s.Battle.State.Cards["c3"].Temp.SequenceEqual(new[] { Part }), "the real tune attached the part to the picked c3");
        Expect(Choices(s, "c1", "now", None, "tune after tuning"), null, 0, 0, 1, null, Hand(s, "c5", "c6"), "a card already carrying the part is no longer offered");
        Expect(Choices(s, "c1", "now", new[] { "c3" }, "tune tuned pick"), "PICK_INVALID", 0, 0, 1, null, Hand(s, "c5", "c6"), "the tuned card is refused");
        Expect(Choices(s, "c1", "now", new[] { "c7" }, "tune curse pick"), "PICK_INVALID", 0, 0, 1, null, Hand(s, "c5", "c6"), "a curse is refused");
        Expect(Choices(s, "c1", "now", new[] { "c1" }, "tune source pick"), "PICK_INVALID", 0, 0, 1, null, Hand(s, "c5", "c6"), "the source itself is refused");
        Expect(Choices(s, "c1", "now", new[] { "c2" }, "tune discard pick"), "PICK_INVALID", 0, 0, 1, null, Hand(s, "c5", "c6"), "a discard-pile card is refused");
        Expect(Choices(s, "c1", "now", new[] { "c5" }, "tune valid pick"), null, 1, 1, 0, null, None, "one valid pick fills the only request");
        Expect(Choices(s, "c1", "now", new[] { "c5", "c6" }, "tune extra pick"), "PICK_INVALID", 1, 1, 0, null, None, "a pick beyond the requests is refused after the accepted one");
        Requests(Choices(s, "c1", "next", None, "tune next"), "tune in mode next asks for no pick");
        Play(s, "c4", "now"); // a curse into the discard pile
        Check(s.Battle.State.Zones.Discard.SequenceEqual(new[] { "c2", "c4" }), "recall fixture discard pile [c2, c4]");
        var recall = Choices(s, "c5", "now", None, "recall");
        Requests(recall, "recall", ("손으로 가져올 카드", "discard", 1, false, "recall", null, true));
        Expect(recall, null, 0, 0, 1, null, new[] { "c2" }, "recall offers the non-curse discard card only");
        Expect(Choices(s, "c5", "now", new[] { "c4" }, "recall curse"), "PICK_INVALID", 0, 0, 1, null, new[] { "c2" }, "a curse recall pick is refused");
        Expect(Choices(s, "c5", "now", new[] { "c3" }, "recall hand"), "PICK_INVALID", 0, 0, 1, null, new[] { "c2" }, "a hand card is no recall pick");
        Expect(Choices(s, "c5", "now", new[] { "c2" }, "recall valid"), null, 1, 1, 0, null, None, "the discard card fills the recall");
        var reserve = Choices(s, "c3", "next", None, "plain next");
        Requests(reserve, "a next reservation asks for no pick"); Expect(reserve, null, 0, 0, 0, null, None, "plain next");
        Expect(Choices(s, "c6", "after", new[] { "c5" }, "plain after pick"), "PICK_INVALID", 0, 0, 0, null, None, "a pick where no request exists is refused");
        Expect(Choices(s, "c6", "now", None, "plain now"), null, 0, 0, 0, null, None, "a card without pick effects asks for none");
        Receipts(s, "tune/curse/recall");
    }

    static void CostdownFreeNext(string root)
    {
        var s = Open(root, "t_dummy", C("t_costdown"), C("t_costdown_late"), C("t_plain"), C("t_cheap"), C("t_cond"), C("t_free2"), C("t_free1"));
        Begin(s);
        var b = s.Battle;
        Check(b.State.FreeNext == 0 && b.CostOf("c3") == 1 && b.CostOf("c4") == 0 && b.CostOf("c5") == 2 && b.CostOf("c2") == 0, "FreeNext 0 prices: c3 1, c4 0, c5 2, c2 0");
        var none = Choices(s, "c1", "now", None, "costdown FreeNext 0");
        Requests(none, "costdown", CostdownRequest);
        Expect(none, null, 0, 0, 1, null, Hand(s, "c3", "c5"), "FreeNext 0: Core's CostOf above 0 (c3) plus the conditional-cost card kept as unknown (c5)");
        var late = Choices(s, "c2", "now", None, "late costdown");
        Requests(late, "late costdown", ("비용을 낮출 카드", "hand", 1, false, "costdown", null, false));
        Expect(late, null, 0, 0, 1, null, Hand(s, "c1", "c3", "c4", "c5", "c6", "c7"), "a costdown after another effect keeps the whole hand (outside the known scope)");
        Play(s, "c7", "now");
        Check(b.State.FreeNext == 1 && b.CostOf("c3") == 0 && b.CostOfWithoutNextFree("c3") == 1 && b.CostOfWithoutNextFree("c4") == 0, "FreeNext 1: CostOf 0, CostOfWithoutNextFree c3 1 / c4 0");
        Expect(Choices(s, "c1", "now", None, "costdown FreeNext 1"), null, 0, 0, 1, null, Hand(s, "c3", "c5"),
            "FreeNext 1: the play's own commit spends the one charge, so the sheet prices with CostOfWithoutNextFree");
        Expect(Choices(s, "c1", "now", new[] { "c3" }, "costdown FreeNext 1 pick"), null, 1, 1, 0, null, None, "FreeNext 1: c3 is accepted");
        Play(s, "c6", "now");
        Check(b.State.FreeNext == 2 && b.CostOf("c3") == 0 && b.CostOfWithoutNextFree("c3") == 1, "FreeNext 2: CostOf 0, CostOfWithoutNextFree c3 1");
        Expect(Choices(s, "c1", "now", None, "costdown FreeNext 2"), null, 0, 0, 1, null, Hand(s, "c5"),
            "FreeNext 2: a charge still remains after the commit, every CostOf is 0, only the unknown conditional-cost card stays");
        Expect(Choices(s, "c1", "now", new[] { "c3" }, "costdown FreeNext 2 pick"), "PICK_INVALID", 0, 0, 1, null, Hand(s, "c5"), "FreeNext 2: c3 is refused");
        Expect(Choices(s, "c2", "now", None, "late costdown FreeNext 2"), null, 0, 0, 1, null, Hand(s, "c1", "c3", "c4", "c5"), "the late costdown still keeps the whole hand");
        Receipts(s, "costdown/FreeNext");
    }

    static void GrowthEvolutionCap(string root)
    {
        var s = Open(root, "t_dummy", C("t_growpick"), C("t_growpick"), C("t_growpick"), C("t_growpick"), C("t_copypick"),
            C("t_grow"), C("t_grow", 3), C("t_evo", 0, "t_grow"), C("t_evo"));
        Begin(s);
        var grow = Choices(s, "c1", "now", None, "growPick");
        Requests(grow, "growPick", GrowRequest);
        Expect(grow, null, 0, 0, 1, null, Hand(s, "c6"), "growPick offers only the growth definition below growth 3");
        var copy = Choices(s, "c5", "now", None, "copyPick");
        Requests(copy, "copyPick", CopyRequest);
        Expect(copy, null, 0, 0, 1, null, Hand(s, "c6", "c7", "c8"), "copyPick offers growth instances: both growth definitions and the evolution from the growth root, not the same evolution without that provenance");
        var definitions = s.Data.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
        Check(BattleChoicePolicy.PickCandidates(s.Battle.State, "c5", ("사본을 만들 성장 카드", "hand", 1, true), None, definitions).SequenceEqual(Hand(s, "c6", "c7")),
            "without a rule a growth-only request lists growth definitions only (the evolution c8 is not one)");
        Expect(Choices(s, "c1", "now", new[] { "c7" }, "growPick maxed"), "PICK_INVALID", 0, 0, 1, null, Hand(s, "c6"), "growth 3 is refused");
        Expect(Choices(s, "c1", "now", new[] { "c8" }, "growPick evolution"), "PICK_INVALID", 0, 0, 1, null, Hand(s, "c6"), "an evolution is no growPick target");
        Play(s, "c1", "now", "c6"); Play(s, "c2", "now", "c6"); Play(s, "c3", "now", "c6");
        var grown = s.Battle.State;
        Check(grown.GrowthCount == 3 && grown.GrowthCap == 3 && grown.Cards["c6"].Growth == 3, "three real growPick plays reach the battle's growth cap 3 (c6 growth 3)");
        var capped = Choices(s, "c4", "now", None, "growPick at cap");
        Requests(capped, "growPick at cap", GrowRequest);
        Expect(capped, null, 0, 0, 1, "PICK_GROWTH_CAP", None, "at the cap growPick is blocked and offers nothing");
        Expect(Choices(s, "c4", "now", new[] { "c6" }, "growPick at cap pick"), "PICK_INVALID", 0, 0, 1, "PICK_GROWTH_CAP", None, "at the cap any pick is refused");
        Expect(Choices(s, "c5", "now", None, "copyPick at cap"), null, 0, 0, 1, null, Hand(s, "c6", "c7", "c8"), "copyPick is not capped by growth");
        Receipts(s, "growth/evolution/cap");
    }

    static void OrderedPicks(string root)
    {
        var s = Open(root, "t_dummy", C("t_multi"), C("t_plain"), C("t_plain"), C("t_curse"), C("t_cheap"), C("t_plain"));
        Begin(s); Play(s, "c5", "now"); Play(s, "c2", "next"); Play(s, "c6", "after");
        var zones = s.Battle.State;
        Check(zones.Zones.Discard.SequenceEqual(new[] { "c5" }) && zones.Reservations.Select(r => r.Uid).SequenceEqual(new[] { "c2", "c6" })
            && zones.Zones.Hand.OrderBy(u => u, StringComparer.Ordinal).SequenceEqual(new[] { "c1", "c3", "c4" }), "multi fixture: discard [c5], reservations [c2, c6], hand {c1, c3, c4}");
        var start = Choices(s, "c1", "now", None, "multi");
        Requests(start, "multi", ("버릴 카드", "hand", 2, false, "discard", null, true), ("버린 카드에서 소멸할 카드", "discard", 1, false, "exhaustPick", null, false),
            ("앞당길 예약", "res", 1, false, "pull", null, false));
        Expect(start, null, 0, 0, 2, null, Hand(s, "c3", "c4"), "first request: two hand discards (a curse too)");
        Expect(Choices(s, "c1", "now", new[] { "c3" }, "multi 1"), null, 1, 0, 1, null, Hand(s, "c4"), "an accepted pick leaves the list");
        Expect(Choices(s, "c1", "now", new[] { "c3", "c3" }, "multi dup"), "PICK_INVALID", 1, 0, 1, null, Hand(s, "c4"), "a repeated pick is refused");
        Expect(Choices(s, "c1", "now", new[] { "c4", "c3" }, "multi 2"), null, 2, 1, 1, null, new[] { "c5" }, "two picks fill the first request; the second lists the discard pile");
        Expect(Choices(s, "c1", "now", new[] { "c3", "c4", "c2" }, "multi wrong zone"), "PICK_INVALID", 2, 1, 1, null, new[] { "c5" }, "a reservation is not a discard-pile pick");
        Expect(Choices(s, "c1", "now", new[] { "c3", "c4", "c5" }, "multi 3"), null, 3, 2, 1, null, new[] { "c2", "c6" }, "the third request lists the reservations in state order");
        Expect(Choices(s, "c1", "now", new[] { "c3", "c4", "c5", "c6" }, "multi 4"), null, 4, 3, 0, null, None, "every request filled");
        Expect(Choices(s, "c1", "now", new[] { "c3", "c4", "c5", "c6", "c2" }, "multi 5"), "PICK_INVALID", 4, 3, 0, null, None, "a fifth pick is beyond the requests");
        Expect(Choices(s, "c1", "now", new[] { "c5" }, "multi early discard"), "PICK_INVALID", 0, 0, 2, null, Hand(s, "c3", "c4"), "a discard-pile card cannot fill the hand request");
        Receipts(s, "ordered picks");
    }

    // ───────── static policy on explicit BattleState fixtures ─────────
    static void StaticPolicy(string root)
    {
        BattleCardDef Def(string id, string rarity, int cost) => new BattleCardDef { Id = id, Name = id, Owner = "t_owner", Rarity = rarity, Cost = cost, Type = "skill", Reservation = "n", Attr = "none" };
        var definitions = new Dictionary<string, BattleCardDef>(StringComparer.Ordinal)
        {
            ["s_src"] = Def("s_src", "R", 0), ["s_paid"] = Def("s_paid", "R", 2), ["s_curse"] = Def("s_curse", "C", 1), ["s_cond"] = Def("s_cond", "R", 2),
            ["s_root"] = Def("s_root", "R", 1), ["s_evo"] = Def("s_evo", "R", 1), ["s_flat"] = Def("s_flat", "R", 1)
        };
        definitions["s_cond"].CostIf = new BattleCardCostIfDef { Condition = new EffectConditionDef { Condition = "debt" }, Delta = 1 };
        definitions["s_root"].Growth = true; definitions["s_root"].Evolutions = new List<string> { "s_evo" };
        var b = new BattleState { GrowthCap = 3 };
        void Put(string uid, string id, int growth = 0, string? from = null, params string[] temp)
        { b.Cards[uid] = new BattleCardState { Uid = uid, Id = id, Growth = growth, From = from, Temp = temp.ToList() }; b.Zones.Hand.Add(uid); }
        Put("h0", "s_src"); Put("h1", "s_paid", 0, null, "p1"); Put("h2", "s_curse"); Put("h3", "s_cond"); Put("h4", "s_root", 2); Put("h5", "s_root", 3);
        Put("h6", "s_evo", 0, "s_root"); Put("h7", "s_evo", 0, "s_flat"); Put("h8", "s_evo"); b.Zones.Hand.Add("ghost");
        // A hand-built fixture has no internal card order, so its cards are printed separately from the state graph.
        string Fixture() => Full(b.ToJsonGraph()) + Full(b.Cards.Values.Select(c => c.ToJsonGraph()).ToList());
        string state = Fixture(), defs = Full(definitions.Values.Select(d => d.ToJsonGraph()).ToList());
        var calls = new List<(string Uid, bool WithoutNextFree)>();
        int Price(string uid, bool withoutNextFree) { calls.Add((uid, withoutNextFree)); if (uid == "h4") throw new RuleException("BATTLE_NUMBER_OUT_OF_RANGE"); return uid == "h1" ? 2 : 0; }
        (string, string?, bool) Rule(string op, string? part = null, bool leading = true) => (op, part, leading);
        var hand = ("x", "hand", 1, false);

        // Core's price delegate: CostOf (false) unless exactly one charge remains, then CostOfWithoutNextFree (true).
        foreach (var (freeNext, flag) in new[] { (0, false), (1, true), (2, false), (3, false) })
        {
            b.FreeNext = freeNext; calls.Clear();
            Check(BattleChoicePolicy.EffectEligible(b, Rule("costdown"), "h1", definitions, Price) == true && calls.SequenceEqual(new[] { ("h1", flag) }),
                $"FreeNext {freeNext}: a leading costdown asks Core's price once with withoutNextFree {flag}");
        }
        b.FreeNext = 0; calls.Clear();
        Check(BattleChoicePolicy.EffectEligible(b, Rule("costdown"), "h2", definitions, Price) == false, "price 0: costdown cannot take it");
        Check(BattleChoicePolicy.EffectEligible(b, Rule("costdown"), "h4", definitions, Price) == null, "a price Core refuses (RuleException) is unknown, never 0");
        calls.Clear();
        Check(BattleChoicePolicy.EffectEligible(b, Rule("costdown"), "h3", definitions, Price) == null && calls.Count == 0, "a conditional cost is unknown and Core's price is not even asked");
        Check(BattleChoicePolicy.EffectEligible(b, Rule("costdown"), "h1", definitions, null) == null, "no price: unknown");
        Check(BattleChoicePolicy.EffectEligible(b, Rule("tune", "p1"), "ghost", definitions, Price) == null && BattleChoicePolicy.EffectEligible(b, Rule("recall"), "h0", new Dictionary<string, BattleCardDef>(), Price) == null,
            "a card the state or the definitions do not hold is unknown");
        calls.Clear();
        Check(BattleChoicePolicy.PickCandidates(b, "h0", hand, None, definitions, Rule("costdown"), Price).SequenceEqual(new[] { "h1", "h3", "h4", "ghost" }),
            "leading costdown: the priced-above-0 card plus every unknown (conditional cost, refused price, missing card) in zone order");
        calls.Clear();
        Check(BattleChoicePolicy.PickCandidates(b, "h0", hand, None, definitions, Rule("costdown", null, false), Price).SequenceEqual(new[] { "h1", "h2", "h3", "h4", "h5", "h6", "h7", "h8", "ghost" }) && calls.Count == 0,
            "a costdown that is not the first effect keeps the zone list and asks no price");
        Check(BattleChoicePolicy.PickCandidates(b, "h0", hand, None, definitions, Rule("costdown"), null).Count == 9, "without Core's price a leading costdown keeps the zone list");
        // tune / recall / curse; tune with a part already attached.
        Check(BattleChoicePolicy.PickCandidates(b, "h0", hand, None, definitions, Rule("tune", "p1"), Price).SequenceEqual(new[] { "h3", "h4", "h5", "h6", "h7", "h8", "ghost" }),
            "tune p1: not the curse h2, not h1 already carrying p1");
        Check(BattleChoicePolicy.PickCandidates(b, "h0", hand, None, definitions, Rule("tune", "p2"), Price).SequenceEqual(new[] { "h1", "h3", "h4", "h5", "h6", "h7", "h8", "ghost" }),
            "tune p2: h1 carries another part only");
        Check(BattleChoicePolicy.EffectEligible(b, Rule("recall"), "h2", definitions, Price) == false && BattleChoicePolicy.EffectEligible(b, Rule("recall"), "h1", definitions, Price) == true,
            "recall: a curse no, a rare yes");
        // growth / evolution provenance.
        Check(BattleChoicePolicy.PickCandidates(b, "h0", hand, None, definitions, Rule("growPick"), Price).SequenceEqual(new[] { "h4", "ghost" }), "growPick: the growth definition below 3 only (plus unknown)");
        Check(BattleChoicePolicy.PickCandidates(b, "h0", hand, None, definitions, Rule("copyPick"), Price).SequenceEqual(new[] { "h4", "h5", "h6", "ghost" }),
            "copyPick: growth definitions and the evolution from its growth root; not an evolution from a non-growth root (h7) or without provenance (h8)");
        Check(BattleChoicePolicy.PickBlocked(b, Rule("growPick")) == null, "below the cap growPick is not blocked");
        b.GrowthCount = 3;
        Check(BattleChoicePolicy.PickBlocked(b, Rule("growPick")) == "PICK_GROWTH_CAP" && BattleChoicePolicy.PickBlocked(b, Rule("copyPick")) == null
            && BattleChoicePolicy.PickCandidates(b, "h0", hand, None, definitions, Rule("growPick"), Price).Count == 0
            && BattleChoicePolicy.PickCandidates(b, "h0", hand, None, definitions, Rule("copyPick"), Price).Count == 4, "at the cap only growPick is blocked (and offers nothing)");
        b.GrowthCount = 0;
        // discard / exhaustPick / pull take any zone card; dedup of picked.
        Check(BattleChoicePolicy.PickCandidates(b, "h0", hand, new[] { "h2", "h5" }, definitions, Rule("discard"), Price).SequenceEqual(new[] { "h1", "h3", "h4", "h6", "h7", "h8", "ghost" }),
            "discard: the zone list minus the source and the cards already picked");
        // Ordered walk and PicksHold.
        var requests = new List<(string, string, int, bool)> { ("a", "hand", 2, false), ("b", "hand", 1, false) };
        var rules = new List<(string, string?, bool)> { Rule("discard"), Rule("recall", null, false) };
        Check(BattleChoicePolicy.WalkPicks(b, "h0", requests, rules, None, definitions, Price) == (0, 0, 2) && BattleChoicePolicy.WalkPicks(b, "h0", requests, rules, new[] { "h2", "h1" }, definitions, Price) == (2, 1, 1)
            && BattleChoicePolicy.WalkPicks(b, "h0", requests, rules, new[] { "h2", "h1", "h2" }, definitions, Price) == (2, 1, 1)
            && BattleChoicePolicy.WalkPicks(b, "h0", requests, rules, new[] { "h2", "h3", "h1" }, definitions, Price) == (3, 2, 0), "WalkPicks accepted/index/left (recall refuses the already-picked curse h2)");
        Check(BattleChoicePolicy.PicksHold(b, "h0", requests, rules, None, definitions, Price) && BattleChoicePolicy.PicksHold(b, "h0", requests, rules, new[] { "h2" }, definitions, Price)
            && !BattleChoicePolicy.PicksHold(b, "h0", requests, rules, new[] { "h2", "h3", "h1", "h4" }, definitions, Price)
            && !BattleChoicePolicy.PicksHold(b, "h0", requests, rules, new[] { "h0" }, definitions, Price), "PicksHold: fewer picks are an explicit auto; extra or invalid picks fail");
        // Requests and rules metadata.
        var content = new BattleData(); content.Parts.Add(new BattlePartDef { Id = "p1", Name = "톱니" });
        var card = Def("s_meta", "R", 0);
        card.Effects = new List<EffectDef> { new EffectDef { Op = "blk", V = 1 }, new EffectDef { Op = "tune", Part = "p1", Pick = "one" }, new EffectDef { Op = "tune", Part = "p1", Pick = "all" },
            new EffectDef { Op = "tune", Part = "p1", Pick = "random" }, new EffectDef { Op = "tune", Part = "p1", Pick = "same" }, new EffectDef { Op = "costdown", N = 1, Pick = "all" },
            new EffectDef { Op = "exhaustPick", N = 3 }, new EffectDef { Op = "exhaustPick", From = "discard", N = 0 }, new EffectDef { Op = "discard" }, new EffectDef { Op = "tune", Part = "p9", Pick = "one" } };
        Check(BattleChoicePolicy.ChoiceRequests(card, "now", content).SequenceEqual(new[] { ("「톱니」을 붙일 카드", "hand", 1, false), ("손에서 소멸할 카드", "hand", 3, false),
            ("버린 카드에서 소멸할 카드", "discard", 1, false), ("버릴 카드", "hand", 1, false), ("「부품」을 붙일 카드", "hand", 1, false) }), "ChoiceRequests: tune one only, counts at least 1, unknown part named 부품");
        Check(BattleChoicePolicy.ChoiceRequests(card, "now")[0].Label == "「부품」을 붙일 카드" && BattleChoicePolicy.ChoiceRequests(card, "next").Count == 0 && BattleChoicePolicy.ChoiceRequests(card, "after").Count == 0,
            "no content: part named 부품; next/after ask for no pick");
        Check(BattleChoicePolicy.ChoiceRules(card, "now").SequenceEqual(new (string, string?, bool)[] { ("tune", "p1", false), ("exhaustPick", null, false), ("exhaustPick", null, false), ("discard", null, false), ("tune", "p9", false) }),
            "ChoiceRules: Leading only for the card's first top-level effect (here blk)");
        var leading = Def("s_lead", "R", 0); leading.Effects = new List<EffectDef> { new EffectDef { Op = "recall" }, new EffectDef { Op = "pull" } };
        Check(BattleChoicePolicy.ChoiceRules(leading, "now").SequenceEqual(new (string, string?, bool)[] { ("recall", null, true), ("pull", null, false) }), "ChoiceRules: the first effect leads");
        var once = BattleChoicePolicy.PickCandidates(b, "h0", hand, None, definitions, Rule("discard"), Price); once.Clear();
        Check(BattleChoicePolicy.PickCandidates(b, "h0", hand, None, definitions, Rule("discard"), Price).Count == 9, "static answers are fresh lists");
        Check(Fixture() == state && Full(definitions.Values.Select(d => d.ToJsonGraph()).ToList()) == defs, "the static policy wrote neither the fixture state (FreeNext/GrowthCount restored) nor the definitions");
    }
}
