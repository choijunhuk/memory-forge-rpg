#nullable enable
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    internal static class MemoryForgeCombatCutin
    {
        // A separate overlay survives an ordinary battle/result view redraw.
        internal static IEnumerator Play(Transform owner, Font font, Sprite? portrait, string name, string title, string objectName)
        {
            if (MemoryForgeUiMotion.Reduced) yield break;
            var overlay = new GameObject(objectName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            overlay.transform.SetParent(owner, false);
            var canvas = overlay.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
            var scaler = overlay.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
            var group = overlay.GetComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
            var frame = Rect(overlay.transform, "Frame", 0, 0, 1600, 900);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f); frame.anchoredPosition = Vector2.zero;
            var band = Rect(frame, "Band", 0, 224, 1600, 420); var background = band.gameObject.AddComponent<Image>();
            background.color = new Color(0.035f, 0.05f, 0.1f, 0.96f); background.raycastTarget = false;
            var lifetime = overlay.AddComponent<MemoryForgeCutinLifetime>();
            try
            {
                var texture = Resources.Load<Texture2D>("Art/UI/battle/ui_cutin_plate");
                if (texture != null)
                {
                    lifetime.Plate = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                    background.sprite = lifetime.Plate; background.color = Color.white;
                }
                var figure = Rect(band, "Figure", 160, 0, 560, 420).gameObject.AddComponent<Image>();
                figure.sprite = portrait; figure.preserveAspect = true; figure.raycastTarget = false;
                if (portrait == null) figure.color = Color.clear;
                var words = Rect(band, "Words", 800, 96, 720, 224).gameObject.AddComponent<Text>();
                words.font = font; words.fontSize = 48; words.fontStyle = FontStyle.Bold; words.color = new Color(0.886f, 0.706f, 0.373f);
                words.text = name + "\n" + title; words.raycastTarget = false;
                yield return MemoryForgeUiMotion.Fade(group, 0, 1, 0.14f);
                for (float time = 0; time < 0.6f && !MemoryForgeUiMotion.Reduced; time += Time.unscaledDeltaTime)
                { if (overlay == null) yield break; yield return null; }
                if (overlay != null) yield return MemoryForgeUiMotion.Fade(group, 1, 0, 0.25f);
            }
            finally
            {
                if (overlay != null) Object.Destroy(overlay);
            }
        }

        static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var host = new GameObject(name, typeof(RectTransform)); host.transform.SetParent(parent, false);
            var rect = (RectTransform)host.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }
    }
}
