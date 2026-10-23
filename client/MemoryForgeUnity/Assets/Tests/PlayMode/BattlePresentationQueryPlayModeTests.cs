// P1-PUBLIC-BATTLE-QUERY §17.4/§14.7/§14.3 in actual PlayMode/Mono: the new Core Battle.ReservationSlots / Battle.HandChoices, the shared
// BattleChoicePolicy, the client's forwarding wrappers (MemoryForgeExpeditionClient.ChoiceRequests/ChoiceRules/PickCandidates/
// PickBlocked/EffectEligible) and the Run forwarders (BattleReservationSlots/BattleHandChoices).
// Battles start from Battle.Create over freshly loaded Resources battle content (ClientBattleContent.Load) with ONE explicitly TEST-ONLY
// in-memory variant: test "t_" cards, one part and two enemies appended and the hand draw raised to 10, so each small deck is drawn
// whole (only the order inside the hand is the shuffle's and is read from the snapshot). Shipping Resources/export are not changed.
// The Run test uses the real exported content and PracticeSetup without any variant. Expected values are hand-derived literals.
// Not Scene/prefab/UI, server or device acceptance.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MemoryForge.Core;
using NUnit.Framework;
using UnityEngine;
using CoreRun = MemoryForge.Core.Run;

namespace MemoryForge.Client.Tests
{
    public class BattlePresentationQueryPlayModeTests
    {
        const string Part = "t_part", Tuned = "「시험 부품」을 붙일 카드";
        static readonly string[] None = new string[0];

        sealed class Session
        {
            public Battle Battle = null!;
            public BattleData Data = null!;
            public readonly List<(BattleRequest Request, string Response)> Receipts = new List<(BattleRequest, string)>();
            public int Sent;
        }

        // ───────── TEST-ONLY content ─────────
        static BattleData Variant()
        {
            var data = ClientBattleContent.Load().Battle;
            data.Rules.HandDraw = 10; // TEST-ONLY: decks of at most 10 are drawn whole every turn
            data.Parts.Add(new BattlePartDef { Id = Part, Slot = "circuit", Name = "시험 부품", Rarity = "R", Text = "TEST-ONLY" });
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
            Card("t_growpick", 0, "n", new EffectDef { Op = "growPick", N = 1 });
            Card("t_copypick", 0, "n", new EffectDef { Op = "copyPick" });
            Card("t_multi", 0, "n", new EffectDef { Op = "discard", N = 2 }, new EffectDef { Op = "exhaustPick", From = "discard", N = 1 }, new EffectDef { Op = "pull" });
            Card("t_free1", 0, "n", new EffectDef { Op = "freeNext", N = 1 }); Card("t_free2", 0, "n", new EffectDef { Op = "freeNext", N = 2 });
            Card("t_slot", 0, "n", new EffectDef { Op = "slot", Next = 1, After = 1 });
            // RULES_NOW_PLAY (2026-10-19): collateral is block + debt 1 and seals nothing, so no test card seals; the enemy t_sealer does.
            void Enemy(string id, params string[] moves) => data.Enemies.Add(new BattleEnemyDef { Id = id, Name = "시험 적", Tier = "normal", Hp = 500, Brk = 50,
                Weak = new List<string> { "frost" }, Pattern = moves.Select(m => new EnemyMoveDef { Type = m }).ToList() });
            Enemy("t_dummy", "charge"); Enemy("t_delay", "delayRes"); Enemy("t_sealer", "sealSlot", "charge");
            data.Index(); return data;
        }
        static BattleCardSpec C(string id, int growth = 0, string? from = null) => new BattleCardSpec { Id = id, Growth = growth, From = from };
        // Deck cards get c1.. in deck order and the one enemy the next sequence number.
        static Session Open(string enemy, params BattleCardSpec[] deck)
        {
            var data = Variant();
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
            var request = new BattleRequest("pq-mono-" + ++s.Sent, s.Battle.State.V, kind, args);
            var response = s.Battle.Command(request); s.Receipts.Add((request, CanonicalState.Serialize(response.ToJsonGraph()))); return response;
        }
        static void Begin(Session s) => Send(s, "begin");
        static void End(Session s) => Send(s, "end");
        static void Play(Session s, string uid, string mode, params string[] picks) => Send(s, "play", Args(uid, mode, picks));
        static void Rejects(Session s, string uid, string mode, string code, string label)
        {
            string before = Fingerprint(s);
            var error = Assert.Throws<RuleException>(() => s.Battle.Command(new BattleRequest("pq-mono-reject-" + ++s.Sent, s.Battle.State.V, "play", Args(uid, mode, None))), label);
            Assert.That(error!.Code, Is.EqualTo(code), label);
            Assert.That(Fingerprint(s), Is.EqualTo(before), label + ": the rejection changes nothing");
        }
        static void ReceiptsReplay(Session s)
        {
            // The reflected private map holds exactly the receipts this test stored, so the fingerprint reads the real map.
            var keys = ReceiptTable(s.Battle).Keys.Cast<string>().ToList();
            Assert.That(keys, Is.EquivalentTo(s.Receipts.Select(r => r.Request.Rid).ToList()), "the private receipt map holds exactly the stored receipts");
            string before = Fingerprint(s);
            foreach (var (request, response) in s.Receipts)
                Assert.That(CanonicalState.Serialize(s.Battle.Command(request).ToJsonGraph()), Is.EqualTo(response), "stored receipt " + request.Rid + " replays its original response after the queries");
            Assert.That(Fingerprint(s), Is.EqualTo(before), "receipt replays execute nothing and add, drop or change no receipt");
        }

        // ───────── purity / detachment ─────────
        // State graph (version, RNG, log, events, zones, cards, reservations) + public view + definitions + the entire private receipt map.
        static string Fingerprint(Session s) => CanonicalState.Serialize(s.Battle.State.ToJsonGraph()) + CanonicalState.Serialize(s.Battle.PublicView())
            + CanonicalState.Serialize(s.Data.Cards.Select(c => (object?)c.ToJsonGraph()).Concat(s.Data.Parts.Select(p => (object?)p.ToJsonGraph())).ToList())
            + "\n" + ReceiptMap(s.Battle);
        // TEST-ONLY read-only reflection over Battle's private receipt map (outside the state graph); nothing is written, restored or injected.
        static IDictionary ReceiptTable(Battle battle) =>
            typeof(Battle).GetField("receipts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(battle) as IDictionary ?? throw new InvalidOperationException("Battle.receipts map not readable");
        // The entire map: count, then every key with its stored identity and response, in map order.
        static string ReceiptMap(Battle battle)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var map = ReceiptTable(battle); var rows = new List<string> { map.Count.ToString(CultureInfo.InvariantCulture) };
            foreach (DictionaryEntry entry in map)
            {
                object receipt = entry.Value ?? throw new InvalidOperationException("null receipt");
                var type = receipt.GetType();
                string identity = type.GetField("Identity", flags)?.GetValue(receipt) as string ?? throw new InvalidOperationException("receipt identity not readable");
                var response = type.GetField("Response", flags)?.GetValue(receipt) as BattleResponse ?? throw new InvalidOperationException("receipt response not readable");
                rows.Add((string)entry.Key + "␟" + identity + "␟" + CanonicalState.Serialize(response.ToJsonGraph()));
            }
            return string.Join("\n", rows);
        }
        static string Print(BattleSlotColumn c) => $"{c.Kind}/{c.Due}/{c.Base}/{c.Temp}/{c.Max}/{c.Cap}/{c.Used}/{c.Sealed}";
        static string Print(BattleSlotView v) => $"{v.Turn}|{v.SealedUntil}|{Print(v.Next)}|{Print(v.After)}|" +
            string.Join(";", v.Reservations.Select(r => $"{r.Uid},{r.Due},{r.Order},{r.Target},{r.Kind},{r.Delayed}"));
        static string Print(BattleChoiceView v) => $"{v.Error}|{v.Uid}|{v.Mode}|{v.Accepted}|{v.Index}|{v.Left}|{v.Blocked}|[{string.Join(",", v.Candidates)}]|" +
            string.Join(";", v.Requests.Select(r => $"{r.Label},{r.From},{r.Count},{r.GrowthOnly},{r.Op},{r.Part},{r.Leading}"));

        static BattleSlotView Slots(Session s, int turn, int sealedUntil, string label, params (string Uid, int Due, int Order, string Kind, bool? Delayed)[] reservations)
        {
            string before = Fingerprint(s);
            var view = s.Battle.ReservationSlots(); string printed = Print(view);
            Assert.That(Fingerprint(s), Is.EqualTo(before), label + ": ReservationSlots is pure");
            Assert.That((view.Turn, view.SealedUntil), Is.EqualTo((turn, sealedUntil)), label);
            Assert.That(view.Reservations.Select(r => (r.Uid, r.Due, r.Order, r.Kind, r.Delayed)).ToList(), Is.EqualTo(reservations.ToList()), label + ": every reservation with its own due/order/kind/delay flag");
            var probe = s.Battle.ReservationSlots();
            Assert.That(Print(probe), Is.EqualTo(printed)); Assert.That(probe.Reservations, Is.Not.SameAs(view.Reservations));
            Assert.That(probe.Next, Is.Not.SameAs(view.Next)); Assert.That(probe.After, Is.Not.SameAs(view.After));
            Assert.That(probe.Reservations.Count, Is.EqualTo(view.Reservations.Count));
            for (int i = 0; i < probe.Reservations.Count; i++) Assert.That(probe.Reservations[i], Is.Not.SameAs(view.Reservations[i]), label + ": reservation " + i + " is a fresh object");
            probe.Turn = -1; probe.SealedUntil = -1; probe.Next.Cap = -9; probe.Next.Sealed = !probe.Next.Sealed; probe.After.Used = 99; probe.After.Kind = "caller-mutation";
            foreach (var r in probe.Reservations) { r.Uid = "caller-mutation"; r.Due = -1; r.Order = -1; r.Kind = "caller-mutation"; r.Target = "caller-mutation"; r.Delayed = false; }
            // Before any further query: a shared or cached column/reservation object would show here.
            Assert.That(Print(view), Is.EqualTo(printed), label + ": mutating a second answer's columns and reservations leaves the first answer unchanged");
            probe.Reservations.Clear();
            Assert.That(Print(view), Is.EqualTo(printed), label + ": clearing a second answer's list leaves the first answer unchanged");
            Assert.That(Print(s.Battle.ReservationSlots()), Is.EqualTo(printed), label + ": the answer is detached");
            Assert.That(Fingerprint(s), Is.EqualTo(before), label + ": a mutated answer never reaches the state");
            return view;
        }
        static void Column(BattleSlotColumn c, string kind, long due, int @base, int temp, int max, int cap, int used, bool @sealed, string label) =>
            Assert.That(Print(c), Is.EqualTo($"{kind}/{due}/{@base}/{temp}/{max}/{cap}/{used}/{@sealed}"), label + " " + kind + " column (kind/due/base/temp/max/cap/used/sealed)");

        static BattleChoiceView Choices(Session s, string uid, string mode, string[] picks, string label)
        {
            string before = Fingerprint(s); var supplied = new List<string>(picks);
            var view = s.Battle.HandChoices(uid, mode, supplied); string printed = Print(view);
            Assert.That(Fingerprint(s), Is.EqualTo(before), label + ": HandChoices is pure");
            Assert.That(supplied, Is.EqualTo(picks), label + ": the caller's picks are not changed");
            supplied.Clear(); supplied.Add("caller-mutation");
            var probe = s.Battle.HandChoices(uid, mode, picks);
            Assert.That(Print(view), Is.EqualTo(printed), label + ": the caller's picks are not retained");
            Assert.That(Print(probe), Is.EqualTo(printed)); Assert.That(probe.Candidates, Is.Not.SameAs(view.Candidates)); Assert.That(probe.Requests, Is.Not.SameAs(view.Requests));
            Assert.That(probe.Requests.Count, Is.EqualTo(view.Requests.Count));
            for (int i = 0; i < probe.Requests.Count; i++) Assert.That(probe.Requests[i], Is.Not.SameAs(view.Requests[i]), label + ": request " + i + " is a fresh object");
            probe.Error = "caller-mutation"; probe.Blocked = "caller-mutation"; probe.Accepted = -1; probe.Index = -1; probe.Left = -1; probe.Candidates.Add("caller-mutation");
            foreach (var r in probe.Requests)
            {
                r.Label = "caller-mutation"; r.From = "caller-mutation"; r.Op = "caller-mutation"; r.Part = "caller-mutation";
                r.Count = -1; r.GrowthOnly = !r.GrowthOnly; r.Leading = !r.Leading;
            }
            // Before any further query: a shared or cached request/list object would show here.
            Assert.That(Print(view), Is.EqualTo(printed), label + ": mutating a second answer's lists and requests leaves the first answer unchanged");
            probe.Requests.Add(new BattleChoiceRequest { Label = "caller-mutation" });
            Assert.That(Print(view), Is.EqualTo(printed), label + ": growing a second answer's request list leaves the first answer unchanged");
            Assert.That(Print(s.Battle.HandChoices(uid, mode, picks)), Is.EqualTo(printed), label + ": the answer is detached");
            return view;
        }
        static void Expect(BattleChoiceView v, string? error, int accepted, int index, int left, string? blocked, IEnumerable<string> candidates, string label)
        {
            Assert.That((v.Error, v.Accepted, v.Index, v.Left, v.Blocked), Is.EqualTo((error, accepted, index, left, blocked)), label + " (error, accepted, index, left, blocked)");
            Assert.That(v.Candidates, Is.EqualTo(candidates.ToList()), label + " candidates");
        }
        static void Refused(BattleChoiceView v, string uid, string mode, string code, string label) =>
            Assert.That(Print(v), Is.EqualTo(code + "|" + uid + "|" + mode + "|0|0|0||[]|"), label + ": exactly " + code + " with every other field default");
        static List<string> Hand(Session s, params string[] members)
        {
            var hand = s.Battle.State.Zones.Hand;
            Assert.That(members.All(hand.Contains), Is.True, "fixture: [" + string.Join(",", members) + "] all in hand [" + string.Join(",", hand) + "]");
            return hand.Where(members.Contains).ToList();
        }

        // ───────── reservation board ─────────
        // RULES_NOW_PLAY (2026-10-19): the live content has no reservable card any more (every card is res 'n'), so this board runs only on
        // the TEST-ONLY "y" cards above, against Core's still-present reservation code (removed in phase 2). Collateral no longer seals, so
        // the seal (a sealed next column, its expiry and its reapplication) comes from the t_sealer enemy's sealSlot, as in CoreChecks'
        // EnemySeal. Rules used for every literal: next base 2 / max 3, after base 1 / max 2, Cap = min(Max, Base + Temp), minus 1 on next
        // while SealedUntil >= Turn; slot temp is clamped to Max - Base; AP 4 per turn; t_plain costs 1, t_slot and t_cheap 0.
        [Test]
        public void ReservationBoardFollowsCoreCapacityThroughTempSealExpiryAndDelay()
        {
            Assert.That(Application.isPlaying, Is.True, "must run inside actual PlayMode");
            var s = Open("t_dummy", C("t_plain"), C("t_plain"), C("t_plain"), C("t_plain"), C("t_slot"), C("t_cheap"));
            var created = Slots(s, 0, 0, "created");
            Column(created.Next, "next", 1, 2, 0, 3, 1, 0, true, "created (SealedUntil 0 >= Turn 0)"); Column(created.After, "after", 2, 1, 0, 2, 1, 0, false, "created");
            Begin(s);
            Column(Slots(s, 1, 0, "turn 1").Next, "next", 2, 2, 0, 3, 2, 0, false, "turn 1");
            Play(s, "c5", "now"); // temp next +1 (clamped to 3 - 2), after +1 (clamped to 2 - 1)
            var widened = Slots(s, 1, 0, "turn 1 temp slots");
            Column(widened.Next, "next", 2, 2, 1, 3, 3, 0, false, "turn 1 temp slots"); Column(widened.After, "after", 3, 1, 1, 2, 2, 0, false, "turn 1 temp slots");
            Play(s, "c1", "next"); Play(s, "c2", "next"); Play(s, "c6", "next"); // AP 4 - 1 - 1 - 0 = 2 left
            Rejects(s, "c3", "next", "SLOT_FULL", "turn 1 next used 3 of widened cap 3");
            Play(s, "c3", "after"); Play(s, "c4", "after"); // AP 0
            var full = Slots(s, 1, 0, "turn 1 full", ("c1", 2, 1, "next", null), ("c2", 2, 2, "next", null), ("c6", 2, 3, "next", null), ("c3", 3, 4, "after", null), ("c4", 3, 5, "after", null));
            Column(full.Next, "next", 2, 2, 1, 3, 3, 3, false, "turn 1 full"); Column(full.After, "after", 3, 1, 1, 2, 2, 2, false, "turn 1 full");
            End(s);
            // c1/c2/c6 resolved at turn-2 start and the temp slots expired; both after reservations are due next turn, kind kept: Used 2 = Cap 2.
            var carried = Slots(s, 2, 0, "turn 2", ("c3", 3, 4, "after", null), ("c4", 3, 5, "after", null));
            Column(carried.Next, "next", 3, 2, 0, 3, 2, 2, false, "turn 2: temp expired, carried after reservations fill next, kind after kept");
            Column(carried.After, "after", 4, 1, 0, 2, 1, 0, false, "turn 2");
            Rejects(s, "c1", "next", "SLOT_FULL", "turn 2 next used 2 of cap 2");
            Play(s, "c1", "after");
            End(s);
            var expired = Slots(s, 3, 0, "turn 3", ("c1", 4, 6, "after", null));
            Column(expired.Next, "next", 4, 2, 0, 3, 2, 1, false, "turn 3 carried after reservation"); Column(expired.After, "after", 5, 1, 0, 2, 1, 0, false, "turn 3");
            ReceiptsReplay(s);

            // The seal: the enemy's sealSlot seals the following player turn's next column, it expires after that turn and is reapplied.
            var e = Open("t_sealer", C("t_plain"));
            Begin(e);
            Column(Slots(e, 1, 0, "enemy seal turn 1").Next, "next", 2, 2, 0, 3, 2, 0, false, "enemy seal turn 1");
            End(e); // sealSlot
            Column(Slots(e, 2, 2, "enemy seal turn 2").Next, "next", 3, 2, 0, 3, 1, 0, true, "enemy seal turn 2");
            Play(e, "c1", "next");
            Column(Slots(e, 2, 2, "enemy seal turn 2 used", ("c1", 3, 1, "next", null)).Next, "next", 3, 2, 0, 3, 1, 1, true, "enemy seal turn 2 used");
            End(e); // charge
            Column(Slots(e, 3, 2, "enemy seal turn 3").Next, "next", 4, 2, 0, 3, 2, 0, false, "enemy seal turn 3 expired");
            End(e); // sealSlot again
            Column(Slots(e, 4, 4, "enemy seal turn 4").Next, "next", 5, 2, 0, 3, 1, 0, true, "enemy seal turn 4 reapplied");
            ReceiptsReplay(e);

            var d = Open("t_delay", C("t_plain"), C("t_plain"), C("t_plain"));
            Begin(d); Play(d, "c1", "next"); Play(d, "c2", "after"); End(d);
            var delayed = Slots(d, 2, 0, "delay turn 2", ("c2", 4, 2, "after", true));
            Column(delayed.Next, "next", 3, 2, 0, 3, 2, 0, false, "delay turn 2: the delayed after card is due 4, not next");
            Column(delayed.After, "after", 4, 1, 0, 2, 1, 1, false, "delay turn 2");
            Play(d, "c3", "next"); End(d);
            var mixed = Slots(d, 3, 0, "delay turn 3", ("c2", 4, 2, "after", true), ("c3", 4, 3, "next", true));
            Column(mixed.Next, "next", 4, 2, 0, 3, 2, 2, false, "delay turn 3: both kinds due 4 count in next"); Column(mixed.After, "after", 5, 1, 0, 2, 1, 0, false, "delay turn 3");
            Rejects(d, "c1", "next", "SLOT_FULL", "delay turn 3 next used 2 of cap 2");
            ReceiptsReplay(d);
        }

        // ───────── hand-source gate ─────────
        [Test]
        public void HandChoicesRefusesEveryNonHandUidBeforeAnyDefinitionIsRead()
        {
            Assert.That(Application.isPlaying, Is.True, "must run inside actual PlayMode");
            var s = Open("t_dummy", C("t_plain"), C("t_cheap"), C("t_exhaust"), C("t_tune"), C("t_solo"));
            Refused(Choices(s, "c4", "now", None, "created draw pile"), "c4", "now", "NOT_IN_HAND", "created: a draw-pile card");
            Begin(s); Play(s, "c1", "next"); Play(s, "c2", "now"); Play(s, "c3", "now");
            var b = s.Battle.State;
            Assert.That((b.Reservations.Single().Uid, b.Zones.Discard.Single(), b.Zones.Exhaust.Single(), b.Enemies.Single().Uid), Is.EqualTo(("c1", "c2", "c3", "e6")), "gate fixture zones");
            foreach (string uid in new[] { "c1", "c2", "c3", "e6", "c99", "" })
                foreach (string mode in new[] { "now", "after", "sideways" })
                    Refused(Choices(s, uid, mode, new[] { "c4" }, "non-hand " + uid + " " + mode), uid, mode, "NOT_IN_HAND", "non-hand '" + uid + "' in " + mode);
            Refused(Choices(s, "c4", "sideways", None, "hand bad mode"), "c4", "sideways", "INVALID_MODE", "a hand card in an unknown mode");
            Expect(Choices(s, "c4", "now", None, "tune before removal"), null, 0, 0, 1, null, None, "tune: the only other hand card is a curse");
            // TEST-ONLY: drop the definitions of the discarded c2 and the hand curse c5 from this variant after the commands.
            s.Data.Cards.RemoveAll(c => c.Id == "t_cheap" || c.Id == "t_solo"); s.Data.Index();
            Refused(Choices(s, "c2", "now", None, "removed non-hand"), "c2", "now", "NOT_IN_HAND", "membership is decided before any definition is read");
            Refused(Choices(s, "c5", "now", None, "removed hand"), "c5", "now", "UNKNOWN_CARD", "a hand card without a definition reports Core's rule code");
            Expect(Choices(s, "c4", "now", None, "tune after removal"), null, 0, 0, 1, null, new[] { "c5" }, "a card the definitions do not hold cannot be told and stays offered");
            ReceiptsReplay(s);
        }

        // ───────── ordered picks and Core's price delegate ─────────
        [Test]
        public void HandChoicesWalksOrderedPicksAndPricesCostdownLikeCore()
        {
            Assert.That(Application.isPlaying, Is.True, "must run inside actual PlayMode");
            var s = Open("t_dummy", C("t_multi"), C("t_plain"), C("t_plain"), C("t_curse"), C("t_cheap"), C("t_plain"));
            Begin(s); Play(s, "c5", "now"); Play(s, "c2", "next"); Play(s, "c6", "after");
            var start = Choices(s, "c1", "now", None, "multi");
            Assert.That(start.Requests.Select(r => (r.Label, r.From, r.Count, r.GrowthOnly, r.Op, r.Part, r.Leading)).ToList(), Is.EqualTo(new List<(string, string, int, bool, string, string?, bool)>
                { ("버릴 카드", "hand", 2, false, "discard", null, true), ("버린 카드에서 소멸할 카드", "discard", 1, false, "exhaustPick", null, false), ("앞당길 예약", "res", 1, false, "pull", null, false) }));
            Expect(start, null, 0, 0, 2, null, Hand(s, "c3", "c4"), "first request");
            Expect(Choices(s, "c1", "now", new[] { "c3", "c3" }, "multi dup"), "PICK_INVALID", 1, 0, 1, null, Hand(s, "c4"), "a repeated pick");
            Expect(Choices(s, "c1", "now", new[] { "c4", "c3" }, "multi 2"), null, 2, 1, 1, null, new[] { "c5" }, "the discard-pile request");
            Expect(Choices(s, "c1", "now", new[] { "c3", "c4", "c2" }, "multi wrong zone"), "PICK_INVALID", 2, 1, 1, null, new[] { "c5" }, "a reservation as a discard-pile pick");
            Expect(Choices(s, "c1", "now", new[] { "c3", "c4", "c5" }, "multi 3"), null, 3, 2, 1, null, new[] { "c2", "c6" }, "the reservation request in state order");
            Expect(Choices(s, "c1", "now", new[] { "c3", "c4", "c5", "c6", "c2" }, "multi 5"), "PICK_INVALID", 4, 3, 0, null, None, "a pick beyond the requests");
            ReceiptsReplay(s);

            var f = Open("t_dummy", C("t_costdown"), C("t_plain"), C("t_cheap"), C("t_cond"), C("t_free2"), C("t_free1"));
            Begin(f);
            Expect(Choices(f, "c1", "now", None, "FreeNext 0"), null, 0, 0, 1, null, Hand(f, "c2", "c4"), "FreeNext 0: CostOf above 0 plus the unknown conditional cost");
            Play(f, "c6", "now");
            Assert.That((f.Battle.State.FreeNext, f.Battle.CostOf("c2"), f.Battle.CostOfWithoutNextFree("c2")), Is.EqualTo((1, 0, 1)), "FreeNext 1 prices");
            Expect(Choices(f, "c1", "now", None, "FreeNext 1"), null, 0, 0, 1, null, Hand(f, "c2", "c4"), "FreeNext 1: priced with CostOfWithoutNextFree");
            Play(f, "c5", "now");
            Assert.That((f.Battle.State.FreeNext, f.Battle.CostOf("c2"), f.Battle.CostOfWithoutNextFree("c2")), Is.EqualTo((2, 0, 1)), "FreeNext 2 prices");
            Expect(Choices(f, "c1", "now", None, "FreeNext 2"), null, 0, 0, 1, null, Hand(f, "c4"), "FreeNext 2: every CostOf is 0; only the unknown stays");
            ReceiptsReplay(f);
        }

        // ───────── the client's forwarding wrappers are the shared Core policy ─────────
        [Test]
        public void ClientPolicyWrappersForwardToTheSharedCorePolicy()
        {
            Assert.That(Application.isPlaying, Is.True, "must run inside actual PlayMode");
            var s = Open("t_dummy", C("t_growpick"), C("t_growpick"), C("t_growpick"), C("t_growpick"), C("t_copypick"),
                C("t_grow"), C("t_grow", 3), C("t_evo", 0, "t_grow"), C("t_evo"), C("t_tune"));
            Begin(s);
            var definitions = s.Data.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
            Func<string, bool, int> price = (uid, withoutNextFree) => withoutNextFree ? s.Battle.CostOfWithoutNextFree(uid, "now") : s.Battle.CostOf(uid, "now");
            void Parity(string source, string label)
            {
                var b = s.Battle.State; var card = s.Battle.EffectiveCard(source);
                var requests = MemoryForgeExpeditionClient.ChoiceRequests(card, "now", s.Data); var rules = MemoryForgeExpeditionClient.ChoiceRules(card, "now");
                Assert.That(requests, Is.EqualTo(BattleChoicePolicy.ChoiceRequests(card, "now", s.Data)), label + ": ChoiceRequests forwards");
                Assert.That(rules, Is.EqualTo(BattleChoicePolicy.ChoiceRules(card, "now")), label + ": ChoiceRules forwards");
                var view = s.Battle.HandChoices(source);
                Assert.That(view.Requests.Select(r => (r.Label, r.From, r.Count, r.GrowthOnly)).ToList(), Is.EqualTo(requests), label + ": HandChoices lists the same requests");
                Assert.That(MemoryForgeExpeditionClient.PickBlocked(b, rules[0]), Is.EqualTo(BattleChoicePolicy.PickBlocked(b, rules[0])).And.EqualTo(view.Blocked), label + ": PickBlocked forwards");
                var wrapped = MemoryForgeExpeditionClient.PickCandidates(b, source, requests[0], None, definitions, rules[0], price);
                Assert.That(wrapped, Is.EqualTo(BattleChoicePolicy.PickCandidates(b, source, requests[0], None, definitions, rules[0], price)).And.EqualTo(view.Candidates), label + ": PickCandidates forwards");
                Assert.That(MemoryForgeExpeditionClient.PickCandidates(b, source, requests[0], None, definitions), Is.EqualTo(BattleChoicePolicy.PickCandidates(b, source, requests[0], None, definitions)),
                    label + ": the rule-less zone list forwards");
                foreach (string uid in b.Zones.Hand)
                    Assert.That(MemoryForgeExpeditionClient.EffectEligible(b, rules[0], uid, definitions, price), Is.EqualTo(BattleChoicePolicy.EffectEligible(b, rules[0], uid, definitions, price)),
                        label + ": EffectEligible forwards for " + uid);
            }
            // Literal sets from the TEST-ONLY definitions, through the wrappers.
            Parity("c1", "growPick");
            Assert.That(s.Battle.HandChoices("c1").Candidates, Is.EqualTo(Hand(s, "c6")), "growPick: the growth definition below growth 3 only");
            Parity("c5", "copyPick");
            Assert.That(s.Battle.HandChoices("c5").Candidates, Is.EqualTo(Hand(s, "c6", "c7", "c8")), "copyPick: growth definitions and the evolution from the growth root");
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(s.Battle.State, "c5", ("사본을 만들 성장 카드", "hand", 1, true), None, definitions), Is.EqualTo(Hand(s, "c6", "c7")),
                "rule-less growth-only list: growth definitions only");
            Parity("c10", "tune");
            Assert.That(s.Battle.HandChoices("c10").Requests.Single().Label, Is.EqualTo(Tuned));
            Play(s, "c1", "now", "c6"); Play(s, "c2", "now", "c6"); Play(s, "c3", "now", "c6");
            Assert.That((s.Battle.State.GrowthCount, s.Battle.State.Cards["c6"].Growth), Is.EqualTo((3, 3)), "three real growPick plays reach the growth cap");
            Parity("c4", "growPick at cap");
            Expect(s.Battle.HandChoices("c4"), null, 0, 0, 1, "PICK_GROWTH_CAP", None, "at the cap growPick is blocked");
            ReceiptsReplay(s);
        }

        // ───────── the Run forwarders on the real exported content ─────────
        [Test]
        public void RunForwardersQueryOnlyTheLiveBattle()
        {
            Assert.That(Application.isPlaying, Is.True, "must run inside actual PlayMode");
            var data = ClientRunContent.Load();
            var run = CoreRun.Create(data, MemoryForgeExpeditionClient.PracticeSetup(1234, "short", "public-query", new Dictionary<string, object?>(), 0));
            Assert.That(Assert.Throws<RuleException>(() => run.BattleReservationSlots())!.Code, Is.EqualTo("NO_BATTLE"));
            Assert.That(Assert.Throws<RuleException>(() => run.BattleHandChoices("c1"))!.Code, Is.EqualTo("NO_BATTLE"));
            run.Enter(run.Available()[0]);
            string Snapshot(RunState state) => CanonicalState.Serialize(state.ToJsonGraph());
            string before = Snapshot(run.State); var b = run.State.Battle!;
            var slots = run.BattleReservationSlots();
            Assert.That((slots.Turn, slots.SealedUntil, slots.Reservations.Count), Is.EqualTo((1, 0, 0)), "opening board");
            Assert.That((slots.Next.Kind, slots.Next.Due, slots.Next.Temp, slots.Next.Max, slots.Next.Used, slots.Next.Sealed), Is.EqualTo(("next", 2L, 0, 3, 0, false)), "opening next column");
            Assert.That((slots.After.Kind, slots.After.Due, slots.After.Temp, slots.After.Max, slots.After.Used, slots.After.Sealed), Is.EqualTo(("after", 3L, 0, 2, 0, false)), "opening after column");
            Assert.That((slots.Next.Base, slots.After.Base), Is.EqualTo((b.SlotsBase.Next, b.SlotsBase.After)), "base columns are the live battle's");
            Assert.That((slots.Next.Cap, slots.After.Cap), Is.EqualTo((Math.Min(3, b.SlotsBase.Next), Math.Min(2, b.SlotsBase.After))), "unsealed opening caps without temporary slots");
            foreach (string uid in b.Zones.Draw.Concat(b.Zones.Discard).Concat(new[] { b.Enemies[0].Uid, "missing", "" }))
                Assert.That(Print(run.BattleHandChoices(uid, "now", new[] { "x" })), Is.EqualTo("NOT_IN_HAND|" + uid + "|now|0|0|0||[]|"), "non-hand " + uid);
            foreach (string uid in b.Zones.Hand)
            {
                var view = run.BattleHandChoices(uid);
                Assert.That(view.Error, Is.Null, uid + ": a real hand card without picks has no error");
                var card = run.BattleEffectiveCard(uid);
                Assert.That(view.Requests.Select(r => (r.Label, r.From, r.Count, r.GrowthOnly)).ToList(), Is.EqualTo(BattleChoicePolicy.ChoiceRequests(card, "now", data.Memory.Battle)), uid + ": the Run forwarder lists Core's requests");
                Assert.That(run.BattleHandChoices(uid, "next").Requests, Is.Empty, uid + ": a reservation mode asks for no pick");
                Assert.That(run.BattleHandChoices(uid, "sideways").Error, Is.EqualTo("INVALID_MODE"));
            }
            Assert.That(Snapshot(run.State), Is.EqualTo(before), "Run queries change no Run/battle state, RNG, version or log");
        }
    }
}
