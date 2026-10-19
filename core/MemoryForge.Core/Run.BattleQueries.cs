// P1-UNITY-EXPEDITION-CLIENT §14.3/§16.2 (and P1-PUBLIC-BATTLE-QUERY §17.4/§14.7 for the server's battle query): read-only battle
// queries over the Run's private live battle.
// Each wrapper forwards to the existing public Battle query and returns that query's own detached result: the mutable Battle never
// leaves the Run, no rule is duplicated here, and nothing injects or restores state or advances RNG, version or receipts. Card
// actions still commit only through BattleCommand. Outside a battle the value queries throw NO_BATTLE exactly as BattleCommand does;
// BattleCheck keeps Battle.Check's error-code contract and returns "NO_BATTLE" instead.
using System.Collections.Generic;

namespace MemoryForge.Core
{
    public sealed partial class Run
    {
        /// <summary>Battle.AllowedModes of a live card instance (a fresh list). NO_BATTLE outside battle.</summary>
        public List<string> BattleAllowedModes(string uid) => LiveBattle().AllowedModes(uid);

        /// <summary>Battle.Retains of a live card instance: it stays in hand at the end of the turn. NO_BATTLE outside battle.</summary>
        public bool BattleRetains(string uid) => LiveBattle().Retains(uid);

        /// <summary>Battle.CostOf of a live card instance for a mode. NO_BATTLE outside battle.</summary>
        public int BattleCostOf(string uid, string mode = "now") => LiveBattle().CostOf(uid, mode);

        /// <summary>Battle.CostOfWithoutNextFree of a live card instance for a mode: CostOf's calculation without a remaining "next card
        /// costs 0" charge. NO_BATTLE outside battle.</summary>
        public int BattleCostOfWithoutNextFree(string uid, string mode = "now") => LiveBattle().CostOfWithoutNextFree(uid, mode);

        /// <summary>Battle.NeedsTarget of a live card instance: "one", "fixed" or null. NO_BATTLE outside battle.</summary>
        public string? BattleNeedsTarget(string uid) => LiveBattle().NeedsTarget(uid);

        /// <summary>Battle.EffectiveCard: a detached definition with upgrade and resonance overlays. NO_BATTLE outside battle.</summary>
        public BattleCardDef BattleEffectiveCard(string uid) => LiveBattle().EffectiveCard(uid);

        /// <summary>Battle.Check: null when the play would pass Battle's own precheck, else its error code; "NO_BATTLE" outside
        /// battle. The picks are copied before the query so a caller's list is never retained.</summary>
        public string? BattleCheck(string uid, string mode = "now", string? target = null, IReadOnlyList<string>? picks = null)
            => battle == null ? "NO_BATTLE" : battle.Check(uid, mode, target, picks == null ? null : new List<string>(picks));

        /// <summary>Battle.Forecast of a card play request on the live battle: a fresh detached result; the Run, its battle, RNG, log,
        /// memory, versions and receipts stay unchanged. Like BattleCheck, outside battle it returns Error "NO_BATTLE" instead of throwing.</summary>
        public BattlePlayForecast BattleForecast(string uid, string mode = "now", string? target = null, IReadOnlyList<string>? picks = null)
            => battle == null ? new BattlePlayForecast { Mode = mode, Error = "NO_BATTLE" } : battle.Forecast(uid, mode, target, picks);

        /// <summary>P1-PUBLIC-BATTLE-QUERY §17.4/§14.7: Battle.ReservationSlots of the live battle, a fresh detached board (capacity, exact-due
        /// use and seal per column, every reservation with its own kind). NO_BATTLE outside battle.</summary>
        public BattleSlotView BattleReservationSlots() => LiveBattle().ReservationSlots();

        /// <summary>P1-PUBLIC-BATTLE-QUERY §14.3: Battle.HandChoices, the pick sheet of a play whose source must be a hand card. Any other uid
        /// is the same fixed NOT_IN_HAND before its definition, price or zone is read, so a hidden card cannot be probed; the picks are
        /// copied. A fresh detached result. NO_BATTLE outside battle.</summary>
        public BattleChoiceView BattleHandChoices(string uid, string mode = "now", IReadOnlyList<string>? picks = null) => LiveBattle().HandChoices(uid, mode, picks);

        Battle LiveBattle() => battle ?? throw new RuleException("NO_BATTLE");
    }
}
