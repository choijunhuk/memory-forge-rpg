using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MemoryForge.Core;
namespace MemoryForge.Server
{
    sealed class FrozenSupport
    {
        public string CharId="",WeaponInstanceId="";
        public Dictionary<string,object?> Build=GraphJson.Object();
        public Dictionary<string,object?> ToGraph()=>GraphJson.Object("charId",CharId,"weaponInstanceId",WeaponInstanceId,"build",Build);
        public RunPartyEntry ToCore()=>RunPartyEntry.FromJsonGraph(GraphJson.Copy(Build));
        public static FrozenSupport Parse(JsonElement j)
        {
            BattleDataLoader.Fields(j,"charId","weaponInstanceId","build");var s=new FrozenSupport{CharId=Envelopes.Text(j,"charId"),WeaponInstanceId=Envelopes.Text(j,"weaponInstanceId"),Build=(Dictionary<string,object?>)MemoryV2DataLoader.Graph(j.GetProperty("build"))!};
            if(!Equals(s.Build.GetValueOrDefault("charId"),s.CharId))throw new InvalidDataException("support identity");_=s.ToCore();return s;
        }
    }
    static partial class Contracts
    {
        public static GrowthRequest ParseSupportSelection(byte[] bytes)
        {
            using var d=Document(bytes);var m=Members(d.RootElement,"requestId","expectedRevision","charId");
            if(!m.TryGetValue("charId",out var c))throw new ApiError(400,"INVALID_REQUEST");
            return new GrowthRequest(RequestId(m),Revision(m,"expectedRevision"),"supportSelection",GraphJson.Object("charId",c.ValueKind==JsonValueKind.Null?null:StringValue(c,"INVALID_REQUEST")));
        }
    }
    static partial class GrowthCommands
    {
        internal static bool SupportActive(AccountEnvelope e,long before=long.MaxValue)=>e.Growth?.Ledger.Any(f=>f.PolicyVersion==SupportPolicy.Version&&f.Revision<before)==true||e.Progression?.Ledger.Any(f=>f.PolicyVersion==SupportPolicy.Version&&f.Revision<before)==true;
        internal static string? SelectedSupport(AccountEnvelope? e,long before=long.MaxValue)=>e?.Growth?.Ledger.LastOrDefault(f=>f.Kind=="supportSelection"&&f.Revision<before)?.Request["charId"] as string;
        static void SupportInitialized(AccountEnvelope e,long before)
        {if(e.Progression?.Ledger.Any(f=>f.Kind=="launch"&&f.Revision<before)!=true)throw new ApiError(422,"TUTORIAL_REQUIRED");}
        static void SupportEligible(PracticeContent c,AccountEnvelope e,string? cid,long before)
        {if(cid!=null)_=V3Party(c,e,new[]{cid},before);}
        static Dictionary<string,object?> ApplySupportSelection(PracticeContent c,PracticeAccount account,AccountEnvelope e,GrowthTrace trace,GrowthFact f)
        {
            Require(f.PolicyVersion==SupportPolicy.Version&&f.PolicyHash==SupportPolicy.Hash&&f.JournalIndex==0&&f.EncounterId.Length==0&&f.TerminalStateSha256.Length==0);
            var q=StoredRequest(f);SupportInitialized(e,f.Revision+1);string? selected=q.Args["charId"] as string,prior=SelectedSupport(e,f.Revision);
            if(selected==prior)throw new ApiError(422,"ALREADY_SELECTED");SupportEligible(c,e,selected,f.Revision+1);
            return GraphJson.Object("requestId",f.RequestId,"revision",f.Revision+1,"kind",f.Kind,"target",GraphJson.Object("charId",selected),"before",GraphJson.Object("charId",prior),
                "after",GraphJson.Object("charId",selected),"cost",GraphJson.Object(),"profile",ProfileAt(c,account,e,trace,f));
        }
        public static Reply SelectSupport(PracticeContent c,PracticeAccount a,AccountEnvelope e,GrowthRequest q)
        {
            SupportPolicy.RequireContent(c);
            if(e.Revision!=q.ExpectedRevision)throw new ApiError(409,"STALE_REVISION");SupportInitialized(e,e.Revision+1);var trace=Trace(c,a,e);long now=Clock();
            if(e.Growth==null){e.Growth=new GrowthRecord();var init=Fact(e,"growthInitialize",q.RequestId,now);init.PolicyVersion=SupportPolicy.Version;init.PolicyHash=SupportPolicy.Hash;e.Growth.Ledger.Add(init);Apply(c,a,e,trace,init);}
            var f=Fact(e,"supportSelection",q.RequestId,now);f.Request=q.Args;return Append(c,a,e,trace,f);
        }
        internal static FrozenSupport? FreezeSupport(PracticeContent c,AccountEnvelope e,IEnumerable<string> party,IEnumerable<string> instances,long before)
        {
            string? selected=SelectedSupport(e,before);if(selected==null)return null;if(party.Contains(selected))throw new ApiError(422,"SUPPORT_IN_PARTY");
            var owned=V3Party(c,e,new[]{selected},before);if(instances.Contains(owned.Instances[0]))throw new ApiError(422,"NO_ELIGIBLE_WEAPON");
            return new FrozenSupport{CharId=selected,WeaponInstanceId=owned.Instances[0],Build=owned.Party[0]};
        }
        internal static ExpeditionLaunch BuildLaunchV4(PracticeContent c,AccountEnvelope e,ExpeditionRequest q)
        {
            SupportPolicy.RequireContent(c);
            var launch=BuildLaunchV3(c,e,q);launch.PolicyVersion=SupportPolicy.Version;launch.PolicyHash=SupportPolicy.Hash;
            launch.Support=FreezeSupport(c,e,q.Party!,launch.WeaponInstances,q.ExpectedRevision+1);return launch;
        }
        static void SupportProfile(AccountEnvelope? e,Dictionary<string,object?> profile,long before)
        {profile["policyVersion"]=SupportPolicy.Version;profile.Add("supportSelection",SelectedSupport(e,before));}
    }
    static class SupportRoutes
    {
        public static Task<Reply> Select(PracticeHost h,PracticeAccount a,byte[] bytes)
        {var q=Contracts.ParseSupportSelection(bytes);return h.Store.Mutate(a,q.RequestId,q.Identity,(e,_)=>GrowthCommands.SelectSupport(h.Content,a,e,q));}
    }
}
