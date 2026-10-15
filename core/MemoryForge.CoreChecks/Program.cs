// Cross-language contract checks for the v0.7 C# core.
// Usage: dotnet run --project core/MemoryForge.CoreChecks -- <repo-root>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MemoryForge.Core;

static class Program
{
    static int failures, checks;
    static void Check(bool ok, string what) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL " + what); } }

    static int Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "game_data.json"))).RootElement;
        var vec = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "engine", "tests", "vectors", "rng_gacha_vectors.json"))).RootElement;
        var cfg = LoadGacha(data.GetProperty("gacha"));
        var stats = LoadStats(data.GetProperty("progression"));
        var weaponRarity = data.GetProperty("weapons").EnumerateArray().ToDictionary(w => w.GetProperty("id").GetString()!, w => w.GetProperty("rarity").GetString()!);

        // 1) RNG
        foreach (var v in vec.GetProperty("rng").EnumerateArray())
        {
            uint seed = v.GetProperty("seed").GetUInt32();
            var st = Rng.SeedState(seed);
            var want = v.GetProperty("state").EnumerateArray().Select(x => x.GetUInt32()).ToArray();
            Check(st.SequenceEqual(want), $"seed state {seed}");
            var r = new Rng(seed);
            var next = v.GetProperty("next").EnumerateArray().Select(x => x.GetUInt32()).ToArray();
            Check(next.All(x => r.Next() == x), $"next sequence {seed}");
        }
        foreach (var v in vec.GetProperty("below").EnumerateArray())
        {
            var r = new Rng(v.GetProperty("seed").GetUInt32()); long n = v.GetProperty("n").GetInt64();
            Check(v.GetProperty("values").EnumerateArray().All(x => r.Below(n) == x.GetInt32()), $"below {n}");
        }
        // 2) gacha — identical result streams and end-of-stream pity
        foreach (var v in vec.GetProperty("gacha").EnumerateArray())
        {
            string banner = v.GetProperty("banner").GetString()!;
            var g = new GachaState(v.GetProperty("seed").GetUInt32()) { Crystals = 1_000_000_000 };
            var got = new List<string>();
            for (int i = 0; i < 30; i++) got.AddRange(Gacha.Pull(cfg, g, banner, 10).Select(x => x.ToString()));
            var want = v.GetProperty("results").EnumerateArray().Select(x => x.GetString()!).ToList();
            int firstDiff = Enumerable.Range(0, Math.Min(got.Count, want.Count)).FirstOrDefault(i => got[i] != want[i]);
            Check(got.SequenceEqual(want), $"gacha {banner} (first diff at {firstDiff}: {(firstDiff < got.Count ? got[firstDiff] : "-")} vs {(firstDiff < want.Count ? want[firstDiff] : "-")})");
            foreach (var kv in v.GetProperty("endPity").EnumerateObject())
            {
                var p = g.Pity[kv.Name]; var j = kv.Value;
                Check(p.N == j.GetProperty("n").GetInt32() && p.SrN == j.GetProperty("srN").GetInt32(), $"pity {banner}/{kv.Name} n/srN");
                if (j.TryGetProperty("guar", out var gu)) Check(p.Guar == gu.GetBoolean(), $"pity {banner}/{kv.Name} guar");
                if (j.TryGetProperty("total", out var to)) Check(p.Total == to.GetInt32(), $"pity {banner}/{kv.Name} total");
            }
        }
        foreach (var kv in vec.GetProperty("curveMeans").EnumerateObject())
            Check(Math.Abs(Gacha.MeanPullsPerSsr(cfg.Curves[kv.Name]) - kv.Value.GetDouble()) < 1e-9, $"curve mean {kv.Name}");
        // 3) stats
        foreach (var v in vec.GetProperty("stats").EnumerateArray())
        {
            var e = v.GetProperty("entry"); var d = v.GetProperty("derived");
            var entry = new GrowthEntry
            {
                CharId = e.GetProperty("charId").GetString()!,
                Level = e.TryGetProperty("level", out var lv) ? lv.GetInt32() : (int?)null,
                WeaponRarity = e.TryGetProperty("weaponId", out var wid) ? weaponRarity[wid.GetString()!] : null,
                WeaponLevel = e.TryGetProperty("weaponLevel", out var wl) ? wl.GetInt32() : 1,
                Armor = e.TryGetProperty("armor", out var ar) ? ar.GetInt32() : (int?)null,
                Accessory = e.TryGetProperty("accessory", out var ac) ? ac.GetInt32() : (int?)null,
                SkillLv = e.TryGetProperty("skillLv", out var sk) ? sk.GetInt32() : 1,
                UltLv = e.TryGetProperty("ultLv", out var ul) ? ul.GetInt32() : 1,
            };
            var got = Stats.Derive(stats, entry);
            Check(got.Power4 == d.GetProperty("power4").GetInt32() && got.Hp == d.GetProperty("hp").GetInt32() && got.GearBps == d.GetProperty("gearBps").GetInt32()
                && got.SkillLv == d.GetProperty("skillLv").GetInt32() && got.UltLv == d.GetProperty("ultLv").GetInt32(), $"stats {entry.CharId} L{entry.Level}");
        }
        foreach (var v in vec.GetProperty("stageScale").EnumerateArray())
        {
            int L = v.GetProperty("level").GetInt32(); var (hp, dmg) = Stats.StageScale(stats, L);
            Check(hp == v.GetProperty("hp").GetInt32() && dmg == v.GetProperty("dmg").GetInt32(), $"stage scale L{L}");
            if (L < 60) Check(Stats.XpToNext(stats, L) == v.GetProperty("xpToNext").GetInt32(), $"xp L{L}");
        }
        // 4) reservation book (behavioural)
        var book = new ReservationBook();
        book.Add("a", "e1", 1, "next"); book.Add("b", "e1", 1, "next");
        Check(Throws(() => book.Add("c", "e1", 1, "next"), "SLOT_FULL"), "next cap 2");
        book.AddTemp(5, 5);
        Check(book.Capacity("next", 1) == 3 && book.Capacity("after", 1) == 2, "temp slots capped at 3/2");
        book.Add("c", "e1", 1, "next");
        book.Add("d", "e1", 1, "after"); book.Add("e", "e1", 1, "after");
        Check(Throws(() => book.Add("f", "e1", 1, "after"), "SLOT_FULL"), "after cap 2");
        Check(Throws(() => book.Add("a", "e1", 1, "after"), "DUPLICATE_RESERVATION"), "duplicate");
        Check(Throws(() => book.Seal(1), "NO_EMPTY_SLOT"), "seal needs an empty next slot");
        var due2 = book.StartTurn(2);
        Check(due2.Select(r => r.CardUid).SequenceEqual(new[] { "a", "b", "c" }), "turn 2 resolves a,b,c in order");
        Check(book.Capacity("next", 2) == 2, "temp cleared");
        // absolute due turns: last turn's after-next reservations (due 3) now occupy this turn's next column
        Check(book.Used("next", 2) == 2 && book.Free("next", 2) == 0, "after-next entries became next-turn entries");
        Check(Throws(() => book.Seal(2), "NO_EMPTY_SLOT"), "seal needs an empty next slot");
        var (pr, now) = book.Pull(2, null);
        Check(pr != null && pr.CardUid == "d" && now, "pulling a next-turn reservation resolves it now");
        book.Seal(2);
        Check(book.Capacity("next", 2) == 1 && book.Free("next", 2) == 0, "sealed slot removed from this turn's next column");
        Check(book.Capacity("next", 3) == 1 && book.Capacity("next", 4) == 2, "seal lasts through the following turn");
        var b2 = new ReservationBook();
        b2.Add("x", "e1", 1, "after");
        var (px, nowx) = b2.Pull(1, null);
        Check(px != null && px.CardUid == "x" && !nowx && px.Due == 2 && px.Kind == "next", "pull moves an after-next reservation to next turn");
        Check(Throws(() => new ReservationBook().Pull(1, null), "RESERVATION_REQUIRED"), "pull needs a reservation");
        var big = new ReservationBook(3, 2); big.AddBase(5, 5);
        Check(big.Capacity("next", 1) == 3 && big.Capacity("after", 1) == 2, "base capped");
        Check(Throws(() => new ReservationBook(4, 1), "CAPACITY_RANGE"), "ctor range");

        Console.WriteLine($"CoreChecks: {checks - failures}/{checks} passed");
        int traceFailures = TraceHashChecks.Run(root);
        int openingFailures = BattleOpeningChecks.Run(root);
        int turnFailures = BattleTurnChecks.Run(root);
        int queryFailures = BattlePlayerQueryChecks.Run(root);
        int playerFailures = BattlePlayerActionChecks.Run(root);
        int memoryFailures = MemoryV2Checks.Run(root);
        int runCreateFailures = RunCreateChecks.Run(root);
        int progressionFailures = RunProgressionChecks.Run(root);
        int numberFailures = RunJsonNumberChecks.Run(root);
        int textFailures = CardTextChecks.Run(root);
        return failures == 0 && traceFailures == 0 && openingFailures == 0 && turnFailures == 0 && queryFailures == 0 && playerFailures == 0 && memoryFailures == 0 && runCreateFailures == 0
            && progressionFailures == 0 && numberFailures == 0 && textFailures == 0 ? 0 : 1;
    }

    static bool Throws(Action a, string code) { try { a(); return false; } catch (RuleException e) { return e.Code == code; } }

    static GachaConfig LoadGacha(JsonElement g)
    {
        var cfg = new GachaConfig { CostPerPull = g.GetProperty("costPerPull").GetInt32() };
        foreach (var c in g.GetProperty("curves").EnumerateObject())
        {
            int Get(string n) => c.Value.TryGetProperty(n, out var x) ? x.GetInt32() : 0;
            cfg.Curves[c.Name] = new GachaCurve { SsrBase = Get("ssrBase"), SoftStart = Get("softStart"), SoftStep = Get("softStep"), Hard = Get("hard"), SrBase = Get("srBase"),
                SrGuarantee = Get("srGuarantee"), FeaturedSsrBps = Get("featuredSsrBps"), FeaturedSrBps = Get("featuredSrBps"), SelectorAt = Get("selectorAt"), MaxPulls = Get("maxPulls"), TenPullTickets = Get("tenPullTickets") };
        }
        List<string> L(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToList();
        var pools = g.GetProperty("pools");
        cfg.StandardSsrChars = L(pools.GetProperty("standardSsrChars")); cfg.SrChars = L(pools.GetProperty("srChars"));
        cfg.StandardSsrWeapons = L(pools.GetProperty("standardSsrWeapons")); cfg.SrWeapons = L(pools.GetProperty("srWeapons"));
        foreach (var b in g.GetProperty("banners").EnumerateArray())
            cfg.Banners.Add(new GachaBanner { Id = b.GetProperty("id").GetString()!, Kind = b.GetProperty("kind").GetString()!, Ticket = b.GetProperty("ticket").GetString()!,
                FeaturedSsr = b.TryGetProperty("featuredSsr", out var f) ? f.GetString() : null, FeaturedSr = b.TryGetProperty("featuredSr", out var fs) ? L(fs) : new List<string>() });
        cfg.RItems = L(g.GetProperty("rItems"));
        var d = g.GetProperty("duplicates");
        cfg.ShardSsr = d.GetProperty("shardSsr").GetInt32(); cfg.ShardSsrOverflow = d.GetProperty("shardSsrOverflow").GetInt32();
        cfg.ShardSr = d.GetProperty("shardSr").GetInt32(); cfg.ShardSrOverflow = d.GetProperty("shardSrOverflow").GetInt32(); cfg.AfterglowR = d.GetProperty("afterglowR").GetInt32();
        return cfg;
    }

    // Shared with tools/UnityContentExport through the linked StatsDataLoader.
    internal static StatsConfig LoadStats(JsonElement p) => StatsDataLoader.Load(p);
}
