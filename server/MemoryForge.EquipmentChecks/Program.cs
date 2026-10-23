// S3 pure frozen-cost vectors against independently written expected tables (checkpoint 1).
// EquipmentJourney uses the unchanged pre-S3 earned S1/S2 envelope; EquipmentRestartChecks audits the resulting real S3 history.
using System;
using System.Linq;
using MemoryForge.Core;
using MemoryForge.Server;

static class EquipmentChecks
{
    static int passed, failed;
    static void Check(bool ok, string name) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL " + name); } }
    static string Code(Action a) { try { a(); return "OK"; } catch (ApiError e) { return e.Code; } }
    public static int Run()
    {
        var cfg = new StatsConfig();
        // independent expected tables (contract §3–§7), not read back from the policy
        var charAsc = new[] { (30, 10, 4L, 0L, 10000L), (40, 20, 8L, 0L, 20000L), (50, 30, 12L, 2L, 40000L), (60, 40, 16L, 4L, 60000L) };
        for (int i = 0; i < 4; i++) Check(EquipmentPolicy.CharAscension(i + 1) == charAsc[i], "char asc " + (i + 1));
        Check(Enumerable.Range(1, 4).Sum(i => EquipmentPolicy.CharAscension(i).Core) == 6, "char asc core total 6");
        var weaponAsc = new[] { (30, 2L, 5000L), (40, 4L, 10000L), (50, 6L, 20000L), (60, 8L, 30000L) };
        for (int i = 0; i < 4; i++) Check(EquipmentPolicy.WeaponAscension(i + 1) == weaponAsc[i], "weapon asc " + (i + 1));
        var skill = new[] { (2L, 5000L, 0L), (4L, 10000L, 0L), (6L, 15000L, 0L), (8L, 20000L, 0L), (10L, 30000L, 0L), (14L, 40000L, 0L), (18L, 60000L, 0L), (24L, 80000L, 2L), (32L, 120000L, 2L) };
        for (int i = 0; i < 9; i++) Check(EquipmentPolicy.Skill(i + 2) == skill[i], "skill to " + (i + 2));
        Check(skill.Sum(x => x.Item1) == 118 && skill.Sum(x => x.Item2) == 380000 && skill.Sum(x => x.Item3) == 4, "one skill line 118/380000/4");
        Check(6 + 2 * skill.Sum(x => x.Item3) == 14, "aggregate core 14");
        Check(EquipmentPolicy.Craft == (20L, 10000L), "craft 20/10000");
        long polish = 0, credits = 0;
        for (int n = 1; n <= 15; n++) { var r = EquipmentPolicy.Enhance(n); Check(r == (n, 1000L * n), "enhance " + n); polish += r.Polish; credits += r.Credits; }
        Check(polish == 120 && credits == 120000 && 2 * (polish + 20) == 280 && 2 * (credits + 10000) == 260000, "gear totals");
        var shop = EquipmentPolicy.Shop.ToList();
        Check(shop.Count == 3 && shop[0] == ("refine_core", 60L, 2) && shop[1] == ("book", 10L, 6) && shop[2] == ("polish", 5L, 20), "boss shop rows");
        Check(EquipmentMath.WeaponCost(cfg, 1, 0, 2, 0) == (100L, 50L), "weapon Lv1→2 100 oil 50 credits");
        Check(4800 - EquipmentMath.WeaponCost(cfg, 1, 0, 2, 0).Oil == 4700, "4800 oil leaves 4700");
        long need = 0; for (int l = 1; l < 20; l++) need += Stats.XpToNext(cfg, l, true);
        Check(EquipmentMath.WeaponCost(cfg, 1, 0, 20, 0) == (need, need / 2), "weapon Lv1→20 exact sum");
        Check(Code(() => EquipmentMath.WeaponCost(cfg, 20, 0, 21, 0)) == "LEVEL_CAP", "weapon cap asc0");
        Check(Code(() => EquipmentMath.WeaponCost(cfg, 1, 0, 21, 0)) == "LEVEL_CAP", "weapon target over cap");
        Check(Code(() => EquipmentMath.WeaponCost(cfg, 5, 0, 5, 0)) == "INVALID_TARGET_LEVEL", "weapon same target");
        Check(Code(() => EquipmentMath.WeaponCost(cfg, 20, 0, 30, 1)) == "OK", "weapon asc1 cap30");
        Check(EquipmentMath.Effective(10, 10, 6) == (12, 12) && EquipmentMath.Effective(1, 1, 2) == (1, 1) && EquipmentMath.Effective(1, 1, 3) == (3, 1), "resonance bonus once");
        int hp(int? armor) => Stats.Derive(cfg, new GrowthEntry { Level = 1, Armor = armor }).Hp;
        Check(hp(null) == 61 && hp(0) == 64 && hp(1) == 66, "armor HP 61/64/66");
        int bps(int? acc) => Stats.Derive(cfg, new GrowthEntry { Level = 1, Accessory = acc }).GearBps;
        Check(bps(null) == 0 && bps(0) == 250 && bps(15) == 1000, "accessory 0/250/1000 bps");
        for(int rank=1;rank<=4;rank++)Check(EquipmentPolicy.RRefineCost(rank)==1,"R refinement next cost1 stage"+rank);
        Check(Code(()=>EquipmentPolicy.RRefineCost(5))=="REFINE_CAP","R5 refuses next");
        Check(EquipmentMath.WeaponCost(cfg,1,40,2,0)==(60L,30L),"weapon XP carry40 exact");
        Console.WriteLine($"EquipmentChecks: {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
