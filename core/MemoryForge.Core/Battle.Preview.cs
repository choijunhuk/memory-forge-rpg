// P1-EXPEDITION-BATTLE-FORECAST §14.3/§6/§16.2: the accepted practice-view preview as one pure Core query. Check first; then the real
// play runs through Command on a private state copy (the same isolated candidate Command itself builds), so no cost/damage/condition
// rule is duplicated. Live state, RNG, log, events, version, receipts and definitions are never written; the typed result is detached.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoryForge.Core
{
    /// <summary>Detached forecast of one card play request {uid, mode, target, picks} at the current battle state.
    /// <para>Error: Battle.Check's code, else the code the real play command would reject with (Run.BattleForecast: "NO_BATTLE" outside
    /// battle). With an error every other field keeps its default.</para>
    /// <para>Mode: the requested mode. Cost: the AP the play pays. Due: the absolute turn a next/after reservation resolves, null for now.
    /// A reservation reports no outcome numbers: its effects resolve later against a future state.</para>
    /// <para>Hidden (now only): the outcome depends on a draw or a random choice. Either the effective definition has tgt "rand", pick
    /// "random" or a draw anywhere in if/then/else, or the actual play moved the RNG or the draw pile through a relic/passive hook. The
    /// numbers then stay zero, so no draw order or random result is revealed.</para>
    /// <para>Otherwise the actual result of the real play: EnemyHpLoss is the total HP the enemies lose (damage absorbed by enemy block is
    /// not HP loss), BlockDelta and HpDelta are the signed party block and HP changes, and Defeat is true when the play ends the battle in
    /// defeat.</para></summary>
    public sealed class BattlePlayForecast
    {
        public string? Error;
        public string Mode = "now";
        public int Cost;
        public int? Due;
        public bool Hidden;
        public int EnemyHpLoss, BlockDelta, HpDelta;
        public bool Defeat;
    }

    public sealed partial class Battle
    {
        /// <summary>Pure forecast of the play request Command would receive as {uid, mode, target, picks}; target is sent as given (Check's
        /// "auto" helper is not a command target). The picks are copied first and never retained. Rule rejections are returned, never thrown.</summary>
        public BattlePlayForecast Forecast(string uid, string mode = "now", string? target = null, IReadOnlyList<string>? picks = null)
        {
            var chosen = picks == null ? new List<string>() : new List<string>(picks);
            var forecast = new BattlePlayForecast { Mode = mode, Error = Check(uid, mode, target, chosen) };
            if (forecast.Error != null) return forecast;
            static BattleArgument Text(string? value) => value == null ? BattleArgument.Null() : BattleArgument.String(value);
            try
            {
                var args = BattleArgument.Object(new[]
                {
                    new KeyValuePair<string, BattleArgument>("uid", Text(uid)), new KeyValuePair<string, BattleArgument>("mode", Text(mode)),
                    new KeyValuePair<string, BattleArgument>("target", Text(target)), new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array(chosen.Select(Text)))
                });
                var candidate = new Battle(data, state.Copy());
                var response = candidate.Command(new BattleRequest(null, state.V, "play", args));
                forecast.Cost = response.Events.First(e => e.E == "play").Cost.GetValueOrDefault();
                if (mode != "now") { forecast.Due = response.Events.First(e => e.E == "reserve" && e.Uid == uid).Due; return forecast; }
                var after = candidate.state;
                if (HiddenOutcome(EffectiveCard(uid).Effects) || !state.Rng.SequenceEqual(after.Rng) || !state.Zones.Draw.SequenceEqual(after.Zones.Draw))
                { forecast.Hidden = true; return forecast; }
                long loss = 0;
                foreach (var enemy in state.Enemies) loss += Math.Max(0, enemy.Hp - (after.Enemies.FirstOrDefault(e => e.Uid == enemy.Uid)?.Hp ?? 0));
                forecast.EnemyHpLoss = StateNumber(loss); forecast.BlockDelta = after.Block - state.Block; forecast.HpDelta = after.Hp - state.Hp;
                forecast.Defeat = after.Phase == "DEFEAT";
                return forecast;
            }
            catch (RuleException rejected) { return new BattlePlayForecast { Mode = mode, Error = rejected.Code }; }
        }

        // The accepted practice-view rule on the effective definition: a random target or pick, or a draw, anywhere in if/then/else.
        static bool HiddenOutcome(IEnumerable<EffectDef> effects) => effects.Any(effect => effect.Target == "rand" || effect.Pick == "random" || effect.Op == "draw"
            || effect.Then != null && HiddenOutcome(effect.Then) || effect.Else != null && HiddenOutcome(effect.Else));
    }
}
