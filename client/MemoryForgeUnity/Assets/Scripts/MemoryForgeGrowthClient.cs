#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MemoryForge.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    public sealed partial class MemoryForgeGrowthClient : MonoBehaviour
    {
        public const string SceneName = "MemoryForgeGrowth", OwnedCanvas = "MemoryForgeGrowthCanvas";
        public Action<string>? SceneLoader { get; set; }
        readonly Dictionary<string, Sprite?> sprites = new Dictionary<string, Sprite?>();
        readonly List<string> picks = new List<string>();
        static readonly Color Ink = Hex("e9eef8"), Muted = Hex("a9b5ca"), Brass = Hex("e2b45f"), Teal = Hex("62d6c6"), Panel = new Color(0.045f, 0.065f, 0.12f, 0.94f);
        PracticeServerSession? session;
        ClientBattleContent? content;
        Font font = null!;
        RectTransform? canvas, frame;
        PracticeEncounterBattleQueryView? query;
        string? selected, target;
        string? lastEncounterId;
        MemoryForgeAccountConnection? connectionOwner;
        string mode = "now", notice = "";
        bool busy, queryBusy;
        public bool Busy => busy || queryBusy || supportQueryBusy;
        public PracticeEncounterSnapshot? Snapshot => session?.Encounter;
        int selection;
        Vector3 layoutCalibration;
        float BattleTop => 32 + Math.Max(160, TouchUnits() + 16);
        float HandHeight => Math.Max(212, TouchUnits() * 1.4f + 16);
        float HandTop => 884 - HandHeight;
        float ActionHeight => Math.Max(112, TouchUnits());
        float ActionTop => HandTop - 16 - ActionHeight;

        void OnEnable()
        {
            font = Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular");
            try { content = ClientBattleContent.Load(); }
            catch (System.IO.InvalidDataException) { notice = "전투 자료를 불러오지 못했어요. 공방에서 다시 확인해주세요."; Render(); return; }
            Open();
        }

        void OnDisable() { session?.Dispose(); session = null; busy = queryBusy = false; selected = target = null; picks.Clear(); query = null; selection++; DropGrowthSupport(); }
        void OnDestroy() { foreach (var sprite in sprites.Values) if (sprite != null) Destroy(sprite); }
        void OnApplicationQuit() => MemoryForgeAccountConnection.SetCurrent(null);
        void OnApplicationPause(bool paused) { if (paused) { session?.Dispose(); session = null; busy = queryBusy = false; DropSelection(); } else if (isActiveAndEnabled) Open(); }
        void OnApplicationFocus(bool focused) { if (focused) MemoryForgeUiViewport.RefreshMetrics(); if (focused && isActiveAndEnabled && !Busy) { if (session == null) Open(); else _ = Refresh(); } }
        void Update() { if (canvas != null && layoutCalibration != MemoryForgeUiViewport.Calibration) Render(); Fit(); if (Input.GetKeyDown(KeyCode.Escape) && !busy) { if (supportOpen) CloseGrowthSupport(); else Home(); } }

        void Open()
        {
            var owner = MemoryForgeAccountConnection.Current;
            if (owner?.IsAuthenticated != true) { notice = "공방에서 계정을 연결해주세요."; Render(); return; }
            if (!ReferenceEquals(connectionOwner, owner)) { connectionOwner = owner; lastEncounterId = null; }
            try { session?.Dispose(); busy = queryBusy = false; session = owner.CreateSession(); _ = Refresh(); }
            catch (Exception) { notice = "계정 연결을 다시 확인해주세요."; Render(); }
        }

        async Task Refresh()
        {
            if (Busy || session == null) return;
            var captured = session; busy = true; DropSelection(); Render();
            try
            {
                var profile = await captured.RefreshProgressionAsync();
                if (!Current(captured)) return;
                if (profile.Snapshot == null || captured.ProgressionNeedsRefresh)
                { notice = "계정 진행을 확인하지 못했어요. 새로고침해주세요. 보낸 요청은 보존되어 있어요."; return; }
                var active = profile.Snapshot?.ActiveEncounter;
                if (active != null)
                {
                    var read = await captured.RefreshEncounterAsync(active.EncounterId);
                    if (!Current(captured)) return;
                    if (read.Snapshot != null) lastEncounterId = read.Snapshot.EncounterId;
                    notice = read.Code == PracticeSessionCodes.Ok ? "" : "전투 상태를 새로 확인해주세요.";
                }
                else if (captured.Encounter != null || lastEncounterId != null)
                {
                    var read = await captured.RefreshEncounterAsync(captured.Encounter?.EncounterId ?? lastEncounterId!);
                    if (!Current(captured)) return;
                    notice = read.Code == PracticeSessionCodes.Ok ? "" : "종료된 전투의 결과를 다시 확인해주세요.";
                }
                else notice = "진행 중인 훈련이 없어요. 공방의 육성에서 선택해주세요.";
            }
            catch (Exception) { if (Current(captured)) notice = "연결을 확인하고 새로고침해주세요. 보낸 요청은 보존됩니다."; }
            finally { if (Current(captured)) { busy = false; Render(); } }
        }

        bool Current(PracticeServerSession captured) => this != null && isActiveAndEnabled && ReferenceEquals(captured, session);
        void DropSelection() { selected = null; target = null; mode = "now"; picks.Clear(); query = null; queryBusy = false; selection++; DropGrowthSupport(); }
        bool SupportInputReady => !Busy && session?.CanMutateEncounter == true;
        bool CanAct => SupportInputReady && !supportOpen;
        void Home()
        {
            if (Busy) return;
            try { (SceneLoader ?? (destination => SceneManager.LoadScene(destination)))(MemoryForgeExpeditionClient.LobbyScene); }
            catch (Exception) { notice = "공방으로 이동하지 못했어요. 다시 선택해주세요."; Render(); }
        }

        async Task Send(string kind, JObject args, string? ultimate = null)
        {
            var captured = session; var shown = captured?.Encounter;
            if (!CanAct || captured == null || shown == null) return;
            string? showUltimate = null;
            busy = true; query = null; selection++; Render();
            lastEncounterId = shown.EncounterId;
            try
            {
                var result = await captured.EncounterCommandAsync(shown.Context, kind, args);
                if (!Current(captured)) return;
                DropSelection();
                notice = MutationNotice(result.Status, result.Code, result.RefreshCode, result.OutboxCode);
                if (ultimate != null && result.Status == PracticeMutationStatus.Committed && result.RefreshCode == null && result.OutboxCode == null
                    && result.Snapshot?.Version == shown.Version + 1) showUltimate = ultimate;
            }
            catch (Exception) { if (Current(captured)) notice = "응답을 확인하지 못했어요. 보존된 같은 요청을 확인해주세요."; }
            finally { if (Current(captured)) { busy = false; Render(); } }
            if (Current(captured) && showUltimate != null) StartCoroutine(UltimateCutin(showUltimate));
        }

        async Task Retry()
        {
            var captured = session; if (busy || captured == null) return;
            busy = true; DropSelection(); Render();
            try
            {
                var result = await captured.RetryGrowthPendingAsync();
                if (Current(captured)) notice = MutationNotice(result.Status, result.Code, result.RefreshCode, result.OutboxCode);
            }
            catch (Exception) { if (Current(captured)) notice = "요청은 보존되어 있어요. 연결을 확인한 뒤 다시 결과를 확인해주세요."; }
            finally { if (Current(captured)) { busy = false; Render(); } }
        }

        void SelectCard(string uid)
        {
            if (!CanAct) return;
            DropGrowthSupport();
            selected = uid; picks.Clear(); query = null;
            var card = session!.Encounter!.StateJObject()["hand"]!.FirstOrDefault(item => Text(item["uid"]) == uid);
            mode = card?["modes"]?.FirstOrDefault()?.Value<string>() ?? "now";
            selection++; _ = Query();
        }

        async Task Query()
        {
            var captured = session; var snapshot = captured?.Encounter; string? uid = selected;
            if (captured == null || snapshot == null || uid == null || Busy) return;
            int asked = ++selection; query = null; queryBusy = true; Render();
            try
            {
                var answer = await captured.EncounterBattleQueryAsync(snapshot.Context, uid, mode, target, picks.ToArray());
                if (Current(captured) && asked == selection && selected == uid && captured.Encounter?.Version == snapshot.Version)
                { query = answer.View; notice = query == null ? "카드 미리보기를 다시 확인해주세요." : ""; }
            }
            catch (Exception) { if (Current(captured) && asked == selection) notice = "카드 미리보기를 받지 못했어요."; }
            if (Current(captured) && asked == selection) { queryBusy = false; Render(); }
        }

        void Render()
        {
            layoutCalibration = MemoryForgeUiViewport.Calibration;
            if (canvas != null) { canvas.gameObject.SetActive(false); Destroy(canvas.gameObject); }
            var host = new GameObject(OwnedCanvas, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); host.transform.SetParent(transform, false);
            host.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = host.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
            canvas = (RectTransform)host.transform;
            if (EventSystem.current == null) new GameObject("GrowthInput", typeof(EventSystem), typeof(StandaloneInputModule)).transform.SetParent(transform, false);
            var backdrop = Box(canvas, "BattleBackdrop", 0, 0, 1600, 900, Color.white); backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one; backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
            backdrop.GetComponent<Image>().sprite = SpriteAt("Backgrounds/bg_zone1_battle"); MemoryForgeUiViewport.Cover(backdrop.GetComponent<Image>());
            frame = Box(canvas, "GrowthFrame", 0, 0, 1600, 900, Color.clear); frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f); frame.anchoredPosition = Vector2.zero; frame.sizeDelta = new Vector2(1600, 900); Fit();
            Box(frame, "Shade", 0, 0, 1600, 900, new Color(0.02f, 0.035f, 0.065f, 0.32f));
            var top = Box(frame, "Header", 32, 16, 1536, Math.Max(160, TouchUnits() + 16), Panel);
            Button(top, "GrowthHome", "공방", 12, 8, 144, 80, Home).interactable = !Busy;
            var headerBody = Scroll(top, "GrowthHeading", 184, 8, session?.Encounter?.Support != null ? 480 : 980, top.rect.height - 16, session?.Encounter?.Support != null ? 480 : 980);
            var headerText = Label(headerBody, (session?.Encounter?.Kind == "weeklyBoss" ? "주간 보스 도전" : "동료 훈련") + (session?.Encounter == null ? "" : " · " + Number(session.Encounter.StateJObject()["turn"]) + "턴")
                + "\n" + (session?.Encounter == null ? "" : "메아리 " + Number(session.Encounter.StateJObject()["echo"]) + "  ·  ") + (Busy ? "서버 확인 중…" : notice), 0, 0, session?.Encounter?.Support != null ? 456 : 956, 1, 28, Brass);
            headerBody.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(top.rect.height - 16, headerText.rectTransform.rect.height));
            var headerScroll = headerBody.parent.GetComponent<ScrollRect>(); headerScroll.horizontal = false; headerScroll.vertical = true;
            GrowthSupportEntry(top);
            Button(top, "GrowthRefresh", "새로고침", 1188, 8, 336, 80, () => _ = Refresh()).interactable = !Busy;
            if (session?.State.Pending != null)
            {
                var pending = Box(frame, "GrowthPending", 260, 264, 1080, Math.Max(280, 192 + TouchUnits()), Panel);
                Label(pending, "미확정 요청이 보존되어 있어요", 40, 32, 1000, 64, 32, Brass);
                Label(pending, "같은 요청을 확인하면 저장된 결과를 다시 받을 수 있어요.", 40, 108, 1000, 48, 24, Ink);
                Button(pending, "GrowthRetry", "같은 요청 확인", 40, 176, 1000, 88, () => _ = Retry()).interactable = !busy;
                DecoratePanels(); Fit(); return;
            }
            var snapshot = session?.Encounter;
            if (snapshot == null) { Label(frame, notice.Length == 0 ? "전투 상태를 불러오는 중이에요." : notice, 120, 300, 1360, 120, 32, Ink); Fit(); return; }
            var state = snapshot.StateJObject();
            if (snapshot.Settlement != null) Terminal(snapshot.Settlement);
            else BattleView(state);
            if (supportOpen && snapshot.Settlement == null) GrowthSupportSheet(state);
            DecoratePanels(); Fit();
        }

        void BattleView(JObject state)
        {
            float battleY = BattleTop, battleHeight = Math.Max(TouchUnits(), ActionTop - BattleTop - 16);
            var party = Box(frame!, "GrowthParty", 32, battleY, 400, battleHeight, Panel);
            var partyBody = Scroll(party, "GrowthStats", 0, 0, 400, battleHeight, 400);
            var hpText = Label(partyBody, "체력  " + Number(state["hp"]) + " / " + Number(state["maxHp"]), 20, 12, 360, 1, 24, Ink);
            float partyY = 12 + hpText.rectTransform.rect.height + 12;
            Bar(partyBody, 20, partyY, 360, 12, Number(state["hp"]), Number(state["maxHp"]), Teal); partyY += 24;
            var statsText = Label(partyBody, "방어 " + Number(state["block"]) + " · 행동력 " + Number(state["ap"]), 20, partyY, 360, 1, 20, Muted);
            partyY += statsText.rectTransform.rect.height + 16;
            var ultimateTitle = Label(partyBody, "필살기", 20, partyY, 360, 1, 20, Brass);
            partyY += ultimateTitle.rectTransform.rect.height + 12;
            var actors = state["actors"] as JArray ?? new JArray();
            float ultimateHeight = Math.Max(152, TouchUnits());
            var ultimateStrip = Scroll(partyBody, "GrowthUltimates", 20, partyY, 360, ultimateHeight, Math.Max(360, actors.Count * 372 - 12));
            int n = 0;
            foreach (var actor in actors)
            {
                string id = Text(actor["id"]); int gauge = Number(actor["gauge"]), max = Number(state["gaugeMax"]);
                var ult = Button(ultimateStrip, "GrowthUlt:" + id, "", n++ * 372, 0, 360, ultimateHeight,
                    () => { var args = new JObject { ["actor"] = id }; if (target != null) args["target"] = target; _ = Send("ult", args, id); });
                ult.interactable = CanAct && gauge >= max && max > 0;
                var caption = Label(ult.transform, CharacterName(id) + "\n" + gauge + "/" + max, 24, 16, 312, ultimateHeight - 24, 21, gauge >= max && max > 0 ? Brass : Ink);
                caption.lineSpacing = 0.85f; MemoryForgeUiViewport.WholeText(caption);
                float captionHeight = caption.rectTransform.rect.height;
                caption.rectTransform.anchorMin = new Vector2(caption.rectTransform.anchorMin.x, 1);
                caption.rectTransform.anchorMax = new Vector2(caption.rectTransform.anchorMax.x, 1);
                caption.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, captionHeight);
                ultimateHeight = Math.Max(ultimateHeight, captionHeight + 24);
            }
            ultimateStrip.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ultimateHeight);
            ((RectTransform)ultimateStrip.parent).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ultimateHeight);
            foreach (RectTransform ultimate in ultimateStrip) ultimate.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ultimateHeight);
            partyBody.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(battleHeight, partyY + ultimateHeight + 16));
            var partyScroll = partyBody.parent.GetComponent<ScrollRect>(); partyScroll.horizontal = false; partyScroll.vertical = true;
            var enemies = (state["enemies"] as JArray ?? new JArray()).ToList();
            float width = Math.Max(360, (1120f - 12 * Math.Max(0, enemies.Count - 1)) / Math.Max(1, enemies.Count));
            var enemyStrip = Scroll(frame!, "GrowthEnemies", 448, battleY, 1120, battleHeight, Math.Max(1120, enemies.Count * (width + 12) - 12));
            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i]; string uid = Text(enemy["uid"]); bool alive = enemy["alive"]?.Value<bool>() == true;
                var tile = Button(enemyStrip, "GrowthEnemy:" + uid, "", i * (width + 12), 0, width, battleHeight,
                    () => { if (!CanAct) return; target = uid; picks.Clear(); selection++; _ = Query(); });
                if (enemies.Count <= 3) { var tileRect = (RectTransform)tile.transform; tileRect.anchorMin = new Vector2((float)i / enemies.Count, 1); tileRect.anchorMax = new Vector2((float)(i + 1) / enemies.Count, 1); tileRect.sizeDelta = new Vector2(-12, battleHeight); tileRect.anchoredPosition = Vector2.zero; }
                tile.interactable = CanAct && alive; tile.GetComponent<Image>().color = target == uid ? new Color(0.1f, 0.25f, 0.29f, 0.7f) : new Color(0.02f, 0.04f, 0.07f, 0.5f);
                var figure = Box(tile.transform, "EnemyArt", 40, 20, width - 80, battleHeight - 152, alive ? Color.white : new Color(1, 1, 1, 0.3f));
                figure.GetComponent<Image>().sprite = SpriteAt("Enemies/enemy_" + Text(enemy["id"])); figure.GetComponent<Image>().preserveAspect = true;
                if (figure.GetComponent<Image>().sprite == null) figure.GetComponent<Image>().color = Color.clear;
                int loss = query?.EnemyHpLosses.FirstOrDefault(item => item.Uid == uid).HpLoss ?? 0;
                var enemyBody = Scroll(tile.transform, "GrowthEnemyInfo", 12, 8, width - 24, battleHeight - 16, width - 24);
                var enemyText = Label(enemyBody, Text(enemy["name"]) + "\n" + Number(enemy["hp"]) + " / " + Number(enemy["maxHp"]) + (loss > 0 ? " → " + Math.Max(0, Number(enemy["hp"]) - loss) : "")
                    + "\n이번  " + MemoryForgeServerPracticeClient.MoveText(enemy["intent"]), 8, 4, width - 40, 1, 28, Ink);
                float enemyY = enemyText.rectTransform.rect.height + 16;
                Bar(enemyBody, 8, enemyY, width - 40, 12, Number(enemy["hp"]), Number(enemy["maxHp"]), Teal);
                enemyBody.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(battleHeight - 16, enemyY + 28));
                var enemyScroll = enemyBody.parent.GetComponent<ScrollRect>(); enemyScroll.horizontal = false; enemyScroll.vertical = true;

            }
            Button(frame!, "GrowthEndTurn", "턴 마치기", 32, ActionTop, 400, ActionHeight, () => _ = Send("end", new JObject())).interactable = CanAct && Text(state["phase"]) == "PLAYER";
            SelectionView(state);
            HandView(state);
        }

        void SelectionView(JObject state)
        {
            if (selected == null) { Label(frame!, "카드를 고른 뒤 대상을 선택하세요.", 448, ActionTop, 1120, ActionHeight, 27, Ink); return; }
            var answer = query?.ToJObject();
            var source = answer?["source"];
            var card = state["hand"]?.FirstOrDefault(item => Text(item["uid"]) == selected);
            var modes = card?["modes"] as JArray ?? new JArray();
            bool onlyNow = modes.Count == 1 && Text(modes[0]) == "now";
            var previewBody = Scroll(frame!, "GrowthSelection", 448, ActionTop, 720, ActionHeight, onlyNow ? 720 : Math.Max(720, modes.Count * 240));
            if (onlyNow)
            {
                var description = Label(previewBody, source == null ? "카드 미리보기 확인 중…" : Text(source["name"]) + "\n" + Text(source["text"]), 12, 4, 696, 1, 24, Brass);
                previewBody.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(112, description.rectTransform.rect.height + 8));
                var previewScroll = previewBody.parent.GetComponent<ScrollRect>(); previewScroll.horizontal = false; previewScroll.vertical = true;
            }
            for (int i = 0; !onlyNow && i < modes.Count; i++)
            {
                string choice = Text(modes[i]);
                Button(previewBody, "GrowthMode:" + choice, choice == "now" ? "지금" : choice == "next" ? "다음" : "다다음", i * 240, 0, 224, Math.Max(112, TouchUnits()),
                    () => { mode = choice; picks.Clear(); selection++; _ = Query(); }, choice == mode ? Brass : Panel).interactable = CanAct;
            }
            var choices = answer?["choices"] as JObject;
            var candidates = choices?["candidates"] as JArray;
            int index = Number(choices?["index"]);
            if (candidates != null && candidates.Count > 0 && index < (choices?["requests"] as JArray)?.Count)
            {
                var sheet = Box(frame!, "GrowthPickSheet", 404, 144, 1164, 360, Panel);
                Label(sheet, Text(choices!["requests"]![index]!["label"]), 24, 16, 1116, 56, 28, Brass);
                var area = Scroll(sheet, "Candidates", 24, 88, 1116, 244, Math.Max(1116, candidates.Count * 220));
                for (int i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i]; string uid = Text(candidate["uid"]);
                    string name = CardName(Text(candidate["id"]));
                    Button(area, "GrowthPick:" + uid, name, i * 220, 8, 208, 212,
                        () => { picks.Add(uid); selection++; _ = Query(); }).interactable = CanAct;
                }
            }
            bool legal = answer != null && answer["forecast"]?["error"]?.Type == JTokenType.Null
                && choices?["error"]?.Type == JTokenType.Null && choices?["blocked"]?.Type == JTokenType.Null
                && Number(choices?["index"]) >= (choices?["requests"] as JArray)?.Count;
            Button(frame!, "GrowthPlay", legal ? "카드 사용" : "선택 확인", 1200, ActionTop, 368, ActionHeight, () =>
            {
                if (selected == null || query == null) return;
                var args = new JObject { ["uid"] = selected, ["mode"] = mode };
                if (target != null) args["target"] = target; if (picks.Count > 0) args["picks"] = new JArray(picks);
                _ = Send("play", args);
            }, Brass).interactable = CanAct && legal;
        }

        void HandView(JObject state)
        {
            var hand = state["hand"] as JArray ?? new JArray();
            float y = HandTop, height = HandHeight;
            float cardWidth = Math.Max(488, MemoryForgeUiViewport.FontSize(24) * 10 + 32), cardPitch = cardWidth + 16;
            var strip = Scroll(frame!, "GrowthHand", 32, y, 1536, height, Math.Max(1536, hand.Count * cardPitch));
            for (int n = 0; n < hand.Count; n++)
            {
                var card = hand[n]; string uid = Text(card["uid"]);
                var tile = Button(strip, "GrowthCard:" + uid, "", n * cardPitch, 0, cardWidth, height - 8, () => SelectCard(uid), selected == uid ? new Color(0.11f, 0.23f, 0.27f) : Panel);
                tile.interactable = CanAct;
                var cardName = Label(tile.transform, CardName(Text(card["id"])), 24, 16, cardWidth - 48, 1, 24, Brass);
                Label(tile.transform, card["cost"]?.Type == JTokenType.Integer ? "행동력 " + Number(card["cost"]) : "사용 불가", 24, 16 + cardName.rectTransform.rect.height + 12, cardWidth - 48, 64, 18, Teal);
            }
        }

        void Terminal(GrowthSettlement settlement)
        {
            var panel = Box(frame!, "GrowthResult", 240, 208, 1120, Math.Max(472, 372 + TouchUnits()), Panel);
            bool won = settlement.Outcome == "VICTORY";
            bool weekly = settlement.Kind == "weeklyBoss";
            Label(panel, won ? weekly ? "주간 보스 격파" : "훈련 완료" : "다음 도전을 준비해요", 48, 32, 1024, 72, 44, Brass);
            Label(panel, (weekly ? "이번 주 보상 " + settlement.Claimed + "/3 · " + settlement.Remaining + "회 남음" : "기록  " + new string('★', settlement.Stars))
                + "\n집중 소모 " + settlement.FocusCost, 48, 128, 1024, 96, 28, Ink);
            var rewards = settlement.Grant.Resources.Where(item => item.Value > 0).Select(item => MaterialName(item.Key) + " " + item.Value);
            string result = !won ? "집중을 소모하지 않았어요. 보유 동료와 기억은 그대로예요."
                : weekly && settlement.Rewarded != true ? settlement.Reason == "PRACTICE" ? "연습 모드를 완료했어요. 주간 보상 횟수는 사용하지 않았어요." : "이번 주 보상을 모두 받았어요. 전투 기록을 저장했어요."
                : string.Join("   ·   ", rewards) + "\n계정 경험치 +" + settlement.Grant.AccountXp + " · 서버에 저장했어요.";
            Label(panel, result, 48, 248, 1024, 100, 25, Teal);
            Button(panel, "GrowthResultHome", "공방으로", 48, 356, 1024, 96, Home, Brass);
        }

        IEnumerator UltimateCutin(string actor)
        {
            yield return MemoryForgeCombatCutin.Play(transform, font, SpriteAt("Characters/char_" + actor + "_cutin"), CharacterName(actor),
                content?.Battle.Characters.FirstOrDefault(item => item.Id == actor)?.Ult.Name ?? "필살기", "GrowthUltimateCutin");
        }

        void Fit()
        {
            if (canvas == null || frame == null) return;
            MemoryForgeUiViewport.Fit(frame, canvas);
        }

        Sprite? SpriteAt(string path)
        {
            if (sprites.TryGetValue(path, out var cached)) return cached;
            var texture = Resources.Load<Texture2D>("Art/" + path);
            Vector4 border = path.EndsWith("ui_panel_frame", StringComparison.Ordinal) ? new Vector4(48, 48, 48, 48) : path.Contains("ui_button_pill") ? new Vector4(64, 0, 64, 0) : Vector4.zero;
            return sprites[path] = texture == null ? null : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }
        void DecoratePanels()
        {
            if (frame == null) return;
            foreach (var image in frame.GetComponentsInChildren<Image>().Where(item => item.color == Panel && item.sprite == null).ToArray())
            {
                var size = image.rectTransform.rect.size;
                var rim = Box(image.transform, "Ornament", 0, 0, size.x, size.y, Color.white).GetComponent<Image>();
                rim.sprite = SpriteAt("UI/battle/ui_panel_frame"); rim.type = Image.Type.Sliced; rim.transform.SetAsFirstSibling();
                rim.rectTransform.anchorMin = Vector2.zero; rim.rectTransform.anchorMax = Vector2.one;
                rim.rectTransform.offsetMin = rim.rectTransform.offsetMax = Vector2.zero;
            }
        }
        string CardName(string id) => content?.Battle.Cards.FirstOrDefault(card => card.Id == id)?.Name ?? id;
        string CharacterName(string id) => content?.Battle.Characters.FirstOrDefault(character => character.Id == id)?.Name ?? id;
        internal static string MaterialName(string id) => id switch { "credits" => "공방화", "ink" => "잉크 XP", "oil" => "기름 XP", "seal" => "인장", "alloy" => "합금", "book" => "기술서", "polish" => "연마제", "core" => "승급핵", "refine_core" => "제련핵", "boss_token" => "보스 토큰", _ => id };
        internal static string MutationNotice(PracticeMutationStatus status, string code, string? refresh, string? outbox) =>
            status == PracticeMutationStatus.Pending ? "보낸 요청을 보존했어요. 같은 요청 확인으로 결과를 확인해주세요."
            : refresh != null || outbox != null ? "결과 확인이 남아 있어요. 새로고침 후 보존된 요청을 확인해주세요."
            : status == PracticeMutationStatus.Committed ? "서버에 저장했어요."
            : code switch
            {
                "NO_ELIGIBLE_WEAPON" => "출전 동료에게 사용할 수 있는 무기를 장착해주세요.",
                "INSUFFICIENT_RESOURCES" => "필요 재료가 부족해요. 성장 공방에서 보유 수량을 확인해주세요.",
                "LEVEL_CAP" => "현재 레벨 상한에 도달했어요. 다음 승급 조건을 확인해주세요.",
                "LOCKED" => "아직 해금되지 않았어요. 필요한 계정 레벨을 확인해주세요.",
                "ACCOUNT_LEVEL_REQUIRED" => "아직 해금되지 않았어요. 필요한 계정 레벨을 확인해주세요.",
                "OVER_CAPACITY" => "기억 덱이 수용량을 넘었어요. 저장한 덱과 동료의 성장을 확인해주세요.",
                "STALE_REVISION" => "진행 정보가 바뀌었어요. 새로고침한 뒤 다시 선택해주세요.",
                "STALE_VERSION" => "진행 정보가 바뀌었어요. 새로고침한 뒤 다시 선택해주세요.",
                _ => "반영하지 못했어요. 계정과 자원을 새로 확인해주세요."
            };
        static string Text(JToken? value) => value?.Type == JTokenType.String ? value.Value<string>() ?? "" : "";
        static int Number(JToken? value) => value?.Type == JTokenType.Integer ? value.Value<int>() : 0;
        static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var value); return value; }
        static RectTransform Box(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Image)); host.transform.SetParent(parent, false);
            var rect = (RectTransform)host.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); MemoryForgeUiViewport.Anchor(rect, x, y, width, height);
            var image = host.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
            if (width == 1600 && height == 900 && MemoryForgeUiViewport.IsDim(name)) MemoryForgeUiViewport.BleedDim(image);
            if (width == 1600 && height == 900 && (name.Contains("Backdrop") || name == "Shade")) MemoryForgeUiViewport.Bleed(rect);
            return rect;
        }
        Text Label(Transform parent, string text, float x, float y, float width, float height, int size, Color color)
        {
            var host = new GameObject("Text", typeof(RectTransform), typeof(Text)); host.transform.SetParent(parent, false);
            var rect = (RectTransform)host.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); MemoryForgeUiViewport.Anchor(rect, x, y, width, height);
            var label = host.GetComponent<Text>(); label.font = font; label.text = text; label.fontSize = size; label.color = color; label.raycastTarget = false; label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; MemoryForgeUiViewport.WholeText(label); return label;
        }
        Button Button(Transform parent, string name, string text, float x, float y, float width, float height, Action action, Color? color = null)
        {
            height = Math.Max(height, TouchUnits()); width = Math.Max(width, TouchUnits());
            var rect = Box(parent, name, x, y, width, height, color ?? Panel); var image = rect.GetComponent<Image>(); image.raycastTarget = true;
            if (text.Length > 0)
            { image.sprite = SpriteAt(color == Brass ? "UI/battle/ui_button_pill_brass" : "UI/battle/ui_button_pill_ink"); image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 128f / Math.Max(1, height); image.color = Color.white; }
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.onClick.AddListener(() => { if (button != null && button.IsActive() && button.IsInteractable()) action(); });
            if (text.Length > 0) { var label = Label(rect, text, 12, 12, width - 24, height - 24, 24, color == Brass ? Panel : Ink); label.alignment = TextAnchor.MiddleCenter; }
            return button;
        }
        static float TouchUnits()
        {
            if (MemoryForgeExpeditionClient.TouchOverride.HasValue) return MemoryForgeExpeditionClient.TouchOverride.Value;
            return MemoryForgeUiViewport.TouchUnits();
        }
        static void Bar(Transform parent, float x, float y, float width, float height, int value, int max, Color color)
        {
            var back = Box(parent, "Bar", x, y, width, height, new Color(0.1f, 0.14f, 0.2f));
            float fraction = max > 0 ? Mathf.Clamp01((float)value / max) : 0;
            var fill = Box(back, "Fill", 0, 0, width * fraction, height, color);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(fraction, 1); fill.offsetMin = fill.offsetMax = Vector2.zero;
        }
        static RectTransform Scroll(Transform parent, string name, float x, float y, float width, float height, float contentWidth)
        {
            var viewport = Box(parent, name, x, y, width, height, Color.clear); viewport.GetComponent<Image>().raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>(); var content = Box(viewport, "Content", 0, 0, contentWidth, height, Color.clear);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(contentWidth > width ? 0 : 1, 1); content.pivot = new Vector2(0, 1); content.sizeDelta = new Vector2(contentWidth > width ? contentWidth : 0, height); content.anchoredPosition = Vector2.zero;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content; scroll.horizontal = true; scroll.vertical = false; scroll.movementType = ScrollRect.MovementType.Clamped; return content;
        }
    }
}
