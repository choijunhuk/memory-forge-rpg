// Real Unity Mono regressions for the original JS receipt-body contract; no account or persistent-store operation.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using MemoryForge.Client;
using MemoryForge.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using CoreRun = MemoryForge.Core.Run;

namespace MemoryForge.PlayModeTests
{
    public class RunJsonNumberPlayModeTests
    {
        [Test]
        public void ActualMonoJsonNumbersMatchAllOriginalJsExactBits()
        {
            Assert.That(Application.isPlaying, Is.True);
            // Editor Play Mode evidence: the repository fixture is independently decoded/validated by Node and CoreChecks.
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../engine/tests/vectors/run_json_number_vectors.json.gz"));
            JObject fixture;
            using (var file = File.OpenRead(path))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, new UTF8Encoding(false, true))) fixture = JObject.Parse(reader.ReadToEnd());
            Assert.That((string)fixture["schema"]!, Is.EqualTo("mf-run-json-number-v1"));
            var rows = (JArray)fixture["cases"]!; Assert.That(rows.Count, Is.EqualTo(2176));
            var method = typeof(CoreRun).GetMethod("JsonText", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var row in rows)
            {
                string bits = (string)row["bits"]!;
                double number = BitConverter.Int64BitsToDouble(unchecked((long)ulong.Parse(bits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)));
                Assert.That((string)method.Invoke(null, new object[] { number })!, Is.EqualTo((string)row["json"]!), "actual Unity Mono, binary64 " + bits);
            }
        }

        [Test]
        public void MatchingOriginalJsReceiptWithALargeIntegerVersionReplaysBeforePhaseCheck()
        {
            Assert.That(Application.isPlaying, Is.True);
            var data = ClientRunContent.Load();
            var setup = new RunSetup { Seed = 1234, Length = "short", MemoryPolicy = "v2", RunId = "numeric-public-witness", MemoryVersion = 0L };
            foreach (string cid in new[] { "sera", "harin", "nia" }) setup.Party.Add(new RunPartyEntry { CharId = cid, Res = 0 });
            var run = CoreRun.Create(data, setup);
            var current = MemoryV2.Migrate(data.Memory, new Dictionary<string, object?>(), new Dictionary<string, object?>()).ToJsonGraph();
            // Exact body captured from the original JS public CommitMemory witness, not produced by the C# formatter under test.
            const string body = "{\"runId\":\"numeric-public-witness\",\"expectedMemoryVersion\":9.999999999999997e+22,\"selectedChangeIds\":[]}";
            var response = new Dictionary<string, object?> { ["memory"] = new Dictionary<string, object?>(), ["used"] = new Dictionary<string, object?>(), ["version"] = 0L };
            ((IDictionary<string, object?>)current["receipts"]!)["numeric-replay"] = new Dictionary<string, object?> { ["body"] = body, ["response"] = response };
            var request = new Dictionary<string, object?> { ["requestId"] = "numeric-replay", ["runId"] = "numeric-public-witness",
                ["expectedMemoryVersion"] = BitConverter.Int64BitsToDouble(0x44b52d02c7e14af5L), ["selectedChangeIds"] = new List<object?>() };
            string before = JsonConvert.SerializeObject(current);
            var result = run.CommitMemory(request, current);
            Assert.That(run.State.Phase, Is.EqualTo("DONE"));
            Assert.That(JToken.DeepEquals(JToken.FromObject(result["response"]!), JToken.FromObject(response)), Is.True);
            Assert.That(JsonConvert.SerializeObject(current), Is.EqualTo(before), "input store stays untouched");
        }

        [TestCase("next-double")]
        [TestCase("wrong-body")]
        public void NearbyNumericReceiptBodiesRemainConflicts(string mode)
        {
            Assert.That(Application.isPlaying, Is.True);
            var data = ClientRunContent.Load();
            var setup = new RunSetup { Seed = 1234, Length = "short", MemoryPolicy = "v2", RunId = "numeric-public-witness", MemoryVersion = 0L };
            foreach (string cid in new[] { "sera", "harin", "nia" }) setup.Party.Add(new RunPartyEntry { CharId = cid, Res = 0 });
            var run = CoreRun.Create(data, setup);
            var current = MemoryV2.Migrate(data.Memory, new Dictionary<string, object?>(), new Dictionary<string, object?>()).ToJsonGraph();
            const string body = "{\"runId\":\"numeric-public-witness\",\"expectedMemoryVersion\":9.999999999999997e+22,\"selectedChangeIds\":[]}";
            var response = new Dictionary<string, object?> { ["memory"] = new Dictionary<string, object?>(), ["used"] = new Dictionary<string, object?>(), ["version"] = 0L };
            ((IDictionary<string, object?>)current["receipts"]!)["numeric-replay"] = new Dictionary<string, object?>
            { ["body"] = mode == "wrong-body" ? body.Replace("999999999999997", "999999999999998") : body, ["response"] = response };
            var request = new Dictionary<string, object?> { ["requestId"] = "numeric-replay", ["runId"] = "numeric-public-witness",
                ["expectedMemoryVersion"] = BitConverter.Int64BitsToDouble(mode == "next-double" ? 0x44b52d02c7e14af6L : 0x44b52d02c7e14af5L), ["selectedChangeIds"] = new List<object?>() };
            string stateBefore = CanonicalState.Hash(run.State.ToJsonGraph()), storeBefore = JsonConvert.SerializeObject(current);
            var error = Assert.Throws<RuleException>(() => run.CommitMemory(request, current));
            Assert.That(error!.Code, Is.EqualTo("REQUEST_CONFLICT"));
            Assert.That(run.State.Phase, Is.EqualTo("MAP"));
            Assert.That(CanonicalState.Hash(run.State.ToJsonGraph()), Is.EqualTo(stateBefore));
            Assert.That(JsonConvert.SerializeObject(current), Is.EqualTo(storeBefore));
        }
    }
}
