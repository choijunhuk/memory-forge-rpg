#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    public sealed partial class MemoryForgeLobbyClient
    {
        public const string ServerExpeditionScene = "MemoryForgeServerPractice";
        readonly List<string> ownedParty = new List<string>();
        RectTransform homePartyStrip = null!;
        Text homePartyCaption = null!;
        bool accountBusy, accountSortieDrawn, useOwnedMemory = true;
        string accountNotice = "";
        public PracticeProgressionSnapshot? AccountProgression => recruitmentSession?.Progression;
        public IReadOnlyList<string> SelectedOwnedParty => ownedParty.ToArray();

        void RestoreAccountConnection()
        {
            var owner = MemoryForgeAccountConnection.Current;
            if (owner?.IsAuthenticated == true) ConnectRecruitment(owner.CreateSession);
        }

        void OnApplicationQuit() => MemoryForgeAccountConnection.SetCurrent(null);

        void OnApplicationFocus(bool focused)
        {
            if (focused) MemoryForgeUiViewport.RefreshMetrics();
            if (focused && recruitmentSession != null && !recruitmentBusy && !accountBusy) StartRecruitmentRefresh();
        }

        void OpenSortie()
        {
            player.Stop();
            if (recruitmentSession != null) RenderAccountSortie();
            else if (accountSortieDrawn) BuildSortie();
            ShowPanel(sortiePanel);
        }

        IEnumerator ReadAccountProgression(PracticeServerSession captured)
        {
            var profile = captured.RefreshProgressionAsync();
            while (!profile.IsCompleted) yield return null;
            if (!ReferenceEquals(captured, recruitmentSession)) yield break;
            accountNotice = profile.IsFaulted || profile.IsCanceled || profile.Result.Snapshot == null
                ? "계정 진행을 확인하지 못했어요. 새로고침 후 출격해주세요." : "";
            var current = captured.RefreshAsync();
            while (!current.IsCompleted) yield return null;
            if (!ReferenceEquals(captured, recruitmentSession)) yield break;
            if (current.IsFaulted || current.IsCanceled || captured.State.NeedsRefresh)
                accountNotice = "진행 중인 탐사를 확인하지 못했어요. 새로고침해주세요.";
            var data = captured.Progression;
            if (data != null)
            {
                ownedParty.RemoveAll(id => !data.Characters.Any(character => character.CharId == id));
                if (ownedParty.Count == 0) ownedParty.AddRange(data.LastParty.Where(id => data.Characters.Any(c => c.CharId == id)).Take(3));
            }
            if (captured.ProgressionNeedsRefresh)
            {
                growthNotice = "계정 진행을 확인하지 못했어요. 새로고침 후 계속해주세요.";
                equipmentNotice = growthNotice;
            }
            RefreshAccountHome(); RepaintAccountPanels();
        }

        // A refresh redraws the sheet the player is viewing; only stale, unsent previews lose their confirmation authority.
        void RepaintAccountPanels()
        {
            var profile = AccountProgression; bool stale = recruitmentSession?.ProgressionNeedsRefresh != false;
            if (supportConfirmation && (stale || profile?.Revision != supportRevision))
            { ClearSupportPreparation(); equipmentNotice = "계정 정보가 바뀌었어요. 지원 파트너를 다시 선택해주세요."; }
            if (levelPreview != null && (stale || profile?.Revision != levelPreview.Revision)) levelPreview = null;
            if (equipmentPreview != null && (stale || profile?.Revision != equipmentPreview.Revision))
            { equipmentPreview = null; equipmentNotice = "계정 정보가 바뀌었어요. 선택한 내용을 다시 미리보기로 확인해주세요."; }
            if (sortiePanel != null && sortiePanel.gameObject.activeSelf) RenderAccountSortie();
            if (growthPanel != null && growthPanel.gameObject.activeSelf)
            {
                bool character = growthPanel.Cast<Transform>().Any(c => c.gameObject.activeSelf && c.name == "CharacterGrowthSheet");
                bool weekly = growthPanel.Cast<Transform>().Any(c => c.gameObject.activeSelf && c.name == "WeeklyBackdrop");
                if (character) ShowCharacterGrowth(); else if (weekly) RenderWeeklyBosses(); else RenderGrowth();
            }
            if (equipmentPanel != null && equipmentPanel.gameObject.activeSelf) RenderEquipment();
        }

        void StartAccountProfileRefresh()
        {
            if (accountBusy || recruitmentBusy || growthBusy || recruitmentSession == null) return;
            accountBusy = true; RepaintAccountPanels();
            StartCoroutine(RefreshAccountProfile(recruitmentSession));
        }

        IEnumerator RefreshAccountProfile(PracticeServerSession captured)
        {
            yield return ReadAccountProgression(captured);
            accountBusy = false;
            if (!ReferenceEquals(captured, recruitmentSession)) yield break;
            if (!captured.ProgressionNeedsRefresh && !captured.State.NeedsRefresh)
            { accountNotice = "계정 정보를 새로 확인했어요."; growthNotice = accountNotice; }
            RepaintAccountPanels();
        }

        void RefreshAccountHome()
        {
            var data = AccountProgression;
            if (homeAccountText == null || content == null || contentUnavailable) return;
            if (data != null)
            {
                bool stale = recruitmentSession!.ProgressionNeedsRefresh;
                homeAccountText.text = (stale ? "마지막 확인 · " : "") + "AL " + data.Account.Level + "\n보유 " + data.Characters.Count + "명";
            }
            var entry = homeControls.transform.Find("StartExpedition");
            if (entry != null)
            {
                var title = entry.GetComponentsInChildren<Text>().FirstOrDefault(text => text.text == "출격" || text.text == "탐사 계속" || text.text == "훈련 계속");
                if (title != null) title.text = data?.ActiveSession != null && data.ActiveSession.Phase != "DONE" ? "탐사 계속"
                    : data?.ActiveEncounter != null ? "훈련 계속" : "출격";
            }
            if (homePartyStrip == null) return;
            ClearPanel(homePartyStrip);
            var party = data == null ? PracticeParty.ToArray() : !data.Tutorial.Completed ? new[] { "nia", "harin", "rien" } : ownedParty.ToArray();
            homePartyCaption.text = data == null ? "연습 동행 3명" : !data.Tutorial.Completed ? "첫 기억의 임시 동행" : party.Length == 0 ? "동료를 편성해주세요" : "다음 출격 편성";
            for (int n = 0; n < party.Length; n++)
            {
                string id = party[n];
                var slot = Box(homePartyStrip, (data == null ? "PreviewParty:" : "OwnedPreviewParty:") + id, n * 152, 0, 136, 80, new Color(0.04f, 0.055f, 0.1f, 0.9f));
                slot.anchorMin = new Vector2(n / 3f, 1); slot.anchorMax = new Vector2((n + 1) / 3f, 1); slot.sizeDelta = new Vector2(-16, 80); slot.anchoredPosition = Vector2.zero;
                var face = Box(slot, "Portrait", 4, 16, 48, 48, Color.white);
                var art = SpriteAt("Art/Characters/char_" + id + "_sd") ?? Portrait(id);
                face.GetComponent<Image>().sprite = art; CenterSprite(face);
                if (art == null) { face.GetComponent<Image>().color = Color.clear; Glyph(face, "nav_companions", 0, 0, 48, Muted); }
                Label(slot, content.Battle.Characters.FirstOrDefault(character => character.Id == id)?.Name ?? id, 56, 0, 76, 80, 18, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            }
        }

        void RenderAccountSortie()
        {
            ClearPanel(sortiePanel); accountSortieDrawn = true;
            var wall = Box(sortiePanel, "AccountPreparationBackdrop", 0, 0, 1600, 900, new Color(0.2f, 0.25f, 0.33f));
            wall.GetComponent<Image>().sprite = SpriteAt("Art/Backgrounds/bg_workshop"); MemoryForgeUiViewport.Cover(wall.GetComponent<Image>());
            Box(sortiePanel, "PreparationVeil", 0, 0, 1600, 900, new Color(0.025f, 0.04f, 0.075f, 0.72f));
            var preparationHeading = Label(sortiePanel, "출격 준비", 56, 28, 1000, 64, 42, Ink, FontStyle.Bold);
            var preparationCaption = Label(sortiePanel, "공방 계정 · 보유한 동료와 저장된 성장으로 출발해요.", 58, 28 + preparationHeading.rectTransform.rect.height + 16, 920, 36, 21, Muted);
            Button(sortiePanel, "CloseSortie", "홈", 1392, 24, 152, 112, ClosePanels, Line, 24);
            Button(sortiePanel, "ChooseSupportPartner", "지원 파트너", 1060, 24, 236, 112, OpenSupportSelection, Line, 24).interactable = !accountBusy && !growthBusy && !recruitmentBusy;
            var data = AccountProgression;
            if (data == null)
            {
                Label(sortiePanel, "계정 진행을 불러와야 출격할 수 있어요.", 96, 256, 1408, 100, 30, Ink);
                AccountRefreshButton(544, 480); return;
            }
            bool stale = recruitmentSession!.ProgressionNeedsRefresh || recruitmentSession.State.NeedsRefresh;
            var active = data.ActiveSession;
            if (active != null && active.Phase != "DONE")
            {
                Label(sortiePanel, "진행 중인 탐사가 있어요", 96, 232, 1408, 72, 36, Brass, FontStyle.Bold);
                Label(sortiePanel, (active.Mode == "tutorial" ? "프롤로그" : active.Mode == "story" ? "이야기 " + active.Chapter + "장" : "기존 서버 연습")
                    + " · " + active.Phase + "\n홈으로 돌아와도 탐사는 이어집니다. 포기는 탐사 화면에서 선택해주세요.", 96, 328, 1408, 132, 26, Ink);
                Button(sortiePanel, "ContinueOwnedExpedition", "탐사 계속", 960, 568, 584, Math.Max(120, TouchUnits()), ContinueOwnedExpedition, Brass, 32)
                    .interactable = !accountBusy && !recruitmentBusy && !stale && recruitmentSession.State.Pending == null;
                AccountRefreshButton(96, 568); AccountStatus(732); return;
            }
            if (data.ActiveEncounter != null)
            {
                Label(sortiePanel, "진행 중인 훈련이 있어요", 96, 232, 1408, 72, 36, Brass, FontStyle.Bold);
                Label(sortiePanel, "성장 공방에서 전투를 계속하거나 동료를 성장시킬 수 있어요.", 96, 328, 1408, 100, 28, Ink);
                Button(sortiePanel, "ContinueOwnedTraining", "훈련 계속", 960, 568, 584, Math.Max(120, TouchUnits()), EnterGrowthScene, Brass, 32)
                    .interactable = !accountBusy && !growthBusy && !recruitmentBusy && !stale && recruitmentSession.State.Pending == null;
                Button(sortiePanel, "TrainingWorkshop", "성장 공방", 96, 568, 704, Math.Max(120, TouchUnits()), OpenGrowth, Line, 28)
                    .interactable = !accountBusy && !growthBusy && !recruitmentBusy;
                AccountRefreshButton(96, 732); AccountStatus(480); return;
            }
            bool tutorial = !data.Tutorial.Completed;
            var chapter = data.Chapters.FirstOrDefault(item => item.Id == 1);
            var zone = Box(sortiePanel, "OwnedDestination", 56, 216, 440, 458, Panel);
            Kit(zone, "Frame", "ui_panel_frame", new Vector4(48, 48, 48, 48), 0, 0, 440, 458, 128);
            var vista = Box(zone, "DestinationArt", 0, 0, 440, 220, Color.white);
            vista.GetComponent<Image>().sprite = SpriteAt("Art/Backgrounds/bg_zone1_map"); MemoryForgeUiViewport.Cover(vista.GetComponent<Image>());
            Box(zone, "ArtShade", 0, 156, 440, 64, new Color(0.025f, 0.04f, 0.075f, 0.86f));
            Label(zone, tutorial ? "프롤로그" : "이야기 1장", 24, 232, 392, 56, 36, Brass, FontStyle.Bold);
            var zoneBody = ScrollArea(zone, 24, 336, 392, 114, 114);
            var zoneText = Label(zoneBody, tutorial ? "종탑 하층 · 첫 기억\n7층 탐사와 보스\n첫 기억 저장 후 동료 3명과 기본 무기 지급"
                : "AL " + data.Account.Level + " · 권장 단계 " + (chapter?.StageLevel.ToString() ?? "확인 중")
                    + "\n보유 동료 3명을 순서대로 편성\n성장과 장비는 출발 시 고정됩니다."
                    + (data.HasSupportSelection ? "\n다음 지원 · " + (data.SupportSelection == null ? "없음" : EquipmentCharacterName(data.SupportSelection)) : ""), 0, 0, 392, 144, 23, Ink);
            zoneBody.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(144, zoneText.rectTransform.rect.height));
            float footerHeight = Math.Max(120, TouchUnits()), footerY = 836 - footerHeight;
            var chosen = tutorial ? new[] { "nia", "harin", "rien" } : ownedParty.ToArray();
            Label(sortiePanel, tutorial ? "프롤로그 임시 동행 · 아직 보유로 지급되지 않았어요" : "출전 편성 · 선택 순서가 출전 순서예요", 536, 216, 1008, 72, 24, Brass, FontStyle.Bold);
            var ownedRow = Box(sortiePanel, "OwnedPartyRow", 536, 304, 1008, 264, Color.clear);
            for (int n = 0; n < 3; n++)
            {
                string? id = n < chosen.Length ? chosen[n] : null;
                var slot = id == null || tutorial ? Box(ownedRow, "OwnedPartySlot:" + n, n * 340, 0, 324, 264, Panel)
                    : (RectTransform)Button(ownedRow, "RemoveOwnedParty:" + id, "", n * 340, 0, 324, 264,
                        () => { ownedParty.Remove(id); RenderAccountSortie(); RefreshAccountHome(); }, Panel).transform;
                slot.anchorMin = new Vector2(n / 3f, 1); slot.anchorMax = new Vector2((n + 1) / 3f, 1); slot.sizeDelta = new Vector2(-16, 264); slot.anchoredPosition = Vector2.zero;
                if (id != null && !tutorial) slot.GetComponent<Button>().interactable = !accountBusy;
                var surface = slot.GetComponent<Image>(); surface.sprite = null; surface.type = Image.Type.Simple; surface.color = Panel;
                Kit(slot, "Frame", "ui_panel_frame", new Vector4(48, 48, 48, 48), 0, 0, 324, 264, 128);
                if (id == null) { Label(slot, "동료 선택", 16, 64, 292, 56, 28, Muted, FontStyle.Normal, TextAnchor.MiddleCenter); continue; }
                var character = content.Battle.Characters.FirstOrDefault(item => item.Id == id);
                var art = Box(slot, "Portrait", 12, 16, 72, 72, Color.white);
                art.GetComponent<Image>().sprite = SpriteAt("Art/Characters/char_" + id + "_sd") ?? Portrait(id); CenterSprite(art);
                if (art.GetComponent<Image>().sprite == null)
                {
                    art.GetComponent<Image>().color = Color.clear;
                    Glyph(art, "nav_companions", 16, 16, 80, Muted);
                }
                var owned = data.Characters.FirstOrDefault(item => item.CharId == id);
                Label(slot, character?.Name ?? id, 96, 8, 216, 72, 28, Ink, FontStyle.Bold);
                Label(slot, tutorial ? "임시 동행 · Lv.1" : "Lv." + owned?.Level + " · 공명 " + owned?.Res, 12, 96, 300, 72, 18, Memory);
                if (!tutorial)
                {
                    string weapon = owned?.WeaponInstanceId == null ? owned?.Deployable == false ? "무기 미장착 · 장비 공방에서 확인" : "장착 무기 확인 필요" : EquipmentWeaponName(owned.WeaponInstanceId);
                    var weaponBody = ScrollArea(slot, 12, 176, 300, 72, 72);
                    var weaponText = Label(weaponBody, weapon, 0, 0, 300, 1, 18, owned?.Deployable == false ? Memory : Muted);
                    weaponBody.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(72, weaponText.rectTransform.rect.height));
                }
            }
            if (tutorial)
                Label(sortiePanel, "이야기와 보유 편성은 첫 기억 저장 후 열려요.\n임시 동행의 성장과 무기는 모두 1단계예요.", 544, 584, 1000, 112, 25, Ink);
            else
            {
                float height = Math.Max(112, TouchUnits());
                float gridHeight = footerY - 580 - 16;
                var grid = ScrollArea(sortiePanel, 536, 580, 1008, gridHeight, Math.Max(gridHeight, (float)Math.Ceiling(data.Characters.Count / 3f) * (height + 12)));
                for (int n = 0; n < data.Characters.Count; n++)
                {
                    var owned = data.Characters[n]; string id = owned.CharId;
                    string name = content.Battle.Characters.FirstOrDefault(item => item.Id == id)?.Name ?? id;
                    var pick = Button(grid, "SelectOwnedParty:" + id, name + "  ·  Lv." + owned.Level, n % 3 * 340, n / 3 * (height + 12), 324, height,
                        () => { if (!ownedParty.Contains(id) && ownedParty.Count < 3) ownedParty.Add(id); RenderAccountSortie(); RefreshAccountHome(); }, ownedParty.Contains(id) ? Brass : Line, 24);
                    var pickRect = (RectTransform)pick.transform; pickRect.anchorMin = new Vector2((n % 3) / 3f, 1); pickRect.anchorMax = new Vector2((n % 3 + 1) / 3f, 1); pickRect.sizeDelta = new Vector2(-16, height); pickRect.anchoredPosition = new Vector2(0, -(n / 3) * (height + 12));
                    pick.interactable = !accountBusy && (ownedParty.Contains(id) || ownedParty.Count < 3);
                }
            }
            Button(sortiePanel, "OwnedMemoryToggle", useOwnedMemory ? "기억 덱 사용 ✓" : "기본 덱 사용", 56, footerY, 440, footerHeight,
                () => { useOwnedMemory = !useOwnedMemory; RenderAccountSortie(); }, Line, 24).interactable = !accountBusy;
            bool ready = !accountBusy && !recruitmentBusy && !stale && recruitmentSession.State.Pending == null && recruitmentSession.State.OutboxCode == null;
            Button(sortiePanel, "DepartOwnedExpedition", tutorial ? "첫 기억으로 출발" : "이야기 출발", 1112, footerY, 432, footerHeight,
                () => StartOwnedDeparture(tutorial), Brass, 29).interactable = ready && (tutorial || chapter?.Available == true && ownedParty.Count == 3 && OwnedWeaponsReady(data) && SupportFitsNextParty(data));
            AccountRefreshButton(536, footerY); AccountStatus(844);
        }

        void AccountRefreshButton(float x, float y) => Button(sortiePanel, "RefreshAccountProgression", "계정 새로고침", x, y, 432, Math.Max(112, TouchUnits()),
            StartAccountProfileRefresh, Line, 24).interactable = !accountBusy && !recruitmentBusy && !growthBusy;

        void AccountStatus(float y)
        {
            string text = accountBusy || recruitmentBusy ? "서버에서 확인 중이에요…" : recruitmentSession?.State.Pending != null
                ? "미확정 요청이 있어요. 모집의 이전 요청 확인에서 같은 요청을 확인해주세요." : accountNotice;
            if (text.Length == 0 && AccountProgression?.Tutorial.Completed == true && !OwnedWeaponsReady(AccountProgression))
                text = "선택한 동료에게 장착 가능한 무기를 장비 공방에서 확인해주세요.";
            if (text.Length == 0 && AccountProgression != null && !SupportFitsNextParty(AccountProgression))
                text = ownedParty.Contains(AccountProgression.SupportSelection!) ? "지원 파트너는 출전 동료 3명과 달라야 해요. 편성이나 지원 설정을 바꿔주세요."
                    : "지원 파트너에게 사용할 무기를 장착해주세요.";
            Label(sortiePanel, text, 56, y, 1488, 48, 20, Memory).gameObject.name = "AccountStatus";
        }

        bool OwnedWeaponsReady(PracticeProgressionSnapshot profile) => ownedParty.All(id => profile.Characters.Any(c => c.CharId == id && c.Deployable != false));
        bool SupportFitsNextParty(PracticeProgressionSnapshot profile) => profile.SupportSelection == null || !ownedParty.Contains(profile.SupportSelection)
            && profile.Characters.Any(c => c.CharId == profile.SupportSelection && c.Deployable != false);

        void StartOwnedDeparture(bool tutorial)
        {
            var captured = recruitmentSession; var profile = captured?.Progression;
            if (captured == null || profile == null || accountBusy || recruitmentBusy || growthBusy) return;
            if (!tutorial && !OwnedWeaponsReady(profile)) { accountNotice = "출전 동료의 장착 무기를 확인해주세요."; RenderAccountSortie(); return; }
            accountBusy = true;
            var task = captured.LaunchExpeditionAsync(profile.Revision, tutorial ? "tutorial" : "story", tutorial ? (int?)null : 1,
                tutorial ? null : ownedParty.ToArray(), useOwnedMemory);
            RenderAccountSortie(); StartCoroutine(AwaitOwnedDeparture(captured, task));
        }

        IEnumerator AwaitOwnedDeparture(PracticeServerSession captured, Task<ProgressionMutationResult> task)
        {
            while (!task.IsCompleted) yield return null;
            accountBusy = false;
            if (!ReferenceEquals(captured, recruitmentSession)) yield break;
            if (task.IsFaulted || task.IsCanceled) accountNotice = "출발 결과를 확인하지 못했어요. 보존된 요청을 다시 확인해주세요.";
            else
            {
                var result = task.Result;
                if (result.Status == PracticeMutationStatus.Committed && result.Receipt != null && result.RefreshCode == null && result.OutboxCode == null
                    && captured.State.Pending == null && captured.State.Snapshot?.Run?.RunId == result.Receipt.RunId)
                { ContinueOwnedExpedition(); yield break; }
                accountNotice = result.Status == PracticeMutationStatus.Pending ? "출발 요청을 보존했어요. 모집에서 미확정 요청을 확인해주세요." : ProgressionMessage(result.Code);
            }
            RenderAccountSortie();
        }

        void ContinueOwnedExpedition()
        {
            if (leaving || accountBusy || recruitmentBusy || recruitmentSession == null || recruitmentSession.State.Pending != null
                || recruitmentSession.State.NeedsRefresh || recruitmentSession.State.Snapshot?.Run == null) return;
            if (MemoryForgeAccountConnection.Current?.IsAuthenticated != true && SceneLoader == null)
            { accountNotice = "계정을 다시 연결한 뒤 계속해주세요."; RenderAccountSortie(); return; }
            StartCoroutine(Leave(ServerExpeditionScene));
        }

        static string ProgressionMessage(string code) => code switch
        {
            "OVER_CAPACITY" => "저장된 기억이 현재 동료의 수용량을 넘어요. 기본 덱을 골라도 출격할 수 없으며, 기억은 보존했어요.",
            "NOT_OWNED" => "보유 동료와 맞는 무기를 확인해주세요. 지급되지 않은 동료나 무기는 출격할 수 없어요.",
            "NO_ELIGIBLE_WEAPON" => "출전 동료에게 사용할 수 있는 무기를 장착해주세요.",
            "CHAPTER_LOCKED" => "계정 레벨이 아직 이 이야기에 도달하지 않았어요.",
            "TUTORIAL_REQUIRED" => "첫 기억 저장을 먼저 완료해주세요.",
            "RUN_ACTIVE" => "진행 중인 탐사가 있어요. 새로고침 후 계속해주세요.",
            "TUTORIAL_DONE" or "STALE_REVISION" or "REFRESH_REQUIRED" => "계정 진행이 바뀌었어요. 새로고침해주세요.",
            _ => RecruitmentMessage(code)
        };
    }
}
