#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    public sealed partial class MemoryForgeLobbyClient
    {
        RectTransform growthPanel = null!;
        string growthDungeon = "dg_ink", growthCharacter = "", growthNotice = "";
        int growthTier = 1;
        bool growthBusy;
        PracticeCharacterLevelPreview? levelPreview;

        void BuildGrowth()
        {
            growthPanel = Box(root, "GrowthPanel", 0, 0, 1600, 900, Background);
            growthPanel.gameObject.SetActive(false);
        }

        void OpenGrowth()
        {
            player.Stop();
            if (recruitmentSession == null) { OpenRecruitment(); return; }
            RenderGrowth(); ShowPanel(growthPanel);
        }

        bool CanContinueGrowth => !leaving && !growthBusy && !recruitmentBusy && !accountBusy && recruitmentSession?.ProgressionNeedsRefresh == false
            && recruitmentSession.State.Pending == null && recruitmentSession.State.OutboxCode == null && recruitmentSession.Progression?.ActiveEncounter != null;

        void GrowthRefreshButton(float x = 56, float y = 176, float width = 944) => Button(growthPanel, "RefreshGrowthAccount", "계정 새로고침", x, y, width, Math.Max(112, TouchUnits()),
            StartAccountProfileRefresh, Line, 24).interactable = !growthBusy && !recruitmentBusy && !accountBusy;

        void RenderGrowth()
        {
            ClearPanel(growthPanel);
            var background = Box(growthPanel, "TrainingWorkshop", 0, 0, 1600, 900, new Color(0.5f, 0.55f, 0.62f));
            background.GetComponent<Image>().sprite = SpriteAt("Art/Backgrounds/bg_workshop");
            Box(growthPanel, "TrainingShade", 0, 0, 1600, 900, new Color(0.025f, 0.04f, 0.075f, 0.76f));
            Label(growthPanel, "성장 공방", 56, 24, 1000, 64, 42, Ink, FontStyle.Bold);
            Button(growthPanel, "CloseGrowth", "홈", 1392, 24, 152, 112, ClosePanels, Line, 24).interactable = !growthBusy;
            var profile = AccountProgression;
            if (profile?.Resources == null || profile.Focus == null || profile.Dungeons == null)
            {
                Label(growthPanel, "계정 진행을 확인하면 성장 공방을 사용할 수 있어요.\n첫 기억과 1장을 마친 뒤 AL5에서 훈련이 열립니다.", 96, 240, 1408, 152, 30, Ink);
                GrowthRefreshButton(96, 480);
                return;
            }
            Label(growthPanel, "AL " + profile.Account.Level + "   ·   집중 " + profile.Focus.Value + "/" + profile.Focus.Cap
                + "   ·   6분마다 1 회복", 58, 108, 1260, 36, 24, Memory);
            Ornament(growthPanel, 58, 164, 480);
            var pending = recruitmentSession?.State.Pending;
            if (pending != null || recruitmentSession?.State.OutboxCode != null)
            {
                Label(growthPanel, "미확정 요청을 먼저 확인해주세요.\n보낸 요청과 마지막 계정 정보는 보존되어 있어요.", 96, 288, 1408, 144, 30, Ink);
                bool equipment = pending != null && PracticeCommandOutbox.IsEquipmentRoute(pending.Route), support = pending != null && PracticeCommandOutbox.IsSupportSelectionRoute(pending.Route);
                Button(growthPanel, "RetryGrowthPending", equipment ? "장비 공방에서 같은 요청 확인" : support ? "지원 설정에서 같은 요청 확인" : pending != null && PracticeCommandOutbox.IsGrowthRoute(pending.Route) ? "같은 요청 확인" : "모집에서 이전 요청 확인", 400, 528, 800, 120,
                    () => { if (equipment) OpenEquipmentWorkshop(); else if (support) OpenSupportSelection(); else if (pending != null && PracticeCommandOutbox.IsGrowthRoute(pending.Route)) BeginGrowthAction("retry"); else OpenRecruitment(); }, Brass, 28).interactable = !growthBusy && !recruitmentBusy;
                return;
            }
            if (profile.ActiveEncounter != null)
            {
                Label(growthPanel, "진행 중인 훈련이 있어요", 96, 256, 1408, 72, 36, Brass, FontStyle.Bold);
                Label(growthPanel, (profile.ActiveEncounter.Kind == "weeklyBoss" ? BossName(profile.ActiveEncounter.BossId!) + " · " + profile.ActiveEncounter.Difficulty
                    : DungeonName(profile.ActiveEncounter.DungeonId) + " · " + profile.ActiveEncounter.Tier) + "단계\n공방에 돌아와도 전투는 보존됩니다.", 96, 360, 1408, 104, 28, Ink);
                Button(growthPanel, "ContinueGrowthEncounter", "훈련 계속", 840, 592, 704, 120, EnterGrowthScene, Brass, 32).interactable = CanContinueGrowth;
                GrowthRefreshButton(96, 744);
                Button(growthPanel, "OpenCharacterGrowth", "동료 성장", 96, 592, 704, 120, ShowCharacterGrowth, Line, 28).interactable = !growthBusy && !recruitmentBusy && !accountBusy;
                Label(growthPanel, growthNotice, 96, 480, 1408, 64, 23, Memory);
                return;
            }
            bool ready = !growthBusy && !accountBusy && !recruitmentBusy && recruitmentSession?.ProgressionNeedsRefresh == false
                && recruitmentSession.State.Pending == null && recruitmentSession.State.OutboxCode == null;
            var source = profile.Dungeons.FirstOrDefault(item => item.Id == growthDungeon) ?? profile.Dungeons.First();
            growthDungeon = source.Id;
            var tier = source.Tiers.FirstOrDefault(item => item.Tier == growthTier) ?? source.Tiers.First(); growthTier = tier.Tier;
            float rowHeight = Math.Max(116, TouchUnits()), pitch = rowHeight + 16;
            float footerHeight = Math.Max(120, TouchUnits()), footerY = 884 - footerHeight;
            float tierHeight = Math.Max(96, TouchUnits()), rewardY = 320 + tierHeight + 16;
            var list = ScrollArea(growthPanel, 56, 200, 456, 496, Math.Max(496, profile.Dungeons.Count * pitch));
            for (int n = 0; n < profile.Dungeons.Count; n++)
            {
                var item = profile.Dungeons[n];
                Button(list, "GrowthDungeon:" + item.Id, DungeonName(item.Id) + "\n" + (item.Available ? "집중 " + item.Cost : "AL " + item.UnlockLevel + " 해금"), 0, n * pitch, 440, rowHeight,
                    () => { growthDungeon = item.Id; growthTier = 1; levelPreview = null; RenderGrowth(); }, item.Id == growthDungeon ? Brass : Line, 25).interactable = ready;
            }
            Label(growthPanel, DungeonName(source.Id), 552, 200, 456, 56, 34, Brass, FontStyle.Bold);
            Label(growthPanel, "1웨이브 · 패배 시 집중 소모 없음", 554, 264, 456, 36, 20, Ink);
            for (int n = 0; n < source.Tiers.Count; n++)
            {
                var item = source.Tiers[n];
                Button(growthPanel, "GrowthTier:" + item.Tier, item.Tier + "단계", 552 + n * 164, 320, 152, tierHeight,
                    () => { growthTier = item.Tier; RenderGrowth(); }, item.Tier == growthTier ? Brass : Line, 23).interactable = ready;
            }
            Label(growthPanel, "권장 단계 " + tier.Stage + " · 기록 " + tier.BestStars + "/3\n보상  " + string.Join(" · ", tier.Reward.Resources.Where(item => item.Value > 0).Select(item => MemoryForgeGrowthClient.MaterialName(item.Key) + " " + item.Value))
                + "\n계정 경험치 +" + tier.Reward.AccountXp, 554, rewardY, 988, 108, 24, Ink);
            Label(growthPanel, "현재 편성  " + string.Join(" / ", ownedParty.Select(id => content.Battle.Characters.FirstOrDefault(item => item.Id == id)?.Name ?? id)), 554, rewardY + 128, 988, 44, 24, Memory);
            Button(growthPanel, "EditGrowthParty", "편성 변경", 1320, 176, 224, 112, OpenSortie, Line, 25).interactable = ready;
            Button(growthPanel, "OpenWeeklyBosses", "주간 보스", 1060, 176, 236, 112, OpenWeeklyBosses, Line, 25).interactable = ready;
            bool noRun = profile.ActiveSession == null || profile.ActiveSession.Phase == "DONE";
            Button(growthPanel, "StartGrowthEncounter", "훈련 출발", 1060, footerY, 484, footerHeight, () => BeginGrowthAction("start"), Brass, 30)
                .interactable = ready && source.Available && ownedParty.Count == 3 && OwnedWeaponsReady(profile) && SupportFitsNextParty(profile) && profile.Focus.Value >= source.Cost && noRun;
            Button(growthPanel, "SweepGrowthEncounter", "1회 소탕 · 집중 " + source.Cost, 552, footerY, 484, footerHeight, () => BeginGrowthAction("sweep"), Line, 24)
                .interactable = ready && source.Available && tier.SweepAvailable && profile.Focus.Value >= source.Cost && noRun;
            if (recruitmentSession?.ProgressionNeedsRefresh == true) GrowthRefreshButton(56, footerY, 440);
            else Button(growthPanel, "OpenCharacterGrowth", "동료 성장", 56, footerY, 440, footerHeight, ShowCharacterGrowth, Line, 25).interactable = ready;
            Label(growthPanel, growthBusy ? "서버에서 확인하고 있어요…" : growthNotice, 56, 176, 944, 28, 18, Memory);
        }

        void ShowCharacterGrowth()
        {
            ClearPanel(growthPanel);
            var profile = AccountProgression;
            if (profile?.Resources == null) { RenderGrowth(); return; }
            if (levelPreview != null && (recruitmentSession?.ProgressionNeedsRefresh != false || levelPreview.Revision != profile.Revision)) levelPreview = null;
            var character = profile.Characters.FirstOrDefault(item => item.CharId == growthCharacter) ?? profile.Characters.FirstOrDefault();
            if (character == null) { RenderGrowth(); return; }
            growthCharacter = character.CharId;
            var paper = Box(growthPanel, "CharacterGrowthSheet", 32, 24, 1536, 852, Panel);
            Kit(paper, "Frame", "ui_panel_frame", new Vector4(48, 48, 48, 48), 0, 0, 1536, 852, 128);
            Label(paper, "동료 성장", 32, 24, 900, 64, 42, Brass, FontStyle.Bold);
            Button(paper, "CharacterEquipment", "성장 · 장비", 968, 16, 240, 112, () => OpenEquipmentFor(growthCharacter), Line, 24).interactable = !growthBusy && !recruitmentBusy && !accountBusy;
            Button(paper, "BackToTraining", "훈련 목록", 1264, 16, 240, 112, RenderGrowth, Line, 24).interactable = !growthBusy && !accountBusy && !recruitmentBusy;
            Label(paper, "잉크 XP " + profile.Resources["ink"] + "   ·   공방화 " + profile.Resources["credits"], 32, 112, 880, 44, 26, Memory);
            float rowHeight = Math.Max(116, TouchUnits()), pitch = rowHeight + 16;
            float actionHeight = Math.Max(120, TouchUnits()), actionY = 852 - 64 - actionHeight;
            var list = ScrollArea(paper, 32, 192, 440, 624, Math.Max(624, profile.Characters.Count * pitch));
            for (int n = 0; n < profile.Characters.Count; n++)
            {
                var owned = profile.Characters[n]; string id = owned.CharId;
                Button(list, "GrowthCharacter:" + id, (content.Battle.Characters.FirstOrDefault(item => item.Id == id)?.Name ?? id) + " · Lv." + owned.Level, 0, n * pitch, 424, rowHeight,
                    () => { growthCharacter = id; levelPreview = null; ShowCharacterGrowth(); }, id == growthCharacter ? Brass : Line, 26).interactable = !growthBusy && !accountBusy && !recruitmentBusy;
            }
            var figure = Box(paper, "GrowthCharacterArt", 496, 192, 440, 568, Color.white); figure.GetComponent<Image>().sprite = Portrait(character.CharId); CenterSprite(figure);
            if (figure.GetComponent<Image>().sprite == null) figure.GetComponent<Image>().color = Color.clear;
            Label(paper, "Lv." + character.Level + "  ·  공명 " + character.Res + "  ·  승급 " + character.Asc, 968, 192, 520, 56, 28, Ink);
            var preview = levelPreview;
            if (preview?.CharId == character.CharId && preview.Revision == profile.Revision)
            {
                Label(paper, "Lv." + preview.Before.Level + " → " + preview.After.Level + "\n체력 " + preview.Before.Stats.Hp + " → " + preview.After.Stats.Hp
                    + "\n위력 " + preview.Before.Stats.Power + " → " + preview.After.Stats.Power, 968, 280, 520, 160, 28, Brass);
                Label(paper, "필요 잉크 XP " + preview.CostInk + "\n필요 공방화 " + preview.CostCredits + "\n남은 XP 재료는 그대로 보존돼요.", 968, 464, 520, 116, 25, Ink);
                Button(paper, "CommitCharacterLevel", "레벨업", 968, actionY, 520, actionHeight, () => BeginGrowthAction("level"), Brass, 30).interactable = !growthBusy && !accountBusy && !recruitmentBusy && recruitmentSession?.ProgressionNeedsRefresh == false && preview.CanCommit;
                if (!preview.CanCommit) Label(paper, "자원 또는 승급 조건을 확인해주세요.", 968, 592, 520, 36, 22, Memory);
            }
            else
            {
                Label(paper, "필요 재료와 다음 전투의 능력치를\n먼저 확인할 수 있어요.", 968, 304, 520, 132, 28, Ink);
                Button(paper, "PreviewCharacterLevel", "다음 레벨 확인", 968, actionY, 520, actionHeight, PreviewGrowth, Brass, 28).interactable = !growthBusy && !accountBusy && !recruitmentBusy && recruitmentSession?.ProgressionNeedsRefresh == false;
            }
            Label(paper, growthBusy ? "서버에서 확인하고 있어요…" : growthNotice, 496, 796, 992, 36, 21, Memory);
            if (recruitmentSession?.ProgressionNeedsRefresh == true)
                Button(paper, "RefreshCharacterGrowth", "계정 새로고침", 496, 616, 440, actionHeight, StartAccountProfileRefresh, Line, 24).interactable = !growthBusy && !accountBusy && !recruitmentBusy;
        }

        void PreviewGrowth()
        {
            var captured = recruitmentSession; var profile = captured?.Progression;
            var character = profile?.Characters.FirstOrDefault(item => item.CharId == growthCharacter);
            if (captured == null || profile == null || character == null || growthBusy || accountBusy || recruitmentBusy || captured.ProgressionNeedsRefresh) return;
            growthBusy = true; levelPreview = null;
            var task = captured.PreviewCharacterLevelAsync(profile.Revision, character.CharId, character.Level + 1);
            ShowCharacterGrowth(); StartCoroutine(AwaitGrowthPreview(captured, character.CharId, task));
        }

        public void OpenCharacterLevel(string charId)
        {
            if (growthBusy || recruitmentBusy || accountBusy) return;
            if (recruitmentSession == null) { OpenRecruitment(); return; }
            player.Stop(); growthCharacter = charId; levelPreview = null;
            ShowCharacterGrowth(); ShowPanel(growthPanel);
        }

        IEnumerator AwaitGrowthPreview(PracticeServerSession captured, string character, Task<CharacterLevelPreviewResult> task)
        {
            while (!task.IsCompleted) yield return null;
            growthBusy = false;
            if (!ReferenceEquals(captured, recruitmentSession) || character != growthCharacter) yield break;
            levelPreview = !task.IsFaulted && !task.IsCanceled ? task.Result.View : null;
            growthNotice = levelPreview == null ? "미리보기를 확인하지 못했어요. 계정을 새로고침해주세요." : "";
            ShowCharacterGrowth();
        }

        void BeginGrowthAction(string action)
        {
            var captured = recruitmentSession; var profile = captured?.Progression;
            if (captured == null || profile == null || growthBusy || recruitmentBusy || accountBusy) return;
            Task<GrowthMutationResult> task;
            if (action == "start") task = captured.StartEncounterAsync(profile.Revision, growthDungeon, growthTier, ownedParty.ToArray());
            else if (action == "sweep") task = captured.SweepDungeonAsync(profile.Revision, growthDungeon, growthTier, 1);
            else if (action == "retry") task = captured.RetryGrowthPendingAsync();
            else if (levelPreview?.CanCommit == true && levelPreview.Revision == profile.Revision)
                task = captured.LevelUpCharacterAsync(profile.Revision, levelPreview.CharId, levelPreview.After.Level);
            else return;
            growthBusy = true; growthNotice = "";
            if (action == "level") ShowCharacterGrowth(); else RenderGrowth();
            StartCoroutine(AwaitGrowthMutation(captured, action, task));
        }

        IEnumerator AwaitGrowthMutation(PracticeServerSession captured, string action, Task<GrowthMutationResult> task)
        {
            while (!task.IsCompleted) yield return null;
            growthBusy = false;
            if (!ReferenceEquals(captured, recruitmentSession)) yield break;
            levelPreview = null;
            if (task.IsFaulted || task.IsCanceled) growthNotice = "결과를 확인하지 못했어요. 보존된 요청을 다시 확인해주세요.";
            else
            {
                var result = task.Result;
                growthNotice = MemoryForgeGrowthClient.MutationNotice(result.Status, result.Code, result.RefreshCode, result.OutboxCode);
                if ((action == "start" || action == "weekly") && result.Status == PracticeMutationStatus.Committed && result.RefreshCode == null && result.OutboxCode == null
                    && result.Snapshot != null && captured.Progression?.ActiveEncounter?.EncounterId == result.Snapshot.EncounterId)
                { EnterGrowthScene(); yield break; }
            }
            RefreshHomeAccount();
            if (action == "level") ShowCharacterGrowth(); else if (action == "weekly") RenderWeeklyBosses(); else RenderGrowth();
        }

        void EnterGrowthScene()
        {
            if (leaving) return;
            if (!CanContinueGrowth)
            {
                growthNotice = recruitmentSession?.State.Pending != null ? "미확정 요청을 먼저 같은 요청으로 확인해주세요."
                    : recruitmentSession?.ProgressionNeedsRefresh != false ? "계정 진행을 새로고침한 뒤 훈련을 계속해주세요." : "진행 중인 훈련과 계정 연결을 다시 확인해주세요.";
                accountNotice = growthNotice; RepaintAccountPanels(); return;
            }
            if (MemoryForgeAccountConnection.Current?.IsAuthenticated != true && SceneLoader == null)
            { growthNotice = "계정을 다시 연결한 뒤 계속해주세요."; RenderGrowth(); return; }
            StartCoroutine(Leave(MemoryForgeGrowthClient.SceneName));
        }

        static string DungeonName(string id) => id switch { "dg_ink" => "기억 잉크 훈련", "dg_credit" => "공방화 수련", "dg_oil" => "장비 기름 훈련", "dg_seal" => "승급 인장 훈련", "dg_book" => "기술서 훈련", "dg_polish" => "장비 연마 훈련", _ => id };
    }
}
