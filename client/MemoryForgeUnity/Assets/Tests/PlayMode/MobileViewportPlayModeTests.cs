#nullable enable
#if UNITY_EDITOR && UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public partial class PracticeServerSessionPlayModeTests
    {
        // Viewport/UI fixture acceptance only; no server/economy/earned-account claims.
        sealed class MobileCase
        {
            public readonly string Name;
            public readonly int Width, Height;
            public readonly Rect Safe;
            public readonly float Density, FontScale;
            public readonly bool Mobile;
            public MobileCase(string name, int width, int height, Rect safe, float density, float fontScale, bool mobile = true)
            { Name = name; Width = width; Height = height; Safe = safe; Density = density; FontScale = fontScale; Mobile = mobile; }
        }
        static IEnumerable<MobileCase> MobileCases()
        {
            yield return new MobileCase("phone-left-450dpi-font100", 2340, 1080, new Rect(72, 0, 2172, 1080), 450f / 160, 1);
            yield return new MobileCase("phone-right-450dpi-font130", 2340, 1080, new Rect(96, 0, 2172, 1080), 450f / 160, 1.3f);
            yield return new MobileCase("s24-left96-right0-450dpi-font100", 2340, 1080, new Rect(96, 0, 2244, 1080), 450f / 160, 1);
            yield return new MobileCase("s24-left0-right96-450dpi-font130", 2340, 1080, new Rect(0, 0, 2244, 1080), 450f / 160, 1.3f);
            yield return new MobileCase("pc-1920x1080", 1920, 1080, new Rect(0, 0, 1920, 1080), 1, 1, false);
            yield return new MobileCase("tablet-4by3-font130", 1440, 1080, new Rect(0, 48, 1440, 1000), 2, 1.3f);
        }

        static void MobileApplyCalibration(MobileCase scenario)
        {
            MemoryForgeExpeditionClient.TouchOverride = null;
            MemoryForgeUiViewport.EditorMetricsOverride = new MemoryForgeUiViewport.Metrics(
                new Vector2(scenario.Width, scenario.Height), scenario.Safe, scenario.Density, scenario.FontScale, scenario.Mobile, scenario.Density * 160);
            MemoryForgeUiViewport.RefreshMetrics();
        }
        static void MobileResetCalibration()
        { MemoryForgeUiViewport.EditorMetricsOverride = null; MemoryForgeUiViewport.RefreshMetrics(); }

        sealed class MobileRenderSurface : IDisposable
        {
            public readonly Camera Camera;
            public readonly MobileCase Scenario;
            readonly RenderTexture texture;
            readonly List<(Canvas Canvas, RenderMode Mode, Camera? Camera, float Plane)> originals = new List<(Canvas, RenderMode, Camera?, float)>();
            public MobileRenderSurface(MobileCase scenario)
            {
                Scenario = scenario; texture = new RenderTexture(scenario.Width, scenario.Height, 24);
                Camera = new GameObject("MobileViewportGeometryCamera").AddComponent<Camera>();
                Camera.targetTexture = texture; Camera.orthographic = true; Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = Color.black;
            }
            public void Bind(GameObject host, string name)
            {
                var canvas = host.GetComponentsInChildren<Canvas>().Single(c => c.name == name);
                if (!originals.Any(o => o.Canvas == canvas)) originals.Add((canvas, canvas.renderMode, canvas.worldCamera, canvas.planeDistance));
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = Camera;
            }
            public void Dispose()
            {
                foreach (var entry in originals.Where(o => o.Canvas != null))
                { entry.Canvas.renderMode = entry.Mode; entry.Canvas.worldCamera = entry.Camera; entry.Canvas.planeDistance = entry.Plane; }
                Camera.targetTexture = null; Object.Destroy(Camera.gameObject); texture.Release(); Object.Destroy(texture);
            }
        }

        // Assertions remain NUnit failures; only their reporting is delayed so every ready screen can be diagnosed.
        static void MobileCheck(List<string>? failures, string scope, Action checks)
        {
            if (failures == null) { checks(); return; }
            // Unity's bundled NUnit lacks Assert.Multiple. Keep the first assertion failure per ready group,
            // then continue the other groups/routes/scenarios and fail the matrix at its end.
            try { checks(); }
            catch (AssertionException error) { failures.Add(scope + ": " + error.Message); }
        }

        static IEnumerator MobileSettle(MobileRenderSurface surface, GameObject screen, string canvasName, string? captureState = null, List<string>? failures = null)
        {
            for (int n = 0; n < 4; n++) { surface.Bind(screen, canvasName); yield return null; }
            Canvas.ForceUpdateCanvases();
            if (captureState != null) yield return MobileOptionalShot(screen, surface.Scenario, captureState, canvasName);
            var frame = screen.GetComponentsInChildren<RectTransform>().Single(r => r.name == "LobbyFrame" || r.name == "GrowthFrame" || r.name == "ServerPracticeFrame");
            var actual = MobilePixels(frame, surface.Camera); var safe = surface.Scenario.Safe;
            MobileCheck(failures, surface.Scenario.Name + "/" + captureState + "/frame", () =>
            {
                Assert.That(actual.xMin, Is.EqualTo(safe.xMin).Within(1), surface.Scenario.Name + " production frame left");
                Assert.That(actual.xMax, Is.EqualTo(safe.xMax).Within(1), surface.Scenario.Name + " production frame right");
                Assert.That(actual.yMin, Is.EqualTo(safe.yMin).Within(1), surface.Scenario.Name + " production frame bottom");
                Assert.That(actual.yMax, Is.EqualTo(safe.yMax).Within(1), surface.Scenario.Name + " production frame top");
            });
        }
        static RectTransform MobilePanel(GameObject host, string name) => host.GetComponentsInChildren<RectTransform>().Single(r => r.name == name);
        static Button MobileButton(GameObject host, string name) => host.GetComponentsInChildren<Button>().Single(b => b.name == name);
        static void MobileClick(GameObject host, string name)
        { var button = MobileButton(host, name); Assert.That(button.IsInteractable(), Is.True, name); button.onClick.Invoke(); }
        static Rect MobilePixels(RectTransform transform, Camera camera)
        {
            var corners = new Vector3[4]; transform.GetWorldCorners(corners);
            var pixels = corners.Select(c => RectTransformUtility.WorldToScreenPoint(camera, c)).ToArray();
            return Rect.MinMaxRect(pixels.Min(p => p.x), pixels.Min(p => p.y), pixels.Max(p => p.x), pixels.Max(p => p.y));
        }
        static void MobileInside(Rect actual, Rect expected, string reason, float tolerance = 1)
        {
            Assert.That(actual.xMin, Is.GreaterThanOrEqualTo(expected.xMin - tolerance), reason + " left");
            Assert.That(actual.xMax, Is.LessThanOrEqualTo(expected.xMax + tolerance), reason + " right");
            Assert.That(actual.yMin, Is.GreaterThanOrEqualTo(expected.yMin - tolerance), reason + " bottom");
            Assert.That(actual.yMax, Is.LessThanOrEqualTo(expected.yMax + tolerance), reason + " top");
        }
        static bool MobileVisible(Component component) => component.gameObject.activeInHierarchy
            && component.GetComponentsInParent<CanvasGroup>().All(g => g.alpha > 0.01f);
        static bool MobileForegroundButton(Button button) => MobileVisible(button)
            && button.GetComponentInParent<ScrollRect>() == null
            && button.name != "FigureTap" && button.name != "MomentTap" && !button.name.EndsWith("Shield", StringComparison.Ordinal);
        static bool MobileAncestor(Transform a, Transform b) => a.IsChildOf(b) || b.IsChildOf(a);
        static void MobileSeparate(Rect a, Rect b, string reason)
        {
            float width = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float height = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            Assert.That(width > 1 && height > 1, Is.False, reason);
        }

        static void MobileWholeText(Text text, MobileCase scenario, Camera camera, float minimumSp)
        {
            if (!MobileVisible(text) || string.IsNullOrWhiteSpace(text.text)) return;
            var settings = text.GetGenerationSettings(text.rectTransform.rect.size);
            // Overflow is not evidence of fitting: every glyph must also fit inside the label's own drawn rectangle.
            settings.verticalOverflow = VerticalWrapMode.Truncate;
            var drawn = new TextGenerator(); drawn.Populate(text.text, settings);
            var open = settings; open.verticalOverflow = VerticalWrapMode.Overflow; open.resizeTextForBestFit = false;
            var whole = new TextGenerator(); whole.Populate(text.text, open);
            string reason = scenario.Name + " / " + text.transform.parent.name + " / " + text.text;
            Assert.That(drawn.characterCountVisible, Is.GreaterThanOrEqualTo(whole.characterCountVisible), reason + " all glyphs must fit");
            int used = text.resizeTextForBestFit ? drawn.fontSizeUsedForBestFit : text.fontSize;
            Assert.That(used, Is.GreaterThanOrEqualTo(text.fontSize), reason + " cannot pass by silently shrinking BestFit");
            var pixelRect = MobilePixels(text.rectTransform, camera);
            float pixelsPerUnit = pixelRect.height / Math.Max(0.01f, text.rectTransform.rect.height);
            float physicalSp = used * pixelsPerUnit / scenario.Density;
            Assert.That(physicalSp + 0.1f, Is.GreaterThanOrEqualTo(minimumSp * scenario.FontScale), reason + " physical sp at requested text scale");
            if (text.GetComponentInParent<ScrollRect>() == null) MobileInside(pixelRect, scenario.Safe, reason);
        }

        static float MobileTextFloor(Text text)
        {
            // Semantic exceptions are limited to metadata, not inferred from whatever tiny font production chose.
            if (text.text == "일러스트 준비 중" || text.text == "그림 준비 중" || text.text == "편성"
                || text.text.StartsWith("보유 · 공명", StringComparison.Ordinal)
                || text.text.Contains("연습 편성:") || text.text.StartsWith("위아래로 밀어", StringComparison.Ordinal)) return 10;
            var button = text.GetComponentInParent<Button>();
            if (button == null) return 12;
            if (button.name.StartsWith("Companion:", StringComparison.Ordinal))
                return text.fontStyle == FontStyle.Bold ? 14 : 12;
            if (button.name.StartsWith("GrowthCard:", StringComparison.Ordinal))
                return text.text.StartsWith("행동력 ", StringComparison.Ordinal) || text.text == "사용 불가" ? 12 : 14;
            if (button.name.StartsWith("GrowthEnemy:", StringComparison.Ordinal))
                return text.text.StartsWith("이번 ", StringComparison.Ordinal) || text.text.Contains(" / ") ? 12 : 14;
            var captions = button.GetComponentsInChildren<Text>();
            return captions.Length <= 1 || text.fontStyle == FontStyle.Bold || captions.First() == text ? 14 : 12;
        }

        static void MobilePanelAcceptance(RectTransform panel, MobileCase scenario, Camera camera)
        {
            var controls = panel.GetComponentsInChildren<Button>().Where(MobileForegroundButton).ToArray();
            foreach (var button in controls)
            {
                var rect = MobilePixels((RectTransform)button.transform, camera); MobileInside(rect, scenario.Safe, scenario.Name + "/" + button.name);
                if (scenario.Mobile)
                {
                    Assert.That(rect.width / scenario.Density + 0.1f, Is.GreaterThanOrEqualTo(48), button.name + " 48dp width");
                    Assert.That(rect.height / scenario.Density + 0.1f, Is.GreaterThanOrEqualTo(48), button.name + " 48dp height");
                }
            }
            for (int i = 0; i < controls.Length; i++) for (int j = i + 1; j < controls.Length; j++)
                if (!MobileAncestor(controls[i].transform, controls[j].transform))
                    MobileSeparate(MobilePixels((RectTransform)controls[i].transform, camera), MobilePixels((RectTransform)controls[j].transform, camera), controls[i].name + " overlaps " + controls[j].name);
            var labels = panel.GetComponentsInChildren<Text>().Where(t => MobileVisible(t) && !string.IsNullOrWhiteSpace(t.text)).ToArray();
            for (int i = 0; i < labels.Length; i++) for (int j = i + 1; j < labels.Length; j++)
                if (labels[i].transform.parent == labels[j].transform.parent)
                    MobileSeparate(MobilePixels(labels[i].rectTransform, camera), MobilePixels(labels[j].rectTransform, camera),
                        labels[i].text + " overlaps sibling text " + labels[j].text);
            foreach (var text in labels)
            {
                // All captions still need whole glyphs. Primary body/button/title floors are asserted separately below.
                MobileWholeText(text, scenario, camera, MobileTextFloor(text));
                if (text.GetComponentInParent<ScrollRect>() != null) continue;
                foreach (var button in controls)
                    if (!MobileAncestor(text.transform, button.transform))
                        MobileSeparate(MobilePixels(text.rectTransform, camera), MobilePixels((RectTransform)button.transform, camera), text.text + " hidden by " + button.name);
            }
        }
        static void MobilePrimary(RectTransform panel, MobileCase scenario, Camera camera, string exact, float minimumSp)
        {
            var text = panel.GetComponentsInChildren<Text>().Single(t => t.text == exact);
            MobileWholeText(text, scenario, camera, minimumSp);
        }
        static void MobileTouchHits(RectTransform panel, Camera camera)
        {
            var raycaster = panel.GetComponentInParent<GraphicRaycaster>();
            Assert.That(EventSystem.current, Is.Not.Null); Assert.That(raycaster, Is.Not.Null);
            foreach (var button in panel.GetComponentsInChildren<Button>().Where(b => MobileForegroundButton(b) && b.IsInteractable()))
            {
                var rect = MobilePixels((RectTransform)button.transform, camera);
                foreach (var uv in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.08f, 0.5f), new Vector2(0.92f, 0.5f) })
                {
                    var pointer = new PointerEventData(EventSystem.current) { position = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, uv.x), Mathf.Lerp(rect.yMin, rect.yMax, uv.y)) };
                    var hits = new List<RaycastResult>(); raycaster.Raycast(pointer, hits);
                    Assert.That(hits.FirstOrDefault().gameObject?.GetComponentInParent<Button>(), Is.EqualTo(button), button.name + " drawn touch surface is unobstructed");
                }
            }
        }
        static IEnumerable<ScrollRect> MobileOwningScrolls(Transform child) => child.GetComponentsInParent<ScrollRect>()
            .Where(s => s.isActiveAndEnabled && s.content != null && child.IsChildOf(s.content));
        static IEnumerable<Rect> MobileClippingRects(Transform child, Camera camera) =>
            child.GetComponentsInParent<RectMask2D>().Where(m => m.isActiveAndEnabled).Select(m => MobilePixels((RectTransform)m.transform, camera))
                .Concat(child.GetComponentsInParent<Mask>().Where(m => m.isActiveAndEnabled).Select(m => MobilePixels((RectTransform)m.transform, camera)));
        static bool MobileFullyInside(Rect rect, Rect clip) => rect.xMin >= clip.xMin - 1 && rect.xMax <= clip.xMax + 1 && rect.yMin >= clip.yMin - 1 && rect.yMax <= clip.yMax + 1;
        static void MobileVisibleScrollButtons(ScrollRect scroll, MobileCase scenario, Camera camera, List<string>? failures)
        {
            var viewport = MobilePixels(scroll.viewport, camera);
            foreach (var button in scroll.content.GetComponentsInChildren<Button>().Where(b => MobileVisible(b) && b.GetComponentInParent<ScrollRect>() == scroll))
            {
                var rect = MobilePixels((RectTransform)button.transform, camera);
                // Partial rows are intentionally clipped. Test their complete hit surfaces only after they have scrolled into view.
                if (!MobileFullyInside(rect, viewport) || !MobileFullyInside(rect, scenario.Safe)
                    || MobileClippingRects(button.transform, camera).Any(clip => !MobileFullyInside(rect, clip))) continue;
                MobileCheck(failures, scenario.Name + "/" + scroll.name + "/visible-touch/" + button.name, () =>
                {
                    MobileInside(rect, scenario.Safe, button.name + " visible scroll control");
                    if (scenario.Mobile)
                    {
                        Assert.That(rect.width / scenario.Density + 0.1f, Is.GreaterThanOrEqualTo(48), button.name + " scroll48dp width");
                        Assert.That(rect.height / scenario.Density + 0.1f, Is.GreaterThanOrEqualTo(48), button.name + " scroll48dp height");
                    }
                    if (!button.IsInteractable()) return;
                    var raycaster = button.GetComponentInParent<GraphicRaycaster>(); Assert.That(raycaster, Is.Not.Null);
                    foreach (var uv in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.08f, 0.5f), new Vector2(0.92f, 0.5f), new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.92f) })
                    {
                        var pointer = new PointerEventData(EventSystem.current) { position = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, uv.x), Mathf.Lerp(rect.yMin, rect.yMax, uv.y)) };
                        var hits = new List<RaycastResult>(); raycaster.Raycast(pointer, hits);
                        Assert.That(hits.FirstOrDefault().gameObject?.GetComponentInParent<Button>(), Is.EqualTo(button), button.name + " visible scroll touch surface is unobstructed");
                    }
                });
            }
        }

        static void MobileSetScrollPosition(ScrollRect scroll, bool horizontal, float position, Vector2 other)
        {
            scroll.StopMovement(); scroll.normalizedPosition = horizontal ? new Vector2(position, other.y) : new Vector2(other.x, position);
        }
        static void MobileScrollTextMetrics(MobileCase scenario, ScrollRect scroll, RectTransform row, int end, float position, Rect viewport, Rect pixels)
        {
            Debug.Log("[MobileViewportScroll] " + scenario.Name + " end=" + end + " normalized=" + position
                + " viewportWidth=" + viewport.width + " viewportHeight=" + viewport.height
                + " textWidth=" + pixels.width + " textHeight=" + pixels.height
                + " contentWidth=" + scroll.content.rect.width + " contentHeight=" + scroll.content.rect.height
                + " textLeft=" + pixels.xMin + " textRight=" + pixels.xMax + " textBottom=" + pixels.yMin + " textTop=" + pixels.yMax);
        }
        static IEnumerator MobileScrollTextReachability(ScrollRect scroll, RectTransform row, int end, MobileCase scenario, Camera camera, List<string>? failures)
        {
            bool horizontal = scroll.horizontal && !scroll.vertical; var before = scroll.normalizedPosition;
            try
            {
                MobileSetScrollPosition(scroll, horizontal, 0, before); yield return null; Canvas.ForceUpdateCanvases();
                var viewport = MobilePixels(scroll.viewport, camera); var atZero = MobilePixels(row, camera);
                MobileScrollTextMetrics(scenario, scroll, row, end, 0, viewport, atZero);
                MobileSetScrollPosition(scroll, horizontal, 1, before); yield return null; Canvas.ForceUpdateCanvases();
                var atOne = MobilePixels(row, camera); MobileScrollTextMetrics(scenario, scroll, row, end, 1, viewport, atOne);
                float delta = horizontal ? atOne.xMin - atZero.xMin : atOne.yMin - atZero.yMin;
                bool oversized = horizontal ? atZero.width > viewport.width : atZero.height > viewport.height;
                if (!oversized)
                {
                    float distance = horizontal ? viewport.center.x - atZero.center.x : viewport.center.y - atZero.center.y;
                    float position = Mathf.Abs(delta) < 0.01f ? 0 : Mathf.Clamp01(distance / delta);
                    MobileSetScrollPosition(scroll, horizontal, position, before); yield return null; Canvas.ForceUpdateCanvases();
                    var actual = MobilePixels(row, camera); MobileScrollTextMetrics(scenario, scroll, row, end, position, viewport, actual);
                    MobileCheck(failures, scenario.Name + "/" + scroll.name + "/text-full-reach-" + end,
                        () => MobileInside(actual, viewport, "short scroll paragraph must fit at an actual reachable position"));
                }
                else
                {
                    for (int edge = 0; edge < 2; edge++)
                    {
                        float sourceEdge = horizontal ? (edge == 0 ? atZero.xMin : atZero.xMax) : (edge == 0 ? atZero.yMax : atZero.yMin);
                        float targetEdge = horizontal ? (edge == 0 ? viewport.xMin : viewport.xMax) : (edge == 0 ? viewport.yMax : viewport.yMin);
                        float position = Mathf.Abs(delta) < 0.01f ? 0 : Mathf.Clamp01((targetEdge - sourceEdge) / delta);
                        MobileSetScrollPosition(scroll, horizontal, position, before); yield return null; Canvas.ForceUpdateCanvases();
                        var actual = MobilePixels(row, camera); MobileScrollTextMetrics(scenario, scroll, row, end, position, viewport, actual);
                        var endpoint = horizontal ? new Rect(edge == 0 ? actual.xMin : actual.xMax - 1, actual.center.y, 1, 1)
                            : new Rect(actual.center.x, edge == 0 ? actual.yMax - 1 : actual.yMin, 1, 1);
                        MobileCheck(failures, scenario.Name + "/" + scroll.name + "/text-edge-reach-" + end + "-" + edge,
                            () => MobileInside(endpoint, viewport, "long scroll paragraph must expose both ends, at possibly different positions"));
                    }
                }
            }
            finally { scroll.normalizedPosition = before; }
        }

        static IEnumerator MobileScrollButtonReachability(ScrollRect scroll, Button button, MobileCase scenario, Camera camera, List<string>? failures)
        {
            var rect = (RectTransform)button.transform;
            // Inner scrolls expose the button in their own viewport first. Moving each enclosing
            // scroll afterwards translates that viewport and its already-exposed child together.
            var ancestors = MobileOwningScrolls(button.transform).ToArray();
            Assert.That(ancestors.Contains(scroll), Is.True, button.name + " belongs to the requested scroll");
            var before = ancestors.Select(s => s.normalizedPosition).ToArray(); var measured = new List<string>();
            try
            {
                for (int index = 0; index < ancestors.Length; index++)
                {
                    var owner = ancestors[index];
                    foreach (bool horizontal in new[] { true, false }.Where(axis => axis ? owner.horizontal : owner.vertical))
                    {
                        var other = owner.normalizedPosition;
                        MobileSetScrollPosition(owner, horizontal, 0, other); yield return null; Canvas.ForceUpdateCanvases(); var zero = MobilePixels(rect, camera);
                        MobileSetScrollPosition(owner, horizontal, 1, other); yield return null; Canvas.ForceUpdateCanvases(); var one = MobilePixels(rect, camera);
                        var viewport = MobilePixels(owner.viewport, camera); float delta = horizontal ? one.center.x - zero.center.x : one.center.y - zero.center.y;
                        float distance = horizontal ? viewport.center.x - zero.center.x : viewport.center.y - zero.center.y;
                        float position = Mathf.Abs(delta) < 0.01f ? 0 : Mathf.Clamp01(distance / delta);
                        MobileSetScrollPosition(owner, horizontal, position, other); yield return null; Canvas.ForceUpdateCanvases();
                        measured.Add(owner.name + (horizontal ? "/x" : "/y") + " at0=" + zero + " at1=" + one + " normalized=" + position
                            + " viewport=" + viewport + " content=" + owner.content.rect + " contentAnchored=" + owner.content.anchoredPosition);
                    }
                }
                var actual = MobilePixels(rect, camera);
                string geometry = "row=" + button.name + " actual=" + actual + " ancestors=[" + string.Join(";", measured) + "] anchors=" + rect.anchorMin + "/" + rect.anchorMax;
                Debug.Log("[MobileViewportScrollButton] " + scenario.Name + " " + geometry);
                MobileCheck(failures, scenario.Name + "/" + scroll.name + "/" + button.name + "/full-reach", () =>
                {
                    foreach (var owner in ancestors) MobileInside(actual, MobilePixels(owner.viewport, camera), geometry + " fully reachable through " + owner.name);
                    foreach (var clip in MobileClippingRects(button.transform, camera)) MobileInside(actual, clip, geometry + " visible through every ancestor mask");
                    MobileInside(actual, scenario.Safe, geometry + " safe button");
                    if (scenario.Mobile)
                    {
                        Assert.That(actual.width / scenario.Density + 0.1f, Is.GreaterThanOrEqualTo(48), button.name + " actual48dp width");
                        Assert.That(actual.height / scenario.Density + 0.1f, Is.GreaterThanOrEqualTo(48), button.name + " actual48dp height");
                    }
                    foreach (var text in button.GetComponentsInChildren<Text>()) MobileWholeText(text, scenario, camera, MobileTextFloor(text));
                    if (!button.IsInteractable()) return;
                    var raycaster = button.GetComponentInParent<GraphicRaycaster>(); Assert.That(raycaster, Is.Not.Null);
                    foreach (var uv in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.08f, 0.5f), new Vector2(0.92f, 0.5f), new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.92f) })
                    {
                        var pointer = new PointerEventData(EventSystem.current) { position = new Vector2(Mathf.Lerp(actual.xMin, actual.xMax, uv.x), Mathf.Lerp(actual.yMin, actual.yMax, uv.y)) };
                        var hits = new List<RaycastResult>(); raycaster.Raycast(pointer, hits);
                        Assert.That(hits.FirstOrDefault().gameObject?.GetComponentInParent<Button>(), Is.EqualTo(button), button.name + " complete drawn surface receives its own raycast");
                    }
                });
            }
            finally { for (int index = ancestors.Length - 1; index >= 0; index--) { ancestors[index].StopMovement(); ancestors[index].normalizedPosition = before[index]; } }
        }

        static IEnumerator MobileScrollEnds(RectTransform panel, MobileCase scenario, Camera camera, List<string>? failures = null)
        {
            foreach (var scroll in panel.GetComponentsInChildren<ScrollRect>().Where(s => s.isActiveAndEnabled))
            {
                var rows = scroll.content.Cast<Transform>().Where(t => t.gameObject.activeInHierarchy).OfType<RectTransform>()
                    .Where(r => r.GetComponent<Button>() != null || r.GetComponent<Text>() != null).ToArray();
                if (rows.Length == 0) continue;
                bool horizontal = scroll.horizontal && !scroll.vertical;
                rows = horizontal ? rows.OrderBy(r => r.anchoredPosition.x).ToArray() : rows.OrderByDescending(r => r.anchoredPosition.y).ToArray();
                var before = scroll.normalizedPosition;
                try
                {
                    for (int end = 0; end < 2; end++)
                    {
                        scroll.StopMovement(); scroll.normalizedPosition = horizontal ? new Vector2(end, before.y) : new Vector2(before.x, 1 - end);
                        yield return null; Canvas.ForceUpdateCanvases();
                        var row = end == 0 ? rows.First() : rows.Last();
                        MobileCheck(failures, scenario.Name + "/" + panel.name + "/" + scroll.name + "/scroll-end-" + end, () =>
                        {
                            var visible = MobilePixels(scroll.viewport, camera);
                            // An inner viewport may itself be clipped by an outer scroller. Its controls
                            // must still be fully exposable through every clip in the traversal below.
                            if (!MobileOwningScrolls(scroll.transform).Any()) MobileInside(visible, scenario.Safe, scroll.name + " viewport");
                            var rowPixels = MobilePixels(row, camera);
                            // Content can precede/follow the first/last control. Endpoint positions are observations,
                            // not a requirement that a button must fit there; every button is proved fully reachable below.
                            foreach (var text in row.GetComponentsInChildren<Text>()) MobileWholeText(text, scenario, camera, MobileTextFloor(text));
                        });
                        if (row.GetComponent<Button>() == null) yield return MobileScrollTextReachability(scroll, row, end, scenario, camera, failures);
                        MobileVisibleScrollButtons(scroll, scenario, camera, failures);
                    }
                    foreach (var button in scroll.content.GetComponentsInChildren<Button>().Where(b => MobileVisible(b) && b.GetComponentInParent<ScrollRect>() == scroll))
                        yield return MobileScrollButtonReachability(scroll, button, scenario, camera, failures);
                }
                finally { scroll.normalizedPosition = before; }
            }
        }
        static IEnumerator MobileRevealRow(RectTransform row, Camera camera, List<string>? failures = null)
        {
            var scroll = row.GetComponentInParent<ScrollRect>();
            Assert.That(scroll, Is.Not.Null, row.name + " must belong to a real scroll viewport");
            for (int step = 0; step <= 20; step++)
            {
                scroll.StopMovement(); scroll.verticalNormalizedPosition = 1 - step / 20f;
                yield return null; Canvas.ForceUpdateCanvases();
                var visible = MobilePixels(scroll.viewport, camera); var pixels = MobilePixels(row, camera);
                if (visible.Contains(pixels.center)) yield break;
            }
            MobileCheck(failures, row.name + "/scroll-reveal", () => Assert.Fail(row.name + " cannot be reached by the real ScrollRect"));
        }

        static IEnumerator MobileOptionalShot(GameObject screen, MobileCase scenario, string state, string canvasName = MemoryForgeLobbyClient.OwnedCanvas)
        {
            string? directory = Environment.GetEnvironmentVariable("MF_MOBILE_VIEWPORT_SHOTS");
            if (string.IsNullOrWhiteSpace(directory)) yield break;
            Directory.CreateDirectory(directory!);
            yield return LobbyMomentCapturePlayModeTests.Shot(screen, Path.Combine(directory!, scenario.Name + "-" + state + ".jpg"), scenario.Width, scenario.Height, canvasName);
        }
        static void MobileHomeEdges(GameObject screen, MobileCase scenario, Camera camera)
        {
            var art = MobilePixels(MobilePanel(screen, "Stage"), camera);
            Assert.That(art.width / art.height, Is.EqualTo(1600f / 900).Within(0.001), "fixed art stage retains its authored aspect");
            MobileInside(new Rect(0, 0, scenario.Width, scenario.Height), MobilePixels(MobilePanel(screen, "FullBleedBackdrop"), camera), "full-bleed backdrop covers the entire native viewport");
            var left = MobilePixels((RectTransform)MobileButton(screen, "HomeCatalogueShortcut").transform, camera);
            var right = MobilePixels((RectTransform)MobileButton(screen, "StartExpedition").transform, camera);
            var dock = MobilePixels(MobilePanel(screen, "HomeDock"), camera);
            float leftDp = (left.xMin - scenario.Safe.xMin) / scenario.Density, rightDp = (scenario.Safe.xMax - right.xMax) / scenario.Density;
            if (scenario.Mobile)
            {
                Assert.That(leftDp, Is.InRange(4f, 40f), scenario.Name + " left HUD is anchored to safe edge, not letterboxed art");
                Assert.That(rightDp, Is.InRange(4f, 40f), scenario.Name + " right HUD is anchored to safe edge, not letterboxed art");
                Assert.That(leftDp, Is.EqualTo(rightDp).Within(4), scenario.Name + " balanced left/right safe padding");
                Assert.That((dock.yMin - scenario.Safe.yMin) / scenario.Density, Is.InRange(4f, 40f), scenario.Name + " bottom dock safe padding");
            }
            else
            {
                float leftRatio = (left.xMin - scenario.Safe.xMin) / scenario.Safe.width, rightRatio = (scenario.Safe.xMax - right.xMax) / scenario.Safe.width;
                Assert.That(leftRatio, Is.InRange(0f, 0.05f), "desktop left gutter uses safe-width proportion, not Android dp");
                Assert.That(rightRatio, Is.InRange(0f, 0.05f));
                Assert.That(leftRatio, Is.EqualTo(rightRatio).Within(4f / scenario.Safe.width), "desktop left/right alignment");
                Assert.That((dock.yMin - scenario.Safe.yMin) / scenario.Safe.height, Is.InRange(0f, 0.05f), "desktop bottom gutter is proportionate to the actual safe height");
            }
            Assert.That(dock.width, Is.GreaterThanOrEqualTo(scenario.Safe.width * 0.9f), scenario.Name + " dock spans the usable viewport");
            var title = MobilePanel(screen, "LobbySide").GetComponentsInChildren<Text>().Single(t => t.text == "기억 공방");
            Assert.That(MobilePixels(title.rectTransform, camera).xMin, Is.EqualTo(left.xMin).Within(2 * scenario.Density), "title and left navigation share an edge");
            var settings = MobilePixels((RectTransform)MobileButton(screen, "OpenSettings").transform, camera);
            Assert.That(settings.xMax, Is.EqualTo(right.xMax).Within(2 * scenario.Density), "right utility and primary action share an edge");
        }

        [Test]
        public void MobileViewportGeometryConservesTheEntireAsymmetricSafeRectangle()
        {
            foreach (var scenario in MobileCases())
            {
                var metrics = new MemoryForgeUiViewport.Metrics(new Vector2(scenario.Width, scenario.Height), scenario.Safe, scenario.Density, scenario.FontScale, scenario.Mobile);
                foreach (var canvasSize in new[] { new Vector2(1600, 900), new Vector2(1873, 865) })
                {
                    var geometry = MemoryForgeUiViewport.Calculate(metrics, canvasSize);
                    Assert.That(geometry.Size.x * geometry.PixelsPerUnit, Is.EqualTo(scenario.Safe.width).Within(0.01), scenario.Name + " preserves safe width");
                    Assert.That(geometry.Size.y * geometry.PixelsPerUnit, Is.EqualTo(scenario.Safe.height).Within(0.01), scenario.Name + " preserves safe height");
                    Assert.That(geometry.Scale, Is.GreaterThan(0));
                    var centre = new Vector2((geometry.Position.x / canvasSize.x + 0.5f) * scenario.Width, (geometry.Position.y / canvasSize.y + 0.5f) * scenario.Height);
                    Assert.That(centre.x, Is.EqualTo(scenario.Safe.center.x).Within(0.01)); Assert.That(centre.y, Is.EqualTo(scenario.Safe.center.y).Within(0.01));
                }
            }
        }

        [Test]
        public void MobileWholeTextKeepsItsAuthoredBodyRoleAcrossRepeatedCalibrationAndFontScaleChanges()
        {
            var labelObject = new GameObject("MobileBodyTypography", typeof(RectTransform), typeof(Text));
            var text = labelObject.GetComponent<Text>();
            text.font = Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular"); Assert.That(text.font, Is.Not.Null);
            text.text = "계정 진행을 확인하면 성장 공방을 사용할 수 있어요.";
            text.fontSize = 24; text.rectTransform.sizeDelta = new Vector2(600, 44);
            try
            {
                MobileApplyCalibration(new MobileCase("body-font130", 2340, 1080, new Rect(96, 0, 2244, 1080), 450f / 160, 1.3f));
                MemoryForgeUiViewport.WholeText(text);
                // 14sp body × 2.8125 density × 1.3 fontScale / 1.2 physical pixels per authored unit = 42.65625, rounded up.
                Assert.That(text.fontSize, Is.EqualTo(43), "authored24 is a body, not an18sp title");
                float height = text.rectTransform.rect.height;
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    MemoryForgeUiViewport.WholeText(text);
                    Assert.That(text.fontSize, Is.EqualTo(43), "a previously enlarged body cannot be reclassified as a title");
                    Assert.That(text.rectTransform.rect.height, Is.EqualTo(height).Within(0.01), "same metrics do not accumulate height");
                }
                MobileApplyCalibration(new MobileCase("body-font100", 2340, 1080, new Rect(96, 0, 2244, 1080), 450f / 160, 1));
                MemoryForgeUiViewport.WholeText(text);
                Assert.That(text.fontSize, Is.EqualTo(33), "return to14sp body at1.0 rather than retaining the previous inflation");
                Assert.That(text.resizeTextForBestFit, Is.False);
            }
            finally { MobileResetCalibration(); Object.Destroy(labelObject); }
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator MobileHomeRosterAndDetailsRespectSafePixelsTextScaleAndReachableScrollEnds()
        {
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            var failures = new List<string>();
            try
            {
                foreach (var scenario in MobileCases())
                {
                    Debug.Log("[MobileViewport] " + scenario.Name);
                    MobileApplyCalibration(scenario); EquipmentLobbyFixture();
                    var screen = new GameObject("MobileLobby:" + scenario.Name); var lobby = screen.AddComponent<MemoryForgeLobbyClient>();
                    using (var surface = new MobileRenderSurface(scenario))
                    {
                        try
                        {
                            yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => Session());
                            yield return Until(() => lobby.AccountProgression?.Resources != null && screen.GetComponentsInChildren<Button>(true).Any(b => b.name == "RefreshRecruitment" && b.interactable), "viewport fixture account GET");
                            yield return MobileSettle(surface, screen, MemoryForgeLobbyClient.OwnedCanvas, "home", failures);
                            var home = MobilePanel(screen, "LobbySide");
                            MobileCheck(failures, scenario.Name + "/home", () =>
                            {
                                MobilePanelAcceptance(home, scenario, surface.Camera); MobileHomeEdges(screen, scenario, surface.Camera);
                                MobilePrimary(home, scenario, surface.Camera, "기억 공방", 18); MobileTouchHits(home, surface.Camera);
                            });

                            MobileClick(screen, "OpenCompanions"); yield return MobileSettle(surface, screen, MemoryForgeLobbyClient.OwnedCanvas, "roster", failures);
                            var roster = MobilePanel(screen, "RosterPanel");
                            MobileCheck(failures, scenario.Name + "/roster", () =>
                            {
                                MobilePanelAcceptance(roster, scenario, surface.Camera);
                                MobilePrimary(roster, scenario, surface.Camera, "동료 기록", 18); MobileTouchHits(roster, surface.Camera);
                            });
                            yield return MobileScrollEnds(roster, scenario, surface.Camera, failures);
                            var definition = ClientBattleContent.Load().Battle.Characters.OrderByDescending(c => c.Passive.Text.Length + c.Ult.Text.Length).First();
                            // Use the real roster button and the longest existing skill descriptions, never a synthetic string.
                            yield return MobileRevealRow((RectTransform)MobileButton(screen, "Companion:" + definition.Id).transform, surface.Camera, failures);
                            MobileClick(screen, "Companion:" + definition.Id); yield return MobileSettle(surface, screen, MemoryForgeLobbyClient.OwnedCanvas, "details", failures);
                            var details = MobilePanel(screen, "CompanionDetails");
                            MobileCheck(failures, scenario.Name + "/details", () =>
                            {
                                MobilePanelAcceptance(details, scenario, surface.Camera);
                                MobilePrimary(details, scenario, surface.Camera, definition.Name, 18);
                                MobilePrimary(details, scenario, surface.Camera, definition.Passive.Text, 14); MobilePrimary(details, scenario, surface.Camera, definition.Ult.Text, 14);
                            });
                            yield return MobileScrollEnds(details, scenario, surface.Camera, failures);
                            MobileCheck(failures, scenario.Name + "/details-touch", () => MobileTouchHits(details, surface.Camera));

                            MobileClick(screen, "ShowCompanionCards"); yield return MobileSettle(surface, screen, MemoryForgeLobbyClient.OwnedCanvas, "cards", failures);
                            details = MobilePanel(screen, "CompanionDetails");
                            MobileCheck(failures, scenario.Name + "/cards", () => MobilePanelAcceptance(details, scenario, surface.Camera));
                            yield return MobileScrollEnds(details, scenario, surface.Camera, failures);
                            var shield = MobilePanel(screen, "ModalShield");
                            MobileCheck(failures, scenario.Name + "/modal-shield", () => MobileInside(scenario.Safe, MobilePixels(shield, surface.Camera), "expanded modal shield covers safe area"));
                            Assert.That(host.Posts, Is.Empty, "viewport acceptance never mutates an account");
                        }
                        finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); }
                    }
                    yield return null; MobileResetCalibration();
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { MobileResetCalibration(); MemoryForgeExpeditionClient.TouchOverride = null; MemoryForgeUiMotion.Reduced = reduced; }
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator MobileGrowthWorkshopAndBattleKeepWholePrimaryTextAnd48DpControls()
        {
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            var failures = new List<string>();
            try
            {
                foreach (var scenario in MobileCases())
                {
                    Debug.Log("[MobileViewport] " + scenario.Name);
                    MobileApplyCalibration(scenario); EquipmentLobbyFixture();
                    var screen = new GameObject("MobileGrowthWorkshop:" + scenario.Name); var lobby = screen.AddComponent<MemoryForgeLobbyClient>();
                    using (var surface = new MobileRenderSurface(scenario))
                    {
                        try
                        {
                            yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => Session());
                            yield return Until(() => lobby.AccountProgression?.Resources != null && screen.GetComponentsInChildren<Button>(true).Any(b => b.name == "RefreshRecruitment" && b.interactable), "workshop fixture GET");
                            MobileClick(screen, "OpenGrowth");
                            yield return Until(() => screen.GetComponentsInChildren<Button>().Any(b => b.name == "OpenCharacterGrowth" && b.IsInteractable()), "growth panel finished GET");
                            yield return MobileSettle(surface, screen, MemoryForgeLobbyClient.OwnedCanvas, "growth-workshop", failures);
                            var workshop = MobilePanel(screen, "GrowthPanel");
                            MobileCheck(failures, scenario.Name + "/growth-workshop", () =>
                            {
                                MobilePanelAcceptance(workshop, scenario, surface.Camera);
                                MobilePrimary(workshop, scenario, surface.Camera, "성장 공방", 18); MobileTouchHits(workshop, surface.Camera);
                            });
                            yield return MobileScrollEnds(workshop, scenario, surface.Camera, failures);
                        }
                        finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); }
                    }
                    yield return null;
                    GrowthFixture(true); var state = (JObject)growthEncounters[GrowthEncounter]["state"]!;
                    foreach (var enemy in state["enemies"]!.OfType<JObject>()) { enemy["id"] = "e_moth"; enemy["name"] = "종탑 나방"; enemy["alive"] = true; enemy["maxHp"] = enemy["hp"]!.DeepClone(); }
                    using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
                    using (var surface = new MobileRenderSurface(scenario))
                    {
                        MemoryForgeAccountConnection.SetCurrent(owner); screen = new GameObject("MobileGrowthBattle:" + scenario.Name); screen.SetActive(false);
                        var view = screen.AddComponent<MemoryForgeGrowthClient>(); screen.SetActive(true);
                        try
                        {
                            yield return Until(() => view.Snapshot != null && !view.Busy, "battle fixture GET");
                            yield return MobileSettle(surface, screen, MemoryForgeGrowthClient.OwnedCanvas, "growth-battle", failures);
                            var frame = MobilePanel(screen, "GrowthFrame");
                            MobileCheck(failures, scenario.Name + "/growth-battle", () =>
                            {
                                MobilePanelAcceptance(frame, scenario, surface.Camera); MobileTouchHits(frame, surface.Camera);
                                foreach (var text in MobilePanel(screen, "GrowthParty").GetComponentsInChildren<Text>().Where(t => t.transform.parent.name == "GrowthParty")) MobileWholeText(text, scenario, surface.Camera, 14);
                            });
                            yield return MobileScrollEnds(frame, scenario, surface.Camera, failures);

                            Assert.That(host.Posts, Is.Empty, "read-only layout checks send no command");
                        }
                        finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); }
                    }
                    yield return null; MobileResetCalibration();
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { MobileResetCalibration(); MemoryForgeExpeditionClient.TouchOverride = null; MemoryForgeUiMotion.Reduced = reduced; }
        }
        static Rect MobileIntersection(Rect a, Rect b)
        {
            float left = Mathf.Max(a.xMin, b.xMin), right = Mathf.Min(a.xMax, b.xMax), bottom = Mathf.Max(a.yMin, b.yMin), top = Mathf.Min(a.yMax, b.yMax);
            return right > left && top > bottom ? Rect.MinMaxRect(left, bottom, right, top) : new Rect();
        }
        static List<Rect> MobileVisibleGlyphs(Text text, MobileCase scenario, Camera camera)
        {
            var result = new List<Rect>();
            if (!MobileVisible(text) || string.IsNullOrWhiteSpace(text.text) || text.color.a <= 0.01f || text.canvasRenderer.cull) return result;
            Rect clip = scenario.Safe;
            foreach (var mask in text.GetComponentsInParent<RectMask2D>().Where(m => m.isActiveAndEnabled)) clip = MobileIntersection(clip, MobilePixels((RectTransform)mask.transform, camera));
            foreach (var mask in text.GetComponentsInParent<Mask>().Where(m => m.isActiveAndEnabled)) clip = MobileIntersection(clip, MobilePixels((RectTransform)mask.transform, camera));
            if (clip.width <= 0 || clip.height <= 0) return result;
            // Read the actual uGUI text mesh, including its font rounding; do not treat a parent's unmasked bounds as clipping.
            var mesh = text.canvasRenderer.GetMesh();
            Assert.That(mesh, Is.Not.Null, "active non-culled owned-slot text needs actual rendered geometry");
            // CanvasRenderer owns this mesh: read its quads without modifying or destroying it.
            var vertices = mesh.vertices; Assert.That(vertices.Length, Is.GreaterThanOrEqualTo(4), "visible owned-slot text cannot pass with an empty glyph mesh");
            for (int start = 0; start + 3 < vertices.Length; start += 4)
            {
                var points = Enumerable.Range(start, 4).Select(i => RectTransformUtility.WorldToScreenPoint(camera, text.rectTransform.TransformPoint(vertices[i]))).ToArray();
                var quad = Rect.MinMaxRect(points.Min(v => v.x), points.Min(v => v.y), points.Max(v => v.x), points.Max(v => v.y));
                var visible = MobileIntersection(quad, clip); if (visible.width > 0.1f && visible.height > 0.1f) result.Add(visible);
            }
            return result;
        }
        static void MobileOwnedPartyTextSeparation(GameObject screen, MobileCase scenario, Camera camera, List<string> failures)
        {
            var row = MobilePanel(screen, "OwnedPartyRow");
            foreach (RectTransform slot in row)
            {
                var status = slot.GetComponentsInChildren<Text>().Where(t => t.transform.parent == slot
                    && (t.text.StartsWith("Lv.", StringComparison.Ordinal) || t.text.StartsWith("임시 동행", StringComparison.Ordinal))).ToArray();
                var portrait = slot.Find("Portrait"); var fallback = portrait == null ? Array.Empty<Text>() : portrait.GetComponentsInChildren<Text>();
                var weapons = slot.GetComponentsInChildren<ScrollRect>().SelectMany(sc => sc.content.GetComponentsInChildren<Text>()).ToArray();
                var pairs = status.SelectMany(a => weapons.Select(b => (A: a, B: b)))
                    .Concat(fallback.SelectMany(a => status.Concat(weapons).Select(b => (A: a, B: b)))).ToArray();
                foreach (var pair in pairs)
                    MobileCheck(failures, scenario.Name + "/OwnedPartyRow/" + slot.name + "/cross-container-visible-text", () =>
                    {
                        var first = MobileVisibleGlyphs(pair.A, scenario, camera); var second = MobileVisibleGlyphs(pair.B, scenario, camera);
                        bool overlap = first.Any(a => second.Any(b => Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 1
                            && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 1));
                        Assert.That(overlap, Is.False, "owned-slot weapon text, level/resonance and missing-art caption must not draw on one another across containers");
                    });
            }
        }

        static IEnumerator MobileReadOnlyAccountPanel(GameObject screen, MobileRenderSurface surface, MobileCase scenario, string panelName, string state, List<string> failures)
        {
            yield return MobileSettle(surface, screen, MemoryForgeLobbyClient.OwnedCanvas, state, failures);
            var panel = MobilePanel(screen, panelName);
            MobileCheck(failures, scenario.Name + "/" + state, () =>
            { MobilePanelAcceptance(panel, scenario, surface.Camera); MobileTouchHits(panel, surface.Camera); });
            if (panelName == "SortiePanel") MobileOwnedPartyTextSeparation(screen, scenario, surface.Camera, failures);
            yield return MobileScrollEnds(panel, scenario, surface.Camera, failures);
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator MobileRecruitmentGrowthEquipmentSupportWeeklyAndPreparationStayReadOnlyInsideEveryViewport()
        {
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            var failures = new List<string>();
            try
            {
                foreach (var scenario in MobileCases())
                {
                    MobileApplyCalibration(scenario); EquipmentLobbyFixture();
                    // The existing v4 fixture adds only the supported nullable selection field to the same three-owned-character equipment profile.
                    progressionProfile = SupportProfileJson(selected: null);
                    var screen = new GameObject("MobileAccountPanels:" + scenario.Name); var lobby = screen.AddComponent<MemoryForgeLobbyClient>();
                    using (var surface = new MobileRenderSurface(scenario))
                    {
                        try
                        {
                            yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => Session());
                            yield return Until(() => lobby.AccountProgression?.Resources != null && screen.GetComponentsInChildren<Button>(true).Any(b => b.name == "RefreshRecruitment" && b.interactable), "read-only account panel GET");
                            Assert.That(lobby.AccountProgression!.Characters.Select(c => c.CharId), Is.EquivalentTo(new[] { "nia", "harin", "rien" }));
                            MobileClick(screen, "OpenRecruitment");
                            yield return Until(() => screen.GetComponentsInChildren<Button>().Any(b => b.name == "RecruitOne" && b.IsInteractable()), "read-only recruitment GET");
                            yield return MobileReadOnlyAccountPanel(screen, surface, scenario, "RecruitmentPanel", "recruitment", failures);
                            MobileClick(screen, "RecruitOne");
                            yield return MobileReadOnlyAccountPanel(screen, surface, scenario, "RecruitmentReveal", "recruitment-confirm-only", failures);
                            MobileClick(screen, "CancelRecruitment"); MobileClick(screen, "CloseRecruitment");
                            lobby.OpenCharacterLevel("nia");
                            yield return MobileReadOnlyAccountPanel(screen, surface, scenario, "GrowthPanel", "character-growth", failures);
                            lobby.OpenEquipmentFor("nia");
                            foreach (string tab in new[] { "characters", "weapons", "shop", "support" })
                            {
                                MobileClick(screen, "EquipmentTab:" + tab);
                                yield return MobileReadOnlyAccountPanel(screen, surface, scenario, "EquipmentPanel", "equipment-" + tab, failures);
                            }
                            var supportPanel = MobilePanel(screen, "EquipmentPanel");
                            Assert.That(supportPanel.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("SupportCandidate:", StringComparison.Ordinal)).All(b => !b.interactable), Is.True,
                                "three deployed owned companions cannot supply a fourth partner");
                            MobileClick(screen, "CloseEquipment"); MobileClick(screen, "OpenGrowth"); MobileClick(screen, "OpenWeeklyBosses");
                            yield return MobileReadOnlyAccountPanel(screen, surface, scenario, "GrowthPanel", "weekly-menu-only", failures);
                            MobileClick(screen, "CloseWeekly"); MobileClick(screen, "CloseGrowth"); MobileClick(screen, "StartExpedition");
                            yield return MobileReadOnlyAccountPanel(screen, surface, scenario, "SortiePanel", "owned-preparation-no-depart", failures);
                            Assert.That(host.Posts, Is.Empty, "menu/confirmation display never submits recruitment, preview, equip, launch or a boss attempt");
                            Assert.That(Outbox().Load(), Is.Null, "read-only viewport routes never stage a durable action");
                        }
                        finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); }
                    }
                    yield return null; MobileResetCalibration();
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { MobileResetCalibration(); MemoryForgeExpeditionClient.TouchOverride = null; MemoryForgeUiMotion.Reduced = reduced; }
        }

        static IEnumerator MobileAnonymousInputField(InputField field, MobileCase scenario, Camera camera, List<string> failures)
        {
            var rect = (RectTransform)field.transform; var scroll = field.GetComponentInParent<ScrollRect>(); Assert.That(scroll, Is.Not.Null);
            var before = scroll.normalizedPosition;
            try
            {
                MobileSetScrollPosition(scroll, false, 0, before); yield return null; Canvas.ForceUpdateCanvases(); var zero = MobilePixels(rect, camera);
                MobileSetScrollPosition(scroll, false, 1, before); yield return null; Canvas.ForceUpdateCanvases(); var one = MobilePixels(rect, camera);
                var viewport = MobilePixels(scroll.viewport, camera); float delta = one.center.y - zero.center.y;
                float position = Mathf.Abs(delta) < 0.01f ? 0 : Mathf.Clamp01((viewport.center.y - zero.center.y) / delta);
                MobileSetScrollPosition(scroll, false, position, before); yield return null; Canvas.ForceUpdateCanvases();
                MobileCheck(failures, scenario.Name + "/anonymous-input/" + field.name, () =>
                {
                    var pixels = MobilePixels(rect, camera); MobileInside(pixels, scenario.Safe, field.name); MobileInside(pixels, viewport, field.name + " fully reachable input");
                    if (scenario.Mobile)
                    {
                        Assert.That(pixels.width / scenario.Density + 0.1f, Is.GreaterThanOrEqualTo(48), field.name + "48dp width");
                        Assert.That(pixels.height / scenario.Density + 0.1f, Is.GreaterThanOrEqualTo(48), field.name + "48dp height");
                    }
                    Assert.That(field.lineType, Is.EqualTo(InputField.LineType.SingleLine));
                    Assert.That(field.textComponent.text.Length, Is.GreaterThan(0), "short synthetic input is visibly represented");
                    MobileWholeText(field.textComponent, scenario, camera, 14);
                    var raycaster = field.GetComponentInParent<GraphicRaycaster>(); Assert.That(raycaster, Is.Not.Null);
                    foreach (var uv in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.08f, 0.5f), new Vector2(0.92f, 0.5f), new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.92f) })
                    {
                        var pointer = new PointerEventData(EventSystem.current) { position = new Vector2(Mathf.Lerp(pixels.xMin, pixels.xMax, uv.x), Mathf.Lerp(pixels.yMin, pixels.yMax, uv.y)) };
                        var hits = new List<RaycastResult>(); raycaster.Raycast(pointer, hits);
                        Assert.That(hits.FirstOrDefault().gameObject?.GetComponentInParent<InputField>(), Is.EqualTo(field), "drawn input surface remains focusable without submitting");
                    }
                });
            }
            finally { scroll.normalizedPosition = before; }
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator MobileAnonymousConnectionAndSettingsAreReadableWithoutSubmittingOrChangingPreferences()
        {
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            var failures = new List<string>();
            try
            {
                foreach (var scenario in MobileCases())
                {
                    MobileApplyCalibration(scenario); MemoryForgeAccountConnection.SetCurrent(null);
                    var screen = new GameObject("MobileAnonymousConnection:" + scenario.Name); var lobby = screen.AddComponent<MemoryForgeLobbyClient>();
                    var originalWindow = Screen.fullScreenMode;
                    using (var surface = new MobileRenderSurface(scenario))
                    {
                        try
                        {
                            yield return null; lobby.Player!.Stop(); yield return MobileSettle(surface, screen, MemoryForgeLobbyClient.OwnedCanvas);
                            MobileClick(screen, "OpenSettings");
                            yield return MobileSettle(surface, screen, MemoryForgeLobbyClient.OwnedCanvas, "anonymous-settings", failures);
                            var settings = MobilePanel(screen, "SettingsPanel");
                            MobileCheck(failures, scenario.Name + "/anonymous-settings", () =>
                            {
                                MobilePanelAcceptance(settings, scenario, surface.Camera); MobileTouchHits(settings, surface.Camera);
                                MobilePrimary(settings, scenario, surface.Camera, "설정", 18);
                            });
                            MobileClick(screen, "CloseSettings"); Assert.That(MemoryForgeUiMotion.Reduced, Is.True); Assert.That(Screen.fullScreenMode, Is.EqualTo(originalWindow));
                            MobileClick(screen, "OpenRecruitment");
                            var inputs = screen.GetComponentsInChildren<InputField>();
                            var endpoint = inputs.Single(f => f.name == "RecruitmentEndpoint"); var account = inputs.Single(f => f.name == "RecruitmentAccount");
                            var credential = inputs.Single(f => f.name == "RecruitmentCredential"); int initialKeyLength = credential.text.Length;
                            // Never connect and never echo persisted values in a capture. Short synthetic strings fit this single-line editor;
                            // its512-character password limit is not a promise to display512 characters simultaneously.
                            endpoint.SetTextWithoutNotify("http://127.0.0.1:1"); account.SetTextWithoutNotify("fixture-only"); credential.SetTextWithoutNotify("TESTONLY");
                            yield return MobileSettle(surface, screen, MemoryForgeLobbyClient.OwnedCanvas, "anonymous-connection", failures);
                            var panel = MobilePanel(screen, "RecruitmentPanel");
                            MobileCheck(failures, scenario.Name + "/anonymous-connection", () =>
                            {
                                MobilePanelAcceptance(panel, scenario, surface.Camera); MobileTouchHits(panel, surface.Camera);
                                MobilePrimary(panel, scenario, surface.Camera, "공방 연결", 18);
                                var introduction = panel.GetComponentsInChildren<Text>().Single(t => t.text.Contains("보유 동료와 결과는 서버에 저장됩니다."));
                                MobileWholeText(introduction, scenario, surface.Camera, 14);
                            });
                            Assert.That(initialKeyLength, Is.Zero, "anonymous form never restores a persisted access key");
                            Assert.That(credential.contentType, Is.EqualTo(InputField.ContentType.Password));
                            Assert.That(endpoint.contentType, Is.EqualTo(InputField.ContentType.Standard)); Assert.That(account.contentType, Is.EqualTo(InputField.ContentType.Standard));
                            foreach (var field in new[] { endpoint, account, credential }) yield return MobileAnonymousInputField(field, scenario, surface.Camera, failures);
                            yield return MobileScrollEnds(panel, scenario, surface.Camera, failures);
                            Assert.That(screen.GetComponentsInChildren<Button>().Any(b => b.name == "RecruitOne" || b.name == "RecruitTen"), Is.False);
                            Assert.That(lobby.AccountProgression, Is.Null); Assert.That(MemoryForgeAccountConnection.Current, Is.Null);
                            Assert.That(host.Seen, Is.Empty, "opening settings/login and inspecting input fields sends no HTTP request"); Assert.That(Outbox().Load(), Is.Null);
                            MobileClick(screen, "CloseRecruitment");
                        }
                        finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); }
                    }
                    yield return null; MobileResetCalibration();
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { MobileResetCalibration(); MemoryForgeExpeditionClient.TouchOverride = null; MemoryForgeUiMotion.Reduced = reduced; }
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator MobileGrowthHpFillMatchesPublishedFractionsAcrossTheFormerAnchorThreshold()
        {
            var scenario = new MobileCase("s24-hp-ratio", 2340, 1080, new Rect(96, 0, 2244, 1080), 450f / 160, 1);
            var failures = new List<string>();
            try
            {
                foreach (int hp in new[] { 100, 109, 110 })
                {
                    MobileApplyCalibration(scenario); GrowthFixture(true);
                    var state = (JObject)growthEncounters[GrowthEncounter]["state"]!;
                    var enemy = (JObject)state["enemies"]![0]!.DeepClone(); enemy["uid"] = "e1"; enemy["hp"] = hp; enemy["maxHp"] = 200; enemy["alive"] = true; enemy["name"] = "검증 대상";
                    state["enemies"] = new JArray(enemy); var original = state.DeepClone();
                    using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
                    using (var surface = new MobileRenderSurface(scenario))
                    {
                        MemoryForgeAccountConnection.SetCurrent(owner); var screen = new GameObject("MobileHpRatio:" + hp); var view = screen.AddComponent<MemoryForgeGrowthClient>();
                        try
                        {
                            yield return Until(() => view.Snapshot != null && !view.Busy, "HP ratio fixture GET");
                            yield return MobileSettle(surface, screen, MemoryForgeGrowthClient.OwnedCanvas, "hp-" + hp, failures);
                            var tile = MobilePanel(screen, "GrowthEnemy:e1");
                            var bar = tile.GetComponentsInChildren<RectTransform>().Single(r => r.name == "Bar");
                            var fill = bar.GetComponentsInChildren<RectTransform>().Single(r => r.name == "Fill");
                            MobileCheck(failures, scenario.Name + "/hp-" + hp, () =>
                            {
                                var barPixels = MobilePixels(bar, surface.Camera); var fillPixels = MobilePixels(fill, surface.Camera);
                                Assert.That(fillPixels.width / barPixels.width, Is.EqualTo(hp / 200f).Within(0.005), "publishedHP/maxHP controls actualpixel fill, including54.5%→55%");
                                Assert.That(fillPixels.xMin, Is.EqualTo(barPixels.xMin).Within(1)); MobileInside(fillPixels, barPixels, "HP fill belongs inside its actual bar");
                            });
                            Assert.That(JToken.DeepEquals(view.Snapshot!.StateJObject(), original), Is.True, "drawing cannot change published HP semantics");
                            Assert.That(host.Posts, Is.Empty); Assert.That(Outbox().Load(), Is.Null);
                        }
                        finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); }
                    }
                    yield return null;
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { MobileResetCalibration(); MemoryForgeAccountConnection.SetCurrent(null); }
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator MobileLiveFitRecalibratesBleedOnSafeSideChangesAndBodyFontOnPixelScaleChanges()
        {
            var initial = new MobileCase("fit-left96", 2340, 1080, new Rect(96, 0, 2244, 1080), 450f / 160, 1);
            var mirror = new MobileCase("fit-right96", 2340, 1080, new Rect(0, 0, 2244, 1080), 450f / 160, 1);
            var smaller = new MobileCase("fit-right64-1560x720", 1560, 720, new Rect(0, 0, 1496, 720), 450f / 160, 1);
            var failures = new List<string>(); MobileApplyCalibration(initial); GrowthFixture(true);
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            using (var initialSurface = new MobileRenderSurface(initial))
            {
                MemoryForgeAccountConnection.SetCurrent(owner); var screen = new GameObject("MobileLiveFit"); var view = screen.AddComponent<MemoryForgeGrowthClient>();
                try
                {
                    yield return Until(() => view.Snapshot != null && !view.Busy, "live fit fixture GET");
                    yield return MobileSettle(initialSurface, screen, MemoryForgeGrowthClient.OwnedCanvas, "live-fit-initial", failures);
                    var frame = MobilePanel(screen, "GrowthFrame"); var shade = MobilePanel(screen, "Shade");
                    var body = MobilePanel(screen, "GrowthParty").GetComponentsInChildren<Text>().Single(t => t.text.StartsWith("체력 ", StringComparison.Ordinal));
                    var original = view.Snapshot!.StateJObject(); long version = view.Snapshot.Version;
                    MobileCheck(failures, "initial-live-fit", () =>
                    {
                        Assert.That(body.fontSize, Is.EqualTo(33), "14sp body at1.2pixels/unit");
                        var pixels = MobilePixels(shade, initialSurface.Camera);
                        Assert.That(pixels.xMin, Is.EqualTo(0).Within(1)); Assert.That(pixels.xMax, Is.EqualTo(2340).Within(1));
                        Assert.That(pixels.yMin, Is.EqualTo(0).Within(1)); Assert.That(pixels.yMax, Is.EqualTo(1080).Within(1));
                    });
                    MobileApplyCalibration(mirror);
                    // Isolate the safe-origin cache on the same registered objects before the controller may legitimately reflow.
                    MemoryForgeUiViewport.Fit(frame, (RectTransform)frame.parent);
                    Assert.That(MobilePanel(screen, "GrowthFrame"), Is.SameAs(frame), "no rebuild may hide missing cache invalidation");
                    MobileCheck(failures, "direct-origin-only-bleed", () =>
                    {
                        var pixels = MobilePixels(shade, initialSurface.Camera);
                        Assert.That(pixels.xMin, Is.EqualTo(0).Within(1)); Assert.That(pixels.xMax, Is.EqualTo(2340).Within(1));
                        Assert.That(pixels.yMin, Is.EqualTo(0).Within(1)); Assert.That(pixels.yMax, Is.EqualTo(1080).Within(1));
                    });
                    using (var mirroredSurface = new MobileRenderSurface(mirror))
                    {
                        yield return MobileSettle(mirroredSurface, screen, MemoryForgeGrowthClient.OwnedCanvas, "live-fit-mirrored", failures);
                        frame = MobilePanel(screen, "GrowthFrame"); shade = MobilePanel(screen, "Shade");
                        MobileCheck(failures, "mirrored-live-bleed", () =>
                        {
                            var pixels = MobilePixels(shade, mirroredSurface.Camera);
                            Assert.That(pixels.xMin, Is.EqualTo(0).Within(1)); Assert.That(pixels.xMax, Is.EqualTo(2340).Within(1));
                            Assert.That(pixels.yMin, Is.EqualTo(0).Within(1)); Assert.That(pixels.yMax, Is.EqualTo(1080).Within(1));
                        });
                    }
                    MobileApplyCalibration(smaller);
                    using (var smallerSurface = new MobileRenderSurface(smaller))
                    {
                        yield return MobileSettle(smallerSurface, screen, MemoryForgeGrowthClient.OwnedCanvas, "live-fit-smaller", failures);
                        frame = MobilePanel(screen, "GrowthFrame"); shade = MobilePanel(screen, "Shade");
                        body = MobilePanel(screen, "GrowthParty").GetComponentsInChildren<Text>().Single(t => t.text.StartsWith("체력 ", StringComparison.Ordinal));
                        MobileCheck(failures, "pixel-scale-live-body", () =>
                        {
                            Assert.That(body.fontSize, Is.EqualTo(50), "same14sp body at0.8pixels/unit must not retain old33-unit font");
                            MobileWholeText(body, smaller, smallerSurface.Camera, 14);
                            var pixels = MobilePixels(shade, smallerSurface.Camera);
                            Assert.That(pixels.xMin, Is.EqualTo(0).Within(1)); Assert.That(pixels.xMax, Is.EqualTo(1560).Within(1));
                            var button = MobilePixels((RectTransform)MobileButton(screen, "GrowthEndTurn").transform, smallerSurface.Camera);
                            Assert.That(button.width / smaller.Density + 0.1f, Is.GreaterThanOrEqualTo(48), "livePPU reflow keeps48dp width");
                            Assert.That(button.height / smaller.Density + 0.1f, Is.GreaterThanOrEqualTo(48), "livePPU reflow keeps48dp height");
                        });
                    }
                    var largerFont = new MobileCase("fit-right64-font130", 1560, 720, new Rect(0, 0, 1496, 720), 450f / 160, 1.3f);
                    MobileApplyCalibration(largerFont);
                    using (var largerFontSurface = new MobileRenderSurface(largerFont))
                    {
                        yield return MobileSettle(largerFontSurface, screen, MemoryForgeGrowthClient.OwnedCanvas, "live-fit-font130", failures);
                        frame = MobilePanel(screen, "GrowthFrame");
                        body = MobilePanel(screen, "GrowthParty").GetComponentsInChildren<Text>().Single(t => t.text.StartsWith("체력 ", StringComparison.Ordinal));
                        MobileCheck(failures, "live-font130-panel-reflow", () =>
                        {
                            Assert.That(body.fontSize, Is.EqualTo(64), "same14sp body atfontScale1.3 and0.8PPU");
                            MobilePanelAcceptance(frame, largerFont, largerFontSurface.Camera); MobileTouchHits(frame, largerFontSurface.Camera);
                            MobileWholeText(body, largerFont, largerFontSurface.Camera, 14);
                        });
                        yield return MobileScrollEnds(frame, largerFont, largerFontSurface.Camera, failures);
                        Assert.That(screen.GetComponentsInChildren<Button>().Any(b => b.name == "GrowthPlay"), Is.False, "no selection or prepared command appears during reflow");
                        Assert.That(screen.GetComponentsInChildren<RectTransform>().Any(r => r.name == "GrowthUltimateCutin"), Is.False, "viewport reflow cannot replay a presentation");
                    }
                    Assert.That(view.Snapshot.Version, Is.EqualTo(version)); Assert.That(JToken.DeepEquals(view.Snapshot.StateJObject(), original), Is.True);
                    Assert.That(host.Posts, Is.Empty); Assert.That(Outbox().Load(), Is.Null);
                    Assert.That(failures, Is.Empty, string.Join("\n", failures));
                }
                finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); MobileResetCalibration(); }
            }
            yield return null;
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator WideDesktopForecastStripMatchesItsPublishedHpIntervalInActualPixels()
        {
            var scenario = new MobileCase("desktop-forecast-2560x1080", 2560, 1080, new Rect(0, 0, 2560, 1080), 1, 1, false);
            var failures = new List<string>(); MobileApplyCalibration(scenario); MemoryForgeAccountConnection.SetCurrent(null); ControlRun();
            var battle = (JObject)world.Extra!["battle"]!; var enemy = (JObject)battle["enemies"]![0]!.DeepClone();
            enemy["uid"] = "e1"; enemy["hp"] = 160; enemy["maxHp"] = 200; enemy["alive"] = true;
            battle["enemies"] = new JArray(enemy);
            post = request =>
            {
                Assert.That(request.Path, Is.EqualTo(BattleQueryRoute), "only a pure forecast read is allowed");
                var asked = JObject.Parse(request.Text);
                var reply = QueryJson(world, asked["uid"]!.Value<string>()!, asked["mode"]!.Value<string>()!, asked["target"]?.Value<string>(),
                    ((asked["picks"] as JArray) ?? new JArray()).Values<string>().Select(x => x!).ToList());
                reply["forecast"] = SplitForecast(null, false, ("e1", 40)); return Reply.Json(200, reply);
            };
            var screen = new GameObject("WideDesktopForecast"); var view = screen.AddComponent<MemoryForgeServerPracticeClient>();
            using (var surface = new MobileRenderSurface(scenario))
            {
                try
                {
                    view.Connect(() => Session()); yield return Settled(view);
                    Assert.That(view.SelectCard("h1"), Is.True); Assert.That(view.SelectEnemy("e1"), Is.True);
                    yield return Until(() => view.QueryView?.Target == "e1" && !view.Busy, "pure per-enemy forecast GET-equivalent POST");
                    yield return MobileSettle(surface, screen, MemoryForgeServerPracticeClient.OwnedCanvas, "forecast-strip", failures);
                    var tile = MobilePanel(screen, "Enemy:e1");
                    var bar = tile.GetComponentsInChildren<RectTransform>().Single(r => r.name == "Bar");
                    var preview = tile.GetComponentsInChildren<RectTransform>().Single(r => r.name == "HpPreview");
                    MobileCheck(failures, "desktop-forecast-pixel-interval", () =>
                    {
                        var barPixels = MobilePixels(bar, surface.Camera); var strip = MobilePixels(preview, surface.Camera);
                        Assert.That((strip.xMin - barPixels.xMin) / barPixels.width, Is.EqualTo(0.6f).Within(0.005), "160HP minus40 forecast loss starts at60% of200maxHP");
                        Assert.That(strip.width / barPixels.width, Is.EqualTo(0.2f).Within(0.005), "forecast strip is40/200 of actual expanded bar");
                        Assert.That((strip.xMax - barPixels.xMin) / barPixels.width, Is.EqualTo(0.8f).Within(0.005));
                        MobileInside(strip, barPixels, "forecast cannot spill outside the currentHP bar");
                    });
                    Assert.That(view.EnemyHpPreview["e1"], Is.EqualTo(40)); Assert.That(Entry((JObject)Shown(view)["battle"]!, "enemies", "e1", "uid")["hp"]!.Value<int>(), Is.EqualTo(160));
                    Assert.That(Outbox().Load(), Is.Null); Assert.That(host.Posts.All(r => r.Path == BattleQueryRoute), Is.True);
                    Assert.That(failures, Is.Empty, string.Join("\n", failures));
                }
                finally { Object.Destroy(screen); MobileResetCalibration(); MemoryForgeAccountConnection.SetCurrent(null); }
            }
            yield return null;
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator MobileForecastStripsUseEachPublishedEnemysOwnLossRatherThanBroadcastingTheSelectedTarget()
        {
            var scenario = new MobileCase("s24-multi-enemy-forecast", 2340, 1080, new Rect(96, 0, 2244, 1080), 450f / 160, 1);
            var failures = new List<string>(); MobileApplyCalibration(scenario); MemoryForgeAccountConnection.SetCurrent(null); ControlRun();
            post = request =>
            {
                Assert.That(request.Path, Is.EqualTo(BattleQueryRoute)); var asked = JObject.Parse(request.Text);
                var reply = QueryJson(world, asked["uid"]!.Value<string>()!, asked["mode"]!.Value<string>()!, asked["target"]?.Value<string>(),
                    ((asked["picks"] as JArray) ?? new JArray()).Values<string>().Select(x => x!).ToList());
                reply["forecast"] = SplitForecast(null, false, ("e1", 8), ("e2", 3), ("e4", 34)); return Reply.Json(200, reply);
            };
            var screen = new GameObject("MobileSplitForecast"); var view = screen.AddComponent<MemoryForgeServerPracticeClient>();
            using (var surface = new MobileRenderSurface(scenario))
            {
                try
                {
                    view.Connect(() => Session()); yield return Settled(view);
                    Assert.That(view.SelectCard("h1"), Is.True); Assert.That(view.SelectEnemy("e1"), Is.True);
                    yield return Until(() => view.QueryView?.Target == "e1" && !view.Busy, "published mobile split forecast");
                    yield return MobileSettle(surface, screen, MemoryForgeServerPracticeClient.OwnedCanvas, "mobile-split-forecast", failures);
                    foreach (var expectation in new[] { (Uid: "e1", Hp: 31, Loss: 8), (Uid: "e2", Hp: 32, Loss: 3), (Uid: "e4", Hp: 34, Loss: 34) })
                    {
                        var tile = MobilePanel(screen, "Enemy:" + expectation.Uid); var scroll = tile.GetComponentInParent<ScrollRect>(); Assert.That(scroll, Is.Not.Null);
                        bool fullyVisible = false;
                        for (int step = 0; step <= 20; step++)
                        {
                            scroll.StopMovement(); scroll.horizontalNormalizedPosition = step / 20f; yield return null; Canvas.ForceUpdateCanvases();
                            var tilePixels = MobilePixels(tile, surface.Camera); var viewport = MobilePixels(scroll.viewport, surface.Camera);
                            if (tilePixels.xMin >= viewport.xMin - 1 && tilePixels.xMax <= viewport.xMax + 1 && tilePixels.yMin >= viewport.yMin - 1 && tilePixels.yMax <= viewport.yMax + 1)
                            { fullyVisible = true; break; }
                        }
                        MobileCheck(failures, expectation.Uid + "/mobile-graphical-forecast", () =>
                        {
                            Assert.That(fullyVisible, Is.True, "each forecast-bearing enemy can actually be scrolled fully into view");
                            var bar = tile.GetComponentsInChildren<RectTransform>().Single(r => r.name == "Bar");
                            var strip = tile.GetComponentsInChildren<RectTransform>().SingleOrDefault(r => r.name == "HpPreview");
                            Assert.That(strip, Is.Not.Null, expectation.Uid + " keeps the graphical preview on mobile, alongside numeric text");
                            var barPixels = MobilePixels(bar, surface.Camera); var lossPixels = MobilePixels(strip!, surface.Camera);
                            Assert.That((lossPixels.xMin - barPixels.xMin) / barPixels.width, Is.EqualTo((expectation.Hp - expectation.Loss) / 40f).Within(0.005));
                            Assert.That(lossPixels.width / barPixels.width, Is.EqualTo(expectation.Loss / 40f).Within(0.005), "per-enemy loss is not the selected enemy's8HP broadcast");
                            Assert.That((lossPixels.xMax - barPixels.xMin) / barPixels.width, Is.EqualTo(expectation.Hp / 40f).Within(0.005));
                            MobileInside(lossPixels, barPixels, expectation.Uid + " forecast interval inside the actual HP bar");
                        });
                    }
                    Assert.That(view.EnemyHpPreview, Is.EquivalentTo(new Dictionary<string, int> { ["e1"] = 8, ["e2"] = 3, ["e4"] = 34 }));
                    foreach (string uid in new[] { "e3", "e5" }) Assert.That(MobilePanel(screen, "Enemy:" + uid).GetComponentsInChildren<RectTransform>().Any(r => r.name == "HpPreview"), Is.False, uid + " has no published loss");
                    Assert.That(view.QueryView!.Target, Is.EqualTo("e1")); Assert.That(Outbox().Load(), Is.Null); Assert.That(host.Posts.All(r => r.Path == BattleQueryRoute), Is.True);
                    Assert.That(failures, Is.Empty, string.Join("\n", failures));
                }
                finally { Object.Destroy(screen); MobileResetCalibration(); MemoryForgeAccountConnection.SetCurrent(null); }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator MobileLiveLobbyReflowKeepsIdentityAndBlocksHomeBehindAnOpenPanel()
        {
            var initial = new MobileCase("live-lobby-font100", 2340, 1080, new Rect(96, 0, 2244, 1080), 450f / 160, 1);
            var changed = new MobileCase("live-lobby-font130-smaller", 1560, 720, new Rect(0, 0, 1496, 720), 450f / 160, 1.3f);
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            MobileApplyCalibration(initial); MemoryForgeAccountConnection.SetCurrent(null);
            var screen = new GameObject("MobileLiveLobbyState"); var lobby = screen.AddComponent<MemoryForgeLobbyClient>();
            var failures = new List<string>();
            using (var initialSurface = new MobileRenderSurface(initial))
            {
                try
                {
                    yield return null; lobby.Player!.Stop();
                    int selected = lobby.Companions.ToList().FindIndex(c => c.Id == "harin"); Assert.That(selected, Is.GreaterThanOrEqualTo(0));
                    lobby.Show(selected); lobby.Player.Stop();
                    yield return MobileSettle(initialSurface, screen, MemoryForgeLobbyClient.OwnedCanvas);
                    var identity = MobilePanel(screen, "CompanionIdentity");
                    var labels = identity.GetComponentsInChildren<Text>().Where(t => t.transform.parent == identity).ToArray();
                    string expectedName = labels.Single(t => t.fontStyle == FontStyle.Bold).text;
                    string expectedNote = labels.Single(t => t.fontStyle == FontStyle.Normal).text;
                    Assert.That(expectedName, Is.EqualTo(lobby.Current!.Name)); Assert.That(expectedNote, Is.Not.Empty);
                    var player = lobby.Player; int replayed = 0; player.Started += _ => replayed++;
                    MobileClick(screen, "OpenRecruitment");
                    Assert.That(MobilePanel(screen, "RecruitmentPanel").gameObject.activeInHierarchy, Is.True);
                    Assert.That(screen.GetComponentsInChildren<CanvasGroup>().Single(g => g.name == "LobbySide").interactable, Is.False);

                    MobileApplyCalibration(changed);
                    using (var changedSurface = new MobileRenderSurface(changed))
                    {
                        yield return MobileSettle(changedSurface, screen, MemoryForgeLobbyClient.OwnedCanvas);
                        MobileCheck(failures, "live-lobby/identity", () =>
                        {
                            var current = MobilePanel(screen, "CompanionIdentity");
                            var text = current.GetComponentsInChildren<Text>().Where(t => t.transform.parent == current).ToArray();
                            Assert.That(text.Single(t => t.fontStyle == FontStyle.Bold).text, Is.EqualTo(expectedName), "rebuilding home controls retains the selected companion name");
                            Assert.That(text.Single(t => t.fontStyle == FontStyle.Normal).text, Is.EqualTo(expectedNote), "rebuilding home controls retains the actual element/weapon note");
                        });
                        MobileCheck(failures, "live-lobby/modal-input", () =>
                        {
                            Assert.That(MobilePanel(screen, "RecruitmentPanel").gameObject.activeInHierarchy, Is.True, "reflow cannot close the active panel to repair home identity");
                            Assert.That(MobileButton(screen, "ModalShield").gameObject.activeInHierarchy, Is.True);
                            var home = screen.GetComponentsInChildren<CanvasGroup>().Single(g => g.name == "LobbySide");
                            Assert.That(home.interactable, Is.False, "recreated home controls stay disabled behind the open panel");
                            Assert.That(home.blocksRaycasts, Is.False);
                            Assert.That(MobileButton(screen, "FigureTap").IsInteractable(), Is.False);
                        });
                        Assert.That(lobby.Current!.Id, Is.EqualTo("harin")); Assert.That(lobby.Player, Is.SameAs(player));
                        Assert.That(player.Playing, Is.False); Assert.That(replayed, Is.Zero, "a calibration change does not replay companion moments");
                        MobileClick(screen, "CloseRecruitment");
                        Assert.That(screen.GetComponentsInChildren<CanvasGroup>().Single(g => g.name == "LobbySide").interactable, Is.True);
                    }
                    Assert.That(host.Seen, Is.Empty); Assert.That(Outbox().Load(), Is.Null);
                    Assert.That(failures, Is.Empty, string.Join("\n", failures));
                }
                finally { Object.Destroy(screen); MobileResetCalibration(); MemoryForgeUiMotion.Reduced = reduced; MemoryForgeAccountConnection.SetCurrent(null); }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator DesktopSettingsWindowModeStaysInsideItsResizedPanel()
        {
            var scenario = new MobileCase("desktop-settings-grown-panel", 1920, 1080, new Rect(0, 0, 1920, 1080), 1, 1, false);
            MobileApplyCalibration(scenario); MemoryForgeAccountConnection.SetCurrent(null);
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            var originalWindow = Screen.fullScreenMode;
            var screen = new GameObject("DesktopSettingsRowBounds"); var lobby = screen.AddComponent<MemoryForgeLobbyClient>();
            using (var surface = new MobileRenderSurface(scenario))
            {
                try
                {
                    yield return null; lobby.Player!.Stop(); yield return MobileSettle(surface, screen, MemoryForgeLobbyClient.OwnedCanvas);
                    MobileClick(screen, "OpenSettings"); yield return MobileSettle(surface, screen, MemoryForgeLobbyClient.OwnedCanvas);
                    var panel = MobilePanel(screen, "SettingsPanel"); var bounds = MobilePixels(panel, surface.Camera);
                    Assert.That(MobileButton(screen, "WindowMode").IsInteractable(), Is.True);
                    foreach (string name in new[] { "CloseSettings", "ReduceMotion", "WindowMode" })
                        MobileInside(MobilePixels((RectTransform)MobileButton(screen, name).transform, surface.Camera), bounds, name + " remains inside the actual resized settings panel");
                    foreach (var text in MobileButton(screen, "WindowMode").GetComponentsInChildren<Text>()) MobileWholeText(text, scenario, surface.Camera, 14);
                    Assert.That(Screen.fullScreenMode, Is.EqualTo(originalWindow)); Assert.That(MemoryForgeUiMotion.Reduced, Is.True);
                    Assert.That(host.Seen, Is.Empty); Assert.That(Outbox().Load(), Is.Null);
                }
                finally { Object.Destroy(screen); MobileResetCalibration(); MemoryForgeUiMotion.Reduced = reduced; MemoryForgeAccountConnection.SetCurrent(null); }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator MobileViewportCaptureUsesProductionGeometryAndRestoresCanvasAfterAWriteFailure()
        {
            var scenario = MobileCases().First(); MobileApplyCalibration(scenario); MemoryForgeAccountConnection.SetCurrent(null);
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            var screen = new GameObject("MobileCaptureFailure"); var lobby = screen.AddComponent<MemoryForgeLobbyClient>();
            using (var surface = new MobileRenderSurface(scenario))
            {
                try
                {
                    yield return null; lobby.Player!.Stop(); yield return MobileSettle(surface, screen, MemoryForgeLobbyClient.OwnedCanvas);
                    var canvas = screen.GetComponentsInChildren<Canvas>().Single(c => c.name == MemoryForgeLobbyClient.OwnedCanvas);
                    var previousMode = canvas.renderMode; var previousCamera = canvas.worldCamera; var previousTarget = RenderTexture.active;
                    var frame = MobilePanel(screen, "LobbyFrame"); var before = MobilePixels(frame, surface.Camera);
                    bool captureGeometryChecked = false;
                    string missingDirectory = Path.Combine(folder, "intentionally-absent", "viewport.jpg");
                    var capture = LobbyMomentCapturePlayModeTests.Shot(screen, missingDirectory, scenario.Width, scenario.Height,
                        MemoryForgeLobbyClient.OwnedCanvas, (_, camera) =>
                        {
                            MobileHomeEdges(screen, scenario, camera);
                            var during = MobilePixels(frame, camera);
                            Assert.That(during.xMin, Is.EqualTo(before.xMin).Within(1)); Assert.That(during.xMax, Is.EqualTo(before.xMax).Within(1));
                            Assert.That(during.yMin, Is.EqualTo(before.yMin).Within(1)); Assert.That(during.yMax, Is.EqualTo(before.yMax).Within(1));
                            captureGeometryChecked = true;
                        });
                    Exception? failure = null;
                    while (true)
                    {
                        bool next;
                        try { next = capture.MoveNext(); } catch (Exception error) { failure = error; break; }
                        if (!next) break;
                        yield return capture.Current;
                    }
                    Assert.That(captureGeometryChecked, Is.True, "actual viewport geometry is checked immediately before rendering");
                    Assert.That(failure, Is.TypeOf<DirectoryNotFoundException>());
                    Assert.That(canvas.renderMode, Is.EqualTo(previousMode)); Assert.That(canvas.worldCamera, Is.SameAs(previousCamera));
                    Assert.That(RenderTexture.active, Is.SameAs(previousTarget));
                    yield return null;
                    Assert.That(screen.GetComponentsInChildren<Camera>().Length, Is.Zero);
                    Assert.That(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Any(c => c.name == "ViewportAcceptanceCamera"), Is.False);
                }
                finally { Object.Destroy(screen); MobileResetCalibration(); MemoryForgeUiMotion.Reduced = reduced; }
            }
            yield return null;
        }
    }
}
#endif
