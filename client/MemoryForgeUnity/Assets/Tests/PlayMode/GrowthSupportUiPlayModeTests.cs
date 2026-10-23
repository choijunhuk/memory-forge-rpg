#nullable enable
#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
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
    // HTTP/uGUI fixture coverage only: these are not earned-account, Core combat or real reward proofs.
    public partial class PracticeServerSessionPlayModeTests
    {
        void GrowthSupportUiFixture(bool used = false, bool selectedSupport = true, int gauge = 100, bool twoEnemies = false, bool weekly = false)
        {
            MemoryForgeAccountConnection.SetCurrent(null); SupportFixture(true);
            if (weekly)
            {
                growthEncounters[GrowthEncounter] = WeeklyEncounterJson();
                growthEncounters[GrowthEncounter]["state"]!["support"] = SupportSummaryJson();
                var fallback = additionalReply;
                additionalReply = seen =>
                {
                    if (seen.Method == "GET" && seen.Path == ProgressionPath)
                    {
                        var profile = (JObject)progressionProfile.DeepClone();
                        profile["activeEncounter"] = growthActive == null ? (JToken)JValue.CreateNull() : new JObject
                        { ["encounterId"] = growthActive, ["kind"] = "weeklyBoss", ["bossId"] = "b_bell", ["difficulty"] = 5, ["practice"] = false,
                            ["phase"] = growthEncounters[growthActive]["phase"]!.DeepClone(), ["version"] = growthEncounters[growthActive]["version"]!.DeepClone() };
                        return Reply.Json(200, profile);
                    }
                    return fallback!(seen);
                };
            }
            var state = (JObject)growthEncounters[GrowthEncounter]["state"]!;
            state["gaugeMax"] = 100; state["actors"]![0]!["gauge"] = gauge;
            foreach (var enemy in state["enemies"]!.OfType<JObject>())
            { enemy["alive"] = true; enemy["maxHp"] = 20; enemy["name"] = "검증 대상"; }
            if (twoEnemies)
            {
                var second = (JObject)state["enemies"]![0]!.DeepClone(); second["uid"] = "e2"; second["name"] = "두 번째 검증 대상";
                ((JArray)state["enemies"]!).Add(second);
            }
            if (!selectedSupport) state.Remove("support"); else state["support"]!["used"] = used;
        }
        static Button GrowthSupportButton(GameObject screen, string name) => screen.GetComponentsInChildren<Button>().Single(b => b.name == name);
        static IEnumerator GrowthSupportReady(MemoryForgeGrowthClient view) => Until(() => view.Snapshot != null && !view.Busy, "growth support account/encounter GET");
        JObject GrowthSupportUiQuery(Seen seen)
        {
            var intent = JObject.Parse(seen.Text);
            Assert.That(intent.Property("requestId"), Is.Null, "pure preview has no durable command identity");
            return SupportQueryJson(true, intent["caller"]!.Value<string>()!, intent["target"]?.Value<string>(),
                intent["picks"]?.Values<string>().Select(x => x!).ToArray() ?? Array.Empty<string>());
        }
        JObject GrowthSupportUiCommit(bool terminal)
        {
            int battleVersion = growthEncounters[GrowthEncounter]["state"]!["v"]!.Value<int>();
            var reply = growthEncounters[GrowthEncounter]["kind"]?.Value<string>() == "weeklyBoss"
                ? WeeklyEncounterJson(terminal ? "VICTORY" : "PLAYER", version: 4, revision: 4)
                : GrowthEncounterJson(4, 4, terminal ? "VICTORY" : "PLAYER", events: true);
            reply["events"] = RunSupportUiEvents(); reply["state"]!["support"] = SupportSummaryJson(true); reply["state"]!["v"] = battleVersion + 1;
            reply["state"]!["actors"]![0]!["gauge"] = 0;
            growthEncounters[GrowthEncounter] = reply; progressionProfile["revision"] = 4L;
            if (terminal) growthActive = null;
            return reply;
        }

        [UnityTest]
        public IEnumerator GrowthSupportPurePreviewRequiresOneExplicitCommandAndNeverAddsAPartyMember()
        {
            GrowthSupportUiFixture(); int writes = 0; bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            post = seen =>
            {
                if (seen.Path.EndsWith("/support-query", StringComparison.Ordinal)) return Reply.Json(200, GrowthSupportUiQuery(seen));
                var intent = JObject.Parse(seen.Text); Assert.That(intent["kind"]!.Value<string>(), Is.EqualTo("support"));
                Assert.That(intent["args"]!["caller"]!.Value<string>(), Is.EqualTo("sera")); Assert.That(intent["args"]!["target"]!.Value<string>(), Is.EqualTo("e1"));
                Assert.That(Outbox().Load()!.Body, Is.EqualTo(seen.Text)); writes++; return Reply.Json(200, GrowthSupportUiCommit(false));
            };
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                MemoryForgeAccountConnection.SetCurrent(owner); var screen = new GameObject("GrowthSupportOneAction"); var view = screen.AddComponent<MemoryForgeGrowthClient>();
                try
                {
                    yield return GrowthSupportReady(view); Assert.That(host.Posts, Is.Empty);
                    GrowthSupportButton(screen, "GrowthSupportEntry").onClick.Invoke(); Assert.That(host.Posts, Is.Empty, "opening a support sheet sends nothing");
                    GrowthSupportButton(screen, "GrowthSupportCaller:sera").onClick.Invoke();
                    yield return Until(() => !view.Busy && screen.GetComponentsInChildren<Button>().Any(b => b.name == "ConfirmGrowthSupport" && b.IsInteractable()), "pure support answer");
                    Assert.That(Outbox().Load(), Is.Null); Assert.That(writes, Is.Zero); Assert.That(host.Posts.Length, Is.EqualTo(1));
                    var confirm = GrowthSupportButton(screen, "ConfirmGrowthSupport"); confirm.onClick.Invoke(); confirm.onClick.Invoke();
                    yield return Until(() => view.Snapshot?.Version == 4 && !view.Busy, "confirmed support applied");
                    Assert.That(writes, Is.EqualTo(1)); Assert.That(view.Snapshot!.Support!.Used, Is.True);
                    var actors = (JArray)view.Snapshot.StateJObject()["actors"]!;
                    Assert.That(actors.Count, Is.EqualTo(1)); Assert.That(actors[0]!["id"]!.Value<string>(), Is.EqualTo("sera")); Assert.That(actors[0]!["gauge"]!.Value<int>(), Is.Zero);
                    Assert.That(GrowthSupportButton(screen, "GrowthSupportEntry").interactable, Is.False);
                    GrowthSupportButton(screen, "GrowthSupportEntry").onClick.Invoke(); Assert.That(writes, Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
                }
                finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); MemoryForgeUiMotion.Reduced = reduced; }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator GrowthSupportTargetAndMandatoryReservationPickMustBeCompletedBeforeConfirmation()
        {
            GrowthSupportUiFixture(twoEnemies: true);
            post = seen =>
            {
                Assert.That(seen.Path, Does.EndWith("/support-query")); var reply = GrowthSupportUiQuery(seen);
                bool hasTarget = reply["source"]!["target"]!.Type == JTokenType.String;
                bool hasPick = ((JArray)reply["source"]!["picks"]!).Count == 1;
                if (!hasTarget)
                {
                    reply["forecast"]!["error"] = "BAD_TARGET"; reply["forecast"]!["enemyHpLosses"] = new JArray();
                    foreach (string field in new[] { "enemyHpLoss", "enemyDamage", "newlyBroken", "blockDelta", "hpDelta" }) reply["forecast"]![field] = 0;
                }
                else reply["choices"] = new JObject { ["error"] = JValue.CreateNull(), ["requests"] = new JArray(new JObject { ["label"] = "앞당길 예약", ["from"] = "res", ["count"] = 1,
                    ["growthOnly"] = false, ["op"] = "pull", ["part"] = JValue.CreateNull(), ["leading"] = true }), ["accepted"] = hasPick ? 1 : 0,
                    ["index"] = hasPick ? 1 : 0, ["left"] = hasPick ? 0 : 1, ["blocked"] = JValue.CreateNull(), ["candidates"] = hasPick ? new JArray() : new JArray(new JObject
                    { ["uid"] = "r1", ["id"] = "guard", ["zone"] = "res", ["plus"] = false, ["mods"] = new JObject { ["c"] = JValue.CreateNull(), ["e"] = JValue.CreateNull() }, ["temp"] = new JArray(), ["growth"] = 0 }) };
                return Reply.Json(200, reply);
            };
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                MemoryForgeAccountConnection.SetCurrent(owner); var screen = new GameObject("GrowthSupportRequiredChoices"); var view = screen.AddComponent<MemoryForgeGrowthClient>();
                try
                {
                    yield return GrowthSupportReady(view); GrowthSupportButton(screen, "GrowthSupportEntry").onClick.Invoke(); GrowthSupportButton(screen, "GrowthSupportCaller:sera").onClick.Invoke();
                    yield return Until(() => !view.Busy && screen.GetComponentsInChildren<Button>().Any(b => b.name == "GrowthSupportTarget:e2"), "server target choices");
                    Assert.That(GrowthSupportButton(screen, "ConfirmGrowthSupport").interactable, Is.False);
                    GrowthSupportButton(screen, "ConfirmGrowthSupport").onClick.Invoke(); Assert.That(Outbox().Load(), Is.Null);
                    GrowthSupportButton(screen, "GrowthSupportTarget:e2").onClick.Invoke();
                    yield return Until(() => !view.Busy && screen.GetComponentsInChildren<Button>().Any(b => b.name == "GrowthSupportPick:r1"), "server reservation candidate");
                    Assert.That(GrowthSupportButton(screen, "ConfirmGrowthSupport").interactable, Is.False);
                    Assert.That(screen.GetComponentsInChildren<Button>().Any(b => b.name == "GrowthSupportPick:invented"), Is.False);
                    GrowthSupportButton(screen, "GrowthSupportPick:r1").onClick.Invoke();
                    yield return Until(() => !view.Busy && GrowthSupportButton(screen, "ConfirmGrowthSupport").IsInteractable(), "all mandatory choices satisfied");
                    Assert.That(JObject.Parse(host.Posts.Last().Text)["picks"]!.Values<string>(), Is.EqualTo(new[] { "r1" }));
                    GrowthSupportButton(screen, "CancelGrowthSupport").onClick.Invoke();
                    Assert.That(host.Posts.All(p => p.Path.EndsWith("/support-query", StringComparison.Ordinal)), Is.True); Assert.That(Outbox().Load(), Is.Null);
                }
                finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator GrowthSupportUsedAbsentUnder100AndForeignPendingCannotConfirm()
        {
            foreach (string state in new[] { "used", "absent", "under100", "pending" })
            {
                GrowthSupportUiFixture(used: state == "used", selectedSupport: state != "absent", gauge: state == "under100" ? 99 : 100);
                int postsBefore = host.Posts.Length; byte[]? pendingBytes = null;
                if (state == "pending")
                {
                    Outbox().Stage(PracticeCommandOutbox.CharacterLevelUpRoute("nia"), "{\"requestId\":\"growth-support-foreign-pending\",\"expectedRevision\":3,\"targetLevel\":2}");
                    pendingBytes = File.ReadAllBytes(RecordPath);
                }
                using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
                {
                    MemoryForgeAccountConnection.SetCurrent(owner); var screen = new GameObject("GrowthSupportUnavailable:" + state); var view = screen.AddComponent<MemoryForgeGrowthClient>();
                    try
                    {
                        yield return GrowthSupportReady(view);
                        var entry = screen.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "GrowthSupportEntry");
                        if (state == "under100")
                        {
                            Assert.That(entry, Is.Not.Null); entry!.onClick.Invoke();
                            Assert.That(GrowthSupportButton(screen, "GrowthSupportCaller:sera").interactable, Is.False);
                            Assert.That(GrowthSupportButton(screen, "ConfirmGrowthSupport").interactable, Is.False);
                            GrowthSupportButton(screen, "GrowthSupportCaller:sera").onClick.Invoke(); GrowthSupportButton(screen, "ConfirmGrowthSupport").onClick.Invoke();
                        }
                        else { Assert.That(entry == null || !entry.interactable, Is.True, state); if (entry != null) entry.onClick.Invoke(); }
                        Assert.That(host.Posts.Length, Is.EqualTo(postsBefore));
                        if (pendingBytes != null) Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(pendingBytes)); else Assert.That(Outbox().Load(), Is.Null);
                    }
                    finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); }
                }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator GrowthSupportStaleQueryAndFailedFreshProfileCannotAuthorizeAnAction()
        {
            GrowthSupportUiFixture(); post = seen => { var reply = GrowthSupportUiQuery(seen); reply["version"] = 4; return Reply.Json(200, reply); };
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                MemoryForgeAccountConnection.SetCurrent(owner); var screen = new GameObject("GrowthSupportStale"); var view = screen.AddComponent<MemoryForgeGrowthClient>();
                try
                {
                    yield return GrowthSupportReady(view); GrowthSupportButton(screen, "GrowthSupportEntry").onClick.Invoke(); GrowthSupportButton(screen, "GrowthSupportCaller:sera").onClick.Invoke();
                    yield return Until(() => !view.Busy && host.Posts.Length == 1, "stale pure query rejected");
                    Assert.That(GrowthSupportButton(screen, "ConfirmGrowthSupport").interactable, Is.False); GrowthSupportButton(screen, "ConfirmGrowthSupport").onClick.Invoke();
                    GrowthSupportButton(screen, "CancelGrowthSupport").onClick.Invoke();
                    var original = additionalReply;
                    additionalReply = seen => seen.Method == "GET" && seen.Path == ProgressionPath ? Error(503, "STORE_UNAVAILABLE") : original!(seen);
                    GrowthSupportButton(screen, "GrowthRefresh").onClick.Invoke();
                    yield return Until(() => !view.Busy && screen.GetComponentsInChildren<Text>().Any(t => t.text.Contains("계정 진행을 확인하지 못")), "failed profile GET blocks support");
                    Assert.That(GrowthSupportButton(screen, "GrowthSupportEntry").interactable, Is.False); Assert.That(Outbox().Load(), Is.Null);
                    Assert.That(host.Posts.All(p => p.Path.EndsWith("/support-query", StringComparison.Ordinal)), Is.True);
                }
                finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator GrowthSupportWeeklyLethalPartnerCutinSurvivesResultRedrawAndTerminalCannotRepeat()
        {
            GrowthSupportUiFixture(weekly: true); int writes = 0; bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = false;
            post = seen =>
            {
                if (seen.Path.EndsWith("/support-query", StringComparison.Ordinal)) return Reply.Json(200, GrowthSupportUiQuery(seen));
                Assert.That(JObject.Parse(seen.Text)["args"]!["caller"]!.Value<string>(), Is.EqualTo("sera")); writes++;
                return Reply.Json(200, GrowthSupportUiCommit(true));
            };
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                MemoryForgeAccountConnection.SetCurrent(owner); var screen = new GameObject("GrowthSupportLethal"); var view = screen.AddComponent<MemoryForgeGrowthClient>();
                try
                {
                    yield return GrowthSupportReady(view); GrowthSupportButton(screen, "GrowthSupportEntry").onClick.Invoke(); GrowthSupportButton(screen, "GrowthSupportCaller:sera").onClick.Invoke();
                    yield return Until(() => !view.Busy && GrowthSupportButton(screen, "ConfirmGrowthSupport").IsInteractable(), "support confirmation ready");
                    GrowthSupportButton(screen, "ConfirmGrowthSupport").onClick.Invoke();
                    yield return Until(() => !view.Busy && view.Snapshot?.Phase == "VICTORY", "lethal support settled");
                    Assert.That(screen.GetComponentsInChildren<RectTransform>().Any(r => r.name == "GrowthUltimateCutin"), Is.True);
                    var cutin = screen.GetComponentsInChildren<RectTransform>().Single(r => r.name == "GrowthUltimateCutin");
                    var words = cutin.GetComponentsInChildren<Text>().Single(t => t.name == "Words").text.Split('\n');
                    Assert.That(words[0], Is.EqualTo("니아"), "cutin is the supporter, not the deployed caller");
                    Assert.That(words.Length, Is.EqualTo(2)); Assert.That(words[1], Is.Not.Empty);
                    Assert.That(screen.GetComponentsInChildren<Text>().Any(t => t.text == "주간 보스 격파"), Is.True);
                    Assert.That(screen.GetComponentsInChildren<Button>().Any(b => b.name == "GrowthPlay" || b.name == "ConfirmGrowthSupport"), Is.False);
                    var entry = screen.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "GrowthSupportEntry");
                    Assert.That(entry == null || !entry.interactable, Is.True); if (entry != null) entry.onClick.Invoke();
                    Assert.That(writes, Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
                }
                finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); MemoryForgeUiMotion.Reduced = reduced; }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator GrowthSupportLostReplyRetriesOneExactCommandWithoutReplayingPartnerCutin()
        {
            GrowthSupportUiFixture(); byte[]? saved = null; string? originalBody = null; int writes = 0;
            post = seen =>
            {
                if (seen.Path.EndsWith("/support-query", StringComparison.Ordinal)) return Reply.Json(200, GrowthSupportUiQuery(seen));
                if (saved == null) { originalBody = seen.Text; writes++; saved = Utf8(GrowthSupportUiCommit(true)); return new Reply(200, saved.Take(saved.Length / 2).ToArray()); }
                Assert.That(seen.Text, Is.EqualTo(originalBody)); return new Reply(200, saved);
            };
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                MemoryForgeAccountConnection.SetCurrent(owner); var screen = new GameObject("GrowthSupportRetry"); var view = screen.AddComponent<MemoryForgeGrowthClient>();
                try
                {
                    yield return GrowthSupportReady(view); GrowthSupportButton(screen, "GrowthSupportEntry").onClick.Invoke(); GrowthSupportButton(screen, "GrowthSupportCaller:sera").onClick.Invoke();
                    yield return Until(() => !view.Busy && GrowthSupportButton(screen, "ConfirmGrowthSupport").IsInteractable(), "support confirmation ready");
                    GrowthSupportButton(screen, "ConfirmGrowthSupport").onClick.Invoke();
                    yield return Until(() => !view.Busy && screen.GetComponentsInChildren<Button>().Any(b => b.name == "GrowthRetry"), "unknown support result preserved");
                    Assert.That(Outbox().Load()!.Body, Is.EqualTo(originalBody)); Assert.That(host.Posts.Count(p => p.Path.EndsWith("/commands", StringComparison.Ordinal)), Is.EqualTo(1));
                    GrowthSupportButton(screen, "GrowthRetry").onClick.Invoke();
                    yield return Until(() => !view.Busy && view.Snapshot?.Phase == "VICTORY", "same request resolved");
                    Assert.That(writes, Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
                    Assert.That(screen.GetComponentsInChildren<RectTransform>().Any(r => r.name == "GrowthUltimateCutin"), Is.False);
                    Assert.That(host.Posts.Where(p => p.Path.EndsWith("/commands", StringComparison.Ordinal)).Select(p => p.Text).Distinct().Count(), Is.EqualTo(1));
                }
                finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); }
            }
            yield return null;
        }
    }
}
#endif
