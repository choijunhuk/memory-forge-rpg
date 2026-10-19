// P1-CARD-FLOW-ONE-UNSENT-INTENT §14.2/§14.3/§14.7: Play Mode checks of the expedition view's one confirmed, unsent next play over the
// real shared-core Run and the exported full content. During a non-terminal player-action display the player may confirm at most one
// next card (a local UI bound, not a measured value or a reservation slot); nothing reaches the Run until, on the Update after the
// display closes, exactly that frozen request is sent as one Run.BattleCommand or dropped with its reason. Commands go through the
// component's public methods or a real EventSystem raycast; an independent mirror Run receives the same public Core commands and must
// stay identical (the whole Run graph with AP/RNG/log/battle events, and the whole session store), so no Run state is injected.
// Witnesses (a second card after a plain play, a next-turn reservation, nia_tune, a finishing card) are found by the bounded actual
// driver and asserted, never skipped. Documented seams, as in the earlier card-flow tests (no production hook): (1) the client's private
// live Run is read by reflection to issue one real public Core command beneath a waiting intent, mirrored on the mirror Run (the public
// gate refuses every such command); (2) the private session store is read, and the private Do boundary called, by reflection; (3) the
// client's own parsed content gets a test-local enemy-move fault, restored in finally, to reach recovery. Not server/ACK/reconnect,
// Android/device, final VFX or measured-timing acceptance.
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
    public class PreparedIntentPlayModeTests
    {
        const long Seed = 1234;
        const string Showing = "ACTION_SHOWING", Pending = "INTENT_PENDING", Stale = "INTENT_STALE", Dropped = "INTENT_DROPPED",
            SheetOpen = "PICKER_OPEN", Barrier = "TURN_SUMMARY_OPEN", Recovery = "RECOVERY_REQUIRED";
        const string Diagnostic = @"^\[MemoryForge\] Practice expedition quarantined after a failed local Run calculation \(";
        // The documented local prototype timing of a player-action display (not a measured or approved target).
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
        // The plain card played first (its display stays open), the second card a witness asked for and its target.
        sealed class Found { public string? First, Card, Enemy; }

        // The seed is set on a disabled host before the component's real OnEnable start; nothing else is injected.
        IEnumerator Open(Session s, long seed = Seed)
        {
            var host = new GameObject("PreparedIntentTestHost"); hosts.Add(host);
            host.SetActive(false);
            var client = host.AddComponent<MemoryForgeExpeditionClient>(); client.Seed = seed; client.Length = "short";
            host.SetActive(true);
            yield return null;
            s.Host = host; s.Client = client; s.Data = ClientRunContent.Load();
            s.Mirror = CoreRun.Create(s.Data, MemoryForgeExpeditionClient.PracticeSetup(seed, "short", "practice-1-" + seed, new Dictionary<string, object?>(), 0));
            AssertSame(s, "the view's Run must be exactly Create of the public PracticeSetup");
            Assert.That((client.HasActionPresentation, client.HasPreparedIntent), Is.EqualTo((false, false)), "a fresh view has no display and no waiting intent");
        }

        // Actual commands through the bounded driver until a plain immediate play leaves its non-terminal display open with a second hand
        // card `want` accepts against the first living enemy of the live state after that play. That display stays open for the caller.
        IEnumerator PlayThenFind(Session s, Found found, Func<Session, string, string?> want, string witness)
        {
            var client = s.Client;
            for (int steps = 0; steps < 4000 && found.Card == null; steps++)
            {
                var state = s.Mirror.State;
                if (state.Phase == "VICTORY" || state.Phase == "DEFEAT") break;
                if (state.Phase == "BATTLE" && state.Battle != null && state.Battle.Phase == "PLAYER")
                {
                    var b = state.Battle; string enemy = b.Enemies.First(e => e.Alive).Uid;
                    string? first = b.Zones.Hand.FirstOrDefault(u => Plain(s, u, enemy));
                    if (first != null)
                    {
                        PlayPlain(s, first, enemy);
                        var after = s.Mirror.State.Battle;
                        if (!client.ActionPresentationTerminal && after != null)
                        {
                            string target = after.Enemies.First(e => e.Alive).Uid;
                            string? card = want(s, target);
                            if (card != null) { found.First = first; found.Card = card; found.Enemy = target; break; }
                        }
                        FinishAction(client);
                        if (steps % 8 == 7) yield return null;
                        continue;
                    }
                }
                if (Step(s, state)) FinishAction(client);
                if (steps % 8 == 7) yield return null;
            }
            Assert.That(found.Card, Is.Not.Null, "the bounded actual commands must reach " + witness);
            Assert.That((client.HasActionPresentation, client.ActionPresentationTerminal, client.HasPreparedIntent, client.PickerOpen), Is.EqualTo((true, false, false, false)),
                "the first play's display is open and nothing waits yet");
        }
        // Witnesses, each asked of the mirror's live state right after the first play.
        static string? NextPlain(Session s, string enemy) => s.Mirror.State.Battle!.Zones.Hand.FirstOrDefault(u => Plain(s, u, enemy));
        // The reference kit's nia_tune (one hand card to pick), playable now with at least one other hand card Core's precheck accepts as its pick.
        // P1-CARD-FLOW-EFFECT-CANDIDATES: that card must also be one the tune effect itself takes (no amp yet, rarity not C), since the sheet
        // now offers only those.
        static string? Tune(Session s, string enemy)
        {
            var b = s.Mirror.State.Battle!; var cards = s.Data.Memory.Battle.Cards;
            return b.Zones.Hand.FirstOrDefault(u => b.Cards[u].Id == "nia_tune" && s.Mirror.BattleCheck(u, "now", enemy) == null
                && b.Zones.Hand.Any(c => c != u && !b.Cards[c].Temp.Contains("amp") && cards.Single(d => d.Id == b.Cards[c].Id).Rarity != "C"
                    && s.Mirror.BattleCheck(u, "now", enemy, new[] { c }) == null));
        }
        // A plain card whose exact forecast (Core's pure play on a copy, no hidden draw/random outcome) defeats the last living enemy.
        static string? Finisher(Session s, string enemy)
        {
            var b = s.Mirror.State.Battle!; var alive = b.Enemies.Where(e => e.Alive).ToList();
            if (alive.Count != 1 || alive[0].Uid != enemy) return null;
            return b.Zones.Hand.FirstOrDefault(u =>
            {
                if (!Plain(s, u, enemy)) return false;
                var f = s.Mirror.BattleForecast(u, "now", enemy);
                return f.Error == null && !f.Hidden && f.EnemyHpLoss >= alive[0].Hp;
            });
        }
        static bool Plain(Session s, string uid, string enemy) => s.Mirror.BattleAllowedModes(uid).Contains("now") && s.Mirror.BattleCheck(uid, "now", enemy) == null
            && MemoryForgeExpeditionClient.ChoiceRequests(s.Mirror.BattleEffectiveCard(uid), "now", s.Data.Memory.Battle).Count == 0;
        // Selects and confirms one plain card through the public handlers with no display open; exactly one actual play on both Runs.
        static void PlayPlain(Session s, string card, string enemy)
        {
            s.Client.SelectEnemy(enemy); s.Client.SelectCard(card); s.Client.SelectMode("now");
            Assert.That(Both(s, s.Client.ConfirmCard, BattleCore(s, "play", Play(card, "now", enemy))), Is.True);
            Assert.That(s.Client.HasActionPresentation, Is.True, "an actual play opens its display");
        }
        // Selects a request and confirms it during the open display: it only prepares the one unsent intent.
        static void Prepare(Session s, string card, string mode, string enemy)
        {
            var client = s.Client;
            Assert.That(client.HasActionPresentation, Is.True, "the confirm must be made during the display");
            client.SelectEnemy(enemy); client.SelectCard(card); client.SelectMode(mode);
            Assert.That(client.ConfirmCard(), Is.False, "a confirm during the display commits nothing");
            Assert.That((client.LastError, client.HasPreparedIntent, client.PreparedCard, client.PreparedMode, client.PreparedTarget),
                Is.EqualTo(((string?)null, true, (string?)card, (string?)mode, (string?)enemy)), "exactly the confirmed request waits");
            AssertSame(s, "preparing sends nothing");
        }

        // A confirm by real rays during the display is one pure unsent intent: AP, cards, RNG, log, events, time slots and receipts stay;
        // its label and cancel replace the confirm; a second confirm, a changed selection and every command are refused; the natural end
        // closes the display in place without sending; the next Update sends exactly the frozen request once and opens its own display;
        // later Updates and skips send nothing more.
        [UnityTest]
        public IEnumerator AConfirmDuringTheDisplayIsOnePureUnsentIntentThatTheNaturalEndSendsOnce()
        {
            var s = new Session(); yield return Open(s);
            var found = new Found(); yield return PlayThenFind(s, found, NextPlain, "an immediate card without picks after a plain play");
            var client = s.Client; string next = found.Card!, target = found.Enemy!;
            var b = s.Mirror.State.Battle!; string name = s.Mirror.BattleEffectiveCard(next).Name;
            string committed = Fingerprint(client.Snapshot!), store = StorePrint(client);
            yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(s.Host, "Enemy:" + target)); yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(s.Host, "Hand:" + next)); yield return null; Canvas.ForceUpdateCanvases();
            // RULES_NOW_PLAY: no mode button; the card's only mode, now, is chosen with the card.
            var confirm = ButtonNamed(s.Host, "ConfirmCard");
            Assert.That((client.HasActionPresentation, confirm.interactable, confirm.GetComponentInChildren<Text>().text), Is.EqualTo((true, true, "사용 대기")),
                "during the display a ready plain confirm says it prepares");
            Click(confirm);
            Assert.That(client.LastError, Is.Null);
            Assert.That((client.HasPreparedIntent, client.PreparedCard, client.PreparedMode, client.PreparedTarget), Is.EqualTo((true, (string?)next, (string?)"now", (string?)target)),
                "exactly the confirmed request waits");
            Assert.That(client.PreparedPicks, Is.Empty);
            Assert.That(client.HasActionPresentation, Is.True);
            Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)), "preparing leaves AP, cards, RNG, log, events, slots and receipts unchanged");
            var live = client.Snapshot!.Battle!;
            Assert.That((live.Ap, live.V, live.Reservations.Count), Is.EqualTo((b.Ap, b.V, b.Reservations.Count)));
            AssertSame(s, "the mirror received nothing");
            yield return null; Canvas.ForceUpdateCanvases();
            var view = ActiveView(s.Host);
            AssertReadable(view, t => t == WaitLabel("즉시 사용"), "the waiting label, apart from the timeline's time reservations");
            var cancel = ButtonNamed(s.Host, "CancelPreparedIntent");
            Assert.That(cancel.interactable, Is.True, "cancel is available during the display");
            var names = view.GetComponentsInChildren<Button>().Select(x => x.name).ToList();
            Assert.That(names.Contains("ConfirmCard") || names.Any(n => n.StartsWith("Mode:", StringComparison.Ordinal)), Is.False, "the waiting intent replaces the confirm and the modes");
            Assert.That(DrawnCells(s.Host), Is.Empty, "RULES_NOW_PLAY: no time-reservation timeline is drawn");
            Assert.That(ButtonNamed(s.Host, "EndTurn").interactable, Is.False);

            // A second confirm, a changed selection and every command are refused before the Run.
            Assert.That(client.ConfirmCard(), Is.False, "a second confirm is refused");
            Assert.That((client.LastError, client.PreparedCard), Is.EqualTo(((string?)Pending, (string?)next)), "the full slot is not overwritten");
            string? other = s.Mirror.State.Battle!.Zones.Hand.FirstOrDefault(u => u != next);
            Assert.That(other, Is.Not.Null, "a second hand card");
            foreach (var (label, call) in new (string, Action)[] { ("SelectCard", () => client.SelectCard(other!)), ("SelectMode", () => client.SelectMode("next")), ("SelectEnemy", () => client.SelectEnemy("e-none")) })
            {
                call();
                Assert.That(client.LastError, Is.EqualTo(Pending), label + ": changing the waiting request takes a cancel first");
                Assert.That(new[] { client.SelectedCard, client.SelectedMode, client.Target }, Is.EqualTo(new[] { next, "now", target }), label);
            }
            var commands = new (string Name, Func<bool> Call)[]
            {
                ("PlayCard", () => client.PlayCard(other!, "now")), ("EndTurn", () => client.EndTurn()), ("UseUltimate", () => client.UseUltimate(b.Actors[0].Id)),
                ("Depart", () => client.Depart()), ("Do", () => Do(client, () => Assert.Fail("the private Do boundary ran a command")))
            };
            foreach (var (label, call) in commands)
            {
                Assert.That(call(), Is.False, label + " must be refused");
                Assert.That(client.LastError, Is.EqualTo(Showing), label + ": the display's code comes first while it is open");
                Assert.That((client.HasPreparedIntent, Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((true, committed, store)), label);
            }

            // The natural end closes the display in place and sends nothing; the intent waits for the next Update.
            float deadline = Time.realtimeSinceStartup + 5f;
            while (client.HasActionPresentation && Time.realtimeSinceStartup < deadline)
            {
                Assert.That((client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((true, committed)), "nothing is sent while the display runs");
                yield return null;
            }
            Assert.That(client.HasActionPresentation, Is.False, "the display ends on its real timer");
            Assert.That((client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((true, committed)), "closing the display is not the dispatch");
            Assert.That(OwnedViews(s.Host), Is.EqualTo(new[] { view }), "the display closed in place");
            Assert.That(ButtonNamed(s.Host, "EndTurn").interactable, Is.False, "the end waits for the intent's dispatch");
            foreach (var (label, call) in new (string, Func<bool>)[] { ("EndTurn", () => client.EndTurn()), ("PlayCard", () => client.PlayCard(other!, "now")),
                ("Do", () => Do(client, () => Assert.Fail("the private Do boundary ran a command before the dispatch"))) })
            {
                Assert.That(call(), Is.False, label + " before the dispatch");
                Assert.That(client.LastError, Is.EqualTo(Pending), label + ": the waiting intent goes first");
            }
            Assert.That(client.SkipActionPresentation(), Is.False, "no display is left to skip");
            AssertSame(s, "nothing was sent before the dispatch");

            yield return null; // the next Update is the dispatch
            MirrorOf(s, BattleCore(s, "play", Play(next, "now", target)));
            Assert.That((client.HasPreparedIntent, client.LastError), Is.EqualTo((false, (string?)null)), "the intent was sent once");
            Assert.That((client.HasActionPresentation, client.ActionPresentationTerminal), Is.EqualTo((true, s.Mirror.State.Phase != "BATTLE")), "the sent play opens its own display");
            AssertRowsMatch(client.ActionPresentationRows, s.Last!.Events);
            Assert.That(client.ActionPresentationRows[0].Text, Does.StartWith("카드 사용 · " + name), "its first row is the frozen card's play");
            string sent = Fingerprint(client.Snapshot!);
            Assert.That(client.SkipActionPresentation(), Is.True);
            Assert.That(client.SkipActionPresentation(), Is.False, "a repeated skip is inert");
            for (int i = 0; i < 5; i++)
            {
                yield return null;
                Assert.That((client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((false, sent)), "later Updates send nothing more");
            }
            AssertSame(s, "exactly the two actual plays");
        }

        // The same first play and the same confirmed second request under each pace (normal, 2x, reduced motion, an explicit skip, and a
        // reduced/2x switch mid-display), then the same actual end: identical Run, logs, events, RNG, store and both displays' rows. A skip
        // only closes the display; the intent is sent by the next Update, never by the skip itself.
        [UnityTest]
        public IEnumerator EveryPaceAndSkipSendTheSameFrozenRequestAndCommitTheSameTwoCommands()
        {
            float scale = Time.timeScale;
            var results = new List<(string Variant, string Run, string Store, string Log, string Rows)>();
            foreach (string variant in new[] { "normal", "double", "reduced", "skip", "switch" })
            {
                var s = new Session(); yield return Open(s);
                var client = s.Client;
                Pacing pace = variant == "double" ? Pacing.Double : variant == "reduced" ? Pacing.Reduced : Pacing.Normal;
                if (pace != Pacing.Normal) Assert.That(client.SetActionPace(pace), Is.True, variant);
                var found = new Found(); yield return PlayThenFind(s, found, NextPlain, "an immediate card without picks after a plain play");
                string next = found.Card!, target = found.Enemy!;
                var first = client.ActionPresentationRows;
                string committed = Fingerprint(client.Snapshot!);
                Prepare(s, next, "now", target);
                switch (variant)
                {
                    case "reduced":
                        Assert.That(client.ActionPresentationShown, Is.EqualTo(first.Count), "reduced motion shows every row at once");
                        break;
                    case "skip":
                        Assert.That(client.SkipActionPresentation(), Is.True);
                        Assert.That((client.HasActionPresentation, client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((false, true, committed)),
                            "skip is display only: it never calls Core itself");
                        Assert.That(client.SkipActionPresentation(), Is.False, "a repeated skip is inert");
                        break;
                    case "switch":
                        Assert.That(client.SetActionPace(Pacing.Reduced), Is.True);
                        Assert.That(client.SetActionPace(Pacing.Double), Is.True);
                        Assert.That(client.HasPreparedIntent, Is.True, "a pace change keeps the intent waiting");
                        break;
                }
                float deadline = Time.realtimeSinceStartup + 6f;
                while (client.HasPreparedIntent && Time.realtimeSinceStartup < deadline)
                {
                    Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed), variant + ": nothing is sent while the intent waits");
                    yield return null;
                }
                Assert.That(client.HasPreparedIntent, Is.False, variant + ": the intent is dispatched");
                MirrorOf(s, BattleCore(s, "play", Play(next, "now", target)));
                Assert.That(client.HasActionPresentation, Is.True, variant + ": the sent play opens its own display");
                var second = client.ActionPresentationRows;
                AssertRowsMatch(second, s.Last!.Events);
                FinishAction(client);
                for (int i = 0; i < 3; i++) yield return null;
                AssertSame(s, variant + ": no further command after the dispatch");
                Assert.That(Time.timeScale, Is.EqualTo(scale), variant + ": the display never touches Time.timeScale");
                if (s.Mirror.State.Phase == "BATTLE") { Assert.That(Both(s, client.EndTurn, BattleCore(s, "end", null)), Is.True, variant); Dismiss(client); }
                results.Add((variant, Fingerprint(client.Snapshot!), StorePrint(client), string.Join("\n", client.BattleLog),
                    string.Join("\n", first.Concat(second).Select(r => r.Kind + "|" + r.Text + "|" + r.Provenance))));
                yield return null;
            }
            foreach (var result in results.Skip(1))
                Assert.That((result.Run, result.Store, result.Log, result.Rows), Is.EqualTo((results[0].Run, results[0].Store, results[0].Log, results[0].Rows)),
                    result.Variant + " must commit exactly what normal pacing commits");
        }

        // One slot: a second confirm and a changed selection are refused without overwriting; the replaced view's old confirm reaches
        // nothing; cancel drops the intent unsent (nothing spent, nothing refunded) and keeps the selection; its old button and a repeated
        // cancel reach nothing; the freed slot takes the same request again; after a cancel the display's end sends nothing, and the kept
        // selection is then the ordinary actual play once.
        [UnityTest]
        public IEnumerator AFullSlotRefusesAnOverwriteAndCancelOrOldButtonsSendNothing()
        {
            var s = new Session(); yield return Open(s);
            var found = new Found(); yield return PlayThenFind(s, found, NextPlain, "an immediate card without picks after a plain play");
            var client = s.Client; string next = found.Card!, target = found.Enemy!;
            string committed = Fingerprint(client.Snapshot!), store = StorePrint(client);
            // No frame passes until the repeat below, so the first play's display is certainly still open.
            client.SelectEnemy(target); client.SelectCard(next); client.SelectMode("now");
            var confirm = ButtonNamed(s.Host, "ConfirmCard");
            confirm.onClick.Invoke();
            Assert.That((client.HasPreparedIntent, client.PreparedCard, client.LastError), Is.EqualTo((true, (string?)next, (string?)null)));
            confirm.onClick.Invoke();
            Assert.That((client.HasPreparedIntent, client.PreparedCard, client.LastError), Is.EqualTo((true, (string?)next, (string?)null)),
                "the replaced view's confirm is inactive and reaches nothing");
            Assert.That(client.ConfirmCard(), Is.False);
            Assert.That((client.LastError, client.PreparedCard), Is.EqualTo(((string?)Pending, (string?)next)), "a second confirm never overwrites the waiting one");
            string? other = s.Mirror.State.Battle!.Zones.Hand.FirstOrDefault(u => u != next);
            Assert.That(other, Is.Not.Null, "a second hand card");
            client.SelectCard(other!);
            Assert.That((client.LastError, client.SelectedCard, client.PreparedCard), Is.EqualTo(((string?)Pending, (string?)next, (string?)next)), "a changed selection takes a cancel first");
            Assert.That(client.PickCard(other!) || client.AutoPick(), Is.False, "no sheet lies behind a plain intent");
            Assert.That(client.HasPreparedIntent, Is.True);
            Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)));

            var cancel = ButtonNamed(s.Host, "CancelPreparedIntent");
            cancel.onClick.Invoke();
            Assert.That((client.HasPreparedIntent, client.LastError), Is.EqualTo((false, (string?)null)), "cancel drops it");
            Assert.That(client.Notice, Does.Contain("취소"));
            Assert.That(new[] { client.SelectedCard, client.SelectedMode, client.Target }, Is.EqualTo(new[] { next, "now", target }), "the selection stays as the ordinary one");
            Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)), "nothing was spent, so nothing is refunded");
            cancel.onClick.Invoke();
            Assert.That(client.CancelPreparedIntent(), Is.False, "nothing is left to cancel");
            Assert.That(ActiveView(s.Host).GetComponentsInChildren<Button>().Any(x => x.name == "CancelPreparedIntent"), Is.False);
            Assert.That(ButtonNamed(s.Host, "ConfirmCard").interactable, Is.True, "the confirm is back");
            Assert.That(client.HasActionPresentation, Is.True, "still within the first play's display");
            Assert.That(client.ConfirmCard(), Is.False);
            Assert.That((client.HasPreparedIntent, client.PreparedCard, client.LastError), Is.EqualTo((true, (string?)next, (string?)null)), "the freed slot takes the request again");
            Assert.That(client.CancelPreparedIntent(), Is.True);
            AssertSame(s, "preparing and cancelling sent nothing");

            Assert.That(client.SkipActionPresentation(), Is.True);
            for (int i = 0; i < 5; i++)
            {
                yield return null;
                Assert.That((client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((false, committed)), "a cancelled intent is never sent");
            }
            AssertSame(s, "cancel sent nothing");
            Assert.That(Both(s, client.ConfirmCard, BattleCore(s, "play", Play(next, "now", target))), Is.True, "after the display the kept selection plays at once");
            Assert.That(client.HasPreparedIntent, Is.False);
        }

        // No spent card and no non-living target stays selected, and a request Core refuses now (an unknown mode, or RULES_NOW_PLAY's
        // refused reservation) is refused with Core's own precheck code without becoming an intent. A plain request then waits and is
        // sent once by the next Update. (It replaced NoSpentCardOrRefusedRequestBecomesAnIntentAndANextRequestTakesNoSlotUntilSent.)
        [UnityTest]
        public IEnumerator NoSpentCardOrRefusedRequestBecomesAnIntentAndAPlainRequestIsSentOnce()
        {
            var s = new Session(); yield return Open(s);
            var found = new Found(); yield return PlayThenFind(s, found, NextPlain, "a second plain card after a plain play");
            var client = s.Client; string spent = found.First!, card = found.Card!, target = found.Enemy!;
            string committed = Fingerprint(client.Snapshot!), store = StorePrint(client);

            Assert.That(ActiveView(s.Host).GetComponentsInChildren<Button>().Any(x => x.name == "Hand:" + spent), Is.False, "the spent card is not drawn");
            client.SelectCard(spent);
            Assert.That(client.SelectedCard, Is.Not.EqualTo(spent), "a spent card never stays selected");
            client.SelectEnemy("e-not-living");
            Assert.That(client.Target == null || s.Mirror.State.Battle!.Enemies.Any(e => e.Uid == client.Target && e.Alive), Is.True, "a non-living target never stays selected");
            foreach (string mode in new[] { "test-unknown-mode", "next", "after" })
            {
                client.SelectEnemy(target); client.SelectCard(card); client.SelectMode(mode);
                string? refused = s.Mirror.BattleCheck(card, mode, target);
                Assert.That(refused, Is.Not.Null, "Core's precheck refuses " + mode);
                if (mode != "test-unknown-mode") Assert.That(refused, Is.EqualTo("NOT_RESERVABLE"), mode);
                Assert.That(client.ConfirmCard(), Is.False);
                Assert.That((client.LastError, client.HasPreparedIntent), Is.EqualTo((refused, false)), mode + ": the refusal is Core's own code and nothing waits");
                Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)), mode);
            }

            Prepare(s, card, "now", target);
            Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)));
            if (client.HasActionPresentation) Assert.That(client.SkipActionPresentation(), Is.True);
            Assert.That((client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((true, committed)), "only the next Update sends it");
            yield return null; // the dispatch
            MirrorOf(s, BattleCore(s, "play", Play(card, "now", target)));
            Assert.That(client.HasPreparedIntent, Is.False);
            Assert.That(s.Mirror.State.Battle == null || s.Mirror.State.Battle.Reservations.Count == 0, Is.True, "nothing was reserved");
            Assert.That(client.ActionPresentationRows.Any(r => r.Kind == BattleDigestKind.CardPlayed), Is.True, "its own display is a card result");
        }

        // A choice card confirmed during the display first opens its pure sheet (which keeps its own lock); its explicit final pick or auto
        // freezes the request with a copy of the picks and sends nothing; the next Update after the display sends exactly that request. If
        // the display ends while the player is still choosing, nothing happens on its own; the explicit final pick is then the ordinary play.
        [UnityTest]
        public IEnumerator AChoiceSheetDuringTheDisplayFreezesItsFinalPickOrExplicitAutoAndNeverActsOnItsOwn()
        {
            foreach (string variant in new[] { "pick", "auto", "late" })
            {
                var s = new Session(); yield return Open(s);
                var found = new Found(); yield return PlayThenFind(s, found, Tune, "the reference kit's nia_tune playable after a plain play");
                var client = s.Client; string tune = found.Card!, target = found.Enemy!;
                string committed = Fingerprint(client.Snapshot!), store = StorePrint(client);
                client.SelectEnemy(target); client.SelectCard(tune); client.SelectMode("now");
                Assert.That(client.ConfirmCard(), Is.False, variant);
                Assert.That((client.PickerOpen, client.HasPreparedIntent, client.HasActionPresentation, client.LastError), Is.EqualTo((true, false, true, (string?)null)),
                    variant + ": the sheet collects the choice first; nothing waits yet");
                string? chosen = client.PickerOptions.FirstOrDefault(u => s.Mirror.BattleCheck(tune, "now", target, new[] { u }) == null);
                Assert.That(chosen, Is.Not.Null, "a drawn candidate Core's precheck accepts");
                Assert.That(client.EndTurn(), Is.False);
                Assert.That(client.LastError, Is.EqualTo(SheetOpen), "the sheet's lock comes before the display's");
                client.SelectCard(chosen!);
                Assert.That((client.LastError, client.SelectedCard), Is.EqualTo(((string?)SheetOpen, (string?)tune)));
                Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)), variant + ": the sheet spent nothing");
                AssertSame(s, variant + ": nothing was sent");
                switch (variant)
                {
                    case "pick":
                        Assert.That(client.PickCard(chosen!), Is.False, "the final pick during the display commits nothing");
                        Assert.That((client.PickerOpen, client.HasPreparedIntent, client.LastError), Is.EqualTo((false, true, (string?)null)));
                        Assert.That((client.PreparedCard, client.PreparedMode, client.PreparedTarget), Is.EqualTo(((string?)tune, (string?)"now", (string?)target)));
                        Assert.That(client.PreparedPicks, Is.EqualTo(new[] { chosen }), "the frozen picks are a copy of the sheet's");
                        Assert.That(client.PickerPicks, Is.Empty, "the sheet is closed");
                        Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)));
                        yield return null; Canvas.ForceUpdateCanvases();
                        AssertReadable(ActiveView(s.Host), t => t == WaitLabel("즉시 사용 · 고른 카드 " + s.Mirror.BattleEffectiveCard(chosen!).Name), "the waiting label names the frozen pick");
                        if (client.HasActionPresentation) Assert.That(client.SkipActionPresentation(), Is.True);
                        Assert.That(client.HasPreparedIntent, Is.True);
                        yield return null; // the dispatch
                        MirrorOf(s, BattleCore(s, "play", Play(tune, "now", target, new[] { chosen! })));
                        break;
                    case "auto":
                        Assert.That(client.AutoPick(), Is.False, "explicit auto during the display commits nothing");
                        Assert.That((client.PickerOpen, client.HasPreparedIntent, client.PreparedCard, client.LastError), Is.EqualTo((false, true, (string?)tune, (string?)null)));
                        Assert.That(client.PreparedPicks, Is.Empty, "explicit auto freezes no picks: Core keeps its own choice");
                        Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)));
                        yield return null; Canvas.ForceUpdateCanvases();
                        AssertReadable(ActiveView(s.Host), t => t == WaitLabel("즉시 사용 · 고를 카드는 자동 선택"), "the waiting label says Core chooses");
                        if (client.HasActionPresentation) Assert.That(client.SkipActionPresentation(), Is.True);
                        Assert.That(client.HasPreparedIntent, Is.True);
                        yield return null; // the dispatch
                        MirrorOf(s, BattleCore(s, "play", Play(tune, "now", target)));
                        break;
                    default:
                        {
                            float deadline = Time.realtimeSinceStartup + 5f;
                            while (client.HasActionPresentation && Time.realtimeSinceStartup < deadline)
                            {
                                Assert.That((client.PickerOpen, client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((true, false, committed)));
                                yield return null;
                            }
                            Assert.That(client.HasActionPresentation, Is.False, "the display ended while the player was still choosing");
                            for (int i = 0; i < 5; i++)
                            {
                                yield return null;
                                Assert.That((client.PickerOpen, client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((true, false, committed)),
                                    "no automatic action follows the display's end");
                            }
                            Assert.That(Both(s, () => client.PickCard(chosen!), BattleCore(s, "play", Play(tune, "now", target, new[] { chosen! }))), Is.True,
                                "the explicit final pick after the display is the ordinary immediate play");
                            break;
                        }
                }
                Assert.That((client.HasPreparedIntent, client.PickerOpen, client.LastError), Is.EqualTo((false, false, (string?)null)), variant);
                Assert.That(client.HasActionPresentation, Is.True, variant + ": the sent play opens its own display");
                AssertRowsMatch(client.ActionPresentationRows, s.Last!.Events);
                Assert.That(client.SkipActionPresentation(), Is.True);
                string sent = Fingerprint(client.Snapshot!);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(sent), variant + ": nothing more is sent");
                AssertSame(s, variant + ": exactly the two actual plays");
            }
        }

        // A committed change beneath a waiting intent (seam 1: one actual end on both Runs) is found at the dispatch: the intent is dropped
        // with the stale reason and no play, Core fallback, retarget or retry is sent, also for frozen picks (they are never resolved by Core's
        // automatic remaining-picks rule). No display opens for it and nothing is sent later.
        [UnityTest]
        public IEnumerator ACommittedChangeBeneathTheWaitingIntentDropsItWithoutAnyCommand()
        {
            foreach (string variant in new[] { "plain", "choice" })
            {
                var s = new Session(); yield return Open(s);
                var found = new Found();
                yield return PlayThenFind(s, found, variant == "plain" ? (Func<Session, string, string?>)NextPlain : Tune, variant == "plain" ? "an immediate card after a plain play" : "nia_tune after a plain play");
                var client = s.Client; string card = found.Card!, target = found.Enemy!;
                if (variant == "plain") Prepare(s, card, "now", target);
                else
                {
                    client.SelectEnemy(target); client.SelectCard(card); client.SelectMode("now");
                    Assert.That(client.ConfirmCard(), Is.False);
                    string? chosen = client.PickerOptions.FirstOrDefault(u => s.Mirror.BattleCheck(card, "now", target, new[] { u }) == null);
                    Assert.That(chosen, Is.Not.Null, "a drawn candidate Core's precheck accepts");
                    Assert.That(client.PickCard(chosen!), Is.False);
                    Assert.That(client.HasPreparedIntent, Is.True);
                    Assert.That(client.PreparedPicks, Is.EqualTo(new[] { chosen }), "the frozen pick");
                }
                int version = client.Snapshot!.Battle!.V;
                Beneath(s, "end");
                Assert.That(s.Mirror.State.Battle?.V, Is.Not.EqualTo(version), "the change beneath moved the battle on");
                string changed = Fingerprint(client.Snapshot!);
                Assert.That(client.HasPreparedIntent, Is.True, variant + ": nothing is checked or sent before the dispatch");
                if (client.HasActionPresentation) Assert.That(client.SkipActionPresentation(), Is.True);
                Assert.That((client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((true, changed)), "skip sent nothing");
                yield return null; // the dispatch finds the confirmed state gone
                Assert.That((client.HasPreparedIntent, client.LastError), Is.EqualTo((false, (string?)Stale)), variant + ": the stale intent is dropped");
                Assert.That(client.Notice, Does.Contain("보내지 않고 취소했어요"));
                Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(changed), variant + ": no play, fallback, retarget or retry was sent");
                Assert.That(client.HasActionPresentation, Is.False, "no display opens for an unsent request");
                AssertSame(s, variant + ": only the change beneath happened");
                for (int i = 0; i < 5; i++)
                {
                    yield return null;
                    Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(changed), variant + ": nothing is sent later");
                }
                Assert.That(client.CancelPreparedIntent(), Is.False);
                var now = s.Mirror.State;
                if (now.Phase == "BATTLE" && now.Battle!.Zones.Hand.Count != 0)
                {
                    string uid = now.Battle.Zones.Hand[0];
                    client.SelectCard(uid);
                    Assert.That((client.LastError, client.SelectedCard), Is.EqualTo(((string?)null, (string?)uid)), variant + ": the ordinary selection resumes");
                }
                AssertSame(s, variant);
            }
        }

        // Focus loss, pause, disable, and focus loss in the frame between a skip and the dispatch each drop the waiting intent unsent at
        // once; nothing is sent while away (longer than the display would have run); resume/enable redraws the actual Run and explains the
        // drop; no command is replayed, nothing is restarted, and the next actual command is accepted.
        [UnityTest]
        public IEnumerator LeavingTheAppOrDisablingDropsTheWaitingIntentWithoutUnseenExecution()
        {
            foreach (string way in new[] { "focus", "pause", "disable", "gap" })
            {
                var s = new Session(); yield return Open(s);
                var found = new Found(); yield return PlayThenFind(s, found, NextPlain, "an immediate card without picks after a plain play");
                var client = s.Client; string next = found.Card!, target = found.Enemy!;
                var rows = client.ActionPresentationRows;
                Prepare(s, next, "now", target);
                string committed = Fingerprint(client.Snapshot!), store = StorePrint(client);
                string? runId = client.Snapshot!.RunId; var log = client.BattleLog;
                var view = ActiveView(s.Host);
                if (way == "gap")
                {
                    Assert.That(client.SkipActionPresentation(), Is.True);
                    Assert.That((client.HasActionPresentation, client.HasPreparedIntent), Is.EqualTo((false, true)), "the intent waits for the next Update");
                }
                if (way == "focus" || way == "gap") s.Host.SendMessage("OnApplicationFocus", false);
                else if (way == "pause") s.Host.SendMessage("OnApplicationPause", true);
                else s.Host.SetActive(false);
                Assert.That((client.HasPreparedIntent, client.HasActionPresentation, client.LastError), Is.EqualTo((false, false, (string?)Dropped)), way + " drops the waiting intent unsent");
                Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)), way);
                float until = Time.realtimeSinceStartup + Duration(rows, 1f) + 0.2f;
                while (Time.realtimeSinceStartup < until)
                {
                    Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed), way + ": nothing is sent while away");
                    yield return null;
                }
                if (way == "focus" || way == "gap") s.Host.SendMessage("OnApplicationFocus", true);
                else if (way == "pause") s.Host.SendMessage("OnApplicationPause", false);
                else s.Host.SetActive(true);
                yield return null; Canvas.ForceUpdateCanvases();
                var redrawn = SingleView(s.Host);
                Assert.That(redrawn, Is.Not.SameAs(view), way + ": resuming redraws the actual view");
                Assert.That((client.HasPreparedIntent, client.HasActionPresentation, client.LastError), Is.EqualTo((false, false, (string?)Dropped)), way + ": nothing comes back");
                Assert.That(redrawn.GetComponentsInChildren<Button>().Any(x => x.name == "CancelPreparedIntent"), Is.False);
                Assert.That(client.Notice, Does.Contain("취소"));
                AssertReadable(redrawn, t => t == client.Notice, way + ": the drop is explained on the action line");
                var b = s.Mirror.State.Battle!;
                Assert.That(redrawn.GetComponentsInChildren<Button>().Where(x => x.name.StartsWith("Hand:", StringComparison.Ordinal)).Select(x => x.name.Substring(5)), Is.EqualTo(b.Zones.Hand));
                Assert.That((Fingerprint(client.Snapshot!), StorePrint(client), client.Snapshot!.RunId), Is.EqualTo((committed, store, runId)), way + ": no command, replay or restart");
                Assert.That(client.BattleLog, Is.EqualTo(log), way + ": no event row is logged twice");
                AssertSame(s, way);
                Assert.That(Both(s, client.EndTurn, BattleCore(s, "end", null)), Is.True, way + ": the next actual command is accepted");
                Dismiss(client);
            }
        }

        // A press held on the cancel while the display finishes: completion rewrites in place (same view, button and EventSystem) and the
        // intent still waits for the next Update, so the release in that frame is exactly one cancel and nothing is ever dispatched. (A
        // release after the dispatch would land on the replaced view's inactive button and reach nothing.)
        [UnityTest]
        public IEnumerator AHeldCancelSurvivesTheDisplayCompletionAndItsReleaseCancelsBeforeTheDispatch()
        {
            var s = new Session(); yield return Open(s);
            var found = new Found(); yield return PlayThenFind(s, found, NextPlain, "an immediate card without picks after a plain play");
            var client = s.Client; string next = found.Card!, target = found.Enemy!;
            Prepare(s, next, "now", target);
            string committed = Fingerprint(client.Snapshot!);
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That((client.HasActionPresentation, client.HasPreparedIntent), Is.EqualTo((true, true)), "the press must start during the display");
            var view = ActiveView(s.Host); var events = EventSystem.current;
            Assert.That(events, Is.Not.Null);
            var cancel = ButtonNamed(s.Host, "CancelPreparedIntent");
            var pointer = Press(events, cancel);

            float deadline = Time.realtimeSinceStartup + 5f;
            while (client.HasActionPresentation && Time.realtimeSinceStartup < deadline)
            {
                Assert.That((client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((true, committed)));
                yield return null;
            }
            Assert.That(client.HasActionPresentation, Is.False, "the display finished during the held press");
            Assert.That(client.HasPreparedIntent, Is.True, "the frame that closed the display does not dispatch");
            Assert.That(OwnedViews(s.Host), Is.EqualTo(new[] { view }), "completion must not rebuild the view under a held cancel");
            Assert.That(cancel != null && cancel.IsActive() && cancel.interactable, Is.True, "the held cancel is the same live button");
            Assert.That(EventSystem.current, Is.SameAs(events), "the same EventSystem keeps the press");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed));
            Release(events, pointer);
            Assert.That((client.HasPreparedIntent, client.LastError), Is.EqualTo((false, (string?)null)), "the release is exactly one cancel");
            for (int i = 0; i < 5; i++)
            {
                yield return null;
                Assert.That((client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((false, committed)), "the cancelled intent is never dispatched");
            }
            AssertSame(s, "nothing was sent");
        }

        // P1-CARD-FLOW-CANCEL-TAP-FIX (§14.7 safe input): two real taps at the same screen point, the actual center of the cancel. The first
        // cancels and redraws synchronously; the second, at the same point of the redrawn view, may only open the read-only card text: it
        // never prepares or plays the cancelled request again. Nothing is sent afterwards, through the display's end and the next Update.
        [UnityTest]
        public IEnumerator DoubleTapCancelNeverPreparesOrPlaysAgain()
        {
            var s = new Session(); yield return Open(s);
            var found = new Found(); yield return PlayThenFind(s, found, NextPlain, "an immediate card without picks after a plain play");
            var client = s.Client; string next = found.Card!, target = found.Enemy!;
            Prepare(s, next, "now", target);
            string committed = Fingerprint(client.Snapshot!), store = StorePrint(client);
            yield return null; Canvas.ForceUpdateCanvases(); // a stable layout for the first tap
            Assert.That((client.HasActionPresentation, client.HasPreparedIntent), Is.EqualTo((true, true)), "the taps start during the display");
            var cancel = ButtonNamed(s.Host, "CancelPreparedIntent"); Vector2 point = Center(cancel);
            var first = TapAt(point);
            Assert.That(first, Is.SameAs(cancel), "the first tap's ray hit belongs to the cancel");
            Assert.That((client.HasPreparedIntent, client.LastError), Is.EqualTo((false, (string?)null)), "the first tap cancels");
            Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)), "cancel spent and refunded nothing");

            yield return null; Canvas.ForceUpdateCanvases(); // the redrawn view is laid out; the replaced one is gone
            var second = TapAt(point);
            Assert.That((client.HasPreparedIntent, client.PickerOpen), Is.EqualTo((false, false)), "the repeated tap at the cancel's point prepares nothing again");
            Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)), "the repeated tap plays nothing: AP, V, RNG, log, events and store stay");
            AssertSame(s, "the repeated tap sent no command");
            Assert.That((second.name, client.CardInfoOpen), Is.EqualTo(("CardInfoButton", true)),
                "after a cancel the same point is the ordinary read-only card text (TapAt asserted it live before the click)");

            client.CloseCardInfo();
            Assert.That(client.CardInfoOpen, Is.False);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (client.HasActionPresentation && Time.realtimeSinceStartup < deadline)
            {
                Assert.That((client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((false, committed)));
                yield return null;
            }
            Assert.That(client.HasActionPresentation, Is.False, "the display ends on its real timer");
            for (int i = 0; i < 3; i++)
            {
                yield return null;
                Assert.That((client.HasPreparedIntent, Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((false, committed, store)),
                    "nothing is dispatched after the display and the next Updates");
            }
            AssertSame(s, "the cancelled request was never sent");
        }

        // P1-CARD-FLOW-CANCEL-TAP-FIX (§14.7 safe input): two real taps at the same screen point, the actual center of the confirm, during the
        // display. The first prepares the one unsent intent and redraws synchronously; the second, at the same point of the redrawn view, may
        // only open the read-only card text: it never cancels or duplicates the intent. The display's end then sends exactly one command.
        [UnityTest]
        public IEnumerator DoubleTapConfirmKeepsOneUnsentIntent()
        {
            var s = new Session(); yield return Open(s);
            var found = new Found(); yield return PlayThenFind(s, found, NextPlain, "an immediate card without picks after a plain play");
            var client = s.Client; string next = found.Card!, target = found.Enemy!;
            client.SelectEnemy(target); client.SelectCard(next); client.SelectMode("now");
            string committed = Fingerprint(client.Snapshot!), store = StorePrint(client);
            yield return null; Canvas.ForceUpdateCanvases(); // a stable layout for the first tap
            Assert.That((client.HasActionPresentation, client.HasPreparedIntent), Is.EqualTo((true, false)), "the taps start during the display");
            var confirm = ButtonNamed(s.Host, "ConfirmCard"); Vector2 point = Center(confirm);
            Assert.That(confirm.interactable, Is.True);
            var first = TapAt(point);
            Assert.That(first, Is.SameAs(confirm), "the first tap's ray hit belongs to the confirm");
            Assert.That((client.HasPreparedIntent, client.PreparedCard, client.PreparedMode, client.PreparedTarget, client.LastError),
                Is.EqualTo((true, (string?)next, (string?)"now", (string?)target, (string?)null)), "the first tap prepares exactly the selected request");
            Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)));

            yield return null; Canvas.ForceUpdateCanvases(); // the redrawn view is laid out; the replaced one is gone
            Assert.That(client.HasActionPresentation, Is.True, "the display is still open, so no dispatch can come between the taps");
            var second = TapAt(point);
            Assert.That((client.HasPreparedIntent, client.PreparedCard, client.PreparedMode, client.PreparedTarget, client.PickerOpen),
                Is.EqualTo((true, (string?)next, (string?)"now", (string?)target, false)), "the repeated tap at the confirm's point keeps the one intent");
            Assert.That(client.PreparedPicks, Is.Empty);
            Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)), "nothing committed: AP, V, RNG, log, events and store stay");
            AssertSame(s, "the repeated tap sent no command");
            Assert.That((second.name, client.CardInfoOpen), Is.EqualTo(("CardInfoButton", true)),
                "after a confirm the same point is the waiting intent's read-only card text (TapAt asserted it live before the click)");

            client.CloseCardInfo();
            Assert.That((client.CardInfoOpen, client.HasPreparedIntent), Is.EqualTo((false, true)), "closing the card text keeps the intent");
            float deadline = Time.realtimeSinceStartup + 6f;
            while (client.HasPreparedIntent && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed), "nothing is sent while the intent waits");
                yield return null;
            }
            Assert.That(client.HasPreparedIntent, Is.False, "the display's natural end and the next Update send the intent");
            MirrorOf(s, BattleCore(s, "play", Play(next, "now", target)));
            Assert.That(client.HasActionPresentation, Is.True, "the sent play opens its own display");
            AssertRowsMatch(client.ActionPresentationRows, s.Last!.Events);
            string sent = Fingerprint(client.Snapshot!);
            Assert.That(client.SkipActionPresentation(), Is.True);
            for (int i = 0; i < 5; i++)
            {
                yield return null;
                Assert.That((client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((false, sent)), "no duplicate command afterwards");
            }
            AssertSame(s, "exactly the two actual plays");
        }

        // A waiting finishing card is sent once at its dispatch and ends the battle: no intent survives, its terminal display covers the next
        // screen, and under it nothing can be prepared, picked, cancelled or committed; the next screen's own command works afterwards.
        [UnityTest]
        public IEnumerator ADispatchedPlayThatEndsTheBattleLeavesNoIntentAndItsSheetRefusesPreparation()
        {
            var s = new Session(); yield return Open(s);
            var found = new Found(); yield return PlayThenFind(s, found, Finisher, "a plain card whose exact forecast defeats the last living enemy after a plain play");
            var client = s.Client; string finisher = found.Card!, target = found.Enemy!;
            Prepare(s, finisher, "now", target);
            Assert.That(client.SkipActionPresentation(), Is.True);
            Assert.That(client.HasPreparedIntent, Is.True);
            yield return null; // the dispatch
            MirrorOf(s, BattleCore(s, "play", Play(finisher, "now", target)));
            var after = client.Snapshot!;
            Assert.That(after.Phase, Is.Not.EqualTo("BATTLE"), "the sent finishing card ended the battle");
            Assert.That(after.Battle, Is.Null);
            Assert.That((client.HasPreparedIntent, client.HasActionPresentation, client.ActionPresentationTerminal), Is.EqualTo((false, true, true)),
                "no intent survives the battle's end; its last display covers the next screen");
            string committed = Fingerprint(after), store = StorePrint(client), pending = CanonicalState.Hash(after.Pending);
            var calls = new (string Name, Func<bool> Call)[]
            {
                ("ConfirmCard", () => client.ConfirmCard()), ("PickCard", () => client.PickCard(finisher)), ("AutoPick", () => client.AutoPick()),
                ("CancelPreparedIntent", () => client.CancelPreparedIntent()), ("Proceed", () => client.Proceed()), ("CloseWithoutSave", () => client.CloseWithoutSave()),
                ("EndTurn", () => client.EndTurn())
            };
            foreach (var (label, call) in calls)
            {
                Assert.That(call(), Is.False, label + " under the battle's last display");
                Assert.That(client.HasPreparedIntent, Is.False, label + ": nothing can wait under the battle's last display");
                Assert.That((Fingerprint(client.Snapshot!), StorePrint(client), CanonicalState.Hash(client.Snapshot!.Pending)), Is.EqualTo((committed, store, pending)), label);
            }
            client.ConfirmCard();
            Assert.That(client.LastError, Is.EqualTo(Showing), "the terminal display refuses the confirm itself");
            client.SelectCard(finisher);
            Assert.That(client.LastError, Is.EqualTo(Showing), "and every next selection");
            Assert.That(client.SkipActionPresentation(), Is.True);
            for (int i = 0; i < 3; i++)
            {
                yield return null;
                Assert.That((client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((false, committed)), "nothing is sent after the battle's end");
            }
            AssertSame(s, "exactly the two actual plays");
            if (after.Phase == "REWARD") Assert.That(Both(s, client.Proceed, r => r.Proceed()), Is.True, "the reward screen accepts its own command now");
            else Assert.That(client.CloseWithoutSave(), Is.True, "the save screen accepts its own command now");
        }

        // The turn summary and the recovery quarantine come before preparation: the confirm, the sheet's pick/auto and the cancel are refused
        // with their codes, nothing waits, and nothing reaches the Run or the store.
        [UnityTest]
        public IEnumerator RecoveryAndTheTurnSummaryOutrankPreparation()
        {
            var s = new Session(); yield return Open(s);
            var client = s.Client;
            string firstNode = s.Mirror.Available()[0];
            Assert.That(Both(s, () => client.EnterNode(firstNode), r => r.Enter(firstNode)), Is.True);
            Assert.That(Both(s, client.EndTurn, BattleCore(s, "end", null)), Is.True);
            Assert.That(client.HasTurnSummary, Is.True);
            string card = s.Mirror.State.Battle!.Zones.Hand[0];
            string committed = Fingerprint(client.Snapshot!), store = StorePrint(client);
            var calls = new (string Name, Func<bool> Call)[]
            {
                ("ConfirmCard", () => client.ConfirmCard()), ("PickCard", () => client.PickCard(card)), ("AutoPick", () => client.AutoPick()),
                ("CancelPreparedIntent", () => client.CancelPreparedIntent())
            };
            foreach (var (label, call) in calls)
            {
                Assert.That(call(), Is.False, label + " under the summary");
                Assert.That((client.LastError, client.HasPreparedIntent), Is.EqualTo(((string?)Barrier, false)), label + ": the summary's barrier comes first");
                Assert.That((Fingerprint(client.Snapshot!), StorePrint(client)), Is.EqualTo((committed, store)), label);
            }
            client.SelectCard(card);
            Assert.That(client.LastError, Is.EqualTo(Barrier));
            Dismiss(client);

            var live = s.Mirror.State.Battle!;
            // Test-local fault in the client's own content: every move of the enemies in this battle gets an unknown type.
            var moves = ClientData(client).Memory.Battle.Enemies.Where(e => live.Enemies.Any(x => x.Id == e.Id)).SelectMany(e => e.Pattern).Select(m => (Move: m, Type: m.Type)).ToList();
            Assert.That(moves, Is.Not.Empty);
            try
            {
                foreach (var entry in moves) entry.Move.Type = "test_unknown_move";
                LogAssert.Expect(LogType.Error, new Regex(Diagnostic + @"BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED\)"));
                Assert.That(client.EndTurn(), Is.False);
                Assert.That((client.RecoveryRequired, client.HasPreparedIntent), Is.EqualTo((true, false)), "a failed end quarantines");
                Assert.That(StorePrint(client), Is.EqualTo(store), "the session store is kept");
                foreach (var (label, call) in calls)
                {
                    Assert.That(call(), Is.False, label + " in recovery");
                    Assert.That((client.LastError, client.HasPreparedIntent), Is.EqualTo(((string?)Recovery, false)), label + ": recovery comes first");
                }
                client.SelectCard(card);
                Assert.That(client.LastError, Is.EqualTo(Recovery));
                Assert.That(StorePrint(client), Is.EqualTo(store));
            }
            finally
            {
                foreach (var entry in moves) entry.Move.Type = entry.Type; // fault removed
            }
            Assert.That(client.DiscardFailedPractice(), Is.True, client.LastError);
            Assert.That((client.RecoveryRequired, client.HasPreparedIntent, client.HasActionPresentation), Is.EqualTo((false, false, false)));
            Assert.That(StorePrint(client), Is.EqualTo(store), "nothing of the failed Run was saved");
        }

        // ───────── expected text and rows (independent copies of the documented strings and the digest's kinds) ─────────
        static string WaitLabel(string request) => "미전송 사용 대기 1/1 · " + request + "\n결과 표시가 끝나면 이 요청 하나만 보내요 · 바꾸려면 먼저 대기 취소";
        static List<string> DrawnCells(GameObject host) =>
            ActiveView(host).GetComponentsInChildren<Image>().Where(i => i.name == "ReservationSlot").Select(i => i.GetComponentInChildren<Text>().text).ToList();
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
                case "kill": return BattleDigestKind.Kill;
                case "break": return BattleDigestKind.Break;
                case "block": return BattleDigestKind.PartyBlock;
                case "end": return BattleDigestKind.BattleEnd;
                default: return null;
            }
        }
        // The documented local timing of a whole display: every row after the first waits its step, then the hold; 2x halves both.
        static float Duration(IReadOnlyList<BattleDigestEntry> rows, float scale) =>
            (rows.Skip(1).Sum(r => r.IsReservedResolution ? ReservedSeconds : RowSeconds) + HoldSeconds) * scale;

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
        // The Core side of a command the view already sent (a real tap, or the intent's dispatch on an Update).
        static void MirrorOf(Session s, Action<CoreRun> core)
        {
            core(s.Mirror);
            AssertSame(s, "the view must have sent exactly the actual Core command");
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
            Assert.That((client.HasActionPresentation, client.HasPreparedIntent, Fingerprint(client.Snapshot!)), Is.EqualTo((false, false, before)), "finishing the display never reaches the Run");
        }
        // The existing bounded driver order (as in the action-presentation tests). Returns true when the command sent was a battle's last
        // action, whose display is left open for the caller.
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
        // Seam 1: one real public Core command on the client's own live Run (read by reflection) and the same command on the mirror. It
        // stands for any committed change the waiting intent did not see; the client's public gate refuses every such command.
        static void Beneath(Session s, string kind)
        {
            var live = (CoreRun)typeof(MemoryForgeExpeditionClient).GetField("run", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s.Client)!;
            live.BattleCommand(kind); s.Mirror.BattleCommand(kind);
            AssertSame(s, "the change beneath is one actual Core command on both Runs");
        }
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
        static BattleArgument Play(string uid, string mode, string? target) => Play(uid, mode, target, new string[0]);
        static BattleArgument Play(string uid, string mode, string? target, IEnumerable<string> picks) => BattleArgument.Object(new[] { Pair("uid", uid), Pair("mode", mode), Pair("target", target),
            new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array(picks.Select(BattleArgument.String))) });
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
        static void Click(Button button)
        {
            Vector2 point = Center(button);
            Assert.That(TopHit(point).GetComponentInParent<Button>(), Is.SameAs(button), button.name + " must be the top hit at its own center");
            ExecuteEvents.ExecuteHierarchy(TopHit(point), new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        }
        // A real tap at a fixed screen point, whatever is drawn there now: a click on the top raycast hit, as the input module delivers it.
        // The hit must belong to a live button of the current view, asserted before the click: the click may redraw synchronously and retire
        // that button before this returns. Returns that owner so callers can check which control it was.
        static Button TapAt(Vector2 point)
        {
            var hit = TopHit(point); var owner = hit.GetComponentInParent<Button>();
            Assert.That(owner, Is.Not.Null, "the tap at " + point + " must land on a control");
            Assert.That(owner!.IsActive(), Is.True, owner.name + " must be a live control of the current view when tapped");
            ExecuteEvents.ExecuteHierarchy(hit, new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            return owner;
        }
        // A pointer pressed on a control by its own top raycast hit and held (released later by Release), as the input module does it.
        static PointerEventData Press(EventSystem events, Button button)
        {
            var pointer = new PointerEventData(events) { position = Center(button), button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); events.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), button.name + " must be the top hit at its own center");
            pointer.pointerPressRaycast = hits[0]; pointer.pointerCurrentRaycast = hits[0]; pointer.pressPosition = pointer.position; pointer.eligibleForClick = true;
            pointer.pointerPress = ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerDownHandler) ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
            pointer.rawPointerPress = hits[0].gameObject;
            Assert.That(pointer.pointerPress, Is.SameAs(button.gameObject), button.name + " takes its own press");
            return pointer;
        }
        static void Release(EventSystem events, PointerEventData pointer)
        {
            var hits = new List<RaycastResult>(); events.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject), Is.SameAs(pointer.pointerPress), "the release lands on the pressed control");
            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerClickHandler);
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
