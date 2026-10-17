#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    // UI skin and motion for the local expedition screens. The client rebuilds its canvas on every state change, so after each redraw
    // this finds the client's named objects and decorates them once: "ExpeditionFrame" (battle background), "Enemy:<uid>" panels (enemy
    // art by name; hit shake and flash when its HP drops; dimmed when defeated) and the "Party" box's "Ult:<id>" rows (party SD; the
    // party panel shakes when party HP drops). Art comes from Resources/Art (Codex-made assets); missing art shows nothing. Nothing
    // here changes layout, buttons, rules or numbers, and every added image ignores raycasts so input is untouched.
    public sealed class MemoryForgeUiSkin : MonoBehaviour
    {
        const string Marker = "UiSkin";
        static readonly Regex Hp = new Regex(@"체력\s*(\d+)\s*/\s*(\d+)");
        static readonly Color HitFlash = new Color(1f, 0.35f, 0.3f, 0.55f);
        readonly Dictionary<string, Sprite?> sprites = new Dictionary<string, Sprite?>();
        readonly Dictionary<string, int> lastHp = new Dictionary<string, int>();
        Dictionary<string, string>? enemyIds;
        int lastPartyHp = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<MemoryForgeUiSkin>() != null) return;
            var host = new GameObject(nameof(MemoryForgeUiSkin));
            DontDestroyOnLoad(host);
            host.AddComponent<MemoryForgeUiSkin>();
        }

        void LateUpdate()
        {
            var frame = GameObject.Find("ExpeditionFrame");
            if (frame == null) return;
            bool battle = false;
            foreach (Transform child in frame.transform)
            {
                if (!child.name.StartsWith("Enemy:")) continue;
                battle = true;
                if (child.Find(Marker) != null) continue;
                Enemy((RectTransform)child);
            }
            var party = frame.transform.Find("Party");
            if (party != null && party.Find(Marker) == null) Party((RectTransform)party, frame.transform);
            if (battle && frame.transform.Find(Marker + ":Background") == null)
            {
                var image = Place(frame.transform, Sprite("Art/Backgrounds/bg_zone1_battle"), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600, 900), new Color(1, 1, 1, 0.35f));
                if (image != null) { image.preserveAspect = false; image.name = Marker + ":Background"; }
            }
            if (!battle) { lastHp.Clear(); lastPartyHp = -1; }
        }

        void Enemy(RectTransform panel)
        {
            var texts = panel.GetComponentsInChildren<Text>();
            var name = texts.Length > 0 ? texts[0].text : "";
            bool dead = name.EndsWith(" · 처치");
            if (dead) name = name.Substring(0, name.Length - " · 처치".Length);
            float side = Mathf.Min(panel.rect.height - 24, panel.rect.width * 0.42f);
            var art = Place(panel, EnemyArt(name), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(side, side), dead ? new Color(0.45f, 0.45f, 0.5f, 0.45f) : Color.white);
            if (art == null) Place(panel, null, new Vector2(1, 0.5f), Vector2.zero, Vector2.zero, Color.clear, true); // marker only
            int hp = HpOf(texts);
            if (hp >= 0 && lastHp.TryGetValue(panel.name, out var before) && hp < before) StartCoroutine(Hit(panel));
            if (hp >= 0) lastHp[panel.name] = hp;
        }

        void Party(RectTransform party, Transform frame)
        {
            // Snapshot the rows first: Place adds children to this same transform, and a live child enumerator would revisit rows forever.
            var rows = new List<Transform>();
            foreach (Transform row in party) if (row.name.StartsWith("Ult:")) rows.Add(row);
            foreach (var row in rows)
            {
                var y = ((RectTransform)row).anchoredPosition.y;
                Place(party, Sprite("Art/Characters/char_" + row.name.Substring(4) + "_sd"), new Vector2(0, 1), new Vector2(98, y + 8), new Vector2(76, 76), Color.white);
            }
            if (party.Find(Marker) == null) Place(party, null, Vector2.zero, Vector2.zero, Vector2.zero, Color.clear, true);
            int hp = -1;
            foreach (var text in frame.GetComponentsInChildren<Text>())
            {
                if (!text.text.StartsWith("파티 체력")) continue;
                var m = Regex.Match(text.text, @"(\d+)\s*/\s*(\d+)");
                if (m.Success) hp = int.Parse(m.Groups[1].Value);
                break;
            }
            if (hp >= 0 && lastPartyHp >= 0 && hp < lastPartyHp) StartCoroutine(Hit(party));
            if (hp >= 0) lastPartyHp = hp;
        }

        static int HpOf(Text[] texts)
        {
            foreach (var text in texts) { var m = Hp.Match(text.text); if (m.Success) return int.Parse(m.Groups[1].Value); }
            return -1;
        }

        // A short shake plus a red flash over the panel; stops quietly if the next redraw destroys the panel.
        IEnumerator Hit(RectTransform panel)
        {
            var flash = Place(panel, null, new Vector2(0.5f, 0.5f), Vector2.zero, panel.rect.size, HitFlash, true, last: true);
            var origin = panel.anchoredPosition;
            const float duration = 0.28f;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                if (panel == null) yield break;
                float k = 1 - t / duration;
                panel.anchoredPosition = origin + new Vector2(Mathf.Sin(t * 90f) * 9f * k, 0);
                if (flash != null) flash.color = new Color(HitFlash.r, HitFlash.g, HitFlash.b, HitFlash.a * k);
                yield return null;
            }
            if (panel == null) yield break;
            panel.anchoredPosition = origin;
            if (flash != null) Destroy(flash.gameObject);
        }

        // One non-raycast image under the parent: first child by default (above the parent's panel, below its text and buttons),
        // or last child for overlays. A null sprite with plain=true draws a flat colour (or an invisible marker).
        Image? Place(Transform parent, Sprite? sprite, Vector2 anchor, Vector2 position, Vector2 size, Color color, bool plain = false, bool last = false)
        {
            if (sprite == null && !plain) return null;
            var obj = new GameObject(Marker, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            if (last) obj.transform.SetAsLastSibling(); else obj.transform.SetAsFirstSibling();
            var r = (RectTransform)obj.transform;
            r.anchorMin = r.anchorMax = anchor; r.pivot = anchor; r.anchoredPosition = position; r.sizeDelta = size;
            var image = obj.GetComponent<Image>();
            image.sprite = sprite; image.color = color; image.preserveAspect = sprite != null; image.raycastTarget = false;
            return image;
        }

        Sprite? EnemyArt(string name)
        {
            if (enemyIds == null)
            {
                enemyIds = new Dictionary<string, string>();
                var asset = Resources.Load<TextAsset>("MemoryForge/battle-content");
                if (asset != null && JObject.Parse(asset.text)["Battle"]?["Enemies"] is JArray enemies)
                    foreach (var enemy in enemies)
                        if (enemy["Name"]?.ToString() is string n && enemy["Id"]?.ToString() is string id) enemyIds[n] = id;
            }
            return enemyIds.TryGetValue(name, out var found) ? Sprite("Art/Enemies/enemy_" + found) : null;
        }

        Sprite? Sprite(string path)
        {
            if (sprites.TryGetValue(path, out var cached)) return cached;
            var texture = Resources.Load<Texture2D>(path);
            var sprite = texture == null ? null : UnityEngine.Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprites[path] = sprite;
            return sprite;
        }
    }
}
