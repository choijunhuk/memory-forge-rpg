// RELEASED V1: never edit these values when adding a new policy; extend the version registry with a separate frozen policy.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace MemoryForge.Server
{
    // Released policy versions are code-pinned immutable values, never the mutable current account or an edited main content file.
    static class AccountPolicy
    {
        public const string Version = "account-progression-20261021-v1";
        // Trusted registry: released source bytes are embedded so running from any cwd/CI output uses exactly the reviewed file.
        const string Sha256 = "e84872ee7792baffdfa4f271aa47fd950411207e236d5f060b9f007c0c45080f";
        static readonly JsonDocument Source = Load();
        static JsonDocument Load()
        {
            using var stream = typeof(AccountPolicy).Assembly.GetManifestResourceStream("MemoryForge.Server.AccountPolicyV1.json") ?? throw new InvalidDataException("missing account policy");
            using var bytes = new MemoryStream(); stream.CopyTo(bytes); byte[] raw = bytes.ToArray();
            if (GraphJson.Sha256(raw) != Sha256) throw new InvalidDataException("account policy digest mismatch");
            var document = JsonDocument.Parse(raw);
            if (document.RootElement.GetProperty("version").GetString() != Version) throw new InvalidDataException("account policy version mismatch");
            return document;
        }
        public static void Verify() { _ = Source.RootElement; }
        public static readonly string[] Starters = Source.RootElement.GetProperty("starters").EnumerateArray().Select(x => x.GetString()!).ToArray();
        public static readonly string[] Basics = Source.RootElement.GetProperty("basics").EnumerateArray().Select(x => x.GetString()!).ToArray();
        public static void Require(string version) { Verify(); if (version != Version) GrowthPolicy.Require(version, GrowthPolicy.HashOf(version)); }
        static JsonElement Chapter(int chapter) => Source.RootElement.GetProperty("chapters").EnumerateArray().FirstOrDefault(x => x.GetProperty("id").GetInt32() == chapter) is var row && row.ValueKind == JsonValueKind.Object ? row : throw new ApiError(422, "CHAPTER_LOCKED");
        public static int Value(int chapter, string field) => Chapter(chapter).GetProperty(field).GetInt32();
        public static int Stage(int chapter) => Value(chapter, "stageLevel");
        public static int Unlock(int chapter) => Value(chapter, "unlockLevel");
        public static int Build(string field) => Source.RootElement.GetProperty("initialBuild").GetProperty(field).GetInt32();
        public static Dictionary<string, object?> Grant(int chapter) => GraphJson.Object("accountXp", Value(chapter, "accountXp"),
            "freeCrystals", Value(chapter, "freeCrystals"), "standardTicket", Value(chapter, "standardTicket"),
            "characters", (chapter == 0 ? Starters : Array.Empty<string>()).Cast<object?>().ToList(),
            "weapons", (chapter == 0 ? Basics : Array.Empty<string>()).Select(w => (object?)GraphJson.Object("instanceId", "basic:" + w, "weaponId", w)).ToList());
        public static (int Level, int Xp) Account(int total)
        {
            var xp = Source.RootElement.GetProperty("accountXp"); int per = xp.GetProperty("per").GetInt32(), basis = xp.GetProperty("base").GetInt32(), cap = xp.GetProperty("cap").GetInt32();
            int level = 1; while (level < cap && total >= per * level + basis) { total -= per * level + basis; level++; }
            return (level, total);
        }
        public static string? Equipped(string weapon) => weapon switch { "w_basic_device" => "nia", "w_basic_melee" => "harin", "w_basic_ritual" => "rien", _ => null };
    }

}
