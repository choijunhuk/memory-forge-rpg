// P1-LOCAL-AUTHORITATIVE-PERSISTENCE: trusted inputs of the dev practice host. Content is the unchanged data/game_data.json read only
// through the linked reviewed loaders and bound by its SHA-256, rules version and the exact MemoryForge.Core module; the fixture is an
// operator-supplied development file of accounts (SHA-256 credential digests, never secrets), the trusted reference party and run length.
// It is labelled development-only and is not a production owned collection, account system or secret store. Stats are derived by Core
// from the party entry; a fixture may not carry power4/hp/gearBps/gauge or any field outside the allowlist. Missing or invalid input
// refuses startup.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoryForge.Core;

namespace MemoryForge.Server
{
    /// <summary>Startup is refused; the message is fixed text about configuration and never includes secrets or file contents.</summary>
    sealed class StartupRefusal : Exception
    {
        public StartupRefusal(string message) : base(message) { }
    }

    sealed class PracticeContent
    {
        /// <summary>Server journal/dispatch format. Bump it whenever RunCommands or the journal meaning changes; stored envelopes of
        /// another format are refused, never reinterpreted.</summary>
        public const string ReplayFormat = "mf-dev-practice-replay-1";
        public RunData Data { get; }
        public string RulesVersion { get; }
        public string ContentSha256 { get; }
        public string CoreModule { get; }
        public BattleRules Rules => Data.Memory.Battle.Rules;

        PracticeContent(RunData data, string rulesVersion, string contentSha256, string coreModule)
        {
            Data = data; RulesVersion = rulesVersion; ContentSha256 = contentSha256; CoreModule = coreModule;
        }

        public static PracticeContent Load(string root)
        {
            string path = Path.Combine(root, "data", "game_data.json");
            byte[] before = ReadContent(path);
            RunData data; string version;
            try
            {
                data = RunDataLoader.Load(root);
                using var document = JsonDocument.Parse(before);
                version = document.RootElement.GetProperty("meta").GetProperty("version").GetString() ?? throw new InvalidDataException("meta.version");
            }
            catch (Exception) { throw new StartupRefusal("data/game_data.json failed the reviewed content loaders"); }
            // The loaders read the file themselves; the bound hash must be of exactly the bytes they read.
            if (!before.AsSpan().SequenceEqual(ReadContent(path))) throw new StartupRefusal("data/game_data.json changed while it was loading");
            return new PracticeContent(data, version, GraphJson.Sha256(before), typeof(Run).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
        }
        static byte[] ReadContent(string path)
        {
            try { return File.ReadAllBytes(path); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { throw new StartupRefusal("the content root has no readable data/game_data.json"); }
        }
    }

    sealed class PracticeAccount
    {
        public string Id { get; }
        public string Length { get; }
        /// <summary>SHA-256 of the canonical party/length; a stored envelope of another fixture is refused.</summary>
        public string FixtureSha256 { get; }
        internal byte[] CredentialDigest { get; }
        readonly List<Dictionary<string, object?>> party;

        public PracticeAccount(string id, byte[] credentialDigest, string length, List<Dictionary<string, object?>> party)
        {
            Id = id; CredentialDigest = credentialDigest; Length = length; this.party = party;
            FixtureSha256 = GraphJson.Digest(GraphJson.Object("length", length, "party", party.Select(p => (object?)p).ToList()));
        }

        /// <summary>The exact Run.Create input: the trusted party (Core derives the stats), normal difficulty, the fixture's length, v2
        /// memory with the store's saved decks and version, and the server-chosen seed and runId. A client contributes only useMemory.</summary>
        public RunSetup Setup(long seed, string runId, long memoryVersion, object? memory, bool useMemory)
        {
            var setup = new RunSetup
            {
                Seed = seed, Length = Length, MemoryPolicy = "v2", RunId = runId, MemoryVersion = memoryVersion, Memory = GraphJson.Copy(memory),
                Difficulty = new RunDifficulty { Hp = 100, Damage = 100 }, UseMemory = useMemory ? (bool?)null : false
            };
            foreach (var entry in party) setup.Party.Add(RunPartyEntry.FromJsonGraph(GraphJson.Copy(entry)));
            return setup;
        }
    }

    sealed class PracticeFixture
    {
        public const string Schema = "mf-dev-practice-fixture-1";
        const UnixFileMode GroupOrOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        // Growth inputs Core reads from a party entry; derived values (power4, hp, gearBps) and battle state (gauge) are never accepted.
        static readonly string[] PartyIntegers = { "res", "level", "asc", "weaponLevel", "refine", "armor", "accessory", "skillLv", "ultLv" };
        static readonly string EmptyDigest = Convert.ToHexStringLower(SHA256.HashData(Array.Empty<byte>()));
        public IReadOnlyList<PracticeAccount> Accounts { get; }
        PracticeFixture(List<PracticeAccount> accounts) { Accounts = accounts; }

        /// <summary>Constant-time match of SHA-256(token) against every configured digest (no early exit); null when none matches.</summary>
        public PracticeAccount? Authenticate(string token)
        {
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            PracticeAccount? match = null;
            foreach (var account in Accounts) if (CryptographicOperations.FixedTimeEquals(digest, account.CredentialDigest)) match = account;
            return match;
        }

        public static PracticeFixture Load(string path, PracticeContent content)
        {
            byte[] bytes;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) throw new StartupRefusal("the practice fixture file does not exist");
                if (info.LinkTarget != null) throw new StartupRefusal("the practice fixture must not be a symbolic link");
                if (info.Length > 1024 * 1024) throw new StartupRefusal("the practice fixture is larger than 1 MiB");
                if ((File.GetUnixFileMode(path) & GroupOrOther) != 0) throw new StartupRefusal("the practice fixture must not be accessible to group/other (chmod 600)");
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { throw new StartupRefusal("the practice fixture is not readable"); }
            var accounts = new List<PracticeAccount>();
            try
            {
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                BattleDataLoader.Fields(root, "schema", "developmentOnly", "label", "accounts");
                if (Text(root, "schema") != Schema || root.GetProperty("developmentOnly").ValueKind != JsonValueKind.True || Text(root, "label").Length == 0)
                    throw new InvalidDataException("fixture header");
                foreach (var item in root.GetProperty("accounts").EnumerateArray()) accounts.Add(Account(item));
            }
            catch (Exception) { throw new StartupRefusal("the practice fixture does not match " + Schema + " (developmentOnly, label, accounts with accountId, credentialSha256, length and an allowlisted party)"); }
            if (accounts.Count < 1 || accounts.Count > 8) throw new StartupRefusal("the practice fixture must define 1 to 8 accounts");
            if (accounts.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != accounts.Count) throw new StartupRefusal("practice fixture account IDs must be unique");
            if (accounts.Select(a => Convert.ToHexStringLower(a.CredentialDigest)).Distinct(StringComparer.Ordinal).Count() != accounts.Count) throw new StartupRefusal("practice fixture credentials must be distinct");
            // Each account must be a real Run.Create party before the host listens.
            foreach (var account in accounts)
            {
                try { Run.Create(content.Data, account.Setup(1, "pr-" + new string('0', 32), 0, new Dictionary<string, object?>(StringComparer.Ordinal), true)); }
                catch (Exception) { throw new StartupRefusal("practice fixture account '" + account.Id + "' is not a valid Run.Create party"); }
            }
            return new PracticeFixture(accounts);
        }

        static PracticeAccount Account(JsonElement item)
        {
            BattleDataLoader.Fields(item, "accountId", "credentialSha256", "length", "party");
            string id = Text(item, "accountId");
            if (id.Length < 2 || id.Length > 32 || !(id[0] >= 'a' && id[0] <= 'z') || !id.All(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-')) throw new InvalidDataException("accountId");
            string credential = Text(item, "credentialSha256");
            if (credential.Length != 64 || !credential.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f') || credential == EmptyDigest) throw new InvalidDataException("credentialSha256");
            string length = Text(item, "length");
            if (length != "short" && length != "full") throw new InvalidDataException("length");
            var party = new List<Dictionary<string, object?>>();
            foreach (var member in item.GetProperty("party").EnumerateArray())
            {
                BattleDataLoader.Fields(member, new[] { "charId", "weaponId" }.Concat(PartyIntegers).ToArray());
                var entry = new Dictionary<string, object?>(StringComparer.Ordinal) { ["charId"] = Text(member, "charId") };
                if (member.TryGetProperty("weaponId", out var weapon)) entry["weaponId"] = Text(member, "weaponId");
                foreach (string key in PartyIntegers)
                {
                    if (!member.TryGetProperty(key, out var number)) continue;
                    if (number.ValueKind != JsonValueKind.Number || !number.TryGetInt64(out long value) || value < 0 || value > 1000) throw new InvalidDataException(key);
                    entry[key] = value;
                }
                party.Add(entry);
            }
            if (party.Count < 1 || party.Count > 3) throw new InvalidDataException("party size");
            return new PracticeAccount(id, Convert.FromHexString(credential), length, party);
        }
        static string Text(JsonElement json, string name)
        {
            var value = json.GetProperty(name);
            if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException(name);
            return value.GetString() ?? throw new InvalidDataException(name);
        }
    }
}
