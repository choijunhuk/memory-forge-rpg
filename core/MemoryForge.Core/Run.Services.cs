// R1b §7.5-7.7/§8: rest, forge, shop and event node services plus common-card assignment.
// Source: engine/rules-core.js Run assign/rest/forgeOffer/forgeApply/forgeRemove/buy/eventChoose, same checks, RNG order,
// field order and numbers. Like the source these mutate in place in source order; effects already applied stay applied when a
// later step throws (event card pool RNG_BOUND, copy from an all-weapon deck). Memory preview/commit are later R1 units.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MemoryForge.Core
{
    public sealed partial class Run
    {
        /// <summary>Source assign: no phase check; BAD_ASSIGN unless the key is a common deck card and charId is in the party.
        /// v2 refuses any key saved in memoryBefore (same character included) with REASSIGNMENT_REQUIRES_SEPARATE_FLOW.
        /// Recaptures memoryAfter while a save is pending.</summary>
        public void Assign(string key, string charId) => Guard(() =>
        {
            var s = state; var card = s.Deck.FirstOrDefault(c => KeyEquals(c, key));
            if (card == null || card.Owner != "common" || !s.Party.Any(x => MemoryJson.Get(x.Fields, "charId") is string member && member == charId)) throw new RuleException("BAD_ASSIGN");
            if (s.MemoryPolicy == "v2")
            {
                var saved = s.MemoryBefore ?? throw new InvalidOperationException("v2 run has no starting memory");
                if (saved.CharacterIds.Any(cid => saved.Cards(cid).Any(c => c.Key == key))) throw new RuleException("REASSIGNMENT_REQUIRES_SEPARATE_FLOW");
            }
            card.Fields["assigned"] = charId;
            if (PendingKind(s.Pending) == "save") CaptureMemory();
        });

        /// <summary>Source rest: WRONG_PHASE outside REST or without uses. heal: CANNOT_HEAL with memory_anvil, else
        /// floor(maxHp*(30, +10 with old_compass)/100) capped at maxHp. upgrade: BAD_UPGRADE for a missing, already-plus or
        /// weapon card, else plus. Other choices BAD_CHOICE. Spends one use.</summary>
        public void Rest(string choice, string? key = null) => Guard(() =>
        {
            var s = state; var p = PendingFields();
            if (s.Phase != "REST" || p == null || PendingUses(p) <= 0) throw new RuleException("WRONG_PHASE");
            if (choice == "heal")
            {
                if (Has("memory_anvil")) throw new RuleException("CANNOT_HEAL");
                int pct = 30 + (Has("old_compass") ? 10 : 0);
                // Math.floor(maxHp * pct / 100): the product is exact and the quotient never rounds across an integer.
                s.Hp = Number(BigInteger.Min(s.MaxHp, s.Hp + FloorDiv((BigInteger)s.MaxHp * pct, 100)));
            }
            else if (choice == "upgrade")
            {
                var card = s.Deck.FirstOrDefault(c => KeyEquals(c, key));
                if (card == null || MemoryJson.Truthy(MemoryJson.Get(card.Fields, "plus")) || card.Weapon) throw new RuleException("BAD_UPGRADE");
                card.Fields["plus"] = true;
            }
            else throw new RuleException("BAD_CHOICE");
            p["uses"] = ToInt(checked(PendingUses(p) - 1));
        });

        /// <summary>Source forgeOffer: WRONG_PHASE outside FORGE or without uses, BAD_CARD for a missing or weapon card; replaces
        /// pending.offers with {key, parts: 3 distinct non-temp parts} without spending a use. Returns a detached copy.</summary>
        public Dictionary<string, object?> ForgeOffer(string key) => Guarded(() =>
        {
            var s = state; var p = PendingFields();
            if (s.Phase != "FORGE" || p == null || PendingUses(p) <= 0) throw new RuleException("WRONG_PHASE");
            var card = s.Deck.FirstOrDefault(c => KeyEquals(c, key));
            if (card == null || card.Weapon) throw new RuleException("BAD_CARD");
            var offers = MemoryJson.Object("key", key, "parts", PartChoices(false, 3).Cast<object?>().ToList());
            p["offers"] = offers;
            return RunValues.Fields(offers);
        });

        /// <summary>Source forgeApply: no phase or uses check (Q4); BAD_PART unless pending.offers lists the part. Merges the part
        /// into the offered card's mods (circuit → c, else e), clears offers and spends a use (which may go below 0, as in source).
        /// An offered card removed meanwhile is the source TypeError, before any change.</summary>
        public void ForgeApply(string partId) => Guard(() =>
        {
            var s = state; var p = PendingFields(); object? offers = p == null ? null : MemoryJson.Get(p, "offers");
            if (p == null || !MemoryJson.Truthy(offers) || !Includes(MemoryJson.Get(offers, "parts"), partId)) throw new RuleException("BAD_PART");
            object? offered = MemoryJson.Get(offers, "key");
            var card = s.Deck.FirstOrDefault(c => offered is string offeredKey && KeyEquals(c, offeredKey))
                ?? throw new MemoryBoundaryException("TypeError", "Cannot read properties of undefined (reading 'mods')");
            var part = PartDefinition(partId);
            var mods = MemoryJson.Get(card.Fields, "mods") is IDictionary<string, object?> old ? RunValues.Fields(old) : MemoryJson.Object();
            mods[part.Slot == "circuit" ? "c" : "e"] = partId;
            card.Fields["mods"] = mods;
            p["offers"] = null; p["uses"] = ToInt(checked(PendingUses(p) - 1));
        });

        /// <summary>Source forgeRemove: WRONG_PHASE outside FORGE or without uses, BAD_CARD for a missing or weapon card; removes
        /// the card and spends a use. Pending offers are left as they are.</summary>
        public void ForgeRemove(string key) => Guard(() =>
        {
            var s = state; var p = PendingFields();
            if (s.Phase != "FORGE" || p == null || PendingUses(p) <= 0) throw new RuleException("WRONG_PHASE");
            int index = s.Deck.FindIndex(c => KeyEquals(c, key));
            if (index < 0 || s.Deck[index].Weapon) throw new RuleException("BAD_CARD");
            s.Deck.RemoveAt(index); p["uses"] = ToInt(checked(PendingUses(p) - 1));
        });

        /// <summary>Source buy: WRONG_PHASE outside SHOP. remove: NOT_FOR_SALE once used (removeCost null), COINS_LOW, BAD_CARD
        /// for a missing or weapon card, then pay, remove, count runFlags.removals and close removal for this shop.
        /// cards/parts/relics: NOT_FOR_SALE unless an unsold listing has the id, COINS_LOW, then pay, mark sold and grant.</summary>
        public void Buy(string kind, string? id = null, string? key = null) => Guard(() =>
        {
            var s = state;
            if (s.Phase != "SHOP") throw new RuleException("WRONG_PHASE");
            var p = PendingFields() ?? throw new MemoryBoundaryException("TypeError", "Cannot read properties of null (reading '" + (kind == "remove" ? "removeCost" : kind) + "')");
            if (kind == "remove")
            {
                object? cost = MemoryJson.Get(p, "removeCost");
                if (cost == null) throw new RuleException("NOT_FOR_SALE"); // one removal per shop
                long removeCost = StateInteger(cost);
                if (s.Coins < removeCost) throw new RuleException("COINS_LOW");
                int index = s.Deck.FindIndex(c => KeyEquals(c, key));
                if (index < 0 || s.Deck[index].Weapon) throw new RuleException("BAD_CARD");
                s.Coins = ToInt(checked(s.Coins - removeCost)); s.Deck.RemoveAt(index);
                s.RunFlags["removals"] = ToInt(checked(JsOr(MemoryJson.Get(s.RunFlags, "removals"), 0) + 1)); p["removeCost"] = null; return;
            }
            // Source `p[kind] && p[kind].find(...)`: a truthy non-array (type, removeCost, Object.prototype names) is a TypeError.
            object? listed = MemoryJson.Get(p, kind);
            if (ReferenceEquals(listed, MemoryJson.Missing) && ObjectPrototypeMembers.Contains(kind)) throw new MemoryBoundaryException("TypeError", "list.find is not a function");
            IDictionary<string, object?>? item = null;
            if (MemoryJson.Truthy(listed))
            {
                if (!(listed is IList list)) throw new MemoryBoundaryException("TypeError", "list.find is not a function");
                item = list.OfType<IDictionary<string, object?>>().FirstOrDefault(x => MemoryJson.Get(x, "id") is string listedId && listedId == id && !MemoryJson.Truthy(MemoryJson.Get(x, "sold")));
            }
            if (item == null) throw new RuleException("NOT_FOR_SALE");
            long price = StateInteger(MemoryJson.Get(item, "price"));
            if (s.Coins < price) throw new RuleException("COINS_LOW");
            s.Coins = ToInt(checked(s.Coins - price)); item["sold"] = true;
            if (kind == "cards")
            {
                var definition = CardDefinition(id);
                var bought = new RunCardSpec();
                bought.Fields["id"] = id; bought.Fields["key"] = NextCardKey(s); bought.Fields["owner"] = definition.Owner;
                bought.Fields["assigned"] = definition.Owner == "common" ? s.Party[0].CharId : null; bought.Fields["gained"] = true;
                s.Deck.Add(bought);
            }
            if (kind == "parts") s.PartsFound.Add(id!);
            if (kind == "relics") s.Relics.Add(id!);
        });

        /// <summary>Source eventChoose: WRONG_PHASE outside EVENT; BAD_CHOICE for a missing choice or a resolved event; COINS_LOW
        /// below need.coins and WOULD_KILL before any effect. Effects then run in source order; variation opens a fromEvent
        /// FORGE and returns before done. keys: one deck key or a list (one pick per copied/removed card); missing removal picks
        /// fall back to the first curse, then the first non-weapon card; a missing copy pick to the first non-weapon card.</summary>
        public void EventChoose(int index, object? keys = null) => Guard(() =>
        {
            var s = state; var p = PendingFields();
            if (s.Phase != "EVENT") throw new RuleException("WRONG_PHASE");
            if (p == null) throw new MemoryBoundaryException("TypeError", "Cannot read properties of null (reading 'id')");
            string? eventId = MemoryJson.Get(p, "id") as string;
            // Source ix.events is Object.fromEntries over data order: the last event with an id wins.
            var definition = data.Events.LastOrDefault(e => e.Id == eventId)
                ?? throw new MemoryBoundaryException("TypeError", "Cannot read properties of undefined (reading 'choices')");
            var choice = index >= 0 && index < definition.Choices.Count ? definition.Choices[index] : null;
            if (choice == null || MemoryJson.Truthy(MemoryJson.Get(p, "done"))) throw new RuleException("BAD_CHOICE"); // an event resolves once
            var fx = choice.Effects;
            if (Present(choice.RequiredCoins) && s.Coins < choice.RequiredCoins.GetValueOrDefault()) throw new RuleException("COINS_LOW");
            if (Present(fx.Hp) && s.Hp + EventHp(fx.Hp.GetValueOrDefault()) < 1) throw new RuleException("WOULD_KILL");
            if (Present(fx.Coins)) s.Coins = ToInt(Math.Max(0L, (long)s.Coins + fx.Coins.GetValueOrDefault()));
            if (Present(fx.Hp)) s.Hp = Number(BigInteger.Max(1, s.Hp + EventHp(fx.Hp.GetValueOrDefault())));
            if (Present(fx.Heal)) s.Hp = Number(BigInteger.Min(s.MaxHp, s.Hp + EventHp(fx.Heal.GetValueOrDefault())));
            if (Present(fx.MaxHp)) { s.MaxHp = Number(s.MaxHp + EventHp(fx.MaxHp.GetValueOrDefault())); s.Hp = Math.Min(s.Hp, s.MaxHp); }
            if (Present(fx.MaxHpPct))
            {
                // Math.floor(maxHp * (100 + pct) / 100), exact while the product stays below 2^53 (all authored data).
                s.MaxHp = Number(BigInteger.Max(1, FloorDiv((BigInteger)s.MaxHp * (100 + (BigInteger)fx.MaxHpPct.GetValueOrDefault()), 100))); s.Hp = Math.Min(s.Hp, s.MaxHp);
            }
            if (Present(fx.NextApBonus)) s.RunFlags["nextApBonus"] = ToInt(checked(JsOr(MemoryJson.Get(s.RunFlags, "nextApBonus"), 0) + fx.NextApBonus.GetValueOrDefault()));
            if (!string.IsNullOrEmpty(fx.Curse))
            {
                var curse = new RunCardSpec();
                curse.Fields["id"] = fx.Curse; curse.Fields["key"] = NextCardKey(s); curse.Fields["owner"] = "curse"; curse.Fields["gained"] = true;
                s.Deck.Add(curse);
            }
            if (!string.IsNullOrEmpty(fx.Relic))
            {
                var relics = RelicChoices(fx.Relic == "common" ? "common" : "elite");
                if (relics.Count != 0 && !string.IsNullOrEmpty(relics[0])) s.Relics.Add(relics[0]);
            }
            if (!string.IsNullOrEmpty(fx.Card))
            {
                var owners = s.Party.Select(x => x.CharId).ToList();
                var pool = data.Memory.Battle.Cards.Where(c => owners.Contains(c.Owner) && c.Rarity == fx.Card && c.Pool != false).ToList();
                var drawn = pool[Below(pool.Count)];
                var gained = new RunCardSpec();
                gained.Fields["id"] = drawn.Id; gained.Fields["key"] = NextCardKey(s); gained.Fields["owner"] = drawn.Owner; gained.Fields["gained"] = true;
                s.Deck.Add(gained);
            }
            // part draws fx.part common parts and keeps the first; partRare always draws exactly one rare part.
            if (Present(fx.Part)) s.PartsFound.AddRange(PartChoices(false, fx.Part.GetValueOrDefault(), true).Take(1));
            if (Present(fx.PartRare)) s.PartsFound.AddRange(PartChoices(true, 1));
            if (Present(fx.Variation))
            {
                s.Phase = "FORGE"; s.Pending = MemoryJson.Object("type", "forge", "uses", fx.Variation.GetValueOrDefault(), "offers", null, "fromEvent", true); return;
            }
            if (Present(fx.Growth))
            {
                // One growth step on one random growth card below 3, whatever the authored growth number.
                var growing = s.Deck.Where(c => CardDefinition(c.Id).Growth == true && JsOr(MemoryJson.Get(c.Fields, "growth"), 0) < 3).ToList();
                if (growing.Count != 0)
                {
                    var grown = growing[Below(growing.Count)];
                    grown.Fields["growth"] = ToInt(checked(JsOr(MemoryJson.Get(grown.Fields, "growth"), 0) + 1));
                }
            }
            // Source Array.isArray(key) ? key : (key ? [key] : []); picks compare strictly with deck keys.
            IList picks = keys is IList given ? given : MemoryJson.Truthy(keys) ? new List<object?> { keys } : (IList)new List<object?>();
            object? Pick(int position) => position < picks.Count ? picks[position] : null;
            if (Present(fx.CardCopy))
            {
                object? first = Pick(0);
                // Source clone(undefined) throws before a key is drawn when every card is a weapon.
                var original = s.Deck.FirstOrDefault(c => first is string firstKey && KeyEquals(c, firstKey) && !c.Weapon) ?? s.Deck.FirstOrDefault(c => !c.Weapon)
                    ?? throw new MemoryBoundaryException("SyntaxError", "\"undefined\" is not valid JSON");
                var copy = original.Copy(); copy.Fields["key"] = NextCardKey(s); copy.Fields["gained"] = true;
                s.Deck.Add(copy);
            }
            if (Present(fx.CardRemove))
            {
                for (int k = 0; k < fx.CardRemove.GetValueOrDefault(); k++)
                {
                    object? pick = Pick(k);
                    int at = MemoryJson.Truthy(pick) ? s.Deck.FindIndex(c => pick is string pickKey && KeyEquals(c, pickKey) && !c.Weapon) : -1;
                    if (at < 0) at = s.Deck.FindIndex(c => c.Owner == "curse");
                    if (at < 0) at = s.Deck.FindIndex(c => !c.Weapon);
                    if (at >= 0) s.Deck.RemoveAt(at);
                }
            }
            p["done"] = true; p["choice"] = index;
        });

        // Source hpk: Math.round(v * (hpRef || 81*n) / (81*n)). The exact half-up rational rounding matches Math.round because
        // a non-half quotient is at least 1/(2*81n) from a half step. An empty party divides by zero in JS (NaN), outside typed state.
        BigInteger EventHp(int value)
        {
            int scale = checked(81 * state.Party.Count);
            if (scale == 0) throw new RuleException("RUN_NUMBER_OUT_OF_RANGE");
            long reference = state.HpReference != 0 ? state.HpReference : scale;
            return Round((BigInteger)value * reference, scale);
        }
        // JS `if (fx.n)` over authored integers.
        static bool Present(int? value) => value.GetValueOrDefault() != 0;
        // pending.uses is always an authored integer; anything else is outside typed Core state (R0 convention).
        static long PendingUses(IDictionary<string, object?> pending) => StateInteger(MemoryJson.Get(pending, "uses"));
        static long StateInteger(object? value) => RunValues.TryInteger(value, out long number) ? number : throw new RuleException("RUN_NUMBER_OUT_OF_RANGE");
        // Object.prototype members reachable through a plain-object pending[kind] lookup.
        static readonly HashSet<string> ObjectPrototypeMembers = new HashSet<string>(StringComparer.Ordinal)
        {
            "constructor", "__proto__", "__defineGetter__", "__defineSetter__", "__lookupGetter__", "__lookupSetter__",
            "hasOwnProperty", "isPrototypeOf", "propertyIsEnumerable", "toString", "toLocaleString", "valueOf"
        };
    }
}
