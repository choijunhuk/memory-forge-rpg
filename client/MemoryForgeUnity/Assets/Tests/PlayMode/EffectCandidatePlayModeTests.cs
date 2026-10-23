// P1-CARD-FLOW-EFFECT-CANDIDATES §14.3/§14.7: these tune regressions check that the sheet offers, accepts and revalidates exactly the
// current-zone cards the authored tune effect takes. Conditional costs and unsupported preludes keep their earlier manual path.
// Core resolution, fallback order, explicit auto and every output stay Core's.
// Client integration (seed 1234, the real reference party and exported content, an independent Create mirror, the complete Run graph with
// its log and battle log/events, and the whole session store): only old public client APIs and real EventSystem rays. Documented seams as in
// the earlier card-flow tests (no production hook): the private session store is read and the client's own parsed content is reached by
// reflection. The source's only rarity-C cards are curses, which the reference party does not hold at its first nia_tune, so ONE
// explicitly TEST-LOCAL fixture marks one current hand card's definition rarity C in the client's content and separately in the mirror's,
// restored in finally. It is not authored-game or balance evidence.
// Pure Core witnesses (public Battle.Create/Command over the real exported content, small decks drawn whole so hand membership does not
// depend on the shuffle): every covered predicate on real cards, real parts (c_lite), real relics (eye_of_rift's curse, quick_hands), a real
// ultimate (Rien's free cards) and real evolution provenance (yuri_sprout_thorn from yuri_sprout). Nothing is injected into any state.
// Not Android/device, server or final-art acceptance.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MemoryForge.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using CoreRun = MemoryForge.Core.Run;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    // Client integration regressions: only APIs the old client already had, so this class alone compiles and runs against the old Main
    // (the separate EffectCandidateCoreWitnessTests file uses the new candidate API).
    public partial class EffectCandidatePlayModeTests
    {
        const long Seed = 1234;
        const string Invalid = "PICK_INVALID";
        readonly List<GameObject> hosts = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator DestroyOwnedHosts()
        {
            foreach (var host in hosts) if (host != null) Object.Destroy(host);
            hosts.Clear();
            yield return null;
        }

        sealed class Session
        {
            public GameObject Host = null!;
            public MemoryForgeExpeditionClient Client = null!;
            public CoreRun Mirror = null!;
            public RunData Data = null!;
            public BattleResponse? Last;
        }
        // The nia_tune witness: the tune card, its target, and the definition id the fixture will make ineligible (the first other hand card's).
        sealed class Witness { public string? Tune, Enemy, Hidden; }
        // Everything a refused input must leave alone.
        sealed class Baseline { public string Run = "", Store = ""; public string? Request; public long Version; }

        // The seed is set on a disabled host before the component's real OnEnable start; nothing else is injected.
        IEnumerator Open(Session s)
        {
            var host = new GameObject("EffectCandidateTestHost"); hosts.Add(host);
            host.SetActive(false);
            var client = host.AddComponent<MemoryForgeExpeditionClient>(); client.Seed = Seed; client.Length = "short";
            host.SetActive(true);
            yield return null;
            s.Host = host; s.Client = client; s.Data = ClientRunContent.Load();
            s.Mirror = CoreRun.Create(s.Data, MemoryForgeExpeditionClient.PracticeSetup(Seed, "short", "practice-1-" + Seed, new Dictionary<string, object?>(), 0));
            AssertSame(s, "the view's Run must be exactly Create of the public PracticeSetup");
        }

        // Actual commands through the bounded driver until nia_tune is playable now against the first living enemy, with another hand card
        // of a different definition that the tune effect takes even after the fixture. With afterPlain the check is made right after a plain
        // immediate play, whose non-terminal display stays open for the caller.
        IEnumerator DriveToTune(Session s, Witness w, bool afterPlain)
        {
            var client = s.Client;
            for (int steps = 0; steps < 4000 && w.Tune == null; steps++)
            {
                var state = s.Mirror.State;
                if (state.Phase == "VICTORY" || state.Phase == "DEFEAT") break;
                if (state.Phase == "BATTLE" && state.Battle != null && state.Battle.Phase == "PLAYER")
                {
                    if (!afterPlain) { if (Find(s, w)) break; }
                    else
                    {
                        var b = state.Battle; string enemy = b.Enemies.First(e => e.Alive).Uid;
                        string? plain = b.Zones.Hand.FirstOrDefault(u => Plain(s, u, enemy));
                        if (plain != null)
                        {
                            PlayPlain(s, plain, enemy);
                            if (!client.ActionPresentationTerminal && s.Mirror.State.Battle != null && Find(s, w)) break;
                            FinishAction(client);
                            if (steps % 8 == 7) yield return null;
                            continue;
                        }
                    }
                }
                if (Step(s, state)) FinishAction(client);
                if (steps % 8 == 7) yield return null;
            }
            Assert.That(w.Tune, Is.Not.Null, "the reference kit's nia_tune with two other hand definitions must be reached within the bounded actual commands");
            Assert.That((client.PickerOpen, client.HasPreparedIntent, client.RecoveryRequired), Is.EqualTo((false, false, false)));
            Assert.That(client.HasActionPresentation, Is.EqualTo(afterPlain), afterPlain ? "the plain play's display is open" : "no display is open");
        }
        static bool Find(Session s, Witness w)
        {
            var b = s.Mirror.State.Battle!; string enemy = b.Enemies.First(e => e.Alive).Uid;
            string? tune = b.Zones.Hand.FirstOrDefault(u => b.Cards[u].Id == "nia_tune" && s.Mirror.BattleCheck(u, "now", enemy) == null);
            if (tune == null) return false;
            var others = b.Zones.Hand.Where(u => u != tune).ToList();
            if (others.Count < 2) return false;
            string hidden = b.Cards[others[0]].Id;
            if (hidden == "nia_tune" || !others.Any(u => b.Cards[u].Id != hidden && TuneTakes(s.Data, b, u))) return false;
            w.Tune = tune; w.Enemy = enemy; w.Hidden = hidden;
            return true;
        }
        // Independent copy of Core's tune(one) predicate for the "amp" part (Battle.Effects.cs tune): no amp yet and definition rarity not C.
        static bool TuneTakes(RunData data, BattleState b, string uid) =>
            !b.Cards[uid].Temp.Contains("amp") && data.Memory.Battle.Cards.Single(c => c.Id == b.Cards[uid].Id).Rarity != "C";
        // Seam, TEST-LOCAL fixture: the definition gets rarity C (the source's ineligible rarity, carried only by curses) in the client's own
        // parsed content and separately in the mirror's. The caller restores the returned originals in finally.
        static List<(BattleCardDef Def, string Rarity)> MarkRarityC(Session s, string id)
        {
            var defs = new[] { ClientData(s.Client), s.Data }.Select(d => d.Memory.Battle.Cards.Single(c => c.Id == id)).ToList();
            Assert.That(defs[0], Is.Not.SameAs(defs[1]), "the client and the mirror parse separate content");
            var kept = defs.Select(d => (d, d.Rarity)).ToList();
            foreach (var d in defs) d.Rarity = "C";
            return kept;
        }
        static void Restore(List<(BattleCardDef Def, string Rarity)> kept) { foreach (var (def, rarity) in kept) def.Rarity = rarity; }
        static void Prepare(Session s, Witness w)
        {
            s.Client.SelectEnemy(w.Enemy!); s.Client.SelectCard(w.Tune!); s.Client.SelectMode("now");
            Assert.That(new[] { s.Client.SelectedCard, s.Client.SelectedMode, s.Client.Target }, Is.EqualTo(new[] { w.Tune, "now", w.Enemy }));
        }

        // RED on the old sheet (its broad hand list offered and accepted the ineligible card; Core then tuned another one): the sheet's list,
        // its drawn buttons and its acceptance are exactly the hand cards the tune effect takes. Every ineligible card is refused without
        // adding, sending or changing the Run, AP, RNG, log, events or store. A real tap on an eligible card is exactly the original play with
        // that pick, and Core tuned exactly that card.
        [UnityTest]
        public IEnumerator TuneSheetOffersAndAcceptsOnlyCardsItsCoreEffectTakes()
        {
            var s = new Session(); yield return Open(s);
            var w = new Witness(); yield return DriveToTune(s, w, false);
            var client = s.Client; string tune = w.Tune!, enemy = w.Enemy!;
            var kept = MarkRarityC(s, w.Hidden!);
            try
            {
                var b = s.Mirror.State.Battle!;
                var others = b.Zones.Hand.Where(u => u != tune).ToList();
                var eligible = others.Where(u => TuneTakes(s.Data, b, u)).ToList();
                var ineligible = others.Except(eligible).ToList();
                Assert.That(ineligible, Is.Not.Empty, "the fixture made a current hand card ineligible");
                Assert.That(eligible, Is.Not.Empty, "another hand card stays eligible");
                Prepare(s, w);
                var start = Capture(s);
                yield return null; Canvas.ForceUpdateCanvases();
                Click(ButtonNamed(s.Host, "ConfirmCard"));
                Assert.That(client.PickerOpen, Is.True, "a choice card opens its sheet first");
                AssertPure(s, start, "opening the sheet");
                Assert.That(client.PickerOptions, Is.EqualTo(eligible), "the sheet offers exactly the hand cards the tune effect takes");
                yield return null; Canvas.ForceUpdateCanvases();
                Assert.That(ActiveView(s.Host).GetComponentsInChildren<Button>().Where(x => x.name.StartsWith("Pick:", StringComparison.Ordinal)).Select(x => x.name.Substring(5)),
                    Is.EqualTo(eligible), "the sheet draws exactly that list");
                foreach (string uid in ineligible)
                {
                    Assert.That(client.PickCard(uid), Is.False, "ineligible pick " + uid);
                    Assert.That(client.LastError, Is.EqualTo(Invalid), "ineligible pick " + uid);
                    Assert.That((client.PickerOpen, client.PickerPicks.Count), Is.EqualTo((true, 0)), "ineligible pick " + uid + " was added or sent");
                    AssertPure(s, start, "ineligible pick " + uid);
                }
                var temp = b.Cards.ToDictionary(c => c.Key, c => c.Value.Temp.Contains("amp"));
                string chosen = eligible[0];
                Click(ButtonNamed(s.Host, "Pick:" + chosen));
                MirrorOf(s, BattleCore(s, "play", Play(tune, "now", enemy, new[] { chosen })));
                Assert.That((client.LastError, client.PickerOpen), Is.EqualTo(((string?)null, false)));
                var after = s.Mirror.State.Battle!;
                Assert.That(after.Cards[chosen].Temp, Does.Contain("amp"), "Core tuned exactly the explicit pick");
                Assert.That(after.Cards.Where(c => !temp[c.Key] && c.Value.Temp.Contains("amp")).Select(c => c.Key), Is.EqualTo(new[] { chosen }), "and no other card");
                Assert.That((client.LastRequestId, client.SessionMemoryVersion, StorePrint(client)), Is.EqualTo((start.Request, start.Version, start.Store)), "no memory request or receipt");
            }
            finally { Restore(kept); }
        }

        // Explicit auto is unchanged: with ineligible cards hidden from the list, auto sends no picks and Core chooses by its own rule, exactly
        // as the mirror does; Core never takes a card the effect refuses.
        [UnityTest]
        public IEnumerator ExplicitAutoStillSendsCoresOwnChoiceAndNeverAHiddenCard()
        {
            var s = new Session(); yield return Open(s);
            var w = new Witness(); yield return DriveToTune(s, w, false);
            var client = s.Client; string tune = w.Tune!, enemy = w.Enemy!;
            var kept = MarkRarityC(s, w.Hidden!);
            try
            {
                var b = s.Mirror.State.Battle!;
                var eligible = b.Zones.Hand.Where(u => u != tune && TuneTakes(s.Data, b, u)).ToList();
                Prepare(s, w);
                var start = Capture(s);
                Assert.That(client.ConfirmCard(), Is.False);
                Assert.That(client.PickerOpen, Is.True);
                Assert.That(client.PickerOptions, Is.EqualTo(eligible), "the sheet lists only the eligible cards");
                AssertPure(s, start, "opening the sheet");
                var temp = b.Cards.ToDictionary(c => c.Key, c => c.Value.Temp.Contains("amp"));
                Assert.That(Both(s, client.AutoPick, BattleCore(s, "play", Play(tune, "now", enemy))), Is.True, "auto is exactly one play with no picks");
                var tuned = s.Mirror.State.Battle!.Cards.Where(c => !temp[c.Key] && c.Value.Temp.Contains("amp")).Select(c => c.Key).ToList();
                Assert.That(tuned.Count, Is.EqualTo(1), "Core tuned one card by its own rule");
                Assert.That(eligible, Does.Contain(tuned[0]), "Core's own choice is one of the cards the sheet offered");
            }
            finally { Restore(kept); }
        }

        // RED on the old sheet: confirmed during a play's display, the same sheet offers only the eligible cards, refuses the ineligible
        // ones, and its final eligible pick becomes the one unsent intent with that frozen pick; the dispatch sends exactly that play.
        [UnityTest]
        public IEnumerator APreparedTunePickUsesTheSameEligibleList()
        {
            var s = new Session(); yield return Open(s);
            var w = new Witness(); yield return DriveToTune(s, w, true);
            var client = s.Client; string tune = w.Tune!, enemy = w.Enemy!;
            var kept = MarkRarityC(s, w.Hidden!);
            try
            {
                var b = s.Mirror.State.Battle!;
                var others = b.Zones.Hand.Where(u => u != tune).ToList();
                var eligible = others.Where(u => TuneTakes(s.Data, b, u)).ToList();
                var ineligible = others.Except(eligible).ToList();
                Assert.That((eligible.Count != 0, ineligible.Count != 0), Is.EqualTo((true, true)));
                Prepare(s, w);
                var start = Capture(s);
                Assert.That(client.ConfirmCard(), Is.False);
                Assert.That((client.PickerOpen, client.HasActionPresentation, client.HasPreparedIntent), Is.EqualTo((true, true, false)), "the sheet opens during the display");
                Assert.That(client.PickerOptions, Is.EqualTo(eligible), "the prepared path offers exactly the same eligible cards");
                foreach (string uid in ineligible)
                {
                    Assert.That(client.PickCard(uid), Is.False, "ineligible pick " + uid);
                    Assert.That((client.LastError, client.PickerOpen, client.HasPreparedIntent), Is.EqualTo(((string?)Invalid, true, false)), "ineligible pick " + uid);
                    AssertPure(s, start, "ineligible pick " + uid);
                }
                string chosen = eligible[0];
                Assert.That(client.PickCard(chosen), Is.False, "the final pick during the display commits nothing");
                Assert.That((client.PickerOpen, client.HasPreparedIntent), Is.EqualTo((false, true)));
                Assert.That(client.PreparedPicks, Is.EqualTo(new[] { chosen }), "the frozen pick is the eligible one");
                AssertPure(s, start, "preparing");
                if (client.HasActionPresentation) Assert.That(client.SkipActionPresentation(), Is.True);
                Assert.That(client.HasPreparedIntent, Is.True);
                yield return null; // the dispatch revalidates the frozen pick on the same list and sends it
                MirrorOf(s, BattleCore(s, "play", Play(tune, "now", enemy, new[] { chosen })));
                Assert.That((client.HasPreparedIntent, client.LastError), Is.EqualTo((false, (string?)null)));
                Assert.That(s.Mirror.State.Battle!.Cards[chosen].Temp, Does.Contain("amp"), "Core tuned exactly the frozen pick");
            }
            finally { Restore(kept); }
        }







        // ───────── drivers: one public command per call, mirrored on the Core Run ─────────
        static bool Plain(Session s, string uid, string enemy) => s.Mirror.BattleAllowedModes(uid).Contains("now") && s.Mirror.BattleCheck(uid, "now", enemy) == null
            && MemoryForgeExpeditionClient.ChoiceRequests(s.Mirror.BattleEffectiveCard(uid), "now", s.Data.Memory.Battle).Count == 0;
        static void PlayPlain(Session s, string card, string enemy)
        {
            s.Client.SelectEnemy(enemy); s.Client.SelectCard(card); s.Client.SelectMode("now");
            Assert.That(Both(s, s.Client.ConfirmCard, BattleCore(s, "play", Play(card, "now", enemy))), Is.True);
            Assert.That(s.Client.HasActionPresentation, Is.True, "an actual play opens its display");
        }
        static Action<CoreRun> BattleCore(Session s, string kind, BattleArgument? args) => r =>
        {
            var response = r.BattleCommand(kind, args);
            if (ReferenceEquals(r, s.Mirror)) s.Last = response;
        };
        static bool Both(Session s, Func<bool> view, Action<CoreRun> core)
        {
            bool ok = view(); string? code = null;
            try { core(s.Mirror); }
            catch (RuleException error) { code = error.Code; }
            Assert.That(ok, Is.EqualTo(code == null), "view and Core disagree: view " + s.Client.LastError + " / Core " + code);
            if (!ok) Assert.That(s.Client.LastError, Is.EqualTo(code));
            AssertSame(s, "the view must issue exactly the actual Core command");
            return ok;
        }
        static void MirrorOf(Session s, Action<CoreRun> core)
        {
            core(s.Mirror);
            AssertSame(s, "the view must have sent exactly the actual Core command");
        }
        static void Dismiss(MemoryForgeExpeditionClient client)
        {
            Assert.That(client.HasTurnSummary, Is.True, "an actual end opens the turn summary");
            string before = Fingerprint(client.Snapshot!);
            Assert.That(client.SkipTurnSummary() && client.ContinueTurnSummary(), Is.True);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "skip and continue never reach the Run");
        }
        static void FinishAction(MemoryForgeExpeditionClient client)
        {
            Assert.That(client.HasActionPresentation, Is.True, "an actual play or ultimate opens its display");
            string before = Fingerprint(client.Snapshot!);
            Assert.That(client.SkipActionPresentation(), Is.True);
            Assert.That(client.SkipActionPresentation(), Is.False, "a repeated skip is inert");
            Assert.That((client.HasActionPresentation, Fingerprint(client.Snapshot!)), Is.EqualTo((false, before)), "finishing the display never reaches the Run");
        }
        // The existing bounded driver order (as in the action-presentation tests). Returns true when the command sent was a battle's last
        // action, whose display is left open for the caller.
        static bool Step(Session s, RunState state)
        {
            var client = s.Client; var p = state.Pending; var events = s.Data.Events;
            switch (state.Phase)
            {
                case "MAP": { string id = s.Mirror.Available()[0]; Both(s, () => client.EnterNode(id), r => r.Enter(id)); return false; }
                case "BATTLE": return BattleStep(s, state.Battle!);
                case "REWARD":
                    {
                        var evolutions = ListOf(Get(p, "evolutions")); var cards = ListOf(Get(p, "cards")); var relics = ListOf(Get(p, "relic"));
                        if (evolutions.Count != 0)
                        {
                            string key = (string)Get(evolutions[0], "key")!, branch = (string)ListOf(Get(evolutions[0], "options"))[0]!;
                            if (Both(s, () => client.Evolve(key, branch), r => r.Evolve(key, branch))) return false;
                        }
                        else if (cards.Count != 0) { string id = (string)cards[0]!; if (Both(s, () => client.TakeCard(id), r => r.TakeCard(id))) return false; }
                        else if (relics.Count != 0) { string id = (string)relics[0]!; if (Both(s, () => client.TakeRelic(id), r => r.TakeRelic(id))) return false; }
                        else if (Get(p, "part") is string) { if (Both(s, client.TakePart, r => r.TakePart())) return false; }
                        Both(s, client.Proceed, r => r.Proceed());
                        return false;
                    }
                case "REST":
                    if (Integer(Get(p, "uses")) > 0)
                    {
                        var plain = state.Deck.FirstOrDefault(c => !c.Weapon && !Truthy(Get(c.ToJsonGraph(), "plus")));
                        if (state.Hp * 10 < state.MaxHp * 6 || plain == null) { if (Both(s, client.RestHeal, r => r.Rest("heal"))) return false; }
                        else { string key = plain.Key!; if (Both(s, () => client.RestUpgrade(key), r => r.Rest("upgrade", key))) return false; }
                    }
                    Both(s, client.Proceed, r => r.Proceed());
                    return false;
                case "FORGE":
                    {
                        var offers = Get(p, "offers"); var parts = ListOf(Get(offers, "parts"));
                        if (parts.Count != 0) { string id = (string)parts[0]!; if (Both(s, () => client.ForgeApply(id), r => r.ForgeApply(id))) return false; }
                        else if (offers == null && Integer(Get(p, "uses")) > 0)
                        {
                            string key = state.Deck.First(c => !c.Weapon).Key!;
                            if (Both(s, () => client.ForgeOffer(key), r => r.ForgeOffer(key))) return false;
                        }
                        Both(s, client.Proceed, r => r.Proceed());
                        return false;
                    }
                case "SHOP":
                    {
                        var cards = ListOf(Get(p, "cards"));
                        bool bought = cards.Concat(ListOf(Get(p, "parts"))).Concat(ListOf(Get(p, "relics"))).Any(x => Truthy(Get(x, "sold")));
                        var item = cards.FirstOrDefault(x => !Truthy(Get(x, "sold")) && Integer(Get(x, "price")) <= state.Coins);
                        if (!bought && item != null) { string id = (string)Get(item, "id")!; if (Both(s, () => client.Buy("cards", id), r => r.Buy("cards", id))) return false; }
                        Both(s, client.Proceed, r => r.Proceed());
                        return false;
                    }
                case "EVENT":
                    {
                        if (Truthy(Get(p, "done"))) { Both(s, client.Proceed, r => r.Proceed()); return false; }
                        string eventId = (string)Get(p, "id")!; var definition = events.Last(e => e.Id == eventId);
                        for (int i = 0; i < definition.Choices.Count; i++)
                        {
                            var fx = definition.Choices[i].Effects; int index = i;
                            if (definition.Choices[i].RequiredCoins.GetValueOrDefault() > state.Coins) continue;
                            int count = fx.CardCopy.GetValueOrDefault() != 0 ? 1 : fx.CardRemove.GetValueOrDefault();
                            var keys = count > 0 ? state.Deck.Where(c => !c.Weapon).Select(c => c.Key!).Take(count).ToList() : null;
                            if (Both(s, () => client.ChooseEvent(index, keys), r => r.EventChoose(index, keys?.Cast<object?>().ToList()))) return false;
                        }
                        Assert.Fail("no choice of event " + eventId + " was accepted");
                        return false;
                    }
                case "ARCHIVE": Both(s, () => client.Archive("return"), r => r.Archive("return")); return false;
                default: Assert.Fail("unexpected expedition phase " + state.Phase); return false;
            }
        }
        static bool BattleStep(Session s, BattleState b)
        {
            var client = s.Client; var mirror = s.Mirror; var content = s.Data.Memory.Battle;
            Assert.That(b.Phase, Is.EqualTo("PLAYER"), "a live Run battle waits for the player");
            string enemy = b.Enemies.First(e => e.Alive).Uid;
            bool Legal(string uid) => mirror.BattleAllowedModes(uid).Contains("now") && mirror.BattleCheck(uid, "now", enemy) == null;
            bool Picks(string uid) => MemoryForgeExpeditionClient.ChoiceRequests(mirror.BattleEffectiveCard(uid), "now", content).Count != 0;
            string? card = b.Zones.Hand.FirstOrDefault(uid => Legal(uid) && !Picks(uid)) ?? b.Zones.Hand.FirstOrDefault(uid => Legal(uid));
            if (card != null)
            {
                client.SelectEnemy(enemy); client.SelectCard(card); client.SelectMode("now");
                if (Picks(card))
                {
                    Assert.That(client.ConfirmCard(), Is.False);
                    Assert.That(client.PickerOpen, Is.True);
                    if (Both(s, client.AutoPick, BattleCore(s, "play", Play(card, "now", enemy)))) return Played(s);
                }
                else if (Both(s, client.ConfirmCard, BattleCore(s, "play", Play(card, "now", enemy)))) return Played(s);
            }
            var ready = b.Actors.FirstOrDefault(a => a.Gauge >= content.Rules.GaugeMax);
            if (ready != null)
            {
                client.SelectEnemy(enemy);
                var args = BattleArgument.Object(new[] { Pair("actor", ready.Id), Pair("target", enemy) });
                if (Both(s, () => client.UseUltimate(ready.Id), BattleCore(s, "ult", args))) return Played(s);
            }
            if (Both(s, client.EndTurn, BattleCore(s, "end", null))) Dismiss(client);
            return false;
        }
        static bool Played(Session s)
        {
            var client = s.Client;
            Assert.That(client.HasActionPresentation, Is.True, "an actual play or ultimate opens its display");
            Assert.That(client.ActionPresentationTerminal, Is.EqualTo(s.Mirror.State.Phase != "BATTLE"), "terminal exactly when the battle has ended");
            if (client.ActionPresentationTerminal) return true;
            FinishAction(client);
            return false;
        }

        // ───────── seams, purity and small copies of the expedition tests' helpers ─────────
        static RunData ClientData(MemoryForgeExpeditionClient client) =>
            (RunData)typeof(MemoryForgeExpeditionClient).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
        static string StorePrint(MemoryForgeExpeditionClient client) =>
            JToken.FromObject(typeof(MemoryForgeExpeditionClient).GetField("store", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!).ToString(Formatting.None);
        static Baseline Capture(Session s) => new Baseline { Run = Fingerprint(s.Client.Snapshot!), Store = StorePrint(s.Client), Request = s.Client.LastRequestId, Version = s.Client.SessionMemoryVersion };
        static void AssertPure(Session s, Baseline b, string what)
        {
            Assert.That(Fingerprint(s.Client.Snapshot!), Is.EqualTo(b.Run), what + " reached the Run (state, AP, RNG, log or events)");
            Assert.That(StorePrint(s.Client), Is.EqualTo(b.Store), what + " changed the session store or its receipts");
            Assert.That((s.Client.LastRequestId, s.Client.SessionMemoryVersion), Is.EqualTo((b.Request, b.Version)), what + " made a memory request");
            AssertSame(s, what + ": the mirror received nothing either");
        }
        static void AssertSame(Session s, string message) => Assert.That(Fingerprint(s.Client.Snapshot!), Is.EqualTo(Fingerprint(s.Mirror.State)), message);
        // CanonicalState omits "log"/"ev", so the Run log and the battle log/events are hashed separately for a complete comparison.
        static string Fingerprint(RunState state)
        {
            var graph = state.ToJsonGraph();
            string battle = graph["battle"] is Dictionary<string, object?> b ? CanonicalState.Hash(b["log"]) + CanonicalState.Hash(b["ev"]) : "-";
            return CanonicalState.Hash(new Dictionary<string, object?> { ["run"] = graph, ["memory"] = null }) + "/" + CanonicalState.Hash(graph["log"]) + "/" + battle;
        }
        static BattleArgument Play(string uid, string mode, string? target) => Play(uid, mode, target, new string[0]);
        static BattleArgument Play(string uid, string mode, string? target, IEnumerable<string> picks) => BattleArgument.Object(new[] { Pair("uid", uid), Pair("mode", mode), Pair("target", target),
            new KeyValuePair<string, BattleArgument>("picks", BattleArgument.Array(picks.Select(BattleArgument.String))) });
        static KeyValuePair<string, BattleArgument> Pair(string key, string? value) => new KeyValuePair<string, BattleArgument>(key, value == null ? BattleArgument.Null() : BattleArgument.String(value));
        static object? Get(object? node, string key) => node is IDictionary<string, object?> map && map.TryGetValue(key, out var value) ? value : null;
        static List<object?> ListOf(object? value) => value is IList list ? list.Cast<object?>().ToList() : new List<object?>();
        static bool Truthy(object? value) => value is bool flag ? flag : value != null;
        static long Integer(object? value) => value is int n ? n : value is long l ? l : 0;
        static Vector2 Center(Button button)
        {
            var rect = button.GetComponent<RectTransform>();
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }
        static GameObject TopHit(Vector2 point)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left }, hits);
            Assert.That(hits, Is.Not.Empty, "something must take a tap at " + point);
            return hits[0].gameObject;
        }
        // A real tap: a click on the top raycast hit at the button's own center, as the input module delivers it.
        static void Click(Button button)
        {
            Vector2 point = Center(button);
            Assert.That(TopHit(point).GetComponentInParent<Button>(), Is.SameAs(button), button.name + " must be the top hit at its own center");
            ExecuteEvents.ExecuteHierarchy(TopHit(point), new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        }
        static Button ButtonNamed(GameObject host, string name) => ActiveView(host).GetComponentsInChildren<Button>().Single(x => x.name == name);
        static GameObject ActiveView(GameObject host)
        {
            var active = new List<GameObject>();
            foreach (Transform child in host.transform) if (child.name == MemoryForgeExpeditionClient.OwnedCanvas && child.gameObject.activeInHierarchy) active.Add(child.gameObject);
            Assert.That(active.Count, Is.EqualTo(1), "expected exactly one active owned expedition view");
            return active[0];
        }
    }
}
