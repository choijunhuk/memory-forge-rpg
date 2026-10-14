// Snapshot/hash prerequisites only: no C# Battle/Run command execution occurs here.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoryForge.Core;

static class TraceHashChecks
{
    const int Scenarios = 1000, ShortScenarios = 700, FullScenarios = 300;
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
    static readonly Dictionary<string, string> TraceSources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["data"] = "data/game_data.json", ["engine"] = "engine/rules-core.js",
        ["memory"] = "proposals/memory_v2.cjs", ["generator"] = "tools/gen_traces.mjs",
        ["fuzzPolicy"] = "engine/tests/fuzz_runs.mjs"
    };
    static readonly HashSet<string> RunMethods = new HashSet<string>(new[] { "enter", "evolve", "takeCard", "skipCard", "takeRelic", "takePart", "applyFoundPart",
        "proceed", "rest", "forgeOffer", "forgeApply", "forgeRemove", "buy", "eventChoose", "archive", "commitMemory" }, StringComparer.Ordinal);
    static int checks, failures;

    public static int Run(string root)
    {
        checks = failures = 0;
        try { CheckVectors(root); }
        catch (Exception error) { Check(false, "canonical boundary fixture: " + error.Message); }
        CheckCoreBoundaries();
        Console.WriteLine($"CanonicalJsonChecks: {checks - failures}/{checks} passed");

        int previousChecks = checks, previousFailures = failures;
        var stats = new SnapshotStats();
        string? manifestLine = null, firstScenarioLine = null;
        try
        {
            ReadGzip(Path.Combine(root, "engine", "tests", "vectors", "traces.jsonl.gz"), reader =>
                ReadSnapshots(reader, root, stats, out manifestLine, out firstScenarioLine));
            Check(stats.Scenarios == Scenarios && stats.Hashes == Scenarios * 2, "complete snapshot fixture");
        }
        catch (Exception error) { Check(false, "trace snapshot fixture: " + error.Message); }
        Console.WriteLine($"CanonicalSnapshotChecks: {stats.Hashes}/{Scenarios * 2} hashes verified; scenarios={stats.Scenarios}/{Scenarios} ({stats.Short} short, {stats.Full} full); sourceFiles={stats.SourceFiles}; commandEnvelopes={stats.Commands}");
        if (manifestLine != null && firstScenarioLine != null)
            CheckFixtureRejections(root, manifestLine, firstScenarioLine);
        CheckGzipRejections();
        Console.WriteLine($"TraceFixtureValidationChecks: {checks - previousChecks - (failures - previousFailures)}/{checks - previousChecks} passed");
        Console.WriteLine("BattleRunCommandReplay: NOT_IMPLEMENTED (0 commands executed in C#; snapshot hashes are not command parity)");
        return failures;
    }

    static void Check(bool ok, string name)
    {
        checks++;
        if (!ok) { failures++; Console.WriteLine("FAIL " + name); }
    }

    static void Rejects(Action action, string expected, string name)
    {
        try { action(); Check(false, name + " accepted invalid input"); }
        catch (Exception error) when (error is ArgumentException || error is InvalidDataException || error is JsonException || error is DecoderFallbackException)
        { Check(error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase), name + ": " + error.Message); }
    }

    static void CheckVectors(string root)
    {
        var fixture = Object(ParseJson(File.ReadAllText(Path.Combine(root, "engine", "tests", "vectors", "canonical_hash_vectors.json"), Utf8)));
        Require(Text(Get(fixture, "schema")) == "mf-canonical-hash-v1", "Unsupported canonical vector schema");
        var sources = Object(Get(fixture, "sources"));
        var expectedSources = new[] { "tools/gen_hash_vectors.mjs", "tools/gen_traces.mjs" };
        Require(sources.Count == expectedSources.Length, "Canonical vector source count mismatch");
        foreach (string path in expectedSources)
            Check(Text(Get(sources, path)) == FileHash(Path.Combine(root, path)), "canonical vector source " + path);
        var vectors = Array(Get(fixture, "vectors"));
        Require(vectors.Count == 17, "Canonical vector count mismatch");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (object? value in vectors)
        {
            var vector = Object(value);
            string id = Text(Get(vector, "id"));
            Require(ids.Add(id), "Duplicate canonical vector id " + id);
            object? graph = Get(vector, "value");
            string before = CanonicalState.Serialize(graph);
            Check(before == Text(Get(vector, "canonical")), "canonical text " + id);
            Check(CanonicalState.Hash(graph) == HashText(Get(vector, "sha256")), "canonical hash " + id);
            Check(CanonicalState.Serialize(graph) == before, "canonical graph preserved " + id);
        }
    }

    static void CheckCoreBoundaries()
    {
        Check(CanonicalState.Hash(null) == "74234e98afe7498fb5daf1f36ac2d78acc339464f950703b8c019892f982b90b", "independent SHA-256 null");
        Check(CanonicalState.Hash(new object[0]) == "4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945", "independent SHA-256 empty array");
        foreach (object value in new object[] { (sbyte)-1, (byte)1, (short)-2, (ushort)2, -3, 3u, -4L, 4UL })
            Check(CanonicalState.Serialize(value) == Convert.ToString(value, CultureInfo.InvariantCulture), "CLR integral type " + value.GetType().Name);
        Check(CanonicalState.Serialize(new[] { 3, 2, 1 }) == "[3,2,1]", "typed array order");
        Check(CanonicalState.Hash(new[] { 3, 2, 1 }) != CanonicalState.Hash(new[] { 1, 2, 3 }), "array order affects SHA-256");
        Check(CanonicalState.Serialize(new Dictionary<string, object?> { ["log"] = new object(), ["ev"] = double.NaN, ["keep"] = 1 }) == "{\"keep\":1}", "ignored diagnostics do not require serializable values");
        foreach (object value in new object[] { 0.5f, 1.0, double.NaN, double.PositiveInfinity, 1m })
            Rejects(() => CanonicalState.Serialize(value), "integer primitives", "reject floating/decimal " + value.GetType().Name);
        foreach (object value in new object[] { CanonicalState.MaxSafeInteger + 1, -CanonicalState.MaxSafeInteger - 1, long.MinValue, long.MaxValue, ulong.MaxValue })
            Rejects(() => CanonicalState.Serialize(value), "safe integers", "reject unsafe integer " + value);
        foreach (object value in new object[] { new object(), DateTime.UnixEpoch, DayOfWeek.Monday, new HashSet<int> { 1 } })
            Rejects(() => CanonicalState.Serialize(value), "unsupported", "reject non-JSON " + value.GetType().Name);
        Rejects(() => CanonicalState.Serialize(new int[1, 1]), "one-dimensional", "reject multidimensional array");
        var cyclic = new List<object?>(); cyclic.Add(cyclic);
        Rejects(() => CanonicalState.Serialize(cyclic), "cyclic", "reject cyclic list");
        var cycleObject = new Dictionary<string, object?>(); cycleObject["self"] = cycleObject;
        Rejects(() => CanonicalState.Serialize(cycleObject), "cyclic", "reject cyclic object");
        var shared = new List<object?> { 1 };
        Check(CanonicalState.Serialize(new[] { shared, shared }) == "[[1],[1]]", "shared acyclic containers are valid");
        Rejects(() => ParseJson("{\"a\":1,\"\\u0061\":2}"), "duplicate", "reject duplicate decoded JSON keys");
        Rejects(() => ParseJson("{\"a\":1.5}"), "integer", "reject fractional JSON input");
        Rejects(() => ParseJson("9007199254740992"), "safe integer", "reject unsafe JSON integer input");
    }

    sealed class SnapshotStats
    {
        public int Scenarios, Hashes, Short, Full, SourceFiles, Commands;
    }

    static void ReadSnapshots(TextReader reader, string root, SnapshotStats stats, out string? manifestLine, out string? firstScenarioLine)
    {
        manifestLine = reader.ReadLine(); firstScenarioLine = null;
        Require(manifestLine != null, "Missing trace manifest");
        var manifest = Object(ParseJson(manifestLine!));
        Require(Text(Get(manifest, "type")) == "manifest", "First trace row must be a manifest");
        Require(Text(Get(manifest, "schema")) == "mf-run-traces-v1", "Unsupported trace schema");
        var counts = Object(Get(manifest, "counts"));
        Require(Integer(Get(counts, "scenarios")) == Scenarios && Integer(Get(counts, "short")) == ShortScenarios && Integer(Get(counts, "full")) == FullScenarios,
            "Trace counts must declare 1000 scenarios: 700 short and 300 full");
        long firstSeed = Integer(Get(manifest, "firstSeed"));
        Require(firstSeed >= 0 && firstSeed + Scenarios - 1 <= uint.MaxValue, "Invalid trace seed range");
        string memoryPolicy = Text(Get(manifest, "memoryPolicy"));
        Require(memoryPolicy == "legacy" || memoryPolicy == "v2", "Unsupported trace memory policy");
        var sources = Object(Get(manifest, "sources"));
        Require(sources.Count == TraceSources.Count, "Trace source provenance count mismatch");
        foreach (var source in TraceSources)
        {
            var entry = Object(Get(sources, source.Key));
            Require(entry.Count == 2 && Text(Get(entry, "path")) == source.Value && HashText(Get(entry, "sha256")) == FileHash(Path.Combine(root, source.Value)),
                "Trace source provenance mismatch: " + source.Key);
            stats.SourceFiles++;
        }
        using (var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "game_data.json"), Utf8)))
            Require(Text(Get(manifest, "dataVersion")) == data.RootElement.GetProperty("meta").GetProperty("version").GetString(), "Trace data version mismatch");
        var hashing = Object(Get(manifest, "hashing"));
        Require(Text(Get(hashing, "algorithm")) == "SHA-256" && Text(Get(hashing, "encoding")) == "UTF-8" &&
            Text(Get(hashing, "keys")) == "recursive ordinal UTF-16 order; arrays retain order" &&
            Text(Get(hashing, "numbers")) == "safe integers only" && Text(Get(hashing, "whitespace")) == "none" &&
            Text(Get(hashing, "strings")) == "ECMAScript JSON.stringify escaping; Unicode preserved" &&
            CanonicalState.Serialize(Get(hashing, "omit")) == "[\"log\",\"ev\"]", "Unsupported trace hashing contract");
        string sourceJson = CanonicalState.Serialize(sources);
        var seen = new HashSet<long>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            Require(line.Length > 0, "Empty trace row");
            var row = Object(ParseJson(line));
            Require(Text(Get(row, "type")) == "scenario", "Expected scenario row; duplicate manifests are forbidden");
            firstScenarioLine ??= line;
            var trace = Object(Get(row, "trace"));
            long seed = Integer(Get(trace, "seed"));
            Require(seen.Add(seed), "Duplicate trace seed " + seed);
            Require(stats.Scenarios < Scenarios && seed == firstSeed + stats.Scenarios, "Missing, extra or out-of-order trace seed " + seed);
            string length = Text(Get(trace, "length"));
            Require(length == (stats.Scenarios < ShortScenarios ? "short" : "full"), "Trace length/order mismatch");
            Require(Text(Get(trace, "schema")) == "mf-run-traces-v1" && Text(Get(trace, "dataVersion")) == Text(Get(manifest, "dataVersion")) &&
                Text(Get(trace, "memoryPolicy")) == memoryPolicy && CanonicalState.Serialize(Get(trace, "sources")) == sourceJson, "Trace metadata/source provenance mismatch");
            var setup = Object(Get(trace, "setup"));
            Require(Integer(Get(setup, "seed")) == seed && Text(Get(setup, "length")) == length && Text(Get(setup, "memoryPolicy")) == memoryPolicy &&
                CanonicalState.Serialize(Get(setup, "party")) == CanonicalState.Serialize(Get(trace, "party")), "Trace setup mismatch");
            var policy = Object(Get(trace, "policy"));
            Require(Integer(Get(policy, "seed")) == ((uint)seed ^ 0xc0decafeu), "Trace policy seed mismatch");
            var basePolicy = Object(Get(manifest, "policy"));
            Require(policy.Count == basePolicy.Count + 1 && basePolicy.All(p => CanonicalState.Serialize(Get(policy, p.Key)) == CanonicalState.Serialize(p.Value)), "Trace policy metadata mismatch");
            string initialHash = HashText(Get(trace, "initialStateHash")), finalHash = HashText(Get(trace, "finalStateHash"));
            CheckSnapshot(Get(trace, "initialState"), initialHash, "MAP", memoryPolicy, seed, stats);
            CheckSnapshot(Get(trace, "finalState"), finalHash, "DONE", memoryPolicy, seed, stats);
            var commands = Array(Get(trace, "commands"));
            Require(commands.Count > 0, "Missing trace commands");
            int committed = 0, rejected = 0;
            string previousHash = initialHash;
            foreach (object? commandValue in commands)
            {
                var step = Object(commandValue);
                string outcome = Text(Get(step, "outcome"));
                string stateHash = HashText(Get(step, "stateHash"));
                object? errorCode = Get(step, "errorCode");
                if (outcome == "committed") { Require(errorCode == null, "Committed command has an error"); committed++; }
                else
                {
                    Require(outcome == "rejected" && errorCode is string code && code.Length > 0 && stateHash == previousHash, "Malformed rejection or rejected command changed its declared hash");
                    rejected++;
                }
                var command = Object(Get(step, "command"));
                string scope = Text(Get(command, "scope")), kind = Text(Get(command, "kind"));
                if (scope == "run") { Require(RunMethods.Contains(kind), "Unknown Run command envelope"); Array(Get(command, "args")); }
                else
                {
                    Require(scope == "battle" && (kind == "play" || kind == "ult" || kind == "end"), "Unknown Battle command envelope");
                    long version = Integer(Get(command, "v"));
                    Require(version >= 0 && Text(Get(command, "rid")) == $"r{version}-{kind}", "Invalid Battle request envelope");
                    Object(Get(command, "args"));
                }
                previousHash = stateHash;
            }
            var summary = Object(Get(trace, "summary"));
            Require(Integer(Get(summary, "committed")) == committed && Integer(Get(summary, "rejected")) == rejected && previousHash == finalHash,
                "Truncated/inconsistent trace command summary");
            var last = Object(commands[commands.Count - 1]);
            var lastCommand = Object(Get(last, "command"));
            Require(Text(Get(last, "outcome")) == "committed" && Text(Get(lastCommand, "scope")) == "run" && Text(Get(lastCommand, "kind")) == "commitMemory", "Missing terminal memory commit envelope");
            stats.Commands += commands.Count;
            stats.Scenarios++;
            if (length == "short") stats.Short++; else stats.Full++;
        }
        Require(stats.Scenarios == Scenarios && stats.Short == ShortScenarios && stats.Full == FullScenarios, "Missing/truncated trace scenarios");
    }

    static void CheckSnapshot(object? value, string expectedHash, string phase, string policy, long seed, SnapshotStats stats)
    {
        var snapshot = Object(value);
        Require(snapshot.Count == 2 && snapshot.ContainsKey("run") && snapshot.ContainsKey("memory"), "Malformed snapshot graph");
        Require(Text(Get(Object(Get(snapshot, "run")), "phase")) == phase, "Snapshot phase mismatch");
        Require(policy == "legacy" ? Get(snapshot, "memory") == null : Get(snapshot, "memory") is Dictionary<string, object?>, "Snapshot memory policy mismatch");
        Require(CanonicalState.Hash(snapshot) == expectedHash, $"C# canonical snapshot hash mismatch: seed {seed}, phase {phase}");
        stats.Hashes++;
    }

    static void CheckFixtureRejections(string root, string manifest, string scenario)
    {
        void Read(string text) => ReadSnapshots(new StringReader(text), root, new SnapshotStats(), out _, out _);
        Rejects(() => Read(""), "missing", "missing trace manifest");
        Rejects(() => Read(manifest + "\n" + scenario + "\n"), "missing/truncated", "missing trace scenarios");
        Rejects(() => Read(manifest + "\n" + scenario + "\n" + scenario + "\n"), "duplicate trace seed", "duplicate trace scenario");
        Rejects(() => Read(manifest + "\n" + scenario + "\n" + manifest + "\n"), "duplicate manifests", "duplicate manifest row");
        Rejects(() => Read(manifest + "\n{\"type\":\"scenario\",\"trace\":"), "JSON", "malformed/truncated scenario JSON");
        var badSource = Object(ParseJson(manifest));
        Object(Get(Object(Get(badSource, "sources")), "data"))["sha256"] = new string('0', 64);
        Rejects(() => Read(CanonicalState.Serialize(badSource) + "\n"), "source provenance", "stale/tampered source provenance");
        var badSnapshot = Object(ParseJson(scenario));
        var trace = Object(Get(badSnapshot, "trace"));
        Object(Get(Object(Get(trace, "initialState")), "run"))["phase"] = "BAD";
        Rejects(() => Read(manifest + "\n" + CanonicalState.Serialize(badSnapshot) + "\n"), "phase mismatch", "tampered snapshot");
        var badHash = Object(ParseJson(scenario));
        Object(Get(badHash, "trace"))["initialStateHash"] = new string('0', 64);
        Rejects(() => Read(manifest + "\n" + CanonicalState.Serialize(badHash) + "\n"), "snapshot hash mismatch", "tampered snapshot hash");
        var truncatedCommands = Object(ParseJson(scenario));
        var commands = Array(Get(Object(Get(truncatedCommands, "trace")), "commands"));
        commands.RemoveAt(commands.Count - 1);
        Rejects(() => Read(manifest + "\n" + CanonicalState.Serialize(truncatedCommands) + "\n"), "command summary", "truncated command list");
    }

    static void ReadGzip(string path, Action<TextReader> consume)
    {
        using var file = File.OpenRead(path);
        Require(file.Length >= 18, "Truncated gzip file");
        Span<byte> footer = stackalloc byte[8];
        file.Seek(-8, SeekOrigin.End); file.ReadExactly(footer); file.Position = 0;
        uint expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(footer), expectedSize = BinaryPrimitives.ReadUInt32LittleEndian(footer[4..]);
        using var gzip = new GZipStream(file, CompressionMode.Decompress, true);
        using var counted = new CrcReadStream(gzip);
        using var reader = new StreamReader(counted, Utf8, false, 65536, true);
        consume(reader);
        Require(reader.Peek() == -1, "Unread gzip content");
        // The JS exporter writes exactly one gzip member; check the footer even when
        // GZipStream tolerates a stream that ends before its trailer arrives.
        Require(counted.Crc == expectedCrc && unchecked((uint)counted.Bytes) == expectedSize, "Truncated or corrupt gzip footer");
    }

    static void CheckGzipRejections()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mf-hash-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "fixture.gz");
        try
        {
            using (var file = File.Create(path))
            using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
                gzip.Write(Utf8.GetBytes("{\"ok\":true}\n"));
            string? text = null;
            ReadGzip(path, reader => text = reader.ReadToEnd());
            Check(text == "{\"ok\":true}\n", "valid single-member gzip and footer");
            byte[] original = File.ReadAllBytes(path);
            File.WriteAllBytes(path, original[..^8]);
            Rejects(() => ReadGzip(path, reader => reader.ReadToEnd()), "gzip", "truncated gzip footer");
            original[^8] ^= 1;
            File.WriteAllBytes(path, original);
            Rejects(() => ReadGzip(path, reader => reader.ReadToEnd()), "", "corrupt gzip CRC");
        }
        finally { Directory.Delete(directory, true); }
    }

    sealed class CrcReadStream : Stream
    {
        static readonly uint[] Table = MakeTable();
        readonly Stream source;
        uint crc = uint.MaxValue;
        public ulong Bytes { get; private set; }
        public uint Crc => crc ^ uint.MaxValue;
        public CrcReadStream(Stream source) { this.source = source; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int length = source.Read(buffer);
            foreach (byte b in buffer[..length]) crc = Table[(crc ^ b) & 255] ^ (crc >> 8);
            Bytes += (uint)length;
            return length;
        }
        static uint[] MakeTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++) value = (value & 1) == 0 ? value >> 1 : 0xedb88320u ^ (value >> 1);
                table[i] = value;
            }
            return table;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    static object? ParseJson(string json)
    {
        var reader = new Utf8JsonReader(Utf8.GetBytes(json), new JsonReaderOptions { MaxDepth = 256 });
        Require(reader.Read(), "Missing JSON value");
        object? value = ReadValue(ref reader);
        Require(!reader.Read(), "Extra JSON content");
        return value;
    }

    static object? ReadValue(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                var obj = new Dictionary<string, object?>(StringComparer.Ordinal);
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    Require(reader.TokenType == JsonTokenType.PropertyName, "Expected JSON property name");
                    string name = DecodeString(ref reader);
                    Require(!obj.ContainsKey(name), "Duplicate JSON key " + name);
                    Require(reader.Read(), "Missing JSON property value");
                    obj.Add(name, ReadValue(ref reader));
                }
                Require(reader.TokenType == JsonTokenType.EndObject, "Truncated JSON object");
                return obj;
            case JsonTokenType.StartArray:
                var array = new List<object?>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) array.Add(ReadValue(ref reader));
                Require(reader.TokenType == JsonTokenType.EndArray, "Truncated JSON array");
                return array;
            case JsonTokenType.String: return DecodeString(ref reader);
            case JsonTokenType.True: return true;
            case JsonTokenType.False: return false;
            case JsonTokenType.Null: return null;
            case JsonTokenType.Number:
                Require(reader.TryGetInt64(out long number) && number >= -CanonicalState.MaxSafeInteger && number <= CanonicalState.MaxSafeInteger, "JSON numbers must be safe integers");
                return number;
            default: throw new InvalidDataException("Unsupported JSON token " + reader.TokenType);
        }
    }

    static string DecodeString(ref Utf8JsonReader reader)
    {
        // GetString rejects lone surrogate escapes. JS JSON.stringify preserves those
        // UTF-16 code units as escapes, so decode validated JSON escapes losslessly.
        string raw = Utf8.GetString(reader.ValueSpan);
        if (!reader.ValueIsEscaped) return raw;
        var value = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c != '\\') { value.Append(c); continue; }
            c = raw[++i];
            switch (c)
            {
                case '"': value.Append('"'); break;
                case '\\': value.Append('\\'); break;
                case '/': value.Append('/'); break;
                case 'b': value.Append('\b'); break;
                case 'f': value.Append('\f'); break;
                case 'n': value.Append('\n'); break;
                case 'r': value.Append('\r'); break;
                case 't': value.Append('\t'); break;
                case 'u': value.Append((char)ushort.Parse(raw.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                default: throw new InvalidDataException("Unsupported JSON escape");
            }
        }
        return value.ToString();
    }

    static Dictionary<string, object?> Object(object? value) => value as Dictionary<string, object?> ?? throw new InvalidDataException("Expected JSON object");
    static List<object?> Array(object? value) => value as List<object?> ?? throw new InvalidDataException("Expected JSON array");
    static string Text(object? value) => value as string ?? throw new InvalidDataException("Expected JSON string");
    static long Integer(object? value) => value is long number ? number : throw new InvalidDataException("Expected JSON integer");
    static object? Get(Dictionary<string, object?> obj, string name) => obj.TryGetValue(name, out object? value) ? value : throw new InvalidDataException("Missing JSON field " + name);
    static string HashText(object? value)
    {
        string hash = Text(value);
        Require(hash.Length == 64 && hash.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f'), "Malformed SHA-256 hex string");
        return hash;
    }
    static string FileHash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(file));
    }
    static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
}
