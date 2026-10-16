// P1-CARD-FLOW-TURN-BARRIER §14.3/§14.7/§16.4: Play Mode checks of the expedition view's turn-result summary and turn barrier over the
// real shared-core Run and the exported full content. Every command goes through the component's public methods or a real EventSystem
// raycast; an independent mirror Run receives the same public Core commands and must stay identical, so no Run state is injected,
// restored or set privately. Replay probes (a fresh Run.Create of the same public setup plus the recorded mirror commands) only choose
// which actual commands to send next; they never touch the client or the mirror. The summary is provisional text, and this is not
// Android/device, server, account or final-animation acceptance.
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
    public class ExpeditionTurnFlowPlayModeTests
    {
        const long Seed = 1234;
        const string Barrier = "TURN_SUMMARY_OPEN";
        const float ReservedSeconds = 0.3f; // §14.3 예약 해결 0.3초/장
        readonly List<GameObject> hosts = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator DestroyOwnedHosts()
        {
            foreach (var host in hosts) if (host != null) Object.Destroy(host);
            hosts.Clear();
            yield return null;
        }

        // One opened practice: the client on its host, its mirror, the recorded mirror commands for replay probes, the mirror's last
        // battle response and the number of actual battle events (each battle's begin events plus every battle command's events).
        sealed class Session
        {
            public GameObject Host = null!;
            public MemoryForgeExpeditionClient Client = null!;
            public CoreRun Mirror = null!;
            public RunData Data = null!, ProbeData = null!;
            public long Seed;
            public readonly List<Action<CoreRun>> History = new List<Action<CoreRun>>();
            public BattleResponse? Last;
            public int Events;
        }
        sealed class Reserved { public string Uid = "", Name = ""; }

        // The seed is set on a disabled host before the component's real OnEnable start; nothing else is injected.
        IEnumerator Open(Session result, long seed = Seed)
        {
            var host = new GameObject("TurnFlowTestHost"); hosts.Add(host);
            host.SetActive(false);
            var client = host.AddComponent<MemoryForgeExpeditionClient>(); client.Seed = seed; client.Length = "short";
            host.SetActive(true);
            yield return null;
            result.Host = host; result.Client = client; result.Seed = seed;
            result.Data = ClientRunContent.Load(); result.ProbeData = ClientRunContent.Load();
            result.Mirror = CoreRun.Create(result.Data, Setup(seed));
            AssertSame(result, "the view's Run must be exactly Create of the public PracticeSetup");
        }
        static RunSetup Setup(long seed) => MemoryForgeExpeditionClient.PracticeSetup(seed, "short", "practice-1-" + seed, new Dictionary<string, object?>(), 0);

        // Enter the first node, reserve the first opening-hand card legal for the next turn, then end that turn: the actual end resolves
        // that reservation at the next turn's start, so its summary holds a time-reservation resolution row.
        IEnumerator ReserveAndEnd(Session s, Reserved result)
        {
            yield return Open(s);
            string first = s.Mirror.Available()[0];
            Assert.That(Both(s, () => s.Client.EnterNode(first), EnterCore(s, first)), Is.True);
            var b = s.Mirror.State.Battle!; string enemy = b.Enemies.First(e => e.Alive).Uid;
            string? uid = b.Zones.Hand.FirstOrDefault(u => s.Mirror.BattleAllowedModes(u).Contains("next") && s.Mirror.BattleCheck(u, "next", enemy) == null);
            Assert.That(uid, Is.Not.Null, "seed 1234's opening hand offers a next-turn reservation");
            result.Uid = uid!; result.Name = s.Mirror.BattleEffectiveCard(uid!).Name;
            s.Client.SelectEnemy(enemy); s.Client.SelectCard(uid!); s.Client.SelectMode("next");
            Assert.That(Both(s, s.Client.ConfirmCard, BattleCore(s, "play", Play(uid!, "next", enemy))), Is.True);
            FinishAction(s.Client);
            Assert.That(Both(s, s.Client.EndTurn, BattleCore(s, "end", null)), Is.True);
            Assert.That(s.Mirror.State.Battle, Is.Not.Null, "seed 1234's first battle is still running after one end");
            Assert.That(s.Client.HasTurnSummary, Is.True, "an actual end opens the turn summary");
        }

        // Two real taps at one point of the end-turn button: the first is the one actual end; the second, a frame later, lands on the
        // barrier instead of the new turn's end-turn button. Continue then accepts the next actual end.
        [UnityTest]
        public IEnumerator TwoTapsOnEndTurnAtOnePointAreExactlyOneEndAndContinueThenAcceptsTheNextEnd()
        {
            var s = new Session(); yield return Open(s);
            string first = s.Mirror.Available()[0];
            Assert.That(Both(s, () => s.Client.EnterNode(first), EnterCore(s, first)), Is.True);
            yield return null; Canvas.ForceUpdateCanvases();
            var endTurn = ButtonNamed(s.Host, "EndTurn"); Vector2 point = Center(endTurn);
            Assert.That(TopHit(point).GetComponentInParent<Button>(), Is.SameAs(endTurn), "positive control: the point is the end-turn button");
            var start = s.Mirror.State.Battle!; int turn = start.Turn, alive = start.Enemies.Count(e => e.Alive);

            TapAt(point); MirrorOf(s, BattleCore(s, "end", null));
            Assert.That(s.Client.LastError, Is.Null);
            Assert.That(s.Client.HasTurnSummary, Is.True, "an actual end opens the turn summary");
            Assert.That(s.Mirror.State.Battle, Is.Not.Null, "seed 1234's first battle survives one end");
            var b = s.Mirror.State.Battle!;
            Assert.That(b.Turn, Is.EqualTo(turn + 1));
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(TopHit(point).GetComponentInParent<Button>(), Is.Null, "the repeated tap meets the barrier, not the new end-turn button");
            TapAt(point);
            AssertSame(s, "the repeated tap must not reach the Run: exactly one end");
            Assert.That(s.Client.Snapshot!.Battle!.Turn, Is.EqualTo(turn + 1));
            Assert.That(s.Client.HasTurnSummary, Is.True);

            // The summary states the committed end: the turn change, the previous intents, one row per actual event, the new turn.
            var rows = s.Client.TurnSummaryRows;
            Assert.That(rows[0].Kind, Is.EqualTo(BattleDigestKind.Summary));
            Assert.That(rows[0].Text, Is.EqualTo("턴 결과 · " + turn + "턴 → " + (turn + 1) + "턴"));
            Assert.That(rows.Count(r => r.Kind == BattleDigestKind.PreviousIntent), Is.EqualTo(alive), "one 직전 예고 row per enemy alive before the end");
            Assert.That(rows.Where(r => r.Kind == BattleDigestKind.PreviousIntent).All(r => r.Text.Contains("직전 예고")), Is.True);
            var eventRows = rows.Where(r => !Decoration(r)).ToList();
            Assert.That(eventRows.Count, Is.EqualTo(s.Last!.Events.Count), "one row per actual event of the end, in Core's order");
            Assert.That(rows.Single(r => r.Kind == BattleDigestKind.TurnStart).Text, Is.EqualTo((turn + 1) + "턴 시작 · 행동력 " + b.Ap));

            Click(ButtonNamed(s.Host, "SkipTurnSummary"));
            Assert.That(s.Client.TurnSummaryFullyShown, Is.True);
            var view = ActiveView(s.Host); Canvas.ForceUpdateCanvases();
            AssertReadable(view, t => t.Contains("· " + eventRows.Last().Text), "the last event row");
            AssertReadable(view, t => t == "모두 표시했어요. '계속'을 누르면 다음 행동을 할 수 있어요.", "summary status");
            string committed = Fingerprint(s.Client.Snapshot!);
            Click(ButtonNamed(s.Host, "ContinueTurnSummary"));
            Assert.That(s.Client.HasTurnSummary, Is.False);
            Assert.That(s.Client.LastError, Is.Null);
            Assert.That(Fingerprint(s.Client.Snapshot!), Is.EqualTo(committed), "skip and continue never reach the Run");

            yield return null; Canvas.ForceUpdateCanvases();
            var next = ButtonNamed(s.Host, "EndTurn");
            Click(next); MirrorOf(s, BattleCore(s, "end", null));
            Assert.That(s.Client.HasTurnSummary, Is.True, "after continue the next actual end is accepted");
            Dismiss(s.Client);
        }

        // Ending turns without cards until the party falls, each end tapped twice at the end-turn button's lower part. On the save screen
        // that point is "저장하지 않고 닫기"; the barrier must take the repeated tap so the defeat's memory choice stays open.
        [UnityTest]
        public IEnumerator LosingEndKeepsTheSaveScreenOpenAndItsRepeatedTapCannotCloseWithoutSaving()
        {
            var s = new Session(); yield return Open(s);
            string first = s.Mirror.Available()[0];
            Assert.That(Both(s, () => s.Client.EnterNode(first), EnterCore(s, first)), Is.True);
            Vector2 point = default; int hp = 0;
            for (int ends = 0; ends < 150 && s.Mirror.State.Phase == "BATTLE"; ends++)
            {
                yield return null; Canvas.ForceUpdateCanvases();
                var endTurn = ButtonNamed(s.Host, "EndTurn"); point = PointIn(endTurn, 25);
                Assert.That(TopHit(point).GetComponentInParent<Button>(), Is.SameAs(endTurn));
                hp = s.Mirror.State.Battle!.Hp;
                TapAt(point); MirrorOf(s, BattleCore(s, "end", null));
                Assert.That(s.Client.HasTurnSummary, Is.True);
                yield return null; Canvas.ForceUpdateCanvases();
                Assert.That(TopHit(point).GetComponentInParent<Button>(), Is.Null, "the repeated tap meets the barrier");
                TapAt(point);
                AssertSame(s, "the repeated tap must not reach the Run");
                if (s.Mirror.State.Phase != "BATTLE") break;
                Dismiss(s.Client);
            }
            if (s.Mirror.State.Phase == "BATTLE") Assert.Inconclusive("seed 1234's first battle did not defeat a party that only ends turns within 150 actual ends");
            Assert.That(s.Mirror.State.Phase, Is.EqualTo("DEFEAT"), "ending turns without cards can only continue or lose the battle");

            var dead = s.Client.Snapshot!;
            Assert.That(Get(dead.Pending, "type"), Is.EqualTo("save"));
            Assert.That(s.Client.ShowingResult, Is.False, "the repeated tap did not close the save screen");
            Assert.That(s.Client.SessionMemoryVersion, Is.EqualTo(0));
            var rows = s.Client.TurnSummaryRows;
            Assert.That(rows[0].Text, Does.EndWith(" · 전투 패배"));
            Assert.That(rows[1].Text, Is.EqualTo("파티 체력 " + hp + " → " + dead.Hp), "a finished battle's after-view is only its phase and the Run's HP");
            Assert.That(rows.Any(r => r.Kind == BattleDigestKind.EnemyState), Is.False, "no final enemy state is invented once the battle is gone");
            Assert.That(rows.Count(r => r.Kind == BattleDigestKind.BattleEnd && r.Text == "전투 종료 · 패배"), Is.EqualTo(1));
            Click(ButtonNamed(s.Host, "SkipTurnSummary")); Click(ButtonNamed(s.Host, "ContinueTurnSummary"));
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(TopHit(point).GetComponentInParent<Button>()?.name, Is.EqualTo("CloseWithoutSave"), "positive control: without the barrier that tap closes the save screen");
            AssertSame(s, "the positive control only reads the raycast");
        }

        // When one low enemy is left, replay probes look for an end that wins (alone, or after next-turn reservations). That end is tapped
        // twice at the end-turn button's lower part, which on the reward screen is "지도로": the rewards must stay.
        [UnityTest]
        public IEnumerator WinningEndKeepsTheRewardsAndItsRepeatedTapCannotProceed()
        {
            var s = new Session(); yield return Open(s);
            var content = s.Data.Memory.Battle; var events = s.Data.Events;
            for (int steps = 0; steps < 1500; steps++)
            {
                var state = s.Mirror.State;
                if (state.Phase == "VICTORY" || state.Phase == "DEFEAT") break;
                var plan = state.Phase == "BATTLE" && state.Battle != null ? WinningPlan(s, state.Battle) : null;
                if (plan == null) { Step(s, state, content, events); if (steps % 8 == 7) yield return null; continue; }

                foreach (var (uid, target) in plan)
                {
                    s.Client.SelectEnemy(target); s.Client.SelectCard(uid); s.Client.SelectMode("next");
                    Assert.That(Both(s, s.Client.ConfirmCard, BattleCore(s, "play", Play(uid, "next", target))), Is.True);
                    FinishAction(s.Client);
                }
                yield return null; Canvas.ForceUpdateCanvases();
                var endTurn = ButtonNamed(s.Host, "EndTurn"); Vector2 point = PointIn(endTurn, 25);
                Assert.That(TopHit(point).GetComponentInParent<Button>(), Is.SameAs(endTurn));
                TapAt(point); MirrorOf(s, BattleCore(s, "end", null));
                var won = s.Client.Snapshot!;
                Assert.That(won.Phase, Is.EqualTo("REWARD"), "the probed end wins the battle");
                string rewards = CanonicalState.Hash(won.Pending);
                var rows = s.Client.TurnSummaryRows;
                Assert.That(rows[0].Text, Does.EndWith(" · 전투 승리"));
                Assert.That(rows.Count(r => r.Kind == BattleDigestKind.BattleEnd && r.Text == "전투 종료 · 승리"), Is.EqualTo(1));
                yield return null; Canvas.ForceUpdateCanvases();
                Assert.That(TopHit(point).GetComponentInParent<Button>(), Is.Null, "the repeated tap meets the barrier");
                TapAt(point);
                AssertSame(s, "the repeated tap must not reach the Run");
                Assert.That(s.Client.Snapshot!.Phase, Is.EqualTo("REWARD"));
                Assert.That(CanonicalState.Hash(s.Client.Snapshot!.Pending), Is.EqualTo(rewards), "every unchosen reward is still offered");
                Click(ButtonNamed(s.Host, "SkipTurnSummary")); Click(ButtonNamed(s.Host, "ContinueTurnSummary"));
                yield return null; Canvas.ForceUpdateCanvases();
                Assert.That(TopHit(point).GetComponentInParent<Button>()?.name, Is.EqualTo("Proceed"), "positive control: without the barrier that tap proceeds and drops the rewards");
                Assert.That(s.Client.BattleLog, Has.Member("전투 종료 · 승리"), "the battle log keeps the winning end after the battle");
                AssertSame(s, "the positive control only reads the raycast");
                yield break;
            }
            Assert.Inconclusive("within 1500 actual commands of seed 1234 no probed end-turn victory was found for the reference party");
        }

        // While the summary is open every public command and selection is refused with the barrier code before it reaches the Run, and
        // real taps on the end-turn, hand, enemy and confirm points meet the blocker. After continue a selection is accepted again.
        [UnityTest]
        public IEnumerator EveryPublicCommandAndSelectionUnderTheTurnBarrierIsRefusedBeforeTheRun()
        {
            var s = new Session(); yield return Open(s);
            string first = s.Mirror.Available()[0];
            Assert.That(Both(s, () => s.Client.EnterNode(first), EnterCore(s, first)), Is.True);
            Assert.That(Both(s, s.Client.EndTurn, BattleCore(s, "end", null)), Is.True);
            Assert.That(s.Client.HasTurnSummary, Is.True);
            yield return null; Canvas.ForceUpdateCanvases();
            var b = s.Mirror.State.Battle!; string card = b.Zones.Hand[0], other = b.Zones.Hand.Last(), enemy = b.Enemies.First(e => e.Alive).Uid;
            Assert.That(b.Zones.Hand.Count, Is.GreaterThan(1));
            string before = Fingerprint(s.Client.Snapshot!);
            var selection = new[] { s.Client.SelectedCard, s.Client.SelectedMode, s.Client.Target };
            Assert.That(s.Client.SelectedCard, Is.Not.EqualTo(other));
            string? request = s.Client.LastRequestId; long version = s.Client.SessionMemoryVersion;
            var commands = new (string Name, Func<bool> Call)[]
            {
                ("EnterNode", () => s.Client.EnterNode(s.Mirror.Available().FirstOrDefault() ?? "none")), ("ConfirmCard", () => s.Client.ConfirmCard()),
                ("PlayCard", () => s.Client.PlayCard(card, "now")), ("EndTurn", () => s.Client.EndTurn()), ("UseUltimate", () => s.Client.UseUltimate("sera")),
                ("PickCard", () => s.Client.PickCard(card)), ("AutoPick", () => s.Client.AutoPick()), ("TakeCard", () => s.Client.TakeCard("sera_round")),
                ("SkipCard", () => s.Client.SkipCard()), ("TakeRelic", () => s.Client.TakeRelic("lucky_coin")), ("TakePart", () => s.Client.TakePart()),
                ("Evolve", () => s.Client.Evolve("k1", "sera_round")), ("Proceed", () => s.Client.Proceed()), ("RestHeal", () => s.Client.RestHeal()),
                ("RestUpgrade", () => s.Client.RestUpgrade("k1")), ("ForgeOffer", () => s.Client.ForgeOffer("k1")), ("ForgeApply", () => s.Client.ForgeApply("p")),
                ("ForgeRemove", () => s.Client.ForgeRemove("k1")), ("Buy", () => s.Client.Buy("cards", "sera_round")), ("BuyRemoval", () => s.Client.BuyRemoval("k1")),
                ("ChooseEvent", () => s.Client.ChooseEvent(0)), ("Archive", () => s.Client.Archive("return")), ("ApplyFoundPart", () => s.Client.ApplyFoundPart("p", "k1")),
                ("Assign", () => s.Client.Assign("k1", "sera")), ("Abandon", () => s.Client.Abandon()), ("ToggleChange", () => s.Client.ToggleChange("x")),
                ("CommitMemory", () => s.Client.CommitMemory()), ("CloseWithoutSave", () => s.Client.CloseWithoutSave()), ("Depart", () => s.Client.Depart())
            };
            foreach (var (name, call) in commands)
            {
                Assert.That(call(), Is.False, name + " must be refused under the turn barrier");
                Assert.That(s.Client.LastError, Is.EqualTo(Barrier), name);
                Assert.That(Fingerprint(s.Client.Snapshot!), Is.EqualTo(before), name + " reached the Run");
            }
            var selections = new (string Name, Action Call)[]
            {
                ("SelectCard", () => s.Client.SelectCard(other)), ("SelectMode", () => s.Client.SelectMode("next")), ("SelectEnemy", () => s.Client.SelectEnemy(enemy)),
                ("CancelPick", () => s.Client.CancelPick()), ("StartPractice", () => s.Client.StartPractice())
            };
            foreach (var (name, call) in selections)
            {
                call();
                Assert.That(s.Client.LastError, Is.EqualTo(Barrier), name);
                Assert.That(new[] { s.Client.SelectedCard, s.Client.SelectedMode, s.Client.Target }, Is.EqualTo(selection), name + " changed the prepared intent");
                Assert.That(Fingerprint(s.Client.Snapshot!), Is.EqualTo(before), name + " reached the Run");
            }
            Assert.That((s.Client.LastRequestId, s.Client.SessionMemoryVersion, s.Client.PickerOpen, s.Client.ShowingResult, s.Client.HasTurnSummary),
                Is.EqualTo((request, version, false, false, true)), "no request, memory version, picker or close came from a refused call");
            AssertSame(s, "nothing reached the Run");

            var points = new[] { "EndTurn", "Hand:" + card, "Enemy:" + enemy, "ConfirmCard" };
            foreach (string name in points)
            {
                var button = ActiveView(s.Host).GetComponentsInChildren<Button>().FirstOrDefault(x => x.name == name);
                if (button == null) continue;
                Vector2 point = Center(button);
                Assert.That(TopHit(point).GetComponentInParent<Button>(), Is.Null, name + " must be covered by the barrier");
                TapAt(point);
                Assert.That(Fingerprint(s.Client.Snapshot!), Is.EqualTo(before), "a tap on " + name + " reached the Run");
                Assert.That(new[] { s.Client.SelectedCard, s.Client.SelectedMode, s.Client.Target }, Is.EqualTo(selection));
            }

            Assert.That(s.Client.ContinueTurnSummary(), Is.True);
            Assert.That(s.Client.LastError, Is.Null);
            s.Client.SelectCard(other);
            Assert.That(s.Client.LastError, Is.Null);
            Assert.That(s.Client.SelectedCard, Is.EqualTo(other), "positive control: after continue the selection is accepted");
            Assert.That(Fingerprint(s.Client.Snapshot!), Is.EqualTo(before));
        }

        // Normal pacing, skip, disable/enable and focus/pause resume of the same committed end give the same committed state, and none
        // leaves a sticky barrier. Each reservation row appears on the approved 0.3 s step; the timer never reaches the Run.
        [UnityTest]
        public IEnumerator ReservationRowsRevealOnTheApprovedTimerAndSkipDisableOrResumeLeaveTheSameCommittedState()
        {
            var a = new Session(); var reserved = new Reserved(); yield return ReserveAndEnd(a, reserved);
            float opened = Time.realtimeSinceStartup;
            var rows = a.Client.TurnSummaryRows; int steps = rows.Count(r => r.IsReservedResolution);
            Assert.That(steps, Is.EqualTo(a.Last!.Events.Count(e => e.E == "resolved" && !string.IsNullOrEmpty(e.Reserved))), "one stepped row per actual time-reservation resolution");
            var row = rows.Single(r => r.IsReservedResolution && r.Text.StartsWith("예약 해결 · " + reserved.Name + " · 다음 턴 예약", StringComparison.Ordinal));
            Assert.That(rows[1].Text, Does.Contain("시간 예약 1장 → 0장"), "the summary counts Core's time reservations");
            Assert.That(a.Client.TurnSummaryFullyShown, Is.False, "the reservation row waits for its 0.3 s step");
            string committed = Fingerprint(a.Client.Snapshot!);
            yield return null; Canvas.ForceUpdateCanvases();
            var view = ActiveView(a.Host);
            while (!a.Client.TurnSummaryFullyShown && Time.realtimeSinceStartup < opened + 5f)
            {
                Assert.That(Fingerprint(a.Client.Snapshot!), Is.EqualTo(committed), "the timer never reaches the Run");
                yield return null;
            }
            Assert.That(a.Client.TurnSummaryFullyShown, Is.True, "the reveal finishes on the real timer");
            Assert.That(Time.realtimeSinceStartup - opened, Is.GreaterThanOrEqualTo(ReservedSeconds * steps - 0.02f), "each reservation row waits its 0.3 s");
            Assert.That(ActiveView(a.Host), Is.SameAs(view), "reveal steps rewrite labels in place; the view is not rebuilt");
            Canvas.ForceUpdateCanvases();
            AssertReadable(view, t => t.Contains("· " + row.Text), "the reservation row");
            Click(ButtonNamed(a.Host, "ContinueTurnSummary"));
            Assert.That(a.Client.HasTurnSummary, Is.False);
            Assert.That(Fingerprint(a.Client.Snapshot!), Is.EqualTo(committed));

            // Skip: every row at once; the same committed end gives the same rows and state.
            var b = new Session(); yield return ReserveAndEnd(b, new Reserved());
            yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(b.Host, "SkipTurnSummary"));
            Assert.That(b.Client.TurnSummaryFullyShown, Is.True, "skip shows every row at once");
            Assert.That(b.Client.TurnSummaryRows.Select(r => (r.Kind, r.Text, r.Provenance)), Is.EqualTo(rows.Select(r => (r.Kind, r.Text, r.Provenance))));
            Click(ButtonNamed(b.Host, "ContinueTurnSummary"));
            Assert.That(Fingerprint(b.Client.Snapshot!), Is.EqualTo(committed), "pacing does not change the committed state");

            // Disable/enable: the cosmetic summary is dropped, the actual Run redrawn, and the next actual end is accepted.
            var c = new Session(); yield return ReserveAndEnd(c, new Reserved());
            c.Host.SetActive(false);
            yield return null;
            Assert.That(c.Client.HasTurnSummary, Is.False, "disabling drops the cosmetic summary and its timer");
            c.Host.SetActive(true);
            yield return null;
            SingleView(c.Host);
            Assert.That(c.Client.HasTurnSummary, Is.False, "re-enabling redraws the Run without a sticky barrier");
            Assert.That(Fingerprint(c.Client.Snapshot!), Is.EqualTo(committed));
            Assert.That(Both(c, c.Client.EndTurn, BattleCore(c, "end", null)), Is.True, "the next actual end is accepted");
            Dismiss(c.Client);

            // Focus/pause resume: nothing is stale, so the open summary and the view stay; continue then releases the barrier.
            var d = new Session(); yield return ReserveAndEnd(d, new Reserved());
            yield return null; Canvas.ForceUpdateCanvases();
            var shown = ActiveView(d.Host);
            d.Host.SendMessage("OnApplicationPause", false); d.Host.SendMessage("OnApplicationFocus", true);
            Assert.That(d.Client.HasTurnSummary, Is.True, "resuming keeps the open summary: it is not stale");
            Assert.That(ActiveView(d.Host), Is.SameAs(shown), "nothing stale, so resuming does not rebuild the view");
            Assert.That(Fingerprint(d.Client.Snapshot!), Is.EqualTo(committed));
            Assert.That(d.Client.SkipTurnSummary(), Is.True);
            Assert.That(d.Client.ContinueTurnSummary(), Is.True);
            Assert.That(Both(d, d.Client.EndTurn, BattleCore(d, "end", null)), Is.True, "the next actual end is accepted after resume");
            Dismiss(d.Client);
        }

        // A press held on continue while a reservation row is still pending: the reveal rewrites labels only, so the same button, canvas
        // and EventSystem keep the press, and the release is exactly one continue.
        [UnityTest]
        public IEnumerator AHeldContinueSurvivesTheRevealStepsAndItsReleaseClosesTheSummaryOnce()
        {
            var s = new Session(); yield return ReserveAndEnd(s, new Reserved());
            Assert.That(s.Client.TurnSummaryFullyShown, Is.False);
            yield return null; Canvas.ForceUpdateCanvases();
            var view = ActiveView(s.Host); var events = EventSystem.current; var cont = ButtonNamed(s.Host, "ContinueTurnSummary");
            Assert.That(events, Is.Not.Null);
            string committed = Fingerprint(s.Client.Snapshot!);
            var pointer = new PointerEventData(events) { position = Center(cont), button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); events.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(cont), "continue must be the top hit at its own center");
            pointer.pointerPressRaycast = hits[0]; pointer.pointerCurrentRaycast = hits[0]; pointer.pressPosition = pointer.position; pointer.eligibleForClick = true;
            pointer.pointerPress = ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerDownHandler) ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
            pointer.rawPointerPress = hits[0].gameObject;
            Assert.That(pointer.pointerPress, Is.SameAs(cont.gameObject), "continue takes its own press");
            Assert.That(s.Client.TurnSummaryFullyShown, Is.False, "the press must start while a reservation row is still pending");

            float deadline = Time.realtimeSinceStartup + 5f;
            while (!s.Client.TurnSummaryFullyShown && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null; // a replaced view would linger inactive until the end of its frame
            Assert.That(s.Client.TurnSummaryFullyShown, Is.True, "the reveal finished during the held press");
            Assert.That(OwnedViews(s.Host), Is.EqualTo(new[] { view }), "reveal steps must not rebuild the view under a held press");
            Assert.That(cont != null && cont.IsActive() && cont.interactable, Is.True, "the held continue is the same live, interactable button");
            Assert.That(EventSystem.current, Is.SameAs(events), "the same EventSystem keeps the press");
            Assert.That(s.Client.HasTurnSummary, Is.True, "a held press alone closes nothing");
            Assert.That(Fingerprint(s.Client.Snapshot!), Is.EqualTo(committed));

            events.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject), Is.SameAs(pointer.pointerPress), "the release lands on the pressed continue");
            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(s.Client.HasTurnSummary, Is.False, "the release is the one continue");
            Assert.That(Fingerprint(s.Client.Snapshot!), Is.EqualTo(committed), "continue never reaches the Run");
        }

        // Beneath the open summary the timeline is exactly Core's time-reservation slots: the resolved next-turn card is gone, a card
        // reserved for the turn after still waits, and no summary row became a reservation.
        [UnityTest]
        public IEnumerator TimeReservationSlotsBeneathTheSummaryAreOnlyCoresReservations()
        {
            var s = new Session(); yield return Open(s);
            string first = s.Mirror.Available()[0];
            Assert.That(Both(s, () => s.Client.EnterNode(first), EnterCore(s, first)), Is.True);
            var b = s.Mirror.State.Battle!; string enemy = b.Enemies.First(e => e.Alive).Uid;
            string? next = b.Zones.Hand.FirstOrDefault(u => s.Mirror.BattleAllowedModes(u).Contains("next") && s.Mirror.BattleCheck(u, "next", enemy) == null);
            Assert.That(next, Is.Not.Null, "seed 1234's opening hand offers a next-turn reservation");
            s.Client.SelectEnemy(enemy); s.Client.SelectCard(next!); s.Client.SelectMode("next");
            Assert.That(Both(s, s.Client.ConfirmCard, BattleCore(s, "play", Play(next!, "next", enemy))), Is.True);
            FinishAction(s.Client);
            var hand = s.Mirror.State.Battle!.Zones.Hand;
            string? later = hand.FirstOrDefault(u => s.Mirror.BattleAllowedModes(u).Contains("after") && s.Mirror.BattleCheck(u, "after", enemy) == null);
            if (later != null)
            {
                s.Client.SelectEnemy(enemy); s.Client.SelectCard(later); s.Client.SelectMode("after");
                Assert.That(Both(s, s.Client.ConfirmCard, BattleCore(s, "play", Play(later, "after", enemy))), Is.True);
                FinishAction(s.Client);
            }
            int reservedBefore = s.Mirror.State.Battle!.Reservations.Count;
            Assert.That(Both(s, s.Client.EndTurn, BattleCore(s, "end", null)), Is.True);
            yield return null; Canvas.ForceUpdateCanvases();

            var live = s.Mirror.State.Battle!; var rules = s.Data.Memory.Battle.Rules;
            var cells = MemoryForgeBattleClient.TimelineCells(live, rules);
            var expected = cells.Select(cell => (cell.Group == "next" ? "다음" : "다다음") + "  " + (cell.Kind == "card" ? s.Mirror.BattleEffectiveCard(cell.Uid!).Name
                : cell.Kind == "empty" ? "빈 칸" : cell.Kind == "sealed" ? "봉인" : "-")).ToList();
            var drawn = ActiveView(s.Host).GetComponentsInChildren<Image>().Where(i => i.name == "ReservationSlot").Select(i => i.GetComponentInChildren<Text>().text).ToList();
            Assert.That(drawn, Is.EqualTo(expected), "the timeline beneath the summary is Core's time-reservation slots only");
            Assert.That(cells.Count(cell => cell.Kind == "card"), Is.EqualTo(live.Reservations.Count));
            Assert.That(live.Reservations.Select(r => r.Uid), Is.EqualTo(later == null ? new string[0] : new[] { later }), "the next-turn card resolved; only the later one waits");
            Assert.That(s.Client.TurnSummaryRows.Count(r => r.IsReservedResolution), Is.EqualTo(1), "one actual resolution row");
            Assert.That(s.Client.TurnSummaryRows[1].Text, Does.Contain("시간 예약 " + reservedBefore + "장 → " + live.Reservations.Count + "장"));
            Assert.That(s.Client.HasTurnSummary, Is.True);
            Dismiss(s.Client);
            Assert.That(s.Client.Snapshot!.Battle!.Reservations.Select(r => r.Uid), Is.EqualTo(live.Reservations.Select(r => r.Uid)), "closing the summary adds or removes no reservation");
        }

        // No stale pick sheet lies beneath an actual end's summary. P1-CARD-FLOW-PICKER-GUARD: an open sheet is one unsent play, so the end
        // that used to retire it is now refused beneath it (the sheet stays, nothing is spent, a real tap at the end point meets the sheet).
        // The available path is the explicit cancel: the actual end then commits with no sheet beneath its summary, none comes back after
        // continue, and the cancelled sheet's old buttons and picks reach nothing. A sheet whose state changes beneath it is retired
        // unsent in ExpeditionPickerGuardPlayModeTests.
        [UnityTest]
        public IEnumerator APickSheetPreparedBeforeAnEndDoesNotSurviveBeneathTheSummary()
        {
            var s = new Session(); yield return Open(s);
            var content = s.Data.Memory.Battle; var events = s.Data.Events;
            string? card = null, enemy = null;
            for (int steps = 0; steps < 3000; steps++)
            {
                var state = s.Mirror.State;
                if (state.Phase == "VICTORY" || state.Phase == "DEFEAT") break;
                if (state.Phase == "BATTLE" && state.Battle != null && state.Battle.Phase == "PLAYER")
                {
                    var b = state.Battle; string target = b.Enemies.First(e => e.Alive).Uid;
                    card = b.Zones.Hand.FirstOrDefault(u => s.Mirror.BattleAllowedModes(u).Contains("now") && s.Mirror.BattleCheck(u, "now", target) == null
                        && MemoryForgeExpeditionClient.ChoiceRequests(s.Mirror.BattleEffectiveCard(u), "now", content).Count != 0);
                    if (card != null) { enemy = target; break; }
                }
                Step(s, state, content, events);
                if (steps % 8 == 7) yield return null;
            }
            Assert.That(card, Is.Not.Null, "a card with picks must reach a playable hand within the bounded actual commands");
            s.Client.SelectEnemy(enemy!); s.Client.SelectCard(card!); s.Client.SelectMode("now");
            string before = Fingerprint(s.Client.Snapshot!);
            Assert.That(s.Client.ConfirmCard(), Is.False);
            Assert.That(s.Client.PickerOpen, Is.True, "the pick sheet is prepared; nothing is spent");
            Assert.That(Fingerprint(s.Client.Snapshot!), Is.EqualTo(before));
            yield return null; Canvas.ForceUpdateCanvases();
            var drawn = ActiveView(s.Host).GetComponentsInChildren<Button>().FirstOrDefault(x => x.name.StartsWith("Pick:", StringComparison.Ordinal));
            Assert.That(drawn, Is.Not.Null, "the prepared sheet draws a candidate");
            var oldPick = drawn!; var oldAuto = ButtonNamed(s.Host, "PickAuto");
            string candidate = oldPick.name.Substring("Pick:".Length);

            // The end beneath the open sheet is refused before the Run; the sheet stays and covers the end button.
            Assert.That(s.Client.EndTurn(), Is.False, "an end is refused while the sheet holds an unsent play");
            Assert.That(s.Client.LastError, Is.EqualTo("PICKER_OPEN"));
            Assert.That((s.Client.PickerOpen, s.Client.HasTurnSummary), Is.EqualTo((true, false)));
            AssertSame(s, "the refused end reached nothing");
            var end = ButtonNamed(s.Host, "EndTurn"); Vector2 point = Center(end);
            var top = TopHit(point).GetComponentInParent<Button>();
            Assert.That(top, Is.Not.SameAs(end), "the sheet covers the end button");
            if (top == null) TapAt(point);
            Assert.That(s.Client.PickerOpen, Is.True);
            AssertSame(s, "a tap at the end point reached nothing");

            // The available path: explicit cancel keeps the ordinary selection, then the actual end commits.
            s.Client.CancelPick();
            Assert.That((s.Client.PickerOpen, s.Client.LastError), Is.EqualTo((false, (string?)null)));
            Assert.That(new[] { s.Client.SelectedCard, s.Client.SelectedMode, s.Client.Target }, Is.EqualTo(new[] { card, "now", enemy }), "cancel keeps the ordinary selection");
            AssertSame(s, "cancel spends nothing");
            Assert.That(Both(s, s.Client.EndTurn, BattleCore(s, "end", null)), Is.True);
            Assert.That(s.Client.PickerOpen, Is.False, "no pick sheet survives into the committed end");
            Assert.That(s.Client.HasTurnSummary, Is.True);
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(ActiveView(s.Host).GetComponentsInChildren<Image>().Any(i => i.name == "BattlePicks"), Is.False, "no pick sheet beneath the summary");

            // The cancelled sheet's buttons and picks reach nothing, under the summary and after continue.
            oldPick.onClick.Invoke(); oldAuto.onClick.Invoke();
            Assert.That(s.Client.PickCard(candidate), Is.False);
            Assert.That(s.Client.LastError, Is.EqualTo(Barrier));
            Assert.That(s.Client.AutoPick(), Is.False);
            Assert.That(s.Client.LastError, Is.EqualTo(Barrier));
            AssertSame(s, "old pick input under the summary reached nothing");
            Dismiss(s.Client);
            Assert.That(s.Client.PickerOpen, Is.False);
            oldPick.onClick.Invoke(); oldAuto.onClick.Invoke();
            Assert.That(s.Client.PickCard(candidate) || s.Client.AutoPick(), Is.False);
            Assert.That(s.Client.PickerOpen, Is.False);
            AssertSame(s, "the cancelled sheet's picks never reach the Run");
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(ActiveView(s.Host).GetComponentsInChildren<Image>().Any(i => i.name == "BattlePicks"), Is.False, "the sheet does not come back after continue");
        }

        // The log sheet's battle part is the recent 40 event rows of the digest (no summary decoration), kept after the battle has left the
        // Run on the reward or save screen, and reset by the next departure.
        [UnityTest]
        public IEnumerator BattleLogKeepsTheRecent40EventRowsThroughTheBattleEndAndResetsOnTheNextDeparture()
        {
            var s = new Session(); yield return Open(s);
            var content = s.Data.Memory.Battle; var events = s.Data.Events;
            bool entered = false;
            for (int steps = 0; steps < 2000; steps++)
            {
                var state = s.Mirror.State;
                if (state.Phase == "BATTLE") entered = true;
                else if (entered) break;
                Step(s, state, content, events);
                if (steps % 8 == 7) yield return null;
            }
            var done = s.Client.Snapshot!;
            Assert.That(done.Phase, Is.EqualTo("REWARD").Or.EqualTo("DEFEAT"), "the first actual battle must end within the bounded driver");
            Assert.That(done.Battle, Is.Null, "the finished battle has left the Run");
            var log = s.Client.BattleLog;
            Assert.That(s.Events, Is.GreaterThan(0));
            Assert.That(log.Count, Is.EqualTo(Math.Min(40, s.Events)), "one log row per actual battle event; the recent 40 are kept after the battle");
            Assert.That(log.Any(t => t.StartsWith("턴 결과", StringComparison.Ordinal) || t.Contains("직전 예고") || t.StartsWith("결과 · ", StringComparison.Ordinal)), Is.False,
                "summary decoration is not logged as an event");
            Assert.That(log, Has.Member(done.Phase == "REWARD" ? "전투 종료 · 승리" : "전투 종료 · 패배"), "the battle's own end event is kept");
            yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(s.Host, "LogButton"));
            yield return null; Canvas.ForceUpdateCanvases();
            var view = ActiveView(s.Host);
            AssertReadable(view, t => t == "전투 기록 · 최근 " + log.Count + "개 (최대 40개)", "battle log heading");
            AssertReadable(view, t => t == string.Join("\n", log), "the retained battle log rows");
            Click(ButtonNamed(s.Host, "CloseLog"));
            AssertSame(s, "reading the log never reaches the Run");

            Assert.That(done.Phase == "REWARD" ? s.Client.Abandon() : s.Client.CloseWithoutSave(), Is.True, s.Client.LastError);
            Assert.That(s.Client.BattleLog, Is.EqualTo(log), "closing the expedition keeps its log until the next departure");
            Assert.That(s.Client.Depart(), Is.True, s.Client.LastError);
            Assert.That(s.Client.BattleLog, Is.Empty, "a new departure starts a new battle log");
        }

        // ───────── drivers: one public command per call, mirrored on the Core Run ─────────
        static Action<CoreRun> EnterCore(Session s, string id) => r =>
        {
            r.Enter(id);
            if (ReferenceEquals(r, s.Mirror) && r.State.Phase == "BATTLE" && r.State.Battle != null) s.Events += r.State.Battle.Events.Count;
        };
        static Action<CoreRun> BattleCore(Session s, string kind, BattleArgument? args) => r =>
        {
            var response = r.BattleCommand(kind, args);
            if (ReferenceEquals(r, s.Mirror)) { s.Last = response; s.Events += response.Events.Count; }
        };
        static bool Both(Session s, Func<bool> view, Action<CoreRun> core)
        {
            bool ok = view(); string? code = null;
            try { core(s.Mirror); }
            catch (RuleException error) { code = error.Code; }
            s.History.Add(core);
            Assert.That(ok, Is.EqualTo(code == null), "view and Core disagree: view " + s.Client.LastError + " / Core " + code);
            if (!ok) Assert.That(s.Client.LastError, Is.EqualTo(code));
            AssertSame(s, "the view must issue exactly the actual Core command");
            return ok;
        }
        // The Core side of a command a real tap already sent to the view.
        static void MirrorOf(Session s, Action<CoreRun> core)
        {
            core(s.Mirror); s.History.Add(core);
            AssertSame(s, "the tap must be exactly the actual Core command");
        }
        static void AssertSame(Session s, string message) => Assert.That(Fingerprint(s.Client.Snapshot!), Is.EqualTo(Fingerprint(s.Mirror.State)), message);
        // Drivers not about the summary close it explicitly; skip and continue are display only and leave the Run as it was.
        static void Dismiss(MemoryForgeExpeditionClient client)
        {
            Assert.That(client.HasTurnSummary, Is.True, "an actual end opens the turn summary");
            string before = Fingerprint(client.Snapshot!);
            Assert.That(client.SkipTurnSummary(), Is.True);
            Assert.That(client.ContinueTurnSummary(), Is.True);
            Assert.That(client.HasTurnSummary, Is.False);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "skip and continue never reach the Run");
        }
        // P1-CARD-FLOW-ACTION-PRESENTATION: an actual play or ultimate opens its display, which refuses the next command. Drivers not about
        // it finish it explicitly right after that command (skip; a repeat is inert); both are display only and leave the Run as it was.
        static void FinishAction(MemoryForgeExpeditionClient client)
        {
            Assert.That(client.HasActionPresentation, Is.True, "an actual play or ultimate opens its display");
            string before = Fingerprint(client.Snapshot!);
            Assert.That(client.SkipActionPresentation(), Is.True);
            Assert.That(client.HasActionPresentation, Is.False);
            Assert.That(client.SkipActionPresentation(), Is.False, "a repeated skip is inert");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "finishing the display never reaches the Run");
        }
        static bool Decoration(BattleDigestEntry row) => row.Kind == BattleDigestKind.Summary || row.Kind == BattleDigestKind.PreviousIntent || row.Kind == BattleDigestKind.EnemyState;

        // A fresh Run from the same public setup with every recorded mirror command; it starts exactly where the mirror is.
        static CoreRun Replay(Session s)
        {
            var probe = CoreRun.Create(s.ProbeData, Setup(s.Seed));
            foreach (var command in s.History) { try { command(probe); } catch (RuleException) { } }
            Assert.That(Fingerprint(probe.State), Is.EqualTo(Fingerprint(s.Mirror.State)), "a replay probe starts from the mirror's exact state");
            return probe;
        }
        // With one living enemy at 40 HP or less: the end alone, or after next-turn reservations of every hand card the probe accepts, in
        // hand order. Returns the reservations to send (empty: just end) when that probed end wins, else null.
        static List<(string Uid, string Target)>? WinningPlan(Session s, BattleState b)
        {
            if (b.Phase != "PLAYER") return null;
            var alive = b.Enemies.Where(e => e.Alive).ToList();
            if (alive.Count != 1 || alive[0].Hp > 40) return null;
            string enemy = alive[0].Uid;
            var plan = new List<(string Uid, string Target)>();
            if (EndWins(Replay(s))) return plan;
            var probe = Replay(s);
            foreach (string uid in b.Zones.Hand)
            {
                if (!probe.BattleAllowedModes(uid).Contains("next") || probe.BattleCheck(uid, "next", enemy) != null) continue;
                try { probe.BattleCommand("play", Play(uid, "next", enemy)); plan.Add((uid, enemy)); }
                catch (RuleException) { return null; }
            }
            return plan.Count != 0 && EndWins(probe) ? plan : null;
        }
        static bool EndWins(CoreRun probe)
        {
            try { probe.BattleCommand("end"); }
            catch (RuleException) { return false; }
            return probe.State.Phase == "REWARD";
        }

        // The existing bounded driver order, with an actual end's summary closed explicitly.
        static void Step(Session s, RunState state, BattleData content, List<RunEventDefinition> events)
        {
            var client = s.Client; var p = state.Pending;
            switch (state.Phase)
            {
                case "MAP": { string id = s.Mirror.Available()[0]; Both(s, () => client.EnterNode(id), EnterCore(s, id)); return; }
                case "BATTLE": BattleStep(s, state.Battle!, content); return;
                case "REWARD":
                    {
                        var evolutions = ListOf(Get(p, "evolutions")); var cards = ListOf(Get(p, "cards")); var relics = ListOf(Get(p, "relic"));
                        if (evolutions.Count != 0)
                        {
                            string key = (string)Get(evolutions[0], "key")!, branch = (string)ListOf(Get(evolutions[0], "options"))[0]!;
                            if (Both(s, () => client.Evolve(key, branch), r => r.Evolve(key, branch))) return;
                        }
                        else if (cards.Count != 0) { string id = (string)cards[0]!; if (Both(s, () => client.TakeCard(id), r => r.TakeCard(id))) return; }
                        else if (relics.Count != 0) { string id = (string)relics[0]!; if (Both(s, () => client.TakeRelic(id), r => r.TakeRelic(id))) return; }
                        else if (Get(p, "part") is string) { if (Both(s, client.TakePart, r => r.TakePart())) return; }
                        Both(s, client.Proceed, r => r.Proceed());
                        return;
                    }
                case "REST":
                    if (Integer(Get(p, "uses")) > 0)
                    {
                        var plain = state.Deck.FirstOrDefault(c => !c.Weapon && !Truthy(Get(c.ToJsonGraph(), "plus")));
                        if (state.Hp * 10 < state.MaxHp * 6 || plain == null) { if (Both(s, client.RestHeal, r => r.Rest("heal"))) return; }
                        else { string key = plain.Key!; if (Both(s, () => client.RestUpgrade(key), r => r.Rest("upgrade", key))) return; }
                    }
                    Both(s, client.Proceed, r => r.Proceed());
                    return;
                case "FORGE":
                    {
                        var offers = Get(p, "offers"); var parts = ListOf(Get(offers, "parts"));
                        if (parts.Count != 0) { string id = (string)parts[0]!; if (Both(s, () => client.ForgeApply(id), r => r.ForgeApply(id))) return; }
                        else if (offers == null && Integer(Get(p, "uses")) > 0)
                        {
                            string key = state.Deck.First(c => !c.Weapon).Key!;
                            if (Both(s, () => client.ForgeOffer(key), r => r.ForgeOffer(key))) return;
                        }
                        Both(s, client.Proceed, r => r.Proceed());
                        return;
                    }
                case "SHOP":
                    {
                        var cards = ListOf(Get(p, "cards"));
                        bool bought = cards.Concat(ListOf(Get(p, "parts"))).Concat(ListOf(Get(p, "relics"))).Any(x => Truthy(Get(x, "sold")));
                        var item = cards.FirstOrDefault(x => !Truthy(Get(x, "sold")) && Integer(Get(x, "price")) <= state.Coins);
                        if (!bought && item != null) { string id = (string)Get(item, "id")!; if (Both(s, () => client.Buy("cards", id), r => r.Buy("cards", id))) return; }
                        Both(s, client.Proceed, r => r.Proceed());
                        return;
                    }
                case "EVENT":
                    {
                        if (Truthy(Get(p, "done"))) { Both(s, client.Proceed, r => r.Proceed()); return; }
                        string eventId = (string)Get(p, "id")!; var definition = events.Last(e => e.Id == eventId);
                        for (int i = 0; i < definition.Choices.Count; i++)
                        {
                            var fx = definition.Choices[i].Effects; int index = i;
                            if (definition.Choices[i].RequiredCoins.GetValueOrDefault() > state.Coins) continue;
                            int count = fx.CardCopy.GetValueOrDefault() != 0 ? 1 : fx.CardRemove.GetValueOrDefault();
                            var keys = count > 0 ? state.Deck.Where(c => !c.Weapon).Select(c => c.Key!).Take(count).ToList() : null;
                            if (Both(s, () => client.ChooseEvent(index, keys), r => r.EventChoose(index, keys?.Cast<object?>().ToList()))) return;
                        }
                        Assert.Fail("no choice of event " + eventId + " was accepted");
                        return;
                    }
                case "ARCHIVE": Both(s, () => client.Archive("return"), r => r.Archive("return")); return;
                default: Assert.Fail("unexpected expedition phase " + state.Phase); return;
            }
        }
        static void BattleStep(Session s, BattleState b, BattleData content)
        {
            var client = s.Client; var mirror = s.Mirror;
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
                    Assert.That(client.ConfirmCard(), Is.False);
                    Assert.That(client.PickerOpen, Is.True);
                    if (Both(s, client.AutoPick, BattleCore(s, "play", Play(card, "now", enemy)))) { FinishAction(client); return; }
                }
                else if (Both(s, client.ConfirmCard, BattleCore(s, "play", Play(card, "now", enemy)))) { FinishAction(client); return; }
            }
            var ready = b.Actors.FirstOrDefault(a => a.Gauge >= content.Rules.GaugeMax);
            if (ready != null)
            {
                client.SelectEnemy(enemy);
                var args = BattleArgument.Object(new[] { Pair("actor", ready.Id), Pair("target", enemy) });
                if (Both(s, () => client.UseUltimate(ready.Id), BattleCore(s, "ult", args))) { FinishAction(client); return; }
            }
            if (Both(s, client.EndTurn, BattleCore(s, "end", null))) Dismiss(client);
        }

        // ───────── small copies of the expedition tests' helpers ─────────
        static BattleArgument Play(string uid, string mode, string? target) => BattleArgument.Object(new[] { Pair("uid", uid), Pair("mode", mode), Pair("target", target),
            new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array(new BattleArgument[0])) });
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
        // A screen point inside a button: its horizontal center, `fromBottom` reference pixels above its bottom edge.
        static Vector2 PointIn(Button button, float fromBottom)
        {
            var rect = button.GetComponent<RectTransform>(); var r = rect.rect;
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3(r.center.x, r.yMin + fromBottom, 0)));
        }
        static Vector2 Center(Button button) => PointIn(button, button.GetComponent<RectTransform>().rect.height / 2);
        static GameObject TopHit(Vector2 point)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left }, hits);
            Assert.That(hits, Is.Not.Empty, "something must take a tap at " + point);
            return hits[0].gameObject;
        }
        // A real tap: a click on the top raycast hit at the point, as the input module delivers it.
        static void TapAt(Vector2 point)
        {
            var pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
            ExecuteEvents.ExecuteHierarchy(TopHit(point), pointer, ExecuteEvents.pointerClickHandler);
        }
        static void Click(Button button)
        {
            Vector2 point = Center(button);
            Assert.That(TopHit(point).GetComponentInParent<Button>(), Is.SameAs(button), button.name + " must be the top hit at its own center");
            TapAt(point);
        }
        static Button ButtonNamed(GameObject host, string name) => ActiveView(host).GetComponentsInChildren<Button>().Single(x => x.name == name);
        static List<GameObject> OwnedViews(GameObject host)
        {
            var views = new List<GameObject>();
            foreach (Transform child in host.transform) if (child.name == MemoryForgeExpeditionClient.OwnedCanvas) views.Add(child.gameObject);
            return views;
        }
        static GameObject SingleView(GameObject host)
        {
            var views = OwnedViews(host); var active = views.Where(v => v.activeInHierarchy).ToList();
            Assert.That(active.Count, Is.EqualTo(1), "expected exactly one active owned expedition view after a frame");
            Assert.That(views.Count, Is.LessThanOrEqualTo(2), "replaced views must be destroyed, not accumulated");
            return active[0];
        }
        static GameObject ActiveView(GameObject host)
        {
            var active = OwnedViews(host).Where(v => v.activeInHierarchy).ToList();
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
