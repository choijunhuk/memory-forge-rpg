// Moment format and player (docs/MOMENTS_FORMAT.md) and the lobby that hosts it. The moment file here is test-only placeholder data,
// not character content: it exercises slots, motion, camera, background, paging, tap, skip, validation and missing-art handling.
#nullable enable
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
    public class MomentPlayerPlayModeTests
    {
        const string Fixture = @"{
  ""format"": ""memory-forge-moments/1"", ""character"": ""t_char"", ""status"": ""draft"", ""idle_figure"": ""fig_a"",
  ""images"": { ""fig_a"": ""t/a.png"", ""fig_b"": ""t/b.png"", ""over"": ""t/o.png"", ""gone"": ""t/missing.png"", ""bg_missing"": ""t/bg.png"" },
  ""anchors"": { ""fig_b"": { ""face"": [0.5, 0.2] } },
  ""motions"": { ""nod"": { ""tracks"": { ""figure"": [ { ""t"": 0, ""y"": 0 }, { ""t"": 0.2, ""y"": 30, ""rot"": 4 } ] }, ""ease"": ""inOut"" } },
  ""scenes"": [
    { ""id"": ""t_lobby_01"", ""kind"": ""lobby"", ""trigger"": { ""on"": ""tap"" }, ""input"": { ""advance"": ""tap"", ""cooldown_s"": 3 },
      ""stage"": { ""background"": ""bg_missing"" },
      ""assets"": [ { ""key"": ""fig_b"", ""type"": ""image"", ""status"": ""ready"" }, { ""key"": ""gone"", ""type"": ""image"", ""status"": ""todo"" } ],
      ""beats"": [
        { ""figure"": ""fig_b"", ""overlays"": { ""o"": ""over"" }, ""motion"": ""nod"", ""camera"": { ""shot"": ""close"", ""focus"": ""face"", ""move"": ""none"" } },
        { ""line"": { ""speaker"": ""t_char"", ""text"": ""LONG"" } },
        { ""overlays"": { ""o"": null }, ""figure"": ""gone"", ""hold"": 0.4 } ] },
    { ""id"": ""t_bond_01"", ""kind"": ""bond"", ""trigger"": { ""on"": ""bond_level"", ""bond_level_min"": 3 }, ""beats"": [ { ""line"": { ""text"": ""x"" } } ] } ]
}";
        readonly List<GameObject> hosts = new List<GameObject>();
        readonly Dictionary<string, Sprite> art = new Dictionary<string, Sprite>();

        [UnityTearDown]
        public IEnumerator Clean() { foreach (var h in hosts) if (h != null) Object.Destroy(h); hosts.Clear(); yield return null; }

        static string LongText() => string.Join(" ", Enumerable.Range(1, 90).Select(i => "문장" + i + "번째")) + "\n\n  끝맺음  ";

        MemoryForgeMomentPlayer MakePlayer(out MomentFile file, System.Func<string, Sprite?>? loader = null)
        {
            foreach (var key in new[] { "t/a.png", "t/b.png", "t/o.png" })
            {
                var tex = new Texture2D(40, 80); art[key] = Sprite.Create(tex, new Rect(0, 0, 40, 80), new Vector2(0.5f, 0.5f));
            }
            var host = new GameObject("MomentHost", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)); hosts.Add(host);
            host.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var player = host.AddComponent<MemoryForgeMomentPlayer>();
            player.Build((RectTransform)host.transform, Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular"), loader ?? (p => art.TryGetValue(p, out var s) ? s : null), 1040);
            file = MomentFile.Parse(Fixture.Replace("LONG", LongText().Replace("\n", "\\n")), out var error)!;
            Assert.IsNull(error);
            player.Bind(file);
            return player;
        }

        static Image FigureOf(MemoryForgeMomentPlayer p) => p.transform.Find("MomentStage/MomentCamera/Figure").GetComponent<Image>();

        [Test]
        public void ParseReadsSnakeCaseFieldsAndRejectsMalformedFilesWithAPath()
        {
            var file = MomentFile.Parse(Fixture, out var error)!;
            Assert.IsNull(error);
            Assert.AreEqual(3, file.Scenes[0].Input.CooldownS);
            Assert.AreEqual(3, file.Scenes[1].Trigger.BondLevelMin);
            Assert.AreEqual(2, file.Scenes[0].Assets.Count);
            Assert.AreEqual("fig_a", file.RestFigure());
            var bad = new Dictionary<string, string>
            {
                { "format", Fixture.Replace("memory-forge-moments/1", "other/9") },
                { "character", Fixture.Replace(@"""character"": ""t_char""", @"""character"": null") },
                { "scenes[0].beats[0]", Fixture.Replace(@"""beats"": [
        {", @"""beats"": [ null,
        {") },
                { "overlays.o", Fixture.Replace(@"""o"": ""over""", @"""o"": 7") },
                { "a loop needs a positive duration", Fixture.Replace(@"""ease"": ""inOut""", @"""ease"": ""inOut"", ""loop"": true").Replace(@"{ ""t"": 0.2, ""y"": 30, ""rot"": 4 }", @"{ ""t"": 0 }") },
                { "out of order", Fixture.Replace(@"""t"": 0.2", @"""t"": -1") },
                { "numeric x/y", Fixture.Replace(@"""o"": ""over""", @"""o"": { ""image"": ""over"", ""x"": {} }") },
                { "assets[0]: null", Fixture.Replace(@"""assets"": [ {", @"""assets"": [ null, {") },
                { "camera.duration", Fixture.Replace(@"""move"": ""none"" }", @"""move"": ""push_in"", ""duration"": ""Infinity"" }") },
            };
            foreach (var c in bad)
            {
                Assert.IsNull(MomentFile.Parse(c.Value, out error), c.Key);
                StringAssert.Contains(c.Key, error, c.Key);
            }
            Assert.IsNull(MomentFile.Parse("{ not json", out error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void SampleInterpolatesWithEasing()
        {
            var keys = new List<MomentKey> { new MomentKey { T = 0, X = 0 }, new MomentKey { T = 1, X = 100 } };
            Assert.AreEqual(50, MemoryForgeMomentPlayer.Sample(keys, 0.5f, "linear").X, 0.01f);
            Assert.AreEqual(25, MemoryForgeMomentPlayer.Sample(keys, 0.5f, "in").X, 0.01f);
            Assert.AreEqual(100, MemoryForgeMomentPlayer.Sample(keys, 2f, "inOut").X, 0.01f);
        }

        [UnityTest]
        public IEnumerator PlaysBeatsPagesExactlyHoldsThenWaitsAndReturnsToRest()
        {
            var player = MakePlayer(out var file);
            yield return null;
            Assert.AreEqual("fig_a", player.CurrentFigure, "rest figure shown before playing");

            var pages = player.Pages(LongText());
            Assert.Greater(pages.Count, 1, "a long line is split into pages");
            Assert.AreEqual(LongText(), string.Concat(pages), "pages are exact slices: whitespace and line breaks kept");

            MomentScene? done = null; player.Finished += s => done = s;
            player.Play(file.Scenes[0]);
            yield return null;
            Assert.AreEqual("fig_b", player.CurrentFigure);
            Assert.AreEqual(2.6f, player.transform.Find("MomentStage/MomentCamera").localScale.x, 0.01f, "close shot frames the face");
            Assert.IsNotNull(player.transform.Find("MomentStage/MomentCamera/Figure/Overlay:o"));
            var backdrop = player.transform.Find("MomentStage/Backdrop").GetComponent<Image>();
            Assert.IsTrue(backdrop.enabled && backdrop.sprite == null, "a requested background that does not exist shows an empty stage");

            // Beat 0 has no line: in tap mode it waits for a tap after its (zero) hold.
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(player.WaitingForTap, "a tap-mode beat without dialogue waits for input");
            player.Tap(); yield return null; yield return null;

            var dialog = player.transform.Find("MomentDialog").gameObject;
            for (int page = 0; page < pages.Count; page++)
            {
                for (int i = 0; i < 3; i++) yield return null;
                Assert.IsTrue(dialog.activeSelf);
                player.Tap(); yield return null; // reveal the page
                player.Tap(); yield return null; // next page (the last page's tap ends the beat)
            }
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual("gone", player.CurrentFigure, "last beat is holding");
            Assert.IsFalse(FigureOf(player).enabled, "missing art is left empty, not substituted");
            Assert.IsNull(player.transform.Find("MomentStage/MomentCamera/Figure/Overlay:o"), "null overlay removes the slot");
            Assert.IsNull(done, "the authored hold is not cut short");
            for (float t = 0; t < 2f && !player.WaitingForTap; t += Time.deltaTime) yield return null;
            Assert.IsTrue(player.WaitingForTap);
            player.Tap();
            for (int i = 0; i < 3 && done == null; i++) yield return null;
            Assert.AreSame(file.Scenes[0], done);
            Assert.IsFalse(dialog.activeSelf);
            Assert.AreEqual("fig_a", player.CurrentFigure, "the stage returns to the rest figure");
            Assert.IsTrue(FigureOf(player).enabled);
            Assert.IsFalse(backdrop.enabled, "scene background is scene-owned");
        }

        [UnityTest]
        public IEnumerator SkipEndsTheSceneAndRestoresTheRestStage()
        {
            var player = MakePlayer(out var file);
            MomentScene? done = null; player.Finished += s => done = s;
            player.Play(file.Scenes[0]);
            yield return null;
            player.Skip();
            for (int i = 0; i < 5 && done == null; i++) yield return null;
            Assert.AreSame(file.Scenes[0], done);
            Assert.IsFalse(player.Playing);
            Assert.AreEqual(1f, player.transform.Find("MomentStage/MomentCamera").localScale.x, 0.01f);
            Assert.AreEqual("fig_a", player.CurrentFigure);
            Assert.IsNull(player.transform.Find("MomentStage/MomentCamera/Figure/Overlay:o"));
        }

        [UnityTest]
        public IEnumerator StopReportsTheInterruptedSceneFinished()
        {
            var player = MakePlayer(out var file);
            MomentScene? done = null; player.Finished += s => done = s;
            player.Play(file.Scenes[0]);
            yield return null;
            player.Stop();
            Assert.AreSame(file.Scenes[0], done);
            Assert.IsFalse(player.Playing);
        }

        [UnityTest]
        public IEnumerator AFailingBeatIsSkippedAndTheSceneFinishesOnce()
        {
            var player = MakePlayer(out var file, p => p == "t/b.png" ? throw new System.InvalidOperationException("broken art") : art.TryGetValue(p, out var s) ? s : null);
            int finished = 0; player.Finished += _ => finished++;
            player.Play(file.Scenes[0]);
            yield return null;
            StringAssert.Contains("broken art", player.LastError);
            player.Skip();
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(1, finished);
            Assert.IsFalse(player.Playing);
        }

        [UnityTest]
        public IEnumerator FinishedIsReportedEvenWhenTheRestPictureFailsToLoad()
        {
            int calls = 0;
            var player = MakePlayer(out var file, p => p == "t/a.png" && ++calls > 1 ? throw new System.InvalidOperationException("rest art gone") : art.TryGetValue(p, out var s) ? s : null);
            int finished = 0; player.Finished += _ => finished++;
            player.Play(file.Scenes[0]);
            yield return null;
            player.Skip();
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(1, finished, "skip still reports the scene finished");
            StringAssert.Contains("rest art gone", player.LastError);
            player.Play(file.Scenes[0]);
            yield return null;
            player.Stop();
            Assert.AreEqual(2, finished, "stop still reports the scene finished");
        }

        [UnityTest]
        public IEnumerator ReplacingAnOverlayKeepsAFinishedTracksOffset()
        {
            const string Moved = @"{ ""format"": ""memory-forge-moments/1"", ""character"": ""t_char"",
  ""images"": { ""fig_a"": ""t/a.png"", ""over"": ""t/o.png"" },
  ""motions"": { ""shift"": { ""tracks"": { ""o"": [ { ""t"": 0, ""x"": 0 }, { ""t"": 0.1, ""x"": 24 } ] } } },
  ""scenes"": [ { ""id"": ""t_lobby_03"", ""kind"": ""lobby"", ""input"": { ""advance"": ""auto"" }, ""beats"": [
    { ""figure"": ""fig_a"", ""overlays"": { ""o"": ""over"" }, ""motion"": ""shift"", ""hold"": 0.3 },
    { ""overlays"": { ""o"": { ""image"": ""over"", ""x"": 192, ""y"": 0 } }, ""hold"": 0.5 } ] } ] }";
            var player = MakePlayer(out _);
            var file = MomentFile.Parse(Moved, out var error)!;
            Assert.IsNull(error);
            player.Bind(file);
            player.Play(file.Scenes[0]);
            for (float t = 0; t < 0.5f; t += Time.deltaTime) yield return null;
            var slot = (RectTransform)player.transform.Find("MomentStage/MomentCamera/Figure/Overlay:o");
            float k = MemoryForgeMomentPlayer.RefWidth / MemoryForgeMomentPlayer.AuthoredWidth;
            Assert.AreEqual((192f + 24f) * k, slot.anchoredPosition.x, 0.5f, "new base plus the offset the finished track left");
            player.Stop();
        }

        [UnityTest]
        public IEnumerator ReaddingARemovedOverlayStartsFromItsNewBase()
        {
            const string Readd = @"{ ""format"": ""memory-forge-moments/1"", ""character"": ""t_char"",
  ""images"": { ""fig_a"": ""t/a.png"", ""over"": ""t/o.png"" },
  ""scenes"": [ { ""id"": ""t_lobby_04"", ""kind"": ""lobby"", ""input"": { ""advance"": ""auto"" }, ""beats"": [
    { ""figure"": ""fig_a"", ""overlays"": { ""o"": { ""image"": ""over"", ""x"": 192 } }, ""hold"": 0.05 },
    { ""overlays"": { ""o"": null }, ""hold"": 0.05 },
    { ""overlays"": { ""o"": { ""image"": ""over"", ""x"": 384 } }, ""hold"": 0.5 } ] } ] }";
            var player = MakePlayer(out _);
            var file = MomentFile.Parse(Readd, out var error)!;
            Assert.IsNull(error);
            player.Bind(file);
            player.Play(file.Scenes[0]);
            for (float t = 0; t < 0.3f; t += Time.deltaTime) yield return null;
            var slot = (RectTransform)player.transform.Find("MomentStage/MomentCamera/Figure/Overlay:o");
            Assert.AreEqual(384f * MemoryForgeMomentPlayer.RefWidth / MemoryForgeMomentPlayer.AuthoredWidth, slot.anchoredPosition.x, 0.5f);
            player.Stop();
        }

        [UnityTest]
        public IEnumerator ReducedMotionEndsTheSceneWithAnInstantCamera()
        {
            bool before = MemoryForgeUiMotion.Reduced;
            try
            {
                MemoryForgeUiMotion.Reduced = true;
                var player = MakePlayer(out var file);
                player.Play(file.Scenes[0]);
                yield return null;
                player.Skip();
                yield return null; yield return null;
                Assert.AreEqual(1f, player.transform.Find("MomentStage/MomentCamera").localScale.x, 0.001f);
                Assert.AreEqual(Vector2.zero, ((RectTransform)player.transform.Find("MomentStage/MomentCamera")).anchoredPosition);
            }
            finally { MemoryForgeUiMotion.Reduced = before; }
        }

        [UnityTest]
        public IEnumerator ReplacingAnOverlayMovesItsTrackToTheNewBase()
        {
            const string Moving = @"{ ""format"": ""memory-forge-moments/1"", ""character"": ""t_char"",
  ""images"": { ""fig_a"": ""t/a.png"", ""over"": ""t/o.png"" },
  ""motions"": { ""sway"": { ""tracks"": { ""o"": [ { ""t"": 0, ""x"": 0 }, { ""t"": 0.5, ""x"": 0 } ] }, ""loop"": true } },
  ""scenes"": [ { ""id"": ""t_lobby_02"", ""kind"": ""lobby"", ""input"": { ""advance"": ""auto"" }, ""beats"": [
    { ""figure"": ""fig_a"", ""overlays"": { ""o"": ""over"" }, ""motion"": ""sway"", ""hold"": 0.1 },
    { ""overlays"": { ""o"": { ""image"": ""over"", ""x"": 192, ""y"": 0 } }, ""hold"": 0.5 } ] } ] }";
            var player = MakePlayer(out _);
            var file = MomentFile.Parse(Moving, out var error)!;
            Assert.IsNull(error);
            player.Bind(file);
            player.Play(file.Scenes[0]);
            for (float t = 0; t < 0.35f; t += Time.deltaTime) yield return null;
            var slot = (RectTransform)player.transform.Find("MomentStage/MomentCamera/Figure/Overlay:o");
            Assert.AreEqual(192f * MemoryForgeMomentPlayer.RefWidth / MemoryForgeMomentPlayer.AuthoredWidth, slot.anchoredPosition.x, 0.5f, "the looping track follows the replacement's base");
            player.Stop();
        }

        [UnityTest]
        public IEnumerator LobbyMenuStaysHiddenThroughAQuickSkipAndReplay()
        {
            var host = new GameObject("Lobby"); hosts.Add(host);
            var lobby = host.AddComponent<MemoryForgeLobbyClient>();
            yield return null;
            var c = lobby.Companions.FirstOrDefault(x => x.Moments != null && x.Moments.Scenes.Count != 0);
            if (c == null) { Assert.Ignore("no imported moments in Resources"); yield break; }
            while (lobby.Current!.Id != c.Id) { lobby.Player!.Stop(); lobby.Show(lobby.Companions.ToList().IndexOf(c)); yield return null; }
            var scene = c.Moments!.Scenes[0];
            lobby.Play(scene); yield return null;
            lobby.Player!.Skip(); yield return null;  // finished: the menu starts fading back in
            lobby.Play(scene);                        // replay at once: the hide must win
            for (float t = 0; t < 0.6f; t += Time.deltaTime) yield return null;
            Assert.IsTrue(lobby.Player.Playing);
            Assert.AreEqual(0f, GameObject.Find("LobbySide").GetComponent<CanvasGroup>().alpha, 0.001f);
            lobby.Player.Stop();
        }

        [UnityTest]
        public IEnumerator LobbyListsCompanionsWithArtAndCyclesThem()
        {
            var host = new GameObject("Lobby"); hosts.Add(host);
            var lobby = host.AddComponent<MemoryForgeLobbyClient>();
            yield return null;
            Assert.IsEmpty(lobby.LoadErrors);
            Assert.IsNotEmpty(lobby.Companions, "characters with moments or a cut-in still appear");
            Assert.IsTrue(lobby.Companions.All(c => c.Moments != null || c.Still != null));
            Assert.IsNotNull(GameObject.Find("StartExpedition")?.GetComponent<Button>());
            lobby.Player!.Stop();
            var first = lobby.Current!.Id;
            GameObject.Find("NextCompanion").GetComponent<Button>().onClick.Invoke();
            yield return null;
            if (lobby.Companions.Count > 1) Assert.AreNotEqual(first, lobby.Current!.Id);
        }
    }
}
