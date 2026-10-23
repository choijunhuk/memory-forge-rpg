#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    // Report screenshots of the imported moment scenes as the lobby plays them (reports/lobby_moments_20261018), for Codex's visual
    // review. Run by name with Temp/moment_shots.txt: its first line is the output folder, an optional second line the character ids
    // (space separated; all companions with moments when absent). Each scene is shot when it starts and at every tap wait, then tapped
    // on, up to 60 shots or 120 seconds a scene. Nothing is changed: the scenes play through the lobby's own Play.
    public class LobbyMomentCapturePlayModeTests
    {
        [UnityTest, Timeout(1800000), Explicit("writes report screenshots; run by name with Temp/moment_shots.txt")]
        public IEnumerator CaptureMomentScenes()
        {
            string named = Path.Combine(Application.dataPath, "../Temp/moment_shots.txt");
            if (!File.Exists(named)) { Assert.Ignore("Temp/moment_shots.txt is not set"); yield break; }
            var lines = File.ReadAllLines(named).Select(l => l.Trim()).Where(l => l.Length != 0).ToList();
            string dir = lines[0]; Directory.CreateDirectory(dir);
            var only = lines.Count > 1 ? new HashSet<string>(lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)) : null;
            var host = new GameObject("LobbyShots"); var lobby = host.AddComponent<MemoryForgeLobbyClient>();
            yield return null;
            var log = new List<string>();
            try
            {
                foreach (var c in lobby.Companions.Where(x => x.Moments != null && (only == null || only.Contains(x.Id))).ToList())
                {
                    lobby.Player!.Stop(); lobby.Show(lobby.Companions.ToList().IndexOf(c)); yield return null; lobby.Player.Stop(); yield return null;
                    foreach (var scene in c.Moments!.Scenes)
                    {
                        lobby.Play(scene); int shot = 0; float started = Time.realtimeSinceStartup;
                        yield return Settle(0.4f);
                        yield return Shot(host, Path.Combine(dir, scene.Id + "_" + (shot++).ToString("00") + ".jpg"));
                        while (lobby.Player.Playing && shot < 60 && Time.realtimeSinceStartup - started < 120)
                        {
                            if (lobby.Player.WaitingForTap)
                            {
                                yield return Shot(host, Path.Combine(dir, scene.Id + "_" + (shot++).ToString("00") + ".jpg"));
                                lobby.Player.Tap(); yield return Settle(0.5f);
                            }
                            else yield return null;
                        }
                        log.Add(c.Id + " " + scene.Id + " " + shot + " shots" + (lobby.Player.Playing ? " (cut at the cap)" : ""));
                        lobby.Player.Stop(); yield return null;
                    }
                }
            }
            finally { try { File.WriteAllLines(Path.Combine(dir, "captured.txt"), log); } finally { Object.Destroy(host); } }
        }

        // NOW-PLAY-RULES-20261019 report shots: the home with no moment playing, then the sortie panel opened by its real button.
        [UnityTest, Explicit("writes report screenshots; run by name with MF_EXPEDITION_SHOTS")]
        public IEnumerator CaptureHomeCatalogueAndDetails()
        {
            string? dir = Environment.GetEnvironmentVariable("MF_EXPEDITION_SHOTS");
            if (string.IsNullOrEmpty(dir)) { Assert.Ignore("MF_EXPEDITION_SHOTS is not set"); yield break; }
            Directory.CreateDirectory(dir!);
            var host = new GameObject("LobbyShots"); var lobby = host.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); yield return Settle(0.5f);
                yield return Shot(host, Path.Combine(dir!, "home.jpg"));
                host.GetComponentsInChildren<Button>().Single(b => b.name == "OpenCompanions").onClick.Invoke();
                yield return Settle(0.3f); yield return Shot(host, Path.Combine(dir!, "companions.jpg"));
                host.GetComponentsInChildren<Button>().Single(b => b.name == "Companion:sera").onClick.Invoke();
                yield return Settle(0.3f); yield return Shot(host, Path.Combine(dir!, "companion-skills.jpg"));
                host.GetComponentsInChildren<Button>().Single(b => b.name == "ShowCompanionCards").onClick.Invoke();
                yield return Settle(0.3f); yield return Shot(host, Path.Combine(dir!, "companion-cards.jpg"));
            }
            finally { Object.Destroy(host); }
        }

        [UnityTest, Explicit("writes report screenshots; run by name with MF_EXPEDITION_SHOTS")]
        public IEnumerator CaptureHomeAndSortie()
        {
            string? dir = Environment.GetEnvironmentVariable("MF_EXPEDITION_SHOTS");
            if (string.IsNullOrEmpty(dir)) { Assert.Ignore("MF_EXPEDITION_SHOTS is not set"); yield break; }
            Directory.CreateDirectory(dir!);
            var host = new GameObject("LobbyShots"); var lobby = host.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); yield return Settle(0.4f);
                yield return Shot(host, Path.Combine(dir!, "home.jpg"));
                host.GetComponentsInChildren<Button>().First(b => b.name == "StartExpedition").onClick.Invoke();
                yield return Settle(0.3f);
                yield return Shot(host, Path.Combine(dir!, "home_sortie.jpg"));
            }
            finally { Object.Destroy(host); }
            // The same home at a 144-unit touch size: the menu rows reflow (and scroll if needed) above the note and footer.
            MemoryForgeExpeditionClient.TouchOverride = 144;
            var big = new GameObject("LobbyShotsTouch"); var bigLobby = big.AddComponent<MemoryForgeLobbyClient>();
            try { yield return null; bigLobby.Player!.Stop(); yield return Settle(0.4f); yield return Shot(big, Path.Combine(dir!, "home_touch144.jpg")); }
            finally { MemoryForgeExpeditionClient.TouchOverride = null; Object.Destroy(big); }
        }

        static IEnumerator Settle(float seconds)
        {
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime) yield return null;
        }
        // Historical reference-art capture only. Its normalized authored frame is not phone/safe-area evidence.
        internal static IEnumerator Shot(GameObject host, string path, string canvasName = MemoryForgeLobbyClient.OwnedCanvas)
        {
            var texture = new RenderTexture(1600, 900, 24);
            var camera = new GameObject("LobbyShotCamera").AddComponent<Camera>();
            camera.targetTexture = texture; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.orthographic = true;
            var canvas = host.GetComponentsInChildren<Canvas>().First(x => x.name == canvasName);
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false); var before = RenderTexture.active;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
                yield return null;
                Canvas.ForceUpdateCanvases();
                // Capture the authored frame; Screen.safeArea still describes the Editor Game view, not this render target.
                var frame = host.GetComponentsInChildren<RectTransform>().FirstOrDefault(r => r.name == "LobbyFrame" || r.name == "ServerPracticeFrame" || r.name == "GrowthFrame");
                if (frame != null) { frame.localScale = Vector3.one; frame.anchoredPosition = Vector2.zero; }
                camera.Render();
                RenderTexture.active = texture; image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToJPG(86));
            }
            finally
            {
                // A failed write leaves no camera, texture or camera-drawn canvas behind.
                RenderTexture.active = before;
                if (canvas != null) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
                Object.Destroy(camera.gameObject); Object.Destroy(image); texture.Release(); Object.Destroy(texture);
            }
            yield return null;
        }

        // Viewport acceptance capture: caller supplies production viewport calibration before building the UI.
        // The production Update/Fit path owns every frame transform; this overload never normalizes one.
        internal static IEnumerator Shot(GameObject host, string path, int pixelWidth, int pixelHeight,
            string canvasName = MemoryForgeLobbyClient.OwnedCanvas, Action<Canvas, Camera>? beforeCapture = null)
        {
            Assert.That(pixelWidth, Is.GreaterThan(0)); Assert.That(pixelHeight, Is.GreaterThan(0));
            var canvas = host.GetComponentsInChildren<Canvas>().First(x => x.name == canvasName);
            var previousMode = canvas.renderMode; var previousCamera = canvas.worldCamera;
            float previousPlane = canvas.planeDistance;
            var texture = new RenderTexture(pixelWidth, pixelHeight, 24);
            var camera = new GameObject("ViewportAcceptanceCamera").AddComponent<Camera>();
            camera.targetTexture = texture; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black; camera.orthographic = true;
            var image = new Texture2D(pixelWidth, pixelHeight, TextureFormat.RGB24, false);
            var previousTarget = RenderTexture.active;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
                yield return null; yield return null; yield return null;
                Canvas.ForceUpdateCanvases(); beforeCapture?.Invoke(canvas, camera); camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, pixelWidth, pixelHeight), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToJPG(90));
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (canvas != null)
                { canvas.renderMode = previousMode; canvas.worldCamera = previousCamera; canvas.planeDistance = previousPlane; }
                camera.targetTexture = null;
                Object.Destroy(camera.gameObject); Object.Destroy(image); texture.Release(); Object.Destroy(texture);
            }
            yield return null;
        }
    }
}
