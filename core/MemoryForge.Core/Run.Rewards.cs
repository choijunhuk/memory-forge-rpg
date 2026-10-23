// R1a §7.2/§7.5/§8: battle reward offers (coins are drawn in afterBattle) and reward actions.
// Source: engine/rules-core.js pendingEvolutions/rarityRoll/cardChoices/relicChoices/partChoices/takeCard/skipCard/takeRelic/
// takePart/evolve/applyFoundPart, same checks, RNG order, data order and log text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MemoryForge.Core
{
    public sealed partial class Run
    {
        List<object?> PendingEvolutions()
        {
            var offers = new List<object?>();
            foreach (var card in state.Deck)
            {
                var definition = CardDefinition(card.Id);
                if (definition.Growth == true && definition.Evolutions != null && JsOr(MemoryJson.Get(card.Fields, "growth"), 0) >= 3)
                    offers.Add(MemoryJson.Object("key", card.Key, "options", definition.Evolutions.Cast<object?>().ToList()));
            }
            return offers;
        }
        // Boss rewards still consume the roll.
        string RarityRoll(string? type)
        {
            int roll = Below(100);
            if (type == "boss") return "S";
            if (type == "elite") return roll < 55 ? "A" : "S";
            return roll < 58 ? "B" : roll < 92 ? "A" : "S";
        }
        List<object?> CardChoices(string? type)
        {
            var s = state; int count = Has("chrono_crown") ? 2 : 3;
            var chars = s.Party.Select(p => p.CharId).ToList();
            var offered = chars.Select(c => s.Offered.TryGetValue(c, out int times) ? times : 0).ToList();
            // Fairness: when offers differ by more than 2, the first slot goes to the least-offered character without RNG.
            string? need = offered.Count != 0 && offered.Max() - offered.Min() > 2 ? chars[offered.IndexOf(offered.Min())] : null;
            var picks = new List<object?>(); var used = new HashSet<string>(StringComparer.Ordinal); var cards = data.Memory.Battle.Cards;
            for (int k = 0; k < count; k++)
            {
                bool common = k == count - 1 && Below(3) == 0;
                string rarity = RarityRoll(type);
                List<BattleCardDef> pool;
                if (common) pool = cards.Where(c => c.Owner == "common" && c.Pool != false && (c.Rarity == rarity || rarity == "S" && c.Rarity == "A")).ToList();
                else
                {
                    string owner = k == 0 && need is string least && least.Length != 0 ? least : chars[Below(chars.Count)];
                    pool = cards.Where(c => c.Owner == owner && c.Pool != false && c.Rarity == rarity).ToList();
                    if (pool.Count == 0) pool = cards.Where(c => c.Owner == owner && c.Pool != false).ToList();
                }
                pool = pool.Where(c => !used.Contains(c.Id)).ToList();
                if (pool.Count == 0) continue;
                var card = pool[Below(pool.Count)]; used.Add(card.Id); picks.Add(card.Id);
                if (data.Memory.Battle.CharacterIndex.ContainsKey(card.Owner))
                    s.Offered[card.Owner] = checked((s.Offered.TryGetValue(card.Owner, out int before) ? before : 0) + 1);
            }
            return picks;
        }
        // kind: boss (3 boss relics), elite (1 of common+rare), shop (2 distinct common+rare), common (1 common).
        List<string> RelicChoices(string kind)
        {
            var owned = new HashSet<string>(state.Relics, StringComparer.Ordinal);
            string[] rarities = kind == "boss" ? new[] { "boss" } : kind == "common" ? new[] { "common" } : new[] { "common", "rare" };
            int want = kind == "boss" ? 3 : kind == "shop" ? 2 : 1;
            return Draw(data.Memory.Battle.Relics.Where(r => rarities.Contains(r.Rarity) && !owned.Contains(r.Id)).Select(r => r.Id).ToList(), want);
        }
        List<string> PartChoices(bool rareOnly, int count, bool commonOnly = false) =>
            Draw(data.Memory.Battle.Parts.Where(p => p.Slot != "temp" && (!rareOnly || p.Rarity == "rare") && (!commonOnly || p.Rarity == "common")).Select(p => p.Id).ToList(), count);
        // Source splice draw: distinct picks from the data-ordered pool, one Run RNG draw each.
        List<string> Draw(List<string> pool, int count)
        {
            var picks = new List<string>();
            for (int k = 0; k < count && pool.Count != 0; k++) { int index = Below(pool.Count); picks.Add(pool[index]); pool.RemoveAt(index); }
            return picks;
        }

        /// <summary>Source takeCard: BAD_REWARD unless a reward lists the card; adds {id,key,owner,assigned,gained} and clears the offer.</summary>
        public void TakeCard(string id) => Guard(() =>
        {
            var s = state; var p = PendingFields();
            if (p == null || PendingKind(p) != "reward" || !Includes(MemoryJson.Get(p, "cards"), id)) throw new RuleException("BAD_REWARD");
            var definition = CardDefinition(id);
            var card = new RunCardSpec();
            card.Fields["id"] = id; card.Fields["key"] = NextCardKey(s); card.Fields["owner"] = definition.Owner;
            card.Fields["assigned"] = definition.Owner == "common" ? s.Party[0].CharId : null; card.Fields["gained"] = true;
            s.Deck.Add(card); p["cards"] = new List<object?>(); Log("카드 획득: " + definition.Name);
        });
        /// <summary>Source skipCard: +15 coins and the virtue_of_refusal bonus only while cards were still offered.</summary>
        public void SkipCard() => Guard(() =>
        {
            var s = state; var p = PendingFields();
            if (p == null || PendingKind(p) != "reward") throw new RuleException("BAD_REWARD");
            if (MemoryJson.Get(p, "cards") is IList offered && offered.Count != 0)
            {
                s.Coins = checked(s.Coins + 15);
                if (Has("virtue_of_refusal") && RunValues.TryInteger(MemoryJson.Get(s.RunFlags, "refusals"), out long refusals) && refusals < 4)
                {
                    s.RunFlags["refusals"] = ToInt(refusals + 1); s.MaxHp = checked(s.MaxHp + 2); s.Hp = checked(s.Hp + 2);
                }
            }
            p["cards"] = new List<object?>();
        });
        /// <summary>Source takeRelic: no pending-type check; BAD_REWARD unless pending.relic lists the relic.</summary>
        public void TakeRelic(string id) => Guard(() =>
        {
            var s = state; var p = PendingFields(); object? listed = p == null ? null : MemoryJson.Get(p, "relic");
            if (p == null || !MemoryJson.Truthy(listed) || !Includes(listed, id)) throw new RuleException("BAD_REWARD");
            s.Relics.Add(id); p["relic"] = null; Log("유물 획득: " + RelicDefinition(id).Name);
        });
        /// <summary>Source takePart: no pending-type check; BAD_REWARD unless pending.part is set.</summary>
        public void TakePart() => Guard(() =>
        {
            var s = state; var p = PendingFields(); object? part = p == null ? null : MemoryJson.Get(p, "part");
            if (p == null || !MemoryJson.Truthy(part)) throw new RuleException("BAD_REWARD");
            s.PartsFound.Add(part as string ?? throw new RuleException("UNSUPPORTED_STATE_VALUE")); p["part"] = null;
        });
        /// <summary>Source evolve: BAD_REWARD without pending evolutions, then BAD_EVOLUTION; sets from/id/evolved and drops the offer.</summary>
        public void Evolve(string key, string branchId) => Guard(() =>
        {
            var s = state; var p = PendingFields(); object? listed = p == null ? null : MemoryJson.Get(p, "evolutions");
            if (p == null || !MemoryJson.Truthy(listed)) throw new RuleException("BAD_REWARD");
            var evolutions = listed as IList ?? throw new MemoryBoundaryException("TypeError", "p.evolutions.find is not a function");
            object? offer = evolutions.Cast<object?>().FirstOrDefault(x => MemoryJson.Get(x, "key") is string offeredKey && offeredKey == key);
            if (offer == null || !Includes(MemoryJson.Get(offer, "options"), branchId)) throw new RuleException("BAD_EVOLUTION");
            var card = s.Deck.FirstOrDefault(c => KeyEquals(c, key)) ?? throw new MemoryBoundaryException("TypeError", "Cannot set properties of undefined (setting 'from')");
            card.Fields["from"] = card.Id; card.Fields["id"] = branchId; card.Fields["evolved"] = true;
            p["evolutions"] = evolutions.Cast<object?>().Where(x => !(MemoryJson.Get(x, "key") is string otherKey && otherKey == key)).ToList();
            Log("진화: " + CardDefinition(branchId).Name);
        });
        /// <summary>Source applyFoundPart: no phase check (Q4); NO_PART, BAD_CARD, then INVALID_PART for v2 non-circuit/engraving parts.
        /// Merges mods, removes the first found copy and recaptures memoryAfter while a save is pending.</summary>
        public void ApplyFoundPart(string partId, string key) => Guard(() =>
        {
            var s = state; int index = s.PartsFound.IndexOf(partId);
            if (index < 0) throw new RuleException("NO_PART");
            var card = s.Deck.FirstOrDefault(c => KeyEquals(c, key));
            if (card == null || card.Weapon) throw new RuleException("BAD_CARD");
            data.Memory.Battle.PartIndex.TryGetValue(partId, out var part);
            if (s.MemoryPolicy == "v2" && (part == null || part.Slot != "circuit" && part.Slot != "engraving")) throw new RuleException("INVALID_PART");
            if (part == null) throw new MemoryBoundaryException("TypeError", "Cannot read properties of undefined (reading 'slot')");
            var mods = MemoryJson.Get(card.Fields, "mods") is IDictionary<string, object?> old ? RunValues.Fields(old) : MemoryJson.Object();
            mods[part.Slot == "circuit" ? "c" : "e"] = partId;
            card.Fields["mods"] = mods; s.PartsFound.RemoveAt(index);
            if (PendingKind(s.Pending) == "save") CaptureMemory();
        });

        BattleCardDef CardDefinition(string? id) => id != null && data.Memory.Battle.CardIndex.TryGetValue(id, out var definition) ? definition
            : throw new MemoryBoundaryException("TypeError", "Unknown expedition card definition");
        BattlePartDef PartDefinition(string id) => data.Memory.Battle.PartIndex.TryGetValue(id, out var definition) ? definition
            : throw new MemoryBoundaryException("TypeError", "Unknown expedition part definition");
        BattleRelicDef RelicDefinition(string id) => data.Memory.Battle.Relics.FirstOrDefault(r => r.Id == id)
            ?? throw new MemoryBoundaryException("TypeError", "Unknown expedition relic definition");
    }
}
