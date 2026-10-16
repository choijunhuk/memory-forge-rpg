// P1-LOCAL-AUTHORITATIVE-PERSISTENCE §17.2/§17.5/§14.7/§16.3: an account's durable practice state as one private envelope file, replaced
// atomically. The live Run is never stored or restored: every request rebuilds it by real Run.Create from the recorded server-chosen inputs
// and replays the version-pinned public-command journal, checking the full-state digest after Create and after every command. A mutation
// runs on that fresh, isolated candidate under the in-process gate and the account's permanent OS lock file (FileShare.None, bounded wait;
// the lock file is never deleted and the replaceable data file is never locked). A rule, I/O or unexpected failure discards the candidate
// and leaves the committed file, run version, memory store and receipts exactly as they were. Receipts are checked before any version
// check and return their original response bytes. Unknown, corrupt, tampered or differently bound envelopes are refused and left
// untouched. An envelope above MaxEnvelopeBytes (512 MiB, the dev-store boundary) is refused on load and never written. Durability scope:
// the complete new envelope is written to a private temp file, flushed with Flush(flushToDisk: true), then renamed over the old one in
// the same directory, and the reply is sent only after that rename. That covers a crash of this process (the file is either the previous
// or the new complete envelope). The directory entry is not fsynced and macOS F_FULLFSYNC is not used, so nothing is claimed for power or
// storage failure: afterwards the store needs inspection and recovery, and a client retry is not guaranteed to be safe (a lost create
// receipt, for instance, would choose a new seed and runId).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MemoryForge.Core;

namespace MemoryForge.Server
{
    sealed class EnvelopeBinding
    {
        public string Replay = "", RulesVersion = "", ContentSha256 = "", CoreModule = "", FixtureSha256 = "";
        public static EnvelopeBinding Current(PracticeAccount account, PracticeContent content) => new EnvelopeBinding
        {
            Replay = PracticeContent.ReplayFormat, RulesVersion = content.RulesVersion, ContentSha256 = content.ContentSha256,
            CoreModule = content.CoreModule, FixtureSha256 = account.FixtureSha256
        };
        public Dictionary<string, object?> ToGraph() => GraphJson.Object("replay", Replay, "rulesVersion", RulesVersion, "contentSha256", ContentSha256,
            "coreModule", CoreModule, "fixtureSha256", FixtureSha256);
        public static EnvelopeBinding Parse(JsonElement json)
        {
            BattleDataLoader.Fields(json, "replay", "rulesVersion", "contentSha256", "coreModule", "fixtureSha256");
            return new EnvelopeBinding { Replay = Envelopes.Text(json, "replay"), RulesVersion = Envelopes.Text(json, "rulesVersion"),
                ContentSha256 = Envelopes.Text(json, "contentSha256"), CoreModule = Envelopes.Text(json, "coreModule"), FixtureSha256 = Envelopes.Text(json, "fixtureSha256") };
        }
    }

    /// <summary>One committed public command: its kind, CheckArgs-normalized arguments and the SHA-256 of the full Run state after it.</summary>
    sealed class JournalEntry
    {
        public string Kind = "", StateSha256 = "";
        public Dictionary<string, object?> Args = new Dictionary<string, object?>(StringComparer.Ordinal);
        public Dictionary<string, object?> ToGraph() => GraphJson.Object("kind", Kind, "args", Args, "sha256", StateSha256);
        public static JournalEntry Parse(JsonElement json)
        {
            BattleDataLoader.Fields(json, "kind", "args", "sha256");
            return new JournalEntry { Kind = Envelopes.Text(json, "kind"), StateSha256 = Envelopes.Hex(json, "sha256"),
                Args = MemoryV2DataLoader.Graph(json.GetProperty("args")) as Dictionary<string, object?> ?? throw new InvalidDataException("journal args") };
        }
    }

    /// <summary>The server-chosen Run.Create inputs (private: seed and start memory are never returned) and the journal.</summary>
    sealed class RunRecord
    {
        public string RunId = "", CreatedSha256 = "";
        public long Seed, MemoryVersion;
        public bool UseMemory;
        public object? StartMemory;
        public List<JournalEntry> Journal = new List<JournalEntry>();
        public Dictionary<string, object?> ToGraph() => GraphJson.Object("runId", RunId, "seed", Seed, "useMemory", UseMemory, "memoryVersion", MemoryVersion,
            "startMemory", StartMemory, "createdSha256", CreatedSha256, "journal", Journal.Select(e => (object?)e.ToGraph()).ToList());
        public static RunRecord Parse(JsonElement json)
        {
            BattleDataLoader.Fields(json, "runId", "seed", "useMemory", "memoryVersion", "startMemory", "createdSha256", "journal");
            var record = new RunRecord
            {
                RunId = Envelopes.Text(json, "runId"), Seed = json.GetProperty("seed").GetInt64(), MemoryVersion = json.GetProperty("memoryVersion").GetInt64(),
                UseMemory = json.GetProperty("useMemory").GetBoolean(), StartMemory = MemoryV2DataLoader.Graph(json.GetProperty("startMemory")),
                CreatedSha256 = Envelopes.Hex(json, "createdSha256")
            };
            if (!PracticeRunStore.IsRunId(record.RunId) || record.Seed < 0 || record.Seed > uint.MaxValue || record.MemoryVersion < 0) throw new InvalidDataException("run record");
            foreach (var entry in json.GetProperty("journal").EnumerateArray()) record.Journal.Add(JournalEntry.Parse(entry));
            return record;
        }
    }

    sealed class StoredReceipt
    {
        public string Identity = "", Body = "";
        public int Status;
        public Dictionary<string, object?> ToGraph() => GraphJson.Object("identity", Identity, "status", Status, "body", Body);
        public static StoredReceipt Parse(JsonElement json)
        {
            BattleDataLoader.Fields(json, "identity", "status", "body");
            var receipt = new StoredReceipt { Identity = Envelopes.Hex(json, "identity"), Status = json.GetProperty("status").GetInt32(), Body = Envelopes.Text(json, "body") };
            if (receipt.Status != 200) throw new InvalidDataException("only committed successes are receipts");
            return receipt;
        }
    }

    /// <summary>The whole private account file: binding, the existing v2 memory store, the current run, finished runs (kept whole) and
    /// every committed request's receipt. No history is truncated and no command count is capped.</summary>
    sealed class AccountEnvelope
    {
        public string AccountId = "";
        public EnvelopeBinding Binding = new EnvelopeBinding();
        public long Revision;
        public Dictionary<string, object?> Memory = new Dictionary<string, object?>(StringComparer.Ordinal);
        public RunRecord? Run;
        public List<RunRecord> Archive = new List<RunRecord>();
        public Dictionary<string, StoredReceipt> Receipts = new Dictionary<string, StoredReceipt>(StringComparer.Ordinal);

        /// <summary>A new account: the same empty v2 store the practice client starts from (Migrate of no legacy decks), version 0.</summary>
        public static AccountEnvelope Fresh(PracticeAccount account, PracticeContent content) => new AccountEnvelope
        {
            AccountId = account.Id, Binding = EnvelopeBinding.Current(account, content),
            Memory = MemoryV2.Migrate(content.Data.Memory, new Dictionary<string, object?>(StringComparer.Ordinal), new Dictionary<string, object?>(StringComparer.Ordinal)).ToJsonGraph()
        };
        public static long MemoryVersion(IDictionary<string, object?> store)
        {
            store.TryGetValue("version", out object? version);
            return version is long n ? n : version is int i ? i : throw new InvalidDataException("memory store version");
        }
    }

    /// <summary>File format: {"schema", "sha256": SHA-256 of the UTF-8 body text, "body": the account JSON as text}.</summary>
    static class Envelopes
    {
        public const string Schema = "mf-dev-practice-account-1";

        public static byte[] Encode(AccountEnvelope envelope)
        {
            var body = GraphJson.Object("accountId", envelope.AccountId, "binding", envelope.Binding.ToGraph(), "revision", envelope.Revision,
                "memory", envelope.Memory, "run", envelope.Run?.ToGraph(), "archive", envelope.Archive.Select(r => (object?)r.ToGraph()).ToList(),
                "receipts", envelope.Receipts.ToDictionary(p => p.Key, p => (object?)p.Value.ToGraph(), StringComparer.Ordinal));
            string text = GraphJson.Text(body, false);
            return GraphJson.Bytes(GraphJson.Object("schema", Schema, "sha256", GraphJson.Sha256(Encoding.UTF8.GetBytes(text)), "body", text), false);
        }

        public static AccountEnvelope Decode(byte[] bytes, PracticeAccount account, PracticeContent content)
        {
            string schema, sha, text;
            try
            {
                using var outer = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
                var root = outer.RootElement;
                BattleDataLoader.Fields(root, "schema", "sha256", "body");
                schema = Text(root, "schema"); sha = Text(root, "sha256"); text = Text(root, "body");
            }
            catch (Exception) { throw new ApiError(500, "STORE_CORRUPT"); }
            if (schema != Schema) throw new ApiError(409, "STORE_UNSUPPORTED");
            if (sha != GraphJson.Sha256(Encoding.UTF8.GetBytes(text))) throw new ApiError(500, "STORE_CORRUPT");
            AccountEnvelope envelope;
            try
            {
                using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 256 });
                envelope = Body(document.RootElement);
            }
            catch (Exception) { throw new ApiError(500, "STORE_CORRUPT"); }
            if (envelope.AccountId != account.Id) throw new ApiError(500, "STORE_CORRUPT");
            var binding = envelope.Binding;
            if (binding.Replay != PracticeContent.ReplayFormat) throw new ApiError(409, "STORE_UNSUPPORTED");
            if (binding.RulesVersion != content.RulesVersion || binding.ContentSha256 != content.ContentSha256) throw new ApiError(409, "STORE_CONTENT_MISMATCH");
            if (binding.CoreModule != content.CoreModule) throw new ApiError(409, "STORE_IMPLEMENTATION_MISMATCH");
            if (binding.FixtureSha256 != account.FixtureSha256) throw new ApiError(409, "STORE_FIXTURE_MISMATCH");
            return envelope;
        }

        static AccountEnvelope Body(JsonElement root)
        {
            BattleDataLoader.Fields(root, "accountId", "binding", "revision", "memory", "run", "archive", "receipts");
            var envelope = new AccountEnvelope
            {
                AccountId = Text(root, "accountId"), Binding = EnvelopeBinding.Parse(root.GetProperty("binding")), Revision = root.GetProperty("revision").GetInt64(),
                Memory = MemoryStore(root.GetProperty("memory")),
                Run = root.GetProperty("run").ValueKind == JsonValueKind.Null ? null : RunRecord.Parse(root.GetProperty("run"))
            };
            foreach (var record in root.GetProperty("archive").EnumerateArray()) envelope.Archive.Add(RunRecord.Parse(record));
            foreach (var receipt in root.GetProperty("receipts").EnumerateObject()) envelope.Receipts.Add(BattleDataLoader.Name(receipt), StoredReceipt.Parse(receipt.Value));
            return envelope;
        }
        // The existing mf-memory-v2 store shape Core's CommitMemory requires; Core re-validates the decks themselves.
        static Dictionary<string, object?> MemoryStore(JsonElement json)
        {
            var store = MemoryV2DataLoader.Graph(json) as Dictionary<string, object?> ?? throw new InvalidDataException("memory store");
            if (!(store.TryGetValue("schema", out object? schema) && schema is string name && name == "mf-memory-v2") || !(store.TryGetValue("version", out object? version) && version is long n && n >= 0)
                || !(store.TryGetValue("memory", out object? memory) && memory is Dictionary<string, object?>) || !(store.TryGetValue("history", out object? history) && history is List<object?>)
                || !(store.TryGetValue("receipts", out object? receipts) && receipts is Dictionary<string, object?>)) throw new InvalidDataException("memory store");
            return store;
        }
        public static string Text(JsonElement json, string name)
        {
            var value = json.GetProperty(name);
            if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException(name);
            return value.GetString() ?? throw new InvalidDataException(name);
        }
        public static string Hex(JsonElement json, string name)
        {
            string value = Text(json, name);
            if (value.Length != 64 || !value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) throw new InvalidDataException(name);
            return value;
        }
    }

    /// <summary>What a command produced beyond the Run itself: battle events, a returned value, or the next memory store of a save.</summary>
    sealed class CommandOutcome
    {
        public List<BattleEvent>? Events;
        public object? Result;
        public Dictionary<string, object?>? NextMemory;
    }

    /// <summary>Fixed journal kind → existing public Run method, identical for a live request and a replayed entry. Arguments are already
    /// CheckArgs-normalized. Nothing here restores state, sets RNG, derives stats or bypasses a Core check.</summary>
    static class RunCommands
    {
        public static CommandOutcome Execute(Run run, string runId, string kind, IReadOnlyDictionary<string, object?> args, Dictionary<string, object?> memoryStore)
        {
            var outcome = new CommandOutcome();
            switch (kind)
            {
                case "enter": run.Enter(Text(args, "nodeId")); break;
                case "play": outcome.Events = run.BattleCommand("play", Arguments(args, "uid", "mode", "target", "picks")).Events; break;
                case "ult": outcome.Events = run.BattleCommand("ult", Arguments(args, "actor", "target", "picks")).Events; break;
                case "end": outcome.Events = run.BattleCommand("end").Events; break;
                case "takeCard": run.TakeCard(Text(args, "id")); break;
                case "skipCard": run.SkipCard(); break;
                case "takeRelic": run.TakeRelic(Text(args, "id")); break;
                case "takePart": run.TakePart(); break;
                case "evolve": run.Evolve(Text(args, "key"), Text(args, "branchId")); break;
                case "proceed": run.Proceed(); break;
                case "archive": run.Archive(Text(args, "choice")); break;
                case "rest": run.Rest(Text(args, "choice"), Optional(args, "key")); break;
                case "forgeOffer": outcome.Result = run.ForgeOffer(Text(args, "key")); break;
                case "forgeApply": run.ForgeApply(Text(args, "partId")); break;
                case "forgeRemove": run.ForgeRemove(Text(args, "key")); break;
                case "applyPart": run.ApplyFoundPart(Text(args, "partId"), Text(args, "key")); break;
                case "buy": run.Buy(Text(args, "kind"), Optional(args, "id"), Optional(args, "key")); break;
                case "event": run.EventChoose(checked((int)(long)args["index"]!), args.TryGetValue("keys", out object? keys) ? GraphJson.Copy(keys) : null); break;
                case "assign": run.Assign(Text(args, "key"), Text(args, "charId")); break;
                case Contracts.MemoryCommit:
                {
                    var request = GraphJson.Object("requestId", Text(args, "requestId"), "expectedMemoryVersion", args["expectedMemoryVersion"], "runId", runId,
                        "selectedChangeIds", GraphJson.Copy(args["selectedChangeIds"]));
                    var committed = run.CommitMemory(request, memoryStore);
                    outcome.Result = committed["response"];
                    outcome.NextMemory = committed["state"] as Dictionary<string, object?> ?? throw new InvalidDataException("a v2 commit returned no store");
                    break;
                }
                default: throw new ApiError(400, "UNSUPPORTED_COMMAND");
            }
            return outcome;
        }
        static string Text(IReadOnlyDictionary<string, object?> args, string name) => args.TryGetValue(name, out object? value) && value is string text ? text
            : throw new ApiError(400, "INVALID_ARGS");
        static string? Optional(IReadOnlyDictionary<string, object?> args, string name) => args.TryGetValue(name, out object? value) ? value as string : null;
        static BattleArgument Arguments(IReadOnlyDictionary<string, object?> args, params string[] names)
        {
            var members = new List<KeyValuePair<string, BattleArgument>>();
            foreach (string name in names)
            {
                if (!args.TryGetValue(name, out object? value) || value == null) continue;
                var argument = value is System.Collections.IList list
                    ? BattleArgument.Array(list.Cast<object?>().Select(item => BattleArgument.String(item as string ?? throw new ApiError(400, "INVALID_ARGS"))))
                    : BattleArgument.String(value as string ?? throw new ApiError(400, "INVALID_ARGS"));
                members.Add(new KeyValuePair<string, BattleArgument>(name, argument));
            }
            return BattleArgument.Object(members);
        }
    }

    sealed class PracticeRunStore
    {
        const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        const UnixFileMode GroupOrOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
        /// <summary>The dev-store boundary for one account file, enforced identically on load and before any write (RequireWithinLimit).
        /// Growing past it needs the reviewed database/migration work, never history truncation.</summary>
        public const long MaxEnvelopeBytes = 512L * 1024 * 1024;
        readonly string directory;
        readonly PracticeContent content;
        // One Core execution at a time in this process (shared RunData); the per-account OS lock serializes writers across processes.
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

        public PracticeRunStore(string directory, PracticeContent content) { this.directory = directory; this.content = content; }

        public static bool IsRunId(string id) => id.Length == 35 && id.StartsWith("pr-", StringComparison.Ordinal) && id.Skip(3).All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        public static string StateDigest(Run run) => GraphJson.Digest(run.State.ToJsonGraph());
        /// <summary>Guards query purity: the public view and preview must leave the full Run state (RNG included) exactly as it was.</summary>
        public static void RequireUnchanged(Run run, string digest)
        {
            if (StateDigest(run) != digest) throw new InvalidOperationException("A read-only query changed the Run state");
        }

        /// <summary>Startup: an absolute directory outside the content root (repository), not a link, owner-only (created 0700).</summary>
        public static string PrepareDirectory(string path, string contentRoot)
        {
            var comparison = OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentRoot));
            if (full.Equals(root, comparison) || full.StartsWith(root + Path.DirectorySeparatorChar, comparison)) throw new StartupRefusal("the private store must be outside the content root (repository)");
            if (File.Exists(full)) throw new StartupRefusal("the private store path is a file");
            var info = new DirectoryInfo(full);
            try
            {
                if (info.Exists)
                {
                    if (info.LinkTarget != null) throw new StartupRefusal("the private store must not be a symbolic link");
                    if ((File.GetUnixFileMode(full) & GroupOrOther) != 0) throw new StartupRefusal("the private store directory must not be accessible to group/other (chmod 700)");
                }
                else Directory.CreateDirectory(full, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { throw new StartupRefusal("the private store directory cannot be prepared"); }
            return full;
        }

        string EnvelopePath(PracticeAccount account) => Path.Combine(directory, "account-" + account.Id + ".json");
        string LockPath(PracticeAccount account) => Path.Combine(directory, "account-" + account.Id + ".lock");

        /// <summary>A pure read: no lock, no write. The envelope is replaced atomically, so this sees one complete committed revision.</summary>
        public async Task<Reply> Read(PracticeAccount account, Func<AccountEnvelope?, Run?, Reply> read)
        {
            await EnterGate();
            try
            {
                var envelope = Load(account);
                Run? live = envelope != null && envelope.Run != null ? Replay(account, envelope) : null;
                return read(envelope, live);
            }
            finally { gate.Release(); }
        }

        /// <summary>§17.2 under the account lock: receipt (same identity → stored reply, else 409) → fresh replay → the command on that isolated
        /// candidate → receipt + revision → atomic replace → reply. Any exception before the replace commits nothing.</summary>
        public async Task<Reply> Mutate(PracticeAccount account, string requestId, string identity, Func<AccountEnvelope, Run?, Reply> mutate)
        {
            await EnterGate();
            try
            {
                using (await LockAccount(account))
                {
                    var envelope = Load(account);
                    if (envelope != null && envelope.Receipts.TryGetValue(requestId, out var receipt))
                    {
                        if (receipt.Identity != identity) throw new ApiError(409, "IDEMPOTENCY_CONFLICT");
                        return new Reply(receipt.Status, receipt.Body);
                    }
                    envelope ??= AccountEnvelope.Fresh(account, content);
                    var live = envelope.Run == null ? null : Replay(account, envelope);
                    var reply = mutate(envelope, live);
                    envelope.Receipts.Add(requestId, new StoredReceipt { Identity = identity, Status = reply.Status, Body = reply.Body });
                    envelope.Revision = checked(envelope.Revision + 1);
                    Persist(account, envelope);
                    return reply;
                }
            }
            finally { gate.Release(); }
        }

        /// <summary>Real Run.Create from the recorded inputs, then every journal command through RunCommands, each step's full-state digest
        /// compared. Any divergence or failure refuses the account (STORE_REPLAY_MISMATCH) and nothing is rewritten.</summary>
        Run Replay(PracticeAccount account, AccountEnvelope envelope)
        {
            var record = envelope.Run ?? throw new InvalidOperationException("No run to replay");
            Run run;
            try
            {
                run = Run.Create(content.Data, account.Setup(record.Seed, record.RunId, record.MemoryVersion, record.StartMemory, record.UseMemory));
                if (StateDigest(run) != record.CreatedSha256) throw new InvalidDataException("created state differs");
                foreach (var entry in record.Journal)
                {
                    var outcome = RunCommands.Execute(run, record.RunId, entry.Kind, Contracts.CheckArgs(entry.Kind, entry.Args), envelope.Memory);
                    // A replayed save must take Core's own receipt path: the committed store comes back unchanged.
                    if (outcome.NextMemory != null && GraphJson.Digest(outcome.NextMemory) != GraphJson.Digest(envelope.Memory)) throw new InvalidDataException("replayed save changed the store");
                    if (StateDigest(run) != entry.StateSha256) throw new InvalidDataException("replayed state differs");
                }
            }
            catch (Exception) { throw new ApiError(500, "STORE_REPLAY_MISMATCH"); }
            return run;
        }

        AccountEnvelope? Load(PracticeAccount account)
        {
            string path = EnvelopePath(account);
            byte[] bytes;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return null;
                if (info.LinkTarget != null) throw new ApiError(500, "STORE_CORRUPT");
                RequireWithinLimit(info.Length);
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { throw new ApiError(500, "STORE_READ_FAILED"); }
            return Envelopes.Decode(bytes, account, content);
        }

        /// <summary>The one size guard of load and write: above the boundary the account is refused (507) and nothing is touched; the
        /// committed file, receipts and version stay as they were and no history is dropped to make room.</summary>
        static void RequireWithinLimit(long length)
        {
            if (length > MaxEnvelopeBytes) throw new ApiError(507, "STORE_TOO_LARGE");
        }

        // Private temp file in the same directory, written and flushed (fsync), then renamed over the envelope (atomic replace).
        // The size guard runs before the temp file exists, so an oversized result never touches the committed envelope.
        void Persist(PracticeAccount account, AccountEnvelope envelope)
        {
            byte[] bytes = Envelopes.Encode(envelope);
            RequireWithinLimit(bytes.Length);
            string temp = Path.Combine(directory, ".account-" + account.Id + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)) + ".tmp");
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, UnixCreateMode = PrivateFile };
                using (var stream = new FileStream(temp, options))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temp, EnvelopePath(account), overwrite: true);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception cleanup) when (cleanup is IOException || cleanup is UnauthorizedAccessException) { }
                throw new ApiError(500, "STORE_WRITE_FAILED");
            }
        }

        // The permanent per-account lock file, opened with FileShare.None (flock on macOS/Linux): exclusive across threads and processes.
        async Task<FileStream> LockAccount(PracticeAccount account)
        {
            var waited = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    return new FileStream(LockPath(account), new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None, UnixCreateMode = PrivateFile });
                }
                catch (UnauthorizedAccessException) { throw new ApiError(500, "STORE_LOCK_FAILED"); }
                catch (IOException) when (waited.Elapsed < Wait) { await Task.Delay(10); }
                catch (IOException) { throw new ApiError(503, "ACCOUNT_BUSY"); }
            }
        }
        async Task EnterGate()
        {
            if (!await gate.WaitAsync(Wait)) throw new ApiError(503, "ACCOUNT_BUSY");
        }
    }
}
