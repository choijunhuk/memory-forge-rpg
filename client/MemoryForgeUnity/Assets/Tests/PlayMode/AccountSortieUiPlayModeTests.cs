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
        void AccountUiFixture(bool active = false)
        {
            MemoryForgeAccountConnection.SetCurrent(null);
            ProgressionFixture(!active);
            var progression = additionalReply;
            additionalReply = seen => seen.Method == "GET" && seen.Path == RecruitmentRoute ? Reply.Json(200, RecruitmentJson()) : progression!(seen);
        }

        void GrowthLobbyFixture(bool active = false)
        {
            MemoryForgeAccountConnection.SetCurrent(null); GrowthFixture(active);
            var growth = additionalReply;
            additionalReply = seen => seen.Method == "GET" && seen.Path == RecruitmentRoute ? Reply.Json(200, RecruitmentJson()) : growth!(seen);
        }

        [UnityTest]
        public IEnumerator GrowthPanelsKeepLargeTouchControlsInsideTheFrameAndScrollRowsSeparate()
        {
            GrowthLobbyFixture(); MemoryForgeExpeditionClient.TouchOverride = 144;
            var rows = new JArray();
            foreach (string id in new[] { "dg_ink", "dg_credit", "dg_oil", "dg_seal", "dg_book", "dg_polish" })
            { var row = (JObject)progressionProfile["dungeons"]![0]!.DeepClone(); row["id"] = id; rows.Add(row); }
            progressionProfile["dungeons"] = rows;
            var home = new GameObject("LargeGrowthPanels"); var lobby = home.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => Session());
                yield return Until(() => lobby.AccountProgression?.Resources != null, "growth lobby GET");
                home.GetComponentsInChildren<Button>().Single(button => button.name == "OpenGrowth").onClick.Invoke();
                yield return Until(() => home.GetComponentsInChildren<Button>().Any(button => button.name == "OpenCharacterGrowth" && button.IsInteractable()), "growth reads finish");
                var panel = home.GetComponentsInChildren<RectTransform>().Single(rect => rect.name == "GrowthPanel");
                void Within()
                {
                    foreach (var button in panel.GetComponentsInChildren<Button>().Where(button => button.GetComponentInParent<ScrollRect>() == null))
                    {
                        var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
                        Assert.That(corners.Min(point => panel.InverseTransformPoint(point).y), Is.GreaterThanOrEqualTo(panel.rect.yMin), button.name + " stays inside the actual bottom edge");
                        Assert.That(corners.Max(point => panel.InverseTransformPoint(point).y), Is.LessThanOrEqualTo(panel.rect.yMax), button.name + " stays below the actual top edge");
                        Assert.That(corners.Min(point => panel.InverseTransformPoint(point).x), Is.GreaterThanOrEqualTo(panel.rect.xMin), button.name + " stays inside the actual left edge");
                        Assert.That(corners.Max(point => panel.InverseTransformPoint(point).x), Is.LessThanOrEqualTo(panel.rect.xMax), button.name + " stays inside the actual right edge");
                    }
                }
                void Rows(string prefix)
                {
                    var buttons = panel.GetComponentsInChildren<Button>().Where(button => button.name.StartsWith(prefix)).Select(button => (RectTransform)button.transform)
                        .OrderByDescending(rect => rect.anchoredPosition.y).ToArray();
                    for (int n = 1; n < buttons.Length; n++) Assert.That(buttons[n - 1].anchoredPosition.y - buttons[n].anchoredPosition.y,
                        Is.GreaterThanOrEqualTo(buttons[n - 1].rect.height), prefix + " scroll rows do not overlap");
                }
                Within(); Rows("GrowthDungeon:");
                string? shots = Environment.GetEnvironmentVariable("MF_ACCOUNT_UI_SHOTS");
                if (!string.IsNullOrEmpty(shots))
                { Directory.CreateDirectory(shots!); yield return LobbyMomentCapturePlayModeTests.Shot(home, Path.Combine(shots!, "training-workshop-144.jpg")); }
                home.GetComponentsInChildren<Button>().Single(button => button.name == "OpenCharacterGrowth").onClick.Invoke();
                Within(); Rows("GrowthCharacter:"); Assert.That(host.Posts, Is.Empty);
                if (!string.IsNullOrEmpty(shots)) yield return LobbyMomentCapturePlayModeTests.Shot(home, Path.Combine(shots!, "character-level-144.jpg"));
            }
            finally { Object.Destroy(home); MemoryForgeExpeditionClient.TouchOverride = null; MemoryForgeAccountConnection.SetCurrent(null); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnActiveGrowthEncounterStillAllowsPreviewAndCommitWithoutChangingItsBattle()
        {
            GrowthLobbyFixture(true);
            var beforeBattle = growthEncounters[GrowthEncounter]["state"]!.DeepClone();
            var home = new GameObject("ActiveEncounterLevelUi"); var lobby = home.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => Session());
                yield return Until(() => lobby.AccountProgression?.ActiveEncounter != null, "active encounter profile GET");
                home.GetComponentsInChildren<Button>().Single(button => button.name == "OpenGrowth").onClick.Invoke();
                yield return Until(() => home.GetComponentsInChildren<Button>().Any(button => button.name == "ContinueGrowthEncounter" && button.IsInteractable()), "active encounter lobby");
                Assert.That(home.GetComponentsInChildren<Button>().Any(button => button.name == "OpenCharacterGrowth"), Is.True, "ongoing training must not hide growth");
                post = seen =>
                {
                    var preview = GrowthPreviewJson(); preview["before"] = GrowthLevelJson(1, 0, 61);
                    preview["cost"] = new JObject { ["ink"] = 200L, ["credits"] = 100L };
                    if (seen.Path.EndsWith("/level-preview", StringComparison.Ordinal)) return Reply.Json(200, preview);
                    Assert.That(seen.Path, Is.EqualTo(PracticeCommandOutbox.CharacterLevelUpRoute("nia")));
                    var request = JObject.Parse(seen.Text); progressionProfile["revision"] = 4;
                    progressionProfile["characters"]![0]!["level"] = 2; progressionProfile["resources"]!["ink"] = 5800L; progressionProfile["resources"]!["credits"] = 9900L;
                    growthEncounters[GrowthEncounter]["revision"] = 4;
                    return Reply.Json(200, new JObject { ["requestId"] = request["requestId"]!.DeepClone(), ["revision"] = 4L, ["kind"] = "characterLevelUp", ["charId"] = "nia",
                        ["before"] = preview["before"]!.DeepClone(), ["after"] = preview["after"]!.DeepClone(), ["cost"] = preview["cost"]!.DeepClone(), ["profile"] = GrowthCurrentProfile() });
                };
                home.GetComponentsInChildren<Button>().Single(button => button.name == "OpenCharacterGrowth").onClick.Invoke();
                home.GetComponentsInChildren<Button>().Single(button => button.name == "PreviewCharacterLevel").onClick.Invoke();
                yield return Until(() => home.GetComponentsInChildren<Button>().Any(button => button.name == "CommitCharacterLevel" && button.IsInteractable()), "level preview completed");
                Assert.That(Outbox().Load(), Is.Null);
                home.GetComponentsInChildren<Button>().Single(button => button.name == "CommitCharacterLevel").onClick.Invoke();
                yield return Until(() => lobby.AccountProgression!.Characters[0].Level == 2, "committed character level");
                Assert.That(JToken.DeepEquals(beforeBattle, growthEncounters[GrowthEncounter]["state"]), Is.True);
                Assert.That(growthEncounters[GrowthEncounter]["version"]!.Value<int>(), Is.EqualTo(3));
                Assert.That(host.Posts.Count(seen => seen.Path.EndsWith("/level-up", StringComparison.Ordinal)), Is.EqualTo(1));
            }
            finally { Object.Destroy(home); MemoryForgeAccountConnection.SetCurrent(null); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator OwnedPreparationUsesOnlyOwnedIdsAndDepartsOnlyAfterAnExplicitConfirmedLaunch()
        {
            AccountUiFixture(); var home = new GameObject("OwnedAccountSortieUi");
            var lobby = home.AddComponent<MemoryForgeLobbyClient>(); var navigation = new List<string>(); lobby.SceneLoader = navigation.Add;
            try
            {
                yield return null; lobby.Player!.Stop();
                Assert.That(lobby.ConnectRecruitment(() => Session()), Is.True);
                yield return Until(() => lobby.AccountProgression != null, "account progression GET");
                home.GetComponentsInChildren<Button>().Single(button => button.name == "StartExpedition").onClick.Invoke();
                yield return Until(() => home.GetComponentsInChildren<Button>().Single(button => button.name == "DepartOwnedExpedition").IsInteractable(), "confirmed account reads");
                string? shots = Environment.GetEnvironmentVariable("MF_ACCOUNT_UI_SHOTS");
                if (!string.IsNullOrEmpty(shots))
                {
                    Directory.CreateDirectory(shots!);
                    yield return LobbyMomentCapturePlayModeTests.Shot(home, Path.Combine(shots!, "owned-preparation-first.jpg"));
                }
                Assert.That(host.Posts, Is.Empty); Assert.That(navigation, Is.Empty);
                Assert.That(lobby.SelectedOwnedParty, Is.EqualTo(new[] { "nia", "harin", "rien" }));
                Assert.That(home.GetComponentsInChildren<Button>().Where(button => button.name.StartsWith("SelectOwnedParty:")).Select(button => button.name),
                    Is.EquivalentTo(new[] { "SelectOwnedParty:nia", "SelectOwnedParty:harin", "SelectOwnedParty:rien" }));
                Assert.That(home.GetComponentsInChildren<Button>().Any(button => button.name.StartsWith("Length:") || button.name == "Depart"), Is.False);
                home.GetComponentsInChildren<Button>().Single(button => button.name == "RemoveOwnedParty:harin").onClick.Invoke();
                home.GetComponentsInChildren<Button>().Single(button => button.name == "SelectOwnedParty:harin").onClick.Invoke();
                Assert.That(lobby.SelectedOwnedParty, Is.EqualTo(new[] { "nia", "rien", "harin" }));
                post = seen =>
                {
                    Assert.That(seen.Path, Is.EqualTo(ExpeditionPath));
                    Assert.That(JObject.Parse(seen.Text)["party"]!.Values<string>(), Is.EqualTo(new[] { "nia", "rien", "harin" }));
                    Assert.That(JObject.Parse(seen.Text)["useMemory"]!.Type, Is.EqualTo(JTokenType.Boolean));
                    Assert.That(Outbox().Load()!.Body, Is.EqualTo(seen.Text));
                    world.RunId = RunA; world.Version = 0; world.Phase = "MAP"; world.InBattle = false; progressionProfile["revision"] = 4;
                    return Reply.Json(200, RunJson(world, 0));
                };
                home.GetComponentsInChildren<Button>().Single(button => button.name == "DepartOwnedExpedition").onClick.Invoke();
                yield return Until(() => navigation.Count > 0, "confirmed departure scene navigation");
                Assert.That(host.Posts.Count, Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
                Assert.That(navigation, Is.EqualTo(new[] { MemoryForgeLobbyClient.ServerExpeditionScene }));
            }
            finally { Object.Destroy(home); MemoryForgeAccountConnection.SetCurrent(null); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AccountHomeRetainsTheRunAndOnlyConfirmedAbandonClosesItBeforeReturning()
        {
            AccountUiFixture(true);
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                MemoryForgeAccountConnection.SetCurrent(owner);
                var screen = new GameObject("OwnedAccountRunUi"); screen.SetActive(false);
                var view = screen.AddComponent<MemoryForgeServerPracticeClient>(); var navigation = new List<string>(); view.SceneLoader = navigation.Add;
                screen.SetActive(true);
                try
                {
                    yield return Settled(view);
                    screen.GetComponentsInChildren<Button>().Single(button => button.name == "ReturnAccountHome").onClick.Invoke();
                    Assert.That(world.RunId, Is.EqualTo(RunA)); Assert.That(host.Posts, Is.Empty);
                    Assert.That(owner.IsAuthenticated, Is.True); Assert.That(navigation.Count, Is.EqualTo(1));
                    screen.GetComponentsInChildren<Button>().Single(button => button.name == "RequestAccountAbandon").onClick.Invoke();
                    Assert.That(host.Posts, Is.Empty);
                    screen.GetComponentsInChildren<Button>().Single(button => button.name == "CancelAccountAbandon").onClick.Invoke();
                    Assert.That(host.Posts, Is.Empty);
                    post = seen =>
                    {
                        Assert.That(seen.Path, Is.EqualTo(PracticeCommandOutbox.AbandonRoute(RunA)));
                        int next = world.Version + 1; world.RunId = null; world.Finished++;
                        return Reply.Json(200, new JObject { ["runId"] = RunA, ["version"] = next, ["memoryVersion"] = world.MemoryVersion });
                    };
                    screen.GetComponentsInChildren<Button>().Single(button => button.name == "RequestAccountAbandon").onClick.Invoke();
                    screen.GetComponentsInChildren<Button>().Single(button => button.name == "ConfirmAccountAbandon").onClick.Invoke();
                    yield return Until(() => navigation.Count == 2, "closed server pointer before home navigation");
                    Assert.That(world.RunId, Is.Null); Assert.That(world.MemoryVersion, Is.EqualTo(2));
                    Assert.That(host.Posts.Count, Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
                    Assert.That(view.Snapshot!.Run, Is.Null); Assert.That(owner.IsAuthenticated, Is.True);
                }
                finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator GrowthReadFailureDoesNotPretendThatAnEncounterIsAbsent()
        {
            MemoryForgeAccountConnection.SetCurrent(null); GrowthFixture();
            var previous = additionalReply;
            additionalReply = seen => seen.Method == "GET" && seen.Path == ProgressionPath ? Error(503, "UNAVAILABLE") : previous!(seen);
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                MemoryForgeAccountConnection.SetCurrent(owner);
                var screen = new GameObject("GrowthFailedProfile"); screen.SetActive(false); screen.AddComponent<MemoryForgeGrowthClient>(); screen.SetActive(true);
                try
                {
                    yield return Until(() => screen.GetComponentsInChildren<Text>().Any(t => t.text.Contains("계정 진행을 확인하지 못했어요")), "failed profile is explicit");
                    Assert.That(screen.GetComponentsInChildren<Text>().Any(t => t.text.Contains("진행 중인 훈련이 없어요")), Is.False);
                    Assert.That(host.Posts, Is.Empty);
                }
                finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator GrowthFinishedResultIsReadAgainAfterPauseWithoutSubmittingAnotherCommand()
        {
            MemoryForgeAccountConnection.SetCurrent(null); GrowthFixture(true);
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                MemoryForgeAccountConnection.SetCurrent(owner);
                var screen = new GameObject("GrowthResultResume"); screen.SetActive(false); var view = screen.AddComponent<MemoryForgeGrowthClient>(); screen.SetActive(true);
                try
                {
                    yield return Until(() => view.Snapshot != null && !view.Busy, "initial encounter");
                    growthEncounters[GrowthEncounter] = GrowthEncounterJson(4, 4, "VICTORY"); growthActive = null; progressionProfile["revision"] = 4L;
                    screen.SendMessage("OnApplicationFocus", true);
                    yield return Until(() => view.Snapshot?.Settlement != null && !view.Busy, "settled encounter read");
                    screen.SendMessage("OnApplicationPause", true); Assert.That(view.Snapshot, Is.Null);
                    screen.SendMessage("OnApplicationPause", false);
                    yield return Until(() => view.Snapshot?.Settlement != null && !view.Busy, "same-owner result restored by GET");
                    Assert.That(view.Snapshot!.EncounterId, Is.EqualTo(GrowthEncounter)); Assert.That(host.Posts, Is.Empty); Assert.That(owner.IsAuthenticated, Is.True);
                }
                finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator InterruptedCombatCutinDestroysItsOwnedPlateSprite()
        {
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = false;
            var home = new GameObject("CutinLifetimeTest"); var lobby = home.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop();
                var play = typeof(MemoryForgeGrowthClient).Assembly.GetType("MemoryForge.Client.MemoryForgeCombatCutin", true)!
                    .GetMethod("Play", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                lobby.StartCoroutine((IEnumerator)play.Invoke(null, new object?[] { home.transform, Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular"), null, "동료", "필살기", "InterruptedCutin" })!);
                var overlay = home.GetComponentsInChildren<Canvas>().Single(c => c.name == "InterruptedCutin");
                var plate = overlay.GetComponentsInChildren<Image>().Single(i => i.name == "Band").sprite; Assert.That(plate != null, Is.True);
                home.SetActive(false);
                yield return Until(() => plate == null, "interrupted cutin releases its dynamically created sprite");
            }
            finally { Object.Destroy(home); MemoryForgeUiMotion.Reduced = reduced; }
            yield return null;
        }

        [UnityTest]
        public IEnumerator GrowthBattleKeepsItsActionsReachableAtLargeTouchSize()
        {
            MemoryForgeAccountConnection.SetCurrent(null); GrowthFixture(true); MemoryForgeExpeditionClient.TouchOverride = 144;
            var state = (JObject)growthEncounters[GrowthEncounter]["state"]!;
            foreach (var enemy in state["enemies"]!.OfType<JObject>())
            { enemy["id"] = "e_moth"; enemy["name"] = "종탑 나방"; enemy["alive"] = true; enemy["maxHp"] = enemy["hp"]!.DeepClone(); }
            var firstActor = (JObject)state["actors"]![0]!.DeepClone(); var actors = new JArray();
            foreach (string id in new[] { "nia", "harin", "rien" })
            { var actor = (JObject)firstActor.DeepClone(); actor["id"] = id; actor["name"] = id; actor["gauge"] = 100; actors.Add(actor); }
            state["actors"] = actors;
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                MemoryForgeAccountConnection.SetCurrent(owner);
                var screen = new GameObject("LargeGrowthBattle"); screen.SetActive(false); var view = screen.AddComponent<MemoryForgeGrowthClient>(); screen.SetActive(true);
                try
                {
                    yield return Until(() => view.Snapshot != null && !view.Busy, "growth battle reads finish");
                    var frame = screen.GetComponentsInChildren<RectTransform>().Single(rect => rect.name == "GrowthFrame");
                    var party = screen.GetComponentsInChildren<RectTransform>().Single(rect => rect.name == "GrowthParty");
                    foreach (var label in party.GetComponentsInChildren<Text>().Where(t => t.transform.parent == party))
                    {
                        label.cachedTextGenerator.Populate(label.text, label.GetGenerationSettings(label.rectTransform.rect.size));
                        Assert.That(label.cachedTextGenerator.characterCountVisible, Is.GreaterThan(0), label.text + " is not clipped out by the font line height");
                    }
                    foreach (var button in screen.GetComponentsInChildren<Button>())
                    {
                        var rect = (RectTransform)button.transform;
                        Assert.That(rect.rect.height, Is.GreaterThanOrEqualTo(144), button.name + " reserves the touch height");
                        if (button.GetComponentInParent<ScrollRect>() != null) continue;
                        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                        Assert.That(corners.Min(p => frame.InverseTransformPoint(p).y), Is.GreaterThanOrEqualTo(frame.rect.yMin), button.name + " stays inside the actual frame bottom");
                        Assert.That(corners.Max(p => frame.InverseTransformPoint(p).y), Is.LessThanOrEqualTo(frame.rect.yMax), button.name + " stays inside the actual frame top");
                        Assert.That(corners.Min(p => frame.InverseTransformPoint(p).x), Is.GreaterThanOrEqualTo(frame.rect.xMin), button.name + " stays inside the actual frame left");
                        Assert.That(corners.Max(p => frame.InverseTransformPoint(p).x), Is.LessThanOrEqualTo(frame.rect.xMax), button.name + " stays inside the actual frame right");
                    }
                    var portraits = screen.GetComponentsInChildren<Button>().Where(button => button.name.StartsWith("GrowthUlt:")).Select(button => (RectTransform)button.transform).ToArray();
                    Assert.That(portraits.Length, Is.EqualTo(3));
                    for (int n = 1; n < portraits.Length; n++) Assert.That(WorldRect(portraits[n]).xMin, Is.GreaterThanOrEqualTo(WorldRect(portraits[n - 1]).xMax), "ultimate controls remain physically separated despite different anchor origins");
                    string? shots = Environment.GetEnvironmentVariable("MF_ACCOUNT_UI_SHOTS");
                    if (!string.IsNullOrEmpty(shots)) { Directory.CreateDirectory(shots!); yield return LobbyMomentCapturePlayModeTests.Shot(screen, Path.Combine(shots!, "growth-battle-144.jpg"), MemoryForgeGrowthClient.OwnedCanvas); }
                    Assert.That(host.Posts, Is.Empty);
                }
                finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); MemoryForgeExpeditionClient.TouchOverride = null; }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator GrowthBattleUsesPureQueriesAndShowsTheConfirmedLethalUltimateOverTheResult()
        {
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = false;
            MemoryForgeAccountConnection.SetCurrent(null); GrowthFixture(true);
            var state = (JObject)growthEncounters[GrowthEncounter]["state"]!;
            foreach (var enemy in state["enemies"]!.OfType<JObject>())
            { enemy["id"] = "e_moth"; enemy["name"] = "종탑 나방"; enemy["alive"] = true; enemy["hp"] = 10; enemy["maxHp"] = 10; }
            var actor = (JObject)state["actors"]![0]!; string actorId = actor["id"]!.Value<string>()!; actor["gauge"] = 100;
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                MemoryForgeAccountConnection.SetCurrent(owner);
                var screen = new GameObject("ConnectedGrowthUi"); screen.SetActive(false);
                var view = screen.AddComponent<MemoryForgeGrowthClient>(); screen.SetActive(true);
                try
                {
                    yield return Until(() => screen.GetComponentsInChildren<Button>().Any(button => button.name == "GrowthUlt:" + actorId && button.IsInteractable()), "current authenticated growth battle");
                    Assert.That(host.Posts, Is.Empty, "opening the scene only reads");
                    string? shots = Environment.GetEnvironmentVariable("MF_ACCOUNT_UI_SHOTS");
                    if (!string.IsNullOrEmpty(shots))
                    {
                        Directory.CreateDirectory(shots!);
                        yield return LobbyMomentCapturePlayModeTests.Shot(screen, Path.Combine(shots!, "growth-battle-first.jpg"), MemoryForgeGrowthClient.OwnedCanvas);
                    }
                    post = seen =>
                    {
                        if (seen.Path.EndsWith("/battle-query", StringComparison.Ordinal))
                        {
                            var query = QueryJson(world, "h1", "now", null, Array.Empty<string>());
                            query.Remove("runId"); query["encounterId"] = GrowthEncounter; query["version"] = 3; query["phase"] = "PLAYER";
                            ((JObject)query["context"]!).Remove("node"); return Reply.Json(200, query);
                        }
                        Assert.That(seen.Path, Is.EqualTo(PracticeCommandOutbox.EncounterCommandRoute(GrowthEncounter)));
                        Assert.That(JObject.Parse(seen.Text)["kind"]!.Value<string>(), Is.EqualTo("ult"));
                        growthEncounters[GrowthEncounter] = GrowthEncounterJson(4, 4, "VICTORY", events: true);
                        growthActive = null; progressionProfile["revision"] = 4;
                        return Reply.Json(200, growthEncounters[GrowthEncounter]);
                    };
                    screen.GetComponentsInChildren<Button>().Single(button => button.name == "GrowthCard:h1").onClick.Invoke();
                    yield return Until(() => host.Posts.Any(seen => seen.Path.EndsWith("/battle-query", StringComparison.Ordinal))
                        && !view.Busy && screen.GetComponentsInChildren<Button>().Any(button => button.name == "GrowthUlt:" + actorId && button.IsInteractable()), "pure card query completes");
                    Assert.That(Outbox().Load(), Is.Null, "query never enters the durable mutation slot");
                    screen.GetComponentsInChildren<Button>().Single(button => button.name == "GrowthUlt:" + actorId).onClick.Invoke();
                    yield return Until(() => screen.GetComponentsInChildren<RectTransform>().Any(rect => rect.name == "GrowthUltimateCutin"), "lethal cutin survives the terminal redraw");
                    Assert.That(screen.GetComponentsInChildren<Text>().Any(text => text.text == "훈련 완료"), Is.True);
                    Assert.That(host.Posts.Count(seen => seen.Path.EndsWith("/commands", StringComparison.Ordinal)), Is.EqualTo(1));
                    Assert.That(Outbox().Load(), Is.Null); Assert.That(owner.IsAuthenticated, Is.True);
                }
                finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); MemoryForgeUiMotion.Reduced = reduced; }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AccountBattleKeepsItsIllustrationsAndLethalCutinWhenTheRewardViewReplacesIt()
        {
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = false;
            AccountUiFixture(true);
            var battle = Battle(world); battle["gaugeMax"] = 100;
            var actor = (JObject)battle["actors"]![0]!; string actorId = actor["id"]!.Value<string>()!; actor["gauge"] = 100;
            foreach (var enemy in battle["enemies"]!.OfType<JObject>()) { enemy["id"] = "e_moth"; enemy["name"] = "종탑 나방"; enemy["alive"] = true; }
            world.Extra = new JObject { ["battle"] = battle };
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                MemoryForgeAccountConnection.SetCurrent(owner);
                var screen = new GameObject("IllustratedAccountBattle"); screen.SetActive(false);
                var view = screen.AddComponent<MemoryForgeServerPracticeClient>(); screen.SetActive(true);
                try
                {
                    yield return Settled(view);
                    Assert.That(host.Posts, Is.Empty);
                    Assert.That(screen.GetComponentsInChildren<Image>().Any(image => image.name == "EnemyIllustration" && image.sprite != null), Is.True);
                    string? shots = Environment.GetEnvironmentVariable("MF_ACCOUNT_UI_SHOTS");
                    if (!string.IsNullOrEmpty(shots))
                    {
                        Directory.CreateDirectory(shots!);
                        yield return LobbyMomentCapturePlayModeTests.Shot(screen, Path.Combine(shots!, "account-battle-first.jpg"), MemoryForgeServerPracticeClient.OwnedCanvas);
                    }
                    post = seen =>
                    {
                        Assert.That(JObject.Parse(seen.Text)["kind"]!.Value<string>(), Is.EqualTo("ult"));
                        world.Version++; world.Phase = "REWARD"; world.InBattle = false; world.Extra = null;
                        return Reply.Json(200, RunJson(world, world.Version, Events()));
                    };
                    var use = view.UltAsync(actorId);
                    yield return Until(() => use.IsCompleted, "confirmed account ultimate");
                    Assert.That(use.Result, Is.True);
                    yield return Until(() => screen.GetComponentsInChildren<RectTransform>().Any(rect => rect.name == "AccountUltimateCutin"), "account cutin over replacement reward view");
                    Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("REWARD"));
                    Assert.That(Outbox().Load(), Is.Null); Assert.That(host.Posts.Count, Is.EqualTo(1));
                }
                finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); MemoryForgeUiMotion.Reduced = reduced; }
            }
            yield return null;
        }
    }
}
#endif
