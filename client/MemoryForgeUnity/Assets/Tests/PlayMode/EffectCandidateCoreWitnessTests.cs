// Claude's original seven concrete-effect witnesses, recovered verbatim from the official initial author transcript and adapted by Codex
// for the accepted three-field metadata / pure cost callback. The rejected auto-only FreeNext=1 expectation is replaced by an explicit
// legal manual-target command witness. Codex added three independent pricing/purity/unknown-input regressions. No production state injection.
// The conditional/overflow definitions below are explicitly test-local, unauthored branch probes; all other data is exported source content.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MemoryForge.Core;
using Newtonsoft.Json;
using NUnit.Framework;

namespace MemoryForge.Client.Tests
{
    public class EffectCandidateCoreWitnessTests
    {

        // Every real request card's effect rule matches its request, in order; reservations get none.
        [Test]
        public void ChoiceRulesNameTheCoreEffectOfEveryRequestInOrder()
        {
            var content = ClientBattleContent.Load().Battle; var defs = Defs(content);
            var expected = new Dictionary<string, (string Op, string? Part)[]>
            {
                ["nia_tune"] = new[] { ("tune", (string?)"amp") }, ["nia_overclock"] = new[] { ("costdown", (string?)null) }, ["nia_rebuild"] = new[] { ("recall", (string?)null) },
                ["com_recall"] = new[] { ("recall", (string?)null) }, ["yuri_dew"] = new[] { ("growPick", (string?)null) }, ["wc_yuri"] = new[] { ("growPick", (string?)null) },
                ["yuri_metamorph"] = new[] { ("copyPick", (string?)null) }, ["sion_erase"] = new[] { ("exhaustPick", (string?)null) }, ["sion_forget"] = new[] { ("exhaustPick", (string?)null) },
                ["com_salvage"] = new[] { ("exhaustPick", (string?)null) }, ["wc_sion"] = new[] { ("exhaustPick", (string?)null) }, ["sera_reload"] = new[] { ("discard", (string?)null) },
                // RULES_NOW_PLAY (2026-10-19): sera_pull (시간 당기기) no longer pulls a reservation; it returns a discard card (recall).
                ["com_sort"] = new[] { ("discard", (string?)null) }, ["sera_pull"] = new[] { ("recall", (string?)null) }
            };
            foreach (var pair in expected)
                Assert.That(MemoryForgeExpeditionClient.ChoiceRules(defs[pair.Key], "now").Select(r => (r.Op, r.Part)), Is.EqualTo(pair.Value), pair.Key);
            var nonLeading = new HashSet<string> { "sera_reload", "com_sort", "wc_yuri", "wc_sion" };
            foreach (var id in expected.Keys)
                Assert.That(Rule(content, id).Leading, Is.EqualTo(!nonLeading.Contains(id)), id + ": literal authored prelude boundary");
            foreach (var def in content.Cards)
            {
                Assert.That(MemoryForgeExpeditionClient.ChoiceRules(def, "now").Count, Is.EqualTo(MemoryForgeExpeditionClient.ChoiceRequests(def, "now", content).Count), def.Id + ": one rule per request");
                Assert.That(MemoryForgeExpeditionClient.ChoiceRules(def, "next"), Is.Empty, def.Id + ": a reservation makes no picks");
                if (MemoryForgeExpeditionClient.ChoiceRules(def, "now").Count != 0) Assert.That(expected.ContainsKey(def.Id), Is.True, def.Id + " is an authored pick card this test covers");
            }
        }

        // tune(one): a real rarity-C curse (eye_of_rift's smudge) and a card already carrying amp are not offered; Core would replace either
        // pick by its fallback (twin battle), and takes an offered pick as asked. Hand membership is the whole small deck each turn.
        [Test]
        public void TuneTakesNeitherACurseNorACardAlreadyCarryingItsPart()
        {
            var content = ClientBattleContent.Load().Battle; var defs = Defs(content);
            BattleCardSpec[] deck = Deck("nia_tune", "sera_round", "com_slash", "sera_cover");
            Battle Create() => Begin(content, 31, new[] { "nia", "sera", "harin" }, deck, new[] { "eye_of_rift" });
            var battle = Create(); var first = battle.State;
            Assert.That(first.Zones.Hand.Count, Is.EqualTo(5), "precondition: the four cards and the relic's curse are the whole hand");
            string tune = Uid(first, "nia_tune"), slash = Uid(first, "com_slash"), smudge = Uid(first, "smudge");
            Assert.That(defs["smudge"].Rarity, Is.EqualTo("C"), "the source's curse is rarity C");
            var request = Request(content, "nia_tune"); var rule = Rule(content, "nia_tune");
            Func<string, bool, int> cost = (u, without) => without ? battle.CostOfWithoutNextFree(u, "now") : battle.CostOf(u, "now");
            Assert.That(Offer(first, tune, request, defs), Does.Contain(smudge), "the zone list without a rule (the old sheet's) offers the curse");
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(first, tune, request, new string[0], defs, rule, cost), Is.EqualTo(first.Zones.Hand.Where(u => u != tune && u != smudge).ToList()),
                "tune offers every other hand card but the curse");

            var twin = Create();
            Commit(twin, "play", Play(tune, "now", null, new[] { smudge }));
            var replaced = twin.State.Cards.Where(c => c.Value.Temp.Contains("amp")).Select(c => c.Key).ToList();
            Assert.That(replaced.Count, Is.EqualTo(1));
            Assert.That(replaced[0], Is.Not.EqualTo(smudge), "Core silently tunes another card for a curse pick: why the sheet hides it");

            Commit(battle, "play", Play(tune, "now", null, new[] { slash }));
            Assert.That(battle.State.Cards[slash].Temp, Does.Contain("amp"), "an offered pick is taken as asked");
            Commit(battle, "end");
            var second = battle.State;
            Assert.That((second.Phase, second.Zones.Hand.Count), Is.EqualTo(("PLAYER", 5)), "precondition: the whole deck is the next hand again");
            Assert.That(second.Zones.Hand, Does.Contain(tune).And.Contain(slash));
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(second, tune, request, new string[0], defs, rule, cost), Is.EqualTo(second.Zones.Hand.Where(u => u != tune && u != smudge && u != slash).ToList()),
                "a card already carrying amp is not offered again");
            Assert.That(Offer(second, tune, request, defs), Does.Contain(slash), "the zone list would still offer it");

            var twin2 = Create();
            Commit(twin2, "play", Play(tune, "now", null, new[] { slash })); Commit(twin2, "end");
            Commit(twin2, "play", Play(tune, "now", null, new[] { slash }));
            Assert.That(twin2.State.Cards.Count(c => c.Value.Temp.Contains("amp")), Is.EqualTo(2), "Core tunes a different card for a repeated amp pick");
        }

        // costdown(one): Core's actual current cost decides (a cost-0 definition and a real cost -1 circuit, c_lite, are not offered; a
        // 보존 card with cost above 0 is). Core would replace an ineligible pick, and takes an offered one as asked.
        [Test]
        public void CostdownOffersOnlyHandCardsWhoseActualCurrentCostIsAboveZero()
        {
            var content = ClientBattleContent.Load().Battle; var defs = Defs(content);
            var deck = new[] { Spec("nia_overclock"), Spec("nia_tune"), new BattleCardSpec { Id = "sera_round", Circuit = "c_lite" }, Spec("sera_timed"), Spec("com_slash") };
            Battle Create() => Begin(content, 32, new[] { "nia", "sera", "harin" }, deck, new string[0]);
            var battle = Create(); var s = battle.State;
            Assert.That(s.Zones.Hand.Count, Is.EqualTo(5), "precondition: the whole deck is the hand");
            string overclock = Uid(s, "nia_overclock"), tune = Uid(s, "nia_tune"), round = Uid(s, "sera_round"), timed = Uid(s, "sera_timed"), slash = Uid(s, "com_slash");
            Assert.That(new[] { battle.CostOf(tune, "now"), battle.CostOf(round, "now"), battle.CostOf(timed, "now"), battle.CostOf(slash, "now") }, Is.EqualTo(new[] { 0, 0, 2, 1 }),
                "Core's current costs: a cost-0 definition, sera_round (cost 1) with the real cost -1 circuit, the 보존 sera_timed cost 2, a cost 1");
            Assert.That(defs["sera_round"].Cost, Is.EqualTo(1), "the circuit, not the definition, makes sera_round cost 0");
            var request = Request(content, "nia_overclock"); var rule = Rule(content, "nia_overclock");
            Func<string, bool, int> cost = (u, without) => without ? battle.CostOfWithoutNextFree(u, "now") : battle.CostOf(u, "now");
            Assert.That(MemoryForgeExpeditionClient.PickBlocked(s, rule), Is.Null);
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(s, overclock, request, new string[0], defs, rule, cost), Is.EqualTo(s.Zones.Hand.Where(u => u == timed || u == slash).ToList()));
            Assert.That(Offer(s, overclock, request, defs), Does.Contain(tune).And.Contain(round), "the zone list would offer the cost-0 cards");
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(s, overclock, request, new string[0], defs, rule, null), Is.EqualTo(Offer(s, overclock, request, defs)), "without Core pricing retain the existing manual path");

            var twin = Create();
            Commit(twin, "play", Play(overclock, "now", null, new[] { round }));
            var t = twin.State;
            Assert.That((t.Cards[round].CostMod, t.Cards[timed].CostMod, t.Cards[slash].CostMod), Is.EqualTo((0, -1, 0)), "Core replaces a cost-0 pick by its highest-cost eligible card");
            Commit(battle, "play", Play(overclock, "now", null, new[] { slash }));
            var after = battle.State;
            Assert.That((after.Cards[slash].CostMod, after.Cards[timed].CostMod), Is.EqualTo((-1, 0)), "an offered pick is taken as asked");
        }

        // The real ultimate supplies two charges. At one charge, the source consumes it before costdown; manual selection remains legal.
        [Test]
        public void CostdownPreservesAManualTargetAfterTheLastFreeCharge()
        {
            var content = ClientBattleContent.Load().Battle; var defs = Defs(content);
            var deck = Deck("nia_overclock", "com_slash", "sera_round", "com_guard", "sera_cover");
            Battle Create() => Begin(content, 33, new[] { "rien", "nia", "sera" }, deck, new string[0], "rien");
            var battle = Create(); var s = battle.State;
            Assert.That(s.Zones.Hand.Count, Is.EqualTo(5));
            string overclock = Uid(s, "nia_overclock"), slash = Uid(s, "com_slash"), round = Uid(s, "sera_round");
            var request = Request(content, "nia_overclock"); var rule = Rule(content, "nia_overclock");
            Func<string, bool, int> cost = (u, without) => without ? battle.CostOfWithoutNextFree(u, "now") : battle.CostOf(u, "now");
            Commit(battle, "ult", BattleArgument.Object(new[] { Pair("actor", "rien"), Pair("target", null) }));
            s = battle.State;
            Assert.That(s.FreeNext, Is.EqualTo(2));
            string before = Fingerprint(battle);
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(s, overclock, request, new string[0], defs, rule, cost), Is.Empty,
                "two charges: Core still sees every target free after the source commits");
            Assert.That(Fingerprint(battle), Is.EqualTo(before));
            var twin = Create();
            Commit(twin, "ult", BattleArgument.Object(new[] { Pair("actor", "rien"), Pair("target", null) }));
            Commit(twin, "play", Play(overclock, "now", null, new[] { slash }));
            Assert.That((twin.State.Cards[slash].CostMod, twin.State.FreeNext), Is.EqualTo((0, 1)));
            Commit(battle, "play", Play(round, "now", s.Enemies[0].Uid));
            s = battle.State;
            Assert.That(s.FreeNext, Is.EqualTo(1));
            Assert.That((battle.CostOf(slash), battle.CostOfWithoutNextFree(slash)), Is.EqualTo((0, 1)));
            before = Fingerprint(battle);
            Assert.That(MemoryForgeExpeditionClient.PickBlocked(s, rule), Is.Null);
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(s, overclock, request, new string[0], defs, rule, cost),
                Is.EqualTo(s.Zones.Hand.Where(u => u != overclock).ToList()), "all other authored cards cost 1 after the charge is spent");
            Assert.That(Fingerprint(battle), Is.EqualTo(before), "offering a manual target spends no state or receipt");
            Commit(battle, "play", Play(overclock, "now", null, new[] { slash }));
            Assert.That((battle.State.Cards[slash].CostMod, battle.State.FreeNext), Is.EqualTo((-1, 0)));
            Assert.That(battle.State.Cards.Where(c => c.Value.CostMod != 0).Select(c => c.Key), Is.EqualTo(new[] { slash }),
                "Core modifies exactly the explicit card, with no fallback substitution");
        }

        // recall: a real curse in the discard pile is not offered (Core would bring back another card for it), any other discard card is
        // and is taken as asked; exhaustPick from the discard pile still takes any card.
        [Test]
        public void RecallSkipsCursesWhileExhaustPickTakesAnyDiscardCard()
        {
            var content = ClientBattleContent.Load().Battle; var defs = Defs(content);
            var deck = Deck("com_recall", "com_sort", "sera_round", "com_slash");
            Battle Create()
            {
                var b = Begin(content, 34, new[] { "nia", "sera", "harin" }, deck, new[] { "eye_of_rift" });
                var s0 = b.State;
                Assert.That(s0.Zones.Hand.Count, Is.EqualTo(5), "precondition: the four cards and the curse are the whole hand");
                Commit(b, "play", Play(Uid(s0, "com_sort"), "now", null, new[] { Uid(s0, "smudge") })); // draws nothing (both piles empty), discards the curse
                Commit(b, "play", Play(Uid(s0, "sera_round"), "now", s0.Enemies[0].Uid));
                return b;
            }
            var battle = Create(); var s = battle.State;
            string recall = Uid(s, "com_recall"), sort = Uid(s, "com_sort"), round = Uid(s, "sera_round"), smudge = Uid(s, "smudge");
            Assert.That(s.Zones.Discard, Is.EqualTo(new[] { smudge, sort, round }), "precondition: the curse, then the two played cards");
            var request = Request(content, "com_recall"); var rule = Rule(content, "com_recall");
            Func<string, bool, int> cost = (u, without) => without ? battle.CostOfWithoutNextFree(u, "now") : battle.CostOf(u, "now");
            Assert.That(MemoryForgeExpeditionClient.PickBlocked(s, rule), Is.Null);
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(s, recall, request, new string[0], defs, rule, cost), Is.EqualTo(new[] { sort, round }));
            Assert.That(Offer(s, recall, request, defs), Does.Contain(smudge), "the zone list would offer the curse");
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(s, recall, Request(content, "sion_forget"), new string[0], defs, Rule(content, "sion_forget"), cost),
                Is.EqualTo(new[] { smudge, sort, round }), "exhaustPick takes any discard card");

            var twin = Create();
            Commit(twin, "play", Play(recall, "now", null, new[] { smudge }));
            Assert.That((twin.State.Zones.Hand.Contains(round), twin.State.Zones.Discard.Contains(smudge)), Is.EqualTo((true, true)), "Core brings back its own choice for a curse pick");
            Commit(battle, "play", Play(recall, "now", null, new[] { sort }));
            Assert.That((battle.State.Zones.Hand.Contains(sort), battle.State.Zones.Discard.Contains(round)), Is.EqualTo((true, true)), "an offered pick is taken as asked");
        }

        // growPick: only growth definitions below growth 3 (a growth card at 3 is not offered; Core would grow another one), and nothing once
        // the battle's growth cap is reached, where Core skips the effect before choosing.
        [Test]
        public void GrowPickOffersGrowthCardsBelowThreeAndNothingAtTheBattleCap()
        {
            var content = ClientBattleContent.Load().Battle; var defs = Defs(content);
            var deck = new[] { Spec("yuri_dew"), Spec("yuri_sprout"), new BattleCardSpec { Id = "yuri_cocoon", Growth = 3 }, Spec("sera_round"),
                new BattleCardSpec { Id = "yuri_sprout_thorn", From = "yuri_sprout" } };
            Battle Create() => Begin(content, 35, new[] { "yuri", "sera", "nia" }, deck, new string[0]);
            var battle = Create(); var s = battle.State;
            Assert.That(s.Zones.Hand.Count, Is.EqualTo(5));
            string dew = Uid(s, "yuri_dew"), sprout = Uid(s, "yuri_sprout"), cocoon = Uid(s, "yuri_cocoon");
            var request = Request(content, "yuri_dew"); var rule = Rule(content, "yuri_dew");
            Func<string, bool, int> cost = (u, without) => without ? battle.CostOfWithoutNextFree(u, "now") : battle.CostOf(u, "now");
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(s, dew, request, new string[0], defs, rule, cost), Is.EqualTo(new[] { sprout }));
            Assert.That(Offer(s, dew, request, defs), Does.Contain(cocoon), "the growth-definition list would offer the card already at growth 3");
            var twin = Create();
            Commit(twin, "play", Play(dew, "now", null, new[] { cocoon }));
            Assert.That((twin.State.Cards[sprout].Growth, twin.State.Cards[cocoon].Growth), Is.EqualTo((1, 3)), "Core grows another card for a growth-3 pick");
            Commit(battle, "play", Play(dew, "now", null, new[] { sprout }));
            Assert.That(battle.State.Cards[sprout].Growth, Is.EqualTo(1), "an offered pick is taken as asked");

            var cap = Begin(content, 36, new[] { "yuri", "sera", "nia" },
                Deck("yuri_dew", "yuri_dew", "yuri_dew", "wc_yuri", "yuri_root", "yuri_sprout", "sera_round"), new[] { "quick_hands" });
            var c = cap.State;
            Assert.That(c.Zones.Hand.Count, Is.EqualTo(7), "precondition: quick_hands draws the whole seven-card deck");
            var dews = c.Zones.Hand.Where(u => c.Cards[u].Id == "yuri_dew").ToList(); string root = Uid(c, "yuri_root"), sprout2 = Uid(c, "yuri_sprout"), lamp = Uid(c, "wc_yuri");
            Commit(cap, "play", Play(dews[0], "now", null, new[] { root }));
            Commit(cap, "play", Play(dews[1], "now", null, new[] { root }));
            Commit(cap, "play", Play(dews[2], "now", null, new[] { sprout2 }));
            c = cap.State;
            Assert.That((c.Cards[root].Growth, c.Cards[sprout2].Growth, c.GrowthCount, c.GrowthCap), Is.EqualTo((2, 1, 3, 3)), "precondition: the battle's growth cap is reached");
            var lampRule = Rule(content, "wc_yuri");
            Assert.That(MemoryForgeExpeditionClient.EffectEligible(c, lampRule, sprout2, defs, (u, without) => without ? cap.CostOfWithoutNextFree(u, "now") : cap.CostOf(u, "now")), Is.True, "the card itself still qualifies");
            Assert.That(MemoryForgeExpeditionClient.PickBlocked(c, lampRule), Is.EqualTo(MemoryForgeExpeditionClient.PickGrowthCap));
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(c, lamp, Request(content, "wc_yuri"), new string[0], defs, lampRule, (u, without) => without ? cap.CostOfWithoutNextFree(u, "now") : cap.CostOf(u, "now")), Is.Empty);
            Commit(cap, "play", Play(lamp, "now", null, new[] { sprout2 }));
            Assert.That((cap.State.Cards[sprout2].Growth, cap.State.GrowthCount), Is.EqualTo((1, 3)), "Core skips the growth: the pick would not be consumed");
        }

        // copyPick: a growth instance includes an evolution of a growth root it came from (yuri_sprout_thorn from yuri_sprout), which the
        // growth-definition list missed; a non-growth card is not offered (Core would copy its own first growth instance). The copy is
        // free, so Core's actual cost keeps it out of costdown while the original stays in.
        [Test]
        public void CopyPickOffersEvolvedGrowthInstancesAndItsFreeCopyLeavesCostdown()
        {
            var content = ClientBattleContent.Load().Battle; var defs = Defs(content);
            Assert.That((defs["yuri_sprout_thorn"].Growth == true, defs["yuri_sprout"].Evolutions!.Contains("yuri_sprout_thorn")), Is.EqualTo((false, true)),
                "precondition: the evolution is not a growth definition, its root is");
            var deck = new[] { Spec("yuri_metamorph"), new BattleCardSpec { Id = "yuri_sprout_thorn", From = "yuri_sprout" }, new BattleCardSpec { Id = "yuri_cocoon", Growth = 3 },
                Spec("sera_round"), Spec("com_slash") };
            Battle Create() => Begin(content, 37, new[] { "yuri", "sera", "nia" }, deck, new string[0]);
            var battle = Create(); var s = battle.State;
            Assert.That(s.Zones.Hand.Count, Is.EqualTo(5));
            string meta = Uid(s, "yuri_metamorph"), thorn = Uid(s, "yuri_sprout_thorn"), cocoon = Uid(s, "yuri_cocoon"), round = Uid(s, "sera_round");
            var request = Request(content, "yuri_metamorph"); var rule = Rule(content, "yuri_metamorph");
            Func<string, bool, int> cost = (u, without) => without ? battle.CostOfWithoutNextFree(u, "now") : battle.CostOf(u, "now");
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(s, meta, request, new string[0], defs, rule, cost), Is.EqualTo(s.Zones.Hand.Where(u => u == thorn || u == cocoon).ToList()));
            Assert.That(Offer(s, meta, request, defs), Does.Not.Contain(thorn), "the growth-definition list missed the evolved instance");

            var twin = Create();
            var before = new HashSet<string>(twin.State.Zones.Hand);
            Commit(twin, "play", Play(meta, "now", null, new[] { round }));
            var copied = twin.State.Zones.Hand.Where(u => !before.Contains(u)).Select(u => twin.State.Cards[u].Id).ToList();
            Assert.That(copied.Count, Is.EqualTo(1));
            Assert.That(new[] { "yuri_sprout_thorn", "yuri_cocoon" }, Does.Contain(copied[0]), "Core copies its own growth instance for a non-growth pick");

            var hand = new HashSet<string>(s.Zones.Hand);
            Commit(battle, "play", Play(meta, "now", null, new[] { thorn }));
            var after = battle.State;
            string copy = after.Zones.Hand.Single(u => !hand.Contains(u));
            Assert.That((after.Cards[copy].Id, after.Cards[copy].Free), Is.EqualTo(("yuri_sprout_thorn", true)), "an offered evolved pick is copied as asked, free");
            var costdown = Rule(content, "nia_overclock");
            Assert.That(battle.CostOf(copy, "now"), Is.EqualTo(0));
            Assert.That(battle.CostOfWithoutNextFree(copy, "now"), Is.EqualTo(0), "instance.Free remains free without a global charge");
            Assert.That(MemoryForgeExpeditionClient.EffectEligible(after, costdown, copy, defs, (u, without) => without ? battle.CostOfWithoutNextFree(u, "now") : battle.CostOf(u, "now")), Is.False, "the free copy costs 0");
            Assert.That(MemoryForgeExpeditionClient.EffectEligible(after, costdown, thorn, defs, (u, without) => without ? battle.CostOfWithoutNextFree(u, "now") : battle.CostOf(u, "now")), Is.True, "its original still costs 1");
        }

        // Independent Codex verification of the Claude-authored shared arithmetic. Commands create every live modifier; no state restoration.
        [Test]
        public void PricesRetainPartsModesCostModsAndZeroOneTwoFreeChargesWithoutMutation()
        {
            var data = ClientBattleContent.Load().Battle;
            var b = Begin(data, 38, new[] { "rien", "nia", "sera" }, new[] { Spec("nia_overclock"), Spec("com_slash"),
                new BattleCardSpec { Id = "sera_round", Circuit = "c_lite" }, Spec("nia_tune"), Spec("sera_timed") }, new[] { "empty_clock" }, "rien");
            var ids = new[] { "nia_overclock", "com_slash", "sera_round", "nia_tune", "sera_timed" }.Select(id => Uid(b.State, id)).ToArray();
            void Check(int free, int[] now, int[] reserved)
            {
                Assert.That(b.State.FreeNext, Is.EqualTo(free)); string before = Fingerprint(b);
                foreach (var mode in new[] { "now", "next", "after" })
                    for (int i = 0; i < ids.Length; i++)
                    {
                        int literal = mode == "now" ? now[i] : reserved[i];
                        Assert.That(b.CostOfWithoutNextFree(ids[i], mode), Is.EqualTo(literal), "suppressed " + mode + ":" + ids[i]);
                        Assert.That(b.CostOf(ids[i], mode), Is.EqualTo(free > 0 ? 0 : literal), "existing query " + mode + ":" + ids[i]);
                    }
                Assert.That(Assert.Throws<RuleException>(() => b.CostOf("absent"))!.Code, Is.EqualTo("UNKNOWN_CARD_INSTANCE"));
                Assert.That(Assert.Throws<RuleException>(() => b.CostOfWithoutNextFree("absent"))!.Code, Is.EqualTo("UNKNOWN_CARD_INSTANCE"));
                Assert.That(Fingerprint(b), Is.EqualTo(before), "all queries preserve full state/log/events/RNG and every receipt");
            }
            // RULES_NOW_PLAY (2026-10-19): empty_clock takes 1 off the battle's first card in every mode (it was next/after only) and is
            // consumed by that first play even when FreeNext makes it free. Authored costs 1, 1, 1 + c_lite -1 = 0, 0, 2; floor 0.
            Check(0, new[] { 0, 0, 0, 0, 1 }, new[] { 0, 0, 0, 0, 1 });
            Commit(b, "ult", BattleArgument.Object(new[] { Pair("actor", "rien"), Pair("target", null) }));
            Check(2, new[] { 0, 0, 0, 0, 1 }, new[] { 0, 0, 0, 0, 1 });
            Commit(b, "play", Play(ids[2], "now", b.State.Enemies[0].Uid)); // the first card: empty_clock is used up
            Check(1, new[] { 1, 1, 0, 0, 2 }, new[] { 1, 1, 0, 0, 2 });
            Commit(b, "play", Play(ids[0], "now", null, new[] { ids[1] }));
            Assert.That(b.State.Cards[ids[1]].CostMod, Is.EqualTo(-1));
            Check(0, new[] { 1, 0, 0, 0, 2 }, new[] { 1, 0, 0, 0, 2 });
        }

        // Explicitly TEST-LOCAL definitions cover unauthored conditional/overflow branches. They never change Resources or live state.
        [Test]
        public void ConditionalCostsAndNumberErrorsUseTheSameCalculation()
        {
            var data = ClientBattleContent.Load().Battle;
            var def = data.Cards.Single(c => c.Id == "sera_round");
            def.CostIf = new BattleCardCostIfDef { Condition = new EffectConditionDef { Condition = "modded" }, Delta = 2 };
            var b = Begin(data, 39, new[] { "rien", "nia", "sera" }, new[] {
                new BattleCardSpec { Id = "sera_round", Circuit = "c_lite" }, Spec("com_guard") }, new[] { "empty_clock" }, "rien");
            string uid = Uid(b.State, "sera_round"), before = Fingerprint(b);
            // 1 + c_lite -1 + modded 2 = 2, minus the unused empty_clock 1 in every mode (RULES_NOW_PLAY: it was next/after only).
            Assert.That((b.CostOf(uid), b.CostOfWithoutNextFree(uid)), Is.EqualTo((1, 1)));
            Assert.That((b.CostOf(uid, "next"), b.CostOfWithoutNextFree(uid, "after")), Is.EqualTo((1, 1)));
            Assert.That(Fingerprint(b), Is.EqualTo(before));
            Commit(b, "ult", BattleArgument.Object(new[] { Pair("actor", "rien"), Pair("target", null) })); before = Fingerprint(b);
            Assert.That((b.CostOf(uid), b.CostOfWithoutNextFree(uid)), Is.EqualTo((0, 1)));
            Assert.That(Fingerprint(b), Is.EqualTo(before));

            var overflow = ClientBattleContent.Load().Battle;
            var max = overflow.Cards.Single(c => c.Id == "sera_round"); max.Cost = int.MaxValue;
            max.CostIf = new BattleCardCostIfDef { Condition = new EffectConditionDef { Condition = "modded" }, Delta = 2 };
            var x = Begin(overflow, 40, new[] { "rien", "nia", "sera" }, new[] { new BattleCardSpec { Id = "sera_round", Circuit = "c_lite" } }, new string[0], "rien");
            uid = Uid(x.State, "sera_round"); before = Fingerprint(x);
            Assert.That(Assert.Throws<RuleException>(() => x.CostOf(uid))!.Code, Is.EqualTo("BATTLE_NUMBER_OUT_OF_RANGE"));
            Assert.That(Assert.Throws<RuleException>(() => x.CostOfWithoutNextFree(uid))!.Code, Is.EqualTo("BATTLE_NUMBER_OUT_OF_RANGE"));
            Assert.That(Fingerprint(x), Is.EqualTo(before));
            Commit(x, "ult", BattleArgument.Object(new[] { Pair("actor", "rien"), Pair("target", null) })); before = Fingerprint(x);
            Assert.That(x.CostOf(uid), Is.EqualTo(0), "existing global free masks range check at the same point");
            Assert.That(Assert.Throws<RuleException>(() => x.CostOfWithoutNextFree(uid))!.Code, Is.EqualTo("BATTLE_NUMBER_OUT_OF_RANGE"));
            Assert.That(Fingerprint(x), Is.EqualTo(before));
        }

        [Test]
        public void UnknownConditionalOrNonLeadingCostsRetainTheExistingManualPath()
        {
            var data = ClientBattleContent.Load().Battle; var defs = Defs(data);
            var b = Begin(data, 41, new[] { "nia", "sera", "harin" }, Deck("nia_overclock", "com_slash", "nia_tune"), new string[0]);
            var s = b.State; string source = Uid(s, "nia_overclock"), slash = Uid(s, "com_slash");
            var req = Request(data, "nia_overclock"); var leading = Rule(data, "nia_overclock");
            var legacy = Offer(s, source, req, defs); string before = Fingerprint(b);
            Func<string, bool, int> refusal = (u, without) => throw new RuleException("BATTLE_DEFINITION_NOT_IMPLEMENTED");
            Assert.That(MemoryForgeExpeditionClient.EffectEligible(s, leading, slash, defs, refusal), Is.Null);
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(s, source, req, new string[0], defs, leading, refusal), Is.EqualTo(legacy));
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(s, source, req, new string[0], defs, leading, null), Is.EqualTo(legacy));
            int calls = 0;
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(s, source, req, new string[0], defs, ("costdown", null, false),
                (u, without) => { calls++; return 0; }), Is.EqualTo(legacy));
            Assert.That(calls, Is.Zero, "non-leading effects cannot use a guessed pre-commit price");
            defs["com_slash"].CostIf = new BattleCardCostIfDef { Condition = new EffectConditionDef { Condition = "modded" }, Delta = 1 };
            Assert.That(MemoryForgeExpeditionClient.EffectEligible(s, leading, slash, defs, (u, without) => 0), Is.Null);
            Assert.That(MemoryForgeExpeditionClient.PickCandidates(s, source, req, new string[0], defs, leading, (u, without) => 0),
                Is.EqualTo(new[] { slash }), "unknown conditional target stays; known zero-cost tune is filtered");
            Assert.That(Fingerprint(b), Is.EqualTo(before));
        }

        static string Fingerprint(Battle battle)
        {
            var all = (System.Collections.IDictionary)typeof(Battle).GetField("receipts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(battle)!;
            var receipts = new SortedDictionary<string, object>();
            foreach (System.Collections.DictionaryEntry pair in all)
            {
                var receipt = pair.Value!; var type = receipt.GetType();
                receipts[(string)pair.Key] = new object[] { type.GetField("Identity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(receipt)!,
                    ((BattleResponse)type.GetField("Response", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(receipt)!).ToJsonGraph() };
            }
            return JsonConvert.SerializeObject(new { State = battle.State.ToJsonGraph(), Receipts = receipts });
        }

        // ───────── pure helpers ─────────
        static Dictionary<string, BattleCardDef> Defs(BattleData content) => content.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
        static BattleCardSpec Spec(string id) => new BattleCardSpec { Id = id };
        static BattleCardSpec[] Deck(params string[] ids) => ids.Select(Spec).ToArray();
        static (string Label, string From, int Count, bool GrowthOnly) Request(BattleData content, string id) =>
            MemoryForgeExpeditionClient.ChoiceRequests(content.Cards.Single(c => c.Id == id), "now", content).Single();
        static (string Op, string? Part, bool Leading) Rule(BattleData content, string id) => MemoryForgeExpeditionClient.ChoiceRules(content.Cards.Single(c => c.Id == id), "now").Single();
        // The zone list without a rule: what the old sheet offered.
        static List<string> Offer(BattleState b, string source, (string Label, string From, int Count, bool GrowthOnly) request, Dictionary<string, BattleCardDef> defs) =>
            MemoryForgeExpeditionClient.PickCandidates(b, source, request, new string[0], defs);
        static string Uid(BattleState b, string id) => b.Cards.Single(c => c.Value.Id == id).Key;
        static Battle Begin(BattleData content, uint seed, string[] party, BattleCardSpec[] deck, string[] relics, string? readyUltimate = null)
        {
            var setup = new BattleSetup { Hp = 243, MaxHp = 243, Seed = seed };
            setup.Party.AddRange(party.Select(id => new BattleActorSetup { CharId = id, Gauge = id == readyUltimate ? content.Rules.GaugeMax : 0 }));
            setup.Deck.AddRange(deck); setup.Relics.AddRange(relics); setup.Enemies.Add("e_cog");
            var battle = Battle.Create(content, setup); battle.Command(new BattleRequest("begin", 0, "begin"));
            Assert.That(battle.State.Phase, Is.EqualTo("PLAYER"));
            return battle;
        }
        static void Commit(Battle battle, string kind, BattleArgument? args = null) => battle.Command(new BattleRequest(kind + "-" + battle.State.V, battle.State.V, kind, args));
        static BattleArgument Play(string uid, string mode, string? target, IEnumerable<string> picks) => BattleArgument.Object(new[] { Pair("uid", uid), Pair("mode", mode), Pair("target", target),
            new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array(picks.Select(BattleArgument.String))) });
        static BattleArgument Play(string uid, string mode, string? target) => Play(uid, mode, target, new string[0]);
        static KeyValuePair<string, BattleArgument> Pair(string key, string? value) => new KeyValuePair<string, BattleArgument>(key, value == null ? BattleArgument.Null() : BattleArgument.String(value));
    }

}
