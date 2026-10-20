#if UNITY_5_3_OR_NEWER
// P1-SERVER-BATTLE-CONTROLS-TESTS §5.10/§14.3/§14.7/§16.4: view checks of the server battle's ultimates, action pace and card details in
// MemoryForgeServerPracticeClient (UltAsync, ActionPace/SetActionPace, CardDetailsOpen/OpenCardDetails/CloseCardDetails; buttons Ult:<actor>,
// Pace:<pace>, OpenCardDetails/CloseCardDetails; label CardDetailsBody in scroll CardDetailsScroll) over the shared PROTOCOL FIXTURE of
// PracticeServerSessionPlayModeTests (World, post, loopback host, View/Settled/Until/Refused/Sent/RunJson/QueryJson, Drawn/DrawnButton;
// nothing is copied). The public battle here (three actors with gauge/gaugeMax, five enemies, two hand cards) is written into world.Extra;
// the fixture's ult/play/end receipts and query answers stand in for Core: their gauges, damage, events and source name/text are protocol
// data, not Core rules, the effective BattleText or server acceptance (Root runs the actual host and real Core separately). sera/yuri/ravien
// and c_precise/e_pierce are real content ids the view names; card ids are fixture data. Every wait is bounded; held replies are released
// in finally. NOT_RUN by the writing job: Root runs them in the actual Unity Editor.
#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Pacing = MemoryForge.Client.MemoryForgeExpeditionClient.ActionPacing;

namespace MemoryForge.Client.Tests
{
    public partial class PracticeServerSessionPlayModeTests
    {
        const string BattleQueryRoute = "/dev/practice/runs/" + RunA + "/battle-query";

        [UnityTest]
        public IEnumerator AReadyUltimateSendsOnlyItsActorAndASelectedTargetFromThePublishedGaugeInTheLivePlayerPhase()
        {
            ControlRun();
            post = ControlServer(request => UltCommitted(ArgOf(request, "actor")!, ArgOf(request, "target")));
            var view = View();
            yield return Settled(view);

            // Three published actors and five enemies: every ultimate and enemy control is on the screen and none covers another.
            var controls = new[] { "Ult:sera", "Ult:yuri", "Ult:ravien" }.Concat(Enumerable.Range(1, 5).Select(i => "Enemy:e" + i))
                .Select(id => (name: id, button: DrawnButton(view, id))).ToList();
            foreach (var (name, button) in controls) Assert.That(button, Is.Not.Null, name);
            var screen = WorldRect((RectTransform)controls[0].button!.GetComponentInParent<Canvas>().rootCanvas.transform);
            var rects = controls.Select(c => WorldRect((RectTransform)c.button!.transform)).ToList();
            for (int i = 0; i < rects.Count; i++)
            {
                Assert.That(rects[i].xMin >= screen.xMin - 0.5f && rects[i].xMax <= screen.xMax + 0.5f && rects[i].yMin >= screen.yMin - 0.5f
                    && rects[i].yMax <= screen.yMax + 0.5f, Is.True, controls[i].name + " is on the screen");
                for (int j = i + 1; j < rects.Count; j++) Assert.That(rects[i].Overlaps(rects[j]), Is.False, controls[i].name + " / " + controls[j].name);
            }

            // Readiness is the published gauge against the published gaugeMax; an unready or unpublished actor sends nothing.
            Assert.That(DrawnButton(view, "Ult:sera")!.interactable, Is.True);
            Assert.That(DrawnButton(view, "Ult:yuri")!.interactable, Is.False, "60 of 100");
            Assert.That(Refused(view.UltAsync("yuri")), Is.True);
            Assert.That(Refused(view.UltAsync("nia")), Is.True, "not a published actor");
            Assert.That(BattleCommands(), Is.Empty);

            // No target chosen: exactly {actor}, no target or picks; the gauge shown afterwards is the server's.
            var first = view.UltAsync("ravien");
            yield return Until(() => first.IsCompleted, "ravien's ultimate resolves");
            Assert.That(first.Result, Is.True);
            Sent(BattleCommands().Single(), "ult", 3, new JObject { ["actor"] = "ravien" });
            Assert.That(ShownActor(view, "ravien")["gauge"]!.Value<int>(), Is.EqualTo(0));
            Assert.That(Refused(view.UltAsync("ravien")), Is.True, "the published gauge is spent");
            yield return Until(() => !view.HasPresentation && !view.Busy, "the ultimate's display closes");

            // A live enemy may be chosen for an ultimate without a hand card; the ultimate asks no query and sends exactly that target.
            int queries = BattleQueries().Length;
            Assert.That(view.SelectEnemy("e5"), Is.False, "a fallen enemy is not a target");
            Assert.That(view.SelectEnemy("e3"), Is.True);
            Assert.That(view.SelectedCard, Is.Null);
            var second = view.UltAsync("sera");
            yield return Until(() => second.IsCompleted, "sera's ultimate resolves");
            Assert.That(second.Result, Is.True);
            Sent(BattleCommands()[1], "ult", 4, new JObject { ["actor"] = "sera", ["target"] = "e3" });
            Assert.That(BattleQueries().Length, Is.EqualTo(queries));
            yield return Until(() => !view.HasPresentation && !view.Busy, "the second display closes");

            // Outside the live player phase a full gauge sends nothing.
            var battle = (JObject)world.Extra!["battle"]!;
            world.Version++; world.BattleV++;
            battle["v"] = world.BattleV; battle["phase"] = "ENEMY";
            Entry(battle, "actors", "yuri")["gauge"] = 100;
            var refresh = view.RefreshAsync();
            yield return Until(() => refresh.IsCompleted, "the refresh resolves");
            Assert.That(Refused(view.UltAsync("yuri")), Is.True);
            Assert.That(BattleCommands().Length, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator AServerBadTargetRefusalOfAnUltimateShowsNoGainAndKeepsTheGauge()
        {
            ControlRun();
            // The fixture stands in for Core's verdict on the chosen target.
            post = ControlServer(request => Error(422, "BAD_TARGET"));
            var view = View();
            yield return Settled(view);
            Assert.That(view.SelectEnemy("e2"), Is.True);
            var refused = view.UltAsync("sera");
            yield return Until(() => refused.IsCompleted, "the refused ultimate resolves");
            Assert.That(refused.Result, Is.False);
            Sent(BattleCommands().Single(), "ult", 3, new JObject { ["actor"] = "sera", ["target"] = "e2" });
            Assert.That(Outbox().Load(), Is.Null, "a proven Core refusal clears the request");
            Assert.That(view.Notice, Does.Contain("반영되지 않았어요"));
            Assert.That(view.Snapshot!.Context.ServerVersion, Is.EqualTo(3));
            Assert.That(view.HasPresentation, Is.False);
            Assert.That(ShownActor(view, "sera")["gauge"]!.Value<int>(), Is.EqualTo(100));
            Assert.That(Entry((JObject)Shown(view)["battle"]!, "enemies", "e2", "uid")["hp"]!.Value<int>(), Is.EqualTo(32));
            Assert.That(DrawnButton(view, "Ult:sera")!.interactable, Is.True, "nothing was spent");
        }

        [UnityTest]
        public IEnumerator AnUncertainUltimateIsSentOnceShowsNoGaugeChangeAndItsExactRetryNeverSpendsTwice()
        {
            ControlRun();
            var posted = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();
            byte[] receipt = Array.Empty<byte>();
            // The server spends the gauge, holds its reply, then the reply never arrives.
            post = ControlServer(request =>
            {
                receipt = UltCommitted("sera", null).Body;
                posted.TrySetResult(true);
                return new Reply(200, receipt) { Hold = release.Task, Drop = true };
            });
            var view = View();
            yield return Settled(view);
            try
            {
                var spending = view.UltAsync("sera");
                yield return Until(() => posted.Task.IsCompleted, "the ultimate is posted");
                // A second click and every other new action are refused at once; nothing is shown as spent.
                Assert.That(Refused(view.UltAsync("sera")), Is.True);
                Assert.That(Refused(view.UltAsync("ravien")), Is.True);
                Assert.That(Refused(view.EndTurnAsync()), Is.True);
                Assert.That(DrawnButton(view, "Ult:sera")!.interactable, Is.False);
                Assert.That(ShownActor(view, "sera")["gauge"]!.Value<int>(), Is.EqualTo(100));
                release.TrySetResult(true);
                yield return Until(() => spending.IsCompleted, "the lost reply resolves");
                Assert.That(spending.Result, Is.False);
            }
            finally { release.TrySetResult(true); }
            Sent(BattleCommands().Single(), "ult", 3, new JObject { ["actor"] = "sera" });
            Assert.That(Outbox().Load()!.Body, Is.EqualTo(BattleCommands()[0].Text));
            Assert.That(view.SessionState!.Pending, Is.Not.Null);
            Assert.That(view.SnapshotStale, Is.True);
            Assert.That(view.HasPresentation, Is.False);
            Assert.That(ShownActor(view, "sera")["gauge"]!.Value<int>(), Is.EqualTo(100), "no optimistic gauge");
            Assert.That(Refused(view.UltAsync("sera")), Is.True);
            Assert.That(Refused(view.UltAsync("ravien")), Is.True);
            yield return null;
            yield return null;
            Assert.That(BattleCommands().Length, Is.EqualTo(1));

            // The explicit retry sends the stored bytes; the server answers its stored receipt and spends nothing more.
            post = ControlServer(request => new Reply(200, receipt));
            var retry = view.RetryAsync();
            yield return Until(() => retry.IsCompleted, "the retry resolves");
            Assert.That(retry.Result, Is.True);
            Assert.That(BattleCommands().Length, Is.EqualTo(2));
            Assert.That(BattleCommands()[1].Body, Is.EqualTo(BattleCommands()[0].Body));
            Assert.That(Outbox().Load(), Is.Null);
            Assert.That(ShownActor(view, "sera")["gauge"]!.Value<int>(), Is.EqualTo(0));
            Assert.That(Entry((JObject)world.Extra!["battle"]!, "enemies", "e1", "uid")["hp"]!.Value<int>(), Is.EqualTo(31 - 12), "spent once");
            Assert.That(Refused(view.UltAsync("sera")), Is.True);
            Assert.That(BattleCommands().Length, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator UltimateCardAndCounterRowsCarryTheirOwnTagsAndTheUltimateDisplayIsTitledWithItsActor()
        {
            ControlRun();
            post = ControlServer(request => KindOf(request) == "ult" ? UltCommitted(ArgOf(request, "actor")!, null)
                : KindOf(request) == "play" ? PlayCommitted(ArgOf(request, "uid")!, ArgOf(request, "target")) : EndCommitted());
            var view = View();
            yield return Settled(view);
            // Reduced motion reveals every row at once, so the row texts are read without depending on the row timing.
            Assert.That(view.SetActionPace(Pacing.Reduced), Is.True);

            var ult = view.UltAsync("sera");
            yield return Until(() => ult.IsCompleted, "the ultimate resolves");
            Assert.That(ult.Result, Is.True);
            yield return Until(() => StripWith(view, "[필살기]") != null, "the ultimate's rows are shown");
            var strip = StripWith(view, "[필살기]")!;
            Assert.That(strip, Does.Contain("세라"), "the display is titled with its actor");
            Assert.That(strip, Does.Not.Contain("[사용]"));
            yield return Until(() => !view.HasPresentation && !view.Busy, "the ultimate's display closes");

            Assert.That(view.SelectCard("h1"), Is.True);
            Assert.That(view.SelectEnemy("e1"), Is.True);
            yield return Until(() => view.QueryView != null && !view.Busy, "the card's query is current");
            var play = view.ConfirmAsync();
            yield return Until(() => play.IsCompleted, "the play resolves");
            Assert.That(play.Result, Is.True);
            Sent(BattleCommands()[1], "play", 4, new JObject { ["uid"] = "h1", ["mode"] = "now", ["target"] = "e1" });
            yield return Until(() => StripWith(view, "[사용]") != null, "the card's rows are shown");
            Assert.That(StripWith(view, "[사용]"), Does.Not.Contain("[필살기]"));
            yield return Until(() => !view.HasPresentation && !view.Busy, "the card's display closes");

            // The end of turn is a barrier: its counter row is [반격], and it waits for Continue.
            var end = view.EndTurnAsync();
            yield return Until(() => end.IsCompleted, "the end resolves");
            Assert.That(end.Result, Is.True);
            Assert.That(view.PresentationBarrier, Is.True);
            view.SkipPresentation();
            var text = Unbuttoned(view);
            Assert.That(text, Does.Contain("[반격]"));
            Assert.That(text, Does.Not.Contain("[필살기]"));
            view.ContinuePresentation();
            Assert.That(view.HasPresentation, Is.False);
        }

        [UnityTest]
        public IEnumerator EveryPaceIsDisplayOnlyAndNoPaceContinuesATurnBarrierByItself()
        {
            ControlRun();
            float scale = Time.timeScale;
            post = ControlServer(request => KindOf(request) == "end" ? EndCommitted() : PlayCommitted(ArgOf(request, "uid")!, ArgOf(request, "target")));
            var view = View();
            yield return Settled(view);
            Assert.That(view.ActionPace, Is.EqualTo(Pacing.Normal));
            Assert.That(view.SetActionPace(Pacing.Normal), Is.False, "the chosen pace again is inert");
            Assert.That(view.SetActionPace((Pacing)99), Is.False, "an unknown pace");
            Assert.That(view.ActionPace, Is.EqualTo(Pacing.Normal));
            foreach (var pace in new[] { "Normal", "Double", "Reduced" }) Assert.That(DrawnButton(view, "Pace:" + pace), Is.Not.Null, pace);

            // A chosen card, mode and target stay as they were; the pace asks and sends nothing.
            Assert.That(view.SelectCard("h1"), Is.True);
            Assert.That(view.SelectMode("next"), Is.True);
            Assert.That(view.SelectEnemy("e2"), Is.True);
            yield return Until(() => view.QueryView != null && !view.Busy, "the card's query is current");
            int posts = host.Posts.Length;
            DrawnButton(view, "Pace:Double")!.onClick.Invoke();
            Assert.That(view.ActionPace, Is.EqualTo(Pacing.Double));
            Assert.That(view.SetActionPace(Pacing.Reduced), Is.True);
            yield return null;
            yield return null;
            Assert.That((view.SelectedCard, view.SelectedMode, view.Target), Is.EqualTo(("h1", "next", "e2")));
            Assert.That(view.QueryView, Is.Not.Null, "the current answer still counts");
            Assert.That(host.Posts.Length, Is.EqualTo(posts));
            Assert.That(Time.timeScale, Is.EqualTo(scale));
            var play = view.ConfirmAsync();
            yield return Until(() => play.IsCompleted, "the play resolves");
            Assert.That(play.Result, Is.True);
            Sent(BattleCommands().Single(), "play", 3, new JObject { ["uid"] = "h1", ["mode"] = "next", ["target"] = "e2" });
            yield return Until(() => !view.HasPresentation && !view.Busy, "the play's display closes");

            // Under every pace the end-of-turn barrier reveals its rows and still waits for an explicit Continue.
            int version = 4;
            foreach (var pace in new[] { Pacing.Reduced, Pacing.Double, Pacing.Normal })
            {
                view.SetActionPace(pace);
                var end = view.EndTurnAsync();
                yield return Until(() => end.IsCompleted, "the end resolves");
                Assert.That(end.Result, Is.True, pace.ToString());
                Sent(BattleCommands().Last(), "end", version++, new JObject());
                Assert.That(view.PresentationBarrier, Is.True, pace.ToString());
                Assert.That(Refused(view.UltAsync("sera")), Is.True, "a barrier blocks a new ultimate");
                float until = Time.realtimeSinceStartup + 2.5f;
                while (Time.realtimeSinceStartup < until) yield return null;
                Assert.That(view.PresentationBarrier, Is.True, pace + ": never continued by itself");
                Assert.That(DrawnButton(view, "ResultContinue")!.interactable, Is.True, pace + ": every row is revealed");
                view.ContinuePresentation();
                Assert.That(view.HasPresentation, Is.False);
                Assert.That(Time.timeScale, Is.EqualTo(scale));
            }
            Assert.That(BattleCommands().Length, Is.EqualTo(4));
        }

        [UnityTest]
        public IEnumerator APreparedCardSurvivesPaceChangesAndOpenDetailsAndGoesOutUnchangedThroughTheExistingDispatch()
        {
            ControlRun();
            // Thirteen rows keep the first display open long enough to choose and prepare the next card at the normal pace.
            post = ControlServer(request => PlayCommitted(ArgOf(request, "uid")!, ArgOf(request, "target"), hits: 11));
            var view = View();
            yield return Settled(view);
            Assert.That(view.SelectCard("h1"), Is.True);
            Assert.That(view.SelectEnemy("e1"), Is.True);
            yield return Until(() => view.QueryView != null && !view.Busy, "the first card's query is current");
            var first = view.ConfirmAsync();
            yield return Until(() => first.IsCompleted, "the first play resolves");
            Assert.That(first.Result, Is.True);
            Assert.That(view.HasPresentation && !view.PresentationBarrier, Is.True);

            Assert.That(view.SelectCard("h2"), Is.True);
            yield return Until(() => view.QueryView?.Uid == "h2" && !view.Busy, "the next card's query is current");
            Assert.That(Refused(view.ConfirmAsync()), Is.True, "prepared, not sent");
            Assert.That((view.HasPreparedIntent, view.PreparedCard, view.PreparedMode, view.PreparedTarget), Is.EqualTo((true, "h2", "now", (string?)null)));
            Assert.That(Refused(view.UltAsync("sera")), Is.True, "a prepared card blocks a new ultimate");

            // Reading the details and changing the pace neither send nor drop the prepared card.
            Assert.That(view.OpenCardDetails(), Is.True);
            Assert.That(view.SetActionPace(Pacing.Double), Is.True);
            Assert.That(view.SetActionPace(Pacing.Reduced), Is.True);
            Assert.That(view.HasPreparedIntent, Is.True);
            Assert.That(view.PreparedCard, Is.EqualTo("h2"));
            Assert.That(BattleCommands().Length, Is.EqualTo(1));

            // The display ends and the existing dispatch sends exactly the prepared request in the context it was prepared in.
            yield return Until(() => BattleCommands().Length == 2 && !view.Busy && view.Snapshot!.Context.ServerVersion == 5, "the prepared card goes out");
            Sent(BattleCommands()[1], "play", 4, new JObject { ["uid"] = "h2", ["mode"] = "now" });
            Assert.That(view.HasPreparedIntent, Is.False);
            Assert.That(view.CardDetailsOpen, Is.False, "the dispatched state change closed the older details");
            yield return null;
            Assert.That(BattleCommands().Length, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator UnderNowPlayACardWithOnlyTheNowModeShowsItsCostWithoutAModeStepAndThereIsNoEmptyReservationBoard()
        {
            // The live content's shape since docs/RULES_NOW_PLAY.md: one published mode (now) and no reservations.
            var nowOnly = ControlCard("h1", "strike", "enemy");
            nowOnly["modes"] = new JArray("now"); nowOnly["costs"] = new JObject { ["now"] = 1 }; nowOnly["blocked"] = new JObject { ["now"] = JValue.CreateNull() };
            ControlRun(new JArray(nowOnly, ControlCard("h2", "guard", null)));
            post = ControlServer(request => PlayCommitted(ArgOf(request, "uid")!, ArgOf(request, "target")));
            var view = View();
            yield return Settled(view);
            Assert.That(view.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Reservations"), Is.False, "no reservation, no board");
            Assert.That(view.SelectCard("h1"), Is.True);
            yield return Until(() => view.QueryView?.Uid == "h1" && !view.Busy, "the card's query is current");
            yield return null;
            Assert.That(view.SelectedMode, Is.EqualTo("now"));
            Assert.That(DrawnButton(view, "Mode:now"), Is.Null, "a single now mode has no mode step");
            Assert.That(Unbuttoned(view), Does.Contain("행동력 1"), "its cost stays on the action panel");
            // A server that still publishes a reservation mode keeps the mode buttons.
            Assert.That(view.SelectCard("h2"), Is.True);
            yield return Until(() => view.QueryView?.Uid == "h2" && !view.Busy, "the second card's query is current");
            yield return null;
            Assert.That(DrawnButton(view, "Mode:next"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator CardDetailsShowTheCurrentServerTextWhollyScrollableReadOnlyAndCloseWhenStale()
        {
            string body = string.Join("\n", Enumerable.Range(1, 40).Select(i => "효과 " + i + ": 대상에게 피해를 주고 다음 턴 예약 한 칸을 앞당겨요.")) + "\n마지막 줄 끝";
            string? name = "정밀 일격", text = body;
            ControlRun(new JArray(ControlCard("h1", "strike", "enemy", new JObject { ["c"] = Precise, ["e"] = Pierce }), ControlCard("h2", "guard", null)));
            post = ControlServer(request => Error(500, "INTERNAL_ERROR"), uid => uid == "h1" ? (name, text) : ("방어", "막아요."));
            var view = View();
            yield return Settled(view);
            Assert.That(view.OpenCardDetails(), Is.False, "no card is chosen");
            Assert.That(view.SelectCard("h1"), Is.True);
            Assert.That(view.SelectMode("next"), Is.True);
            Assert.That(view.SelectEnemy("e1"), Is.True);
            yield return Until(() => view.QueryView != null && !view.Busy, "the card's query is current");
            Assert.That(Drawn(view), Does.Not.Contain("정밀 회로"));

            int posts = host.Posts.Length;
            Assert.That(view.OpenCardDetails(), Is.True);
            Assert.That(view.CardDetailsOpen, Is.True);
            var label = DetailsBody(view)!;
            Assert.That(label, Is.Not.Null);
            Assert.That(label.text, Does.Contain(body), "the server's whole effective text");
            Assert.That(Drawn(view), Does.Contain("정밀 회로").And.Contain("관통"), "the attached parts by their content names");
            var scroll = view.GetComponentsInChildren<UnityEngine.UI.ScrollRect>().Single(s => s.name == "CardDetailsScroll");
            var content = scroll.content;
            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)content.parent;
            Assert.That(scroll.vertical, Is.True);
            Assert.That(label.resizeTextForBestFit, Is.False, "not shrunk to fit");
            Assert.That(label.rectTransform.rect.height, Is.GreaterThanOrEqualTo(label.preferredHeight - 1f), "the label is as tall as its wrapped text");
            Assert.That(content.rect.height, Is.GreaterThan(viewport.rect.height), "the long text scrolls");
            var corners = new Vector3[4];
            label.rectTransform.GetWorldCorners(corners);
            Assert.That(content.InverseTransformPoint(corners[0]).y, Is.GreaterThanOrEqualTo(content.rect.yMin - 0.5f), "the text ends inside the content");
            Assert.That(content.InverseTransformPoint(corners[1]).y, Is.LessThanOrEqualTo(content.rect.yMax + 0.5f));
            Assert.That(scroll.GetComponentsInChildren<UnityEngine.UI.InputField>(), Is.Empty, "read-only");
            Assert.That(DrawnButton(view, "CloseCardDetails"), Is.Not.Null);

            // Opening asked and sent nothing and changed no selection; while open no new manual action goes out.
            yield return null;
            yield return null;
            Assert.That(host.Posts.Length, Is.EqualTo(posts));
            Assert.That((view.SelectedCard, view.SelectedMode, view.Target), Is.EqualTo(("h1", "next", "e1")));
            Assert.That(view.QueryView, Is.Not.Null);
            Assert.That(Refused(view.ConfirmAsync()), Is.True);
            Assert.That(Refused(view.UltAsync("sera")), Is.True);
            Assert.That(Refused(view.EndTurnAsync()), Is.True);
            view.CloseCardDetails();
            Assert.That(view.CardDetailsOpen, Is.False);
            Assert.That((view.SelectedCard, view.SelectedMode, view.Target), Is.EqualTo(("h1", "next", "e1")));
            Assert.That(host.Posts.Length, Is.EqualTo(posts));

            // A selection change is either refused by the open details or closes them: details never show an older selection.
            Assert.That(view.OpenCardDetails(), Is.True);
            if (view.SelectMode("now")) Assert.That(view.CardDetailsOpen, Is.False);
            else { Assert.That(view.SelectedMode, Is.EqualTo("next")); view.CloseCardDetails(); }
            yield return Until(() => view.QueryView != null && !view.Busy, "the query of the current selection is current");

            // Another context closes them.
            Assert.That(view.OpenCardDetails(), Is.True);
            world.Version++;
            var refresh = view.RefreshAsync();
            yield return Until(() => refresh.IsCompleted, "the refresh resolves");
            Assert.That(view.Snapshot!.Context.ServerVersion, Is.EqualTo(4));
            Assert.That(view.CardDetailsOpen, Is.False);

            // So does leaving; the new session starts without them.
            yield return Until(() => view.QueryView != null && !view.Busy, "the query is asked again");
            Assert.That(view.OpenCardDetails(), Is.True);
            view.enabled = false;
            Assert.That(view.CardDetailsOpen, Is.False);
            view.enabled = true;
            yield return Settled(view);
            Assert.That(view.CardDetailsOpen, Is.False);

            // A source without name or text is shown as unavailable, never as an older text or a placeholder value.
            name = null; text = null;
            Assert.That(view.SelectCard("h1"), Is.True);
            Assert.That(view.SelectEnemy("e1"), Is.True);
            yield return Until(() => view.QueryView != null && !view.Busy, "the textless query is current");
            Assert.That(view.QueryView!.ToJObject()["source"]!["text"]!.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(view.OpenCardDetails(), Is.True);
            string unavailable = DetailsBody(view)!.text;
            Assert.That(unavailable, Is.Not.Empty);
            Assert.That(unavailable, Does.Not.Contain("효과 1:"));
            Assert.That(unavailable, Does.Not.Contain("null"));
            view.CloseCardDetails();
            Assert.That(BattleCommands(), Is.Empty);
        }

        // ---- battle-control fixtures (protocol data on the shared World) ----

        // A live BATTLE in the PLAYER phase: PublicView.Battle's members for three actors (sera and ravien at the gaugeMax 100, yuri at
        // 60), five enemies e1..e5 (e5 fallen) and two hand cards (h1 needs an enemy target; h2 does not).
        void ControlRun(JArray? hand = null)
        {
            world.Phase = "BATTLE";
            world.InBattle = true;
            hand ??= new JArray(ControlCard("h1", "strike", "enemy"), ControlCard("h2", "guard", null));
            world.Extra = new JObject
            {
                ["pending"] = JValue.CreateNull(),
                ["battle"] = new JObject
                {
                    ["v"] = world.BattleV, ["phase"] = "PLAYER", ["turn"] = world.Turn, ["hp"] = 40, ["maxHp"] = 50, ["block"] = 0, ["counter"] = 0, ["ap"] = 3,
                    ["piles"] = new JObject { ["draw"] = 10, ["hand"] = hand.Count, ["discard"] = 0, ["exhaust"] = 0 },
                    ["hand"] = hand, ["reservations"] = new JArray(),
                    ["slots"] = new JObject { ["next"] = SlotColumn(), ["after"] = SlotColumn(), ["sealedUntil"] = 0 },
                    ["gaugeMax"] = 100,
                    ["actors"] = new JArray(ControlActor("sera", "세라", "volt", 100), ControlActor("yuri", "유리", "frost", 60), ControlActor("ravien", "라비엔", "flame", 100)),
                    ["enemies"] = new JArray(Enumerable.Range(1, 5).Select(i => (object)ControlFoe(i)).ToArray()),
                    ["result"] = JValue.CreateNull()
                }
            };
        }
        static JObject SlotColumn() => new JObject { ["cap"] = 2, ["used"] = 0, ["sealed"] = false, ["due"] = 3 };
        static JObject ControlActor(string id, string name, string attr, int gauge) =>
            new JObject { ["id"] = id, ["name"] = name, ["attr"] = attr, ["gauge"] = gauge, ["res"] = 0 };
        static JObject ControlFoe(int i) => new JObject
        {
            ["uid"] = "e" + i, ["id"] = "slime", ["name"] = "슬라임 " + i, ["hp"] = i == 5 ? 0 : 30 + i, ["maxHp"] = 40, ["block"] = 0, ["alive"] = i != 5,
            ["intent"] = new JObject { ["t"] = "atk", ["d"] = 5 }, ["next"] = JValue.CreateNull()
        };
        static JObject ControlCard(string uid, string id, string? needsTarget, JObject? mods = null) => new JObject
        {
            ["uid"] = uid, ["id"] = id, ["plus"] = false, ["mods"] = mods ?? new JObject { ["c"] = JValue.CreateNull(), ["e"] = JValue.CreateNull() },
            ["needsTarget"] = needsTarget == null ? JValue.CreateNull() : (JToken)needsTarget, ["cost"] = 1,
            ["modes"] = new JArray("now", "next"), ["costs"] = new JObject { ["now"] = 1, ["next"] = 2 },
            ["blocked"] = new JObject { ["now"] = JValue.CreateNull(), ["next"] = JValue.CreateNull() }
        };

        // Battle-query answers (QueryJson echoing the exact request, with the given source name/text) and every other POST as a command.
        Func<Seen, Reply> ControlServer(Func<Seen, Reply> command, Func<string, (string? Name, string? Text)>? source = null) =>
            request => request.Path == BattleQueryRoute ? Queried(request, source) : command(request);
        Reply Queried(Seen request, Func<string, (string? Name, string? Text)>? source)
        {
            var asked = JObject.Parse(request.Text);
            string uid = asked["uid"]!.Value<string>()!, mode = asked["mode"]!.Value<string>()!;
            var picks = ((asked["picks"] as JArray) ?? new JArray()).Select(p => p.Value<string>()!).ToList();
            var reply = QueryJson(world, uid, mode, asked["target"]?.Value<string>(), picks);
            (string? Name, string? Text) shown = source != null ? source(uid) : ("카드 " + uid, "fixture card text");
            reply["source"]!["name"] = shown.Name == null ? JValue.CreateNull() : (JToken)shown.Name;
            reply["source"]!["text"] = shown.Text == null ? JValue.CreateNull() : (JToken)shown.Text;
            return Reply.Json(200, reply);
        }

        // The fixture commits a battle command: the run and battle.v move on, the change applies, and the receipt carries its events.
        Reply Battled(Action<JObject> change, JArray events, Task? hold = null)
        {
            var battle = (JObject)world.Extra!["battle"]!;
            world.Version++;
            world.BattleV++;
            battle["v"] = world.BattleV;
            change(battle);
            return new Reply(200, Utf8(RunJson(world, world.Version, events))) { Hold = hold };
        }
        // ult: the actor's gauge to 0 and 12 damage to the target (e1 without one), with Core's ult and dmg src:ult events.
        Reply UltCommitted(string actor, string? target) => Battled(b =>
        {
            Entry(b, "actors", actor)["gauge"] = 0;
            var foe = Entry(b, "enemies", target ?? "e1", "uid");
            foe["hp"] = foe["hp"]!.Value<int>() - 12;
        }, new JArray(new JObject { ["e"] = "ult", ["actor"] = actor },
            new JObject { ["e"] = "dmg", ["uid"] = target ?? "e1", ["n"] = 12, ["blocked"] = 0, ["src"] = "ult", ["card"] = JValue.CreateNull() }));
        // play: the card leaves the hand, 1 AP is spent and each hit deals 1 card damage; play, dmg src:card ×hits, resolved.
        Reply PlayCommitted(string uid, string? target, int hits = 1)
        {
            var events = new JArray(new JObject { ["e"] = "play", ["uid"] = uid, ["mode"] = "now", ["cost"] = 1, ["target"] = target == null ? JValue.CreateNull() : (JToken)target });
            for (int i = 0; i < hits; i++) events.Add(new JObject { ["e"] = "dmg", ["uid"] = target ?? "e1", ["n"] = 1, ["blocked"] = 0, ["src"] = "card", ["card"] = "strike" });
            events.Add(new JObject { ["e"] = "resolved", ["uid"] = uid });
            return Battled(b =>
            {
                b["hand"] = new JArray(((JArray)b["hand"]!).Where(c => c["uid"]!.Value<string>() != uid));
                b["piles"]!["hand"] = ((JArray)b["hand"]!).Count;
                b["ap"] = b["ap"]!.Value<int>() - 1;
                var foe = Entry(b, "enemies", target ?? "e1", "uid");
                foe["hp"] = foe["hp"]!.Value<int>() - hits;
            }, events);
        }
        // end: 3 counter damage to e1 and the next turn.
        Reply EndCommitted()
        {
            var events = new JArray(new JObject { ["e"] = "dmg", ["uid"] = "e1", ["n"] = 3, ["blocked"] = 0, ["src"] = "counter" },
                new JObject { ["e"] = "turn", ["turn"] = world.Turn + 1, ["ap"] = 3 });
            return Battled(b =>
            {
                world.Turn++;
                b["turn"] = world.Turn;
                b["ap"] = 3;
                var foe = Entry(b, "enemies", "e1", "uid");
                foe["hp"] = foe["hp"]!.Value<int>() - 3;
            }, events);
        }

        static JObject Entry(JObject battle, string list, string id, string key = "id") =>
            ((JArray)battle[list]!).OfType<JObject>().First(x => x[key]!.Value<string>() == id);
        static JObject ShownActor(MemoryForgeServerPracticeClient view, string id) => Entry((JObject)Shown(view)["battle"]!, "actors", id);
        Seen[] BattleCommands() => host.Posts.Where(p => p.Path == PracticeCommandOutbox.CommandRoute(RunA)).ToArray();
        Seen[] BattleQueries() => host.Posts.Where(p => p.Path == BattleQueryRoute).ToArray();
        static string KindOf(Seen request) => JObject.Parse(request.Text)["kind"]!.Value<string>()!;
        static string? ArgOf(Seen request, string name) => JObject.Parse(request.Text)["args"]?[name]?.Value<string>();
        static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
        // Every live text outside a button (display rows and titles, not control captions).
        static string Unbuttoned(MemoryForgeServerPracticeClient view) => string.Join("\n", view.GetComponentsInChildren<UnityEngine.UI.Text>()
            .Where(t => t.GetComponentInParent<UnityEngine.UI.Button>() == null).Select(t => t.text));
        // The texts of the panel holding the first non-button text with the marker (a display row with its title), or null.
        static string? StripWith(MemoryForgeServerPracticeClient view, string marker)
        {
            var row = view.GetComponentsInChildren<UnityEngine.UI.Text>().FirstOrDefault(t => t.text.Contains(marker) && t.GetComponentInParent<UnityEngine.UI.Button>() == null);
            return row == null ? null : string.Join("\n", row.transform.parent.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text));
        }
        static UnityEngine.UI.Text? DetailsBody(MemoryForgeServerPracticeClient view) =>
            view.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "CardDetailsBody")?.GetComponentInChildren<UnityEngine.UI.Text>();
    }
}
#endif
