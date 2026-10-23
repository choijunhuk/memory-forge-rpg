// Host-only gacha → GachaConfig adapter. CoreChecks and MemoryForge.Server link this one file (not copies). Body moved from
// Program.LoadGacha unchanged (missing curve members stay 0) plus each banner's optional display name, which the rules never read.
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MemoryForge.Core;

static class GachaDataLoader
{
    public static GachaConfig Load(JsonElement g)
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
            cfg.Banners.Add(new GachaBanner { Id = b.GetProperty("id").GetString()!, Name = b.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "", Kind = b.GetProperty("kind").GetString()!,
                Ticket = b.GetProperty("ticket").GetString()!, FeaturedSsr = b.TryGetProperty("featuredSsr", out var f) ? f.GetString() : null,
                FeaturedSr = b.TryGetProperty("featuredSr", out var fs) ? L(fs) : new List<string>() });
        cfg.RItems = L(g.GetProperty("rItems"));
        var d = g.GetProperty("duplicates");
        cfg.ShardSsr = d.GetProperty("shardSsr").GetInt32(); cfg.ShardSsrOverflow = d.GetProperty("shardSsrOverflow").GetInt32();
        cfg.ShardSr = d.GetProperty("shardSr").GetInt32(); cfg.ShardSrOverflow = d.GetProperty("shardSrOverflow").GetInt32(); cfg.AfterglowR = d.GetProperty("afterglowR").GetInt32();
        return cfg;
    }
}
