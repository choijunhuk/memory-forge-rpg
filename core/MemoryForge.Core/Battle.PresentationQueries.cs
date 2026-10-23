// P1-PUBLIC-BATTLE-QUERY §17.4/§14.7/§14.3: pure presentation queries of the live battle for a reconnecting client. ReservationSlots is the
// reservation board exactly as Core enforces it (SlotCapacity and FreeSlots over ReservationBook.CalculateCapacity); HandChoices is the
// shared pick policy (BattleChoicePolicy) applied to one hand card with its effective definition and Core's own price. Both return fresh
// detached results: no state, RNG, log, events, version, receipt or definition is written, and no rule is re-derived here.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoryForge.Core
{
    /// <summary>One reservation column of the current turn (§14.7/§17.4).
    /// <para>Kind: "next" or "after". Due: the absolute turn its reservations resolve on, (long)Turn + 1 or + 2, as FreeSlots compares it.</para>
    /// <para>Base and Temp: the Core-held base and this turn's temporary slots (SlotsBase/SlotsTemp). Max: the authored column maximum.</para>
    /// <para>Cap: the capacity Core enforces for a new reservation (SlotCapacity: ReservationBook.CalculateCapacity, min(Max, Base + Temp),
    /// one less when Sealed, never below 0). Sealed: the seal takes one slot of this column; next only, with exactly the capacity
    /// predicate SealedUntil &gt;= Turn, so it also holds at turn 0, where SealedUntil is still 0.</para>
    /// <para>Used: the reservations due exactly on Due (SlotCapacity − FreeSlots), whatever their kind; a reservation due on another turn
    /// is in neither column. Used may exceed Cap (a seal, or temporary slots that expired after the reservations were made); every
    /// reservation stays listed in BattleSlotView.Reservations.</para></summary>
    public sealed class BattleSlotColumn
    {
        public string Kind = "next";
        public long Due;
        public int Base, Temp, Max, Cap, Used;
        public bool Sealed;
    }

    /// <summary>The live battle's reservation board: the turn, SealedUntil, both columns and a detached copy of every reservation in the
    /// state's order with its own due turn, order, target, delay flag and kind (the value it resolves with), wherever it is due.</summary>
    public sealed class BattleSlotView
    {
        public int Turn, SealedUntil;
        public BattleSlotColumn Next = new BattleSlotColumn(), After = new BattleSlotColumn { Kind = "after" };
        public List<BattleReservationState> Reservations = new List<BattleReservationState>();
    }

    /// <summary>One card-pick request of a play: BattleChoicePolicy.ChoiceRequests (Label, From = zone, Count, GrowthOnly) with the
    /// ChoiceRules effect at the same position (Op, Part, Leading).</summary>
    public sealed class BattleChoiceRequest
    {
        public string Label = "", From = "hand", Op = "";
        public string? Part;
        public int Count;
        public bool GrowthOnly, Leading;
    }

    /// <summary>The pick sheet of one play request {Uid, Mode, picks} of a hand card (§14.3).
    /// <para>Error: NOT_IN_HAND when Uid is not in the hand (always this one answer, before the card's definition, price or zone is
    /// read); INVALID_MODE for a mode other than now/next/after; a Core rule code when the effective definition or a price fails; or
    /// PICK_INVALID when a supplied pick does not hold. With NOT_IN_HAND, INVALID_MODE or a rule code every other field keeps its default.</para>
    /// <para>Requests: every request of the effective definition in this mode, in order (none for next/after). Accepted: how many
    /// leading supplied picks hold on the sheet's own progression (BattleChoicePolicy.WalkPicks). Index and Left: the request the next
    /// pick fills and its picks still open (Index == Requests.Count once every request is filled).</para>
    /// <para>Blocked and Candidates: for the request at Index, PickBlocked (PICK_GROWTH_CAP) and PickCandidates given the accepted picks,
    /// priced by Core (CostOf, or CostOfWithoutNextFree while exactly one "next card costs 0" charge remains): instance uids of the
    /// hand, discard pile or reservations in zone order. Null and empty once every request is filled.</para></summary>
    public sealed class BattleChoiceView
    {
        public string? Error;
        public string Uid = "", Mode = "now";
        public List<BattleChoiceRequest> Requests = new List<BattleChoiceRequest>();
        public int Accepted, Index, Left;
        public string? Blocked;
        public List<string> Candidates = new List<string>();
    }

    public sealed partial class Battle
    {
        /// <summary>The reservation board of the current state (see BattleSlotView/BattleSlotColumn). Pure; the result is detached.</summary>
        public BattleSlotView ReservationSlots() => new BattleSlotView
        {
            Turn = state.Turn, SealedUntil = state.SealedUntil, Next = SlotColumn("next"), After = SlotColumn("after"),
            Reservations = state.Reservations.Select(r => r.Copy()).ToList()
        };
        // Cap and Used are SlotCapacity and SlotCapacity − FreeSlots themselves: the same capacity and exact-due count a reservation meets.
        BattleSlotColumn SlotColumn(string kind)
        {
            bool next = kind == "next"; int cap = SlotCapacity(kind);
            return new BattleSlotColumn
            {
                Kind = kind, Due = (long)state.Turn + (next ? 1 : 2), Base = next ? state.SlotsBase.Next : state.SlotsBase.After,
                Temp = next ? state.SlotsTemp.Next : state.SlotsTemp.After, Max = next ? data.Rules.NextSlotsMax : data.Rules.AfterSlotsMax,
                Cap = cap, Used = cap - FreeSlots(kind), Sealed = next && state.SealedUntil >= state.Turn
            };
        }

        /// <summary>The pick sheet of the play {uid, mode, picks} (see BattleChoiceView). Hand membership is checked first, so a uid of the
        /// draw pile, discard pile, exhaust pile, a reservation or no card at all gets the same NOT_IN_HAND before any definition or price is
        /// read. Then BattleChoicePolicy with the effective definition, this battle's definitions and Core's own price of hand cards in mode
        /// now. The picks are copied first and never retained; no rule is added and Core's own play, fallback and auto are unchanged. Pure.</summary>
        public BattleChoiceView HandChoices(string uid, string mode = "now", IReadOnlyList<string>? picks = null)
        {
            var chosen = picks == null ? new List<string>() : new List<string>(picks);
            if (!state.Zones.Hand.Contains(uid)) return new BattleChoiceView { Uid = uid, Mode = mode, Error = "NOT_IN_HAND" };
            if (mode != "now" && mode != "next" && mode != "after") return new BattleChoiceView { Uid = uid, Mode = mode, Error = "INVALID_MODE" };
            try
            {
                var effective = EffectiveCard(uid);
                var requests = BattleChoicePolicy.ChoiceRequests(effective, mode, data); var rules = BattleChoicePolicy.ChoiceRules(effective, mode);
                Func<string, bool, int> price = (card, withoutNextFree) => withoutNextFree ? CostOfWithoutNextFree(card, "now") : CostOf(card, "now");
                var walk = BattleChoicePolicy.WalkPicks(state, uid, requests, rules, chosen, data.CardIndex, price);
                var view = new BattleChoiceView { Uid = uid, Mode = mode, Accepted = walk.Accepted, Index = walk.Index, Left = walk.Left };
                for (int i = 0; i < requests.Count; i++)
                    view.Requests.Add(new BattleChoiceRequest { Label = requests[i].Label, From = requests[i].From, Count = requests[i].Count,
                        GrowthOnly = requests[i].GrowthOnly, Op = rules[i].Op, Part = rules[i].Part, Leading = rules[i].Leading });
                if (walk.Accepted < chosen.Count) view.Error = BattleChoicePolicy.PickInvalid;
                if (walk.Index < requests.Count)
                {
                    view.Blocked = BattleChoicePolicy.PickBlocked(state, rules[walk.Index]);
                    view.Candidates = BattleChoicePolicy.PickCandidates(state, uid, requests[walk.Index], chosen.Take(walk.Accepted), data.CardIndex, rules[walk.Index], price);
                }
                return view;
            }
            catch (RuleException error) { return new BattleChoiceView { Uid = uid, Mode = mode, Error = error.Code }; }
            catch (OverflowException) { return new BattleChoiceView { Uid = uid, Mode = mode, Error = "BATTLE_NUMBER_OUT_OF_RANGE" }; }
        }
    }
}
