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
        void OnApplicationFocus(bool focused) { if (focused && isActiveAndEnabled && !Busy) { if (session == null) Open(); else _ = Refresh(); } }
        void Update() { Fit(); if (Input.GetKeyDown(KeyCode.Escape) && !busy) { if (supportOpen) CloseGrowthSupport(); else Home(); } }

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
            if (canvas != null) { canvas.gameObject.SetActive(false); Destroy(canvas.gameObject); }
            var host = new GameObject(OwnedCanvas, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); host.transform.SetParent(transform, false);
            host.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = host.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
            canvas = (RectTransform)host.transform;
            if (EventSystem.current == null) new GameObject("GrowthInput", typeof(EventSystem), typeof(StandaloneInputModule)).transform.SetParent(transform, false);
            var backdrop = Box(canvas, "BattleBackdrop", 0, 0, 1600, 900, Color.white); backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one; backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
            backdrop.GetComponent<Image>().sprite = SpriteAt("Backgrounds/bg_zone1_battle");
            frame = Box(canvas, "GrowthFrame", 0, 0, 1600, 900, Color.clear); frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f); frame.anchoredPosition = Vector2.zero;
            Box(frame, "Shade", 0, 0, 1600, 900, new Color(0.02f, 0.035f, 0.065f, 0.32f));
            var top = Box(frame, "Header", 32, 16, 1536, Math.Max(96, TouchUnits() + 16), Panel);
            Button(top, "GrowthHome", "공방", 12, 8, 144, 80, Home).interactable = !Busy;
            Label(top, session?.Encounter?.Kind == "weeklyBoss" ? "주간 보스 도전" : "동료 훈련", 184, 8, 420, 48, 32, Brass);
            Label(top, Busy ? "서버 확인 중…" : notice, 184, 56, session?.Encounter?.Support != null ? 480 : 980, Math.Max(32, top.sizeDelta.y - 64), 18, Muted);
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
            bool large = TouchUnits() > 96;
            float battleY = 32 + Math.Max(96, TouchUnits() + 16), battleHeight = (large ? 476 : 512) - battleY;
            var party = Box(frame!, "GrowthParty", 32, battleY, 352, battleHeight, Panel);
            Label(party, "턴 " + Number(state["turn"]) + "  ·  행동력 " + Number(state["ap"]), 20, large ? 0 : 20, 312, 44, 28, Brass);
            Label(party, "파티 체력  " + Number(state["hp"]) + " / " + Number(state["maxHp"]), 20, large ? 44 : 80, 312, 40, 24, Ink);
            Bar(party, 20, large ? 88 : 128, 312, 12, Number(state["hp"]), Number(state["maxHp"]), Teal);
            Label(party, "방어 " + Number(state["block"]) + "  ·  메아리 " + Number(state["echo"]), 20, large ? 100 : 164, 312, 32, 20, Muted);
            var actors = state["actors"] as JArray ?? new JArray();
            float ultimateHeight = Math.Max(80, TouchUnits());
            var ultimateStrip = Scroll(party, "GrowthUltimates", 20, large ? 136 : 216, 312, ultimateHeight, Math.Max(312, actors.Count * 324 - 12));
            int n = 0;
            foreach (var actor in actors)
            {
                string id = Text(actor["id"]); int gauge = Number(actor["gauge"]), max = Number(state["gaugeMax"]);
                var ult = Button(ultimateStrip, "GrowthUlt:" + id, "", n++ * 324, 0, 312, ultimateHeight,
                    () => { var args = new JObject { ["actor"] = id }; if (target != null) args["target"] = target; _ = Send("ult", args, id); });
                ult.interactable = CanAct && gauge >= max && max > 0;
                var portrait = Box(ult.transform, "Companion", 8, 8, 56, ultimateHeight - 16, Color.white); portrait.GetComponent<Image>().sprite = SpriteAt("Characters/char_" + id + "_sd"); portrait.GetComponent<Image>().preserveAspect = true;
                if (portrait.GetComponent<Image>().sprite == null) portrait.GetComponent<Image>().color = Color.clear;
                Label(ult.transform, CharacterName(id) + "  " + gauge + "/" + max + "\n필살기 · 좌우로 동료 선택", 72, 12, 228, ultimateHeight - 24, 21, gauge >= max && max > 0 ? Brass : Muted);
            }
            var enemies = (state["enemies"] as JArray ?? new JArray()).ToList();
            float width = Math.Min(540, 1144f / Math.Max(1, enemies.Count));
            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i]; string uid = Text(enemy["uid"]); bool alive = enemy["alive"]?.Value<bool>() == true;
                var tile = Button(frame!, "GrowthEnemy:" + uid, "", 412 + i * (width + 12), battleY, width, battleHeight,
                    () => { if (!CanAct) return; target = uid; picks.Clear(); selection++; _ = Query(); });
                tile.interactable = CanAct && alive; tile.GetComponent<Image>().color = target == uid ? new Color(0.1f, 0.25f, 0.29f, 0.7f) : new Color(0.02f, 0.04f, 0.07f, 0.5f);
                var figure = Box(tile.transform, "EnemyArt", 40, 20, width - 80, battleHeight - 152, alive ? Color.white : new Color(1, 1, 1, 0.3f));
                figure.GetComponent<Image>().sprite = SpriteAt("Enemies/enemy_" + Text(enemy["id"])); figure.GetComponent<Image>().preserveAspect = true;
                if (figure.GetComponent<Image>().sprite == null) figure.GetComponent<Image>().color = Color.clear;
                Label(tile.transform, "이번  " + MemoryForgeServerPracticeClient.MoveText(enemy["intent"]), 20, 8, width - 40, 44, 20, Brass);
                Label(tile.transform, Text(enemy["name"]), 20, battleHeight - 132, width - 40, 44, 28, Ink);
                int loss = query?.EnemyHpLosses.FirstOrDefault(item => item.Uid == uid).HpLoss ?? 0;
                Label(tile.transform, Number(enemy["hp"]) + " / " + Number(enemy["maxHp"]) + (loss > 0 ? " → " + Math.Max(0, Number(enemy["hp"]) - loss) : ""), 20, battleHeight - 80, width - 40, 44, 22, Teal);
                Bar(tile.transform, 20, battleHeight - 28, width - 40, 12, Number(enemy["hp"]), Number(enemy["maxHp"]), Teal);
            }
            Button(frame!, "GrowthEndTurn", "턴 마치기", 32, 528, 352, 96, () => _ = Send("end", new JObject())).interactable = CanAct && Text(state["phase"]) == "PLAYER";
            SelectionView(state);
            HandView(state);
        }

        void SelectionView(JObject state)
        {
            if (selected == null) { Label(frame!, "카드를 고른 뒤 대상을 선택하세요.", 424, 548, 1088, 48, 27, Ink); return; }
            var answer = query?.ToJObject();
            var source = answer?["source"];
            Label(frame!, source == null ? "카드 미리보기 확인 중…" : Text(source["name"]), 424, TouchUnits() > 96 ? 480 : 528, 744, 44, 26, Brass);
            var card = state["hand"]?.FirstOrDefault(item => Text(item["uid"]) == selected);
            var modes = card?["modes"] as JArray ?? new JArray();
            for (int i = 0; i < modes.Count; i++)
            {
                string choice = Text(modes[i]);
                Button(frame!, "GrowthMode:" + choice, choice == "now" ? "지금" : choice == "next" ? "다음" : "다다음", 424 + i * 160, TouchUnits() > 96 ? 528 : 576, 152, 48,
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
            Button(frame!, "GrowthPlay", legal ? "카드 사용" : "선택 확인", 1200, 528, 368, 96, () =>
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
            float y = 528 + Math.Max(96, TouchUnits()) + 16, height = 884 - y;
            var strip = Scroll(frame!, "GrowthHand", 32, y, 1536, height, Math.Max(1536, hand.Count * 228));
            for (int n = 0; n < hand.Count; n++)
            {
                var card = hand[n]; string uid = Text(card["uid"]);
                var tile = Button(strip, "GrowthCard:" + uid, "", n * 228, 0, 212, height - 8, () => SelectCard(uid), selected == uid ? new Color(0.11f, 0.23f, 0.27f) : Panel);
                tile.interactable = CanAct;
                Label(tile.transform, CardName(Text(card["id"])), 16, 16, 180, 64, 24, Brass);
                Label(tile.transform, card["cost"]?.Type == JTokenType.Integer ? "행동력 " + Number(card["cost"]) : "사용 불가", 16, 82, 180, 32, 20, Teal);
                var definition = content?.Battle.Cards.FirstOrDefault(item => item.Id == Text(card["id"]));
                Label(tile.transform, definition == null || content == null ? "카드를 골라 자세히 확인하세요." : BattleText.Card(definition, content.Battle), 16, 122, 180, height - 136, 17, Ink);
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
            var bounds = canvas.rect; var safe = Screen.safeArea;
            float width = bounds.width * safe.width / Math.Max(1, Screen.width), height = bounds.height * safe.height / Math.Max(1, Screen.height);
            frame.localScale = Vector3.one * Math.Min(width / 1600f, height / 900f);
            frame.anchoredPosition = new Vector2(bounds.width * (safe.center.x / Math.Max(1, Screen.width) - 0.5f), bounds.height * (safe.center.y / Math.Max(1, Screen.height) - 0.5f));
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
                var size = image.rectTransform.sizeDelta;
                var rim = Box(image.transform, "Ornament", 0, 0, size.x, size.y, Color.white).GetComponent<Image>();
                rim.sprite = SpriteAt("UI/battle/ui_panel_frame"); rim.type = Image.Type.Sliced;
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
            var rect = (RectTransform)host.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
            var image = host.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return rect;
        }
        Text Label(Transform parent, string text, float x, float y, float width, float height, int size, Color color)
        {
            var host = new GameObject("Text", typeof(RectTransform), typeof(Text)); host.transform.SetParent(parent, false);
            var rect = (RectTransform)host.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
            var label = host.GetComponent<Text>(); label.font = font; label.text = text; label.fontSize = size; label.color = color; label.raycastTarget = false; label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; return label;
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
            if (!Application.isMobilePlatform || Screen.dpi <= 0) return 0;
            float scale = Mathf.Min(Screen.safeArea.width / 1600f, Screen.safeArea.height / 900f);
            return scale <= 0 ? 0 : 48f * Screen.dpi / 160f / scale;
        }
        static void Bar(Transform parent, float x, float y, float width, float height, int value, int max, Color color)
        {
            var back = Box(parent, "Bar", x, y, width, height, new Color(0.1f, 0.14f, 0.2f));
            Box(back, "Fill", 0, 0, max > 0 ? width * Mathf.Clamp01((float)value / max) : 0, height, color);
        }
        static RectTransform Scroll(Transform parent, string name, float x, float y, float width, float height, float contentWidth)
        {
            var viewport = Box(parent, name, x, y, width, height, Color.clear); viewport.GetComponent<Image>().raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>(); var content = Box(viewport, "Content", 0, 0, contentWidth, height, Color.clear);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content; scroll.horizontal = true; scroll.vertical = false; scroll.movementType = ScrollRect.MovementType.Clamped; return content;
        }
    }
}
