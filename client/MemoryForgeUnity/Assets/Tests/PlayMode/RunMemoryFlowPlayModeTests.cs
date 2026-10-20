// Editor Play Mode: original full Run commands, original content/costs and actual C# saved-memory restarts.
#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MemoryForge.Client;
using MemoryForge.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CoreRun = MemoryForge.Core.Run;

namespace MemoryForge.PlayModeTests
{
    public class RunMemoryFlowPlayModeTests
    {
        static object? Graph(JToken value) => value.Type switch
        {
            JTokenType.Object => ((JObject)value).Properties().ToDictionary(p => p.Name, p => Graph(p.Value)),
            JTokenType.Array => ((JArray)value).Select(Graph).ToList(),
            JTokenType.String => (string?)value, JTokenType.Integer => (long)value, JTokenType.Float => (double)value,
            JTokenType.Boolean => (bool)value, JTokenType.Null => null,
            _ => throw new System.InvalidOperationException("Unexpected recorded JSON type")
        };
        static BattleArgument Argument(JToken value) => value.Type switch
        {
            JTokenType.Object => BattleArgument.Object(((JObject)value).Properties().Select(p => new KeyValuePair<string, BattleArgument>(p.Name, Argument(p.Value)))),
            JTokenType.Array => BattleArgument.Array(((JArray)value).Select(Argument)), JTokenType.String => BattleArgument.String((string)value!),
            JTokenType.Integer => BattleArgument.Integer((long)value), JTokenType.Boolean => BattleArgument.Boolean((bool)value), JTokenType.Null => BattleArgument.Null(),
            _ => throw new System.InvalidOperationException("Unexpected battle argument")
        };
        static RunSetup Setup(JToken source)
        {
            var setup = new RunSetup { Seed = (long)source["seed"]!, Length = (string)source["length"]!, MemoryPolicy = (string)source["memoryPolicy"]!,
                Memory = Graph(source["memory"]!), Zones = source["zones"]!.Select(x => (int)x).ToList(),
                Relics = source["relics"]!.Select(x => (string)x!).ToList(), Difficulty = new RunDifficulty { Hp = (int)source["difficulty"]!["hp"]!, Damage = (int)source["difficulty"]!["dmg"]! } };
            foreach (var party in source["party"]!) setup.Party.Add(RunPartyEntry.FromJsonGraph(Graph(party)));
            if (source["runId"] != null) { setup.RunId = (string)source["runId"]!; setup.MemoryVersion = (long)source["memoryVersion"]!; setup.UseMemory = (bool)source["useMemory"]!; }
            return setup;
        }
        static string Hash(CoreRun run, object? store) => CanonicalState.Hash(new Dictionary<string, object?> { ["run"] = run.State.ToJsonGraph(), ["memory"] = store });
        static JObject Transcript(CoreRun run)
        {
            var graph = JObject.FromObject(run.State.ToJsonGraph());
            var battle = graph["battle"] as JObject;
            return new JObject { ["runLog"] = graph["log"]!.DeepClone(), ["battleLog"] = battle?["log"]?.DeepClone() ?? JValue.CreateNull(),
                ["ev"] = battle?["ev"]?.DeepClone() ?? JValue.CreateNull() };
        }

        [UnityTest]
        public IEnumerator OriginalLegacyAndV2RunsSaveAndRestartThroughActualUnityCore()
        {
            Assert.That(Application.isPlaying, Is.True);
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../reports/run_memory_core_20261016/unity_full_run_reference.json"));
            var envelope = JObject.Parse(File.ReadAllText(path));
            Assert.That((string)envelope["schema"]!, Is.EqualTo("mf-unity-full-run-v1"));
            var cases = (JArray)envelope["cases"]!; Assert.That(cases.Count, Is.EqualTo(3));
            Assert.That(cases.Select(c => (string)c["id"]!), Is.EqualTo(new[] { "seed1000-short-legacy", "seed1102-full-legacy", "seed1102-full-v2" }));
            int commands = 0, saved = 0, queries = 0;
            foreach (var source in cases)
            {
                var data = ClientRunContent.Load(); var setup = Setup(source["setup"]!); var run = CoreRun.Create(data, setup);
                Dictionary<string, object?>? store = setup.MemoryPolicy == "v2" ? MemoryV2.Migrate(data.Memory, new Dictionary<string, object?>(), new Dictionary<string, object?>()).ToJsonGraph() : null;
                Assert.That(Hash(run, store), Is.EqualTo((string)source["initial"]!));
                Assert.That(JToken.DeepEquals(Transcript(run), source["initialTranscript"]), Is.True, "initial log/event state");
                foreach (var step in source["steps"]!)
                {
                    var command = step["command"]!; string kind = (string)command["kind"]!; var args = command["args"]!;
                    if (kind == "commitMemory")
                    {
                        string before = Hash(run, store); var beforeTranscript = Transcript(run);
                        Assert.That(JToken.DeepEquals(JToken.FromObject(run.MemoryChanges()), step["changes"]), Is.True); queries++;
                        if (store != null) { Assert.That(JToken.DeepEquals(JToken.FromObject(run.PreviewMemory(Graph(args[0]!["selectedChangeIds"]!))), step["preview"]), Is.True); queries++; }
                        Assert.That(Hash(run, store), Is.EqualTo(before), "memory queries leave live Run/store unchanged");
                        Assert.That(JToken.DeepEquals(Transcript(run), beforeTranscript), Is.True, "memory queries also leave logs/events unchanged");
                    }
                    string? error = null; object? returned = null; bool hasReturn = false;
                    try
                    {
                        if ((string)command["scope"]! == "battle") { returned = run.BattleCommand(kind, Argument(args)).ToJsonGraph(); hasReturn = true; }
                        else switch (kind)
                        {
                            case "enter": run.Enter((string)args[0]!); break;
                            case "takeCard": run.TakeCard((string)args[0]!); break;
                            case "skipCard": run.SkipCard(); break;
                            case "takePart": run.TakePart(); break;
                            case "takeRelic": run.TakeRelic((string)args[0]!); break;
                            case "applyFoundPart": run.ApplyFoundPart((string)args[0]!, (string)args[1]!); break;
                            case "evolve": run.Evolve((string)args[0]!, (string)args[1]!); break;
                            case "proceed": run.Proceed(); break;
                            case "archive": run.Archive((string)args[0]!); break;
                            case "rest": run.Rest((string)args[0]!, (string?)args.ElementAtOrDefault(1)); break;
                            case "forgeOffer": returned = run.ForgeOffer((string)args[0]!); hasReturn = true; break;
                            case "forgeApply": run.ForgeApply((string)args[0]!); break;
                            case "forgeRemove": run.ForgeRemove((string)args[0]!); break;
                            case "eventChoose": run.EventChoose((int)args[0]!, Graph(args.ElementAtOrDefault(1) ?? JValue.CreateNull())); break;
                            case "buy": run.Buy((string)args[0]!, (string?)args[1], (string?)args.ElementAtOrDefault(2)); break;
                            case "commitMemory":
                                if (store != null) Assert.That(JToken.DeepEquals(JToken.FromObject(store), args[1]), Is.True);
                                var result = run.CommitMemory(Graph(args[0]!), store);
                                returned = result; hasReturn = true;
                                if (store != null) store = (Dictionary<string, object?>)Graph(JToken.FromObject(result["state"]!))!;
                                break;
                            default: Assert.Fail("Unexpected source command " + kind); break;
                        }
                    }
                    catch (RuleException failure) { error = failure.Code; }
                    Assert.That(error, Is.EqualTo((string?)step["code"]), source["id"] + " " + kind);
                    if (error == null)
                    {
                        Assert.That((string)step["result"]!["kind"]!, Is.EqualTo(hasReturn ? "json" : "undefined"));
                        if (hasReturn) Assert.That(JToken.DeepEquals(JToken.FromObject(returned!), step["result"]!["value"]), Is.True, source["id"] + " complete return " + kind);
                    }
                    Assert.That(Hash(run, store), Is.EqualTo((string)step["hash"]!), source["id"] + " actual command " + commands);
                    Assert.That(JToken.DeepEquals(Transcript(run), step["transcript"]), Is.True, source["id"] + " complete logs/events " + commands);
                    commands++; if (commands % 40 == 0) yield return null;
                }
                Assert.That(run.State.Phase, Is.EqualTo("DONE"));
                Assert.That((string)JObject.FromObject(run.State.Result!)["outcome"]!, Is.EqualTo((string)source["outcome"]!));
                var next = Setup(source["setup"]!); next.Memory = Graph(JToken.FromObject(run.State.SavedMemory!)); next.Seed = ((long)source["setup"]!["seed"]! + 99) & 0xffffffffL;
                if (store != null) { next.RunId = (string)source["setup"]!["runId"]! + "-reload"; next.MemoryVersion = store["version"]; }
                var restarted = CoreRun.Create(data, next);
                Assert.That(Hash(restarted, store), Is.EqualTo((string)source["reload"]!), "restart uses actual C# saved memory"); saved++;
                Assert.That(JToken.DeepEquals(Transcript(restarted), source["reloadTranscript"]), Is.True, "restart log/event state");
            }
            // RULES_NOW_PLAY §7 gauge 20/50 (2026-10-20): the regenerated reference runs 666 actual commands (reports/run_memory_core_20261016/unity_full_run_reference.json).
            Assert.That(commands, Is.EqualTo(666)); Assert.That(saved, Is.EqualTo(3)); Assert.That(queries, Is.EqualTo(4));
        }
    }
}
