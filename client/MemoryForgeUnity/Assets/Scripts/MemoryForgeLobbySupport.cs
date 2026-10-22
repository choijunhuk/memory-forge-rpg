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
        string? supportCandidate;
        bool supportConfirmation;
        long supportRevision;

        public void OpenSupportSelection()
        {
            if (!EquipmentIdle) return;
            equipmentTab = "support"; OpenEquipmentWorkshop();
        }

        void ClearSupportPreparation() { supportConfirmation = false; supportCandidate = null; }

        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            ClearSupportPreparation();
            if (equipmentPanel != null && equipmentPanel.gameObject.activeSelf && equipmentTab == "support") RenderEquipment();
        }

        void RenderSupportSelection()
        {
            var profile = AccountProgression!;
            if (!profile.HasSupportSelection)
            {
                Label(equipmentPanel, "파트너 정보를 확인해야 해요. 계정을 새로고침해주세요.", 96, 400, 1408, 120, 30, Ink);
                Button(equipmentPanel, "RefreshSupportSelection", "계정 새로고침", 480, 600, 640, Math.Max(120, TouchUnits()), StartAccountProfileRefresh, Brass, 28).interactable = EquipmentIdle;
                return;
            }
            if (supportConfirmation) { RenderSupportConfirmation(); return; }
            string selected = profile.SupportSelection == null ? "선택하지 않음" : EquipmentCharacterName(profile.SupportSelection);
            int eligible = profile.Characters.Count(c => !ownedParty.Contains(c.CharId) && c.Deployable == true);
            Label(equipmentPanel, "다음 출격의 지원  " + selected + "\n출전 동료의 게이지 100 · 전투당 1회 · 파티 체력과 시작 덱에는 추가되지 않아요."
                + (eligible == 0 ? "\n출전 편성 밖에 무기를 장착한 보유 동료가 있어야 지원을 선택할 수 있어요." : "\n무기와 성장은 출발할 때 고정돼요. 진행 중인 전투의 파트너는 바뀌지 않아요."),
                56, 360, 1112, 144, 24, Ink);
            Button(equipmentPanel, "ClearSupportSelection", profile.SupportSelection == null ? "지원 없음" : "지원 해제 확인", 1200, 360, 344, 144,
                () => PrepareSupportSelection(null), Line, 25).interactable = EquipmentReady && profile.SupportSelection != null;
            float height = Math.Max(176, TouchUnits()), pitch = height + 16;
            var rows = ScrollArea(equipmentPanel, 56, 520, 1488, 348, Math.Max(348, (float)Math.Ceiling(profile.Characters.Count / 3f) * pitch));
            for (int n = 0; n < profile.Characters.Count; n++)
            {
                var character = profile.Characters[n]; string id = character.CharId;
                bool selectedNow = profile.SupportSelection == id, deployed = ownedParty.Contains(id);
                var cell = Button(rows, "SupportCandidate:" + id, "", n % 3 * 504, n / 3 * pitch, 480, height,
                    () => PrepareSupportSelection(id), selectedNow ? Brass : Line);
                cell.interactable = EquipmentReady && !selectedNow && !deployed && character.Deployable == true;
                var picture = Box(cell.transform, "SupportPortrait", 12, 12, 112, height - 24, Color.white);
                picture.GetComponent<Image>().sprite = Portrait(id); CenterSprite(picture);
                if (picture.GetComponent<Image>().sprite == null) { picture.GetComponent<Image>().color = Color.clear; Glyph(picture, "nav_companions", 16, 24, 80, Muted); }
                Label(cell.transform, EquipmentCharacterName(id), 136, 12, 328, 48, 28, selectedNow ? Background : Brass, FontStyle.Bold);
                Label(cell.transform, "Lv." + character.Level + " · 공명 " + character.Res + "\n" + EquipmentWeaponName(character.WeaponInstanceId), 136, 64, 328, 72, 22, selectedNow ? Background : Ink);
                Label(cell.transform, selectedNow ? "현재 지원 파트너" : deployed ? "출전 편성에 포함됨" : character.Deployable == false ? "사용할 무기 장착 필요" : "지원 선택", 136, height - 40, 328, 32, 20, selectedNow ? Background : Memory);
            }
        }

        void PrepareSupportSelection(string? charId)
        {
            var profile = AccountProgression;
            if (!EquipmentReady || profile?.HasSupportSelection != true || charId == profile.SupportSelection) return;
            if (charId != null && (ownedParty.Contains(charId) || profile.Characters.FirstOrDefault(c => c.CharId == charId)?.Deployable != true)) return;
            supportCandidate = charId; supportRevision = profile.Revision; supportConfirmation = true; equipmentNotice = "";
            RenderEquipment();
        }

        void RenderSupportConfirmation()
        {
            var profile = AccountProgression!;
            var sheet = Box(equipmentPanel, "SupportSelectionConfirmation", 56, 360, 1488, 368, Panel);
            Kit(sheet, "Frame", "ui_panel_frame", new Vector4(48, 48, 48, 48), 0, 0, 1488, 368, 128);
            if (supportCandidate != null)
            {
                var picture = Box(sheet, "SelectedSupportPortrait", 24, 16, 288, 336, Color.white);
                picture.GetComponent<Image>().sprite = Portrait(supportCandidate); CenterSprite(picture);
                if (picture.GetComponent<Image>().sprite == null) picture.GetComponent<Image>().color = Color.clear;
            }
            Label(sheet, supportCandidate == null ? "지원 파트너를 해제할까요?" : EquipmentCharacterName(supportCandidate) + "의 지원을 받을까요?", 344, 24, 1112, 72, 36, Brass, FontStyle.Bold);
            Label(sheet, "현재  " + (profile.SupportSelection == null ? "지원 없음" : EquipmentCharacterName(profile.SupportSelection)) + "\n변경  " + (supportCandidate == null ? "지원 없음" : EquipmentCharacterName(supportCandidate))
                + "\n선택에는 재료를 쓰지 않아요. 게이지는 전투에서 지원을 사용할 때 소모돼요.\n다음 출격에 적용되며, 이미 시작한 탐사는 출발 당시 파트너를 유지해요.", 344, 112, 1112, 224, 26, Ink);
            float height = Math.Max(120, TouchUnits()), y = 884 - height;
            Button(equipmentPanel, "CancelSupportSelection", "다시 선택", 56, y, 704, height, () => { ClearSupportPreparation(); RenderEquipment(); }, Line, 28).interactable = EquipmentIdle;
            Button(equipmentPanel, "ConfirmSupportSelection", "확인 · 지원 설정", 840, y, 704, height, CommitSupportSelection, Brass, 30)
                .interactable = EquipmentReady && profile.Revision == supportRevision;
        }

        void CommitSupportSelection()
        {
            var captured = recruitmentSession;
            if (!EquipmentReady || !supportConfirmation || captured?.Progression?.Revision != supportRevision) return;
            growthBusy = true;
            var task = captured.SelectSupportAsync(supportRevision, supportCandidate);
            RenderEquipment(); StartCoroutine(AwaitSupportSelection(captured, task));
        }

        void RetrySupportSelection()
        {
            if (!EquipmentIdle || recruitmentSession == null) return;
            var captured = recruitmentSession; growthBusy = true;
            var task = captured.RetrySupportPendingAsync(); RenderEquipment(); StartCoroutine(AwaitSupportSelection(captured, task));
        }

        IEnumerator AwaitSupportSelection(PracticeServerSession captured, Task<SupportSelectionMutationResult> task)
        {
            while (!task.IsCompleted) yield return null;
            if (!ReferenceEquals(captured, recruitmentSession)) yield break;
            growthBusy = false; ClearSupportPreparation();
            equipmentNotice = task.IsFaulted || task.IsCanceled ? "결과를 확인하지 못했어요. 보존된 같은 요청으로 다시 확인해주세요." : MemoryForgeGrowthClient.MutationNotice(task.Result.Status, task.Result.Code, task.Result.RefreshCode, task.Result.OutboxCode);
            RefreshHomeAccount(); RenderEquipment();
        }

        IEnumerator AwaitSupportRecruitmentRetry(PracticeServerSession captured, Task<SupportSelectionMutationResult> task)
        {
            while (!task.IsCompleted) yield return null;
            if (!ReferenceEquals(captured, recruitmentSession)) yield break;
            recruitmentBusy = false;
            if (!task.IsFaulted && !task.IsCanceled && task.Result.OutboxCode == null
                && (task.Result.Status == PracticeMutationStatus.Committed || task.Result.Status == PracticeMutationStatus.NotCommitted)) StartRecruitmentRefresh();
            else { recruitmentNotice = "지원 설정 요청은 보존되어 있어요. 같은 요청으로 다시 확인해주세요."; RenderRecruitment(); }
        }
    }
}
