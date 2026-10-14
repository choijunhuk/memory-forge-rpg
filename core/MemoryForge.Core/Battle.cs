// §5.3/5.9/5.15-18: real CREATED -> first player turn. Later Battle/Run commands remain unported.
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
        /// <summary>Detached snapshot. Restoration/later-turn execution is deliberately not an API of this opening slice.</summary>
        public BattleState State => state.Copy();

        /// <summary>Opening state numbers use Int32, except skill/ultimate levels. Out-of-range results reject explicitly.</summary>
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
            foreach (string id in setup.Enemies)
            {
                if (!data.EnemyIndex.TryGetValue(id, out var enemy)) throw new RuleException("UNKNOWN_ENEMY");
                int hp = StateNumber(BigInteger.Max(1, Round((BigInteger)enemy.Hp * s.DifficultyHp, 100)));
                s.Seq++;
                s.Enemies.Add(new BattleEnemyState { Uid = "e" + s.Seq, Id = id, Name = enemy.Name, Tier = enemy.Tier,
                    Hp = hp, MaxHp = hp, Brk = enemy.Brk, BrkMax = enemy.Brk, Weak = new List<string>(enemy.Weak), Passive = enemy.Passive?.Copy() });
            }
            var battle = new Battle(data, s); battle.CheckInvariants(); return battle;
        }

        static int Or(int value, int fallback) => value == 0 ? fallback : value;
        static string? OrNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
        static string MakeCard(BattleState s, BattleData data, BattleCardSpec spec, bool token = false)
        {
            if (!data.CardIndex.TryGetValue(spec.Id, out var def)) throw new RuleException("UNKNOWN_CARD");
            string uid = "c" + (++s.Seq);
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
                    case "play": case "ult": case "end": throw new RuleException("BATTLE_COMMAND_NOT_IMPLEMENTED");
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
            if (state.Turn != 0 || state.Reservations.Count != 0 || state.Powers.Count != 0) throw new RuleException("BATTLE_STATE_NOT_IMPLEMENTED");
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
                var eligible = state.CardOrder.Select(uid => state.Cards[uid]).Where(c => data.CardIndex[c.Id].Growth && c.Growth < 3).ToList();
                if (eligible.Count != 0) ApplyGrowth(eligible[Below(eligible.Count)], 1, "eternal_seed");
            }
            StartFirstTurn();
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
            if (!data.CardIndex[card.Id].Growth || amount <= 0) return;
            int delta = Math.Min(3 - card.Growth, amount); if (delta <= 0) return;
            card.Growth += delta;
            Emit(new BattleEvent { E = "grow", Uid = card.Uid, Growth = card.Growth, Source = source });
            if (Has("symbiotic_moss")) GainBlockRaw(3, "moss");
        }

        void StartFirstTurn()
        {
            var s = state; var rules = data.Rules;
            if (s.Turn != 0 || s.Debts.Count != 0 || s.Reservations.Count != 0 || s.Powers.Count != 0) throw new RuleException("BATTLE_STATE_NOT_IMPLEMENTED");
            int ap = (int)Math.Max(0, Math.Min(rules.ApMax, (long)rules.ApPerTurn + s.ApNext + (Has("chrono_crown") ? 1 : 0) - (Has("cracked_hourglass") ? 1 : 0) - s.ApDrainNext));
            s.Turn++; s.Phase = "RESOLVING";
            s.Buffs = s.Buffs.Where(b => b.Until >= s.Turn).ToList();
            s.Played = 0; s.LastOwner = null; s.OwnersThisTurn.Clear(); s.Contracts = 0; s.FreeNext = 0; s.NextBoost = null;
            s.Echo = s.EchoActive ? Math.Min(s.EchoMax, s.EchoStart) : 0;
            s.SlotsTemp = new BattleSlots(); s.TurnFlags.Clear(); s.BreakApTurn = 0;
            foreach (var actor in s.Actors) actor.GaugeTurn = 0;
            foreach (string uid in s.CardOrder) s.Cards[uid].CostMod = 0;
            s.ApNext = 0; s.ApDrainNext = 0; s.Ap = ap;
            if (Has("infinite_circuit")) { LoseHp(ScaleVitality(2), "relic"); if (CheckEnd()) return; }
            s.BattleFlags["seraR6Turn"] = 0;
            int count = rules.HandDraw - s.DrawPenalty; s.DrawPenalty = 0;
            if (Has("quick_hands")) count += 2;
            var draw = PartyWeapon("w_startDraw"); if (draw.HasValue) count += draw.Value.Value;
            Draw(Math.Max(0, count));
            if (Has("memory_anvil")) for (int i = 0; i < 2; i++) TuneRandom("amp");
            foreach (var actor in s.Actors) foreach (var effect in actor.BattleStart.Where(e => e.Op == "tune")) ExecuteOpening(effect, actor);
            foreach (var enemy in s.Enemies.Where(e => e.Alive))
            {
                var pattern = data.EnemyIndex[enemy.Id].Pattern;
                if (pattern.Count == 0) throw new RuleException("BATTLE_PATTERN_NOT_IMPLEMENTED");
                enemy.Intent = enemy.Stunned ? new EnemyMoveDef { Type = "stun", Label = "균열: 행동 불가" } : pattern[enemy.PatternIndex % pattern.Count].Copy();
                enemy.Next = pattern[(enemy.PatternIndex + 1) % pattern.Count].Copy();
            }
            s.Phase = "PLAYER"; Emit(new BattleEvent { E = "turn", Turn = s.Turn, Ap = s.Ap });
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
