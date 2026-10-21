// Memory Forge v0.7 core — recruitment (gacha) rules. Server-authoritative in production.
// Identical results to engine/rules-core.js Gacha for the same seed/state (see CoreChecks). Every draw goes through one bounded source
// below(n) ∈ [0, n): the seeded overloads forward to the state's Rng (unchanged vectors); the authoritative server injects its CSPRNG
// into the Func<int, int> overloads on an unseeded state, so the same state machine never touches a seeded stream there.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoryForge.Core
{
    public sealed class GachaCurve
    {
        public int SsrBase { get; set; }          // bps
        public int SoftStart { get; set; }        // pull index (1-based) where soft pity starts
        public int SoftStep { get; set; }         // bps added per pull from SoftStart
        public int Hard { get; set; }             // pull index with 100% SSR
        public int SrBase { get; set; }           // bps
        public int SrGuarantee { get; set; }      // SR-or-better at least every N pulls
        public int FeaturedSsrBps { get; set; }   // 5000 = 50:50, 7500 = 75:25
        public int FeaturedSrBps { get; set; }
        public int SelectorAt { get; set; }       // standard only
        public int MaxPulls { get; set; }         // beginner only
        public int TenPullTickets { get; set; }   // beginner only
    }

    public sealed class GachaBanner
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";     // display name (content); never read by the rules
        public string Kind { get; set; } = "";     // character | weapon | standard | beginner
        public string? FeaturedSsr { get; set; }
        public List<string> FeaturedSr { get; set; } = new List<string>();
        public string Ticket { get; set; } = "";
    }

    public sealed class GachaConfig
    {
        public int CostPerPull { get; set; } = 160;
        public Dictionary<string, GachaCurve> Curves { get; set; } = new Dictionary<string, GachaCurve>();
        public List<string> StandardSsrChars { get; set; } = new List<string>();
        public List<string> SrChars { get; set; } = new List<string>();
        public List<string> StandardSsrWeapons { get; set; } = new List<string>();
        public List<string> SrWeapons { get; set; } = new List<string>();
        public List<GachaBanner> Banners { get; set; } = new List<GachaBanner>();
        public List<string> RItems { get; set; } = new List<string>();
        public int ShardSsr { get; set; } = 10;
        public int ShardSsrOverflow { get; set; } = 25;
        public int ShardSr { get; set; } = 2;
        public int ShardSrOverflow { get; set; } = 5;
        public int AfterglowR { get; set; } = 15;
    }

    public sealed class PityState
    {
        public int N { get; set; }            // pulls since the last SSR
        public int SrN { get; set; }          // pulls since the last SR-or-better
        public bool Guar { get; set; }        // next SSR is featured (lost the previous 50:50 / 75:25)
        public bool SrGuar { get; set; }
        public int Total { get; set; }        // standard: standard+beginner pulls (selector); beginner: pulls on the beginner banner
        public bool Done { get; set; }
        public bool GotSsr { get; set; }
        public bool SelectorUsed { get; set; }
    }

    public sealed class GachaState
    {
        /// <summary>The seeded stream of the seeded overloads; null on an unseeded state, which only an injected source can draw for.</summary>
        public Rng? Rng { get; }
        public long Crystals { get; set; }
        public Dictionary<string, int> Tickets { get; } = new Dictionary<string, int> { ["limitedTicket"] = 0, ["weaponTicket"] = 0, ["standardTicket"] = 0 };
        public long Shards { get; set; }
        public long Afterglow { get; set; }
        public Dictionary<string, PityState> Pity { get; } = new Dictionary<string, PityState>
        {
            ["character"] = new PityState(), ["weapon"] = new PityState(), ["standard"] = new PityState(), ["beginner"] = new PityState(),
        };
        public Dictionary<string, int> OwnedChars { get; } = new Dictionary<string, int>();
        public Dictionary<string, int> OwnedWeapons { get; } = new Dictionary<string, int>();
        public long SpentCrystals { get; set; }
        public long SpentPulls { get; set; }
        public GachaState(uint seed) { Rng = new Rng(seed); }
        /// <summary>No seeded stream: the seeded Pull/One overloads refuse it (RNG_UNSEEDED); the injected-source overloads draw.</summary>
        public GachaState() { }
    }

    public sealed class PullResult
    {
        public string Rarity { get; set; } = "R";
        public string Kind { get; set; } = "item";   // char | weapon | item
        public string Id { get; set; } = "";
        public bool Featured { get; set; }
        public bool Lost { get; set; }
        public int PityAt { get; set; }
        public bool IsNew { get; set; }
        public int Resonance { get; set; }
        public int Refine { get; set; }
        public int Shards { get; set; }
        public int Afterglow { get; set; }
        public override string ToString() => Rarity + ":" + Id;
    }

    public static class Gacha
    {
        public const int Bps = 10000;

        public static string KindOf(GachaBanner b) =>
            b.Kind == "weapon" ? "weapon" : b.Kind == "character" ? "character" : b.Kind == "beginner" ? "beginner" : "standard";

        /// <summary>SSR chance for the next pull when n pulls have passed since the last SSR.</summary>
        public static int SsrRate(GachaCurve c, int n)
        {
            int k = n + 1;
            if (k >= c.Hard) return Bps;
            if (k >= c.SoftStart) return Math.Min(Bps, c.SsrBase + c.SoftStep * (k - c.SoftStart + 1));
            return c.SsrBase;
        }

        /// <summary>Seeded: every draw comes from g.Rng, exactly as before the injected-source overload existed.</summary>
        public static List<PullResult> Pull(GachaConfig cfg, GachaState g, string bannerId, int count) => Pull(cfg, g, bannerId, count, Seeded(g));

        /// <summary>The same rules with an injected bounded source: below(n) must return a uniform integer in [0, n) for n ≥ 1 (a value
        /// outside it is RNG_RANGE). Validation and payment come before any draw. Any exception can leave g partly changed (paid, some
        /// pulls applied), so a caller that must not commit a partial pull discards g on failure.</summary>
        public static List<PullResult> Pull(GachaConfig cfg, GachaState g, string bannerId, int count, Func<int, int> below)
        {
            if (below == null) throw new ArgumentNullException(nameof(below));
            var banner = cfg.Banners.FirstOrDefault(b => b.Id == bannerId) ?? throw new RuleException("UNKNOWN_BANNER");
            if (count != 1 && count != 10) throw new RuleException("BAD_COUNT");
            if (banner.Kind == "beginner")
            {
                var bp = g.Pity["beginner"];
                if (bp.Done || bp.Total + count > cfg.Curves["beginner"].MaxPulls) throw new RuleException("BEGINNER_LIMIT");
                if (count != 10) throw new RuleException("BEGINNER_TEN_ONLY");
            }
            int ticketCost = banner.Kind == "beginner" ? cfg.Curves["beginner"].TenPullTickets : count;
            g.Tickets.TryGetValue(banner.Ticket, out int have);
            int fromTicket = Math.Min(have, ticketCost);
            long crystalCost = (long)(ticketCost - fromTicket) * cfg.CostPerPull;
            if (g.Crystals < crystalCost) throw new RuleException("CRYSTALS_LOW");
            g.Tickets[banner.Ticket] = have - fromTicket;
            g.Crystals -= crystalCost;
            g.SpentCrystals += crystalCost + (long)fromTicket * cfg.CostPerPull;
            g.SpentPulls += count;
            var outList = new List<PullResult>(count);
            for (int i = 0; i < count; i++) outList.Add(One(cfg, g, banner, below));
            if (banner.Kind == "beginner" && g.Pity["beginner"].Total >= cfg.Curves["beginner"].MaxPulls) g.Pity["beginner"].Done = true;
            return outList;
        }

        /// <summary>Seeded single pull (no payment): every draw comes from g.Rng.</summary>
        public static PullResult One(GachaConfig cfg, GachaState g, GachaBanner banner) => One(cfg, g, banner, Seeded(g));

        /// <summary>One pull with an injected bounded source (see Pull): the rarity roll below(10000), then the 50:50/75:25 roll of a
        /// character/weapon SSR or SR when no guarantee applies, then one pick from the result's pool.</summary>
        public static PullResult One(GachaConfig cfg, GachaState g, GachaBanner banner, Func<int, int> below)
        {
            if (below == null) throw new ArgumentNullException(nameof(below));
            string kind = KindOf(banner);
            var curve = cfg.Curves[kind];
            var p = g.Pity[kind];
            int ssrBp = SsrRate(curve, p.N);
            int roll = Draw(below, Bps);
            string rarity;
            if (roll < ssrBp) rarity = "SSR";
            else if (p.SrN + 1 >= curve.SrGuarantee) rarity = "SR";
            else if (roll < ssrBp + curve.SrBase) rarity = "SR";
            else rarity = "R";
            if (kind == "beginner" && rarity != "SSR" && !p.GotSsr && p.N + 1 >= curve.MaxPulls) rarity = "SSR";
            var r = new PullResult { Rarity = rarity, PityAt = p.N + 1 };
            bool charLike = kind == "character" || kind == "weapon";
            if (rarity == "SSR")
            {
                p.N = 0; p.SrN = 0; if (kind == "beginner") p.GotSsr = true;
                if (charLike)
                {
                    bool win = p.Guar || Draw(below, Bps) < curve.FeaturedSsrBps; // short-circuit: no roll while guaranteed
                    if (win) { r.Id = banner.FeaturedSsr ?? throw new RuleException("NO_FEATURED"); r.Featured = true; p.Guar = false; }
                    else { r.Id = Pick(below, kind == "weapon" ? cfg.StandardSsrWeapons : cfg.StandardSsrChars); p.Guar = true; r.Lost = true; }
                }
                else r.Id = Pick(below, cfg.StandardSsrChars);
                r.Kind = kind == "weapon" ? "weapon" : "char";
            }
            else if (rarity == "SR")
            {
                p.N += 1; p.SrN = 0;
                if (charLike && (p.SrGuar || Draw(below, Bps) < curve.FeaturedSrBps))
                {
                    r.Id = Pick(below, banner.FeaturedSr); r.Featured = true; p.SrGuar = false;
                    r.Kind = kind == "weapon" ? "weapon" : "char";
                }
                else
                {
                    if (charLike) p.SrGuar = true;
                    var pool = new List<KeyValuePair<string, string>>();
                    if (kind != "weapon") pool.AddRange(cfg.SrChars.Select(x => new KeyValuePair<string, string>("char", x)));
                    pool.AddRange(cfg.SrWeapons.Select(x => new KeyValuePair<string, string>("weapon", x)));
                    var pick = Pick(below, pool);
                    r.Kind = pick.Key; r.Id = pick.Value;
                }
            }
            else { p.N += 1; p.SrN += 1; r.Id = Pick(below, cfg.RItems); }
            if (kind == "standard" || kind == "beginner") g.Pity["standard"].Total += 1;
            if (kind == "beginner") p.Total += 1;
            ApplyOwnership(cfg, g, r);
            return r;
        }

        // The seeded source: exactly Rng.Below per draw, so the vectors are unchanged.
        private static Func<int, int> Seeded(GachaState g)
        {
            var rng = g.Rng ?? throw new RuleException("RNG_UNSEEDED");
            return n => rng.Below(n);
        }
        private static int Draw(Func<int, int> below, int n)
        {
            int value = below(n);
            if (value < 0 || value >= n) throw new RuleException("RNG_RANGE");
            return value;
        }
        // Rng.Pick's order: an empty pool is refused before any draw.
        private static T Pick<T>(Func<int, int> below, IReadOnlyList<T> list)
        {
            if (list == null || list.Count == 0) throw new RuleException("EMPTY_POOL");
            return list[Draw(below, list.Count)];
        }

        private static void ApplyOwnership(GachaConfig cfg, GachaState g, PullResult r)
        {
            bool ssr = r.Rarity == "SSR";
            if (r.Kind == "char")
            {
                g.OwnedChars.TryGetValue(r.Id, out int c); g.OwnedChars[r.Id] = c + 1;
                r.IsNew = c == 0; r.Resonance = Math.Min(6, c);
                r.Shards = c == 0 ? 0 : c >= 7 ? (ssr ? cfg.ShardSsrOverflow : cfg.ShardSrOverflow) : (ssr ? cfg.ShardSsr : cfg.ShardSr);
                g.Shards += r.Shards;
            }
            else if (r.Kind == "weapon")
            {
                g.OwnedWeapons.TryGetValue(r.Id, out int c); g.OwnedWeapons[r.Id] = c + 1;
                r.IsNew = c == 0; r.Refine = Math.Min(5, c + 1);
                r.Shards = c >= 5 ? (ssr ? cfg.ShardSsrOverflow : cfg.ShardSrOverflow) : (ssr ? cfg.ShardSsr : cfg.ShardSr);
                g.Shards += r.Shards;
            }
            else { g.Afterglow += cfg.AfterglowR; r.Afterglow = cfg.AfterglowR; }
        }

        public static PullResult ClaimSelector(GachaConfig cfg, GachaState g, string charId)
        {
            var st = g.Pity["standard"];
            if (st.SelectorUsed) throw new RuleException("SELECTOR_USED");
            if (st.Total < cfg.Curves["standard"].SelectorAt) throw new RuleException("SELECTOR_NOT_READY");
            if (!cfg.StandardSsrChars.Contains(charId)) throw new RuleException("SELECTOR_BAD_CHOICE");
            st.SelectorUsed = true;
            g.OwnedChars.TryGetValue(charId, out int c); g.OwnedChars[charId] = c + 1;
            int shards = c >= 7 ? cfg.ShardSsrOverflow : (c >= 1 ? cfg.ShardSsr : 0);
            g.Shards += shards;
            return new PullResult { Rarity = "SSR", Kind = "char", Id = charId, IsNew = c == 0, Resonance = Math.Min(6, c), Shards = shards };
        }

        public static int Resonance(GachaState g, string charId) { g.OwnedChars.TryGetValue(charId, out int c); return Math.Max(0, Math.Min(6, c - 1)); }
        public static int Refine(GachaState g, string weaponId) { g.OwnedWeapons.TryGetValue(weaponId, out int c); return Math.Max(1, Math.Min(5, c)); }

        /// <summary>Exact expected pulls per SSR for a curve (survival sum).</summary>
        public static double MeanPullsPerSsr(GachaCurve c)
        {
            double surv = 1, mean = 0;
            for (int k = 1; k <= c.Hard; k++) { double p = SsrRate(c, k - 1) / (double)Bps; mean += k * surv * p; surv *= 1 - p; }
            return mean;
        }
    }
}
