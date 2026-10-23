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
    public partial class PracticeServerSessionPlayModeTests
    {
        void SupportLobbyFixture(bool fourth)
        {
            MemoryForgeAccountConnection.SetCurrent(null); SupportFixture(false);
            if (fourth)
            {
                var character = (JObject)progressionProfile["characters"]![0]!.DeepClone(); character["charId"] = "sera";
                character["weaponType"] = "ranged"; character["weaponInstanceId"] = "basic:w_basic_ranged"; character["eligibleWeapons"] = new JArray("basic:w_basic_ranged");
                ((JArray)progressionProfile["characters"]!).Add(character);
                var weapon = (JObject)progressionProfile["weapons"]![0]!.DeepClone(); weapon["instanceId"] = "basic:w_basic_ranged";
                weapon["weaponId"] = "w_basic_ranged"; weapon["type"] = "ranged"; weapon["equippedBy"] = "sera"; ((JArray)progressionProfile["weapons"]!).Add(weapon);
            }
            var previous = additionalReply;
            additionalReply = seen => seen.Method == "GET" && seen.Path == RecruitmentRoute ? Reply.Json(200, RecruitmentJson()) : previous!(seen);
        }

        [UnityTest]
        public IEnumerator SupportSelectionOnlyStagesAfterConfirmationAndRetriesTheSameRequestFromRecruitment()
        {
            SupportLobbyFixture(true); var frozen = world.Extra!["battle"]!.DeepClone(); byte[]? saved = null; string? body = null; int committed = 0;
            var session = Session(); var home = new GameObject("SupportLobbyConfirm"); var lobby = home.AddComponent<MemoryForgeLobbyClient>();
            post = seen =>
            {
                Assert.That(seen.Path, Is.EqualTo(SupportSelectionPath));
                if (saved != null) { Assert.That(seen.Text, Is.EqualTo(body)); return new Reply(200, saved); }
                committed++; body = seen.Text; var request = JObject.Parse(body); Assert.That(request["charId"]!.Value<string>(), Is.EqualTo("sera"));
                var receipt = SupportSelectionReply(progressionProfile, "nia", "sera", request["requestId"]!.Value<string>()!);
                progressionProfile = (JObject)receipt["profile"]!.DeepClone(); saved = Utf8(receipt);
                return new Reply(200, saved.Take(saved.Length / 2).ToArray());
            };
            try
            {
                yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => session);
                yield return Until(() => lobby.AccountProgression?.HasSupportSelection == true && home.GetComponentsInChildren<Button>(true).Any(b => b.name == "RefreshRecruitment" && b.interactable), "support account GET");
                lobby.OpenSupportSelection();
                home.GetComponentsInChildren<Button>().Single(b => b.name == "SupportCandidate:sera").onClick.Invoke();
                Assert.That(host.Posts, Is.Empty); Assert.That(Outbox().Load(), Is.Null);
                string? shots = Environment.GetEnvironmentVariable("MF_ACCOUNT_UI_SHOTS");
                if (!string.IsNullOrEmpty(shots)) { Directory.CreateDirectory(shots!); yield return LobbyMomentCapturePlayModeTests.Shot(home, Path.Combine(shots!, "support-selection-confirm.jpg")); }
                home.GetComponentsInChildren<Button>().Single(b => b.name == "ConfirmSupportSelection").onClick.Invoke();
                yield return Until(() => home.GetComponentsInChildren<Button>().Any(b => b.name == "RetryEquipmentPending" && b.IsInteractable()), "uncertain support selection retained");
                Assert.That(committed, Is.EqualTo(1)); Assert.That(Outbox().Load()!.Body, Is.EqualTo(body)); Assert.That(host.Posts.Length, Is.EqualTo(1));
                home.GetComponentsInChildren<Button>().Single(b => b.name == "CloseEquipment").onClick.Invoke();
                home.GetComponentsInChildren<Button>().Single(b => b.name == "OpenRecruitment").onClick.Invoke();
                home.GetComponentsInChildren<Button>().Single(b => b.name == "RetryRecruitmentPending").onClick.Invoke();
                yield return Until(() => lobby.AccountProgression?.SupportSelection == "sera" && Outbox().Load() == null, "same support request confirmed");
                Assert.That(committed, Is.EqualTo(1)); Assert.That(host.Posts.Length, Is.EqualTo(2));
                Assert.That(JToken.DeepEquals(frozen, world.Extra!["battle"]), Is.True); Assert.That(session.State.Snapshot!.Run!.Support!.CharId, Is.EqualTo("nia"));
            }
            finally { Object.Destroy(home); MemoryForgeAccountConnection.SetCurrent(null); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SupportSelectionShowsTheMissingFourthCompanionWithoutBorrowingOrAddingAnything()
        {
            SupportLobbyFixture(false); progressionProfile["supportSelection"] = JValue.CreateNull(); MemoryForgeExpeditionClient.TouchOverride = 144;
            var home = new GameObject("SupportEmptyUi"); var lobby = home.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); lobby.ConnectRecruitment(() => Session());
                yield return Until(() => lobby.AccountProgression?.HasSupportSelection == true && home.GetComponentsInChildren<Button>(true).Any(b => b.name == "RefreshRecruitment" && b.interactable), "support account GET");
                lobby.OpenSupportSelection(); var panel = home.GetComponentsInChildren<RectTransform>().Single(r => r.name == "EquipmentPanel");
                Assert.That(panel.GetComponentsInChildren<Button>().Count(b => b.name.StartsWith("SupportCandidate:")), Is.EqualTo(3));
                Assert.That(panel.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("SupportCandidate:")).All(b => !b.IsInteractable()), Is.True);
                Assert.That(panel.GetComponentsInChildren<Text>().Any(t => t.text.Contains("출전 편성 밖")), Is.True);
                Assert.That(lobby.SelectedOwnedParty.Count, Is.EqualTo(3)); Assert.That(host.Posts, Is.Empty);
                string? shots = Environment.GetEnvironmentVariable("MF_ACCOUNT_UI_SHOTS");
                if (!string.IsNullOrEmpty(shots)) { Directory.CreateDirectory(shots!); yield return LobbyMomentCapturePlayModeTests.Shot(home, Path.Combine(shots!, "support-no-eligible-fourth-144.jpg")); }
            }
            finally { Object.Destroy(home); MemoryForgeExpeditionClient.TouchOverride = null; MemoryForgeAccountConnection.SetCurrent(null); }
            yield return null;
        }
    }
}
#endif
