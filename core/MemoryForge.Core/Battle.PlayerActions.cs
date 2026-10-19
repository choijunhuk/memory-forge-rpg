using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MemoryForge.Core
{
    public sealed partial class Battle
    {
        sealed class EffectContext
        {
            internal BattleCardState? Instance;
            internal BattleCardDef? Definition;
            internal BattleActorState? Actor;
            internal string Attr = "none";
            internal string? Reserved, Target, LastTarget, LastPicked;
            internal List<string> Picks = new List<string>();
            internal BattleArgument? RawPicks;
            internal bool IsUlt, Killed, FirstDamage, CreditLedger, Held;
            internal int LastCount, RestraintBps, BoostBps, ChargeBps, FlatBonus;
        }
        static bool Flag(Dictionary<string, object?> flags, string key) => flags.TryGetValue(key, out var value) && BattleGraph.Truthy(value);
        BattleActorState? Actor(string? id) => state.Actors.FirstOrDefault(a => a.Id == id);
        BattleEnemyState? Enemy(string? uid) => state.Enemies.FirstOrDefault(e => e.Uid == uid);
        bool HasHook(string? actor, string id) => Actor(actor)?.Hooks.Any(h => h.Id == id) == true;
        bool IsOver() => state.Phase == "VICTORY" || state.Phase == "DEFEAT";
        int DebtTotal() => StateNumber(state.Debts.Aggregate(BigInteger.Zero, (total, d) => total + d.N));
        BattleBuff? Buff(string status) => state.Buffs.FirstOrDefault(b => b.Status == status);
        string AttrOf(BattleCardState instance) => data.CardIndex[instance.Id].Attr == "wielder" ? Actor(instance.Wielder)?.Attr ?? "none" : OrNull(data.CardIndex[instance.Id].Attr) ?? "none";
        int SlotCapacity(string kind) => ReservationBook.CalculateCapacity(kind, state.SlotsBase.Next, state.SlotsBase.After, state.SlotsTemp.Next, state.SlotsTemp.After,
            state.SealedUntil, state.Turn, data.Rules.NextSlotsMax, data.Rules.AfterSlotsMax);
        int FreeSlots(string kind) => SlotCapacity(kind) - state.Reservations.Count(r => r.Due == (long)state.Turn + (kind == "next" ? 1 : 2));

        static string? ArgumentText(BattleArgument? value) => value == null || !value.Truthy ? null : value.TextValue ?? "\0invalid-argument";
        static List<string> ArgumentPicks(BattleArgument? value)
        {
            if (value == null || !value.Truthy) return new List<string>();
            if (value.ListValue != null) return value.ListValue.Select(v => v.TextValue ?? "\0invalid-pick").ToList();
            if (value.TextValue != null) return value.TextValue.Select(c => c.ToString()).ToList();
            throw new RuleException("INVALID_COMMAND_ARGS");
        }
        static IReadOnlyList<string> ReservationPicks(BattleArgument? value)
        {
            if (value?.ListValue != null) return value.ListValue.Select(v => v.TextValue ?? "\0invalid-pick").ToList();
            if (!string.IsNullOrEmpty(value?.TextValue)) return new[] { value!.TextValue![0].ToString() };
            if (value?.Member("length")?.Truthy == true) return new[] { value.Member("0")?.TextValue ?? "\0invalid-pick" };
            return new List<string>();
        }
        void PlayArguments(BattleArgument? args) => Play(ArgumentText(args?.Member("uid")) ?? "", ArgumentText(args?.Member("mode")) ?? "now",
            ArgumentText(args?.Member("target")), args?.Member("picks"));
        void UltArguments(BattleArgument? args) => Ult(ArgumentText(args?.Member("actor")), ArgumentText(args?.Member("target")), args?.Member("picks"));

        public string? Precheck(string uid, string mode = "now", string? target = null, IReadOnlyList<string>? picks = null)
        {
            if (state.Phase != "PLAYER") throw new RuleException("WRONG_PHASE");
            if (mode != "now" && mode != "next" && mode != "after") throw new RuleException("INVALID_MODE");
            if (!state.Zones.Hand.Contains(uid)) throw new RuleException("NOT_IN_HAND");
            var instance = state.Cards[uid]; var definition = EffectiveCard(uid);
            if (!AllowedModes(uid).Contains(mode)) throw new RuleException(mode == "now" ? "RESERVE_ONLY" : "NOT_RESERVABLE");
            if (state.Ap < CostOf(uid, mode)) throw new RuleException("AP_LOW");
            string? need = NeedsTarget(uid);
            if (need != null)
            {
                var alive = state.Enemies.Where(e => e.Alive).ToList();
                if (string.IsNullOrEmpty(target) && alive.Count == 1) target = alive[0].Uid;
                var enemy = Enemy(target);
                if (enemy == null || !enemy.Alive) throw new RuleException("BAD_TARGET");
                if (need == "fixed" && enemy.Status.Mark <= 0) throw new RuleException("FIXED_MARK_REQUIRED");
            }
            else target = null;
            ValidateOps(definition.Effects, instance, mode, picks ?? new List<string>());
            if (mode != "now" && FreeSlots(mode) <= 0) throw new RuleException("SLOT_FULL");
            return target;
        }
        public string? Check(string uid, string mode = "now", string? target = null, IReadOnlyList<string>? picks = null)
        {
            try
            {
                if (target == "auto")
                {
                    string? need = state.Cards.ContainsKey(uid) ? NeedsTarget(uid) : null;
                    target = state.Enemies.FirstOrDefault(e => e.Alive && (need != "fixed" || e.Status.Mark > 0))?.Uid;
                }
                Precheck(uid, mode, target, picks); return null;
            }
            catch (RuleException error) { return error.Code; }
            catch (OverflowException) { return "BATTLE_NUMBER_OUT_OF_RANGE"; }
        }
        void ValidateOps(IEnumerable<EffectDef> effects, BattleCardState instance, string mode, IReadOnlyList<string> picks)
        {
            foreach (var op in effects)
            {
                if (op.Op == "loan")
                {
                    if ((BigInteger)DebtTotal() + op.N!.Value > state.DebtCap) throw new RuleException("DEBT_CAP");
                    if ((BigInteger)state.Ap - CostOf(instance.Uid, mode) + op.N.Value > data.Rules.ApMax) throw new RuleException("LOAN_OVERFLOW");
                }
                if (op.Op == "defer")
                {
                    if (!state.Debts.Any(d => d.Due == (long)state.Turn + 1 && !d.Deferred)) throw new RuleException("NOT_DEFERRABLE");
                    if ((BigInteger)DebtTotal() + 1 > state.DebtCap) throw new RuleException("DEBT_CAP");
                }
                if (op.Op == "restraint" && (state.Played >= 3 || state.RestraintUsedTurn == state.Turn)) throw new RuleException("RESTRAINT_UNAVAILABLE");
                if (op.Op == "collateral" && (BigInteger)DebtTotal() + 1 > state.DebtCap) throw new RuleException("DEBT_CAP");
                if (op.Op == "pull")
                {
                    if (state.Reservations.Count == 0) throw new RuleException("RESERVATION_REQUIRED");
                    var reservation = PickReservation(picks);
                    if (reservation != null && reservation.Due > (long)state.Turn + 1 && FreeSlots("next") <= 0) throw new RuleException("PULL_DESTINATION_FULL");
                }
                if (op.Op == "selfdmg" && (BigInteger)state.Hp - OverloadCost(op.V!.Value, true) < 1) throw new RuleException("OVERLOAD_WOULD_KILL");
                if (op.Then != null) ValidateOps(op.Then, instance, mode, picks);
            }
        }
        BattleReservationState? PickReservation(IReadOnlyList<string> picks) => (picks.Count == 0 ? null : state.Reservations.FirstOrDefault(r => r.Uid == picks[0]))
            ?? state.Reservations.Where(r => r.Due > (long)state.Turn + 1).OrderBy(r => r.Order).FirstOrDefault() ?? state.Reservations.OrderBy(r => r.Order).FirstOrDefault();
        int OverloadCost(int value, bool peek)
        {
            var free = Buff("overloadFree");
            if (free != null) { if (!peek) state.Buffs.RemoveAll(b => b.Status == "overloadFree"); return 0; }
            if (Flag(state.BattleFlags, "firstOverloadFree") && !state.OverloadFreeUsed) { if (!peek) state.OverloadFreeUsed = true; return 0; }
            return ScaleVitality(value);
        }
        void Play(string uid, string mode, string? target, BattleArgument? picks)
        {
            target = Precheck(uid, mode, target, ReservationPicks(picks));
            var instance = state.Cards[uid]; var definition = EffectiveCard(uid); int cost = CostOf(uid, mode);
            state.Ap = checked(state.Ap - cost); if (state.FreeNext > 0) state.FreeNext--;
            if (Has("empty_clock") && !Flag(state.BattleFlags, "emptyClockUsed")) state.BattleFlags["emptyClockUsed"] = true;
            state.Zones.Hand.Remove(uid);
            bool held = instance.Held; instance.Held = false;
            state.Played = checked(state.Played + 1);
            string? owner = OwnerCharacter(instance);
            if (owner != null)
            {
                if (state.EchoActive && !string.IsNullOrEmpty(state.LastOwner) && state.LastOwner != owner) { state.Echo = StateNumber(BigInteger.Min(state.EchoMax, (BigInteger)state.Echo + 1)); EchoCheck(); }
                state.LastOwner = owner; if (!state.OwnersThisTurn.Contains(owner)) state.OwnersThisTurn.Add(owner);
                if (Has("unfinished_score") && state.OwnersThisTurn.Count >= 3 && !Flag(state.TurnFlags, "score")) { state.TurnFlags["score"] = true; Draw(1); }
            }
            if (definition.Keywords.Contains("contract")) state.Contracts = checked(state.Contracts + 1);
            if (state.RestraintActive && state.Played >= 4) { state.RestraintActive = false; state.DrawPenalty = checked(state.DrawPenalty + 1); Emit(new BattleEvent { E = "restraintBroken" }); }
            Emit(new BattleEvent { E = "play", Uid = uid, Id = instance.Id, Mode = mode, Cost = cost, Target = target });
            // JS spreads immediate-play picks only when entering resolution, after precheck/commit.
            if (mode == "now") ResolveCard(uid, null, target, ArgumentPicks(picks), held);
            else
            {
                state.ReservationSeq = checked(state.ReservationSeq + 1); int due = checked(state.Turn + (mode == "next" ? 1 : 2));
                state.Reservations.Add(new BattleReservationState { Uid = uid, Due = due, Order = state.ReservationSeq, Target = target, Kind = mode });
                Emit(new BattleEvent { E = "reserve", Uid = uid, Due = due, Kind = mode });
            }
            CheckEnd();
            RiftEchoAfterPlay(owner);
        }
        void Ult(string? actorId, string? target, BattleArgument? picks)
        {
            if (state.Phase != "PLAYER") throw new RuleException("WRONG_PHASE");
            var actor = Actor(actorId); if (actor == null) throw new RuleException("UNKNOWN_ACTOR");
            if (actor.Gauge < data.Rules.GaugeMax) throw new RuleException("ULT_NOT_READY");
            var effects = actor.UltReplace ?? data.CharacterIndex[actor.Id].Ult.Effects;
            if (effects.Any(o => o.Target == "one" || o.Op == "dmg" && string.IsNullOrEmpty(o.Target)))
            {
                var alive = state.Enemies.Where(e => e.Alive).ToList(); if (string.IsNullOrEmpty(target) && alive.Count == 1) target = alive[0].Uid;
                if (Enemy(target)?.Alive != true) throw new RuleException("BAD_TARGET");
            }
            actor.Gauge = checked(actor.Gauge - data.Rules.GaugeMax);
            var context = new EffectContext { Actor = actor, Attr = actor.Attr, Target = target, LastTarget = target, RawPicks = picks, IsUlt = true };
            Emit(new BattleEvent { E = "ult", Actor = actor.Id });
            foreach (var op in effects) { ExecuteOp(op, context); if (CheckEnd()) return; }
            foreach (var extra in actor.UltExtra)
            {
                if (extra.Once) { string key = "ultOnce_" + actor.Id; if (Flag(state.BattleFlags, key)) continue; state.BattleFlags[key] = true; }
                foreach (var op in extra.Effects) { ExecuteOp(op, context); if (CheckEnd()) return; }
            }
            if (HasHook(actor.Id, "sera_r6")) state.BattleFlags["seraR6Turn"] = checked(state.Turn + 1);
            if (HasHook(actor.Id, "kaya_r6") && context.Killed) actor.Gauge = StateNumber(BigInteger.Min(data.Rules.GaugeMax, (BigInteger)actor.Gauge + 50));
            CheckEnd();
        }
        void ResolveCard(string uid, string? reserved, string? target, IReadOnlyList<string> picks, bool held = false)
        {
            var instance = state.Cards[uid]; var definition = EffectiveCard(uid); string? owner = OwnerCharacter(instance); var actor = Actor(owner);
            var context = new EffectContext { Instance = instance, Definition = definition, Actor = actor, Attr = AttrOf(instance), Reserved = reserved, Held = held, Target = target, LastTarget = target, Picks = new List<string>(picks), FirstDamage = true };
            bool HasFixed(IEnumerable<EffectDef> effects) => effects.Any(o => o.Target == "fixed" || o.Then != null && HasFixed(o.Then) || o.Else != null && HasFixed(o.Else));
            if (HasFixed(definition.Effects))
            {
                var enemy = Enemy(target); if (enemy == null || !enemy.Alive || enemy.Status.Mark <= 0) { Emit(new BattleEvent { E = "fizzle", Uid = uid }); AfterResolve(instance, definition, context, true); return; }
            }
            else if (reserved != null && !string.IsNullOrEmpty(target) && Enemy(target)?.Alive != true) { context.Target = state.Enemies.FirstOrDefault(e => e.Alive)?.Uid; context.LastTarget = context.Target; }
            if (state.BonusAvailable && state.BonusTurn == state.Turn && definition.Effects.Any(o => o.Op == "dmg" || o.Op == "blk")) { context.RestraintBps = 3000; state.BonusAvailable = false; }
            if (state.NextBoost != null && owner != null && owner != state.NextBoost.From) { context.BoostBps = state.NextBoost.Bps; state.NextBoost = null; }
            if (actor != null) GainGauge(actor, data.Rules.GaugePerCard);
            if (actor != null && context.Held && Has("time_stamp")) GainGauge(actor, 5, true);
            for (int i = 0; i < definition.Effects.Count; i++)
            {
                ExecuteOp(definition.Effects[i], context);
                if (IsOver() || CheckEnd())
                {
                    // RULES_NOW_PLAY: growth comes with every resolution, so a winning blow still grows its card.
                    if (state.Phase == "VICTORY") foreach (var rest in definition.Effects.Skip(i + 1)) if (rest.Op == "grow") ExecuteOp(rest, context);
                    break;
                }
            }
            if (!IsOver())
            {
                foreach (var part in PartsOf(instance))
                {
                    if (part.Fx.ExtraBlock.GetValueOrDefault() != 0) GainBlockRaw(part.Fx.ExtraBlock!.Value, "part");
                    if (part.Fx.DrawOnce.GetValueOrDefault() != 0 && !Flag(state.TurnFlags, "recall_" + uid)) { state.TurnFlags["recall_" + uid] = true; Draw(part.Fx.DrawOnce!.Value); }
                    if (part.Fx.Gauge.GetValueOrDefault() != 0 && actor != null) GainGauge(actor, part.Fx.Gauge!.Value, true);
                    if (part.Fx.EchoCopy.GetValueOrDefault() != 0 && instance.EchoCopy != true)
                    {
                        state.PendingCopies = state.PendingCopies ?? new List<BattlePendingCopy>();
                        state.PendingCopies.Add(new BattlePendingCopy { Id = instance.Id, Wielder = instance.Wielder, From = instance.From, Due = checked(state.Turn + 1) });
                    }
                }
                if (IsGrowthInstance(instance)) OnGrowthResolved(instance);
                if (instance.Id == "sera_round" && HasHook("sera", "sera_r1") && !Flag(state.TurnFlags, "seraR1")) { state.TurnFlags["seraR1"] = true; Draw(1); }
            }
            AfterResolve(instance, definition, context, false);
        }
        void AfterResolve(BattleCardState instance, BattleCardDef definition, EffectContext context, bool fizzled)
        {
            instance.Used = checked(instance.Used + 1);
            if (definition.Keywords.Contains("exhaust") || PartsOf(instance).Any(p => p.Fx.Exhaust == true) || instance.EchoCopy == true || definition.Uses.GetValueOrDefault() != 0 && instance.Used >= definition.Uses)
                ExhaustCard(instance.Uid);
            else state.Zones.Discard.Add(instance.Uid);
            Emit(new BattleEvent { E = "resolved", Uid = instance.Uid, Id = instance.Id, Reserved = context.Reserved, Fizzled = fizzled });
        }
        void GainGauge(BattleActorState actor, int amount, bool bypassCap = false)
        {
            var weapon = WeaponPassive(actor.Id, "w_gaugeUp"); int gain = weapon.HasValue ? StateNumber(Floor((BigInteger)amount * (10000 + (BigInteger)weapon.Value.Value), 10000)) : amount;
            if (!bypassCap) gain = Math.Min(gain, checked(data.Rules.GaugeTurnCap - actor.GaugeTurn));
            if (gain <= 0) return;
            if (!bypassCap) actor.GaugeTurn = checked(actor.GaugeTurn + gain);
            actor.Gauge = StateNumber(BigInteger.Min(data.Rules.GaugeMax, (BigInteger)actor.Gauge + gain));
        }
        int EffectiveGrowth(BattleCardState instance) => !IsGrowthInstance(instance) ? 0 : data.CardIndex[instance.Id].Growth == true ? instance.Growth : 3;
        int ApplyGrowth(BattleCardState? instance, int amount, string source, bool capped = false, bool once = false)
        {
            if (instance == null || data.CardIndex[instance.Id].Growth != true || amount <= 0 || capped && state.GrowthCount >= state.GrowthCap || once && instance.Grown) return 0;
            int delta = Math.Min(3 - instance.Growth, amount); if (delta <= 0) return 0;
            instance.Growth += delta; if (capped) state.GrowthCount = checked(state.GrowthCount + 1); if (once) instance.Grown = true;
            Emit(new BattleEvent { E = "grow", Uid = instance.Uid, Growth = instance.Growth, Source = source }); if (Has("symbiotic_moss")) GainBlockRaw(3, "moss"); return delta;
        }
        void OnGrowthResolved(BattleCardState instance)
        {
            var yuri = PassiveOf("yuri_symbiosis"); int growth = EffectiveGrowth(instance);
            if (yuri != null && growth > 0) GainBlockRaw(StateNumber((BigInteger)growth * yuri.Params["perGrowth"]), "yuri");
            foreach (var power in state.Powers) if (power.Id == "greenhouse") GainBlockRaw(power.Block ?? 0, "greenhouse");
            var weapon = PartyWeapon("w_growthHeal"); if (weapon.HasValue) HealParty(weapon.Value.Value, fromWeapon: true);
            var bloom = RiftRule("rift_bloom"); if (bloom != null) GainBlockRaw(bloom["growthBlock"], "rift");
        }
        void ExhaustCard(string uid)
        {
            state.Zones.Hand.Remove(uid); state.Zones.Discard.Remove(uid); state.Zones.Draw.Remove(uid); state.Zones.Exhaust.Add(uid); state.Cards[uid].Held = false; state.Exhausted = checked(state.Exhausted + 1);
            Emit(new BattleEvent { E = "exhaust", Uid = uid });
            foreach (var power in state.Powers) if (power.Id == "oblivion") GainBlockRaw(power.Block ?? 0, "oblivion");
            var weapon = PartyWeapon("w_exhaustGuard"); if (weapon.HasValue) GainBlockRaw(weapon.Value.Value, "weapon");
            if (Has("void_shard")) { var enemy = RandomEnemy(); if (enemy != null) DamageEnemyRaw(enemy, 3, "relic"); }
            if (Has("oblivion_ink") && !Flag(state.TurnFlags, "ink") && state.Phase == "PLAYER") { state.TurnFlags["ink"] = true; state.Ap = StateNumber(BigInteger.Min(data.Rules.ApMax, (BigInteger)state.Ap + 1)); }
        }
        void DeliverCopies()
        {
            if (state.PendingCopies == null || state.PendingCopies.Count == 0) return;
            var due = state.PendingCopies.Where(p => p.Due <= state.Turn).ToList(); state.PendingCopies = state.PendingCopies.Where(p => p.Due > state.Turn).ToList();
            foreach (var copy in due)
            {
                string uid = MakeCard(state, data, new BattleCardSpec { Id = copy.Id, Wielder = copy.Wielder, From = copy.From }, true, true);
                state.Cards[uid].Temp.Add("echoHalf"); state.Cards[uid].EchoCopy = true;
                if (state.Zones.Hand.Count < state.HandMax) state.Zones.Hand.Add(uid); else state.Zones.Discard.Add(uid);
                Emit(new BattleEvent { E = "echoCopy", Uid = uid, Id = copy.Id });
            }
        }
        string? TakePick(EffectContext context, List<string> zone, Func<string, bool> valid, string fallback)
        {
            if (context.RawPicks != null)
            {
                var raw = context.RawPicks;
                // Ult keeps raw picks: only a reached choice operation reads length/shift.
                if (raw.ListValue != null) context.Picks = ArgumentPicks(raw);
                else if (!string.IsNullOrEmpty(raw.TextValue) || raw.Member("length")?.Truthy == true) throw new RuleException("INVALID_COMMAND_ARGS");
                context.RawPicks = null;
            }
            while (context.Picks.Count != 0) { string pick = context.Picks[0]; context.Picks.RemoveAt(0); if (zone.Contains(pick) && valid(pick)) return pick; }
            var candidates = zone.Where(valid);
            if (fallback == "highestCost") candidates = candidates.OrderByDescending(uid => data.CardIndex[state.Cards[uid].Id].Cost);
            else if (fallback != "first") candidates = candidates.OrderBy(uid => data.CardIndex[state.Cards[uid].Id].Rarity == "C" ? -1 : 0).ThenBy(uid => data.CardIndex[state.Cards[uid].Id].Cost);
            return candidates.FirstOrDefault();
        }
        void AttachTemp(string uid, string part)
        {
            if (!state.Cards.TryGetValue(uid, out var instance) || instance.Temp.Contains(part)) return;
            instance.Temp.Add(part); state.TempParts = checked(state.TempParts + 1); Emit(new BattleEvent { E = "tune", Uid = uid, Part = part });
        }
        void EchoCheck()
        {
            if (state.Echo >= 3 && state.Powers.Any(p => p.Id == "echochamber") && !Flag(state.TurnFlags, "echoChamber")) { state.TurnFlags["echoChamber"] = true; Draw(1); }
        }
    }
}
