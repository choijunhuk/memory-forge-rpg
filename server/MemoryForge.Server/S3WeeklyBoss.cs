using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using MemoryForge.Core;

namespace MemoryForge.Server
{
    static partial class Contracts
    {
        public static GrowthRequest ParseWeeklyBoss(byte[] bytes)
        {
            using var d=Document(bytes);var m=Members(d.RootElement,"requestId","expectedRevision","bossId","difficulty","practice","party");
            string id=RequestId(m),boss=Reference(m,"bossId");long revision=Revision(m,"expectedRevision");int difficulty=Version(m,"difficulty");
            if(!m.TryGetValue("practice",out var practice) || (practice.ValueKind!=JsonValueKind.True && practice.ValueKind!=JsonValueKind.False)
                || !m.TryGetValue("party",out var party) || party.ValueKind!=JsonValueKind.Array || party.GetArrayLength()>32) throw new ApiError(400,"INVALID_REQUEST");
            return new GrowthRequest(id,revision,"weeklyBossStart",GraphJson.Object("bossId",boss,"difficulty",difficulty,"practice",practice.GetBoolean(),"party",party.EnumerateArray().Select(p=>(object?)StringValue(p,"INVALID_REQUEST")).ToList()));
        }
    }
    static partial class GrowthCommands
    {
        static bool TerminalFact(string kind)=>kind is "encounterVictory" or "encounterDefeat" or "weeklyBossVictory" or "weeklyBossDefeat";
        static bool CommandFact(string kind)=>kind=="encounterCommand" || TerminalFact(kind);
        static string EndKind(EncounterRecord r,string phase)=>phase=="VICTORY" ? (r.Kind=="weeklyBoss"?"weeklyBossVictory":"encounterVictory") : phase=="DEFEAT" ? (r.Kind=="weeklyBoss"?"weeklyBossDefeat":"encounterDefeat") : "encounterCommand";
        static void WeeklyMetadata(EncounterRecord r,Dictionary<string,object?> view)
        {
            if(r.Kind!="weeklyBoss") return;
            view.Add("kind","weeklyBoss");view.Add("bossId",r.BossId);view.Add("difficulty",r.Difficulty);view.Add("practice",r.Practice);
        }
        static long WeeklyAccountXp(AccountEnvelope? e,long before)
        {
            long xp=0;var counts=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var f in e?.Growth?.Ledger.Where(f=>f.Kind=="weeklyBossVictory" && f.Revision<before) ?? Enumerable.Empty<GrowthFact>())
            {
                var r=e!.Growth!.Encounters.Single(r=>r.Id==f.EncounterId);string week=EquipmentPolicy.WeekAt(f.AtMs).Key;
                if(!r.Practice && counts.GetValueOrDefault(week)<EquipmentPolicy.WeeklyLimit)
                { counts[week]=counts.GetValueOrDefault(week)+1;xp=checked(xp+EquipmentPolicy.WeeklyXp); }
            }
            return xp;
        }
        static Dictionary<string,object?> WeeklyStage(ContentModesData modes,string boss,int difficulty)
        {
            if(!modes.WeeklyBoss.Bosses.Contains(boss)) throw new ApiError(422,"UNKNOWN_CONTENT");
            var row=modes.WeeklyBoss.Difficulty.SingleOrDefault(r=>r.Lv==difficulty) ?? throw new ApiError(422,"UNKNOWN_TIER");
            return GraphJson.Object("difficulty",difficulty,"stage",row.Stage,"hpBps",row.HpBps,"dmgBps",row.DmgBps,"lineup",new List<object?>{new List<object?>{boss}});
        }
        static Dictionary<string,object?> WeeklySettle(PracticeContent c,AccountEnvelope e,GrowthState s,EncounterRecord r,Battle battle,long at,long before)
        {
            Require(r.PolicyVersion is EquipmentPolicy.Version or SupportPolicy.Version && r.PolicyHash==GrowthPolicy.HashOf(r.PolicyVersion));
            var modes=EquipmentPolicy.Modes(c);var week=EquipmentPolicy.Week(modes,at);var account=ContentAccount(e,s,before);
            account.Weekly=new ContentWeeklyLedger{Key=week.Key,Claimed=s.S3.Claims.GetValueOrDefault(week.Key)};
            var settled=ContentModes.WeeklyBoss.Settle(modes,account,battle.State,at);
            var resources=GrowthPolicy.Resources.ToDictionary(k=>k,_=>(object?)0L,StringComparer.Ordinal);resources.Add("boss_token",0L);long xp=0;
            if(settled.Reason==null)
            {
                var grant=new Dictionary<string,int>(settled.Grant!,StringComparer.Ordinal);
                int tokens=grant["bossToken"];grant.Remove("bossToken");
                if(tokens<0) throw new InvalidDataException("weekly token grant");
                try { s.S3.BossToken=checked(s.S3.BossToken+tokens); } catch(OverflowException){throw new ApiError(422,"RESOURCE_OVERFLOW");}
                s.Grant(grant);s.S3.Claims[week.Key]=settled.Account.Weekly.Claimed;
                foreach(var p in grant) if(p.Key=="accountXp") xp=p.Value;else resources[p.Key]=(long)p.Value;
                resources["boss_token"]=(long)tokens;
            }
            s.Active=null;
            int claimed=s.S3.Claims.GetValueOrDefault(week.Key);
            return GraphJson.Object("outcome",battle.State.Phase,"stars",0,"cost",GraphJson.Object("focus",0),"grant",GraphJson.Object("resources",resources,"accountXp",xp),
                "kind","weeklyBoss","rewarded",settled.Reason==null,"reason",settled.Reason,"weekKey",week.Key,"claimed",claimed,"remaining",modes.WeeklyBoss.ClaimsPerWeek-claimed);
        }
        static Dictionary<string,object?> ApplyWeeklyStart(PracticeContent c,PracticeAccount a,AccountEnvelope e,GrowthTrace trace,GrowthFact f)
        {
            Require(f.PolicyVersion is EquipmentPolicy.Version or SupportPolicy.Version && f.JournalIndex==0);
            var q=StoredRequest(f);var r=e.Growth!.Encounters.Single(r=>r.Id==f.EncounterId);var s=trace.State;
            Require(s.Active==null && r.Kind=="weeklyBoss" && r.RequestId==f.RequestId && r.Identity==q.Identity && r.Revision==f.Revision && r.AtMs==f.AtMs);
            Require(r.PolicyVersion==f.PolicyVersion && r.PolicyHash==f.PolicyHash && r.CoreModule==c.CoreModule && r.ContentSha256==c.ContentSha256 && r.MemoryVersion==f.MemoryVersion);
            Require(r.BossId==Text(q.Args,"bossId") && r.Difficulty==Int(q.Args,"difficulty") && r.Practice==Equals(q.Args["practice"],true));
            var launch=Build(c,e,Party(q.Args),f.Revision,f.PolicyVersion);
            Require(GraphJson.Digest(launch.Party.Cast<object?>().ToList())==GraphJson.Digest(r.Party.Cast<object?>().ToList()) && launch.WeaponInstances.SequenceEqual(r.Weapons));
            Require(GraphJson.Digest(launch.Support?.ToGraph())==GraphJson.Digest(r.Support?.ToGraph()));
            Require(GraphJson.Digest(MemoryAt(e,r.MemoryVersion))==GraphJson.Digest(r.Memory) && GraphJson.Digest(ContentAccount(e,s,f.Revision+1).ToJsonGraph())==GraphJson.Digest(r.AccountAtStart));
            Require(GraphJson.Digest(WeeklyStage(EquipmentPolicy.Modes(c),r.BossId,r.Difficulty))==GraphJson.Digest(r.Stage));Capacity(c,r.Party,r.Memory);
            var battle=r.Start(c);Require(GraphJson.Digest(battle.State.ToJsonGraph())==r.CreatedSha256);trace.Battles.Add(r.Id,battle);s.Active=r.Id;
            return EncounterReply(c,r,battle,0,f.Revision+1,null,null);
        }
        public static Reply StartWeekly(PracticeContent c,PracticeAccount a,AccountEnvelope e,Run? run,GrowthRequest q)
        {
            if(e.Revision!=q.ExpectedRevision) throw new ApiError(409,"STALE_REVISION");NoRun(e,run);
            long now=Clock();var trace=Begin(c,a,e,q.RequestId,now);var state=Effective(trace.State,now);var launch=Build(c,e,Party(q.Args),e.Revision,SupportPolicy.Version);
            string boss=Text(q.Args,"bossId");int difficulty=Int(q.Args,"difficulty");
            var r=new EncounterRecord{Id="ec-"+Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),RequestId=q.RequestId,Identity=q.Identity,Kind="weeklyBoss",BossId=boss,Difficulty=difficulty,Practice=Equals(q.Args["practice"],true),
                PolicyVersion=SupportPolicy.Version,PolicyHash=SupportPolicy.Hash,Revision=e.Revision,AtMs=now,CoreModule=c.CoreModule,ContentSha256=c.ContentSha256,MemoryVersion=AccountEnvelope.MemoryVersion(e.Memory),
                Memory=GraphJson.Copy(e.Memory["memory"]),Party=launch.Party,Weapons=launch.WeaponInstances,Seed=BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)),
                AccountAtStart=ContentAccount(e,state,e.Revision+1).ToJsonGraph(),Stage=WeeklyStage(EquipmentPolicy.Modes(c),boss,difficulty),Support=launch.Support};
            Capacity(c,r.Party,r.Memory);r.CreatedSha256=GraphJson.Digest(r.Start(c).State.ToJsonGraph());e.Growth!.Encounters.Add(r);
            var f=Fact(e,"weeklyBossStart",q.RequestId,now);f.Request=q.Args;f.EncounterId=r.Id;return Append(c,a,e,trace,f);
        }
        static void WeeklyProfile(PracticeContent c,AccountEnvelope? e,GrowthState s,Dictionary<string,object?> view,long at,long before)
        {
            var modes=EquipmentPolicy.Modes(c);var w=modes.WeeklyBoss;var week=EquipmentPolicy.Week(modes,at);int claimed=s.S3.Claims.GetValueOrDefault(week.Key);
            view["weeklyBoss"]=GraphJson.Object("weekKey",week.Key,"resetsAtMs",week.ResetsAtMs,"claimed",claimed,"remaining",w.ClaimsPerWeek-claimed,
                "bosses",w.Bosses.Select(id=>(object?)GraphJson.Object("id",id,"unlockLevel",w.Unlock,"available",e?.Progression?.Cleared(0,before)==true && Account(e,before).Level>=w.Unlock,
                    "difficulties",w.Difficulty.Select(d=>(object?)GraphJson.Object("difficulty",d.Lv,"stage",d.Stage,"hpBps",d.HpBps,"dmgBps",d.DmgBps)).ToList())).ToList());
        }
    }
    static class WeeklyBossRoutes
    {
        public static async Task<Reply> Start(PracticeHost h,PracticeAccount a,byte[] bytes)
        {
            var q=Contracts.ParseWeeklyBoss(bytes);
            try { return await h.Store.Mutate(a,q.RequestId,q.Identity,(e,r)=>GrowthCommands.StartWeekly(h.Content,a,e,r,q)); }
            catch(RuleException ex){throw new ApiError(422,ex.Code);}catch(MemoryBoundaryException){throw new ApiError(422,"COMMAND_REJECTED");}catch(OverflowException){throw new ApiError(422,"RESOURCE_OVERFLOW");}
        }
    }
}
