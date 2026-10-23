// Host-only progression → StatsConfig adapter. CoreChecks and tools/UnityContentExport link this one file (not copies).
// Body moved unchanged from Program.LoadStats; WeaponXpRatioNum/Den are not read here and keep the StatsConfig initializers.
using System.Text.Json;
using MemoryForge.Core;

static class StatsDataLoader
{
    public static StatsConfig Load(JsonElement p)
    {
        var s = p.GetProperty("stats"); var lv = p.GetProperty("levelling");
        var wp = s.GetProperty("weaponPower");
        return new StatsConfig
        {
            LevelCap = s.GetProperty("levelCap").GetInt32(), ReferencePower = s.GetProperty("referencePower").GetInt32(), ReferenceHpPerChar = s.GetProperty("referenceHpPerChar").GetInt32(),
            CharPowerBase = s.GetProperty("charPower").GetProperty("base").GetInt32(), CharPowerNum = s.GetProperty("charPower").GetProperty("num").GetInt32(), CharPowerDen = s.GetProperty("charPower").GetProperty("den").GetInt32(),
            HpBase = s.GetProperty("hpBase").GetInt32(),
            WeaponSsrBase = wp.GetProperty("SSR").GetProperty("base").GetInt32(), WeaponSsrK = wp.GetProperty("SSR").GetProperty("k").GetInt32(),
            WeaponSrBase = wp.GetProperty("SR").GetProperty("base").GetInt32(), WeaponSrK = wp.GetProperty("SR").GetProperty("k").GetInt32(),
            WeaponRBase = wp.GetProperty("R").GetProperty("base").GetInt32(), WeaponRK = wp.GetProperty("R").GetProperty("k").GetInt32(),
            GearMax = s.GetProperty("gear").GetProperty("maxEnhance").GetInt32(), ArmorFlatBase = s.GetProperty("gear").GetProperty("armorFlatBase").GetInt32(),
            ArmorBpsBase = s.GetProperty("gear").GetProperty("armorBpsBase").GetInt32(), ArmorBpsPer = s.GetProperty("gear").GetProperty("armorBpsPer").GetInt32(),
            AccessoryBpsBase = s.GetProperty("gear").GetProperty("accessoryBpsBase").GetInt32(), AccessoryBpsPer = s.GetProperty("gear").GetProperty("accessoryBpsPer").GetInt32(),
            SkillMax = s.GetProperty("skill").GetProperty("maxLevel").GetInt32(), StageRefWeapon = s.GetProperty("stageScale").GetProperty("refWeapon").GetString()!,
            XpK = lv.GetProperty("charXp").GetProperty("k").GetInt32(), XpA = lv.GetProperty("charXp").GetProperty("a").GetInt32(), XpSqDiv = lv.GetProperty("charXp").GetProperty("sqDiv").GetInt32(),
        };
    }
}
