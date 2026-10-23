// P1-LOCAL-AUTHORITATIVE-PERSISTENCE: process/HTTP checks of server/MemoryForge.Server. Every scenario launches the real host as a child
// process (built once with dotnet build, or --server-dll) with a test-only private fixture, random credentials and stores under a fresh
// owner-only temp directory, sends real HTTP requests, kills (SIGKILL) and restarts processes, and runs two hosts on one store for the
// OS lock. The private envelope is read only as a check-side oracle: byte equality (nothing changed), seeds for leak scans, journal
// kinds, and an independent check-side Core replay; nothing is ever injected into the server and no Core behaviour is mocked. A failed
// assertion stops the run with exit code 1. Console output carries only check names, statuses, safe codes and length/digest
// fingerprints: raw responses, host/build output and exception text go only to diagnostics.log (0600) in the owner-only temp directory,
// and known credentials/seeds are additionally redacted from every console line. Usage:
//   dotnet run --project server/MemoryForge.ServerChecks -- <repoRoot> [--server-dll <built MemoryForge.Server.dll>]
//   dotnet run --project server/MemoryForge.ServerChecks -- <repoRoot> --postgres-file <private connection file> --psql <oracle wrapper>
// The default runs the file-backend suite only (the PostgreSQL mode is then NOT_RUN). The PostgreSQL mode (PostgresChecks.cs) runs only
// when both PostgreSQL arguments are given, and then the file-backend suite is NOT_RUN in that invocation. Both modes end with the
// recruitment scenario (RecruitmentChecks.cs) on fresh accounts, then the read-only --verify-store scenario (AccountStoreVerifierChecks.cs)
// over the stores the suite committed.
using System.Net;
using System.Net.Sockets;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MemoryForge.Core;

[assembly: System.Runtime.Versioning.UnsupportedOSPlatform("windows")]

static class Program
{
    const string Usage = "usage: dotnet run --project server/MemoryForge.ServerChecks -- <repoRoot> [--server-dll <MemoryForge.Server.dll>] "
        + "[--postgres-file <private connection file> --psql <oracle wrapper>]";

    static async Task<int> Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine(Usage); return 2; }
        string? dll = null, postgresFile = null, psql = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--server-dll" && i + 1 < args.Length) dll = Path.GetFullPath(args[++i]);
            else if (args[i] == "--postgres-file" && i + 1 < args.Length) postgresFile = Path.GetFullPath(args[++i]);
            else if (args[i] == "--psql" && i + 1 < args.Length) psql = Path.GetFullPath(args[++i]);
            else { Console.Error.WriteLine(Usage); return 2; }
        }
        // The PostgreSQL mode needs both of its arguments; one alone is a usage error, never a silently skipped mode.
        if ((postgresFile == null) != (psql == null)) { Console.Error.WriteLine(Usage); return 2; }
        if (OperatingSystem.IsWindows()) { Console.Error.WriteLine("These checks need POSIX permissions and file locks (macOS/Linux)."); return 2; }
        var checks = new ServerChecks(Path.GetFullPath(args[0]), dll);
        bool passed = false;
        try
        {
            if (postgresFile != null && psql != null)
            {
                await checks.RunPostgres(postgresFile, psql);
                await checks.RunPostgresRecruitment();
                await checks.RunPostgresVerifyStore();
                passed = true;
                Console.WriteLine("SERVER CHECKS PASS [postgres]: " + checks.Passed + " assertions");
                Console.WriteLine("File-backend suite: NOT_RUN in this invocation (run without the PostgreSQL arguments)");
                return 0;
            }
            await checks.RunAll();
            passed = true;
            Console.WriteLine("SERVER CHECKS PASS: " + checks.Passed + " assertions");
            Console.WriteLine("PostgreSQL mode: NOT_RUN in this invocation (select it with --postgres-file and --psql)");
            return 0;
        }
        catch (CheckFailure failure) { Console.Error.WriteLine(checks.Redact("SERVER CHECKS FAIL after " + checks.Passed + " assertions: " + failure.Message)); }
        catch (Exception error)
        {
            checks.Record("unexpected exception", error.ToString());
            Console.Error.WriteLine(checks.Redact("SERVER CHECKS FAIL after " + checks.Passed + " assertions: " + ServerChecks.Describe(error)));
        }
        finally { checks.Finish(passed); }
        return 1;
    }
}

sealed class CheckFailure : Exception
{
    public CheckFailure(string message) : base(message) { }
}

sealed class Result
{
    JsonElement? json;
    public int Status { get; }
    public string Body { get; }
    public Result(int status, string body) { Status = status; Body = body; }
    public JsonElement Json
    {
        get
        {
            if (json == null) { using var document = JsonDocument.Parse(Body); json = document.RootElement.Clone(); }
            return json.Value;
        }
    }
    /// <summary>The error code of a JSON object body, or null (also for a non-JSON body: diagnostics must not throw).</summary>
    public string? Code
    {
        get
        {
            try { return Json.ValueKind == JsonValueKind.Object && Json.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null; }
            catch (JsonException) { return null; }
        }
    }
}

/// <summary>Every request started and every response body captured, for the final complete-capture leak scan.</summary>
sealed class ResponseCapture
{
    int started;
    public ConcurrentQueue<string> Bodies { get; } = new ConcurrentQueue<string>();
    public int Started => Volatile.Read(ref started);
    public void Start() => Interlocked.Increment(ref started);
}

/// <summary>HTTP client of one host process; every response body is kept for the final leak scan.</summary>
sealed class Api
{
    readonly HttpClient http;
    readonly ResponseCapture capture;
    public Api(int port, ResponseCapture capture)
    {
        http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/"), Timeout = TimeSpan.FromSeconds(180) };
        this.capture = capture;
    }
    public Task<Result> Get(string path, string? token) => Send(HttpMethod.Get, path, token, null, null);
    public Task<Result> Post(string path, string? token, string body, string contentType = "application/json") => Send(HttpMethod.Post, path, token, body, contentType);
    async Task<Result> Send(HttpMethod method, string path, string? token, string? body, string? contentType)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        if (body != null)
        {
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "application/json");
        }
        capture.Start();
        using var response = await http.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        capture.Bodies.Enqueue(text);
        return new Result((int)response.StatusCode, text);
    }
}

/// <summary>One real host process; stdout/stderr are captured for the listening line, diagnostics and the leak scan.</summary>
sealed class ServerProcess
{
    static readonly Regex ListeningLine = new Regex(@"listening on http://127\.0\.0\.1:(\d+) ", RegexOptions.CultureInvariant);
    readonly StringBuilder output = new StringBuilder();
    readonly object sync = new object();
    readonly TaskCompletionSource<int> listening = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Name { get; }
    public Process Process { get; }
    public bool ListeningSeen => listening.Task.IsCompleted;
    public int Port => listening.Task.IsCompletedSuccessfully ? listening.Task.Result : throw new CheckFailure(Name + " has no port");
    public string Output { get { lock (sync) return output.ToString(); } }
    ServerProcess(string name, Process process) { Name = name; Process = process; }

    // arguments: raw arguments before the configuration (the bare --verify-store flag and its refused spellings).
    public static ServerProcess Launch(string name, string dotnet, string dll, IReadOnlyDictionary<string, string> config, string environment,
        IReadOnlyDictionary<string, string>? extra, string? workingDirectory, IReadOnlyList<string>? arguments = null)
    {
        var start = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        // The host's content root (and so its appsettings.json) is its working directory.
        if (workingDirectory != null) start.WorkingDirectory = workingDirectory;
        start.ArgumentList.Add(dll);
        if (arguments != null) foreach (string argument in arguments) start.ArgumentList.Add(argument);
        foreach (var pair in config) start.ArgumentList.Add("--" + pair.Key + "=" + pair.Value);
        // Only this launch's configuration: no inherited URLs, ports, Kestrel endpoints, environment names or MemoryForge settings.
        foreach (string key in start.Environment.Keys.ToList())
            if (key.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("DOTNET_URLS", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("DOTNET_HTTP", StringComparison.OrdinalIgnoreCase) || key.Equals("DOTNET_ENVIRONMENT", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("Kestrel__", StringComparison.OrdinalIgnoreCase) || key.StartsWith("MemoryForge__", StringComparison.OrdinalIgnoreCase))
                start.Environment.Remove(key);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
        start.Environment["DOTNET_ENVIRONMENT"] = environment;
        if (extra != null) foreach (var pair in extra) start.Environment[pair.Key] = pair.Value;
        var process = new Process { StartInfo = start };
        var server = new ServerProcess(name, process);
        process.OutputDataReceived += (_, line) => server.Line(line.Data, true);
        process.ErrorDataReceived += (_, line) => server.Line(line.Data, false);
        if (!process.Start()) throw new CheckFailure("could not start " + name);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return server;
    }
    void Line(string? line, bool stdout)
    {
        if (line == null) return;
        lock (sync) output.AppendLine(line);
        if (!stdout) return;
        var match = ListeningLine.Match(line);
        if (match.Success) listening.TrySetResult(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    }
    public async Task<bool> WaitListening(TimeSpan timeout)
    {
        await Task.WhenAny(listening.Task, Task.Delay(timeout), Process.WaitForExitAsync());
        return listening.Task.IsCompletedSuccessfully;
    }
    public async Task<bool> WaitExit(TimeSpan timeout)
    {
        using var cancel = new CancellationTokenSource(timeout);
        try { await Process.WaitForExitAsync(cancel.Token); }
        catch (OperationCanceledException) { return false; }
        Process.WaitForExit(); // drains the redirected output
        return true;
    }
    /// <summary>SIGKILL: a crash, not a graceful shutdown.</summary>
    public void Kill()
    {
        try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        Process.WaitForExit();
    }
}

sealed partial class ServerChecks
{
    const string RunsPath = "dev/practice/runs", AccountPath = "dev/practice/account";
    const UnixFileMode Owner = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    const UnixFileMode OwnerDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    static readonly string[] AccountIds = { "alpha", "beta", "gamma" };
    static readonly string[] Party = { "sera", "harin", "nia" };
    static readonly HashSet<string> ForbiddenKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "seed", "rng", "receipts", "receipt", "pattern", "pi", "journal", "digest", "sha256", "createdSha256", "identity", "credential",
        "credentialSha256", "binding", "revision", "runFlags", "battleFlags", "turnFlags", "offered", "hpRef", "seqCard", "seq", "resSeq",
        "startMemory", "memoryBaseline", "playBefore", "memoryAfter", "savedMemory", "costMod", "passive", "killedMarked", "pendingCopies"
    };
    readonly string root, temp, dotnet;
    readonly string? prebuilt;
    readonly Dictionary<string, string> tokens = new Dictionary<string, string>(StringComparer.Ordinal);
    readonly List<ServerProcess> processes = new List<ServerProcess>();
    readonly ResponseCapture capture = new ResponseCapture();
    readonly List<long> seeds = new List<long>();
    // Scenarios that must all have completed before the final leak scan counts as covering the suite.
    static readonly string[] RequiredScenarios = { "refusals", "auth-input", "expedition", "concurrency", "ownership", "write-failure", "battle-query", "kestrel-reload", "hosting-url-pin", "partial-mutation", "corrupt-stores",
        "recruitment", "verify-store", "expedition-balance", "progression-http", "growth-http" };
    readonly HashSet<string> completed = new HashSet<string>(StringComparer.Ordinal);
    string serverDll = "", fixture = "", store = "", alphaRunId = "", lockedDirectory = "";
    byte[] alphaDone = Array.Empty<byte>();
    public int Passed { get; private set; }
    string DiagnosticsFile => Path.Combine(temp, "diagnostics.log");

    public ServerChecks(string root, string? prebuilt)
    {
        this.root = root; this.prebuilt = prebuilt;
        temp = Path.Combine(Path.GetTempPath(), "mf-server-checks-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)));
        Directory.CreateDirectory(temp, OwnerDirectory);
        string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        dotnet = !string.IsNullOrEmpty(host) && File.Exists(host) ? host : "dotnet";
    }

    public async Task RunAll()
    {
        DiagnosticSafety();
        RecruitmentScanSafety();
        Check(File.Exists(Path.Combine(root, "data", "game_data.json")), "the repository root has data/game_data.json");
        serverDll = await BuildServer();
        WriteFixture();
        await StartupRefusals();
        store = Path.Combine(temp, "store");
        var main = await Start("main", root, store);
        await AuthenticationAndInput(main);
        main = await MainExpedition(main);
        await Concurrency(main);
        await Ownership(main);
        await WriteFailureRollback(main);
        await BattleQuery(main);
        main.Kill();
        await KestrelReloadCannotOpenEndpoints();
        await HostingUrlsCannotMoveThePort();
        await PartialMutationRollback();
        await CorruptStores();
        await RecruitmentFile();
        await VerifyStoreFile();
        await ExpeditionBalanceFile();
        await ProgressionHttpFile();
        await GrowthHttpFile();
        RecruitmentLeakScan();
        LeakScan(RequiredScenarios);
    }

    public void Finish(bool passed)
    {
        foreach (var process in processes) process.Kill();
        oracle?.StopAll();
        if (lockedDirectory.Length != 0) File.SetUnixFileMode(lockedDirectory, OwnerDirectory);
        if (passed) { try { Directory.Delete(temp, true); } catch (IOException) { } return; }
        // Host output stays private: raw text to the owner-only diagnostics file, only its size on the console.
        foreach (var process in processes) Console.Error.WriteLine(ProcessSummary(process.Name, process.Output));
        Console.Error.WriteLine("Raw diagnostics (owner-only, may contain private test data): " + DiagnosticsFile);
    }

    // ───────── safe diagnostics ─────────
    /// <summary>Appends raw runtime material to diagnostics.log (0600) inside the owner-only temp directory; never to the console.</summary>
    public void Record(string label, string raw)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("=== " + label + " ===\n" + raw + "\n");
        using (var stream = new FileStream(DiagnosticsFile, new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, UnixCreateMode = Owner })) stream.Write(bytes, 0, bytes.Length);
        File.SetUnixFileMode(DiagnosticsFile, Owner);
    }
    /// <summary>Last-line defence for console text: every known credential and seed (≥ 7 digits) is replaced.</summary>
    public string Redact(string text)
    {
        foreach (string secret in tokens.Values.Concat(seeds.Where(s => s >= 1000000).Select(s => s.ToString(CultureInfo.InvariantCulture))))
            text = text.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        return text;
    }
    /// <summary>An exception for the console: its type only (messages can quote private data).</summary>
    public static string Describe(Exception error) => "unexpected " + error.GetType().Name + " (message and stack in diagnostics.log)";
    string ProcessSummary(string name, string output)
    {
        Record(name + " output", output);
        return "--- " + name + ": " + output.Split('\n').Length + " output lines, " + Fingerprint(output) + " (raw in diagnostics.log) ---";
    }
    // HMAC with a per-run random key that is never printed: equal values compare equal within one run's output, yet a short private
    // value (a 32-bit seed, say) cannot be recovered from the tag by brute force.
    static readonly byte[] FingerprintKey = RandomNumberGenerator.GetBytes(32);
    static string Fingerprint(string text) => "length " + text.Length + ", tag " + Convert.ToHexStringLower(HMACSHA256.HashData(FingerprintKey, Encoding.UTF8.GetBytes(text))).Substring(0, 12);
    // Server error codes are fixed uppercase constants; anything else is not echoed.
    static string SafeCode(string? code) => code != null && code.Length <= 48 && code.Length >= 2 && code[0] >= 'A' && code[0] <= 'Z'
        && code.All(c => c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_') ? code : "<no listed code>";
    static string SafeName(string name) => name.Length >= 1 && name.Length <= 40 && name.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_') ? name : "?";

    // Planted markers (never registered as known secrets) prove the failure formatting itself cannot echo private values, and that the
    // privacy detectors catch seeds inside strings, credentials and raw RNG members in logs. Nothing here touches the server.
    void DiagnosticSafety()
    {
        string credential = "planted-" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        long seed = 1000000000L + RandomNumberGenerator.GetInt32(int.MaxValue);
        string seedText = seed.ToString(CultureInfo.InvariantCulture);
        string body = "{\"code\":\"" + credential + "\",\"debug\":\"" + seedText + "\",\"seed\":" + seedText + ",\"rng\":[1,2,3,4],\"message\":\"Exception at " + credential + "\"}";
        bool Echoes(string text) => text.Contains(credential, StringComparison.Ordinal) || text.Contains(seedText, StringComparison.Ordinal)
            || text.Contains("\"rng\"", StringComparison.Ordinal) || text.Contains("Exception at", StringComparison.Ordinal);
        var messages = new[]
        {
            FailureOf(() => Status(new Result(500, body), 200, "STALE_VERSION", "planted status")),
            FailureOf(() => Status(new Result(500, "not json " + credential), 200, null, "planted non-JSON status")),
            FailureOf(() => Same(body, "{}", "planted text")),
            FailureOf(() => Same(seed, 1L, "planted number")),
            ProcessSummary("planted host", "log " + body),
            Describe(new InvalidOperationException(body))
        };
        Check(messages.All(m => m.Length > 0 && !Echoes(m)), "failure diagnostics never echo a planted credential, seed, RNG member or error body");
        Check(File.GetUnixFileMode(DiagnosticsFile) == Owner && File.ReadAllText(DiagnosticsFile).Contains(credential, StringComparison.Ordinal),
            "raw failure material is kept only in the owner-only diagnostics file");
        var secrets = new Secrets(new[] { credential }, new[] { seedText });
        Check(ResponseLeak("{\"debug\":\"" + seedText + "\"}", secrets) != null && ResponseLeak("{\"note\":\"x" + credential + "y\"}", secrets) != null
            && ResponseLeak("{\"n\":" + seedText + "}", secrets) != null && ResponseLeak("{\"rng\":[1,2,3,4]}", secrets) != null
            && ResponseLeak("{\"code\":\"STALE_VERSION\",\"version\":3}", secrets) == null, "the response scan catches seeds in strings/numbers, credentials and private members");
        Check(LogLeak("info {\"rng\":[1,2,3,4]}", secrets) != null && LogLeak("seed " + seedText, secrets) != null && LogLeak("token " + credential, secrets) != null
            && LogLeak("MemoryForge dev practice server listening on http://127.0.0.1:5000 (Development, loopback only)", secrets) == null,
            "the log scan catches raw RNG/seed members, seeds and credentials");
    }
    static string FailureOf(Action check)
    {
        try { check(); }
        catch (CheckFailure failure) { return failure.Message; }
        return "";
    }

    // ───────── build, fixture, processes ─────────
    async Task<string> BuildServer()
    {
        if (prebuilt != null) { Check(File.Exists(prebuilt), "--server-dll exists"); return prebuilt; }
        string output = Path.Combine(temp, "server-bin");
        var start = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = root };
        foreach (string argument in new[] { "build", Path.Combine(root, "server", "MemoryForge.Server", "MemoryForge.Server.csproj"), "-c", "Debug", "-o", output, "--nologo" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new CheckFailure("could not start dotnet build");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string text = await stdout + await stderr;
        if (process.ExitCode != 0)
        {
            Record("dotnet build output", text);
            throw new CheckFailure("dotnet build of MemoryForge.Server failed (exit " + process.ExitCode + "; output in diagnostics.log)");
        }
        string dll = Path.Combine(output, "MemoryForge.Server.dll");
        Check(File.Exists(dll), "the server was built");
        return dll;
    }

    void WriteFixture()
    {
        foreach (string id in AccountIds) tokens[id] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        fixture = Path.Combine(temp, "fixture.json");
        WritePrivate(fixture, FixtureJson(null));
    }
    // ids: the fixture's account IDs (default alpha/beta/gamma); each needs its credential in tokens.
    string FixtureJson(Action<JsonObject>? change, IReadOnlyList<string>? ids = null)
    {
        var accounts = new JsonArray();
        foreach (string id in ids ?? AccountIds)
        {
            var party = new JsonArray();
            foreach (string cid in Party) party.Add(new JsonObject { ["charId"] = cid, ["res"] = 0 });
            var account = new JsonObject { ["accountId"] = id, ["credentialSha256"] = Sha256Hex(tokens[id]), ["length"] = "short", ["party"] = party };
            change?.Invoke(account);
            accounts.Add(account);
        }
        return new JsonObject
        {
            ["schema"] = "mf-dev-practice-fixture-1", ["developmentOnly"] = true,
            ["label"] = "MemoryForge.ServerChecks test fixture: development/test only, reference practice party, not a production owned collection",
            ["accounts"] = accounts
        }.ToJsonString();
    }
    Dictionary<string, string> Config(string contentRoot, string storeDirectory, string? fixturePath = null) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MemoryForge:ContentRoot"] = contentRoot, ["MemoryForge:PracticeFixture"] = fixturePath ?? fixture,
        ["MemoryForge:PracticeStore"] = storeDirectory, ["MemoryForge:PracticePort"] = "0"
    };
    ServerProcess Launch(string name, IReadOnlyDictionary<string, string> config, string environment = "Development", IReadOnlyDictionary<string, string>? extra = null,
        string? workingDirectory = null, IReadOnlyList<string>? arguments = null)
    {
        var server = ServerProcess.Launch(name, dotnet, serverDll, config, environment, extra, workingDirectory, arguments);
        processes.Add(server);
        return server;
    }
    async Task<ServerProcess> Start(string name, string contentRoot, string storeDirectory, string? workingDirectory = null)
    {
        var server = Launch(name, Config(contentRoot, storeDirectory), workingDirectory: workingDirectory);
        if (!await server.WaitListening(TimeSpan.FromSeconds(120)))
        {
            Record(name + " output", server.Output);
            throw new CheckFailure(name + " did not start listening (output in diagnostics.log)");
        }
        Check(server.ListeningSeen, name + " listens on 127.0.0.1 only");
        return server;
    }
    Api Client(ServerProcess server) => new Api(server.Port, capture);

    // ───────── startup refusals ─────────
    async Task StartupRefusals()
    {
        string refusalStore = Path.Combine(temp, "refusal-store");
        await Refused("the Production environment", Config(root, refusalStore), "Production");
        var missing = Config(root, refusalStore); missing.Remove("MemoryForge:PracticeFixture");
        await Refused("a missing fixture setting", missing);
        await Refused("a fixture path that does not exist", Config(root, refusalStore, Path.Combine(temp, "absent-fixture.json")));
        string noCredential = Path.Combine(temp, "fixture-no-credential.json");
        WritePrivate(noCredential, FixtureJson(account => account.Remove("credentialSha256")));
        await Refused("an account without a credential digest", Config(root, refusalStore, noCredential));
        string derived = Path.Combine(temp, "fixture-derived-stat.json");
        WritePrivate(derived, FixtureJson(account => ((JsonObject)account["party"]![0]!)["power4"] = 9999));
        await Refused("a fixture party carrying a derived stat", Config(root, refusalStore, derived));
        string shared = Path.Combine(temp, "fixture-group-readable.json");
        WritePrivate(shared, FixtureJson(null));
        File.SetUnixFileMode(shared, Owner | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        await Refused("a group/other-readable fixture", Config(root, refusalStore, shared));
        string inside = Path.Combine(root, "ops", "ai", "local", "server-checks-store-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6)));
        await Refused("a store inside the repository", Config(root, inside));
        Check(!Directory.Exists(inside), "a refused in-repository store is not created");
        await Refused("an injected Kestrel endpoint", Config(root, refusalStore), "Development",
            new Dictionary<string, string> { ["Kestrel__Endpoints__Public__Url"] = "http://0.0.0.0:0" });
        var noPort = Config(root, refusalStore); noPort.Remove("MemoryForge:PracticePort");
        await Refused("a missing port setting", noPort);
        completed.Add("refusals");
    }
    async Task Refused(string what, IReadOnlyDictionary<string, string> config, string environment = "Development", IReadOnlyDictionary<string, string>? extra = null)
    {
        var server = Launch("refusal: " + what, config, environment, extra);
        bool exited = await server.WaitExit(TimeSpan.FromSeconds(90));
        if (!exited) server.Kill();
        Check(exited && server.Process.ExitCode != 0 && !server.ListeningSeen, what + " refuses startup without listening");
        Check(server.Output.Contains("refused to start", StringComparison.Ordinal), what + " reports a refusal reason");
    }

    // ───────── authentication and input (nothing may be written) ─────────
    async Task AuthenticationAndInput(ServerProcess server)
    {
        var api = Client(server);
        string alpha = tokens["alpha"], file = AccountFile(store, "alpha");
        Status(await api.Get(AccountPath, null), 401, "UNAUTHORIZED", "no credential");
        Status(await api.Get(AccountPath, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24))), 401, "UNAUTHORIZED", "an unknown credential");
        Status(await api.Get(AccountPath, "short"), 401, "UNAUTHORIZED", "a too-short credential");
        Status(await api.Post(RunsPath, null, Obj(("requestId", Id()))), 401, "UNAUTHORIZED", "an anonymous create");
        var account = await api.Get(AccountPath, alpha);
        Status(account, 200, null, "the account read");
        Check(S(account.Json, "accountId") == "alpha" && I(account.Json.GetProperty("memory"), "version") == 0 && account.Json.GetProperty("run").ValueKind == JsonValueKind.Null,
            "a new account has memory version 0 and no run");
        Status(await api.Post(RunsPath, alpha, Obj(("requestId", Id()), ("seed", 7))), 400, "UNKNOWN_FIELD", "a client seed");
        Status(await api.Post(RunsPath, alpha, "{\"requestId\":\"" + Id() + "\",\"party\":[{\"charId\":\"sera\",\"hp\":999}]}"), 400, "UNKNOWN_FIELD", "a client party/HP");
        Status(await api.Post(RunsPath, alpha, "{\"requestId\":\"" + Id() + "\",\"state\":{}}"), 400, "UNKNOWN_FIELD", "a client state");
        Status(await api.Post(RunsPath, alpha, Obj(("requestId", "short"))), 400, "INVALID_REQUEST_ID", "a short requestId");
        string duplicate = Id();
        Status(await api.Post(RunsPath, alpha, "{\"requestId\":\"" + duplicate + "\",\"requestId\":\"" + duplicate + "\"}"), 400, "DUPLICATE_FIELD", "a duplicate member");
        Status(await api.Post(RunsPath, alpha, "not json"), 400, "INVALID_JSON", "malformed JSON");
        Status(await api.Post(RunsPath, alpha, Obj(("requestId", Id())), "text/plain"), 415, "UNSUPPORTED_MEDIA_TYPE", "a non-JSON content type");
        Status(await api.Get("dev/practice/unknown", alpha), 404, "NOT_FOUND", "an unknown route");
        Status(await api.Get(RunsPath, alpha), 405, "METHOD_NOT_ALLOWED", "GET on the create route");
        Status(await api.Get(AccountPath + "?seed=1", alpha), 400, "INVALID_REQUEST", "a query string");
        Status(await api.Get(RunsPath + "/pr-" + new string('0', 32), alpha), 404, "RUN_NOT_FOUND", "an unknown run");
        Status(await api.Get(RunsPath + "/not-a-run", alpha), 404, "RUN_NOT_FOUND", "a malformed run ID");
        Check(!File.Exists(file), "reads, refused credentials and rejected input wrote no account file");
        completed.Add("auth-input");
    }

    // ───────── alpha: create → enter → battle → restart/retry → reward → defeat → v2 save → restart → next departure ─────────
    async Task<ServerProcess> MainExpedition(ServerProcess server)
    {
        var api = Client(server);
        string token = tokens["alpha"], file = AccountFile(store, "alpha");
        var kinds = new List<string>();
        string createId = Id();
        var created = await api.Post(RunsPath, token, Obj(("requestId", createId)));
        Status(created, 200, null, "alpha creates a practice run");
        string runId = S(created.Json, "runId");
        var state = created.Json.GetProperty("state");
        Check(I(created.Json, "version") == 0 && S(state, "phase") == "MAP" && A(state, "available").Count == 3 && I(created.Json, "memoryVersion") == 0,
            "the run starts on the map at version 0 with three first-floor choices and memory version 0");
        Check(A(state, "party").Select(p => S(p, "charId")).SequenceEqual(Party), "the party is the trusted fixture party");
        CheckPrivateFiles(store, "alpha");
        seeds.Add(ReadBody(file).GetProperty("run").GetProperty("seed").GetInt64());

        Same((await api.Post(RunsPath, token, Obj(("requestId", createId)))).Body, created.Body, "a retried create returns the original bytes");
        byte[] committed = File.ReadAllBytes(file);
        Status(await api.Post(RunsPath, token, Obj(("requestId", createId), ("useMemory", false))), 409, "IDEMPOTENCY_CONFLICT", "the create requestId with another payload");
        Status(await api.Post(RunsPath, token, Obj(("requestId", Id()))), 409, "RUN_ACTIVE", "a second run while one is active");
        var read = await api.Get(RunsPath + "/" + runId, token);
        Status(read, 200, null, "the run read");
        Same(Raw(read.Json, "state"), Raw(created.Json, "state"), "the read view equals the created view");
        Status(await Preview(api, token, runId), 422, "WRONG_PHASE", "a memory preview outside a terminal save");
        Status(await Command(api, token, runId, Id(), 0, "setState", null), 400, "UNSUPPORTED_COMMAND", "an unsupported command");
        Status(await Command(api, token, runId, Id(), 0, "memoryCommit", null), 400, "UNSUPPORTED_COMMAND", "a save through the command route");
        string node = A(state, "available")[0].GetString()!;
        Status(await Command(api, token, runId, Id(), 0, "enter", new JsonObject { ["nodeId"] = node, ["hp"] = 999 }), 400, "UNKNOWN_FIELD", "an extra command argument");
        Status(await api.Post(CommandsPath(runId), token, "{\"requestId\":\"" + Id() + "\",\"expectedVersion\":0,\"kind\":\"enter\",\"args\":{\"nodeId\":\"" + node + "\"},\"state\":{}}"),
            400, "UNKNOWN_FIELD", "a client state beside a command");
        Status(await api.Post(CommandsPath(runId), token, "{\"requestId\":\"" + Id() + "\",\"expectedVersion\":0.5,\"kind\":\"end\"}"), 400, "INVALID_REQUEST", "a fractional version");
        Status(await Command(api, token, runId, Id(), 0, "enter", new JsonObject { ["nodeId"] = "z1f6n0" }), 422, "NODE_UNREACHABLE", "an unreachable node (Core rule)");
        Status(await Command(api, token, runId, Id(), 3, "enter", new JsonObject { ["nodeId"] = node }), 409, "STALE_VERSION", "a stale expectedVersion");
        Same(File.ReadAllBytes(file), committed, "queries, conflicts and rejections left the envelope byte-identical");

        string enterId = Id();
        var entered = await Command(api, token, runId, enterId, 0, "enter", new JsonObject { ["nodeId"] = node });
        Status(entered, 200, null, "enter the first node");
        kinds.Add("enter");
        state = entered.Json.GetProperty("state");
        Check(I(entered.Json, "version") == 1 && S(state, "phase") == "BATTLE" && S(state.GetProperty("battle"), "phase") == "PLAYER", "entering a battle node starts a real battle at version 1");
        Same((await Command(api, token, runId, enterId, 0, "enter", new JsonObject { ["nodeId"] = node })).Body, entered.Body,
            "the duplicate receipt wins before the (now stale) version check");
        Status(await Command(api, token, runId, enterId, 0, "enter", new JsonObject { ["nodeId"] = A(created.Json.GetProperty("state"), "available")[1].GetString()! }),
            409, "IDEMPOTENCY_CONFLICT", "the enter requestId with another node");

        committed = File.ReadAllBytes(file);
        string reused = Id();
        Status(await Command(api, token, runId, reused, 1, "play", new JsonObject { ["uid"] = "c9999", ["mode"] = "now" }), 422, "NOT_IN_HAND", "a card that is not in hand (Core rule)");
        Same(File.ReadAllBytes(file), committed, "the rejected play changed nothing");
        var first = Choose(state.GetProperty("battle"), new HashSet<string>(), 1) ?? ("end", new JsonObject(), "end");
        var played = await Command(api, token, runId, reused, 1, first.Kind, first.Args);
        Status(played, 200, null, "the rejected requestId is not consumed: the same ID commits a valid " + first.Kind);
        kinds.Add(first.Kind);
        state = played.Json.GetProperty("state");
        Check(I(played.Json, "version") == 2, "one committed command per success");

        // Crash and restart: the committed state, receipts and run version survive in the envelope alone.
        server.Kill();
        server = await Start("main after crash", root, store);
        api = Client(server);
        var restored = await api.Get(RunsPath + "/" + runId, token);
        Status(restored, 200, null, "the run read after a restart");
        Same(Raw(restored.Json, "state"), Raw(played.Json, "state"), "a fresh process serves exactly the last committed view");
        Same((await Command(api, token, runId, reused, 1, first.Kind, first.Args)).Body, played.Body, "a retry after the restart returns the original bytes");
        Same((await api.Post(RunsPath, token, Obj(("requestId", createId)))).Body, created.Body, "the create receipt survives the restart");
        // The identity differs by expectedVersion alone, so the conflict does not depend on which first command the hand allowed.
        Status(await Command(api, token, runId, reused, 2, first.Kind, first.Args), 409, "IDEMPOTENCY_CONFLICT", "another payload under a committed ID after the restart");

        var played1 = await PlayToDefeat(api, token, runId, state, kinds);
        state = played1.State;
        Check(S(state, "phase") == "DEFEAT" && S(state.GetProperty("pending"), "type") == "save" && state.GetProperty("pending").GetProperty("defeat").GetBoolean(),
            "the expedition reached a real defeat with a save pending");
        var refreshed = await api.Get(RunsPath + "/" + runId, token);
        Same(Raw(refreshed.Json, "state"), Raw(state, null), "the read at the terminal save equals the last command view");
        IndependentReplay(file, state, kinds);
        int version = I(state, "version");

        // Pure previews and rejected saves (Core rules) change nothing.
        committed = File.ReadAllBytes(file);
        var none = await Preview(api, token, runId);
        Status(none, 200, null, "the empty-selection preview");
        Check(none.Json.GetProperty("preview").GetProperty("error").ValueKind == JsonValueKind.Null, "keeping the previous deck is always allowed");
        (string Cid, string Id)? add = played1.TakenKey == null ? null : AddChange(state, played1.TakenKey);
        Check(played1.TakenKey == null || add != null, "the taken reward card is offered as an add change");
        if (add != null)
        {
            var preview = await Preview(api, token, runId, add.Value.Id);
            Status(preview, 200, null, "the add-change preview");
            Check(preview.Json.GetProperty("preview").GetProperty("error").ValueKind == JsonValueKind.Null
                && Keys(preview.Json.GetProperty("preview").GetProperty("memory").GetProperty(add.Value.Cid)).Contains(played1.TakenKey), "the preview deck contains the new card");
        }
        Status(await Preview(api, token, runId, "delta:none:none:add"), 422, "INVALID_SELECTION", "an unknown change in a preview");
        Status(await Commit(api, token, runId, Id(), version, 7), 422, "VERSION_CONFLICT", "a save against another memory version");
        Status(await Commit(api, token, runId, Id(), version, 0, "delta:none:none:add"), 422, "INVALID_SELECTION", "a save of an unknown change");
        Status(await Commit(api, token, runId, Id(), version - 1, 0), 409, "STALE_VERSION", "a save against an old run version");
        Same(File.ReadAllBytes(file), committed, "previews and rejected saves left the envelope byte-identical");

        string saveId = Id();
        string[] selected = add == null ? Array.Empty<string>() : new[] { add.Value.Id };
        var saved = await Commit(api, token, runId, saveId, version, 0, selected);
        Status(saved, 200, null, "the v2 terminal save");
        kinds.Add("memoryCommit");
        Check(I(saved.Json.GetProperty("result"), "version") == 1 && I(saved.Json, "memoryVersion") == 1 && S(saved.Json.GetProperty("state"), "phase") == "DONE",
            "the save commits memory version 1 and finishes the run");
        if (add != null) Check(Keys(saved.Json.GetProperty("result").GetProperty("memory").GetProperty(add.Value.Cid)).Contains(played1.TakenKey), "the saved deck holds the new card");
        Same((await Commit(api, token, runId, saveId, version, 0, selected)).Body, saved.Body, "a retried save returns the original bytes");
        Status(await Commit(api, token, runId, saveId, version, 0, "delta:none:none:add"), 409, "IDEMPOTENCY_CONFLICT", "the save ID with another selection");
        alphaDone = File.ReadAllBytes(file);
        var body = ReadBody(file);
        var memory = body.GetProperty("memory");
        Check(I(memory, "version") == 1 && memory.GetProperty("history").GetArrayLength() == 1 && memory.GetProperty("receipts").TryGetProperty(saveId, out _),
            "the stored v2 store has version 1, one history entry and the save's own receipt");
        Check(body.GetProperty("run").GetProperty("journal").EnumerateArray().Select(e => S(e, "kind")).SequenceEqual(kinds),
            "the journal holds exactly the successful commands, in order, once each");
        CheckPrivateFiles(store, "alpha");

        server.Kill();
        server = await Start("main after the save", root, store);
        api = Client(server);
        var account = await api.Get(AccountPath, token);
        Status(account, 200, null, "the account after the restart");
        Check(I(account.Json.GetProperty("memory"), "version") == 1 && S(account.Json.GetProperty("run"), "phase") == "DONE", "the saved memory and finished run survive the restart");
        if (add != null) Check(Keys(account.Json.GetProperty("memory").GetProperty("memory").GetProperty(add.Value.Cid)).Contains(played1.TakenKey), "the saved card survives the restart");
        Same((await Commit(api, token, runId, saveId, version, 0, selected)).Body, saved.Body, "the save receipt survives the restart");

        // A defeat that won no battle saves nothing new; depart again until a won reward card is saved (bounded, legitimate play only).
        (string RunId, string SaveId, int Version, int MemoryVersion, string[] Ids, string Body, string Cid, string Key)? cardSave = add == null || played1.TakenKey == null
            ? null : (runId, saveId, version, 0, selected, saved.Body, add.Value.Cid, played1.TakenKey!);
        int memoryVersion = 1;
        for (int attempt = 0; cardSave == null && attempt < 3; attempt++)
        {
            var again = await api.Post(RunsPath, token, Obj(("requestId", Id())));
            Status(again, 200, null, "a new departure after a save");
            string next = S(again.Json, "runId");
            seeds.Add(ReadBody(file).GetProperty("run").GetProperty("seed").GetInt64());
            var outcome = await PlayToDefeat(api, token, next, again.Json.GetProperty("state"), new List<string>());
            var change = outcome.TakenKey == null ? null : AddChange(outcome.State, outcome.TakenKey);
            string[] ids = change == null ? Array.Empty<string>() : new[] { change.Value.Id };
            string commitId = Id();
            int at = I(outcome.State, "version");
            var commit = await Commit(api, token, next, commitId, at, memoryVersion, ids);
            Status(commit, 200, null, "the save of a later departure");
            if (change != null && outcome.TakenKey != null)
            {
                Check(Keys(commit.Json.GetProperty("result").GetProperty("memory").GetProperty(change.Value.Cid)).Contains(outcome.TakenKey), "the later save holds the new card");
                cardSave = (next, commitId, at, memoryVersion, ids, commit.Body, change.Value.Cid, outcome.TakenKey!);
            }
            memoryVersion++;
        }
        if (cardSave == null) throw new CheckFailure("within four departures the check bot did not win a first battle to save a reward card (a bounded check-bot limit, not an observed server result)");
        var card = cardSave.Value;

        // The nonempty earned save must survive an actual crash: a new host process serves the saved deck and that save's original bytes.
        server.Kill();
        server = await Start("main after the card save", root, store);
        api = Client(server);
        var afterCard = await api.Get(AccountPath, token);
        Status(afterCard, 200, null, "the account after the card-save restart");
        Check(I(afterCard.Json.GetProperty("memory"), "version") == memoryVersion
            && Keys(afterCard.Json.GetProperty("memory").GetProperty("memory").GetProperty(card.Cid)).Contains(card.Key), "a new process serves the saved earned card");
        Same((await Commit(api, token, card.RunId, card.SaveId, card.Version, card.MemoryVersion, card.Ids)).Body, card.Body,
            "the earned card save's receipt returns its original bytes after the restart");

        var departure = await api.Post(RunsPath, token, Obj(("requestId", Id())));
        Status(departure, 200, null, "the next departure");
        alphaRunId = S(departure.Json, "runId");
        seeds.Add(ReadBody(file).GetProperty("run").GetProperty("seed").GetInt64());
        Check(I(departure.Json, "memoryVersion") == memoryVersion && A(departure.Json.GetProperty("state"), "deck").Any(c => c.TryGetProperty("key", out var key) && key.GetString() == card.Key),
            "the next departure starts from the saved memory version and deck");
        Status(await api.Post(RunsPath, token, Obj(("requestId", Id()))), 409, "RUN_ACTIVE", "one active run per account");
        Check(ReadBody(file).GetProperty("archive").GetArrayLength() == memoryVersion, "finished runs are kept whole in the archive");
        completed.Add("expedition");
        return server;
    }

    // Wins the first battle with a greedy check bot, takes one reward card, then only ends turns in the next battle until defeat. pick: the
    // index of the offered reward card to take, or -1 to take none yet and keep fighting (default: the first card).
    async Task<(JsonElement State, string? TakenKey)> PlayToDefeat(Api api, string token, string runId, JsonElement state, List<string> kinds,
        Func<List<JsonElement>, int>? pick = null, bool keepWinning = false)
    {
        string? taken = null;
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        for (int guard = 0; guard < 3000; guard++)
        {
            string phase = S(state, "phase");
            if (phase == "DEFEAT" || phase == "VICTORY") return (state, taken);
            int version = I(state, "version");
            (string Kind, JsonObject Args, string Key) command;
            switch (phase)
            {
                case "BATTLE":
                    var battle = state.GetProperty("battle");
                    command = (keepWinning || taken == null) && S(battle, "phase") == "PLAYER" ? Choose(battle, rejected, version) ?? ("end", new JsonObject(), "end") : ("end", new JsonObject(), "end");
                    break;
                case "REWARD":
                    var cards = A(state.GetProperty("pending"), "cards");
                    int choice = (keepWinning || taken == null) && cards.Count > 0 ? pick?.Invoke(cards) ?? 0 : -1;
                    command = choice >= 0 && choice < cards.Count ? ("takeCard", new JsonObject { ["id"] = cards[choice].GetString() }, "takeCard") : ("proceed", new JsonObject(), "proceed");
                    break;
                case "MAP": command = ("enter", new JsonObject { ["nodeId"] = A(state, "available")[0].GetString() }, "enter"); break;
                case "REST" when keepWinning && I(state.GetProperty("pending"), "uses") > 0:
                    command = ("rest", new JsonObject { ["choice"] = "heal" }, "rest"); break;
                case "REST": case "FORGE": case "SHOP": case "EVENT": command = ("proceed", new JsonObject(), "proceed"); break;
                case "ARCHIVE": command = ("archive", new JsonObject { ["choice"] = "return" }, "archive"); break;
                default:
                    Record("unexpected run phase", phase);
                    throw new CheckFailure("the run reached a phase the check bot does not handle (value in diagnostics.log)");
            }
            var before = DeckKeys(state);
            var result = await Command(api, token, runId, Id(), version, command.Kind, command.Args);
            if (result.Status == 422 && (command.Kind == "play" || command.Kind == "ult")) { rejected.Add(version.ToString(CultureInfo.InvariantCulture) + ":" + command.Key); continue; }
            Status(result, 200, null, "expedition " + command.Kind);
            kinds.Add(command.Kind);
            state = result.Json.GetProperty("state");
            if (command.Kind == "takeCard")
            {
                var added = DeckKeys(state).Except(before).ToList();
                Check(added.Count == 1, "takeCard adds exactly one deck card");
                taken = added[0];
            }
        }
        throw new CheckFailure("the expedition did not end within 3000 commands");
    }

    static (string Kind, JsonObject Args, string Key)? Choose(JsonElement battle, HashSet<string> rejected, int version)
    {
        string prefix = version.ToString(CultureInfo.InvariantCulture) + ":";
        var alive = battle.GetProperty("enemies").EnumerateArray().Where(e => e.GetProperty("alive").GetBoolean()).ToList();
        if (alive.Count == 0) return null;
        string lowest = S(alive.OrderBy(e => I(e, "hp")).First(), "uid");
        string? marked = alive.Where(e => I(e.GetProperty("st"), "mark") > 0).Select(e => S(e, "uid")).FirstOrDefault();
        foreach (var actor in battle.GetProperty("actors").EnumerateArray())
        {
            string id = S(actor, "id");
            if (I(actor, "gauge") >= I(battle, "gaugeMax") && !rejected.Contains(prefix + "ult:" + id)) return ("ult", new JsonObject { ["actor"] = id, ["target"] = lowest }, "ult:" + id);
        }
        foreach (var card in battle.GetProperty("hand").EnumerateArray())
        {
            string uid = S(card, "uid");
            if (rejected.Contains(prefix + "play:" + uid) || !card.TryGetProperty("blocked", out var blocked) || !blocked.TryGetProperty("now", out var now) || now.ValueKind != JsonValueKind.Null) continue;
            var args = new JsonObject { ["uid"] = uid, ["mode"] = "now" };
            string? need = card.GetProperty("needsTarget").ValueKind == JsonValueKind.String ? card.GetProperty("needsTarget").GetString() : null;
            if (need == "fixed") { if (marked == null) continue; args["target"] = marked; }
            else if (need == "one") args["target"] = lowest;
            return ("play", args, "play:" + uid);
        }
        return null;
    }

    // Check-side oracle: real Run.Create from the stored inputs plus an independent dispatch of the stored journal, compared with the served view.
    void IndependentReplay(string file, JsonElement served, List<string> kinds)
    {
        var run = ReadBody(file).GetProperty("run");
        var journal = run.GetProperty("journal").EnumerateArray().ToList();
        Check(journal.Select(e => S(e, "kind")).SequenceEqual(kinds), "the stored journal is exactly the committed command sequence");
        var core = CheckSideRun(run);
        var s = core.State;
        Check(s.Phase == S(served, "phase") && s.Hp == I(served, "hp") && s.MaxHp == I(served, "maxHp") && s.Coins == I(served, "coins")
            && s.Position == served.GetProperty("position").GetString(), "the independent Core replay matches the served phase, HP, coins and position");
        Check(s.Deck.Select(c => c.Key ?? "").SequenceEqual(DeckKeys(served)), "the independent Core replay matches the served deck");
        var changes = core.MemoryChanges();
        foreach (var pair in changes)
        {
            var expected = ((IList)((IDictionary<string, object?>)pair.Value!)["changes"]!).Cast<IDictionary<string, object?>>().Select(c => (string)c["id"]!).ToList();
            var actual = A(served.GetProperty("memory").GetProperty(pair.Key), "changes").Select(c => S(c, "id")).ToList();
            Check(expected.SequenceEqual(actual), "the independent Core replay offers the served change list for " + SafeName(pair.Key));
        }
    }
    // The check-side Core oracle of one stored run record: Run.Create from its stored inputs (seed, runId, memory version, start memory,
    // useMemory and the fixture party) plus an independent dispatch of its stored journal. No host code is involved and nothing is injected.
    Run CheckSideRun(JsonElement run)
    {
        var setup = new RunSetup
        {
            Seed = run.GetProperty("seed").GetInt64(), Length = "short", MemoryPolicy = "v2", RunId = S(run, "runId"), MemoryVersion = run.GetProperty("memoryVersion").GetInt64(),
            Memory = MemoryV2DataLoader.Graph(run.GetProperty("startMemory")), Difficulty = new RunDifficulty { Hp = 100, Damage = 100 },
            UseMemory = run.GetProperty("useMemory").GetBoolean() ? (bool?)null : false
        };
        foreach (string cid in Party) setup.Party.Add(new RunPartyEntry { CharId = cid, Res = 0 });
        var core = Run.Create(RunDataLoader.Load(root), setup);
        foreach (var entry in run.GetProperty("journal").EnumerateArray()) CoreExecute(core, S(entry, "kind"), entry.GetProperty("args"));
        return core;
    }
    static void CoreExecute(Run run, string kind, JsonElement args)
    {
        string Text(string name) => args.GetProperty(name).GetString()!;
        BattleArgument Arguments(params string[] names)
        {
            var members = new List<KeyValuePair<string, BattleArgument>>();
            foreach (string name in names)
            {
                if (!args.TryGetProperty(name, out var value)) continue;
                members.Add(new KeyValuePair<string, BattleArgument>(name, value.ValueKind == JsonValueKind.Array
                    ? BattleArgument.Array(value.EnumerateArray().Select(item => BattleArgument.String(item.GetString()!))) : BattleArgument.String(value.GetString()!)));
            }
            return BattleArgument.Object(members);
        }
        switch (kind)
        {
            case "enter": run.Enter(Text("nodeId")); break;
            case "play": run.BattleCommand("play", Arguments("uid", "mode", "target", "picks")); break;
            case "ult": run.BattleCommand("ult", Arguments("actor", "target", "picks")); break;
            case "end": run.BattleCommand("end"); break;
            case "takeCard": run.TakeCard(Text("id")); break;
            case "proceed": run.Proceed(); break;
            case "archive": run.Archive(Text("choice")); break;
            default: throw new CheckFailure("the check-side replay met a journal kind it does not expect");
        }
    }

    // ───────── beta: duplicate and stale races in one process and across two processes on one store ─────────
    async Task Concurrency(ServerProcess server)
    {
        var api = Client(server);
        string token = tokens["beta"], file = AccountFile(store, "beta");
        var created = await api.Post(RunsPath, token, Obj(("requestId", Id())));
        Status(created, 200, null, "beta creates a run");
        string runId = S(created.Json, "runId");
        seeds.Add(ReadBody(file).GetProperty("run").GetProperty("seed").GetInt64());
        var entered = await Command(api, token, runId, Id(), 0, "enter", new JsonObject { ["nodeId"] = A(created.Json.GetProperty("state"), "available")[0].GetString() });
        Status(entered, 200, null, "beta enters a battle");

        string same = Id();
        var duplicates = await Burst(16, _ => Command(api, token, runId, same, 1, "end", null));
        Check(duplicates.All(r => r.Status == 200) && duplicates.All(r => r.Body == duplicates[0].Body), "16 concurrent copies of one request all return the one committed reply");
        Same(await Version(api, token, runId), 2, "the duplicates committed once");
        var stale = await Burst(16, _ => Command(api, token, runId, Id(), 2, "end", null));
        Check(stale.Count(r => r.Status == 200) == 1 && stale.Count(r => r.Status == 409 && r.Code == "STALE_VERSION") == 15, "16 concurrent different requests on one version: exactly one commits");
        Same(await Version(api, token, runId), 3, "only the winner committed");

        var second = await Start("second host on the same store", root, store);
        var other = Client(second);
        var crossStale = await Burst(16, i => Command(i % 2 == 0 ? api : other, token, runId, Id(), 3, "end", null));
        Check(crossStale.Count(r => r.Status == 200) == 1 && crossStale.Count(r => r.Status == 409 && r.Code == "STALE_VERSION") == 15,
            "two host processes racing one version: the OS file lock lets exactly one commit");
        string crossSame = Id();
        var crossDuplicates = await Burst(16, i => Command(i % 2 == 0 ? api : other, token, runId, crossSame, 4, "end", null));
        Check(crossDuplicates.All(r => r.Status == 200 && r.Body == crossDuplicates[0].Body), "one request through two processes commits once and replies identically");
        var viewA = await api.Get(RunsPath + "/" + runId, token);
        var viewB = await other.Get(RunsPath + "/" + runId, token);
        Check(I(viewA.Json, "version") == 5 && Raw(viewA.Json, "state") == Raw(viewB.Json, "state"), "both processes serve the same committed version 5");
        Check(ReadBody(file).GetProperty("run").GetProperty("journal").EnumerateArray().Select(e => S(e, "kind")).SequenceEqual(new[] { "enter", "end", "end", "end", "end" }),
            "the journal has exactly one entry per committed command");
        second.Kill();
        Check(File.Exists(LockFile(store, "beta")), "the permanent lock file stays in place");
        CheckPrivateFiles(store, "beta");
        completed.Add("concurrency");
    }

    // ───────── ownership: another account's run does not exist for the caller ─────────
    async Task Ownership(ServerProcess server)
    {
        var api = Client(server);
        string beta = tokens["beta"], gamma = tokens["gamma"], alphaFile = AccountFile(store, "alpha");
        byte[] before = File.ReadAllBytes(alphaFile);
        Status(await api.Get(RunsPath + "/" + alphaRunId, beta), 404, "RUN_NOT_FOUND", "beta reading alpha's run");
        Status(await Command(api, beta, alphaRunId, Id(), 0, "enter", new JsonObject { ["nodeId"] = "z1f0n0" }), 404, "RUN_NOT_FOUND", "beta commanding alpha's run");
        Status(await Preview(api, beta, alphaRunId), 404, "RUN_NOT_FOUND", "beta previewing alpha's save");
        Status(await Commit(api, beta, alphaRunId, Id(), 0, 0), 404, "RUN_NOT_FOUND", "beta saving alpha's run");
        Status(await Command(api, gamma, alphaRunId, Id(), 0, "enter", new JsonObject { ["nodeId"] = "z1f0n0" }), 404, "RUN_NOT_FOUND", "gamma commanding alpha's run");
        Same(File.ReadAllBytes(alphaFile), before, "other accounts changed nothing of alpha");
        Check(!File.Exists(AccountFile(store, "gamma")), "a refused account wrote no file");
        Status(await api.Get(RunsPath + "/" + alphaRunId, tokens["alpha"]), 200, null, "alpha still reads its own run");
        completed.Add("ownership");
    }

    // ───────── a real write failure (read-only store directory) commits nothing and does not consume the requestId ─────────
    async Task WriteFailureRollback(ServerProcess server)
    {
        var api = Client(server);
        string token = tokens["beta"], file = AccountFile(store, "beta");
        string runId = S(ReadBody(file).GetProperty("run"), "runId");
        int version = await Version(api, token, runId);
        byte[] before = File.ReadAllBytes(file);
        string id = Id();
        File.SetUnixFileMode(store, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        lockedDirectory = store;
        try
        {
            bool enforced;
            string probe = Path.Combine(store, "probe");
            try { File.WriteAllText(probe, "x"); File.Delete(probe); enforced = false; }
            catch (UnauthorizedAccessException) { enforced = true; }
            Check(enforced, "the OS enforces the read-only store directory for this user (run the checks as a normal user, not root)");
            Status(await Command(api, token, runId, id, version, "end", null), 500, "STORE_WRITE_FAILED", "a command whose envelope cannot be written");
        }
        finally { File.SetUnixFileMode(store, OwnerDirectory); lockedDirectory = ""; }
        Same(File.ReadAllBytes(file), before, "the failed write left the committed envelope byte-identical");
        Same(await Version(api, token, runId), version, "the failed write committed no version");
        var retried = await Command(api, token, runId, id, version, "end", null);
        Status(retried, 200, null, "the same requestId commits once writing works again");
        Same(I(retried.Json, "version"), version + 1, "the retry committed exactly one command");
        CheckPrivateFiles(store, "beta");
        completed.Add("write-failure");
    }

    // ───────── a configuration reload can never open another socket ─────────
    // The host runs from a temp working directory (its content root) holding an empty appsettings.json; after it listens, the file gains
    // a Kestrel endpoint on a free 127.0.0.1 port. With the default reloadable Kestrel loader that endpoint opened at runtime; the host
    // replaces that loader, so the port must stay closed for the whole observation window while the approved route keeps working.
    async Task KestrelReloadCannotOpenEndpoints()
    {
        string workingDirectory = Path.Combine(temp, "reload-cwd"), settings = Path.Combine(workingDirectory, "appsettings.json");
        Directory.CreateDirectory(workingDirectory, OwnerDirectory);
        File.WriteAllText(settings, "{}");
        var server = await Start("reload host", root, Path.Combine(temp, "store-reload"), workingDirectory);
        var api = Client(server);
        Status(await api.Get(AccountPath, tokens["gamma"]), 200, null, "the approved route before the configuration change");
        int extra = FreeLoopbackPort();
        Check(extra != server.Port && !await CanConnect(extra), "the extra loopback port is closed before the change");
        File.WriteAllText(settings, "{\"Kestrel\":{\"Endpoints\":{\"UnexpectedReload\":{\"Url\":\"http://127.0.0.1:" + extra.ToString(CultureInfo.InvariantCulture) + "\"}}}}");
        bool opened = false;
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(8) && !opened)
        {
            await Task.Delay(250);
            opened = await CanConnect(extra);
        }
        Check(!opened, "a runtime appsettings Kestrel endpoint never opens a socket");
        Status(await api.Get(AccountPath, tokens["gamma"]), 200, null, "the approved authenticated route still works after the change");
        Check(!server.Process.HasExited, "the host keeps running after the configuration change");
        server.Kill();
        completed.Add("kestrel-reload");
    }
    // ───────── hosting URLs cannot replace the configured port ─────────
    // An explicit PracticePort plus a conflicting loopback-only hosting URL with preferHostingUrls=true, given once on the command line and
    // once through ASPNETCORE_* environment entries: the host must listen on the configured port, serve the authenticated route there,
    // and leave the other port closed. Both ports are free 127.0.0.1 ports; no wildcard or public address is used.
    async Task HostingUrlsCannotMoveThePort()
    {
        foreach (bool viaEnvironment in new[] { false, true })
        {
            string how = viaEnvironment ? "environment" : "command line";
            int pinned = FreeLoopbackPort(), other = FreeLoopbackPort();
            while (other == pinned) other = FreeLoopbackPort();
            string url = "http://127.0.0.1:" + other.ToString(CultureInfo.InvariantCulture);
            var config = Config(root, Path.Combine(temp, "store-pinned"));
            config["MemoryForge:PracticePort"] = pinned.ToString(CultureInfo.InvariantCulture);
            Dictionary<string, string>? environment = null;
            if (viaEnvironment) environment = new Dictionary<string, string> { ["ASPNETCORE_URLS"] = url, ["ASPNETCORE_PREFERHOSTINGURLS"] = "true" };
            else { config["urls"] = url; config["preferHostingUrls"] = "true"; }
            var server = Launch("pinned port (" + how + ")", config, extra: environment);
            if (!await server.WaitListening(TimeSpan.FromSeconds(120)))
            {
                Record(server.Name + " output", server.Output);
                throw new CheckFailure(server.Name + " did not start listening (output in diagnostics.log)");
            }
            Same(server.Port, pinned, "hosting URLs from the " + how + " cannot move the configured port");
            Status(await Client(server).Get(AccountPath, tokens["gamma"]), 200, null, "the authenticated route works on the configured port (" + how + ")");
            Check(!await CanConnect(other), "the hosting URL port from the " + how + " stays closed");
            Check(!server.Process.HasExited, "the pinned host keeps running (" + how + ")");
            server.Kill();
        }
        completed.Add("hosting-url-pin");
    }
    static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
    static async Task<bool> CanConnect(int port)
    {
        using var client = new TcpClient();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await client.ConnectAsync(IPAddress.Loopback, port, cancel.Token); return true; }
        catch (SocketException) { return false; }
        catch (OperationCanceledException) { return false; }
    }

    // ───────── gamma: Core's genuine partial Enter (test-local unknown encounter enemy) is rolled back ─────────
    async Task PartialMutationRollback()
    {
        string faultRoot = Path.Combine(temp, "fault-content");
        Directory.CreateDirectory(Path.Combine(faultRoot, "data"));
        var content = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "data", "game_data.json")))!;
        content["encounters"]!["1"]!["normal"] = new JsonArray(new JsonArray("test_missing_enemy"));
        File.WriteAllText(Path.Combine(faultRoot, "data", "game_data.json"), content.ToJsonString());
        string faultStore = Path.Combine(temp, "store-fault"), file = AccountFile(faultStore, "gamma"), token = tokens["gamma"];
        var fault = await Start("fault-content host", faultRoot, faultStore);
        var api = Client(fault);
        var created = await api.Post(RunsPath, token, Obj(("requestId", Id())));
        Status(created, 200, null, "gamma creates a run on the faulty content");
        string runId = S(created.Json, "runId");
        var available = A(created.Json.GetProperty("state"), "available");
        string node = available[0].GetString()!;
        byte[] before = File.ReadAllBytes(file);
        string id = Id();
        Status(await Command(api, token, runId, id, 0, "enter", new JsonObject { ["nodeId"] = node }), 422, "UNKNOWN_ENEMY", "Core refuses the faulty encounter");
        Same(File.ReadAllBytes(file), before, "the failed partial Enter left the envelope byte-identical");
        Same(Raw((await api.Get(RunsPath + "/" + runId, token)).Json, "state"), Raw(created.Json, "state"), "the served run is the pre-failure run");
        Status(await Command(api, token, runId, id, 0, "enter", new JsonObject { ["nodeId"] = node }), 422, "UNKNOWN_ENEMY", "the same failed request runs again (no receipt)");
        Status(await Command(api, token, runId, id, 0, "enter", new JsonObject { ["nodeId"] = available[1].GetString() }), 422, "UNKNOWN_ENEMY",
            "the failed requestId is not consumed by a conflict");

        // Check-side probe: the same Core call on the same inputs really did move position and draw RNG before refusing.
        var run = ReadBody(file).GetProperty("run");
        seeds.Add(run.GetProperty("seed").GetInt64());
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
            "Core's own Enter changed position and RNG before refusing, so the server rollback was of a genuine partial mutation");
        fault.Kill();

        var real = await Start("real-content host on the fault store", root, faultStore);
        var realApi = Client(real);
        Status(await realApi.Get(AccountPath, token), 409, "STORE_CONTENT_MISMATCH", "an envelope bound to other content");
        Status(await realApi.Post(RunsPath, token, Obj(("requestId", Id()))), 409, "STORE_CONTENT_MISMATCH", "a mutation of an envelope bound to other content");
        Same(File.ReadAllBytes(file), before, "the mismatched envelope is preserved, not reset");
        real.Kill();
        completed.Add("partial-mutation");
    }

    // ───────── tampered or unknown envelopes are refused and left untouched ─────────
    async Task CorruptStores()
    {
        string corrupt = Path.Combine(temp, "store-corrupt"), file = AccountFile(corrupt, "alpha"), token = tokens["alpha"];
        Directory.CreateDirectory(corrupt, OwnerDirectory);
        WritePrivate(file, alphaDone);
        var server = await Start("corrupt-store host", root, corrupt);
        var api = Client(server);
        var valid = await api.Get(AccountPath, token);
        Status(valid, 200, null, "an intact copied envelope loads in another directory and process");
        string runId = S(valid.Json.GetProperty("run"), "runId");
        Status(await api.Get(RunsPath + "/" + runId, token), 200, null, "its finished run replays");
        string firstNode = S(ReadBody(file).GetProperty("run").GetProperty("journal")[0].GetProperty("args"), "nodeId");
        string otherNode = firstNode == "z1f0n0" ? "z1f0n1" : "z1f0n0";
        var cases = new (string What, byte[] Bytes, int Status, string Code)[]
        {
            ("garbage bytes", Encoding.UTF8.GetBytes("{not json"), 500, "STORE_CORRUPT"),
            ("a body edit without its digest", Tamper(alphaDone, body => body["revision"] = body["revision"]!.GetValue<long>() + 1, false), 500, "STORE_CORRUPT"),
            ("a re-digested journal edit", Tamper(alphaDone, body => body["run"]!["journal"]![0]!["args"]!["nodeId"] = otherNode, true), 500, "STORE_REPLAY_MISMATCH"),
            ("a re-digested created-state edit", Tamper(alphaDone, body => body["run"]!["createdSha256"] = new string('0', 64), true), 500, "STORE_REPLAY_MISMATCH"),
            ("an unknown schema", Outer(alphaDone, outer => outer["schema"] = "mf-dev-practice-account-999"), 409, "STORE_UNSUPPORTED"),
            ("other content", Tamper(alphaDone, body => body["binding"]!["contentSha256"] = new string('0', 64), true), 409, "STORE_CONTENT_MISMATCH"),
            ("another Core build", Tamper(alphaDone, body => body["binding"]!["coreModule"] = new string('0', 32), true), 409, "STORE_IMPLEMENTATION_MISMATCH"),
            ("another fixture", Tamper(alphaDone, body => body["binding"]!["fixtureSha256"] = new string('0', 64), true), 409, "STORE_FIXTURE_MISMATCH"),
        };
        foreach (var item in cases)
        {
            WritePrivate(file, item.Bytes);
            Status(await api.Get(AccountPath, token), item.Status, item.Code, item.What + ": account read");
            Status(await api.Get(RunsPath + "/" + runId, token), item.Status, item.Code, item.What + ": run read");
            Status(await api.Post(RunsPath, token, Obj(("requestId", Id()))), item.Status, item.Code, item.What + ": create");
            Same(File.ReadAllBytes(file), item.Bytes, item.What + ": the refused envelope is preserved byte for byte");
        }
        server.Kill();
        completed.Add("corrupt-stores");
    }

    // ───────── privacy: no response or log carries a credential, seed, RNG or private member ─────────
    // Complete capture: every started request's response body and every launched process's output is scanned, after every required
    // scenario completed. Findings are reported by safe JSON path/category only; the raw material goes to diagnostics.log.
    // required: the invocation's mandatory scenarios (file suite or PostgreSQL mode). tokens holds every secret to look for, including the
    // database password in the PostgreSQL mode.
    void LeakScan(IReadOnlyList<string> required)
    {
        var secrets = new Secrets(tokens.Values.ToArray(), seeds.Where(s => s >= 1000000).Select(s => s.ToString(CultureInfo.InvariantCulture)).ToArray());
        Check(required.All(completed.Contains), "every required scenario completed before the privacy scan");
        int scanned = 0;
        foreach (string body in capture.Bodies)
        {
            string? leak = ResponseLeak(body, secrets);
            if (leak != null)
            {
                Record("response leak at " + leak, body);
                throw new CheckFailure("private data in a captured response (" + leak + "; raw in diagnostics.log)");
            }
            scanned++;
        }
        Check(scanned > 0 && scanned == capture.Started,
            "every started request's response (" + scanned + ") was captured and is JSON without private members, credentials, seeds or exception text");
        int logs = 0;
        foreach (var process in processes)
        {
            string? leak = LogLeak(process.Output, secrets);
            if (leak != null)
            {
                Record(process.Name + " log leak (" + leak + ")", process.Output);
                throw new CheckFailure(process.Name + " output carries " + leak + " (raw in diagnostics.log)");
            }
            logs++;
        }
        Check(logs > 0 && logs == processes.Count, "no launched host process (" + logs + ") logged a credential, seed or private state member");
    }
    sealed record Secrets(string[] Tokens, string[] Seeds);
    static readonly Regex PrivateMember = new Regex("\"(seed|rng|receipts|journal|startMemory|createdSha256|credentialSha256|sha256|identity|binding)\"\\s*:", RegexOptions.CultureInvariant);
    static string? ResponseLeak(string body, Secrets secrets)
    {
        if (secrets.Tokens.Any(t => body.Contains(t, StringComparison.Ordinal))) return "credential text";
        try
        {
            using var document = JsonDocument.Parse(body);
            return Leak(document.RootElement, secrets, "$");
        }
        catch (JsonException) { return "a non-JSON body"; }
    }
    static string? LogLeak(string output, Secrets secrets)
    {
        if (secrets.Tokens.Any(t => output.Contains(t, StringComparison.Ordinal))) return "a credential";
        if (secrets.Seeds.Any(s => output.Contains(s, StringComparison.Ordinal))) return "a seed";
        return PrivateMember.IsMatch(output) ? "a private state member" : null;
    }
    static string? Leak(JsonElement element, Secrets secrets, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    string at = path + "." + SafeName(property.Name);
                    if (ForbiddenKeys.Contains(property.Name)) return at;
                    if ((property.Name == "draw" || property.Name == "discard" || property.Name == "exhaust") && property.Value.ValueKind != JsonValueKind.Number) return at + " (pile contents)";
                    if (property.Name == "cards" && property.Value.ValueKind == JsonValueKind.Object) return at + " (battle card map)";
                    string? inner = Leak(property.Value, secrets, at);
                    if (inner != null) return inner;
                }
                return null;
            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray()) { string? inner = Leak(item, secrets, path + "[" + index++ + "]"); if (inner != null) return inner; }
                return null;
            case JsonValueKind.Number: return secrets.Seeds.Contains(element.GetRawText()) ? path + " (seed value)" : null;
            case JsonValueKind.String:
                string text = element.GetString() ?? "";
                if (secrets.Seeds.Any(s => text.Contains(s, StringComparison.Ordinal)) || secrets.Tokens.Any(t => text.Contains(t, StringComparison.Ordinal))) return path + " (private value in text)";
                return text.Contains("Exception", StringComparison.Ordinal) || text.Contains("   at ", StringComparison.Ordinal) ? path + " (exception text)" : null;
            default: return null;
        }
    }

    // ───────── helpers ─────────
    void Check(bool condition, string what)
    {
        if (!condition) throw new CheckFailure(what);
        Passed++;
    }
    // Failure text names the check and safe metadata only (keyed fingerprints cannot be reversed or brute-forced without the run's key);
    // the raw values go to diagnostics.log.
    void Same<T>(T actual, T expected, string what)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected)) { Passed++; return; }
        string got = Convert.ToString(actual, CultureInfo.InvariantCulture) ?? "null", want = Convert.ToString(expected, CultureInfo.InvariantCulture) ?? "null";
        Record(what, "actual:\n" + got + "\nexpected:\n" + want);
        throw new CheckFailure(what + ": values differ (actual " + Fingerprint(got) + "; expected " + Fingerprint(want) + "; raw in diagnostics.log)");
    }
    void Same(byte[] actual, byte[] expected, string what) => Check(actual.AsSpan().SequenceEqual(expected), what);
    void Status(Result result, int status, string? code, string what)
    {
        if (result.Status == status && (code == null || result.Code == code)) { Passed++; return; }
        Record(what, "status " + result.Status.ToString(CultureInfo.InvariantCulture) + "\n" + result.Body);
        throw new CheckFailure(what + ": expected " + status + (code == null ? "" : " " + code) + ", got " + result.Status + " " + SafeCode(result.Code)
            + " (body " + Fingerprint(result.Body) + "; raw in diagnostics.log)");
    }
    void CheckPrivateFiles(string directory, string account)
    {
        Check(File.GetUnixFileMode(directory) == OwnerDirectory, "the store directory is owner-only (0700)");
        Check(File.GetUnixFileMode(AccountFile(directory, account)) == Owner && File.GetUnixFileMode(LockFile(directory, account)) == Owner, "envelope and lock files are owner-only (0600)");
        Check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "no temporary envelope is left behind");
    }
    async Task<int> Version(Api api, string token, string runId)
    {
        var read = await api.Get(RunsPath + "/" + runId, token);
        Status(read, 200, null, "run read");
        return I(read.Json, "version");
    }
    static async Task<Result[]> Burst(int count, Func<int, Task<Result>> send) => await Task.WhenAll(Enumerable.Range(0, count).Select(send));
    static Task<Result> Command(Api api, string token, string runId, string requestId, int version, string kind, JsonObject? args) =>
        api.Post(CommandsPath(runId), token, new JsonObject { ["requestId"] = requestId, ["expectedVersion"] = version, ["kind"] = kind, ["args"] = args?.DeepClone() ?? new JsonObject() }.ToJsonString());
    static Task<Result> Preview(Api api, string token, string runId, params string[] ids) =>
        api.Post(RunsPath + "/" + runId + "/memory-preview", token, new JsonObject { ["selectedChangeIds"] = Ids(ids) }.ToJsonString());
    static Task<Result> Commit(Api api, string token, string runId, string requestId, int version, int memoryVersion, params string[] ids) =>
        api.Post(RunsPath + "/" + runId + "/memory-commit", token, new JsonObject
        {
            ["requestId"] = requestId, ["expectedVersion"] = version, ["expectedMemoryVersion"] = memoryVersion, ["selectedChangeIds"] = Ids(ids)
        }.ToJsonString());
    static JsonArray Ids(IEnumerable<string> ids) => new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
    static string CommandsPath(string runId) => RunsPath + "/" + runId + "/commands";
    static string Obj(params (string Name, object Value)[] members)
    {
        var json = new JsonObject();
        foreach (var (name, value) in members) json[name] = value switch { string text => JsonValue.Create(text), bool flag => JsonValue.Create(flag), int number => JsonValue.Create(number), _ => throw new ArgumentException(name) };
        return json.ToJsonString();
    }
    static (string Cid, string Id)? AddChange(JsonElement state, string key)
    {
        foreach (var character in state.GetProperty("memory").EnumerateObject())
            foreach (var change in character.Value.GetProperty("changes").EnumerateArray())
                if (S(change, "kind") == "add" && S(change, "key") == key) return (character.Name, S(change, "id"));
        return null;
    }
    static List<string> DeckKeys(JsonElement state) => A(state, "deck").Select(c => c.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String ? key.GetString()! : "").ToList();
    static List<string?> Keys(JsonElement specs) => specs.EnumerateArray().Select(c => c.TryGetProperty("key", out var key) ? key.GetString() : null).ToList();
    static JsonElement ReadBody(string file)
    {
        using var outer = JsonDocument.Parse(File.ReadAllBytes(file));
        using var inner = JsonDocument.Parse(outer.RootElement.GetProperty("body").GetString()!);
        return inner.RootElement.Clone();
    }
    // Rewrites the envelope body; recompute = also replace the outer digest so only the server's semantic checks can notice.
    static byte[] Tamper(byte[] original, Action<JsonObject> edit, bool recompute) => Outer(original, outer =>
    {
        var body = JsonNode.Parse(outer["body"]!.GetValue<string>())!.AsObject();
        edit(body);
        string text = body.ToJsonString();
        outer["body"] = text;
        if (recompute) outer["sha256"] = Sha256Hex(text);
    });
    static byte[] Outer(byte[] original, Action<JsonObject> edit)
    {
        var outer = JsonNode.Parse(original)!.AsObject();
        edit(outer);
        return Encoding.UTF8.GetBytes(outer.ToJsonString());
    }
    static void WritePrivate(string path, string text) => WritePrivate(path, Encoding.UTF8.GetBytes(text));
    static void WritePrivate(string path, byte[] bytes)
    {
        using (var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = Owner })) stream.Write(bytes, 0, bytes.Length);
        File.SetUnixFileMode(path, Owner);
    }
    static string AccountFile(string directory, string account) => Path.Combine(directory, "account-" + account + ".json");
    static string LockFile(string directory, string account) => Path.Combine(directory, "account-" + account + ".lock");
    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Id() => "chk-" + Guid.NewGuid().ToString("N");
    static string S(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new CheckFailure(name + " is null");
    static int I(JsonElement element, string name) => element.GetProperty(name).GetInt32();
    static List<JsonElement> A(JsonElement element, string name) => element.GetProperty(name).EnumerateArray().ToList();
    static string Raw(JsonElement element, string? name) => name == null ? element.GetRawText() : element.GetProperty(name).GetRawText();
}
