using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MemoryForge.Core;

namespace MemoryForge.Server
{
    static class GrowthPolicy
    {
        public const string Version = "account-progression-20261021-v2";
        public const string Hash = "d771af5acfc5975b32423ec09c737429a14335cf5a8673e0bfeeab735941428f";
        static readonly JsonDocument Source = Load();
        public static readonly string[] Resources = { "credits", "ink", "oil", "seal", "alloy", "book", "polish", "core", "refine_core" };
        static JsonDocument Load()
        {
            using var input = typeof(GrowthPolicy).Assembly.GetManifestResourceStream("MemoryForge.Server.AccountPolicyV2.json") ?? throw new InvalidDataException("missing growth policy");
            using var output = new MemoryStream(); input.CopyTo(output); var bytes = output.ToArray();
            if (GraphJson.Sha256(bytes) != Hash) throw new InvalidDataException("growth policy hash");
            var doc = JsonDocument.Parse(bytes); var root = doc.RootElement;
            if (root.GetProperty("version").GetString() != Version || root.GetProperty("defeatFocusCost").GetInt32() != 0
                || GraphJson.Sha256(Encoding.UTF8.GetBytes(root.GetProperty("sourceModesJson").GetString()!)) != root.GetProperty("sourceModesSha256").GetString()) throw new InvalidDataException("growth policy source");
            return doc;
        }
        public static ContentModesData Modes(PracticeContent content)
        {
            var p = Source.RootElement;
            if (p.GetProperty("mainContentSha256").GetString() != content.ContentSha256) throw new InvalidDataException("growth main content");
            using var doc = JsonDocument.Parse(p.GetProperty("sourceModesJson").GetString()!);
            var modes = ContentModesDataLoader.Load(p.GetProperty("progression"), doc.RootElement, content.Data);
            modes.Dungeon.Encounters = p.GetProperty("encounters").EnumerateObject().ToDictionary(d => d.Name,
                d => d.Value.EnumerateObject().ToDictionary(t => t.Name, t => t.Value.EnumerateArray().Select(w => w.EnumerateArray().Select(e => e.GetString()!).ToList()).ToList(), StringComparer.Ordinal), StringComparer.Ordinal);
            return modes;
        }
        // Trusted registry: v2 and the separately embedded v3 are the only accepted growth policy identities.
        public static void Require(string version, string hash)
        {
            _ = Source.RootElement;
            if(version==SupportPolicy.Version&&hash==SupportPolicy.Hash){SupportPolicy.Verify();return;}
            if (version == EquipmentPolicy.Version && hash == EquipmentPolicy.Hash) { EquipmentPolicy.Verify(); return; }
            if (version != Version || hash != Hash) throw new InvalidDataException("unknown growth policy");
        }
        public static string HashOf(string version) => version == SupportPolicy.Version?SupportPolicy.Hash:version == EquipmentPolicy.Version ? EquipmentPolicy.Hash : Hash;
        // Explicit item grants normalize once; dungeon yields already use these canonical units.
        public static Dictionary<string, long> Normalize(IEnumerable<KeyValuePair<string, long>> award)
        {
            var result = Resources.ToDictionary(x => x, _ => 0L, StringComparer.Ordinal);
            foreach (var p in award)
            {
                if (p.Value < 0) throw new ApiError(422, "RESOURCE_OVERFLOW");
                string key = p.Key; long factor = 1;
                if (key.StartsWith("ink_", StringComparison.Ordinal) || key.StartsWith("oil_", StringComparison.Ordinal))
                { factor = key.Substring(4) switch { "s" => 1000, "m" => 5000, "l" => 20000, _ => throw new InvalidDataException("XP item") }; key = key.Substring(0, 3); }
                if (!result.ContainsKey(key)) throw new InvalidDataException("unknown material");
                try { result[key] = checked(result[key] + p.Value * factor); } catch (OverflowException) { throw new ApiError(422, "RESOURCE_OVERFLOW"); }
            }
            return result;
        }
    }
    sealed class GrowthState
    {
        public Dictionary<string, long> Resources = GrowthPolicy.Resources.ToDictionary(x => x, _ => 0L, StringComparer.Ordinal);
        public Dictionary<string, int> Levels = new Dictionary<string, int>(StringComparer.Ordinal), Stars = new Dictionary<string, int>(StringComparer.Ordinal);
        public long ExtraAccountXp, Anchor; public int Focus = 240; public bool Initialized;
        public string? Active;
        public EquipmentState S3 = new EquipmentState();
        public GrowthState Copy() => new GrowthState { Resources = new Dictionary<string, long>(Resources, StringComparer.Ordinal), Levels = new Dictionary<string, int>(Levels, StringComparer.Ordinal),
            Stars = new Dictionary<string, int>(Stars, StringComparer.Ordinal), ExtraAccountXp = ExtraAccountXp, Anchor = Anchor, Focus = Focus, Initialized = Initialized, Active = Active, S3 = S3.Copy() };
        public void At(long now)
        {
            if (now < 0 || Focus < 0 || Focus > 999) throw new InvalidDataException("growth clock or focus");
            if (!Initialized) return;
            if (Focus >= 240) { Anchor = Math.Max(Anchor, now); return; }
            long earned = Math.Max(0, now - Anchor) / 360000;
            int gain = (int)Math.Min(240 - Focus, earned); Focus += gain;
            Anchor = Focus == 240 ? Math.Max(Anchor, now) : checked(Anchor + gain * 360000L);
        }
        public void Spend(int cost, long now)
        { At(now); if (Focus < cost) throw new ApiError(422, "NOT_ENOUGH_STAMINA"); if (Focus >= 240) Anchor = Math.Max(Anchor, now); Focus -= cost; }
        public void Grant(Dictionary<string, int> grant)
        {
            try
            {
                foreach (var p in grant)
                {
                    if (p.Value < 0) throw new InvalidDataException("negative growth grant");
                    if (p.Key == "accountXp") ExtraAccountXp = checked(ExtraAccountXp + p.Value);
                    else { if (!Resources.ContainsKey(p.Key) || p.Value < 0) throw new InvalidDataException("material grant"); Resources[p.Key] = checked(Resources[p.Key] + p.Value); }
                }
                if (ExtraAccountXp > int.MaxValue) throw new OverflowException();
            }
            catch (OverflowException) { throw new ApiError(422, "RESOURCE_OVERFLOW"); }
        }
        public Dictionary<string, object?> FocusView(long now)
        { var s = Copy(); s.At(now); return GraphJson.Object("value", s.Focus, "cap", 240, "overflowCap", 999, "regenSeconds", 360, "nextRegenAtMs", s.Focus >= 240 ? null : checked(s.Anchor + 360000), "serverNowMs", now); }
    }
    static class GrowthMath
    {
        public static (long Ink, long Credits) Cost(StatsConfig cfg, int current, int xp, int target, int asc = 0)
        {
            int cap = new[] { 20, 30, 40, 50, 60 }[asc];
            if (target > cap || current >= cap) throw new ApiError(422, "LEVEL_CAP");
            if (target <= current || target < 1) throw new ApiError(422, "INVALID_TARGET_LEVEL");
            if (xp < 0 || xp >= Stats.XpToNext(cfg, current) || xp % 2 != 0) throw new InvalidDataException("character XP interval");
            long need = -xp; for (int level = current; level < target; level++) need = checked(need + Stats.XpToNext(cfg, level));
            if (need <= 0 || need % 2 != 0) throw new InvalidDataException("character XP cost");
            return (need, need / 2);
        }
    }
}
