#nullable enable
#if UNITY_EDITOR && UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MemoryForge.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    // Real uGUI/production fitting. HTTP is a protocol fixture; local expeditions use only the shipped public practice APIs.
    public partial class PracticeServerSessionPlayModeTests
    {
        static IEnumerable<MobileCase> LegacyFrameCases()
        {
            yield return new MobileCase("legacy-s24", 2340, 1080, new Rect(96, 0, 2244, 1080), 450f / 160, 1);
            yield return new MobileCase("legacy-wide-desktop", 2560, 1080, new Rect(0, 0, 2560, 1080), 1, 1, false);
            yield return new MobileCase("legacy-tablet", 1440, 1080, new Rect(0, 48, 1440, 1000), 2, 1.3f);
        }
        static IEnumerator LegacySettle(GameObject host, MobileRenderSurface surface, string canvasName, string frameName, string state, List<string> failures)
        {
            for (int frame = 0; frame < 4; frame++) { surface.Bind(host, canvasName); yield return null; }
            Canvas.ForceUpdateCanvases();
            yield return MobileOptionalShot(host, surface.Scenario, "legacy-" + state, canvasName);
            MobileCheck(failures, surface.Scenario.Name + "/" + state + "/production-frame", () =>
            {
                var pixels = MobilePixels(MobilePanel(host, frameName), surface.Camera); var safe = surface.Scenario.Safe;
                Assert.That(pixels.xMin, Is.EqualTo(safe.xMin).Within(1)); Assert.That(pixels.xMax, Is.EqualTo(safe.xMax).Within(1));
                Assert.That(pixels.yMin, Is.EqualTo(safe.yMin).Within(1)); Assert.That(pixels.yMax, Is.EqualTo(safe.yMax).Within(1));
            });
        }
        static RectTransform LegacyPracticePanel(GameObject host, string name)
        {
            var canvas = host.GetComponentsInChildren<Canvas>().Single(c => c.name == MemoryForgeExpeditionClient.OwnedCanvas && c.isActiveAndEnabled);
            var frame = canvas.GetComponentsInChildren<RectTransform>().Single(r => r.name == "ExpeditionFrame" && r.gameObject.activeInHierarchy);
            return name == "ExpeditionFrame" ? frame : frame.GetComponentsInChildren<RectTransform>().Single(r => r.name == name && r.gameObject.activeInHierarchy);
        }
        static IEnumerator LegacyReveal(Button button, Camera camera)
        {
            var scroll = button.GetComponentInParent<ScrollRect>(); if (scroll == null) yield break;
            bool horizontal = scroll.horizontal && !scroll.vertical; var other = scroll.normalizedPosition; var rect = (RectTransform)button.transform;
            MobileSetScrollPosition(scroll, horizontal, 0, other); yield return null; Canvas.ForceUpdateCanvases(); var zero = MobilePixels(rect, camera);
            MobileSetScrollPosition(scroll, horizontal, 1, other); yield return null; Canvas.ForceUpdateCanvases(); var one = MobilePixels(rect, camera);
            var viewport = MobilePixels(scroll.viewport, camera); float delta = horizontal ? one.center.x - zero.center.x : one.center.y - zero.center.y;
            float distance = horizontal ? viewport.center.x - zero.center.x : viewport.center.y - zero.center.y;
            MobileSetScrollPosition(scroll, horizontal, Mathf.Abs(delta) < 0.01f ? 0 : Mathf.Clamp01(distance / delta), other);
            yield return null; Canvas.ForceUpdateCanvases();
        }
        static bool LegacyPointerClick(Button button, Camera camera, List<string> failures, string scope)
        {
            var rect = MobilePixels((RectTransform)button.transform, camera); var position = rect.center;
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = position, pressPosition = position, clickCount = 1 };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            var hit = hits.FirstOrDefault(); var top = hit.gameObject == null ? null : hit.gameObject.GetComponentInParent<Button>();
            MobileCheck(failures, scope + "/pointer-hit", () => Assert.That(top, Is.SameAs(button), "actual rendered pointer raycast must select its own control"));
            if (top != button) return false;
            pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = hit; pointer.pointerPress = button.gameObject; pointer.rawPointerPress = hit.gameObject;
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            return true;
        }
        Reply LegacyPureQuery(Seen request, string? title = null, string? text = null)
        {
            Assert.That(request.Path, Is.EqualTo(BattleQueryRoute), "only a pure battle query may be posted"); var asked = JObject.Parse(request.Text);
            var answer = QueryJson(world, asked["uid"]!.Value<string>()!, asked["mode"]!.Value<string>()!, asked["target"]?.Value<string>(),
                ((asked["picks"] as JArray) ?? new JArray()).Values<string>().Select(x => x!).ToList());
            if (title != null) answer["source"]!["name"] = title; if (text != null) answer["source"]!["text"] = text;
            return Reply.Json(200, answer);
        }
        static void LegacySheetFits(ScrollRect scroll, MobileCase scenario, Camera camera)
        {
            var viewport = MobilePixels(scroll.viewport, camera); var content = MobilePixels(scroll.content, camera);
            Assert.That(content.width, Is.LessThanOrEqualTo(viewport.width + 1), scroll.name + " stretch content cannot add the authored width twice");
            foreach (var text in scroll.content.GetComponentsInChildren<Text>().Where(t => MobileVisible(t) && !string.IsNullOrWhiteSpace(t.text)))
            {
                var pixels = MobilePixels(text.rectTransform, camera);
                Assert.That(pixels.width, Is.LessThanOrEqualTo(viewport.width + 1), scroll.name + " text line width");
                Assert.That(pixels.xMin, Is.GreaterThanOrEqualTo(viewport.xMin - 1)); Assert.That(pixels.xMax, Is.LessThanOrEqualTo(viewport.xMax + 1));
                MobileWholeText(text, scenario, camera, MobileTextFloor(text));
            }
        }
        sealed class LegacyPractice : IDisposable
        {
            public readonly GameObject Host;
            public readonly MemoryForgeExpeditionClient Client;
            public readonly MobileRenderSurface Surface;
            public LegacyPractice(MobileCase scenario, long seed = 1234)
            {
                MemoryForgeAccountConnection.SetCurrent(null); MemoryForgePracticeSession.ResetForTests(); MobileApplyCalibration(scenario);
                Host = new GameObject("LegacyPractice:" + scenario.Name); Host.SetActive(false);
                Client = Host.AddComponent<MemoryForgeExpeditionClient>(); Client.Seed = seed; Client.Length = "short";
                Surface = new MobileRenderSurface(scenario); Host.SetActive(true);
            }
            public IEnumerator Ready(List<string> failures)
            {
                yield return Until(() => Client.Snapshot != null, "real practice Create");
                yield return LegacySettle(Host, Surface, MemoryForgeExpeditionClient.OwnedCanvas, "ExpeditionFrame", "practice-map", failures);
            }
            public IEnumerator EnterBattle(List<string> failures)
            {
                var node = Host.GetComponentsInChildren<Button>().First(b => b.name.StartsWith("Node:", StringComparison.Ordinal) && b.interactable);
                Assert.That(Client.EnterNode(node.name.Substring(5)), Is.True, Client.LastError); yield return null;
                Assert.That(Client.Snapshot!.Phase, Is.EqualTo("BATTLE"));
                yield return LegacySettle(Host, Surface, MemoryForgeExpeditionClient.OwnedCanvas, "ExpeditionFrame", "practice-battle", failures);
            }
            public void Dispose()
            { Surface.Dispose(); Object.Destroy(Host); MemoryForgePracticeSession.ResetForTests(); MobileResetCalibration(); MemoryForgeAccountConnection.SetCurrent(null); }
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator LegacyPhoneHandNeverCoversFixedTurnOrPileControlsAndItsRealCardTapCannotEndTheTurn()
        {
            var baseline = LegacyFrameCases().First(); var failures = new List<string>();
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            try
            {
                foreach (var scenario in new[] { baseline, new MobileCase("legacy-s24-font130", baseline.Width, baseline.Height, baseline.Safe, baseline.Density, 1.3f) })
                {
                    using (var practice = new LegacyPractice(scenario))
                    {
                        yield return practice.Ready(failures); yield return practice.EnterBattle(failures);
                        Assert.That(practice.Client.Snapshot!.Battle!.Zones.Hand.Count, Is.EqualTo(5), "the real starter battle supplies the five-card regression hand");
                        var frame = LegacyPracticePanel(practice.Host, "ExpeditionFrame");
                        var cards = frame.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Hand:", StringComparison.Ordinal)).ToArray();
                        Assert.That(cards.Length, Is.EqualTo(5)); var hand = cards[0].GetComponentInParent<ScrollRect>(); Assert.That(hand, Is.Not.Null);
                        var reserved = new[] { "EndTurn", "DrawPile", "DiscardPile" }.Select(name => LegacyPracticePanel(practice.Host, name)).ToList();
                        reserved.AddRange(frame.GetComponentsInChildren<Text>().Where(t => t.transform.parent == frame
                            && (t.name == "DrawCount" || t.name == "DiscardCount" || t.text.StartsWith("덱", StringComparison.Ordinal)
                                || t.text.StartsWith("뽑을 ", StringComparison.Ordinal) || t.text.StartsWith("버림", StringComparison.Ordinal) || (t.name == "ExhaustCount" || t.text.StartsWith("소멸", StringComparison.Ordinal)))).Select(t => t.rectTransform));
                        string unchanged = CanonicalState.Hash(practice.Client.Snapshot.ToJsonGraph()); int turn = practice.Client.Snapshot.Battle.Turn;
                        foreach (float position in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                        {
                            hand!.StopMovement(); hand.horizontalNormalizedPosition = position; yield return null; Canvas.ForceUpdateCanvases();
                            var viewport = MobilePixels(hand.viewport, practice.Surface.Camera);
                            foreach (var card in cards)
                            {
                                var visible = MobileIntersection(MobilePixels((RectTransform)card.transform, practice.Surface.Camera), viewport);
                                visible = MobileIntersection(visible, scenario.Safe); if (visible.width <= 0 || visible.height <= 0) continue;
                                foreach (var control in reserved)
                                {
                                    var controlPixels = MobilePixels(control, practice.Surface.Camera);
                                    MobileCheck(failures, scenario.Name + "/hand-" + position + "/" + card.name + "/" + control.name,
                                        () => MobileSeparate(visible, controlPixels, "visible hand card " + card.name + " normalized=" + position + " clipped=" + visible + " viewport=" + viewport + " must not cover fixed " + control.name + "=" + controlPixels));
                                }
                            }
                        }
                        var endTurn = LegacyPracticePanel(practice.Host, "EndTurn");
                        foreach (var text in endTurn.GetComponentsInChildren<Text>())
                            MobileCheck(failures, scenario.Name + "/end-turn-whole-label/" + text.name, () =>
                            {
                                MobileWholeText(text, scenario, practice.Surface.Camera, MobileTextFloor(text));
                                var pixels = MobilePixels(text.rectTransform, practice.Surface.Camera);
                                MobileInside(pixels, MobilePixels(endTurn, practice.Surface.Camera), "every whole turn-end label stays inside its own actual hit rectangle");
                                MobileInside(pixels, scenario.Safe, "every whole turn-end label stays inside native safe pixels");
                            });
                        var discard = frame.GetComponentsInChildren<Text>().Single(t => t.transform.parent == frame && (t.name == "DiscardCount" || t.text.StartsWith("버림", StringComparison.Ordinal)));
                        MobileCheck(failures, scenario.Name + "/end-turn-discard-drawn-glyphs", () =>
                        {
                            MobileWholeText(discard, scenario, practice.Surface.Camera, MobileTextFloor(discard));
                            var discardGlyphs = MobileVisibleGlyphs(discard, scenario, practice.Surface.Camera);
                            Assert.That(discardGlyphs, Is.Not.Empty, "the published discard count must actually draw");
                            var discardBounds = Rect.MinMaxRect(discardGlyphs.Min(r => r.xMin), discardGlyphs.Min(r => r.yMin), discardGlyphs.Max(r => r.xMax), discardGlyphs.Max(r => r.yMax));
                            foreach (var caption in endTurn.GetComponentsInChildren<Text>().Where(t => !string.IsNullOrWhiteSpace(t.text)))
                            {
                                var glyphs = MobileVisibleGlyphs(caption, scenario, practice.Surface.Camera);
                                Assert.That(glyphs, Is.Not.Empty, "the turn-end caption must actually draw");
                                var bounds = Rect.MinMaxRect(glyphs.Min(r => r.xMin), glyphs.Min(r => r.yMin), glyphs.Max(r => r.xMax), glyphs.Max(r => r.yMax));
                                Debug.Log("[LegacyTurnGlyphs] " + scenario.Name + " caption=" + bounds + " discard=" + discardBounds + " discardText=" + discard.text);
                                MobileSeparate(bounds, discardBounds, "actual rendered turn-end and discard glyph bounds must remain separate even when their label boxes overlap");
                            }
                        });
                        MobileCheck(failures, scenario.Name + "/header-turn-once-readable", () =>
                        {
                            var turns = frame.GetComponentsInChildren<Text>().Where(t => !t.transform.IsChildOf(endTurn) && t.text == turn + "턴").ToArray();
                            Assert.That(turns.Length, Is.EqualTo(1), "the published turn counter moves to the battle header exactly once");
                            MobileWholeText(turns[0], scenario, practice.Surface.Camera, MobileTextFloor(turns[0]));
                            MobileInside(MobilePixels(turns[0].rectTransform, practice.Surface.Camera), scenario.Safe, "the relocated header turn counter remains safe and readable");
                            Assert.That(MobileVisibleGlyphs(turns[0], scenario, practice.Surface.Camera), Is.Not.Empty, "the header turn counter actually draws");
                        });
                        var lastViewport = MobilePixels(hand!.viewport, practice.Surface.Camera);
                        var tapped = cards.Last(c => MobileFullyInside(MobilePixels((RectTransform)c.transform, practice.Surface.Camera), lastViewport));
                        string tappedName = tapped.name, tappedUid = tappedName.Substring(5);
                        var tappedPixels = MobilePixels((RectTransform)tapped.transform, practice.Surface.Camera);
                        MobileInside(tappedPixels, scenario.Safe, "the tapped card is fully visible, not a clipped phantom");
                        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = tappedPixels.center, pressPosition = tappedPixels.center, clickCount = 1, eligibleForClick = true };
                        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits); Assert.That(hits, Is.Not.Empty, "real visible card center has a pointer receiver");
                        var hit = hits[0]; var receiver = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject); Assert.That(receiver, Is.Not.Null);
                        Debug.Log("[LegacyHandTap] uid=" + tapped.name + " normalized=" + hand.normalizedPosition + " card=" + tappedPixels + " viewport=" + lastViewport + " actualReceiver=" + receiver.name);
                        MobileCheck(failures, scenario.Name + "/hand-real-pointer", () => Assert.That(receiver.GetComponentInParent<Button>(), Is.SameAs(tapped), "the card's actual center must select that card, never the fixed turn control"));
                        pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = hit; pointer.pointerPress = receiver; pointer.rawPointerPress = hit.gameObject;
                        ExecuteEvents.Execute(receiver, pointer, ExecuteEvents.pointerDownHandler); ExecuteEvents.Execute(receiver, pointer, ExecuteEvents.pointerUpHandler); ExecuteEvents.Execute(receiver, pointer, ExecuteEvents.pointerClickHandler);
                        yield return LegacySettle(practice.Host, practice.Surface, MemoryForgeExpeditionClient.OwnedCanvas, "ExpeditionFrame", "hand-after-real-tap", failures);
                        Assert.That(practice.Client.Snapshot!.Battle!.Turn, Is.EqualTo(turn), "a real card-center tap cannot end the turn");
                        Assert.That(CanonicalState.Hash(practice.Client.Snapshot.ToJsonGraph()), Is.EqualTo(unchanged), "card selection cannot commit an end-turn or play action");
                        Assert.That(MobileButton(practice.Host, tappedName), Is.Not.Null, "the selected UID exists in the freshly rendered hand");
                        Assert.That(practice.Client.SelectedCard, Is.EqualTo(tappedUid), "the visible card receives the actual tap");
                    }
                    yield return null;
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { MobileResetCalibration(); MemoryForgeUiMotion.Reduced = reduced; MemoryForgePracticeSession.ResetForTests(); }
            yield return null;
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator LegacyPhonePileCountProjectionKeepsTwoDigitGlyphsWholeAndSeparateWithoutChangingTheRun()
        {
            var baseline = LegacyFrameCases().First(); var failures = new List<string>();
            var scenarios = new[] {
                new MobileCase("legacy-native-small-font130", 1280, 720, new Rect(68, 0, 1212, 720), 2, 1.3f),
                new MobileCase("legacy-s24-font130", baseline.Width, baseline.Height, baseline.Safe, baseline.Density, 1.3f)
            };
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            try
            {
                foreach (var scenario in scenarios)
                {
                    using (var practice = new LegacyPractice(scenario))
                    {
                        yield return practice.Ready(failures); yield return practice.EnterBattle(failures);
                        string unchanged = CanonicalState.Hash(practice.Client.Snapshot!.ToJsonGraph());
                        Assert.That(host.Posts, Is.Empty, "local practice setup cannot POST");
                        var render = typeof(MemoryForgeExpeditionClient).GetMethod("Piles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert.That(render, Is.Not.Null);
                        var renderAp = typeof(MemoryForgeExpeditionClient).GetMethod("ApGem", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert.That(renderAp, Is.Not.Null);
                        foreach (var pileCounts in new[] { (Count: 10, Exhaust: 0), (Count: 23, Exhaust: 0), (Count: 99, Exhaust: 0), (Count: 10, Exhaust: 3), (Count: 23, Exhaust: 12), (Count: 99, Exhaust: 88) })
                        {
                            int count = pileCounts.Count, exhaustCount = pileCounts.Exhaust;
                            var frame = LegacyPracticePanel(practice.Host, "ExpeditionFrame");
                            foreach (var old in frame.GetComponentsInChildren<RectTransform>().Where(r => r.parent == frame
                                && (r.name == "ApGem" || r.name == "ApGlow" || r.name == "DrawPile" || r.name == "DiscardPile" || r.name == "DrawCount" || r.name == "DiscardCount" || r.name == "ExhaustCount"
                                    || r.GetComponent<Text>() is Text label && (label.text.StartsWith("뽑을 ", StringComparison.Ordinal)
                                        || label.text.StartsWith("덱", StringComparison.Ordinal) || label.text.StartsWith("버림", StringComparison.Ordinal) || label.text.StartsWith("소멸", StringComparison.Ordinal)))).ToArray())
                            { old.gameObject.SetActive(false); Object.Destroy(old.gameObject); }
                            // Detached presentation counts exercise the row width; these UIDs are never cards in the live run.
                            var projection = new BattleState { Ap = practice.Client.Snapshot!.Battle!.Ap, Zones = new BattleZones {
                                Draw = Enumerable.Range(0, count).Select(i => "presentation-only:draw:" + i).ToList(),
                                Discard = Enumerable.Range(0, count).Select(i => "presentation-only:discard:" + i).ToList(),
                                Exhaust = Enumerable.Range(0, exhaustCount).Select(i => "presentation-only:exhaust:" + i).ToList()
                            } };
                            renderAp!.Invoke(practice.Client, new object[] { projection });
                            render!.Invoke(practice.Client, new object[] { projection });
                            yield return null; Canvas.ForceUpdateCanvases();
                            string scope = scenario.Name + "/pile-projection-" + count + "-exhaust-" + exhaustCount;
                            var labels = frame.GetComponentsInChildren<Text>().Where(t => t.transform.parent == frame && t.isActiveAndEnabled).ToArray();
                            var draw = labels.Single(t => t.name == "DrawCount" || t.text.StartsWith("뽑을 ", StringComparison.Ordinal) || t.text.StartsWith("덱", StringComparison.Ordinal));
                            var discard = labels.Single(t => t.name == "DiscardCount" || t.text.StartsWith("버림", StringComparison.Ordinal));
                            var exhaustLabels = labels.Where(t => t.name == "ExhaustCount" || t.text.StartsWith("소멸", StringComparison.Ordinal)).ToArray();
                            Assert.That(exhaustLabels.Length, Is.EqualTo(exhaustCount == 0 ? 0 : 1), scope + " publishes exactly the nonzero exhaust count");
                            MobileCheck(failures, scope + "/exact-counts", () =>
                            {
                                Assert.That(draw.text.Replace(" ", ""), Is.EqualTo("뽑을" + count).Or.EqualTo("덱" + count));
                                Assert.That(discard.text.Replace(" ", ""), Is.EqualTo("버림" + count));
                                if (exhaustCount != 0) Assert.That(exhaustLabels[0].text.Replace(" ", ""), Is.EqualTo("소멸" + exhaustCount));
                            });
                            foreach (var text in new[] { draw, discard }.Concat(exhaustLabels))
                                MobileCheck(failures, scope + "/whole-single-line/" + text.name, () =>
                                {
                                    MobileWholeText(text, scenario, practice.Surface.Camera, MobileTextFloor(text));
                                    var glyphs = MobileVisibleGlyphs(text, scenario, practice.Surface.Camera);
                                    // Strong adds shadow quads; check character generation separately from the decorated mesh.
                                    Assert.That(text.cachedTextGenerator.characterCountVisible, Is.GreaterThanOrEqualTo(text.text.Count(c => !char.IsWhiteSpace(c))), "every count character must be generated");
                                    Assert.That(glyphs.Count, Is.GreaterThanOrEqualTo(text.text.Count(c => !char.IsWhiteSpace(c))), "every count glyph must actually draw");
                                    Assert.That(text.cachedTextGenerator.lines.Count, Is.EqualTo(1), "a pile count cannot wrap into the control row below");
                                    foreach (var glyph in glyphs) MobileInside(glyph, MobilePixels(text.rectTransform, practice.Surface.Camera), "every drawn count glyph stays in its own label");
                                });
                            MobileCheck(failures, scope + "/discard-end-turn-glyphs", () =>
                            {
                                var glyphs = MobileVisibleGlyphs(discard, scenario, practice.Surface.Camera); Assert.That(glyphs, Is.Not.Empty);
                                var bounds = Rect.MinMaxRect(glyphs.Min(r => r.xMin), glyphs.Min(r => r.yMin), glyphs.Max(r => r.xMax), glyphs.Max(r => r.yMax));
                                foreach (var caption in LegacyPracticePanel(practice.Host, "EndTurn").GetComponentsInChildren<Text>().Where(t => !string.IsNullOrWhiteSpace(t.text)))
                                {
                                    var other = MobileVisibleGlyphs(caption, scenario, practice.Surface.Camera); Assert.That(other, Is.Not.Empty);
                                    var otherBounds = Rect.MinMaxRect(other.Min(r => r.xMin), other.Min(r => r.yMin), other.Max(r => r.xMax), other.Max(r => r.yMax));
                                    MobileSeparate(bounds, otherBounds, "actual discard and turn-end glyph bounds cannot overlap at " + count);
                                }
                            });
                            MobileCheck(failures, scope + "/draw-ap-gem", () =>
                            {
                                var glyphs = MobileVisibleGlyphs(draw, scenario, practice.Surface.Camera); Assert.That(glyphs, Is.Not.Empty);
                                var bounds = Rect.MinMaxRect(glyphs.Min(r => r.xMin), glyphs.Min(r => r.yMin), glyphs.Max(r => r.xMax), glyphs.Max(r => r.yMax));
                                var ap = LegacyPracticePanel(practice.Host, "ApGem").GetComponentsInChildren<Text>().Single(text => text.text == practice.Client.Snapshot!.Battle!.Ap.ToString());
                                var apGlyphs = MobileVisibleGlyphs(ap, scenario, practice.Surface.Camera); Assert.That(apGlyphs, Is.Not.Empty);
                                var apBounds = Rect.MinMaxRect(apGlyphs.Min(r => r.xMin), apGlyphs.Min(r => r.yMin), apGlyphs.Max(r => r.xMax), apGlyphs.Max(r => r.yMax));
                                MobileSeparate(bounds, apBounds, "actual draw count glyphs cannot cover the AP number at " + count);
                            });
                            if (exhaustCount != 0)
                                MobileCheck(failures, scope + "/exhaust-control-glyphs", () =>
                                {
                                    var glyphs = MobileVisibleGlyphs(exhaustLabels[0], scenario, practice.Surface.Camera); Assert.That(glyphs, Is.Not.Empty);
                                    var bounds = Rect.MinMaxRect(glyphs.Min(r => r.xMin), glyphs.Min(r => r.yMin), glyphs.Max(r => r.xMax), glyphs.Max(r => r.yMax));
                                    var ap = LegacyPracticePanel(practice.Host, "ApGem").GetComponentsInChildren<Text>().Single(text => text.text == practice.Client.Snapshot!.Battle!.Ap.ToString());
                                    var neighbors = new[] { draw, discard, ap }.Concat(LegacyPracticePanel(practice.Host, "EndTurn").GetComponentsInChildren<Text>().Where(t => !string.IsNullOrWhiteSpace(t.text)));
                                    foreach (var neighbor in neighbors)
                                    {
                                        var other = MobileVisibleGlyphs(neighbor, scenario, practice.Surface.Camera); Assert.That(other, Is.Not.Empty);
                                        var otherBounds = Rect.MinMaxRect(other.Min(r => r.xMin), other.Min(r => r.yMin), other.Max(r => r.xMax), other.Max(r => r.yMax));
                                        MobileSeparate(bounds, otherBounds, "actual exhaust count glyphs cannot overlap " + neighbor.name + " / " + neighbor.text + " at " + exhaustCount);
                                    }
                                });
                            Assert.That(CanonicalState.Hash(practice.Client.Snapshot!.ToJsonGraph()), Is.EqualTo(unchanged), "a detached presentation projection cannot change the live Core graph");
                            Assert.That(host.Posts, Is.Empty, "a detached presentation projection cannot POST");
                        }
                    }
                    yield return null;
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { MobileResetCalibration(); MemoryForgeUiMotion.Reduced = reduced; MemoryForgePracticeSession.ResetForTests(); }
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator LegacyMobileServerHandAndUltimateTilesAreReachableAndARealCardPointerOnlyQueries()
        {
            var failures = new List<string>(); bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            try
            {
                foreach (var scenario in MobileCases().Where(c => c.Name.StartsWith("s24-", StringComparison.Ordinal)))
                {
                    MobileApplyCalibration(scenario); MemoryForgeAccountConnection.SetCurrent(null); ControlRun(); post = request => LegacyPureQuery(request);
                    var screen = new GameObject("LegacyServerHand"); var view = screen.AddComponent<MemoryForgeServerPracticeClient>();
                    using (var surface = new MobileRenderSurface(scenario))
                    {
                        try
                        {
                            view.Connect(() => Session()); yield return Settled(view); int? version = view.Snapshot!.Context.ServerVersion;
                            yield return LegacySettle(screen, surface, MemoryForgeServerPracticeClient.OwnedCanvas, "ServerPracticeFrame", "server-hand", failures);
                            var tiles = screen.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Card:", StringComparison.Ordinal) || b.name.StartsWith("Ult:", StringComparison.Ordinal)).ToArray();
                            Assert.That(tiles.Count(b => b.name.StartsWith("Card:", StringComparison.Ordinal)), Is.GreaterThanOrEqualTo(2)); Assert.That(tiles.Count(b => b.name.StartsWith("Ult:", StringComparison.Ordinal)), Is.EqualTo(3));
                            foreach (var button in tiles)
                            {
                                yield return LegacyReveal(button, surface.Camera);
                                MobileCheck(failures, scenario.Name + "/" + button.name, () =>
                                {
                                    var rect = MobilePixels((RectTransform)button.transform, surface.Camera); MobileInside(rect, scenario.Safe, button.name);
                                    var scroll = button.GetComponentInParent<ScrollRect>(); if (scroll != null) MobileInside(rect, MobilePixels(scroll.viewport, surface.Camera), button.name + " fully reachable");
                                    Assert.That(Mathf.Min(rect.width, rect.height) / scenario.Density + 0.1f, Is.GreaterThanOrEqualTo(48));
                                    foreach (var text in button.GetComponentsInChildren<Text>()) MobileWholeText(text, scenario, surface.Camera, MobileTextFloor(text));
                                });
                            }
                            var card = MobileButton(screen, "Card:h1"); yield return LegacyReveal(card, surface.Camera);
                            if (LegacyPointerClick(card, surface.Camera, failures, scenario.Name + "/Card:h1"))
                                yield return Until(() => view.QueryView?.Uid == "h1" && !view.Busy, "real card pointer produces its pure query");
                            Assert.That(view.Snapshot.Context.ServerVersion, Is.EqualTo(version)); Assert.That(host.Posts.All(r => r.Path == BattleQueryRoute), Is.True); Assert.That(Outbox().Load(), Is.Null);
                        }
                        finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); }
                    }
                    yield return null;
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { MobileResetCalibration(); MemoryForgeUiMotion.Reduced = reduced; }
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator LegacyCardDetailsCardInfoLogAndTurnSummaryKeepWholeRealCoreTextInsideTheirViewports()
        {
            var failures = new List<string>(); bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            var content = ClientBattleContent.Load(); var longest = content.Battle.Cards.OrderByDescending(c => BattleText.Card(c, content.Battle).Length).First();
            string longestText = BattleText.Card(longest, content.Battle);
            try
            {
                foreach (var scenario in MobileCases().Where(c => c.Name == "pc-1920x1080" || c.Name == "s24-left96-right0-450dpi-font100"))
                {
                    MobileApplyCalibration(scenario); MemoryForgeAccountConnection.SetCurrent(null); ControlRun(); post = request => LegacyPureQuery(request, longest.Name, longestText);
                    var screen = new GameObject("LegacyServerCardDetails"); var view = screen.AddComponent<MemoryForgeServerPracticeClient>();
                    using (var surface = new MobileRenderSurface(scenario))
                    {
                        try
                        {
                            view.Connect(() => Session()); yield return Settled(view); Assert.That(view.SelectCard("h1"), Is.True);
                            yield return Until(() => view.QueryView != null && !view.Busy, "source-text pure query"); Assert.That(view.OpenCardDetails(), Is.True);
                            yield return LegacySettle(screen, surface, MemoryForgeServerPracticeClient.OwnedCanvas, "ServerPracticeFrame", "server-card-details", failures);
                            var scroll = screen.GetComponentsInChildren<ScrollRect>().Single(s => s.name == "CardDetailsScroll");
                            MobileCheck(failures, scenario.Name + "/server-card-details", () => LegacySheetFits(scroll, scenario, surface.Camera));
                            Assert.That(DetailsBody(view)!.text, Does.Contain(longestText)); Assert.That(host.Posts.All(r => r.Path == BattleQueryRoute), Is.True); Assert.That(Outbox().Load(), Is.Null);
                        }
                        finally { Object.Destroy(screen); }
                    }
                    yield return null;
                    using (var practice = new LegacyPractice(scenario))
                    {
                        yield return practice.Ready(failures); string original = CanonicalState.Hash(practice.Client.Snapshot!.ToJsonGraph());
                        Assert.That(practice.Client.ShowCardInfo(longest.Id), Is.True);
                        yield return LegacySettle(practice.Host, practice.Surface, MemoryForgeExpeditionClient.OwnedCanvas, "ExpeditionFrame", "practice-card-info", failures);
                        var cardInfo = practice.Host.GetComponentsInChildren<ScrollRect>().Single(s => s.name == "CardInfoScroll");
                        MobileCheck(failures, scenario.Name + "/practice-card-info", () => LegacySheetFits(cardInfo, scenario, practice.Surface.Camera));
                        Assert.That(CanonicalState.Hash(practice.Client.Snapshot.ToJsonGraph()), Is.EqualTo(original)); practice.Client.CloseCardInfo(); yield return null;
                        MobileClick(practice.Host, "LogButton");
                        yield return LegacySettle(practice.Host, practice.Surface, MemoryForgeExpeditionClient.OwnedCanvas, "ExpeditionFrame", "practice-log", failures);
                        var log = practice.Host.GetComponentsInChildren<ScrollRect>().Single(s => s.name == "ExpeditionLogScroll");
                        MobileCheck(failures, scenario.Name + "/practice-log", () => LegacySheetFits(log, scenario, practice.Surface.Camera));
                        Assert.That(CanonicalState.Hash(practice.Client.Snapshot.ToJsonGraph()), Is.EqualTo(original)); MobileClick(practice.Host, "CloseLog"); yield return null;
                        yield return practice.EnterBattle(failures); Assert.That(practice.Client.EndTurn(), Is.True, practice.Client.LastError);
                        Assert.That(practice.Client.HasTurnSummary, Is.True); practice.Client.SkipTurnSummary();
                        string committed = CanonicalState.Hash(practice.Client.Snapshot.ToJsonGraph());
                        yield return LegacySettle(practice.Host, practice.Surface, MemoryForgeExpeditionClient.OwnedCanvas, "ExpeditionFrame", "practice-turn-summary", failures);
                        var summary = practice.Host.GetComponentsInChildren<ScrollRect>().Single(s => s.name == "TurnSummaryScroll");
                        MobileCheck(failures, scenario.Name + "/practice-turn-summary", () => LegacySheetFits(summary, scenario, practice.Surface.Camera));
                        Assert.That(CanonicalState.Hash(practice.Client.Snapshot.ToJsonGraph()), Is.EqualTo(committed));
                    }
                    yield return null;
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { MobileResetCalibration(); MemoryForgeUiMotion.Reduced = reduced; MemoryForgePracticeSession.ResetForTests(); }
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator LegacyEnemyInfoHpAndIntentRemainInsideTheirActualSlots()
        {
            var failures = new List<string>(); bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            try
            {
                foreach (var scenario in LegacyFrameCases())
                {
                    using (var practice = new LegacyPractice(scenario, 1235))
                    {
                        yield return practice.Ready(failures); yield return practice.EnterBattle(failures);
                        foreach (var enemy in practice.Client.Snapshot!.Battle!.Enemies.Where(e => e.Alive))
                        {
                            var slot = LegacyPracticePanel(practice.Host, "Enemy:" + enemy.Uid);
                            MobileCheck(failures, scenario.Name + "/enemy/" + enemy.Uid, () =>
                            {
                                var slotPixels = MobilePixels(slot, practice.Surface.Camera);
                                foreach (string name in new[] { "Info", "Hp", "Intent" })
                                {
                                    var rect = slot.GetComponentsInChildren<RectTransform>().Single(r => r.name == name);
                                    MobileInside(MobilePixels(rect, practice.Surface.Camera), slotPixels, "enemy " + name + " remains inside its own slot");
                                }
                            });
                        }
                    }
                    yield return null;
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { MobileResetCalibration(); MemoryForgeUiMotion.Reduced = reduced; MemoryForgePracticeSession.ResetForTests(); }
        }
        static Rect LegacyDrawnImage(Image image, Camera camera)
        {
            var mesh = image.canvasRenderer.GetMesh(); Assert.That(mesh, Is.Not.Null); var vertices = mesh.vertices; Assert.That(vertices.Length, Is.GreaterThanOrEqualTo(4));
            var points = vertices.Select(v => RectTransformUtility.WorldToScreenPoint(camera, image.rectTransform.TransformPoint(v))).ToArray();
            return Rect.MinMaxRect(points.Min(v => v.x), points.Min(v => v.y), points.Max(v => v.x), points.Max(v => v.y));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator LegacyBackgroundArtPreservesSpriteAspectCoversNativePixelsAndHasNoUndimmedSafeEdgeBand()
        {
            var failures = new List<string>(); bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            try
            {
                foreach (var scenario in LegacyFrameCases())
                using (var practice = new LegacyPractice(scenario))
                {
                    yield return practice.Ready(failures); yield return practice.EnterBattle(failures);
                    var backdrop = LegacyPracticePanel(practice.Host, "Backdrop");
                    var art = backdrop.GetComponentsInChildren<Image>().Single(i => i.sprite != null && i.sprite.texture.name.StartsWith("bg_", StringComparison.Ordinal));
                    var scrim = LegacyPracticePanel(practice.Host, "Scrim").GetComponent<Image>();
                    MobileCheck(failures, scenario.Name + "/background-art-and-dim", () =>
                    {
                        var drawn = LegacyDrawnImage(art, practice.Surface.Camera); float expectedAspect = art.sprite.rect.width / art.sprite.rect.height;
                        Assert.That(drawn.width / drawn.height / expectedAspect, Is.EqualTo(1).Within(0.005), "actual drawn background preserves the sprite aspect within0.5%");
                        MobileInside(new Rect(0, 0, scenario.Width, scenario.Height), drawn, "background art covers the full native viewport");
                        MobileInside(new Rect(0, 0, scenario.Width, scenario.Height), LegacyDrawnImage(scrim, practice.Surface.Camera), "uniform scrim covers the same native pixels across both safe edges");
                        Assert.That(scrim.color.a, Is.GreaterThan(0));
                        var colours = scrim.canvasRenderer.GetMesh().colors32; Assert.That(colours.Length, Is.GreaterThanOrEqualTo(4));
                        Assert.That(colours.All(c => c.Equals(colours[0])), Is.True, "the existing uniform tint/alpha continues across the notch strip; scenic RGB need not match");
                    });
                    yield return null;
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { MobileResetCalibration(); MemoryForgeUiMotion.Reduced = reduced; MemoryForgePracticeSession.ResetForTests(); }
        }





        [UnityTest, Timeout(240000)]
        public IEnumerator LegacyFramePointerForecastAndMotionStayOnTheirActualExpandedTargets()
        {
            var failures = new List<string>(); bool reduced = MemoryForgeUiMotion.Reduced;
            try
            {
                foreach (var scenario in LegacyFrameCases())
                {
                    MemoryForgeUiMotion.Reduced = true;
                    using (var practice = new LegacyPractice(scenario))
                    {
                        yield return practice.Ready(failures); yield return practice.EnterBattle(failures);
                        // Only target-local, deterministic positive forecasts are used to leave exactly one real enemy alive.
                        // This is a bounded public-client driver, not a fabricated Battle state.
                        for (int step = 0; step < 120 && practice.Client.Snapshot!.Phase == "BATTLE" && practice.Client.Snapshot.Battle!.Enemies.Count(e => e.Alive) > 1; step++)
                        {
                            if (practice.Client.HasActionPresentation) { practice.Client.SkipActionPresentation(); yield return null; continue; }
                            if (practice.Client.HasTurnSummary) { practice.Client.SkipTurnSummary(); practice.Client.ContinueTurnSummary(); yield return null; continue; }
                            if (practice.Client.PickerOpen) { practice.Client.CancelPick(); yield return null; continue; }
                            var live = practice.Client.Snapshot.Battle!; var aim = live.Enemies.Where(e => e.Alive).OrderBy(e => e.Hp).First(); practice.Client.SelectEnemy(aim.Uid);
                            var definitions = ClientRunContent.Load().Memory.Battle;
                            string? single = live.Zones.Hand.FirstOrDefault(id =>
                            {
                                var f = practice.Client.PlayForecast(id, "now"); var definition = definitions.Cards.FirstOrDefault(c => c.Id == live.Cards[id].Id);
                                return definition != null && MemoryForgeExpeditionClient.ChoiceRequests(definition, "now", definitions).Count == 0
                                    && f != null && f.Error == null && !f.Hidden && f.EnemyHpLosses.Count == 1 && f.EnemyHpLosses[0].Uid == aim.Uid;
                            });
                            if (single != null) Assert.That(practice.Client.PlayCard(single, "now"), Is.True, practice.Client.LastError);
                            else Assert.That(practice.Client.EndTurn(), Is.True, practice.Client.LastError);
                            yield return null;
                        }
                        Assert.That(practice.Client.Snapshot!.Phase, Is.EqualTo("BATTLE"));
                        if (practice.Client.HasActionPresentation) practice.Client.SkipActionPresentation();
                        if (practice.Client.HasTurnSummary) { practice.Client.SkipTurnSummary(); practice.Client.ContinueTurnSummary(); }
                        var battle = practice.Client.Snapshot!.Battle!; var enemy = battle.Enemies.Single(e => e.Alive); practice.Client.SelectEnemy(enemy.Uid);
                        string? uid = battle.Zones.Hand.FirstOrDefault(id => { var f = practice.Client.PlayForecast(id, "now"); return f != null && f.Error == null && !f.Hidden && f.EnemyHpLoss > 0; });
                        if (uid == null)
                        {
                            Assert.That(practice.Client.EndTurn(), Is.True); practice.Client.SkipTurnSummary(); practice.Client.ContinueTurnSummary();
                            battle = practice.Client.Snapshot.Battle!; enemy = battle.Enemies.Single(e => e.Alive); practice.Client.SelectEnemy(enemy.Uid); uid = battle.Zones.Hand.FirstOrDefault(id => { var f = practice.Client.PlayForecast(id, "now"); return f != null && f.Error == null && !f.Hidden && f.EnemyHpLoss > 0; });
                        }
                        Assert.That(uid, Is.Not.Null, "actual starter battle offers a legal deterministic damaging card");
                        var forecast = practice.Client.PlayForecast(uid!, "now")!;
                        yield return LegacySettle(practice.Host, practice.Surface, MemoryForgeExpeditionClient.OwnedCanvas, "ExpeditionFrame", "drag-before", failures);
                        string unchanged = CanonicalState.Hash(practice.Client.Snapshot.ToJsonGraph());
                        var card = MobileButton(practice.Host, "Hand:" + uid); var target = LegacyPracticePanel(practice.Host, "Enemy:" + enemy.Uid);
                        var handScroll = card.GetComponentInParent<ScrollRect>(); var cardBefore = MobilePixels((RectTransform)card.transform, practice.Surface.Camera);
                        Debug.Log("[LegacyDragCard] " + scenario.Name + " uid=" + uid + " handIndex=" + battle.Zones.Hand.ToList().IndexOf(uid!)
                            + " before=" + cardBefore + (handScroll == null ? " scroll=none" : " viewport=" + MobilePixels(handScroll.viewport, practice.Surface.Camera) + " normalized=" + handScroll.normalizedPosition));
                        if (handScroll != null && !MobileFullyInside(cardBefore, MobilePixels(handScroll.viewport, practice.Surface.Camera)))
                            yield return LegacyReveal(card, practice.Surface.Camera);
                        var cardPixels = MobilePixels((RectTransform)card.transform, practice.Surface.Camera);
                        Debug.Log("[LegacyDragCard] " + scenario.Name + " uid=" + uid + " after=" + cardPixels
                            + (handScroll == null ? "" : " viewport=" + MobilePixels(handScroll.viewport, practice.Surface.Camera) + " normalized=" + handScroll.normalizedPosition));
                        MobileInside(cardPixels, scenario.Safe, "selected real hand card is fully inside safe pixels");
                        if (handScroll != null) MobileInside(cardPixels, MobilePixels(handScroll.viewport, practice.Surface.Camera), "selected real hand card is fully revealed before pointer start");
                        if (scenario.Mobile) Assert.That(Mathf.Min(cardPixels.width, cardPixels.height) / scenario.Density + 0.1f, Is.GreaterThanOrEqualTo(48), "revealed hand card retains48dp");
                        foreach (var text in card.GetComponentsInChildren<Text>()) MobileWholeText(text, scenario, practice.Surface.Camera, MobileTextFloor(text));
                        Assert.That(CanonicalState.Hash(practice.Client.Snapshot.ToJsonGraph()), Is.EqualTo(unchanged), "revealing a real hand UID does not play it");
                        var start = cardPixels.center; var destination = MobilePixels(target, practice.Surface.Camera).center;
                        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = start, pressPosition = start };
                        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                        Assert.That(hits.FirstOrDefault().gameObject?.GetComponentInParent<Button>(), Is.SameAs(card), "drag must start from the actual hand's pointer hit, not direct dispatch to an offscreen control");
                        pointer.pointerPressRaycast = hits.First(); pointer.pointerCurrentRaycast = hits.First();
                        ExecuteEvents.Execute(card.gameObject, pointer, ExecuteEvents.initializePotentialDrag); pointer.dragging = true; pointer.position = Vector2.Lerp(start, destination, 0.25f);
                        ExecuteEvents.Execute(card.gameObject, pointer, ExecuteEvents.beginDragHandler); pointer.delta = destination - pointer.position; pointer.position = destination; ExecuteEvents.Execute(card.gameObject, pointer, ExecuteEvents.dragHandler);
                        // beginDrag and drag replace the presentation in this same event dispatch. Let Unity
                        // retire the previous layer before selecting the one active owned-frame visual.
                        yield return null; practice.Surface.Bind(practice.Host, MemoryForgeExpeditionClient.OwnedCanvas); Canvas.ForceUpdateCanvases();
                        MobileCheck(failures, scenario.Name + "/frame-drag-forecast", () =>
                        {
                            var arrow = LegacyPracticePanel(practice.Host, "DragArrow"); var head = arrow.GetComponentsInChildren<RectTransform>().Last(r => r.name == "ArrowHead" || r.name == "ArrowDot");
                            Assert.That(Vector2.Distance(MobilePixels(head, practice.Surface.Camera).center, destination), Is.LessThanOrEqualTo(2), "arrow head follows actual screen pointer within2px");
                            var hot = arrow.GetComponentsInChildren<RectTransform>().Single(r => r.name == "DropHot"); MobileInside(MobilePixels(target, practice.Surface.Camera), MobilePixels(hot, practice.Surface.Camera), "drop highlight surrounds its actual target");
                            var hp = target.GetComponentsInChildren<RectTransform>().Single(r => r.name == "Hp"); var loss = arrow.GetComponentsInChildren<RectTransform>().Single(r => r.name == "ForecastLoss");
                            var track = MobilePixels(hp, practice.Surface.Camera); var strip = MobilePixels(loss, practice.Surface.Camera); int hpLoss = Math.Min(enemy.Hp, forecast.EnemyHpLoss);
                            Assert.That((strip.xMin - track.xMin) / track.width, Is.EqualTo((enemy.Hp - hpLoss) / (float)enemy.MaxHp).Within(0.005));
                            Assert.That(strip.width / track.width, Is.EqualTo(hpLoss / (float)enemy.MaxHp).Within(0.005)); MobileInside(strip, track, "drag forecast stays on the real HP track");
                        });
                        pointer.position = new Vector2(-1000, -1000); ExecuteEvents.Execute(card.gameObject, pointer, ExecuteEvents.dragHandler); ExecuteEvents.Execute(card.gameObject, pointer, ExecuteEvents.endDragHandler);
                        Assert.That(CanonicalState.Hash(practice.Client.Snapshot.ToJsonGraph()), Is.EqualTo(unchanged), "a canceled drag does not play a card");
                    }
                    yield return null; MemoryForgeUiMotion.Reduced = false;
                    using (var practice = new LegacyPractice(scenario))
                    {
                        yield return practice.Ready(failures); yield return practice.EnterBattle(failures);
                        var battle = practice.Client.Snapshot!.Battle!; var enemy = battle.Enemies.First(e => e.Alive && e.Hp * 100 >= e.MaxHp * 55); practice.Client.SelectEnemy(enemy.Uid);
                        string? uid = battle.Zones.Hand.FirstOrDefault(id => { var f = practice.Client.PlayForecast(id, "now"); return f != null && f.Error == null && !f.Hidden && f.EnemyHpLoss > 0; });
                        Assert.That(uid, Is.Not.Null); Assert.That(practice.Client.PlayCard(uid!, "now"), Is.True, practice.Client.LastError);
                        string committed = CanonicalState.Hash(practice.Client.Snapshot.ToJsonGraph()); bool sawLost = false, sawFly = false; Vector2? lastFly = null; Rect targetPixels = default;
                        float motionDeadline = Time.realtimeSinceStartup + 2;
                        for (int step = 0; step < 3000 && Time.realtimeSinceStartup < motionDeadline; step++)
                        {
                            practice.Surface.Bind(practice.Host, MemoryForgeExpeditionClient.OwnedCanvas); Canvas.ForceUpdateCanvases();
                            foreach (var lost in practice.Host.GetComponentsInChildren<RectTransform>().Where(r => r.name == "Lost"))
                            { sawLost = true; MobileCheck(failures, scenario.Name + "/drain-motion", () => MobileInside(MobilePixels(lost, practice.Surface.Camera), MobilePixels((RectTransform)lost.parent, practice.Surface.Camera), "Lost stays inside its real HP bar on every frame")); }
                            var fly = practice.Host.GetComponentsInChildren<RectTransform>().FirstOrDefault(r => r.name == "PlayedCard");
                            if (fly != null)
                            {
                                sawFly = true; lastFly = MobilePixels(fly, practice.Surface.Camera).center;
                                targetPixels = MobilePixels(LegacyPracticePanel(practice.Host, "Enemy:" + enemy.Uid), practice.Surface.Camera);
                            }
                            if (step > 2 && !practice.Host.GetComponentsInChildren<RectTransform>().Any(r => r.name == "Lost" || r.name == "PlayedCard")) break;
                            yield return null;
                        }
                        MobileCheck(failures, scenario.Name + "/played-card-motion", () => { Assert.That(sawLost, Is.True); Assert.That(sawFly, Is.True); Assert.That(lastFly.HasValue && targetPixels.Contains(lastFly.Value), Is.True, "played-card path ends inside its actual target rect"); });
                        Assert.That(CanonicalState.Hash(practice.Client.Snapshot.ToJsonGraph()), Is.EqualTo(committed), "presentation never repeats the committed Core action");
                    }
                    yield return null;
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { MobileResetCalibration(); MemoryForgeUiMotion.Reduced = reduced; MemoryForgePracticeSession.ResetForTests(); }
        }
        [UnityTest, Timeout(90000)]
        public IEnumerator LegacyLocalPracticePreparationKeepsItsHeldStoreAndSelectedLengthAcrossLiveReflow()
        {
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true; var failures = new List<string>();
            MemoryForgeAccountConnection.SetCurrent(null); MemoryForgePracticeSession.ResetForTests();
            var initial = new MobileCase("legacy-preparation-font100", 2340, 1080, new Rect(96, 0, 2244, 1080), 450f / 160, 1);
            var changed = new MobileCase("legacy-preparation-font130", 2340, 1080, new Rect(96, 0, 2244, 1080), 450f / 160, 1.3f);
            MobileApplyCalibration(initial);
            var source = new GameObject("LegacyHeldPractice"); source.SetActive(false); var client = source.AddComponent<MemoryForgeExpeditionClient>(); client.Seed = 1234; client.Length = "short";
            var destinations = new List<string>(); client.SceneLoader = destinations.Add; source.SetActive(true); GameObject? home = null;
            try
            {
                yield return Until(() => client.Snapshot != null, "actual local practice"); Assert.That(client.Abandon(), Is.True); Assert.That(client.ReturnToWorkshop(), Is.True);
                Assert.That(destinations, Is.EqualTo(new[] { MemoryForgeExpeditionClient.LobbyScene })); var held = MemoryForgePracticeSession.Peek(); Assert.That(held, Is.Not.Null);
                string store = CanonicalState.Hash(held!.Store); int departures = held.Departures, serial = held.RequestSerial; string heldLength = held.Length;
                Object.Destroy(source); yield return null;
                home = new GameObject("LegacyPracticePreparation"); var lobby = home.AddComponent<MemoryForgeLobbyClient>();
                using (var first = new MobileRenderSurface(initial))
                {
                    yield return null; lobby.Player!.Stop(); MobileClick(home, "StartExpedition");
                    yield return LegacySettle(home, first, MemoryForgeLobbyClient.OwnedCanvas, "LobbyFrame", "local-preparation-before", failures);
                    MobileClick(home, "Length:full"); var selectedTexture = MobileButton(home, "Length:full").GetComponent<Image>().sprite.texture.name;
                    MobileApplyCalibration(changed);
                    using (var second = new MobileRenderSurface(changed))
                    {
                        yield return LegacySettle(home, second, MemoryForgeLobbyClient.OwnedCanvas, "LobbyFrame", "local-preparation-reflow", failures);
                        MobileCheck(failures, "local-practice-preparation-reflow", () =>
                        {
                            var full = home.GetComponentsInChildren<Button>().SingleOrDefault(b => b.name == "Length:full");
                            var shortButton = home.GetComponentsInChildren<Button>().SingleOrDefault(b => b.name == "Length:short");
                            Assert.That(full, Is.Not.Null, "anonymous local practice preparation cannot become account preparation"); Assert.That(shortButton, Is.Not.Null);
                            Assert.That(full!.GetComponent<Image>().sprite.texture.name, Is.EqualTo(selectedTexture));
                            Assert.That(shortButton!.GetComponent<Image>().sprite.texture.name, Is.Not.EqualTo(selectedTexture), "pending full length remains selected without Depart");
                        });
                    }
                }
                var after = MemoryForgePracticeSession.Peek(); Assert.That(after, Is.Not.Null); Assert.That(CanonicalState.Hash(after!.Store), Is.EqualTo(store));
                Assert.That(after.Departures, Is.EqualTo(departures)); Assert.That(after.RequestSerial, Is.EqualTo(serial)); Assert.That(after.Length, Is.EqualTo(heldLength));
                Assert.That(MemoryForgeAccountConnection.Current, Is.Null); Assert.That(host.Seen, Is.Empty); Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { if (home != null) Object.Destroy(home); if (source != null) Object.Destroy(source); MemoryForgePracticeSession.ResetForTests(); MobileResetCalibration(); MemoryForgeUiMotion.Reduced = reduced; }
            yield return null;
        }
        [UnityTest, Timeout(240000)]
        public IEnumerator LegacyOverflowingAuthoredChoicesRemainReachableWithoutChoosingAnOutcome()
        {
            var failures = new List<string>(); bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            bool sawOverflow = false;
            try
            {
                foreach (var scenario in LegacyFrameCases().Concat(new[] { new MobileCase("legacy-landscape-multiwindow-450dpi-font130", 1560, 540, new Rect(64, 16, 1496, 508), 450f / 160, 1.3f) }))
                {
                    // The small multiwindow calibration targets the event sheet, not a battle
                    // layout stress test. Reach the authored event under the normal S24 first.
                    var travel = scenario.Name.StartsWith("legacy-landscape-multiwindow", StringComparison.Ordinal) ? LegacyFrameCases().First() : scenario;
                    using (var practice = new LegacyPractice(travel, 1235))
                    {
                        yield return practice.Ready(failures); yield return practice.EnterBattle(failures);
                        foreach (var enemy in practice.Client.Snapshot!.Battle!.Enemies.Where(e => e.Alive))
                        {
                            var slot = LegacyPracticePanel(practice.Host, "Enemy:" + enemy.Uid);
                            MobileCheck(failures, scenario.Name + "/enemy/" + enemy.Uid, () =>
                            {
                                var slotPixels = MobilePixels(slot, practice.Surface.Camera);
                                foreach (string name in new[] { "Info", "Hp", "Intent" })
                                {
                                    var rect = slot.GetComponentsInChildren<RectTransform>().Single(r => r.name == name);
                                    MobileInside(MobilePixels(rect, practice.Surface.Camera), slotPixels, "enemy " + name + " remains inside its own slot");
                                }
                            });
                        }
                        var driver = new ExpeditionUiCapturePlayModeTests.Driver(); var taken = new HashSet<string> { "battle_five", "shop", "forge", "rest" };
                        for (int step = 0; step < 500 && practice.Client.Snapshot.Phase != "EVENT"; step++)
                        {
                            Assert.That(practice.Client.Snapshot.Phase != "VICTORY" && practice.Client.Snapshot.Phase != "DEFEAT", Is.True, "published seed1235 must reach its authored event before settlement");
                            driver.Step(practice.Client, taken); yield return null;
                        }
                        Assert.That(practice.Client.Snapshot.Phase, Is.EqualTo("EVENT"));
                        string before = CanonicalState.Hash(practice.Client.Snapshot.ToJsonGraph());
                        MobileApplyCalibration(scenario);
                        using (var eventSurface = new MobileRenderSurface(scenario))
                        {
                            yield return LegacySettle(practice.Host, eventSurface, MemoryForgeExpeditionClient.OwnedCanvas, "ExpeditionFrame", "authored-event", failures);
                            var choices = practice.Host.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("EventChoice:", StringComparison.Ordinal)).ToArray(); Assert.That(choices.Length, Is.GreaterThan(1));
                            var overflow = practice.Host.GetComponentsInChildren<ScrollRect>().SingleOrDefault(s => s.name == "EventChoices");
                            var eventPanel = LegacyPracticePanel(practice.Host, "Event");
                            Debug.Log("[LegacyEventChoices] " + scenario.Name + " count=" + choices.Length + " touchUnits=" + MemoryForgeUiViewport.TouchUnits()
                                + " panel=" + MobilePixels(eventPanel, eventSurface.Camera) + " overflow=" + (overflow != null)
                                + (overflow == null ? "" : " viewport=" + MobilePixels(overflow.viewport, eventSurface.Camera) + " content=" + MobilePixels(overflow.content, eventSurface.Camera))
                                + " rows=" + string.Join(";", choices.Select(c => c.name + ":" + MobilePixels((RectTransform)c.transform, eventSurface.Camera))));
                            if (overflow != null)
                            {
                                sawOverflow = true;
                                foreach (var choice in choices) yield return MobileScrollButtonReachability(overflow, choice, scenario, eventSurface.Camera, failures);
                            }
                            else foreach (var choice in choices) MobileCheck(failures, scenario.Name + "/" + choice.name, () => MobileInside(MobilePixels((RectTransform)choice.transform, eventSurface.Camera), scenario.Safe, "authored nonoverflow choice"));
                            Assert.That(CanonicalState.Hash(practice.Client.Snapshot.ToJsonGraph()), Is.EqualTo(before), "live event reflow and scrolling never choose an outcome");
                        }
                    }
                    yield return null;
                }
                Assert.That(sawOverflow, Is.True, "at least one real authored event must exercise overflowing choices");
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally { MobileResetCalibration(); MemoryForgeUiMotion.Reduced = reduced; MemoryForgePracticeSession.ResetForTests(); }
        }

    }
}
#endif
