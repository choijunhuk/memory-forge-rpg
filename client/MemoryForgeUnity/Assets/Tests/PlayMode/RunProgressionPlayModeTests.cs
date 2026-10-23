// R1a selected first-node runtime cycle, using full authored Battle definitions and the source encounter/starting-common records.
// Expected hashes/commands are actual JS Create/public-command output; no snapshot is restored and no Run state is injected.
// RULES_NOW_PLAY (2026-10-19): regenerated with reports/run_progression_core_20261016/unity_reference_generator.mjs against the current
// engine/rules-core.js and data/game_data.json. The commands, offered cards and final state are unchanged; every battle hash changed
// because card instances now carry "held" (보존) and the rules around it changed.
// RULES_NOW_PLAY §7 gauge 20/50 (2026-10-20): regenerated the same way; commands, offered cards and final state are unchanged again,
// and only the battle hashes changed because each resolved card now adds 20 gauge (turn cap 50) instead of 10 (cap 30).
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
        const string Reference = @"{""scope"":""Selected original full-content legacy first node cycle, actual public commands, not complete Run"",""setup"":{""party"":[{""charId"":""sera"",""res"":0},{""charId"":""harin"",""res"":0},{""charId"":""nia"",""res"":0}],""seed"":1234,""length"":""short"",""memoryPolicy"":""legacy"",""memory"":{},""relics"":[],""difficulty"":{""hp"":100,""dmg"":100},""zones"":[1]},""initial"":""56a6ba8623a1cb296f5a8c83f5c855464a04aea56aaeec9838b1455d272a1973"",""offered"":[""harin_counter"",""nia_scan"",""sera_cover""],""steps"":[{""command"":{""scope"":""run"",""kind"":""enter"",""args"":[""z1f0n0""]},""hash"":""6c4002a268c002df0ea21e79dfc454b429c9fd5add6eb9a98942837b20cb47ee"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c7"",""mode"":""now"",""target"":""e20"",""picks"":[]},""v"":1,""rid"":""r1-play""},""hash"":""2d3aeb53cf15df39334848efe12ad6eeef5f69a21b159c3f172ab09cbda6fdfc"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c1"",""mode"":""now"",""target"":""e20"",""picks"":[]},""v"":2,""rid"":""r2-play""},""hash"":""310d94337b05f35afed92b263ced1e0d351976e3200d08b9c7a8e67afee2d36c"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c15"",""mode"":""now"",""target"":""e20"",""picks"":[]},""v"":3,""rid"":""r3-play""},""hash"":""3052e12284f8caa27d6dfdf28289876c05fda07bd3718de66630d203c6221d1c"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c13"",""mode"":""now"",""target"":""e20"",""picks"":[]},""v"":4,""rid"":""r4-play""},""hash"":""36acf8edf7171613888aa170447e91c5204ce9001e9bbaba16dd0b3fd7c87701"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c2"",""mode"":""now"",""target"":""e19"",""picks"":[]},""v"":5,""rid"":""r5-play""},""hash"":""a1120d25e66224eabed08a2ac1d2b73175164ad1fdb345a13432a26b3f087303"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""end"",""args"":{},""v"":6,""rid"":""r6-end""},""hash"":""53d1bfac98a5d362a79219fd11045a5bcd9fa21da82449a38716432d8ebf6788"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c8"",""mode"":""now"",""target"":""e19"",""picks"":[]},""v"":7,""rid"":""r7-play""},""hash"":""5ee474fbbeb9b6f0b6fa6637d6841772f8db7b6a150a1c0c63ec5426672edf52"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c3"",""mode"":""now"",""target"":""e19"",""picks"":[]},""v"":8,""rid"":""r8-play""},""hash"":""947dd49f3ff9d42be3bcf22c5363cc6c138bd3679ed735d30f43a2893a55f8bc"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c11"",""mode"":""now"",""target"":""e19"",""picks"":[]},""v"":9,""rid"":""r9-play""},""hash"":""076fda36ff7e4e68d74a6c32076a2915f44a72c87305c232d1c37d0dd71ce732"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c17"",""mode"":""now"",""target"":""e19"",""picks"":[]},""v"":10,""rid"":""r10-play""},""hash"":""61d7461ba795d6db3251219d50b84f0a8d1130fccc47b03b7806be2550aa2268"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c10"",""mode"":""now"",""target"":""e19"",""picks"":[]},""v"":11,""rid"":""r11-play""},""hash"":""4bc36920c8a921ec17ec7267ee5749ddcd63696852093827ea30b500c1569d55"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""end"",""args"":{},""v"":12,""rid"":""r12-end""},""hash"":""68c0812052093a4abf715300d0c8794deb9d7d27907b27884a6eaa27c363af15"",""phase"":""BATTLE"",""hp"":243,""coins"":60},{""command"":{""scope"":""battle"",""kind"":""play"",""args"":{""uid"":""c6"",""mode"":""now"",""target"":""e19"",""picks"":[]},""v"":13,""rid"":""r13-play""},""hash"":""7571ce40e6736040f1454dbe028ecf6bc89aeeb5b7315d217c93a505b16df522"",""phase"":""REWARD"",""hp"":233,""coins"":86},{""command"":{""scope"":""run"",""kind"":""takeCard"",""args"":[""harin_counter""]},""hash"":""f5270d780a6b9d2667e2c5ce48a3bd28f5366be8b3804b4c81c55480c4a32493"",""phase"":""REWARD"",""hp"":233,""coins"":86},{""command"":{""scope"":""run"",""kind"":""takePart"",""args"":[]},""hash"":""1b33a3e53db0735603e96ae5724d7442c0db40e6d480dc4581503a09417bf794"",""phase"":""REWARD"",""hp"":233,""coins"":86},{""command"":{""scope"":""run"",""kind"":""proceed"",""args"":[]},""hash"":""bfe1760e896fca29db94d3ef740725efacdb5dc6581077f5250a07430b4a126d"",""phase"":""MAP"",""hp"":233,""coins"":86}],""final"":{""hp"":233,""coins"":86,""battles"":1,""keys"":[""k1"",""k2"",""k3"",""k4"",""k5"",""k6"",""k7"",""k8"",""k9"",""k10"",""k11"",""k12"",""k13"",""k14"",""k15"",""k16"",""k17"",""k18"",""k19""]}}";
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

        // R1b: both policies use actual Create. The reference records successful legacy reassignment and v2 saved-key refusals.
        const string AssignmentReference = @"[{""policy"":""legacy"",""initial"":""56a6ba8623a1cb296f5a8c83f5c855464a04aea56aaeec9838b1455d272a1973"",""commonKey"":""k5"",""ownKey"":""k1"",""steps"":[{""args"":[""k5"",""harin""],""error"":null,""hash"":""fb033004a615194a07a8cfdb36c4e3afaefff09af082776602aea141d66720c3""},{""args"":[""k5"",""sera""],""error"":null,""hash"":""56a6ba8623a1cb296f5a8c83f5c855464a04aea56aaeec9838b1455d272a1973""},{""args"":[""k1"",""harin""],""error"":""BAD_ASSIGN"",""hash"":""56a6ba8623a1cb296f5a8c83f5c855464a04aea56aaeec9838b1455d272a1973""},{""args"":[""k5"",""unknown""],""error"":""BAD_ASSIGN"",""hash"":""56a6ba8623a1cb296f5a8c83f5c855464a04aea56aaeec9838b1455d272a1973""},{""args"":[""missing"",""harin""],""error"":""BAD_ASSIGN"",""hash"":""56a6ba8623a1cb296f5a8c83f5c855464a04aea56aaeec9838b1455d272a1973""}]},{""policy"":""v2"",""initial"":""11a43e7465dc0089d3b8fdfded7acc4b12c84ef0cd77d38a3df66acc075c7cc3"",""commonKey"":""base:sera:4"",""ownKey"":""base:sera:0"",""steps"":[{""args"":[""base:sera:4"",""harin""],""error"":""REASSIGNMENT_REQUIRES_SEPARATE_FLOW"",""hash"":""11a43e7465dc0089d3b8fdfded7acc4b12c84ef0cd77d38a3df66acc075c7cc3""},{""args"":[""base:sera:4"",""sera""],""error"":""REASSIGNMENT_REQUIRES_SEPARATE_FLOW"",""hash"":""11a43e7465dc0089d3b8fdfded7acc4b12c84ef0cd77d38a3df66acc075c7cc3""},{""args"":[""base:sera:0"",""harin""],""error"":""BAD_ASSIGN"",""hash"":""11a43e7465dc0089d3b8fdfded7acc4b12c84ef0cd77d38a3df66acc075c7cc3""},{""args"":[""base:sera:4"",""unknown""],""error"":""BAD_ASSIGN"",""hash"":""11a43e7465dc0089d3b8fdfded7acc4b12c84ef0cd77d38a3df66acc075c7cc3""},{""args"":[""missing"",""harin""],""error"":""BAD_ASSIGN"",""hash"":""11a43e7465dc0089d3b8fdfded7acc4b12c84ef0cd77d38a3df66acc075c7cc3""}]}]";
        [TestCase("legacy")]
        [TestCase("v2")]
        public void ActualCommonAssignmentMatchesOriginalJs(string policy)
        {
            Assert.That(Application.isPlaying, Is.True);
            var source = ((JArray)JToken.Parse(AssignmentReference)).First(x => (string)x["policy"]! == policy);
            var setup = new RunSetup { Seed = 1234, Length = "short", MemoryPolicy = policy, Memory = new Dictionary<string, object?>(),
                Zones = new List<int> { 1 }, Difficulty = new RunDifficulty { Hp = 100, Damage = 100 } };
            if (policy == "v2") { setup.RunId = "unity-assignment"; setup.MemoryVersion = 0L; }
            foreach (string id in new[] { "sera", "harin", "nia" }) setup.Party.Add(new RunPartyEntry { CharId = id, Res = 0 });
            var run = CoreRun.Create(Content(), setup);
            Assert.That(Hash(run), Is.EqualTo((string)source["initial"]!));
            int attempts = 0;
            foreach (var step in (JArray)source["steps"]!)
            {
                string? error = null;
                try { run.Assign((string)step["args"]![0]!, (string)step["args"]![1]!); }
                catch (RuleException failure) { error = failure.Code; }
                Assert.That(error, Is.EqualTo((string?)step["error"]), policy + " assignment #" + attempts);
                Assert.That(Hash(run), Is.EqualTo((string)step["hash"]!), policy + " complete state after assignment #" + attempts);
                attempts++;
            }
            Assert.That(attempts, Is.EqualTo(5));
        }
    }
}
