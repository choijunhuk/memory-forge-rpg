#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    // The hand-off is earned through a real defeat/save/return. Faults affect only the private content-loader boundary or the fresh
    // content it returns; session stores and Runs are never injected. Scene requests are observed, not actually loaded.
    public class ExpeditionResumeFailurePlayModeTests
    {
        const long Seed = 1234;
        const string FailedDeparture = @"^\[MemoryForge\] Practice departure could not start \(";
        readonly List<GameObject> hosts = new List<GameObject>();

        [SetUp]
        public void Reset() { MemoryForgePracticeSession.ResetForTests(); MemoryForgeLobbyClient.NextLength = null; }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            foreach (var host in hosts) if (host != null) Object.Destroy(host);
            hosts.Clear();
            MemoryForgePracticeSession.ResetForTests(); MemoryForgeLobbyClient.NextLength = null;
            yield return null;
        }

        MemoryForgeExpeditionClient NewClient(out GameObject host)
        {
            host = new GameObject("ExpeditionResumeFailureTestHost"); hosts.Add(host); host.SetActive(false);
            var client = host.AddComponent<MemoryForgeExpeditionClient>(); client.Seed = Seed; client.Length = "short";
            client.SceneLoader = _ => { };
            return client;
        }

        static FieldInfo Field(string name) => typeof(MemoryForgeExpeditionClient).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        static object? Read(MemoryForgeExpeditionClient client, string name) => Field(name).GetValue(client);
        static string Print(object? value) => value == null ? "null" : JToken.FromObject(value).ToString(Formatting.None);
        static string HeldPrint() => Print(typeof(MemoryForgePracticeSession).GetField("held", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        static string StorePrint(MemoryForgeExpeditionClient client) => Print(Read(client, "store"));
        static void Loader(MemoryForgeExpeditionClient client, Func<RunData> load) => Field("contentLoader").SetValue(client, load);

        IEnumerator EarnAndReturn(MemoryForgeExpeditionClient client, GameObject host)
        {
            host.SetActive(true); yield return null;
            Assert.That(client.EnterNode(client.Snapshot!.Map!.Floors[0][0].Id), Is.True, client.LastError);
            yield return Defeat(client);
            Assert.That(client.CommitMemory(), Is.True, client.LastError);
            Assert.That(client.SessionMemoryVersion, Is.EqualTo(1));
            var store = (IDictionary<string, object?>)Read(client, "store")!;
            Assert.That(((IList)store["history"]!).Count, Is.EqualTo(1));
            Assert.That(((IDictionary<string, object?>)store["receipts"]!).ContainsKey(client.LastRequestId!), Is.True);
            Assert.That(client.ReturnToWorkshop(), Is.True, client.LastError);
            host.SetActive(false); yield return null;
        }

        static IEnumerator Defeat(MemoryForgeExpeditionClient client)
        {
            for (int end = 0; end < 600 && client.Snapshot!.Phase == "BATTLE"; end++)
            {
                Assert.That(client.EndTurn(), Is.True, client.LastError);
                Assert.That(client.SkipTurnSummary() && client.ContinueTurnSummary(), Is.True);
                if (end % 8 == 7) yield return null;
            }
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("DEFEAT"), "the authored seeded battle ends through actual turn commands");
        }

        [UnityTest]
        public IEnumerator ThrowingContentLoadRetainsTheExplicitDepartureAcrossRetriesAndReenable()
        { yield return FailedResume("load"); }

        [UnityTest]
        public IEnumerator RejectedRunCreateRetainsTheExplicitDepartureAcrossRetriesAndReenable()
        { yield return FailedResume("rule"); }

        [UnityTest]
        public IEnumerator ThrowingRunCreateRetainsTheExplicitDepartureAcrossRetriesAndReenable()
        { yield return FailedResume("create"); }

        IEnumerator FailedResume(string fault)
        {
            var source = NewClient(out var oldHost); yield return EarnAndReturn(source, oldHost);
            string held = HeldPrint(), store = StorePrint(source); var memory = source.SessionMemory;
            MemoryForgePracticeSession.ConsumeReturnNotice();
            MemoryForgeLobbyClient.NextLength = "full";
            var client = NewClient(out var host);
            bool broken = true; int loads = 0;
            Loader(client, () =>
            {
                loads++;
                if (broken && fault == "load") throw new InvalidDataException("test-local run content load failure");
                var content = ClientRunContent.Load();
                if (broken && fault == "rule") content.Encounters.Clear(); // Run.Create refuses ZONE_NOT_AUTHORED
                if (broken && fault == "create") content.Stats = null!; // Load succeeds; real Run.Create cannot derive party stats
                return content;
            });
            LogAssert.Expect(LogType.Error, new Regex(FailedDeparture));
            host.SetActive(true); yield return null; Canvas.ForceUpdateCanvases();

            void Kept(string step)
            {
                Assert.That(client.RecoveryRequired, Is.True, step + ": a visible recovery state stays in force");
                Assert.That(client.Snapshot, Is.Null, step + ": no fresh baseline or failed Run is installed");
                Assert.That(HeldPrint(), Is.EqualTo(held), step + ": held store, last save, counters and original length are exact");
                Assert.That(StorePrint(client), Is.EqualTo(store), step + ": the client retains the trusted store even if loading failed");
                Assert.That((Read(client, "departures"), Read(client, "requestSerial")), Is.EqualTo(((object)1, (object)1)), step + ": failed attempts consume no numbers");
                Assert.That((client.Length, MemoryForgeLobbyClient.NextLength), Is.EqualTo(("full", (string?)"full")), step + ": the explicit departure choice remains");
                var view = View(host);
                Assert.That(view.GetComponentsInChildren<Button>().Select(b => b.name), Is.EquivalentTo(new[] { "RecoveryDiscard", "RecoveryHome" }));
                var text = view.GetComponentsInChildren<Text>().Select(t => t.text).ToArray();
                Assert.That(text.Any(t => t.Contains("출발하지 못")), Is.True, "the failed departure is explained");
                Assert.That(text.Any(t => t.Contains("기억 버전 1")), Is.True, "the trusted memory version is visible");
                Assert.That(text.Any(t => t.Contains("다시 출발")), Is.True, "retry is explicit");
            }
            Kept("initial attempt");
            int attempted = loads;
            host.SetActive(false); yield return null; host.SetActive(true); yield return null;
            host.SendMessage("OnApplicationPause", false); host.SendMessage("OnApplicationFocus", true);
            Kept("reenable and focus");
            Assert.That(loads, Is.EqualTo(attempted), "lifecycle redraw never retries or starts a fresh session");
            LogAssert.Expect(LogType.Error, new Regex(FailedDeparture));
            Click(ButtonNamed(host, "RecoveryDiscard"));
            yield return null; Canvas.ForceUpdateCanvases();
            Kept("retry");
            Assert.That(loads, Is.EqualTo(attempted + 1));
            Assert.That(client.Depart(), Is.False, "ordinary departure cannot bypass the pending recovery");
            client.StartPractice();
            Kept("direct fresh-start request");

            broken = false;
            Click(ButtonNamed(host, "RecoveryDiscard"));
            yield return null;
            Assert.That(client.RecoveryRequired, Is.False);
            var expected = CoreRun.Create(ClientRunContent.Load(), MemoryForgeExpeditionClient.PracticeSetup(Seed + 1, "full", "practice-2-" + (Seed + 1), memory, 1));
            Assert.That(JToken.DeepEquals(JToken.FromObject(client.Snapshot!.ToJsonGraph()), JToken.FromObject(expected.State.ToJsonGraph())), Is.True,
                "the repaired explicit departure creates exactly the next authored Run");
            Assert.That(StorePrint(client), Is.EqualTo(store));
            Assert.That((MemoryForgePracticeSession.HasHeldPractice, MemoryForgeLobbyClient.NextLength), Is.EqualTo((false, (string?)null)), "only successful departure consumes the hand-off");
            Assert.That(client.EnterNode(client.Snapshot!.Map!.Floors[0][0].Id), Is.True, client.LastError);
            yield return Defeat(client);
            Assert.That(client.CommitMemory(), Is.True, client.LastError);
            Assert.That(client.LastRequestId, Is.EqualTo("practice-save-practice-2-" + (Seed + 1) + "-2"), "failed attempts consumed no save serial");
            Assert.That(client.SessionMemoryVersion, Is.EqualTo(2));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator FailedDepartureCanReturnHomeWithoutARunAndSceneFailureKeepsItsRecovery()
        {
            var source = NewClient(out var oldHost); yield return EarnAndReturn(source, oldHost);
            string held = HeldPrint(), store = StorePrint(source);
            MemoryForgeLobbyClient.NextLength = "full";
            var client = NewClient(out var host);
            Loader(client, () => throw new InvalidDataException("test-local run content load failure"));
            LogAssert.Expect(LogType.Error, new Regex(FailedDeparture));
            host.SetActive(true); yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(client.Snapshot, Is.Null);
            client.SceneLoader = _ => throw new InvalidOperationException("test-local home scene failure");
            LogAssert.Expect(LogType.Error, new Regex(@"^\[MemoryForge\] The practice did not return to the home \("));
            Assert.That(client.ReturnToWorkshop(), Is.False);
            Assert.That((client.ReturnRequested, client.RecoveryRequired), Is.EqualTo((false, true)));
            Assert.That(MemoryForgeLobbyClient.NextLength, Is.EqualTo("full"), "a failed home load keeps the explicit departure pending");
            Assert.That(HeldPrint(), Is.EqualTo(held), "a failed home request restores the held hand-off exactly");
            Assert.That(StorePrint(client), Is.EqualTo(store));
            Assert.That(View(host).GetComponentsInChildren<Button>().Select(b => b.name), Does.Contain("RecoveryHome"));
            var opened = new List<string>(); client.SceneLoader = opened.Add;
            yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(host, "RecoveryHome"));
            Assert.That(opened, Is.EqualTo(new[] { MemoryForgeExpeditionClient.LobbyScene }));
            Assert.That(client.ReturnRequested, Is.True);
            Assert.That(client.Snapshot, Is.Null, "returning reads no failed Run");
            Assert.That(MemoryForgeLobbyClient.NextLength, Is.Null, "returning home cancels the queued departure without dropping held memory");
            Assert.That(StorePrint(client), Is.EqualTo(store));
            Assert.That(MemoryForgePracticeSession.HasHeldPractice, Is.True);
            Assert.That(MemoryForgePracticeSession.HeldMemoryVersion, Is.EqualTo(1));
            // An old component still exists in this observed-scene test: its references cannot alter the held copy.
            var sourceStore = (IDictionary<string, object?>)Read(source, "store")!;
            sourceStore["version"] = 999L;
            Assert.That(MemoryForgePracticeSession.HeldMemoryVersion, Is.EqualTo(1));
            Assert.That(client.ReturnToWorkshop(), Is.False);
            Assert.That(opened.Count, Is.EqualTo(1));
        }

        static GameObject View(GameObject host) => host.GetComponentsInChildren<Canvas>().Single(c => c.name == MemoryForgeExpeditionClient.OwnedCanvas).gameObject;
        static Button ButtonNamed(GameObject host, string name) => View(host).GetComponentsInChildren<Button>().Single(b => b.name == name);
        static void Click(Button button)
        {
            var face = button.targetGraphic.rectTransform;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, face.TransformPoint(face.rect.center)), button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), button.name + " is the top hit at its face");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
    }
}
