#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using MemoryForge.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    // The home (HOME-HUB-20261021: the game opens here, a subculture home, and the roguelike expedition is entered from 출격): one
    // companion on the workshop backdrop with their moments (tap, enter, idle and time-of-day reactions), a top bar, companion catalogue/details and navigation over
    // the backdrop (출격 opens the expedition preparation: zone, length, party), a bond and staging list, and the settings. Tiles of
    // systems that are not built yet say 준비 중 and do nothing (§14.7: never shown as done). Companions are the characters with a moments file (Resources/Art/moments/<id>/
    // moments.json, imported from art/moments) or a still in Resources/Art/Characters/char_<id>_cutin. Bond progress is not built
    // yet, so bond scenes open as labelled previews; the lobby never invents lines or motion for a companion.
    public sealed partial class MemoryForgeLobbyClient : MonoBehaviour
    {
        public const string ExpeditionScene = "MemoryForgeExpedition", OwnedCanvas = "MemoryForgeLobbyCanvas";
        /// <summary>The expedition length chosen in 출격 준비, read once by the expedition client when it starts.</summary>
        public static string? NextLength;
        internal static string? ReturnCompanion;
        public Action<string>? SceneLoader { get; set; }
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
        RectTransform rosterPanel = null!, detailPanel = null!, stage = null!;
        RectTransform modalShield = null!;
        CanvasGroup homeControls = null!;
        Button figureTap = null!;
        ClientBattleContent content = null!;
        Func<ClientBattleContent> contentLoader = ClientBattleContent.Load;
        bool contentUnavailable;
        string catalogueFilter = "all", detailId = "";
        static IReadOnlyList<string> PracticeParty => MemoryForgeExpeditionClient.ReferenceParty;
        string sortieLength = "short";
        CanvasGroup veil = null!;
        Coroutine? menuFade;
        bool leaving;
        Text nameText = null!, noteText = null!, homeAccountText = null!;
        int index, tapCursor;
        Vector3 layoutCalibration;
        bool homeEventsBound, detailCards;
        RectTransform? homeRightShade;
        string sceneListKind = "bond";
        string? lastPeriod;
        float idleSince;

        void Start()
        {
            font = Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular");
            try { LoadCompanions(); }
            catch (System.IO.InvalidDataException e)
            {
                loadErrors.Add(e.Message); ShowLoadError(); return;
            }
            string? returned = MemoryForgePracticeSession.ConsumeReturnNotice();
            sortieLength = MemoryForgePracticeSession.HeldLength ?? sortieLength;
            int representative = Math.Max(0, companions.FindIndex(companion => companion.Id == ReturnCompanion));
            ReturnCompanion = null;
            if (MemoryForgePracticeSession.HasHeldPractice && companions.Count != 0) enteredFor.Add(companions[representative].Id);
            Build();
            Show(representative);
            if (!string.IsNullOrEmpty(returned)) ShowHomeNotice(returned!);
            RestoreAccountConnection();
        }

        void Update()
        {
            if (root != null && homeControls != null && layoutCalibration != MemoryForgeUiViewport.Calibration) ReflowLayout();
            FitHome();
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (recruitmentReveal != null && recruitmentReveal.gameObject.activeSelf) FinishRecruitmentReveal();
                else if (detailPanel.gameObject.activeSelf) BackToRoster();
                else ClosePanels();
            }
            if (rosterPanel.gameObject.activeSelf || detailPanel.gameObject.activeSelf || sortiePanel.gameObject.activeSelf || settingsPanel.gameObject.activeSelf || listPanel.gameObject.activeSelf || recruitmentPanel.gameObject.activeSelf || recruitmentReveal.gameObject.activeSelf || growthPanel.gameObject.activeSelf || equipmentPanel != null && equipmentPanel.gameObject.activeSelf)
            { idleSince = Time.time; return; }
            if (player.Playing) { idleSince = Time.time; return; }
            // A new part of the day plays that period's reaction once, when nothing else is playing.
            string period = TimeOfDay(DateTime.Now.Hour);
            if (period != lastPeriod) { lastPeriod = period; if (PlayFirst("time")) return; }
            if (Time.time - idleSince > IdleAfter) { idleSince = Time.time; PlayFirst("idle"); }
        }

        void LoadCompanions()
        {
            content = contentLoader();
            var names = new Dictionary<string, string>();
            var order = new List<string>();
            foreach (var c in content.Battle.Characters) { names[c.Id] = c.Name; order.Add(c.Id); }
            var files = new Dictionary<string, MomentFile>();
            foreach (var asset in Resources.LoadAll<TextAsset>("Art/moments").Where(a => a.name == "moments"))
            {
                var file = MomentFile.Parse(asset.text, out var error);
                if (file == null) loadErrors.Add(error ?? "unreadable"); else files[file.Character] = file;
            }
            foreach (var id in order.Concat(files.Keys.Except(order)))
            {
                var still = SpriteAt("Art/Characters/char_" + id + "_cutin") ?? SpriteAt("Art/Characters/char_" + id + "_sd");
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
            // The scenery fills a wide phone; only interactive content is fitted to its safe area.
            var scenery = Box(canvas, "FullBleedBackdrop", 0, 0, 1600, 900, new Color(0.78f, 0.78f, 0.8f));
            scenery.anchorMin = Vector2.zero; scenery.anchorMax = Vector2.one;
            scenery.offsetMin = scenery.offsetMax = Vector2.zero;
            scenery.GetComponent<Image>().sprite = SpriteAt("Art/Backgrounds/bg_workshop"); MemoryForgeUiViewport.Cover(scenery.GetComponent<Image>());
            root = Box(canvas, "LobbyFrame", 0, 0, 1600, 900, Color.clear);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f); root.anchoredPosition = Vector2.zero; root.sizeDelta = new Vector2(1600, 900); FitHome();


            // The figure tap area sits under the stage so the player's own tap catcher and skip button stay on top while playing.
            var tapArea = Button(root, "FigureTap", "", FigureCenterX - 670, 40, 660, 730, OnFigureTap, Color.clear);
            var tapRect = (RectTransform)tapArea.transform; tapRect.anchorMin = tapRect.anchorMax = new Vector2(0.5f, 1); tapRect.anchoredPosition = new Vector2(FigureCenterX - 670 - 800, -40);
            figureTap = tapArea;
            tapArea.transition = Selectable.Transition.None;
            stage = Box(root, "Stage", -340, 0, 1600, 900, Color.clear);
            MemoryForgeUiViewport.ArtIsland(stage, -340);
            player = gameObject.AddComponent<MemoryForgeMomentPlayer>();
            // The menu hides during a scene, so the dialogue takes the free left side and never covers the figure or the props
            // at its feet (a chair, a table): the figure stands right of x=740 at rest.
            player.Build(stage, font, SpriteForArt, FigureCenterX, new Rect(40, 610, 600, 256));
            player.NameOf = id => companions.FirstOrDefault(c => c.Id == id)?.Name;

            // The home's controls in one group (LobbySide): the top bar and the menu tiles over the backdrop's left, clear of the
            // figure (x >= 710). While a moment plays they step back so the scene has the screen; they return when it ends.
            BuildHomeControls();
            modalShield = (RectTransform)Button(root, "ModalShield", "", 0, 0, 1600, 900, ClosePanels, new Color(0, 0, 0, 0.45f)).transform;
            var shieldNavigation = modalShield.GetComponent<Button>().navigation; shieldNavigation.mode = Navigation.Mode.None;
            modalShield.GetComponent<Button>().navigation = shieldNavigation; modalShield.gameObject.SetActive(false);
            BuildSortie();
            BuildSettings();
            BuildRoster();
            listPanel = Box(root, "SceneList", 428, 80, 744, 736, new Color(0.08f, 0.1f, 0.17f, 0.96f));
            listPanel.gameObject.SetActive(false);
            BuildRecruitment();
            BuildGrowth();
            var veilBox = Box(root, "Veil", 0, 0, 1600, 900, Color.black); veilBox.GetComponent<Image>().raycastTarget = true; // blocks input while leaving
            veil = veilBox.gameObject.AddComponent<CanvasGroup>(); veil.blocksRaycasts = false;
            foreach (var b in root.GetComponentsInChildren<Button>(true)) if (b.name != "FigureTap" && b.name != "MomentTap" && b.name != "ModalShield" && b.GetComponent<UiPress>() == null) b.gameObject.AddComponent<UiPress>();
            FitHome();
            // Entrance: the veil lifts, the side column slides in from the left.
            StartCoroutine(MemoryForgeUiMotion.Fade(veil, 1, 0, 0.45f));
            StartCoroutine(MemoryForgeUiMotion.SlideIn((RectTransform)homeControls.transform, new Vector2(-120, 0), 0.45f));
        }

        // Rebuild only UI groups. The existing Moment player, stage, coroutine and session stay alive.
        void BuildHomeControls()
        {
            layoutCalibration = MemoryForgeUiViewport.Calibration;
            var side = Box(root, "LobbySide", 0, 0, 1600, 900, Color.clear);
            var sideGroup = side.gameObject.AddComponent<CanvasGroup>();
            homeControls = sideGroup;
            if (!homeEventsBound)
            {
                homeEventsBound = true;
                player.Started += _ => { MemoryForgeUiViewport.ArtIsland(stage); SetHomeInput(false); FadeMenu(homeControls, 0f, 0.25f); };
                player.Finished += _ => { MemoryForgeUiViewport.ArtIsland(stage, -340); idleSince = Time.time; SetHomeInput(true); FadeMenu(homeControls, 1f, 0.3f); };
            }
            MemoryForgeUiViewport.BleedX(Shade(side, "TopShade", 0, 0, 1600, 180, new Color(0.025f, 0.035f, 0.07f, 0.8f), false));
            homeRightShade = Shade(side, "RightShade", 1016, 0, 584, 900, new Color(0.025f, 0.035f, 0.07f, 0.88f), false);
            Shade(side, "IdentityShade", 32, 24, 620, 150, new Color(0.025f, 0.035f, 0.07f, 0.8f), true);
            var homeHeading = Label(side, "기억 공방", 56, 24, 480, 64, 42, Ink, FontStyle.Bold);
            Ornament(side, 58, 24 + homeHeading.rectTransform.rect.height + 16, 310);
            float utilitySize = Math.Max(112, TouchUnits());
            UtilityButton(side, "OpenSettings", "설정", 1544 - utilitySize, utilitySize, () => Toggle(settingsPanel), true);
            if (!Application.isMobilePlatform) UtilityButton(side, "QuitGame", "종료", 1520 - utilitySize * 2, utilitySize, Application.Quit, false);
            var account = Box(side, "HomeAccount", 1040, 24, 224, Math.Max(160, utilitySize), Color.clear);
            homeAccountText = Label(account, "계정 미연결\n모집에서 연결", 8, 8, 208, Math.Max(144, utilitySize - 16), 18, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);

            float dockHeight = Math.Max(120, TouchUnits()), dockY = 884 - dockHeight;
            float entryHeight = Math.Max(144, TouchUnits()), entryY = Math.Min(632, dockY - entryHeight - 16);
            float destinationY = Math.Max(224, 24 + utilitySize + 96 + 16), destinationHeight = entryY - destinationY - 16;
            var destination = Box(side, "ExpeditionDestination", 1040, destinationY, 504, destinationHeight, Panel);
            var map = SpriteAt("Art/Backgrounds/bg_zone1_map");
            float mapHeight = Math.Max(80, Math.Min(120, destinationHeight - 312));
            var vista = Box(destination, "DestinationArt", 0, 0, 504, mapHeight, Color.white);
            vista.GetComponent<Image>().sprite = map;
            vista.GetComponent<Image>().color = map == null ? Panel : new Color(0.75f, 0.8f, 0.85f);
            Box(destination, "DestinationShade", 0, Math.Max(0, mapHeight - 92), 504, Math.Min(92, mapHeight), new Color(0.025f, 0.035f, 0.07f, 0.85f));
            Box(destination, "BrassEdge", 0, 0, 4, destinationHeight, Brass);
            var destinationTitle = Label(destination, "종탑 하층", 22, 12, 456, 64, 36, Ink, FontStyle.Bold);
            float destinationTextY = 12 + destinationTitle.rectTransform.rect.height + 12;
            homePartyCaption = Label(destination, "연습 동행 3명", 24, destinationTextY, 456, 48, 18, Brass);
            homePartyStrip = Box(destination, "HomePartyStrip", 24, destinationTextY + homePartyCaption.rectTransform.rect.height + 8, 456, 80, Color.clear);
            for (int n = 0; n < PracticeParty.Count; n++)
            {
                string id = PracticeParty[n];
                var slot = Box(homePartyStrip, "PreviewParty:" + id, n * 152, 0, 136, 80, new Color(0.04f, 0.055f, 0.1f, 0.9f));
                var face = Box(slot, "Portrait", 4, 16, 48, 48, Color.white);
                var art = SpriteAt("Art/Characters/char_" + id + "_sd") ?? Portrait(id);
                face.GetComponent<Image>().sprite = art; CenterSprite(face);
                if (art == null) face.GetComponent<Image>().color = Color.clear;
                Label(slot, content?.Battle.Characters.FirstOrDefault(c => c.Id == id)?.Name ?? id, 56, 0, 76, 80, 18, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            }
            var sortie = MainEntry(side, "StartExpedition", "출격", "탐사 준비", "nav_expedition", 1040, entryY, 504, entryHeight, OpenSortie);
            sortie.anchorMin = new Vector2(sortie.anchorMin.x, 0); sortie.anchorMax = new Vector2(sortie.anchorMax.x, 0);
            sortie.anchoredPosition = new Vector2(sortie.anchoredPosition.x, 900 - entryY);
            destination.anchorMin = new Vector2(destination.anchorMin.x, 0); destination.anchorMax = new Vector2(destination.anchorMax.x, 1);
            destination.sizeDelta = new Vector2(destination.sizeDelta.x, destinationHeight - 900);
            Breathe(sortie);

            var routes = new (string Name, string Title, string Detail, string Icon, Action Click)[] {
                ("HomeCatalogueShortcut", "동료 기록", content == null ? "동료 자료 확인 중" : "전체 " + content.Battle.Characters.Count + "명의 동료", "nav_companions", OpenRoster),
                ("HomeRecruitmentShortcut", "새로운 만남", "모집과 보유 기록", "nav_recruit", OpenRecruitment) };
            float routeHeight = Math.Max(160, TouchUnits());
            for (int n = 0; n < routes.Length; n++)
            {
                var route = routes[n];
                var tile = Button(side, route.Name, "", 56, 208 + n * (routeHeight + 16), 416, routeHeight, route.Click, Line);
                Glyph(tile.transform, route.Icon, 18, (routeHeight - 76) / 2, 76, Color.white);
                Label(tile.transform, route.Title, 104, 24, 296, routeHeight - 48, 25, Ink, FontStyle.Bold, TextAnchor.MiddleLeft);
            }
            var identity = Box(side, "CompanionIdentity", 56, dockY - 208, 520, 192, Color.clear);
            identity.anchorMin = new Vector2(identity.anchorMin.x, 0); identity.anchorMax = new Vector2(identity.anchorMax.x, 0);
            identity.anchoredPosition = new Vector2(identity.anchoredPosition.x, 900 - (dockY - 208));
            Shade(identity, "NameShade", 0, 0, 520, 192, new Color(0.025f, 0.035f, 0.07f, 0.85f), true);
            nameText = Label(identity, "", 0, 8, 340, 96, 46, Ink, FontStyle.Bold);
            noteText = Label(identity, "", 0, 112, 340, 64, 19, Muted);
            Button(identity, "NextCompanion", "교체", 356, 8, 128, 120, () => Show(index + 1), Line, 22);

            var dock = Box(side, "HomeDock", 40, dockY, 1520, dockHeight, new Color(0.04f, 0.055f, 0.1f, 0.94f));
            Box(dock, "DockRule", 0, 0, 1520, 2, new Color(Brass.r, Brass.g, Brass.b, 0.65f));
            var entries = new (string Name, string Title, string Icon, Action Click)[] {
                ("Home", "홈", "nav_home", ClosePanels),
                ("OpenCompanions", "동료", "nav_companions", OpenRoster),
                ("OpenRecruitment", "모집", "nav_recruit", OpenRecruitment),
                ("OpenGrowth", "육성", "nav_companions", OpenGrowth),
                ("BondStories", "인연", "nav_bond", () => OpenList("bond")),
                ("Staging", "연출", "nav_moments", () => OpenList("staging")) };
            for (int n = 0; n < entries.Length; n++)
            {
                var e = entries[n];
                float step = 1496f / entries.Length;
                var tab = Button(dock, e.Name, "", 12 + n * step, 0, step - 12, dockHeight, e.Click, Color.clear);
                var tabRect = (RectTransform)tab.transform; tabRect.anchorMin = new Vector2((float)n / entries.Length, 0); tabRect.anchorMax = new Vector2((float)(n + 1) / entries.Length, 1); tabRect.offsetMin = new Vector2(12, 0); tabRect.offsetMax = new Vector2(0, 0);
                Glyph(tab.transform, e.Icon, 12, (dockHeight - 72) / 2, 72, Color.white);
                Label(tab.transform, e.Title, 96, (dockHeight - 58) / 2, step - 116, 58, 27, n == 0 ? Brass : Ink, FontStyle.Bold, TextAnchor.MiddleLeft);
            }
        }

        void ReflowLayout()
        {
            int sibling = homeControls.transform.GetSiblingIndex();
            string companionName = nameText.text, companionNote = noteText.text;
            var previous = homeControls.gameObject; string? returnNotice = previous.transform.Find("HomeNotice")?.GetComponentInChildren<Text>()?.text; previous.SetActive(false); Destroy(previous);
            BuildHomeControls(); homeControls.transform.SetSiblingIndex(sibling);
            nameText.text = companionName; noteText.text = companionNote;
            MemoryForgeUiViewport.WholeText(nameText); MemoryForgeUiViewport.WholeText(noteText);
            RefreshHomeAccount();
            if (!string.IsNullOrEmpty(returnNotice)) ShowHomeNotice(returnNotice!);
            if (player.Playing) homeControls.alpha = 0;
            SetHomeInput(!player.Playing && !modalShield.gameObject.activeSelf);
            player.ReflowControls();
            if (listPanel.gameObject.activeSelf) BuildSceneList(sceneListKind);
            if (rosterPanel.gameObject.activeSelf) RenderRoster();
            if (detailPanel.gameObject.activeSelf) RenderDetails(detailCards);
            if (recruitmentPanel.gameObject.activeSelf) { if (recruitmentSession == null && recruitmentPanel.GetComponentInChildren<InputField>() != null) ReflowRecruitmentConnection(); else RenderRecruitment(); }
            if (recruitmentReveal.gameObject.activeSelf) ReflowRecruitmentReveal();
            if (settingsPanel.gameObject.activeSelf)
            {
                var old = settingsPanel; int order = old.GetSiblingIndex(); old.gameObject.SetActive(false); Destroy(old.gameObject);
                BuildSettings(); settingsPanel.SetSiblingIndex(order); settingsPanel.gameObject.SetActive(true);
            }
            if (recruitmentSession != null) RepaintAccountPanels();
            else if (sortiePanel.gameObject.activeSelf)
            {
                int order = sortiePanel.GetSiblingIndex();
                BuildSortie(); sortiePanel.SetSiblingIndex(order); sortiePanel.gameObject.SetActive(true);
            }
        }

        // Exit to the expedition behind a short fade so the scene change is not a hard cut.
        System.Collections.IEnumerator Leave(string destination = ExpeditionScene)
        {
            if (leaving) yield break;
            leaving = true; veil.blocksRaycasts = true; player.Stop();
            yield return MemoryForgeUiMotion.Fade(veil, 0, 1, 0.25f);
            string? previous = ReturnCompanion;
            ReturnCompanion = Current?.Id;
            try { (SceneLoader ?? (scene => SceneManager.LoadScene(scene)))(destination); }
            catch (Exception error)
            {
                ReturnCompanion = previous; NextLength = null; leaving = false;
                ClosePanels(); veil.blocksRaycasts = false;
                StartCoroutine(MemoryForgeUiMotion.Fade(veil, 1, 0, 0.2f));
                ShowHomeNotice("출발하지 못했어요. 기억은 그대로이니 다시 시도해주세요.");
                Debug.LogError("[MemoryForge] Workshop departure failed (" + error.GetType().Name + ")");
            }
        }

        void ShowHomeNotice(string message)
        {
            var previous = homeControls.transform.Find("HomeNotice");
            if (previous != null) Destroy(previous.gameObject);
            var back = Box(homeControls.transform, "HomeNotice", 46, 164, 544, 66, new Color(0.04f, 0.06f, 0.11f, 0.96f));
            var line = Label(back, message, 12, 6, 520, 54, 20, Memory);
            line.gameObject.name = "HomeReturnNotice"; line.resizeTextMinSize = 16;
        }

        void UtilityButton(Transform parent, string name, string caption, float x, float size, Action click, bool settings)
        {
            var button = Button(parent, name, "", x, 24, size, size + 96, click, Color.clear);
            var face = Box(button.transform, "Face", 0, 0, size, size, Hex("111b2a"));
            face.GetComponent<Image>().sprite = MemoryForgeExpeditionClient.Pill(0);
            button.targetGraphic = face.GetComponent<Image>();
            Kit(button.transform, "BrassRim", "ui_portrait_ring", Vector4.zero, 0, 0, size, size, 160);
            if (settings) Glyph(button.transform, "ico_node_forge", (size - 44) / 2, (size - 44) / 2, 44, Ink);
            else
            {
                var icon = Box(button.transform, "Power", (size - 44) / 2, (size - 44) / 2, 44, 44, Color.clear);
                var arc = Box(icon, "Arc", 0, 0, 44, 44, Ink);
                arc.anchorMin = arc.anchorMax = new Vector2(0, 1); arc.sizeDelta = new Vector2(44, 44);
                arc.pivot = new Vector2(0.5f, 0.5f); arc.anchoredPosition = new Vector2(22, -22); arc.localRotation = Quaternion.Euler(0, 0, -32.4f);
                var image = arc.GetComponent<Image>(); image.sprite = MemoryForgeExpeditionClient.Pill(3);
                image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Radial360;
                image.fillOrigin = (int)Image.Origin360.Top; image.fillClockwise = true; image.fillAmount = 0.82f;
                Box(icon, "Stem", 20, -2, 4, 24, Ink);
            }
            Label(button.transform, caption, -8, size + 4, size + 16, 80, 22, Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
        }

        // 출격: a framed glass band (the kit's ornate panel frame) lit brass from the left, its glyph in the kit's ready ring, the title,
        // the zone under it and a chevron at the right end.
        RectTransform MainEntry(Transform parent, string name, string title, string sub, string icon, float x, float y, float w, float h, Action click)
        {
            var r = (RectTransform)Button(parent, name, "", x, y, w, h, click, Color.clear).transform;
            Shade(r, "Glow", 0, 0, w, h, new Color(Brass.r * 0.45f, Brass.g * 0.32f, Brass.b * 0.12f, 0.9f), true);
            Kit(r, "Frame", "ui_panel_frame", new Vector4(48, 48, 48, 48), 0, 0, w, h, 128);
            var edge = r.gameObject.AddComponent<Outline>(); edge.effectColor = new Color(Brass.r, Brass.g, Brass.b, 0.55f); edge.effectDistance = new Vector2(1, -1);
            var ring = Kit(r, "Ring", "ui_portrait_ring_ready", Vector4.zero, 24, (h - 108) / 2, 108, 108, 160);
            Glyph(ring, icon, 12, 12, 84, Brass);
            Label(r, title, 152, 16, w - 224, h - 32, 54, Ink, FontStyle.Bold, TextAnchor.MiddleLeft);
            Label(r, "›", w - 70, (h - 90) / 2, 50, 90, 72, Brass, FontStyle.Bold, TextAnchor.MiddleCenter);
            return r;
        }
        // A vertical scroll band (clamped, masked) and its content, for rows taller than their room.
        RectTransform ScrollArea(Transform parent, float x, float y, float w, float h, float contentHeight)
        {
            var view = Box(parent, "MenuScroll", x, y, w, h, new Color(0, 0, 0, 0.001f)); view.GetComponent<Image>().raycastTarget = true;
            view.gameObject.AddComponent<RectMask2D>();
            if (h >= 160) MemoryForgeUiViewport.StretchY(view, y, MemoryForgeUiViewport.ReferenceSize((RectTransform)parent).y - y - h);
            var content = Box(view, "Content", 0, 0, w, contentHeight, Color.clear);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0, 1); content.sizeDelta = new Vector2(0, contentHeight); content.anchoredPosition = Vector2.zero;
            var scroll = view.gameObject.AddComponent<ScrollRect>(); scroll.viewport = view; scroll.content = content; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            {
                var track = Box(view, "ScrollPosition", w - 10, 4, 6, h - 8, Line);
                var handle = Box(track, "Handle", 0, 0, 6, h - 8, Brass);
                var indicator = track.gameObject.AddComponent<Scrollbar>(); indicator.handleRect = handle; indicator.targetGraphic = handle.GetComponent<Image>();
                indicator.direction = Scrollbar.Direction.BottomToTop; indicator.interactable = false; indicator.transition = Selectable.Transition.None;
                indicator.navigation = new Navigation { mode = Navigation.Mode.None };
                // The whole scroll area is the input target; this narrow bar only shows position.
                scroll.verticalScrollbar = indicator; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            }
            return content;
        }
        // A thin double brass rule with a diamond at its start: the title's underline.
        void Ornament(Transform parent, float x, float y, float w)
        {
            Shade(parent, "Ornament", x + 14, y, w - 14, 2, new Color(Brass.r, Brass.g, Brass.b, 0.75f), true);
            Shade(parent, "Ornament", x + 14, y + 5, (w - 14) * 0.6f, 1, new Color(Brass.r, Brass.g, Brass.b, 0.35f), true);
            Box(parent, "OrnamentMark", x + 4, y - 3, 8, 8, Brass).localRotation = Quaternion.Euler(0, 0, 45);
        }
        // A colour fading out to the right (sideways) or downwards: the menu shade, glows and rules.
        RectTransform Shade(Transform parent, string name, float x, float y, float w, float h, Color color, bool sideways)
        {
            var r = Box(parent, name, x, y, w, h, color);
            r.GetComponent<Image>().sprite = sideways ? Ramp(true) : Ramp(false);
            return r;
        }
        static readonly Dictionary<bool, Sprite> ramps = new Dictionary<bool, Sprite>();
        static Sprite Ramp(bool sideways)
        {
            if (ramps.TryGetValue(sideways, out var cached) && cached != null) return cached;
            int w = sideways ? 64 : 1, h = sideways ? 1 : 64;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < 64; i++) { float a = 1 - i / 63f; texture.SetPixel(sideways ? i : 0, sideways ? 0 : 63 - i, new Color(1, 1, 1, a * a * (3 - 2 * a))); }
            texture.Apply();
            return ramps[sideways] = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }
        // A piece of the battle UI kit (Resources/Art/UI/battle), 9-sliced when a border is given; the source height sets the scale.
        RectTransform Kit(Transform parent, string name, string id, Vector4 border, float x, float y, float w, float h, float sourceHeight)
        {
            var r = Box(parent, name, x, y, w, h, Color.white);
            var image = r.GetComponent<Image>(); var sprite = KitSprite(id, border);
            if (sprite == null) { image.color = Color.clear; return r; }
            image.sprite = sprite;
            if (border != Vector4.zero) { image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = sourceHeight / Math.Max(1, Math.Min(h, sourceHeight)); }
            else image.preserveAspect = true;
            return r;
        }
        Sprite? KitSprite(string id, Vector4 border)
        {
            string key = "kit:" + id + border;
            if (sprites.TryGetValue(key, out var cached)) return cached;
            var texture = Resources.Load<Texture2D>("Art/UI/battle/" + id);
            var sprite = texture == null ? null : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sprites[key] = sprite; return sprite;
        }
        void Glyph(Transform parent, string icon, float x, float y, float size, Color tint)
        {
            int cell = icon switch { "nav_home" => 0, "nav_companions" => 1, "nav_recruit" => 2, "nav_memory" => 3,
                "nav_equipment" => 4, "nav_bond" => 5, "nav_moments" => 6, "nav_expedition" => 7, _ => -1 };
            Sprite? glyph;
            if (cell < 0) glyph = SpriteAt("Art/UI/" + icon);
            else
            {
                if (!sprites.TryGetValue(icon, out glyph))
                {
                    var atlas = Resources.Load<Texture2D>("Art/UI/home-nav-20261021");
                    glyph = atlas == null ? null : Sprite.Create(atlas,
                        new Rect(cell % 4 * atlas.width / 4f, (1 - cell / 4) * atlas.height / 2f, atlas.width / 4f, atlas.height / 2f),
                        new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                    sprites[icon] = glyph;
                }
                tint = Color.white;
            }
            if (glyph == null) return;
            var g = Box(parent, "Icon", x, y, size, size, tint); g.GetComponent<Image>().sprite = glyph; CenterSprite(g);
        }
        // Preserve-aspect images use their pivot for alignment; center the sprite without moving its hit/layout rectangle.
        static void CenterSprite(RectTransform rect)
        {
            Vector3 corner = rect.TransformPoint(rect.rect.min);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.position += corner - rect.TransformPoint(rect.rect.min);
            rect.GetComponent<Image>().preserveAspect = true;
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
            ClosePanels();
            if (open) ShowPanel(panel);
            if (open) StartCoroutine(MemoryForgeUiMotion.SlideIn(panel, new Vector2(0, -20), 0.2f));
        }

        void ClosePanels()
        {
            if (accountBusy || growthBusy) return;
            ClearSupportPreparation();
            foreach (var panel in new[] { sortiePanel, settingsPanel, listPanel, rosterPanel, detailPanel, recruitmentPanel, recruitmentReveal, growthPanel, equipmentPanel })
                if (panel != null) panel.gameObject.SetActive(false);
            idleSince = Time.time;
            if (modalShield != null) modalShield.gameObject.SetActive(false);
            SetHomeInput(!player.Playing);
            RefreshHomeAccount();
        }

        void RefreshHomeAccount()
        {
            if (homeAccountText == null) return;
            var snapshot = recruitmentSession?.Recruitment;
            homeAccountText.text = snapshot == null ? "계정 미연결\n모집에서 연결"
                : recruitmentSession!.RecruitmentNeedsRefresh ? "마지막 확인 " + snapshot.OwnedChars.Count + "명\n연결 확인 필요"
                : "보유 동료 " + snapshot.OwnedChars.Count + "명\n공방 계정";
            RefreshAccountHome();
        }

        void SetHomeInput(bool enabled)
        {
            enabled &= !contentUnavailable;
            homeControls.interactable = homeControls.blocksRaycasts = enabled;
            figureTap.interactable = enabled;
        }

        void BackToRoster() { RenderRoster(); ShowPanel(rosterPanel); }

        void ShowPanel(RectTransform panel)
        {
            ClosePanels(); SetHomeInput(false);
            panel.GetComponent<Image>().raycastTarget = true;
            modalShield.GetComponent<Button>().interactable = !contentUnavailable;
            modalShield.gameObject.SetActive(true); panel.gameObject.SetActive(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(panel.GetComponentsInChildren<Button>().FirstOrDefault(b => b.interactable)?.gameObject);
        }

        void ShowLoadError()
        {
            contentUnavailable = true;
            if (root == null) Build();
            enabled = false;
            var error = Box(root, "ContentUnavailable", 360, 250, 880, 360, Panel);
            error.GetComponent<Image>().raycastTarget = true;
            Label(error, "게임 데이터를 불러오지 못했어요.", 40, 40, 800, 64, 32, Ink, FontStyle.Bold);
            Label(error, "다시 불러온 뒤에도 계속되면 앱을 다시 실행해주세요.", 40, 124, 800, 72, 24, Muted);
            Button(error, "ReloadContent", "다시 불러오기", 240, 216, 400, 120, () => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex), Brass, 26);
            ShowPanel(error);
        }

        void FitHome()
        {
            MemoryForgeUiViewport.Fit(root, (RectTransform)root.GetComponentInParent<Canvas>().transform);
            if (homeRightShade != null)
            {
                var metrics = MemoryForgeUiViewport.Current; var geometry = MemoryForgeUiViewport.Calculate(metrics, metrics.Pixels);
                float right = Math.Max(0, geometry.Pixels.x - geometry.SafeRect.xMax) / geometry.PixelsPerUnit;
                homeRightShade.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Right, -right, 584 + right);
            }
        }

        void BuildRoster()
        {
            rosterPanel = Box(root, "RosterPanel", 0, 0, 1600, 900, Background);
            rosterPanel.GetComponent<Image>().raycastTarget = true;
            detailPanel = Box(root, "CompanionDetails", 0, 0, 1600, 900, Background);
            detailPanel.GetComponent<Image>().raycastTarget = true;
            rosterPanel.gameObject.SetActive(false); detailPanel.gameObject.SetActive(false);
        }

        void OpenRoster()
        {
            player.Stop(); ClosePanels();
            RenderRoster(); ShowPanel(rosterPanel);
        }

        void RenderRoster()
        {
            ClearPanel(rosterPanel);
            var wall = Box(rosterPanel, "Workshop", 0, 0, 1600, 900, new Color(0.2f, 0.25f, 0.32f));
            wall.GetComponent<Image>().sprite = SpriteAt("Art/Backgrounds/bg_workshop"); MemoryForgeUiViewport.Cover(wall.GetComponent<Image>());
            Box(rosterPanel, "CatalogueVeil", 0, 0, 1600, 900, new Color(0.035f, 0.05f, 0.09f, 0.83f));
            var rosterHeading = Label(rosterPanel, "동료 기록", 56, 24, 700, 64, 42, Ink, FontStyle.Bold);
            var rosterSubtitle = Label(rosterPanel, recruitmentSession?.RecruitmentNeedsRefresh == true ? "마지막 확인한 보유 정보예요. 모집에서 연결을 새로 확인해주세요." : "동료의 능력과 시작 카드를 살펴보세요.", 58, 100, 1080, 30, 21, Muted);
            Button(rosterPanel, "CloseRoster", "홈", 1392, 24, 152, 112, ClosePanels, Line, 24);
            float subtitleY = 24 + rosterHeading.rectTransform.rect.height + 12;
            rosterSubtitle.rectTransform.anchoredPosition = new Vector2(rosterSubtitle.rectTransform.anchoredPosition.x, -subtitleY);
            float filterY = subtitleY + rosterSubtitle.rectTransform.rect.height + 16;
            float filterHeight = Math.Max(112, TouchUnits()), gridY = filterY + filterHeight + 24, gridHeight = 800 - gridY;
            Button(rosterPanel, "FilterAll", "전체 동료", 56, filterY, 264, filterHeight, () => { catalogueFilter = "all"; RenderRoster(); }, catalogueFilter == "all" ? Brass : Panel, 24);
            Button(rosterPanel, "FilterParty", "연습 편성", 336, filterY, 264, filterHeight, () => { catalogueFilter = "party"; RenderRoster(); }, catalogueFilter == "party" ? Brass : Panel, 24);
            Button(rosterPanel, "FilterOwned", "보유 동료", 616, filterY, 264, filterHeight, () => { catalogueFilter = "owned"; RenderRoster(); }, catalogueFilter == "owned" ? Brass : Panel, 24);
            var owned = recruitmentSession?.Recruitment?.OwnedChars;
            var characters = content.Battle.Characters.Where(c => (catalogueFilter != "party" || PracticeParty.Contains(c.Id))
                && (catalogueFilter != "owned" || owned != null && owned.ContainsKey(c.Id))).ToList();
            Label(rosterPanel, characters.Count + "명의 동료", 1112, filterY + 24, 424, 56, 22, Muted, FontStyle.Normal, TextAnchor.MiddleRight);
            float cellH = Math.Max(320, TouchUnits() * 2f + 24), footer = Math.Max(104, MemoryForgeUiViewport.FontSize(27) * 1.9f), total = (float)Math.Ceiling(characters.Count / 3f) * (cellH + 20) + 80;
            if (characters.Count == 0)
            {
                Glyph(rosterPanel, "nav_companions", 432, gridY + 24, 176, Color.white);
                Label(rosterPanel, owned == null ? "계정을 연결하면 보유 동료를 볼 수 있어요." : "아직 모집한 동료가 없어요.", 96, gridY + 216, 880, 72, 28, Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
                Button(rosterPanel, "RecruitFromRoster", owned == null ? "계정 연결" : "모집으로", 196, gridY + 316, 688, Math.Max(112, TouchUnits()), OpenRecruitment, Brass, 26);
            }
            else
            {
                var grid = ScrollArea(rosterPanel, 56, gridY, 984, gridHeight, Math.Max(gridHeight, total));
                for (int n = 0; n < characters.Count; n++)
                {
                    var c = characters[n]; float x = n % 3 * 328, y = 16 + n / 3 * (cellH + 20);
                    var tile = Button(grid, "Companion:" + c.Id, "", x, y, 312, cellH, () => OpenDetails(c.Id), Color.clear);
                    var tileRect = (RectTransform)tile.transform;
                    tileRect.anchorMin = new Vector2((n % 3) / 3f, 1); tileRect.anchorMax = new Vector2((n % 3 + 1) / 3f, 1);
                    tileRect.sizeDelta = new Vector2(-16, cellH); tileRect.anchoredPosition = new Vector2(0, -y);
                    Box(tile.transform, "CardBody", 0, 0, 312, cellH, new Color(0.09f, 0.12f, 0.19f, 0.96f));
                    var art = Portrait(c.Id);
                    var portrait = Box(tile.transform, "Portrait", 4, 4, 304, cellH - footer, Color.white);
                    portrait.GetComponent<Image>().sprite = art; CenterSprite(portrait);
                    if (art == null)
                    {
                        portrait.GetComponent<Image>().color = Color.clear;
                        Glyph(portrait, "nav_companions", 100, 28, 104, new Color(1, 1, 1, 0.65f));
                        Label(portrait, "일러스트 준비 중", 12, 150, 280, 40, 18, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
                    }
                    Box(tile.transform, "ElementEdge", 0, cellH - footer, 312, 3, ElementColor(c.Attr));
                    var name = Label(tile.transform, c.Name, 16, cellH - footer + 12, 186, footer - 24, 27, Ink, FontStyle.Bold);
                    var element = Label(tile.transform, ElementName(c.Attr), 208, cellH - footer + 12, 88, footer - 24, 20, ElementColor(c.Attr), FontStyle.Normal, TextAnchor.MiddleRight);
                    float elementWidth = Math.Max(88, Mathf.Ceil(element.preferredWidth) + 8);
                    element.rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Right, 16, elementWidth);
                    name.rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 16, Math.Max(1, tileRect.rect.width - 44 - elementWidth));
                    MemoryForgeUiViewport.WholeText(name); MemoryForgeUiViewport.WholeText(element);
                    if (PracticeParty.Contains(c.Id)) Label(tile.transform, "편성", 12, 8, 84, 32, 18, Brass, FontStyle.Bold);
                    if (owned != null && owned.TryGetValue(c.Id, out int copies)) Label(tile.transform, "보유 · 공명 " + Math.Min(6, Math.Max(0, copies - 1)), 112, 8, 184, 32, 18, Memory, FontStyle.Bold, TextAnchor.MiddleRight);
                }
            }
            var previewCharacter = characters.FirstOrDefault(character => character.Id == detailId) ?? characters.FirstOrDefault() ?? content.Battle.Characters.First();
            var preview = Box(rosterPanel, "RosterPreview", 1072, gridY, 472, gridHeight, new Color(0.04f, 0.055f, 0.1f, 0.96f));
            Box(preview, "ElementEdge", 0, 0, 4, gridHeight, ElementColor(previewCharacter.Attr));
            float buttonHeight = Math.Max(104, TouchUnits()), infoHeight = Math.Max(64, gridHeight - buttonHeight - 32);
            float artHeight = Math.Max(88, Math.Min(184, infoHeight - 246));
            var info = ScrollArea(preview, 0, 0, 472, infoHeight, Math.Max(infoHeight, artHeight + 246));
            var previewHeading = Label(info, characters.Count == 0 ? "전체 도감 미리보기" : "동료 도감 미리보기", 24, 16, 424, 28, 18, Brass);
            float previewArtY = 16 + previewHeading.rectTransform.rect.height + 16;
            var previewArt = Box(info, "PreviewArt", 24, previewArtY, 424, artHeight, Color.white);
            var previewSprite = SpriteAt("Art/Characters/char_" + previewCharacter.Id + "_sd") ?? Portrait(previewCharacter.Id);
            previewArt.GetComponent<Image>().sprite = previewSprite; CenterSprite(previewArt);
            if (previewSprite == null)
            {
                previewArt.GetComponent<Image>().color = Color.clear;
                float crest = Math.Min(80, Math.Max(24, artHeight - 40));
                Glyph(previewArt, "nav_companions", (424 - crest) / 2, 0, crest, Color.white);
                Label(previewArt, "일러스트 준비 중", 0, artHeight - 32, 424, 32, 18, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            }
            float previewY = previewArtY + artHeight + 16;
            var previewName = Label(info, previewCharacter.Name, 24, previewY, 424, 1, 34, Ink, FontStyle.Bold); previewY += previewName.rectTransform.rect.height + 12;
            string weapon = content.CharacterWeaponTypes.TryGetValue(previewCharacter.Id, out var type) ? WeaponName(type) : "무기 정보 없음";
            var previewType = Label(info, ElementName(previewCharacter.Attr) + "  /  " + weapon, 26, previewY, 420, 1, 21, ElementColor(previewCharacter.Attr)); previewY += previewType.rectTransform.rect.height + 12;
            var previewPassive = Label(info, "고유 능력  " + previewCharacter.Passive.Name, 26, previewY, 420, 1, 20, Ink); previewY += previewPassive.rectTransform.rect.height + 12;
            var previewUltimate = Label(info, "필살기  " + previewCharacter.Ult.Name, 26, previewY, 420, 1, 20, Memory); previewY += previewUltimate.rectTransform.rect.height + 16;
            info.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(infoHeight, previewY));
            Button(preview, "PreviewCompanionDetails", "스킬과 카드 보기", 24, gridHeight - buttonHeight - 16, 424, buttonHeight, () => OpenDetails(previewCharacter.Id), Line, 23);
            Label(rosterPanel, (total > gridHeight ? "위아래로 밀어 더 볼 수 있어요.  ·  " : "") + "연습 편성: " + PartyNames(), 58, 812, 1440, 72, 19, Muted);
        }

        void OpenDetails(string id)
        {
            detailId = id; RenderDetails(false); ShowPanel(detailPanel);
        }

        void RenderDetails(bool cards)
        {
            detailCards = cards;
            ClearPanel(detailPanel);
            var c = content.Battle.Characters.Single(x => x.Id == detailId);
            var wall = Box(detailPanel, "Workshop", 0, 0, 1600, 900, new Color(0.3f, 0.35f, 0.42f));
            wall.GetComponent<Image>().sprite = SpriteAt("Art/Backgrounds/bg_workshop"); MemoryForgeUiViewport.Cover(wall.GetComponent<Image>());
            Box(detailPanel, "DetailVeil", 0, 0, 1600, 900, new Color(0.035f, 0.05f, 0.09f, 0.65f));
            float buttonHeight = Math.Max(112, TouchUnits()), bodyY = 24 + buttonHeight + 16;
            float footerY = 884 - buttonHeight, bodyHeight = footerY - bodyY - 16;
            Button(detailPanel, "BackToRoster", "동료 목록", 56, 24, 240, buttonHeight, BackToRoster, Line, 24);
            Button(detailPanel, "CloseDetails", "홈", 1392, 24, 152, buttonHeight, ClosePanels, Line, 24);
            var portrait = Box(detailPanel, "CharacterArt", 88, bodyY, 600, bodyHeight, Color.white);
            MemoryForgeUiViewport.StretchY(portrait, bodyY, 900 - bodyY - bodyHeight);
            portrait.GetComponent<Image>().sprite = Portrait(c.Id); CenterSprite(portrait);
            if (portrait.GetComponent<Image>().sprite == null) { portrait.GetComponent<Image>().color = Color.clear; Label(portrait, "그림 준비 중", 100, 250, 400, 60, 26, Muted, FontStyle.Normal, TextAnchor.MiddleCenter); }
            var paper = Box(detailPanel, "CharacterSheet", 740, bodyY, 804, bodyHeight, new Color(0.04f, 0.055f, 0.1f, 0.95f));
            MemoryForgeUiViewport.StretchX(paper, 740, 56); MemoryForgeUiViewport.StretchY(paper, bodyY, 900 - bodyY - bodyHeight);
            Box(paper, "ElementEdge", 0, 0, 4, bodyHeight, ElementColor(c.Attr));
            var detailHeading = Label(paper, c.Name, 32, 20, 600, 70, 48, Ink, FontStyle.Bold);
            string weapon = content.CharacterWeaponTypes.TryGetValue(c.Id, out var type) ? WeaponName(type) : "무기 정보 없음";
            float detailTypeY = 20 + detailHeading.rectTransform.rect.height + 12;
            var detailType = Label(paper, ElementName(c.Attr) + "  /  " + weapon, 34, detailTypeY, 720, 44, 23, ElementColor(c?.Attr ?? ""));
            float tabY = detailTypeY + detailType.rectTransform.rect.height + 16;
            float tabHeight = Math.Max(112, TouchUnits()), scrollY = tabY + tabHeight + 16;
            Button(paper, "ShowCompanionSkills", "스킬", 32, tabY, 350, tabHeight, () => RenderDetails(false), cards ? Panel : Brass, 24);
            Button(paper, "ShowCompanionCards", "시작 고유 카드", 398, tabY, 374, tabHeight, () => RenderDetails(true), cards ? Brass : Panel, 24);
            var details = ScrollArea(paper, 32, scrollY, 740, bodyHeight - scrollY - 16, cards ? content.Battle.Cards.Count(x => x.Owner == c.Id && x.Start == true) * 170 : 360);
            float textY = 0;
            if (cards)
            {
                foreach (var card in content.Battle.Cards.Where(x => x.Owner == c.Id && x.Start == true))
                {
                    textY = FlowLabel(details, card.Name + "   ·   행동력 " + card.Cost, textY, 25, Brass, FontStyle.Bold);
                    textY = FlowLabel(details, BattleText.Card(card, content.Battle), textY, 23, Ink) + 16;
                }
            }
            else
            {
                textY = FlowLabel(details, "고유 능력  /  " + c.Passive.Name, textY, 25, Brass, FontStyle.Bold);
                textY = FlowLabel(details, c.Passive.Text, textY, 23, Ink) + 16;
                textY = FlowLabel(details, "필살기  /  " + c.Ult.Name, textY, 25, Memory, FontStyle.Bold);
                textY = FlowLabel(details, c.Ult.Text, textY, 23, Ink);
            }
            details.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(((RectTransform)details.parent).rect.height, textY + 16));
            int homeIndex = companions.FindIndex(x => x.Id == c.Id);
            var use = Button(detailPanel, "UseAsLobby", homeIndex < 0 ? "홈 일러스트 준비 중" : "홈에 함께하기", 128, footerY, 500, buttonHeight,
                () => { ClosePanels(); Show(homeIndex); }, Brass, 27);
            use.interactable = homeIndex >= 0;
            var owned = AccountProgression?.Characters.FirstOrDefault(item => item.CharId == c.Id);
            if (owned != null) Button(detailPanel, "GrowOwnedCompanion", "성장 · 장비 공방", 752, footerY, 776, buttonHeight, () => OpenEquipmentFor(c.Id), Line, 27)
                .interactable = !accountBusy && !growthBusy && !recruitmentBusy;
            else Label(detailPanel, "보유 동료 육성: 성장 공방", 752, footerY + 10, 776, buttonHeight - 20, 20, Muted);
        }

        float FlowLabel(RectTransform parent, string value, float y, int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var text = Label(parent, value, 0, y, Math.Max(1, MemoryForgeUiViewport.ReferenceSize(parent).x - 32), 1, size, color, style);
            return y + text.rectTransform.rect.height + 14;
        }

        Sprite? Portrait(string id)
        {
            var c = companions.FirstOrDefault(x => x.Id == id);
            var key = c?.Moments?.RestFigure();
            return key != null && c!.Moments!.Images.TryGetValue(key, out var path) ? SpriteForArt(path) : c?.Still ?? SpriteAt("Art/Characters/char_" + id + "_sd");
        }

        static void ClearPanel(RectTransform panel)
        {
            foreach (Transform child in panel) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        }

        static string ElementName(string id) => id == "volt" ? "전격" : id == "flame" ? "화염" : id == "frost" ? "빙결" : id == "void" ? "공허" : id;
        static Color ElementColor(string id) => Hex(id == "volt" ? "ffd84d" : id == "flame" ? "ff7a3d" : id == "frost" ? "7cc8ff" : id == "void" ? "b68cff" : "98a5c0");
        static string WeaponName(string id) => id == "ranged" ? "원거리" : id == "melee" ? "근접" : id == "device" ? "장치" : id == "ritual" ? "의식" : id;
        string PartyNames() => content == null ? "데이터 불러오기 실패" : string.Join(" / ", PracticeParty.Select(id => content.Battle.Characters.First(c => c.Id == id).Name));

        // 출격 준비: the zone, the run's length and the party, then 출발. The practice party is fixed until 편성 is built.
        void BuildSortie()
        {
            if (sortiePanel == null) sortiePanel = Box(root, "SortiePanel", 0, 0, 1600, 900, Background);
            else ClearPanel(sortiePanel);
            accountSortieDrawn = false;
            var wall = Box(sortiePanel, "Workshop", 0, 0, 1600, 900, new Color(0.18f, 0.23f, 0.3f));
            wall.GetComponent<Image>().sprite = SpriteAt("Art/Backgrounds/bg_workshop"); MemoryForgeUiViewport.Cover(wall.GetComponent<Image>());
            Box(sortiePanel, "PreparationVeil", 0, 0, 1600, 900, new Color(0.025f, 0.04f, 0.075f, 0.83f));
            Label(sortiePanel, "탐사 준비", 56, 28, 900, 64, 42, Ink, FontStyle.Bold);
            Label(sortiePanel, "길이를 고르고, 함께 출발할 동료를 확인하세요.", 58, 108, 1100, 36, 21, Muted);
            Ornament(sortiePanel, 58, 164, 440);
            Button(sortiePanel, "CloseSortie", "홈", 1392, 24, 152, 112, ClosePanels, Line, 24);
            var zone = Box(sortiePanel, "Zone", 56, 200, 456, 420, Color.white);
            var map = SpriteAt("Art/Backgrounds/bg_zone1_map");
            zone.GetComponent<Image>().sprite = map; zone.GetComponent<Image>().color = map == null ? Panel : new Color(0.7f, 0.75f, 0.82f);
            Box(zone, "DestinationShade", 0, 232, 456, 188, new Color(0.025f, 0.04f, 0.075f, 0.92f));
            Box(zone, "BrassEdge", 0, 0, 4, 420, Brass);
            Label(zone, "출발 구역", 24, 252, 408, 32, 19, Brass);
            Label(zone, "종탑 하층", 22, 292, 410, 52, 38, Ink, FontStyle.Bold);
            Label(zone, "7층과 보스\n전투 · 공방 · 휴식 · 상점 · 사건", 24, 352, 408, 60, 20, Muted);
            Text? shortText = null, regularText = null;
            Button? shortButton = null, regularButton = null;
            float choiceHeight = Math.Max(120, TouchUnits());
            void Mark()
            {
                Pill(shortButton!.GetComponent<Image>(), sortieLength == "short" ? Brass : Panel, choiceHeight); shortText!.color = sortieLength == "short" ? Background : Ink;
                Pill(regularButton!.GetComponent<Image>(), sortieLength == "full" ? Brass : Panel, choiceHeight); regularText!.color = sortieLength == "full" ? Background : Ink;
            }
            shortButton = Button(sortiePanel, "Length:short", "", 544, 200, 480, choiceHeight, () => { sortieLength = "short"; Mark(); }, Brass, 22);
            shortText = Label(shortButton.transform, "짧은 탐사\n1구역 · 약 15분", 12, 20, 456, choiceHeight - 36, 23, Background, FontStyle.Normal, TextAnchor.MiddleCenter);
            regularButton = Button(sortiePanel, "Length:full", "", 1040, 200, 504, choiceHeight, () => { sortieLength = "full"; Mark(); }, Panel, 22);
            regularText = Label(regularButton.transform, "긴 탐사\n2구역까지 · 약 30분", 12, 20, 480, choiceHeight - 36, 23, Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
            Label(sortiePanel, "함께 출발하는 동료", 544, 376, 1000, 36, 26, Brass, FontStyle.Bold);
            for (int i = 0; i < PracticeParty.Count; i++)
            {
                string id = PracticeParty[i]; var c = content?.Battle.Characters.FirstOrDefault(x => x.Id == id);
                var slot = Box(sortiePanel, "Party:" + id, 544 + i * 328, 424, 312, 232, new Color(0.08f, 0.11f, 0.18f, 0.96f));
                Box(slot, "ElementEdge", 0, 0, 312, 3, ElementColor(c?.Attr ?? ""));
                var art = SpriteAt("Art/Characters/char_" + id + "_sd") ?? Portrait(id);
                var face = Box(slot, "Face", 16, 20, 128, 156, Color.white);
                face.GetComponent<Image>().sprite = art; CenterSprite(face);
                if (art == null) face.GetComponent<Image>().color = Color.clear;
                Label(slot, c?.Name ?? id, 156, 32, 140, 48, 30, Ink, FontStyle.Bold);
                Label(slot, ElementName(c?.Attr ?? ""), 158, 96, 138, 36, 23, ElementColor(c?.Attr ?? ""));
                Label(slot, content != null && content.CharacterWeaponTypes.TryGetValue(id, out var type) ? WeaponName(type) : "", 158, 144, 138, 32, 20, Muted);
                Label(slot, "시작 고유 카드 " + (content?.Battle.Cards.Count(card => card.Owner == id && card.Start == true) ?? 0) + "장", 24, 190, 264, 28, 18, Muted);
            }
            Label(sortiePanel, "고정 연습 편성: " + PartyNames(), 546, 676, 996, 32, 20, Muted);
            Label(sortiePanel, "연습 탐사\n저장한 기억은 이 앱을 실행하는 동안 이어집니다.\n편성·장비 변경은 준비 중이에요.", 58, 696, 984, 148, 21, Muted);
            Button(sortiePanel, "Depart", "출발", 1112, 720, 432, Math.Max(120, TouchUnits()), () => { if (leaving) return; NextLength = sortieLength; StartCoroutine(Leave()); }, Brass, 32);
            Mark();
            sortiePanel.gameObject.SetActive(false);
        }

        // 설정: reduced motion everywhere; on a PC also the window mode.
        void BuildSettings()
        {
            float tall = Application.isMobilePlatform ? 280 : 420;
            settingsPanel = Box(root, "SettingsPanel", 1012, 148, 532, tall, Panel);
            Kit(settingsPanel, "Frame", "ui_panel_frame", new Vector4(48, 48, 48, 48), 0, 0, 532, tall, 128);
            var settingsHeading = Label(settingsPanel, "설정", 24, 18, 300, 88, 36, Ink, FontStyle.Bold);
            float closeWidth = Math.Max(124, TouchUnits());
            Button(settingsPanel, "CloseSettings", "닫기", 532 - 24 - closeWidth, 8, closeWidth, 112, ClosePanels, Line, 22);
            Text? motionLabel = null;
            float rowHeight = Math.Max(160, TouchUnits()), motionY = Math.Max(136, 18 + settingsHeading.rectTransform.rect.height + 16);
            var motionToggle = Button(settingsPanel, "ReduceMotion", "", 24, motionY, 484, rowHeight, () => { MemoryForgeUiMotion.Reduced = !MemoryForgeUiMotion.Reduced; motionLabel!.text = MotionCaption(); }, Panel, 20);
            motionLabel = Label(motionToggle.transform, MotionCaption(), 12, 24, 460, rowHeight - 48, 20, Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
            float nextY = motionY + Math.Max(rowHeight, motionLabel.rectTransform.rect.height + 48) + 16;
            ((RectTransform)motionToggle.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, nextY - motionY - 16);
            if (!Application.isMobilePlatform)
            {
                Text? windowLabel = null;
                var window = Button(settingsPanel, "WindowMode", "", 24, nextY, 484, rowHeight, () =>
                {
                    Screen.fullScreenMode = Screen.fullScreenMode == FullScreenMode.Windowed ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                    windowLabel!.text = WindowCaption(Screen.fullScreenMode != FullScreenMode.Windowed);
                }, Panel, 20);
                windowLabel = Label(window.transform, WindowCaption(Screen.fullScreenMode != FullScreenMode.Windowed), 12, 24, 460, rowHeight - 48, 20, Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
                float windowHeight = Math.Max(rowHeight, windowLabel.rectTransform.rect.height + 48);
                ((RectTransform)window.transform).SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, nextY, windowHeight); nextY += windowHeight + 16;
            }
            settingsPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(tall, nextY + 16));
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
            ClosePanels();
            var c = companions[index];
            nameText.text = c.Name;
            if (c.Moments != null) player.Bind(c.Moments); else player.ShowStill(c.Still);
            StillFrame(c.Moments == null);
            var definition = content.Battle.Characters.FirstOrDefault(character => character.Id == c.Id);
            noteText.text = definition == null ? "동료 장면 미리보기" : ElementName(definition.Attr)
                + (content.CharacterWeaponTypes.TryGetValue(c.Id, out var weapon) ? "  /  " + WeaponName(weapon) : "");
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
            ClosePanels();
            lastPlayed[scene.Id] = Time.time;
            player.Play(scene);
        }

        public static string TimeOfDay(int hour) => hour < 5 ? "night" : hour < 11 ? "morning" : hour < 17 ? "day" : hour < 21 ? "evening" : "night";

        // Bond stories, or entry/victory/skin staging, as a list. Preview policy: these rows open the scene directly, ignoring
        // bond level, event triggers, repeat and cooldown, because bond progress and battle events are not connected yet; each
        // row says so. Lobby reactions (tap, enter, idle, time) always go through Ready().
        void OpenList(string which)
        {
            player.Stop(); ClosePanels();
            sceneListKind = which; BuildSceneList(which);
            ShowPanel(listPanel);
            var group = listPanel.GetComponent<CanvasGroup>(); if (group == null) group = listPanel.gameObject.AddComponent<CanvasGroup>();
            StartCoroutine(MemoryForgeUiMotion.Fade(group, 0, 1, 0.2f));
            StartCoroutine(MemoryForgeUiMotion.SlideIn(listPanel, new Vector2(0, -24), 0.2f));
        }

        void BuildSceneList(string which)
        {
            ClearPanel(listPanel);
            var m = Current?.Moments;
            var scenes = m == null ? new List<MomentScene>() : m.Scenes.Where(s => which == "bond" ? s.Kind == "bond" : s.Kind == "entry" || s.Kind == "victory" || s.Kind == "skin").ToList();
            Label(listPanel, which == "bond" ? "인연 이야기" : "등장·승리 연출", 28, 22, 420, 46, 30, Brass, FontStyle.Bold);
            Button(listPanel, "CloseList", "닫기", 592, 8, 124, 120, ClosePanels, Line, 22);
            if (scenes.Count == 0) Label(listPanel, "아직 준비된 장면이 없어요.", 28, 144, 688, 72, 22, Muted);
            float rowHeight = Math.Max(120, TouchUnits()), pitch = rowHeight + 12;
            var rows = ScrollArea(listPanel, 24, 160, 696, 552, Math.Max(552, scenes.Count * pitch));
            float y = 0;
            foreach (var s in scenes)
            {
                var row = Button(rows, "Scene:" + s.Id, "", 0, y, 672, rowHeight, () => Play(s), Panel);
                var title = Label(row.transform, s.Title.Length == 0 ? s.Id : s.Title, 18, 14, 636, 38, 24, Ink, FontStyle.Bold);
                float conditionY = Math.Max(70, 14 + title.rectTransform.rect.height + 12);
                var condition = Label(row.transform, Condition(s), 18, conditionY, 636, 30, 18, Memory);
                condition.rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, conditionY, condition.rectTransform.rect.height);
                float height = Math.Max(rowHeight, conditionY + condition.rectTransform.rect.height + 16);
                ((RectTransform)row.transform).SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, y, height);
                y += height + 12;
            }
            rows.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(552, y));
            foreach (var b in listPanel.GetComponentsInChildren<Button>(true)) if (b.GetComponent<UiPress>() == null) b.gameObject.AddComponent<UiPress>();
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
            var r = (RectTransform)obj.transform; r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height); MemoryForgeUiViewport.Anchor(r, x, y, width, height);
            var image = obj.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
            if (width == 1600 && height == 900 && MemoryForgeUiViewport.IsDim(name)) MemoryForgeUiViewport.BleedDim(image);
            if (width == 1600 && height == 900 && (name.Contains("Workshop") || name.Contains("Backdrop") || name.EndsWith("Veil", StringComparison.Ordinal) || name.EndsWith("Shade", StringComparison.Ordinal) || name == "Shade")) MemoryForgeUiViewport.Bleed(r);
            return r;
        }
        Text Label(Transform parent, string value, float x, float y, float width, float height, int size, Color color, FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.UpperLeft)
        {
            var obj = new GameObject("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            var r = (RectTransform)obj.transform; r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height); MemoryForgeUiViewport.Anchor(r, x, y, width, height);
            var text = obj.GetComponent<Text>(); text.font = font; text.text = value; text.fontSize = size; text.color = color; text.fontStyle = style; text.alignment = align; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            MemoryForgeUiViewport.WholeText(text); return text;
        }
        Button Button(Transform parent, string name, string caption, float x, float y, float width, float height, Action click, Color color, int size = 24)
        {
            float touch = TouchUnits(); width = Math.Max(width, touch); height = Math.Max(height, touch);
            var r = Box(parent, name, x, y, width, height, color); var image = r.GetComponent<Image>(); image.raycastTarget = true;
            if (name != "ModalShield") Pill(image, color, height);
            var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            if (name != "FigureTap" && name != "ModalShield") r.gameObject.AddComponent<UiPress>();
            button.onClick.AddListener(() => { if (button != null && button.IsActive() && button.IsInteractable()) click(); });
            var colors = button.colors; colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f); colors.pressedColor = new Color(0.78f, 0.78f, 0.78f); button.colors = colors;
            if (caption.Length != 0) Label(r, caption, 6, 5, width - 12, height - 10, size, color == Brass || color == Memory ? Background : Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
            return button;
        }
        // Brass buttons wear the kit's brass pill, dark ones its ink pill (sliced at the ends); clear ones stay invisible hit areas.
        void Pill(Image image, Color color, float height)
        {
            bool brass = color == Brass, ink = color == Line || color == Panel;
            var sprite = brass || ink ? KitSprite(brass ? "ui_button_pill_brass" : "ui_button_pill_ink", new Vector4(64, 0, 64, 0)) : null;
            image.sprite = sprite; image.type = sprite == null ? Image.Type.Simple : Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = sprite == null ? 1 : 128f / Math.Max(1, height); image.color = sprite == null ? color : Color.white;
        }
        // 48dp in this canvas's units (ScaleWithScreenSize, 1600×900, match 0.5); 0 off phones. The expedition's touch override applies too.
        static float TouchUnits()
        {
            if (MemoryForgeExpeditionClient.TouchOverride.HasValue) return MemoryForgeExpeditionClient.TouchOverride.Value;
            return MemoryForgeUiViewport.TouchUnits();
        }
        static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
    }
}
