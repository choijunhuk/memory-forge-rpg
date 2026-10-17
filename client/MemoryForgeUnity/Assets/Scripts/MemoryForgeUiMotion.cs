#nullable enable
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MemoryForge.Client
{
    // Generic UI motion (OWNERSHIP.md: Claude's UI layer). Nothing here changes layout, rules or input routing; it only eases
    // scale, offset and alpha back to where the layout put them, on unscaled time so pace settings and pauses don't affect it.
    public static class MemoryForgeUiMotion
    {
        // Accessibility: reduced motion makes every transition instant and motions jump to their final pose; text and timing stay.
        const string ReducedKey = "mf.reduceMotion";
        static bool? reduced; // read once: UiPress and UiHoverLift check it every frame
        public static bool Reduced
        {
            get { return reduced ??= PlayerPrefs.GetInt(ReducedKey, 0) == 1; }
            set { reduced = value; PlayerPrefs.SetInt(ReducedKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static float EaseOut(float u) => 1 - (1 - u) * (1 - u) * (1 - u);

        public static IEnumerator Fade(CanvasGroup group, float from, float to, float duration)
        {
            if (Reduced) duration = 0;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                if (group == null) yield break;
                group.alpha = Mathf.Lerp(from, to, EaseOut(t / duration));
                yield return null;
            }
            if (group != null) group.alpha = to;
        }

        // Slides from an offset back to the layout position.
        public static IEnumerator SlideIn(RectTransform rect, Vector2 offset, float duration)
        {
            if (rect == null || Reduced) yield break;
            var home = rect.anchoredPosition;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                if (rect == null) yield break;
                rect.anchoredPosition = home + offset * (1 - EaseOut(t / duration));
                yield return null;
            }
            if (rect != null) rect.anchoredPosition = home;
        }
    }

    // Press feedback: the control dips to 95% while held and springs back on release or exit.
    public sealed class UiPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        float target = 1;
        public void OnPointerDown(PointerEventData _) => target = 0.95f;
        public void OnPointerUp(PointerEventData _) => target = 1;
        public void OnPointerExit(PointerEventData _) => target = 1;
        void Update()
        {
            if (MemoryForgeUiMotion.Reduced) target = 1;
            float s = transform.localScale.x;
            if (Mathf.Abs(s - target) < 0.001f) return;
            transform.localScale = Vector3.one * Mathf.MoveTowards(s, target, Time.unscaledDeltaTime * 1.6f);
        }
    }

    // Hover lift for cards on pointer devices: rises a few pixels and returns; the layout position is kept as home.
    public sealed class UiHoverLift : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public float Lift = 12;
        Vector2 home; bool homed; float target;
        public void OnPointerEnter(PointerEventData _) { Home(); target = MemoryForgeUiMotion.Reduced ? 0 : Lift; }
        public void OnPointerExit(PointerEventData _) { Home(); target = 0; }
        void Home() { if (homed) return; home = ((RectTransform)transform).anchoredPosition; homed = true; }
        void Update()
        {
            if (!homed) return;
            var r = (RectTransform)transform;
            float y = r.anchoredPosition.y - home.y;
            if (Mathf.Abs(y - target) < 0.05f) return;
            r.anchoredPosition = new Vector2(r.anchoredPosition.x, home.y + Mathf.MoveTowards(y, target, Time.unscaledDeltaTime * 160f));
        }
    }
}
