using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MemoryForge.Core
{
    public sealed partial class Battle
    {
        bool Test(EffectConditionDef condition, EffectContext context, string? target = null) => Condition(condition, context.Instance, context.Held, target ?? context.LastTarget, context.Killed);
        int Value(EffectDef op, EffectContext context) => context.Reserved == "after" ? op.Ra ?? op.Rv ?? op.V ?? 0 : context.Reserved == "next" ? op.Rv ?? op.V ?? 0 : op.V ?? 0;
        int ReservedNumber(EffectDef op, EffectContext context) => context.Reserved != null ? op.Rn ?? op.N ?? 0 : op.N ?? 0;
        BigInteger CounterValue(string per, EffectContext context)
        {
            switch (per)
            {
                case "echo": return state.Echo;
                case "exhausted": return (BigInteger)state.Exhausted + state.ExhaustStart;
                case "debt": return DebtTotal();
                case "growthSum": return state.CardOrder.Aggregate(BigInteger.Zero, (n, uid) => n + EffectiveGrowth(state.Cards[uid]));
                case "growthCards": return state.CardOrder.Count(uid => IsGrowthInstance(state.Cards[uid]));
                case "counter": return state.Counter;
                case "missingHp10": return Floor((BigInteger)state.MaxHp - state.Hp, 10);
                case "contracts": return state.Contracts;
                case "tempParts": return state.TempParts;
                case "block": return state.Block;
                case "lastCount": return context.LastCount;
                case "targetBurn": return Enemy(context.LastTarget)?.Status.Burn ?? 0;
                default: throw new RuleException("BATTLE_DEFINITION_NOT_IMPLEMENTED");
            }
        }
        BigInteger Scaled(BigInteger value, EffectScaleDef? scale, EffectContext context)
        {
            if (scale == null) return value;
            BigInteger add = scale.K * CounterValue(scale.Per, context);
            return value + (scale.Max.HasValue ? BigInteger.Min(add, scale.Max.Value) : add);
        }
        List<BattleEnemyState> Targets(string? target, EffectContext context)
        {
            var alive = state.Enemies.Where(e => e.Alive).ToList();
            BattleEnemyState? enemy;
            switch (target)
            {
                case "all": return alive;
                case "rand": return alive.Count == 0 ? new List<BattleEnemyState>() : new List<BattleEnemyState> { alive[Below(alive.Count)] };
                case "fixed": enemy = Enemy(context.Target); return enemy?.Alive == true && enemy.Status.Mark > 0 ? new List<BattleEnemyState> { enemy } : new List<BattleEnemyState>();
                case "same": enemy = Enemy(context.LastTarget); break;
                default: enemy = Enemy(context.Target); break;
            }
            return enemy?.Alive == true ? new List<BattleEnemyState> { enemy } : alive.Take(1).ToList();
        }
        BigInteger DamageBonus(EffectContext context, BattleEnemyState? enemy)
        {
            BigInteger add = 0; var instance = context.Instance; string? owner = context.Source?.Id;
            if (instance != null)
            {
                foreach (var part in PartsOf(instance))
                {
                    var fx = part.Fx; add += (BigInteger)(fx.AddBps ?? 0) + (fx.AllBps ?? 0);
                    add += context.Reserved != null ? fx.ResBps ?? 0 : fx.ImmBps ?? 0;
                    if (context.Reserved == "after") add += fx.AfterBps ?? 0;
                }
                if (instance.Temp.Contains("echoHalf")) add -= 5000;
                if (PartsOf(instance).Count != 0)
                {
                    add += PassiveOf("nia_handiwork")?.Params["bps"] ?? 0;
                    foreach (var power in state.Powers) if (power.Id == "blueprint") add += power.Bps ?? 0;
                    if (Has("golden_quill")) add += 1500;
                }
                if (!string.IsNullOrEmpty(instance.From) && SourceHook(context.Source, "yuri_r4") && instance.Owner == "yuri") add += 1500;
                if (owner != null)
                {
                    if (context.Held) add += SourceWeapon(context.Source, "w_reservedDmg")?.Value ?? 0;
                    if (enemy != null && enemy.Status.Mark > 0) add += SourceWeapon(context.Source, "w_markedDmg")?.Value ?? 0;
                    if ((BigInteger)state.Hp * 2 <= state.MaxHp) add += SourceWeapon(context.Source, "w_lowHpDmg")?.Value ?? 0;
                    if (enemy != null && enemy.BrokenUntil != 0) add += (SourceWeapon(context.Source, "w_brokenDmg") ?? SourceWeapon(context.Source, "w_breakDmg"))?.Value ?? 0;
                    add += SourceWeapon(context.Source, "w_none")?.Alt ?? 0;
                    if (!state.FirstAttackDone) add += SourceWeapon(context.Source, "w_firstAtk")?.Value ?? 0;
                }
            }
            if (DebtTotal() > 0) add += PartyWeapon("w_debtDmg")?.Value ?? 0;
            add += (BigInteger)(context.Source?.GearBps ?? 0) + context.RestraintBps + context.BoostBps + context.ChargeBps;
            if (owner == "sera" && context.Instance != null)
            {
                if (enemy != null && enemy.BrokenUntil != 0) add += PassiveOf("sera_foresight")?.Params["bps"] ?? 0;
                add += Buff("seraLock")?.N ?? 0;
                if (IsTurnFlag(state.BattleFlags, "seraR6Active", state.Turn)) add += 5000;
            }
            if (owner == "ravien" && DebtTotal() > 0) add += PassiveOf("ravien_credit")?.Params["bps"] ?? 0;
            if (owner == "sion")
            {
                var passive = PassiveOf("sion_shelf"); if (passive != null) add += BigInteger.Min(passive.Params["maxBps"], (BigInteger)passive.Params["perBps"] * ((BigInteger)state.Exhausted + state.ExhaustStart));
            }
            if (owner == "lumi")
            {
                var passive = PassiveOf("lumi_critical"); if (passive != null && (BigInteger)state.Hp * 100 <= (BigInteger)state.MaxHp * passive.Params["pct"]) add += passive.Params["bps"];
                add += Buff("lumiRush")?.N ?? 0;
            }
            foreach (var power in state.Powers) if (power.Id == "casefile" && enemy != null && enemy.Status.Mark > 0) add += power.Bps ?? 0;
            if (Has("ember_ring") && enemy != null && enemy.Status.Burn > 0) add += 1000;
            if (context.CreditLedger) add += 2000;
            return add + RiftDamageBonus(context);
        }
        long EffectLevel(EffectContext context) => context.Source == null ? 1 : context.IsUlt ? context.Source.UltLv : context.Instance?.Owner == context.Source.Id ? context.Source.SkillLv : 1;
        int CalculateDamage(EffectDef op, EffectContext context, BattleEnemyState enemy)
        {
            BigInteger value = Scaled(Value(op, context), op.Scale, context) + context.FlatBonus; if (value <= 0) return 0;
            BigInteger num = value * (context.Source?.Power4 ?? PartyPower()) * (10000 + 400 * ((BigInteger)EffectLevel(context) - 1)) * BigInteger.Max(0, 10000 + DamageBonus(context, enemy));
            BigInteger den = (BigInteger)160 * 10000 * 10000;
            if (enemy.Status.Vul > 0) { num *= 10000 + (BigInteger)data.Rules.VulnerableBps; den *= 10000; }
            if (enemy.BrokenUntil != 0) { num *= 10000 + (BigInteger)data.Rules.BrokenDamageBps; den *= 10000; }
            if (state.Weak > 0) { num *= 10000 - (BigInteger)data.Rules.WeakBps; den *= 10000; }
            if (op.Multipliers != null) foreach (var mult in op.Multipliers) if (Test(mult.Condition, context, enemy.Uid)) num *= mult.Multiplier;
            return StateNumber(num / den);
        }
        int CalculateBlock(EffectDef op, EffectContext context)
        {
            BigInteger value = Scaled(Value(op, context), op.Scale, context); if (value <= 0) return 0;
            BigInteger add = (BigInteger)context.RestraintBps + context.BoostBps + (context.Source?.GearBps ?? 0); var instance = context.Instance;
            if (instance != null)
            {
                foreach (var part in PartsOf(instance))
                {
                    var fx = part.Fx; if (fx.Exhaust != true) add += fx.AddBps ?? 0; add += fx.AllBps ?? 0;
                    add += context.Reserved != null ? fx.ResBps ?? 0 : fx.ImmBps ?? 0;
                    if (context.Reserved == "after") add += fx.AfterBps ?? 0;
                }
                if (instance.Temp.Contains("echoHalf")) add -= 5000;
                if (PartsOf(instance).Count != 0)
                {
                    add += PassiveOf("nia_handiwork")?.Params["bps"] ?? 0;
                    foreach (var power in state.Powers) if (power.Id == "blueprint") add += power.Bps ?? 0;
                    if (Has("golden_quill")) add += 1500;
                }
                if (!string.IsNullOrEmpty(instance.From) && SourceHook(context.Source, "yuri_r4") && instance.Owner == "yuri") add += 1500;
                if (context.Source != null) { add += SourceWeapon(context.Source, "w_blockUp")?.Value ?? 0; add += SourceWeapon(context.Source, "w_none")?.Alt ?? 0; }
            }
            // base0 is retained as metadata; the frozen JS/live sion_forget uses its v5 base.
            return StateNumber(value * (context.Source?.Vit ?? PartyVitality()) * (10000 + 400 * ((BigInteger)EffectLevel(context) - 1)) * BigInteger.Max(0, 10000 + add) / ((BigInteger)81 * 10000 * 10000));
        }
        int HealParty(int amount, bool preScaled = false, bool fromWeapon = false, int overflowBlock = 0, bool ultAila = false)
        {
            if (state.Hp <= 0) return 0;
            if (!preScaled) amount = ScaleVitality(amount);
            var weapon = PartyWeapon("w_healUp"); if (weapon.HasValue && !fromWeapon) amount = StateNumber(Floor((BigInteger)amount * (10000 + (BigInteger)weapon.Value.Value), 10000));
            int before = state.Hp; state.Hp = StateNumber(BigInteger.Min(state.MaxHp, (BigInteger)state.Hp + amount));
            int overflow = StateNumber((BigInteger)amount - (state.Hp - (BigInteger)before));
            int pct = Math.Max(PassiveOf("aila_warmth")?.Params["pct"] ?? 0, overflowBlock); if (ultAila) pct = 100;
            if (overflow > 0 && pct > 0) GainBlockRaw(StateNumber(Floor((BigInteger)overflow * pct, 100)), "warmth");
            Emit(new BattleEvent { E = "heal", N = checked(state.Hp - before) }); return overflow;
        }
        void BreakEnemy(BattleEnemyState enemy)
        {
            enemy.BrokenUntil = checked(state.Turn + 1); enemy.Stunned = true; enemy.Intent = new EnemyMoveDef { Type = "stun", Label = "균열: 행동 불가" };
            if (state.BreakApTurn < data.Rules.BreakApBonusMaxPerTurn) { state.ApNext = checked(state.ApNext + data.Rules.BreakApBonus); state.BreakApTurn = checked(state.BreakApTurn + 1); }
            Emit(new BattleEvent { E = "break", Uid = enemy.Uid }); if (Has("silver_needle")) HealParty(3);
        }
        int BreakFor(EffectDef op, EffectContext context, BattleEnemyState enemy)
        {
            BigInteger amount = op.Brk ?? 1;
            if (context.Instance != null) foreach (var part in PartsOf(context.Instance)) amount += part.Fx.Brk ?? 0;
            if (context.Held && Has("precision_lens")) amount++;
            if (Has("eye_of_rift")) amount++;
            if (context.Attr == "frost" && Has("frost_crystal") && !Flag(state.TurnFlags, "frostCrystal") && enemy.Weak.Contains("frost")) { state.TurnFlags["frostCrystal"] = true; amount++; }
            return StateNumber(amount);
        }
        void HitEnemy(BattleEnemyState enemy, int amount, int pierceBps, int brk, int shock, EffectContext context)
        {
            int pierce = StateNumber(Floor((BigInteger)amount * pierceBps, 10000)); int normal = checked(amount - pierce), blocked = Math.Min(enemy.Block, normal);
            enemy.Block = checked(enemy.Block - blocked); enemy.Hp = StateNumber(BigInteger.Max(0, (BigInteger)enemy.Hp - ((BigInteger)amount - blocked)));
            Emit(new BattleEvent { E = "dmg", Uid = enemy.Uid, N = amount, Blocked = blocked, Source = context.IsUlt ? "ult" : "card", Card = context.Instance?.Id });
            if (!context.IsUlt && enemy.Status.Reflect > 0) { int reflect = ScaleEnemy(enemy.Status.Reflect), absorbed = Math.Min(state.Block, reflect); state.Block = checked(state.Block - absorbed); if (reflect - (long)absorbed > 0) LoseHp(checked(reflect - absorbed), "reflect"); }
            if (enemy.BrokenUntil == 0)
            {
                int reduce = StateNumber((BigInteger)(enemy.Weak.Contains(context.Attr) ? brk : 0) + shock);
                if (reduce > 0) { enemy.Brk = StateNumber(BigInteger.Max(0, (BigInteger)enemy.Brk - reduce)); if (enemy.Brk == 0) BreakEnemy(enemy); }
            }
            if (context.Instance != null && enemy.Alive) foreach (var part in PartsOf(context.Instance))
            {
                enemy.Status.Mark = checked(enemy.Status.Mark + (part.Fx.MarkOnHit ?? 0)); enemy.Status.Burn = checked(enemy.Status.Burn + (part.Fx.BurnOnHit ?? 0)); enemy.Status.Weak = checked(enemy.Status.Weak + (part.Fx.WeakOnHit ?? 0));
            }
            if (enemy.Hp == 0) KillEnemy(enemy, context);
        }

        void ExecuteOp(EffectDef op, EffectContext context)
        {
            var s = state;
            switch (op.Op)
            {
                case "dmg":
                    if (context.FirstDamage)
                    {
                        context.FirstDamage = false; var charge = Buff("charge");
                        if (charge != null && !context.IsUlt) { context.ChargeBps = charge.N; s.Buffs.Remove(charge); }
                        if (Has("credit_ledger") && DebtTotal() > 0 && !Flag(s.TurnFlags, "ledger") && !context.IsUlt) { s.TurnFlags["ledger"] = true; context.CreditLedger = true; }
                        if (context.Attr == "volt" && Has("volt_coil") && !Flag(s.TurnFlags, "voltCoil")) { s.TurnFlags["voltCoil"] = true; context.FlatBonus = 4; }
                        if (context.Instance != null && context.Source != null) { var weapon = SourceWeapon(context.Source, "w_blockOnAtk"); if (weapon.HasValue) GainBlockRaw(weapon.Value.Value, "weapon"); }
                    }
                    int hits = StateNumber(Scaled(op.Hits ?? 1, op.HitsScale, context));
                    for (int h = 0; h < hits; h++)
                    {
                        foreach (var enemy in Targets(op.Target, context))
                        {
                            if (!enemy.Alive) continue;
                            int amount = CalculateDamage(op, context, enemy);
                            if (h == 0 && hits >= 2 && context.Source != null) amount = checked(amount + (SourceWeapon(context.Source, "w_multiHit")?.Value ?? 0));
                            context.LastTarget = enemy.Uid; int pierce = op.Pierce ?? 0;
                            if (context.Instance != null) foreach (var part in PartsOf(context.Instance)) if (part.Fx.Pierce.GetValueOrDefault() != 0) pierce = Math.Max(pierce, part.Fx.Pierce!.Value);
                            HitEnemy(enemy, amount, pierce, BreakFor(op, context, enemy), op.Shock ?? 0, context);
                            if (CheckEnd()) break;
                        }
                        if (IsOver() || !s.Enemies.Any(e => e.Alive)) break;
                    }
                    context.FlatBonus = 0;
                    if (context.Instance != null && !s.FirstAttackDone && data.CardIndex[context.Instance.Id].Type == "atk") s.FirstAttackDone = true;
                    if (!s.Enemies.Any(e => e.Alive)) CheckEnd();
                    break;
                case "blk":
                    int block = CalculateBlock(op, context); GainBlockRaw(block, "card");
                    var harin = PassiveOf("harin_stance"); if (harin != null && context.Actor?.Id == "harin" && block > 0) s.Counter = checked(s.Counter + harin.Params["n"]); break;
                case "heal":
                    int heal = StateNumber(BigInteger.Max(0, Scaled(Value(op, context), op.Scale, context)) * (context.Source?.Vit ?? PartyVitality()) * (10000 + 400 * ((BigInteger)EffectLevel(context) - 1)) / ((BigInteger)81 * 10000));
                    var healUp = Buff("healUp"); if (healUp != null) { heal = StateNumber(Floor((BigInteger)heal * (10000 + (BigInteger)healUp.N), 10000)); s.Buffs.Remove(healUp); }
                    HealParty(heal, true, overflowBlock: op.OverflowBlock ?? 0, ultAila: context.IsUlt && SourceHook(context.Source, "aila_r6")); break;
                case "st": foreach (var enemy in Targets(op.Target, context)) { AddStatus(enemy, op.Status!, ReservedNumber(op, context)); context.LastTarget = enemy.Uid; } break;
                case "buff":
                    int until = checked(s.Turn + Or(op.Turns ?? 1, 1) - 1); var existing = Buff(op.Status!);
                    if (existing != null) { existing.N = Math.Max(existing.N, op.N!.Value); existing.Until = Math.Max(existing.Until, until); }
                    else s.Buffs.Add(new BattleBuff { Status = op.Status!, N = op.N!.Value, Until = until }); break;
                case "counter": s.Counter = checked(s.Counter + ReservedNumber(op, context)); break;
                case "draw": Draw(StateNumber(Scaled(ReservedNumber(op, context), op.Scale, context))); break;
                case "discard":
                    for (int k = 0; k < op.N!.Value && s.Zones.Hand.Count != 0; k++) { var uid = TakePick(context, s.Zones.Hand, _ => true, "lowestValue"); if (uid == null) break; s.Zones.Hand.Remove(uid); s.Zones.Discard.Add(uid); s.Cards[uid].Held = false; } break;
                case "ap": s.Ap = StateNumber(BigInteger.Min(data.Rules.ApMax, (BigInteger)s.Ap + op.N!.Value)); break;
                case "apNext": s.ApNext = checked(s.ApNext + op.N!.Value); break;
                case "gauge": foreach (var actor in op.Who == "all" ? s.Actors : context.Actor == null ? new List<BattleActorState>() : new List<BattleActorState> { context.Actor }) GainGauge(actor, op.N!.Value, true); break;
                case "create":
                    for (int k = 0; k < op.N!.Value; k++)
                    {
                        string uid = MakeCard(s, data, new BattleCardSpec { Id = op.Card!, Wielder = context.Instance?.Wielder, SupportSourceId = context.Source != null && s.Support != null && context.Source.Id == s.Support.Source.Id && data.CardIndex.TryGetValue(op.Card!, out var created) && created.Owner == context.Source.Id ? context.Source.Id : null }, true);
                        if (op.Zone == "hand" && s.Zones.Hand.Count < s.HandMax) s.Zones.Hand.Add(uid); else s.Zones.Discard.Add(uid);
                    }
                    break;
                case "pull":
                    var reservation = PickReservation(context.RawPicks != null ? ReservationPicks(context.RawPicks) : context.Picks); if (reservation == null) break;
                    if (reservation.Due > (long)s.Turn + 1)
                    {
                        if (FreeSlots("next") <= 0) throw new RuleException("PULL_DESTINATION_FULL");
                        reservation.Due--; reservation.Kind = "next"; reservation.Delayed = null; Emit(new BattleEvent { E = "pull", Uid = reservation.Uid, Due = reservation.Due });
                    }
                    else { s.Reservations.Remove(reservation); Emit(new BattleEvent { E = "pull", Uid = reservation.Uid, Due = s.Turn }); ResolveCard(reservation.Uid, reservation.Kind, reservation.Target, new List<string>()); }
                    break;
                case "slot":
                    s.SlotsTemp.Next = StateNumber(BigInteger.Max(0, BigInteger.Min((BigInteger)data.Rules.NextSlotsMax - s.SlotsBase.Next, (BigInteger)s.SlotsTemp.Next + (op.Next ?? 0))));
                    s.SlotsTemp.After = StateNumber(BigInteger.Max(0, BigInteger.Min((BigInteger)data.Rules.AfterSlotsMax - s.SlotsBase.After, (BigInteger)s.SlotsTemp.After + (op.After ?? 0)))); break;
                case "grow": if (context.Instance != null) ApplyGrowth(context.Instance, 1, "resolve", true, true); break;
                case "growPick":
                    if (s.GrowthCount >= s.GrowthCap) break;
                    var growthUid = TakePick(context, s.Zones.Hand, uid => data.CardIndex[s.Cards[uid].Id].Growth == true && s.Cards[uid].Growth < 3, "first");
                    if (growthUid != null) ApplyGrowth(s.Cards[growthUid], op.N!.Value, "growPick", true); break;
                case "growLowest":
                    var growths = s.CardOrder.Select(uid => s.Cards[uid]).Where(c => data.CardIndex[c.Id].Growth == true && c.Growth < 3).OrderBy(c => c.Growth).ThenBy(c => c.Uid, StringComparer.Ordinal).ToList();
                    foreach (var card in growths.Take(op.N!.Value >= 0 ? op.N.Value : Math.Max(0, growths.Count + op.N.Value))) ApplyGrowth(card, 1, "growLowest"); break;
                case "growAll": foreach (string uid in s.CardOrder) ApplyGrowth(s.Cards[uid], 1, "growAll"); break;
                case "loan":
                    s.Ap = StateNumber(BigInteger.Min(data.Rules.ApMax, (BigInteger)s.Ap + op.N!.Value)); s.ReservationSeq = checked(s.ReservationSeq + 1);
                    s.Debts.Add(new BattleDebt { Id = "d" + s.ReservationSeq, N = op.N.Value, Due = checked(s.Turn + 1), Deferred = false }); Emit(new BattleEvent { E = "loan", N = op.N.Value }); break;
                case "defer":
                    var debt = s.Debts.FirstOrDefault(d => d.Due == (long)s.Turn + 1 && !d.Deferred);
                    if (debt != null) { debt.Due = checked(debt.Due + 1); debt.N = checked(debt.N + 1); debt.Deferred = true; Emit(new BattleEvent { E = "defer", Id = debt.Id }); } break;
                case "restraint": s.RestraintActive = true; s.RestraintUsedTurn = s.Turn; break;
                case "collateral":
                    GainBlockRaw(op.V!.Value, "collateral"); s.ReservationSeq = checked(s.ReservationSeq + 1);
                    s.Debts.Add(new BattleDebt { Id = "d" + s.ReservationSeq, N = 1, Due = checked(s.Turn + 1), Deferred = false }); Emit(new BattleEvent { E = "loan", N = 1 }); break;
                case "selfdmg":
                    int cost = OverloadCost(op.V!.Value, false); if (cost > 0) { LoseHp(cost, "overload"); if (Has("lightning_rod")) GainBlockRaw(StateNumber(Floor(cost, 2)), "rod"); } break;
                case "cleanse": context.LastCount = s.Weak > 0 ? 1 : 0; s.Weak = 0; break;
                case "cleanseOne": if (op.Status == "weak" && s.Weak > 0) s.Weak = 0; break;
                case "exhaustPick":
                    int exhausted = 0;
                    // Exhaust hooks can reshuffle and replace the discard list between picks.
                    for (int k = 0; k < op.N!.Value; k++) { var uid = TakePick(context, op.From == "discard" ? s.Zones.Discard : s.Zones.Hand, _ => true, "lowestValue"); if (uid == null) break; ExhaustCard(uid); exhausted++; } context.LastCount = exhausted; break;
                case "exhaustHand": var hand = s.Zones.Hand.ToList(); foreach (var uid in hand) ExhaustCard(uid); context.LastCount = hand.Count; break;
                case "tune":
                    if (op.Pick == "all") foreach (var uid in s.Zones.Hand) AttachTemp(uid, op.Part!);
                    else if (op.Pick == "random") TuneRandom(op.Part!);
                    else if (op.Pick == "same") { if (context.LastPicked != null && s.Zones.Hand.Contains(context.LastPicked)) AttachTemp(context.LastPicked, op.Part!); }
                    else { var uid = TakePick(context, s.Zones.Hand, u => !s.Cards[u].Temp.Contains(op.Part!) && data.CardIndex[s.Cards[u].Id].Rarity != "C", "highestCost"); if (uid != null) { AttachTemp(uid, op.Part!); context.LastPicked = uid; } } break;
                case "costdown":
                    if (op.Pick == "all") foreach (string uid in s.Zones.Hand) s.Cards[uid].CostMod = checked(s.Cards[uid].CostMod - op.N!.Value);
                    else { var uid = TakePick(context, s.Zones.Hand, u => CostOf(u, "now") > 0, "highestCost"); if (uid != null) { s.Cards[uid].CostMod = checked(s.Cards[uid].CostMod - op.N!.Value); context.LastPicked = uid; } } break;
                case "recall":
                    if (s.Zones.Hand.Count >= s.HandMax) break;
                    var recall = TakePick(context, s.Zones.Discard, uid => data.CardIndex[s.Cards[uid].Id].Rarity != "C", "highestCost"); if (recall != null) { s.Zones.Discard.Remove(recall); s.Cards[recall].Held = false; s.Zones.Hand.Add(recall); } break;
                case "copyPick":
                    var sourceUid = TakePick(context, s.Zones.Hand, uid => IsGrowthInstance(s.Cards[uid]), "first");
                    if (sourceUid != null && s.Zones.Hand.Count < s.HandMax)
                    {
                        var original = s.Cards[sourceUid]; string uid = MakeCard(s, data, new BattleCardSpec { Id = original.Id, Plus = original.Plus, Circuit = original.Circuit, Edge = original.Edge, From = original.From, SupportSourceId = original.SupportSourceId }, true, true);
                        s.Cards[uid].Growth = original.Growth; s.Cards[uid].EchoCopy = true; s.Zones.Hand.Add(uid);
                    }
                    break;
                case "power":
                    var power = s.Powers.FirstOrDefault(p => p.Id == op.Id);
                    if (power == null) s.Powers.Add(new BattlePowerState { Id = op.Id!, Bps = op.Bps, Block = op.Block, Keep = op.Keep, Heal = op.Heal });
                    else { if (op.Bps.HasValue) power.Bps = checked((power.Bps ?? 0) + op.Bps.Value); if (op.Block.HasValue) power.Block = checked((power.Block ?? 0) + op.Block.Value); if (op.Keep.HasValue) power.Keep = checked((power.Keep ?? 0) + op.Keep.Value); if (op.Heal.HasValue) power.Heal = checked((power.Heal ?? 0) + op.Heal.Value); }
                    Emit(new BattleEvent { E = "power", Id = op.Id }); break;
                case "echo": if (s.EchoActive) { s.Echo = StateNumber(BigInteger.Min(s.EchoMax, (BigInteger)s.Echo + op.N!.Value)); EchoCheck(); } break;
                case "nextBoost": s.NextBoost = new BattleBoostState { Bps = op.Bps!.Value, From = context.Source?.Id }; break;
                case "freeNext": s.FreeNext = Math.Max(s.FreeNext, op.N!.Value); break;
                case "clearDebt": context.LastCount = DebtTotal(); s.Debts.Clear(); break;
                case "breakBonus": case "breakBonusAll":
                    int bonus = StateNumber(Scaled(op.N ?? 0, op.Scale, context));
                    foreach (var enemy in op.Op == "breakBonusAll" ? s.Enemies.Where(e => e.Alive).ToList() : new[] { Enemy(context.LastTarget) }.Where(e => e != null).Select(e => e!).ToList())
                        if (enemy.Alive && enemy.BrokenUntil == 0 && enemy.Weak.Contains(context.Attr) && bonus > 0) { enemy.Brk = StateNumber(BigInteger.Max(0, (BigInteger)enemy.Brk - bonus)); if (enemy.Brk == 0) BreakEnemy(enemy); } break;
                case "if": foreach (var branch in Test(op.Condition!, context) ? op.Then! : op.Else ?? new List<EffectDef>()) { ExecuteOp(branch, context); if (IsOver()) break; } break;
                default: throw new RuleException("UNSUPPORTED_EFFECT");
            }
        }
    }
}
