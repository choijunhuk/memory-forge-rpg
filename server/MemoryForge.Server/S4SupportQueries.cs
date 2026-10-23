using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MemoryForge.Core;
namespace MemoryForge.Server
{
    sealed record SupportQueryRequest(int ExpectedVersion,string Caller,string? Target,List<string> Picks);
    static partial class Contracts
    {
        public static SupportQueryRequest ParseSupportQuery(byte[] bytes)
        {
            using var d=Document(bytes);var m=Members(d.RootElement,"expectedVersion","caller","target","picks");int version=Version(m,"expectedVersion");
            var raw=m.Where(p=>p.Key!="expectedVersion").ToDictionary(p=>p.Key,p=>Value(p.Value,0),StringComparer.Ordinal);var a=CheckArgs("support",raw);
            return new SupportQueryRequest(version,(string)a["caller"]!,a.GetValueOrDefault("target") as string,a.GetValueOrDefault("picks") is IList picks?picks.Cast<string>().ToList():new List<string>());
        }
    }
    static partial class GrowthCommands
    {
        public static Reply SupportQuery(PracticeContent c,PracticeAccount a,AccountEnvelope? e,string id,SupportQueryRequest q)
        {
            if(e==null)throw new ApiError(404,"ENCOUNTER_NOT_FOUND");var r=Record(e,id);if(r.Journal.Count!=q.ExpectedVersion)throw new ApiError(409,"STALE_VERSION");
            var battle=Trace(c,a,e).Battles[id];var b=battle.State;if(b.Phase!="PLAYER")throw new ApiError(409,"ENCOUNTER_FINISHED");string digest=GraphJson.Digest(b.ToJsonGraph());
            var info=battle.SupportInfo(q.Caller,q.Target,q.Picks);var choices=battle.SupportChoices(q.Caller,q.Picks);var forecast=battle.SupportForecast(q.Caller,q.Target,q.Picks);
            var view=PublicView.SupportQuery("encounterId",id,q.ExpectedVersion,b.Phase,GraphJson.Object("turn",b.Turn,"battleVersion",b.V,"battlePhase",b.Phase),q,info,choices,forecast,b,battle.ReservationSlots(),c.Rules);
            Require(digest==GraphJson.Digest(battle.State.ToJsonGraph()));return Reply.Json(200,view);
        }
    }
    static partial class PublicView
    {
        static Dictionary<string,object?> WithSupport(Dictionary<string,object?> view,BattleState b)
        {if(b.Support!=null)view.Add("support",Obj("charId",b.Support.Source.Id,"used",b.Support.Used,"gaugeCost",100));return view;}
        public static Dictionary<string,object?> SupportQuery(string key,string id,int version,string phase,Dictionary<string,object?> context,SupportQueryRequest q,BattleSupportInfo info,
            BattleChoiceView choices,BattlePlayForecast forecast,BattleState b,BattleSlotView slots,BattleRules rules)
        {
            string? zone=choices.Index<choices.Requests.Count?choices.Requests[choices.Index].From:null;
            object? Outcome(object value)=>forecast.Hidden?null:value;
            return Obj(key,id,"version",version,"phase",phase,"context",context,
                "source",Obj("kind","support","caller",q.Caller,"charId",info.CharId,"name",info.Name,"ultName",info.UltName,"text",info.Text,"attr",info.Attr,"gaugeCost",100,
                    "used",info.Used,"ready",info.Ready,"code",info.Code,"needsTarget",info.NeedsTarget,"target",q.Target,"picks",q.Picks.Cast<object?>().ToList()),
                "slots",Slots(slots,b,rules),"forecast",Obj("error",forecast.Error,"mode",forecast.Mode,"cost",100,"due",forecast.Due,"hidden",forecast.Hidden,
                    "enemyHpLoss",Outcome(forecast.EnemyHpLoss),"enemyDamage",Outcome(forecast.EnemyDamage),"newlyBroken",Outcome(forecast.NewlyBroken),"blockDelta",Outcome(forecast.BlockDelta),"hpDelta",Outcome(forecast.HpDelta),"defeat",Outcome(forecast.Defeat),
                    "enemyHpLosses",forecast.Hidden?new List<object?>():forecast.EnemyHpLosses.Select(x=>(object?)Obj("uid",x.Uid,"hpLoss",x.HpLoss)).ToList()),
                "choices",Obj("error",choices.Error,"requests",choices.Requests.Select(r=>(object?)Obj("label",r.Label,"from",r.From,"count",r.Count,"growthOnly",r.GrowthOnly,"op",r.Op,"part",r.Part,"leading",r.Leading)).ToList(),
                    "accepted",choices.Accepted,"index",choices.Index,"left",choices.Left,"blocked",choices.Blocked,"candidates",choices.Candidates.Select(uid=>(object?)Candidate(b.Cards[uid],zone)).ToList()));
        }
    }
}
