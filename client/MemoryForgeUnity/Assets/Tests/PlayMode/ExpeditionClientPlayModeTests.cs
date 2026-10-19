// P1-UNITY-EXPEDITION-CLIENT: Play Mode checks of the expedition practice view over the real shared-core Run and the exported full
// content (ClientRunContent.Load). Every step goes through the component's public commands (the handlers its buttons call) or a real
// EventSystem raycast. An independent mirror Run, created from the same public PracticeSetup, receives the same public Core commands
// and must stay identical, so no Run state is injected, restored or set privately. This is not Android/device, server, account or
// final-art acceptance, and the bounded command driver is neither a strategy, a balance check nor a whole-game claim.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MemoryForge.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using CoreRun = MemoryForge.Core.Run;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public class ExpeditionClientPlayModeTests
    {
        const long Seed = 1234;
        readonly List<GameObject> hosts = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator DestroyOwnedHosts()
        {
            foreach (var host in hosts) if (host != null) Object.Destroy(host);
            hosts.Clear();
            yield return null;
        }

        sealed class Opening
        {
            public GameObject? Host;
            public MemoryForgeExpeditionClient? Client;
            public CoreRun? Mirror;
        }

        // The seed is set on a disabled host before the component's real OnEnable start; nothing else is injected.
        IEnumerator Open(Opening result, long seed = Seed)
        {
            var host = new GameObject("ExpeditionClientTestHost"); hosts.Add(host);
            host.SetActive(false);
            var client = host.AddComponent<MemoryForgeExpeditionClient>(); client.Seed = seed; client.Length = "short";
            host.SetActive(true);
            yield return null;
            result.Host = host; result.Client = client;
            result.Mirror = CoreRun.Create(ClientRunContent.Load(), MemoryForgeExpeditionClient.PracticeSetup(seed, "short", "practice-1-" + seed, new Dictionary<string, object?>(), 0));
        }

        [UnityTest]
        public IEnumerator FreshPracticeOpensTheActualReachableMapInOneOwnedCanvas()
        {
            Assert.That(Application.isPlaying, Is.True);
            var opening = new Opening(); yield return Open(opening);
            var host = opening.Host!; var client = opening.Client!; var mirror = opening.Mirror!;
            Assert.That(client.LastError, Is.Null);
            var s = client.Snapshot!;
            Assert.That(s.Phase, Is.EqualTo("MAP"));
            Assert.That(s.Position, Is.Null);
            Assert.That(s.RunId, Is.EqualTo("practice-1-" + Seed));
            Assert.That(s.Schema, Is.EqualTo("mf-run-memory-v2"));
            Assert.That(s.MemoryPolicy, Is.EqualTo("v2"));
            Assert.That(s.MemoryVersion, Is.EqualTo(0));
            Assert.That(s.Zones, Is.EqualTo(new[] { 1 }), "original short expedition: zone 1 only");
            Assert.That(s.Party.Select(p => p.CharId), Is.EqualTo(MemoryForgeExpeditionClient.ReferenceParty));
            Assert.That(Fingerprint(s), Is.EqualTo(Fingerprint(mirror.State)), "the view's Run must be exactly Create of the public PracticeSetup");
            Assert.That(client.SessionMemoryVersion, Is.EqualTo(0));
            Assert.That(client.SessionMemory, Is.Empty);

            var view = SingleView(host);
            var nodes = view.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Node:", StringComparison.Ordinal)).ToList();
            var all = s.Map!.Floors.SelectMany(row => row).Select(n => n.Id).Concat(new[] { s.Map.Boss.Id }).ToList();
            Assert.That(nodes.Select(b => b.name.Substring(5)), Is.EquivalentTo(all), "every actual map node is drawn once");
            Assert.That(nodes.Where(b => b.interactable).Select(b => b.name.Substring(5)), Is.EquivalentTo(mirror.Available()), "only the actual reachable nodes are enabled");
            Assert.That(mirror.Available(), Is.EquivalentTo(s.Map.Floors[0].Select(n => n.Id)));
            Canvas.ForceUpdateCanvases();
            AssertReadable(view, t => t == "기억 공방", "title");
            AssertReadable(view, t => t.StartsWith("탐사 연습", StringComparison.Ordinal), "practice label");
            AssertReadable(view, t => t == "체력 " + s.Hp + " / " + s.MaxHp, "expedition HP");
            AssertReadable(view, t => t.Contains("앱을 닫으면 사라져요"), "session-only practice notice");
            var active = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(e => e.isActiveAndEnabled).ToList();
            Assert.That(active.Count, Is.EqualTo(1), "exactly one active input system");
            Assert.That(active[0].transform.IsChildOf(view.transform), Is.True, "the clean test scene's only input system must be owned by the view");

            // Disabling removes only the owned view; enabling again redraws the same expedition rather than starting another.
            string before = Fingerprint(client.Snapshot!);
            host.SetActive(false);
            yield return null;
            Assert.That(OwnedViews(host), Is.Empty);
            host.SetActive(true);
            yield return null;
            SingleView(host);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator PointerOnAReachableNodeEntersTheActualBattleAndAnUnreachableNodeChangesNothing()
        {
            var opening = new Opening(); yield return Open(opening);
            var host = opening.Host!; var client = opening.Client!; var mirror = opening.Mirror!;
            var s = client.Snapshot!; string before = Fingerprint(s);
            Assert.That(client.EnterNode(s.Map!.Floors[3][0].Id), Is.False);
            Assert.That(client.LastError, Is.EqualTo("NODE_UNREACHABLE"));
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "a refused Enter must change nothing");
            yield return null;

            string first = mirror.Available()[0];
            Canvas.ForceUpdateCanvases();
            var button = ActiveView(host).GetComponentsInChildren<Button>().Single(b => b.name == "Node:" + first);
            Assert.That(button.interactable, Is.True);
            var pointer = PointerAt(button);
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), "the reachable node must be the top hit at its own center");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            mirror.Enter(first);
            var entered = client.Snapshot!;
            Assert.That(client.LastError, Is.Null);
            Assert.That(entered.Phase, Is.EqualTo("BATTLE"));
            Assert.That(entered.Position, Is.EqualTo(first));
            Assert.That(entered.BattleType, Is.EqualTo("battle"), "floor 1 nodes are always normal battles in the original map rule");
            Assert.That(entered.Battle!.Phase, Is.EqualTo("PLAYER"));
            Assert.That(Fingerprint(entered), Is.EqualTo(Fingerprint(mirror.State)), "the click must be exactly the actual Run.Enter");

            yield return null;
            var view = ActiveView(host); var b = entered.Battle; var rules = ClientBattleContent.Load().Battle.Rules;
            Canvas.ForceUpdateCanvases();
            AssertReadable(view, t => t == "행동력  " + b.Ap, "AP");
            AssertReadable(view, t => t.StartsWith("턴 종료", StringComparison.Ordinal), "end turn");
            foreach (var group in b.Enemies.GroupBy(e => e.Name)) AssertReadable(view, t => t == group.Key, "enemy " + group.Key, group.Count());
            foreach (var enemy in b.Enemies)
            {
                string intent = "이번  " + MemoryForgeBattleClient.IntentText(enemy.Intent, enemy.Status, b.DifficultyDamage, rules.WeakBps, true)
                    + "\n다음  " + MemoryForgeBattleClient.IntentText(enemy.Next, enemy.Status, b.DifficultyDamage, rules.WeakBps, false);
                AssertReadable(view, t => t == intent, "intent of " + enemy.Uid);
            }
            Assert.That(view.GetComponentsInChildren<Button>().Where(x => x.name.StartsWith("Hand:", StringComparison.Ordinal)).Select(x => x.name.Substring(5)), Is.EqualTo(b.Zones.Hand));
        }

        // §14.2: the actual 2.6 s departure toast expires while a reachable node is held down. Expiry must keep the pressed button, its
        // canvas and the EventSystem, change nothing in the Run and show the map's own fallback hint; the release over the same button is
        // then the one click that enters the battle.
        [UnityTest]
        public IEnumerator NoticeExpiryDuringAHeldPressKeepsTheViewAndTheReleaseEntersTheBattleOnce()
        {
            const string departed = "출발했어요. 밝게 표시된 칸을 골라 나아가세요.", fallback = "밝게 표시된 칸만 갈 수 있어요. 칸을 누르면 바로 들어가요.";
            float opened = Time.realtimeSinceStartup;
            var opening = new Opening(); yield return Open(opening);
            var host = opening.Host!; var client = opening.Client!; var mirror = opening.Mirror!;
            yield return null; // graphics of a fresh view become raycastable one rendered frame later
            Assert.That(client.Notice, Is.EqualTo(departed), "the press must start while the actual departure toast is showing");
            var view = ActiveView(host); var events = EventSystem.current;
            Assert.That(events, Is.Not.Null);
            var noticeLabel = view.GetComponentsInChildren<Text>().Single(t => t.text == departed);
            string first = mirror.Available()[0];
            var button = view.GetComponentsInChildren<Button>().Single(x => x.name == "Node:" + first);
            Assert.That(button.interactable, Is.True);
            string before = Fingerprint(client.Snapshot!);
            Assert.That(before, Is.EqualTo(Fingerprint(mirror.State)));

            // Press as the input module does: pointer-down on the top hit, which becomes the remembered press target.
            var pointer = PointerAt(button); var hits = new List<RaycastResult>(); events.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), "the reachable node must be the top hit at its own center");
            pointer.pointerPressRaycast = hits[0]; pointer.pointerCurrentRaycast = hits[0]; pointer.pressPosition = pointer.position; pointer.eligibleForClick = true;
            pointer.pointerPress = ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerDownHandler) ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
            pointer.rawPointerPress = hits[0].gameObject;
            Assert.That(pointer.pointerPress, Is.SameAs(button.gameObject), "the node button takes its own press");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "a press alone changes nothing");

            // Hold through the component's real timer; nothing private is set and the timer is not shortened.
            float deadline = Time.realtimeSinceStartup + 6f;
            while (client.Notice.Length != 0 && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null; // a replaced view would linger inactive until the end of the expiry frame
            Assert.That(client.Notice, Is.Empty, "the actual departure toast must have expired");
            Assert.That(Time.realtimeSinceStartup - opened, Is.GreaterThanOrEqualTo(2.6f), "the hold spans the real 2.6 s timer");
            Assert.That(client.LastError, Is.Null);
            Assert.That(OwnedViews(host), Is.EqualTo(new[] { view }), "toast expiry must not rebuild the view under a held press");
            Assert.That(view != null && view.activeInHierarchy, Is.True, "the same canvas stays active");
            Assert.That(button != null && button.interactable, Is.True, "the held button is the same live, interactable button");
            Assert.That(EventSystem.current, Is.SameAs(events), "the same EventSystem keeps the press");
            Assert.That(noticeLabel != null && noticeLabel.isActiveAndEnabled, Is.True, "the toast's own label is reused");
            Assert.That(noticeLabel!.text, Is.EqualTo(fallback), "the map's existing fallback hint replaces the toast");
            Assert.That(noticeLabel.color, Is.EqualTo(Hex("98a5c0")), "with the fallback hint's own muted color");
            AssertReadable(view!, t => t == fallback, "map fallback hint");
            Assert.That(view!.GetComponentsInChildren<Text>().Any(t => t.text == departed), Is.False, "the expired toast is gone");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "expiry changes no Run state, log, events or RNG");
            Assert.That(Fingerprint(mirror.State), Is.EqualTo(before));

            // Release as the input module does: pointer-up on the press target, then a click only if the release lands on it.
            events.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject), Is.SameAs(pointer.pointerPress), "the release lands on the object that took the press");
            Assert.That(pointer.eligibleForClick, Is.True);
            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerClickHandler);
            mirror.Enter(first);
            var entered = client.Snapshot!;
            Assert.That(client.LastError, Is.Null);
            Assert.That(entered.Phase, Is.EqualTo("BATTLE"));
            Assert.That(entered.Position, Is.EqualTo(first));
            Assert.That(Fingerprint(entered), Is.EqualTo(Fingerprint(mirror.State)), "the held click must be exactly one actual Run.Enter");
        }

        [UnityTest]
        public IEnumerator BattleSelectionSpendsNothingAndOnlyTheConfirmedPlayReachesTheRun()
        {
            var opening = new Opening(); yield return Open(opening);
            var host = opening.Host!; var client = opening.Client!; var mirror = opening.Mirror!;
            var content = ClientBattleContent.Load().Battle;
            string first = mirror.Available()[0];
            Assert.That(client.EnterNode(first), Is.True); mirror.Enter(first);
            yield return null;
            var b = mirror.State.Battle!;
            string enemy = b.Enemies.First(e => e.Alive).Uid;
            string? card = b.Zones.Hand.FirstOrDefault(uid => mirror.BattleAllowedModes(uid).Contains("now") && mirror.BattleCheck(uid, "now", enemy) == null
                && MemoryForgeExpeditionClient.ChoiceRequests(mirror.BattleEffectiveCard(uid), "now", content).Count == 0);
            Assert.That(card, Is.Not.Null, "seed 1234's opening hand offers an immediate card without card picks");
            string before = Fingerprint(client.Snapshot!);
            client.SelectEnemy(enemy); client.SelectCard(card!); client.SelectMode("now");
            Assert.That(new[] { client.Target, client.SelectedCard, client.SelectedMode }, Is.EqualTo(new[] { enemy, card, "now" }));
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "selecting a card, mode and target must not touch the Run");
            yield return null;
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "rendering the selection must not touch the Run");

            // Positive control at the confirm button, then the history overlay must intercept the same screen point.
            Canvas.ForceUpdateCanvases();
            var confirm = ActiveView(host).GetComponentsInChildren<Button>().Single(x => x.name == "ConfirmCard");
            Assert.That(confirm.interactable, Is.True);
            var probe = PointerAt(confirm); Vector2 behind = probe.position;
            var control = new List<RaycastResult>(); EventSystem.current.RaycastAll(probe, control);
            Assert.That(control, Is.Not.Empty);
            Assert.That(control[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(confirm), "the probe point must hit the real confirm button first");
            ActiveView(host).GetComponentsInChildren<Button>().Single(x => x.name == "LogButton").onClick.Invoke();
            // Rebuilt UI is raycastable after the next frame, as with a real subsequent pointer input.
            yield return null;
            Canvas.ForceUpdateCanvases();
            var pointer = new PointerEventData(EventSystem.current) { position = behind, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.Null, "the overlay must intercept the obscured confirm button");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "a click behind the overlay spent a card");
            ActiveView(host).GetComponentsInChildren<Button>().Single(x => x.name == "CloseLog").onClick.Invoke();

            // Core rejections: explicit codes, nothing changes.
            var actor = client.Snapshot!.Battle!.Actors.First(a => a.Gauge < content.Rules.GaugeMax);
            Assert.That(client.UseUltimate(actor.Id), Is.False);
            Assert.That(client.LastError, Is.EqualTo("ULT_NOT_READY"));
            Assert.That(client.PlayCard("not-in-hand", "now"), Is.False);
            Assert.That(client.LastError, Is.EqualTo("NOT_IN_HAND"));
            Assert.That(client.TakeCard(content.Cards.First().Id), Is.False);
            Assert.That(client.LastError, Is.EqualTo("BAD_REWARD"));
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "rejected commands must change nothing");

            var start = client.Snapshot!; int cost = mirror.BattleCostOf(card!, "now");
            Assert.That(client.ConfirmCard(), Is.True);
            Assert.That(client.LastError, Is.Null);
            mirror.BattleCommand("play", Play(card!, "now", enemy));
            var after = client.Snapshot!;
            Assert.That(Fingerprint(after), Is.EqualTo(Fingerprint(mirror.State)), "the confirmed play is exactly one actual Run.BattleCommand");
            if (after.Battle != null)
            {
                Assert.That(after.Battle.V, Is.EqualTo(start.Battle!.V + 1));
                Assert.That(after.Battle.Ap, Is.EqualTo(start.Battle.Ap - cost));
                Assert.That(after.Battle.Zones.Hand, Does.Not.Contain(card));
            }
            else Assert.That(after.Phase, Is.EqualTo("REWARD").Or.EqualTo("DEFEAT"));

            // Caller snapshots stay detached from the live Run and battle.
            string committed = Fingerprint(after);
            after.Hp = 1; after.Coins = 99999; after.Deck.Clear(); after.Map!.Floors.Clear(); start.Battle!.Zones.Hand.Clear();
            if (after.Battle != null) { after.Battle.Hp = 1; after.Battle.Zones.Hand.Clear(); }
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed), "caller snapshots must stay detached");
            yield return null;
        }

        [Test]
        public void RunBattleQueriesAreDetachedPureReadsOfTheLiveBattle()
        {
            var run = CoreRun.Create(ClientRunContent.Load(), MemoryForgeExpeditionClient.PracticeSetup(Seed, "short", "queries", new Dictionary<string, object?>(), 0));
            // Outside a battle the value queries reject like BattleCommand; BattleCheck keeps Check's error-code contract.
            Assert.That(run.BattleCheck("c1"), Is.EqualTo("NO_BATTLE"));
            foreach (TestDelegate query in new TestDelegate[] { () => run.BattleAllowedModes("c1"), () => run.BattleCostOf("c1"), () => run.BattleNeedsTarget("c1"), () => run.BattleEffectiveCard("c1") })
                Assert.That(Assert.Throws<RuleException>(query)!.Code, Is.EqualTo("NO_BATTLE"));
            Assert.That(Assert.Throws<RuleException>(() => run.BattleCommand("end"))!.Code, Is.EqualTo("NO_BATTLE"));

            run.Enter(run.Available()[0]);
            string before = Fingerprint(run.State);
            var hand = run.State.Battle!.Zones.Hand; string enemy = run.State.Battle.Enemies.First(e => e.Alive).Uid;
            foreach (string uid in hand)
            {
                run.BattleAllowedModes(uid).Clear();
                var definition = run.BattleEffectiveCard(uid); string name = definition.Name; definition.Name = "changed"; definition.Effects.Clear();
                Assert.That(run.BattleEffectiveCard(uid).Name, Is.EqualTo(name), "EffectiveCard must return a detached definition");
                foreach (string mode in run.BattleAllowedModes(uid))
                {
                    run.BattleCostOf(uid, mode); run.BattleCheck(uid, mode, enemy);
                    var picks = new List<string> { "x" }; run.BattleCheck(uid, mode, "auto", picks);
                    Assert.That(picks, Is.EqualTo(new[] { "x" }));
                }
                run.BattleNeedsTarget(uid);
            }
            Assert.That(run.BattleCheck("missing", "now", enemy), Is.EqualTo("NOT_IN_HAND"));
            Assert.That(Fingerprint(run.State), Is.EqualTo(before), "queries must not change state, RNG, version or logs");

            // The queries agree with the committed command: a card Check accepts is played at exactly its CostOf.
            string card = hand.First(uid => run.BattleAllowedModes(uid).Contains("now") && run.BattleCheck(uid, "now", enemy) == null);
            var start = run.State.Battle!; int cost = run.BattleCostOf(card, "now");
            run.BattleCommand("play", Play(card, "now", enemy));
            var after = run.State;
            if (after.Battle == null) Assert.That(after.Phase, Is.EqualTo("REWARD").Or.EqualTo("DEFEAT"));
            else
            {
                Assert.That(after.Battle.V, Is.EqualTo(start.V + 1));
                Assert.That(after.Battle.Ap, Is.EqualTo(start.Ap - cost));
                Assert.That(run.BattleCheck(card, "now", enemy), Is.EqualTo("NOT_IN_HAND"));
            }
        }

        [UnityTest]
        public IEnumerator AbandonKeepsTheSessionMemoryAndTheNextDepartureStartsFromIt()
        {
            var opening = new Opening(); yield return Open(opening);
            var client = opening.Client!; var mirror = opening.Mirror!;
            string first = mirror.Available()[0];
            Assert.That(client.EnterNode(first), Is.True);
            string running = Fingerprint(client.Snapshot!);
            Assert.That(client.Depart(), Is.False, "a departure while an expedition runs must be refused");
            Assert.That(client.LastError, Is.EqualTo("RUN_IN_PROGRESS"));
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(running));
            Assert.That(client.Abandon(), Is.True);
            Assert.That(client.ShowingResult, Is.True);
            Assert.That(client.SessionMemoryVersion, Is.EqualTo(0));
            Assert.That(client.SessionMemory, Is.Empty, "abandoning writes nothing to the session store");
            yield return null;
            Assert.That(client.Depart(), Is.True, client.LastError);
            var next = client.Snapshot!;
            var expected = CoreRun.Create(ClientRunContent.Load(), MemoryForgeExpeditionClient.PracticeSetup(Seed + 1, "short", "practice-2-" + (Seed + 1), new Dictionary<string, object?>(), 0));
            Assert.That(next.RunId, Is.EqualTo("practice-2-" + (Seed + 1)));
            Assert.That(Fingerprint(next), Is.EqualTo(Fingerprint(expected.State)), "the next departure is exactly Create from the unchanged session memory");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ActualCommandsReachTheTerminalMemoryChoiceCommitAndDepartFromTheSavedVersion()
        {
            var opening = new Opening(); yield return Open(opening);
            var host = opening.Host!; var client = opening.Client!; var mirror = opening.Mirror!;
            var content = ClientBattleContent.Load().Battle; var events = ClientRunContent.Load().Events;
            int steps = 0;
            while (steps < 4000)
            {
                var state = mirror.State;
                if (state.Phase == "VICTORY" || state.Phase == "DEFEAT") break;
                Step(client, mirror, state, content, events);
                if (++steps % 8 == 0) yield return null;
            }
            var terminal = client.Snapshot!;
            Assert.That(terminal.Phase, Is.EqualTo("VICTORY").Or.EqualTo("DEFEAT"), "the bounded driver must reach the terminal save within 4000 actual commands");
            Assert.That(Get(terminal.Pending, "type"), Is.EqualTo("save"));
            yield return null;

            var info = client.MemoryChanges()!;
            Assert.That(CanonicalState.Hash(info), Is.EqualTo(CanonicalState.Hash(mirror.MemoryChanges())), "the save screen reads the Run's own change list");
            var order = terminal.Party.SelectMany(p => ListOf(Get(Get(info, p.CharId), "changes")).Select(c => (string)Get(c, "id")!)).ToList();
            string before = Fingerprint(client.Snapshot!);
            Assert.That(client.ToggleChange("not-a-change"), Is.False);
            Assert.That(client.LastError, Is.EqualTo("INVALID_SELECTION"));
            Assert.That(client.SelectedChangeIds, Is.Empty);
            foreach (string id in order) Assert.That(client.ToggleChange(id), Is.True);
            Assert.That(client.SelectedChangeIds, Is.EquivalentTo(order));
            var everything = client.PreviewSelection()!;
            Assert.That(CanonicalState.Hash(everything), Is.EqualTo(CanonicalState.Hash(mirror.PreviewMemory(order.Cast<object?>().ToList()))), "the preview is the Run's PreviewMemory");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "selection and preview must not change the Run");
            Assert.That(client.SessionMemoryVersion, Is.EqualTo(0));
            yield return null;

            if (Get(everything, "error") is string error)
            {
                // The confirm button is disabled with a Korean reason, and the Core refusal through the public command changes nothing.
                var view = ActiveView(host); Canvas.ForceUpdateCanvases();
                Assert.That(view.GetComponentsInChildren<Button>().Single(x => x.name == "CommitMemory").interactable, Is.False);
                AssertReadable(view, t => t.StartsWith("저장 버튼을 쓸 수 없는 이유", StringComparison.Ordinal), "refusal reason");
                Assert.That(client.CommitMemory(), Is.False);
                Assert.That(client.LastError, Is.EqualTo(error));
                Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before));
                Assert.That(client.SessionMemoryVersion, Is.EqualTo(0));
                // Keep, in change-list order, each change whose preview stays valid (the original "fill within capacity" order).
                foreach (string id in order) client.ToggleChange(id);
                Assert.That(client.SelectedChangeIds, Is.Empty);
                foreach (string id in order) { client.ToggleChange(id); if (Get(client.PreviewSelection(), "error") != null) client.ToggleChange(id); }
            }
            var chosen = order.Where(client.SelectedChangeIds.Contains).ToList();
            var preview = client.PreviewSelection()!;
            Assert.That(Get(preview, "error"), Is.Null);
            Assert.That(client.CommitMemory(), Is.True, client.LastError);
            var store = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schema"] = "mf-memory-v2", ["version"] = 0L, ["memory"] = new Dictionary<string, object?>(StringComparer.Ordinal),
                ["quarantine"] = new Dictionary<string, object?>(StringComparer.Ordinal), ["legacyBackup"] = new Dictionary<string, object?>(StringComparer.Ordinal),
                ["history"] = new List<object?>(), ["receipts"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            };
            var request = new Dictionary<string, object?>(StringComparer.Ordinal)
                { ["requestId"] = client.LastRequestId, ["expectedMemoryVersion"] = 0L, ["runId"] = terminal.RunId, ["selectedChangeIds"] = chosen.Cast<object?>().ToList() };
            var committed = mirror.CommitMemory(request, store);
            var saved = (Dictionary<string, object?>)committed["state"]!;
            var done = client.Snapshot!;
            Assert.That(done.Phase, Is.EqualTo("DONE"));
            Assert.That(client.ShowingResult, Is.True);
            Assert.That(Fingerprint(done), Is.EqualTo(Fingerprint(mirror.State)), "the commit is exactly the actual Run.CommitMemory");
            Assert.That(client.SessionMemoryVersion, Is.EqualTo(1));
            Assert.That(CanonicalState.Hash(client.SessionMemory), Is.EqualTo(CanonicalState.Hash(saved["memory"])), "the session store is the store CommitMemory returned");
            Assert.That(CanonicalState.Hash(Get(committed["response"], "used")), Is.EqualTo(CanonicalState.Hash(Get(preview, "used"))), "the committed capacities equal the confirmed preview");
            yield return null;
            var result = ActiveView(host); Canvas.ForceUpdateCanvases();
            string savedLine = chosen.Count == 0 ? "기존 덱 유지" : "변화 " + chosen.Count + "개 저장";
            AssertReadable(result, t => t.StartsWith(savedLine, StringComparison.Ordinal) && t.Contains("용량"), "saved result");

            Assert.That(client.Depart(), Is.True, client.LastError);
            var next = client.Snapshot!;
            var expected = CoreRun.Create(ClientRunContent.Load(), MemoryForgeExpeditionClient.PracticeSetup(Seed + 1, "short", "practice-2-" + (Seed + 1), saved["memory"], 1));
            Assert.That(next.RunId, Is.EqualTo("practice-2-" + (Seed + 1)));
            Assert.That(next.MemoryVersion, Is.EqualTo(1));
            Assert.That(Fingerprint(next), Is.EqualTo(Fingerprint(expected.State)), "the next departure is exactly Create from the saved memory and version");
            var startMemory = next.MemoryBefore!.ToJsonGraph();
            foreach (var party in next.Party)
                Assert.That(CanonicalState.Hash(startMemory[party.CharId]), Is.EqualTo(CanonicalState.Hash(Get(saved["memory"], party.CharId))), party.CharId + " starts from the saved deck");
            string running = Fingerprint(next);
            Assert.That(client.Depart(), Is.False);
            Assert.That(client.LastError, Is.EqualTo("RUN_IN_PROGRESS"));
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(running));
        }

        [UnityTest]
        public IEnumerator HandCardInfoIsCoresTextOfTheEffectiveCardAndOnlyTheLaterExplicitConfirmPlays()
        {
            var opening = new Opening(); yield return Open(opening);
            var host = opening.Host!; var client = opening.Client!; var mirror = opening.Mirror!;
            var battle = ClientRunContent.Load().Memory.Battle;
            string first = mirror.Available()[0];
            Assert.That(client.EnterNode(first), Is.True); mirror.Enter(first);
            var b = mirror.State.Battle!;
            string enemy = b.Enemies.First(e => e.Alive).Uid;
            string? card = b.Zones.Hand.FirstOrDefault(uid => mirror.BattleAllowedModes(uid).Contains("now") && mirror.BattleCheck(uid, "now", enemy) == null
                && MemoryForgeExpeditionClient.ChoiceRequests(mirror.BattleEffectiveCard(uid), "now", battle).Count == 0);
            Assert.That(card, Is.Not.Null, "seed 1234's opening hand offers an immediate card without card picks");
            client.SelectEnemy(enemy); client.SelectCard(card!); client.SelectMode("now");
            string before = Fingerprint(client.Snapshot!);

            // Every hand card's detail is exactly Core's text of its effective definition; reading spends and selects nothing.
            foreach (string uid in b.Zones.Hand)
            {
                Assert.That(client.ShowHandCardInfo(uid), Is.True);
                Assert.That(client.CardInfoText, Is.EqualTo(BattleText.Card(mirror.BattleEffectiveCard(uid), battle)), "hand detail of " + uid);
                client.CloseCardInfo();
                Assert.That(client.CardInfoOpen, Is.False);
            }
            Assert.That(client.ShowHandCardInfo("not-in-hand"), Is.False);
            Assert.That(new[] { client.Target, client.SelectedCard, client.SelectedMode }, Is.EqualTo(new[] { enemy, card, "now" }), "reading must not change the selection");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "reading card details must not touch the Run, its log, events or RNG");
            yield return null;

            // Positive control at confirm, then the real '카드 설명' button opens the detail for the selected card.
            Canvas.ForceUpdateCanvases();
            var confirm = ButtonNamed(host, "ConfirmCard"); Assert.That(confirm.interactable, Is.True);
            Vector2 behind = PointerAt(confirm).position;
            Assert.That(TopButton(behind), Is.SameAs(confirm), "the probe point must hit the real confirm button first");
            Click(ButtonNamed(host, "CardInfoButton"));
            Assert.That(client.CardInfoOpen, Is.True);
            string expected = BattleText.Card(mirror.BattleEffectiveCard(card!), battle);
            Assert.That(client.CardInfoText, Is.EqualTo(expected));
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before));
            yield return null;
            var view = ActiveView(host); Canvas.ForceUpdateCanvases();
            AssertReadable(view, t => t == expected, "full effect text");
            AssertReadable(view, t => t.Contains("즉시 비용 " + mirror.BattleCostOf(card!, "now")), "current cost");
            // The note keeps the definition-text vs actual-outcome distinction, points at the action-line forecast and states that a
            // reservation forecast is the current-state estimate (the old "later power is not previewed" phrase is gone).
            AssertReadable(view, t => t.Contains("카드 정의의 값") && t.Contains("행동 안내 줄에 보여요") && t.Contains("미리 보여 주지 않아요")
                && t.Contains("적 방어에 막힌 몫도 들어가요") && t.Contains("예약의 예상은 지금 상태에서 바로 해결한다고 가정한 값") && !t.Contains("나중 위력"), "definition text vs forecast note");

            // The detail overlay intercepts the obscured confirm; a click there spends nothing and keeps the detail open.
            var pointer = new PointerEventData(EventSystem.current) { position = behind, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.Null, "the card detail must intercept the confirm button");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "a click behind the card detail spent a card");
            Assert.That(client.CardInfoOpen, Is.True);
            ButtonNamed(host, "CloseCardInfo").onClick.Invoke();
            Assert.That(client.CardInfoOpen, Is.False);
            Assert.That(new[] { client.Target, client.SelectedCard, client.SelectedMode }, Is.EqualTo(new[] { enemy, card, "now" }));
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before));
            yield return null;

            // Only the explicit confirm afterwards is the one actual play.
            Canvas.ForceUpdateCanvases();
            var again = ButtonNamed(host, "ConfirmCard");
            Assert.That(TopButton(behind), Is.SameAs(again));
            Click(again);
            mirror.BattleCommand("play", Play(card!, "now", enemy));
            Assert.That(client.LastError, Is.Null);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(Fingerprint(mirror.State)), "the confirmed play is exactly one actual Run.BattleCommand");
            yield return null;
        }

        // ───────── P1-EXPEDITION-FORECAST-UI §14.3/§14.7/§6: labels of Core's pure Run.BattleForecast ─────────
        // Reference party (power4 160 / vit 81 / level 1, no weapon passive or relic) against a living turn-1 enemy without block:
        // the original card values (sera_cover: block 5, then 4 damage).
        static readonly Dictionary<string, (int Loss, int Block)> TurnOneLiterals = new Dictionary<string, (int Loss, int Block)>(StringComparer.Ordinal)
        {
            ["sera_round"] = (8, 0), ["sera_cover"] = (4, 5), ["wc_basic_ranged"] = (7, 0), ["com_guard"] = (0, 6), ["harin_shield"] = (0, 8), ["harin_bash"] = (6, 0),
            ["harin_counter"] = (0, 5), ["com_slash"] = (6, 0), ["wc_basic_melee"] = (7, 0), ["nia_wrench"] = (7, 0), ["nia_barrier"] = (0, 7), ["wc_basic_device"] = (0, 6)
        };

        // Every hand card × mode × living target: the view's forecast equals the mirror's, its action line is exactly that forecast's label,
        // nothing is spent; then real rays select one reference card and only the confirmed play reaches the Run, matching the forecast.
        [UnityTest]
        public IEnumerator ActionLineShowsCoresForecastWhileSelectingAndOnlyTheConfirmedPlayMatchesItOnce()
        {
            var opening = new Opening(); yield return Open(opening);
            var host = opening.Host!; var client = opening.Client!; var mirror = opening.Mirror!;
            var content = ClientBattleContent.Load().Battle;
            string first = mirror.Available()[0];
            Assert.That(client.EnterNode(first), Is.True); mirror.Enter(first);
            yield return null;
            var b = mirror.State.Battle!; string before = Fingerprint(client.Snapshot!);
            Assert.That(before, Is.EqualTo(Fingerprint(mirror.State)));
            int literals = 0, reservations = 0; string? played = null, playedTarget = null;
            foreach (var enemy in b.Enemies.Where(e => e.Alive).ToList())
                foreach (string uid in b.Zones.Hand)
                    foreach (string mode in mirror.BattleAllowedModes(uid))
                    {
                        client.SelectEnemy(enemy.Uid); client.SelectCard(uid); client.SelectMode(mode);
                        var f = mirror.BattleForecast(uid, mode, enemy.Uid);
                        Assert.That(Project(client.PlayForecast(uid, mode)!), Is.EqualTo(Project(f)), "the view forecasts Core's own result of the exact request " + uid + "/" + mode);
                        bool auto = f.Error == null && MemoryForgeExpeditionClient.ChoiceRequests(mirror.BattleEffectiveCard(uid), mode, content).Count != 0;
                        string line = client.ForecastLine(f, auto);
                        yield return null; Canvas.ForceUpdateCanvases();
                        AssertReadable(ActiveView(host), t => t == line, "action-line forecast of " + uid + "/" + mode + " on " + enemy.Uid);
                        if (f.Error != null)
                        {
                            Assert.That((f.Cost, f.Due, f.Hidden, f.EnemyHpLoss, f.BlockDelta, f.HpDelta, f.Defeat, f.EnemyDamage, f.NewlyBroken), Is.EqualTo((0, (int?)null, false, 0, 0, 0, false, 0, 0)),
                                "a rejected request reports only its code");
                            continue;
                        }
                        Assert.That(line, Does.StartWith((auto ? "자동 선택 기준 예상 · " : "예상 · ") + (mode == "now" ? "" : ModeLabel(mode) + " · ") + "행동력 " + f.Cost + " · "));
                        if (mode != "now")
                        {
                            // The actual cost and absolute due turn, then Core's current-state estimate labelled as such (or nothing when hidden).
                            reservations++;
                            string due = f.Due + "턴 시작(" + ModeLabel(mode) + ")에 해결 · ";
                            Assert.That(f.Due, Is.EqualTo(b.Turn + (mode == "next" ? 1 : 2)), "a reservation shows its absolute due turn");
                            Assert.That(line.Contains("예약의 실제 해결 결과는 그때 확인해요") || line.Contains("이 행동으로 패배해요"), Is.False,
                                "no cost/due-only note, and an estimate never says committing the reservation defeats the party");
                            if (f.Hidden)
                            {
                                Assert.That((f.EnemyHpLoss, f.EnemyDamage, f.NewlyBroken, f.BlockDelta, f.HpDelta, f.Defeat), Is.EqualTo((0, 0, 0, 0, 0, false)), "a hidden estimate discloses no outcome");
                                Assert.That(line, Does.EndWith(due + "드로우·무작위 결과는 해결된 뒤에 알 수 있어요"));
                                continue;
                            }
                            Assert.That(line, Does.Contain(due + "현재 상태 기준 예상: "), "a visible reservation outcome is the labelled current-state estimate");
                            if (f.EnemyDamage != 0) Assert.That(line, Does.Contain("적 피해 " + f.EnemyDamage));
                            if (f.BlockDelta != 0) Assert.That(line, Does.Contain("방어 " + (f.BlockDelta > 0 ? "+" : "") + f.BlockDelta));
                            if (f.NewlyBroken != 0) Assert.That(line, Does.Contain("새 균열 " + f.NewlyBroken + "체"));
                            Assert.That(line.Contains("지금 해결되면 패배해요"), Is.EqualTo(f.Defeat));
                            // Source harin_shield rv11 on the unmodified opening (its immediate value 8 is a TurnOneLiterals entry).
                            if (b.Cards[uid].Id == "harin_shield")
                                Assert.That((f.BlockDelta, f.EnemyDamage, f.EnemyHpLoss, f.HpDelta, f.NewlyBroken, f.Defeat), Is.EqualTo((11, 0, 0, 0, 0, false)), "harin_shield reservation estimate");
                            continue;
                        }
                        if (f.Hidden) { Assert.That(line, Does.EndWith("드로우·무작위 결과는 사용한 뒤에 알 수 있어요")); continue; }
                        if (TurnOneLiterals.TryGetValue(b.Cards[uid].Id, out var literal) && enemy.Block == 0 && enemy.Hp >= literal.Loss)
                        {
                            literals++;
                            // Without enemy block, the damage is exactly the HP loss.
                            Assert.That((f.EnemyHpLoss, f.EnemyDamage, f.BlockDelta, f.HpDelta, f.Defeat), Is.EqualTo((literal.Loss, literal.Loss, literal.Block, 0, false)), "original card value of " + b.Cards[uid].Id);
                            Assert.That(line, Does.Contain(literal.Loss != 0 ? "적 피해 " + literal.Loss : "방어 +" + literal.Block));
                            if (played == null && !auto) { played = uid; playedTarget = enemy.Uid; }
                        }
                    }
            Assert.That(literals, Is.GreaterThan(0), "seed 1234's opening hand holds a reference attack or shield card");
            // RULES_NOW_PLAY: every card is played now; no hand card offers a reservation mode.
            Assert.That(reservations, Is.EqualTo(0), "seed 1234's opening hand offers no reservation");
            Assert.That(b.Zones.Hand.All(uid => mirror.BattleAllowedModes(uid).SequenceEqual(new[] { "now" })), Is.True, "every hand card offers only now");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "every selection, mode, target and forecast left the Run, RNG, log, events and AP untouched");
            Assert.That(Fingerprint(mirror.State), Is.EqualTo(before));

            // Real rays: target, card, mode; the shown forecast is the clicked request's, and only the confirm plays it.
            string card = played!, target = playedTarget!;
            Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(host, "Enemy:" + target)); yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(host, "Hand:" + card)); yield return null; Canvas.ForceUpdateCanvases();
            // RULES_NOW_PLAY: no mode button; the card's only mode, now, is chosen with the card.
            Assert.That(new[] { client.Target, client.SelectedCard, client.SelectedMode }, Is.EqualTo(new[] { target, card, "now" }));
            var forecast = mirror.BattleForecast(card, "now", target);
            string shown = client.ForecastLine(forecast, false);
            AssertReadable(ActiveView(host), t => t == shown, "forecast of the clicked selection");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "real clicks on target, card and mode spend nothing");
            var start = mirror.State.Battle!;
            Click(ButtonNamed(host, "ConfirmCard"));
            mirror.BattleCommand("play", Play(card, "now", target));
            Assert.That(client.LastError, Is.Null);
            var after = client.Snapshot!;
            Assert.That(Fingerprint(after), Is.EqualTo(Fingerprint(mirror.State)), "the confirmed play is exactly one actual Run.BattleCommand");
            if (after.Battle != null)
            {
                BattleState done = after.Battle;
                int loss = start.Enemies.Sum(e => Math.Max(0, e.Hp - done.Enemies.Single(a => a.Uid == e.Uid).Hp));
                int damage = start.Enemies.Sum(e => { var a = done.Enemies.Single(x => x.Uid == e.Uid); return Math.Max(0, e.Hp + e.Block - (a.Hp + a.Block)); });
                int broken = done.Enemies.Count(a => a.BrokenUntil != 0 && start.Enemies.Single(e => e.Uid == a.Uid).BrokenUntil == 0);
                Assert.That(done.V, Is.EqualTo(start.V + 1));
                Assert.That((loss, damage, broken, done.Block - start.Block, done.Hp - start.Hp, done.Ap),
                    Is.EqualTo((forecast.EnemyHpLoss, forecast.EnemyDamage, forecast.NewlyBroken, forecast.BlockDelta, forecast.HpDelta, start.Ap - forecast.Cost)),
                    "the actual play equals the forecast that was shown");
            }
            else Assert.That(after.Phase, Is.EqualTo("REWARD").Or.EqualTo("DEFEAT"));
            yield return null;
        }

        // P1-FORECAST-VISIBILITY-FIX regression: right after an actual play its 2.6 s success toast is showing. Choosing the next card, mode
        // and target at once (no wait on the timer) must show that request's forecast immediately, change nothing in the Run, and only the
        // explicit confirm then plays it once.
        [UnityTest]
        public IEnumerator NextIntentShowsItsForecastImmediatelyWhileThePreviousToastWouldStillBeActive()
        {
            var opening = new Opening(); yield return Open(opening);
            var host = opening.Host!; var client = opening.Client!; var mirror = opening.Mirror!;
            var content = ClientBattleContent.Load().Battle;
            string first = mirror.Available()[0];
            Assert.That(client.EnterNode(first), Is.True); mirror.Enter(first);
            yield return null;
            var b = mirror.State.Battle!; string enemy = b.Enemies.First(e => e.Alive).Uid;
            bool Plain(string uid) => mirror.BattleAllowedModes(uid).Contains("now") && mirror.BattleCheck(uid, "now", enemy) == null
                && MemoryForgeExpeditionClient.ChoiceRequests(mirror.BattleEffectiveCard(uid), "now", content).Count == 0;
            string? card = b.Zones.Hand.FirstOrDefault(Plain);
            Assert.That(card, Is.Not.Null, "seed 1234's opening hand offers an immediate card without card picks");
            client.SelectEnemy(enemy); client.SelectCard(card!); client.SelectMode("now");
            Assert.That(client.ConfirmCard(), Is.True, client.LastError);
            float played = Time.realtimeSinceStartup;
            mirror.BattleCommand("play", Play(card!, "now", enemy));
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(Fingerprint(mirror.State)), "the first play is exactly one actual Run.BattleCommand");
            string toast = client.Notice;
            Assert.That(toast, Is.Not.Empty, "the actual play's success toast is active");
            var live = mirror.State.Battle;
            Assert.That(live, Is.Not.Null, "seed 1234's first play leaves the battle running");
            enemy = live!.Enemies.First(e => e.Alive).Uid;
            string? next = live.Zones.Hand.FirstOrDefault(Plain);
            Assert.That(next, Is.Not.Null, "a second immediate card without picks is still playable");
            yield return null; Canvas.ForceUpdateCanvases();
            AssertReadable(ActiveView(host), t => t == toast, "positive control: the toast is drawn before the next intent");
            string before = Fingerprint(client.Snapshot!);

            // The next intent by real rays, without waiting on the toast timer.
            Click(ButtonNamed(host, "Enemy:" + enemy)); yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(host, "Hand:" + next)); yield return null; Canvas.ForceUpdateCanvases();
            // RULES_NOW_PLAY: no mode button; the card's only mode, now, is chosen with the card.
            Assert.That(Time.realtimeSinceStartup - played, Is.LessThan(2.6f), "read while the previous toast's real timer would still be running");
            Assert.That(new[] { client.Target, client.SelectedCard, client.SelectedMode }, Is.EqualTo(new[] { enemy, next, "now" }));
            Assert.That(client.Notice, Is.Empty, "the new intent retired the stale toast");
            Assert.That(client.LastError, Is.Null);
            var forecast = mirror.BattleForecast(next!, "now", enemy);
            Assert.That(Project(client.PlayForecast(next!, "now")!), Is.EqualTo(Project(forecast)));
            string shown = client.ForecastLine(forecast, false);
            var view = ActiveView(host);
            AssertReadable(view, t => t == shown, "the current request's forecast is drawn immediately");
            Assert.That(view.GetComponentsInChildren<Text>().Any(t => t.text == toast), Is.False, "the previous command's toast no longer hides the forecast");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "the new selection leaves the Run, RNG, log, events and AP untouched");
            Assert.That(Fingerprint(mirror.State), Is.EqualTo(before));

            // The first play's display refuses the next command; it is finished explicitly (it may already have ended on its timer).
            FinishActionDisplay(client, false);
            var start = mirror.State.Battle!;
            Click(ButtonNamed(host, "ConfirmCard"));
            mirror.BattleCommand("play", Play(next!, "now", enemy));
            Assert.That(client.LastError, Is.Null);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(Fingerprint(mirror.State)), "the explicit confirm is exactly one actual Run.BattleCommand");
            if (client.Snapshot!.Battle is BattleState done) Assert.That(done.V, Is.EqualTo(start.V + 1));
            yield return null;
        }

        // The reference kit's draw card nia_scan: every number of the immediate play stays hidden, its reservation shows only cost and due,
        // and the confirmed play is the one actual command.
        [UnityTest]
        public IEnumerator DrawCardForecastHidesEveryNumberAndOnlyTheConfirmedPlayReachesTheRun()
        {
            var opening = new Opening(); yield return Open(opening);
            var host = opening.Host!; var client = opening.Client!; var mirror = opening.Mirror!;
            var found = new Found(); yield return DriveUntil(client, mirror, "nia_scan", found);
            Assert.That(found.Uid, Is.Not.Null, "the reference kit's nia_scan must reach a playable hand within the bounded actual commands");
            string card = found.Uid!, enemy = found.Enemy!;
            yield return AwaitNoticeExpiry(client);
            string before = Fingerprint(client.Snapshot!);
            Assert.That(before, Is.EqualTo(Fingerprint(mirror.State)));
            client.SelectEnemy(enemy); client.SelectCard(card); client.SelectMode("now");
            var f = mirror.BattleForecast(card, "now", enemy);
            Assert.That((f.Error, f.Hidden, f.EnemyHpLoss, f.BlockDelta, f.HpDelta, f.Defeat, f.EnemyDamage, f.NewlyBroken), Is.EqualTo(((string?)null, true, 0, 0, 0, false, 0, 0)),
                "a draw card's numeric outcome stays hidden");
            Assert.That(Project(client.PlayForecast(card, "now")!), Is.EqualTo(Project(f)));
            string hidden = "예상 · 행동력 " + f.Cost + " · 드로우·무작위 결과는 사용한 뒤에 알 수 있어요"; // RULES_NOW_PLAY: no "즉시" mode label
            Assert.That(client.ForecastLine(f, false), Is.EqualTo(hidden));
            yield return null; Canvas.ForceUpdateCanvases();
            AssertReadable(ActiveView(host), t => t == hidden, "hidden draw forecast");
            AssertNoForecastNumbers(ActiveView(host));

            // The draw definition hides its reservation estimate too: cost and absolute due only, no outcome, break or defeat.
            var reserved = mirror.BattleForecast(card, "next", enemy);
            if (reserved.Error == null)
            {
                client.SelectMode("next");
                Assert.That((reserved.Hidden, reserved.EnemyHpLoss, reserved.EnemyDamage, reserved.NewlyBroken, reserved.BlockDelta, reserved.HpDelta, reserved.Defeat),
                    Is.EqualTo((true, 0, 0, 0, 0, 0, false)), "a reserved draw card discloses no estimate");
                string line = "예상 · 다음 턴 · 행동력 " + reserved.Cost + " · " + (mirror.State.Battle!.Turn + 1) + "턴 시작(다음 턴)에 해결 · 드로우·무작위 결과는 해결된 뒤에 알 수 있어요";
                Assert.That(client.ForecastLine(reserved, false), Is.EqualTo(line));
                yield return null; Canvas.ForceUpdateCanvases();
                AssertReadable(ActiveView(host), t => t == line, "hidden reservation forecast");
                AssertNoForecastNumbers(ActiveView(host));
                client.SelectMode("now");
            }
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "hidden and reservation forecasts spend nothing");
            yield return null; Canvas.ForceUpdateCanvases();
            var start = mirror.State.Battle!;
            Click(ButtonNamed(host, "ConfirmCard"));
            mirror.BattleCommand("play", Play(card, "now", enemy));
            Assert.That(client.LastError, Is.Null);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(Fingerprint(mirror.State)), "the confirmed hidden play is exactly one actual Run.BattleCommand");
            if (client.Snapshot!.Battle is BattleState done) Assert.That(done.V, Is.EqualTo(start.V + 1));
            yield return null;
        }

        // The reference kit's pick card nia_tune: the action line names Core's automatic choice as its basis, the last pick's candidates each
        // show the forecast of exactly their own picks, the auto line that of the auto request, and the clicked candidate is the one play.
        [UnityTest]
        public IEnumerator PickSheetForecastsMatchTheAutoPromiseAndEachConcreteFinalCandidate()
        {
            var opening = new Opening(); yield return Open(opening);
            var host = opening.Host!; var client = opening.Client!; var mirror = opening.Mirror!;
            var content = ClientBattleContent.Load().Battle;
            var found = new Found(); yield return DriveUntil(client, mirror, "nia_tune", found);
            Assert.That(found.Uid, Is.Not.Null, "the reference kit's nia_tune must reach a playable hand within the bounded actual commands");
            string card = found.Uid!, enemy = found.Enemy!;
            yield return AwaitNoticeExpiry(client);
            string before = Fingerprint(client.Snapshot!);
            Assert.That(MemoryForgeExpeditionClient.ChoiceRequests(mirror.BattleEffectiveCard(card), "now", content).Count, Is.EqualTo(1), "nia_tune asks for one card, so the first candidate click completes the picks");
            client.SelectEnemy(enemy); client.SelectCard(card); client.SelectMode("now");
            var auto = mirror.BattleForecast(card, "now", enemy);
            string line = client.ForecastLine(auto, true);
            Assert.That(line, Does.StartWith("자동 선택 기준 예상 · 행동력 ")); // RULES_NOW_PLAY: no "즉시" mode label
            yield return null; Canvas.ForceUpdateCanvases();
            AssertReadable(ActiveView(host), t => t == line, "auto-basis action line");
            Click(ButtonNamed(host, "ConfirmCard"));
            Assert.That(client.PickerOpen, Is.True);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "opening the pick sheet spends nothing");
            yield return null; Canvas.ForceUpdateCanvases();
            var view = ActiveView(host); var b = mirror.State.Battle!;
            var candidates = b.Zones.Hand.Where(u => u != card).ToList();
            Assert.That(candidates, Is.Not.Empty, "a hand card to tune");
            foreach (string uid in candidates)
            {
                var picks = new List<string> { uid };
                var f = mirror.BattleForecast(card, "now", enemy, picks);
                Assert.That(Project(client.PlayForecast(card, "now", picks)!), Is.EqualTo(Project(f)), "the candidate forecast is Core's for exactly that pick");
                Assert.That(picks, Is.EqualTo(new[] { uid }), "forecasting never consumes the caller's picks");
                string caption = mirror.BattleEffectiveCard(uid).Name + " · 손패\n" + client.ForecastOutcome(f);
                AssertReadable(view, t => t == caption, "final candidate " + uid);
            }
            AssertReadable(view, t => t == "자동으로 고르고 사용하면 · " + client.ForecastText(auto), "auto-pick forecast");
            AssertReadable(view, t => t.StartsWith("마지막 선택이에요", StringComparison.Ordinal), "final-pick promise");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "candidate and auto forecasts spend nothing");
            var start = b; string chosen = candidates[0];
            Click(ButtonNamed(host, "Pick:" + chosen));
            mirror.BattleCommand("play", Play(card, "now", enemy, new[] { chosen }));
            Assert.That(client.LastError, Is.Null);
            Assert.That(client.PickerOpen, Is.False);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(Fingerprint(mirror.State)), "the final candidate click is exactly one actual play with that pick");
            if (client.Snapshot!.Battle is BattleState done) Assert.That(done.V, Is.EqualTo(start.V + 1));
            yield return null;
        }

        // Supplement, not the client path: Core forecasts of original authored battles (the forecast oracle's reflect-defeat and
        // hidden-definition setups) through the view's own formatter, for labels the reference opening cannot reach.
        [UnityTest]
        public IEnumerator ForecastLabelsOfAuthoredCoreBattlesWarnDefeatAndHideDrawResults()
        {
            var opening = new Opening(); yield return Open(opening);
            var client = opening.Client!; var content = ClientBattleContent.Load().Battle;
            var reflect = Battle.Create(content, new BattleSetup { Party = { new BattleActorSetup { CharId = "sera" } },
                Deck = { new BattleCardSpec { Id = "sera_double" }, new BattleCardSpec { Id = "sera_round" } }, Hp = 3, MaxHp = 81, Seed = 13, Enemies = { "e_mirrorclerk" } });
            reflect.Command(new BattleRequest("begin", 0, "begin")); reflect.Command(new BattleRequest("end", 1, "end"));
            string reflectBefore = BattlePrint(reflect);
            var lethal = reflect.Forecast("c1");
            Assert.That(Project(lethal), Is.EqualTo(Project(new BattlePlayForecast { Cost = 1, EnemyHpLoss = 4, EnemyDamage = 4, HpDelta = -3, Defeat = true })), "oracle literal: the first reflected hit is lethal");
            Assert.That(client.ForecastText(lethal), Is.EqualTo("행동력 1 · 적 피해 4 · 파티 체력 -3 · 이 행동으로 패배해요"));
            // RULES_NOW_PLAY: a reservation request is refused, and its text is the error, not an estimate.
            var estimate = reflect.Forecast("c1", "next");
            Assert.That((estimate.Error, estimate.Cost, estimate.Due), Is.EqualTo(("NOT_RESERVABLE", 0, (int?)null)), "a reservation request is refused");
            Assert.That(client.ForecastText(estimate), Does.Not.StartWith("행동력 "));
            Assert.That(BattlePrint(reflect), Is.EqualTo(reflectBefore), "defeat forecasts change nothing");

            var hidden = Battle.Create(content, new BattleSetup { Party = { new BattleActorSetup { CharId = "sion" }, new BattleActorSetup { CharId = "lumi" }, new BattleActorSetup { CharId = "rien" } },
                Deck = { new BattleCardSpec { Id = "sion_erase" }, new BattleCardSpec { Id = "lumi_storm" }, new BattleCardSpec { Id = "rien_rest" }, new BattleCardSpec { Id = "sion_cut" } },
                Hp = 243, MaxHp = 243, Seed = 15, Enemies = { "e_cog", "e_page" } });
            hidden.Command(new BattleRequest("begin", 0, "begin"));
            string hiddenBefore = BattlePrint(hidden);
            foreach (var (uid, cost) in new[] { ("c1", 0), ("c2", 3), ("c3", 1) })
            {
                var f = hidden.Forecast(uid);
                Assert.That((f.Error, f.Hidden, f.Cost, f.EnemyHpLoss, f.BlockDelta, f.HpDelta, f.Defeat, f.EnemyDamage, f.NewlyBroken), Is.EqualTo(((string?)null, true, cost, 0, 0, 0, false, 0, 0)),
                    "draw/random/nested-draw " + uid);
                Assert.That(client.ForecastText(f), Is.EqualTo("행동력 " + cost + " · 드로우·무작위 결과는 사용한 뒤에 알 수 있어요"));
            }
            Assert.That(hidden.Forecast("c3", "next").Error, Is.EqualTo("NOT_RESERVABLE"), "RULES_NOW_PLAY: no reservation estimate exists");
            Assert.That(client.ForecastText(hidden.Forecast("c4", "now", "e6")), Is.EqualTo("행동력 1 · 적 피해 8"));
            var rejected = hidden.Forecast("c99");
            Assert.That(rejected.Error, Is.EqualTo("NOT_IN_HAND"));
            Assert.That(client.ForecastText(rejected), Does.Not.StartWith("행동력 "), "a rejected request shows its error text, not an outcome");
            Assert.That(BattlePrint(hidden), Is.EqualTo(hiddenBefore), "forecasts change nothing");
            yield return null;
        }

        // RULES_NOW_PLAY: the view draws no mode button, a reservation request through the client is refused before it reaches the Run,
        // and nothing changes. (It replaced ReservationShowsItsCurrentStateEstimateAndOnlyTheConfirmSchedulesItWithoutApplyingIt.)
        [UnityTest]
        public IEnumerator AReservationRequestIsRefusedAndTheViewOffersNoModeChoice()
        {
            var opening = new Opening(); yield return Open(opening);
            var host = opening.Host!; var client = opening.Client!; var mirror = opening.Mirror!;
            string first = mirror.Available()[0];
            Assert.That(client.EnterNode(first), Is.True); mirror.Enter(first);
            yield return null;
            var b = mirror.State.Battle!; string enemy = b.Enemies.First(e => e.Alive).Uid;
            string card = b.Zones.Hand.First(uid => mirror.BattleCheck(uid, "now", enemy) == null);
            string before = Fingerprint(client.Snapshot!);
            Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(host, "Enemy:" + enemy)); yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(host, "Hand:" + card)); yield return null; Canvas.ForceUpdateCanvases();
            Assert.That((client.SelectedCard, client.SelectedMode), Is.EqualTo((card, "now")), "choosing a card chooses its only mode");
            Assert.That(ActiveView(host).GetComponentsInChildren<Button>().Any(x => x.name.StartsWith("Mode:", StringComparison.Ordinal)), Is.False, "no mode button is drawn");
            foreach (string mode in new[] { "next", "after" })
            {
                Assert.That(mirror.BattleCheck(card, mode, enemy), Is.EqualTo("NOT_RESERVABLE"), mode);
                Assert.That(client.PlayCard(card, mode), Is.False, mode + " is refused");
                Assert.That(client.LastError, Is.EqualTo("NOT_RESERVABLE"), mode);
                Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), mode + ": a refused request changes nothing");
            }
            Assert.That(Fingerprint(mirror.State), Is.EqualTo(before));
            yield return null;
        }

        // RULES_NOW_PLAY: the former reservation sources play now with their immediate source values through the view's own formatter.
        // (It replaced ReservationEstimateLabelsOfAuthoredCoreBattlesKeepCostDueAndCurrentStateSemantics.)
        [UnityTest]
        public IEnumerator FormerReservationSourcesPlayNowWithTheirImmediateValues()
        {
            var opening = new Opening(); yield return Open(opening);
            var client = opening.Client!; var content = ClientBattleContent.Load().Battle;

            // harin_shield: block 8 now (its old rv11 is gone); the play gains exactly that block.
            var shield = Begin(content, 0, 81, new[] { "harin" }, new[] { "harin_shield" }, new[] { "e_cog" });
            string print = BattlePrint(shield);
            var guard = shield.Forecast("c1");
            Assert.That(Project(guard), Is.EqualTo(Project(new BattlePlayForecast { Cost = 1, BlockDelta = 8 })), "source harin_shield v8");
            Assert.That(client.ForecastText(guard), Is.EqualTo("행동력 1 · 방어 +8"));
            Assert.That(shield.Forecast("c1", "next").Error, Is.EqualTo("NOT_RESERVABLE"));
            Assert.That(BattlePrint(shield), Is.EqualTo(print), "a forecast changes nothing");
            Commit(shield, "play", Play("c1", "now", null));
            Assert.That((shield.State.Block, shield.State.Ap, shield.State.Reservations.Count), Is.EqualTo((8, 3, 0)), "the play gains its block now");

            // sera_timed: 보존, damage 20 now (its old rv22/ra30 are gone), cost 2.
            var est = Begin(content, 19, 81, new[] { "sera" }, new[] { "sera_round", "sera_double", "sera_cover", "sera_timed", "sera_aim" }, new[] { "e_keeper", "e_cog" });
            string keeper = EnemyUid(est, "e_keeper");
            Assert.That(content.Cards.Single(c => c.Id == "sera_timed").Keywords, Does.Contain("retain"), "source: sera_timed has 보존");
            var timed = est.Forecast("c4", "now", keeper);
            Assert.That((timed.Error, timed.Cost, timed.EnemyHpLoss, timed.EnemyDamage), Is.EqualTo(((string?)null, 2, 20, 20)), "source sera_timed v20");
            Assert.That(client.ForecastText(timed), Does.StartWith("행동력 2 · 적 피해 20"));
            Assert.That((est.Forecast("c4", "next", keeper).Error, est.Forecast("c4", "after", keeper).Error), Is.EqualTo(("NOT_RESERVABLE", "NOT_RESERVABLE")));
            yield return null;
        }

        sealed class Found { public string? Uid, Enemy; }
        // Actual commands through the existing bounded Step until the live hand holds `cardId` playable now against the first living enemy.
        static IEnumerator DriveUntil(MemoryForgeExpeditionClient client, CoreRun mirror, string cardId, Found found)
        {
            var content = ClientBattleContent.Load().Battle; var events = ClientRunContent.Load().Events;
            for (int steps = 0; steps < 3000; steps++)
            {
                var s = mirror.State;
                if (s.Phase == "VICTORY" || s.Phase == "DEFEAT") yield break;
                if (s.Phase == "BATTLE" && s.Battle != null && s.Battle.Phase == "PLAYER")
                {
                    var battle = s.Battle; string enemy = battle.Enemies.First(e => e.Alive).Uid;
                    string? uid = battle.Zones.Hand.FirstOrDefault(u => battle.Cards[u].Id == cardId && mirror.BattleCheck(u, "now", enemy) == null);
                    if (uid != null) { found.Uid = uid; found.Enemy = enemy; yield break; }
                }
                Step(client, mirror, s, content, events);
                if (steps % 8 == 7) yield return null;
            }
        }
        // The last driver command's 2.6 s toast covers the action line: wait on the component's real timer (nothing set or shortened).
        static IEnumerator AwaitNoticeExpiry(MemoryForgeExpeditionClient client)
        {
            float deadline = Time.realtimeSinceStartup + 6f;
            while (client.Notice.Length != 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(client.Notice, Is.Empty, "the driver's last toast must expire on its real timer");
        }
        static (string?, string, int, int?, bool, int, int, int, bool, int, int) Project(BattlePlayForecast f) =>
            (f.Error, f.Mode, f.Cost, f.Due, f.Hidden, f.EnemyHpLoss, f.BlockDelta, f.HpDelta, f.Defeat, f.EnemyDamage, f.NewlyBroken);
        // No forecast outcome wording anywhere in the view (the enemy panels' own "체력 n / m" and "균열 n/m" are not forecast labels).
        static void AssertNoForecastNumbers(GameObject view) =>
            Assert.That(view.GetComponentsInChildren<Text>().Any(t => new[] { "적 피해 ", "적 체력 -", "파티 체력 -", "파티 체력 +", "방어 +", "새 균열 ", "이 행동으로 패배해요", "지금 해결되면 패배해요", "현재 상태 기준 예상" }
                .Any(t.text.Contains)), Is.False, "no forecast number is drawn anywhere");
        // A fresh public Battle.Create of an original authored setup followed by the real begin command.
        static Battle Begin(BattleData content, uint seed, int hp, string[] party, string[] deck, string[] enemies, params string[] relics)
        {
            var setup = new BattleSetup { Hp = hp, MaxHp = hp, Seed = seed };
            setup.Party.AddRange(party.Select(id => new BattleActorSetup { CharId = id })); setup.Deck.AddRange(deck.Select(id => new BattleCardSpec { Id = id }));
            setup.Enemies.AddRange(enemies); setup.Relics.AddRange(relics);
            var battle = Battle.Create(content, setup); battle.Command(new BattleRequest("begin", 0, "begin"));
            return battle;
        }
        static void Commit(Battle battle, string kind, BattleArgument? args = null) => battle.Command(new BattleRequest(kind + "-" + battle.State.V, battle.State.V, kind, args));
        static string EnemyUid(Battle battle, string id) => battle.State.Enemies.First(e => e.Id == id).Uid;
        static string ModeLabel(string mode) => mode == "now" ? "즉시" : mode == "next" ? "다음 턴" : "다다음 턴";
        static string BattlePrint(Battle battle)
        {
            var graph = battle.State.ToJsonGraph();
            return CanonicalState.Hash(graph) + "/" + CanonicalState.Hash(graph["log"]) + "/" + CanonicalState.Hash(graph["ev"]);
        }

        [UnityTest]
        public IEnumerator RewardShopAndDeckDetailsAreReadableBeforeTakingOrPayingAndNeverChangeTheRun()
        {
            // Seed1234 has no shop; seed1235's actual first-edge path reaches one without changing any generated nodes.
            var opening = new Opening(); yield return Open(opening, 1235);
            var host = opening.Host!; var client = opening.Client!; var mirror = opening.Mirror!;
            var battle = ClientRunContent.Load().Memory.Battle; var content = ClientBattleContent.Load().Battle; var events = ClientRunContent.Load().Events;
            int steps = 0;
            while (steps < 2000 && mirror.State.Phase != "REWARD" && mirror.State.Phase != "VICTORY" && mirror.State.Phase != "DEFEAT")
            { Step(client, mirror, mirror.State, content, events); if (++steps % 8 == 0) yield return null; }
            Assert.That(mirror.State.Phase, Is.EqualTo("REWARD"), "the first actual battle of seed 1235 must be won by the bounded driver");
            var cards = ListOf(Get(mirror.State.Pending, "cards")).Select(x => (string)x!).ToList();
            Assert.That(cards, Is.Not.Empty, "a normal battle reward offers cards");
            yield return null;

            string before = Fingerprint(client.Snapshot!);
            Canvas.ForceUpdateCanvases();
            var take = ButtonNamed(host, "RewardCard:" + cards[0]);
            Vector2 behind = PointerAt(take).position;
            Assert.That(TopButton(behind), Is.SameAs(take), "the probe point must hit the real take button first");
            foreach (string id in cards)
            {
                Click(ButtonNamed(host, "CardInfo:" + id));
                string expected = BattleText.Card(battle.Cards.Single(c => c.Id == id), battle);
                Assert.That(client.CardInfoText, Is.EqualTo(expected), "reward detail of " + id);
                Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "reading a reward card must not take it");
                yield return null;
                Canvas.ForceUpdateCanvases();
                AssertReadable(ActiveView(host), t => t == expected, "reward card text " + id);
                var pointer = new PointerEventData(EventSystem.current) { position = behind, button = PointerEventData.InputButton.Left };
                var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                Assert.That(hits, Is.Not.Empty);
                Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.Not.SameAs(ButtonNamed(host, "RewardCard:" + cards[0])), "the detail must intercept the take button");
                ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
                Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "a click behind the reward detail took a card");
                ButtonNamed(host, "CloseCardInfo").onClick.Invoke();
                yield return null;
                Canvas.ForceUpdateCanvases();
            }

            // Deck entries: source-definition text for every card, the + named rather than computed; the deck sheet stays open after closing.
            ButtonNamed(host, "DeckButton").onClick.Invoke();
            var deck = mirror.State.Deck;
            foreach (var spec in deck)
            {
                Assert.That(client.ShowDeckCardInfo(spec.Key!), Is.True);
                Assert.That(client.CardInfoText, Is.EqualTo(BattleText.Card(battle.Cards.Single(c => c.Id == spec.Id), battle)), "deck detail of " + spec.Key);
                Assert.That(client.CardInfoBasis!.Contains("강화(+)"), Is.EqualTo(Truthy(Get(spec.ToJsonGraph(), "plus"))));
                client.CloseCardInfo();
            }
            Assert.That(client.ShowDeckCardInfo("not-a-key"), Is.False);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(host, "DeckInfo:" + deck[0].Key));
            yield return null;
            Canvas.ForceUpdateCanvases();
            AssertReadable(ActiveView(host), t => t == client.CardInfoText, "deck card text");
            ButtonNamed(host, "CloseCardInfo").onClick.Invoke();
            Assert.That(ActiveView(host).GetComponentsInChildren<Button>().Any(x => x.name == "CloseDeck"), Is.True, "closing the detail returns to the deck sheet");
            ButtonNamed(host, "CloseDeck").onClick.Invoke();
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "reading the deck must not touch the Run");

            // Only the explicit take changes the deck, exactly as Core's TakeCard.
            Assert.That(Both(client, mirror, () => client.TakeCard(cards[0]), () => mirror.TakeCard(cards[0])), Is.True);
            Assert.That(client.CardInfoOpen, Is.False);
            yield return null;

            // Continue by actual commands, entering a shop whenever the map offers one.
            steps = 0;
            while (steps < 4000 && mirror.State.Phase != "SHOP" && mirror.State.Phase != "VICTORY" && mirror.State.Phase != "DEFEAT")
            {
                var s = mirror.State;
                string? shop = s.Phase == "MAP" ? mirror.Available().FirstOrDefault(id => mirror.NodeById(id)?.Type == "shop") : null;
                if (shop != null) { string node = shop; Both(client, mirror, () => client.EnterNode(node), () => mirror.Enter(node)); }
                else Step(client, mirror, s, content, events);
                if (++steps % 8 == 0) yield return null;
            }
            Assert.That(mirror.State.Phase, Is.EqualTo("SHOP"), "the actual bounded route must reach a shop to verify reading before buying");
            yield return null;
            Canvas.ForceUpdateCanvases();
            string shopBefore = Fingerprint(client.Snapshot!);
            foreach (var item in ListOf(Get(mirror.State.Pending, "cards")))
            {
                string id = (string)Get(item, "id")!;
                var buy = ButtonNamed(host, "Buy:cards:" + id); Vector2 buyPoint = PointerAt(buy).position;
                Click(ButtonNamed(host, "Info:cards:" + id));
                Assert.That(client.CardInfoText, Is.EqualTo(BattleText.Card(battle.Cards.Single(c => c.Id == id), battle)), "shop detail of " + id);
                Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(shopBefore), "reading a shop card must not pay");
                yield return null;
                Canvas.ForceUpdateCanvases();
                AssertReadable(ActiveView(host), t => t == client.CardInfoText, "shop card text " + id);
                var pointer = new PointerEventData(EventSystem.current) { position = buyPoint, button = PointerEventData.InputButton.Left };
                var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                Assert.That(hits, Is.Not.Empty);
                Assert.That(hits[0].gameObject.GetComponentInParent<Button>()?.name, Is.Not.EqualTo("Buy:cards:" + id), "the detail must intercept the buy button");
                ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
                Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(shopBefore), "a click behind the shop detail paid");
                ButtonNamed(host, "CloseCardInfo").onClick.Invoke();
                yield return null;
                Canvas.ForceUpdateCanvases();
            }
        }

        // One public command per call, in a fixed simple order. Battle choices use the mirror's pure queries; every command goes to both
        // the view and the mirror, which must agree on success/error code and end in identical state.
        static void Step(MemoryForgeExpeditionClient client, CoreRun mirror, RunState s, BattleData content, List<RunEventDefinition> events)
        {
            var p = s.Pending;
            switch (s.Phase)
            {
                case "MAP":
                    {
                        string id = mirror.Available()[0];
                        Both(client, mirror, () => client.EnterNode(id), () => mirror.Enter(id));
                        return;
                    }
                case "BATTLE": BattleStep(client, mirror, s.Battle!, content); return;
                case "REWARD":
                    {
                        var evolutions = ListOf(Get(p, "evolutions")); var cards = ListOf(Get(p, "cards")); var relics = ListOf(Get(p, "relic"));
                        if (evolutions.Count != 0)
                        {
                            string key = (string)Get(evolutions[0], "key")!, branch = (string)ListOf(Get(evolutions[0], "options"))[0]!;
                            if (Both(client, mirror, () => client.Evolve(key, branch), () => mirror.Evolve(key, branch))) return;
                        }
                        else if (cards.Count != 0) { string id = (string)cards[0]!; if (Both(client, mirror, () => client.TakeCard(id), () => mirror.TakeCard(id))) return; }
                        else if (relics.Count != 0) { string id = (string)relics[0]!; if (Both(client, mirror, () => client.TakeRelic(id), () => mirror.TakeRelic(id))) return; }
                        else if (Get(p, "part") is string) { if (Both(client, mirror, () => client.TakePart(), () => mirror.TakePart())) return; }
                        Both(client, mirror, () => client.Proceed(), () => mirror.Proceed());
                        return;
                    }
                case "REST":
                    if (Integer(Get(p, "uses")) > 0)
                    {
                        var plain = s.Deck.FirstOrDefault(c => !c.Weapon && !Truthy(Get(c.ToJsonGraph(), "plus")));
                        if (s.Hp * 10 < s.MaxHp * 6 || plain == null) { if (Both(client, mirror, () => client.RestHeal(), () => mirror.Rest("heal"))) return; }
                        else { string key = plain.Key!; if (Both(client, mirror, () => client.RestUpgrade(key), () => mirror.Rest("upgrade", key))) return; }
                    }
                    Both(client, mirror, () => client.Proceed(), () => mirror.Proceed());
                    return;
                case "FORGE":
                    {
                        var offers = Get(p, "offers"); var parts = ListOf(Get(offers, "parts"));
                        if (parts.Count != 0) { string id = (string)parts[0]!; if (Both(client, mirror, () => client.ForgeApply(id), () => mirror.ForgeApply(id))) return; }
                        else if (offers == null && Integer(Get(p, "uses")) > 0)
                        {
                            string key = s.Deck.First(c => !c.Weapon).Key!;
                            if (Both(client, mirror, () => client.ForgeOffer(key), () => mirror.ForgeOffer(key))) return;
                        }
                        Both(client, mirror, () => client.Proceed(), () => mirror.Proceed());
                        return;
                    }
                case "SHOP":
                    {
                        var cards = ListOf(Get(p, "cards"));
                        bool bought = cards.Concat(ListOf(Get(p, "parts"))).Concat(ListOf(Get(p, "relics"))).Any(x => Truthy(Get(x, "sold")));
                        var item = cards.FirstOrDefault(x => !Truthy(Get(x, "sold")) && Integer(Get(x, "price")) <= s.Coins);
                        if (!bought && item != null) { string id = (string)Get(item, "id")!; if (Both(client, mirror, () => client.Buy("cards", id), () => mirror.Buy("cards", id))) return; }
                        Both(client, mirror, () => client.Proceed(), () => mirror.Proceed());
                        return;
                    }
                case "EVENT":
                    {
                        if (Truthy(Get(p, "done"))) { Both(client, mirror, () => client.Proceed(), () => mirror.Proceed()); return; }
                        string eventId = (string)Get(p, "id")!; var definition = events.Last(e => e.Id == eventId);
                        for (int i = 0; i < definition.Choices.Count; i++)
                        {
                            var fx = definition.Choices[i].Effects; int index = i;
                            if (definition.Choices[i].RequiredCoins.GetValueOrDefault() > s.Coins) continue;
                            int count = fx.CardCopy.GetValueOrDefault() != 0 ? 1 : fx.CardRemove.GetValueOrDefault();
                            var keys = count > 0 ? s.Deck.Where(c => !c.Weapon).Select(c => c.Key!).Take(count).ToList() : null;
                            if (Both(client, mirror, () => client.ChooseEvent(index, keys), () => mirror.EventChoose(index, keys?.Cast<object?>().ToList()))) return;
                        }
                        Assert.Fail("no choice of event " + eventId + " was accepted");
                        return;
                    }
                case "ARCHIVE": Both(client, mirror, () => client.Archive("return"), () => mirror.Archive("return")); return;
                default: Assert.Fail("unexpected expedition phase " + s.Phase); return;
            }
        }
        static void BattleStep(MemoryForgeExpeditionClient client, CoreRun mirror, BattleState b, BattleData content)
        {
            Assert.That(b.Phase, Is.EqualTo("PLAYER"), "a live Run battle waits for the player");
            string enemy = b.Enemies.First(e => e.Alive).Uid;
            bool Legal(string uid) => mirror.BattleAllowedModes(uid).Contains("now") && mirror.BattleCheck(uid, "now", enemy) == null;
            bool Picks(string uid) => MemoryForgeExpeditionClient.ChoiceRequests(mirror.BattleEffectiveCard(uid), "now", content).Count != 0;
            string? card = b.Zones.Hand.FirstOrDefault(uid => Legal(uid) && !Picks(uid)) ?? b.Zones.Hand.FirstOrDefault(uid => Legal(uid));
            if (card != null)
            {
                client.SelectEnemy(enemy); client.SelectCard(card); client.SelectMode("now");
                if (Picks(card))
                {
                    // The pick overlay opens without spending; "auto" then sends the picks made so far (none), as its button says.
                    string before = Fingerprint(client.Snapshot!);
                    Assert.That(client.ConfirmCard(), Is.False);
                    Assert.That(client.PickerOpen, Is.True);
                    Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "opening the pick overlay must spend nothing");
                    if (Both(client, mirror, () => client.AutoPick(), () => mirror.BattleCommand("play", Play(card, "now", enemy)))) { FinishActionDisplay(client); return; }
                }
                else if (Both(client, mirror, () => client.ConfirmCard(), () => mirror.BattleCommand("play", Play(card, "now", enemy)))) { FinishActionDisplay(client); return; }
            }
            var ready = b.Actors.FirstOrDefault(a => a.Gauge >= content.Rules.GaugeMax);
            if (ready != null)
            {
                client.SelectEnemy(enemy);
                var args = BattleArgument.Object(new[] { Pair("actor", ready.Id), Pair("target", enemy) });
                if (Both(client, mirror, () => client.UseUltimate(ready.Id), () => mirror.BattleCommand("ult", args))) { FinishActionDisplay(client); return; }
            }
            if (Both(client, mirror, () => client.EndTurn(), () => mirror.BattleCommand("end"))) DismissTurnSummary(client);
        }
        // P1-CARD-FLOW-ACTION-PRESENTATION: an actual play or ultimate opens its display, which refuses the next command. Drivers not about
        // it finish it explicitly (skip; a repeat is inert); both are display only and must leave the Run exactly as it was. `open`: the
        // display must still be open (a driver right after its command); otherwise it may already have ended on its real timer.
        static void FinishActionDisplay(MemoryForgeExpeditionClient client, bool open = true)
        {
            string before = Fingerprint(client.Snapshot!);
            if (open) Assert.That(client.HasActionPresentation, Is.True, "an actual play or ultimate opens its display");
            if (client.HasActionPresentation) Assert.That(client.SkipActionPresentation(), Is.True);
            Assert.That(client.HasActionPresentation, Is.False);
            Assert.That(client.SkipActionPresentation(), Is.False, "a repeated skip is inert");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "finishing the display never reaches the Run");
        }
        // P1-CARD-FLOW-TURN-BARRIER: an actual end opens the blocking turn summary. These drivers are not about it, so they close it
        // explicitly (skip, then continue) before the next command; both are display only and must leave the Run exactly as it was.
        static void DismissTurnSummary(MemoryForgeExpeditionClient client)
        {
            Assert.That(client.HasTurnSummary, Is.True, "an actual end opens the turn summary");
            string before = Fingerprint(client.Snapshot!);
            Assert.That(client.SkipTurnSummary(), Is.True);
            Assert.That(client.TurnSummaryFullyShown, Is.True);
            Assert.That(client.ContinueTurnSummary(), Is.True);
            Assert.That(client.HasTurnSummary, Is.False);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "skip and continue never reach the Run");
        }
        static bool Both(MemoryForgeExpeditionClient client, CoreRun mirror, Func<bool> view, Action core)
        {
            bool ok = view(); string? code = null;
            try { core(); }
            catch (RuleException error) { code = error.Code; }
            Assert.That(ok, Is.EqualTo(code == null), "view and Core disagree: view " + client.LastError + " / Core " + code);
            if (!ok) Assert.That(client.LastError, Is.EqualTo(code));
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(Fingerprint(mirror.State)), "the view must issue exactly the actual Core command");
            return ok;
        }

        static BattleArgument Play(string uid, string mode, string? target) => BattleArgument.Object(new[] { Pair("uid", uid), Pair("mode", mode), Pair("target", target),
            new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array(new BattleArgument[0])) });
        static BattleArgument Play(string uid, string mode, string? target, IEnumerable<string> picks) => BattleArgument.Object(new[] { Pair("uid", uid), Pair("mode", mode), Pair("target", target),
            new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array(picks.Select(BattleArgument.String))) });
        static KeyValuePair<string, BattleArgument> Pair(string key, string? value) => new KeyValuePair<string, BattleArgument>(key, value == null ? BattleArgument.Null() : BattleArgument.String(value));
        static object? Get(object? node, string key) => node is IDictionary<string, object?> map && map.TryGetValue(key, out var value) ? value : null;
        static List<object?> ListOf(object? value) => value is IList list ? list.Cast<object?>().ToList() : new List<object?>();
        static bool Truthy(object? value) => value is bool flag ? flag : value != null;
        static long Integer(object? value) => value is int n ? n : value is long l ? l : 0;

        // CanonicalState omits "log"/"ev", so the Run log and the battle log/events are hashed separately for a complete comparison.
        static string Fingerprint(RunState state)
        {
            var graph = state.ToJsonGraph();
            string battle = graph["battle"] is Dictionary<string, object?> b ? CanonicalState.Hash(b["log"]) + CanonicalState.Hash(b["ev"]) : "-";
            return CanonicalState.Hash(new Dictionary<string, object?> { ["run"] = graph, ["memory"] = null }) + "/" + CanonicalState.Hash(graph["log"]) + "/" + battle;
        }
        static PointerEventData PointerAt(Button button)
        {
            var rect = button.GetComponent<RectTransform>();
            return new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)), button = PointerEventData.InputButton.Left };
        }
        static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
        static Button ButtonNamed(GameObject host, string name) => ActiveView(host).GetComponentsInChildren<Button>().Single(x => x.name == name);
        static Button? TopButton(Vector2 position)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position, button = PointerEventData.InputButton.Left }, hits);
            Assert.That(hits, Is.Not.Empty);
            return hits[0].gameObject.GetComponentInParent<Button>();
        }
        // A real pointer click at the button's own center; the button must be the top raycast hit there.
        static void Click(Button button)
        {
            var pointer = PointerAt(button); var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), button.name + " must be the top hit at its own center");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        static List<GameObject> OwnedViews(GameObject host)
        {
            var views = new List<GameObject>();
            foreach (Transform child in host.transform) if (child.name == MemoryForgeExpeditionClient.OwnedCanvas) views.Add(child.gameObject);
            return views;
        }
        // One owned view after a frame. The component's 2.6 s notice timer may have redrawn in that very frame, so a replaced view can
        // still be present but inactive until its deferred Destroy; it must never be active.
        static GameObject SingleView(GameObject host)
        {
            var views = OwnedViews(host); var active = views.Where(v => v.activeInHierarchy).ToList();
            Assert.That(active.Count, Is.EqualTo(1), "expected exactly one active owned expedition view after a frame");
            Assert.That(views.Count, Is.LessThanOrEqualTo(2), "replaced views must be destroyed, not accumulated");
            Assert.That(active[0].GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(active[0].GetComponent<CanvasScaler>(), Is.Not.Null);
            Assert.That(active[0].GetComponent<GraphicRaycaster>(), Is.Not.Null);
            return active[0];
        }
        // Within a frame a replaced view is inactive until its deferred Destroy; only one may be active.
        static GameObject ActiveView(GameObject host)
        {
            var active = OwnedViews(host).Where(v => v.activeInHierarchy).ToList();
            Assert.That(active.Count, Is.EqualTo(1), "expected exactly one active owned expedition view");
            return active[0];
        }
        // Labels must generate glyphs and must not be truncated (the font line-height regression of the battle practice view).
        static void AssertReadable(GameObject view, Func<string, bool> match, string what, int atLeast = 1)
        {
            var labels = view.GetComponentsInChildren<Text>(false).Where(t => match(t.text)).ToList();
            Assert.That(labels.Count, Is.GreaterThanOrEqualTo(atLeast), what + " label is missing");
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
