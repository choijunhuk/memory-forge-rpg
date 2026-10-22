#nullable enable
#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
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
        void RunSupportUiFixture(bool used = false, bool selected = true)
        {
            MemoryForgeAccountConnection.SetCurrent(null); SupportFixture(false);
            var b = (JObject)world.Extra!["battle"]!; b["gaugeMax"] = 100;
            foreach (var e in b["enemies"]!.OfType<JObject>()) { e["alive"] = true; e["maxHp"] = 20; e["name"] = "검증 대상"; }
            if (!selected) b.Remove("support"); else b["support"]!["used"] = used;
        }
        JObject RunSupportUiQuery(Seen seen)
        {
            var body = JObject.Parse(seen.Text);
            return SupportQueryJson(false, body["caller"]!.Value<string>()!, body["target"]?.Value<string>(),
                body["picks"]?.Values<string>().Select(x => x!).ToArray() ?? Array.Empty<string>());
        }
        static JArray RunSupportUiEvents() => new JArray(new JObject { ["e"] = "support", ["actor"] = "nia", ["caller"] = "sera" },
            new JObject { ["e"] = "dmg", ["uid"] = "e1", ["n"] = 20, ["blocked"] = 0, ["src"] = "ult" });

        [UnityTest]
        public IEnumerator RunSupportPurePreviewConfirmsOnceAndKeepsPartnerCutinAcrossLethalBarrier()
        {
            RunSupportUiFixture(); int writes = 0; bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = false;
            post = seen =>
            {
                if (seen.Path.EndsWith("/support-query", StringComparison.Ordinal)) return Reply.Json(200, RunSupportUiQuery(seen));
                var body = JObject.Parse(seen.Text); Assert.That(body["kind"]!.Value<string>(), Is.EqualTo("support")); Assert.That(body["args"]!["caller"]!.Value<string>(), Is.EqualTo("sera"));
                Assert.That(body["args"]!["target"]!.Value<string>(), Is.EqualTo("e1")); writes++;
                world.Version++; world.Phase = "REWARD"; world.InBattle = false; world.Extra = null;
                return Reply.Json(200, RunJson(world, world.Version, RunSupportUiEvents()));
            };
            using (var owner = new MemoryForgeAccountConnection(host.Endpoint, Account, Credential, RecordPath))
            {
                MemoryForgeAccountConnection.SetCurrent(owner); var screen = new GameObject("RunSupportLethalUi"); screen.SetActive(false);
                var view = screen.AddComponent<MemoryForgeServerPracticeClient>(); screen.SetActive(true);
                try
                {
                    yield return Settled(view); Assert.That(host.Posts, Is.Empty);
                    var open = view.OpenSupportAsync(); yield return Until(() => open.IsCompleted && !view.Busy, "pure support preview");
                    Assert.That(view.SupportQueryView!.Source.CharId, Is.EqualTo("nia")); Assert.That(view.SupportQueryView.Source.GaugeCost, Is.EqualTo(100));
                    Assert.That(Outbox().Load(), Is.Null); Assert.That(writes, Is.Zero);
                    var target = view.SelectSupportTargetAsync("e1"); yield return Until(() => target.IsCompleted && view.CanConfirmSupport, "target bound support preview");
                    var sheet = screen.GetComponentsInChildren<RectTransform>().Single(r => r.name == "RunSupportSheet");
                    foreach (var button in sheet.GetComponentsInChildren<Button>().Where(b => b.GetComponentInParent<ScrollRect>() == null))
                    {
                        var rect = (RectTransform)button.transform; Assert.That(rect.rect.height, Is.GreaterThanOrEqualTo(144));
                        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                        Assert.That(corners.Min(p => sheet.InverseTransformPoint(p).x), Is.GreaterThanOrEqualTo(0)); Assert.That(corners.Max(p => sheet.InverseTransformPoint(p).x), Is.LessThanOrEqualTo(1600));
                        Assert.That(corners.Min(p => sheet.InverseTransformPoint(p).y), Is.GreaterThanOrEqualTo(-900)); Assert.That(corners.Max(p => sheet.InverseTransformPoint(p).y), Is.LessThanOrEqualTo(0));
                    }
                    var confirm = view.ConfirmSupportAsync(); var duplicate = view.ConfirmSupportAsync();
                    yield return Until(() => confirm.IsCompleted && duplicate.IsCompleted && !view.Busy, "one explicit support command");
                    Assert.That(confirm.Result, Is.True); Assert.That(duplicate.Result, Is.False); Assert.That(writes, Is.EqualTo(1));
                    Assert.That(view.PresentationBarrier, Is.True); Assert.That(view.PresentationRows.Any(r => r.Provenance == "지원" && r.Text.Contains("니아") && r.Text.Contains("세라")), Is.True);
                    Assert.That(screen.GetComponentsInChildren<RectTransform>().Any(r => r.name == "AccountSupportCutin"), Is.True);
                    Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("REWARD")); Assert.That(view.CanConfirmSupport, Is.False);
                    view.SkipPresentation(); Assert.That(view.PresentationBarrier, Is.True); view.ContinuePresentation();
                    Assert.That(screen.GetComponentsInChildren<RectTransform>().Any(r => r.name == "AccountSupportCutin"), Is.True, "cutin is outside the replaced result canvas");
                    Assert.That(host.Posts.Count(p => p.Path.EndsWith("/commands", StringComparison.Ordinal)), Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
                }
                finally { Object.Destroy(screen); MemoryForgeAccountConnection.SetCurrent(null); MemoryForgeUiMotion.Reduced = reduced; }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator RunSupportMandatoryServerChoicesCannotBeSkippedOrForged()
        {
            RunSupportUiFixture();
            post = seen =>
            {
                Assert.That(seen.Path, Does.EndWith("/support-query")); var q = RunSupportUiQuery(seen); bool picked = ((JArray)q["source"]!["picks"]!).Count == 1;
                q["choices"] = new JObject { ["error"] = JValue.CreateNull(), ["requests"] = new JArray(new JObject { ["label"] = "앞당길 예약", ["from"] = "res", ["count"] = 1,
                    ["growthOnly"] = false, ["op"] = "pull", ["part"] = JValue.CreateNull(), ["leading"] = true }), ["accepted"] = picked ? 1 : 0, ["index"] = picked ? 1 : 0, ["left"] = picked ? 0 : 1,
                    ["blocked"] = JValue.CreateNull(), ["candidates"] = picked ? new JArray() : new JArray(new JObject { ["uid"] = "r1", ["id"] = "guard", ["zone"] = "res", ["plus"] = false,
                        ["mods"] = new JObject { ["c"] = JValue.CreateNull(), ["e"] = JValue.CreateNull() }, ["temp"] = new JArray(), ["growth"] = 0 }) };
                return Reply.Json(200, q);
            };
            var screen = new GameObject("RunSupportChoices"); var view = screen.AddComponent<MemoryForgeServerPracticeClient>(); view.Connect(() => Session());
            try
            {
                yield return Settled(view); var open = view.OpenSupportAsync(); yield return Until(() => open.IsCompleted && !view.Busy, "support choice sheet");
                Assert.That(view.CanConfirmSupport, Is.False); var invalid = view.PickSupportCardAsync("invented"); var refused = view.ConfirmSupportAsync();
                yield return Until(() => invalid.IsCompleted && refused.IsCompleted, "choice refusals"); Assert.That(host.Posts.Length, Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
                var picked = view.PickSupportCardAsync("r1"); yield return Until(() => picked.IsCompleted && view.CanConfirmSupport, "actual reservation candidate accepted");
                Assert.That(view.SupportPicks, Is.EqualTo(new[] { "r1" }));
                world.Version++; world.BattleV++; world.Extra!["battle"]!["v"] = world.BattleV;
                var refreshed = view.RefreshAsync(); yield return Until(() => refreshed.IsCompleted && !view.Busy, "new battle context drops unsent support");
                Assert.That(view.SupportSheetOpen, Is.False); Assert.That(view.SupportPicks, Is.Empty); Assert.That(view.CanConfirmSupport, Is.False);
                Assert.That(host.Posts.All(p => p.Path.EndsWith("/support-query", StringComparison.Ordinal)), Is.True); Assert.That(Outbox().Load(), Is.Null);
            }
            finally { Object.Destroy(screen); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator RunSupportUsedMalformedAndAbsentStatesNeverSendACommand()
        {
            RunSupportUiFixture(used: true);
            post = seen => { var q = RunSupportUiQuery(seen); q["source"]!["used"] = true; q["source"]!["ready"] = false; q["source"]!["code"] = "SUPPORT_ALREADY_USED"; return Reply.Json(200, q); };
            var screen = new GameObject("RunSupportUnavailable"); var view = screen.AddComponent<MemoryForgeServerPracticeClient>(); view.Connect(() => Session());
            try
            {
                yield return Settled(view); var open = view.OpenSupportAsync(); yield return Until(() => open.IsCompleted && !view.Busy, "used support read");
                Assert.That(view.SupportQueryView!.Source.Used, Is.True); Assert.That(view.CanConfirmSupport, Is.False); Assert.That(view.ConfirmSupportAsync().Result, Is.False);
                view.CancelSupport();
                post = seen => { var q = RunSupportUiQuery(seen); q["source"]!["caller"] = "wrong-caller"; return Reply.Json(200, q); };
                var malformed = view.OpenSupportAsync(); yield return Until(() => malformed.IsCompleted && !view.Busy, "malformed support response");
                Assert.That(view.SupportQueryView, Is.Null); Assert.That(view.CanConfirmSupport, Is.False); view.SendMessage("OnApplicationPause", true);
                Assert.That(view.SupportSheetOpen, Is.False); Assert.That(Outbox().Load(), Is.Null);
                Assert.That(host.Posts.All(p => p.Path.EndsWith("/support-query", StringComparison.Ordinal)), Is.True);
            }
            finally { Object.Destroy(screen); }
            yield return null;
            RunSupportUiFixture(selected: false); var absent = new GameObject("RunSupportAbsent"); var noSupport = absent.AddComponent<MemoryForgeServerPracticeClient>(); noSupport.Connect(() => Session());
            try { yield return Settled(noSupport); Assert.That(noSupport.OpenSupportAsync().Result, Is.False); Assert.That(noSupport.SupportSheetOpen, Is.False); }
            finally { Object.Destroy(absent); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator RunSupportLostReplyRetriesOnlyOneExactActionWithoutReplayingItsCutin()
        {
            RunSupportUiFixture(); byte[]? saved = null; int writes = 0; string? request = null;
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = false;
            post = seen =>
            {
                if (seen.Path.EndsWith("/support-query", StringComparison.Ordinal)) return Reply.Json(200, RunSupportUiQuery(seen));
                if (saved == null)
                {
                    request = seen.Text; writes++; world.Version++; world.Phase = "REWARD"; world.InBattle = false; world.Extra = null;
                    saved = Utf8(RunJson(world, world.Version, RunSupportUiEvents())); return new Reply(200, saved.Take(saved.Length / 2).ToArray());
                }
                Assert.That(seen.Text, Is.EqualTo(request)); return new Reply(200, saved);
            };
            var screen = new GameObject("RunSupportPending"); var view = screen.AddComponent<MemoryForgeServerPracticeClient>(); view.Connect(() => Session());
            try
            {
                yield return Settled(view); var open = view.OpenSupportAsync(); yield return Until(() => open.IsCompleted && !view.Busy, "support query");
                var action = view.ConfirmSupportAsync(); yield return Until(() => action.IsCompleted && !view.Busy, "unresolved support command");
                Assert.That(action.Result, Is.False); Assert.That(Outbox().Load()!.Body, Is.EqualTo(request)); Assert.That(writes, Is.EqualTo(1));
                Assert.That(host.Posts.Count(p => p.Path.EndsWith("/commands", StringComparison.Ordinal)), Is.EqualTo(1));
                Assert.That(view.PresentationBarrier, Is.False);
                Assert.That(screen.GetComponentsInChildren<RectTransform>().Any(r => r.name == "AccountSupportCutin"), Is.False, "an unread reply starts no cutin");
                var retry = view.RetryAsync(); yield return Until(() => retry.IsCompleted && !view.Busy, "explicit exact support retry");
                Assert.That(retry.Result, Is.True); Assert.That(writes, Is.EqualTo(1)); Assert.That(Outbox().Load(), Is.Null);
                Assert.That(host.Posts.Count(p => p.Path.EndsWith("/commands", StringComparison.Ordinal)), Is.EqualTo(2));
                Assert.That(host.Posts.Where(p => p.Path.EndsWith("/commands", StringComparison.Ordinal)).Select(p => p.Text).Distinct().Count(), Is.EqualTo(1));
                Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("REWARD"));
                Assert.That(view.PresentationBarrier, Is.True, "retry resolution keeps the terminal Continue barrier");
                Assert.That(screen.GetComponentsInChildren<RectTransform>().Any(r => r.name == "AccountSupportCutin"), Is.False);
            }
            finally { Object.Destroy(screen); MemoryForgeUiMotion.Reduced = reduced; }
            yield return null;
        }
    }
}
#endif
