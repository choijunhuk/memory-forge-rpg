// §14.1–14.3/§16.2 battle-practice content. Resources/MemoryForge/battle-content.json is produced only by
// tools/UnityContentExport from unchanged data/game_data.json; this loader never substitutes fallback content.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using MemoryForge.Core;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

namespace MemoryForge.Client
{
    [Preserve]
    public sealed class BattleContentCounts
    {
        public int Characters, Cards, Weapons, Parts, Relics, Enemies, Zones;
    }

    [Preserve]
    public sealed class EncounterSet
    {
        public string[][] Normal = null!, Elite = null!, Boss = null!;
    }

    // Field names match tools/UnityContentExport/Program.cs ExportEnvelope exactly.
    [Preserve]
    public sealed class ClientBattleContent
    {
        public const string SchemaId = "mf-unity-battle-content-v1";
        public const string ResourcePath = "MemoryForge/battle-content";
        const int MaxDepth = 128;

        public string Schema = null!, DataVersion = null!, SourceSha256 = null!;
        public BattleContentCounts SourceCounts = null!;
        public BattleData Battle = null!;
        public Dictionary<string, string> StartingCommon = null!;
        public Dictionary<string, string> CharacterWeaponTypes = null!;
        public Dictionary<string, EncounterSet> Encounters = null!;

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error, ObjectCreationHandling = ObjectCreationHandling.Replace,
            NullValueHandling = NullValueHandling.Include, MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            TypeNameHandling = TypeNameHandling.None, DateParseHandling = DateParseHandling.None,
            CheckAdditionalContent = true, MaxDepth = MaxDepth
        };

        // Returns a fresh instance each call; BattleData is mutable, so callers cache deliberately.
        public static ClientBattleContent Load()
        {
            TextAsset? asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
                throw Invalid("Resources/" + ResourcePath + ".json was not found; export it with: dotnet run --project tools/UnityContentExport -- <repoRoot> <outputPath>");
            try { return FromJson(asset.text); }
            finally { Resources.UnloadAsset(asset); }
        }

        public static ClientBattleContent FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw Invalid("content JSON is empty");
            ClientBattleContent? content;
            try
            {
                RejectDuplicateMembers(json);
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = MaxDepth })
                    content = JsonSerializer.Create(Settings).Deserialize<ClientBattleContent>(reader);
            }
            catch (JsonException e) { throw Invalid("content JSON could not be read: " + e.Message); }
            if (content == null) throw Invalid("content JSON is null");
            content.Validate();
            try { content.Battle.Index(); }
            catch (ArgumentException e) { throw Invalid("Battle.Index failed: " + e.Message); }
            return content;
        }

        // Newtonsoft keeps the last duplicate silently; reject them so no authored value is dropped.
        internal static void RejectDuplicateMembers(string json)
        {
            var scopes = new Stack<HashSet<string>?>();
            using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = MaxDepth })
                while (reader.Read())
                    switch (reader.TokenType)
                    {
                        case JsonToken.StartObject: scopes.Push(new HashSet<string>(StringComparer.Ordinal)); break;
                        case JsonToken.StartArray: scopes.Push(null); break;
                        case JsonToken.EndObject: case JsonToken.EndArray: scopes.Pop(); break;
                        case JsonToken.Comment: throw Invalid("comments are not allowed at " + reader.Path);
                        case JsonToken.PropertyName:
                            if (!scopes.Peek()!.Add((string)reader.Value!)) throw Invalid("duplicate member at " + reader.Path);
                            break;
                    }
        }

        // Mirrors the exporter's Validate; counts are the raw source array lengths recorded at export.
        void Validate()
        {
            if (Schema != SchemaId) throw Invalid("schema must be '" + SchemaId + "' but was '" + Short(Schema) + "'");
            if (string.IsNullOrWhiteSpace(DataVersion)) throw Invalid("DataVersion is empty");
            if (!IsSha256(SourceSha256)) throw Invalid("SourceSha256 must be 64 lowercase hex characters");
            if (SourceCounts == null) throw Invalid("SourceCounts is missing");
            if (Battle == null || Battle.Rules == null) throw Invalid("Battle or Battle.Rules is missing");
            if (Battle.Rules.UpgradeDenominator <= 0) throw Invalid("Battle.Rules.UpgradeDenominator must be positive");

            var characters = Definitions("Characters", Battle.Characters, SourceCounts.Characters, x => x.Id, x => x.Name);
            foreach (var x in Battle.Characters)
                if (x.Passive?.Params == null || x.Ult?.Effects == null || x.Resonance == null) throw Invalid("character definition is incomplete: " + Short(x.Id));
            var cards = Definitions("Cards", Battle.Cards, SourceCounts.Cards, x => x.Id, x => x.Name);
            foreach (var x in Battle.Cards)
                if (x.Keywords == null || x.Effects == null) throw Invalid("card definition is incomplete: " + Short(x.Id));
            Definitions("Weapons", Battle.Weapons, SourceCounts.Weapons, x => x.Id, x => x.Name);
            foreach (var x in Battle.Weapons)
                if (x.Passive?.Values == null) throw Invalid("weapon definition is incomplete: " + Short(x.Id));
            Definitions("Parts", Battle.Parts, SourceCounts.Parts, x => x.Id, x => x.Name);
            foreach (var x in Battle.Parts)
                if (x.Fx == null) throw Invalid("part definition is incomplete: " + Short(x.Id));
            Definitions("Relics", Battle.Relics, SourceCounts.Relics, x => x.Id, x => x.Name);
            var enemies = Definitions("Enemies", Battle.Enemies, SourceCounts.Enemies, x => x.Id, x => x.Name);
            foreach (var x in Battle.Enemies)
                if (x.Weak == null || x.Pattern == null) throw Invalid("enemy definition is incomplete: " + Short(x.Id));

            if (StartingCommon == null || StartingCommon.Count == 0) throw Invalid("StartingCommon is missing or empty");
            foreach (var pair in StartingCommon)
            {
                if (!characters.Contains(pair.Key)) throw Invalid("StartingCommon character does not resolve: " + Short(pair.Key));
                if (pair.Value == null || !cards.Contains(pair.Value)) throw Invalid("StartingCommon card does not resolve: " + Short(pair.Value));
            }

            if (CharacterWeaponTypes == null || CharacterWeaponTypes.Count != Battle.Characters.Count) throw Invalid("CharacterWeaponTypes must cover every character");
            var weaponTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var weapon in Battle.Weapons) weaponTypes.Add(weapon.Type);
            foreach (var character in Battle.Characters)
            {
                if (!CharacterWeaponTypes.TryGetValue(character.Id, out string? type) || type == null) throw Invalid("missing weapon type for character " + Short(character.Id));
                if (!weaponTypes.Contains(type)) throw Invalid("weapon type does not resolve: " + Short(type));
            }

            if (Encounters == null || Encounters.Count == 0 || Encounters.Count != SourceCounts.Zones)
                throw Invalid("Encounters has " + (Encounters?.Count ?? 0) + " zones but the source had " + SourceCounts.Zones);
            foreach (var zone in Encounters)
            {
                if (zone.Value == null) throw Invalid("encounter zone " + Short(zone.Key) + " is null");
                Rows(zone.Key, "Normal", zone.Value.Normal, enemies);
                Rows(zone.Key, "Elite", zone.Value.Elite, enemies);
                Rows(zone.Key, "Boss", zone.Value.Boss, enemies);
            }
        }

        static void Rows(string zone, string tier, string[][]? rows, HashSet<string> enemies)
        {
            if (rows == null) throw Invalid("Encounters[" + Short(zone) + "]." + tier + " is missing");
            foreach (var row in rows)
            {
                if (row == null || row.Length == 0) throw Invalid("empty encounter in Encounters[" + Short(zone) + "]." + tier);
                foreach (string id in row)
                    if (id == null || !enemies.Contains(id)) throw Invalid("encounter enemy does not resolve: " + Short(id));
            }
        }

        static HashSet<string> Definitions<T>(string kind, List<T>? list, int expected, Func<T, string?> id, Func<T, string?> name) where T : class
        {
            if (list == null) throw Invalid("Battle." + kind + " is missing");
            if (expected <= 0) throw Invalid("SourceCounts." + kind + " must be positive");
            if (list.Count != expected) throw Invalid("Battle." + kind + " has " + list.Count + " definitions but the source had " + expected);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++)
            {
                T? item = list[i];
                if (item == null) throw Invalid("Battle." + kind + "[" + i + "] is null");
                string? value = id(item);
                if (string.IsNullOrEmpty(value) || !ids.Add(value!)) throw Invalid("empty or duplicate " + kind + " id at index " + i + ": " + Short(value));
                if (string.IsNullOrEmpty(name(item))) throw Invalid(kind + " name is empty: " + Short(value));
            }
            return ids;
        }

        static bool IsSha256(string? value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
            return true;
        }
        static string Short(string? value) => value == null ? "<null>" : value.Length <= 64 ? value : value.Substring(0, 64) + "…";
        static InvalidDataException Invalid(string message)
        {
            message = message.Replace('\r', ' ').Replace('\n', ' ');
            return new InvalidDataException("battle-content: " + (message.Length <= 400 ? message : message.Substring(0, 400) + "…"));
        }
    }
}
