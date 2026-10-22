using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MemoryForge.Server;

// A second process consumes only the envelope produced by the real earned journey. It never mints a fixture balance.
static class EquipmentRestartChecks
{
    static int checks;
    static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    static string Id()=>Guid.NewGuid().ToString("N");
    static bool Same(byte[] a,byte[] b)=>a.AsSpan().SequenceEqual(b);
    public static async Task<int> Run(string root,string path,string oldPath,string fixturePath)
    {
        var content=PracticeContent.Load(root);var account=new PracticeAccount("growth-check",new byte[32],"short",new List<Dictionary<string,object?>>{GraphJson.Object("charId","sera")});
        byte[] original=File.ReadAllBytes(path);var report=AccountStoreVerifier.Verify(content,account,original,out var envelope);
        var preS3=Envelopes.Decode(File.ReadAllBytes(oldPath),account,content);
        Check(preS3.Growth!=null&&preS3.Growth.Ledger.All(f=>f.PolicyVersion==GrowthPolicy.Version),"original input is actual preS3 policy history");
        Check(preS3.Receipts.Count>0,"original preS3 receipt witnesses exist");
        foreach(var p in preS3.Receipts)Check(envelope.Receipts.TryGetValue(p.Key,out var r)&&r.Status==p.Value.Status&&r.Identity==p.Value.Identity&&r.Body==p.Value.Body,"every original preS3 receipt identity/status/body remains byte exact "+p.Key);
        Check(envelope.GrowthCache!.State.Active==null,"restart terminal encounter inactive");
        Check(report.GrowthReceipts==GrowthCommands.ReceiptIds(envelope).Count(),"offline verifier accounts for every typed S2/S3 receipt");
        Check(report.Receipts==envelope.Receipts.Count && report.Revision==envelope.Revision,"offline verifier reconciles all receipt/revision witnesses");
        var weekly=envelope.Growth!.Encounters.Where(r=>r.Kind=="weeklyBoss").ToList();
        Check(weekly.Select(r=>r.BossId).Distinct().Count()==4,"actual earned history includes all four bosses");
        Check(weekly.Any(r=>r.Difficulty==5)&&weekly.Any(r=>r.Practice),"actual difficulty5 defeat and practice replay on restart");
        var state=envelope.GrowthCache.State;Check(state.S3.Claims.Count==2 && state.S3.Claims.Values.All(n=>n==3),"restart retains both weekly shared claim counts");
        Check(state.S3.BossToken==20 && state.S3.RRefine["basic:w_basic_device"]==3,"restart retains earned shop/refine state");
        Check(envelope.GrowthCache.Settlements.Values.Count(s=>Equals(s.GetValueOrDefault("rewarded"),true))==6,"exactly six rewarded weekly wins across two weeks");
        foreach(var f in envelope.Growth.Ledger.Where(f=>Contracts.EquipmentActions.ContainsKey(f.Kind)))
        {
            using var json=JsonDocument.Parse(envelope.Receipts[f.RequestId].Body);var j=json.RootElement;var cost=j.GetProperty("cost");var before=j.GetProperty("before");var expected=new Dictionary<string,long>(StringComparer.Ordinal);
            void Put(string key,long value){if(value!=0)expected.Add(key,value);}
            switch(f.Kind)
            {
                case "characterAscend": {int asc=before.GetProperty("asc").GetInt32();Put("seal",new[]{4L,8,12,16}[asc]);Put("core",new[]{0L,0,2,4}[asc]);Put("credits",new[]{10000L,20000,40000,60000}[asc]);break;}
                case "skillUp": {int rank=before.GetProperty((string)f.Request["line"]! == "card"?"skillLv":"ultLv").GetInt32();Put("book",new[]{2L,4,6,8,10,14,18,24,32}[rank-1]);Put("credits",new[]{5000L,10000,15000,20000,30000,40000,60000,80000,120000}[rank-1]);Put("core",rank>=8?2:0);break;}
                case "gearCraft":Put("polish",20);Put("credits",10000);break;
                case "gearEnhance": {long level=before.GetProperty((string)f.Request["slot"]!).GetInt64()+1;Put("polish",level);Put("credits",level*1000);break;}
                case "weaponLevelUp": {int from=before.GetProperty("level").GetInt32(),to=j.GetProperty("after").GetProperty("level").GetInt32();long oil=(from,to) switch{(1,20)=>10080,(20,30)=>17780,_=>throw new Exception("unexpected earned weapon-level range")};Put("oil",oil);Put("credits",oil/2);break;}
                case "weaponAscend": {int asc=before.GetProperty("asc").GetInt32();Put("alloy",new[]{2L,4,6,8}[asc]);Put("credits",new[]{5000L,10000,20000,30000}[asc]);break;}
                case "weaponRefine":Put("refine_core",1);break;
                case "bossExchange":Put("boss_token",(string)f.Request["itemId"]! switch{"refine_core"=>60L,"book"=>10L,"polish"=>5L,_=>throw new Exception("exchange item")}*Convert.ToInt32(f.Request["quantity"]));break;
                case "weaponEquip":break;
            }
            Check(cost.EnumerateObject().Count()==expected.Count && expected.All(p=>cost.TryGetProperty(p.Key,out var n)&&n.GetInt64()==p.Value),"independent authored exact cost in earned receipt "+f.Kind);
        }
        foreach(var r in weekly)
        {
            var settled=envelope.GrowthCache.Settlements[r.Id];var grant=(Dictionary<string,object?>)settled["grant"]!;var resources=(Dictionary<string,object?>)grant["resources"]!;bool rewarded=Equals(settled["rewarded"],true);
            Check(Convert.ToInt32(((Dictionary<string,object?>)settled["cost"]!)["focus"])==0 && resources.Count==10,"weekly settlement exact cost0 and ten canonical resources");
            Check(resources.All(p=>Convert.ToInt64(p.Value)==(rewarded?p.Key switch{"core"=>2,"credits"=>20000,"boss_token"=>40,_=>0}:0)) && Convert.ToInt64(grant["accountXp"])==(rewarded?100:0),"weekly actual settlement exact authored grant or zero");
        }
        void Corrupt(Action<AccountEnvelope> change,string label)
        {
            var edited=Envelopes.Decode(original,account,content);change(edited);byte[] rehashed=Envelopes.Encode(edited);
            try{Envelopes.Decode(rehashed,account,content);throw new Exception("accepted rehashed "+label);}catch(ApiError e){Check(e.Code=="STORE_CORRUPT","rehashed "+label+" rejected by full replay");}
        }
        Corrupt(e=>e.Growth!.Encounters.Last().BossId="b_notary","weekly boss metadata");
        Corrupt(e=>e.Growth!.Encounters.Last().CreatedSha256=new string('0',64),"weekly creation checkpoint");
        Corrupt(e=>e.Growth!.Encounters.Last().Journal.Last().Sha256=new string('0',64),"weekly command checkpoint");
        Corrupt(e=>e.Growth!.Ledger.Last(f=>f.Kind=="weeklyBossVictory").WeekKey="1999-01-04","weekly terminal week");
        Corrupt(e=>e.Growth!.Ledger.First(f=>f.Kind=="skillUp").Request.Add("clock",0L),"equipment strict stored action");
        var clock=GrowthCommands.Clock;GrowthCommands.Clock=()=>envelope.Growth.Ledger.Last().AtMs;
        string directory=Path.Combine(Path.GetTempPath(),"mf-equipment-restart-"+Id());Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var persistence=new FileEnvelopePersistence(directory);var store=new PracticeRunStore(content,persistence);bool complete=false;
        try
        {
            await persistence.TransactAsync(account,original,_=>new Transition(Reply.Json(200,null),original));
            string accountFile=Path.Combine(directory,"account-growth-check.json");var stamp=File.GetLastWriteTimeUtc(accountFile);var mode=File.GetUnixFileMode(accountFile);
            var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"/Users/choi/.dotnet/dotnet"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
            foreach(string arg in new[]{Path.Combine(AppContext.BaseDirectory,"MemoryForge.Server.dll"),"--verify-store","--MemoryForge:ContentRoot="+root,"--MemoryForge:PracticeFixture="+fixturePath,"--MemoryForge:PracticeStore="+directory,"--MemoryForge:PracticePort=0"})start.ArgumentList.Add(arg);
            start.Environment["DOTNET_ENVIRONMENT"]="Development";start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";
            using(var process=Process.Start(start)??throw new Exception("spawn verifier"))
            {
                var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();string text=await stdout;string error=await stderr;
                Check(process.ExitCode==0&&error.Length==0,"spawned actual verifier validates complete earned account");
                string capacity=text.Split('\n').Single(line=>line.StartsWith("verify-store capacity id=growth-check char=nia ",StringComparison.Ordinal));
                Check(capacity.Contains(" asc=2 ownedCapacity=7 owned=OK",StringComparison.Ordinal),"spawned actual verifier current Nia asc2/capacity7 (observed "+capacity+")");
            }
            Check(Same(original,(await persistence.ReadAsync(account))!)&&File.GetLastWriteTimeUtc(accountFile)==stamp&&File.GetUnixFileMode(accountFile)==mode,"spawned verifier preserves complete bytes/mtime/mode");
            var q=new GrowthRequest(Id(),envelope.Revision,"gearEnhance",GraphJson.Object("charId","harin","slot","armor"));var preview=GrowthCommands.EquipmentPreview(content,envelope,q);
            using(var j=JsonDocument.Parse(preview.Body))Check(j.RootElement.GetProperty("canCommit").GetBoolean()&&j.RootElement.GetProperty("cost").GetProperty("polish").GetInt64()==1&&j.RootElement.GetProperty("cost").GetProperty("credits").GetInt64()==1000,"restart earned armor preview exact1polish1000credits");
            Check(Same(original,(await persistence.ReadAsync(account))!),"restart preview byte-pure");
            var reply=await store.Mutate(account,q.RequestId,q.Identity,(e,r)=>GrowthCommands.Mutate(content,account,e,r,q));byte[] committed=(await persistence.ReadAsync(account))!;var after=Envelopes.Decode(committed,account,content);
            Check(after.GrowthCache!.State.Resources.All(p=>p.Value==state.Resources[p.Key]-(p.Key switch{"polish"=>1,"credits"=>1000,_=>0}))&&after.GrowthCache.State.S3.BossToken==state.S3.BossToken,"exact live material delta only once after restart");
            var retry=await store.Mutate(account,q.RequestId,q.Identity,(_,_)=>throw new Exception("retry reexecuted"));Check(reply.Body==retry.Body&&Same(committed,(await persistence.ReadAsync(account))!),"lost-response retry body and bytes identical");
            var stale=new GrowthRequest(Id(),q.ExpectedRevision,q.Kind,q.Args);
            try{await store.Mutate(account,stale.RequestId,stale.Identity,(e,r)=>GrowthCommands.Mutate(content,account,e,r,stale));throw new Exception("stale action accepted");}catch(ApiError e){Check(e.Code=="STALE_REVISION"&&Same(committed,(await persistence.ReadAsync(account))!),"stale action no write or receipt");}
            var final=AccountStoreVerifier.Verify(content,account,committed,out _);Check(final.Revision==report.Revision+1&&final.GrowthReceipts==report.GrowthReceipts+1,"post-restart normal verifier covers new exact equipment receipt");
            complete=true;
        }
        finally{GrowthCommands.Clock=clock;if(complete)Directory.Delete(directory,true);else Console.WriteLine("Retained isolated restart store "+directory);}
        Console.WriteLine("EquipmentRestartChecks: "+checks+" passed; full earned S3 normal decode/offline verifier/exact costs/tamper/restart");return 0;
    }
}
