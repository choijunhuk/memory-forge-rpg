// §11.5/§11.6/§11.7/§11.13/§13.3: replays tools/gen_content_modes_vectors.mjs op scripts against the C# content modes and compares every
// result, error, state hash and checkpoint state. Expected values are comparison data only, never engine input.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MemoryForge.Core;

static class ContentModesChecks
{
    const string Schema = "mf-content-modes-v1";
    const string Scope = "Actual content-mode op scripts: reset clocks, party setup, stage scaling, rift_break data, ModeBattle waves and six rift rules, " +
        "dungeon/weekly boss/record rift/daily ledgers; DECISION_PENDING data only as TEST-ONLY modes variants";
    static readonly Dictionary<string, string> Sources = new(StringComparer.Ordinal)
    {
        ["data"] = "data/game_data.json", ["contentModes"] = "data/content_modes.json", ["engine"] = "engine/rules-core.js", ["contentEngine"] = "engine/content-modes.js",
        ["lateEnemies"] = "proposals/late_enemies.cjs", ["canonical"] = "tools/gen_traces.mjs", ["generator"] = "tools/gen_content_modes_vectors.mjs"
    };
    static int checks, failures, ops, errors, commands;
    static void Check(bool ok, string label) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL ContentModes " + label); } }
    static void Require(bool ok, string label) { if (!ok) throw new InvalidDataException(label); }

    public static int Run(string root)
    {
        checks = failures = ops = errors = commands = 0;
        try
        {
            using var fixture = JsonDocument.Parse(new GZipStream(File.OpenRead(Path.Combine(root, "engine", "tests", "vectors", "content_modes_vectors.json.gz")), CompressionMode.Decompress));
            using var game = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
            using var shippedModes = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "content_modes.json")));
            var f = fixture.RootElement; var progression = game.RootElement.GetProperty("progression");
            Require(Text(f.GetProperty("schema")) == Schema && Text(f.GetProperty("scope")) == Scope, "Wrong content-mode fixture schema/scope");
            Require(Text(f.GetProperty("dataVersion")) == Text(game.RootElement.GetProperty("meta").GetProperty("version")), "Wrong content-mode data version");
            var records = f.GetProperty("sources").EnumerateObject().ToList();
            Require(records.Count == Sources.Count && records.All(r => Sources.TryGetValue(r.Name, out var path) && Text(r.Value.GetProperty("path")) == path
                && Text(r.Value.GetProperty("sha256")) == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, path))))), "Stale/substituted content-mode source");
            Require(JsonSerializer.Serialize(f.GetProperty("modes").GetProperty("shipped")) == JsonSerializer.Serialize(shippedModes.RootElement), "Shipped modes variant differs from data/content_modes.json");

            var runs = new Dictionary<string, RunData>(StringComparer.Ordinal);
            foreach (var patch in f.GetProperty("dataPatches").EnumerateObject()) runs.Add(patch.Name, PatchedRun(root, patch.Value));
            var cache = new Dictionary<string, ContentModesData>(StringComparer.Ordinal);
            ContentModesData Modes(string data, string modes)
            {
                string key = data + "|" + modes;
                if (!cache.TryGetValue(key, out var value)) cache.Add(key, value = ContentModesDataLoader.Load(progression, f.GetProperty("modes").GetProperty(modes), runs[data]));
                return value;
            }
            NativeChecks(Modes("live", "shipped"), root);
            foreach (var row in f.GetProperty("cases").EnumerateArray()) Replay(row, Modes);
            var counts = f.GetProperty("counts");
            Check(ops == counts.GetProperty("ops").GetInt32() && errors == counts.GetProperty("errors").GetInt32()
                && commands == counts.GetProperty("battleCommands").GetInt32() + counts.GetProperty("riftCommands").GetInt32(), "op/error/command counts match the fixture");
            foreach (var w in f.GetProperty("witness").EnumerateObject()) Check(w.Value.GetInt32() > 0, "witness " + w.Name + " exercised");
        }
        catch (Exception error) { Check(false, "fixture/execution: " + error); }
        Console.WriteLine($"ContentModesChecks: {checks - failures}/{checks} passed; {ops} oracle ops, {errors} exact rejections, {commands} battle/rift commands");
        Console.WriteLine("ContentModesScope: C# ModeBattle/ledgers vs engine/content-modes.js; TEST-ONLY waves/rotation/thresholds/pool; server transaction, persisted RiftRun restore, Unity UI NOT_RUN here");
        return failures;
    }

    static RunData PatchedRun(string root, JsonElement patch)
    {
        var run = RunDataLoader.Load(root);
        foreach (var p in patch.EnumerateObject())
        {
            if (p.Name == "enemyHp") foreach (var e in p.Value.EnumerateObject()) run.Memory.Battle.Enemies.Single(x => x.Id == e.Name).Hp = e.Value.GetInt32();
            else if (p.Name == "overlayEnemies") run.Memory.Battle = BattleDataLoader.LoadWithEnemies(root, p.Value);
            else throw new InvalidDataException("Unknown data patch " + p.Name);
        }
        run.Memory.Battle.Index();
        return run;
    }

    // ───── op replay ─────
    static void Replay(JsonElement row, Func<string, string, ContentModesData> modes)
    {
        string id = Text(row.GetProperty("id"));
        var slots = new Dictionary<string, object>(StringComparer.Ordinal);
        int index = 0;
        foreach (var op in row.GetProperty("ops").EnumerateArray())
        {
            string kind = Text(op.GetProperty("op")), label = $"{id} op{index++} {kind}";
            var m = modes(Text(op.GetProperty("data")), Text(op.GetProperty("modes")));
            bool expectOk = Text(op.GetProperty("outcome")) == "ok";
            ops++; if (kind == "battleCmd" || kind == "riftCmd") commands++;
            object? actual = null; Exception? thrown = null;
            var accountBefore = op.TryGetProperty("account", out var accountInput) ? ResolveAccount(accountInput, slots) : null;
            var accountGraph = accountBefore?.ToJsonGraph();
            try { actual = Execute(kind, op, m, slots, accountBefore); }
            catch (RuleException e) { thrown = e; }
            catch (MemoryBoundaryException e) { thrown = e; }
            if (accountBefore != null) Same(accountGraph, accountBefore.ToJsonGraph(), label + " input account unchanged");
            if (expectOk)
            {
                Check(thrown == null, label + " succeeds" + (thrown == null ? "" : " (threw " + Code(thrown) + ")"));
                if (thrown == null) CompareResult(kind, op.GetProperty("result"), actual!, slots, op, label);
            }
            else
            {
                errors++;
                var error = op.GetProperty("error");
                string want = error.TryGetProperty("code", out var code) ? Text(code) : "native:" + Text(error.GetProperty("name"));
                Check(thrown != null && Code(thrown) == want, label + " exact error " + want + " (got " + (thrown == null ? "success" : Code(thrown)) + ")");
                if (op.TryGetProperty("after", out var after))
                {
                    string slot = Text(op.GetProperty("slot"));
                    if (kind == "battleCmd") CompareBattle(after, ((Battle)slots[slot]).State, label + " rejected command keeps state");
                    else CompareRun(after, ((RiftRun)slots[slot]).State, label + " state after rejection");
                }
            }
            if (expectOk && thrown == null && op.TryGetProperty("save", out var save)) slots[Text(save)] = actual!;
        }
    }
    static string Code(Exception e) => e is RuleException r ? r.Code : "native:" + ((MemoryBoundaryException)e).JavaScriptName;

    static object Execute(string kind, JsonElement op, ContentModesData m, Dictionary<string, object> slots, ContentAccount? account)
    {
        JsonElement P(string name) => op.GetProperty(name);
        object? G(string name) => op.TryGetProperty(name, out var value) ? MemoryV2DataLoader.Graph(value) : null;
        string? Policy() => op.TryGetProperty("policy", out var p) ? (string?)G("policy") : null;
        long Now() => P("nowMs").GetInt64();
        switch (kind)
        {
            case "time":
                string fn = Text(P("fn"));
                if (fn == "dayKey") return ContentModes.Time.DayKey(m, Now());
                if (fn == "weekKey") return ContentModes.Time.WeekKey(m, Now());
                var cycle = ContentModes.Time.RiftCycle(m, G("season") as string, Now());
                return MemoryJsonObject("index", cycle.Index, "key", cycle.Key, "startsAt", cycle.StartsAt, "endsAt", cycle.EndsAt);
            case "partySetup":
                return ContentModes.PartySetup(m.Run, ContentModesDataLoader.Party(G("party")), G("memory"), ((List<object?>)G("owned")!).Select(o => (string)o!).ToList()).ToJsonGraph();
            case "stageDiff":
            {
                var r = P("row"); var d = ContentModes.StageDiff(m.Run, new ContentStageRow { Stage = r.GetProperty("stage").GetInt32(),
                    HpBps = r.TryGetProperty("hpBps", out var h) ? h.GetInt32() : 0, DmgBps = r.TryGetProperty("dmgBps", out var g) ? g.GetInt32() : 0 });
                return MemoryJsonObject("hp", d.Hp, "dmg", d.Dmg);
            }
            case "modeData":
            {
                var source = m.Run.Memory.Battle; var before = source.Enemies.Select(e => e.Brk).ToList(); int brokenBefore = source.Rules.BrokenDamageBps;
                var output = ContentModes.ModeData(source, Mode(P("mode")));
                Check(source.Enemies.Select(e => e.Brk).SequenceEqual(before) && source.Rules.BrokenDamageBps == brokenBefore, "modeData leaves the shared data unchanged");
                return MemoryJsonObject("same", ReferenceEquals(output, source), "brokenDamageBps", output.Rules.BrokenDamageBps,
                    "enemies", output.Enemies.Select(e => (object?)MemoryJsonObject("id", e.Id, "brk", e.Brk, "phaseBrk", e.PhaseDescriptor()?.Brk)).ToList());
            }
            case "battleCreate":
            {
                var b = Battle.CreateMode(m.Run.Memory.Battle, BattleDataLoader.Setup(P("setup")), Mode(P("mode"))); slots[Text(P("slot"))] = b; return b;
            }
            case "battleCmd":
            {
                var b = (Battle)slots[Text(P("slot"))]; return b.Command(BattleDataLoader.Request(P("request")));
            }
            case "dungeonReward": return ContentModes.Dungeon.Reward(m, G("dungeonId") as string, (int?)(long?)G("tier"));
            case "dungeonStars":
            {
                var r = G("battleResult") as Dictionary<string, object?>;
                return ContentModes.Dungeon.Stars(m, r == null ? null : new BattleResult { Phase = (string)r["phase"]!, Turn = ContentModesDataLoader.Int(r["turn"]), Hp = ContentModesDataLoader.Int(r["hp"]) }, P("maxHp").GetInt32());
            }
            case "dungeonStart":
            {
                var o = G("opts") as Dictionary<string, object?> ?? throw new InvalidDataException("opts");
                var b = ContentModes.Dungeon.Start(m, account!, new DungeonStartOptions { DungeonId = Get(o, "dungeonId") as string, Tier = OptInt(o, "tier"),
                    Party = ContentModesDataLoader.Party(Get(o, "party")), Memory = Get(o, "memory"), Seed = OptUInt(o, "seed") });
                slots[Text(P("slot"))] = b; return b;
            }
            case "dungeonSettle": return ContentModes.Dungeon.Settle(m, account!, ResolveBattle(P("battle"), slots));
            case "dungeonSweep":
            {
                var a = (Dictionary<string, object?>)G("args")!;
                return ContentModes.Dungeon.Sweep(m, account!, Get(a, "dungeonId") as string, OptInt(a, "tier"), ContentModesDataLoader.Int(Get(a, "times")));
            }
            case "weeklyStart":
            {
                var o = (Dictionary<string, object?>)G("opts")!;
                var b = ContentModes.WeeklyBoss.Start(m, account!, new WeeklyBossStartOptions { BossId = Get(o, "bossId") as string, Difficulty = OptInt(o, "difficulty"),
                    Party = ContentModesDataLoader.Party(Get(o, "party")), Memory = Get(o, "memory"), Seed = OptUInt(o, "seed"), Practice = Get(o, "practice") is true });
                slots[Text(P("slot"))] = b; return b;
            }
            case "weeklySettle": return ContentModes.WeeklyBoss.Settle(m, account!, ResolveBattle(P("battle"), slots), Now());
            case "riftCreate":
            {
                var o = (Dictionary<string, object?>)G("opts")!; var c = Get(o, "cycle") as Dictionary<string, object?>;
                var run = RiftRun.Create(m, account!, new RiftRunOptions { Floor = OptInt(o, "floor"),
                    Cycle = c == null ? null : new RiftCycleRef { Index = OptInt(c, "index") ?? int.MinValue, Key = Get(c, "key") as string },
                    Teams = Get(o, "teams") is List<object?> teams ? teams.Select(ContentModesDataLoader.Party).ToList() : null,
                    Memory = Get(o, "memory"), Seed = OptUInt(o, "seed"), Policy = Get(o, "policy") as string });
                slots[Text(P("slot"))] = run; return run;
            }
            case "riftNext": { var run = (RiftRun)slots[Text(P("slot"))]; run.Next(); return run; }
            case "riftCmd": return ((RiftRun)slots[Text(P("slot"))]).BattleCommand(Text(P("kind")), BattleDataLoader.Argument(P("args")));
            case "riftStars": { var s = ((RiftRun)slots[Text(P("slot"))]).Stars(); return MemoryJsonObject("stars", s.Stars, "turns", s.Turns, "hp", s.Hp, "maxHp", s.MaxHp); }
            case "riftSettle": return ContentModes.Rift.Settle(m, account!, ResolveRift(P("rift"), slots));
            case "dailyTasks": return ContentModes.Daily.Tasks(m, Policy()).Select(t => (object?)t.ToJsonGraph()).ToList();
            case "dailyOpen": return ContentModes.Daily.Open(m, account!, Now());
            case "dailyRecord":
            {
                var e = G("event") as Dictionary<string, object?>;
                return ContentModes.Daily.Record(m, account!, e == null ? null : Get(e, "type") as string, e == null ? 0 : ContentModesDataLoader.Int(Get(e, "n")), Now(), Policy());
            }
            case "dailyPoints": return ContentModes.Daily.Points(m, account!, Now(), Policy());
            case "dailyClaim": return ContentModes.Daily.Claim(m, account!, P("at").GetInt32(), Now(), Policy());
            default: throw new InvalidDataException("Unknown op " + kind);
        }
    }

    static void CompareResult(string kind, JsonElement expected, object actual, Dictionary<string, object> slots, JsonElement op, string label)
    {
        switch (kind)
        {
            case "battleCreate": case "dungeonStart": case "weeklyStart": CompareBattle(expected, ((Battle)actual).State, label); return;
            case "battleCmd":
                Same(MemoryV2DataLoader.Graph(expected.GetProperty("response")), ((BattleResponse)actual).ToJsonGraph(), label + " response");
                CompareBattle(expected, ((Battle)slots[Text(op.GetProperty("slot"))]).State, label); return;
            case "riftCreate": case "riftNext": CompareRun(expected, ((RiftRun)actual).State, label); return;
            case "riftCmd":
                Same(MemoryV2DataLoader.Graph(expected.GetProperty("response")), ((BattleResponse)actual).ToJsonGraph(), label + " response");
                CompareRun(expected, ((RiftRun)slots[Text(op.GetProperty("slot"))]).State, label); return;
            case "dungeonSettle": case "dungeonSweep": case "weeklySettle": case "riftSettle": case "dailyClaim":
            {
                var s = (ContentSettlement)actual; var graph = new Dictionary<string, object?>(StringComparer.Ordinal) { ["account"] = s.Account.ToJsonGraph() };
                if (kind == "dungeonSettle" || kind == "weeklySettle") graph["battle"] = s.Battle!.ToJsonGraph();
                if (kind == "riftSettle") graph["rift"] = s.Rift!.ToJsonGraph();
                if (kind == "dungeonSettle" || kind == "riftSettle") graph["stars"] = s.Stars;
                graph["grant"] = s.Grant?.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal);
                if (kind == "weeklySettle") graph["reason"] = s.Reason;
                Same(MemoryV2DataLoader.Graph(expected), graph, label + " result"); return;
            }
            case "dailyOpen": case "dailyRecord": Same(MemoryV2DataLoader.Graph(expected), ((ContentAccount)actual).ToJsonGraph(), label + " account"); return;
            case "dungeonReward": Same(MemoryV2DataLoader.Graph(expected), ((Dictionary<string, int>)actual).ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal), label + " reward"); return;
            case "dungeonStars": case "dailyPoints": Check(expected.GetInt64() == (int)actual, label + " number"); return;
            default: Same(MemoryV2DataLoader.Graph(expected), actual, label + " result"); return;
        }
    }
    static void CompareBattle(JsonElement expected, BattleState state, string label)
    {
        var graph = state.ToJsonGraph();
        Check(CanonicalState.Hash(graph) == Text(expected.GetProperty("stateHash")), label + " state hash");
        Check(CanonicalState.Hash(graph["log"]) == Text(expected.GetProperty("logHash")) && CanonicalState.Hash(graph["ev"]) == Text(expected.GetProperty("evHash")), label + " log/ev hash");
        if (expected.TryGetProperty("state", out var full)) Same(BattleDataLoader.Graph(full), graph, label + " full state");
    }
    static void CompareRun(JsonElement expected, RiftRunState state, string label)
    {
        var graph = state.ToJsonGraph();
        Check(CanonicalState.Hash(graph) == Text(expected.GetProperty("runHash")), label + " rift state hash");
        var battleLog = expected.GetProperty("battleLogHash");
        Check(battleLog.ValueKind == JsonValueKind.Null ? state.Battle == null : state.Battle != null && CanonicalState.Hash(state.Battle.ToJsonGraph()["log"]) == Text(battleLog), label + " battle log hash");
        if (expected.TryGetProperty("state", out var full)) Same(BattleDataLoader.Graph(full), graph, label + " full rift state");
    }

    // ───── references: a recorded literal, or a value saved by an earlier op (copied, optionally merged/patched as the generator does) ─────
    static ContentAccount? ResolveAccount(JsonElement json, Dictionary<string, object> slots)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("ref", out var reference)) return ContentModesDataLoader.Account(MemoryV2DataLoader.Graph(json));
        var holder = slots[Text(reference)];
        string? path = json.TryGetProperty("path", out var p) ? Text(p) : null;
        var account = path == "account" ? ((ContentSettlement)holder).Account : path == null ? (ContentAccount)holder : throw new InvalidDataException("Account path " + path);
        var graph = account.ToJsonGraph();
        if (json.TryGetProperty("merge", out var merge)) foreach (var field in merge.EnumerateObject()) graph[field.Name] = MemoryV2DataLoader.Graph(field.Value);
        return ContentModesDataLoader.Account(graph);
    }
    static BattleState? ResolveBattle(JsonElement json, Dictionary<string, object> slots)
    {
        if (json.ValueKind == JsonValueKind.Null) return null;
        var holder = slots[Text(json.GetProperty("ref"))]; string path = Text(json.GetProperty("path"));
        BattleState state = path == "state" ? ((Battle)holder).State : path == "battle" ? ((ContentSettlement)holder).Battle! : throw new InvalidDataException("Battle path " + path);
        if (json.TryGetProperty("patch", out var patch))
        {
            Require(path == "state", "Only a detached Battle.State is patched");
            if (patch.TryGetProperty("phase", out var phase)) state.Phase = Text(phase);
            if (patch.TryGetProperty("result", out var result))
                state.Result = new BattleResult { Phase = state.Phase, Turn = result.TryGetProperty("turn", out var t) ? t.GetInt32() : state.Turn, Hp = result.TryGetProperty("hp", out var h) ? h.GetInt32() : state.Hp };
        }
        return state;
    }
    static RiftRunState? ResolveRift(JsonElement json, Dictionary<string, object> slots)
    {
        if (json.ValueKind == JsonValueKind.Null) return null;
        var holder = slots[Text(json.GetProperty("ref"))]; string path = Text(json.GetProperty("path"));
        var state = path == "state" ? ((RiftRun)holder).State : path == "rift" ? ((ContentSettlement)holder).Rift! : throw new InvalidDataException("Rift path " + path);
        if (json.TryGetProperty("patch", out var patch))
        {
            Require(path == "state", "Only a detached RiftRun.State is patched");
            if (patch.TryGetProperty("schema", out var schema)) state.Schema = Text(schema);
            if (patch.TryGetProperty("phase", out var phase)) state.Phase = Text(phase);
        }
        return state;
    }
    static BattleMode Mode(JsonElement json)
    {
        BattleDataLoader.Fields(json, "kind", "contentId", "rule", "params", "waves", "tier", "difficulty", "battle", "practice", "rewarded", "wave", "settled");
        var g = (Dictionary<string, object?>)MemoryV2DataLoader.Graph(json)!;
        return new BattleMode { Kind = Get(g, "kind") as string, ContentId = Get(g, "contentId") as string, Rule = Get(g, "rule") as string,
            Params = Get(g, "params") is Dictionary<string, object?> p ? p.ToDictionary(x => x.Key, x => ContentModesDataLoader.Int(x.Value), StringComparer.Ordinal) : null,
            Waves = Get(g, "waves") is List<object?> w ? w.Select(x => ((List<object?>)x!).Select(i => (string)i!).ToList()).ToList() : null!,
            Tier = OptInt(g, "tier"), Difficulty = OptInt(g, "difficulty"), Battle = OptInt(g, "battle"),
            Practice = Get(g, "practice") as bool?, Rewarded = Get(g, "rewarded") as bool?, Wave = OptInt(g, "wave") ?? 1, Settled = Get(g, "settled") is true };
    }
    static object? Get(Dictionary<string, object?> g, string key) => g.TryGetValue(key, out var value) ? value : null;
    static int? OptInt(Dictionary<string, object?> g, string key) => Get(g, key) is long n ? ContentModesDataLoader.Int(n) : (int?)null;
    static uint? OptUInt(Dictionary<string, object?> g, string key) => Get(g, key) is long n ? checked((uint)n) : (uint?)null;
    static Dictionary<string, object?> MemoryJsonObject(params object?[] pairs)
    {
        var graph = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (int i = 0; i < pairs.Length; i += 2) graph.Add((string)pairs[i]!, pairs[i + 1]);
        return graph;
    }

    // ───── checks that do not come from the oracle ─────
    static void NativeChecks(ContentModesData m, string root)
    {
        var data = m.Run.Memory.Battle;
        // shipped data keeps every DECISION_PENDING item unauthored
        Check(m.Dungeon.Encounters == null && m.Rift.Rotation == null && m.Rift.MaxTotalTurns == null && m.Rift.MinHpPct == null && m.Daily.TargetPool == null, "shipped DECISION_PENDING items stay null");
        Check(Battle.RiftRules.OrderBy(r => r, StringComparer.Ordinal).SequenceEqual(m.Rift.Modifiers.Keys.OrderBy(r => r, StringComparer.Ordinal)), "C# rule list equals content_modes.json modifiers");
        // a ModeBattle without waves or a rule is Battle.Create plus state.mode
        var setup = new BattleSetup { Hp = 243, MaxHp = 243, Seed = 77, Enemies = new List<string> { "e_moth", "e_cog" } };
        foreach (string cid in new[] { "sera", "harin", "rien" })
        {
            setup.Party.Add(new BattleActorSetup { CharId = cid });
            foreach (var spec in MemoryForge.Core.Run.BaseKit(m.Run, cid)) setup.Deck.Add(new BattleCardSpec { Id = spec.Id!, Assigned = spec.Assigned });
        }
        var plain = Battle.Create(data, setup).State.ToJsonGraph(); var mode = Battle.CreateMode(data, setup, new BattleMode { Kind = "test" }).State.ToJsonGraph();
        Check(mode.Remove("mode") && JsonGraphComparison.Difference(plain, mode, "$", 0) == null, "CreateMode without a rule equals Battle.Create");
        // typed diagnostics: incomplete rule params are refused instead of computing with a missing number
        foreach (var rule in Battle.RiftRules)
        {
            bool refused = false;
            try { Battle.CreateMode(data, setup, new BattleMode { Kind = "rift", Rule = rule, Params = new Dictionary<string, int>(StringComparer.Ordinal) }); }
            catch (RuleException e) { refused = e.Code == "UNKNOWN_RULE"; }
            Check(refused, "incomplete " + rule + " params refused");
        }
        // the rift_break copy is detached: changing every nested definition of it leaves the shared data and a plain battle as they were
        string Fingerprint(BattleData d) => string.Join("|", d.Enemies.Select(e => e.Brk + "/" + e.Pattern.Count + "/" + e.Weak.Count)) + "#" + d.Cards[0].Effects.Count + "#" + d.Characters[0].Resonance.Count
            + "#" + d.Weapons[0].Passive.Values.Count + "#" + d.Parts[0].Fx.AddBps + "#" + d.Relics[0].Name + "#" + d.Rules.ApMax + "#" + d.Rules.BrokenDamageBps;
        string before = Fingerprint(data); var plainBefore = Battle.Create(data, setup).State.ToJsonGraph();
        var broken = ContentModes.ModeData(data, new BattleMode { Rule = "rift_break", Params = new Dictionary<string, int>(m.Rift.Modifiers["rift_break"], StringComparer.Ordinal) });
        Check(!ReferenceEquals(broken, data) && broken.Rules.BrokenDamageBps == m.Rift.Modifiers["rift_break"]["brokenDamageBps"], "rift_break returns its own data copy");
        foreach (var e in broken.Enemies) { e.Pattern.Clear(); e.Weak.Clear(); e.Brk = -1; }
        broken.Cards[0].Effects.Clear(); broken.Characters[0].Resonance.Clear(); broken.Weapons[0].Passive.Values.Clear(); broken.Parts[0].Fx.AddBps = 99; broken.Relics[0].Name = "x"; broken.Rules.ApMax = 0;
        Check(Fingerprint(data) == before && JsonGraphComparison.Difference(plainBefore, Battle.Create(data, setup).State.ToJsonGraph(), "$", 0) == null, "changing the rift_break copy never reaches the shared data");
        bool timeRefused = false;
        try { ContentModes.Time.DayKey(m, CanonicalState.MaxSafeInteger + 1); } catch (RuleException e) { timeRefused = e.Code == "INVALID_TIME"; }
        Check(timeRefused, "a time beyond 2^53 ms has no exact JS number and is refused");
    }

    static void Same(object? expected, object? actual, string label)
    {
        string? difference = JsonGraphComparison.Difference(expected, actual, "$", 0);
        Check(difference == null, label + (difference == null ? "" : ": " + difference));
    }
    static string Text(JsonElement json) => json.GetString() ?? throw new InvalidDataException("Expected string");
}
