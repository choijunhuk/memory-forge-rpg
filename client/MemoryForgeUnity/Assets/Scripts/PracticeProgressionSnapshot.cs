#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using P = MemoryForge.Client.PracticePublicJson;

namespace MemoryForge.Client
{
    static class ProgressionJson
    {
        internal static void Fields(JObject json, string[] required, params string[] optional)
        {
            if (required.Any(name => json.Property(name, StringComparison.Ordinal) == null)
                || json.Properties().Any(property => !required.Contains(property.Name, StringComparer.Ordinal) && !optional.Contains(property.Name, StringComparer.Ordinal)))
                throw P.Invalid("progression members");
        }
        internal static int Number(JObject json, string name, int minimum, int maximum = int.MaxValue)
        {
            int value = P.Count(json, name);
            return value >= minimum && value <= maximum ? value : throw P.Invalid(name);
        }
        internal static string? OptionalId(JObject json, string name) => json.Property(name) == null || json[name]!.Type == JTokenType.Null ? null : RecruitmentJson.Id(json, name);
        internal static int? OptionalGear(JObject json, string name) => json.Property(name) == null || json[name]!.Type == JTokenType.Null ? (int?)null : Number(json, name, 0, 15);
        internal static string Reference(JObject json, string name)
        {
            string value = P.Text(json, name);
            return value.Length > 0 && value.Length <= 128 && value.All(character => character >= ' ' && character <= '~') ? value : throw P.Invalid(name);
        }
        internal static IReadOnlyList<string> IdList(JObject json, string name)
        {
            var result = new List<string>();
            foreach (var token in P.Array(json, name))
            {
                string? id = token.Type == JTokenType.String ? token.Value<string>() : null;
                if (id == null || !PracticeCommandOutbox.IsPublicId(id)) throw P.Invalid(name);
                result.Add(id);
            }
            return result.AsReadOnly();
        }
    }

    public sealed class ProgressionAccount
    {
        internal ProgressionAccount(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "level", "xp", "next" });
            Level = ProgressionJson.Number(json, "level", 1); Xp = RecruitmentJson.Long(json, "xp"); Next = RecruitmentJson.Long(json, "next");
        }
        public int Level { get; }
        public long Xp { get; }
        public long Next { get; }
    }

    public sealed class ProgressionTutorial
    {
        internal ProgressionTutorial(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "completed" }, "activeRunId");
            Completed = RecruitmentJson.Bool(json, "completed");
            ActiveRunId = json.Property("activeRunId") == null || json["activeRunId"]!.Type == JTokenType.Null ? null : P.RunId(json, "activeRunId");
        }
        public bool Completed { get; }
        public string? ActiveRunId { get; }
    }

    public sealed class ProgressionChapter
    {
        internal ProgressionChapter(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "id", "stageLevel", "zones", "bosses", "unlockLevel", "firstClear", "available" });
            Id = P.Count(json, "id"); StageLevel = ProgressionJson.Number(json, "stageLevel", 1); UnlockLevel = ProgressionJson.Number(json, "unlockLevel", 1);
            var zones = new List<int>();
            foreach (var token in P.Array(json, "zones"))
            {
                int value = P.AsCount(token) ?? throw P.Invalid("zones");
                if (value == 0) throw P.Invalid("zones"); zones.Add(value);
            }
            Zones = zones.AsReadOnly(); Bosses = ProgressionJson.IdList(json, "bosses");
            FirstClear = RecruitmentJson.Bool(json, "firstClear"); Available = RecruitmentJson.Bool(json, "available");
        }
        public int Id { get; }
        public int StageLevel { get; }
        public IReadOnlyList<int> Zones { get; }
        public IReadOnlyList<string> Bosses { get; }
        public int UnlockLevel { get; }
        public bool FirstClear { get; }
        public bool Available { get; }
    }

    public sealed class ProgressionCharacter
    {
        internal ProgressionCharacter(JObject json, bool equipment = false)
        {
            var baseFields = new[] { "charId", "copies", "res", "level", "xp", "asc", "skillLv", "ultLv" };
            ProgressionJson.Fields(json, equipment ? baseFields.Concat(EquipmentJson.CharacterFields).Concat(new[] { "weaponInstanceId", "armor", "accessory" }).ToArray() : baseFields, equipment ? new string[0] : new[] { "weaponInstanceId", "armor", "accessory" });
            CharId = RecruitmentJson.Id(json, "charId"); Copies = ProgressionJson.Number(json, "copies", 1); Res = ProgressionJson.Number(json, "res", 0, 6);
            Level = ProgressionJson.Number(json, "level", 1, 60); Xp = RecruitmentJson.Long(json, "xp"); Asc = ProgressionJson.Number(json, "asc", 0, 4);
            SkillLv = ProgressionJson.Number(json, "skillLv", 1, 10); UltLv = ProgressionJson.Number(json, "ultLv", 1, 10);
            WeaponInstanceId = ProgressionJson.OptionalId(json, "weaponInstanceId"); Armor = ProgressionJson.OptionalGear(json, "armor"); Accessory = ProgressionJson.OptionalGear(json, "accessory");
            if (equipment)
            {
                Cap = ProgressionJson.Number(json, "cap", 20, 60); EffectiveSkillLv = ProgressionJson.Number(json, "effectiveSkillLv", SkillLv, 12); EffectiveUltLv = ProgressionJson.Number(json, "effectiveUltLv", UltLv, 12);
                Capacity = ProgressionJson.Number(json, "capacity", 6, 9); WeaponType = RecruitmentJson.Choice(json, "weaponType", "device", "melee", "ranged", "ritual");
                Deployable = RecruitmentJson.Bool(json, "deployable"); EligibleWeapons = ProgressionJson.IdList(json, "eligibleWeapons"); Stats = new GrowthStats(P.Object(json, "stats"));
                if (Cap != 20 + Asc * 10 || Level > Cap || Deployable != (WeaponInstanceId != null) || EligibleWeapons.Distinct(StringComparer.Ordinal).Count() != EligibleWeapons.Count
                    || WeaponInstanceId != null && !EligibleWeapons.Contains(WeaponInstanceId) || Stats.SkillLv != SkillLv || Stats.UltLv != UltLv) throw P.Invalid("equipment character");
            }
        }
        public int? Cap { get; } public int? EffectiveSkillLv { get; } public int? EffectiveUltLv { get; } public int? Capacity { get; }
        public string? WeaponType { get; } public bool? Deployable { get; } public IReadOnlyList<string>? EligibleWeapons { get; } public GrowthStats? Stats { get; }
        public string CharId { get; }
        public int Copies { get; }
        public int Res { get; }
        public int Level { get; }
        public long Xp { get; }
        public int Asc { get; }
        public int SkillLv { get; }
        public int UltLv { get; }
        public string? WeaponInstanceId { get; }
        public int? Armor { get; }
        public int? Accessory { get; }
    }

    public sealed class ProgressionWeapon
    {
        internal ProgressionWeapon(JObject json, bool equipment = false)
        {
            var baseFields = new[] { "instanceId", "weaponId", "level", "xp", "asc", "refine" };
            ProgressionJson.Fields(json, equipment ? baseFields.Concat(EquipmentJson.WeaponFields).Concat(new[] { "equippedBy" }).ToArray() : baseFields, equipment ? new string[0] : new[] { "equippedBy" });
            InstanceId = RecruitmentJson.Id(json, "instanceId"); WeaponId = RecruitmentJson.Id(json, "weaponId");
            Level = ProgressionJson.Number(json, "level", 1, 60); Xp = RecruitmentJson.Long(json, "xp"); Asc = ProgressionJson.Number(json, "asc", 0, 4);
            Refine = ProgressionJson.Number(json, "refine", 1, 5); EquippedBy = ProgressionJson.OptionalId(json, "equippedBy");
            if (equipment)
            {
                Cap = ProgressionJson.Number(json, "cap", 20, 60); Type = RecruitmentJson.Choice(json, "type", "device", "melee", "ranged", "ritual");
                Rarity = RecruitmentJson.Choice(json, "rarity", "R", "SR", "SSR"); Copies = ProgressionJson.Number(json, "copies", 1); RefineMode = RecruitmentJson.Choice(json, "refineMode", "core", "copy");
                if (Cap != 20 + Asc * 10 || Level > Cap || (Rarity == "R") != (RefineMode == "core") || RefineMode == "copy" && Refine != Math.Min(5, Copies.Value)) throw P.Invalid("equipment weapon");
            }
        }
        public int? Cap { get; } public int? Copies { get; } public string? Type { get; } public string? Rarity { get; } public string? RefineMode { get; }
        public string InstanceId { get; }
        public string WeaponId { get; }
        public int Level { get; }
        public long Xp { get; }
        public int Asc { get; }
        public int Refine { get; }
        public string? EquippedBy { get; }
    }

    public sealed class ProgressionActiveSession
    {
        internal ProgressionActiveSession(JObject json)
        {
            ProgressionJson.Fields(json, new[] { "runId", "mode", "chapter", "phase", "version" });
            RunId = P.RunId(json, "runId"); Mode = RecruitmentJson.Choice(json, "mode", "legacy", "tutorial", "story");
            Chapter = P.NullableCount(json, "chapter"); Phase = RecruitmentJson.Id(json, "phase"); Version = RecruitmentJson.Long(json, "version");
        }
        public string RunId { get; }
        public string Mode { get; }
        public int? Chapter { get; }
        public string Phase { get; }
        public long Version { get; }
    }

    /// <summary>The S1 public profile. Only its root revision is public; existing Run/recruitment private-field rules stay unchanged.</summary>
    public sealed class PracticeProgressionSnapshot
    {
        readonly JObject json;
        PracticeProgressionSnapshot(JObject json, string owner)
        {
            ProgressionJson.Fields(json, new[] { "accountId", "revision", "policyVersion", "initialized", "account", "tutorial", "chapters", "characters", "weapons", "lastParty" },
                "activeSession", "resources", "resourceUnits", "focus", "dungeons", "activeEncounter", "bossShop", "weeklyBoss", "supportSelection");
            AccountId = P.Text(json, "accountId"); if (AccountId != owner) throw new PracticeProtocolException(PracticeProtocolException.WrongAccount, "The profile belongs to another account.");
            Revision = RecruitmentJson.Long(json, "revision"); PolicyVersion = ProgressionJson.Reference(json, "policyVersion"); Initialized = RecruitmentJson.Bool(json, "initialized");
            Account = new ProgressionAccount(P.Object(json, "account")); Tutorial = new ProgressionTutorial(P.Object(json, "tutorial"));
            bool equipment = SupportJson.EquipmentPolicy(PolicyVersion);
            Chapters = RecruitmentJson.List(json, "chapters", item => new ProgressionChapter(item)); Characters = RecruitmentJson.List(json, "characters", item => new ProgressionCharacter(item, equipment));
            Weapons = RecruitmentJson.List(json, "weapons", item => new ProgressionWeapon(item, equipment)); LastParty = ProgressionJson.IdList(json, "lastParty");
            if (Chapters.Select(chapter => chapter.Id).Distinct().Count() != Chapters.Count || Characters.Select(character => character.CharId).Distinct(StringComparer.Ordinal).Count() != Characters.Count
                || Weapons.Select(weapon => weapon.InstanceId).Distinct(StringComparer.Ordinal).Count() != Weapons.Count
                || LastParty.Count != 0 && LastParty.Count != 3 || LastParty.Distinct(StringComparer.Ordinal).Count() != LastParty.Count) throw P.Invalid("duplicate or invalid progression list");
            if (LastParty.Any(id => !Characters.Any(character => character.CharId == id))) throw P.Invalid("lastParty owner");
            ActiveSession = json.Property("activeSession") == null || json["activeSession"]!.Type == JTokenType.Null ? null : new ProgressionActiveSession(P.Object(json, "activeSession"));
            string[] growthFields = { "resources", "resourceUnits", "focus", "dungeons" };
            if (growthFields.Any(name => json.Property(name) != null))
            {
                if (growthFields.Any(name => json.Property(name) == null)) throw P.Invalid("incomplete growth profile");
                Resources = equipment ? EquipmentJson.Resources(P.Object(json, "resources")) : GrowthJson.Resources(P.Object(json, "resources"));
                var units = P.Object(json, "resourceUnits"); ProgressionJson.Fields(units, new[] { "ink", "oil" });
                if (P.Text(units, "ink") != "XP" || P.Text(units, "oil") != "XP") throw P.Invalid("resource units");
                ResourceUnits = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["ink"] = "XP", ["oil"] = "XP" });
                Focus = new GrowthFocus(P.Object(json, "focus")); Dungeons = RecruitmentJson.List(json, "dungeons", item => new GrowthDungeon(item));
                if (Dungeons.Select(dungeon => dungeon.Id).Distinct(StringComparer.Ordinal).Count() != Dungeons.Count) throw P.Invalid("duplicate dungeon");
            }
            ActiveEncounter = json.Property("activeEncounter") == null || json["activeEncounter"]!.Type == JTokenType.Null ? null : new GrowthActiveEncounter(P.Object(json, "activeEncounter"));
            if (ActiveEncounter != null && (Resources == null || ActiveSession != null && ActiveSession.Phase != "DONE" || !equipment && ActiveEncounter.Kind == "weeklyBoss")) throw P.Invalid("active growth encounter");
            if (equipment)
            {
                if (Resources == null) throw P.Invalid("v3 resources");
                BossShop = new EquipmentBossShop(P.Object(json, "bossShop")); WeeklyBoss = new EquipmentWeeklyProfile(P.Object(json, "weeklyBoss"));
                if (BossShop.WeekKey != WeeklyBoss.WeekKey || BossShop.ResetsAtMs != WeeklyBoss.ResetsAtMs) throw P.Invalid("weekly snapshot");
                foreach (var c in Characters)
                {
                    foreach (string id in c.EligibleWeapons!) if (!Weapons.Any(w => w.InstanceId == id && w.Type == c.WeaponType)) throw P.Invalid("eligible weapon owner");
                    if (c.WeaponInstanceId != null && !Weapons.Any(w => w.InstanceId == c.WeaponInstanceId && w.EquippedBy == c.CharId && w.Type == c.WeaponType)) throw P.Invalid("equipped weapon owner");
                }
                foreach (var w in Weapons) if (w.EquippedBy != null && !Characters.Any(c => c.CharId == w.EquippedBy && c.WeaponInstanceId == w.InstanceId)) throw P.Invalid("weapon assignment");
            }
            else if (json.Property("bossShop") != null || json.Property("weeklyBoss") != null) throw P.Invalid("v3 profile context");
            HasSupportSelection = PolicyVersion == SupportJson.Policy;
            if (HasSupportSelection)
            {
                P.Member(json, "supportSelection"); SupportSelection = ProgressionJson.OptionalId(json, "supportSelection");
                if (SupportSelection != null && !Characters.Any(c => c.CharId == SupportSelection)) throw P.Invalid("support selection owner");
            }
            else if (json.Property("supportSelection") != null) throw P.Invalid("support profile version");
            this.json = (JObject)json.DeepClone();
        }
        public bool HasSupportSelection { get; } public string? SupportSelection { get; }
        public EquipmentBossShop? BossShop { get; } public EquipmentWeeklyProfile? WeeklyBoss { get; }
        public string AccountId { get; }
        public long Revision { get; }
        public string PolicyVersion { get; }
        public bool Initialized { get; }
        public ProgressionAccount Account { get; }
        public ProgressionTutorial Tutorial { get; }
        public IReadOnlyList<ProgressionChapter> Chapters { get; }
        public IReadOnlyList<ProgressionCharacter> Characters { get; }
        public IReadOnlyList<ProgressionWeapon> Weapons { get; }
        public IReadOnlyList<string> LastParty { get; }
        public ProgressionActiveSession? ActiveSession { get; }
        public IReadOnlyDictionary<string, long>? Resources { get; }
        public IReadOnlyDictionary<string, string>? ResourceUnits { get; }
        public GrowthFocus? Focus { get; }
        public IReadOnlyList<GrowthDungeon>? Dungeons { get; }
        public GrowthActiveEncounter? ActiveEncounter { get; }
        public JObject ToJObject() => (JObject)json.DeepClone();
        public static PracticeProgressionSnapshot Parse(byte[] utf8, string owner) => new PracticeProgressionSnapshot(P.ParseProgressionObject(utf8), owner);
        internal static PracticeProgressionSnapshot Parse(JObject json, string owner) => new PracticeProgressionSnapshot(json, owner);
    }

    public sealed class PracticeAbandonReceipt
    {
        PracticeAbandonReceipt(string runId, long version, int memoryVersion) { RunId = runId; Version = version; MemoryVersion = memoryVersion; }
        public string RunId { get; }
        public long Version { get; }
        public int MemoryVersion { get; }
        public static PracticeAbandonReceipt Parse(PendingPracticeCommand pending, byte[] utf8)
        {
            if (!PracticeCommandOutbox.IsAbandonRoute(pending.Route) || !pending.ExpectedVersion.HasValue) throw P.Mismatch("abandon request");
            var json = P.ParseObject(utf8); ProgressionJson.Fields(json, new[] { "runId", "version", "memoryVersion" });
            string runId = P.RunId(json, "runId"); long version = RecruitmentJson.Long(json, "version");
            if (pending.Route != PracticeCommandOutbox.AbandonRoute(runId) || version != (long)pending.ExpectedVersion.Value + 1) throw P.Mismatch("abandon receipt");
            return new PracticeAbandonReceipt(runId, version, P.Count(json, "memoryVersion"));
        }
    }
}
