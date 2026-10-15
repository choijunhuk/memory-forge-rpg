// R1a §7.1-7.7/§8.5/§16.2: real enter → Battle begin/commands → afterBattle → proceed/archive with terminal v2 capture.
// Source: engine/rules-core.js Run enter/startBattle/encounterFor/battleCmd/afterBattle/proceed/archive/makeShop/captureMemory.
// Commands mutate the live state in source order like the JS reference (an invalid authored setup can leave pos/floor/RNG
// changed); this is not a transactional Run. Rest/forge/buy/eventChoose, assign and commitMemory are later R1 units.
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
        /// <summary>Source enter: NODE_UNREACHABLE, then pos/floor and the node pending, then the entry log; a battle that
        /// begin already ended is finalized once, after that log.</summary>
        public void Enter(string nodeId) => Guard(() =>
        {
            var s = state;
            if (!Available().Contains(nodeId)) throw new RuleException("NODE_UNREACHABLE");
            var node = NodeById(nodeId) ?? throw new InvalidOperationException("Reachable node is missing from the map");
            s.Position = nodeId; s.Floor = node.Floor;
            switch (node.Type)
            {
                case "battle": case "elite": case "boss": StartBattle(node.Type); break;
                case "rest": s.Phase = "REST"; s.Pending = MemoryJson.Object("type", "rest", "uses", Has("forge_ember") ? 2 : 1); break;
                case "forge": s.Phase = "FORGE"; s.Pending = MemoryJson.Object("type", "forge", "uses", Has("forge_ember") ? 2 : 1, "offers", null); break;
                case "shop": s.Phase = "SHOP"; s.Pending = MakeShop(); break;
                case "event":
                    {
                        s.Phase = "EVENT";
                        var picked = data.Events[Below(data.Events.Count)];
                        s.Pending = MemoryJson.Object("type", "event", "id", picked.Id);
                        break;
                    }
            }
            Log(NodeName(node.Type) + " 진입");
            if (battle != null && battle.Finished) AfterBattle();
        });

        /// <summary>Source battleCmd: NO_BATTLE, then Battle.Command with rid r{v}-{kind} at the live version (atomic in Battle),
        /// then the Run battle pointer, then the existing finalizer when the battle ended. Returns the Battle response.</summary>
        public BattleResponse BattleCommand(string kind, BattleArgument? args = null) => Guarded(() =>
        {
            var live = battle ?? throw new RuleException("NO_BATTLE");
            int version = live.Version;
            var response = live.Command(new BattleRequest("r" + version.ToString(CultureInfo.InvariantCulture) + "-" + kind, version, kind, args));
            state.Battle = live.Live;
            if (live.Finished) AfterBattle();
            return response;
        });

        /// <summary>Existing Battle.PublicView of the live battle (a fresh graph), or null outside battle.</summary>
        public Dictionary<string, object?>? BattlePublicView() => battle?.PublicView();

        void StartBattle(string type)
        {
            var s = state;
            // Source argument order: seed draw, runFlags copy carrying nextApBonus 0, then the encounter choice.
            uint seed = NextRandom();
            var flags = RunValues.Fields(s.RunFlags); flags["nextApBonus"] = 0;
            var enemies = EncounterFor(type);
            var setup = new BattleSetup
            {
                Party = s.Party.Select(p => ActorSetup(p)).ToList(), Deck = s.Deck.Select(c => CardSpec(c)).ToList(), Relics = new List<string>(s.Relics),
                Hp = s.Hp, MaxHp = s.MaxHp, Seed = seed, RunFlags = flags, Enemies = enemies,
                DifficultyHp = ToInt(JsOr(MemoryJson.Get(s.Difficulty.Fields, "hp"), 100)), DifficultyDamage = ToInt(JsOr(MemoryJson.Get(s.Difficulty.Fields, "dmg"), 100)),
                NextApBonus = ToInt(JsOr(MemoryJson.Get(s.RunFlags, "nextApBonus"), 0))
            };
            Battle created;
            try { created = Battle.Create(data.Memory.Battle, setup); }
            catch (OverflowException) { throw new RuleException("BATTLE_NUMBER_OUT_OF_RANGE"); }
            // One-shot: the Run consumes the bonus here and the battle copy already carries 0, so afterBattle cannot refund it.
            s.RunFlags["nextApBonus"] = 0;
            created.BindRunKeys(s.Deck.Select(c => c.Key).ToList());
            created.Command(new BattleRequest("r" + created.Version.ToString(CultureInfo.InvariantCulture) + "-begin", created.Version, "begin"));
            battle = created; s.Battle = created.Live; s.Phase = "BATTLE"; s.BattleType = type;
        }
        // Actors come from the Run party entries: R0 already overwrote supplied power4/hp/gearBps/skillLv/ultLv with derived
        // values. res/refine/gauge/weaponId are read as source Battle.create reads them (`|| 0`, clamps, truthy weapon).
        static BattleActorSetup ActorSetup(RunPartyEntry party)
        {
            string? weapon = OptionalText(MemoryJson.Get(party.Fields, "weaponId"));
            return new BattleActorSetup
            {
                CharId = party.CharId, WeaponId = weapon, Res = Clamp(JsOr(MemoryJson.Get(party.Fields, "res"), 0), 0, 6),
                Refine = weapon == null ? 0 : Clamp(JsOr(MemoryJson.Get(party.Fields, "refine"), 1), 1, 5),
                Gauge = Clamp(JsOr(MemoryJson.Get(party.Fields, "gauge"), 0), int.MinValue, int.MaxValue),
                Power4 = party.Power4 ?? 0, Hp = party.Hp ?? 0, GearBps = party.GearBps ?? 0, SkillLv = party.SkillLv ?? 0, UltLv = party.UltLv ?? 0
            };
        }
        // Source deck copy {id, plus, mods, growth || 0, wielder, assigned, from}; the key list is bound after Battle.Create.
        static BattleCardSpec CardSpec(RunCardSpec card)
        {
            object? mods = MemoryJson.Get(card.Fields, "mods");
            return new BattleCardSpec
            {
                Id = card.Id ?? "", Plus = MemoryJson.Truthy(MemoryJson.Get(card.Fields, "plus")),
                Circuit = OptionalText(MemoryJson.Get(mods, "c")), Edge = OptionalText(MemoryJson.Get(mods, "e")), Growth = BattleGrowth(MemoryJson.Get(card.Fields, "growth")),
                Wielder = OptionalText(MemoryJson.Get(card.Fields, "wielder")), Assigned = OptionalText(MemoryJson.Get(card.Fields, "assigned")), From = OptionalText(MemoryJson.Get(card.Fields, "from"))
            };
        }
        // JS `growth || 0`, then Battle.create's invariant (integer 0..3): a non-integer keeps a value Battle rejects as GROWTH_INVARIANT.
        static int BattleGrowth(object? value)
        {
            if (!MemoryJson.Truthy(value)) return 0;
            return RunValues.TryInteger(value, out long growth) ? Clamp(growth, int.MinValue, int.MaxValue) : -1;
        }

        List<string> EncounterFor(string type)
        {
            var s = state;
            string zone = s.Zone.HasValue ? s.Zone.Value.ToString(CultureInfo.InvariantCulture) : "undefined";
            if (!data.EncounterIndex.TryGetValue(zone, out var encounter)) throw new RuleException("ZONE_NOT_AUTHORED");
            // Source fixed boss: `bosses && bosses[zoneIdx]` truthiness, no RNG.
            if (type == "boss" && MemoryJson.Truthy(s.Bosses))
            {
                object? fixedBoss = JsElement(s.Bosses, s.ZoneIndex);
                if (MemoryJson.Truthy(fixedBoss)) return new List<string> { BossId(fixedBoss) };
            }
            var pool = type == "battle" ? encounter.Normal : type == "elite" ? encounter.Elite : encounter.Boss;
            return new List<string>(pool[Below(pool.Count)]);
        }
        // JS value[index] for the opaque bosses graph: array element, object member "index" or string code unit; else undefined.
        static object? JsElement(object? value, int index)
        {
            if (index < 0) return null;
            if (value is string text) return index < text.Length ? text[index].ToString() : null;
            if (value is IList list) return index < list.Count ? list[index] : null;
            if (value is IDictionary<string, object?> members) return members.TryGetValue(index.ToString(CultureInfo.InvariantCulture), out object? member) ? member : null;
            return null;
        }
        // JS looks enemies up by String(value): an unknown key reaches Battle.Create's UNKNOWN_ENEMY at the source point. A non-string
        // that names a real enemy would put that raw value in battle state, which typed Core state cannot hold.
        string BossId(object? value)
        {
            if (value is string id) return id;
            string key = JsKey(value);
            if (data.Memory.Battle.EnemyIndex.ContainsKey(key)) throw new RuleException("UNSUPPORTED_STATE_VALUE");
            return key;
        }
        static string JsKey(object? value)
        {
            switch (value)
            {
                case null: return "null";
                case string text: return text;
                case bool flag: return flag ? "true" : "false";
                case double number: return Math.Floor(number) == number && Math.Abs(number) < 1e21 ? new BigInteger(number).ToString(CultureInfo.InvariantCulture) : number.ToString("R", CultureInfo.InvariantCulture);
                case IList list: return string.Join(",", list.Cast<object?>().Select(item => item == null ? "" : JsKey(item)));
                case IDictionary<string, object?> _: return "[object Object]";
                default: return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
        }

        // Source makeShop: 3 × (pick party character, pick its non-start pool card), 2 parts, 2 shop relics, then the removal price.
        Dictionary<string, object?> MakeShop()
        {
            var s = state; var chars = s.Party.Select(p => p.CharId).ToList(); var cards = new List<object?>();
            for (int k = 0; k < 3; k++)
            {
                string owner = chars[Below(chars.Count)];
                var pool = data.Memory.Battle.Cards.Where(c => c.Owner == owner && c.Pool != false && c.Start != true).ToList();
                var card = pool[Below(pool.Count)];
                cards.Add(MemoryJson.Object("id", card.Id, "price", CardPrice(card.Rarity)));
            }
            var parts = PartChoices(false, 2).Select(id => (object?)MemoryJson.Object("id", id, "price", PartDefinition(id).Rarity == "rare" ? 90 : 60)).ToList();
            var relics = RelicChoices("shop").Select(id => (object?)MemoryJson.Object("id", id, "price", RelicDefinition(id).Rarity == "rare" ? 180 : 120)).ToList();
            long removals = JsOr(MemoryJson.Get(s.RunFlags, "removals"), 0);
            return MemoryJson.Object("type", "shop", "cards", cards, "parts", parts, "relics", relics, "removeCost", ToInt(checked(50 + 25 * removals)));
        }
        static int CardPrice(string rarity)
        {
            switch (rarity) { case "B": return 45; case "A": return 70; case "S": return 110; default: return 60; }
        }

        void AfterBattle()
        {
            var s = state; var live = battle ?? throw new InvalidOperationException("No live battle to finalize"); var bs = live.Live;
            s.Hp = bs.Hp;
            var flags = RunValues.Fields(s.RunFlags);
            foreach (var pair in bs.RunFlags) flags[pair.Key] = MemoryJson.Copy(pair.Value);
            s.RunFlags = flags;
            // Growth persists through the keys bound in StartBattle, in source Object.values(cards) insertion order; 0 creates the field too.
            foreach (string uid in bs.CardOrder)
            {
                var instance = bs.Cards[uid];
                if (instance.Key is string key && key.Length != 0)
                {
                    var owned = s.Deck.FirstOrDefault(d => KeyEquals(d, key));
                    if (owned != null) owned.Fields["growth"] = instance.Growth;
                }
            }
            if (bs.Phase == "DEFEAT")
            {
                // Defeat: no stats, rewards or Run RNG; legacy has no memoryAfter.
                s.Phase = "DEFEAT"; s.Result = MemoryJson.Object("outcome", "defeat", "floor", s.Floor); Log("패배");
                s.Pending = MemoryJson.Object("type", "save", "defeat", true); CaptureMemory(); battle = null; s.Battle = null; return;
            }
            s.Statistics.Battles = checked(s.Statistics.Battles + 1); s.Statistics.Kills = checked(s.Statistics.Kills + bs.Enemies.Count(e => !e.Alive));
            string? type = s.BattleType;
            if (type == "elite") s.Statistics.Elites = checked(s.Statistics.Elites + 1);
            int coinBase = type == "boss" ? 100 : type == "elite" ? 50 + Below(16) : 22 + Below(11);
            int coins = ToInt(coinBase + (Has("lucky_coin") ? 10 : 0) + JsOr(MemoryJson.Get(bs.RunFlags, "coinsBonus"), 0));
            s.RunFlags["coinsBonus"] = 0;
            s.Coins = checked(s.Coins + coins);
            var reward = MemoryJson.Object("type", "reward", "coins", coins, "cards", CardChoices(type), "relic", null, "part", null, "evolutions", PendingEvolutions());
            if (type == "elite") reward["relic"] = RelicChoices("elite").Cast<object?>().ToList();
            if (type == "boss") reward["relic"] = RelicChoices("boss").Cast<object?>().ToList();
            if (type == "battle" && Below(4) == 0)
            {
                var found = PartChoices(false, 1);
                if (found.Count != 0) reward["part"] = found[0];
                else reward.Remove("part"); // source stores undefined, which JSON omits
            }
            s.Pending = reward; s.Phase = "REWARD";
            battle = null; s.Battle = null;
        }

        /// <summary>Source proceed: WRONG_PHASE outside REWARD/REST/FORGE/SHOP/EVENT; a boss node opens ARCHIVE while zones
        /// remain, else VICTORY clear with a save pending (v2 capture); other nodes return to MAP.</summary>
        public void Proceed() => Guard(() =>
        {
            var s = state;
            if (s.Phase != "REWARD" && s.Phase != "REST" && s.Phase != "FORGE" && s.Phase != "SHOP" && s.Phase != "EVENT") throw new RuleException("WRONG_PHASE");
            var node = s.Position is string position ? NodeById(position) : null;
            s.Pending = null;
            if (node != null && node.Type == "boss")
            {
                if (s.ZoneIndex < s.Zones.Count - 1) { s.Phase = "ARCHIVE"; s.Pending = MemoryJson.Object("type", "archive"); return; }
                s.Phase = "VICTORY"; s.Result = MemoryJson.Object("outcome", "clear", "floor", s.Floor); s.Pending = MemoryJson.Object("type", "save", "defeat", false); CaptureMemory(); return;
            }
            s.Phase = "MAP";
        });

        /// <summary>Source archive: WRONG_PHASE outside ARCHIVE; "return" ends with VICTORY return and a save pending. Every other
        /// value (null included, Q5) continues: heal floor(maxHp*0.15) capped at maxHp, next zone, new map from the Run RNG.</summary>
        public void Archive(string? choice) => Guard(() =>
        {
            var s = state;
            if (s.Phase != "ARCHIVE") throw new RuleException("WRONG_PHASE");
            if (choice == "return")
            {
                s.Phase = "VICTORY"; s.Result = MemoryJson.Object("outcome", "return", "floor", s.Floor); s.Pending = MemoryJson.Object("type", "save", "defeat", false); CaptureMemory(); return;
            }
            // Math.floor(maxHp * 0.15) equals floor(maxHp * 15 / 100) for every Int32 maxHp (the binary error never crosses an integer).
            s.Hp = Number(BigInteger.Min(s.MaxHp, s.Hp + FloorDiv((BigInteger)s.MaxHp * 15, 100)));
            s.ZoneIndex = checked(s.ZoneIndex + 1); s.Zone = s.ZoneIndex < s.Zones.Count ? s.Zones[s.ZoneIndex] : (int?)null;
            s.Position = null; s.Floor = 0; s.Map = GenMap(s, s.Zone); s.Phase = "MAP"; s.Pending = null;
        });

        // Source captureMemory: terminal pending only; legacy keeps no memoryAfter. Commit/store/receipts are later units.
        void CaptureMemory() { if (state.MemoryPolicy == "v2") state.MemoryAfter = TerminalMemory(); }
        MemorySnapshot TerminalMemory()
        {
            var s = state; var after = DeckMemorySnapshot(data, s);
            if (s.PlayBefore == null) return after;
            // Source memorySnapshot: playing the base kit does not itself remove or revert persistent cards.
            var saved = s.MemoryBefore ?? throw new InvalidOperationException("v2 run has no starting memory");
            var savedKeys = new HashSet<string>(saved.CharacterIds.SelectMany(cid => saved.Cards(cid).Select(c => c.Key)), StringComparer.Ordinal);
            var changes = MemoryV2.ListChanges(data.Memory, s.PlayBefore.ToJsonGraph(), after.ToJsonGraph());
            var additions = new List<object?>(); var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var change in changes)
            {
                if (savedKeys.Contains(change.Key) || change.Kind == "remove") continue;
                var addition = change.ToJsonGraph(); addition["kind"] = "add";
                addition["value"] = after.Cards(change.Cid).First(c => c.Key == change.Key).ToJsonGraph();
                // Map.set: a later change for the same key replaces the value in its first position.
                if (positions.TryGetValue(change.Key, out int position)) additions[position] = addition;
                else { positions.Add(change.Key, additions.Count); additions.Add(addition); }
            }
            var applied = changes.Where(delta => savedKeys.Contains(delta.Key)).Select(delta => (object?)delta.ToJsonGraph()).Concat(additions).ToList();
            return MemoryV2.Inspect(data.Memory, MemoryV2.ApplyChanges(saved.ToJsonGraph(), applied)).Snapshot;
        }

        static void Guard(Action command)
        {
            try { command(); }
            catch (OverflowException) { throw new RuleException("RUN_NUMBER_OUT_OF_RANGE"); }
        }
        static T Guarded<T>(Func<T> command)
        {
            try { return command(); }
            catch (OverflowException) { throw new RuleException("RUN_NUMBER_OUT_OF_RANGE"); }
        }
        int Below(int bound) { var rng = new Rng(state.Rng); int value = rng.Below(bound); state.Rng = rng.State; return value; }
        uint NextRandom() { var rng = new Rng(state.Rng); uint value = rng.Next(); state.Rng = rng.State; return value; }
        void Log(string text) { state.Log.Add(text); if (state.Log.Count > 100) state.Log.RemoveAt(0); }
        bool Has(string relic) => state.Relics.Contains(relic);
        IDictionary<string, object?>? PendingFields() => state.Pending as IDictionary<string, object?>;
        static string? PendingKind(object? pending) => MemoryJson.Get(pending, "type") as string;
        // JS `value || fallback` over the integer domain; other truthy values are outside typed Core state (R0 convention).
        static long JsOr(object? value, long fallback)
        {
            if (!MemoryJson.Truthy(value)) return fallback;
            if (!RunValues.TryInteger(value, out long number)) throw new RuleException("RUN_NUMBER_OUT_OF_RANGE");
            return number;
        }
        static int ToInt(long value) => value < int.MinValue || value > int.MaxValue ? throw new RuleException("RUN_NUMBER_OUT_OF_RANGE") : (int)value;
        static int Clamp(long value, int low, int high) => (int)Math.Max(low, Math.Min(high, value));
        static string? OptionalText(object? value) => !MemoryJson.Truthy(value) ? null : value as string ?? throw new RuleException("UNSUPPORTED_STATE_VALUE");
        static bool KeyEquals(RunCardSpec card, string? key) => MemoryJson.Get(card.Fields, "key") is string own && own == key;
        static bool Includes(object? values, string? value) => values is IList list && list.Cast<object?>().Any(item => item is string text && text == value);
        static BigInteger FloorDiv(BigInteger numerator, int denominator)
        {
            var quotient = BigInteger.DivRem(numerator, denominator, out var remainder); return remainder.Sign < 0 ? quotient - 1 : quotient;
        }
        static string NodeName(string type)
        {
            switch (type)
            {
                case "battle": return "전투"; case "elite": return "엘리트"; case "rest": return "휴식"; case "forge": return "공방";
                case "shop": return "상점"; case "event": return "사건"; case "boss": return "보스"; case "archive": return "기록소";
                default: return "undefined";
            }
        }
    }
}
