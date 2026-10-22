#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MemoryForge.Client
{
    public sealed partial class MemoryForgeServerPracticeClient
    {
        bool accountScene, accountAbandonOpen, accountReturnAfterOperation;
        public Action<string>? SceneLoader { get; set; }
        readonly Dictionary<string, Sprite?> accountSprites = new Dictionary<string, Sprite?>();

        void OnApplicationQuit() => MemoryForgeAccountConnection.SetCurrent(null);
        void OnDestroy() { foreach (var sprite in accountSprites.Values) if (sprite != null) Destroy(sprite); }

        Sprite? AccountSprite(string resource)
        {
            if (accountSprites.TryGetValue(resource, out var cached)) return cached;
            var texture = Resources.Load<Texture2D>("Art/" + resource);
            return accountSprites[resource] = texture == null ? null : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, resource.EndsWith("ui_panel_frame", StringComparison.Ordinal) ? new Vector4(48, 48, 48, 48) : Vector4.zero);
        }

        void AccountScenery(Transform parent)
        {
            var background = Box(parent, "AccountBackdrop", 0, 0, 1600, 900, Color.white);
            background.anchorMin = Vector2.zero; background.anchorMax = Vector2.one; background.offsetMin = background.offsetMax = Vector2.zero;
            background.GetComponent<UnityEngine.UI.Image>().sprite = AccountSprite(Snapshot?.Run?.Phase == "BATTLE" ? "Backgrounds/bg_zone1_battle"
                : Snapshot?.Run?.Phase == "MAP" ? "Backgrounds/bg_zone1_map" : "Backgrounds/bg_workshop");
        }

        void AccountPartyArt(Transform parent, JObject battle)
        {
            var actors = Actors(battle);
            for (int n = 0; n < actors.Count; n++)
            {
                var portrait = Box(parent, "PartyPortrait:" + n, 14 + n * 122, 272, 108, 92, Color.white);
                var image = portrait.GetComponent<UnityEngine.UI.Image>(); image.sprite = AccountSprite("Characters/char_" + Str(actors[n]["id"]) + "_sd"); image.preserveAspect = true;
                if (image.sprite == null) image.color = Color.clear;
            }
        }

        void AccountEnemyArt(Transform parent, string? enemy, float width, bool alive, bool expanded)
        {
            var picture = Box(parent, "EnemyIllustration", 24, expanded ? 100 : 72, width - 48, expanded ? 164 : 152,
                new Color(1, 1, 1, expanded ? (alive ? 1 : 0.35f) : 0.22f));
            var image = picture.GetComponent<UnityEngine.UI.Image>(); image.sprite = AccountSprite("Enemies/enemy_" + enemy); image.preserveAspect = true;
            if (image.sprite == null) image.color = Color.clear;
        }

        void AccountFrames()
        {
            if (frame == null) return;
            foreach (var image in frame.GetComponentsInChildren<UnityEngine.UI.Image>().Where(item => item.transform.parent == frame && item.color == Panel
                || item.transform.name.StartsWith("Enemy:", StringComparison.Ordinal)).ToArray())
            {
                image.color = new Color(Panel.r, Panel.g, Panel.b, 0.92f);
                var size = image.rectTransform.sizeDelta;
                var rim = Box(image.transform, "WorkshopFrame", 0, 0, size.x, size.y, Color.white).GetComponent<UnityEngine.UI.Image>();
                rim.sprite = AccountSprite("UI/battle/ui_panel_frame"); rim.type = UnityEngine.UI.Image.Type.Sliced;
            }
        }

        async Task<bool> AccountUltimate(string actor, JObject args, string name)
        {
            var captured = session; var before = captured?.State.Snapshot?.Run;
            bool committed = await Mutate((current, context) => current.CommandAsync(context, "ult", args), true, false, name + "의 필살기를 사용했어요.");
            var after = session?.State.Snapshot?.Run;
            if (committed && ReferenceEquals(captured, session) && before != null && after?.RunId == before.RunId && after.ServerVersion == before.ServerVersion + 1
                && session?.State.Pending == null && session?.State.NeedsRefresh == false && this != null && isActiveAndEnabled)
            {
                string title;
                try { title = ClientBattleContent.Load().Battle.Characters.FirstOrDefault(character => character.Id == actor)?.Ult.Name ?? "필살기"; }
                catch (System.IO.InvalidDataException) { title = "필살기"; }
                StartCoroutine(MemoryForgeCombatCutin.Play(transform, font!, AccountSprite("Characters/char_" + actor + "_cutin"), name, title, "AccountUltimateCutin"));
            }
            return committed;
        }

        void AccountHeader()
        {
            Button(frame!, "ReturnAccountHome", "공방", 32, 12, 144, 76, ReturnAccountHome, Line, 24).interactable = !busy;
            Label(frame!, "동료 원정", 200, 12, 500, 36, 30, Brass);
            Label(frame!, StatusText(), 200, 52, 568, 32, 18, SnapshotStale ? Danger : Muted);
            if (Snapshot?.Run?.Phase == "BATTLE") RunSupportEntry(784, 12, 216, 76);
            Button(frame!, "Refresh", "새로고침", 1016, 12, 224, 76, () => Launch(RefreshAsync), Line, 22).interactable = session != null && !busy;
            if (session?.State.Pending != null)
                Button(frame!, "Retry", "같은 요청 확인", 1252, 12, 316, 76, () => Launch(RetryAsync), Brass, 22).interactable = !busy;
            else if (Snapshot?.Run != null && Snapshot.Run.Phase != "DONE")
                Button(frame!, "RequestAccountAbandon", "탐사 포기", 1252, 12, 316, 76,
                    () => { if (!CanCommand) return; DropUnsent(); accountAbandonOpen = true; Render(); }, Line, 22).interactable = CanCommand;
        }

        public void ReturnAccountHome()
        {
            if (busy) return;
            DropUnsent(); accountAbandonOpen = false;
            try { (SceneLoader ?? (destination => SceneManager.LoadScene(destination)))(MemoryForgeExpeditionClient.LobbyScene); }
            catch (Exception) { Say("공방으로 돌아가지 못했어요. 탐사는 보존되어 있어요.", true); Render(); }
        }

        void AccountAbandonView()
        {
            var shield = Box(frame!, "AccountAbandonConfirmation", 0, 0, 1600, 900, new Color(0.01f, 0.02f, 0.04f, 0.94f));
            shield.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            var paper = Box(shield, "ConfirmationPaper", 240, 176, 1120, 548, Panel);
            Label(paper, "탐사를 포기할까요?", 56, 40, 1008, 76, 38, Brass);
            Label(paper, "이번 탐사는 닫히고 기억은 새로 저장되지 않아요.\n기존 기억 덱과 보유 동료는 그대로 남습니다.\n공방으로만 돌아가려면 취소 후 '공방'을 선택해주세요.", 56, 156, 1008, 156, 26, Ink);
            Button(paper, "CancelAccountAbandon", "계속 탐사", 56, 368, 472, 120,
                () => { accountAbandonOpen = false; Render(); }, Line, 28).interactable = !busy;
            Button(paper, "ConfirmAccountAbandon", "탐사 포기", 552, 368, 512, 120, () => Launch(AbandonAccountRunAsync), Brass, 28).interactable = !busy;
        }

        public Task<bool> AbandonAccountRunAsync()
        {
            if (!accountAbandonOpen || !CanCommand || session?.State.Snapshot?.Run == null) return Refuse("진행 중인 탐사와 최신 상태를 확인해주세요.");
            var context = session.State.Snapshot.Context;
            accountAbandonOpen = false;
            return Operate(async captured => HandleAccountAbandon(await captured.AbandonRunAsync(context), captured));
        }

        bool HandleAccountAbandon(ProgressionMutationResult result, PracticeServerSession captured)
        {
            if (!ReferenceEquals(captured, session)) return false;
            AfterSnapshotChange();
            if (result.Status == PracticeMutationStatus.Committed && result.AbandonReceipt != null && result.RefreshCode == null && result.OutboxCode == null
                && captured.State.Pending == null && captured.State.Snapshot != null && captured.State.Snapshot.Run?.RunId != result.AbandonReceipt.RunId)
            {
                Say(captured.State.Snapshot.Run == null ? "탐사를 포기했어요. 기억은 그대로예요." : "이전 탐사를 포기했어요. 계정에 다른 탐사가 진행 중이에요.", false);
                accountReturnAfterOperation = true;
                return true;
            }
            Say(result.Status == PracticeMutationStatus.Pending ? "포기 결과를 아직 확인하지 못했어요. 보존된 같은 요청을 다시 확인해주세요."
                : "서버에서 포기를 확인하지 못했어요. 새로고침해주세요.", true);
            return false;
        }

        async Task<bool> CommitSaveAndRefreshAccount(PracticeUiContext context, string[] ids, string success)
        {
            bool committed = await Mutate((captured, current) => captured.CommitMemoryAsync(context, ids), false, false, success);
            var captured = session;
            if (!committed || !accountScene || captured == null) return committed;
            return await Operate(async current =>
            {
                var recruitment = await current.RefreshRecruitmentAsync();
                var progression = await current.RefreshProgressionAsync();
                if (!ReferenceEquals(current, session)) return false;
                if (recruitment.Code != PracticeSessionCodes.Ok || progression.Code != PracticeSessionCodes.Ok)
                    Say("기억은 저장됐어요. 계정 보상은 공방에서 새로고침해 확인해주세요.", true);
                return true;
            });
        }
    }
}
