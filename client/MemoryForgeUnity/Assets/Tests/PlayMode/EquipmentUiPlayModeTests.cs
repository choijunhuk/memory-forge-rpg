#nullable enable
#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public partial class PracticeServerSessionPlayModeTests
    {
        void EquipmentLobbyFixture(bool active = false)
        {
            MemoryForgeAccountConnection.SetCurrent(null); EquipmentFixture(active);
            var equipment = additionalReply;
            additionalReply = seen => seen.Method == "GET" && seen.Path == RecruitmentRoute ? Reply.Json(200, RecruitmentJson()) : equipment!(seen);
        }

        static IEnumerator EquipmentShot(GameObject home, string name)
        {
            string? folder = Environment.GetEnvironmentVariable("MF_ACCOUNT_UI_SHOTS");
            if (string.IsNullOrEmpty(folder)) yield break;
            Directory.CreateDirectory(folder!);
            yield return LobbyMomentCapturePlayModeTests.Shot(home, Path.Combine(folder!, name));
        }

        [UnityTest]
        public IEnumerator EquipmentWorkshopRequiresTheExistingAccountConnection()
        {
            MemoryForgeAccountConnection.SetCurrent(null);
            var home = new GameObject("EquipmentNeedsAccount"); var lobby = home.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); lobby.OpenEquipmentWorkshop();
                Assert.That(home.GetComponentsInChildren<Button>().Any(b => b.name == "ConnectRecruitment"), Is.True);
                Assert.That(home.GetComponentsInChildren<Button>().Any(b => b.name == "ConfirmEquipment"), Is.False);
                Assert.That(host.Posts, Is.Empty);
            }
            finally { Object.Destroy(home); MemoryForgeAccountConnection.SetCurrent(null); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator EquipmentTabsUseOwnedRowsWithoutPostingAndKeepLargeTouchControlsInsideTheFrame()
        {
            EquipmentLobbyFixture(); MemoryForgeExpeditionClient.TouchOverride = 144;
            var home = new GameObject("EquipmentLargeTouch"); var lobby = home.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => Session());
                yield return Until(() => lobby.AccountProgression?.Resources != null, "equipment profile GET");
                yield return Until(() => home.GetComponentsInChildren<Button>(true).Any(b => b.name == "RefreshRecruitment" && b.interactable), "account GET finishes");
                lobby.OpenEquipmentFor("nia");
                var panel = home.GetComponentsInChildren<RectTransform>().Single(r => r.name == "EquipmentPanel");
                void CheckBounds()
                {
                    foreach (var b in panel.GetComponentsInChildren<Button>().Where(b => b.GetComponentInParent<ScrollRect>() == null))
                    {
                        var corners = new Vector3[4]; ((RectTransform)b.transform).GetWorldCorners(corners);
                        Assert.That(corners.Min(p => panel.InverseTransformPoint(p).x), Is.GreaterThanOrEqualTo(0), b.name);
                        Assert.That(corners.Max(p => panel.InverseTransformPoint(p).x), Is.LessThanOrEqualTo(1600), b.name);
                        Assert.That(corners.Min(p => panel.InverseTransformPoint(p).y), Is.GreaterThanOrEqualTo(-900), b.name);
                        Assert.That(corners.Max(p => panel.InverseTransformPoint(p).y), Is.LessThanOrEqualTo(0), b.name);
                    }
                }
                CheckBounds();
                var characters = panel.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("EquipmentCharacter:")).Select(b => (RectTransform)b.transform)
                    .OrderByDescending(r => r.anchoredPosition.y).ToArray();
                Assert.That(characters.Length, Is.EqualTo(lobby.AccountProgression!.Characters.Count));
                for (int i = 1; i < characters.Length; i++) Assert.That(characters[i - 1].anchoredPosition.y - characters[i].anchoredPosition.y,
                    Is.GreaterThanOrEqualTo(characters[i - 1].rect.height));
                yield return EquipmentShot(home, "equipment-characters-144.jpg");
                var gear = panel.GetComponentsInChildren<Button>().Single(b => b.name == "EquipmentAction:Accessory");
                var gearScroll = gear.GetComponentInParent<ScrollRect>();
                Assert.That(gearScroll.verticalScrollbar, Is.Not.Null, "below-fold growth controls have a visible scrolling cue");
                gearScroll.verticalNormalizedPosition = 0; yield return null;
                var gearRect = (RectTransform)gear.transform;
                Assert.That(RectTransformUtility.RectangleContainsScreenPoint(gearScroll.viewport,
                    RectTransformUtility.WorldToScreenPoint(null, gearRect.TransformPoint(gearRect.rect.center))), Is.True, "gear can be reached in the actual viewport");
                yield return EquipmentShot(home, "equipment-gear-actions-144.jpg");
                panel.GetComponentsInChildren<Button>().Single(b => b.name == "EquipmentTab:weapons").onClick.Invoke();
                CheckBounds(); Assert.That(panel.GetComponentsInChildren<Button>().Count(b => b.name.StartsWith("EquipmentWeapon:")), Is.EqualTo(lobby.AccountProgression.Weapons.Count));
                yield return EquipmentShot(home, "equipment-weapons-144.jpg");
                panel.GetComponentsInChildren<Button>().Single(b => b.name == "EquipmentTab:shop").onClick.Invoke();
                CheckBounds(); Assert.That(panel.GetComponentsInChildren<Text>().Any(t => t.text.Contains("새로고침") || t.text.Contains("보스")), Is.True);
                yield return EquipmentShot(home, "equipment-shop-144.jpg");
                Assert.That(host.Posts, Is.Empty, "opening/selecting current account data sends no mutation or preview POST");
            }
            finally { Object.Destroy(home); MemoryForgeExpeditionClient.TouchOverride = null; MemoryForgeAccountConnection.SetCurrent(null); }
            yield return null;
        }
        [UnityTest]
        public IEnumerator EquipmentInsufficiencyShowsTheExactCostAndCannotCommitAPreview()
        {
            EquipmentLobbyFixture();
            post = seen =>
            {
                Assert.That(seen.Path, Does.EndWith("/skill-up-preview"));
                var preview = EquipmentChange(progressionProfile, "skillUp", true);
                preview["canCommit"] = false; preview["code"] = "INSUFFICIENT_RESOURCES";
                return Reply.Json(200, preview);
            };
            var home = new GameObject("EquipmentInsufficientUi"); var lobby = home.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => Session());
                yield return Until(() => lobby.AccountProgression?.BossShop != null && home.GetComponentsInChildren<Button>(true).Any(b => b.name == "RefreshRecruitment" && b.interactable), "equipment account GET");
                lobby.OpenEquipmentFor("nia");
                home.GetComponentsInChildren<Button>().Single(b => b.name == "EquipmentAction:CardSkill").onClick.Invoke();
                yield return Until(() => home.GetComponentsInChildren<Text>().Any(t => t.text.Contains("필요 재료가 부족해요")), "insufficient equipment preview");
                Assert.That(home.GetComponentsInChildren<Text>().Any(t => t.text.Contains("INSUFFICIENT_RESOURCES")), Is.False);
                Assert.That(home.GetComponentsInChildren<Text>().Any(t => t.text.Contains("기술서") && t.text.Contains("2 부족")), Is.True);
                var confirm = home.GetComponentsInChildren<Button>().Single(b => b.name == "ConfirmEquipment");
                Assert.That(confirm.interactable, Is.False); confirm.onClick.Invoke();
                Assert.That(host.Posts.Length, Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
                Assert.That(lobby.AccountProgression!.Characters.Single(c => c.CharId == "nia").SkillLv, Is.EqualTo(1));
                yield return EquipmentShot(home, "equipment-insufficient-preview.jpg");
            }
            finally { Object.Destroy(home); MemoryForgeAccountConnection.SetCurrent(null); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator EquipmentUnknownCommitRetriesTheSameRequestOnlyAfterAnExplicitClick() => EquipmentLostReplyScenario("workshop");
        [UnityTest]
        public IEnumerator EquipmentUnknownCommitCanBeResolvedFromGrowthWithoutCreatingANewRequest() => EquipmentLostReplyScenario("growth");
        [UnityTest]
        public IEnumerator EquipmentUnknownCommitCanBeResolvedFromRecruitmentWithoutCreatingANewRequest() => EquipmentLostReplyScenario("recruitment");

        IEnumerator EquipmentLostReplyScenario(string entry)
        {
            EquipmentLobbyFixture(true); progressionProfile["resources"]!["refine_core"] = 1L;
            var frozen = growthEncounters[GrowthEncounter]["state"]!.DeepClone(); byte[]? saved = null; int commits = 0; string? firstBody = null;
            post = seen =>
            {
                if (seen.Path.EndsWith("/refine-preview", StringComparison.Ordinal)) return Reply.Json(200, EquipmentChange(progressionProfile, "weaponRefine", true));
                Assert.That(seen.Path, Is.EqualTo(PracticeCommandOutbox.EquipmentActionRoute("weaponRefine", EquipmentWeapon)));
                if (saved == null)
                {
                    commits++; firstBody = seen.Text; var request = JObject.Parse(seen.Text);
                    var receipt = EquipmentChange(GrowthCurrentProfile(), "weaponRefine", false, request["requestId"]!.Value<string>()!);
                    progressionProfile = (JObject)receipt["profile"]!.DeepClone(); saved = Utf8(receipt);
                    return new Reply(200, saved.Take(saved.Length / 2).ToArray());
                }
                Assert.That(seen.Text, Is.EqualTo(firstBody)); return new Reply(200, saved);
            };
            var home = new GameObject("EquipmentPendingUi"); var lobby = home.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => Session());
                yield return Until(() => lobby.AccountProgression?.BossShop != null && home.GetComponentsInChildren<Button>(true).Any(b => b.name == "RefreshRecruitment" && b.interactable), "equipment account GET");
                lobby.OpenEquipmentWorkshop();
                home.GetComponentsInChildren<Button>().Single(b => b.name == "EquipmentTab:weapons").onClick.Invoke();
                home.GetComponentsInChildren<Button>().Single(b => b.name == "EquipmentWeapon:" + EquipmentWeapon).onClick.Invoke();
                home.GetComponentsInChildren<Button>().Single(b => b.name == "EquipmentAction:WeaponRefine").onClick.Invoke();
                yield return Until(() => home.GetComponentsInChildren<Button>().Any(b => b.name == "ConfirmEquipment" && b.IsInteractable()), "refine preview");
                Assert.That(Outbox().Load(), Is.Null); Assert.That(commits, Is.Zero, "a preview never applies the equipment change");
                home.GetComponentsInChildren<Button>().Single(b => b.name == "ConfirmEquipment").onClick.Invoke();
                yield return Until(() => home.GetComponentsInChildren<Button>().Any(b => b.name == "RetryEquipmentPending" && b.IsInteractable()), "equipment unresolved commit");
                Assert.That(commits, Is.EqualTo(1)); Assert.That(Outbox().Load()!.Body, Is.EqualTo(firstBody));
                Assert.That(host.Posts.Count(p => p.Path.EndsWith("/refine", StringComparison.Ordinal)), Is.EqualTo(1), "no automatic retry");
                if (entry != "workshop")
                {
                    home.GetComponentsInChildren<Button>().Single(b => b.name == "CloseEquipment").onClick.Invoke();
                    home.GetComponentsInChildren<Button>().Single(b => b.name == (entry == "growth" ? "OpenGrowth" : "OpenRecruitment")).onClick.Invoke();
                    if (entry == "growth") home.GetComponentsInChildren<Button>().Single(b => b.name == "RetryGrowthPending").onClick.Invoke();
                }
                home.GetComponentsInChildren<Button>().Single(b => b.name == (entry == "recruitment" ? "RetryRecruitmentPending" : "RetryEquipmentPending")).onClick.Invoke();
                yield return Until(() => lobby.AccountProgression!.Weapons.Single(w => w.InstanceId == EquipmentWeapon).Refine == 2 && Outbox().Load() == null, "same request confirmed");
                Assert.That(commits, Is.EqualTo(1)); Assert.That(lobby.AccountProgression!.Resources!["refine_core"], Is.Zero, "exactly one owned refine core was consumed");
                Assert.That(JToken.DeepEquals(frozen, growthEncounters[GrowthEncounter]["state"]), Is.True);
            }
            finally { Object.Destroy(home); MemoryForgeAccountConnection.SetCurrent(null); }
            yield return null;
        }
    }
}
#endif
