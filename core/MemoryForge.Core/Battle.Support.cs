using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoryForge.Core
{
    /// <summary>Trusted, already-derived support input. Account ownership/equipment is validated by the launch boundary.</summary>
    public sealed class BattleSupportSetup
    {
        public string CharId = "", WeaponId = "";
        public int Res, Refine, Power4, Hp, GearBps, SkillLv, UltLv;
    }

    /// <summary>Detached combat-effect source: no party HP contribution, gauge, ordinary passive or start/turn interface.</summary>
    public sealed class BattleEffectSource
    {
        public string Id = "", Name = "", Attr = "";
        public int Res, Power4, Vit, GearBps;
        public long SkillLv, UltLv;
        public BattleWeaponState? Weapon;
        public List<ResonanceFx> CardMods = new List<ResonanceFx>(), Hooks = new List<ResonanceFx>();
        public List<BattleUltExtra> UltExtra = new List<BattleUltExtra>();
        public List<EffectDef>? UltReplace;
        internal BattleEffectSource Copy()
        {
            var c = (BattleEffectSource)MemberwiseClone(); c.Weapon = Weapon?.Copy();
            c.CardMods = CardMods.Select(f => f.Copy()).ToList(); c.Hooks = Hooks.Select(f => f.Copy()).ToList();
            c.UltExtra = UltExtra.Select(f => f.Copy()).ToList(); c.UltReplace = UltReplace?.Select(e => e.Copy()).ToList(); return c;
        }
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("id", Id, "name", Name, "attr", Attr, "res", Res,
            "power4", Power4, "vit", Vit, "gearBps", GearBps, "skillLv", SkillLv, "ultLv", UltLv, "weapon", Weapon?.ToJsonGraph(),
            "cardMods", CardMods.Select(f => f.ToJsonGraph()).ToList(), "ultExtra", UltExtra.Select(f => f.ToJsonGraph()).ToList(),
            "ultReplace", UltReplace?.Select(e => e.ToJsonGraph()).ToList(), "hooks", Hooks.Select(f => f.ToJsonGraph()).ToList());
    }

    public sealed class BattleSupportState
    {
        public BattleEffectSource Source = new BattleEffectSource();
        public bool Used;
        internal BattleSupportState Copy() => new BattleSupportState { Source = Source.Copy(), Used = Used };
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("source", Source.ToJsonGraph(), "used", Used);
    }

    public sealed class BattleSupportInfo
    {
        public string Kind = "support", Caller = "", CharId = "", Name = "", UltName = "", Text = "", Attr = "";
        public int GaugeCost = 100;
        public bool Used, Ready;
        public string? Code, NeedsTarget, Target;
        public List<string> Picks = new List<string>();
    }

    public sealed partial class Battle
    {
        static BattleEffectSource EffectSource(BattleActorState a) => new BattleEffectSource { Id = a.Id, Name = a.Name, Attr = a.Attr,
            Res = a.Res, Power4 = a.Power4, Vit = a.Vit, GearBps = a.GearBps, SkillLv = a.SkillLv, UltLv = a.UltLv,
            Weapon = a.Weapon?.Copy(), CardMods = a.CardMods.Select(f => f.Copy()).ToList(), Hooks = a.Hooks.Select(f => f.Copy()).ToList(),
            UltExtra = a.UltExtra.Select(e => e.Copy()).ToList(), UltReplace = a.UltReplace?.Select(e => e.Copy()).ToList() };

        static BattleSupportState MakeSupport(BattleData data, BattleSupportSetup input, IEnumerable<string> party)
        {
            if (party.Contains(input.CharId)) throw new RuleException("SUPPORT_IN_PARTY");
            if (!data.CharacterIndex.TryGetValue(input.CharId, out var ch)) throw new RuleException("UNKNOWN_CHARACTER");
            if (input.Res < 0 || input.Res > 6 || input.Refine < 1 || input.Refine > 5 || input.Power4 <= 0 || input.Hp <= 0
                || input.GearBps < 0 || input.SkillLv < 1 || input.UltLv < 1 || !data.WeaponIndex.ContainsKey(input.WeaponId)) throw new RuleException("INVALID_SNAPSHOT");
            var source = new BattleEffectSource { Id = ch.Id, Name = ch.Name, Attr = ch.Attr, Res = input.Res, Power4 = input.Power4,
                Vit = input.Hp, GearBps = input.GearBps, SkillLv = input.SkillLv, UltLv = input.UltLv,
                Weapon = new BattleWeaponState { Id = input.WeaponId, Refine = input.Refine } };
            foreach (var fx in ch.Resonance.Take(input.Res)) ApplySourceResonance(source, fx);
            return new BattleSupportState { Source = source };
        }
        static void ApplySourceResonance(BattleEffectSource source, ResonanceFx fx)
        {
            switch (fx.Type)
            {
                case "multi": foreach (var child in fx.List!) ApplySourceResonance(source, child); break;
                case "cardMod": source.CardMods.Add(fx.Copy()); break;
                case "skillLv": source.SkillLv = checked(source.SkillLv + fx.Delta!.Value); break;
                case "ultLv": source.UltLv = checked(source.UltLv + fx.Delta!.Value); break;
                case "ultExtra": source.UltExtra.Add(new BattleUltExtra { Effects = fx.Effects!.Select(e => e.Copy()).ToList(), Once = fx.Once ?? false }); break;
                case "ultReplace": source.UltReplace = fx.Effects!.Select(e => e.Copy()).ToList(); break;
                case "hook": source.Hooks.Add(fx.Copy()); break;
                case "passiveParam": case "capacity": case "battleStart": case "rule": break;
                default: throw new RuleException("BATTLE_RESONANCE_NOT_IMPLEMENTED");
            }
        }
        BattleEffectSource? CardSource(BattleCardState card)
        {
            if (card.SupportSourceId != null)
            {
                var source = state.Support?.Source;
                if (source == null || source.Id != card.SupportSourceId || !card.Token || card.Key != null || card.Owner != source.Id)
                    throw new RuleException("INVALID_SNAPSHOT");
                return source;
            }
            var actor = Actor(OwnerCharacter(card)); return actor == null ? null : EffectSource(actor);
        }
        static bool SourceHook(BattleEffectSource? source, string hook) => source?.Hooks.Any(h => h.Id == hook) == true;
        (int Value, int Alt, string Scope)? SourceWeapon(BattleEffectSource? source, string id)
        {
            if (source?.Weapon == null || !data.WeaponIndex.TryGetValue(source.Weapon.Id, out var weapon) || weapon.Passive.Id != id) return null;
            var p = weapon.Passive; int refine = source.Weapon.Refine - 1;
            return (p.Values[refine], p.RefineAlt == null ? 0 : p.RefineAlt[refine], p.Scope);
        }
        List<EffectDef> UltEffects(BattleEffectSource source) => source.UltReplace ?? data.CharacterIndex[source.Id].Ult.Effects;
        List<EffectDef> SupportEffects() => state.Support == null ? throw new RuleException("SUPPORT_NOT_SELECTED")
            : UltEffects(state.Support.Source).Concat(state.Support.Source.UltExtra.SelectMany(x => x.Effects)).ToList();
        // Existing ordinary Ult target semantics stay unchanged; support adds its own fixed/nested validation below.
        string? UltTarget(IEnumerable<EffectDef> effects, string? target)
        {
            if (effects.Any(o => o.Target == "one" || o.Op == "dmg" && string.IsNullOrEmpty(o.Target)))
            {
                var alive = state.Enemies.Where(e => e.Alive).ToList();
                if (string.IsNullOrEmpty(target) && alive.Count == 1) target = alive[0].Uid;
                if (Enemy(target)?.Alive != true) throw new RuleException("BAD_TARGET");
            }
            return target;
        }
        static string? EffectTarget(IEnumerable<EffectDef> effects)
        {
            string? need = null;
            foreach (var op in effects)
            {
                if (op.Target == "fixed") return "fixed";
                if (op.Target == "one" || op.Op == "dmg" && string.IsNullOrEmpty(op.Target)) need = "one";
                foreach (var branch in new[] { op.Then, op.Else })
                {
                    string? nested = branch == null ? null : EffectTarget(branch);
                    if (nested == "fixed") return nested;
                    if (nested != null) need = nested;
                }
            }
            return need;
        }
        public string? SupportNeedsTarget() => EffectTarget(SupportEffects());
        BattleActorState SupportCaller(string caller)
        {
            if (state.Support == null) throw new RuleException("SUPPORT_NOT_SELECTED");
            if (state.Support.Used) throw new RuleException("SUPPORT_ALREADY_USED");
            if (state.Phase != "PLAYER") throw new RuleException("WRONG_PHASE");
            var actor = Actor(caller) ?? throw new RuleException("UNKNOWN_ACTOR");
            if (actor.Gauge < 100) throw new RuleException("ULT_NOT_READY");
            return actor;
        }
        string? SupportPrecheck(string caller, string? target, IReadOnlyList<string> picks)
        {
            SupportCaller(caller);
            string? need = SupportNeedsTarget();
            if (need != null)
            {
                var eligible = state.Enemies.Where(e => e.Alive && (need != "fixed" || e.Status.Mark > 0)).ToList();
                if (string.IsNullOrEmpty(target) && eligible.Count == 1) target = eligible[0].Uid;
                if (!eligible.Any(e => e.Uid == target)) throw new RuleException("BAD_TARGET");
            }
            else target = null;
            var choices = SupportChoices(caller, picks);
            if (choices.Error != null) throw new RuleException(choices.Error);
            ValidateOps(SupportEffects(), null, "now", picks);
            return target;
        }
        void SupportArguments(BattleArgument? args) => Support(ArgumentText(args?.Member("caller")) ?? "", ArgumentText(args?.Member("target")), ArgumentPicks(args?.Member("picks")));
        void Support(string caller, string? target, IReadOnlyList<string> picks)
        {
            target = SupportPrecheck(caller, target, picks);
            var actor = Actor(caller)!; var support = state.Support!; var source = support.Source;
            actor.Gauge = checked(actor.Gauge - 100); support.Used = true;
            Emit(new BattleEvent { E = "support", Actor = source.Id, Caller = caller });
            ResolveUlt(source, new EffectContext { Source = source, Attr = source.Attr, Target = target, LastTarget = target,
                Picks = new List<string>(picks), IsUlt = true });
        }
        void ResolveUlt(BattleEffectSource source, EffectContext context)
        {
            foreach (var op in UltEffects(source)) { ExecuteOp(op, context); if (CheckEnd()) return; }
            foreach (var extra in source.UltExtra)
            {
                if (extra.Once) { string key = "ultOnce_" + source.Id; if (Flag(state.BattleFlags, key)) continue; state.BattleFlags[key] = true; }
                foreach (var op in extra.Effects) { ExecuteOp(op, context); if (CheckEnd()) return; }
            }
            if (SourceHook(source, "sera_r6")) state.BattleFlags["seraR6Turn"] = checked(state.Turn + 1);
            if (context.Actor != null && SourceHook(source, "kaya_r6") && context.Killed)
                context.Actor.Gauge = StateNumber(System.Numerics.BigInteger.Min(data.Rules.GaugeMax, (System.Numerics.BigInteger)context.Actor.Gauge + 50));
            CheckEnd();
        }
        public BattleSupportInfo SupportInfo(string caller, string? target = null, IReadOnlyList<string>? picks = null)
        {
            var source = state.Support?.Source ?? throw new RuleException("SUPPORT_NOT_SELECTED");
            var chosen = picks == null ? new List<string>() : new List<string>(picks);
            var view = new BattleSupportInfo { Caller = caller, CharId = source.Id, Name = source.Name, UltName = data.CharacterIndex[source.Id].Ult.Name,
                Attr = source.Attr, Used = state.Support!.Used, NeedsTarget = SupportNeedsTarget(), Target = target, Picks = chosen,
                Text = SupportText(source) };
            try { view.Target = SupportPrecheck(caller, target, chosen); view.Ready = true; }
            catch (RuleException error) { view.Code = error.Code; }
            catch (OverflowException) { view.Code = "BATTLE_NUMBER_OUT_OF_RANGE"; }
            return view;
        }
        string SupportText(BattleEffectSource source)
        {
            string text = string.Join(". ", SupportEffects().Select(e => BattleText.Op(e, data)).Where(t => t.Length != 0)) + ".";
            if (SourceHook(source, "aila_r6")) text += " 필살기의 초과 회복은 100% 방어로 전환.";
            if (SourceHook(source, "sera_r6")) text += " 필살기를 쓴 다음 턴, 세라의 카드 공격 피해 +50%.";
            return text;
        }
        public BattleChoiceView SupportChoices(string caller, IReadOnlyList<string>? picks = null)
        {
            try
            {
                SupportCaller(caller);
                var effects = SupportEffects(); var chosen = picks == null ? new List<string>() : new List<string>(picks);
                var requests = BattleChoicePolicy.ChoiceRequests(effects, "now", data); var rules = BattleChoicePolicy.ChoiceRules(effects, "now");
                Func<string, bool, int> price = (uid, _) => CostOf(uid, "now"); // support never consumes FreeNext
                var walk = BattleChoicePolicy.WalkPicks(state, "", requests, rules, chosen, data.CardIndex, price);
                var view = new BattleChoiceView { Accepted = walk.Accepted, Index = walk.Index, Left = walk.Left };
                for (int i = 0; i < requests.Count; i++) view.Requests.Add(new BattleChoiceRequest { Label = requests[i].Label, From = requests[i].From,
                    Count = requests[i].Count, GrowthOnly = requests[i].GrowthOnly, Op = rules[i].Op, Part = rules[i].Part, Leading = rules[i].Leading });
                if (walk.Accepted < chosen.Count) view.Error = BattleChoicePolicy.PickInvalid;
                if (walk.Index < requests.Count)
                {
                    view.Blocked = BattleChoicePolicy.PickBlocked(state, rules[walk.Index]);
                    view.Candidates = BattleChoicePolicy.PickCandidates(state, "", requests[walk.Index], chosen.Take(walk.Accepted), data.CardIndex, rules[walk.Index], price);
                }
                return view;
            }
            catch (RuleException error) { return new BattleChoiceView { Error = error.Code }; }
            catch (OverflowException) { return new BattleChoiceView { Error = "BATTLE_NUMBER_OUT_OF_RANGE" }; }
        }
        public BattlePlayForecast SupportForecast(string caller, string? target = null, IReadOnlyList<string>? picks = null)
        {
            try
            {
                var chosen = picks == null ? new List<string>() : new List<string>(picks);
                var args = BattleArgument.Object(new[] { new KeyValuePair<string, BattleArgument>("caller", BattleArgument.String(caller)),
                    new KeyValuePair<string, BattleArgument>("target", target == null ? BattleArgument.Null() : BattleArgument.String(target)),
                    new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array(chosen.Select(BattleArgument.String))) });
                var candidate = new Battle(data, state.Copy()); candidate.Command(new BattleRequest(null, state.V, "support", args));
                var forecast = new BattlePlayForecast { Cost = 100 };
                if (HiddenOutcome(SupportEffects()) || !state.Rng.SequenceEqual(candidate.state.Rng) || !state.Zones.Draw.SequenceEqual(candidate.state.Zones.Draw))
                { forecast.Hidden = true; return forecast; }
                FillForecast(forecast, candidate.state); return forecast;
            }
            catch (RuleException error) { return new BattlePlayForecast { Error = error.Code }; }
        }
    }
}
