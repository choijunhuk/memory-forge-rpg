// P1-POSTGRES-TRANSACTIONAL-PRACTICE: the explicit PostgreSQL mode of the server checks (only with --postgres-file and --psql).
// Real host processes run with MemoryForge:PracticeBackend=postgres against Root's dedicated test database through Root's private
// connection file; this project loads no database driver. Every database read, tamper or lock goes through PostgresChecks, which runs
// Root's psql wrapper (already bound to that database: ordinary flags and SQL on stdin, no connection or password arguments). Accounts are
// fresh run-tagged aliases of the reference Sera/Harin/Nia test fixture with generated credentials, so no earlier row is reused, reset or
// deleted. The database password is read from the connection file only to scan every response and host log for it; it is never printed,
// and psql errors reach only the owner-only diagnostics file. Nothing injects a seed, state or expected snapshot: the stored bytea is read
// for byte-equality and oracle checks, and replaced only with deliberately tampered or previously committed bytes.
// Not covered here (left to Root's independent runs): database outage/reconnect and revoked-permission failures during requests, and a
// database without the schema marker (only a non-existent database is refused here).
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MemoryForge.Core;

/// <summary>The psql oracle over Root's wrapper: SQL on stdin, unaligned tuples out; failures record stderr privately only.</summary>
sealed class PostgresChecks
{
    static readonly Regex SafeAlias = new Regex("^[a-z][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant);
    static readonly string[] PasswordKeys = { "password", "pwd", "psw" };
    readonly string psql;
    readonly Action<string, string> record;
    readonly List<Process> holders = new List<Process>();

    public PostgresChecks(string psql, Action<string, string> record) { this.psql = psql; this.record = record; }

    /// <summary>A psql session that holds one account row's FOR UPDATE lock inside an open transaction for a bounded time.</summary>
    public sealed class LockHolder
    {
        public Process Process { get; }
        public string Token { get; }
        public Task<string> Error { get; }
        public LockHolder(Process process, string token, Task<string> error) { Process = process; Token = token; Error = error; }
    }

    ProcessStartInfo StartInfo()
    {
        var start = new ProcessStartInfo(psql) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in new[] { "-X", "-q", "-A", "-t", "-v", "ON_ERROR_STOP=1" }) start.ArgumentList.Add(argument);
        return start;
    }

    public async Task<string> Sql(string what, string sql)
    {
        using var process = Process.Start(StartInfo()) ?? throw new CheckFailure("could not start the psql oracle");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(sql);
        process.StandardInput.Close();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try { await process.WaitForExitAsync(cancel.Token); }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw new CheckFailure("the psql oracle timed out: " + what);
        }
        string output = await stdout, error = await stderr;
        if (process.ExitCode != 0)
        {
            record("psql oracle failure: " + what, error);
            throw new CheckFailure("the psql oracle failed: " + what + " (stderr in diagnostics.log)");
        }
        return output.Trim();
    }

    /// <summary>The stored envelope bytes of one alias, or null when it has no row.</summary>
    public async Task<byte[]?> Envelope(string alias)
    {
        string hex = await Sql("read a stored envelope", "SELECT encode(envelope, 'hex') FROM dev_practice.account_state WHERE account_alias = " + Literal(alias) + ";");
        return hex.Length == 0 ? null : Convert.FromHexString(hex);
    }
    public async Task<int> Rows(string alias) => int.Parse(await Sql("count an alias's rows",
        "SELECT count(*) FROM dev_practice.account_state WHERE account_alias = " + Literal(alias) + ";"), NumberStyles.None, CultureInfo.InvariantCulture);
    /// <summary>Replaces exactly one existing row's bytes (tamper cases and the restore of the host's own last committed bytes).</summary>
    public async Task Replace(string alias, byte[] bytes)
    {
        string changed = await Sql("replace a stored envelope", "WITH changed AS (UPDATE dev_practice.account_state SET envelope = decode('" + Convert.ToHexStringLower(bytes)
            + "', 'hex') WHERE account_alias = " + Literal(alias) + " RETURNING 1) SELECT count(*) FROM changed;");
        if (changed != "1") throw new CheckFailure("the psql oracle did not replace exactly one stored row");
    }

    /// <summary>BEGIN; lock the row FOR UPDATE; sleep (bounded, tagged); ROLLBACK. The session never changes data.</summary>
    public LockHolder HoldRowLock(string alias, int seconds)
    {
        string token = "mf-lock-holder-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));
        var process = Process.Start(StartInfo()) ?? throw new CheckFailure("could not start the psql lock holder");
        holders.Add(process);
        _ = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write("BEGIN;\nSELECT 1 FROM dev_practice.account_state WHERE account_alias = " + Literal(alias) + " FOR UPDATE;\nSELECT pg_sleep("
            + seconds.ToString(CultureInfo.InvariantCulture) + ") /* " + token + " */;\nROLLBACK;\n");
        process.StandardInput.Close();
        return new LockHolder(process, token, error);
    }
    /// <summary>True once the holder runs its tagged sleep, i.e. after its FOR UPDATE succeeded in the same transaction.</summary>
    public async Task<bool> Sleeping(LockHolder holder) => await Sql("lock holder state",
        "SELECT count(*) FROM pg_stat_activity WHERE state = 'active' AND pid <> pg_backend_pid() AND query LIKE '%" + holder.Token + "%';") == "1";
    public async Task<bool> Finished(LockHolder holder, TimeSpan timeout)
    {
        using var cancel = new CancellationTokenSource(timeout);
        try { await holder.Process.WaitForExitAsync(cancel.Token); }
        catch (OperationCanceledException) { return false; }
        string error = await holder.Error;
        if (holder.Process.ExitCode == 0) return true;
        record("psql lock holder failure", error);
        return false;
    }
    public void Stop(LockHolder holder) => Kill(holder.Process);
    public void StopAll() { foreach (var process in holders) Kill(process); }

    /// <summary>The connection file's password, only so responses and logs can be scanned for it. Never printed.</summary>
    public static string Password(string connectionFile)
    {
        var builder = Parse(connectionFile);
        foreach (string key in builder.Keys.Cast<string>())
            if (PasswordKeys.Contains(key.Trim(), StringComparer.OrdinalIgnoreCase)) return Convert.ToString(builder[key], CultureInfo.InvariantCulture) ?? "";
        throw new CheckFailure("the PostgreSQL connection file has no password (the host refuses such a file)");
    }
    /// <summary>The same connection string pointed at another database (for a genuine refusal before listening).</summary>
    public static string WithDatabase(string connectionFile, string database)
    {
        var builder = Parse(connectionFile);
        foreach (string key in builder.Keys.Cast<string>().ToList())
            if (key.Trim().Equals("database", StringComparison.OrdinalIgnoreCase) || key.Trim().Equals("db", StringComparison.OrdinalIgnoreCase)) builder.Remove(key);
        builder["Database"] = database;
        return builder.ConnectionString;
    }
    static DbConnectionStringBuilder Parse(string connectionFile) => new DbConnectionStringBuilder { ConnectionString = File.ReadAllText(connectionFile).Trim() };
    static string Literal(string alias) => SafeAlias.IsMatch(alias) ? "'" + alias + "'" : throw new CheckFailure("an unsafe alias reached the psql oracle");
    static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }
}

sealed partial class ServerChecks
{
    const string PostgresPasswordKey = "#postgres-password";
    static readonly string[] PostgresScenarios = { "pg-refusals", "pg-auth-input", "pg-expedition", "pg-concurrency", "pg-ownership", "pg-battle-query", "pg-row-lock", "pg-partial-mutation", "pg-corrupt" };
    PostgresChecks? oracle;
    string postgresFile = "", runTag = "";

    PostgresChecks Oracle => oracle ?? throw new CheckFailure("the PostgreSQL oracle is not configured");
    string Alias(string role) => runTag + "-" + role;
    string Credential(string role) => tokens[Alias(role)];

    public async Task RunPostgres(string connectionFile, string psql)
    {
        DiagnosticSafety();
        Check(File.Exists(Path.Combine(root, "data", "game_data.json")), "the repository root has data/game_data.json");
        postgresFile = connectionFile;
        Check(File.Exists(postgresFile) && File.Exists(psql), "the PostgreSQL connection file and the psql oracle exist");
        string password = PostgresChecks.Password(postgresFile);
        Check(password.Length >= 8, "the test database password is long enough to scan responses and logs for");
        tokens[PostgresPasswordKey] = password;
        oracle = new PostgresChecks(psql, Record);
        Check(await Oracle.Sql("schema marker", "SELECT purpose || '/' || format FROM dev_practice.schema_marker;") == "development-practice/mf-dev-practice-pg-1",
            "the dedicated test database carries the development schema marker");
        serverDll = await BuildServer();
        runTag = "t" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var aliases = new[] { Alias("alpha"), Alias("beta"), Alias("gamma") };
        foreach (string alias in aliases)
        {
            tokens[alias] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Check(await Oracle.Rows(alias) == 0, "a fresh run-tagged alias has no stored row");
        }
        fixture = Path.Combine(temp, "fixture.json");
        WritePrivate(fixture, FixtureJson(null, aliases));
        await PostgresRefusals();
        var host = await StartPostgres("postgres host", root);
        await PostgresAuthAndInput(host);
        host = await PostgresExpedition(host);
        await PostgresConcurrency(host);
        await PostgresOwnership(host);
        await PostgresBattleQuery(host);
        await PostgresRowLock(host);
        host.Kill();
        await PostgresPartialMutation();
        await PostgresCorruptRows();
        LeakScan(PostgresScenarios);
    }

    Dictionary<string, string> PostgresConfig(string contentRoot, string? connectionFile = null) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MemoryForge:ContentRoot"] = contentRoot, ["MemoryForge:PracticeFixture"] = fixture, ["MemoryForge:PracticeBackend"] = "postgres",
        ["MemoryForge:PracticePostgresFile"] = connectionFile ?? postgresFile, ["MemoryForge:PracticePort"] = "0"
    };
    async Task<ServerProcess> StartPostgres(string name, string contentRoot)
    {
        var server = Launch(name, PostgresConfig(contentRoot));
        if (!await server.WaitListening(TimeSpan.FromSeconds(120)))
        {
            Record(name + " output", server.Output);
            throw new CheckFailure(name + " did not start listening (output in diagnostics.log)");
        }
        Check(server.ListeningSeen, name + " listens on 127.0.0.1 only");
        return server;
    }
    async Task<byte[]> Stored(string alias) => await Oracle.Envelope(alias) ?? throw new CheckFailure("a fixture alias that committed has no stored row");
    static JsonElement BodyOf(byte[] envelope)
    {
        using var outer = JsonDocument.Parse(envelope);
        using var inner = JsonDocument.Parse(outer.RootElement.GetProperty("body").GetString()!);
        return inner.RootElement.Clone();
    }

    // ───────── startup refusals (none may listen) ─────────
    async Task PostgresRefusals()
    {
        string bad = Path.Combine(temp, "postgres-refusal.conn");
        var mixed = PostgresConfig(root); mixed["MemoryForge:PracticeStore"] = Path.Combine(temp, "postgres-mixed-store");
        await Refused("the postgres backend together with a file store", mixed);
        var withoutFile = PostgresConfig(root); withoutFile.Remove("MemoryForge:PracticePostgresFile");
        await Refused("the postgres backend without a connection file", withoutFile);
        var fileBackend = Config(root, Path.Combine(temp, "postgres-file-backend-store")); fileBackend["MemoryForge:PracticePostgresFile"] = postgresFile;
        await Refused("a connection file without the postgres backend", fileBackend);
        var unknown = PostgresConfig(root); unknown["MemoryForge:PracticeBackend"] = "sqlite";
        await Refused("an unknown backend name", unknown);
        // Placeholder credentials only: each file is refused while it is validated, before any connection is attempted.
        foreach (var (what, text) in new[]
        {
            ("a non-loopback database host", "Host=192.0.2.10;Database=practice;Username=practice;Password=placeholder-not-a-secret"),
            ("several database hosts", "Host=127.0.0.1,127.0.0.2;Database=practice;Username=practice;Password=placeholder-not-a-secret"),
            ("a database host name instead of a loopback IP", "Host=localhost;Database=practice;Username=practice;Password=placeholder-not-a-secret"),
            ("a connection file without a password", "Host=127.0.0.1;Database=practice;Username=practice"),
            ("an unsupported connection option", "Host=127.0.0.1;Database=practice;Username=practice;Password=placeholder-not-a-secret;Include Error Detail=true"),
            ("session options in the connection file", "Host=127.0.0.1;Database=practice;Username=practice;Password=placeholder-not-a-secret;Options=-c synchronous_commit=off"),
        })
        {
            WritePrivate(bad, text);
            await Refused(what, PostgresConfig(root, bad));
        }
        WritePrivate(bad, "Host=127.0.0.1;Database=practice;Username=practice;Password=placeholder-not-a-secret");
        File.SetUnixFileMode(bad, Owner | UnixFileMode.GroupRead);
        await Refused("a group-readable connection file", PostgresConfig(root, bad));
        // A genuine database refusal before listening: the real credentials against a database that does not exist (file 0600 in the
        // owner-only temp directory, deleted with it on success).
        WritePrivate(bad, PostgresChecks.WithDatabase(postgresFile, "mf_absent_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))));
        await Refused("a database that does not exist", PostgresConfig(root, bad));
        completed.Add("pg-refusals");
    }

    // ───────── auth and strict input: nothing may create a row ─────────
    async Task PostgresAuthAndInput(ServerProcess server)
    {
        var api = Client(server);
        string alias = Alias("alpha"), token = Credential("alpha"), none = "pr-" + new string('0', 32);
        Status(await api.Get(AccountPath, null), 401, "UNAUTHORIZED", "no credential (postgres)");
        Status(await api.Get(AccountPath, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24))), 401, "UNAUTHORIZED", "an unknown credential (postgres)");
        Status(await api.Post(RunsPath, null, Obj(("requestId", Id()))), 401, "UNAUTHORIZED", "an anonymous create (postgres)");
        var account = await api.Get(AccountPath, token);
        Status(account, 200, null, "the account read (postgres)");
        Check(S(account.Json, "accountId") == alias && I(account.Json.GetProperty("memory"), "version") == 0 && account.Json.GetProperty("run").ValueKind == JsonValueKind.Null,
            "a new account has memory version 0 and no run (postgres)");
        Status(await api.Post(RunsPath, token, Obj(("requestId", Id()), ("seed", 7))), 400, "UNKNOWN_FIELD", "a client seed (postgres)");
        Status(await api.Post(RunsPath, token, "{\"requestId\":\"" + Id() + "\",\"state\":{}}"), 400, "UNKNOWN_FIELD", "a client state (postgres)");
        Status(await api.Post(RunsPath, token, "not json"), 400, "INVALID_JSON", "malformed JSON (postgres)");
        Status(await api.Get(RunsPath + "/" + none, token), 404, "RUN_NOT_FOUND", "an unknown run (postgres)");
        Status(await Command(api, token, none, Id(), 0, "end", null), 404, "RUN_NOT_FOUND", "a command on no run (postgres)");
        Check(await Oracle.Rows(alias) == 0, "pure reads, refused input and a refused first mutation leave no row (the placeholder insert rolled back)");
        completed.Add("pg-auth-input");
    }

    // ───────── alpha: create → enter → play → SIGKILL → retries → defeat → v2 save → earned card → SIGKILL → next departure ─────────
    async Task<ServerProcess> PostgresExpedition(ServerProcess server)
    {
        var api = Client(server);
        string alias = Alias("alpha"), token = Credential("alpha");
        var kinds = new List<string>();
        string createId = Id();
        var created = await api.Post(RunsPath, token, Obj(("requestId", createId)));
        Status(created, 200, null, "a create commits in PostgreSQL");
        string runId = S(created.Json, "runId");
        var state = created.Json.GetProperty("state");
        byte[] committed = await Stored(alias);
        var body = BodyOf(committed);
        seeds.Add(body.GetProperty("run").GetProperty("seed").GetInt64());
        Check(S(body, "accountId") == alias && body.GetProperty("revision").GetInt64() == 1 && body.GetProperty("receipts").TryGetProperty(createId, out _),
            "the stored bytea is this alias's committed revision 1 with the create's receipt");
        Same((await api.Post(RunsPath, token, Obj(("requestId", createId)))).Body, created.Body, "a retried create returns the original bytes (postgres)");
        Status(await api.Post(RunsPath, token, Obj(("requestId", createId), ("useMemory", false))), 409, "IDEMPOTENCY_CONFLICT", "the create requestId with another payload (postgres)");
        Status(await api.Post(RunsPath, token, Obj(("requestId", Id()))), 409, "RUN_ACTIVE", "a second run while one is active (postgres)");
        Same(Raw((await api.Get(RunsPath + "/" + runId, token)).Json, "state"), Raw(created.Json, "state"), "the read view equals the created view (postgres)");
        Status(await Preview(api, token, runId), 422, "WRONG_PHASE", "a preview outside a terminal save (postgres)");
        string node = A(state, "available")[0].GetString()!;
        Status(await Command(api, token, runId, Id(), 3, "enter", new JsonObject { ["nodeId"] = node }), 409, "STALE_VERSION", "a stale expectedVersion (postgres)");
        Status(await Command(api, token, runId, Id(), 0, "enter", new JsonObject { ["nodeId"] = node, ["hp"] = 999 }), 400, "UNKNOWN_FIELD", "an extra command argument (postgres)");
        Same(await Stored(alias), committed, "reads, conflicts and rejections left the stored bytea byte-identical");

        string enterId = Id();
        var entered = await Command(api, token, runId, enterId, 0, "enter", new JsonObject { ["nodeId"] = node });
        Status(entered, 200, null, "enter the first node (postgres)");
        kinds.Add("enter");
        state = entered.Json.GetProperty("state");
        Same((await Command(api, token, runId, enterId, 0, "enter", new JsonObject { ["nodeId"] = node })).Body, entered.Body,
            "the duplicate receipt wins before the (now stale) version check (postgres)");
        Status(await Command(api, token, runId, enterId, 0, "enter", new JsonObject { ["nodeId"] = A(created.Json.GetProperty("state"), "available")[1].GetString() }),
            409, "IDEMPOTENCY_CONFLICT", "the enter requestId with another node (postgres)");
        committed = await Stored(alias);
        string reused = Id();
        Status(await Command(api, token, runId, reused, 1, "play", new JsonObject { ["uid"] = "c9999", ["mode"] = "now" }), 422, "NOT_IN_HAND", "a card that is not in hand (postgres)");
        Same(await Stored(alias), committed, "the rejected play changed no stored byte");
        var first = Choose(state.GetProperty("battle"), new HashSet<string>(), 1) ?? ("end", new JsonObject(), "end");
        var played = await Command(api, token, runId, reused, 1, first.Kind, first.Args);
        Status(played, 200, null, "the rejected requestId is not consumed: the same ID commits a valid " + first.Kind + " (postgres)");
        kinds.Add(first.Kind);
        state = played.Json.GetProperty("state");

        // SIGKILL and a fresh process: the committed row alone carries state, receipts and the run version.
        server.Kill();
        server = await StartPostgres("postgres host after a crash", root);
        api = Client(server);
        Same(Raw((await api.Get(RunsPath + "/" + runId, token)).Json, "state"), Raw(played.Json, "state"), "a fresh process serves the last committed view from PostgreSQL");
        Same((await Command(api, token, runId, reused, 1, first.Kind, first.Args)).Body, played.Body, "a retry after the crash returns the original bytes (postgres)");
        Same((await api.Post(RunsPath, token, Obj(("requestId", createId)))).Body, created.Body, "the create receipt survives the crash (postgres)");
        Status(await Command(api, token, runId, reused, 2, first.Kind, first.Args), 409, "IDEMPOTENCY_CONFLICT", "another payload under a committed ID after the crash (postgres)");

        var outcome = await PlayToDefeat(api, token, runId, state, kinds);
        state = outcome.State;
        Check(S(state, "phase") == "DEFEAT" && S(state.GetProperty("pending"), "type") == "save", "the expedition reached a real defeat with a save pending (postgres)");
        int version = I(state, "version");
        committed = await Stored(alias);
        Status(await Preview(api, token, runId), 200, null, "the empty-selection preview (postgres)");
        Status(await Commit(api, token, runId, Id(), version, 7), 422, "VERSION_CONFLICT", "a save against another memory version (postgres)");
        Status(await Commit(api, token, runId, Id(), version - 1, 0), 409, "STALE_VERSION", "a save against an old run version (postgres)");
        Same(await Stored(alias), committed, "previews and rejected saves left the stored bytea byte-identical");
        (string Cid, string Id)? add = outcome.TakenKey == null ? null : AddChange(state, outcome.TakenKey);
        Check(outcome.TakenKey == null || add != null, "the taken reward card is offered as an add change (postgres)");
        string saveId = Id();
        string[] selected = add == null ? Array.Empty<string>() : new[] { add.Value.Id };
        var saved = await Commit(api, token, runId, saveId, version, 0, selected);
        Status(saved, 200, null, "the v2 terminal save commits in PostgreSQL");
        kinds.Add("memoryCommit");
        alphaDone = await Stored(alias);
        var stored = BodyOf(alphaDone);
        var memory = stored.GetProperty("memory");
        Check(I(memory, "version") == 1 && memory.GetProperty("history").GetArrayLength() == 1 && memory.GetProperty("receipts").TryGetProperty(saveId, out _)
            && stored.GetProperty("receipts").TryGetProperty(saveId, out _)
            && stored.GetProperty("run").GetProperty("journal").EnumerateArray().Select(e => S(e, "kind")).SequenceEqual(kinds),
            "one committed row holds the saved memory, its history, both receipts and exactly the committed journal");
        Same((await Commit(api, token, runId, saveId, version, 0, selected)).Body, saved.Body, "a retried save returns the original bytes (postgres)");

        // A defeat that won no battle saves nothing new; depart again (legitimate play only, bounded) until an earned card is saved.
        (string RunId, string SaveId, int Version, int MemoryVersion, string[] Ids, string Body, string Cid, string Key)? card = add == null || outcome.TakenKey == null
            ? null : (runId, saveId, version, 0, selected, saved.Body, add.Value.Cid, outcome.TakenKey!);
        int memoryVersion = 1;
        for (int attempt = 0; card == null && attempt < 3; attempt++)
        {
            var again = await api.Post(RunsPath, token, Obj(("requestId", Id())));
            Status(again, 200, null, "a new departure after a save (postgres)");
            string next = S(again.Json, "runId");
            seeds.Add(BodyOf(await Stored(alias)).GetProperty("run").GetProperty("seed").GetInt64());
            var later = await PlayToDefeat(api, token, next, again.Json.GetProperty("state"), new List<string>());
            var change = later.TakenKey == null ? null : AddChange(later.State, later.TakenKey);
            string[] ids = change == null ? Array.Empty<string>() : new[] { change.Value.Id };
            string commitId = Id();
            int at = I(later.State, "version");
            var commit = await Commit(api, token, next, commitId, at, memoryVersion, ids);
            Status(commit, 200, null, "the save of a later departure (postgres)");
            if (change != null && later.TakenKey != null) card = (next, commitId, at, memoryVersion, ids, commit.Body, change.Value.Cid, later.TakenKey!);
            memoryVersion++;
        }
        if (card == null) throw new CheckFailure("within four departures the check bot did not win a first battle to save a reward card (a bounded check-bot limit, not an observed server result)");
        var earned = card.Value;

        // The nonempty earned save must survive an actual crash: a new process serves the saved deck and that save's original bytes.
        server.Kill();
        server = await StartPostgres("postgres host after the card save", root);
        api = Client(server);
        var account = await api.Get(AccountPath, token);
        Status(account, 200, null, "the account after the card-save crash (postgres)");
        Check(I(account.Json.GetProperty("memory"), "version") == memoryVersion
            && Keys(account.Json.GetProperty("memory").GetProperty("memory").GetProperty(earned.Cid)).Contains(earned.Key), "a new process serves the saved earned card from PostgreSQL");
        Same((await Commit(api, token, earned.RunId, earned.SaveId, earned.Version, earned.MemoryVersion, earned.Ids)).Body, earned.Body,
            "the earned card save's receipt returns its original bytes after the crash (postgres)");
        var departure = await api.Post(RunsPath, token, Obj(("requestId", Id())));
        Status(departure, 200, null, "the next departure (postgres)");
        alphaRunId = S(departure.Json, "runId");
        seeds.Add(BodyOf(await Stored(alias)).GetProperty("run").GetProperty("seed").GetInt64());
        Check(I(departure.Json, "memoryVersion") == memoryVersion
            && A(departure.Json.GetProperty("state"), "deck").Any(c => c.TryGetProperty("key", out var key) && key.GetString() == earned.Key),
            "the next departure starts from the saved memory version and deck (postgres)");
        Status(await api.Post(RunsPath, token, Obj(("requestId", Id()))), 409, "RUN_ACTIVE", "one active run per account (postgres)");
        completed.Add("pg-expedition");
        return server;
    }

    // ───────── beta: duplicate and stale races in one process and across two processes on one database ─────────
    async Task PostgresConcurrency(ServerProcess server)
    {
        var api = Client(server);
        string alias = Alias("beta"), token = Credential("beta");
        var created = await api.Post(RunsPath, token, Obj(("requestId", Id())));
        Status(created, 200, null, "beta creates a run (postgres)");
        string runId = S(created.Json, "runId");
        seeds.Add(BodyOf(await Stored(alias)).GetProperty("run").GetProperty("seed").GetInt64());
        Status(await Command(api, token, runId, Id(), 0, "enter", new JsonObject { ["nodeId"] = A(created.Json.GetProperty("state"), "available")[0].GetString() }),
            200, null, "beta enters a battle (postgres)");
        string same = Id();
        var duplicates = await Burst(16, _ => Command(api, token, runId, same, 1, "end", null));
        Check(duplicates.All(r => r.Status == 200 && r.Body == duplicates[0].Body), "16 concurrent copies of one request return the one committed reply (postgres)");
        var stale = await Burst(16, _ => Command(api, token, runId, Id(), 2, "end", null));
        Check(stale.Count(r => r.Status == 200) == 1 && stale.Count(r => r.Status == 409 && r.Code == "STALE_VERSION") == 15,
            "16 concurrent different requests on one version: exactly one commits (postgres)");
        var second = await StartPostgres("second postgres host", root);
        var other = Client(second);
        var crossStale = await Burst(16, i => Command(i % 2 == 0 ? api : other, token, runId, Id(), 3, "end", null));
        Check(crossStale.Count(r => r.Status == 200) == 1 && crossStale.Count(r => r.Status == 409 && r.Code == "STALE_VERSION") == 15,
            "two host processes racing one version through the row lock: exactly one commits");
        string crossSame = Id();
        var crossDuplicates = await Burst(16, i => Command(i % 2 == 0 ? api : other, token, runId, crossSame, 4, "end", null));
        Check(crossDuplicates.All(r => r.Status == 200 && r.Body == crossDuplicates[0].Body), "one new request sent through two processes commits once and replies identically");
        var body = BodyOf(await Stored(alias));
        Check(body.GetProperty("revision").GetInt64() == 6 && body.GetProperty("receipts").EnumerateObject().Count() == 6
            && body.GetProperty("run").GetProperty("journal").EnumerateArray().Select(e => S(e, "kind")).SequenceEqual(new[] { "enter", "end", "end", "end", "end" }),
            "the one stored envelope holds revision 6, six receipts and exactly one journal entry per committed command");
        var viewA = await api.Get(RunsPath + "/" + runId, token);
        var viewB = await other.Get(RunsPath + "/" + runId, token);
        Check(I(viewA.Json, "version") == 5 && Raw(viewA.Json, "state") == Raw(viewB.Json, "state"), "both processes serve the same committed version 5 (postgres)");
        second.Kill();
        completed.Add("pg-concurrency");
    }

    // ───────── ownership: another account's run does not exist, and a refused first mutation leaves no row ─────────
    async Task PostgresOwnership(ServerProcess server)
    {
        var api = Client(server);
        string alpha = Alias("alpha"), beta = Credential("beta"), gamma = Credential("gamma");
        byte[] before = await Stored(alpha);
        Status(await api.Get(RunsPath + "/" + alphaRunId, beta), 404, "RUN_NOT_FOUND", "beta reading alpha's run (postgres)");
        Status(await Command(api, beta, alphaRunId, Id(), 0, "enter", new JsonObject { ["nodeId"] = "z1f0n0" }), 404, "RUN_NOT_FOUND", "beta commanding alpha's run (postgres)");
        Status(await Preview(api, beta, alphaRunId), 404, "RUN_NOT_FOUND", "beta previewing alpha's save (postgres)");
        Status(await Commit(api, beta, alphaRunId, Id(), 0, 0), 404, "RUN_NOT_FOUND", "beta saving alpha's run (postgres)");
        Status(await Command(api, gamma, alphaRunId, Id(), 0, "enter", new JsonObject { ["nodeId"] = "z1f0n0" }), 404, "RUN_NOT_FOUND", "gamma commanding alpha's run (postgres)");
        Same(await Stored(alpha), before, "other accounts changed nothing of alpha's stored row");
        Check(await Oracle.Rows(Alias("gamma")) == 0, "gamma's refused first mutation left no row (the placeholder insert rolled back)");
        Status(await api.Get(RunsPath + "/" + alphaRunId, Credential("alpha")), 200, null, "alpha still reads its own run (postgres)");
        completed.Add("pg-ownership");
    }

    // ───────── the read-only battle query: the same backend-neutral scenario as the file suite, on the stored rows ─────────
    // alpha's current departure (version 0 on the map) and its committed bytea; gamma has no row yet (PostgresOwnership checked it), so
    // every refused query must keep it rowless. Later scenarios use beta, gamma and alpha's latest stored bytes, not alpha's map state.
    async Task PostgresBattleQuery(ServerProcess server)
    {
        string alpha = Alias("alpha"), gamma = Alias("gamma");
        await BattleQueryScenario(Client(server), Credential("alpha"), Credential("beta"), Credential("gamma"), () => Stored(alpha),
            async () => await Oracle.Rows(gamma) == 0, " (postgres)");
        completed.Add("pg-battle-query");
    }

    // ───────── a real per-account row-lock contention: bounded wait, nothing committed, then the same requestId commits once ─────────
    async Task PostgresRowLock(ServerProcess server)
    {
        var api = Client(server);
        string alias = Alias("beta"), token = Credential("beta");
        string runId = S(BodyOf(await Stored(alias)).GetProperty("run"), "runId");
        int version = await Version(api, token, runId);
        byte[] before = await Stored(alias);
        var holder = Oracle.HoldRowLock(alias, 15);
        try
        {
            bool sleeping = false;
            var waited = Stopwatch.StartNew();
            while (!sleeping && waited.Elapsed < TimeSpan.FromSeconds(10))
            {
                sleeping = await Oracle.Sleeping(holder);
                if (!sleeping) await Task.Delay(100);
            }
            Check(sleeping, "a psql session holds the account row lock inside its open transaction");
            string id = Id();
            var timer = Stopwatch.StartNew();
            Status(await Command(api, token, runId, id, version, "end", null), 503, "ACCOUNT_BUSY", "a mutation that cannot take the row lock in time (postgres)");
            Check(timer.Elapsed < TimeSpan.FromSeconds(13), "the row-lock wait is bounded by lock_timeout, not by the other transaction");
            var read = await api.Get(RunsPath + "/" + runId, token);
            Status(read, 200, null, "a read is not blocked by the row lock (postgres)");
            Same(I(read.Json, "version"), version, "the read serves the last committed version (postgres)");
            Same(await Stored(alias), before, "the timed-out mutation committed nothing");
            Check(await Oracle.Finished(holder, TimeSpan.FromSeconds(30)), "the lock holder ended its transaction within its bound");
            var retried = await Command(api, token, runId, id, version, "end", null);
            Status(retried, 200, null, "the same requestId commits once the row is free (postgres)");
            Same(I(retried.Json, "version"), version + 1, "the retry committed exactly one command (postgres)");
        }
        finally { Oracle.Stop(holder); }
        completed.Add("pg-row-lock");
    }

    // ───────── gamma: Core's genuine partial Enter (test-local unknown encounter enemy) commits nothing ─────────
    async Task PostgresPartialMutation()
    {
        string faultRoot = Path.Combine(temp, "postgres-fault-content");
        Directory.CreateDirectory(Path.Combine(faultRoot, "data"));
        var content = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "data", "game_data.json")))!;
        content["encounters"]!["1"]!["normal"] = new JsonArray(new JsonArray("test_missing_enemy"));
        File.WriteAllText(Path.Combine(faultRoot, "data", "game_data.json"), content.ToJsonString());
        string alias = Alias("gamma"), token = Credential("gamma");
        var fault = await StartPostgres("postgres fault-content host", faultRoot);
        var api = Client(fault);
        var created = await api.Post(RunsPath, token, Obj(("requestId", Id())));
        Status(created, 200, null, "gamma creates a run on the faulty content (postgres)");
        string runId = S(created.Json, "runId");
        var available = A(created.Json.GetProperty("state"), "available");
        string node = available[0].GetString()!;
        byte[] before = await Stored(alias);
        var run = BodyOf(before).GetProperty("run");
        seeds.Add(run.GetProperty("seed").GetInt64());
        string id = Id();
        Status(await Command(api, token, runId, id, 0, "enter", new JsonObject { ["nodeId"] = node }), 422, "UNKNOWN_ENEMY", "Core refuses the faulty encounter (postgres)");
        Same(await Stored(alias), before, "the failed partial Enter committed no stored byte");
        Same(Raw((await api.Get(RunsPath + "/" + runId, token)).Json, "state"), Raw(created.Json, "state"), "the served run is the pre-failure run (postgres)");
        Status(await Command(api, token, runId, id, 0, "enter", new JsonObject { ["nodeId"] = node }), 422, "UNKNOWN_ENEMY", "the same failed request runs again: no receipt (postgres)");
        Status(await Command(api, token, runId, id, 0, "enter", new JsonObject { ["nodeId"] = available[1].GetString() }), 422, "UNKNOWN_ENEMY",
            "the failed requestId is not consumed by a conflict (postgres)");
        Same(await Stored(alias), before, "repeated failures still committed nothing");

        // Check-side probe: the same Core call on the same stored inputs really moved position and drew RNG before refusing.
        var setup = new RunSetup
        {
            Seed = run.GetProperty("seed").GetInt64(), Length = "short", MemoryPolicy = "v2", RunId = runId, MemoryVersion = 0L,
            Memory = MemoryV2DataLoader.Graph(run.GetProperty("startMemory")), Difficulty = new RunDifficulty { Hp = 100, Damage = 100 }
        };
        foreach (string cid in Party) setup.Party.Add(new RunPartyEntry { CharId = cid, Res = 0 });
        var core = Run.Create(RunDataLoader.Load(faultRoot), setup);
        var start = core.State;
        string? code = null;
        try { core.Enter(node); }
        catch (RuleException error) { code = error.Code; }
        var damaged = core.State;
        Check(code == "UNKNOWN_ENEMY" && damaged.Position == node && !damaged.Rng.SequenceEqual(start.Rng) && damaged.Phase == "MAP",
            "Core's own Enter changed position and RNG before refusing, so the PostgreSQL rollback was of a genuine partial mutation");
        fault.Kill();

        var real = await StartPostgres("postgres real-content host on the faulty-content row", root);
        var realApi = Client(real);
        Status(await realApi.Get(AccountPath, token), 409, "STORE_CONTENT_MISMATCH", "a row bound to other content (postgres)");
        Status(await realApi.Post(RunsPath, token, Obj(("requestId", Id()))), 409, "STORE_CONTENT_MISMATCH", "a mutation of a row bound to other content (postgres)");
        Same(await Stored(alias), before, "the mismatched row is preserved, not reset");
        real.Kill();
        completed.Add("pg-partial-mutation");
    }

    // ───────── tampered or unknown rows are refused and left untouched; the host's own last bytes are then restored ─────────
    async Task PostgresCorruptRows()
    {
        string alias = Alias("alpha"), token = Credential("alpha");
        var server = await StartPostgres("postgres corrupt-row host", root);
        var api = Client(server);
        byte[] latest = await Stored(alias);
        Status(await api.Get(AccountPath, token), 200, null, "the intact stored row loads before tampering (postgres)");
        string firstNode = S(BodyOf(alphaDone).GetProperty("run").GetProperty("journal")[0].GetProperty("args"), "nodeId");
        string otherNode = firstNode == "z1f0n0" ? "z1f0n1" : "z1f0n0";
        var cases = new (string What, byte[] Bytes, int Status, string Code)[]
        {
            ("garbage bytes", System.Text.Encoding.UTF8.GetBytes("{not json"), 500, "STORE_CORRUPT"),
            ("a body edit without its digest", Tamper(alphaDone, body => body["revision"] = body["revision"]!.GetValue<long>() + 1, false), 500, "STORE_CORRUPT"),
            ("a re-digested journal edit", Tamper(alphaDone, body => body["run"]!["journal"]![0]!["args"]!["nodeId"] = otherNode, true), 500, "STORE_REPLAY_MISMATCH"),
            ("a re-digested created-state edit", Tamper(alphaDone, body => body["run"]!["createdSha256"] = new string('0', 64), true), 500, "STORE_REPLAY_MISMATCH"),
            ("an unknown schema", Outer(alphaDone, outer => outer["schema"] = "mf-dev-practice-account-999"), 409, "STORE_UNSUPPORTED"),
            ("other content", Tamper(alphaDone, body => body["binding"]!["contentSha256"] = new string('0', 64), true), 409, "STORE_CONTENT_MISMATCH"),
            ("another Core build", Tamper(alphaDone, body => body["binding"]!["coreModule"] = new string('0', 32), true), 409, "STORE_IMPLEMENTATION_MISMATCH"),
            ("another fixture", Tamper(alphaDone, body => body["binding"]!["fixtureSha256"] = new string('0', 64), true), 409, "STORE_FIXTURE_MISMATCH"),
        };
        try
        {
            foreach (var item in cases)
            {
                await Oracle.Replace(alias, item.Bytes);
                Status(await api.Get(AccountPath, token), item.Status, item.Code, item.What + ": account read (postgres)");
                Status(await api.Post(RunsPath, token, Obj(("requestId", Id()))), item.Status, item.Code, item.What + ": create (postgres)");
                Status(await Command(api, token, alphaRunId, Id(), 0, "end", null), item.Status, item.Code, item.What + ": command (postgres)");
                Same(await Stored(alias), item.Bytes, item.What + ": the refused row is preserved byte for byte");
            }
        }
        finally { await Oracle.Replace(alias, latest); }
        Status(await api.Get(AccountPath, token), 200, null, "restoring the host's last committed bytes serves the account again (postgres)");
        server.Kill();
        completed.Add("pg-corrupt");
    }
}
