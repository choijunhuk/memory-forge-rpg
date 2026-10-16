// P1-LOCAL-AUTHORITATIVE-PERSISTENCE §16.5/§17.2/§17.3/§8.11: request contracts and graph JSON of the local dev practice host.
// Bodies are strict allowlists: an unknown or duplicate member, a fraction, an out-of-range integer or a non-ASCII/oversized string is
// refused with 400 before any account work, so a client can never send a seed, state, stats, HP, power, account or file path. A request's
// identity is the SHA-256 of the canonical JSON of its validated content (members sorted, arrays in order); the same requestId with any
// other identity is IDEMPOTENCY_CONFLICT. CheckArgs is the one argument check of live requests and of replayed journal entries.
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MemoryForge.Server
{
    /// <summary>A refused request: HTTP status and a public error code. It never carries exception text or private state.</summary>
    sealed class ApiError : Exception
    {
        public int Status { get; }
        public string Code { get; }
        public ApiError(int status, string code) : base(code) { Status = status; Code = code; }
    }

    /// <summary>One HTTP reply as a status and its exact UTF-8 JSON text; a stored receipt keeps both and is replayed byte for byte.</summary>
    sealed class Reply
    {
        public int Status { get; }
        public string Body { get; }
        public Reply(int status, string body) { Status = status; Body = body; }
        public static Reply Json(int status, object? graph) => new Reply(status, GraphJson.Text(graph, false));
        public static Reply Error(int status, string code) => Json(status, GraphJson.Object("code", code, "message", Message(status, code)));
        // Fixed category text only: no request, state or exception content.
        static string Message(int status, string code)
        {
            // A commit whose outcome is unknown must not claim that nothing changed.
            if (code == "STORE_COMMIT_UNKNOWN") return "The commit outcome is unknown; retry with the same requestId to resolve it.";
            if (code == "STORE_UNAVAILABLE") return "The development store is unavailable; nothing was committed. Retry with the same requestId.";
            switch (status)
            {
                case 400: return "The request does not match the dev practice contract; nothing changed.";
                case 401: return "A configured development credential is required.";
                case 403: return "Only loopback development requests are accepted.";
                case 404: return "Not found for this account.";
                case 405: return "Method not allowed.";
                case 409: return "Conflicts with the committed account state; nothing changed.";
                case 413: return "Request body too large.";
                case 415: return "Use application/json.";
                case 422: return "Rejected by the game rules; nothing changed.";
                case 503: return "The account is busy; retry with the same requestId.";
                case 507: return "The account file reached the development store limit; nothing changed.";
                default: return "The request failed; nothing changed.";
            }
        }
    }

    /// <summary>JSON text and digests of graphs: string-keyed dictionaries, lists/arrays, strings, booleans, null, integers and finite
    /// doubles. sorted = ordinal member order (identities, state digests); otherwise each dictionary's own member order.</summary>
    static class GraphJson
    {
        static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static Dictionary<string, object?> Object(params object?[] pairs)
        {
            var graph = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) graph.Add((string)pairs[i]!, pairs[i + 1]);
            return graph;
        }
        public static byte[] Bytes(object? value, bool sorted)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, WriterOptions)) Write(writer, value, sorted, 0);
            return buffer.WrittenSpan.ToArray();
        }
        public static string Text(object? value, bool sorted) => Encoding.UTF8.GetString(Bytes(value, sorted));
        public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
        public static string Digest(object? value) => Sha256(Bytes(value, true));
        public static object? Copy(object? value)
        {
            if (value is IDictionary<string, object?> members)
            {
                var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var pair in members) copy.Add(pair.Key, Copy(pair.Value));
                return copy;
            }
            if (value is IList list) return list.Cast<object?>().Select(Copy).ToList();
            return value;
        }
        static void Write(Utf8JsonWriter writer, object? value, bool sorted, int depth)
        {
            if (depth > 128) throw new InvalidDataException("JSON graph nesting exceeds 128");
            switch (value)
            {
                case null: writer.WriteNullValue(); break;
                case string text: writer.WriteStringValue(text); break;
                case bool flag: writer.WriteBooleanValue(flag); break;
                case int number: writer.WriteNumberValue(number); break;
                case uint number: writer.WriteNumberValue(number); break;
                case long number: writer.WriteNumberValue(number); break;
                case double number when double.IsFinite(number): writer.WriteNumberValue(number); break;
                case IDictionary<string, object?> members:
                    writer.WriteStartObject();
                    IEnumerable<KeyValuePair<string, object?>> pairs = sorted ? members.OrderBy(p => p.Key, StringComparer.Ordinal) : members;
                    foreach (var pair in pairs) { writer.WritePropertyName(pair.Key); Write(writer, pair.Value, sorted, depth + 1); }
                    writer.WriteEndObject();
                    break;
                case IList list:
                    writer.WriteStartArray();
                    foreach (object? item in list) Write(writer, item, sorted, depth + 1);
                    writer.WriteEndArray();
                    break;
                default: throw new InvalidDataException("Unsupported JSON graph value");
            }
        }
    }

    sealed record CreateRequest(string RequestId, bool UseMemory, string Identity);
    sealed record CommandRequest(string RequestId, int ExpectedVersion, string Kind, Dictionary<string, object?> Args, string Identity);
    sealed record PreviewRequest(List<object?> SelectedChangeIds);
    sealed record CommitRequest(string RequestId, int ExpectedVersion, Dictionary<string, object?> Args, string Identity);

    static class Contracts
    {
        public const int MaxBodyBytes = 64 * 1024;
        /// <summary>Journal kind of the v2 terminal save; clients reach it only through the memory-commit endpoint.</summary>
        public const string MemoryCommit = "memoryCommit";
        const int MaxText = 128, MaxList = 64;
        static readonly (string Name, bool Required, string Type)[] None = Array.Empty<(string, bool, string)>();

        // Every supported journal kind and its argument allowlist; RunCommands maps each to an existing public Run method.
        // "choice:a|b" accepts exactly those strings; every other value is left to Core's own checks and error codes.
        static readonly Dictionary<string, (string Name, bool Required, string Type)[]> CommandArgs = new Dictionary<string, (string, bool, string)[]>(StringComparer.Ordinal)
        {
            ["enter"] = new[] { ("nodeId", true, "text") },
            ["play"] = new[] { ("uid", true, "text"), ("mode", false, "text"), ("target", false, "text"), ("picks", false, "list") },
            ["ult"] = new[] { ("actor", true, "text"), ("target", false, "text"), ("picks", false, "list") },
            ["end"] = None,
            ["takeCard"] = new[] { ("id", true, "text") },
            ["skipCard"] = None,
            ["takeRelic"] = new[] { ("id", true, "text") },
            ["takePart"] = None,
            ["evolve"] = new[] { ("key", true, "text"), ("branchId", true, "text") },
            ["proceed"] = None,
            ["archive"] = new[] { ("choice", true, "choice:return|continue") },
            ["rest"] = new[] { ("choice", true, "choice:heal|upgrade"), ("key", false, "text") },
            ["forgeOffer"] = new[] { ("key", true, "text") },
            ["forgeApply"] = new[] { ("partId", true, "text") },
            ["forgeRemove"] = new[] { ("key", true, "text") },
            ["applyPart"] = new[] { ("partId", true, "text"), ("key", true, "text") },
            ["buy"] = new[] { ("kind", true, "choice:cards|parts|relics|remove"), ("id", false, "text"), ("key", false, "text") },
            ["event"] = new[] { ("index", true, "int"), ("keys", false, "list") },
            ["assign"] = new[] { ("key", true, "text"), ("charId", true, "text") },
            [MemoryCommit] = new[] { ("requestId", true, "text"), ("expectedMemoryVersion", true, "int"), ("selectedChangeIds", true, "list") },
        };

        public static CreateRequest ParseCreate(byte[] body)
        {
            using var document = Document(body);
            var members = Members(document.RootElement, "requestId", "useMemory");
            string requestId = RequestId(members);
            bool useMemory = true;
            if (members.TryGetValue("useMemory", out var raw) && raw.ValueKind != JsonValueKind.Null)
            {
                if (raw.ValueKind != JsonValueKind.True && raw.ValueKind != JsonValueKind.False) throw new ApiError(400, "INVALID_REQUEST");
                useMemory = raw.GetBoolean();
            }
            return new CreateRequest(requestId, useMemory, GraphJson.Digest(GraphJson.Object("op", "create", "useMemory", useMemory)));
        }

        public static CommandRequest ParseCommand(byte[] body, string runId)
        {
            using var document = Document(body);
            var members = Members(document.RootElement, "requestId", "expectedVersion", "kind", "args");
            string requestId = RequestId(members);
            int expected = Version(members, "expectedVersion");
            if (!members.TryGetValue("kind", out var rawKind) || rawKind.ValueKind != JsonValueKind.String) throw new ApiError(400, "INVALID_REQUEST");
            string kind = StringValue(rawKind, "UNSUPPORTED_COMMAND");
            if (kind == MemoryCommit || !CommandArgs.ContainsKey(kind)) throw new ApiError(400, "UNSUPPORTED_COMMAND");
            var raw = members.TryGetValue("args", out var rawArgs) && rawArgs.ValueKind != JsonValueKind.Null ? ArgsObject(rawArgs) : new Dictionary<string, object?>(StringComparer.Ordinal);
            var args = CheckArgs(kind, raw);
            string identity = GraphJson.Digest(GraphJson.Object("op", "command", "runId", runId, "expectedVersion", expected, "kind", kind, "args", args));
            return new CommandRequest(requestId, expected, kind, args, identity);
        }

        public static PreviewRequest ParsePreview(byte[] body)
        {
            using var document = Document(body);
            return new PreviewRequest(ChangeIds(Members(document.RootElement, "selectedChangeIds")));
        }

        /// <summary>§8.11 {requestId, expectedMemoryVersion, runId (the path), selectedChangeIds} plus the run expectedVersion, so a save
        /// cannot race a later assign/part change of the same terminal Run.</summary>
        public static CommitRequest ParseCommit(byte[] body, string runId)
        {
            using var document = Document(body);
            var members = Members(document.RootElement, "requestId", "expectedVersion", "expectedMemoryVersion", "selectedChangeIds");
            string requestId = RequestId(members);
            int expected = Version(members, "expectedVersion");
            int memoryVersion = Version(members, "expectedMemoryVersion");
            var args = CheckArgs(MemoryCommit, GraphJson.Object("requestId", requestId, "expectedMemoryVersion", (long)memoryVersion, "selectedChangeIds", ChangeIds(members)));
            string identity = GraphJson.Digest(GraphJson.Object("op", MemoryCommit, "runId", runId, "expectedVersion", expected, "args", args));
            return new CommitRequest(requestId, expected, args, identity);
        }

        /// <summary>Members ⊆ the kind's allowlist, required members present, null optional members dropped, values typed (ASCII text,
        /// non-negative Int32 as long, lists of text). Returns a fresh normalized dictionary; anything else is 400.</summary>
        public static Dictionary<string, object?> CheckArgs(string kind, IDictionary<string, object?> args)
        {
            if (!CommandArgs.TryGetValue(kind, out var specs)) throw new ApiError(400, "UNSUPPORTED_COMMAND");
            foreach (string name in args.Keys) if (!specs.Any(spec => spec.Name == name)) throw new ApiError(400, "UNKNOWN_FIELD");
            var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var spec in specs)
            {
                if (!args.TryGetValue(spec.Name, out object? value) || value == null)
                {
                    if (spec.Required) throw new ApiError(400, "INVALID_ARGS");
                    continue;
                }
                normalized.Add(spec.Name, Typed(spec.Type, value));
            }
            return normalized;
        }

        static object Typed(string type, object value)
        {
            switch (type)
            {
                case "text": return Text(value);
                case "int":
                    if (value is long number && number >= 0 && number <= int.MaxValue) return number;
                    throw new ApiError(400, "INVALID_ARGS");
                case "list":
                    if (!(value is IList list) || list.Count > MaxList) throw new ApiError(400, "INVALID_ARGS");
                    return list.Cast<object?>().Select(item => (object?)Text(item)).ToList();
                default:
                    string text = Text(value);
                    if (!type.StartsWith("choice:", StringComparison.Ordinal) || !type.Substring(7).Split('|').Contains(text, StringComparer.Ordinal)) throw new ApiError(400, "INVALID_ARGS");
                    return text;
            }
        }
        // Card/node/change IDs, keys and uids are all printable ASCII; nothing longer than 128 characters is a valid reference.
        static string Text(object? value)
        {
            if (!(value is string text) || text.Length == 0 || text.Length > MaxText || text.Any(c => c < 0x20 || c > 0x7e)) throw new ApiError(400, "INVALID_ARGS");
            return text;
        }

        static JsonDocument Document(byte[] body)
        {
            try { return JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 }); }
            catch (JsonException) { throw new ApiError(400, "INVALID_JSON"); }
        }
        static Dictionary<string, JsonElement> Members(JsonElement element, params string[] allowed)
        {
            if (element.ValueKind != JsonValueKind.Object) throw new ApiError(400, "INVALID_REQUEST");
            var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                string name = Name(property);
                if (!allowed.Contains(name, StringComparer.Ordinal)) throw new ApiError(400, "UNKNOWN_FIELD");
                if (!members.TryAdd(name, property.Value)) throw new ApiError(400, "DUPLICATE_FIELD");
            }
            return members;
        }
        static string Name(JsonProperty property)
        {
            try { return property.Name; }
            catch (InvalidOperationException) { throw new ApiError(400, "INVALID_REQUEST"); }
        }
        static string StringValue(JsonElement element, string code)
        {
            string? value;
            try { value = element.GetString(); }
            catch (InvalidOperationException) { throw new ApiError(400, code); }
            if (value == null) throw new ApiError(400, code);
            try { return Text(value); }
            catch (ApiError) { throw new ApiError(400, code); }
        }
        // 8..64 characters of [A-Za-z0-9._:-] (openapi RequestId length; UUIDs fit).
        static string RequestId(Dictionary<string, JsonElement> members)
        {
            if (!members.TryGetValue("requestId", out var raw) || raw.ValueKind != JsonValueKind.String) throw new ApiError(400, "INVALID_REQUEST_ID");
            string id = StringValue(raw, "INVALID_REQUEST_ID");
            if (id.Length < 8 || id.Length > 64 || !id.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.' or ':'))
                throw new ApiError(400, "INVALID_REQUEST_ID");
            return id;
        }
        static int Version(Dictionary<string, JsonElement> members, string name)
        {
            if (!members.TryGetValue(name, out var raw) || raw.ValueKind != JsonValueKind.Number || !raw.TryGetInt64(out long value) || value < 0 || value > int.MaxValue)
                throw new ApiError(400, "INVALID_REQUEST");
            return (int)value;
        }
        static List<object?> ChangeIds(Dictionary<string, JsonElement> members)
        {
            if (!members.TryGetValue("selectedChangeIds", out var raw) || raw.ValueKind != JsonValueKind.Array) throw new ApiError(400, "INVALID_REQUEST");
            return (List<object?>)Typed("list", Value(raw, 0) ?? throw new ApiError(400, "INVALID_REQUEST"));
        }
        static Dictionary<string, object?> ArgsObject(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) throw new ApiError(400, "INVALID_ARGS");
            var graph = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!graph.TryAdd(Name(property), Value(property.Value, 0))) throw new ApiError(400, "DUPLICATE_FIELD");
            return graph;
        }
        // Argument values: null, booleans, strings, plain integers (no fraction/exponent) and arrays of them; nested objects are refused.
        static object? Value(JsonElement element, int depth)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null: return null;
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.String: return StringValue(element, "INVALID_ARGS");
                case JsonValueKind.Number:
                    if (element.TryGetInt64(out long number)) return number;
                    throw new ApiError(400, "INVALID_ARGS");
                case JsonValueKind.Array:
                    if (depth > 0) throw new ApiError(400, "INVALID_ARGS");
                    return element.EnumerateArray().Select(item => Value(item, depth + 1)).ToList();
                default: throw new ApiError(400, "INVALID_ARGS");
            }
        }
    }
}
