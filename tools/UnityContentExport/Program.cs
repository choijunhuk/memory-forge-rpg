// §14.1–14.3/§16.2 Unity battle-practice content bridge. Reads unchanged data/game_data.json through the
// reviewed BattleDataLoader and writes the typed definitions verbatim; it never authors or retunes content.
// §7.5–7.7/§8.9–8.14/§20.9: the same run also writes the sibling run-content.json from the reviewed RunDataLoader.
// Usage: dotnet run --project tools/UnityContentExport -- <repoRoot> <outputPath>
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryForge.Core;

// Field names are the Unity contract (MemoryForge.Client.ClientBattleContent); keep both sides identical.
sealed class BattleContentCounts
{
    public int Characters, Cards, Weapons, Parts, Relics, Enemies, Zones;
}

sealed class EncounterSet
{
    public string[][] Normal = Array.Empty<string[]>(), Elite = Array.Empty<string[]>(), Boss = Array.Empty<string[]>();
}

sealed class ExportEnvelope
{
    public string Schema = "", DataVersion = "", SourceSha256 = "";
    public BattleContentCounts SourceCounts = new BattleContentCounts();
    public BattleData Battle = new BattleData();
    public Dictionary<string, string> StartingCommon = new Dictionary<string, string>(StringComparer.Ordinal);
    public Dictionary<string, string> CharacterWeaponTypes = new Dictionary<string, string>(StringComparer.Ordinal);
    public Dictionary<string, EncounterSet> Encounters = new Dictionary<string, EncounterSet>(StringComparer.Ordinal);
}

// Unity contract MemoryForge.Client.ClientRunContent. Battle, startingCommon, characters and encounters are not repeated:
// Unity combines this sidecar with battle-content.json only after DataVersion and SourceSha256 match.
sealed class RunExportEnvelope
{
    public string Schema = "", DataVersion = "", SourceSha256 = "";
    public int SourceEventCount;
    public StatsConfig Stats = new StatsConfig();
    public MemoryCostConfig Costs = new MemoryCostConfig();
    public int CapacityBase;
    public List<MemoryAscensionCapacity> AscensionCapacity = new List<MemoryAscensionCapacity>();
    public List<RunEventDefinition> Events = new List<RunEventDefinition>();
}

static class Program
{
    const string Schema = "mf-unity-battle-content-v1";
    const string RunSchema = "mf-unity-run-content-v1";
    const string RunFileName = "run-content.json";
    const string Usage = "usage: dotnet run --project tools/UnityContentExport -- <repoRoot> <outputPath>  (also writes " + RunFileName + " beside outputPath)";

    // Public fields only: BattleData's internal indexes are never written, and unknown members fail the round trip.
    static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        IncludeFields = true, WriteIndented = true, NewLine = "\n", MaxDepth = 128,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    static int Main(string[] args)
    {
        if (args.Length != 2 || string.IsNullOrWhiteSpace(args[0]) || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine(Usage); return 2;
        }
        try { Export(args[0], args[1]); return 0; }
        catch (Exception e)
        {
            Console.Error.WriteLine("UnityContentExport FAIL " + e.GetType().Name + ": " + Bounded(e.Message)); return 1;
        }
    }

    static void Export(string rootArg, string outputArg)
    {
        string root = Path.GetFullPath(rootArg);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("repoRoot does not exist: " + Bounded(root));
        string source = Path.Combine(root, "data", "game_data.json");
        if (!File.Exists(source)) throw new FileNotFoundException("source data not found: " + Bounded(source));
        string output = Path.GetFullPath(outputArg);
        if (!output.EndsWith(".json", StringComparison.Ordinal)) throw new ArgumentException("outputPath must end with .json");
        if (Directory.Exists(output)) throw new ArgumentException("outputPath is a directory: " + Bounded(output));
        // macOS/Windows volumes are usually case-insensitive; never risk overwriting the source.
        if (string.Equals(output, source, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("outputPath must not be the source data file");
        string directory = Path.GetDirectoryName(output) ?? "";
        if (directory.Length == 0 || !Directory.Exists(directory)) throw new DirectoryNotFoundException("output directory does not exist: " + Bounded(directory));
        string runOutput = Path.Combine(directory, RunFileName);
        if (string.Equals(output, runOutput, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("outputPath must not be the " + RunFileName + " sidecar");
        if (Directory.Exists(runOutput)) throw new ArgumentException("sidecar path is a directory: " + Bounded(runOutput));

        byte[] sourceBytes = File.ReadAllBytes(source);
        string sha = Sha256(sourceBytes);
        BattleData battle = BattleDataLoader.Load(root);
        RunData run = RunDataLoader.Load(root);
        if (Sha256(File.ReadAllBytes(source)) != sha) throw new IOException("source data changed while loading");

        var (envelope, weaponTypeRegistry) = Build(sourceBytes, sha, battle);
        Validate(envelope, weaponTypeRegistry);

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, Options);
        var reloaded = JsonSerializer.Deserialize<ExportEnvelope>(new ReadOnlySpan<byte>(bytes), Options)
            ?? throw new InvalidDataException("exported JSON deserialized to null");
        reloaded.Battle.Index();
        Validate(reloaded, weaponTypeRegistry);
        Same(envelope, reloaded, "$");
        if (!bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(reloaded, Options)))
            throw new InvalidDataException("re-serialized content differs from the first export");

        var runEnvelope = BuildRun(sourceBytes, sha, run);
        ValidateRun(runEnvelope, envelope);
        SameAsBattleContent(run, envelope);
        byte[] runBytes = JsonSerializer.SerializeToUtf8Bytes(runEnvelope, Options);
        var runReloaded = JsonSerializer.Deserialize<RunExportEnvelope>(new ReadOnlySpan<byte>(runBytes), Options)
            ?? throw new InvalidDataException("exported run JSON deserialized to null");
        ValidateRun(runReloaded, envelope);
        Same(runEnvelope, runReloaded, "$run");
        if (!runBytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(runReloaded, Options)))
            throw new InvalidDataException("re-serialized run content differs from the first export");

        if (Sha256(File.ReadAllBytes(source)) != sha) throw new IOException("source data changed during export");
        // Two atomic file replacements, not one cross-file transaction. If the second fails, Unity rejects the stale
        // or missing sidecar through its DataVersion/SourceSha256 match instead of mixing sources.
        WriteAtomically(directory, output, bytes);
        WriteAtomically(directory, runOutput, runBytes);

        var c = envelope.SourceCounts;
        Console.WriteLine(Schema + " data=" + envelope.DataVersion + " sourceSha256=" + sha + " characters=" + c.Characters + " cards=" + c.Cards
            + " weapons=" + c.Weapons + " parts=" + c.Parts + " relics=" + c.Relics + " enemies=" + c.Enemies + " zones=" + c.Zones
            + " startingCommon=" + envelope.StartingCommon.Count + " bytes=" + bytes.Length);
        Console.WriteLine(RunSchema + " data=" + runEnvelope.DataVersion + " sourceSha256=" + sha + " events=" + runEnvelope.Events.Count
            + " capacityBase=" + runEnvelope.CapacityBase + " ascensionRules=" + runEnvelope.AscensionCapacity.Count + " bytes=" + runBytes.Length);
    }

    static RunExportEnvelope BuildRun(byte[] sourceBytes, string sha, RunData run)
    {
        using var document = JsonDocument.Parse(sourceBytes);
        var json = document.RootElement;
        // StatsDataLoader never reads levelling.weaponXpRatio, so StatsConfig keeps its initializer; publish it only while it equals the source.
        decimal ratio = json.GetProperty("progression").GetProperty("levelling").GetProperty("weaponXpRatio").GetDecimal();
        if (run.Stats.WeaponXpRatioDen <= 0 || ratio * run.Stats.WeaponXpRatioDen != run.Stats.WeaponXpRatioNum)
            throw new InvalidDataException("StatsConfig.WeaponXpRatioNum/Den differs from progression.levelling.weaponXpRatio");
        // The event count comes from the raw source array, independently of RunDataLoader, so a dropped event fails.
        return new RunExportEnvelope { Schema = RunSchema, DataVersion = Text(json.GetProperty("meta").GetProperty("version")), SourceSha256 = sha,
            SourceEventCount = json.GetProperty("events").GetArrayLength(), Stats = run.Stats, Costs = run.Memory.Costs, CapacityBase = run.Memory.CapacityBase,
            AscensionCapacity = run.Memory.AscensionCapacity, Events = run.Events };
    }

    // Mirrors ClientRunContent.Validate so a sidecar that passes here also loads in Unity. Ranges guard integrity only;
    // authored zero costs stay valid and no economic rule is added.
    static void ValidateRun(RunExportEnvelope r, ExportEnvelope content)
    {
        if (r.Schema != RunSchema) throw new InvalidDataException("unexpected run schema");
        if (r.DataVersion != content.DataVersion || r.SourceSha256 != content.SourceSha256) throw new InvalidDataException("run content source differs from battle content");
        var stats = r.Stats;
        foreach (PropertyInfo property in typeof(StatsConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.PropertyType == typeof(int) && (int)property.GetValue(stats)! < 0) throw new InvalidDataException("Stats." + property.Name + " is negative");
        if (stats.LevelCap <= 0 || stats.SkillMax <= 0 || stats.CharPowerDen <= 0 || stats.XpSqDiv <= 0 || stats.WeaponXpRatioDen <= 0)
            throw new InvalidDataException("Stats cap or denominator is not positive");
        if (!content.Battle.Weapons.Any(w => w.Rarity == stats.StageRefWeapon)) throw new InvalidDataException("Stats.StageRefWeapon is not a weapon rarity: " + Short(stats.StageRefWeapon));
        var cost = r.Costs;
        if (new[] { cost.AddB, cost.AddA, cost.AddS, cost.AddCommon, cost.Upgrade, cost.PartCommon, cost.PartRare, cost.Evolution, cost.Remove }.Any(x => x < 0))
            throw new InvalidDataException("memory cost is negative");
        if (r.CapacityBase < 0 || r.AscensionCapacity.Any(x => x.Asc < 0 || x.Add < 0)) throw new InvalidDataException("memory capacity is negative");
        if (r.SourceEventCount <= 0 || r.Events.Count != r.SourceEventCount)
            throw new InvalidDataException("Events has " + r.Events.Count + " definitions but the source has " + r.SourceEventCount);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in r.Events)
        {
            if (string.IsNullOrEmpty(item.Id) || !ids.Add(item.Id)) throw new InvalidDataException("empty or duplicate event id: " + Short(item.Id));
            if (string.IsNullOrEmpty(item.Name) || string.IsNullOrEmpty(item.Text) || item.Choices.Count == 0) throw new InvalidDataException("event definition is incomplete: " + Short(item.Id));
            foreach (var choice in item.Choices)
            {
                if (string.IsNullOrEmpty(choice.Label) || string.IsNullOrEmpty(choice.Text)) throw new InvalidDataException("event choice text is empty: " + Short(item.Id));
                if (choice.RequiredCoins < 0) throw new InvalidDataException("event choice need.coins is negative: " + Short(item.Id));
                var fx = choice.Effects;
                if (fx.Curse != null && !content.Battle.Cards.Any(x => x.Id == fx.Curse && x.Owner == "curse")) throw new InvalidDataException("event curse does not resolve: " + Short(fx.Curse));
                if (fx.Card != null && !content.Battle.Cards.Any(x => x.Rarity == fx.Card)) throw new InvalidDataException("event card rarity does not resolve: " + Short(fx.Card));
                if (fx.Relic != null && fx.Relic.Length == 0) throw new InvalidDataException("event relic is empty: " + Short(item.Id));
            }
        }
    }

    // Unity rebuilds these RunData parts from battle-content.json; prove they equal the host RunDataLoader output.
    static void SameAsBattleContent(RunData run, ExportEnvelope content)
    {
        static List<List<string>> Rows(string[][] rows) => rows.Select(row => row.ToList()).ToList();
        Same(content.Battle, run.Memory.Battle, "$hostRun.Memory.Battle");
        Same(content.StartingCommon, run.Memory.StartingCommon, "$hostRun.Memory.StartingCommon");
        Same(content.Battle.Characters.Select(x => new RunCharacterMetadata { Id = x.Id, WeaponType = content.CharacterWeaponTypes[x.Id] }).ToList(), run.Characters, "$hostRun.Characters");
        Same(content.Encounters.Select(z => new RunEncounterDefinition { Zone = z.Key, Normal = Rows(z.Value.Normal), Elite = Rows(z.Value.Elite), Boss = Rows(z.Value.Boss) }).ToList(),
            run.Encounters, "$hostRun.Encounters");
    }

    static (ExportEnvelope, HashSet<string>) Build(byte[] sourceBytes, string sha, BattleData battle)
    {
        using var document = JsonDocument.Parse(sourceBytes);
        var json = document.RootElement;
        // Counts come from the raw source arrays, independently of the loader, so a dropped definition fails.
        var counts = new BattleContentCounts { Characters = json.GetProperty("characters").GetArrayLength(), Cards = json.GetProperty("cards").GetArrayLength(),
            Weapons = json.GetProperty("weapons").GetArrayLength(), Parts = json.GetProperty("parts").GetArrayLength(), Relics = json.GetProperty("relics").GetArrayLength(),
            Enemies = json.GetProperty("enemies").GetArrayLength(), Zones = json.GetProperty("encounters").EnumerateObject().Count() };
        var envelope = new ExportEnvelope { Schema = Schema, DataVersion = Text(json.GetProperty("meta").GetProperty("version")), SourceSha256 = sha, SourceCounts = counts, Battle = battle };
        foreach (var entry in json.GetProperty("startingCommon").EnumerateObject())
            Add(envelope.StartingCommon, BattleDataLoader.Name(entry), Text(entry.Value), "startingCommon");
        foreach (var character in json.GetProperty("characters").EnumerateArray())
            Add(envelope.CharacterWeaponTypes, Text(character.GetProperty("id")), Text(character.GetProperty("weapon")), "characters[].weapon");
        foreach (var zone in json.GetProperty("encounters").EnumerateObject())
        {
            BattleDataLoader.Fields(zone.Value, "normal", "elite", "boss");
            string[][] Rows(string key) => zone.Value.GetProperty(key).EnumerateArray().Select(row => row.EnumerateArray().Select(Text).ToArray()).ToArray();
            Add(envelope.Encounters, BattleDataLoader.Name(zone), new EncounterSet { Normal = Rows("normal"), Elite = Rows("elite"), Boss = Rows("boss") }, "encounters");
        }
        var registry = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in json.GetProperty("weaponTypes").EnumerateArray())
            if (!registry.Add(Text(type.GetProperty("id")))) throw new InvalidDataException("duplicate weaponTypes id");
        return (envelope, registry);
    }

    // Mirrors ClientBattleContent.Validate so an export that passes here also loads in Unity.
    static void Validate(ExportEnvelope e, HashSet<string> weaponTypeRegistry)
    {
        if (e.Schema != Schema) throw new InvalidDataException("unexpected schema");
        if (string.IsNullOrWhiteSpace(e.DataVersion)) throw new InvalidDataException("meta.version is empty");
        if (e.SourceSha256.Length != 64 || e.SourceSha256.Any(ch => !(ch >= '0' && ch <= '9' || ch >= 'a' && ch <= 'f'))) throw new InvalidDataException("SourceSha256 is not lowercase SHA-256 hex");
        var b = e.Battle; var c = e.SourceCounts;
        if (b.Rules.UpgradeDenominator <= 0) throw new InvalidDataException("Rules.UpgradeDenominator must be positive");
        var characters = Definitions("Characters", b.Characters, c.Characters, x => x.Id, x => x.Name);
        var cards = Definitions("Cards", b.Cards, c.Cards, x => x.Id, x => x.Name);
        Definitions("Weapons", b.Weapons, c.Weapons, x => x.Id, x => x.Name);
        Definitions("Parts", b.Parts, c.Parts, x => x.Id, x => x.Name);
        Definitions("Relics", b.Relics, c.Relics, x => x.Id, x => x.Name);
        var enemies = Definitions("Enemies", b.Enemies, c.Enemies, x => x.Id, x => x.Name);

        if (e.StartingCommon.Count == 0) throw new InvalidDataException("startingCommon is empty");
        foreach (var pair in e.StartingCommon)
        {
            if (!characters.Contains(pair.Key)) throw new InvalidDataException("startingCommon character does not resolve: " + Short(pair.Key));
            if (!cards.Contains(pair.Value)) throw new InvalidDataException("startingCommon card does not resolve: " + Short(pair.Value));
        }
        var weaponTypes = new HashSet<string>(b.Weapons.Select(w => w.Type), StringComparer.Ordinal);
        if (e.CharacterWeaponTypes.Count != b.Characters.Count) throw new InvalidDataException("CharacterWeaponTypes does not cover every character");
        foreach (var character in b.Characters)
        {
            if (!e.CharacterWeaponTypes.TryGetValue(character.Id, out string? type)) throw new InvalidDataException("missing weapon type for character " + Short(character.Id));
            if (!weaponTypeRegistry.Contains(type) || !weaponTypes.Contains(type)) throw new InvalidDataException("weapon type does not resolve: " + Short(type));
        }
        if (e.Encounters.Count != c.Zones || c.Zones <= 0) throw new InvalidDataException("encounter zone count differs from source");
        foreach (var zone in e.Encounters)
            foreach (var (tier, rows) in new[] { ("normal", zone.Value.Normal), ("elite", zone.Value.Elite), ("boss", zone.Value.Boss) })
                foreach (var row in rows)
                {
                    if (row.Length == 0) throw new InvalidDataException("empty encounter in zone " + Short(zone.Key) + " " + tier);
                    foreach (string id in row)
                        if (!enemies.Contains(id)) throw new InvalidDataException("encounter enemy does not resolve: " + Short(id));
                }
    }

    static HashSet<string> Definitions<T>(string kind, List<T> list, int expected, Func<T, string> id, Func<T, string> name)
    {
        if (expected <= 0) throw new InvalidDataException("source has no " + kind);
        if (list.Count != expected) throw new InvalidDataException("Battle." + kind + " has " + list.Count + " definitions but the source has " + expected);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (T item in list)
        {
            if (string.IsNullOrEmpty(id(item)) || !ids.Add(id(item))) throw new InvalidDataException("empty or duplicate " + kind + " id: " + Short(id(item)));
            if (string.IsNullOrEmpty(name(item))) throw new InvalidDataException(kind + " name is empty: " + Short(id(item)));
        }
        return ids;
    }

    // Exact typed comparison of every public field, in authored order; strings compare ordinally.
    static void Same(object? a, object? b, string path)
    {
        if (a is null || b is null)
        {
            if (a is null && b is null) return;
            throw Mismatch(path);
        }
        Type type = a.GetType();
        if (type != b.GetType()) throw Mismatch(path);
        if (a is string text) { if (!string.Equals(text, (string)b, StringComparison.Ordinal)) throw Mismatch(path); return; }
        if (type.IsPrimitive || type.IsEnum || a is decimal) { if (!a.Equals(b)) throw Mismatch(path); return; }
        if (a is IDictionary left)
        {
            var right = (IDictionary)b; if (left.Count != right.Count) throw Mismatch(path);
            IDictionaryEnumerator l = left.GetEnumerator(), r = right.GetEnumerator();
            while (l.MoveNext() && r.MoveNext()) { Same(l.Key, r.Key, path + ".<key>"); Same(l.Value, r.Value, path + "[" + l.Key + "]"); }
            return;
        }
        if (a is IList list)
        {
            var other = (IList)b; if (list.Count != other.Count) throw Mismatch(path);
            for (int i = 0; i < list.Count; i++) Same(list[i], other[i], path + "[" + i + "]");
            return;
        }
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        // StatsConfig is the one exported type made of auto-properties; a public property anywhere else still fails closed.
        if (properties.Length != 0 && type != typeof(StatsConfig)) throw new InvalidDataException("unexpected public property on " + type.Name);
        foreach (PropertyInfo property in properties)
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0) throw new InvalidDataException("unsupported property " + type.Name + "." + property.Name);
            Same(property.GetValue(a), property.GetValue(b), path + "." + property.Name);
        }
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) Same(field.GetValue(a), field.GetValue(b), path + "." + field.Name);
    }
    static InvalidDataException Mismatch(string path) => new InvalidDataException("round trip changed " + Bounded(path));

    static void WriteAtomically(string directory, string output, byte[] bytes)
    {
        // Dot-prefixed temp files are ignored by the Unity importer.
        string temp = Path.Combine(directory, "." + Path.GetFileName(output) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes); stream.Flush(true);
            }
            if (!File.ReadAllBytes(temp).AsSpan().SequenceEqual(bytes)) throw new IOException("temporary export file did not match");
            File.Move(temp, output, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    static void Add<T>(Dictionary<string, T> target, string key, T value, string section)
    {
        if (!target.TryAdd(key, value)) throw new InvalidDataException("duplicate key in " + section + ": " + Short(key));
    }
    static string Text(JsonElement json) => json.ValueKind == JsonValueKind.String ? json.GetString()! : throw new InvalidDataException("Expected JSON string");
    static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static string Short(string? value) => value == null ? "<null>" : value.Length <= 64 ? value : value.Substring(0, 64) + "…";
    static string Bounded(string value)
    {
        value = value.Replace('\r', ' ').Replace('\n', ' ');
        return value.Length <= 400 ? value : value.Substring(0, 400) + "…";
    }
}
