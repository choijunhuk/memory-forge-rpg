#nullable enable
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public class LobbyOverviewPlayModeTests
    {
        GameObject host = null!;
        MemoryForgeLobbyClient lobby = null!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            host = new GameObject("LobbyOverviewAcceptance"); lobby = host.AddComponent<MemoryForgeLobbyClient>();
            yield return null; lobby.Player!.Stop();
            for (float elapsed = 0; elapsed < 0.5f; elapsed += Time.unscaledDeltaTime) yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MemoryForgeExpeditionClient.TouchOverride = null;
            if (host != null) Object.Destroy(host);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HomeOverviewShowsTheActualPracticePartyAndWorkingDestinations()
        {
            var content = ClientBattleContent.Load();
            var cards = host.GetComponentsInChildren<RectTransform>().Where(item => item.name.StartsWith("PreviewParty:")).ToArray();
            CollectionAssert.AreEquivalent(MemoryForgeExpeditionClient.ReferenceParty.Select(id => "PreviewParty:" + id), cards.Select(item => item.name));
            foreach (var card in cards)
            {
                string id = card.name.Substring("PreviewParty:".Length);
                Assert.That(TextOf(card), Does.Contain(content.Battle.Characters.Single(character => character.Id == id).Name));
                Assert.That(TextOf(card), Does.Not.Contain("보유"), "the practice preview must not imply account ownership");
            }
            Click("HomeCatalogueShortcut"); yield return null;
            Assert.That(Panel("RosterPanel").gameObject.activeInHierarchy, Is.True);
            Click("CloseRoster"); yield return null;
            Click("HomeRecruitmentShortcut"); yield return null;
            Assert.That(Panel("RecruitmentPanel").gameObject.activeInHierarchy, Is.True);
            Assert.That(host.GetComponentsInChildren<InputField>().Single(field => field.name == "RecruitmentCredential").text, Is.Empty);
        }

        [UnityTest]
        public IEnumerator RosterPreviewUsesRealSkillsAndBothDetailEntrypointsStillWork()
        {
            var definition = ClientBattleContent.Load().Battle.Characters.First();
            Click("OpenCompanions"); yield return null;
            var preview = Panel("RosterPreview");
            Assert.That(TextOf(preview), Does.Contain(definition.Name));
            Assert.That(TextOf(preview), Does.Contain(definition.Passive.Name));
            Assert.That(TextOf(preview), Does.Contain(definition.Ult.Name));
            Click("PreviewCompanionDetails"); yield return null;
            Assert.That(Panel("CompanionDetails").gameObject.activeInHierarchy, Is.True);
            Assert.That(TextOf(Panel("CompanionDetails")), Does.Contain(definition.Passive.Name));
            Click("BackToRoster"); yield return null;
            Click("Companion:" + definition.Id); yield return null;
            Assert.That(Panel("CompanionDetails").gameObject.activeInHierarchy, Is.True);
            Assert.That(TextOf(Panel("CompanionDetails")), Does.Contain(definition.Ult.Name));
            Click("BackToRoster"); yield return null;
            Click("Companion:nia"); yield return null;
            host.SendMessage("BackToRoster"); yield return null; // Escape uses the same back action as the visible button.
            Assert.That(Panel("RosterPanel").gameObject.activeInHierarchy, Is.True);
            Assert.That(Panel("CompanionDetails").gameObject.activeSelf, Is.False);
            Assert.That(TextOf(Panel("RosterPreview")), Does.Contain("니아"));
        }

        [UnityTest]
        public IEnumerator OwnedEmptyStateStillConnectsWithoutInventingPracticeOwnership()
        {
            Click("OpenCompanions"); Click("FilterOwned"); yield return null;
            Assert.That(Panel("RosterPanel").GetComponentsInChildren<Button>().Any(button => button.name.StartsWith("Companion:")), Is.False);
            Assert.That(TextOf(Panel("RosterPanel")), Does.Contain("계정"));
            Click("RecruitFromRoster"); yield return null;
            Assert.That(Panel("RecruitmentPanel").gameObject.activeInHierarchy, Is.True);
            Assert.That(host.GetComponentsInChildren<InputField>().Single(field => field.name == "RecruitmentCredential").contentType, Is.EqualTo(InputField.ContentType.Password));
        }

        [UnityTest]
        public IEnumerator FullSortieOverviewUsesActualPartyNamesAndElementsWithoutChangingLengthControls()
        {
            Click("StartExpedition"); yield return null;
            var panel = Panel("SortiePanel"); var frame = Panel("LobbyFrame");
            Assert.That(panel.rect.width * panel.rect.height, Is.GreaterThanOrEqualTo(frame.rect.width * frame.rect.height * 0.9f), "preparation is a full overview, not a narrow popup");
            var content = ClientBattleContent.Load();
            var cards = panel.GetComponentsInChildren<RectTransform>().Where(item => item.name.StartsWith("Party:")).ToArray();
            CollectionAssert.AreEquivalent(MemoryForgeExpeditionClient.ReferenceParty.Select(id => "Party:" + id), cards.Select(item => item.name));
            foreach (var card in cards)
            {
                var definition = content.Battle.Characters.Single(character => character.Id == card.name.Substring("Party:".Length));
                Assert.That(TextOf(card), Does.Contain(definition.Name));
                Assert.That(TextOf(card), Does.Contain(Element(definition.Attr)));
            }
            var shortButton = Button("Length:short"); var fullButton = Button("Length:full");
            var selected = shortButton.GetComponent<Image>().sprite;
            Click("Length:full"); yield return null;
            Assert.That(fullButton.GetComponent<Image>().sprite, Is.EqualTo(selected));
            Assert.That(shortButton.GetComponent<Image>().sprite, Is.Not.EqualTo(selected));
            Click("Length:short"); yield return null;
            Assert.That(shortButton.GetComponent<Image>().sprite, Is.EqualTo(selected));
            Assert.That(Button("Depart").IsInteractable(), Is.True);
            Click("CloseSortie"); yield return null;
            Assert.That(panel.gameObject.activeInHierarchy, Is.False);
            Assert.That(Button("StartExpedition").IsInteractable(), Is.True);
        }

        [UnityTest]
        public IEnumerator LargeTouchControlsStayInsideTheFrameAndClearOfEachOtherAndHeaders()
        {
            Object.Destroy(host); yield return null;
            MemoryForgeExpeditionClient.TouchOverride = 144;
            host = new GameObject("LobbyOverviewLargeTouch"); lobby = host.AddComponent<MemoryForgeLobbyClient>();
            yield return null; lobby.Player!.Stop();
            for (float elapsed = 0; elapsed < 0.5f; elapsed += Time.unscaledDeltaTime) yield return null;
            var frame = Panel("LobbyFrame"); var home = Panel("LobbySide");
            AssertControlsFit(home, frame);
            Assert.That(Bounds(Panel("HomeDock"), frame).Overlaps(Bounds((RectTransform)Button("StartExpedition").transform, frame)), Is.False, "home navigation cannot cover the main action");
            Click("OpenCompanions"); yield return null;
            var roster = Panel("RosterPanel"); AssertControlsFit(roster, frame);
            var headers = roster.GetComponentsInChildren<Text>().Where(text => text.transform.parent == roster && Bounds(text.rectTransform, roster).yMax > -160).ToArray();
            foreach (string filter in new[] { "FilterAll", "FilterParty", "FilterOwned" })
                foreach (var header in headers)
                    Assert.That(Bounds((RectTransform)Button(filter).transform, roster).Overlaps(Bounds(header.rectTransform, roster)), Is.False, filter + " cannot hide behind the roster header");
            Click("FilterOwned"); yield return null; AssertControlsFit(roster, frame);
            Click("FilterAll"); yield return null; Click("Companion:sera"); yield return null;
            var details = Panel("CompanionDetails"); AssertControlsFit(details, frame);
            var viewport = details.GetComponentsInChildren<ScrollRect>().Single().viewport;
            foreach (string tab in new[] { "ShowCompanionSkills", "ShowCompanionCards" })
                Assert.That(Bounds((RectTransform)Button(tab).transform, frame).Overlaps(Bounds(viewport, frame)), Is.False, tab + " cannot cover the scroll viewport");
            foreach (string header in new[] { "BackToRoster", "CloseDetails" })
                foreach (string content in new[] { "CharacterArt", "CharacterSheet" })
                    Assert.That(Bounds((RectTransform)Button(header).transform, frame).Overlaps(Bounds(Panel(content), frame)), Is.False, header + " cannot cover " + content);
            Click("BackToRoster"); yield return null;
            Click("CloseRoster"); Click("StartExpedition"); yield return null;
            AssertControlsFit(Panel("SortiePanel"), frame);
        }

        void Click(string name)
        {
            var button = Button(name); Assert.That(button.IsInteractable(), Is.True, name); button.onClick.Invoke();
        }

        Button Button(string name)
        {
            var button = host.GetComponentsInChildren<Button>().SingleOrDefault(item => item.name == name);
            Assert.That(button, Is.Not.Null, name + " must exist"); return button!;
        }

        RectTransform Panel(string name)
        {
            var panel = host.GetComponentsInChildren<RectTransform>(true).SingleOrDefault(item => item.name == name);
            Assert.That(panel, Is.Not.Null, name + " must exist"); return panel!;
        }

        static string TextOf(Component component) => string.Join("\n", component.GetComponentsInChildren<Text>().Select(text => text.text));
        static string Element(string id) => id == "volt" ? "전격" : id == "flame" ? "화염" : id == "frost" ? "빙결" : id == "void" ? "공허" : id;

        static void AssertControlsFit(RectTransform panel, RectTransform frame)
        {
            // Scroll content is intentionally clipped; modal underlays and moment catchers are not foreground controls.
            var controls = panel.GetComponentsInChildren<Button>().Where(button => button.IsInteractable()
                && button.GetComponentInParent<ScrollRect>() == null && button.name != "FigureTap" && button.name != "MomentTap" && button.name != "ModalShield").ToArray();
            foreach (var control in controls)
            {
                var rect = Bounds((RectTransform)control.transform, frame);
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(frame.rect.xMin - 1), control.name);
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(frame.rect.xMax + 1), control.name);
                Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(frame.rect.yMin - 1), control.name);
                Assert.That(rect.yMax, Is.LessThanOrEqualTo(frame.rect.yMax + 1), control.name);
            }
            for (int i = 0; i < controls.Length; i++) for (int j = i + 1; j < controls.Length; j++)
            {
                if (controls[i].transform.IsChildOf(controls[j].transform) || controls[j].transform.IsChildOf(controls[i].transform)) continue;
                var a = Bounds((RectTransform)controls[i].transform, frame); var b = Bounds((RectTransform)controls[j].transform, frame);
                float overlapWidth = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                float overlapHeight = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                Assert.That(overlapWidth > 1 && overlapHeight > 1, Is.False, controls[i].name + " overlaps " + controls[j].name);
            }
        }

        static Rect Bounds(RectTransform rect, RectTransform relativeTo)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var points = corners.Select(relativeTo.InverseTransformPoint).ToArray();
            return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y), points.Max(point => point.x), points.Max(point => point.y));
        }
    }
}
