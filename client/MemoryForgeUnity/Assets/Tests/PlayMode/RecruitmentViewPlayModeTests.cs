#nullable enable
#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    // Protocol fixtures exercise actual HTTP/uGUI and presentation only, not probability or real server acceptance.
    public partial class PracticeServerSessionPlayModeTests
    {
        [UnityTest]
        public IEnumerator RecruitmentConfirmationSkipAndOwnedRosterUseOneCommittedRequest() => RecruitmentViewFlow(false, false);

        [UnityTest]
        public IEnumerator ReducedRecruitmentMotionShowsTheSameCommittedResult() => RecruitmentViewFlow(false, true);

        [UnityTest, Explicit("writes protocol-fixture screenshots with MF_EXPEDITION_SHOTS")]
        public IEnumerator CaptureRecruitmentFlow() => RecruitmentViewFlow(true, false);

        [UnityTest]
        public IEnumerator TenRecruitmentResultsRemainOneRequest() => RecruitmentViewFlow(false, false, 10);

        [UnityTest, Explicit("writes ten-pull protocol screenshots with MF_EXPEDITION_SHOTS")]
        public IEnumerator CaptureTenRecruitmentFlow() => RecruitmentViewFlow(true, false, 10);

        IEnumerator RecruitmentViewFlow(bool capture, bool reduced, int count = 1)
        {
            string? dir = Environment.GetEnvironmentVariable("MF_EXPEDITION_SHOTS");
            if (capture && string.IsNullOrEmpty(dir)) { Assert.Ignore("MF_EXPEDITION_SHOTS not set"); yield break; }
            if (capture) Directory.CreateDirectory(dir!);
            RecruitmentFixture(); recruitment["launchClaimed"] = true;
            post = seen =>
            {
                var receipt = RecruitmentReceiptJson(seen, count: count);
                var committed = (JObject)receipt["snapshot"]!;
                committed["launchClaimed"] = true; committed["ownedChars"] = new JObject { ["sera"] = count };
                committed["wallet"]!["tickets"]!["standardTicket"] = 10 - count;
                committed["history"] = new JArray(new JObject { ["requestId"] = receipt["requestId"]!.DeepClone(), ["bannerId"] = "standard", ["results"] = receipt["results"]!.DeepClone() });
                recruitment = (JObject)committed.DeepClone();
                return Reply.Json(200, receipt);
            };
            bool before = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = reduced;
            var obj = new GameObject("RecruitmentViewFixture"); var lobby = obj.AddComponent<MemoryForgeLobbyClient>();
            Button Find(string name) => obj.GetComponentsInChildren<Button>().Single(b => b.name == name);
            try
            {
                yield return null; lobby.Player!.Stop(); yield return null;
                Find("OpenRecruitment").onClick.Invoke();
                Assert.That(lobby.ConnectRecruitment(() => Session()), Is.True);
                yield return Until(() => obj.GetComponentsInChildren<Button>().Any(b => b.name == "RecruitOne" && b.interactable), "recruitment snapshot applied");
                if (capture) yield return LobbyMomentCapturePlayModeTests.Shot(obj, Path.Combine(dir!, count == 10 ? "recruitment-ten-connected.jpg" : "recruitment-connected.jpg"));
                Find(count == 10 ? "RecruitTen" : "RecruitOne").onClick.Invoke(); yield return null;
                Assert.That(host.Seen.Count(s => s.Method == "POST"), Is.Zero, "confirmation alone never spends");
                if (capture) yield return LobbyMomentCapturePlayModeTests.Shot(obj, Path.Combine(dir!, count == 10 ? "recruitment-ten-confirm.jpg" : "recruitment-confirm.jpg"));
                Find("ConfirmRecruitment").onClick.Invoke();
                yield return Until(() => obj.GetComponentsInChildren<Button>().Any(b => b.name == "SkipRecruitmentReveal" || b.name == "RecruitmentResultDone"), "validated committed receipt presented");
                if (!reduced) { Find("SkipRecruitmentReveal").onClick.Invoke(); yield return null; }
                yield return Until(() => obj.GetComponentsInChildren<Button>().Any(b => b.name == "RecruitmentResultDone"), "result overview after skip");
                Assert.That(host.Seen.Count(s => s.Method == "POST"), Is.EqualTo(1), "skip/reduced motion never rerolls");
                Assert.That(obj.GetComponentsInChildren<Text>().Any(t => t.text == "세라"), Is.True);
                if (capture) yield return LobbyMomentCapturePlayModeTests.Shot(obj, Path.Combine(dir!, count == 10 ? "recruitment-ten-results.jpg" : "recruitment-results.jpg"));
                Find("RecruitmentResultRoster").onClick.Invoke(); yield return null;
                Assert.That(obj.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Companion:")).Select(b => b.name), Is.EqualTo(new[] { "Companion:sera" }));
                Assert.That(host.Seen.Count(s => s.Method == "POST"), Is.EqualTo(1));
            }
            finally { MemoryForgeUiMotion.Reduced = before; Object.Destroy(obj); }
        }
    }
}
#endif
