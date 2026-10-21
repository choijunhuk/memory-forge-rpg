// P1-CARD-FLOW-ACTION-PRESENTATION §14.2/§14.3/§14.7: Play Mode checks of the expedition view's player-action display over the real
// shared-core Run and the exported full content. After an actual play or ultimate the committed command's own digest rows step through a
// small inline strip (a covering sheet once the battle has ended); meanwhile only the pure next card/mode/target selection is accepted.
// Every command goes through the component's public methods or a real EventSystem raycast; an independent mirror Run receives the same
// public Core commands and must stay identical, so no Run state is injected, restored or set privately. Documented seams, as in the
// failure and picker tests (no production hook): the private session store is read, the private Do boundary called and the client's own
// parsed content changed (a test-local enemy-move fault, restored in finally) by reflection. The display is provisional text with local
// prototype timing, not final VFX/audio, a measured target, Android/device, server, account or reconnect acceptance.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
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
using Pacing = MemoryForge.Client.MemoryForgeExpeditionClient.ActionPacing;

namespace MemoryForge.Client.Tests
{
    public class ActionPresentationPlayModeTests
    {
        const long Seed = 1234;
        const string Showing = "ACTION_SHOWING", Barrier = "TURN_SUMMARY_OPEN", SheetOpen = "PICKER_OPEN", Recovery = "RECOVERY_REQUIRED";
        const string Diagnostic = @"^\[MemoryForge\] Practice expedition quarantined after a failed local Run calculation \(";
        // The documented local prototype timing (not a measured or approved target) and the original §14.3 0.3 s reservation step.
        const float RowSeconds = 0.2f, ReservedSeconds = 0.3f, HoldSeconds = 0.6f;
        readonly List<GameObject> hosts = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator DestroyOwnedHosts()
        {
            foreach (var host in hosts) if (host != null) Object.Destroy(host);
            hosts.Clear();
            yield return null;
        }

        sealed class Session
        {
            public GameObject Host = null!;
            public MemoryForgeExpeditionClient Client = null!;
            public CoreRun Mirror = null!;
            public RunData Data = null!;
            public BattleResponse? Last;
        }
        sealed class Found { public string? Card, Other, Enemy, Actor; }

        // The seed is set on a disabled host before the component's real OnEnable start; nothing else is injected.
        IEnumerator Open(Session s, long seed = Seed)
        {
            var host = new GameObject("ActionPresentationTestHost"); hosts.Add(host);
            host.SetActive(false);
            var client = host.AddComponent<MemoryForgeExpeditionClient>(); client.Seed = seed; client.Length = "short";
            host.SetActive(true);
            yield return null;
            s.Host = host; s.Client = client; s.Data = ClientRunContent.Load();
            s.Mirror = CoreRun.Create(s.Data, MemoryForgeExpeditionClient.PracticeSetup(seed, "short", "practice-1-" + seed, new Dictionary<string, object?>(), 0));
            AssertSame(s, "the view's Run must be exactly Create of the public PracticeSetup");
            Assert.That((client.HasActionPresentation, client.ActionPace), Is.EqualTo((false, Pacing.Normal)), "a fresh view has no display and the normal pace");
        }
        // Opens a practice and enters its first node (a battle) by the actual command.
        IEnumerator OpenBattle(Session s, long seed = Seed)
        {
            yield return Open(s, seed);
            string first = s.Mirror.Available()[0];
            Assert.That(Both(s, () => s.Client.EnterNode(first), r => r.Enter(first)), Is.True);
            Assert.That(s.Mirror.State.Battle, Is.Not.Null);
            yield return null; Canvas.ForceUpdateCanvases();
        }
        // The first living enemy and the first hand card legal now against it without card picks.
        static (string Card, string Enemy) PlainChoice(Session s, string? except = null)
        {
            var b = s.Mirror.State.Battle!; string enemy = b.Enemies.First(e => e.Alive).Uid;
            string? card = b.Zones.Hand.FirstOrDefault(u => u != except && Plain(s, u, enemy));
            Assert.That(card, Is.Not.Null, "seed 1234's hand offers an immediate card without card picks");
            return (card!, enemy);
        }
        static bool Plain(Session s, string uid, string enemy) => s.Mirror.BattleAllowedModes(uid).Contains("now") && s.Mirror.BattleCheck(uid, "now", enemy) == null
            && MemoryForgeExpeditionClient.ChoiceRequests(s.Mirror.BattleEffectiveCard(uid), "now", s.Data.Memory.Battle).Count == 0;
        // Selects and confirms one plain card through the public handlers; exactly one actual play on both Runs.
        static void PlayPlain(Session s, string card, string enemy)
        {
            s.Client.SelectEnemy(enemy); s.Client.SelectCard(card); s.Client.SelectMode("now");
            Assert.That(Both(s, s.Client.ConfirmCard, BattleCore(s, "play", Play(card, "now", enemy))), Is.True);
            Assert.That(s.Client.HasActionPresentation, Is.True, "an actual play opens its display");
        }

        // The chosen card's Core forecast, split per enemy, lights on each enemy's own HP bar the HP it would take, with "now → after".
        [UnityTest]
        public IEnumerator EachEnemysBarShowsTheHpTheChosenCardWouldTake()
        {
            var s = new Session(); yield return OpenBattle(s);
            var client = s.Client; var b = s.Mirror.State.Battle!;
            string enemy = b.Enemies.First(e => e.Alive).Uid;
            string? card = b.Zones.Hand.FirstOrDefault(u => Plain(s, u, enemy) && s.Mirror.BattleForecast(u, "now", enemy) is { Error: null, Hidden: false } f && f.EnemyHpLosses.Count > 0);
            Assert.That(card, Is.Not.Null, "seed 1234's hand has a plain card that hits");
            client.SelectEnemy(enemy); client.SelectCard(card!); client.SelectMode("now");
            yield return null;
            var expected = s.Mirror.BattleForecast(card!, "now", enemy).EnemyHpLosses.ToDictionary(e => e.Uid, e => e.HpLoss);
            var view = ActiveView(s.Host);
            foreach (var e in b.Enemies.Where(e => e.Alive))
            {
                var slot = view.GetComponentsInChildren<Button>().First(x => x.name == "Enemy:" + e.Uid).transform;
                bool lit = slot.GetComponentsInChildren<RectTransform>().Any(r => r.name == "HpPreview");
                Assert.That(lit, Is.EqualTo(expected.ContainsKey(e.Uid)), e.Uid + " lit exactly when the forecast takes its HP");
                if (lit) Assert.That(slot.GetComponentsInChildren<Text>().Any(t => t.text == e.Hp + " → " + Math.Max(0, e.Hp - expected[e.Uid]) + " / " + e.MaxHp), Is.True, e.Uid + " numbers");
            }
            // Played, the card leaves the hand and nothing is chosen any more: no forecast, nothing lit.
            Assert.That(Both(s, client.ConfirmCard, BattleCore(s, "play", Play(card!, "now", enemy))), Is.True);
            FinishAction(client);
            yield return null;
            Assert.That(s.Mirror.State.Battle!.Zones.Hand.Contains(card!) && client.SelectedMode != null, Is.False, "the played card is no longer a request to forecast");
            Assert.That(ActiveView(s.Host).GetComponentsInChildren<RectTransform>().Any(r => r.name == "HpPreview"), Is.False, "nothing to forecast, nothing lit");
        }

        // An ultimate shows the battle kit's cut-in (plate, no touch) while motion is on, and none under reduced motion (kit README).
        [UnityTest]
        public IEnumerator AnUltimateShowsTheKitCutInUnlessMotionIsReduced()
        {
            bool savedReduced = MemoryForgeUiMotion.Reduced; // the global preference is the player's: fixed off here, restored after
            MemoryForgeUiMotion.Reduced = false;
            try
            {
            foreach (bool calm in new[] { false, true })
            {
                var s = new Session(); yield return OpenBattle(s);
                var client = s.Client; int max = s.Data.Memory.Battle.Rules.GaugeMax;
                if (calm) Assert.That(client.SetActionPace(Pacing.Reduced), Is.True);
                string? actor = null;
                for (int step = 0; step < 80 && actor == null; step++)
                {
                    var b = s.Mirror.State.Battle;
                    Assert.That(b, Is.Not.Null, "the battle lasts until a gauge fills");
                    actor = b!.Actors.FirstOrDefault(a => a.Gauge >= max)?.Id;
                    if (actor != null) break;
                    // Fill a gauge without ending the battle: only cards whose visible forecast leaves every enemy standing, else end the turn.
                    string enemy = b.Enemies.First(e => e.Alive).Uid;
                    string? card = b.Zones.Hand.FirstOrDefault(u => Plain(s, u, enemy) && s.Mirror.BattleForecast(u, "now", enemy) is { Error: null, Hidden: false } f
                        && f.EnemyHpLosses.All(l => b.Enemies.First(e => e.Uid == l.Uid).Hp > l.HpLoss));
                    if (card != null) { PlayPlain(s, card, enemy); Assert.That(client.ActionPresentationTerminal, Is.False, "the battle lasts until a gauge fills"); FinishAction(client); }
                    else { Assert.That(Both(s, client.EndTurn, BattleCore(s, "end", null)), Is.True); Dismiss(client); }
                    yield return null;
                }
                Assert.That(actor, Is.Not.Null, "a gauge fills");
                string aim = s.Mirror.State.Battle!.Enemies.First(e => e.Alive).Uid;
                client.SelectEnemy(aim);
                var args = BattleArgument.Object(new[] { Pair("actor", actor), Pair("target", aim) });
                Assert.That(Both(s, () => client.UseUltimate(actor!), BattleCore(s, "ult", args)), Is.True);
                var cut = ActiveView(s.Host).GetComponentsInChildren<RectTransform>().FirstOrDefault(r => r.name == "UltimateCutIn");
                Assert.That(cut != null, Is.EqualTo(!calm), calm ? "reduced motion omits the cut-in" : "the ultimate shows its cut-in");
                if (cut != null)
                {
                    Assert.That(cut.GetComponent<Image>().sprite, Is.Not.Null, "on the kit's plate");
                    Assert.That(cut.GetComponent<CanvasGroup>().blocksRaycasts, Is.False, "decoration takes no touch");
                }
                if (client.HasActionPresentation) FinishAction(client);
                yield return null;
            }
            }
            finally { MemoryForgeUiMotion.Reduced = savedReduced; }
        }

        // The committed play is shown in the strip at once over the actual current view; the next card, mode and target are prepared by
        // real rays during the display with their forecast on the action line; a confirm then only prepares the one unsent intent, which
        // is cancelled explicitly; every command is refused before the Run; the display ends on its real timer in place; then the prepared
        // selection is the next actual play.
        [UnityTest]
        public IEnumerator ACommittedPlayIsShownInlineWhileOnlyThePureNextSelectionIsAccepted()
        {
            var s = new Session(); yield return OpenBattle(s);
            var client = s.Client; float scale = Time.timeScale;
            var (card, enemy) = PlainChoice(s);
            string name = s.Mirror.BattleEffectiveCard(card).Name;
            client.SelectEnemy(enemy); client.SelectCard(card); client.SelectMode("now");
            yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(s.Host, "ConfirmCard"));
            float played = Time.realtimeSinceStartup;
            MirrorOf(s, BattleCore(s, "play", Play(card, "now", enemy)));
            Assert.That(client.LastError, Is.Null);
            Assert.That((client.HasActionPresentation, client.ActionPresentationTerminal), Is.EqualTo((true, false)), "an actual play opens a non-terminal display");
            var rows = client.ActionPresentationRows;
            AssertRowsMatch(rows, s.Last!.Events);
            Assert.That(rows[0].Text, Does.StartWith("카드 사용 · " + name), "the first row is the committed play");
            Assert.That(rows.Count, Is.GreaterThan(1), "a played card has more than its play event");
            Assert.That(client.ActionPresentationShown, Is.EqualTo(1), "the first row shows at once, the rest step");
            var eventRows = rows.Where(r => r.Kind != BattleDigestKind.EnemyState).Select(r => r.Text).ToList();
            Assert.That(client.BattleLog.Skip(client.BattleLog.Count - eventRows.Count), Is.EqualTo(eventRows), "the same committed event rows went to the battle log once");
            string committed = Fingerprint(client.Snapshot!), store = StorePrint(client);

            // The actual current state is drawn immediately beneath: no spent card and no dead enemy is selectable.
            var view = ActiveView(s.Host); var b = s.Mirror.State.Battle!;
            Assert.That(view.GetComponentsInChildren<Button>().Where(x => x.name.StartsWith("Hand:", StringComparison.Ordinal)).Select(x => x.name.Substring(5)), Is.EqualTo(b.Zones.Hand));
            Assert.That(b.Zones.Hand, Does.Not.Contain(card));
            foreach (var e in b.Enemies) Assert.That(ButtonNamed(s.Host, "Enemy:" + e.Uid).interactable, Is.EqualTo(e.Alive), "enemy " + e.Uid);
            Assert.That(ButtonNamed(s.Host, "EndTurn").interactable, Is.False, "the end waits for the display");
            Assert.That(view.GetComponentsInChildren<Button>().Where(x => x.name.StartsWith("Ult:", StringComparison.Ordinal)).All(x => !x.interactable), Is.True);
            Assert.That(ButtonNamed(s.Host, "SkipActionPresentation").interactable, Is.True);
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(ActiveView(s.Host), Is.SameAs(view), "reveal steps rewrite labels in place");
            Assert.That(client.HasActionPresentation, Is.True);
            int k = client.ActionPresentationShown;
            AssertReadable(view, t => t == "카드 사용 결과 " + k + "/" + rows.Count, "strip status");
            AssertReadable(view, t => t == StripText(rows, k), "strip rows");
            AssertReadable(view, t => t == "행동력  " + b.Ap, "the actual AP");

            // The next intent by real rays during the display: accepted, pure, and its forecast on its own action line.
            string target = b.Enemies.First(e => e.Alive).Uid;
            string? next = b.Zones.Hand.FirstOrDefault(u => Plain(s, u, target));
            Assert.That(next, Is.Not.Null, "seed 1234's first play leaves another immediate card without picks");
            Click(ButtonNamed(s.Host, "Enemy:" + target)); yield return null; Canvas.ForceUpdateCanvases();
            // RULES_NOW_PLAY: there is no mode button; a card's only mode, now, is chosen with the card.
            Click(ButtonNamed(s.Host, "Hand:" + next)); yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(client.HasActionPresentation, Is.True, "the selection was made during the display");
            Assert.That(client.LastError, Is.Null);
            Assert.That(new[] { client.SelectedCard, client.SelectedMode, client.Target }, Is.EqualTo(new[] { next, "now", target }));
            var forecast = s.Mirror.BattleForecast(next!, "now", target);
            var shown = ActiveView(s.Host);
            AssertReadable(shown, t => t == client.ForecastLine(forecast, false), "the next request's forecast on the action line");
            AssertReadable(shown, t => t == "카드 사용 결과 " + client.ActionPresentationShown + "/" + rows.Count, "the display continues beside it");
            // P1-CARD-FLOW-ONE-UNSENT-INTENT: a valid next request may now be confirmed during the display, but only as the one unsent intent.
            Assert.That(ButtonNamed(s.Host, "ConfirmCard").interactable, Is.True, "a valid next request can be confirmed into the unsent intent");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed), "selections and forecasts during the display never reach the Run");
            AssertSame(s, "the mirror received nothing either");
            Assert.That(client.ConfirmCard(), Is.False, "a confirm during the display commits nothing");
            Assert.That((client.LastError, client.HasPreparedIntent, client.PreparedCard, client.HasActionPresentation), Is.EqualTo(((string?)null, true, next, true)),
                "it prepares the one unsent intent");
            Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)), "preparing reached the Run or the store");
            Assert.That(client.CancelPreparedIntent(), Is.True, "the explicit cancel drops it unsent");
            Assert.That((client.HasPreparedIntent, client.LastError), Is.EqualTo((false, (string?)null)));
            Assert.That(new[] { client.SelectedCard, client.SelectedMode, client.Target }, Is.EqualTo(new[] { next, "now", target }), "the selection stays");
            AssertSame(s, "preparing and cancelling sent nothing");

            // Every command is refused before the Run with the display code; the private Do boundary itself refuses.
            var commands = new (string Name, Func<bool> Call)[]
            {
                ("PlayCard", () => client.PlayCard(next!, "now")), ("EndTurn", () => client.EndTurn()),
                ("UseUltimate", () => client.UseUltimate(b.Actors[0].Id)), ("EnterNode", () => client.EnterNode("none")), ("TakeCard", () => client.TakeCard("sera_round")),
                ("SkipCard", () => client.SkipCard()), ("TakeRelic", () => client.TakeRelic("lucky_coin")), ("TakePart", () => client.TakePart()),
                ("Evolve", () => client.Evolve("k1", "sera_round")), ("Proceed", () => client.Proceed()), ("RestHeal", () => client.RestHeal()),
                ("RestUpgrade", () => client.RestUpgrade("k1")), ("ForgeOffer", () => client.ForgeOffer("k1")), ("ForgeApply", () => client.ForgeApply("p")),
                ("ForgeRemove", () => client.ForgeRemove("k1")), ("Buy", () => client.Buy("cards", "sera_round")), ("BuyRemoval", () => client.BuyRemoval("k1")),
                ("ChooseEvent", () => client.ChooseEvent(0)), ("Archive", () => client.Archive("return")), ("ApplyFoundPart", () => client.ApplyFoundPart("p", "k1")),
                ("Assign", () => client.Assign("k1", "sera")), ("Abandon", () => client.Abandon()), ("ToggleChange", () => client.ToggleChange("x")),
                ("CommitMemory", () => client.CommitMemory()), ("CloseWithoutSave", () => client.CloseWithoutSave()), ("Depart", () => client.Depart()),
                ("Do", () => Do(client, () => Assert.Fail("the private Do boundary ran a command during the display")))
            };
            foreach (var (label, call) in commands)
            {
                Assert.That(call(), Is.False, label + " must be refused during the display");
                Assert.That(client.LastError, Is.EqualTo(Showing), label);
                Assert.That((client.HasActionPresentation, client.PickerOpen, client.RecoveryRequired, client.HasPreparedIntent), Is.EqualTo((true, false, false, false)), label);
                Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)), label + " reached the Run or the store");
            }
            client.StartPractice();
            Assert.That(client.LastError, Is.EqualTo(Showing), "StartPractice");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed), "StartPractice restarted the Run");
            client.SelectMode("now");
            Assert.That(client.LastError, Is.Null, "a selection is still accepted after the refusals");
            AssertSame(s, "nothing reached the Run");

            // The real timer ends the display in place: same view, the gated controls become available, nothing reaches the Run.
            yield return null; Canvas.ForceUpdateCanvases();
            var live = ActiveView(s.Host); var endTurn = ButtonNamed(s.Host, "EndTurn"); var confirm = ButtonNamed(s.Host, "ConfirmCard");
            var skip = ButtonNamed(s.Host, "SkipActionPresentation");
            float deadline = Time.realtimeSinceStartup + 5f;
            while (client.HasActionPresentation && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed), "the display timer never reaches the Run");
                yield return null;
            }
            Assert.That(client.HasActionPresentation, Is.False, "the display ends on its real timer");
            Assert.That(Time.realtimeSinceStartup - played, Is.GreaterThanOrEqualTo(Duration(rows, 1f) - 0.05f), "each row and the hold take their local time");
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(OwnedViews(s.Host), Is.EqualTo(new[] { live }), "completion rewrites in place; the view is not rebuilt");
            Assert.That((endTurn.interactable, confirm.interactable, skip.interactable), Is.EqualTo((true, true, false)), "control availability changes in place");
            AssertReadable(live, t => t == "행동 결과", "idle strip status");
            Assert.That(live.GetComponentsInChildren<Text>().Any(t => t.text.StartsWith("카드 사용 결과", StringComparison.Ordinal)), Is.False);
            Assert.That((Time.timeScale, Fingerprint(client.Snapshot!)), Is.EqualTo((scale, committed)), "the display never touches Time.timeScale or the Run");

            // The prepared intent is now the next actual play, once.
            Click(confirm);
            MirrorOf(s, BattleCore(s, "play", Play(next!, "now", target)));
            Assert.That(client.LastError, Is.Null);
            Assert.That(client.HasActionPresentation, Is.True, "the next actual play opens its own display");
            AssertRowsMatch(client.ActionPresentationRows, s.Last!.Events);
        }

        // The same committed play under each pace (normal, 2x, reduced motion, an explicit skip, and a reduced/2x switch mid-display), then
        // the same actual end: identical Run, logs, events, RNG, store and digest rows. The pace changes only when rows appear.
        [UnityTest]
        public IEnumerator EveryPaceAndSkipLeaveTheSameCommittedStateAndChangeOnlyTheDisplay()
        {
            float scale = Time.timeScale;
            var results = new List<(string Variant, string Run, string Store, string Log, string Rows)>();
            foreach (string variant in new[] { "normal", "double", "reduced", "skip", "switch" })
            {
                var s = new Session(); yield return OpenBattle(s);
                var client = s.Client;
                Pacing pace = variant == "double" ? Pacing.Double : variant == "reduced" ? Pacing.Reduced : Pacing.Normal;
                if (pace != Pacing.Normal) Assert.That(client.SetActionPace(pace), Is.True, variant);
                Assert.That(client.SetActionPace(pace), Is.False, "choosing the chosen pace again is inert");
                Assert.That(client.ActionPace, Is.EqualTo(pace));
                yield return null; Canvas.ForceUpdateCanvases();
                var (card, enemy) = PlainChoice(s);
                PlayPlain(s, card, enemy);
                // The pace buttons sit on the action strip, which the battle screen draws only while a committed action plays.
                Canvas.ForceUpdateCanvases();
                AssertReadable(ActiveView(s.Host), t => t == "● " + PaceName(pace), "the chosen pace is marked by text, not color alone");
                float opened = Time.realtimeSinceStartup;
                var rows = client.ActionPresentationRows;
                Assert.That(rows.Count, Is.GreaterThan(1), "a played card has more than its play event");
                string committed = Fingerprint(client.Snapshot!);
                switch (variant)
                {
                    case "reduced":
                        Assert.That(client.ActionPresentationShown, Is.EqualTo(rows.Count), "reduced motion shows every row at once");
                        break;
                    case "skip":
                        Assert.That(client.ActionPresentationShown, Is.EqualTo(1));
                        Assert.That(client.SkipActionPresentation(), Is.True);
                        Assert.That(client.HasActionPresentation, Is.False, "skip ends the display at once");
                        Assert.That(client.SkipActionPresentation(), Is.False, "a repeated skip is inert");
                        break;
                    case "switch":
                        Assert.That(client.ActionPresentationShown, Is.EqualTo(1));
                        Assert.That(client.SetActionPace(Pacing.Reduced), Is.True);
                        Assert.That((client.HasActionPresentation, client.ActionPresentationShown), Is.EqualTo((true, rows.Count)), "reduced motion mid-display shows the rest at once");
                        Assert.That(client.SetActionPace(Pacing.Double), Is.True);
                        break;
                    default:
                        Assert.That(client.ActionPresentationShown, Is.EqualTo(1), "the rows step one at a time");
                        break;
                }
                float deadline = Time.realtimeSinceStartup + 5f;
                while (client.HasActionPresentation && Time.realtimeSinceStartup < deadline)
                {
                    Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed), variant + ": the display never reaches the Run");
                    yield return null;
                }
                Assert.That(client.HasActionPresentation, Is.False, variant + ": the display ends");
                float elapsed = Time.realtimeSinceStartup - opened;
                if (variant == "normal") Assert.That(elapsed, Is.GreaterThanOrEqualTo(Duration(rows, 1f) - 0.05f), "normal pace");
                if (variant == "double") Assert.That(elapsed, Is.GreaterThanOrEqualTo(Duration(rows, 0.5f) - 0.05f), "2x pace");
                if (variant == "reduced") Assert.That(elapsed, Is.GreaterThanOrEqualTo(HoldSeconds - 0.05f), "reduced motion keeps the still hold");
                Assert.That(client.SkipActionPresentation(), Is.False, variant + ": skipping a finished display is inert");
                Assert.That((Time.timeScale, Fingerprint(client.Snapshot!)), Is.EqualTo((scale, committed)), variant + ": only the display changed");

                // The same next actual command in every variant.
                Assert.That(Both(s, client.EndTurn, BattleCore(s, "end", null)), Is.True, variant + ": the next actual end is accepted");
                Dismiss(client);
                results.Add((variant, Fingerprint(client.Snapshot!), StorePrint(client), string.Join("\n", client.BattleLog),
                    string.Join("\n", rows.Select(r => r.Kind + "|" + r.Text + "|" + r.Provenance))));
                yield return null;
            }
            foreach (var result in results.Skip(1))
                Assert.That((result.Run, result.Store, result.Log, result.Rows), Is.EqualTo((results[0].Run, results[0].Store, results[0].Log, results[0].Rows)),
                    result.Variant + " must commit exactly what normal pacing commits");
        }

        // The battle's last action covers the reward or save screen beneath with its sheet: rewards, saves, the next intent and real taps are
        // refused until it ends; it ends in place on its real timer, and a repeated tap at its skip point then reaches no reward or save.
        [UnityTest]
        public IEnumerator TheBattlesLastActionCoversTheNextScreenUntilItEndsInPlace()
        {
            var s = new Session(); yield return Open(s);
            var client = s.Client; bool terminal = false;
            for (int steps = 0; steps < 4000 && !terminal; steps++)
            {
                var state = s.Mirror.State;
                if (state.Phase == "VICTORY" || state.Phase == "DEFEAT") break;
                terminal = Step(s, state);
                if (steps % 8 == 7) yield return null;
            }
            Assert.That(terminal, Is.True, "the bounded actual driver must end a battle with a play or an ultimate");
            var after = client.Snapshot!;
            Assert.That(after.Phase, Is.Not.EqualTo("BATTLE"));
            Assert.That(after.Battle, Is.Null, "the finished battle has left the Run");
            var rows = client.ActionPresentationRows;
            AssertRowsMatch(rows, s.Last!.Events);
            Assert.That(rows.Count(r => r.Kind == BattleDigestKind.BattleEnd), Is.EqualTo(1), "the battle's own end event is shown");
            Assert.That(rows.Any(r => r.Kind == BattleDigestKind.EnemyState), Is.False, "no enemy result is invented once the battle is gone");
            string committed = Fingerprint(after), store = StorePrint(client), pending = CanonicalState.Hash(after.Pending);
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(client.HasActionPresentation, Is.True);
            var view = ActiveView(s.Host);
            var sheet = view.GetComponentsInChildren<Image>().Single(i => i.name == "ActionPresentationSheet");
            AssertReadable(view, t => t == StripText(rows, client.ActionPresentationShown, true), "every shown row on the sheet");
            bool reward = after.Phase == "REWARD";
            var beneath = view.GetComponentsInChildren<Button>().Where(x => reward ? x.name == "Proceed" || x.name.StartsWith("RewardCard:", StringComparison.Ordinal)
                || x.name.StartsWith("Relic:", StringComparison.Ordinal) || x.name == "TakePart" || x.name == "SkipCard" : x.name == "CommitMemory" || x.name == "CloseWithoutSave").ToList();
            Assert.That(beneath, Is.Not.Empty, "the next screen is drawn beneath the sheet");
            var skip = ButtonNamed(s.Host, "SkipActionPresentation"); Vector2 skipPoint = Center(skip);

            var commands = new (string Name, Func<bool> Call)[]
            {
                ("TakeCard", () => client.TakeCard(ListOf(Get(after.Pending, "cards")).Select(x => (string)x!).FirstOrDefault() ?? "sera_round")), ("SkipCard", () => client.SkipCard()),
                ("TakeRelic", () => client.TakeRelic("lucky_coin")), ("TakePart", () => client.TakePart()), ("Evolve", () => client.Evolve("k1", "sera_round")),
                ("Proceed", () => client.Proceed()), ("ToggleChange", () => client.ToggleChange("x")), ("CommitMemory", () => client.CommitMemory()),
                ("CloseWithoutSave", () => client.CloseWithoutSave()), ("Abandon", () => client.Abandon()), ("Depart", () => client.Depart()),
                ("EnterNode", () => client.EnterNode("none")), ("ConfirmCard", () => client.ConfirmCard()), ("EndTurn", () => client.EndTurn())
            };
            foreach (var (label, call) in commands)
            {
                Assert.That(call(), Is.False, label + " must be refused under the battle's last display");
                Assert.That(client.LastError, Is.EqualTo(Showing), label);
                Assert.That((Fingerprint(client.Snapshot!), StorePrint(client), CanonicalState.Hash(client.Snapshot!.Pending)), Is.EqualTo((committed, store, pending)), label);
            }
            var selection = new[] { client.SelectedCard, client.SelectedMode, client.Target };
            foreach (var (label, call) in new (string, Action)[] { ("SelectCard", () => client.SelectCard("c1")), ("SelectMode", () => client.SelectMode("now")), ("SelectEnemy", () => client.SelectEnemy("e1")) })
            {
                call();
                Assert.That(client.LastError, Is.EqualTo(Showing), label + ": no next intent passes the battle's last display");
                Assert.That(new[] { client.SelectedCard, client.SelectedMode, client.Target }, Is.EqualTo(selection), label);
            }
            foreach (var button in beneath)
            {
                Vector2 point = Center(button);
                Assert.That(TopHit(point).GetComponentInParent<Button>(), Is.Not.SameAs(button), button.name + " must be covered by the sheet");
                if (TopHit(point).GetComponentInParent<Button>() == null) TapAt(point);
                Assert.That((Fingerprint(client.Snapshot!), CanonicalState.Hash(client.Snapshot!.Pending)), Is.EqualTo((committed, pending)), "a tap at " + button.name);
            }

            float deadline = Time.realtimeSinceStartup + 6f;
            while (client.HasActionPresentation && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(client.HasActionPresentation, Is.False, "the sheet ends on its real timer");
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(OwnedViews(s.Host), Is.EqualTo(new[] { view }), "the sheet closes in place");
            Assert.That(sheet.gameObject.activeInHierarchy, Is.False, "the sheet is gone");
            Assert.That(SkipPointIsSafe(skipPoint), Is.True, "after the sheet closes its skip point lies over no reward or save control");
            var repeat = new List<RaycastResult>(); var tap = new PointerEventData(EventSystem.current) { position = skipPoint, button = PointerEventData.InputButton.Left };
            EventSystem.current.RaycastAll(tap, repeat);
            if (repeat.Count != 0) ExecuteEvents.ExecuteHierarchy(repeat[0].gameObject, tap, ExecuteEvents.pointerClickHandler); // the repeated tap, wherever it lands
            Assert.That(client.SkipActionPresentation(), Is.False, "a repeated skip is inert");
            Assert.That((Fingerprint(client.Snapshot!), StorePrint(client), CanonicalState.Hash(client.Snapshot!.Pending)), Is.EqualTo((committed, store, pending)), "the repeated tap reached nothing");
            Assert.That(TopHit(Center(beneath[0])).GetComponentInParent<Button>(), Is.SameAs(beneath[0]), "positive control: the next screen is reachable");
            if (reward) Assert.That(Both(s, client.Proceed, r => r.Proceed()), Is.True, "the reward screen accepts its own command now");
            else Assert.That(client.CloseWithoutSave(), Is.True, "the save screen accepts its own command now");
        }
        static bool SkipPointIsSafe(Vector2 point)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left }, hits);
            string? name = hits.Count == 0 ? null : hits[0].gameObject.GetComponentInParent<Button>()?.name;
            return name == null || !(name == "Proceed" || name == "SkipCard" || name == "TakePart" || name == "CommitMemory" || name == "CloseWithoutSave" || name == "AssignButton"
                || name.StartsWith("RewardCard:", StringComparison.Ordinal) || name.StartsWith("Relic:", StringComparison.Ordinal) || name.StartsWith("Evolve", StringComparison.Ordinal)
                || name.StartsWith("Change:", StringComparison.Ordinal) || name.StartsWith("CardInfo:", StringComparison.Ordinal));
        }

        // An actual ultimate, clicked on its real button, is presented as its own committed events (the ult event first), titled as one.
        [UnityTest]
        public IEnumerator AnActualUltimateIsPresentedAsItsOwnCommittedEvents()
        {
            // RULES_NOW_PLAY: battles are shorter, so seed 1234's short run fills no gauge; the next seeds are tried in order, each
            // practice destroyed before the next opens, until one fills a gauge by actual commands.
            var s = new Session(); var found = new Found();
            foreach (long seed in new long[] { Seed, 1, 2, 3, 4, 5, 6, 7, 8 })
            {
                if (s.Host != null) { hosts.Remove(s.Host); Object.Destroy(s.Host); yield return null; s = new Session(); }
                yield return Open(s, seed);
                var rules = s.Data.Memory.Battle.Rules;
                for (int steps = 0; steps < 4000; steps++)
                {
                    var state = s.Mirror.State;
                    if (state.Phase == "VICTORY" || state.Phase == "DEFEAT") break;
                    if (state.Phase == "BATTLE" && state.Battle != null && state.Battle.Phase == "PLAYER")
                    {
                        var ready = state.Battle.Actors.FirstOrDefault(a => a.Gauge >= rules.GaugeMax);
                        if (ready != null) { found.Actor = ready.Id; found.Enemy = state.Battle.Enemies.First(e => e.Alive).Uid; break; }
                    }
                    if (Step(s, state)) FinishAction(s.Client);
                    if (steps % 8 == 7) yield return null;
                }
                if (found.Actor != null) break;
            }
            Assert.That(found.Actor, Is.Not.Null, "an actor's gauge must fill within the bounded actual commands of seeds 1234, 1-8");
            var client = s.Client;
            client.SelectEnemy(found.Enemy!);
            yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(s.Host, "Ult:" + found.Actor));
            var args = BattleArgument.Object(new[] { Pair("actor", found.Actor), Pair("target", found.Enemy) });
            MirrorOf(s, BattleCore(s, "ult", args));
            Assert.That(client.LastError, Is.Null);
            Assert.That(client.HasActionPresentation, Is.True);
            var rows = client.ActionPresentationRows;
            AssertRowsMatch(rows, s.Last!.Events);
            Assert.That((rows[0].Kind, rows[0].Text), Is.EqualTo((BattleDigestKind.Other, "필살기 사용")), "the committed ult event comes first");
            yield return null; Canvas.ForceUpdateCanvases();
            if (client.HasActionPresentation)
                AssertReadable(ActiveView(s.Host), t => t == "필살기 결과 " + client.ActionPresentationShown + "/" + rows.Count, "the display is titled as an ultimate");
            if (!client.ActionPresentationTerminal && client.HasActionPresentation)
                Assert.That(ActiveView(s.Host).GetComponentsInChildren<Button>().Where(x => x.name.StartsWith("Ult:", StringComparison.Ordinal)).All(x => !x.interactable), Is.True,
                    "no other ultimate is sent during the display");
            string committed = Fingerprint(client.Snapshot!);
            if (client.HasActionPresentation) Assert.That(client.SkipActionPresentation(), Is.True);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed));
            AssertSame(s, "skip reached nothing");
        }

        // Focus loss, pause and disable drop the display of the already committed play; resume/enable redraws the actual Run. Nothing is
        // replayed or restarted, and the next actual command is accepted.
        [UnityTest]
        public IEnumerator FocusLossPauseAndDisableDropTheDisplayAndResumeRedrawsWithoutReplay()
        {
            foreach (string way in new[] { "focus", "pause", "disable" })
            {
                var s = new Session(); yield return OpenBattle(s);
                var client = s.Client;
                var (card, enemy) = PlainChoice(s);
                PlayPlain(s, card, enemy);
                yield return null; Canvas.ForceUpdateCanvases();
                Assert.That(client.HasActionPresentation, Is.True);
                var view = ActiveView(s.Host); var endTurn = ButtonNamed(s.Host, "EndTurn");
                string committed = Fingerprint(client.Snapshot!); string? runId = client.Snapshot!.RunId; int version = client.Snapshot!.Battle!.V;
                var log = client.BattleLog;
                if (way == "focus") s.Host.SendMessage("OnApplicationFocus", false);
                else if (way == "pause") s.Host.SendMessage("OnApplicationPause", true);
                else s.Host.SetActive(false);
                Assert.That(client.HasActionPresentation, Is.False, way + " drops the committed display");
                if (way != "disable") Assert.That(endTurn.interactable, Is.True, way + ": the controls are released in place");
                Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed), way + " never reaches the Run");
                yield return null;
                if (way == "focus") s.Host.SendMessage("OnApplicationFocus", true);
                else if (way == "pause") s.Host.SendMessage("OnApplicationPause", false);
                else s.Host.SetActive(true);
                yield return null; Canvas.ForceUpdateCanvases();
                var redrawn = SingleView(s.Host);
                Assert.That(redrawn, Is.Not.SameAs(view), way + ": resuming redraws the actual view");
                Assert.That(client.HasActionPresentation, Is.False, way + ": nothing is shown again");
                var b = s.Mirror.State.Battle!;
                Assert.That(redrawn.GetComponentsInChildren<Button>().Where(x => x.name.StartsWith("Hand:", StringComparison.Ordinal)).Select(x => x.name.Substring(5)), Is.EqualTo(b.Zones.Hand));
                Assert.That(ButtonNamed(s.Host, "EndTurn").interactable, Is.True);
                Assert.That((Fingerprint(client.Snapshot!), client.Snapshot!.RunId, client.Snapshot!.Battle!.V), Is.EqualTo((committed, runId, version)), way + ": no restart and no replayed command");
                Assert.That(client.BattleLog, Is.EqualTo(log), way + ": no event row is logged twice");
                AssertSame(s, way + ": the mirror is unchanged");
                Assert.That(Both(s, client.EndTurn, BattleCore(s, "end", null)), Is.True, way + ": the next actual end is accepted");
                Dismiss(client);
            }
        }

        // A press held on a next hand card while the display finishes: completion rewrites in place, so the same button, canvas and
        // EventSystem keep the press, the end becomes available on the same button, and the release is exactly one pure selection.
        [UnityTest]
        public IEnumerator AHeldNextCardPressSurvivesTheDisplayCompletionAndItsReleaseSelectsOnce()
        {
            var s = new Session(); yield return OpenBattle(s);
            var client = s.Client;
            var (card, enemy) = PlainChoice(s);
            PlayPlain(s, card, enemy);
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(client.HasActionPresentation, Is.True, "the press must start during the display");
            var view = ActiveView(s.Host); var events = EventSystem.current;
            Assert.That(events, Is.Not.Null);
            string? other = s.Mirror.State.Battle!.Zones.Hand.FirstOrDefault(u => u != client.SelectedCard);
            Assert.That(other, Is.Not.Null, "a second hand card to press");
            var button = ButtonNamed(s.Host, "Hand:" + other); var endTurn = ButtonNamed(s.Host, "EndTurn");
            var status = view.GetComponentsInChildren<Text>().Single(t => t.text.StartsWith("카드 사용 결과", StringComparison.Ordinal));
            Assert.That(endTurn.interactable, Is.False);
            string committed = Fingerprint(client.Snapshot!);

            var pointer = new PointerEventData(events) { position = Center(button), button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); events.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), "the hand card must be the top hit at its own center");
            pointer.pointerPressRaycast = hits[0]; pointer.pointerCurrentRaycast = hits[0]; pointer.pressPosition = pointer.position; pointer.eligibleForClick = true;
            pointer.pointerPress = ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerDownHandler) ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
            pointer.rawPointerPress = hits[0].gameObject;
            Assert.That(pointer.pointerPress, Is.SameAs(button.gameObject), "the hand card takes its own press");

            float deadline = Time.realtimeSinceStartup + 5f;
            while (client.HasActionPresentation && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null; // a replaced view would linger inactive until the end of its frame
            Assert.That(client.HasActionPresentation, Is.False, "the display finished during the held press");
            Assert.That(OwnedViews(s.Host), Is.EqualTo(new[] { view }), "completion must not rebuild the view under a held press");
            Assert.That(button != null && button.IsActive() && button.interactable, Is.True, "the held card is the same live, interactable button");
            Assert.That(EventSystem.current, Is.SameAs(events), "the same EventSystem keeps the press");
            Assert.That(endTurn.interactable, Is.True, "the same end button became available in place");
            Assert.That(status != null && status.isActiveAndEnabled && status.text == "행동 결과", Is.True, "the same strip label reads idle");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed));

            events.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject), Is.SameAs(pointer.pointerPress), "the release lands on the pressed card");
            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That((client.LastError, client.SelectedCard), Is.EqualTo(((string?)null, other)), "the release is exactly one selection");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed), "selecting never reaches the Run");
            AssertSame(s, "nothing reached the mirror");
        }

        // During the display a pick card's confirm opens only its pure local sheet (P1-CARD-FLOW-ONE-UNSENT-INTENT), whose lock comes first;
        // after it the sheet opens and keeps its own lock as before. A summary refuses with its own code and has no display beneath it.
        [UnityTest]
        public IEnumerator PickSheetAndTurnSummaryKeepTheirOwnLocksAroundTheDisplay()
        {
            var s = new Session(); yield return Open(s);
            var client = s.Client; var found = new Found();
            for (int steps = 0; steps < 3000 && found.Card == null; steps++)
            {
                var state = s.Mirror.State;
                if (state.Phase == "VICTORY" || state.Phase == "DEFEAT") break;
                if (state.Phase == "BATTLE" && state.Battle != null && state.Battle.Phase == "PLAYER")
                {
                    var b = state.Battle; string enemy = b.Enemies.First(e => e.Alive).Uid;
                    string? tune = b.Zones.Hand.FirstOrDefault(u => b.Cards[u].Id == "nia_tune" && s.Mirror.BattleCheck(u, "now", enemy) == null);
                    string? plain = tune == null ? null : b.Zones.Hand.FirstOrDefault(u => u != tune && Plain(s, u, enemy)
                        && s.Mirror.BattleCostOf(u, "now") + s.Mirror.BattleCostOf(tune, "now") <= b.Ap);
                    if (plain != null) { found.Card = tune; found.Other = plain; found.Enemy = enemy; break; }
                }
                if (Step(s, state)) FinishAction(client);
                if (steps % 8 == 7) yield return null;
            }
            Assert.That(found.Card, Is.Not.Null, "the reference kit's nia_tune and an affordable immediate card must meet within the bounded actual commands");
            string tuneCard = found.Card!, enemyUid = found.Enemy!;
            PlayPlain(s, found.Other!, enemyUid);
            Assert.That(s.Mirror.BattleCheck(tuneCard, "now", enemyUid), Is.Null, "precondition: nia_tune is still playable after the first card");
            string committed = Fingerprint(client.Snapshot!);

            // During the display the pick card is prepared. P1-CARD-FLOW-ONE-UNSENT-INTENT: its confirm now opens the sheet as a pure local
            // choice (the unsent intent's own picker); the sheet's lock comes before the display's, and cancelling it sends nothing.
            client.SelectEnemy(enemyUid); client.SelectCard(tuneCard); client.SelectMode("now");
            Assert.That((client.LastError, client.SelectedCard), Is.EqualTo(((string?)null, (string?)tuneCard)));
            Assert.That(client.ConfirmCard(), Is.False);
            Assert.That((client.LastError, client.PickerOpen, client.HasActionPresentation, client.HasPreparedIntent), Is.EqualTo(((string?)null, true, true, false)),
                "the sheet opens during the display, spending nothing");
            Assert.That(client.EndTurn(), Is.False);
            Assert.That(client.LastError, Is.EqualTo(SheetOpen), "the sheet's lock comes before the display's");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed));
            client.CancelPick();
            Assert.That((client.PickerOpen, client.HasPreparedIntent, client.LastError), Is.EqualTo((false, false, (string?)null)));
            Assert.That(client.AutoPick() || client.PickCard(tuneCard), Is.False, "the cancelled sheet's pick and auto reach nothing");
            Assert.That(client.HasPreparedIntent, Is.False);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed));
            AssertSame(s, "opening and cancelling the sheet during the display sent nothing");
            FinishAction(client);

            // After the display the sheet opens as before and keeps its own lock.
            Assert.That(client.ConfirmCard(), Is.False);
            Assert.That(client.PickerOpen, Is.True, "the sheet opens after the display, spending nothing");
            string other = s.Mirror.State.Battle!.Zones.Hand.FirstOrDefault(u => u != tuneCard) ?? "c1";
            client.SelectCard(other);
            Assert.That((client.LastError, client.SelectedCard), Is.EqualTo(((string?)SheetOpen, (string?)tuneCard)), "the sheet's lock is unchanged");
            Assert.That(client.EndTurn(), Is.False);
            Assert.That(client.LastError, Is.EqualTo(SheetOpen));
            Assert.That(client.SkipActionPresentation(), Is.False, "no display is open beneath the sheet");
            client.CancelPick();
            Assert.That((client.PickerOpen, client.LastError), Is.EqualTo((false, (string?)null)));
            AssertSame(s, "nothing reached the Run");

            // An actual end opens the turn summary, which refuses with its own code; no display lies beneath it.
            Assert.That(Both(s, client.EndTurn, BattleCore(s, "end", null)), Is.True);
            Assert.That((client.HasTurnSummary, client.HasActionPresentation), Is.EqualTo((true, false)));
            client.SelectCard(other);
            Assert.That(client.LastError, Is.EqualTo(Barrier), "the summary's barrier comes first");
            Assert.That(client.SkipActionPresentation(), Is.False);
            Dismiss(client);
        }

        // A display refuses the end before the Run, so no failure can start beneath it. After the display a failed end quarantines: the
        // recovery code comes first, no display survives or reappears through disable/enable or resume, and only the recovery action remains.
        [UnityTest]
        public IEnumerator RecoveryQuarantineOutranksAndOutlivesTheDisplay()
        {
            var s = new Session(); yield return OpenBattle(s);
            var client = s.Client;
            var (card, enemy) = PlainChoice(s);
            PlayPlain(s, card, enemy);
            string committed = Fingerprint(client.Snapshot!), store = StorePrint(client);
            var live = s.Mirror.State.Battle!;
            // Test-local fault in the client's own content: every move of the enemies in this battle gets an unknown type.
            var moves = ClientData(client).Memory.Battle.Enemies.Where(e => live.Enemies.Any(x => x.Id == e.Id)).SelectMany(e => e.Pattern).Select(m => (Move: m, Type: m.Type)).ToList();
            Assert.That(moves, Is.Not.Empty);
            try
            {
                foreach (var entry in moves) entry.Move.Type = "test_unknown_move";
                Assert.That(client.EndTurn(), Is.False);
                Assert.That((client.LastError, client.RecoveryRequired, client.HasActionPresentation), Is.EqualTo(((string?)Showing, false, true)),
                    "the display refuses the end before it reaches the faulty Run");
                Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed));
                Assert.That(client.SkipActionPresentation(), Is.True);
                LogAssert.Expect(LogType.Error, new Regex(Diagnostic + @"BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED\)"));
                Assert.That(client.EndTurn(), Is.False);
                Assert.That((client.RecoveryRequired, client.HasActionPresentation, client.HasTurnSummary), Is.EqualTo((true, false, false)), "a failed end quarantines");
                Assert.That(StorePrint(client), Is.EqualTo(store), "the session store is kept");
                foreach (var (label, call) in new (string, Func<bool>)[] { ("ConfirmCard", () => client.ConfirmCard()), ("EndTurn", () => client.EndTurn()), ("SkipAction", () => client.SkipActionPresentation()) })
                {
                    Assert.That(call(), Is.False, label);
                    if (label != "SkipAction") Assert.That(client.LastError, Is.EqualTo(Recovery), label);
                }
                client.SelectCard(card);
                Assert.That(client.LastError, Is.EqualTo(Recovery), "recovery outranks the next selection");
                Assert.That(client.SetActionPace(Pacing.Double), Is.True, "the pace is a display preference");
                Assert.That(client.RecoveryRequired, Is.True, "choosing a pace does not leave recovery");
                yield return null; Canvas.ForceUpdateCanvases();
                Assert.That(SingleView(s.Host).GetComponentsInChildren<Button>().Select(x => x.name), Is.EqualTo(new[] { "RecoveryDiscard", "RecoveryHome" }), "only explicit recovery and home actions");
                s.Host.SendMessage("OnApplicationFocus", false); s.Host.SendMessage("OnApplicationFocus", true);
                s.Host.SendMessage("OnApplicationPause", true); s.Host.SendMessage("OnApplicationPause", false);
                s.Host.SetActive(false);
                yield return null;
                s.Host.SetActive(true);
                yield return null; Canvas.ForceUpdateCanvases();
                Assert.That((client.RecoveryRequired, client.HasActionPresentation), Is.EqualTo((true, false)), "the quarantine survives; no display comes back");
                Assert.That(SingleView(s.Host).GetComponentsInChildren<Button>().Select(x => x.name), Is.EqualTo(new[] { "RecoveryDiscard", "RecoveryHome" }));
            }
            finally
            {
                foreach (var entry in moves) entry.Move.Type = entry.Type; // fault removed
            }
            Assert.That(client.DiscardFailedPractice(), Is.True, client.LastError);
            Assert.That((client.RecoveryRequired, client.HasActionPresentation, client.Snapshot!.RunId), Is.EqualTo((false, false, "practice-2-" + (Seed + 1))));
            Assert.That(StorePrint(client), Is.EqualTo(store), "nothing of the failed Run was saved");
        }

        // ───────── expected rows (independent of the client's digest projection) ─────────
        // One row per actual Core event in Core's order, each of the kind its event names, then only enemy results; no summary framing.
        static void AssertRowsMatch(IReadOnlyList<BattleDigestEntry> rows, List<BattleEvent> events)
        {
            Assert.That(rows.Any(r => r.Kind == BattleDigestKind.Summary || r.Kind == BattleDigestKind.PreviousIntent), Is.False, "no turn-summary framing");
            var eventRows = rows.TakeWhile(r => r.Kind != BattleDigestKind.EnemyState).ToList();
            Assert.That(rows.Skip(eventRows.Count).All(r => r.Kind == BattleDigestKind.EnemyState), Is.True, "enemy results come last");
            Assert.That(eventRows.Count, Is.EqualTo(events.Count), "one row per actual event of the command");
            for (int i = 0; i < events.Count; i++)
            {
                var kind = ExpectedKind(events[i]);
                if (kind.HasValue) Assert.That(eventRows[i].Kind, Is.EqualTo(kind.Value), "row " + i + " for event " + events[i].E);
                Assert.That(MemoryForgeExpeditionClient.ActionRowText(eventRows[i]), Is.EqualTo(ExpectedTag(eventRows[i]) + eventRows[i].Text), "the kind label of row " + i);
            }
        }
        static BattleDigestKind? ExpectedKind(BattleEvent e)
        {
            switch (e.E)
            {
                case "play": return e.Mode == "next" || e.Mode == "after" ? BattleDigestKind.ReservationPlayed : BattleDigestKind.CardPlayed;
                case "reserve": return BattleDigestKind.ReservationRegistered;
                case "resolved": return string.IsNullOrEmpty(e.Reserved) ? BattleDigestKind.CardResolved : BattleDigestKind.ReservationResolved;
                case "echoCopy": return BattleDigestKind.Copy;
                case "dmg":
                    return e.Source == "card" ? BattleDigestKind.CardDamage : e.Source == "ult" ? BattleDigestKind.UltimateDamage : e.Source == "counter" ? BattleDigestKind.CounterDamage
                        : e.Source == "relic" ? BattleDigestKind.RelicDamage : e.Source == "burn" ? BattleDigestKind.BurnDamage : BattleDigestKind.EffectDamage;
                case "kill": return BattleDigestKind.Kill;
                case "break": return BattleDigestKind.Break;
                case "block": return BattleDigestKind.PartyBlock;
                case "end": return BattleDigestKind.BattleEnd;
                case "ult": return BattleDigestKind.Other;
                default: return null;
            }
        }
        // play / reservation / resolution / echo / relic / counter / ultimate are labelled apart; other rows get a plain bullet.
        static string ExpectedTag(BattleDigestEntry row)
        {
            switch (row.Kind)
            {
                case BattleDigestKind.CardPlayed: return "[사용] ";
                case BattleDigestKind.ReservationPlayed: case BattleDigestKind.ReservationRegistered: return "[예약] ";
                case BattleDigestKind.CardResolved: case BattleDigestKind.ReservationResolved: return "[해결] ";
                case BattleDigestKind.Copy: return "[메아리] ";
                case BattleDigestKind.CounterDamage: return "[반격] ";
                case BattleDigestKind.UltimateDamage: return "[필살기] ";
                default: return row.Provenance == "유물" ? "[유물] " : "· ";
            }
        }
        // The strip shows the two latest shown rows; the battle's last display shows every shown row.
        static string StripText(IReadOnlyList<BattleDigestEntry> rows, int shown, bool all = false) =>
            string.Join("\n", rows.Take(shown).Skip(all ? 0 : Math.Max(0, shown - 2)).Select(MemoryForgeExpeditionClient.ActionRowText));
        // The documented local timing of a whole display: every row after the first waits its step, then the hold; 2x halves both.
        static float Duration(IReadOnlyList<BattleDigestEntry> rows, float scale) =>
            (rows.Skip(1).Sum(r => r.IsReservedResolution ? ReservedSeconds : RowSeconds) + HoldSeconds) * scale;
        static string PaceName(Pacing pace) => pace == Pacing.Double ? "2배속" : pace == Pacing.Reduced ? "동작 줄이기" : "보통";

        // ───────── drivers: one public command per call, mirrored on the Core Run ─────────
        static Action<CoreRun> BattleCore(Session s, string kind, BattleArgument? args) => r =>
        {
            var response = r.BattleCommand(kind, args);
            if (ReferenceEquals(r, s.Mirror)) s.Last = response;
        };
        static bool Both(Session s, Func<bool> view, Action<CoreRun> core)
        {
            bool ok = view(); string? code = null;
            try { core(s.Mirror); }
            catch (RuleException error) { code = error.Code; }
            Assert.That(ok, Is.EqualTo(code == null), "view and Core disagree: view " + s.Client.LastError + " / Core " + code);
            if (!ok) Assert.That(s.Client.LastError, Is.EqualTo(code));
            AssertSame(s, "the view must issue exactly the actual Core command");
            return ok;
        }
        // The Core side of a command a real tap already sent to the view.
        static void MirrorOf(Session s, Action<CoreRun> core)
        {
            core(s.Mirror);
            AssertSame(s, "the tap must be exactly the actual Core command");
        }
        static void Dismiss(MemoryForgeExpeditionClient client)
        {
            Assert.That(client.HasTurnSummary, Is.True, "an actual end opens the turn summary");
            string before = Fingerprint(client.Snapshot!);
            Assert.That(client.SkipTurnSummary() && client.ContinueTurnSummary(), Is.True);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "skip and continue never reach the Run");
        }
        static void FinishAction(MemoryForgeExpeditionClient client)
        {
            Assert.That(client.HasActionPresentation, Is.True, "an actual play or ultimate opens its display");
            string before = Fingerprint(client.Snapshot!);
            Assert.That(client.SkipActionPresentation(), Is.True);
            Assert.That(client.SkipActionPresentation(), Is.False, "a repeated skip is inert");
            Assert.That((client.HasActionPresentation, Fingerprint(client.Snapshot!)), Is.EqualTo((false, before)), "finishing the display never reaches the Run");
        }
        // The existing bounded driver order. Each non-terminal display is finished explicitly; returns true when the command sent was a
        // battle's last action, whose display is left open for the caller.
        static bool Step(Session s, RunState state)
        {
            var client = s.Client; var p = state.Pending; var events = s.Data.Events;
            switch (state.Phase)
            {
                case "MAP": { string id = s.Mirror.Available()[0]; Both(s, () => client.EnterNode(id), r => r.Enter(id)); return false; }
                case "BATTLE": return BattleStep(s, state.Battle!);
                case "REWARD":
                    {
                        var evolutions = ListOf(Get(p, "evolutions")); var cards = ListOf(Get(p, "cards")); var relics = ListOf(Get(p, "relic"));
                        if (evolutions.Count != 0)
                        {
                            string key = (string)Get(evolutions[0], "key")!, branch = (string)ListOf(Get(evolutions[0], "options"))[0]!;
                            if (Both(s, () => client.Evolve(key, branch), r => r.Evolve(key, branch))) return false;
                        }
                        else if (cards.Count != 0) { string id = (string)cards[0]!; if (Both(s, () => client.TakeCard(id), r => r.TakeCard(id))) return false; }
                        else if (relics.Count != 0) { string id = (string)relics[0]!; if (Both(s, () => client.TakeRelic(id), r => r.TakeRelic(id))) return false; }
                        else if (Get(p, "part") is string) { if (Both(s, client.TakePart, r => r.TakePart())) return false; }
                        Both(s, client.Proceed, r => r.Proceed());
                        return false;
                    }
                case "REST":
                    if (Integer(Get(p, "uses")) > 0)
                    {
                        var plain = state.Deck.FirstOrDefault(c => !c.Weapon && !Truthy(Get(c.ToJsonGraph(), "plus")));
                        if (state.Hp * 10 < state.MaxHp * 6 || plain == null) { if (Both(s, client.RestHeal, r => r.Rest("heal"))) return false; }
                        else { string key = plain.Key!; if (Both(s, () => client.RestUpgrade(key), r => r.Rest("upgrade", key))) return false; }
                    }
                    Both(s, client.Proceed, r => r.Proceed());
                    return false;
                case "FORGE":
                    {
                        var offers = Get(p, "offers"); var parts = ListOf(Get(offers, "parts"));
                        if (parts.Count != 0) { string id = (string)parts[0]!; if (Both(s, () => client.ForgeApply(id), r => r.ForgeApply(id))) return false; }
                        else if (offers == null && Integer(Get(p, "uses")) > 0)
                        {
                            string key = state.Deck.First(c => !c.Weapon).Key!;
                            if (Both(s, () => client.ForgeOffer(key), r => r.ForgeOffer(key))) return false;
                        }
                        Both(s, client.Proceed, r => r.Proceed());
                        return false;
                    }
                case "SHOP":
                    {
                        var cards = ListOf(Get(p, "cards"));
                        bool bought = cards.Concat(ListOf(Get(p, "parts"))).Concat(ListOf(Get(p, "relics"))).Any(x => Truthy(Get(x, "sold")));
                        var item = cards.FirstOrDefault(x => !Truthy(Get(x, "sold")) && Integer(Get(x, "price")) <= state.Coins);
                        if (!bought && item != null) { string id = (string)Get(item, "id")!; if (Both(s, () => client.Buy("cards", id), r => r.Buy("cards", id))) return false; }
                        Both(s, client.Proceed, r => r.Proceed());
                        return false;
                    }
                case "EVENT":
                    {
                        if (Truthy(Get(p, "done"))) { Both(s, client.Proceed, r => r.Proceed()); return false; }
                        string eventId = (string)Get(p, "id")!; var definition = events.Last(e => e.Id == eventId);
                        for (int i = 0; i < definition.Choices.Count; i++)
                        {
                            var fx = definition.Choices[i].Effects; int index = i;
                            if (definition.Choices[i].RequiredCoins.GetValueOrDefault() > state.Coins) continue;
                            int count = fx.CardCopy.GetValueOrDefault() != 0 ? 1 : fx.CardRemove.GetValueOrDefault();
                            var keys = count > 0 ? state.Deck.Where(c => !c.Weapon).Select(c => c.Key!).Take(count).ToList() : null;
                            if (Both(s, () => client.ChooseEvent(index, keys), r => r.EventChoose(index, keys?.Cast<object?>().ToList()))) return false;
                        }
                        Assert.Fail("no choice of event " + eventId + " was accepted");
                        return false;
                    }
                case "ARCHIVE": Both(s, () => client.Archive("return"), r => r.Archive("return")); return false;
                default: Assert.Fail("unexpected expedition phase " + state.Phase); return false;
            }
        }
        static bool BattleStep(Session s, BattleState b)
        {
            var client = s.Client; var mirror = s.Mirror; var content = s.Data.Memory.Battle;
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
                    if (Both(s, client.AutoPick, BattleCore(s, "play", Play(card, "now", enemy)))) return Played(s);
                }
                else if (Both(s, client.ConfirmCard, BattleCore(s, "play", Play(card, "now", enemy)))) return Played(s);
            }
            var ready = b.Actors.FirstOrDefault(a => a.Gauge >= content.Rules.GaugeMax);
            if (ready != null)
            {
                client.SelectEnemy(enemy);
                var args = BattleArgument.Object(new[] { Pair("actor", ready.Id), Pair("target", enemy) });
                if (Both(s, () => client.UseUltimate(ready.Id), BattleCore(s, "ult", args))) return Played(s);
            }
            if (Both(s, client.EndTurn, BattleCore(s, "end", null))) Dismiss(client);
            return false;
        }
        // After an actual play or ultimate: the display is open, terminal exactly when the battle has left the Run.
        static bool Played(Session s)
        {
            var client = s.Client;
            Assert.That(client.HasActionPresentation, Is.True, "an actual play or ultimate opens its display");
            Assert.That(client.ActionPresentationTerminal, Is.EqualTo(s.Mirror.State.Phase != "BATTLE"), "terminal exactly when the battle has ended");
            if (client.ActionPresentationTerminal) return true;
            FinishAction(client);
            return false;
        }

        // ───────── seams, comparison and small copies of the expedition tests' helpers ─────────
        static bool Do(MemoryForgeExpeditionClient client, Action command) =>
            (bool)typeof(MemoryForgeExpeditionClient).GetMethod("Do", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(client, new object?[] { command, null, true })!;
        static RunData ClientData(MemoryForgeExpeditionClient client) =>
            (RunData)typeof(MemoryForgeExpeditionClient).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
        static string StorePrint(MemoryForgeExpeditionClient client) =>
            JToken.FromObject(typeof(MemoryForgeExpeditionClient).GetField("store", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!).ToString(Formatting.None);
        static void AssertSame(Session s, string message) => Assert.That(Fingerprint(s.Client.Snapshot!), Is.EqualTo(Fingerprint(s.Mirror.State)), message);
        // CanonicalState omits "log"/"ev", so the Run log and the battle log/events are hashed separately for a complete comparison.
        static string Fingerprint(RunState state)
        {
            var graph = state.ToJsonGraph();
            string battle = graph["battle"] is Dictionary<string, object?> b ? CanonicalState.Hash(b["log"]) + CanonicalState.Hash(b["ev"]) : "-";
            return CanonicalState.Hash(new Dictionary<string, object?> { ["run"] = graph, ["memory"] = null }) + "/" + CanonicalState.Hash(graph["log"]) + "/" + battle;
        }
        static BattleArgument Play(string uid, string mode, string? target) => BattleArgument.Object(new[] { Pair("uid", uid), Pair("mode", mode), Pair("target", target),
            new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array(new BattleArgument[0])) });
        static KeyValuePair<string, BattleArgument> Pair(string key, string? value) => new KeyValuePair<string, BattleArgument>(key, value == null ? BattleArgument.Null() : BattleArgument.String(value));
        static object? Get(object? node, string key) => node is IDictionary<string, object?> map && map.TryGetValue(key, out var value) ? value : null;
        static List<object?> ListOf(object? value) => value is IList list ? list.Cast<object?>().ToList() : new List<object?>();
        static bool Truthy(object? value) => value is bool flag ? flag : value != null;
        static long Integer(object? value) => value is int n ? n : value is long l ? l : 0;
        static Vector2 Center(Button button)
        {
            var rect = button.GetComponent<RectTransform>();
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }
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
