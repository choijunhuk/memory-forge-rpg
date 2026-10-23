// Host adapter for the content modes: game data progression + a data/content_modes.json-shaped document → ContentModesData, and the
// JSON graph → typed account/party/option readers the C# check uses. Shapes outside the source's are rejected, never coerced.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using MemoryForge.Core;

static class ContentModesDataLoader
{
    public static ContentModesData Load(JsonElement progression, JsonElement modes, RunData run)
    {
        var data = new ContentModesData { Run = run };
        var resets = modes.GetProperty("resets");
        data.Resets = new ContentResets { UtcOffsetMinutes = I(resets, "utcOffsetMinutes"), DailyHour = I(resets, "dailyHour"), WeeklyWeekday = I(resets, "weeklyWeekday"), WeeklyHour = I(resets, "weeklyHour") };

        var dungeon = modes.GetProperty("dungeon"); var waves = dungeon.GetProperty("waves"); var stars = progression.GetProperty("dungeonStars");
        data.Dungeon = new ContentDungeonData
        {
            SweepUnlock = I(stars, "sweepUnlock"), SweepMax = I(stars, "sweepMax"), MaxTurns = I(dungeon.GetProperty("stars"), "maxTurns"), MinHpPct = I(dungeon.GetProperty("stars"), "minHpPct"),
            MinWaves = I(waves, "minWaves"), MaxWaves = I(waves, "maxWaves"),
            Encounters = Nullable(waves.GetProperty("encounters"), e => e.EnumerateObject().ToDictionary(d => d.Name, d => d.Value.EnumerateObject().ToDictionary(t => t.Name, t => Lineups(t.Value), StringComparer.Ordinal), StringComparer.Ordinal))
        };
        foreach (var d in progression.GetProperty("dungeons").EnumerateArray())
            data.Dungeon.Dungeons.Add(new ContentDungeonDef { Id = S(d, "id"), Unlock = I(d, "unlock"), Cost = I(d, "cost"), Yield = Ints(d.GetProperty("yield")) });
        foreach (var t in progression.GetProperty("dungeonTiers").EnumerateArray())
            data.Dungeon.Tiers.Add(new ContentDungeonTier { Tier = I(t, "tier"), Stage = I(t, "stage"), Pct = I(t, "pct"), HpBps = O(t, "hpBps"), DmgBps = O(t, "dmgBps") });

        var weekly = progression.GetProperty("weeklyBoss");
        data.WeeklyBoss = new ContentWeeklyData { Unlock = I(weekly, "unlock"), ClaimsPerWeek = I(weekly, "claimsPerWeek"), PerClaim = Ints(weekly.GetProperty("perClaim")),
            Bosses = Strings(modes.GetProperty("weeklyBoss").GetProperty("bosses")) };
        foreach (var row in weekly.GetProperty("difficulty").EnumerateArray())
            data.WeeklyBoss.Difficulty.Add(new ContentWeeklyLevel { Lv = I(row, "lv"), Stage = I(row, "stage"), HpBps = O(row, "hpBps"), DmgBps = O(row, "dmgBps") });

        var rift = progression.GetProperty("recordRift"); var m = modes.GetProperty("rift");
        data.Rift = new ContentRiftData
        {
            Unlock = I(rift, "unlock"), CycleDays = I(rift, "cycleDays"), CyclesPerSeason = I(rift, "cyclesPerSeason"), CycleStartHour = I(m, "cycleStartHour"),
            DistinctCharacters = I(m, "distinctCharacters"), SmallRosterMaxStars = I(m, "smallRosterMaxStars"),
            TwoTeamOrder = IntList(m.GetProperty("battles").GetProperty("twoTeamOrder")), OneTeamOrder = IntList(m.GetProperty("battles").GetProperty("oneTeamOrder")),
            StarPortionsPct = IntList(m.GetProperty("starPortionsPct")), ActiveTeamPolicy = S(m, "activeTeamPolicy"),
            MaxTotalTurns = Nullable(m.GetProperty("stars").GetProperty("maxTotalTurns"), e => (int?)e.GetInt32()), MinHpPct = Nullable(m.GetProperty("stars").GetProperty("minHpPct"), e => (int?)e.GetInt32()),
            Rotation = Nullable(m.GetProperty("rotation").GetProperty("cycles"), e => e.EnumerateArray().Select(c => Nullable(c, Cycle)).ToList())
        };
        foreach (var f in rift.GetProperty("floors").EnumerateArray())
            data.Rift.Floors.Add(new ContentRiftFloor { Floor = I(f, "floor"), Stage = I(f, "stage"), Reward = I(f, "reward"), HpBps = O(f, "hpBps"), DmgBps = O(f, "dmgBps") });
        // the "source" annotation strings sit beside the policy arrays and rule params; only arrays/objects are content
        foreach (var p in m.GetProperty("teamsByFloor").EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Array) data.Rift.TeamsByFloor.Add(p.Name, IntList(p.Value));
        foreach (var p in m.GetProperty("modifiers").EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Object) data.Rift.Modifiers.Add(p.Name, Ints(p.Value));

        var daily = modes.GetProperty("daily"); var missions = progression.GetProperty("missions").GetProperty("daily");
        data.Daily = new ContentDailyData { Unlock = I(daily, "unlock"), PointsCap = I(daily, "pointsCap"), ActivePolicy = S(daily, "activePolicy"),
            Goals = daily.GetProperty("goals").EnumerateObject().ToDictionary(g => g.Name, g => new ContentDailyGoal { Event = S(g.Value, "event"), N = I(g.Value, "n") }, StringComparer.Ordinal),
            TargetPool = Nullable(daily.GetProperty("targetPool").GetProperty("tasks"), e => e.EnumerateArray().Select(t => new ContentDailyTask { Id = S(t, "id"), Event = S(t, "event"), N = I(t, "n"), Pts = I(t, "pts") }).ToList()) };
        foreach (var t in missions.GetProperty("tasks").EnumerateArray()) data.Daily.MissionTasks.Add(new ContentDailyTask { Id = S(t, "id"), Pts = I(t, "pts") });
        foreach (var ms in missions.GetProperty("milestones").EnumerateArray())
            data.Daily.Milestones.Add(new ContentMilestone { At = I(ms, "at"), Grant = ms.EnumerateObject().Where(p => p.Name != "at").ToDictionary(p => p.Name, p => p.Value.GetInt32(), StringComparer.Ordinal) });

        var xp = progression.GetProperty("accountLevel").GetProperty("xpSources");
        data.Xp = new ContentXpSources { StaminaSpent = I(xp, "staminaSpent"), DailyMissionComplete = I(xp, "dailyMissionComplete"), WeeklyBossClaim = I(xp, "weeklyBossClaim") };
        return data;
    }
    static ContentRiftCycle Cycle(JsonElement json)
    {
        BattleDataLoader.Fields(json, "rules", "encounters");
        return new ContentRiftCycle
        {
            Rules = Nullable(json.GetProperty("rules"), e => e.EnumerateArray().Select(r => r.ValueKind == JsonValueKind.Null ? null : r.GetString()).ToList()),
            Encounters = Nullable(json.GetProperty("encounters"), e => e.EnumerateArray().Select(f => Nullable(f, Lineups)).ToList())
        };
    }
    static List<List<string>> Lineups(JsonElement json) => json.EnumerateArray().Select(Strings).ToList();

    // ───── graph readers for op inputs ─────
    /// <summary>A recorded account. An absent level/stamina is not a number in the source (INVALID_ACCOUNT); -1 keeps it invalid here.</summary>
    public static ContentAccount? Account(object? graph)
    {
        if (graph == null) return null;
        var g = graph as Dictionary<string, object?> ?? throw new InvalidDataException("Account must be an object");
        foreach (string key in g.Keys) if (!new[] { "level", "stamina", "owned", "dungeons", "weekly", "rift", "daily" }.Contains(key)) throw new InvalidDataException("Unexpected account field " + key);
        var a = new ContentAccount { Level = g.TryGetValue("level", out var level) ? Int(level) : -1, Stamina = g.TryGetValue("stamina", out var stamina) ? Int(stamina) : -1,
            Owned = g.TryGetValue("owned", out var owned) && owned is System.Collections.IList list ? list.Cast<object?>().Select(o => o == null ? null! : (string)o).ToList() : null! };
        if (g.TryGetValue("dungeons", out var d) && d != null) a.Dungeons = ((Dictionary<string, object?>)d).ToDictionary(p => p.Key, p => Int(p.Value), StringComparer.Ordinal);
        if (g.TryGetValue("weekly", out var w) && w is Dictionary<string, object?> weekly) a.Weekly = new ContentWeeklyLedger { Key = weekly["key"] as string, Claimed = Int(weekly["claimed"]) };
        if (g.TryGetValue("rift", out var r) && r is Dictionary<string, object?> rift)
            a.Rift = new ContentRiftLedger { Key = rift["key"] as string, Floors = ((Dictionary<string, object?>)rift["floors"]!).ToDictionary(p => int.Parse(p.Key, NumberStyles.None, CultureInfo.InvariantCulture), p => Int(p.Value)) };
        if (g.TryGetValue("daily", out var y) && y is Dictionary<string, object?> daily)
            a.Daily = new ContentDailyLedger { Key = daily["key"] as string, Counts = ((Dictionary<string, object?>)daily["counts"]!).ToDictionary(p => p.Key, p => Int(p.Value), StringComparer.Ordinal),
                Claimed = ((System.Collections.IList)daily["claimed"]!).Cast<object?>().Select(Int).ToList() };
        return a;
    }
    public static List<RunPartyEntry?>? Party(object? graph) => graph == null ? null : ((List<object?>)graph).Select(p => p == null ? null : RunPartyEntry.FromJsonGraph(p)).ToList();
    // recorded JSON integers arrive as long; a C# ToJsonGraph re-read (merged refs) carries int
    public static int Int(object? value) => value is int i ? i : value is long n && n >= int.MinValue && n <= int.MaxValue ? (int)n : throw new InvalidDataException("Expected Int32");
    static T? Nullable<T>(JsonElement json, Func<JsonElement, T> read) where T : class => json.ValueKind == JsonValueKind.Null ? null : read(json);
    static int? Nullable(JsonElement json, Func<JsonElement, int?> read) => json.ValueKind == JsonValueKind.Null ? null : read(json);
    static int I(JsonElement json, string key) => json.GetProperty(key).GetInt32();
    static int O(JsonElement json, string key) => json.TryGetProperty(key, out var value) ? value.GetInt32() : 0;
    static string S(JsonElement json, string key) => json.GetProperty(key).GetString() ?? throw new InvalidDataException("Expected string " + key);
    static List<string> Strings(JsonElement json) => json.EnumerateArray().Select(s => s.GetString() ?? throw new InvalidDataException("Expected string")).ToList();
    static List<int> IntList(JsonElement json) => json.EnumerateArray().Select(n => n.GetInt32()).ToList();
    static Dictionary<string, int> Ints(JsonElement json) => json.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32(), StringComparer.Ordinal);
}
