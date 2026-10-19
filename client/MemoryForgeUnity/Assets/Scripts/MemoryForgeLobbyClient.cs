#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    // The home (HOME-20261019: the game opens here, a subculture home, and the roguelike expedition is entered from 출격): one
    // companion on the workshop backdrop with their moments (tap, enter, idle and time-of-day reactions), a top bar, menu tiles over
    // the backdrop (출격 opens the expedition preparation: zone, length, party), a bond and staging list, and the settings. Tiles of
    // systems that are not built yet say 준비 중 and do nothing (§14.7: never shown as done). Companions are the characters with a moments file (Resources/Art/moments/<id>/
    // moments.json, imported from art/moments) or a still in Resources/Art/Characters/char_<id>_cutin. Bond progress is not built
    // yet, so bond scenes open as labelled previews; the lobby never invents lines or motion for a companion.
    public sealed class MemoryForgeLobbyClient : MonoBehaviour
    {
        public const string ExpeditionScene = "MemoryForgeExpedition", OwnedCanvas = "MemoryForgeLobbyCanvas";
        /// <summary>The expedition length chosen in 출격 준비, read once by the expedition client when it starts.</summary>
        public static string? NextLength;
        const float IdleAfter = 25f, FigureCenterX = 1040f;
        static readonly Color Background = Hex("0d1322"), Panel = Hex("141c31"), Line = Hex("2f3d63"), Ink = Hex("e9eef8"),
            Muted = Hex("98a5c0"), Brass = Hex("e2b45f"), Memory = Hex("62d6c6");

        public sealed class Companion { public string Id = "", Name = ""; public MomentFile? Moments; public Sprite? Still; }

        public IReadOnlyList<Companion> Companions => companions;
        public Companion? Current => companions.Count == 0 ? null : companions[index];
        public MemoryForgeMomentPlayer? Player => player;
        public IReadOnlyList<string> LoadErrors => loadErrors;

        readonly List<Companion> companions = new List<Companion>();
        readonly List<string> loadErrors = new List<string>();
        readonly Dictionary<string, Sprite?> sprites = new Dictionary<string, Sprite?>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        readonly HashSet<string> enteredFor = new HashSet<string>();
        Font font = null!;
        MemoryForgeMomentPlayer player = null!;
        RectTransform root = null!, listPanel = null!, sortiePanel = null!, settingsPanel = null!;
        string sortieLength = "short";
        CanvasGroup veil = null!;
        Coroutine? menuFade;
        bool leaving;
        Text nameText = null!, noteText = null!;
        int index, tapCursor;
        string? lastPeriod;
        float idleSince;

        void Start()
        {
            font = Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular");
            LoadCompanions();
            Build();
            Show(0);
        }

        void Update()
        {
            if (player.Playing) { idleSince = Time.time; return; }
            // A new part of the day plays that period's reaction once, when nothing else is playing.
            string period = TimeOfDay(DateTime.Now.Hour);
            if (period != lastPeriod) { lastPeriod = period; if (PlayFirst("time")) return; }
            if (Time.time - idleSince > IdleAfter) { idleSince = Time.time; PlayFirst("idle"); }
        }

        void LoadCompanions()
        {
            var names = new Dictionary<string, string>();
            var order = new List<string>();
            var content = Resources.Load<TextAsset>("MemoryForge/battle-content");
            if (content != null && JObject.Parse(content.text)["Battle"]?["Characters"] is JArray characters)
                foreach (var c in characters) if (c["Id"]?.ToString() is string id) { names[id] = c["Name"]?.ToString() ?? id; order.Add(id); }
            var files = new Dictionary<string, MomentFile>();
            foreach (var asset in Resources.LoadAll<TextAsset>("Art/moments").Where(a => a.name == "moments"))
            {
                var file = MomentFile.Parse(asset.text, out var error);
                if (file == null) loadErrors.Add(error ?? "unreadable"); else files[file.Character] = file;
            }
            foreach (var id in order.Concat(files.Keys.Except(order)))
            {
                var still = SpriteAt("Art/Characters/char_" + id + "_cutin");
                files.TryGetValue(id, out var moments);
                if (moments == null && still == null) continue;
                companions.Add(new Companion { Id = id, Name = names.TryGetValue(id, out var n) ? n : id, Moments = moments, Still = still });
            }
        }

        void Build()
        {
            var canvasObject = new GameObject(OwnedCanvas, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(MemoryForgeMomentPlayer.RefWidth, MemoryForgeMomentPlayer.RefHeight); scaler.matchWidthOrHeight = 0.5f;
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
            var canvas = (RectTransform)canvasObject.transform;
            root = Box(canvas, "LobbyFrame", 0, 0, 1600, 900, Background);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f); root.anchoredPosition = Vector2.zero;
            var backdrop = Box(root, "Backdrop", 0, 0, 1600, 900, Color.white);
            var bg = SpriteAt("Art/Backgrounds/bg_workshop");
            backdrop.GetComponent<Image>().sprite = bg; backdrop.GetComponent<Image>().color = bg == null ? Background : new Color(0.78f, 0.78f, 0.8f, 1f);

            // The figure tap area sits under the stage so the player's own tap catcher and skip button stay on top while playing.
            var tapArea = Button(root, "FigureTap", "", FigureCenterX - 330, 40, 660, 820, OnFigureTap, Color.clear);
            tapArea.transition = Selectable.Transition.None;
            var stage = Box(root, "Stage", 0, 0, 1600, 900, Color.clear);
            player = gameObject.AddComponent<MemoryForgeMomentPlayer>();
            // The menu hides during a scene, so the dialogue takes the free left side and never covers the figure or the props
            // at its feet (a chair, a table): the figure stands right of x=740 at rest.
            player.Build(stage, font, SpriteForArt, FigureCenterX, new Rect(40, 610, 600, 256));
            player.NameOf = id => companions.FirstOrDefault(c => c.Id == id)?.Name;

            // The home's controls in one group (LobbySide): the top bar and the menu tiles over the backdrop's left, clear of the
            // figure (x >= 710). While a moment plays they step back so the scene has the screen; they return when it ends.
            var side = Box(root, "LobbySide", 0, 0, 1600, 900, Color.clear);
            var sideGroup = side.gameObject.AddComponent<CanvasGroup>();
            player.Started += _ => { sideGroup.interactable = false; sideGroup.blocksRaycasts = false; FadeMenu(sideGroup, 0f, 0.25f); };
            player.Finished += _ => { idleSince = Time.time; sideGroup.interactable = true; sideGroup.blocksRaycasts = true; FadeMenu(sideGroup, 1f, 0.3f); };
            Box(side, "MenuShade", 0, 0, 700, 900, new Color(0.03f, 0.05f, 0.1f, 0.55f));
            var bar = Box(side, "TopBar", 0, 0, 1600, 76, new Color(0.03f, 0.05f, 0.1f, 0.62f));
            Box(bar, "TopEdge", 0, 74, 1600, 2, new Color(Brass.r, Brass.g, Brass.b, 0.45f));
            Label(bar, "기억 공방", 36, 12, 260, 52, 34, Brass, FontStyle.Bold);
            nameText = Label(bar, "", 300, 22, 360, 40, 26, Ink, FontStyle.Bold);
            Button(bar, "OpenSettings", "설정", 1330, 12, 116, 52, () => Toggle(settingsPanel), Panel, 22);
            if (!Application.isMobilePlatform) Button(bar, "QuitGame", "종료", 1458, 12, 116, 52, Application.Quit, Panel, 22);

            // 출격: the big tile; the rest in rows of two and three.
            var sortie = Tile(side, "StartExpedition", "출격", "1구역 · 종탑 하층 탐사", "ico_node_battle", 40, 120, 620, 176, Brass, () => Toggle(sortiePanel));
            Breathe(sortie);
            Tile(side, "NextCompanion", "동료", "다음 동료 보기", "ico_node_rest", 40, 316, 302, 124, Memory, () => Show(index + 1));
            Tile(side, "BondStories", "인연", "인연 이야기 미리보기", "ico_node_event", 358, 316, 302, 124, Memory, () => OpenList("bond"));
            Tile(side, "Staging", "연출", "등장·승리 연출 미리보기", "ico_node_archive", 40, 456, 302, 124, Memory, () => OpenList("staging"));
            Tile(side, "MemoryDeck", "기억 덱", "준비 중", "ico_node_forge", 358, 456, 302, 124, Line, null);
            Tile(side, "Story", "스토리", "준비 중", "ico_node_boss", 40, 596, 198, 110, Line, null);
            Tile(side, "Challenge", "도전", "준비 중", "ico_node_elite", 251, 596, 198, 110, Line, null);
            Tile(side, "Recruit", "모집", "준비 중", "ico_node_shop", 462, 596, 198, 110, Line, null);
            noteText = Label(side, "", 40, 724, 620, 60, 19, Muted);
            Label(side, "연습 빌드 · 인연 단계와 진행은 아직 저장되지 않아요.", 40, 846, 620, 34, 17, Muted);
            BuildSortie();
            BuildSettings();
            listPanel = Box(root, "SceneList", 460, 120, 560, 660, new Color(0.08f, 0.1f, 0.17f, 0.96f));
            listPanel.gameObject.SetActive(false);
            var veilBox = Box(root, "Veil", 0, 0, 1600, 900, Color.black); veilBox.GetComponent<Image>().raycastTarget = true; // blocks input while leaving
            veil = veilBox.gameObject.AddComponent<CanvasGroup>(); veil.blocksRaycasts = false;
            foreach (var b in root.GetComponentsInChildren<Button>(true)) if (b.name != "FigureTap" && b.name != "MomentTap") b.gameObject.AddComponent<UiPress>();
            // Entrance: the veil lifts, the side column slides in from the left.
            StartCoroutine(MemoryForgeUiMotion.Fade(veil, 1, 0, 0.45f));
            StartCoroutine(MemoryForgeUiMotion.SlideIn(side, new Vector2(-120, 0), 0.45f));
        }

        // Exit to the expedition behind a short fade so the scene change is not a hard cut.
        System.Collections.IEnumerator Leave()
        {
            if (leaving) yield break;
            leaving = true; veil.blocksRaycasts = true; player.Stop();
            yield return MemoryForgeUiMotion.Fade(veil, 0, 1, 0.3f);
            SceneManager.LoadScene(ExpeditionScene);
        }

        // A home tile: a dark glass panel with a coloured edge and a lit top line, its icon, title and one line under it. A tile with
        // no action is a system not built yet: dimmed, not a button, so it can never look done.
        RectTransform Tile(Transform parent, string name, string title, string sub, string icon, float x, float y, float w, float h, Color tone, Action? click)
        {
            bool live = click != null;
            RectTransform r;
            if (live) r = (RectTransform)Button(parent, name, "", x, y, w, h, click!, new Color(0.06f, 0.09f, 0.16f, 0.86f)).transform;
            else r = Box(parent, name, x, y, w, h, new Color(0.05f, 0.07f, 0.12f, 0.72f));
            var edge = r.gameObject.AddComponent<Outline>(); edge.effectColor = new Color(tone.r, tone.g, tone.b, live ? 0.85f : 0.35f); edge.effectDistance = new Vector2(2, -2);
            Box(r, "Crest", 0, 0, w, 4, new Color(tone.r, tone.g, tone.b, live ? 0.9f : 0.3f));
            float big = h >= 150 ? 76 : h >= 120 ? 54 : 44, pad = h >= 150 ? 26 : 18;
            var glyph = SpriteAt("Art/UI/" + icon);
            if (glyph != null) { var g = Box(r, "Icon", pad, (h - big) / 2, big, big, live ? tone : new Color(Muted.r, Muted.g, Muted.b, 0.6f)); g.GetComponent<Image>().sprite = glyph; g.GetComponent<Image>().preserveAspect = true; }
            float tx = pad + big + 16, tw = w - tx - 12;
            Label(r, title, tx, h >= 150 ? 30 : 16, tw, h >= 150 ? 64 : 40, h >= 150 ? 48 : 28, live ? Ink : Muted, FontStyle.Bold);
            Label(r, sub, tx, h >= 150 ? 100 : 58, tw, 34, h >= 150 ? 22 : 17, live ? (tone == Brass ? Brass : Muted) : new Color(Muted.r, Muted.g, Muted.b, 0.7f));
            return r;
        }
        // The 출격 tile breathes its edge so the way into a run is the first thing the eye finds (calm under reduced motion).
        void Breathe(RectTransform tile)
        {
            var edge = tile.GetComponent<Outline>(); if (edge == null) return;
            var pulse = tile.gameObject.AddComponent<UiPulse>(); pulse.Bind(edge, () => MemoryForgeUiMotion.Reduced);
        }
        void Toggle(RectTransform panel)
        {
            bool open = !panel.gameObject.activeSelf;
            sortiePanel.gameObject.SetActive(false); settingsPanel.gameObject.SetActive(false); listPanel.gameObject.SetActive(false);
            panel.gameObject.SetActive(open);
            if (open) StartCoroutine(MemoryForgeUiMotion.SlideIn(panel, new Vector2(0, -20), 0.2f));
        }

        // 출격 준비: the zone, the run's length and the party, then 출발. The practice party is fixed until 편성 is built.
        void BuildSortie()
        {
            sortiePanel = Box(root, "SortiePanel", 700, 110, 860, 640, new Color(0.05f, 0.07f, 0.13f, 0.96f));
            var edge = sortiePanel.gameObject.AddComponent<Outline>(); edge.effectColor = new Color(Brass.r, Brass.g, Brass.b, 0.8f); edge.effectDistance = new Vector2(2, -2);
            Label(sortiePanel, "출격 준비", 32, 22, 500, 52, 36, Brass, FontStyle.Bold);
            Button(sortiePanel, "CloseSortie", "닫기", 712, 24, 116, 52, () => sortiePanel.gameObject.SetActive(false), Line, 22);
            var zone = Box(sortiePanel, "Zone", 32, 92, 796, 150, Color.white);
            var map = SpriteAt("Art/Backgrounds/bg_zone1_map");
            zone.GetComponent<Image>().sprite = map; zone.GetComponent<Image>().color = map == null ? Panel : new Color(0.7f, 0.7f, 0.75f, 1f);
            Label(zone, "1구역 · 종탑 하층", 24, 20, 600, 50, 34, Ink, FontStyle.Bold);
            Label(zone, "7층과 보스 · 전투, 공방, 휴식, 상점, 사건", 24, 82, 700, 40, 21, Ink);
            Label(sortiePanel, "탐사 길이", 32, 262, 300, 36, 22, Muted);
            Text? shortText = null, regularText = null;
            Button? shortButton = null, regularButton = null;
            void Mark()
            {
                shortButton!.GetComponent<Image>().color = sortieLength == "short" ? Brass : Panel; shortText!.color = sortieLength == "short" ? Background : Ink;
                regularButton!.GetComponent<Image>().color = sortieLength == "full" ? Brass : Panel; regularText!.color = sortieLength == "full" ? Background : Ink;
            }
            shortButton = Button(sortiePanel, "Length:short", "", 32, 302, 390, 64, () => { sortieLength = "short"; Mark(); }, Brass, 22);
            shortText = Label(shortButton.transform, "짧은 탐사 · 1구역 · 약 15분", 6, 5, 378, 54, 22, Background, FontStyle.Normal, TextAnchor.MiddleCenter);
            regularButton = Button(sortiePanel, "Length:full", "", 438, 302, 390, 64, () => { sortieLength = "full"; Mark(); }, Panel, 22);
            regularText = Label(regularButton.transform, "긴 탐사 · 2구역까지 · 약 30분", 6, 5, 378, 54, 22, Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
            Label(sortiePanel, "파티", 32, 386, 300, 36, 22, Muted);
            string[] party = { "sera", "harin", "nia" };
            for (int i = 0; i < party.Length; i++)
            {
                var slot = Box(sortiePanel, "Party:" + party[i], 32 + i * 136, 424, 124, 124, new Color(0.1f, 0.13f, 0.22f, 1f));
                var art = SpriteAt("Art/Characters/char_" + party[i] + "_sd");
                if (art != null) { var face = Box(slot, "Face", 2, 2, 120, 120, Color.white); face.GetComponent<Image>().sprite = art; face.GetComponent<Image>().preserveAspect = true; }
                Label(slot, companions.FirstOrDefault(c => c.Id == party[i])?.Name ?? party[i], 0, 96, 124, 28, 18, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            }
            Label(sortiePanel, "편성은 준비 중이에요. 연습 탐사는 이 세 명으로 출발해요.", 456, 450, 372, 80, 18, Muted);
            Button(sortiePanel, "Depart", "출발", 528, 560, 300, 64, () => { NextLength = sortieLength; StartCoroutine(Leave()); }, Brass, 30);
            sortiePanel.gameObject.SetActive(false);
        }

        // 설정: reduced motion everywhere; on a PC also the window mode.
        void BuildSettings()
        {
            settingsPanel = Box(root, "SettingsPanel", 1100, 84, 476, Application.isMobilePlatform ? 196 : 270, new Color(0.05f, 0.07f, 0.13f, 0.96f));
            var edge = settingsPanel.gameObject.AddComponent<Outline>(); edge.effectColor = new Color(Line.r, Line.g, Line.b, 1f); edge.effectDistance = new Vector2(2, -2);
            Label(settingsPanel, "설정", 24, 18, 300, 44, 28, Ink, FontStyle.Bold);
            Text? motionLabel = null;
            var motionToggle = Button(settingsPanel, "ReduceMotion", "", 24, 76, 428, 56, () => { MemoryForgeUiMotion.Reduced = !MemoryForgeUiMotion.Reduced; motionLabel!.text = MotionCaption(); }, Panel, 20);
            motionLabel = Label(motionToggle.transform, MotionCaption(), 6, 5, 416, 46, 20, Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
            if (!Application.isMobilePlatform)
            {
                Text? windowLabel = null;
                var window = Button(settingsPanel, "WindowMode", "", 24, 146, 428, 56, () =>
                {
                    Screen.fullScreenMode = Screen.fullScreenMode == FullScreenMode.Windowed ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                    windowLabel!.text = WindowCaption(Screen.fullScreenMode != FullScreenMode.Windowed);
                }, Panel, 20);
                windowLabel = Label(window.transform, WindowCaption(Screen.fullScreenMode != FullScreenMode.Windowed), 6, 5, 416, 46, 20, Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
            }
            settingsPanel.gameObject.SetActive(false);
        }
        // The caption shows the state the press switches to's opposite: the current mode.
        static string WindowCaption(bool fullScreen) => "화면: " + (fullScreen ? "전체 화면" : "창 모드") + " (눌러서 바꾸기)";

        // One menu fade at a time: a newer fade cancels the older one, so a quick skip-and-replay never leaves the menu visible.
        void FadeMenu(CanvasGroup group, float to, float duration)
        {
            if (menuFade != null) StopCoroutine(menuFade);
            menuFade = StartCoroutine(MemoryForgeUiMotion.Fade(group, group.alpha, to, duration));
        }

        public void Show(int i)
        {
            if (companions.Count == 0) { nameText.text = "동료 없음"; noteText.text = "표시할 캐릭터 그림이 아직 없어요."; return; }
            index = ((i % companions.Count) + companions.Count) % companions.Count;
            listPanel.gameObject.SetActive(false);
            var c = companions[index];
            nameText.text = c.Name;
            if (c.Moments != null) player.Bind(c.Moments); else player.ShowStill(c.Still);
            StillFrame(c.Moments == null);
            int scenes = c.Moments?.Scenes.Count ?? 0;
            noteText.text = scenes == 0 ? "이 동료의 로비 장면은 아직 제작 중이에요." : (c.Moments!.Status == "approved" ? "" : "초안 장면 · ") + "장면 " + scenes + "개";
            tapCursor = 0; idleSince = Time.time;
            lastPeriod = TimeOfDay(DateTime.Now.Hour);
            if (enteredFor.Add(c.Id) && !PlayFirst("enter")) PlayFirst("time");
        }

        // A still is card art with its own backdrop; a thin brass edge and shadow make it read as a framed card, not a cut-out.
        void StillFrame(bool on)
        {
            var go = player.FigureImage.gameObject;
            var edge = go.GetComponent<Outline>(); var drop = go.GetComponents<Shadow>().FirstOrDefault(x => !(x is Outline));
            if (!on) { if (edge != null) Destroy(edge); if (drop != null) Destroy(drop); return; }
            if (drop == null) { drop = go.AddComponent<Shadow>(); drop.effectColor = new Color(0, 0, 0, 0.55f); drop.effectDistance = new Vector2(10, -12); }
            if (edge == null) { edge = go.AddComponent<Outline>(); edge.effectColor = new Color(Brass.r, Brass.g, Brass.b, 0.9f); edge.effectDistance = new Vector2(3, -3); }
        }

        void OnFigureTap()
        {
            if (player.Playing) return; // taps during a scene belong to the player, never start another reaction
            var scenes = LobbyScenes("tap").ToList();
            if (scenes.Count == 0) return;
            for (int n = 0; n < scenes.Count; n++)
            {
                var s = scenes[(tapCursor + n) % scenes.Count];
                if (!Ready(s)) continue;
                tapCursor = (tapCursor + n + 1) % scenes.Count;
                Play(s); return;
            }
        }

        // Lobby reactions for a trigger; "time" also matches the local time of day.
        IEnumerable<MomentScene> LobbyScenes(string on)
        {
            var m = Current?.Moments;
            if (m == null) return Enumerable.Empty<MomentScene>();
            return m.Scenes.Where(s => s.Kind == "lobby" && s.Trigger.On == on && (on != "time" || s.Trigger.Time == null || s.Trigger.Time == TimeOfDay(DateTime.Now.Hour)));
        }

        bool PlayFirst(string on)
        {
            var scene = LobbyScenes(on).FirstOrDefault(Ready);
            if (scene == null) return false;
            Play(scene); return true;
        }

        bool Ready(MomentScene s) =>
            !lastPlayed.TryGetValue(s.Id, out var at) || (s.Input.Repeatable && Time.time - at >= s.Input.CooldownS);

        public void Play(MomentScene scene)
        {
            // A moment takes the whole screen: every home panel closes, so none stays live over the scene.
            listPanel.gameObject.SetActive(false); sortiePanel.gameObject.SetActive(false); settingsPanel.gameObject.SetActive(false);
            lastPlayed[scene.Id] = Time.time;
            player.Play(scene);
        }

        public static string TimeOfDay(int hour) => hour < 5 ? "night" : hour < 11 ? "morning" : hour < 17 ? "day" : hour < 21 ? "evening" : "night";

        // Bond stories, or entry/victory/skin staging, as a list. Preview policy: these rows open the scene directly, ignoring
        // bond level, event triggers, repeat and cooldown, because bond progress and battle events are not connected yet; each
        // row says so. Lobby reactions (tap, enter, idle, time) always go through Ready().
        void OpenList(string which)
        {
            foreach (Transform child in listPanel) Destroy(child.gameObject);
            var m = Current?.Moments;
            var scenes = m == null ? new List<MomentScene>() : m.Scenes.Where(s => which == "bond" ? s.Kind == "bond" : s.Kind == "entry" || s.Kind == "victory" || s.Kind == "skin").ToList();
            Label(listPanel, which == "bond" ? "인연 이야기" : "등장·승리 연출", 28, 22, 420, 46, 30, Brass, FontStyle.Bold);
            Button(listPanel, "CloseList", "닫기", 420, 22, 112, 48, () => listPanel.gameObject.SetActive(false), Line, 22);
            if (scenes.Count == 0) Label(listPanel, "아직 준비된 장면이 없어요.", 28, 96, 500, 40, 22, Muted);
            float y = 92;
            foreach (var s in scenes)
            {
                var row = Button(listPanel, "Scene:" + s.Id, "", 28, y, 504, 92, () => Play(s), Panel);
                Label(row.transform, s.Title.Length == 0 ? s.Id : s.Title, 18, 10, 470, 38, 24, Ink, FontStyle.Bold);
                Label(row.transform, Condition(s), 18, 52, 470, 30, 18, Memory);
                y += 104;
            }
            foreach (var b in listPanel.GetComponentsInChildren<Button>(true)) b.gameObject.AddComponent<UiPress>();
            listPanel.gameObject.SetActive(true);
            var group = listPanel.GetComponent<CanvasGroup>(); if (group == null) group = listPanel.gameObject.AddComponent<CanvasGroup>();
            StartCoroutine(MemoryForgeUiMotion.Fade(group, 0, 1, 0.2f));
            StartCoroutine(MemoryForgeUiMotion.SlideIn(listPanel, new Vector2(0, -24), 0.2f));
        }

        static string MotionCaption() => "움직임 줄이기: " + (MemoryForgeUiMotion.Reduced ? "켬" : "끔");

        static string Condition(MomentScene s)
        {
            string kind = s.Kind switch { "bond" => "인연", "entry" => "등장", "victory" => "승리", "skin" => "스킨", _ => s.Kind };
            string level = s.Trigger.BondLevelMin is int lv ? " · 인연 Lv " + lv : "";
            int ready = s.Assets.Count(a => a.Status == "ready");
            string assets = s.Assets.Count == 0 ? "" : " · 자산 " + ready + "/" + s.Assets.Count;
            return kind + level + " · 미리보기" + assets;
        }

        Sprite? SpriteForArt(string artPath) => SpriteAt("Art/" + MemoryForgeMomentPlayer.StripExtension(artPath));

        Sprite? SpriteAt(string resourcePath)
        {
            if (sprites.TryGetValue(resourcePath, out var cached)) return cached;
            var texture = Resources.Load<Texture2D>(resourcePath);
            var sprite = texture == null ? null : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprites[resourcePath] = sprite;
            return sprite;
        }

        // ── UI helpers (1600x900 reference, top-left coordinates; same conventions as the expedition client) ──
        static RectTransform Box(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false);
            var r = (RectTransform)obj.transform; r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height);
            var image = obj.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return r;
        }
        Text Label(Transform parent, string value, float x, float y, float width, float height, int size, Color color, FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.UpperLeft)
        {
            var obj = new GameObject("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            var r = (RectTransform)obj.transform; r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height);
            var text = obj.GetComponent<Text>(); text.font = font; text.text = value; text.fontSize = size; text.color = color; text.fontStyle = style; text.alignment = align; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = Math.Max(14, (int)(size * 0.7f)); text.resizeTextMaxSize = size; return text;
        }
        Button Button(Transform parent, string name, string caption, float x, float y, float width, float height, Action click, Color color, int size = 24)
        {
            var r = Box(parent, name, x, y, width, height, color); var image = r.GetComponent<Image>(); image.raycastTarget = true;
            var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.onClick.AddListener(() => { if (button != null && button.IsActive() && button.IsInteractable()) click(); });
            var colors = button.colors; colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f); colors.pressedColor = new Color(0.78f, 0.78f, 0.78f); button.colors = colors;
            if (caption.Length != 0) Label(r, caption, 6, 5, width - 12, height - 10, size, color == Brass || color == Memory ? Background : Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
            return button;
        }
        static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
    }
}
