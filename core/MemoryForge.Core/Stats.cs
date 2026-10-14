// Memory Forge v0.7 core — combat stats derived from account growth (content/progression.mjs → stats).
// Integer-only. JS Math.round(a/b) for non-negative values is reproduced as (2a + b) / (2b).
using System;

namespace MemoryForge.Core
{
    public sealed class StatsConfig
    {
        public int LevelCap { get; set; } = 60;
        public int ReferencePower { get; set; } = 40;
        public int ReferenceHpPerChar { get; set; } = 81;
        public int CharPowerBase { get; set; } = 20;
        public int CharPowerNum { get; set; } = 14;
        public int CharPowerDen { get; set; } = 60;
        public int HpBase { get; set; } = 60;
        // weapon power: base + floor(k·wLv/60)
        public int WeaponSsrBase { get; set; } = 2; public int WeaponSsrK { get; set; } = 6;
        public int WeaponSrBase { get; set; } = 1; public int WeaponSrK { get; set; } = 5;
        public int WeaponRBase { get; set; } = 1; public int WeaponRK { get; set; } = 3;
        public int GearMax { get; set; } = 15;
        public int ArmorFlatBase { get; set; } = 2;
        public int ArmorBpsBase { get; set; } = 300;
        public int ArmorBpsPer { get; set; } = 60;
        public int AccessoryBpsBase { get; set; } = 250;
        public int AccessoryBpsPer { get; set; } = 50;
        public int SkillMax { get; set; } = 10;
        public string StageRefWeapon { get; set; } = "SR";
        // levelling: charXp(L) = k·(a + L + floor(L²/sqDiv)); weapon = round(charXp·ratio)
        public int XpK { get; set; } = 40; public int XpA { get; set; } = 4; public int XpSqDiv { get; set; } = 10;
        public int WeaponXpRatioNum { get; set; } = 1; public int WeaponXpRatioDen { get; set; } = 2;
    }

    public sealed class GrowthEntry
    {
        public string CharId { get; set; } = "";
        public int? Level { get; set; }            // null → reference battle values
        public string? WeaponRarity { get; set; }  // SSR | SR | R (null = no weapon)
        public int WeaponLevel { get; set; } = 1;
        public int? Armor { get; set; }            // null = not equipped, 0..15
        public int? Accessory { get; set; }
        public int SkillLv { get; set; } = 1;
        public int UltLv { get; set; } = 1;
    }

    public struct Derived
    {
        public int Power4, Power, Hp, GearBps, SkillLv, UltLv; public bool Reference;
    }

    public static class Stats
    {
        private static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));
        private static int RoundDiv(long a, long b) => (int)((2 * a + b) / (2 * b)); // a,b ≥ 0

        public static int CharPower(StatsConfig c, int lv) => c.CharPowerBase + c.CharPowerNum * lv / c.CharPowerDen;

        public static int WeaponPower(StatsConfig c, string rarity, int wlv)
        {
            switch (rarity)
            {
                case "SSR": return c.WeaponSsrBase + c.WeaponSsrK * wlv / 60;
                case "SR": return c.WeaponSrBase + c.WeaponSrK * wlv / 60;
                default: return c.WeaponRBase + c.WeaponRK * wlv / 60;
            }
        }

        public static Derived Derive(StatsConfig c, GrowthEntry e)
        {
            int skill = Clamp(e.SkillLv, 1, c.SkillMax), ult = Clamp(e.UltLv, 1, c.SkillMax);
            if (e.Level == null) return new Derived { Power4 = 4 * c.ReferencePower, Power = c.ReferencePower, Hp = c.ReferenceHpPerChar, GearBps = 0, SkillLv = skill, UltLv = ult, Reference = true };
            int lv = Clamp(e.Level.Value, 1, c.LevelCap);
            int wl = Clamp(e.WeaponLevel, 1, c.LevelCap);
            int power = CharPower(c, lv) + (e.WeaponRarity != null ? WeaponPower(c, e.WeaponRarity, wl) : 0);
            int baseHp = c.HpBase + lv; int hp = baseHp;
            if (e.Armor != null) { int n = Clamp(e.Armor.Value, 0, c.GearMax); hp += c.ArmorFlatBase + n + baseHp * (c.ArmorBpsBase + c.ArmorBpsPer * n) / 10000; }
            int gear = e.Accessory != null ? c.AccessoryBpsBase + c.AccessoryBpsPer * Clamp(e.Accessory.Value, 0, c.GearMax) : 0;
            return new Derived { Power4 = 4 * power, Power = power, Hp = hp, GearBps = gear, SkillLv = skill, UltLv = ult, Reference = false };
        }

        /// <summary>Percent multipliers (hp, dmg) that make an at-level party equal to the reference battle.</summary>
        public static (int Hp, int Dmg) StageScale(StatsConfig c, int level)
        {
            int lv = Clamp(level, 1, c.LevelCap);
            int pref = CharPower(c, lv) + WeaponPower(c, c.StageRefWeapon, lv);
            return (RoundDiv(100L * pref, c.ReferencePower), RoundDiv(100L * (c.HpBase + lv), c.ReferenceHpPerChar));
        }

        public static int XpToNext(StatsConfig c, int lv, bool weapon = false)
        {
            int x = c.XpK * (c.XpA + lv + lv * lv / c.XpSqDiv);
            return weapon ? RoundDiv((long)x * c.WeaponXpRatioNum, c.WeaponXpRatioDen) : x;
        }
    }
}
