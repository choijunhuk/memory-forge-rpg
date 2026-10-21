// RECRUIT-SERVER-20261021 §12.1–§12.3: the injected-source overloads of Core Gacha against the seeded ones, before the server uses them.
// The seeded overloads stay bit-identical to engine/tests/vectors (Program checks the vectors directly). Here: (1) fed the same bounded
// draws by a recorder over an equally seeded Rng, the injected overloads give the seeded results, per-request states and RNG states and the
// JS vector strings; (2) the recorded draws alone replay the same outcome on a fresh unseeded state; (3) every bound is 10000 or a
// configured pool size; (4) validation and payment precede any draw and a refused state is untouched; (5) a missing source, a draw outside
// [0, n) and a seeded call on an unseeded state are refused; (6) scripted extreme sources pin hard pity, the SR guarantee window, the
// beginner cap/guarantee and the §12.3 duplicate table; (7) tickets come before crystals, and Core's SpentCrystals counts ticket
// equivalents (so it is not a crystal delta); Core's beginner crystal fallback is kept unchanged and named; (8) the selector; (9) the
// server's CSPRNG source keeps the pity invariants. Expected values derive from the content, the vectors and the §12.3 table only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MemoryForge.Core;

static class GachaRandomChecks
{
    static readonly uint[] Seeds = { 1, 7, 99, 20261021, 4294967295 };
    // Every banner and both counts interleaved on one state (shared standard total, beginner cap, limited/weapon carry-over).
    static readonly (string Banner, int Count)[] Mixed =
    {
        ("beginner", 10), ("standard", 1), ("limited_lumi", 10), ("weapon_kaya", 1), ("standard", 10), ("limited_kaya", 1), ("weapon_lumi", 10),
        ("beginner", 10), ("limited_lumi", 1), ("standard", 1), ("weapon_lumi", 1), ("beginner", 10), ("limited_kaya", 10), ("weapon_kaya", 10),
    };
    static int checks, failures;
    static void Check(bool ok, string label) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL GachaRandom " + label); } }

    public static int Run(string root)
    {
        checks = failures = 0;
        try
        {
            using var data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
            using var vectors = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "engine", "tests", "vectors", "rng_gacha_vectors.json")));
            var cfg = GachaDataLoader.Load(data.RootElement.GetProperty("gacha"));
            VectorParity(cfg, vectors.RootElement);
            foreach (uint seed in Seeds)
                Parity(cfg, seed, Enumerable.Repeat(Mixed, 3).SelectMany(ops => ops).ToList(), Funded, "mixed banners and counts, seed " + seed);
            SingleParity(cfg);
            Refusals(cfg);
            PityEdges(cfg);
            Payment(cfg);
            Selector(cfg);
            Csprng(cfg);
        }
        catch (Exception error) { Check(false, "execution: " + error.GetType().Name + ": " + error.Message); }
        Console.WriteLine($"GachaRandomChecks: {checks - failures}/{checks} passed (seeded vs injected source, draw replay, refusals before any draw, pity edges, payment, selector, CSPRNG invariants)");
        return failures;
    }

    static void Funded(GachaState g)
    {
        g.Crystals = 1_000_000_000; g.Tickets["limitedTicket"] = 15; g.Tickets["weaponTicket"] = 15; g.Tickets["standardTicket"] = 25;
    }

    // The vectors' own streams through the injected overload: the JS reference strings, and per ten-pull the seeded results and state.
    static void VectorParity(GachaConfig cfg, JsonElement vectors)
    {
        foreach (var v in vectors.GetProperty("gacha").EnumerateArray())
        {
            string banner = v.GetProperty("banner").GetString()!;
            var outcome = Parity(cfg, v.GetProperty("seed").GetUInt32(), Enumerable.Repeat((Banner: banner, Count: 10), 30).ToList(), g => g.Crystals = 1_000_000_000, "vector " + banner);
            Check(outcome.Pulls.Select(p => p.ToString()).SequenceEqual(v.GetProperty("results").EnumerateArray().Select(x => x.GetString()!)),
                "vector " + banner + ": the injected stream is the JS reference stream");
        }
    }

    sealed class Outcome
    {
        public List<PullResult> Pulls { get; } = new List<PullResult>();
        public List<string> Trace { get; } = new List<string>();
    }

    sealed class Recorder
    {
        readonly Rng rng;
        public List<(int Bound, int Value)> Draws { get; } = new List<(int Bound, int Value)>();
        public Recorder(uint seed) { rng = new Rng(seed); }
        public uint[] State => rng.State;
        public int Below(int n) { int value = rng.Below(n); Draws.Add((n, value)); return value; }
    }

    // One script three ways: the seeded overload; the injected overload over a recorder of an equally seeded Rng; the recorded draws alone
    // on a fresh unseeded state. Every request's results and the full state after it are compared.
    static Outcome Parity(GachaConfig cfg, uint seed, IReadOnlyList<(string Banner, int Count)> ops, Action<GachaState> fund, string what)
    {
        var seeded = new GachaState(seed); fund(seeded);
        var a = Session(cfg, seeded, ops, (g, banner, count) => Gacha.Pull(cfg, g, banner, count));
        var recorder = new Recorder(seed);
        var injected = new GachaState(); fund(injected);
        var b = Session(cfg, injected, ops, (g, banner, count) => Gacha.Pull(cfg, g, banner, count, recorder.Below));
        Check(a.Trace.Count > 0 && a.Trace.SequenceEqual(b.Trace), what + ": the injected overload gives the seeded results and state after every request");
        Check(seeded.Rng!.State.SequenceEqual(recorder.State), what + ": both sides consumed exactly the same draws");
        var bounds = new HashSet<int> { Gacha.Bps, cfg.StandardSsrChars.Count, cfg.StandardSsrWeapons.Count, cfg.SrChars.Count + cfg.SrWeapons.Count, cfg.SrWeapons.Count, cfg.RItems.Count };
        foreach (var banner in cfg.Banners) bounds.Add(banner.FeaturedSr.Count);
        Check(recorder.Draws.All(d => bounds.Contains(d.Bound)) && recorder.Draws.Count(d => d.Bound == Gacha.Bps) >= b.Pulls.Count,
            what + ": every draw is a 10000 roll or a configured pool pick, at least one roll per pull");
        int at = 0; bool aligned = true;
        var replayed = new GachaState(); fund(replayed);
        var c = Session(cfg, replayed, ops, (g, banner, count) => Gacha.Pull(cfg, g, banner, count, n =>
        {
            if (at >= recorder.Draws.Count || recorder.Draws[at].Bound != n) { aligned = false; return 0; }
            return recorder.Draws[at++].Value;
        }));
        Check(aligned && at == recorder.Draws.Count && c.Trace.SequenceEqual(b.Trace), what + ": the recorded draws alone replay the same outcome on a fresh unseeded state");
        return b;
    }

    static Outcome Session(GachaConfig cfg, GachaState g, IReadOnlyList<(string Banner, int Count)> ops, Func<GachaState, string, int, List<PullResult>> pull)
    {
        var outcome = new Outcome();
        foreach (var (banner, count) in ops)
        {
            // Past the beginner cap every side skips identically (equal states), so a long mixed script never throws.
            if (banner == "beginner" && g.Pity["beginner"].Total + count > cfg.Curves["beginner"].MaxPulls) continue;
            var pulls = pull(g, banner, count);
            outcome.Pulls.AddRange(pulls);
            outcome.Trace.AddRange(pulls.Select(Result));
            outcome.Trace.Add(banner + " x" + count + " -> " + State(g));
        }
        return outcome;
    }

    // The single-pull overloads (no payment) round-robin over every banner.
    static void SingleParity(GachaConfig cfg)
    {
        foreach (uint seed in Seeds)
        {
            var seeded = new GachaState(seed); var injected = new GachaState(); var recorder = new Recorder(seed);
            bool same = true;
            for (int i = 0; i < 160; i++)
            {
                var banner = cfg.Banners[i % cfg.Banners.Count];
                same &= Result(Gacha.One(cfg, seeded, banner)) == Result(Gacha.One(cfg, injected, banner, recorder.Below)) && State(seeded) == State(injected);
            }
            Check(same && seeded.Rng!.State.SequenceEqual(recorder.State), "One, seed " + seed + ": the injected single pull equals the seeded one on every banner");
        }
    }

    // Validation and payment come before any draw: a refused request draws nothing and leaves the state exactly as it was.
    static void Refusals(GachaConfig cfg)
    {
        void Refused(string what, Action<GachaState> setup, string banner, int count, string code)
        {
            var g = new GachaState(); setup(g);
            string before = State(g);
            int draws = 0;
            string? got = Code(() => Gacha.Pull(cfg, g, banner, count, n => { draws++; return 0; }));
            Check(got == code && draws == 0 && State(g) == before, what + ": " + code + " before any draw, state untouched");
        }
        int price = cfg.CostPerPull, tenTickets = cfg.Curves["beginner"].TenPullTickets, cap = cfg.Curves["beginner"].MaxPulls;
        Refused("an unknown banner", g => g.Crystals = 10_000, "no_such_banner", 1, "UNKNOWN_BANNER");
        foreach (int count in new[] { 0, 2, 9, 11, -1, int.MaxValue }) Refused("count " + count, g => g.Crystals = 10_000_000, "standard", count, "BAD_COUNT");
        Refused("a single beginner pull", g => g.Crystals = 10_000, "beginner", 1, "BEGINNER_TEN_ONLY");
        Refused("a beginner ten-pull past the cap", g => { g.Crystals = 10_000; g.Pity["beginner"].Total = cap - 5; }, "beginner", 10, "BEGINNER_LIMIT");
        Refused("a finished beginner banner", g => { g.Crystals = 10_000; g.Pity["beginner"].Done = true; }, "beginner", 10, "BEGINNER_LIMIT");
        Refused("a single pull without funds", g => { }, "standard", 1, "CRYSTALS_LOW");
        Refused("a single pull one crystal short", g => g.Crystals = price - 1, "standard", 1, "CRYSTALS_LOW");
        Refused("nine tickets and one crystal short of a ten-pull", g => { g.Tickets["standardTicket"] = 9; g.Crystals = price - 1; }, "standard", 10, "CRYSTALS_LOW");
        Refused("only another banner's tickets", g => g.Tickets["limitedTicket"] = 10, "standard", 1, "CRYSTALS_LOW");
        Refused("a beginner ten-pull one crystal short of the crystal fallback", g => g.Crystals = (long)tenTickets * price - 1, "beginner", 10, "CRYSTALS_LOW");

        var unseeded = new GachaState { Crystals = 10_000 };
        string start = State(unseeded);
        Check(Code(() => Gacha.Pull(cfg, unseeded, "standard", 1)) == "RNG_UNSEEDED" && Code(() => Gacha.One(cfg, unseeded, cfg.Banners[0])) == "RNG_UNSEEDED" && State(unseeded) == start,
            "the seeded overloads refuse an unseeded state before any payment or draw");
        bool missing = false;
        try { Gacha.Pull(cfg, new GachaState { Crystals = 10_000 }, "standard", 1, null!); }
        catch (ArgumentNullException) { missing = true; }
        Check(missing, "a missing source is refused");
        Check(Code(() => Gacha.Pull(cfg, new GachaState { Crystals = 10_000 }, "standard", 1, n => n)) == "RNG_RANGE"
            && Code(() => Gacha.Pull(cfg, new GachaState { Crystals = 10_000 }, "standard", 1, n => -1)) == "RNG_RANGE"
            && Code(() => Gacha.One(cfg, new GachaState(), cfg.Banners[0], n => n + 1)) == "RNG_RANGE", "a draw outside [0, n) is refused (RNG_RANGE)");
    }
    static string? Code(Action action)
    {
        try { action(); return null; }
        catch (RuleException error) { return error.Code; }
    }

    // Scripted extremes. Largest draws: no SSR before the curve reaches 10000 (hard pity on this content), SR or better in every
    // SR-guarantee window, every pick the last pool entry. Smallest draws: an SSR every pull, every pick the first entry. Duplicates by §12.3.
    static void PityEdges(GachaConfig cfg)
    {
        Func<int, int> highest = n => n - 1, lowest = n => 0;
        var standard = cfg.Curves["standard"];
        int ssrAt = Enumerable.Range(1, standard.Hard).First(k => Gacha.SsrRate(standard, k - 1) == Gacha.Bps);
        var high = new GachaState { Crystals = (long)ssrAt * cfg.CostPerPull };
        var got = new List<PullResult>();
        for (int k = 0; k < ssrAt; k++) got.AddRange(Gacha.Pull(cfg, high, "standard", 1, highest));
        Check(got.Count(r => r.Rarity == "SSR") == 1 && got[^1].Rarity == "SSR" && got[^1].PityAt == ssrAt && got[^1].Id == cfg.StandardSsrChars[^1],
            "largest draws: the only SSR is pull " + ssrAt + " (the curve's 100% point), the last standard SSR");
        Check(Guaranteed(got, standard.SrGuarantee), "largest draws: SR or better at least once in every " + standard.SrGuarantee + " pulls");
        Check(got.Where(r => r.Rarity == "SR").All(r => r.Kind == "weapon" && r.Id == cfg.SrWeapons[^1]) && got.Where(r => r.Rarity == "R").All(r => r.Kind == "item" && r.Id == cfg.RItems[^1]),
            "largest draws pick the last entry of the SR (characters then weapons) and R pools");
        Check(Results(got) == Results(Expected(cfg, got)), "largest draws: the pity counter and §12.3 duplicates (refine, shards, afterglow) of every result");
        Check(high.Pity["standard"].N == 0 && high.Pity["standard"].SrN == 0 && high.Pity["standard"].Total == ssrAt && high.Crystals == 0
            && high.Afterglow == got.Count(r => r.Rarity == "R") * (long)cfg.AfterglowR && high.Shards == got.Sum(r => (long)r.Shards),
            "hard pity resets n and srN; the total, afterglow and shards are applied to the state");

        var low = new GachaState { Crystals = 10L * cfg.CostPerPull };
        var ten = Gacha.Pull(cfg, low, "standard", 10, lowest);
        Check(ten.All(r => r.Rarity == "SSR" && r.PityAt == 1 && r.Id == cfg.StandardSsrChars[0]) && Results(ten) == Results(Expected(cfg, ten))
            && low.OwnedChars[cfg.StandardSsrChars[0]] == 10 && low.Shards == ten.Sum(r => (long)r.Shards),
            "smallest draws: an SSR every pull, the first standard SSR, resonance and the shard overflow by §12.3");

        var beginner = cfg.Curves["beginner"];
        int cap = beginner.MaxPulls;
        int guaranteed = Enumerable.Range(1, cap).First(k => k >= cap || Gacha.SsrRate(beginner, k - 1) == Gacha.Bps);
        var b = new GachaState();
        b.Tickets["standardTicket"] = beginner.TenPullTickets * (cap / 10);
        var all = new List<PullResult>();
        for (int i = 0; i < cap / 10; i++) all.AddRange(Gacha.Pull(cfg, b, "beginner", 10, highest));
        Check(all.Count == cap && all.Count(r => r.Rarity == "SSR") == 1 && all[guaranteed - 1].Rarity == "SSR" && all[guaranteed - 1].PityAt == guaranteed
            && Guaranteed(all, beginner.SrGuarantee) && Results(all) == Results(Expected(cfg, all)), "beginner: one SSR guaranteed by pull " + guaranteed + " of " + cap);
        Check(b.Pity["beginner"].Done && b.Pity["beginner"].Total == cap && b.Pity["beginner"].GotSsr && b.Pity["standard"].Total == cap && b.Tickets["standardTicket"] == 0 && b.Crystals == 0,
            "beginner: done at the cap, its pulls count toward the standard selector total, paid with tickets only");
        string done = State(b);
        int draws = 0;
        Check(Code(() => Gacha.Pull(cfg, b, "beginner", 10, n => { draws++; return 0; })) == "BEGINNER_LIMIT" && draws == 0 && State(b) == done, "a finished beginner banner refuses before any draw");
    }

    // No run of `every` consecutive pulls below SR (§12.1: after nine pulls without SR or better, the tenth is SR or better).
    static bool Guaranteed(List<PullResult> pulls, int every)
    {
        int run = 0;
        foreach (var r in pulls)
        {
            run = r.Rarity == "R" ? run + 1 : 0;
            if (run >= every) return false;
        }
        return true;
    }

    // §12.3 from the content constants, applied to an observed (rarity, kind, id) sequence of one pity kind from a fresh state: the pity
    // counter, first copy, resonance (characters, max 6), refine (weapons, max R5), duplicate shards and R afterglow.
    static List<PullResult> Expected(GachaConfig cfg, IEnumerable<PullResult> observed)
    {
        var chars = new Dictionary<string, int>(StringComparer.Ordinal);
        var weapons = new Dictionary<string, int>(StringComparer.Ordinal);
        int since = 0;
        var list = new List<PullResult>();
        foreach (var o in observed)
        {
            var r = new PullResult { Rarity = o.Rarity, Kind = o.Kind, Id = o.Id, PityAt = ++since };
            bool ssr = o.Rarity == "SSR";
            if (ssr) since = 0;
            if (o.Kind == "char")
            {
                chars.TryGetValue(o.Id, out int c); chars[o.Id] = c + 1;
                r.IsNew = c == 0; r.Resonance = Math.Min(6, c);
                r.Shards = c == 0 ? 0 : c >= 7 ? (ssr ? cfg.ShardSsrOverflow : cfg.ShardSrOverflow) : (ssr ? cfg.ShardSsr : cfg.ShardSr);
            }
            else if (o.Kind == "weapon")
            {
                weapons.TryGetValue(o.Id, out int c); weapons[o.Id] = c + 1;
                r.IsNew = c == 0; r.Refine = Math.Min(5, c + 1);
                r.Shards = c >= 5 ? (ssr ? cfg.ShardSsrOverflow : cfg.ShardSrOverflow) : (ssr ? cfg.ShardSsr : cfg.ShardSr);
            }
            else r.Afterglow = cfg.AfterglowR;
            list.Add(r);
        }
        return list;
    }

    // Tickets first, then crystals. Core's SpentCrystals adds the ticket equivalents, so only the Crystals delta is a crystal debit. The
    // beginner ten-pull costs TenPullTickets tickets, and Core currently also takes crystals for missing tickets (a fallback the §12.1 table
    // does not list): kept unchanged and disclosed.
    static void Payment(GachaConfig cfg)
    {
        int price = cfg.CostPerPull, tenTickets = cfg.Curves["beginner"].TenPullTickets;
        var mixed = new GachaState { Crystals = 7L * price };
        mixed.Tickets["standardTicket"] = 3;
        Gacha.Pull(cfg, mixed, "standard", 10, n => 0);
        Check(mixed.Tickets["standardTicket"] == 0 && mixed.Crystals == 0 && mixed.SpentCrystals == 10L * price && mixed.SpentPulls == 10,
            "a ten-pull spends 3 tickets first, then 7 pulls of crystals; SpentCrystals also counts the ticket equivalents");
        var single = new GachaState { Crystals = price };
        single.Tickets["standardTicket"] = 1;
        Gacha.Pull(cfg, single, "standard", 1, n => 0);
        Check(single.Tickets["standardTicket"] == 0 && single.Crystals == price, "a single pull uses a ticket before any crystal");
        var fallback = new GachaState { Crystals = (long)tenTickets * price };
        Gacha.Pull(cfg, fallback, "beginner", 10, n => 0);
        Check(fallback.Crystals == 0 && fallback.Tickets["standardTicket"] == 0, "beginner crystal fallback (current Core behaviour, disclosed): no ticket, " + tenTickets + " pulls' worth of crystals");
        var part = new GachaState { Crystals = 3L * price };
        part.Tickets["standardTicket"] = tenTickets - 3;
        Gacha.Pull(cfg, part, "beginner", 10, n => 0);
        Check(part.Crystals == 0 && part.Tickets["standardTicket"] == 0, "a beginner ten-pull short of tickets pays the remainder in crystals");
        var tickets = new GachaState { Crystals = 5 };
        tickets.Tickets["standardTicket"] = tenTickets + 2;
        Gacha.Pull(cfg, tickets, "beginner", 10, n => 0);
        Check(tickets.Crystals == 5 && tickets.Tickets["standardTicket"] == 2, "a beginner ten-pull with enough tickets costs exactly " + tenTickets + " tickets");
    }

    // §12.1: after selectorAt standard + beginner pulls, one standard SSR character of the player's choice, once per account.
    static void Selector(GachaConfig cfg)
    {
        int at = cfg.Curves["standard"].SelectorAt, cap = cfg.Curves["beginner"].MaxPulls;
        var g = new GachaState { Crystals = 1_000_000_000 };
        var recorder = new Recorder(20261021);
        for (int i = 0; i < cap / 10; i++) Gacha.Pull(cfg, g, "beginner", 10, recorder.Below);
        while (g.Pity["standard"].Total < at - 1) Gacha.Pull(cfg, g, "standard", g.Pity["standard"].Total + 10 <= at - 1 ? 10 : 1, recorder.Below);
        string before = State(g);
        Check(Code(() => Gacha.ClaimSelector(cfg, g, cfg.StandardSsrChars[0])) == "SELECTOR_NOT_READY" && State(g) == before, "the selector is not ready at " + (at - 1) + " standard + beginner pulls");
        Gacha.Pull(cfg, g, "standard", 1, recorder.Below);
        Check(g.Pity["standard"].Total == at && g.Pity["beginner"].Total == cap, "beginner pulls count toward the selector total");
        before = State(g);
        var refused = cfg.SrChars.Concat(cfg.Banners.Where(b => b.FeaturedSsr != null).Select(b => b.FeaturedSsr!)).Append("nobody").ToList();
        Check(refused.All(id => Code(() => Gacha.ClaimSelector(cfg, g, id)) == "SELECTOR_BAD_CHOICE") && State(g) == before, "the selector refuses SR, featured and unknown choices and changes nothing");
        string choice = cfg.StandardSsrChars[^1];
        g.OwnedChars.TryGetValue(choice, out int copies);
        long shards = g.Shards;
        var granted = Gacha.ClaimSelector(cfg, g, choice);
        int want = copies >= 7 ? cfg.ShardSsrOverflow : copies >= 1 ? cfg.ShardSsr : 0;
        Check(granted.Rarity == "SSR" && granted.Kind == "char" && granted.Id == choice && granted.IsNew == (copies == 0) && granted.Resonance == Math.Min(6, copies)
            && granted.Shards == want && granted.PityAt == 0 && g.Shards == shards + want && g.OwnedChars[choice] == copies + 1 && g.Pity["standard"].SelectorUsed,
            "the selector grants the chosen standard SSR with the §12.3 duplicate shards");
        Check(Code(() => Gacha.ClaimSelector(cfg, g, choice)) == "SELECTOR_USED", "a second selector claim is refused");
    }

    // The server's source (RandomNumberGenerator.GetInt32) under the same state machine on an unseeded state: the bounded contract holds
    // (no RNG_RANGE) and the §12.1 pity invariants and §12.3 ledgers hold over many pulls.
    static void Csprng(GachaConfig cfg)
    {
        var standard = cfg.Curves["standard"];
        var g = new GachaState { Crystals = 1_000_000_000 };
        var pulls = new List<PullResult>();
        for (int i = 0; i < 200; i++) pulls.AddRange(Gacha.Pull(cfg, g, "standard", 10, RandomNumberGenerator.GetInt32));
        Check(pulls.All(r => r.PityAt >= 1 && r.PityAt <= standard.Hard) && pulls.Count(r => r.Rarity == "SSR") >= pulls.Count / standard.Hard && Guaranteed(pulls, standard.SrGuarantee),
            "CSPRNG source: every result within hard pity, at least one SSR per " + standard.Hard + " pulls, the SR guarantee holds");
        Check(Results(pulls) == Results(Expected(cfg, pulls)) && g.Shards == pulls.Sum(r => (long)r.Shards) && g.Afterglow == pulls.Count(r => r.Rarity == "R") * (long)cfg.AfterglowR
            && g.OwnedChars.Values.Sum() + g.OwnedWeapons.Values.Sum() == pulls.Count(r => r.Kind != "item") && g.Pity["standard"].Total == pulls.Count
            && g.Crystals == 1_000_000_000 - pulls.Count * (long)cfg.CostPerPull, "CSPRNG source: duplicates, ledgers, totals and the crystal debit agree with the results");
    }

    static string Result(PullResult r) => string.Join("|", r.Rarity, r.Kind, r.Id, r.Featured, r.Lost, r.PityAt, r.IsNew, r.Resonance, r.Refine, r.Shards, r.Afterglow);
    static string Results(IEnumerable<PullResult> list) => string.Join(" ", list.Select(Result));
    static string State(GachaState g)
    {
        static string Map<T>(IEnumerable<KeyValuePair<string, T>> map, Func<T, string> text) =>
            string.Join(",", map.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + text(p.Value)));
        return "crystals " + g.Crystals + " tickets " + Map(g.Tickets, n => n.ToString()) + " shards " + g.Shards + " afterglow " + g.Afterglow
            + " spent " + g.SpentCrystals + "/" + g.SpentPulls + " pity " + Map(g.Pity, p => string.Join("/", p.N, p.SrN, p.Guar, p.SrGuar, p.Total, p.Done, p.GotSsr, p.SelectorUsed))
            + " chars " + Map(g.OwnedChars, n => n.ToString()) + " weapons " + Map(g.OwnedWeapons, n => n.ToString());
    }
}
