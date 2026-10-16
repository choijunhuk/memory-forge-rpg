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
    // commands (create, enter, play, end) and pure battle queries; it never creates or hydrates a Run/BattleState, seeds anything or
    // computes a cost, slot, target, legality or damage. Costs, modes, blocked[mode] (the DEFAULT request only), forecasts and pick sheets
    // are the server's values; card/character names come from the existing content for display only. It does not replace the original
    // local expedition client, and rewards/services/memory screens are a later unit (shown as such).
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
    //   asked in. Local prototype timing (not a measurement): rows 0.2 s, a reservation resolution row the original 0.3 s, 0.6 s hold.
    public sealed class MemoryForgeServerPracticeClient : MonoBehaviour
    {
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

        Func<PracticeServerSession>? factory;
        PracticeServerSession? session;
        PracticeSnapshot? lastGood;
        bool busy, queryWanted, refreshWanted;
        int generation, queryGeneration = -1;
        PracticeBattleQueryResult? queryResult;
        string? selectedCard, selectedMode, target;
        PickSheet? picker;
        Prepared? prepared;
        IReadOnlyList<BattleDigestEntry>? rows;
        bool barrier;
        int shown;
        float nextAt = -1, closeAt = -1;
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
        /// <summary>Whether a new command may be sent now (session CanMutate, nothing local open, no presentation, no operation).</summary>
        public bool CanCommand => !busy && session != null && session.State.CanMutate && rows == null && picker == null && prepared == null;

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
            selectedCard = null; selectedMode = null; target = null; picker = null; prepared = null; rows = null;
            queryResult = null; queryWanted = false; refreshWanted = false; generation++;
        }

        void Update()
        {
            FitView();
            float now = Time.realtimeSinceStartup;
            if (rows != null) Advance(now);
            else if (prepared != null && !busy) DispatchPrepared();
            if (refreshWanted && !busy && session != null) { refreshWanted = false; Launch(RefreshAsync); }
            if (queryWanted && !busy && session != null && !PresentationBarrier) Launch(RunQueryAsync);
        }

        // ───────── operations (each button calls one of these) ─────────

        public Task<bool> RefreshAsync() => Operate(async s =>
        {
            var result = await s.RefreshAsync();
            if (!ReferenceEquals(s, session)) return false;
            AfterSnapshotChange();
            if (result.Snapshot == null) { Say(Explain(result.Code) + " 마지막으로 받은 화면을 보여 주고 있어요.", true); return false; }
            Say("서버의 최신 상태를 불러왔어요.", false);
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
            // A prepared play still waiting (e.g. after skip, before its Update dispatch) is never overtaken by an end.
            if (rows != null || picker != null || prepared != null) return Refuse("지금은 턴을 마칠 수 없어요.");
            if (!CanCommand) return Refuse(NotReady());
            return Mutate((s, context) => s.CommandAsync(context, "end", null), true, true, "턴을 마쳤어요.");
        }

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
        /// <summary>proceed {}: an explicit command; Core decides whether it is allowed now.</summary>
        public Task<bool> ProceedAsync() => RewardAction(p => true, "proceed", null, "다음으로 나아가요.");
        /// <summary>True while the session's current snapshot shows the public pending battle reward.</summary>
        public bool ShowingReward => Pending(session?.State.Snapshot?.Run?.StateJObject(), "reward") != null;

        // Gate first (no request while busy, stale, pending, disconnected, during a barrier/sheet/prepared play), then the published
        // choice from the SESSION's current snapshot, then exactly one existing command through Mutate and the outbox.
        Task<bool> RewardAction(Func<JObject, bool> offered, string kind, JObject? args, string success)
        {
            if (!CanCommand) return Refuse(rows != null || picker != null || prepared != null ? "지금은 할 수 없어요." : NotReady());
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
            if (selectedCard != null && Hand(selectedCard) == null) { selectedCard = null; selectedMode = null; }
            if (target != null && !Enemies().Any(e => Str(e["uid"]) == target && Bool(e["alive"]))) target = null;
            generation++; queryResult = null;
            queryWanted = selectedCard != null || picker != null;
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
        }

        void Advance(float now)
        {
            if (rows == null) return;
            if (shown < rows.Count)
            {
                if (now < nextAt) return;
                shown++;
                nextAt = now + (shown < rows.Count && rows[shown].IsReservedResolution ? ReservedRowSeconds : RowSeconds);
                UpdatePresentation();
                return;
            }
            if (barrier) return;
            if (closeAt < 0) closeAt = now + HoldSeconds;
            else if (now >= closeAt) ClosePresentation();
        }

        void UpdatePresentation()
        {
            if (rows == null) return;
            if (presentationText != null) presentationText.text = string.Join("\n", rows.Take(shown).Select(r => r.Text).Reverse().Take(barrier ? 14 : 3).Reverse());
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
        HashSet<string> Available() => new HashSet<string>(((State()?["available"] as JArray) ?? new JArray()).Select(Str).OfType<string>(), StringComparer.Ordinal);
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
            RemoveView();
            var canvasObject = new GameObject(OwnedCanvas, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
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
                else LaterView(run.Phase);
                Label(frame, notice, 32, 846, 1536, 44, 21, noticeError ? Danger : Memory);
                if (picker != null) PickView();
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
            Label(panel, "이 단계(휴식·공방·상점·사건·기록소·기억 저장)는 다음 작업 단위에서 서버 화면으로 연결돼요. 원래 로컬 탐사 화면은 그대로 있어요.", 24, 140, 1488, 80, 22, Muted);
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
            Label(party, "파티 체력  " + Int(b["hp"]) + " / " + Int(b["maxHp"]), 18, 10, 344, 34, 26, Ink, FontStyle.Bold);
            Bar(party, 18, 50, 344, 10, Int(b["hp"]) ?? 0, Int(b["maxHp"]) ?? 0, Memory);
            Label(party, "방어 " + Int(b["block"]) + "   ·   " + Int(b["turn"]) + "턴", 18, 72, 344, 30, 22, Ink);
            Label(party, "행동력  " + Int(b["ap"]), 18, 108, 344, 44, 36, Brass, FontStyle.Bold);
            var piles = b["piles"] as JObject;
            Label(party, "뽑을 더미 " + Int(piles?["draw"]) + " · 손패 " + Int(piles?["hand"]) + " · 버린 더미 " + Int(piles?["discard"]) + " · 소멸 " + Int(piles?["exhaust"]),
                18, 162, 344, 56, 19, Muted);
            var actors = ((b["actors"] as JArray) ?? new JArray()).OfType<JObject>().Select(a => Str(a["name"]) ?? CharacterName(Str(a["id"])));
            Label(party, string.Join(" · ", actors), 18, 222, 344, 34, 19, Muted);

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

            // Time reservations: the server's cap/used/sealed/due per column and every public reservation.
            var slots = b["slots"] as JObject;
            var board = Box(frame!, "Reservations", 32, 370, 1000, 112, Panel);
            Label(board, "기억 시간표", 16, 8, 200, 30, 22, Memory, FontStyle.Bold);
            Label(board, SlotText("다음 턴", slots?["next"] as JObject) + "\n" + SlotText("다다음 턴", slots?["after"] as JObject), 16, 40, 420, 64, 18, Ink);
            var reserved = ((b["reservations"] as JArray) ?? new JArray()).OfType<JObject>()
                .Select(r => CardName(Str(r["id"])) + " (" + Int(r["due"]) + "턴)").ToList();
            Label(board, reserved.Count == 0 ? "예약된 카드 없음" : string.Join(" · ", reserved), 450, 10, 534, 94, 18, reserved.Count == 0 ? Muted : Ink);

            // Ordinary action presentation strip (text only; the next selection stays open)
            var strip = Box(frame!, "Presentation", 1048, 370, 520, 112, Panel);
            presentationText = Label(strip, "", 14, 8, 380, 96, 17, Memory);
            if (rows != null && !barrier)
            {
                Button(strip, "SkipPresentation", "건너뛰기", 404, 30, 104, 52, SkipPresentation, Line, 19);
                UpdatePresentation();
            }
            else presentationText.text = prepared != null ? "사용 대기: " + CardName(Str(Hand(prepared.Uid)?["id"])) : "";

            HandView(b);
            ActionView();
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
            }
            else
            {
                var view = QueryCurrent() ? queryResult!.View : null;
                var source = view?.ToJObject()["source"];
                Label(panel, (Str(source?["name"]) ?? CardName(Str(card["id"]))) + "  " + TargetText(), 18, 10, 760, 36, 25, Ink, FontStyle.Bold);
                Label(panel, Str(source?["text"]) ?? (busy || queryWanted ? "카드 설명을 불러오는 중…" : ""), 18, 48, 760, 64, 18, Muted);
                Label(panel, view == null ? "예상을 확인하는 중…" : ForecastText(view), 18, 116, 760, 44, 19, view?.ForecastError != null ? Danger : Memory);
                var modes = (card["modes"] as JArray)?.Select(Str).OfType<string>().ToList() ?? new List<string>();
                for (int i = 0; i < modes.Count; i++)
                {
                    string mode = modes[i];
                    // blocked[mode] is Core's check of the DEFAULT request only (auto target, no manual picks): shown, never enforced here.
                    string? blocked = Str((card["blocked"] as JObject)?[mode]);
                    string caption = ModeName(mode) + " · 행동력 " + (Int((card["costs"] as JObject)?[mode])?.ToString() ?? "-") + (blocked != null ? "\n기본 요청: " + ErrorText(blocked) : "");
                    var button = Button(panel, "Mode:" + mode, caption, 790 + i * 132, 14, 124, 92, () => SelectMode(mode), mode == selectedMode ? Brass : Line, 16);
                    button.interactable = !SelectionLocked();
                }
                if (modes.Count == 0) Label(panel, ErrorText(Str(card["error"]) ?? ""), 790, 20, 380, 60, 18, Danger);
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
            Label(p, "턴 결과", 32, 16, 900, 46, 32, Brass, FontStyle.Bold);
            presentationText = Label(p, "", 32, 76, 1016, 520, 21, Ink);
            Button(p, "ResultSkip", "모두 보기", 32, 620, 300, 76, SkipPresentation, Line, 24);
            continueButton = Button(p, "ResultContinue", "계속", 748, 620, 300, 76, ContinuePresentation, Brass, 24);
            UpdatePresentation();
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
            return "지금은 이 행동을 할 수 없어요.";
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
                foreach (var part in data.Memory.Battle.Parts) { partNames[part.Id] = part.Name; partTexts[part.Id] = part.Text; }
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
            canvasRoot = null; frame = null; presentationText = null; continueButton = null;
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
