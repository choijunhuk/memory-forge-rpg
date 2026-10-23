#nullable enable
#if UNITY_EDITOR && UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public class RosterArtPlayModeTests
    {
        static readonly string[] NewStills = { "yuri", "rien", "kaya" };
        static readonly string[] StillCharacters = { "nia", "yuri", "rien", "kaya" };
        static readonly string[] MomentCharacters = { "aila", "harin", "lumi", "ravien", "sera", "sion" };
        GameObject host = null!;
        MemoryForgeLobbyClient lobby = null!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.That(MemoryForgeAccountConnection.Current, Is.Null, "art acceptance uses an offline lobby and never changes an account");
            host = new GameObject("RosterArtAcceptance"); lobby = host.AddComponent<MemoryForgeLobbyClient>();
            yield return null; lobby.Player!.Stop(); yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (host != null) Object.Destroy(host);
            yield return null;
            Assert.That(MemoryForgeAccountConnection.Current, Is.Null, "viewing art cannot create an account connection");
        }

        [UnityTest]
        public IEnumerator AllTenCharactersResolveRealPortraitsAndKeepMomentAndNiaFallbacks()
        {
            var characters = ClientBattleContent.Load().Battle.Characters;
            Assert.That(characters.Count, Is.EqualTo(10)); Assert.That(lobby.LoadErrors, Is.Empty);
            CollectionAssert.AreEquivalent(characters.Select(c => c.Id), lobby.Companions.Select(c => c.Id), "all runtime characters can be selected at home");
            CollectionAssert.AreEquivalent(MomentCharacters, lobby.Companions.Where(c => c.Moments != null).Select(c => c.Id), "existing authored moments remain loaded");
            foreach (var character in characters)
            {
                var portrait = (Sprite?)Call(lobby, "Portrait", character.Id);
                Assert.That(portrait, Is.Not.Null, character.Id + " needs actual portrait art");
                Assert.That(portrait!.texture.width, Is.GreaterThan(32), character.Id + " portrait uses an imported image");
                var companion = lobby.Companions.Single(c => c.Id == character.Id);
                if (companion.Moments != null)
                {
                    var key = companion.Moments.RestFigure();
                    Assert.That(key, Is.Not.Null, character.Id + " keeps an authored rest figure");
                    var authored = (Sprite?)Call(lobby, "SpriteForArt", companion.Moments.Images[key!]);
                    Assert.That(portrait, Is.SameAs(authored), character.Id + " moment portrait has precedence over a still");
                }
                else
                {
                    string suffix = character.Id == "nia" ? "sd" : "cutin";
                    Assert.That(portrait.texture, Is.SameAs(Resources.Load<Texture2D>("Art/Characters/char_" + character.Id + "_" + suffix)), character.Id + " uses its own approved still");
                }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryRosterAndDetailPortraitCanBecomeTheActualHomeFigure()
        {
            foreach (var character in ClientBattleContent.Load().Battle.Characters)
            {
                Click("OpenCompanions"); yield return null;
                var tile = host.GetComponentsInChildren<Button>().Single(b => b.name == "Companion:" + character.Id);
                var portrait = (Sprite)Call(lobby, "Portrait", character.Id)!;
                Assert.That(tile.transform.Find("Portrait").GetComponent<Image>().sprite, Is.SameAs(portrait), character.Id + " roster draws its resolver result");
                tile.onClick.Invoke(); yield return null;
                var detail = host.GetComponentsInChildren<Image>().Single(i => i.name == "CharacterArt");
                Assert.That(detail.sprite, Is.SameAs(portrait), character.Id + " detail draws its own portrait");
                if (StillCharacters.Contains(character.Id))
                {
                    yield return OptionalShot(character.Id + "-detail");
                    AssertStillAspect(detail, character.Id + " detail");
                }
                Click("UseAsLobby"); yield return null; lobby.Player!.Stop(); yield return null;
                Assert.That(lobby.Current!.Id, Is.EqualTo(character.Id), character.Id + " home selection remains enabled");
                Assert.That(lobby.Player.FigureImage.enabled, Is.True);
                Assert.That(lobby.Player.FigureImage.sprite, Is.SameAs(portrait), character.Id + " actual home figure preserves its portrait");
                Assert.That(lobby.Player.FigureImage.color.a, Is.GreaterThan(0));
                if (StillCharacters.Contains(character.Id))
                {
                    yield return OptionalShot(character.Id + "-home");
                    AssertStillAspect(lobby.Player.FigureImage, character.Id + " home");
                }
            }
        }

        [UnityTest]
        public IEnumerator NewCharacterRecruitmentArrivalUsesDedicatedArtWithoutARecruitmentCommand()
        {
            var constructor = typeof(RecruitmentPull).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(JObject) }, null);
            Assert.That(constructor, Is.Not.Null);
            var reveal = host.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == "RecruitmentReveal");
            foreach (string id in StillCharacters)
            {
                // Detached presentation data exercises the real arrival renderer without a receipt, ownership or request.
                var pull = (RecruitmentPull)constructor!.Invoke(new object[] { new JObject {
                    ["rarity"] = "SSR", ["kind"] = "char", ["id"] = id, ["featured"] = false, ["lost"] = false, ["pityAt"] = 1,
                    ["isNew"] = true, ["resonance"] = 0, ["refine"] = 0, ["shards"] = 0, ["afterglow"] = 0
                } });
                Call(lobby, "ClearPanel", reveal);
                try
                {
                    var arrival = (RectTransform)Call(lobby, "RecruitmentArrival", pull)!;
                    Call(lobby, "ShowPanel", reveal); yield return null;
                    var image = arrival.GetComponent<Image>();
                    Assert.That(image.sprite, Is.Not.Null, id + " recruitment must draw character art instead of the preparation placeholder");
                    string suffix = id == "nia" ? "sd" : "cutin";
                    Assert.That(image.sprite.texture, Is.SameAs(Resources.Load<Texture2D>("Art/Characters/char_" + id + "_" + suffix)));
                    AssertStillAspect(image, id + " recruitment arrival");
                    Assert.That(image.GetComponentsInChildren<Text>().Any(t => t.text.Contains("준비 중")), Is.False);
                    Assert.That(lobby.AccountProgression, Is.Null, "rendering a detached arrival does not grant a character");
                }
                finally { Call(lobby, "ClearPanel", reveal); reveal.gameObject.SetActive(false); }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator NewCharacterBattleAndGrowthResolversLoadTheirDedicatedSdAndCutinTextures()
        {
            var renderers = new GameObject("RosterArtResolvers"); renderers.SetActive(false);
            try
            {
                var battle = renderers.AddComponent<MemoryForgeExpeditionClient>();
                var growth = renderers.AddComponent<MemoryForgeGrowthClient>();
                var distinct = new HashSet<Texture2D>();
                foreach (string id in NewStills)
                    foreach (string suffix in new[] { "sd", "cutin" })
                    {
                        string path = "Characters/char_" + id + "_" + suffix;
                        var texture = Resources.Load<Texture2D>("Art/" + path);
                        Assert.That(texture, Is.Not.Null, id + " must ship its dedicated " + suffix + " resource");
                        Assert.That(texture!.width, Is.GreaterThan(32));
                        Assert.That(distinct.Add(texture), Is.True, "each resource key resolves its dedicated imported texture");
                        var battleSprite = (Sprite?)Call(battle, "Art", path, true);
                        var growthSprite = (Sprite?)Call(growth, "SpriteAt", path);
                        Assert.That(battleSprite, Is.Not.Null, path + " battle art resolver");
                        Assert.That(growthSprite, Is.Not.Null, path + " growth art resolver");
                        Assert.That(battleSprite!.texture, Is.SameAs(texture)); Assert.That(growthSprite!.texture, Is.SameAs(texture));
                    }
                Assert.That(battle.Snapshot, Is.Null, "resource resolution does not start a practice run");
                Assert.That(growth.Snapshot, Is.Null, "resource resolution does not create an encounter");
            }
            finally { Object.Destroy(renderers); }
            yield return null;
        }

        static void AssertStillAspect(Image image, string scope)
        {
            Assert.That(image.sprite, Is.Not.Null, scope + " has actual art");
            Assert.That(image.preserveAspect, Is.True, scope + " preserves its source aspect");
            Assert.That(image.type, Is.EqualTo(Image.Type.Simple), scope + " draws an unsliced character image");
            Canvas.ForceUpdateCanvases();
            var mesh = image.canvasRenderer.GetMesh();
            Assert.That(mesh, Is.Not.Null, scope + " has rendered geometry");
            var vertices = mesh.vertices;
            Assert.That(vertices.Length, Is.GreaterThanOrEqualTo(4), scope + " cannot pass with an empty figure");
            var triangles = mesh.triangles;
            var uv = mesh.uv;
            Assert.That(uv.Length, Is.EqualTo(vertices.Length), scope + " every image vertex samples its source texture");
            Assert.That(triangles.Length, Is.GreaterThanOrEqualTo(6), scope + " has an actual image face");
            Assert.That(triangles.Length % 6, Is.EqualTo(0), scope + " simple image faces consist of triangle pairs");
            float sourceAspect = image.sprite.rect.width / image.sprite.rect.height;
            var texture = image.sprite.texture;
            var camera = image.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : image.canvas.worldCamera;
            for (int start = 0; start + 5 < triangles.Length; start += 6)
            {
                // Triangle indices cover both shared-vertex images and Shadow/Outline's expanded vertex stream.
                var indices = triangles.Skip(start).Take(6).ToArray();
                var quad = indices.Select(i => RectTransformUtility.WorldToScreenPoint(camera, image.rectTransform.TransformPoint(vertices[i]))).ToArray();
                float width = quad.Max(v => v.x) - quad.Min(v => v.x), height = quad.Max(v => v.y) - quad.Min(v => v.y);
                Assert.That(height, Is.GreaterThan(0), scope + " drawn quad has height");
                float texelWidth = (indices.Max(i => uv[i].x) - indices.Min(i => uv[i].x)) * texture.width;
                float texelHeight = (indices.Max(i => uv[i].y) - indices.Min(i => uv[i].y)) * texture.height;
                Assert.That(texelWidth, Is.GreaterThan(0)); Assert.That(texelHeight, Is.GreaterThan(0));
                // uGUI trims transparent sprite padding; compare sampled texels to physical pixels, allowing one rounded pixel per dimension.
                float aspect = texelWidth / texelHeight, tolerance = (1 + aspect) / height;
                string geometry = scope + " quad=" + start / 6 + " drawnPx=" + width + "x" + height + " sourceAspect=" + sourceAspect
                    + " sampledTexels=" + texelWidth + "x" + texelHeight + " padding=" + UnityEngine.Sprites.DataUtility.GetPadding(image.sprite);
                if (start == 0) Debug.Log("[RosterArtAspect] " + geometry);
                Assert.That(width / height, Is.EqualTo(aspect).Within(tolerance), geometry + " keeps equal physical scale in both source axes");
            }
        }

        static object? Call(object receiver, string name, params object[] args)
        {
            var method = receiver.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, receiver.GetType().Name + "." + name);
            return method!.Invoke(receiver, args);
        }

        void Click(string name)
        {
            var button = host.GetComponentsInChildren<Button>().Single(b => b.name == name);
            Assert.That(button.IsInteractable(), Is.True, name); button.onClick.Invoke();
        }

        IEnumerator OptionalShot(string state)
        {
            string? directory = Environment.GetEnvironmentVariable("MF_MOBILE_VIEWPORT_SHOTS");
            if (string.IsNullOrWhiteSpace(directory)) yield break;
            Directory.CreateDirectory(directory!);
            yield return LobbyMomentCapturePlayModeTests.Shot(host, Path.Combine(directory!, "roster-art-" + state + ".jpg"));
        }
    }
}
#endif
