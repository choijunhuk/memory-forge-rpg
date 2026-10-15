// §6/§14.3/§16.2: authored card effect text, ported verbatim from engine/rules-core.js opText/cardText and their maps.
// Reads only the supplied (possibly effective upgraded/resonance) definition; no state, RNG or actor-derived numbers.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MemoryForge.Core
{
    public static class BattleText
    {
        static readonly Dictionary<string, string> CounterName = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["echo"] = "잔향", ["exhausted"] = "소멸한 카드", ["debt"] = "부채", ["growthSum"] = "덱의 성장치 합", ["growthCards"] = "성장 카드 수",
            ["counter"] = "반격", ["missingHp10"] = "잃은 체력 10", ["contracts"] = "이번 턴 계약", ["tempParts"] = "붙인 임시 부품", ["block"] = "현재 방어",
            ["lastCount"] = "그 수", ["targetBurn"] = "대상 화상",
        };
        static readonly Dictionary<string, string> StatusName = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["vul"] = "취약", ["weak"] = "약화", ["mark"] = "표식", ["burn"] = "화상",
        };
        static readonly Dictionary<string, string> TargetName = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["one"] = "", ["same"] = "그 적에게 ", ["all"] = "모든 적에게 ", ["rand"] = "무작위 적에게 ", ["fixed"] = "표식 적에게 ",
        };

        public static string Card(BattleCardDef card, BattleData data)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (card.Effects == null) throw new ArgumentException("Card definition has no eff list", nameof(card));
            var parts = card.Effects.Select(e => Op(e, data)).Where(t => t.Length > 0).ToList();
            var keywords = card.Keywords ?? new List<string>();
            var kw = new List<string>();
            if (card.Reservation == "o") kw.Add("예약 전용"); else if (card.Reservation == "after") kw.Add("다다음 턴 예약 전용");
            else if (card.Reservation == "n" && !keywords.Contains("contract")) kw.Add("즉시 전용");
            if (keywords.Contains("contract")) kw.Add("계약");
            if (card.Growth == true) kw.Add("성장");
            if (keywords.Contains("exhaust")) kw.Add("소멸");
            if (Truthy(card.Uses)) kw.Add($"전투당 {Int(card.Uses)}회");
            if (card.CostIf != null) kw.Add($"{Condition(card.CostIf.Condition)} 비용 {Int(card.CostIf.Delta)}");
            if (!string.IsNullOrEmpty(card.Flavor) && parts.Count == 0) parts.Add(card.Flavor!);
            return (kw.Count > 0 ? $"[{string.Join("·", kw)}] " : "") + string.Join(". ", parts) + (parts.Count > 0 ? "." : "");
        }

        // Source returns undefined for a missing op name; both of its callers (filter/join) render that as "".
        public static string Op(EffectDef effect, BattleData data)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            if (data == null) throw new ArgumentNullException(nameof(data));
            var op = effect;
            switch (op.Op)
            {
                case "dmg":
                {
                    int hits = op.Hits ?? 1; string t = Target(op.Target) + "피해 ";
                    if (op.V == 0 && op.Scale != null) t += $"{Counter(op.Scale.Per)}×{Int(op.Scale.K)}{(op.Scale.Max.HasValue ? $"(최대 {Int(op.Scale.Max)})" : "")}";
                    else t += ValueText(op.V, op.Rv, op.Ra) + ScaleText(op.Scale);
                    if (op.HitsScale != null) t += $" ×({Int(hits)}+{Counter(op.HitsScale.Per)})회"; else if (hits > 1) t += $" ×{Int(hits)}";
                    if (Truthy(op.Pierce)) t += $", 관통 {Hundredth(op.Pierce)}%";
                    if (Truthy(op.Brk) && op.Brk != 1) t += $", 균열 {Int(op.Brk)}";
                    if (Truthy(op.Shock)) t += $", 충격 {Int(op.Shock)}";
                    if (op.Multipliers != null) t += string.Concat(op.Multipliers.Select(m => $". {Condition(m.Condition)} 피해 ×{Int(m.Multiplier)}"));
                    return t;
                }
                case "blk": return op.V == 0 && op.Scale != null ? $"방어 {Counter(op.Scale.Per)}×{Int(op.Scale.K)}" : $"방어 {ValueText(op.V, op.Rv, op.Ra)}{ScaleText(op.Scale)}";
                case "heal": return $"회복 {ValueText(op.V, op.Rv, op.Ra)}{ScaleText(op.Scale)}{(Truthy(op.OverflowBlock) ? ", 넘친 회복은 방어로" : "")}";
                case "st": return $"{Target(op.Target)}{Lookup(StatusName, op.Status) ?? "undefined"} {Int(op.N)}{(op.Rn.HasValue ? $" (예약 {Int(op.Rn)})" : "")}";
                case "buff": return $"{Buff(op)}{(Truthy(op.Turns) && op.Turns > 1 ? $" ({Int(op.Turns)}턴)" : "")}";
                case "counter": return $"반격 {Int(op.N)}";
                case "draw":
                    return op.Scale != null && !Truthy(op.N)
                        ? $"{Counter(op.Scale.Per)}만큼 카드 뽑기{(op.Scale.Max.HasValue ? $"(최대 {Int(op.Scale.Max)})" : "")}"
                        : $"카드 {Int(op.N)}장 뽑기{(op.Rn.HasValue ? $" (예약 {Int(op.Rn)}장)" : "")}";
                case "discard": return $"카드 {Int(op.N)}장 버리기";
                case "ap": return $"행동력 +{Int(op.N)}";
                case "apNext": return $"다음 턴 행동력 +{Int(op.N)}";
                case "gauge": return $"{(op.Who == "all" ? "모든 캐릭터" : "소유자")} 필살기 게이지 +{Int(op.N)}";
                case "create": return $"손에 「{CardName(data, op.Card)}」 {Int(op.N)}장 추가";
                case "pull": return "예약 1개를 1턴 앞당김(이번 턴에 도착하면 즉시 해결)";
                case "slot":
                    return string.Join(", ", new[] { Truthy(op.Next) ? $"이번 턴 다음 턴 예약 칸 +{Int(op.Next)}" : "", Truthy(op.After) ? $"다다음 턴 예약 칸 +{Int(op.After)}" : "" }.Where(s => s.Length > 0));
                case "grow": return "성장";
                case "growPick": return $"손의 성장 카드 1장 성장 +{Int(op.N)}";
                case "growLowest": return $"성장이 가장 낮은 성장 카드 {Int(op.N)}장 성장 +1";
                case "growAll": return "모든 성장 카드 성장 +1";
                case "loan": return $"행동력 +{Int(op.N)}, 다음 턴 행동력 -{Int(op.N)}(부채 {Int(op.N)})";
                case "defer": return "다음 턴 부채 1개를 1턴 미루고 금액 +1(재유예 불가)";
                case "restraint": return "이번 턴 카드를 3장 이하로 쓰면 다음 턴 첫 예약 피해·방어 +30%. 4장째에 위약: 다음 턴 드로우 -1";
                case "collateral": return $"다음 턴 예약 칸 1개 봉인(다음 내 턴 종료까지), 방어 {Int(op.V)}";
                case "selfdmg": return $"과부하 {Int(op.V)}";
                case "cleanse": return "해로운 효과 모두 제거";
                case "cleanseOne": return $"{Lookup(StatusName, op.Status) ?? Text(op.Status)} 제거";
                case "exhaustPick": return $"{(op.From == "discard" ? "버린 카드" : "손")}에서 카드 {Int(op.N)}장 소멸";
                case "exhaustHand": return "손패를 모두 소멸";
                case "tune":
                {
                    string where = op.Pick == "all" ? "손의 모든 카드" : op.Pick == "same" ? "그 카드" : op.Pick == "random" ? "손의 무작위 카드 1장" : "손의 카드 1장";
                    return $"{where}에 임시 회로 「{PartName(data, op.Part)}」 부착";
                }
                case "costdown": return $"{(op.Pick == "all" ? "이번 턴 손패의 모든 카드" : "손의 카드 1장")} 비용 -{Int(op.N)}(이번 턴)";
                case "recall": return "버린 카드 1장을 손으로";
                case "copyPick": return "손의 성장 카드 1장의 비용 0 소멸 사본 추가";
                case "power": return Power(op);
                case "echo": return $"잔향 +{Int(op.N)}";
                case "nextBoost": return $"이번 턴 다음에 내는 다른 캐릭터 카드 피해·방어 +{Hundredth(op.Bps)}%";
                case "freeNext": return $"이번 턴 다음에 내는 카드 {Int(op.N)}장 비용 0";
                case "clearDebt": return "모든 부채 제거";
                case "breakBonus": return op.Scale != null ? $"균열 +{Counter(op.Scale.Per)}" : $"균열 +{Int(op.N)}";
                case "breakBonusAll": return $"모든 적 균열 +{Lookup(CounterName, op.Scale?.Per) ?? Int(op.N)}";
                case "if":
                {
                    // Source dereferences cond/then unconditionally; a definition without them is a TypeError there, not text.
                    if (op.Then == null) throw new ArgumentException("if effect has no then list", nameof(effect));
                    string t = $"{Condition(op.Condition)} {string.Join(", ", op.Then.Select(o => Op(o, data)))}";
                    if (op.Else != null && op.Else.Count > 0) t += $", 아니면 {string.Join(", ", op.Else.Select(o => Op(o, data)))}";
                    return t;
                }
                default: return op.Op ?? "";
            }
        }

        static string Condition(EffectConditionDef? c)
        {
            if (c == null) throw new ArgumentException("Effect condition is missing");
            switch (c.Condition)
            {
                case "marked": return "대상에게 표식이 있으면";
                case "burning": return "대상이 화상 상태면";
                case "broken": return "대상이 균열 상태면";
                case "debt": return "부채가 있으면";
                case "modded": return "이 카드에 개조가 있으면";
                case "reserved": return "예약으로 해결되면";
                case "intentAttack": return "대상이 공격할 예정이면";
                case "echo": return $"잔향 {Int(c.N)} 이상이면";
                case "hpBelow": return $"파티 체력 {Int(c.Pct)}% 이하면";
                case "targetHpBelow": return $"대상 체력 {Int(c.Pct)}% 이하면";
                case "growthInHand": return $"손에 성장 카드가 {Int(c.N)}장 이상이면";
                case "exhaustedAtLeast": return $"소멸한 카드가 {Int(c.N)}장 이상이면";
                case "killedThis": return "이 카드로 적을 처치하면";
                default: return Text(c.Condition);
            }
        }

        static string Buff(EffectDef op)
        {
            switch (op.Status)
            {
                case "charge": return $"다음 공격 카드 피해 +{Hundredth(op.N)}%";
                case "seraLock": return $"세라의 예약 공격 피해 +{Hundredth(op.N)}%";
                case "healUp": return $"다음 회복 +{Hundredth(op.N)}%";
                case "lumiRush": return $"이번 턴 루미의 공격 피해 +{Hundredth(op.N)}%";
                case "counterDouble": return "반격 피해 2배";
                case "overloadFree": return "이번 턴 과부하 체력 대가 0";
                default: return Text(op.Status);
            }
        }

        static string Power(EffectDef p)
        {
            switch (p.Id)
            {
                case "blueprint": return $"이번 전투 동안 개조된 카드의 피해·방어 +{Hundredth(p.Bps)}%";
                case "greenhouse": return $"이번 전투 동안 성장 카드가 해결될 때 방어 {Int(p.Block)}";
                case "bulwark": return $"이번 전투 동안 턴이 바뀌어도 방어를 최대 {Int(p.Keep)} 유지";
                case "rebirth": return $"이번 전투 동안 턴 시작 시 회복 {Int(p.Heal)}";
                case "oblivion": return $"이번 전투 동안 카드가 소멸할 때마다 방어 {Int(p.Block)}";
                case "casefile": return $"이번 전투 동안 표식 적에게 주는 피해 +{Hundredth(p.Bps)}%";
                case "echochamber": return "이번 전투 동안 잔향이 3이 되면 카드 1장 뽑기(턴당 1회)";
                default: return Text(p.Id);
            }
        }

        static string ScaleText(EffectScaleDef? sc) =>
            sc == null ? "" : $" +{Int(sc.K)}×{Lookup(CounterName, sc.Per) ?? Text(sc.Per)}{(sc.Max.HasValue ? $"(최대 +{Int(sc.Max)})" : "")}";

        static string ValueText(int? v, int? rv, int? ra)
        {
            string t = Int(v);
            if (rv.HasValue && ra.HasValue && ra != rv) t += $" (예약 {Int(rv)} / 다다음 {Int(ra)})";
            else if (rv.HasValue && rv != v) t += $" (예약 {Int(rv)})";
            else if (ra.HasValue && ra != v) t += $" (다다음 {Int(ra)})";
            return t;
        }

        // ix.cards[id]?.name || id and ix.parts[id]?.name || id: an empty name falls back to the id.
        static string CardName(BattleData data, string? id)
        {
            string key = Text(id);
            BattleCardDef? found = data.CardIndex.TryGetValue(key, out var indexed) ? indexed : data.Cards.LastOrDefault(c => c.Id == key);
            return string.IsNullOrEmpty(found?.Name) ? Text(id) : found!.Name;
        }
        static string PartName(BattleData data, string? id)
        {
            string key = Text(id);
            BattlePartDef? found = data.PartIndex.TryGetValue(key, out var indexed) ? indexed : data.Parts.LastOrDefault(p => p.Id == key);
            return string.IsNullOrEmpty(found?.Name) ? Text(id) : found!.Name;
        }

        static string Target(string? target) => Lookup(TargetName, string.IsNullOrEmpty(target) ? "one" : target) ?? "undefined";
        static string Counter(string? per) => Lookup(CounterName, per) ?? "undefined";
        static string? Lookup(Dictionary<string, string> map, string? key) => map.TryGetValue(Text(key), out var value) ? value : null;
        static bool Truthy(int? value) => value.HasValue && value.Value != 0;
        // JS template literals: an absent number or string renders as "undefined".
        static string Text(string? value) => value ?? "undefined";
        static string Int(int? value) => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "undefined";
        // Number#toString of an integer / 100 (absent → NaN); exact for every int, so no runtime double formatting is involved.
        static string Hundredth(int? value)
        {
            if (!value.HasValue) return "NaN";
            long x = value.Value, a = Math.Abs(x), whole = a / 100, fraction = a % 100;
            string sign = x < 0 ? "-" : "", head = sign + whole.ToString(CultureInfo.InvariantCulture);
            return fraction == 0 ? head : head + "." + fraction.ToString("00", CultureInfo.InvariantCulture).TrimEnd('0');
        }
    }
}
