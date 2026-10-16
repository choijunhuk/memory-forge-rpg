// P1-CARD-FLOW-PICKER-GUARD §14.3/§14.7: Play Mode checks of the expedition view's card-pick sheet over the real shared-core Run and the
// exported full content. The sheet is a local unsent intent of one play: it offers exactly the shared PickCandidates list and accepts
// nothing else, refuses every other command and selection while open, never sends once the state it was opened in is gone, and spends
// nothing until its final pick or auto sends the one play. Commands go through the component's public methods or a real EventSystem
// raycast; an independent mirror Run receives the same public Core commands and must stay identical.
// Documented seams, no production hook: (1) the client's private live Run is read by reflection to issue one real public Core command
// beneath an open sheet, mirrored on the mirror Run; the public gate now refuses every such command, so this is the only way to make the
// sheet's state go stale. (2) The private session store is read, and the private Do boundary called, by reflection as in the failure
// tests. Original-content reachability: the reference party only reaches one-pick hand requests (nia_tune), so the growth filter and the
// reservation/discard zones are checked on the same public helper over public Battle.Create states of real cards. (3) One explicitly
// TEST-LOCAL branch fixture drives the actual client's two-pick path: nia_tune's effects are replaced by Core's existing two-card hand
// discard in the client's own parsed content (read by reflection) and separately in the mirror's, and restored in finally. That variant is
// not authored-game or source-oracle evidence; Resources and source content are unchanged. Not Android/device, server or account acceptance.
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

namespace MemoryForge.Client.Tests
{
    public class ExpeditionPickerGuardPlayModeTests
    {
        const long Seed = 1234;
        const string SheetOpen = "PICKER_OPEN", Invalid = "PICK_INVALID", Stale = "PICK_STALE", Recovery = "RECOVERY_REQUIRED";
        const string Diagnostic = @"^\[MemoryForge\] Practice expedition quarantined after a failed local Run calculation \(";
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
            public string Card = "", Enemy = "";
        }
        // Everything a refused or display-only input must leave alone: the whole Run graph with its AP, RNG, log and battle events, the
        // whole session store with its receipts, the memory request and version.
        sealed class Baseline
        {
            public string Run = "", Store = "";
            public string? Request;
            public long Version;
        }

        // The seed is set on a disabled host before the component's real OnEnable start; nothing else is injected.
        IEnumerator Open(Session s, long seed = Seed)
        {
            var host = new GameObject("PickerGuardTestHost"); hosts.Add(host);
            host.SetActive(false);
            var client = host.AddComponent<MemoryForgeExpeditionClient>(); client.Seed = seed; client.Length = "short";
            host.SetActive(true);
            yield return null;
            s.Host = host; s.Client = client; s.Data = ClientRunContent.Load();
            s.Mirror = CoreRun.Create(s.Data, MemoryForgeExpeditionClient.PracticeSetup(seed, "short", "practice-1-" + seed, new Dictionary<string, object?>(), 0));
            AssertSame(s, "the view's Run must be exactly Create of the public PracticeSetup");
        }
        // Actual commands through the bounded driver until the live hand holds the reference kit's nia_tune playable now against the first
        // living enemy. It asks for one hand card and is the only choice card the reference party starts with.
        IEnumerator OpenAtTune(Session s)
        {
            yield return Open(s);
            for (int steps = 0; steps < 3000; steps++)
            {
                var state = s.Mirror.State;
                if (state.Phase == "VICTORY" || state.Phase == "DEFEAT") break;
                if (state.Phase == "BATTLE" && state.Battle != null && state.Battle.Phase == "PLAYER")
                {
                    var b = state.Battle; string enemy = b.Enemies.First(e => e.Alive).Uid;
                    string? uid = b.Zones.Hand.FirstOrDefault(u => b.Cards[u].Id == "nia_tune" && s.Mirror.BattleCheck(u, "now", enemy) == null);
                    if (uid != null) { s.Card = uid; s.Enemy = enemy; break; }
                }
                Step(s, state);
                if (steps % 8 == 7) yield return null;
            }
            Assert.That(s.Card, Is.Not.Empty, "the reference kit's nia_tune must reach a playable hand within the bounded actual commands");
            Assert.That((s.Client.PickerOpen, s.Client.HasTurnSummary, s.Client.RecoveryRequired), Is.EqualTo((false, false, false)));
            yield return null;
        }
        static void Prepare(Session s)
        {
            s.Client.SelectEnemy(s.Enemy); s.Client.SelectCard(s.Card); s.Client.SelectMode("now");
            Assert.That(Selection(s.Client), Is.EqualTo(new string?[] { s.Card, "now", s.Enemy }));
        }

        // The shared list is what the sheet draws, what PickerOptions reports and what the public helper returns. Every other card (the
        // playing card, out-of-zone draw/discard/exhaust/reservation cards, enemies, unknown ids) and every other command or selection is
        // refused without adding, sending or changing the Run, its AP/RNG/log/events or the store's receipts. One real tap on a drawn
        // candidate is then exactly one actual play with that pick, and the replaced sheet's buttons reach nothing.
        [UnityTest]
        public IEnumerator TheSheetAcceptsOnlyItsSharedCandidatesAndItsFinalPickIsExactlyOneActualPlay()
        {
            var s = new Session(); yield return OpenAtTune(s);
            var client = s.Client; string card = s.Card, enemy = s.Enemy;
            Prepare(s);
            var start = Capture(s);
            yield return null; Canvas.ForceUpdateCanvases();
            Click(ButtonNamed(s.Host, "ConfirmCard"));
            Assert.That(client.PickerOpen, Is.True, "a choice card opens its sheet first");
            Assert.That(client.PickerPicks, Is.Empty);
            AssertPure(s, start, "opening the sheet");
            yield return null; Canvas.ForceUpdateCanvases();

            var b = s.Mirror.State.Battle!;
            var defs = s.Data.Memory.Battle.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
            var request = MemoryForgeExpeditionClient.ChoiceRequests(s.Mirror.BattleEffectiveCard(card), "now", s.Data.Memory.Battle).Single();
            Assert.That((request.From, request.Count, request.GrowthOnly), Is.EqualTo(("hand", 1, false)), "nia_tune asks for one hand card");
            // P1-CARD-FLOW-EFFECT-CANDIDATES: the sheet offers only the hand cards Core's tune(one) effect itself takes (no amp yet, rarity not C:
            // Battle.Effects tune); the zone helper without a rule still returns the whole hand without the playing card.
            var zone = b.Zones.Hand.Where(u => u != card).ToList();
            var expected = zone.Where(u => !b.Cards[u].Temp.Contains("amp") && defs[b.Cards[u].Id].Rarity != "C").ToList();
            Assert.That(expected, Is.Not.Empty, "a hand card to tune");
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(b, card, request, Array.Empty<string>(), defs), Is.EqualTo(zone), "the zone helper offers the hand without the playing card");
            Assert.That(client.PickerOptions, Is.EqualTo(expected), "the open sheet's list is the tune effect's own candidates");
            Assert.That(ActiveView(s.Host).GetComponentsInChildren<Button>().Where(x => x.name.StartsWith("Pick:", StringComparison.Ordinal)).Select(x => x.name.Substring(5)),
                Is.EqualTo(expected), "the sheet draws exactly that list");

            // Source, out-of-zone, enemy and unknown ids: refused with the sheet's own code, nothing added or sent.
            var others = new List<string> { card };
            others.AddRange(zone.Except(expected)); // hand cards the effect refuses (none at the usual first nia_tune; refused like any other)
            others.AddRange(b.Zones.Draw); others.AddRange(b.Zones.Discard); others.AddRange(b.Zones.Exhaust); others.AddRange(b.Reservations.Select(r => r.Uid));
            others.AddRange(b.Enemies.Select(e => e.Uid)); others.Add(""); others.Add("not-a-card");
            Assert.That(others.Intersect(expected), Is.Empty);
            Assert.That(b.Zones.Draw.Count + b.Zones.Discard.Count + b.Zones.Exhaust.Count, Is.GreaterThan(0), "out-of-zone card witnesses");
            foreach (string uid in others)
            {
                Assert.That(client.PickCard(uid), Is.False, "pick '" + uid + "'");
                Assert.That(client.LastError, Is.EqualTo(Invalid), "pick '" + uid + "'");
                Assert.That((client.PickerOpen, client.PickerPicks.Count), Is.EqualTo((true, 0)), "pick '" + uid + "' was added");
                AssertPure(s, start, "pick '" + uid + "'");
            }

            // While the sheet is open every other command and selection is refused before the Run with the sheet code.
            string other = expected[0], actor = b.Actors[0].Id;
            string otherEnemy = b.Enemies.Where(e => e.Alive).Select(e => e.Uid).FirstOrDefault(u => u != enemy) ?? enemy;
            var commands = new (string Name, Func<bool> Call)[]
            {
                ("ConfirmCard", () => client.ConfirmCard()), ("PlayCard", () => client.PlayCard(card, "now")), ("PlayCard other", () => client.PlayCard(other, "now")),
                ("PlayCard with picks", () => client.PlayCard(card, "now", new[] { other })), ("EndTurn", () => client.EndTurn()), ("UseUltimate", () => client.UseUltimate(actor)),
                ("EnterNode", () => client.EnterNode("none")), ("TakeCard", () => client.TakeCard("sera_round")), ("SkipCard", () => client.SkipCard()),
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
                Assert.That(call(), Is.False, name + " must be refused while the sheet is open");
                Assert.That(client.LastError, Is.EqualTo(SheetOpen), name);
                Assert.That(client.PickerOpen, Is.True, name + " closed the sheet");
                AssertPure(s, start, name);
            }
            var selections = new (string Name, Action Call)[]
            {
                ("SelectCard", () => client.SelectCard(other)), ("SelectMode", () => client.SelectMode("next")), ("SelectEnemy", () => client.SelectEnemy(otherEnemy)),
                ("StartPractice", () => client.StartPractice())
            };
            foreach (var (name, call) in selections)
            {
                call();
                Assert.That(client.LastError, Is.EqualTo(SheetOpen), name);
                Assert.That(Selection(client), Is.EqualTo(new string?[] { card, "now", enemy }), name + " changed the prepared intent");
                Assert.That((client.PickerOpen, client.PickerPicks.Count), Is.EqualTo((true, 0)), name);
                AssertPure(s, start, name);
            }
            // The lock is the Run-command boundary itself: the private Do refuses beneath the sheet without running its command.
            bool ran = false;
            Assert.That(Do(client, () => { ran = true; }), Is.False);
            Assert.That((ran, client.LastError, client.RecoveryRequired), Is.EqualTo((false, (string?)SheetOpen, false)), "the private Do boundary");
            AssertPure(s, start, "the private Do boundary");

            // Real taps on controls beneath the sheet meet the sheet, never the covered control.
            foreach (string name in new[] { "EndTurn", "Hand:" + other, "Enemy:" + enemy, "ConfirmCard", "Ult:" + actor, "LogButton", "DeckButton", "AbandonButton" })
            {
                var covered = ActiveView(s.Host).GetComponentsInChildren<Button>().FirstOrDefault(x => x.name == name);
                Assert.That(covered, Is.Not.Null, name + " is drawn beneath the sheet");
                Vector2 point = Center(covered!);
                var top = TopHit(point).GetComponentInParent<Button>();
                Assert.That(top, Is.Not.SameAs(covered), name + " must be covered by the sheet");
                if (top == null) TapAt(point);
                Assert.That(client.PickerOpen, Is.True, "a tap at " + name);
                AssertPure(s, start, "a tap at " + name);
            }

            // Display-only reads stay available and keep the sheet: the candidate's forecast and a hand card's text.
            var picks = new List<string> { other };
            Assert.That(Project(client.PlayForecast(card, "now", picks)!), Is.EqualTo(Project(s.Mirror.BattleForecast(card, "now", enemy, picks))));
            Assert.That(client.ShowHandCardInfo(other), Is.True);
            client.CloseCardInfo();
            Assert.That((client.PickerOpen, client.PickerPicks.Count), Is.EqualTo((true, 0)));
            Assert.That(client.PickerOptions, Is.EqualTo(expected));
            AssertPure(s, start, "display-only reads");

            // The final pick by a real tap: exactly one actual play with that pick, matched by the mirror.
            yield return null; Canvas.ForceUpdateCanvases();
            var pickButton = ButtonNamed(s.Host, "Pick:" + other); var autoButton = ButtonNamed(s.Host, "PickAuto");
            var before = s.Mirror.State.Battle!;
            Click(pickButton);
            s.Mirror.BattleCommand("play", Play(card, "now", enemy, new[] { other }));
            Assert.That((client.LastError, client.PickerOpen), Is.EqualTo(((string?)null, false)));
            AssertSame(s, "the final pick is exactly one actual play with that pick");
            if (client.Snapshot!.Battle is BattleState done) Assert.That(done.V, Is.EqualTo(before.V + 1), "exactly one battle command");
            Assert.That((client.LastRequestId, client.SessionMemoryVersion, StorePrint(client)), Is.EqualTo((start.Request, start.Version, start.Store)), "a battle play writes no memory request or receipt");

            // Repeated old callbacks: the replaced sheet's buttons are inactive, and the old ids no longer have a sheet.
            string played = Fingerprint(client.Snapshot!);
            pickButton.onClick.Invoke(); autoButton.onClick.Invoke();
            Assert.That(client.PickCard(other), Is.False);
            Assert.That(client.AutoPick(), Is.False);
            Assert.That(client.PickerOpen, Is.False);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(played), "no second play");
            AssertSame(s, "repeated old callbacks reached nothing");
        }

        // A committed change beneath an open sheet (seam 1) leaves its still-drawn candidate unable to send: the real tap is refused with the
        // stale code, the sheet closes, nothing is sent, refunded or retargeted, and the old buttons and ids reach nothing afterwards.
        [UnityTest]
        public IEnumerator AStaleSheetsStillDrawnCandidateCannotSendAndTheSheetCloses()
        {
            var s = new Session(); yield return OpenAtTune(s);
            var client = s.Client; string enemy = s.Enemy;
            Prepare(s);
            Assert.That(client.ConfirmCard(), Is.False);
            Assert.That(client.PickerOpen, Is.True);
            yield return null; Canvas.ForceUpdateCanvases();
            string chosen = client.PickerOptions[0];
            var pickButton = ButtonNamed(s.Host, "Pick:" + chosen); var autoButton = ButtonNamed(s.Host, "PickAuto");

            Beneath(s, "end");
            Assert.That(client.PickerOpen, Is.True, "nothing has redrawn yet: the old sheet and its buttons are still live");
            Assert.That(pickButton.IsActive() && pickButton.IsInteractable(), Is.True);
            Assert.That(client.PickerOptions, Is.Empty, "a sheet whose state is gone offers nothing");
            string changed = Fingerprint(client.Snapshot!);

            Click(pickButton);
            Assert.That(client.LastError, Is.EqualTo(Stale));
            Assert.That(client.Notice, Does.Contain("고르기를 닫았어요"));
            Assert.That(client.PickerOpen, Is.False);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(changed), "the stale pick sent nothing: no play and no Core fallback");
            AssertSame(s, "only the change beneath the sheet happened");
            var live = s.Mirror.State.Battle;
            if (live != null && live.Enemies.Any(e => e.Uid == enemy && e.Alive)) Assert.That(client.Target, Is.EqualTo(enemy), "the target is not replaced");

            pickButton.onClick.Invoke(); autoButton.onClick.Invoke();
            Assert.That(client.PickCard(chosen) || client.AutoPick(), Is.False);
            Assert.That(client.PickerOpen, Is.False);
            AssertSame(s, "the closed sheet's old buttons and ids reached nothing");
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(ActiveView(s.Host).GetComponentsInChildren<Image>().Any(i => i.name == "BattlePicks"), Is.False, "the stale sheet is not drawn again");
            var now = client.Snapshot!;
            if (now.Phase == "BATTLE" && now.Battle!.Zones.Hand.Count != 0)
            {
                string uid = now.Battle.Zones.Hand[0];
                client.SelectCard(uid);
                Assert.That((client.LastError, client.SelectedCard), Is.EqualTo(((string?)null, (string?)uid)), "no sheet lock remains");
            }
        }

        // Focus/pause resume and disable/enable keep a current sheet; once its state is gone, auto is refused with the stale code and no
        // request reaches Core (Core's automatic choice is never used as a fallback for a stale sheet).
        [UnityTest]
        public IEnumerator ResumeKeepsACurrentSheetAndAutoNeverSendsAStaleOne()
        {
            var s = new Session(); yield return OpenAtTune(s);
            var client = s.Client;
            Prepare(s);
            var start = Capture(s);
            Assert.That(client.ConfirmCard(), Is.False);
            Assert.That(client.PickerOpen, Is.True);
            s.Host.SendMessage("OnApplicationPause", false); s.Host.SendMessage("OnApplicationFocus", true);
            Assert.That(client.PickerOpen, Is.True, "resume keeps a sheet whose opening state still holds");
            s.Host.SetActive(false); s.Host.SetActive(true);
            Assert.That(client.PickerOpen, Is.True, "disable/enable redraws the same sheet");
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(ActiveView(s.Host).GetComponentsInChildren<Image>().Any(i => i.name == "BattlePicks"), Is.True);
            AssertPure(s, start, "resume and disable/enable");

            Beneath(s, "end");
            string changed = Fingerprint(client.Snapshot!);
            Assert.That(client.AutoPick(), Is.False, "auto never sends a stale sheet");
            Assert.That((client.LastError, client.PickerOpen), Is.EqualTo(((string?)Stale, false)));
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(changed), "nothing was sent");
            AssertSame(s, "only the change beneath the sheet happened");
            Assert.That(client.AutoPick(), Is.False);
            AssertSame(s, "a repeated auto reached nothing");
        }

        // Resume after a change beneath the sheet drops it without sending (the existing silent resume rule, now on the same freshness
        // check); its old buttons and ids reach nothing.
        [UnityTest]
        public IEnumerator ResumeDropsASheetWhoseStateChangedBeneathItWithoutSending()
        {
            var s = new Session(); yield return OpenAtTune(s);
            var client = s.Client;
            Prepare(s);
            Assert.That(client.ConfirmCard(), Is.False);
            yield return null; Canvas.ForceUpdateCanvases();
            string chosen = client.PickerOptions[0];
            var pickButton = ButtonNamed(s.Host, "Pick:" + chosen); var autoButton = ButtonNamed(s.Host, "PickAuto");
            Beneath(s, "end");
            string changed = Fingerprint(client.Snapshot!);
            s.Host.SendMessage("OnApplicationFocus", true);
            Assert.That(client.PickerOpen, Is.False, "resume drops a stale sheet");
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(changed), "dropping it sent nothing");
            pickButton.onClick.Invoke(); autoButton.onClick.Invoke();
            Assert.That(client.PickCard(chosen) || client.AutoPick(), Is.False);
            AssertSame(s, "the dropped sheet's buttons and ids reached nothing");
            yield return null; Canvas.ForceUpdateCanvases();
            Assert.That(ActiveView(s.Host).GetComponentsInChildren<Image>().Any(i => i.name == "BattlePicks"), Is.False);
        }

        // Cancel closes the sheet unsent and restores the ordinary selection; the reopened sheet's auto states Core's own forecast and sends
        // exactly that request (no picks: Core's rule) once. Recovery keeps its precedence over the sheet's entry points and stays usable.
        [UnityTest]
        public IEnumerator CancelRestoresTheSelectionAutoSendsCoresOwnChoiceOnceAndRecoveryStaysFirst()
        {
            var s = new Session(); yield return OpenAtTune(s);
            var client = s.Client; string card = s.Card, enemy = s.Enemy;
            Prepare(s);
            var start = Capture(s);
            Assert.That(client.ConfirmCard(), Is.False);
            Assert.That(client.PickCard("not-a-card"), Is.False);
            Assert.That(client.LastError, Is.EqualTo(Invalid));
            client.CancelPick();
            Assert.That((client.PickerOpen, client.LastError), Is.EqualTo((false, (string?)null)));
            Assert.That(Selection(client), Is.EqualTo(new string?[] { card, "now", enemy }), "cancel keeps the card, mode and target");
            AssertPure(s, start, "cancel");
            string other = s.Mirror.State.Battle!.Zones.Hand.First(u => u != card);
            client.SelectCard(other);
            Assert.That((client.LastError, client.SelectedCard), Is.EqualTo(((string?)null, (string?)other)), "a selection is accepted again");
            Prepare(s);
            AssertPure(s, start, "selections after cancel");

            Assert.That(client.ConfirmCard(), Is.False);
            Assert.That(client.PickerOpen, Is.True, "the sheet opens again");
            yield return null; Canvas.ForceUpdateCanvases();
            var auto = s.Mirror.BattleForecast(card, "now", enemy);
            var view = ActiveView(s.Host);
            AssertReadable(view, t => t == "자동으로 고르고 사용하면 · " + client.ForecastText(auto), "the auto request's forecast");
            AssertReadable(view, t => t == "남은 대상은 자동으로 고르고 사용", "the auto button says Core chooses the rest");
            AssertPure(s, start, "reopening and the auto forecast");
            var autoButton = ButtonNamed(s.Host, "PickAuto");
            var before = s.Mirror.State.Battle!;
            Click(autoButton);
            s.Mirror.BattleCommand("play", Play(card, "now", enemy));
            Assert.That((client.LastError, client.PickerOpen), Is.EqualTo(((string?)null, false)));
            AssertSame(s, "auto is exactly one actual play with no picks");
            if (client.Snapshot!.Battle is BattleState done) Assert.That(done.V, Is.EqualTo(before.V + 1));
            autoButton.onClick.Invoke();
            Assert.That(client.AutoPick(), Is.False);
            AssertSame(s, "a repeated auto reached nothing");
            // The auto play's display would refuse the private Do boundary below; it is finished explicitly (display only).
            FinishAction(client);

            // Recovery (seam 2): a failed calculation quarantines; the sheet's own entry points answer the recovery code first.
            LogAssert.Expect(LogType.Error, new Regex(Diagnostic + @"InvalidOperationException\)"));
            Assert.That(Do(client, () => throw new InvalidOperationException("test fault")), Is.False);
            Assert.That(client.RecoveryRequired, Is.True);
            foreach (var (name, call) in new (string Name, Func<bool> Call)[] { ("PickCard", () => client.PickCard(other)), ("AutoPick", () => client.AutoPick()), ("ConfirmCard", () => client.ConfirmCard()) })
            {
                Assert.That(call(), Is.False, name);
                Assert.That(client.LastError, Is.EqualTo(Recovery), name);
            }
            client.CancelPick();
            Assert.That(client.LastError, Is.EqualTo(Recovery));
            Assert.That((client.PickerOpen, client.PickerOptions.Count, client.PickerPicks.Count), Is.EqualTo((false, 0, 0)));
            Assert.That(client.DiscardFailedPractice(), Is.True, client.LastError);
            Assert.That((client.RecoveryRequired, client.PickerOpen, client.Snapshot!.RunId), Is.EqualTo((false, false, "practice-2-" + (Seed + 1))));
        }

        // Seam 3, TEST-LOCAL branch fixture (not authored content): after the real drive reaches nia_tune, its definition in both parsed
        // contents asks for two hand discards ({op: discard, n: 2}, an existing Core effect). Through public input only, the first valid pick
        // stays unsent, its repeat is refused without appending or spending, and a second distinct pick sends exactly one play equal to the
        // mirror's. Both definitions are restored in finally.
        [UnityTest]
        public IEnumerator TestLocalTwoPickVariantKeepsTheFirstPickUnsentRefusesItsRepeatAndSendsOnce()
        {
            var s = new Session(); yield return OpenAtTune(s);
            var client = s.Client; string card = s.Card, enemy = s.Enemy;
            var defs = new[] { ClientData(client), s.Data }.Select(d => d.Memory.Battle.Cards.Single(c => c.Id == "nia_tune")).ToList();
            Assert.That(defs[0], Is.Not.SameAs(defs[1]), "the client and the mirror parse separate content");
            var originals = defs.Select(d => d.Effects).ToList();
            try
            {
                foreach (var d in defs) d.Effects = new List<EffectDef> { new EffectDef { Op = "discard", N = 2 } };
                var request = MemoryForgeExpeditionClient.ChoiceRequests(s.Mirror.BattleEffectiveCard(card), "now", s.Data.Memory.Battle).Single();
                Assert.That((request.From, request.Count, request.GrowthOnly), Is.EqualTo(("hand", 2, false)), "the test-local variant asks for two hand cards");
                Prepare(s);
                var start = Capture(s);
                Assert.That(client.ConfirmCard(), Is.False);
                Assert.That(client.PickerOpen, Is.True);
                var options = client.PickerOptions.ToList();
                Assert.That(options.Count, Is.GreaterThanOrEqualTo(2), "two distinct hand cards to pick");
                string first = options[0], second = options[1];

                Assert.That(client.PickCard(first), Is.True, "the first of two picks is accepted");
                Assert.That(client.PickerOpen, Is.True, "the first pick stays unsent");
                Assert.That(client.PickerPicks, Is.EqualTo(new[] { first }));
                Assert.That(client.PickerOptions, Does.Not.Contain(first));
                AssertPure(s, start, "the first pick");
                Assert.That(client.PickCard(first), Is.False, "a repeat of the first pick");
                Assert.That(client.LastError, Is.EqualTo(Invalid));
                Assert.That(client.PickerPicks, Is.EqualTo(new[] { first }), "the repeat was not appended");
                AssertPure(s, start, "the repeated pick");
                yield return null; Canvas.ForceUpdateCanvases();
                Assert.That(ActiveView(s.Host).GetComponentsInChildren<Button>().Any(x => x.name == "Pick:" + first), Is.False, "the picked card is not drawn again");

                var before = s.Mirror.State.Battle!;
                Click(ButtonNamed(s.Host, "Pick:" + second));
                s.Mirror.BattleCommand("play", Play(card, "now", enemy, new[] { first, second }));
                Assert.That((client.LastError, client.PickerOpen), Is.EqualTo(((string?)null, false)));
                AssertSame(s, "the second pick sends exactly one actual play with both picks");
                if (client.Snapshot!.Battle is BattleState done) Assert.That(done.V, Is.EqualTo(before.V + 1), "exactly one battle command");
                Assert.That(client.PickCard(second) || client.AutoPick(), Is.False);
                AssertSame(s, "repeated picks after the play reached nothing");
            }
            finally
            {
                for (int i = 0; i < defs.Count; i++) defs[i].Effects = originals[i];
            }
        }

        // A card without picks never opens a sheet: the confirm is the one actual play, and the end that follows is not locked.
        [UnityTest]
        public IEnumerator ACardWithoutPicksPlaysAtOnceAndNoSheetLockEngages()
        {
            var s = new Session(); yield return Open(s);
            var client = s.Client;
            string first = s.Mirror.Available()[0];
            Assert.That(Both(s, () => client.EnterNode(first), r => r.Enter(first)), Is.True);
            var b = s.Mirror.State.Battle!; string enemy = b.Enemies.First(e => e.Alive).Uid;
            string? card = b.Zones.Hand.FirstOrDefault(u => s.Mirror.BattleAllowedModes(u).Contains("now") && s.Mirror.BattleCheck(u, "now", enemy) == null
                && MemoryForgeExpeditionClient.ChoiceRequests(s.Mirror.BattleEffectiveCard(u), "now", s.Data.Memory.Battle).Count == 0);
            Assert.That(card, Is.Not.Null, "seed 1234's opening hand offers an immediate card without card picks");
            client.SelectEnemy(enemy); client.SelectCard(card!); client.SelectMode("now");
            Assert.That(Both(s, client.ConfirmCard, r => r.BattleCommand("play", Play(card!, "now", enemy))), Is.True);
            Assert.That(client.PickerOpen, Is.False);
            FinishAction(client); // the play's display, not a sheet lock, is what the end waits for
            Assert.That(Both(s, client.EndTurn, r => r.BattleCommand("end")), Is.True);
            Dismiss(client);
            yield return null;
        }

        // Supplement for requests the reference party cannot reach, on the same public helper over public Battle.Create states of real
        // cards (c1..c10 in deck order): the playing card and picked cards are never offered (sion_forget's two discard picks cannot repeat),
        // only the request's zone is offered, the growth filter offers exactly the deck's growth cards, and pull offers the reservations.
        [Test]
        public void TheSharedCandidateHelperFiltersSourceZoneRepeatsAndGrowthOnCoreBattleStates()
        {
            var content = ClientBattleContent.Load().Battle;
            var defs = content.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
            (string Label, string From, int Count, bool GrowthOnly) Request(string id) => MemoryForgeExpeditionClient.ChoiceRequests(defs[id], "now", content).Single();
            var tune = Request("nia_tune"); var grow = Request("yuri_dew"); var copy = Request("yuri_metamorph"); var forget = Request("sion_forget"); var pull = Request("sera_pull");
            Assert.That(new[] { (tune.From, tune.Count, tune.GrowthOnly), (grow.From, grow.Count, grow.GrowthOnly), (copy.From, copy.Count, copy.GrowthOnly),
                (forget.From, forget.Count, forget.GrowthOnly), (pull.From, pull.Count, pull.GrowthOnly) },
                Is.EqualTo(new[] { ("hand", 1, false), ("hand", 1, true), ("hand", 1, true), ("discard", 2, false), ("res", 1, false) }), "the real definitions' requests");
            List<string> Offer(BattleState b, string source, (string Label, string From, int Count, bool GrowthOnly) request, params string[] picked) =>
                MemoryForgeExpeditionClient.PickCandidates(b, source, request, picked, defs);

            var battle = Begin(content, 21, new[] { "yuri", "sera", "sion" },
                new[] { "yuri_dew", "yuri_sprout", "yuri_cocoon", "sera_pull", "sion_forget", "com_guard", "com_guard", "com_guard", "com_guard", "com_guard" }, "e_cog");
            var first = battle.State;
            var hand1 = first.Zones.Hand.ToList(); var draw1 = first.Zones.Draw.ToList();
            Assert.That((hand1.Count, draw1.Count, first.Zones.Discard.Count, first.Reservations.Count), Is.EqualTo((5, 5, 0, 0)), "precondition: five drawn, five left");
            var growth = new[] { "c2", "c3" }; // yuri_sprout and yuri_cocoon
            Assert.That(first.Cards.Values.Where(c => defs[c.Id].Growth == true).Select(c => c.Uid).OrderBy(u => u, StringComparer.Ordinal), Is.EqualTo(growth),
                "precondition: the deck's only growth definitions");

            // Hand requests: the hand in order without the playing card, then without every card already picked.
            string source = hand1[0];
            Assert.That(Offer(first, source, tune), Is.EqualTo(hand1.Skip(1).ToList()));
            Assert.That(Offer(first, source, tune, hand1[1]), Is.EqualTo(hand1.Skip(2).ToList()), "a picked card is not offered again");
            Assert.That(Offer(first, source, tune, hand1[1], hand1[2]), Is.EqualTo(hand1.Skip(3).ToList()));
            Assert.That(Offer(first, source, tune).Intersect(draw1), Is.Empty, "draw-pile cards are out of zone");
            Assert.That((Offer(first, source, forget).Count, Offer(first, source, pull).Count), Is.EqualTo((0, 0)), "empty discard and reservation zones offer nothing");
            var grown = Offer(first, "c1", grow);
            Assert.That(grown.All(u => hand1.Contains(u) && growth.Contains(u)), Is.True, "only growth cards of the hand");
            Assert.That(Offer(first, "c1", copy), Is.EqualTo(grown), "copyPick uses the same growth filter");
            foreach (string uid in grown) Assert.That(Offer(first, uid, grow), Does.Not.Contain(uid), "the playing card is never offered");

            // After an actual end the first hand lies in the discard pile and the former draw pile is the hand.
            Commit(battle, "end");
            var second = battle.State;
            Assert.That(second.Zones.Discard, Is.EquivalentTo(hand1), "precondition: the first hand was discarded");
            Assert.That(second.Zones.Hand, Is.EquivalentTo(draw1), "precondition: the second hand is the former draw pile");
            var hand2 = second.Zones.Hand.ToList(); var discard = second.Zones.Discard.ToList();
            Assert.That(Offer(second, hand2[0], forget), Is.EqualTo(discard), "sion_forget offers the discard pile in order");
            Assert.That(Offer(second, hand2[0], forget, discard[0]), Is.EqualTo(discard.Skip(1).ToList()), "its second pick cannot repeat the first");
            Assert.That(Offer(second, hand2[0], forget).Intersect(hand2), Is.Empty, "hand cards are out of the discard zone");
            Assert.That(grown.Concat(Offer(second, "c1", grow)).OrderBy(u => u, StringComparer.Ordinal), Is.EqualTo(growth), "over both hands the growth filter offered exactly the growth cards");

            // A next-turn reservation: pull offers exactly the reserved card, which has left every hand request.
            string enemy = second.Enemies.First(e => e.Alive).Uid;
            string? reserved = hand2.FirstOrDefault(u => battle.Check(u, "next", enemy) == null);
            Assert.That(reserved, Is.Not.Null, "precondition: a reservable card in the second hand");
            Commit(battle, "play", Play(reserved!, "next", enemy));
            var third = battle.State;
            Assert.That(third.Reservations.Select(r => r.Uid), Is.EqualTo(new[] { reserved }));
            Assert.That(Offer(third, "c1", pull), Is.EqualTo(new[] { reserved }));
            Assert.That(Offer(third, "c1", pull, reserved!), Is.Empty, "a picked reservation is not offered again");
            Assert.That(Offer(third, third.Zones.Hand[0], tune), Does.Not.Contain(reserved), "a reserved card is no longer a hand candidate");
        }

        // ───────── drivers: one public command per call, mirrored on the Core Run ─────────
        static void Step(Session s, RunState state)
        {
            var client = s.Client; var p = state.Pending; var events = s.Data.Events;
            switch (state.Phase)
            {
                case "MAP": { string id = s.Mirror.Available()[0]; Both(s, () => client.EnterNode(id), r => r.Enter(id)); return; }
                case "BATTLE": BattleStep(s, state.Battle!); return;
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
        // The existing bounded battle order: the first legal immediate card without picks, else a choice card through its sheet's auto,
        // else a ready ultimate, else the end (its summary closed explicitly).
        static void BattleStep(Session s, BattleState b)
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
                    if (Both(s, client.AutoPick, r => r.BattleCommand("play", Play(card, "now", enemy)))) { FinishAction(client); return; }
                }
                else if (Both(s, client.ConfirmCard, r => r.BattleCommand("play", Play(card, "now", enemy)))) { FinishAction(client); return; }
            }
            var ready = b.Actors.FirstOrDefault(a => a.Gauge >= content.Rules.GaugeMax);
            if (ready != null)
            {
                client.SelectEnemy(enemy);
                var args = BattleArgument.Object(new[] { Pair("actor", ready.Id), Pair("target", enemy) });
                if (Both(s, () => client.UseUltimate(ready.Id), r => r.BattleCommand("ult", args))) { FinishAction(client); return; }
            }
            if (Both(s, client.EndTurn, r => r.BattleCommand("end"))) Dismiss(client);
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
        // Seam 1: one real public Core command on the client's own live Run (read by reflection) and the same command on the mirror. It
        // stands for any committed change the open sheet did not see; the client's public gate refuses every such command.
        static void Beneath(Session s, string kind)
        {
            var live = (CoreRun)typeof(MemoryForgeExpeditionClient).GetField("run", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s.Client)!;
            live.BattleCommand(kind); s.Mirror.BattleCommand(kind);
            AssertSame(s, "the change beneath the sheet is one actual Core command on both Runs");
        }
        // Seam 3: the client's own parsed content, read by reflection as in the failure tests.
        static RunData ClientData(MemoryForgeExpeditionClient client) =>
            (RunData)typeof(MemoryForgeExpeditionClient).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
        // Seam 2: the private Do boundary, as in the failure tests (no production test hook).
        static bool Do(MemoryForgeExpeditionClient client, Action command) =>
            (bool)typeof(MemoryForgeExpeditionClient).GetMethod("Do", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(client, new object?[] { command, null, true })!;

        // ───────── purity and comparison ─────────
        static Baseline Capture(Session s) => new Baseline { Run = Fingerprint(s.Client.Snapshot!), Store = StorePrint(s.Client), Request = s.Client.LastRequestId, Version = s.Client.SessionMemoryVersion };
        static void AssertPure(Session s, Baseline b, string what)
        {
            Assert.That(Fingerprint(s.Client.Snapshot!), Is.EqualTo(b.Run), what + " reached the Run (state, AP, RNG, log or events)");
            Assert.That(StorePrint(s.Client), Is.EqualTo(b.Store), what + " changed the session store or its receipts");
            Assert.That((s.Client.LastRequestId, s.Client.SessionMemoryVersion), Is.EqualTo((b.Request, b.Version)), what + " made a memory request");
            AssertSame(s, what + ": the mirror received nothing either");
        }
        static void AssertSame(Session s, string message) => Assert.That(Fingerprint(s.Client.Snapshot!), Is.EqualTo(Fingerprint(s.Mirror.State)), message);
        static string?[] Selection(MemoryForgeExpeditionClient client) => new[] { client.SelectedCard, client.SelectedMode, client.Target };
        // CanonicalState omits "log"/"ev", so the Run log and the battle log/events are hashed separately for a complete comparison.
        static string Fingerprint(RunState state)
        {
            var graph = state.ToJsonGraph();
            string battle = graph["battle"] is Dictionary<string, object?> b ? CanonicalState.Hash(b["log"]) + CanonicalState.Hash(b["ev"]) : "-";
            return CanonicalState.Hash(new Dictionary<string, object?> { ["run"] = graph, ["memory"] = null }) + "/" + CanonicalState.Hash(graph["log"]) + "/" + battle;
        }
        // The complete private session store graph (schema, memory, version, history, receipts, …) as JSON text, read-only.
        static string StorePrint(MemoryForgeExpeditionClient client) =>
            JToken.FromObject(typeof(MemoryForgeExpeditionClient).GetField("store", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!).ToString(Formatting.None);
        static (string?, string, int, int?, bool, int, int, int, bool, int, int) Project(BattlePlayForecast f) =>
            (f.Error, f.Mode, f.Cost, f.Due, f.Hidden, f.EnemyHpLoss, f.BlockDelta, f.HpDelta, f.Defeat, f.EnemyDamage, f.NewlyBroken);

        // ───────── small copies of the expedition tests' helpers ─────────
        static Battle Begin(BattleData content, uint seed, string[] party, string[] deck, params string[] enemies)
        {
            var setup = new BattleSetup { Hp = 243, MaxHp = 243, Seed = seed };
            setup.Party.AddRange(party.Select(id => new BattleActorSetup { CharId = id })); setup.Deck.AddRange(deck.Select(id => new BattleCardSpec { Id = id }));
            setup.Enemies.AddRange(enemies);
            var battle = Battle.Create(content, setup); battle.Command(new BattleRequest("begin", 0, "begin"));
            return battle;
        }
        static void Commit(Battle battle, string kind, BattleArgument? args = null) => battle.Command(new BattleRequest(kind + "-" + battle.State.V, battle.State.V, kind, args));
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
        static GameObject ActiveView(GameObject host)
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
