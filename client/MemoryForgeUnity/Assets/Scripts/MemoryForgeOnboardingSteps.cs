#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using MemoryForge.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    // ONBOARDING-FIRST-RUN §14.5/§3.4: the order and wording of the first-run guide over the local expedition practice. §14.5 ①–④ (the
    // prologue's three battles and the boss) become lessons 1–4 of the practice's own battles, ⑤ is the first memory save and ⑥'s forge
    // (변주) introduction is shown at the first forge. Each step is one explanation sentence plus one thing to do on the real screen
    // ("설명 1문장 + 직접 해 보기", at most three lines). A step shows only while its control is actually drawn and usable, and is done
    // when the practice's public state shows the player did it (an explanation-only step: when acknowledged). Lessons 2–4 start in a
    // later battle than the one that finished the lesson they need, except the boss lesson, whose own battle is the boss. The
    // practice's encounters, numbers and rewards are not changed for the guide, and nothing here grants or prices anything: §14.5 ⑥'s
    // recruitment and tickets, ⑤'s character grants, contracts and the weekly boss do not exist in the practice and are not taught.
    // The memory-save guide cannot be skipped (§14.5).
    public sealed class OnboardingStep
    {
        public string Id = "";
        public int Lesson;          // 0: map, reward, forge, save; 1–4: §14.5's battles 1–3 and the boss
        public int Needs;           // the lesson that must be finished first, in an earlier battle (0: none)
        public bool OwnBattle;      // the boss is this lesson's own battle: it may follow the needed lesson there
        public bool Core;           // counts toward finishing its lesson
        public bool Ack;            // explanation only: done by '알겠어요'
        public bool Required;       // §14.5: the memory-save guide cannot be skipped
        public string[] After = Array.Empty<string>();
        public string Text = "";    // {0}: the caption of CaptionOf
        public string? Alt;         // shown, with AltTargets, instead of Text and Targets while UseAlt holds
        public string? CaptionOf;
        public string[] Targets = Array.Empty<string>(), AltTargets = Array.Empty<string>();
        public Func<OnboardingContext, bool> Show = _ => false;
        public Func<OnboardingContext, bool>? UseAlt;
        public Func<OnboardingContext, OnboardingMark, bool>? Done;
    }

    /// <summary>The public state when a step became the current one, so its Done can tell what the player changed since.</summary>
    public sealed class OnboardingMark
    {
        public string? Card;
        public object? Rows; // the action display open at the mark (a newer one is a newer command)
        public int Turn;
        public long Memory;
        public HashSet<string> Reserved = new HashSet<string>(StringComparer.Ordinal);             // reservation uids already on the timeline
        public Dictionary<string, int> ReadyGauges = new Dictionary<string, int>(StringComparer.Ordinal); // actors whose ultimate was usable
    }

    /// <summary>What a step reads: the practice's public state and the controls of its current view, by GameObject name ("Node:*" is a
    /// prefix). Reading never reaches the Run.</summary>
    public sealed class OnboardingContext
    {
        public readonly MemoryForgeExpeditionClient Client;
        readonly List<RectTransform> drawn = new List<RectTransform>();
        RectTransform? frame;
        public bool HandClicked; // a hand card was pressed since the current step began (the auto-selected card changes no state)

        public OnboardingContext(MemoryForgeExpeditionClient client) { Client = client; }
        internal void Index(RectTransform? view)
        {
            frame = view; drawn.Clear();
            if (view != null) view.GetComponentsInChildren(true, drawn);
        }

        public RunState? Run => Client.RecoveryRequired ? null : Client.Snapshot;
        public BattleState? Battle => Run?.Phase == "BATTLE" ? Run.Battle : null;
        public string? BattleKey => Battle == null ? null : Run!.RunId + "@" + Run.Position;
        /// <summary>A covering sheet (deck, log, card text, card pick, turn result, a battle's last action…) is open: its full-frame blocker.</summary>
        public bool Covered => frame != null && frame.Cast<Transform>().Any(t => t.gameObject.activeSelf && t.name.EndsWith("InputBlocker", StringComparison.Ordinal));
        public bool PlayerTurn => Battle?.Phase == "PLAYER" && !Covered && !Client.HasPreparedIntent;
        public bool InHand(string? uid) => uid != null && Battle != null && Battle.Zones.Hand.Contains(uid);
        public IEnumerable<RectTransform> Drawn(string name) => drawn.Where(r => r != null && r.gameObject.activeInHierarchy && Match(r.name, name));
        public Button? Ready(string name) => Drawn(name).Select(r => r.GetComponent<Button>()).FirstOrDefault(b => b != null && b.IsInteractable());
        public string Caption(string name) => Drawn(name).Select(r => r.GetComponentInChildren<Text>()?.text).FirstOrDefault(t => t != null) ?? "";
        static bool Match(string actual, string name) =>
            name.EndsWith("*", StringComparison.Ordinal) ? actual.StartsWith(name.Substring(0, name.Length - 1), StringComparison.Ordinal) : actual == name;
    }

    public static class OnboardingSteps
    {
        static bool TurnPassed(OnboardingContext c, OnboardingMark m) => c.Client.HasTurnSummary || c.Battle == null || c.Battle.Turn > m.Turn;
        static bool CanEndTurn(OnboardingContext c) => c.Battle != null && !c.Covered && !c.Client.HasPreparedIntent && c.Ready("EndTurn") != null;
        static bool Boss(OnboardingContext c) => c.Battle != null && c.Run!.BattleType == "boss";
        static bool Left(OnboardingContext c, string phase) => c.Run != null && c.Run.Phase != phase;
        // A command committed after the mark whose own action display shows a row of the given kinds.
        static bool Committed(OnboardingContext c, OnboardingMark m, params BattleDigestKind[] kinds) =>
            c.Client.HasActionPresentation && !ReferenceEquals(c.Client.ActionPresentationRows, m.Rows) && c.Client.ActionPresentationRows.Any(r => kinds.Contains(r.Kind));
        static bool Played(OnboardingContext c, OnboardingMark m) => Committed(c, m, BattleDigestKind.CardPlayed, BattleDigestKind.ReservationPlayed);
        // A reservation registered after the mark (of the given kind, or any): resolutions and discards never count.
        static bool Reserved(OnboardingContext c, OnboardingMark m, string? kind = null) =>
            c.Battle != null && c.Battle.Reservations.Any(r => !m.Reserved.Contains(r.Uid) && (kind == null || r.Kind == kind));

        /// <summary>The guide in order; the first step that is not done, whose prerequisites hold and whose Show holds is the one shown.</summary>
        public static readonly IReadOnlyList<OnboardingStep> All = new[]
        {
            new OnboardingStep
            {
                Id = "map", Core = true, Targets = new[] { "Node:*" },
                Text = "지도의 밝은 칸이 지금 갈 수 있는 곳이에요. 들어간 칸은 되돌릴 수 없어요. 칸 하나를 눌러 들어가 보세요.",
                Show = c => c.Run?.Phase == "MAP" && !c.Client.ShowingResult && !c.Covered, Done = (c, m) => Left(c, "MAP")
            },
            // ① 프롤로그 1전투: 카드 지금 사용만(행동력·손패 설명)
            new OnboardingStep
            {
                Id = "battle.hand", Lesson = 1, Core = true, Targets = new[] { "Hand" },
                Text = "아래 손패가 이번 턴에 쓸 카드예요. 카드에 적힌 행동력만큼 오른쪽 위 행동력이 줄어요. 카드 하나를 눌러 보세요.",
                Show = c => c.PlayerTurn && c.Client.SelectedMode == null,
                Done = (c, m) => c.HandClicked || c.Client.SelectedMode != null || c.Client.SelectedCard != m.Card
            },
            new OnboardingStep
            {
                Id = "battle.mode", Lesson = 1, Core = true, After = new[] { "battle.hand" }, Targets = new[] { "Mode:now" }, AltTargets = new[] { "Hand" },
                Text = "쓰는 방식을 골라요. '즉시'는 이번 턴에 바로 해결되고, 버튼에 비용이 적혀 있어요. '즉시'를 눌러 보세요.",
                Alt = "이 카드는 지금 바로 쓸 수 없어요. 손패에서 '즉시 사용'이나 '즉시 · 예약'이라고 적힌 다른 카드를 눌러 보세요.",
                Show = c => c.PlayerTurn && c.Client.SelectedCard != null, UseAlt = c => c.Client.SelectedMode != "now" && c.Ready("Mode:now") == null,
                Done = (c, m) => c.Client.SelectedMode == "now"
            },
            new OnboardingStep
            {
                Id = "battle.target", Lesson = 1, After = new[] { "battle.mode" }, Targets = new[] { "Enemy:*" },
                Text = "이 카드는 맞힐 적을 골라야 해요. 위의 적 칸을 누르면 테두리가 생기고 그 적이 대상이 돼요. 적 하나를 눌러 보세요.",
                Show = c => c.PlayerTurn && c.Client.SelectedMode != null && c.Client.Target == null && c.Caption("ConfirmCard") == "대상 선택",
                Done = (c, m) => c.Client.Target != null
            },
            new OnboardingStep
            {
                Id = "battle.confirm", Lesson = 1, Core = true, After = new[] { "battle.mode" }, Targets = new[] { "ConfirmCard" }, CaptionOf = "ConfirmCard",
                AltTargets = new[] { "Mode:*" },
                Text = "고르기만 해서는 아무것도 쓰지 않아요. 행동 안내 줄의 예상을 확인하고 '{0}' 버튼을 눌러 보세요.",
                Alt = "확정 버튼에 적힌 것부터 해요. 방식이나 대상을 고르면 확정 버튼이 '사용 확정'으로 바뀌어요.",
                Show = c => c.PlayerTurn && c.Client.SelectedCard != null, UseAlt = c => c.Ready("ConfirmCard") == null, Done = Played
            },
            new OnboardingStep
            {
                Id = "battle.endturn", Lesson = 1, Core = true, After = new[] { "battle.confirm" }, Targets = new[] { "EndTurn" },
                Text = "더 쓸 카드가 없으면 '턴 종료'를 눌러요. 적들이 '이번'에 적힌 행동을 하고 새 턴이 시작돼요.",
                Show = CanEndTurn, Done = TurnPassed
            },
            new OnboardingStep
            {
                Id = "battle.summary", Lesson = 1, Core = true, After = new[] { "battle.endturn" }, Targets = new[] { "ContinueTurnSummary" },
                Text = "적의 행동과 예약 해결 결과가 차례로 적혀요. 다 읽었으면 '계속'을 눌러 다음 턴을 시작해요.",
                Show = c => c.Client.HasTurnSummary, Done = (c, m) => !c.Client.HasTurnSummary
            },
            new OnboardingStep
            {
                Id = "reward", Targets = new[] { "RewardCards", "Proceed" }, CaptionOf = "Proceed",
                Text = "이겼어요. 보상 카드는 한 장만 덱에 넣을 수 있고, '카드 설명 보기'로 먼저 읽을 수 있어요. 다 골랐으면 '{0}' 버튼을 눌러요.",
                Show = c => c.Run?.Phase == "REWARD" && !c.Covered, Done = (c, m) => Left(c, "REWARD")
            },
            // ② 2전투: 다음 턴 예약 + 적 의도 2개 보기
            new OnboardingStep
            {
                Id = "battle.intent", Lesson = 2, Needs = 1, Core = true, Ack = true, Targets = new[] { "Enemy:*" },
                Text = "적 칸의 '이번'은 이 턴을 마칠 때 할 행동, '다음'은 그다음 턴에 할 행동이에요. 두 줄을 보고 막을지 공격할지 정해요.",
                Show = c => c.PlayerTurn
            },
            new OnboardingStep
            {
                Id = "reserve.next", Lesson = 2, Needs = 1, Core = true, After = new[] { "battle.intent" }, Targets = new[] { "Mode:next" }, AltTargets = new[] { "Hand" },
                Text = "'다음 턴'으로 쓰면 행동력은 지금 내고, 카드는 다음 턴이 시작될 때 해결돼요. '다음 턴' 버튼을 눌러 보세요.",
                Alt = "손패에서 '즉시 · 예약'이나 '예약 전용'이라고 적힌 카드를 누르면 '다음 턴' 예약 버튼이 나와요.",
                Show = c => c.PlayerTurn && c.Client.SelectedCard != null, UseAlt = c => c.Client.SelectedMode != "next" && c.Ready("Mode:next") == null,
                Done = (c, m) => c.Client.SelectedMode == "next"
            },
            new OnboardingStep
            {
                Id = "reserve.confirm", Lesson = 2, Needs = 1, Core = true, After = new[] { "reserve.next" }, Targets = new[] { "ConfirmCard" }, CaptionOf = "ConfirmCard",
                AltTargets = new[] { "Mode:next", "Hand" },
                Text = "예약도 '{0}' 버튼을 눌러야 아래 기억 시간표 칸에 들어가요. 눌러 보세요.",
                Alt = "예약하려면 카드를 고른 뒤 '다음 턴' 버튼을 먼저 눌러요. 그다음 확정하면 기억 시간표에 들어가요.",
                Show = c => c.PlayerTurn && c.Client.SelectedCard != null, UseAlt = c => c.Client.SelectedMode != "next" || c.Ready("ConfirmCard") == null,
                Done = (c, m) => Reserved(c, m, "next")
            },
            new OnboardingStep
            {
                Id = "reserve.timeline", Lesson = 2, Needs = 1, Core = true, After = new[] { "reserve.confirm" }, Targets = new[] { "Timeline", "EndTurn" },
                Text = "예약이 기억 시간표에 들어갔어요. '턴 종료'를 누르면 다음 턴이 시작될 때 자동으로 해결돼요. 눌러 보세요.",
                Show = c => CanEndTurn(c) && c.Battle!.Reservations.Any(r => r.Kind == "next"), Done = TurnPassed
            },
            // ③ 3전투: 약점·균열 + 필살기
            new OnboardingStep
            {
                Id = "battle.break", Lesson = 3, Needs = 2, Core = true, Ack = true, Targets = new[] { "Enemy:*", "Party" },
                Text = "적의 약점과 같은 속성 캐릭터의 카드로 때리면 균열 수치가 줄어요. 0이 되면 그 적의 다음 행동이 취소되고 받는 피해가 25% 늘어요.",
                Show = c => c.PlayerTurn
            },
            new OnboardingStep
            {
                Id = "battle.ult", Lesson = 3, Needs = 2, Core = true, After = new[] { "battle.break" }, Targets = new[] { "Ult:*" },
                Text = "캐릭터 게이지가 가득 차면 '필살기'를 쓸 수 있어요. 대상이 필요하면 먼저 적을 골라요. '필살기'를 눌러 보세요.",
                Show = c => CanEndTurn(c) && c.Ready("Ult:*") != null,
                Done = (c, m) => Committed(c, m, BattleDigestKind.UltimateDamage)
                    || (c.Battle != null && c.Battle.Actors.Any(a => m.ReadyGauges.TryGetValue(a.Id, out int gauge) && a.Gauge < gauge))
            },
            // ④ 보스: 다다음 턴 예약 + 균열 타이밍 맞추기
            new OnboardingStep
            {
                Id = "boss.timing", Lesson = 4, Needs = 3, OwnBattle = true, Core = true, Ack = true, Targets = new[] { "Enemy:*", "Timeline" },
                Text = "보스전이에요. 균열이 걸린 다음 턴이 시작될 때 예약이 해결되면 늘어난 피해를 온전히 받아요. 균열 수치를 보고 시점을 맞춰 보세요.",
                Show = c => c.PlayerTurn && Boss(c)
            },
            new OnboardingStep
            {
                Id = "boss.after", Lesson = 4, Needs = 3, OwnBattle = true, After = new[] { "boss.timing" }, Targets = new[] { "Mode:after", "ConfirmCard" }, AltTargets = new[] { "Hand" },
                CaptionOf = "ConfirmCard",
                Text = "'다다음 턴'은 두 턴 뒤가 시작될 때 해결돼요. 균열 타이밍에 맞춰 '다다음 턴'을 고르고 '{0}' 버튼으로 예약해 보세요.",
                Alt = "손패의 카드를 눌러 '다다음 턴' 버튼이 있는지 확인해 보세요. 두 턴 뒤에 해결되는 예약이에요.",
                Show = c => c.PlayerTurn && Boss(c) && c.Client.SelectedCard != null, UseAlt = c => c.Client.SelectedMode != "after" && c.Ready("Mode:after") == null,
                Done = (c, m) => Reserved(c, m, "after")
            },
            // ⑥ (in the practice the forge is on the first zone's map): 공방(변주) 소개
            new OnboardingStep
            {
                Id = "forge", Targets = new[] { "ForgeVariation", "ForgeRemove", "ForgePart:*" },
                Text = "공방이에요. '카드 변주'는 카드 한 장에 부품을 붙이고, '카드 제거'는 덱에서 카드를 빼요. 필요 없으면 '떠나기'를 눌러요.",
                Show = c => c.Run?.Phase == "FORGE" && !c.Covered, Done = (c, m) => Left(c, "FORGE")
            },
            // ⑤ 첫 기억 저장(용량 안내) — cannot be skipped
            new OnboardingStep
            {
                Id = "save", Core = true, Required = true, Targets = new[] { "Memory:*", "CommitMemory" },
                Text = "탐사가 끝났어요. 남길 변화를 고르고 캐릭터마다 결과 용량 안에서 저장 버튼을 눌러요. 저장하지 않고 닫으면 이번 변화는 기억에 남지 않아요.",
                Show = c => c.Drawn("CommitMemory").Any() && !c.Covered, Done = (c, m) => c.Client.SessionMemoryVersion > m.Memory
            },
        };
    }
}
