#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MemoryForge.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CoreRun = MemoryForge.Core.Run;

namespace MemoryForge.Client
{
    // P1-UNITY-EXPEDITION-CLIENT §7/§8/§14.1-14.3/§16.2: expedition practice over the real shared-core Run, separate from the battle
    // practice. Map, battle, reward, service, archive and memory actions are the Run's public commands and queries; the view keeps no
    // rule of its own (it labels what Core returns) and never injects or restores Run state. The v2 memory store is session-local
    // practice data held by this component only — no PlayerPrefs, file, account, server or real-asset write — so closing the app
    // discards it, and the screens say so. Full card text is Core's BattleText.Card of the battle's effective definition (hand) or of
    // the source definition (reward, shop, deck). The play forecast on the action line and the pick sheet labels Core's pure
    // Run.BattleForecast of the exact request a button would send; reading it never reaches the Run. A next/after outcome is Core's
    // current-state estimate (the original preview) and is labelled as such.
    // P1-CARD-FLOW-TURN-BARRIER §14.3/§14.7/§16.4: after an actual end the committed events are shown as a blocking text summary
    // (BattleTurnDigest of public projections) over the already committed view. While it is open every public command and selection
    // is refused before it reaches the Run; its timer, skip and continue only change text. It is local practice, not a server reply.
    // P1-CARD-FLOW-FAIL-CLOSED §14.7/§16.4: a Run command that fails with anything but an ordinary, unchanged input rejection may have
    // left the Run partly changed (Run commands are not transactional), so the practice is quarantined: only a recovery screen and its
    // single action remain, which discards the failed practice Run unsaved and departs again from the last valid session store.
    // P1-CARD-FLOW-PICKER-GUARD §14.3/§14.7: the card-pick sheet is a local unsent intent of one play, tied to the state it was opened in.
    // It offers exactly PickCandidates and accepts nothing else; once that state is gone it can no longer send and closes. While it is
    // open every other command and selection is refused. Nothing is spent until its final pick or auto sends the one play.
    // P1-CARD-FLOW-ACTION-PRESENTATION §14.2/§14.3/§14.7: after an actual play or ultimate its own committed digest rows step through a
    // small inline strip (a covering sheet once the battle has ended) over the already committed view. Meanwhile only the pure next card/
    // mode/target selection is accepted; every command is refused until the display finishes, skips or is dropped. Speed, reduced motion
    // and skip change only that text (local prototype timing, unscaled real time), never Time.timeScale or the Run.
    // P1-CARD-FLOW-ONE-UNSENT-INTENT §14.2/§14.3/§14.7: during a non-terminal display the player may confirm at most one next play (a
    // local UI bound, not a reservation slot). Its request is frozen with the state it was confirmed in and nothing reaches the Run; on
    // the Update after the display closes it is taken, checked against that state and sent as exactly one Run.BattleCommand, or dropped
    // with its reason. Cancel, leaving the app or disabling drops it unsent. There is no server, ACK or request id behind it.
    [ExecuteAlways]
    public sealed class MemoryForgeExpeditionClient : MonoBehaviour
    {
        public long Seed = 1234;
        public string Length = "short";
        public const string OwnedCanvas = "MemoryForgeExpeditionView";
        // The source's explicit reference party (res 0, Create's default weapons); not account ownership or saved growth.
        public static readonly IReadOnlyList<string> ReferenceParty = new[] { "sera", "harin", "nia" };

        public RunState? Snapshot => run?.State;
        public string? LastError { get; private set; }
        public string Notice => notice;
        public string? SelectedCard => selectedCard;
        public string? SelectedMode => selectedMode;
        public string? Target => target;
        public bool PickerOpen => picker != null;
        /// <summary>The open pick sheet's picks so far, in order (a copy); empty without a sheet.</summary>
        public IReadOnlyList<string> PickerPicks => picker?.Picks.ToArray() ?? Array.Empty<string>();
        /// <summary>The cards the open pick sheet offers now: exactly the list it draws and the only cards PickCard accepts. Empty
        /// without a sheet, or once the state the sheet was opened in is gone.</summary>
        public IReadOnlyList<string> PickerOptions
        {
            get
            {
                var live = run; var pick = picker;
                if (recovery != null || live == null || pick == null) return Array.Empty<string>();
                var s = live.State;
                if (!Fresh(pick, s)) return Array.Empty<string>();
                return PickOptions(s.Battle!, pick);
            }
        }
        public bool ShowingResult => run != null && Finished(run.State);
        public IReadOnlyList<string> SelectedChangeIds => selectedChanges.AsReadOnly();
        public string? LastRequestId { get; private set; }
        public long SessionMemoryVersion => Integer(Get(store, "version"));
        /// <summary>A copy of the session-local memory graph {charId: [card specs]}; empty before the first save.</summary>
        public Dictionary<string, object?> SessionMemory => Copy(Get(store, "memory")) as Dictionary<string, object?> ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        public bool CardInfoOpen => cardInfo != null;
        /// <summary>The open card detail's effect text: exactly BattleText.Card of the definition named by CardInfoBasis.</summary>
        public string? CardInfoText => cardInfo?.Text;
        public string? CardInfoBasis => cardInfo?.Basis;
        /// <summary>True while the turn-result summary of the last committed end is open; every public command and selection is refused.</summary>
        public bool HasTurnSummary => turnSummary != null;
        /// <summary>True when every row of the open summary is shown (after the 0.3 s reservation steps, or SkipTurnSummary).</summary>
        public bool TurnSummaryFullyShown => turnSummary != null && summaryShown >= turnSummary.Count;
        /// <summary>The open summary's immutable rows (BattleTurnDigest of the committed end); empty when none is open.</summary>
        public IReadOnlyList<BattleDigestEntry> TurnSummaryRows => turnSummary ?? Array.Empty<BattleDigestEntry>();
        /// <summary>A copy of the recent battle event rows (at most 40) the log sheet shows; kept after a battle until the next departure.</summary>
        public IReadOnlyList<string> BattleLog => battleLog.ToArray();
        /// <summary>True after a failed Run calculation quarantined the practice: every command and selection is refused and only
        /// DiscardFailedPractice remains. It survives disable/enable and focus/pause.</summary>
        public bool RecoveryRequired => recovery != null;
        /// <summary>Presentation pace of a committed player action. Display only: it never changes Time.timeScale or reaches the Run.</summary>
        public enum ActionPacing { Normal, Double, Reduced }
        /// <summary>The chosen pace (session-local preference; not saved).</summary>
        public ActionPacing ActionPace => actionPace;
        /// <summary>True while the last actual play/ultimate is presented; only the next card/mode/target selection is accepted then.</summary>
        public bool HasActionPresentation => actionRows != null;
        /// <summary>The open display's rows: the committed command's event rows and enemy results (no summary or previous-intent rows).</summary>
        public IReadOnlyList<BattleDigestEntry> ActionPresentationRows => actionRows ?? Array.Empty<BattleDigestEntry>();
        /// <summary>How many rows of the open display are shown; 0 without one.</summary>
        public int ActionPresentationShown => actionRows == null ? 0 : actionShown;
        /// <summary>True while the open display is the battle's last command (the battle has left the Run): it covers the next screen.</summary>
        public bool ActionPresentationTerminal => actionRows != null && actionTerminal;
        /// <summary>How many confirmed plays may wait unsent: a conservative local UI bound, not a measured value or a reservation slot.</summary>
        public const int PreparedLimit = 1;
        /// <summary>True while one confirmed play waits unsent for the current player-action display to finish. It reaches the Run only
        /// at the dispatch on the Update after the display closes, or is dropped. A confirm that prepares it returns false.</summary>
        public bool HasPreparedIntent => prepared != null;
        /// <summary>The waiting intent's frozen card uid, mode and target; null without one.</summary>
        public string? PreparedCard => prepared?.Uid;
        public string? PreparedMode => prepared?.Mode;
        public string? PreparedTarget => prepared?.Target;
        /// <summary>A copy of the waiting intent's frozen ordered picks; empty without one (or for no picks: Core's own choice).</summary>
        public IReadOnlyList<string> PreparedPicks => prepared?.Picks.ToArray() ?? Array.Empty<string>();

        enum Sheet { None, Deck, Log, Abandon, Assign, DeckPick }
        enum Purpose { Upgrade, ForgeOffer, ForgeRemove, ShopRemove, EventCards, FoundPart }
        sealed class BattlePick
        {
            public string Uid = "", Mode = "now";
            public List<(string Label, string From, int Count, bool GrowthOnly)> Requests = new List<(string, string, int, bool)>();
            public int Index, Left;
            public readonly List<string> Picks = new List<string>();
            // The state the sheet was opened in (see Fresh): its Run, battle node, battle version and target.
            public CoreRun? Run;
            public string? Node, Target;
            public int Version;
        }
        // The one confirmed, unsent next play (see Prepare): its frozen request and the state it was confirmed in. Never changed once made.
        sealed class PreparedPlay
        {
            public string Uid = "", Mode = "now";
            public string? Target, Node;
            public IReadOnlyList<(string Label, string From, int Count, bool GrowthOnly)> Requests = Array.Empty<(string, string, int, bool)>();
            public IReadOnlyList<string> Picks = Array.Empty<string>();
            public CoreRun? Run;
            public int Version, Turn;
        }
        // A read-only snapshot for the detail overlay; opening or closing it never reaches the Run.
        sealed class CardInfo
        {
            public string Title = "", Basis = "", Facts = "", Text = "", Notes = "";
        }

        RunData? data;
        CoreRun? run;
        Dictionary<string, object?>? store, lastSave;
        string? closedKind; // "closed": save skipped at the terminal screen; "abandon": given up mid-expedition
        int departures, requestSerial, lastSaveCount;
        readonly Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, BattleCardDef> cardDefs = new Dictionary<string, BattleCardDef>(StringComparer.Ordinal);
        readonly Dictionary<string, BattlePartDef> partDefs = new Dictionary<string, BattlePartDef>(StringComparer.Ordinal);
        readonly Dictionary<string, BattleRelicDef> relicDefs = new Dictionary<string, BattleRelicDef>(StringComparer.Ordinal);
        readonly Dictionary<string, string> characterNames = new Dictionary<string, string>(StringComparer.Ordinal);
        RectTransform? canvasRoot, frame;
        Font? font;
        string? selectedCard, selectedMode, target;
        BattlePick? picker;
        PreparedPlay? prepared;
        CardInfo? cardInfo;
        Sheet sheet;
        Purpose purpose;
        int need, eventIndex;
        string? partId;
        string deckTitle = "", notice = "", noticeFallback = "";
        float noticeUntil;
        Text? noticeLabel; // the current view's label showing `notice`; cleared with the view
        Color noticeFallbackColor;
        readonly List<string> deckPicks = new List<string>(), selectedChanges = new List<string>();
        // Turn summary (cosmetic): rows of the committed end, how many are shown, and when the next reservation row may appear (<0: no wait).
        IReadOnlyList<BattleDigestEntry>? turnSummary;
        int summaryShown;
        float summaryRevealAt = -1;
        Text? summaryText, summaryStatus; // the current view's summary labels, rewritten in place; cleared with the view
        RectTransform? summaryContent;
        ScrollRect? summaryScroll;
        readonly List<string> battleLog = new List<string>();
        const string TurnSummaryOpen = "TURN_SUMMARY_OPEN";
        const float ReservedRevealSeconds = 0.3f; // §14.3 예약 해결: 0.3 s per card, skippable
        const int BattleLogLimit = 40;            // §14.3 기록: the recent 40 lines
        string? recovery; // the failure that quarantined the practice (a Core code or exception type); null while healthy
        const string RecoveryCode = "RECOVERY_REQUIRED";
        // Player-action display (cosmetic): its rows, how many are shown, when the next row may appear and when the finished display
        // closes (<0: not scheduled), whether the battle ended with it, and the labels/controls it rewrites in place (cleared with the view).
        IReadOnlyList<BattleDigestEntry>? actionRows;
        int actionShown;
        float actionNextAt = -1, actionUntil = -1;
        bool actionTerminal, actionAll, actionDropped;
        string actionTitle = "";
        ActionPacing actionPace = ActionPacing.Normal;
        Text? actionText, actionStatus;
        Button? actionSkip;
        Text? confirmCaption; // the plain confirm's caption, rewritten in place when a display opens or closes (사용 대기 / 사용 확정)
        GameObject? actionOverlay;
        readonly List<(Selectable Control, bool Enabled)> actionGated = new List<(Selectable, bool)>();
        readonly List<(Image Back, Text Caption, ActionPacing Pace)> paceButtons = new List<(Image, Text, ActionPacing)>();
        const string ActionShowing = "ACTION_SHOWING";
        // Local prototype timing (not a measured or approved target): each row 0.2 s, an actual time-reservation resolution row the
        // original 0.3 s, then 0.6 s with every row shown. 2x halves all three; reduced motion shows every row at once and keeps the hold.
        const float ActionRowSeconds = 0.2f, ActionHoldSeconds = 0.6f;
        // Pick sheet refusals: another command or selection while a sheet is open, a card outside its drawn list, and any pick or auto
        // once the state the sheet was opened in is gone. These are local client codes; Core never sees the refused input.
        const string PickSheetOpen = "PICKER_OPEN", PickInvalid = "PICK_INVALID", PickStale = "PICK_STALE";
        // Unsent-intent codes (local, never sent to Core): a command, selection or second confirm while one waits; a waiting request whose
        // confirmed state is gone at its dispatch; one dropped because the app was left or the view disabled.
        const string IntentPending = "INTENT_PENDING", IntentStale = "INTENT_STALE", IntentDropped = "INTENT_DROPPED";
        // Ordinary input rejections: Core refuses the request as asked (wrong phase, unreachable node, missing card/target/AP, unpaid
        // price, invalid memory selection…) after checks that precede any change. Only these, and only when the Run and the session
        // store compare unchanged, stay a normal toast. Engine/definition gaps, invariants, number ranges, unknown ids, malformed
        // arguments, snapshots, versions and request conflicts are absent on purpose: the practice can no longer be trusted, so they
        // quarantine even when nothing changed. INVALID_PART is absent because the same code also reports a damaged memory store.
        static readonly HashSet<string> ExpectedRejections = new HashSet<string>(StringComparer.Ordinal)
        {
            "NODE_UNREACHABLE", "NO_BATTLE", "WRONG_PHASE", "INVALID_MODE", "NOT_IN_HAND", "RESERVE_ONLY", "NOT_RESERVABLE", "AP_LOW", "BAD_TARGET",
            "FIXED_MARK_REQUIRED", "SLOT_FULL", "DEBT_CAP", "LOAN_OVERFLOW", "NOT_DEFERRABLE", "RESTRAINT_UNAVAILABLE", "NO_EMPTY_SLOT",
            "RESERVATION_REQUIRED", "PULL_DESTINATION_FULL", "OVERLOAD_WOULD_KILL", "UNKNOWN_ACTOR", "ULT_NOT_READY",
            "BAD_REWARD", "BAD_EVOLUTION", "NO_PART", "BAD_CARD", "BAD_PART", "BAD_UPGRADE", "BAD_CHOICE", "CANNOT_HEAL", "NOT_FOR_SALE",
            "COINS_LOW", "WOULD_KILL", "BAD_ASSIGN", "REASSIGNMENT_REQUIRES_SEPARATE_FLOW",
            "INVALID_SELECTION", "DEFEAT_KEEP_ONE", "OVER_CAPACITY"
        };
        static readonly Color Background = Hex("0d1322"), Panel = Hex("141c31"), Line = Hex("2f3d63"), Ink = Hex("e9eef8"),
            Muted = Hex("98a5c0"), Brass = Hex("e2b45f"), Memory = Hex("62d6c6"), Danger = Hex("ff6f6f");
        const string PracticeNote = "연습용 탐사 · 진행과 기억은 이 세션에만 있어요. 앱을 닫으면 사라져요.";

        void OnEnable()
        {
            if (Resources.Load<TextAsset>(ClientRunContent.ResourcePath) == null || Resources.Load<TextAsset>("MemoryForge/battle-content") == null) return;
            if (run == null) StartPractice(); else Render();
        }
        // Disabling drops only the cosmetic summary, the player-action display and their timers, and the unsent intent (never sent);
        // OnEnable redraws the actual Run without restarting or changing it.
        void OnDisable() { RemoveView(); CloseTurnSummary(); CloseActionPresentation(); DropPrepared(IntentDropped); }
        void Update()
        {
            FitView();
            if (noticeUntil > 0 && Time.realtimeSinceStartup >= noticeUntil) ExpireNotice();
            if (turnSummary != null && summaryShown < turnSummary.Count) AdvanceTurnSummary(Time.realtimeSinceStartup);
            if (actionRows != null) AdvanceActionPresentation(Time.realtimeSinceStartup);
            else if (prepared != null) DispatchPrepared(); // the Update after the display closed on its timer or by skip
        }
        void OnApplicationPause(bool paused) { if (paused) Leave(); else ResumeView(); }
        void OnApplicationFocus(bool focused) { if (focused) ResumeView(); else Leave(); }
        // Leaving the app (focus loss, pause) drops the unsent intent, so nothing is sent unseen, and then the committed display.
        void Leave() { if (DropPrepared(IntentDropped)) actionDropped = true; DropActionPresentation(); }
        // Resume: an unsent intent still waiting is dropped (never sent on resume), a pick sheet whose opening state is gone (Fresh) is
        // dropped, and the view is redrawn from the actual Run only when one of those happened, a player-action display or intent was
        // dropped while away, or the view is missing. No Run command, replay, restart or server fetch; an open summary stays (it is not stale).
        void ResumeView()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || run == null || data == null || font == null) return;
            if (recovery != null) { if (canvasRoot == null) Render(); return; } // the failed Run is not read; the quarantine stays
            var s = run.State; bool stale = actionDropped; actionDropped = false;
            if (DropPrepared(IntentDropped)) stale = true;
            if (picker != null && !Fresh(picker, s)) { picker = null; stale = true; }
            if (stale || canvasRoot == null) Render();
        }
        // §14.2 2.6 s toast: expiring it rewrites only its own label to the screen's fallback text and color. Rebuilding the view here
        // would destroy the canvas, its buttons and the owned EventSystem while a pointer may be held, dropping that press.
        void ExpireNotice()
        {
            noticeUntil = 0; LastError = null; notice = "";
            if (noticeLabel != null) { noticeLabel.text = noticeFallback; noticeLabel.color = noticeFallbackColor; }
        }
        Text TrackNotice(Text label, string fallback, Color fallbackColor)
        {
            noticeLabel = label; noticeFallback = fallback; noticeFallbackColor = fallbackColor; return label;
        }

        // ───────── departure ─────────
        /// <summary>The exact Run.Create input of a practice departure: the reference party, a normal difficulty, the original
        /// short/full zone rule and v2 memory with the session store's memory and version.</summary>
        public static RunSetup PracticeSetup(long seed, string length, string runId, object? memory, long memoryVersion)
        {
            var setup = new RunSetup { Seed = seed, Length = length, MemoryPolicy = "v2", RunId = runId, MemoryVersion = memoryVersion,
                Memory = memory, Difficulty = new RunDifficulty { Hp = 100, Damage = 100 } };
            foreach (string id in ReferenceParty) setup.Party.Add(new RunPartyEntry { CharId = id, Res = 0 });
            return setup;
        }

        /// <summary>New practice session: an empty session-local v2 memory store, then the first departure.</summary>
        public void StartPractice()
        {
            if (Blocked()) return;
            Load();
            store = MemoryV2.Migrate(data!.Memory, new Dictionary<string, object?>(StringComparer.Ordinal),
                new Dictionary<string, object?>(StringComparer.Ordinal)).ToJsonGraph();
            run = null; departures = 0; requestSerial = 0;
            Depart();
        }

        /// <summary>Next departure from the session store's saved memory and version. Refused while an expedition is in progress.</summary>
        public bool Depart(string? length = null)
        {
            if (Blocked()) return false;
            if (data == null || store == null) { StartPractice(); return run != null; }
            if (run != null && !Finished(run.State)) { Reject("RUN_IN_PROGRESS"); return false; }
            string chosen = length ?? Length; long seed = Seed + departures;
            string runId = "practice-" + (departures + 1) + "-" + seed;
            try { run = CoreRun.Create(data, PracticeSetup(seed, chosen, runId, Copy(Get(store, "memory")), SessionMemoryVersion)); }
            catch (RuleException error) { Reject(error.Code); return false; }
            battleLog.Clear(); // the battle log belongs to one expedition
            departures++; Length = chosen; closedKind = null; lastSave = null; lastSaveCount = 0; LastRequestId = null;
            selectedCard = null; selectedMode = null; target = null; picker = null; prepared = null; cardInfo = null; sheet = Sheet.None; selectedChanges.Clear();
            CloseActionPresentation();
            LastError = null; notice = "출발했어요. 밝게 표시된 칸을 골라 나아가세요."; noticeUntil = Time.realtimeSinceStartup + 2.6f;
            Render(); return true;
        }

        void Load()
        {
            if (font == null) font = Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular");
            if (font == null) throw new InvalidOperationException("The licensed Korean interface font is missing.");
            if (data != null) return;
            data = ClientRunContent.Load();
            foreach (var card in data.Memory.Battle.Cards) cardDefs[card.Id] = card;
            foreach (var part in data.Memory.Battle.Parts) partDefs[part.Id] = part;
            foreach (var relic in data.Memory.Battle.Relics) relicDefs[relic.Id] = relic;
            foreach (var character in data.Memory.Battle.Characters) characterNames[character.Id] = character.Name;
            var messages = Resources.Load<TextAsset>("MemoryForge/error-messages");
            if (messages != null)
                foreach (var pair in Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(messages.text)!) errors[pair.Key] = pair.Value;
        }

        // ───────── commands (each button calls exactly one of these) ─────────
        public bool EnterNode(string nodeId)
        {
            if (Blocked()) return false;
            bool ok = Do(() => run!.Enter(nodeId), null, false);
            if (ok)
            {
                // A new battle's own begin events (opening relic/weapon effects, turn 1) start its part of the battle log; no summary.
                var s = run!.State;
                if (s.Phase == "BATTLE" && s.Battle != null) AppendBattleLog(Digest(null, s.Battle.Events, PublicBattle(s.Battle, false), s.Battle));
            }
            Render(); return ok;
        }
        // Selections pass a battle's player-action display (Locked, not Blocked): preparing the next card spends nothing. While an unsent
        // intent waits they are refused: changing it takes an explicit cancel first.
        public void SelectCard(string uid) { if (Locked()) return; selectedCard = uid; selectedMode = null; ClearIntentNotice(); Render(); }
        public void SelectMode(string mode) { if (Locked()) return; selectedMode = mode; ClearIntentNotice(); Render(); }
        public void SelectEnemy(string uid) { if (Locked()) return; target = uid; ClearIntentNotice(); Render(); }
        // A new card/mode/target intent retires the previous command's toast (or error) at once, so the action line shows this request's
        // forecast before any confirm. Only view text changes; the Run is untouched.
        void ClearIntentNotice() { noticeUntil = 0; LastError = null; notice = ""; }
        /// <summary>Plays the selected card, or first opens the pick overlay when the card lets the player choose cards (spends nothing).
        /// During a non-terminal player-action display it instead confirms the selection as the one unsent intent (HasPreparedIntent)
        /// or opens the overlay for it; either way it returns false, because nothing was committed.</summary>
        public bool ConfirmCard()
        {
            // Locked, not Blocked: a non-terminal display lets the confirm prepare. A waiting intent refuses a second one (no overwrite).
            if (Locked() || run == null || selectedCard == null || selectedMode == null) return false;
            List<(string Label, string From, int Count, bool GrowthOnly)> requests;
            try { requests = ChoiceRequests(run.BattleEffectiveCard(selectedCard), selectedMode, data?.Memory.Battle); }
            catch (RuleException error) { Reject(error.Code); return false; }
            // The sheet (and an intent) keeps the state it is made in; a card that is not a live hand card of the player phase opens nothing.
            var s = run.State;
            var pick = new BattlePick { Uid = selectedCard, Mode = selectedMode, Requests = requests, Left = requests.Count == 0 ? 0 : requests[0].Count,
                Run = run, Node = s.Position, Version = s.Battle?.V ?? -1, Target = target };
            if (requests.Count == 0) return actionRows != null ? Prepare(pick) : PlayCard(selectedCard, selectedMode);
            if (!Fresh(pick, s)) { Reject(run.BattleCheck(selectedCard, selectedMode, target) ?? "NOT_IN_HAND"); return false; }
            picker = pick;
            Render(); return false;
        }
        public bool PlayCard(string uid, string mode = "now", IReadOnlyList<string>? picks = null) => Play(uid, mode, target, picks);
        // One play request with an explicit target: the current selection's (PlayCard) or a waiting intent's frozen one (DispatchPrepared).
        bool Play(string uid, string mode, string? aim, IReadOnlyList<string>? picks)
        {
            if (Blocked()) return false;
            var args = new List<KeyValuePair<string, BattleArgument>> { Pair("uid", uid), Pair("mode", mode), Pair("target", aim),
                new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array((picks ?? new string[0]).Select(BattleArgument.String))) };
            string name = BattleCardName(uid);
            return SendBattle("play", BattleArgument.Object(args), mode == "now" ? name + " 사용" : name + " 예약");
        }
        public bool EndTurn() => SendBattle("end", null, "적의 행동과 예약 해결을 마쳤어요.");
        public bool UseUltimate(string actor) => SendBattle("ult", BattleArgument.Object(new[] { Pair("actor", actor), Pair("target", target) }), "필살기를 사용했어요.");
        // The sheet's own pick, auto and cancel are its explicit owners: they pass the recovery/summary gate (Barred) only, never the
        // sheet lock of Blocked.
        /// <summary>Adds one card of the open sheet's current list (exactly the cards it draws); the last pick plays at once (during a
        /// player-action display: confirms the one unsent intent and returns false). Any other card is refused without adding or
        /// sending; any pick once the sheet's opening state is gone closes the sheet unsent.</summary>
        public bool PickCard(string uid)
        {
            if (Barred() || run == null || picker == null) return false;
            var pick = picker; var s = run.State;
            if (!Fresh(pick, s)) return RetirePicker();
            if (!PickOptions(s.Battle!, pick).Contains(uid)) { LastError = PickInvalid; return false; }
            pick.Picks.Add(uid); pick.Left--;
            if (pick.Left <= 0 && ++pick.Index < pick.Requests.Count) pick.Left = pick.Requests[pick.Index].Count;
            if (pick.Index >= pick.Requests.Count) return SendPicks(pick);
            Render(); return true;
        }
        /// <summary>Plays with the picks made so far; Core chooses any remaining ones by its own rule, as the button says (during a
        /// player-action display: confirms that request as the one unsent intent and returns false). Once the sheet's opening state is
        /// gone it closes the sheet unsent instead.</summary>
        public bool AutoPick() => !Barred() && picker != null && SendPicks(picker);
        /// <summary>Closes the open sheet without sending anything; the selected card, mode and target stay as the ordinary selection.</summary>
        public void CancelPick()
        {
            if (Barred()) return;
            picker = null;
            if (LastError == PickSheetOpen || LastError == PickInvalid) LastError = null; // refusals of the closed sheet
            Render();
        }
        // The sheet's one play, checked against its opening state right before sending. The sheet closes first, so the ordinary gate
        // (which refuses everything else while a sheet is open) lets exactly this request through, whatever Core then answers. While a
        // player-action display is open the same request becomes the one unsent intent instead; once the display has closed (even if it
        // closed while the sheet was open) it plays at once, as without a display.
        bool SendPicks(BattlePick pick)
        {
            if (run == null || !Fresh(pick, run.State)) return RetirePicker();
            picker = null;
            return actionRows != null ? Prepare(pick) : PlayCard(pick.Uid, pick.Mode, pick.Picks);
        }
        // A sheet whose opening state is gone closes with a notice. It spent nothing, so nothing is sent, refunded or retargeted.
        bool RetirePicker() { picker = null; Reject(PickStale); return false; }

        // ───────── one unsent next play (§14.2/§14.3/§14.7: local UI intent; no server, ACK, request id or reservation slot) ─────────
        // Freezes one explicitly confirmed request while the last play/ultimate is shown: the Run, battle node, turn and version it was
        // confirmed in, its card, mode, target and a copy of its ordered picks. Nothing reaches the Run, so AP, cards, RNG, receipts and
        // time-reservation slots stay as they are (a next/after request takes its slot only when sent). A request Core would refuse now,
        // a card no longer in hand or a dead target never becomes an intent. Returns false: nothing was committed.
        bool Prepare(BattlePick from)
        {
            var live = run; var b = live?.State.Battle;
            if (live == null || b == null) return false;
            var intent = new PreparedPlay { Uid = from.Uid, Mode = from.Mode, Target = from.Target, Node = from.Node, Requests = from.Requests.ToArray(),
                Picks = from.Picks.ToArray(), Run = from.Run, Version = from.Version, Turn = b.Turn };
            string? reason = Unsendable(intent);
            if (reason != null) { Reject(reason); return false; }
            prepared = intent;
            LastError = null; notice = "사용 대기 " + PreparedLimit + "/" + PreparedLimit + " · " + BattleCardName(intent.Uid) + " · 방금 행동의 결과 표시가 끝나면 보내요.";
            noticeUntil = Time.realtimeSinceStartup + 2.6f;
            Render(); return false;
        }
        /// <summary>Drops the waiting unsent intent without sending it: nothing was spent, so nothing is refunded. The card, mode and
        /// target stay as the ordinary selection. False (and nothing happens) without one.</summary>
        public bool CancelPreparedIntent()
        {
            if (Barred() || prepared == null) return false;
            prepared = null;
            LastError = null; notice = "사용 대기를 취소했어요. 아무것도 쓰지 않았어요."; noticeUntil = Time.realtimeSinceStartup + 2.6f;
            Render(); return true;
        }
        // The intent's command boundary, reached from Update once no display is open: the natural end or a skip only unblocks it, and
        // neither calls the Run. The slot is cleared first, so reentry, a later Update or a repeated skip finds nothing to send. A request
        // whose confirmed state is gone is dropped with its reason: nothing is sent, refunded, retargeted or retried. Otherwise exactly
        // its frozen card, mode, target and picks go out as one Run.BattleCommand through the ordinary SendBattle, which opens that
        // play's own display.
        void DispatchPrepared()
        {
            var intent = prepared;
            if (intent == null || actionRows != null) return;
            prepared = null;
            if (LastError == IntentPending) LastError = null;
            string? reason = Unsendable(intent);
            if (reason == null) { Play(intent.Uid, intent.Mode, intent.Target, intent.Picks); return; }
            Dropped(IntentStale, reason); Render();
        }
        // Null while the frozen request still holds, else why not: the same Run, node, turn and battle version in the player phase, its
        // card in hand, its target (if any) a living enemy, the same pick requests with every frozen pick, in order, on the shared
        // candidate list (PickCandidates) given the picks before it, and finally Core's own precheck of exactly this request. The client
        // list check comes first, so a pick that is gone is never handed to Core's automatic remaining-picks rule; fewer picks than
        // requested is an explicit auto, whose rest stays Core's choice. Core's effect predicates are not duplicated or changed here.
        string? Unsendable(PreparedPlay p)
        {
            var live = run; var s = live?.State; var b = s?.Battle;
            if (live == null || s == null || b == null || !ReferenceEquals(live, p.Run) || s.Phase != "BATTLE" || b.Phase != "PLAYER" || s.Position != p.Node
                || b.Turn != p.Turn || b.V != p.Version) return IntentStale;
            if (!b.Zones.Hand.Contains(p.Uid)) return "NOT_IN_HAND";
            if (p.Target != null && !b.Enemies.Any(e => e.Uid == p.Target && e.Alive)) return "BAD_TARGET";
            List<(string Label, string From, int Count, bool GrowthOnly)> requests;
            try { requests = ChoiceRequests(live.BattleEffectiveCard(p.Uid), p.Mode, data?.Memory.Battle); }
            catch (RuleException error) { return error.Code; }
            if (!requests.SequenceEqual(p.Requests) || !PicksHold(b, p.Uid, requests, p.Picks)) return PickInvalid;
            return live.BattleCheck(p.Uid, p.Mode, p.Target, p.Picks);
        }
        // The ordered picks replayed through the sheet's own progression: each on its request's current list given the picks before it.
        bool PicksHold(BattleState b, string source, List<(string Label, string From, int Count, bool GrowthOnly)> requests, IReadOnlyList<string> picks)
        {
            if (picks.Count > requests.Sum(r => r.Count)) return false;
            var made = new List<string>(); int index = 0, left = requests.Count == 0 ? 0 : requests[0].Count;
            foreach (string uid in picks)
            {
                if (!PickCandidates(b, source, requests[index], made, cardDefs).Contains(uid)) return false;
                made.Add(uid);
                if (--left <= 0 && ++index < requests.Count) left = requests[index].Count;
            }
            return true;
        }
        // Drops a waiting intent unsent and says why; false when none waited. The view is redrawn by resume/enable, not here.
        bool DropPrepared(string code)
        {
            if (prepared == null) return false;
            prepared = null; Dropped(code, null); return true;
        }
        // The drop notice stays until the next intent or command (the player may only see it after coming back).
        void Dropped(string code, string? reason)
        {
            LastError = code; noticeUntil = 0;
            notice = ErrorText(code) + (reason == null || reason == code ? "" : " (" + ErrorText(reason) + ")");
        }
        bool SendBattle(string kind, BattleArgument? args, string success)
        {
            if (Blocked()) return false;
            var live = run;
            if (live == null) return false;
            // Only public fields of the battle before the command are projected; its draw order and RNG are never read for display.
            var previous = live.State.Battle;
            var before = previous == null ? null : PublicBattle(previous, true);
            BattleResponse? response = null;
            bool ok = Do(() => response = live.BattleCommand(kind, args), success, false);
            if (ok && response != null)
            {
                picker = null; // defensive: the gate refuses battle commands under a sheet, and the sheet's own play closed it first
                if (kind == "play") selectedMode = null;
                var s = live.State;
                if (s.Phase == "REWARD") notice = "전투에서 이겼어요. 보상을 고르세요.";
                if (s.Phase == "DEFEAT") notice = "파티가 쓰러졌어요. 남길 기억을 고르세요.";
                // A finished battle leaves the Run: the honest after-view is then only the battle's final phase and the Run's HP.
                var after = s.Battle != null ? PublicBattle(s.Battle, false) : new BattleDigestSnapshot { Phase = response.Phase, Hp = s.Hp };
                var rows = Digest(before, response.Events, after, s.Battle);
                AppendBattleLog(rows);
                if (kind == "end") OpenTurnSummary(rows);
                else OpenActionPresentation(rows, kind == "ult", s.Phase != "BATTLE");
            }
            Render(); return ok;
        }

        public bool TakeCard(string id) => Do(() => run!.TakeCard(id), "카드를 덱에 넣었어요.");
        public bool SkipCard() => Do(() => run!.SkipCard(), "카드를 받지 않았어요.");
        public bool TakeRelic(string id) => Do(() => run!.TakeRelic(id), "유물을 얻었어요.");
        public bool TakePart() => Do(() => run!.TakePart(), "부품을 챙겼어요. 덱 화면에서 카드에 붙일 수 있어요.");
        public bool Evolve(string key, string branchId) => Do(() => run!.Evolve(key, branchId), CardName(branchId) + "(으)로 진화했어요.");
        public bool Proceed() => Do(() => run!.Proceed(), null);
        public bool RestHeal() => Do(() => run!.Rest("heal"), "체력을 회복했어요.");
        public bool RestUpgrade(string key) => Do(() => run!.Rest("upgrade", key), "카드를 강화했어요.");
        public bool ForgeOffer(string key) => Do(() => run!.ForgeOffer(key), "붙일 부품을 고르세요.");
        public bool ForgeApply(string part) => Do(() => run!.ForgeApply(part), "부품을 붙였어요.");
        public bool ForgeRemove(string key) => Do(() => run!.ForgeRemove(key), "카드를 덱에서 뺐어요.");
        public bool Buy(string kind, string id) => Do(() => run!.Buy(kind, id), "구매했어요.");
        public bool BuyRemoval(string key) => Do(() => run!.Buy("remove", null, key), "카드를 덱에서 뺐어요.");
        /// <summary>Event choice; keys are the player's explicit picks for card copy/removal choices (one per card).</summary>
        public bool ChooseEvent(int index, IReadOnlyList<string>? keys = null) => Do(() => run!.EventChoose(index, keys?.Cast<object?>().ToList()), null);
        public bool Archive(string choice) => Do(() => run!.Archive(choice), choice == "return" ? "기록소에서 귀환했어요. 남길 기억을 고르세요." : "체력을 조금 회복하고 다음 구역으로 나아가요.");
        public bool ApplyFoundPart(string part, string key) => Do(() => run!.ApplyFoundPart(part, key), "부품을 붙였어요.");
        public bool Assign(string key, string charId)
        {
            if (Blocked()) return false;
            bool ok = Do(() => run!.Assign(key, charId), "배정을 바꿨어요. 고른 변화는 초기화했어요.", false);
            if (ok) selectedChanges.Clear();
            Render(); return ok;
        }
        /// <summary>Gives up a running expedition without touching the session store; the terminal save screen uses CloseWithoutSave.</summary>
        public bool Abandon()
        {
            if (Blocked() || run == null || Finished(run.State) || SaveOpen(run.State)) return false;
            closedKind = "abandon"; sheet = Sheet.None; picker = null; cardInfo = null; LastError = null; notice = "탐사를 포기했어요. 기억 덱은 바뀌지 않았어요."; Render(); return true;
        }

        // ───────── v2 memory: selection is local, preview and commit are the Run's ─────────
        /// <summary>The Run's pure MemoryChanges graph (a fresh copy), or null without a Run.</summary>
        public Dictionary<string, object?>? MemoryChanges() => recovery != null ? null : run?.MemoryChanges();
        /// <summary>Toggles one actual change ID of this Run's terminal change list; nothing is spent or committed.</summary>
        public bool ToggleChange(string changeId)
        {
            if (Blocked() || run == null) return false;
            var s = run.State;
            if (!SaveOpen(s)) { Reject("WRONG_PHASE"); return false; }
            if (!ChangeOrder(run.MemoryChanges(), s).Contains(changeId)) { Reject("INVALID_SELECTION"); return false; }
            if (!selectedChanges.Remove(changeId)) selectedChanges.Add(changeId);
            LastError = null; notice = ""; Render(); return true;
        }
        /// <summary>The Run's PreviewMemory of the current selection in change-list order ({memory, used, error, capacities}).</summary>
        public Dictionary<string, object?>? PreviewSelection() => recovery != null || run == null || !SaveOpen(run.State) ? null : SafePreview(Flatten(run.MemoryChanges(), run.State));
        /// <summary>Commits the selection with a fresh requestId and the Run's own runId/expected version against the session store.
        /// Core validates everything; on success the returned store becomes the session store for the next departure.</summary>
        public bool CommitMemory()
        {
            if (Blocked() || run == null) return false;
            var s = run.State; var flat = SaveOpen(s) ? Flatten(run.MemoryChanges(), s) : new List<string>();
            string requestId = "practice-save-" + s.RunId + "-" + (++requestSerial);
            var request = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["requestId"] = requestId, ["expectedMemoryVersion"] = s.MemoryVersion ?? 0L, ["runId"] = s.RunId, ["selectedChangeIds"] = flat.Cast<object?>().ToList()
            };
            LastRequestId = requestId;
            Dictionary<string, object?>? result = null;
            bool ok = Do(() => result = run!.CommitMemory(request, Copy(store)), flat.Count == 0 ? "기존 기억 덱을 그대로 유지했어요." : "기억을 저장했어요.", false);
            if (ok)
            {
                store = (Dictionary<string, object?>)Get(result, "state")!; lastSave = Get(result, "response") as Dictionary<string, object?>;
                lastSaveCount = flat.Count; selectedChanges.Clear();
            }
            Render(); return ok;
        }
        /// <summary>Closes the finished expedition without writing anything to the session store.</summary>
        public bool CloseWithoutSave()
        {
            if (Blocked()) return false;
            if (run == null || !SaveOpen(run.State)) { Reject("WRONG_PHASE"); return false; }
            closedKind = "closed"; selectedChanges.Clear(); cardInfo = null; LastError = null; notice = "저장하지 않고 탐사를 닫았어요. 기억 덱은 바뀌지 않았어요."; Render(); return true;
        }

        // ───────── card information (read-only: Core's BattleText.Card of one definition; nothing reaches the Run) ─────────
        /// <summary>Full text of a hand card (default: the selected one) from the live battle's effective definition, which already
        /// carries the card's upgrade and resonance overlays. Costs are Core's CostOf in the current state.</summary>
        public bool ShowHandCardInfo(string? uid = null)
        {
            if (recovery != null) return false; // card details read the Run; the failed one is not read
            var live = run; string? chosen = uid ?? selectedCard;
            var b = live?.State.Battle;
            if (live == null || chosen == null || b == null || !b.Zones.Hand.Contains(chosen)) return false;
            string id = chosen; BattleCardDef def; List<string> modes;
            try { def = live.BattleEffectiveCard(id); modes = live.BattleAllowedModes(id); }
            catch (RuleException error) { Reject(error.Code); return false; }
            b.Cards.TryGetValue(id, out var instance);
            var notes = new List<string>();
            var parts = instance == null ? new List<string>() : new[] { instance.Circuit, instance.Edge }.Concat(instance.Temp).Where(m => !string.IsNullOrEmpty(m)).Select(m => m!).ToList();
            if (parts.Count != 0) notes.Add("붙은 부품\n" + string.Join("\n", parts.Select(PartLine)));
            if (instance != null && def.Uses.GetValueOrDefault() != 0) notes.Add("이번 전투 사용 " + instance.Used + " / " + def.Uses);
            if (instance != null && def.Growth == true) notes.Add("성장 " + instance.Growth);
            notes.Add("적힌 수치는 카드 정의의 값이에요. 약점·상태·유물까지 반영한 예상은 방식을 고르면 행동 안내 줄에 보여요. 적 피해에는 적 방어에 막힌 몫도 들어가요. "
                + "예약의 예상은 지금 상태에서 바로 해결한다고 가정한 값이라, 실제 결과는 해결되는 턴의 상태로 정해져요. 드로우·무작위 결과는 미리 보여 주지 않아요.");
            return ShowInfo(new CardInfo
            {
                Title = def.Name + (instance != null && instance.Plus ? "+" : ""),
                Basis = "전투 중 이 카드의 현재 정의" + (instance != null && instance.Plus ? " · 강화(+) 반영" : "") + " · 공명 보정 반영 · 비용은 지금 상태 기준",
                Facts = OwnerText(def.Owner) + " · 등급 " + def.Rarity + "  ·  " + (modes.Count == 0 ? "지금 쓸 수 있는 방식 없음"
                    : string.Join("  ·  ", modes.Select(m => ModeName(m) + " 비용 " + live.BattleCostOf(id, m)))),
                Text = BattleText.Card(def, data!.Memory.Battle), Notes = string.Join("\n\n", notes)
            });
        }
        /// <summary>Full text of an offered card (reward, evolution or shop) from its source definition. Opening it takes or buys nothing.</summary>
        public bool ShowCardInfo(string cardId)
        {
            if (recovery != null || run == null || !cardDefs.TryGetValue(cardId, out var def)) return false;
            return ShowInfo(new CardInfo
            {
                Title = def.Name, Basis = "원본 카드 정의 · 덱에 넣은 뒤 강화나 부품이 붙으면 전투 중 카드 설명에 반영돼요",
                Facts = CardLine(def), Text = BattleText.Card(def, data!.Memory.Battle), Notes = EvolutionNote(def)
            });
        }
        /// <summary>Full text of an expedition deck card. Outside battle this is the source definition; an upgrade (+) is named, not computed.</summary>
        public bool ShowDeckCardInfo(string key)
        {
            if (recovery != null) return false;
            var card = run?.State.Deck.FirstOrDefault(c => c.Key == key);
            if (card == null || card.Id == null || !cardDefs.TryGetValue(card.Id, out var def)) return false;
            var graph = card.ToJsonGraph(); bool plus = Truthy(Get(graph, "plus"));
            var parts = new[] { Get(Get(graph, "mods"), "c") as string, Get(Get(graph, "mods"), "e") as string }.Where(m => !string.IsNullOrEmpty(m)).Select(m => m!).ToList();
            var notes = new List<string>();
            if (parts.Count != 0) notes.Add("붙은 부품\n" + string.Join("\n", parts.Select(PartLine)));
            string evolution = EvolutionNote(def); if (evolution.Length != 0) notes.Add(evolution);
            return ShowInfo(new CardInfo
            {
                Title = SpecText(graph),
                Basis = plus ? "덱 카드 · 아래는 강화(+) 전 원본 정의예요. 강화된 실제 수치는 전투 중 카드 설명에서 보여요." : "덱 카드 · 원본 정의",
                Facts = CardLine(def) + "  ·  " + DeckOwner(card), Text = BattleText.Card(def, data!.Memory.Battle), Notes = string.Join("\n\n", notes)
            });
        }
        public void CloseCardInfo() { cardInfo = null; Render(); }
        bool ShowInfo(CardInfo info) { cardInfo = info; Render(); return true; }
        string PartLine(string id) => "· " + PartName(id) + " (" + SlotName(id) + ") — " + (partDefs.TryGetValue(id, out var part) ? part.Text : "");
        string EvolutionNote(BattleCardDef def) => def.Evolutions == null || def.Evolutions.Count == 0 ? "" : "성장 후 진화 후보: " + string.Join(", ", def.Evolutions.Select(CardName));

        // ───────── play forecast (labels of Core's pure Run.BattleForecast; reading it never reaches the Run) ─────────
        /// <summary>Core's forecast of the exact play request PlayCard would send: this card and mode, the current target and these
        /// picks (null/empty: Core's automatic choice). Null without a Run.</summary>
        public BattlePlayForecast? PlayForecast(string uid, string mode, IReadOnlyList<string>? picks = null) => recovery != null ? null : run?.BattleForecast(uid, mode, target, picks);
        /// <summary>The action line for a forecast: its basis ("예상", or Core's automatic card choice when a pick card's picks stay
        /// unmade), the mode and ForecastText.</summary>
        public string ForecastLine(BattlePlayForecast f, bool autoPicks) => (autoPicks ? "자동 선택 기준 예상" : "예상") + " · " + ModeName(f.Mode) + " · " + ForecastText(f);
        /// <summary>Error text, else the AP cost and ForecastOutcome.</summary>
        public string ForecastText(BattlePlayForecast f) => f.Error != null ? ErrorText(f.Error) : "행동력 " + f.Cost + " · " + ForecastOutcome(f);
        /// <summary>Only the DTO's own values: error text; for a reservation first its absolute due turn, then Core's estimate labelled
        /// "현재 상태 기준 예상" (the original preview: resolving it in the present state, never a promise of that later turn); the
        /// draw/random notice with every number hidden; else the enemy damage including enemy block (the HP loss named when it differs),
        /// the signed party block/HP, newly broken enemies and a defeat warning (for an estimate: if it resolved now).</summary>
        public string ForecastOutcome(BattlePlayForecast f)
        {
            if (f.Error != null) return ErrorText(f.Error);
            bool reserved = f.Mode != "now";
            string due = reserved ? f.Due + "턴 시작(" + ModeName(f.Mode) + ")에 해결 · " : "";
            if (f.Hidden) return due + "드로우·무작위 결과는 " + (reserved ? "해결된" : "사용한") + " 뒤에 알 수 있어요";
            var parts = new List<string>();
            if (f.EnemyDamage != 0) parts.Add("적 피해 " + f.EnemyDamage + (f.EnemyHpLoss == f.EnemyDamage ? "" : f.EnemyHpLoss == 0 ? " (체력 그대로)" : " (체력 -" + f.EnemyHpLoss + ")"));
            else if (f.EnemyHpLoss != 0) parts.Add("적 체력 -" + f.EnemyHpLoss);
            if (f.BlockDelta != 0) parts.Add("방어 " + Signed(f.BlockDelta));
            if (f.HpDelta != 0) parts.Add("파티 체력 " + Signed(f.HpDelta));
            if (f.NewlyBroken != 0) parts.Add("새 균열 " + f.NewlyBroken + "체");
            if (f.Defeat) parts.Add(reserved ? "지금 해결되면 패배해요" : "이 행동으로 패배해요");
            return due + (reserved ? "현재 상태 기준 예상: " : "") + (parts.Count == 0 ? "체력·방어 변화 없음" : string.Join(" · ", parts));
        }
        static string Signed(int value) => (value > 0 ? "+" : "") + value;
        static Color ForecastColor(BattlePlayForecast f) => f.Error != null || f.Defeat ? Danger : Memory;

        bool SaveOpen(RunState s) => closedKind == null && (s.Phase == "VICTORY" || s.Phase == "DEFEAT") && Text(Get(s.Pending, "type")) == "save";
        bool Finished(RunState s) => closedKind != null || s.Phase == "DONE";
        static List<string> ChangeOrder(Dictionary<string, object?> info, RunState s) =>
            s.Party.SelectMany(p => ListOf(Get(Get(info, p.CharId), "changes")).Select(c => Text(Get(c, "id")))).ToList();
        List<string> Flatten(Dictionary<string, object?> info, RunState s) => ChangeOrder(info, s).Where(selectedChanges.Contains).ToList();
        Dictionary<string, object?>? SafePreview(List<string> ids)
        {
            try { return run!.PreviewMemory(ids.Cast<object?>().ToList()); }
            catch (RuleException) { return null; }
        }

        bool Do(Action command, string? success, bool render = true)
        {
            // Every Run command passes here: none reaches the Run under the turn barrier, in recovery or beneath an open pick sheet.
            if (run == null || Blocked()) return false;
            var live = run;
            // Detached copies of the Run and the session store, compared only if the command fails; a copy that cannot be made fails closed.
            RunState before; object? kept;
            try { before = live.State; kept = Copy(store); }
            catch (Exception error) { Quarantine("SNAPSHOT_FAILED", error, null, render); return false; }
            try { command(); }
            catch (Exception error)
            {
                string? code = (error as RuleException)?.Code;
                if (code != null && ExpectedRejections.Contains(code) && Unchanged(live, before, kept)) { Reject(code, render); return false; }
                Quarantine(code ?? error.GetType().Name, error, kept, render); return false;
            }
            cardInfo = null; // the detail described the state before this command
            LastError = null; notice = success ?? ""; noticeUntil = success == null ? 0 : Time.realtimeSinceStartup + 2.6f;
            if (render) Render();
            return true;
        }
        void Reject(string code, bool render = true)
        {
            LastError = code; notice = ErrorText(code); noticeUntil = Time.realtimeSinceStartup + 2.6f;
            if (render) Render();
        }

        // ───────── fail-closed recovery (§14.7/§16.4: local practice quarantine, not a server resync or a transaction) ─────────
        // The Run and the store are compared as JSON trees: JToken keeps legal opaque fractional metadata exactly, which
        // CanonicalState.Hash rejects. A comparison that cannot be made counts as changed.
        bool Unchanged(CoreRun live, RunState before, object? kept)
        {
            try { return SameRun(before, live.State) && SameGraph(kept, store); }
            catch (Exception) { return false; }
        }
        static bool SameRun(RunState a, RunState b) => SameGraph(a.ToJsonGraph(), b.ToJsonGraph());
        static bool SameGraph(object? a, object? b) => SameToken(a == null ? JValue.CreateNull() : JToken.FromObject(a), b == null ? JValue.CreateNull() : JToken.FromObject(b));
        // Objects by member name (order-free), arrays in order, and a Float by its boxed value's own Equals: Json.NET's DeepEquals
        // treats nearby doubles as equal (0.1 vs the next double), so it cannot decide "unchanged". No tolerance, rounding or coercion;
        // a type difference counts as changed. Other primitives keep Json.NET's normal comparison (int and long of one value are equal).
        static bool SameToken(JToken a, JToken b)
        {
            if (a.Type != b.Type) return false;
            switch (a)
            {
                case JObject x:
                    {
                        var y = (JObject)b;
                        if (x.Count != y.Count) return false;
                        foreach (var member in x.Properties())
                        {
                            var other = y.Property(member.Name);
                            if (other == null || !SameToken(member.Value, other.Value)) return false;
                        }
                        return true;
                    }
                case JArray x:
                    {
                        var y = (JArray)b;
                        if (x.Count != y.Count) return false;
                        for (int i = 0; i < x.Count; i++) if (!SameToken(x[i], y[i])) return false;
                        return true;
                    }
                case JValue x when x.Type == JTokenType.Float: return Equals(x.Value, ((JValue)b).Value);
                default: return JToken.DeepEquals(a, b);
            }
        }
        // Stops the practice after a failed calculation: the session store goes back to its copy from before the command, cosmetic
        // intent (pick sheet, selection, sheets, card detail, turn summary, action display) is dropped and one diagnostic is logged. No retry, refund,
        // auto-advance, auto-save or server fetch; the recovery screen's action is the only way on.
        void Quarantine(string reason, Exception error, object? kept, bool render)
        {
            recovery = reason;
            if (kept is Dictionary<string, object?> valid) store = valid;
            picker = null; prepared = null; cardInfo = null; sheet = Sheet.None; deckPicks.Clear(); selectedChanges.Clear();
            selectedCard = null; selectedMode = null; target = null; CloseTurnSummary(); CloseActionPresentation();
            LastError = reason; notice = ""; noticeUntil = 0;
            Debug.LogError("[MemoryForge] Practice expedition quarantined after a failed local Run calculation (" + reason + "); "
                + "the Run may be partly changed, so it will be discarded unsaved. Session memory version " + SessionMemoryVersion + " is kept.\n" + error);
            if (render) Render();
        }
        /// <summary>The recovery screen's single action: drops the failed local practice Run without saving it (no Core abandon, memory
        /// commit or close; nothing of it is shown or kept) and departs again from the last valid session store, whose memory, version
        /// and receipts stay, as does the departure numbering. If that departure is refused or throws, the failed Run, the quarantine,
        /// the store and the numbering are all restored and nothing escapes; only a successful departure leaves recovery.</summary>
        public bool DiscardFailedPractice()
        {
            if (recovery == null) return false;
            // Without the loaded content or the last valid store, Depart would fall back to StartPractice and erase the session memory.
            if (data == null || store == null)
            {
                LastError = RecoveryCode; notice = "기억 저장소를 확인할 수 없어 새로 출발하지 않았어요. 저장된 기억은 지우지 않았어요."; noticeUntil = 0;
                Render(); return false;
            }
            var failed = run; string reason = recovery; string? closed = closedKind; var kept = store; int counted = departures;
            run = null; recovery = null; closedKind = null; LastError = null; notice = ""; noticeUntil = 0;
            Exception? fault = null; bool ok;
            try { ok = Depart(); }
            catch (Exception error) { ok = false; fault = error; }
            if (ok && run != null) return true;
            run = failed; recovery = reason; closedKind = closed; store = kept; departures = counted;
            if (fault != null)
            {
                LastError = fault.GetType().Name; notice = "새 탐사를 시작하지 못했어요. 이 탐사는 계속 멈춰 있고 저장된 기억은 그대로예요."; noticeUntil = 0;
                Debug.LogError("[MemoryForge] Practice recovery could not start a new departure (" + fault.GetType().Name + "); the quarantine and session memory version "
                    + SessionMemoryVersion + " are kept.\n" + fault);
            }
            Render(); return false;
        }

        // ───────── turn summary and battle log (§14.3/§16.4: text of Core's committed events; display only) ─────────
        // The turn barrier: while the summary is open every public command and selection is refused before it reaches the Run. Only
        // the summary's own skip/continue and display-only reads stay available. A refusal changes no view state, so nothing is redrawn.
        // In recovery the same gate refuses everything; only DiscardFailedPractice remains.
        // An open pick sheet is one prepared, unsent play, so every other command and selection is refused as well, after recovery and
        // the summary (their codes come first). Only the sheet's own pick, auto and cancel pass, through Barred alone; its final send
        // closes the sheet before playing, so this gate never refuses that one play.
        // A player-action display refuses every command after those; the next card/mode/target selection and its confirm pass it through
        // Locked unless the battle ended with that action. A waiting unsent intent refuses everything after those (its code comes last):
        // every command until its dispatch (which takes it out of the slot first, so this gate never refuses that one play), and every
        // selection or second confirm until it is cancelled.
        bool Blocked()
        {
            if (Held()) return true;
            if (actionRows != null) { LastError = ActionShowing; return true; }
            return Waiting();
        }
        bool Locked()
        {
            if (Held()) return true;
            if (actionRows != null && actionTerminal) { LastError = ActionShowing; return true; }
            return Waiting();
        }
        bool Held()
        {
            if (Barred()) return true;
            if (picker == null) return false;
            LastError = PickSheetOpen; return true;
        }
        bool Waiting()
        {
            if (prepared == null) return false;
            LastError = IntentPending; return true;
        }
        bool Barred()
        {
            if (recovery != null) { LastError = RecoveryCode; return true; }
            if (turnSummary == null) return false;
            LastError = TurnSummaryOpen; return true;
        }
        /// <summary>Shows every remaining row of the open turn summary at once. Display only: the Run is never called.</summary>
        public bool SkipTurnSummary()
        {
            if (turnSummary == null) return false;
            summaryShown = turnSummary.Count; summaryRevealAt = -1; RefreshTurnSummary(); return true;
        }
        /// <summary>Closes the open turn summary so the next input is accepted. Display only: the Run is never called.</summary>
        public bool ContinueTurnSummary()
        {
            if (turnSummary == null) return false;
            CloseTurnSummary(); Render(); return true;
        }
        void OpenTurnSummary(IReadOnlyList<BattleDigestEntry> rows)
        {
            turnSummary = rows; summaryShown = 0; summaryRevealAt = -1; AdvanceTurnSummary(Time.realtimeSinceStartup);
        }
        void CloseTurnSummary()
        {
            turnSummary = null; summaryShown = 0; summaryRevealAt = -1;
            if (LastError == TurnSummaryOpen) LastError = null;
        }
        // §14.3 예약 해결: each actual time-reservation resolution row appears 0.3 s after the previous reveal; every other row at once.
        // Labels change in place (no Render), so a held skip/continue keeps its button, canvas and EventSystem.
        void AdvanceTurnSummary(float now)
        {
            var rows = turnSummary;
            if (rows == null) return;
            int shown = summaryShown;
            while (summaryShown < rows.Count)
            {
                if (rows[summaryShown].IsReservedResolution)
                {
                    if (summaryRevealAt < 0) summaryRevealAt = now + ReservedRevealSeconds;
                    if (now < summaryRevealAt) break;
                    summaryRevealAt = -1;
                }
                summaryShown++;
            }
            if (summaryShown != shown) RefreshTurnSummary();
        }
        void RefreshTurnSummary()
        {
            var rows = turnSummary;
            if (rows == null || summaryText == null || summaryContent == null) return;
            summaryText.text = string.Join("\n", rows.Take(summaryShown).Select(row => Decoration(row) ? row.Text : "· " + row.Text));
            float height = Mathf.Ceil(summaryText.preferredHeight) + 6;
            summaryText.rectTransform.sizeDelta = new Vector2(1184, height);
            summaryContent.sizeDelta = new Vector2(1216, Math.Max(600, height + 24));
            if (summaryScroll != null) summaryScroll.vertical = height + 24 > 600;
            if (summaryStatus != null)
                summaryStatus.text = summaryShown < rows.Count ? "예약 해결을 차례로 보여 주는 중이에요 (" + summaryShown + "/" + rows.Count + ") · '남은 결과 모두 보기'로 바로 볼 수 있어요"
                    : "모두 표시했어요. '계속'을 누르면 다음 행동을 할 수 있어요.";
        }
        // ───────── player-action display (§14.2/§14.3: text of the committed play/ultimate's own events; display only) ─────────
        /// <summary>Ends the open player-action display at once so the next command is accepted. Display only: the Run is never called.
        /// False (and nothing happens) without an open display.</summary>
        public bool SkipActionPresentation()
        {
            if (actionRows == null) return false;
            CloseActionPresentation(); RefreshActionPresentation(); return true;
        }
        /// <summary>Chooses the pace of player-action displays (this one's remaining rows and later ones). Display only: never
        /// Time.timeScale, the Run or a reservation's timing. False when the pace is unknown or already chosen.</summary>
        public bool SetActionPace(ActionPacing pace)
        {
            if (!Enum.IsDefined(typeof(ActionPacing), pace) || pace == actionPace) return false;
            actionPace = pace;
            if (actionRows != null) AdvanceActionPresentation(Time.realtimeSinceStartup);
            RefreshActionPresentation(); return true;
        }
        /// <summary>The strip/sheet text of one display row: a short kind label (사용·예약·해결·메아리·유물·반격·필살기) and the row's own text.</summary>
        public static string ActionRowText(BattleDigestEntry row) => ActionTag(row) + row.Text;
        static string ActionTag(BattleDigestEntry row)
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
        // The committed command's event rows and its enemy results; the summary and previous-intent rows belong to the turn summary.
        // The first row shows at once (reduced motion: every row).
        void OpenActionPresentation(IReadOnlyList<BattleDigestEntry> rows, bool ultimate, bool terminal)
        {
            var shown = rows.Where(r => r.Kind != BattleDigestKind.Summary && r.Kind != BattleDigestKind.PreviousIntent).ToArray();
            if (shown.Length == 0) return;
            actionRows = Array.AsReadOnly(shown); actionShown = 1; actionNextAt = -1; actionUntil = -1; actionTerminal = terminal;
            actionTitle = ultimate ? "필살기 결과" : shown.Any(r => r.Kind == BattleDigestKind.ReservationPlayed) ? "예약 결과" : "카드 사용 결과";
            AdvanceActionPresentation(Time.realtimeSinceStartup);
        }
        void CloseActionPresentation()
        {
            actionRows = null; actionShown = 0; actionNextAt = -1; actionUntil = -1; actionTerminal = false;
            if (LastError == ActionShowing) LastError = null;
        }
        // Focus loss or pause: the display of an already committed action is dropped (it is in the battle log); the view is redrawn
        // from the actual Run on resume. Nothing is replayed.
        void DropActionPresentation()
        {
            if (actionRows == null) return;
            CloseActionPresentation(); actionDropped = true; RefreshActionPresentation();
        }
        float PaceScale => actionPace == ActionPacing.Double ? 0.5f : 1f;
        // One row per step on unscaled real time; when every row is shown the display holds, then closes. Labels and the gated controls
        // change in place (no Render), so a held next-card press keeps its button, canvas and EventSystem.
        void AdvanceActionPresentation(float now)
        {
            var rows = actionRows;
            if (rows == null) return;
            int shown = actionShown;
            if (actionPace == ActionPacing.Reduced) { actionShown = rows.Count; actionNextAt = -1; }
            while (actionShown < rows.Count)
            {
                if (actionNextAt < 0) actionNextAt = now + (rows[actionShown].IsReservedResolution ? ReservedRevealSeconds : ActionRowSeconds) * PaceScale;
                if (now < actionNextAt) break;
                actionNextAt = -1; actionShown++;
            }
            if (actionShown >= rows.Count)
            {
                if (actionUntil < 0) actionUntil = now + ActionHoldSeconds * PaceScale;
                if (now >= actionUntil) { CloseActionPresentation(); RefreshActionPresentation(); return; }
            }
            if (actionShown != shown) RefreshActionPresentation();
        }
        void RefreshActionPresentation()
        {
            var rows = actionRows; bool open = rows != null;
            foreach (var (control, allowed) in actionGated) if (control != null) control.interactable = allowed && !open && prepared == null;
            if (actionSkip != null) actionSkip.interactable = open;
            if (confirmCaption != null) confirmCaption.text = ConfirmCaption(open);
            foreach (var (back, caption, pace) in paceButtons)
            {
                if (back == null || caption == null) continue;
                bool chosen = pace == actionPace;
                back.color = chosen ? Memory : Line; caption.color = chosen ? Background : Ink;
                caption.text = (chosen ? "● " : "") + PaceName(pace);
            }
            if (actionOverlay != null && !open) actionOverlay.SetActive(false);
            if (actionStatus != null)
                actionStatus.text = rows == null ? "행동 결과" : actionTitle + " " + actionShown + "/" + rows.Count;
            if (actionText != null)
            {
                if (rows == null) { actionText.text = "카드를 쓰면 확정된 결과를 여기서 차례로 보여 줘요. 그동안 다음 카드 1장을 사용 대기로 둘 수 있어요."; actionText.color = Muted; }
                else
                {
                    var visible = rows.Take(actionShown); if (!actionAll) visible = visible.Skip(Math.Max(0, actionShown - 2));
                    actionText.text = string.Join("\n", visible.Select(ActionRowText)); actionText.color = Ink;
                }
            }
        }
        static string PaceName(ActionPacing pace) => pace == ActionPacing.Double ? "2배속" : pace == ActionPacing.Reduced ? "동작 줄이기" : "보통";
        // A ready plain confirm during a display prepares the one unsent intent; otherwise it plays.
        static string ConfirmCaption(bool showing) => showing ? "사용 대기" : "사용 확정";
        // A command control drawn while a display may be open: available only when its own state allows, no display is open and no
        // unsent intent waits for its dispatch.
        T Gate<T>(T control, bool allowed) where T : Selectable
        {
            control.interactable = allowed && actionRows == null && prepared == null; actionGated.Add((control, allowed)); return control;
        }
        void PaceButton(Transform parent, ActionPacing pace, float x, float y, float width, float height, int size)
        {
            var button = Button(parent, "Pace:" + pace, "", x, y, width, height, () => SetActionPace(pace), Line, size);
            paceButtons.Add((button.GetComponent<Image>(), Label(button.transform, "", 6, 4, width - 12, height - 8, size, Ink, FontStyle.Normal, TextAnchor.MiddleCenter), pace));
        }

        // Summary, previous-intent and final-state rows frame a summary; only event rows are battle log lines.
        static bool Decoration(BattleDigestEntry row) => row.Kind == BattleDigestKind.Summary || row.Kind == BattleDigestKind.PreviousIntent || row.Kind == BattleDigestKind.EnemyState;
        void AppendBattleLog(IReadOnlyList<BattleDigestEntry> rows)
        {
            foreach (var row in rows) if (!Decoration(row)) battleLog.Add(row.Text);
            if (battleLog.Count > BattleLogLimit) battleLog.RemoveRange(0, battleLog.Count - BattleLogLimit);
        }
        // The public part of a live battle: turn/phase/HP/block/AP, each enemy with its shown "이번" intent text, the time reservations and
        // the names of hand and reservation cards. Draw order, RNG and other private card mappings are not read.
        BattleDigestSnapshot PublicBattle(BattleState b, bool intents)
        {
            var rules = data!.Memory.Battle.Rules;
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string uid in b.Zones.Hand.Concat(b.Reservations.Select(r => r.Uid))) names[uid] = BattleCardName(uid);
            return new BattleDigestSnapshot
            {
                Turn = b.Turn, Phase = b.Phase, Hp = b.Hp, Block = b.Block, Ap = b.Ap, CardNames = names,
                Enemies = b.Enemies.Select(e => new BattleDigestEnemy { Uid = e.Uid, Name = e.Name, Hp = e.Hp, Block = e.Block, Alive = e.Alive,
                    IntentText = intents && e.Alive ? MemoryForgeBattleClient.IntentText(e.Intent, e.Status, b.DifficultyDamage, rules.WeakBps, true) : null }).ToList(),
                Reservations = b.Reservations.Select(r => new BattleDigestReservation { Uid = r.Uid, Name = BattleCardName(r.Uid), Due = r.Due, Kind = r.Kind }).ToList()
            };
        }
        // Card uids named by the events: a play/resolved/echoCopy event names its own card definition (static catalog); any other uid only
        // when it now lies in a public zone (hand, discard, exhaust, reservations) of the live battle. Others stay unnamed ("카드").
        IReadOnlyList<BattleDigestEntry> Digest(BattleDigestSnapshot? before, List<BattleEvent> events, BattleDigestSnapshot after, BattleState? live)
        {
            var names = after.CardNames ??= new Dictionary<string, string>(StringComparer.Ordinal);
            var open = live == null ? null : new HashSet<string>(live.Zones.Hand.Concat(live.Zones.Discard).Concat(live.Zones.Exhaust).Concat(live.Reservations.Select(r => r.Uid)), StringComparer.Ordinal);
            foreach (var e in events)
            {
                if (string.IsNullOrEmpty(e.Uid) || names.ContainsKey(e.Uid!)) continue;
                string uid = e.Uid!;
                if ((e.E == "play" || e.E == "resolved" || e.E == "echoCopy") && e.Id != null && cardDefs.TryGetValue(e.Id, out var def)) names[uid] = def.Name;
                else if (open != null && open.Contains(uid)) names[uid] = BattleCardName(uid);
            }
            return BattleTurnDigest.Build(before, events, after);
        }

        // Display-only projection of prototype/src/ui.js choiceRequests: the immediate plays whose card picks the player makes.
        // Core still validates and resolves them; picks left unmade keep Core's own choice, which the overlay states.
        public static List<(string Label, string From, int Count, bool GrowthOnly)> ChoiceRequests(BattleCardDef card, string mode, BattleData? content = null)
        {
            var requests = new List<(string Label, string From, int Count, bool GrowthOnly)>();
            if (mode != "now") return requests;
            foreach (var op in card.Effects)
            {
                int count = Math.Max(1, op.N ?? 1);
                switch (op.Op)
                {
                    case "exhaustPick": requests.Add((op.From == "discard" ? "버린 카드에서 소멸할 카드" : "손에서 소멸할 카드", op.From == "discard" ? "discard" : "hand", count, false)); break;
                    case "discard": requests.Add(("버릴 카드", "hand", count, false)); break;
                    case "tune" when op.Pick == "one":
                        string part = content?.Parts.FirstOrDefault(p => p.Id == op.Part)?.Name ?? "부품";
                        requests.Add(("「" + part + "」을 붙일 카드", "hand", 1, false)); break;
                    case "costdown" when op.Pick == "one": requests.Add(("비용을 낮출 카드", "hand", 1, false)); break;
                    case "recall": requests.Add(("손으로 가져올 카드", "discard", 1, false)); break;
                    case "growPick": requests.Add(("성장시킬 카드", "hand", 1, true)); break;
                    case "copyPick": requests.Add(("사본을 만들 성장 카드", "hand", 1, true)); break;
                    case "pull": requests.Add(("앞당길 예약", "res", 1, false)); break;
                }
            }
            return requests;
        }
        /// <summary>The cards a pick sheet offers for one request, in zone order (prototype/src/ui.js pickerOptions): the hand without the
        /// playing card, the discard pile or the time reservations; only growth-card definitions when the request says so; none already
        /// picked. PickSheet draws exactly this list and PickCard accepts nothing else. Core still validates and resolves the picks.</summary>
        public static List<string> PickCandidates(BattleState b, string source, (string Label, string From, int Count, bool GrowthOnly) request,
            IEnumerable<string> picked, IReadOnlyDictionary<string, BattleCardDef> definitions)
        {
            IEnumerable<string> options = request.From == "hand" ? b.Zones.Hand.Where(u => u != source) : request.From == "discard" ? b.Zones.Discard : b.Reservations.Select(r => r.Uid);
            if (request.GrowthOnly) options = options.Where(u => b.Cards.TryGetValue(u, out var c) && definitions.TryGetValue(c.Id, out var d) && d.Growth == true);
            var used = new HashSet<string>(picked, StringComparer.Ordinal);
            return options.Where(u => !used.Contains(u)).ToList();
        }
        List<string> PickOptions(BattleState b, BattlePick pick) => PickCandidates(b, pick.Uid, pick.Requests[pick.Index], pick.Picks, cardDefs);
        // The sheet's opening state still holds: the same Run and battle node, the battle version and target it was opened at, the
        // player phase and its card in hand. Any committed change moves the battle version, so picks made against an older state never
        // send; the target is compared, never replaced.
        bool Fresh(BattlePick pick, RunState s) => ReferenceEquals(run, pick.Run) && s.Phase == "BATTLE" && s.Battle != null && s.Battle.Phase == "PLAYER"
            && s.Position == pick.Node && s.Battle.V == pick.Version && target == pick.Target && s.Battle.Zones.Hand.Contains(pick.Uid);

        // ───────── view ─────────
        void Render()
        {
            if (run == null || data == null || font == null) return;
            RemoveView();
            var canvasObject = new GameObject(OwnedCanvas, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            if (!Application.isPlaying) canvasObject.hideFlags = HideFlags.DontSaveInEditor;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
            canvasRoot = canvasObject.GetComponent<RectTransform>();
            frame = Box(canvasRoot, "ExpeditionFrame", 0, 0, 1600, 900, Background);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f); frame.pivot = new Vector2(0.5f, 0.5f); frame.anchoredPosition = Vector2.zero;
            if (recovery != null) RecoveryView(); // before the header and every normal view: the failed Run may not be renderable
            else
            {
                var s = run.State;
                Header(s);
                if (Finished(s)) ResultView(s);
                else
                    switch (s.Phase)
                    {
                        case "BATTLE": BattleView(s); break;
                        case "VICTORY": case "DEFEAT": SaveView(s); break;
                        default:
                            RunStatus(s);
                            if (s.Phase == "MAP") MapView(s);
                            else if (s.Phase == "REWARD") RewardView(s);
                            else if (s.Phase == "REST") RestView(s);
                            else if (s.Phase == "FORGE") ForgeView(s);
                            else if (s.Phase == "SHOP") ShopView(s);
                            else if (s.Phase == "EVENT") EventView(s);
                            else if (s.Phase == "ARCHIVE") ArchiveView(s);
                            break;
                    }
                if (picker != null && Fresh(picker, s)) PickSheet(s.Battle!); else picker = null; // a stale sheet is never drawn
                if (sheet == Sheet.Deck) DeckSheet(s);
                if (sheet == Sheet.Log) LogSheet(s);
                if (sheet == Sheet.Abandon) AbandonSheet();
                if (sheet == Sheet.Assign && SaveOpen(s)) AssignSheet(s);
                if (sheet == Sheet.DeckPick && !Finished(s)) DeckPickSheet(s);
                if (cardInfo != null) CardInfoSheet(cardInfo); // above the other sheets: it can be opened from the deck and deck-pick sheets
                if (actionRows != null && actionTerminal) ActionPresentationSheet(); // the battle's last action covers the next screen
                if (turnSummary != null) TurnSummarySheet(); // topmost: the turn barrier covers every other control
            }
            if (Application.isPlaying && EventSystem.current == null)
            { var events = new GameObject("ExpeditionInput", typeof(EventSystem), typeof(StandaloneInputModule)); events.transform.SetParent(canvasObject.transform, false); }
            Canvas.ForceUpdateCanvases(); FitView();
        }

        // The recovery screen reads nothing from the failed Run: only the session store's version, and one explicit action.
        void RecoveryView()
        {
            var panel = Box(frame!, "Recovery", 200, 160, 1200, 560, Panel);
            Label(panel, "이 연습 탐사를 계속할 수 없어요", 40, 36, 1120, 56, 34, Danger, FontStyle.Bold);
            Label(panel, "계산 중 문제가 생겨 탐사를 멈췄어요. 이 탐사의 진행과 보상은 저장하지 않고 버려요.", 40, 120, 1120, 40, 22, Ink);
            Label(panel, store == null ? "저장된 기억을 확인할 수 없어 새로 출발할 수 없어요." : "마지막으로 저장한 기억 덱(기억 버전 " + SessionMemoryVersion + ")은 그대로이고, 새 탐사는 그 덱으로 시작해요.", 40, 170, 1120, 40, 22, Ink);
            Label(panel, "연습 기억은 이 앱 세션에만 있어요. 앱을 닫으면 사라져요.", 40, 220, 1120, 34, 19, Muted);
            if (notice.Length != 0) Label(panel, notice, 40, 290, 1120, 60, 20, Danger); // a failed new departure keeps the quarantine
            Button(panel, "RecoveryDiscard", "이 탐사를 버리고 새로 출발", 40, 440, 560, 80, () => DiscardFailedPractice(), Brass, 26);
        }

        void Header(RunState s)
        {
            Label(frame!, "기억 공방", 32, 18, 230, 48, 36, Brass, FontStyle.Bold);
            Label(frame!, "탐사 연습 · " + string.Join("·", ReferenceParty.Select(CharacterName)) + " 기준 파티", 272, 14, 640, 32, 22, Ink);
            Label(frame!, Location(s) + "  ·  " + PhaseName(s), 272, 46, 640, 28, 19, Muted);
            Button(frame!, "DeckButton", "덱 " + s.Deck.Count + "장" + (s.PartsFound.Count != 0 ? " · 부품 " + s.PartsFound.Count : ""), 930, 20, 240, 52, () => Open(Sheet.Deck), Line, 22);
            Button(frame!, "LogButton", "기록", 1186, 20, 120, 52, () => Open(Sheet.Log), Line, 22);
            if (!Finished(s) && s.Phase != "VICTORY" && s.Phase != "DEFEAT") Button(frame!, "AbandonButton", "탐사 포기", 1322, 20, 246, 52, () => Open(Sheet.Abandon), Line, 22);
        }
        void RunStatus(RunState s)
        {
            var panel = Box(frame!, "RunStatus", 32, 88, 1536, 70, Panel);
            Label(panel, "체력 " + s.Hp + " / " + s.MaxHp, 20, 8, 300, 32, 25, Ink, FontStyle.Bold);
            Bar(panel, 20, 48, 280, 10, s.Hp, s.MaxHp, Memory);
            Label(panel, "동전 " + s.Coins + "   ·   유물 " + s.Relics.Count + "개   ·   부품 " + s.PartsFound.Count + "개", 340, 6, 720, 30, 22, Brass);
            Label(panel, s.Relics.Count == 0 ? "보유 유물 없음" : string.Join(", ", s.Relics.Select(RelicName)), 340, 38, 720, 26, 17, Muted);
            Label(panel, PracticeNote, 1080, 8, 440, 54, 17, Muted);
        }
        void NoticeLine(string hint)
        {
            string text = notice.Length != 0 ? notice : hint;
            TrackNotice(Label(frame!, text, 32, 830, 1230, 46, 21, LastError != null ? Danger : notice.Length != 0 ? Memory : Muted), hint, Muted);
        }

        void MapView(RunState s)
        {
            var map = s.Map!; var open = new HashSet<string>(run!.Available(), StringComparer.Ordinal);
            var panel = Box(frame!, "Map", 32, 170, 1536, 650, Panel);
            var columns = map.Floors.Select(row => (IReadOnlyList<RunNode>)row).ToList(); columns.Add(new[] { map.Boss });
            float column = 1536f / columns.Count, top = 58, height = 570, nodeWidth = Math.Min(160, column - 32), nodeHeight = 66;
            var centers = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            for (int f = 0; f < columns.Count; f++)
            {
                Label(panel, f == columns.Count - 1 ? "보스" : (f + 1) + "층", f * column, 14, column, 32, 21, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
                for (int i = 0; i < columns[f].Count; i++) centers[columns[f][i].Id] = new Vector2(f * column + column / 2, top + (i + 0.5f) * height / columns[f].Count);
            }
            var nodes = columns.SelectMany(c => c).ToList();
            foreach (var node in nodes)
                foreach (string next in node.Next ?? new List<string>())
                    if (centers.TryGetValue(next, out var to))
                        Connector(panel, centers[node.Id] + new Vector2(nodeWidth / 2, 0), to - new Vector2(nodeWidth / 2, 0), node.Id == s.Position && open.Contains(next) ? Brass : Line);
            foreach (var node in nodes)
            {
                bool here = node.Id == s.Position, reachable = open.Contains(node.Id); var c = centers[node.Id];
                var button = Button(panel, "Node:" + node.Id, NodeName(node.Type) + (here ? "\n현재 위치" : reachable ? "\n갈 수 있음" : ""),
                    c.x - nodeWidth / 2, c.y - nodeHeight / 2, nodeWidth, nodeHeight, () => EnterNode(node.Id), here ? Memory : reachable ? Brass : Background, 21);
                button.interactable = reachable;
            }
            NoticeLine("밝게 표시된 칸만 갈 수 있어요. 칸을 누르면 바로 들어가요.");
        }

        // ───────── battle (same layout and projections as the battle practice) ─────────
        void BattleView(RunState s)
        {
            var b = s.Battle!; var rules = data!.Memory.Battle.Rules;
            if (!b.Enemies.Any(e => e.Uid == target && e.Alive)) target = null;
            if (target == null && b.Enemies.Count(e => e.Alive) == 1) target = b.Enemies.First(e => e.Alive).Uid;
            if (!b.Zones.Hand.Contains(selectedCard ?? "")) { selectedCard = b.Zones.Hand.FirstOrDefault(); selectedMode = null; }
            var top = Box(frame!, "PartyStatus", 32, 88, 1536, 90, Panel);
            Label(top, "파티 체력  " + b.Hp + " / " + b.MaxHp, 20, 10, 420, 38, 30, Ink, FontStyle.Bold);
            Bar(top, 20, 58, 416, 12, b.Hp, b.MaxHp, Memory);
            Label(top, b.Turn + "턴  ·  방어 " + b.Block + "  ·  반격 " + b.Counter + "  ·  부채 " + b.Debts.Sum(d => d.N) + "  ·  잔향 " + b.Echo + "/" + b.EchoMax, 472, 14, 720, 34, 23, Ink);
            Label(top, "뽑을 " + b.Zones.Draw.Count + "  /  버림 " + b.Zones.Discard.Count + "  /  소멸 " + b.Zones.Exhaust.Count + "  ·  동전 " + s.Coins, 472, 51, 420, 28, 21, Muted);
            string party = MemoryForgeBattleClient.PartyStatusText(b);
            if (party.Length != 0) Label(top, party, 900, 51, 320, 28, 21, Brass);
            Label(top, "행동력  " + b.Ap, 1232, 17, 280, 52, 40, Brass, FontStyle.Bold);

            var actors = Box(frame!, "Party", 32, 194, 350, 256, Panel);
            for (int i = 0; i < b.Actors.Count; i++)
            {
                var actor = b.Actors[i]; float y = 14 + i * 80; bool ready = actor.Gauge >= rules.GaugeMax;
                Label(actors, actor.Name, 18, y, 155, 38, 30, Ink, FontStyle.Bold);
                Label(actors, Attribute(actor.Attr) + "  ·  " + actor.Gauge + "/" + rules.GaugeMax, 18, y + 40, 170, 24, 21, Muted);
                Bar(actors, 188, y + 50, 142, 9, actor.Gauge, rules.GaugeMax, Brass);
                Gate(Button(actors, "Ult:" + actor.Id, ready ? "필살기" : "충전 중", 188, y, 144, 44, () => UseUltimate(actor.Id), ready ? Brass : Line), ready && b.Phase == "PLAYER");
            }
            float width = (1154 - 16 * (b.Enemies.Count - 1)) / Math.Max(1, b.Enemies.Count);
            for (int i = 0; i < b.Enemies.Count; i++)
            {
                var enemy = b.Enemies[i];
                var button = Button(frame!, "Enemy:" + enemy.Uid, "", 414 + i * (width + 16), 194, width, 256, () => SelectEnemy(enemy.Uid), Panel);
                button.interactable = enemy.Alive; var p = button.GetComponent<RectTransform>();
                if (target == enemy.Uid && enemy.Alive) { var outline = p.gameObject.AddComponent<Outline>(); outline.effectColor = Memory; outline.effectDistance = new Vector2(2, 2); }
                Label(p, enemy.Alive ? enemy.Name : enemy.Name + " · 처치", 18, 14, width - 36, 40, 29, enemy.Alive ? Ink : Muted, FontStyle.Bold);
                Label(p, "체력 " + enemy.Hp + " / " + enemy.MaxHp + (enemy.Block > 0 ? "  방어 " + enemy.Block : ""), 18, 60, width - 36, 30, 23, Ink);
                Bar(p, 18, 96, width - 36, 12, enemy.Hp, enemy.MaxHp, Danger);
                Label(p, "균열 " + enemy.Brk + "/" + enemy.BrkMax + "  ·  약점 " + string.Join("·", enemy.Weak.Select(Attribute)), 18, 116, width - 36, 28, 20, Memory);
                string status = MemoryForgeBattleClient.StatusText(enemy, rules);
                if (enemy.Alive && status.Length != 0) Label(p, status, 18, 146, width - 36, 28, 20, Ink);
                Label(p, "이번  " + MemoryForgeBattleClient.IntentText(enemy.Intent, enemy.Status, b.DifficultyDamage, rules.WeakBps, true)
                    + "\n다음  " + MemoryForgeBattleClient.IntentText(enemy.Next, enemy.Status, b.DifficultyDamage, rules.WeakBps, false), 18, 178, width - 36, 64, 22, enemy.Alive ? Brass : Muted);
            }

            // The timeline and action panels are 22 px and 4 px shorter than before to make room for the action strip between them.
            var timeline = Box(frame!, "Timeline", 32, 458, 1536, 78, Panel);
            Label(timeline, "기억 시간표", 18, 8, 180, 32, 24, Memory, FontStyle.Bold);
            var cells = MemoryForgeBattleClient.TimelineCells(b, rules);
            float slot = Math.Min(220, 1308f / Math.Max(1, cells.Count));
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                string text = (cell.Group == "next" ? "다음" : "다다음") + "  " + (cell.Kind == "card" ? run!.BattleEffectiveCard(cell.Uid!).Name
                    : cell.Kind == "empty" ? "빈 칸" : cell.Kind == "sealed" ? "봉인" : "-");
                var box = Box(timeline, "ReservationSlot", 204 + i * slot, 9, slot - 12, 60, cell.Kind == "sealed" ? Line : Background);
                Label(box, text, 10, 4, slot - 32, 52, 22, cell.Kind == "card" ? Memory : cell.Kind == "sealed" ? Danger : Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            }

            ActionStrip();
            var actions = Box(frame!, "CardActions", 32, 596, 1536, 94, Panel);
            string hint = "카드를 고르고 방식을 정한 뒤 사용을 확정하세요. 고르는 동안에는 아무것도 쓰지 않아요."; var hintColor = Muted;
            var intent = prepared;
            if (intent != null)
            {
                // The waiting intent's own request: its frozen card, mode, target and picks (unmade picks are Core's automatic choice).
                var forecast = run!.BattleForecast(intent.Uid, intent.Mode, intent.Target, intent.Picks);
                hint = ForecastLine(forecast, forecast.Error == null && intent.Picks.Count < intent.Requests.Sum(r => r.Count)); hintColor = ForecastColor(forecast);
            }
            else if (selectedCard != null && selectedMode != null && b.Phase == "PLAYER")
            {
                // The exact request ConfirmCard leads to; a pick card's unmade picks are Core's automatic choice. Recomputed on Render only.
                var forecast = run!.BattleForecast(selectedCard, selectedMode, target);
                bool auto = forecast.Error == null && ChoiceRequests(run.BattleEffectiveCard(selectedCard), selectedMode, data!.Memory.Battle).Count != 0;
                hint = ForecastLine(forecast, auto); hintColor = ForecastColor(forecast);
            }
            TrackNotice(Label(actions, notice.Length != 0 ? notice : hint, 18, 6, 1498, 30, 23, notice.Length != 0 ? (LastError == null ? Muted : Danger) : hintColor), hint, hintColor);
            if (intent != null) PreparedControls(actions, b, intent);
            else if (selectedCard != null && b.Phase == "PLAYER")
            {
                string uid = selectedCard; var def = run!.BattleEffectiveCard(uid);
                Label(actions, def.Name + " → " + (b.Enemies.FirstOrDefault(e => e.Uid == target)?.Name ?? (run.BattleNeedsTarget(uid) == null ? "파티" : "적 선택")), 18, 44, 360, 42, 25, Ink, FontStyle.Bold);
                Button(actions, "CardInfoButton", "카드 설명", 388, 38, 148, 52, () => ShowHandCardInfo(uid), Line, 21);
                int i = 0;
                foreach (string mode in run.BattleAllowedModes(uid))
                {
                    string? error = run.BattleCheck(uid, mode, target);
                    string caption = ModeName(mode) + " · 비용 " + run.BattleCostOf(uid, mode) + "\n" + (error == null ? ModeTiming(mode) : ErrorText(error));
                    Button(actions, "Mode:" + mode, caption, 548 + i++ * 268, 38, 252, 52, () => SelectMode(mode), selectedMode == mode ? (mode == "now" ? Brass : Memory) : Line, 19)
                        .interactable = error == null || error == "BAD_TARGET" || error == "FIXED_MARK_REQUIRED";
                }
                string? reason = selectedMode == null ? "MODE" : run.BattleCheck(uid, selectedMode, target);
                bool plain = reason == null && ChoiceRequests(def, selectedMode!, data!.Memory.Battle).Count == 0;
                string confirm = reason == null ? (plain ? ConfirmCaption(actionRows != null) : "카드 고르기")
                    : reason == "MODE" ? "방식 선택" : reason == "BAD_TARGET" || reason == "FIXED_MARK_REQUIRED" ? "대상 선택" : ErrorText(reason);
                // Not gated by the display: during one it confirms the single unsent intent (or opens its sheet) and spends nothing.
                var button = Button(actions, "ConfirmCard", confirm, 1360, 38, 158, 52, () => ConfirmCard(), reason == null ? Brass : Line, 22);
                button.interactable = reason == null;
                if (plain) confirmCaption = button.GetComponentInChildren<Text>();
            }

            var dock = Scroll(frame!, "Hand", 32, 696, 1308, 180, b.Zones.Hand.Count * 194, 180, Background);
            for (int i = 0; i < b.Zones.Hand.Count; i++)
            {
                string uid = b.Zones.Hand[i]; var def = run!.BattleEffectiveCard(uid); bool selected = uid == selectedCard;
                var modes = run.BattleAllowedModes(uid);
                var p = Button(dock, "Hand:" + uid, "", i * 194, selected ? 0 : 4, 180, 172, () => SelectCard(uid), selected ? Line : Panel).GetComponent<RectTransform>();
                Label(p, "행동력 " + run.BattleCostOf(uid, modes.FirstOrDefault() ?? "now"), 12, 10, 156, 28, 21, Brass);
                Label(p, def.Name + (b.Cards.TryGetValue(uid, out var instance) && instance.Plus ? "+" : ""), 12, 52, 156, 64, 27, Ink, FontStyle.Bold);
                Label(p, !modes.Contains("now") ? "예약 전용" : modes.Count > 1 ? "즉시 · 예약" : "즉시 사용", 12, 130, 156, 28, 20, Memory);
            }
            Gate(Button(frame!, "EndTurn", b.Phase == "PLAYER" ? "턴 종료\n" + b.Turn + "턴" : "전투 종료", 1360, 696, 208, 180, () => EndTurn(), Brass, 32), b.Phase == "PLAYER");
        }
        // §14.3 행동 연출 (provisional text, no final VFX/audio): the committed action's rows step in here, apart from the action line,
        // so the next card's forecast stays readable while they play. Pace and skip change only this strip.
        void ActionStrip()
        {
            var strip = Box(frame!, "ActionPresentation", 32, 544, 1536, 44, Panel);
            actionStatus = Label(strip, "", 14, 4, 226, 36, 18, Memory, FontStyle.Bold);
            actionText = Label(strip, "", 248, 1, 764, 42, 16, Ink);
            PaceButton(strip, ActionPacing.Normal, 1022, 4, 110, 36, 18);
            PaceButton(strip, ActionPacing.Double, 1138, 4, 110, 36, 18);
            PaceButton(strip, ActionPacing.Reduced, 1254, 4, 150, 36, 18);
            actionSkip = Button(strip, "SkipActionPresentation", "건너뛰기", 1410, 4, 112, 36, () => SkipActionPresentation(), Line, 18);
            actionAll = false; RefreshActionPresentation();
        }
        // The waiting intent in place of the selection controls: its frozen card and target, its card text, a 1/1 waiting label kept apart
        // from the timeline's time reservations (a next/after request takes its slot only when it is sent), and its cancel, which sends
        // nothing and so is never gated by the display. Cancel and card text trade places with the ordinary rects: cancel sits where the
        // ordinary card text is and card text where the ordinary confirm is, so a repeated tap after a confirm or a cancel (each redraws
        // synchronously) can only open the read-only card text, never cancel a just-confirmed intent or confirm a just-cancelled one.
        void PreparedControls(RectTransform actions, BattleState b, PreparedPlay intent)
        {
            string aim = b.Enemies.FirstOrDefault(e => e.Uid == intent.Target)?.Name ?? (intent.Target == null ? "파티" : "대상");
            Label(actions, BattleCardName(intent.Uid) + " → " + aim, 18, 44, 360, 42, 25, Ink, FontStyle.Bold);
            Button(actions, "CancelPreparedIntent", "대기 취소", 388, 38, 148, 52, () => CancelPreparedIntent(), Line, 22);
            Label(actions, PreparedText(intent), 548, 38, 796, 52, 19, Brass);
            if (b.Zones.Hand.Contains(intent.Uid)) Button(actions, "CardInfoButton", "카드 설명", 1360, 38, 158, 52, () => ShowHandCardInfo(intent.Uid), Line, 21);
        }
        string PreparedText(PreparedPlay intent)
        {
            int total = intent.Requests.Sum(r => r.Count);
            string picks = total == 0 ? "" : intent.Picks.Count == 0 ? " · 고를 카드는 자동 선택"
                : " · 고른 카드 " + string.Join("·", intent.Picks.Select(BattleCardName)) + (intent.Picks.Count < total ? " · 나머지 자동" : "");
            return "미전송 사용 대기 " + PreparedLimit + "/" + PreparedLimit + " · " + ModeName(intent.Mode) + (intent.Mode == "now" ? " 사용" : " 예약 요청 · 시간표 칸은 보낼 때 차지해요")
                + picks + "\n결과 표시가 끝나면 이 요청 하나만 보내요 · 바꾸려면 먼저 대기 취소";
        }
        // The last command of a battle: the next screen (reward or save) is already drawn beneath this full-frame sheet, whose controls sit
        // over that screen's status text rather than its buttons, so a repeated tap after the sheet closes reaches no reward or save.
        void ActionPresentationSheet()
        {
            var p = OverlayPanel("ActionPresentationSheet", 260, 90, 1080, 600);
            actionOverlay = p.parent.gameObject;
            actionStatus = Label(p, "", 24, 14, 326, 48, 22, Brass, FontStyle.Bold);
            PaceButton(p, ActionPacing.Normal, 360, 14, 130, 48, 20);
            PaceButton(p, ActionPacing.Double, 500, 14, 130, 48, 20);
            PaceButton(p, ActionPacing.Reduced, 640, 14, 170, 48, 20);
            actionSkip = Button(p, "SkipActionPresentation", "건너뛰기", 820, 14, 200, 48, () => SkipActionPresentation(), Line, 22);
            Label(p, "이 연습 세션에서 이미 확정된 결과예요. 표시가 끝나면 이 창이 닫히고 다음 화면을 쓸 수 있어요.", 24, 74, 1032, 28, 18, Muted);
            actionText = Label(p, "", 24, 112, 1032, 468, 20, Ink);
            actionAll = true; RefreshActionPresentation();
        }
        void PickSheet(BattleState b)
        {
            var pick = picker!; var request = pick.Requests[pick.Index];
            // PickCard plays at once when this click completes the picks, so each candidate then shows the forecast of its own picks.
            bool last = pick.Index == pick.Requests.Count - 1 && pick.Left == 1;
            var p = OverlayPanel("BattlePicks", 300, 150, 1000, 600);
            Label(p, request.Label + " 고르기 (" + (pick.Picks.Count + 1) + "/" + pick.Requests.Sum(r => r.Count) + ")", 24, 18, 952, 44, 30, Ink, FontStyle.Bold);
            // Opened during a player-action display, the final pick confirms the unsent intent until that display ends (it may end while
            // the sheet is open, so the text holds either way).
            Label(p, last ? (actionRows != null ? "마지막 선택이에요. 누르면 사용을 확정하고, 결과 표시 중이면 끝난 뒤 보내요. 아래 줄은 고를 때의 예상이에요."
                : "마지막 선택이에요. 카드를 누르면 바로 사용해요. 각 카드 아래 줄은 그 카드를 골랐을 때의 예상이에요.")
                : "고른 순서대로 적용돼요. 사용을 확정하기 전에는 행동력과 카드를 쓰지 않아요.", 24, 64, 952, 28, 20, Muted);
            var list = PickOptions(b, pick); // the same list PickCard accepts
            var content = Scroll(p, "PickList", 24, 104, 952, 336, 952, ((list.Count + 2) / 3) * 64, Panel);
            for (int i = 0; i < list.Count; i++)
            {
                string uid = list[i]; string zone = b.Zones.Hand.Contains(uid) ? "손패" : b.Zones.Discard.Contains(uid) ? "버림" : "예약";
                string caption = BattleCardName(uid) + " · " + zone;
                if (last) caption += "\n" + ForecastOutcome(run!.BattleForecast(pick.Uid, pick.Mode, target, pick.Picks.Concat(new[] { uid }).ToList()));
                Button(content, "Pick:" + uid, caption, i % 3 * 320, i / 3 * 64, 308, 54, () => PickCard(uid), Line, 21);
            }
            if (list.Count == 0) Label(p, "고를 카드가 없어요.", 24, 110, 952, 40, 22, Muted);
            // AutoPick sends exactly the picks made so far and Core chooses the rest: that request's forecast.
            var auto = run!.BattleForecast(pick.Uid, pick.Mode, target, pick.Picks);
            Label(p, "자동으로 고르고 사용하면 · " + ForecastText(auto), 24, 448, 952, 52, 20, ForecastColor(auto));
            Button(p, "PickAuto", "남은 대상은 자동으로 고르고 사용", 24, 510, 420, 62, () => AutoPick(), Brass, 21);
            Button(p, "PickCancel", "취소", 776, 510, 200, 62, CancelPick, Line, 22);
        }

        // ───────── rewards and node services ─────────
        void RewardView(RunState s)
        {
            var p = s.Pending; var evolutions = ListOf(Get(p, "evolutions")); var cards = ListOf(Get(p, "cards"));
            var relics = ListOf(Get(p, "relic")); string? part = Get(p, "part") as string;
            float height = 72 + evolutions.Count * 108 + (cards.Count != 0 ? 262 : 0) + (relics.Count != 0 ? 162 : 0) + (part != null ? 92 : 0);
            var body = Scroll(frame!, "Reward", 32, 170, 1536, 640, 1536, height, Background);
            var head = Box(body, "RewardCoins", 0, 0, 1536, 60, Panel);
            Label(head, "전투 보상  ·  동전 +" + Integer(Get(p, "coins")), 20, 10, 1000, 42, 30, Brass, FontStyle.Bold);
            float y = 72;
            foreach (var offer in evolutions)
            {
                string key = Text(Get(offer, "key")); var card = s.Deck.FirstOrDefault(c => c.Key == key);
                var panel = Box(body, "Evolution:" + key, 0, y, 1536, 96, Panel);
                Label(panel, CardName(card?.Id) + " · 성장 3 달성 — 진화를 고르세요. 진화는 이번 탐사 동안 유지되고, 저장할 때 기억에 남길 수 있어요.", 20, 8, 1496, 30, 20, Ink);
                var options = ListOf(Get(offer, "options")).Select(Text).ToList();
                for (int k = 0; k < options.Count; k++)
                {
                    string branch = options[k];
                    Button(panel, "Evolve:" + key + ":" + branch, CardName(branch), 20 + k * 380, 42, 270, 46, () => Evolve(key, branch), Memory, 22);
                    Button(panel, "EvolveInfo:" + key + ":" + branch, "설명", 298 + k * 380, 42, 82, 46, () => ShowCardInfo(branch), Line, 20);
                }
                y += 108;
            }
            if (cards.Count != 0)
            {
                var panel = Box(body, "RewardCards", 0, y, 1536, 250, Panel);
                Label(panel, "카드 1장을 덱에 추가 · 카드를 누르면 바로 덱에 들어가요. 먼저 설명을 볼 수 있어요.", 20, 8, 1120, 34, 22, Ink, FontStyle.Bold);
                for (int k = 0; k < cards.Count; k++)
                {
                    string id = Text(cards[k]);
                    CardButton(panel, "RewardCard:" + id, id, 20 + k * 380, 48, 360, 126, () => TakeCard(id));
                    Button(panel, "CardInfo:" + id, "카드 설명 보기", 20 + k * 380, 184, 360, 52, () => ShowCardInfo(id), Line, 20);
                }
                Button(panel, "SkipCard", "받지 않기 (동전 +15)", 1160, 48, 356, 60, () => SkipCard(), Line, 22);
                y += 262;
            }
            if (relics.Count != 0)
            {
                var panel = Box(body, "RewardRelics", 0, y, 1536, 150, Panel);
                Label(panel, "유물 1개 선택", 20, 8, 600, 34, 24, Ink, FontStyle.Bold);
                for (int k = 0; k < relics.Count; k++)
                {
                    string id = Text(relics[k]); var r = Button(panel, "Relic:" + id, "", 20 + k * 500, 48, 480, 92, () => TakeRelic(id), Line).GetComponent<RectTransform>();
                    Label(r, RelicName(id), 14, 6, 452, 32, 23, Ink, FontStyle.Bold);
                    Label(r, relicDefs.TryGetValue(id, out var def) ? def.Text : "", 14, 40, 452, 46, 17, Muted);
                }
                y += 162;
            }
            if (part != null)
            {
                var panel = Box(body, "RewardPart", 0, y, 1536, 80, Panel);
                Label(panel, "부품 발견: " + PartName(part) + " — " + (partDefs.TryGetValue(part, out var def) ? def.Text : ""), 20, 10, 1200, 60, 20, Ink);
                Button(panel, "TakePart", "가져가기", 1296, 12, 220, 56, () => TakePart(), Memory, 22);
            }
            bool boss = s.Position != null && run!.NodeById(s.Position)?.Type == "boss";
            Button(frame!, "Proceed", boss ? "다음으로" : "지도로", 1288, 822, 280, 56, () => Proceed(), Brass, 24);
            NoticeLine("받을 보상을 고른 뒤 다음으로 나아가세요. 고르지 않은 보상은 사라져요.");
        }
        void RestView(RunState s)
        {
            long uses = Integer(Get(s.Pending, "uses"));
            var panel = Box(frame!, "Rest", 32, 170, 1536, 320, Panel);
            Label(panel, "휴식", 20, 14, 400, 44, 32, Brass, FontStyle.Bold);
            Label(panel, "잠깐 쉬어 간다  ·  남은 이용 " + uses + "회", 20, 66, 1000, 36, 24, Ink);
            Button(panel, "RestHeal", "회복 (최대 체력의 " + (30 + (s.Relics.Contains("old_compass") ? 10 : 0)) + "%)", 20, 150, 440, 72, () => RestHeal(), Memory, 23).interactable = uses > 0;
            Button(panel, "RestUpgrade", "카드 강화(+)", 480, 150, 440, 72, () => OpenDeckPick(Purpose.Upgrade, "강화할 카드를 고르세요(수치 약 30% 상승)"), Line, 23).interactable = uses > 0;
            Button(panel, "Proceed", "떠나기", 1196, 150, 320, 72, () => Proceed(), Brass, 24);
            NoticeLine("회복이나 강화 중 남은 이용 횟수만큼 고를 수 있어요.");
        }
        void ForgeView(RunState s)
        {
            var p = s.Pending; long uses = Integer(Get(p, "uses")); var offers = Get(p, "offers") as IDictionary<string, object?>;
            var panel = Box(frame!, "Forge", 32, 170, 1536, 420, Panel);
            Label(panel, "공방" + (Truthy(Get(p, "fromEvent")) ? " · 사건의 변주" : ""), 20, 14, 600, 44, 32, Brass, FontStyle.Bold);
            if (offers != null)
            {
                string key = Text(Get(offers, "key"));
                Label(panel, CardName(s.Deck.FirstOrDefault(c => c.Key == key)?.Id) + "에 붙일 부품을 고르세요", 20, 66, 1496, 36, 24, Ink);
                Button(panel, "Proceed", "변주하지 않고 떠나기", 1196, 330, 320, 64, () => Proceed(), Line, 22);
                var parts = ListOf(Get(offers, "parts")).Select(Text).ToList();
                for (int k = 0; k < parts.Count; k++)
                {
                    string id = parts[k]; var r = Button(panel, "ForgePart:" + id, "", 20 + k * 506, 120, 490, 160, () => ForgeApply(id), Line).GetComponent<RectTransform>();
                    Label(r, PartName(id) + "  ·  " + SlotName(id), 14, 8, 462, 34, 23, Ink, FontStyle.Bold);
                    Label(r, partDefs.TryGetValue(id, out var def) ? def.Text : "", 14, 46, 462, 104, 18, Muted);
                }
            }
            else
            {
                Label(panel, "카드를 다듬는다  ·  남은 이용 " + uses + "회. 변주는 카드 1장에 무작위 부품 3개 중 하나를 붙여요(회로·각인 각 1칸).", 20, 66, 1496, 60, 22, Ink);
                Button(panel, "ForgeVariation", "카드 변주", 20, 150, 440, 72, () => OpenDeckPick(Purpose.ForgeOffer, "변주할 카드를 고르세요"), Memory, 23).interactable = uses > 0;
                Button(panel, "ForgeRemove", "카드 제거", 480, 150, 440, 72, () => OpenDeckPick(Purpose.ForgeRemove, "제거할 카드를 고르세요"), Line, 23).interactable = uses > 0;
                Button(panel, "Proceed", "떠나기", 1196, 150, 320, 72, () => Proceed(), Brass, 24);
            }
            NoticeLine(offers != null ? "부품 하나를 고르면 그 카드에 붙고 이용 1회를 써요." : "변주나 제거를 고르면 덱에서 카드를 골라요.");
        }
        void ShopView(RunState s)
        {
            var p = s.Pending;
            var panel = Box(frame!, "Shop", 32, 170, 1536, 650, Panel);
            Label(panel, "상점 · 떠돌이 기억 상인", 20, 10, 800, 40, 30, Brass, FontStyle.Bold);
            Label(panel, "카드", 20, 56, 200, 26, 20, Memory);
            var cards = ListOf(Get(p, "cards"));
            for (int k = 0; k < cards.Count; k++)
            {
                string id = Text(Get(cards[k], "id")); cardDefs.TryGetValue(id, out var def);
                ShopItem(panel, "cards", cards[k], CardName(id), def == null ? "" : CardLine(def), 20 + k * 502, 84, 490, 130, s.Coins, () => ShowCardInfo(id));
            }
            Label(panel, "부품", 20, 226, 200, 26, 20, Memory);
            var parts = ListOf(Get(p, "parts"));
            for (int k = 0; k < parts.Count; k++)
            {
                string id = Text(Get(parts[k], "id"));
                ShopItem(panel, "parts", parts[k], PartName(id) + " · " + SlotName(id), partDefs.TryGetValue(id, out var def) ? def.Text : "", 20 + k * 754, 254, 742, 110, s.Coins);
            }
            Label(panel, "유물", 20, 376, 200, 26, 20, Memory);
            var relics = ListOf(Get(p, "relics"));
            for (int k = 0; k < relics.Count; k++)
            {
                string id = Text(Get(relics[k], "id"));
                ShopItem(panel, "relics", relics[k], RelicName(id), relicDefs.TryGetValue(id, out var def) ? def.Text : "", 20 + k * 754, 404, 742, 110, s.Coins);
            }
            object? removeCost = Get(p, "removeCost");
            if (removeCost == null) Label(panel, "제거 서비스 이용 완료", 20, 546, 600, 40, 22, Muted);
            else Button(panel, "ShopRemove", "카드 제거 (동전 " + Integer(removeCost) + ")", 20, 534, 440, 64, () => OpenDeckPick(Purpose.ShopRemove, "제거할 카드를 고르세요"), Line, 22)
                .interactable = s.Coins >= Integer(removeCost);
            Button(panel, "Proceed", "떠나기", 1196, 534, 320, 64, () => Proceed(), Brass, 24);
            NoticeLine("가격은 상점이 정한 그대로예요. 동전이 부족하면 살 수 없어요.");
        }
        void ShopItem(Transform parent, string kind, object? item, string title, string detail, float x, float y, float width, float height, int coins, Action? info = null)
        {
            string id = Text(Get(item, "id")); long price = Integer(Get(item, "price")); bool sold = Truthy(Get(item, "sold"));
            var box = Box(parent, "Shop:" + kind + ":" + id, x, y, width, height, Background);
            Label(box, title, 14, 8, width - (info != null ? 200 : 28), 34, 23, sold ? Muted : Ink, FontStyle.Bold);
            // Reading is separate from buying: the detail button never pays.
            if (info != null) Button(box, "Info:" + kind + ":" + id, "카드 설명", width - 176, 8, 162, 54, info, Line, 20);
            Label(box, detail, 14, 44, width - 200, height - 52, 17, Muted);
            Button(box, "Buy:" + kind + ":" + id, sold ? "구매함" : "동전 " + price, width - 176, height - 60, 162, 50, () => Buy(kind, id), sold ? Line : Brass, 21)
                .interactable = !sold && coins >= price;
        }
        void EventView(RunState s)
        {
            var p = s.Pending; string id = Text(Get(p, "id"));
            var definition = data!.Events.LastOrDefault(e => e.Id == id);
            var panel = Box(frame!, "Event", 32, 170, 1536, 650, Panel);
            if (definition == null) { Label(panel, "알 수 없는 사건이에요.", 20, 14, 1496, 44, 28, Danger); return; }
            Label(panel, definition.Name, 20, 14, 1496, 44, 32, Brass, FontStyle.Bold);
            Label(panel, definition.Text, 20, 66, 1496, 120, 22, Ink);
            if (Truthy(Get(p, "done")))
            {
                var chosen = definition.Choices[(int)Integer(Get(p, "choice"))];
                Label(panel, chosen.Label + " — " + chosen.Text, 20, 200, 1496, 90, 22, Memory);
                Button(panel, "Proceed", "지도로", 1196, 566, 320, 64, () => Proceed(), Brass, 24);
            }
            else
                for (int i = 0; i < definition.Choices.Count; i++)
                {
                    int index = i; var choice = definition.Choices[i]; int coins = choice.RequiredCoins.GetValueOrDefault();
                    Button(panel, "EventChoice:" + i, choice.Label + (coins > 0 ? " (동전 " + coins + " 필요)" : "") + "\n" + choice.Text, 20, 200 + i * 100, 1496, 88, () => EventChoice(index, choice), Line, 21)
                        .interactable = s.Coins >= coins;
                }
            NoticeLine("선택지를 고르면 바로 결과가 적용돼요. 카드를 고르는 선택지는 먼저 카드를 골라요.");
        }
        void EventChoice(int index, RunEventChoice choice)
        {
            var fx = choice.Effects; int count = fx.CardCopy.GetValueOrDefault() != 0 ? 1 : fx.CardRemove.GetValueOrDefault();
            if (count > 0) OpenDeckPick(Purpose.EventCards, fx.CardCopy.GetValueOrDefault() != 0 ? "복제할 카드를 고르세요" : "제거할 카드를 고르세요 (" + count + "장)", count, index);
            else ChooseEvent(index);
        }
        void ArchiveView(RunState s)
        {
            var panel = Box(frame!, "Archive", 32, 170, 1536, 360, Panel);
            Label(panel, "기록소", 20, 14, 400, 44, 32, Brass, FontStyle.Bold);
            Label(panel, "첫 구역의 기록을 확정했다", 20, 66, 1496, 40, 26, Ink, FontStyle.Bold);
            Label(panel, "여기서 돌아가면 지금까지의 덱을 기억으로 저장할 수 있어요. 계속 가면 체력 15%를 회복하고 다음 구역으로 갑니다(패배하면 변경 1개만 남아요).", 20, 116, 1496, 70, 21, Muted);
            Button(panel, "ArchiveContinue", "계속 탐사", 20, 220, 440, 72, () => Archive("continue"), Brass, 24);
            Button(panel, "ArchiveReturn", "귀환하고 저장", 480, 220, 440, 72, () => Archive("return"), Line, 24);
            NoticeLine("귀환하면 기억 저장 화면으로 가요.");
        }

        // ───────── terminal memory (§8.9-8.14) ─────────
        void SaveView(RunState s)
        {
            var info = run!.MemoryChanges(); var order = ChangeOrder(info, s);
            selectedChanges.RemoveAll(id => !order.Contains(id));
            var flat = Flatten(info, s); var preview = SafePreview(flat); bool defeat = Truthy(Get(s.Pending, "defeat"));
            var head = Box(frame!, "SaveHeader", 32, 88, 1536, 120, Panel);
            Label(head, defeat ? "패배 · 출발 전 기억은 지켜져요. 이번 변화는 하나까지" : "탐사 종료 · 이번 탐사의 변화를 골라 기억에 더하세요", 20, 8, 1260, 40, 28, defeat ? Danger : Brass, FontStyle.Bold);
            Label(head, defeat ? "패배해도 출발 전 기억 덱은 그대로예요. 이번 탐사에서 생긴 변화 중 파티 전체에서 1개까지 더할 수 있어요. 아무것도 고르지 않으면 기존 덱이 유지돼요."
                : "출발 전 기억 덱에 이번 탐사의 변화를 여러 개 더할 수 있어요. 용량은 고른 것의 합이 아니라 완성된 결과 덱 전체로 계산해요. 저장한 덱은 다음 탐사의 시작 덱이 돼요.", 20, 50, 1496, 44, 18, Ink);
            Label(head, "선택 " + flat.Count + "개" + (defeat ? " / 1" : "") + "  ·  연습용 기억은 이 세션에만 있고 앱을 닫으면 사라져요.", 20, 94, 1240, 24, 17, Muted);
            if (s.Deck.Any(GainedCommon)) Button(head, "AssignButton", "공용 카드 배정", 1300, 10, 216, 44, () => Open(Sheet.Assign), Line, 20);
            var reasons = new List<string>(); string? error = preview == null ? "INVALID_SNAPSHOT" : Get(preview, "error") as string;
            if (error == "DEFEAT_KEEP_ONE") reasons.Add("패배하면 이번 탐사의 변화는 파티 전체에서 1개까지만 남길 수 있어요. 지금 " + flat.Count + "개를 골랐어요.");
            else if (error == "OVER_CAPACITY")
                reasons.Add("용량을 넘는 캐릭터가 있어요: " + string.Join(", ", s.Party.Select(x => x.CharId).Where(cid => Used(preview, cid) > Capacity(preview, cid))
                    .Select(cid => CharacterName(cid) + " " + Used(preview, cid) + "/" + Capacity(preview, cid))) + ". 결과 덱 전체 비용으로 계산해요.");
            else if (error != null) reasons.Add(ErrorText(error));
            for (int i = 0; i < s.Party.Count; i++) MemoryColumn(s.Party[i].CharId, Get(info, s.Party[i].CharId), order, flat, preview, 32 + i * 518);
            string fallback = reasons.Count != 0 ? "저장 버튼을 쓸 수 없는 이유: " + string.Join(" / ", reasons) : "고른 변화는 확정하기 전까지 아무것도 바꾸지 않아요.";
            Color fallbackColor = reasons.Count != 0 ? Danger : Muted;
            TrackNotice(Label(frame!, LastError != null ? notice : fallback, 32, 816, 930, 62, 18, LastError != null ? Danger : fallbackColor), fallback, fallbackColor);
            Button(frame!, "CommitMemory", flat.Count != 0 ? "고른 변화 " + flat.Count + "개 저장하고 끝내기" : "변화 없이 저장(기존 덱 유지)", 978, 816, 316, 62, () => CommitMemory(), reasons.Count == 0 ? Brass : Line, 21)
                .interactable = reasons.Count == 0;
            Button(frame!, "CloseWithoutSave", "저장하지 않고 닫기", 1310, 816, 258, 62, () => CloseWithoutSave(), Line, 21);
        }
        void MemoryColumn(string cid, object? entry, List<string> order, List<string> flat, Dictionary<string, object?>? preview, float x)
        {
            var column = Box(frame!, "Memory:" + cid, x, 218, 500, 590, Panel);
            long capacity = preview != null ? Capacity(preview, cid) : Integer(Get(entry, "capacity")), used = preview != null ? Used(preview, cid) : Integer(Get(entry, "usedBefore"));
            Label(column, CharacterName(cid), 20, 10, 460, 36, 28, Ink, FontStyle.Bold);
            Label(column, "출발 전 용량 " + Integer(Get(entry, "usedBefore")) + "/" + Integer(Get(entry, "capacity")) + "  ·  결과 용량 " + used + "/" + capacity, 20, 50, 460, 28, 19, used > capacity ? Danger : Brass);
            var before = ListOf(Get(entry, "before"));
            Label(column, "출발 전에 있던 기억 " + before.Count + "장 — 유지", 20, 84, 460, 24, 17, Memory);
            Label(column, string.Join(", ", before.Select(SpecText)), 20, 108, 460, 66, 16, Muted);
            var changes = ListOf(Get(entry, "changes"));
            Label(column, "이번 탐사에서 생긴 변화 " + changes.Count + "개", 20, 180, 460, 26, 18, Memory);
            if (changes.Count == 0) Label(column, "바뀐 점이 없어요. 출발 전 기억이 그대로 유지돼요.", 20, 212, 460, 52, 17, Muted);
            else
            {
                var list = Scroll(column, "Changes:" + cid, 20, 210, 460, 230, 460, changes.Count * 58, Background);
                for (int k = 0; k < changes.Count; k++)
                {
                    string id = Text(Get(changes[k], "id")); bool on = flat.Contains(id);
                    string hint = on ? "선택됨 · 저장하면 남아요" : Hint(order, flat, id, cid);
                    Button(list, "Change:" + id, (on ? "[선택] " : "") + KindName(Text(Get(changes[k], "kind"))) + " · " + Text(Get(changes[k], "label")) + "\n" + hint, 0, k * 58, 460, 52,
                        () => ToggleChange(id), on ? Memory : Line, 18);
                }
            }
            var result = preview != null ? ListOf(Get(Get(preview, "memory"), cid)) : before;
            var beforeByKey = before.GroupBy(r => Text(Get(r, "key"))).ToDictionary(g => g.Key, g => Signature(g.First()), StringComparer.Ordinal);
            var resultKeys = new HashSet<string>(result.Select(r => Text(Get(r, "key"))), StringComparer.Ordinal);
            Label(column, "결과 덱 미리보기 " + result.Count + "장", 20, 448, 460, 24, 17, Memory);
            Label(column, string.Join(", ", result.Select(r => SpecText(r) + (!beforeByKey.TryGetValue(Text(Get(r, "key")), out var sig) ? " (새로)" : sig != Signature(r) ? " (바뀜)" : ""))), 20, 472, 460, 72, 16, Ink);
            var dropped = before.Where(r => !resultKeys.Contains(Text(Get(r, "key")))).ToList();
            if (dropped.Count != 0) Label(column, "이번 저장으로 빠지는 카드: " + string.Join(", ", dropped.Select(SpecText)), 20, 548, 460, 36, 16, Danger);
        }
        string Hint(List<string> order, List<string> flat, string id, string cid)
        {
            var alternative = SafePreview(order.Where(x => flat.Contains(x) || x == id).ToList());
            if (alternative == null) return "-";
            string? error = Get(alternative, "error") as string;
            if (error == "DEFEAT_KEEP_ONE") return "패배: 1개까지";
            return "고르면 결과 용량 " + Used(alternative, cid) + "/" + Capacity(alternative, cid) + (error == "OVER_CAPACITY" ? " · 초과" : "");
        }
        static long Used(object? preview, string cid) => Integer(Get(Get(preview, "used"), cid));
        static long Capacity(object? preview, string cid) => Integer(Get(Get(preview, "capacities"), cid));
        static bool GainedCommon(RunCardSpec card) => card.Owner == "common" && Truthy(Get(card.ToJsonGraph(), "gained"));

        void ResultView(RunState s)
        {
            var panel = Box(frame!, "Result", 32, 88, 1536, 788, Panel);
            string outcome = Text(Get(s.Result, "outcome"));
            string title = closedKind == "abandon" ? "탐사 포기" : outcome == "defeat" ? "패배" : outcome == "return" ? "기록소 귀환" : outcome == "clear" ? "탐사 완주" : "탐사 종료";
            Label(panel, title, 24, 18, 1488, 56, 42, Brass, FontStyle.Bold);
            Label(panel, "전투 " + s.Statistics.Battles + "회 (엘리트 " + s.Statistics.Elites + ")  ·  처치 " + s.Statistics.Kills + "  ·  남은 체력 " + s.Hp + "/" + s.MaxHp
                + "  ·  유물 " + (s.Relics.Count == 0 ? "-" : string.Join(", ", s.Relics.Select(RelicName))), 24, 84, 1488, 34, 22, Ink);
            var used = Get(lastSave, "used") as IDictionary<string, object?>;
            string memory = closedKind != null || lastSave == null ? "저장하지 않고 닫음 · 기억 덱 변화 없음"
                : (lastSaveCount == 0 ? "기존 덱 유지" : "변화 " + lastSaveCount + "개 저장")
                  + (used == null ? "" : " · " + string.Join(", ", used.Select(u => CharacterName(u.Key) + " 용량 " + Integer(u.Value))));
            Label(panel, memory, 24, 124, 1488, 34, 22, Memory);
            for (int i = 0; i < ReferenceParty.Count; i++)
            {
                string cid = ReferenceParty[i]; var column = Box(panel, "Saved:" + cid, 24 + i * 500, 176, 480, 360, Background);
                var rows = Get(Get(store, "memory"), cid) as IList;
                Label(column, CharacterName(cid), 16, 10, 448, 36, 28, Ink, FontStyle.Bold);
                Label(column, rows == null ? "기본 덱 (저장된 변화 없음)" : "저장된 기억 덱 " + rows.Count + "장", 16, 50, 448, 28, 19, Memory);
                var specs = rows != null ? rows.Cast<object?>().ToList() : MemoryV2.Baseline(data!.Memory, cid).Select(c => (object?)c.ToJsonGraph()).ToList();
                Label(column, string.Join(", ", specs.Select(SpecText)), 16, 84, 448, 260, 17, Muted);
            }
            Label(panel, "이 기억은 연습용으로 이 세션에만 있어요. 앱을 닫으면 사라져요. 다음 탐사는 위 기억 덱으로 시작해요.", 24, 552, 1488, 60, 20, Muted);
            Button(panel, "LengthShort", "짧은 탐사 (1구역)", 24, 640, 300, 64, () => { Length = "short"; Render(); }, Length == "short" ? Memory : Line, 22);
            Button(panel, "LengthFull", "긴 탐사 (2구역)", 340, 640, 300, 64, () => { Length = "full"; Render(); }, Length == "full" ? Memory : Line, 22);
            Button(panel, "Depart", "다음 탐사 출발", 1188, 640, 324, 64, () => Depart(), Brass, 26);
            if (notice.Length != 0) TrackNotice(Label(panel, notice, 24, 722, 1488, 44, 20, LastError != null ? Danger : Memory), "", Memory);
        }

        // ───────── overlay sheets ─────────
        void Open(Sheet next) { sheet = next; Render(); }
        void CloseSheet() { sheet = Sheet.None; deckPicks.Clear(); Render(); }
        void OpenDeckPick(Purpose kind, string title, int count = 1, int index = -1, string? part = null)
        { purpose = kind; deckTitle = title; need = count; eventIndex = index; partId = part; deckPicks.Clear(); sheet = Sheet.DeckPick; Render(); }
        bool Pickable(RunCardSpec card) => !card.Weapon && !(purpose == Purpose.Upgrade && Truthy(Get(card.ToJsonGraph(), "plus")));
        void DeckPickSheet(RunState s)
        {
            var p = OverlayPanel("DeckPicker", 160, 90, 1280, 720);
            int required = Math.Min(need, s.Deck.Count(Pickable));
            Label(p, deckTitle, 24, 16, 1232, 44, 28, Ink, FontStyle.Bold);
            Label(p, (purpose == Purpose.Upgrade ? "이미 강화한 카드와 무기 카드는 고를 수 없어요." : "무기 카드는 고를 수 없어요.") + " 확정하기 전에는 아무것도 바뀌지 않아요.", 24, 62, 1232, 28, 19, Muted);
            var list = Scroll(p, "DeckPickList", 24, 100, 1232, 500, 1232, ((s.Deck.Count + 2) / 3) * 62, Panel);
            for (int i = 0; i < s.Deck.Count; i++)
            {
                var card = s.Deck[i]; string key = card.Key ?? ""; bool ok = Pickable(card); int order = deckPicks.IndexOf(key);
                Button(list, "DeckCard:" + key, (order >= 0 ? (order + 1) + ". " : "") + DeckCardText(card) + (ok ? "" : " · 선택 불가"), i % 3 * 412, i / 3 * 62, 400, 54,
                    () => ToggleDeckPick(key, required), order >= 0 ? Memory : Line, 18).interactable = ok;
            }
            string verb = purpose == Purpose.Upgrade ? "강화하기" : purpose == Purpose.ForgeOffer ? "이 카드에 변주" : purpose == Purpose.FoundPart ? "부품 붙이기"
                : purpose == Purpose.EventCards ? "선택 완료" : "제거하기";
            Button(p, "ConfirmDeckPick", verb + (required > 1 ? " (" + deckPicks.Count + "/" + required + ")" : ""), 24, 620, 420, 64, ConfirmDeckPick, deckPicks.Count == required ? Brass : Line, 22)
                .interactable = required > 0 && deckPicks.Count == required;
            Button(p, "DeckPickInfo", deckPicks.Count != 0 ? "고른 카드 설명" : "카드를 고르면 설명", 470, 620, 300, 64, () => ShowDeckCardInfo(deckPicks.LastOrDefault() ?? ""), Line, 21)
                .interactable = deckPicks.Count != 0;
            Button(p, "CancelDeckPick", "취소", 1032, 620, 224, 64, CloseSheet, Line, 22);
        }
        void ToggleDeckPick(string key, int required)
        {
            if (deckPicks.Remove(key)) { Render(); return; }
            if (required == 1) deckPicks.Clear();
            if (deckPicks.Count < required) deckPicks.Add(key);
            Render();
        }
        void ConfirmDeckPick()
        {
            var picks = new List<string>(deckPicks); string key = picks.FirstOrDefault() ?? ""; sheet = Sheet.None; deckPicks.Clear();
            bool ok = purpose == Purpose.Upgrade ? RestUpgrade(key) : purpose == Purpose.ForgeOffer ? ForgeOffer(key) : purpose == Purpose.ForgeRemove ? ForgeRemove(key)
                : purpose == Purpose.ShopRemove ? BuyRemoval(key) : purpose == Purpose.EventCards ? ChooseEvent(eventIndex, picks) : ApplyFoundPart(partId ?? "", key);
            if (!ok) { sheet = Sheet.DeckPick; deckPicks.AddRange(picks); Render(); }
        }
        void DeckSheet(RunState s)
        {
            var p = OverlayPanel("DeckView", 160, 90, 1280, 720);
            Label(p, "덱 " + s.Deck.Count + "장 · 카드를 누르면 전체 설명을 볼 수 있어요", 24, 16, 1060, 44, 28, Ink, FontStyle.Bold);
            Button(p, "CloseDeck", "닫기", 1110, 12, 146, 52, CloseSheet, Line, 22);
            var list = Scroll(p, "DeckList", 24, 76, 1232, 430, 1232, ((s.Deck.Count + 2) / 3) * 56, Panel);
            for (int i = 0; i < s.Deck.Count; i++)
            {
                string key = s.Deck[i].Key ?? "";
                Button(list, "DeckInfo:" + key, DeckCardText(s.Deck[i]), i % 3 * 412, i / 3 * 56, 400, 50, () => ShowDeckCardInfo(key), Line, 18);
            }
            Label(p, "보유 부품 " + s.PartsFound.Count + "개" + (s.PartsFound.Count != 0 ? " · 회로·각인은 카드마다 1칸씩, 같은 칸에 붙이면 교체돼요." : ""), 24, 520, 1232, 30, 20, Memory);
            var parts = Scroll(p, "FoundParts", 24, 558, 1232, 140, 1232, ((s.PartsFound.Count + 2) / 3) * 66, Panel);
            for (int i = 0; i < s.PartsFound.Count; i++)
            {
                string id = s.PartsFound[i];
                Button(parts, "FoundPart:" + i, PartName(id) + " · " + SlotName(id) + " → 카드에 붙이기", i % 3 * 412, i / 3 * 66, 400, 56,
                    () => OpenDeckPick(Purpose.FoundPart, "「" + PartName(id) + "」을 붙일 카드를 고르세요", 1, -1, id), Line, 18).interactable = !Finished(s);
            }
        }
        // The effect text is one label with Core's exact string; every block grows to its wrapped height and the body scrolls, so long
        // names and nested conditions are never cut off.
        void CardInfoSheet(CardInfo info)
        {
            var p = OverlayPanel("CardInfo", 240, 80, 1120, 740);
            Label(p, info.Title, 24, 14, 920, 52, 30, Ink, FontStyle.Bold);
            Button(p, "CloseCardInfo", "닫기", 960, 12, 136, 56, CloseCardInfo, Line, 22);
            var content = Scroll(p, "CardInfoScroll", 24, 80, 1072, 636, 1072, 636, Background);
            float y = Flow(content, info.Basis, 12, 19, Memory);
            y = Flow(content, info.Facts, y, 21, Brass);
            y = Flow(content, info.Text, y, 26, Ink);
            if (info.Notes.Length != 0) y = Flow(content, info.Notes, y, 20, Muted);
            content.sizeDelta = new Vector2(1072, Math.Max(636, y));
            content.parent.GetComponent<ScrollRect>().vertical = y > 636;
        }
        float Flow(RectTransform content, string value, float y, int size, Color color, float width = 1040)
        {
            var text = Label(content, value, 16, y, width, 10, size, color);
            text.resizeTextForBestFit = false; text.verticalOverflow = VerticalWrapMode.Overflow;
            float height = Mathf.Ceil(text.preferredHeight) + 6;
            text.rectTransform.sizeDelta = new Vector2(width, height);
            return y + height + 14;
        }
        // §14.3 기록: the Run's own log and the recent 40 battle event rows (digest event rows only; kept after a battle ends until the
        // next departure). Read-only text; the body scrolls.
        void LogSheet(RunState s)
        {
            var p = OverlayPanel("ExpeditionLog", 300, 80, 1000, 740);
            Label(p, "탐사 기록", 22, 14, 700, 40, 28, Brass, FontStyle.Bold);
            Button(p, "CloseLog", "닫기", 856, 8, 122, 48, CloseSheet, Line, 22);
            var content = Scroll(p, "ExpeditionLogScroll", 22, 68, 956, 650, 956, 650, Background);
            var lines = s.Log.TakeLast(14).ToList();
            float y = Flow(content, "탐사 진행", 12, 20, Memory, 924);
            y = Flow(content, lines.Count == 0 ? "아직 기록이 없어요." : string.Join("\n", lines), y, 19, Ink, 924);
            y = Flow(content, "전투 기록 · 최근 " + battleLog.Count + "개 (최대 " + BattleLogLimit + "개)", y, 20, Memory, 924);
            y = Flow(content, battleLog.Count == 0 ? "아직 전투 기록이 없어요." : string.Join("\n", battleLog), y, 19, Ink, 924);
            content.sizeDelta = new Vector2(956, Math.Max(650, y));
            content.parent.GetComponent<ScrollRect>().vertical = y > 650;
        }
        // The turn barrier sheet: a full-frame blocker, the summary text revealed by AdvanceTurnSummary, and skip/continue placed away
        // from the end-turn, map/proceed and save buttons so a repeated tap cannot reach them.
        void TurnSummarySheet()
        {
            var p = OverlayPanel("TurnSummary", 160, 60, 1280, 830);
            Label(p, "턴 결과", 32, 16, 900, 46, 32, Brass, FontStyle.Bold);
            Label(p, "이 연습 세션에서 이미 확정된 결과예요. '계속'을 누르면 다음 행동을 할 수 있어요.", 32, 66, 1216, 30, 18, Muted);
            summaryContent = Scroll(p, "TurnSummaryScroll", 32, 104, 1216, 600, 1216, 600, Background);
            summaryScroll = summaryContent.parent.GetComponent<ScrollRect>();
            summaryText = Label(summaryContent, "", 16, 12, 1184, 10, 21, Ink);
            summaryText.resizeTextForBestFit = false; summaryText.verticalOverflow = VerticalWrapMode.Overflow;
            summaryStatus = Label(p, "", 32, 712, 1216, 34, 19, Memory);
            Button(p, "SkipTurnSummary", "남은 결과 모두 보기", 40, 760, 340, 60, () => SkipTurnSummary(), Line, 22);
            Button(p, "ContinueTurnSummary", "계속", 400, 760, 380, 60, () => ContinueTurnSummary(), Brass, 24);
            RefreshTurnSummary();
        }
        void AbandonSheet()
        {
            var p = OverlayPanel("AbandonSheet", 440, 260, 720, 330);
            Label(p, "탐사를 포기할까요?", 24, 20, 672, 44, 30, Ink, FontStyle.Bold);
            Label(p, "이번 탐사의 진행과 변화는 기억에 남지 않고 버려져요. 출발 전 기억 덱은 그대로예요.", 24, 76, 672, 90, 21, Muted);
            Button(p, "ConfirmAbandon", "포기하기", 24, 236, 300, 64, () => Abandon(), Danger, 24);
            Button(p, "CancelAbandon", "계속 탐사", 396, 236, 300, 64, CloseSheet, Brass, 24);
        }
        void AssignSheet(RunState s)
        {
            var commons = s.Deck.Where(GainedCommon).ToList();
            var p = OverlayPanel("AssignSheet", 260, 140, 1080, 620);
            Label(p, "공용 카드는 누구의 기억에 넣을까요?", 24, 16, 900, 44, 28, Ink, FontStyle.Bold);
            Label(p, "배정을 바꾸면 고른 변화가 초기화돼요. 출발 전 기억에 있던 카드의 배정은 여기서 바꿀 수 없어요.", 24, 62, 1032, 30, 19, Muted);
            var list = Scroll(p, "AssignList", 24, 100, 1032, 420, 1032, commons.Count * 64, Panel);
            for (int i = 0; i < commons.Count; i++)
            {
                var card = commons[i]; string key = card.Key ?? "";
                Label(list, CardName(card.Id), 8, i * 64 + 10, 400, 40, 22, Ink);
                for (int k = 0; k < s.Party.Count; k++)
                {
                    string cid = s.Party[k].CharId;
                    Button(list, "Assign:" + key + ":" + cid, CharacterName(cid), 420 + k * 204, i * 64 + 4, 192, 52, () => Assign(key, cid), card.Assigned == cid ? Memory : Line, 21);
                }
            }
            Button(p, "CloseAssign", "닫기", 832, 540, 224, 60, CloseSheet, Line, 22);
        }

        // ───────── text projections ─────────
        string Location(RunState s) =>(s.Zone.HasValue ? s.Zone.Value + "구역 (" + (s.ZoneIndex + 1) + "/" + s.Zones.Count + ")" : "구역 밖") + " · "
            + (s.Position == null ? "출발 전" : s.Floor >= 7 ? "보스" : (s.Floor + 1) + "층") + " · " + (s.Length == "full" ? "긴 탐사" : "짧은 탐사");
        string PhaseName(RunState s)
        {
            if (closedKind == "abandon") return "포기";
            switch (s.Phase)
            {
                case "MAP": return "지도";
                case "BATTLE": return s.BattleType == "elite" ? "엘리트 전투" : s.BattleType == "boss" ? "보스 전투" : "전투";
                case "REWARD": return "전투 보상"; case "REST": return "휴식"; case "FORGE": return "공방"; case "SHOP": return "상점";
                case "EVENT": return "사건"; case "ARCHIVE": return "기록소"; case "VICTORY": return "탐사 종료 · 기억 저장";
                case "DEFEAT": return "패배 · 잔존 기억"; case "DONE": return "기억 저장 완료"; default: return s.Phase;
            }
        }
        static string NodeName(string type)
        {
            switch (type)
            {
                case "battle": return "전투"; case "elite": return "엘리트"; case "rest": return "휴식"; case "forge": return "공방";
                case "shop": return "상점"; case "event": return "사건"; case "boss": return "보스"; default: return type;
            }
        }
        static string KindName(string kind) => kind == "add" ? "추가" : kind == "remove" ? "제거" : kind == "evolve" ? "진화" : kind == "upgrade" ? "강화" : kind == "part" ? "부품" : kind;
        static string ModeName(string mode) => mode == "now" ? "즉시" : mode == "next" ? "다음 턴" : "다다음 턴";
        static string ModeTiming(string mode) => mode == "now" ? "바로 해결" : mode == "next" ? "다음 턴 시작에 해결" : "다다음 턴 시작에 해결";
        string CardName(string? id) => id != null && cardDefs.TryGetValue(id, out var card) ? card.Name : "알 수 없는 카드";
        string PartName(string id) => partDefs.TryGetValue(id, out var part) ? part.Name : "알 수 없는 부품";
        string SlotName(string id) => partDefs.TryGetValue(id, out var part) ? part.Slot == "circuit" ? "시간 회로" : part.Slot == "engraving" ? "보조 각인" : "임시" : "";
        string RelicName(string id) => relicDefs.TryGetValue(id, out var relic) ? relic.Name : "알 수 없는 유물";
        string CharacterName(string? id) => id != null && characterNames.TryGetValue(id, out var name) ? name : "공용";
        string BattleCardName(string uid)
        {
            try { return run?.BattleEffectiveCard(uid).Name ?? "카드"; }
            catch (RuleException) { return "카드"; }
        }
        string CardLine(BattleCardDef def) => OwnerText(def.Owner) + " · 등급 " + def.Rarity + " · 비용 " + def.Cost + " · "
            + (def.Reservation == "y" ? "즉시·예약" : def.Reservation == "o" ? "예약 전용" : def.Reservation == "after" ? "다다음 턴 예약 전용" : "즉시 사용");
        void CardButton(Transform parent, string name, string id, float x, float y, float width, float height, Action click)
        {
            var r = Button(parent, name, "", x, y, width, height, click, Line).GetComponent<RectTransform>();
            Label(r, CardName(id), 14, 8, width - 28, 38, 25, Ink, FontStyle.Bold);
            Label(r, cardDefs.TryGetValue(id, out var def) ? CardLine(def) : "", 14, 52, width - 28, 60, 18, Muted);
        }
        // Card spec rows: name, + for an upgrade, then fitted circuit/engraving parts.
        string SpecText(object? spec)
        {
            string text = CardName(Get(spec, "id") as string) + (Truthy(Get(spec, "plus")) ? "+" : "");
            var mods = new[] { Get(Get(spec, "mods"), "c") as string, Get(Get(spec, "mods"), "e") as string }.Where(m => !string.IsNullOrEmpty(m)).Select(m => PartName(m!)).ToList();
            return mods.Count == 0 ? text : text + "[" + string.Join("·", mods) + "]";
        }
        string DeckCardText(RunCardSpec card) => SpecText(card.ToJsonGraph()) + " · " + DeckOwner(card);
        string DeckOwner(RunCardSpec card) =>
            card.Weapon ? CharacterName(card.Wielder) + " 무기" : card.Owner == "common" ? "공용→" + CharacterName(card.Assigned) : card.Owner == "curse" ? "저주" : CharacterName(card.Owner);
        string OwnerText(string owner) => owner == "common" ? "공용" : owner == "curse" ? "저주" : owner == "weapon" ? "무기" : CharacterName(owner);
        static string Signature(object? spec) => string.Join("|", Get(spec, "id") as string, Truthy(Get(spec, "plus")), Get(spec, "from") as string,
            Get(Get(spec, "mods"), "c") as string, Get(Get(spec, "mods"), "e") as string, Get(spec, "assigned") as string);
        static string Attribute(string attr) => attr == "volt" ? "전격" : attr == "flame" ? "화염" : attr == "frost" ? "빙결" : attr == "void" ? "공허" : attr == "none" ? "무속성" : attr;
        // Retain the existing legacy mapper; the new event log uses the richer detached digest.
        string EventText(BattleEvent e)
        {
            string actor = e.Actor != null && characterNames.TryGetValue(e.Actor, out var name) ? name : "";
            switch (e.E)
            {
                case "hit": return "피해 " + (e.Damage ?? e.N ?? 0);
                case "play": return "카드 사용";
                case "reserve": return "기억을 예약했어요";
                case "ult": return actor + " 필살기";
                case "turn": return "턴 " + e.Turn;
                case "heal": return "체력 회복 " + e.N;
                case "block": return "방어 +" + e.N;
                case "break": return "균열!";
                case "kill": return "적을 처치했어요";
                case "end": return "턴 종료";
                default: return "전투 효과 적용";
            }
        }
        static readonly Dictionary<string, string> LocalErrors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Memory v2 texts from prototype/src/ui.js ERR, worded for the session-local practice store.
            ["VERSION_CONFLICT"] = "기억 덱이 먼저 바뀌어 있어 이번 저장은 반영하지 않았어요.",
            ["REQUEST_CONFLICT"] = "같은 요청 번호로 다른 내용이 이미 처리됐어요. 이번 저장은 반영하지 않았어요.",
            ["INVALID_SELECTION"] = "고른 변화가 이번 탐사의 변화 목록과 맞지 않아요.",
            ["INVALID_SNAPSHOT"] = "탐사 기록이 손상돼 저장할 수 없어요.",
            ["INVALID_PART"] = "허용되지 않는 부품이 있어 저장할 수 없어요.",
            ["RUN_MISMATCH"] = "이 탐사와 저장 요청이 맞지 않아요.",
            ["REASSIGNMENT_REQUIRES_SEPARATE_FLOW"] = "출발 전 기억에 있던 공용 카드의 배정은 여기서 바꿀 수 없어요.",
            ["RUN_IN_PROGRESS"] = "진행 중인 탐사를 끝내거나 포기한 뒤에 출발할 수 있어요.",
            [TurnSummaryOpen] = "턴 결과를 확인하는 중이에요. '계속'을 누르면 다음 행동을 할 수 있어요.",
            [RecoveryCode] = "연습 탐사를 멈췄어요. 이 탐사를 버리고 새로 출발해 주세요.",
            [PickSheetOpen] = "카드를 고르는 중이에요. 고르기를 마치거나 취소한 뒤에 다른 행동을 할 수 있어요.",
            [PickInvalid] = "지금 목록에 있는 카드만 고를 수 있어요.",
            [PickStale] = "고르기를 연 뒤 전투가 바뀌어 고르기를 닫았어요. 아무것도 쓰지 않았어요. 다시 골라 주세요.",
            [ActionShowing] = "방금 행동의 결과를 보여 주는 중이에요. 다음 카드는 미리 고를 수 있고, 표시가 끝나거나 건너뛰면 사용할 수 있어요.",
            [IntentPending] = "보내지 않은 사용 대기가 이미 1개 있어요. 결과 표시가 끝나면 보내요. 바꾸려면 먼저 '대기 취소'를 눌러 주세요.",
            [IntentStale] = "사용 대기를 확정한 뒤 전투가 바뀌어 보내지 않고 취소했어요. 아무것도 쓰지 않았어요. 다시 골라 주세요.",
            [IntentDropped] = "화면을 벗어나 보내지 않은 사용 대기를 취소했어요. 아무것도 쓰지 않았어요.",
            ["INTERNAL"] = "문제가 생겼어요. 다시 시도해 주세요."
        };
        string ErrorText(string code)
        {
            if (errors.TryGetValue(code, out var text)) return text.Contains("내부") ? "문제가 생겼어요. 다시 시도해 주세요." : text;
            return LocalErrors.TryGetValue(code, out var local) ? local : "지금은 이 행동을 할 수 없어요.";
        }

        // ───────── JSON graph helpers (Run graphs are plain dictionaries/lists) ─────────
        static object? Get(object? node, string key) => node is IDictionary<string, object?> map && map.TryGetValue(key, out var value) ? value : null;
        static List<object?> ListOf(object? value) => value is IList list ? list.Cast<object?>().ToList() : new List<object?>();
        static string Text(object? value) => value as string ?? "";
        static bool Truthy(object? value) => value is bool flag ? flag : value != null;
        static long Integer(object? value)
        {
            switch (value) { case int n: return n; case long n: return n; case uint n: return n; case double n: return (long)n; default: return 0; }
        }
        static object? Copy(object? value)
        {
            if (value is IDictionary<string, object?> map) { var copy = new Dictionary<string, object?>(StringComparer.Ordinal); foreach (var pair in map) copy.Add(pair.Key, Copy(pair.Value)); return copy; }
            if (value is IList list) { var copy = new List<object?>(); foreach (object? item in list) copy.Add(Copy(item)); return copy; }
            return value;
        }

        // ───────── uGUI helpers (same construction as the battle practice view) ─────────
        RectTransform OverlayPanel(string name, float x, float y, float width, float height)
        {
            var blocker = Box(frame!, name + "InputBlocker", 0, 0, 1600, 900, new Color(0, 0, 0, 0.65f));
            blocker.GetComponent<Image>().raycastTarget = true;
            var panel = Box(blocker, name, x, y, width, height, Panel);
            panel.GetComponent<Image>().raycastTarget = true;
            return panel;
        }
        RectTransform Scroll(Transform parent, string name, float x, float y, float width, float height, float contentWidth, float contentHeight, Color color)
        {
            var viewport = Box(parent, name, x, y, width, height, color); viewport.GetComponent<Image>().raycastTarget = true; viewport.gameObject.AddComponent<RectMask2D>();
            var content = Box(viewport, name + "Content", 0, 0, Math.Max(width, contentWidth), Math.Max(height, contentHeight), color);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = contentWidth > width; scroll.vertical = contentHeight > height; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 25;
            return content;
        }
        void Connector(Transform parent, Vector2 from, Vector2 to, Color color)
        {
            var obj = new GameObject("Path", typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false);
            var r = obj.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 0.5f);
            Vector2 delta = new Vector2(to.x - from.x, from.y - to.y); r.anchoredPosition = new Vector2(from.x, -from.y); r.sizeDelta = new Vector2(delta.magnitude, 3);
            r.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            var image = obj.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
        }
        void FitView()
        {
            if (canvasRoot == null || frame == null || Screen.width <= 0 || Screen.height <= 0) return;
            Rect safe = Screen.safeArea; Vector2 size = canvasRoot.rect.size;
            float width = size.x * safe.width / Screen.width, height = size.y * safe.height / Screen.height;
            frame.localScale = Vector3.one * Mathf.Min(width / 1600, height / 900);
            frame.anchoredPosition = new Vector2((safe.center.x / Screen.width - 0.5f) * size.x, (safe.center.y / Screen.height - 0.5f) * size.y);
        }
        void RemoveView()
        {
            var previous = canvasRoot != null ? canvasRoot : transform.Find(OwnedCanvas);
            if (previous == null) return;
            previous.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(previous.gameObject); else DestroyImmediate(previous.gameObject);
            canvasRoot = null; frame = null; noticeLabel = null; summaryText = null; summaryStatus = null; summaryContent = null; summaryScroll = null;
            actionText = null; actionStatus = null; actionSkip = null; confirmCaption = null; actionOverlay = null; actionGated.Clear(); paceButtons.Clear();
        }
        RectTransform Box(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false);
            var r = obj.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height);
            var image = obj.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return r;
        }
        Text Label(Transform parent, string value, float x, float y, float width, float height, int size, Color color, FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.UpperLeft)
        {
            var obj = new GameObject("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            var r = obj.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height);
            var text = obj.GetComponent<Text>(); text.font = font; text.text = value; text.fontSize = size; text.color = color; text.fontStyle = style; text.alignment = align; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = Math.Max(14, (int)(size * 0.7f)); text.resizeTextMaxSize = size; return text;
        }
        Button Button(Transform parent, string name, string caption, float x, float y, float width, float height, Action click, Color? color = null, int size = 24)
        {
            var r = Box(parent, name, x, y, width, height, color ?? Line); var image = r.GetComponent<Image>(); image.raycastTarget = true;
            var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            // Only a live, interactable button acts: a replaced view's button is inactive (RemoveView) until its deferred destroy.
            button.onClick.AddListener(() => { if (button != null && button.IsActive() && button.IsInteractable()) click(); });
            var colors = button.colors; colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f); colors.pressedColor = new Color(0.78f, 0.78f, 0.78f); colors.disabledColor = new Color(0.45f, 0.45f, 0.45f); button.colors = colors;
            bool light = color.HasValue && (color.Value == Brass || color.Value == Memory || color.Value == Danger);
            if (caption.Length != 0) Label(r, caption, 6, 5, width - 12, height - 10, size, light ? Background : Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
            return button;
        }
        void Bar(Transform parent, float x, float y, float width, float height, int value, int max, Color color)
        { var back = Box(parent, "Bar", x, y, width, height, Line); Box(back, "Fill", 0, 0, width * Mathf.Clamp01(max > 0 ? (float)value / max : 0), height, color); }
        static KeyValuePair<string, BattleArgument> Pair(string key, string? value) => new KeyValuePair<string, BattleArgument>(key, value == null ? BattleArgument.Null() : BattleArgument.String(value));
        static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
    }
}
