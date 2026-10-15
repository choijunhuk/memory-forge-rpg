// R1a selected first-node runtime cycle, using full authored Battle definitions and the source encounter/starting-common records.
// Expected hashes/commands are actual JS Create/public-command output; no snapshot is restored and no Run state is injected.
// This is real Unity Play Mode Core acceptance, not completed exploration UI, services/save/restore/server/device acceptance.
#nullable enable
using System.Collections.Generic;
using System.Linq;
using MemoryForge.Client;
using MemoryForge.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using CoreRun = MemoryForge.Core.Run;

namespace MemoryForge.PlayModeTests
{
    public class RunProgressionPlayModeTests
    {
        const string Reference = @"{""scope"":""Selected original full-content legacy first node cycle, actual public commands, not complete Run"",""setup"":{""party"":[{""charId"":""sera"",""res"":0},{""charId"":""harin"",""res"":0},{""charId"":""nia"",""res"":0}],""seed"":1234,""length"":""short"",""memoryPolicy"":""legacy"",""memory"":{},""relics"":[],""difficulty"":{""hp"":100,""dmg"":100},""zones"":[1]},""initial"":""56a6ba8623a1cb296f5a8c83f5c855464a04aea56aaeec9838b1455d272a1973"",""offered"":[""harin_counter"",""nia_scan"",""sera_cover""],""steps"":[{""command"":{""scope"":""run"",""kind"":""enter"",""args"":[""z1f0n0""]},""hash"":""e34a764590397e88f5084a5a05aa9451f3e8fb543594c1e3e453f67b519bd957"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c7"",""mode"":""now"",""target"":""e20"",""picks"":[]},""v"":1,""rid"":""r1-play""},""hash"":""8baea96947f50db2e2b977566294bfaf25373d3432ac2fdced343f13541148bd"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c1"",""mode"":""now"",""target"":""e20"",""picks"":[]},""v"":2,""rid"":""r2-play""},""hash"":""759926fa52a65c2e4c3205e165a87933dce145f390006e819b7c41c69799a889"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c15"",""mode"":""now"",""target"":""e20"",""picks"":[]},""v"":3,""rid"":""r3-play""},""hash"":""b64d8b9461d4305ae3663577134bd63e700e76fa5cd2cd6a65e4851c07eb9f67"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c13"",""mode"":""now"",""target"":""e20"",""picks"":[]},""v"":4,""rid"":""r4-play""},""hash"":""10e418af4201a6d7ba389ca2083d73c7c67f7cb3c11badb02306389607ebe88d"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c2"",""mode"":""now"",""target"":""e19"",""picks"":[]},""v"":5,""rid"":""r5-play""},""hash"":""232ea9bd7edd974847846a7a4a13e5371ea5cb6f51ea2375160f3dcf4a8a342f"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""end"",""args"":{},""v"":6,""rid"":""r6-end""},""hash"":""279fdc9fa22ef34d3bea78d4f174bb2564753a47ded412cfa70006166c9d8117"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c8"",""mode"":""now"",""target"":""e19"",""picks"":[]},""v"":7,""rid"":""r7-play""},""hash"":""0e89b1d34632dd394589be0cb419d5d1efec086dbf3a4e3625fa7bc73c5b080f"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c3"",""mode"":""now"",""target"":""e19"",""picks"":[]},""v"":8,""rid"":""r8-play""},""hash"":""4e9d8ddb77858afbcc2c296b10c9e9f2130d3e8d68492980120484e825d055d6"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c11"",""mode"":""now"",""target"":""e19"",""picks"":[]},""v"":9,""rid"":""r9-play""},""hash"":""b0f39d7b28c06b9a26c70a1fc1b815859571c8764199963ba65f58f625cbf72a"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c17"",""mode"":""now"",""target"":""e19"",""picks"":[]},""v"":10,""rid"":""r10-play""},""hash"":""c7c9f19ea141bc8c9b93a689e6e4174e7bacff6245b60955e1c85435d8355ff7"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c10"",""mode"":""now"",""target"":""e19"",""picks"":[]},""v"":11,""rid"":""r11-play""},""hash"":""6d29b4a4c79f1be5fedf09e3f7c0c90e31f3f0e5873e6113f55bb41a4631da6b"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""end"",""args"":{},""v"":12,""rid"":""r12-end""},""hash"":""e70def058ce3cceea752a91a83f7ff62b5f77899b742f85f42fcc269a45ef382"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c6"",""mode"":""now"",""target"":""e19"",""picks"":[]},""v"":13,""rid"":""r13-play""},""hash"":""7571ce40e6736040f1454dbe028ecf6bc89aeeb5b7315d217c93a505b16df522"",""phase"":""REWARD"",""hp"":233,""coins"":86},{""command"":{""scope"":""run"",""kind"":""takeCard"",""args"":[""harin_counter""]},""hash"":""f5270d780a6b9d2667e2c5ce48a3bd28f5366be8b3804b4c81c55480c4a32493"",""phase"":""REWARD"",""hp"":233,""coins"":86},{""command"":{""scope"":""run"",""kind"":""takePart"",""args"":[]},""hash"":""1b33a3e53db0735603e96ae5724d7442c0db40e6d480dc4581503a09417bf794"",""phase"":""REWARD"",""hp"":233,""coins"":86},{""command"":{""scope"":""run"",""kind"":""proceed"",""args"":[]},""hash"":""bfe1760e896fca29db94d3ef740725efacdb5dc6581077f5250a07430b4a126d"",""phase"":""MAP"",""hp"":233,""coins"":86}],""final"":{""hp"":233,""coins"":86,""battles"":1,""keys"":[""k1"",""k2"",""k3"",""k4"",""k5"",""k6"",""k7"",""k8"",""k9"",""k10"",""k11"",""k12"",""k13"",""k14"",""k15"",""k16"",""k17"",""k18"",""k19""]}}";
        static RunData Content()
        {
            var content = ClientBattleContent.Load();
            var memory = new MemoryRulesData { Battle = content.Battle, StartingCommon = content.StartingCommon };
            var data = new RunData { Memory = memory, Stats = new StatsConfig() };
            foreach (var entry in content.CharacterWeaponTypes)
                data.Characters.Add(new RunCharacterMetadata { Id = entry.Key, WeaponType = entry.Value });
            foreach (var entry in content.Encounters)
                data.Encounters.Add(new RunEncounterDefinition { Zone = entry.Key, Normal = entry.Value.Normal.Select(x => x.ToList()).ToList(),
                    Elite = entry.Value.Elite.Select(x => x.ToList()).ToList(), Boss = entry.Value.Boss.Select(x => x.ToList()).ToList() });
            // This selected cycle never enters an event node. Full Run events/progression import is still a separate client task.
            return data;
        }
        static BattleArgument Argument(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object: return BattleArgument.Object(((JObject)token).Properties().Select(p => new KeyValuePair<string, BattleArgument>(p.Name, Argument(p.Value))));
                case JTokenType.Array: return BattleArgument.Array(((JArray)token).Select(Argument));
                case JTokenType.String: return BattleArgument.String((string)token!);
                case JTokenType.Integer: return BattleArgument.Integer((long)token);
                case JTokenType.Boolean: return BattleArgument.Boolean((bool)token);
                case JTokenType.Null: return BattleArgument.Null();
                default: throw new System.InvalidOperationException("Unsupported recorded argument");
            }
        }
        static string Hash(CoreRun run) => CanonicalState.Hash(new Dictionary<string, object?> { ["run"] = run.State.ToJsonGraph(), ["memory"] = null });

        [Test]
        public void ActualRunEnterBattleRewardAndProceedMatchEveryOriginalJsHash()
        {
            Assert.That(Application.isPlaying, Is.True);
            var source = JObject.Parse(Reference);
            var setup = new RunSetup { Seed = 1234, Length = "short", MemoryPolicy = "legacy", Memory = new Dictionary<string, object?>(),
                Zones = new List<int> { 1 }, Difficulty = new RunDifficulty { Hp = 100, Damage = 100 } };
            foreach (string id in new[] { "sera", "harin", "nia" }) setup.Party.Add(new RunPartyEntry { CharId = id, Res = 0 });
            var run = CoreRun.Create(Content(), setup);
            Assert.That(Hash(run), Is.EqualTo((string)source["initial"]!));
            int commands = 0, battleCommands = 0;
            foreach (var step in (JArray)source["steps"]!)
            {
                var command = step["command"]!; string kind = (string)command["kind"]!; var args = command["args"]!;
                if ((string)command["scope"]! == "battle")
                {
                    Assert.That(run.State.Battle!.V, Is.EqualTo((int)command["v"]!));
                    run.BattleCommand(kind, Argument(args)); battleCommands++;
                }
                else switch (kind)
                {
                    case "enter": run.Enter((string)args[0]!); break;
                    case "takeCard": run.TakeCard((string)args[0]!); break;
                    case "takePart": run.TakePart(); break;
                    case "takeRelic": run.TakeRelic((string)args[0]!); break;
                    case "proceed": run.Proceed(); break;
                    default: Assert.Fail("Unexpected recorded Run method " + kind); break;
                }
                Assert.That(Hash(run), Is.EqualTo((string)step["hash"]!), "actual command " + commands + " " + kind);
                Assert.That(run.State.Phase, Is.EqualTo((string)step["phase"]!)); commands++;
                if (run.State.Battle != null)
                {
                    string before = Hash(run); var snapshot = run.State; snapshot.Battle!.Hp = 1; snapshot.Rng[0] ^= 1; snapshot.Deck.Clear();
                    var view = run.BattlePublicView()!; view.Clear();
                    Assert.That(Hash(run), Is.EqualTo(before), "snapshot/public view must remain detached in the actual runtime");
                }
            }
            Assert.That(commands, Is.EqualTo(17)); Assert.That(battleCommands, Is.EqualTo(13));
            Assert.That(new[] { run.State.Hp, run.State.Coins, run.State.Statistics.Battles, run.State.Deck.Count }, Is.EqualTo(new[] { 233, 86, 1, 19 }));
            Assert.That(run.State.Phase, Is.EqualTo("MAP")); Assert.That(run.State.Battle, Is.Null);
            Assert.That(run.State.Deck.Select(c => c.Key), Is.EqualTo(((JArray)source["final"]!["keys"]!).Select(x => (string)x!).ToArray()));
            Assert.That(setup.Party.All(p => p.Power4 == null && p.Hp == null), Is.True, "actual Create must not mutate caller stats");
        }
    }
}
