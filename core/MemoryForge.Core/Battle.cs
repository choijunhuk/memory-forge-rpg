// §5.3/5.7-9/5.13-18: real Create/begin/end with skipped player turns. Play/ult/Run remain unported.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MemoryForge.Core
{
    public sealed class Battle
    {
        readonly BattleData data;
        BattleState state;
        readonly Rng rng;
        readonly Dictionary<string, Receipt> receipts = new Dictionary<string, Receipt>(StringComparer.Ordinal);
        sealed class Receipt
        {
            internal string Identity = "";
            internal BattleResponse Response = new BattleResponse();
        }
        Battle(BattleData data, BattleState state) { this.data = data; this.state = state; rng = new Rng(state.Rng); }
        /// <summary>Detached snapshot. Execution is only through Create/begin/end; restoration is not exposed.</summary>
        public BattleState State => state.Copy();

        /// <summary>Combat state numbers use Int32, except skill/ultimate levels. Out-of-range results reject explicitly.</summary>
        public static Battle Create(BattleData data, BattleSetup setup)
        {
            if (setup.Party.Count < 1 || setup.Party.Count > 3) throw new RuleException("PARTY_SIZE");
            data.Index();
            var rules = data.Rules;
            var s = new BattleState
            {
                Rng = Rng.SeedState(setup.Seed ?? 20261014), Hp = setup.Hp, MaxHp = setup.MaxHp, ApNext = setup.NextApBonus,
                DebtCap = rules.DebtCap, GrowthCap = rules.GrowthCapPerBattle, HandMax = rules.HandMax,
                SlotsBase = new BattleSlots { Next = rules.NextSlots, After = rules.AfterSlots },
                Relics = new List<string>(setup.Relics), RunFlags = BattleGraph.CopyDictionary(setup.RunFlags),
                DifficultyHp = Or(setup.DifficultyHp, 100), DifficultyDamage = Or(setup.DifficultyDamage, 100)
            };
            foreach (var p in setup.Party)
            {
                if (!data.CharacterIndex.TryGetValue(p.CharId, out var ch)) throw new RuleException("UNKNOWN_CHARACTER");
                int resonance = Math.Max(0, Math.Min(6, p.Res));
                var a = new BattleActorState
                {
                    Id = ch.Id, Name = ch.Name, Attr = ch.Attr, Gauge = p.Gauge, Res = resonance,
                    Power4 = Or(p.Power4, 160), Vit = Or(p.Hp, 81), GearBps = p.GearBps,
                    SkillLv = Math.Max(1, Or(p.SkillLv, 1)), UltLv = Math.Max(1, Or(p.UltLv, 1)), Passive = ch.Passive.Copy(),
                    Weapon = string.IsNullOrEmpty(p.WeaponId) ? null : new BattleWeaponState { Id = p.WeaponId!, Refine = Math.Max(1, Math.Min(5, Or(p.Refine, 1))) }
                };
                foreach (var fx in ch.Resonance.Take(resonance)) ApplyResonance(s, a, fx);
                s.Actors.Add(a);
            }
            var rien = s.Actors.FirstOrDefault(a => a.Id == "rien");
            if (rien != null) { s.EchoActive = true; s.EchoMax = rien.Passive.Params["max"]; }
            if (s.Relics.Contains("tuning_fork")) { s.EchoMax++; s.EchoStart = Math.Max(s.EchoStart, 1); }
            if (s.Relics.Contains("contract_seal")) s.DebtCap++;
            if (s.Relics.Contains("cracked_hourglass")) s.SlotsBase.After = Math.Min(rules.AfterSlotsMax, s.SlotsBase.After + 1);
            if (s.Relics.Contains("twin_pendulum")) { s.SlotsBase.Next = Math.Min(rules.NextSlotsMax, s.SlotsBase.Next + 1); s.HandMax = 9; }
            if (s.Relics.Contains("infinite_circuit")) s.SlotsBase.Next = Math.Min(rules.NextSlotsMax, s.SlotsBase.Next + 1);
            foreach (var spec in setup.Deck) s.Zones.Draw.Add(MakeCard(s, data, spec));
            if (s.Relics.Contains("eye_of_rift")) s.Zones.Draw.Add(MakeCard(s, data, new BattleCardSpec { Id = "smudge" }, true));
            foreach (string id in setup.Enemies) AddEnemy(s, data, id);
            var battle = new Battle(data, s); battle.CheckInvariants(); return battle;
        }

        static int Or(int value, int fallback) => value == 0 ? fallback : value;
        static string? OrNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
        static BattleEnemyState AddEnemy(BattleState s, BattleData data, string id)
        {
            if (!data.EnemyIndex.TryGetValue(id, out var definition)) throw new RuleException("UNKNOWN_ENEMY");
            if (definition.Passive != null && definition.Passive.Id != "seedHeal") throw new RuleException("BATTLE_ENEMY_PASSIVE_NOT_IMPLEMENTED");
            int hp = StateNumber(BigInteger.Max(1, Round((BigInteger)definition.Hp * s.DifficultyHp, 100)));
            s.Seq = StateNumber((BigInteger)s.Seq + 1);
            var enemy = new BattleEnemyState { Uid = "e" + s.Seq, Id = id, Name = definition.Name, Tier = definition.Tier,
                Hp = hp, MaxHp = hp, Brk = definition.Brk, BrkMax = definition.Brk, Weak = new List<string>(definition.Weak), Passive = definition.Passive?.Copy() };
            s.Enemies.Add(enemy); return enemy;
        }
        static string MakeCard(BattleState s, BattleData data, BattleCardSpec spec, bool token = false)
        {
            if (!data.CardIndex.TryGetValue(spec.Id, out var def)) throw new RuleException("UNKNOWN_CARD");
            s.Seq = StateNumber((BigInteger)s.Seq + 1);
            string uid = "c" + s.Seq;
            s.Cards.Add(uid, new BattleCardState { Uid = uid, Id = spec.Id, Owner = def.Owner, Wielder = OrNull(spec.Wielder),
                Plus = spec.Plus, Circuit = OrNull(spec.Circuit), Edge = OrNull(spec.Edge), Growth = spec.Growth,
                Token = token, Assigned = OrNull(spec.Assigned), From = OrNull(spec.From) });
            s.CardOrder.Add(uid); return uid;
        }

        static void ApplyResonance(BattleState s, BattleActorState actor, ResonanceFx fx)
        {
            switch (fx.Type)
            {
                case "multi": foreach (var item in fx.List!) ApplyResonance(s, actor, item); break;
                case "cardMod": actor.CardMods.Add(fx.Copy()); break;
                case "passiveParam": actor.Passive.Params[fx.Param!] = fx.IntValue!.Value; break;
                case "skillLv": actor.SkillLv = checked(actor.SkillLv + fx.Delta!.Value); break;
                case "ultLv": actor.UltLv = checked(actor.UltLv + fx.Delta!.Value); break;
                case "capacity": actor.Capacity += fx.N!.Value; break;
                case "battleStart": actor.BattleStart.AddRange(fx.Effects!.Select(e => e.Copy())); break;
                case "ultExtra": actor.UltExtra.Add(new BattleUltExtra { Effects = fx.Effects!.Select(e => e.Copy()).ToList(), Once = fx.Once ?? false }); break;
                case "ultReplace": actor.UltReplace = fx.Effects!.Select(e => e.Copy()).ToList(); break;
                case "hook": actor.Hooks.Add(fx.Copy()); break;
                case "rule":
                    switch (fx.Key)
                    {
                        case "growthCap": s.GrowthCap = Math.Max(s.GrowthCap, fx.IntValue!.Value); break;
                        case "debtCap": s.DebtCap = Math.Max(s.DebtCap, fx.IntValue!.Value); break;
                        case "echoStart": s.EchoStart = Math.Max(s.EchoStart, fx.IntValue!.Value); break;
                        case "exhaustStart": s.ExhaustStart = Math.Max(s.ExhaustStart, fx.IntValue!.Value); break;
                        case "firstOverloadFree": s.BattleFlags["firstOverloadFree"] = true; break;
                        default: throw new RuleException("BATTLE_RESONANCE_NOT_IMPLEMENTED");
                    }
                    break;
                default: throw new RuleException("BATTLE_RESONANCE_NOT_IMPLEMENTED");
            }
        }

        public BattleResponse Command(BattleRequest request)
        {
            string identity = request.ReceiptIdentity();
            if (!string.IsNullOrEmpty(request.Rid) && receipts.TryGetValue(request.Rid!, out var previous))
            {
                if (previous.Identity != identity) throw new RuleException("IDEMPOTENCY_CONFLICT");
                return previous.Response.Copy();
            }
            if (request.V != state.V) throw new RuleException("STALE_VERSION");
            var candidate = new Battle(data, state.Copy());
            BattleResponse response;
            try
            {
                candidate.state.Events.Clear();
                switch (request.Kind)
                {
                    case "begin": candidate.Begin(); break;
                    case "end": candidate.EndTurn(); break;
                    case "play": case "ult": throw new RuleException("BATTLE_COMMAND_NOT_IMPLEMENTED");
                    default: throw new RuleException("UNKNOWN_COMMAND");
                }
                candidate.state.V = checked(candidate.state.V + 1);
                candidate.CheckInvariants();
                response = new BattleResponse { V = candidate.state.V, Phase = candidate.state.Phase, Events = candidate.state.Events.Select(e => e.Copy()).ToList() };
            }
            catch (OverflowException) { throw new RuleException("BATTLE_NUMBER_OUT_OF_RANGE"); }
            state = candidate.state;
            if (!string.IsNullOrEmpty(request.Rid)) receipts.Add(request.Rid!, new Receipt { Identity = identity, Response = response.Copy() });
            return response;
        }

        /// <summary>Detached source definition with upgrade and resonance overlays. Does not execute a card.</summary>
        public BattleCardDef EffectiveCard(string uid)
        {
            try { return EffectiveDefinition(CardInstance(uid)); }
            catch (OverflowException) { throw new RuleException("BATTLE_NUMBER_OUT_OF_RANGE"); }
        }
        public List<string> AllowedModes(string uid)
        {
            var instance = CardInstance(uid); var definition = EffectiveCard(uid);
            if (definition.Keywords.Contains("contract")) return new List<string> { "now" };
            if (PartsOf(instance).Any(p => p.Fx.NoReserve == true))
                return definition.Reservation == "n" || definition.Reservation == "y" ? new List<string> { "now" } : new List<string>();
            switch (definition.Reservation)
            {
                case "y": return new List<string> { "now", "next", "after" };
                case "o": return new List<string> { "next", "after" };
                case "after": return new List<string> { "after" };
                default: return new List<string> { "now" };
            }
        }
        public int CostOf(string uid, string mode = "now")
        {
            try
            {
                var instance = CardInstance(uid); var definition = EffectiveDefinition(instance);
                if (instance.Free) return 0;
                BigInteger cost = (BigInteger)definition.Cost + instance.CostMod;
                foreach (var part in PartsOf(instance)) if (part.Fx.Cost.HasValue) cost += part.Fx.Cost.Value;
                if (definition.CostIf != null && Condition(definition.CostIf.Condition, instance)) cost += definition.CostIf.Delta;
                state.BattleFlags.TryGetValue("emptyClockUsed", out var clockUsed);
                if (mode != "now" && Has("empty_clock") && !BattleGraph.Truthy(clockUsed)) cost--;
                if (mode != "now" && state.SilenceUntil > 0 && state.SilenceUntil >= state.Turn) cost++;
                if (state.FreeNext > 0) cost = 0;
                return StateNumber(BigInteger.Max(0, cost));
            }
            catch (OverflowException) { throw new RuleException("BATTLE_NUMBER_OUT_OF_RANGE"); }
        }
        public string? NeedsTarget(string uid)
        {
            string? need = null;
            void Walk(IEnumerable<EffectDef> effects)
            {
                foreach (var effect in effects)
                {
                    if (effect.Target == "fixed") need = "fixed";
                    else if (effect.Target == "one" && need != "fixed") need = "one";
                    else if (effect.Op == "dmg" && string.IsNullOrEmpty(effect.Target) && need == null) need = "one";
                    if (effect.Then != null) Walk(effect.Then);
                    if (effect.Else != null) Walk(effect.Else);
                }
            }
            Walk(EffectiveCard(uid).Effects); return need;
        }
        BattleCardState CardInstance(string uid)
        {
            if (string.IsNullOrEmpty(uid) || !state.Cards.TryGetValue(uid, out var card)) throw new RuleException("UNKNOWN_CARD_INSTANCE");
            return card;
        }
        string? OwnerCharacter(BattleCardState card) => data.CharacterIndex.ContainsKey(card.Owner) ? card.Owner : card.Owner == "weapon" ? card.Wielder : null;
        List<BattlePartDef> PartsOf(BattleCardState card)
        {
            var parts = new List<BattlePartDef>();
            foreach (string? id in new[] { card.Circuit, card.Edge }.Concat(card.Temp))
                if (!string.IsNullOrEmpty(id) && data.PartIndex.TryGetValue(id!, out var part)) parts.Add(part);
            return parts;
        }
        BattleCardDef EffectiveDefinition(BattleCardState instance)
        {
            if (!data.CardIndex.TryGetValue(instance.Id, out var source)) throw new RuleException("UNKNOWN_CARD");
            var definition = source.Copy();
            if (instance.Plus)
            {
                if (definition.Plus?.Effects != null) definition.Effects = definition.Plus.Effects.Select(e => e.Copy()).ToList();
                else MapEffects(definition.Effects, UpgradeEffect);
            }
            string? owner = OwnerCharacter(instance);
            var actor = owner == null ? null : state.Actors.FirstOrDefault(a => a.Id == owner);
            if (actor != null)
            {
                if (instance.Owner == owner)
                {
                    var modifiers = actor.CardMods.Where(m => m.Cards != null && (m.Cards.Contains(instance.Id) || m.Cards.Contains(source.Id))).ToList();
                    MapEffects(definition.Effects, effect => { foreach (var modifier in modifiers) ApplyCardModifier(effect, modifier); });
                }
                foreach (var modifier in actor.CardMods)
                    if (modifier.Prop == "uses" && modifier.Cards?.Contains(source.Id) == true)
                        definition.Uses = StateNumber((BigInteger)(definition.Uses ?? 0) + modifier.Add!.Value);
            }
            return definition;
        }
        static void MapEffects(IEnumerable<EffectDef> effects, Action<EffectDef> map)
        {
            foreach (var effect in effects)
            {
                map(effect);
                if (effect.Then != null) MapEffects(effect.Then, map);
                if (effect.Else != null) MapEffects(effect.Else, map);
            }
        }
        int? UpgradeValue(int? value)
        {
            if (!value.HasValue || value.Value <= 0) return value;
            int numerator = data.Rules.UpgradeNumerator, denominator = data.Rules.UpgradeDenominator;
            if (numerator <= 0 || denominator <= 0) throw new RuleException("BATTLE_DEFINITION_NOT_IMPLEMENTED");
            return StateNumber(((BigInteger)value.Value * numerator + denominator - 1) / denominator);
        }
        void UpgradeEffect(EffectDef effect)
        {
            switch (effect.Op)
            {
                case "dmg": case "blk": case "heal": effect.V = UpgradeValue(effect.V); effect.Rv = UpgradeValue(effect.Rv); effect.Ra = UpgradeValue(effect.Ra); break;
                case "counter": effect.N = UpgradeValue(effect.N); effect.Rn = UpgradeValue(effect.Rn); break;
                case "collateral": effect.V = UpgradeValue(effect.V); break;
            }
            if (effect.Op == "st")
            {
                if (!effect.N.HasValue) throw new RuleException("BATTLE_DEFINITION_NOT_IMPLEMENTED");
                effect.N = checked(effect.N.Value + 1);
                if (effect.Rn.HasValue) effect.Rn = checked(effect.Rn.Value + 1);
            }
        }
        static int? AddModifier(int? value, int amount) => value.HasValue ? StateNumber((BigInteger)value.Value + amount) : (int?)null;
        static void ApplyCardModifier(EffectDef effect, ResonanceFx modifier)
        {
            int add = modifier.Add!.Value;
            if (modifier.Prop == "dmg" && effect.Op == "dmg" || modifier.Prop == "heal" && effect.Op == "heal")
            { effect.V = AddModifier(effect.V, add); effect.Rv = AddModifier(effect.Rv, add); effect.Ra = AddModifier(effect.Ra, add); }
            if (modifier.Prop == "brk" && effect.Op == "dmg") effect.Brk = StateNumber((BigInteger)(effect.Brk ?? 1) + add);
            if (modifier.Prop == "n" && (effect.Op == "st" || effect.Op == "counter")) { effect.N = AddModifier(effect.N, add); effect.Rn = AddModifier(effect.Rn, add); }
            if (modifier.Prop == "draw" && effect.Op == "draw") effect.N = AddModifier(effect.N, add);
            if (modifier.Prop == "selfdmg" && effect.Op == "selfdmg" && effect.V.HasValue) effect.V = StateNumber(BigInteger.Max(0, (BigInteger)effect.V.Value + add));
        }
        bool IsGrowthInstance(BattleCardState instance)
        {
            if (data.CardIndex[instance.Id].Growth == true) return true;
            return instance.From != null && data.CardIndex.TryGetValue(instance.From, out var root) && root.Growth == true && root.Evolutions?.Contains(instance.Id) == true;
        }
        bool Condition(EffectConditionDef condition, BattleCardState? instance = null, string? reserved = null, string? lastTarget = null, bool killed = false)
        {
            var target = lastTarget == null ? null : state.Enemies.FirstOrDefault(e => e.Uid == lastTarget);
            switch (condition.Condition)
            {
                case "marked": return target != null && target.Status.Mark > 0;
                case "burning": return target != null && target.Status.Burn > 0;
                case "broken": return target != null && target.BrokenUntil > 0;
                case "debt": return state.Debts.Aggregate(BigInteger.Zero, (sum, debt) => sum + debt.N) > 0;
                case "modded": return instance != null && PartsOf(instance).Count != 0;
                case "reserved": return !string.IsNullOrEmpty(reserved);
                case "intentAttack": return target?.Intent != null && (target.Intent.Type == "atk" || target.Intent.Type == "atkdebuff" || target.Intent.Type == "expose");
                case "echo": return state.Echo >= condition.N;
                case "hpBelow": return (BigInteger)state.Hp * 100 <= (BigInteger)state.MaxHp * condition.Pct!.Value;
                case "targetHpBelow": return target != null && (BigInteger)target.Hp * 100 <= (BigInteger)target.MaxHp * condition.Pct!.Value;
                case "growthInHand": return state.Zones.Hand.Count(uid => IsGrowthInstance(state.Cards[uid])) >= condition.N;
                case "exhaustedAtLeast": return (BigInteger)state.Exhausted + state.ExhaustStart >= condition.N!.Value;
                case "killedThis": return killed;
                default: throw new RuleException("BATTLE_CONDITION_NOT_IMPLEMENTED");
            }
        }

        bool Has(string relic) => state.Relics.Contains(relic);
        int Below(int bound) { int value = rng.Below(bound); state.Rng = rng.State; return value; }
        void Shuffle(List<string> cards)
        {
            for (int i = cards.Count - 1; i > 0; i--) { int j = Below(i + 1); string c = cards[i]; cards[i] = cards[j]; cards[j] = c; }
        }
        void Emit(BattleEvent e)
        {
            state.Events.Add(e); var log = e.Copy(); log.T = state.Turn; state.Log.Add(log);
            if (state.Log.Count > 400) state.Log.RemoveAt(0);
        }
        (int Value, int Alt, string Scope)? WeaponPassive(string actorId, string passiveId)
        {
            var a = state.Actors.FirstOrDefault(actor => actor.Id == actorId);
            if (a?.Weapon == null || !data.WeaponIndex.TryGetValue(a.Weapon.Id, out var weapon) || weapon.Passive.Id != passiveId) return null;
            var p = weapon.Passive;
            return (p.Values[a.Weapon.Refine - 1], p.RefineAlt == null ? 0 : p.RefineAlt[a.Weapon.Refine - 1], p.Scope);
        }
        (int Value, int Alt, string Scope)? PartyWeapon(string passiveId)
        {
            (int Value, int Alt, string Scope)? best = null;
            foreach (var a in state.Actors)
            {
                var p = WeaponPassive(a.Id, passiveId);
                if (p.HasValue && (!best.HasValue || p.Value.Value > best.Value.Value)) best = p;
            }
            return best;
        }
        PassiveDef? PassiveOf(string id) => state.Actors.FirstOrDefault(a => a.Passive.Id == id)?.Passive;
        int PartyVitality() => StateNumber(Round(state.Actors.Aggregate(BigInteger.Zero, (total, a) => total + Or(a.Vit, 81)), Math.Max(1, state.Actors.Count)));
        int PartyPower() => StateNumber(Round(state.Actors.Aggregate(BigInteger.Zero, (total, a) => total + a.Power4), Math.Max(1, state.Actors.Count)));
        static BigInteger Floor(BigInteger numerator, BigInteger denominator)
        {
            var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
            return remainder.Sign < 0 ? quotient - 1 : quotient;
        }
        static BigInteger Round(BigInteger numerator, int denominator) => Floor(2 * numerator + denominator, 2 * (BigInteger)denominator);
        static int StateNumber(BigInteger value)
        {
            if (value < int.MinValue || value > int.MaxValue) throw new RuleException("BATTLE_NUMBER_OUT_OF_RANGE");
            return (int)value;
        }
        int ScaleVitality(int n) => checked((int)Floor((BigInteger)n * PartyVitality(), 81));
        int ScaleAttack(int n) => StateNumber(Floor((BigInteger)n * PartyPower(), 160));
        int ScaleEnemy(int n) => StateNumber(Round((BigInteger)n * Or(state.DifficultyDamage, 100), 100));
        void GainBlockRaw(int n, string source)
        {
            if (source != "card" && source != "warmth") n = ScaleVitality(n);
            state.Block = checked(state.Block + n);
            if (n != 0) Emit(new BattleEvent { E = "block", N = n, Source = source });
        }
        BattleEnemyState? RandomEnemy()
        {
            var alive = state.Enemies.Where(e => e.Alive).ToList(); return alive.Count == 0 ? null : alive[Below(alive.Count)];
        }
        void AddStatus(BattleEnemyState enemy, string status, int n)
        {
            switch (status)
            {
                case "vul": enemy.Status.Vul += n; break;
                case "weak": enemy.Status.Weak += n; break;
                case "mark": enemy.Status.Mark += n; break;
                case "burn": enemy.Status.Burn += n; break;
                default: throw new RuleException("BATTLE_EFFECT_NOT_IMPLEMENTED");
            }
            Emit(new BattleEvent { E = "status", Uid = enemy.Uid, Status = status, N = n });
        }

        void Begin()
        {
            if (state.Phase != "CREATED") throw new RuleException("WRONG_PHASE");
            if (state.Turn != 0) throw new RuleException("BATTLE_STATE_NOT_IMPLEMENTED");
            RequireSupportedTurnState();
            Shuffle(state.Zones.Draw);
            if (Has("bronze_cog")) GainBlockRaw(8, "relic");
            if (Has("pocket_brazier")) { var enemy = RandomEnemy(); if (enemy != null) AddStatus(enemy, "burn", 4); }
            foreach (var actor in state.Actors)
            {
                var block = WeaponPassive(actor.Id, "w_startBlock"); if (block.HasValue) GainBlockRaw(block.Value.Value, "weapon");
                foreach (var effect in actor.BattleStart.Where(e => e.Op != "tune")) ExecuteOpening(effect, actor);
            }
            if (Has("eternal_seed"))
            {
                var eligible = state.CardOrder.Select(uid => state.Cards[uid]).Where(c => data.CardIndex[c.Id].Growth == true && c.Growth < 3).ToList();
                if (eligible.Count != 0) ApplyGrowth(eligible[Below(eligible.Count)], 1, "eternal_seed");
            }
            StartTurn(true);
        }

        static void RequireShape(EffectDef effect, params string[] keys)
        {
            var graph = effect.ToJsonGraph();
            if (graph.Count != keys.Length || keys.Any(key => !graph.ContainsKey(key))) throw new RuleException("BATTLE_EFFECT_NOT_IMPLEMENTED");
        }
        void ExecuteOpening(EffectDef effect, BattleActorState actor)
        {
            switch (effect.Op)
            {
                case "blk":
                    RequireShape(effect, "op", "v");
                    int block = effect.V!.Value <= 0 ? 0 : checked((int)((BigInteger)effect.V.Value * actor.Vit * BigInteger.Max(0, 10000 + (BigInteger)actor.GearBps) / (81 * (BigInteger)10000)));
                    GainBlockRaw(block, "card");
                    var harin = PassiveOf("harin_stance");
                    if (harin != null && actor.Id == "harin" && block > 0) state.Counter += harin.Params["n"];
                    break;
                case "heal":
                    RequireShape(effect, "op", "v");
                    int heal = checked((int)((BigInteger)Math.Max(0, effect.V!.Value) * actor.Vit / 81));
                    HealParty(heal);
                    break;
                case "st":
                    RequireShape(effect, "op", "s", "n", "tgt");
                    if (effect.Target != "rand" || effect.Status != "mark") throw new RuleException("BATTLE_EFFECT_NOT_IMPLEMENTED");
                    var enemy = RandomEnemy(); if (enemy != null) AddStatus(enemy, effect.Status, effect.N!.Value);
                    break;
                case "tune":
                    RequireShape(effect, "op", "part", "pick");
                    if (effect.Part != "amp" || effect.Pick != "random") throw new RuleException("BATTLE_EFFECT_NOT_IMPLEMENTED");
                    TuneRandom(effect.Part); break;
                default: throw new RuleException("BATTLE_EFFECT_NOT_IMPLEMENTED");
            }
        }
        void HealParty(int amount)
        {
            if (state.Hp <= 0) return;
            var weapon = PartyWeapon("w_healUp");
            if (weapon.HasValue) amount = checked((int)Floor((BigInteger)amount * (10000 + (BigInteger)weapon.Value.Value), 10000));
            int before = state.Hp;
            state.Hp = (int)BigInteger.Min(state.MaxHp, (BigInteger)state.Hp + amount);
            int overflow = amount - (state.Hp - before);
            var aila = PassiveOf("aila_warmth");
            int pct = aila == null ? 0 : aila.Params["pct"];
            if (overflow > 0 && pct > 0) GainBlockRaw(checked((int)Floor((BigInteger)overflow * pct, 100)), "warmth");
            Emit(new BattleEvent { E = "heal", N = state.Hp - before });
        }
        void ApplyGrowth(BattleCardState card, int amount, string source)
        {
            if (data.CardIndex[card.Id].Growth != true || amount <= 0) return;
            int delta = Math.Min(3 - card.Growth, amount); if (delta <= 0) return;
            card.Growth += delta;
            Emit(new BattleEvent { E = "grow", Uid = card.Uid, Growth = card.Growth, Source = source });
            if (Has("symbiotic_moss")) GainBlockRaw(3, "moss");
        }

        void StartTurn(bool first)
        {
            var s = state; var rules = data.Rules;
            RequireSupportedTurnState();
            if (first ? s.Turn != 0 : s.Turn < 1) throw new RuleException("BATTLE_STATE_NOT_IMPLEMENTED");
            int ap = (int)Math.Max(0, Math.Min(rules.ApMax, (long)rules.ApPerTurn + s.ApNext + (Has("chrono_crown") ? 1 : 0) - (s.Turn == 0 && Has("cracked_hourglass") ? 1 : 0) - s.ApDrainNext));
            s.Turn = checked(s.Turn + 1); s.Phase = "RESOLVING";
            if (!first) { s.Block = Math.Min(s.Block, 0); s.Counter = 0; }
            s.Buffs = s.Buffs.Where(b => b.Until >= s.Turn).ToList();
            s.Played = 0; s.LastOwner = null; s.OwnersThisTurn.Clear(); s.Contracts = 0; s.FreeNext = 0; s.NextBoost = null;
            s.Echo = s.EchoActive ? Math.Min(s.EchoMax, s.EchoStart) : 0;
            s.SlotsTemp = new BattleSlots(); s.TurnFlags.Clear(); s.BreakApTurn = 0;
            foreach (var actor in s.Actors) actor.GaugeTurn = 0;
            foreach (string uid in s.CardOrder) s.Cards[uid].CostMod = 0;
            s.ApNext = 0; s.ApDrainNext = 0; s.Ap = ap;
            if (Has("infinite_circuit")) { LoseHp(ScaleVitality(2), "relic"); if (CheckEnd()) return; }
            s.BattleFlags["seraR6Turn"] = 0;
            int count = checked(rules.HandDraw - s.DrawPenalty); s.DrawPenalty = 0;
            if (first)
            {
                if (Has("quick_hands")) count = checked(count + 2);
                var draw = PartyWeapon("w_startDraw"); if (draw.HasValue) count = checked(count + draw.Value.Value);
            }
            Draw(Math.Max(0, count));
            if (first)
            {
                if (Has("memory_anvil")) for (int i = 0; i < 2; i++) TuneRandom("amp");
                foreach (var actor in s.Actors) foreach (var effect in actor.BattleStart.Where(e => e.Op == "tune")) ExecuteOpening(effect, actor);
            }
            foreach (var enemy in s.Enemies.Where(e => e.Alive)) SetIntents(enemy);
            s.Phase = "PLAYER"; Emit(new BattleEvent { E = "turn", Turn = s.Turn, Ap = s.Ap });
        }

        void RequireSupportedTurnState()
        {
            var s = state;
            // Create/begin/end have no producers for these systems. Guard them rather
            // than silently executing a partial power/reservation/late-action engine.
            if (s.Reservations.Count != 0 || s.Debts.Count != 0 || s.Powers.Count != 0 || s.Buffs.Count != 0 ||
                s.RestraintActive || s.BonusAvailable || s.NextBoost != null || s.FreeNext != 0 ||
                s.SlotsTemp.Next != 0 || s.SlotsTemp.After != 0 || s.SealedUntil != 0 || s.ApDrainNext != 0 || s.SilenceUntil != 0 ||
                s.Enemies.Any(e => e.Stunned || e.BrokenUntil != 0 || e.Brk != e.BrkMax || e.PatternIndex < 0))
                throw new RuleException("BATTLE_STATE_NOT_IMPLEMENTED");
            if (s.Enemies.Any(e => e.Passive != null && e.Passive.Id != "seedHeal")) throw new RuleException("BATTLE_ENEMY_PASSIVE_NOT_IMPLEMENTED");
        }
        List<EnemyMoveDef> EnemyPattern(BattleEnemyState enemy)
        {
            var pattern = data.EnemyIndex[enemy.Id].Pattern;
            if (pattern.Count == 0) throw new RuleException("BATTLE_PATTERN_NOT_IMPLEMENTED");
            return pattern;
        }
        void SetIntents(BattleEnemyState enemy)
        {
            var pattern = EnemyPattern(enemy);
            enemy.Intent = pattern[enemy.PatternIndex % pattern.Count].Copy();
            enemy.Next = pattern[(enemy.PatternIndex + 1) % pattern.Count].Copy();
        }

        void EndTurn()
        {
            var s = state;
            if (s.Phase != "PLAYER") throw new RuleException("WRONG_PHASE");
            RequireSupportedTurnState();
            s.Phase = "RESOLVING";
            if (s.RestraintActive && s.Played <= 3)
            {
                s.BonusTurn = checked(s.Turn + 1); s.BonusAvailable = true; Emit(new BattleEvent { E = "restraintKept" });
            }
            else if (s.BonusTurn <= s.Turn) s.BonusAvailable = false;
            s.RestraintActive = false;
            foreach (string uid in s.Zones.Hand.ToList()) if (s.Cards[uid].Id == "smudge") LoseHp(ScaleEnemy(2), "smudge");
            s.Zones.Discard.AddRange(s.Zones.Hand); s.Zones.Hand.Clear();
            if (CheckEnd()) return;
            if (s.Weak > 0) s.Weak--;
            if (s.SilenceUntil <= s.Turn) s.SilenceUntil = 0;
            foreach (var enemy in s.Enemies.ToList())
            {
                if (!enemy.Alive) continue;
                enemy.Block = 0; enemy.Status.Reflect = 0;
                var pattern = EnemyPattern(enemy);
                var move = pattern[enemy.PatternIndex % pattern.Count];
                enemy.PatternIndex = checked(enemy.PatternIndex + 1) % pattern.Count;
                EnemyMove(enemy, move);
                if (CheckEnd()) return;
            }
            foreach (var enemy in s.Enemies)
            {
                if (!enemy.Alive) continue;
                if (enemy.Status.Burn > 0)
                {
                    DamageEnemyRaw(enemy, enemy.Status.Burn, "burn");
                    enemy.Status.Burn = Math.Max(0, enemy.Status.Burn - 1);
                }
            }
            foreach (var enemy in s.Enemies)
            {
                if (!enemy.Alive || enemy.Passive == null) continue;
                if (enemy.Passive.Id != "seedHeal") throw new RuleException("BATTLE_ENEMY_PASSIVE_NOT_IMPLEMENTED");
                int seeds = s.Enemies.Count(e => e.Alive && e.Id == "e_seed");
                if (seeds != 0)
                {
                    int heal = StateNumber((BigInteger)enemy.Passive.N * seeds);
                    enemy.Hp = StateNumber(BigInteger.Min(enemy.MaxHp, (BigInteger)enemy.Hp + heal));
                    Emit(new BattleEvent { E = "enemyHeal", Uid = enemy.Uid, N = heal });
                }
            }
            foreach (var enemy in s.Enemies)
            {
                if (!enemy.Alive) continue;
                if (enemy.Status.Vul > 0) enemy.Status.Vul--;
                if (enemy.Status.Weak > 0) enemy.Status.Weak--;
                if (enemy.Status.Mark > 0) enemy.Status.Mark--;
            }
            if (CheckEnd()) return;
            StartTurn(false);
        }

        static void RequireMoveShape(EnemyMoveDef move, string[] required, params string[] optional)
        {
            var graph = move.ToJsonGraph();
            var allowed = new HashSet<string>(required.Concat(optional), StringComparer.Ordinal) { "t", "label" };
            if (required.Any(key => !graph.ContainsKey(key)) || graph.Keys.Any(key => !allowed.Contains(key)))
                throw new RuleException("BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED");
        }
        void EnemyMove(BattleEnemyState enemy, EnemyMoveDef move)
        {
            switch (move.Type)
            {
                case "atk": case "atkdebuff":
                    if (move.Type == "atk") RequireMoveShape(move, new[] { "d" }, "h");
                    else
                    {
                        RequireMoveShape(move, new[] { "d", "s", "n" }, "h");
                        if (move.Status != "weak") throw new RuleException("BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED");
                    }
                    for (int h = 0; h < Or(move.Hits ?? 1, 1); h++)
                    {
                        EnemyHit(enemy, move.Damage!.Value);
                        if (CheckEnd() || !enemy.Alive) return;
                    }
                    if (move.Type == "atkdebuff") state.Weak = checked(state.Weak + move.N!.Value);
                    break;
                case "def":
                    RequireMoveShape(move, new[] { "b" }, "str");
                    enemy.Block = checked(enemy.Block + move.Block!.Value);
                    if (move.Strength.HasValue) enemy.Status.Strength = checked(enemy.Status.Strength + move.Strength.Value);
                    break;
                case "buff":
                    RequireMoveShape(move, new[] { "str" }); enemy.Status.Strength = checked(enemy.Status.Strength + move.Strength!.Value); break;
                case "debuff":
                    RequireMoveShape(move, new[] { "s", "n" });
                    if (move.Status != "weak") throw new RuleException("BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED");
                    state.Weak = checked(state.Weak + move.N!.Value);
                    Emit(new BattleEvent { E = "partyDebuff", Status = move.Status, N = move.N.Value }); break;
                case "charge":
                    RequireMoveShape(move, new string[0]); Emit(new BattleEvent { E = "charge", Uid = enemy.Uid }); break;
                case "summon":
                    RequireMoveShape(move, new[] { "n", "e" });
                    for (int k = 0; k < move.N!.Value; k++) if (state.Enemies.Count(e => e.Alive) < 5) SetIntents(AddEnemy(state, data, move.Enemy!));
                    break;
                case "heal":
                    RequireMoveShape(move, new[] { "n" }, "who");
                    if (move.Who != null && move.Who != "lowest") throw new RuleException("BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED");
                    var target = LowestHealthEnemy();
                    if (target != null)
                    {
                        target.Hp = StateNumber(BigInteger.Min(target.MaxHp, (BigInteger)target.Hp + move.N!.Value));
                        Emit(new BattleEvent { E = "enemyHeal", Uid = target.Uid, N = move.N.Value });
                    }
                    break;
                case "insert":
                    RequireMoveShape(move, new[] { "n", "card" });
                    for (int k = 0; k < move.N!.Value; k++) state.Zones.Discard.Add(MakeCard(state, data, new BattleCardSpec { Id = move.Card! }, true));
                    Emit(new BattleEvent { E = "insert", Card = move.Card, N = move.N.Value }); break;
                case "reflect":
                    RequireMoveShape(move, new[] { "n" }); enemy.Status.Reflect = move.N!.Value; break;
                case "expose":
                    RequireMoveShape(move, new[] { "n" }, "then"); enemy.Status.Vul = checked(enemy.Status.Vul + move.N!.Value);
                    Emit(new BattleEvent { E = "expose", Uid = enemy.Uid }); if (move.Then != null) EnemyMove(enemy, move.Then); break;
                case "blockAll":
                    RequireMoveShape(move, new[] { "b" }); foreach (var alive in state.Enemies.Where(e => e.Alive)) alive.Block = checked(alive.Block + move.Block!.Value); break;
                default: throw new RuleException("BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED");
            }
        }

        BattleEnemyState? LowestHealthEnemy()
        {
            BattleEnemyState? target = null; BigInteger lowest = 0;
            foreach (var enemy in state.Enemies.Where(e => e.Alive))
            {
                BigInteger ratio = RoundedHealthRatio(enemy.Hp, enemy.MaxHp);
                if (target == null || ratio < lowest) { target = enemy; lowest = ratio; }
            }
            return target;
        }
        static BigInteger RoundedHealthRatio(int hp, int maxHp)
        {
            if (hp < 0 || maxHp <= 0 || hp > maxHp) throw new RuleException("STATE_RANGE");
            if (hp == 0) return BigInteger.Zero;
            // Match JS binary64 division with integer round-to-nearest/even. Positive
            // Int32 HP ratios are >=2^-31, so a 2^83 grid represents every quotient.
            int shift = 52; BigInteger normalized = hp;
            while (normalized < maxHp) { normalized <<= 1; shift++; }
            BigInteger mantissa = BigInteger.DivRem((BigInteger)hp << shift, maxHp, out var remainder);
            if (2 * remainder > maxHp || 2 * remainder == maxHp && !mantissa.IsEven) mantissa++;
            return mantissa << (83 - shift);
        }
        void EnemyHit(BattleEnemyState enemy, int baseDamage)
        {
            BigInteger damage = Round((BigInteger)baseDamage * Or(state.DifficultyDamage, 100), 100) + enemy.Status.Strength;
            if (enemy.Status.Weak > 0) damage = Floor(damage * (10000 - (BigInteger)data.Rules.WeakBps), 10000);
            int d = StateNumber(damage), blocked = Math.Min(state.Block, d);
            state.Block = checked(state.Block - blocked);
            int loss = checked(d - blocked);
            Emit(new BattleEvent { E = "hit", Uid = enemy.Uid, Damage = d, Blocked = blocked });
            if (loss > 0) LoseHp(loss, "enemy");
            if (state.Counter > 0 && enemy.Alive) DamageEnemyRaw(enemy, state.Counter, "counter");
        }
        void DamageEnemyRaw(BattleEnemyState enemy, int amount, string source)
        {
            amount = ScaleAttack(amount);
            if (!enemy.Alive || amount <= 0) return;
            int blocked = source == "burn" ? 0 : Math.Min(enemy.Block, amount);
            enemy.Block = checked(enemy.Block - blocked);
            enemy.Hp = StateNumber(BigInteger.Max(0, (BigInteger)enemy.Hp - ((BigInteger)amount - blocked)));
            Emit(new BattleEvent { E = "dmg", Uid = enemy.Uid, N = amount, Blocked = blocked, Source = source });
            if (enemy.Hp == 0) KillEnemy(enemy);
        }
        void KillEnemy(BattleEnemyState enemy)
        {
            enemy.Alive = false; Emit(new BattleEvent { E = "kill", Uid = enemy.Uid, Id = enemy.Id });
            if (enemy.Status.Mark <= 0) return;
            state.KilledMarked = checked(state.KilledMarked + 1);
            if (state.Phase == "PLAYER" && PassiveOf("kaya_deduction") != null)
                throw new RuleException("BATTLE_EFFECT_NOT_IMPLEMENTED"); // PLAYER kills require the unported play/ultimate hooks.
            if (Has("detective_notebook")) { Draw(1); AddNotebookCoins(); }
        }
        void AddNotebookCoins()
        {
            state.RunFlags.TryGetValue("coinsBonus", out var value);
            if (!BattleGraph.Truthy(value)) value = 0;
            if (value is string text) { state.RunFlags["coinsBonus"] = text + "10"; return; }
            long coins;
            if (value is int i) coins = i;
            else if (value is uint u) coins = u;
            else if (value is long n) coins = n;
            else if (value is bool b) coins = b ? 1 : 0;
            else throw new RuleException("BATTLE_STATE_NOT_IMPLEMENTED");
            coins = checked(coins + 10);
            if (coins < -CanonicalState.MaxSafeInteger || coins > CanonicalState.MaxSafeInteger) throw new RuleException("BATTLE_NUMBER_OUT_OF_RANGE");
            state.RunFlags["coinsBonus"] = coins;
        }

        void Draw(int count)
        {
            var zones = state.Zones;
            for (int i = 0; i < count; i++)
            {
                if (zones.Draw.Count == 0)
                {
                    if (zones.Discard.Count == 0) break;
                    zones.Draw = zones.Discard; zones.Discard = new List<string>(); Shuffle(zones.Draw);
                    Emit(new BattleEvent { E = "reshuffle" });
                }
                string uid = zones.Draw[0]; zones.Draw.RemoveAt(0);
                if (zones.Hand.Count < state.HandMax) zones.Hand.Add(uid);
                else { zones.Discard.Add(uid); Emit(new BattleEvent { E = "burnCard", Uid = uid }); }
            }
        }
        void TuneRandom(string part)
        {
            var eligible = state.Zones.Hand.Where(uid => !state.Cards[uid].Temp.Contains(part) && data.CardIndex[state.Cards[uid].Id].Rarity != "C").ToList();
            if (eligible.Count == 0) return;
            string uid = eligible[Below(eligible.Count)];
            state.Cards[uid].Temp.Add(part); state.TempParts++;
            Emit(new BattleEvent { E = "tune", Uid = uid, Part = part });
        }
        void LoseHp(int n, string source)
        {
            state.Hp = (int)BigInteger.Max(0, (BigInteger)state.Hp - n);
            state.RunFlags.TryGetValue("envelopeUsed", out var used);
            if (state.Hp == 0 && Has("memory_envelope") && !BattleGraph.Truthy(used))
            {
                state.Hp = 1; state.RunFlags["envelopeUsed"] = true; Emit(new BattleEvent { E = "envelope" });
            }
            Emit(new BattleEvent { E = "hpLoss", N = n, Source = source });
        }
        bool CheckEnd()
        {
            if (state.Phase == "VICTORY" || state.Phase == "DEFEAT") return true;
            if (state.Hp <= 0) state.Phase = "DEFEAT";
            else if (!state.Enemies.Any(e => e.Alive)) state.Phase = "VICTORY";
            else return false;
            foreach (var reservation in state.Reservations) state.Zones.Discard.Add((string)reservation["uid"]!);
            state.Reservations.Clear(); state.Debts.Clear(); state.RestraintActive = false; state.BonusAvailable = false;
            state.Result = state.Result ?? new BattleResult { Phase = state.Phase, Turn = state.Turn, Hp = state.Hp };
            Emit(new BattleEvent { E = "end", Phase = state.Phase }); return true;
        }

        void CheckInvariants()
        {
            var s = state; var rules = data.Rules;
            if (s.Hp < 0 || s.Hp > s.MaxHp || s.Ap < 0 || s.Ap > rules.ApMax || s.Zones.Hand.Count > s.HandMax) throw new RuleException("STATE_RANGE");
            if (s.Debts.Sum(d => (long)d.N) > s.DebtCap) throw new RuleException("DEBT_INVARIANT");
            if (s.GrowthCount < 0 || s.GrowthCount > s.GrowthCap || s.Cards.Values.Any(c => c.Growth < 0 || c.Growth > 3)) throw new RuleException("GROWTH_INVARIANT");
            var seen = s.Zones.Draw.Concat(s.Zones.Hand).Concat(s.Zones.Discard).Concat(s.Zones.Exhaust).Concat(s.Reservations.Select(r => (string)r["uid"]!)).ToList();
            if (seen.Count != s.Cards.Count || seen.Distinct(StringComparer.Ordinal).Count() != seen.Count || s.CardOrder.Any(uid => !seen.Contains(uid))) throw new RuleException("CARD_ZONE_CONSERVATION");
            if (s.Actors.Any(a => a.Gauge < 0 || a.Gauge > rules.GaugeMax)) throw new RuleException("GAUGE_RANGE");
            if (s.Enemies.Any(e => e.Hp < 0 || e.Block < 0 || e.Brk < 0)) throw new RuleException("ENEMY_RANGE");
            if (s.Reservations.Count(r => Convert.ToInt32(r["due"]) == s.Turn + 1) > rules.NextSlotsMax || s.Reservations.Count(r => Convert.ToInt32(r["due"]) == s.Turn + 2) > rules.AfterSlotsMax) throw new RuleException("CAPACITY_INVARIANT");
        }

        public Dictionary<string, object?> PublicView()
        {
            var s = state;
            return BattleGraph.Object("v", s.V, "turn", s.Turn, "phase", s.Phase, "hp", s.Hp, "maxHp", s.MaxHp, "block", s.Block, "counter", s.Counter, "ap", s.Ap,
                "apNext", s.ApNext, "apDrainNext", s.ApDrainNext, "silenceUntil", s.SilenceUntil, "debt", s.Debts.Sum(d => d.N), "drawCount", s.Zones.Draw.Count,
                "discard", new List<string>(s.Zones.Discard), "exhaustCount", s.Zones.Exhaust.Count,
                "hand", s.Zones.Hand.Select(uid => s.Cards[uid].ToJsonGraph()).ToList(), "res", s.Reservations.Select(BattleGraph.CopyDictionary).ToList(),
                "enemies", s.Enemies.Select(e => e.ToJsonGraph()).ToList(), "actors", s.Actors.Select(a => BattleGraph.Object("id", a.Id, "gauge", a.Gauge)).ToList());
        }
    }
}
