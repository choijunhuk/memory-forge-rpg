// Memory Forge v0.8.1 content modes: §11.5 성장 던전 · §11.6 주간 보스 · §11.7 기록 균열 · §11.13 일일 활동 · §13.3 reset clocks.
// Port of engine/content-modes.js. Ledger functions copy their inputs and never change them. Numbers come from game data progression
// and data/content_modes.json (host-loaded into ContentModesData); DECISION_PENDING items stay null and throw CONTENT_NOT_AUTHORED.
// Account, memory and party rows are server-held state; the client only names content and a party.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MemoryForge.Core
{
    // ───────────────────────── data (game data progression + data/content_modes.json) ─────────────────────────
    public sealed class ContentResets { public int UtcOffsetMinutes, DailyHour, WeeklyWeekday, WeeklyHour; }
    /// <summary>A content row's stage level and extra §11.1 scaling; 0 bps means absent, as the source's `||`.</summary>
    public class ContentStageRow { public int Stage, HpBps, DmgBps; }
    public sealed class ContentDungeonDef { public string Id = ""; public int Unlock, Cost; public Dictionary<string, int> Yield = new Dictionary<string, int>(StringComparer.Ordinal); }
    public sealed class ContentDungeonTier : ContentStageRow { public int Tier, Pct; }
    public sealed class ContentDungeonData
    {
        public List<ContentDungeonDef> Dungeons = new List<ContentDungeonDef>();
        public List<ContentDungeonTier> Tiers = new List<ContentDungeonTier>();
        public int SweepUnlock, SweepMax, MaxTurns, MinHpPct, MinWaves, MaxWaves;
        /// <summary>waves.encounters[dungeonId][tier as text] = waves of enemy ids; null while DECISION_PENDING.</summary>
        public Dictionary<string, Dictionary<string, List<List<string>>>>? Encounters;
    }
    public sealed class ContentWeeklyLevel : ContentStageRow { public int Lv; }
    public sealed class ContentWeeklyData
    {
        public int Unlock, ClaimsPerWeek;
        public Dictionary<string, int> PerClaim = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<ContentWeeklyLevel> Difficulty = new List<ContentWeeklyLevel>();
        public List<string> Bosses = new List<string>();
    }
    public sealed class ContentRiftFloor : ContentStageRow { public int Floor, Reward; }
    /// <summary>One authored rotation cycle: a rule and three enemy lineups per floor (index floor − 1).</summary>
    public sealed class ContentRiftCycle { public List<string?>? Rules; public List<List<List<string>>?>? Encounters; }
    public sealed class ContentRiftData
    {
        public int Unlock, CycleDays, CyclesPerSeason, CycleStartHour, DistinctCharacters, SmallRosterMaxStars;
        public List<ContentRiftFloor> Floors = new List<ContentRiftFloor>();
        public List<int> TwoTeamOrder = new List<int>(), OneTeamOrder = new List<int>(), StarPortionsPct = new List<int>();
        public Dictionary<string, List<int>> TeamsByFloor = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        public string ActiveTeamPolicy = "";
        /// <summary>Star 2/3 thresholds; null while DECISION_PENDING.</summary>
        public int? MaxTotalTurns, MinHpPct;
        /// <summary>Per-cycle rule/lineup assignment; null while DECISION_PENDING.</summary>
        public List<ContentRiftCycle?>? Rotation;
        public Dictionary<string, Dictionary<string, int>> Modifiers = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
    }
    public sealed class ContentDailyGoal { public string Event = ""; public int N; }
    public sealed class ContentDailyTask
    {
        public string Id = "";
        public string? Event;
        public int? N;
        public int Pts;
        internal ContentDailyTask Copy() => (ContentDailyTask)MemberwiseClone();
        public Dictionary<string, object?> ToJsonGraph()
        {
            var graph = BattleGraph.Object("id", Id, "pts", Pts); BattleGraph.Optional(graph, "event", Event); BattleGraph.Optional(graph, "n", N); return graph;
        }
    }
    public sealed class ContentMilestone { public int At; public Dictionary<string, int> Grant = new Dictionary<string, int>(StringComparer.Ordinal); }
    public sealed class ContentDailyData
    {
        public int Unlock, PointsCap;
        public string ActivePolicy = "";
        /// <summary>progression.missions.daily.tasks (id, pts); the v0.8.1 policy joins them with Goals.</summary>
        public List<ContentDailyTask> MissionTasks = new List<ContentDailyTask>();
        public Dictionary<string, ContentDailyGoal> Goals = new Dictionary<string, ContentDailyGoal>(StringComparer.Ordinal);
        /// <summary>The §11.13 selectable pool; null while DECISION_PENDING.</summary>
        public List<ContentDailyTask>? TargetPool;
        public List<ContentMilestone> Milestones = new List<ContentMilestone>();
    }
    public sealed class ContentXpSources { public int StaminaSpent, DailyMissionComplete, WeeklyBossClaim; }
    public sealed class ContentModesData
    {
        /// <summary>Battle definitions, stats, base kits and weapons (the same RunData expeditions use).</summary>
        public RunData Run = new RunData();
        public ContentResets Resets = new ContentResets();
        public ContentDungeonData Dungeon = new ContentDungeonData();
        public ContentWeeklyData WeeklyBoss = new ContentWeeklyData();
        public ContentRiftData Rift = new ContentRiftData();
        public ContentDailyData Daily = new ContentDailyData();
        public ContentXpSources Xp = new ContentXpSources();
    }

    // ───────────────────────── account ledger rows ─────────────────────────
    public sealed class ContentWeeklyLedger { public string? Key; public int Claimed; }
    public sealed class ContentRiftLedger { public string? Key; public Dictionary<int, int> Floors = new Dictionary<int, int>(); }
    public sealed class ContentDailyLedger
    {
        public string? Key;
        public Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<int> Claimed = new List<int>();
    }
    /// <summary>The account fields the content ledgers read and write. Other account fields stay with the server's row.</summary>
    public sealed class ContentAccount
    {
        public int Level, Stamina;
        public List<string> Owned = new List<string>();
        /// <summary>"dungeonId:tier" → best stars.</summary>
        public Dictionary<string, int> Dungeons = new Dictionary<string, int>(StringComparer.Ordinal);
        public ContentWeeklyLedger Weekly = new ContentWeeklyLedger();
        public ContentRiftLedger Rift = new ContentRiftLedger();
        public ContentDailyLedger Daily = new ContentDailyLedger();
        // normAccount's defaults: an absent ledger section starts empty.
        internal ContentAccount Copy() => new ContentAccount
        {
            Level = Level, Stamina = Stamina, Owned = new List<string>(Owned),
            Dungeons = new Dictionary<string, int>(Dungeons ?? new Dictionary<string, int>(), StringComparer.Ordinal),
            Weekly = Weekly == null ? new ContentWeeklyLedger() : new ContentWeeklyLedger { Key = Weekly.Key, Claimed = Weekly.Claimed },
            Rift = Rift == null ? new ContentRiftLedger() : new ContentRiftLedger { Key = Rift.Key, Floors = new Dictionary<int, int>(Rift.Floors ?? new Dictionary<int, int>()) },
            Daily = Daily == null ? new ContentDailyLedger() : new ContentDailyLedger { Key = Daily.Key,
                Counts = new Dictionary<string, int>(Daily.Counts ?? new Dictionary<string, int>(), StringComparer.Ordinal), Claimed = new List<int>(Daily.Claimed ?? new List<int>()) }
        };
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("level", Level, "stamina", Stamina, "owned", Owned == null ? null : new List<string>(Owned),
            "dungeons", Dungeons.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal),
            "weekly", BattleGraph.Object("key", Weekly.Key, "claimed", Weekly.Claimed),
            "rift", BattleGraph.Object("key", Rift.Key, "floors", Rift.Floors.ToDictionary(p => p.Key.ToString(CultureInfo.InvariantCulture), p => (object?)p.Value, StringComparer.Ordinal)),
            "daily", BattleGraph.Object("key", Daily.Key, "counts", Daily.Counts.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal), "claimed", Daily.Claimed.Cast<object?>().ToList()));
    }

    // ───────────────────────── inputs and results ─────────────────────────
    public sealed class DungeonStartOptions { public string? DungeonId; public int? Tier; public List<RunPartyEntry?>? Party; public object? Memory; public uint? Seed; }
    public sealed class WeeklyBossStartOptions { public string? BossId; public int? Difficulty; public List<RunPartyEntry?>? Party; public object? Memory; public uint? Seed; public bool Practice; }
    public sealed class RiftCycleRef { public int Index; public string? Key; }
    public sealed class RiftRunOptions
    {
        public int? Floor;
        public RiftCycleRef? Cycle;
        public List<List<RunPartyEntry?>?>? Teams;
        public object? Memory;
        public uint? Seed;
        /// <summary>"v0.8.1" | "target"; empty uses rift.activeTeamPolicy.</summary>
        public string? Policy;
    }
    public sealed class ContentPartySetup
    {
        public List<RunPartyEntry> Party = new List<RunPartyEntry>();
        public List<RunCardSpec> Deck = new List<RunCardSpec>();
        public int MaxHp;
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("party", Party.Select(p => (object?)p.ToJsonGraph()).ToList(),
            "deck", Deck.Select(c => (object?)c.ToJsonGraph()).ToList(), "maxHp", MaxHp);
    }
    /// <summary>A settle/sweep/claim result: the new account and, by function, the settled battle or rift state, stars, grant and reason.</summary>
    public sealed class ContentSettlement
    {
        public ContentAccount Account = new ContentAccount();
        public BattleState? Battle;
        public RiftRunState? Rift;
        public int? Stars;
        public Dictionary<string, int>? Grant;
        public string? Reason;
    }
    public sealed class RiftCycleInfo { public int Index; public string Key = ""; public long StartsAt, EndsAt; }
    public sealed class RiftStars { public int Stars, Turns, Hp, MaxHp; }

    public static class ContentModes
    {
        const int Bps = 10000;

        // ───────────────────────── reset clocks (§13.3) ─────────────────────────
        public static class Time
        {
            const long Day = 86400000, Hour = 3600000, DateLimit = 8640000000000000;
            /// <summary>KST day that starts at 05:00.</summary>
            public static string DayKey(ContentModesData modes, long nowMs) => IsoDate(Shifted(modes, nowMs, modes.Resets.DailyHour));
            /// <summary>Monday date of the KST week that starts Monday 05:00.</summary>
            public static string WeekKey(ContentModesData modes, long nowMs)
            {
                long t = Shifted(modes, nowMs, modes.Resets.WeeklyHour);
                // new Date(t).getUTCDay() of an invalid date is NaN and the key then fails as toISOString does
                if (t > DateLimit || t < -DateLimit) throw InvalidDate();
                long weekday = Mod(FloorDiv(t, Day) + 4, 7);
                return IsoDate(t - (weekday - modes.Resets.WeeklyWeekday + 7) % 7 * Day);
            }
            /// <summary>§13.3: cycles start on season day 1·15·29 at 11:00 KST. seasonStartDate is the KST calendar date of day 1 (ops config).</summary>
            public static RiftCycleInfo RiftCycle(ContentModesData modes, string? seasonStartDate, long nowMs)
            {
                long? parsed = seasonStartDate == null ? null : ParseDate(seasonStartDate);
                if (parsed == null) throw new RuleException("INVALID_SEASON");
                RequireTime(nowMs);
                var R = modes.Rift; long offset = modes.Resets.UtcOffsetMinutes * 60000L;
                long start0 = parsed.Value - offset + R.CycleStartHour * Hour, span = R.CycleDays * Day;
                // x / 0 is ±Infinity or NaN in the source, never a cycle index
                if (span == 0) throw new RuleException("RIFT_CLOSED");
                long index = FloorDiv(nowMs - start0, span);
                if (!(index >= 0 && index < R.CyclesPerSeason)) throw new RuleException("RIFT_CLOSED");
                long startsAt = start0 + index * span;
                return new RiftCycleInfo { Index = (int)index, Key = IsoDate(startsAt + offset), StartsAt = startsAt, EndsAt = startsAt + span };
            }
            // Number.isFinite(nowMs); a long beyond 2^53 has no exact JS number, so it is refused here as well.
            static void RequireTime(long nowMs) { if (nowMs > CanonicalState.MaxSafeInteger || nowMs < -CanonicalState.MaxSafeInteger) throw new RuleException("INVALID_TIME"); }
            static long Shifted(ContentModesData modes, long nowMs, int hour)
            {
                RequireTime(nowMs);
                return nowMs + modes.Resets.UtcOffsetMinutes * 60000L - hour * Hour;
            }
            static Exception InvalidDate() => new MemoryBoundaryException("RangeError", "Invalid time value");
            static long FloorDiv(long a, long b) { long q = a / b; return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q; }
            static long Mod(long a, long b) => (a % b + b) % b;
            // new Date(ms).toISOString().slice(0, 10), including ±YYYYYY years and the ±8.64e15 ms limit
            static string IsoDate(long ms)
            {
                if (ms > DateLimit || ms < -DateLimit) throw InvalidDate();
                long z = FloorDiv(ms, Day) + 719468, era = FloorDiv(z, 146097), doe = z - era * 146097;
                long yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365, doy = doe - (365 * yoe + yoe / 4 - yoe / 100), mp = (5 * doy + 2) / 153;
                long day = doy - (153 * mp + 2) / 5 + 1, month = mp < 10 ? mp + 3 : mp - 9, year = yoe + era * 400 + (month <= 2 ? 1 : 0);
                string mm = month.ToString("D2", CultureInfo.InvariantCulture);
                if (year < 0 || year > 9999) return (year < 0 ? "-" : "+") + Math.Abs(year).ToString("D6", CultureInfo.InvariantCulture) + "-" + mm;
                return year.ToString("D4", CultureInfo.InvariantCulture) + "-" + mm + "-" + day.ToString("D2", CultureInfo.InvariantCulture);
            }
            // /^\d{4}-\d{2}-\d{2}$/ and V8 Date.parse: month 01–12, day 01–31 (a day past the month's end rolls over), UTC midnight
            static long? ParseDate(string text)
            {
                if (text.Length != 10 || text[4] != '-' || text[7] != '-') return null;
                for (int i = 0; i < 10; i++) if (i != 4 && i != 7 && (text[i] < '0' || text[i] > '9')) return null;
                long year = long.Parse(text.Substring(0, 4), CultureInfo.InvariantCulture), month = long.Parse(text.Substring(5, 2), CultureInfo.InvariantCulture), day = long.Parse(text.Substring(8, 2), CultureInfo.InvariantCulture);
                if (month < 1 || month > 12 || day < 1 || day > 31) return null;
                long y = year - (month <= 2 ? 1 : 0), era = FloorDiv(y, 400), yoe = y - era * 400;
                long doy = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5, doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
                return (era * 146097 + doe - 719468 + day - 1) * Day;
            }
        }

        // ───────────────────────── shared setup ─────────────────────────
        internal static ContentAccount NormAccount(ContentAccount? account)
        {
            if (account == null || account.Level < 1 || account.Stamina < 0 || account.Owned == null || account.Owned.Any(c => c == null)) throw new RuleException("INVALID_ACCOUNT");
            return account.Copy();
        }
        static T Guard<T>(Func<T> body)
        {
            try { return body(); }
            catch (OverflowException) { throw new RuleException("CONTENT_NUMBER_OUT_OF_RANGE"); }
        }

        /// <summary>partySetup: derived stats (as Run.create) and the battle deck: each member's saved memory deck (base kit when absent)
        /// plus their weapon card. Deck, reservations and gauge start fresh in every battle.</summary>
        public static ContentPartySetup PartySetup(RunData data, IReadOnlyList<RunPartyEntry?>? party, object? memory, IReadOnlyList<string> owned) => Guard(() =>
        {
            data.Index();
            if (party == null || party.Count < 1 || party.Count > 3) throw new RuleException("PARTY_SIZE");
            var ids = party.Select(p => p == null ? null : MemoryJson.Get(p.Fields, "charId")).ToList();
            for (int i = 0; i < ids.Count; i++) for (int j = i + 1; j < ids.Count; j++) if (MemoryJson.Equal(ids[i], ids[j])) throw new RuleException("PARTY_DUPLICATE");
            var result = new ContentPartySetup();
            foreach (var source in party)
            {
                if (source == null) throw new MemoryBoundaryException("TypeError", "Cannot read properties of null (reading 'charId')");
                var p = source.Copy(); result.Party.Add(p);
                string? cid = MemoryJson.Get(p.Fields, "charId") as string;
                if (cid == null || !data.Memory.Battle.CharacterIndex.ContainsKey(cid)) throw new RuleException("UNKNOWN_CHARACTER");
                if (!owned.Contains(cid)) throw new RuleException("NOT_OWNED");
                Run.Derive(data, p);
                object? saved = MemoryJson.Get(memory, cid);
                IList specs = MemoryJson.Truthy(saved) ? saved as IList ?? throw new RuleException("INVALID_SNAPSHOT")
                    : Run.BaseKit(data, cid).Select(c => (object?)c.ToJsonGraph()).ToList();
                foreach (object? sp in specs)
                {
                    string? id = MemoryJson.Get(sp, "id") as string;
                    if (id == null || !data.Memory.Battle.CardIndex.TryGetValue(id, out var card) || card.Owner != cid && card.Owner != "common"
                        || MemoryJson.Truthy(MemoryJson.Get(sp, "token")) || MemoryJson.Truthy(MemoryJson.Get(sp, "weapon"))) throw new RuleException("INVALID_CARD");
                    object? Or(string key, object? fallback) { var value = MemoryJson.Get(sp, key); return MemoryJson.Truthy(value) ? MemoryJson.Copy(value) : fallback; }
                    result.Deck.Add(RunCardSpec.FromJsonGraph(MemoryJson.Object("id", id, "plus", MemoryJson.Truthy(MemoryJson.Get(sp, "plus")), "mods", Or("mods", null),
                        "growth", Or("growth", 0), "assigned", card.Owner == "common" ? cid : null, "from", Or("from", null))));
                }
                var weapons = data.Memory.Battle.WeaponIndex;
                if (!(MemoryJson.Get(p.Fields, "weaponId") is string wid && weapons.TryGetValue(wid, out var weapon)) && !weapons.TryGetValue(Run.DefaultWeapon(data, cid), out weapon))
                    throw new MemoryBoundaryException("TypeError", "Cannot read properties of undefined (reading 'card')");
                result.Deck.Add(RunCardSpec.FromJsonGraph(MemoryJson.Object("id", weapon.Card, "wielder", cid)));
                result.MaxHp = checked(result.MaxHp + p.Hp!.Value);
            }
            return result;
        });

        /// <summary>stageDiff: §11.1 stage level scaling combined with a row's extra hpBps/dmgBps (weekly boss 5, rift floor 6).</summary>
        public static (int Hp, int Dmg) StageDiff(RunData data, ContentStageRow row)
        {
            var sc = Stats.StageScale(data.Stats, row.Stage);
            if (row.HpBps == 0 && row.DmgBps == 0) return sc;
            // Stats.combine in binary64 as the source evaluates it: Math.round((bps/100) * scale / 100). The exact rational differs
            // (stage 41, dmgBps 12920: 129.2 × 125 / 100 = 161.49999999999997 → 161, not 162).
            int Combine(int bps, int scale)
            {
                double x = (bps != 0 ? bps : Bps) / 100.0 * (scale != 0 ? scale : 100) / 100.0, r = Math.Floor(x);
                if (x - r >= 0.5) r += 1; // Math.round: nearest integer, ties toward +∞
                if (!(r >= int.MinValue && r <= int.MaxValue)) throw new RuleException("CONTENT_NUMBER_OUT_OF_RANGE");
                return (int)r;
            }
            return (Combine(row.HpBps, sc.Hp), Combine(row.DmgBps, sc.Dmg));
        }

        /// <summary>modeData: rift_break is a data rule (every enemy break gauge ×gaugeBps, boss phases included; broken bonus replaced).
        /// Other modes return the same data.</summary>
        public static BattleData ModeData(BattleData data, BattleMode? mode) => Battle.ModeData(data, mode);

        static Battle StartModeBattle(ContentModesData modes, ContentAccount account, List<RunPartyEntry?>? party, object? memory, uint? seed,
            ContentStageRow row, List<List<string>> enemies, BattleMode mode)
        {
            var t = PartySetup(modes.Run, party, memory, account.Owned);
            var diff = StageDiff(modes.Run, row);
            mode.Waves = enemies.Skip(1).Select(w => new List<string>(w)).ToList();
            var setup = new BattleSetup
            {
                Party = t.Party.Select(Run.ActorSetup).ToList(), Deck = t.Deck.Select(Run.CardSpec).ToList(), Hp = t.MaxHp, MaxHp = t.MaxHp,
                Seed = seed ?? 1, DifficultyHp = diff.Hp, DifficultyDamage = diff.Dmg, Enemies = new List<string>(enemies[0])
            };
            Battle battle;
            try { battle = Battle.CreateMode(modes.Run.Memory.Battle, setup, mode); }
            catch (OverflowException) { throw new RuleException("BATTLE_NUMBER_OUT_OF_RANGE"); }
            battle.Do("begin");
            return battle;
        }
        static BattleState Settleable(BattleState? bs, string kind)
        {
            if (bs?.Mode == null || bs.Mode.Kind != kind) throw new RuleException("WRONG_CONTENT");
            if (bs.Mode.Settled) throw new RuleException("ALREADY_SETTLED");
            if (bs.Phase != "VICTORY" && bs.Phase != "DEFEAT") throw new RuleException("BATTLE_NOT_OVER");
            var output = bs.Copy(); output.Mode!.Settled = true;
            return output;
        }
        static long FloorDiv(long a, long b) { long q = a / b; return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q; }

        // ───────────────────────── §11.5 growth dungeon ─────────────────────────
        public static class Dungeon
        {
            static (ContentDungeonDef D, ContentDungeonTier T) Rows(ContentModesData modes, string? dungeonId, int? tier)
            {
                var d = modes.Dungeon.Dungeons.FirstOrDefault(x => x.Id == dungeonId) ?? throw new RuleException("UNKNOWN_CONTENT");
                var t = modes.Dungeon.Tiers.FirstOrDefault(x => x.Tier == tier) ?? throw new RuleException("UNKNOWN_TIER");
                return (d, t);
            }
            /// <summary>One run at a tier: tier-6 yield × tier pct, integer floor.</summary>
            public static Dictionary<string, int> Reward(ContentModesData modes, string? dungeonId, int? tier) => Guard(() =>
            {
                var (d, t) = Rows(modes, dungeonId, tier);
                return d.Yield.ToDictionary(p => p.Key, p => checked((int)FloorDiv((long)p.Value * t.Pct, 100)), StringComparer.Ordinal);
            });
            /// <summary>Star 1 victory, star 2 within maxTurns, star 3 party HP ≥ minHpPct (each on top of victory).</summary>
            public static int Stars(ContentModesData modes, BattleResult? result, int maxHp)
            {
                var st = modes.Dungeon;
                if (result == null || result.Phase != "VICTORY") return 0;
                return 1 + (result.Turn <= st.MaxTurns ? 1 : 0) + ((long)result.Hp * 100 >= (long)maxHp * st.MinHpPct ? 1 : 0);
            }
            public static Battle Start(ContentModesData modes, ContentAccount account, DungeonStartOptions opts) => Guard(() =>
            {
                var a = NormAccount(account); var (d, t) = Rows(modes, opts.DungeonId, opts.Tier);
                if (a.Level < d.Unlock) throw new RuleException("LOCKED");
                if (a.Stamina < d.Cost) throw new RuleException("NOT_ENOUGH_STAMINA");
                var W = modes.Dungeon; List<List<string>>? waves = null;
                if (W.Encounters != null && W.Encounters.TryGetValue(d.Id, out var byTier)) byTier.TryGetValue(t.Tier.ToString(CultureInfo.InvariantCulture), out waves);
                if (waves == null) throw new RuleException("CONTENT_NOT_AUTHORED");
                if (waves.Count < W.MinWaves || waves.Count > W.MaxWaves || waves.Any(w => w == null || w.Count == 0)) throw new RuleException("INVALID_CONTENT");
                return StartModeBattle(modes, a, opts.Party, opts.Memory, opts.Seed, t, waves, new BattleMode { Kind = "dungeon", ContentId = d.Id, Tier = t.Tier });
            });
            /// <summary>Victory: spend 집중, pay the tier reward, record best stars. Defeat stays unsettled until defeatStamina is authored.</summary>
            public static ContentSettlement Settle(ContentModesData modes, ContentAccount account, BattleState? battle) => Guard(() =>
            {
                var output = Settleable(battle, "dungeon"); var a = NormAccount(account); var bs = battle!;
                if (bs.Phase == "DEFEAT") throw new RuleException("CONTENT_NOT_AUTHORED");
                var (d, t) = Rows(modes, bs.Mode!.ContentId, bs.Mode.Tier);
                if (a.Stamina < d.Cost) throw new RuleException("NOT_ENOUGH_STAMINA");
                int stars = Stars(modes, bs.Result, bs.MaxHp);
                string key = d.Id + ":" + t.Tier.ToString(CultureInfo.InvariantCulture);
                a.Stamina -= d.Cost; a.Dungeons[key] = Math.Max(a.Dungeons.TryGetValue(key, out int best) ? best : 0, stars);
                var grant = Reward(modes, d.Id, t.Tier); grant["accountXp"] = checked(d.Cost * modes.Xp.StaminaSpent);
                return new ContentSettlement { Account = a, Battle = output, Stars = stars, Grant = grant };
            });
            /// <summary>Instant clears after a 3-star clear of that tier: same reward and 집중 per run, at most sweepMax per call.</summary>
            public static ContentSettlement Sweep(ContentModesData modes, ContentAccount account, string? dungeonId, int? tier, int times) => Guard(() =>
            {
                var a = NormAccount(account); var (d, t) = Rows(modes, dungeonId, tier); var W = modes.Dungeon;
                if (times < 1 || times > W.SweepMax) throw new RuleException("INVALID_SWEEP");
                if (a.Level < d.Unlock) throw new RuleException("LOCKED");
                if ((a.Dungeons.TryGetValue(d.Id + ":" + t.Tier.ToString(CultureInfo.InvariantCulture), out int best) ? best : 0) < W.SweepUnlock) throw new RuleException("SWEEP_LOCKED");
                if (a.Stamina < (long)d.Cost * times) throw new RuleException("NOT_ENOUGH_STAMINA");
                a.Stamina = checked(a.Stamina - d.Cost * times);
                var grant = Reward(modes, d.Id, t.Tier).ToDictionary(p => p.Key, p => checked(p.Value * times), StringComparer.Ordinal);
                grant["accountXp"] = checked(d.Cost * times * modes.Xp.StaminaSpent);
                return new ContentSettlement { Account = a, Grant = grant };
            });
        }

        // ───────────────────────── §11.6 weekly boss ─────────────────────────
        public static class WeeklyBoss
        {
            /// <summary>practice: true never pays; otherwise a victory pays while the shared weekly counter is below claimsPerWeek.</summary>
            public static Battle Start(ContentModesData modes, ContentAccount account, WeeklyBossStartOptions opts) => Guard(() =>
            {
                var a = NormAccount(account); var W = modes.WeeklyBoss;
                if (a.Level < W.Unlock) throw new RuleException("LOCKED");
                if (opts.BossId == null || !W.Bosses.Contains(opts.BossId)) throw new RuleException("UNKNOWN_CONTENT");
                var row = W.Difficulty.FirstOrDefault(x => x.Lv == opts.Difficulty) ?? throw new RuleException("UNKNOWN_TIER");
                return StartModeBattle(modes, a, opts.Party, opts.Memory, opts.Seed, row, new List<List<string>> { new List<string> { opts.BossId } },
                    new BattleMode { Kind = "weekly_boss", ContentId = opts.BossId, Difficulty = row.Lv, Practice = opts.Practice });
            });
            /// <summary>§11.6: the victory record and the weekly counter are checked together. Every ended battle settles once, so a victory
            /// beyond the limit cannot be held back for next week; a defeat never touches the counter.</summary>
            public static ContentSettlement Settle(ContentModesData modes, ContentAccount account, BattleState? battle, long nowMs) => Guard(() =>
            {
                var output = Settleable(battle, "weekly_boss"); var a = NormAccount(account); var W = modes.WeeklyBoss; var bs = battle!;
                string week = Time.WeekKey(modes, nowMs);
                if (a.Weekly.Key != week) a.Weekly = new ContentWeeklyLedger { Key = week, Claimed = 0 };
                string? reason = null;
                if (bs.Phase == "DEFEAT") reason = "DEFEAT";
                else if (bs.Mode!.Practice == true) reason = "PRACTICE";
                else if (a.Weekly.Claimed >= W.ClaimsPerWeek) reason = "WEEKLY_LIMIT";
                output.Mode!.Rewarded = reason == null;
                if (reason != null) return new ContentSettlement { Account = a, Battle = output, Reason = reason };
                a.Weekly.Claimed = checked(a.Weekly.Claimed + 1);
                var grant = new Dictionary<string, int>(W.PerClaim, StringComparer.Ordinal); grant["accountXp"] = modes.Xp.WeeklyBossClaim;
                return new ContentSettlement { Account = a, Battle = output, Grant = grant };
            });
        }

        // ───────────────────────── §11.7 record rift reward ─────────────────────────
        public static class Rift
        {
            /// <summary>§11.7: per floor once per cycle, paid by star portions (60/20/20 for stars 1/2/3). A later better clear in the same
            /// cycle pays only the portions not yet paid, so a floor never exceeds its reward per cycle.</summary>
            public static ContentSettlement Settle(ContentModesData modes, ContentAccount account, RiftRunState? state) => Guard(() =>
            {
                if (state == null || state.Schema != RiftRunState.SchemaId) throw new RuleException("WRONG_CONTENT");
                if (state.Settled) throw new RuleException("ALREADY_SETTLED");
                if (state.Phase != "CLEARED" && state.Phase != "FAILED") throw new RuleException("RIFT_NOT_OVER");
                var a = NormAccount(account); var M = modes.Rift;
                if (a.Rift.Key != state.CycleKey)
                {
                    if (!string.IsNullOrEmpty(a.Rift.Key) && string.CompareOrdinal(a.Rift.Key, state.CycleKey) > 0) throw new RuleException("STALE_CYCLE");
                    a.Rift = new ContentRiftLedger { Key = state.CycleKey };
                }
                int stars = RiftRun.Score(modes, state).Stars;
                var row = M.Floors.FirstOrDefault(f => f.Floor == state.Floor);
                int paid = a.Rift.Floors.TryGetValue(state.Floor, out int already) ? already : 0;
                long crystals = 0;
                for (int i = paid; i < stars; i++) crystals += FloorDiv((long)(row ?? throw new MemoryBoundaryException("TypeError", "Cannot read properties of undefined (reading 'reward')")).Reward * M.StarPortionsPct[i], 100);
                a.Rift.Floors[state.Floor] = Math.Max(paid, stars);
                var output = state.Copy(); output.Settled = true;
                return new ContentSettlement { Account = a, Rift = output, Stars = stars,
                    Grant = crystals != 0 ? new Dictionary<string, int>(StringComparer.Ordinal) { ["crystals"] = checked((int)crystals) } : null };
            });
        }

        // ───────────────────────── §11.13 daily activity (100 points) ─────────────────────────
        // v0.8.1 policy: the five §11.11 tasks. target policy: a selectable pool (DECISION_PENDING) where any mix reaches 100.
        public static class Daily
        {
            public static List<ContentDailyTask> Tasks(ContentModesData modes, string? policy)
            {
                var D = modes.Daily; string p = string.IsNullOrEmpty(policy) ? D.ActivePolicy : policy!;
                if (p == "v0.8.1") return D.MissionTasks.Select(t => D.Goals.TryGetValue(t.Id, out var g)
                    ? new ContentDailyTask { Id = t.Id, Pts = t.Pts, Event = g.Event, N = g.N } : new ContentDailyTask { Id = t.Id, Pts = t.Pts }).ToList();
                if (p == "target") return (D.TargetPool ?? throw new RuleException("CONTENT_NOT_AUTHORED")).Select(t => t.Copy()).ToList();
                throw new RuleException("UNKNOWN_POLICY");
            }
            public static ContentAccount Open(ContentModesData modes, ContentAccount account, long nowMs)
            {
                var a = NormAccount(account);
                if (a.Level < modes.Daily.Unlock) throw new RuleException("LOCKED");
                string key = Time.DayKey(modes, nowMs);
                if (a.Daily.Key != key) a.Daily = new ContentDailyLedger { Key = key };
                return a;
            }
            public static ContentAccount Record(ContentModesData modes, ContentAccount account, string? eventType, int count, long nowMs, string? policy = null) => Guard(() =>
            {
                var a = Open(modes, account, nowMs);
                var events = new HashSet<string?>(Tasks(modes, policy).Select(t => t.Event), StringComparer.Ordinal);
                if (eventType == null || !events.Contains(eventType) || count < 1) throw new RuleException("INVALID_EVENT");
                a.Daily.Counts[eventType] = checked((a.Daily.Counts.TryGetValue(eventType, out int n) ? n : 0) + count);
                return a;
            });
            public static int Points(ContentModesData modes, ContentAccount account, long nowMs, string? policy = null) => Guard(() =>
            {
                var a = Open(modes, account, nowMs);
                long sum = Tasks(modes, policy).Sum(t => t.N.HasValue && (t.Event != null && a.Daily.Counts.TryGetValue(t.Event, out int n) ? n : 0) >= t.N.Value ? (long)t.Pts : 0);
                return (int)Math.Min(modes.Daily.PointsCap, sum);
            });
            /// <summary>Milestone rewards from progression.missions.daily; reaching the cap is the day's completion (§11.9 account XP).</summary>
            public static ContentSettlement Claim(ContentModesData modes, ContentAccount account, int at, long nowMs, string? policy = null) => Guard(() =>
            {
                var a = Open(modes, account, nowMs);
                var milestone = modes.Daily.Milestones.FirstOrDefault(m => m.At == at) ?? throw new RuleException("UNKNOWN_MILESTONE");
                if (a.Daily.Claimed.Contains(at)) throw new RuleException("ALREADY_CLAIMED");
                if (Points(modes, a, nowMs, policy) < at) throw new RuleException("NOT_ENOUGH_POINTS");
                a.Daily.Claimed.Add(at);
                var grant = new Dictionary<string, int>(milestone.Grant, StringComparer.Ordinal);
                if (at == modes.Daily.PointsCap) grant["accountXp"] = modes.Xp.DailyMissionComplete;
                return new ContentSettlement { Account = a, Grant = grant };
            });
        }
    }

    // ───────────────────────── §11.7 record rift floor ─────────────────────────
    public sealed class RiftTeam
    {
        public List<RunPartyEntry> Party = new List<RunPartyEntry>();
        public List<RunCardSpec> Deck = new List<RunCardSpec>();
        public int Hp, MaxHp;
        internal RiftTeam Copy() => new RiftTeam { Party = Party.Select(p => p.Copy()).ToList(), Deck = Deck.Select(c => c.Copy()).ToList(), Hp = Hp, MaxHp = MaxHp };
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("party", Party.Select(p => (object?)p.ToJsonGraph()).ToList(),
            "deck", Deck.Select(c => (object?)c.ToJsonGraph()).ToList(), "hp", Hp, "maxHp", MaxHp);
    }
    public sealed class RiftBattleResult
    {
        public int Battle, Team, Turn, Hp;
        public string Phase = "";
        internal RiftBattleResult Copy() => (RiftBattleResult)MemberwiseClone();
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("battle", Battle, "team", Team, "phase", Phase, "turn", Turn, "hp", Hp);
    }
    public sealed class RiftRunState
    {
        public const string SchemaId = "mf-rift-floor-0.8.1";
        public string Schema = SchemaId, CycleKey = "", Rule = "", Phase = "READY";
        public int Floor, CycleIndex, DifficultyHp, DifficultyDamage, Idx, StarCap;
        public Dictionary<string, int> Params = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<List<string>> Encounters = new List<List<string>>();
        public List<RiftTeam> Teams = new List<RiftTeam>();
        public List<int> Order = new List<int>();
        public List<RiftBattleResult> Results = new List<RiftBattleResult>();
        public uint[] Rng = new uint[4];
        public BattleState? Battle;
        public bool Settled;
        internal RiftRunState Copy()
        {
            var copy = (RiftRunState)MemberwiseClone();
            copy.Params = new Dictionary<string, int>(Params, StringComparer.Ordinal); copy.Encounters = Encounters.Select(e => new List<string>(e)).ToList();
            copy.Teams = Teams.Select(t => t.Copy()).ToList(); copy.Order = new List<int>(Order);
            copy.Results = Results.Select(r => r.Copy()).ToList(); copy.Rng = (uint[])Rng.Clone(); copy.Battle = Battle?.Copy();
            return copy;
        }
        public Dictionary<string, object?> ToJsonGraph() => BattleGraph.Object("schema", Schema, "floor", Floor, "cycleIndex", CycleIndex, "cycleKey", CycleKey,
            "rule", Rule, "params", Params.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal), "difficulty", BattleGraph.Object("hp", DifficultyHp, "dmg", DifficultyDamage),
            "encounters", Encounters.Select(e => new List<string>(e)).ToList(), "teams", Teams.Select(t => (object?)t.ToJsonGraph()).ToList(), "order", new List<int>(Order),
            "idx", Idx, "results", Results.Select(r => (object?)r.ToJsonGraph()).ToList(), "starCap", StarCap, "phase", Phase, "rng", (uint[])Rng.Clone(),
            "battle", Battle?.ToJsonGraph(), "settled", Settled);
    }

    /// <summary>Three battles per floor. Each team's HP carries across its battles on the floor; every battle builds a fresh deck,
    /// reservations and gauge. Two-team floors fight team order [0, 1, 0].</summary>
    public sealed class RiftRun
    {
        readonly ContentModesData data;
        readonly RiftRunState state;
        Battle? battle;
        RiftRun(ContentModesData data, RiftRunState state) { this.data = data; this.state = state; }
        /// <summary>Detached snapshot, the live battle state included.</summary>
        public RiftRunState State => state.Copy();

        public static RiftRun Create(ContentModesData data, ContentAccount account, RiftRunOptions opts)
        {
            try { return CreateCore(data, account, opts); }
            catch (OverflowException) { throw new RuleException("CONTENT_NUMBER_OUT_OF_RANGE"); }
        }
        static RiftRun CreateCore(ContentModesData data, ContentAccount account, RiftRunOptions opts)
        {
            var a = ContentModes.NormAccount(account); var M = data.Rift;
            if (a.Level < M.Unlock) throw new RuleException("LOCKED");
            var row = M.Floors.FirstOrDefault(f => f.Floor == opts.Floor) ?? throw new RuleException("UNKNOWN_TIER");
            var cycle = opts.Cycle;
            if (cycle == null || cycle.Index < 0 || cycle.Index >= M.CyclesPerSeason || cycle.Key == null) throw new RuleException("INVALID_CYCLE");
            var cyc = M.Rotation != null && cycle.Index < M.Rotation.Count ? M.Rotation[cycle.Index] : null;
            if (cyc == null) throw new RuleException("CONTENT_NOT_AUTHORED");
            int at = row.Floor - 1;
            string? rule = cyc.Rules != null && at >= 0 && at < cyc.Rules.Count ? cyc.Rules[at] : null;
            var encounters = cyc.Encounters != null && at >= 0 && at < cyc.Encounters.Count ? cyc.Encounters[at] : null;
            if (rule == null || !M.Modifiers.ContainsKey(rule) || !Battle.RiftRules.Contains(rule) || encounters == null || encounters.Count != 3 || encounters.Any(e => e == null || e.Count == 0))
                throw new RuleException("INVALID_CONTENT");
            if (!M.TeamsByFloor.TryGetValue(string.IsNullOrEmpty(opts.Policy) ? M.ActiveTeamPolicy : opts.Policy!, out var teamsByFloor)) throw new RuleException("UNKNOWN_POLICY");
            int? need = at >= 0 && at < teamsByFloor.Count ? teamsByFloor[at] : (int?)null;
            if (opts.Teams == null || opts.Teams.Count != need) throw new RuleException("TEAM_COUNT");
            var teams = opts.Teams.Select(p => { var t = ContentModes.PartySetup(data.Run, p, opts.Memory, a.Owned); return new RiftTeam { Party = t.Party, Deck = t.Deck, Hp = t.MaxHp, MaxHp = t.MaxHp }; }).ToList();
            int starCap = 3;
            if (need == 2)
            {
                // §11.7 "서로 다른 6명": two full parties
                if (teams.Any(t => t.Party.Count * 2 != M.DistinctCharacters)) throw new RuleException("TEAM_SIZE");
                var first = new HashSet<string>(teams[0].Party.Select(p => p.CharId), StringComparer.Ordinal);
                if (teams[1].Party.Any(p => first.Contains(p.CharId)))
                {
                    // §11.7: re-entry from team 1 only when fewer than six characters are owned; the floor then caps at 2 stars
                    if (a.Owned.Count >= M.DistinctCharacters) throw new RuleException("PARTY_OVERLAP");
                    starCap = M.SmallRosterMaxStars;
                }
            }
            var diff = ContentModes.StageDiff(data.Run, row);
            var s = new RiftRunState
            {
                Floor = row.Floor, CycleIndex = cycle.Index, CycleKey = cycle.Key, Rule = rule, Params = new Dictionary<string, int>(M.Modifiers[rule], StringComparer.Ordinal),
                DifficultyHp = diff.Hp, DifficultyDamage = diff.Dmg, Encounters = encounters.Select(e => new List<string>(e)).ToList(), Teams = teams,
                Order = new List<int>(need == 2 ? M.TwoTeamOrder : M.OneTeamOrder), StarCap = starCap, Rng = MemoryForge.Core.Rng.SeedState(opts.Seed ?? 1)
            };
            return new RiftRun(data, s);
        }

        /// <summary>Start the next battle. As the source, its seed is drawn from the floor RNG before the battle is built.</summary>
        public void Next()
        {
            var s = state;
            if (s.Phase != "READY") throw new RuleException("WRONG_PHASE");
            var team = s.Teams[s.Order[s.Idx]];
            var rng = new Rng(s.Rng); uint seed = rng.Next(); s.Rng = rng.State;
            var setup = new BattleSetup
            {
                Party = team.Party.Select(Run.ActorSetup).ToList(), Deck = team.Deck.Select(Run.CardSpec).ToList(), Hp = team.Hp, MaxHp = team.MaxHp, Seed = seed,
                DifficultyHp = s.DifficultyHp, DifficultyDamage = s.DifficultyDamage, Enemies = new List<string>(s.Encounters[s.Idx])
            };
            Battle created;
            try
            {
                created = Battle.CreateMode(data.Run.Memory.Battle, setup, new BattleMode { Kind = "rift", ContentId = "floor:" + s.Floor.ToString(CultureInfo.InvariantCulture),
                    Battle = checked(s.Idx + 1), Rule = s.Rule, Params = s.Params });
            }
            catch (OverflowException) { throw new RuleException("BATTLE_NUMBER_OUT_OF_RANGE"); }
            created.Do("begin");
            battle = created; s.Battle = created.Live; s.Phase = "BATTLE";
            if (created.Finished) AfterBattle();
        }
        /// <summary>Battle#do on the live battle (rid r{v}-{kind}); the floor advances when it ends.</summary>
        public BattleResponse BattleCommand(string kind, BattleArgument? args = null)
        {
            var live = battle ?? throw new RuleException("NO_BATTLE");
            var response = live.Do(kind, args);
            state.Battle = live.Live;
            if (live.Finished) AfterBattle();
            return response;
        }
        void AfterBattle()
        {
            var s = state; var bs = battle!.Live; int team = s.Order[s.Idx];
            s.Teams[team].Hp = bs.Hp;
            s.Results.Add(new RiftBattleResult { Battle = s.Idx + 1, Team = team, Phase = bs.Phase, Turn = bs.Turn, Hp = bs.Hp });
            battle = null; s.Battle = null;
            if (bs.Phase == "DEFEAT") s.Phase = "FAILED";
            else if (s.Idx == s.Order.Count - 1) s.Phase = "CLEARED";
            else { s.Idx += 1; s.Phase = "READY"; }
        }

        /// <summary>Star 1 all three battles won, star 2 total turns ≤ maxTotalTurns, star 3 remaining team HP ≥ minHpPct. A cleared floor
        /// cannot be scored while either threshold is DECISION_PENDING; a failed floor is 0 stars.</summary>
        public RiftStars Stars() => Score(data, state);
        internal static RiftStars Score(ContentModesData data, RiftRunState s)
        {
            var st = data.Rift;
            int turns = checked(s.Results.Sum(r => r.Turn)), hp = checked(s.Teams.Sum(t => t.Hp)), maxHp = checked(s.Teams.Sum(t => t.MaxHp));
            if (s.Phase != "CLEARED") return new RiftStars { Stars = 0, Turns = turns, Hp = hp, MaxHp = maxHp };
            if (st.MaxTotalTurns == null || st.MinHpPct == null) throw new RuleException("CONTENT_NOT_AUTHORED");
            int n = 1 + (turns <= st.MaxTotalTurns.Value ? 1 : 0) + ((long)hp * 100 >= (long)maxHp * st.MinHpPct.Value ? 1 : 0);
            return new RiftStars { Stars = Math.Min(n, s.StarCap), Turns = turns, Hp = hp, MaxHp = maxHp };
        }
    }
}
