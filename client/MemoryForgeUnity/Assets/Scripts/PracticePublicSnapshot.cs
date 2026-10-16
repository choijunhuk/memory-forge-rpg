// P1-PRACTICE-TRANSPORT §14.7/§17.2/§17.4: the Development practice client's read model of the server's PUBLIC JSON. Each view owns one
// detached JObject parsed from the exact reply bytes and keeps every member the server sent (nothing is dropped, merged or re-derived);
// the typed members are only the context a client must bind to: owner, runId, the SERVER journal version, node, turn, battle.v and the
// memory versions, each with its own meaning. Nothing here hydrates a BattleState or Run, seeds anything or computes a game rule: a price,
// capacity, legality or forecast is the server's value or a question to the server. Accessors return deep copies.
// Strict reading (PracticePublicJson): 1..MaxResponseBytes of strict UTF-8 (no byte order mark) holding one JSON object, the outbox's RFC
// 8259 lexical pass (PracticeCommandOutbox.CheckLexemes, so lone surrogates, comments, single quotes, NaN, hex/leading-zero numbers and
// stray commas are refused), then Newtonsoft with duplicate members refused, nesting at most MaxDepth and nothing after the value. A
// non-finite number or a member NAMED like a private server field (seed, rng, journal, receipts, binding, digests, enemy pattern, ...;
// whole names, case-insensitive; words inside string values are content, not names) refuses the whole reply, and so does a hidden battle
// container at its battle location (state.battle.zones/cards; battle piles must be counts). A required context member
// that is missing or mistyped refuses it too, so a protocol failure never becomes a zero or empty game.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MemoryForge.Client
{
    /// <summary>A server reply that is not acceptable public JSON of this protocol. Code is one of the constants below; the message names
    /// at most a member name of the protocol, never a value, body, header or credential.</summary>
    public sealed class PracticeProtocolException : Exception
    {
        /// <summary>Not strict UTF-8 JSON of the expected shape: size, lexeme, duplicate, depth, trailing content, non-finite number, or a
        /// required member missing or mistyped.</summary>
        public const string Invalid = "RESPONSE_INVALID";
        /// <summary>A member is named like a private server field; the reply is refused whole.</summary>
        public const string PrivateField = "RESPONSE_PRIVATE_FIELD";
        /// <summary>Well formed, but bound to another run, server version, node, turn, battle version or request than the one asked for.</summary>
        public const string Mismatch = "RESPONSE_MISMATCH";
        /// <summary>The account reply names another account than the configured owner.</summary>
        public const string WrongAccount = "WRONG_ACCOUNT";

        public string Code { get; }
        public PracticeProtocolException(string code, string message) : base(message) { Code = code; }
    }

    /// <summary>The one strict reader of practice server replies and the typed member checks of the views below.</summary>
    public static class PracticePublicJson
    {
        /// <summary>Largest reply body read; a longer declared or actual body is never buffered. The deepest public reply (an enemy intent
        /// chain, a save-screen change) nests about ten levels.</summary>
        public const int MaxResponseBytes = 4 * 1024 * 1024, MaxDepth = 32;
        static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Ignore, LineInfoHandling = LineInfoHandling.Ignore
        };
        // Member names of the private Run/Battle graphs, the account envelope and the credential that PublicView never projects
        // (RunState/BattleState.ToJsonGraph, AccountEnvelope, the fixture). Public names such as "token" (a token card), "order"
        // (reservation order), "cards" (reward/shop lists) and "draw" (a pile count) are deliberately not here.
        static readonly HashSet<string> PrivateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "seed", "rng", "journal", "receipts", "receipt", "binding", "requestId", "identity", "digest", "sha256", "stateSha256",
            "createdSha256", "credential", "credentialSha256", "password", "secret", "authorization", "pattern", "pi", "passive", "runFlags",
            "turnFlags", "offered", "startMemory", "memoryBaseline", "envelope", "revision", "archive", "seq", "resSeq", "seqCard"
        };

        // Location-dependent: members of state.battle that are private there although the same names are public elsewhere.
        internal static readonly string[] HiddenBattleMembers = { "zones", "cards" };
        internal static readonly string[] PileCounts = { "draw", "hand", "discard", "exhaust" };

        /// <summary>One strict JSON object from exact reply bytes, detached (nothing else references it). Throws
        /// PracticeProtocolException (RESPONSE_INVALID or RESPONSE_PRIVATE_FIELD).</summary>
        public static JObject ParseObject(byte[] utf8)
        {
            if (utf8 == null) throw new ArgumentNullException(nameof(utf8));
            if (utf8.Length == 0 || utf8.Length > MaxResponseBytes) throw Invalid("body size");
            JToken root;
            try
            {
                string text = PracticeCommandOutbox.StrictUtf8.GetString(utf8);
                PracticeCommandOutbox.CheckLexemes(text);
                using (var reader = new JsonTextReader(new StringReader(text))
                       { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double, MaxDepth = MaxDepth })
                {
                    root = JToken.ReadFrom(reader, LoadSettings);
                    if (reader.Read()) throw new FormatException("content after the JSON value");
                }
            }
            catch (Exception error) when (error is FormatException || error is JsonException || error is DecoderFallbackException) { throw Invalid("JSON"); }
            if (!(root is JObject top)) throw Invalid("not a JSON object");
            CheckTree(top);
            return top;
        }

        // Every member name against PrivateNames and every value's type, iteratively (the depth is already bounded by the reader).
        static void CheckTree(JObject top)
        {
            var pending = new Stack<JToken>();
            pending.Push(top);
            while (pending.Count > 0)
            {
                switch (pending.Pop())
                {
                    case JObject members:
                        foreach (var member in members.Properties())
                        {
                            if (PrivateNames.Contains(member.Name)) throw Private();
                            pending.Push(member.Value);
                        }
                        break;
                    case JArray items:
                        foreach (var item in items) pending.Push(item);
                        break;
                    case JValue value:
                        switch (value.Type)
                        {
                            case JTokenType.String: case JTokenType.Integer: case JTokenType.Boolean: case JTokenType.Null: break;
                            case JTokenType.Float when value.Value is double number && !double.IsNaN(number) && !double.IsInfinity(number): break;
                            default: throw Invalid("value");
                        }
                        break;
                    default: throw Invalid("value");
                }
            }
        }

        // ---- typed members: a required member must be present with exactly this type ----

        internal static JToken Member(JObject json, string name) => json.TryGetValue(name, out JToken? value) && value != null ? value : throw Invalid(name);
        internal static string Text(JObject json, string name) =>
            Member(json, name) is JValue value && value.Type == JTokenType.String ? (string)value.Value! : throw Invalid(name);
        internal static string? NullableText(JObject json, string name)
        {
            var token = Member(json, name);
            return token.Type == JTokenType.Null ? null : token is JValue value && value.Type == JTokenType.String ? (string)value.Value! : throw Invalid(name);
        }
        /// <summary>A JSON integer 0..2147483647.</summary>
        internal static int Count(JObject json, string name) => AsCount(Member(json, name)) ?? throw Invalid(name);
        internal static int? NullableCount(JObject json, string name)
        {
            var token = Member(json, name);
            return token.Type == JTokenType.Null ? (int?)null : AsCount(token) ?? throw Invalid(name);
        }
        static int? AsCount(JToken token) =>
            token is JValue value && value.Type == JTokenType.Integer && value.Value is long number && number >= 0 && number <= int.MaxValue ? (int?)number : null;
        internal static JObject Object(JObject json, string name) => Member(json, name) as JObject ?? throw Invalid(name);
        internal static JObject? NullableObject(JObject json, string name)
        {
            var token = Member(json, name);
            return token.Type == JTokenType.Null ? null : token as JObject ?? throw Invalid(name);
        }
        internal static JArray Array(JObject json, string name) => Member(json, name) as JArray ?? throw Invalid(name);
        // PracticeRunStore.IsRunId: "pr-" and 32 lowercase hex digits.
        internal static string RunId(JObject json, string name)
        {
            string id = Text(json, name);
            if (id.Length != 35 || !id.StartsWith("pr-", StringComparison.Ordinal)) throw Invalid(name);
            for (int i = 3; i < id.Length; i++) if (!(id[i] >= '0' && id[i] <= '9' || id[i] >= 'a' && id[i] <= 'f')) throw Invalid(name);
            return id;
        }

        internal static PracticeProtocolException Invalid(string what) =>
            new PracticeProtocolException(PracticeProtocolException.Invalid, "The reply is not valid practice JSON (" + what + ").");
        internal static PracticeProtocolException Private() =>
            new PracticeProtocolException(PracticeProtocolException.PrivateField, "The reply carries a private server field; it was refused.");
        internal static PracticeProtocolException Mismatch(string what) =>
            new PracticeProtocolException(PracticeProtocolException.Mismatch, "The reply belongs to another request or state (" + what + ").");
    }

    /// <summary>The account's current run as GET /dev/practice/account summarizes it.</summary>
    public sealed class PracticeRunSummary
    {
        internal PracticeRunSummary(string runId, int serverVersion, string phase) { RunId = runId; ServerVersion = serverVersion; Phase = phase; }
        public string RunId { get; }
        /// <summary>The server journal version (committed command count), not battle.v.</summary>
        public int ServerVersion { get; }
        public string Phase { get; }
    }

    /// <summary>GET /dev/practice/account exactly as received: {accountId, memory {version, memory, history}, run|null, finishedRuns}.</summary>
    public sealed class PracticeAccountView
    {
        readonly JObject json;
        PracticeAccountView(JObject json, string accountId, int memoryVersion, int finishedRuns, PracticeRunSummary? run)
        {
            this.json = json; AccountId = accountId; MemoryVersion = memoryVersion; FinishedRuns = finishedRuns; Run = run;
        }

        public string AccountId { get; }
        /// <summary>The saved v2 memory store version (memory.version): the expectedMemoryVersion of a save.</summary>
        public int MemoryVersion { get; }
        public int FinishedRuns { get; }
        /// <summary>Null when the account has no current run (none yet; a DONE run stays current until the next new run).</summary>
        public PracticeRunSummary? Run { get; }
        public JObject ToJObject() => (JObject)json.DeepClone();

        /// <summary>Strictly parses the reply and refuses (WRONG_ACCOUNT) any accountId other than the configured owner.</summary>
        public static PracticeAccountView Parse(byte[] utf8, string expectedAccountId)
        {
            if (expectedAccountId == null) throw new ArgumentNullException(nameof(expectedAccountId));
            var json = PracticePublicJson.ParseObject(utf8);
            if (PracticePublicJson.Text(json, "accountId") != expectedAccountId)
                throw new PracticeProtocolException(PracticeProtocolException.WrongAccount, "The credential belongs to another account than the configured one.");
            var memory = PracticePublicJson.Object(json, "memory");
            int memoryVersion = PracticePublicJson.Count(memory, "version");
            PracticePublicJson.Object(memory, "memory");
            PracticePublicJson.Array(memory, "history");
            var run = PracticePublicJson.NullableObject(json, "run");
            var summary = run == null ? null
                : new PracticeRunSummary(PracticePublicJson.RunId(run, "runId"), PracticePublicJson.Count(run, "version"), PracticePublicJson.Text(run, "phase"));
            return new PracticeAccountView(json, expectedAccountId, memoryVersion, PracticePublicJson.Count(json, "finishedRuns"), summary);
        }
    }

    /// <summary>A run reply exactly as received: GET /dev/practice/runs/{runId}, a new run or a command/memory-commit receipt —
    /// {runId, version, phase, memoryVersion, events?, result?, state}. Only the server's own state is shown; nothing is replayed.</summary>
    public sealed class PracticeRunView
    {
        readonly JObject json;
        PracticeRunView(JObject json, string runId, int serverVersion, string phase, int accountMemoryVersion, int? runMemoryVersion, string? node,
            int? battleVersion, int? turn, string? battlePhase)
        {
            this.json = json; RunId = runId; ServerVersion = serverVersion; Phase = phase; AccountMemoryVersion = accountMemoryVersion;
            RunMemoryVersion = runMemoryVersion; Node = node; BattleVersion = battleVersion; Turn = turn; BattlePhase = battlePhase;
        }

        public string RunId { get; }
        /// <summary>The server journal version (committed command count): the expectedVersion of the next command. Not battle.v.</summary>
        public int ServerVersion { get; }
        public string Phase { get; }
        /// <summary>The account's saved memory store version when this reply was made (top-level memoryVersion).</summary>
        public int AccountMemoryVersion { get; }
        /// <summary>The memory version this run started from (state.memoryVersion); null when the run carries none.</summary>
        public int? RunMemoryVersion { get; }
        /// <summary>state.position: the current map node, or null.</summary>
        public string? Node { get; }
        /// <summary>battle.v: the battle's own version; null outside a battle.</summary>
        public int? BattleVersion { get; }
        public int? Turn { get; }
        public string? BattlePhase { get; }

        public JObject ToJObject() => (JObject)json.DeepClone();
        public JObject StateJObject() => (JObject)json["state"]!.DeepClone();
        /// <summary>The command's public events, when this is a receipt that carries them.</summary>
        public JArray? EventsJArray() => json.TryGetValue("events", out JToken? events) ? (JArray)events!.DeepClone() : null;
        /// <summary>The command's result member (forgeOffer, memory-commit), when this is a receipt that carries one.</summary>
        public JToken? ResultJToken() => json.TryGetValue("result", out JToken? result) ? result!.DeepClone() : null;

        /// <summary>Strictly parses a run reply; expectedRunId, when given, must be its runId (RESPONSE_MISMATCH otherwise). The state must
        /// repeat the reply's runId, version and phase.</summary>
        public static PracticeRunView Parse(byte[] utf8, string? expectedRunId)
        {
            var json = PracticePublicJson.ParseObject(utf8);
            string runId = PracticePublicJson.RunId(json, "runId");
            if (expectedRunId != null && runId != expectedRunId) throw PracticePublicJson.Mismatch("runId");
            int version = PracticePublicJson.Count(json, "version");
            string phase = PracticePublicJson.Text(json, "phase");
            int memoryVersion = PracticePublicJson.Count(json, "memoryVersion");
            var state = PracticePublicJson.Object(json, "state");
            if (PracticePublicJson.Text(state, "runId") != runId || PracticePublicJson.Count(state, "version") != version || PracticePublicJson.Text(state, "phase") != phase)
                throw PracticePublicJson.Mismatch("state");
            int? runMemoryVersion = PracticePublicJson.NullableCount(state, "memoryVersion");
            string? node = PracticePublicJson.NullableText(state, "position");
            var battle = PracticePublicJson.NullableObject(state, "battle");
            int? battleVersion = null, turn = null;
            string? battlePhase = null;
            if (battle != null)
            {
                // BattleState.ToJsonGraph's ordered piles ("zones") and whole card map ("cards") reveal draw order and hidden instances;
                // PublicView never projects them into the battle. Refused at this location only: the run's own zones and reward/shop
                // card lists are public. The piles must be the public counts, never lists.
                foreach (string hidden in PracticePublicJson.HiddenBattleMembers)
                    if (battle.ContainsKey(hidden)) throw PracticePublicJson.Private();
                var piles = PracticePublicJson.Object(battle, "piles");
                foreach (string pile in PracticePublicJson.PileCounts) PracticePublicJson.Count(piles, pile);
                battleVersion = PracticePublicJson.Count(battle, "v");
                turn = PracticePublicJson.Count(battle, "turn");
                battlePhase = PracticePublicJson.Text(battle, "phase");
            }
            if (json.ContainsKey("events"))
                foreach (var item in PracticePublicJson.Array(json, "events")) if (!(item is JObject)) throw PracticePublicJson.Invalid("events");
            return new PracticeRunView(json, runId, version, phase, memoryVersion, runMemoryVersion, node, battleVersion, turn, battlePhase);
        }
    }

    /// <summary>What a UI showed when the player acted: owner, runId, server journal version, node, turn, battle.v and the saved memory
    /// version. A mutation or battle query is accepted only with the context of the current snapshot, and a query answer stays bound to it.</summary>
    public sealed class PracticeUiContext : IEquatable<PracticeUiContext>
    {
        internal PracticeUiContext(string owner, string? runId, int? serverVersion, string? node, int? turn, int? battleVersion, int memoryVersion)
        {
            Owner = owner; RunId = runId; ServerVersion = serverVersion; Node = node; Turn = turn; BattleVersion = battleVersion; MemoryVersion = memoryVersion;
        }

        public string Owner { get; }
        public string? RunId { get; }
        /// <summary>The server journal version; null without a current run.</summary>
        public int? ServerVersion { get; }
        public string? Node { get; }
        public int? Turn { get; }
        /// <summary>battle.v; null outside a battle.</summary>
        public int? BattleVersion { get; }
        /// <summary>The account's saved memory store version.</summary>
        public int MemoryVersion { get; }

        public bool Equals(PracticeUiContext? other) => other != null && Owner == other.Owner && RunId == other.RunId && ServerVersion == other.ServerVersion
            && Node == other.Node && Turn == other.Turn && BattleVersion == other.BattleVersion && MemoryVersion == other.MemoryVersion;
        public override bool Equals(object? other) => Equals(other as PracticeUiContext);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(Owner);
                hash = hash * 31 + (RunId == null ? 0 : StringComparer.Ordinal.GetHashCode(RunId));
                hash = hash * 31 + (ServerVersion ?? -1);
                hash = hash * 31 + (Node == null ? 0 : StringComparer.Ordinal.GetHashCode(Node));
                hash = hash * 31 + (Turn ?? -1);
                hash = hash * 31 + (BattleVersion ?? -1);
                return hash * 31 + MemoryVersion;
            }
        }
    }

    /// <summary>One coherent authoritative public state: an account read and, when it names a current run, that run read at the same
    /// server version and memory version.</summary>
    public sealed class PracticeSnapshot
    {
        internal PracticeSnapshot(PracticeAccountView account, PracticeRunView? run)
        {
            Account = account; Run = run;
            Context = new PracticeUiContext(account.AccountId, run?.RunId, run?.ServerVersion, run?.Node, run?.Turn, run?.BattleVersion, account.MemoryVersion);
        }

        public PracticeAccountView Account { get; }
        /// <summary>Null when the account has no current run.</summary>
        public PracticeRunView? Run { get; }
        public PracticeUiContext Context { get; }
    }

    /// <summary>A validated successful reply to one exact pending mutation (its requestId, route and server version). It confirms that the
    /// request executed; it is never applied as the current state (the session re-reads that), and an old receipt replays no events.</summary>
    public sealed class PracticeReceipt
    {
        PracticeReceipt(string route, string requestId, PracticeRunView reply) { Route = route; RequestId = requestId; Reply = reply; }

        public string Route { get; }
        public string RequestId { get; }
        public PracticeRunView Reply { get; }
        public string RunId => Reply.RunId;
        public int ServerVersion => Reply.ServerVersion;

        /// <summary>A new run's reply has a valid runId and version 0; a command or memory-commit reply has the route's runId and exactly
        /// the request's expectedVersion + 1 (the journal entry it added, the same in a stored receipt); a memory-commit reply carries its
        /// result object. Anything else is RESPONSE_INVALID or RESPONSE_MISMATCH.</summary>
        public static PracticeReceipt Parse(PendingPracticeCommand pending, byte[] utf8)
        {
            if (pending == null) throw new ArgumentNullException(nameof(pending));
            const string runsPrefix = PracticeCommandOutbox.CreateRunRoute + "/";
            bool create = pending.Route == PracticeCommandOutbox.CreateRunRoute;
            // The outbox only holds the three mutation routes, so a run route always carries a validated 35-character run ID here.
            string? runId = create ? null : pending.Route.Substring(runsPrefix.Length, 35);
            var reply = PracticeRunView.Parse(utf8, runId);
            long expected = create ? 0 : (long)(pending.ExpectedVersion ?? throw PracticePublicJson.Invalid("expectedVersion")) + 1;
            if (reply.ServerVersion != expected) throw PracticePublicJson.Mismatch("version");
            if (runId != null && pending.Route == PracticeCommandOutbox.MemoryCommitRoute(runId) && !(reply.ResultJToken() is JObject))
                throw PracticePublicJson.Invalid("result");
            return new PracticeReceipt(pending.Route, pending.RequestId, reply);
        }
    }

    /// <summary>POST /dev/practice/runs/{runId}/battle-query exactly as received, bound to the context it was asked in and to its own
    /// request. Core's forecast and pick sheet for that one request: not a promise about later states, not a command, and never a
    /// replacement for the current state. A hand card's blocked[mode] in the run state is the DEFAULT request only (auto target, no manual
    /// picks), never a verdict on every manual pull/target; this query answers one exact request.</summary>
    public sealed class PracticeBattleQueryView
    {
        readonly JObject json;
        PracticeBattleQueryView(JObject json, PracticeUiContext context, string uid, string mode, string? target, IReadOnlyList<string> picks,
            string battlePhase, string? forecastError, string? choicesError, string? choicesBlocked)
        {
            this.json = json; Context = context; Uid = uid; Mode = mode; Target = target; Picks = picks; BattlePhase = battlePhase;
            ForecastError = forecastError; ChoicesError = choicesError; ChoicesBlocked = choicesBlocked;
        }

        /// <summary>The context the query was asked in; the answer belongs to it alone.</summary>
        public PracticeUiContext Context { get; }
        public string Uid { get; }
        public string Mode { get; }
        public string? Target { get; }
        public IReadOnlyList<string> Picks { get; }
        public string BattlePhase { get; }
        /// <summary>forecast.error: Core's verdict on exactly this request (phase, AP, target, slot, pull destination); null = legal now.</summary>
        public string? ForecastError { get; }
        /// <summary>choices.error: PICK_INVALID when a supplied pick does not hold; null only means the pick sheet was computed.</summary>
        public string? ChoicesError { get; }
        /// <summary>choices.blocked (PICK_GROWTH_CAP), or null.</summary>
        public string? ChoicesBlocked { get; }
        public JObject ToJObject() => (JObject)json.DeepClone();

        /// <summary>Strictly parses the reply and requires it to echo the asking context (runId, server version, node, turn, battle.v) and
        /// the exact request (uid, effective mode, target, ordered picks); anything else is RESPONSE_MISMATCH.</summary>
        public static PracticeBattleQueryView Parse(byte[] utf8, PracticeUiContext context, string uid, string mode, string? target, IReadOnlyList<string> picks)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (uid == null) throw new ArgumentNullException(nameof(uid));
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            if (picks == null) throw new ArgumentNullException(nameof(picks));
            var json = PracticePublicJson.ParseObject(utf8);
            if (PracticePublicJson.RunId(json, "runId") != context.RunId || PracticePublicJson.Count(json, "version") != context.ServerVersion)
                throw PracticePublicJson.Mismatch("run");
            PracticePublicJson.Text(json, "phase");
            var at = PracticePublicJson.Object(json, "context");
            if (PracticePublicJson.NullableText(at, "node") != context.Node || PracticePublicJson.Count(at, "turn") != context.Turn
                || PracticePublicJson.Count(at, "battleVersion") != context.BattleVersion)
                throw PracticePublicJson.Mismatch("context");
            string battlePhase = PracticePublicJson.Text(at, "battlePhase");
            var source = PracticePublicJson.Object(json, "source");
            PracticePublicJson.Text(source, "id");
            PracticePublicJson.NullableText(source, "name");
            PracticePublicJson.NullableText(source, "text");
            var echoed = PracticePublicJson.Array(source, "picks");
            bool same = PracticePublicJson.Text(source, "uid") == uid && PracticePublicJson.Text(source, "mode") == mode
                && PracticePublicJson.NullableText(source, "target") == target && echoed.Count == picks.Count;
            for (int i = 0; same && i < picks.Count; i++) same = echoed[i] is JValue pick && pick.Type == JTokenType.String && (string)pick.Value! == picks[i];
            if (!same) throw PracticePublicJson.Mismatch("source");
            PracticePublicJson.Object(json, "slots");
            var forecast = PracticePublicJson.Object(json, "forecast");
            string? forecastError = PracticePublicJson.NullableText(forecast, "error");
            var choices = PracticePublicJson.Object(json, "choices");
            string? choicesError = PracticePublicJson.NullableText(choices, "error"), choicesBlocked = PracticePublicJson.NullableText(choices, "blocked");
            PracticePublicJson.Array(choices, "requests");
            PracticePublicJson.Array(choices, "candidates");
            var copy = System.Array.AsReadOnly(new List<string>(picks).ToArray());
            return new PracticeBattleQueryView(json, context, uid, mode, target, copy, battlePhase, forecastError, choicesError, choicesBlocked);
        }
    }
}
