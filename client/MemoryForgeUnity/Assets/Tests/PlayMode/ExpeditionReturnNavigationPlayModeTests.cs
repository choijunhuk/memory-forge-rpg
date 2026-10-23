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
    public class ExpeditionReturnNavigationPlayModeTests
    {
        GameObject? host;
        [UnityTearDown] public IEnumerator Clean() { if (host != null) Object.Destroy(host); yield return null; }
        [UnityTest] public IEnumerator AnAbandonedExpeditionOffersAWorkshopExitWithoutAutomaticallyDeparting()
        {
            host = new GameObject("AbandonNavigationRegression");
            var client = host.AddComponent<MemoryForgeExpeditionClient>();
            yield return null;
            Assert.That(client.Abandon(), Is.True);
            Assert.That(client.ShowingResult, Is.True);
            Assert.That(host.GetComponentsInChildren<Button>().Any(button => button.name == "ReturnHome" && button.IsInteractable()), Is.True,
                "abandon must offer the workshop instead of trapping the player at another expedition");
        }
    }
}
