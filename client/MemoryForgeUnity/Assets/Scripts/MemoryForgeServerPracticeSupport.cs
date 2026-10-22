#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MemoryForge.Client
{
    public sealed partial class MemoryForgeServerPracticeClient
    {
        bool supportOpen;
        int supportEpoch;
        string? supportCaller, supportTarget;
        readonly List<string> supportPicks = new List<string>();
        SupportQueryResult? supportAnswer;
        PracticeUiContext? supportContext;
        string? supportFrozenId;
        readonly Dictionary<string, string> supportUltNames = new Dictionary<string, string>();
        public bool SupportSheetOpen => supportOpen;
        public PracticeSupportQueryView? SupportQueryView => SupportCurrent() ? supportAnswer!.View : null;
        public IReadOnlyList<string> SupportPicks => supportPicks.ToArray();
        bool SupportReadable => !busy && session != null && !session.State.NeedsRefresh && session.State.Snapshot?.Run?.Phase == "BATTLE"
            && session.State.Snapshot.Run.BattlePhase == "PLAYER" && rows == null && picker == null && prepared == null && deckPick == null
            && !DetailsCurrent() && !accountAbandonOpen && (!supportOpen || supportContext?.Equals(session.State.Snapshot.Context) == true);
        bool SupportCurrent() => supportOpen && session != null && supportContext?.Equals(session.State.Snapshot?.Context) == true && supportAnswer?.View != null && session.IsCurrent(supportAnswer)
            && supportAnswer.View.Source.Caller == supportCaller && supportAnswer.View.Source.Target == supportTarget
            && supportAnswer.View.Source.Picks.SequenceEqual(supportPicks) && session.State.Snapshot?.Run?.Support?.CharId == supportAnswer.View.Source.CharId;
        public bool CanConfirmSupport => SupportReadable && session!.State.CanMutate && SupportCurrent() && supportAnswer!.View!.CanAct
            && session.State.Snapshot!.Run!.Support?.Used == false && Actors(Battle()!).Any(a => Str(a["id"]) == supportCaller && Int(a["gauge"]) >= 100);

        void DropSupport()
        {
            supportEpoch++; supportOpen = false; supportCaller = null; supportTarget = null; supportPicks.Clear(); supportAnswer = null; supportContext = null; supportFrozenId = null;
        }
        void RevalidateSupportPreparation()
        {
            if (supportOpen && (session?.State.Snapshot?.Context.Equals(supportContext) != true || session.State.Snapshot.Run?.Support?.CharId != supportFrozenId))
            { DropSupport(); Render(); }
        }
        void RunSupportEntry(float x, float y, float width, float height)
        {
            var summary = Snapshot?.Run?.Support;
            string caption = summary == null ? "지원 미선택" : "지원 · " + CharacterName(summary.CharId) + (summary.Used ? "\n이번 전투 사용 완료" : "\n호출자 게이지 " + summary.GaugeCost);
            Button(frame!, "OpenRunSupport", caption, x, y, width, height, () => Launch(OpenSupportAsync), summary?.Used == false ? Memory : Line, 20)
                .interactable = summary != null && SupportReadable;
        }
        public Task<bool> OpenSupportAsync()
        {
            if (!SupportReadable) return Refuse("연출과 선택을 마친 뒤 최신 전투에서 지원을 확인해주세요.");
            if (session!.State.Snapshot!.Run!.Support == null) return Refuse("이번 탐사에는 지원 동료가 선택되지 않았어요.");
            var shown = session.State.Snapshot!; var summary = shown.Run!.Support!;
            DropSupport(); supportOpen = true; supportContext = shown.Context; supportFrozenId = summary.CharId;
            var actors = Actors(Battle()!);
            supportCaller = Str((actors.FirstOrDefault(a => GaugeFull(a, Battle()!)) ?? actors.FirstOrDefault())?["id"]);
            supportTarget = target != null && Enemies().Any(e => Str(e["uid"]) == target && Bool(e["alive"])) ? target : null;
            selectedCard = null; selectedMode = null; generation++; queryWanted = false; queryResult = null;
            return ReadSupportAsync();
        }
        public void CancelSupport()
        {
            DropSupport(); Render();
        }
        public Task<bool> SelectSupportCallerAsync(string caller)
        {
            if (!supportOpen || !SupportReadable || !Actors(Battle()!).Any(a => Str(a["id"]) == caller)) return Refuse("현재 전투의 호출자를 골라주세요.");
            supportCaller = caller; supportPicks.Clear(); supportAnswer = null; supportEpoch++;
            return ReadSupportAsync();
        }
        public Task<bool> SelectSupportTargetAsync(string uid)
        {
            if (!supportOpen || !SupportReadable || !Enemies().Any(e => Str(e["uid"]) == uid && Bool(e["alive"]))) return Refuse("살아 있는 대상 적을 골라주세요.");
            supportTarget = uid; supportPicks.Clear(); supportAnswer = null; supportEpoch++;
            return ReadSupportAsync();
        }
        public Task<bool> PickSupportCardAsync(string uid)
        {
            if (!supportOpen || !SupportReadable || !SupportCurrent() || !SupportCandidates().Any(c => Str(c["uid"]) == uid)) return Refuse("서버가 제시한 선택지를 골라주세요.");
            supportPicks.Add(uid); supportAnswer = null; supportEpoch++; return ReadSupportAsync();
        }
        Task<bool> ReadSupportAsync()
        {
            if (!supportOpen || supportCaller == null || !SupportReadable) return Refuse("지원 호출자를 먼저 확인해주세요.");
            var context = session!.State.Snapshot!.Context; string caller = supportCaller; string? asked = supportTarget;
            string[] picks = supportPicks.ToArray(); int epoch = supportEpoch;
            return Operate(async captured =>
            {
                var answer = await captured.SupportQueryAsync(context, caller, asked, picks);
                if (!ReferenceEquals(captured, session) || epoch != supportEpoch || !supportOpen || !captured.IsCurrent(answer))
                {
                    if (ReferenceEquals(captured, session) && epoch == supportEpoch && supportOpen && answer.View == null)
                        Say("지원 미리보기를 확인하지 못했어요. " + Explain(answer.Code), true);
                    return false;
                }
                supportAnswer = answer; supportUltNames[answer.View!.Source.CharId] = answer.View.Source.UltName;
                Say(answer.View.CanAct ? "호출자 게이지 100을 쓰는 지원이에요. 실행 확인 전에는 소모하지 않아요."
                    : SupportReason(answer.View.Source.Code ?? answer.View.ForecastError ?? answer.View.ChoicesBlocked ?? answer.View.ChoicesError), !answer.View.CanAct);
                return true;
            });
        }
        public async Task<bool> ConfirmSupportAsync()
        {
            if (!CanConfirmSupport) return await Refuse("최신 지원 미리보기와 미확정 요청을 먼저 확인해주세요.");
            var captured = session!; var before = captured.State.Snapshot!; var source = supportAnswer!.View!.Source;
            var args = new JObject { ["caller"] = source.Caller };
            if (source.Target != null) args.Add("target", source.Target);
            if (source.Picks.Count != 0) args.Add("picks", new JArray(source.Picks));
            // The local preparation ends before the one shared mutation starts; a second tap cannot send it again.
            DropSupport();
            return await Mutate((current, context) => current.CommandAsync(before.Context, "support", args), true, false,
                CharacterName(source.CharId) + "의 지원을 사용했어요.");
        }
        List<JObject> SupportCandidates() => SupportCurrent()
            ? ((supportAnswer!.View!.ToJObject()["choices"]?["candidates"] as JArray) ?? new JArray()).OfType<JObject>().ToList() : new List<JObject>();

        void RunSupportView()
        {
            var shield = Box(frame!, "RunSupportSheet", 0, 0, 1600, 900, new Color(0.02f, 0.03f, 0.06f, 0.98f)); shield.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            var summary = Snapshot?.Run?.Support; var view = SupportQueryView;
            Label(shield, "지원 · " + (view?.Source.Name ?? CharacterName(summary?.CharId)), 56, 24, 1000, 64, 38, Brass);
            Label(shield, "전투당 1회 · 호출자의 개인 게이지 100 사용 · 출전 인원은 그대로예요.", 56, 104, 1220, 48, 23, Memory);
            float height = Math.Max(144, MemoryForgeExpeditionClient.TouchOverride ?? 0), pitch = height + 16;
            var actors = Battle() is JObject battle ? Actors(battle) : new List<JObject>();
            float width = 1488f / Math.Max(1, actors.Count);
            for (int i = 0; i < actors.Count; i++)
            {
                var actor = actors[i]; string caller = Str(actor["id"])!;
                Button(shield, "SupportCaller:" + caller, ActorName(actor) + "\n게이지 " + Int(actor["gauge"]) + "/100", 56 + i * width, 184, width - 16, height,
                    () => Launch(() => SelectSupportCallerAsync(caller)), caller == supportCaller ? Brass : Line, 25).interactable = SupportReadable;
            }
            var body = Scroll(shield, "RunSupportBody", 56, 352, 1488, 344, 1488, 4000, Panel);
            float y = 16;
            y = Flow(body, view == null ? "서버의 지원 미리보기를 확인해주세요." : view.Source.UltName + "\n" + view.Source.Text, y, 26, Ink);
            if (view != null)
            {
                y = Flow(body, view.Source.Used ? "이번 전투에서 이미 사용했어요." : view.Source.Ready ? "호출자 게이지 100 사용" : SupportReason(view.Source.Code), y, 24, view.CanAct ? Memory : Danger);
                y = Flow(body, view.ForecastHidden ? "무작위 또는 숨겨진 정보가 있어 결과 수치 예상은 표시하지 않아요."
                    : view.ForecastError != null ? SupportReason(view.ForecastError) : string.Join(" · ", view.EnemyHpLosses.Select(e => (Str(Enemies().FirstOrDefault(n => Str(n["uid"]) == e.Uid)?["name"]) ?? "적") + " 체력 -" + e.HpLoss)), y, 23, Memory);
            }
            if (view?.Source.NeedsTarget == "one" || view?.Source.NeedsTarget == "fixed")
            {
                y = Flow(body, view.Source.NeedsTarget == "fixed" ? "표식 대상 조건은 서버가 확인해요. 원하는 대상을 골라 확인할 수 있어요." : "대상 적을 선택해주세요.", y, 24, Brass);
                foreach (var enemy in Enemies().Where(e => Bool(e["alive"])))
                {
                    string uid = Str(enemy["uid"])!;
                    Button(body, "SupportTarget:" + uid, (Str(enemy["name"]) ?? "적") + (uid == supportTarget ? " · 선택" : ""), 16, y, 1440, height,
                        () => Launch(() => SelectSupportTargetAsync(uid)), uid == supportTarget ? Brass : Line, 26).interactable = SupportReadable; y += pitch;
                }
            }
            if (view != null && view.ChoicesIndex < view.ChoicesCount)
            {
                var choices = view.ToJObject()["choices"]; var request = (choices?["requests"] as JArray)?[view.ChoicesIndex];
                y = Flow(body, Str(request?["label"]) ?? "필요한 카드를 골라주세요.", y, 24, Brass);
                foreach (var candidate in SupportCandidates())
                {
                    string uid = Str(candidate["uid"])!;
                    Button(body, "SupportPick:" + uid, CardName(Str(candidate["id"])), 16, y, 1440, height,
                        () => Launch(() => PickSupportCardAsync(uid)), Line, 26).interactable = SupportReadable; y += pitch;
                }
                if (SupportCandidates().Count == 0) y = Flow(body, SupportReason(view.ChoicesBlocked ?? view.ChoicesError ?? "PICK_INVALID"), y, 24, Danger);
            }
            if (session?.State.Pending != null) y = Flow(body, "다른 미확정 요청이 남아 있어 실행 확인은 잠겨 있어요. 창을 닫고 같은 요청 확인을 먼저 진행해주세요.", y, 24, Danger);
            y = Flow(body, notice, y, 22, noticeError ? Danger : Memory); body.sizeDelta = new Vector2(1488, Math.Max(344, y + 32));
            Button(shield, "CancelRunSupport", "취소 · 돌아가기", 56, 728, 704, height, CancelSupport, Line, 28).interactable = !busy;
            Button(shield, "ConfirmRunSupport", "지원 실행 · 게이지 100", 840, 728, 704, height, () => Launch(ConfirmSupportAsync), Brass, 28).interactable = CanConfirmSupport;
        }
        static string SupportReason(string? code) => code switch
        {
            "SUPPORT_ALREADY_USED" => "이번 전투의 지원을 이미 사용했어요.", "SUPPORT_NOT_SELECTED" => "이번 탐사에는 지원 동료가 없어요.",
            "ULT_NOT_READY" => "호출자의 개인 게이지가 100이 되어야 해요.", "BAD_TARGET" => "대상 적과 표식 조건을 확인해주세요.",
            null => "지원 조건을 확인하고 있어요.", _ => "지원 조건을 충족하지 못했어요. (" + code + ")"
        };
        void PresentRunSupportCutin(PracticeSnapshot before, JArray events, PracticeSnapshot after)
        {
            var row = events.OfType<JObject>().FirstOrDefault(e => Str(e["e"]) == "support");
            string? partner = Str(row?["actor"]), caller = Str(row?["caller"]);
            if (partner == null || caller == null || before.Run?.Support?.CharId != partner || (Battle(before) is not JObject beforeBattle || !Actors(beforeBattle).Any(a => Str(a["id"]) == caller))
                || before.Run.RunId != after.Run?.RunId || after.Run.ServerVersion != before.Run.ServerVersion + 1 || session?.State.Pending != null
                || session?.State.NeedsRefresh != false || session.State.Snapshot?.Run?.RunId != after.Run.RunId || session.State.Snapshot.Run.ServerVersion != after.Run.ServerVersion || !isActiveAndEnabled) return;
            string title = supportUltNames.TryGetValue(partner, out var known) ? known : "지원 필살기";
            StartCoroutine(MemoryForgeCombatCutin.Play(transform, font!, AccountSprite("Characters/char_" + partner + "_cutin"), CharacterName(partner), title, "AccountSupportCutin"));
        }
    }
}
