#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    // EXPEDITION-UI-20261018: the redesigned expedition view's phone layout and decoration guarantees, driven through real practices with
    // the client's public commands only (no injected Run state).
    public class ExpeditionUiLayoutPlayModeTests
    {
        // 48dp in frame units for phones whose safe area fits the 1600×900 frame at 1.2 px per unit (2340×1080): 420 dpi → 105, 480 dpi
        // → 120, 520 dpi → 130; 0 is the desktop layout (no finger-size requirement, every other check still applies).
        static readonly float[] PhoneTouches = { 0, 105, 120, 130 };
        float touch;
        readonly List<GameObject> hosts = new List<GameObject>();
        bool reducedBefore;

        [SetUp]
        public void Remember() { reducedBefore = MemoryForgeUiMotion.Reduced; }
        [UnityTearDown]
        public IEnumerator Restore()
        {
            MemoryForgeExpeditionClient.TouchOverride = null;
            foreach (string key in guideKeys) { PlayerPrefs.DeleteKey(key + ".done"); PlayerPrefs.DeleteKey(key + ".skipped"); }
            guideKeys.Clear();
            if (MemoryForgeUiMotion.Reduced != reducedBefore) MemoryForgeUiMotion.Reduced = reducedBefore;
            foreach (var host in hosts) if (host != null) Object.Destroy(host);
            hosts.Clear();
            yield return null;
        }

        // On a phone every control is at least 48dp in both directions, no two controls' touch areas overlap (so each keeps all of it),
        // and each takes the press anywhere on its own drawn face, on every screen of whole short expeditions and on the sheets opened
        // from them, with the first-run guide attached (its buttons count too). Enemy tier/selection chips never cover the enemy's name.
        [UnityTest, Timeout(900000)] // three densities × several whole expeditions
        public IEnumerator PhoneTouchTargetsAreFortyEightDpAndEachFaceTakesItsOwnPress()
        {
            foreach (float units in PhoneTouches)
            {
                touch = units; MemoryForgeExpeditionClient.TouchOverride = units;
                yield return PhoneWalk();
            }
        }
        IEnumerator PhoneWalk()
        {
            var taken = new HashSet<string>(); var checkedKinds = new List<string>();
            string[] wanted = { "map", "battle_two", "battle_five", "reward", "rest", "forge", "shop", "event", "save_victory", "result", "summary", "lastAction", "picks" };
            bool deckPick = false;
            // The seeds (in this order, with the driver's preference for screens not yet seen) that reach every wanted screen: 1234 the
            // first battle, picks, rest, a boss and the save; 1235 event, forge and shop; 1237 another boss; 1245 a five-enemy elite fight.
            foreach (long seed in new long[] { 1234, 1235, 1237, 1245 })
            {
                var host = OpenWithGuide(seed); var c = host.GetComponent<MemoryForgeExpeditionClient>(); yield return null; int before = checkedKinds.Count;
                var driver = new ExpeditionUiCapturePlayModeTests.Driver();
                for (int step = 0; step < 8000 && !c.ShowingResult; step++)
                {
                    string? key = ExpeditionUiCapturePlayModeTests.Key(c);
                    bool summary = c.HasTurnSummary && !checkedKinds.Contains("summary"), picks = c.PickerOpen && !checkedKinds.Contains("picks"),
                        last = c.ActionPresentationTerminal && !checkedKinds.Contains("lastAction"), fresh = key != null && !taken.Contains(key);
                    // A frame only where a check is due (the guide draws in LateUpdate) and now and then to let time pass.
                    if (summary || picks || last || fresh || step % 16 == 0) yield return Settle();
                    if (summary) { AssertTouch(host, "turn summary"); checkedKinds.Add("summary"); }
                    if (picks) { AssertTouch(host, "pick sheet"); checkedKinds.Add("picks"); }
                    if (last) { AssertTouch(host, "last action sheet"); checkedKinds.Add("lastAction"); }
                    if (fresh && taken.Add(key!))
                    {
                        checkedKinds.Add(key!);
                        AssertTouch(host, key!);
                        if (touch == 0) { yield return AtCanvasScale(host, 1600, 900, key!); yield return AtCanvasScale(host, 2340, 1080, key!); }
                        if (key!.StartsWith("battle", StringComparison.Ordinal) || key.StartsWith("boss", StringComparison.Ordinal)) AssertChipsClearNames(host, key);
                        if (key == "map")
                        {
                            Press(host, "DeckButton"); yield return Settle(); AssertTouch(host, "deck sheet"); Press(host, "CloseDeck"); yield return Settle();
                            Press(host, "LogButton"); yield return Settle(); AssertTouch(host, "log sheet"); Press(host, "CloseLog"); yield return Settle();
                            Press(host, "AbandonButton"); yield return Settle(); AssertTouch(host, "abandon sheet"); Press(host, "CancelAbandon"); yield return Settle();
                        }
                        if (key == "battle_two")
                        {
                            Press(host, "CardInfoButton"); yield return Settle(); AssertTouch(host, "card info sheet"); Press(host, "CloseCardInfo"); yield return Settle();
                        }
                        if (key == "rest" && !deckPick && Usable(host, "RestUpgrade") != null)
                        {
                            deckPick = true; Press(host, "RestUpgrade"); yield return Settle(); AssertTouch(host, "deck pick sheet"); checkedKinds.Add("deckPick");
                            Press(host, "CancelDeckPick"); yield return Settle();
                        }
                    }
                    driver.Step(c, taken);
                }
                Assert.That(c.ShowingResult, Is.True, "the driver finishes the short expedition of seed " + seed);
                yield return Settle();
                if (!checkedKinds.Contains("result")) { AssertTouch(host, "result"); checkedKinds.Add("result"); }
                Debug.Log("[ExpeditionUiLayout] seed " + seed + " added: " + string.Join(", ", checkedKinds.Skip(before)));
                Object.Destroy(host); yield return null;
            }
            foreach (string kind in wanted) Assert.That(checkedKinds, Does.Contain(kind), "the walk reached " + kind);
            Debug.Log("[ExpeditionUiLayout] phone touch " + touch + " units checked on: " + string.Join(", ", checkedKinds));
        }

        // A defeat changes no slot: the surviving enemies keep the very rectangles they had (only a summon may re-lay the band).
        [UnityTest]
        public IEnumerator DefeatingAnEnemyLeavesEverySurvivorsSlotWhereItWas()
        {
            var host = Open(1234); var c = host.GetComponent<MemoryForgeExpeditionClient>(); yield return null;
            Press(host, b => b.name.StartsWith("Node:", StringComparison.Ordinal) && b.interactable); yield return null;
            var b0 = c.Snapshot!.Battle!; Assert.That(b0.Enemies.Count(e => e.Alive), Is.GreaterThanOrEqualTo(2), "seed 1234's first battle has two foes");
            string victim = b0.Enemies.Where(e => e.Alive).OrderBy(e => e.Hp).First().Uid;
            var before = Slots(host); int count = b0.Enemies.Count;
            for (int guard = 0; guard < 400 && c.Snapshot!.Phase == "BATTLE" && c.Snapshot.Battle!.Enemies.Single(e => e.Uid == victim).Alive; guard++)
            {
                if (c.HasTurnSummary) { c.SkipTurnSummary(); c.ContinueTurnSummary(); continue; }
                if (c.HasActionPresentation) { c.SkipActionPresentation(); continue; }
                if (c.PickerOpen) { c.AutoPick(); continue; }
                var b = c.Snapshot.Battle!; c.SelectEnemy(victim); bool played = false;
                foreach (string uid in b.Zones.Hand) { c.SelectCard(uid); c.SelectMode("now"); if (c.ConfirmCard()) { played = true; break; } if (c.PickerOpen) { c.AutoPick(); played = true; break; } }
                if (!played) c.EndTurn();
            }
            yield return null; Canvas.ForceUpdateCanvases();
            var b1 = c.Snapshot!.Battle;
            Assert.That(b1, Is.Not.Null, "the battle goes on after the first defeat");
            Assert.That(b1!.Enemies.Single(e => e.Uid == victim).Alive, Is.False);
            Assume.That(b1.Enemies.Count, Is.EqualTo(count), "no summon re-laid the band");
            var after = Slots(host);
            foreach (var e in b1.Enemies) Assert.That(after[e.Uid], Is.EqualTo(before[e.Uid]), "slot of " + e.Uid);
            Assert.That(View(host).GetComponentsInChildren<Button>().Single(x => x.name == "Enemy:" + victim).interactable, Is.False, "a defeated enemy takes no input");
            Assert.That(View(host).GetComponentsInChildren<Text>().Any(t => t.text == "처치"), Is.True, "and says 처치");
        }

        // Switching to 동작 줄이기 while a play's decoration runs settles it at once: the flying card is gone, the drained bar part is
        // gone and a hit number stands still.
        [UnityTest]
        public IEnumerator ReducedMotionSettlesRunningDecorationAtOnce()
        {
            if (MemoryForgeUiMotion.Reduced) MemoryForgeUiMotion.Reduced = false;
            var host = Open(1234); var c = host.GetComponent<MemoryForgeExpeditionClient>(); yield return null;
            Press(host, b => b.name.StartsWith("Node:", StringComparison.Ordinal) && b.interactable); yield return null;
            yield return Settle();
            var b0 = c.Snapshot!.Battle!; string aim = b0.Enemies.First(e => e.Alive).Uid; c.SelectEnemy(aim);
            bool played = false;
            foreach (string uid in b0.Zones.Hand)
            {
                int hp = c.Snapshot.Battle!.Enemies.Single(e => e.Uid == aim).Hp;
                c.SelectCard(uid); c.SelectMode("now");
                if (c.ConfirmCard() && c.Snapshot.Battle!.Enemies.Single(e => e.Uid == aim).Hp < hp) { played = true; break; }
                if (c.HasActionPresentation) c.SkipActionPresentation();
            }
            Assume.That(played, Is.True, "a hand card damaged the enemy");
            yield return null;
            var view = View(host);
            Assert.That(view.GetComponentsInChildren<RectTransform>().Any(r => r.name == "PlayedCard"), Is.True, "positive control: the played card flies");
            var number = view.GetComponentsInChildren<Text>().First(t => t.name == "HitNumber");
            Assert.That(c.SetActionPace(MemoryForgeExpeditionClient.ActionPacing.Reduced), Is.True);
            yield return null; yield return null;
            Assert.That(view.GetComponentsInChildren<RectTransform>().Any(r => r.name == "PlayedCard" || r.name == "Lost"), Is.False, "flight and drain settle at once");
            if (number != null)
            {
                var at = number.rectTransform.anchoredPosition; yield return null; yield return null;
                if (number != null) Assert.That(number.rectTransform.anchoredPosition, Is.EqualTo(at), "the hit number stands still");
            }
        }

        // ───────── helpers ─────────
        GameObject Open(long seed) { var host = ExpeditionUiCapturePlayModeTests.Open(seed); hosts.Add(host); return host; }
        // The client with the first-run guide on a fresh, test-only progress record (removed again at teardown).
        readonly List<string> guideKeys = new List<string>();
        GameObject OpenWithGuide(long seed)
        {
            var host = new GameObject("ExpeditionUiLayoutHost"); host.SetActive(false); hosts.Add(host);
            var client = host.AddComponent<MemoryForgeExpeditionClient>(); client.Seed = seed; client.Length = "short";
            var guide = host.AddComponent<MemoryForgeOnboarding>(); guide.PrefsKey = "MemoryForge.Onboarding.layouttest." + Guid.NewGuid().ToString("N"); guideKeys.Add(guide.PrefsKey);
            host.SetActive(true); return host;
        }
        static IEnumerator Settle() { yield return null; Canvas.ForceUpdateCanvases(); }
        static GameObject View(GameObject host)
        {
            var active = host.GetComponentsInChildren<Canvas>().Where(c => c.name == MemoryForgeExpeditionClient.OwnedCanvas && c.gameObject.activeInHierarchy).ToList();
            Assert.That(active.Count, Is.EqualTo(1), "exactly one active owned expedition view");
            return active[0].gameObject;
        }
        static Button? Usable(GameObject host, string name) => View(host).GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == name && b.IsInteractable());
        static void Press(GameObject host, string name) => Press(host, b => b.name == name && b.IsInteractable());
        static void Press(GameObject host, Func<Button, bool> which)
        {
            Canvas.ForceUpdateCanvases();
            var button = View(host).GetComponentsInChildren<Button>().First(which);
            var face = button.targetGraphic.rectTransform;
            var point = RectTransformUtility.WorldToScreenPoint(null, face.TransformPoint(face.rect.center));
            var hit = Top(point);
            Assert.That(hit, Is.SameAs(button), button.name + " takes the press at its face centre");
            button.onClick.Invoke();
        }
        static Button? Top(Vector2 point)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left }, hits);
            return hits.Count == 0 ? null : hits[0].gameObject.GetComponentInParent<Button>();
        }
        // Every drawn control of the topmost layer (the open sheet, else the screen): its hit rectangle is at least PhoneTouch units both
        // ways, and five points of its own face (centre and 20% in from each corner) take the press for it. Controls whose face lies
        // outside its scroll viewport (scrolled away) are skipped; those are reached by scrolling.
        void AssertTouch(GameObject host, string what)
        {
            Canvas.ForceUpdateCanvases();
            var view = View(host); var frame = view.transform.Find("ExpeditionFrame");
            var layer = frame.Cast<Transform>().LastOrDefault(t => t.gameObject.activeSelf && t.name.EndsWith("InputBlocker", StringComparison.Ordinal)) ?? frame;
            var guide = frame.Find(MemoryForgeOnboarding.GuideName);
            var buttons = layer.GetComponentsInChildren<Button>().Where(b => b.isActiveAndEnabled)
                .Concat(guide != null && layer != frame ? guide.GetComponentsInChildren<Button>() : Enumerable.Empty<Button>()).Distinct().ToList();
            Assert.That(buttons, Is.Not.Empty, what + ": controls drawn");
            var corners = new Vector3[4]; int checkedCount = 0;
            foreach (var button in buttons)
            {
                var face = button.targetGraphic.rectTransform;
                face.GetWorldCorners(corners);
                var points = new[] { 0.5f, 0.2f, 0.8f }.SelectMany(u => new[] { 0.2f, 0.8f, 0.5f }.Select(v => (u, v))).Where(p => p.u == 0.5f == (p.v == 0.5f))
                    .Select(p => (Vector2)RectTransformUtility.WorldToScreenPoint(null, Vector3.Lerp(Vector3.Lerp(corners[0], corners[3], p.u), Vector3.Lerp(corners[1], corners[2], p.u), p.v))).ToList();
                var viewport = button.GetComponentsInParent<ScrollRect>(true).Select(s => s.viewport).FirstOrDefault();
                if (viewport != null && !RectTransformUtility.RectangleContainsScreenPoint(viewport, points[0])) continue;
                checkedCount++;
                // Measured where it can be pressed: on the frame (never off its edges) and, when the control is wholly scrolled into view,
                // its whole area.
                var whole = FrameRect((RectTransform)button.transform, (RectTransform)frame); var shown = TouchArea(button, (RectTransform)frame);
                if (viewport == null || shown.width >= whole.width - 0.5f && shown.height >= whole.height - 0.5f)
                    Assert.That(Math.Min(shown.width, shown.height), Is.GreaterThanOrEqualTo(touch - 0.5f), what + " at " + touch + ": " + button.name + " is at least 48dp on the frame (" + shown + ")");
                foreach (var point in points)
                {
                    if (viewport != null && !RectTransformUtility.RectangleContainsScreenPoint(viewport, point)) continue;
                    Assert.That(Top(point), Is.SameAs(button), what + " at " + touch + ": " + button.name + " takes a press on its own face at " + point);
                }
            }
            Assert.That(checkedCount, Is.GreaterThan(0), what + ": at least one control checked");
            // No two touch areas overlap (an area inside a scroll counts only where its viewport shows it).
            var areas = buttons.Select(b => (Button: b, Area: TouchArea(b, (RectTransform)frame))).Where(a => a.Area.width > 0 && a.Area.height > 0).ToList();
            for (int i = 0; i < areas.Count; i++)
                for (int k = i + 1; k < areas.Count; k++)
                {
                    var a = areas[i]; var o = areas[k];
                    if (a.Button.transform.IsChildOf(o.Button.transform) || o.Button.transform.IsChildOf(a.Button.transform)) continue;
                    float w = Math.Min(a.Area.xMax, o.Area.xMax) - Math.Max(a.Area.xMin, o.Area.xMin), h = Math.Min(a.Area.yMax, o.Area.yMax) - Math.Max(a.Area.yMin, o.Area.yMin);
                    Assert.That(w <= 0.5f || h <= 0.5f, Is.True, what + " at " + touch + ": touch areas of " + a.Button.name + " " + a.Area + " and " + o.Button.name + " " + o.Area + " overlap");
                }
            // No control's touch area lies over a scroll region it is not part of (a drag there must scroll, not press).
            foreach (var scroll in layer.GetComponentsInChildren<ScrollRect>().Where(x => x.isActiveAndEnabled && x.viewport != null))
            {
                var region = Clip(FrameRect(scroll.viewport, (RectTransform)frame), ((RectTransform)frame).rect);
                foreach (var a in areas)
                {
                    if (a.Button.transform.IsChildOf(scroll.viewport) || scroll.transform.IsChildOf(a.Button.transform)) continue;
                    float w = Math.Min(a.Area.xMax, region.xMax) - Math.Max(a.Area.xMin, region.xMin), h = Math.Min(a.Area.yMax, region.yMax) - Math.Max(a.Area.yMin, region.yMin);
                    Assert.That(w <= 0.5f || h <= 0.5f, Is.True, what + " at " + touch + ": " + a.Button.name + " " + a.Area + " lies over the scroll " + scroll.name + " " + region);
                }
            }
            AssertWhole(layer.GetComponentsInChildren<Text>().Concat(guide != null && layer != frame ? guide.GetComponentsInChildren<Text>() : Enumerable.Empty<Text>()), what + " at " + touch);
        }
        // Every text shows all of its characters (nothing is cut): generated with the label's own settings (at its canvas's scale), its
        // visible characters equal those of the same text with no height limit. Generated here because a label scrolled out of view is
        // culled and has no mesh yet; rich-text guide lines are skipped.
        static void AssertWhole(IEnumerable<Text> texts, string what)
        {
            foreach (var text in texts)
            {
                if (!text.isActiveAndEnabled || text.text.Length == 0 || text.text.Contains("<")) continue;
                var settings = text.GetGenerationSettings(text.rectTransform.rect.size);
                var drawn = new TextGenerator(); drawn.Populate(text.text, settings);
                var open = settings; open.verticalOverflow = VerticalWrapMode.Overflow; open.resizeTextForBestFit = false;
                var whole = new TextGenerator(); whole.Populate(text.text, open);
                Assert.That(drawn.characterCountVisible, Is.GreaterThanOrEqualTo(whole.characterCountVisible),
                    what + ", canvas scale " + settings.scaleFactor + ": '" + text.text + "' is cut (" + text.transform.parent.name + ")");
            }
        }
        // The same screen drawn by a camera at another size (another canvas scale: 1600×900 is 1.0, a 2340×1080 phone 1.32): the client
        // redraws at that scale, and nothing is cut there either. The overlay is restored afterwards.
        static IEnumerator AtCanvasScale(GameObject host, int width, int height, string what)
        {
            var texture = new RenderTexture(width, height, 24); var eye = new GameObject("LayoutScaleCamera").AddComponent<Camera>();
            eye.targetTexture = texture;
            var canvas = View(host).GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = eye;
            yield return null; yield return null; yield return null; // the scaler takes the camera's size, then the client redraws
            Canvas.ForceUpdateCanvases();
            var redrawn = View(host).GetComponent<Canvas>();
            float expected = Mathf.Pow(2, Mathf.Lerp(Mathf.Log(width / 1600f, 2), Mathf.Log(height / 900f, 2), 0.5f));
            Assert.That(redrawn.scaleFactor, Is.EqualTo(expected).Within(0.01f), what + ": the camera's scale applies");
            AssertWhole(redrawn.GetComponentsInChildren<Text>(), what + " at " + width + "×" + height);
            redrawn.renderMode = RenderMode.ScreenSpaceOverlay; redrawn.worldCamera = null;
            yield return null; yield return null; yield return null;
            Object.Destroy(eye.gameObject); texture.Release();
        }
        // A control's touch area in frame units (its root rectangle), clipped to the frame (the screen part a press can reach) and to the
        // scroll viewport that shows it.
        static Rect TouchArea(Button button, RectTransform frame)
        {
            var area = Clip(FrameRect((RectTransform)button.transform, frame), frame.rect);
            var viewport = button.GetComponentsInParent<ScrollRect>(true).Select(s => s.viewport).FirstOrDefault();
            return viewport == null ? area : Clip(area, FrameRect(viewport, frame));
        }
        static Rect Clip(Rect a, Rect b) => Rect.MinMaxRect(Math.Max(a.xMin, b.xMin), Math.Max(a.yMin, b.yMin), Math.Min(a.xMax, b.xMax), Math.Min(a.yMax, b.yMax));
        static Rect FrameRect(RectTransform r, RectTransform frame)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            Vector3 a = frame.InverseTransformPoint(corners[0]), b = frame.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        }
        // Tier and selection chips sit in the name row's reserved room: no chip overlaps the text of an enemy's name.
        static void AssertChipsClearNames(GameObject host, string what)
        {
            var view = View(host); var corners = new Vector3[4];
            foreach (var enemy in view.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Enemy:", StringComparison.Ordinal)))
            {
                var chips = enemy.GetComponentsInChildren<RectTransform>().Where(r => r.name == "Tier" || r.name == "Selected").Select(r => WorldRect(r, corners)).ToList();
                var names = enemy.GetComponentsInChildren<Text>().Where(t => t.transform.parent.name == "Info" || t.transform.parent.name.StartsWith("InfoScroll", StringComparison.Ordinal)).Take(1).ToList();
                foreach (var name in names)
                {
                    var nameRect = WorldRect(name.rectTransform, corners);
                    foreach (var chip in chips) Assert.That(chip.Overlaps(nameRect), Is.False, what + ": a chip covers the name of " + enemy.name);
                }
            }
        }
        static Rect WorldRect(RectTransform r, Vector3[] corners) { r.GetWorldCorners(corners); return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y); }
        static Dictionary<string, Rect> Slots(GameObject host)
        {
            Canvas.ForceUpdateCanvases(); var corners = new Vector3[4];
            return View(host).GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Enemy:", StringComparison.Ordinal))
                .ToDictionary(b => b.name.Substring("Enemy:".Length), b => WorldRect((RectTransform)b.transform, corners));
        }
    }
}
