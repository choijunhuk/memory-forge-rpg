#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MemoryForge.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    // Report screenshots of the expedition practice screens (reports/expedition_ui_20261018). Run by name with MF_EXPEDITION_SHOTS set
    // to the output folder (or, in an open Editor, that folder written in Temp/expedition_shots.txt). Real practices are driven through the client's public commands only (no injected Run state); the driver
    // passes turns in a seed-summoning fight until five enemies stand, so the crowded layout is a reachable state, not a mock. Each
    // capture renders the owned canvas through a camera into a texture of the given size, then restores the overlay canvas.
    public class ExpeditionUiCapturePlayModeTests
    {
        static readonly string[] Wanted =
            { "map", "battle_two", "battle_five", "boss_b_bell", "boss_b_notary", "boss_b_garden", "reward", "reward_elite", "rest", "forge", "shop", "event", "save_victory", "result" };

        [UnityTest, Timeout(900000), Explicit("writes report screenshots; run by name with MF_EXPEDITION_SHOTS")] // whole expeditions on 14 seeds
        public IEnumerator CaptureExpeditionScreens()
        {
            // In an open Editor (whose environment is fixed at launch) the folder can instead be the one line of Temp/expedition_shots.txt.
            string named = Path.Combine(Application.dataPath, "../Temp/expedition_shots.txt");
            string? dir = Environment.GetEnvironmentVariable("MF_EXPEDITION_SHOTS") is string set && set.Length != 0 ? set : File.Exists(named) ? File.ReadAllText(named).Trim() : null;
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("MF_EXPEDITION_SHOTS is not set");
            Directory.CreateDirectory(dir!);
            var taken = new HashSet<string>();
            for (long seed = 1234; seed < 1234 + 14 && Wanted.Any(w => !taken.Contains(w)); seed++)
            {
                var host = Open(seed); var client = host.GetComponent<MemoryForgeExpeditionClient>(); yield return null;
                var driver = new Driver();
                for (int step = 0; step < 8000 && !client.ShowingResult; step++)
                {
                    string? key = Key(client);
                    if (key != null && !taken.Contains(key))
                    {
                        taken.Add(key);
                        yield return Settle();
                        yield return Capture(host, Path.Combine(dir!, key + ".png"), 1600, 900);
                        if (key == "battle_five") yield return Capture(host, Path.Combine(dir!, "battle_five_phone_2340x1080.png"), 2340, 1080);
                    }
                    driver.Step(client, taken);
                    if (step % 6 == 0) yield return null;
                }
                if (client.ShowingResult && !taken.Contains("result")) { taken.Add("result"); yield return Settle(); yield return Capture(host, Path.Combine(dir!, "result.png"), 1600, 900); }
                Object.Destroy(host); yield return null;
            }
            // A defeat: the first battle of a fresh practice with only turn ends.
            var lost = Open(4321); var c = lost.GetComponent<MemoryForgeExpeditionClient>(); yield return null;
            Click(lost, b => b.name.StartsWith("Node:", StringComparison.Ordinal) && b.interactable);
            for (int turns = 0; turns < 400 && c.Snapshot!.Phase == "BATTLE"; turns++)
                if (c.HasTurnSummary) c.ContinueTurnSummary(); else c.EndTurn();
            if (c.HasTurnSummary) c.ContinueTurnSummary();
            if (c.HasActionPresentation) c.SkipActionPresentation();
            if (c.Snapshot!.Phase == "DEFEAT") { yield return Settle(); yield return Capture(lost, Path.Combine(dir!, "save_defeat.png"), 1600, 900); taken.Add("save_defeat"); }
            Object.Destroy(lost); yield return null;
            File.WriteAllText(Path.Combine(dir!, "captured.txt"), string.Join("\n", taken.OrderBy(k => k)) + "\nmissing: " + string.Join(", ", Wanted.Where(w => !taken.Contains(w))) + "\n");
        }

        // CARD-HOLD-BACKVIEW-20261019: the first battle with a hand card held, its owner from behind, at phone size. The canvas goes to the
        // camera first: that switch redraws the view, and a redraw ends a hold.
        [UnityTest, Explicit("writes a report screenshot; run by name with MF_EXPEDITION_SHOTS")]
        public IEnumerator CaptureCardHoldBackView()
        {
            string? dir = Environment.GetEnvironmentVariable("MF_EXPEDITION_SHOTS");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("MF_EXPEDITION_SHOTS is not set");
            Directory.CreateDirectory(dir!);
            var host = Open(1234); var client = host.GetComponent<MemoryForgeExpeditionClient>(); yield return null;
            Click(host, b => b.name.StartsWith("Node:", StringComparison.Ordinal) && b.interactable);
            yield return Settle();
            const int width = 2340, height = 1080;
            var texture = new RenderTexture(width, height, 24); var camera = new GameObject("BackViewShotCamera").AddComponent<Camera>();
            camera.targetTexture = texture; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.orthographic = true;
            var canvas = host.GetComponentsInChildren<Canvas>().First(x => x.name == MemoryForgeExpeditionClient.OwnedCanvas);
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            yield return null; yield return null; yield return null;
            string uid = client.Snapshot!.Battle!.Zones.Hand[0];
            var card = host.GetComponentsInChildren<Button>().First(b => b.name == "Hand:" + uid).gameObject;
            var press = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
            UnityEngine.EventSystems.ExecuteEvents.Execute(card, press, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
            yield return Settle(); // past the 0.2 s hold and the rise
            Assert.That(host.GetComponentsInChildren<RectTransform>().Any(r => r.name.StartsWith("OwnerBack:", StringComparison.Ordinal)), Is.True, "the owner is shown");
            var image = new Texture2D(width, height, TextureFormat.RGB24, false); var before = RenderTexture.active;
            Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = texture; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply(); RenderTexture.active = before;
            File.WriteAllBytes(Path.Combine(dir!, "battle_card_hold_backview.png"), image.EncodeToPNG());
            UnityEngine.EventSystems.ExecuteEvents.Execute(card, press, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
            Object.Destroy(camera.gameObject); texture.Release(); Object.Destroy(image); Object.Destroy(host);
        }

        internal static GameObject Open(long seed)
        {
            var host = new GameObject("ExpeditionUiCaptureHost"); host.SetActive(false);
            var client = host.AddComponent<MemoryForgeExpeditionClient>(); client.Seed = seed; client.Length = "short";
            host.SetActive(true); return host;
        }

        // The screen a capture is named after; null when it is not one to capture (or a sheet covers it).
        internal static string? Key(MemoryForgeExpeditionClient c)
        {
            if (c.HasTurnSummary || c.PickerOpen || c.CardInfoOpen || c.HasPreparedIntent) return null;
            if (c.ShowingResult) return "result";
            var s = c.Snapshot!;
            switch (s.Phase)
            {
                case "MAP": return "map";
                case "BATTLE":
                    var b = s.Battle!; if (c.HasActionPresentation || b.Phase != "PLAYER") return null;
                    int alive = b.Enemies.Count(e => e.Alive);
                    if (s.BattleType == "boss") return "boss_" + b.Enemies[0].Id;
                    if (alive >= 5) return "battle_five";
                    return b.Turn == 1 && alive == 2 ? "battle_two" : null;
                case "REWARD":
                    if (c.HasActionPresentation) return null;
                    return Get(s.Pending, "relic") is IList relic && relic.Count != 0 ? "reward_elite" : "reward";
                case "REST": return "rest";
                case "FORGE": return "forge";
                case "SHOP": return "shop";
                case "EVENT": return Get(s.Pending, "done") is bool done && done ? null : "event";
                case "VICTORY": return c.HasActionPresentation ? null : "save_victory";
                default: return null;
            }
        }

        internal sealed class Driver
        {
            readonly HashSet<string> tried = new HashSet<string>();
            string turn = "";
            readonly RunData data = ClientRunContent.Load();
            readonly int gaugeMax = ClientBattleContent.Load().Battle.Rules.GaugeMax;

            public void Step(MemoryForgeExpeditionClient c, HashSet<string> taken)
            {
                var s = c.Snapshot!;
                if (c.HasTurnSummary) { c.SkipTurnSummary(); c.ContinueTurnSummary(); return; }
                if (c.HasActionPresentation) { c.SkipActionPresentation(); return; }
                if (c.PickerOpen) { c.AutoPick(); return; }
                if (c.CardInfoOpen) { c.CloseCardInfo(); return; }
                switch (s.Phase)
                {
                    case "MAP": Travel(c, s, taken); return;
                    case "BATTLE": Fight(c, s, taken); return;
                    case "REWARD":
                        var cards = ListOf(Get(s.Pending, "cards")); var relics = ListOf(Get(s.Pending, "relic"));
                        if (relics.Count != 0 && c.TakeRelic((string)relics[0]!)) return;
                        if (cards.Count != 0 && c.TakeCard((string)cards[0]!)) return;
                        c.Proceed(); return;
                    case "REST": if (!(Integer(Get(s.Pending, "uses")) > 0 && s.Hp < s.MaxHp && c.RestHeal())) c.Proceed(); return;
                    case "EVENT":
                        if (Get(s.Pending, "done") is bool done && done) { c.Proceed(); return; }
                        var def = data.Events.Last(e => e.Id == (string)Get(s.Pending, "id")!);
                        for (int i = 0; i < def.Choices.Count; i++)
                        {
                            var fx = def.Choices[i].Effects;
                            if (def.Choices[i].RequiredCoins.GetValueOrDefault() <= s.Coins && fx.CardCopy.GetValueOrDefault() == 0 && fx.CardRemove.GetValueOrDefault() == 0 && c.ChooseEvent(i)) return;
                        }
                        c.Proceed(); return;
                    case "ARCHIVE": c.Archive("return"); return;
                    case "VICTORY": case "DEFEAT": c.CommitMemory(); return;
                    default: c.Proceed(); return;
                }
            }

            // Prefer a node type whose screen is still missing; an elite first while the five-enemy fight is missing.
            static void Travel(MemoryForgeExpeditionClient c, RunState s, HashSet<string> taken)
            {
                // The live view: a replaced canvas stays an inactive child of the same name until its deferred Destroy.
                var open = c.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Node:", StringComparison.Ordinal) && b.interactable).Select(b => b.name.Substring(5)).ToList();
                var nodes = s.Map!.Floors.SelectMany(f => f).Concat(new[] { s.Map.Boss }).ToDictionary(n => n.Id, n => n.Type);
                string[] order = { taken.Contains("battle_five") ? "" : "elite", taken.Contains("shop") ? "" : "shop", taken.Contains("event") ? "" : "event",
                    taken.Contains("forge") ? "" : "forge", taken.Contains("rest") ? "" : "rest", "battle", "rest", "event", "forge", "shop", "elite", "boss" };
                string pick = order.Select(t => open.FirstOrDefault(id => nodes[id] == t)).FirstOrDefault(id => id != null) ?? open.First();
                c.EnterNode(pick);
            }

            void Fight(MemoryForgeExpeditionClient c, RunState s, HashSet<string> taken)
            {
                var b = s.Battle!;
                string now = s.RunId + "@" + s.Position + "#" + b.Turn;
                if (now != turn) { turn = now; tried.Clear(); }
                var alive = b.Enemies.Where(e => e.Alive).ToList();
                // Seeds are summoned at the start of a summoner's pattern and again four turns later: pass until five stand.
                if (!taken.Contains("battle_five") && alive.Any(e => e.Id == "e_gardener" || e.Id == "b_garden") && alive.Count < 5 && s.Battle!.Hp * 3 > b.MaxHp && b.Turn < 9)
                { c.EndTurn(); return; }
                var aim = alive.OrderBy(e => e.Tier == "minion" ? 1 : 0).ThenBy(e => e.Hp).FirstOrDefault();
                if (aim != null && c.Target != aim.Uid) c.SelectEnemy(aim.Uid);
                foreach (string uid in b.Zones.Hand)
                {
                    if (!tried.Add(uid)) continue;
                    c.SelectCard(uid); c.SelectMode("now");
                    if (c.ConfirmCard() || c.PickerOpen) return; // an opened pick sheet is auto-picked at the next step (so it can be seen)
                }
                foreach (var actor in b.Actors) if (actor.Gauge >= gaugeMax && tried.Add("ult:" + actor.Id) && c.UseUltimate(actor.Id)) return;
                c.EndTurn();
            }
        }

        static IEnumerator Settle()
        {
            float until = Time.realtimeSinceStartup + 0.7f; // entry motions finish
            while (Time.realtimeSinceStartup < until) yield return null;
        }
        static void Click(GameObject host, Func<Button, bool> which) =>
            host.GetComponentsInChildren<Button>().First(which).onClick.Invoke();
        static IEnumerator Capture(GameObject host, string path, int width, int height)
        {
            var texture = new RenderTexture(width, height, 24);
            var camera = new GameObject("ExpeditionShotCamera").AddComponent<Camera>();
            camera.targetTexture = texture; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.orthographic = true;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false); var before = RenderTexture.active; Canvas? canvas = null;
            try
            {
                canvas = host.GetComponentsInChildren<Canvas>().First(x => x.name == MemoryForgeExpeditionClient.OwnedCanvas);
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
                // The scaler takes the camera's size and the client redraws at that scale (a new canvas, presented by the same camera).
                yield return null; yield return null; yield return null;
                canvas = host.GetComponentsInChildren<Canvas>().First(x => x.name == MemoryForgeExpeditionClient.OwnedCanvas);
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = texture; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                // A failed write leaves no camera, texture or camera-drawn canvas behind.
                RenderTexture.active = before;
                if (canvas != null) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
                Object.Destroy(camera.gameObject); Object.Destroy(image); texture.Release(); Object.Destroy(texture);
            }
            yield return null; yield return null; yield return null; // back to the overlay scale
        }

        static object? Get(object? node, string key) => node is IDictionary<string, object?> map && map.TryGetValue(key, out var value) ? value : null;
        static List<object?> ListOf(object? value) => value is IList list ? list.Cast<object?>().ToList() : new List<object?>();
        static long Integer(object? value) => value switch { int n => n, long n => n, double n => (long)n, _ => 0 };
    }
}
