#nullable enable
#if UNITY_5_3_OR_NEWER
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
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public partial class PracticeServerSessionPlayModeTests
    {
        void WeeklyLobbyFixture(bool unlocked)
        {
            MemoryForgeAccountConnection.SetCurrent(null); EquipmentFixture();
            foreach (var boss in progressionProfile["weeklyBoss"]!["bosses"]!.OfType<JObject>()) boss["available"] = unlocked;
            var growth = additionalReply;
            additionalReply = seen =>
            {
                if (seen.Method == "GET" && seen.Path == RecruitmentRoute) return Reply.Json(200, RecruitmentJson());
                if (seen.Method == "GET" && seen.Path == ProgressionPath && growthActive != null)
                {
                    var profile = (JObject)progressionProfile.DeepClone();
                    profile["activeEncounter"] = new JObject { ["encounterId"] = GrowthEncounter, ["kind"] = "weeklyBoss", ["bossId"] = "b_bell", ["difficulty"] = 5,
                        ["practice"] = true, ["phase"] = "PLAYER", ["version"] = 0 };
                    return Reply.Json(200, profile);
                }
                return growth!(seen);
            };
        }

        [UnityTest]
        public IEnumerator WeeklyBossMenuShowsTheRealAllowanceAndKeepsLockedContentHonestAtLargeTouchSize()
        {
            WeeklyLobbyFixture(false); MemoryForgeExpeditionClient.TouchOverride = 144;
            var home = new GameObject("WeeklyLockedUi"); var lobby = home.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => Session());
                yield return Until(() => lobby.AccountProgression?.WeeklyBoss != null && home.GetComponentsInChildren<Button>(true).Any(b => b.name == "RefreshRecruitment" && b.interactable), "weekly profile reads");
                home.GetComponentsInChildren<Button>().Single(b => b.name == "OpenGrowth").onClick.Invoke();
                home.GetComponentsInChildren<Button>().Single(b => b.name == "OpenWeeklyBosses").onClick.Invoke();
                var panel = home.GetComponentsInChildren<RectTransform>().Single(r => r.name == "GrowthPanel");
                Assert.That(panel.GetComponentsInChildren<Button>().Count(b => b.name.StartsWith("WeeklyBoss:")), Is.EqualTo(4));
                Assert.That(panel.GetComponentsInChildren<Button>().Count(b => b.name.StartsWith("WeeklyDifficulty:")), Is.EqualTo(5));
                Assert.That(panel.GetComponentsInChildren<Button>().Single(b => b.name == "StartWeeklyBoss").IsInteractable(), Is.False);
                foreach (var button in panel.GetComponentsInChildren<Button>().Where(b => b.GetComponentInParent<ScrollRect>() == null))
                {
                    var rect = (RectTransform)button.transform; var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                    Assert.That(rect.rect.height, Is.GreaterThanOrEqualTo(144));
                    Assert.That(corners.Min(p => panel.InverseTransformPoint(p).y), Is.GreaterThanOrEqualTo(-900), button.name);
                    Assert.That(corners.Max(p => panel.InverseTransformPoint(p).x), Is.LessThanOrEqualTo(1600), button.name);
                }
                Assert.That(host.Posts, Is.Empty);
                string? shots = Environment.GetEnvironmentVariable("MF_ACCOUNT_UI_SHOTS");
                if (!string.IsNullOrEmpty(shots)) { Directory.CreateDirectory(shots!); yield return LobbyMomentCapturePlayModeTests.Shot(home, Path.Combine(shots!, "weekly-menu-144.jpg")); }
            }
            finally { Object.Destroy(home); MemoryForgeAccountConnection.SetCurrent(null); MemoryForgeExpeditionClient.TouchOverride = null; }
            yield return null;
        }

        [UnityTest]
        public IEnumerator WeeklyPracticeLaunchStagesOnlyTheExplicitBossDifficultyAndPracticeIntent()
        {
            WeeklyLobbyFixture(true);
            var home = new GameObject("WeeklyLaunchUi"); var lobby = home.AddComponent<MemoryForgeLobbyClient>(); var navigation = new List<string>(); lobby.SceneLoader = navigation.Add;
            try
            {
                yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => Session());
                yield return Until(() => lobby.AccountProgression?.WeeklyBoss != null && home.GetComponentsInChildren<Button>(true).Any(b => b.name == "RefreshRecruitment" && b.interactable), "weekly profile reads");
                home.GetComponentsInChildren<Button>().Single(b => b.name == "OpenGrowth").onClick.Invoke();
                home.GetComponentsInChildren<Button>().Single(b => b.name == "OpenWeeklyBosses").onClick.Invoke();
                home.GetComponentsInChildren<Button>().Single(b => b.name == "WeeklyDifficulty:5").onClick.Invoke();
                home.GetComponentsInChildren<Button>().Single(b => b.name == "WeeklyPractice").onClick.Invoke();
                Assert.That(host.Posts, Is.Empty);
                post = seen =>
                {
                    Assert.That(seen.Path, Is.EqualTo(WeeklyPath)); var body = JObject.Parse(seen.Text);
                    Assert.That(body["difficulty"]!.Value<int>(), Is.EqualTo(5)); Assert.That(body["practice"]!.Value<bool>(), Is.True);
                    Assert.That(body["party"]!.Values<string>(), Is.EqualTo(lobby.SelectedOwnedParty)); Assert.That(Outbox().Load()!.Body, Is.EqualTo(seen.Text));
                    progressionProfile["revision"] = 4L; growthActive = GrowthEncounter;
                    var current = WeeklyEncounterJson(version: 0, revision: 4); current["practice"] = true; growthEncounters[GrowthEncounter] = current;
                    return Reply.Json(200, current);
                };
                home.GetComponentsInChildren<Button>().Single(b => b.name == "StartWeeklyBoss").onClick.Invoke();
                yield return Until(() => navigation.Count == 1, "confirmed weekly encounter navigation");
                Assert.That(navigation[0], Is.EqualTo(MemoryForgeGrowthClient.SceneName)); Assert.That(host.Posts.Count, Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
            }
            finally { Object.Destroy(home); MemoryForgeAccountConnection.SetCurrent(null); }
            yield return null;
        }
    }
}
#endif
