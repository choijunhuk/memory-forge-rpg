#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using MemoryForge.Core;

namespace MemoryForge.Client
{
    // P1-CARD-FLOW-TURN-DIGEST §14.3/§16.4: the text of a turn-result summary shown over an already committed local practice view.
    // Build reads only public snapshot DTOs the caller projects before and after one committed command plus that command's own Core
    // events, and returns detached read-only rows. It calls no Run or Battle, keeps no input, owns no timer and decides nothing about
    // play. An enemy's previous intent is labelled 직전 예고 and never reported as executed; only events state what happened, in Core's
    // order. Enemy moves Core emits no event for (def, buff, reflect, summon…) show only through the before/after public values.

    /// <summary>Public battle values projected at one moment. Null means "not projected"; draw order, RNG and private cards never belong here.</summary>
    public sealed class BattleDigestSnapshot
    {
        public int? Turn, Hp, Block, Ap;
        public string? Phase;
        public List<BattleDigestEnemy>? Enemies;
        public List<BattleDigestReservation>? Reservations;
        /// <summary>Display names by card instance uid: public hand and reservation cards and the card uids the command's events name.</summary>
        public Dictionary<string, string>? CardNames;
    }
    public sealed class BattleDigestEnemy
    {
        public string? Uid, Name;
        public int? Hp, Block;
        public bool Alive;
        /// <summary>The intent display text shown before the command: an announcement, not an executed action.</summary>
        public string? IntentText;
    }
    /// <summary>A public time reservation (next/after turn). Input or presentation waits are never one of these.</summary>
    public sealed class BattleDigestReservation
    {
        public string? Uid, Name, Kind;
        public int? Due;
    }

    public enum BattleDigestKind
    {
        Summary, PreviousIntent, EnemyState,
        CardPlayed, ReservationPlayed, ReservationRegistered, CardResolved, ReservationResolved, Fizzle, Copy, Exhaust,
        CardDamage, UltimateDamage, CounterDamage, RelicDamage, BurnDamage, EffectDamage,
        EnemyAttack, EnemyAction, EnemyStatus, Break, Kill, PartyHpLoss, PartyBlock, PartyHeal, Repay, TurnStart, BattleEnd, Other
    }

    public sealed class BattleDigestEntry
    {
        public BattleDigestKind Kind { get; }
        public string Text { get; }
        /// <summary>Korean label of the Core source of a damage, HP-loss or block row (카드, 반격, 유물, 반사…); empty otherwise.</summary>
        public string Provenance { get; }
        /// <summary>Only an actual time-reservation resolution (resolved with Reserved set): the row the original 0.3 s per card applies to.</summary>
        public bool IsReservedResolution => Kind == BattleDigestKind.ReservationResolved;
        internal BattleDigestEntry(BattleDigestKind kind, string text, string provenance) { Kind = kind; Text = text; Provenance = provenance; }
    }

    public static class BattleTurnDigest
    {
        const string Generic = "기타 전투 효과";

        /// <summary>Two summary rows (turn; party HP/block/AP/reservation count), the previous intent of each enemy alive before, one row
        /// per event in the given order, then the final public state of each enemy that changed. Missing fields and unknown events give
        /// generic rows instead of throwing. The result is a new read-only list of immutable rows.</summary>
        public static IReadOnlyList<BattleDigestEntry> Build(BattleDigestSnapshot? before, IEnumerable<BattleEvent?>? events, BattleDigestSnapshot? after)
        {
            var list = events == null ? new List<BattleEvent?>() : new List<BattleEvent?>(events);
            var names = new Names(before, after, list);
            var rows = new List<BattleDigestEntry> { Header(before, after), Party(before, after) };
            var enemies = before?.Enemies;
            if (enemies != null)
                foreach (var enemy in enemies)
                    if (enemy != null && enemy.Alive)
                        rows.Add(Row(BattleDigestKind.PreviousIntent, names.Enemy(enemy) + (string.IsNullOrEmpty(enemy.IntentText) ? " · 직전 예고 정보 없음" : " · 직전 예고 · " + enemy.IntentText)));
            foreach (var e in list) rows.Add(Line(e, names));
            Results(before, after, names, rows);
            return Array.AsReadOnly(rows.ToArray());
        }

        static BattleDigestEntry Header(BattleDigestSnapshot? before, BattleDigestSnapshot? after)
        {
            int? from = before?.Turn, to = after?.Turn;
            string text = "턴 결과";
            if (from.HasValue) text += " · " + N(from) + "턴" + (to.HasValue && to.Value != from.Value ? " → " + N(to) + "턴" : "");
            else if (to.HasValue) text += " · " + N(to) + "턴";
            string? phase = after?.Phase;
            if (phase == "VICTORY") text += " · 전투 승리";
            else if (phase == "DEFEAT") text += " · 전투 패배";
            return Row(BattleDigestKind.Summary, text);
        }
        static BattleDigestEntry Party(BattleDigestSnapshot? before, BattleDigestSnapshot? after)
        {
            var parts = new List<string>();
            Pair(parts, "파티 체력", before?.Hp, after?.Hp, false);
            Pair(parts, "방어", before?.Block, after?.Block, false);
            Pair(parts, "행동력", before?.Ap, after?.Ap, false);
            var from = before?.Reservations; var to = after?.Reservations;
            // RULES_NOW_PLAY: the player makes no reservation, so an empty count is not news.
            if (from != null && to != null && (from.Count != 0 || to.Count != 0)) parts.Add("시간 예약 " + Present(from) + "장 → " + Present(to) + "장");
            return Row(BattleDigestKind.Summary, parts.Count == 0 ? "파티 상태 정보 없음" : string.Join(" · ", parts));
        }
        // Final public state of each enemy that changed, appeared or fell; unchanged ones add nothing. Needs both projections.
        static void Results(BattleDigestSnapshot? before, BattleDigestSnapshot? after, Names names, List<BattleDigestEntry> rows)
        {
            var old = before?.Enemies; var current = after?.Enemies;
            if (old == null || current == null) return;
            foreach (var now in current)
            {
                if (now == null) continue;
                BattleDigestEnemy? was = null;
                foreach (var candidate in old) if (candidate != null && candidate.Uid != null && candidate.Uid == now.Uid) { was = candidate; break; }
                var parts = new List<string>();
                if (was == null) { parts.Add("새로 등장"); if (now.Hp.HasValue) parts.Add("체력 " + N(now.Hp)); }
                else
                {
                    Pair(parts, "체력", was.Hp, now.Hp, true); Pair(parts, "방어", was.Block, now.Block, true);
                    if (was.Alive && !now.Alive) parts.Add("처치됨");
                }
                if (parts.Count != 0) rows.Add(Row(BattleDigestKind.EnemyState, "결과 · " + names.Enemy(now) + " · " + string.Join(" · ", parts)));
            }
        }

        // One row per Core event, using only the fields that event carries.
        static BattleDigestEntry Line(BattleEvent? e, Names names)
        {
            if (e == null) return Row(BattleDigestKind.Other, Generic);
            string card = names.Card(e.Uid, e.Id), enemy = names.Enemy(e.Uid);
            switch (e.E)
            {
                case "play":
                    {
                        string text = card + (string.IsNullOrEmpty(e.Target) ? "" : " → " + names.Enemy(e.Target)), cost = e.Cost.HasValue ? " · 행동력 " + N(e.Cost) : "";
                        string? timing = e.Mode == "next" ? "다음 턴" : e.Mode == "after" ? "다다음 턴" : null;
                        return timing != null ? Row(BattleDigestKind.ReservationPlayed, "예약 사용 · " + text + " · " + timing + cost) : Row(BattleDigestKind.CardPlayed, "카드 사용 · " + text + cost);
                    }
                case "reserve":
                    return Row(BattleDigestKind.ReservationRegistered, "시간 예약 등록 · " + card + Suffix(Reservation(e.Kind)) + (e.Due.HasValue ? " · " + N(e.Due) + "턴 시작에 해결" : ""));
                case "resolved":
                    {
                        string fizzled = e.Fizzled == true ? " · 불발" : "";
                        return string.IsNullOrEmpty(e.Reserved) ? Row(BattleDigestKind.CardResolved, "효과 처리 끝 · " + card + fizzled)
                            : Row(BattleDigestKind.ReservationResolved, "예약 해결 · " + card + Suffix(Reservation(e.Reserved)) + fizzled);
                    }
                case "fizzle": return Row(BattleDigestKind.Fizzle, "불발 · " + card + " · 표식 대상 없음");
                case "echoCopy": return Row(BattleDigestKind.Copy, "메아리 사본 · " + card);
                case "exhaust": return Row(BattleDigestKind.Exhaust, "소멸 · " + card);
                case "dmg":
                    {
                        string head = enemy + "에게 피해" + Amount(e.N) + (e.Blocked > 0 ? " (방어 " + N(e.Blocked) + " 막힘)" : "");
                        switch (e.Source)
                        {
                            case "card":
                                {
                                    string? source = names.Definition(e.Card);
                                    return Row(BattleDigestKind.CardDamage, head + " · 카드" + (source == null ? "" : " 「" + source + "」"), "카드");
                                }
                            case "ult": return Row(BattleDigestKind.UltimateDamage, head + " · 필살기", "필살기");
                            case "counter": return Row(BattleDigestKind.CounterDamage, head + " · 반격", "반격");
                            case "relic": return Row(BattleDigestKind.RelicDamage, head + " · 유물", "유물");
                            case "burn": return Row(BattleDigestKind.BurnDamage, head + " · 화상", "화상");
                            default: return Row(BattleDigestKind.EffectDamage, head + " · 기타 효과", "기타 효과");
                        }
                    }
                case "hit":
                    return Row(BattleDigestKind.EnemyAttack, enemy + "의 공격" + (e.Damage.HasValue ? " · 피해 " + N(e.Damage) : "")
                        + (e.Blocked > 0 ? " (방어 " + N(e.Blocked) + " 막음)" : ""), "적 공격");
                case "hpLoss":
                    {
                        string label = HpLossSource(e.Source);
                        return Row(BattleDigestKind.PartyHpLoss, (e.N.HasValue ? "파티 체력 -" + N(e.N) : "파티 체력 감소") + " · " + label, label);
                    }
                case "block":
                    {
                        string label = BlockSource(e.Source);
                        return Row(BattleDigestKind.PartyBlock, (e.N.HasValue ? "방어 +" + N(e.N) : "방어 증가") + " · " + label, label);
                    }
                case "heal": return Row(BattleDigestKind.PartyHeal, e.N.HasValue ? "파티 체력 +" + N(e.N) : "파티 체력 회복");
                case "status": return Row(BattleDigestKind.EnemyStatus, enemy + " · " + StatusName(e.Status) + Plus(e.N));
                case "break": return Row(BattleDigestKind.Break, enemy + " · 균열!");
                case "kill": return Row(BattleDigestKind.Kill, enemy + " 처치");
                case "stunSkip": return Row(BattleDigestKind.EnemyAction, enemy + " · 균열로 행동하지 못함");
                case "charge": return Row(BattleDigestKind.EnemyAction, enemy + " · 힘을 모음");
                case "expose": return Row(BattleDigestKind.EnemyAction, enemy + " · 약점 노출");
                case "enemyHeal": return Row(BattleDigestKind.EnemyAction, enemy + " 체력 회복" + Plus(e.N));
                case "insert": return Row(BattleDigestKind.EnemyAction, "버린 카드 더미에 추가 · " + (names.Definition(e.Card) ?? "카드") + (e.N.HasValue ? " " + N(e.N) + "장" : ""));
                case "partyDebuff": return Row(BattleDigestKind.EnemyAction, "파티 " + StatusName(e.Status) + Plus(e.N));
                case "recover": return Row(BattleDigestKind.EnemyAction, enemy + " · 균열 회복");
                case "phase": return Row(BattleDigestKind.EnemyAction, enemy + " · 단계 전환");
                case "repay": return Row(BattleDigestKind.Repay, "부채 상환" + (e.N.HasValue ? " · 행동력 -" + N(e.N) : ""));
                case "turn": return Row(BattleDigestKind.TurnStart, (e.Turn.HasValue ? N(e.Turn) + "턴 시작" : "새 턴 시작") + (e.Ap.HasValue ? " · 행동력 " + N(e.Ap) : ""));
                case "end": return Row(BattleDigestKind.BattleEnd, e.Phase == "VICTORY" ? "전투 종료 · 승리" : e.Phase == "DEFEAT" ? "전투 종료 · 패배" : "전투 종료");
                case "envelope": return Row(BattleDigestKind.Other, "유물 효과 · 체력 1로 버팀", "유물");
                case "reshuffle": return Row(BattleDigestKind.Other, "버린 카드를 섞어 뽑을 더미로 되돌림");
                case "burnCard": return Row(BattleDigestKind.Other, "손패가 가득 차 버려짐 · " + card);
                case "grow": return Row(BattleDigestKind.Other, "성장 · " + card + (e.Growth.HasValue ? " · " + N(e.Growth) + "/3" : ""));
                case "tune": return Row(BattleDigestKind.Other, "임시 부품 부착 · " + card);
                case "pull": return Row(BattleDigestKind.Other, "예약 앞당김 · " + card);
                case "loan": return Row(BattleDigestKind.Other, "행동력 차입" + Plus(e.N));
                case "defer": return Row(BattleDigestKind.Other, "부채 상환 연기");
                case "power": return Row(BattleDigestKind.Other, "지속 효과 등록");
                case "restraintKept": return Row(BattleDigestKind.Other, "절제 유지");
                case "restraintBroken": return Row(BattleDigestKind.Other, "절제 깨짐");
                case "ult": return Row(BattleDigestKind.Other, "필살기 사용");
                default: return Row(BattleDigestKind.Other, Generic);
            }
        }

        static string HpLossSource(string? source)
        {
            switch (source)
            {
                case "enemy": return "적 공격";
                case "reflect": return "반사";
                case "overload": return "과부하";
                case "relic": return "유물";
                case "smudge": return "얼룩";
                default: return "기타";
            }
        }
        static string BlockSource(string? source)
        {
            switch (source)
            {
                case "card": case "collateral": return "카드";
                case "relic": case "moss": case "rod": return "유물";
                case "weapon": return "무기";
                case "part": return "부품";
                case "yuri": return "패시브";
                case "warmth": return "넘친 회복";
                case "greenhouse": case "oblivion": return "지속 효과";
                default: return "기타";
            }
        }
        static string StatusName(string? status) =>
            status == "vul" ? "취약" : status == "weak" ? "약화" : status == "mark" ? "표식" : status == "burn" ? "화상" : "상태";
        static string? Reservation(string? kind) => kind == "next" ? "다음 턴 예약" : kind == "after" ? "다다음 턴 예약" : null;
        static string Suffix(string? label) => label == null ? "" : " · " + label;
        static string Amount(int? value) => value.HasValue ? " " + N(value) : "";
        static string Plus(int? value) => value.HasValue ? " +" + N(value) : "";
        static void Pair(List<string> parts, string label, int? from, int? to, bool changedOnly)
        {
            if (!from.HasValue || !to.HasValue || changedOnly && from.Value == to.Value) return;
            parts.Add(label + " " + N(from) + " → " + N(to));
        }
        static int Present<T>(List<T> items) where T : class
        {
            int count = 0;
            foreach (var item in items) if (item != null) count++;
            return count;
        }
        static string N(int? value) => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";
        static BattleDigestEntry Row(BattleDigestKind kind, string text, string provenance = "") => new BattleDigestEntry(kind, text, provenance);

        // Display names copied out of the projections for one Build call.
        sealed class Names
        {
            readonly Dictionary<string, string> cards = new Dictionary<string, string>(StringComparer.Ordinal),
                definitions = new Dictionary<string, string>(StringComparer.Ordinal), enemies = new Dictionary<string, string>(StringComparer.Ordinal);
            internal Names(BattleDigestSnapshot? before, BattleDigestSnapshot? after, List<BattleEvent?> events)
            {
                foreach (var snapshot in new[] { before, after })
                {
                    if (snapshot == null) continue;
                    if (snapshot.CardNames != null) foreach (var pair in snapshot.CardNames) Add(cards, pair.Key, pair.Value);
                    if (snapshot.Reservations != null) foreach (var r in snapshot.Reservations) if (r != null) Add(cards, r.Uid, r.Name);
                    if (snapshot.Enemies != null) foreach (var x in snapshot.Enemies) if (x != null) Add(enemies, x.Uid, x.Name);
                }
                // A definition's name is known only from an event that states both a named card uid and that card's definition id.
                foreach (var e in events)
                    if (e != null && (e.E == "play" || e.E == "resolved" || e.E == "echoCopy") && e.Uid != null && cards.TryGetValue(e.Uid, out var name)) Add(definitions, e.Id, name);
            }
            internal string Card(string? uid, string? id) =>
                uid != null && cards.TryGetValue(uid, out var name) ? name : id != null && definitions.TryGetValue(id, out var source) ? source : "카드";
            internal string? Definition(string? id) => id != null && definitions.TryGetValue(id, out var name) ? name : null;
            internal string Enemy(string? uid) => uid != null && enemies.TryGetValue(uid, out var name) ? name : "적";
            internal string Enemy(BattleDigestEnemy enemy) => string.IsNullOrEmpty(enemy.Name) ? Enemy(enemy.Uid) : enemy.Name!;
            static void Add(Dictionary<string, string> map, string? key, string? value)
            {
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value) && !map.ContainsKey(key!)) map.Add(key!, value!);
            }
        }
    }
}
