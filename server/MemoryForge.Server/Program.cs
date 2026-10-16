// P1-LOCAL-AUTHORITATIVE-PERSISTENCE §16.5/§17.2-17.5/§18.1/§20.9 P1-2: the LOCAL DEVELOPMENT practice host. It is not the production
// API of server/openapi.yaml: explicit /dev/practice routes, Development environment only, Kestrel bound to 127.0.0.1 only (configured
// Kestrel endpoints refuse startup, the default reloadable Kestrel configuration loader is replaced by an empty non-reloading one so no
// configuration change can open another socket later, and any other bound address at start stops the host), a bearer credential matched in constant time against the trusted fixture's SHA-256 digests, no CORS,
// no anonymous route, no static files, no query parameters. Configuration (command line --MemoryForge:Key=value or MemoryForge__Key):
// ContentRoot (repository with data/game_data.json), PracticeFixture (owner-only fixture file), PracticeStore (owner-only directory
// outside the content root) and PracticePort (0 = ephemeral). A missing or invalid value refuses startup with exit code 2. Logs carry
// only exception type names: never credentials, seeds, RNG, private state or exception messages.
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
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(options => { options.SingleLine = true; options.IncludeScopes = false; });
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            PracticeHost host;
            try { host = PracticeHost.Configure(builder.Environment, builder.Configuration); }
            catch (StartupRefusal refusal) { Console.Error.WriteLine("MemoryForge dev practice server refused to start: " + refusal.Message); return 2; }
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
            var api = new PracticeApi(host, app.Logger);
            app.Run(api.Handle);
            try { await app.StartAsync(); }
            catch (Exception error) { Console.Error.WriteLine("MemoryForge dev practice server refused to start: the loopback port could not be bound (" + error.GetType().Name + ")"); return 2; }
            var addresses = app.Urls.ToList();
            if (addresses.Count != 1 || !addresses.All(IsLoopback))
            {
                Console.Error.WriteLine("MemoryForge dev practice server stopped: it was bound outside 127.0.0.1");
                await app.StopAsync();
                return 3;
            }
            Console.Out.WriteLine(Listening + addresses[0] + " (Development, loopback only)");
            Console.Out.Flush();
            await app.WaitForShutdownAsync();
            return 0;
        }

        static bool IsLoopback(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp
            && IPAddress.TryParse(uri.Host, out var ip) && ip.Equals(IPAddress.Loopback);
    }

    sealed class PracticeHost
    {
        public PracticeContent Content { get; }
        public PracticeFixture Fixture { get; }
        public PracticeRunStore Store { get; }
        public int Port { get; }
        PracticeHost(PracticeContent content, PracticeFixture fixture, PracticeRunStore store, int port) { Content = content; Fixture = fixture; Store = store; Port = port; }

        public static PracticeHost Configure(IHostEnvironment environment, IConfiguration configuration)
        {
            if (!environment.IsDevelopment()) throw new StartupRefusal("only the Development environment is supported; there is no production mode or fallback");
            if (configuration.GetSection("Kestrel:Endpoints").GetChildren().Any()) throw new StartupRefusal("Kestrel endpoint configuration is not accepted; the host binds 127.0.0.1 only");
            string contentRoot = AbsolutePath(configuration, "MemoryForge:ContentRoot");
            string fixture = AbsolutePath(configuration, "MemoryForge:PracticeFixture");
            string store = AbsolutePath(configuration, "MemoryForge:PracticeStore");
            if (!int.TryParse(configuration["MemoryForge:PracticePort"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int port) || port > 65535)
                throw new StartupRefusal("MemoryForge:PracticePort must be 0..65535 (0 = ephemeral)");
            if (!Directory.Exists(contentRoot)) throw new StartupRefusal("MemoryForge:ContentRoot is not a directory");
            var content = PracticeContent.Load(contentRoot);
            var practiceFixture = PracticeFixture.Load(fixture, content);
            string directory = PracticeRunStore.PrepareDirectory(store, contentRoot);
            return new PracticeHost(content, practiceFixture, new PracticeRunStore(directory, content), port);
        }
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
                case ["runs"]:
                    Method(post); return await Create(account, await Body(request));
                case ["runs", var id]:
                    Method(get); return await ReadRun(account, RunId(id));
                case ["runs", var id, "commands"]:
                    Method(post); return await Command(account, RunId(id), await Body(request));
                case ["runs", var id, "memory-preview"]:
                    Method(post); return await Preview(account, RunId(id), await Body(request));
                case ["runs", var id, "memory-commit"]:
                    Method(post); return await Commit(account, RunId(id), await Body(request));
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
                var view = PublicView.Run(run, runId, 0, host.Content.Rules);
                PracticeRunStore.RequireUnchanged(run, created);
                return Reply.Json(200, GraphJson.Object("runId", runId, "version", 0, "phase", view["phase"], "memoryVersion", memoryVersion, "state", view));
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
            int version = record.Journal.Count;
            var view = PublicView.Run(run, runId, version, host.Content.Rules);
            var reply = GraphJson.Object("runId", runId, "version", version, "phase", view["phase"], "memoryVersion", AccountEnvelope.MemoryVersion(envelope.Memory));
            if (outcome.Events != null) reply.Add("events", PublicView.Events(outcome.Events));
            if (kind == "forgeOffer") reply.Add("result", PublicView.Offer(outcome.Result));
            if (kind == Contracts.MemoryCommit) reply.Add("result", PublicView.Commit(outcome.Result));
            reply.Add("state", view);
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
}
