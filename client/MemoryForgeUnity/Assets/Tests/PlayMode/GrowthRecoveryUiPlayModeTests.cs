#nullable enable
#if UNITY_5_3_OR_NEWER
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public partial class PracticeServerSessionPlayModeTests
    {
        [UnityTest]
        public IEnumerator StaleActiveGrowthAndOwnedPreparationRecoverWithOnlyGetsAndNavigateOnce()
        {
            GrowthLobbyFixture(true); bool failProfile = false; var original = additionalReply;
            additionalReply = seen => failProfile && seen.Method == "GET" && seen.Path == ProgressionPath ? Error(503, "STORE_UNAVAILABLE") : original!(seen);
            var session = Session(); var destinations = new List<string>();
            var home = new GameObject("GrowthRecoveryUi"); var lobby = home.AddComponent<MemoryForgeLobbyClient>(); lobby.SceneLoader = destinations.Add;
            try
            {
                yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => session);
                yield return Until(() => lobby.AccountProgression?.ActiveEncounter != null && home.GetComponentsInChildren<Button>(true).Any(b => b.name == "RefreshRecruitment" && b.interactable), "initial account GET");
                failProfile = true; var failed = session.RefreshProgressionAsync();
                yield return Until(() => failed.IsCompleted, "failed profile GET"); Assert.That(failed.Result.Snapshot, Is.Null); Assert.That(session.ProgressionNeedsRefresh, Is.True);
                home.GetComponentsInChildren<Button>().Single(b => b.name == "OpenGrowth").onClick.Invoke();
                var staleGrowth = home.GetComponentsInChildren<Button>().Single(b => b.name == "ContinueGrowthEncounter");
                Assert.That(staleGrowth.interactable, Is.False); staleGrowth.onClick.Invoke(); Assert.That(destinations, Is.Empty);
                Assert.That(home.GetComponentsInChildren<Button>().Single(b => b.name == "RefreshGrowthAccount").IsInteractable(), Is.True);
                home.GetComponentsInChildren<Button>().Single(b => b.name == "CloseGrowth").onClick.Invoke();
                home.GetComponentsInChildren<Button>().Single(b => b.name == "StartExpedition").onClick.Invoke();
                Assert.That(home.GetComponentsInChildren<Button>().Single(b => b.name == "ContinueOwnedTraining").interactable, Is.False);
                home.GetComponentsInChildren<Button>().Single(b => b.name == "RefreshAccountProgression").onClick.Invoke();
                yield return Until(() => home.GetComponentsInChildren<Button>().Any(b => b.name == "RefreshAccountProgression" && b.IsInteractable())
                    && home.GetComponentsInChildren<Text>().Any(t => t.text.Contains("확인하지 못")), "failed refresh shows a usable recovery action");
                Assert.That(home.GetComponentsInChildren<Button>().Single(b => b.name == "ContinueOwnedTraining").interactable, Is.False);
                Assert.That(host.Posts, Is.Empty);
                failProfile = false;
                home.GetComponentsInChildren<Button>().Single(b => b.name == "RefreshAccountProgression").onClick.Invoke();
                yield return Until(() => home.GetComponentsInChildren<Button>().Any(b => b.name == "ContinueOwnedTraining" && b.IsInteractable()), "fresh GET enables continuation");
                home.GetComponentsInChildren<Button>().Single(b => b.name == "ContinueOwnedTraining").onClick.Invoke();
                yield return Until(() => destinations.Count == 1, "one growth navigation");
                Assert.That(destinations, Is.EqualTo(new[] { MemoryForgeGrowthClient.SceneName })); Assert.That(host.Posts, Is.Empty);
            }
            finally { Object.Destroy(home); MemoryForgeAccountConnection.SetCurrent(null); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator OwnedPreparationAtTouch144ReservesASeparateStatusRowAndShowsMissingEquipment()
        {
            EquipmentLobbyFixture(); MemoryForgeExpeditionClient.TouchOverride = 144;
            EquipmentCharacter(progressionProfile)["weaponInstanceId"] = JValue.CreateNull(); EquipmentCharacter(progressionProfile)["deployable"] = false;
            EquipmentWeaponRow(progressionProfile)["equippedBy"] = JValue.CreateNull();
            var home = new GameObject("OwnedPreparationStatusLayout"); var lobby = home.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => Session());
                yield return Until(() => lobby.AccountProgression?.BossShop != null && home.GetComponentsInChildren<Button>(true).Any(b => b.name == "RefreshRecruitment" && b.interactable), "equipment profile GET");
                home.GetComponentsInChildren<Button>().Single(b => b.name == "StartExpedition").onClick.Invoke();
                var panel = home.GetComponentsInChildren<RectTransform>().Single(r => r.name == "SortiePanel");
                var status = panel.GetComponentsInChildren<RectTransform>().Single(r => r.name == "AccountStatus");
                foreach (string name in new[] { "OwnedMemoryToggle", "RefreshAccountProgression", "DepartOwnedExpedition" })
                {
                    var rect = (RectTransform)panel.GetComponentsInChildren<Button>().Single(b => b.name == name).transform;
                    Assert.That(-status.anchoredPosition.y, Is.GreaterThanOrEqualTo(-rect.anchoredPosition.y + rect.rect.height + 8), name + " has a separate status row");
                }
                Assert.That(-status.anchoredPosition.y + status.rect.height, Is.LessThanOrEqualTo(900));
                Assert.That(panel.GetComponentsInChildren<Text>().Any(t => t.text.Contains("무기 미장착")), Is.True);
                Assert.That(panel.GetComponentsInChildren<Button>().Single(b => b.name == "DepartOwnedExpedition").interactable, Is.False);
                Assert.That(host.Posts, Is.Empty);
            }
            finally { Object.Destroy(home); MemoryForgeExpeditionClient.TouchOverride = null; MemoryForgeAccountConnection.SetCurrent(null); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExplicitAccountRefreshKeepsTheVisibleCharacterAndEquipmentSheets()
        {
            EquipmentLobbyFixture(); var session = Session();
            var home = new GameObject("AccountRefreshPreservesSheet"); var lobby = home.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => session);
                yield return Until(() => lobby.AccountProgression?.BossShop != null && home.GetComponentsInChildren<Button>(true).Any(b => b.name == "RefreshRecruitment" && b.interactable), "equipment profile GET");
                lobby.OpenCharacterLevel("harin");
                lobby.SendMessage("StartAccountProfileRefresh");
                yield return Until(() => home.GetComponentsInChildren<Button>().Any(b => b.name == "PreviewCharacterLevel" && b.IsInteractable()), "character sheet GET refresh");
                Assert.That(home.GetComponentsInChildren<RectTransform>().Any(r => r.name == "CharacterGrowthSheet"), Is.True);
                Assert.That(home.GetComponentsInChildren<Button>().Any(b => b.name == "StartGrowthEncounter"), Is.False, "refresh does not replace a character sheet with training");
                lobby.OpenEquipmentFor("harin"); lobby.SendMessage("StartAccountProfileRefresh");
                yield return Until(() => home.GetComponentsInChildren<Button>().Any(b => b.name == "EquipmentAction:CardSkill" && b.IsInteractable()), "equipment sheet GET refresh");
                Assert.That(home.GetComponentsInChildren<RectTransform>().Any(r => r.name == "EquipmentPanel"), Is.True); Assert.That(host.Posts, Is.Empty);
            }
            finally { Object.Destroy(home); MemoryForgeAccountConnection.SetCurrent(null); }
            yield return null;
        }
    }
}
#endif
