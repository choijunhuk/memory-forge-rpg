#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MemoryForge.Client
{
    public sealed partial class MemoryForgeGrowthClient
    {
        bool supportOpen, supportQueryBusy;
        string? supportCaller, supportTarget;
        readonly List<string> supportPicks = new List<string>();
        SupportQueryResult? supportAnswer;
        int supportEpoch;

        void DropGrowthSupport()
        {
            supportOpen = supportQueryBusy = false; supportCaller = supportTarget = null;
            supportPicks.Clear(); supportAnswer = null; supportEpoch++;
        }

        void GrowthSupportEntry(Transform parent)
        {
            var support = session?.Encounter?.Support;
            if (support == null) return;
            Button(parent, "GrowthSupportEntry", support.Used ? "지원 사용 완료" : "지원 · " + CharacterName(support.CharId), 696, 8, 464, 80,
                () => { if (!CanAct) return; DropSelection(); supportOpen = true; Render(); }, Brass).interactable = CanAct && !support.Used;
        }

        void CloseGrowthSupport() { DropGrowthSupport(); Render(); }

        void GrowthSupportSheet(JObject state)
        {
            var shield = Box(frame!, "GrowthSupportShield", 0, 0, 1600, 900, new Color(0.015f, 0.025f, 0.05f, 0.9f)); shield.GetComponent<Image>().raycastTarget = true;
            var paper = Box(shield, "GrowthSupportSheet", 208, 72, 1184, 784, Panel);
            var support = session?.Encounter?.Support;
            Label(paper, "파트너 지원 · " + CharacterName(support?.CharId ?? ""), 32, 16, 1120, 56, 34, Brass);
            Label(paper, "호출 동료의 게이지 100 소비 · 이번 전투에서 1회 · 자신의 필살기 대신 사용", 32, 80, 1120, 48, 24, Ink);
            var actors = state["actors"] as JArray ?? new JArray();
            var callers = Scroll(paper, "GrowthSupportCallers", 32, 144, 1120, Math.Max(144, TouchUnits()), Math.Max(1120, actors.Count * 368));
            int n = 0;
            foreach (var actor in actors)
            {
                string id = Text(actor["id"]); int gauge = Number(actor["gauge"]);
                Button(callers, "GrowthSupportCaller:" + id, CharacterName(id) + "\n게이지 " + gauge + "/100", n++ * 368, 0, 352, 144,
                    () => ChooseGrowthSupportCaller(id), supportCaller == id ? Brass : Panel).interactable = SupportInputReady && gauge >= 100 && support?.Used == false;
            }
            var view = supportAnswer?.View;
            string message = supportQueryBusy ? "지원 효과를 확인하고 있어요…" : supportCaller == null ? "게이지가 가득 찬 호출 동료를 선택해주세요."
                : view == null ? "지원을 확인하지 못했어요. 취소 후 새로고침해주세요."
                : view.Source.UltName + "\n" + view.Source.Text + (view.ForecastHidden ? "\n무작위·드로우 결과는 사용한 뒤 확인할 수 있어요." : "");
            var messageBody = Scroll(paper, "GrowthSupportMessage", 32, 304, 1120, 128, 1120);
            var messageText = Label(messageBody, message, 0, 0, 1120, 128, 22, Ink);
            messageBody.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(128, messageText.rectTransform.rect.height));
            var messageScroll = messageBody.parent.GetComponent<UnityEngine.UI.ScrollRect>(); messageScroll.horizontal = false; messageScroll.vertical = true;
            var choices = view?.ToJObject()["choices"] as JObject;
            var candidates = choices?["candidates"] as JArray;
            bool choosing = candidates != null && Number(choices?["index"]) < (choices?["requests"] as JArray)?.Count;
            if (choosing)
            {
                var rows = Scroll(paper, "GrowthSupportChoices", 32, 448, 1120, 144, Math.Max(1120, candidates!.Count * 280));
                for (int i = 0; i < candidates.Count; i++)
                {
                    var item = candidates[i]; string uid = Text(item["uid"]);
                    Button(rows, "GrowthSupportPick:" + uid, CardName(Text(item["id"])), i * 280, 0, 264, 144,
                        () => { supportPicks.Add(uid); _ = QueryGrowthSupport(); }).interactable = SupportInputReady;
                }
            }
            else if (view?.Source.NeedsTarget == "one")
            {
                var enemies = (state["enemies"] as JArray ?? new JArray()).Where(e => e["alive"]?.Value<bool>() == true).ToArray();
                var rows = Scroll(paper, "GrowthSupportTargets", 32, 448, 1120, 144, Math.Max(1120, enemies.Length * 280));
                for (int i = 0; i < enemies.Length; i++)
                {
                    var enemy = enemies[i]; string uid = Text(enemy["uid"]);
                    Button(rows, "GrowthSupportTarget:" + uid, Text(enemy["name"]), i * 280, 0, 264, 144,
                        () => { supportTarget = uid; supportPicks.Clear(); _ = QueryGrowthSupport(); }, uid == supportTarget ? Brass : Panel).interactable = SupportInputReady;
                }
            }
            else Label(paper, supportCaller == null ? "아직 지원 요청을 보내지 않았어요." : "대상과 효과는 파트너의 필살기 규칙을 따릅니다.", 32, 464, 1120, 72, 24, Muted);
            if (view != null)
            {
                var forecast = view.ToJObject()["forecast"];
                Label(paper, view.ForecastHidden ? "결과는 사용 후 확인" : view.ForecastError != null ? "대상·선택을 확인해주세요."
                    : "예상 체력 손실 " + Number(forecast?["enemyHpLoss"]) + "\n방어 변화 " + Number(forecast?["blockDelta"]) + "\n체력 변화 " + Number(forecast?["hpDelta"]), 408, 616, 328, 144, 22, Teal);
            }
            Button(paper, "CancelGrowthSupport", "취소", 32, 616, 352, 144, CloseGrowthSupport).interactable = !busy;
            Button(paper, "ConfirmGrowthSupport", "확인 · 지원 사용", 768, 616, 384, 144, ConfirmGrowthSupport, Brass).interactable = CanConfirmGrowthSupport();
        }

        void ChooseGrowthSupportCaller(string id)
        {
            if (!SupportInputReady || !supportOpen) return;
            supportCaller = id; supportPicks.Clear();
            var enemies = (session?.Encounter?.StateJObject()["enemies"] as JArray ?? new JArray()).Where(e => e["alive"]?.Value<bool>() == true).ToArray();
            supportTarget = enemies.Length == 1 ? Text(enemies[0]["uid"]) : null;
            _ = QueryGrowthSupport();
        }

        async System.Threading.Tasks.Task QueryGrowthSupport()
        {
            var captured = session; var shown = captured?.Encounter; string? caller = supportCaller;
            if (captured == null || shown == null || caller == null || !supportOpen || busy || supportQueryBusy) return;
            int epoch = ++supportEpoch; supportQueryBusy = true; supportAnswer = null; Render();
            try
            {
                var answer = await captured.EncounterSupportQueryAsync(shown.Context, caller, supportTarget, supportPicks.ToArray());
                if (Current(captured) && supportOpen && epoch == supportEpoch && captured.IsCurrent(answer)) supportAnswer = answer;
                else if (Current(captured) && epoch == supportEpoch && captured.Encounter?.Context.Equals(shown.Context) != true)
                { DropGrowthSupport(); notice = "전투 정보가 바뀌었어요. 새로고침한 뒤 다시 선택해주세요."; Render(); }
            }
            catch (Exception) { if (Current(captured) && epoch == supportEpoch) notice = "지원을 확인하지 못했어요. 취소 후 새로고침해주세요."; }
            finally { if (Current(captured) && epoch == supportEpoch) { supportQueryBusy = false; Render(); } }
        }

        bool CanConfirmGrowthSupport()
        {
            var answer = supportAnswer;
            return supportOpen && SupportInputReady && answer?.View?.CanAct == true && session?.IsCurrent(answer) == true
                && answer.View.Source.Caller == supportCaller && answer.View.Source.Target == supportTarget
                && answer.View.Source.Picks.SequenceEqual(supportPicks) && session.Encounter?.Support?.Used == false;
        }

        void ConfirmGrowthSupport()
        {
            if (!CanConfirmGrowthSupport()) return;
            var source = supportAnswer!.View!.Source;
            var args = new JObject { ["caller"] = source.Caller };
            if (source.Target != null) args["target"] = source.Target;
            if (source.Picks.Count > 0) args["picks"] = new JArray(source.Picks);
            string partner = source.CharId; DropGrowthSupport(); _ = Send("support", args, partner);
        }
    }
}
