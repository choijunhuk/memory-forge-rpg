using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoryForge.Core
{
    public sealed partial class Run
    {
        // This input must be the explicit owned build; unlike legacy parties it never falls back to reference stats or a basic weapon.
        internal static RunPartyEntry PrepareSupport(RunData data, RunPartyEntry input, IReadOnlyList<RunPartyEntry> party)
        {
            var p = input.Copy();
            if (party.Any(a => a.CharId == p.CharId)) throw new RuleException("SUPPORT_IN_PARTY");
            if (!data.Memory.Battle.CharacterIndex.ContainsKey(p.CharId)) throw new RuleException("UNKNOWN_CHARACTER");
            var character = data.Characters.FirstOrDefault(c => c.Id == p.CharId);
            if (character == null || p.WeaponId == null || !data.Memory.Battle.WeaponIndex.TryGetValue(p.WeaponId, out var weapon)
                || weapon.Type != character.WeaponType || !p.Level.HasValue || p.Level < 1 || p.Level > data.Stats.LevelCap
                || !p.Asc.HasValue || p.Asc < 0 || !p.Res.HasValue || p.Res < 0 || p.Res > 6 || !p.Refine.HasValue || p.Refine < 1 || p.Refine > 5
                || !p.WeaponLevel.HasValue || p.WeaponLevel < 1 || p.WeaponLevel > data.Stats.LevelCap
                || !p.SkillLv.HasValue || p.SkillLv < 1 || p.SkillLv > data.Stats.SkillMax || !p.UltLv.HasValue || p.UltLv < 1 || p.UltLv > data.Stats.SkillMax
                || p.Armor < 0 || p.Armor > data.Stats.GearMax || p.Accessory < 0 || p.Accessory > data.Stats.GearMax) throw new RuleException("INVALID_SNAPSHOT");
            object? instance = MemoryJson.Get(p.Fields, "weaponInstanceId");
            if (instance is string id && party.Any(a => MemoryJson.Get(a.Fields, "weaponInstanceId") is string other && other == id)) throw new RuleException("INVALID_SNAPSHOT");
            Derive(data, p); return p;
        }
        internal static BattleSupportSetup SupportSetup(RunPartyEntry p) => new BattleSupportSetup { CharId = p.CharId,
            WeaponId = p.WeaponId!, Res = p.Res!.Value, Refine = p.Refine!.Value, Power4 = p.Power4!.Value, Hp = p.Hp!.Value,
            GearBps = p.GearBps!.Value, SkillLv = p.SkillLv!.Value, UltLv = p.UltLv!.Value };
    }
}
