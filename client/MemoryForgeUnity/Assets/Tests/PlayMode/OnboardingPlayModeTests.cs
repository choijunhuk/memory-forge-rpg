// ONBOARDING-FIRST-RUN §14.5/§3.4: Play Mode checks of the first-run coach over the real expedition practice. Every press is a real
// EventSystem raycast click at the control's own center, asserting the control is the top hit (the coach never covers it). Each test
// uses its own PlayerPrefs key, removed afterwards. This is not a human first-play test, Android/device or final-art acceptance.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public class OnboardingPlayModeTests
    {
        readonly List<GameObject> hosts = new List<GameObject>();
        readonly List<string> keys = new List<string>();
        readonly List<string> scenes = new List<string>();

        [UnityTearDown]
        public IEnumerator DestroyHostsAndPrefs()
        {
            foreach (var host in hosts) if (host != null) Object.Destroy(host);
            hosts.Clear();
            foreach (string name in scenes) { var scene = SceneManager.GetSceneByName(name); if (scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene); }
            scenes.Clear();
            foreach (string key in keys) { PlayerPrefs.DeleteKey(key + ".done"); PlayerPrefs.DeleteKey(key + ".skipped"); }
            keys.Clear(); PlayerPrefs.Save();
            yield return null;
        }

        sealed class Opening
        {
            public GameObject Host = null!;
            public MemoryForgeExpeditionClient Client = null!;
            public MemoryForgeOnboarding Coach = null!;
        }
        string NewKey() { string key = "MemoryForge.Onboarding.Test." + Guid.NewGuid().ToString("N"); keys.Add(key); return key; }

        IEnumerator Open(Opening result, string key, long seed = 1234)
        {
            var host = new GameObject("OnboardingTestHost"); hosts.Add(host);
            host.SetActive(false);
            var client = host.AddComponent<MemoryForgeExpeditionClient>(); client.Seed = seed; client.Length = "short";
            var coach = host.AddComponent<MemoryForgeOnboarding>(); coach.PrefsKey = key;
            host.SetActive(true);
            yield return null; yield return null;
            result.Host = host; result.Client = client; result.Coach = coach;
        }

        [UnityTest]
        public IEnumerator FirstRunCoachLeadsTheFirstBattleThroughTheRealButtonsAndOpensLessonTwoInTheNextBattle()
        {
            var o = new Opening(); string key = NewKey(); yield return Open(o, key);
            var client = o.Client; var coach = o.Coach;
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("MAP"));
            Assert.That(coach.CurrentStep, Is.EqualTo("map"), "a first run starts with the map step");
            var view = View(o.Host);
            var panel = CoachPanel(view);
            Assert.That(panel.transform.parent.GetSiblingIndex(), Is.EqualTo(panel.transform.parent.parent.childCount - 1), "the coach is drawn above the view");
            Assert.That(panel.GetComponentsInChildren<Graphic>(true).Where(g => g.raycastTarget).All(g => g.GetComponentInParent<Button>() != null),
                Is.True, "only the coach's own buttons take a raycast");
            var reachable = view.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Node:", StringComparison.Ordinal) && b.interactable).ToList();
            Assert.That(coach.Highlighted.Select(r => r.name), Is.EquivalentTo(reachable.Select(b => b.name)), "the map step outlines exactly the reachable nodes");
            // Stable names for a later restyle: the guide root last under the frame, its panel/text/skip, one highlight group per node.
            var guide = panel.transform.parent;
            Assert.That(guide.name, Is.EqualTo(MemoryForgeOnboarding.GuideName));
            Assert.That(guide.parent.name, Is.EqualTo("ExpeditionFrame"));
            Assert.That(panel.GetComponentsInChildren<Text>().Any(t => t.name == MemoryForgeOnboarding.TextName), Is.True);
            Assert.That(panel.GetComponentsInChildren<Button>().Select(b => b.name), Is.EqualTo(new[] { MemoryForgeOnboarding.SkipName }));
            var groups = guide.Cast<Transform>().Where(t => t.name.StartsWith(MemoryForgeOnboarding.HighlightPrefix, StringComparison.Ordinal)).ToList();
            Assert.That(groups.Select(t => t.name), Is.EquivalentTo(reachable.Select(b => MemoryForgeOnboarding.HighlightPrefix + b.name)));
            foreach (var group in groups)
            {
                Assert.That(group.Cast<Transform>().Select(t => t.name), Is.EqualTo(new[] { "Top", "Bottom", "Left", "Right" }));
                var node = reachable.Single(b => MemoryForgeOnboarding.HighlightPrefix + b.name == group.name).GetComponent<RectTransform>();
                Vector3[] outer = new Vector3[4], inner = new Vector3[4];
                ((RectTransform)group).GetWorldCorners(outer); node.GetWorldCorners(inner);
                Assert.That(outer[0].x <= inner[0].x && outer[0].y <= inner[0].y && outer[2].x >= inner[2].x && outer[2].y >= inner[2].y, Is.True, group.name + " surrounds its node");
            }

            var seen = new List<string>(); var driver = new Driver();
            for (int guard = 0; guard < 800 && client.Snapshot!.Phase != "REWARD"; guard++)
            {
                Assert.That(client.Snapshot.Phase, Is.EqualTo("MAP").Or.EqualTo("BATTLE"), "the first battle must not be lost");
                string? step = coach.CurrentStep;
                if (step != null)
                {
                    if (seen.LastOrDefault() != step) { seen.Add(step); AssertThreeLines(View(o.Host), step); }
                    PressCoach(o, driver);
                }
                else driver.Practice(o);
                yield return null;
            }
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("REWARD"), "following the coach, then repeating what it taught, wins the first battle");
            // RULES_NOW_PLAY: the mode step is gone; a card is chosen and confirmed.
            Assert.That(seen.Where(s => s != "battle.target"), Is.EqualTo(new[] { "map", "battle.hand", "battle.confirm", "battle.endturn", "battle.summary" }),
                "lesson 1 runs in §14.5 order (a target choice may come between card and confirm) and no later lesson starts in the same battle");
            Assert.That(driver.Plays, Is.GreaterThan(0));

            // The winning play's result sheet (or the last turn result) covers the reward screen; the coach waits under it and returns
            // once the player closes it.
            yield return null;
            for (int guard = 0; guard < 4 && (client.HasActionPresentation || client.HasTurnSummary); guard++)
            {
                Assert.That(coach.CurrentStep, Is.Null, "no step is drawn over a covering sheet");
                Click(ButtonNamed(o.Host, client.HasTurnSummary ? "ContinueTurnSummary" : "SkipActionPresentation"));
                yield return null;
            }
            Assert.That(coach.CurrentStep, Is.EqualTo("reward"));
            Assert.That(coach.CurrentText, Does.Contain("'지도로'"), "the reward step names the drawn proceed caption");
            PressCoach(o, driver);
            yield return null;
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("MAP"));
            Assert.That(coach.CurrentStep, Is.Null, "a done map step never shows again");
            var stored = PlayerPrefs.GetString(key + ".done").Split(',');
            Assert.That(stored, Is.SupersetOf(new[] { "map", "battle.hand", "battle.confirm", "battle.endturn", "battle.summary", "reward" }));
            Assert.That(coach.Completed, Is.EquivalentTo(stored));

            // The next battle opens lesson 2 (§14.5 ②) with the intent explanation; acknowledging it moves to 보존 (RULES_NOW_PLAY:
            // it replaced the reservation lesson).
            string firstBattle = client.Snapshot.RunId + "@" + client.Snapshot.Position;
            for (int guard = 0; guard < 200 && !(client.Snapshot!.Phase == "BATTLE" && client.Snapshot.RunId + "@" + client.Snapshot.Position != firstBattle); guard++)
            {
                if (coach.CurrentStep == "forge") PressCoachProceed(o); else driver.Travel(o);
                yield return null;
            }
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("BATTLE"));
            yield return null;
            Assert.That(coach.CurrentStep, Is.EqualTo("battle.intent"));
            Assert.That(coach.Highlighted.All(r => r.name.StartsWith("Enemy:", StringComparison.Ordinal)), Is.True);
            Click(ButtonNamed(o.Host, MemoryForgeOnboarding.NextName));
            yield return null;
            Assert.That(coach.Completed, Does.Contain("battle.intent"));
            Assert.That(coach.CurrentStep, Is.EqualTo("battle.retain"));
            Assert.That(coach.Highlighted.All(r => r.name == "Hand" || r.name.StartsWith("Hand:", StringComparison.Ordinal)), Is.True, "the 보존 step points at the hand");
            Click(ButtonNamed(o.Host, MemoryForgeOnboarding.NextName));
            yield return null;
            Assert.That(coach.Completed, Is.SupersetOf(new[] { "battle.intent", "battle.retain" }), "lesson 2 by the real buttons");

            // On to the end of the expedition with the original numbers: lessons 3 (break, ultimate) and 4 (boss) open in later battles
            // and the first memory save closes it.
            var lessons = new List<string>(); int gaugeMax = ClientBattleContent.Load().Battle.Rules.GaugeMax; bool ultReady = false;
            for (int guard = 0; guard < 8000 && !client.ShowingResult; guard++)
            {
                var live = client.Snapshot?.Battle;
                if (coach.Completed.Contains("battle.break") && live != null && live.Phase == "PLAYER" && live.Actors.Any(a => a.Gauge >= gaugeMax)) ultReady = true;
                string? step = coach.CurrentStep;
                if (step != null && !lessons.Contains(step)) { lessons.Add(step); AssertThreeLines(View(o.Host), step); }
                if (step == "forge") PressCoachProceed(o);
                else if (step != null) PressCoach(o, driver);
                else driver.Practice(o);
                yield return null; yield return null;
            }
            Assert.That(client.ShowingResult, Is.True, "the expedition ends at its result screen; lessons seen: " + string.Join(",", lessons));
            Assert.That(lessons, Does.Contain("save"));
            Assert.That(coach.Completed, Is.SupersetOf(new[] { "battle.break", "save" }), "lesson 3 and the first save by the real buttons; seen: " + string.Join(",", lessons));
            // RULES_NOW_PLAY: the ultimate tip is not part of finishing lesson 3; it must show and be done once a gauge fills after the break step.
            if (ultReady)
            {
                Assert.That(coach.Completed, Does.Contain("battle.ult"), "a ready ultimate after the break step shows the ultimate tip; seen: " + string.Join(",", lessons));
                Assert.That(lessons.IndexOf("battle.break"), Is.LessThan(lessons.IndexOf("battle.ult")));
            }
            else Assert.That(lessons, Does.Not.Contain("battle.ult"), "no gauge filled, so the ultimate tip had nothing to point at");
            Assert.That(Get(client.Snapshot!.Result, "outcome"), Is.EqualTo("clear"), "seed 1234's short expedition is cleared by following the guide");
            Assert.That(coach.Completed, Is.SupersetOf(new[] { "boss.timing" }), "the boss lesson by the real buttons; seen: " + string.Join(",", lessons));
            Assert.That(lessons.IndexOf("battle.break"), Is.LessThan(lessons.IndexOf("boss.timing")), "§14.5 order: ③ before ④");
            TestContext.WriteLine("onboarding run: outcome=" + Get(client.Snapshot.Result, "outcome") + " battles=" + client.Snapshot.Statistics.Battles
                + " plays=" + driver.Plays + " ultReady=" + ultReady + " steps=" + string.Join(",", lessons));
        }
        static object? Get(object? node, string key) => node is IDictionary<string, object?> map && map.TryGetValue(key, out var value) ? value : null;

        [UnityTest]
        public IEnumerator SkipHidesTheGuideForGoodButTheMemorySaveGuideStillShowsAndCannotBeSkipped()
        {
            var o = new Opening(); string key = NewKey(); yield return Open(o, key);
            var client = o.Client; var coach = o.Coach;
            Assert.That(coach.CurrentStep, Is.EqualTo("map"));
            Click(ButtonNamed(o.Host, MemoryForgeOnboarding.SkipName));
            yield return null;
            Assert.That(coach.Skipped, Is.True);
            Assert.That(PlayerPrefs.GetInt(key + ".skipped"), Is.EqualTo(1));
            Assert.That(coach.CurrentStep, Is.Null);
            Assert.That(View(o.Host).GetComponentsInChildren<RectTransform>().Any(r => r.name == MemoryForgeOnboarding.GuideName), Is.False, "a skipped guide draws nothing");

            Click(View(o.Host).GetComponentsInChildren<Button>().First(b => b.name.StartsWith("Node:", StringComparison.Ordinal) && b.interactable));
            yield return null;
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("BATTLE"));
            Assert.That(coach.CurrentStep, Is.Null, "skipped battle steps stay hidden");

            // Ending every turn without playing lets the party fall: the defeat's memory save is the first save screen.
            for (int turns = 0; turns < 600 && client.Snapshot!.Phase == "BATTLE"; turns++)
            {
                if (client.HasTurnSummary) Assert.That(client.ContinueTurnSummary(), Is.True); else Assert.That(client.EndTurn(), Is.True, client.LastError);
                if (turns % 10 == 9) yield return null;
            }
            if (client.HasTurnSummary) client.ContinueTurnSummary();
            Assert.That(client.Snapshot!.Phase, Is.EqualTo("DEFEAT"));
            yield return null;
            Assert.That(coach.CurrentStep, Is.EqualTo("save"), "§14.5: the memory-save guide is not skippable");
            Assert.That(View(o.Host).GetComponentsInChildren<Button>().Any(b => b.name == MemoryForgeOnboarding.SkipName), Is.False, "the save guide has no skip button");
            Assert.That(coach.Skip(), Is.False);
            Assert.That(coach.Highlighted.Select(r => r.name), Does.Contain("CommitMemory"));
            AssertThreeLines(View(o.Host), "save");

            // Closing without saving keeps the existing button's meaning; the guide is not done and returns at the next save screen.
            Click(ButtonNamed(o.Host, "CloseWithoutSave"));
            yield return null;
            Assert.That(client.ShowingResult, Is.True);
            Assert.That(coach.CurrentStep, Is.Null);
            Assert.That(coach.Completed, Does.Not.Contain("save"));
            Click(ButtonNamed(o.Host, "Depart"));
            yield return null;
            Click(View(o.Host).GetComponentsInChildren<Button>().First(b => b.name.StartsWith("Node:", StringComparison.Ordinal) && b.interactable));
            for (int turns = 0; turns < 600 && client.Snapshot!.Phase == "BATTLE"; turns++)
            {
                if (client.HasTurnSummary) Assert.That(client.ContinueTurnSummary(), Is.True); else Assert.That(client.EndTurn(), Is.True, client.LastError);
                if (turns % 10 == 9) yield return null;
            }
            if (client.HasTurnSummary) client.ContinueTurnSummary();
            yield return null;
            Assert.That(coach.CurrentStep, Is.EqualTo("save"), "the unfinished save guide shows again");

            Click(ButtonNamed(o.Host, "CommitMemory"));
            Assert.That(client.SessionMemoryVersion, Is.EqualTo(1));
            yield return null;
            Assert.That(coach.CurrentStep, Is.Null);
            Assert.That(PlayerPrefs.GetString(key + ".done").Split(','), Does.Contain("save"));
        }

        [UnityTest]
        public IEnumerator CompletedStepsStayHiddenOnTheNextLaunchAndANewRecordStartsOver()
        {
            string key = NewKey();
            var first = new Opening(); yield return Open(first, key);
            Assert.That(first.Coach.CurrentStep, Is.EqualTo("map"));
            PressCoach(first, new Driver());
            yield return null;
            Assert.That(first.Client.Snapshot!.Phase, Is.EqualTo("BATTLE"));
            Assert.That(first.Coach.CurrentStep, Is.EqualTo("battle.hand"));
            first.Host.SetActive(false);
            yield return null;
            first.Host.SetActive(true);
            yield return null; yield return null;
            Assert.That(first.Coach.CurrentStep, Is.EqualTo("battle.hand"), "re-enabling redraws the same step from the record");
            CoachPanel(View(first.Host));
            Object.Destroy(first.Host);
            yield return null;

            var next = new Opening(); yield return Open(next, key);
            Assert.That(next.Client.Snapshot!.Phase, Is.EqualTo("MAP"));
            Assert.That(next.Coach.CurrentStep, Is.Null, "the map step done on the earlier launch does not show again");
            Assert.That(View(next.Host).GetComponentsInChildren<RectTransform>().Any(r => r.name == MemoryForgeOnboarding.GuideName), Is.False);
            Click(View(next.Host).GetComponentsInChildren<Button>().First(b => b.name.StartsWith("Node:", StringComparison.Ordinal) && b.interactable));
            yield return null;
            Assert.That(next.Coach.CurrentStep, Is.EqualTo("battle.hand"), "a step not yet done still shows");
            Object.Destroy(next.Host);
            yield return null;

            var fresh = new Opening(); yield return Open(fresh, NewKey());
            Assert.That(fresh.Coach.CurrentStep, Is.EqualTo("map"), "positive control: an empty record starts from the map step");
        }

        // Codex round 4: ending the turn without touching the hand lets the client auto-select the new hand's first card; that is not
        // the player's press, so the hand step stays open.
        [UnityTest]
        public IEnumerator HandStepNeedsARealHandPressNotTheNextTurnsAutoSelection()
        {
            var o = new Opening(); string key = NewKey(); yield return Open(o, key);
            Click(View(o.Host).GetComponentsInChildren<Button>().First(b => b.name.StartsWith("Node:", StringComparison.Ordinal) && b.interactable));
            yield return null;
            Assert.That(o.Coach.CurrentStep, Is.EqualTo("battle.hand"));
            Click(ButtonNamed(o.Host, "EndTurn"));
            yield return null;
            Click(ButtonNamed(o.Host, "ContinueTurnSummary"));
            yield return null; yield return null;
            Assert.That(o.Client.Snapshot!.Battle!.Turn, Is.EqualTo(2));
            Assert.That(o.Coach.Completed, Does.Not.Contain("battle.hand"), "the auto-selected card of the new turn is not a press");
            Assert.That(PlayerPrefs.GetString(key + ".done").Split(','), Does.Not.Contain("battle.hand"));
            Assert.That(o.Coach.CurrentStep, Is.EqualTo("battle.hand"));
            PressFirstHandCard(o);
            yield return null;
            Assert.That(o.Coach.Completed, Does.Contain("battle.hand"), "positive control: a real hand press finishes it");
        }
        static void PressFirstHandCard(Opening o) => Click(View(o.Host).GetComponentsInChildren<Button>().First(b => b.name.StartsWith("Hand:", StringComparison.Ordinal)));

        // Codex round 3: a gauge marked full in one battle must not read as an ultimate used when the next battle starts at 0.
        [UnityTest]
        public IEnumerator UltimateStepComparesGaugesOnlyWithinTheBattleItWasMarkedIn()
        {
            var o = new Opening(); yield return Open(o, NewKey());
            Click(View(o.Host).GetComponentsInChildren<Button>().First(b => b.name.StartsWith("Node:", StringComparison.Ordinal) && b.interactable));
            yield return null;
            var battle = o.Client.Snapshot!.Battle!; var actor = battle.Actors[0];
            Assert.That(actor.Gauge, Is.LessThan(100), "a fresh battle starts below a full gauge");
            var context = new OnboardingContext(o.Client); var ult = OnboardingSteps.All.Single(s => s.Id == "battle.ult");
            var earlier = new OnboardingMark { Battle = "an earlier battle" }; earlier.ReadyGauges[actor.Id] = 100;
            Assert.That(ult.Done!(context, earlier), Is.False, "a full gauge from another battle is not an ultimate used here");
            var here = new OnboardingMark { Battle = context.BattleKey }; here.ReadyGauges[actor.Id] = 100;
            Assert.That(ult.Done!(context, here), Is.True, "positive control: in the marked battle a lower gauge is a used ultimate");
        }

        [Test]
        public void EveryStepTextFitsThreeLinesOfTheCoachAsPlainSentences()
        {
            var font = Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular");
            Assert.That(font, Is.Not.Null);
            var ids = OnboardingSteps.All.Select(s => s.Id).ToList();
            Assert.That(ids, Is.Unique);
            Assert.That(OnboardingSteps.All.Where(s => s.Required).Select(s => s.Id), Is.EqualTo(new[] { "save" }), "only the memory-save guide is unskippable");
            foreach (var step in OnboardingSteps.All)
            {
                Assert.That(step.After.All(ids.Contains) && step.After.All(a => ids.IndexOf(a) < ids.IndexOf(step.Id)), Is.True, step.Id + " needs only earlier steps");
                Assert.That(step.Ack || step.Done != null, Is.True, step.Id + " can be finished");
                foreach (string? variant in new[] { step.Text, step.Alt })
                {
                    if (variant == null) continue;
                    string text = MemoryForgeOnboarding.Compose(string.Format(variant, "카드 고르기"));
                    var generator = new TextGenerator();
                    generator.Populate(text, new TextGenerationSettings
                    {
                        font = font, fontSize = MemoryForgeOnboarding.FontSize, fontStyle = FontStyle.Normal, richText = true, lineSpacing = 1, scaleFactor = 1,
                        color = Color.white, generationExtents = new Vector2(MemoryForgeOnboarding.TextWidth(step), 1000), pivot = new Vector2(0, 1),
                        horizontalOverflow = HorizontalWrapMode.Wrap, verticalOverflow = VerticalWrapMode.Overflow, textAnchor = TextAnchor.UpperLeft
                    });
                    Assert.That(generator.lineCount, Is.InRange(1, 3), step.Id + ": §14.5 explanation text is at most three lines: " + variant);
                    Assert.That(variant.TrimEnd().EndsWith("요.", StringComparison.Ordinal), Is.True, step.Id + " is plain sentences");
                }
            }
        }

        [UnityTest]
        public IEnumerator OnlyTheShippedSceneClientGetsTheCoach()
        {
            var host = new GameObject("PlainClientHost"); hosts.Add(host);
            host.AddComponent<MemoryForgeExpeditionClient>();
            yield return null;
            Assert.That(host.GetComponent<MemoryForgeOnboarding>(), Is.Null, "a client outside the shipped scene (every other test's setup) gets no coach");
            Object.Destroy(host);
            yield return null;

            scenes.Add(MemoryForgeOnboarding.SceneName);
            yield return SceneManager.LoadSceneAsync(MemoryForgeOnboarding.SceneName, LoadSceneMode.Additive);
            var scene = SceneManager.GetSceneByName(MemoryForgeOnboarding.SceneName);
            Assert.That(scene.isLoaded, Is.True);
            var clients = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MemoryForgeExpeditionClient>(true)).ToList();
            Assert.That(clients, Is.Not.Empty);
            foreach (var found in clients) Assert.That(found.GetComponents<MemoryForgeOnboarding>().Length, Is.EqualTo(1), "the shipped scene's client gets exactly one coach");
        }

        // Report screenshots only (reports/onboarding_20261017): run by name with MF_ONBOARDING_SHOTS set to the output folder. Each
        // capture draws the owned view through a camera into a 1600×900 texture, then restores the overlay canvas.
        [UnityTest, Explicit("writes report screenshots; run by name with MF_ONBOARDING_SHOTS")]
        public IEnumerator CaptureCoachScreens()
        {
            string? dir = Environment.GetEnvironmentVariable("MF_ONBOARDING_SHOTS");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("MF_ONBOARDING_SHOTS is not set");
            var o = new Opening(); yield return Open(o, NewKey());
            var client = o.Client; var coach = o.Coach; var driver = new Driver(); var shot = new List<string>();
            for (int guard = 0; guard < 1200 && !(coach.CurrentStep == "battle.retain"); guard++)
            {
                string? step = coach.CurrentStep;
                if (step != null && !shot.Contains(step)) { shot.Add(step); yield return Capture(o.Host, System.IO.Path.Combine(dir!, (shot.Count).ToString("00") + "_" + step + ".png")); }
                if (step == "forge") PressCoachProceed(o);
                else if (step != null) PressCoach(o, driver);
                else if (client.HasActionPresentation) Click(ButtonNamed(o.Host, "SkipActionPresentation"));
                else driver.Practice(o);
                yield return null;
            }
            Object.Destroy(o.Host); yield return null;

            var save = new Opening(); yield return Open(save, NewKey());
            save.Coach.Skip(); yield return null;
            Click(View(save.Host).GetComponentsInChildren<Button>().First(b => b.name.StartsWith("Node:", StringComparison.Ordinal) && b.interactable));
            for (int turns = 0; turns < 600 && save.Client.Snapshot!.Phase == "BATTLE"; turns++)
                if (save.Client.HasTurnSummary) save.Client.ContinueTurnSummary(); else save.Client.EndTurn();
            if (save.Client.HasTurnSummary) save.Client.ContinueTurnSummary();
            yield return null;
            Assert.That(save.Coach.CurrentStep, Is.EqualTo("save"));
            yield return Capture(save.Host, System.IO.Path.Combine(dir!, (shot.Count + 1).ToString("00") + "_save.png"));
        }
        static IEnumerator Capture(GameObject host, string path)
        {
            var canvas = View(host).GetComponent<Canvas>();
            var texture = new RenderTexture(1600, 900, 24);
            var camera = new GameObject("OnboardingShotCamera").AddComponent<Camera>();
            camera.targetTexture = texture; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.orthographic = true;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            yield return null; // the scaler and the view's fit settle on the camera's 1600×900
            Canvas.ForceUpdateCanvases(); camera.Render();
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            RenderTexture.active = texture; image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply(); RenderTexture.active = null;
            System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
            if (canvas != null) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
            Object.Destroy(camera.gameObject); Object.Destroy(image); texture.Release();
            yield return null; // the overlay layout settles before the next raycast
        }

        // ───────── helpers ─────────
        // Presses what the coach shows: the acknowledge button of an explanation step, else the first usable outlined button; an
        // outlined hand (a panel) is answered with the next hand card.
        void PressCoach(Opening o, Driver driver)
        {
            var coach = o.Coach;
            var ack = o.Host.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == MemoryForgeOnboarding.NextName && b.IsInteractable());
            if (ack != null) { Click(ack); return; }
            var button = coach.Highlighted.Select(r => r.GetComponent<Button>()).FirstOrDefault(b => b != null && b.IsInteractable());
            if (button != null && button.name.StartsWith("Ult:", StringComparison.Ordinal) && o.Client.Target == null)
            { Click(View(o.Host).GetComponentsInChildren<Button>().First(x => x.name.StartsWith("Enemy:", StringComparison.Ordinal) && x.IsInteractable())); return; }
            if (button != null) { Click(button); if (button.name == "ConfirmCard") driver.Plays++; return; }
            // An outlined hand is answered with the next hand card; with nothing left to press, play on as taught.
            if (!coach.Highlighted.Any(r => r.name == "Hand") || !driver.NextHandCard(o)) driver.Practice(o);
        }
        static void PressCoachProceed(Opening o) => Click(ButtonNamed(o.Host, "Proceed"));

        // What the first lesson taught, repeated: continue a turn result, skip a shown action, auto-pick, confirm a ready play, pick a
        // target, choose '즉시', try the next hand card, else end the turn.
        sealed class Driver
        {
            public int Plays;
            readonly HashSet<string> tried = new HashSet<string>();
            string turn = "";
            public void Practice(Opening o)
            {
                var c = o.Client; var b = c.Snapshot!.Battle;
                if (c.HasTurnSummary) { Click(ButtonNamed(o.Host, "ContinueTurnSummary")); return; }
                if (c.HasActionPresentation) { Click(ButtonNamed(o.Host, "SkipActionPresentation")); return; }
                if (c.Snapshot.Phase != "BATTLE" || b == null) { Travel(o); return; }
                string now = c.Snapshot.RunId + "@" + c.Snapshot.Position + "#" + b.Turn;
                if (now != turn) { turn = now; tried.Clear(); }
                if (c.PickerOpen) { Click(ButtonNamed(o.Host, "PickAuto")); return; }
                var confirm = Usable(o.Host, "ConfirmCard");
                if (confirm != null) { Click(confirm); Plays++; return; }
                var caption = o.Host.GetComponentsInChildren<Button>().FirstOrDefault(x => x.name == "ConfirmCard")?.GetComponentInChildren<Text>()?.text;
                if (caption == "대상 선택") { Click(o.Host.GetComponentsInChildren<Button>().First(x => x.name.StartsWith("Enemy:", StringComparison.Ordinal) && x.IsInteractable())); return; }
                if (NextHandCard(o)) return;
                Click(ButtonNamed(o.Host, "EndTurn"));
            }
            // Map: the first reachable node; rest, shop and forge: leave; event: its first affordable choice without card picks.
            public void Travel(Opening o)
            {
                var c = o.Client; var s = c.Snapshot!;
                if (s.Phase == "MAP") { Click(View(o.Host).GetComponentsInChildren<Button>().First(x => x.name.StartsWith("Node:", StringComparison.Ordinal) && x.interactable)); return; }
                if (s.Phase == "EVENT" && Usable(o.Host, "Proceed") == null)
                {
                    // The first affordable choice, with the first non-weapon cards for a copy/removal choice (as the existing run driver).
                    var choice = o.Host.GetComponentsInChildren<Button>().First(x => x.name.StartsWith("EventChoice:", StringComparison.Ordinal) && x.IsInteractable());
                    int index = int.Parse(choice.name.Substring("EventChoice:".Length));
                    string id = (string)Get(s.Pending, "id")!; var fx = ClientRunContent.Load().Events.Last(e => e.Id == id).Choices[index].Effects;
                    int count = fx.CardCopy.GetValueOrDefault() != 0 ? 1 : fx.CardRemove.GetValueOrDefault();
                    var keys = count > 0 ? s.Deck.Where(d => !d.Weapon).Select(d => d.Key!).Take(count).ToList() : null;
                    Assert.That(c.ChooseEvent(index, keys), Is.True, c.LastError);
                    return;
                }
                if (s.Phase == "ARCHIVE") { Click(ButtonNamed(o.Host, "ArchiveReturn")); return; }
                if (s.Phase == "VICTORY" || s.Phase == "DEFEAT") { Click(ButtonNamed(o.Host, "CommitMemory")); return; }
                if (s.Phase == "BATTLE") { Practice(o); return; }
                Click(ButtonNamed(o.Host, "Proceed"));
            }
            // A hand card not yet tried this turn whose center lies inside the hand's visible area; false when none is left.
            public bool NextHandCard(Opening o)
            {
                var view = View(o.Host); var dock = view.GetComponentsInChildren<RectTransform>().First(r => r.name == "Hand");
                foreach (var card in view.GetComponentsInChildren<Button>().Where(x => x.name.StartsWith("Hand:", StringComparison.Ordinal)))
                {
                    if (!tried.Add(card.name)) continue;
                    var rect = card.GetComponent<RectTransform>();
                    if (!RectTransformUtility.RectangleContainsScreenPoint(dock, RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)))) continue;
                    Click(card); return true;
                }
                return false;
            }
        }

        static GameObject View(GameObject host)
        {
            var active = host.GetComponentsInChildren<Canvas>().Where(c => c.name == MemoryForgeExpeditionClient.OwnedCanvas && c.gameObject.activeInHierarchy).ToList();
            Assert.That(active.Count, Is.EqualTo(1), "exactly one active owned expedition view");
            return active[0].gameObject;
        }
        static RectTransform CoachPanel(GameObject view)
        {
            var panel = view.GetComponentsInChildren<RectTransform>().SingleOrDefault(r => r.name == MemoryForgeOnboarding.PanelName);
            Assert.That(panel, Is.Not.Null, "the coach panel is drawn");
            return panel!;
        }
        static Button? Usable(GameObject host, string name) => View(host).GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == name && b.IsInteractable());
        static Button ButtonNamed(GameObject host, string name)
        {
            var button = Usable(host, name);
            Assert.That(button, Is.Not.Null, name + " is drawn and usable");
            return button!;
        }
        // A real pointer click at the button's own center; the button must be the top raycast hit there.
        static void Click(Button button)
        {
            Canvas.ForceUpdateCanvases(); // a view rebuilt in this frame's Update is laid out before the raycast
            var rect = button.GetComponent<RectTransform>();
            var pointer = new PointerEventData(EventSystem.current)
                { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)), button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), button.name + " must be the top hit at its own center");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        // The drawn coach text wraps to at most three lines (§14.5) and is not cut off.
        static void AssertThreeLines(GameObject view, string step)
        {
            Canvas.ForceUpdateCanvases();
            var text = CoachPanel(view).GetComponentsInChildren<Text>().Single(t => t.name == MemoryForgeOnboarding.TextName);
            Assert.That(text.cachedTextGenerator.lineCount, Is.InRange(1, 3), step + " coach text lines");
            Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 1), step + " coach text fits its panel");
        }
    }
}
