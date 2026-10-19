// P1-UNITY-BATTLE-VIEW-REVIEW: opposite-author acceptance for the §14.3 battle-practice view.
// Uses the exported Resources content and the real controller/Core; no hand-made states, forced gauges or injected expectations.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MemoryForge.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MemoryForge.Client.Tests
{
    public class BattleClientPlayModeTests
    {
        const string ContentResource = "MemoryForge/battle-content";
        const string OwnedCanvas = "MemoryForgeBattleView";
        static readonly string[] PresetParty = { "sera", "harin", "nia" };
        // Fixed, deterministic seed list: the controller default first, then a bounded fallback range.
        static readonly uint[] Seeds = new uint[] { 1234 }.Concat(Enumerable.Range(1, 48).Select(i => (uint)i)).ToArray();
        readonly List<GameObject> hosts = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator DestroyOwnedHosts()
        {
            foreach (var host in hosts) if (host != null) Object.Destroy(host);
            hosts.Clear();
            yield return null;
        }

        [Test]
        public void ExportedContentLoadsEveryOriginalDefinitionAndRoundTripsExactly()
        {
            var asset = Resources.Load<TextAsset>(ContentResource);
            Assert.That(asset, Is.Not.Null, "Resources/" + ContentResource + ".json is missing; export it with tools/UnityContentExport");
            string json = asset!.text;
            var raw = (JObject)ParseStrict(json);
            var content = ClientBattleContent.FromJson(json);
            var loaded = ClientBattleContent.Load();

            // Original 0.7.0 content, carried unchanged by design v0.8.1.
            var b = content.Battle; var c = content.SourceCounts;
            var counts = new[] { b.Characters.Count, b.Cards.Count, b.Weapons.Count, b.Parts.Count, b.Relics.Count, b.Enemies.Count, content.Encounters.Count };
            Assert.That(counts, Is.EqualTo(new[] { 10, 173, 22, 16, 32, 20, 2 }));
            Assert.That(new[] { c.Characters, c.Cards, c.Weapons, c.Parts, c.Relics, c.Enemies, c.Zones }, Is.EqualTo(counts));

            var serializer = JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include, TypeNameHandling = TypeNameHandling.None });
            Assert.That(raw["Battle"], Is.Not.Null);
            string? battleDifference = Difference(raw["Battle"]!, JToken.FromObject(content.Battle, serializer), "$.Battle");
            Assert.That(battleDifference, Is.Null, "typed Battle definitions changed in the Newtonsoft round trip at " + battleDifference);
            var roundTrip = JToken.FromObject(content, serializer);
            string? envelopeDifference = Difference(raw, roundTrip, "$");
            Assert.That(envelopeDifference, Is.Null, "the exported envelope changed in the Newtonsoft round trip at " + envelopeDifference);
            string? loadDifference = Difference(roundTrip, JToken.FromObject(loaded, serializer), "$");
            Assert.That(loadDifference, Is.Null, "Resources Load() differs from FromJson() at " + loadDifference);

            if (Application.isEditor) CompareWithOriginalSource(content);
        }

        [UnityTest]
        public IEnumerator DefaultPracticeOpensTheReferenceBattleWithReadableCriticalLabels()
        {
            Assert.That(Resources.Load<TextAsset>(ContentResource), Is.Not.Null, "battle content resource is missing");
            var host = new GameObject("BattleClientTestHost"); hosts.Add(host);
            var client = host.AddComponent<MemoryForgeBattleClient>(); // default OnEnable start
            yield return null;

            var content = ClientBattleContent.Load();
            var rules = content.Battle.Rules;
            Assert.That(client.LastError, Is.Null);
            var s = client.Snapshot;
            Assert.That(s, Is.Not.Null, "OnEnable did not start the practice battle");
            Assert.That(s!.Phase, Is.EqualTo("PLAYER"));
            Assert.That(s.V, Is.EqualTo(1));
            Assert.That(s.Turn, Is.EqualTo(1));
            Assert.That(s.Hp, Is.EqualTo(243));
            Assert.That(s.MaxHp, Is.EqualTo(243));
            Assert.That(s.Ap, Is.EqualTo(4).And.EqualTo(rules.ApPerTurn));
            Assert.That(s.Zones.Hand.Count, Is.EqualTo(5).And.EqualTo(rules.HandDraw));
            Assert.That(client.SelectedCard, Is.Not.Null);
            Assert.That(s.Actors.Select(a => a.Id), Is.EqualTo(PresetParty));
            Assert.That(s.Actors.Select(a => a.Name), Is.EqualTo(PresetParty.Select(id => content.Battle.Characters.Single(ch => ch.Id == id).Name)));

            string firstZone = content.Encounters.Keys.OrderBy(k => k, StringComparer.Ordinal).First();
            Assert.That(s.Enemies.Select(e => e.Id), Is.EqualTo(content.Encounters[firstZone].Normal[0]));
            foreach (var enemy in s.Enemies)
            {
                var definition = content.Battle.Enemies.Single(e => e.Id == enemy.Id);
                Assert.That(new object[] { enemy.Name, enemy.Hp, enemy.MaxHp, enemy.Brk, enemy.Alive },
                    Is.EqualTo(new object[] { definition.Name, definition.Hp, definition.Hp, definition.Brk, true }), enemy.Uid);
            }
            int kit = PresetParty.Sum(id => content.Battle.Cards.Count(card => card.Owner == id && card.Start == true) + 2);
            Assert.That(s.Cards.Count, Is.EqualTo(kit));
            Assert.That(s.Zones.Draw.Count + s.Zones.Hand.Count, Is.EqualTo(kit));
            foreach (string id in PresetParty)
            {
                Assert.That(s.Cards.Values.Count(card => card.Id == content.StartingCommon[id] && card.Assigned == id), Is.EqualTo(1), id + " starting common");
                Assert.That(s.Cards.Values.Count(card => card.Owner == "weapon" && card.Wielder == id), Is.EqualTo(1), id + " basic weapon card");
            }

            var view = SingleView(host);
            Canvas.ForceUpdateCanvases();
            AssertReadable(view, t => t == "기억 공방", "title");
            AssertReadable(view, t => t.StartsWith("전투 연습", StringComparison.Ordinal), "practice header");
            AssertReadable(view, t => t.StartsWith("파티 체력", StringComparison.Ordinal) && t.Contains(s.Hp + " / " + s.MaxHp), "party HP");
            AssertReadable(view, t => t.StartsWith(s.Turn + "턴", StringComparison.Ordinal), "turn status");
            AssertReadable(view, t => t == "행동력  " + s.Ap, "AP"); // controller format, MemoryForgeBattleClient.cs:156
            AssertReadable(view, t => t.StartsWith("턴 종료", StringComparison.Ordinal), "end turn");
            foreach (var actor in s.Actors) AssertReadable(view, t => t == actor.Name, "actor " + actor.Id);
            foreach (var group in s.Enemies.GroupBy(e => e.Name)) AssertReadable(view, t => t == group.Key, "enemy " + group.Key, group.Count());
            foreach (var group in s.Zones.Hand.Select(uid => content.Battle.Cards.Single(card => card.Id == s.Cards[uid].Id).Name).GroupBy(n => n))
                AssertReadable(view, t => t == group.Key, "hand card " + group.Key, group.Count());
        }

        [UnityTest]
        public IEnumerator PreviewLeavesTheBattleUntouchedAndPlayCommitsThroughTheCore()
        {
            var opening = new Opening();
            yield return OpenWithCard("now", (running, uid) => DirectOnly(running.EffectiveCard(uid)), opening);
            Assert.That(opening.Failure, Is.Null, opening.Failure ?? "");
            var host = opening.Host!; var client = opening.Client!; var running = opening.Running!; string uid = opening.Card;

            client.SelectEnemy(opening.Target);
            client.SelectCard(uid);
            Assert.That(client.SelectedCard, Is.EqualTo(uid));
            var before = client.Snapshot!;
            string beforePrint = Fingerprint(before);
            int cost = running.CostOf(uid, "now");
            var definition = running.EffectiveCard(uid);

            string preview = client.Preview(uid, "now");
            Assert.That(preview, Is.Not.Empty);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(beforePrint), "Preview changed the running battle (state, RNG, log or events)");
            yield return null;
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(beforePrint), "rendering previews changed the running battle");

            client.PlayCard(uid, "now");
            Assert.That(client.LastError, Is.Null);
            var after = client.Snapshot!;
            Assert.That(after.V, Is.EqualTo(before.V + 1));
            Assert.That(after.Phase, Is.EqualTo("PLAYER").Or.EqualTo("VICTORY"));
            Assert.That(after.Zones.Hand, Does.Not.Contain(uid));
            Assert.That(after.Zones.Hand.Count, Is.EqualTo(before.Zones.Hand.Count - 1));
            Assert.That(after.Zones.Discard.Contains(uid) || after.Zones.Exhaust.Contains(uid), Is.True);
            Assert.That(after.Ap, Is.EqualTo(before.Ap - cost));
            Assert.That(after.Cards[uid].Used, Is.EqualTo(before.Cards[uid].Used + 1));
            Assert.That(after.Events.Any(e => e.E == "play" && e.Uid == uid && e.Mode == "now"), Is.True);
            int damage = before.Enemies.Sum(e => Math.Max(0, e.Hp - after.Enemies.First(a => a.Uid == e.Uid).Hp));
            int block = after.Block - before.Block;
            var previewParts = preview.Split(new[] { " · " }, StringSplitOptions.None); // MemoryForgeBattleClient.cs:127
            if (definition.Effects.Any(e => e.Op == "dmg"))
            {
                Assert.That(damage, Is.GreaterThan(0));
                Assert.That(previewParts, Does.Contain("피해 " + damage), "preview '" + preview + "' must equal the committed result");
            }
            if (definition.Effects.Any(e => e.Op == "blk"))
            {
                Assert.That(block, Is.GreaterThan(0));
                Assert.That(previewParts, Does.Contain("방어 +" + block), "preview '" + preview + "' must equal the committed result");
            }

            // Core rejections: explicit code, Korean toast, and no mutation at all.
            string committed = Fingerprint(after);
            string code = after.Phase == "PLAYER" ? "NOT_IN_HAND" : "WRONG_PHASE";
            client.PlayCard(uid, "now"); // the card already left the hand
            Assert.That(client.LastError, Is.EqualTo(code));
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed), "a rejected play changed the battle");
            string toast = ErrorMessage(code);
            Canvas.ForceUpdateCanvases();
            AssertReadable(ActiveView(host), t => t == toast, "rejection toast");
            if (after.Phase == "PLAYER")
            {
                int gaugeMax = ClientBattleContent.Load().Battle.Rules.GaugeMax;
                var actor = after.Actors.First(a => a.Gauge < gaugeMax);
                client.UseUltimate(actor.Id);
                Assert.That(client.LastError, Is.EqualTo("ULT_NOT_READY"));
                Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(committed), "a rejected ultimate changed the battle");
            }
        }

        // RULES_NOW_PLAY: no card can be reserved. The end of the turn keeps only 보존 cards in hand (marked held) and discards the rest;
        // the reset still rebuilds one owned view. (It replaced ReservationResolvesOnTheNextTurnAndResetRebuildsOneOwnedView.)
        [UnityTest]
        public IEnumerator EndTurnKeepsOnlyRetainedCardsAndResetRebuildsOneOwnedView()
        {
            var opening = new Opening();
            yield return OpenWithCard("now", (running, uid) => true, opening);
            Assert.That(opening.Failure, Is.Null, opening.Failure ?? "");
            var host = opening.Host!; var client = opening.Client!; var running = opening.Running!; string uid = opening.Card;
            string beginning = Fingerprint(client.Snapshot!);
            Assert.That(running.Check(uid, "next", opening.Target), Is.EqualTo("NOT_RESERVABLE"), "no card can be reserved");

            var before = client.Snapshot!; var hand = before.Zones.Hand.ToList();
            bool Retain(string card) => running.EffectiveCard(card).Keywords.Contains("retain");
            client.EndTurn();
            Assert.That(client.LastError, Is.Null);
            var next = client.Snapshot!;
            Assert.That(next.V, Is.EqualTo(before.V + 1));
            Assert.That(next.Turn, Is.EqualTo(before.Turn + 1));
            Assert.That(next.Phase, Is.EqualTo("PLAYER"));
            foreach (string card in hand)
            {
                Assert.That(next.Cards[card].Held, Is.EqualTo(Retain(card)), card + ": only a 보존 card is held");
                if (Retain(card)) Assert.That(next.Zones.Hand, Does.Contain(card), card + ": a 보존 card stays in hand");
            }
            // With at least a new hand's worth in the draw pile nothing was reshuffled, so every other card is in the discard pile.
            if (before.Zones.Draw.Count >= next.Zones.Hand.Count)
                Assert.That(next.Zones.Discard, Is.SupersetOf(hand.Where(card => !Retain(card))), "the rest of the hand is discarded");
            if (next.Phase == "PLAYER") Assert.That(next.Events.Any(e => e.E == "turn" && e.Turn == next.Turn), Is.True);

            client.StartPracticeBattle(); // the "다시 시작" handler
            yield return null;
            var view = SingleView(host);
            var active = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(e => e.isActiveAndEnabled).ToList();
            Assert.That(active.Count, Is.EqualTo(1), "exactly one active input system after reset");
            Assert.That(active[0].transform.IsChildOf(view.transform), Is.True, "the clean test scene's only input system must be owned by the view");
            Assert.That(client.LastError, Is.Null);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(beginning), "reset must recreate the same canonical opening");

            host.SetActive(false);
            yield return null;
            Assert.That(OwnedViews(host), Is.Empty, "disabling the controller must remove only its owned view");
            host.SetActive(true);
            yield return null;
            SingleView(host);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(beginning), "re-enabling must start the same canonical opening");
        }

        [UnityTest]
        public IEnumerator ModeAndTargetSelectionSpendNothingAndHistoryBlocksBackgroundClicks()
        {
            var opening = new Opening();
            yield return OpenWithCard("now", (running, uid) => DirectOnly(running.EffectiveCard(uid)), opening);
            Assert.That(opening.Failure, Is.Null, opening.Failure ?? "");
            var client = opening.Client!;
            string before = Fingerprint(client.Snapshot!); int version = client.Snapshot!.V;
            client.SelectCard(opening.Card); client.SelectMode("now"); client.SelectEnemy(opening.Target);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "selection/preview must never commit a card");
            yield return null; // Rebuilt uGUI graphics enter the raycast registry on the next frame.
            Canvas.ForceUpdateCanvases();
            var confirm = client.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text == "사용 확정");
            Assert.That(confirm.interactable, Is.True);
            var rect = confirm.GetComponent<RectTransform>();
            Vector2 behind = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            // Positive control: before the overlay opens, the same screen point must reach the real confirm button.
            var control = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = behind, button = PointerEventData.InputButton.Left }, control);
            Assert.That(control, Is.Not.Empty, "the probe point hit nothing before the overlay opened");
            Assert.That(control[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(confirm), "the probe point must hit the real confirm button before the overlay opens");
            client.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text == "기록").onClick.Invoke();
            yield return null;
            Canvas.ForceUpdateCanvases();
            var pointer = new PointerEventData(EventSystem.current) { position = behind, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.Null, "the overlay must intercept the obscured confirm button");
            ExecuteEvents.Execute(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "click-through spent a card/AP behind the history overlay");
            client.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text == "닫기").onClick.Invoke();
            client.ConfirmCard();
            Assert.That(client.LastError, Is.Null);
            Assert.That(client.Snapshot!.V, Is.EqualTo(version + 1));
            Assert.That(Fingerprint(client.Snapshot!), Is.Not.EqualTo(before), "only the explicit confirmation commits the action");
            yield return null;
        }

        [UnityTest]
        public IEnumerator DrawForecastDoesNotRevealOrConsumeTheHiddenDeck()
        {
            var opening = new Opening();
            yield return OpenWithCard("now", (running, uid) => running.EffectiveCard(uid).Effects.Any(e => e.Op == "draw"), opening);
            Assert.That(opening.Failure, Is.Null, opening.Failure ?? "");
            var client = opening.Client!; client.SelectEnemy(opening.Target); client.SelectCard(opening.Card);
            string before = Fingerprint(client.Snapshot!);
            Assert.That(client.Preview(opening.Card, "now"), Is.EqualTo("드로우·무작위 결과는 사용 후 확인합니다"));
            Assert.That(Fingerprint(client.Snapshot!), Is.EqualTo(before), "forecast consumed the hidden deck or RNG");
            yield return null;
        }

        // RULES_NOW_PLAY: Sera's ultimate draws a card instead of opening a next-turn slot, and collateral borrows one AP from the next
        // turn instead of sealing its slot, so the timeline holds no player reservation. (It replaced
        // TimelineKeepsEveryReservationAfterSeraUltimateAndLabelsSealedSlots.)
        [Test]
        public void SeraUltimateDrawsACardAndCollateralBorrowsInsteadOfSealingASlot()
        {
            var data = ClientBattleContent.Load().Battle; var rules = data.Rules;
            var effects = data.Characters.Single(c => c.Id == "sera").Ult.Effects;
            Assert.That((effects.Any(e => e.Op == "slot"), effects.Any(e => e.Op == "draw" && e.N == 1)), Is.EqualTo((false, true)), "source: the ultimate draws one card");

            // Independent Core battles from legitimate BattleSetup inputs.
            var ult = Battle.Create(data, new BattleSetup { Party = { new BattleActorSetup { CharId = "sera", Gauge = rules.GaugeMax } },
                Deck = Enumerable.Repeat("sera_round", 8).Select(id => new BattleCardSpec { Id = id }).ToList(),
                Enemies = { "e_shelf" }, Hp = 243, MaxHp = 243, Seed = 1 });
            Send(ult, "begin");
            string enemy = ult.State.Enemies[0].Uid; int hand = ult.State.Zones.Hand.Count;
            Assert.That(ult.State.Zones.Draw, Is.Not.Empty, "precondition: a card is left to draw");
            Send(ult, "ult", ("actor", "sera"), ("target", enemy));
            Assert.That((ult.State.SlotsTemp.Next, ult.State.Zones.Hand.Count), Is.EqualTo((0, hand + 1)), "the ultimate drew one card and opened no slot");
            Assert.That(ult.Check(ult.State.Zones.Hand[0], "next", enemy), Is.EqualTo("NOT_RESERVABLE"));
            Assert.That(MemoryForgeBattleClient.TimelineCells(ult.State, rules).Any(c => c.Kind == "card" || c.Kind == "sealed"), Is.False, "no player reservation");

            var loan = Battle.Create(data, new BattleSetup { Party = { new BattleActorSetup { CharId = "ravien" } },
                Deck = new[] { "ravien_collateral", "com_guard", "com_guard", "com_guard", "com_guard" }.Select(id => new BattleCardSpec { Id = id }).ToList(),
                Enemies = { "e_shelf" }, Hp = 243, MaxHp = 243, Seed = 1 });
            Send(loan, "begin");
            Send(loan, "play", ("uid", loan.State.Zones.Hand.Single(uid => loan.State.Cards[uid].Id == "ravien_collateral")), ("mode", "now"));
            var s = loan.State;
            Assert.That(s.SealedUntil, Is.LessThan(s.Turn), "collateral seals no slot");
            Assert.That(s.Debts.Select(d => (d.N, d.Due, d.Deferred)), Is.EqualTo(new[] { (1, s.Turn + 1, false) }), "it borrows one AP from the next turn");
            Assert.That(MemoryForgeBattleClient.TimelineCells(s, rules).Any(c => c.Kind == "card" || c.Kind == "sealed"), Is.False, "no sealed slot is drawn");
        }

        [UnityTest]
        public IEnumerator EnemyIntentForecastMatchesActualHitsAndStatusesAreVisible()
        {
            var data = ClientBattleContent.Load().Battle; var rules = data.Rules;
            var moth = data.Enemies.Single(e => e.Id == "e_moth"); // source pattern: atk 12, atk 7×2, buff str 1
            int vul = data.Cards.Single(c => c.Id == "nia_scan").Effects.First(e => e.Op == "st").N!.Value;
            int weak = data.Cards.Single(c => c.Id == "harin_taunt").Effects[0].Then![0].N!.Value;

            // Independent Core battle from legitimate BattleSetup inputs; Core's actual hit events are the oracle.
            var battle = Battle.Create(data, new BattleSetup
            {
                Party = { new BattleActorSetup { CharId = "nia" }, new BattleActorSetup { CharId = "harin" }, new BattleActorSetup { CharId = "sera" } },
                Deck = new[] { "nia_scan", "harin_taunt", "sera_aim", "harin_shield", "nia_barrier" }.Select(id => new BattleCardSpec { Id = id }).ToList(),
                Enemies = { "e_moth" }, Hp = 243, MaxHp = 243, Seed = 1
            });
            Send(battle, "begin");
            string uid = battle.State.Enemies[0].Uid;
            string InHand(string id) => battle.State.Zones.Hand.Single(c => battle.State.Cards[c].Id == id);
            Send(battle, "play", ("uid", InHand("nia_scan")), ("mode", "now"), ("target", uid));
            Send(battle, "play", ("uid", InHand("harin_taunt")), ("mode", "now"), ("target", uid));
            Send(battle, "play", ("uid", InHand("sera_aim")), ("mode", "now"));
            var s = battle.State; var enemy = s.Enemies[0];
            Assert.That(enemy.Status.Vul, Is.EqualTo(vul));
            Assert.That(enemy.Status.Weak, Is.EqualTo(weak));
            Assert.That(MemoryForgeBattleClient.StatusText(enemy, rules), Does.Contain("취약 " + vul).And.Contain("약화 " + weak));
            Assert.That(MemoryForgeBattleClient.PartyStatusText(s), Does.Contain("충전"));
            Assert.That(MemoryForgeBattleClient.IntentText(enemy.Next, enemy.Status, s.DifficultyDamage, rules.WeakBps, false),
                Is.EqualTo("기본 공격 " + moth.Pattern[1].Damage + " × " + moth.Pattern[1].Hits));

            bool weakened = false, strengthened = false;
            for (int turn = 0; turn < 4; turn++)
            {
                s = battle.State; enemy = s.Enemies[0];
                Assert.That(s.Phase, Is.EqualTo("PLAYER"));
                var intent = enemy.Intent!;
                int forecast = MemoryForgeBattleClient.IntentDamage(intent.Damage ?? 0, s.DifficultyDamage, enemy.Status.Strength, enemy.Status.Weak, rules.WeakBps);
                string text = MemoryForgeBattleClient.IntentText(intent, enemy.Status, s.DifficultyDamage, rules.WeakBps, true);
                Send(battle, "end");
                var hits = battle.State.Events.Where(e => e.E == "hit" && e.Uid == enemy.Uid).Select(e => e.Damage).ToList();
                if (intent.Type == "atk")
                {
                    Assert.That(text, Does.StartWith("공격 " + forecast));
                    Assert.That(hits.Count, Is.EqualTo(Math.Max(1, intent.Hits ?? 1)));
                    Assert.That(hits, Is.All.EqualTo(forecast), "turn " + s.Turn + " forecast must equal Core's actual hit");
                    weakened |= enemy.Status.Weak > 0 && forecast != intent.Damage;
                    strengthened |= enemy.Status.Strength > 0 && forecast != intent.Damage;
                }
                else Assert.That(hits, Is.Empty);
            }
            Assert.That(weakened && strengthened, Is.True, "the source pattern must exercise weak (turn 1) and strength (turn 4)");

            // The fixed UI practice flow: structure analysis visibly registers on the chosen enemy panel.
            var opening = new Opening();
            yield return OpenWithCard("now", (running, card) => running.State.Cards[card].Id == "nia_scan", opening);
            Assert.That(opening.Failure, Is.Null, opening.Failure ?? "");
            var client = opening.Client!;
            client.SelectCard(opening.Card); client.SelectMode("now"); client.SelectEnemy(opening.Target);
            client.ConfirmCard();
            Assert.That(client.LastError, Is.Null);
            var after = client.Snapshot!;
            var scanned = after.Enemies.Single(e => e.Uid == opening.Target);
            Assert.That(scanned.Status.Vul, Is.EqualTo(vul));
            Canvas.ForceUpdateCanvases();
            var view = ActiveView(opening.Host!);
            AssertReadable(view, t => t == MemoryForgeBattleClient.StatusText(scanned, rules), "enemy status chips");
            foreach (var foe in after.Enemies.Where(x => x.Alive))
            {
                string lines = "이번  " + MemoryForgeBattleClient.IntentText(foe.Intent, foe.Status, after.DifficultyDamage, rules.WeakBps, true)
                    + "\n다음  " + MemoryForgeBattleClient.IntentText(foe.Next, foe.Status, after.DifficultyDamage, rules.WeakBps, false);
                AssertReadable(view, t => t == lines, "intent of " + foe.Uid);
            }
            yield return null;
        }

        static void Send(Battle battle, string kind, params (string Key, string Value)[] args)
        {
            int v = battle.State.V;
            battle.Command(new BattleRequest(kind + "-" + v, v, kind, args.Length == 0 ? null
                : BattleArgument.Object(args.Select(a => new KeyValuePair<string, BattleArgument>(a.Key, BattleArgument.String(a.Value))))));
        }

        sealed class Opening
        {
            public GameObject? Host;
            public MemoryForgeBattleClient? Client;
            public Battle? Running;
            public string Card = "", Target = "";
            public string? Failure;
        }

        // Seeds are set on a disabled host before the controller's real OnEnable start; nothing else is injected.
        IEnumerator OpenWithCard(string mode, Func<Battle, string, bool> wanted, Opening result)
        {
            foreach (uint seed in Seeds)
            {
                var host = new GameObject("BattleClientTestHost"); hosts.Add(host);
                host.SetActive(false);
                host.AddComponent<MemoryForgeBattleClient>().Seed = seed;
                host.SetActive(true);
                yield return null;
                if (Inspect(host, mode, wanted, seed, result) || result.Failure != null) yield break;
                hosts.Remove(host); Object.Destroy(host);
                yield return null;
            }
            result.Failure = "no opening hand for seeds 1234,1-48 offers a legal '" + mode + "' card for this check";
        }

        // No asserts here: failures are returned so a nested coroutine never throws.
        static bool Inspect(GameObject host, string mode, Func<Battle, string, bool> wanted, uint seed, Opening result)
        {
            try
            {
                var client = host.GetComponent<MemoryForgeBattleClient>();
                var state = client.Snapshot;
                if (state == null || state.Phase != "PLAYER") { result.Failure = "seed " + seed + " did not open a PLAYER turn (" + client.LastError + ")"; return false; }
                var running = RunningBattle(client);
                string target = state.Enemies.First(e => e.Alive).Uid;
                string? card = state.Zones.Hand.FirstOrDefault(uid => running.AllowedModes(uid).Contains(mode) && running.NeedsTarget(uid) != "fixed"
                    && running.Check(uid, mode, target) == null && wanted(running, uid));
                if (card == null) return false;
                result.Host = host; result.Client = client; result.Running = running; result.Card = card; result.Target = target;
                return true;
            }
            catch (Exception error) { result.Failure = error.GetType().Name + ": " + error.Message; return false; }
        }

        // Read-only use of the controller's running core for pure queries (Check/AllowedModes/CostOf/EffectiveCard).
        static Battle RunningBattle(MemoryForgeBattleClient client)
        {
            var field = typeof(MemoryForgeBattleClient).GetField("battle", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("MemoryForgeBattleClient has no private 'battle' field; expose a read-only query surface instead");
            return field.GetValue(client) as Battle ?? throw new InvalidOperationException("the controller has no running battle");
        }

        static bool DirectOnly(BattleCardDef card) => card.Effects.Count != 0 && card.Effects.All(e =>
            (e.Op == "dmg" || e.Op == "blk" && e.Base0 != true) && e.V > 0 && e.Condition == null && e.Multipliers == null && e.Then == null && e.Else == null);

        // CanonicalState omits "log"/"ev", so they are hashed separately for a complete state comparison.
        static string Fingerprint(BattleState state)
        {
            var graph = state.ToJsonGraph();
            return CanonicalState.Hash(graph) + "/" + CanonicalState.Hash(graph["log"]) + "/" + CanonicalState.Hash(graph["ev"]);
        }

        static List<GameObject> OwnedViews(GameObject host)
        {
            var views = new List<GameObject>();
            foreach (Transform child in host.transform) if (child.name == OwnedCanvas) views.Add(child.gameObject);
            return views;
        }
        static GameObject SingleView(GameObject host)
        {
            var views = OwnedViews(host);
            Assert.That(views.Count, Is.EqualTo(1), "expected exactly one owned battle view after a frame");
            Assert.That(views[0].activeInHierarchy, Is.True);
            Assert.That(views[0].GetComponent<Canvas>(), Is.Not.Null);
            return views[0];
        }
        // Within a frame a replaced view is inactive until its deferred Destroy; only one may be active.
        static GameObject ActiveView(GameObject host)
        {
            var active = OwnedViews(host).Where(v => v.activeInHierarchy).ToList();
            Assert.That(active.Count, Is.EqualTo(1), "expected exactly one active owned battle view");
            return active[0];
        }

        // Regression for the font line-height clipping: labels must generate glyphs and must not be truncated.
        static void AssertReadable(GameObject view, Func<string, bool> match, string what, int atLeast = 1)
        {
            var labels = view.GetComponentsInChildren<Text>(false).Where(t => match(t.text)).ToList();
            Assert.That(labels.Count, Is.GreaterThanOrEqualTo(atLeast), what + " label is missing");
            foreach (var label in labels)
            {
                Assert.That(label.isActiveAndEnabled && label.font != null && label.color.a > 0f, Is.True, what + " label is not drawable");
                var generator = label.cachedTextGenerator;
                int glyphs = label.text.Count(ch => !char.IsWhiteSpace(ch));
                Assert.That(generator.vertexCount, Is.GreaterThan(0), what + " '" + label.text + "' generated no vertices");
                Assert.That(generator.characterCountVisible, Is.GreaterThanOrEqualTo(glyphs), what + " '" + label.text + "' is truncated");
            }
        }

        static string ErrorMessage(string code)
        {
            var asset = Resources.Load<TextAsset>("MemoryForge/error-messages");
            Assert.That(asset, Is.Not.Null, "error-messages resource is missing");
            return (string?)((JObject)ParseStrict(asset!.text))[code] ?? throw new AssertionException("no Korean message for " + code);
        }

        static JToken ParseStrict(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal })
            {
                var token = JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                Assert.That(reader.Read(), Is.False, "unexpected content after the JSON document");
                return token;
            }
        }

        // First difference path, or null. Arrays and dictionary keys keep authored order; typed members compare as sets.
        static string? Difference(JToken expected, JToken actual, string path)
        {
            if (expected.Type != actual.Type) return path + " (" + expected.Type + " vs " + actual.Type + ")";
            if (expected is JObject left)
            {
                var right = (JObject)actual;
                var leftKeys = left.Properties().Select(p => p.Name).ToList();
                var rightKeys = right.Properties().Select(p => p.Name).ToList();
                bool ordered = path == "$.StartingCommon" || path == "$.CharacterWeaponTypes" || path == "$.Encounters" || path.EndsWith(".Passive.Params", StringComparison.Ordinal);
                if (ordered ? !leftKeys.SequenceEqual(rightKeys) : (leftKeys.Count != rightKeys.Count || leftKeys.Except(rightKeys).Any()))
                    return path + " members [" + string.Join(",", leftKeys) + "] vs [" + string.Join(",", rightKeys) + "]";
                foreach (string name in leftKeys)
                {
                    string? difference = Difference(left[name]!, right[name]!, path + "." + name);
                    if (difference != null) return difference;
                }
                return null;
            }
            if (expected is JArray list)
            {
                var other = (JArray)actual;
                if (list.Count != other.Count) return path + " length " + list.Count + " vs " + other.Count;
                for (int i = 0; i < list.Count; i++)
                {
                    string? difference = Difference(list[i], other[i], path + "[" + i + "]");
                    if (difference != null) return difference;
                }
                return null;
            }
            return JToken.DeepEquals(expected, actual) ? null : path + " value " + expected.ToString(Formatting.None) + " vs " + actual.ToString(Formatting.None);
        }

        // Editor only: the resource must come from the current original data/game_data.json, unchanged and complete.
        static void CompareWithOriginalSource(ClientBattleContent content)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "data", "game_data.json"));
            Assert.That(File.Exists(path), Is.True, "original data/game_data.json not found at " + path);
            byte[] bytes = File.ReadAllBytes(path);
            string sha;
            using (var hash = SHA256.Create()) sha = string.Concat(hash.ComputeHash(bytes).Select(x => x.ToString("x2", CultureInfo.InvariantCulture)));
            Assert.That(content.SourceSha256, Is.EqualTo(sha), "battle-content.json is stale; re-export from the current data/game_data.json");
            var source = (JObject)ParseStrict(new UTF8Encoding(false, true).GetString(bytes));
            Assert.That(content.DataVersion, Is.EqualTo((string?)source["meta"]?["version"]));

            var b = content.Battle;
            Rows(source, "characters", b.Characters.Select(x => Row(x.Id, x.Name, x.Attr)), "id", "name", "attr");
            Rows(source, "cards", b.Cards.Select(x => Row(x.Id, x.Name, x.Owner, x.Rarity, x.Cost, x.Reservation, x.Type, x.Attr)), "id", "name", "owner", "rar", "cost", "res", "type", "attr");
            Rows(source, "weapons", b.Weapons.Select(x => Row(x.Id, x.Name, x.Type, x.Rarity, x.Card)), "id", "name", "type", "rarity", "card");
            Rows(source, "parts", b.Parts.Select(x => Row(x.Id, x.Slot, x.Name, x.Rarity)), "id", "slot", "name", "rarity");
            Rows(source, "relics", b.Relics.Select(x => Row(x.Id, x.Name, x.Rarity)), "id", "name", "rarity");
            Rows(source, "enemies", b.Enemies.Select(x => Row(x.Id, x.Name, x.Tier, x.Hp, x.Brk)), "id", "name", "tier", "hp", "brk");

            Assert.That(content.StartingCommon.Select(p => p.Key + "=" + p.Value),
                Is.EqualTo(((JObject)source["startingCommon"]!).Properties().Select(p => p.Name + "=" + (string?)p.Value)));
            Assert.That(content.CharacterWeaponTypes.Select(p => p.Key + "=" + p.Value),
                Is.EqualTo(((JArray)source["characters"]!).Select(ch => Field(ch, "id") + "=" + Field(ch, "weapon"))));
            var zones = (JObject)source["encounters"]!;
            Assert.That(content.Encounters.Keys, Is.EqualTo(zones.Properties().Select(p => p.Name)));
            foreach (var zone in zones.Properties())
            {
                var set = content.Encounters[zone.Name];
                foreach (var (key, rows) in new[] { ("normal", set.Normal), ("elite", set.Elite), ("boss", set.Boss) })
                    Assert.That(JToken.DeepEquals(zone.Value[key], JArray.FromObject(rows)), Is.True, "encounters." + zone.Name + "." + key);
            }
        }
        static void Rows(JObject source, string key, IEnumerable<string> actual, params string[] fields) =>
            Assert.That(actual.ToList(), Is.EqualTo(((JArray)source[key]!).Select(item => string.Join("|", fields.Select(f => Field(item, f)))).ToList()),
                key + " differ from the original source (ids, values or authored order)");
        static string Row(params object[] values) => string.Join("|", values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));
        static string Field(JToken item, string key) => (string?)item[key] ?? "<missing>";
    }
}
