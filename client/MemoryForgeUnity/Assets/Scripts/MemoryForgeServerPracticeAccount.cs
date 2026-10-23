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
            MemoryForgeUiViewport.Cover(background.GetComponent<UnityEngine.UI.Image>());
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
                var size = image.rectTransform.rect.size;
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
            Label(frame!, "동료 원정" + (Battle() is JObject battleHeader ? " · " + Int(battleHeader["turn"]) + "턴" : ""), 200, 12, 500, 52, 30, Brass);
            Label(frame!, busy ? "서버 확인 중…" : session?.State.Pending != null ? "미확정 요청 보존 · 같은 요청 확인" : SnapshotStale ? "마지막 확인 정보 · 새로고침 필요" : session == null ? "연결 확인 필요" : "서버 상태와 일치해요", 200, 76, 568, 48, 18, SnapshotStale ? Danger : Muted);
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

        // The phone keeps a stable header, scrollable gauge/hand strips and bottom action band.
        // These are the same published state and commands as the desktop view.
        void MobileBattleView(JObject battle)
        {
            var party = Box(frame!, "Party", 32, 144, 380, 304, Panel);
            Label(party, "체력  " + Int(battle["hp"]) + " / " + Int(battle["maxHp"]), 16, 12, 348, 56, 24, Ink, UnityEngine.FontStyle.Bold);
            Bar(party, 16, 80, 348, 12, Int(battle["hp"]) ?? 0, Int(battle["maxHp"]) ?? 0, Memory);
            Label(party, "방어 " + Int(battle["block"]) + " · 행동력 " + Int(battle["ap"]) , 16, 104, 348, 56, 20, Brass);
            var actors = Actors(battle);
            float row = Math.Max(112, MemoryForgeExpeditionClient.TouchUnits());
            var gauges = Scroll(party, "Actors", 12, 176, 356, row, Math.Max(356, actors.Count * 320), row, Panel);
            for (int i = 0; i < actors.Count; i++)
            {
                var actor = actors[i]; string id = Str(actor["id"]) ?? ""; bool full = GaugeFull(actor, battle);
                var ultimate = Button(gauges, "Ult:" + id, "", i * 320, 0, 308, row,
                    () => Launch(() => UltAsync(id)), full ? Brass : Line, 20);
                ultimate.interactable = full && CanCommand && Str(battle["phase"]) == "PLAYER";
                Label(ultimate.transform, ActorName(actor), 12, 4, 284, 52, 20, full ? Background : Ink);
                Label(ultimate.transform, Int(actor["gauge"]) + "/" + Int(battle["gaugeMax"]) + (full ? " · 필살기" : " · 충전 중"), 12, 60, 284, 44, 18, full ? Background : Memory);

            }
            var enemies = Enemies(); var losses = PreviewLosses();
            var foes = Box(frame!, "Enemies", 428, 144, 1140, 304, Panel);
            var enemyStrip = Scroll(foes, "EnemyStrip", 0, 0, 1140, 304, Math.Max(1140, enemies.Count * 380), 304, Color.clear);
            float width = Math.Max(380, 1140f / Math.Max(1, enemies.Count));
            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i]; string uid = Str(enemy["uid"]) ?? ""; bool alive = Bool(enemy["alive"]);
                int hp = Int(enemy["hp"]) ?? 0, loss = losses.TryGetValue(uid, out int predicted) && alive ? predicted : 0;
                var tile = Button(enemyStrip, "Enemy:" + uid, "", i * width + 6, 6, width - 12, 292, () => SelectEnemy(uid), uid == target ? Brass : Line);
                if (enemies.Count <= 3) { var tileRect = (RectTransform)tile.transform; tileRect.anchorMin = new Vector2((float)i / enemies.Count, 1); tileRect.anchorMax = new Vector2((float)(i + 1) / enemies.Count, 1); tileRect.sizeDelta = new Vector2(-12, 292); tileRect.anchoredPosition = new Vector2(6, -6); }
                tile.interactable = alive && !SelectionLocked();
                if (accountScene) AccountEnemyArt(tile.transform, Str(enemy["id"]), width - 12, alive, false);
                Label(tile.transform, (Str(enemy["name"]) ?? "적") + (alive ? "" : " · 처치"), 16, 12, width - 44, 52, 25, Ink, UnityEngine.FontStyle.Bold);
                Label(tile.transform, "체력 " + hp + (loss > 0 ? " → " + Math.Max(0, hp - loss) : "") + " / " + Int(enemy["maxHp"]), 16, 76, width - 44, 48, 21, Ink);
                var hpBar = Bar(tile.transform, 16, 132, width - 44, 12, hp, Int(enemy["maxHp"]) ?? 0, Danger);
                if (loss > 0) PreviewLoss(hpBar, hp, Int(enemy["maxHp"]) ?? 0, loss);
                Label(tile.transform, "방어 " + Int(enemy["block"]) + (uid == target ? " · 선택한 대상" : ""), 16, 148, width - 44, 40, 18, Memory);
                if (alive) Label(tile.transform, "이번 " + MoveText(enemy["intent"]) + "\n다음 " + MoveText(enemy["next"]), 16, 196, width - 44, 88, 19, Brass);
            }
            var reserved = (battle["reservations"] as JArray)?.OfType<JObject>().Select(r => CardName(Str(r["id"])) + " " + Int(r["due"]) + "턴").ToArray() ?? Array.Empty<string>();
            var strip = Box(frame!, "Presentation", 32, 460, 1536, Math.Max(112, row), Panel);
            presentationText = Label(strip, reserved.Length > 0 ? string.Join(" · ", reserved) : prepared != null ? "사용 대기: " + CardName(Str(Hand(prepared.Uid)?["id"])) : "행동 결과", 16, 12, 720, row - 24, 16, Memory);
            if (rows != null && !barrier) { UpdatePresentation(); Button(strip, "SkipPresentation", "건너뛰기", 752, 0, 224, row, SkipPresentation, Line, 19); }
            PaceButton(strip, MemoryForgeExpeditionClient.ActionPacing.Normal, "보통", 996, 120);
            PaceButton(strip, MemoryForgeExpeditionClient.ActionPacing.Double, "2배속", 1128, 136);
            PaceButton(strip, MemoryForgeExpeditionClient.ActionPacing.Reduced, "동작 줄이기", 1276, 244);
            var hand = (battle["hand"] as JArray)?.OfType<JObject>().ToList() ?? new System.Collections.Generic.List<JObject>();
            var cards = Scroll(frame!, "Hand", 32, 584, 1536, row, Math.Max(1536, hand.Count * 504), row, Panel);
            // The full card name and cost are also in the selected-card action/detail view.
            for (int i = 0; i < hand.Count; i++)
            {
                var card = hand[i]; string uid = Str(card["uid"]) ?? "";
                var cardButton = Button(cards, "Card:" + uid, "", i * 504, 0, 488, row, () => SelectCard(uid), uid == selectedCard ? Memory : Line, 20);
                cardButton.interactable = !SelectionLocked();
                Label(cardButton.transform, CardName(Str(card["id"])) + (Bool(card["plus"]) ? "+" : ""), 12, 4, 464, 52, 20, uid == selectedCard ? Background : Ink);
                Label(cardButton.transform, Int(card["cost"]) is int cost ? "행동력 " + cost : "사용 불가", 12, 60, 464, 44, 18, uid == selectedCard ? Background : Memory);

            }
            ActionView();
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
