#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    public sealed partial class MemoryForgeLobbyClient
    {
        RectTransform equipmentPanel = null!;
        float equipmentBodyY = 360, equipmentActionY = 416;
        float EquipmentBodyHeight => Math.Max(160, 868 - equipmentBodyY);
        string equipmentTab = "characters", equipmentCharacter = "", equipmentWeapon = "", equipmentNotice = "";
        string? equipmentKind, equipmentTarget;
        JObject equipmentIntent = new JObject();
        PracticeEquipmentPreview? equipmentPreview;

        public void OpenEquipmentWorkshop()
        {
            if (growthBusy || recruitmentBusy || accountBusy) return;
            player.Stop();
            if (recruitmentSession == null) { OpenRecruitment(); return; }
            if (equipmentPanel == null) equipmentPanel = Box(root, "EquipmentPanel", 0, 0, 1600, 900, Background);
            EquipmentClearPreview(); RenderEquipment(); ShowPanel(equipmentPanel);
        }

        public void OpenEquipmentFor(string charId)
        {
            if (growthBusy || recruitmentBusy || accountBusy) return;
            equipmentCharacter = charId; equipmentTab = "characters"; OpenEquipmentWorkshop();
        }

        bool EquipmentIdle => !growthBusy && !recruitmentBusy && !accountBusy && recruitmentSession?.State.InFlight != true;
        bool EquipmentReady => EquipmentIdle && recruitmentSession?.Progression != null && !recruitmentSession.ProgressionNeedsRefresh
            && recruitmentSession.State.Pending == null && recruitmentSession.State.OutboxCode == null;
        void EquipmentClearPreview() { equipmentKind = null; equipmentTarget = null; equipmentPreview = null; equipmentIntent = new JObject(); ClearSupportPreparation(); }
        string EquipmentCharacterName(string? id) => id == null ? "미장착" : content.Battle.Characters.FirstOrDefault(c => c.Id == id)?.Name ?? id;
        string EquipmentWeaponName(string? instance)
        {
            var weapon = AccountProgression?.Weapons.FirstOrDefault(w => w.InstanceId == instance);
            return weapon == null ? instance ?? "미장착" : content.Battle.Weapons.FirstOrDefault(w => w.Id == weapon.WeaponId)?.Name ?? weapon.WeaponId;
        }
        static string EquipmentMaterial(string id) => id == "boss_token" ? "보스 토큰" : MemoryForgeGrowthClient.MaterialName(id);
        static string EquipmentGear(int? value) => value.HasValue ? "+" + value : "미제작";

        void RenderEquipment()
        {
            ClearPanel(equipmentPanel);
            var backdrop = Box(equipmentPanel, "EquipmentWorkshopArt", 0, 0, 1600, 900, Color.white);
            backdrop.GetComponent<Image>().sprite = SpriteAt("Art/Backgrounds/bg_workshop"); MemoryForgeUiViewport.Cover(backdrop.GetComponent<Image>());
            Box(equipmentPanel, "EquipmentWorkshopVeil", 0, 0, 1600, 900, new Color(0.025f, 0.04f, 0.075f, 0.86f));
            var equipmentHeading = Label(equipmentPanel, supportConfirmation ? "지원 설정 확인" : equipmentKind != null ? "변경 내용 확인" : equipmentTab == "support" ? "지원 파트너" : "장비 공방", 56, 24, 1240, 64, 42, Brass, FontStyle.Bold);
            Button(equipmentPanel, "CloseEquipment", "홈", 1392, 24, 152, Math.Max(112, TouchUnits()), ClosePanels, Line, 24).interactable = EquipmentIdle;
            float noticeY = 24 + equipmentHeading.rectTransform.rect.height + 16;
            var noticeBody = ScrollArea(equipmentPanel, 58, noticeY, 1260, 144, 144);
            var equipmentMessage = Label(noticeBody, growthBusy ? "서버에서 확인하고 있어요…" : equipmentNotice.Length > 0 ? equipmentNotice
                : "선택한 성장에 필요한 재료를 먼저 확인하세요.", 0, 0, 1228, 1, 22, Memory);
            float noticeHeight = Math.Min(144, Math.Max(64, equipmentMessage.rectTransform.rect.height));
            ((RectTransform)noticeBody.parent).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, noticeHeight);
            noticeBody.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(noticeHeight, equipmentMessage.rectTransform.rect.height));
            equipmentBodyY = noticeY + noticeHeight + 16;
            var profile = AccountProgression;
            if (profile == null)
            {
                Label(equipmentPanel, "계정의 실제 보유 기록을 확인한 뒤 이용할 수 있어요.", 96, 272, 1408, 112, 30, Ink);
                Button(equipmentPanel, "RefreshEquipmentProfile", "계정 새로고침", 480, 504, 640, Math.Max(120, TouchUnits()), StartAccountProfileRefresh, Brass, 28).interactable = EquipmentIdle;
                return;
            }
            var pending = recruitmentSession!.State.Pending;
            if (pending != null || recruitmentSession.State.OutboxCode != null)
            {
                Label(equipmentPanel, "아직 결과가 확정되지 않은 요청이 있어요.\n보유 기록과 보낸 요청은 그대로 보존돼요.", 96, 256, 1408, 144, 30, Ink);
                bool support = pending != null && PracticeCommandOutbox.IsSupportSelectionRoute(pending.Route);
                bool ours = support || pending != null && PracticeCommandOutbox.IsEquipmentRoute(pending.Route);
                Button(equipmentPanel, "RetryEquipmentPending", ours ? "같은 요청으로 결과 확인" : "이전 요청 확인", 400, 512, 800, Math.Max(120, TouchUnits()),
                    () => { if (support) RetrySupportSelection(); else if (ours) EquipmentRetry(); else OpenRecruitment(); }, Brass, 28).interactable = EquipmentIdle;
                if (recruitmentSession.State.OutboxCode != null) Label(equipmentPanel, RecruitmentMessage(recruitmentSession.State.OutboxCode), 96, 704, 1408, 72, 22, Memory);
                return;
            }
            if (recruitmentSession.ProgressionNeedsRefresh)
            {
                Label(equipmentPanel, "마지막으로 확인한 계정 정보예요. 변경하기 전에 새로 확인해주세요.", 96, 240, 1408, 112, 28, Ink);
                Button(equipmentPanel, "RefreshEquipmentCurrent", "계정 새로고침", 480, 480, 640, Math.Max(120, TouchUnits()), StartAccountProfileRefresh, Brass, 28).interactable = EquipmentIdle;
                return;
            }
            if (equipmentKind != null) { RenderEquipmentPreview(); return; }
            string[] tabs = { "characters", "weapons", "shop", "support" }, names = { "동료 성장", "보유 무기", "보스 교환소", "지원 파트너" };
            float height = Math.Max(120, TouchUnits()), tabsY = equipmentBodyY; equipmentBodyY += height + 32;
            for (int n = 0; n < tabs.Length; n++)
            {
                string tab = tabs[n];
                var tabButton = Button(equipmentPanel, "EquipmentTab:" + tab, names[n], 56 + n * 376, tabsY, 360, height,
                    () => { equipmentTab = tab; EquipmentClearPreview(); RenderEquipment(); }, tab == equipmentTab ? Brass : Line, 28);
                tabButton.interactable = EquipmentIdle;
                var tabRect = (RectTransform)tabButton.transform;
                tabRect.anchorMin = new Vector2(n / 4f, 1); tabRect.anchorMax = new Vector2((n + 1) / 4f, 1);
                tabRect.offsetMin = new Vector2(56 - n * 24, -tabsY - height); tabRect.offsetMax = new Vector2(16 - n * 24, -tabsY);
            }
            if (equipmentTab == "characters") RenderEquipmentCharacters();
            else if (equipmentTab == "weapons") RenderEquipmentWeapons();
            else if (equipmentTab == "support") RenderSupportSelection();
            else RenderEquipmentShop();
        }

        void RenderEquipmentCharacters()
        {
            var profile = AccountProgression!; var rows = profile.Characters;
            var selected = rows.FirstOrDefault(c => c.CharId == equipmentCharacter) ?? rows.FirstOrDefault();
            if (selected == null) { Label(equipmentPanel, "보유한 동료가 없어요. 첫 기억을 완료하거나 모집 기록을 확인해주세요.", 96, 400, 1408, 152, 30, Ink); return; }
            equipmentCharacter = selected.CharId;
            float height = Math.Max(192, Math.Max(TouchUnits(), MemoryForgeUiViewport.FontSize(25) * 3.4f + 32)), pitch = height + 16;
            var list = ScrollArea(equipmentPanel, 56, equipmentBodyY, 440, EquipmentBodyHeight, Math.Max(EquipmentBodyHeight, rows.Count * pitch));
            for (int n = 0; n < rows.Count; n++)
            {
                var row = rows[n]; string id = row.CharId;
                Button(list, "EquipmentCharacter:" + id, EquipmentCharacterName(id) + "\nLv." + row.Level + " · 승급 " + row.Asc, 0, n * pitch, 424, height,
                    () => { equipmentCharacter = id; RenderEquipment(); }, id == selected.CharId ? Brass : Line, 25).interactable = EquipmentIdle;
            }
            var body = ScrollArea(equipmentPanel, 520, equipmentBodyY, 1024, EquipmentBodyHeight, 416 + 3 * pitch + 32);
            var characterHeading = Label(body, EquipmentCharacterName(selected.CharId), 16, 0, 712, 56, 34, Brass, FontStyle.Bold);
            var figure = Box(body, "EquipmentPortrait", 776, 0, 216, 304, Color.white); figure.GetComponent<Image>().sprite = Portrait(selected.CharId); CenterSprite(figure);
            if (figure.GetComponent<Image>().sprite == null) figure.GetComponent<Image>().color = Color.clear;
            var characterStats = Label(body, "Lv." + selected.Level + (selected.Cap.HasValue ? "/" + selected.Cap : "") + "  ·  승급 " + selected.Asc + "  ·  공명 " + selected.Res
                + "\n고유 카드 " + selected.SkillLv + (selected.EffectiveSkillLv.HasValue ? " (적용 " + selected.EffectiveSkillLv + ")" : "")
                + "  /  필살기 " + selected.UltLv + (selected.EffectiveUltLv.HasValue ? " (적용 " + selected.EffectiveUltLv + ")" : "")
                + "\n갑주 " + EquipmentGear(selected.Armor) + "  ·  장신구 " + EquipmentGear(selected.Accessory)
                + "\n무기  " + EquipmentWeaponName(selected.WeaponInstanceId) + "\n" + EquipmentStats(selected.Stats), 16, characterHeading.rectTransform.rect.height + 16, 728, 240, 25, Ink);
            equipmentActionY = Math.Max(416, characterHeading.rectTransform.rect.height + 16 + characterStats.rectTransform.rect.height + 24);
            body.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, equipmentActionY + 3 * pitch + 32);
            EquipmentAction(body, 0, "CharacterAscend", "승급 확인", "characterAscend", selected.CharId);
            EquipmentAction(body, 1, "CardSkill", "고유 카드 스킬 확인", "skillUp", selected.CharId, new JObject { ["line"] = "card" });
            EquipmentAction(body, 2, "UltSkill", "필살기 스킬 확인", "skillUp", selected.CharId, new JObject { ["line"] = "ult" });
            EquipmentAction(body, 3, "Armor", selected.Armor.HasValue ? "갑주 강화 확인" : "갑주 제작 확인", selected.Armor.HasValue ? "gearEnhance" : "gearCraft", selected.CharId, new JObject { ["slot"] = "armor" });
            EquipmentAction(body, 4, "Accessory", selected.Accessory.HasValue ? "장신구 강화 확인" : "장신구 제작 확인", selected.Accessory.HasValue ? "gearEnhance" : "gearCraft", selected.CharId, new JObject { ["slot"] = "accessory" });
            Button(body, "EquipmentCharacterLevel", "동료 레벨업", 512, equipmentActionY + 2 * pitch, 496, height,
                () => OpenCharacterLevel(selected.CharId), Line, 25).interactable = EquipmentReady;
        }

        void EquipmentAction(RectTransform body, int index, string name, string caption, string kind, string? target, JObject? intent = null)
        {
            float height = Math.Max(192, Math.Max(TouchUnits(), MemoryForgeUiViewport.FontSize(25) * 3.4f + 32));
            Button(body, "EquipmentAction:" + name, caption, index % 2 * 512, equipmentActionY + index / 2 * (height + 16), 496, height,
                () => EquipmentReadPreview(kind, target, intent ?? new JObject()), Line, 25).interactable = EquipmentReady;
        }

        void RenderEquipmentWeapons()
        {
            var profile = AccountProgression!; var rows = profile.Weapons;
            var selected = rows.FirstOrDefault(w => w.InstanceId == equipmentWeapon) ?? rows.FirstOrDefault();
            if (selected == null) { Label(equipmentPanel, "보유한 무기가 없어요. 첫 기억의 기본 지급과 실제 모집 기록을 확인해주세요.", 96, 400, 1408, 152, 30, Ink); return; }
            equipmentWeapon = selected.InstanceId;
            float height = Math.Max(192, Math.Max(TouchUnits(), MemoryForgeUiViewport.FontSize(25) * 3.4f + 32)), pitch = height + 16;
            var list = ScrollArea(equipmentPanel, 56, equipmentBodyY, 440, EquipmentBodyHeight, Math.Max(EquipmentBodyHeight, rows.Count * pitch));
            for (int n = 0; n < rows.Count; n++)
            {
                var row = rows[n]; string id = row.InstanceId;
                Button(list, "EquipmentWeapon:" + id, EquipmentWeaponName(id) + "\nLv." + row.Level + " · 정련 " + row.Refine, 0, n * pitch, 424, height,
                    () => { equipmentWeapon = id; RenderEquipment(); }, id == selected.InstanceId ? Brass : Line, 23).interactable = EquipmentIdle;
            }
            var body = ScrollArea(equipmentPanel, 520, equipmentBodyY, 1024, EquipmentBodyHeight, 848 + (profile.Characters.Count + 1) / 2 * pitch);
            var weaponHeading = Label(body, EquipmentWeaponName(selected.InstanceId), 16, 0, 976, 64, 34, Brass, FontStyle.Bold);
            var weaponStats = Label(body, (selected.Rarity ?? "") + "  ·  " + EquipmentType(selected.Type) + "\nLv." + selected.Level + (selected.Cap.HasValue ? "/" + selected.Cap : "")
                + "  ·  승급 " + selected.Asc + "  ·  정련 " + selected.Refine + "\n장착 동료  " + EquipmentCharacterName(selected.EquippedBy)
                + "\n" + (selected.RefineMode == "copy" ? "같은 무기의 중복 획득으로 정련이 반영돼요." : "기본 R 무기는 제련핵으로 정련해요.")
                + "\n장착을 바꿔도 진행 중인 탐사의 출발 기록은 유지돼요.", 16, weaponHeading.rectTransform.rect.height + 16, 976, 224, 26, Ink);
            equipmentActionY = Math.Max(416, weaponHeading.rectTransform.rect.height + 16 + weaponStats.rectTransform.rect.height + 24);
            EquipmentAction(body, 0, "WeaponLevel", selected.Level >= 60 ? "레벨 상한" : "다음 레벨 확인", "weaponLevelUp", selected.InstanceId, new JObject { ["targetLevel"] = selected.Level + 1 });
            body.Find("EquipmentAction:WeaponLevel").GetComponent<Button>().interactable &= selected.Level < 60;
            EquipmentAction(body, 1, "WeaponAscend", "무기 승급 확인", "weaponAscend", selected.InstanceId);
            EquipmentAction(body, 2, "WeaponRefine", selected.RefineMode == "copy" ? "중복 획득으로 자동 정련" : "정련 확인", "weaponRefine", selected.InstanceId);
            body.Find("EquipmentAction:WeaponRefine").GetComponent<Button>().interactable &= selected.RefineMode != "copy";
            if (selected.EquippedBy != null) EquipmentAction(body, 3, "Unequip", "장착 해제 확인", "weaponEquip", selected.InstanceId, new JObject { ["charId"] = JValue.CreateNull() });
            float equipY = equipmentActionY + 2 * pitch + 24;
            var equipCaption = Label(body, "장착할 보유 동료 · 무기 종류와 교체 결과는 서버에서 먼저 확인해요.", 16, equipY, 976, 64, 24, Memory);
            equipY += equipCaption.rectTransform.rect.height + 16;
            for (int n = 0; n < profile.Characters.Count; n++)
            {
                var character = profile.Characters[n]; string id = character.CharId;
                Button(body, "EquipmentEquip:" + id, EquipmentCharacterName(id) + "에게 장착 확인", n % 2 * 512, equipY + n / 2 * pitch, 496, height,
                    () => EquipmentReadPreview("weaponEquip", selected.InstanceId, new JObject { ["charId"] = id }), Line, 25).interactable = EquipmentReady;
            }
            body.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, equipY + (profile.Characters.Count + 1) / 2 * pitch + 24);
        }

        void RenderEquipmentShop()
        {
            var profile = AccountProgression!; var shop = profile.BossShop;
            if (shop == null)
            {
                Label(equipmentPanel, "보스 교환소의 현재 한도를 먼저 확인해주세요.\n계정을 새로고침해도 확인되지 않으면 아직 연결되지 않은 상태예요.", 96, 400, 1408, 144, 28, Ink);
                Button(equipmentPanel, "RefreshEquipmentShop", "계정 새로고침", 480, 632, 640, Math.Max(120, TouchUnits()), StartAccountProfileRefresh, Brass, 28).interactable = EquipmentIdle; return;
            }
            long? tokens = profile.Resources != null && profile.Resources.TryGetValue("boss_token", out var amount) ? amount : (long?)null;
            float height = Math.Max(192, Math.Max(TouchUnits(), MemoryForgeUiViewport.FontSize(25) * 3.4f + 32)), pitch = height + 64;
            var body = ScrollArea(equipmentPanel, 56, equipmentBodyY, 1488, EquipmentBodyHeight, 96 + shop.Items.Count * pitch);
            Label(body, "보스 토큰 " + (tokens.HasValue ? tokens.ToString() : "확인 필요") + "  ·  주간 기준 " + shop.WeekKey, 16, 0, 1440, 64, 27, Memory);
            for (int n = 0; n < shop.Items.Count; n++)
            {
                var row = shop.Items[n];
                Label(body, EquipmentMaterial(row.ItemId) + " 1개\n보스 토큰 " + row.UnitCost + " · 이번 주 남은 교환 " + row.Remaining + "/" + row.Limit,
                    16, 96 + n * pitch + 16, 944, height - 16, 28, Ink);
                Button(body, "EquipmentExchange:" + row.ItemId, "1개 교환 확인", 1008, 96 + n * pitch, 448, height,
                    () => EquipmentReadPreview("bossExchange", null, new JObject { ["itemId"] = row.ItemId, ["quantity"] = 1 }), Line, 28).interactable = EquipmentReady;
            }
        }

        void EquipmentReadPreview(string kind, string? target, JObject intent)
        {
            if (!EquipmentReady) return;
            var captured = recruitmentSession!; long revision = captured.Progression!.Revision;
            equipmentKind = kind; equipmentTarget = target; equipmentIntent = (JObject)intent.DeepClone(); equipmentPreview = null; equipmentNotice = "";
            growthBusy = true; var task = captured.PreviewEquipmentAsync(revision, kind, target, equipmentIntent);
            RenderEquipment(); StartCoroutine(AwaitEquipmentPreview(captured, revision, task));
        }
        IEnumerator AwaitEquipmentPreview(PracticeServerSession captured, long revision, Task<EquipmentPreviewResult> task)
        {
            while (!task.IsCompleted) yield return null;
            growthBusy = false;
            if (!ReferenceEquals(captured, recruitmentSession)) yield break;
            equipmentPreview = !task.IsFaulted && !task.IsCanceled ? task.Result.View : null;
            equipmentNotice = task.IsFaulted || task.IsCanceled ? "미리보기를 확인하지 못했어요. 다시 확인해주세요."
                : equipmentPreview == null ? EquipmentReason(task.Result.Code) : equipmentPreview.CanCommit ? "확인 버튼을 누르기 전에는 아무것도 바뀌지 않아요." : EquipmentReason(equipmentPreview.Code);
            if (captured.Progression?.Revision != revision) { equipmentPreview = null; equipmentNotice = EquipmentReason("STALE_REVISION"); }
            RenderEquipment();
        }

        void RenderEquipmentPreview()
        {
            var profile = AccountProgression!; var preview = equipmentPreview; float height = Math.Max(192, Math.Max(TouchUnits(), MemoryForgeUiViewport.FontSize(25) * 3.4f + 32));
            var body = ScrollArea(equipmentPanel, 56, equipmentBodyY, 1488, Math.Max(160, 680 - equipmentBodyY), 1200);
            Label(body, EquipmentActionName(equipmentKind!, equipmentIntent), 24, 16, 1440, 64, 34, Brass, FontStyle.Bold);
            string details = preview == null ? "서버가 허용한 변경 내역을 아직 확인하지 못했어요.\n" + equipmentNotice : EquipmentChange(preview);
            Label(body, details, 24, 104, 1440, 300, 28, Ink);
            float y = 440;
            if (preview != null)
            {
                if (preview.Cost.Count == 0) { Label(body, "소모 재료 없음", 24, y, 1440, 52, 27, Memory); y += 72; }
                foreach (var cost in preview.Cost)
                {
                    long? held = profile.Resources != null && profile.Resources.TryGetValue(cost.Key, out var value) ? value : (long?)null;
                    string balance = !held.HasValue ? "현재 수량 확인 필요" : held.Value < cost.Value ? held + " 보유 · " + (cost.Value - held.Value) + " 부족" : held + " → " + (held.Value - cost.Value);
                    Label(body, EquipmentMaterial(cost.Key) + "  " + balance + "  ·  소모 " + cost.Value, 24, y, 1440, 64, 28, Ink); y += 80;
                }
                if (!preview.CanCommit) { Label(body, EquipmentReason(preview.Code), 24, y, 1440, 112, 28, Memory); y += 128; }
            }
            Label(body, "새 출격에는 변경한 성장이 반영돼요. 이미 시작한 탐사는 출발 당시 상태를 유지해요.", 24, y + 16, 1440, 112, 25, Muted);
            body.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(484, y + 144));
            Button(equipmentPanel, "CancelEquipmentPreview", "공방으로", 56, 704, 704, height,
                () => { EquipmentClearPreview(); RenderEquipment(); }, Line, 28).interactable = EquipmentIdle;
            Button(equipmentPanel, "ConfirmEquipment", preview == null ? "다시 미리보기" : "확인 · 변경하기", 840, 704, 704, height,
                () => { if (equipmentPreview == null && equipmentKind != null) EquipmentReadPreview(equipmentKind, equipmentTarget, equipmentIntent); else EquipmentCommit(); }, Brass, 30)
                .interactable = EquipmentReady && (preview == null || preview.CanCommit && preview.Revision == profile.Revision);
        }

        void EquipmentCommit()
        {
            if (!EquipmentReady || equipmentPreview?.CanCommit != true || equipmentPreview.Revision != AccountProgression!.Revision || equipmentKind == null) return;
            var captured = recruitmentSession!; growthBusy = true;
            var task = captured.CommitEquipmentAsync(equipmentPreview.Revision, equipmentKind, equipmentTarget, (JObject)equipmentIntent.DeepClone());
            RenderEquipment(); StartCoroutine(AwaitEquipmentMutation(captured, task));
        }
        void EquipmentRetry()
        {
            if (!EquipmentIdle || recruitmentSession == null) return;
            var captured = recruitmentSession; growthBusy = true; var task = captured.RetryEquipmentPendingAsync();
            RenderEquipment(); StartCoroutine(AwaitEquipmentMutation(captured, task));
        }
        IEnumerator AwaitEquipmentMutation(PracticeServerSession captured, Task<EquipmentMutationResult> task)
        {
            while (!task.IsCompleted) yield return null;
            growthBusy = false;
            if (!ReferenceEquals(captured, recruitmentSession)) yield break;
            EquipmentClearPreview();
            equipmentNotice = task.IsFaulted || task.IsCanceled ? "결과를 확인하지 못했어요. 보존된 요청이 있으면 같은 요청으로 확인해주세요."
                : MemoryForgeGrowthClient.MutationNotice(task.Result.Status, task.Result.Code, task.Result.RefreshCode, task.Result.OutboxCode);
            RefreshHomeAccount(); RenderEquipment();
        }

        static string EquipmentStats(GrowthStats? stats) => stats == null ? "전투 수치는 서버 미리보기에서 확인할 수 있어요." : "위력 " + stats.Power + " · 체력 " + stats.Hp + " · 장신구 가산 " + (stats.GearBps / 100f).ToString("0.##") + "%";
        static string EquipmentType(string? type) => type switch { "ranged" => "원거리", "melee" => "근접", "device" => "장치", "ritual" => "의식", _ => "종류 확인 필요" };
        string EquipmentChange(PracticeEquipmentPreview p)
        {
            if (p.CharacterBefore != null && p.CharacterAfter != null)
            {
                var b = p.CharacterBefore; var a = p.CharacterAfter;
                return EquipmentCharacterName(b.CharId) + "\n승급 " + b.Asc + " → " + a.Asc + "  ·  레벨 상한 " + b.Cap + " → " + a.Cap
                    + "\n고유 카드 " + b.SkillLv + " → " + a.SkillLv + " (적용 " + b.EffectiveSkillLv + " → " + a.EffectiveSkillLv + ")"
                    + "\n필살기 " + b.UltLv + " → " + a.UltLv + " (적용 " + b.EffectiveUltLv + " → " + a.EffectiveUltLv + ")"
                    + "\n갑주 " + EquipmentGear(b.Armor) + " → " + EquipmentGear(a.Armor) + "  ·  장신구 " + EquipmentGear(b.Accessory) + " → " + EquipmentGear(a.Accessory)
                    + "\n" + EquipmentStats(b.Stats) + "\n→ " + EquipmentStats(a.Stats);
            }
            if (p.WeaponBefore != null && p.WeaponAfter != null)
            { var b = p.WeaponBefore; var a = p.WeaponAfter; return EquipmentWeaponName(b.InstanceId) + "\n레벨 " + b.Level + " → " + a.Level + "\n승급 " + b.Asc + " → " + a.Asc + " · 레벨 상한 " + b.Cap + " → " + a.Cap + "\n정련 " + b.Refine + " → " + a.Refine; }
            if (p.AssignmentBefore != null && p.AssignmentAfter != null)
            {
                var b = p.AssignmentBefore; var a = p.AssignmentAfter;
                return EquipmentWeaponName(b.InstanceId) + "\n장착 동료 " + EquipmentCharacterName(b.EquippedBy) + " → " + EquipmentCharacterName(a.EquippedBy)
                    + (a.DisplacedInstanceId == null ? "" : "\n기존 무기 「" + EquipmentWeaponName(a.DisplacedInstanceId) + "」는 미장착 상태로 보존돼요.")
                    + (b.EquippedBy == null || b.EquippedBy == a.EquippedBy ? "" : "\n「" + EquipmentCharacterName(b.EquippedBy) + "」의 기존 무기는 다른 무기로 자동 교체되지 않아요.");
            }
            if (p.ShopBefore != null && p.ShopAfter != null)
            { var b = p.ShopBefore; var a = p.ShopAfter; return EquipmentMaterial(b.ItemId) + "\n보유 " + b.Balance + " → " + a.Balance + "\n이번 주 교환 가능 " + b.Remaining + " → " + a.Remaining + "\n주간 기준 " + b.WeekKey; }
            return "변경 내역 확인이 필요해요.";
        }
        static string EquipmentActionName(string kind, JObject args) => kind switch
        {
            "characterAscend" => "동료 승급", "skillUp" => (string?)args["line"] == "ult" ? "필살기 스킬 성장" : "고유 카드 스킬 성장",
            "gearCraft" => (string?)args["slot"] == "armor" ? "갑주 제작" : "장신구 제작", "gearEnhance" => (string?)args["slot"] == "armor" ? "갑주 강화" : "장신구 강화",
            "weaponLevelUp" => "무기 레벨 성장", "weaponAscend" => "무기 승급", "weaponRefine" => "기본 무기 정련", "weaponEquip" => args["charId"]?.Type == JTokenType.Null ? "무기 장착 해제" : "무기 장착 변경", "bossExchange" => "보스 토큰 교환", _ => "변경 확인"
        };
        static string EquipmentReason(string? code) => code switch
        {
            "INSUFFICIENT_RESOURCES" => "필요 재료가 부족해요.", "ACCOUNT_LEVEL_REQUIRED" => "필요한 계정 레벨에 아직 도달하지 않았어요.",
            "ASCENSION_LEVEL_REQUIRED" => "현재 승급 단계의 레벨 상한까지 먼저 성장해야 해요.", "ASCENSION_CAP" => "최대 승급 단계예요.",
            "SKILL_CAP" => "스킬이 최대 레벨이에요.", "GEAR_CAP" => "장비가 최대 강화 단계예요.", "GEAR_ALREADY_CRAFTED" => "이미 제작한 장비예요.",
            "GEAR_NOT_CRAFTED" => "먼저 장비를 제작해주세요.", "WEAPON_TYPE_MISMATCH" => "이 동료와 무기의 종류가 맞지 않아요.", "ALREADY_EQUIPPED" => "이미 같은 장착 상태예요.",
            "AUTO_REFINED" => "이 무기는 중복 획득으로 자동 정련돼요.", "REFINE_CAP" => "최대 정련 단계예요.", "WEAPON_NOT_OWNED" => "현재 보유한 무기가 아니에요.",
            "NOT_OWNED" => "현재 보유한 동료가 아니에요.", "EXCHANGE_LIMIT" => "이번 주 교환 한도를 모두 사용했어요.", "STALE_REVISION" => "계정 정보가 바뀌었어요. 새로고침 후 다시 확인해주세요.",
            "TUTORIAL_REQUIRED" => "첫 기억을 먼저 완료해주세요.", "LEVEL_CAP" => "현재 레벨 상한에 도달했어요.", _ => "서버의 조건을 확인하지 못했어요. 계정을 새로고침한 뒤 다시 확인해주세요."
        };
    }
}
