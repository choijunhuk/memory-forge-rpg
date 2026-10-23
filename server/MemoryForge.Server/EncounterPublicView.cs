using System.Collections.Generic;
using System.Linq;
using MemoryForge.Core;
namespace MemoryForge.Server
{
    static partial class PublicView
    {
        public static Dictionary<string, object?> BattleQuery(Battle run, string encounterId, int version, BattleQueryRequest request, BattleChoiceView choices,
            BattlePlayForecast forecast, BattleData content)
        {
            var b = run.State;
            // Candidates exist only for the request at Index; their zone is that request's own From (hand, discard or res).
            string? zone = choices.Index < choices.Requests.Count ? choices.Requests[choices.Index].From : null;
            string? name = null, text = null;
            try { var effective = run.EffectiveCard(request.Uid); name = effective.Name; text = BattleText.Card(effective, content); }
            catch (RuleException) { }
            return Obj("encounterId", encounterId, "version", version, "phase", b.Phase,
                "context", Obj("turn", b.Turn, "battleVersion", b.V, "battlePhase", b.Phase),
                "source", Obj("uid", request.Uid, "id", b.Cards[request.Uid].Id, "name", name, "text", text, "mode", request.Mode, "target", request.Target,
                    "picks", request.Picks.Select(p => (object?)p).ToList()),
                "slots", Slots(run.ReservationSlots(), b, content.Rules),
                "forecast", Obj("error", forecast.Error, "mode", forecast.Mode, "cost", forecast.Cost, "due", forecast.Due, "hidden", forecast.Hidden,
                    "enemyHpLoss", forecast.EnemyHpLoss, "enemyDamage", forecast.EnemyDamage, "newlyBroken", forecast.NewlyBroken,
                    "blockDelta", forecast.BlockDelta, "hpDelta", forecast.HpDelta, "defeat", forecast.Defeat,
                    // Core's own per-enemy split (BattlePlayForecast.EnemyHpLosses), copied entry by entry: empty with an error or hidden.
                    "enemyHpLosses", forecast.EnemyHpLosses.Select(l => (object?)Obj("uid", l.Uid, "hpLoss", l.HpLoss)).ToList()),
                "choices", Obj("error", choices.Error,
                    "requests", choices.Requests.Select(r => (object?)Obj("label", r.Label, "from", r.From, "count", r.Count, "growthOnly", r.GrowthOnly,
                        "op", r.Op, "part", r.Part, "leading", r.Leading)).ToList(),
                    "accepted", choices.Accepted, "index", choices.Index, "left", choices.Left, "blocked", choices.Blocked,
                    "candidates", choices.Candidates.Select(uid => (object?)Candidate(b.Cards[uid], zone)).ToList()));
        }

        public static Dictionary<string, object?> Battle(Battle run, BattleState b, BattleRules rules) => WithSupport(Obj(
            "v", b.V, "phase", b.Phase, "turn", b.Turn, "hp", b.Hp, "maxHp", b.MaxHp, "block", b.Block, "counter", b.Counter,
            "ap", b.Ap, "apNext", b.ApNext, "apDrainNext", b.ApDrainNext, "silenceUntil", b.SilenceUntil, "weak", b.Weak,
            "debts", b.Debts.Select(d => (object?)Obj("n", d.N, "due", d.Due, "deferred", d.Deferred)).ToList(),
            "echo", b.Echo, "echoMax", b.EchoMax, "echoActive", b.EchoActive, "freeNext", b.FreeNext, "played", b.Played,
            "restraint", Obj("active", b.RestraintActive, "usedTurn", b.RestraintUsedTurn), "bonus", Obj("turn", b.BonusTurn, "available", b.BonusAvailable),
            "growth", Obj("count", b.GrowthCount, "cap", b.GrowthCap),
            "buffs", b.Buffs.Select(x => (object?)Obj("s", x.Status, "n", x.N, "until", x.Until)).ToList(),
            "powers", b.Powers.Select(p => (object?)Obj("id", p.Id, "bps", p.Bps, "blk", p.Block, "keep", p.Keep, "heal", p.Heal)).ToList(),
            "piles", Obj("draw", b.Zones.Draw.Count, "hand", b.Zones.Hand.Count, "discard", b.Zones.Discard.Count, "exhaust", b.Zones.Exhaust.Count),
            "hand", b.Zones.Hand.Select(uid => (object?)HandCard(run, b, uid)).ToList(),
            "reservations", b.Reservations.Select(r => (object?)Obj("uid", r.Uid, "id", b.Cards.TryGetValue(r.Uid, out var card) ? card.Id : null,
                "due", r.Due, "order", r.Order, "kind", r.Kind, "target", r.Target, "delayed", r.Delayed)).ToList(),
            "slots", Slots(run.ReservationSlots(), b, rules),
            "gaugeMax", rules.GaugeMax,
            "actors", b.Actors.Select(a => (object?)Obj("id", a.Id, "name", a.Name, "attr", a.Attr, "gauge", a.Gauge, "res", a.Res)).ToList(),
            "enemies", b.Enemies.Select(e => (object?)Enemy(e)).ToList(),
            "result", b.Result == null ? null : Obj("phase", b.Result.Phase, "turn", b.Result.Turn, "hp", b.Result.Hp)),b);

        static Dictionary<string, object?> HandCard(Battle run, BattleState b, string uid)
        {
            var card = b.Cards[uid];
            var view = Obj("uid", uid, "id", card.Id, "key", Scalar(Member(card.ToJsonGraph(), "key")), "owner", card.Owner, "wielder", card.Wielder,
                "assigned", card.Assigned, "from", card.From, "plus", card.Plus, "mods", Obj("c", card.Circuit, "e", card.Edge),
                "temp", card.Temp.Select(t => (object?)t).ToList(), "growth", card.Growth, "grown", card.Grown, "used", card.Used,
                "token", card.Token, "free", card.Free, "echoCopy", card.EchoCopy, "held", card.Held, "retain", run.Retains(uid));
            List<string> modes;
            try { modes = run.AllowedModes(uid); view["needsTarget"] = run.NeedsTarget(uid); view["cost"] = run.CostOf(uid, "now"); }
            catch (RuleException error) { view["modes"] = new List<object?>(); view["error"] = error.Code; return view; }
            var costs = Obj(); var blocked = Obj();
            foreach (string mode in modes)
            {
                try { costs[mode] = run.CostOf(uid, mode); }
                catch (RuleException error) { costs[mode] = null; blocked[mode] = error.Code; continue; }
                blocked[mode] = run.Check(uid, mode, "auto");
            }
            view["modes"] = modes.Select(m => (object?)m).ToList(); view["costs"] = costs; view["blocked"] = blocked;
            return view;
        }

    }
}
