#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("MemoryForge.PlayModeTests")]
#endif

namespace MemoryForge.Client
{
    // Pixel/safe-area geometry is separate from the fixed, uniformly scaled art stage.
    internal static class MemoryForgeUiViewport
    {
        internal readonly struct Metrics
        {
            internal readonly Vector2 Pixels;
            internal readonly Rect SafeRect;
            internal readonly float Density, FontScale, ReportedDpi, ScaledDensity;
            internal readonly bool Mobile;
            internal Metrics(Vector2 pixels, Rect safeRect, float density, float fontScale, bool mobile, float reportedDpi = 0, float scaledDensity = 0)
            { Pixels = pixels; SafeRect = safeRect; Density = density; FontScale = fontScale; Mobile = mobile; ReportedDpi = reportedDpi; ScaledDensity = scaledDensity > 0 ? scaledDensity : density * fontScale; }
        }
        internal readonly struct Geometry
        {
            internal readonly Vector2 Size, Position, Pixels;
            internal readonly Rect SafeRect;
            internal readonly float Scale, PixelsPerUnit;
            internal Geometry(Vector2 size, Vector2 position, float scale, float pixelsPerUnit, Vector2 pixels = default, Rect safeRect = default)
            { Size = size; Position = position; Scale = scale; PixelsPerUnit = pixelsPerUnit; Pixels = pixels; SafeRect = safeRect; }
        }
#if UNITY_EDITOR
        internal static Metrics? EditorMetricsOverride { get; set; }
#endif
        static float density = 1, fontScale = 1, scaledDensity = 1, refreshAt = -1;
        static float captionPixels, bodyPixels, titlePixels;
        static Vector2 lastPixels;
        internal static void RefreshMetrics() => refreshAt = -1;
        internal static Metrics Current
        {
            get
            {
#if UNITY_EDITOR
                if (EditorMetricsOverride.HasValue) return EditorMetricsOverride.Value;
#endif
                var pixels = new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
                if (Time.realtimeSinceStartup >= refreshAt || pixels != lastPixels)
                {
                    lastPixels = pixels; refreshAt = Time.realtimeSinceStartup + 2;
#if UNITY_ANDROID && !UNITY_EDITOR
                    try
                    {
                        using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                        using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
                        using var resources = activity.Call<AndroidJavaObject>("getResources");
                        using var display = resources.Call<AndroidJavaObject>("getDisplayMetrics");
                        float nextDensity = Positive(display.Get<float>("density"), density);
                        float nextScaledDensity = Positive(display.Get<float>("scaledDensity"), nextDensity);
                        using var configuration = resources.Call<AndroidJavaObject>("getConfiguration");
                        float nextFontScale = Positive(configuration.Get<float>("fontScale"), fontScale);
                        using var typedValue = new AndroidJavaClass("android.util.TypedValue");
                        float nextCaption = typedValue.CallStatic<float>("applyDimension", 2, 12f, display), nextBody = typedValue.CallStatic<float>("applyDimension", 2, 14f, display), nextTitle = typedValue.CallStatic<float>("applyDimension", 2, 18f, display);
                        if (!Finite(nextCaption) || !Finite(nextBody) || !Finite(nextTitle) || nextCaption <= 0 || nextBody <= 0 || nextTitle <= 0) throw new InvalidOperationException();
                        density = nextDensity; scaledDensity = nextScaledDensity; fontScale = nextFontScale;
                        captionPixels = nextCaption; bodyPixels = nextBody; titlePixels = nextTitle;
                    }
                    catch (Exception) { /* Read-only platform metrics can be unavailable during activity teardown. */ }
#endif
                }
                return new Metrics(pixels, Screen.safeArea, density, fontScale, Application.isMobilePlatform, Screen.dpi, scaledDensity);
            }
        }
        static float Positive(float value, float fallback) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0 ? value : fallback;
        internal static Geometry Calculate(Metrics metrics, Vector2 canvasSize)
        {
            float screenW = Positive(metrics.Pixels.x, 1600), screenH = Positive(metrics.Pixels.y, 900);
            float canvasW = Positive(canvasSize.x, 1600), canvasH = Positive(canvasSize.y, 900);
            Rect safe = metrics.SafeRect;
            if (!Finite(safe.x) || !Finite(safe.y) || !Finite(safe.width) || !Finite(safe.height) || safe.width <= 0 || safe.height <= 0)
                safe = new Rect(0, 0, screenW, screenH);
            float left = Mathf.Clamp(safe.xMin, 0, screenW), bottom = Mathf.Clamp(safe.yMin, 0, screenH);
            float right = Mathf.Clamp(safe.xMax, left, screenW), top = Mathf.Clamp(safe.yMax, bottom, screenH);
            if (right <= left || top <= bottom) { left = bottom = 0; right = screenW; top = screenH; }
            float pixelScale = Mathf.Min((right - left) / 1600, (top - bottom) / 900);
            float canvasScale = screenW / canvasW;
            return new Geometry(new Vector2((right - left) / pixelScale, (top - bottom) / pixelScale),
                new Vector2(((left + right) / 2 / screenW - 0.5f) * canvasW, ((bottom + top) / 2 / screenH - 0.5f) * canvasH),
                pixelScale / canvasScale, pixelScale, new Vector2(screenW, screenH), Rect.MinMaxRect(left, bottom, right, top));
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        sealed class FitStamp { internal Vector2 Size, Position, Pixels, CanvasSize; internal Rect SafeRect; internal float Density, FontScale, PixelsPerUnit, Scale; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RectTransform, FitStamp> fitted = new System.Runtime.CompilerServices.ConditionalWeakTable<RectTransform, FitStamp>();
        internal static void Fit(RectTransform frame, RectTransform canvas)
        {
            var metrics = Current; var geometry = Calculate(metrics, canvas.rect.size);
            bool changed = !fitted.TryGetValue(frame, out var stamp) || stamp.Size != geometry.Size || stamp.Density != metrics.Density || stamp.FontScale != metrics.FontScale || stamp.Position != geometry.Position || stamp.Pixels != geometry.Pixels || stamp.SafeRect != geometry.SafeRect || stamp.PixelsPerUnit != geometry.PixelsPerUnit || stamp.Scale != geometry.Scale || stamp.CanvasSize != canvas.rect.size;
            if (stamp == null) { stamp = new FitStamp(); fitted.Add(frame, stamp); }
            stamp.Size = geometry.Size; stamp.Density = metrics.Density; stamp.FontScale = metrics.FontScale; stamp.Position = geometry.Position; stamp.Pixels = geometry.Pixels; stamp.SafeRect = geometry.SafeRect; stamp.PixelsPerUnit = geometry.PixelsPerUnit; stamp.Scale = geometry.Scale; stamp.CanvasSize = canvas.rect.size;
            frame.sizeDelta = geometry.Size; frame.localScale = Vector3.one * geometry.Scale; frame.anchoredPosition = geometry.Position;
            if (changed)
            {
                foreach (var text in frame.GetComponentsInChildren<Text>(true)) if (typography.TryGetValue(text, out _)) WholeText(text);
                foreach (var image in frame.GetComponentsInChildren<Image>(true)) if (bleeding.TryGetValue(image.rectTransform, out _)) Bleed(image.rectTransform); else if (bleedingX.TryGetValue(image.rectTransform, out _)) BleedX(image.rectTransform);
            }
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            if (changed)
            {
            string state = Current.Pixels + " safe=" + Current.SafeRect + " unityDpi=" + Current.ReportedDpi + " density=" + Current.Density + " scaledDensity=" + Current.ScaledDensity + " fontScale=" + Current.FontScale
                + " canvas=" + canvas.rect.size + " frame=" + frame.sizeDelta + " scale=" + geometry.Scale + " position=" + geometry.Position;
            int key = frame.GetInstanceID();
            if (!diagnostics.TryGetValue(key, out string previous) || previous != state)
            { if (diagnostics.Count > 32) diagnostics.Clear(); diagnostics[key] = state; Debug.Log("MF_UI_VIEWPORT " + state); }
            }
#endif
        }
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        static readonly System.Collections.Generic.Dictionary<int, string> diagnostics = new System.Collections.Generic.Dictionary<int, string>();
#endif
        internal static Vector3 Calibration
        {
            get { var metrics = Current; return new Vector3(Calculate(metrics, metrics.Pixels).PixelsPerUnit, Positive(metrics.Density, 1), Positive(metrics.FontScale, 1)); }
        }
        internal static float TouchUnits()
        {
            var metrics = Current; if (!metrics.Mobile) return 0;
            return 48 * Positive(metrics.Density, 1) / Calculate(metrics, metrics.Pixels).PixelsPerUnit;
        }
        internal static int FontSize(int authored)
        {
            var metrics = Current; if (!metrics.Mobile) return authored;
            float sp = authored >= 34 ? 18 : authored < 20 ? 12 : 14;
            float pixels = sp * Positive(metrics.Density, 1) * Positive(metrics.FontScale, 1);
#if UNITY_ANDROID && !UNITY_EDITOR
            pixels = Positive(sp == 12 ? captionPixels : sp == 14 ? bodyPixels : titlePixels, pixels);
#endif
            return Mathf.Max(authored, Mathf.CeilToInt(pixels / Calculate(metrics, metrics.Pixels).PixelsPerUnit));
        }
        // Existing top-left rectangles gain real edge anchors; the art stage is excluded by its caller.
        sealed class AuthoredSize { internal Vector2 Size; internal AuthoredSize(float width, float height) { Size = new Vector2(width, height); } }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RectTransform, AuthoredSize> authoredSizes = new System.Runtime.CompilerServices.ConditionalWeakTable<RectTransform, AuthoredSize>();
        internal static void Anchor(RectTransform rect, float x, float y, float width, float height)
        {
            var parent = rect.parent as RectTransform; if (parent == null) return;
            authoredSizes.Remove(rect); authoredSizes.Add(rect, new AuthoredSize(width, height));
            Vector2 original = authoredSizes.TryGetValue(parent, out var known) ? known.Size : parent.rect.size;
            float parentW = original.x, parentH = original.y;
            if (parentW <= 0 || parentH <= 0) return;
            bool wide = width >= parentW * 0.55f, centered = !wide && Mathf.Abs(x + width / 2 - parentW / 2) < 4, right = !wide && !centered && x + width / 2 > parentW * 0.5f;
            var scroll = parent.parent != null ? parent.parent.GetComponent<ScrollRect>() : null;
            bool flowing = scroll != null && scroll.content == parent;
            bool tall = !flowing && height >= parentH * 0.7f && y < parentH * 0.3f, bottom = !flowing && !tall && y >= parentH * 0.55f;
            rect.anchorMin = new Vector2(wide ? 0 : right ? 1 : centered ? 0.5f : 0, tall || bottom ? 0 : 1);
            rect.anchorMax = new Vector2(wide || right ? 1 : centered ? 0.5f : 0, bottom ? 0 : 1);
            rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(wide ? width - parentW : width, tall ? height - parentH : height);
            rect.anchoredPosition = new Vector2(right ? x - parentW : centered ? x - parentW / 2 : x, bottom ? parentH - y : -y);
        }
        sealed class Typography
        {
            internal readonly int Size; internal readonly float Height;
            internal Typography(Text text) { Size = text.fontSize; Height = text.rectTransform.rect.height; }
        }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Text, Typography> typography = new System.Runtime.CompilerServices.ConditionalWeakTable<Text, Typography>();
        internal static Vector2 ReferenceSize(RectTransform rect) => authoredSizes.TryGetValue(rect, out var size) ? size.Size : rect.rect.size;
        internal static void StretchX(RectTransform rect, float left, float right)
        { rect.anchorMin = new Vector2(0, rect.anchorMin.y); rect.anchorMax = new Vector2(1, rect.anchorMax.y); rect.sizeDelta = new Vector2(-left - right, rect.sizeDelta.y); rect.anchoredPosition = new Vector2(left, rect.anchoredPosition.y); }
        internal static void StretchY(RectTransform rect, float top, float bottom)
        { rect.anchorMin = new Vector2(rect.anchorMin.x, 0); rect.anchorMax = new Vector2(rect.anchorMax.x, 1); rect.sizeDelta = new Vector2(rect.sizeDelta.x, -top - bottom); rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -top); }
        internal static void WholeText(Text text)
        {
            if (!typography.TryGetValue(text, out var authored)) { authored = new Typography(text); typography.Add(text, authored); }
            text.resizeTextForBestFit = false; text.fontSize = FontSize(authored.Size);
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            float height = Mathf.Max(authored.Height, Mathf.Ceil(text.preferredHeight) + 8);
            if (text.font != null && !string.IsNullOrEmpty(text.text))
            {
                // Measure the same bounded glyph generation used to draw the label; dynamic font padding varies with canvas scale.
                var settings = text.GetGenerationSettings(new Vector2(Mathf.Max(1, text.rectTransform.rect.width), height));
                settings.resizeTextForBestFit = false; settings.verticalOverflow = VerticalWrapMode.Overflow;
                var generator = text.cachedTextGeneratorForLayout; generator.Populate(text.text, settings); int whole = generator.characterCountVisible;
                settings.verticalOverflow = VerticalWrapMode.Truncate;
                for (int line = 0; line < 8; line++)
                {
                    settings.generationExtents = new Vector2(settings.generationExtents.x, height);
                    generator.Populate(text.text, settings);
                    if (generator.characterCountVisible >= whole) break;
                    height += Mathf.Max(16, text.fontSize * 1.5f);
                }
            }
            text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RectTransform, object> bleeding = new System.Runtime.CompilerServices.ConditionalWeakTable<RectTransform, object>();
        internal static void Bleed(RectTransform rect)
        {
            if (!bleeding.TryGetValue(rect, out _)) bleeding.Add(rect, new object());
            var metrics = Current; var geometry = Calculate(metrics, metrics.Pixels);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(-Mathf.Max(0, geometry.SafeRect.xMin), -Mathf.Max(0, geometry.SafeRect.yMin)) / geometry.PixelsPerUnit;
            rect.offsetMax = new Vector2(Mathf.Max(0, geometry.Pixels.x - geometry.SafeRect.xMax), Mathf.Max(0, geometry.Pixels.y - geometry.SafeRect.yMax)) / geometry.PixelsPerUnit;
        }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RectTransform, object> bleedingX = new System.Runtime.CompilerServices.ConditionalWeakTable<RectTransform, object>();
        internal static void BleedX(RectTransform rect)
        {
            if (!bleedingX.TryGetValue(rect, out _)) bleedingX.Add(rect, new object());
            var metrics = Current; var geometry = Calculate(metrics, metrics.Pixels);
            StretchX(rect, -geometry.SafeRect.xMin / geometry.PixelsPerUnit, -(geometry.Pixels.x - geometry.SafeRect.xMax) / geometry.PixelsPerUnit);
        }
        internal static void BleedDim(Image surface)
        {
            var existing = surface.transform.Find("ViewportDim")?.GetComponent<Image>();
            if (existing != null) return;
            var host = new GameObject("ViewportDim", typeof(RectTransform), typeof(Image)); host.transform.SetParent(surface.transform, false); host.transform.SetAsFirstSibling();
            var dim = host.GetComponent<Image>(); dim.color = surface.color; dim.sprite = surface.sprite; dim.raycastTarget = true;
            surface.color = Color.clear; surface.sprite = null;
            Bleed(dim.rectTransform);
        }
        internal static bool IsDim(string name) => name == "Veil" || name == "AccountAbandonConfirmation" || name.EndsWith("Blocker", StringComparison.Ordinal) || name.EndsWith("Shield", StringComparison.Ordinal);
        internal static void Cover(Image container, float focus = 0.5f)
        {
            var art = container.transform.Find("ViewportArt")?.GetComponent<Image>();
            if (container.sprite == null) { if (art != null) art.gameObject.SetActive(false); return; }
            Sprite sprite = container.sprite; Color tint = container.color;
            if (art == null)
            {
                var host = new GameObject("ViewportArt", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
                host.transform.SetParent(container.transform, false); art = host.GetComponent<Image>(); art.raycastTarget = false;
                if (container.GetComponent<RectMask2D>() == null) container.gameObject.AddComponent<RectMask2D>();
            }
            art.gameObject.SetActive(true); art.sprite = sprite; art.color = tint; art.preserveAspect = true;
            art.rectTransform.pivot = new Vector2(0.5f, 1 - Mathf.Clamp01(focus));
            var fitter = art.GetComponent<AspectRatioFitter>(); fitter.aspectRatio = sprite.rect.width / Mathf.Max(1, sprite.rect.height); fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            container.sprite = null; container.color = Color.clear;
        }
        internal static void ArtIsland(RectTransform stage, float offsetX = 0)
        {
            stage.anchorMin = stage.anchorMax = new Vector2(0.5f, 0.5f); stage.pivot = new Vector2(0.5f, 0.5f);
            stage.sizeDelta = new Vector2(1600, 900); stage.anchoredPosition = new Vector2(offsetX, 0);
        }
    }
}
