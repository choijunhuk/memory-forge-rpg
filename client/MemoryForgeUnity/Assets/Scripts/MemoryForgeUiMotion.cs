#nullable enable
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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

        // Every motion below also stops at once, in its final pose, when `stop` turns true mid-way (a caller's own reduced-motion setting,
        // e.g. the practice's 동작 줄이기, switched on while it runs) or when Reduced is switched on.
        static bool Halted(System.Func<bool>? stop) => Reduced || (stop != null && stop());

        public static IEnumerator Fade(CanvasGroup group, float from, float to, float duration, System.Func<bool>? stop = null)
        {
            for (float t = 0; t < duration && !Halted(stop); t += Time.unscaledDeltaTime)
            {
                if (group == null) yield break;
                group.alpha = Mathf.Lerp(from, to, EaseOut(t / duration));
                yield return null;
            }
            if (group != null) group.alpha = to;
        }

        // Slides from an offset back to the layout position.
        public static IEnumerator SlideIn(RectTransform rect, Vector2 offset, float duration, System.Func<bool>? stop = null)
        {
            if (rect == null) yield break;
            var home = rect.anchoredPosition;
            for (float t = 0; t < duration && !Halted(stop); t += Time.unscaledDeltaTime)
            {
                if (rect == null) yield break;
                rect.anchoredPosition = home + offset * (1 - EaseOut(t / duration));
                yield return null;
            }
            if (rect != null) rect.anchoredPosition = home;
        }

        // A short horizontal shake around the layout position (decoration: callers only shake art, never a control).
        public static IEnumerator Shake(RectTransform rect, float amplitude, float duration, System.Func<bool>? stop = null)
        {
            if (rect == null) yield break;
            var home = rect.anchoredPosition;
            for (float t = 0; t < duration && !Halted(stop); t += Time.unscaledDeltaTime)
            {
                if (rect == null) yield break;
                rect.anchoredPosition = home + new Vector2(Mathf.Sin(t * 90f) * amplitude * (1 - t / duration), 0);
                yield return null;
            }
            if (rect != null) rect.anchoredPosition = home;
        }

        // A label that rises by `rise` and fades out, then removes itself. Halted, it stands still at its start for the same time.
        public static IEnumerator Rise(Graphic label, float rise, float duration, System.Func<bool>? stop = null)
        {
            if (label == null) yield break;
            var rect = label.rectTransform; var home = rect.anchoredPosition; var color = label.color;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                if (label == null) yield break;
                float u = t / duration; bool still = Halted(stop);
                rect.anchoredPosition = home + new Vector2(0, still ? 0 : rise * EaseOut(u));
                label.color = new Color(color.r, color.g, color.b, color.a * (still || u < 0.6f ? 1 : 1 - (u - 0.6f) / 0.4f));
                yield return null;
            }
            if (label != null) Object.Destroy(label.gameObject);
        }

        // Eases a rect's width to `width` and then removes it (a bar's drained part).
        public static IEnumerator Resize(RectTransform rect, float width, float duration, System.Func<bool>? stop = null)
        {
            if (rect == null) yield break;
            float from = rect.sizeDelta.x;
            for (float t = 0; t < duration && !Halted(stop); t += Time.unscaledDeltaTime)
            {
                if (rect == null) yield break;
                rect.sizeDelta = new Vector2(Mathf.Lerp(from, width, EaseOut(t / duration)), rect.sizeDelta.y);
                yield return null;
            }
            if (rect != null) Object.Destroy(rect.gameObject);
        }

        // Moves a decoration to `to` (anchored position in its parent's top-left space, y down) while shrinking and fading, then removes it.
        public static IEnumerator Fly(RectTransform rect, Vector2 to, float duration, System.Func<bool>? stop = null)
        {
            if (rect == null) yield break;
            var group = rect.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
            var from = rect.anchoredPosition; var target = new Vector2(to.x, -to.y);
            for (float t = 0; t < duration && !Halted(stop); t += Time.unscaledDeltaTime)
            {
                if (rect == null) yield break;
                float u = EaseOut(t / duration);
                rect.anchoredPosition = Vector2.Lerp(from, target, u); rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.45f, u); group.alpha = 1 - u * u;
                yield return null;
            }
            if (rect != null) Object.Destroy(rect.gameObject);
        }

        // Scales a centre-pivoted decoration from `from` back to 1 (a hit number popping in); halted, it is at once at 1.
        public static IEnumerator Punch(RectTransform rect, float from, float duration, System.Func<bool>? stop = null)
        {
            for (float t = 0; t < duration && !Halted(stop); t += Time.unscaledDeltaTime)
            {
                if (rect == null) yield break;
                rect.localScale = Vector3.one * Mathf.Lerp(from, 1f, EaseOut(t / duration));
                yield return null;
            }
            if (rect != null) rect.localScale = Vector3.one;
        }

        // Grows a centre-pivoted decoration from `from` to `to` while fading it out, then removes it (a hit's burst).
        public static IEnumerator Burst(RectTransform rect, float from, float to, float duration, System.Func<bool>? stop = null)
        {
            if (rect == null) yield break;
            var group = rect.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
            for (float t = 0; t < duration && !Halted(stop); t += Time.unscaledDeltaTime)
            {
                if (rect == null) yield break;
                float u = EaseOut(t / duration); rect.localScale = Vector3.one * Mathf.Lerp(from, to, u); group.alpha = 1 - u;
                yield return null;
            }
            if (rect != null) Object.Destroy(rect.gameObject);
        }
    }

    // A breathing alpha on a decoration (an edge or a marker) that shows what can be pressed next; still at full alpha whenever motion
    // is reduced (globally, or by the caller's own setting).
    public sealed class UiPulse : MonoBehaviour
    {
        Outline? edge; Graphic? graphic; System.Func<bool>? still; Color rest;
        public void Bind(Outline target, System.Func<bool> calm) { edge = target; rest = target.effectColor; still = calm; }
        public void Bind(Graphic target, System.Func<bool> calm) { graphic = target; rest = target.color; still = calm; }
        void Update()
        {
            float k = MemoryForgeUiMotion.Reduced || (still != null && still()) ? 1f : 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / 1.4f));
            var color = new Color(rest.r, rest.g, rest.b, rest.a * k);
            if (edge != null) edge.effectColor = color;
            if (graphic != null) graphic.color = color;
        }
    }

    // Press feedback: the control dips to 95% while held and springs back on release or exit. Bound to a face (a top-left pivoted
    // child or the button itself), it scales that face about its own centre and puts it back exactly where the layout put it; `calm`
    // (a caller's own reduced-motion setting) keeps it still like Reduced. The hit area never changes size while held.
    public sealed class UiPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        float target = 1;
        RectTransform? face; Vector2 home; System.Func<bool>? calm;
        public void Bind(RectTransform drawn, System.Func<bool> still) { face = drawn; home = drawn.anchoredPosition; calm = still; }
        public void OnPointerDown(PointerEventData _) => target = 0.95f;
        public void OnPointerUp(PointerEventData _) => target = 1;
        public void OnPointerExit(PointerEventData _) => target = 1;
        // A press cut short (the control disabled or its view hidden mid-press) leaves the face exactly where the layout put it.
        void OnDisable()
        {
            target = 1;
            var moved = face != null ? face : (RectTransform)transform;
            moved.localScale = Vector3.one; if (face != null) face.anchoredPosition = home;
        }
        void Update()
        {
            bool still = MemoryForgeUiMotion.Reduced || (calm != null && calm());
            if (still) target = 1;
            var moved = face != null ? face : (RectTransform)transform;
            float s = moved.localScale.x;
            if (Mathf.Abs(s - target) < 0.001f) return;
            s = still ? target : Mathf.MoveTowards(s, target, Time.unscaledDeltaTime * 1.6f); // reduced motion settles at once
            if (Mathf.Abs(s - target) < 0.001f) s = target;
            moved.localScale = Vector3.one * s;
            if (face != null) { var size = face.rect.size; face.anchoredPosition = home + new Vector2(size.x * (1 - s) / 2, -size.y * (1 - s) / 2); }
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
