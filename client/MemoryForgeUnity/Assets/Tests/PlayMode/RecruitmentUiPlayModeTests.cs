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
    public class RecruitmentUiPlayModeTests
    {
        [UnityTest]
        public IEnumerator DisconnectedRecruitmentAsksForConnectionAndDoesNotInventBalances()
        {
            var host = new GameObject("RecruitmentUiTest");
            var lobby = host.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); yield return null;
                host.GetComponentsInChildren<Button>().Single(b => b.name == "OpenRecruitment").onClick.Invoke();
                yield return null;
                var inputs = host.GetComponentsInChildren<InputField>();
                Assert.That(inputs.Single(x => x.name == "RecruitmentCredential").contentType, Is.EqualTo(InputField.ContentType.Password));
                Assert.That(host.GetComponentsInChildren<Button>().Any(b => b.name == "ConnectRecruitment"), Is.True);
                Assert.That(host.GetComponentsInChildren<Button>().Any(b => b.name == "RecruitOne" || b.name == "RecruitTen"), Is.False);
                Assert.That(host.GetComponentsInChildren<Text>().Any(t => t.text.Contains("서버")), Is.True);
                Assert.That(host.GetComponentsInChildren<CanvasGroup>().Single(g => g.name == "LobbySide").interactable, Is.False);
            }
            finally { Object.Destroy(host); }
        }

        [UnityTest]
        public IEnumerator LargeTouchHomeDockFitsAndDoesNotOverlapExpedition()
        {
            MemoryForgeExpeditionClient.TouchOverride = 144;
            var host = new GameObject("RecruitmentLargeTouchTest");
            var lobby = host.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); yield return null;
                var frame = host.GetComponentsInChildren<RectTransform>().Single(r => r.name == "LobbyFrame");
                var dock = host.GetComponentsInChildren<RectTransform>().Single(r => r.name == "HomeDock");
                var corners = new Vector3[4];
                foreach (var button in dock.GetComponentsInChildren<Button>())
                {
                    ((RectTransform)button.transform).GetWorldCorners(corners);
                    Assert.That(corners.Min(v => frame.InverseTransformPoint(v).y), Is.GreaterThanOrEqualTo(-450f), button.name + " remains inside the safe frame");
                }
                dock.GetWorldCorners(corners);
                float dockTop = corners.Max(v => frame.InverseTransformPoint(v).y);
                var sortie = (RectTransform)host.GetComponentsInChildren<Button>().Single(b => b.name == "StartExpedition").transform;
                sortie.GetWorldCorners(corners);
                Assert.That(corners.Min(v => frame.InverseTransformPoint(v).y), Is.GreaterThan(dockTop), "expedition and dock touch areas stay separate");
            }
            finally { MemoryForgeExpeditionClient.TouchOverride = null; Object.Destroy(host); }
        }

        [UnityTest]
        public IEnumerator OwnedFilterDoesNotTurnThePracticeFixtureIntoOwnedCharacters()
        {
            var host = new GameObject("RecruitmentOwnedTest");
            var lobby = host.AddComponent<MemoryForgeLobbyClient>();
            try
            {
                yield return null; lobby.Player!.Stop(); yield return null;
                host.GetComponentsInChildren<Button>().Single(b => b.name == "OpenCompanions").onClick.Invoke();
                host.GetComponentsInChildren<Button>().Single(b => b.name == "FilterOwned").onClick.Invoke();
                yield return null;
                Assert.That(host.GetComponentsInChildren<Button>().Any(b => b.name.StartsWith("Companion:")), Is.False);
                Assert.That(host.GetComponentsInChildren<Button>().Single(b => b.name == "RecruitFromRoster").interactable, Is.True);
                host.GetComponentsInChildren<Button>().Single(b => b.name == "RecruitFromRoster").onClick.Invoke();
                yield return null;
                Assert.That(host.GetComponentsInChildren<InputField>().Single(x => x.name == "RecruitmentCredential").text, Is.Empty);
            }
            finally { Object.Destroy(host); }
        }
    }
}
