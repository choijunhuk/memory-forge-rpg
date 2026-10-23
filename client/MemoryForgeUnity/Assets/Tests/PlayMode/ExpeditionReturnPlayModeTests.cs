// EXPEDITION-RETURN-UX-20261021: Play Mode checks of the expedition's way back to the home (공방) over the real shared-core Run and the
// exported content. Every step is a public command or a real EventSystem click at a control's own centre. The return's scene request
// is observed through the client's SceneLoader (the home itself is not loaded here; its scene name must be in the build), and the
// home's departure is MemoryForgeLobbyClient.NextLength exactly as its 출발 button sets it before opening the expedition scene. The
// private session store is only read (by reflection) to compare the whole graph; faults are test-local content changes reached
// through real commands, as in ExpeditionFailurePlayModeTests. One test opens the shipped expedition scene additively. This is not
// Android/device, home-screen UI, server, account or reconnect acceptance.
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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using CoreRun = MemoryForge.Core.Run;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public class ExpeditionReturnPlayModeTests
    {
        const long Seed = 1234;
        const string Home = MemoryForgeExpeditionClient.LobbyScene;
        const string Diagnostic = @"^\[MemoryForge\] Practice expedition quarantined after a failed local Run calculation \(";
        readonly List<GameObject> hosts = new List<GameObject>();
        readonly List<string> scenes = new List<string>();

        // The hand-off is app-session static state: every test starts and ends without a held practice, notice or home departure.
        [SetUp]
        public void StartClean()
        {
            MemoryForgePracticeSession.ResetForTests(); MemoryForgeLobbyClient.NextLength = null; MemoryForgeExpeditionClient.TouchOverride = null;
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            foreach (var host in hosts) if (host != null) Object.Destroy(host);
            hosts.Clear();
            foreach (string name in scenes) { var scene = SceneManager.GetSceneByName(name); if (scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene); }
            scenes.Clear();
            MemoryForgePracticeSession.ResetForTests(); MemoryForgeLobbyClient.NextLength = null; MemoryForgeExpeditionClient.TouchOverride = null;
            yield return null;
        }

        sealed class Opened
        {
            public GameObject Host = null!;
            public MemoryForgeExpeditionClient Client = null!;
            public readonly List<string> Loads = new List<string>(); // every scene the client asked to open
        }

        // As the other expedition tests: the seed on a disabled host before the component's real OnEnable start. The scene request is
        // recorded instead of loaded; nothing else is injected.
        IEnumerator Open(Opened result, long seed = Seed)
        {
            var host = new GameObject("ExpeditionReturnTestHost"); hosts.Add(host);
            host.SetActive(false);
            var client = host.AddComponent<MemoryForgeExpeditionClient>(); client.Seed = seed; client.Length = "short";
            client.SceneLoader = result.Loads.Add;
            host.SetActive(true);
            yield return null;
            result.Host = host; result.Client = client;
        }

        // 계속 탐사 changes nothing; the confirmed 탐사 포기 abandons, writes nothing to the store, opens the home exactly once and departs
        // nothing; a repeated press does nothing more. The home's departure then creates one new Run from the untouched empty baseline,
        // numbered after the abandoned one, at the length the home chose.
        [UnityTest]
        public IEnumerator CancelChangesNothingAndTheConfirmedAbandonReturnsHomeOnceWithTheUntouchedStore()
        {
            var a = new Opened(); yield return Open(a);
            var client = a.Client;
            string running = Print(client.Snapshot!), store = StorePrint(client);
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("MAP"));
            Assert.That(client.ReturnToWorkshop(), Is.False, "a running expedition goes home only through 탐사 포기");
            Assert.That(client.LastError, Is.EqualTo("RUN_NOT_ENDED"));
            Assert.That((a.Loads.Count, MemoryForgePracticeSession.HasHeldPractice, client.ReturnRequested), Is.EqualTo((0, false, false)));

            // The sheet says what the confirm does; 계속 탐사 only closes it.
            yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(a.Host, "AbandonButton"));
            yield return null; Canvas.ForceUpdateCanvases();
            var sheet = View(a.Host);
            AssertReadable(sheet, t => t == "탐사를 포기하고 공방으로 돌아갈까요?", "the question names the return");
            AssertReadable(sheet, t => t.Contains("기억에 남기지 않고 버려요"), "what is dropped");
            AssertReadable(sheet, t => t.StartsWith("기억 덱은 출발 전 그대로예요.", StringComparison.Ordinal) && t.Contains("공방에서 다시 출격"), "what stays and where it goes on");
            AssertReadable(sheet, t => t == "포기하고 공방으로", "the confirm's own words");
            Click(ButtonNamed(a.Host, "CancelAbandon"));
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(View(a.Host).GetComponentsInChildren<RectTransform>().Any(r => r.name == "AbandonSheet"), Is.False, "the sheet closed");
            Assert.That(Print(client.Snapshot!), Is.EqualTo(running), "계속 탐사 changes no Run state");
            Assert.That(StorePrint(client), Is.EqualTo(store));
            Assert.That((client.ShowingResult, client.ReturnRequested, a.Loads.Count, MemoryForgePracticeSession.HasHeldPractice), Is.EqualTo((false, false, 0, false)));

            Click(ButtonNamed(a.Host, "AbandonButton"));
            yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(a.Host, "ConfirmAbandon"));
            Assert.That(a.Loads, Is.EqualTo(new[] { Home }), "the confirm opens the home once");
            Assert.That(client.ReturnRequested, Is.True);
            Assert.That(client.ShowingResult, Is.True, "the expedition was abandoned through the compatible Abandon");
            Assert.That(client.Snapshot!.RunId, Is.EqualTo("practice-1-" + Seed), "ending departs nothing");
            Assert.That(StorePrint(client), Is.EqualTo(store), "abandoning wrote nothing to the session store");
            Assert.That((MemoryForgePracticeSession.HasHeldPractice, MemoryForgePracticeSession.HeldMemoryVersion, MemoryForgePracticeSession.HeldLength),
                Is.EqualTo((true, (long?)0, (string?)"short")));
            Assert.That(MemoryForgePracticeSession.ConsumeReturnNotice(), Is.EqualTo("탐사 포기 · 기억 덱은 바뀌지 않았어요."));
            Assert.That(MemoryForgePracticeSession.ConsumeReturnNotice(), Is.Null, "the home reads the notice once");

            // A repeated press does nothing more: the returning veil takes the tap, and the command itself is refused.
            yield return null; Canvas.ForceUpdateCanvases();
            var home = ButtonNamed(a.Host, "ReturnHome");
            Assert.That(TopButton(Center(home)), Is.Null, "the returning veil covers the result's controls");
            home.onClick.Invoke();
            Assert.That(client.ReturnToWorkshop(), Is.False);
            Assert.That(client.LastError, Is.EqualTo("RETURNING_HOME"));
            Assert.That(client.Depart(), Is.False, "a returning practice departs nothing");
            Assert.That(client.LastError, Is.EqualTo("RETURNING_HOME"));
            Assert.That(a.Loads.Count, Is.EqualTo(1), "still one request for the home");
            Assert.That(client.Snapshot!.RunId, Is.EqualTo("practice-1-" + Seed));
            Assert.That(MemoryForgePracticeSession.HasHeldPractice, Is.True);

            // The home's 출발 (its length, then the expedition scene's component): one new Run from the handed-over empty store.
            Object.Destroy(a.Host); yield return null;
            MemoryForgeLobbyClient.NextLength = "full";
            var b = new Opened(); yield return Open(b);
            var expected = CoreRun.Create(ClientRunContent.Load(), MemoryForgeExpeditionClient.PracticeSetup(Seed + 1, "full", "practice-2-" + (Seed + 1), new Dictionary<string, object?>(), 0));
            Assert.That(JToken.DeepEquals(Tree(b.Client.Snapshot!), Tree(expected.State)), Is.True,
                "the departure is exactly Create from the unchanged empty store, numbered after the abandoned one, at the home's length");
            Assert.That(b.Client.Length, Is.EqualTo("full"));
            Assert.That(StorePrint(b.Client), Is.EqualTo(store), "the whole store graph came back unchanged");
            Assert.That((MemoryForgePracticeSession.HasHeldPractice, MemoryForgeLobbyClient.NextLength, b.Loads.Count), Is.EqualTo((false, (string?)null, 0)),
                "the departure took the practice and asked for no scene");
        }

        // A real save (changes kept within capacity, version 0 → 1, one history entry and its receipt) survives the destroy/recreate
        // through the hand-off exactly: the store graph, the departure numbering and the request numbering. A client started without the
        // home's departure meanwhile stays an independent fresh practice and leaves the returned one waiting.
        [UnityTest, Timeout(600000)] // a whole short expedition, then a lost battle
        public IEnumerator SavedMemoryHistoryReceiptsAndNumberingSurviveTheHandoffToTheNextExplicitDeparture()
        {
            var a = new Opened(); yield return Open(a);
            var client = a.Client;
            // The report driver, preferring ordinary battles, up to the save screen; the save itself is chosen here, not by the driver.
            var driver = new ExpeditionUiCapturePlayModeTests.Driver();
            var seen = new HashSet<string> { "battle_five", "shop", "event", "forge", "rest" };
            for (int step = 0; step < 8000 && !Saving(client); step++)
            {
                driver.Step(client, seen);
                if (step % 8 == 7) yield return null;
            }
            Assert.That(Saving(client), Is.True, "seed 1234's practice reaches its memory save through the report driver");
            var s = client.Snapshot!;

            // The save screen is not an end: no way home there, and asking changes nothing.
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(View(a.Host).GetComponentsInChildren<Button>().Any(x => x.name == "HomeButton" || x.name == "ReturnHome" || x.name == "AbandonButton"), Is.False);
            string saving = Print(s);
            Assert.That(client.ReturnToWorkshop(), Is.False);
            Assert.That(client.LastError, Is.EqualTo("RUN_NOT_ENDED"));
            Assert.That((Print(client.Snapshot!), a.Loads.Count, MemoryForgePracticeSession.HasHeldPractice), Is.EqualTo((saving, 0, false)));

            // Keep, in change-list order, each change whose preview stays valid (the original "fill within capacity" order), and save.
            var info = client.MemoryChanges()!;
            var order = s.Party.SelectMany(p => ListOf(Get(Get(info, p.CharId), "changes")).Select(c => (string)Get(c, "id")!)).ToList();
            foreach (string id in order) { client.ToggleChange(id); if (Get(client.PreviewSelection(), "error") != null) client.ToggleChange(id); }
            var chosen = order.Where(client.SelectedChangeIds.Contains).ToList();
            Assert.That(chosen, Is.Not.Empty, "seed 1234's save offers a change the preview accepts");
            Assert.That(client.CommitMemory(), Is.True, client.LastError);
            string request = client.LastRequestId!;
            Assert.That(request, Is.EqualTo("practice-save-practice-1-" + Seed + "-1"));
            Assert.That(client.SessionMemoryVersion, Is.EqualTo(1));
            var saved = client.SessionMemory;
            Assert.That(saved, Is.Not.Empty, "the save kept real cards");
            var done = client.Snapshot!; string store = StorePrint(client);
            Assert.That(((IList)Store(client)["history"]!).Count, Is.EqualTo(1));
            Assert.That(((IDictionary<string, object?>)Store(client)["receipts"]!).ContainsKey(request), Is.True);

            // The result: the way home first, the next departure as the optional second choice, and each saved deck as a card sized to
            // its rows that lists every saved card.
            yield return null; Canvas.ForceUpdateCanvases();
            var view = View(a.Host);
            Assert.That(view.GetComponentsInChildren<Button>().Select(x => x.name), Is.SupersetOf(new[] { "ReturnHome", "HomeButton", "Depart", "LengthShort", "LengthFull" }));
            AssertReadable(view, t => t == "공방으로 돌아가기", "the primary return");
            AssertReadable(view, t => t.StartsWith("변화 " + chosen.Count + "개 저장", StringComparison.Ordinal) && t.Contains("용량"), "the saved result");
            var baseline = ClientRunContent.Load().Memory;
            foreach (string cid in MemoryForgeExpeditionClient.ReferenceParty)
                AssertDeckCard(view, cid, saved.TryGetValue(cid, out var rows) && rows is IList list ? list.Count : MemoryV2.Baseline(baseline, cid).Count());

            Click(ButtonNamed(a.Host, "ReturnHome"));
            Assert.That(a.Loads, Is.EqualTo(new[] { Home }));
            Assert.That(MemoryForgePracticeSession.ConsumeReturnNotice(), Is.EqualTo(Title(done) + " · 변화 " + chosen.Count + "개를 기억에 저장했어요 (기억 버전 1)."));
            Assert.That(client.Snapshot!.RunId, Is.EqualTo("practice-1-" + Seed), "ending departs nothing");
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("DONE"));
            Assert.That(MemoryForgePracticeSession.HeldMemoryVersion, Is.EqualTo(1));

            // An independent client started without the home's departure is a fresh practice and leaves the returned one waiting.
            var c = new Opened(); yield return Open(c);
            Assert.That((c.Client.Snapshot!.RunId, c.Client.SessionMemoryVersion, c.Client.SessionMemory.Count), Is.EqualTo(("practice-1-" + Seed, 0L, 0)));
            c.Client.StartPractice();
            Assert.That((c.Client.Snapshot!.RunId, c.Client.SessionMemoryVersion), Is.EqualTo(("practice-1-" + Seed, 0L)), "StartPractice stays fresh");
            Assert.That(MemoryForgePracticeSession.HasHeldPractice, Is.True, "the returned practice still waits for the home's departure");
            Object.Destroy(c.Host); Object.Destroy(a.Host); yield return null;

            // The home's departure: exactly Create from the saved memory and version, numbered after the first, with the whole store.
            MemoryForgeLobbyClient.NextLength = "short";
            var b = new Opened(); yield return Open(b);
            var bc = b.Client;
            var expected = CoreRun.Create(ClientRunContent.Load(), MemoryForgeExpeditionClient.PracticeSetup(Seed + 1, "short", "practice-2-" + (Seed + 1), saved, 1));
            Assert.That(JToken.DeepEquals(Tree(bc.Snapshot!), Tree(expected.State)), Is.True, "the departure is exactly Create from the saved memory and version");
            var startMemory = bc.Snapshot!.MemoryBefore!.ToJsonGraph();
            foreach (var party in bc.Snapshot!.Party)
                Assert.That(CanonicalState.Hash(startMemory[party.CharId]), Is.EqualTo(CanonicalState.Hash(Get(saved, party.CharId))), party.CharId + " starts from the saved deck");
            Assert.That(StorePrint(bc), Is.EqualTo(store), "memory, version, history and receipts came back exactly");
            Assert.That(MemoryForgePracticeSession.HasHeldPractice, Is.False);

            // The request numbering continues too: the next save of the new expedition is the session's second request and version.
            Assert.That(bc.EnterNode(bc.Snapshot!.Map!.Floors[0][0].Id), Is.True, bc.LastError);
            yield return EndTurnsUntilDefeat(bc, 600, "seed " + (Seed + 1) + "'s first battle");
            Assert.That(bc.CommitMemory(), Is.True, bc.LastError);
            Assert.That(bc.LastRequestId, Is.EqualTo("practice-save-practice-2-" + (Seed + 1) + "-2"), "the request serial carried over");
            Assert.That(bc.SessionMemoryVersion, Is.EqualTo(2), "the commit's expected version 1 was the handed-over store's");
            Assert.That(((IDictionary<string, object?>)Store(bc)["receipts"]!).Keys, Is.SupersetOf(new[] { request, bc.LastRequestId! }), "both receipts are in the store");
            Assert.That(((IList)Store(bc)["history"]!).Count, Is.EqualTo(2));
        }

        // From the recovery screen the return hands over only the last valid store: the failed Run is neither read, changed nor saved, the
        // quarantine stays in that component, and without a store nothing is handed over (the recovery stays explicit). The home's
        // departure then starts exactly where DiscardFailedPractice would have: the kept store and the next departure number.
        [UnityTest]
        public IEnumerator RecoveryReturnHandsOverOnlyTheTrustedStoreAndTheHomesDepartureStartsFromIt()
        {
            var a = new Opened(); yield return Open(a);
            var client = a.Client;
            Assert.That(client.EnterNode(client.Snapshot!.Map!.Floors[0][0].Id), Is.True, client.LastError);
            yield return EndTurnsUntilDefeat(client, 200, "seed 1234's first battle");
            Assert.That(client.CommitMemory(), Is.True, client.LastError);
            Assert.That(client.Depart(), Is.True, client.LastError);
            var start = client.Snapshot!;
            Assert.That((start.RunId, start.MemoryVersion), Is.EqualTo(("practice-2-" + (Seed + 1), (long?)1)));
            string store = StorePrint(client); var savedMemory = client.SessionMemory;

            // A genuine quarantine through the real partial Enter (test-local content fault, removed at once).
            var encounter = Content(client).Encounters.Single(e => e.Zone == start.Zone!.Value.ToString(CultureInfo.InvariantCulture));
            var original = encounter.Normal;
            encounter.Normal = new List<List<string>> { new List<string> { "test_missing_enemy" } };
            LogAssert.Expect(LogType.Error, new Regex(Diagnostic + @"UNKNOWN_ENEMY\)"));
            try { Assert.That(client.EnterNode(start.Map!.Floors[0][0].Id), Is.False); }
            finally { encounter.Normal = original; }
            Assert.That(client.RecoveryRequired, Is.True);
            string failed = Print(client.Snapshot!);

            // Without a store there is nothing trusted to hand over: refused, quarantine kept, nothing held or opened.
            var storeField = typeof(MemoryForgeExpeditionClient).GetField("store", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var kept = storeField.GetValue(client);
            storeField.SetValue(client, null);
            Assert.That(client.ReturnToWorkshop(), Is.False);
            Assert.That((client.RecoveryRequired, client.ReturnRequested, a.Loads.Count, MemoryForgePracticeSession.HasHeldPractice), Is.EqualTo((true, false, 0, false)));
            storeField.SetValue(client, kept);

            Assert.That(client.ReturnToWorkshop(), Is.True, client.LastError);
            Assert.That(a.Loads, Is.EqualTo(new[] { Home }));
            Assert.That(client.RecoveryRequired, Is.True, "the quarantine is not lifted in this component");
            Assert.That(Print(client.Snapshot!), Is.EqualTo(failed), "the failed Run is neither changed nor saved");
            Assert.That(StorePrint(client), Is.EqualTo(store));
            Assert.That(MemoryForgePracticeSession.HeldMemoryVersion, Is.EqualTo(1));
            Assert.That(MemoryForgePracticeSession.ConsumeReturnNotice(), Is.EqualTo("멈춘 연습 탐사는 저장하지 않고 버렸어요. 저장한 기억 덱(기억 버전 1)은 그대로예요."));
            Assert.That(client.DiscardFailedPractice(), Is.False, "the store already went home");
            Assert.That(client.LastError, Is.EqualTo("RETURNING_HOME"));
            Assert.That(client.ReturnToWorkshop(), Is.False);
            Assert.That(a.Loads.Count, Is.EqualTo(1));
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(View(a.Host).GetComponentsInChildren<Button>().Select(x => x.name), Is.EqualTo(new[] { "RecoveryDiscard", "RecoveryHome" }), "the recovery screen keeps its explicit recovery and home actions");
            Assert.That(TopButton(Center(ButtonNamed(a.Host, "RecoveryDiscard"))), Is.Null, "under the returning veil");
            LogAssert.NoUnexpectedReceived();

            Object.Destroy(a.Host); yield return null;
            MemoryForgeLobbyClient.NextLength = "short";
            var b = new Opened(); yield return Open(b);
            var expected = CoreRun.Create(ClientRunContent.Load(), MemoryForgeExpeditionClient.PracticeSetup(Seed + 2, "short", "practice-3-" + (Seed + 2), savedMemory, 1));
            Assert.That(JToken.DeepEquals(Tree(b.Client.Snapshot!), Tree(expected.State)), Is.True, "exactly Create from the kept store with the next departure number");
            Assert.That(StorePrint(b.Client), Is.EqualTo(store), "nothing of the failed Run was saved");
            Assert.That(b.Client.RecoveryRequired, Is.False);
        }

        // At 48dp = 144 frame units the abandon sheet's two choices and every control of the abandoned result are at least that size, inside
        // the 1600×900 frame, apart from each other and the top hit on their own faces; the result's panel ends above the exits' touch
        // row. An expedition abandoned in battle ends with the normal bar. The public Abandon alone shows the result and opens nothing.
        [UnityTest]
        public IEnumerator AtFortyEightDpTheAbandonSheetAndTheResultKeepEveryControlWholeInsideTheFrame()
        {
            const float touch = 144;
            MemoryForgeExpeditionClient.TouchOverride = touch;
            var a = new Opened(); yield return Open(a);
            var client = a.Client;
            Assert.That(client.EnterNode(client.Snapshot!.Map!.Floors[0][0].Id), Is.True, client.LastError);
            yield return null; Canvas.ForceUpdateCanvases();
            ButtonNamed(a.Host, "AbandonButton").onClick.Invoke();
            yield return null; Canvas.ForceUpdateCanvases();
            AssertControls(a.Host, new[] { "ConfirmAbandon", "CancelAbandon" }, touch, "abandon sheet");
            ButtonNamed(a.Host, "CancelAbandon").onClick.Invoke();
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("BATTLE"));

            Assert.That(client.Abandon(), Is.True, client.LastError);
            yield return null; Canvas.ForceUpdateCanvases();
            var view = View(a.Host);
            Assert.That(view.GetComponentsInChildren<RectTransform>().Any(r => r.name == "PartyStatus"), Is.False, "an expedition abandoned in battle ends with the normal bar");
            AssertControls(a.Host, new[] { "DeckButton", "LogButton", "HomeButton", "LengthShort", "LengthFull", "Depart", "ReturnHome" }, touch, "abandoned result");
            var frame = (RectTransform)view.transform.Find("ExpeditionFrame");
            var panel = (RectTransform)frame.Find("Result");
            Assert.That(FrameRect(panel, frame).yMin, Is.GreaterThanOrEqualTo(frame.rect.yMax - (900 - touch) - 0.5f), "the result ends above the exits' touch row");
            var title = view.GetComponentsInChildren<Text>().Single(t => t.text == "탐사 포기");
            Assert.That(title.color, Is.Not.EqualTo(Hex("ff6f6f")), "an abandon is a calm title, not the danger red");
            AssertReadable(view, t => t == "이번 탐사의 변화는 남기지 않았어요 · 기억 덱 변화 없음", "the abandoned memory result");
            Assert.That((a.Loads.Count, MemoryForgePracticeSession.HasHeldPractice, client.ReturnRequested), Is.EqualTo((0, false, false)), "the public Abandon alone opens nothing");
            Assert.That(client.Snapshot!.RunId, Is.EqualTo("practice-1-" + Seed), "and departs nothing");
        }

        // The shipped expedition scene, opened as the home's 출발 opens it (its length first), departs from the returned practice: the
        // earned save and the next departure number. The home scene the return asks for is in the build.
        [UnityTest]
        public IEnumerator TheShippedExpeditionSceneDepartsFromTheReturnedPracticeOnTheHomesDeparture()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Home), Is.True, "the home the return opens is in the build settings");
            var a = new Opened(); yield return Open(a);
            var client = a.Client;
            Assert.That(client.EnterNode(client.Snapshot!.Map!.Floors[0][0].Id), Is.True, client.LastError);
            yield return EndTurnsUntilDefeat(client, 200, "seed 1234's first battle");
            Assert.That(client.CommitMemory(), Is.True, client.LastError);
            var saved = client.SessionMemory; string store = StorePrint(client);
            yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(a.Host, "ReturnHome"));
            Assert.That(a.Loads, Is.EqualTo(new[] { Home }));
            Assert.That(MemoryForgePracticeSession.ConsumeReturnNotice(), Is.EqualTo("패배 · 기존 기억 덱을 그대로 유지했어요."));
            Object.Destroy(a.Host); yield return null;

            MemoryForgeLobbyClient.NextLength = "short";
            scenes.Add(MemoryForgeLobbyClient.ExpeditionScene);
            yield return SceneManager.LoadSceneAsync(MemoryForgeLobbyClient.ExpeditionScene, LoadSceneMode.Additive);
            var scene = SceneManager.GetSceneByName(MemoryForgeLobbyClient.ExpeditionScene);
            Assert.That(scene.isLoaded, Is.True);
            var shipped = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MemoryForgeExpeditionClient>(true)).Single();
            yield return null;
            var expected = CoreRun.Create(ClientRunContent.Load(), MemoryForgeExpeditionClient.PracticeSetup(shipped.Seed + 1, "short", "practice-2-" + (shipped.Seed + 1), saved, 1));
            Assert.That(JToken.DeepEquals(Tree(shipped.Snapshot!), Tree(expected.State)), Is.True, "the shipped scene departs exactly from the returned store with the next number");
            Assert.That(StorePrint(shipped), Is.EqualTo(store));
            Assert.That((MemoryForgePracticeSession.HasHeldPractice, MemoryForgeLobbyClient.NextLength), Is.EqualTo((false, (string?)null)));
        }

        // ───────── helpers ─────────
        static bool Saving(MemoryForgeExpeditionClient c)
        {
            var phase = c.Snapshot!.Phase;
            return !c.HasTurnSummary && !c.HasActionPresentation && !c.PickerOpen && (phase == "VICTORY" || phase == "DEFEAT");
        }
        // Only turn ends until the party falls; the bounded loop is the acceptance witness named by `what`.
        static IEnumerator EndTurnsUntilDefeat(MemoryForgeExpeditionClient client, int bound, string what)
        {
            for (int ends = 0; ends < bound && client.Snapshot!.Phase == "BATTLE"; ends++)
            {
                Assert.That(client.EndTurn(), Is.True, client.LastError);
                Assert.That(client.SkipTurnSummary() && client.ContinueTurnSummary(), Is.True);
                if (ends % 8 == 7) yield return null;
            }
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("DEFEAT"), what + " must defeat a party that only ends turns within " + bound + " ends");
        }
        static string Title(RunState s)
        {
            string outcome = (string?)Get(s.Result, "outcome") ?? "";
            return outcome == "defeat" ? "패배" : outcome == "return" ? "기록소 귀환" : outcome == "clear" ? "탐사 완주" : "탐사 종료";
        }
        // A saved deck card: one row per distinct card (×N for repeats) covering exactly the deck the store holds (or the baseline), and,
        // unless its rows scroll, ending at its last row rather than at a fixed column height.
        static void AssertDeckCard(GameObject view, string cid, int cards)
        {
            var card = view.GetComponentsInChildren<RectTransform>().Single(r => r.name == "Saved:" + cid);
            var list = card.GetComponentsInChildren<RectTransform>().Single(r => r.name == "SavedList:" + cid);
            var rows = list.GetComponentsInChildren<Text>().ToList();
            Assert.That(rows, Is.Not.Empty, cid + " shows its deck");
            int counted = rows.Sum(t => { int at = t.text.LastIndexOf("  ×", StringComparison.Ordinal); return at < 0 ? 1 : int.Parse(t.text.Substring(at + 3), CultureInfo.InvariantCulture); });
            Assert.That(counted, Is.EqualTo(cards), cid + ": the rows cover every card of the deck once");
            Assert.That(rows.Select(t => t.text).Distinct().Count(), Is.EqualTo(rows.Count), cid + ": a repeated card is one row");
            foreach (var row in rows) AssertReadable(row.gameObject, t => t == row.text, cid + " row");
            if (list.GetComponent<ScrollRect>() != null) return; // a long deck scrolls inside its card
            var last = rows.Last().rectTransform;
            float bottom = -list.anchoredPosition.y - last.anchoredPosition.y + last.sizeDelta.y;
            Assert.That(card.sizeDelta.y - bottom, Is.LessThanOrEqualTo(16f), cid + ": the card ends at its last row, with no empty column under it");
        }
        // Each named control: at least `touch` both ways, wholly inside the frame, the top hit at its own face centre, and no two touch
        // areas overlapping.
        static void AssertControls(GameObject host, string[] names, float touch, string what)
        {
            var frame = (RectTransform)View(host).transform.Find("ExpeditionFrame");
            var areas = names.Select(n => (Name: n, Button: ButtonNamed(host, n))).Select(x => (x.Name, x.Button, Area: FrameRect((RectTransform)x.Button.transform, frame))).ToList();
            var bounds = frame.rect;
            foreach (var (name, button, area) in areas)
            {
                Assert.That(Math.Min(area.width, area.height), Is.GreaterThanOrEqualTo(touch - 0.5f), what + ": " + name + " is at least 48dp (" + area + ")");
                Assert.That(area.xMin >= bounds.xMin - 0.5f && area.xMax <= bounds.xMax + 0.5f && area.yMin >= bounds.yMin - 0.5f && area.yMax <= bounds.yMax + 0.5f, Is.True,
                    what + ": " + name + " " + area + " lies inside the frame " + bounds);
                var face = button.targetGraphic.rectTransform;
                Assert.That(TopButton(RectTransformUtility.WorldToScreenPoint(null, face.TransformPoint(face.rect.center))), Is.SameAs(button), what + ": " + name + " takes a press on its face");
            }
            for (int i = 0; i < areas.Count; i++)
                for (int k = i + 1; k < areas.Count; k++)
                {
                    var x = areas[i].Area; var o = areas[k].Area;
                    float w = Math.Min(x.xMax, o.xMax) - Math.Max(x.xMin, o.xMin), h = Math.Min(x.yMax, o.yMax) - Math.Max(x.yMin, o.yMin);
                    Assert.That(w <= 0.5f || h <= 0.5f, Is.True, what + ": touch areas of " + areas[i].Name + " " + x + " and " + areas[k].Name + " " + o + " overlap");
                }
        }
        static Rect FrameRect(RectTransform r, RectTransform frame)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            Vector3 a = frame.InverseTransformPoint(corners[0]), b = frame.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        }
        static RunData Content(MemoryForgeExpeditionClient client) =>
            (RunData)typeof(MemoryForgeExpeditionClient).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
        // JSON trees, not CanonicalState: a Run graph may legally carry fractional metadata.
        static JToken Tree(RunState state) => JToken.FromObject(state.ToJsonGraph());
        static string Print(RunState state) => Tree(state).ToString(Formatting.None);
        // The complete private session store graph (schema, memory, version, history, receipts, …) as JSON text, read-only.
        static string StorePrint(MemoryForgeExpeditionClient client) => JToken.FromObject(Store(client)).ToString(Formatting.None);
        static IDictionary<string, object?> Store(MemoryForgeExpeditionClient client) =>
            (IDictionary<string, object?>)typeof(MemoryForgeExpeditionClient).GetField("store", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
        static object? Get(object? node, string key) => node is IDictionary<string, object?> map && map.TryGetValue(key, out var value) ? value : null;
        static List<object?> ListOf(object? value) => value is IList list ? list.Cast<object?>().ToList() : new List<object?>();
        static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
        static GameObject View(GameObject host)
        {
            var active = new List<GameObject>();
            foreach (Transform child in host.transform) if (child.name == MemoryForgeExpeditionClient.OwnedCanvas && child.gameObject.activeInHierarchy) active.Add(child.gameObject);
            Assert.That(active.Count, Is.EqualTo(1), "exactly one active owned expedition view");
            return active[0];
        }
        static Button ButtonNamed(GameObject host, string name) => View(host).GetComponentsInChildren<Button>().Single(x => x.name == name);
        static Vector2 Center(Button button)
        {
            var rect = (RectTransform)button.transform;
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }
        static Button? TopButton(Vector2 point)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left }, hits);
            Assert.That(hits, Is.Not.Empty, "something takes a press at " + point);
            return hits[0].gameObject.GetComponentInParent<Button>();
        }
        // A real pointer click at the button's own centre; the button must be the top raycast hit there.
        static void Click(Button button)
        {
            var pointer = new PointerEventData(EventSystem.current) { position = Center(button), button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), button.name + " must be the top hit at its own centre");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
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
