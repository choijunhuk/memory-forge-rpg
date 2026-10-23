// P1-LOCAL-AUTHORITATIVE-PERSISTENCE §16.5/§17.2-17.5/§18.1/§20.9 P1-2: the LOCAL DEVELOPMENT practice host. It is not the production
// API of server/openapi.yaml: explicit /dev/practice routes, Development environment only, Kestrel bound to 127.0.0.1 only (configured
// Kestrel endpoints refuse startup, the default reloadable Kestrel configuration loader is replaced by an empty non-reloading one so no
// configuration change can open another socket later, PreferHostingUrls is pinned false so --urls/ASPNETCORE_URLS cannot replace the
// explicit Listen, and any address other than 127.0.0.1 on the configured port at start stops the host), a bearer credential matched in constant time against the trusted fixture's SHA-256 digests, no CORS,
// no anonymous route, no static files, no query parameters. Configuration (command line --MemoryForge:Key=value or MemoryForge__Key):
// ContentRoot (repository with data/game_data.json), PracticeFixture (owner-only fixture file), PracticePort (0 = ephemeral) and the
// backend: PracticeBackend absent or "file" uses PracticeStore (owner-only directory outside the content root); PracticeBackend=postgres
// uses PracticePostgresFile (owner-only connection-string file outside the content root, loopback/Unix-socket database only) and must not
// be combined with PracticeStore; a connection file without the postgres backend, or any other backend name, is refused. There is no
// fallback between backends. A missing or invalid value, or a database that fails verification, refuses startup with exit code 2. Logs
// carry only exception type names (and SQLSTATE codes): never credentials, seeds, RNG, private state or exception messages.
// RECRUIT-SERVER-20261021 adds the account's development recruitment under the same credential and envelope: GET /dev/practice/recruitment
// and POST .../recruitment/pulls, .../claim-launch and .../selector (Recruitment.cs).
// ACCOUNT-PROGRESSION-S0-20261021: the bare flag --verify-store runs the owner-only, offline, read-only store verifier
// (AccountStoreVerifier.cs) on this same startup configuration instead of the listener: no socket, route, authentication or write, and the
// file store directory is never created. The create/command reply projection (RunReplies) is shared with it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MemoryForge.Core;

// POSIX owner-only permissions and flock-backed FileShare.None are part of the contract; Windows refuses at startup.
[assembly: System.Runtime.Versioning.UnsupportedOSPlatform("windows")]

namespace MemoryForge.Server
{
    static class Program
    {
        const string Listening = "MemoryForge dev practice server listening on ";

        static async Task<int> Main(string[] args)
        {
            if (OperatingSystem.IsWindows()) { Console.Error.WriteLine("MemoryForge dev practice server refused to start: macOS/Linux only (POSIX permissions and file locks)"); return 2; }
            if(OwnerMigration.Requested(args))return await OwnerMigration.RunAsync(args,Console.Out,Console.Error);
            // The bare --verify-store is taken out of the arguments, so the configuration parser never pairs it with the next argument; any other
            // spelling of it (repeated, with a value, another case or prefix) is refused rather than starting the listener.
            bool verify = false;
            var hostArgs = new List<string>();
            foreach (string arg in args)
            {
                if (arg == AccountStoreVerifier.Flag && !verify) { verify = true; continue; }
                if (AccountStoreVerifier.Resembles(arg))
                {
                    Console.Error.WriteLine("MemoryForge dev practice server refused to start: " + AccountStoreVerifier.Flag + " is accepted only once, as a bare flag");
                    return 2;
                }
                hostArgs.Add(arg);
            }
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = hostArgs.ToArray() });
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(options => { options.SingleLine = true; options.IncludeScopes = false; });
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            if (verify) return await AccountStoreVerifier.RunAsync(builder.Environment, builder.Configuration, Console.Out, Console.Error);
            PracticeHost host;
            try { host = await PracticeHost.ConfigureAsync(builder.Environment, builder.Configuration); }
            catch (StartupRefusal refusal) { Console.Error.WriteLine("MemoryForge dev practice server refused to start: " + refusal.Message); return 2; }
            await using var owned = host;
            // Hosting URLs (--urls, ASPNETCORE_URLS, ...) must never take precedence over the single explicit Listen below, whatever a
            // command-line or environment preferHostingUrls says; this setting is applied last at the builder, so it wins.
            builder.WebHost.PreferHostingUrls(false);
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.AddServerHeader = false;
                kestrel.Limits.MaxRequestBodySize = Contracts.MaxBodyBytes;
                // Replaces the default reloadable "Kestrel" configuration loader (the last Configure before Load wins): an empty, non-reloading
                // section, so no appsettings/environment change can ever add an endpoint at startup or at runtime. Only this Listen opens.
                kestrel.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false);
                kestrel.Listen(IPAddress.Loopback, host.Port);
            });
            var app = builder.Build();
            host.UseLogger(app.Logger);
            var api = new PracticeApi(host, app.Logger);
            app.Run(api.Handle);
            try { await app.StartAsync(); }
            catch (Exception error) { Console.Error.WriteLine("MemoryForge dev practice server refused to start: the loopback port could not be bound (" + error.GetType().Name + ")"); return 2; }
            var addresses = app.Urls.ToList();
            // Final fail-closed check: exactly one http://127.0.0.1 address, on the configured port (0 = the server-chosen ephemeral port).
            if (addresses.Count != 1 || !addresses.All(address => IsApproved(address, host.Port)))
            {
                Console.Error.WriteLine("MemoryForge dev practice server stopped: it was bound outside the approved 127.0.0.1 port");
                await app.StopAsync();
                return 3;
            }
            Console.Out.WriteLine(Listening + addresses[0] + " (Development, loopback only)");
            Console.Out.Flush();
            await app.WaitForShutdownAsync();
            return 0;
        }

        static bool IsApproved(string address, int port) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp
            && IPAddress.TryParse(uri.Host, out var ip) && ip.Equals(IPAddress.Loopback) && (port == 0 || uri.Port == port);
    }

    sealed class PracticeHost : IAsyncDisposable
    {
        const UnixFileMode GroupOrOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        public PracticeContent Content { get; }
        public PracticeFixture Fixture { get; }
        public PracticeRunStore Store { get; }
        /// <summary>The configured backend Store decides around; --verify-store only calls its pure ReadAsync and never transacts.</summary>
        public IEnvelopePersistence Persistence { get; }
        public string Backend => postgres == null ? "file" : "postgres";
        public int Port { get; }
        readonly PostgresEnvelopePersistence? postgres;
        PracticeHost(PracticeContent content, PracticeFixture fixture, IEnvelopePersistence persistence, int port, PostgresEnvelopePersistence? postgres)
        {
            Content = content; Fixture = fixture; Persistence = persistence; Store = new PracticeRunStore(content, persistence); Port = port; this.postgres = postgres;
        }

        /// <summary>readOnly (--verify-store): the same configuration and checks, except that the file store directory must already exist and
        /// is never created; the PostgreSQL startup verification is SELECT-only in both modes.</summary>
        public static async Task<PracticeHost> ConfigureAsync(IHostEnvironment environment, IConfiguration configuration, bool readOnly = false)
        {
            if (!environment.IsDevelopment()) throw new StartupRefusal("only the Development environment is supported; there is no production mode or fallback");
            if (configuration.GetSection("Kestrel:Endpoints").GetChildren().Any()) throw new StartupRefusal("Kestrel endpoint configuration is not accepted; the host binds 127.0.0.1 only");
            string contentRoot = AbsolutePath(configuration, "MemoryForge:ContentRoot");
            string fixture = AbsolutePath(configuration, "MemoryForge:PracticeFixture");
            string? backend = configuration["MemoryForge:PracticeBackend"];
            bool postgresBackend = backend == "postgres";
            if (backend != null && backend != "file" && !postgresBackend) throw new StartupRefusal("MemoryForge:PracticeBackend must be file (the default) or postgres");
            string store = "", connectionFile = "";
            if (postgresBackend)
            {
                if (!string.IsNullOrEmpty(configuration["MemoryForge:PracticeStore"])) throw new StartupRefusal("MemoryForge:PracticeStore must not be set with the postgres backend (no dual or fallback store)");
                connectionFile = AbsolutePath(configuration, "MemoryForge:PracticePostgresFile");
            }
            else
            {
                if (!string.IsNullOrEmpty(configuration["MemoryForge:PracticePostgresFile"])) throw new StartupRefusal("MemoryForge:PracticePostgresFile is only accepted with MemoryForge:PracticeBackend=postgres");
                store = AbsolutePath(configuration, "MemoryForge:PracticeStore");
            }
            if (!int.TryParse(configuration["MemoryForge:PracticePort"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int port) || port > 65535)
                throw new StartupRefusal("MemoryForge:PracticePort must be 0..65535 (0 = ephemeral)");
            if (!Directory.Exists(contentRoot)) throw new StartupRefusal("MemoryForge:ContentRoot is not a directory");
            try { AccountPolicy.Verify(); }
            catch (InvalidDataException) { throw new StartupRefusal("the account progression policy is invalid"); }
            var content = PracticeContent.Load(contentRoot);
            GrowthPolicy.Require(GrowthPolicy.Version, GrowthPolicy.Hash);
            try { GrowthPolicy.Require(EquipmentPolicy.Version, EquipmentPolicy.Hash); }
            catch (InvalidDataException) { throw new StartupRefusal("the account equipment policy is invalid"); }
            try{GrowthPolicy.Require(SupportPolicy.Version,SupportPolicy.Hash);}catch(InvalidDataException){throw new StartupRefusal("the account support policy is invalid");}
            // Only the embedded preset's identity is verified here; its content binding is checked when a story launch records it, so a
            // host on other (test) content still starts and refuses its stores by the existing content binding.
            try { ExpeditionBalancePolicy.Verify(); }
            catch (InvalidDataException) { throw new StartupRefusal("the expedition balance policy is invalid"); }
            var practiceFixture = PracticeFixture.Load(fixture, content);
            if (!postgresBackend)
            {
                string directory = readOnly ? ExistingStore(store, contentRoot) : FileEnvelopePersistence.PrepareDirectory(store, contentRoot);
                return new PracticeHost(content, practiceFixture, new FileEnvelopePersistence(directory), port, null);
            }
            var postgres = await PostgresEnvelopePersistence.OpenAsync(PostgresSettings.ConnectionString(connectionFile, contentRoot));
            return new PracticeHost(content, practiceFixture, postgres, port, postgres);
        }

        // FileEnvelopePersistence.PrepareDirectory's checks (outside the repository, not a link, owner-only) without its creation: a store that
        // does not exist is refused, so a verification never leaves even an empty directory behind.
        static string ExistingStore(string path, string contentRoot)
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (PracticeRunStore.IsInside(full, contentRoot)) throw new StartupRefusal("the private store must be outside the content root (repository)");
            try
            {
                var info = new DirectoryInfo(full);
                if (!info.Exists) throw new StartupRefusal(File.Exists(full) ? "the private store path is a file" : "the private store directory does not exist (--verify-store never creates it)");
                if (info.LinkTarget != null) throw new StartupRefusal("the private store must not be a symbolic link");
                if ((File.GetUnixFileMode(full) & GroupOrOther) != 0) throw new StartupRefusal("the private store directory must not be accessible to group/other (chmod 700)");
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { throw new StartupRefusal("the private store directory cannot be inspected"); }
            return full;
        }

        /// <summary>Lets the PostgreSQL backend log failure categories (type and SQLSTATE only) once the host's logger exists.</summary>
        public void UseLogger(ILogger logger) => postgres?.UseLogger(logger);

        public ValueTask DisposeAsync() => postgres?.DisposeAsync() ?? ValueTask.CompletedTask;
        static string AbsolutePath(IConfiguration configuration, string key)
        {
            string? value = configuration[key];
            if (string.IsNullOrEmpty(value)) throw new StartupRefusal(key + " is required");
            if (!Path.IsPathFullyQualified(value)) throw new StartupRefusal(key + " must be an absolute path");
            return Path.GetFullPath(value);
        }
    }

    /// <summary>Routes, authentication and the §17.2 handlers. Every route requires the credential; the account comes only from it.</summary>
    sealed class PracticeApi
    {
        const string Prefix = "/dev/practice/";
        readonly PracticeHost host;
        readonly ILogger log;
        public PracticeApi(PracticeHost host, ILogger log) { this.host = host; this.log = log; }

        public async Task Handle(HttpContext context)
        {
            Reply reply;
            try { reply = await Dispatch(context); }
            catch (ApiError error) { reply = Reply.Error(error.Status, error.Code); }
            catch (BadHttpRequestException error) { reply = error.StatusCode == 413 ? Reply.Error(413, "BODY_TOO_LARGE") : Reply.Error(400, "INVALID_REQUEST"); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                log.LogError("Unexpected {ExceptionType} in the dev practice host; the request was refused and nothing was committed", error.GetType().Name);
                reply = Reply.Error(500, "INTERNAL_ERROR");
            }
            byte[] bytes = Encoding.UTF8.GetBytes(reply.Body);
            var response = context.Response;
            response.StatusCode = reply.Status;
            response.ContentType = "application/json; charset=utf-8";
            response.Headers.CacheControl = "no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            if (reply.Status == 401) response.Headers.WWWAuthenticate = "Bearer";
            if (reply.Status == 503) response.Headers.RetryAfter = "1";
            response.ContentLength = bytes.Length;
            try { await response.Body.WriteAsync(bytes, context.RequestAborted); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        }

        async Task<Reply> Dispatch(HttpContext context)
        {
            var request = context.Request;
            var remote = context.Connection.RemoteIpAddress;
            if (remote == null || !IPAddress.IsLoopback(remote)) throw new ApiError(403, "LOOPBACK_ONLY");
            if (!request.Host.HasValue || !(request.Host.Host == "127.0.0.1" || request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))) throw new ApiError(403, "HOST_NOT_ALLOWED");
            var account = Authenticate(request);
            string path = request.Path.Value ?? "";
            if (!path.StartsWith(Prefix, StringComparison.Ordinal)) throw new ApiError(404, "NOT_FOUND");
            if (request.QueryString.HasValue) throw new ApiError(400, "INVALID_REQUEST");
            bool get = HttpMethods.IsGet(request.Method), post = HttpMethods.IsPost(request.Method);
            switch (path.Substring(Prefix.Length).Split('/'))
            {
                case ["account"]:
                    Method(get); return await Account(account);
                case ["progression"]:
                    Method(get); return await ProgressionRoutes.Read(host, account);
                case ["progression","support"]:
                    Method(post);return await SupportRoutes.Select(host,account,await Body(request));
                case ["weekly-bosses"]:
                    Method(post); return await WeeklyBossRoutes.Start(host,account,await Body(request));
                case ["encounters"]:
                    Method(post); return await GrowthRoutes.Start(host, account, await Body(request));
                case ["encounters", var encounter]:
                    Method(get); return await GrowthRoutes.Read(host, account, encounter);
                case ["encounters", var encounter, "commands"]:
                    Method(post); return await GrowthRoutes.Command(host, account, encounter, await Body(request));
                case ["encounters", var encounter, "battle-query"]:
                    Method(post); return await GrowthRoutes.Query(host, account, encounter, await Body(request));
                case ["encounters",var encounter,"support-query"]:
                    Method(post);return await GrowthRoutes.SupportQuery(host,account,encounter,await Body(request));
                case ["dungeons", "sweep"]:
                    Method(post); return await GrowthRoutes.Sweep(host, account, await Body(request));
                case ["progression", "characters", var character, "level-preview"]:
                    Method(post); return await GrowthRoutes.Level(host, account, character, await Body(request), true);
                case ["progression", "characters", var character, "level-up"]:
                    Method(post); return await GrowthRoutes.Level(host, account, character, await Body(request), false);
                case ["progression", "characters" or "weapons", var target, var action] when EquipmentRoutes.Match(path.Substring(Prefix.Length).Split('/')[1], action) is { } s3:
                    Method(post); return await EquipmentRoutes.Execute(host, account, s3.Kind, target, await Body(request), s3.Preview);
                case ["progression", "boss-shop", var action] when EquipmentRoutes.Match("boss-shop", action) is { } shop:
                    Method(post); return await EquipmentRoutes.Execute(host, account, shop.Kind, null, await Body(request), shop.Preview);
                case ["expeditions"]:
                    Method(post); return await ProgressionRoutes.Launch(host, account, await Body(request));
                case ["runs"]:
                    Method(post); return await Create(account, await Body(request));
                case ["runs", var id]:
                    Method(get); return await ReadRun(account, RunId(id));
                case ["runs", var id, "commands"]:
                    Method(post); return await Command(account, RunId(id), await Body(request));
                case ["runs", var id, "abandon"]:
                    Method(post); return await ProgressionRoutes.Abandon(host, account, RunId(id), await Body(request));
                case ["runs", var id, "memory-preview"]:
                    Method(post); return await Preview(account, RunId(id), await Body(request));
                case ["runs", var id, "memory-commit"]:
                    Method(post); return await Commit(account, RunId(id), await Body(request));
                case ["runs", var id, "battle-query"]:
                    Method(post); return await BattleQuery(account, RunId(id), await Body(request));
                case ["runs",var id,"support-query"]:
                    Method(post);return await SupportQuery(account,RunId(id),await Body(request));
                case ["recruitment"]:
                    Method(get); return await RecruitmentRead(account);
                case ["recruitment", "pulls"]:
                    Method(post); return await RecruitmentPull(account, await Body(request));
                case ["recruitment", "claim-launch"]:
                    Method(post); return await RecruitmentLaunch(account, await Body(request));
                case ["recruitment", "selector"]:
                    Method(post); return await RecruitmentSelector(account, await Body(request));
                default: throw new ApiError(404, "NOT_FOUND");
            }
        }

        PracticeAccount Authenticate(HttpRequest request)
        {
            var values = request.Headers.Authorization;
            if (values.Count != 1) throw new ApiError(401, "UNAUTHORIZED");
            string header = values[0] ?? "";
            const string scheme = "Bearer ";
            if (!header.StartsWith(scheme, StringComparison.Ordinal)) throw new ApiError(401, "UNAUTHORIZED");
            string token = header.Substring(scheme.Length);
            if (token.Length < 16 || token.Length > 512) throw new ApiError(401, "UNAUTHORIZED");
            return host.Fixture.Authenticate(token) ?? throw new ApiError(401, "UNAUTHORIZED");
        }

        Task<Reply> Account(PracticeAccount account) => host.Store.Read(account, (envelope, live) =>
        {
            var store = envelope?.Memory ?? AccountEnvelope.Fresh(account, host.Content).Memory;
            Dictionary<string, object?>? run = null;
            var record = envelope?.Run;
            if (record != null && live != null)
            {
                string digest = PracticeRunStore.StateDigest(live);
                run = GraphJson.Object("runId", record.RunId, "version", record.Journal.Count, "phase", live.State.Phase);
                PracticeRunStore.RequireUnchanged(live, digest);
            }
            return Reply.Json(200, PublicView.Account(account.Id, store, run, envelope?.Archive.Count ?? 0));
        });

        Task<Reply> ReadRun(PracticeAccount account, string runId) => host.Store.Read(account, (envelope, live) =>
        {
            var record = Current(envelope, runId);
            var run = live ?? throw new InvalidOperationException("current run was not replayed");
            string digest = PracticeRunStore.StateDigest(run);
            var view = PublicView.Run(run, runId, record.Journal.Count, host.Content.Rules);
            PracticeRunStore.RequireUnchanged(run, digest);
            return Reply.Json(200, GraphJson.Object("runId", runId, "version", record.Journal.Count, "phase", view["phase"],
                "memoryVersion", AccountEnvelope.MemoryVersion(envelope!.Memory), "state", view));
        });

        async Task<Reply> Create(PracticeAccount account, byte[] body)
        {
            var request = Contracts.ParseCreate(body);
            return await host.Store.Mutate(account, request.RequestId, request.Identity, (envelope, live) =>
            {
                GrowthCommands.NoEncounter(envelope);
                ProgressionCommands.EnsureLegacyAllowed(envelope);
                // §17.1: one run in progress per account; a finished (DONE) run moves to the archive whole.
                if (envelope.Run != null)
                {
                    if (live == null || live.State.Phase != "DONE") throw new ApiError(409, "RUN_ACTIVE");
                    envelope.Archive.Add(envelope.Run); envelope.Run = null;
                }
                string runId = "pr-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
                long seed = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4), 0);
                long memoryVersion = AccountEnvelope.MemoryVersion(envelope.Memory);
                object? startMemory = GraphJson.Copy(envelope.Memory["memory"]);
                Run run;
                try { run = Run.Create(host.Content.Data, account.Setup(seed, runId, memoryVersion, startMemory, request.UseMemory)); }
                catch (RuleException error) { throw new ApiError(422, error.Code); }
                catch (MemoryBoundaryException) { throw new ApiError(422, "COMMAND_REJECTED"); }
                string created = PracticeRunStore.StateDigest(run);
                envelope.Run = new RunRecord { RunId = runId, Seed = seed, UseMemory = request.UseMemory, MemoryVersion = memoryVersion, StartMemory = startMemory, CreatedSha256 = created };
                var reply = RunReplies.Created(run, runId, memoryVersion, host.Content.Rules);
                PracticeRunStore.RequireUnchanged(run, created);
                return Reply.Json(200, reply);
            });
        }

        async Task<Reply> Command(PracticeAccount account, string runId, byte[] body)
        {
            var request = Contracts.ParseCommand(body, runId);
            return await host.Store.Mutate(account, request.RequestId, request.Identity, (envelope, live) =>
                Execute(envelope, live, runId, request.ExpectedVersion, request.Kind, request.Args));
        }

        async Task<Reply> Commit(PracticeAccount account, string runId, byte[] body)
        {
            var request = Contracts.ParseCommit(body, runId);
            return await host.Store.Mutate(account, request.RequestId, request.Identity, (envelope, live) =>
                Execute(envelope, live, runId, request.ExpectedVersion, Contracts.MemoryCommit, request.Args));
        }

        // Pure: the preview runs on the replayed Run and nothing is written; the full-state digest must be unchanged.
        async Task<Reply> Preview(PracticeAccount account, string runId, byte[] body)
        {
            var request = Contracts.ParsePreview(body);
            return await host.Store.Read(account, (envelope, live) =>
            {
                var record = Current(envelope, runId);
                var run = live ?? throw new InvalidOperationException("current run was not replayed");
                string digest = PracticeRunStore.StateDigest(run);
                Dictionary<string, object?> preview;
                try { preview = run.PreviewMemory(request.SelectedChangeIds); }
                catch (RuleException error) { throw new ApiError(422, error.Code); }
                catch (MemoryBoundaryException) { throw new ApiError(422, "COMMAND_REJECTED"); }
                PracticeRunStore.RequireUnchanged(run, digest);
                return Reply.Json(200, GraphJson.Object("runId", runId, "version", record.Journal.Count, "preview", PublicView.Preview(preview)));
            });
        }

        // P1-PUBLIC-BATTLE-QUERY §17.4/§14.3/§14.7, pure: one card play's pick sheet and Core forecast on the replayed Run. Owner/run →
        // expectedVersion (the server journal version, not battle.v) → a live battle → Core's hand-source gate: a uid outside the hand gets
        // the one fixed NOT_IN_HAND before any definition, price or forecast of it is read. No receipt, journal entry, revision or row is
        // written and no command runs; the full-state digest must be unchanged. The default omitted-picks check (a hand card's
        // blocked[mode]) is never applied here: explicit picks/targets are forecast as sent.
        async Task<Reply> BattleQuery(PracticeAccount account, string runId, byte[] body)
        {
            var request = Contracts.ParseBattleQuery(body);
            return await host.Store.Read(account, (envelope, live) =>
            {
                var record = Current(envelope, runId);
                if (request.ExpectedVersion != record.Journal.Count) throw new ApiError(409, "STALE_VERSION");
                var run = live ?? throw new InvalidOperationException("current run was not replayed");
                string digest = PracticeRunStore.StateDigest(run);
                if (run.State.Battle == null) throw new ApiError(422, "NO_BATTLE");
                var choices = run.BattleHandChoices(request.Uid, request.Mode, request.Picks);
                if (choices.Error == "NOT_IN_HAND") throw new ApiError(422, "NOT_IN_HAND");
                var forecast = run.BattleForecast(request.Uid, request.Mode, request.Target, request.Picks);
                var reply = PublicView.BattleQuery(run, runId, record.Journal.Count, request, choices, forecast, host.Content.Data.Memory.Battle);
                PracticeRunStore.RequireUnchanged(run, digest);
                return Reply.Json(200, reply);
            });
        }

        async Task<Reply> SupportQuery(PracticeAccount account,string runId,byte[] body)
        {
            var q=Contracts.ParseSupportQuery(body);
            try{return await host.Store.Read(account,(envelope,live)=>
            {
                var record=Current(envelope,runId);if(record.Journal.Count!=q.ExpectedVersion)throw new ApiError(409,"STALE_VERSION");var run=live??throw new InvalidOperationException("run replay");
                var s=run.State;var b=s.Battle??throw new ApiError(422,"NO_BATTLE");string digest=PracticeRunStore.StateDigest(run);
                var info=run.BattleSupportInfo(q.Caller,q.Target,q.Picks);var choices=run.BattleSupportChoices(q.Caller,q.Picks);var forecast=run.BattleSupportForecast(q.Caller,q.Target,q.Picks);
                var view=PublicView.SupportQuery("runId",runId,record.Journal.Count,s.Phase,GraphJson.Object("node",s.Position,"turn",b.Turn,"battleVersion",b.V,"battlePhase",b.Phase),q,info,choices,forecast,b,run.BattleReservationSlots(),host.Content.Rules);
                PracticeRunStore.RequireUnchanged(run,digest);return Reply.Json(200,view);
            });}catch(RuleException e){throw new ApiError(422,e.Code);}catch(OverflowException){throw new ApiError(422,"BATTLE_NUMBER_OUT_OF_RANGE");}
        }

        // RECRUIT-SERVER-20261021 (Recruitment.cs). The read is pure: an account without an envelope or without a recruitment member is shown
        // empty (revision 0 / the envelope's revision) and nothing is created. Each mutation is one Mutate transaction with its own canonical
        // identity: a committed receipt first, then expectedRevision (409 STALE_REVISION), then the rules; no client account, seed, state,
        // wallet or result is accepted, and the balance is checked inside the transaction.
        Task<Reply> RecruitmentRead(PracticeAccount account) => host.Store.Read(account, (envelope, live) =>
            Reply.Json(200, RecruitmentCommands.Snapshot(host.Content.Recruitment, account.Id, envelope?.Revision ?? 0, envelope?.Recruitment)));

        async Task<Reply> RecruitmentPull(PracticeAccount account, byte[] body)
        {
            var request = Contracts.ParsePull(body);
            return await host.Store.Mutate(account, request.RequestId, request.Identity, (envelope, live) => RecruitmentCommands.Pull(host.Content.Recruitment, envelope, request));
        }

        async Task<Reply> RecruitmentLaunch(PracticeAccount account, byte[] body)
        {
            var request = Contracts.ParseLaunchClaim(body);
            return await host.Store.Mutate(account, request.RequestId, request.Identity, (envelope, live) => RecruitmentCommands.ClaimLaunch(host.Content.Recruitment, envelope, request));
        }

        async Task<Reply> RecruitmentSelector(PracticeAccount account, byte[] body)
        {
            var request = Contracts.ParseSelector(body);
            return await host.Store.Mutate(account, request.RequestId, request.Identity, (envelope, live) => RecruitmentCommands.ClaimSelector(host.Content.Recruitment, envelope, request));
        }

        // The command on the fresh replayed candidate: owner/run → version → Core → journal entry with the new state digest → public reply.
        Reply Execute(AccountEnvelope envelope, Run? live, string runId, int expectedVersion, string kind, Dictionary<string, object?> args)
        {
            var record = Current(envelope, runId);
            if (expectedVersion != record.Journal.Count) throw new ApiError(409, "STALE_VERSION");
            var run = live ?? throw new InvalidOperationException("current run was not replayed");
            CommandOutcome outcome;
            try { outcome = RunCommands.Execute(run, runId, kind, args, envelope.Memory); }
            catch (RuleException error) { throw new ApiError(422, error.Code); }
            catch (MemoryBoundaryException) { throw new ApiError(422, "COMMAND_REJECTED"); }
            if (outcome.NextMemory != null) envelope.Memory = outcome.NextMemory;
            string digest = PracticeRunStore.StateDigest(run);
            record.Journal.Add(new JournalEntry { Kind = kind, Args = args, StateSha256 = digest });
            if (kind == Contracts.MemoryCommit)
                ProgressionCommands.AfterMemoryCommit(envelope, run, record, (string)args["requestId"]!, host.Content);
            int version = record.Journal.Count;
            var reply = RunReplies.Executed(run, runId, version, kind, outcome, AccountEnvelope.MemoryVersion(envelope.Memory), host.Content.Rules);
            PracticeRunStore.RequireUnchanged(run, digest);
            return Reply.Json(200, reply);
        }

        // Ownership: only the authenticated account's current run exists; any other runId (another account's included) is 404.
        static RunRecord Current(AccountEnvelope? envelope, string runId)
        {
            var record = envelope?.Run;
            return record != null && record.RunId == runId ? record : throw new ApiError(404, "RUN_NOT_FOUND");
        }
        static string RunId(string id) => PracticeRunStore.IsRunId(id) ? id : throw new ApiError(404, "RUN_NOT_FOUND");
        static void Method(bool allowed) { if (!allowed) throw new ApiError(405, "METHOD_NOT_ALLOWED"); }

        static async Task<byte[]> Body(HttpRequest request)
        {
            string? type = request.ContentType;
            if (type == null || !(type.Equals("application/json", StringComparison.OrdinalIgnoreCase) || type.StartsWith("application/json;", StringComparison.OrdinalIgnoreCase)))
                throw new ApiError(415, "UNSUPPORTED_MEDIA_TYPE");
            if (request.ContentLength > Contracts.MaxBodyBytes) throw new ApiError(413, "BODY_TOO_LARGE");
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int read;
            while ((read = await request.Body.ReadAsync(chunk.AsMemory(0, chunk.Length), request.HttpContext.RequestAborted)) > 0)
            {
                if (buffer.Length + read > Contracts.MaxBodyBytes) throw new ApiError(413, "BODY_TOO_LARGE");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
    }

    /// <summary>The public reply of a create and of a committed command (its receipt body), member for member. The live handlers call it on
    /// the fresh candidate; AccountStoreVerifier calls it at every reconstructed checkpoint with that checkpoint's historical memory version,
    /// so a stored run receipt is proven byte for byte by the same projection, never by a copy of it.</summary>
    static class RunReplies
    {
        public static Dictionary<string, object?> Created(Run run, string runId, long memoryVersion, BattleRules rules)
        {
            var view = PublicView.Run(run, runId, 0, rules);
            return GraphJson.Object("runId", runId, "version", 0, "phase", view["phase"], "memoryVersion", memoryVersion, "state", view);
        }

        /// <summary>version: the journal length after the command; memoryVersion: the store's version after it (a save advances it).</summary>
        public static Dictionary<string, object?> Executed(Run run, string runId, int version, string kind, CommandOutcome outcome, long memoryVersion, BattleRules rules)
        {
            var view = PublicView.Run(run, runId, version, rules);
            var reply = GraphJson.Object("runId", runId, "version", version, "phase", view["phase"], "memoryVersion", memoryVersion);
            if (outcome.Events != null) reply.Add("events", PublicView.Events(outcome.Events));
            if (kind == "forgeOffer") reply.Add("result", PublicView.Offer(outcome.Result));
            if (kind == Contracts.MemoryCommit) reply.Add("result", PublicView.Commit(outcome.Result));
            reply.Add("state", view);
            return reply;
        }
    }
}
