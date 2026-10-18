// §11.5 waves and §11.7/§11.1 rift rules: engine/content-modes.js ModeBattle. The JS subclass exists only because rules-core.js is
// sha256-pinned; here the same hooks live in Battle and stay inert unless state.Mode is set (CreateMode), so every plain Battle is unchanged.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MemoryForge.Core
{
    public sealed partial class Battle
    {
        // Parameter names each rule reads (content_modes.json rift.modifiers). JS would compute NaN for a missing one; C# rejects it.
        static readonly Dictionary<string, string[]> RiftRuleParams = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["rift_reserve"] = new[] { "reservedBps", "immediateBps" }, ["rift_break"] = new[] { "gaugeBps", "brokenDamageBps" },
            ["rift_ink"] = new[] { "smudgePerTurn", "perExhaustBps" }, ["rift_debt"] = new[] { "debtCapAdd", "debtDamageBps" },
            ["rift_bloom"] = new[] { "growthCapAdd", "growthBlock" }, ["rift_echo"] = new[] { "echoMaxAdd", "distinctChain", "apGain" },
        };
        public static IReadOnlyList<string> RiftRules => RiftRuleParams.Keys.ToList();

        /// <summary>ModeBattle.create: Battle.Create on the mode's data (rift_break rewrites it), then state.Mode and the rule caps on top of
        /// relic/resonance results. The caller's mode is copied; wave restarts at 1 and settled at false.</summary>
        public static Battle CreateMode(BattleData data, BattleSetup setup, BattleMode mode)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            var m = Mode(mode);
            var battle = Create(ModeData(data, m), setup);
            var s = battle.state; s.Mode = m;
            try
            {
                if (m.Rule == "rift_debt") s.DebtCap = checked(s.DebtCap + m.Params!["debtCapAdd"]);
                if (m.Rule == "rift_bloom") s.GrowthCap = checked(s.GrowthCap + m.Params!["growthCapAdd"]);
                if (m.Rule == "rift_echo") s.EchoMax = checked(s.EchoMax + m.Params!["echoMaxAdd"]);
            }
            catch (OverflowException) { throw new RuleException("BATTLE_NUMBER_OUT_OF_RANGE"); }
            battle.CheckInvariants();
            return battle;
        }
        // JS: rule = mode.rule || null; params only with a rule; UNKNOWN_RULE for an unknown rule or absent params.
        static BattleMode Mode(BattleMode mode)
        {
            string? rule = string.IsNullOrEmpty(mode.Rule) ? null : mode.Rule;
            if (rule != null && (!RiftRuleParams.TryGetValue(rule, out var names) || mode.Params == null || !names.All(mode.Params.ContainsKey)))
                throw new RuleException("UNKNOWN_RULE");
            return new BattleMode
            {
                Kind = mode.Kind, ContentId = mode.ContentId, Rule = rule, Tier = mode.Tier, Difficulty = mode.Difficulty, Battle = mode.Battle,
                Practice = mode.Practice, Rewarded = mode.Rewarded, Params = rule == null ? null : new Dictionary<string, int>(mode.Params!, StringComparer.Ordinal),
                Waves = (mode.Waves ?? new List<List<string>>()).Select(w => new List<string>(w)).ToList(), Wave = 1, Settled = false
            };
        }
        /// <summary>modeData: the rift_break copy of the data, otherwise the same instance.</summary>
        internal static BattleData ModeData(BattleData data, BattleMode? mode)
        {
            if (mode == null || mode.Rule != "rift_break") return data;
            var p = mode.Params;
            if (p == null || !p.ContainsKey("gaugeBps") || !p.ContainsKey("brokenDamageBps")) throw new RuleException("UNKNOWN_RULE");
            return data.WithRiftBreak(p["gaugeBps"], p["brokenDamageBps"]);
        }
        /// <summary>Battle#do: a command at the live version with rid r{v}-{kind}.</summary>
        internal BattleResponse Do(string kind, BattleArgument? args = null) =>
            Command(new BattleRequest("r" + state.V.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + kind, state.V, kind, args));

        Dictionary<string, int>? RiftRule(string id) => state.Mode != null && state.Mode.Rule == id ? state.Mode.Params : null;

        // §11.5: clearing a wave brings the next one into the same battle; turn count, HP, hand and reservations carry on.
        bool SpawnNextWave()
        {
            var m = state.Mode;
            if (m == null || m.Waves.Count == 0 || IsOver() || state.Hp <= 0 || state.Enemies.Any(e => e.Alive)) return false;
            var wave = m.Waves[0]; m.Waves.RemoveAt(0); m.Wave = checked(m.Wave + 1);
            foreach (string id in wave) SetIntents(AddEnemy(state, data, id));
            Emit(new BattleEvent { E = "wave", Wave = m.Wave, Left = m.Waves.Count });
            return true;
        }

        // rift_reserve / rift_ink / rift_debt: additive bps on party card and ultimate damage.
        BigInteger RiftDamageBonus(EffectContext context)
        {
            BigInteger add = 0;
            var reserve = RiftRule("rift_reserve"); if (reserve != null) add += context.Reserved != null ? reserve["reservedBps"] : reserve["immediateBps"];
            var ink = RiftRule("rift_ink"); if (ink != null) add += ink["perExhaustBps"] * CounterValue("exhausted", context);
            return add + RiftDebtRawBps();
        }
        // rift_debt "모든 피해": with any debt, card/ult damage and raw party damage (counter, burn, relic through ScaleAttack).
        int RiftDebtRawBps()
        {
            var debt = RiftRule("rift_debt");
            return debt != null && DebtTotal() > 0 ? debt["debtDamageBps"] : 0;
        }

        // rift_ink: smudge into the discard pile at every turn start, before reservations and the draw (an enemy "insert" move).
        void RiftInkTurnStart()
        {
            var ink = RiftRule("rift_ink");
            if (ink != null) EnemyMove(null!, new EnemyMoveDef { Type = "insert", Card = "smudge", N = ink["smudgePerTurn"] });
        }

        // rift_echo: three consecutive character cards from different characters give AP once per turn. A card without an owner
        // character (common, curse, token) clears the chain; ultimates are not cards and leave it alone.
        void RiftEchoAfterPlay(string? owner)
        {
            var echo = RiftRule("rift_echo");
            if (echo == null || IsOver()) return;
            var flags = state.TurnFlags; int n = echo["distinctChain"];
            var chain = new List<string>();
            if (owner != null)
            {
                if (flags.TryGetValue("riftChain", out var previous) && previous is IList list) foreach (object? item in list) chain.Add((string)item!);
                chain.Add(owner);
                // Array.prototype.slice(-n)
                long start = -(long)n, from = start < 0 ? Math.Max(chain.Count + start, 0) : Math.Min(start, chain.Count);
                chain = chain.Skip((int)from).ToList();
            }
            flags["riftChain"] = chain.Cast<object?>().ToList();
            if (!Flag(flags, "riftEcho") && chain.Count == n && chain.Distinct(StringComparer.Ordinal).Count() == n)
            {
                flags["riftEcho"] = true;
                state.Ap = StateNumber(BigInteger.Min(data.Rules.ApMax, (BigInteger)state.Ap + echo["apGain"]));
                Emit(new BattleEvent { E = "riftEcho", Ap = echo["apGain"] });
            }
        }
    }
}
