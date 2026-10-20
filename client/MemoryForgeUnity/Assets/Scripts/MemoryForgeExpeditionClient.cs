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
    // P1-CARD-FLOW-EFFECT-CANDIDATES §14.3/§14.7: within a known scope a sheet offers, accepts and revalidates only cards the reached Core
    // effect takes, so such a manual pick is not silently replaced by Core's fallback. tune, recall, growPick and copyPick leave out only
    // cards their effect refuses now and cannot take later in the same play; costdown, as its card's first effect, is priced as Core will
    // price it after the play's own "next card costs 0" charge. Outside that scope (a costdown after other effects, a conditional cost, a
    // price Core refuses) the sheet keeps its earlier zone list, never shrinking manual choice. Filtering uses the current public zones;
    // it does not predict cards drawn by the commit or later effects. Core's resolution, fallback order and explicit auto are unchanged.
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
            public List<(string Op, string? Part, bool Leading)> Rules = new List<(string, string?, bool)>(); // the Core effect of each request (ChoiceRules)
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
            public IReadOnlyList<(string Op, string? Part, bool Leading)> Rules = Array.Empty<(string, string?, bool)>();
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
        readonly Dictionary<string, string> ultimateNames = new Dictionary<string, string>(StringComparer.Ordinal); // the cut-in's caption
        readonly Dictionary<string, string> characterAttrs = new Dictionary<string, string>(StringComparer.Ordinal); // card frames take the owner's attribute colour
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
        // Why a request offers no card at all (PickBlocked): Core skips its effect before choosing, so no pick could do anything.
        public const string PickGrowthCap = BattleChoicePolicy.PickGrowthCap;
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
        // §14.2 tokens above; Glass is the panel token at 0.94 so the painting reads at the edges, LineEdge a secondary button's edge.
        static readonly Color Glass = new Color(0.078f, 0.110f, 0.192f, 0.94f), LineEdge = Hex("4a5d92");
        // View-only state (EXPEDITION-UI-20261018): art cache, text measure, sheets drawn in the previous view, and what the last
        // uncovered battle view showed, so the next one can animate the public difference. None of it is read by a command.
        readonly Dictionary<string, Sprite?> sprites = new Dictionary<string, Sprite?>(StringComparer.Ordinal);
        TextGenerator? measure;
        readonly Dictionary<string, float> heights = new Dictionary<string, float>(StringComparer.Ordinal);
        float renderedScale;
        readonly HashSet<string> sheetsBefore = new HashSet<string>(StringComparer.Ordinal), sheetsDrawn = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, RectTransform> enemyArt = new Dictionary<string, RectTransform>(StringComparer.Ordinal), enemyRoots = new Dictionary<string, RectTransform>(StringComparer.Ordinal),
            enemyBars = new Dictionary<string, RectTransform>(StringComparer.Ordinal), handFaces = new Dictionary<string, RectTransform>(StringComparer.Ordinal),
            ultRects = new Dictionary<string, RectTransform>(StringComparer.Ordinal), railZones = new Dictionary<string, RectTransform>(StringComparer.Ordinal),
            partyArt = new Dictionary<string, RectTransform>(StringComparer.Ordinal); // railZones: the rail's drop zones; partyArt: each member's SD
        RectTransform? partyPanel, partyBar, partyStatus; // partyStatus: the party HP strip, where a party hit shows (it holds no control)
        string? lastScreen, seenBattle;
        int seenHp = -1, seenTurn = -1;
        readonly Dictionary<string, (int Hp, int Block)> seenEnemies = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        readonly HashSet<string> seenHand = new HashSet<string>(StringComparer.Ordinal);
        (string Name, Vector2 From, string? Target, string Mode, string? Owner)? flight, launched; // launched: a sent play's origin; flight: one that committed
        const string PracticeNote = "연습용 탐사 · 진행과 기억은 이 세션에만 있어요. 앱을 닫으면 사라져요.";

        void OnEnable()
        {
            if (Resources.Load<TextAsset>(ClientRunContent.ResourcePath) == null || Resources.Load<TextAsset>("MemoryForge/battle-content") == null) return;
            // HOME-20261019: the length chosen in the home's 출격 준비 (once; a test or scene default stays when nothing was chosen).
            if (run == null && MemoryForgeLobbyClient.NextLength is string chosen) { Length = chosen; MemoryForgeLobbyClient.NextLength = null; }
            if (run == null) StartPractice(); else Render();
        }
        // Disabling drops only the cosmetic summary, the player-action display and their timers, and the unsent intent (never sent);
        // OnEnable redraws the actual Run without restarting or changing it.
        void OnDisable() { RemoveView(); CloseTurnSummary(); CloseActionPresentation(); DropPrepared(IntentDropped); }
        void Update()
        {
            // A canvas whose scale changed (a resized window, another screen or camera) is redrawn, so every measured text is measured at
            // the scale it is drawn at (line breaks follow the pixel size glyphs are generated at). Nothing but the view is rebuilt.
            if (canvasRoot != null && Mathf.Abs(canvasRoot.GetComponent<Canvas>().scaleFactor - renderedScale) > 0.001f) Render();
            FitView();
            if (noticeUntil > 0 && Time.realtimeSinceStartup >= noticeUntil) ExpireNotice();
            if (turnSummary != null && summaryShown < turnSummary.Count) AdvanceTurnSummary(Time.realtimeSinceStartup);
            if (actionRows != null) AdvanceActionPresentation(Time.realtimeSinceStartup);
            else if (prepared != null) DispatchPrepared(); // the Update after the display closed on its timer or by skip
        }
        void OnApplicationPause(bool paused) { if (paused) Leave(); else ResumeView(); }
        void OnApplicationFocus(bool focused) { if (focused) ResumeView(); else Leave(); }
        // Leaving the app (focus loss, pause) drops the unsent intent, so nothing is sent unseen, and then the committed display.
        void Leave()
        {
            if (DropPrepared(IntentDropped)) actionDropped = true;
            DropActionPresentation();
            // Away, a still press never hears its release: void its timer and take away a held (not aimed) card's owner.
            if (holdUid != null && holdUid != dragUid) { holdUid = null; holdTicket++; }
            HidePeek();
            if (backUid != null && backUid != dragUid) HideOwnerBack(backUid, false);
        }
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
            foreach (var character in data.Memory.Battle.Characters) { characterNames[character.Id] = character.Name; characterAttrs[character.Id] = character.Attr; ultimateNames[character.Id] = character.Ult.Name; }
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
            BattleCardDef effective;
            try { effective = run.BattleEffectiveCard(selectedCard); }
            catch (RuleException error) { Reject(error.Code); return false; }
            var requests = ChoiceRequests(effective, selectedMode, data?.Memory.Battle);
            // The sheet (and an intent) keeps the state it is made in; a card that is not a live hand card of the player phase opens nothing.
            var s = run.State;
            var pick = new BattlePick { Uid = selectedCard, Mode = selectedMode, Requests = requests, Rules = ChoiceRules(effective, selectedMode),
                Left = requests.Count == 0 ? 0 : requests[0].Count, Run = run, Node = s.Position, Version = s.Battle?.V ?? -1, Target = target };
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
            Launch(handFaces, uid, name, mode, aim, CardOwner(uid));
            return SendBattle("play", BattleArgument.Object(args), mode == "now" ? name + " 사용" : name + " 예약");
        }
        public bool EndTurn() => SendBattle("end", null, "적의 행동을 마쳤어요.");
        public bool UseUltimate(string actor)
        {
            Launch(ultRects, actor, UltimateFlight, "now", target, actor);
            bool ok = SendBattle("ult", BattleArgument.Object(new[] { Pair("actor", actor), Pair("target", target) }), "필살기를 사용했어요.");
            if (ok) CutIn(actor); // after its redraw, so an ultimate that ends the battle shows it too
            return ok;
        }
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
                Rules = from.Rules.ToArray(), Picks = from.Picks.ToArray(), Run = from.Run, Version = from.Version, Turn = b.Turn };
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
        // card in hand, its target (if any) a living enemy, the same pick requests and effects with every frozen pick, in order, on the
        // sheet's own candidate list (PickCandidates with the effect's rule) given the picks before it, and finally Core's own precheck of
        // exactly this request. Within the list's known scope a pick the reached effect would no longer take is refused here, before Core
        // could replace it by its fallback; fewer picks than requested is an explicit auto, whose rest stays Core's choice.
        string? Unsendable(PreparedPlay p)
        {
            var live = run; var s = live?.State; var b = s?.Battle;
            if (live == null || s == null || b == null || !ReferenceEquals(live, p.Run) || s.Phase != "BATTLE" || b.Phase != "PLAYER" || s.Position != p.Node
                || b.Turn != p.Turn || b.V != p.Version) return IntentStale;
            if (!b.Zones.Hand.Contains(p.Uid)) return "NOT_IN_HAND";
            if (p.Target != null && !b.Enemies.Any(e => e.Uid == p.Target && e.Alive)) return "BAD_TARGET";
            BattleCardDef effective;
            try { effective = live.BattleEffectiveCard(p.Uid); }
            catch (RuleException error) { return error.Code; }
            var requests = ChoiceRequests(effective, p.Mode, data?.Memory.Battle); var rules = ChoiceRules(effective, p.Mode);
            if (!requests.SequenceEqual(p.Requests) || !rules.SequenceEqual(p.Rules) || !PicksHold(b, p.Uid, requests, rules, p.Picks)) return PickInvalid;
            return live.BattleCheck(p.Uid, p.Mode, p.Target, p.Picks);
        }
        // The ordered picks replayed through the sheet's own progression: each on its request's current list given the picks before it
        // (Core's shared BattleChoicePolicy.PicksHold with this view's definitions and Core price).
        bool PicksHold(BattleState b, string source, List<(string Label, string From, int Count, bool GrowthOnly)> requests,
            List<(string Op, string? Part, bool Leading)> rules, IReadOnlyList<string> picks) =>
            BattleChoicePolicy.PicksHold(b, source, requests, rules, picks, cardDefs, Price);
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
            flight = ok && response != null && kind != "end" ? launched : null; launched = null; // only a committed play or ultimate flies
            Render();
            // §14.3: a reservation row's 0.3 s counts from the moment the summary is on screen, not from before its redraw.
            if (ok && kind == "end" && turnSummary != null && summaryRevealAt > 0) summaryRevealAt = Time.realtimeSinceStartup + ReservedRevealSeconds;
            return ok;
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
            var info = HandCardInfo(uid ?? selectedCard);
            return info != null && ShowInfo(info);
        }
        // A hand card's current details (카드 설명 and the long-press peek); null when there is no such card to read.
        CardInfo? HandCardInfo(string? chosen)
        {
            if (recovery != null) return null; // card details read the Run; the failed one is not read
            var live = run;
            var b = live?.State.Battle;
            if (live == null || chosen == null || b == null || !b.Zones.Hand.Contains(chosen)) return null;
            string id = chosen; BattleCardDef def; List<string> modes;
            try { def = live.BattleEffectiveCard(id); modes = live.BattleAllowedModes(id); }
            catch (RuleException error) { Reject(error.Code); return null; }
            b.Cards.TryGetValue(id, out var instance);
            var notes = new List<string>();
            var parts = instance == null ? new List<string>() : new[] { instance.Circuit, instance.Edge }.Concat(instance.Temp).Where(m => !string.IsNullOrEmpty(m)).Select(m => m!).ToList();
            if (parts.Count != 0) notes.Add("붙은 부품\n" + string.Join("\n", parts.Select(PartLine)));
            if (instance != null && def.Uses.GetValueOrDefault() != 0) notes.Add("이번 전투 사용 " + instance.Used + " / " + def.Uses);
            if (instance != null && def.Growth == true) notes.Add("성장 " + instance.Growth);
            notes.Add("적힌 수치는 카드 정의의 값이에요. 약점·상태·유물까지 반영한 예상은 방식을 고르면 행동 안내 줄에 보여요. 적 피해에는 적 방어에 막힌 몫도 들어가요. "
                + "예약의 예상은 지금 상태에서 바로 해결한다고 가정한 값이라, 실제 결과는 해결되는 턴의 상태로 정해져요. 드로우·무작위 결과는 미리 보여 주지 않아요.");
            return new CardInfo
            {
                Title = def.Name + (instance != null && instance.Plus ? "+" : ""),
                Basis = "전투 중 이 카드의 현재 정의" + (instance != null && instance.Plus ? " · 강화(+) 반영" : "") + " · 공명 보정 반영 · 비용은 지금 상태 기준",
                Facts = OwnerText(def.Owner) + " · 등급 " + def.Rarity + "  ·  " + (modes.Count == 0 ? "지금 쓸 수 있는 방식 없음"
                    : string.Join("  ·  ", modes.Select(m => ModeName(m) + " 비용 " + live.BattleCostOf(id, m)))),
                Text = BattleText.Card(def, data!.Memory.Battle), Notes = string.Join("\n\n", notes)
            };
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
        // RULES_NOW_PLAY: every card is played now, so the mode is named only for a (refused) reservation request.
        public string ForecastLine(BattlePlayForecast f, bool autoPicks) => (autoPicks ? "자동 선택 기준 예상" : "예상") + " · " + (f.Mode == "now" ? "" : ModeName(f.Mode) + " · ") + ForecastText(f);
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
                // On the kit's ink pill a tint multiplies the art, so the chosen pace is a light teal wash with light words.
                if (KitPills) { back.color = chosen ? Color.Lerp(Color.white, Memory, 0.55f) : Color.white; caption.color = chosen ? Memory : Ink; }
                else { back.color = chosen ? Memory : Line; caption.color = chosen ? Background : Ink; }
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
        bool KitPills => Kit("ui_button_pill_ink", new Vector4(64, 0, 64, 0)) != null;
        static string PaceName(ActionPacing pace) => pace == ActionPacing.Double ? "2배속" : pace == ActionPacing.Reduced ? "동작 줄이기" : "보통";
        // A ready plain confirm during a display prepares the one unsent intent; otherwise it plays.
        static string ConfirmCaption(bool showing) => showing ? "사용 대기" : "사용 확정";
        // A command control drawn while a display may be open: available only when its own state allows, no display is open and no
        // unsent intent waits for its dispatch.
        T Gate<T>(T control, bool allowed) where T : Selectable
        {
            control.interactable = allowed && actionRows == null && prepared == null; actionGated.Add((control, allowed)); return control;
        }
        void PaceButton(Transform parent, ActionPacing pace, float x, float y, float width, float height, int size, Rect? hit = null)
        {
            var button = Button(parent, "Pace:" + pace, "", x, y, width, height, () => SetActionPace(pace), Line, size, hit: hit);
            paceButtons.Add(((Image)button.targetGraphic, Label(Inner(button), "", 6, 4, width - 12, height - 8, size, Ink, FontStyle.Normal, TextAnchor.MiddleCenter), pace));
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

        // P1-PUBLIC-BATTLE-QUERY §14.3: the pick policy itself is Core's BattleChoicePolicy (moved there unchanged and shared with
        // Battle.HandChoices and the server's battle query). These wrappers keep this view's public signatures; they add no rule.
        /// <summary>BattleChoicePolicy.ChoiceRequests: the display-only projection of prototype/src/ui.js choiceRequests (the immediate
        /// plays whose card picks the player makes); picks left unmade keep Core's own choice, which the overlay states.</summary>
        public static List<(string Label, string From, int Count, bool GrowthOnly)> ChoiceRequests(BattleCardDef card, string mode, BattleData? content = null) =>
            BattleChoicePolicy.ChoiceRequests(card, mode, content);
        /// <summary>BattleChoicePolicy.ChoiceRules: the Core effect each request of ChoiceRequests feeds, in the same order (operation, tune
        /// part, whether it is the card's first top-level effect). Metadata of the given (effective) definition only.</summary>
        public static List<(string Op, string? Part, bool Leading)> ChoiceRules(BattleCardDef card, string mode) => BattleChoicePolicy.ChoiceRules(card, mode);
        /// <summary>BattleChoicePolicy.PickCandidates: the cards a pick sheet offers for one request, in zone order (the list PickSheet
        /// draws, PickCard accepts and a prepared intent revalidates). Inside the known scope only the cards the reached Core effect takes
        /// (a card it cannot tell stays) and none when Core skips the effect; outside it, and without a rule, the zone list, so manual choice
        /// never shrinks for what the client cannot know. price is Core's price of a hand card in mode now (Run.BattleCostOf, or
        /// Run.BattleCostOfWithoutNextFree when its flag is true); a refusal is Core's RuleException.</summary>
        public static List<string> PickCandidates(BattleState b, string source, (string Label, string From, int Count, bool GrowthOnly) request,
            IEnumerable<string> picked, IReadOnlyDictionary<string, BattleCardDef> definitions, (string Op, string? Part, bool Leading)? rule = null,
            Func<string, bool, int>? price = null) => BattleChoicePolicy.PickCandidates(b, source, request, picked, definitions, rule, price);
        /// <summary>BattleChoicePolicy.PickBlocked: PICK_GROWTH_CAP when the battle's growth cap is already reached (Core skips growPick
        /// before choosing). Otherwise null.</summary>
        public static string? PickBlocked(BattleState b, (string Op, string? Part, bool Leading) rule) => BattleChoicePolicy.PickBlocked(b, rule);
        /// <summary>BattleChoicePolicy.EffectEligible: the reached Core effect's own validity predicate for one zone card (true/false), or
        /// null when it cannot be told here (such a card stays offered).</summary>
        public static bool? EffectEligible(BattleState b, (string Op, string? Part, bool Leading) rule, string uid, IReadOnlyDictionary<string, BattleCardDef> definitions,
            Func<string, bool, int>? price) => BattleChoicePolicy.EffectEligible(b, rule, uid, definitions, price);
        List<string> PickOptions(BattleState b, BattlePick pick) => PickCandidates(b, pick.Uid, pick.Requests[pick.Index], pick.Picks, cardDefs, pick.Rules[pick.Index], Price);
        // Core's price of a live hand card in mode now (Run.BattleCostOf), or the same calculation without a remaining "next card costs 0"
        // charge (Run.BattleCostOfWithoutNextFree). A refusal stays Core's RuleException, which the candidate check reads as unknown, never 0.
        int Price(string uid, bool withoutNextFree)
        {
            var live = run ?? throw new RuleException("NO_BATTLE");
            return withoutNextFree ? live.BattleCostOfWithoutNextFree(uid, "now") : live.BattleCostOf(uid, "now");
        }
        // The sheet's opening state still holds: the same Run and battle node, the battle version and target it was opened at, the
        // player phase and its card in hand. Any committed change moves the battle version, so picks made against an older state never
        // send; the target is compared, never replaced.
        bool Fresh(BattlePick pick, RunState s) => ReferenceEquals(run, pick.Run) && s.Phase == "BATTLE" && s.Battle != null && s.Battle.Phase == "PLAYER"
            && s.Position == pick.Node && s.Battle.V == pick.Version && target == pick.Target && s.Battle.Zones.Hand.Contains(pick.Uid);

        // ───────── view ─────────
        // EXPEDITION-UI-20261018: a 1600×900 frame aspect-fit into the safe area. Each place draws Codex's painting for it full-bleed
        // under a navy scrim (battle: the plaza, map: the clock city, every other screen: the workshop as a thematic backdrop; the screen
        // title names the actual place) and puts every rule text on opaque rounded panels, never on detailed scenery. Art and motion are
        // decoration only: no image takes a raycast, a control's hit rectangle never moves (only art, numbers and fades do), and with
        // reduced motion (MemoryForgeUiMotion.Reduced or this practice's 동작 줄이기) every change is instant. The names other code finds
        // (ExpeditionFrame, Enemy:<uid>, Party/Ult:<id>, Hand/Hand:<uid>, Node:<id>, Mode:*, ConfirmCard, EndTurn, Pace:*, Timeline/
        // ReservationSlot, RewardCards/RewardCard:*, SkipCard, Proceed, Memory:*, CommitMemory, …) and every caption stay as they were.
        // EXPEDITION-RESTYLE-20261018: the same layout in a mobile game's visual grammar (reports/expedition_ui_20261018/
        // subculture_ui_reference.md): framed panels with corner ornaments, pills and gems instead of flat boxes, a HUD of chips, the
        // party standing on the stage, enemy nameplates, cards framed in their owner's attribute colour, a round end-turn dial and
        // medal map nodes. Every shape is procedural until Codex's UI kit arrives (art/requests/expedition-restyle-needs-20261018.md).
        void Render()
        {
            if (run == null || data == null || font == null) return;
            // A redraw keeps how the previous canvas was presented (overlay, or a camera that draws it).
            var previous = canvasRoot != null ? canvasRoot.GetComponent<Canvas>() : null;
            var mode = previous != null ? previous.renderMode : RenderMode.ScreenSpaceOverlay; var eye = previous != null ? previous.worldCamera : null;
            RemoveView();
            sheetsBefore.Clear(); sheetsBefore.UnionWith(sheetsDrawn); sheetsDrawn.Clear();
            var canvasObject = new GameObject(OwnedCanvas, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            if (!Application.isPlaying) canvasObject.hideFlags = HideFlags.DontSaveInEditor;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = mode; canvas.worldCamera = eye;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
            scaler.enabled = false; scaler.enabled = true; // apply the scale now, so text measured below wraps as it will be drawn
            renderedScale = canvas.scaleFactor;
            canvasRoot = canvasObject.GetComponent<RectTransform>(); minTouch = TouchUnits();
            var letterbox = Box(canvasRoot, "Letterbox", 0, 0, 0, 0, Background); // fills the bands beside an aspect-fit frame
            letterbox.anchorMin = Vector2.zero; letterbox.anchorMax = Vector2.one; letterbox.offsetMin = letterbox.offsetMax = Vector2.zero;
            frame = Box(canvasRoot, "ExpeditionFrame", 0, 0, 1600, 900, Background);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f); frame.pivot = new Vector2(0.5f, 0.5f); frame.anchoredPosition = Vector2.zero;
            enemyArt.Clear(); enemyRoots.Clear(); enemyBars.Clear(); handFaces.Clear(); ultRects.Clear(); railZones.Clear(); partyArt.Clear(); partyPanel = null; partyBar = null; partyStatus = null;
            string screen = "recovery";
            if (recovery != null) RecoveryView(); // before the header and every normal view: the failed Run may not be renderable
            else
            {
                var s = run.State;
                screen = Finished(s) ? "result" : s.Phase;
                Backdrop(screen);
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
            Animate(screen);
        }

        // The place's painting, aspect-filled, under a light navy scrim, with a shade falling from the top (the header) and one rising
        // from the bottom (in battle, under the timeline, the controls and the hand).
        void Backdrop(string screen)
        {
            string path = screen == "BATTLE" ? "Backgrounds/bg_zone1_battle" : screen == "MAP" ? "Backgrounds/bg_zone1_map" : "Backgrounds/bg_workshop";
            Cover(frame!, "Backdrop", Art(path), 0, 0, 1600, 900, 0.5f);
            float dim = screen == "BATTLE" ? 0.12f : screen == "MAP" ? 0.32f : screen == "VICTORY" || screen == "DEFEAT" || screen == "result" ? 0.58f : 0.46f;
            Box(frame!, "Scrim", 0, 0, 1600, 900, Alpha(Background, dim));
            Deco(frame!, "HeaderShade", Fade(false), 0, 0, 1600, 170, Alpha(Background, 0.9f));
            if (screen == "BATTLE") Deco(frame!, "ScrimLow", Fade(true), 0, ActionsTop - 160, 1600, 1060 - ActionsTop, Alpha(Background, 0.95f));
            else Deco(frame!, "FloorShade", Fade(true), 0, 620, 1600, 280, Alpha(Background, 0.75f));
        }

        // The recovery screen reads nothing from the failed Run: only the session store's version, and one explicit action.
        void RecoveryView()
        {
            var panel = Plate(frame!, "Recovery", 200, 160, 1200, 560, Panel, Danger, 2);
            Label(panel, "이 연습 탐사를 계속할 수 없어요", 40, 36, 1120, 56, 34, Danger, FontStyle.Bold);
            Label(panel, "계산 중 문제가 생겨 탐사를 멈췄어요. 이 탐사의 진행과 보상은 저장하지 않고 버려요.", 40, 120, 1120, 40, 22, Ink);
            Label(panel, store == null ? "저장된 기억을 확인할 수 없어 새로 출발할 수 없어요." : "마지막으로 저장한 기억 덱(기억 버전 " + SessionMemoryVersion + ")은 그대로이고, 새 탐사는 그 덱으로 시작해요.", 40, 170, 1120, 40, 22, Ink);
            Label(panel, "연습 기억은 이 앱 세션에만 있어요. 앱을 닫으면 사라져요.", 40, 220, 1120, 34, 19, Muted);
            if (notice.Length != 0) Label(panel, notice, 40, 290, 1120, 60, 20, Danger); // a failed new departure keeps the quarantine
            Button(panel, "RecoveryDiscard", "이 탐사를 버리고 새로 출발", 40, 440, 560, 80, () => DiscardFailedPractice(), Brass, 26);
        }

        // The top bar: the workshop's brass medal and title, the practice and place lines, and the deck, log and abandon pills on the right.
        void Header(RunState s)
        {
            if (s.Phase == "BATTLE") BattleHeader(s); // RULES_NOW_PLAY: a battle keeps its top for the stage (no logo, no rail)
            else
            {
                var medal = Gem(frame!, "HeaderMedal", 20, 12, 62, Hex("1b2540"), Alpha(Brass, 0.95f));
                Icon(medal, "Glyph", "ico_node_forge", 15, 15, 32, 32, Brass);
                Strong(Label(frame!, "기억 공방", 92, 18, 176, 48, 34, Brass, FontStyle.Bold));
                Label(frame!, "탐사 연습 · " + string.Join("·", ReferenceParty.Select(CharacterName)) + " 기준 파티", 272, 16, 640, 30, 21, Ink);
                Label(frame!, Location(s) + "  ·  " + PhaseName(s), 272, 46, 640, 28, 18, Muted);
            }
            // On a phone their touch areas reach from the top of the frame down (never above it), over the status strip's text. They line
            // up on the frame's right edge; in a battle they are small pills so the stage keeps the top (KAZENA-BATTLE-20261019).
            float reach = Math.Max(minTouch, 72); bool fight = s.Phase == "BATTLE";
            // Each width is already its touch width, so the touch areas tile without overlapping on any phone.
            float by = fight ? 16 : 18, bh = fight ? 46 : 54, gap = fight ? 10 : 16, aw = Math.Max(fight ? 150 : 246, minTouch), lw = Math.Max(fight ? 100 : 120, minTouch),
                dw = Math.Max(fight ? 210 : 240, minTouch); int size = fight ? 19 : 22;
            float ax = 1568 - aw, lx = ax - gap - lw, dx = lx - gap - dw;
            Button(frame!, "DeckButton", "덱 " + s.Deck.Count + "장" + (s.PartsFound.Count != 0 ? " · 부품 " + s.PartsFound.Count : ""), dx, by, dw, bh, () => Open(Sheet.Deck), Line, size, hit: Band(dx, 0, dw, reach));
            Button(frame!, "LogButton", "기록", lx, by, lw, bh, () => Open(Sheet.Log), Line, size, hit: Band(lx, 0, lw, reach));
            if (!Finished(s) && s.Phase != "VICTORY" && s.Phase != "DEFEAT") Button(frame!, "AbandonButton", "탐사 포기", ax, by, aw, bh, () => Open(Sheet.Abandon), Line, size, hit: Band(ax, 0, aw, reach));
        }
        // In a battle the header's left is the party's one shared HP bar with the block gem at its end and the party's statuses under it
        // (KAZENA-BATTLE-20261019: one long bar top left, as the reference's party HP); the place is a small line at the right under the
        // buttons. The piles sit at the hand's two ends (Piles).
        void BattleHeader(RunState s)
        {
            var b = s.Battle!; const float barWidth = 560;
            var info = Box(frame!, "PartyStatus", 32, 12, barWidth + 52, 74, Color.clear); partyStatus = info;
            partyBar = HpBar(info, "PartyHp", "ui_hp_frame_party", 0, 2, barWidth, 38, b.Hp, b.MaxHp, Hex("1f8f7d")); // a deeper teal keeps the numbers legible
            var hpText = Strong(Label(info, "파티 체력 " + b.Hp + " / " + b.MaxHp, 0, 6, barWidth, 30, 20, Ink, FontStyle.Normal, TextAnchor.MiddleCenter));
            hpText.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.6f);
            var gem = Shield(info, barWidth + 6, 0, 42, b.Block > 0 ? Color.white : new Color(1, 1, 1, 0.45f));
            Strong(Label(gem, b.Block.ToString(), 0, 0, 42, 40, 17, Ink, FontStyle.Normal, TextAnchor.MiddleCenter));
            var stats = new List<(string, Color, Color, Color?)>();
            if (b.Counter != 0) stats.Add(("반격 " + b.Counter, Ink, Alpha(Background, 0.8f), LineEdge));
            int debt = b.Debts.Sum(d => d.N);
            if (debt != 0) stats.Add(("부채 " + debt, Danger, Alpha(Background, 0.8f), Alpha(Danger, 0.6f)));
            if (b.EchoActive) stats.Add(("잔향 " + b.Echo + "/" + b.EchoMax, Memory, Alpha(Background, 0.8f), Alpha(Memory, 0.5f))); // only with Rien
            string party = MemoryForgeBattleClient.PartyStatusText(b); // each of the party's statuses as its own brass chip, in Core's order
            if (party.Length != 0) foreach (string buff in party.Split(new[] { " · " }, StringSplitOptions.None)) stats.Add((buff, Brass, Alpha(Brass, 0.14f), Alpha(Brass, 0.6f)));
            Chips(frame!, "Status", stats, 32, 56, 900, 24, 15, 4);
            Label(frame!, Location(s) + " · " + PhaseName(s), 940, 68, 628, 22, 15, Muted, FontStyle.Normal, TextAnchor.MiddleRight);
        }
        // Off the battle: the HP on a framed plate with its gem, coins, relics and parts as chips, the relic names and the practice note.
        void RunStatus(RunState s)
        {
            var panel = Frame(frame!, "RunStatus", 24, 88, 560, 76);
            Gem(panel, "HpGem", 10, 10, 56, Memory, Hex("1d6b63"));
            Strong(Label(panel, "체력 " + s.Hp + " / " + s.MaxHp, 78, 4, 460, 36, 26, Ink, FontStyle.Bold));
            Bar(panel, "HpBar", 78, 46, 460, 14, s.Hp, s.MaxHp, Memory);
            Icon(frame!, "Coin", "ico_node_shop", 604, 92, 30, 30, Brass);
            Chips(frame!, "Wallet", new List<(string, Color, Color, Color?)> { ("동전 " + s.Coins, Brass, Alpha(Background, 0.86f), Alpha(Brass, 0.6f)),
                ("유물 " + s.Relics.Count + "개", Ink, Alpha(Background, 0.86f), LineEdge), ("부품 " + s.PartsFound.Count + "개", Ink, Alpha(Background, 0.86f), LineEdge) }, 640, 92, 1100, 30, 19);
            Grow(Label(frame!, s.Relics.Count == 0 ? "보유 유물 없음" : string.Join(", ", s.Relics.Select(RelicName)), 606, 130, 494, 26, 17, Muted));
            Label(frame!, PracticeNote, 1120, 94, 448, 60, 17, Muted);
        }
        void NoticeLine(string hint)
        {
            string text = notice.Length != 0 ? notice : hint;
            Plate(frame!, "NoticeBar", 24, 824, 1248, 58, Alpha(Background, 0.9f), Alpha(Memory, 0.35f), 1, true);
            Deco(frame!, "NoticeMark", Diamond, 44, 845, 16, 16, LastError != null ? Danger : Memory);
            TrackNotice(Label(frame!, text, 72, 830, 1188, 46, 21, LastError != null ? Danger : notice.Length != 0 ? Memory : Muted, FontStyle.Normal, TextAnchor.MiddleLeft), hint, Muted);
        }

        // Every floor and the boss as columns over the city painting; centres follow the actual rows. A node is a medal (its type's
        // glyph in a ring) over a name ribbon: reachable ones have a brass ring with a breathing halo and take the press at once, the
        // current one a teal ring; floors already passed are dimmed (passed, not "visited": the Run keeps no route history). The glyph
        // and the type word always go together, so colour never carries meaning alone.
        void MapView(RunState s)
        {
            var map = s.Map!; var open = new HashSet<string>(run!.Available(), StringComparer.Ordinal);
            var columns = map.Floors.Select(row => (IReadOnlyList<RunNode>)row).ToList(); columns.Add(new[] { map.Boss });
            float column = Math.Max(176, 1536f / columns.Count), width = column * columns.Count, top = 70, area = 512, nodeWidth = Math.Min(152, column - 24), nodeHeight = Math.Max(108, minTouch);
            var shell = Frame(frame!, "MapPanel", 32, 172, 1536, 648, null, Alpha(Background, 0.42f));
            RectTransform panel = width > 1536 ? Scroll(shell, "Map", 0, 0, 1536, 648, width, 648, Color.clear) : Box(shell, "Map", 0, 0, 1536, 648, Color.clear);
            var centers = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            for (int f = 0; f < columns.Count; f++)
            {
                bool passed = s.Position != null && f < s.Floor && f < columns.Count - 1;
                Strong(Label(panel, f == columns.Count - 1 ? "보스" : (f + 1) + "층", f * column, 18, column, 32, 21, passed ? Alpha(Muted, 0.6f) : f == columns.Count - 1 ? Danger : Muted, FontStyle.Normal, TextAnchor.MiddleCenter));
                for (int i = 0; i < columns[f].Count; i++) centers[columns[f][i].Id] = new Vector2(f * column + column / 2, top + (i + 0.5f) * area / columns[f].Count);
            }
            const float medal = 66;
            var nodes = columns.SelectMany(c => c).ToList();
            foreach (var node in nodes)
                foreach (string next in node.Next ?? new List<string>())
                    if (centers.TryGetValue(next, out var to))
                    {
                        bool route = node.Id == s.Position && open.Contains(next);
                        var from = centers[node.Id] + new Vector2(0, -nodeHeight / 2 + medal / 2); var end = to + new Vector2(0, -nodeHeight / 2 + medal / 2);
                        Connector(panel, from + new Vector2(medal / 2, 0), end - new Vector2(medal / 2, 0), route ? Brass : Alpha(Muted, 0.45f), route ? 5 : 2);
                    }
            foreach (var node in nodes)
            {
                bool here = node.Id == s.Position, reachable = open.Contains(node.Id), passed = !here && s.Position != null && node.Floor < s.Floor && node != map.Boss;
                bool boss = node == map.Boss;
                var c = centers[node.Id];
                var button = Button(panel, "Node:" + node.Id, "", c.x - nodeWidth / 2, c.y - nodeHeight / 2, nodeWidth, nodeHeight, () => EnterNode(node.Id), Color.clear);
                button.interactable = reachable;
                var dim = button.colors; dim.disabledColor = new Color(0.9f, 0.9f, 0.92f); button.colors = dim; // not reachable is a state, not a dead control: the word says so
                var r = Inner(button);
                Color ring = here ? Memory : reachable ? Brass : boss ? Alpha(Danger, 0.8f) : passed ? Alpha(Line, 0.8f) : LineEdge;
                Color ink = here ? Memory : reachable ? Brass : boss ? Danger : passed ? Alpha(Muted, 0.55f) : Ink;
                float mx = nodeWidth / 2 - medal / 2;
                if (here || reachable) { var halo = Deco(r, "Halo", Halo, mx - 22, -22, medal + 44, medal + 44, Alpha(ring, 0.5f)); if (reachable) Breathe(halo.GetComponent<Image>()); }
                var disc = Gem(r, "Medal", mx, 0, medal, passed ? Hex("10172a") : Hex("18223d"), ring);
                Icon(disc, "Glyph", "ico_node_" + node.Type, medal / 2 - 19, medal / 2 - 19, 38, 38, ink);
                var ribbon = Plate(r, "Ribbon", 8, medal - 8, nodeWidth - 16, 28, Alpha(Background, passed ? 0.7f : 0.92f), Alpha(ring, 0.8f), 1, true);
                Label(ribbon, NodeName(node.Type), 0, 0, nodeWidth - 16, 28, 19, passed ? Alpha(Muted, 0.7f) : Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
                if (here || reachable) Strong(Label(r, here ? "현재 위치" : "갈 수 있음", 4, medal + 18, nodeWidth - 8, 26, 17, here ? Memory : Brass, FontStyle.Normal, TextAnchor.MiddleCenter));
            }
            // Legend: every node type with its glyph and word.
            string[] kinds = { "battle", "elite", "boss", "rest", "forge", "shop", "event" };
            var legend = Plate(shell, "MapLegend", 16, 596, 7 * 116 + 24, 40, Alpha(Background, 0.8f), Alpha(Line, 0.9f), 1, true);
            for (int i = 0; i < kinds.Length; i++)
            {
                Icon(legend, "Legend", "ico_node_" + kinds[i], 18 + i * 116, 8, 24, 24, Muted);
                Label(legend, NodeName(kinds[i]), 48 + i * 116, 6, 76, 28, 18, Muted, FontStyle.Normal, TextAnchor.MiddleLeft);
            }
            NoticeLine("밝게 표시된 칸만 갈 수 있어요. 칸을 누르면 바로 들어가요.");
        }

        // ───────── battle ─────────
        // BATTLE-STAGE-20261018 (reports/battle_ui_research_20261018/README.md): the stage takes the middle of the screen and the
        // information sits on the bodies, not on panels. Top band: the place (left), the reservation rail 다음 턴 | 다다음 턴 (centre),
        // the piles and coins (right), with the committed action's rows on a strip under it. Stage: the party on the left on a diagonal,
        // each member its own ultimate button with its gauge, the shared HP bar and the party's statuses at their feet; the enemies on
        // the right, each with its intent over its head and its name, HP, crack, weak points and statuses at its feet. Bottom: the card
        // row (forecast, modes, confirm) over the fanned hand, the action-point gem at its left and the end-turn dial at its right. A
        // hand card can also be dragged up: dropped on an enemy (or, for a card without a target, anywhere on the stage) it plays now,
        // dropped on the rail's 다음 턴 or 다다음 턴 it reserves, through the same selection and confirm a tap takes (DragPlay).
        void BattleView(RunState s)
        {
            var b = s.Battle!; var rules = data!.Memory.Battle.Rules;
            if (!b.Enemies.Any(e => e.Uid == target && e.Alive)) target = null;
            if (target == null && b.Enemies.Count(e => e.Alive) == 1) target = b.Enemies.First(e => e.Alive).Uid;
            if (!b.Zones.Hand.Contains(selectedCard ?? "")) { selectedCard = b.Zones.Hand.FirstOrDefault(); selectedMode = null; }
            // RULES_NOW_PLAY: a card has one way to be played, so choosing it is choosing its mode.
            if (selectedCard != null && selectedMode == null && run!.BattleAllowedModes(selectedCard) is var only && only.Count == 1) selectedMode = only[0];
            // Corner brackets mark every living enemy while the chosen card still needs a target.
            bool aiming = b.Phase == "PLAYER" && prepared == null && selectedCard != null && target == null && run!.BattleNeedsTarget(selectedCard) != null;

            PartyStage(b, rules);
            EnemyStage(b, rules, aiming);
            stageEnd = frame!.childCount; // a held card's owner stands here: over the stage, under the card row, hand and sheets
            if (actionRows != null) ActionStrip(); // only while a committed action plays, over the stage's top
            if (selectedCard != null && b.Phase == "PLAYER" || prepared != null || notice.Length != 0) CardActions(b);
            HandDock(b);
            ApGem(b);
            EndTurnDial(b);
            Piles(b);
        }
        // The draw pile at the hand's left end and the discard pile at its right end, each the kit's stack icon with its count beside it
        // (KAZENA-BATTLE-20261019); the exhausted count, when there is one, under the draw pile. Decoration: no press.
        void Piles(BattleState b)
        {
            bool phone = minTouch > 0; float icon = phone ? 30 : 44, lx = 22 + icon + 2;
            Picture(frame!, "DrawPile", Kit("ui_pile_draw"), 22, HandTop - 2, icon, icon, Color.white);
            Strong(Label(frame!, "뽑을 " + b.Zones.Draw.Count, lx, HandTop + (phone ? 0 : 6), 134 - lx, phone ? 22 : 26, phone ? 14 : 15, Ink, FontStyle.Normal, TextAnchor.MiddleLeft));
            if (b.Zones.Exhaust.Count != 0) Label(frame!, "소멸 " + b.Zones.Exhaust.Count, lx, HandTop + (phone ? 21 : 28), 134 - lx, 20, 13, Muted, FontStyle.Normal, TextAnchor.MiddleLeft);
            Picture(frame!, "DiscardPile", Kit("ui_pile_discard"), 1522, HandTop - 2, 44, 44, Color.white);
            Strong(Label(frame!, "버림 " + b.Zones.Discard.Count, 1448, HandTop + 6, 72, 26, 15, Ink, FontStyle.Normal, TextAnchor.MiddleRight));
        }
        // The battle's bands (frame units, y down). On a phone the card row's controls grow to 48dp and the hand starts lower, so the
        // stage gives up the height; every other band keeps its place.
        const float TopBandY = 100, HandLeft = 136, HandRight = 1370;
        // The action strip's band: under the top band off phones; on a phone below the header's touch band and 48dp tall (pace and skip).
        float StripTop => minTouch > 0 ? Math.Max(150, Math.Max(72, minTouch) + 2) : 150;
        float StripHeight => Math.Max(42, minTouch);
        float StageTop => minTouch > 0 ? Math.Max(TopBandY, Math.Max(72, minTouch) + 6) : TopBandY; // RULES_NOW_PLAY: no rail band
        float HandTop => minTouch > 0 ? 720 : 650; // KAZENA-BATTLE-20261019: larger portrait cards off phones
        float ControlsHeight => Math.Max(56, minTouch);
        float ActionsTop => HandTop - 4 - 34 - ControlsHeight;
        float StageBottom => ActionsTop - 6;
        // The card row's controls: a 56-unit face centred in its touch band (34 units below the row's top, under the forecast line).
        float ControlY => 34 + (ControlsHeight - 56) / 2;
        Rect InfoRect => new Rect(370, ControlY, 150, 56);
        Rect ConfirmRect => new Rect(1318, ControlY, 200, 56);
        Rect? CardRow(float x, float width) => Band(x, 34, width, ControlsHeight);
        // The party is wide enough for one 48dp ultimate per member (440 off phones); the enemy band takes the rest.
        float PartyWidth(BattleState b) => Math.Max(440, 8 + Math.Max(1, b.Actors.Count) * (minTouch + 4));

        // The reservation rail (Timeline): a clock mark, then each time group, 다음 턴 and 다다음 턴, as its label with its counts and its
        // sockets (ReservationSlot) in Core's order, each socket sized to its words. A reserved card's socket is lit with its name, a
        // sealed one is red. Each group's rectangle is also where a dragged card is dropped to reserve it for that time (railZones).
        void Rail(BattleState b, BattleRules rules, float left, float right)
        {
            var cells = MemoryForgeBattleClient.TimelineCells(b, rules);
            string Caption((string Group, string Kind, string? Uid) c) => (c.Group == "next" ? "다음" : "다다음") + "  "
                + (c.Kind == "card" ? run!.BattleEffectiveCard(c.Uid!).Name : c.Kind == "empty" ? "빈 칸" : c.Kind == "sealed" ? "봉인" : "-");
            float Socket((string Group, string Kind, string? Uid) c) => Math.Max(96, TextWidth(Caption(c), 17) + 44); // the kit socket's caps take 19 each
            string Head(string group) => group == "now" ? "지금" : (group == "next" ? "다음 턴 " : "다다음 턴 ") + cells.Count(c => c.Group == group && c.Kind == "card") + "/"
                + cells.Count(c => c.Group == group && (c.Kind == "card" || c.Kind == "empty"));
            // The threat each time meets: the living enemies' attack totals this turn (as their "이번" intent shows it) and next turn
            // (their "다음" intent); the turn after is not known yet.
            // An exposing move counts the attack it leads into, as its intent text shows it.
            int Attack(EnemyMoveDef? m, BattleEnemyState e, bool next) =>
                m == null ? 0 : m.Type == "expose" ? Attack(m.Then, e, next) : m.Type != "atk" && m.Type != "atkdebuff" ? 0
                : MemoryForgeBattleClient.IntentDamage(m.Damage ?? 0, b.DifficultyDamage, next ? 0 : e.Status.Strength, next ? 0 : e.Status.Weak, rules.WeakBps) * Math.Max(1, m.Hits ?? 1);
            int Threat(bool next) => b.Enemies.Where(e => e.Alive).Sum(e => Attack(next ? e.Next : e.Intent, e, next));
            string? ThreatText(string group) => group == "after" ? null : Threat(group == "next") is int t && t > 0 ? "적 공격 " + t : "적 공격 없음";
            float HeadWidth(string group) => Math.Max(TextWidth(Head(group), 16), ThreatText(group) is string d ? TextWidth(d, 14) : 0) + 12;
            string[] groups = { "now", "next", "after" };
            const float gap = 6, h = 58, socket = 42, lead = 42;
            float heads = lead + groups.Sum(g => HeadWidth(g) + 16), sockets = cells.Sum(c => Socket(c) + gap), content = heads + sockets;
            // It never scrolls (on a phone the header's touch band reaches over it): past its room only the sockets narrow, alike, and
            // their captions shrink to fit (best fit); the time labels and threats keep their measured width, so every word stays whole.
            float shown = Math.Min(right - left, content), x0 = left + (right - left - shown) / 2, squeeze = content > shown ? Math.Max(0.3f, (shown - heads - 10) / sockets) : 1;
            var rail = Plate(frame!, "Timeline", x0, TopBandY, shown, h, Alpha(Background, 0.82f), Alpha(Memory, 0.35f), 1, true);
            Clock(rail, "Clock", 14, (h - 22) / 2, 22, Memory);
            RectTransform host = Box(rail, "TimelineSlots", lead, 0, shown - lead, h, Color.clear);
            float x = 0;
            foreach (string group in groups)
            {
                float start = x, head = HeadWidth(group); string? threat = ThreatText(group);
                Label(host, Head(group), x, threat == null ? 0 : 6, head, threat == null ? h : 24, 16, group == "now" ? Brass : Memory, FontStyle.Normal, TextAnchor.MiddleLeft);
                if (threat != null) Label(host, threat, x, 30, head, 22, 14, threat.EndsWith("없음") ? Muted : Danger, FontStyle.Normal, TextAnchor.MiddleLeft);
                x += head;
                foreach (var cell in cells.Where(c => c.Group == group))
                {
                    float w = Socket(cell) * squeeze;
                    Color fill = cell.Kind == "card" ? Alpha(Memory, 0.26f) : cell.Kind == "sealed" ? Alpha(Danger, 0.16f) : Alpha(Background, cell.Kind == "none" ? 0.35f : 0.85f);
                    Color? edge = cell.Kind == "card" ? Memory : cell.Kind == "sealed" ? Danger : cell.Kind == "empty" ? Alpha(Memory, 0.38f) : Alpha(Line, 0.7f);
                    var box = Plate(host, "ReservationSlot", x, (h - socket) / 2, w, socket, fill, edge, cell.Kind == "card" ? 2 : 1, true);
                    var skin = Kit("ui_rail_" + group + "_" + (cell.Kind == "card" ? "reserved" : cell.Kind == "sealed" ? "sealed" : "empty"), new Vector4(44, 0, 44, 0));
                    if (skin != null)
                    {
                        var image = box.GetComponent<Image>(); Sideways(image, skin, 96, socket); image.color = cell.Kind == "none" ? new Color(1, 1, 1, 0.4f) : Color.white;
                        var ring = box.Find("Edge"); if (ring != null) Destroy(ring.gameObject);
                    }
                    Label(box, Caption(cell), 20, 0, w - 40, socket, 17, cell.Kind == "card" ? Ink : cell.Kind == "sealed" ? Danger : Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
                    if (cell.Kind == "card") Sheen(box, w, socket, 0.1f);
                    x += w + gap * squeeze;
                }
                // Each time's rectangle is where a dragged card is dropped for it: 지금 plays now on the chosen target, the others reserve.
                railZones[group] = Box(host, "RailZone:" + group, start - 6, 3, x - start + 4, h - 6, Color.clear);
                x += 16;
            }
        }

        // The party on the stage's left on a diagonal (the front member lowest, nearest the enemies). Each member is its own ultimate
        // button (Ult:<id>, the whole column): its SD over a floor shadow, a name pill edged in its attribute's colour, and the gauge as a
        // ring in that colour, which becomes a glowing brass 필살기 pill once full. At their feet ("PartyStatus", where a party hit flashes) the shared HP bar
        // (PartyHp) with the block gem at its left, and the party's counter, debt, echo and statuses as chips.
        void PartyStage(BattleState b, BattleRules rules)
        {
            float width = PartyWidth(b); int n = Math.Max(1, b.Actors.Count);
            float height = StageBottom - StageTop, column = (width - 8) / n;
            var actors = Box(frame!, "Party", 32, StageTop, width, height, Color.clear); partyPanel = actors;
            Deco(actors, "Floor", Halo, -40, height - 110, width + 80, 130, Alpha(Memory, 0.16f));
            for (int i = 0; i < b.Actors.Count; i++)
            {
                var actor = b.Actors[i]; float x = 4 + i * column; bool ready = actor.Gauge >= rules.GaugeMax; var tone = AttributeColor(actor.Attr);
                var ult = Gate(Button(actors, "Ult:" + actor.Id, "", x, 0, column - 4, height, () => UseUltimate(actor.Id), Color.clear), ready && b.Phase == "PLAYER");
                var face = Inner(ult); ultRects[actor.Id] = face;
                const float caption = 66; float lift = (n - 1 - i) * 24; // back members stand higher
                float side = Math.Min(column * 1.36f, height - caption - lift); // a square SD whose feet stand on the nameplate, back members higher
                var sd = Picture(face, "Portrait:" + actor.Id, Art("Characters/char_" + actor.Id + "_sd"), -column * 0.18f, height - caption - side + 8 - lift, column * 1.36f, side, Color.white);
                if (sd != null) partyArt[actor.Id] = sd.rectTransform;
                // The name on the kit's title ribbon, the attribute glyph at its left (KAZENA-BATTLE-20261019).
                var plate = KitPlate(face, "NamePlate", "ui_ribbon_title", new Vector4(64, 0, 64, 0), 96, 2, height - caption, column - 8, 32, Alpha(Background, 0.9f), Alpha(tone, 0.85f));
                Icon(plate, "Attribute", "ico_attribute_" + actor.Attr, 14, 7, 18, 18, tone);
                Strong(Label(plate, actor.Name, 30, 0, column - 52, 32, 18, Ink, FontStyle.Normal, TextAnchor.MiddleCenter));
                if (ready)
                {
                    var pill = KitPlate(face, "UltReady", "ui_button_pill_brass", new Vector4(64, 0, 64, 0), 128, 2, height - 32, column - 8, 30, Brass, Alpha(Color.white, 0.4f));
                    Strong(Label(pill, "필살기", 0, 0, column - 8, 30, 18, Background, FontStyle.Bold, TextAnchor.MiddleCenter));
                    if (ult.interactable) Breathe(Glow(pill, Brass));
                }
                else
                {
                    // The gauge as a ring that fills clockwise from the top, set in the kit's portrait ring.
                    Deco(face, "GaugeTrack", Pill(3), 8, height - 31, 26, 26, Alpha(Line, 0.95f));
                    var ring = Deco(face, "Gauge", Pill(3), 8, height - 31, 26, 26, tone).GetComponent<Image>();
                    ring.type = Image.Type.Filled; ring.fillMethod = Image.FillMethod.Radial360; ring.fillOrigin = (int)Image.Origin360.Top; ring.fillClockwise = true;
                    ring.fillAmount = rules.GaugeMax > 0 ? Mathf.Clamp01((float)actor.Gauge / rules.GaugeMax) : 0;
                    Picture(face, "GaugeRing", Kit("ui_portrait_ring"), 2, height - 37, 38, 38, Color.white);
                }
            }
        }

        // Enemy slots in Core's order across the stage's right, all of one width, so a defeat changes no slot: a defeated enemy keeps its
        // slot with its name, HP 0 and 처치, and every surviving target stays where it was (only a summon, which adds an enemy, re-lays the
        // band). When even 196-unit slots do not fit, the band scrolls.
        void EnemyStage(BattleState b, BattleRules rules, bool aiming)
        {
            FillPreviewLosses(b);
            float left = 32 + PartyWidth(b) + 20, band = 1568 - left, top = StageTop, height = StageBottom - StageTop; const float gap = 12, narrowest = 196;
            int count = b.Enemies.Count;
            float widest = b.Enemies.Any(e => e.Tier == "boss") ? 480 : 360;
            float width = count == 0 ? 0 : Math.Min(widest, (band - gap * Math.Max(0, count - 1)) / count);
            Transform host = frame!; float x = left, y = top;
            if (count != 0 && width < narrowest)
            {
                width = narrowest;
                host = Scroll(frame!, "Enemies", left, top, band, height, count * narrowest + gap * (count - 1), height, Color.clear); x = 0; y = 0;
            }
            else x += (band - (count * width + gap * Math.Max(0, count - 1))) / 2;
            foreach (var enemy in b.Enemies)
            {
                EnemySlot(host, enemy, b, rules, x, y, width, height, aiming);
                x += width + gap;
            }
        }
        // An enemy stands on the stage: over its head the intent ("이번" this turn, large; "다음" smaller and dim), its painting over a floor
        // shadow (a teal ring under the chosen target), and at its feet the nameplate ("Info"): the name, with Core's tier and the
        // selection as chips in the name row's reserved room (over the painting when the slot is too narrow), the HP bar with its numbers
        // and the block gem, the crack and its pips, the weak points and Core's statuses. Every part is measured; the painting takes what
        // is left (never less than 76), and a nameplate taller than its room scrolls while the intent over the head stays wholly in view.
        void EnemySlot(Transform host, BattleEnemyState enemy, BattleState b, BattleRules rules, float x, float y, float w, float h, bool aiming)
        {
            bool selected = target == enemy.Uid && enemy.Alive;
            var button = Button(host, "Enemy:" + enemy.Uid, "", x, y, w, h, () => SelectEnemy(enemy.Uid), Color.clear);
            button.interactable = enemy.Alive; var root = button.GetComponent<RectTransform>(); enemyRoots[enemy.Uid] = root;
            var sprite = Art("Enemies/enemy_" + enemy.Id);
            Color tier = enemy.Tier == "boss" ? Danger : enemy.Tier == "elite" ? Brass : Memory;
            if (!enemy.Alive)
            {
                var gone = Plate(root, "Info", 0, h - 132, w, 132, Alpha(Background, 0.82f), Alpha(Line, 0.8f));
                var faded = Picture(root, "Art", sprite, 10, 6, w - 20, h - 148, new Color(1, 1, 1, 0.25f));
                if (faded != null) enemyArt[enemy.Uid] = faded.rectTransform;
                float gy = 10 + Grow(Label(gone, enemy.Name, 10, 10, w - 20, 26, 19, Muted, FontStyle.Bold));
                gy += Grow(Label(gone, "체력 " + enemy.Hp + " / " + enemy.MaxHp, 10, gy, w - 20, 22, 17, Muted)) + 6;
                var tag = Plate(gone, "Defeated", 10, gy, 72, 28, Alpha(Danger, 0.18f), Danger, 1, true);
                Label(tag, "처치", 0, 0, 72, 28, 17, Danger, FontStyle.Bold, TextAnchor.MiddleCenter);
                return;
            }
            const float pad = 10; float inner = w - 2 * pad;
            // Over the head (KAZENA-BATTLE-20261019): the kit's intent diamond (sword, shield or special) with the shown attack total in its
            // slot, and beside it the exact two lines (this turn, then the next) on a light ribbon.
            var move = enemy.Intent; int hits = Math.Max(1, move?.Hits ?? 1);
            int total = move == null ? 0 : MemoryForgeBattleClient.IntentDamage(move.Damage ?? 0, b.DifficultyDamage, enemy.Status.Strength, enemy.Status.Weak, rules.WeakBps) * hits;
            string glyph = IntentIcon(move, total);
            const float badgeSide = 58;
            var intentBox = Plate(root, "Intent", badgeSide - 6, 4, w - badgeSide + 4, 48, new Color(0.08f, 0.07f, 0.05f, 0.7f), Alpha(Brass, 0.4f), 1);
            float lines = Grow(Strong(Label(intentBox, "이번  " + MemoryForgeBattleClient.IntentText(enemy.Intent, enemy.Status, b.DifficultyDamage, rules.WeakBps, true)
                + "\n다음  " + MemoryForgeBattleClient.IntentText(enemy.Next, enemy.Status, b.DifficultyDamage, rules.WeakBps, false), 12, 4, w - badgeSide - 18, 40, w >= 260 ? 20 : 17, Brass)));
            intentBox.sizeDelta = new Vector2(w - badgeSide + 4, lines + 8);
            float intentHeight = Math.Max(badgeSide, lines + 12);
            var intentBadge = Picture(root, "IntentBadge", Kit(glyph.StartsWith("ui_intent_attack", StringComparison.Ordinal) ? "ui_badge_intent_attack"
                : glyph.StartsWith("ui_intent_defend", StringComparison.Ordinal) ? "ui_badge_intent_defend" : "ui_badge_intent_special"), 0, 0, badgeSide, badgeSide, Color.white);
            if (intentBadge == null) { if (Picture(intentBox, "IntentMark", Kit(glyph), 2, 4, 40, 40, Color.white) == null) Deco(root, "IntentMark", Diamond, 8, 12, 22, 22, Brass); }
            else if (total > 0) Strong(Label(intentBadge.rectTransform, total.ToString(), 7, 31, 44, 24, 15, Ink, FontStyle.Normal, TextAnchor.MiddleCenter)); // room for 3 digits at any canvas scale
            // Core's tier and the selection as chips: in the name row's room when it leaves the name 120 units, else over the painting.
            float chips = (selected ? 60 : 0) + (enemy.Tier == "boss" ? 62 : enemy.Tier == "elite" ? 78 : 0);
            bool roomy = inner - chips >= 120;
            var info = Plate(root, "Info", 0, h - 100, w, 100, Alpha(Background, 0.6f), selected ? Memory : (Color?)null, selected ? 2 : 0);
            Box(info, "TierLine", 10, 0, w - 20, 3, Alpha(tier, 0.9f)); // the plate's crest in the tier's colour
            float cy = 8;
            cy += Grow(Strong(Label(info, enemy.Name, pad, cy, inner - (roomy ? chips : 0), 28, 20, Ink, FontStyle.Bold))) + 2;
            // The HP bar with its numbers on it, the block gem at its left.
            float barX = pad;
            if (enemy.Block > 0)
            {
                var shield = Shield(info, pad - 2, cy - 6, 32);
                Strong(Label(shield, enemy.Block.ToString(), 0, 0, 32, 30, 15, Ink, FontStyle.Normal, TextAnchor.MiddleCenter)); barX += 32;
            }
            var fill = enemyBars[enemy.Uid] = HpBar(info, "Hp", "ui_hp_frame_enemy", barX, cy - 4, pad + inner - barX, 30, enemy.Hp, enemy.MaxHp, Danger);
            previewLosses.TryGetValue(enemy.Uid, out int taking);
            if (taking > 0) PreviewLoss(fill, enemy.Hp, enemy.MaxHp, taking);
            Strong(Label(info, (taking > 0 ? enemy.Hp + " → " + Math.Max(0, enemy.Hp - taking) : enemy.Hp.ToString()) + " / " + enemy.MaxHp,
                barX, cy - 2, pad + inner - barX, 24, 15, Ink, FontStyle.Normal, TextAnchor.MiddleCenter));
            cy += 28;
            float crack = Grow(Label(info, "균열 " + enemy.Brk + "/" + enemy.BrkMax, pad, cy, 92, 22, 15, Memory));
            float pip = enemy.BrkMax > 0 ? Math.Min(12, (Math.Min(inner, 300) - 96 - 3 * (enemy.BrkMax - 1)) / enemy.BrkMax) : 0, pips = pip >= 4 ? enemy.BrkMax * (pip + 3) : 0;
            if (pip >= 4) // the crack pips beside the number: lit while the value remains (Core counts it down to a break)
                for (int k = 0; k < enemy.BrkMax; k++) Deco(info, "CrackPip", Diamond, pad + 96 + k * (pip + 3), cy + crack / 2 - pip / 2, pip, pip, k < enemy.Brk ? Memory : Alpha(Line, 0.95f));
            // On a wide plate the weak points follow on the crack's row when their words fit there; otherwise on their own row.
            string weakText = "약점 " + string.Join("·", enemy.Weak.Select(Attribute));
            float after = pad + 96 + pips + 14, room = pad + inner - after - enemy.Weak.Count * 22;
            bool sameRow = room > 0 && TextWidth(weakText, 15) <= room;
            if (!sameRow) cy += crack;
            float wx = sameRow ? after : pad;
            foreach (string weak in enemy.Weak) if (Icon(info, "Weak", "ico_attribute_" + weak, wx, cy + 1, 20, 20, AttributeColor(weak)) != null) wx += 22;
            if (!sameRow && TextWidth(weakText, 15) > pad + inner - wx) { cy += 22; wx = pad; } // too long beside the glyphs: one line under them
            cy += Math.Max(sameRow ? crack : 0, Grow(Label(info, weakText, wx, cy, pad + inner - wx, 22, 15, Ink))) + 2;
            // Core's statuses as brass chips.
            string status = MemoryForgeBattleClient.StatusText(enemy, rules);
            var statuses = status.Length == 0 ? new List<(string, Color, Color, Color?)>()
                : status.Split(new[] { " · " }, StringSplitOptions.None).Select(word => (word, Brass, new Color(0.11f, 0.09f, 0.05f, 0.92f), (Color?)Alpha(Brass, 0.7f))).ToList();
            if (statuses.Count != 0) cy += Chips(info, "Status", statuses, pad, cy + 2, pad + inner, 24, 14, 4) + 4;
            float needed = cy + 6, plate = Math.Min(needed, Math.Max(60, h - intentHeight - 4 - 76));
            info.anchoredPosition = new Vector2(0, -(h - plate)); info.sizeDelta = new Vector2(w, plate);
            // The painting's feet stand a little behind the nameplate (drawn over it), so the figure keeps more height.
            Standing(root, enemy.Uid, sprite, 6, intentHeight + 4, w - 12, h - plate - intentHeight + 14, selected, tier);
            Transform badges = roomy ? info : root; float badgeRight = w - (roomy ? pad : 6), badgeY = roomy ? 8 : intentHeight + 6, used = 0;
            if (selected) { used += 60; var chip = Plate(badges, "Selected", badgeRight - 56, badgeY, 56, 28, Memory, null, 1, true); Label(chip, "선택", 0, 0, 56, 28, 17, Background, FontStyle.Bold, TextAnchor.MiddleCenter); }
            if (enemy.Tier == "boss" || enemy.Tier == "elite")
            {
                float tw = enemy.Tier == "boss" ? 58 : 74; used += tw + 4;
                var badge = Plate(badges, "Tier", badgeRight - used + 4, badgeY, tw, 28, Alpha(Background, 0.9f), tier, 1, true);
                Label(badge, enemy.Tier == "boss" ? "보스" : "엘리트", 0, 0, tw, 28, 17, tier, FontStyle.Bold, TextAnchor.MiddleCenter);
            }
            if (needed > plate) Overflow(info, needed, plate, info);
            if (selected || aiming) Brackets(root, w, h, selected ? Memory : Ink, !selected);
        }
        // An enemy's painting standing in its box: a soft aura of its tier behind it, a floor shadow (a teal ring when it is the target)
        // under it. The painting is the art the hit motions move.
        void Standing(RectTransform root, string uid, Sprite? sprite, float x, float y, float w, float h, bool selected, Color tier)
        {
            var aura = Deco(root, "Aura", Halo, x - w * 0.1f, y + h * 0.05f, w * 1.2f, h * 0.9f, Alpha(tier, 0.16f)); aura.SetSiblingIndex(1);
            var floor = Deco(root, "Floor", Halo, x + w * 0.1f, y + h - 30, w * 0.8f, 36, selected ? Alpha(Memory, 0.75f) : new Color(0, 0, 0, 0.6f)); floor.SetSiblingIndex(2);
            var art = Picture(root, "Art", sprite, x, y, w, h, Color.white);
            if (art != null) { art.rectTransform.SetSiblingIndex(3); enemyArt[uid] = art.rectTransform; }
        }
        // The kit's block shield (a blue gem without it), a decoration the block number is written on.
        RectTransform Shield(Transform parent, float x, float y, float size, Color? tint = null)
        {
            var art = Picture(parent, "BlockGem", Kit("ui_shield_block"), x, y, size, size, tint ?? Color.white);
            return art != null ? art.rectTransform : Gem(parent, "BlockGem", x, y, size, Hex("5b8bd6"), Hex("1c2a48"));
        }
        // An HP bar on the kit's frame: the frame behind, the track and fill in its inner rectangle ([20,14,216,12] of 256×40, scaled to the
        // given height); the procedural bar alone, centred in that height, without the art. Returns the fill (a hit drains behind it).
        // Core's forecast of the card row's request (a waiting intent's, else the chosen card's), split per enemy
        // (BattlePlayForecast.EnemyHpLosses): each enemy's HP bar lights the part that request takes. Nothing with an error or a hidden
        // outcome (a draw or a random choice is never revealed). Recomputed on Render only, like the forecast line.
        readonly Dictionary<string, int> previewLosses = new Dictionary<string, int>(StringComparer.Ordinal);
        void FillPreviewLosses(BattleState b)
        {
            previewLosses.Clear();
            var intent = prepared;
            var f = intent != null ? run!.BattleForecast(intent.Uid, intent.Mode, intent.Target, intent.Picks)
                : selectedCard != null && selectedMode != null && b.Phase == "PLAYER" ? run!.BattleForecast(selectedCard, selectedMode, target) : null;
            if (f == null || f.Error != null || f.Hidden) return;
            foreach (var loss in f.EnemyHpLosses) previewLosses[loss.Uid] = loss.HpLoss;
        }
        // The HP the forecast takes, lit over the bar's fill from what remains up to the current HP.
        void PreviewLoss(RectTransform fill, int hp, int max, int taking)
        {
            var back = (RectTransform)fill.parent; float w = back.sizeDelta.x, k = max > 0 ? w / max : 0;
            float from = k * Math.Max(0, hp - taking), to = k * Math.Min(hp, max);
            if (to > from) Plate(back, "HpPreview", from, 0, to - from, back.sizeDelta.y, new Color(1, 1, 1, 0.55f), null, 1, true);
        }
        RectTransform HpBar(Transform parent, string name, string frameId, float x, float y, float width, float height, int value, int max, Color color)
        {
            var art = Kit(frameId, new Vector4(20, 0, 20, 0));
            if (art == null) return Bar(parent, name, x, y + (height - 20) / 2, width, 20, value, max, color);
            float k = height / 40f;
            Sideways(Box(parent, name + "Frame", x, y, width, height, Color.white).GetComponent<Image>(), art, 40, height);
            return Bar(parent, name, x + 20 * k, y + 14 * k, width - 40 * k, 12 * k, value, max, color);
        }
        // A nameplate whose measured details are taller than their room keeps every line: its children (all but its edge and the
        // pinned intent badge) move into a vertical scroll of the given height at its top.
        void Overflow(RectTransform info, float needed, float height, Transform keep)
        {
            var children = info.Cast<Transform>().Where(child => child.name != "Edge" && child != keep).ToList();
            var content = Scroll(info, "InfoScroll", 0, 0, info.sizeDelta.x, height, info.sizeDelta.x, needed, Color.clear);
            foreach (var child in children) child.SetParent(content, false);
        }
        // Four corner brackets around a slot (selected: teal; still to choose: ink, breathing).
        void Brackets(RectTransform root, float w, float h, Color color, bool breathe)
        {
            const float arm = 26, t = 4;
            var group = Box(root, "Brackets", 0, 0, w, h, Color.clear);
            foreach (var (bx, by, horizontal) in new[] { (0f, 0f, true), (0f, 0f, false), (w - arm, 0f, true), (w - t, 0f, false), (0f, h - t, true), (0f, h - arm, false), (w - arm, h - t, true), (w - t, h - arm, false) })
            {
                var edge = Box(group, "Bracket", bx, by, horizontal ? arm : t, horizontal ? t : arm, color);
                if (breathe) Breathe(edge.GetComponent<Image>());
            }
        }
        // Chips laid left to right from x, wrapping before `right` (rows `gap` apart); returns the height used.
        float Chips(Transform parent, string name, IEnumerable<(string Text, Color Ink, Color Fill, Color? Edge)> words, float x, float y, float right, float height, int size, float gap = 6)
        {
            float cx = x, cy = y; bool any = false;
            foreach (var (text, ink, fill, edge) in words)
            {
                float w = Math.Min(right - x, ChipWidth(text, height, size));
                if (any && cx + w > right) { cx = x; cy += height + gap; }
                var chip = Plate(parent, name, cx, cy, w, height, fill, edge, 1, true);
                var glyph = StatusIcon(text); float lead = glyph != null ? height * 0.8f : 0;
                if (glyph != null) Picture(chip, "StatusIcon", glyph, height * 0.3f, height * 0.1f, height * 0.8f, height * 0.8f, Color.white);
                Label(chip, text, lead, 0, w - lead, height, size, ink, FontStyle.Normal, TextAnchor.MiddleCenter);
                cx += w + gap; any = true;
            }
            return any ? cy - y + height : 0;
        }
        float ChipWidth(string text, float height, int size) => TextWidth(text, size) + height * 0.8f + 6 + (StatusIcon(text) != null ? height * 0.8f : 0);
        // Codex's status glyph for a status chip (by the word Core's text starts with), drawn before the words; none for other chips.
        static readonly (string Prefix, string Icon)[] StatusIcons = { ("파티 약화 ", "ui_status_weak"), ("취약 ", "ui_status_vulnerable"), ("약화 ", "ui_status_weak"),
            ("표식 ", "ui_status_mark"), ("화상 ", "ui_status_burn"), ("힘 ", "ui_status_strength"), ("반사 ", "ui_status_reflect"), ("방어 ", "ui_status_block"),
            ("반격", "ui_status_counter"), ("부채 ", "ui_status_debt"), ("잔향 ", "ui_status_echo") };
        Sprite? StatusIcon(string text)
        {
            foreach (var (prefix, icon) in StatusIcons) if (text.StartsWith(prefix, StringComparison.Ordinal)) return Kit(icon);
            return null;
        }
        // The height Chips would use for these words in this width (nothing drawn).
        float ChipsHeight(IEnumerable<(string Text, Color, Color, Color?)> words, float width, float height, int size, float gap = 6)
        {
            float cx = 0; int rows = 0; bool any = false;
            foreach (var word in words)
            {
                float w = Math.Min(width, ChipWidth(word.Text, height, size));
                if (any && cx + w > width) { cx = 0; rows++; }
                cx += w + gap; any = true;
            }
            return any ? (rows + 1) * height + rows * gap : 0;
        }
        float ChipsWidth(IEnumerable<(string Text, Color, Color, Color?)> words, float height, int size) => words.Sum(word => ChipWidth(word.Text, height, size) + 6) - 6;
        // A small clock face (a ring and two hands): the timeline's mark.
        void Clock(Transform parent, string name, float x, float y, float size, Color color)
        {
            var face = Deco(parent, name, Pill(7), x, y, size, size, color);
            Box(face, "ClockHand", size / 2 - 1, size * 0.22f, 2, size * 0.3f, color);
            Box(face, "ClockHand", size / 2 - 1, size / 2 - 1, size * 0.3f, 2, color);
        }

        // The hand: one card per hand uid in order, each wide enough for its whole effect text (Core's BattleText.Card of the battle's
        // effective definition) or, past the widest card, pointing to 카드 설명 instead of cutting it. A card is a frame in its owner's
        // attribute colour: the owner's art window, the cost gem, a name plate, the effect and a mode chip. The cards fan out about the
        // hand's centre (tilted and lowered towards the ends, a short hand centred); the chosen one stands straight and lifted. The
        // button's own hit rectangle never moves or turns, so neighbours never share a press; its face ("Card") is the part that deals
        // in, tilts, lifts and sinks while pressed. Dragged up, a card is played on its drop (CardDrag); sideways drags scroll the hand.
        void HandDock(BattleState b)
        {
            // KAZENA-BATTLE-20261019: every card is the same portrait shape (the kit's card frame, 360×520), so the row lines up. The art
            // window takes the top half down to the frame's divider; the name and the effect sit under it. An effect that does not fit
            // in three short lines points to 카드 설명 instead of being cut. The dock reaches the frame's bottom and the cards leave room
            // under them for the fan's drop and tilt, so no fanned face is ever cut by the dock's mask.
            float height = 900 - HandTop, cardHeight = height - 16 - FanDrop - 8, width = Math.Max(Mathf.Round(cardHeight * 0.72f), Mathf.Ceil(minTouch)), // never under 48dp
                artHeight = Mathf.Round(cardHeight * 0.46f), nameTop = artHeight + 2, textTop = nameTop + 24, textHeight = cardHeight - textTop - 8;
            int textSize = cardHeight >= 170 ? 14 : 13;
            var plan = new List<(string Uid, BattleCardDef Def, string Text, bool Fits)>();
            foreach (string uid in b.Zones.Hand)
            {
                var def = run!.BattleEffectiveCard(uid); string text = BattleText.Card(def, data!.Memory.Battle);
                plan.Add((uid, def, text, TextHeight(text, textSize, width - 16) <= textHeight));
            }
            const float gap = 8;
            float content = plan.Count * (width + gap), view = HandRight - HandLeft;
            // The arc the hand sits on: a soft teal glow under the row (the reference's lit curve), drawn before the cards.
            Deco(frame!, "HandArc", Halo, HandLeft + view * 0.08f, 900 - 46, view * 0.84f, 70, Alpha(Memory, 0.22f));
            var dock = Scroll(frame!, "Hand", HandLeft, HandTop, view, height, content, height, Color.clear);
            var scroll = dock.parent.GetComponent<ScrollRect>();
            float x = Math.Max(0, (view - content) / 2), mid = (plan.Count - 1) / 2f; int index = 0;
            foreach (var (uid, def, text, fits) in plan)
            {
                bool selected = uid == selectedCard;
                var modes = run!.BattleAllowedModes(uid); string first = modes.FirstOrDefault() ?? "now";
                bool poor = modes.Count != 0 && modes.All(m => run.BattleCheck(uid, m, target) == "AP_LOW"); // Core's own refusal, not a local rule
                // The root covers the dock's whole height: a face dealing in or fanned low never leaves its own press area.
                var button = Button(dock, "Hand:" + uid, "", x, 0, width, height, () => SelectCard(uid), Color.clear);
                var tone = OwnerColor(def.Owner);
                float offset = index++ - mid, arc = selected ? 0 : Math.Min(FanDrop, Math.Abs(offset) * 4), tilt = selected ? 0 : Mathf.Clamp(-offset * 2.5f, -5, 5);
                var face = Plate(button.transform, "Card", 0, (selected ? 2 : 14) + arc, width, cardHeight, Hex("0f1628"), null, 0);
                if (tilt != 0) Tilt(face, tilt);
                button.targetGraphic = face.GetComponent<Image>();
                button.GetComponent<UiPress>()?.Bind(face, () => Calm);
                if (Application.isPlaying) button.gameObject.AddComponent<CardDrag>().Bind(this, uid, scroll);
                if (selected) Glow(face, Alpha(Memory, 0.6f));
                handFaces[uid] = face;
                // The kit's frame in the owner's attribute (brass for common cards) is the card's plate; its divider is the art's lower edge.
                string attr = characterAttrs.TryGetValue(def.Owner, out var a) ? a : "common";
                var frameArt = Picture(face, "Frame", Kit("ui_card_frame_" + attr), 0, 0, width, cardHeight, selected ? Hex("d8fff8") : Color.white);
                if (frameArt == null) Plate(face, "Edge", 0, 0, width, cardHeight, Color.clear, selected ? Memory : Alpha(tone, 0.9f), selected ? 3 : 2);
                else frameArt.preserveAspect = false; // the card and the frame share the portrait shape; fill it exactly
                var art = Box(face, "ArtWindow", 6, 6, width - 12, artHeight - 6, Color.clear); art.gameObject.AddComponent<RectMask2D>();
                // A weapon or common card shows the art of the member who holds it (its wielder or assignee), tagged 무기/공용.
                string painted = def.Owner;
                if (!characterAttrs.ContainsKey(painted) && b.Cards.TryGetValue(uid, out var held0)) painted = held0.Wielder ?? held0.Assigned ?? painted;
                OwnerBand(art, painted, width - 6, artHeight - 3);
                if (painted != def.Owner)
                {
                    string kind = OwnerText(def.Owner); float kw = TextWidth(kind, 13) + 26;
                    var kindTag = Plate(face, "Kind", width - 8 - kw, artHeight - 28, kw, 22, Alpha(Background, 0.85f), Alpha(tone, 0.8f), 1, true);
                    Label(kindTag, kind, 0, 0, kw, 22, 13, Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
                }
                Deco(face, "BandFade", Fade(true), 6, artHeight - 26, width - 12, 22, Alpha(Hex("0f1628"), 0.9f));
                Strong(Label(face, def.Name + (b.Cards.TryGetValue(uid, out var instance) && instance.Plus ? "+" : ""), 6, nameTop, width - 12, 24, 17, Ink, FontStyle.Bold, TextAnchor.MiddleCenter));
                var body = Label(face, fits ? text : LongEffect, 8, textTop, width - 16, textHeight, textSize, fits ? Ink : Muted, FontStyle.Normal, TextAnchor.UpperCenter);
                body.resizeTextForBestFit = false;
                var costArt = Picture(face, "Cost", Kit("ui_gem_cost"), -2, -2, 42, 42, poor ? new Color(0.5f, 0.5f, 0.58f) : Color.white);
                var cost = costArt != null ? costArt.rectTransform : Gem(face, "Cost", 2, 2, 36, poor ? Hex("4a5677") : Brass, poor ? Hex("262e45") : Hex("7a5a22"));
                float gemSide = cost.sizeDelta.x;
                Strong(Label(cost, run.BattleCostOf(uid, first).ToString(), 0, 0, gemSide, gemSide, 22, poor ? Danger : costArt != null ? Ink : Background, FontStyle.Bold, TextAnchor.MiddleCenter));
                float tagY = 6;
                if (selected) { var chip = Plate(face, "Selected", width - 48, tagY, 42, 22, Memory, null, 1, true); Label(chip, "선택", 0, 0, 42, 22, 14, Background, FontStyle.Bold, TextAnchor.MiddleCenter); tagY += 26; }
                // RULES_NOW_PLAY: a 보존 card (keyword or part) says so; one kept over a turn end glows brass.
                bool held = b.Cards.TryGetValue(uid, out var kept) && kept.Held;
                if (held || run.BattleRetains(uid))
                {
                    string tag = held ? "보존 중" : "보존";
                    float tw = Math.Min(width - 12, TextWidth(tag, 13) + 16);
                    var tagChip = Plate(face, "Retain", width - 6 - tw, tagY, tw, 22, held ? Alpha(Brass, 0.9f) : Alpha(Background, 0.85f), Brass, 1, true);
                    Label(tagChip, tag, 0, 0, tw, 22, 13, held ? Background : Brass, FontStyle.Normal, TextAnchor.MiddleCenter);
                }
                if (held && !selected) Glow(face, Alpha(Brass, 0.45f));
                x += width + gap;
            }
        }
        const float FanDrop = 12; // the outermost card's drop; a 5° tilt lowers a 184-wide card's corner by 8 more, inside the dock's last 8
        // Turns a top-left-pivoted face by `angle` degrees about its bottom centre (a fanned card); the result is its layout position.
        static void Tilt(RectTransform face, float angle)
        {
            var size = face.sizeDelta; var foot = new Vector3(size.x / 2, -size.y, 0); var turn = Quaternion.Euler(0, 0, angle);
            face.localRotation = turn; face.anchoredPosition += (Vector2)(foot - turn * foot);
        }
        // The owner's approved cut-in as the card's art window (decorative: it does not depict the card's effect); other party members
        // show their SD's face; common, curse and weapon cards keep a plain window with the word.
        const string LongEffect = "전체 효과는 '카드 설명'에서";
        void OwnerBand(RectTransform face, string owner, float width, float height)
        {
            var cutin = Art("Characters/char_" + owner + "_cutin", true);
            if (cutin != null) { Cover(face, "Band", cutin, 3, 3, width - 6, height, 0.12f); return; }
            var band = Box(face, "Band", 3, 3, width - 6, height, Alpha(OwnerColor(owner), 0.18f)); band.gameObject.AddComponent<RectMask2D>();
            var sd = Art("Characters/char_" + owner + "_sd", true);
            if (sd != null) Picture(band, "Art", sd, width - 6 - 120, -16, 120, 120, Color.white);
            else Label(band, OwnerText(owner), 0, (height - 26) / 2, width - 18, 26, 17, Muted, FontStyle.Normal, TextAnchor.MiddleRight);
        }
        // The end of the turn: a round brass dial (rim, face, gloss and twelve hour ticks) over the whole bottom-right touch area. It
        // glows while there is nothing left to pay for, the turn's natural next step.
        void EndTurnDial(BattleState b)
        {
            float left = HandRight + 6, w = 1568 - left, h = 892 - HandTop, d = Math.Min(w, h) - 36, cx = left + w / 2, cy = HandTop + h / 2 + 12;
            var end = Gate(Button(frame!, "EndTurn", "", cx - d / 2, cy - d / 2, d, d, () => EndTurn(), Color.clear, 32, hit: new Rect(left, HandTop, w, 900 - HandTop)), b.Phase == "PLAYER");
            var face = Inner(end); var disc = face.GetComponent<Image>(); var kit = Kit("ui_button_endturn");
            if (kit != null)
            {
                // The kit dial with its own pressed and disabled faces (swapped by the button); the label sits in its centre circle.
                disc.sprite = kit; disc.type = Image.Type.Simple; disc.color = Color.white;
                end.transition = Selectable.Transition.SpriteSwap;
                end.spriteState = new SpriteState { pressedSprite = Kit("ui_button_endturn_pressed"), disabledSprite = Kit("ui_button_endturn_disabled"), highlightedSprite = kit, selectedSprite = kit };
                Strong(Label(face, b.Phase == "PLAYER" ? "턴 종료\n" + b.Turn + "턴" : "전투 종료", d * 0.16f, d * 0.24f, d * 0.68f, d * 0.52f, (int)Math.Min(24, d * 0.15f), Ink, FontStyle.Bold, TextAnchor.MiddleCenter));
                if (end.interactable && b.Ap == 0) Breathe(Glow(face, Brass));
                return;
            }
            disc.sprite = Pill(0); disc.type = Image.Type.Simple; disc.color = Hex("8a6a2c");
            Deco(face, "Dial", Pill(0), d * 0.06f, d * 0.06f, d * 0.88f, d * 0.88f, Brass);
            Deco(face, "Inner", Pill(4), d * 0.14f, d * 0.14f, d * 0.72f, d * 0.72f, Alpha(Hex("5a4318"), 0.7f));
            for (int k = 0; k < 12; k++)
            {
                float angle = k * 30 * Mathf.Deg2Rad, radius = d * 0.39f;
                var tick = Box(face, "Tick", 0, 0, 3, k % 3 == 0 ? 10 : 6, Alpha(Hex("3b2c10"), 0.8f));
                tick.pivot = new Vector2(0.5f, 0.5f); tick.anchoredPosition = new Vector2(d / 2 + Mathf.Sin(angle) * radius, -(d / 2 - Mathf.Cos(angle) * radius));
                tick.localRotation = Quaternion.Euler(0, 0, -k * 30);
            }
            Deco(face, "Gloss", Pill(0), d * 0.24f, d * 0.1f, d * 0.52f, d * 0.24f, new Color(1, 1, 1, 0.24f));
            Strong(Label(face, b.Phase == "PLAYER" ? "턴 종료\n" + b.Turn + "턴" : "전투 종료", d * 0.12f, d * 0.2f, d * 0.76f, d * 0.6f, 26, Background, FontStyle.Bold, TextAnchor.MiddleCenter));
            if (end.interactable && b.Ap == 0) Breathe(Glow(face, Brass));
        }
        // The action points on a big brass gem at the hand's left (decoration: Core's number), dimmed at 0.
        void ApGem(BattleState b)
        {
            const float d = 104, x = 18; float y = minTouch > 0 ? HandTop + 46 : HandTop + Math.Max(4, (892 - HandTop - d - 24) / 2); // a phone keeps the pile row above it
            Deco(frame!, "ApGlow", Halo, x - 22, y - 22, d + 44, d + 44, Alpha(Brass, b.Ap > 0 ? 0.5f : 0.15f));
            var art = Picture(frame!, "ApGem", Kit("ui_gem_ap"), x - 8, y - 8, d + 16, d + 16, b.Ap > 0 ? Color.white : new Color(0.55f, 0.55f, 0.6f));
            var gem = art != null ? art.rectTransform : Gem(frame!, "ApGem", x, y, d, b.Ap > 0 ? Brass : Hex("6b5a3a"), Hex("7a5a22"));
            float side = gem.sizeDelta.x;
            Strong(Label(gem, b.Ap.ToString(), 0, 0, side, side, 50, art != null ? Ink : Background, FontStyle.Bold, TextAnchor.MiddleCenter));
            Strong(Label(frame!, "행동력  " + b.Ap, x - 8, y + d, d + 16, 24, 16, Brass, FontStyle.Normal, TextAnchor.MiddleCenter));
        }
        // The card row over the hand: the forecast (or the last command's notice) of exactly the request the confirm sends, then the
        // chosen card and its target, 카드 설명, one button per mode Core allows (cost and timing, or Core's refusal) and the confirm; a
        // waiting intent shows its own request and cancel instead (PreparedControls). On a phone the controls' touch band is 48dp tall.
        void CardActions(BattleState b)
        {
            var actions = Plate(frame!, "CardActions", 32, ActionsTop, 1536, 34 + ControlsHeight, Alpha(Background, 0.66f), null, 0);
            Box(actions, "Rule", 24, 0, 1488, 1, Alpha(Brass, 0.5f));
            string hint = ""; var hintColor = Muted; // RULES_NOW_PLAY: no standing tutorial line (the first-run guide teaches)
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
            TrackNotice(Label(actions, notice.Length != 0 ? notice : hint, 18, 4, 1498, 30, 20, notice.Length != 0 ? (LastError == null ? Muted : Danger) : hintColor), hint, hintColor);
            float y = ControlY;
            if (intent != null) PreparedControls(actions, b, intent);
            else if (selectedCard != null && b.Phase == "PLAYER")
            {
                string uid = selectedCard; var def = run!.BattleEffectiveCard(uid);
                Deco(actions, "Pointer", Diamond, 16, y + 21, 16, 16, Memory);
                Strong(Label(actions, def.Name + " → " + (b.Enemies.FirstOrDefault(e => e.Uid == target)?.Name ?? (run.BattleNeedsTarget(uid) == null ? "파티" : "적 선택")), 40, y, 318, 56, 23, Ink, FontStyle.Bold, TextAnchor.MiddleLeft));
                Button(actions, "CardInfoButton", "카드 설명", InfoRect.x, InfoRect.y, InfoRect.width, InfoRect.height, () => ShowHandCardInfo(uid), Line, 21, hit: CardRow(InfoRect.x, InfoRect.width));
                // RULES_NOW_PLAY: one way to play a card, so no mode buttons; the cost sits on the card and in the forecast.
                string? reason = selectedMode == null ? "MODE" : run.BattleCheck(uid, selectedMode, target);
                bool plain = reason == null && ChoiceRequests(def, selectedMode!, data!.Memory.Battle).Count == 0;
                string confirm = reason == null ? (plain ? ConfirmCaption(actionRows != null) : "카드 고르기")
                    : reason == "MODE" ? "방식 선택" : reason == "BAD_TARGET" || reason == "FIXED_MARK_REQUIRED" ? "대상 선택" : ErrorText(reason);
                // Not gated by the display: during one it confirms the single unsent intent (or opens its sheet) and spends nothing.
                var button = Button(actions, "ConfirmCard", confirm, ConfirmRect.x, ConfirmRect.y, ConfirmRect.width, ConfirmRect.height, () => ConfirmCard(), reason == null ? Brass : Line, 22,
                    hit: CardRow(ConfirmRect.x, ConfirmRect.width));
                button.interactable = reason == null;
                if (plain) confirmCaption = button.GetComponentInChildren<Text>();
                if (reason == null && actionRows == null) Breathe(Glow(Inner(button), Alpha(Brass, 0.8f))); // ready: the next step glows
            }
        }
        // §14.3 행동 연출 (provisional text, no final VFX/audio): the committed action's rows step in on a strip under the top band, apart
        // from the card row, so the next card's forecast stays readable while they play. Pace and skip sit at the strip's right end
        // (the header's left belongs to the first-run guide); on a phone each is 48dp both ways inside the strip's own band.
        void ActionStrip()
        {
            float band = StripTop, tall = StripHeight, y = band + (tall - 42) / 2;
            var strip = Plate(frame!, "ActionPresentation", 32, y, 1536, 42, Alpha(Background, 0.8f), Alpha(Memory, 0.3f), 1, true);
            float skip = Math.Max(112, minTouch), slow = Math.Max(150, minTouch), pace = Math.Max(100, minTouch);
            float skipX = 1562 - skip, slowX = skipX - 8 - slow, doubleX = slowX - 6 - pace, normalX = doubleX - 6 - pace, faceY = band + (tall - 38) / 2;
            actionStatus = Label(strip, "", 20, 3, 222, 36, 18, Memory, FontStyle.Bold);
            actionText = Label(strip, "", 248, 0, normalX - 32 - 10 - 248, 42, 16, Ink);
            Plate(frame!, "PaceGroup", normalX - 3, faceY - 2, slowX + slow - normalX + 6, 42, Alpha(Background, 0.9f), LineEdge, 1, true);
            PaceButton(frame!, ActionPacing.Normal, normalX, faceY, pace, 38, 17, Band(normalX, band, pace, tall));
            PaceButton(frame!, ActionPacing.Double, doubleX, faceY, pace, 38, 17, Band(doubleX, band, pace, tall));
            PaceButton(frame!, ActionPacing.Reduced, slowX, faceY, slow, 38, 17, Band(slowX, band, slow, tall));
            actionSkip = Button(frame!, "SkipActionPresentation", "건너뛰기", skipX, faceY, skip, 38, () => SkipActionPresentation(), Line, 17, hit: Band(skipX, band, skip, tall));
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
            Label(actions, BattleCardName(intent.Uid) + " → " + aim, 18, ControlY, 340, 56, 23, Ink, FontStyle.Bold, TextAnchor.MiddleLeft);
            Button(actions, "CancelPreparedIntent", "대기 취소", InfoRect.x, InfoRect.y, InfoRect.width, InfoRect.height, () => CancelPreparedIntent(), Line, 22, hit: CardRow(InfoRect.x, InfoRect.width));
            var waiting = Plate(actions, "PreparedIntent", 532, ControlY, 774, 56, Alpha(Brass, 0.12f), Alpha(Brass, 0.6f));
            Label(waiting, PreparedText(intent), 10, 2, 754, 52, 18, Brass);
            if (b.Zones.Hand.Contains(intent.Uid)) Button(actions, "CardInfoButton", "카드 설명", ConfirmRect.x, ConfirmRect.y, ConfirmRect.width, ConfirmRect.height, () => ShowHandCardInfo(intent.Uid), Line, 21,
                hit: CardRow(ConfirmRect.x, ConfirmRect.width));
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
            float row = Math.Max(54, minTouch), pitch = row + 10;
            var content = Scroll(p, "PickList", 24, 104, 952, 336, 952, ((list.Count + 2) / 3) * pitch, Alpha(Background, 0.6f));
            for (int i = 0; i < list.Count; i++)
            {
                string uid = list[i]; string zone = b.Zones.Hand.Contains(uid) ? "손패" : b.Zones.Discard.Contains(uid) ? "버림" : "예약";
                string caption = BattleCardName(uid) + " · " + zone;
                if (last) caption += "\n" + ForecastOutcome(run!.BattleForecast(pick.Uid, pick.Mode, target, pick.Picks.Concat(new[] { uid }).ToList()));
                Button(content, "Pick:" + uid, caption, i % 3 * 320, i / 3 * pitch, 308, row, () => PickCard(uid), Line, 21, hit: new Rect(i % 3 * 320, i / 3 * pitch, 308, row));
            }
            if (list.Count == 0) Label(p, PickEmptyText(PickBlocked(b, pick.Rules[pick.Index])), 24, 110, 952, 96, 22, Muted);
            // AutoPick sends exactly the picks made so far and Core chooses the rest: that request's forecast.
            var auto = run!.BattleForecast(pick.Uid, pick.Mode, target, pick.Picks);
            Label(p, "자동으로 고르고 사용하면 · " + ForecastText(auto), 24, 448, 952, 52, 20, ForecastColor(auto));
            Button(p, "PickAuto", "남은 대상은 자동으로 고르고 사용", 24, 510, 420, 62, () => AutoPick(), Brass, 21);
            Button(p, "PickCancel", "취소", 776, 510, 200, 62, CancelPick, Line, 22);
        }
        // An empty list says why: Core skips the effect at the growth cap; otherwise no card of its zone is one the effect takes.
        static string PickEmptyText(string? blocked) =>
            blocked == PickGrowthCap ? "이번 전투의 성장 한도에 닿아 이 효과는 건너뛰어요. 고를 카드가 없어요." : "이 효과가 받을 수 있는 카드가 없어요.";

        // ───────── rewards and node services ─────────
        // Sections in the existing order inside one vertical scroll (absent ones take no space). Each reward card shows its whole source
        // text (Core's BattleText.Card) and grows with it; the card is still the immediate take, its separate info button still only reads.
        void RewardView(RunState s)
        {
            var p = s.Pending; var evolutions = ListOf(Get(p, "evolutions")); var cards = ListOf(Get(p, "cards")).Select(Text).ToList();
            var relics = ListOf(Get(p, "relic")).Select(Text).ToList(); string? part = Get(p, "part") as string;
            float effect = cards.Count == 0 ? 0 : cards.Max(id => cardDefs.TryGetValue(id, out var def) ? TextHeight(BattleText.Card(def, data!.Memory.Battle), 17, 328) : 0);
            float cardHeight = 196 + Math.Max(60, effect) + 14;
            float relicText = relics.Count == 0 ? 0 : relics.Max(id => TextHeight(relicDefs.TryGetValue(id, out var def) ? def.Text : "", 17, 452));
            float relicHeight = Math.Max(Math.Max(92, minTouch), 48 + relicText + 12);
            float partText = part == null ? 0 : TextHeight("부품 발견: " + PartName(part) + " — " + (partDefs.TryGetValue(part, out var pd) ? pd.Text : ""), 20, 1200);
            float take = Math.Max(56, minTouch), partHeight = Math.Max(Math.Max(84, take + 28), partText + 28);
            float infoHeight = Math.Max(52, minTouch); // its own button below the card, grown (not overlapping it) on a phone
            float cardsHeight = 54 + cardHeight + 10 + infoHeight + 18;
            float height = 76 + evolutions.Count * (62 + Math.Max(46, minTouch)) + (cards.Count != 0 ? cardsHeight + 12 : 0) + (relics.Count != 0 ? 54 + relicHeight + 18 + 12 : 0) + (part != null ? partHeight : 0);
            var body = Scroll(frame!, "Reward", 32, 172, 1536, PanelBottom - 172, 1536, height, Color.clear);
            var head = Frame(body, "RewardCoins", 0, 0, 1536, 64, Alpha(Brass, 0.8f));
            var coin = Gem(head, "CoinGem", 12, 6, 52, Hex("2a2110"), Brass);
            Icon(coin, "Coin", "ico_node_shop", 12, 12, 28, 28, Brass);
            Strong(Label(head, "전투 보상  ·  동전 +" + Integer(Get(p, "coins")), 78, 10, 1000, 44, 30, Brass, FontStyle.Bold));
            float y = 76;
            foreach (var offer in evolutions)
            {
                string key = Text(Get(offer, "key")); var card = s.Deck.FirstOrDefault(c => c.Key == key);
                float pick = Math.Max(46, minTouch), infoWidth = Math.Max(82, minTouch), evolveWidth = 350 - infoWidth; // 268/82 off phones
                var panel = Frame(body, "Evolution:" + key, 0, y, 1536, 50 + pick, Alpha(Memory, 0.8f));
                Label(panel, CardName(card?.Id) + " · 성장 3 달성 — 진화를 고르세요. 진화는 이번 탐사 동안 유지되고, 저장할 때 기억에 남길 수 있어요.", 20, 8, 1496, 30, 20, Ink);
                var options = ListOf(Get(offer, "options")).Select(Text).ToList();
                for (int k = 0; k < options.Count; k++)
                {
                    string branch = options[k];
                    Button(panel, "Evolve:" + key + ":" + branch, CardName(branch), 20 + k * 380, 42, evolveWidth, pick, () => Evolve(key, branch), Memory, 22);
                    Button(panel, "EvolveInfo:" + key + ":" + branch, "설명", 30 + evolveWidth + k * 380, 42, infoWidth, pick, () => ShowCardInfo(branch), Line, 20);
                }
                y += 62 + pick;
            }
            if (cards.Count != 0)
            {
                var panel = Frame(body, "RewardCards", 0, y, 1536, cardsHeight);
                Label(panel, "카드 1장을 덱에 추가 · 카드를 누르면 바로 덱에 들어가요. 먼저 설명을 볼 수 있어요.", 20, 10, 1120, 34, 21, Ink, FontStyle.Bold);
                for (int k = 0; k < cards.Count; k++)
                {
                    string id = cards[k];
                    RewardCardTile(panel, "RewardCard:" + id, id, 20 + k * 380, 54, 360, cardHeight, () => TakeCard(id));
                    Button(panel, "CardInfo:" + id, "카드 설명 보기", 20 + k * 380, 54 + cardHeight + 10, 360, infoHeight, () => ShowCardInfo(id), Line, 20);
                }
                Button(panel, "SkipCard", "받지 않기 (동전 +15)", 1160, 54, 356, 64, () => SkipCard(), Line, 22);
                y += cardsHeight + 12;
            }
            if (relics.Count != 0)
            {
                var panel = Frame(body, "RewardRelics", 0, y, 1536, 54 + relicHeight + 18);
                Label(panel, "유물 1개 선택", 20, 10, 600, 34, 24, Ink, FontStyle.Bold);
                for (int k = 0; k < relics.Count; k++)
                {
                    string id = relics[k]; var r = Inner(Button(panel, "Relic:" + id, "", 20 + k * 500, 54, 480, relicHeight, () => TakeRelic(id), Hex("1a2238"), 24, Alpha(Brass, 0.65f)));
                    Deco(r, "RelicMark", Diamond, 14, 14, 22, 22, Brass);
                    Strong(Label(r, RelicName(id), 44, 8, 422, 32, 23, Brass, FontStyle.Bold));
                    Grow(Label(r, relicDefs.TryGetValue(id, out var def) ? def.Text : "", 14, 44, 452, 40, 17, Ink));
                }
                y += 54 + relicHeight + 18 + 12;
            }
            if (part != null)
            {
                var panel = Frame(body, "RewardPart", 0, y, 1536, partHeight);
                Grow(Label(panel, "부품 발견: " + PartName(part) + " — " + (partDefs.TryGetValue(part, out var def) ? def.Text : ""), 20, 14, 1200, 40, 20, Ink));
                Button(panel, "TakePart", "가져가기", 1296, 14, 220, take, () => TakePart(), Memory, 22);
            }
            bool boss = s.Position != null && run!.NodeById(s.Position)?.Type == "boss";
            Button(frame!, "Proceed", boss ? "다음으로" : "지도로", 1288, 822, 280, 58, () => Proceed(), Brass, 24, hit: ProceedHit);
            NoticeLine("받을 보상을 고른 뒤 다음으로 나아가세요. 고르지 않은 보상은 사라져요.");
        }
        // A whole reward card, framed in its owner's colour: the owner's art window with the cost gem, the name, its catalogue line
        // (owner, grade, cost, modes) and the full source effect text.
        void RewardCardTile(Transform parent, string name, string id, float x, float y, float width, float height, Action click)
        {
            cardDefs.TryGetValue(id, out var def);
            var tone = def != null ? OwnerColor(def.Owner) : Line;
            var r = Inner(Button(parent, name, "", x, y, width, height, click, Hex("121a2e"), 24, Alpha(tone, 0.95f), 2));
            if (def != null)
            {
                var cutin = Art("Characters/char_" + def.Owner + "_cutin", true);
                if (cutin != null) Cover(r, "Band", cutin, 3, 3, width - 6, 108, 0.1f);
                else { var band = Box(r, "Band", 3, 3, width - 6, 108, Alpha(tone, 0.18f)); band.gameObject.AddComponent<RectMask2D>(); var sd = Art("Characters/char_" + def.Owner + "_sd", true); if (sd != null) Picture(band, "Art", sd, width - 170, -24, 160, 160, Color.white); }
                Deco(r, "BandFade", Fade(true), 3, 84, width - 6, 28, Hex("121a2e"));
                var cost = Gem(r, "Cost", 10, 10, 46, Brass, Hex("7a5a22"));
                Strong(Label(cost, def.Cost.ToString(), 0, 0, 46, 46, 26, Background, FontStyle.Bold, TextAnchor.MiddleCenter));
            }
            Strong(Label(r, CardName(id), 16, 118, width - 32, 38, 25, Ink, FontStyle.Bold));
            Box(r, "NameRule", 16, 156, width - 32, 1, Alpha(tone, 0.6f));
            Label(r, def != null ? CardLine(def) : "", 16, 162, width - 32, 30, 17, Brass);
            if (def != null) Grow(Label(r, BattleText.Card(def, data!.Memory.Battle), 16, 196, width - 32, 40, 17, Ink));
        }

        // Rest, forge, shop, event and archive share one layout: a place card on the left (its glyph, title and introduction) and a
        // service panel on the right whose rows grow to their whole text. Proceed keeps its place at the bottom right.
        RectTransform Service(string name, string glyph, string title, string intro)
        {
            var side = Frame(frame!, "ServiceInfo", 32, 172, 400, PanelBottom - 172);
            Deco(side, "Halo", Halo, 90, -6, 220, 200, Alpha(Brass, 0.28f));
            var medal = Gem(side, "Medal", 130, 22, 140, Hex("18223d"), Brass);
            Icon(medal, "Glyph", glyph, 26, 26, 88, 88, Brass);
            var ribbon = Plate(side, "TitleRibbon", 30, 172, 340, 52, Alpha(Background, 0.92f), Alpha(Brass, 0.8f), 1, true);
            Strong(Label(ribbon, title, 26, 0, 288, 52, 30, Brass, FontStyle.Bold, TextAnchor.MiddleCenter));
            float h = TextHeight(intro, 20, 352);
            if (h <= 280) Grow(Label(side, intro, 24, 238, 352, 40, 20, Ink, FontStyle.Normal, TextAnchor.UpperCenter));
            else { var content = Scroll(side, "IntroScroll", 16, 238, 368, 280, 368, h + 16, Color.clear); Grow(Label(content, intro, 8, 0, 352, 40, 20, Ink)); }
            return Frame(frame!, name, 448, 172, 1120, PanelBottom - 172);
        }
        // One full-width service row: a button (or a plain panel when click is null) with a title and its whole description.
        RectTransform Row(Transform parent, string name, string title, string detail, float y, Action? click, Color color, bool enabled = true, float width = 1072)
        {
            float h = Math.Max(Math.Max(96, minTouch), 54 + (detail.Length == 0 ? 0 : TextHeight(detail, 18, width - 72)) + 14);
            RectTransform r;
            if (click != null) { var button = Button(parent, name, "", 24, y, width, h, click, color); button.interactable = enabled; r = Inner(button); }
            else r = Plate(parent, name, 24, y, width, h, Alpha(Background, 0.7f), Line);
            bool light = color == Brass || color == Memory;
            if (click != null) Deco(r, "Chevron", Diamond, width - 36, h / 2 - 9, 18, 18, light ? Alpha(Background, 0.8f) : Alpha(Brass, 0.85f));
            Label(r, title, 20, 12, width - 72, 34, 24, light ? Background : Ink, FontStyle.Bold);
            if (detail.Length != 0) Grow(Label(r, detail, 20, 52, width - 72, 30, 18, light ? Alpha(Background, 0.85f) : Muted));
            return r;
        }
        void RestView(RunState s)
        {
            long uses = Integer(Get(s.Pending, "uses"));
            var panel = Service("Rest", "ico_node_rest", "휴식", "잠깐 쉬어 간다  ·  남은 이용 " + uses + "회");
            float next = 24 + 12 + Row(panel, "RestHeal", "회복 (최대 체력의 " + (30 + (s.Relics.Contains("old_compass") ? 10 : 0)) + "%)", "지금 체력 " + s.Hp + " / " + s.MaxHp + " · 이용 1회를 써요.", 24, () => RestHeal(), Memory, uses > 0).sizeDelta.y;
            Row(panel, "RestUpgrade", "카드 강화(+)", "덱에서 카드 1장을 골라 강화해요(수치 약 30% 상승). 이용 1회를 써요.", Math.Max(144, next),
                () => OpenDeckPick(Purpose.Upgrade, "강화할 카드를 고르세요(수치 약 30% 상승)"), Line, uses > 0);
            Button(frame!, "Proceed", "떠나기", 1288, 822, 280, 58, () => Proceed(), Brass, 24, hit: ProceedHit);
            NoticeLine("회복이나 강화 중 남은 이용 횟수만큼 고를 수 있어요.");
        }
        void ForgeView(RunState s)
        {
            var p = s.Pending; long uses = Integer(Get(p, "uses")); var offers = Get(p, "offers") as IDictionary<string, object?>;
            string title = "공방" + (Truthy(Get(p, "fromEvent")) ? " · 사건의 변주" : "");
            if (offers != null)
            {
                string key = Text(Get(offers, "key"));
                var panel = Service("Forge", "ico_node_forge", title, CardName(s.Deck.FirstOrDefault(c => c.Key == key)?.Id) + "에 붙일 부품을 고르세요");
                float y = 24;
                foreach (string id in ListOf(Get(offers, "parts")).Select(Text))
                    y += Row(panel, "ForgePart:" + id, PartName(id) + "  ·  " + SlotName(id), partDefs.TryGetValue(id, out var def) ? def.Text : "", y, () => ForgeApply(id), Line).sizeDelta.y + 12;
                Button(frame!, "Proceed", "변주하지 않고 떠나기", 1288, 822, 280, 58, () => Proceed(), Line, 22, hit: ProceedHit);
            }
            else
            {
                var panel = Service("Forge", "ico_node_forge", title, "카드를 다듬는다  ·  남은 이용 " + uses + "회. 변주는 카드 1장에 무작위 부품 3개 중 하나를 붙여요(회로·각인 각 1칸).");
                float next = 24 + 12 + Row(panel, "ForgeVariation", "카드 변주", "카드 1장을 골라 무작위 부품 3개 중 하나를 붙여요. 이용 1회를 써요.", 24, () => OpenDeckPick(Purpose.ForgeOffer, "변주할 카드를 고르세요"), Memory, uses > 0).sizeDelta.y;
                Row(panel, "ForgeRemove", "카드 제거", "덱에서 카드 1장을 빼요. 이용 1회를 써요.", Math.Max(144, next), () => OpenDeckPick(Purpose.ForgeRemove, "제거할 카드를 고르세요"), Line, uses > 0);
                Button(frame!, "Proceed", "떠나기", 1288, 822, 280, 58, () => Proceed(), Brass, 24, hit: ProceedHit);
            }
            NoticeLine(offers != null ? "부품 하나를 고르면 그 카드에 붙고 이용 1회를 써요." : "변주나 제거를 고르면 덱에서 카드를 골라요.");
        }
        // Wares as tiles under group headers (cards three across, parts and relics two across), each tile as tall as its group's longest
        // text; reading (카드 설명) is a separate button from buying. The removal service is a service, not a ware: it sits on the place
        // card, always in view, with its price and the existing coin check.
        void ShopView(RunState s)
        {
            var p = s.Pending;
            var panel = Service("Shop", "ico_node_shop", "상점", "떠돌이 기억 상인 · 가격은 상점이 정한 그대로예요.");
            var cards = ListOf(Get(p, "cards")); var parts = ListOf(Get(p, "parts")); var relics = ListOf(Get(p, "relics"));
            var groups = new List<(string Kind, string Header, List<object?> Items, int Across)> { ("cards", "카드", cards, 3), ("parts", "부품", parts, 2), ("relics", "유물", relics, 2) };
            string Title(string kind, string id) => kind == "cards" ? CardName(id) : kind == "parts" ? PartName(id) + " · " + SlotName(id) : RelicName(id);
            string Detail(string kind, string id) => kind == "cards" ? (cardDefs.TryGetValue(id, out var c) ? CardLine(c) : "") : kind == "parts" ? (partDefs.TryGetValue(id, out var part) ? part.Text : "") : (relicDefs.TryGetValue(id, out var relic) ? relic.Text : "");
            var heights = groups.Select(g => g.Items.Count == 0 ? 0 : g.Items.Max(item => Math.Max(112, TextHeight(Detail(g.Kind, Text(Get(item, "id"))), 17, (1056 - 16 * (g.Across - 1)) / g.Across - 28) + 68 + ShopButton))).ToList();
            float total = 8 + groups.Select((g, i) => g.Items.Count == 0 ? 0 : 36 + ((g.Items.Count + g.Across - 1) / g.Across) * (heights[i] + 12) + 2).Sum();
            var list = Scroll(panel, "ShopList", 16, 16, 1088, PanelBottom - 172 - 32, 1088, total, Color.clear);
            float y = 8;
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var g = groups[gi]; if (g.Items.Count == 0) continue;
                Label(list, g.Header, 16, y, 300, 30, 21, Memory, FontStyle.Bold); y += 36;
                float tile = (1056 - 16f * (g.Across - 1)) / g.Across;
                for (int k = 0; k < g.Items.Count; k++)
                {
                    string id = Text(Get(g.Items[k], "id"));
                    ShopItem(list, g.Kind, g.Items[k], Title(g.Kind, id), Detail(g.Kind, id), 16 + (k % g.Across) * (tile + 16), y + (k / g.Across) * (heights[gi] + 12), tile, heights[gi], s.Coins,
                        g.Kind == "cards" ? () => ShowCardInfo(id) : (Action?)null);
                }
                y += ((g.Items.Count + g.Across - 1) / g.Across) * (heights[gi] + 12) + 2;
            }
            var side = frame!.Find("ServiceInfo");
            object? removeCost = Get(p, "removeCost");
            if (removeCost == null) Label(side, "제거 서비스 이용 완료", 24, 520, 352, 64, 22, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            else Button(side, "ShopRemove", "카드 제거 (동전 " + Integer(removeCost) + ")", 24, 520, 352, Math.Max(68, minTouch), () => OpenDeckPick(Purpose.ShopRemove, "제거할 카드를 고르세요"), Line, 22)
                .interactable = s.Coins >= Integer(removeCost);
            Button(frame!, "Proceed", "떠나기", 1288, 822, 280, 58, () => Proceed(), Brass, 24, hit: ProceedHit);
            NoticeLine("가격은 상점이 정한 그대로예요. 동전이 부족하면 살 수 없어요.");
        }
        float ShopButton => Math.Max(50, minTouch); // a ware's read and buy buttons: drawn at 48dp on a phone, in a tile grown for them
        void ShopItem(Transform parent, string kind, object? item, string title, string detail, float x, float y, float width, float height, int coins, Action? info = null)
        {
            string id = Text(Get(item, "id")); long price = Integer(Get(item, "price")); bool sold = Truthy(Get(item, "sold"));
            var box = Plate(parent, "Shop:" + kind + ":" + id, x, y, width, height, Alpha(Background, 0.82f), sold ? Alpha(Line, 0.6f) : Alpha(Brass, 0.5f));
            Sheen(box, width, height, 0.05f);
            Strong(Label(box, title, 14, 10, width - 28, 34, 22, sold ? Muted : Ink, FontStyle.Bold));
            Grow(Label(box, detail, 14, 48, width - 28, 30, 17, Muted));
            // Reading is separate from buying: the detail button never pays.
            if (info != null) Button(box, "Info:" + kind + ":" + id, "카드 설명", 14, height - 12 - ShopButton, 150, ShopButton, info, Line, 20);
            Button(box, "Buy:" + kind + ":" + id, sold ? "구매함" : "동전 " + price, width - 176, height - 12 - ShopButton, 162, ShopButton, () => Buy(kind, id), sold ? Line : Brass, 21)
                .interactable = !sold && coins >= price;
        }
        void EventView(RunState s)
        {
            var p = s.Pending; string id = Text(Get(p, "id"));
            var definition = data!.Events.LastOrDefault(e => e.Id == id);
            if (definition == null) { var unknown = Frame(frame!, "Event", 448, 172, 1120, PanelBottom - 172, Danger); Label(unknown, "알 수 없는 사건이에요.", 20, 14, 1080, 44, 28, Danger); return; }
            var panel = Service("Event", "ico_node_event", definition.Name, definition.Text);
            if (Truthy(Get(p, "done")))
            {
                var chosen = definition.Choices[(int)Integer(Get(p, "choice"))];
                var outcome = Row(panel, "EventOutcome", chosen.Label, chosen.Text, 24, null, Line);
                EdgeOf(outcome)!.color = Memory;
                Button(frame!, "Proceed", "지도로", 1288, 822, 280, 58, () => Proceed(), Brass, 24, hit: ProceedHit);
            }
            else
            {
                float y = 24;
                for (int i = 0; i < definition.Choices.Count; i++)
                {
                    int index = i; var choice = definition.Choices[i]; int coins = choice.RequiredCoins.GetValueOrDefault();
                    y += Row(panel, "EventChoice:" + i, choice.Label + (coins > 0 ? " (동전 " + coins + " 필요)" : ""), choice.Text, y, () => EventChoice(index, choice), Line, s.Coins >= coins).sizeDelta.y + 12;
                }
                if (y > PanelBottom - 172) // more choices than the panel holds: the rows move into a scroll, all reachable
                {
                    var rows = panel.Cast<Transform>().Where(row => row.name != "Edge" && row.name != "Sheen" && row.name != "Ornament").ToList();
                    var content = Scroll(panel, "EventChoices", 0, 0, 1120, PanelBottom - 172, 1120, y + 12, Color.clear);
                    foreach (var row in rows) row.SetParent(content, false);
                }
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
            var panel = Service("Archive", "ico_node_archive", "기록소", "첫 구역의 기록을 확정했다");
            Grow(Label(panel, "여기서 돌아가면 지금까지의 덱을 기억으로 저장할 수 있어요. 계속 가면 체력 15%를 회복하고 다음 구역으로 갑니다(패배하면 변경 1개만 남아요).", 24, 24, 1072, 40, 21, Ink));
            float next = 120 + 12 + Row(panel, "ArchiveContinue", "계속 탐사", "체력 15%를 회복하고 다음 구역으로 가요. 패배하면 변경 1개만 남아요.", 120, () => Archive("continue"), Brass).sizeDelta.y;
            Row(panel, "ArchiveReturn", "귀환하고 저장", "지금까지의 덱을 기억 저장 화면으로 가져가요.", Math.Max(240, next), () => Archive("return"), Line);
            NoticeLine("귀환하면 기억 저장 화면으로 가요.");
        }

        // ───────── terminal memory (§8.9-8.14) ─────────
        // A sober ledger over the dimmed workshop: the rule header, one column per member (portrait, before → result capacity with a meter
        // that marks only an over-capacity, the kept deck, the selectable changes, the result preview and any card the save would drop) and
        // the reason line beside the two exits. Every list scrolls inside its own region; nothing is recomputed here.
        void SaveView(RunState s)
        {
            var info = run!.MemoryChanges(); var order = ChangeOrder(info, s);
            selectedChanges.RemoveAll(id => !order.Contains(id));
            var flat = Flatten(info, s); var preview = SafePreview(flat); bool defeat = Truthy(Get(s.Pending, "defeat"));
            var head = Frame(frame!, "SaveHeader", 32, 88, 1536, 124, defeat ? Danger : Alpha(Brass, 0.85f));
            Label(head, defeat ? "패배 · 출발 전 기억은 지켜져요. 이번 변화는 하나까지" : "탐사 종료 · 이번 탐사의 변화를 골라 기억에 더하세요", 20, 8, 1260, 40, 28, defeat ? Danger : Brass, FontStyle.Bold);
            Label(head, defeat ? "패배해도 출발 전 기억 덱은 그대로예요. 이번 탐사에서 생긴 변화 중 파티 전체에서 1개까지 더할 수 있어요. 아무것도 고르지 않으면 기존 덱이 유지돼요."
                : "출발 전 기억 덱에 이번 탐사의 변화를 여러 개 더할 수 있어요. 용량은 고른 것의 합이 아니라 완성된 결과 덱 전체로 계산해요. 저장한 덱은 다음 탐사의 시작 덱이 돼요.", 20, 50, 1496, 44, 18, Ink);
            Label(head, "선택 " + flat.Count + "개" + (defeat ? " / 1" : "") + "  ·  연습용 기억은 이 세션에만 있고 앱을 닫으면 사라져요.", 20, 96, 1240, 24, 17, Muted);
            if (s.Deck.Any(GainedCommon)) Button(head, "AssignButton", "공용 카드 배정", 1312, 10, 204, 52, () => Open(Sheet.Assign), Line, 20, hit: Band(1312, 8, 204, Math.Max(minTouch, 54)));
            var reasons = new List<string>(); string? error = preview == null ? "INVALID_SNAPSHOT" : Get(preview, "error") as string;
            if (error == "DEFEAT_KEEP_ONE") reasons.Add("패배하면 이번 탐사의 변화는 파티 전체에서 1개까지만 남길 수 있어요. 지금 " + flat.Count + "개를 골랐어요.");
            else if (error == "OVER_CAPACITY")
                reasons.Add("용량을 넘는 캐릭터가 있어요: " + string.Join(", ", s.Party.Select(x => x.CharId).Where(cid => Used(preview, cid) > Capacity(preview, cid))
                    .Select(cid => CharacterName(cid) + " " + Used(preview, cid) + "/" + Capacity(preview, cid))) + ". 결과 덱 전체 비용으로 계산해요.");
            else if (error != null) reasons.Add(ErrorText(error));
            for (int i = 0; i < s.Party.Count; i++) MemoryColumn(s.Party[i].CharId, Get(info, s.Party[i].CharId), order, flat, preview, 32 + i * 518);
            string fallback = reasons.Count != 0 ? "저장 버튼을 쓸 수 없는 이유: " + string.Join(" / ", reasons) : "고른 변화는 확정하기 전까지 아무것도 바꾸지 않아요.";
            Color fallbackColor = reasons.Count != 0 ? Danger : Muted;
            Plate(frame!, "SaveReason", 24, 812, 864, 72, Glass, reasons.Count != 0 ? Danger : Line);
            TrackNotice(Label(frame!, LastError != null ? notice : fallback, 40, 816, 836, 64, 18, LastError != null ? Danger : fallbackColor, FontStyle.Normal, TextAnchor.MiddleLeft), fallback, fallbackColor);
            Button(frame!, "CommitMemory", flat.Count != 0 ? "고른 변화 " + flat.Count + "개 저장하고 끝내기" : "변화 없이 저장(기존 덱 유지)", 898, 816, 390, 64, () => CommitMemory(), reasons.Count == 0 ? Brass : Line, 21, hit: new Rect(898, ExitTop, 390, 900 - ExitTop))
                .interactable = reasons.Count == 0;
            Button(frame!, "CloseWithoutSave", "저장하지 않고 닫기", 1304, 816, 264, 64, () => CloseWithoutSave(), Line, 21, hit: new Rect(1304, ExitTop, 264, 900 - ExitTop));
        }
        void MemoryColumn(string cid, object? entry, List<string> order, List<string> flat, Dictionary<string, object?>? preview, float x)
        {
            long capacity = preview != null ? Capacity(preview, cid) : Integer(Get(entry, "capacity")), used = preview != null ? Used(preview, cid) : Integer(Get(entry, "usedBefore"));
            bool over = used > capacity;
            var column = Frame(frame!, "Memory:" + cid, x, 220, 500, ExitTop - 6 - 220, over ? Danger : (Color?)null);
            Gem(column, "PortraitMedal", 8, 6, 72, Hex("18223d"), Alpha(AttributeColor(characterAttrs.TryGetValue(cid, out var attr) ? attr : ""), 0.9f));
            Picture(column, "Portrait", Art("Characters/char_" + cid + "_sd"), 8, 4, 72, 72, Color.white);
            Strong(Label(column, CharacterName(cid), 88, 10, 280, 36, 28, Ink, FontStyle.Bold));
            Label(column, "출발 전 용량 " + Integer(Get(entry, "usedBefore")) + "/" + Integer(Get(entry, "capacity")) + "  ·  결과 용량 " + used + "/" + capacity, 88, 48, 400, 26, 18, over ? Danger : Brass);
            var meter = Bar(column, "Capacity", 16, 84, over ? 380 : 468, 10, (int)Math.Min(used, capacity), (int)Math.Max(1, capacity), over ? Danger : Brass);
            if (over) { var tag = Plate(column, "OverCapacity", 404, 76, 80, 26, Alpha(Danger, 0.2f), Danger); Label(tag, "용량 초과", 0, 0, 80, 26, 15, Danger, FontStyle.Bold, TextAnchor.MiddleCenter); }
            var before = ListOf(Get(entry, "before"));
            Label(column, "출발 전에 있던 기억 " + before.Count + "장 — 유지", 16, 104, 468, 24, 17, Memory);
            TextList(column, "Before:" + cid, string.Join(", ", before.Select(SpecText)), 16, 130, 468, 60, 16, Muted);
            var changes = ListOf(Get(entry, "changes"));
            Label(column, "이번 탐사에서 생긴 변화 " + changes.Count + "개", 16, 198, 468, 26, 18, Memory);
            if (changes.Count == 0) Label(column, "바뀐 점이 없어요. 출발 전 기억이 그대로 유지돼요.", 16, 228, 468, 52, 17, Muted);
            else
            {
                float row = Math.Max(62, minTouch), pitch = row + 8;
                // The changes list takes what the column leaves above the result preview (see below).
                float listHeight = column.sizeDelta.y - 8 - 40 - 6 - 54 - 26 - 8 - 226;
                var list = Scroll(column, "Changes:" + cid, 16, 226, 468, listHeight, 468, changes.Count * pitch, Alpha(Background, 0.6f));
                for (int k = 0; k < changes.Count; k++)
                {
                    string id = Text(Get(changes[k], "id")); bool on = flat.Contains(id);
                    string hint = on ? "선택됨 · 저장하면 남아요" : Hint(order, flat, id, cid);
                    Button(list, "Change:" + id, (on ? "[선택] " : "") + KindName(Text(Get(changes[k], "kind"))) + " · " + Text(Get(changes[k], "label")) + "\n" + hint, 0, k * pitch, 468, row,
                        () => ToggleChange(id), on ? Memory : Line, 18, hit: new Rect(0, k * pitch, 468, row));
                }
            }
            var result = preview != null ? ListOf(Get(Get(preview, "memory"), cid)) : before;
            var beforeByKey = before.GroupBy(r => Text(Get(r, "key"))).ToDictionary(g => g.Key, g => Signature(g.First()), StringComparer.Ordinal);
            var resultKeys = new HashSet<string>(result.Select(r => Text(Get(r, "key"))), StringComparer.Ordinal);
            var dropped = before.Where(r => !resultKeys.Contains(Text(Get(r, "key")))).ToList();
            // The result preview and any dropped cards stack up from the column's own bottom (which ends above the exits' touch row).
            float bottom = column.sizeDelta.y - 8, droppedTop = bottom - 40, resultHeight = dropped.Count != 0 ? 54 : 96;
            float resultTop = (dropped.Count != 0 ? droppedTop - 6 : bottom) - resultHeight;
            Label(column, "결과 덱 미리보기 " + result.Count + "장", 16, resultTop - 26, 468, 24, 17, Memory);
            TextList(column, "Result:" + cid, string.Join(", ", result.Select(r => SpecText(r) + (!beforeByKey.TryGetValue(Text(Get(r, "key")), out var sig) ? " (새로)" : sig != Signature(r) ? " (바뀜)" : ""))),
                16, resultTop, 468, resultHeight, 16, Ink);
            if (dropped.Count != 0) TextList(column, "Dropped:" + cid, "이번 저장으로 빠지는 카드: " + string.Join(", ", dropped.Select(SpecText)), 16, droppedTop, 468, 40, 16, Danger);
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
            var panel = Frame(frame!, "Result", 32, 88, 1536, 796);
            string outcome = Text(Get(s.Result, "outcome"));
            string title = closedKind == "abandon" ? "탐사 포기" : outcome == "defeat" ? "패배" : outcome == "return" ? "기록소 귀환" : outcome == "clear" ? "탐사 완주" : "탐사 종료";
            Strong(Label(panel, title, 24, 18, 1488, 56, 42, outcome == "defeat" || closedKind == "abandon" ? Danger : Brass, FontStyle.Bold));
            Label(panel, "전투 " + s.Statistics.Battles + "회 (엘리트 " + s.Statistics.Elites + ")  ·  처치 " + s.Statistics.Kills + "  ·  남은 체력 " + s.Hp + "/" + s.MaxHp
                + "  ·  유물 " + (s.Relics.Count == 0 ? "-" : string.Join(", ", s.Relics.Select(RelicName))), 24, 84, 1488, 34, 22, Ink);
            var used = Get(lastSave, "used") as IDictionary<string, object?>;
            string memory = closedKind != null || lastSave == null ? "저장하지 않고 닫음 · 기억 덱 변화 없음"
                : (lastSaveCount == 0 ? "기존 덱 유지" : "변화 " + lastSaveCount + "개 저장")
                  + (used == null ? "" : " · " + string.Join(", ", used.Select(u => CharacterName(u.Key) + " 용량 " + Integer(u.Value))));
            Label(panel, memory, 24, 124, 1488, 34, 22, Memory);
            for (int i = 0; i < ReferenceParty.Count; i++)
            {
                string cid = ReferenceParty[i]; var column = Plate(panel, "Saved:" + cid, 24 + i * 500, 176, 480, 360, Alpha(Background, 0.75f), Alpha(Brass, 0.4f));
                Sheen(column, 480, 360, 0.04f);
                var rows = Get(Get(store, "memory"), cid) as IList;
                Picture(column, "Portrait", Art("Characters/char_" + cid + "_sd"), 8, 6, 64, 64, Color.white);
                Label(column, CharacterName(cid), 80, 10, 384, 36, 28, Ink, FontStyle.Bold);
                Label(column, rows == null ? "기본 덱 (저장된 변화 없음)" : "저장된 기억 덱 " + rows.Count + "장", 80, 46, 384, 26, 19, Memory);
                var specs = rows != null ? rows.Cast<object?>().ToList() : MemoryV2.Baseline(data!.Memory, cid).Select(c => (object?)c.ToJsonGraph()).ToList();
                TextList(column, "SavedList:" + cid, string.Join(", ", specs.Select(SpecText)), 16, 84, 448, 260, 17, Muted);
            }
            Label(panel, "이 기억은 연습용으로 이 세션에만 있어요. 앱을 닫으면 사라져요. 다음 탐사는 위 기억 덱으로 시작해요.", 24, 552, 1488, 60, 20, Muted);
            Button(panel, "LengthShort", "짧은 탐사 (1구역)", 24, 640, 300, 64, () => { Length = "short"; Render(); }, Length == "short" ? Memory : Line, 22);
            Button(panel, "LengthFull", "긴 탐사 (2구역)", 340, 640, 300, 64, () => { Length = "full"; Render(); }, Length == "full" ? Memory : Line, 22);
            Button(panel, "Depart", "다음 탐사 출발", 1188, 640, 324, 64, () => Depart(), Brass, 26);
            if (notice.Length != 0) TrackNotice(Label(panel, notice, 24, 722, 1488, 44, 20, LastError != null ? Danger : Memory), "", Memory);
        }
        // A comma list in its own region: drawn whole, scrolling when it is longer than the region.
        void TextList(Transform parent, string name, string value, float x, float y, float width, float height, int size, Color color)
        {
            float h = TextHeight(value, size, width - 8);
            if (h <= height) { Grow(Label(parent, value, x, y, width, height, size, color)); return; }
            var content = Scroll(parent, name, x, y, width, height, width, h + 12, Color.clear);
            Grow(Label(content, value, 0, 0, width - 8, height, size, color));
        }

        // ───────── motion (decoration only: never moves a control, never reaches the Run) ─────────
        // Per Render: a fade when the screen changes kind, and in a battle the public differences since the last uncovered view: a turn
        // banner, hit numbers sized by the damage over a white burst, a shake of the enemy's art (and of the backdrop on a heavy hit), the
        // party panel's hit or heal, an HP bar's lost part holding a moment and then draining away, new hand cards dealing in, the played
        // card flying to its target or the rail and its owner stepping forward. While a turn summary or a battle's last action covers the
        // view, nothing is compared, so the numbers appear when it closes. Reduced motion: instant, no banner, burst, shake or flight.
        bool Calm => MemoryForgeUiMotion.Reduced || actionPace == ActionPacing.Reduced;
        bool Still() => Calm; // checked every frame by a running motion, so switching to 동작 줄이기 settles it at once
        void Animate(string screen)
        {
            if (!Application.isPlaying || frame == null || run == null) return;
            if (screen != lastScreen)
            {
                lastScreen = screen;
                if (!Calm) StartCoroutine(MemoryForgeUiMotion.Fade(frame.gameObject.AddComponent<CanvasGroup>(), 0f, 1f, 0.24f, Still));
            }
            var s = recovery == null ? run.State : null; var b = s?.Phase == "BATTLE" ? s.Battle : null; // a quarantined Run is never read
            RedrawOwnerBack(b != null);
            if (s == null || b == null) { seenBattle = null; seenEnemies.Clear(); seenHand.Clear(); flight = null; return; }
            string key = s.RunId + "@" + s.Position;
            if (key != seenBattle)
            {
                seenBattle = key; seenEnemies.Clear(); seenHand.Clear(); seenHp = b.Hp; seenTurn = b.Turn;
                foreach (var e in b.Enemies) seenEnemies[e.Uid] = (e.Hp, e.Block);
                if (!Calm)
                {
                    foreach (var art in enemyArt.Values) StartCoroutine(MemoryForgeUiMotion.SlideIn(art, new Vector2(60, 0), 0.35f, Still));
                    foreach (var art in partyArt.Values) StartCoroutine(MemoryForgeUiMotion.SlideIn(art, new Vector2(-60, 0), 0.35f, Still));
                }
                Banner(s.BattleType == "boss" ? "보스 전투" : s.BattleType == "elite" ? "엘리트 전투" : "전투 시작", Danger);
            }
            if (turnSummary != null || (actionRows != null && actionTerminal)) return;
            if (b.Turn != seenTurn) { seenTurn = b.Turn; if (b.Phase == "PLAYER") Banner(b.Turn + "턴 · 내 차례", Brass); }
            foreach (var e in b.Enemies)
            {
                if (!seenEnemies.TryGetValue(e.Uid, out var seen))
                { if (!Calm && enemyArt.TryGetValue(e.Uid, out var arrived)) StartCoroutine(MemoryForgeUiMotion.SlideIn(arrived, new Vector2(0, 40), 0.3f, Still)); }
                else
                {
                    if (e.Hp < seen.Hp) Hit(e.Uid, seen.Hp - e.Hp, seen.Hp, e.MaxHp);
                    else if (e.Hp > seen.Hp) Pop(Anchor(e.Uid), "+" + (e.Hp - seen.Hp), Memory);
                    if (e.Block > seen.Block) Pop(Anchor(e.Uid), "+" + (e.Block - seen.Block) + " 방어", Ink, 34);
                }
                seenEnemies[e.Uid] = (e.Hp, e.Block);
            }
            if (partyStatus != null && partyBar != null && seenHp >= 0 && b.Hp != seenHp)
            {
                int change = Math.Abs(b.Hp - seenHp);
                Pop(partyBar, (b.Hp < seenHp ? "-" : "+") + change, b.Hp < seenHp ? Danger : Memory, 46, b.Hp < seenHp ? change : 0);
                if (b.Hp < seenHp) { Flash(partyStatus); Drain(partyBar, seenHp, b.MaxHp); foreach (var art in partyArt.Values) if (!Calm) StartCoroutine(MemoryForgeUiMotion.Shake(art, change < 10 ? 8 : 16, 0.3f, Still)); }
            }
            seenHp = b.Hp;
            bool first = seenHand.Count == 0; int order = 0;
            foreach (string uid in b.Zones.Hand)
                if (!seenHand.Contains(uid) && handFaces.TryGetValue(uid, out var face) && !Calm) StartCoroutine(Deal(face, first ? order++ * 0.05f : 0));
            seenHand.Clear(); seenHand.UnionWith(b.Zones.Hand);
            if (flight != null) { Fly(flight.Value); Lunge(flight.Value.Owner); flight = null; }
        }
        IEnumerator Deal(RectTransform face, float delay)
        {
            var group = face.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0;
            for (float t = 0; t < delay && !Calm; t += Time.unscaledDeltaTime) { if (face == null) yield break; yield return null; }
            StartCoroutine(MemoryForgeUiMotion.Fade(group, 0, 1, 0.2f, Still));
            yield return MemoryForgeUiMotion.SlideIn(face, new Vector2(0, -70), 0.26f, Still);
        }
        // A hit on an enemy, scaled by its size (StS-style tiers at 5 and 20): the number, a burst and a red tint on the art, a shake that
        // grows with the damage (the backdrop too from 20) and the bar's lost part.
        void Hit(string uid, int amount, int before, int max)
        {
            enemyArt.TryGetValue(uid, out var art);
            Pop(Anchor(uid), "-" + amount, Danger, 46, amount);
            if (Calm) return;
            if (art != null)
            {
                StartCoroutine(MemoryForgeUiMotion.Shake(art, amount < 5 ? 8 : amount < 20 ? 16 : 26, amount < 20 ? 0.3f : 0.45f, Still));
                var image = art.GetComponent<Image>(); if (image != null) StartCoroutine(Tint(image, new Color(1f, 0.42f, 0.38f, image.color.a)));
                Spark(art, amount);
            }
            if (amount >= 20 && frame != null && frame.Find("Backdrop/Art") is RectTransform stage) StartCoroutine(MemoryForgeUiMotion.Shake(stage, 10, 0.35f, Still));
            if (enemyBars.TryGetValue(uid, out var bar)) Drain(bar, before, max);
        }
        RectTransform? Anchor(string uid) => enemyArt.TryGetValue(uid, out var art) ? art : enemyRoots.TryGetValue(uid, out var root) ? root : null;
        IEnumerator Tint(Image image, Color hit)
        {
            var rest = image.color;
            for (float t = 0; t < 0.3f && !Calm; t += Time.unscaledDeltaTime) { if (image == null) yield break; image.color = Color.Lerp(hit, rest, t / 0.3f); yield return null; }
            if (image != null) image.color = rest;
        }
        // A white burst over a struck painting, larger for a bigger hit.
        void Spark(RectTransform art, int amount)
        {
            if (frame == null) return;
            float size = 120 + Math.Min(160, amount * 6); var at = FramePoint(art);
            var burst = Deco(frame, "HitSpark", Halo, at.x - size / 2, at.y - size / 2, size, size, new Color(1f, 0.97f, 0.9f, 0.85f));
            burst.pivot = new Vector2(0.5f, 0.5f); burst.anchoredPosition += new Vector2(size / 2, -size / 2);
            StartCoroutine(MemoryForgeUiMotion.Burst(burst, 0.5f, 1.4f, 0.28f, Still));
            var frames = Enumerable.Range(1, 6).Select(k => Kit("ui_hit_slash_0" + k)).ToArray();
            if (frames.Any(f => f == null)) return;
            float slash = 200 + Math.Min(120, amount * 4);
            var image = Deco(frame, "HitSlash", frames[0]!, at.x - slash / 2, at.y - slash / 2, slash, slash, Color.white).GetComponent<Image>();
            StartCoroutine(Flipbook(image, frames!, 0.06f));
        }
        // Steps an image through its frames, then removes it; with reduced motion it goes at once.
        IEnumerator Flipbook(Image image, Sprite[] frames, float step)
        {
            for (int k = 0; k < frames.Length && !Calm; k++)
            {
                if (image == null) yield break;
                image.sprite = frames[k];
                for (float t = 0; t < step && !Calm; t += Time.unscaledDeltaTime) yield return null;
            }
            if (image != null) Destroy(image.gameObject);
        }
        void Flash(RectTransform panel)
        {
            if (Calm) return;
            var flash = Box(panel, "HitFlash", 0, 0, panel.sizeDelta.x, panel.sizeDelta.y, new Color(1f, 0.35f, 0.3f, 0.35f));
            StartCoroutine(MemoryForgeUiMotion.Fade(flash.gameObject.AddComponent<CanvasGroup>(), 1, 0, 0.35f, Still));
        }
        // The lost part of a bar (its old value) shown behind the new fill: it holds a moment, so the size of the hit reads, then drains.
        void Drain(RectTransform fill, int before, int max)
        {
            if (Calm || max <= 0) return;
            var back = (RectTransform)fill.parent; float width = back.sizeDelta.x;
            var ghost = Box(back, "Lost", 0, 0, width * Mathf.Clamp01((float)before / max), back.sizeDelta.y, new Color(1f, 0.85f, 0.6f, 0.75f));
            ghost.SetSiblingIndex(0);
            StartCoroutine(DrainLater(ghost, fill.sizeDelta.x));
        }
        IEnumerator DrainLater(RectTransform ghost, float width)
        {
            for (float t = 0; t < 0.4f && !Calm; t += Time.unscaledDeltaTime) { if (ghost == null) yield break; yield return null; }
            if (ghost != null) yield return MemoryForgeUiMotion.Resize(ghost, width, 0.5f, Still);
        }
        // A number rising from the middle of an element (the enemy's art, the party panel), popping in larger for a bigger hit; with
        // reduced motion it stays still at its size, then goes.
        void Pop(RectTransform? anchor, string text, Color color, float rise = 46, int amount = 0)
        {
            if (anchor == null || frame == null) return;
            var at = FramePoint(anchor); int size = 40 + Math.Min(24, amount);
            var label = Label(frame, text, at.x - 150, at.y - 45, 300, 90, size, color, FontStyle.Bold, TextAnchor.MiddleCenter);
            label.name = "HitNumber"; label.resizeTextForBestFit = false;
            var shadow = label.gameObject.AddComponent<Outline>(); shadow.effectColor = new Color(0, 0, 0, 0.8f); shadow.effectDistance = new Vector2(2, -2);
            var r = label.rectTransform; r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition += new Vector2(150, -45);
            StartCoroutine(MemoryForgeUiMotion.Rise(label, rise, 0.9f, Still));
            if (!Calm) StartCoroutine(MemoryForgeUiMotion.Punch(r, 1.7f, 0.2f, Still));
        }
        // A band across the stage saying what begins (the battle, a player turn); it fades in and out and takes no input.
        void Banner(string text, Color tone)
        {
            if (Calm || frame == null) return;
            var art = Kit(tone == Danger ? "ui_banner_turn_enemy" : "ui_banner_turn_player");
            var band = Box(frame, "TurnBanner", 0, art != null ? 262 : 316, 1600, art != null ? 200 : 92, art != null ? Color.white : Alpha(Background, 0.8f));
            if (art != null) band.GetComponent<Image>().sprite = art;
            else { Box(band, "Line", 0, 0, 1600, 3, Alpha(tone, 0.9f)); Box(band, "Line", 0, 89, 1600, 3, Alpha(tone, 0.9f)); }
            var label = Strong(Label(band, text, 0, 0, 1600, art != null ? 200 : 92, 42, tone, FontStyle.Bold, TextAnchor.MiddleCenter)); label.resizeTextForBestFit = false;
            StartCoroutine(ShowBanner(band, label.rectTransform));
        }
        IEnumerator ShowBanner(RectTransform band, RectTransform words)
        {
            var group = band.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
            StartCoroutine(MemoryForgeUiMotion.SlideIn(words, new Vector2(120, 0), 0.3f, Still));
            yield return MemoryForgeUiMotion.Fade(group, 0, 1, 0.16f, Still);
            for (float t = 0; t < 0.55f && !Calm; t += Time.unscaledDeltaTime) { if (band == null) yield break; yield return null; }
            if (band != null) yield return MemoryForgeUiMotion.Fade(group, 1, 0, 0.3f, Still);
            if (band != null) Destroy(band.gameObject);
        }
        // An ultimate's cut-in (Codex battle kit README: the common diagonal plate with the owner's approved cut-in layered on it, no
        // new character motion): plate and picture slide in from the left, the member's name and ultimate follow from the right, all
        // hold and fade. Decoration only (no raycasts, the committed result is already in the Run and its display); reduced motion
        // omits it, as the kit asks, and 2배속 halves it. A member without a cut-in picture gets the plate and the words.
        const string UltimateFlight = "필살기";
        void CutIn(string? actor)
        {
            if (Calm || frame == null || actor == null) return;
            var plate = Kit("ui_cutin_plate");
            var band = Box(frame, "UltimateCutIn", 0, 240, 1600, 420, plate != null ? Color.white : Alpha(Background, 0.85f));
            if (plate != null) band.GetComponent<Image>().sprite = plate;
            Cover(band, "Figure", Art("Characters/char_" + actor + "_cutin", true), 160, 0, 600, 420, 0.12f);
            var words = Strong(Label(band, CharacterName(actor) + "\n" + (ultimateNames.TryGetValue(actor, out var ult) && ult.Length > 0 ? ult : UltimateFlight),
                800, 110, 720, 200, 52, Brass, FontStyle.Bold, TextAnchor.MiddleLeft));
            words.resizeTextForBestFit = false;
            StartCoroutine(ShowCutIn(band, (RectTransform)band.Find("Figure"), words.rectTransform));
        }
        IEnumerator ShowCutIn(RectTransform band, RectTransform figure, RectTransform words)
        {
            float pace = PaceScale;
            var group = band.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
            StartCoroutine(MemoryForgeUiMotion.SlideIn(band, new Vector2(-320, 0), 0.22f * pace, Still));
            StartCoroutine(MemoryForgeUiMotion.SlideIn(figure, new Vector2(-140, 0), 0.32f * pace, Still));
            StartCoroutine(MemoryForgeUiMotion.SlideIn(words, new Vector2(160, 0), 0.32f * pace, Still));
            yield return MemoryForgeUiMotion.Fade(group, 0, 1, 0.14f * pace, Still);
            for (float t = 0; t < 0.6f * pace && !Calm; t += Time.unscaledDeltaTime) { if (band == null) yield break; yield return null; }
            if (band != null) yield return MemoryForgeUiMotion.Fade(group, 1, 0, 0.25f * pace, Still);
            if (band != null) Destroy(band.gameObject);
        }
        // The played card's ghost (decoration) from its hand slot to the enemy it hit, the rail group it was reserved in or the party.
        void Fly((string Name, Vector2 From, string? Target, string Mode, string? Owner) shot)
        {
            if (Calm || frame == null) return;
            Vector2 to = shot.Mode != "now" ? (railZones.TryGetValue(shot.Mode, out var zone) ? FramePoint(zone) : new Vector2(800, 113))
                : shot.Target != null && enemyArt.TryGetValue(shot.Target, out var aim) ? FramePoint(aim) : partyPanel != null ? FramePoint(partyPanel) : new Vector2(800, 300);
            var ghost = Plate(frame, "PlayedCard", shot.From.x - 80, shot.From.y - 90, 160, 180, Panel, Memory, 2);
            Label(ghost, shot.Name, 10, 70, 140, 40, 22, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            // Under every panel and control (just above the stage scrim): it shows over the painted stage only and never hides a
            // control's caption that may still be used while it flies.
            var under = frame.Find("ScrimLow") ?? frame.Find("Scrim");
            if (under != null) ghost.SetSiblingIndex(under.GetSiblingIndex() + 1);
            StartCoroutine(MemoryForgeUiMotion.Fly(ghost, to - new Vector2(80, 90), 0.36f, Still));
        }
        // The member whose card or ultimate committed steps towards the enemies and back.
        void Lunge(string? owner)
        {
            if (Calm || owner == null || !partyArt.TryGetValue(owner, out var art) || art == null) return;
            StartCoroutine(MemoryForgeUiMotion.SlideIn(art, new Vector2(48, 0), 0.32f, Still));
        }
        // A rect's centre in the frame's top-left coordinates (the space Box and Label place in).
        Vector2 FramePoint(RectTransform r)
        {
            var local = frame!.InverseTransformPoint(r.TransformPoint(r.rect.center));
            return new Vector2(local.x + 800, 450 - local.y);
        }
        // Remembers where a play or an ultimate leaves from (its card face or Ult button) and whose it is, so the view after it commits
        // can fly its ghost and step its owner forward.
        void Launch(Dictionary<string, RectTransform> from, string key, string name, string mode, string? aim, string? owner)
        {
            launched = frame != null && from.TryGetValue(key, out var origin) && origin != null ? (name, FramePoint(origin), aim, mode, owner) : null;
        }
        string? CardOwner(string uid)
        {
            try { return run?.BattleEffectiveCard(uid).Owner; } catch (RuleException) { return null; }
        }

        // ───────── card drag (BATTLE-STAGE-20261018) ─────────
        // A drag only draws: an arrow from the card to the pointer and a light on the zone a drop there asks for. The drop goes through
        // DragPlay, the tap path's own selection and confirm. None of it takes a raycast.
        RectTransform? dragLayer; string? dragUid;
        bool CardInPlay(string uid)
        {
            var b = run?.State.Battle;
            return frame != null && b != null && b.Phase == "PLAYER" && !Locked() && prepared == null && b.Zones.Hand.Contains(uid) && handFaces.ContainsKey(uid);
        }
        bool BeginCardDrag(string uid)
        {
            if (!CardInPlay(uid)) return false;
            dragUid = uid; holdUid = uid; holdTicket++; HidePeek(); ShowOwnerBack(uid); return true;
        }
        void CardDragTo(Vector2 screen, Camera? eye)
        {
            if (dragLayer != null) Destroy(dragLayer.gameObject);
            dragLayer = null;
            if (frame == null || dragUid == null || !handFaces.TryGetValue(dragUid, out var face) || face == null) return;
            dragLayer = Box(frame, "DragArrow", 0, 0, 1600, 900, Color.clear);
            var (mode, aim, zone) = DropTarget(dragUid, screen, eye);
            Color color = mode == null ? Alpha(Ink, 0.7f) : mode == "now" ? Danger : Memory;
            if (zone != null) { var r = FrameRect(zone); Plate(dragLayer, "DropHot", r.x - 6, r.y - 6, r.width + 12, r.height + 12, Alpha(color, 0.12f), color, 3); }
            if (mode != null) DragForecast(dragUid, mode, aim, ScreenToFrame(screen, eye));
            Vector2 from = FramePoint(face) - new Vector2(0, face.rect.height / 2), to = ScreenToFrame(screen, eye);
            var bend = (from + to) / 2 + new Vector2(0, -Math.Min(160, Vector2.Distance(from, to) * 0.3f));
            const int dots = 14;
            string state = mode == null ? "normal" : "target";
            Sprite? body = Kit("ui_drag_arrow_body_" + state), head = Kit("ui_drag_arrow_head_" + state);
            Vector2 Point(float t) => (1 - t) * (1 - t) * from + 2 * (1 - t) * t * bend + t * t * to;
            for (int k = 1; k <= dots; k++)
            {
                float t = k / (float)dots, size = k == dots ? 26 : 8 + 8 * t; var p = Point(t);
                if (body == null || head == null) { Deco(dragLayer, "ArrowDot", k == dots ? Diamond : Pill(0), p.x - size / 2, p.y - size / 2, size, size, color); continue; }
                // The kit's segments follow the curve (art forward is up), the head at its end; frame y runs down, so the turn flips.
                var along = Point(t) - Point(t - 1f / dots); float turn = Mathf.Atan2(-along.y, along.x) * Mathf.Rad2Deg - 90;
                float w = k == dots ? 52 : 12 + 8 * t, h2 = k == dots ? 52 : Math.Max(12, along.magnitude + 4);
                var piece = Deco(dragLayer, k == dots ? "ArrowHead" : "ArrowBody", k == dots ? head : body, p.x - w / 2, p.y - h2 / 2, w, h2, Color.white);
                piece.pivot = new Vector2(0.5f, 0.5f); piece.anchoredPosition += new Vector2(w / 2, -h2 / 2); piece.localRotation = Quaternion.Euler(0, 0, turn);
            }
        }
        // While a drag would send a request: Core's forecast of exactly that request beside the pointer and, on the last enemy standing,
        // the HP it would take off as a white part of its bar. Computed once per card, mode and target while the drag stays over them.
        (string Key, BattlePlayForecast? Forecast) dragForecast;
        void DragForecast(string uid, string mode, string? aim, Vector2 at)
        {
            if (dragLayer == null || run == null) return;
            string key = uid + "|" + mode + "|" + aim;
            if (dragForecast.Key != key)
            {
                BattlePlayForecast? made; try { made = run.BattleForecast(uid, mode, aim ?? target); } catch (RuleException) { made = null; }
                dragForecast = (key, made);
            }
            var f = dragForecast.Forecast; if (f == null) return;
            string text = ForecastText(f); float w = Math.Min(560, TextWidth(text, 18) + 28);
            var tip = Plate(dragLayer, "DragForecast", Mathf.Clamp(at.x - w / 2, 8, 1592 - w), Math.Max(8, at.y - 86), w, 40, Alpha(Background, 0.94f), ForecastColor(f), 2, true);
            Label(tip, text, 14, 0, w - 28, 40, 18, ForecastColor(f), FontStyle.Normal, TextAnchor.MiddleCenter);
            // Core's EnemyHpLoss is the sum over every enemy, so the bar shows it only when the target is the one enemy left standing.
            var e = aim == null ? null : run.State.Battle?.Enemies.FirstOrDefault(x => x.Uid == aim);
            if (run.State.Battle?.Enemies.Count(x => x.Alive) != 1) e = null;
            if (e != null && f.Error == null && f.EnemyHpLoss > 0 && e.MaxHp > 0 && enemyBars.TryGetValue(e.Uid, out var fill) && fill != null)
            {
                var track = FrameRect((RectTransform)fill.parent); float now = track.width * e.Hp / e.MaxHp, after = track.width * Math.Max(0, e.Hp - f.EnemyHpLoss) / e.MaxHp;
                Box(dragLayer, "ForecastLoss", track.x + after, track.y, now - after, track.height, new Color(1, 1, 1, 0.85f));
            }
        }
        void EndCardDrag(string uid, Vector2 screen, Camera? eye, bool drop)
        {
            dragForecast = default;
            if (dragLayer != null) Destroy(dragLayer.gameObject);
            dragLayer = null; bool ours = dragUid == uid; dragUid = null;
            if (holdUid == uid) { holdUid = null; holdTicket++; } // another card's pending hold stays
            bool played = false;
            if (drop && ours)
            {
                var (mode, aim, _) = DropTarget(uid, screen, eye);
                playingUid = uid; try { played = mode != null && DragPlay(uid, mode, aim); } finally { playingUid = null; }
            }
            HideOwnerBack(uid, played); // after the play's redraw, which drew the owner again
        }

        // ───────── the card's owner from behind (CARD-HOLD-BACKVIEW-20261019, art/requests/card-hold-backview-20261019.md) ─────────
        // While a hand card is held (0.2 s) or aimed, its owner stands at the stage's lower left seen from behind, facing the enemies:
        // up in 0.18 s and a slow breath while held; on letting go a short push and down after a play, straight down otherwise. The
        // figure is state, not an object: every redraw draws it again (still up, or still leaving), as `flight` survives a play's
        // redraw. Calm (동작 줄이기): no motion, the figure just appears and goes. Codex's back-view art (Characters/char_<id>_back_ready,
        // _back_act) is drawn as it is. A play through the pick sheet leaves as a cancel; the sheet's later commit shows no figure.
        // ponytail: until that art exists the owner's SD stands in as a dark silhouette; drop the fallback once all ten have art.
        // backUid: the card whose owner is shown. A newer card (a second finger's aim) replaces it, and only that card takes it away.
        string? backWho, backUid; (string Who, bool Played, int Ticket)? backLeaving; int backTicket, holdTicket;
        RectTransform? ownerBack; string? holdUid, playingUid; int stageEnd;
        // Returns the press's ticket (0: not a hold); its release asks TakeLongPress with it.
        int HoldCard(string uid)
        {
            if (!CardInPlay(uid)) return 0;
            HidePeek();
            holdUid = uid; int ticket = ++holdTicket; StartCoroutine(ShowOwnerBackAfter(uid, ticket));
            if (!Showing) StartCoroutine(PeekAfter(uid, ticket));
            return ticket;
        }
        // A long press (still held after 0.5 s, not dragged) lays the card's whole current text (as 카드 설명 shows it) over the stage
        // while it is held, without a redraw, so the owner's figure and the hand stay as they are. Letting go of that card, a drag, a new
        // press, focus loss or a redraw takes it away, and that press's own release taps nothing (kept per press, so another finger's
        // press in between cannot give it its tap back). A press made while an action's display or a turn summary is showing is the
        // held next-card press (its release chooses the card once that display ends), so it never peeks.
        const float LongPressSeconds = 0.5f; string? peekUid; RectTransform? peek; readonly HashSet<int> peekedPresses = new HashSet<int>();
        bool Showing => actionRows != null || turnSummary != null;
        IEnumerator PeekAfter(string uid, int ticket)
        {
            yield return new WaitForSecondsRealtime(LongPressSeconds);
            if (ticket != holdTicket || holdUid != uid || dragUid != null || Showing || frame == null || !CardInPlay(uid)) yield break;
            var info = HandCardInfo(uid);
            if (info == null) yield break;
            peek = Plate(frame, "CardPeek", 380, 110, 840, 430, Panel, Memory, 2);
            Label(peek, info.Title, 24, 16, 792, 44, 30, Ink, FontStyle.Bold);
            Label(peek, info.Facts, 24, 64, 792, 34, 19, Muted);
            Label(peek, info.Text, 24, 108, 792, 302, 22, Ink);
            foreach (var graphic in peek.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false; // it never takes a touch
            peekUid = uid; peekedPresses.Add(ticket);
        }
        void HidePeek(string? uid = null)
        {
            if (uid != null && uid != peekUid) return; // another card's release leaves this card's peek
            if (peek != null) Destroy(peek.gameObject);
            peek = null; peekUid = null;
        }
        bool TakeLongPress(int press) => peekedPresses.Remove(press);
        IEnumerator ShowOwnerBackAfter(string uid, int ticket)
        {
            yield return new WaitForSecondsRealtime(0.2f);
            if (ticket == holdTicket && holdUid == uid && CardInPlay(uid)) ShowOwnerBack(uid); // a newer press or a release voids it
        }
        // A release (or a view replaced mid-press) always takes away its own card's owner; another card's stays. During the card's
        // own drop, its play's redraw replaces the card: the drop then decides how the owner leaves.
        void ReleaseCard(string uid)
        {
            if (uid == playingUid) return;
            if (holdUid == uid) { holdUid = null; holdTicket++; }
            HidePeek(uid);
            HideOwnerBack(uid, false);
        }
        void ShowOwnerBack(string uid)
        {
            if (backUid == uid) return;
            if (backUid != null) HideOwnerBack(backUid, false);
            string? who = CardOwner(uid); var drawn = who == null ? null : DrawOwnerBack(who);
            if (drawn == null) return;
            backWho = who; backUid = uid; backLeaving = null; ownerBack = drawn;
            if (!Calm) StartCoroutine(OwnerBackUp(drawn, true));
        }
        void HideOwnerBack(string uid, bool played)
        {
            if (backWho == null || backUid != uid) return;
            var leave = (backWho, played, ++backTicket); backWho = null; backUid = null; backLeaving = leave;
            var shown = ownerBack; ownerBack = null;
            if (shown != null) StartCoroutine(OwnerBackOut(shown, leave)); else backLeaving = null;
        }
        // After every redraw (Animate): the figure again in the new view, or nothing once the battle is over.
        void RedrawOwnerBack(bool battle)
        {
            if (!battle) { backWho = null; backUid = null; backLeaving = null; return; }
            if (backWho != null) { ownerBack = DrawOwnerBack(backWho); if (ownerBack != null && !Calm) StartCoroutine(OwnerBackUp(ownerBack, false)); }
            else if (backLeaving is { } leave && DrawOwnerBack(leave.Who) is { } again) StartCoroutine(OwnerBackOut(again, leave));
        }
        RectTransform? DrawOwnerBack(string who)
        {
            if (frame == null) return null;
            var back = Art("Characters/char_" + who + "_back_ready", true);
            var art = back ?? Art("Characters/char_" + who + "_sd", true); if (art == null) return null;
            const float height = 560; float width = height * art.rect.width / art.rect.height;
            var image = Picture(frame, "OwnerBack:" + who, art, 24, 900 - height + 40, width, height, back != null ? Color.white : new Color(0.05f, 0.06f, 0.1f, 1f))!;
            image.raycastTarget = false; image.transform.SetSiblingIndex(Math.Min(stageEnd, frame.childCount - 1));
            if (back == null) { var rim = image.gameObject.AddComponent<Outline>(); rim.effectColor = Alpha(Memory, 0.85f); rim.effectDistance = new Vector2(3, -3); } // a backlit edge
            var group = image.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
            return image.rectTransform;
        }
        IEnumerator OwnerBackUp(RectTransform shown, bool enter)
        {
            bool Gone() => ownerBack != shown || Calm;
            if (enter)
            {
                StartCoroutine(MemoryForgeUiMotion.Fade(shown.GetComponent<CanvasGroup>(), 0, 1, 0.18f, Gone));
                yield return MemoryForgeUiMotion.SlideIn(shown, new Vector2(-60, -140), 0.18f, Gone);
            }
            if (shown == null) yield break; // a redraw during the slide draws it again
            var home = shown.anchoredPosition;
            for (float t = 0; shown != null && !Gone(); t += Time.unscaledDeltaTime)
            {
                shown.anchoredPosition = home + new Vector2(0, 4 * Mathf.Sin(t * Mathf.PI * 2 / 1.6f));
                yield return null;
            }
            if (shown != null && ownerBack == shown) shown.anchoredPosition = home; // calm settles it where it stands
        }
        IEnumerator OwnerBackOut(RectTransform shown, (string Who, bool Played, int Ticket) leave)
        {
            if (!Calm && leave.Played)
            {
                var act = Art("Characters/char_" + leave.Who + "_back_act", true); var image = shown.GetComponent<Image>();
                if (act != null && image != null) image.sprite = act;
                yield return MoveBy(shown, null, new Vector2(30, 24), 0.12f, Still); // a short push toward the enemies
            }
            if (!Calm && shown != null) yield return MoveBy(shown, shown.GetComponent<CanvasGroup>(), new Vector2(-40, -160), leave.Played ? 0.2f : 0.15f, Still);
            if (shown == null) yield break; // a redraw cut it short: the redrawn copy finishes leaving
            Destroy(shown.gameObject);
            if (backLeaving?.Ticket == leave.Ticket) backLeaving = null;
        }
        // Eases a rect by an anchored offset (y up), fading the group out with it when one is given; stopped, it stays where it is.
        static IEnumerator MoveBy(RectTransform rect, CanvasGroup? fade, Vector2 by, float duration, Func<bool> stop)
        {
            if (rect == null) yield break;
            var from = rect.anchoredPosition; float alpha = fade != null ? fade.alpha : 1;
            for (float t = 0; t < duration && !stop(); t += Time.unscaledDeltaTime)
            {
                if (rect == null) yield break;
                float u = MemoryForgeUiMotion.EaseOut(t / duration);
                rect.anchoredPosition = from + by * u; if (fade != null) fade.alpha = alpha * (1 - u);
                yield return null;
            }
        }

        // What a drop at this screen point asks for, and the zone it lights: a living enemy's slot (where it is shown) plays now on it,
        // a rail group reserves for that time, and the rest of the stage plays now a card that needs no target (or, with one enemy left,
        // on that one). The card row and the hand ask for nothing.
        (string? Mode, string? Aim, RectTransform? Zone) DropTarget(string uid, Vector2 screen, Camera? eye)
        {
            var b = run?.State.Battle; if (frame == null || b == null) return (null, null, null);
            foreach (var e in b.Enemies)
                if (e.Alive && enemyRoots.TryGetValue(e.Uid, out var slot) && slot != null && Shown(slot, screen, eye)) return ("now", e.Uid, slot);
            foreach (var pair in railZones)
                if (pair.Value != null && Shown(pair.Value, screen, eye)) return (pair.Key, null, pair.Value);
            var at = ScreenToFrame(screen, eye);
            if (at.x >= 32 && at.x <= 1568 && at.y >= StageTop && at.y < ActionsTop && partyPanel != null) // the stage itself, never a letterbox
            {
                if (run!.BattleNeedsTarget(uid) == null) return ("now", null, partyPanel);
                var alive = b.Enemies.Where(e => e.Alive).ToList();
                if (alive.Count == 1 && enemyRoots.TryGetValue(alive[0].Uid, out var only) && only != null && Shown(only, RectTransformUtility.WorldToScreenPoint(eye, only.TransformPoint(only.rect.center)), eye))
                    return ("now", alive[0].Uid, only);
            }
            return (null, null, null);
        }
        // Inside the rect and every mask above it (a slot scrolled out of the stage band is not a target).
        static bool Shown(RectTransform r, Vector2 screen, Camera? eye) =>
            RectTransformUtility.RectangleContainsScreenPoint(r, screen, eye) && r.GetComponentsInParent<RectMask2D>().All(m => RectTransformUtility.RectangleContainsScreenPoint(m.rectTransform, screen, eye));
        /// <summary>Plays or reserves a hand card as a drop asks (mode "now" with an optional enemy, or "next"/"after"): selects the
        /// card, its target and the mode, then confirms exactly like the card row's confirm (the same checks, pick sheet and
        /// player-action display rules). A mode Core does not allow for the card leaves it selected with a notice instead. True only
        /// when the play was committed.</summary>
        public bool DragPlay(string uid, string mode, string? aim)
        {
            if (Locked() || run == null) return false;
            selectedCard = uid; if (aim != null) target = aim; ClearIntentNotice();
            bool allowed; try { allowed = run.BattleAllowedModes(uid).Contains(mode); } catch (RuleException) { allowed = false; }
            if (!allowed)
            {
                selectedMode = null; notice = mode == "now" ? "이 카드는 바로 쓸 수 없어요. 시간표에 놓아 예약하세요." : "이 카드는 예약할 수 없어요. 적에게 놓아 바로 쓰세요.";
                noticeUntil = Time.realtimeSinceStartup + 2.6f; Render(); return false;
            }
            selectedMode = mode;
            if (ConfirmCard()) return true;
            if (picker == null) Render();
            return false;
        }
        Vector2 ScreenToFrame(Vector2 screen, Camera? eye)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(frame!, screen, eye, out var local);
            return new Vector2(local.x + 800, 450 - local.y);
        }
        // A rect's bounds in the frame's top-left coordinates.
        Rect FrameRect(RectTransform r)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            Vector3 a = frame!.InverseTransformPoint(corners[0]), c = frame.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Math.Min(a.x, c.x) + 800, 450 - Math.Max(a.y, c.y), Math.Max(a.x, c.x) + 800, 450 - Math.Min(a.y, c.y));
        }
        // A hand card's drag: upward, it aims and plays on release; otherwise every drag event goes on to the hand's own scroll, so
        // sideways drags still scroll it. A view replaced mid-drag (its card disabled) drops nothing.
        sealed class CardDrag : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
        {
            MemoryForgeExpeditionClient? owner; string uid = ""; ScrollRect? scroll; bool aiming;
            readonly Dictionary<int, int> presses = new Dictionary<int, int>(); // each pointer's own press ticket, for its release
            public void Bind(MemoryForgeExpeditionClient client, string card, ScrollRect? hand) { owner = client; uid = card; scroll = hand; }
            // One left pointer holds the card (its owner shows from behind) and one aims it; every event checks it comes from that
            // pointer, so another finger or button only scrolls the hand. An aim's release goes through OnEndDrag (sent after OnPointerUp).
            int holder = int.MinValue, aimer;
            bool Holds(PointerEventData e) => e.button == PointerEventData.InputButton.Left && e.pointerId == holder;
            bool Aims(PointerEventData e) => aiming && e.pointerId == aimer;
            public void OnPointerDown(PointerEventData e) { if (e.button != PointerEventData.InputButton.Left || owner == null) return; holder = e.pointerId; presses[e.pointerId] = owner.HoldCard(uid); }
            public void OnPointerUp(PointerEventData e)
            {
                // A press that showed its details taps nothing on its own release, even after another finger pressed this card since.
                if (e.button == PointerEventData.InputButton.Left && presses.TryGetValue(e.pointerId, out int press))
                { presses.Remove(e.pointerId); if (owner != null && owner.TakeLongPress(press)) e.eligibleForClick = false; }
                if (!Holds(e)) return;
                holder = int.MinValue;
                if (!aiming && owner != null) owner.ReleaseCard(uid);
            }
            public void OnInitializePotentialDrag(PointerEventData e) { if (scroll != null) scroll.OnInitializePotentialDrag(e); }
            public void OnBeginDrag(PointerEventData e)
            {
                e.eligibleForClick = false; // a drag never also presses the card (the module keeps a click when press and drag share an object)
                if (aiming) { if (scroll != null) scroll.OnBeginDrag(e); return; } // the card is already aimed by another pointer
                // Any upward drag aims, unless the hand scrolls and the drag runs mostly sideways (more than twice as wide as tall).
                var moved = e.position - e.pressPosition; bool sideways = scroll != null && scroll.horizontal && Mathf.Abs(moved.x) > moved.y * 2;
                aiming = e.button == PointerEventData.InputButton.Left && moved.y > 0 && !sideways && owner != null && owner.BeginCardDrag(uid);
                if (aiming) { aimer = e.pointerId; owner!.CardDragTo(e.position, e.pressEventCamera); return; }
                if (Holds(e)) owner?.ReleaseCard(uid); // the holding finger scrolling the hand is no longer a hold
                if (scroll != null) scroll.OnBeginDrag(e);
            }
            public void OnDrag(PointerEventData e) { if (Aims(e)) owner!.CardDragTo(e.position, e.pressEventCamera); else if (scroll != null) scroll.OnDrag(e); }
            public void OnEndDrag(PointerEventData e)
            {
                if (Aims(e)) { aiming = false; owner!.EndCardDrag(uid, e.position, e.pressEventCamera, true); }
                else if (scroll != null) scroll.OnEndDrag(e);
            }
            // A view replaced mid-press never sends this card its release: end the aim, or the hold, here.
            void OnDisable() { if (aiming && owner != null) owner.EndCardDrag(uid, Vector2.zero, null, false); else owner?.ReleaseCard(uid); aiming = false; }
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
            float row = Math.Max(54, minTouch), pitch = row + 8; // rows grow to 48dp on a phone
            var list = Scroll(p, "DeckPickList", 24, 100, 1232, 500, 1232, ((s.Deck.Count + 2) / 3) * pitch, Panel);
            for (int i = 0; i < s.Deck.Count; i++)
            {
                var card = s.Deck[i]; string key = card.Key ?? ""; bool ok = Pickable(card); int order = deckPicks.IndexOf(key);
                Button(list, "DeckCard:" + key, (order >= 0 ? (order + 1) + ". " : "") + DeckCardText(card) + (ok ? "" : " · 선택 불가"), i % 3 * 412, i / 3 * pitch, 400, row,
                    () => ToggleDeckPick(key, required), order >= 0 ? Memory : Line, 18).interactable = ok;
            }
            string verb = purpose == Purpose.Upgrade ? "강화하기" : purpose == Purpose.ForgeOffer ? "이 카드에 변주" : purpose == Purpose.FoundPart ? "부품 붙이기"
                : purpose == Purpose.EventCards ? "선택 완료" : "제거하기";
            Button(p, "ConfirmDeckPick", verb + (required > 1 ? " (" + deckPicks.Count + "/" + required + ")" : ""), 24, 620, 420, 64, ConfirmDeckPick, deckPicks.Count == required ? Brass : Line, 22, hit: Band(24, 606, 420, Math.Max(minTouch, 80)))
                .interactable = required > 0 && deckPicks.Count == required;
            Button(p, "DeckPickInfo", deckPicks.Count != 0 ? "고른 카드 설명" : "카드를 고르면 설명", 470, 620, 300, 64, () => ShowDeckCardInfo(deckPicks.LastOrDefault() ?? ""), Line, 21, hit: Band(470, 606, 300, Math.Max(minTouch, 80)))
                .interactable = deckPicks.Count != 0;
            Button(p, "CancelDeckPick", "취소", 1032, 620, 224, 64, CloseSheet, Line, 22, hit: Band(1032, 606, 224, Math.Max(minTouch, 80))); // below the list
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
            Button(p, "CloseDeck", "닫기", 1110, 12, 146, 52, CloseSheet, Line, 22, hit: Band(1110, 64 - minTouch, 146, minTouch)); // grows upward, clear of the list
            float row = Math.Max(50, minTouch), pitch = row + 6, partRow = Math.Max(56, minTouch), partPitch = partRow + 10; // rows grow to 48dp on a phone
            var list = Scroll(p, "DeckList", 24, 76, 1232, 430, 1232, ((s.Deck.Count + 2) / 3) * pitch, Panel);
            for (int i = 0; i < s.Deck.Count; i++)
            {
                string key = s.Deck[i].Key ?? "";
                Button(list, "DeckInfo:" + key, DeckCardText(s.Deck[i]), i % 3 * 412, i / 3 * pitch, 400, row, () => ShowDeckCardInfo(key), Line, 18, hit: new Rect(i % 3 * 412, i / 3 * pitch, 400, row));
            }
            Label(p, "보유 부품 " + s.PartsFound.Count + "개" + (s.PartsFound.Count != 0 ? " · 회로·각인은 카드마다 1칸씩, 같은 칸에 붙이면 교체돼요." : ""), 24, 520, 1232, 30, 20, Memory);
            var parts = Scroll(p, "FoundParts", 24, 558, 1232, 140, 1232, ((s.PartsFound.Count + 2) / 3) * partPitch, Panel);
            for (int i = 0; i < s.PartsFound.Count; i++)
            {
                string id = s.PartsFound[i];
                Button(parts, "FoundPart:" + i, PartName(id) + " · " + SlotName(id) + " → 카드에 붙이기", i % 3 * 412, i / 3 * partPitch, 400, partRow,
                    () => OpenDeckPick(Purpose.FoundPart, "「" + PartName(id) + "」을 붙일 카드를 고르세요", 1, -1, id), Line, 18).interactable = !Finished(s);
            }
        }
        // The effect text is one label with Core's exact string; every block grows to its wrapped height and the body scrolls, so long
        // names and nested conditions are never cut off.
        void CardInfoSheet(CardInfo info)
        {
            var p = OverlayPanel("CardInfo", 240, 80, 1120, 740);
            Label(p, info.Title, 24, 14, 920, 52, 30, Ink, FontStyle.Bold);
            Button(p, "CloseCardInfo", "닫기", 960, 12, 136, 56, CloseCardInfo, Line, 22, hit: Band(960, 68 - minTouch, 136, minTouch));
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
            Button(p, "CloseLog", "닫기", 856, 8, 122, 48, CloseSheet, Line, 22, hit: Band(978 - Math.Max(122, minTouch), 56 - minTouch, Math.Max(122, minTouch), minTouch));
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
            // On a phone their touch areas reach down to the frame's bottom edge (the sheet starts at y 60), not past it.
            Button(p, "SkipTurnSummary", "남은 결과 모두 보기", 40, 760, 340, 60, () => SkipTurnSummary(), Line, 22, hit: Band(40, 840 - minTouch, 340, minTouch));
            Button(p, "ContinueTurnSummary", "계속", 400, 760, 380, 60, () => ContinueTurnSummary(), Brass, 24, hit: Band(400, 840 - minTouch, 380, minTouch));
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
            float row = Math.Max(52, minTouch), pitch = row + 12; // rows grow to 48dp on a phone
            var list = Scroll(p, "AssignList", 24, 100, 1032, 420, 1032, commons.Count * pitch, Panel);
            for (int i = 0; i < commons.Count; i++)
            {
                var card = commons[i]; string key = card.Key ?? "";
                Label(list, CardName(card.Id), 8, i * pitch + 10, 400, 40, 22, Ink);
                for (int k = 0; k < s.Party.Count; k++)
                {
                    string cid = s.Party[k].CharId;
                    Button(list, "Assign:" + key + ":" + cid, CharacterName(cid), 420 + k * 204, i * pitch + 4, 192, row, () => Assign(key, cid), card.Assigned == cid ? Memory : Line, 21);
                }
            }
            Button(p, "CloseAssign", "닫기", 832, 540, 224, 60, CloseSheet, Line, 22, hit: Band(832, 524, 224, Math.Max(minTouch, 76))); // below the list
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

        // ───────── uGUI helpers (top-left placement in the parent's space, y down; no image takes a raycast unless it is a control) ─────────
        RectTransform OverlayPanel(string name, float x, float y, float width, float height)
        {
            var blocker = Box(frame!, name + "InputBlocker", 0, 0, 1600, 900, new Color(0.02f, 0.035f, 0.07f, 0.8f));
            blocker.GetComponent<Image>().raycastTarget = true;
            var panel = Frame(blocker, name, x, y, width, height, Alpha(Brass, 0.55f), Panel);
            panel.GetComponent<Image>().raycastTarget = true;
            // A sheet that was not open in the previous view fades in; its blocker takes input from the first frame.
            sheetsDrawn.Add(name);
            if (!sheetsBefore.Contains(name) && Application.isPlaying && !Calm) StartCoroutine(MemoryForgeUiMotion.Fade(blocker.gameObject.AddComponent<CanvasGroup>(), 0, 1, 0.16f, Still));
            return panel;
        }
        RectTransform Scroll(Transform parent, string name, float x, float y, float width, float height, float contentWidth, float contentHeight, Color color)
        {
            var viewport = Box(parent, name, x, y, width, height, color); viewport.GetComponent<Image>().raycastTarget = true; viewport.gameObject.AddComponent<RectMask2D>();
            var content = Box(viewport, name + "Content", 0, 0, Math.Max(width, contentWidth), Math.Max(height, contentHeight), Color.clear);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = contentWidth > width; scroll.vertical = contentHeight > height; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 25;
            return content;
        }
        void Connector(Transform parent, Vector2 from, Vector2 to, Color color, float thickness = 3)
        {
            var obj = new GameObject("Path", typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false);
            var r = obj.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 0.5f);
            Vector2 delta = new Vector2(to.x - from.x, from.y - to.y); r.anchoredPosition = new Vector2(from.x, -from.y); r.sizeDelta = new Vector2(delta.magnitude, thickness);
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
            actionText = null; actionStatus = null; actionSkip = null; confirmCaption = null; actionOverlay = null; dragLayer = null; dragUid = null; ownerBack = null; holdUid = null; peek = null; peekUid = null; peekedPresses.Clear(); actionGated.Clear(); paceButtons.Clear();
        }
        RectTransform Box(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false);
            var r = obj.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height);
            var image = obj.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return r;
        }
        // A rounded panel (procedural 9-sliced corner) or a pill (half-height ends) with an optional edge: a ring child named "Edge",
        // stretched to the panel, drawn above the fill and below every later child (so a translucent fill never shows an edge colour
        // through it).
        RectTransform Plate(Transform parent, string name, float x, float y, float width, float height, Color fill, Color? edge = null, float edgeWidth = 1, bool pill = false)
        {
            var r = Box(parent, name, x, y, width, height, fill);
            // A pill's 31 px ends always draw at half its height; a rounded corner at 10 units (5.6 on a small chip).
            float scale = pill ? 62f / Math.Max(1, Math.Min(width, height)) : Math.Min(width, height) < 36 ? 2.5f : 1.4f;
            var image = r.GetComponent<Image>(); image.sprite = pill ? Pill(0) : Rounded(0); image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = scale;
            if (edge.HasValue && edgeWidth > 0)
            {
                var ring = Box(r, "Edge", 0, 0, 0, 0, edge.Value); ring.anchorMin = Vector2.zero; ring.anchorMax = Vector2.one; ring.offsetMin = ring.offsetMax = Vector2.zero;
                var line = ring.GetComponent<Image>(); line.sprite = pill ? Pill(edgeWidth * scale) : Rounded(edgeWidth * scale); line.type = Image.Type.Sliced; line.pixelsPerUnitMultiplier = scale;
            }
            return r;
        }
        // A framed panel: the glass fill under a soft top sheen and an ornate edge (a thin ring with a diamond in each corner, one
        // procedural 9-sliced sprite until Codex's panel frame arrives). Decoration only, never a raycast.
        RectTransform Frame(Transform parent, string name, float x, float y, float width, float height, Color? edge = null, Color? fill = null)
        {
            var r = Plate(parent, name, x, y, width, height, fill ?? Glass);
            Sheen(r, width, height, 0.05f);
            var ring = Box(r, "Edge", 0, 0, 0, 0, edge ?? Alpha(Brass, 0.55f)); ring.anchorMin = Vector2.zero; ring.anchorMax = Vector2.one; ring.offsetMin = ring.offsetMax = Vector2.zero;
            var line = ring.GetComponent<Image>(); var kit = Kit("ui_panel_frame", new Vector4(48, 48, 48, 48));
            if (kit != null) { line.sprite = kit; line.color = Color.white; line.pixelsPerUnitMultiplier = 2f; } else { line.sprite = Ornate; line.pixelsPerUnitMultiplier = 1.4f; }
            line.type = Image.Type.Sliced;
            ring.SetSiblingIndex(0);
            return r;
        }
        // A plate drawn with a piece of the UI kit (9-sliced; the source height sets the scale), or the procedural pill/plate without it.
        RectTransform KitPlate(Transform parent, string name, string id, Vector4 border, float sourceHeight, float x, float y, float width, float height, Color fill, Color? edge)
        {
            var art = Kit(id, border);
            if (art == null) return Plate(parent, name, x, y, width, height, fill, edge, 1, height <= 64);
            var r = Box(parent, name, x, y, width, height, Color.white);
            var image = r.GetComponent<Image>(); image.sprite = art; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = sourceHeight / Math.Max(1, Math.Min(height, sourceHeight));
            return r;
        }
        // A light from above: white fading downwards over the top of a face (a fill of the same shape, so it follows its corners).
        void Sheen(RectTransform r, float width, float height, float alpha)
        {
            Deco(r, "Sheen", Fade(false), 2, 2, Math.Max(0, width - 4), Math.Min(height * 0.55f, 64), new Color(1, 1, 1, alpha));
        }
        // A button's gloss: a white lozenge of the face's own shape over its upper half.
        void Gloss(RectTransform r, float width, float height, bool pill, float alpha)
        {
            float w = Math.Max(0, width - 8), h = Math.Max(0, Math.Min(height * 0.48f, 40));
            var image = Deco(r, "Gloss", pill ? Pill(0) : Rounded(0), 4, 3, w, h, new Color(1, 1, 1, alpha)).GetComponent<Image>();
            image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = pill ? 62f / Math.Max(1, Math.Min(w, h)) : Math.Min(w, h) < 36 ? 2.5f : 1.4f;
        }
        // A decoration image (a procedural shape or art), never a raycast target.
        RectTransform Deco(Transform parent, string name, Sprite sprite, float x, float y, float width, float height, Color color)
        {
            var r = Box(parent, name, x, y, width, height, color); r.GetComponent<Image>().sprite = sprite; return r;
        }
        // A pill sized to its text (statuses, counts, tags); its width is returned through sizeDelta.
        RectTransform Chip(Transform parent, string name, string text, float x, float y, float height, int size, Color ink, Color fill, Color? edge = null)
        {
            float width = TextWidth(text, size) + height * 0.8f + 6;
            var r = Plate(parent, name, x, y, width, height, fill, edge, 1, true);
            Label(r, text, 0, 0, width, height, size, ink, FontStyle.Normal, TextAnchor.MiddleCenter);
            return r;
        }
        // A round gem (outer rim, a lighter face and a glint): the cost and action-point mark, the medals and the party gem.
        RectTransform Gem(Transform parent, string name, float x, float y, float diameter, Color color, Color rim)
        {
            var r = Deco(parent, name, Pill(0), x, y, diameter, diameter, rim);
            Deco(r, "Face", GemFace, diameter * 0.09f, diameter * 0.09f, diameter * 0.82f, diameter * 0.82f, color);
            return r;
        }
        // Numbers and titles drawn over art or glow get a dark drop shadow, so they hold like a game's outlined digits.
        static Text Strong(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, 0.75f); shadow.effectDistance = new Vector2(0, -2); return text;
        }
        // The colour of a card owner's frame: the member's attribute; common cards brass, curses red, weapons steel.
        Color OwnerColor(string owner) => characterAttrs.TryGetValue(owner, out var attr) ? AttributeColor(attr) : owner == "curse" ? Danger : owner == "weapon" ? Hex("9aa7bf") : Brass;
        static Color AttributeColor(string attr) => attr == "volt" ? Hex("f2d14b") : attr == "flame" ? Hex("ff8a4c") : attr == "frost" ? Hex("7fd0ff") : attr == "void" ? Hex("b78cff") : Muted;
        static Image? EdgeOf(RectTransform r) => r.Find("Edge")?.GetComponent<Image>();
        Text Label(Transform parent, string value, float x, float y, float width, float height, int size, Color color, FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.UpperLeft)
        {
            var obj = new GameObject("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            var r = obj.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height);
            // Only the Regular face ships: synthetic bold is kept for sizes where it stays crisp (22+).
            var text = obj.GetComponent<Text>(); text.font = font; text.text = value; text.fontSize = size; text.color = color; text.fontStyle = size >= 22 ? style : FontStyle.Normal; text.alignment = align; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = Math.Max(14, (int)(size * 0.7f)); text.resizeTextMaxSize = size; return text;
        }
        // Sizes a wrapped label to its whole text at its own font size and returns that height (TextHeight's measure).
        float Grow(Text text)
        {
            text.resizeTextForBestFit = false;
            var r = text.rectTransform;
            float height = TextHeight(text.text, text.fontSize, r.sizeDelta.x, text.fontStyle);
            r.sizeDelta = new Vector2(r.sizeDelta.x, height); return height;
        }
        // The height of a wrapped text at this canvas's scale (the one it is drawn at; a scale change redraws the view): Text.
        // preferredHeight's own generator settings, measured 3% narrower than drawn, plus a margin. Cached per scale, size and width,
        // so a redraw of the same screen measures nothing again.
        float TextHeight(string value, int size, float width, FontStyle style = FontStyle.Normal)
        {
            if (value.Length == 0) return 0;
            float scale = canvasRoot != null ? Mathf.Max(0.01f, canvasRoot.GetComponent<Canvas>().scaleFactor) : 1f;
            string key = scale.ToString("F4") + "|" + size + "|" + (int)style + "|" + width.ToString("F1") + "|" + value;
            if (heights.TryGetValue(key, out float known)) return known;
            measure ??= new TextGenerator();
            var settings = new TextGenerationSettings
            {
                font = font, fontSize = size, fontStyle = style, lineSpacing = 1, richText = true, scaleFactor = scale, color = Color.white, textAnchor = TextAnchor.UpperLeft,
                generationExtents = new Vector2(width * 0.97f, 0), pivot = new Vector2(0, 1), horizontalOverflow = HorizontalWrapMode.Wrap, verticalOverflow = VerticalWrapMode.Overflow,
                resizeTextForBestFit = false, updateBounds = false, generateOutOfBounds = true, alignByGeometry = false
            };
            float height = Mathf.Ceil(measure.GetPreferredHeight(value, settings) / scale) + 4;
            if (heights.Count > 4000) heights.Clear(); // ponytail: whole-cache reset; an LRU if long sessions ever thrash it
            heights[key] = height; return height;
        }
        // The one-line width of a text at this canvas's scale (cached like TextHeight), for chips sized to their words.
        float TextWidth(string value, int size, FontStyle style = FontStyle.Normal)
        {
            if (value.Length == 0) return 0;
            float scale = canvasRoot != null ? Mathf.Max(0.01f, canvasRoot.GetComponent<Canvas>().scaleFactor) : 1f;
            string key = "w|" + scale.ToString("F4") + "|" + size + "|" + (int)style + "|" + value;
            if (heights.TryGetValue(key, out float known)) return known;
            measure ??= new TextGenerator();
            var settings = new TextGenerationSettings
            {
                font = font, fontSize = size, fontStyle = style, lineSpacing = 1, richText = true, scaleFactor = scale, color = Color.white, textAnchor = TextAnchor.UpperLeft,
                generationExtents = new Vector2(10000, 0), pivot = new Vector2(0, 1), horizontalOverflow = HorizontalWrapMode.Overflow, verticalOverflow = VerticalWrapMode.Overflow,
                resizeTextForBestFit = false, updateBounds = false, generateOutOfBounds = true, alignByGeometry = false
            };
            float width = Mathf.Ceil(measure.GetPreferredWidth(value, settings) / scale * 1.04f) + 4;
            if (heights.Count > 4000) heights.Clear();
            heights[key] = width; return width;
        }
        // Buttons: one hit target (the root image); the caption, art and decorations ignore raycasts. A one-line button up to 64 tall
        // is a pill, a taller or two-line one a rounded panel; a primary (brass, teal, red) face carries a gloss, a secondary (Line)
        // face a lighter edge. Pressed darkens, disabled greys. On a phone the hit area grows to 48dp
        // (minTouch) around the drawn face, or to the given hit rectangle where a neighbour needs it (lists keep their rows and grow
        // the rows instead): the root is then a transparent hit area and the drawn button its child "Face", which also sinks a little
        // about its centre while pressed (not with reduced motion), the hit area staying whole. Without a phone the root is the face.
        Button Button(Transform parent, string name, string caption, float x, float y, float width, float height, Action click, Color? color = null, int size = 24, Color? edge = null, float edgeWidth = 1, Rect? hit = null)
        {
            var fill = color ?? Line;
            var area = hit ?? new Rect(x - Math.Max(0, minTouch - width) / 2, y - Math.Max(0, minTouch - height) / 2, Math.Max(width, minTouch), Math.Max(height, minTouch));
            area = Rect.MinMaxRect(Math.Min(area.xMin, x), Math.Min(area.yMin, y), Math.Max(area.xMax, x + width), Math.Max(area.yMax, y + height)); // never less than the face
            bool pill = height <= 64 && width >= height && caption.IndexOf('\n') < 0;
            bool light = fill == Brass || fill == Memory || fill == Danger;
            Color? rim = edge ?? (fill == Line ? LineEdge : light ? Alpha(Color.white, 0.35f) : (Color?)null);
            bool grown = area.x < x - 0.5f || area.y < y - 0.5f || area.xMax > x + width + 0.5f || area.yMax > y + height + 0.5f;
            RectTransform r, root;
            // A brass or plain pill wears the kit's brass or ink pill (its own rim and gloss) when the kit is there.
            var kitPill = pill && edge == null && (fill == Brass || fill == Line) ? Kit(fill == Brass ? "ui_button_pill_brass" : "ui_button_pill_ink", new Vector4(64, 0, 64, 0)) : null;
            if (kitPill != null) rim = null;
            if (!grown) { root = r = Plate(parent, name, x, y, width, height, fill, rim, edgeWidth, pill); r.GetComponent<Image>().raycastTarget = true; }
            else
            {
                root = Box(parent, name, area.x, area.y, area.width, area.height, Color.clear); root.GetComponent<Image>().raycastTarget = true;
                r = Plate(root, "Face", x - area.x, y - area.y, width, height, fill, rim, edgeWidth, pill);
                r.GetComponent<Image>().raycastTarget = false;
            }
            if (kitPill != null) { var face = r.GetComponent<Image>(); Sideways(face, kitPill, 128, height); face.color = Color.white; }
            else if (light) Gloss(r, width, height, pill, 0.2f);
            var button = root.gameObject.AddComponent<Button>(); button.targetGraphic = r.GetComponent<Image>();
            if (grown && Application.isPlaying) root.gameObject.AddComponent<UiPress>().Bind(r, () => Calm);
            // Only a live, interactable button acts: a replaced view's button is inactive (RemoveView) until its deferred destroy.
            button.onClick.AddListener(() => { if (button != null && button.IsActive() && button.IsInteractable()) click(); });
            var colors = button.colors; colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f); colors.pressedColor = new Color(0.72f, 0.72f, 0.76f); colors.disabledColor = new Color(0.45f, 0.45f, 0.45f); button.colors = colors;
            float pad = pill ? Math.Max(6, height * 0.18f) : 6; // a pill's text keeps off its round ends
            if (caption.Length != 0) Label(r, caption, pad, 5, width - 2 * pad, height - 10, size, light ? Background : Ink, light ? FontStyle.Bold : FontStyle.Normal, TextAnchor.MiddleCenter);
            return button;
        }
        // The drawn part of a button (its face when the hit area is larger), where its own labels and art go.
        static RectTransform Inner(Button button) => button.targetGraphic.rectTransform;
        // A touch band (parent coordinates) for a control whose neighbours leave it no room to grow evenly, sized by the caller from
        // minTouch and always kept on the frame; none off phones. The button adds its own face to it.
        Rect? Band(float x, float y, float width, float height) => minTouch > 0 ? new Rect(x, y, width, height) : (Rect?)null;
        // The frame's bottom row of exits (Proceed, save/close): its touch areas start at ExitTop, so every scrolling panel ends above it.
        float ExitTop => 900 - Math.Max(108, minTouch);
        float PanelBottom => ExitTop - 4;
        Rect ProceedHit => new Rect(1288, ExitTop, 280, 900 - ExitTop);
        // Units of the 1600×900 frame that make 48dp on this screen: a phone's density over the frame's aspect-fit pixel scale. 0 off
        // phones (a mouse needs no finger-size targets), so desktop and test layouts are unchanged; TouchOverride lets a test act as one.
        public static float? TouchOverride;
        float minTouch;
        public static float TouchUnits()
        {
            if (TouchOverride.HasValue) return TouchOverride.Value;
            if (!Application.isMobilePlatform || Screen.dpi <= 0) return 0;
            var safe = Screen.safeArea; float pixels = Mathf.Min(safe.width / 1600f, safe.height / 900f);
            return pixels <= 0 ? 0 : 48f * Screen.dpi / 160f / pixels;
        }
        // A bar and its fill (returned, so a hit can drain the lost part behind it): a dark pill track, the coloured fill
        // and, on a thick bar, a highlight along its top.
        RectTransform Bar(Transform parent, string name, float x, float y, float width, float height, int value, int max, Color color)
        {
            var back = Plate(parent, name, x, y, width, height, Alpha(Background, 0.92f), null, 1, true);
            float filled = width * Mathf.Clamp01(max > 0 ? (float)value / max : 0);
            var fill = Plate(back, "Fill", 0, 0, filled, height, color, null, 1, true);
            if (height >= 12 && filled >= height) Deco(fill, "Shine", Fade(false), height * 0.4f, 1, Math.Max(0, filled - height * 0.8f), height * 0.45f, new Color(1, 1, 1, 0.35f));
            return fill;
        }
        // An art image fitted inside its rect (aspect kept); null, with nothing drawn, when the art is missing.
        Image? Picture(Transform parent, string name, Sprite? sprite, float x, float y, float width, float height, Color tint)
        {
            if (sprite == null) return null;
            var image = Box(parent, name, x, y, width, height, tint).GetComponent<Image>(); image.sprite = sprite; image.preserveAspect = true; return image;
        }
        Image? Icon(Transform parent, string name, string file, float x, float y, float width, float height, Color tint) => Picture(parent, name, Art("UI/" + file), x, y, width, height, tint);
        // An art image filling its rect (aspect kept, the overflow clipped), its visible window placed at focus (0 top … 1 bottom).
        void Cover(Transform parent, string name, Sprite? sprite, float x, float y, float width, float height, float focus)
        {
            var window = Box(parent, name, x, y, width, height, Color.clear); window.gameObject.AddComponent<RectMask2D>();
            if (sprite == null) return;
            float aspect = sprite.rect.width / sprite.rect.height, w = Math.Max(width, height * aspect), h = w / aspect;
            var image = Box(window, "Art", (width - w) / 2, -focus * (h - height), w, h, Color.white).GetComponent<Image>();
            image.sprite = sprite;
        }
        Outline Glow(RectTransform r, Color color)
        {
            var outline = r.gameObject.AddComponent<Outline>(); outline.effectColor = color; outline.effectDistance = new Vector2(4, -4); outline.useGraphicAlpha = false; return outline;
        }
        void Breathe(Outline? edge) { if (edge != null) edge.gameObject.AddComponent<UiPulse>().Bind(edge, () => Calm); }
        void Breathe(Graphic? graphic) { if (graphic != null) graphic.gameObject.AddComponent<UiPulse>().Bind(graphic, () => Calm); }
        // Codex's art from Resources/Art (byte copies of art/, see tools/check_art_copies.py), loaded once. A missing file draws nothing
        // and is logged once; every rule text stays on its named panel.
        // optional: art that may not exist by design (a member without a cut-in, a common card's owner), which is not worth a warning.
        Sprite? Art(string path, bool optional = false)
        {
            if (sprites.TryGetValue(path, out var cached)) return cached;
            var texture = Resources.Load<Texture2D>("Art/" + path);
            if (texture == null && !optional) Debug.LogWarning("[MemoryForge] Missing art Resources/Art/" + path + "; its panel and every text stay.");
            var sprite = texture == null ? null : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprites[path] = sprite; return sprite;
        }
        // Codex's battle UI kit (art/ui/battle-kit/assets, byte copies in Resources/Art/UI/battle): a sprite, 9-sliced when a border
        // (source pixels: left, bottom, right, top) is given; null when the file is missing, so the procedural shape stays in its place.
        Sprite? Kit(string id, Vector4? border = null)
        {
            string key = "UI/battle/" + id + (border.HasValue ? "|sliced" : "");
            if (sprites.TryGetValue(key, out var cached)) return cached;
            var texture = KitTexture(id);
            if (texture == null) Debug.LogWarning("[MemoryForge] Missing UI kit art Resources/Art/UI/battle/" + id + "; the procedural shape stays.");
            var sprite = texture == null ? null : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border ?? Vector4.zero);
            sprites[key] = sprite; return sprite;
        }
        // The kit's textures, loaded as one folder on first use and shared by every client (a later draw never waits on a single load,
        // so a command's redraw stays as quick as before); loaded again only when one was unloaded meanwhile.
        static readonly Dictionary<string, Texture2D> kitTextures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        static bool kitLoaded;
        static Texture2D? KitTexture(string id)
        {
            if (kitTextures.TryGetValue(id, out var known)) { if (known != null) return known; kitLoaded = false; }
            if (!kitLoaded) { kitLoaded = true; kitTextures.Clear(); foreach (var t in Resources.LoadAll<Texture2D>("Art/UI/battle")) kitTextures[t.name] = t; }
            return kitTextures.TryGetValue(id, out var texture) && texture != null ? texture : null;
        }
        // A kit piece stretched sideways only (rail sockets, bar frames): sliced with its caps scaled to the drawn height.
        static void Sideways(Image image, Sprite sprite, float sourceHeight, float height)
        { image.sprite = sprite; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = sourceHeight / Math.Max(1, height); }
        // The kit's intent glyph for a move: an attack by its shown total in seven tiers (StS's steps of 5 up to 30), the rest by type.
        static string IntentIcon(EnemyMoveDef? move, int total)
        {
            switch (move?.Type)
            {
                case "atk": return "ui_intent_attack_0" + (1 + Math.Min(6, Math.Max(0, total) / 5));
                case "atkdebuff": return "ui_intent_attack_debuff";
                case "def": return "ui_intent_defend";
                case "buff": return "ui_intent_strength";
                case "debuff": return "ui_intent_debuff";
                case "charge": return "ui_intent_charge";
                case "summon": return "ui_intent_summon";
                case "heal": return "ui_intent_heal";
                case "insert": return "ui_intent_curse";
                case "reflect": return "ui_intent_reflect";
                case "expose": return "ui_intent_expose";
                case "blockAll": return "ui_intent_defend_all";
                default: return "ui_intent_unknown";
            }
        }
        // Procedural shapes (white, tinted by Image.color), each made once per session; until Codex's UI kit (art/requests/
        // ui-skin-kit-20261018.md) arrives they carry every frame: a rounded panel (corner 14 px) or its ring, a pill (9-sliced, half-
        // height ends; unsliced, a disc) or its ring, a diamond, a soft radial glow and a vertical fade. No imported UI skin is needed.
        static readonly Dictionary<string, Sprite> shapes = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        // Ring widths are kept to half pixels, so every size of panel or pill shares a handful of textures.
        static Sprite Rounded(float ring) { float w = Mathf.Round(ring * 2) / 2; return Shape("r" + w, 48, 48, 14, (x, y) => Corner(x, y, 48, 14, w)); }
        static Sprite Pill(float ring) { float w = Mathf.Round(ring * 2) / 2; return Shape("p" + w, 64, 64, 31, (x, y) => Corner(x, y, 64, 32, w)); }
        static Sprite Diamond => Shape("d", 64, 64, 0, (x, y) => (32 - Math.Abs(x - 32) - Math.Abs(y - 32)) * 0.7f + 0.5f);
        static Sprite Halo => Shape("g", 64, 64, 0, (x, y) => { float d = Mathf.Clamp01(Mathf.Sqrt((x - 32) * (x - 32) + (y - 32) * (y - 32)) / 32); return (1 - d) * (1 - d); });
        // A rounded ring with a diamond on each corner: the framed panels' edge.
        static Sprite Ornate => Shape("o", 64, 64, 22, (x, y) =>
        {
            float ring = Corner(x, y, 64, 14, 1.6f);
            float dx = Math.Min(x, 64 - x) - 7, dy = Math.Min(y, 64 - y) - 7;
            return Math.Max(ring, Mathf.Clamp01((5.5f - Math.Abs(dx) - Math.Abs(dy)) * 0.8f + 0.5f));
        });
        // A gem's face: a disc lit from above (a bright glint high up, darker towards the bottom rim), tinted by its colour.
        static Sprite GemFace => Shape("gf", 64, 64, 0, (x, y) => Corner(x, y, 64, 32, 0), (x, y) =>
        {
            float glint = Mathf.Clamp01(1 - ((x - 32) * (x - 32) / 256f + (y - 47) * (y - 47) / 64f)); // an ellipse high on the face
            return Mathf.Lerp(0.62f + 0.24f * y / 64f, 1f, glint * 0.9f);
        });
        // Opaque at the bottom (a scrim rising from it) or at the top (a sheen or a header shade falling from it).
        static Sprite Fade(bool bottom) => Shape(bottom ? "fb" : "ft", 4, 64, 0, (x, y) => bottom ? 1 - y / 64f : y / 64f);
        // A rounded square's coverage at a pixel centre: 1 inside, 0 outside, anti-aliased over a pixel; a ring keeps only its outer band.
        static float Corner(float px, float py, int size, float radius, float ring)
        {
            float cx = Mathf.Clamp(px, radius, size - radius), cy = Mathf.Clamp(py, radius, size - radius);
            float edgeDistance = radius - Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)); // > 0 inside, measured from the outline
            float alpha = Mathf.Clamp01(edgeDistance + 0.5f);
            return ring > 0 ? alpha * (1 - Mathf.Clamp01(edgeDistance - ring + 0.5f)) : alpha;
        }
        static Sprite Shape(string key, int width, int height, float border, Func<float, float, float> alpha, Func<float, float, float>? light = null)
        {
            if (shapes.TryGetValue(key, out var made) && made != null) return made;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    byte v = light == null ? (byte)255 : (byte)(255 * Mathf.Clamp01(light(x + 0.5f, y + 0.5f)));
                    pixels[y * width + x] = new Color32(v, v, v, (byte)(255 * Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f))));
                }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            made = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            made.hideFlags = HideFlags.DontSave;
            shapes[key] = made; return made;
        }
        static Color Alpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
        static KeyValuePair<string, BattleArgument> Pair(string key, string? value) => new KeyValuePair<string, BattleArgument>(key, value == null ? BattleArgument.Null() : BattleArgument.String(value));
        static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
    }
}
