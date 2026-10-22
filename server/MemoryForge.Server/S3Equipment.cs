using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MemoryForge.Core;

namespace MemoryForge.Server
{
    sealed record EquipmentPlan(Dictionary<string, object?> Target, Dictionary<string, object?> Before, Dictionary<string, object?> After, Dictionary<string, long> Cost, Dictionary<string, long> Gain);
    // S3 build/equipment actions over the S2 growth ledger: typed v3 facts, exact receipts, one global Revision.
    static partial class GrowthCommands
    {
        public static bool Active(AccountEnvelope e, long before = long.MaxValue) => SupportActive(e,before)||e.Growth?.Ledger.Any(f => f.PolicyVersion == EquipmentPolicy.Version && f.Revision < before) == true || e.Progression?.Ledger.Any(f=>f.PolicyVersion==EquipmentPolicy.Version && f.Revision<before)==true;
        // Once an account has any v3 fact every later fact is pinned v3; untouched S2 accounts keep v2 bytes.
        static (string Version, string Hash) Pin(AccountEnvelope e, string kind)
            => SupportActive(e)||kind is "encounterStart" or "weeklyBossStart" or "supportSelection"||Contracts.EquipmentActions.ContainsKey(kind)?(SupportPolicy.Version,SupportPolicy.Hash):Active(e)?(EquipmentPolicy.Version,EquipmentPolicy.Hash):(GrowthPolicy.Version,GrowthPolicy.Hash);
        static (string Type, string Rarity) WeaponDef(PracticeContent c, string wid) { var d = c.Data.Memory.Battle.Weapons.Single(w => w.Id == wid); return (d.Type, d.Rarity); }
        static string CharType(PracticeContent c, string cid) => c.Data.Characters.Single(x => x.Id == cid).WeaponType;
        static int Refine(PracticeContent c, AccountEnvelope e, EquipmentState b, (string Instance, string Weapon, string? Equipped) w, long before)
            => WeaponDef(c, w.Weapon).Rarity == "R" ? b.RRefine.GetValueOrDefault(w.Instance, 1) : Math.Min(5, ProgressionCommands.Copies(e, true, before).GetValueOrDefault(w.Weapon));
        internal static EquipmentState BuildAt(PracticeContent c, AccountEnvelope e, long before)
        {
            var s = new EquipmentState();
            foreach (var f in e.Growth?.Ledger.Where(f => f.Revision < before && Contracts.EquipmentActions.ContainsKey(f.Kind)).ToList() ?? new List<GrowthFact>()) Step(c, e, s, f.Kind, f.Request, f.Revision + 1);
            return s;
        }
        static void Step(PracticeContent c, AccountEnvelope e, EquipmentState s, string kind, Dictionary<string, object?> a, long before)
        {
            switch (kind)
            {
                case "characterAscend": { string cid = Text(a, "charId"); s.Asc[cid] = checked(s.Asc.GetValueOrDefault(cid) + 1); break; }
                case "skillUp": { string cid = Text(a, "charId"); var d = Text(a, "line") == "card" ? s.Skill : s.Ult; d[cid] = checked(d.GetValueOrDefault(cid, 1) + 1); break; }
                case "gearCraft": { (Text(a, "slot") == "armor" ? s.Armor : s.Accessory)[Text(a, "charId")] = 0; break; }
                case "gearEnhance": { string cid = Text(a, "charId"); var g = Text(a, "slot") == "armor" ? s.Armor : s.Accessory; g[cid] = checked(g[cid] + 1); break; }
                case "weaponLevelUp": { s.WLevel[Text(a, "instanceId")] = Int(a, "targetLevel"); break; }
                case "weaponAscend": { string x = Text(a, "instanceId"); s.WAsc[x] = checked(s.WAsc.GetValueOrDefault(x) + 1); break; }
                case "weaponRefine": { string x = Text(a, "instanceId"); s.RRefine[x] = checked(s.RRefine.GetValueOrDefault(x, 1) + 1); break; }
                case "weaponEquip":
                {
                    string x = Text(a, "instanceId"); string? to = a["charId"] as string;
                    // X onto B clears X's old character A (by moving X) and B's old weapon Y to unequipped; Y never moves onto A.
                    if (to != null) foreach (var w in ProgressionCommands.Weapons(c, e, before)) if (w.Instance != x && s.EquippedBy(w) == to) s.Equip[w.Instance] = null;
                    s.Equip[x] = to; break;
                }
                case "bossExchange": break;
                default: throw new System.IO.InvalidDataException("equipment kind");
            }
        }
        // v3 launch party: the EXACT explicitly equipped instance/build of each member; no automatic weapon choice.
        internal static (List<Dictionary<string, object?>> Party, List<string> Instances) V3Party(PracticeContent c, AccountEnvelope e, IEnumerable<string> party, long before)
        {
            var owned = ProgressionCommands.Copies(e, false, before); var weapons = ProgressionCommands.Weapons(c, e, before); var b = BuildAt(c, e, before);
            var result = new List<Dictionary<string, object?>>(); var instances = new List<string>();
            foreach (string id in party)
            {
                if (!owned.ContainsKey(id)) throw new ApiError(422, "NOT_OWNED");
                var held = weapons.Where(w => b.EquippedBy(w) == id).ToList(); Require(held.Count <= 1);
                if (held.Count == 0 || WeaponDef(c, held[0].Weapon).Type != CharType(c, id)) throw new ApiError(422, "NO_ELIGIBLE_WEAPON");
                var w = held[0]; Require(!instances.Contains(w.Instance));
                result.Add(GraphJson.Object("charId", id, "level", CharacterLevel(e, id, before), "asc", b.Asc.GetValueOrDefault(id), "res", Math.Min(6, owned[id] - 1),
                    "skillLv", b.Skill.GetValueOrDefault(id, 1), "ultLv", b.Ult.GetValueOrDefault(id, 1), "armor", b.Gear(id, "armor"), "accessory", b.Gear(id, "accessory"),
                    "weaponId", w.Weapon, "weaponLevel", b.WLevel.GetValueOrDefault(w.Instance, 1), "refine", Refine(c, e, b, w, before)));
                instances.Add(w.Instance);
            }
            return (result, instances);
        }
        internal static ExpeditionLaunch BuildLaunchV3(PracticeContent c, AccountEnvelope e, ExpeditionRequest q)
        {
            // Same S1/S2 story gates as ProgressionCommands.Build; only the party source changes to explicit equipment.
            int chapter = q.Mode == "tutorial" ? 0 : q.Chapter ?? -1; int stage = AccountPolicy.Stage(chapter); var progress = e.Progression;
            if (q.Mode == "tutorial") throw new ApiError(422, "TUTORIAL_DONE");
            if (chapter != 1) throw new ApiError(422, "CHAPTER_LOCKED");
            if (progress?.Cleared(0, q.ExpectedRevision + 1) != true) throw new ApiError(422, "TUTORIAL_REQUIRED");
            if (progress.Account(q.ExpectedRevision + 1).Level < AccountPolicy.Unlock(chapter)) throw new ApiError(422, "CHAPTER_LOCKED");
            if (q.Party == null || q.Party.Count != 3) throw new ApiError(422, "PARTY_SIZE");
            if (q.Party.Distinct(StringComparer.Ordinal).Count() != 3) throw new ApiError(422, "PARTY_DUPLICATE");
            var built = V3Party(c, e, q.Party, q.ExpectedRevision + 1);
            return new ExpeditionLaunch { Mode = q.Mode, Chapter = chapter, StageLevel = stage, Revision = q.ExpectedRevision, CoreModule = c.CoreModule, ContentSha256 = c.ContentSha256,
                UseMemory = q.UseMemory, PolicyVersion = EquipmentPolicy.Version, PolicyHash = EquipmentPolicy.Hash, Party = built.Party, WeaponInstances = built.Instances };
        }
        static Dictionary<string, object?> StatsOf(PracticeContent c, int level, string? rarity, int weaponLevel, int? armor, int? accessory, int skill, int ult)
        {
            var d = Stats.Derive(c.Data.Stats, new GrowthEntry { Level = level, WeaponRarity = rarity, WeaponLevel = weaponLevel, Armor = armor, Accessory = accessory, SkillLv = skill, UltLv = ult });
            return GraphJson.Object("power4", d.Power4, "power", d.Power, "hp", d.Hp, "gearBps", d.GearBps, "skillLv", d.SkillLv, "ultLv", d.UltLv, "reference", d.Reference);
        }
        static (int Card,int Ult) EffectiveRanks(PracticeContent c,string cid,int skill,int ult,int res)
        {
            void Apply(ResonanceFx fx)
            {
                if(fx.Type=="multi") foreach(var inner in fx.List!) Apply(inner);
                else if(fx.Type=="skillLv") skill=checked(skill+fx.Delta!.Value);
                else if(fx.Type=="ultLv") ult=checked(ult+fx.Delta!.Value);
            }
            foreach(var fx in c.Data.Memory.Battle.Characters.Single(x=>x.Id==cid).Resonance.Take(res)) Apply(fx);
            return (skill,ult);
        }
        static Dictionary<string,object?> EquippedLevelRow(PracticeContent c,AccountEnvelope e,GrowthState s,string cid,int level,int asc,long before)
        {
            var next=s.Copy();next.Levels[cid]=level;
            var row=CharView(c,e,next,cid,before);
            return GraphJson.Object("level",level,"xp",0,"cap",EquipmentPolicy.Caps[asc],"stats",row["stats"]);
        }
        static Dictionary<string, object?> CharView(PracticeContent c, AccountEnvelope e, GrowthState s, string cid, long before)
        {
            var b = s.S3; int asc = b.Asc.GetValueOrDefault(cid), level = s.Levels.GetValueOrDefault(cid, 1), copies = ProgressionCommands.Copies(e, false, before).GetValueOrDefault(cid), res = Math.Min(6, copies - 1);
            int skill = b.Skill.GetValueOrDefault(cid, 1), ult = b.Ult.GetValueOrDefault(cid, 1); var eff = EffectiveRanks(c, cid, skill, ult, res);
            var weapons = ProgressionCommands.Weapons(c, e, before); var held = weapons.Where(w => b.EquippedBy(w) == cid).Select(w => (w.Instance, w.Weapon)).FirstOrDefault();
            string type = CharType(c, cid);
            return GraphJson.Object("charId", cid, "copies", copies, "res", res, "level", level, "xp", 0, "asc", asc, "cap", EquipmentPolicy.Caps[asc],
                "skillLv", skill, "ultLv", ult, "effectiveSkillLv", eff.Card, "effectiveUltLv", eff.Ult,
                "capacity", MemoryV2.CapacityOf(c.Data.Memory, new MemoryCapacityEntry { CharId = cid, Res = res, Asc = asc }),
                "weaponInstanceId", held.Instance, "weaponType", type, "deployable", held.Instance != null,
                "eligibleWeapons", weapons.Where(w => WeaponDef(c, w.Weapon).Type == type).Select(w => (object?)w.Instance).ToList(),
                "armor", b.Gear(cid, "armor"), "accessory", b.Gear(cid, "accessory"),
                "stats", StatsOf(c, level, held.Weapon == null ? null : WeaponDef(c, held.Weapon).Rarity, held.Instance == null ? 1 : b.WLevel.GetValueOrDefault(held.Instance, 1), b.Gear(cid, "armor"), b.Gear(cid, "accessory"), skill, ult));
        }
        static Dictionary<string, object?> WeaponView(PracticeContent c, AccountEnvelope e, GrowthState s, (string Instance, string Weapon, string? Equipped) w, long before)
        {
            var b = s.S3; var d = WeaponDef(c, w.Weapon); int asc = b.WAsc.GetValueOrDefault(w.Instance);
            return GraphJson.Object("instanceId", w.Instance, "weaponId", w.Weapon, "type", d.Type, "rarity", d.Rarity,
                "copies", w.Instance.StartsWith("basic:", StringComparison.Ordinal) ? 1 : ProgressionCommands.Copies(e, true, before).GetValueOrDefault(w.Weapon),
                "refineMode", d.Rarity == "R" ? "core" : "copy", "level", b.WLevel.GetValueOrDefault(w.Instance, 1), "xp", 0, "asc", asc, "cap", EquipmentPolicy.Caps[asc],
                "refine", Refine(c, e, b, w, before), "equippedBy", b.EquippedBy(w));
        }
        static (string Instance, string Weapon, string? Equipped) OwnedWeapon(PracticeContent c, AccountEnvelope e, string id, long before)
            => ProgressionCommands.Weapons(c, e, before).Where(w => w.Instance == id).Cast<(string, string, string?)?>().SingleOrDefault() ?? throw new ApiError(422, "WEAPON_NOT_OWNED");
        static void OwnedCharacter(AccountEnvelope e, string cid, long before) { if (!ProgressionCommands.Copies(e, false, before).ContainsKey(cid)) throw new ApiError(422, "NOT_OWNED"); }
        static Dictionary<string, long> Cost(params (string Key, long Value)[] rows) { var d = new Dictionary<string, long>(StringComparer.Ordinal); foreach (var r in rows) if (r.Value != 0) d[r.Key] = r.Value; return d; }
        static long Balance(GrowthState s, string key) => key == "boss_token" ? s.S3.BossToken : s.Resources[key];
        static bool Enough(GrowthState s, Dictionary<string, long> cost) => cost.All(p => Balance(s, p.Key) >= p.Value);
        static EquipmentPlan PlanEquipment(PracticeContent c, AccountEnvelope e, GrowthState s, GrowthRequest q, long now)
        {
            long before = q.ExpectedRevision + 1; var a = q.Args; var b = s.S3; var none = new Dictionary<string, long>(StringComparer.Ordinal);
            GrowthState After() { var next = s.Copy(); Step(c, e, next.S3, q.Kind, a, before); return next; }
            if (q.Kind == "bossExchange")
            {
                string item = Text(a, "itemId"); int quantity = Int(a, "quantity"); var row = EquipmentPolicy.Shop.Where(x => x.Item == item).ToList();
                if (row.Count != 1 || quantity < 1) throw new ApiError(422, "INVALID_EXCHANGE");
                var week = EquipmentPolicy.Week(EquipmentPolicy.Modes(c), now); int bought = b.Bought.GetValueOrDefault(week.Key + "|" + item);
                if ((long)bought + quantity > row[0].Limit) throw new ApiError(422, "EXCHANGE_LIMIT");
                Dictionary<string, object?> View(int n, long tokens, long held) => GraphJson.Object("itemId", item, "weekKey", week.Key, "resetsAtMs", week.ResetsAtMs, "bought", n, "remaining", row[0].Limit - n, "boss_token", tokens, "balance", held);
                long price = checked(row[0].Cost * quantity);
                return new EquipmentPlan(GraphJson.Object("itemId", item, "quantity", quantity), View(bought, b.BossToken, s.Resources[item]),
                    View(bought + quantity, b.BossToken - price, checked(s.Resources[item] + quantity)), Cost(("boss_token", price)), new Dictionary<string, long>(StringComparer.Ordinal) { [item] = quantity });
            }
            if (Contracts.EquipmentActions[q.Kind].Target == "charId")
            {
                string cid = Text(a, "charId"); OwnedCharacter(e, cid, before); Dictionary<string, long> cost;
                if (q.Kind == "characterAscend")
                {
                    int asc = b.Asc.GetValueOrDefault(cid); if (asc >= 4) throw new ApiError(422, "ASCENSION_CAP");
                    var row = EquipmentPolicy.CharAscension(asc + 1);
                    if (s.Levels.GetValueOrDefault(cid, 1) < EquipmentPolicy.Caps[asc]) throw new ApiError(422, "ASCENSION_LEVEL_REQUIRED");
                    if (Account(e, before).Level < row.Account) throw new ApiError(422, "ACCOUNT_LEVEL_REQUIRED");
                    cost = Cost(("seal", row.Seal), ("core", row.Core), ("credits", row.Credits));
                }
                else if (q.Kind == "skillUp")
                {
                    string line = Text(a, "line"); if (!EquipmentPolicy.SkillLines.Contains(line)) throw new ApiError(422, "INVALID_SKILL_LINE");
                    int rank = (line == "card" ? b.Skill : b.Ult).GetValueOrDefault(cid, 1); if (rank >= EquipmentPolicy.SkillMax) throw new ApiError(422, "SKILL_CAP");
                    var row = EquipmentPolicy.Skill(rank + 1); cost = Cost(("book", row.Book), ("credits", row.Credits), ("core", row.Core));
                }
                else
                {
                    string slot = Text(a, "slot"); if (slot != "armor" && slot != "accessory") throw new ApiError(422, "INVALID_GEAR_SLOT"); int? n = b.Gear(cid, slot);
                    if (q.Kind == "gearCraft")
                    {
                        if (Account(e, before).Level < EquipmentPolicy.CraftUnlock) throw new ApiError(422, "ACCOUNT_LEVEL_REQUIRED");
                        if (n != null) throw new ApiError(422, "GEAR_ALREADY_CRAFTED"); var row = EquipmentPolicy.Craft; cost = Cost(("polish", row.Polish), ("credits", row.Credits));
                    }
                    else
                    {
                        if (n == null) throw new ApiError(422, "GEAR_NOT_CRAFTED"); if (n >= EquipmentPolicy.GearMax) throw new ApiError(422, "GEAR_CAP");
                        var row = EquipmentPolicy.Enhance(n.Value + 1); cost = Cost(("polish", row.Polish), ("credits", row.Credits));
                    }
                }
                return new EquipmentPlan(GraphJson.Object("charId", cid), CharView(c, e, s, cid, before), CharView(c, e, After(), cid, before), cost, none);
            }
            var w = OwnedWeapon(c, e, Text(a, "instanceId"), before); var def = WeaponDef(c, w.Weapon);
            int level = b.WLevel.GetValueOrDefault(w.Instance, 1), wasc = b.WAsc.GetValueOrDefault(w.Instance); Dictionary<string, long> wcost = none;
            if (q.Kind == "weaponEquip")
            {
                string? to = a["charId"] as string; string? from = b.EquippedBy(w);
                if (to != null) { OwnedCharacter(e, to, before); if (CharType(c, to) != def.Type) throw new ApiError(422, "WEAPON_TYPE_MISMATCH"); }
                if (to == from) throw new ApiError(422, "ALREADY_EQUIPPED");
                string? displaced = to == null ? null : ProgressionCommands.Weapons(c, e, before).Where(x => x.Instance != w.Instance && b.EquippedBy(x) == to).Select(x => x.Instance).SingleOrDefault();
                return new EquipmentPlan(GraphJson.Object("instanceId", w.Instance, "charId", to),
                    GraphJson.Object("instanceId", w.Instance, "equippedBy", from, "charId", to, "charWeaponInstanceId", displaced),
                    GraphJson.Object("instanceId", w.Instance, "equippedBy", to, "charId", to, "charWeaponInstanceId", to == null ? null : w.Instance,
                        "displaced", GraphJson.Object("charId", from, "instanceId", displaced)), none, none);
            }
            if (q.Kind == "weaponLevelUp") { var row = EquipmentMath.WeaponCost(c.Data.Stats, level, 0, Int(a, "targetLevel"), wasc); wcost = Cost(("oil", row.Oil), ("credits", row.Credits)); }
            else if (q.Kind == "weaponAscend")
            {
                if (wasc >= 4) throw new ApiError(422, "ASCENSION_CAP"); if (level < EquipmentPolicy.Caps[wasc]) throw new ApiError(422, "ASCENSION_LEVEL_REQUIRED");
                var row = EquipmentPolicy.WeaponAscension(wasc + 1); wcost = Cost(("alloy", row.Alloy), ("credits", row.Credits));
            }
            else
            {
                if (def.Rarity != "R") throw new ApiError(422, "AUTO_REFINED");
                wcost = Cost(("refine_core", EquipmentPolicy.RRefineCost(b.RRefine.GetValueOrDefault(w.Instance,1))));
            }
            return new EquipmentPlan(GraphJson.Object("instanceId", w.Instance), WeaponView(c, e, s, w, before), WeaponView(c, e, After(), w, before), wcost, none);
        }
        static Dictionary<string, object?> CostView(Dictionary<string, long> cost) => cost.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal);
        static Dictionary<string, object?> ApplyEquipment(PracticeContent c, PracticeAccount account, AccountEnvelope e, GrowthTrace trace, GrowthFact f, GrowthRequest q)
        {
            Require(f.PolicyVersion is EquipmentPolicy.Version or SupportPolicy.Version && f.PolicyHash == GrowthPolicy.HashOf(f.PolicyVersion)); var s = trace.State; var plan = PlanEquipment(c, e, s, q, f.AtMs);
            if (!Enough(s, plan.Cost)) throw new ApiError(422, "INSUFFICIENT_RESOURCES");
            try
            {
                foreach (var p in plan.Cost) if (p.Key == "boss_token") s.S3.BossToken -= p.Value; else s.Resources[p.Key] -= p.Value;
                foreach (var p in plan.Gain) s.Resources[p.Key] = checked(s.Resources[p.Key] + p.Value);
            }
            catch (OverflowException) { throw new ApiError(422, "RESOURCE_OVERFLOW"); }
            if (q.Kind == "bossExchange") { string key = EquipmentPolicy.Week(EquipmentPolicy.Modes(c), f.AtMs).Key + "|" + Text(q.Args, "itemId"); s.S3.Bought[key] = checked(s.S3.Bought.GetValueOrDefault(key) + Int(q.Args, "quantity")); }
            else Step(c, e, s.S3, q.Kind, q.Args, f.Revision + 1);
            return GraphJson.Object("requestId", f.RequestId, "revision", f.Revision + 1, "kind", q.Kind, "target", plan.Target, "before", plan.Before, "after", plan.After,
                "cost", CostView(plan.Cost), "profile", ProfileAt(c, account, e, trace, f));
        }
        public static Reply EquipmentPreview(PracticeContent c, AccountEnvelope? e, GrowthRequest q)
        {
            if (e == null) throw new ApiError(422, "TUTORIAL_REQUIRED");
            if (e.Revision != q.ExpectedRevision) throw new ApiError(409, "STALE_REVISION"); Tutorial(e, e.Revision + 1);
            var state = e.GrowthCache?.State ?? new GrowthState(); var plan = PlanEquipment(c, e, state, q, Clock()); bool enough = Enough(state, plan.Cost);
            var g = GraphJson.Object("revision", e.Revision, "kind", q.Kind, "target", plan.Target, "before", plan.Before, "after", plan.After, "cost", CostView(plan.Cost), "canCommit", enough);
            if (!enough) g.Add("code", "INSUFFICIENT_RESOURCES"); return Reply.Json(200, g);
        }
        // v3 projection only: v2 receipts never pass through here, so they gain no v3 fields.
        static void Enrich(PracticeContent c, AccountEnvelope? e, GrowthState s, Dictionary<string, object?> view, long at, long before)
        {
            var resources = s.Resources.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal); resources["boss_token"] = s.S3.BossToken; view["resources"] = resources;
            view["policyVersion"] = EquipmentPolicy.Version;
            WeeklyProfile(c, e, s, view, at, before);
            if (e == null) { AddBossShop(c,s,view,at); return; }
            view["characters"] = ProgressionCommands.Copies(e, false, before).Keys.OrderBy(x => x, StringComparer.Ordinal).Select(cid => (object?)CharView(c, e, s, cid, before)).ToList();
            view["weapons"] = ProgressionCommands.Weapons(c, e, before).Select(w => (object?)WeaponView(c, e, s, w, before)).ToList();
            AddBossShop(c,s,view,at);
        }
        static void AddBossShop(PracticeContent c, GrowthState s, Dictionary<string,object?> view, long at)
        {
            var week = EquipmentPolicy.Week(EquipmentPolicy.Modes(c), at);
            view["bossShop"] = GraphJson.Object("weekKey", week.Key, "resetsAtMs", week.ResetsAtMs, "items", EquipmentPolicy.Shop.Select(x =>
            { int n = s.S3.Bought.GetValueOrDefault(week.Key + "|" + x.Item); return (object?)GraphJson.Object("itemId", x.Item, "unitCost", x.Cost, "limit", x.Limit, "bought", n, "remaining", x.Limit - n); }).ToList());
        }
    }
    static class EquipmentRoutes
    {
        static readonly Dictionary<string, string> Characters = new Dictionary<string, string>(StringComparer.Ordinal) { ["ascend"] = "characterAscend", ["skill-up"] = "skillUp", ["gear-craft"] = "gearCraft", ["gear-enhance"] = "gearEnhance" };
        static readonly Dictionary<string, string> Weapons = new Dictionary<string, string>(StringComparer.Ordinal) { ["level-up"] = "weaponLevelUp", ["ascend"] = "weaponAscend", ["refine"] = "weaponRefine", ["equip"] = "weaponEquip" };
        static readonly Dictionary<string, string> WeaponPreviews = new Dictionary<string, string>(StringComparer.Ordinal) { ["level-preview"] = "weaponLevelUp", ["ascend-preview"] = "weaponAscend", ["refine-preview"] = "weaponRefine", ["equip-preview"] = "weaponEquip" };
        // Returns null when the path is not an S3 action so the dispatcher keeps its existing 404.
        public static (string Kind, bool Preview)? Match(string scope, string action)
        {
            if (scope == "characters") { if (Characters.TryGetValue(action, out var k)) return (k, false); if (action.EndsWith("-preview", StringComparison.Ordinal) && Characters.TryGetValue(action[..^8], out k)) return (k, true); }
            if (scope == "weapons") { if (Weapons.TryGetValue(action, out var k)) return (k, false); if (WeaponPreviews.TryGetValue(action, out k)) return (k, true); }
            if (scope == "boss-shop") { if (action == "exchange") return ("bossExchange", false); if (action == "exchange-preview") return ("bossExchange", true); }
            return null;
        }
        public static async Task<Reply> Execute(PracticeHost h, PracticeAccount a, string kind, string? target, byte[] body, bool preview)
        {
            var q = Contracts.ParseEquipment(body, kind, target, preview);
            try
            {
                return preview ? await h.Store.Read(a, (e, _) => GrowthCommands.EquipmentPreview(h.Content, e, q))
                    : await h.Store.Mutate(a, q.RequestId, q.Identity, (e, r) => GrowthCommands.Mutate(h.Content, a, e, r, q));
            }
            catch (RuleException error) { throw new ApiError(422, error.Code); }
            catch (MemoryBoundaryException) { throw new ApiError(422, "COMMAND_REJECTED"); }
            catch (OverflowException) { throw new ApiError(422, "RESOURCE_OVERFLOW"); }
        }
    }
}
