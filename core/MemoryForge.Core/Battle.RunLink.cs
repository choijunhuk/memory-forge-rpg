// R1a §7.1/§16.2: internal Run link. Run aliases Live only inside its private RunState (Run.State deep-copies it),
// so no mutable Battle or battle state escapes publicly. The existing Battle files are unchanged.
using System;
using System.Collections.Generic;

namespace MemoryForge.Core
{
    public sealed partial class Battle
    {
        internal BattleState Live => state;
        internal int Version => state.V;
        internal bool Finished => IsOver();

        /// <summary>Source startBattle: the first deck-count Object.keys(cards) entries take the Run deck keys before begin;
        /// later instances (the eye_of_rift smudge token) keep none. BattleCardState.Copy keeps them through Command candidates.</summary>
        internal void BindRunKeys(IReadOnlyList<string?> keys)
        {
            if (state.Phase != "CREATED" || state.V != 0 || keys.Count > state.CardOrder.Count)
                throw new InvalidOperationException("Run keys bind only to a newly created battle's deck instances");
            for (int i = 0; i < keys.Count; i++) state.Cards[state.CardOrder[i]].Key = keys[i];
        }
    }
}
