#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    // ONBOARDING-FIRST-RUN §14.5/§3.4: the first-run coach over the expedition practice (steps and wording: OnboardingSteps). It reads
    // the practice's public state and its drawn controls and draws one panel over the header text, outlines around the controls to press
    // and nothing else. None of its graphics takes a raycast except its own skip/acknowledge buttons, which sit over header text only, so
    // every practice button keeps its press. It sends no command and changes no Run, memory, number or reward. Progress is a local
    // PlayerPrefs record (no account or server): a done step never shows again, and a skip hides every step except the memory-save
    // guide, which §14.5 forbids skipping. It is attached only to the client of the shipped scene (Install); the scene is not edited.
    [DisallowMultipleComponent]
    public sealed class MemoryForgeOnboarding : MonoBehaviour
    {
        public const string SceneName = "MemoryForgeExpedition", DefaultPrefsKey = "MemoryForge.Onboarding.v1";
        // Stable object names (a later restyle finds the guide by these): the root, its panel, text and buttons, and one
        // "GuideHighlight:<target name>" group per outlined control with "Top"/"Bottom"/"Left"/"Right" edges.
        public const string GuideName = "Guide", PanelName = "Guide:Panel", TextName = "Guide:Text", NextName = "Guide:Next", SkipName = "Guide:Skip",
            HighlightPrefix = "GuideHighlight:";
        public const int FontSize = 17;
        // The coach panel covers only the header's title and practice/location text (frame x 20–920, y 2–74); every sheet starts lower.
        const float PanelX = 20, PanelY = 2, PanelWidth = 900, PanelHeight = 72, Pad = 16, ButtonWidth = 132, ButtonHeight = 48, Gap = 8, Edge = 4;
        public string PrefsKey = DefaultPrefsKey;

        /// <summary>The shown step's id; null when nothing is shown.</summary>
        public string? CurrentStep => shown?.Id;
        public string? CurrentText { get; private set; }
        /// <summary>The controls the shown step outlines (only usable buttons, and the non-button panels it names).</summary>
        public IReadOnlyList<RectTransform> Highlighted => highlighted;
        public IReadOnlyCollection<string> Completed => done;
        public bool Skipped => skipped;

        MemoryForgeExpeditionClient? client;
        OnboardingContext? context;
        readonly HashSet<string> done = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<int, string?> finishedIn = new Dictionary<int, string?>(); // lesson → battle that finished it (this session)
        readonly List<RectTransform> highlighted = new List<RectTransform>();
        readonly List<Image> edges = new List<Image>(); // the drawn highlight edges, pulsed in place
        bool skipped;
        OnboardingStep? shown, last; // last: the step most recently shown, whose Done is still checked while nothing else shows
        OnboardingMark mark = new OnboardingMark();
        RectTransform? indexed;
        GameObject? coach;
        string? drawnKey;
        Font? font;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= Attach; SceneManager.sceneLoaded += Attach;
            for (int i = 0; i < SceneManager.sceneCount; i++) Attach(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
        }
        static void Attach(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != SceneName || !scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var found in root.GetComponentsInChildren<MemoryForgeExpeditionClient>(true))
                    if (found.GetComponent<MemoryForgeOnboarding>() == null) found.gameObject.AddComponent<MemoryForgeOnboarding>();
        }

        void OnEnable()
        {
            client = GetComponent<MemoryForgeExpeditionClient>();
            if (client != null && context == null) context = new OnboardingContext(client);
            if (font == null) font = Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular");
            done.Clear(); done.UnionWith(PlayerPrefs.GetString(PrefsKey + ".done", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            skipped = PlayerPrefs.GetInt(PrefsKey + ".skipped", 0) == 1;
        }
        void OnDisable() { RemoveCoach(); indexed = null; shown = null; }

        /// <summary>Hides every step except the memory-save guide, now and on later launches. False when already skipped.</summary>
        public bool Skip()
        {
            if (skipped) return false;
            skipped = true; Save(); return true;
        }
        /// <summary>Finishes the shown explanation-only step. False when the shown step is not one.</summary>
        public bool Acknowledge()
        {
            if (shown == null || !shown.Ack) return false;
            Complete(shown); return true;
        }

        // After every Update and input event, so a view the client rebuilt this frame already has its coach when it is drawn.
        void LateUpdate()
        {
            if (client == null || context == null) return;
            var view = ActiveFrame();
            if (view != indexed) { indexed = view; context.Index(view); Listen(view); coach = null; drawnKey = null; }
            if (last != null && !done.Contains(last.Id) && last.Done != null && last.Done(context, mark)) Complete(last);
            var previous = shown;
            shown = view == null ? null : Pick(context);
            // Each new showing measures from now: a re-shown step must not finish on a card or turn it had before a gap.
            if (shown != null && shown != previous) { last = shown; mark = Capture(context); context.HandClicked = false; }
            Draw(view, context);
        }

        OnboardingStep? Pick(OnboardingContext c)
        {
            if (client!.RecoveryRequired || client.Snapshot == null) return null;
            foreach (var step in OnboardingSteps.All)
            {
                if (done.Contains(step.Id) || (skipped && !step.Required) || step.After.Any(id => !done.Contains(id)) || !Open(step, c)) continue;
                if (!step.Show(c)) continue;
                // Already done before it could show (e.g. '다음 턴' chosen while reading the step before): finished, not shown.
                if (step.Done != null && step.Done(c, Capture(c))) { Complete(step); continue; }
                return step;
            }
            return null;
        }
        // A lesson starts after the one it needs is finished, and in another battle than the one that finished it (§14.5: one per
        // battle); the boss lesson may follow it in the boss fight itself, its only battle.
        bool Open(OnboardingStep step, OnboardingContext c) => step.Needs == 0 || (LessonDone(step.Needs)
            && (step.OwnBattle || !finishedIn.TryGetValue(step.Needs, out var where) || where != c.BattleKey));
        bool LessonDone(int lesson) => OnboardingSteps.All.Where(s => s.Lesson == lesson && s.Core).All(s => done.Contains(s.Id));
        void Complete(OnboardingStep step)
        {
            if (!done.Add(step.Id)) return;
            if (step.Lesson != 0 && !finishedIn.ContainsKey(step.Lesson) && LessonDone(step.Lesson)) finishedIn[step.Lesson] = context!.BattleKey;
            Save();
        }
        void Save()
        {
            PlayerPrefs.SetString(PrefsKey + ".done", string.Join(",", OnboardingSteps.All.Select(s => s.Id).Where(done.Contains)));
            PlayerPrefs.SetInt(PrefsKey + ".skipped", skipped ? 1 : 0);
            PlayerPrefs.Save();
        }
        static OnboardingMark Capture(OnboardingContext c)
        {
            var b = c.Battle;
            var mark = new OnboardingMark { Card = c.Client.SelectedCard, Battle = c.BattleKey, Turn = b?.Turn ?? 0, Memory = c.Client.SessionMemoryVersion,
                Rows = c.Client.HasActionPresentation ? c.Client.ActionPresentationRows : null };
            if (b == null) return mark;
            mark.Reserved.UnionWith(b.Reservations.Select(r => r.Uid));
            foreach (var ult in c.Drawn("Ult:*").Select(r => r.GetComponent<Button>()).Where(x => x != null && x.IsInteractable()))
            {
                var actor = b.Actors.FirstOrDefault(a => a.Id == ult.name.Substring(4));
                if (actor != null) mark.ReadyGauges[actor.Id] = actor.Gauge;
            }
            return mark;
        }
        // The client's live view: a replaced canvas stays inactive under the same name until its deferred Destroy.
        RectTransform? ActiveFrame()
        {
            foreach (Transform child in client!.transform)
                if (child.name == MemoryForgeExpeditionClient.OwnedCanvas && child.gameObject.activeSelf)
                    return child.Find("ExpeditionFrame") as RectTransform;
            return null;
        }
        // A press on the already selected hand card changes no state, so the hand step also listens to the presses themselves.
        void Listen(RectTransform? view)
        {
            if (view == null) return;
            foreach (var button in view.GetComponentsInChildren<Button>(true))
                if (button.name.StartsWith("Hand:", StringComparison.Ordinal)) button.onClick.AddListener(() => { if (context != null) context.HandClicked = true; });
        }

        // ───────── view ─────────
        /// <summary>The width of a step's text: the panel less its padding and its buttons (skip unless Required, acknowledge if Ack).</summary>
        public static float TextWidth(OnboardingStep step)
        {
            int buttons = (step.Required ? 0 : 1) + (step.Ack ? 1 : 0);
            return PanelWidth - 2 * Pad - buttons * (ButtonWidth + Gap);
        }
        public static string Compose(string text) => "<color=#62d6c6><b>안내</b></color>  " + text;
        void Draw(RectTransform? view, OnboardingContext c)
        {
            highlighted.Clear();
            var step = shown;
            if (view == null || step == null) { CurrentText = null; RemoveCoach(); return; }
            bool alt = step.UseAlt != null && step.Alt != null && step.UseAlt(c);
            CurrentText = string.Format(alt ? step.Alt! : step.Text, step.CaptionOf == null ? "" : c.Caption(step.CaptionOf));
            foreach (string name in alt ? step.AltTargets : step.Targets)
                foreach (var target in c.Drawn(name))
                {
                    var button = target.GetComponent<Button>();
                    // A usable button (the mode already chosen is done, so not outlined again) or a named panel.
                    if ((button == null || (button.IsInteractable() && target.name != "Mode:" + c.Client.SelectedMode)) && !highlighted.Contains(target)) highlighted.Add(target);
                }
            string key = view.GetInstanceID() + "|" + step.Id + "|" + CurrentText + "|" + string.Join(",", highlighted.Select(h => h.GetInstanceID()));
            if (coach != null && key == drawnKey) { if (coach.transform.GetSiblingIndex() != view.childCount - 1) coach.transform.SetAsLastSibling(); Pulse(); return; }
            RemoveCoach(); drawnKey = key;
            var root = new GameObject(GuideName, typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(view, false); root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.pivot = new Vector2(0, 1);
            root.offsetMin = root.offsetMax = Vector2.zero; root.SetAsLastSibling(); coach = root.gameObject;
            var corners = new Vector3[4];
            foreach (var target in highlighted)
            {
                target.GetWorldCorners(corners);
                Vector2 min = root.InverseTransformPoint(corners[0]), max = root.InverseTransformPoint(corners[2]);
                Highlight(root, target.name, min.x - Edge, max.y + Edge, max.x - min.x + 2 * Edge, max.y - min.y + 2 * Edge);
            }
            var panel = Box(root, PanelName, PanelX, -PanelY, PanelWidth, PanelHeight, new Color(0.078f, 0.110f, 0.192f, 0.97f));
            var border = panel.gameObject.AddComponent<Outline>(); border.effectColor = Brass; border.effectDistance = new Vector2(2, -2);
            var label = Label(panel, Compose(CurrentText), Pad, 3, TextWidth(step), PanelHeight - 6, FontSize, Ink, TextAnchor.MiddleLeft);
            label.name = TextName;
            float x = PanelWidth - Pad - ButtonWidth;
            if (step.Ack) { CoachButton(panel, NextName, "알겠어요", x, () => Acknowledge(), Brass, Background); x -= ButtonWidth + Gap; }
            if (!step.Required) CoachButton(panel, SkipName, "안내 건너뛰기", x, () => Skip(), Line, Ink);
        }
        void RemoveCoach()
        {
            if (coach != null) { coach.SetActive(false); Destroy(coach); }
            coach = null; drawnKey = null; edges.Clear();
        }
        // The outlines breathe (1.2 s) so the next control catches the eye; with the client's 동작 줄이기 (§14.2) they stay still.
        void Pulse()
        {
            float alpha = client!.ActionPace == MemoryForgeExpeditionClient.ActionPacing.Reduced ? 1f : 0.55f + 0.45f * Mathf.Abs(Mathf.Cos(Time.unscaledTime * Mathf.PI / 1.2f));
            foreach (var edge in edges) if (edge != null) edge.color = new Color(Ink.r, Ink.g, Ink.b, alpha);
        }

        static readonly Color Background = Hex("0d1322"), Line = Hex("2f3d63"), Ink = Hex("e9eef8"), Brass = Hex("e2b45f");
        // An empty group over the control (top-left at x, y in the guide's space) with four edges anchored to its sides.
        void Highlight(RectTransform parent, string target, float x, float y, float width, float height)
        {
            var group = new GameObject(HighlightPrefix + target, typeof(RectTransform)).GetComponent<RectTransform>();
            group.SetParent(parent, false); group.anchorMin = group.anchorMax = new Vector2(0, 1); group.pivot = new Vector2(0, 1);
            group.anchoredPosition = new Vector2(x, y); group.sizeDelta = new Vector2(width, height);
            Side(group, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, Edge));
            Side(group, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, Edge));
            Side(group, "Left", new Vector2(0, 0), new Vector2(0, 1), new Vector2(Edge, 0));
            Side(group, "Right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(Edge, 0));
        }
        void Side(RectTransform group, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(group, false);
            var r = obj.GetComponent<RectTransform>(); r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.pivot = (anchorMin + anchorMax) / 2;
            r.anchoredPosition = Vector2.zero; r.sizeDelta = size;
            // Ink, not brass: the controls to press are often brass themselves (사용 확정, 턴 종료, 저장).
            var image = obj.GetComponent<Image>(); image.color = Ink; image.raycastTarget = false; edges.Add(image);
        }
        // Top-left placement in the coach's own space (pivot at the frame's top-left, y up); never a raycast target.
        static RectTransform Box(RectTransform parent, string name, float x, float y, float width, float height, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false);
            var r = obj.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(width, height);
            var image = obj.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return r;
        }
        Text Label(RectTransform parent, string value, float x, float y, float width, float height, int size, Color color, TextAnchor align)
        {
            var obj = new GameObject("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            var r = obj.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height);
            var text = obj.GetComponent<Text>(); text.font = font; text.text = value; text.fontSize = size; text.color = color; text.alignment = align;
            text.raycastTarget = false; text.supportRichText = true; text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow; return text;
        }
        // On a phone the press area grows to 48dp (the client's TouchUnits): the root is then a transparent hit area from the top edge of
        // the frame down (never above it) and the drawn button its child "Face". It reaches only over header text, never a practice
        // control (those start at x 930 in the header and y 162 below it).
        void CoachButton(RectTransform panel, string name, string caption, float x, Action click, Color back, Color ink)
        {
            float touch = MemoryForgeExpeditionClient.TouchUnits(), faceTop = (PanelHeight - ButtonHeight) / 2;
            bool grown = touch > ButtonHeight;
            float width = Math.Max(ButtonWidth, touch), height = grown ? Math.Max(touch, PanelY + faceTop + ButtonHeight) : ButtonHeight;
            var r = grown ? Box(panel, name, x - (width - ButtonWidth) / 2, PanelY, width, height, Color.clear) : Box(panel, name, x, -faceTop, ButtonWidth, ButtonHeight, back);
            var image = r.GetComponent<Image>(); image.raycastTarget = true;
            var face = grown ? Box(r, "Face", (width - ButtonWidth) / 2, -(PanelY + faceTop), ButtonWidth, ButtonHeight, back) : r;
            var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = face.GetComponent<Image>();
            button.onClick.AddListener(() => { if (button != null && button.IsActive()) click(); });
            Label(face, caption, 4, 4, ButtonWidth - 8, ButtonHeight - 8, FontSize, ink, TextAnchor.MiddleCenter);
        }
        static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
    }
}
