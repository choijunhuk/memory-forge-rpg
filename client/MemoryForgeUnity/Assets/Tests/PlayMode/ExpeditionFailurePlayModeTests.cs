// P1-CARD-FLOW-FAIL-CLOSED §14.7/§16.4: Play Mode checks of the expedition view's fail-closed recovery over the real shared-core Run.
// Faults are test-local configurations of the client's own freshly loaded content (an unknown encounter enemy, an unknown enemy move)
// reached through real public commands, or the private Do boundary called by reflection for failures no content can produce; no
// expected Run state is injected. Each fault is removed before the recovery action. Not Android/device, server, account or reconnect
// acceptance: the quarantine is local practice only.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MemoryForge.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using CoreRun = MemoryForge.Core.Run;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public class ExpeditionFailurePlayModeTests
    {
        const long Seed = 1234;
        const string Recovery = "RECOVERY_REQUIRED";
        const string Diagnostic = @"^\[MemoryForge\] Practice expedition quarantined after a failed local Run calculation \(";
        readonly List<GameObject> hosts = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator DestroyOwnedHosts()
        {
            foreach (var host in hosts) if (host != null) Object.Destroy(host);
            hosts.Clear();
            yield return null;
        }

        sealed class Opened
        {
            public GameObject Host = null!;
            public MemoryForgeExpeditionClient Client = null!;
        }

        // The seed is set on a disabled host before the component's real OnEnable start; nothing else is injected.
        IEnumerator Open(Opened result, long seed = Seed)
        {
            var host = new GameObject("FailureTestHost"); hosts.Add(host);
            host.SetActive(false);
            var client = host.AddComponent<MemoryForgeExpeditionClient>(); client.Seed = seed; client.Length = "short";
            host.SetActive(true);
            yield return null;
            result.Host = host; result.Client = client;
            Assert.That(client.RecoveryRequired, Is.False);
        }

        // A genuinely earned session save: in the first battle the party only ends turns until it falls, then the defeat is committed
        // with no change (memory version 0 → 1), and the next departure starts from it.
        static IEnumerator EarnSave(MemoryForgeExpeditionClient client)
        {
            Assert.That(client.EnterNode(client.Snapshot!.Map!.Floors[0][0].Id), Is.True, client.LastError);
            for (int ends = 0; ends < 200 && client.Snapshot!.Phase == "BATTLE"; ends++)
            {
                Assert.That(client.EndTurn(), Is.True, client.LastError);
                Assert.That(client.SkipTurnSummary() && client.ContinueTurnSummary(), Is.True);
                if (ends % 8 == 7) yield return null;
            }
            // The fixed seed is the acceptance witness: the bounded real driver must reach this defeat.
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("DEFEAT"), "seed 1234's first battle must defeat a party that only ends turns within 200 actual ends");
            Assert.That(client.CommitMemory(), Is.True, client.LastError);
            Assert.That(client.SessionMemoryVersion, Is.EqualTo(1), "the defeat's save is a real committed version");
            // The whole earned store, not only memory/version: one history entry and the commit's receipt.
            var store = Store(client);
            Assert.That(((IList)store["history"]!).Count, Is.EqualTo(1), "one committed history entry");
            Assert.That(((IDictionary<string, object?>)store["receipts"]!).ContainsKey(client.LastRequestId!), Is.True, "the commit's receipt");
            Assert.That(client.Depart(), Is.True, client.LastError);
            yield return null;
        }

        // The regression: Run.Enter moves position and draws RNG before Battle.Create refuses an unknown encounter enemy. The client must
        // not keep playing that partly changed Run: it quarantines, keeps the earned save, refuses everything through disable/enable and
        // resume, and its one action discards the failed Run unsaved and departs from the kept store once the fault is removed.
        [UnityTest]
        public IEnumerator PartialEnterFailureQuarantinesKeepsTheEarnedSaveAndTheDiscardDepartsFromIt()
        {
            var o = new Opened(); yield return Open(o);
            var client = o.Client;
            yield return EarnSave(client);
            var start = client.Snapshot!;
            Assert.That((start.RunId, start.MemoryVersion, start.Phase), Is.EqualTo(("practice-2-" + (Seed + 1), (long?)1, "MAP")));
            string store = StorePrint(client); var savedMemory = client.SessionMemory; string? request = client.LastRequestId;

            // Test-local fault in the client's own content: every normal encounter of this zone names an enemy no definition has.
            var encounter = Content(client).Encounters.Single(e => e.Zone == start.Zone!.Value.ToString(CultureInfo.InvariantCulture));
            var original = encounter.Normal;
            encounter.Normal = new List<List<string>> { new List<string> { "test_missing_enemy" } };
            string first = start.Map!.Floors[0][0].Id;
            LogAssert.Expect(LogType.Error, new Regex(Diagnostic + @"UNKNOWN_ENEMY\)"));
            Assert.That(client.EnterNode(first), Is.False);
            Assert.That(client.RecoveryRequired, Is.True, "the failed Enter quarantines the practice");

            // The actual partial change: position set and RNG drawn, yet no battle and still the map phase.
            var damaged = client.Snapshot!;
            Assert.That(damaged.Position, Is.EqualTo(first), "Run.Enter had already moved before the battle failed");
            Assert.That(damaged.Rng, Is.Not.EqualTo(start.Rng), "Run.Enter had already drawn from the Run RNG");
            Assert.That(damaged.Battle, Is.Null);
            Assert.That(damaged.Phase, Is.EqualTo("MAP"));
            Assert.That(StorePrint(client), Is.EqualTo(store), "the last valid session store is kept");
            string failed = Print(damaged);

            yield return null; Canvas.ForceUpdateCanvases();
            var view = SingleView(o.Host);
            Assert.That(view.GetComponentsInChildren<Button>().Select(b => b.name), Is.EqualTo(new[] { "RecoveryDiscard" }), "one explicit action and no normal control");
            AssertReadable(view, t => t == "이 연습 탐사를 계속할 수 없어요", "recovery title");
            AssertReadable(view, t => t.Contains("저장하지 않고 버려요"), "the failed Run is not saved");
            AssertReadable(view, t => t.Contains("기억 버전 1"), "the kept memory version");
            AssertReadable(view, t => t.Contains("앱을 닫으면 사라져요"), "the session-only limitation");
            Assert.That(view.GetComponentsInChildren<Text>().Any(t => t.text.Contains("동전") || t.text.Contains("보상을 고르세요") || t.text.Contains("층")), Is.False,
                "nothing of the failed Run is drawn");
            AssertEverythingRefused(client, failed, store, first);
            Assert.That(client.LastRequestId, Is.EqualTo(request), "no save request was made");

            // The quarantine survives disable/enable and focus/pause resume; the failed Run is neither redrawn nor changed.
            o.Host.SetActive(false);
            yield return null;
            Assert.That(client.RecoveryRequired, Is.True, "disabling keeps the quarantine");
            o.Host.SetActive(true);
            yield return null;
            Assert.That(client.RecoveryRequired, Is.True, "enabling redraws the recovery screen, not the failed Run");
            Assert.That(SingleView(o.Host).GetComponentsInChildren<Button>().Select(b => b.name), Is.EqualTo(new[] { "RecoveryDiscard" }));
            o.Host.SendMessage("OnApplicationPause", false); o.Host.SendMessage("OnApplicationFocus", true);
            Assert.That(client.RecoveryRequired, Is.True, "resuming keeps the quarantine");
            Assert.That(Print(client.Snapshot!), Is.EqualTo(failed));
            Assert.That(StorePrint(client), Is.EqualTo(store));

            // Fault removed, then the one action by a real click: the failed Run is dropped unsaved and the next departure number starts
            // exactly from the kept store.
            encounter.Normal = original;
            yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(o.Host, "RecoveryDiscard"));
            Assert.That(client.RecoveryRequired, Is.False);
            Assert.That(client.LastError, Is.Null);
            var next = client.Snapshot!;
            var expected = CoreRun.Create(ClientRunContent.Load(), MemoryForgeExpeditionClient.PracticeSetup(Seed + 2, "short", "practice-3-" + (Seed + 2), savedMemory, 1));
            Assert.That(JToken.DeepEquals(Tree(next), Tree(expected.State)), Is.True, "the new departure is exactly Create from the kept store with the next departure number");
            Assert.That(StorePrint(client), Is.EqualTo(store), "the failed Run was never saved");
            Assert.That(client.ShowingResult, Is.False);
            yield return null;
            Assert.That(client.EnterNode(next.Map!.Floors[0][0].Id), Is.True, client.LastError);
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("BATTLE"), "with the fault removed the first node enters a real battle");
        }

        // P1-CARD-FLOW-FAILURE-RESTART-FIX: a recovery retry whose real Run.Create throws (test-local content without memory rules), or
        // that has no content or store to depart from, must keep the failed Run, the quarantine, the earned store and the departure
        // numbering, let nothing escape, survive disable/enable and resume, and depart exactly once after the content is repaired.
        [UnityTest]
        public IEnumerator FailedRecoveryRetriesKeepTheQuarantineAndTheEarnedSaveUntilARepairedRetryDepartsOnce()
        {
            var o = new Opened(); yield return Open(o);
            var client = o.Client;
            yield return EarnSave(client);
            var start = client.Snapshot!;
            Assert.That((start.RunId, start.MemoryVersion), Is.EqualTo(("practice-2-" + (Seed + 1), (long?)1)));
            string store = StorePrint(client); var savedMemory = client.SessionMemory;
            var content = Content(client);

            // A genuine quarantine through the real partial Enter.
            var encounter = content.Encounters.Single(e => e.Zone == start.Zone!.Value.ToString(CultureInfo.InvariantCulture));
            var rows = encounter.Normal;
            encounter.Normal = new List<List<string>> { new List<string> { "test_missing_enemy" } };
            LogAssert.Expect(LogType.Error, new Regex(Diagnostic + @"UNKNOWN_ENEMY\)"));
            Assert.That(client.EnterNode(start.Map!.Floors[0][0].Id), Is.False);
            Assert.That(client.RecoveryRequired, Is.True);
            encounter.Normal = rows;
            string failed = Print(client.Snapshot!);
            yield return null; Canvas.ForceUpdateCanvases();

            void StillQuarantined(string what)
            {
                Assert.That(client.RecoveryRequired, Is.True, what + ": the quarantine stays");
                Assert.That(client.Snapshot, Is.Not.Null, what + ": the failed Run is kept, no Run is lost");
                Assert.That(Print(client.Snapshot!), Is.EqualTo(failed), what + ": no new Run started and the failed one is unchanged");
                Assert.That(StorePrint(client), Is.EqualTo(store), what + ": the earned store and version are kept");
            }

            // Test-local invalid content: the real Run.Create of the retry throws a non-rule exception. Nothing escapes, twice.
            var memory = content.Memory;
            content.Memory = null!;
            const string Restart = @"^\[MemoryForge\] Practice recovery could not start a new departure \(";
            LogAssert.Expect(LogType.Error, new Regex(Restart));
            Click(ButtonNamed(o.Host, "RecoveryDiscard"));
            StillQuarantined("a throwing retry by a real click");
            LogAssert.Expect(LogType.Error, new Regex(Restart));
            bool retried = true;
            Assert.DoesNotThrow(() => retried = client.DiscardFailedPractice());
            Assert.That(retried, Is.False);
            StillQuarantined("a throwing retry by the public action");
            LogAssert.NoUnexpectedReceived();
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(SingleView(o.Host).GetComponentsInChildren<Button>().Select(b => b.name), Is.EqualTo(new[] { "RecoveryDiscard" }), "still only the read-only recovery screen");
            AssertReadable(SingleView(o.Host), t => t.StartsWith("새 탐사를 시작하지 못했어요", StringComparison.Ordinal), "failed retry notice");
            AssertEverythingRefused(client, failed, store, start.Map!.Floors[0][0].Id);
            LogAssert.NoUnexpectedReceived(); // ordinary blocked commands log nothing

            // The gate survives disable/enable and resume without StartPractice resetting the session memory.
            o.Host.SetActive(false);
            yield return null;
            o.Host.SetActive(true);
            yield return null;
            o.Host.SendMessage("OnApplicationPause", false); o.Host.SendMessage("OnApplicationFocus", true);
            StillQuarantined("disable/enable and resume");
            content.Memory = memory;

            // No content or no store to depart from: the retry is refused without falling back to StartPractice.
            var dataField = typeof(MemoryForgeExpeditionClient).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var storeField = typeof(MemoryForgeExpeditionClient).GetField("store", BindingFlags.NonPublic | BindingFlags.Instance)!;
            dataField.SetValue(client, null);
            Assert.That(client.DiscardFailedPractice(), Is.False);
            dataField.SetValue(client, content);
            StillQuarantined("a retry without loaded content");
            var kept = storeField.GetValue(client);
            storeField.SetValue(client, null);
            Assert.That(client.DiscardFailedPractice(), Is.False);
            Assert.That((client.RecoveryRequired, client.Snapshot != null, storeField.GetValue(client)), Is.EqualTo((true, true, (object?)null)), "an unavailable store stays blocked, never reset");
            Assert.That(SingleView(o.Host).GetComponentsInChildren<Text>().Any(t => t.text.Contains("기억 버전")), Is.False, "an unavailable store must not invent version zero");
            AssertReadable(SingleView(o.Host), t => t.StartsWith("저장된 기억을 확인할 수 없어", StringComparison.Ordinal), "unavailable store is unknown, not version zero");
            storeField.SetValue(client, kept);
            StillQuarantined("a retry without a store");
            LogAssert.NoUnexpectedReceived();

            // Repaired: one real click departs exactly once from the kept store with the next departure number.
            yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(o.Host, "RecoveryDiscard"));
            Assert.That(client.RecoveryRequired, Is.False);
            var expected = CoreRun.Create(ClientRunContent.Load(), MemoryForgeExpeditionClient.PracticeSetup(Seed + 2, "short", "practice-3-" + (Seed + 2), savedMemory, 1));
            Assert.That(JToken.DeepEquals(Tree(client.Snapshot!), Tree(expected.State)), Is.True, "failed retries consumed no departure number");
            Assert.That(StorePrint(client), Is.EqualTo(store), "the failed Run was never saved");
        }

        // Ordinary input rejections that Core makes before any change stay toasts: no quarantine, no error log, and play continues.
        [UnityTest]
        public IEnumerator OrdinaryUnchangedRejectionsStayToastsWithoutQuarantineOrErrorLog()
        {
            var o = new Opened(); yield return Open(o);
            var client = o.Client; var map = client.Snapshot!.Map!;
            void Refused(Func<bool> call, string code)
            {
                string before = Print(client.Snapshot!);
                Assert.That(call(), Is.False, code);
                Assert.That((client.LastError, client.RecoveryRequired), Is.EqualTo(((string?)code, false)), code);
                Assert.That(Print(client.Snapshot!), Is.EqualTo(before), code + " changed the Run");
            }
            Refused(() => client.EnterNode(map.Floors[3][0].Id), "NODE_UNREACHABLE");
            Assert.That(client.EnterNode(map.Floors[0][0].Id), Is.True, client.LastError);
            var b = client.Snapshot!.Battle!;
            Refused(() => client.PlayCard("not-in-hand", "now"), "NOT_IN_HAND");
            Refused(() => client.UseUltimate(b.Actors.First(a => a.Gauge < ClientBattleContent.Load().Battle.Rules.GaugeMax).Id), "ULT_NOT_READY");
            Refused(() => client.TakeCard("sera_round"), "BAD_REWARD");
            Refused(() => client.Proceed(), "WRONG_PHASE");
            // With several living enemies and none chosen, a card that needs one is refused before any change. The seed's first battle
            // is the witness for that setup, so it is asserted rather than skipped.
            Assert.That(b.Enemies.Count(e => e.Alive), Is.GreaterThan(1), "seed 1234's first battle has several living enemies");
            Assert.That(client.Target, Is.Null, "no enemy is chosen when several are alive");
            var mirror = CoreRun.Create(ClientRunContent.Load(), MemoryForgeExpeditionClient.PracticeSetup(Seed, "short", "practice-1-" + Seed, new Dictionary<string, object?>(), 0));
            mirror.Enter(map.Floors[0][0].Id);
            Assert.That(JToken.DeepEquals(Tree(mirror.State), Tree(client.Snapshot!)), Is.True, "the mirror is exactly the view's Run");
            string living = b.Enemies.First(e => e.Alive).Uid;
            string? targeted = b.Zones.Hand.FirstOrDefault(u => mirror.BattleNeedsTarget(u) == "one" && mirror.BattleAllowedModes(u).Contains("now") && mirror.BattleCheck(u, "now", living) == null);
            Assert.That(targeted, Is.Not.Null, "the opening hand holds a legal card that needs one target");
            Refused(() => client.PlayCard(targeted!, "now"), "BAD_TARGET");
            LogAssert.NoUnexpectedReceived();
            Assert.That(client.EndTurn(), Is.True, "play continues after ordinary rejections");
            Assert.That(client.RecoveryRequired, Is.False);
            Assert.That(client.SkipTurnSummary() && client.ContinueTurnSummary(), Is.True);
        }

        // Battle.Command rolled back an unknown engine failure, so the Run compares unchanged; it is still not an input rejection, so the
        // practice is quarantined rather than toasted, and the end leaves no turn summary.
        [UnityTest]
        public IEnumerator UnknownBattleErrorQuarantinesEvenThoughTheBattleRolledBack()
        {
            var o = new Opened(); yield return Open(o);
            var client = o.Client;
            Assert.That(client.EnterNode(client.Snapshot!.Map!.Floors[0][0].Id), Is.True, client.LastError);
            var live = client.Snapshot!.Battle!;
            // Test-local fault in the client's own content: every move of the enemies in this battle gets an unknown type.
            var moves = Content(client).Memory.Battle.Enemies.Where(e => live.Enemies.Any(x => x.Id == e.Id)).SelectMany(e => e.Pattern).Select(m => (Move: m, Type: m.Type)).ToList();
            Assert.That(moves, Is.Not.Empty);
            foreach (var entry in moves) entry.Move.Type = "test_unknown_move";
            string before = Print(client.Snapshot!), store = StorePrint(client);
            LogAssert.Expect(LogType.Error, new Regex(Diagnostic + @"BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED\)"));
            Assert.That(client.EndTurn(), Is.False);
            Assert.That(client.RecoveryRequired, Is.True, "an unknown engine failure quarantines even when nothing changed");
            Assert.That(Print(client.Snapshot!), Is.EqualTo(before), "Battle.Command is atomic: the Run is unchanged");
            Assert.That(client.HasTurnSummary, Is.False, "a failed end has no turn summary");
            AssertEverythingRefused(client, before, store, client.Snapshot!.Map!.Floors[0][0].Id);

            foreach (var entry in moves) entry.Move.Type = entry.Type; // fault removed
            Assert.That(client.DiscardFailedPractice(), Is.True, client.LastError);
            Assert.That(client.RecoveryRequired, Is.False);
            Assert.That(client.Snapshot!.RunId, Is.EqualTo("practice-2-" + (Seed + 1)), "the next departure number");
            Assert.That(client.Snapshot!.MemoryVersion, Is.EqualTo(0));
            Assert.That(StorePrint(client), Is.EqualTo(store), "nothing of the failed Run was saved");
        }

        // Failures no content can produce, through the private Do boundary by reflection (no production test hook): a non-rule exception
        // quarantines although nothing changed; an allowed input code stays a toast when nothing changed but quarantines when the Run did.
        [UnityTest]
        public IEnumerator ThePrivateDoBoundaryQuarantinesNonRuleFailuresAndChangedRunsButKeepsUnchangedInputRejections()
        {
            var o = new Opened(); yield return Open(o);
            var client = o.Client;
            var method = typeof(MemoryForgeExpeditionClient).GetMethod("Do", BindingFlags.NonPublic | BindingFlags.Instance)!;
            bool Do(Action command) => (bool)method.Invoke(client, new object?[] { command, null, true })!;
            string before = Print(client.Snapshot!), store = StorePrint(client);

            Assert.That(Do(() => throw new RuleException("NOT_IN_HAND")), Is.False);
            Assert.That((client.LastError, client.RecoveryRequired), Is.EqualTo(((string?)"NOT_IN_HAND", false)), "an unchanged input rejection is a toast");
            LogAssert.NoUnexpectedReceived();

            LogAssert.Expect(LogType.Error, new Regex(Diagnostic + @"InvalidOperationException\)"));
            Assert.That(Do(() => throw new InvalidOperationException("test fault")), Is.False);
            Assert.That(client.RecoveryRequired, Is.True, "a non-rule failure quarantines");
            Assert.That(Print(client.Snapshot!), Is.EqualTo(before));
            Assert.That(client.DiscardFailedPractice(), Is.True, client.LastError);
            Assert.That(client.Snapshot!.RunId, Is.EqualTo("practice-2-" + (Seed + 1)));

            // A real Run change followed by an allowed code: the comparison sees the change, so it is not treated as an input rejection.
            var live = (CoreRun)typeof(MemoryForgeExpeditionClient).GetField("run", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
            string first = client.Snapshot!.Map!.Floors[0][0].Id;
            LogAssert.Expect(LogType.Error, new Regex(Diagnostic + @"NOT_IN_HAND\)"));
            Assert.That(Do(() => { live.Enter(first); throw new RuleException("NOT_IN_HAND"); }), Is.False);
            Assert.That(client.RecoveryRequired, Is.True, "a changed Run is never a plain rejection");
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("BATTLE"), "the real Enter had changed the Run");
            Assert.That(client.DiscardFailedPractice(), Is.True, client.LastError);
            Assert.That(client.Snapshot!.RunId, Is.EqualTo("practice-3-" + (Seed + 2)));
            Assert.That(StorePrint(client), Is.EqualTo(store));
            yield return null;
        }

        // Legal opaque fractional metadata (a difficulty field the Run copies through) is compared exactly as a JSON tree, where the
        // canonical hash refuses it.
        [Test]
        public void FractionalOpaqueMetadataComparesExactlyWhereTheCanonicalHashRefuses()
        {
            RunState Fractional(double note)
            {
                var setup = MemoryForgeExpeditionClient.PracticeSetup(Seed, "short", "fractional", new Dictionary<string, object?>(), 0);
                setup.Difficulty = RunDifficulty.FromJsonGraph(new Dictionary<string, object?> { ["hp"] = 100, ["dmg"] = 100, ["note"] = note });
                return CoreRun.Create(ClientRunContent.Load(), setup).State;
            }
            var a = Fractional(0.1); var same = Fractional(0.1);
            var ulp = Fractional(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(0.1) + 1));
            Assert.That(((IDictionary<string, object?>)a.ToJsonGraph()["difficulty"]!)["note"], Is.EqualTo(0.1), "the Run keeps the fractional metadata");
            Assert.Catch<ArgumentException>(() => CanonicalState.Hash(a.ToJsonGraph()), "the canonical hash refuses floating point");
            var sameRun = typeof(MemoryForgeExpeditionClient).GetMethod("SameRun", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.That((bool)sameRun.Invoke(null, new object[] { a, same })!, Is.True, "equal fractional metadata compares unchanged");
            Assert.That((bool)sameRun.Invoke(null, new object[] { a, ulp })!, Is.False, "one ulp of fractional metadata is a change");
        }

        // The exact trust boundary of the failed-command comparison on small explicit graphs: object member order is free, array order
        // is not, a nested double differs by one ulp, and int/long of one value stay equal (normal primitive comparison).
        [Test]
        public void ComparisonIsOrderFreeForObjectsOrderedForArraysAndExactForNestedDoubles()
        {
            var sameGraph = typeof(MemoryForgeExpeditionClient).GetMethod("SameGraph", BindingFlags.NonPublic | BindingFlags.Static)!;
            bool Same(object? x, object? y) => (bool)sameGraph.Invoke(null, new[] { x, y })!;
            double next = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(0.2) + 1);
            Dictionary<string, object?> Graph(double inner, bool reordered, bool swapped)
            {
                var items = new List<object?> { 0.1, new Dictionary<string, object?> { ["c"] = inner, ["d"] = "x" } };
                if (swapped) items.Reverse();
                return reordered ? new Dictionary<string, object?> { ["b"] = items, ["a"] = 1 } : new Dictionary<string, object?> { ["a"] = 1, ["b"] = items };
            }
            Assert.That(Same(Graph(0.2, false, false), Graph(0.2, false, false)), Is.True, "identical graphs");
            Assert.That(Same(Graph(0.2, false, false), Graph(0.2, true, false)), Is.True, "object member order does not matter");
            Assert.That(Same(Graph(0.2, false, false), Graph(0.2, false, true)), Is.False, "array order matters");
            Assert.That(Same(Graph(0.2, false, false), Graph(next, false, false)), Is.False, "a nested double one ulp apart is a change");
            Assert.That(Same(new Dictionary<string, object?> { ["n"] = 5 }, new Dictionary<string, object?> { ["n"] = 5L }), Is.True, "int and long of one value are equal");
            Assert.That(Same(new Dictionary<string, object?> { ["n"] = 1 }, new Dictionary<string, object?> { ["n"] = 1.0 }), Is.False, "an integer and a float are a type change");
            Assert.That(Same(new Dictionary<string, object?> { ["a"] = 1 }, new Dictionary<string, object?> { ["a"] = 1, ["b"] = null }), Is.False, "an added member is a change");
            Assert.That(Same(null, null), Is.True);
        }

        // Every normal public command and selection is refused with the recovery code before it reaches the Run; reads of the failed Run
        // answer nothing, and neither the Run nor the session store changes.
        static void AssertEverythingRefused(MemoryForgeExpeditionClient client, string run, string store, string node)
        {
            var commands = new (string Name, Func<bool> Call)[]
            {
                ("EnterNode", () => client.EnterNode(node)), ("ConfirmCard", () => client.ConfirmCard()), ("PlayCard", () => client.PlayCard("c1", "now")),
                ("EndTurn", () => client.EndTurn()), ("UseUltimate", () => client.UseUltimate("sera")), ("PickCard", () => client.PickCard("c1")),
                ("AutoPick", () => client.AutoPick()), ("TakeCard", () => client.TakeCard("sera_round")), ("SkipCard", () => client.SkipCard()),
                ("TakeRelic", () => client.TakeRelic("lucky_coin")), ("TakePart", () => client.TakePart()), ("Evolve", () => client.Evolve("k1", "sera_round")),
                ("Proceed", () => client.Proceed()), ("RestHeal", () => client.RestHeal()), ("RestUpgrade", () => client.RestUpgrade("k1")),
                ("ForgeOffer", () => client.ForgeOffer("k1")), ("ForgeApply", () => client.ForgeApply("p")), ("ForgeRemove", () => client.ForgeRemove("k1")),
                ("Buy", () => client.Buy("cards", "sera_round")), ("BuyRemoval", () => client.BuyRemoval("k1")), ("ChooseEvent", () => client.ChooseEvent(0)),
                ("Archive", () => client.Archive("return")), ("ApplyFoundPart", () => client.ApplyFoundPart("p", "k1")), ("Assign", () => client.Assign("k1", "sera")),
                ("Abandon", () => client.Abandon()), ("ToggleChange", () => client.ToggleChange("x")), ("CommitMemory", () => client.CommitMemory()),
                ("CloseWithoutSave", () => client.CloseWithoutSave()), ("Depart", () => client.Depart())
            };
            foreach (var (name, call) in commands)
            {
                Assert.That(call(), Is.False, name + " must be refused in recovery");
                Assert.That(client.LastError, Is.EqualTo(Recovery), name);
                Assert.That(Print(client.Snapshot!), Is.EqualTo(run), name + " reached the failed Run");
                Assert.That(StorePrint(client), Is.EqualTo(store), name + " changed the session store");
            }
            var selections = new (string Name, Action Call)[]
            {
                ("SelectCard", () => client.SelectCard("c1")), ("SelectMode", () => client.SelectMode("now")), ("SelectEnemy", () => client.SelectEnemy("e1")),
                ("CancelPick", () => client.CancelPick()), ("StartPractice", () => client.StartPractice())
            };
            foreach (var (name, call) in selections)
            {
                call();
                Assert.That(client.LastError, Is.EqualTo(Recovery), name);
                Assert.That(new[] { client.SelectedCard, client.SelectedMode, client.Target }, Is.EqualTo(new string?[] { null, null, null }), name + " prepared an intent");
                Assert.That(Print(client.Snapshot!), Is.EqualTo(run), name + " reached the failed Run");
                Assert.That(StorePrint(client), Is.EqualTo(store), name + " changed the session store");
            }
            Assert.That(client.ShowHandCardInfo("c1") || client.ShowCardInfo("sera_round") || client.ShowDeckCardInfo("k1"), Is.False, "no card detail of the failed Run");
            Assert.That((client.PlayForecast("c1", "now"), client.MemoryChanges(), client.PreviewSelection()), Is.EqualTo(((BattlePlayForecast?)null, (Dictionary<string, object?>?)null, (Dictionary<string, object?>?)null)));
            Assert.That(client.SkipTurnSummary() || client.ContinueTurnSummary(), Is.False);
            Assert.That((client.RecoveryRequired, client.PickerOpen, client.CardInfoOpen, client.HasTurnSummary, client.ShowingResult), Is.EqualTo((true, false, false, false, false)));
        }

        // ───────── helpers ─────────
        static RunData Content(MemoryForgeExpeditionClient client) =>
            (RunData)typeof(MemoryForgeExpeditionClient).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
        // JSON trees, not CanonicalState: a Run graph may legally carry fractional metadata.
        static JToken Tree(RunState state) => JToken.FromObject(state.ToJsonGraph());
        static string Print(RunState state) => Tree(state).ToString(Formatting.None);
        // The complete private session store graph (schema, memory, version, history, receipts, …) as JSON text, read-only.
        static string StorePrint(MemoryForgeExpeditionClient client) => JToken.FromObject(Store(client)).ToString(Formatting.None);
        static IDictionary<string, object?> Store(MemoryForgeExpeditionClient client) =>
            (IDictionary<string, object?>)typeof(MemoryForgeExpeditionClient).GetField("store", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
        static Button ButtonNamed(GameObject host, string name) => SingleView(host).GetComponentsInChildren<Button>().Single(x => x.name == name);
        // A real pointer click at the button's own center; the button must be the top raycast hit there.
        static void Click(Button button)
        {
            var rect = button.GetComponent<RectTransform>();
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)), button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), button.name + " must be the top hit at its own center");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        static GameObject SingleView(GameObject host)
        {
            var active = new List<GameObject>();
            foreach (Transform child in host.transform) if (child.name == MemoryForgeExpeditionClient.OwnedCanvas && child.gameObject.activeInHierarchy) active.Add(child.gameObject);
            Assert.That(active.Count, Is.EqualTo(1), "expected exactly one active owned expedition view");
            return active[0];
        }
        // Labels must generate glyphs and must not be truncated.
        static void AssertReadable(GameObject view, Func<string, bool> match, string what)
        {
            var labels = view.GetComponentsInChildren<Text>(false).Where(t => match(t.text)).ToList();
            Assert.That(labels.Count, Is.GreaterThanOrEqualTo(1), what + " label is missing");
            foreach (var label in labels)
            {
                Assert.That(label.isActiveAndEnabled && label.font != null && label.color.a > 0f, Is.True, what + " label is not drawable");
                var generator = label.cachedTextGenerator;
                int glyphs = label.text.Count(ch => !char.IsWhiteSpace(ch));
                Assert.That(generator.vertexCount, Is.GreaterThan(0), what + " '" + label.text + "' generated no vertices");
                Assert.That(generator.characterCountVisible, Is.GreaterThanOrEqualTo(glyphs), what + " '" + label.text + "' is truncated");
            }
        }
    }
}