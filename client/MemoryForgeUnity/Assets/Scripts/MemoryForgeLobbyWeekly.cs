#nullable enable
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    public sealed partial class MemoryForgeLobbyClient
    {
        string weeklyBoss = "b_bell";
        int weeklyDifficulty = 1;
        bool weeklyPractice;

        void OpenWeeklyBosses()
        {
            player.Stop();
            if (recruitmentSession == null) { OpenRecruitment(); return; }
            RenderWeeklyBosses(); ShowPanel(growthPanel);
        }

        void RenderWeeklyBosses()
        {
            var profile = AccountProgression;
            if (profile?.ActiveEncounter != null || recruitmentSession?.State.Pending != null || recruitmentSession?.State.OutboxCode != null)
            { RenderGrowth(); return; }
            ClearPanel(growthPanel);
            var wall = Box(growthPanel, "WeeklyBackdrop", 0, 0, 1600, 900, Color.white);
            wall.GetComponent<Image>().sprite = SpriteAt("Art/Backgrounds/bg_zone1_battle");
            Box(growthPanel, "WeeklyVeil", 0, 0, 1600, 900, new Color(0.025f, 0.04f, 0.075f, 0.84f));
            Label(growthPanel, "주간 보스", 56, 24, 1000, 64, 42, Brass, FontStyle.Bold);
            Button(growthPanel, "RefreshWeeklyProfile", "새로고침", 1060, 24, 236, 112, StartAccountProfileRefresh, Line, 24).interactable = !growthBusy && !accountBusy && !recruitmentBusy;
            Button(growthPanel, "CloseWeekly", "공방", 1320, 24, 224, 112, RenderGrowth, Line, 24).interactable = !growthBusy;
            var weekly = profile?.WeeklyBoss;
            if (profile == null || weekly == null || weekly.Bosses.Count == 0)
            {
                Label(growthPanel, "계정의 주간 도전 정보를 새로고침해주세요.", 96, 280, 1408, 120, 30, Ink);
                Button(growthPanel, "RefreshWeekly", "계정 새로고침", 480, 480, 640, 120, StartAccountProfileRefresh, Brass, 28).interactable = !growthBusy && !accountBusy && !recruitmentBusy;
                return;
            }
            Label(growthPanel, "AL " + profile.Account.Level + "  ·  주간 보상 " + weekly.Claimed + "/3  ·  월요일 05:00 초기화", 58, 108, 944, 36, 24, Memory);
            bool ready = !growthBusy && !accountBusy && !recruitmentBusy && recruitmentSession?.ProgressionNeedsRefresh == false;
            var boss = weekly.Bosses.FirstOrDefault(item => item.Id == weeklyBoss) ?? weekly.Bosses.First(); weeklyBoss = boss.Id;
            var difficulty = boss.Difficulties.FirstOrDefault(item => item.Difficulty == weeklyDifficulty) ?? boss.Difficulties.First(); weeklyDifficulty = difficulty.Difficulty;
            float height = Math.Max(116, TouchUnits()), pitch = height + 16, footerHeight = Math.Max(120, TouchUnits()), footerY = 884 - footerHeight;
            var list = ScrollArea(growthPanel, 56, 200, 456, 496, Math.Max(496, weekly.Bosses.Count * pitch));
            for (int n = 0; n < weekly.Bosses.Count; n++)
            {
                var row = weekly.Bosses[n];
                Button(list, "WeeklyBoss:" + row.Id, BossName(row.Id) + "\n" + (row.Available ? "도전 가능" : "AL " + row.UnlockLevel + " 해금"), 0, n * pitch, 440, height,
                    () => { weeklyBoss = row.Id; weeklyDifficulty = 1; RenderWeeklyBosses(); }, row.Id == weeklyBoss ? Brass : Line, 25).interactable = ready;
            }
            Label(growthPanel, BossName(boss.Id), 552, 200, 992, 56, 34, Brass, FontStyle.Bold);
            Label(growthPanel, "동료 3명 · 집중 소모 없음 · 보상 횟수는 모든 보스가 공유해요.", 554, 264, 988, 36, 22, Ink);
            for (int n = 0; n < boss.Difficulties.Count; n++)
            {
                var row = boss.Difficulties[n];
                Button(growthPanel, "WeeklyDifficulty:" + row.Difficulty, row.Difficulty + "단계", 552 + n * 198, 320, 184, height,
                    () => { weeklyDifficulty = row.Difficulty; RenderWeeklyBosses(); }, row.Difficulty == weeklyDifficulty ? Brass : Line, 24).interactable = ready;
            }
            float detailY = 320 + height + 16;
            Label(growthPanel, "권장 단계 " + difficulty.Stage + (difficulty.HpBps > 0 ? " · 체력 " + difficulty.HpBps / 100 + "% · 피해 " + difficulty.DmgBps / 100 + "%" : "")
                + "\n보상: 승급핵 2 · 공방화 20,000 · 보스 토큰 40 · 계정 XP 100\n" + (weekly.Remaining == 0 ? "이번 주 보상을 모두 받았어요. 전투는 계속할 수 있어요." : "이번 주 보상 " + weekly.Remaining + "회 남음"),
                554, detailY, 988, 96, 24, Ink);
            Button(growthPanel, "WeeklyPractice", weeklyPractice ? "연습 모드 ✓ · 보상 없음" : "보상 도전 · 주간 횟수 적용", 552, detailY + 108, 992, 112,
                () => { weeklyPractice = !weeklyPractice; RenderWeeklyBosses(); }, weeklyPractice ? Brass : Line, 26).interactable = ready;
            Button(growthPanel, "WeeklyEditParty", "출전 편성", 56, footerY, 440, footerHeight, OpenSortie, Line, 26).interactable = ready;
            Button(growthPanel, "WeeklyEquipment", "장비 공방", 552, footerY, 484, footerHeight, OpenEquipmentWorkshop, Line, 26).interactable = ready;
            Button(growthPanel, "StartWeeklyBoss", weeklyPractice ? "연습 출발" : weekly.Remaining == 0 ? "보상 없이 도전" : "보스 도전", 1060, footerY, 484, footerHeight,
                BeginWeeklyBoss, Brass, 30).interactable = ready && boss.Available && ownedParty.Count == 3
                    && (profile.ActiveSession == null || profile.ActiveSession.Phase == "DONE") && OwnedWeaponsReady(profile) && SupportFitsNextParty(profile);
            Label(growthPanel, growthBusy ? "서버에서 도전을 준비하고 있어요…" : growthNotice, 56, 176, 944, 24, 18, Memory);
        }

        void BeginWeeklyBoss()
        {
            var captured = recruitmentSession; var profile = captured?.Progression;
            if (captured == null || profile == null || growthBusy || accountBusy || recruitmentBusy) return;
            var task = captured.StartWeeklyBossAsync(profile.Revision, weeklyBoss, weeklyDifficulty, weeklyPractice, ownedParty.ToArray());
            growthBusy = true; growthNotice = ""; RenderWeeklyBosses();
            StartCoroutine(AwaitGrowthMutation(captured, "weekly", task));
        }

        string BossName(string id) => content.Battle.Enemies.FirstOrDefault(item => item.Id == id)?.Name ?? id;
    }
}
