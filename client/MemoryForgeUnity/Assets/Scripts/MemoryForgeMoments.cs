#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    // Data for docs/MOMENTS_FORMAT.md ("memory-forge-moments/1"). Codex writes the content; this only reads and checks it.
    public sealed class MomentFile
    {
        public const string Format = "memory-forge-moments/1";
        [JsonProperty("format")] public string FormatId = "";
        public string Character = "";
        public string Status = "draft";
        public string? IdleFigure;
        public Dictionary<string, string> Images = new Dictionary<string, string>();
        public Dictionary<string, Dictionary<string, float[]>> Anchors = new Dictionary<string, Dictionary<string, float[]>>();
        public Dictionary<string, MomentMotion> Motions = new Dictionary<string, MomentMotion>();
        public List<MomentScene> Scenes = new List<MomentScene>();

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
            MissingMemberHandling = MissingMemberHandling.Ignore, NullValueHandling = NullValueHandling.Ignore
        };

        // Returns null with the first problem and its field path when the file is not a playable moments file; a bad file is
        // reported and skipped, never allowed to stop the lobby or the player.
        public static MomentFile? Parse(string json, out string? error)
        {
            try
            {
                var file = JsonConvert.DeserializeObject<MomentFile>(json, Settings);
                error = file == null ? "empty file" : file.Problem();
                return error == null ? file : null;
            }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; return null; }
        }

        string? Problem()
        {
            if (FormatId != Format) return "format: " + FormatId;
            if (string.IsNullOrEmpty(Character)) return "character: missing";
            if (Images == null || Anchors == null || Motions == null || Scenes == null) return "images/anchors/motions/scenes: null";
            foreach (var p in Images) if (string.IsNullOrEmpty(p.Value)) return "images." + p.Key + ": no path";
            foreach (var a in Anchors)
                if (a.Value == null) return "anchors." + a.Key + ": null";
                else foreach (var p in a.Value)
                    if (p.Value == null || p.Value.Length != 2 || p.Value.Any(v => float.IsNaN(v) || float.IsInfinity(v))) return "anchors." + a.Key + "." + p.Key + ": needs [u, v]";
            foreach (var m in Motions)
            {
                string at = "motions." + m.Key;
                if (m.Value == null) return at + ": null";
                if (m.Value.Frames == null || m.Value.Tracks == null) return at + ": frames/tracks null";
                if (m.Value.Frames.Any(string.IsNullOrEmpty)) return at + ".frames: empty entry";
                if (m.Value.Frames.Count != 0 && !(m.Value.Fps > 0 && Finite(m.Value.Fps))) return at + ".fps: must be positive";
                float length = m.Value.Frames.Count != 0 ? m.Value.Frames.Count / m.Value.Fps : 0;
                foreach (var t in m.Value.Tracks)
                {
                    if (t.Value == null || t.Value.Count == 0 || t.Value.Any(k => k == null)) return at + ".tracks." + t.Key + ": needs keys";
                    for (int i = 0; i < t.Value.Count; i++)
                        if (!new[] { t.Value[i].X, t.Value[i].Y, t.Value[i].Rot, t.Value[i].Scale, t.Value[i].Alpha }.All(Finite)) return at + ".tracks." + t.Key + "[" + i + "]: not a number";
                    for (int i = 0; i < t.Value.Count; i++)
                    {
                        float time = t.Value[i].T;
                        if (float.IsNaN(time) || float.IsInfinity(time) || time < 0 || (i > 0 && time < t.Value[i - 1].T)) return at + ".tracks." + t.Key + "[" + i + "].t: out of order";
                    }
                    length = Mathf.Max(length, t.Value[t.Value.Count - 1].T);
                }
                if (m.Value.Loop && !(length > 0)) return at + ": a loop needs a positive duration";
            }
            for (int s = 0; s < Scenes.Count; s++)
            {
                var scene = Scenes[s]; string at = "scenes[" + s + "]";
                if (scene == null) return at + ": null";
                if (string.IsNullOrEmpty(scene.Id)) return at + ".id: missing";
                if (scene.Beats == null || scene.Trigger == null || scene.Input == null || scene.Assets == null) return at + ": beats/trigger/input/assets null";
                for (int a = 0; a < scene.Assets.Count; a++) if (scene.Assets[a] == null) return at + ".assets[" + a + "]: null";
                if (!Finite(scene.Input.CooldownS) || scene.Input.CooldownS < 0) return at + ".input.cooldown_s: not a time";
                for (int b = 0; b < scene.Beats.Count; b++)
                {
                    var beat = scene.Beats[b]; string bat = at + ".beats[" + b + "]";
                    if (beat == null) return bat + ": null";
                    if (float.IsNaN(beat.Hold) || float.IsInfinity(beat.Hold) || beat.Hold < 0) return bat + ".hold: not a time";
                    if (beat.Camera?.Duration is float d && (!Finite(d) || d < 0)) return bat + ".camera.duration: not a time";
                    if (beat.Overlays != null)
                        foreach (var o in beat.Overlays)
                            if (o.Value != null && o.Value.Type != JTokenType.Null && o.Value.Type != JTokenType.String && !(o.Value.Type == JTokenType.Object && o.Value["image"]?.Type == JTokenType.String && Coordinate(o.Value["x"]) && Coordinate(o.Value["y"])))
                                return bat + ".overlays." + o.Key + ": image key, {image,x,y} with numeric x/y, or null";
                }
            }
            return null;
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Coordinate(JToken? v) => v == null || v.Type == JTokenType.Null || ((v.Type == JTokenType.Integer || v.Type == JTokenType.Float) && Finite((float)v));

        // The figure shown while nothing plays: idle_figure, else the first figure of the first lobby scene, else any figure.
        public string? RestFigure()
        {
            if (IdleFigure != null && Images.ContainsKey(IdleFigure)) return IdleFigure;
            foreach (var scene in Scenes.Where(s => s.Kind == "lobby").Concat(Scenes))
                foreach (var beat in scene.Beats) if (beat.Figure != null && Images.ContainsKey(beat.Figure)) return beat.Figure;
            return null;
        }
    }

    public sealed class MomentMotion
    {
        public float Fps = 12;
        public List<string> Frames = new List<string>();
        public Dictionary<string, List<MomentKey>> Tracks = new Dictionary<string, List<MomentKey>>();
        public bool Loop;
        public string Ease = "linear";
        public string Slot = "figure";
        public string? Clip;
    }

    public sealed class MomentKey { public float T, X, Y, Rot; public float Scale = 1, Alpha = 1; }

    public sealed class MomentScene
    {
        public string Id = "", Kind = "", Title = "";
        public MomentTrigger Trigger = new MomentTrigger();
        public string Why = "", Intent = "";
        public List<MomentAsset> Assets = new List<MomentAsset>();
        public MomentInput Input = new MomentInput();
        public MomentStage? Stage;
        public List<MomentBeat> Beats = new List<MomentBeat>();
    }

    public sealed class MomentAsset { public string Key = "", Type = "", Status = ""; }
    public sealed class MomentTrigger { public string On = "tap"; public int? BondLevelMin; public string? Time; public string? Note; }
    public sealed class MomentInput { public bool Skippable = true; public string Advance = "tap"; public bool Repeatable = true; public float CooldownS; }
    public sealed class MomentStage { public string? Background; public string? Music; }

    public sealed class MomentBeat
    {
        public string? Figure, Motion, Gaze, Voice, Music, Sfx;
        public Dictionary<string, JToken?>? Overlays;
        public MomentCamera? Camera;
        public MomentLine? Line;
        public float Hold;
    }

    public sealed class MomentCamera { public string Shot = "full"; public string? Focus; public string Move = "none"; public float? Duration; }
    public sealed class MomentLine { public string? Speaker; public string Text = ""; public string? Tone; }

    // Plays one character's moments on a stage it builds under the given parent: background, figure and overlay slots, frame and
    // keyframe motions, camera framing, the dialogue box with typewriter text and paging, tap to advance and skip. It plays exactly
    // what the file says; a missing image leaves its slot empty instead of substituting other art (MOMENTS_FORMAT.md, playback).
    // When a scene ends, is skipped or is stopped, the stage returns to the rest figure with no scene-owned overlays, audio or backdrop.
    public sealed class MemoryForgeMomentPlayer : MonoBehaviour
    {
        public const float RefWidth = 1600, RefHeight = 900, AuthoredWidth = 1920; // keyframe x/y are authored on 1920x1080
        const float FigureHeight = 860, CharsPerSecond = 38;
        static readonly Dictionary<string, float> ShotScale = new Dictionary<string, float> { { "full", 1f }, { "medium", 1.45f }, { "bust", 1.9f }, { "close", 2.6f } };
        static readonly Color Empty = new Color(0.051f, 0.075f, 0.133f, 1f);

        public MomentFile? File { get; private set; }
        public bool Playing => running;
        public bool WaitingForTap { get; private set; }
        public string? CurrentFigure { get; private set; }
        public string? LastError { get; private set; }
        public Image FigureImage => figure;
        public event Action<MomentScene>? Started, Finished;
        public Func<string, string?>? NameOf;

        Font font = null!;
        Func<string, Sprite?> sprite = null!;
        RectTransform stage = null!, lens = null!, dialog = null!;
        Image figure = null!, backdrop = null!;
        Text speaker = null!, line = null!, waitMark = null!;
        Button skip = null!, tapCatcher = null!;
        Sprite? still;
        readonly Dictionary<string, Image> overlays = new Dictionary<string, Image>();
        readonly Dictionary<string, Coroutine> motions = new Dictionary<string, Coroutine>();
        readonly Dictionary<string, Vector2> slotBase = new Dictionary<string, Vector2>();
        Vector2 figureHome;
        Coroutine? routine, cameraMove;
        MomentScene? current;
        bool tapped, skipped, running; // running, not the handle: a scene can finish inside StartCoroutine
        AudioSource music = null!, sfx = null!;

        // parent: a full-screen RectTransform on a 1600x900 reference canvas. figureCenterX: where the figure stands at rest.
        public void Build(RectTransform parent, Font uiFont, Func<string, Sprite?> loadSprite, float figureCenterX, Rect? dialogArea = null)
        {
            font = uiFont; sprite = loadSprite;
            stage = Rect("MomentStage", parent); Stretch(stage);
            backdrop = Rect("Backdrop", stage).gameObject.AddComponent<Image>(); Stretch(backdrop.rectTransform); backdrop.raycastTarget = false; backdrop.enabled = false;
            lens = Rect("MomentCamera", stage); Stretch(lens);
            figure = new GameObject("Figure", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            figure.transform.SetParent(lens, false); figure.preserveAspect = true; figure.raycastTarget = false; figure.enabled = false;
            figureHome = new Vector2(figureCenterX - RefWidth / 2, -10); figure.rectTransform.anchoredPosition = figureHome;

            tapCatcher = MakeButton("MomentTap", parent, Color.clear, Tap); Stretch((RectTransform)tapCatcher.transform); tapCatcher.gameObject.SetActive(false);
            dialog = Rect("MomentDialog", parent, new Color(0.055f, 0.075f, 0.13f, 0.93f));
            var area = dialogArea ?? new Rect(200, 668, 1200, 206);
            Place(dialog, area.x, area.y, area.width, area.height);
            var edge = Rect("Edge", dialog, new Color(0.886f, 0.706f, 0.373f, 0.85f)); Place(edge, 0, 0, area.width, 3);
            speaker = MakeText(dialog, 34, 16, 600, 40, 26, new Color(0.886f, 0.706f, 0.373f), FontStyle.Bold);
            line = MakeText(dialog, 34, 62, area.width - 68, area.height - 78, 29, new Color(0.914f, 0.933f, 0.973f), FontStyle.Normal);
            dialog.gameObject.SetActive(false);
            // The "tap to continue" mark sits at the dialogue box's lower right; with no dialogue it shows at the same spot.
            waitMark = MakeText(parent, area.x + area.width - 56, area.y + area.height - 50, 40, 40, 26, new Color(0.886f, 0.706f, 0.373f), FontStyle.Bold);
            waitMark.text = "▼"; waitMark.alignment = TextAnchor.MiddleCenter; waitMark.gameObject.SetActive(false);
            skip = MakeButton("MomentSkip", parent, new Color(0.08f, 0.11f, 0.19f, 0.85f), Skip);
            Place((RectTransform)skip.transform, 1404, 24, 172, 52);
            var label = MakeText(skip.transform, 0, 0, 172, 52, 22, new Color(0.914f, 0.933f, 0.973f), FontStyle.Normal); label.text = "건너뛰기"; label.alignment = TextAnchor.MiddleCenter;
            skip.gameObject.SetActive(false);
            music = gameObject.AddComponent<AudioSource>(); music.loop = true; sfx = gameObject.AddComponent<AudioSource>();
        }

        public void Bind(MomentFile? file)
        {
            Stop();
            File = file; still = null;
            Rest();
        }

        // Shows a still image outside any moment file (a character without moments yet).
        public void ShowStill(Sprite? image)
        {
            Bind(null);
            still = image; Rest();
        }

        public void Play(MomentScene scene)
        {
            if (File == null) return;
            Stop();
            Started?.Invoke(scene);
            routine = StartCoroutine(Run(scene));
        }

        // Stopping a running scene still reports it finished, so hosts that dimmed their UI for it restore it.
        public void Stop()
        {
            var interrupted = running ? current : null;
            if (routine != null) StopCoroutine(routine);
            routine = null; running = false;
            try { Cleanup(instant: true); }
            finally { if (interrupted != null) Finished?.Invoke(interrupted); }
        }

        public void Tap() => tapped = true;
        public void Skip() => skipped = true;

        IEnumerator Run(MomentScene scene)
        {
            current = scene; tapped = skipped = false; running = true;
            try
            {
                tapCatcher.gameObject.SetActive(true);
                skip.gameObject.SetActive(scene.Input.Skippable);
                if (scene.Stage?.Background != null) Backdrop(scene.Stage.Background);
                if (scene.Stage?.Music != null) Music(scene.Stage.Music);
                bool tapMode = scene.Input.Advance != "auto";
                foreach (var beat in scene.Beats)
                {
                    if (skipped) break;
                    Apply(beat);
                    // Order (MOMENTS_FORMAT.md): reveal the line, hold the authored pause (taps never cut it), then in tap mode wait
                    // for a tap. A tap made during the hold is kept and advances as soon as the hold ends.
                    if (beat.Line != null && beat.Line.Text.Length != 0)
                    {
                        var say = Say(beat.Line, tapMode);
                        while (!skipped && say.MoveNext()) yield return say.Current;
                    }
                    for (float t = 0; t < beat.Hold && !skipped; t += Time.deltaTime) yield return null;
                    if (tapMode) { var wait = WaitTap(); while (!skipped && wait.MoveNext()) yield return wait.Current; }
                }
            }
            finally
            {
                // Normal end, skip or an exception inside a step: clean up and report the scene finished exactly once.
                // (Stop() stops the coroutine without running this block and reports the interruption itself.)
                running = false;
                try { Cleanup(instant: skipped); }
                finally { Finished?.Invoke(scene); }
            }
        }

        // One beat's visual and sound changes. A failure is recorded and that change skipped; the scene keeps its timing.
        void Apply(MomentBeat beat)
        {
            try
            {
                if (beat.Figure != null) { StopMotion("figure#frames"); ShowFigure(beat.Figure); } // a new picture ends the slot's frame sequence, not its track
                if (beat.Overlays != null) foreach (var pair in beat.Overlays) Overlay(pair.Key, pair.Value);
                if (beat.Motion != null) StartMotion(beat.Motion);
                if (beat.Camera != null) Frame(beat.Camera);
                if (beat.Music != null) Music(beat.Music);
                if (beat.Sfx != null) Sound(beat.Sfx);
                if (beat.Voice != null) Sound(beat.Voice);
            }
            catch (Exception e) { LastError = e.GetType().Name + ": " + e.Message; Debug.LogWarning("Moment beat skipped: " + LastError); }
        }

        // Typewriter per page. A tap shows the rest of the page; pages before the last wait for a tap (tap mode) or the reading
        // time (auto); the last page returns so the beat's hold and tap rule apply once.
        IEnumerator Say(MomentLine said, bool tapMode)
        {
            if (!dialog.gameObject.activeSelf)
            {
                dialog.gameObject.SetActive(true);
                var group = dialog.GetComponent<CanvasGroup>(); if (group == null) group = dialog.gameObject.AddComponent<CanvasGroup>();
                StartCoroutine(MemoryForgeUiMotion.Fade(group, 0, 1, 0.18f));
            }
            speaker.text = said.Speaker == null ? "" : NameOf?.Invoke(said.Speaker) ?? said.Speaker;
            var pages = Pages(said.Text);
            for (int p = 0; p < pages.Count; p++)
            {
                var page = pages[p];
                tapped = false;
                for (float shown = 0; shown < page.Length; shown += Time.deltaTime * CharsPerSecond)
                {
                    if (tapped) { tapped = false; break; }
                    line.text = page.Substring(0, Mathf.Min(page.Length, (int)shown + 1));
                    yield return null;
                }
                line.text = page;
                if (tapMode && p < pages.Count - 1) { var wait = WaitTap(); while (wait.MoveNext()) yield return wait.Current; }
                else if (!tapMode) for (float t = 0, read = Mathf.Max(1.2f, page.Length * 0.07f); t < read; t += Time.deltaTime) yield return null;
            }
        }

        IEnumerator WaitTap()
        {
            WaitingForTap = true; waitMark.gameObject.SetActive(true);
            try { while (!tapped) yield return null; }
            finally { tapped = false; WaitingForTap = false; if (waitMark != null) waitMark.gameObject.SetActive(false); }
        }

        // Splits a line into pages that fit the dialogue box at its font size. Pages are exact slices: joined, they are the
        // authored text, whitespace and line breaks included.
        public List<string> Pages(string text)
        {
            var pages = new List<string>();
            var settings = line.GetGenerationSettings(line.rectTransform.rect.size);
            settings.resizeTextForBestFit = false; settings.verticalOverflow = VerticalWrapMode.Truncate;
            var generator = new TextGenerator();
            for (int start = 0; start < text.Length;)
            {
                string rest = text.Substring(start);
                generator.Populate(rest, settings);
                int fit = Mathf.Clamp(generator.characterCountVisible, 1, rest.Length);
                if (fit < rest.Length) { int space = rest.LastIndexOfAny(new[] { ' ', '\n' }, fit - 1, fit); if (space > fit / 2) fit = space + 1; }
                pages.Add(rest.Substring(0, fit));
                start += fit;
            }
            return pages;
        }

        // ── stage state ──
        // Never throws: a rest picture that fails to load leaves the slot empty and is recorded in LastError.
        void Rest()
        {
            ClearOverlays();
            try
            {
                if (File != null) ShowFigure(File.RestFigure());
                else { CurrentFigure = null; SetSprite(figure, still); }
            }
            catch (Exception e) { LastError = e.GetType().Name + ": " + e.Message; SetSprite(figure, null); }
        }

        void Cleanup(bool instant)
        {
            EndMotions();
            music.Stop(); sfx.Stop();
            backdrop.enabled = false;
            dialog.gameObject.SetActive(false); skip.gameObject.SetActive(false); tapCatcher.gameObject.SetActive(false);
            waitMark.gameObject.SetActive(false); WaitingForTap = false;
            Rest();
            if (cameraMove != null) StopCoroutine(cameraMove);
            if (instant || MemoryForgeUiMotion.Reduced) { lens.anchoredPosition = Vector2.zero; lens.localScale = Vector3.one; }
            else cameraMove = StartCoroutine(MoveCamera(Vector2.zero, 1, 0.45f));
        }

        void ShowFigure(string? key)
        {
            CurrentFigure = key;
            SetSprite(figure, key == null ? null : SpriteFor(key));
        }

        void SetSprite(Image image, Sprite? s)
        {
            image.sprite = s; image.enabled = s != null;
            if (s == null) return;
            float aspect = s.rect.width / Mathf.Max(1, s.rect.height);
            image.rectTransform.sizeDelta = new Vector2(FigureHeight * aspect, FigureHeight);
        }

        Sprite? SpriteFor(string key) => File != null && File.Images.TryGetValue(key, out var path) ? sprite(path) : null;

        // An explicitly requested background that does not exist yet shows as an empty dark stage, not the host's backdrop.
        void Backdrop(string key)
        {
            var s = SpriteFor(key);
            backdrop.enabled = true; backdrop.sprite = s; backdrop.color = s == null ? Empty : Color.white; backdrop.preserveAspect = false;
        }

        // Overlay value: an image key (same frame as the figure), {"image","x","y"} (offset in authored pixels), or null to remove.
        void Overlay(string slot, JToken? value)
        {
            string? key = value == null || value.Type == JTokenType.Null ? null : value.Type == JTokenType.String ? (string?)value : (string?)value["image"];
            StopMotion(slot + "#frames"); // replacing or removing a slot's picture ends that slot's frame sequence
            if (key == null)
            {
                StopMotion(slot + "#track");
                if (overlays.TryGetValue(slot, out var gone)) { Discard(gone); overlays.Remove(slot); }
                slotBase.Remove(slot);
                return;
            }
            bool existed = overlays.TryGetValue(slot, out var image);
            if (!existed)
            {
                image = new GameObject("Overlay:" + slot, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                image.transform.SetParent(figure.transform, false); image.preserveAspect = true; image.raycastTarget = false;
                overlays[slot] = image;
            }
            SetSprite(image, SpriteFor(key));
            image.rectTransform.sizeDelta = figure.rectTransform.sizeDelta;
            float k = RefWidth / AuthoredWidth;
            var basePosition = value!.Type == JTokenType.Object ? new Vector2((float?)value["x"] ?? 0, -((float?)value["y"] ?? 0)) * k : Vector2.zero;
            // Keep the offset a track has already applied (running, finished or reduced to its last pose) and move the base.
            var offset = existed && slotBase.TryGetValue(slot, out var oldBase) ? image.rectTransform.anchoredPosition - oldBase : Vector2.zero;
            slotBase[slot] = basePosition;
            image.rectTransform.anchoredPosition = basePosition + offset;
        }

        void ClearOverlays()
        {
            foreach (var o in overlays) { StopMotion(o.Key + "#frames"); StopMotion(o.Key + "#track"); Discard(o.Value); }
            overlays.Clear(); slotBase.Clear();
        }

        // Detach before the deferred destroy so a slot is gone at once, not at the end of the frame.
        static void Discard(Image? image)
        {
            if (image == null) return;
            image.transform.SetParent(null, false); image.gameObject.SetActive(false); Destroy(image.gameObject);
        }

        RectTransform? SlotRect(string slot) => slot == "figure" ? figure.rectTransform : overlays.TryGetValue(slot, out var o) ? o.rectTransform : null;

        void StartMotion(string key)
        {
            if (File == null || !File.Motions.TryGetValue(key, out var motion)) { LastError = "motion " + key + " not in file"; return; }
            if (motion.Frames.Count != 0) Restart(motion.Slot + "#frames", Frames(motion));
            foreach (var track in motion.Tracks) Restart(track.Key + "#track", Track(track.Key, track.Value, motion));
        }

        void StopMotion(string id)
        {
            if (motions.TryGetValue(id, out var job) && job != null) StopCoroutine(job);
            motions.Remove(id);
        }

        void Restart(string id, IEnumerator body)
        {
            StopMotion(id);
            motions[id] = StartCoroutine(body);
        }

        IEnumerator Frames(MomentMotion motion)
        {
            var image = motion.Slot == "figure" ? figure : overlays.TryGetValue(motion.Slot, out var o) ? o : null;
            if (image == null) yield break;
            var frames = motion.Frames.Select(sprite).ToList();
            if (MemoryForgeUiMotion.Reduced) { SetSprite(image, frames.LastOrDefault()); yield break; } // final pose only
            float step = 1f / motion.Fps;
            do
                foreach (var f in frames)
                {
                    if (image == null) yield break;
                    SetSprite(image, f);
                    yield return new WaitForSeconds(step);
                }
            while (motion.Loop);
        }

        IEnumerator Track(string slot, List<MomentKey> keys, MomentMotion motion)
        {
            var target = SlotRect(slot);
            if (target == null || keys.Count == 0) yield break;
            var image = target.GetComponent<Image>();
            float end = MemoryForgeUiMotion.Reduced ? 0 : keys[keys.Count - 1].T, k = RefWidth / AuthoredWidth;
            while (true)
            {
                for (float t = 0; ; t += Time.deltaTime)
                {
                    if (target == null) yield break;
                    var pose = Sample(keys, MemoryForgeUiMotion.Reduced ? keys[keys.Count - 1].T : Mathf.Min(t, end), motion.Ease);
                    var home = slot == "figure" ? figureHome : slotBase.TryGetValue(slot, out var b) ? b : Vector2.zero;
                    target.anchoredPosition = home + new Vector2(pose.X, -pose.Y) * k;
                    target.localEulerAngles = new Vector3(0, 0, pose.Rot);
                    target.localScale = Vector3.one * pose.Scale;
                    if (image != null) { var c = image.color; c.a = pose.Alpha; image.color = c; }
                    if (t >= end) break;
                    yield return null;
                }
                if (!motion.Loop || MemoryForgeUiMotion.Reduced) yield break;
                yield return null; // every loop pass yields, so even a degenerate loop can never hold the frame
            }
        }

        public static MomentKey Sample(List<MomentKey> keys, float t, string ease)
        {
            if (t <= keys[0].T) return keys[0];
            for (int i = 1; i < keys.Count; i++)
            {
                if (t > keys[i].T) continue;
                var a = keys[i - 1]; var b = keys[i];
                float u = Ease((t - a.T) / Mathf.Max(0.0001f, b.T - a.T), ease);
                return new MomentKey { T = t, X = Mathf.Lerp(a.X, b.X, u), Y = Mathf.Lerp(a.Y, b.Y, u), Rot = Mathf.Lerp(a.Rot, b.Rot, u), Scale = Mathf.Lerp(a.Scale, b.Scale, u), Alpha = Mathf.Lerp(a.Alpha, b.Alpha, u) };
            }
            return keys[keys.Count - 1];
        }

        static float Ease(float u, string ease) => ease switch
        {
            "in" => u * u,
            "out" => 1 - (1 - u) * (1 - u),
            "inOut" => u < 0.5f ? 2 * u * u : 1 - Mathf.Pow(-2 * u + 2, 2) / 2,
            _ => u
        };

        // Framing: scale the stage by the shot and move it so the named anchor of the current figure sits above the dialogue box.
        void Frame(MomentCamera shot)
        {
            float scale = ShotScale.TryGetValue(shot.Shot, out var s) ? s : 1f;
            Vector2 focus = figureHome;
            if (scale > 1f && shot.Focus != null && CurrentFigure != null && File != null && File.Anchors.TryGetValue(CurrentFigure, out var anchors) && anchors.TryGetValue(shot.Focus, out var uv))
            {
                var size = figure.rectTransform.sizeDelta;
                focus = figureHome + new Vector2((uv[0] - 0.5f) * size.x, (0.5f - uv[1]) * size.y);
            }
            var targetPoint = new Vector2(figureHome.x, scale > 1f ? 150 : 0);
            Vector2 position = scale > 1f ? targetPoint - focus * scale : Vector2.zero;
            float duration = shot.Move == "none" || MemoryForgeUiMotion.Reduced ? 0 : shot.Duration ?? 0.8f;
            if (cameraMove != null) StopCoroutine(cameraMove);
            cameraMove = StartCoroutine(MoveCamera(position, scale, duration));
        }

        IEnumerator MoveCamera(Vector2 position, float scale, float duration)
        {
            Vector2 p0 = lens.anchoredPosition; float s0 = lens.localScale.x;
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                float u = Ease(t / duration, "inOut");
                lens.anchoredPosition = Vector2.Lerp(p0, position, u); lens.localScale = Vector3.one * Mathf.Lerp(s0, scale, u);
                yield return null;
            }
            lens.anchoredPosition = position; lens.localScale = Vector3.one * scale; cameraMove = null;
        }

        void Music(string cue)
        {
            if (cue == "silence") { music.Stop(); return; }
            var clip = Resources.Load<AudioClip>("Audio/Music/" + cue);
            if (clip == null) return;
            if (music.clip != clip) music.clip = clip;
            if (!music.isPlaying) music.Play(); // also resumes the same cue after a silence
        }

        void Sound(string key)
        {
            var clip = Resources.Load<AudioClip>("Art/" + StripExtension(key)) ?? Resources.Load<AudioClip>("Audio/Sfx/" + key);
            if (clip != null) sfx.PlayOneShot(clip);
        }

        void EndMotions()
        {
            foreach (var m in motions.Values) if (m != null) StopCoroutine(m);
            motions.Clear();
            var r = figure.rectTransform;
            r.anchoredPosition = figureHome; r.localEulerAngles = Vector3.zero; r.localScale = Vector3.one;
            var c = figure.color; c.a = 1; figure.color = c;
        }

        public static string StripExtension(string path) { int dot = path.LastIndexOf('.'); return dot > path.LastIndexOf('/') ? path.Substring(0, dot) : path; }

        // ── small UI helpers (1600x900 reference, top-left coordinates) ──
        static RectTransform Rect(string name, Transform parent, Color? color = null)
        {
            var obj = new GameObject(name, typeof(RectTransform)); obj.transform.SetParent(parent, false);
            if (color.HasValue) { var img = obj.AddComponent<Image>(); img.color = color.Value; img.raycastTarget = false; }
            return (RectTransform)obj.transform;
        }
        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; r.pivot = new Vector2(0.5f, 0.5f); }
        static void Place(RectTransform r, float x, float y, float w, float h) { r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); }
        Text MakeText(Transform parent, float x, float y, float w, float h, int size, Color color, FontStyle style)
        {
            var r = Rect("Text", parent); Place(r, x, y, w, h);
            var text = r.gameObject.AddComponent<Text>(); text.font = font; text.fontSize = size; text.color = color; text.fontStyle = style; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; text.lineSpacing = 1.15f;
            return text;
        }
        static Button MakeButton(string name, Transform parent, Color color, Action click)
        {
            var r = Rect(name, parent, color); var image = r.GetComponent<Image>(); image.raycastTarget = true;
            var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => click());
            return button;
        }
    }
}
