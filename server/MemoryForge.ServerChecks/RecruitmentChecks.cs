// RECRUIT-SERVER-20261021: GET /dev/practice/recruitment and POST .../pulls, .../claim-launch, .../selector of a real host process over
// real HTTP. One backend-neutral scenario (stored/replace delegates) runs in the file suite ("recruitment", a fresh store) and in the
// PostgreSQL mode ("pg-recruitment", a fresh run-tagged alias plus an existing alias with an active run): authentication, the strict
// bodies (expectedRevision included), rule and STALE_REVISION refusals that write nothing (no file/row for an unborn account), the
// once-per-account launch claim, single/ten/beginner pulls with tickets first and free before paid crystals (Core's disclosed beginner
// crystal fallback included), byte-identical retries, concurrent copies (one debit) and concurrent different requests (one commit, the
// rest STALE_REVISION) in one process and across two, a crash after a commit, per-account request IDs, an active run left untouched, a
// later run mutation making recruitment stale, and the fixed practice party. Every successful pull is replayed on the check side by Core
// over the private draw tape read from the stored envelope (an oracle read, nothing injected) and must reproduce the served results and
// snapshot. Tampered but re-digested recruitment members (balances, copies, pity, debit, draws, results, ledger, receipts) and tampered
// recruitment receipts (a changed result, historical wallet/pity/copies, injected private members, a malformed body, another account,
// request, kind or revision, receipts kept without their member or with an emptied one, an orphan receipt) are refused with STORE_CORRUPT
// for the read, an exact retry and a new claim, and preserved byte for byte; another Core build is still STORE_IMPLEMENTATION_MISMATCH;
// unedited re-encodings and a never-recruited (legacy) shape are accepted. The check-side Core configuration
// reads the content file's own gacha members (not the server's loader) after checking the published banners equal them. Paid crystals,
// the 300-pull selector and the beginner 50-pull cap have no legitimate source on a development account (the only grant is the approved
// launch reward) and the store refuses unbacked balances, so they are covered by CoreChecks, not here. Recruitment replies are captured
// separately and scanned with the unchanged response scan after removing exactly the two documented integer revisions.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MemoryForge.Core;

sealed partial class ServerChecks
{
    const string RecruitPath = "dev/practice/recruitment";
    static readonly string[] SnapshotMembers = { "accountId", "revision", "wallet", "ownedChars", "ownedWeapons", "pity", "launchClaimed", "banners", "history" };
    static readonly string[] WalletMembers = { "freeCrystals", "paidCrystals", "tickets", "shards", "afterglow" };
    static readonly string[] TicketMembers = { "limitedTicket", "weaponTicket", "standardTicket" };
    static readonly string[] PityKindMembers = { "character", "weapon", "standard", "beginner" };
    static readonly string[] PityMembers = { "n", "srN", "guar", "srGuar", "total", "done", "gotSsr", "selectorUsed" };
    static readonly string[] BannerMembers = { "id", "name", "kind", "ticket", "costPerPull", "tenPullTickets", "curve", "ssrPool", "srChars", "srWeapons", "rItems" };
    static readonly string[] CurveMembers = { "ssrBase", "softStart", "softStep", "hard", "srBase", "srGuarantee", "maxPulls", "selectorAt" };
    static readonly string[] HistoryMembers = { "requestId", "bannerId", "results" };
    static readonly string[] ResultMembers = { "rarity", "kind", "id", "featured", "lost", "pityAt", "isNew", "resonance", "refine", "shards", "afterglow" };
    static readonly string[] MutationMembers = { "requestId", "accountId", "revision", "kind", "results", "snapshot" };
    // Private recruitment members that must never appear in a recruitment reply (beside the unchanged scan's own list).
    static readonly HashSet<string> RecruitmentPrivate = new HashSet<string>(StringComparer.Ordinal)
    {
        "draws", "tape", "ledger", "debit", "grant", "expectedRevision", "roll", "rolls", "rollBps", "random", "spentCrystals", "spentPulls", "state", "receipt", "identity"
    };
    readonly ResponseCapture recruitCapture = new ResponseCapture();
    GachaConfig? oracleConfig;
    readonly List<string> inactiveBanners = new List<string>();
    long launchCrystals;
    int launchTickets;

    /// <summary>One recruiting account: its ID, credential, committed envelope bytes (null: no file/row) and a raw replace of those bytes.</summary>
    sealed record RecruitAccount(string Id, string Token, Func<Task<byte[]?>> Stored, Func<byte[], Task> Replace);

    Api RecruitClient(ServerProcess server) => new Api(server.Port, recruitCapture);
    static Task<Result> RecruitRead(Api api, string? token) => api.Get(RecruitPath, token);
    static Task<Result> RecruitPull(Api api, string? token, string body) => api.Post(RecruitPath + "/pulls", token, body);
    static Task<Result> RecruitLaunch(Api api, string? token, string body) => api.Post(RecruitPath + "/claim-launch", token, body);
    static Task<Result> RecruitSelector(Api api, string? token, string body) => api.Post(RecruitPath + "/selector", token, body);
    static string PullBody(string requestId, long expected, string bannerId, int count) =>
        new JsonObject { ["requestId"] = requestId, ["expectedRevision"] = expected, ["bannerId"] = bannerId, ["count"] = count }.ToJsonString();
    static string LaunchBody(string requestId, long expected) => new JsonObject { ["requestId"] = requestId, ["expectedRevision"] = expected }.ToJsonString();
    static string SelectorBody(string requestId, long expected, string charId) =>
        new JsonObject { ["requestId"] = requestId, ["expectedRevision"] = expected, ["charId"] = charId }.ToJsonString();
    // Raw member text, for number forms a JsonObject cannot express (1.0, 1e0, beyond Int64).
    static string PullRaw(string requestId, string expected, string bannerId, string count) =>
        "{\"requestId\":\"" + requestId + "\",\"expectedRevision\":" + expected + ",\"bannerId\":" + bannerId + ",\"count\":" + count + "}";
    async Task<byte[]> StoredOf(RecruitAccount account) => await account.Stored() ?? throw new CheckFailure("an account that committed has no stored envelope");

    // ───────── file suite: a fresh store; alpha recruits from nothing, beta has an active run first, gamma never commits ─────────
    async Task RecruitmentFile()
    {
        string directory = Path.Combine(temp, "store-recruitment");
        string alpha = AccountFile(directory, "alpha"), beta = AccountFile(directory, "beta");
        var host = await Start("recruitment host", root, directory);
        var runs = Client(host);
        var created = await runs.Post(RunsPath, tokens["beta"], Obj(("requestId", Id())));
        Status(created, 200, null, "beta creates a run before recruiting");
        seeds.Add(ReadBody(beta).GetProperty("run").GetProperty("seed").GetInt64());
        Status(await Command(runs, tokens["beta"], S(created.Json, "runId"), Id(), 0, "enter", new JsonObject { ["nodeId"] = A(created.Json.GetProperty("state"), "available")[0].GetString() }),
            200, null, "beta enters a battle before recruiting");
        Func<string, Func<Task<byte[]?>>> stored = file => () => Task.FromResult<byte[]?>(File.Exists(file) ? File.ReadAllBytes(file) : null);
        Func<string, Func<byte[], Task>> replace = file => bytes => { WritePrivate(file, bytes); return Task.CompletedTask; };
        host = await RecruitmentScenario(host, name => Start(name, root, directory), new RecruitAccount("alpha", tokens["alpha"], stored(alpha), replace(alpha)),
            new RecruitAccount("beta", tokens["beta"], stored(beta), replace(beta)), "");
        Check(!File.Exists(AccountFile(directory, "gamma")), "an account that never recruited has no file on the recruitment store");
        CheckPrivateFiles(directory, "alpha");
        CheckPrivateFiles(directory, "beta");
        host.Kill();
        completed.Add("recruitment");
    }

    // ───────── PostgreSQL mode, after RunPostgres: a fresh run-tagged alias recruits from nothing; beta (an active run) is the other ─────────
    public async Task RunPostgresRecruitment()
    {
        RecruitmentScanSafety();
        string owner = Alias("delta"), beta = Alias("beta"), gamma = Alias("gamma");
        tokens[owner] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Check(await Oracle.Rows(owner) == 0, "a fresh run-tagged recruitment alias has no stored row");
        // Same reference party for every alias, so the existing rows keep their fixture binding.
        WritePrivate(fixture, FixtureJson(null, new[] { Alias("alpha"), beta, gamma, owner }));
        var host = await StartPostgres("postgres recruitment host", root);
        var api = RecruitClient(host);
        byte[] gammaRow = await Stored(gamma);
        Status(await RecruitRead(api, Credential("gamma")), 409, "STORE_CONTENT_MISMATCH", "recruitment read of a row bound to other content (postgres)");
        Status(await RecruitLaunch(api, Credential("gamma"), LaunchBody(Id(), 0)), 409, "STORE_CONTENT_MISMATCH", "a launch claim on a row bound to other content (postgres)");
        Same(await Stored(gamma), gammaRow, "the mismatched row is preserved by recruitment requests (postgres)");
        host = await RecruitmentScenario(host, name => StartPostgres(name, root), new RecruitAccount(owner, tokens[owner], () => Oracle.Envelope(owner), bytes => Oracle.Replace(owner, bytes)),
            new RecruitAccount(beta, Credential("beta"), () => Oracle.Envelope(beta), bytes => Oracle.Replace(beta, bytes)), " (postgres)");
        host.Kill();
        completed.Add("pg-recruitment");
        RecruitmentLeakScan();
        LeakScan(PostgresScenarios.Append("pg-recruitment").ToArray());
    }

    // owner: no envelope yet. other: an envelope with an active run and no recruitment. start(name) launches another host on the same store.
    async Task<ServerProcess> RecruitmentScenario(ServerProcess server, Func<string, Task<ServerProcess>> start, RecruitAccount owner, RecruitAccount other, string label)
    {
        var api = RecruitClient(server);
        string token = owner.Token;
        var (empty, refusedId) = await RecruitRefusals(api, owner, label);
        var cfg = oracleConfig ?? throw new CheckFailure("the recruitment oracle is not configured");
        int price = cfg.CostPerPull, tenTickets = cfg.Curves["beginner"].TenPullTickets;

        // ── the launch claim (with the requestId of a refused pull: a refusal consumed nothing) ──
        var launched = await RecruitLaunch(api, token, LaunchBody(refusedId, 0));
        Status(launched, 200, null, "the launch claim" + label);
        SameLaunch(empty, launched, refusedId, await StoredOf(owner), "the launch claim" + label);
        var snapshot = launched.Json.GetProperty("snapshot");
        Same((await RecruitRead(api, token)).Body, Raw(launched.Json, "snapshot"), "the read after the claim is the claim's snapshot" + label);
        Same((await RecruitLaunch(api, token, LaunchBody(refusedId, 0))).Body, launched.Body, "a retried launch claim returns the original bytes" + label);
        byte[] committed = await StoredOf(owner);
        Status(await RecruitLaunch(api, token, LaunchBody(refusedId, 1)), 409, "IDEMPOTENCY_CONFLICT", "the claim's requestId with another expectedRevision" + label);
        Status(await RecruitPull(api, token, PullBody(refusedId, 1, "standard", 1)), 409, "IDEMPOTENCY_CONFLICT", "the claim's requestId as a pull" + label);
        Status(await RecruitSelector(api, token, SelectorBody(refusedId, 1, "sera")), 409, "IDEMPOTENCY_CONFLICT", "the claim's requestId as a selector claim" + label);
        Status(await RecruitLaunch(api, token, LaunchBody(Id(), 0)), 409, "STALE_REVISION", "a new claim against the revision before the claim" + label);
        Status(await RecruitLaunch(api, token, LaunchBody(Id(), 1)), 422, "LAUNCH_ALREADY_CLAIMED", "a second claim at the current revision" + label);
        Status(await RecruitLaunch(api, token, LaunchBody(Id(), 2)), 409, "STALE_REVISION", "a claim against a revision not yet committed" + label);
        Same(await StoredOf(owner), committed, "conflicting, stale and repeated claims wrote nothing" + label);
        Same((await RecruitLaunch(api, token, LaunchBody(refusedId, 0))).Body, launched.Body, "the committed claim's retry still wins before the revision check" + label);

        // ── one standard pull sent 16 times at once: one debit, one reply ──
        string single = Id();
        var copies = await Burst(16, _ => RecruitPull(api, token, PullBody(single, 1, "standard", 1)));
        Check(copies.All(r => r.Status == 200 && r.Body == copies[0].Body), "16 concurrent copies of one pull return the one committed reply" + label);
        byte[] bytes = await StoredOf(owner);
        SamePull(snapshot, copies[0], LedgerEntryOf(bytes, single), single, "standard", 1, "the single standard pull" + label);
        Check(TicketsOf(copies[0].Json.GetProperty("snapshot")) == TicketsOf(snapshot) - 1 && Free(copies[0].Json.GetProperty("snapshot")) == Free(snapshot),
            "a single pull spends a standard ticket before any crystal" + label);
        snapshot = copies[0].Json.GetProperty("snapshot");
        Same((await RecruitPull(api, token, PullBody(single, 1, "standard", 1))).Body, copies[0].Body, "a retried pull returns the original bytes, never a new draw" + label);
        Status(await RecruitPull(api, token, PullBody(single, 1, "standard", 10)), 409, "IDEMPOTENCY_CONFLICT", "the pull's requestId with another count" + label);
        Status(await RecruitPull(api, token, PullBody(single, 2, "standard", 1)), 409, "IDEMPOTENCY_CONFLICT", "the pull's requestId with another expectedRevision" + label);
        Status(await RecruitPull(api, token, PullBody(single, 1, "beginner", 10)), 409, "IDEMPOTENCY_CONFLICT", "the pull's requestId on another banner" + label);
        Status(await RecruitPull(api, token, PullBody(Id(), 1, "standard", 1)), 409, "STALE_REVISION", "a new pull against the revision before the last one" + label);
        Same(await StoredOf(owner), bytes, "retries, conflicts and stale pulls wrote nothing" + label);

        // ── 16 different pulls at one revision: exactly one commits ──
        var racing = await Burst(16, _ => RecruitPull(api, token, PullBody(Id(), 2, "standard", 1)));
        Check(racing.Count(r => r.Status == 200) == 1 && racing.Count(r => r.Status == 409 && r.Code == "STALE_REVISION") == 15,
            "16 concurrent different pulls at one revision: exactly one commits, the rest are STALE_REVISION" + label);
        var won = racing.Single(r => r.Status == 200);
        string wonId = S(won.Json, "requestId");
        SamePull(snapshot, won, LedgerEntryOf(await StoredOf(owner), wonId), wonId, "standard", 1, "the winning concurrent pull" + label);
        snapshot = won.Json.GetProperty("snapshot");

        // ── a standard ten-pull: the remaining tickets first, then free crystals; then a crash after the commit ──
        string ten = Id();
        int ticketsBefore = TicketsOf(snapshot);
        var tenPull = await RecruitPull(api, token, PullBody(ten, 3, "standard", 10));
        Status(tenPull, 200, null, "a standard ten-pull" + label);
        SamePull(snapshot, tenPull, LedgerEntryOf(await StoredOf(owner), ten), ten, "standard", 10, "the standard ten-pull" + label);
        Check(ticketsBefore > 0 && ticketsBefore < 10 && TicketsOf(tenPull.Json.GetProperty("snapshot")) == 0
            && Free(snapshot) - Free(tenPull.Json.GetProperty("snapshot")) == (10L - ticketsBefore) * price, "the ten-pull spent its " + ticketsBefore + " tickets first, then free crystals for the rest" + label);
        snapshot = tenPull.Json.GetProperty("snapshot");
        server.Kill();
        server = await start("recruitment host after a crash" + label);
        api = RecruitClient(server);
        Same((await RecruitPull(api, token, PullBody(ten, 3, "standard", 10))).Body, tenPull.Body, "after a crash the retried ten-pull returns the committed reply (a lost reveal cannot reroll)" + label);
        Same((await RecruitRead(api, token)).Body, Raw(tenPull.Json, "snapshot"), "a new process serves the committed snapshot" + label);

        // ── the beginner banner: ten-only; without tickets Core charges crystals (the disclosed fallback) ──
        bytes = await StoredOf(owner);
        Status(await RecruitPull(api, token, PullBody(Id(), 4, "beginner", 1)), 422, "BEGINNER_TEN_ONLY", "a single beginner pull" + label);
        Same(await StoredOf(owner), bytes, "the refused beginner pull wrote nothing" + label);
        string begin = Id();
        var beginner = await RecruitPull(api, token, PullBody(begin, 4, "beginner", 10));
        Status(beginner, 200, null, "a beginner ten-pull" + label);
        SamePull(snapshot, beginner, LedgerEntryOf(await StoredOf(owner), begin), begin, "beginner", 10, "the beginner ten-pull" + label);
        Check(TicketsOf(snapshot) == 0 && TicketsOf(beginner.Json.GetProperty("snapshot")) == 0 && Free(snapshot) - Free(beginner.Json.GetProperty("snapshot")) == (long)tenTickets * price,
            "with no tickets the beginner ten-pull cost " + tenTickets + " pulls of free crystals (Core's current, disclosed fallback)" + label);
        snapshot = beginner.Json.GetProperty("snapshot");

        // ── an empty wallet: every refusal writes nothing; stale requests are refused before any rule ──
        long rev = N64(snapshot, "revision");
        bytes = await StoredOf(owner);
        string low = Id();
        foreach (var (what, send, status, code) in new (string, Func<Task<Result>>, int, string)[]
        {
            ("a single pull with an empty wallet", () => RecruitPull(api, token, PullBody(low, rev, "standard", 1)), 422, "CRYSTALS_LOW"),
            ("the same refused pull again", () => RecruitPull(api, token, PullBody(low, rev, "standard", 1)), 422, "CRYSTALS_LOW"),
            ("a standard ten-pull with an empty wallet", () => RecruitPull(api, token, PullBody(Id(), rev, "standard", 10)), 422, "CRYSTALS_LOW"),
            ("a beginner ten-pull with an empty wallet", () => RecruitPull(api, token, PullBody(Id(), rev, "beginner", 10)), 422, "CRYSTALS_LOW"),
            ("a single beginner pull", () => RecruitPull(api, token, PullBody(Id(), rev, "beginner", 1)), 422, "BEGINNER_TEN_ONLY"),
            ("a zero count", () => RecruitPull(api, token, PullBody(Id(), rev, "standard", 0)), 422, "BAD_COUNT"),
            ("an inactive banner", () => RecruitPull(api, token, PullBody(Id(), rev, inactiveBanners[0], 1)), 422, "BANNER_INACTIVE"),
            ("the selector before " + cfg.Curves["standard"].SelectorAt + " pulls", () => RecruitSelector(api, token, SelectorBody(Id(), rev, "sera")), 422, "SELECTOR_NOT_READY"),
            ("another launch claim", () => RecruitLaunch(api, token, LaunchBody(Id(), rev)), 422, "LAUNCH_ALREADY_CLAIMED"),
            ("a stale empty-wallet pull", () => RecruitPull(api, token, PullBody(Id(), rev - 1, "standard", 1)), 409, "STALE_REVISION"),
            ("a stale beginner pull", () => RecruitPull(api, token, PullBody(Id(), rev - 1, "beginner", 1)), 409, "STALE_REVISION"),
            ("a stale selector claim", () => RecruitSelector(api, token, SelectorBody(Id(), rev - 1, "sera")), 409, "STALE_REVISION"),
            ("a pull at a revision not yet committed", () => RecruitPull(api, token, PullBody(Id(), rev + 1, "standard", 1)), 409, "STALE_REVISION"),
        })
            Status(await send(), status, code, what + label);
        Same(await StoredOf(owner), bytes, "refusals on an empty wallet wrote nothing" + label);
        var pity = snapshot.GetProperty("pity");
        Check(I(pity.GetProperty("standard"), "total") == 22 && I(pity.GetProperty("beginner"), "total") == 10 && !pity.GetProperty("beginner").GetProperty("done").GetBoolean()
            && A(snapshot, "history").Select(h => S(h, "requestId")).SequenceEqual(new[] { begin, ten, wonId, single }),
            "the selector total counts standard and beginner pulls; the history is the four pulls, newest first" + label);
        var ledger = A(BodyOf(bytes).GetProperty("recruitment"), "ledger");
        Check(ledger.Select(e => S(e, "kind")).SequenceEqual(new[] { "launch", "pull", "pull", "pull", "pull" }) && ledger.Select(e => N64(e, "expectedRevision")).SequenceEqual(new long[] { 0, 1, 2, 3, 4 })
            && ledger.Skip(1).Select(e => e.GetProperty("draws").GetArrayLength()).SequenceEqual(new[] { 2, 2, 20, 20 }),
            "the private ledger keeps every commit in order with its revision and a rarity roll plus a pick per pull" + label);

        // ── restart: every receipt replays its original bytes ──
        server.Kill();
        server = await start("recruitment host after the pulls" + label);
        api = RecruitClient(server);
        foreach (var (send, reply, what) in new (Func<Task<Result>>, Result, string)[]
        {
            (() => RecruitLaunch(api, token, LaunchBody(refusedId, 0)), launched, "launch claim"), (() => RecruitPull(api, token, PullBody(single, 1, "standard", 1)), copies[0], "single pull"),
            (() => RecruitPull(api, token, PullBody(wonId, 2, "standard", 1)), won, "concurrent winner"), (() => RecruitPull(api, token, PullBody(ten, 3, "standard", 10)), tenPull, "ten-pull"),
            (() => RecruitPull(api, token, PullBody(begin, 4, "beginner", 10)), beginner, "beginner ten-pull"),
        })
            Same((await send()).Body, reply.Body, "after a restart the " + what + " receipt returns its original bytes" + label);
        Same((await RecruitRead(api, token)).Body, Raw(beginner.Json, "snapshot"), "after a restart the read is the last committed snapshot" + label);

        await OtherAccount(server, start, api, owner, other, single, label);
        await RunAfterRecruitment(server, api, owner, snapshot, label);
        await RecruitmentTamper(api, Client(server), owner, label);
        return server;
    }

    // ───────── unauthenticated, malformed, rule and stale refusals while the owner has no envelope ─────────
    async Task<(JsonElement Empty, string RefusedId)> RecruitRefusals(Api api, RecruitAccount owner, string label)
    {
        string token = owner.Token, unknown = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        foreach (var (what, credential) in new (string, string?)[] { ("no credential", null), ("an unknown credential", unknown) })
        {
            Status(await RecruitRead(api, credential), 401, "UNAUTHORIZED", "a recruitment read with " + what + label);
            Status(await RecruitPull(api, credential, PullBody(Id(), 0, "standard", 1)), 401, "UNAUTHORIZED", "a pull with " + what + label);
            Status(await RecruitLaunch(api, credential, LaunchBody(Id(), 0)), 401, "UNAUTHORIZED", "a launch claim with " + what + label);
            Status(await RecruitSelector(api, credential, SelectorBody(Id(), 0, "sera")), 401, "UNAUTHORIZED", "a selector claim with " + what + label);
        }
        var read = await RecruitRead(api, token);
        Status(read, 200, null, "the first recruitment read" + label);
        var empty = read.Json;
        SnapshotShape(empty, owner.Id, "the first recruitment read" + label);
        oracleConfig ??= OracleConfig();
        SameBanners(empty, "the first recruitment read" + label);
        var wallet = empty.GetProperty("wallet");
        Check(N64(empty, "revision") == 0 && WalletMembers.Where(m => m != "tickets").All(m => N64(wallet, m) == 0) && TicketMembers.All(t => I(wallet.GetProperty("tickets"), t) == 0)
            && !empty.GetProperty("ownedChars").EnumerateObject().Any() && !empty.GetProperty("ownedWeapons").EnumerateObject().Any()
            && PityKindMembers.All(k => empty.GetProperty("pity").GetProperty(k).EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.False || p.Value.ValueKind == JsonValueKind.Number && p.Value.GetInt32() == 0))
            && !empty.GetProperty("launchClaimed").GetBoolean() && A(empty, "history").Count == 0,
            "a new account: revision 0, an empty wallet and collection, zero pity, launch unclaimed, no history (no fixture-party ownership)" + label);
        Check(await owner.Stored() == null, "the read created no envelope" + label);

        string rid = Id();
        var bodies = new List<(string What, Func<Task<Result>> Send, int Status, string Code)>();
        void Pull(string what, string body, string code) => bodies.Add(("a pull with " + what, () => RecruitPull(api, token, body), 400, code));
        Pull("malformed JSON", "not json", "INVALID_JSON");
        Pull("a non-object body", "[]", "INVALID_REQUEST");
        Pull("no requestId", "{\"expectedRevision\":0,\"bannerId\":\"standard\",\"count\":1}", "INVALID_REQUEST_ID");
        Pull("a short requestId", PullRaw("short", "0", "\"standard\"", "1"), "INVALID_REQUEST_ID");
        Pull("a requestId with a space", PullRaw("chk with space", "0", "\"standard\"", "1"), "INVALID_REQUEST_ID");
        Pull("a 65-character requestId", PullRaw(new string('a', 65), "0", "\"standard\"", "1"), "INVALID_REQUEST_ID");
        Pull("no expectedRevision", "{\"requestId\":\"" + rid + "\",\"bannerId\":\"standard\",\"count\":1}", "INVALID_REQUEST");
        foreach (var (what, raw) in new[] { ("negative", "-1"), ("fractional", "0.5"), ("1.0", "1.0"), ("exponent", "1e0"), ("string", "\"0\""), ("null", "null"), ("boolean", "true"),
            ("beyond-Int64", "9223372036854775808") })
            Pull("a " + what + " expectedRevision", PullRaw(rid, raw, "\"standard\"", "1"), "INVALID_REQUEST");
        Pull("no bannerId", "{\"requestId\":\"" + rid + "\",\"expectedRevision\":0,\"count\":1}", "INVALID_REQUEST");
        foreach (var (what, raw) in new[] { ("numeric", "7"), ("empty", "\"\""), ("null", "null"), ("non-ASCII", "\"st\\u00e4ndard\""), ("129-character", "\"" + new string('b', 129) + "\"") })
            Pull("a " + what + " bannerId", PullRaw(rid, "0", raw, "1"), "INVALID_REQUEST");
        Pull("no count", "{\"requestId\":\"" + rid + "\",\"expectedRevision\":0,\"bannerId\":\"standard\"}", "INVALID_REQUEST");
        foreach (var (what, raw) in new[] { ("negative", "-1"), ("fractional", "0.5"), ("string", "\"1\""), ("beyond-Int32", "2147483648") })
            Pull("a " + what + " count", PullRaw(rid, "0", "\"standard\"", raw), "INVALID_REQUEST");
        Pull("a duplicate member", "{\"requestId\":\"" + rid + "\",\"expectedRevision\":0,\"expectedRevision\":0,\"bannerId\":\"standard\",\"count\":1}", "DUPLICATE_FIELD");
        foreach (string extra in new[] { "accountId", "seed", "rng", "state", "wallet", "crystals", "freeCrystals", "tickets", "pity", "results", "draws", "growth", "revision" })
            Pull("a client " + extra, "{\"requestId\":\"" + rid + "\",\"expectedRevision\":0,\"bannerId\":\"standard\",\"count\":1,\"" + extra + "\":1}", "UNKNOWN_FIELD");
        void Other(string what, Func<Task<Result>> send, int status, string code) => bodies.Add((what, send, status, code));
        Other("a launch claim without expectedRevision", () => RecruitLaunch(api, token, "{\"requestId\":\"" + rid + "\"}"), 400, "INVALID_REQUEST");
        Other("a launch claim with a negative expectedRevision", () => RecruitLaunch(api, token, "{\"requestId\":\"" + rid + "\",\"expectedRevision\":-1}"), 400, "INVALID_REQUEST");
        Other("a launch claim with a fractional expectedRevision", () => RecruitLaunch(api, token, "{\"requestId\":\"" + rid + "\",\"expectedRevision\":0.5}"), 400, "INVALID_REQUEST");
        Other("a launch claim without requestId", () => RecruitLaunch(api, token, "{\"expectedRevision\":0}"), 400, "INVALID_REQUEST_ID");
        foreach (string extra in new[] { "bannerId", "freeCrystals", "grant", "accountId" })
            Other("a launch claim with a client " + extra, () => RecruitLaunch(api, token, "{\"requestId\":\"" + rid + "\",\"expectedRevision\":0,\"" + extra + "\":1}"), 400, "UNKNOWN_FIELD");
        Other("a selector claim without charId", () => RecruitSelector(api, token, "{\"requestId\":\"" + rid + "\",\"expectedRevision\":0}"), 400, "INVALID_REQUEST");
        Other("a selector claim with a numeric charId", () => RecruitSelector(api, token, "{\"requestId\":\"" + rid + "\",\"expectedRevision\":0,\"charId\":7}"), 400, "INVALID_REQUEST");
        Other("a selector claim with a negative expectedRevision", () => RecruitSelector(api, token, "{\"requestId\":\"" + rid + "\",\"expectedRevision\":-1,\"charId\":\"sera\"}"), 400, "INVALID_REQUEST");
        Other("a selector claim with a client count", () => RecruitSelector(api, token, "{\"requestId\":\"" + rid + "\",\"expectedRevision\":0,\"charId\":\"sera\",\"count\":1}"), 400, "UNKNOWN_FIELD");
        Other("a pull that is not JSON", () => api.Post(RecruitPath + "/pulls", token, PullBody(rid, 0, "standard", 1), "text/plain"), 415, "UNSUPPORTED_MEDIA_TYPE");
        Other("GET on the pull route", () => api.Get(RecruitPath + "/pulls", token), 405, "METHOD_NOT_ALLOWED");
        Other("GET on the launch route", () => api.Get(RecruitPath + "/claim-launch", token), 405, "METHOD_NOT_ALLOWED");
        Other("POST on the recruitment read", () => api.Post(RecruitPath, token, PullBody(rid, 0, "standard", 1)), 405, "METHOD_NOT_ALLOWED");
        Other("an unknown recruitment route", () => api.Get(RecruitPath + "/history", token), 404, "NOT_FOUND");
        Other("a trailing route segment", () => api.Post(RecruitPath + "/pulls/extra", token, PullBody(rid, 0, "standard", 1)), 404, "NOT_FOUND");
        Other("a query string", () => api.Get(RecruitPath + "?bannerId=standard", token), 400, "INVALID_REQUEST");

        // Rules at the current revision 0 (Core's codes and the inactive banners), then stale revisions, which are refused before any rule.
        string refusedId = Id();
        Other("a standard pull without funds", () => RecruitPull(api, token, PullBody(refusedId, 0, "standard", 1)), 422, "CRYSTALS_LOW");
        Other("the same refused pull again (nothing was consumed)", () => RecruitPull(api, token, PullBody(refusedId, 0, "standard", 1)), 422, "CRYSTALS_LOW");
        Other("a standard ten-pull without funds", () => RecruitPull(api, token, PullBody(Id(), 0, "standard", 10)), 422, "CRYSTALS_LOW");
        Other("a beginner ten-pull without funds", () => RecruitPull(api, token, PullBody(Id(), 0, "beginner", 10)), 422, "CRYSTALS_LOW");
        Other("a single beginner pull", () => RecruitPull(api, token, PullBody(Id(), 0, "beginner", 1)), 422, "BEGINNER_TEN_ONLY");
        foreach (int count in new[] { 0, 2, 11 }) Other("a pull of " + count, () => RecruitPull(api, token, PullBody(Id(), 0, "standard", count)), 422, "BAD_COUNT");
        Other("an unknown banner", () => RecruitPull(api, token, PullBody(Id(), 0, "no_such_banner", 1)), 422, "UNKNOWN_BANNER");
        foreach (string banner in inactiveBanners) Other("the inactive banner " + SafeName(banner), () => RecruitPull(api, token, PullBody(Id(), 0, banner, 1)), 422, "BANNER_INACTIVE");
        Other("the selector before any pull", () => RecruitSelector(api, token, SelectorBody(Id(), 0, "sera")), 422, "SELECTOR_NOT_READY");
        Other("the selector with an unknown choice before any pull", () => RecruitSelector(api, token, SelectorBody(Id(), 0, "nobody")), 422, "SELECTOR_NOT_READY");
        Other("a pull at a revision not yet committed", () => RecruitPull(api, token, PullBody(Id(), 1, "standard", 1)), 409, "STALE_REVISION");
        Other("an unknown banner at a stale revision (revision first)", () => RecruitPull(api, token, PullBody(Id(), 1, "no_such_banner", 1)), 409, "STALE_REVISION");
        Other("a zero count at a stale revision (revision first)", () => RecruitPull(api, token, PullBody(Id(), 3, "standard", 0)), 409, "STALE_REVISION");
        Other("a launch claim at a revision not yet committed", () => RecruitLaunch(api, token, LaunchBody(Id(), 1)), 409, "STALE_REVISION");
        Other("a selector claim at a revision not yet committed", () => RecruitSelector(api, token, SelectorBody(Id(), 1, "sera")), 409, "STALE_REVISION");
        foreach (var item in bodies) Status(await item.Send(), item.Status, item.Code, item.What + label);
        Check(await owner.Stored() == null, "unauthenticated, malformed, refused and stale requests created no envelope (no file or row)" + label);
        return (empty, refusedId);
    }

    // ───────── another account: isolation, cross-process races, per-account request IDs and an active run left untouched ─────────
    async Task OtherAccount(ServerProcess server, Func<string, Task<ServerProcess>> start, Api api, RecruitAccount owner, RecruitAccount other, string ownerRequestId, string label)
    {
        byte[] ownerBytes = await StoredOf(owner), otherBytes = await StoredOf(other);
        var body = BodyOf(otherBytes);
        var run = body.GetProperty("run");
        Check(S(body, "accountId") == other.Id && run.ValueKind == JsonValueKind.Object && !body.TryGetProperty("recruitment", out _),
            "precondition: the other account has an active run and has never recruited" + label);
        string runId = S(run, "runId");
        var runs = Client(server);
        var view = await runs.Get(RunsPath + "/" + runId, other.Token);
        Status(view, 200, null, "the other account's run read before recruiting" + label);
        var read = await RecruitRead(api, other.Token);
        Status(read, 200, null, "the other account's recruitment read" + label);
        var start0 = read.Json;
        SnapshotShape(start0, other.Id, "the other account's recruitment read" + label);
        Check(N64(start0, "revision") == body.GetProperty("revision").GetInt64() && !start0.GetProperty("launchClaimed").GetBoolean() && A(start0, "history").Count == 0
            && Free(start0) == 0 && TicketsOf(start0) == 0 && !start0.GetProperty("ownedChars").EnumerateObject().Any(),
            "another account sees only its own empty recruitment at its own envelope revision, never the owner's" + label);
        long r0 = N64(start0, "revision");

        var second = await start("second recruitment host" + label);
        var api2 = RecruitClient(second);
        string claim = Id();
        var claims = await Burst(16, i => RecruitLaunch(i % 2 == 0 ? api : api2, other.Token, LaunchBody(claim, r0)));
        Check(claims.All(r => r.Status == 200 && r.Body == claims[0].Body), "16 copies of one launch claim through two host processes commit once and reply identically" + label);
        SameLaunch(start0, claims[0], claim, await StoredOf(other), "the cross-process launch claim" + label);
        var claimed = claims[0].Json.GetProperty("snapshot");
        var racing = await Burst(16, i => RecruitPull(i % 2 == 0 ? api : api2, other.Token, PullBody(Id(), r0 + 1, "standard", 1)));
        Check(racing.Count(r => r.Status == 200) == 1 && racing.Count(r => r.Status == 409 && r.Code == "STALE_REVISION") == 15,
            "16 different pulls at one revision through two host processes: exactly one commits" + label);
        var won = racing.Single(r => r.Status == 200);
        string wonId = S(won.Json, "requestId");
        SamePull(claimed, won, LedgerEntryOf(await StoredOf(other), wonId), wonId, "standard", 1, "the cross-process winning pull" + label);
        Status(await RecruitLaunch(api2, other.Token, LaunchBody(Id(), r0 + 2)), 422, "LAUNCH_ALREADY_CLAIMED", "a second claim through the other process" + label);
        // Request IDs are per account: the owner's committed pull requestId is a new request here.
        var reused = await RecruitPull(api2, other.Token, PullBody(ownerRequestId, r0 + 2, "standard", 1));
        Status(reused, 200, null, "the owner's committed requestId is a new request for another account" + label);
        SamePull(won.Json.GetProperty("snapshot"), reused, LedgerEntryOf(await StoredOf(other), ownerRequestId), ownerRequestId, "standard", 1, "the per-account requestId pull" + label);
        second.Kill();

        Same(await StoredOf(owner), ownerBytes, "the other account's recruitment changed nothing of the owner" + label);
        var after = BodyOf(await StoredOf(other));
        Check(after.GetProperty("run").GetRawText() == run.GetRawText() && after.GetProperty("archive").GetRawText() == body.GetProperty("archive").GetRawText()
            && after.GetProperty("memory").GetRawText() == body.GetProperty("memory").GetRawText(), "recruitment never touched the active run, the archive or the memory store" + label);
        var again = await runs.Get(RunsPath + "/" + runId, other.Token);
        Same(again.Body, view.Body, "the active run's view is byte-identical after recruitment" + label);
        Check(I(again.Json, "version") == run.GetProperty("journal").GetArrayLength(), "the run version is still its journal length" + label);
    }

    // ───────── a run mutation after recruiting: the fixed practice party, the revision moves, recruitment becomes stale ─────────
    async Task RunAfterRecruitment(ServerProcess server, Api api, RecruitAccount owner, JsonElement last, string label)
    {
        var created = await Client(server).Post(RunsPath, owner.Token, Obj(("requestId", Id())));
        Status(created, 200, null, "a practice run after recruiting" + label);
        seeds.Add(BodyOf(await StoredOf(owner)).GetProperty("run").GetProperty("seed").GetInt64());
        Check(A(created.Json.GetProperty("state"), "party").Select(p => S(p, "charId")).SequenceEqual(Party),
            "the run still uses the fixed fixture party: recruited copies are never inferred into it" + label);
        var read = await RecruitRead(api, owner.Token);
        Status(read, 200, null, "the recruitment read after the run mutation" + label);
        Check(N64(read.Json, "revision") == N64(last, "revision") + 1 && SnapshotMembers.Where(m => m != "revision").All(m => Raw(read.Json, m) == Raw(last, m)),
            "the run mutation advanced the envelope revision only; the recruitment state is unchanged" + label);
        Status(await RecruitPull(api, owner.Token, PullBody(Id(), N64(last, "revision"), "standard", 1)), 409, "STALE_REVISION", "recruitment against the revision before the run mutation" + label);
        Status(await RecruitLaunch(api, owner.Token, LaunchBody(Id(), N64(read.Json, "revision"))), 422, "LAUNCH_ALREADY_CLAIMED", "the launch claim at the new revision" + label);
    }

    // ───────── tampered but re-digested recruitment members are refused and preserved; the binding still comes first ─────────
    async Task RecruitmentTamper(Api api, Api runs, RecruitAccount owner, string label)
    {
        byte[] good = await StoredOf(owner);
        var read = await RecruitRead(api, owner.Token);
        Status(read, 200, null, "the intact owner envelope before tampering" + label);
        long rev = N64(read.Json, "revision");
        var cfg = oracleConfig!;
        var stored = BodyOf(good);
        var ledger = A(stored.GetProperty("recruitment"), "ledger");
        string launchId = S(ledger[0], "requestId"), firstPull = S(ledger[1], "requestId");
        // The first pull's exact original request and committed reply: its retry must replay these bytes and never a stored forgery.
        string retryBody = PullBody(firstPull, N64(ledger[1], "expectedRevision"), S(ledger[1], "bannerId"), I(ledger[1], "count"));
        string original = S(stored.GetProperty("receipts").GetProperty(firstPull), "body");
        var result = ledger[1].GetProperty("results")[0];
        string kind = S(result, "kind"), rarity = S(result, "rarity");
        var pool = kind == "item" ? cfg.RItems : rarity == "SSR" ? cfg.StandardSsrChars : kind == "char" ? cfg.SrChars : cfg.SrWeapons;
        string swapped = pool.First(id => id != S(result, "id"));
        JsonObject R(JsonObject b) => b["recruitment"]!.AsObject();
        JsonObject W(JsonObject b) => R(b)["wallet"]!.AsObject();
        JsonObject P(JsonObject b) => R(b)["ledger"]![1]!.AsObject();
        // A stored receipt's reply, edited and re-encoded the way the server writes JSON (relaxed escaping, no indentation): an unedited
        // re-encoding is byte-identical (a control below), so each refusal is its edit's.
        void Receipt(JsonObject b, string id, Action<JsonNode> edit)
        {
            var receipt = b["receipts"]![id]!.AsObject();
            var reply = JsonNode.Parse(receipt["body"]!.GetValue<string>())!;
            edit(reply);
            receipt["body"] = reply.ToJsonString(ServerJson);
        }
        var cases = new (string What, byte[] Bytes, int Status, string Code)[]
        {
            ("an unbacked free crystal balance", Tamper(good, b => W(b)["freeCrystals"] = W(b)["freeCrystals"]!.GetValue<long>() + 1600, true), 500, "STORE_CORRUPT"),
            ("paid crystals without a ledger source", Tamper(good, b => W(b)["paidCrystals"] = 160, true), 500, "STORE_CORRUPT"),
            ("an extra standard ticket", Tamper(good, b => W(b)["tickets"]!["standardTicket"] = W(b)["tickets"]!["standardTicket"]!.GetValue<int>() + 1, true), 500, "STORE_CORRUPT"),
            ("shards without a duplicate", Tamper(good, b => W(b)["shards"] = W(b)["shards"]!.GetValue<long>() + 2, true), 500, "STORE_CORRUPT"),
            ("afterglow without an R result", Tamper(good, b => W(b)["afterglow"] = W(b)["afterglow"]!.GetValue<long>() + 15, true), 500, "STORE_CORRUPT"),
            ("an extra owned character copy", Tamper(good, b => { var o = R(b)["ownedChars"]!.AsObject(); o["sera"] = (o["sera"]?.GetValue<int>() ?? 0) + 1; }, true), 500, "STORE_CORRUPT"),
            ("an extra owned weapon copy", Tamper(good, b => { var o = R(b)["ownedWeapons"]!.AsObject(); o["w_mech_bow"] = (o["w_mech_bow"]?.GetValue<int>() ?? 0) + 1; }, true), 500, "STORE_CORRUPT"),
            ("moved standard pity", Tamper(good, b => { var p = R(b)["pity"]!["standard"]!; int n = p["n"]!.GetValue<int>(); p["n"] = n > 0 ? n - 1 : n + 1; }, true), 500, "STORE_CORRUPT"),
            ("an inflated selector total", Tamper(good, b => R(b)["pity"]!["standard"]!["total"] = R(b)["pity"]!["standard"]!["total"]!.GetValue<int>() + 1, true), 500, "STORE_CORRUPT"),
            ("a cleared launch flag", Tamper(good, b => R(b)["launchClaimed"] = false, true), 500, "STORE_CORRUPT"),
            ("an edited ledger debit", Tamper(good, b => P(b)["debit"]!["freeCrystals"] = P(b)["debit"]!["freeCrystals"]!.GetValue<long>() + 160, true), 500, "STORE_CORRUPT"),
            ("a changed raw draw (another known result)", Tamper(good, b =>
            {
                var pick = P(b)["draws"]![1]!.AsArray();
                pick[1] = (pick[1]!.GetValue<int>() + 1) % pick[0]!.GetValue<int>();
            }, true), 500, "STORE_CORRUPT"),
            ("a changed draw bound", Tamper(good, b => P(b)["draws"]![0]![0] = 9999, true), 500, "STORE_CORRUPT"),
            ("a draw at its bound", Tamper(good, b => P(b)["draws"]![0]![1] = 10000, true), 500, "STORE_CORRUPT"),
            ("an unused extra draw", Tamper(good, b => P(b)["draws"]!.AsArray().Add(new JsonArray(10000, 0)), true), 500, "STORE_CORRUPT"),
            ("a result swapped to another known ID", Tamper(good, b => P(b)["results"]![0]!["id"] = swapped, true), 500, "STORE_CORRUPT"),
            ("a flipped duplicate flag", Tamper(good, b => { var r = P(b)["results"]![0]!; r["isNew"] = !r["isNew"]!.GetValue<bool>(); }, true), 500, "STORE_CORRUPT"),
            ("a removed launch entry", Tamper(good, b => R(b)["ledger"]!.AsArray().RemoveAt(0), true), 500, "STORE_CORRUPT"),
            ("a moved ledger revision", Tamper(good, b => P(b)["expectedRevision"] = 9, true), 500, "STORE_CORRUPT"),
            ("a ledger entry without its receipt", Tamper(good, b => b["receipts"]!.AsObject().Remove(firstPull), true), 500, "STORE_CORRUPT"),
            ("a null recruitment member", Tamper(good, b => b["recruitment"] = null, true), 500, "STORE_CORRUPT"),
            ("an unknown recruitment member", Tamper(good, b => R(b)["bonus"] = 1, true), 500, "STORE_CORRUPT"),
            // Receipts: each must be its ledger entry's exact reply (Core-replayed prefix snapshot), and every recruitment receipt needs its entry.
            ("a receipt result changed to another known ID", Tamper(good, b => Receipt(b, firstPull, r =>
            {
                r["results"]![0]!["id"] = swapped;
                r["snapshot"]!["history"]![0]!["results"]![0]!["id"] = swapped;
            }), true), 500, "STORE_CORRUPT"),
            ("a receipt's historical wallet", Tamper(good, b => Receipt(b, firstPull,
                r => r["snapshot"]!["wallet"]!["freeCrystals"] = r["snapshot"]!["wallet"]!["freeCrystals"]!.GetValue<long>() + 160), true), 500, "STORE_CORRUPT"),
            ("a receipt's historical pity", Tamper(good, b => Receipt(b, firstPull, r =>
            {
                var p = r["snapshot"]!["pity"]!["standard"]!;
                int n = p["n"]!.GetValue<int>();
                p["n"] = n > 0 ? n - 1 : n + 1;
            }), true), 500, "STORE_CORRUPT"),
            ("a receipt's historical owned copies", Tamper(good, b => Receipt(b, firstPull, r =>
            {
                var o = r["snapshot"]!["ownedChars"]!.AsObject();
                o["sera"] = (o["sera"]?.GetValue<int>() ?? 0) + 1;
            }), true), 500, "STORE_CORRUPT"),
            ("a private draw tape injected into a receipt result", Tamper(good, b => Receipt(b, firstPull, r => r["results"]![0]!["draws"] = new JsonArray(new JsonArray(10000, 1))), true),
                500, "STORE_CORRUPT"),
            ("a private ledger injected into a receipt's historical snapshot", Tamper(good, b => Receipt(b, firstPull, r => r["snapshot"]!["ledger"] = new JsonArray()), true),
                500, "STORE_CORRUPT"),
            ("a malformed receipt body", Tamper(good, b => b["receipts"]![firstPull]!["body"] = "{not json", true), 500, "STORE_CORRUPT"),
            ("a receipt for another account", Tamper(good, b => Receipt(b, firstPull, r => r["accountId"] = "someone-else"), true), 500, "STORE_CORRUPT"),
            ("a receipt naming another request", Tamper(good, b => Receipt(b, firstPull, r => r["requestId"] = "chk-another-request"), true), 500, "STORE_CORRUPT"),
            ("a receipt of another kind", Tamper(good, b => Receipt(b, launchId, r => r["kind"] = "selector"), true), 500, "STORE_CORRUPT"),
            ("a receipt at another revision", Tamper(good, b => Receipt(b, firstPull, r => r["revision"] = r["revision"]!.GetValue<long>() + 1), true), 500, "STORE_CORRUPT"),
            ("the recruitment member removed with its receipts kept", Tamper(good, b => b.Remove("recruitment"), true), 500, "STORE_CORRUPT"),
            ("an emptied recruitment member with its receipts kept", Tamper(good, b => b["recruitment"] = EmptyRecruitment(), true), 500, "STORE_CORRUPT"),
            ("an orphan recruitment receipt", Tamper(good, b => b["receipts"]!.AsObject().Add("chk-orphan-" + firstPull.Substring(4), b["receipts"]![firstPull]!.DeepClone()), true),
                500, "STORE_CORRUPT"),
            ("another Core build", Tamper(good, b => b["binding"]!["coreModule"] = new string('0', 32), true), 409, "STORE_IMPLEMENTATION_MISMATCH"),
            ("other content", Tamper(good, b => b["binding"]!["contentSha256"] = new string('0', 64), true), 409, "STORE_CONTENT_MISMATCH"),
        };
        try
        {
            // Controls: the same re-digest path without an edit, and an unedited receipt re-encoding, are accepted and replay the original
            // reply, so each refusal below is its edit's.
            foreach (var (what, bytes) in new[] { ("a re-digested but unedited envelope", Tamper(good, b => { }, true)), ("an unedited receipt re-encoding", Tamper(good, b => Receipt(b, firstPull, r => { }), true)) })
            {
                await owner.Replace(bytes);
                var control = await RecruitRead(api, owner.Token);
                Status(control, 200, null, what + " is accepted" + label);
                Same(control.Body, read.Body, what + " serves the same snapshot" + label);
                Same((await RecruitPull(api, owner.Token, retryBody)).Body, original, what + ": the exact retry replays the original reply" + label);
                Same(await StoredOf(owner), bytes, what + ": reads and retries wrote nothing" + label);
            }
            // The never-recruited shape (no member and no recruitment receipt) stays accepted. Removing a whole consistent history is
            // therefore not detected: this is internal consistency, not authentication (there is no MAC).
            var legacy = Tamper(good, b =>
            {
                b.Remove("recruitment");
                foreach (var entry in ledger) b["receipts"]!.AsObject().Remove(S(entry, "requestId"));
            }, true);
            await owner.Replace(legacy);
            var plain = await RecruitRead(api, owner.Token);
            Status(plain, 200, null, "an envelope without the member and without recruitment receipts is accepted" + label);
            Check(!plain.Json.GetProperty("launchClaimed").GetBoolean() && Free(plain.Json) == 0 && A(plain.Json, "history").Count == 0 && N64(plain.Json, "revision") == rev,
                "the never-recruited shape is shown empty at the envelope revision" + label);
            Status(await runs.Get(AccountPath, owner.Token), 200, null, "the never-recruited shape's account read (its run receipts kept)" + label);
            Same(await StoredOf(owner), legacy, "the accepted shape was only read" + label);
            foreach (var item in cases)
            {
                await owner.Replace(item.Bytes);
                Status(await RecruitRead(api, owner.Token), item.Status, item.Code, item.What + ": recruitment read" + label);
                Status(await RecruitPull(api, owner.Token, retryBody), item.Status, item.Code, item.What + ": the exact retry of a committed pull" + label);
                Status(await RecruitPull(api, owner.Token, PullBody(Id(), rev, "standard", 1)), item.Status, item.Code, item.What + ": a new pull" + label);
                Status(await RecruitLaunch(api, owner.Token, LaunchBody(Id(), rev)), item.Status, item.Code, item.What + ": a new launch claim" + label);
                Status(await runs.Get(AccountPath, owner.Token), item.Status, item.Code, item.What + ": account read" + label);
                Same(await StoredOf(owner), item.Bytes, item.What + ": the refused envelope is preserved byte for byte" + label);
            }
        }
        finally { await owner.Replace(good); }
        Same((await RecruitRead(api, owner.Token)).Body, read.Body, "the host's own committed bytes serve the same snapshot again" + label);
        Same((await RecruitPull(api, owner.Token, retryBody)).Body, original, "after the restore the exact retry still replays the original reply (idempotency unchanged)" + label);
    }

    // The server's JSON text settings (GraphJson): relaxed escaping, no indentation.
    static readonly JsonSerializerOptions ServerJson = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // A structurally valid, empty recruitment member (zero wallet, no copies, zero pity, unclaimed, no ledger).
    static JsonObject EmptyRecruitment()
    {
        var pity = new JsonObject();
        foreach (string kind in PityKindMembers)
            pity[kind] = new JsonObject { ["n"] = 0, ["srN"] = 0, ["guar"] = false, ["srGuar"] = false, ["total"] = 0, ["done"] = false, ["gotSsr"] = false, ["selectorUsed"] = false };
        return new JsonObject
        {
            ["wallet"] = new JsonObject
            {
                ["freeCrystals"] = 0, ["paidCrystals"] = 0, ["tickets"] = new JsonObject { ["limitedTicket"] = 0, ["weaponTicket"] = 0, ["standardTicket"] = 0 }, ["shards"] = 0, ["afterglow"] = 0
            },
            ["ownedChars"] = new JsonObject(), ["ownedWeapons"] = new JsonObject(), ["pity"] = pity, ["launchClaimed"] = false, ["ledger"] = new JsonArray()
        };
    }

    // ───────── reply checks ─────────
    void SnapshotShape(JsonElement s, string accountId, string what)
    {
        Check(s.ValueKind == JsonValueKind.Object && Names(s).SequenceEqual(SnapshotMembers) && S(s, "accountId") == accountId && s.GetProperty("revision").ValueKind == JsonValueKind.Number
            && s.GetProperty("revision").TryGetInt64(out long revision) && revision >= 0, what + ": exactly the agreed snapshot members");
        var wallet = s.GetProperty("wallet");
        Check(Names(wallet).SequenceEqual(WalletMembers) && Names(wallet.GetProperty("tickets")).SequenceEqual(TicketMembers)
            && WalletMembers.Where(m => m != "tickets").All(m => N64(wallet, m) >= 0) && TicketMembers.All(t => I(wallet.GetProperty("tickets"), t) >= 0), what + ": the wallet members");
        foreach (string map in new[] { "ownedChars", "ownedWeapons" })
        {
            var names = Names(s.GetProperty(map));
            Check(names.SequenceEqual(names.OrderBy(n => n, StringComparer.Ordinal)) && s.GetProperty(map).EnumerateObject().All(p => p.Value.GetInt32() >= 1),
                what + ": " + map + " is {id: copies} in ID order");
        }
        var pity = s.GetProperty("pity");
        Check(Names(pity).SequenceEqual(PityKindMembers) && PityKindMembers.All(k => Names(pity.GetProperty(k)).SequenceEqual(PityMembers)), what + ": the four pity states");
        Check(s.GetProperty("launchClaimed").ValueKind == JsonValueKind.True || s.GetProperty("launchClaimed").ValueKind == JsonValueKind.False, what + ": the launch flag");
        Check(A(s, "banners").All(b => Names(b).SequenceEqual(BannerMembers) && Names(b.GetProperty("curve")).SequenceEqual(CurveMembers)), what + ": the banner members");
        static bool Entry(JsonElement h)
        {
            var results = h.GetProperty("results");
            return Names(h).SequenceEqual(HistoryMembers) && (results.GetArrayLength() == 1 || results.GetArrayLength() == 10) && results.EnumerateArray().All(r => Names(r).SequenceEqual(ResultMembers));
        }
        Check(A(s, "history").All(Entry), what + ": history entries are pulls of 1 or 10 results");
    }

    void SameBanners(JsonElement snapshot, string what)
    {
        var cfg = oracleConfig!;
        var published = A(snapshot, "banners");
        bool Matches(JsonElement p, GachaBanner b)
        {
            var c = cfg.Curves[b.Kind]; var curve = p.GetProperty("curve");
            return S(p, "id") == b.Id && S(p, "name") == b.Name && S(p, "kind") == b.Kind && S(p, "ticket") == b.Ticket && I(p, "costPerPull") == cfg.CostPerPull
                && I(p, "tenPullTickets") == (b.Kind == "beginner" ? c.TenPullTickets : 10)
                && I(curve, "ssrBase") == c.SsrBase && I(curve, "softStart") == c.SoftStart && I(curve, "softStep") == c.SoftStep && I(curve, "hard") == c.Hard
                && I(curve, "srBase") == c.SrBase && I(curve, "srGuarantee") == c.SrGuarantee && I(curve, "maxPulls") == c.MaxPulls && I(curve, "selectorAt") == c.SelectorAt
                && Strings(p, "ssrPool").SequenceEqual(cfg.StandardSsrChars) && Strings(p, "srChars").SequenceEqual(cfg.SrChars) && Strings(p, "srWeapons").SequenceEqual(cfg.SrWeapons)
                && Strings(p, "rItems").SequenceEqual(cfg.RItems);
        }
        Check(published.Count == cfg.Banners.Count && published.Zip(cfg.Banners).All(p => Matches(p.First, p.Second)),
            what + ": the published banners are exactly the content's standard and beginner banners with Core's curves (maxPulls, selectorAt), pools and prices");
    }

    void SameLaunch(JsonElement pre, Result reply, string requestId, byte[] stored, string what)
    {
        var json = reply.Json; var post = json.GetProperty("snapshot");
        Check(Names(json).SequenceEqual(MutationMembers) && S(json, "requestId") == requestId && S(json, "accountId") == S(pre, "accountId") && S(json, "kind") == "launch"
            && A(json, "results").Count == 0 && N64(json, "revision") == N64(pre, "revision") + 1 && N64(post, "revision") == N64(json, "revision"),
            what + ": the agreed reply members, kind launch, no results, revision expectedRevision + 1");
        SnapshotShape(post, S(pre, "accountId"), what);
        var a = pre.GetProperty("wallet"); var b = post.GetProperty("wallet");
        Check(N64(b, "freeCrystals") == N64(a, "freeCrystals") + launchCrystals && N64(b, "paidCrystals") == N64(a, "paidCrystals")
            && I(b.GetProperty("tickets"), "standardTicket") == I(a.GetProperty("tickets"), "standardTicket") + launchTickets
            && I(b.GetProperty("tickets"), "limitedTicket") == I(a.GetProperty("tickets"), "limitedTicket") && I(b.GetProperty("tickets"), "weaponTicket") == I(a.GetProperty("tickets"), "weaponTicket")
            && N64(b, "shards") == N64(a, "shards") && N64(b, "afterglow") == N64(a, "afterglow")
            && new[] { "ownedChars", "ownedWeapons", "pity", "banners", "history" }.All(m => Raw(post, m) == Raw(pre, m))
            && !pre.GetProperty("launchClaimed").GetBoolean() && post.GetProperty("launchClaimed").GetBoolean(),
            what + ": exactly the approved " + launchCrystals + " free crystals and " + launchTickets + " standard tickets, nothing else (no beginner tickets, characters or weapons)");
        var entry = LedgerEntryOf(stored, requestId);
        Check(S(entry, "kind") == "launch" && N64(entry, "expectedRevision") == N64(pre, "revision") && N64(entry.GetProperty("grant"), "freeCrystals") == launchCrystals
            && I(entry.GetProperty("grant"), "standardTicket") == launchTickets, what + ": the private ledger records the grant at its revision");
    }

    // A pull reply against its pre-snapshot: members and revision, the private ledger entry, check-side Core over the stored draw tape (the
    // served results and the served snapshot state), the ticket/free/paid debit and the newest-first history.
    void SamePull(JsonElement pre, Result reply, JsonElement entry, string requestId, string bannerId, int count, string what)
    {
        var cfg = oracleConfig!;
        var json = reply.Json; var post = json.GetProperty("snapshot"); var results = A(json, "results");
        Check(Names(json).SequenceEqual(MutationMembers) && S(json, "requestId") == requestId && S(json, "accountId") == S(pre, "accountId") && S(json, "kind") == "pull"
            && N64(json, "revision") == N64(pre, "revision") + 1 && N64(post, "revision") == N64(json, "revision") && results.Count == count,
            what + ": the agreed reply members, kind pull, " + count + " results, revision expectedRevision + 1");
        SnapshotShape(post, S(pre, "accountId"), what);
        var draws = A(entry, "draws").Select(d => (Bound: d[0].GetInt32(), Value: d[1].GetInt32())).ToList();
        Check(S(entry, "kind") == "pull" && N64(entry, "expectedRevision") == N64(pre, "revision") && S(entry, "bannerId") == bannerId && I(entry, "count") == count
            && Raw(entry, "results") == Raw(json, "results") && draws.Count == 2 * count && draws.Where((d, i) => i % 2 == 0).All(d => d.Bound == Gacha.Bps),
            what + ": the private ledger entry holds these results and one rarity roll plus one pick per pull");
        var g = OracleState(pre);
        int at = 0;
        bool aligned = true;
        string? code = null;
        var core = new List<PullResult>();
        try
        {
            core = Gacha.Pull(cfg, g, bannerId, count, n =>
            {
                if (at >= draws.Count || draws[at].Bound != n) { aligned = false; return 0; }
                return draws[at++].Value;
            });
        }
        catch (RuleException error) { code = error.Code; }
        Check(code == null && aligned && at == draws.Count && core.Count == results.Count && core.Zip(results).All(p => SameResult(p.Second, p.First)),
            what + ": check-side Core over the stored draw tape reproduces the served results in order, duplicates included");
        Check(OracleText(g) == OracleText(OracleState(post)), what + ": Core's state after the pull is the served snapshot (tickets, crystals, shards, afterglow, copies, pity)");
        var a = pre.GetProperty("wallet"); var b = post.GetProperty("wallet"); var debit = entry.GetProperty("debit");
        long spent = N64(a, "freeCrystals") + N64(a, "paidCrystals") - N64(b, "freeCrystals") - N64(b, "paidCrystals"), fromFree = Math.Min(N64(a, "freeCrystals"), spent);
        string ticket = cfg.Banners.Single(x => x.Id == bannerId).Ticket;
        Check(spent >= 0 && N64(b, "freeCrystals") == N64(a, "freeCrystals") - fromFree && N64(b, "paidCrystals") == N64(a, "paidCrystals") - (spent - fromFree)
            && S(debit, "ticket") == ticket && I(debit, "tickets") == I(a.GetProperty("tickets"), ticket) - I(b.GetProperty("tickets"), ticket)
            && N64(debit, "freeCrystals") == fromFree && N64(debit, "paidCrystals") == spent - fromFree, what + ": tickets first, then free before paid crystals, as the ledger debit records");
        var history = A(post, "history"); var before = A(pre, "history");
        Check(history.Count == before.Count + 1 && S(history[0], "requestId") == requestId && S(history[0], "bannerId") == bannerId && Raw(history[0], "results") == Raw(json, "results")
            && history.Skip(1).Select(h => h.GetRawText()).SequenceEqual(before.Select(h => h.GetRawText())), what + ": the pull heads the newest-first history; older entries are unchanged");
        Check(Raw(post, "banners") == Raw(pre, "banners") && post.GetProperty("launchClaimed").GetBoolean() == pre.GetProperty("launchClaimed").GetBoolean(),
            what + ": the banners and the launch claim are unchanged");
    }

    static bool SameResult(JsonElement r, PullResult p) => Names(r).SequenceEqual(ResultMembers) && S(r, "rarity") == p.Rarity && S(r, "kind") == p.Kind && S(r, "id") == p.Id
        && r.GetProperty("featured").GetBoolean() == p.Featured && r.GetProperty("lost").GetBoolean() == p.Lost && I(r, "pityAt") == p.PityAt && r.GetProperty("isNew").GetBoolean() == p.IsNew
        && I(r, "resonance") == p.Resonance && I(r, "refine") == p.Refine && I(r, "shards") == p.Shards && I(r, "afterglow") == p.Afterglow;
    static JsonElement LedgerEntryOf(byte[] envelope, string requestId) =>
        BodyOf(envelope).GetProperty("recruitment").GetProperty("ledger").EnumerateArray().Single(e => S(e, "requestId") == requestId);

    // ───────── the check-side Core oracle ─────────
    // The content file's own gacha members for the active (standard and beginner) banners, read directly rather than through the server's
    // loader; SameBanners then requires the published banners to equal them. The approved launch reward is asserted, not just read.
    GachaConfig OracleConfig()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json")));
        var gacha = document.RootElement.GetProperty("gacha");
        var curves = gacha.GetProperty("curves"); var pools = gacha.GetProperty("pools"); var duplicates = gacha.GetProperty("duplicates");
        static int Member(JsonElement e, string name) => e.TryGetProperty(name, out var value) ? value.GetInt32() : 0;
        static List<string> TextList(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToList();
        var cfg = new GachaConfig
        {
            CostPerPull = gacha.GetProperty("costPerPull").GetInt32(), StandardSsrChars = TextList(pools.GetProperty("standardSsrChars")), SrChars = TextList(pools.GetProperty("srChars")),
            StandardSsrWeapons = TextList(pools.GetProperty("standardSsrWeapons")), SrWeapons = TextList(pools.GetProperty("srWeapons")), RItems = TextList(gacha.GetProperty("rItems")),
            ShardSsr = Member(duplicates, "shardSsr"), ShardSsrOverflow = Member(duplicates, "shardSsrOverflow"), ShardSr = Member(duplicates, "shardSr"),
            ShardSrOverflow = Member(duplicates, "shardSrOverflow"), AfterglowR = Member(duplicates, "afterglowR")
        };
        foreach (var banner in gacha.GetProperty("banners").EnumerateArray())
        {
            string id = banner.GetProperty("id").GetString()!, kind = banner.GetProperty("kind").GetString()!;
            if (kind != "standard" && kind != "beginner") { inactiveBanners.Add(id); continue; }
            cfg.Banners.Add(new GachaBanner { Id = id, Name = banner.GetProperty("name").GetString()!, Kind = kind, Ticket = banner.GetProperty("ticket").GetString()! });
            var c = curves.GetProperty(kind);
            cfg.Curves[kind] = new GachaCurve
            {
                SsrBase = Member(c, "ssrBase"), SoftStart = Member(c, "softStart"), SoftStep = Member(c, "softStep"), Hard = Member(c, "hard"), SrBase = Member(c, "srBase"),
                SrGuarantee = Member(c, "srGuarantee"), SelectorAt = Member(c, "selectorAt"), MaxPulls = Member(c, "maxPulls"), TenPullTickets = Member(c, "tenPullTickets")
            };
        }
        var launch = document.RootElement.GetProperty("progression").GetProperty("seasonIncome").GetProperty("oneTime").GetProperty("launch");
        launchCrystals = launch.GetProperty("crystals").GetInt64();
        launchTickets = launch.GetProperty("standardTicket").GetInt32();
        Check(Names(launch).SequenceEqual(new[] { "crystals", "standardTicket" }) && launchCrystals == 1600 && launchTickets == 10,
            "the content's launch reward is exactly the approved 1600 free crystals and 10 standard tickets");
        Check(cfg.Banners.Select(b => b.Kind).SequenceEqual(new[] { "standard", "beginner" }) && inactiveBanners.Count > 0, "the content has the standard and beginner banners and inactive limited/weapon banners");
        return cfg;
    }

    static GachaState OracleState(JsonElement snapshot)
    {
        var wallet = snapshot.GetProperty("wallet");
        var g = new GachaState { Crystals = checked(N64(wallet, "freeCrystals") + N64(wallet, "paidCrystals")), Shards = N64(wallet, "shards"), Afterglow = N64(wallet, "afterglow") };
        foreach (var p in wallet.GetProperty("tickets").EnumerateObject()) g.Tickets[p.Name] = p.Value.GetInt32();
        foreach (var p in snapshot.GetProperty("ownedChars").EnumerateObject()) g.OwnedChars[p.Name] = p.Value.GetInt32();
        foreach (var p in snapshot.GetProperty("ownedWeapons").EnumerateObject()) g.OwnedWeapons[p.Name] = p.Value.GetInt32();
        foreach (var p in snapshot.GetProperty("pity").EnumerateObject())
        {
            var s = g.Pity[p.Name]; var v = p.Value;
            s.N = I(v, "n"); s.SrN = I(v, "srN"); s.Guar = v.GetProperty("guar").GetBoolean(); s.SrGuar = v.GetProperty("srGuar").GetBoolean(); s.Total = I(v, "total");
            s.Done = v.GetProperty("done").GetBoolean(); s.GotSsr = v.GetProperty("gotSsr").GetBoolean(); s.SelectorUsed = v.GetProperty("selectorUsed").GetBoolean();
        }
        return g;
    }
    static string OracleText(GachaState g)
    {
        static string Map<T>(IEnumerable<KeyValuePair<string, T>> map, Func<T, string> text) =>
            string.Join(",", map.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + text(p.Value)));
        return "crystals " + g.Crystals.ToString(CultureInfo.InvariantCulture) + " tickets " + Map(g.Tickets, n => n.ToString(CultureInfo.InvariantCulture))
            + " shards " + g.Shards.ToString(CultureInfo.InvariantCulture) + " afterglow " + g.Afterglow.ToString(CultureInfo.InvariantCulture)
            + " chars " + Map(g.OwnedChars, n => n.ToString(CultureInfo.InvariantCulture)) + " weapons " + Map(g.OwnedWeapons, n => n.ToString(CultureInfo.InvariantCulture))
            + " pity " + Map(g.Pity, p => string.Join("/", p.N, p.SrN, p.Guar, p.SrGuar, p.Total, p.Done, p.GotSsr, p.SelectorUsed));
    }

    // ───────── privacy of recruitment replies ─────────
    // The unchanged response scan (ForbiddenKeys keeps "revision" private everywhere else) after removing exactly the two documented integer
    // members $.revision and $.snapshot.revision, plus the recruitment-private member names. Complete capture of every recruitment request.
    void RecruitmentLeakScan()
    {
        var secrets = new Secrets(tokens.Values.ToArray(), seeds.Where(s => s >= 1000000).Select(s => s.ToString(CultureInfo.InvariantCulture)).ToArray());
        int scanned = 0;
        foreach (string body in recruitCapture.Bodies)
        {
            string? leak = RecruitmentLeak(body, secrets);
            if (leak != null)
            {
                Record("recruitment response leak at " + leak, body);
                throw new CheckFailure("private data in a captured recruitment response (" + leak + "; raw in diagnostics.log)");
            }
            scanned++;
        }
        Check(scanned > 0 && scanned == recruitCapture.Started,
            "every recruitment response (" + scanned + ") was captured and carries no credential, seed, draw tape, ledger, receipt or other private member");
    }
    static string? RecruitmentLeak(string body, Secrets secrets)
    {
        if (secrets.Tokens.Any(t => body.Contains(t, StringComparison.Ordinal))) return "credential text";
        JsonNode? node;
        try { node = JsonNode.Parse(body); }
        catch (JsonException) { return "a non-JSON body"; }
        if (node is JsonObject reply)
        {
            if (!WithoutRevision(reply)) return "$.revision (not an integer)";
            if (reply["snapshot"] is JsonObject snapshot && !WithoutRevision(snapshot)) return "$.snapshot.revision (not an integer)";
        }
        string text = node?.ToJsonString() ?? "null";
        string? leak = ResponseLeak(text, secrets);
        if (leak != null) return leak;
        using var document = JsonDocument.Parse(text);
        return PrivateRecruitmentMember(document.RootElement, "$");
    }
    static bool WithoutRevision(JsonObject value)
    {
        if (!value.ContainsKey("revision")) return true;
        if (!(value["revision"] is JsonValue revision) || revision.GetValueKind() != JsonValueKind.Number || !revision.TryGetValue(out long _)) return false;
        value.Remove("revision");
        return true;
    }
    static string? PrivateRecruitmentMember(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                string at = path + "." + SafeName(property.Name);
                if (RecruitmentPrivate.Contains(property.Name)) return at;
                string? inner = PrivateRecruitmentMember(property.Value, at);
                if (inner != null) return inner;
            }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var item in element.EnumerateArray())
            {
                string? inner = PrivateRecruitmentMember(item, path + "[" + index++ + "]");
                if (inner != null) return inner;
            }
        }
        return null;
    }
    // Planted bodies only (nothing touches a server): the recruitment scan admits exactly the two documented integer revisions and still
    // catches a revision elsewhere, a draw tape, a ledger, a seed and a credential.
    void RecruitmentScanSafety()
    {
        string credential = "planted-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        string seed = (1000000000L + RandomNumberGenerator.GetInt32(int.MaxValue)).ToString(CultureInfo.InvariantCulture);
        var secrets = new Secrets(new[] { credential }, new[] { seed });
        Check(RecruitmentLeak("{\"revision\":3,\"snapshot\":{\"revision\":3,\"history\":[]}}", secrets) == null
            && RecruitmentLeak("{\"snapshot\":{\"history\":[{\"revision\":3}]}}", secrets) != null && RecruitmentLeak("{\"revision\":\"3\"}", secrets) != null
            && RecruitmentLeak("{\"snapshot\":{\"draws\":[[10000,1]]}}", secrets) != null && RecruitmentLeak("{\"ledger\":[]}", secrets) != null
            && RecruitmentLeak("{\"results\":[{\"expectedRevision\":1}]}", secrets) != null && RecruitmentLeak("{\"n\":" + seed + "}", secrets) != null
            && RecruitmentLeak("{\"code\":\"" + credential + "\"}", secrets) != null && RecruitmentLeak("{\"receipts\":{}}", secrets) != null,
            "the recruitment scan admits only $.revision and $.snapshot.revision and still catches private members, seeds and credentials");
    }

    static List<string> Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToList();
    static List<string> Strings(JsonElement element, string name) => A(element, name).Select(x => x.GetString()!).ToList();
    static long N64(JsonElement element, string name) => element.GetProperty(name).GetInt64();
    static long Free(JsonElement snapshot) => N64(snapshot.GetProperty("wallet"), "freeCrystals");
    static int TicketsOf(JsonElement snapshot) => I(snapshot.GetProperty("wallet").GetProperty("tickets"), "standardTicket");
}
