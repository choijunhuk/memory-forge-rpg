// R0: actual expedition creation/map/query execution. R1a enter/battle/reward/proceed/archive is in Run.Progression.cs and
// Run.Rewards.cs; node services (rest/forge/shop/event choices), assign and memory commit are later R1 units.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace MemoryForge.Core
{
    public sealed partial class Run
    {
        readonly RunData data;
        readonly RunState state;
        // Live source `this.battle`; its state is aliased only by the private RunState.Battle, never exposed mutably.
        Battle? battle;
        Run(RunData data, RunState state) { this.data = data; this.state = state; }
        public RunState State => state.Copy();

        public static Run Create(RunData data, RunSetup setup)
        {
            try { return CreateCore(data, setup); }
            catch (OverflowException) { throw new RuleException("RUN_NUMBER_OUT_OF_RANGE"); }
        }
        static Run CreateCore(RunData data, RunSetup setup)
        {
            data.Index(); long seed = setup.Seed ?? 1;
            if (seed < -CanonicalState.MaxSafeInteger || seed > CanonicalState.MaxSafeInteger) throw new RuleException("RUN_NUMBER_OUT_OF_RANGE");
            string length = string.IsNullOrEmpty(setup.Length) ? "short" : setup.Length!;
            var s = new RunState { Seed = seed, Rng = Rng.SeedState(unchecked((uint)seed) ^ 0x5eedu), Length = length,
                Difficulty = setup.Difficulty?.Copy() ?? new RunDifficulty { Hp = 100, Damage = 100 }, StageLevel = setup.StageLevel == 0 ? null : setup.StageLevel,
                Zones = setup.Zones == null ? length == "full" ? new List<int> { 1, 2 } : new List<int> { 1 } : new List<int>(setup.Zones),
                Bosses = MemoryJson.Truthy(setup.Bosses) ? MemoryJson.Copy(setup.Bosses) : null, Party = setup.Party.Select(p => p.Copy()).ToList(),
                Relics = new List<string>(setup.Relics), StartMemory = MemoryJson.Copy(MemoryJson.Truthy(setup.Memory) ? setup.Memory : MemoryJson.Object()) };
            if (s.StageLevel.HasValue)
            {
                var scale = Stats.StageScale(data.Stats, s.StageLevel.Value);
                s.Difficulty = new RunDifficulty { Hp = Number(Round((BigInteger)Fallback(s.Difficulty.Hp, 100) * scale.Hp, 100)), Damage = Number(Round((BigInteger)Fallback(s.Difficulty.Damage, 100) * scale.Dmg, 100)) };
            }
            if (setup.MemoryPolicy == "v2")
            {
                if (s.Party.Count == 0 || s.Party.Select(p => p.CharId).Distinct(StringComparer.Ordinal).Count() != s.Party.Count || s.Party.Any(p => !data.Memory.Battle.CharacterIndex.ContainsKey(p.CharId))) throw new RuleException("INVALID_SNAPSHOT");
                if (!(setup.RunId is string runId) || runId.Length == 0) throw new RuleException("RUN_ID_REQUIRED");
                object version = setup.MemoryVersion ?? 0;
                if (!RunValues.TryInteger(version, out long memoryVersion) || memoryVersion < 0) throw new RuleException("INVALID_SNAPSHOT");
                s.Schema = "mf-run-memory-v2"; s.MemoryPolicy = "v2"; s.RunId = runId; s.MemoryVersion = memoryVersion; s.MemoryBaseline = new MemorySnapshot();
                var before = MemoryJson.Object();
                foreach (var party in s.Party)
                {
                    string cid = party.CharId; var baseline = MemoryV2.Baseline(data.Memory, cid); s.MemoryBaseline.Add(cid, baseline.Select(c => new MemoryCardSpec(c.ToJsonGraph())).ToList());
                    object? saved = MemoryJson.Get(setup.Memory, cid);
                    before.Add(cid, MemoryJson.Copy(MemoryJson.Truthy(saved) ? saved : baseline.Select(c => (object?)c.ToJsonGraph()).ToList()));
                }
                s.MemoryBefore = MemoryV2.Inspect(data.Memory, before).Snapshot;
                foreach (var party in s.Party)
                {
                    string cid = party.CharId;
                    long cost = MemoryV2.Cost(data.Memory, cid, s.MemoryBefore.Cards(cid).Select(c => (object?)c.ToJsonGraph()).ToList(), s.MemoryBaseline.Cards(cid).Select(c => (object?)c.ToJsonGraph()).ToList());
                    if (cost > MemoryV2.CapacityOf(data.Memory, new MemoryCapacityEntry { CharId = cid, Res = party.Res ?? 0, Asc = party.Asc ?? 0 })) throw new RuleException("OVER_CAPACITY");
                }
            }
            foreach (var party in s.Party)
            {
                data.Memory.Battle.WeaponIndex.TryGetValue(party.WeaponId ?? "", out var weapon);
                var derived = Stats.Derive(data.Stats, new GrowthEntry { CharId = party.CharId, Level = party.Level, WeaponRarity = weapon?.Rarity,
                    WeaponLevel = Fallback(party.WeaponLevel, 1), Armor = party.Armor, Accessory = party.Accessory, SkillLv = Fallback(party.SkillLv, 1), UltLv = Fallback(party.UltLv, 1) });
                party.Power4 = derived.Power4; party.Hp = derived.Hp; party.GearBps = derived.GearBps; party.SkillLv = derived.SkillLv; party.UltLv = derived.UltLv;
                s.MaxHp = Number((BigInteger)s.MaxHp + derived.Hp);
            }
            s.Hp = s.MaxHp; s.HpReference = s.MaxHp;
            foreach (var party in s.Party)
            {
                string cid = party.CharId; List<RunCardSpec> specs;
                if (s.MemoryPolicy == "v2")
                {
                    var source = setup.UseMemory == false ? s.MemoryBaseline! : s.MemoryBefore!;
                    specs = source.Cards(cid).Select(c => RunCardSpec.FromJsonGraph(c.ToJsonGraph())).ToList();
                    if (setup.UseMemory == false) foreach (var spec in specs)
                    {
                        if (!s.MemoryBefore!.Cards(cid).Any(old => old.Key == spec.Key && old.Id == spec.Id && !MemoryJson.Truthy(MemoryJson.Get(old.Fields, "from")))) spec.Key = NextCardKey(s);
                    }
                }
                else
                {
                    object? saved = MemoryJson.Get(setup.Memory, cid);
                    specs = MemoryJson.Truthy(saved) ? MemoryJson.Array(saved).Cast<object?>().Select(RunCardSpec.FromJsonGraph).ToList() : BaseKit(data, cid);
                }
                foreach (var spec in specs)
                {
                    var card = spec.Copy(); card.Key = s.MemoryPolicy == "v2" ? spec.Key : NextCardKey(s);
                    if (card.Id == null || !data.Memory.Battle.CardIndex.TryGetValue(card.Id, out var definition)) throw new MemoryBoundaryException("TypeError", "Unknown expedition card");
                    card.Owner = definition.Owner;
                    object? assigned = MemoryJson.Get(spec.Fields, "assigned"); card.Fields["assigned"] = MemoryJson.Truthy(assigned) ? MemoryJson.Copy(assigned) : definition.Owner == "common" ? (object)cid : null;
                    s.Deck.Add(card);
                }
                if (!data.Memory.Battle.WeaponIndex.TryGetValue(party.WeaponId ?? "", out var equipped)) data.Memory.Battle.WeaponIndex.TryGetValue(DefaultWeapon(data, cid), out equipped);
                if (equipped == null) throw new MemoryBoundaryException("TypeError", "Unknown expedition weapon");
                s.Deck.Add(new RunCardSpec { Id = equipped.Card, Key = NextCardKey(s), Owner = "weapon", Wielder = cid, Weapon = true });
            }
            foreach (var party in s.Party) s.Offered[party.CharId] = 0;
            foreach (int zone in s.Zones) if (!data.EncounterIndex.ContainsKey(zone.ToString(CultureInfo.InvariantCulture))) throw new RuleException("ZONE_NOT_AUTHORED");
            s.Zone = s.Zones.Count == 0 ? (int?)null : s.Zones[0]; s.Map = GenMap(s, s.Zone);
            if (s.MemoryPolicy == "v2" && setup.UseMemory == false) s.PlayBefore = DeckMemorySnapshot(data, s);
            return new Run(data, s);
        }
        static int Fallback(int? value, int fallback) => !value.HasValue || value.Value == 0 ? fallback : value.Value;
        static int Number(BigInteger value)
        {
            if (value < int.MinValue || value > int.MaxValue) throw new RuleException("RUN_NUMBER_OUT_OF_RANGE"); return (int)value;
        }
        static BigInteger Round(BigInteger numerator, int denominator)
        {
            BigInteger n = 2 * numerator + denominator, d = 2 * (BigInteger)denominator;
            var quotient = BigInteger.DivRem(n, d, out var remainder); return remainder.Sign < 0 ? quotient - 1 : quotient;
        }
        static string NextCardKey(RunState s)
        {
            string key;
            do
            {
                s.SequenceCard = checked(s.SequenceCard + 1);
                key = s.MemoryPolicy == "v2" ? "run:" + s.RunId + ":" + s.SequenceCard.ToString(CultureInfo.InvariantCulture) : "k" + s.SequenceCard.ToString(CultureInfo.InvariantCulture);
            }
            while (s.MemoryPolicy == "v2" && (s.Deck.Any(c => c.Key == key) || s.MemoryBefore!.CharacterIds.Any(cid => s.MemoryBefore.Cards(cid).Any(c => c.Key == key))));
            return key;
        }
        public static List<RunCardSpec> BaseKit(RunData data, string charId) => MemoryV2.Baseline(data.Memory, charId).Select(c =>
        { var spec = RunCardSpec.FromJsonGraph(c.ToJsonGraph()); spec.Fields.Remove("key"); return spec; }).ToList();
        public static string DefaultWeapon(RunData data, string charId)
        {
            var character = data.Characters.FirstOrDefault(c => c.Id == charId);
            if (character == null) throw new MemoryBoundaryException("TypeError", "Unknown expedition character");
            switch (character.WeaponType)
            {
                case "ranged": return "w_basic_ranged"; case "melee": return "w_basic_melee"; case "device": return "w_basic_device"; case "ritual": return "w_basic_ritual";
                default: throw new MemoryBoundaryException("TypeError", "Unknown character weapon type");
            }
        }
        static MemorySnapshot DeckMemorySnapshot(RunData data, RunState s)
        {
            var graph = MemoryJson.Object(); foreach (var party in s.Party) graph[party.CharId] = new List<object?>();
            foreach (var card in s.Deck)
            {
                if (card.Id == null || !data.Memory.Battle.CardIndex.TryGetValue(card.Id, out var definition) || card.Weapon || MemoryJson.Truthy(MemoryJson.Get(card.Fields, "token")) || !(s.Party.Any(p => p.CharId == definition.Owner) || definition.Owner == "common")) continue;
                string? cid = definition.Owner == "common" ? card.Assigned : definition.Owner;
                if (cid == null || !graph.TryGetValue(cid, out var rows)) throw new RuleException("INVALID_ASSIGNMENT");
                var spec = MemoryJson.Object("key", card.Key, "id", card.Id);
                if (MemoryJson.Truthy(MemoryJson.Get(card.Fields, "plus"))) spec.Add("plus", true);
                if (MemoryJson.Truthy(MemoryJson.Get(card.Fields, "from"))) spec.Add("from", MemoryJson.Copy(MemoryJson.Get(card.Fields, "from")));
                if (MemoryJson.Truthy(MemoryJson.Get(card.Fields, "mods"))) spec.Add("mods", MemoryJson.Copy(MemoryJson.Get(card.Fields, "mods")));
                if (definition.Owner == "common") spec.Add("assigned", cid);
                ((List<object?>)rows!).Add(spec);
            }
            return MemoryV2.Inspect(data.Memory, graph).Snapshot;
        }
        static RunMap GenMap(RunState s, int? zone)
        {
            var rng = new Rng(s.Rng); int Below(int n) { int value = rng.Below(n); s.Rng = rng.State; return value; }
            string zoneText = zone?.ToString(CultureInfo.InvariantCulture) ?? "undefined";
            var map = new RunMap { Zone = zone };
            for (int f = 0; f < 7; f++)
            {
                int width = f == 0 ? 3 : 2 + Below(2); var row = new List<RunNode>();
                for (int i = 0; i < width; i++) row.Add(new RunNode { Id = "z" + zoneText + "f" + f + "n" + i, Floor = f, Index = i, Next = new List<string>() });
                map.Floors.Add(row);
            }
            string Assign(int floor, int index)
            {
                if (floor == 0 || floor == 2) return "battle";
                if (floor == 1) return index == 0 && Below(3) == 0 ? "event" : "battle";
                if (floor == 6) return new[] { "rest", "forge", "shop" }[index % 3];
                var table = floor == 3 ? new[] { ("battle", 30), ("elite", 20), ("event", 20), ("rest", 15), ("forge", 15) }
                    : floor == 4 ? new[] { ("battle", 60), ("elite", 40) } : new[] { ("battle", 30), ("elite", 15), ("event", 20), ("shop", 20), ("forge", 15) };
                int roll = Below(100); foreach (var item in table) { if (roll < item.Item2) return item.Item1; roll -= item.Item2; } return "battle";
            }
            foreach (var row in map.Floors) foreach (var node in row) node.Type = Assign(node.Floor, node.Index);
            int elites = 0; foreach (var row in map.Floors) foreach (var node in row) if (node.Type == "elite" && ++elites > 2) node.Type = "battle";
            if (!map.Floors[3].Any(n => n.Type == "rest" || n.Type == "forge")) map.Floors[3][0].Type = "rest";
            for (int f = 0; f < 6; f++)
            {
                var a = map.Floors[f]; var b = map.Floors[f + 1];
                foreach (var node in a)
                {
                    int j = Math.Min(b.Count - 1, Number(Round(node.Index * (b.Count - 1), Math.Max(1, a.Count - 1)))); node.Next!.Add(b[j].Id);
                    if (Below(2) == 0 && j + 1 < b.Count) node.Next.Add(b[j + 1].Id); else if (j > 0 && Below(3) == 0) node.Next.Add(b[j - 1].Id);
                }
                foreach (var node in b) if (!a.Any(parent => parent.Next!.Contains(node.Id))) a[Math.Min(a.Count - 1, node.Index)].Next!.Add(node.Id);
                foreach (var node in a) node.Next = node.Next!.Distinct(StringComparer.Ordinal).ToList();
                foreach (var node in a.Where(n => n.Type == "elite")) foreach (string id in node.Next!) { var next = b.First(n => n.Id == id); if (next.Type == "elite") next.Type = "battle"; }
            }
            map.Boss = new RunNode { Id = "z" + zoneText + "boss", Type = "boss", Floor = 7, Index = 0 };
            foreach (var node in map.Floors[6]) node.Next = new List<string> { map.Boss.Id }; return map;
        }
        public RunNode? NodeById(string id)
        {
            var map = state.Map!; if (map.Boss.Id == id) return map.Boss.Copy(); return map.Floors.SelectMany(row => row).FirstOrDefault(n => n.Id == id)?.Copy();
        }
        public List<string> Available()
        {
            if (state.Phase != "MAP") return new List<string>();
            if (string.IsNullOrEmpty(state.Position)) return state.Map!.Floors[0].Select(n => n.Id).ToList();
            return NodeById(state.Position)?.Next ?? new List<string>();
        }
    }
}
