// Regression checks written after the independent review fixes. Protocol fixture + actual uGUI; not Core or real-server acceptance.
#nullable enable
#if UNITY_5_3_OR_NEWER
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public partial class PracticeServerSessionPlayModeTests
    {
        static Button ReviewButton(GameObject obj, string name) => obj.GetComponentsInChildren<Button>().Single(b => b.name == name);
        static string ReviewText(GameObject obj) => string.Join("\n", obj.GetComponentsInChildren<Text>().Select(t => t.text));
        static bool ReviewPanelOpen(GameObject obj, string name) => obj.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == name).gameObject.activeInHierarchy;

        [UnityTest]
        public IEnumerator RecruitmentUnreadableOrOtherOwnerOutboxExplainsTheBlockAndPreservesBytes()
        {
            RecruitmentFixture(); recruitment["launchClaimed"] = true;
            foreach (bool wrongOwner in new[] { true, false })
            {
                if (File.Exists(RecordPath)) File.Delete(RecordPath);
                if (wrongOwner)
                    new PracticeCommandOutbox(RecordPath, host.Endpoint, OtherAccount).Stage(PracticeCommandOutbox.RecruitmentPullRoute,
                        "{\"requestId\":\"other-owner-pull\",\"expectedRevision\":3,\"bannerId\":\"standard\",\"count\":1}");
                else File.WriteAllText(RecordPath, "{broken-record");
                byte[] before = File.ReadAllBytes(RecordPath);
                var obj = new GameObject("RecruitmentStorageReview"); var lobby = obj.AddComponent<MemoryForgeLobbyClient>();
                var session = Session();
                try
                {
                    yield return null; lobby.Player!.Stop(); yield return null;
                    ReviewButton(obj, "OpenRecruitment").onClick.Invoke();
                    Assert.That(lobby.ConnectRecruitment(() => session), Is.True);
                    string explanation = wrongOwner ? "이전 주소 또는 계정의 요청이 보존" : "요청 기록을 안전하게 읽지 못했어요";
                    yield return Until(() => ReviewText(obj).Contains(explanation), "storage block is explained after the successful account GET");
                    Assert.That(session.Recruitment, Is.Not.Null, "account reads remain available while the local request record is blocked");
                    Assert.That(ReviewText(obj), Does.Contain("개발 계정 · " + Account));
                    Assert.That(ReviewText(obj), Does.Not.Contain("OUTBOX_"));
                    Assert.That(ReviewButton(obj, "RecruitOne").interactable, Is.False);
                    Assert.That(ReviewButton(obj, "RecruitTen").interactable, Is.False);
                    Assert.That(host.Posts, Is.Empty);
                    Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(before));
                }
                finally { Object.Destroy(obj); }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator RecruitmentBeginnerAchievementAndCompletionDoNotInventAnotherGuarantee()
        {
            RecruitmentFixture(); recruitment["launchClaimed"] = true;
            var banner = (JObject)recruitment["banners"]![0]!;
            banner["id"] = "beginner"; banner["kind"] = "beginner"; banner["name"] = "첫 공방 모집";
            banner["curve"]!["maxPulls"] = 50; banner["curve"]!["hard"] = 50; banner["curve"]!["softStart"] = 66;
            recruitment["pity"]!["beginner"]!["gotSsr"] = true; recruitment["pity"]!["beginner"]!["total"] = 20;
            var obj = new GameObject("RecruitmentBeginnerReview"); var lobby = obj.AddComponent<MemoryForgeLobbyClient>(); var session = Session();
            try
            {
                yield return null; lobby.Player!.Stop(); yield return null;
                ReviewButton(obj, "OpenRecruitment").onClick.Invoke(); Assert.That(lobby.ConnectRecruitment(() => session), Is.True);
                yield return Until(() => ReviewText(obj).Contains("SSR 획득 완료 · 남은 30회"), "beginner uses total pulls and its already acquired SSR");
                Assert.That(ReviewText(obj), Does.Not.Contain("SSR 1명 확정까지"));
                Assert.That(ReviewButton(obj, "RecruitOne").interactable, Is.False);
                ReviewButton(obj, "RecruitmentRates").onClick.Invoke(); yield return null;
                string rates = ReviewText(obj);
                Assert.That(rates, Does.Contain("SSR 종합 확률 (천장 포함): 2.1385%"), "finite50-pull window: (1 + .008 * (50 - (1 - .992^50)/.008))/50");
                Assert.That(rates, Does.Contain("세라  0.8%"));
                Assert.That(rates, Does.Contain("니아  3%"));
                Assert.That(rates, Does.Contain("잔향 기록: 경험치  93.2%"));
                Assert.That(rates, Does.Not.Contain("66회"), "a soft-pity section beyond the hard cap is not advertised");
                ReviewButton(obj, "CloseRecruitmentRates").onClick.Invoke();
                recruitment["revision"] = 4;
                recruitment["pity"]!["beginner"]!["done"] = true; recruitment["pity"]!["beginner"]!["total"] = 50;
                ReviewButton(obj, "RefreshRecruitment").onClick.Invoke();
                yield return Until(() => ReviewText(obj).Contains("첫 공방 모집 완료"), "completed beginner banner is labelled complete");
                Assert.That(ReviewButton(obj, "RecruitTen").interactable, Is.False);
                Assert.That(host.Posts, Is.Empty);
            }
            finally { Object.Destroy(obj); }
        }

        [UnityTest]
        public IEnumerator RecruitmentLateReplyLeavesRosterAndRecentResultClosesWithKeyboardFocus() => LateRecruitmentReply(false);

        [UnityTest]
        public IEnumerator RecruitmentLateReceiptRemainsVisibleWhenItsRefreshFails() => LateRecruitmentReply(true);

        [UnityTest]
        public IEnumerator RecruitmentStaleRefreshKeepsNewerHistoryInsteadOfAnOlderCachedReceipt()
        {
            RecruitmentFixture(); recruitment["launchClaimed"] = true;
            post = seen =>
            {
                var receipt = RecruitmentReceiptJson(seen);
                receipt["snapshot"]!["launchClaimed"] = true;
                recruitment = (JObject)receipt["snapshot"]!.DeepClone();
                return Reply.Json(200, receipt);
            };
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            var obj = new GameObject("RecruitmentNewestHistoryReview"); var lobby = obj.AddComponent<MemoryForgeLobbyClient>(); var session = Session();
            try
            {
                yield return null; lobby.Player!.Stop(); yield return null;
                ReviewButton(obj, "OpenRecruitment").onClick.Invoke(); Assert.That(lobby.ConnectRecruitment(() => session), Is.True);
                yield return Until(() => obj.GetComponentsInChildren<Button>().Any(b => b.name == "RecruitOne" && b.interactable), "account is ready");
                ReviewButton(obj, "RecruitOne").onClick.Invoke(); ReviewButton(obj, "ConfirmRecruitment").onClick.Invoke();
                yield return Until(() => obj.GetComponentsInChildren<Button>().Any(b => b.name == "RecruitmentResultDone"), "older Sera receipt is cached");
                ReviewButton(obj, "RecruitmentResultDone").onClick.Invoke();
                recruitment["revision"] = 5;
                var newer = RecruitmentPullJson(); newer["rarity"] = "SR"; newer["id"] = "nia";
                recruitment["history"] = new JArray(new JObject { ["requestId"] = "newer-nia-pull", ["bannerId"] = "standard", ["results"] = new JArray(newer) });
                ReviewButton(obj, "RefreshRecruitment").onClick.Invoke();
                yield return Until(() => session.Recruitment?.Revision == 5 && !session.State.InFlight, "newer history replaces the server snapshot");
                yield return null;
                additionalReply = seen => seen.Method == "GET" && seen.Path == RecruitmentRoute ? Error(503, "STORE_UNAVAILABLE") : null;
                ReviewButton(obj, "RefreshRecruitment").onClick.Invoke();
                yield return Until(() => session.RecruitmentNeedsRefresh && !session.State.InFlight, "a failed refresh marks the newer snapshot stale");
                yield return null;
                ReviewButton(obj, "RecruitmentLastResult").onClick.Invoke();
                yield return Until(() => obj.GetComponentsInChildren<Button>().Any(b => b.name == "RecruitmentResultDone"), "latest known result is displayed");
                var reveal = obj.GetComponentsInChildren<RectTransform>().Single(panel => panel.name == "RecruitmentReveal");
                string resultText = string.Join("\n", reveal.GetComponentsInChildren<Text>().Select(text => text.text));
                Assert.That(resultText, Does.Contain("니아"));
                Assert.That(resultText, Does.Not.Contain("세라"), "a revision 4 cached receipt cannot hide revision 5 history");
                Assert.That(host.Posts.Length, Is.EqualTo(1), "history refresh and viewing do not spend currency");
            }
            finally { MemoryForgeUiMotion.Reduced = reduced; Object.Destroy(obj); }
        }

        IEnumerator LateRecruitmentReply(bool failRefresh)
        {
            RecruitmentFixture(); recruitment["launchClaimed"] = true;
            var entered = new TaskCompletionSource<bool>(); var release = new TaskCompletionSource<bool>();
            post = seen =>
            {
                var receipt = RecruitmentReceiptJson(seen);
                var snapshot = (JObject)receipt["snapshot"]!;
                snapshot["launchClaimed"] = true; snapshot["ownedChars"] = new JObject { ["sera"] = 1 };
                snapshot["history"] = new JArray(new JObject { ["requestId"] = receipt["requestId"]!.DeepClone(), ["bannerId"] = "standard", ["results"] = receipt["results"]!.DeepClone() });
                recruitment = (JObject)snapshot.DeepClone(); entered.TrySetResult(true);
                return new Reply(200, Utf8(receipt)) { Hold = release.Task };
            };
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            var obj = new GameObject("RecruitmentLateReplyReview"); var lobby = obj.AddComponent<MemoryForgeLobbyClient>(); var session = Session();
            try
            {
                yield return null; lobby.Player!.Stop(); yield return null;
                ReviewButton(obj, "OpenRecruitment").onClick.Invoke(); Assert.That(lobby.ConnectRecruitment(() => session), Is.True);
                yield return Until(() => obj.GetComponentsInChildren<Button>().Any(b => b.name == "RecruitOne" && b.interactable), "account is ready");
                ReviewButton(obj, "RecruitOne").onClick.Invoke(); ReviewButton(obj, "ConfirmRecruitment").onClick.Invoke();
                yield return Until(() => entered.Task.IsCompleted, "pull is committed by the fixture while its reply is held");
                ReviewButton(obj, "CloseRecruitment").onClick.Invoke();
                ReviewButton(obj, "OpenCompanions").onClick.Invoke();
                Assert.That(ReviewPanelOpen(obj, "RosterPanel"), Is.True);
                if (failRefresh) additionalReply = seen => seen.Method == "GET" && seen.Path == RecruitmentRoute ? Error(503, "STORE_UNAVAILABLE") : null;
                release.TrySetResult(true);
                yield return Until(() => !session.State.InFlight, "committed reply and its follow-up refresh settle");
                yield return null;
                Assert.That(ReviewPanelOpen(obj, "RosterPanel"), Is.True, "late recruitment never takes over navigation");
                Assert.That(ReviewPanelOpen(obj, "RecruitmentReveal"), Is.False);
                Assert.That(lobby.Player!.Playing, Is.False, "no moment is started behind the roster");
                if (failRefresh) Assert.That(session.RecruitmentNeedsRefresh, Is.True);
                else Assert.That(session.Recruitment!.OwnedChars["sera"], Is.EqualTo(1));
                Assert.That(host.Posts.Length, Is.EqualTo(1));
                ReviewButton(obj, "CloseRoster").onClick.Invoke(); ReviewButton(obj, "OpenRecruitment").onClick.Invoke();
                yield return Until(() => obj.GetComponentsInChildren<Button>().Any(b => b.name == "RecruitmentLastResult" && b.interactable), "late result is reachable from history");
                if (failRefresh) Assert.That(ReviewText(obj), Does.Contain("결과는 저장됐어요"));
                ReviewButton(obj, "RecruitmentLastResult").onClick.Invoke();
                yield return Until(() => obj.GetComponentsInChildren<Button>().Any(b => b.name == "RecruitmentResultDone"), "reduced-motion overview is dismissible");
                Assert.That(EventSystem.current, Is.Not.Null);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(ReviewButton(obj, "RecruitmentResultDone").gameObject));
                ReviewButton(obj, "RecruitmentResultDone").onClick.Invoke(); yield return null;
                Assert.That(ReviewPanelOpen(obj, "RecruitmentReveal"), Is.False);
                Assert.That(ReviewPanelOpen(obj, "RecruitmentPanel"), Is.True);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.Null);
                Assert.That(EventSystem.current.currentSelectedGameObject.activeInHierarchy, Is.True);
                Assert.That(host.Posts.Length, Is.EqualTo(1), "history and closing results never send another pull");
            }
            finally { release.TrySetResult(true); MemoryForgeUiMotion.Reduced = reduced; Object.Destroy(obj); }
        }

        [UnityTest]
        public IEnumerator RecruitmentEligibleSelectorRequiresChoiceAndExplicitConfirmationAtShownRevision()
        {
            RecruitmentFixture(); recruitment["launchClaimed"] = true;
            recruitment["banners"]![0]!["curve"]!["selectorAt"] = 300; recruitment["pity"]!["standard"]!["total"] = 300;
            post = seen =>
            {
                Assert.That(seen.Path, Is.EqualTo(PracticeCommandOutbox.RecruitmentSelectorRoute));
                var body = JObject.Parse(seen.Text);
                Assert.That(body["expectedRevision"]!.Value<long>(), Is.EqualTo(3)); Assert.That(body["charId"]!.Value<string>(), Is.EqualTo("sera"));
                var receipt = RecruitmentReceiptJson(seen, "selector"); var snapshot = (JObject)receipt["snapshot"]!;
                snapshot["launchClaimed"] = true; snapshot["ownedChars"] = new JObject { ["sera"] = 1 }; snapshot["pity"]!["standard"]!["selectorUsed"] = true;
                recruitment = (JObject)snapshot.DeepClone(); return Reply.Json(200, receipt);
            };
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = true;
            var obj = new GameObject("RecruitmentSelectorReview"); var lobby = obj.AddComponent<MemoryForgeLobbyClient>(); var session = Session();
            try
            {
                yield return null; lobby.Player!.Stop(); yield return null;
                ReviewButton(obj, "OpenRecruitment").onClick.Invoke(); Assert.That(lobby.ConnectRecruitment(() => session), Is.True);
                yield return Until(() => obj.GetComponentsInChildren<Button>().Any(b => b.name == "OpenRecruitmentSelector" && b.interactable), "earned selector is available");
                ReviewButton(obj, "OpenRecruitmentSelector").onClick.Invoke();
                Assert.That(host.Posts, Is.Empty, "opening the eligible selection spends nothing");
                ReviewButton(obj, "RecruitmentSelector:sera").onClick.Invoke();
                Assert.That(host.Posts, Is.Empty, "choosing a candidate still requires confirmation");
                Assert.That(ReviewText(obj), Does.Contain("선택할 동료: 세라"));
                ReviewButton(obj, "ConfirmRecruitmentSelector").onClick.Invoke();
                yield return Until(() => obj.GetComponentsInChildren<Button>().Any(b => b.name == "RecruitmentResultDone"), "selector receipt reaches the result overview");
                Assert.That(host.Posts.Length, Is.EqualTo(1));
                Assert.That(session.Recruitment!.Pity["standard"].SelectorUsed, Is.True);
                Assert.That(session.Recruitment.OwnedChars["sera"], Is.EqualTo(1));
                Assert.That(Outbox().Load(), Is.Null);
            }
            finally { MemoryForgeUiMotion.Reduced = reduced; Object.Destroy(obj); }
        }
    }
}
#endif
