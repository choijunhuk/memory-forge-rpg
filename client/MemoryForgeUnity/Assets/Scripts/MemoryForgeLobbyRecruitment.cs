#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    public sealed partial class MemoryForgeLobbyClient
    {
        RectTransform recruitmentPanel = null!, recruitmentReveal = null!;
        PracticeServerSession? recruitmentSession;
        string recruitmentNotice = "", recruitmentBanner = "standard";
        bool recruitmentBusy, revealSkipped, recruitmentRevealing;
        Coroutine? revealRoutine;
        Text? recruitmentDockLabel;
        IReadOnlyList<RecruitmentPull>? revealedResults;
        RecruitmentReceipt? latestRecruitmentReceipt;

        void BuildRecruitment()
        {
            recruitmentPanel = Box(root, "RecruitmentPanel", 0, 0, 1600, 900, Background);
            recruitmentPanel.gameObject.SetActive(false);
            recruitmentReveal = Box(root, "RecruitmentReveal", 0, 0, 1600, 900, Background);
            recruitmentReveal.gameObject.SetActive(false);
            recruitmentDockLabel = homeControls.transform.Find("HomeDock/OpenRecruitment")?.GetComponentInChildren<Text>();
        }

        void OnDestroy()
        {
            recruitmentSession?.Dispose(); recruitmentSession = null;
            foreach (var sprite in sprites.Values) if (sprite != null) Destroy(sprite);
        }

        void OpenRecruitment()
        {
            player.Stop(); RenderRecruitment(); ShowPanel(recruitmentPanel);
            if (recruitmentDockLabel != null) recruitmentDockLabel.text = "모집";
            FocusRecruitment(recruitmentPanel);
        }

        void RenderRecruitment()
        {
            ClearPanel(recruitmentPanel);
            var wall = Box(recruitmentPanel, "RecruitmentWorkshop", 0, 0, 1600, 900, new Color(0.22f, 0.26f, 0.35f));
            wall.GetComponent<Image>().sprite = SpriteAt("Art/Backgrounds/bg_workshop");
            Box(recruitmentPanel, "RecruitmentVeil", 0, 0, 1600, 900, new Color(0.025f, 0.035f, 0.07f, 0.76f));
            Glyph(recruitmentPanel, "nav_recruit", 36, 12, 112, Color.white);
            Label(recruitmentPanel, "모집", 152, 28, 300, 76, 44, Ink, FontStyle.Bold);
            var subtitle = Label(recruitmentPanel, "공방의 문 너머, 새로운 인연", 154, 106, 650, 42, 22, Muted);
            Button(recruitmentPanel, "CloseRecruitment", "홈", 1392, 24, 152, 112, ClosePanels, Line, 24);
            var snapshot = recruitmentSession?.Recruitment;
            if (snapshot == null)
            {
                RenderRecruitmentConnection(); return;
            }
            subtitle.text = "";
            Label(recruitmentPanel, "개발 계정 · " + snapshot.AccountId, 816, 30, 540, 40, 19, Memory, FontStyle.Normal, TextAnchor.MiddleRight);
            Label(recruitmentPanel, (recruitmentSession!.RecruitmentNeedsRefresh ? "마지막 확인 · " : "") + "결정 " + (snapshot.FreeCrystals + snapshot.PaidCrystals) + "   ·   상시 티켓 " + snapshot.Tickets["standardTicket"], 750, 82, 604, 44, 24, Brass, FontStyle.Bold, TextAnchor.MiddleRight);
            var banner = snapshot.Banners.FirstOrDefault(x => x.Id == recruitmentBanner) ?? snapshot.Banners.FirstOrDefault();
            if (banner == null) { Label(recruitmentPanel, "현재 모집할 수 있는 배너가 없어요.", 360, 360, 880, 100, 30, Ink); return; }
            recruitmentBanner = banner.Id;
            var art = Box(recruitmentPanel, "RecruitmentBannerArt", 24, 106, 800, 680, Color.white);
            art.GetComponent<Image>().sprite = Portrait("sera"); art.GetComponent<Image>().preserveAspect = true;
            if (art.GetComponent<Image>().sprite == null) art.GetComponent<Image>().color = Color.clear;
            Shade(recruitmentPanel, "RecruitmentBannerShade", 56, 570, 740, 160, new Color(0.03f, 0.04f, 0.075f, 0.97f), false);
            Label(recruitmentPanel, banner.Name, 82, 592, 680, 68, 40, Ink, FontStyle.Bold);
            Label(recruitmentPanel, "상시 SSR 동료가 같은 확률로 등장합니다.", 84, 670, 676, 42, 22, Muted);
            float tabWidth = Math.Min(304, 640f / snapshot.Banners.Count);
            for (int i = 0; i < snapshot.Banners.Count; i++)
            {
                var b = snapshot.Banners[i];
                var tab = Button(recruitmentPanel, "Banner:" + b.Id, b.Kind == "beginner" ? "첫 공방" : "상시 모집", 856 + i * (tabWidth + 12), 176, tabWidth, 112,
                    () => { recruitmentBanner = b.Id; RenderRecruitment(); }, b.Id == banner.Id ? Brass : Panel, 26);
                tab.interactable = !recruitmentBusy;
            }
            var pity = snapshot.Pity[banner.Kind];
            Label(recruitmentPanel, "다음 만남을 향한 기록", 872, 318, 620, 42, 24, Memory, FontStyle.Bold);
            int maxPulls = banner.Curve.MaxPulls ?? banner.Curve.Hard;
            string guarantee = banner.Kind != "beginner" ? "SSR 확정까지 최대 " + (banner.Curve.Hard - pity.N) + "회"
                : pity.Done || pity.Total >= maxPulls ? "첫 공방 모집 완료"
                : pity.GotSsr ? "SSR 획득 완료 · 남은 " + (maxPulls - pity.Total) + "회"
                : "SSR 1명 확정까지 최대 " + (maxPulls - pity.Total) + "회";
            Label(recruitmentPanel, guarantee, 872, 378, 620, 64, 34, Ink, FontStyle.Bold);
            string rule = banner.Kind == "beginner" ? "10회 전용 · 최대 " + maxPulls + "회\n10회당 상시 티켓 " + banner.TenPullTickets + "장 상당" : "1회당 티켓 1장 또는 결정 " + banner.CostPerPull;
            var standard = snapshot.Pity["standard"];
            if (banner.Kind == "standard" && banner.Curve.SelectorAt is int selectorAt && selectorAt > 0 && standard.Total >= selectorAt && !standard.SelectorUsed)
                Button(recruitmentPanel, "OpenRecruitmentSelector", "SSR 동료 선택", 872, 456, 640, 112, () => ShowRecruitmentSelector(banner, snapshot.Revision), Brass, 28).interactable = CanRecruitmentAct();
            else Label(recruitmentPanel, rule + "\n티켓 우선 사용 · 부족분은 무료 결정부터 사용", 874, 458, 620, 112, 22, Muted);
            var one = Button(recruitmentPanel, "RecruitOne", "1회 모집", 856, 600, 300, 120, () => ConfirmRecruitment(banner, 1), Line, 27);
            one.interactable = CanRecruitmentAct() && banner.Kind != "beginner";
            var ten = Button(recruitmentPanel, "RecruitTen", "10회 모집", 1176, 600, 368, 120, () => ConfirmRecruitment(banner, 10), Brass, 30);
            ten.interactable = CanRecruitmentAct() && !pity.Done && (banner.Kind != "beginner" || pity.Total < maxPulls);
            if (!snapshot.LaunchClaimed)
                Button(recruitmentPanel, "ClaimRecruitmentLaunch", "런칭 선물 받기", 56, 752, 340, 120, () => BeginRecruitmentAction("launch"), Brass, 26).interactable = CanRecruitmentAct();
            else
                Button(recruitmentPanel, "RecruitmentOwned", "보유 동료", 56, 752, 340, 120, () => { catalogueFilter = "owned"; OpenRoster(); }, Line, 26);
            Button(recruitmentPanel, "RecruitmentConnectionSettings", "연결 설정", 416, 752, 184, 120, () =>
            {
                recruitmentSession?.Dispose(); recruitmentSession = null; MemoryForgeAccountConnection.SetCurrent(null);
                recruitmentNotice = ""; RenderRecruitment();
                RefreshHomeAccount();
            }, Line, 20).interactable = !recruitmentBusy;
            var recent = recruitmentSession.RecruitmentNeedsRefresh && latestRecruitmentReceipt?.AccountId == snapshot.AccountId
                && latestRecruitmentReceipt.Revision > snapshot.Revision && latestRecruitmentReceipt.Results.Count > 0
                ? latestRecruitmentReceipt.Results : snapshot.History.FirstOrDefault()?.Results;
            if (recent != null)
                Button(recruitmentPanel, "RecruitmentLastResult", "최근 결과", 616, 752, 248, 120, () => PresentRecruitment(recent, false), Line, 24).interactable = !recruitmentBusy;
            var pending = recruitmentSession!.State.Pending;
            if (pending != null)
                Button(recruitmentPanel, "RetryRecruitmentPending", PracticeCommandOutbox.IsRecruitmentRoute(pending.Route) ? "미확정 모집 결과 확인" : PracticeCommandOutbox.IsEquipmentRoute(pending.Route) ? "미확정 장비 변경 확인" : PracticeCommandOutbox.IsSupportSelectionRoute(pending.Route) ? "미확정 지원 설정 확인" : "이전 원정 요청 확인", 880, 752, 664, 120,
                    () => { if (PracticeCommandOutbox.IsRecruitmentRoute(pending.Route)) BeginRecruitmentAction("retry"); else BeginPreviousRunRetry(); }, Brass, 24).interactable = !recruitmentBusy;
            else
            {
                Button(recruitmentPanel, "RecruitmentRates", "확률·구성 확인", 880, 752, 344, 120, () => ShowRecruitmentRates(banner), Line, 22);
                Button(recruitmentPanel, "RefreshRecruitment", "새로고침", 1288, 752, 256, 120, () => StartRecruitmentRefresh(), Line, 22).interactable = !recruitmentBusy;
            }
            string? outboxError = recruitmentSession.State.OutboxCode;
            if (outboxError != null)
            {
                Box(recruitmentPanel, "RecruitmentStorageNotice", 856, 310, 688, 266, Panel);
                Label(recruitmentPanel, RecruitmentMessage(outboxError), 880, 336, 640, 212, 26, Memory);
            }
            Label(recruitmentPanel, recruitmentBusy ? "서버에서 확인하고 있어요…" : recruitmentNotice, 858, 724, 680, 28, 18, Memory);
            FocusRecruitment(recruitmentPanel);
        }

        bool CanRecruitmentAct() => !recruitmentBusy && !accountBusy && !growthBusy && recruitmentSession?.Recruitment != null && !recruitmentSession.RecruitmentNeedsRefresh
            && !recruitmentSession.State.InFlight && recruitmentSession.State.Pending == null && recruitmentSession.State.OutboxCode == null;

        void FocusRecruitment(RectTransform panel)
        {
            if (!panel.gameObject.activeInHierarchy || EventSystem.current == null) return;
            var buttons = panel.GetComponentsInChildren<Button>().Where(x => x.IsInteractable()).ToList();
            var preferred = buttons.FirstOrDefault(x => x.name == "RecruitTen" || x.name == "RecruitmentResultDone" || x.name == "ConnectRecruitment" || x.name == "RetryRecruitmentPending") ?? buttons.FirstOrDefault();
            EventSystem.current.SetSelectedGameObject(preferred?.gameObject);
        }

        void ShowRecruitmentRates(RecruitmentBanner banner)
        {
            ClearPanel(recruitmentReveal);
            RecruitmentBackdrop();
            Label(recruitmentReveal, "모집 확률과 구성", 56, 28, 1200, 76, 40, Brass, FontStyle.Bold);
            Button(recruitmentReveal, "CloseRecruitmentRates", "돌아가기", 1240, 24, 304, 120, OpenRecruitment, Line, 24);
            var c = banner.Curve;
            string Percent(double bps) => (bps / 100).ToString("0.####") + "%";
            var curve = new MemoryForge.Core.GachaCurve { SsrBase = c.SsrBase, SoftStart = c.SoftStart, SoftStep = c.SoftStep, Hard = c.Hard };
            int srPool = banner.SrChars.Count + banner.SrWeapons.Count;
            var lines = new List<string> { banner.Name, "기본 SSR " + Percent(c.SsrBase) + " · 기본 SR " + Percent(c.SrBase),
                "SSR 종합 확률 (천장 포함): " + ConsolidatedRecruitmentPercent(banner).ToString("0.####") + "%",
                c.SrGuarantee + "회 안에 SR 이상", banner.Kind == "beginner" ? (c.MaxPulls ?? c.Hard) + "회 안에 SSR 동료 1명 확정" : c.Hard + "회에 SSR 확정",
                "", "개별 기본 확률 · 천장 보정에 따라 달라집니다", "SSR 동료" };
            lines.AddRange(banner.SsrPool.Select(id => (content.Battle.Characters.FirstOrDefault(x => x.Id == id)?.Name ?? id) + "  " + Percent(c.SsrBase / (double)Math.Max(1, banner.SsrPool.Count))));
            lines.Add("SR 동료와 무기");
            lines.AddRange(banner.SrChars.Select(id => (content.Battle.Characters.FirstOrDefault(x => x.Id == id)?.Name ?? id) + "  " + Percent(c.SrBase / (double)Math.Max(1, srPool))));
            lines.AddRange(banner.SrWeapons.Select(id => (content.Battle.Weapons.FirstOrDefault(x => x.Id == id)?.Name ?? id) + "  " + Percent(c.SrBase / (double)Math.Max(1, srPool))));
            lines.Add("R 재료");
            lines.AddRange(banner.RItems.Select(id => id + "  " + Percent(Math.Max(0, 10000 - c.SsrBase - c.SrBase) / (double)Math.Max(1, banner.RItems.Count))));
            if (c.SoftStart <= c.Hard)
            {
                lines.Add(""); lines.Add("SSR 증가 구간 · " + c.SoftStart + "회부터 확률 증가");
                lines.AddRange(Enumerable.Range(c.SoftStart, c.Hard - c.SoftStart + 1).Select(pull => pull + "회: " + Percent(MemoryForge.Core.Gacha.SsrRate(curve, pull - 1))));
            }
            float height = lines.Count * 46;
            var rows = ScrollArea(recruitmentReveal, 80, 166, 1440, 664, Math.Max(664, height));
            Label(rows, string.Join("\n", lines), 0, 0, 1400, height, 26, Ink);
            ShowPanel(recruitmentReveal);
        }

        static double ConsolidatedRecruitmentPercent(RecruitmentBanner banner)
        {
            var c = banner.Curve;
            var curve = new MemoryForge.Core.GachaCurve { SsrBase = c.SsrBase, SoftStart = c.SoftStart, SoftStep = c.SoftStep, Hard = c.Hard };
            if (banner.Kind != "beginner") return 100 / MemoryForge.Core.Gacha.MeanPullsPerSsr(curve);
            // A capped banner cannot renew forever: sum SSR expectation over its actual finite pull window.
            int cap = c.MaxPulls ?? c.Hard;
            var mass = new double[cap + 1, 2]; mass[0, 0] = 1;
            double expected = 0;
            for (int pull = 0; pull < cap; pull++)
            {
                var next = new double[cap + 1, 2];
                for (int n = 0; n <= pull; n++) for (int got = 0; got < 2; got++)
                {
                    double chance = got == 0 && n + 1 >= cap ? 1 : MemoryForge.Core.Gacha.SsrRate(curve, n) / 10000d;
                    double ssr = mass[n, got] * chance;
                    expected += ssr; next[0, 1] += ssr;
                    next[n + 1, got] += mass[n, got] - ssr;
                }
                mass = next;
            }
            return 100 * expected / cap;
        }

        void RenderRecruitmentConnection()
        {
            Label(recruitmentPanel, "공방 연결", 560, 192, 600, 64, 36, Ink, FontStyle.Bold);
            Label(recruitmentPanel, "현재 모집은 인증된 개발 서버에 연결해 사용할 수 있어요.\n보유 동료와 결과는 서버에 저장됩니다.", 560, 250, 920, 64, 24, Muted);
            float inputHeight = Math.Max(108, TouchUnits()), step = inputHeight + 24;
            var form = ScrollArea(recruitmentPanel, 180, 362, 1200, 510, step * 3 + 136);
            Label(form, "서버 주소", 20, 24, 320, 50, 24, Brass);
            var endpoint = RecruitmentInput(form, "RecruitmentEndpoint", PlayerPrefs.GetString("mf.account.endpoint", "http://127.0.0.1:5287"), 380, 0, inputHeight, false);
            Label(form, "개발 계정", 20, step + 24, 320, 50, 24, Brass);
            var account = RecruitmentInput(form, "RecruitmentAccount", PlayerPrefs.GetString("mf.account.alias", "dev-local"), 380, step, inputHeight, false);
            Label(form, "접속 키", 20, step * 2 + 24, 320, 50, 24, Brass);
            var credential = RecruitmentInput(form, "RecruitmentCredential", "", 380, step * 2, inputHeight, true);
            var connect = Button(form, "ConnectRecruitment", "공방에 연결", 380, step * 3, 620, 120, () =>
            {
                if (recruitmentBusy) return;
                string address = endpoint.text.Trim(), alias = account.text.Trim();
                string key = credential.text; credential.text = "";
                try
                {
                    Directory.CreateDirectory(Application.persistentDataPath);
                    var owner = new MemoryForgeAccountConnection(address, alias, key, Path.Combine(Application.persistentDataPath, "account-command-outbox.json"));
                    MemoryForgeAccountConnection.SetCurrent(owner);
                    ConnectRecruitment(owner.CreateSession);
                }
                catch (Exception error)
                {
                    if (Debug.isDebugBuild) Debug.LogWarning("MF_ACCOUNT_CONNECT_OWNER type=" + error.GetType().Name + " param=" + (error is ArgumentException argument ? argument.ParamName : "none") + " keyLength=" + key.Length);
                    recruitmentNotice = "서버 주소와 개발 계정 설정을 확인해주세요."; RenderRecruitment();
                }
            }, Brass, 28);
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            connect.interactable = false;
            Label(recruitmentPanel, "상용 계정 연결은 아직 준비 중이에요.", 560, 320, 920, 36, 22, Memory);
#else
            connect.interactable = !recruitmentBusy;
            Label(recruitmentPanel, recruitmentBusy ? "연결을 확인하고 있어요…" : recruitmentNotice, 560, 320, 940, 36, 20, Memory);
#endif
            FocusRecruitment(recruitmentPanel);
        }

        InputField RecruitmentInput(Transform parent, string name, string initial, float x, float y, float height, bool password)
        {
            var box = Box(parent, name, x, y, 760, height, Panel);
            box.GetComponent<Image>().raycastTarget = true;
            var field = box.gameObject.AddComponent<InputField>(); field.targetGraphic = box.GetComponent<Image>();
            var text = Label(box, "", 24, 16, 712, 76, 28, Ink, FontStyle.Normal, TextAnchor.MiddleLeft);
            text.resizeTextForBestFit = false; field.textComponent = text;
            field.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            field.characterLimit = password ? 512 : 128; field.lineType = InputField.LineType.SingleLine;
            field.text = initial; field.interactable = !recruitmentBusy;
            return field;
        }

        /// <summary>The same in-memory session boundary used by server practice; no credential is serialized by the lobby.</summary>
        public bool ConnectRecruitment(Func<PracticeServerSession> create)
        {
            if (recruitmentBusy) return false;
            try
            {
                var next = create(); recruitmentSession?.Dispose(); recruitmentSession = next; latestRecruitmentReceipt = null;
            }
            catch (Exception error)
            {
                if (Debug.isDebugBuild) Debug.LogWarning("MF_ACCOUNT_CONNECT_SESSION type=" + error.GetType().Name + " inner=" + (error.InnerException?.GetType().Name ?? "none"));
                recruitmentNotice = "서버 주소와 개발 계정 설정을 확인해주세요."; RenderRecruitment(); return false;
            }
            StartRecruitmentRefresh(); return true;
        }

        void StartRecruitmentRefresh()
        {
            if (recruitmentBusy || accountBusy || growthBusy || recruitmentSession == null) return;
            recruitmentBusy = true; RenderRecruitment();
            StartCoroutine(RefreshRecruitmentView(recruitmentSession.RefreshRecruitmentAsync()));
        }

        IEnumerator RefreshRecruitmentView(Task<RecruitmentRefreshResult> task)
        {
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted || task.IsCanceled) recruitmentNotice = "연결이 끊겼어요. 다시 확인해주세요.";
            else
            {
                recruitmentNotice = RecruitmentMessage(task.Result.Code);
                if (task.Result.Code == PracticeSessionCodes.Ok && recruitmentSession != null)
                {
                    PlayerPrefs.SetString("mf.account.endpoint", recruitmentSession.Endpoint); PlayerPrefs.SetString("mf.account.alias", recruitmentSession.Account);
                    yield return ReadAccountProgression(recruitmentSession);
                }
            }
            recruitmentBusy = false;
            RefreshHomeAccount();
            RepaintAccountPanels();
            RenderRecruitment();
        }

        void ConfirmRecruitment(RecruitmentBanner banner, int count)
        {
            if (!CanRecruitmentAct()) return;
            var snapshot = recruitmentSession!.Recruitment!;
            int tickets = snapshot.Tickets[banner.Ticket];
            int ticketCost = banner.Kind == "beginner" ? banner.TenPullTickets : count;
            long crystals = (long)Math.Max(0, ticketCost - tickets) * banner.CostPerPull;
            ClearPanel(recruitmentReveal);
            RecruitmentBackdrop();
            Box(recruitmentReveal, "ConfirmationPaper", 240, 140, 1120, 650, new Color(0.035f, 0.05f, 0.09f, 0.96f));
            Kit(recruitmentReveal, "ConfirmationFrame", "ui_panel_frame", new Vector4(48, 48, 48, 48), 240, 140, 1120, 650, 128);
            Ornament(recruitmentReveal, 480, 286, 640);
            Label(recruitmentReveal, "모집 확인", 320, 188, 960, 80, 42, Brass, FontStyle.Bold, TextAnchor.MiddleCenter);
            Label(recruitmentReveal, banner.Name + " · " + count + "회\n티켓 " + Math.Min(tickets, ticketCost) + "장 + 결정 " + crystals + "개를 사용합니다.", 320, 318, 960, 140, 32, Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
            bool affordable = snapshot.FreeCrystals + snapshot.PaidCrystals >= crystals;
            Label(recruitmentReveal, affordable ? "결과가 저장된 뒤 연출이 시작됩니다." : "티켓과 결정이 부족해요.", 320, 490, 960, 64, 24, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            Button(recruitmentReveal, "CancelRecruitment", "돌아가기", 360, 626, 400, 120, OpenRecruitment, Line, 28);
            Button(recruitmentReveal, "ConfirmRecruitment", "모집하기", 840, 626, 400, 120, () => BeginRecruitmentAction("pull", count, bannerId: banner.Id, expectedRevision: snapshot.Revision), Brass, 30).interactable = affordable;
            ShowPanel(recruitmentReveal);
        }

        void ShowRecruitmentSelector(RecruitmentBanner banner, long revision)
        {
            ClearPanel(recruitmentReveal); RecruitmentBackdrop();
            Label(recruitmentReveal, "함께할 SSR 동료를 선택하세요", 80, 36, 1220, 84, 40, Brass, FontStyle.Bold);
            Button(recruitmentReveal, "CloseRecruitmentSelector", "돌아가기", 1240, 24, 304, 120, OpenRecruitment, Line, 24);
            for (int i = 0; i < banner.SsrPool.Count; i++)
            {
                string id = banner.SsrPool[i];
                string name = content.Battle.Characters.FirstOrDefault(c => c.Id == id)?.Name ?? id;
                var choice = Button(recruitmentReveal, "RecruitmentSelector:" + id, "", 80 + i % 2 * 740, 190 + i / 2 * 264, 700, 240, () =>
                {
                    ClearPanel(recruitmentReveal); RecruitmentBackdrop();
                    Label(recruitmentReveal, "선택할 동료: " + name + "\nSSR 선택은 계정당 한 번만 받을 수 있어요.", 320, 260, 960, 180, 36, Ink, FontStyle.Normal, TextAnchor.MiddleCenter);
                    Button(recruitmentReveal, "CancelRecruitmentSelector", "돌아가기", 360, 626, 400, 120, () => ShowRecruitmentSelector(banner, revision), Line, 28);
                    Button(recruitmentReveal, "ConfirmRecruitmentSelector", "선택 확정", 840, 626, 400, 120, () => BeginRecruitmentAction("selector", charId: id, expectedRevision: revision), Brass, 30);
                    FocusRecruitment(recruitmentReveal);
                }, Panel);
                var art = Box(choice.transform, "SelectorPortrait", 20, 12, 180, 216, Color.white);
                art.GetComponent<Image>().sprite = Portrait(id); art.GetComponent<Image>().preserveAspect = true;
                if (art.GetComponent<Image>().sprite == null) art.GetComponent<Image>().color = Color.clear;
                Label(choice.transform, name, 236, 84, 432, 72, 40, Ink, FontStyle.Bold);
            }
            ShowPanel(recruitmentReveal);
        }

        void BeginPreviousRunRetry()
        {
            if (recruitmentBusy || accountBusy || growthBusy || recruitmentSession == null) return;
            recruitmentBusy = true; OpenRecruitment();
            var pending = recruitmentSession.State.Pending;
            if (pending != null && PracticeCommandOutbox.IsSupportSelectionRoute(pending.Route))
                StartCoroutine(AwaitSupportRecruitmentRetry(recruitmentSession, recruitmentSession.RetrySupportPendingAsync()));
            else if (pending != null && PracticeCommandOutbox.IsEquipmentRoute(pending.Route))
                StartCoroutine(AwaitEquipmentRecruitmentRetry(recruitmentSession, recruitmentSession.RetryEquipmentPendingAsync()));
            else if (pending != null && PracticeCommandOutbox.IsGrowthRoute(pending.Route))
                StartCoroutine(AwaitGrowthRecruitmentRetry(recruitmentSession.RetryGrowthPendingAsync()));
            else if (pending != null && (pending.Route == PracticeCommandOutbox.ExpeditionRoute || pending.Route.EndsWith("/abandon", StringComparison.Ordinal)))
                StartCoroutine(AwaitProgressionRetry(recruitmentSession.RetryProgressionPendingAsync()));
            else StartCoroutine(AwaitPreviousRunRetry(recruitmentSession.RetryPendingAsync()));
        }

        IEnumerator AwaitProgressionRetry(Task<ProgressionMutationResult> task)
        {
            while (!task.IsCompleted) yield return null;
            recruitmentBusy = false;
            if (!task.IsFaulted && !task.IsCanceled && task.Result.OutboxCode == null
                && (task.Result.Status == PracticeMutationStatus.Committed || task.Result.Status == PracticeMutationStatus.NotCommitted)) StartRecruitmentRefresh();
            else { recruitmentNotice = "이전 요청은 보존되어 있어요. 연결을 확인한 뒤 다시 결과를 확인해주세요."; RenderRecruitment(); }
        }

        IEnumerator AwaitEquipmentRecruitmentRetry(PracticeServerSession captured, Task<EquipmentMutationResult> task)
        {
            while (!task.IsCompleted) yield return null;
            if (!ReferenceEquals(captured, recruitmentSession)) yield break;
            recruitmentBusy = false;
            if (!task.IsFaulted && !task.IsCanceled && task.Result.OutboxCode == null
                && (task.Result.Status == PracticeMutationStatus.Committed || task.Result.Status == PracticeMutationStatus.NotCommitted)) StartRecruitmentRefresh();
            else { recruitmentNotice = "장비 변경 요청은 보존되어 있어요. 같은 요청으로 다시 확인해주세요."; RenderRecruitment(); }
        }

        IEnumerator AwaitGrowthRecruitmentRetry(Task<GrowthMutationResult> task)
        {
            while (!task.IsCompleted) yield return null;
            recruitmentBusy = false;
            if (!task.IsFaulted && !task.IsCanceled && task.Result.OutboxCode == null
                && (task.Result.Status == PracticeMutationStatus.Committed || task.Result.Status == PracticeMutationStatus.NotCommitted)) StartRecruitmentRefresh();
            else { recruitmentNotice = "이전 요청은 보존되어 있어요. 연결을 확인한 뒤 다시 결과를 확인해주세요."; RenderRecruitment(); }
        }

        IEnumerator AwaitPreviousRunRetry(Task<PracticeMutationResult> task)
        {
            while (!task.IsCompleted) yield return null;
            recruitmentBusy = false;
            if (!task.IsFaulted && !task.IsCanceled && task.Result.OutboxCode == null
                && (task.Result.Status == PracticeMutationStatus.Committed || task.Result.Status == PracticeMutationStatus.NotCommitted)) StartRecruitmentRefresh();
            else { recruitmentNotice = "이전 원정 요청은 보존되어 있어요. 연결을 확인한 뒤 다시 결과를 확인해주세요."; RenderRecruitment(); }
        }

        void BeginRecruitmentAction(string kind, int count = 0, string charId = "", string? bannerId = null, long? expectedRevision = null)
        {
            var session = recruitmentSession;
            if (recruitmentBusy || session?.Recruitment == null || kind != "retry" && !CanRecruitmentAct()) return;
            long revision = expectedRevision ?? session.Recruitment.Revision;
            if (revision != session.Recruitment.Revision) { recruitmentNotice = RecruitmentMessage("REFRESH_REQUIRED"); OpenRecruitment(); return; }
            recruitmentBusy = true; recruitmentNotice = "";
            OpenRecruitment();
            Task<RecruitmentMutationResult> task = kind switch
            {
                "launch" => session.ClaimRecruitmentLaunchAsync(revision),
                "selector" => session.ClaimRecruitmentSelectorAsync(revision, charId),
                "retry" => session.RetryRecruitmentPendingAsync(),
                _ => session.PullRecruitmentAsync(revision, bannerId ?? recruitmentBanner, count)
            };
            StartCoroutine(AwaitRecruitmentMutation(task));
        }

        IEnumerator AwaitRecruitmentMutation(Task<RecruitmentMutationResult> task)
        {
            while (!task.IsCompleted) yield return null;
            recruitmentBusy = false;
            if (task.IsFaulted || task.IsCanceled) recruitmentNotice = "응답을 확인하지 못했어요. 미확정 결과를 다시 확인해주세요.";
            else
            {
                var result = task.Result;
                if (result.Status == PracticeMutationStatus.Committed && result.Receipt != null) latestRecruitmentReceipt = result.Receipt;
                recruitmentNotice = result.Status == PracticeMutationStatus.Pending
                    ? "요청은 보존되어 있어요. 미확정 결과 확인으로 같은 요청의 결과를 확인해주세요."
                    : result.OutboxCode != null ? RecruitmentMessage(result.OutboxCode)
                    : result.RefreshCode != null ? "결과는 저장됐어요. 계정 정보를 다시 불러와주세요."
                    : RecruitmentMessage(result.Code);
                if (result.Status == PracticeMutationStatus.Committed && result.Receipt != null && result.OutboxCode == null)
                {
                    if (result.Receipt.Results.Count > 0)
                    {
                        if (recruitmentPanel.gameObject.activeSelf) { PresentRecruitment(result.Receipt.Results, true); yield break; }
                        if (result.RefreshCode == null) recruitmentNotice = "모집 결과가 도착했어요. 최근 결과에서 확인해주세요.";
                        if (recruitmentDockLabel != null) recruitmentDockLabel.text = "모집 · 결과";
                    }
                    else if (result.RefreshCode == null) recruitmentNotice = "선물을 받았어요. 서버에 안전하게 저장했어요.";
                }
            }
            RenderRecruitment();
        }

        static string RecruitmentMessage(string code) => code switch
        {
            "OK" => "",
            "CRYSTALS_LOW" => "티켓과 결정이 부족해요.",
            "LAUNCH_CLAIMED" or "LAUNCH_ALREADY_CLAIMED" => "이미 선물을 받았어요.",
            "UNAUTHORIZED" or "CREDENTIAL_UNAVAILABLE" => "접속 키를 확인해주세요.",
            "WRONG_ACCOUNT" => "접속 키와 계정이 일치하지 않아요.",
            "STALE_REVISION" or "REFRESH_REQUIRED" => "계정 정보가 바뀌었어요. 새로고침 후 다시 모집해주세요.",
            "PENDING_UNRESOLVED" => "이전 요청의 결과부터 확인해주세요.",
            "BEGINNER_LIMIT" => "첫 공방 모집을 모두 마쳤어요.",
            "OUTBOX_BINDING_MISMATCH" => "이전 주소 또는 계정의 요청이 보존되어 있어요. 연결 설정에서 이전 서버 주소와 계정으로 연결해 결과를 확인해주세요.",
            "OUTBOX_BUSY" => "다른 작업이 요청 기록을 사용 중이에요. 잠시 뒤 새로고침해주세요.",
            "OUTBOX_RECORD_CORRUPT" or "OUTBOX_RECORD_UNSUPPORTED" => "요청 기록을 안전하게 읽지 못했어요. 기록은 보존되어 있으며 지원 확인이 필요해요.",
            "OUTBOX_LOCK_FAILED" or "OUTBOX_READ_FAILED" or "OUTBOX_WRITE_FAILED" or "OUTBOX_UNSUPPORTED_PLATFORM" => "요청을 안전하게 보관할 수 없어 모집을 잠갔어요. 저장 공간과 접근 권한을 확인한 뒤 새로고침해주세요.",
            "OUTBOX_CLEAR_FAILED" or "OUTBOX_NOT_CURRENT" => "결과는 저장됐지만 요청 기록 확인이 남아 있어요. 미확정 결과를 다시 확인해주세요.",
            "OUTBOX_INVALID_CONFIGURATION" or "OUTBOX_INVALID_REQUEST" or "OUTBOX_REQUEST_ID_REUSED" or "OUTBOX_PENDING_OTHER" => "이전 요청 기록을 확인해야 해요. 연결 설정과 미확정 결과를 먼저 확인해주세요.",
            _ => "연결 또는 결과 확인이 필요해요. (" + code + ")"
        };

        void PresentRecruitment(IReadOnlyList<RecruitmentPull> results, bool animate)
        {
            if (results.Count == 0) return;
            player.Stop();
            revealedResults = results; revealSkipped = !animate || MemoryForgeUiMotion.Reduced;
            ClearPanel(recruitmentReveal);
            ShowPanel(recruitmentReveal);
            if (revealRoutine != null) StopCoroutine(revealRoutine);
            recruitmentRevealing = true;
            revealRoutine = StartCoroutine(RevealRecruitment());
        }

        IEnumerator RevealRecruitment()
        {
            var results = revealedResults!;
            Color glow = results.Any(x => x.Rarity == "SSR") ? Brass : results.Any(x => x.Rarity == "SR") ? Hex("bca2f3") : Memory;
            var halo = Box(recruitmentReveal, "RecruitmentHalo", 530, 154, 540, 540, Color.clear);
            Kit(halo, "OuterRing", "ui_portrait_ring_ready", Vector4.zero, 0, 0, 540, 540, 160).GetComponent<Image>().color = glow;
            Glyph(halo, "nav_recruit", 70, 70, 400, Color.white);
            Label(recruitmentReveal, "기 억 공 명", 400, 702, 800, 80, 40, glow, FontStyle.Bold, TextAnchor.MiddleCenter);
            Button(recruitmentReveal, "SkipRecruitmentReveal", "건너뛰기", 1240, 28, 304, 120, () => revealSkipped = true, Line, 24);
            for (float elapsed = 0; elapsed < 1.6f && !revealSkipped && !MemoryForgeUiMotion.Reduced; elapsed += Time.unscaledDeltaTime)
            {
                float t = elapsed / 1.6f;
                halo.localScale = Vector3.one * (0.82f + 0.18f * Mathf.SmoothStep(0, 1, t));
                halo.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-12, 0, t));
                yield return null;
            }
            if (!revealSkipped && !MemoryForgeUiMotion.Reduced)
            {
                ClearPanel(recruitmentReveal);
                var highlight = results.OrderByDescending(x => x.Rarity == "SSR" ? 2 : x.Rarity == "SR" ? 1 : 0).First();
                var art = RecruitmentArrival(highlight);
                Button(recruitmentReveal, "SkipRecruitmentArrival", "전체 결과", 1140, 746, 400, 120, () => revealSkipped = true, Brass, 26);
                var arrival = art.gameObject.AddComponent<CanvasGroup>();
                for (float elapsed = 0; elapsed < 1.2f && !revealSkipped && !MemoryForgeUiMotion.Reduced; elapsed += Time.unscaledDeltaTime)
                {
                    float t = Mathf.Clamp01(elapsed / 0.4f);
                    arrival.alpha = t; art.anchoredPosition = new Vector2(80 - 60 * (1 - Mathf.SmoothStep(0, 1, t)), -120);
                    yield return null;
                }
            }
            revealRoutine = null;
            recruitmentRevealing = false;
            RenderRecruitmentResults();
        }

        void FinishRecruitmentReveal()
        {
            if (recruitmentRevealing) { revealSkipped = true; return; }
            OpenRecruitment();
        }

        void RenderRecruitmentResults()
        {
            var results = revealedResults;
            if (results == null) return;
            ClearPanel(recruitmentReveal);
            if (results.Count == 1) RecruitmentArrival(results[0]); else RecruitmentBackdrop();
            Label(recruitmentReveal, "새로운 기억이 공방에 도착했어요", 80, 36, 1440, 80, 40, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            Label(recruitmentReveal, "모든 결과와 보유 정보는 서버에 저장되어 있어요.", 80, 126, 1440, 40, 22, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            float cellW = results.Count == 1 ? 480 : 280, startX = results.Count == 1 ? 560 : 72;
            for (int i = 0; i < results.Count; i++)
            {
                if (results.Count == 1) break;
                var result = results[i]; float x = startX + i % 5 * 296, y = 202 + i / 5 * 250;
                Color rarity = result.Rarity == "SSR" ? Brass : result.Rarity == "SR" ? Hex("bca2f3") : Memory;
                var tile = Box(recruitmentReveal, "RecruitmentResult:" + i, x, y, cellW, 230, Panel);
                Kit(tile, "ResultFrame", "ui_panel_frame", new Vector4(48, 48, 48, 48), 0, 0, cellW, 230, 128);
                Box(tile, "RarityEdge", 0, 0, cellW, 3, rarity);
                var picture = Box(tile, "ResultArt", 12, 12, 112, 156, Color.white);
                var portrait = result.Kind == "char" ? Portrait(result.Id) : null;
                if (portrait != null) { picture.GetComponent<Image>().sprite = portrait; picture.GetComponent<Image>().preserveAspect = true; }
                else { picture.GetComponent<Image>().color = Color.clear; Glyph(picture, result.Kind == "weapon" ? "nav_equipment" : "nav_memory", 0, 10, 112, Color.white); }
                Label(tile, result.Rarity, 140, 20, cellW - 152, 48, 30, rarity, FontStyle.Bold);
                Label(tile, result.IsNew ? "NEW" : result.Kind == "char" ? "공명 " + result.Resonance : result.Kind == "weapon" ? "정련 " + result.Refine : "잔광 +" + result.Afterglow,
                    140, 82, cellW - 152, 68, 22, result.IsNew ? Memory : Muted);
                Label(tile, RecruitmentResultName(result), 16, 172, cellW - 32, 48, 26, Ink, FontStyle.Bold);
            }
            Button(recruitmentReveal, "RecruitmentResultDone", "확인", 840, 756, 440, 120, OpenRecruitment, Brass, 30);
            Button(recruitmentReveal, "RecruitmentResultRoster", "보유 동료", 320, 756, 440, 120, () => { catalogueFilter = "owned"; OpenRoster(); }, Line, 28);
            FocusRecruitment(recruitmentReveal);
        }

        void RecruitmentBackdrop()
        {
            var wall = Box(recruitmentReveal, "ResultWorkshop", 0, 0, 1600, 900, new Color(0.25f, 0.3f, 0.4f));
            wall.GetComponent<Image>().sprite = SpriteAt("Art/Backgrounds/bg_workshop");
            Shade(recruitmentReveal, "ResultShade", 0, 0, 1600, 900, new Color(0.025f, 0.035f, 0.07f, 0.94f), false);
        }

        RectTransform RecruitmentArrival(RecruitmentPull result)
        {
            RecruitmentBackdrop();
            Color rarity = result.Rarity == "SSR" ? Brass : result.Rarity == "SR" ? Hex("bca2f3") : Memory;
            Kit(recruitmentReveal, "ArrivalRing", "ui_portrait_ring_ready", Vector4.zero, 112, 200, 600, 600, 160).GetComponent<Image>().color = new Color(rarity.r, rarity.g, rarity.b, 0.28f);
            var art = Box(recruitmentReveal, "RecruitmentArrival", 80, 120, 760, 640, Color.white);
            var figure = result.Kind == "char" ? Portrait(result.Id) : null;
            if (figure != null) { art.GetComponent<Image>().sprite = figure; art.GetComponent<Image>().preserveAspect = true; }
            else
            {
                art.GetComponent<Image>().color = Color.clear; Glyph(art, result.Kind == "weapon" ? "nav_equipment" : "nav_memory", 120, 60, 520, Color.white);
                if (result.Kind == "char") Label(art, "일러스트 준비 중", 120, 560, 520, 60, 24, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            }
            Label(recruitmentReveal, result.Rarity, 872, 254, 640, 100, 68, rarity, FontStyle.Bold);
            Ornament(recruitmentReveal, 876, 372, 560);
            Label(recruitmentReveal, RecruitmentResultName(result), 872, 408, 640, 112, 52, Ink, FontStyle.Bold);
            Label(recruitmentReveal, result.IsNew ? "NEW · 새로운 인연이 시작됩니다." : result.Kind == "char" ? "공명 " + result.Resonance : result.Kind == "weapon" ? "정련 " + result.Refine : "잔광 +" + result.Afterglow,
                876, 558, 620, 100, 28, result.IsNew ? Memory : Muted);
            return art;
        }

        string RecruitmentResultName(RecruitmentPull result)
        {
            if (result.Kind == "char") return content.Battle.Characters.FirstOrDefault(x => x.Id == result.Id)?.Name ?? result.Id;
            return result.Kind == "weapon" ? content.Battle.Weapons.FirstOrDefault(x => x.Id == result.Id)?.Name ?? result.Id : result.Id;
        }
    }
}
