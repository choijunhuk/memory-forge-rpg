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
    // commands (create, enter, play, end, the reward and node-service kinds, proceed) and pure battle queries; it never creates or
    // hydrates a Run/BattleState, seeds anything or computes a cost, slot, target, legality or damage. Costs, modes, blocked[mode] (the
    // DEFAULT request only), forecasts and pick sheets
    // are the server's values; card/character names come from the existing content for display only. It does not replace the original
    // local expedition client. The battle reward, the rest/forge/shop node services, events and the archive have their own screens (one
    // existing command per button); the memory-save screens are a later unit (shown as such).
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
                else if (run.Phase == "REST" && Pending(State(), "rest") != null) RestView();
                else if (run.Phase == "FORGE" && Pending(State(), "forge") != null) ForgeView();
                else if (run.Phase == "SHOP" && Pending(State(), "shop") != null) ShopView();
                else if (run.Phase == "EVENT" && Pending(State(), "event") != null) EventView();
                else if (run.Phase == "ARCHIVE" && Pending(State(), "archive") != null) ArchiveView();
                else LaterView(run.Phase);
                Label(frame, notice, 32, 846, 1536, 44, 21, noticeError ? Danger : Memory);
                if (picker != null) PickView();
                if (deckPick != null) DeckPickView();
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
            Label(panel, "이 단계(기억 저장)는 다음 작업 단위에서 서버 화면으로 연결돼요. 원래 로컬 탐사 화면은 그대로 있어요.", 24, 140, 1488, 80, 22, Muted);
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
        // follows are Core's (the save screens remain a later unit).
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
            Label(panel, "귀환하면 기억 저장 단계로 가요. 기억 저장 화면은 다음 작업 단위에서 서버 화면으로 연결돼요.", 20, 316, 1496, 60, 20, Muted);
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
