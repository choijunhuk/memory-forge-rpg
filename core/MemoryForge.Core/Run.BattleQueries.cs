// P1-UNITY-EXPEDITION-CLIENT §14.3/§16.2: read-only battle queries for the Unity expedition view over the Run's private live battle.
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

        /// <summary>Battle.CostOf of a live card instance for a mode. NO_BATTLE outside battle.</summary>
        public int BattleCostOf(string uid, string mode = "now") => LiveBattle().CostOf(uid, mode);

        /// <summary>Battle.NeedsTarget of a live card instance: "one", "fixed" or null. NO_BATTLE outside battle.</summary>
        public string? BattleNeedsTarget(string uid) => LiveBattle().NeedsTarget(uid);

        /// <summary>Battle.EffectiveCard: a detached definition with upgrade and resonance overlays. NO_BATTLE outside battle.</summary>
        public BattleCardDef BattleEffectiveCard(string uid) => LiveBattle().EffectiveCard(uid);

        /// <summary>Battle.Check: null when the play would pass Battle's own precheck, else its error code; "NO_BATTLE" outside
        /// battle. The picks are copied before the query so a caller's list is never retained.</summary>
        public string? BattleCheck(string uid, string mode = "now", string? target = null, IReadOnlyList<string>? picks = null)
            => battle == null ? "NO_BATTLE" : battle.Check(uid, mode, target, picks == null ? null : new List<string>(picks));

        Battle LiveBattle() => battle ?? throw new RuleException("NO_BATTLE");
    }
}
