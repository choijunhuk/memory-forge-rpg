#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MemoryForge.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Codes = MemoryForge.Client.PracticeSessionCodes;

namespace MemoryForge.Client
{
    // P1-PRACTICE-SERVER-UI §14.2/§14.3/§14.7/§17.2/§17.4: a Development-only, optional uGUI view of the local practice SERVER: one scoped
    // map → battle slice over PracticeServerSession. It renders only the server's complete public snapshot and sends only the existing
    // commands (create, enter, play, ult, end, the reward and node-service kinds, proceed) and pure battle queries; it never creates or
    // hydrates a Run/BattleState, seeds anything or computes a cost, slot, target, legality or damage. Costs, modes, blocked[mode] (the
    // DEFAULT request only), forecasts and pick sheets
    // are the server's values; card/character names come from the existing content for display only. It does not replace the original
    // local expedition client. The battle reward, the rest/forge/shop node services, events and the archive have their own screens (one
    // existing command per button); so do the terminal memory save (VICTORY/DEFEAT: local selection, the server's memory-preview, one
    // memory-commit; its assignment and held-part sheets send the existing assign/applyPart) and the DONE screen of the confirmed saved
    // memory (P1-SERVER-MEMORY-SAVE-UI, P1-SERVER-SAVE-PARTS-UI).
    // - Unconfigured it shows a small harmless status. Connect(factory) (runtime, Editor or development build only) creates the session
    //   and only refreshes: nothing is created, sent or retried on Connect or resume. The factory, session, credential, outbox, bodies
    //   and snapshots are never serialized or logged; nothing here chooses an endpoint or credential.
    // - Disable, focus loss and pause drop only the unsent selection, pick sheet, prepared play and presentation, and dispose the session
    //   (an unresolved request stays in the outbox). Enable/focus calls the factory again and refreshes; the exact retry is the explicit
    //   "같은 요청 다시 보내기" button. One UI operation at a time; every async path catches into a fixed Korean notice, on the main thread.
    // - The last good snapshot stays drawn, marked stale, while the session is gone, a refresh failed or a request is unresolved; no
    //   zero/empty placeholder state is invented. New commands need the session's CanMutate.
    // - Distinct things: a local selection / pick sheet / prepared play is unsent; a request in flight or unresolved is the session's;
    //   a presentation shows only a fresh receipt's PresentableEvents (an old receipt's are null and never replayed); next/after
    //   reservations are the server's public state.
    // - Targets: "auto" exists only in Core's pure Check; a play sends the clicked live enemy uid, or no target (Core's own rule for a
    //   single living enemy) — never a client re-target. A query answer is applied only for the selection generation and context it was
    //   asked in. Local prototype timing (not a measurement): rows 0.2 s, a reservation resolution row the original 0.3 s, 0.6 s hold;
    //   the original's display pace (Normal, Double ×0.5, Reduced: every row at once) changes only these waits, never a barrier's Continue.
    // - Services and event choices that need deck cards open a local, unsent deck sheet bound to the context it was opened in; cancel,
    //   any snapshot change (a commit, another device, a new node) and a reconnect drop it, so an old selection is never sent.
    public sealed class MemoryForgeServerPracticeClient : MonoBehaviour
    {
        /// <summary>Why the deck sheet is open: a node service that needs one card (§7.3 node table: rest upgrade, forge variation or
        /// removal, shop removal), or an event choice that copies or removes cards (§7.7, its authored count).</summary>
        public enum DeckPurpose { Upgrade, ForgeOffer, ForgeRemove, ShopRemove, EventCards }

        public const string OwnedCanvas = "MemoryForgeServerPracticeView";
        /// <summary>At most one confirmed play may wait unsent behind an ordinary action presentation (a local UI bound, not a slot).</summary>
        public const int PreparedLimit = 1;
        const float RowSeconds = 0.2f, ReservedRowSeconds = 0.3f, HoldSeconds = 0.6f;
        static readonly Color Background = Hex("0d1322"), Panel = Hex("141c31"), Line = Hex("2f3d63"), Ink = Hex("e9eef8"),
            Muted = Hex("98a5c0"), Brass = Hex("e2b45f"), Memory = Hex("62d6c6"), Danger = Hex("ff6f6f");

        sealed class PickSheet
        {
            public PickSheet(PracticeUiContext context, string uid, string mode, string? target) { Context = context; Uid = uid; Mode = mode; Target = target; }
            public PracticeUiContext Context { get; }
            public string Uid { get; }
            public string Mode { get; }
            public string? Target { get; }
            public List<string> Picks { get; } = new List<string>();
        }
        sealed class Prepared
        {
            public Prepared(PracticeUiContext context, string uid, string mode, string? target) { Context = context; Uid = uid; Mode = mode; Target = target; }
            public PracticeUiContext Context { get; }
            public string Uid { get; }
            public string Mode { get; }
            public string? Target { get; }
        }
        sealed class DeckPick
        {
            public DeckPick(PracticeUiContext context, DeckPurpose purpose, int count, int eventIndex)
            {
                Context = context; Purpose = purpose; Count = count; EventIndex = eventIndex;
            }
            public PracticeUiContext Context { get; }
            public DeckPurpose Purpose { get; }
            /// <summary>How many cards the purpose asks for: 1, or the event choice's authored copy (1) / removal count.</summary>
            public int Count { get; }
            /// <summary>The event choice index for EventCards; -1 otherwise.</summary>
            public int EventIndex { get; }
            /// <summary>The chosen deck keys in the order chosen (Core reads event picks in this order).</summary>
            public List<string> Keys { get; } = new List<string>();
        }

        Func<PracticeServerSession>? factory;
        PracticeServerSession? session;
        PracticeSnapshot? lastGood;
        bool busy, queryWanted, refreshWanted;
        int generation, queryGeneration = -1;
        PracticeBattleQueryResult? queryResult;
        string? selectedCard, selectedMode, target;
        PickSheet? picker;
        Prepared? prepared;
        DeckPick? deckPick;
        IReadOnlyList<BattleDigestEntry>? rows;
        bool barrier;
        int shown;
        float nextAt = -1, closeAt = -1;
        // P1-SERVER-BATTLE-CONTROLS-UI: the shown presentation's title, the display pace of result rows (display only, default Normal,
        // kept for this component only), and the read-only card detail bound to the selection generation it was opened in (-1: closed).
        string presentationTitle = "턴 결과";
        MemoryForgeExpeditionClient.ActionPacing actionPace = MemoryForgeExpeditionClient.ActionPacing.Normal;
        int detailsAt = -1;
        RectTransform? detailsContent;
        float? detailsScrollY;
        int detailsScrollAt = -1;
        string notice = "";
        bool noticeError;
        Text? presentationText;
        Button? continueButton;
        RectTransform? canvasRoot, frame;
        Font? font;
        bool namesLoaded;
        readonly Dictionary<string, string> cardNames = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> characterNames = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
        // Display-only content texts for the reward screen (existing definitions; nothing here decides a reward).
        readonly Dictionary<string, string> cardTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> relicNames = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> relicTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> partNames = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> partTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> partSlots = new Dictionary<string, string>(StringComparer.Ordinal);
        // Display-only event content (names, texts, choice labels, need.coins and the authored copy/remove counts); Core runs the effects.
        readonly Dictionary<string, RunEventDefinition> events = new Dictionary<string, RunEventDefinition>(StringComparer.Ordinal);
        // P1-SERVER-MEMORY-SAVE-UI: the local, unsent save selection bound to the context it was made in; its revision (every selection,
        // assignment, refresh and snapshot change moves it on) and the server preview asked at one revision.
        PracticeUiContext? saveContext;
        readonly List<string> saveSelection = new List<string>();
        int saveRevision, previewRevision = -1;
        bool previewWanted, previewing, assignOpen;
        PracticeMemoryPreviewResult? previewResult;
        // P1-SERVER-SAVE-PARTS-UI (SAVE-081-12): the save screen's held-part sheet, bound to the context it was opened in, and its local,
        // unsent part choice (an inventory id; the server's applyPart is sent only from a target card).
        bool partsOpen;
        string? partPick;
        PracticeUiContext? partsContext;
        // The save list's scroll offset, kept across redraws of the same context only (a selection, preview or busy change redraws all).
        RectTransform? saveContent;
        PracticeUiContext? saveScrollContext;
        float? saveScrollY;

        // ───────── read-only state for Root's checks ─────────
        public bool Configured => factory != null;
        public bool Connected => session != null;
        public bool Busy => busy;
        public PracticeSessionState? SessionState => session?.State;
        /// <summary>The snapshot drawn: the session's current one, else the last good one (then SnapshotStale).</summary>
        public PracticeSnapshot? Snapshot => session?.State.Snapshot ?? lastGood;
        public bool SnapshotStale => session == null || session.State.Snapshot == null || session.State.NeedsRefresh || session.State.Pending != null
            || session.State.OutboxCode != null;
        public string Notice => notice;
        public string? SelectedCard => selectedCard;
        public string? SelectedMode => selectedMode;
        public string? Target => target;
        /// <summary>The battle query answer for the current selection and context; null while none is current.</summary>
        public PracticeBattleQueryView? QueryView => QueryCurrent() ? queryResult!.View : null;
        public bool PickerOpen => picker != null;
        public IReadOnlyList<string> PickerPicks => picker?.Picks.ToArray() ?? Array.Empty<string>();
        /// <summary>Exactly the server's candidates for the open sheet's next pick (empty while its answer is not current).</summary>
        public IReadOnlyList<string> PickerOptions => picker != null && QueryCurrent() ? Candidates(queryResult!.View!) : Array.Empty<string>();
        public bool HasPreparedIntent => prepared != null;
        public string? PreparedCard => prepared?.Uid;
        public string? PreparedMode => prepared?.Mode;
        public string? PreparedTarget => prepared?.Target;
        public bool HasPresentation => rows != null;
        /// <summary>True while an end-turn/terminal result is shown: every selection and command is refused until Continue.</summary>
        public bool PresentationBarrier => rows != null && barrier;
        public IReadOnlyList<BattleDigestEntry> PresentationRows => rows ?? Array.Empty<BattleDigestEntry>();
        public int PresentationShown => rows == null ? 0 : shown;
        /// <summary>The pace of result rows (display only): Normal, Double (every wait ×0.5) or Reduced (every row at once).</summary>
        public MemoryForgeExpeditionClient.ActionPacing ActionPace => actionPace;
        /// <summary>True while the read-only detail of the selected hand card is open (it closes with any selection, query generation,
        /// snapshot change, pick sheet, barrier or leave).</summary>
        public bool CardDetailsOpen => DetailsCurrent();
        public bool DeckPickOpen => deckPick != null;
        public DeckPurpose? DeckPickPurpose => deckPick?.Purpose;
        /// <summary>The first deck key chosen in the open (unsent) deck sheet; null while none is chosen or no sheet is open.</summary>
        public string? DeckPickKey => deckPick?.Keys.FirstOrDefault();
        /// <summary>Every deck key chosen in the open sheet, in the order chosen.</summary>
        public IReadOnlyList<string> DeckPickKeys => deckPick?.Keys.ToArray() ?? Array.Empty<string>();
        /// <summary>How many keys the open sheet needs before it can be sent: its count, at most the pickable cards (the original rule).</summary>
        public int DeckPickRequired => deckPick == null ? 0 : Required(deckPick);
        /// <summary>Whether a new command may be sent now (session CanMutate, nothing local open, no presentation, no operation).</summary>
        public bool CanCommand => !busy && session != null && session.State.CanMutate && rows == null && picker == null && prepared == null && deckPick == null;
        /// <summary>True while the session's current snapshot shows the published terminal save (VICTORY/DEFEAT, pending save, state.memory).</summary>
        public bool ShowingSave => SaveState(session?.State.Snapshot) != null;
        /// <summary>The unsent save selection in the published change order (what a preview or save sends); empty without a current save.</summary>
        public IReadOnlyList<string> SaveSelection => OrderedSelection().ToArray();
        /// <summary>The server's preview of exactly the current selection in exactly the current context; null while none is current.</summary>
        public PracticeMemoryPreviewView? SavePreview => PreviewCurrent() ? previewResult!.View : null;
        /// <summary>The code of the current selection's failed preview (an answer without a view); null otherwise.</summary>
        public string? SavePreviewFailure => PreviewFailure();
        /// <summary>True while a preview of the current selection is due or in flight.</summary>
        public bool SavePreviewPending => ShowingSave && (previewWanted || previewing);
        /// <summary>Whether the save may be sent now: CanCommand, no open sheet and a fresh, error-free server preview of exactly this selection.</summary>
        public bool CanCommitSave => CanCommand && !assignOpen && !partsOpen && ShowingSave && PreviewCurrent() && previewResult!.View!.Error == null;
        public bool AssignOpen => assignOpen;
        /// <summary>True while the save screen's held-part sheet is open (selection, assignment and saving wait until it is closed).</summary>
        public bool SavePartsOpen => partsOpen;

        // ───────── lifecycle ─────────

        /// <summary>Runtime only, Editor or development build only: uses factory for every (re)connection and refreshes once. It sends no
        /// mutation and retries nothing. Root may disable the original local client itself; this component never touches it.</summary>
        public bool Connect(Func<PracticeServerSession> sessionFactory)
        {
            if (sessionFactory == null) throw new ArgumentNullException(nameof(sessionFactory));
            if (!Application.isPlaying || !(Application.isEditor || Debug.isDebugBuild))
            {
                Say("서버 연습은 에디터나 개발 빌드에서만 쓸 수 있어요.", true);
                return false;
            }
            Suspend();
            factory = sessionFactory;
            return OpenAndRefresh();
        }

        void OnEnable()
        {
            if (font == null) font = Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular");
            if (!OpenAndRefresh()) Render();
        }
        void OnDisable() { Suspend(); RemoveView(); }
        void OnApplicationFocus(bool focused) { if (focused) { if (session == null) OpenAndRefresh(); } else LeaveApp(); }
        void OnApplicationPause(bool paused) { if (paused) LeaveApp(); else if (session == null) OpenAndRefresh(); }
        void LeaveApp() { if (session == null) return; Suspend(); Render(); }

        bool OpenAndRefresh()
        {
            if (factory == null || session != null || !isActiveAndEnabled || !Application.isPlaying) return false;
            LoadNames();
            PracticeServerSession? created = null;
            try { created = factory(); }
            catch (Exception) { created = null; }
            if (created == null) { Say("서버 연습 연결을 만들 수 없어요.", true); Render(); return false; }
            session = created;
            refreshWanted = true; // Update refreshes once no earlier operation is still finishing
            Render();
            return true;
        }

        // Drops only unsent local things and disposes the session; the outbox record of an unresolved request stays.
        void Suspend()
        {
            DropUnsent();
            var old = session;
            session = null;
            old?.Dispose();
        }
        void DropUnsent()
        {
            selectedCard = null; selectedMode = null; target = null; picker = null; prepared = null; deckPick = null; rows = null;
            queryResult = null; queryWanted = false; refreshWanted = false; generation++;
            saveContext = null; saveSelection.Clear(); assignOpen = false; previewResult = null; previewWanted = false; saveRevision++;
            saveContent = null; saveScrollContext = null; saveScrollY = null;
            DropParts();
            detailsAt = -1; detailsContent = null; detailsScrollY = null; detailsScrollAt = -1;
        }
        void DropParts() { partsOpen = false; partPick = null; partsContext = null; }

        void Update()
        {
            FitView();
            float now = Time.realtimeSinceStartup;
            if (rows != null) Advance(now);
            else if (prepared != null && !busy) DispatchPrepared();
            if (refreshWanted && !busy && session != null) { refreshWanted = false; Launch(RefreshAsync); }
            if (queryWanted && !busy && session != null && !PresentationBarrier) Launch(RunQueryAsync);
            if (previewWanted && !busy && session != null && !PresentationBarrier) Launch(RunPreviewAsync);
        }

        // ───────── operations (each button calls one of these) ─────────

        public Task<bool> RefreshAsync() => Operate(async s =>
        {
            var result = await s.RefreshAsync();
            if (!ReferenceEquals(s, session)) return false;
            // A refresh never keeps an unsent save selection: the player chooses again from what the server shows now.
            bool dropped = saveSelection.Count > 0, partsDropped = partsOpen;
            saveContext = null; saveSelection.Clear(); DropParts();
            AfterSnapshotChange();
            if (result.Snapshot == null) { Say(Explain(result.Code) + " 마지막으로 받은 화면을 보여 주고 있어요.", true); return false; }
            Say("서버의 최신 상태를 불러왔어요." + (dropped ? " 고른 변화는 저장하지 않았으니 다시 골라 주세요." : "")
                + (partsDropped ? " 부품 창은 닫았어요(붙이지 않은 선택은 보내지 않았어요)." : ""), false);
            return true;
        });

        /// <summary>The explicit exact retry of the outbox's unresolved request (never automatic).</summary>
        public Task<bool> RetryAsync() => Operate(async s =>
        {
            var before = s.State.Snapshot;
            var result = await s.RetryPendingAsync();
            if (!ReferenceEquals(s, session)) return false;
            return Handle(result, before, true, "보관했던 요청을 확인했어요.");
        });

        /// <summary>A new server run; only offered without a current run or after it is DONE (a create refusal stays unresolved).</summary>
        public Task<bool> CreateRunAsync()
        {
            var run = Snapshot?.Run;
            if (run != null && run.Phase != "DONE") return Refuse("진행 중인 탐사가 있어요.");
            if (!CanCommand) return Refuse(NotReady());
            return Mutate((s, context) => s.CreateRunAsync(context, true), false, false, "새 서버 탐사를 시작했어요.");
        }

        public Task<bool> EnterNodeAsync(string nodeId)
        {
            if (nodeId == null) throw new ArgumentNullException(nameof(nodeId));
            if (!CanCommand) return Refuse(NotReady());
            if (!Available().Contains(nodeId)) return Refuse("지금은 그 칸으로 갈 수 없어요.");
            return Mutate((s, context) => s.CommandAsync(context, "enter", new JObject { ["nodeId"] = nodeId }), false, false, "칸에 들어갔어요.");
        }

        public bool SelectCard(string uid)
        {
            if (SelectionLocked() || Hand(uid) == null) return false;
            selectedCard = uid;
            var modes = Hand(uid)!["modes"] as JArray;
            selectedMode = modes != null && modes.Count > 0 ? Str(modes[0]) ?? "now" : "now";
            SelectionChanged();
            return true;
        }

        public bool SelectMode(string mode)
        {
            if (SelectionLocked() || selectedCard == null) return false;
            var modes = Hand(selectedCard)?["modes"] as JArray;
            if (modes == null || !modes.Any(m => Str(m) == mode)) return false;
            selectedMode = mode;
            SelectionChanged();
            return true;
        }

        public bool SelectEnemy(string uid)
        {
            if (SelectionLocked() || !Enemies().Any(e => Str(e["uid"]) == uid && Bool(e["alive"]))) return false;
            target = uid;
            SelectionChanged();
            return true;
        }

        /// <summary>Uses the selected card as the current query answer describes it: opens the server pick sheet when it asks for picks,
        /// prepares it (one, unsent) during an ordinary presentation, or sends exactly one play.</summary>
        public Task<bool> ConfirmAsync()
        {
            if (DetailsCurrent()) return Refuse(DetailsOpenText);
            if (picker != null || PresentationBarrier) return Refuse("지금은 카드를 사용할 수 없어요.");
            if (!Ready()) return Refuse(NotReady());
            if (selectedCard == null || selectedMode == null) return Refuse("사용할 카드를 먼저 골라 주세요.");
            if (!QueryCurrent()) { queryWanted = true; return Refuse("예상을 확인하는 중이에요. 잠시 후 다시 눌러 주세요."); }
            if (TargetMissing()) return Refuse("대상 적을 먼저 눌러 골라 주세요.");
            var view = queryResult!.View!;
            if (NeedsPicks(view))
            {
                if (rows != null) return Refuse("연출이 끝난 뒤에 카드를 고를 수 있어요.");
                picker = new PickSheet(view.Context, selectedCard, selectedMode, SendTarget());
                generation++; queryResult = null; queryWanted = true;
                Say("고를 카드를 순서대로 누르세요.", false);
                Render();
                return Task.FromResult(false);
            }
            if (view.ForecastError != null) return Refuse(ErrorText(view.ForecastError));
            if (rows != null)
            {
                if (prepared != null) return Refuse("이미 사용 대기 중인 카드가 있어요.");
                prepared = new Prepared(view.Context, selectedCard, selectedMode, SendTarget());
                Say("연출이 끝나면 이 카드를 사용해요. 아직 보내지 않았어요.", false);
                Render();
                return Task.FromResult(false);
            }
            return SendPlayAsync(selectedCard, selectedMode, SendTarget(), Array.Empty<string>());
        }

        public bool PickCard(string uid)
        {
            if (picker == null || busy || !QueryCurrent() || !Candidates(queryResult!.View!).Contains(uid)) return false;
            picker.Picks.Add(uid);
            generation++; queryResult = null; queryWanted = true;
            Render();
            return true;
        }

        /// <summary>Sends the sheet's play with its picks once the server's sheet says every request is filled.</summary>
        public Task<bool> ConfirmPicksAsync()
        {
            var sheet = picker;
            if (sheet == null) return Refuse("열린 선택 창이 없어요.");
            // Gate before the sheet is dropped: a refused send must leave it open and usable.
            if (!Ready() || rows != null) return Refuse(NotReady());
            if (!QueryCurrent()) return Refuse("고른 카드를 확인하는 중이에요.");
            var view = queryResult!.View!;
            if (NeedsPicks(view)) return Refuse("아직 고를 카드가 남았어요.");
            if (view.ForecastError != null) return Refuse(ErrorText(view.ForecastError));
            picker = null;
            return SendPlayAsync(sheet.Uid, sheet.Mode, sheet.Target, sheet.Picks.ToArray());
        }

        /// <summary>Sends the sheet's play without picks: Core's own choice (the existing auto semantics).</summary>
        public Task<bool> AutoPickAsync()
        {
            var sheet = picker;
            if (sheet == null) return Refuse("열린 선택 창이 없어요.");
            if (!Ready() || rows != null) return Refuse(NotReady());
            picker = null;
            return SendPlayAsync(sheet.Uid, sheet.Mode, sheet.Target, Array.Empty<string>());
        }

        public void CancelPick()
        {
            if (picker == null) return;
            picker = null;
            SelectionChanged();
        }

        public bool CancelPrepared()
        {
            if (prepared == null) return false;
            prepared = null;
            Say("사용 대기를 취소했어요. 보낸 것은 없어요.", false);
            Render();
            return true;
        }

        public Task<bool> EndTurnAsync()
        {
            if (DetailsCurrent()) return Refuse(DetailsOpenText);
            // A prepared play still waiting (e.g. after skip, before its Update dispatch) is never overtaken by an end.
            if (rows != null || picker != null || prepared != null) return Refuse("지금은 턴을 마칠 수 없어요.");
            if (!CanCommand) return Refuse(NotReady());
            return Mutate((s, context) => s.CommandAsync(context, "end", null), true, true, "턴을 마쳤어요.");
        }

        // ───────── battle controls (P1-SERVER-BATTLE-CONTROLS-UI): ultimate, result pace, card detail ─────────
        // §5.10: an ultimate is the existing ult {actor, target?} command, offered from the published actors' gauge and gaugeMax only. No
        // authored ultimate or resonance asks for picks, so none is ever sent; whether one needs a target is Core's (a single living enemy
        // is its own choice, a missing or dead one its BAD_TARGET refusal before anything changes). There is no ult query or forecast, as in
        // the original. Its fresh events are shown like a play's, titled with the actor and tagged by kind (ActionRowText).

        /// <summary>ult {actor, target?}: a published actor whose published gauge has reached the published gaugeMax, in the live PLAYER
        /// phase, through the existing mutation/outbox. Sends the actor and, only when one is selected, the clicked live enemy. Refused
        /// while busy, unresolved, during a presentation, an open pick sheet or a prepared play; nothing is shown as spent before the server
        /// answers.</summary>
        public Task<bool> UltAsync(string actorId)
        {
            if (actorId == null) throw new ArgumentNullException(nameof(actorId));
            if (DetailsCurrent()) return Refuse(DetailsOpenText);
            if (rows != null || picker != null || prepared != null) return Refuse("지금은 필살기를 쓸 수 없어요.");
            if (!CanCommand) return Refuse(Blocked());
            var b = session!.State.Snapshot?.Run?.StateJObject()["battle"] as JObject;
            if (b == null || Str(b["phase"]) != "PLAYER" || b["result"] is JObject) return Refuse("지금은 필살기를 쓸 수 없어요.");
            var actor = Actors(b).FirstOrDefault(a => Str(a["id"]) == actorId);
            if (actor == null) return Refuse("지금 전투에 없는 동료예요.");
            if (!GaugeFull(actor, b)) return Refuse("필살기 게이지가 아직 차지 않았어요.");
            var args = new JObject { ["actor"] = actorId };
            if (target != null && Enemies().Any(e => Str(e["uid"]) == target && Bool(e["alive"]))) args["target"] = target;
            return Mutate((s, context) => s.CommandAsync(context, "ult", args), true, false, ActorName(actor) + "의 필살기를 사용했어요.");
        }

        /// <summary>Chooses the pace of result rows (the open presentation's remaining rows and later ones). Display only: never
        /// Time.timeScale, a request, the selection, the mode/target/picks or the prepared play; a turn or terminal barrier still waits
        /// for Continue. False when the pace is unknown or already chosen.</summary>
        public bool SetActionPace(MemoryForgeExpeditionClient.ActionPacing pace)
        {
            if (!Enum.IsDefined(typeof(MemoryForgeExpeditionClient.ActionPacing), pace) || pace == actionPace) return false;
            actionPace = pace;
            if (rows != null) Advance(Time.realtimeSinceStartup);
            Render();
            return true;
        }

        /// <summary>Opens the read-only detail of the selected hand card: the current battle query's server name and text, the published
        /// modes, costs, default-request verdicts and parts. Asks and sends nothing; changes no selection, target, mode, pick sheet or
        /// prepared play. Refused without a selected hand card, with a pick sheet open or during a barrier.</summary>
        public bool OpenCardDetails()
        {
            if (selectedCard == null || Hand(selectedCard) == null || picker != null || PresentationBarrier || Snapshot?.Run?.Phase != "BATTLE") return false;
            detailsAt = generation;
            Render();
            return true;
        }

        public void CloseCardDetails()
        {
            if (detailsAt < 0) return;
            detailsAt = -1;
            Render();
        }

        // The open detail is modal for the manual commands (ConfirmAsync, EndTurnAsync, UltAsync refuse first, changing nothing); the
        // already-prepared play keeps its own dispatch (DispatchPrepared/SendPlayAsync are not gated here).
        const string DetailsOpenText = "카드 설명 창을 먼저 닫아 주세요. 보낸 것은 없어요.";
        // The detail belongs to the selection generation it was opened in: a card/mode/enemy selection, a pick sheet, a snapshot change
        // (a commit, refresh or the prepared play's own dispatch) and a leave move the generation on and so close it, as do a barrier and
        // leaving the battle. A query answer for the same generation only fills it in.
        bool DetailsCurrent() => detailsAt >= 0 && detailsAt == generation && selectedCard != null && picker == null && !PresentationBarrier
            && Snapshot?.Run?.Phase == "BATTLE" && Hand(selectedCard) != null;

        // ───────── battle reward (P1-SERVER-REWARD-UI): one existing command per button ─────────

        /// <summary>takeCard {id}: id must be one of the published reward cards.</summary>
        public Task<bool> TakeCardAsync(string id) =>
            RewardAction(p => Ids(p["cards"]).Contains(id), "takeCard", new JObject { ["id"] = id }, "카드를 덱에 넣었어요.");
        /// <summary>skipCard {}: only while reward cards are published.</summary>
        public Task<bool> SkipCardAsync() => RewardAction(p => Ids(p["cards"]).Count > 0, "skipCard", null, "카드를 받지 않았어요.");
        /// <summary>takeRelic {id}: id must be one of the published reward relics.</summary>
        public Task<bool> TakeRelicAsync(string id) =>
            RewardAction(p => Ids(p["relic"]).Contains(id), "takeRelic", new JObject { ["id"] = id }, "유물을 얻었어요.");
        /// <summary>takePart {}: only while a reward part is published.</summary>
        public Task<bool> TakePartAsync() => RewardAction(p => Str(p["part"]) != null, "takePart", null, "부품을 챙겼어요.");
        /// <summary>evolve {key, branchId}: the pair must be one of the published evolution offers.</summary>
        public Task<bool> EvolveAsync(string key, string branchId) => RewardAction(p => ((p["evolutions"] as JArray) ?? new JArray()).OfType<JObject>()
            .Any(e => Str(e["key"]) == key && Ids(e["options"]).Contains(branchId)), "evolve", new JObject { ["key"] = key, ["branchId"] = branchId }, "진화했어요.");
        /// <summary>proceed {}: leaves the battle reward, a published rest/forge/shop node (unclaimed rewards and unused services are
        /// left, as in the original) or an event once its choice is published as made (the original offers leaving only then; every
        /// authored event has a free choice). An explicit command; Core decides whether it is allowed now and where it leads.</summary>
        public Task<bool> ProceedAsync()
        {
            string? phase = session?.State.Snapshot?.Run?.Phase;
            if (phase == "EVENT") return EventAction(p => Truthy(p["done"]), "proceed", null, "지도로 돌아가요.", "먼저 선택지를 골라 주세요.");
            string? service = ServiceType(phase);
            return service != null ? ServiceAction(service, p => true, "proceed", null, "다음으로 나아가요.")
                : RewardAction(p => true, "proceed", null, "다음으로 나아가요.");
        }
        /// <summary>True while the session's current snapshot shows the public pending battle reward.</summary>
        public bool ShowingReward => Pending(session?.State.Snapshot?.Run?.StateJObject(), "reward") != null;

        // Gate first (no request while busy, stale, pending, disconnected, during a barrier/sheet/prepared play), then the published
        // choice from the SESSION's current snapshot, then exactly one existing command through Mutate and the outbox.
        Task<bool> RewardAction(Func<JObject, bool> offered, string kind, JObject? args, string success)
        {
            if (!CanCommand) return Refuse(Blocked());
            var pending = Pending(session!.State.Snapshot?.Run?.StateJObject(), "reward");
            if (pending == null || Str(session.State.Snapshot!.Run!.StateJObject()["phase"]) != "REWARD" || !offered(pending))
                return Refuse("지금 고를 수 있는 보상이 아니에요.");
            return Mutate((s, context) => s.CommandAsync(context, kind, args), false, false, success);
        }

        static JObject? Pending(JObject? state, string type)
        {
            var pending = state?["pending"] as JObject;
            return pending != null && Str(pending["type"]) == type ? pending : null;
        }
        static List<string> Ids(JToken? list) => ((list as JArray) ?? new JArray()).Select(Str).OfType<string>().ToList();

        // ───────── node services (P1-SERVER-SERVICES-UI): one existing command per button ─────────
        // §7.3 node table / §14.3 / §17.4: the server's published pending rest {uses}, forge {uses, offers {key, parts}|null, fromEvent?}
        // and shop {cards|parts|relics [{id, price, sold}], removeCost|null}. Buttons are offered only from those members, mirroring the
        // original local screens; amounts, prices, CANNOT_HEAL, COINS_LOW, BAD_UPGRADE/BAD_CARD and the next phase stay Core's verdict. A
        // used service or sold listing disappears only when the committed state says so — never by local change.

        static readonly string[] ShopKinds = { "cards", "parts", "relics" };

        /// <summary>rest {choice: "heal"}: only while the published rest has uses left.</summary>
        public Task<bool> RestHealAsync() =>
            ServiceAction("rest", p => (Int(p["uses"]) ?? 0) > 0, "rest", new JObject { ["choice"] = "heal" }, "체력을 회복했어요.");
        /// <summary>forgeApply {partId}: the part must be one of the published open offer's parts.</summary>
        public Task<bool> ForgeApplyAsync(string partId) =>
            ServiceAction("forge", p => Ids((p["offers"] as JObject)?["parts"]).Contains(partId), "forgeApply", new JObject { ["partId"] = partId }, "부품을 붙였어요.");
        /// <summary>buy {kind, id}: kind cards|parts|relics and an unsold published listing with that id.</summary>
        public Task<bool> BuyAsync(string kind, string id) => ServiceAction("shop",
            p => ShopKinds.Contains(kind) && Items(p, kind).Any(x => Str(x["id"]) == id && !Bool(x["sold"])), "buy", new JObject { ["kind"] = kind, ["id"] = id }, "구매했어요.");
        /// <summary>"rest", "forge" or "shop" while the session's current snapshot shows that published node service; else null.</summary>
        public string? ShowingService
        {
            get
            {
                var run = session?.State.Snapshot?.Run;
                string? type = ServiceType(run?.Phase);
                return type != null && Pending(run!.StateJObject(), type) != null ? type : null;
            }
        }

        /// <summary>Opens the local deck sheet for a published service that needs one card; nothing is sent until ConfirmDeckPickAsync.
        /// Rest upgrade and forge variation/removal need uses left (forge also no open offer: the original offers no re-roll); shop
        /// removal needs a published removeCost.</summary>
        public bool OpenDeckPick(DeckPurpose purpose) => OpenDeck(purpose, 1, -1);

        bool OpenDeck(DeckPurpose purpose, int count, int eventIndex)
        {
            if (!CanCommand) { Refuse(Blocked()); return false; }
            if (DeckService(purpose, eventIndex) == null) { Refuse("지금은 이 서비스를 이용할 수 없어요."); return false; }
            deckPick = new DeckPick(session!.State.Snapshot!.Context, purpose, count, eventIndex);
            Say(DeckTitle(deckPick) + ". 확정하기 전에는 보내지 않아요.", false);
            Render();
            return true;
        }

        /// <summary>Chooses (or, again, clears) one public deck card the purpose allows; local only. A one-card sheet replaces its choice;
        /// a multi-card sheet adds in order up to its required count and refuses more.</summary>
        public bool PickDeckCard(string key)
        {
            var sheet = deckPick;
            if (sheet == null || busy || !Pickable(DeckCard(key), sheet.Purpose)) return false;
            if (!sheet.Keys.Remove(key))
            {
                if (sheet.Count == 1) sheet.Keys.Clear();
                if (sheet.Keys.Count >= Required(sheet)) return false;
                sheet.Keys.Add(key);
            }
            Render();
            return true;
        }

        /// <summary>Sends the sheet's one existing command with its chosen keys: rest {upgrade, key}, forgeOffer {key}, forgeRemove {key},
        /// buy {remove, key} or event {index, keys}. Gated before the sheet is dropped (a refused send leaves it open); once sent the sheet
        /// is gone, whatever the answer. A sheet whose context, service or event choice is no longer current is closed unsent.</summary>
        public Task<bool> ConfirmDeckPickAsync()
        {
            var sheet = deckPick;
            if (sheet == null) return Refuse("열린 카드 선택 창이 없어요.");
            if (!Ready() || rows != null || picker != null || prepared != null) return Refuse(NotReady());
            if (!sheet.Context.Equals(session!.State.Snapshot!.Context) || DeckService(sheet.Purpose, sheet.EventIndex) == null)
            {
                deckPick = null;
                return Refuse("상태가 바뀌어 카드 선택을 닫았어요. 보낸 것은 없어요.");
            }
            var keys = sheet.Keys.ToList();
            int required = Required(sheet);
            if (required == 0 || keys.Count != required || !keys.All(k => Pickable(DeckCard(k), sheet.Purpose)))
                return Refuse(sheet.Count > 1 ? "카드를 " + required + "장 골라 주세요." : "카드를 먼저 골라 주세요.");
            deckPick = null;
            string key = keys[0];
            var (kind, args, success) = sheet.Purpose == DeckPurpose.Upgrade ? ("rest", new JObject { ["choice"] = "upgrade", ["key"] = key }, "카드를 강화했어요.")
                : sheet.Purpose == DeckPurpose.ForgeOffer ? ("forgeOffer", new JObject { ["key"] = key }, "붙일 부품을 고르세요.")
                : sheet.Purpose == DeckPurpose.ForgeRemove ? ("forgeRemove", new JObject { ["key"] = key }, "카드를 덱에서 뺐어요.")
                : sheet.Purpose == DeckPurpose.ShopRemove ? ("buy", new JObject { ["kind"] = "remove", ["key"] = key }, "카드를 덱에서 뺐어요.")
                : ("event", new JObject { ["index"] = sheet.EventIndex, ["keys"] = new JArray(keys.Cast<object>().ToArray()) }, "선택의 결과가 반영됐어요.");
            return Mutate((s, context) => s.CommandAsync(context, kind, args), false, false, success);
        }

        public void CancelDeckPick()
        {
            if (deckPick == null) return;
            deckPick = null;
            Say("카드 선택을 취소했어요. 보낸 것은 없어요.", false);
            Render();
        }

        // RewardAction's gate for a node service: readiness, then the SESSION's current phase and published pending of that type.
        Task<bool> ServiceAction(string type, Func<JObject, bool> offered, string kind, JObject? args, string success)
        {
            if (!CanCommand) return Refuse(Blocked());
            var pending = CurrentService(type);
            if (pending == null || !offered(pending)) return Refuse("지금은 이 서비스를 이용할 수 없어요.");
            return Mutate((s, context) => s.CommandAsync(context, kind, args), false, false, success);
        }

        JObject? CurrentService(string type)
        {
            var run = session?.State.Snapshot?.Run;
            return run != null && ServiceType(run.Phase) == type ? Pending(run.StateJObject(), type) : null;
        }
        static string? ServiceType(string? phase) => phase == "REST" ? "rest" : phase == "FORGE" ? "forge" : phase == "SHOP" ? "shop" : null;

        // The published service a deck purpose belongs to, while it still offers that purpose (the original screens' enabling rule); for
        // EventCards the published, unresolved event while choice eventIndex still asks for cards.
        JObject? DeckService(DeckPurpose purpose, int eventIndex = -1)
        {
            if (purpose == DeckPurpose.EventCards)
            {
                var choice = OfferedChoice(eventIndex);
                return choice != null && EventPicks(choice) > 0 ? CurrentEvent() : null;
            }
            var p = CurrentService(purpose == DeckPurpose.Upgrade ? "rest" : purpose == DeckPurpose.ShopRemove ? "shop" : "forge");
            if (p == null) return null;
            bool open = purpose == DeckPurpose.ShopRemove ? Int(p["removeCost"]) != null
                : (Int(p["uses"]) ?? 0) > 0 && (purpose == DeckPurpose.Upgrade || !(p["offers"] is JObject));
            return open ? p : null;
        }

        // The original deck picker's rule over the public deck members: never a weapon card; an upgrade never an already-plus card.
        static bool Pickable(JObject? card, DeckPurpose purpose) =>
            card != null && !Truthy(card["weapon"]) && !(purpose == DeckPurpose.Upgrade && Truthy(card["plus"]));
        // The original's required count: what the purpose asks for, at most the pickable cards in the public deck.
        int Required(DeckPick sheet) => Math.Min(sheet.Count, Deck().Count(c => Pickable(c, sheet.Purpose)));
        static List<JObject> Items(JObject pending, string kind) => ((pending[kind] as JArray) ?? new JArray()).OfType<JObject>().ToList();

        // ───────── events and the archive (P1-SERVER-EVENT-ARCHIVE-UI): one existing command per button ─────────
        // §7.5/§7.7 / §17.4: the published pending event {id, done?, choice?} drawn with this device's matching run content (name, text,
        // choice labels/texts, need.coins and the authored copy/remove counts — display and pick counts only), and the published archive
        // {type}. Effects, random draws, COINS_LOW, WOULD_KILL, the heal, the next zone and the save that follows are Core's; a choice
        // shows as made only when the committed state says done. A variation choice is Core's move to a fromEvent forge (ForgeView).

        /// <summary>event {index}: one choice of the published, unresolved event, as this device's matching content lists it. A choice that
        /// copies or removes cards first opens the local deck sheet for its authored count (nothing sent, false); ConfirmDeckPickAsync then
        /// sends event {index, keys}.</summary>
        public Task<bool> ChooseEventAsync(int index)
        {
            if (!CanCommand) return Refuse(Blocked());
            var choice = OfferedChoice(index);
            if (choice == null) return Refuse("지금 고를 수 있는 선택지가 아니에요.");
            int picks = EventPicks(choice);
            if (picks > 0) { OpenDeck(DeckPurpose.EventCards, picks, index); return Task.FromResult(false); }
            return EventAction(p => true, "event", new JObject { ["index"] = index }, "선택의 결과가 반영됐어요.", "지금 고를 수 있는 선택지가 아니에요.");
        }

        /// <summary>archive {choice: "continue"|"return"}: only while the published archive is open (§7.5).</summary>
        public Task<bool> ArchiveAsync(string choice)
        {
            if (!CanCommand) return Refuse(Blocked());
            var run = session!.State.Snapshot?.Run;
            if (run == null || run.Phase != "ARCHIVE" || Pending(run.StateJObject(), "archive") == null) return Refuse("지금은 기록소가 열려 있지 않아요.");
            if (choice != "continue" && choice != "return") return Refuse("보낼 수 없는 선택이에요.");
            return Mutate((s, context) => s.CommandAsync(context, "archive", new JObject { ["choice"] = choice }), false, false,
                choice == "return" ? "기록소에서 귀환했어요." : "다음 구역으로 나아가요.");
        }

        // RewardAction's gate for the published event: readiness, then the SESSION's current EVENT phase and pending, then one command.
        Task<bool> EventAction(Func<JObject, bool> offered, string kind, JObject? args, string success, string refused)
        {
            if (!CanCommand) return Refuse(Blocked());
            var pending = CurrentEvent();
            if (pending == null || !offered(pending)) return Refuse(refused);
            return Mutate((s, context) => s.CommandAsync(context, kind, args), false, false, success);
        }

        JObject? CurrentEvent()
        {
            var run = session?.State.Snapshot?.Run;
            return run != null && run.Phase == "EVENT" ? Pending(run.StateJObject(), "event") : null;
        }
        // A choice of the published event that is not yet resolved, by index into the matching content definition (Core's own bounds).
        RunEventChoice? OfferedChoice(int index)
        {
            var p = CurrentEvent();
            if (p == null || Truthy(p["done"])) return null;
            var definition = EventDefinition(Str(p["id"]));
            return definition != null && index >= 0 && index < definition.Choices.Count ? definition.Choices[index] : null;
        }
        RunEventDefinition? EventDefinition(string? id) => id != null && events.TryGetValue(id, out var definition) ? definition : null;
        // The original's pick count: a copy takes one card, a removal its authored count (Core falls back for missing picks).
        static int EventPicks(RunEventChoice choice) =>
            choice.Effects.CardCopy.GetValueOrDefault() != 0 ? 1 : Math.Max(0, choice.Effects.CardRemove.GetValueOrDefault());

        // ───────── terminal memory save (P1-SERVER-MEMORY-SAVE-UI): local selection, the server's preview, one memory-commit ─────────
        // §8.9–8.12 / §14.7 / §17.4: the published pending {type: save, defeat} with state.memory {cid: {capacity, usedBefore, before, after,
        // changes [{id, cid, key, kind, label, value?}]}}. A selection is local intent bound to the context it was made in. Every change of
        // it, an assignment, a refresh and any snapshot change drops the earlier preview (an answer still in flight included) and asks the
        // pure memory-preview of exactly the new selection. The resulting deck, used/capacities and the preview error — DEFEAT_KEEP_ONE (one
        // NEW change across the party on a defeat) and OVER_CAPACITY (the whole resulting deck) — are the server's; nothing here diffs decks,
        // adds costs or judges a rule. Saving is the existing memory-commit of exactly the previewed ids, enabled only by a fresh error-free
        // preview in the current context; an uncertain save is the header's exact retry, and only the confirmed account shows it saved.

        /// <summary>Adds or removes one published change id of the current save (local, unsent) and asks the server's preview of the new
        /// selection. Allowed while an earlier preview is in flight (its answer is then dropped); refused during a send, an unresolved or
        /// unreadable request, a due refresh, a presentation or the assignment sheet.</summary>
        public bool ToggleChange(string changeId)
        {
            if (changeId == null) throw new ArgumentNullException(nameof(changeId));
            var snap = session?.State.Snapshot;
            var save = SaveState(snap);
            if (save == null || !SaveSelectable() || !ChangeOrder(save).Contains(changeId)) return false;
            BindSave(snap!.Context);
            if (!saveSelection.Remove(changeId)) saveSelection.Add(changeId);
            InvalidatePreview(true);
            Say("선택을 바꿨어요. 서버 미리보기를 조회하며, 저장을 확정하기 전에는 기억이 바뀌지 않아요.", false);
            Render();
            return true;
        }

        /// <summary>Empties the local selection (keep the saved deck as it is) and asks the server's preview of no change.</summary>
        public bool ClearChanges()
        {
            var snap = session?.State.Snapshot;
            if (SaveState(snap) == null || !SaveSelectable() || OrderedSelection().Count == 0) return false;
            saveSelection.Clear();
            InvalidatePreview(true);
            Say("선택을 비웠어요. 이대로 저장하면 기존 기억 덱을 그대로 유지해요.", false);
            Render();
            return true;
        }

        /// <summary>Asks the server's pure preview of exactly the current selection now (it is also asked by itself after every change).</summary>
        public Task<bool> PreviewSaveAsync()
        {
            if (SaveState(session?.State.Snapshot) == null) return Refuse("지금은 기억 저장 단계가 아니에요.");
            if (busy || !SaveReadable()) return Refuse(NotReady());
            return RunPreviewAsync();
        }

        /// <summary>memory-commit {selectedChangeIds}: exactly the ids of the fresh, error-free server preview of the current selection in the
        /// current context (published order), sent once. The selection is gone once sent, whatever the answer. An uncertain answer stays
        /// pending for the exact retry; nothing is shown as saved before the server's confirmed account and run say so.</summary>
        public Task<bool> CommitSaveAsync()
        {
            if (assignOpen) return Refuse("배정 창을 먼저 닫아 주세요.");
            if (partsOpen) return Refuse("부품 창을 먼저 닫아 주세요.");
            if (!CanCommand) return Refuse(Blocked());
            if (SaveState(session!.State.Snapshot) == null) return Refuse("지금은 기억 저장 단계가 아니에요.");
            if (!PreviewCurrent())
            {
                if (!previewing && PreviewFailure() == null) previewWanted = true;
                return Refuse("서버 미리보기를 아직 받지 못했어요. 미리보기를 받은 뒤에 저장할 수 있어요.");
            }
            var view = previewResult!.View!;
            if (view.Error != null) return Refuse("저장할 수 없어요: " + SaveReason(view));
            var context = view.Context;
            var ids = view.SelectedChangeIds.ToArray();
            saveContext = null; saveSelection.Clear(); InvalidatePreview(false);
            return Mutate((s, current) => s.CommitMemoryAsync(context, ids), false, false,
                ids.Length == 0 ? "기존 기억 덱을 그대로 유지하고 탐사를 마쳤어요." : "고른 변화 " + ids.Length + "개를 기억에 저장했어요.");
        }

        /// <summary>Opens the save screen's local assignment sheet: every gained common card of the public deck, one button per party member.</summary>
        public bool OpenAssign()
        {
            var save = SaveState(session?.State.Snapshot);
            if (save == null || !SaveReadable() || busy || partsOpen || Assignable(save).Count == 0) return false;
            assignOpen = true;
            Render();
            return true;
        }

        public void CloseAssign()
        {
            if (!assignOpen) return;
            assignOpen = false;
            Render();
        }

        /// <summary>assign {key, charId} (the existing command): a gained common card of the public deck to a party member. The selection and
        /// preview are dropped before it is sent (the change list after it is the server's); BAD_ASSIGN and
        /// REASSIGNMENT_REQUIRES_SEPARATE_FLOW stay Core's verdict. Nothing is ever assigned or selected by itself.</summary>
        public Task<bool> AssignAsync(string key, string charId)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (charId == null) throw new ArgumentNullException(nameof(charId));
            if (partsOpen) return Refuse("부품 창을 먼저 닫아 주세요.");
            if (!CanCommand) return Refuse(Blocked());
            var save = SaveState(session!.State.Snapshot);
            if (save == null) return Refuse("지금은 기억 저장 단계가 아니에요.");
            if (!Assignable(save).Any(c => Str(c["key"]) == key) || !PartyIds(save).Contains(charId)) return Refuse("지금 배정할 수 있는 카드나 동료가 아니에요.");
            saveContext = null; saveSelection.Clear(); InvalidatePreview(false);
            return Mutate((s, context) => s.CommandAsync(context, "assign", new JObject { ["key"] = key, ["charId"] = charId }), false, false,
                "배정을 바꿨어요. 고른 변화와 미리보기는 초기화했어요. 서버가 다시 만든 변화 목록에서 골라 주세요.");
        }

        // ───────── held parts on the save screen (P1-SERVER-SAVE-PARTS-UI, SAVE-081-12) ─────────
        // The original's found-part flow: choose a held part (state.partsFound, one entry per copy), then a published non-weapon deck card;
        // that sends the existing applyPart {partId, key} at once. Core consumes ONE matching copy, sets the card's circuit (c) or engraving
        // (e) slot (an earlier part there is replaced, never refunded) and recaptures the save's change list. Nothing is decremented,
        // attached or saved here: the inventory, the mods, the rebuilt changes and the whole resulting memory are the server's, and keeping
        // the result in memory is still the player's change selection and the one memory-commit. Legality (NO_PART, BAD_CARD,
        // INVALID_PART) is Core's verdict; a part this device cannot name or that has no circuit/engraving slot is shown, never offered.

        /// <summary>Opens the save screen's held-part sheet (local, sends nothing) in the current context; refused without a readable
        /// current save, while busy or unresolved, while the assignment sheet is open or when no part is held.</summary>
        public bool OpenSaveParts()
        {
            var snap = session?.State.Snapshot;
            var save = SaveState(snap);
            if (save == null || !SaveReadable() || busy || assignOpen || rows != null || picker != null || prepared != null || deckPick != null
                || HeldParts(save).Count == 0) return false;
            partsOpen = true; partPick = null; partsContext = snap!.Context;
            Render();
            return true;
        }

        /// <summary>Closes the held-part sheet and drops its unsent part choice; sends nothing.</summary>
        public void CloseSaveParts()
        {
            if (!partsOpen) return;
            DropParts();
            Render();
        }

        /// <summary>applyPart {partId, key} (the existing command): one held copy of partId onto a published non-weapon deck card. The
        /// selection, preview and part sheet are dropped before it is sent (the change list after it is the server's); an uncertain answer
        /// stays in the outbox for the exact retry. Refused while busy or unresolved (a second click cannot consume another copy).</summary>
        public Task<bool> ApplySavePartAsync(string partId, string key)
        {
            if (partId == null) throw new ArgumentNullException(nameof(partId));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (assignOpen) return Refuse("배정 창을 먼저 닫아 주세요.");
            if (!CanCommand) return Refuse(Blocked());
            var save = SaveState(session!.State.Snapshot);
            if (save == null) return Refuse("지금은 기억 저장 단계가 아니에요.");
            if (!HeldParts(save).Contains(partId)) return Refuse("지금 가지고 있는 부품이 아니에요.");
            if (!Attachable(partId)) return Refuse("이 기기에서 확인할 수 없는 부품이라 붙일 수 없어요. 아무것도 보내지 않았어요.");
            if (!PartTargets(save).Any(c => Str(c["key"]) == key)) return Refuse("부품을 붙일 수 있는 카드가 아니에요. 무기 카드에는 붙일 수 없어요.");
            DropParts();
            saveContext = null; saveSelection.Clear(); InvalidatePreview(false);
            return Mutate((s, context) => s.CommandAsync(context, "applyPart", new JObject { ["partId"] = partId, ["key"] = key }), false, false,
                "「" + PartName(partId) + "」을 붙였어요. 부품 1개를 사용했고, 같은 칸에 있던 부품은 교체됐어요. 고른 변화와 미리보기는 초기화했어요. "
                + "기억에 남기려면 서버가 다시 만든 변화 목록에서 골라 저장해 주세요.");
        }

        // The sheet's local part choice (unsent): a held, attachable inventory id; choosing it again clears it.
        bool PickSavePart(string partId)
        {
            var save = SaveState(session?.State.Snapshot);
            if (!partsOpen || save == null || !HeldParts(save).Contains(partId) || !Attachable(partId)) return false;
            partPick = partPick == partId ? null : partId;
            Render();
            return true;
        }

        async Task<bool> RunPreviewAsync()
        {
            previewWanted = false;
            var snap = session?.State.Snapshot;
            if (snap == null || SaveState(snap) == null || !SaveReadable()) return false;
            BindSave(snap.Context);
            var ids = OrderedSelection();
            int asking = saveRevision;
            var context = snap.Context;
            previewing = true;
            try
            {
                return await Operate(async s =>
                {
                    var result = await s.PreviewMemoryAsync(context, ids);
                    // A stale answer (another selection, context or session) is dropped: only the current one may enable saving.
                    if (!ReferenceEquals(s, session) || asking != saveRevision || !s.IsCurrent(result)) return false;
                    previewResult = result; previewRevision = asking;
                    if (result.View == null) { Say("서버 미리보기를 받지 못했어요. " + Explain(result.Code), true); return false; }
                    return true;
                });
            }
            finally
            {
                previewing = false;
                Render();
            }
        }

        // The answer of the current revision, still bound to the session's current context, for exactly the current selection.
        bool PreviewCurrent()
        {
            var view = previewResult?.View;
            if (view == null || previewRevision != saveRevision || session == null || !session.IsCurrent(previewResult!)) return false;
            return view.Context.Equals(saveContext) && view.SelectedChangeIds.SequenceEqual(OrderedSelection());
        }
        string? PreviewFailure() => previewResult != null && previewResult.View == null && previewRevision == saveRevision && session != null
            && session.IsCurrent(previewResult) ? previewResult.Code : null;

        // The selection belongs to the context it was made in; another context starts empty.
        void BindSave(PracticeUiContext context)
        {
            if (saveContext != null && saveContext.Equals(context)) return;
            saveContext = context; saveSelection.Clear(); InvalidatePreview(false);
        }
        // Every selection, assignment, refresh and snapshot change makes the earlier preview unusable, an answer still in flight included.
        void InvalidatePreview(bool ask)
        {
            saveRevision++; previewResult = null;
            previewWanted = ask && ShowingSave;
        }
        // The unsent selection in the published change order: exactly what a preview or save sends.
        List<string> OrderedSelection()
        {
            var snap = session?.State.Snapshot;
            var save = SaveState(snap);
            if (save == null || saveContext == null || !saveContext.Equals(snap!.Context)) return new List<string>();
            return ChangeOrder(save).Where(saveSelection.Contains).ToList();
        }
        // A preview may be asked with a current, refreshed snapshot and nothing unresolved or unreadable (an earlier preview may be running).
        bool SaveReadable() => session != null && session.State.Snapshot != null && session.State.Pending == null && session.State.OutboxCode == null
            && !session.State.NeedsRefresh;
        bool SaveSelectable() => SaveReadable() && (!busy || previewing) && rows == null && picker == null && prepared == null && deckPick == null && !assignOpen
            && !partsOpen;

        // The published terminal save of a snapshot: VICTORY/DEFEAT with pending {type: save} and the public state.memory; else null.
        static JObject? SaveState(PracticeSnapshot? snap)
        {
            var run = snap?.Run;
            if (run == null || run.Phase != "VICTORY" && run.Phase != "DEFEAT") return null;
            var state = run.StateJObject();
            return Pending(state, "save") != null && state["memory"] is JObject ? state : null;
        }
        static List<string> PartyIds(JObject state) =>
            ((state["party"] as JArray) ?? new JArray()).OfType<JObject>().Select(p => Str(p["charId"])).OfType<string>().Distinct().ToList();
        // The save's characters: the party order, then any other published member of state.memory (shown, never dropped).
        static List<string> SaveCharacters(JObject state)
        {
            var memory = state["memory"] as JObject;
            var order = PartyIds(state).Where(cid => memory?[cid] is JObject).ToList();
            if (memory != null) foreach (var member in memory.Properties()) if (member.Value is JObject && !order.Contains(member.Name)) order.Add(member.Name);
            return order;
        }
        static List<JObject> SaveChanges(JObject state, string cid) =>
            ((((state["memory"] as JObject)?[cid] as JObject)?["changes"] as JArray) ?? new JArray()).OfType<JObject>().ToList();
        // Every published change id in the published order (the original's change-list order: party, then each character's list).
        static List<string> ChangeOrder(JObject state)
        {
            var ids = new List<string>();
            foreach (string cid in SaveCharacters(state))
                foreach (var change in SaveChanges(state, cid))
                    if (Str(change["id"]) is string id && !ids.Contains(id)) ids.Add(id);
            return ids;
        }
        // The original assignment sheet's cards: gained common cards of the public deck (Core judges whether one may move).
        static List<JObject> Assignable(JObject state) =>
            ((state["deck"] as JArray) ?? new JArray()).OfType<JObject>().Where(c => Str(c["owner"]) == "common" && Truthy(c["gained"])).ToList();
        // The public held parts, one entry per copy in the published order (two equal ids are two copies).
        static List<string> HeldParts(JObject state) => ((state["partsFound"] as JArray) ?? new JArray()).Select(Str).OfType<string>().ToList();
        // A part this device names with a circuit or engraving slot (what a v2 save attaches); any other is shown as unavailable.
        bool Attachable(string partId) => partNames.ContainsKey(partId) && partSlots.TryGetValue(partId, out var slot) && (slot == "circuit" || slot == "engraving");
        // Every published non-weapon deck card with a key, in deck order (Core judges whether the part may go on it).
        static List<JObject> PartTargets(JObject state) =>
            ((state["deck"] as JArray) ?? new JArray()).OfType<JObject>().Where(c => !Truthy(c["weapon"]) && Str(c["key"]) != null).ToList();
        // The ids of an unresolved memory-commit of this run, read from the stored request: shown as unconfirmed, never as saved.
        List<string>? PendingSaveIds(string runId)
        {
            var pending = session?.State.Pending;
            if (pending == null || pending.Route != PracticeCommandOutbox.MemoryCommitRoute(runId)) return null;
            try { return ((JObject.Parse(pending.Body)["selectedChangeIds"] as JArray) ?? new JArray()).Select(Str).OfType<string>().ToList(); }
            catch (Exception) { return new List<string>(); }
        }

        /// <summary>Barrier: shows every row at once. Ordinary presentation: closes it. Changes text only; sends nothing.</summary>
        public void SkipPresentation()
        {
            if (rows == null) return;
            if (barrier) { shown = rows.Count; UpdatePresentation(); }
            else ClosePresentation();
        }

        /// <summary>Closes a fully shown barrier presentation; sends nothing.</summary>
        public void ContinuePresentation()
        {
            if (rows != null && barrier && shown >= rows.Count) ClosePresentation();
        }

        // ───────── internals ─────────

        async Task<bool> Operate(Func<PracticeServerSession, Task<bool>> body)
        {
            var s = session;
            if (s == null) { Say("서버 연습에 연결되어 있지 않아요.", true); Render(); return false; }
            if (busy) { Say("이전 요청을 처리하는 중이에요.", true); return false; }
            busy = true;
            Render();
            try { return await body(s); }
            catch (Exception) { Say("문제가 생겼어요. 새로고침한 뒤 다시 시도해 주세요.", true); return false; }
            finally
            {
                busy = false;
                Render();
            }
        }

        Task<bool> Mutate(Func<PracticeServerSession, PracticeUiContext, Task<PracticeMutationResult>> send, bool present, bool endTurn, string success) =>
            Operate(async s =>
            {
                var before = s.State.Snapshot;
                if (before == null) { Say("먼저 서버 상태를 새로고침해 주세요.", true); return false; }
                var result = await send(s, before.Context);
                if (!ReferenceEquals(s, session)) return false;
                return Handle(result, present ? before : null, endTurn, success);
            });

        Task<bool> SendPlayAsync(string uid, string mode, string? playTarget, IReadOnlyList<string> picks)
        {
            var args = new JObject { ["uid"] = uid, ["mode"] = mode };
            if (playTarget != null) args["target"] = playTarget;
            if (picks.Count > 0) args["picks"] = new JArray(picks.Cast<object>().ToArray());
            return Mutate((s, context) => s.CommandAsync(context, "play", args), true, false, "카드를 사용했어요.");
        }

        // A resolution: the session already re-read the state; presentation only from a fresh receipt's events.
        bool Handle(PracticeMutationResult result, PracticeSnapshot? before, bool endTurn, string success)
        {
            AfterSnapshotChange();
            switch (result.Status)
            {
                case PracticeMutationStatus.Committed:
                    var events = result.PresentableEvents();
                    if (before != null && events != null && result.Snapshot != null) StartPresentation(before, events, result.Snapshot, endTurn);
                    if (result.RefreshCode != null) Say("행동은 서버에 반영됐지만 최신 상태를 다시 불러와야 해요. 새로고침해 주세요.", true);
                    else if (result.OutboxCode != null) Say("행동은 반영됐지만 요청 기록을 정리하지 못했어요. '같은 요청 다시 보내기'로 확인해 주세요.", true);
                    else Say(success, false);
                    return true;
                case PracticeMutationStatus.NotCommitted:
                    Say(ErrorText(result.Code) + " (반영되지 않았어요)", true);
                    return false;
                case PracticeMutationStatus.Pending:
                    Say(Explain(result.Code) + " 보낸 요청은 그대로 보관했어요. '같은 요청 다시 보내기'로 확인해 주세요.", true);
                    return false;
                default:
                    Say(Explain(result.Code), true);
                    return false;
            }
        }

        // After any server read: keep the last good snapshot, drop local things that no longer match it, and re-ask the query.
        void AfterSnapshotChange()
        {
            var snap = session?.State.Snapshot;
            if (snap != null) lastGood = snap;
            if (picker != null && (snap == null || !picker.Context.Equals(snap.Context))) picker = null;
            if (deckPick != null && (snap == null || !deckPick.Context.Equals(snap.Context))) deckPick = null;
            if (selectedCard != null && Hand(selectedCard) == null) { selectedCard = null; selectedMode = null; }
            if (target != null && !Enemies().Any(e => Str(e["uid"]) == target && Bool(e["alive"]))) target = null;
            generation++; queryResult = null;
            queryWanted = selectedCard != null || picker != null;
            // The save selection survives only in its own context with every id still published; the preview is always asked again.
            var save = SaveState(snap);
            if (save == null || saveContext == null || !saveContext.Equals(snap!.Context)) { saveContext = null; saveSelection.Clear(); }
            else { var published = ChangeOrder(save); saveSelection.RemoveAll(id => !published.Contains(id)); }
            if (save == null) assignOpen = false;
            // The part sheet belongs to the context it was opened in; any other drops it with its unsent choice.
            if (save == null || partsContext == null || !partsContext.Equals(snap!.Context)) DropParts();
            else if (partPick != null && !HeldParts(save).Contains(partPick)) partPick = null;
            InvalidatePreview(save != null);
        }

        void SelectionChanged()
        {
            generation++; queryResult = null; queryWanted = selectedCard != null;
            Render();
        }

        async Task<bool> RunQueryAsync()
        {
            queryWanted = false;
            var snap = session?.State.Snapshot;
            if (snap?.Run?.BattleVersion == null) return false;
            string? uid = picker?.Uid ?? selectedCard;
            string? mode = picker?.Mode ?? selectedMode;
            if (uid == null || mode == null) return false;
            string? asked = picker != null ? picker.Target : SendTarget();
            var picks = picker?.Picks.ToArray() ?? Array.Empty<string>();
            int asking = generation;
            var context = snap.Context;
            return await Operate(async s =>
            {
                var result = await s.BattleQueryAsync(context, uid, mode, asked, picks);
                // A stale answer (another selection, context or session) never replaces the current one.
                if (!ReferenceEquals(s, session) || asking != generation || !s.IsCurrent(result)) return false;
                queryResult = result; queryGeneration = asking;
                if (result.View == null) { Say(Explain(result.Code), true); return false; }
                return true;
            });
        }

        bool QueryCurrent()
        {
            var view = queryResult?.View;
            if (view == null || queryGeneration != generation || session == null || !session.IsCurrent(queryResult!)) return false;
            string? uid = picker?.Uid ?? selectedCard, mode = picker?.Mode ?? selectedMode, asked = picker != null ? picker.Target : SendTarget();
            var picks = picker?.Picks ?? new List<string>();
            return view.Uid == uid && view.Mode == mode && view.Target == asked && view.Picks.SequenceEqual(picks);
        }

        void DispatchPrepared()
        {
            var waiting = prepared!;
            prepared = null;
            var snap = session?.State.Snapshot;
            bool fresh = snap != null && waiting.Context.Equals(snap.Context) && CanCommand && QueryCurrent() && picker == null
                && selectedCard == waiting.Uid && selectedMode == waiting.Mode && SendTarget() == waiting.Target && queryResult!.View!.ForecastError == null
                && !NeedsPicks(queryResult.View!);
            if (!fresh) { Say("상태가 바뀌어 사용 대기한 카드를 보내지 않았어요.", true); Render(); return; }
            Launch(() => SendPlayAsync(waiting.Uid, waiting.Mode, waiting.Target, Array.Empty<string>()));
        }

        // Authoritative readiness (§14.7): no operation of this view in flight and the session's own CanMutate (a current snapshot, nothing
        // unresolved, the outbox readable, no refresh due, no exchange in flight). Selection, confirmation and every new command need it,
        // checked BEFORE any sheet is opened or dropped and before Operate (which itself marks busy); refresh and the explicit retry do not.
        // An approved ordinary presentation leaves it true, so the next selection and one prepared play stay possible.
        bool Ready() => !busy && session != null && session.State.CanMutate;
        string NotReady() => busy ? "이전 요청을 처리하는 중이에요."
            : session == null ? "서버 연습에 연결되어 있지 않아요."
            : "서버 확인이 끝나지 않았어요. 새로고침하거나 '같은 요청 다시 보내기'로 확인해 주세요.";
        string Blocked() => rows != null || picker != null || prepared != null || deckPick != null ? "지금은 할 수 없어요." : NotReady();
        bool SelectionLocked() => !Ready() || PresentationBarrier || picker != null || prepared != null || session!.State.Snapshot?.Run?.BattleVersion == null;

        // ───────── presentation (cosmetic public event rows) ─────────

        void StartPresentation(PracticeSnapshot before, JArray events, PracticeSnapshot after, bool endTurn)
        {
            var built = BattleTurnDigest.Build(Project(before), events.OfType<JObject>().Select(ToEvent), Project(after));
            var afterBattle = Battle(after);
            bool terminal = afterBattle == null || afterBattle["result"] is JObject;
            barrier = endTurn || terminal;
            var list = barrier ? built.ToList() : built.Where(r => !Decoration(r)).ToList();
            if (list.Count == 0) return;
            rows = list.AsReadOnly(); shown = 0; nextAt = Time.realtimeSinceStartup; closeAt = -1;
            presentationTitle = UltTitle(before, events) ?? (endTurn ? "턴 결과"
                : list.Any(r => r.Kind == BattleDigestKind.ReservationPlayed) ? "예약 결과" : "카드 사용 결과");
        }

        // The fresh receipt's own ult event names whose ultimate this is (its actor, as the battle before the command published it).
        string? UltTitle(PracticeSnapshot before, JArray events)
        {
            var ult = events.OfType<JObject>().FirstOrDefault(e => Str(e["e"]) == "ult");
            if (ult == null) return null;
            string? id = Str(ult["actor"]);
            var b = Battle(before);
            var actor = b == null ? null : Actors(b).FirstOrDefault(a => Str(a["id"]) == id);
            return (actor != null ? ActorName(actor) : CharacterName(id)) + " 필살기 결과";
        }

        // Display pace only: Double halves every wait, Reduced shows every row at once (a barrier still waits for Continue).
        float PaceScale => actionPace == MemoryForgeExpeditionClient.ActionPacing.Double ? 0.5f : 1f;
        void Advance(float now)
        {
            if (rows == null) return;
            if (actionPace == MemoryForgeExpeditionClient.ActionPacing.Reduced && shown < rows.Count)
            {
                shown = rows.Count;
                UpdatePresentation();
                return;
            }
            if (shown < rows.Count)
            {
                if (now < nextAt) return;
                shown++;
                nextAt = now + (shown < rows.Count && rows[shown].IsReservedResolution ? ReservedRowSeconds : RowSeconds) * PaceScale;
                UpdatePresentation();
                return;
            }
            if (barrier) return;
            if (closeAt < 0) closeAt = now + HoldSeconds * PaceScale;
            else if (now >= closeAt) ClosePresentation();
        }

        void UpdatePresentation()
        {
            if (rows == null) return;
            // Each row with its kind tag ([사용]·[예약]·[해결]·[메아리]·[반격]·[필살기]·[유물]), exactly as the original labels them.
            if (presentationText != null)
                presentationText.text = string.Join("\n", rows.Take(shown).Select(MemoryForgeExpeditionClient.ActionRowText).Reverse().Take(barrier ? 14 : 3).Reverse());
            if (continueButton != null) continueButton.interactable = shown >= rows.Count;
        }

        void ClosePresentation()
        {
            rows = null; closeAt = -1;
            Render();
        }

        static bool Decoration(BattleDigestEntry row) =>
            row.Kind == BattleDigestKind.Summary || row.Kind == BattleDigestKind.PreviousIntent || row.Kind == BattleDigestKind.EnemyState;

        // Public values only, for the digest text; no BattleState is built.
        BattleDigestSnapshot Project(PracticeSnapshot snap)
        {
            var state = snap.Run?.StateJObject();
            var b = state?["battle"] as JObject;
            if (b == null) return new BattleDigestSnapshot { Phase = Str(state?["phase"]) };
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var card in (b["hand"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                if (Str(card["uid"]) is string uid) names[uid] = CardName(Str(card["id"]));
            var reservations = (b["reservations"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            foreach (var r in reservations) if (Str(r["uid"]) is string uid) names[uid] = CardName(Str(r["id"]));
            return new BattleDigestSnapshot
            {
                Turn = Int(b["turn"]), Hp = Int(b["hp"]), Block = Int(b["block"]), Ap = Int(b["ap"]),
                Phase = Str((b["result"] as JObject)?["phase"]) ?? Str(b["phase"]),
                Enemies = ((b["enemies"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()).Select(e => new BattleDigestEnemy
                {
                    Uid = Str(e["uid"]), Name = Str(e["name"]), Hp = Int(e["hp"]), Block = Int(e["block"]), Alive = Bool(e["alive"]),
                    IntentText = Bool(e["alive"]) ? MoveText(e["intent"]) : null
                }).ToList(),
                Reservations = reservations.Select(r => new BattleDigestReservation { Uid = Str(r["uid"]), Name = CardName(Str(r["id"])), Kind = Str(r["kind"]), Due = Int(r["due"]) }).ToList(),
                CardNames = names
            };
        }

        // The public event row as Core's plain event DTO (the same member names PublicView projects); nothing else is read.
        static BattleEvent ToEvent(JObject e) => new BattleEvent
        {
            E = Str(e["e"]) ?? "", T = Int(e["t"]), N = Int(e["n"]), Growth = Int(e["growth"]), Turn = Int(e["turn"]), Ap = Int(e["ap"]),
            Damage = Int(e["d"]), Blocked = Int(e["blocked"]), Cost = Int(e["cost"]), Due = Int(e["due"]), Until = Int(e["until"]),
            From = Int(e["from"]), To = Int(e["to"]), Index = Int(e["index"]), Hp = Int(e["hp"]), Brk = Int(e["brk"]),
            Uid = Str(e["uid"]), Source = Str(e["src"]), Status = Str(e["s"]), Part = Str(e["part"]), Phase = Str(e["phase"]), Id = Str(e["id"]),
            Card = Str(e["card"]), Mode = Str(e["mode"]), Target = Str(e["target"]), Kind = Str(e["kind"]), Reserved = Str(e["reserved"]),
            Actor = Str(e["actor"]), Fizzled = BoolOrNull(e["fizzled"]), Fallback = BoolOrNull(e["fallback"]), Discarded = BoolOrNull(e["discarded"])
        };

        // ───────── public-state readers (the drawn snapshot only) ─────────

        JObject? State() => Snapshot?.Run?.StateJObject();
        static JObject? Battle(PracticeSnapshot snap) => snap.Run?.StateJObject()["battle"] as JObject;
        JObject? Battle() => State()?["battle"] as JObject;
        JObject? Hand(string uid) => (Battle()?["hand"] as JArray)?.OfType<JObject>().FirstOrDefault(c => Str(c["uid"]) == uid);
        List<JObject> Enemies() => (Battle()?["enemies"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        static List<JObject> Actors(JObject battle) => ((battle["actors"] as JArray) ?? new JArray()).OfType<JObject>().ToList();
        // The published gauge against the published gaugeMax (both required; nothing is assumed when either is missing).
        static bool GaugeFull(JObject actor, JObject battle) => Int(actor["gauge"]) is int gauge && Int(battle["gaugeMax"]) is int max && gauge >= max;
        string ActorName(JObject actor) => Str(actor["name"]) ?? CharacterName(Str(actor["id"]));
        HashSet<string> Available() => new HashSet<string>(((State()?["available"] as JArray) ?? new JArray()).Select(Str).OfType<string>(), StringComparer.Ordinal);
        List<JObject> Deck() => ((State()?["deck"] as JArray) ?? new JArray()).OfType<JObject>().ToList();
        JObject? DeckCard(string? key) => key == null ? null : Deck().FirstOrDefault(c => Str(c["key"]) == key);
        static List<string> Candidates(PracticeBattleQueryView view) =>
            ((view.ToJObject()["choices"]?["candidates"] as JArray) ?? new JArray()).OfType<JObject>().Select(c => Str(c["uid"])).OfType<string>().ToList();
        static bool NeedsPicks(PracticeBattleQueryView view)
        {
            var choices = view.ToJObject()["choices"];
            int requests = (choices?["requests"] as JArray)?.Count ?? 0;
            return requests > 0 && (Int(choices?["index"]) ?? requests) < requests;
        }
        string? NeedsTarget() => selectedCard == null ? null : Str(Hand(selectedCard)?["needsTarget"]);
        // The target a play/query sends: the clicked live enemy, else none (Core's own rule takes a single living enemy).
        string? SendTarget() => NeedsTarget() != null && target != null ? target : null;
        bool TargetMissing() => NeedsTarget() != null && target == null && Enemies().Count(e => Bool(e["alive"])) > 1;

        // ───────── view ─────────

        void Render()
        {
            if (!isActiveAndEnabled || font == null || !Application.isPlaying) return;
            // The offset the player scrolled the save list to (SaveView restores it only for the same context).
            saveScrollY = saveContent != null ? saveContent.anchoredPosition.y : (float?)null;
            detailsScrollY = detailsContent != null ? detailsContent.anchoredPosition.y : (float?)null;
            RemoveView();
            var canvasObject = new GameObject(OwnedCanvas, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
            // The scaler's OnEnable already applied its default constant scale 1 and it re-applies only before the next canvas render, so
            // re-enable it now: text measured while building (Flow's wrapped heights) must use the scale it is drawn at.
            scaler.enabled = false; scaler.enabled = true;
            canvasRoot = canvasObject.GetComponent<RectTransform>();
            if (factory == null)
            {
                // Unconfigured: a small, non-blocking status only.
                frame = Box(canvasRoot, "ServerPracticeFrame", 0, 0, 1600, 900, Color.clear);
                Center(frame);
                var status = Box(frame, "ServerPracticeIdle", 1180, 840, 400, 44, Panel);
                Label(status, "서버 연습 · 연결 안 됨 (개발용)", 12, 8, 376, 30, 18, Muted);
            }
            else
            {
                frame = Box(canvasRoot, "ServerPracticeFrame", 0, 0, 1600, 900, Background);
                Center(frame);
                Header();
                var snap = Snapshot;
                var run = snap?.Run;
                if (snap == null) Label(frame, busy ? "서버 상태를 불러오는 중이에요…" : "아직 서버 상태를 받지 못했어요. 새로고침을 눌러 주세요.", 32, 200, 1536, 60, 26, Muted);
                else if (run == null) Label(frame, "진행 중인 서버 탐사가 없어요. '새 서버 탐사'로 시작할 수 있어요.", 32, 200, 1536, 60, 26, Ink);
                else if (run.Phase == "MAP") MapView();
                else if (run.Phase == "BATTLE" && Battle() != null) BattleView();
                else if (run.Phase == "REWARD" && Pending(State(), "reward") != null) RewardView();
                else if (run.Phase == "REST" && Pending(State(), "rest") != null) RestView();
                else if (run.Phase == "FORGE" && Pending(State(), "forge") != null) ForgeView();
                else if (run.Phase == "SHOP" && Pending(State(), "shop") != null) ShopView();
                else if (run.Phase == "EVENT" && Pending(State(), "event") != null) EventView();
                else if (run.Phase == "ARCHIVE" && Pending(State(), "archive") != null) ArchiveView();
                else if (SaveState(snap) != null) SaveView();
                else if (run.Phase == "DONE") DoneView();
                else LaterView(run.Phase);
                Label(frame, notice, 32, 846, 1536, 44, 21, noticeError ? Danger : Memory);
                if (picker != null) PickView();
                if (deckPick != null) DeckPickView();
                if (assignOpen && SaveState(snap) != null) AssignView();
                if (partsOpen && SaveState(snap) != null) SavePartsView();
                if (DetailsCurrent()) CardDetailsView();
                if (rows != null && barrier) BarrierView();
            }
            if (factory != null && EventSystem.current == null)
            { var input = new GameObject("ServerPracticeInput", typeof(EventSystem), typeof(StandaloneInputModule)); input.transform.SetParent(canvasObject.transform, false); }
            Canvas.ForceUpdateCanvases(); FitView();
        }

        void Header()
        {
            Label(frame!, "기억 공방", 32, 16, 230, 48, 36, Brass, FontStyle.Bold);
            Label(frame!, "서버 연습 · 개발용 · 연습 계정", 272, 12, 600, 30, 22, Ink);
            Label(frame!, StatusText(), 272, 44, 800, 30, 18, SnapshotStale ? Danger : Muted);
            var refresh = Button(frame!, "Refresh", "새로고침", 1090, 18, 150, 52, () => Launch(RefreshAsync), Line, 22);
            refresh.interactable = session != null && !busy;
            if (session?.State.Pending != null)
            {
                var retry = Button(frame!, "Retry", "같은 요청 다시 보내기", 1252, 18, 316, 52, () => Launch(RetryAsync), Brass, 22);
                retry.interactable = !busy;
            }
            else
            {
                var run = Snapshot?.Run;
                if (Snapshot != null && (run == null || run.Phase == "DONE"))
                {
                    var create = Button(frame!, "CreateRun", "새 서버 탐사", 1252, 18, 316, 52, () => Launch(CreateRunAsync), Brass, 22);
                    create.interactable = CanCommand;
                }
            }
        }

        string StatusText()
        {
            if (session == null) return "연결이 멈췄어요 · 다시 돌아오면 새로고침해요 · 마지막 화면은 최신이 아닐 수 있어요";
            var s = session.State;
            if (s.OutboxCode != null) return "이 기기의 요청 기록을 확인할 수 없어요 · 새 행동은 막혀 있어요 · 기록은 지우지 않았어요";
            if (busy || s.InFlight) return "서버와 통신하는 중이에요…";
            if (s.Pending != null) return "확인되지 않은 요청이 1건 있어요 · '같은 요청 다시 보내기'로 확인해 주세요";
            if (s.NeedsRefresh) return "화면이 최신이 아닐 수 있어요 · 새로고침해 주세요";
            if (s.Snapshot == null) return "서버 상태를 아직 받지 못했어요";
            return "서버 상태와 일치해요";
        }

        void MapView()
        {
            var state = State()!;
            var map = state["map"] as JObject;
            var panel = Box(frame!, "Map", 32, 100, 1536, 730, Panel);
            Label(panel, "체력 " + Int(state["hp"]) + " / " + Int(state["maxHp"]) + "   ·   동전 " + Int(state["coins"]), 20, 10, 900, 34, 24, Ink, FontStyle.Bold);
            if (map == null) { Label(panel, "지도 정보가 없어요.", 20, 60, 800, 40, 24, Muted); return; }
            var columns = ((map["floors"] as JArray) ?? new JArray()).OfType<JArray>().Select(row => row.OfType<JObject>().ToList()).ToList();
            if (map["boss"] is JObject boss) columns.Add(new List<JObject> { boss });
            var open = Available();
            string? here = Str(state["position"]);
            float column = 1536f / Math.Max(1, columns.Count), top = 100, height = 600, nodeWidth = Math.Min(160, column - 24), nodeHeight = 66;
            for (int f = 0; f < columns.Count; f++)
            {
                Label(panel, f == columns.Count - 1 && map["boss"] is JObject ? "보스" : (f + 1) + "층", f * column, 56, column, 32, 21, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
                for (int i = 0; i < columns[f].Count; i++)
                {
                    var node = columns[f][i];
                    string id = Str(node["id"]) ?? "";
                    bool current = id == here, reachable = open.Contains(id);
                    float y = top + (i + 0.5f) * height / columns[f].Count - nodeHeight / 2;
                    var button = Button(panel, "Node:" + id, NodeName(Str(node["type"]) ?? "") + (current ? "\n현재 위치" : reachable ? "\n갈 수 있음" : ""),
                        f * column + (column - nodeWidth) / 2, y, nodeWidth, nodeHeight, () => Launch(() => EnterNodeAsync(id)), current ? Memory : reachable ? Brass : Background, 21);
                    button.interactable = reachable && CanCommand;
                }
            }
        }

        void LaterView(string phase)
        {
            var state = State()!;
            var panel = Box(frame!, "Later", 32, 120, 1536, 300, Panel);
            Label(panel, PhaseName(phase), 24, 20, 1488, 44, 32, Brass, FontStyle.Bold);
            Label(panel, "체력 " + Int(state["hp"]) + " / " + Int(state["maxHp"]) + "   ·   동전 " + Int(state["coins"]), 24, 80, 1488, 36, 24, Ink);
            // A terminal phase without the published change list cannot be saved from here: nothing is offered or sent.
            Label(panel, phase == "VICTORY" || phase == "DEFEAT" ? "서버가 기억 저장에 필요한 변화 목록을 보내지 않았어요. 새로고침해 주세요. 아무것도 보내지 않았어요."
                : "이 단계는 아직 서버 화면이 없어요. 원래 로컬 탐사 화면은 그대로 있어요.", 24, 140, 1488, 80, 22, Muted);
        }

        // P1-SERVER-EVENT-ARCHIVE-UI §7.7: the published event by this device's matching content. Before the choice: every authored choice
        // (label, need.coins, text) in a scroll; a choice is enabled, as in the original, when the shown coins cover its need (COINS_LOW
        // and WOULD_KILL themselves are Core's). After it: the choice the server records and leaving to the map.
        void EventView()
        {
            var state = State()!;
            var p = Pending(state, "event")!;
            var definition = EventDefinition(Str(p["id"]));
            int coins = Int(state["coins"]) ?? 0;
            bool ready = CanCommand;
            var panel = Box(frame!, "Event", 32, 100, 1536, 736, Panel);
            Label(panel, "체력 " + Int(state["hp"]) + " / " + Int(state["maxHp"]) + "   ·   동전 " + Int(state["coins"]) + "  (서버 기록)", 900, 22, 616, 32, 20, Ink);
            if (definition == null)
            {
                // The server's event is not in this device's content: no choice can be offered or explained, so none is sent; a choice
                // the server already records as made can still be left.
                Label(panel, "알 수 없는 사건이에요. 이 기기의 콘텐츠가 서버와 맞지 않아요.", 20, 14, 860, 44, 26, Danger);
                if (Truthy(p["done"])) Button(panel, "Proceed", "지도로", 1196, 640, 320, 72, () => Launch(ProceedAsync), Brass, 24).interactable = ready;
                return;
            }
            Label(panel, definition.Name, 20, 14, 860, 44, 32, Brass, FontStyle.Bold);
            Label(panel, definition.Text, 20, 66, 1496, 96, 22, Ink);
            if (Truthy(p["done"]))
            {
                int chosen = Int(p["choice"]) ?? -1;
                var choice = chosen >= 0 && chosen < definition.Choices.Count ? definition.Choices[chosen] : null;
                Label(panel, choice != null ? "선택: " + choice.Label + " — " + choice.Text : "선택을 마쳤어요.", 20, 180, 1496, 90, 22, Memory);
                Button(panel, "Proceed", "지도로", 1196, 640, 320, 72, () => Launch(ProceedAsync), Brass, 24).interactable = ready;
                return;
            }
            var list = Scroll(panel, "EventChoices", 20, 176, 1496, 540, 1496, definition.Choices.Count * 100, Panel);
            for (int i = 0; i < definition.Choices.Count; i++)
            {
                int index = i;
                var choice = definition.Choices[i];
                int need = choice.RequiredCoins.GetValueOrDefault();
                Button(list, "EventChoice:" + i, choice.Label + (need > 0 ? " (동전 " + need + " 필요)" : "") + "\n" + choice.Text, 0, i * 100, 1496, 88,
                    () => Launch(() => ChooseEventAsync(index)), Line, 21).interactable = ready && coins >= need;
            }
        }

        // §7.5: the published archive. Continue or return is the existing archive command; the heal, the next zone map and the save that
        // follows are Core's (the save itself is SaveView's).
        void ArchiveView()
        {
            var state = State()!;
            bool ready = CanCommand;
            int zone = (Int(state["zoneIndex"]) ?? 0) + 1, zones = (state["zones"] as JArray)?.Count ?? 0;
            var panel = Box(frame!, "Archive", 32, 100, 1536, 400, Panel);
            Label(panel, "기록소", 20, 14, 400, 44, 32, Brass, FontStyle.Bold);
            Label(panel, "체력 " + Int(state["hp"]) + " / " + Int(state["maxHp"]) + "   ·   동전 " + Int(state["coins"]) + "  (서버 기록)", 900, 22, 616, 32, 20, Ink);
            Label(panel, zone + "구역" + (zones > 0 ? " / " + zones : "") + "의 기록을 확정했다", 20, 66, 1496, 40, 26, Ink, FontStyle.Bold);
            Label(panel, "여기서 돌아가면 이번 탐사의 변화를 골라 기억에 더할 수 있어요. 계속 가면 최대 체력의 15%를 회복하고 다음 구역으로 갑니다. "
                + "그 뒤 패배하면 출발 전 기억 덱은 그대로 유지되고, 이번 탐사의 변화는 파티 전체에서 1개까지만 더해 저장해요.",
                20, 116, 1496, 90, 21, Muted);
            Button(panel, "ArchiveContinue", "계속 탐사", 20, 220, 440, 72, () => Launch(() => ArchiveAsync("continue")), Brass, 24).interactable = ready;
            Button(panel, "ArchiveReturn", "귀환하고 저장", 480, 220, 440, 72, () => Launch(() => ArchiveAsync("return")), Line, 24).interactable = ready;
            Label(panel, "귀환하면 기억 저장 화면에서 이번 탐사의 변화를 골라 저장해요.", 20, 316, 1496, 60, 20, Muted);
        }

        // P1-SERVER-MEMORY-SAVE-UI §8.12: the published terminal save. Per character, top: the saved deck before this run ("그대로 유지");
        // then this run's changes exactly as the server lists them (one button each: a local selection, only previewed, never committed); then the server's
        // preview of exactly the current selection — its resulting deck and used/capacity. Everything sits in one vertical scroll whose
        // height comes from the published counts, so no row or card is hidden. The footer states why saving is closed (preview due, failed
        // or refused, an unresolved request) and offers the one save, clearing the selection (keep the saved deck) and an explicit re-ask.
        void SaveView()
        {
            var state = State()!;
            var snap = Snapshot!;
            bool defeat = Truthy(Pending(state, "save")!["defeat"]);
            var pendingIds = PendingSaveIds(snap.Run!.RunId);
            var selected = OrderedSelection();
            var view = SavePreview;
            var preview = view?.ToJObject()["preview"] as JObject;
            string? failure = PreviewFailure();
            bool asking = previewWanted || previewing, selectable = SaveSelectable() && pendingIds == null;

            var head = Box(frame!, "SaveHead", 32, 100, 1536, 132, Panel);
            int held = HeldParts(state).Count;
            Label(head, defeat ? "패배 · 출발 전 기억은 그대로 지켜져요" : "탐사 종료 · 이번 탐사의 변화를 골라 기억에 더하세요", 20, 8, held > 0 ? 1020 : 1260, 42, 28, defeat ? Danger : Brass, FontStyle.Bold);
            if (held > 0)
                Button(head, "OpenSaveParts", "보유 부품 " + held + "개 붙이기", 1060, 8, 224, 44, () => OpenSaveParts(), Line, 20).interactable =
                    SaveReadable() && !busy && pendingIds == null && !assignOpen;
            Label(head, defeat
                ? "패배해도 출발 전 기억 덱은 그대로예요. 이번 탐사에서 생긴 변화는 파티 전체에서 1개까지 더할 수 있어요. 카드 한 장 단위가 아니라 강화·진화·부품·추가·제거 같은 변화 하나예요. 아무것도 고르지 않으면 기존 덱을 유지해요."
                : "출발 전 기억 덱에 이번 탐사의 변화를 여러 개 더할 수 있어요. 용량은 고른 변화의 합이 아니라 완성된 결과 덱 전체로 서버가 계산해요. 저장한 덱은 다음 탐사의 시작 덱이 돼요.",
                20, 52, 1496, 50, 18, Ink);
            Label(head, "고른 변화 " + selected.Count + "개" + (defeat ? " (패배: 파티 전체 1개까지)" : "") + "  ·  고르기는 미리보기 조회만 해요 · 기억은 저장 후에 바뀌어요  ·  저장된 기억 버전 "
                + snap.Context.MemoryVersion + " (서버)", 20, 104, 1260, 26, 17, Muted);
            if (Assignable(state).Count > 0)
                Button(head, "OpenAssign", "공용 카드 배정", 1300, 8, 216, 44, () => OpenAssign(), Line, 20).interactable = SaveReadable() && !busy && pendingIds == null;

            var memory = (JObject)state["memory"]!;
            var sections = SaveCharacters(state).Select(cid =>
            {
                var entry = (JObject)memory[cid]!;
                var before = ((entry["before"] as JArray) ?? new JArray()).OfType<JObject>().ToList();
                var result = ((preview?["memory"] as JObject)?[cid] as JArray)?.OfType<JObject>().ToList();
                return (cid, entry, before, changes: SaveChanges(state, cid), result);
            }).ToList();
            var list = Scroll(frame!, "SaveList", 32, 242, 1536, 470, 1536, sections.Sum(s => SectionHeight(s.before.Count, s.changes.Count, s.result?.Count)), Background);
            // A redraw in the same context keeps where the player scrolled (clamped to the new content); another context starts at the top.
            if (saveScrollY != null && snap.Context.Equals(saveScrollContext))
                list.anchoredPosition = new Vector2(list.anchoredPosition.x,
                    Mathf.Clamp(saveScrollY.Value, 0, Math.Max(0, list.rect.height - ((RectTransform)list.parent).rect.height)));
            saveContent = list; saveScrollContext = snap.Context;
            float y = 0;
            foreach (var (cid, entry, before, changes, result) in sections)
            {
                float height = SectionHeight(before.Count, changes.Count, result?.Count);
                var section = Box(list, "Memory:" + cid, 0, y, 1536, height - 16, Panel);
                string resultText = asking ? "결과 용량: 서버 미리보기 확인 중…" : failure != null ? "결과 용량: 미리보기를 받지 못했어요" : "결과 용량: 미리보기 없음";
                Color resultColor = failure != null ? Danger : Muted;
                if (preview != null)
                {
                    int? used = Int((preview["used"] as JObject)?[cid]), capacity = Int((preview["capacities"] as JObject)?[cid]);
                    // Only points at the column the server's own OVER_CAPACITY refusal is about; the verdict is the preview's error.
                    bool over = view!.Error == "OVER_CAPACITY" && used > capacity;
                    resultText = "결과 용량 " + used + "/" + capacity + " (서버 미리보기)" + (over ? " · 초과" : "");
                    resultColor = over ? Danger : Memory;
                }
                Label(section, CharacterName(cid), 20, 10, 400, 40, 28, Ink, FontStyle.Bold);
                Label(section, "출발 전 용량 " + Int(entry["usedBefore"]) + "/" + Int(entry["capacity"]), 440, 14, 360, 34, 20, Brass);
                Label(section, resultText, 820, 14, 696, 34, 20, resultColor);
                float sy = 58;
                Label(section, "출발 전에 있던 기억 " + before.Count + "장 — 그대로 유지", 20, sy, 1496, 30, 20, Memory, FontStyle.Bold);
                sy = Chips(section, before.Select(SpecText).ToList(), sy + 34, "저장된 카드가 없어요.");
                Label(section, "이번 탐사에서 생긴 변화 " + changes.Count + "개" + (changes.Count > 0 ? "  ·  눌러서 고르거나 해제해요 (아직 저장 안 함)" : ""), 20, sy, 1496, 30, 20, Memory, FontStyle.Bold);
                sy += 34;
                if (changes.Count == 0) { Label(section, "바뀐 점이 없어요. 출발 전 기억이 그대로 유지돼요.", 20, sy, 1496, 34, 18, Muted); sy += 40; }
                foreach (var change in changes)
                {
                    string id = Str(change["id"]) ?? "";
                    bool on = pendingIds != null ? pendingIds.Contains(id) : selected.Contains(id);
                    string line = pendingIds != null ? (on ? "확인 중인 저장 요청에 들어 있어요 · 아직 저장된 것이 아니에요" : "확인 중인 저장 요청에 없어요")
                        : on ? "고름 · 서버 미리보기 결과에 반영돼요" : "고르지 않음 · 이번에는 남기지 않아요";
                    Button(section, "Change:" + id, (on ? "[선택] " : "") + KindName(Str(change["kind"])) + " · " + (Str(change["label"]) ?? "변화") + "\n" + line,
                        20, sy, 1496, 62, () => ToggleChange(id), on ? Memory : Line, 19).interactable = selectable;
                    sy += 70;
                }
                Label(section, result == null ? "서버 미리보기 결과 덱" : "서버 미리보기 결과 덱 " + result.Count + "장", 20, sy, 1496, 30, 20, Memory, FontStyle.Bold);
                if (result != null) Chips(section, result.Select(SpecText).ToList(), sy + 34, "결과 덱이 비어 있어요.");
                else Label(section, asking ? "서버 미리보기를 확인하는 중이에요…" : "서버 미리보기를 받으면 결과 덱을 보여 줘요.", 20, sy + 34, 1496, 34, 18, Muted);
                y += height;
            }

            string status;
            Color tone = Danger;
            if (pendingIds != null)
                status = "저장 요청(변화 " + pendingIds.Count + "개)을 서버에서 확인하지 못했어요. 위의 '같은 요청 다시 보내기'로 같은 요청을 확인해 주세요. 확인되기 전에는 저장된 것으로 보지 않아요.";
            else if (!SaveReadable())
                status = session == null ? "연결이 멈췄어요. 다시 돌아오면 서버 상태를 새로 불러와요." : "서버 확인이 끝나지 않아 고르거나 저장할 수 없어요. 새로고침해 주세요.";
            else if (asking) { status = "서버 미리보기를 확인하는 중이에요… 미리보기를 받기 전에는 저장할 수 없어요."; tone = Muted; }
            else if (failure != null) status = "서버 미리보기를 받지 못했어요: " + Explain(failure) + " '미리보기 다시 확인'을 눌러 주세요. 저장은 막혀 있어요.";
            else if (view == null) { status = "서버 미리보기가 아직 없어요. '미리보기 다시 확인'을 눌러 주세요."; tone = Muted; }
            else if (view.Error != null) status = "저장할 수 없는 이유: " + SaveReason(view) + " 선택을 바꾸거나 '선택 비우기'로 기존 덱을 유지할 수 있어요.";
            else
            {
                status = selected.Count == 0 ? "서버 미리보기: 기존 기억 덱 그대로예요. 저장하면 이대로 탐사를 마쳐요."
                    : "서버 미리보기: 고른 변화 " + selected.Count + "개를 더한 결과 덱이 위에 보여요. 저장하면 탐사를 마쳐요.";
                tone = Memory;
            }
            var foot = Box(frame!, "SaveFoot", 32, 722, 1536, 116, Panel);
            Label(foot, status, 20, 8, 936, 100, 19, tone);
            bool ready = CanCommitSave;
            string caption = pendingIds != null ? "저장 요청 확인 중" : selected.Count != 0 ? "고른 변화 " + selected.Count + "개 저장하고 끝내기" : "변화 없이 저장 (기존 덱 유지)";
            Button(foot, "CommitSave", caption, 976, 10, 344, 96, () => Launch(CommitSaveAsync), ready ? Brass : Line, 22).interactable = ready;
            Button(foot, "ClearChanges", "선택 비우기", 1336, 10, 180, 44, () => ClearChanges(), Line, 19).interactable = selectable && selected.Count > 0;
            Button(foot, "PreviewSave", "미리보기 다시 확인", 1336, 62, 180, 44, () => Launch(PreviewSaveAsync), Line, 18).interactable = SaveReadable() && !busy;
        }

        // One character section of SaveView: header, the saved deck, the change rows and the preview deck (each deck in chip rows).
        static float SectionHeight(int before, int changes, int? result) =>
            58 + 34 + ChipsHeight(before) + 34 + (changes == 0 ? 40 : changes * 70) + 34 + (result == null ? 40 : ChipsHeight(result.Value)) + 10 + 16;
        static float ChipsHeight(int count) => count == 0 ? 40 : (count + 3) / 4 * 46;
        // Deck rows as fixed chips, four per line, so a long deck never cuts a card name off; returns the y below them.
        float Chips(Transform parent, IReadOnlyList<string> names, float y, string empty)
        {
            if (names.Count == 0) { Label(parent, empty, 20, y, 1496, 34, 18, Muted); return y + ChipsHeight(0); }
            for (int i = 0; i < names.Count; i++)
            {
                var chip = Box(parent, "Chip", 20 + i % 4 * 374, y + i / 4 * 46, 366, 40, Background);
                Label(chip, names[i], 10, 4, 346, 32, 18, Ink);
            }
            return y + ChipsHeight(names.Count);
        }

        // The save screen's assignment sheet (the original's): every gained common card with one button per party member; each sends the
        // existing assign command at once. Which assignment Core allows is its verdict (a card saved before this run is refused).
        void AssignView()
        {
            var state = State()!;
            var cards = Assignable(state);
            var party = PartyIds(state);
            var blocker = Box(frame!, "AssignBlocker", 0, 0, 1600, 900, new Color(0, 0, 0, 0.6f));
            blocker.GetComponent<Image>().raycastTarget = true;
            var p = Box(frame!, "AssignSheet", 160, 100, 1280, 700, Panel);
            Label(p, "공용 카드는 누구의 기억에 넣을까요?", 24, 16, 1232, 44, 28, Ink, FontStyle.Bold);
            Label(p, "누르면 바로 서버에 배정을 보내요. 배정을 바꾸면 고른 변화와 미리보기가 초기화되고, 서버가 다시 만든 변화 목록에서 다시 골라야 해요. "
                + "출발 전 기억에 있던 카드의 배정은 여기서 바꿀 수 없어요.", 24, 62, 1232, 56, 18, Muted);
            float width = Math.Min(260, (1232f - 420) / Math.Max(1, party.Count));
            var list = Scroll(p, "AssignList", 24, 128, 1232, 460, 1232, cards.Count * 70, Background);
            bool ready = CanCommand;
            for (int i = 0; i < cards.Count; i++)
            {
                string key = Str(cards[i]["key"]) ?? "", assigned = Str(cards[i]["assigned"]) ?? "";
                Label(list, CardName(Str(cards[i]["id"])) + (Truthy(cards[i]["plus"]) ? "+" : ""), 12, i * 70 + 14, 396, 44, 22, Ink);
                for (int k = 0; k < party.Count; k++)
                {
                    string cid = party[k];
                    Button(list, "Assign:" + key + ":" + cid, CharacterName(cid) + (assigned == cid ? " (지금)" : ""), 420 + k * width, i * 70 + 6, width - 8, 58,
                        () => Launch(() => AssignAsync(key, cid)), assigned == cid ? Memory : Line, 20).interactable = ready;
                }
            }
            Button(p, "CloseAssign", "닫기", 1000, 612, 256, 64, CloseAssign, Line, 22);
        }

        // The save screen's held-part sheet (the original's found-part flow): every distinct held part id with its copy count and slot, an
        // unknown or slotless one shown as unavailable; after a part is chosen, every published non-weapon deck card, each sending the
        // existing applyPart at once. The counts and cards are the published state; nothing changes here before the server answers.
        void SavePartsView()
        {
            var state = State()!;
            var parts = HeldParts(state).GroupBy(id => id, StringComparer.Ordinal).Select(g => (id: g.Key, count: g.Count())).ToList();
            var blocker = Box(frame!, "SavePartsBlocker", 0, 0, 1600, 900, new Color(0, 0, 0, 0.6f));
            blocker.GetComponent<Image>().raycastTarget = true;
            var p = Box(frame!, "SavePartsSheet", 160, 100, 1280, 700, Panel);
            Label(p, "보유 부품을 카드에 붙일까요?", 24, 16, 1232, 44, 28, Ink, FontStyle.Bold);
            Label(p, "부품을 고른 뒤 카드를 누르면 바로 서버에 보내요. 부품 1개를 사용하고, 그 카드의 같은 칸(시간 회로·보조 각인)에 있던 부품은 교체되며 돌려받지 않아요. "
                + "붙이면 고른 변화와 미리보기가 초기화돼요. 영구 기억에 남기려면 서버가 다시 만든 변화 목록에서 그 변화를 골라 저장해야 해요. 무기 카드에는 붙일 수 없어요.",
                24, 62, 1232, 78, 17, Muted);
            Label(p, "보유 부품 " + parts.Sum(x => x.count) + "개 · 같은 부품은 개수로 보여 줘요", 24, 148, 1232, 28, 20, Memory, FontStyle.Bold);
            var list = Scroll(p, "SavePartList", 24, 180, 1232, 124, 1232, (parts.Count + 2) / 3 * 62, Background);
            bool readable = SaveReadable();
            for (int i = 0; i < parts.Count; i++)
            {
                var (id, count) = parts[i];
                bool ok = Attachable(id), on = id == partPick;
                string caption = !partNames.ContainsKey(id) ? "알 수 없는 부품 (" + id + ") ×" + count + "\n이 기기에서 확인할 수 없어 붙일 수 없어요"
                    : !ok ? PartName(id) + " ×" + count + "\n이 저장에서는 붙일 수 없는 부품이에요"
                    : (on ? "[선택] " : "") + PartName(id) + " · " + SlotName(id) + " ×" + count;
                Button(list, "SavePart:" + id, caption, i % 3 * 412, i / 3 * 62, 400, 56, () => PickSavePart(id), on ? Memory : Line, 18).interactable = ok && readable;
            }
            string? pick = partPick != null && parts.Any(x => x.id == partPick) ? partPick : null;
            Label(p, pick == null ? "붙일 카드 · 먼저 부품을 고르세요" : "「" + PartName(pick) + "」(" + SlotName(pick) + ")을 붙일 카드 · 무기 카드는 빠져 있어요",
                24, 314, 1232, 28, 20, Memory, FontStyle.Bold);
            bool ready = CanCommand && !assignOpen;
            if (pick != null)
            {
                string chosen = pick;
                var cards = PartTargets(state);
                string slot = partSlots.TryGetValue(chosen, out var kind) && kind == "circuit" ? "c" : "e";
                var targets = Scroll(p, "SavePartTargetList", 24, 346, 1232, 250, 1232, (cards.Count + 2) / 3 * 62, Background);
                for (int i = 0; i < cards.Count; i++)
                {
                    string key = Str(cards[i]["key"])!, current = Str((cards[i]["mods"] as JObject)?[slot]) ?? "";
                    Button(targets, "SavePartTarget:" + key, DeckCardText(cards[i]) + "\n" + (current.Length != 0 ? "같은 칸의 「" + PartName(current) + "」 교체" : "빈 " + SlotName(chosen) + " 칸"),
                        i % 3 * 412, i / 3 * 62, 400, 56, () => Launch(() => ApplySavePartAsync(chosen, key)), Line, 17).interactable = ready;
                }
            }
            string status = !readable || session == null ? NotReady() : busy ? "서버와 통신하는 중이에요…" : pick == null ? "부품을 고르면 붙일 카드가 보여요. 아직 아무것도 보내지 않았어요."
                : "카드를 누르면 바로 서버에 보내요. 붙일 수 있는지는 서버가 판정해요.";
            Label(p, status, 24, 612, 960, 64, 18, readable ? Muted : Danger);
            Button(p, "CloseSaveParts", "닫기", 1000, 612, 256, 64, CloseSaveParts, Line, 22);
        }

        // DONE after a confirmed save: the run's outcome, this run's entry of the account's public history, and the saved decks and memory
        // version exactly as the confirmed account read shows them — never a local copy of what was selected. The next run is the header's
        // existing new-run command, which starts from this memory (useMemory).
        void DoneView()
        {
            var snap = Snapshot!;
            var state = State()!;
            string runId = snap.Run!.RunId;
            var memory = snap.Account.ToJObject()["memory"] as JObject;
            var decks = memory?["memory"] as JObject;
            var entry = ((memory?["history"] as JArray) ?? new JArray()).OfType<JObject>().LastOrDefault(h => Str(h["runId"]) == runId);
            int? saved = (entry?["selectedChangeIds"] as JArray)?.Count;
            var head = Box(frame!, "Done", 32, 100, 1536, 150, Panel);
            Label(head, OutcomeName(Str((state["result"] as JObject)?["outcome"])) + " · 기억 저장을 마쳤어요", 20, 10, 1496, 44, 32, Brass, FontStyle.Bold);
            Label(head, (entry == null ? "이 탐사의 저장 기록은 서버 계정 기록에서 찾지 못했어요" : saved == 0 ? "기존 기억 덱을 그대로 유지했어요" : "변화 " + saved + "개를 기억에 저장했어요")
                + "  ·  저장된 기억 버전 " + snap.Account.MemoryVersion + " (서버 확인)", 20, 62, 1496, 34, 22, Memory);
            Label(head, "다음 탐사는 아래의 저장된 기억 덱으로 시작해요. 위의 '새 서버 탐사'를 누르면 이 기억으로 출발해요.", 20, 104, 1496, 34, 19, Muted);
            var columns = PartyIds(state).Concat(decks?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>()).Distinct()
                .Select(cid => (cid, cards: (decks?[cid] as JArray)?.OfType<JObject>().Select(SpecText).ToList())).ToList();
            float Height(List<string>? cards) => 50 + (cards == null ? 0 : ChipsHeight(cards.Count)) + 16;
            var list = Scroll(frame!, "SavedList", 32, 260, 1536, 576, 1536, columns.Sum(c => Height(c.cards)), Background);
            float y = 0;
            foreach (var (cid, cards) in columns)
            {
                var section = Box(list, "Saved:" + cid, 0, y, 1536, Height(cards) - 16, Panel);
                Label(section, CharacterName(cid) + (cards == null ? " · 기본 덱 (저장된 변화 없음)" : " · 저장된 기억 덱 " + cards.Count + "장"), 20, 8, 1496, 36, 24, Ink, FontStyle.Bold);
                if (cards != null) Chips(section, cards, 50, "저장된 카드가 없어요.");
                y += Height(cards);
            }
        }

        // P1-SERVER-SERVICES-UI: the published rest {uses}. How much is healed and whether healing is allowed (CANNOT_HEAL) are Core's,
        // so no amount is stated here; the new HP appears only from the committed state.
        void RestView()
        {
            var state = State()!;
            int uses = Int(Pending(state, "rest")!["uses"]) ?? 0;
            bool ready = CanCommand;
            var panel = Box(frame!, "Rest", 32, 100, 1536, 330, Panel);
            Label(panel, "휴식", 20, 14, 400, 44, 32, Brass, FontStyle.Bold);
            Label(panel, "체력 " + Int(state["hp"]) + " / " + Int(state["maxHp"]) + "   ·   남은 이용 " + uses + "회  (서버 기록)", 20, 66, 1496, 36, 24, Ink);
            Button(panel, "RestHeal", "체력 회복", 20, 140, 440, 72, () => Launch(RestHealAsync), Memory, 23).interactable = ready && uses > 0;
            Button(panel, "RestUpgrade", "카드 강화(+)", 480, 140, 440, 72, () => OpenDeckPick(DeckPurpose.Upgrade), Line, 23).interactable = ready && uses > 0;
            Button(panel, "Proceed", "떠나기", 1196, 140, 320, 72, () => Launch(ProceedAsync), Brass, 24).interactable = ready;
            Label(panel, "회복이나 강화 중 남은 이용 횟수만큼 고를 수 있어요. 회복량과 회복할 수 있는지는 서버가 판정해요.", 20, 236, 1496, 64, 20, Muted);
        }

        // The published forge {uses, offers, fromEvent?}. Without an open offer: variation (forgeOffer, no use spent) or removal, each on one
        // chosen deck card. With one: only its published parts (forgeApply) or leaving — no re-roll, as in the original.
        void ForgeView()
        {
            var state = State()!;
            var p = Pending(state, "forge")!;
            int uses = Int(p["uses"]) ?? 0;
            var offers = p["offers"] as JObject;
            bool ready = CanCommand;
            var panel = Box(frame!, "Forge", 32, 100, 1536, 440, Panel);
            Label(panel, "공방" + (Truthy(p["fromEvent"]) ? " · 사건의 변주" : ""), 20, 14, 600, 44, 32, Brass, FontStyle.Bold);
            Label(panel, "남은 이용 " + uses + "회  (서버 기록)", 640, 22, 876, 36, 22, Ink);
            if (offers != null)
            {
                Label(panel, CardName(Str(DeckCard(Str(offers["key"]))?["id"])) + "에 붙일 부품을 고르세요", 20, 66, 1496, 36, 24, Ink);
                var parts = Ids(offers["parts"]);
                float width = Math.Min(490, (1496f + 16) / Math.Max(1, parts.Count) - 16);
                for (int k = 0; k < parts.Count; k++)
                {
                    string id = parts[k];
                    var button = Button(panel, "ForgePart:" + id, "", 20 + k * (width + 16), 116, width, 180, () => Launch(() => ForgeApplyAsync(id)), Line);
                    button.interactable = ready;
                    Label(button.transform, PartName(id) + (SlotName(id).Length != 0 ? "  ·  " + SlotName(id) : ""), 14, 8, width - 28, 34, 23, Ink, FontStyle.Bold);
                    Label(button.transform, partTexts.TryGetValue(id, out var text) ? text : "", 14, 48, width - 28, 124, 18, Muted);
                }
                Button(panel, "Proceed", "변주하지 않고 떠나기", 1196, 350, 320, 64, () => Launch(ProceedAsync), Line, 22).interactable = ready;
                Label(panel, "부품 하나를 고르면 그 카드에 붙고 이용 1회를 써요.", 20, 360, 1150, 44, 20, Muted);
            }
            else
            {
                Label(panel, "카드를 다듬는다. 변주는 고른 카드에 서버가 제시한 부품 하나를 붙이고(회로·각인 각 1칸), 제거는 덱에서 카드 1장을 빼요.", 20, 66, 1496, 60, 22, Ink);
                Button(panel, "ForgeVariation", "카드 변주", 20, 150, 440, 72, () => OpenDeckPick(DeckPurpose.ForgeOffer), Memory, 23).interactable = ready && uses > 0;
                Button(panel, "ForgeRemove", "카드 제거", 480, 150, 440, 72, () => OpenDeckPick(DeckPurpose.ForgeRemove), Line, 23).interactable = ready && uses > 0;
                Button(panel, "Proceed", "떠나기", 1196, 150, 320, 72, () => Launch(ProceedAsync), Brass, 24).interactable = ready;
            }
        }

        // The published shop: every listing with its server price and sold mark, and the one removal at its published cost. Every row stays
        // reachable on a small screen: a vertical scroll whose height comes from the published counts. A buy button is enabled, as in the
        // original, only for an unsold listing the shown coins cover; COINS_LOW itself is Core's.
        void ShopView()
        {
            var state = State()!;
            var p = Pending(state, "shop")!;
            int coins = Int(state["coins"]) ?? 0;
            bool ready = CanCommand;
            var head = Box(frame!, "ShopHead", 32, 100, 1536, 56, Panel);
            Label(head, "상점  ·  보유 동전 " + Int(state["coins"]) + "  (서버 기록)", 20, 10, 1210, 40, 26, Brass, FontStyle.Bold);
            Button(head, "Proceed", "떠나기", 1256, 4, 268, 48, () => Launch(ProceedAsync), Brass, 23).interactable = ready;
            var sections = ShopKinds.Select(kind => (kind, items: Items(p, kind))).ToList();
            var list = Scroll(frame!, "Shop", 32, 166, 1536, 670, 1536, sections.Sum(s => 40 + s.items.Count * 96) + 80, Background);
            float y = 0;
            foreach (var (kind, items) in sections)
            {
                string title = kind == "cards" ? "카드" : kind == "parts" ? "부품" : "유물";
                Label(list, title + (items.Count == 0 ? "  ·  진열된 상품이 없어요" : ""), 20, y + 6, 1000, 30, 21, Memory, FontStyle.Bold);
                y += 40;
                for (int k = 0; k < items.Count; k++)
                {
                    string id = Str(items[k]["id"]) ?? "";
                    int? price = Int(items[k]["price"]);
                    bool sold = Bool(items[k]["sold"]);
                    var row = Box(list, "Shop:" + kind + ":" + k, 0, y, 1536, 88, Panel);
                    Label(row, ItemName(kind, id), 20, 8, 1260, 32, 23, sold ? Muted : Ink, FontStyle.Bold);
                    Label(row, ItemText(kind, id), 20, 42, 1260, 42, 16, Muted);
                    Button(row, "Buy:" + kind + ":" + k + ":" + id, sold ? "판매 완료" : "동전 " + price, 1316, 16, 200, 56, () => Launch(() => BuyAsync(kind, id)), sold ? Line : Brass, 22)
                        .interactable = ready && !sold && price != null && coins >= price;
                    y += 96;
                }
            }
            var removal = Box(list, "ShopRemoval", 0, y, 1536, 80, Panel);
            int? removeCost = Int(p["removeCost"]);
            if (removeCost == null) Label(removal, "카드 제거는 이 상점에서 이미 이용했어요.", 20, 20, 1200, 40, 22, Muted);
            else
            {
                Label(removal, "카드 제거 1회  ·  동전 " + removeCost, 20, 20, 1100, 40, 22, Ink);
                Button(removal, "ShopRemove", "제거할 카드 고르기", 1176, 12, 340, 56, () => OpenDeckPick(DeckPurpose.ShopRemove), Line, 22).interactable = ready && coins >= removeCost;
            }
        }

        // The local, unsent deck sheet: every public deck card in a scroll (a deck has no fixed size), the original's pickable rule, the
        // chosen keys in order (numbered when more than one is asked), confirm (one command) or cancel (nothing sent).
        void DeckPickView()
        {
            var sheet = deckPick!;
            int required = Required(sheet);
            var blocker = Box(frame!, "DeckPickBlocker", 0, 0, 1600, 900, new Color(0, 0, 0, 0.6f));
            blocker.GetComponent<Image>().raycastTarget = true;
            var p = Box(frame!, "DeckPicker", 160, 90, 1280, 720, Panel);
            Label(p, DeckTitle(sheet), 24, 16, 1232, 44, 28, Ink, FontStyle.Bold);
            Label(p, (sheet.Purpose == DeckPurpose.Upgrade ? "이미 강화한 카드와 무기 카드는 고를 수 없어요." : "무기 카드는 고를 수 없어요.") + " 확정하기 전에는 아무것도 보내지 않아요.",
                24, 62, 1232, 28, 19, Muted);
            var deck = Deck();
            var list = Scroll(p, "DeckPickList", 24, 100, 1232, 500, 1232, (deck.Count + 2) / 3 * 62, Background);
            for (int i = 0; i < deck.Count; i++)
            {
                string key = Str(deck[i]["key"]) ?? "";
                bool ok = Pickable(deck[i], sheet.Purpose);
                int order = sheet.Keys.IndexOf(key);
                Button(list, "DeckCard:" + key, (order >= 0 && sheet.Count > 1 ? (order + 1) + ". " : "") + DeckCardText(deck[i]) + (ok ? "" : " · 선택 불가"),
                    i % 3 * 412, i / 3 * 62, 400, 54, () => PickDeckCard(key), order >= 0 ? Memory : Line, 18).interactable = ok && !busy;
            }
            bool complete = required > 0 && sheet.Keys.Count == required;
            string verb = sheet.Purpose == DeckPurpose.Upgrade ? "강화하기" : sheet.Purpose == DeckPurpose.ForgeOffer ? "이 카드에 변주"
                : sheet.Purpose == DeckPurpose.EventCards ? "선택 완료" : "제거하기";
            Button(p, "ConfirmDeckPick", verb + (sheet.Count > 1 ? " (" + sheet.Keys.Count + "/" + required + ")" : ""), 24, 620, 420, 64,
                () => Launch(ConfirmDeckPickAsync), complete ? Brass : Line, 22).interactable = complete && !busy && session != null && session.State.CanMutate;
            Button(p, "CancelDeckPick", "취소 (보내지 않음)", 1000, 620, 256, 64, CancelDeckPick, Line, 22);
        }

        // P1-SERVER-REWARD-UI §7/§14.3/§17.4: the server's public pending reward exactly as published (coins, cards [id], relic [id]|null,
        // part?, evolutions [{key, options}]). Every button is one existing command; only published choices are offered and Core decides
        // the rest. A claimed choice disappears when the committed state no longer lists it — never by local deletion.
        void RewardView()
        {
            var state = State()!;
            var p = Pending(state, "reward")!;
            bool ready = CanCommand;
            var head = Box(frame!, "RewardCoins", 32, 100, 1536, 56, Panel);
            Label(head, "전투 보상  ·  보상 동전 " + Int(p["coins"]) + "  ·  보유 동전 " + Int(state["coins"]) + "  (서버 기록)", 20, 10, 1210, 40, 26, Brass, FontStyle.Bold);
            // Proceeding is an explicit command; whether unclaimed rewards may be left is Core's verdict.
            Button(head, "Proceed", "다음으로 진행", 1256, 4, 268, 48, () => Launch(ProceedAsync), Brass, 23).interactable = ready;
            // Every published offer stays reachable: the rows sit in a vertical scroll whose content height comes from the actual number
            // of published evolution rows (growthCap can reach 4, and every qualifying deck card is offered) plus the card/relic/part rows.
            var evolutions = ((p["evolutions"] as JArray) ?? new JArray()).OfType<JObject>().ToList();
            var panel = Scroll(frame!, "Reward", 32, 166, 1536, 670, 1536, evolutions.Count * 80 + 204 + 132 + 72, Background);
            float y = 0;
            var deck = ((state["deck"] as JArray) ?? new JArray()).OfType<JObject>().ToList();
            foreach (var offer in evolutions)
            {
                string key = Str(offer["key"]) ?? "";
                string? from = Str(deck.FirstOrDefault(c => Str(c["key"]) == key)?["id"]);
                var row = Box(panel, "Evolution:" + key, 0, y, 1536, 72, Panel);
                Label(row, CardName(from) + " · 진화를 고르세요", 20, 8, 560, 56, 21, Ink);
                var options = ((offer["options"] as JArray) ?? new JArray()).Select(Str).OfType<string>().ToList();
                for (int k = 0; k < options.Count; k++)
                {
                    string branch = options[k];
                    Button(row, "Evolve:" + key + ":" + branch, CardName(branch), 600 + k * 300, 10, 284, 52, () => Launch(() => EvolveAsync(key, branch)), Memory, 21).interactable = ready;
                }
                y += 80;
            }
            var cards = Ids(p["cards"]);
            var cardPanel = Box(panel, "RewardCards", 0, y, 1536, 196, Panel);
            if (cards.Count == 0) Label(cardPanel, "남은 카드 보상이 없어요.", 20, 10, 800, 34, 22, Muted);
            else
            {
                Label(cardPanel, "카드 1장을 덱에 추가할 수 있어요.", 20, 8, 1100, 30, 21, Ink, FontStyle.Bold);
                for (int k = 0; k < cards.Count; k++)
                {
                    string id = cards[k];
                    var button = Button(cardPanel, "RewardCard:" + id, "", 20 + k * 380, 44, 360, 140, () => Launch(() => TakeCardAsync(id)), Line);
                    button.interactable = ready;
                    Label(button.transform, CardName(id), 12, 8, 336, 32, 23, Ink, FontStyle.Bold);
                    Label(button.transform, cardTexts.TryGetValue(id, out var text) ? text : "", 12, 42, 336, 92, 16, Muted);
                }
                Button(cardPanel, "SkipCard", "카드 받지 않기", 1176, 44, 340, 60, () => Launch(SkipCardAsync), Line, 22).interactable = ready;
            }
            y += 204;
            var relics = Ids(p["relic"]);
            var relicPanel = Box(panel, "RewardRelics", 0, y, 1536, 124, Panel);
            if (relics.Count == 0) Label(relicPanel, "남은 유물 보상이 없어요.", 20, 10, 800, 34, 22, Muted);
            else
            {
                Label(relicPanel, "유물 1개 선택", 20, 6, 600, 30, 21, Ink, FontStyle.Bold);
                for (int k = 0; k < relics.Count; k++)
                {
                    string id = relics[k];
                    var button = Button(relicPanel, "Relic:" + id, "", 20 + k * 500, 40, 480, 78, () => Launch(() => TakeRelicAsync(id)), Line);
                    button.interactable = ready;
                    Label(button.transform, relicNames.TryGetValue(id, out var name) ? name : "유물", 12, 6, 456, 30, 22, Ink, FontStyle.Bold);
                    Label(button.transform, relicTexts.TryGetValue(id, out var text) ? text : "", 12, 38, 456, 36, 16, Muted);
                }
            }
            y += 132;
            string? part = Str(p["part"]);
            var partPanel = Box(panel, "RewardPart", 0, y, 1536, 72, Panel);
            if (part == null) Label(partPanel, "남은 부품 보상이 없어요.", 20, 10, 800, 34, 22, Muted);
            else
            {
                Label(partPanel, "부품 발견: " + (partNames.TryGetValue(part, out var name) ? name : "부품") + " — " + (partTexts.TryGetValue(part, out var text) ? text : ""), 20, 8, 1200, 56, 19, Ink);
                Button(partPanel, "TakePart", "가져가기", 1296, 8, 220, 56, () => Launch(TakePartAsync), Memory, 22).interactable = ready;
            }
        }

        void BattleView()
        {
            var b = Battle()!;
            // Party
            var party = Box(frame!, "Party", 32, 96, 380, 264, Panel);
            Label(party, "파티 체력  " + Int(b["hp"]) + " / " + Int(b["maxHp"]), 18, 8, 344, 30, 24, Ink, FontStyle.Bold);
            Bar(party, 18, 42, 344, 8, Int(b["hp"]) ?? 0, Int(b["maxHp"]) ?? 0, Memory);
            Label(party, "방어 " + Int(b["block"]) + "   ·   " + Int(b["turn"]) + "턴", 18, 56, 184, 30, 20, Ink);
            Label(party, "행동력  " + Int(b["ap"]), 206, 52, 156, 36, 28, Brass, FontStyle.Bold);
            var piles = b["piles"] as JObject;
            Label(party, "뽑을 더미 " + Int(piles?["draw"]) + " · 손패 " + Int(piles?["hand"]) + " · 버린 더미 " + Int(piles?["discard"]) + " · 소멸 " + Int(piles?["exhaust"]),
                18, 88, 344, 40, 16, Muted);
            ActorsView(party, b);

            // Enemies (public HP, block, alive, current and next intent as published)
            var enemies = Enemies();
            var foes = Box(frame!, "Enemies", 428, 96, 1140, 264, Panel);
            float width = 1140f / Math.Max(1, enemies.Count);
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                string uid = Str(e["uid"]) ?? "";
                bool alive = Bool(e["alive"]);
                var box = Button(foes, "Enemy:" + uid, "", i * width + 6, 6, width - 12, 252, () => SelectEnemy(uid), uid == target ? Brass : Line);
                box.interactable = alive && !SelectionLocked();
                Label(box.transform, (Str(e["name"]) ?? "적") + (alive ? "" : " · 처치"), 12, 10, width - 36, 36, 25, alive ? Ink : Muted, FontStyle.Bold);
                Label(box.transform, "체력 " + Int(e["hp"]) + " / " + Int(e["maxHp"]) + "   방어 " + Int(e["block"]), 12, 52, width - 36, 30, 21, Ink);
                Bar(box.transform, 12, 86, width - 36, 8, Int(e["hp"]) ?? 0, Int(e["maxHp"]) ?? 0, Danger);
                if (alive) Label(box.transform, "이번  " + MoveText(e["intent"]) + "\n다음  " + MoveText(e["next"]), 12, 104, width - 36, 96, 19, Brass);
                if (uid == target) Label(box.transform, "대상", 12, 210, width - 36, 30, 19, Background, FontStyle.Bold);
            }

            // Time reservations: the server's cap/used/sealed/due per column and every public reservation. Under the now-play rules
            // (docs/RULES_NOW_PLAY.md) the live content reserves nothing, so the board shows only when the server publishes a reservation.
            var reserved = ((b["reservations"] as JArray) ?? new JArray()).OfType<JObject>()
                .Select(r => CardName(Str(r["id"])) + " (" + Int(r["due"]) + "턴)").ToList();
            if (reserved.Count > 0)
            {
                var slots = b["slots"] as JObject;
                var board = Box(frame!, "Reservations", 32, 370, 1000, 112, Panel);
                Label(board, "기억 시간표", 16, 8, 200, 30, 22, Memory, FontStyle.Bold);
                Label(board, SlotText("다음 턴", slots?["next"] as JObject) + "\n" + SlotText("다다음 턴", slots?["after"] as JObject), 16, 40, 420, 64, 18, Ink);
                Label(board, string.Join(" · ", reserved), 450, 10, 534, 94, 18, Ink);
            }

            // Ordinary action presentation strip (text only; the next selection stays open): its title, the display pace and skip.
            var strip = Box(frame!, "Presentation", 1048, 370, 520, 112, Panel);
            Label(strip, rows != null && !barrier ? presentationTitle : "행동 결과", 14, 6, 182, 28, 18, Brass, FontStyle.Bold);
            PaceButton(strip, MemoryForgeExpeditionClient.ActionPacing.Normal, "보통", 200, 84);
            PaceButton(strip, MemoryForgeExpeditionClient.ActionPacing.Double, "2배속", 288, 96);
            PaceButton(strip, MemoryForgeExpeditionClient.ActionPacing.Reduced, "동작 줄이기", 388, 124);
            presentationText = Label(strip, "", 14, 40, 380, 68, 16, Memory);
            if (rows != null && !barrier)
            {
                Button(strip, "SkipPresentation", "건너뛰기", 404, 44, 104, 60, SkipPresentation, Line, 19);
                UpdatePresentation();
            }
            else presentationText.text = prepared != null ? "사용 대기: " + CardName(Str(Hand(prepared.Uid)?["id"])) : "";

            HandView(b);
            ActionView();
        }

        // §5.10: every published actor (never capped or cut: the rows scroll) with its published gauge/gaugeMax and one ult button. The
        // button opens only for a full gauge in the live PLAYER phase while a new command may be sent; the gauge shown is the server's.
        void ActorsView(Transform party, JObject b)
        {
            var actors = Actors(b);
            int? max = Int(b["gaugeMax"]);
            bool open = CanCommand && Str(b["phase"]) == "PLAYER" && !(b["result"] is JObject);
            var list = Scroll(party, "Actors", 8, 130, 364, 128, 364, actors.Count * 42, Panel);
            for (int i = 0; i < actors.Count; i++)
            {
                var actor = actors[i];
                string id = Str(actor["id"]) ?? "";
                int? gauge = Int(actor["gauge"]);
                bool full = GaugeFull(actor, b);
                float y = i * 42;
                Label(list, ActorName(actor) + "  " + (gauge?.ToString() ?? "-") + " / " + (max?.ToString() ?? "-"), 8, y + 1, 216, 26, 18, full ? Brass : Ink);
                Bar(list, 8, y + 29, 216, 6, gauge ?? 0, max ?? 0, Brass);
                Button(list, "Ult:" + id, full ? "필살기" : "충전 중", 232, y + 2, 124, 38, () => Launch(() => UltAsync(id)), full ? Brass : Line, 18)
                    .interactable = full && open;
            }
            if (actors.Count == 0) Label(list, "서버가 동료 정보를 보내지 않았어요.", 8, 8, 340, 30, 17, Muted);
        }

        // One display-pace choice of the strip (the chosen one marked); changing it sends nothing.
        void PaceButton(Transform strip, MemoryForgeExpeditionClient.ActionPacing pace, string caption, float x, float width)
        {
            bool chosen = pace == actionPace;
            Button(strip, "Pace:" + pace, (chosen ? "● " : "") + caption, x, 4, width, 30, () => SetActionPace(pace), chosen ? Memory : Line, 15);
        }

        void HandView(JObject b)
        {
            var hand = ((b["hand"] as JArray) ?? new JArray()).OfType<JObject>().ToList();
            var panel = Box(frame!, "Hand", 32, 492, 1536, 168, Panel);
            float width = Math.Min(150, (1536f - 16) / Math.Max(1, hand.Count));
            for (int i = 0; i < hand.Count; i++)
            {
                var card = hand[i];
                string uid = Str(card["uid"]) ?? "";
                string cost = card["cost"] is JValue value && value.Type == JTokenType.Integer ? "행동력 " + (int)value : "사용 불가";
                var button = Button(panel, "Card:" + uid, CardName(Str(card["id"])) + (Bool(card["plus"]) ? "+" : "") + "\n" + cost,
                    8 + i * width, 10, width - 8, 148, () => SelectCard(uid), uid == selectedCard ? Memory : Line, 20);
                button.interactable = !SelectionLocked();
            }
            if (hand.Count == 0) Label(panel, "손패가 없어요", 20, 60, 600, 40, 22, Muted);
        }

        void ActionView()
        {
            var panel = Box(frame!, "Action", 32, 668, 1536, 170, Panel);
            var card = selectedCard == null ? null : Hand(selectedCard);
            if (card == null)
            {
                Label(panel, "손패의 카드를 누르면 서버의 비용과 예상을 보여 줘요.", 18, 14, 1100, 36, 22, Muted);
                Label(panel, "필살기는 왼쪽 동료의 버튼으로 써요. 적이 여럿일 때 대상이 필요한 필살기는 적을 먼저 눌러 골라 주세요.", 18, 58, 1100, 34, 19, Muted);
            }
            else
            {
                var view = QueryCurrent() ? queryResult!.View : null;
                var source = view?.ToJObject()["source"];
                Label(panel, (Str(source?["name"]) ?? CardName(Str(card["id"]))) + "  " + TargetText(), 18, 10, 760, 36, 25, Ink, FontStyle.Bold);
                Label(panel, Str(source?["text"]) ?? (busy || queryWanted ? "카드 설명을 불러오는 중…" : ""), 18, 48, 760, 64, 18, Muted);
                Label(panel, view == null ? "예상을 확인하는 중…" : ForecastText(view), 18, 116, 760, 44, 19, view?.ForecastError != null ? Danger : Memory);
                var modes = (card["modes"] as JArray)?.Select(Str).OfType<string>().ToList() ?? new List<string>();
                // Under the now-play rules a card's only published mode is now: no mode step, its cost and verdict as a plain line,
                // like the expedition screen. Mode buttons come back only when the server publishes a reservation mode.
                bool onlyNow = modes.Count == 1 && modes[0] == "now";
                for (int i = 0; i < modes.Count; i++)
                {
                    string mode = modes[i];
                    // blocked[mode] is Core's check of the DEFAULT request only (auto target, no manual picks): shown, never enforced here.
                    string? blocked = Str((card["blocked"] as JObject)?[mode]);
                    string cost = "행동력 " + (Int((card["costs"] as JObject)?[mode])?.ToString() ?? "-") + (blocked != null ? "\n기본 요청: " + ErrorText(blocked) : "");
                    if (onlyNow) { Label(panel, cost, 790, 20, 380, 60, 19, blocked != null ? Danger : Ink); break; }
                    var button = Button(panel, "Mode:" + mode, ModeName(mode) + " · " + cost, 790 + i * 132, 14, 124, 92, () => SelectMode(mode), mode == selectedMode ? Brass : Line, 16);
                    button.interactable = !SelectionLocked();
                }
                if (modes.Count == 0) Label(panel, ErrorText(Str(card["error"]) ?? ""), 790, 20, 380, 60, 18, Danger);
                // The summary above is cut to its box; the detail shows the whole server text in a scroll (read-only, sends nothing).
                Button(panel, "OpenCardDetails", "카드 설명 전체 보기", 790, 116, 240, 44, () => OpenCardDetails(), Line, 18)
                    .interactable = picker == null && !PresentationBarrier;
            }
            var confirm = Button(panel, "Confirm", rows != null && !barrier ? "사용 대기" : "사용 확정", 1190, 14, 160, 92, () => Launch(ConfirmAsync), Brass, 24);
            confirm.interactable = card != null && !busy && session != null && session.State.CanMutate && picker == null && !PresentationBarrier && prepared == null;
            var end = Button(panel, "EndTurn", "턴 종료", 1362, 14, 160, 92, () => Launch(EndTurnAsync), Line, 24);
            end.interactable = CanCommand;
            if (prepared != null) Button(panel, "CancelPrepared", "대기 취소", 1190, 116, 160, 44, () => CancelPrepared(), Line, 18);
        }

        void PickView()
        {
            var sheet = picker!;
            var blocker = Box(frame!, "PickBlocker", 0, 0, 1600, 900, new Color(0, 0, 0, 0.6f));
            blocker.GetComponent<Image>().raycastTarget = true;
            var p = Box(frame!, "PickSheet", 200, 150, 1200, 600, Panel);
            var view = QueryCurrent() ? queryResult!.View : null;
            var choices = view?.ToJObject()["choices"];
            var requests = (choices?["requests"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            int index = Int(choices?["index"]) ?? 0;
            string label = index < requests.Count ? Str(requests[index]["label"]) ?? "카드" : "선택 완료";
            Label(p, CardName(Str(Hand(sheet.Uid)?["id"])) + " · " + label + " 고르기 (" + sheet.Picks.Count + "장 고름)", 24, 18, 1152, 44, 28, Ink, FontStyle.Bold);
            if (view == null) Label(p, "서버에서 고를 수 있는 카드를 확인하는 중이에요…", 24, 90, 1152, 40, 22, Muted);
            else
            {
                var candidates = (choices?["candidates"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    string uid = Str(candidates[i]["uid"]) ?? "";
                    var button = Button(p, "Pick:" + uid, CardName(Str(candidates[i]["id"])) + "\n" + ZoneName(Str(candidates[i]["zone"])),
                        24 + (i % 6) * 190, 80 + (i / 6) * 120, 178, 108, () => PickCard(uid), Line, 20);
                    button.interactable = !busy;
                }
                string? blocked = view.ChoicesBlocked ?? view.ChoicesError;
                if (candidates.Count == 0 && NeedsPicks(view)) Label(p, blocked != null ? ErrorText(blocked) : "고를 수 있는 카드가 없어요.", 24, 90, 1152, 40, 22, Danger);
                if (!NeedsPicks(view)) Label(p, view.ForecastError != null ? ErrorText(view.ForecastError) : ForecastText(view), 24, 420, 1152, 40, 22, view.ForecastError != null ? Danger : Memory);
            }
            var done = Button(p, "PickConfirm", "이대로 사용", 24, 500, 300, 76, () => Launch(ConfirmPicksAsync), Brass, 24);
            done.interactable = view != null && !NeedsPicks(view) && view.ForecastError == null && !busy && session != null && session.State.CanMutate;
            var auto = Button(p, "PickAuto", "자동 선택", 340, 500, 240, 76, () => Launch(AutoPickAsync), Line, 24);
            auto.interactable = !busy && session != null && session.State.CanMutate;
            Button(p, "PickCancel", "취소 (보내지 않음)", 900, 500, 276, 76, CancelPick, Line, 22);
        }

        void BarrierView()
        {
            var blocker = Box(frame!, "ResultBlocker", 0, 0, 1600, 900, new Color(0, 0, 0, 0.7f));
            blocker.GetComponent<Image>().raycastTarget = true;
            var p = Box(frame!, "ResultSheet", 260, 90, 1080, 720, Panel);
            Label(p, presentationTitle, 32, 16, 900, 46, 32, Brass, FontStyle.Bold);
            presentationText = Label(p, "", 32, 76, 1016, 520, 21, Ink);
            Button(p, "ResultSkip", "모두 보기", 32, 620, 300, 76, SkipPresentation, Line, 24);
            continueButton = Button(p, "ResultContinue", "계속", 748, 620, 300, 76, ContinuePresentation, Brass, 24);
            UpdatePresentation();
        }

        // The read-only detail of the selected hand card (the original's card-info sheet) above the battle: the current battle query's
        // server name and full text (its effective BattleText), the published modes with cost and default-request verdict, the attached
        // parts with their texts and the current forecast. Every block grows to its wrapped height and the body scrolls, so nothing is cut
        // off; a missing server value is said to be missing and nothing is predicted here. The prepared play keeps its own dispatch.
        void CardDetailsView()
        {
            var card = Hand(selectedCard!)!;
            var view = QueryCurrent() ? queryResult!.View : null;
            var source = view?.ToJObject()["source"];
            string? serverName = Str(source?["name"]), serverText = Str(source?["text"]);
            bool loading = view == null && (busy || queryWanted);
            var blocker = Box(frame!, "CardDetailsBlocker", 0, 0, 1600, 900, new Color(0, 0, 0, 0.6f));
            blocker.GetComponent<Image>().raycastTarget = true;
            var p = Box(frame!, "CardDetailsSheet", 240, 80, 1120, 740, Panel);
            Label(p, serverName ?? CardName(Str(card["id"])) + (Bool(card["plus"]) ? "+" : ""), 24, 14, 920, 52, 30, Ink, FontStyle.Bold);
            Button(p, "CloseCardDetails", "닫기", 960, 12, 136, 56, CloseCardDetails, Line, 22);
            var content = Scroll(p, "CardDetailsScroll", 24, 80, 1072, 636, 1072, 636, Background);
            string basis = view == null
                ? (loading ? "서버 설명을 불러오는 중이에요." : "서버 설명을 지금 받지 못했어요.") + " 위 이름은 이 기기의 콘텐츠 기준이에요."
                : "서버 전투 조회의 현재 설명 · " + ModeName(view.Mode) + " " + TargetText() + (serverName == null ? " · 서버가 이름을 보내지 않아 이 기기의 이름을 보여 줘요" : "");
            float y = Flow(content, basis, 12, 19, Memory);
            string body = view == null ? (loading ? "설명을 불러오는 중이에요…" : "설명을 받지 못했어요. 창을 닫고 카드를 다시 골라 주세요.")
                : serverText ?? "서버가 이 카드의 설명을 보내지 않았어요.";
            y = Flow(content, body, y, 24, serverText != null ? Ink : Muted, "CardDetailsBody");
            y = Flow(content, ModesText(card), y, 20, Brass);
            string parts = PartsText(card);
            if (parts.Length != 0) y = Flow(content, parts, y, 19, Ink);
            y = Flow(content, view == null ? "예상: 서버 조회를 기다리는 중이에요." : (view.ForecastError != null ? "지금 이 요청으로는 쓸 수 없어요: " : "") + ForecastText(view),
                y, 19, view?.ForecastError != null ? Danger : Memory);
            content.sizeDelta = new Vector2(1072, Math.Max(636, y));
            content.parent.GetComponent<ScrollRect>().vertical = y > 636;
            // A redraw of the same open detail keeps where the player scrolled (clamped to the new content).
            if (detailsScrollY != null && detailsScrollAt == detailsAt)
                content.anchoredPosition = new Vector2(0, Mathf.Clamp(detailsScrollY.Value, 0, Math.Max(0, y - 636)));
            detailsContent = content; detailsScrollAt = detailsAt;
        }
        // A label that grows to its wrapped height (no best fit, no truncation); returns the y below it.
        float Flow(RectTransform content, string value, float y, int size, Color color, string name = "Text")
        {
            var text = Label(content, value, 16, y, 1040, 10, size, color);
            text.gameObject.name = name;
            text.resizeTextForBestFit = false; text.verticalOverflow = VerticalWrapMode.Overflow;
            float height = Mathf.Ceil(text.preferredHeight) + 6;
            text.rectTransform.sizeDelta = new Vector2(1040, height);
            return y + height + 14;
        }
        // The published modes of a hand card with each cost and the default-request verdict (blocked[mode]: auto target, no picks; shown,
        // never enforced here); the selected one marked.
        string ModesText(JObject card)
        {
            var modes = (card["modes"] as JArray)?.Select(Str).OfType<string>().ToList() ?? new List<string>();
            if (modes.Count == 0) return "사용 방식 없음 · " + ErrorText(Str(card["error"]) ?? "");
            return "사용 방식 (서버 공개 값)\n" + string.Join("\n", modes.Select(mode =>
            {
                string? blocked = Str((card["blocked"] as JObject)?[mode]);
                return (mode == selectedMode ? "● " : "· ") + ModeName(mode) + " · 행동력 " + (Int((card["costs"] as JObject)?[mode])?.ToString() ?? "-")
                    + (blocked != null ? " · 기본 요청: " + ErrorText(blocked) : "");
            }));
        }
        // The card's attached circuit/engraving parts and temporary parts with their content texts; an id this device cannot name is
        // still listed as it is published.
        string PartsText(JObject card)
        {
            var slots = card["mods"] as JObject;
            var ids = new[] { Str(slots?["c"]), Str(slots?["e"]) }.OfType<string>().Where(m => m.Length != 0)
                .Concat(((card["temp"] as JArray) ?? new JArray()).Select(Str).OfType<string>()).ToList();
            if (ids.Count == 0) return "";
            return "부품\n" + string.Join("\n", ids.Select(id => partNames.ContainsKey(id)
                ? "· " + PartName(id) + (SlotName(id).Length != 0 ? " (" + SlotName(id) + ")" : "") + (partTexts.TryGetValue(id, out var text) && text.Length != 0 ? " — " + text : "")
                : "· 이 기기에서 이름을 모르는 표시: " + id));
        }

        // ───────── text (labels of server values; no rule is computed) ─────────

        string TargetText()
        {
            if (NeedsTarget() == null) return "→ 파티";
            if (target != null) return "→ " + (Str(Enemies().FirstOrDefault(e => Str(e["uid"]) == target)?["name"]) ?? "적");
            var alive = Enemies().Where(e => Bool(e["alive"])).ToList();
            return alive.Count == 1 ? "→ " + (Str(alive[0]["name"]) ?? "적") : "→ 적을 눌러 대상을 고르세요";
        }

        string ForecastText(PracticeBattleQueryView view)
        {
            if (view.ForecastError != null) return ErrorText(view.ForecastError);
            var f = view.ToJObject()["forecast"];
            string head = (view.Mode == "now" ? "예상" : "현재 상태 기준 추정") + " · " + ModeName(view.Mode) + " · 행동력 " + Int(f?["cost"]);
            if (Bool(f?["hidden"])) return head + " · 결과는 사용한 뒤에 확인돼요";
            var parts = new List<string>();
            if ((Int(f?["enemyHpLoss"]) ?? 0) != 0) parts.Add("적 체력 -" + Int(f?["enemyHpLoss"]));
            if ((Int(f?["blockDelta"]) ?? 0) != 0) parts.Add("방어 " + Signed(Int(f?["blockDelta"]) ?? 0));
            if ((Int(f?["hpDelta"]) ?? 0) != 0) parts.Add("체력 " + Signed(Int(f?["hpDelta"]) ?? 0));
            if (Bool(f?["defeat"])) parts.Add("처치");
            return head + (parts.Count == 0 ? "" : " · " + string.Join(" · ", parts));
        }

        static string SlotText(string label, JObject? column) => column == null ? label + " 정보 없음"
            : label + " (" + Int(column["due"]) + "턴) · 사용 " + Int(column["used"]) + " / 칸 " + Int(column["cap"]) + (Bool(column["sealed"]) ? " · 봉인" : "");

        // The published move as-is (base values; the server does not publish a final damage forecast for intents).
        static string MoveText(JToken? move)
        {
            if (!(move is JObject m)) return "대기";
            string? label = Str(m["label"]);
            int? d = Int(m["d"]), h = Int(m["h"]), b = Int(m["b"]), n = Int(m["n"]), strength = Int(m["str"]);
            string Hit() => (label != null ? label + " " : "") + "공격 (기본 피해 " + d + (h > 1 ? " × " + h : "") + ")";
            switch (Str(m["t"]))
            {
                case "atk": return Hit();
                case "atkdebuff": return Hit() + " · 파티 약화 " + n;
                case "def": return "방어 " + b + (strength != null ? " · 힘 +" + strength : "");
                case "buff": return "힘 +" + strength;
                case "debuff": return "파티 약화 " + n;
                case "charge": return label ?? "힘을 모읍니다";
                case "summon": return label ?? "증원 소환";
                case "heal": return "아군 회복 " + n;
                case "insert": return "저주 카드 " + n + "장 추가";
                case "reflect": return "반사 " + n;
                case "expose": return (label ?? "약점 노출") + (m["then"] is JObject then ? " → " + MoveText(then) : "");
                case "blockAll": return "전원 방어 " + b;
                default: return label ?? "행동 준비";
            }
        }

        // Session/protocol codes become fixed Korean recovery notices; no code, JSON or exception text reaches the player.
        string Explain(string code)
        {
            switch (code)
            {
                case Codes.Busy: return "이전 요청을 처리하는 중이에요.";
                case Codes.Disposed: return "연결이 멈췄어요. 다시 돌아오면 새로고침해요.";
                case Codes.CredentialUnavailable: case "UNAUTHORIZED": case PracticeProtocolException.WrongAccount:
                    return "연습 계정 인증을 확인할 수 없어요. 새 행동은 막혀 있어요.";
                case Codes.NoSnapshot: case Codes.RefreshRequired: case Codes.ContextStale: case Codes.RefreshRaced:
                    return "화면이 최신이 아니에요. 새로고침해 주세요.";
                case Codes.RollbackRefused: return "서버 상태가 화면보다 이전이라 적용하지 않았어요. 새 행동은 막혀 있어요.";
                case Codes.PendingUnresolved: return "확인되지 않은 요청이 있어요. 먼저 '같은 요청 다시 보내기'로 확인해 주세요.";
                case Codes.NothingPending: return "다시 보낼 요청이 없어요.";
                case Codes.NoRun: case Codes.NoBattle: return "지금은 이 행동을 할 수 없어요.";
                case Codes.InvalidCommand: return "보낼 수 없는 요청이에요.";
                case Codes.Timeout: case Codes.NetworkFailed: case Codes.Canceled: case Codes.ResponseTooLarge: case Codes.ResponseTruncated:
                case PracticeProtocolException.Invalid: case PracticeProtocolException.Mismatch: case PracticeProtocolException.PrivateField:
                case "ACCOUNT_BUSY": case "STORE_UNAVAILABLE": case "STORE_COMMIT_UNKNOWN":
                    return "서버 응답을 확인하지 못했어요.";
            }
            if (code.StartsWith("OUTBOX_", StringComparison.Ordinal)) return "이 기기의 요청 기록을 확인할 수 없어요. 기록은 지우지 않았고 새 행동은 막혀 있어요.";
            if (code.StartsWith("STORE_", StringComparison.Ordinal) || code == "IDEMPOTENCY_CONFLICT" || code == "INTERNAL_ERROR")
                return "서버가 이 요청을 처리할 수 없다고 했어요. 요청 기록은 보관 중이고 새 행동은 막혀 있어요.";
            return ErrorText(code);
        }

        string ErrorText(string code)
        {
            if (errors.TryGetValue(code, out var text) && !text.Contains("내부")) return text;
            if (SaveErrors.TryGetValue(code, out var save)) return save;
            return "지금은 이 행동을 할 수 없어요.";
        }
        // Fixed texts for the save codes the shared error table does not name (display only; the verdicts are Core's).
        static readonly Dictionary<string, string> SaveErrors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["REASSIGNMENT_REQUIRES_SEPARATE_FLOW"] = "출발 전 기억에 있던 카드의 배정은 여기서 바꿀 수 없어요.",
            ["INVALID_SELECTION"] = "고른 변화가 서버의 변화 목록과 맞지 않아요. 새로고침한 뒤 다시 골라 주세요.",
            ["VERSION_CONFLICT"] = "저장된 기억이 그사이 바뀌었어요. 새로고침해서 최신 기억을 확인해 주세요.",
            ["INVALID_PART"] = "이 부품은 기억 저장에서 카드에 붙일 수 없어요."
        };

        // The server preview's refusal in words: its own error and numbers (nothing is recomputed here).
        string SaveReason(PracticeMemoryPreviewView view)
        {
            if (view.Error == "DEFEAT_KEEP_ONE")
                return "패배하면 이번 탐사의 변화는 파티 전체에서 1개까지만 더할 수 있어요(카드 한 장이 아니라 변화 하나). 지금 " + view.SelectedChangeIds.Count + "개를 골랐어요.";
            if (view.Error != "OVER_CAPACITY") return ErrorText(view.Error ?? "");
            var preview = view.ToJObject()["preview"] as JObject;
            var used = preview?["used"] as JObject;
            var capacities = preview?["capacities"] as JObject;
            var over = (used?.Properties() ?? Enumerable.Empty<JProperty>()).Where(p => Int(p.Value) > Int(capacities?[p.Name]))
                .Select(p => CharacterName(p.Name) + " " + Int(p.Value) + "/" + Int(capacities?[p.Name])).ToList();
            return "완성된 결과 덱 전체의 비용이 용량을 넘어요" + (over.Count == 0 ? "" : " (" + string.Join(", ", over) + ")")
                + ". 용량은 고른 변화의 합이 아니라 결과 덱 전체로 계산해요.";
        }

        void LoadNames()
        {
            if (namesLoaded) return;
            namesLoaded = true;
            try
            {
                var data = ClientRunContent.Load();
                foreach (var card in data.Memory.Battle.Cards) cardNames[card.Id] = card.Name;
                foreach (var character in data.Memory.Battle.Characters) characterNames[character.Id] = character.Name;
                foreach (var relic in data.Memory.Battle.Relics) { relicNames[relic.Id] = relic.Name; relicTexts[relic.Id] = relic.Text; }
                foreach (var part in data.Memory.Battle.Parts) { partNames[part.Id] = part.Name; partTexts[part.Id] = part.Text; partSlots[part.Id] = part.Slot; }
                foreach (var definition in data.Events) events[definition.Id] = definition; // the last with an id wins, as in Core
                foreach (var card in data.Memory.Battle.Cards)
                {
                    try { cardTexts[card.Id] = BattleText.Card(card, data.Memory.Battle); }
                    catch (Exception) { } // a card without readable text shows its name only
                }
            }
            catch (Exception) { } // display names only: without content every card shows a generic name
            var messages = Resources.Load<TextAsset>("MemoryForge/error-messages");
            if (messages == null) return;
            try
            {
                var map = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(messages.text);
                if (map != null) foreach (var pair in map) errors[pair.Key] = pair.Value;
            }
            catch (Exception) { }
        }

        string CardName(string? id) => id != null && cardNames.TryGetValue(id, out var name) ? name : "카드";
        string CharacterName(string? id) => id != null && characterNames.TryGetValue(id, out var name) ? name : "동료";
        string PartName(string id) => partNames.TryGetValue(id, out var name) ? name : "부품";
        string SlotName(string id) => !partSlots.TryGetValue(id, out var slot) ? "" : slot == "circuit" ? "시간 회로" : slot == "engraving" ? "보조 각인" : "임시";
        string ItemName(string kind, string id) => kind == "cards" ? CardName(id)
            : kind == "parts" ? PartName(id) + (SlotName(id).Length != 0 ? " · " + SlotName(id) : "") : relicNames.TryGetValue(id, out var name) ? name : "유물";
        string ItemText(string kind, string id) =>
            (kind == "cards" ? cardTexts : kind == "parts" ? partTexts : relicTexts).TryGetValue(id, out var text) ? text : "";
        // The original deck row: name, + when upgraded, attached parts, and whose card it is (public deck members only).
        string DeckCardText(JObject card)
        {
            var slots = card["mods"] as JObject;
            var mods = new[] { Str(slots?["c"]), Str(slots?["e"]) }.OfType<string>().Where(m => m.Length != 0).Select(PartName).ToList();
            string owner = Str(card["owner"]) ?? "";
            string whose = Truthy(card["weapon"]) ? CharacterName(Str(card["wielder"])) + " 무기" : owner == "common" ? "공용→" + CharacterName(Str(card["assigned"]))
                : owner == "curse" ? "저주" : CharacterName(owner);
            return CardName(Str(card["id"])) + (Truthy(card["plus"]) ? "+" : "") + (mods.Count == 0 ? "" : "[" + string.Join("·", mods) + "]") + " · " + whose;
        }
        // A saved-memory row (§8.9 spec: key, id, plus, from, assigned, mods) under its character: name, + when upgraded, attached parts
        // and a mark for a common card (only common rows carry assigned).
        string SpecText(JObject spec)
        {
            var slots = spec["mods"] as JObject;
            var mods = new[] { Str(slots?["c"]), Str(slots?["e"]) }.OfType<string>().Where(m => m.Length != 0).Select(PartName).ToList();
            return CardName(Str(spec["id"])) + (Truthy(spec["plus"]) ? "+" : "") + (mods.Count == 0 ? "" : "[" + string.Join("·", mods) + "]")
                + (Str(spec["assigned"]) != null ? " · 공용" : "");
        }
        static string KindName(string? kind)
        {
            switch (kind)
            {
                case "add": return "추가"; case "remove": return "제거"; case "evolve": return "진화"; case "upgrade": return "강화";
                case "part": return "부품"; default: return "변화";
            }
        }
        static string OutcomeName(string? outcome) =>
            outcome == "defeat" ? "패배" : outcome == "return" ? "기록소 귀환" : outcome == "clear" ? "탐사 완주" : "탐사 종료";
        string DeckTitle(DeckPick sheet) => sheet.Purpose == DeckPurpose.Upgrade ? "강화할 카드를 고르세요"
            : sheet.Purpose == DeckPurpose.ForgeOffer ? "변주할 카드를 고르세요" : sheet.Purpose == DeckPurpose.ForgeRemove ? "덱에서 뺄 카드를 고르세요"
            : sheet.Purpose == DeckPurpose.ShopRemove ? "제거할 카드를 고르세요 (동전 " + Int(Pending(State(), "shop")?["removeCost"]) + ")"
            : (OfferedChoice(sheet.EventIndex)?.Effects.CardCopy ?? 0) != 0 ? "복제할 카드를 고르세요"
            : "제거할 카드를 고르세요 (" + sheet.Count + "장)";
        static string NodeName(string type)
        {
            switch (type)
            {
                case "battle": return "전투"; case "elite": return "엘리트"; case "rest": return "휴식"; case "forge": return "공방";
                case "shop": return "상점"; case "event": return "사건"; case "boss": return "보스"; default: return "칸";
            }
        }
        static string ModeName(string mode) => mode == "now" ? "즉시" : mode == "next" ? "다음 턴" : "다다음 턴";
        static string ZoneName(string? zone) => zone == "hand" ? "손패" : zone == "discard" ? "버린 더미" : zone == "res" ? "예약" : "";
        static string PhaseName(string phase)
        {
            switch (phase)
            {
                case "REWARD": return "전투 보상"; case "REST": return "휴식"; case "FORGE": return "공방"; case "SHOP": return "상점";
                case "EVENT": return "사건"; case "ARCHIVE": return "기록소"; case "VICTORY": case "DEFEAT": return "기억 저장"; case "DONE": return "탐사 종료";
                default: return "다음 단계";
            }
        }
        static string Signed(int value) => (value > 0 ? "+" : "") + value;

        void Say(string text, bool error) { notice = text; noticeError = error; }
        Task<bool> Refuse(string text) { Say(text, true); Render(); return Task.FromResult(false); }

        // Fire-and-forget from a button or Update, with every failure caught into a fixed notice (no unhandled async void).
        void Launch(Func<Task<bool>> action) { _ = Guard(action); }
        async Task Guard(Func<Task<bool>> action)
        {
            try { await action(); }
            catch (Exception) { Say("문제가 생겼어요. 새로고침한 뒤 다시 시도해 주세요.", true); Render(); }
        }

        static string? Str(JToken? token) => token != null && token.Type == JTokenType.String ? (string?)token : null;
        static int? Int(JToken? token) => token != null && token.Type == JTokenType.Integer ? (int?)(long)token : null;
        static bool Bool(JToken? token) => token != null && token.Type == JTokenType.Boolean && (bool)token;
        static bool? BoolOrNull(JToken? token) => token != null && token.Type == JTokenType.Boolean ? (bool?)(bool)token : null;
        // JS truthiness of a public scalar, as Core reads plus/weapon/fromEvent (MemoryJson.Truthy).
        static bool Truthy(JToken? token) => token != null && (token.Type == JTokenType.Boolean ? (bool)token
            : token.Type == JTokenType.Integer ? (long)token != 0 : token.Type == JTokenType.String && ((string?)token)?.Length > 0);

        // ───────── uGUI helpers (the same construction and palette as MemoryForgeExpeditionClient) ─────────

        static void Center(RectTransform r) { r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = Vector2.zero; }
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
            canvasRoot = null; frame = null; presentationText = null; continueButton = null; saveContent = null; detailsContent = null;
            if (previous == null) return;
            previous.gameObject.SetActive(false);
            Destroy(previous.gameObject);
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
            text.resizeTextForBestFit = true; text.resizeTextMinSize = Math.Max(12, (int)(size * 0.7f)); text.resizeTextMaxSize = size; return text;
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
        // The original's scroll construction: a masked viewport (RectMask2D) with a ScrollRect over a top-left anchored content box.
        RectTransform Scroll(Transform parent, string name, float x, float y, float width, float height, float contentWidth, float contentHeight, Color color)
        {
            var viewport = Box(parent, name, x, y, width, height, color); viewport.GetComponent<Image>().raycastTarget = true; viewport.gameObject.AddComponent<RectMask2D>();
            var content = Box(viewport, name + "Content", 0, 0, Math.Max(width, contentWidth), Math.Max(height, contentHeight), color);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = contentWidth > width; scroll.vertical = contentHeight > height; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 25;
            return content;
        }
        void Bar(Transform parent, float x, float y, float width, float height, int value, int max, Color color)
        { var back = Box(parent, "Bar", x, y, width, height, Line); Box(back, "Fill", 0, 0, width * Mathf.Clamp01(max > 0 ? (float)value / max : 0), height, color); }
        static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
    }
}
