// P1-PUBLIC-BATTLE-QUERY §14.3/§14.7: the one card-pick policy of a play, shared by the Unity expedition view and Core's
// Battle.HandChoices (and through it the server's battle query). Moved unchanged from MemoryForgeExpeditionClient
// (P1-CARD-FLOW-EFFECT-CANDIDATES), which keeps its public signatures as forwarding wrappers. BCL only: it reads the given state and
// definitions, never writes or retains them, and returns fresh lists. Core's resolution, fallback order and explicit auto are unchanged.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MemoryForge.Core
{
    public static class BattleChoicePolicy
    {
        /// <summary>Why a request offers no card at all (PickBlocked): Core skips its effect before choosing, so no pick could do anything.</summary>
        public const string PickGrowthCap = "PICK_GROWTH_CAP";
        /// <summary>An ordered pick that is not on its request's candidate list given the picks before it, or one more than the requests take.</summary>
        public const string PickInvalid = "PICK_INVALID";

        // Display-only projection of prototype/src/ui.js choiceRequests: the immediate plays whose card picks the player makes, each with the
        // Core effect it feeds and whether that effect is the card's first one (Leading: only the play's commit runs before it). Core still
        // validates and resolves them; picks left unmade keep Core's own choice, which the overlay states.
        static IEnumerable<((string Label, string From, int Count, bool GrowthOnly) Request, (string Op, string? Part, bool Leading) Rule)> Choices(BattleCardDef card, string mode, BattleData? content)
        {
            if (mode != "now") yield break;
            for (int i = 0; i < card.Effects.Count; i++)
            {
                var op = card.Effects[i]; bool leading = i == 0;
                int count = Math.Max(1, op.N ?? 1);
                switch (op.Op)
                {
                    case "exhaustPick": yield return ((op.From == "discard" ? "버린 카드에서 소멸할 카드" : "손에서 소멸할 카드", op.From == "discard" ? "discard" : "hand", count, false), (op.Op, null, leading)); break;
                    case "discard": yield return (("버릴 카드", "hand", count, false), (op.Op, null, leading)); break;
                    case "tune" when op.Pick == "one":
                        string part = content?.Parts.FirstOrDefault(p => p.Id == op.Part)?.Name ?? "부품";
                        yield return (("「" + part + "」을 붙일 카드", "hand", 1, false), (op.Op, op.Part, leading)); break;
                    case "costdown" when op.Pick == "one": yield return (("비용을 낮출 카드", "hand", 1, false), (op.Op, null, leading)); break;
                    case "recall": yield return (("손으로 가져올 카드", "discard", 1, false), (op.Op, null, leading)); break;
                    case "growPick": yield return (("성장시킬 카드", "hand", 1, true), (op.Op, null, leading)); break;
                    case "copyPick": yield return (("사본을 만들 성장 카드", "hand", 1, true), (op.Op, null, leading)); break;
                    case "pull": yield return (("앞당길 예약", "res", 1, false), (op.Op, null, leading)); break;
                }
            }
        }
        /// <summary>The card picks one play of the given (effective) definition asks for, in order: label (part names from content, else
        /// "부품"), zone ("hand", "discard" or "res"), count and whether only growth definitions qualify. A next/after reservation asks for none.</summary>
        public static List<(string Label, string From, int Count, bool GrowthOnly)> ChoiceRequests(BattleCardDef card, string mode, BattleData? content = null) =>
            Choices(card, mode, content).Select(c => c.Request).ToList();
        /// <summary>The Core effect each request of ChoiceRequests feeds, in the same order: its operation (tune, costdown, recall, growPick,
        /// copyPick, discard, exhaustPick, pull), for tune the part it attaches, and whether it is the card's first top-level effect (only
        /// the play's commit runs before it). Metadata of the given (effective) definition only; nothing is resolved here.</summary>
        public static List<(string Op, string? Part, bool Leading)> ChoiceRules(BattleCardDef card, string mode) => Choices(card, mode, null).Select(c => c.Rule).ToList();
        /// <summary>The cards a pick sheet offers for one request, in zone order (prototype/src/ui.js pickerOptions): the hand without the
        /// playing card, the discard pile or the time reservations, none already picked. Without a rule this is that zone list (growth-card
        /// definitions only when the request says so), the sheet's earlier list. With the request's rule (ChoiceRules) it is the list
        /// PickSheet draws, PickCard accepts and a prepared intent revalidates: inside the known scope (see Narrowed) only the zone cards the
        /// reached Core effect takes (EffectEligible; a card it cannot tell stays) and none when Core skips the effect (PickBlocked); outside
        /// that scope the zone list again, so manual choice never shrinks for what the client cannot know. price is Core's price of a hand
        /// card in mode now: price(uid, false) is Run.BattleCostOf / Battle.CostOf, price(uid, true) Run.BattleCostOfWithoutNextFree /
        /// Battle.CostOfWithoutNextFree; a refusal is Core's RuleException. Core still validates and resolves the picks, and its own fallback
        /// for unmade picks. Not promised outside the scope: a card offered there may still be one Core then does not take.</summary>
        public static List<string> PickCandidates(BattleState b, string source, (string Label, string From, int Count, bool GrowthOnly) request,
            IEnumerable<string> picked, IReadOnlyDictionary<string, BattleCardDef> definitions, (string Op, string? Part, bool Leading)? rule = null,
            Func<string, bool, int>? price = null)
        {
            IEnumerable<string> options = request.From == "hand" ? b.Zones.Hand.Where(u => u != source) : request.From == "discard" ? b.Zones.Discard : b.Reservations.Select(r => r.Uid);
            var used = new HashSet<string>(picked, StringComparer.Ordinal);
            options = options.Where(u => !used.Contains(u));
            if (rule == null || !Narrowed(rule.Value, price))
                return (request.GrowthOnly ? options.Where(u => b.Cards.TryGetValue(u, out var c) && definitions.TryGetValue(c.Id, out var d) && d.Growth == true) : options).ToList();
            if (PickBlocked(b, rule.Value) != null) return new List<string>();
            var effect = rule.Value;
            return options.Where(u => EffectEligible(b, effect, u, definitions, price) != false).ToList();
        }
        // The known scope where a request is narrowed to its effect's own candidates. tune, recall, growPick and copyPick: always, because a
        // card their effect refuses now stays refused for the rest of the play (parts are never removed, rarity and growth definitions are
        // fixed, growth and the battle's growth count never go down), so leaving it out never removes a card Core would take. costdown: only
        // as the card's first effect and with Core's price, because then only the play's commit runs before it (see EffectEligible). discard,
        // exhaustPick and pull take any card of their zone.
        static bool Narrowed((string Op, string? Part, bool Leading) rule, Func<string, bool, int>? price)
        {
            switch (rule.Op)
            {
                case "tune": case "recall": case "growPick": case "copyPick": return true;
                case "costdown": return rule.Leading && price != null;
                default: return false;
            }
        }
        /// <summary>PICK_GROWTH_CAP when the battle's growth cap is already reached: Core skips growPick before choosing and the cap count never
        /// goes down, so no pick could do anything. Otherwise null.</summary>
        public static string? PickBlocked(BattleState b, (string Op, string? Part, bool Leading) rule) =>
            rule.Op == "growPick" && b.GrowthCount >= b.GrowthCap ? PickGrowthCap : null;
        /// <summary>The reached Core effect's own validity predicate for one zone card (Battle.Effects ExecuteOp → TakePick), for a request
        /// inside the known scope (see PickCandidates). tune: not already carrying the part and definition rarity not C. recall: definition
        /// rarity not C. growPick: a growth definition below growth 3. copyPick: a growth instance (a growth definition, or an evolution of
        /// the growth root it came from). costdown, as the card's first effect: Core's price above 0 as the effect will see it after the
        /// play's commit spent its one "next card costs 0" charge, which is CostOf while no charge or two or more remain and
        /// CostOfWithoutNextFree while exactly one remains (the source's own play consumes it). discard, exhaustPick and pull: any zone card.
        /// Null when it cannot be told here, and such a card stays offered: a costdown card with a conditional cost (the commit may change
        /// it), a price Core refuses (its RuleException is never read as 0), no price, or a card the state or definitions do not hold.</summary>
        public static bool? EffectEligible(BattleState b, (string Op, string? Part, bool Leading) rule, string uid, IReadOnlyDictionary<string, BattleCardDef> definitions,
            Func<string, bool, int>? price)
        {
            if (!b.Cards.TryGetValue(uid, out var card) || !definitions.TryGetValue(card.Id, out var def)) return null;
            switch (rule.Op)
            {
                case "tune": return (rule.Part == null || !card.Temp.Contains(rule.Part)) && def.Rarity != "C";
                case "recall": return def.Rarity != "C";
                case "growPick": return def.Growth == true && card.Growth < 3;
                case "copyPick":
                    return def.Growth == true || card.From != null && definitions.TryGetValue(card.From, out var root) && root.Growth == true && root.Evolutions?.Contains(card.Id) == true;
                case "costdown":
                    if (price == null || def.CostIf != null) return null;
                    try { return price(uid, b.FreeNext == 1) > 0; }
                    catch (RuleException) { return null; }
                default: return true;
            }
        }
        /// <summary>The ordered picks replayed through the pick sheet's own progression (each request takes its Count picks in turn): every
        /// pick must be on its request's PickCandidates (with that request's rule) given the picks before it. Accepted: how many leading
        /// picks hold; the walk stops at the first that does not, or once every request is filled. Index and Left: the request the next
        /// pick fills and its picks still open (Index == requests.Count once all are filled).</summary>
        public static (int Accepted, int Index, int Left) WalkPicks(BattleState b, string source, IReadOnlyList<(string Label, string From, int Count, bool GrowthOnly)> requests,
            IReadOnlyList<(string Op, string? Part, bool Leading)> rules, IReadOnlyList<string> picks, IReadOnlyDictionary<string, BattleCardDef> definitions,
            Func<string, bool, int>? price)
        {
            var made = new List<string>(); int index = 0, left = requests.Count == 0 ? 0 : requests[0].Count;
            foreach (string uid in picks)
            {
                if (index >= requests.Count || !PickCandidates(b, source, requests[index], made, definitions, rules[index], price).Contains(uid)) break;
                made.Add(uid);
                if (--left <= 0 && ++index < requests.Count) left = requests[index].Count;
            }
            return (made.Count, index, left);
        }
        /// <summary>True when there are no more picks than the requests take and every ordered pick holds (WalkPicks); fewer picks than
        /// requested is an explicit auto, whose rest stays Core's choice.</summary>
        public static bool PicksHold(BattleState b, string source, IReadOnlyList<(string Label, string From, int Count, bool GrowthOnly)> requests,
            IReadOnlyList<(string Op, string? Part, bool Leading)> rules, IReadOnlyList<string> picks, IReadOnlyDictionary<string, BattleCardDef> definitions,
            Func<string, bool, int>? price)
        {
            if (picks.Count > requests.Sum(r => r.Count)) return false;
            return WalkPicks(b, source, requests, rules, picks, definitions, price).Accepted == picks.Count;
        }
    }
}
