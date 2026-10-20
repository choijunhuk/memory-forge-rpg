// P1-EXPEDITION-BATTLE-FORECAST / P1-RESERVED-FORECAST-CORE §14.2/§14.3/§5.4-5.6/§6/§16.2: the original preview as one pure Core query.
// Check first; then the real play runs through Command on a private state copy (the same isolated candidate Command itself builds) for
// the actual cost, due turn and immediate outcome. A next/after reservation's outcome is the original HTML preview's current-state
// estimate: another copy of this state with the card out of the hand, phase RESOLVING and the existing reserved resolver. No rule is
// duplicated. Live state, RNG, log, events, version, receipts and definitions are never written; the typed result is detached.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoryForge.Core
{
    /// <summary>Detached forecast of one card play request {uid, mode, target, picks} at the current battle state.
    /// <para>Error: Battle.Check's code, else the code the real play command would reject with, else a rule rejection raised while the
    /// current-state reservation estimate resolves (Run.BattleForecast: "NO_BATTLE" outside battle). With an error every other field
    /// keeps its default.</para>
    /// <para>Mode: the requested mode. Cost: the AP the real play pays. Due: the absolute turn a next/after reservation resolves, null
    /// for now.</para>
    /// <para>Outcome fields (EnemyHpLoss, EnemyDamage, NewlyBroken, BlockDelta, HpDelta, Defeat). For now they are the actual result of the
    /// real play. For next/after they are a current-state estimate, as the original HTML preview computes it: the reserved resolution
    /// applied to a copy of the present state with the card out of the hand, phase RESOLVING, the target the real reservation stores and
    /// no picks. Nothing is paid, scheduled or advanced first, and no enemy turn, draw or future status runs. The real resolution happens
    /// later against that later state, so an estimate is never a promise.</para>
    /// <para>EnemyHpLoss is the total HP the enemies lose (damage absorbed by enemy block is not HP loss). EnemyDamage is the total damage
    /// including block: the sum over enemies of max(0, (HP+block) before − (HP+block) after). NewlyBroken counts enemies broken by it that
    /// were not broken before. BlockDelta and HpDelta are the signed party block and HP changes. Defeat is true when the play (or, for a
    /// reservation, its estimated resolution) ends the battle in defeat.</para>
    /// <para>Hidden: the outcome depends on a draw or a random choice. Either the effective definition has tgt "rand", pick "random" or a
    /// draw anywhere in if/then/else, or the resolution moved the RNG or the draw pile through a relic/passive hook (now: the real play;
    /// next/after: the estimate). Every outcome field then keeps its default, so no draw order or random result is revealed.</para>
    /// <para>EnemyHpLosses splits EnemyHpLoss per enemy for the client's per-enemy preview: one entry for each enemy that loses HP, in the
    /// battle's enemy order, summing to EnemyHpLoss; empty with an error or a hidden outcome. The JS oracle pins the total; CoreChecks
    /// recomputes each entry from the real play.</para></summary>
    public sealed class BattlePlayForecast
    {
        public string? Error;
        public string Mode = "now";
        public int Cost;
        public int? Due;
        public bool Hidden;
        public int EnemyHpLoss, BlockDelta, HpDelta;
        public bool Defeat;
        public int EnemyDamage, NewlyBroken;
        public List<BattleEnemyHpLoss> EnemyHpLosses = new List<BattleEnemyHpLoss>();
    }

    /// <summary>The HP one enemy loses in a forecast (BattlePlayForecast.EnemyHpLosses).</summary>
    public sealed class BattleEnemyHpLoss
    {
        public string Uid = "";
        public int HpLoss;
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
                var after = candidate.state;
                if (mode != "now")
                {
                    forecast.Due = response.Events.First(e => e.E == "reserve" && e.Uid == uid).Due;
                    after = ReservedEstimate(uid, mode, after.Reservations.FirstOrDefault(r => r.Uid == uid)?.Target);
                }
                if (HiddenOutcome(EffectiveCard(uid).Effects) || !state.Rng.SequenceEqual(after.Rng) || !state.Zones.Draw.SequenceEqual(after.Zones.Draw))
                { forecast.Hidden = true; return forecast; }
                long loss = 0, damage = 0;
                foreach (var enemy in state.Enemies)
                {
                    var now = after.Enemies.FirstOrDefault(e => e.Uid == enemy.Uid);
                    long lost = Math.Max(0, enemy.Hp - (now?.Hp ?? 0));
                    loss += lost;
                    if (lost > 0) forecast.EnemyHpLosses.Add(new BattleEnemyHpLoss { Uid = enemy.Uid, HpLoss = StateNumber(lost) });
                    if (now != null) damage += Math.Max(0L, (long)enemy.Hp + enemy.Block - ((long)now.Hp + now.Block));
                }
                forecast.EnemyHpLoss = StateNumber(loss); forecast.EnemyDamage = StateNumber(damage);
                forecast.NewlyBroken = after.Enemies.Count(e => e.BrokenUntil != 0 && state.Enemies.Any(x => x.Uid == e.Uid && x.BrokenUntil == 0));
                forecast.BlockDelta = after.Block - state.Block; forecast.HpDelta = after.Hp - state.Hp;
                forecast.Defeat = after.Phase == "DEFEAT";
                return forecast;
            }
            catch (RuleException rejected) { return new BattlePlayForecast { Mode = mode, Error = rejected.Code }; }
        }

        // The original HTML preview's reserved branch (prototype/index.html preview): a copy of this unpaid state, the card out of the hand,
        // phase RESOLVING and the existing resolver with the reservation's mode, the target the real reservation stores and no picks, as
        // StartTurn later resolves it. No turn advance, payment, played/echo bookkeeping, enemy turn or capture; the copy is discarded.
        BattleState ReservedEstimate(string uid, string mode, string? target)
        {
            var estimate = new Battle(data, state.Copy());
            estimate.state.Zones.Hand.Remove(uid); estimate.state.Phase = "RESOLVING";
            try { estimate.ResolveCard(uid, mode, target, new List<string>()); }
            catch (OverflowException) { throw new RuleException("BATTLE_NUMBER_OUT_OF_RANGE"); }
            return estimate.state;
        }

        // The accepted practice-view rule on the effective definition: a random target or pick, or a draw, anywhere in if/then/else.
        static bool HiddenOutcome(IEnumerable<EffectDef> effects) => effects.Any(effect => effect.Target == "rand" || effect.Pick == "random" || effect.Op == "draw"
            || effect.Then != null && HiddenOutcome(effect.Then) || effect.Else != null && HiddenOutcome(effect.Else));
    }
}
