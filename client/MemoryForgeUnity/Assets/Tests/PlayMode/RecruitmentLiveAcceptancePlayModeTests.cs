#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public class RecruitmentLiveAcceptancePlayModeTests
    {
        // Explicit opt-in spends exactly the existing dev account's final 160 free crystals once.
        [UnityTest]
        public IEnumerator ExistingLocalAccountPullPersistsAcrossLobbyRecreation()
        {
            string? metadata = Environment.GetEnvironmentVariable("MF_RECRUITMENT_LIVE_METADATA");
            if (string.IsNullOrEmpty(metadata)) { Assert.Ignore("MF_RECRUITMENT_LIVE_METADATA is not set; no live account is changed"); yield break; }
            var settings = JObject.Parse(File.ReadAllText(metadata!));
            string endpoint = (string)settings["endpoint"]!, account = (string)settings["account"]!;
            var uri = new Uri(endpoint);
            Assert.That(uri.Scheme, Is.EqualTo("http"));
            Assert.That(uri.Host, Is.EqualTo("127.0.0.1"), "this acceptance may only change a local development server");
            string credential = File.ReadAllText((string)settings["credential_file"]!).Trim();
            Assert.That(credential.Length > 0, Is.True, "a generated local development credential is required");
            string output = Environment.GetEnvironmentVariable("MF_RECRUITMENT_LIVE_OUTPUT") ?? Path.GetDirectoryName(metadata!)!;
            Directory.CreateDirectory(output);
            string[] preferenceKeys = { "mf.account.endpoint", "mf.account.alias" };
            var previous = preferenceKeys.ToDictionary(key => key, key => PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null);
            bool reduced = MemoryForgeUiMotion.Reduced; MemoryForgeUiMotion.Reduced = false;
            GameObject? host = null;
            try
            {
                host = new GameObject("LiveRecruitmentAcceptance");
                var lobby = host.AddComponent<MemoryForgeLobbyClient>();
                yield return null; lobby.Player!.Stop(); yield return null;
                Click(host, "OpenRecruitment");
                FillConnection(host, endpoint, account, credential);
                yield return null;
                Assert.That(host.GetComponentsInChildren<Text>().Any(text => text.text.Contains(credential)), Is.False, "credential must remain masked in every visible label");
                yield return LobbyMomentCapturePlayModeTests.Shot(host, Path.Combine(output, "recruitment-connection-masked.jpg"));
                Click(host, "ConnectRecruitment");
                yield return WaitFor(() => Snapshot(lobby) != null && FindButton(host, "RecruitOne")?.IsInteractable() == true, "authenticated recruitment view");
                var before = Snapshot(lobby)!;
                Assert.That(before.Revision, Is.EqualTo(4), "never repeat a live mutation after the approved account has changed");
                Assert.That(before.FreeCrystals, Is.EqualTo(160));
                Assert.That(before.PaidCrystals, Is.Zero);
                Assert.That(before.Tickets.Values.All(value => value == 0), Is.True);
                Assert.That(before.History.Sum(entry => entry.Results.Count), Is.EqualTo(21));
                Assert.That(before.OwnedChars.Count, Is.EqualTo(2));
                yield return LobbyMomentCapturePlayModeTests.Shot(host, Path.Combine(output, "recruitment-live-before.jpg"));
                Click(host, "RecruitOne");
                Assert.That(host.GetComponentsInChildren<Text>().Any(text => text.text.Contains("티켓 0장 + 결정 160개")), Is.True, "confirmation must disclose the exact cost");
                Assert.That(Snapshot(lobby)!.Revision, Is.EqualTo(4), "opening confirmation must not spend currency");
                yield return LobbyMomentCapturePlayModeTests.Shot(host, Path.Combine(output, "recruitment-live-confirmation.jpg"));
                Click(host, "ConfirmRecruitment");
                yield return WaitFor(() => FindButton(host, "SkipRecruitmentReveal") != null || FindButton(host, "RecruitmentResultDone") != null, "persisted pull reveal");
                var skip = FindButton(host, "SkipRecruitmentReveal");
                Assert.That(skip, Is.Not.Null, "the live reveal must offer its skip control");
                Click(host, "SkipRecruitmentReveal");
                yield return WaitFor(() => FindButton(host, "RecruitmentResultDone") != null, "final result after skipping");
                Assert.That(host.GetComponentsInChildren<RectTransform>().Count(item => item.name == "RecruitmentArrival"), Is.EqualTo(1));
                var committed = Snapshot(lobby)!;
                Assert.That(committed.Revision, Is.EqualTo(5));
                Assert.That(committed.FreeCrystals, Is.Zero); Assert.That(committed.PaidCrystals, Is.Zero);
                Assert.That(committed.History.Sum(entry => entry.Results.Count), Is.EqualTo(22));
                Assert.That(committed.OwnedChars.Count, Is.GreaterThanOrEqualTo(before.OwnedChars.Count));
                yield return LobbyMomentCapturePlayModeTests.Shot(host, Path.Combine(output, "recruitment-live-result.jpg"));
                string latestRequest = committed.History.First().RequestId;
                Object.Destroy(host); host = null; yield return null;

                host = new GameObject("LiveRecruitmentReconnect");
                lobby = host.AddComponent<MemoryForgeLobbyClient>();
                yield return null; lobby.Player!.Stop(); yield return null;
                Click(host, "OpenRecruitment");
                Assert.That(host.GetComponentsInChildren<InputField>().Single(field => field.name == "RecruitmentCredential").text, Is.Empty, "recreated lobby must require reauthentication");
                Assert.That(FindButton(host, "RecruitOne"), Is.Null);
                FillConnection(host, endpoint, account, credential); Click(host, "ConnectRecruitment");
                yield return WaitFor(() => Snapshot(lobby) != null && FindButton(host, "RecruitOne")?.IsInteractable() == true, "fresh authenticated server snapshot");
                var restored = Snapshot(lobby)!;
                Assert.That(restored.Revision, Is.EqualTo(committed.Revision));
                Assert.That(restored.FreeCrystals, Is.Zero); Assert.That(restored.PaidCrystals, Is.Zero);
                Assert.That(restored.History.Sum(entry => entry.Results.Count), Is.EqualTo(22));
                Assert.That(restored.History.First().RequestId, Is.EqualTo(latestRequest));
                CollectionAssert.AreEquivalent(committed.OwnedChars, restored.OwnedChars);
                Click(host, "RecruitmentOwned");
                var ownedButtons = host.GetComponentsInChildren<Button>().Where(button => button.name.StartsWith("Companion:")).Select(button => button.name.Substring("Companion:".Length)).ToArray();
                CollectionAssert.AreEquivalent(restored.OwnedChars.Keys, ownedButtons);
                yield return LobbyMomentCapturePlayModeTests.Shot(host, Path.Combine(output, "recruitment-live-owned-reconnected.jpg"));
                File.WriteAllText(Path.Combine(output, "recruitment-live-acceptance.json"), new JObject
                {
                    ["evidence"] = "Unity PlayMode UI Button.onClick with real localhost HTTP; not native manual pointer input",
                    ["beforeRevision"] = before.Revision, ["afterRevision"] = restored.Revision,
                    ["freeCrystalsBefore"] = before.FreeCrystals, ["freeCrystalsAfter"] = restored.FreeCrystals,
                    ["paidCrystalsAfter"] = restored.PaidCrystals, ["totalPullsBefore"] = 21, ["totalPullsAfter"] = 22,
                    ["ownedCharacters"] = new JArray(restored.OwnedChars.Keys), ["requestId"] = latestRequest,
                    ["maskedCredential"] = true, ["confirmationCostVerified"] = true, ["revealSkipped"] = true,
                    ["recreatedLobbyRequiredCredential"] = true, ["serverPersistenceVerified"] = true
                }.ToString());
            }
            finally
            {
                if (host != null) Object.Destroy(host);
                credential = "";
                MemoryForgeUiMotion.Reduced = reduced;
                foreach (var setting in previous)
                    if (setting.Value == null) PlayerPrefs.DeleteKey(setting.Key); else PlayerPrefs.SetString(setting.Key, setting.Value);
                PlayerPrefs.Save();
            }
        }

        // Read-only continuation for a spent account; never funds, resets, or repeats its pull.
        [UnityTest]
        public IEnumerator SpentLocalAccountReconnectsToItsPersistedResultAndOwnedRoster()
        {
            string? metadata = Environment.GetEnvironmentVariable("MF_RECRUITMENT_LIVE_METADATA");
            if (string.IsNullOrEmpty(metadata)) { Assert.Ignore("MF_RECRUITMENT_LIVE_METADATA is not set; no live account is accessed"); yield break; }
            var settings = JObject.Parse(File.ReadAllText(metadata!));
            string endpoint = (string)settings["endpoint"]!, account = (string)settings["account"]!;
            Assert.That(new Uri(endpoint).Scheme, Is.EqualTo("http")); Assert.That(new Uri(endpoint).Host, Is.EqualTo("127.0.0.1"));
            string credential = File.ReadAllText((string)settings["credential_file"]!).Trim();
            Assert.That(credential.Length > 0, Is.True);
            string output = Environment.GetEnvironmentVariable("MF_RECRUITMENT_LIVE_OUTPUT") ?? Path.GetDirectoryName(metadata!)!;
            Directory.CreateDirectory(output);
            string[] keys = { "mf.account.endpoint", "mf.account.alias" };
            var previous = keys.ToDictionary(key => key, key => PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null);
            GameObject? host = null; RecruitmentSnapshot? first = null;
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    host = new GameObject("LiveRecruitmentSpentReconnect"); var lobby = host.AddComponent<MemoryForgeLobbyClient>();
                    yield return null; lobby.Player!.Stop(); yield return null;
                    Click(host, "OpenRecruitment");
                    Assert.That(host.GetComponentsInChildren<InputField>().Single(field => field.name == "RecruitmentCredential").text, Is.Empty);
                    Assert.That(FindButton(host, "RecruitOne"), Is.Null, "recreated lobby must require reauthentication");
                    FillConnection(host, endpoint, account, credential); Click(host, "ConnectRecruitment");
                    yield return WaitFor(() => Snapshot(lobby) != null && FindButton(host, "RecruitOne")?.IsInteractable() == true, "fresh authenticated spent account");
                    var restored = Snapshot(lobby)!;
                    Assert.That(restored.Revision, Is.EqualTo(5)); Assert.That(restored.FreeCrystals, Is.Zero); Assert.That(restored.PaidCrystals, Is.Zero);
                    Assert.That(restored.Tickets.Values.All(value => value == 0), Is.True);
                    Assert.That(restored.History.Sum(entry => entry.Results.Count), Is.EqualTo(22));
                    Assert.That(restored.OwnedChars["harin"], Is.EqualTo(2)); Assert.That(restored.OwnedChars["rien"], Is.EqualTo(1));
                    Assert.That(restored.History.First().Results.Single().Id, Is.EqualTo("harin"));
                    Assert.That(restored.History.First().Results.Single().Resonance, Is.EqualTo(1));
                    if (first != null) Assert.That(restored.ToJObject().ToString(), Is.EqualTo(first.ToJObject().ToString()), "destroying the entire lobby and session cannot alter server state");
                    first = restored;
                    Click(host, "RecruitOne"); Assert.That(FindButton(host, "ConfirmRecruitment")!.IsInteractable(), Is.False, "spent account cannot confirm another pull");
                    Click(host, "CancelRecruitment"); Click(host, "RecruitmentLastResult");
                    Assert.That(host.GetComponentsInChildren<RectTransform>().Count(item => item.name == "RecruitmentArrival"), Is.EqualTo(1));
                    yield return LobbyMomentCapturePlayModeTests.Shot(host, Path.Combine(output, "recruitment-live-result-reconnected-" + attempt + ".jpg"));
                    Click(host, "RecruitmentResultRoster");
                    CollectionAssert.AreEquivalent(restored.OwnedChars.Keys, host.GetComponentsInChildren<Button>().Where(button => button.name.StartsWith("Companion:")).Select(button => button.name.Substring("Companion:".Length)));
                    yield return LobbyMomentCapturePlayModeTests.Shot(host, Path.Combine(output, "recruitment-live-owned-reconnected-" + attempt + ".jpg"));
                    Object.Destroy(host); host = null; yield return null;
                }
                File.WriteAllText(Path.Combine(output, "recruitment-live-reconnect.json"), new JObject { ["revision"] = first!.Revision,
                    ["freeCrystals"] = first.FreeCrystals, ["paidCrystals"] = first.PaidCrystals, ["totalPulls"] = 22,
                    ["ownedChars"] = JObject.FromObject(first.OwnedChars), ["historyRequestId"] = first.History.First().RequestId,
                    ["reauthentications"] = 2, ["mutationsInContinuation"] = 0, ["insufficientBalanceConfirmationDisabled"] = true }.ToString());
            }
            finally
            {
                if (host != null) Object.Destroy(host); credential = "";
                foreach (var setting in previous) if (setting.Value == null) PlayerPrefs.DeleteKey(setting.Key); else PlayerPrefs.SetString(setting.Key, setting.Value);
                PlayerPrefs.Save();
            }
        }

        static RecruitmentSnapshot? Snapshot(MemoryForgeLobbyClient lobby) =>
            ((PracticeServerSession?)typeof(MemoryForgeLobbyClient).GetField("recruitmentSession", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lobby))?.Recruitment;

        static Button? FindButton(GameObject host, string name) => host.GetComponentsInChildren<Button>().SingleOrDefault(button => button.name == name);

        static void Click(GameObject host, string name)
        {
            var button = FindButton(host, name);
            Assert.That(button, Is.Not.Null, name);
            Assert.That(button!.IsInteractable(), Is.True, name);
            button.onClick.Invoke();
        }

        static void FillConnection(GameObject host, string endpoint, string account, string credential)
        {
            var fields = host.GetComponentsInChildren<InputField>();
            fields.Single(field => field.name == "RecruitmentEndpoint").text = endpoint;
            fields.Single(field => field.name == "RecruitmentAccount").text = account;
            var password = fields.Single(field => field.name == "RecruitmentCredential");
            Assert.That(password.contentType, Is.EqualTo(InputField.ContentType.Password));
            password.text = credential;
        }

        static IEnumerator WaitFor(Func<bool> ready, string step)
        {
            float until = Time.realtimeSinceStartup + 30;
            while (!ready() && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(ready(), Is.True, step + " timed out");
        }
    }
}
