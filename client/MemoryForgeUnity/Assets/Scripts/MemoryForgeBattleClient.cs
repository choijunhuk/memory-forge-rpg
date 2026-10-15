#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using MemoryForge.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    // §14.3: the view dispatches real shared-core commands; it never owns combat rules.
    [ExecuteAlways]
    public sealed class MemoryForgeBattleClient : MonoBehaviour
    {
        public uint Seed = 1234;
        public BattleState? Snapshot => battle?.State;
        public string? SelectedCard => selectedCard;
        public string? SelectedMode => selectedMode;
        public string? LastError { get; private set; }
        ClientBattleContent? content;
        Battle? battle;
        BattleSetup? setup;
        readonly List<BattleRequest> history = new List<BattleRequest>();
        readonly List<string> picks = new List<string>();
        readonly List<string[]> encounters = new List<string[]>();
        readonly Dictionary<string, string> errors = new Dictionary<string, string>();
        RectTransform? canvasRoot, frame;
        Font? font;
        string? selectedCard, selectedMode, target;
        string notice = "카드를 고른 뒤 사용 방식을 선택하세요.";
        float noticeUntil;
        int encounterIndex;
        bool showLog, showPicks;
        const string OwnedCanvas = "MemoryForgeBattleView";
        static readonly Color Background = Hex("0d1322"), Panel = Hex("141c31"), Line = Hex("2f3d63"), Ink = Hex("e9eef8"),
            Muted = Hex("98a5c0"), Brass = Hex("e2b45f"), Memory = Hex("62d6c6"), Danger = Hex("ff6f6f");

        void OnEnable() { if (Resources.Load<TextAsset>("MemoryForge/battle-content") != null) StartPracticeBattle(); }
        void OnDisable() { RemoveView(); }
        void Update()
        {
            FitView();
            if (noticeUntil > 0 && Time.realtimeSinceStartup >= noticeUntil)
            { noticeUntil = 0; LastError = null; notice = "카드를 고른 뒤 사용 방식을 선택하세요."; Render(); }
        }

        public void StartPracticeBattle()
        {
            content = ClientBattleContent.Load();
            font = Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular");
            if (font == null) throw new InvalidOperationException("The licensed Korean interface font is missing.");
            var messages = Resources.Load<TextAsset>("MemoryForge/error-messages");
            errors.Clear();
            if (messages != null)
                foreach (var pair in Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(messages.text)!) errors[pair.Key] = pair.Value;
            encounters.Clear();
            foreach (var zone in content.Encounters.OrderBy(p => p.Key, StringComparer.Ordinal))
            { encounters.AddRange(zone.Value.Normal); encounters.AddRange(zone.Value.Elite); encounters.AddRange(zone.Value.Boss); }
            if (encounters.Count == 0) throw new InvalidOperationException("No authored practice encounter exists.");
            encounterIndex %= encounters.Count;
            setup = new BattleSetup { Seed = Seed, Hp = 243, MaxHp = 243, DifficultyHp = 100, DifficultyDamage = 100,
                Enemies = new List<string>(encounters[encounterIndex]) };
            // These are the source's explicit reference-battle inputs, not account growth or saved assets.
            foreach (string id in new[] { "sera", "harin", "nia" })
            {
                setup.Party.Add(new BattleActorSetup { CharId = id, Hp = 81, Power4 = 160, SkillLv = 1, UltLv = 1, Refine = 1 });
                foreach (var card in content.Battle.Cards.Where(c => c.Owner == id && c.Start == true)) setup.Deck.Add(new BattleCardSpec { Id = card.Id });
                setup.Deck.Add(new BattleCardSpec { Id = content.StartingCommon[id], Assigned = id });
                var weapon = content.Battle.Weapons.First(w => w.Type == content.CharacterWeaponTypes[id] && w.Id.StartsWith("w_basic_", StringComparison.Ordinal));
                setup.Deck.Add(new BattleCardSpec { Id = weapon.Card, Wielder = id });
            }
            battle = Battle.Create(content.Battle, setup); history.Clear(); picks.Clear(); selectedCard = null; selectedMode = null; target = null; LastError = null;
            showLog = false; showPicks = false;
            Execute("begin");
            notice = "카드를 고르고, 적을 선택해 기억을 펼치세요."; Render();
        }

        public void SelectCard(string uid) { selectedCard = uid; selectedMode = null; picks.Clear(); showPicks = false; Render(); }
        public void SelectMode(string mode) { selectedMode = mode; Render(); }
        public void ConfirmCard() { if (selectedCard != null && selectedMode != null) PlayCard(selectedCard, selectedMode); }
        public void SelectEnemy(string uid) { target = uid; Render(); }
        public void EndTurn() { Execute("end"); }
        public void UseUltimate(string actor) { Execute("ult", Arguments(("actor", actor), ("target", target)), "필살기를 사용했습니다."); }
        public void PlayCard(string uid, string mode = "now")
        {
            var args = new List<KeyValuePair<string, BattleArgument>> { Pair("uid", uid), Pair("mode", mode), Pair("target", target) };
            args.Add(new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array(picks.Select(BattleArgument.String))));
            string name = battle!.State.Cards.TryGetValue(uid, out var card) ? content!.Battle.Cards.First(c => c.Id == card.Id).Name : "카드";
            Execute("play", BattleArgument.Object(args), mode == "now" ? name + " 사용" : name + " 예약");
        }

        bool Execute(string kind, BattleArgument? args = null, string? success = null)
        {
            if (battle == null) return false;
            try
            {
                var state = battle.State;
                var request = new BattleRequest("ui-" + state.V + "-" + kind, state.V, kind, args);
                battle.Command(request); history.Add(request); LastError = null;
                if (kind == "play") { picks.Clear(); selectedMode = null; }
            }
            catch (RuleException error)
            { LastError = error.Code; notice = ErrorText(error.Code); noticeUntil = Time.realtimeSinceStartup + 2.6f; Render(); return false; }
            if (success != null) notice = success;
            if (kind == "end") notice = "적의 행동과 예약 해결을 마쳤습니다.";
            noticeUntil = Time.realtimeSinceStartup + 2.6f;
            Render(); return true;
        }

        public string Preview(string uid, string mode)
        {
            if (battle == null || content == null || setup == null) return "";
            string? error = battle.Check(uid, mode, target, picks);
            if (error != null) return ErrorText(error);
            if (mode != "now") return "행동력 " + battle.CostOf(uid, mode) + " 지불 · " + (mode == "next" ? "다음 턴" : "다다음 턴") + "에 해결";
            if (HiddenOutcome(battle.EffectiveCard(uid).Effects)) return "드로우·무작위 결과는 사용 후 확인합니다";
            // Replaying committed commands gives a genuine independent candidate without exposing a mutable core state.
            var candidate = Battle.Create(content.Battle, setup);
            foreach (var command in history) candidate.Command(command);
            var before = candidate.State;
            var values = new List<KeyValuePair<string, BattleArgument>> { Pair("uid", uid), Pair("mode", mode), Pair("target", target) };
            values.Add(new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array(picks.Select(BattleArgument.String))));
            try { candidate.Command(new BattleRequest("preview-" + before.V, before.V, "play", BattleArgument.Object(values))); }
            catch (RuleException rejected) { return ErrorText(rejected.Code); }
            var after = candidate.State;
            if (!before.Rng.SequenceEqual(after.Rng) || !before.Zones.Draw.SequenceEqual(after.Zones.Draw))
                return "드로우·무작위 결과는 사용 후 확인합니다";
            int damage = before.Enemies.Sum(e => Math.Max(0, e.Hp - (after.Enemies.FirstOrDefault(a => a.Uid == e.Uid)?.Hp ?? 0)));
            var parts = new List<string>();
            if (damage > 0) parts.Add("피해 " + damage);
            if (after.Block > before.Block) parts.Add("방어 +" + (after.Block - before.Block));
            if (after.Hp != before.Hp) parts.Add("체력 " + (after.Hp > before.Hp ? "+" : "") + (after.Hp - before.Hp));
            if (after.Phase == "DEFEAT") parts.Add("이 행동으로 패배");
            if (parts.Count == 0) parts.Add("카드 효과 적용");
            return string.Join(" · ", parts);
        }

        void Render()
        {
            if (battle == null || content == null || font == null) return;
            RemoveView();
            var canvasObject = new GameObject(OwnedCanvas, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            if (!Application.isPlaying) canvasObject.hideFlags = HideFlags.DontSaveInEditor;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
            canvasRoot = canvasObject.GetComponent<RectTransform>();
            frame = Box(canvasRoot, "BattleFrame", 0, 0, 1600, 900, Background);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f); frame.pivot = new Vector2(0.5f, 0.5f); frame.anchoredPosition = Vector2.zero;
            var s = battle.State;
            if (!s.Enemies.Any(e => e.Uid == target && e.Alive)) target = null;
            if (target == null && s.Enemies.Count(e => e.Alive) == 1) target = s.Enemies.First(e => e.Alive).Uid;
            if (!s.Zones.Hand.Contains(selectedCard ?? "")) { selectedCard = s.Zones.Hand.FirstOrDefault(); selectedMode = null; }
            Label(frame, "기억 공방", 32, 20, 350, 46, 36, Brass, FontStyle.Bold);
            Label(frame, "전투 연습  /  " + string.Join(" · ", setup!.Enemies.Select(id => content.Battle.Enemies.First(e => e.Id == id).Name)), 386, 29, 700, 38, 22, Muted);
            Button(frame, "다른 조우", 1120, 20, 172, 52, () => { encounterIndex++; StartPracticeBattle(); });
            Button(frame, "다시 시작", 1308, 20, 180, 52, StartPracticeBattle);
            Button(frame, "기록", 1504, 20, 64, 52, () => { showLog = !showLog; Render(); });
            var top = Box(frame, "PartyStatus", 32, 88, 1536, 90, Panel);
            Label(top, "파티 체력  " + s.Hp + " / " + s.MaxHp, 20, 10, 420, 38, 30, Ink, FontStyle.Bold);
            Bar(top, 20, 58, 416, 12, s.Hp, s.MaxHp, Memory);
            Label(top, s.Turn + "턴  ·  방어 " + s.Block + "  ·  반격 " + s.Counter + "  ·  부채 " + s.Debts.Sum(d => d.N) + "  ·  잔향 " + s.Echo + "/" + s.EchoMax, 472, 14, 720, 34, 23, Ink);
            Label(top, "뽑을 " + s.Zones.Draw.Count + "  /  버림 " + s.Zones.Discard.Count + "  /  소멸 " + s.Zones.Exhaust.Count, 472, 51, 340, 28, 21, Muted);
            string party = PartyStatusText(s);
            if (party.Length != 0) Label(top, party, 830, 51, 380, 28, 21, Brass);
            Label(top, "행동력  " + s.Ap, 1232, 17, 280, 52, 40, Brass, FontStyle.Bold);
            Party(s); Enemies(s); Timeline(s); Actions(s); Hand(s);
            if (s.Phase == "VICTORY" || s.Phase == "DEFEAT")
            {
                var result = Overlay("BattleResult", 480, 240, 640, 228);
                Label(result, s.Phase == "VICTORY" ? "기억을 지켜냈습니다" : "기억이 흩어졌습니다", 24, 28, 592, 66, 38, s.Phase == "VICTORY" ? Brass : Danger, FontStyle.Bold, TextAnchor.MiddleCenter);
                Label(result, s.Turn + "턴 · 남은 체력 " + s.Hp, 24, 104, 592, 42, 26, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
                Button(result, "다시 연습하기", 170, 160, 300, 52, StartPracticeBattle, Brass);
            }
            if (showPicks) Picks(s);
            if (showLog) Log(s);
            if (Application.isPlaying && EventSystem.current == null)
            { var events = new GameObject("BattleInput", typeof(EventSystem), typeof(StandaloneInputModule)); events.transform.SetParent(canvasObject.transform, false); }
            Canvas.ForceUpdateCanvases(); FitView();
        }

        void Party(BattleState s)
        {
            var panel = Box(frame!, "Party", 32, 194, 350, 256, Panel);
            for (int i = 0; i < s.Actors.Count; i++)
            {
                var actor = s.Actors[i]; float y = 14 + i * 80;
                Label(panel, actor.Name, 18, y, 155, 38, 30, Ink, FontStyle.Bold);
                Label(panel, Attribute(actor.Attr) + "  ·  " + actor.Gauge + "/100", 18, y + 40, 170, 24, 21, Muted);
                Bar(panel, 188, y + 50, 142, 9, actor.Gauge, 100, Brass);
                var button = Button(panel, actor.Gauge >= 100 ? "필살기" : "충전 중", 188, y, 144, 44, () => UseUltimate(actor.Id), actor.Gauge >= 100 ? Brass : Line);
                button.interactable = actor.Gauge >= 100 && s.Phase == "PLAYER";
            }
        }
        void Enemies(BattleState s)
        {
            float width = (1154 - 16 * (s.Enemies.Count - 1)) / Math.Max(1, s.Enemies.Count);
            for (int i = 0; i < s.Enemies.Count; i++)
            {
                var enemy = s.Enemies[i];
                var button = Button(frame!, enemy.Uid, 414 + i * (width + 16), 194, width, 256, () => SelectEnemy(enemy.Uid), Panel);
                button.GetComponentInChildren<Text>().text = ""; button.interactable = enemy.Alive;
                var p = button.GetComponent<RectTransform>();
                if (target == enemy.Uid && enemy.Alive) { var outline = p.gameObject.AddComponent<Outline>(); outline.effectColor = Memory; outline.effectDistance = new Vector2(2, 2); }
                Label(p, enemy.Alive ? enemy.Name : enemy.Name + " · 처치", 18, 14, width - 36, 40, 29, enemy.Alive ? Ink : Muted, FontStyle.Bold);
                Label(p, "체력 " + enemy.Hp + " / " + enemy.MaxHp + (enemy.Block > 0 ? "  방어 " + enemy.Block : ""), 18, 60, width - 36, 30, 23, Ink);
                Bar(p, 18, 96, width - 36, 12, enemy.Hp, enemy.MaxHp, Danger);
                Label(p, "균열 " + enemy.Brk + "/" + enemy.BrkMax + "  ·  약점 " + string.Join("·", enemy.Weak.Select(Attribute)), 18, 116, width - 36, 28, 20, Memory);
                var rules = content!.Battle.Rules; string status = StatusText(enemy, rules);
                if (enemy.Alive && status.Length != 0) Label(p, status, 18, 146, width - 36, 28, 20, Ink);
                Label(p, IntentLines(enemy, s, rules), 18, 178, width - 36, 64, 22, enemy.Alive ? Brass : Muted);
            }
        }
        void Timeline(BattleState s)
        {
            var panel = Box(frame!, "Timeline", 32, 466, 1536, 100, Panel);
            Label(panel, "기억 시간표", 18, 10, 180, 32, 24, Memory, FontStyle.Bold);
            var cells = TimelineCells(s, content!.Battle.Rules);
            float slotWidth = Math.Min(220, 1308f / Math.Max(1, cells.Count));
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                string text = (cell.Group == "next" ? "다음" : "다다음") + "  " + (cell.Kind == "card" ? battle!.EffectiveCard(cell.Uid!).Name
                    : cell.Kind == "empty" ? "빈 칸" : cell.Kind == "sealed" ? "봉인" : "-");
                var box = Box(panel, "ReservationSlot", 204 + i * slotWidth, 20, slotWidth - 12, 60, cell.Kind == "sealed" ? Line : Background);
                Label(box, text, 10, 4, slotWidth - 32, 52, 22, cell.Kind == "card" ? Memory : cell.Kind == "sealed" ? Danger : Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            }
        }
        void Actions(BattleState s)
        {
            var panel = Box(frame!, "CardActions", 32, 582, 1536, 98, Panel);
            Label(panel, notice, 18, 6, 1498, 30, 23, LastError == null ? Muted : Danger);
            if (selectedCard == null || s.Phase != "PLAYER") return;
            string uid = selectedCard; var def = battle!.EffectiveCard(uid);
            Label(panel, def.Name + " → " + (s.Enemies.FirstOrDefault(e => e.Uid == target)?.Name ?? (battle.NeedsTarget(uid) == null ? "파티" : "적 선택")), 18, 44, 342, 42, 25, Ink, FontStyle.Bold);
            Button(panel, "효과 대상", 376, 38, 156, 52, () => { showPicks = !showPicks; Render(); }, Line, 22);
            int i = 0;
            foreach (string mode in battle.AllowedModes(uid))
            {
                string? error = battle.Check(uid, mode, target, picks);
                string caption = (mode == "now" ? "즉시" : mode == "next" ? "다음 턴" : "다다음 턴") + " · 비용 " + battle.CostOf(uid, mode);
                caption += "\n" + (error == null ? Preview(uid, mode) : ErrorText(error));
                var button = Button(panel, caption, 548 + i++ * 268, 38, 252, 52, () => SelectMode(mode), selectedMode == mode ? (mode == "now" ? Brass : Memory) : Line, 19);
                button.interactable = error == null || error == "BAD_TARGET" || error == "FIXED_MARK_REQUIRED";
            }
            string? reason = selectedMode == null ? "방식을 고르세요" : battle.Check(uid, selectedMode, target, picks);
            string confirmText = reason == null ? "사용 확정" : selectedMode == null ? "방식 선택" : reason == "BAD_TARGET" || reason == "FIXED_MARK_REQUIRED" ? "대상 선택" : ErrorText(reason);
            var confirm = Button(panel, confirmText, 1360, 38, 158, 52, ConfirmCard, reason == null ? Brass : Line, 22);
            confirm.interactable = reason == null;
        }
        void Hand(BattleState s)
        {
            var dock = Box(frame!, "Hand", 32, 696, 1308, 180, Background);
            var viewport = Box(dock, "Viewport", 0, 0, 1308, 180, Background); viewport.gameObject.AddComponent<RectMask2D>();
            var cards = Box(viewport, "Cards", 0, 0, Math.Max(1308, s.Zones.Hand.Count * 194), 180, Background);
            var scroll = dock.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = cards; scroll.horizontal = true; scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 25;
            for (int i = 0; i < s.Zones.Hand.Count; i++)
            {
                string uid = s.Zones.Hand[i]; var def = battle!.EffectiveCard(uid); bool selected = uid == selectedCard;
                var button = Button(cards, uid, i * 194, selected ? 0 : 4, 180, 172, () => SelectCard(uid), selected ? Line : Panel);
                button.GetComponentInChildren<Text>().text = ""; var p = button.GetComponent<RectTransform>();
                Label(p, "행동력 " + battle.CostOf(uid, battle.AllowedModes(uid).FirstOrDefault() ?? "now"), 12, 10, 156, 28, 21, Brass);
                Label(p, def.Name, 12, 52, 156, 64, 27, Ink, FontStyle.Bold);
                var modes = battle.AllowedModes(uid);
                Label(p, !modes.Contains("now") ? "예약 전용" : modes.Count > 1 ? "즉시 · 예약" : "즉시 사용", 12, 130, 156, 28, 20, Memory);
            }
            var end = Button(frame!, s.Phase == "PLAYER" ? "턴 종료\n" + s.Turn + "턴" : "전투 종료", 1360, 696, 208, 180, EndTurn, Brass, 32); end.interactable = s.Phase == "PLAYER";
        }
        void Picks(BattleState s)
        {
            var p = Overlay("EffectTargetPicker", 440, 180, 720, 490);
            Label(p, "효과 대상 · 누른 순서대로 선택", 24, 18, 672, 42, 28, Ink, FontStyle.Bold);
            var candidates = s.Zones.Hand.Concat(s.Reservations.Select(r => r.Uid)).Concat(s.Zones.Discard).Distinct().Where(uid => uid != selectedCard).ToList();
            var viewport = Box(p, "PickViewport", 24, 78, 672, 318, Panel); viewport.gameObject.AddComponent<RectMask2D>();
            var list = Box(viewport, "PickList", 0, 0, 672, Math.Max(318, ((candidates.Count + 2) / 3) * 64), Panel);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = list; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            for (int i = 0; i < candidates.Count; i++)
            {
                string uid = candidates[i]; string name = battle!.EffectiveCard(uid).Name; int order = picks.IndexOf(uid);
                string zone = s.Zones.Hand.Contains(uid) ? "손패" : s.Zones.Discard.Contains(uid) ? "버림" : "예약";
                Button(list, (order < 0 ? "" : (order + 1) + ". ") + name + " · " + zone, i % 3 * 226, i / 3 * 64, 212, 52,
                    () => { if (picks.Contains(uid)) picks.Remove(uid); else picks.Add(uid); Render(); }, order < 0 ? Line : Memory, 22);
            }
            Button(p, "자동 선택", 24, 416, 220, 52, () => { picks.Clear(); showPicks = false; Render(); });
            Button(p, "선택 완료", 470, 416, 226, 52, () => { showPicks = false; Render(); }, Brass);
        }
        void Log(BattleState s)
        {
            var p = Overlay("CombatHistory", 704, 190, 864, 484);
            Label(p, "전투 기록", 22, 14, 820, 40, 28, Brass, FontStyle.Bold);
            Button(p, "닫기", 720, 8, 122, 48, () => { showLog = false; Render(); });
            Label(p, string.Join("\n", s.Log.TakeLast(12).Select(EventText)), 22, 68, 820, 394, 22, Ink);
        }

        RectTransform Overlay(string name, float x, float y, float width, float height)
        {
            var blocker = Box(frame!, name + "InputBlocker", 0, 0, 1600, 900, new Color(0, 0, 0, 0.65f));
            blocker.GetComponent<Image>().raycastTarget = true;
            var panel = Box(blocker, name, x, y, width, height, Panel);
            panel.GetComponent<Image>().raycastTarget = true;
            return panel;
        }

        static bool HiddenOutcome(IEnumerable<EffectDef> effects)
        {
            foreach (var effect in effects)
                if (effect.Target == "rand" || effect.Pick == "random" || effect.Op == "draw"
                    || effect.Then != null && HiddenOutcome(effect.Then) || effect.Else != null && HiddenOutcome(effect.Else)) return true;
            return false;
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
            canvasRoot = null; frame = null;
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
        Button Button(Transform parent, string caption, float x, float y, float width, float height, Action click, Color? color = null, int size = 24)
        {
            var r = Box(parent, caption, x, y, width, height, color ?? Line); var image = r.GetComponent<Image>(); image.raycastTarget = true;
            var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(() => click());
            var colors = button.colors; colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f); colors.pressedColor = new Color(0.78f, 0.78f, 0.78f); colors.disabledColor = new Color(0.45f, 0.45f, 0.45f); button.colors = colors;
            Label(r, caption, 6, 5, width - 12, height - 10, size, (color.HasValue && (color.Value == Brass || color.Value == Memory)) ? Background : Ink, FontStyle.Normal, TextAnchor.MiddleCenter); return button;
        }
        void Bar(Transform parent, float x, float y, float width, float height, int value, int max, Color color)
        { var back = Box(parent, "Bar", x, y, width, height, Line); Box(back, "Fill", 0, 0, width * Mathf.Clamp01(max > 0 ? (float)value / max : 0), height, color); }
        string ErrorText(string code) => errors.TryGetValue(code, out var text)
            ? text.Contains("내부") ? "문제가 생겼어요. 다시 시도해 주세요." : text : "지금은 이 행동을 할 수 없어요.";
        static KeyValuePair<string, BattleArgument> Pair(string key, string? value) => new KeyValuePair<string, BattleArgument>(key, value == null ? BattleArgument.Null() : BattleArgument.String(value));
        static BattleArgument Arguments(params (string Key, string? Value)[] values) => BattleArgument.Object(values.Select(v => Pair(v.Key, v.Value)));
        static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
        static string Attribute(string attr) => attr == "volt" ? "전격" : attr == "flame" ? "화염" : attr == "frost" ? "빙결" : attr == "void" ? "공허" : attr == "none" ? "무속성" : attr;
        // Display-only projections of public Core state; they never execute or alter a rule.

        // Mirrors Core SlotCapacity (ReservationBook.CalculateCapacity is internal) and prototype/src/ui.js slotsView:
        // capacity = min(authored max, base + temporary), minus one sealed next-turn slot. Every reservation is always listed.
        public static List<(string Group, string Kind, string? Uid)> TimelineCells(BattleState s, BattleRules rules)
        {
            var cells = new List<(string Group, string Kind, string? Uid)>();
            foreach (string group in new[] { "next", "after" })
            {
                bool next = group == "next";
                var rows = s.Reservations.Where(r => next ? r.Due <= s.Turn + 1 : r.Due > s.Turn + 1).OrderBy(r => r.Due).ThenBy(r => r.Order).ToList();
                int max = next ? rules.NextSlotsMax : rules.AfterSlotsMax;
                int sealedCells = next && s.SealedUntil >= s.Turn ? 1 : 0;
                int cap = Math.Max(0, Math.Min(max, next ? s.SlotsBase.Next + s.SlotsTemp.Next : s.SlotsBase.After + s.SlotsTemp.After) - sealedCells);
                int total = Math.Max(rows.Count, Math.Max(max, cap + sealedCells));
                for (int i = 0; i < total; i++)
                {
                    if (i < rows.Count) cells.Add((group, "card", rows[i].Uid));
                    else if (i < cap) cells.Add((group, "empty", null));
                    else if (i < cap + sealedCells) cells.Add((group, "sealed", null));
                    else cells.Add((group, "none", null));
                }
            }
            return cells;
        }

        // Battle.cs EnemyHit: Round(d × difficulty / 100) + strength, then enemy weak floors by (10000 − weakBps) / 10000.
        public static int IntentDamage(int baseDamage, int difficultyDamage, int strength, int weak, int weakBps)
        {
            long damage = FloorDiv(2L * baseDamage * (difficultyDamage == 0 ? 100 : difficultyDamage) + 100, 200) + strength;
            if (weak > 0) damage = FloorDiv(damage * (10000 - weakBps), 10000);
            return (int)damage;
        }
        static long FloorDiv(long a, long b) { long q = a / b; return a % b != 0 && (a < 0) != (b < 0) ? q - 1 : q; }

        // forecast: the current intent with this enemy's present strength/weak (as Core applies at its move).
        // Otherwise a difficulty-scaled base value, because future statuses can still change.
        public static string IntentText(EnemyMoveDef? move, BattleEnemyStatus status, int difficultyDamage, int weakBps, bool forecast)
        {
            if (move == null) return "대기";
            string Hit(EnemyMoveDef m)
            {
                int value = IntentDamage(m.Damage ?? 0, difficultyDamage, forecast ? status.Strength : 0, forecast ? status.Weak : 0, weakBps);
                int hits = Math.Max(1, m.Hits ?? 1);
                return (m.Label != null ? m.Label + " " : "") + (forecast ? "공격 " : "기본 공격 ") + value + (hits > 1 ? " × " + hits : "");
            }
            switch (move.Type)
            {
                case "atk": return Hit(move);
                case "atkdebuff": return Hit(move) + " · 파티 약화 " + move.N;
                case "def": return "방어 " + move.Block + (move.Strength.HasValue ? " · 힘 +" + move.Strength : "");
                case "buff": return "힘 +" + move.Strength;
                case "debuff": return "파티 약화 " + move.N;
                case "charge": return move.Label ?? "힘을 모읍니다";
                case "summon": return move.Label ?? "증원 소환";
                case "heal": return "아군 회복 " + move.N;
                case "insert": return "저주 카드 " + move.N + "장 추가";
                case "reflect": return "반사 " + move.N;
                case "expose": return (move.Label ?? "약점 노출") + (move.Then != null ? " → " + IntentText(move.Then, status, difficultyDamage, weakBps, forecast) : "");
                case "blockAll": return "전원 방어 " + move.Block;
                default: return move.Label ?? "행동 준비";
            }
        }
        static string IntentLines(BattleEnemyState enemy, BattleState s, BattleRules rules) =>
            "이번  " + IntentText(enemy.Intent, enemy.Status, s.DifficultyDamage, rules.WeakBps, true)
            + "\n다음  " + IntentText(enemy.Next, enemy.Status, s.DifficultyDamage, rules.WeakBps, false);

        // Same chips and order as prototype/src/ui.js enemyCard (broken, vul, weak, mark, burn, str, reflect).
        public static string StatusText(BattleEnemyState enemy, BattleRules rules)
        {
            var parts = new List<string>();
            if (enemy.BrokenUntil != 0) parts.Add("균열 +" + rules.BrokenDamageBps / 100 + "%");
            var st = enemy.Status;
            if (st.Vul != 0) parts.Add("취약 " + st.Vul);
            if (st.Weak != 0) parts.Add("약화 " + st.Weak);
            if (st.Mark != 0) parts.Add("표식 " + st.Mark);
            if (st.Burn != 0) parts.Add("화상 " + st.Burn);
            if (st.Strength != 0) parts.Add("힘 " + st.Strength);
            if (st.Reflect != 0) parts.Add("반사 " + st.Reflect);
            return string.Join(" · ", parts);
        }

        // prototype/src/ui.js viewBattle: party weak plus active buffs (until >= turn) with the same names.
        public static string PartyStatusText(BattleState s)
        {
            var parts = new List<string>();
            if (s.Weak != 0) parts.Add("파티 약화 " + s.Weak);
            foreach (var buff in s.Buffs.Where(b => b.Until >= s.Turn))
                parts.Add(buff.Status == "charge" ? "충전" : buff.Status == "seraLock" ? "미래 확정" : buff.Status == "healUp" ? "기도문" : buff.Status == "lumiRush" ? "한계 돌파"
                    : buff.Status == "counterDouble" ? "반격 2배" : buff.Status == "overloadFree" ? "과부하 무료" : buff.Status);
            return string.Join(" · ", parts);
        }
        string EventText(BattleEvent e)
        {
            string actor = content!.Battle.Characters.FirstOrDefault(c => c.Id == e.Actor)?.Name ?? "";
            switch (e.E)
            {
                case "hit": return "피해 " + (e.Damage ?? e.N ?? 0);
                case "play": return "카드 사용";
                case "reserve": return "기억을 예약했습니다";
                case "ult": return actor + " 필살기";
                case "turn": return "턴 " + e.Turn;
                case "heal": return "체력 회복 " + e.N;
                case "block": return "방어 +" + e.N;
                case "break": return "균열!";
                case "kill": return "적을 처치했습니다";
                case "end": return "턴 종료";
                default: return "전투 효과 적용";
            }
        }
    }
}
