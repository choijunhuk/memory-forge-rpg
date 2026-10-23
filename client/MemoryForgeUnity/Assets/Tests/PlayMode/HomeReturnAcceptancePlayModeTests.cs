#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public class HomeReturnAcceptancePlayModeTests
    {
        readonly List<GameObject> hosts = new List<GameObject>();
        readonly List<Scene> scenes = new List<Scene>();

        [SetUp]
        public void Reset() { MemoryForgePracticeSession.ResetForTests(); MemoryForgeLobbyClient.NextLength = null; }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            foreach (var host in hosts) if (host != null) Object.Destroy(host);
            foreach (var scene in scenes) if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            scenes.Clear();
            hosts.Clear(); MemoryForgePracticeSession.ResetForTests(); MemoryForgeLobbyClient.NextLength = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator HomeHandoffShowsTheActualReturnOnceAndPreselectsTheHeldLengthWithoutDeparting()
        {
            var source = Host("HomeHandoffSource"); source.SetActive(false);
            var client = source.AddComponent<MemoryForgeExpeditionClient>(); client.Seed = 1234; client.Length = "full";
            var requests = new List<string>(); client.SceneLoader = requests.Add;
            source.SetActive(true); yield return null;
            Assert.That(client.Abandon(), Is.True, client.LastError);
            Assert.That(client.ReturnToWorkshop(), Is.True, client.LastError);
            Assert.That(requests, Is.EqualTo(new[] { MemoryForgeExpeditionClient.LobbyScene }));
            Assert.That(MemoryForgePracticeSession.HasHeldPractice, Is.True);
            Assert.That(MemoryForgePracticeSession.HeldLength, Is.EqualTo("full"));
            Assert.That(MemoryForgePracticeSession.HeldMemoryVersion, Is.EqualTo(0));
            Object.Destroy(source); yield return null;

            var home = Host("HomeHandoffFirstLobby"); var lobby = home.AddComponent<MemoryForgeLobbyClient>();
            yield return null; yield return null;
            Assert.That(lobby.Player!.Playing, Is.False, "a return must not automatically replay the arrival scene");
            var notices = home.GetComponentsInChildren<Text>().Where(text => text.name == "HomeReturnNotice").ToArray();
            Assert.That(notices.Length, Is.EqualTo(1));
            Assert.That(notices.Single().text, Is.EqualTo("탐사 포기 · 기억 덱은 바뀌지 않았어요."));
            var noticeBack = notices.Single().transform.parent.GetComponent<Image>();
            Assert.That(noticeBack, Is.Not.Null, "the return notice has a readable background over the workshop lamp");
            Assert.That(noticeBack.color.a, Is.GreaterThanOrEqualTo(0.9f));
            Assert.That(noticeBack.raycastTarget, Is.False, "the status cannot intercept home input");
            Assert.That(MemoryForgePracticeSession.HasHeldPractice, Is.True, "opening the home never consumes the hand-off");
            Assert.That(MemoryForgeLobbyClient.NextLength, Is.Null, "opening the home never queues another departure");
            var open = home.GetComponentsInChildren<Button>().Single(button => button.name == "StartExpedition");
            Assert.That(open.IsInteractable(), Is.True); open.onClick.Invoke(); yield return null;
            var shortButton = home.GetComponentsInChildren<Button>().Single(button => button.name == "Length:short");
            var fullButton = home.GetComponentsInChildren<Button>().Single(button => button.name == "Length:full");
            var fullSelected = fullButton.GetComponent<Image>().sprite;
            Assert.That(fullSelected, Is.Not.Null);
            Assert.That(fullSelected.texture, Is.SameAs(Resources.Load<Texture2D>("Art/UI/battle/ui_button_pill_brass")), "the held full length must be visibly selected before any tap");
            Assert.That(shortButton.GetComponent<Image>().sprite, Is.Not.EqualTo(fullSelected));
            Assert.That(MemoryForgePracticeSession.HasHeldPractice, Is.True);
            Assert.That(MemoryForgeLobbyClient.NextLength, Is.Null);
            Object.Destroy(home); yield return null;

            var second = Host("HomeHandoffSecondLobby"); var nextLobby = second.AddComponent<MemoryForgeLobbyClient>();
            yield return null; yield return null;
            Assert.That(second.GetComponentsInChildren<Text>().Any(text => text.name == "HomeReturnNotice"), Is.False, "the actual return notice is consumed exactly once");
            Assert.That(nextLobby.Player!.Playing, Is.False);
            Assert.That(MemoryForgePracticeSession.HasHeldPractice, Is.True);
            Assert.That(MemoryForgePracticeSession.HeldMemoryVersion, Is.EqualTo(0));
            Assert.That(MemoryForgeLobbyClient.NextLength, Is.Null);
        }

        [UnityTest, Timeout(300000), Explicit("loads the shipped home and expedition scenes; writes MF_HOME_RETURN_SHOTS")]
        public IEnumerator ActualHomeAndExpeditionScenesPreserveSavedMemoryAndRequireEveryDeparture()
        {
            string? output = Environment.GetEnvironmentVariable("MF_HOME_RETURN_SHOTS");
            if (string.IsNullOrEmpty(output)) { Assert.Ignore("MF_HOME_RETURN_SHOTS is not set"); yield break; }
            Directory.CreateDirectory(output!);
            var requests = new List<string>(); AsyncOperation? pending = null;
            void Route(string destination) { requests.Add(destination); pending = SceneManager.LoadSceneAsync(destination, LoadSceneMode.Additive); }
            IEnumerator Navigate(Scene previous, string expected)
            {
                Assert.That(requests.Last(), Is.EqualTo(expected));
                yield return Until(() => pending != null && pending.isDone, "the actual " + expected + " scene loads");
                var loaded = SceneManager.GetSceneByName(expected); Assert.That(loaded.isLoaded, Is.True); scenes.Add(loaded);
                if (previous.IsValid() && previous.isLoaded) yield return SceneManager.UnloadSceneAsync(previous);
                pending = null; yield return null;
            }
            yield return SceneManager.LoadSceneAsync(MemoryForgeExpeditionClient.LobbyScene, LoadSceneMode.Additive);
            var homeScene = SceneManager.GetSceneByName(MemoryForgeExpeditionClient.LobbyScene); scenes.Add(homeScene);
            var lobby = SceneClient<MemoryForgeLobbyClient>(homeScene); lobby.SceneLoader = Route;
            yield return null; lobby.Player!.Stop(); yield return Settle();
            lobby.Show(lobby.Companions.ToList().FindIndex(companion => companion.Id == "harin"));
            yield return null; lobby.Player.Stop(); yield return Settle();
            Assert.That(lobby.Current!.Id, Is.EqualTo("harin"));
            yield return LobbyMomentCapturePlayModeTests.Shot(lobby.gameObject, Path.Combine(output!, "actual-home-initial.jpg"));
            Click(lobby.gameObject, "StartExpedition"); Click(lobby.gameObject, "Length:short");
            yield return LobbyMomentCapturePlayModeTests.Shot(lobby.gameObject, Path.Combine(output!, "actual-home-preparation.jpg"));
            var depart = Named(lobby.gameObject, "Depart"); Click(depart); depart.onClick.Invoke();
            yield return Until(() => pending != null, "the explicit home departure requests its scene");
            Assert.That(requests.Count, Is.EqualTo(1), "a repeated departure tap loads one scene");
            yield return Navigate(homeScene, MemoryForgeLobbyClient.ExpeditionScene);
            var expeditionScene = SceneManager.GetSceneByName(MemoryForgeLobbyClient.ExpeditionScene);
            var client = SceneClient<MemoryForgeExpeditionClient>(expeditionScene); client.SceneLoader = Route;
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("MAP")); Assert.That(client.SessionMemoryVersion, Is.Zero);
            Assert.That(client.Snapshot.RunId, Is.EqualTo("practice-1-" + client.Seed));
            yield return ShotExpedition(client.gameObject, Path.Combine(output!, "actual-expedition-map.png"));
            Assert.That(client.EnterNode(client.Snapshot.Map!.Floors[0][0].Id), Is.True, client.LastError);
            yield return ShotExpedition(client.gameObject, Path.Combine(output!, "actual-expedition-battle.png"));
            for (int ends = 0; ends < 600 && client.Snapshot!.Phase == "BATTLE"; ends++)
            {
                Assert.That(client.EndTurn(), Is.True, client.LastError);
                Assert.That(client.SkipTurnSummary() && client.ContinueTurnSummary(), Is.True);
                if (ends % 8 == 7) yield return null;
            }
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("DEFEAT"));
            Assert.That(client.CommitMemory(), Is.True, client.LastError); Assert.That(client.SessionMemoryVersion, Is.EqualTo(1));
            var saved = JToken.FromObject(client.SessionMemory);
            yield return ShotExpedition(client.gameObject, Path.Combine(output!, "actual-expedition-result-saved.png"));
            var returnHome = Named(client.gameObject, "ReturnHome"); Click(returnHome); returnHome.onClick.Invoke();
            Assert.That(requests.Count, Is.EqualTo(2), "repeated result return opens one home");
            yield return Navigate(expeditionScene, MemoryForgeExpeditionClient.LobbyScene);
            homeScene = SceneManager.GetSceneByName(MemoryForgeExpeditionClient.LobbyScene);
            lobby = SceneClient<MemoryForgeLobbyClient>(homeScene); lobby.SceneLoader = Route;
            yield return Settle();
            AssertStableHome(lobby, "패배");
            yield return LobbyMomentCapturePlayModeTests.Shot(lobby.gameObject, Path.Combine(output!, "actual-home-saved-return-notice.jpg"));
            Click(lobby.gameObject, "StartExpedition"); Click(lobby.gameObject, "Depart");
            yield return Until(() => pending != null, "the second departure is explicit");
            Assert.That(requests.Count, Is.EqualTo(3));
            yield return Navigate(homeScene, MemoryForgeLobbyClient.ExpeditionScene);
            expeditionScene = SceneManager.GetSceneByName(MemoryForgeLobbyClient.ExpeditionScene);
            client = SceneClient<MemoryForgeExpeditionClient>(expeditionScene); client.SceneLoader = Route;
            Assert.That(client.Snapshot!.RunId, Is.EqualTo("practice-2-" + (client.Seed + 1)));
            Assert.That(client.SessionMemoryVersion, Is.EqualTo(1)); Assert.That(JToken.DeepEquals(saved, JToken.FromObject(client.SessionMemory)), Is.True);
            Assert.That(MemoryForgePracticeSession.HasHeldPractice, Is.False); Assert.That(MemoryForgeLobbyClient.NextLength, Is.Null);
            Click(client.gameObject, "AbandonButton");
            yield return ShotExpedition(client.gameObject, Path.Combine(output!, "actual-expedition-abandon-confirmation.png"));
            var confirm = Named(client.gameObject, "ConfirmAbandon"); Click(confirm); confirm.onClick.Invoke();
            Assert.That(requests.Count, Is.EqualTo(4), "repeated abandon confirmation returns once");
            yield return Navigate(expeditionScene, MemoryForgeExpeditionClient.LobbyScene);
            homeScene = SceneManager.GetSceneByName(MemoryForgeExpeditionClient.LobbyScene); lobby = SceneClient<MemoryForgeLobbyClient>(homeScene);
            yield return Settle(); AssertStableHome(lobby, "탐사 포기");
            yield return LobbyMomentCapturePlayModeTests.Shot(lobby.gameObject, Path.Combine(output!, "actual-home-abandon-return-notice.jpg"));
            yield return Settle(); Assert.That(requests.Count, Is.EqualTo(4), "the returned home never automatically loads another expedition");
            File.WriteAllText(Path.Combine(output!, "actual-scenes-acceptance.json"), new JObject { ["evidence"] = "shipped scenes loaded with SceneManager.LoadSceneAsync(Additive), actual uGUI Button.onClick and public Run commands",
                ["sceneRequests"] = new JArray(requests), ["savedMemoryVersion"] = 1, ["heldMemoryVersionAfterAbandon"] = MemoryForgePracticeSession.HeldMemoryVersion,
                ["doubleDepartureCreatedOneRun"] = true, ["doubleResultReturnLoadedOneHome"] = true, ["doubleAbandonLoadedOneHome"] = true, ["automaticDepartureAfterReturn"] = false }.ToString());
        }

        static void AssertStableHome(MemoryForgeLobbyClient lobby, string outcome)
        {
            Assert.That(lobby.Player!.Playing, Is.False);
            Assert.That(lobby.Current!.Id, Is.EqualTo("harin"), "the actual scene return preserves the selected representative");
            Assert.That(lobby.GetComponentsInChildren<Text>().Single(text => text.name == "HomeReturnNotice").text, Does.Contain(outcome));
            Assert.That(MemoryForgePracticeSession.HasHeldPractice, Is.True); Assert.That(MemoryForgePracticeSession.HeldMemoryVersion, Is.EqualTo(1));
            Assert.That(MemoryForgeLobbyClient.NextLength, Is.Null);
            Assert.That(lobby.GetComponentsInChildren<Button>().Any(button => button.name == "StartExpedition" && button.IsInteractable()), Is.True);
        }

        static T SceneClient<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();
        static Button Named(GameObject host, string name) => host.GetComponentsInChildren<Button>().Single(button => button.name == name);
        static void Click(GameObject host, string name) => Click(Named(host, name));
        static void Click(Button button) { Assert.That(button.IsInteractable(), Is.True, button.name); button.onClick.Invoke(); }
        static IEnumerator Settle() { for (float elapsed = 0; elapsed < 0.5f; elapsed += Time.unscaledDeltaTime) yield return null; }
        static IEnumerator Until(Func<bool> ready, string step)
        {
            float until = Time.realtimeSinceStartup + 30;
            while (!ready() && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(ready(), Is.True, step);
        }

        static IEnumerator ShotExpedition(GameObject host, string path)
        {
            var texture = new RenderTexture(1600, 900, 24); var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var camera = new GameObject("ActualReturnShotCamera").AddComponent<Camera>();
            camera.targetTexture = texture; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.orthographic = true;
            Canvas? canvas = null; var previous = RenderTexture.active;
            try
            {
                canvas = host.GetComponentsInChildren<Canvas>().Single(item => item.name == MemoryForgeExpeditionClient.OwnedCanvas);
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
                yield return null; yield return null; yield return null;
                canvas = host.GetComponentsInChildren<Canvas>().Single(item => item.name == MemoryForgeExpeditionClient.OwnedCanvas);
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                if (canvas != null) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
                Object.Destroy(camera.gameObject); Object.Destroy(image); texture.Release(); Object.Destroy(texture);
            }
            yield return null; yield return null; yield return null;
        }

        GameObject Host(string name) { var host = new GameObject(name); hosts.Add(host); return host; }
    }
}
