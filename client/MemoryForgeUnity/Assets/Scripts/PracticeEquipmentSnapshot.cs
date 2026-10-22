#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using P = MemoryForge.Client.PracticePublicJson;

namespace MemoryForge.Client
{
    static class EquipmentJson
    {
        internal const string Policy = "account-progression-20261021-v3", ExchangeRoute = "/dev/practice/progression/boss-shop/exchange";
        internal static readonly string[] ResourceKeys = GrowthJson.ResourceKeys.Concat(new[] { "boss_token" }).ToArray();
        internal static readonly string[] CharacterFields = { "cap", "effectiveSkillLv", "effectiveUltLv", "capacity", "weaponType", "deployable", "eligibleWeapons", "stats" };
        internal static readonly string[] WeaponFields = { "type", "rarity", "copies", "refineMode", "cap" };
        internal static readonly string[] Kinds = { "characterAscend", "skillUp", "gearCraft", "gearEnhance", "weaponLevelUp", "weaponAscend", "weaponRefine", "weaponEquip", "bossExchange" };
        static readonly string[] Actions = { "ascend", "skill-up", "gear-craft", "gear-enhance", "level-up", "ascend", "refine", "equip", "exchange" };
        internal static string Route(string kind, string? target, bool preview = false)
        {
            int index = Array.IndexOf(Kinds, kind);
            if (index < 0 || index != 8 && (target == null || !PracticeCommandOutbox.IsPublicId(target)) || index == 8 && target != null)
                throw new ArgumentException("equipment target/kind");
            string action = Actions[index];
            if (preview) action = kind == "weaponLevelUp" ? "level-preview" : action + "-preview";
            return index == 8 ? "/dev/practice/progression/boss-shop/" + action : "/dev/practice/progression/" + (index < 4 ? "characters/" : "weapons/") + target + "/" + action;
        }
        internal static string? Kind(string route, out string? target)
        {
            target = null;
            if (route == ExchangeRoute) return "bossExchange";
            for (int i = 0; i < 8; i++)
            {
                string prefix = "/dev/practice/progression/" + (i < 4 ? "characters/" : "weapons/"), suffix = "/" + Actions[i];
                if (!route.StartsWith(prefix, StringComparison.Ordinal) || !route.EndsWith(suffix, StringComparison.Ordinal) || route.Length <= prefix.Length + suffix.Length) continue;
                string id = route.Substring(prefix.Length, route.Length - prefix.Length - suffix.Length);
                if (PracticeCommandOutbox.IsPublicId(id)) { target = id; return Kinds[i]; }
            }
            return null;
        }
        internal static string[] IntentFields(string kind) => kind == "skillUp" ? new[] { "line" } : kind == "gearCraft" || kind == "gearEnhance" ? new[] { "slot" }
            : kind == "weaponLevelUp" ? new[] { "targetLevel" } : kind == "weaponEquip" ? new[] { "charId" } : kind == "bossExchange" ? new[] { "itemId", "quantity" } : new string[0];
        internal static bool Intent(string kind, JObject args)
        {
            if (!Kinds.Contains(kind, StringComparer.Ordinal)) return false;
            try
            {
                ProgressionJson.Fields(args, IntentFields(kind));
                if (kind == "skillUp") RecruitmentJson.Choice(args, "line", "card", "ult");
                if (kind == "gearCraft" || kind == "gearEnhance") RecruitmentJson.Choice(args, "slot", "armor", "accessory");
                if (kind == "weaponLevelUp") ProgressionJson.Number(args, "targetLevel", 1, 60);
                if (kind == "weaponEquip") { P.Member(args, "charId"); ProgressionJson.OptionalId(args, "charId"); }
                if (kind == "bossExchange") { RecruitmentJson.Choice(args, "itemId", "refine_core", "book", "polish"); ProgressionJson.Number(args, "quantity", 1, 20); }
                return true;
            }
            catch (PracticeProtocolException) { return false; }
        }
        internal static IReadOnlyDictionary<string, long> Resources(JObject json)
        {
            ProgressionJson.Fields(json, ResourceKeys);
            return new ReadOnlyDictionary<string, long>(ResourceKeys.ToDictionary(key => key, key => RecruitmentJson.Long(json, key), StringComparer.Ordinal));
        }
        internal static string Week(JObject json, string name = "weekKey")
        {
            string value = P.Text(json, name);
            return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? value : throw P.Invalid(name);
        }
        internal static void Claims(JObject json, out int claimed, out int remaining)
        { claimed = ProgressionJson.Number(json, "claimed", 0, 3); remaining = ProgressionJson.Number(json, "remaining", 0, 3); if (claimed + remaining != 3) throw P.Invalid("weekly allowance"); }
        internal static IReadOnlyDictionary<string, long> Cost(JObject json, string kind)
        {
            string[] allowed = kind == "characterAscend" ? new[] { "seal", "core", "credits" } : kind == "skillUp" ? new[] { "book", "credits", "core" }
                : kind == "gearCraft" || kind == "gearEnhance" ? new[] { "polish", "credits" } : kind == "weaponLevelUp" ? new[] { "oil", "credits" }
                : kind == "weaponAscend" ? new[] { "alloy", "credits" } : kind == "weaponRefine" ? new[] { "refine_core" } : kind == "bossExchange" ? new[] { "boss_token" } : new string[0];
            ProgressionJson.Fields(json, new string[0], allowed);
            var values = json.Properties().ToDictionary(p => p.Name, p => RecruitmentJson.Long(json, p.Name), StringComparer.Ordinal);
            if (kind != "weaponEquip" && (values.Count == 0 || values.Values.All(value => value == 0))) throw P.Invalid("equipment cost");
            if (kind == "weaponRefine" && (values.Count != 1 || values["refine_core"] != 1)) throw P.Invalid("refine cost");
            return new ReadOnlyDictionary<string, long>(values);
        }
    }

    public sealed class EquipmentShopItem
    {
        internal EquipmentShopItem(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "itemId", "unitCost", "limit", "bought", "remaining" });
            ItemId = RecruitmentJson.Choice(json, "itemId", "refine_core", "book", "polish"); UnitCost = RecruitmentJson.Long(json, "unitCost");
            Limit = ProgressionJson.Number(json, "limit", 1, 20); Bought = P.Count(json, "bought"); Remaining = P.Count(json, "remaining");
            if ((long)Bought + Remaining != Limit || UnitCost == 0) throw P.Invalid("shop allowance");
        }
        public string ItemId { get; } public long UnitCost { get; } public int Limit { get; } public int Bought { get; } public int Remaining { get; }
    }
    public sealed class EquipmentBossShop
    {
        internal EquipmentBossShop(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "weekKey", "resetsAtMs", "items" }); WeekKey = EquipmentJson.Week(json); ResetsAtMs = RecruitmentJson.Long(json, "resetsAtMs");
            Items = RecruitmentJson.List(json, "items", item => new EquipmentShopItem(item));
            if (Items.Select(item => item.ItemId).Distinct(StringComparer.Ordinal).Count() != Items.Count) throw P.Invalid("duplicate shop item");
        }
        public string WeekKey { get; } public long ResetsAtMs { get; } public IReadOnlyList<EquipmentShopItem> Items { get; }
    }
    public sealed class EquipmentWeeklyDifficulty
    {
        internal EquipmentWeeklyDifficulty(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "difficulty", "stage", "hpBps", "dmgBps" }); Difficulty = ProgressionJson.Number(json, "difficulty", 1, 5);
            Stage = ProgressionJson.Number(json, "stage", 1, 60); HpBps = ProgressionJson.Number(json, "hpBps", 0); DmgBps = ProgressionJson.Number(json, "dmgBps", 0);
        }
        public int Difficulty { get; } public int Stage { get; } public int HpBps { get; } public int DmgBps { get; }
    }
    public sealed class EquipmentWeeklyBoss
    {
        internal EquipmentWeeklyBoss(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "id", "unlockLevel", "available", "difficulties" });
            Id = RecruitmentJson.Choice(json, "id", "b_bell", "b_notary", "b_garden", "b_archivist"); UnlockLevel = ProgressionJson.Number(json, "unlockLevel", 15, 15); Available = RecruitmentJson.Bool(json, "available");
            Difficulties = RecruitmentJson.List(json, "difficulties", item => new EquipmentWeeklyDifficulty(item));
            if (Difficulties.Select(item => item.Difficulty).Distinct().Count() != Difficulties.Count) throw P.Invalid("duplicate weekly difficulty");
        }
        public string Id { get; } public int UnlockLevel { get; } public bool Available { get; } public IReadOnlyList<EquipmentWeeklyDifficulty> Difficulties { get; }
    }
    public sealed class EquipmentWeeklyProfile
    {
        internal EquipmentWeeklyProfile(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "weekKey", "resetsAtMs", "claimed", "remaining", "bosses" });
            WeekKey = EquipmentJson.Week(json); ResetsAtMs = RecruitmentJson.Long(json, "resetsAtMs"); EquipmentJson.Claims(json, out int claimed, out int remaining); Claimed = claimed; Remaining = remaining;
            Bosses = RecruitmentJson.List(json, "bosses", item => new EquipmentWeeklyBoss(item));
            if (Bosses.Select(item => item.Id).Distinct().Count() != Bosses.Count) throw P.Invalid("duplicate weekly boss");
        }
        public string WeekKey { get; } public long ResetsAtMs { get; } public int Claimed { get; } public int Remaining { get; } public IReadOnlyList<EquipmentWeeklyBoss> Bosses { get; }
    }
    public sealed class EquipmentAssignment
    {
        internal EquipmentAssignment(JObject json, bool after)
        {
            ProgressionJson.Fields(json, after ? new[] { "instanceId", "equippedBy", "charId", "charWeaponInstanceId", "displaced" } : new[] { "instanceId", "equippedBy", "charId", "charWeaponInstanceId" });
            InstanceId = RecruitmentJson.Id(json, "instanceId"); EquippedBy = ProgressionJson.OptionalId(json, "equippedBy"); CharId = ProgressionJson.OptionalId(json, "charId"); CharWeaponInstanceId = ProgressionJson.OptionalId(json, "charWeaponInstanceId");
            if (after) { var displaced = P.Object(json, "displaced"); ProgressionJson.Fields(displaced, new[] { "charId", "instanceId" }); DisplacedCharId = ProgressionJson.OptionalId(displaced, "charId"); DisplacedInstanceId = ProgressionJson.OptionalId(displaced, "instanceId"); }
        }
        public string InstanceId { get; } public string? EquippedBy { get; } public string? CharId { get; } public string? CharWeaponInstanceId { get; }
        public string? DisplacedCharId { get; } public string? DisplacedInstanceId { get; }
    }
    public sealed class EquipmentShopBalance
    {
        internal EquipmentShopBalance(JObject json, bool projected)
        {
            ProgressionJson.Fields(json, new[] { "itemId", "weekKey", "resetsAtMs", "bought", "remaining", "boss_token", "balance" });
            ItemId = RecruitmentJson.Choice(json, "itemId", "refine_core", "book", "polish"); WeekKey = EquipmentJson.Week(json); ResetsAtMs = RecruitmentJson.Long(json, "resetsAtMs");
            Bought = P.Count(json, "bought"); Remaining = P.Count(json, "remaining"); Balance = RecruitmentJson.Long(json, "balance");
            var token = P.Member(json, "boss_token");
            if (!(token is JValue value) || value.Type != JTokenType.Integer || !(value.Value is long count) || !projected && count < 0) throw P.Invalid("projected tokens");
            BossTokens = count;
        }
        public string ItemId { get; } public string WeekKey { get; } public long ResetsAtMs { get; } public int Bought { get; } public int Remaining { get; } public long BossTokens { get; } public long Balance { get; }
    }

    /// <summary>One exact action's public before/after projection; it never becomes a current account snapshot.</summary>
    public sealed class PracticeEquipmentPreview
    {
        readonly JObject json;
        internal PracticeEquipmentPreview(JObject json, long revision, string kind, string? target, JObject intent, bool preview)
        {
            ProgressionJson.Fields(json, preview ? new[] { "revision", "kind", "target", "before", "after", "cost", "canCommit" } : new[] { "requestId", "revision", "kind", "target", "before", "after", "cost", "profile" }, preview ? new[] { "code" } : new string[0]);
            Revision = RecruitmentJson.Long(json, "revision"); Kind = RecruitmentJson.Choice(json, "kind", EquipmentJson.Kinds);
            if (Revision != revision || Kind != kind) throw P.Mismatch("equipment context");
            var at = P.Object(json, "target"); string key = kind == "bossExchange" ? "itemId" : kind.StartsWith("weapon", StringComparison.Ordinal) ? "instanceId" : "charId";
            ProgressionJson.Fields(at, kind == "weaponEquip" ? new[] { "instanceId", "charId" } : kind == "bossExchange" ? new[] { "itemId", "quantity" } : new[] { key });
            TargetId = RecruitmentJson.Id(at, key); DestinationCharId = kind == "weaponEquip" ? ProgressionJson.OptionalId(at, "charId") : null;
            Quantity = kind == "bossExchange" ? ProgressionJson.Number(at, "quantity", 1, 20) : (int?)null;
            if (TargetId != (kind == "bossExchange" ? P.Text(intent, "itemId") : target) || kind == "weaponEquip" && DestinationCharId != P.NullableText(intent, "charId") || kind == "bossExchange" && Quantity != P.Count(intent, "quantity")) throw P.Mismatch("equipment target");
            CanCommit = !preview || RecruitmentJson.Bool(json, "canCommit"); Code = !preview || json.Property("code") == null ? null : P.NullableText(json, "code");
            if (Code != null && (!P.IsCode(Code) || CanCommit)) throw P.Invalid("equipment preview code");
            Cost = EquipmentJson.Cost(P.Object(json, "cost"), kind);
            var before = P.Object(json, "before"); var after = P.Object(json, "after");
            if (kind == "weaponEquip")
            {
                AssignmentBefore = new EquipmentAssignment(before, false); AssignmentAfter = new EquipmentAssignment(after, true);
                if (AssignmentBefore.InstanceId != target || AssignmentAfter.InstanceId != target || AssignmentBefore.CharId != DestinationCharId || AssignmentAfter.CharId != DestinationCharId
                    || AssignmentAfter.EquippedBy != DestinationCharId || AssignmentAfter.CharWeaponInstanceId != (DestinationCharId == null ? null : target)
                    || AssignmentAfter.DisplacedCharId != AssignmentBefore.EquippedBy || AssignmentAfter.DisplacedInstanceId != AssignmentBefore.CharWeaponInstanceId) throw P.Mismatch("equipment assignments");
            }
            else if (kind == "bossExchange")
            {
                ShopBefore = new EquipmentShopBalance(before, false); ShopAfter = new EquipmentShopBalance(after, preview && !CanCommit);
                if (ShopBefore.ItemId != TargetId || ShopAfter.ItemId != TargetId || ShopBefore.WeekKey != ShopAfter.WeekKey || ShopBefore.ResetsAtMs != ShopAfter.ResetsAtMs
                    || (long)ShopAfter.Bought - ShopBefore.Bought != Quantity || (long)ShopBefore.Remaining - ShopAfter.Remaining != Quantity
                    || ShopAfter.Balance < ShopBefore.Balance || ShopAfter.Balance - ShopBefore.Balance != Quantity
                    || !Cost.TryGetValue("boss_token", out long tokens) || ShopBefore.BossTokens < long.MinValue + tokens || ShopAfter.BossTokens != ShopBefore.BossTokens - tokens) throw P.Mismatch("shop exchange");
            }
            else if (kind.StartsWith("weapon", StringComparison.Ordinal))
            {
                WeaponBefore = new ProgressionWeapon(before, true); WeaponAfter = new ProgressionWeapon(after, true);
                if (WeaponBefore.InstanceId != target || WeaponAfter.InstanceId != target || WeaponBefore.WeaponId != WeaponAfter.WeaponId) throw P.Mismatch("weapon target");
                if (kind == "weaponLevelUp" && (WeaponAfter.Level != P.Count(intent, "targetLevel") || WeaponAfter.Level <= WeaponBefore.Level || WeaponAfter.Xp != 0)
                    || kind == "weaponAscend" && WeaponAfter.Asc != WeaponBefore.Asc + 1 || kind == "weaponRefine" && (WeaponAfter.Refine != WeaponBefore.Refine + 1 || WeaponAfter.RefineMode != "core")) throw P.Mismatch("weapon action");
            }
            else
            {
                CharacterBefore = new ProgressionCharacter(before, true); CharacterAfter = new ProgressionCharacter(after, true);
                if (CharacterBefore.CharId != target || CharacterAfter.CharId != target) throw P.Mismatch("character target");
                if (kind == "characterAscend" && CharacterAfter.Asc != CharacterBefore.Asc + 1
                    || kind == "skillUp" && (P.Text(intent, "line") == "card" ? CharacterAfter.SkillLv != CharacterBefore.SkillLv + 1 || CharacterAfter.UltLv != CharacterBefore.UltLv : CharacterAfter.UltLv != CharacterBefore.UltLv + 1 || CharacterAfter.SkillLv != CharacterBefore.SkillLv)
                    || (kind == "gearCraft" || kind == "gearEnhance") && (P.Text(intent, "slot") == "armor" ? !GearStep(CharacterBefore.Armor, CharacterAfter.Armor, kind) || CharacterBefore.Accessory != CharacterAfter.Accessory : !GearStep(CharacterBefore.Accessory, CharacterAfter.Accessory, kind) || CharacterBefore.Armor != CharacterAfter.Armor)) throw P.Mismatch("character action");
            }
            if (CharacterBefore != null || WeaponBefore != null)
            {
                var unchangedBefore = (JObject)before.DeepClone(); var unchangedAfter = (JObject)after.DeepClone();
                string[] changed = kind == "characterAscend" || kind == "weaponAscend" ? new[] { "asc", "cap", "capacity", "stats" }
                    : kind == "skillUp" ? new[] { "skillLv", "ultLv", "effectiveSkillLv", "effectiveUltLv", "stats" }
                    : kind == "gearCraft" || kind == "gearEnhance" ? new[] { P.Text(intent, "slot"), "stats" }
                    : kind == "weaponLevelUp" ? new[] { "level", "xp" } : new[] { "refine" };
                foreach (string name in changed) { unchangedBefore.Remove(name); unchangedAfter.Remove(name); }
                if (!JToken.DeepEquals(unchangedBefore, unchangedAfter)) throw P.Mismatch("unrelated equipment change");
            }
            this.json = (JObject)json.DeepClone();
        }
        static bool GearStep(int? before, int? after, string kind) => kind == "gearCraft" ? before == null && after == 0 : before.HasValue && after == before + 1;
        public long Revision { get; } public string Kind { get; } public string TargetId { get; } public string? DestinationCharId { get; } public int? Quantity { get; }
        public IReadOnlyDictionary<string, long> Cost { get; } public bool CanCommit { get; } public string? Code { get; }
        public ProgressionCharacter? CharacterBefore { get; } public ProgressionCharacter? CharacterAfter { get; }
        public ProgressionWeapon? WeaponBefore { get; } public ProgressionWeapon? WeaponAfter { get; }
        public EquipmentAssignment? AssignmentBefore { get; } public EquipmentAssignment? AssignmentAfter { get; }
        public EquipmentShopBalance? ShopBefore { get; } public EquipmentShopBalance? ShopAfter { get; }
        public JObject ToJObject() => (JObject)json.DeepClone();
        public static PracticeEquipmentPreview Parse(byte[] utf8, long revision, string kind, string? target, JObject intent) => new PracticeEquipmentPreview(P.ParseGrowthObject(utf8, GrowthPublicKind.Preview), revision, kind, target, intent, true);
    }
    public sealed class PracticeEquipmentReceipt
    {
        PracticeEquipmentReceipt(PendingPracticeCommand pending, PracticeEquipmentPreview change, PracticeProgressionSnapshot profile) { RequestId = pending.RequestId; Route = pending.Route; Change = change; Profile = profile; }
        public string RequestId { get; } public string Route { get; } public long Revision => Change.Revision; public string Kind => Change.Kind;
        public PracticeEquipmentPreview Change { get; } public PracticeProgressionSnapshot Profile { get; }
        public static PracticeEquipmentReceipt Parse(PendingPracticeCommand pending, byte[] utf8)
        {
            string? kind = EquipmentJson.Kind(pending.Route, out string? target);
            if (kind == null || !(pending.ExpectedRevision is long revision) || revision == long.MaxValue) throw P.Mismatch("equipment request");
            var json = P.ParseGrowthObject(utf8, GrowthPublicKind.Action); if (P.Text(json, "requestId") != pending.RequestId) throw P.Mismatch("equipment requestId");
            var intent = JObject.Parse(pending.Body); intent.Remove("requestId"); intent.Remove("expectedRevision");
            var change = new PracticeEquipmentPreview(json, revision + 1, kind, target, intent, false);
            var profile = PracticeProgressionSnapshot.Parse(P.Object(json, "profile"), pending.Account);
            if (profile.Revision != change.Revision || !SupportJson.EquipmentPolicy(profile.PolicyVersion)) throw P.Mismatch("equipment receipt profile");
            var after = json["after"]!;
            if (change.CharacterAfter != null && !JToken.DeepEquals(P.Array(P.Object(json, "profile"), "characters").SingleOrDefault(row => row["charId"]?.Value<string>() == target), after)
                || change.WeaponAfter != null && !JToken.DeepEquals(P.Array(P.Object(json, "profile"), "weapons").SingleOrDefault(row => row["instanceId"]?.Value<string>() == target), after)) throw P.Mismatch("equipment receipt after");
            if (change.AssignmentAfter != null)
            {
                var applied = profile.Weapons.SingleOrDefault(w => w.InstanceId == target);
                if (applied == null || applied.EquippedBy != change.DestinationCharId
                    || change.AssignmentAfter.DisplacedInstanceId != null && !profile.Weapons.Any(w => w.InstanceId == change.AssignmentAfter.DisplacedInstanceId && w.EquippedBy == null)
                    || change.AssignmentAfter.DisplacedCharId != null && !profile.Characters.Any(c => c.CharId == change.AssignmentAfter.DisplacedCharId && c.WeaponInstanceId == null)) throw P.Mismatch("receipt assignment");
            }
            if (change.ShopAfter != null && (profile.Resources!["boss_token"] != change.ShopAfter.BossTokens || profile.Resources[change.TargetId] != change.ShopAfter.Balance
                || !profile.BossShop!.Items.Any(item => item.ItemId == change.TargetId && item.Bought == change.ShopAfter.Bought && item.Remaining == change.ShopAfter.Remaining)
                || profile.BossShop.WeekKey != change.ShopAfter.WeekKey || profile.BossShop.ResetsAtMs != change.ShopAfter.ResetsAtMs)) throw P.Mismatch("receipt shop balance");
            return new PracticeEquipmentReceipt(pending, change, profile);
        }
    }
}
