using System;
using System.Collections.Generic;
using System.Linq;
using MemoryForge.Core;
using MemoryForge.Server;

static class SupportGaugeBotChecks
{
    public static int Run(BattleData data)
    {
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("SupportGaugeBotChecks: " + label);
            checks++;
        }
        Battle Begin(string actor, string card, string enemy, int initialGauge = 0)
        {
            var battle = Battle.Create(data, new BattleSetup
            {
                Party = new List<BattleActorSetup> { new BattleActorSetup { CharId = actor, Gauge = initialGauge } },
                Deck = Enumerable.Range(0, 5).Select(_ => new BattleCardSpec { Id = card, Assigned = card == "com_guard" ? actor : null }).ToList(),
                Enemies = new List<string> { enemy }, Hp = 81, MaxHp = 81, Seed = 42
            });
            battle.Command(new BattleRequest(null, 0, "begin"));
            return battle;
        }
        void Play(Battle battle, string uid, string target)
        {
            var args = BattleArgument.Object(new[]
            {
                new KeyValuePair<string, BattleArgument>("uid", BattleArgument.String(uid)),
                new KeyValuePair<string, BattleArgument>("mode", BattleArgument.String("now")),
                new KeyValuePair<string, BattleArgument>("target", BattleArgument.String(target))
            });
            battle.Command(new BattleRequest(null, battle.State.V, "play", args));
        }

        var harin = Begin("harin", "harin_counter", "e_moth");
        var first = harin.State;
        var firstUid = first.Zones.Hand[0];
        var firstDigest = GraphJson.Digest(first.ToJsonGraph());
        Check(SupportGaugeBot.Allows(data, first, firstUid, first.Enemies[0].Uid), "positive gauge with a surviving enemy is safe");
        Check(GraphJson.Digest(first.ToJsonGraph()) == firstDigest && GraphJson.Digest(harin.State.ToJsonGraph()) == firstDigest,
            "safe probe preserves the supplied snapshot and original battle");

        Play(harin, firstUid, first.Enemies[0].Uid);
        Play(harin, harin.State.Zones.Hand[0], first.Enemies[0].Uid);
        Play(harin, harin.State.Zones.Hand[0], first.Enemies[0].Uid);
        harin.Command(new BattleRequest(null, harin.State.V, "end"));
        var counter = harin.State;
        var counterUid = counter.Zones.Hand[0];
        Check(counter.Phase == "PLAYER" && counter.Enemies[0].Hp == 6 && counter.Actors[0].Gauge == 50 &&
            harin.Forecast(counterUid, "now", counter.Enemies[0].Uid).EnemyHpLoss == 0,
            "authored Harin counter setup has zero immediate preview and six enemy HP");
        var counterDigest = GraphJson.Digest(counter.ToJsonGraph());
        Check(!SupportGaugeBot.Allows(data, counter, counterUid, counter.Enemies[0].Uid),
            "future counter victory before gauge 100 is rejected");
        Check(GraphJson.Digest(counter.ToJsonGraph()) == counterDigest && GraphJson.Digest(harin.State.ToJsonGraph()) == counterDigest,
            "rejected counter probe preserves the supplied snapshot and original battle");

        var ready = Begin("harin", "harin_counter", "e_moth", 30);
        for (int i = 0; i < 3; i++) Play(ready, ready.State.Zones.Hand[0], ready.State.Enemies[0].Uid);
        ready.Command(new BattleRequest(null, ready.State.V, "end"));
        var nearReady = ready.State;
        var readyDigest = GraphJson.Digest(nearReady.ToJsonGraph());
        Check(nearReady.Phase == "PLAYER" && nearReady.Enemies[0].Hp == 6 && nearReady.Actors[0].Gauge == 80 &&
            SupportGaugeBot.Allows(data, nearReady, nearReady.Zones.Hand[0], nearReady.Enemies[0].Uid),
            "card reaching gauge 100 stays usable before its future counter victory");
        Check(GraphJson.Digest(nearReady.ToJsonGraph()) == readyDigest && GraphJson.Digest(ready.State.ToJsonGraph()) == readyDigest,
            "ready-before-end probe preserves the supplied snapshot and original battle");

        var common = Begin("nia", "com_guard", "e_page");
        var commonState = common.State;
        var commonDigest = GraphJson.Digest(commonState.ToJsonGraph());
        Check(common.Forecast(commonState.Zones.Hand[0], "now", commonState.Enemies[0].Uid).EnemyHpLoss == 0 &&
            !SupportGaugeBot.Allows(data, commonState, commonState.Zones.Hand[0], commonState.Enemies[0].Uid),
            "assigned common guard cannot earn the actor gauge");
        Check(GraphJson.Digest(commonState.ToJsonGraph()) == commonDigest && GraphJson.Digest(common.State.ToJsonGraph()) == commonDigest,
            "common-card probe preserves the supplied snapshot and original battle");
        return checks;
    }
}
