using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MemoryForge.Core;

namespace MemoryForge.Server
{
    // S3 frozen v3 policy: separate embedded bytes and trusted hash; v1/v2 constants and resources are untouched.
    static class EquipmentPolicy
    {
        public const string Version = "account-progression-20261021-v3";
        public const string Hash = "b13963c1cc6a25e90bfae1d98ed471ad8cd0b8a6e6510e601d32322542a7d924";
        static readonly JsonDocument Source = Load();
        static JsonDocument Load()
        {
            using var input = typeof(EquipmentPolicy).Assembly.GetManifestResourceStream("MemoryForge.Server.AccountPolicyV3.json") ?? throw new InvalidDataException("missing equipment policy");
            using var output = new MemoryStream(); input.CopyTo(output); var bytes = output.ToArray();
            if (GraphJson.Sha256(bytes) != Hash) throw new InvalidDataException("equipment policy hash");
            var doc = JsonDocument.Parse(bytes); var root = doc.RootElement;
            var r = root.GetProperty("approvedOverrides").GetProperty("rWeaponRefine");
            if (root.GetProperty("version").GetString() != Version || r.GetProperty("material").GetString() != "refine_core" || r.GetProperty("count").GetInt32() != 1) throw new InvalidDataException("equipment policy source");
            return doc;
        }
        public static void Verify() { _ = Source.RootElement; }
        static JsonElement Levelling => Source.RootElement.GetProperty("progression").GetProperty("levelling");
        static IEnumerable<JsonElement> Rows(string name) => Levelling.GetProperty(name).EnumerateArray();
        public static readonly int[] Caps = { 20, 30, 40, 50, 60 };
        // next character ascension stage 1..4: (cap, account, seal, core, credits)
        public static (int Cap, int Account, long Seal, long Core, long Credits) CharAscension(int stage)
            => Rows("charAscension").Where(x => x.GetProperty("stage").GetInt32() == stage).Select(x => (x.GetProperty("cap").GetInt32(), x.GetProperty("account").GetInt32(),
                (long)x.GetProperty("seals").GetInt32(), (long)x.GetProperty("cores").GetInt32(), (long)x.GetProperty("credits").GetInt32())).Single();
        public static (int Cap, long Alloy, long Credits) WeaponAscension(int stage)
            => Rows("weaponAscension").Where(x => x.GetProperty("stage").GetInt32() == stage).Select(x => (x.GetProperty("cap").GetInt32(), (long)x.GetProperty("alloys").GetInt32(), (long)x.GetProperty("credits").GetInt32())).Single();
        public static (long Book, long Credits, long Core) Skill(int to)
            => Rows("skillCosts").Where(x => x.GetProperty("to").GetInt32() == to).Select(x => ((long)x.GetProperty("books").GetInt32(), (long)x.GetProperty("credits").GetInt32(), (long)x.GetProperty("cores").GetInt32())).Single();
        public static (long Polish, long Credits) Craft => ((long)Levelling.GetProperty("gearCraft").GetProperty("polish").GetInt32(), (long)Levelling.GetProperty("gearCraft").GetProperty("credits").GetInt32());
        public static (long Polish, long Credits) Enhance(int n) => (checked((long)Levelling.GetProperty("gearEnhance").GetProperty("polishPerLevel").GetInt32() * n), checked((long)Levelling.GetProperty("gearEnhance").GetProperty("creditsPerLevel").GetInt32() * n));
        public static string[] SkillLines => Levelling.GetProperty("skillLines").EnumerateArray().Select(x => x.GetString()!).ToArray();
        public const int SkillMax = 10, GearMax = 15, RefineMax = 5, CraftUnlock = 10;
        public static IEnumerable<(string Item, long Cost, int Limit)> Shop => Source.RootElement.GetProperty("progression").GetProperty("exchangeShops").GetProperty("bossToken").GetProperty("items").EnumerateArray()
            .Select(x => (x.GetProperty("item").GetString()!, (long)x.GetProperty("cost").GetInt32(), x.GetProperty("limit").GetInt32()));
        public static long RRefineCost(int rank)
        {
            if(rank>=RefineMax)throw new ApiError(422,"REFINE_CAP");
            if(rank<1)throw new InvalidDataException("R refinement rank");
            return Source.RootElement.GetProperty("approvedOverrides").GetProperty("rWeaponRefine").GetProperty("count").GetInt64();
        }
        public static ContentModesData Modes(PracticeContent content)
        {
            var p = Source.RootElement;
            var modes = ContentModesDataLoader.Load(p.GetProperty("progression"), p.GetProperty("contentModes"), content.Data);
            modes.Dungeon.Encounters = GrowthPolicy.Modes(content).Dungeon.Encounters;
            return modes;
        }
        public static int WeeklyLimit => Source.RootElement.GetProperty("progression").GetProperty("weeklyBoss").GetProperty("claimsPerWeek").GetInt32();
        public static int WeeklyXp => Source.RootElement.GetProperty("progression").GetProperty("accountLevel").GetProperty("xpSources").GetProperty("weeklyBossClaim").GetInt32();
        public static (string Key,long ResetsAtMs) WeekAt(long now)
        {
            var r=Source.RootElement.GetProperty("contentModes").GetProperty("resets");
            return Week(new ContentModesData{Resets=new ContentResets{UtcOffsetMinutes=r.GetProperty("utcOffsetMinutes").GetInt32(),WeeklyHour=r.GetProperty("weeklyHour").GetInt32(),WeeklyWeekday=r.GetProperty("weeklyWeekday").GetInt32()}},now);
        }
        // Week key and next reset come only from Core Time.WeekKey over the frozen resets; no posted clock exists.
        public static (string Key, long ResetsAtMs) Week(ContentModesData modes, long nowMs)
        {
            string key = ContentModes.Time.WeekKey(modes, nowMs);
            long midnight = new DateTimeOffset(DateTime.ParseExact(key, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal)).ToUnixTimeMilliseconds();
            return (key, checked(midnight - modes.Resets.UtcOffsetMinutes * 60000L + modes.Resets.WeeklyHour * 3600000L + 7 * 86400000L));
        }
    }
    // Typed v3 build/equipment state replayed from v3 facts over the S1 instances. Absent = Lv1/Asc0/skill1/R1/uncrafted null.
    sealed class EquipmentState
    {
        public Dictionary<string, int> Asc = new Dictionary<string, int>(StringComparer.Ordinal), Skill = new Dictionary<string, int>(StringComparer.Ordinal), Ult = new Dictionary<string, int>(StringComparer.Ordinal),
            Armor = new Dictionary<string, int>(StringComparer.Ordinal), Accessory = new Dictionary<string, int>(StringComparer.Ordinal),
            WLevel = new Dictionary<string, int>(StringComparer.Ordinal), WAsc = new Dictionary<string, int>(StringComparer.Ordinal), RRefine = new Dictionary<string, int>(StringComparer.Ordinal),
            Bought = new Dictionary<string, int>(StringComparer.Ordinal), Claims = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, string?> Equip = new Dictionary<string, string?>(StringComparer.Ordinal);
        public long BossToken;
        static Dictionary<string, int> C(Dictionary<string, int> d) => new Dictionary<string, int>(d, StringComparer.Ordinal);
        public EquipmentState Copy() => new EquipmentState { Asc = C(Asc), Skill = C(Skill), Ult = C(Ult), Armor = C(Armor), Accessory = C(Accessory), WLevel = C(WLevel), WAsc = C(WAsc), RRefine = C(RRefine),
            Bought = C(Bought), Claims = C(Claims), Equip = new Dictionary<string, string?>(Equip, StringComparer.Ordinal), BossToken = BossToken };
        public int? Gear(string cid, string slot) => (slot == "armor" ? Armor : Accessory).TryGetValue(cid, out int n) ? n : null;
        public string? EquippedBy((string Instance, string Weapon, string? Equipped) w) => Equip.TryGetValue(w.Instance, out var by) ? by : w.Equipped;
    }
    static class EquipmentMath
    {
        // S2 exact-target algorithm over the weapon threshold; credits are exactly half the oil.
        public static (long Oil, long Credits) WeaponCost(StatsConfig cfg, int current, int xp, int target, int asc)
        {
            int cap = EquipmentPolicy.Caps[asc];
            if (target > cap || current >= cap) throw new ApiError(422, "LEVEL_CAP");
            if (target <= current || target < 1) throw new ApiError(422, "INVALID_TARGET_LEVEL");
            if (xp < 0 || xp >= Stats.XpToNext(cfg, current, true)) throw new InvalidDataException("weapon XP interval");
            long need = -xp; for (int level = current; level < target; level++) need = checked(need + Stats.XpToNext(cfg, level, true));
            if (need <= 0 || need % 2 != 0) throw new InvalidDataException("weapon XP cost");
            return (need, need / 2);
        }
        // Frozen authored resonance effect (R3 +2 card, R5 +2 ult); stored base ranks never include it.
        public static (int Card, int Ult) Effective(int skill, int ult, int res) => (skill + (res >= 3 ? 2 : 0), ult + (res >= 5 ? 2 : 0));
    }
}
