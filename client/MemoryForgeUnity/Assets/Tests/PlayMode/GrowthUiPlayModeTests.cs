#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public class GrowthUiPlayModeTests
    {
        [UnityTest]
        public IEnumerator AnUnconnectedGrowthScreenShowsAnHonestConnectionStateAndCanReturnHome()
        {
            MemoryForgeAccountConnection.SetCurrent(null);
            var host = new GameObject("UnconnectedGrowthUi"); var view = host.AddComponent<MemoryForgeGrowthClient>();
            var destinations = new List<string>(); view.SceneLoader = destinations.Add;
            try
            {
                yield return null;
                Assert.That(host.GetComponentsInChildren<Button>().Any(button => button.name.StartsWith("GrowthCard:") || button.name == "GrowthPlay"), Is.False);
                Assert.That(host.GetComponentsInChildren<Text>().Any(text => text.text.Contains("계정")), Is.True);
                Assert.That(MemoryForgeAccountConnection.Current, Is.Null);
                host.GetComponentsInChildren<Button>().Single(button => button.name == "GrowthHome").onClick.Invoke();
                Assert.That(destinations, Is.EqualTo(new[] { MemoryForgeExpeditionClient.LobbyScene }));
            }
            finally { Object.Destroy(host); MemoryForgeAccountConnection.SetCurrent(null); }
            yield return null;
        }
    }
}
