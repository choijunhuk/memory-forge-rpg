// P1-CARD-FLOW-TURN-DIGEST: pure checks of BattleTurnDigest.Build over explicit public DTOs and hand-written Core BattleEvents with
// current field semantics. No Run, Battle, content, scene, timer or live state is created or injected; expected rows are literal.
// This is not Unity view integration, presentation timing, device or server acceptance.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using MemoryForge.Core;
using NUnit.Framework;

namespace MemoryForge.Client.Tests
{
    public class BattleTurnDigestPlayModeTests
    {
        static readonly HashSet<BattleDigestKind> Frame = new HashSet<BattleDigestKind> { BattleDigestKind.Summary, BattleDigestKind.PreviousIntent, BattleDigestKind.EnemyState };
        static List<(BattleDigestKind, string)> EventRows(IReadOnlyList<BattleDigestEntry> rows) => rows.Where(r => !Frame.Contains(r.Kind)).Select(r => (r.Kind, r.Text)).ToList();
        static List<(BattleDigestKind, string)> All(IReadOnlyList<BattleDigestEntry> rows) => rows.Select(r => (r.Kind, r.Text)).ToList();

        static BattleDigestEnemy Enemy(string uid, string name, int hp, int block, bool alive, string? intent) =>
            new BattleDigestEnemy { Uid = uid, Name = name, Hp = hp, Block = block, Alive = alive, IntentText = intent };
        static BattleDigestSnapshot Field(Dictionary<string, string>? cards = null, List<BattleDigestReservation>? reservations = null) => new BattleDigestSnapshot
        {
            Turn = 3, Phase = "PLAYER",
            Enemies = new List<BattleDigestEnemy> { Enemy("e1", "기억 나방", 18, 0, true, "공격 12"), Enemy("e2", "톱니 병정", 24, 0, true, "방어 6") },
            CardNames = cards, Reservations = reservations
        };
        static BattleDigestReservation Reserved(string uid, string name, int due, string kind) => new BattleDigestReservation { Uid = uid, Name = name, Due = due, Kind = kind };
        static BattleEvent Dmg(string uid, int n, int blocked, string source, string? card = null) => new BattleEvent { E = "dmg", Uid = uid, N = n, Blocked = blocked, Source = source, Card = card };

        [Test]
        public void ImmediateUseTimeReservationRegistrationAndReservationResolutionAreDistinctRows()
        {
            var before = Field(new Dictionary<string, string> { ["c14"] = "정조준", ["c20"] = "유리탄", ["c21"] = "단검 찌르기" }, new List<BattleDigestReservation> { Reserved("c9", "시한 포격", 4, "next") });
            var events = new List<BattleEvent>
            {
                new BattleEvent { E = "play", Uid = "c14", Id = "sera_aim", Mode = "now", Cost = 1, Target = "e2" },
                Dmg("e2", 8, 0, "card", "sera_aim"),
                new BattleEvent { E = "resolved", Uid = "c14", Id = "sera_aim", Fizzled = false },
                new BattleEvent { E = "play", Uid = "c20", Id = "sera_round", Mode = "next", Cost = 2, Target = "e1" },
                new BattleEvent { E = "reserve", Uid = "c20", Due = 4, Kind = "next" },
                Dmg("e1", 22, 3, "card", "sera_timed"),
                new BattleEvent { E = "resolved", Uid = "c9", Id = "sera_timed", Reserved = "next", Fizzled = false },
                new BattleEvent { E = "fizzle", Uid = "c21" },
                new BattleEvent { E = "resolved", Uid = "c21", Id = "dagger", Reserved = "after", Fizzled = true }
            };
            var rows = BattleTurnDigest.Build(before, events, null);
            Assert.That(EventRows(rows), Is.EqualTo(new[]
            {
                (BattleDigestKind.CardPlayed, "카드 사용 · 정조준 → 톱니 병정 · 행동력 1"),
                (BattleDigestKind.CardDamage, "톱니 병정에게 피해 8 · 카드 「정조준」"),
                (BattleDigestKind.CardResolved, "효과 처리 끝 · 정조준"),
                (BattleDigestKind.ReservationPlayed, "예약 사용 · 유리탄 → 기억 나방 · 다음 턴 · 행동력 2"),
                (BattleDigestKind.ReservationRegistered, "시간 예약 등록 · 유리탄 · 다음 턴 예약 · 4턴 시작에 해결"),
                (BattleDigestKind.CardDamage, "기억 나방에게 피해 22 (방어 3 막힘) · 카드 「시한 포격」"),
                (BattleDigestKind.ReservationResolved, "예약 해결 · 시한 포격 · 다음 턴 예약"),
                (BattleDigestKind.Fizzle, "불발 · 단검 찌르기 · 표식 대상 없음"),
                (BattleDigestKind.ReservationResolved, "예약 해결 · 단검 찌르기 · 다다음 턴 예약 · 불발")
            }));
            Assert.That(rows.Where(r => r.IsReservedResolution).Select(r => r.Text), Is.EqualTo(new[] { "예약 해결 · 시한 포격 · 다음 턴 예약", "예약 해결 · 단검 찌르기 · 다다음 턴 예약 · 불발" }),
                "only resolved events with Reserved set are time-reservation resolutions; the immediate resolve and the registration are not");
            Assert.That(rows.Single(r => r.Kind == BattleDigestKind.CardDamage && r.Text.Contains("정조준")).Provenance, Is.EqualTo("카드"));
        }

        [Test]
        public void CopiesAreCopyRowsNotPlaysAndUseOnlyNamesTheEventsSupport()
        {
            var before = Field(new Dictionary<string, string> { ["c31"] = "유리탄" });
            var events = new List<BattleEvent>
            {
                new BattleEvent { E = "resolved", Uid = "c31", Id = "sera_round", Reserved = "next" },
                new BattleEvent { E = "echoCopy", Uid = "c40", Id = "sera_round" }, // unnamed copy uid: the definition's name comes from c31's own event
                new BattleEvent { E = "echoCopy", Uid = "c41", Id = "unknown_card" },
                new BattleEvent { E = "exhaust", Uid = "c31" },
                new BattleEvent { E = "exhaust", Uid = "c41" }
            };
            var rows = BattleTurnDigest.Build(before, events, null);
            Assert.That(EventRows(rows), Is.EqualTo(new[]
            {
                (BattleDigestKind.ReservationResolved, "예약 해결 · 유리탄 · 다음 턴 예약"),
                (BattleDigestKind.Copy, "메아리 사본 · 유리탄"),
                (BattleDigestKind.Copy, "메아리 사본 · 카드"),
                (BattleDigestKind.Exhaust, "소멸 · 유리탄"),
                (BattleDigestKind.Exhaust, "소멸 · 카드")
            }));
            Assert.That(rows.Any(r => r.Kind == BattleDigestKind.CardPlayed || r.Kind == BattleDigestKind.ReservationPlayed), Is.False, "a copy is not a card use");
        }

        [Test]
        public void EnemyAttackCounterRelicBurnReflectAndOverloadKeepTheirOwnProvenance()
        {
            var events = new List<BattleEvent>
            {
                new BattleEvent { E = "hit", Uid = "e1", Damage = 12, Blocked = 6 },
                new BattleEvent { E = "hpLoss", N = 6, Source = "enemy" },
                Dmg("e1", 2, 0, "counter"),
                Dmg("e2", 3, 1, "relic"),
                Dmg("e2", 4, 0, "burn"),
                new BattleEvent { E = "hpLoss", N = 5, Source = "reflect" },
                new BattleEvent { E = "hpLoss", N = 4, Source = "overload" },
                new BattleEvent { E = "hpLoss", N = 2, Source = "relic" },
                new BattleEvent { E = "block", N = 8, Source = "relic" },
                new BattleEvent { E = "block", N = 3, Source = "moss" },
                new BattleEvent { E = "block", N = 5, Source = "card" },
                new BattleEvent { E = "block", N = 2, Source = "weapon" },
                Dmg("e1", 9, 0, "ult"),
                Dmg("e1", 7, 0, "card", "never_named"),
                new BattleEvent { E = "envelope" }
            };
            var rows = BattleTurnDigest.Build(Field(), events, null);
            var detail = rows.Where(r => !Frame.Contains(r.Kind)).Select(r => (r.Kind, r.Text, r.Provenance)).ToList();
            Assert.That(detail, Is.EqualTo(new[]
            {
                (BattleDigestKind.EnemyAttack, "기억 나방의 공격 · 피해 12 (방어 6 막음)", "적 공격"),
                (BattleDigestKind.PartyHpLoss, "파티 체력 -6 · 적 공격", "적 공격"),
                (BattleDigestKind.CounterDamage, "기억 나방에게 피해 2 · 반격", "반격"),
                (BattleDigestKind.RelicDamage, "톱니 병정에게 피해 3 (방어 1 막힘) · 유물", "유물"),
                (BattleDigestKind.BurnDamage, "톱니 병정에게 피해 4 · 화상", "화상"),
                (BattleDigestKind.PartyHpLoss, "파티 체력 -5 · 반사", "반사"),
                (BattleDigestKind.PartyHpLoss, "파티 체력 -4 · 과부하", "과부하"),
                (BattleDigestKind.PartyHpLoss, "파티 체력 -2 · 유물", "유물"),
                (BattleDigestKind.PartyBlock, "방어 +8 · 유물", "유물"),
                (BattleDigestKind.PartyBlock, "방어 +3 · 유물", "유물"),
                (BattleDigestKind.PartyBlock, "방어 +5 · 카드", "카드"),
                (BattleDigestKind.PartyBlock, "방어 +2 · 무기", "무기"),
                (BattleDigestKind.UltimateDamage, "기억 나방에게 피해 9 · 필살기", "필살기"),
                (BattleDigestKind.CardDamage, "기억 나방에게 피해 7 · 카드", "카드"),
                (BattleDigestKind.Other, "유물 효과 · 체력 1로 버팀", "유물")
            }));
            var damage = new[] { BattleDigestKind.CardDamage, BattleDigestKind.UltimateDamage, BattleDigestKind.CounterDamage, BattleDigestKind.RelicDamage, BattleDigestKind.BurnDamage, BattleDigestKind.EffectDamage };
            Assert.That(rows.Where(r => r.Provenance == "반사" || r.Provenance == "과부하").Select(r => r.Kind), Is.All.EqualTo(BattleDigestKind.PartyHpLoss),
                "reflect and overload are party HP loss, never enemy or card-use damage");
            Assert.That(rows.Where(r => damage.Contains(r.Kind) && r.Provenance == "카드").Select(r => r.Text).Any(t => t.Contains("반격") || t.Contains("유물")), Is.False);
        }

        [Test]
        public void UnknownEventsMissingFieldsAndNullInputsGiveSafeGenericRows()
        {
            Assert.That(All(BattleTurnDigest.Build(null, null, null)), Is.EqualTo(new[] { (BattleDigestKind.Summary, "턴 결과"), (BattleDigestKind.Summary, "파티 상태 정보 없음") }));

            var before = new BattleDigestSnapshot
            {
                Enemies = new List<BattleDigestEnemy> { null!, new BattleDigestEnemy { Uid = "e1", Alive = true } },
                Reservations = new List<BattleDigestReservation> { null!, new BattleDigestReservation() },
                CardNames = new Dictionary<string, string> { ["c1"] = null! }
            };
            var events = new List<BattleEvent?>
            {
                null, new BattleEvent { E = "" }, new BattleEvent { E = "apDrain", Uid = "e1", N = 1 }, new BattleEvent { E = "dmg" }, new BattleEvent { E = "hit" },
                new BattleEvent { E = "play", Uid = "c1" }, new BattleEvent { E = "resolved", Reserved = "later" }, new BattleEvent { E = "hpLoss", Source = "mystery" },
                new BattleEvent { E = "block" }, new BattleEvent { E = "turn" }, new BattleEvent { E = "end" }, new BattleEvent { E = "reserve", Uid = "zz" },
                new BattleEvent { E = "status", Uid = "e9" }
            };
            List<(BattleDigestKind, string)> rows = new List<(BattleDigestKind, string)>();
            Assert.DoesNotThrow(() => rows = All(BattleTurnDigest.Build(before, events, new BattleDigestSnapshot())));
            Assert.That(rows, Is.EqualTo(new[]
            {
                (BattleDigestKind.Summary, "턴 결과"),
                (BattleDigestKind.Summary, "파티 상태 정보 없음"),
                (BattleDigestKind.PreviousIntent, "적 · 직전 예고 정보 없음"),
                (BattleDigestKind.Other, "기타 전투 효과"),
                (BattleDigestKind.Other, "기타 전투 효과"),
                (BattleDigestKind.Other, "기타 전투 효과"),
                (BattleDigestKind.EffectDamage, "적에게 피해 · 기타 효과"),
                (BattleDigestKind.EnemyAttack, "적의 공격"),
                (BattleDigestKind.CardPlayed, "카드 사용 · 카드"),
                (BattleDigestKind.ReservationResolved, "예약 해결 · 카드"),
                (BattleDigestKind.PartyHpLoss, "파티 체력 감소 · 기타"),
                (BattleDigestKind.PartyBlock, "방어 증가 · 기타"),
                (BattleDigestKind.TurnStart, "새 턴 시작"),
                (BattleDigestKind.BattleEnd, "전투 종료"),
                (BattleDigestKind.ReservationRegistered, "시간 예약 등록 · 카드"),
                (BattleDigestKind.EnemyStatus, "적 · 상태")
            }));
        }

        [Test]
        public void PreviousIntentIsOnlyAnAnnouncementAndOnlyEventsOrFinalPublicStateReportWhatHappened()
        {
            var before = new BattleDigestSnapshot
            {
                Turn = 3,
                Enemies = new List<BattleDigestEnemy>
                {
                    Enemy("e1", "기억 나방", 18, 0, true, "공격 12"), Enemy("e2", "톱니 병정", 24, 0, true, "방어 6"),
                    Enemy("e3", "녹슨 종", 0, 0, false, "공격 5"), Enemy("e4", "잉크 덩어리", 30, 0, true, null)
                }
            };
            var after = new BattleDigestSnapshot
            {
                Turn = 4,
                Enemies = new List<BattleDigestEnemy>
                {
                    Enemy("e1", "기억 나방", 18, 0, true, "기본 공격 7"), Enemy("e2", "톱니 병정", 24, 6, true, "공격 18"),
                    Enemy("e3", "녹슨 종", 0, 0, false, null), Enemy("e4", "잉크 덩어리", 30, 0, true, "대기")
                }
            };
            var events = new List<BattleEvent> { new BattleEvent { E = "hit", Uid = "e1", Damage = 12, Blocked = 6 }, new BattleEvent { E = "hpLoss", N = 6, Source = "enemy" } };
            var rows = BattleTurnDigest.Build(before, events, after);
            Assert.That(All(rows), Is.EqualTo(new[]
            {
                (BattleDigestKind.Summary, "턴 결과 · 3턴 → 4턴"),
                (BattleDigestKind.Summary, "파티 상태 정보 없음"),
                (BattleDigestKind.PreviousIntent, "기억 나방 · 직전 예고 · 공격 12"),
                (BattleDigestKind.PreviousIntent, "톱니 병정 · 직전 예고 · 방어 6"),
                (BattleDigestKind.PreviousIntent, "잉크 덩어리 · 직전 예고 정보 없음"),
                (BattleDigestKind.EnemyAttack, "기억 나방의 공격 · 피해 12 (방어 6 막음)"),
                (BattleDigestKind.PartyHpLoss, "파티 체력 -6 · 적 공격"),
                (BattleDigestKind.EnemyState, "결과 · 톱니 병정 · 방어 0 → 6")
            }), "a dead enemy has no previous intent row; the new intents after the command are not reported as past actions");
            Assert.That(EventRows(rows).Any(r => r.Item2.Contains("톱니 병정")), Is.False,
                "no event says the 방어 6 intent ran, so it appears only as 직전 예고 and the final public block, never as an executed action");
        }

        [Test]
        public void SummaryAndFinalEnemyStateShowOnlyProjectedPublicDeltasIncludingVictoryAndDefeat()
        {
            BattleDigestSnapshot Before() => new BattleDigestSnapshot
            {
                Turn = 3, Phase = "PLAYER", Hp = 120, Block = 6, Ap = 1,
                Reservations = new List<BattleDigestReservation> { Reserved("c9", "시한 포격", 4, "next"), Reserved("c11", "유리탄", 5, "after") },
                Enemies = new List<BattleDigestEnemy> { Enemy("e1", "기억 나방", 18, 0, true, "공격 12"), Enemy("e2", "톱니 병정", 24, 0, true, "방어 6") }
            };
            var after = new BattleDigestSnapshot
            {
                Turn = 4, Phase = "PLAYER", Hp = 98, Block = 0, Ap = 4,
                Reservations = new List<BattleDigestReservation> { Reserved("c11", "유리탄", 5, "after") },
                Enemies = new List<BattleDigestEnemy> { Enemy("e1", "기억 나방", 0, 0, false, null), Enemy("e2", "톱니 병정", 14, 6, true, "공격 18"), Enemy("e5", "작은 나방", 9, 0, true, "공격 4") }
            };
            Assert.That(All(BattleTurnDigest.Build(Before(), new List<BattleEvent>(), after)), Is.EqualTo(new[]
            {
                (BattleDigestKind.Summary, "턴 결과 · 3턴 → 4턴"),
                (BattleDigestKind.Summary, "파티 체력 120 → 98 · 방어 6 → 0 · 행동력 1 → 4 · 시간 예약 2장 → 1장"),
                (BattleDigestKind.PreviousIntent, "기억 나방 · 직전 예고 · 공격 12"),
                (BattleDigestKind.PreviousIntent, "톱니 병정 · 직전 예고 · 방어 6"),
                (BattleDigestKind.EnemyState, "결과 · 기억 나방 · 체력 18 → 0 · 처치됨"),
                (BattleDigestKind.EnemyState, "결과 · 톱니 병정 · 체력 24 → 14 · 방어 0 → 6"),
                (BattleDigestKind.EnemyState, "결과 · 작은 나방 · 새로 등장 · 체력 9")
            }));

            // After a finishing end the live battle is gone: only the projected phase and party HP are known, and nothing else is invented.
            var victory = BattleTurnDigest.Build(Before(), new List<BattleEvent> { new BattleEvent { E = "kill", Uid = "e2", Id = "e_cog" }, new BattleEvent { E = "end", Phase = "VICTORY" } },
                new BattleDigestSnapshot { Phase = "VICTORY", Hp = 98 });
            Assert.That(All(victory), Is.EqualTo(new[]
            {
                (BattleDigestKind.Summary, "턴 결과 · 3턴 · 전투 승리"),
                (BattleDigestKind.Summary, "파티 체력 120 → 98"),
                (BattleDigestKind.PreviousIntent, "기억 나방 · 직전 예고 · 공격 12"),
                (BattleDigestKind.PreviousIntent, "톱니 병정 · 직전 예고 · 방어 6"),
                (BattleDigestKind.Kill, "톱니 병정 처치"),
                (BattleDigestKind.BattleEnd, "전투 종료 · 승리")
            }));
            var defeat = BattleTurnDigest.Build(Before(), new List<BattleEvent> { new BattleEvent { E = "hpLoss", N = 120, Source = "enemy" }, new BattleEvent { E = "end", Phase = "DEFEAT" } },
                new BattleDigestSnapshot { Phase = "DEFEAT", Hp = 0 });
            Assert.That(defeat[0].Text, Is.EqualTo("턴 결과 · 3턴 · 전투 패배"));
            Assert.That(defeat[1].Text, Is.EqualTo("파티 체력 120 → 0"));
            Assert.That(defeat.Last().Text, Is.EqualTo("전투 종료 · 패배"));
            Assert.That(defeat.Any(r => r.Kind == BattleDigestKind.EnemyState), Is.False);
        }

        [Test]
        public void EndTurnEventsKeepCoreChronologicalOrder()
        {
            var before = Field(null, new List<BattleDigestReservation> { Reserved("c9", "시한 포격", 4, "next") });
            var events = new List<BattleEvent>
            {
                new BattleEvent { E = "restraintKept" },
                new BattleEvent { E = "hpLoss", N = 2, Source = "smudge" },
                new BattleEvent { E = "hit", Uid = "e1", Damage = 12, Blocked = 4 },
                new BattleEvent { E = "hpLoss", N = 8, Source = "enemy" },
                Dmg("e1", 3, 0, "counter"),
                new BattleEvent { E = "kill", Uid = "e1", Id = "e_moth" },
                new BattleEvent { E = "stunSkip", Uid = "e2" },
                Dmg("e2", 4, 0, "burn"),
                new BattleEvent { E = "recover", Uid = "e2" },
                new BattleEvent { E = "repay", N = 1 },
                Dmg("e2", 22, 0, "card", "sera_timed"),
                new BattleEvent { E = "break", Uid = "e2" },
                new BattleEvent { E = "resolved", Uid = "c9", Id = "sera_timed", Reserved = "next", Fizzled = false },
                new BattleEvent { E = "echoCopy", Uid = "c30", Id = "sera_timed" },
                new BattleEvent { E = "turn", Turn = 4, Ap = 3 }
            };
            Assert.That(EventRows(BattleTurnDigest.Build(before, events, null)).Select(r => r.Item2), Is.EqualTo(new[]
            {
                "절제 유지",
                "파티 체력 -2 · 얼룩",
                "기억 나방의 공격 · 피해 12 (방어 4 막음)",
                "파티 체력 -8 · 적 공격",
                "기억 나방에게 피해 3 · 반격",
                "기억 나방 처치",
                "톱니 병정 · 균열로 행동하지 못함",
                "톱니 병정에게 피해 4 · 화상",
                "톱니 병정 · 균열 회복",
                "부채 상환 · 행동력 -1",
                "톱니 병정에게 피해 22 · 카드 「시한 포격」",
                "톱니 병정 · 균열!",
                "예약 해결 · 시한 포격 · 다음 턴 예약",
                "메아리 사본 · 시한 포격",
                "4턴 시작 · 행동력 3"
            }));
        }

        [Test]
        public void RowsAreReadOnlyAndDetachedFromLaterCallerMutationAndBuildDoesNotChangeItsInputs()
        {
            var before = Field(new Dictionary<string, string> { ["c14"] = "정조준" }, new List<BattleDigestReservation> { Reserved("c9", "시한 포격", 4, "next") });
            before.Hp = 50; before.Ap = 2;
            var after = new BattleDigestSnapshot { Turn = 4, Hp = 44, Ap = 3, Enemies = new List<BattleDigestEnemy> { Enemy("e1", "기억 나방", 10, 0, true, null), Enemy("e2", "톱니 병정", 24, 0, true, null) } };
            var events = new List<BattleEvent>
            {
                new BattleEvent { E = "play", Uid = "c14", Id = "sera_aim", Mode = "now", Cost = 1, Target = "e1" },
                Dmg("e1", 8, 0, "card", "sera_aim"),
                new BattleEvent { E = "resolved", Uid = "c9", Id = "sera_timed", Reserved = "next" }
            };
            string inputs = CanonicalState.Serialize(events.Select(e => (object?)e.ToJsonGraph()).ToList());
            var rows = BattleTurnDigest.Build(before, events, after);
            Assert.That(CanonicalState.Serialize(events.Select(e => (object?)e.ToJsonGraph()).ToList()), Is.EqualTo(inputs), "Build must not change the events");
            Assert.That(before.CardNames!["c14"], Is.EqualTo("정조준"));
            Assert.That(BattleTurnDigest.Build(before, events, after), Is.Not.SameAs(rows), "every call returns a new list");
            var expected = rows.Select(r => (r.Kind, r.Text, r.Provenance, r.IsReservedResolution)).ToList();

            var first = before.Enemies![0]; first.Name = "변조"; first.IntentText = "변조";
            before.CardNames!["c14"] = "변조"; before.Reservations!.Clear(); before.Hp = 1;
            after.Hp = 999; after.Enemies!.Clear();
            events[0].Uid = "zz"; events[1].N = 999; events.Clear();
            Assert.That(rows.Select(r => (r.Kind, r.Text, r.Provenance, r.IsReservedResolution)).ToList(), Is.EqualTo(expected), "rows keep no reference to the caller's DTOs, maps or events");
            Assert.That(expected.Select(r => r.Text), Has.Member("카드 사용 · 정조준 → 기억 나방 · 행동력 1"));
            Assert.That(expected.Select(r => r.Text), Has.Member("예약 해결 · 시한 포격 · 다음 턴 예약"));
            Assert.Throws<NotSupportedException>(() => ((IList<BattleDigestEntry>)rows).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<BattleDigestEntry>)rows).RemoveAt(0));
        }
    }
}
