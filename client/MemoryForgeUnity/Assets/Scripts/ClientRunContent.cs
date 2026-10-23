// §7.5–7.7/§8.9–8.14/§14/§20.9 Run content for the actual Unity runtime. Resources/MemoryForge/run-content.json is written
// by tools/UnityContentExport in the same run as battle-content.json, from unchanged data/game_data.json. This loader never
// fills in default stats, free memory costs or missing events: any missing, mistyped or unmatched value is an error.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MemoryForge.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;
using UnityEngine.Scripting;

namespace MemoryForge.Client
{
    // Field names match tools/UnityContentExport/Program.cs RunExportEnvelope exactly.
    [Preserve]
    internal sealed class RunContentEnvelope
    {
        public string Schema = null!, DataVersion = null!, SourceSha256 = null!;
        public int SourceEventCount;
        public StatsConfig Stats = null!;
        public MemoryCostConfig Costs = null!;
        public int CapacityBase;
        public List<MemoryAscensionCapacity> AscensionCapacity = null!;
        public List<RunEventDefinition> Events = null!;
    }

    public static class ClientRunContent
    {
        public const string SchemaId = "mf-unity-run-content-v1";
        public const string ResourcePath = "MemoryForge/run-content";
        const int MaxDepth = 128;

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new StrictResolver(),
            MissingMemberHandling = MissingMemberHandling.Error, ObjectCreationHandling = ObjectCreationHandling.Replace,
            NullValueHandling = NullValueHandling.Include, MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            TypeNameHandling = TypeNameHandling.None, DateParseHandling = DateParseHandling.None,
            CheckAdditionalContent = true, MaxDepth = MaxDepth
        };

        // Returns fresh RunData (over a freshly loaded battle content) each call; Run.Create indexes and keeps what it is given.
        public static RunData Load()
        {
            ClientBattleContent battle = ClientBattleContent.Load();
            TextAsset? asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
                throw Invalid("Resources/" + ResourcePath + ".json was not found; export it with: dotnet run --project tools/UnityContentExport -- <repoRoot> <battleOutputPath>");
            try { return FromJson(asset.text, battle); }
            finally { Resources.UnloadAsset(asset); }
        }

        // The result uses battle.Battle itself (not a copy); pass a fresh ClientBattleContent when callers must not share it.
        public static RunData FromJson(string json, ClientBattleContent battle)
        {
            if (battle == null || battle.Battle == null || battle.StartingCommon == null || battle.CharacterWeaponTypes == null || battle.Encounters == null)
                throw Invalid("battle content is required");
            if (string.IsNullOrWhiteSpace(json)) throw Invalid("content JSON is empty");
            RunContentEnvelope? envelope;
            try
            {
                try { ClientBattleContent.RejectDuplicateMembers(json); }
                catch (InvalidDataException e) { throw Invalid(e.Message.StartsWith("battle-content: ", StringComparison.Ordinal) ? e.Message.Substring(16) : e.Message); }
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = MaxDepth })
                    envelope = JsonSerializer.Create(Settings).Deserialize<RunContentEnvelope>(reader);
            }
            catch (JsonException e) { throw Invalid("content JSON could not be read: " + e.Message); }
            if (envelope == null) throw Invalid("content JSON is null");
            Validate(envelope, battle);

            var data = new RunData
            {
                Memory = new MemoryRulesData { Battle = battle.Battle, StartingCommon = new Dictionary<string, string>(battle.StartingCommon, StringComparer.Ordinal),
                    Costs = envelope.Costs, CapacityBase = envelope.CapacityBase, AscensionCapacity = envelope.AscensionCapacity },
                Stats = envelope.Stats, Events = envelope.Events
            };
            // Same order as the host RunDataLoader: source characters, then source encounter zones.
            foreach (var character in battle.Battle.Characters)
            {
                if (!battle.CharacterWeaponTypes.TryGetValue(character.Id, out string? type) || type == null) throw Invalid("missing weapon type for character " + Short(character.Id));
                data.Characters.Add(new RunCharacterMetadata { Id = character.Id, WeaponType = type });
            }
            foreach (var zone in battle.Encounters)
                data.Encounters.Add(new RunEncounterDefinition { Zone = zone.Key, Normal = Rows(zone.Value.Normal), Elite = Rows(zone.Value.Elite), Boss = Rows(zone.Value.Boss) });
            return data;
        }

        static List<List<string>> Rows(string[][] rows) => rows.Select(row => row.ToList()).ToList();

        // Mirrors the exporter's ValidateRun. Ranges guard integrity only; authored zero costs stay valid and no economic rule is added.
        static void Validate(RunContentEnvelope e, ClientBattleContent battle)
        {
            if (e.Schema != SchemaId) throw Invalid("schema must be '" + SchemaId + "' but was '" + Short(e.Schema) + "'");
            if (string.IsNullOrWhiteSpace(e.DataVersion) || e.DataVersion != battle.DataVersion)
                throw Invalid("DataVersion '" + Short(e.DataVersion) + "' does not match battle content '" + Short(battle.DataVersion) + "'");
            if (string.IsNullOrEmpty(e.SourceSha256) || e.SourceSha256 != battle.SourceSha256) throw Invalid("SourceSha256 does not match battle content");
            if (e.Stats == null || e.Costs == null || e.AscensionCapacity == null || e.Events == null) throw Invalid("Stats, Costs, AscensionCapacity or Events is missing");

            var stats = e.Stats;
            foreach (PropertyInfo property in typeof(StatsConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (property.PropertyType == typeof(int) && (int)property.GetValue(stats)! < 0) throw Invalid("Stats." + property.Name + " is negative");
            if (stats.LevelCap <= 0 || stats.SkillMax <= 0 || stats.CharPowerDen <= 0 || stats.XpSqDiv <= 0 || stats.WeaponXpRatioDen <= 0)
                throw Invalid("Stats cap or denominator is not positive");
            if (!battle.Battle.Weapons.Any(w => w.Rarity == stats.StageRefWeapon)) throw Invalid("Stats.StageRefWeapon is not a weapon rarity: " + Short(stats.StageRefWeapon));

            var cost = e.Costs;
            if (new[] { cost.AddB, cost.AddA, cost.AddS, cost.AddCommon, cost.Upgrade, cost.PartCommon, cost.PartRare, cost.Evolution, cost.Remove }.Any(x => x < 0))
                throw Invalid("memory cost is negative");
            if (e.CapacityBase < 0) throw Invalid("CapacityBase is negative");
            foreach (var rule in e.AscensionCapacity)
                if (rule == null || rule.Asc < 0 || rule.Add < 0) throw Invalid("AscensionCapacity rule is null or negative");

            if (e.SourceEventCount <= 0 || e.Events.Count != e.SourceEventCount)
                throw Invalid("Events has " + e.Events.Count + " definitions but the source had " + e.SourceEventCount);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < e.Events.Count; i++)
            {
                RunEventDefinition? item = e.Events[i];
                if (item == null) throw Invalid("Events[" + i + "] is null");
                if (string.IsNullOrEmpty(item.Id) || !ids.Add(item.Id)) throw Invalid("empty or duplicate event id at index " + i + ": " + Short(item.Id));
                if (string.IsNullOrEmpty(item.Name) || string.IsNullOrEmpty(item.Text) || item.Choices == null || item.Choices.Count == 0)
                    throw Invalid("event definition is incomplete: " + Short(item.Id));
                foreach (RunEventChoice? choice in item.Choices)
                {
                    if (choice == null || choice.Effects == null) throw Invalid("event choice or its effects is null: " + Short(item.Id));
                    if (string.IsNullOrEmpty(choice.Label) || string.IsNullOrEmpty(choice.Text)) throw Invalid("event choice text is empty: " + Short(item.Id));
                    if (choice.RequiredCoins < 0) throw Invalid("event choice need.coins is negative: " + Short(item.Id));
                    var fx = choice.Effects;
                    if (fx.Curse != null && !battle.Battle.Cards.Any(x => x.Id == fx.Curse && x.Owner == "curse")) throw Invalid("event curse does not resolve: " + Short(fx.Curse));
                    if (fx.Card != null && !battle.Battle.Cards.Any(x => x.Rarity == fx.Card)) throw Invalid("event card rarity does not resolve: " + Short(fx.Card));
                    if (fx.Relic != null && fx.Relic.Length == 0) throw Invalid("event relic is empty: " + Short(item.Id));
                }
            }
        }

        // Core types carry no Json.NET attributes, so the sidecar contract is enforced here: every member must be present
        // (event effects and need.coins may be null, which means absent in the source) and must have its exact JSON token type.
        sealed class StrictResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                JsonProperty property = base.CreateProperty(member, memberSerialization);
                bool optional = property.DeclaringType == typeof(RunEventEffects)
                    || property.DeclaringType == typeof(RunEventChoice) && property.UnderlyingName == nameof(RunEventChoice.RequiredCoins);
                property.Required = optional ? Required.AllowNull : Required.Always;
                if (property.PropertyType == typeof(int) || property.PropertyType == typeof(int?)) property.Converter = StrictInteger.Instance;
                else if (property.PropertyType == typeof(string)) property.Converter = StrictString.Instance;
                return property;
            }
        }

        // Json.NET would otherwise accept "1" or truncate-check floats into ints; only 32-bit JSON integers are accepted.
        sealed class StrictInteger : JsonConverter
        {
            internal static readonly StrictInteger Instance = new StrictInteger();
            public override bool CanConvert(Type objectType) => objectType == typeof(int) || objectType == typeof(int?);
            public override bool CanWrite => false;
            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => throw new NotSupportedException();
            public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null && objectType == typeof(int?)) return null;
                if (reader.TokenType != JsonToken.Integer || !(reader.Value is long value) || value < int.MinValue || value > int.MaxValue)
                    throw new JsonSerializationException("expected a 32-bit JSON integer at " + reader.Path);
                return (int)value;
            }
        }

        // Json.NET would otherwise turn numbers and booleans into strings.
        sealed class StrictString : JsonConverter
        {
            internal static readonly StrictString Instance = new StrictString();
            public override bool CanConvert(Type objectType) => objectType == typeof(string);
            public override bool CanWrite => false;
            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => throw new NotSupportedException();
            public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return null;
                if (reader.TokenType != JsonToken.String) throw new JsonSerializationException("expected a JSON string at " + reader.Path);
                return (string)reader.Value!;
            }
        }

        static string Short(string? value) => value == null ? "<null>" : value.Length <= 64 ? value : value.Substring(0, 64) + "…";
        static InvalidDataException Invalid(string message)
        {
            message = message.Replace('\r', ' ').Replace('\n', ' ');
            return new InvalidDataException("run-content: " + (message.Length <= 400 ? message : message.Substring(0, 400) + "…"));
        }
    }
}
