// P1-PRACTICE-COMMAND-OUTBOX §14.7/§17.2/§17.4: the Development practice client's one durable, exact, unresolved mutating request.
// Run mutations and the three recruitment mutations are staged here
// before it is first sent, so closing the client after a send never regenerates its requestId or body: an uncertain commit is retried
// with the same bytes instead of becoming a new action. At most one request is pending per configured file. While it is pending, the same
// exact request reloads/restages unchanged and any other request (or its requestId with another route or body) is refused, the record
// kept. Only Confirm (a validated successful response) or ConfirmNotCommitted (a validated refusal the server guarantees committed
// nothing) clears it, for exactly the current request, after the owning transport validated that response to a send of it. Nothing
// else clears it: no timeout, disconnect, 503 STORE_COMMIT_UNKNOWN, focus/pause/disable, restart or current-state GET.
// This is not a transport, UI, queue, credential store or authority: the server judges every command, nothing here reads Core state,
// and unsent prepared intents and real reservations/AP stay where they are.
// Files (the record path is explicit, trusted Development configuration; no account or request value becomes part of a path):
// - <file>: {"schema":"mf-dev-practice-outbox-1","sha256":<SHA-256 hex of the UTF-8 record text>,"record":<record text>}, the record
//   text being {"endpoint","account","method","route","requestId","body"} (strings, in that order). Both levels are ASCII: '"' and '\'
//   are backslash-escaped and every other character outside 0x20..0x7E is a \u escape (lowercase hex). A file is accepted only as
//   exactly that canonical encoding of a valid record bound to this endpoint and account. The checksum detects accidental corruption,
//   not tampering. No credential, seed, RNG or Core state is stored.
// - <file>.lock: the permanent lock file, never deleted, replaced or written. Each Load/Stage/Confirm opens it with FileShare.None
//   (same-process exclusion; Mono enforces FileShare only inside one process) and holds a checked native flock(LOCK_EX|LOCK_NB) on that
//   descriptor (exclusion between processes, verified by Root on macOS Mono/.NET) until the operation ends. Contention is OUTBOX_BUSY at
//   once; there is no wait or spin. Platforms other than macOS/Linux, or any native failure, fail closed before the record is touched.
// - .<file name>.<random>.tmp: the private temp file of one Stage, created new, set to owner-only 0600 by a checked fchmod on its
//   descriptor before any byte is written, written, Flush(true)ed and moved to <file> by a move that never overwrites (so a newly
//   published record is 0600). A temp file is never read or promoted. Existing records and the lock file keep whatever mode they have.
// JSON strings are checked on the original text for escapes and well-formed UTF-16 (no lone or misordered surrogate, escaped or raw)
// before Newtonsoft reads them. A corrupt, truncated, unknown, oversized or differently bound record is refused and kept byte for byte;
// nothing here resets, quarantines, migrates or deletes it. Durability covers a crash of this process (the record is absent or complete); the directory
// entry is not fsynced, so nothing is claimed for power loss.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;

namespace MemoryForge.Client
{
    /// <summary>A refused or failed outbox operation. Code is one of the constants below; the message carries no body, path or secret.</summary>
    public sealed class PracticeOutboxException : Exception
    {
        /// <summary>Constructor arguments: record path, endpoint or account alias.</summary>
        public const string InvalidConfiguration = "OUTBOX_INVALID_CONFIGURATION";
        /// <summary>Not a stageable mutation: route, body size/UTF-8/strict JSON, or the body's requestId/version members. Nothing changed.</summary>
        public const string InvalidRequest = "OUTBOX_INVALID_REQUEST";
        /// <summary>Another holder (this or another process) has the lock; nothing was read or changed. Retry later.</summary>
        public const string Busy = "OUTBOX_BUSY";
        /// <summary>The lock could not be taken (missing directory, a directory in its place, access, native lock error); nothing changed.</summary>
        public const string LockFailed = "OUTBOX_LOCK_FAILED";
        /// <summary>No checked interprocess lock on this platform (only macOS/Linux libc flock); nothing was read or changed.</summary>
        public const string Unsupported = "OUTBOX_UNSUPPORTED_PLATFORM";
        /// <summary>A different request is pending; it was kept.</summary>
        public const string PendingOther = "OUTBOX_PENDING_OTHER";
        /// <summary>The pending request has this requestId with another route or body; it was kept.</summary>
        public const string RequestIdReused = "OUTBOX_REQUEST_ID_REUSED";
        /// <summary>The pending record belongs to another endpoint or account; it was kept.</summary>
        public const string BindingMismatch = "OUTBOX_BINDING_MISMATCH";
        /// <summary>The record path holds something that is not exactly a valid record of this format; it was kept byte for byte.</summary>
        public const string RecordCorrupt = "OUTBOX_RECORD_CORRUPT";
        /// <summary>The record has another schema; it was kept byte for byte.</summary>
        public const string RecordUnsupported = "OUTBOX_RECORD_UNSUPPORTED";
        /// <summary>The record could not be read; nothing changed and the pending state is unknown.</summary>
        public const string ReadFailed = "OUTBOX_READ_FAILED";
        /// <summary>Staging failed and nothing was returned to send. Load reports whether the request nevertheless became pending.</summary>
        public const string WriteFailed = "OUTBOX_WRITE_FAILED";
        /// <summary>The expected request is not the current pending one (already cleared, or another request); nothing was cleared.</summary>
        public const string NotCurrent = "OUTBOX_NOT_CURRENT";
        /// <summary>Removing the confirmed record failed; treat it as still pending (Load shows it).</summary>
        public const string ClearFailed = "OUTBOX_CLEAR_FAILED";

        public string Code { get; }
        public PracticeOutboxException(string code, string message) : base(message) { Code = code; }
    }

    /// <summary>One pending request exactly as it is sent and re-sent: immutable and detached from the store. Its identity is endpoint,
    /// account, route and the exact body; RequestId and ExpectedVersion are read from that body.</summary>
    public sealed class PendingPracticeCommand
    {
        internal PendingPracticeCommand(string endpoint, string account, string route, string body, string requestId, int? expectedVersion, long? expectedRevision = null)
        {
            Endpoint = endpoint; Account = account; Route = route; Body = body; RequestId = requestId; ExpectedVersion = expectedVersion; ExpectedRevision = expectedRevision;
        }

        /// <summary>The practice host origin, http://127.0.0.1:&lt;port&gt;; the request URL is Endpoint + Route.</summary>
        public string Endpoint { get; }
        /// <summary>The owning fixture account alias (never a credential).</summary>
        public string Account { get; }
        /// <summary>Always POST.</summary>
        public string Method => PracticeCommandOutbox.Method;
        public string Route { get; }
        /// <summary>The exact JSON body text, sent unchanged on every attempt.</summary>
        public string Body { get; }
        public string RequestId { get; }
        /// <summary>The body's expectedVersion on the commands and memory-commit routes; null for a new run.</summary>
        public int? ExpectedVersion { get; }
        /// <summary>The recruitment account revision staged in the exact body; null on run routes.</summary>
        public long? ExpectedRevision { get; }
        /// <summary>The exact UTF-8 body bytes to send, as a fresh copy on every call.</summary>
        public byte[] BodyUtf8() => PracticeCommandOutbox.StrictUtf8.GetBytes(Body);

        internal bool SameAs(PendingPracticeCommand other) =>
            Endpoint == other.Endpoint && Account == other.Account && Route == other.Route && Body == other.Body;
    }

    /// <summary>The pending-request store of one configured record file. Instances hold only configuration and take the lock per
    /// operation, so any number of them, in one or several processes, may share the file.</summary>
    public sealed class PracticeCommandOutbox
    {
        public const string Schema = "mf-dev-practice-outbox-1";
        public const string Method = "POST";
        /// <summary>POST /dev/practice/runs (new run); the run routes come from CommandRoute and MemoryCommitRoute.</summary>
        public const string CreateRunRoute = "/dev/practice/runs";
        public const string RecruitmentPullRoute = "/dev/practice/recruitment/pulls", RecruitmentLaunchRoute = "/dev/practice/recruitment/claim-launch",
            RecruitmentSelectorRoute = "/dev/practice/recruitment/selector";
        public static bool IsRecruitmentRoute(string route) => route == RecruitmentPullRoute || route == RecruitmentLaunchRoute || route == RecruitmentSelectorRoute;
        /// <summary>The server's body limit (Contracts.MaxBodyBytes) and JSON nesting limit.</summary>
        public const int MaxBodyBytes = 64 * 1024, MaxBodyDepth = 8;
        /// <summary>Above every canonical record of a body within MaxBodyBytes (at most four file bytes per body byte, plus the fields).</summary>
        public const int MaxRecordBytes = 1024 * 1024;
        public const string LockSuffix = ".lock", TempSuffix = ".tmp";
        const string RunPrefix = CreateRunRoute + "/", CommandsSuffix = "/commands", MemoryCommitSuffix = "/memory-commit", EndpointPrefix = "http://127.0.0.1:";
        const int RunIdLength = 35, LockExclusive = 2, LockNonBlocking = 4, Interrupted = 4, MaxInterruptedRetries = 8;
        // POSIX mode 0600 (owner read/write) written in hex: a C# literal 0600 would be decimal 600.
        const uint OwnerReadWrite = 0x180;
        internal static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        // EWOULDBLOCK of flock(LOCK_NB): 35 on macOS, 11 on Linux; 0 = no checked lock on this platform.
        static readonly int WouldBlock = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 35 : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? 11 : 0;
        static readonly object Nested = new object();

        readonly string recordPath, lockPath, folder, fileName;

        /// <summary>filePath: absolute path of the record file in an existing directory (trusted Development configuration). endpoint:
        /// exactly http://127.0.0.1:&lt;port 1..65535&gt; (no credentials, path, query or fragment). account: the fixture alias, 2..32
        /// of a-z0-9- starting with a letter. Nothing is created or locked here.</summary>
        public PracticeCommandOutbox(string filePath, string endpoint, string account)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (account == null) throw new ArgumentNullException(nameof(account));
            if (!Path.IsPathFullyQualified(filePath)) throw Fail(PracticeOutboxException.InvalidConfiguration, "The outbox file path must be absolute.");
            string full = Path.GetFullPath(filePath);
            string name = Path.GetFileName(full), directory = Path.GetDirectoryName(full) ?? "";
            if (name.Length == 0 || directory.Length == 0 || !Directory.Exists(directory))
                throw Fail(PracticeOutboxException.InvalidConfiguration, "The outbox file must be a file name in an existing directory.");
            if (!IsEndpoint(endpoint)) throw Fail(PracticeOutboxException.InvalidConfiguration, "The endpoint must be http://127.0.0.1:<port>.");
            if (!IsAccount(account)) throw Fail(PracticeOutboxException.InvalidConfiguration, "Not a practice account alias.");
            recordPath = full; lockPath = full + LockSuffix; folder = directory; fileName = name;
            Endpoint = endpoint; Account = account;
        }

        public string Endpoint { get; }
        public string Account { get; }

        public static string CommandRoute(string runId) => RunRoute(runId) + CommandsSuffix;
        public static string MemoryCommitRoute(string runId) => RunRoute(runId) + MemoryCommitSuffix;
        static string RunRoute(string runId) => runId != null && IsRunId(runId) ? RunPrefix + runId
            : throw Fail(PracticeOutboxException.InvalidRequest, "Not a practice run ID.");

        /// <summary>The pending request of this endpoint and account, or null when there is none. Read under the lock; nothing is written
        /// except the permanent lock file on first use. A record of another binding, a corrupt/unknown record or an unreadable store
        /// throws and is kept as it is: it never reads as "nothing pending".</summary>
        public PendingPracticeCommand? Load()
        {
            using (Lock()) return Current();
        }

        /// <summary>Durably records the exact request before it is first sent and returns it as the one sendable pending request. Call it
        /// only for a fully built mutation the player is sending now, never for a query or an unsent local intent. Send exactly
        /// BodyUtf8() to Endpoint + Route, and after a timeout, disconnect, 503 or restart send the same record again (never a new
        /// requestId or a rebuilt body). The same request again returns the pending record unchanged; any other request, or this
        /// requestId with another route or body, is refused while one is pending. On any exception nothing was returned to send; after
        /// OUTBOX_WRITE_FAILED, Load shows whether the request nevertheless became pending.</summary>
        public PendingPracticeCommand Stage(string route, string exactJsonBody)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));
            if (exactJsonBody == null) throw new ArgumentNullException(nameof(exactJsonBody));
            PendingPracticeCommand staged;
            try { staged = Command(Endpoint, Account, route, exactJsonBody); }
            catch (FormatException error) { throw Fail(PracticeOutboxException.InvalidRequest, "Not a stageable practice mutation (" + error.Message + "); nothing was staged."); }
            using (Lock())
            {
                var current = Current();
                if (current != null)
                {
                    if (current.SameAs(staged)) return current;
                    throw current.RequestId == staged.RequestId
                        ? Fail(PracticeOutboxException.RequestIdReused, "The pending request has this requestId with another route or body; it was kept.")
                        : Fail(PracticeOutboxException.PendingOther, "Another request is still pending; it was kept. Resolve it before staging a new one.");
                }
                byte[] bytes = Encode(staged);
                if (!ReadsBackAs(bytes, staged)) throw Fail(PracticeOutboxException.InvalidRequest, "The request cannot be recorded exactly; nothing was staged.");
                Publish(bytes);
                return staged;
            }
        }

        /// <summary>Clears the pending request after the owning transport validated a successful response to a send of exactly this
        /// request. Only the exact current request (endpoint, account, route, body) is cleared; anything else is OUTBOX_NOT_CURRENT. This
        /// neither authenticates the response nor applies any state. Never call it for a timeout, disconnect, 503 (STORE_COMMIT_UNKNOWN
        /// included), a rejection, a lifecycle event or a current-state GET. After OUTBOX_CLEAR_FAILED the request is still pending.</summary>
        public void Confirm(PendingPracticeCommand expectedPending) =>
            ClearCurrent(expectedPending, "The confirmed request could not be cleared; treat it as still pending.");

        /// <summary>Clears the pending request after the owning transport, with the same credential whose account it verified, received a
        /// validated refusal of a send of exactly this request that the server guarantees was decided before anything was committed (its
        /// requestId not consumed). The transport alone decides which refusals qualify, from the server's actual codes; this method neither
        /// authenticates nor judges the response. The identity check, lock and removal are Confirm's own; anything else is
        /// OUTBOX_NOT_CURRENT. Never call it for a timeout, disconnect, 401, 500, 503 (STORE_COMMIT_UNKNOWN included), 409
        /// IDEMPOTENCY_CONFLICT, a store binding refusal, an unreadable reply, a lifecycle event or a current-state GET. After
        /// OUTBOX_CLEAR_FAILED the request is still pending.</summary>
        public void ConfirmNotCommitted(PendingPracticeCommand expectedPending) =>
            ClearCurrent(expectedPending, "The refused request could not be cleared; treat it as still pending.");

        void ClearCurrent(PendingPracticeCommand expectedPending, string clearFailed)
        {
            if (expectedPending == null) throw new ArgumentNullException(nameof(expectedPending));
            using (Lock())
            {
                var current = Current();
                if (current == null || !current.SameAs(expectedPending))
                    throw Fail(PracticeOutboxException.NotCurrent, "This is not the pending request (already cleared, or another request); nothing was cleared.");
                try { File.Delete(recordPath); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    throw Fail(PracticeOutboxException.ClearFailed, clearFailed);
                }
            }
        }

        // ---- record file (callers hold the lock) ----

        PendingPracticeCommand? Current()
        {
            byte[]? bytes = ReadRecord();
            if (bytes == null) return null;
            var record = Decode(bytes);
            if (record.Endpoint != Endpoint || record.Account != Account)
                throw Fail(PracticeOutboxException.BindingMismatch, "The pending request belongs to another endpoint or account; it was kept.");
            return record;
        }

        byte[]? ReadRecord()
        {
            if (Directory.Exists(recordPath)) throw Fail(PracticeOutboxException.RecordCorrupt, "The outbox path holds a directory; it was kept.");
            try
            {
                using (var stream = new FileStream(recordPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var buffer = new MemoryStream())
                {
                    var chunk = new byte[8192];
                    int read;
                    while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        if (buffer.Length + read > MaxRecordBytes) throw Fail(PracticeOutboxException.RecordCorrupt, "The outbox record is too large; it was kept.");
                        buffer.Write(chunk, 0, read);
                    }
                    return buffer.ToArray();
                }
            }
            catch (FileNotFoundException) { return null; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                throw Fail(PracticeOutboxException.ReadFailed, "The outbox record could not be read; the pending state is unknown.");
            }
        }

        // Private temp file in the same directory: created new, made owner-only (0600, fchmod on its descriptor) before any byte is
        // written, written and flushed to disk, then moved to the record path (the rename keeps that mode). File.Move without overwrite
        // refuses an existing destination; the lock (no other cooperating writer between the check and the move) is what makes that
        // check-then-rename safe. If the mode cannot be set, only this still-empty temp is removed and nothing is staged. A temp file left
        // by a failure or crash is never read.
        void Publish(byte[] bytes)
        {
            string temp = Path.Combine(folder, "." + fileName + "." + Guid.NewGuid().ToString("N") + TempSuffix);
            bool created = false;
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    RestrictToOwner(stream.SafeFileHandle);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                File.Move(temp, recordPath);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                if (created)
                    try { File.Delete(temp); }
                    catch (Exception cleanup) when (cleanup is IOException || cleanup is UnauthorizedAccessException) { }
                throw Fail(PracticeOutboxException.WriteFailed, "The request was not staged and must not be sent; Load shows whether it is pending.");
            }
        }

        static byte[] Encode(PendingPracticeCommand command)
        {
            string record = Json("endpoint", command.Endpoint, "account", command.Account, "method", Method, "route", command.Route,
                "requestId", command.RequestId, "body", command.Body);
            return StrictUtf8.GetBytes(Json("schema", Schema, "sha256", Sha256(record), "record", record));
        }

        static PendingPracticeCommand Decode(byte[] bytes)
        {
            Dictionary<string, object?> file;
            try { file = StrictObject(StrictUtf8.GetString(bytes), MaxBodyDepth); }
            catch (Exception error) when (error is FormatException || error is DecoderFallbackException) { throw Corrupt(); }
            if (file.TryGetValue("schema", out object? schema) && schema is string name && name != Schema)
                throw Fail(PracticeOutboxException.RecordUnsupported, "The outbox record has another schema; it was kept.");
            PendingPracticeCommand command;
            try
            {
                string[] envelope = Fields(file, "schema", "sha256", "record");
                if (envelope[1] != Sha256(envelope[2])) throw new FormatException("checksum");
                string[] record = Fields(StrictObject(envelope[2], 1), "endpoint", "account", "method", "route", "requestId", "body");
                if (!IsEndpoint(record[0]) || !IsAccount(record[1]) || record[2] != Method) throw new FormatException("binding");
                command = Command(record[0], record[1], record[3], record[5]);
                if (command.RequestId != record[4]) throw new FormatException("requestId");
            }
            catch (Exception error) when (error is FormatException || error is EncoderFallbackException) { throw Corrupt(); }
            // Only the canonical encoding of a valid record is accepted; any other spelling of the same values was not written here.
            if (!SameBytes(Encode(command), bytes)) throw Corrupt();
            return command;
        }

        static bool ReadsBackAs(byte[] bytes, PendingPracticeCommand command)
        {
            try { return Decode(bytes).SameAs(command); }
            catch (PracticeOutboxException) { return false; }
        }

        // ---- the stageable request ----

        /// <summary>One allowed mutation route and a body of 1..MaxBodyBytes strict UTF-8 holding one strict JSON object whose
        /// requestId is a server-valid ID; on the run routes its expectedVersion (and, for memory-commit, expectedMemoryVersion) is a JSON
        /// integer 0..2147483647, and a new run carries none. Recruitment requires a nonnegative Int64 expectedRevision and only its public intent fields.
        /// Every other run member and all game rules are the server's to judge.</summary>
        static PendingPracticeCommand Command(string endpoint, string account, string route, string body)
        {
            bool command = IsRunRoute(route, CommandsSuffix), commit = IsRunRoute(route, MemoryCommitSuffix);
            bool recruitment = IsRecruitmentRoute(route);
            if (route != CreateRunRoute && !command && !commit && !recruitment) throw new FormatException("route");
            int size;
            try { size = StrictUtf8.GetByteCount(body); }
            catch (EncoderFallbackException) { throw new FormatException("body UTF-8"); }
            if (size == 0 || size > MaxBodyBytes) throw new FormatException("body size");
            var members = StrictObject(body, MaxBodyDepth);
            if (!(members.TryGetValue("requestId", out object? id) && id is string requestId && IsRequestId(requestId))) throw new FormatException("body requestId");
            int? expectedVersion = null;
            if (command || commit)
            {
                expectedVersion = Version(members, "expectedVersion") ?? throw new FormatException("body expectedVersion");
                if (commit && Version(members, "expectedMemoryVersion") == null) throw new FormatException("body expectedMemoryVersion");
            }
            else if (members.ContainsKey("expectedVersion")) throw new FormatException("a new run has no expectedVersion");
            long? expectedRevision = null;
            if (recruitment)
            {
                if (!(members.TryGetValue("expectedRevision", out object? revision) && revision is long number && number >= 0)) throw new FormatException("body expectedRevision");
                expectedRevision = number;
                int fields = route == RecruitmentPullRoute ? 4 : route == RecruitmentSelectorRoute ? 3 : 2;
                if (members.Count != fields) throw new FormatException("recruitment members");
                if (route == RecruitmentPullRoute && (!(members.TryGetValue("bannerId", out object? banner) && banner is string bannerId && IsPublicId(bannerId))
                    || !(members.TryGetValue("count", out object? count) && count is long pulls && (pulls == 1 || pulls == 10)))) throw new FormatException("recruitment pull");
                if (route == RecruitmentSelectorRoute && !(members.TryGetValue("charId", out object? character) && character is string charId && IsPublicId(charId)))
                    throw new FormatException("recruitment selector");
            }
            return new PendingPracticeCommand(endpoint, account, route, body, requestId, expectedVersion, expectedRevision);
        }

        internal static bool IsPublicId(string id)
        {
            if (id.Length == 0 || id.Length > 128) return false;
            foreach (char c in id) if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_' || c == '.' || c == ':')) return false;
            return true;
        }

        static int? Version(Dictionary<string, object?> members, string name) =>
            members.TryGetValue(name, out object? value) && value is long number && number >= 0 && number <= int.MaxValue ? (int?)number : null;

        static bool IsRunRoute(string route, string suffix) => route.Length == RunPrefix.Length + RunIdLength + suffix.Length
            && route.StartsWith(RunPrefix, StringComparison.Ordinal) && route.EndsWith(suffix, StringComparison.Ordinal)
            && IsRunId(route.Substring(RunPrefix.Length, RunIdLength));
        // PracticeRunStore.IsRunId: "pr-" and 32 lowercase hex digits.
        static bool IsRunId(string id)
        {
            if (id.Length != RunIdLength || !id.StartsWith("pr-", StringComparison.Ordinal)) return false;
            for (int i = 3; i < id.Length; i++) if (!(id[i] >= '0' && id[i] <= '9' || id[i] >= 'a' && id[i] <= 'f')) return false;
            return true;
        }
        // Contracts.RequestId: 8..64 characters of [A-Za-z0-9._:-].
        static bool IsRequestId(string id)
        {
            if (id.Length < 8 || id.Length > 64) return false;
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_' || c == '.' || c == ':')) return false;
            return true;
        }
        // The fixture accountId: 2..32 of [a-z0-9-], starting with a letter.
        static bool IsAccount(string id)
        {
            if (id.Length < 2 || id.Length > 32 || !(id[0] >= 'a' && id[0] <= 'z')) return false;
            foreach (char c in id) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-')) return false;
            return true;
        }
        // The practice host binds 127.0.0.1 only; the port is the bound one (1..65535, no sign or leading zero).
        static bool IsEndpoint(string endpoint)
        {
            if (!endpoint.StartsWith(EndpointPrefix, StringComparison.Ordinal)) return false;
            string port = endpoint.Substring(EndpointPrefix.Length);
            if (port.Length < 1 || port.Length > 5 || port[0] == '0') return false;
            foreach (char c in port) if (c < '0' || c > '9') return false;
            return int.Parse(port, NumberStyles.None, CultureInfo.InvariantCulture) <= 65535;
        }

        // ---- strict JSON: an RFC 8259 lexical pass, then Newtonsoft for structure, depth, duplicates and trailing content ----

        /// <summary>The top-level members of one strict JSON object (nested containers appear as an opaque marker). Refused: anything
        /// but an object, nesting deeper than maxDepth (the top object is 1), a duplicate member name in any object (compared after
        /// unescaping), unpaired surrogates, and everything CheckLexemes refuses.</summary>
        static Dictionary<string, object?> StrictObject(string text, int maxDepth)
        {
            CheckLexemes(text);
            var top = new Dictionary<string, object?>(StringComparer.Ordinal);
            var scopes = new Stack<HashSet<string>?>();
            string? member = null;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(text))
                       { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double, MaxDepth = null })
                {
                    if (!reader.Read() || reader.TokenType != JsonToken.StartObject) throw new FormatException("not a JSON object");
                    scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                    while (scopes.Count > 0)
                    {
                        if (!reader.Read()) throw new FormatException("truncated JSON");
                        switch (reader.TokenType)
                        {
                            case JsonToken.PropertyName:
                                string name = Paired((string)reader.Value!);
                                if (!scopes.Peek()!.Add(name)) throw new FormatException("duplicate member");
                                if (scopes.Count == 1) member = name;
                                break;
                            case JsonToken.StartObject:
                            case JsonToken.StartArray:
                                if (scopes.Count >= maxDepth) throw new FormatException("JSON nesting too deep");
                                Assign(top, scopes, ref member, Nested);
                                scopes.Push(reader.TokenType == JsonToken.StartObject ? new HashSet<string>(StringComparer.Ordinal) : null);
                                break;
                            case JsonToken.EndObject:
                            case JsonToken.EndArray:
                                scopes.Pop();
                                break;
                            case JsonToken.String:
                                Assign(top, scopes, ref member, Paired((string)reader.Value!));
                                break;
                            case JsonToken.Integer:
                            case JsonToken.Float:
                            case JsonToken.Boolean:
                            case JsonToken.Null:
                                Assign(top, scopes, ref member, reader.Value);
                                break;
                            default: throw new FormatException("not plain JSON");
                        }
                    }
                    if (reader.Read()) throw new FormatException("content after the JSON value");
                }
            }
            catch (JsonException) { throw new FormatException("malformed JSON"); }
            return top;
        }

        static void Assign(Dictionary<string, object?> top, Stack<HashSet<string>?> scopes, ref string? member, object? value)
        {
            if (scopes.Count != 1 || member == null) return;
            top[member] = value;
            member = null;
        }

        static string Paired(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
                else if (char.IsSurrogate(text[i])) throw new FormatException("unpaired surrogate");
            }
            return text;
        }

        /// <summary>The lexical layer Newtonsoft is lenient about: only "-quoted strings with JSON escapes and no raw control character,
        /// JSON number lexemes (no leading zero, '+', hex/octal, NaN/Infinity), true/false/null, the six structural characters and the
        /// four whitespace characters. No comment, single quote, unquoted name, byte order mark, empty or trailing comma. Also the lexical
        /// pass of PracticePublicJson (server replies), so both read JSON with one checker.</summary>
        internal static void CheckLexemes(string text)
        {
            char previous = '\0';
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { i++; continue; }
                if ((c == '}' || c == ']' || c == ',') && previous == ',' || c == ',' && (previous == '\0' || previous == '{' || previous == '[' || previous == ':'))
                    throw new FormatException("misplaced comma");
                if (c == '{' || c == '}' || c == '[' || c == ']' || c == ':' || c == ',') i++;
                else if (c == '"') i = StringEnd(text, i);
                else
                {
                    i = c == '-' || c >= '0' && c <= '9' ? NumberEnd(text, i) : LiteralEnd(text, i);
                    if (i < text.Length && !(text[i] == ' ' || text[i] == '\t' || text[i] == '\n' || text[i] == '\r' || text[i] == ',' || text[i] == ']' || text[i] == '}'))
                        throw new FormatException("JSON token");
                }
                previous = c;
            }
        }

        // One JSON string from its opening quote. Besides the escape grammar, the decoded UTF-16 units (raw characters and \u escapes
        // alike) must be well-formed: every high surrogate immediately followed by a low one, no low surrogate without one. This runs on
        // the original text because Newtonsoft silently turns an escaped lone surrogate into U+FFFD; a genuine U+FFFD, a paired escape
        // and literal backslash text such as \\uD800 stay valid.
        static int StringEnd(string text, int i)
        {
            bool expectLow = false;
            for (i++; i < text.Length; i++)
            {
                char unit = text[i];
                if (unit == '"')
                {
                    if (expectLow) throw new FormatException("unpaired surrogate in a JSON string");
                    return i + 1;
                }
                if (unit < ' ') throw new FormatException("control character in a JSON string");
                if (unit == '\\')
                {
                    if (++i >= text.Length) break;
                    if (text[i] == 'u')
                    {
                        if (i + 4 >= text.Length) break;
                        int code = 0;
                        for (int k = 1; k <= 4; k++)
                        {
                            char h = text[i + k];
                            int digit = h >= '0' && h <= '9' ? h - '0' : h >= 'a' && h <= 'f' ? h - 'a' + 10 : h >= 'A' && h <= 'F' ? h - 'A' + 10 : -1;
                            if (digit < 0) throw new FormatException("JSON string escape");
                            code = code * 16 + digit;
                        }
                        unit = (char)code;
                        i += 4;
                    }
                    else if ("\"\\/bfnrt".IndexOf(text[i]) < 0) throw new FormatException("JSON string escape");
                    else unit = text[i];
                }
                if (char.IsHighSurrogate(unit))
                {
                    if (expectLow) throw new FormatException("unpaired surrogate in a JSON string");
                    expectLow = true;
                }
                else if (char.IsLowSurrogate(unit))
                {
                    if (!expectLow) throw new FormatException("unpaired surrogate in a JSON string");
                    expectLow = false;
                }
                else if (expectLow) throw new FormatException("unpaired surrogate in a JSON string");
            }
            throw new FormatException("truncated JSON string");
        }

        static int NumberEnd(string text, int i)
        {
            if (text[i] == '-') i++;
            if (i < text.Length && text[i] == '0') i++;
            else i = Digits(text, i);
            if (i < text.Length && text[i] == '.') i = Digits(text, i + 1);
            if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
            {
                i++;
                if (i < text.Length && (text[i] == '+' || text[i] == '-')) i++;
                i = Digits(text, i);
            }
            return i;
        }

        static int Digits(string text, int i)
        {
            int start = i;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
            if (i == start) throw new FormatException("JSON number");
            return i;
        }

        static int LiteralEnd(string text, int i)
        {
            foreach (string word in new[] { "true", "false", "null" })
                if (string.CompareOrdinal(text, i, word, 0, word.Length) == 0) return i + word.Length;
            throw new FormatException("not a JSON token");
        }

        static string[] Fields(Dictionary<string, object?> members, params string[] names)
        {
            if (members.Count != names.Length) throw new FormatException("record members");
            var values = new string[names.Length];
            for (int i = 0; i < names.Length; i++)
                values[i] = members.TryGetValue(names[i], out object? value) && value is string text ? text : throw new FormatException("record member " + names[i]);
            return values;
        }

        // Canonical ASCII JSON object of string members in the given order.
        static string Json(params string[] members)
        {
            var json = new StringBuilder("{");
            for (int i = 0; i < members.Length; i += 2)
            {
                if (i > 0) json.Append(',');
                Quote(json, members[i]);
                json.Append(':');
                Quote(json, members[i + 1]);
            }
            return json.Append('}').ToString();
        }

        static void Quote(StringBuilder json, string text)
        {
            json.Append('"');
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') json.Append('\\').Append(c);
                else if (c >= ' ' && c <= '~') json.Append(c);
                else json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            }
            json.Append('"');
        }

        static string Sha256(string text)
        {
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(StrictUtf8.GetBytes(text));
            var hex = new StringBuilder(64);
            foreach (byte b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return hex.ToString();
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ---- the lock ----

        /// <summary>The permanent lock file, opened with FileShare.None (same-process exclusion) and then held by a checked native
        /// flock(LOCK_EX|LOCK_NB) on that very descriptor (exclusion between processes) until the returned stream is disposed. Contention
        /// at either level is OUTBOX_BUSY at once; EINTR is retried a bounded number of times; anything else fails closed.</summary>
        FileStream Lock()
        {
            if (WouldBlock == 0) throw Fail(PracticeOutboxException.Unsupported, "The outbox needs the macOS/Linux flock lock; nothing was read or changed.");
            if (Directory.Exists(lockPath)) throw Fail(PracticeOutboxException.LockFailed, "The outbox lock path holds a directory; nothing was changed.");
            FileStream stream;
            try { stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (Exception error) when (error is UnauthorizedAccessException || error is FileNotFoundException || error is DirectoryNotFoundException || error is PathTooLongException)
            {
                throw Fail(PracticeOutboxException.LockFailed, "The outbox lock file cannot be opened; nothing was changed.");
            }
            catch (IOException) { throw Busy(); }
            try
            {
                HoldNativeLock(stream.SafeFileHandle);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        static void HoldNativeLock(SafeFileHandle handle)
        {
            bool added = false;
            try
            {
                handle.DangerousAddRef(ref added);
                long fd = handle.IsInvalid ? -1 : handle.DangerousGetHandle().ToInt64();
                if (fd < 0 || fd > int.MaxValue) throw Fail(PracticeOutboxException.LockFailed, "The outbox lock descriptor is not usable; nothing was changed.");
                for (int attempt = 0; attempt < MaxInterruptedRetries; attempt++)
                {
                    if (flock((int)fd, LockExclusive | LockNonBlocking) == 0) return;
                    int errno = Marshal.GetLastWin32Error();
                    if (errno == Interrupted) continue;
                    if (errno == WouldBlock) throw Busy();
                    throw Fail(PracticeOutboxException.LockFailed, "The outbox lock was refused by the system; nothing was changed.");
                }
                throw Fail(PracticeOutboxException.LockFailed, "The outbox lock kept being interrupted; nothing was changed.");
            }
            catch (Exception error) when (error is DllNotFoundException || error is EntryPointNotFoundException || error is MarshalDirectiveException)
            {
                throw Fail(PracticeOutboxException.Unsupported, "The native flock lock is not available; nothing was read or changed.");
            }
            finally
            {
                if (added) handle.DangerousRelease();
            }
        }

        /// <summary>Checked fchmod(fd, 0600) on the open temp descriptor, kept alive by DangerousAddRef; EINTR is retried a bounded number of
        /// times. Any other errno, an unusable descriptor or a missing native entry point is an IOException, which Publish turns into
        /// OUTBOX_WRITE_FAILED after removing its own empty temp. The process umask and every other path are left alone.</summary>
        static void RestrictToOwner(SafeFileHandle handle)
        {
            bool added = false;
            try
            {
                handle.DangerousAddRef(ref added);
                long fd = handle.IsInvalid ? -1 : handle.DangerousGetHandle().ToInt64();
                if (fd < 0 || fd > int.MaxValue) throw new IOException("The temp descriptor is not usable.");
                for (int attempt = 0; attempt < MaxInterruptedRetries; attempt++)
                {
                    if (fchmod((int)fd, OwnerReadWrite) == 0) return;
                    if (Marshal.GetLastWin32Error() != Interrupted) throw new IOException("The owner-only mode was refused.");
                }
                throw new IOException("Setting the owner-only mode kept being interrupted.");
            }
            catch (Exception error) when (error is DllNotFoundException || error is EntryPointNotFoundException || error is MarshalDirectiveException)
            {
                throw new IOException("The native fchmod is not available.");
            }
            finally
            {
                if (added) handle.DangerousRelease();
            }
        }

        [DllImport("libc", SetLastError = true)]
        static extern int flock(int fd, int operation);
        [DllImport("libc", SetLastError = true)]
        static extern int fchmod(int fd, uint mode);

        static PracticeOutboxException Busy() => Fail(PracticeOutboxException.Busy, "Another outbox operation holds the lock; nothing was changed. Retry later.");
        static PracticeOutboxException Corrupt() => Fail(PracticeOutboxException.RecordCorrupt, "The outbox record is not a valid record of this format; it was kept.");
        static PracticeOutboxException Fail(string code, string message) => new PracticeOutboxException(code, message);
    }
}
