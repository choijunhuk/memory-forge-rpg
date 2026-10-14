// Memory Forge v0.7 core — time reservations (시간 예약) with absolute due turns.
// Fixes the v0.6 kernel: per-turn temporary slots, hard caps (next ≤ 3, after-next ≤ 2), sealing that only
// reduces the next-turn capacity, and pull (앞당김) semantics identical to engine/rules-core.js.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoryForge.Core
{
    public sealed class Reservation
    {
        public string CardUid { get; set; } = "";
        public string? Target { get; set; }
        public int Due { get; set; }           // absolute turn number
        public int Order { get; set; }         // global sequence (resolution order within a turn)
        public string Kind { get; set; } = "next"; // next | after (kept for value selection: rv / ra)
        public Reservation Copy() => (Reservation)MemberwiseClone();
    }

    public sealed class ReservationBook
    {
        public const int NextBaseDefault = 2, AfterBaseDefault = 1, NextMax = 3, AfterMax = 2;
        private readonly List<Reservation> entries = new List<Reservation>();
        private int sequence;
        public int NextBase { get; private set; }
        public int AfterBase { get; private set; }
        public int TempNext { get; private set; }
        public int TempAfter { get; private set; }
        public int SealedUntil { get; private set; }

        public ReservationBook(int nextBase = NextBaseDefault, int afterBase = AfterBaseDefault)
        {
            if (nextBase < 1 || nextBase > NextMax || afterBase < 1 || afterBase > AfterMax) throw new RuleException("CAPACITY_RANGE");
            NextBase = nextBase; AfterBase = afterBase;
        }

        public IReadOnlyList<Reservation> Snapshot() => entries.Select(r => r.Copy()).ToArray();

        /// <summary>Relics that permanently add a base slot (capped).</summary>
        public void AddBase(int next, int after) { NextBase = Math.Min(NextMax, NextBase + next); AfterBase = Math.Min(AfterMax, AfterBase + after); }

        /// <summary>Card/ult effects that add slots for this turn only (cleared by StartTurn).</summary>
        public void AddTemp(int next, int after)
        {
            TempNext = Math.Max(0, Math.Min(NextMax - NextBase, TempNext + next));
            TempAfter = Math.Max(0, Math.Min(AfterMax - AfterBase, TempAfter + after));
        }

        public int Capacity(string kind, int turn)
        {
            if (kind == "next")
            {
                int cap = Math.Min(NextMax, NextBase + TempNext);
                if (SealedUntil >= turn) cap -= 1;
                return Math.Max(0, cap);
            }
            if (kind == "after") return Math.Min(AfterMax, AfterBase + TempAfter);
            throw new RuleException("INVALID_MODE");
        }

        public int Used(string kind, int turn) { int due = turn + (kind == "next" ? 1 : 2); return entries.Count(r => r.Due == due); }
        public int Free(string kind, int turn) => Capacity(kind, turn) - Used(kind, turn);

        public void Add(string cardUid, string? target, int turn, string kind)
        {
            if (kind != "next" && kind != "after") throw new RuleException("INVALID_MODE");
            if (entries.Any(r => r.CardUid == cardUid)) throw new RuleException("DUPLICATE_RESERVATION");
            if (Free(kind, turn) <= 0) throw new RuleException("SLOT_FULL");
            entries.Add(new Reservation { CardUid = cardUid, Target = target, Due = turn + (kind == "next" ? 1 : 2), Order = ++sequence, Kind = kind });
        }

        /// <summary>담보 계약: seal one next-turn slot through the end of the following turn.</summary>
        public void Seal(int turn)
        {
            if (SealedUntil >= turn || Free("next", turn) <= 0) throw new RuleException("NO_EMPTY_SLOT");
            SealedUntil = turn + 1;
        }

        /// <summary>Called at the start of a turn: clears temp slots and returns due reservations in (due, order).</summary>
        public List<Reservation> StartTurn(int turn)
        {
            TempNext = 0; TempAfter = 0;
            var due = entries.Where(r => r.Due <= turn).OrderBy(r => r.Due).ThenBy(r => r.Order).ToList();
            entries.RemoveAll(r => r.Due <= turn);
            return due;
        }

        /// <summary>시간 당기기: pick (or default: earliest after-next, else earliest) and move it one turn earlier.
        /// Returns (reservation, resolveNow). If it arrives this turn the caller resolves it immediately.</summary>
        public (Reservation? res, bool resolveNow) Pull(int turn, string? pickUid)
        {
            Reservation? r = null;
            if (pickUid != null) r = entries.FirstOrDefault(x => x.CardUid == pickUid);
            if (r == null) r = entries.Where(x => x.Due > turn + 1).OrderBy(x => x.Order).FirstOrDefault();
            if (r == null) r = entries.OrderBy(x => x.Order).FirstOrDefault();
            if (r == null) throw new RuleException("RESERVATION_REQUIRED");
            if (r.Due > turn + 1)
            {
                if (Free("next", turn) <= 0) throw new RuleException("PULL_DESTINATION_FULL");
                r.Due -= 1; r.Kind = "next";
                return (r, false);
            }
            entries.Remove(r);
            return (r, true);
        }

        /// <summary>Target died before resolution: ordinary attacks retarget, fixed (표식 고정) attacks fizzle — decided by the caller.</summary>
        public void Retarget(string cardUid, string? newTarget) { var r = entries.FirstOrDefault(x => x.CardUid == cardUid); if (r != null) r.Target = newTarget; }
    }
}
