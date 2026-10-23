// Synthetic dialogue exercises live viewport calibration; it is not product character/narrative content.
#nullable enable
#if UNITY_EDITOR && UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public sealed class MomentViewportLifecyclePlayModeTests
    {
        const float Density = 450f / 160f;
        readonly List<Object> owned = new List<Object>();
        GameObject host = null!;
        RectTransform canvas = null!, frame = null!;
        Camera camera = null!;
        RenderTexture target = null!;
        MemoryForgeMomentPlayer player = null!;
        MomentScene scene = null!;
        int started, finished, talkFigureLoads, afterFigureLoads;
        MomentScene? startedScene, finishedScene;

        static string Dialogue => "  " + string.Join("  ", Enumerable.Range(1, 80).Select(n => "기록" + n.ToString("D3") + "번째 문장")) + "\n\n  끝맺음  \n";

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (camera != null) camera.targetTexture = null;
            foreach (var item in owned) if (item != null) Object.Destroy(item);
            owned.Clear();
            MemoryForgeUiViewport.EditorMetricsOverride = null;
            MemoryForgeUiViewport.RefreshMetrics();
            yield return null;
        }

        [UnityTest]
        public IEnumerator WaitingDialogueReflowsLargerFontWithoutReplayOrLostText() => ReflowWhileWaiting(true);

        [UnityTest]
        public IEnumerator WaitingDialogueReflowsSmallerPixelScaleWithoutReplayOrLostText() => ReflowWhileWaiting(false);

        [UnityTest]
        public IEnumerator TypingDialogueReflowKeepsAbsoluteProgressWithoutReplayOrLostText()
        {
            ApplyMetrics(2340, 1080, 1); BuildPlayer(); yield return SettleFrame();
            player.Play(scene); yield return FullPage();
            string completedPrefix = Body().text;
            player.Tap(); yield return NewPage(completedPrefix);
            float typingDeadline = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < typingDeadline && Body().text.Length < 8 && !player.WaitingForTap) yield return null;
            Assert.That(player.WaitingForTap, Is.False, "change calibration while the second page is still typing");
            string typedPrefix = Body().text;
            Assert.That(typedPrefix.Length, Is.GreaterThanOrEqualTo(8));
            Assert.That(Dialogue.Substring(completedPrefix.Length).StartsWith(typedPrefix, StringComparison.Ordinal), Is.True);
            int absoluteProgress = completedPrefix.Length + typedPrefix.Length;
            var figure = player.FigureImage; var figureSprite = figure.sprite;

            // Font-only calibration needs no asynchronous canvas resize: retain precisely the progress visible at this call.
            ApplyMetrics(2340, 1080, 1.3f);
            Canvas.ForceUpdateCanvases(); MemoryForgeUiViewport.Fit(frame, canvas); player.ReflowControls();
            yield return null;
            var body = Body();
            Assert.That(body.fontSize * MemoryForgeUiViewport.Calibration.x, Is.GreaterThanOrEqualTo(14 * Density * 1.3f - 0.01f));
            Assert.That(body.text.StartsWith(typedPrefix, StringComparison.Ordinal), Is.True, "reflow cannot rewind already-typed characters to the page start");
            Assert.That(completedPrefix.Length + body.text.Length, Is.GreaterThanOrEqualTo(absoluteProgress), "absolute progress includes the previously consumed page");
            Assert.That(Dialogue.Substring(completedPrefix.Length).StartsWith(body.text, StringComparison.Ordinal), Is.True);
            AssertWholePage(body); AssertInsideFrame(body.rectTransform); AssertContinuity(figure, figureSprite);
            yield return CompleteRemaining(Dialogue, completedPrefix, figure, figureSprite);
        }

        [UnityTest]
        public IEnumerator FinalPageReflowsDuringAuthoredHoldAndKeepsItsOuterTapWait()
        {
            const string finalText = "  마지막 문장  \n  공백 보존  ";
            const float authoredHold = 1.2f;
            ApplyMetrics(2340, 1080, 1); BuildPlayer(); yield return SettleFrame();
            scene.Beats[0].Line!.Text = finalText; scene.Beats[0].Hold = authoredHold;
            Assert.That(player.Pages(finalText).Count, Is.EqualTo(1), "this branch reaches the hold after Say returns");
            player.Play(scene); player.Tap();
            for (int n = 0; n < 12 && Body().text != finalText; n++) yield return null;
            Assert.That(Body().text, Is.EqualTo(finalText));
            Assert.That(player.Playing, Is.True); Assert.That(player.WaitingForTap, Is.False, "the authored hold is active after the final page");
            float observedHoldAt = Time.time;
            var figure = player.FigureImage; var figureSprite = figure.sprite;
            while (Time.time - observedHoldAt < 0.45f) yield return null;
            Assert.That(player.WaitingForTap, Is.False, "the calibration change is inside the existing hold");

            ApplyMetrics(2340, 1080, 1.3f);
            Canvas.ForceUpdateCanvases(); MemoryForgeUiViewport.Fit(frame, canvas); player.ReflowControls();
            yield return null;
            Assert.That(Body().fontSize * MemoryForgeUiViewport.Calibration.x, Is.GreaterThanOrEqualTo(14 * Density * 1.3f - 0.01f), "completed Say retains calibration context through its authored hold");
            Assert.That(Body().text, Is.EqualTo(finalText), "fully revealed text remains revealed after Say has returned");
            AssertWholePage(Body()); AssertInsideFrame(Body().rectTransform); AssertContinuity(figure, figureSprite);
            float outerWaitDeadline = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < outerWaitDeadline && !player.WaitingForTap && player.Playing) yield return null;
            Assert.That(player.WaitingForTap, Is.True, "normal hold completion still reaches the beat's outer tap wait");
            Assert.That(Time.time - observedHoldAt, Is.GreaterThanOrEqualTo(authoredHold - Time.maximumDeltaTime - 0.05f), "reflow does not cut the existing authored hold short");
            Assert.That(Time.time - observedHoldAt, Is.LessThanOrEqualTo(authoredHold + Mathf.Max(0.35f, Time.maximumDeltaTime * 1.1f)), "reflow does not restart the authored hold");
            Assert.That(Body().text, Is.EqualTo(finalText)); AssertContinuity(figure, figureSprite);
            yield return CompleteRemaining(finalText, "", figure, figureSprite);
        }

        [UnityTest]
        public IEnumerator CompletedLineDoesNotBecomeUnreadPagesDuringLaterSilentBeatReflow()
        {
            ApplyMetrics(2340, 1080, 1); BuildPlayer(); yield return SettleFrame();
            string firstLine = player.Pages(Dialogue)[0];
            scene.Beats[0].Line!.Text = firstLine;
            scene.Beats.Add(new MomentBeat { Figure = "after", Sfx = "test_later_beat_sfx_unavailable", Hold = 1.2f });
            player.Play(scene); yield return FullPage();
            Assert.That(Body().text, Is.EqualTo(firstLine));
            player.Tap();
            float nextBeatDeadline = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < nextBeatDeadline && player.CurrentFigure != "after") yield return null;
            Assert.That(player.CurrentFigure, Is.EqualTo("after"), "the first line's final tap has entered the later no-Line beat");
            Assert.That(player.WaitingForTap, Is.False, "the later beat has its own authored hold");
            Assert.That(afterFigureLoads, Is.EqualTo(1));
            var figure = player.FigureImage; var sprite = figure.sprite;

            ApplyMetrics(2340, 1080, 1.3f);
            Canvas.ForceUpdateCanvases(); MemoryForgeUiViewport.Fit(frame, canvas); player.ReflowControls();
            yield return null;
            Assert.That(Body().text, Is.Not.Empty, "a completed dialogue remains visible during the later silent beat");
            Assert.That(firstLine.Contains(Body().text), Is.True, "reflow retains only the already-authored visible dialogue");
            float holdDeadline = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < holdDeadline && !player.WaitingForTap && player.Playing) yield return null;
            Assert.That(player.WaitingForTap, Is.True);
            Assert.That(finished, Is.Zero);
            Assert.That(started, Is.EqualTo(1)); Assert.That(startedScene, Is.SameAs(scene));
            Assert.That(player.CurrentFigure, Is.EqualTo("after"));
            Assert.That(player.FigureImage, Is.SameAs(figure)); Assert.That(player.FigureImage.sprite, Is.SameAs(sprite));
            Assert.That(talkFigureLoads, Is.EqualTo(1)); Assert.That(afterFigureLoads, Is.EqualTo(1));

            player.Tap(); // The later beat needs exactly its own tap, never another gate for the completed line's suffix.
            float finishDeadline = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < finishDeadline && player.Playing) yield return null;
            Assert.That(player.Playing, Is.False, "completed line text cannot create new unread pages or extra taps in a later beat");
            Assert.That(finished, Is.EqualTo(1)); Assert.That(finishedScene, Is.SameAs(scene));
            Assert.That(talkFigureLoads, Is.EqualTo(1)); Assert.That(afterFigureLoads, Is.EqualTo(1));
            Assert.That(player.LastError, Is.Null);
        }

        [UnityTest]
        public IEnumerator TapAfterLengtheningAWaitingPageRevealsItWithoutConsumingIt()
        {
            ApplyMetrics(2340, 1080, 1.3f); BuildPlayer(); yield return SettleFrame();
            player.Play(scene); yield return FullPage();
            string previousPage = Body().text;
            var figure = player.FigureImage; var sprite = figure.sprite;
            Assert.That(previousPage.Length, Is.LessThan(Dialogue.Length), "start at an internal page gate");

            ApplyMetrics(2340, 1080, 1);
            Canvas.ForceUpdateCanvases(); MemoryForgeUiViewport.Fit(frame, canvas);
            player.ReflowControls(); player.Tap(); // Deliberately synchronous: the old page gate has not resumed yet.
            yield return null; yield return null;

            Assert.That(player.WaitingForTap, Is.True, "the tap reveals the newly longer current page immediately, instead of being discarded before slow typing");
            Assert.That(Body().text.Length, Is.GreaterThan(previousPage.Length));
            Assert.That(Body().text.StartsWith(previousPage, StringComparison.Ordinal), Is.True, "the reveal cannot consume or jump past this page");
            Assert.That(Body().text.Length, Is.LessThan(Dialogue.Length), "remaining pages still precede the authored beat hold and outer wait");
            AssertWholePage(Body()); AssertInsideFrame(Body().rectTransform); AssertContinuity(figure, sprite);
            yield return CompleteRemaining(Dialogue, "", figure, sprite);
        }

        IEnumerator CompleteRemaining(string original, string completedPrefix, Image figure, Sprite figureSprite)
        {
            string observed = completedPrefix;
            for (int page = 0; page < 256 && player.Playing; page++)
            {
                yield return FullPage();
                var body = Body(); string slice = body.text;
                Assert.That(slice.Length, Is.GreaterThan(0));
                Assert.That(observed.Length, Is.LessThan(original.Length));
                Assert.That(original.Substring(observed.Length).StartsWith(slice, StringComparison.Ordinal), Is.True, "displayed remainder is contiguous with the consumed prefix");
                AssertWholePage(body); AssertInsideFrame(body.rectTransform); AssertContinuity(figure, figureSprite);
                observed += slice; player.Tap();
                if (observed.Length == original.Length)
                {
                    for (int n = 0; n < 12 && player.Playing; n++) yield return null;
                    break;
                }
                yield return NewPage(slice);
            }
            Assert.That(observed, Is.EqualTo(original), "consumed and remaining text preserve every whitespace character exactly once");
            Assert.That(player.Playing, Is.False); Assert.That(started, Is.EqualTo(1)); Assert.That(finished, Is.EqualTo(1));
            Assert.That(startedScene, Is.SameAs(scene)); Assert.That(finishedScene, Is.SameAs(scene));
            Assert.That(talkFigureLoads, Is.EqualTo(1)); Assert.That(player.LastError, Is.Null);
        }

        IEnumerator ReflowWhileWaiting(bool increaseFont)
        {
            ApplyMetrics(2340, 1080, 1);
            BuildPlayer();
            yield return SettleFrame();
            Assert.That(MemoryForgeUiViewport.Calibration.x, Is.EqualTo(1.2f).Within(0.001f));
            player.Play(scene);
            yield return FullPage();
            string completedPrefix = Body().text;
            Assert.That(completedPrefix.Length, Is.GreaterThan(0).And.LessThan(Dialogue.Length));
            Assert.That(Dialogue.StartsWith(completedPrefix, StringComparison.Ordinal), Is.True);
            AssertWholePage(Body());

            player.Tap();
            yield return NewPage(completedPrefix);
            yield return FullPage();
            string oldCurrentPage = Body().text;
            Assert.That(Dialogue.Substring(completedPrefix.Length).StartsWith(oldCurrentPage, StringComparison.Ordinal), Is.True);
            int oldFont = Body().fontSize;
            var figure = player.FigureImage;
            var figureSprite = figure.sprite;

            // The current page has been revealed but not consumed. Reflow keeps the consumed prefix and restarts at this page's start.
            ApplyMetrics(increaseFont ? 2340 : 1560, increaseFont ? 1080 : 720, increaseFont ? 1.3f : 1);
            if (!increaseFont) SetTarget(1560, 720);
            yield return SettleFrame();
            player.ReflowControls();
            yield return null;
            Canvas.ForceUpdateCanvases();

            var body = Body();
            float pixelsPerUnit = MemoryForgeUiViewport.Calibration.x;
            float requiredPixels = 14 * Density * (increaseFont ? 1.3f : 1);
            Assert.That(body.fontSize * pixelsPerUnit, Is.GreaterThanOrEqualTo(requiredPixels - 0.01f), "an already-active Say adopts the current 14sp body floor before another Say or Pages call");
            Assert.That(body.fontSize, Is.GreaterThan(oldFont));
            AssertContinuity(figure, figureSprite);
            yield return FullPage();
            AssertDialogChrome();
            Assert.That(Body().text.Length, Is.LessThan(oldCurrentPage.Length), "the newly larger font must repaginate the current page, not retain its old overflowing slice");

            string observed = completedPrefix;
            for (int page = 0; page < 256 && player.Playing; page++)
            {
                yield return FullPage();
                body = Body();
                string slice = body.text;
                Assert.That(slice.Length, Is.GreaterThan(0));
                Assert.That(Dialogue.Substring(observed.Length).StartsWith(slice, StringComparison.Ordinal), Is.True, "the next displayed slice starts exactly after previously consumed text");
                AssertWholePage(body);
                AssertInsideFrame(body.rectTransform);
                AssertContinuity(figure, figureSprite);
                observed += slice;
                player.Tap();
                if (observed.Length == Dialogue.Length)
                {
                    for (int n = 0; n < 12 && player.Playing; n++) yield return null;
                    break;
                }
                yield return NewPage(slice);
            }
            Assert.That(observed, Is.EqualTo(Dialogue), "completed pages plus every repaginated remainder preserve all whitespace exactly once");
            Assert.That(player.Playing, Is.False);
            Assert.That(started, Is.EqualTo(1));
            Assert.That(finished, Is.EqualTo(1));
            Assert.That(startedScene, Is.SameAs(scene));
            Assert.That(finishedScene, Is.SameAs(scene));
            Assert.That(talkFigureLoads, Is.EqualTo(1), "reflow must not replay the beat that owns the dialogue and its SFX cue");
            Assert.That(player.LastError, Is.Null);
        }

        void BuildPlayer()
        {
            started = finished = talkFigureLoads = afterFigureLoads = 0; startedScene = finishedScene = null;
            host = new GameObject("MomentViewportLifecycleHost"); owned.Add(host);
            camera = new GameObject("MomentViewportLifecycleCamera").AddComponent<Camera>(); camera.transform.SetParent(host.transform, false);
            camera.orthographic = true; SetTarget(2340, 1080);
            var root = new GameObject("MomentViewportCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)); root.transform.SetParent(host.transform, false);
            canvas = (RectTransform)root.transform;
            var ui = root.GetComponent<Canvas>(); ui.renderMode = RenderMode.ScreenSpaceCamera; ui.worldCamera = camera;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
            frame = Rect("MomentViewportFrame", canvas); frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f); frame.sizeDelta = new Vector2(1600, 900);
            var stage = Rect("Stage", frame); MemoryForgeUiViewport.ArtIsland(stage);
            var texture = new Texture2D(40, 80); owned.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 40, 80), new Vector2(0.5f, 0.5f)); owned.Add(sprite);
            player = host.AddComponent<MemoryForgeMomentPlayer>();
            player.Build(stage, Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular"), path => { if (path == "fixture/talk.png") talkFigureLoads++; if (path == "fixture/after.png") afterFigureLoads++; return sprite; }, 1240, new Rect(40, 610, 600, 256));
            scene = new MomentScene { Id = "test_viewport_waiting", Kind = "lobby", Input = new MomentInput { Advance = "tap" },
                Beats = new List<MomentBeat> { new MomentBeat { Figure = "talk", Sfx = "test_viewport_sfx_unavailable", Line = new MomentLine { Speaker = "fixture", Text = Dialogue } } } };
            var file = new MomentFile { FormatId = MomentFile.Format, Character = "fixture", IdleFigure = "idle", Images = new Dictionary<string, string> { ["idle"] = "fixture/idle.png", ["talk"] = "fixture/talk.png", ["after"] = "fixture/after.png" }, Scenes = new List<MomentScene> { scene } };
            player.Bind(file); player.NameOf = _ => "시험 화자";
            player.Started += value => { started++; startedScene = value; };
            player.Finished += value => { finished++; finishedScene = value; };
        }

        static RectTransform Rect(string name, Transform parent)
        { var value = new GameObject(name, typeof(RectTransform)); value.transform.SetParent(parent, false); return (RectTransform)value.transform; }
        void SetTarget(int width, int height)
        {
            if (target != null) { camera.targetTexture = null; target.Release(); }
            target = new RenderTexture(width, height, 24); owned.Add(target); camera.targetTexture = target;
        }
        static void ApplyMetrics(int width, int height, float fontScale)
        {
            float cutout = width == 2340 ? 96 : 64;
            MemoryForgeUiViewport.EditorMetricsOverride = new MemoryForgeUiViewport.Metrics(new Vector2(width, height), new Rect(cutout, 0, width - cutout, height), Density, fontScale, true, 450);
            MemoryForgeUiViewport.RefreshMetrics();
        }
        IEnumerator SettleFrame()
        {
            for (int n = 0; n < 3; n++) { Canvas.ForceUpdateCanvases(); MemoryForgeUiViewport.Fit(frame, canvas); yield return null; }
            Canvas.ForceUpdateCanvases();
        }
        Text Body() => frame.GetComponentsInChildren<Text>(true).Single(text => text.transform.parent.name == "MomentDialog" && text.fontStyle == FontStyle.Normal);
        IEnumerator FullPage()
        {
            if (!player.WaitingForTap) { player.Tap(); yield return null; }
            for (int n = 0; n < 12 && !player.WaitingForTap && player.Playing; n++) yield return null;
            Assert.That(player.Playing, Is.True, "dialogue cannot finish before its current page is consumed");
            Assert.That(player.WaitingForTap, Is.True, "the revealed page waits for an explicit tap");
        }
        IEnumerator NewPage(string previous)
        {
            for (int n = 0; n < 12 && player.Playing && Body().text == previous; n++) yield return null;
            Assert.That(player.Playing, Is.True);
            Assert.That(Body().text, Is.Not.EqualTo(previous), "one advance reaches the next page without replaying the completed slice");
        }
        void AssertContinuity(Image figure, Sprite figureSprite)
        {
            Assert.That(player.Playing, Is.True); Assert.That(player.CurrentFigure, Is.EqualTo("talk"));
            Assert.That(player.FigureImage, Is.SameAs(figure)); Assert.That(player.FigureImage.sprite, Is.SameAs(figureSprite));
            Assert.That(started, Is.EqualTo(1)); Assert.That(startedScene, Is.SameAs(scene)); Assert.That(finished, Is.Zero);
            Assert.That(talkFigureLoads, Is.EqualTo(1), "calibration does not Apply the dialogue beat a second time");
        }
        void AssertDialogChrome()
        {
            var speaker = frame.GetComponentsInChildren<Text>(true).Single(text => text.transform.parent.name == "MomentDialog" && text.fontStyle == FontStyle.Bold);
            Assert.That(speaker.text, Is.EqualTo("시험 화자"), "the fixture exercises an actual rendered speaker label");
            AssertWholePage(speaker); AssertInsideFrame(speaker.rectTransform);
            var mark = frame.GetComponentsInChildren<Text>(true).Single(text => text.text == "▼");
            Assert.That(mark.gameObject.activeInHierarchy, Is.True);
            float requiredPixels = 14 * Density * MemoryForgeUiViewport.Current.FontScale;
            Assert.That(mark.fontSize * MemoryForgeUiViewport.Calibration.x, Is.GreaterThanOrEqualTo(requiredPixels - 0.01f), "the active continuation glyph follows the same current calibration");
            AssertWholePage(mark); AssertInsideFrame(mark.rectTransform);
        }
        static void AssertWholePage(Text body)
        {
            var settings = body.GetGenerationSettings(body.rectTransform.rect.size); settings.resizeTextForBestFit = false;
            var generator = new TextGenerator(); settings.verticalOverflow = VerticalWrapMode.Overflow; generator.Populate(body.text, settings); int whole = generator.characterCountVisible;
            settings.verticalOverflow = VerticalWrapMode.Truncate; generator.Populate(body.text, settings);
            Assert.That(generator.characterCountVisible, Is.EqualTo(whole), "the entire displayed slice fits at the actual current font");
        }
        void AssertInsideFrame(RectTransform body)
        {
            var corners = new Vector3[4]; body.GetWorldCorners(corners);
            foreach (var point in corners)
            {
                var local = frame.InverseTransformPoint(point);
                Assert.That(local.x, Is.InRange(frame.rect.xMin - 1, frame.rect.xMax + 1), "dialogue body stays horizontally inside the safe frame");
                Assert.That(local.y, Is.InRange(frame.rect.yMin - 1, frame.rect.yMax + 1), "dialogue body stays vertically inside the safe frame");
            }
        }
    }
}
#endif
