using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MemoryForge.Core;
using MemoryForge.Server;
[assembly: System.Runtime.Versioning.UnsupportedOSPlatform("windows")]

static class EquipmentJourney
{
    static int checks;
    static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    static string Id()=>Guid.NewGuid().ToString("N");
    static bool Same(byte[] a,byte[] b)=>a.AsSpan().SequenceEqual(b);
    static long now=1800000000000;
    static readonly List<object?> Party=new List<object?>{"nia","harin","rien"};
    static async Task<int> Main(string[] args)
    {
        try
        {
            if(args.Length>0 && args[0]=="--capture-old") return await Capture(args[1],args[2]);
            if(args.Length>0 && args[0]=="--audit-earned") return await EquipmentRestartChecks.Run(args[1],args[2],args[3],args[4]);
            if(args.Length>0 && args[0]=="--binding-refusal")
            {
                var c=PracticeContent.Load(args[1]);var a=new PracticeAccount("growth-check",new byte[32],"short",new List<Dictionary<string,object?>>{GraphJson.Object("charId","sera")});
                try{Envelopes.Decode(File.ReadAllBytes(args[2]),a,c);throw new Exception("mismatched runtime accepted old binding");}
                catch(ApiError e){Check(e.Code=="STORE_IMPLEMENTATION_MISMATCH","exact old Core binding remains enforced");Console.WriteLine("Expected old-Core binding refusal PASS");return 0;}
            }
            if(EquipmentChecks.Run()!=0)return 1;
            if(args.Length<2)return 0;
            await Journey(Path.GetFullPath(args[0]),Path.GetFullPath(args[1]));
            Console.WriteLine("EquipmentJourney: "+checks+" passed (real earned isolated account; no live accounts)");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static async Task<int> Capture(string oldRoot,string output)
    {
        Directory.CreateDirectory(output,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        string temp=Path.Combine(output,"old-runtime-temp");Directory.CreateDirectory(temp);
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"/Users/choi/.dotnet/dotnet"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,WorkingDirectory=oldRoot};
        start.ArgumentList.Add(Path.Combine(oldRoot,"server/MemoryForge.GrowthChecks/bin/Debug/net10.0/MemoryForge.GrowthChecks.dll"));start.ArgumentList.Add(oldRoot);
        start.Environment["TMPDIR"]=temp+Path.DirectorySeparatorChar;
        using var process=Process.Start(start)??throw new Exception("old verifier process");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        byte[]? captured=null;
        while(!process.HasExited)
        {
            foreach(string file in Directory.EnumerateFiles(temp,"account-growth-check.json",SearchOption.AllDirectories))
            {
                try
                {
                    byte[] bytes=File.ReadAllBytes(file);using var outer=JsonDocument.Parse(bytes);using var body=JsonDocument.Parse(outer.RootElement.GetProperty("body").GetString()!);
                    if(body.RootElement.TryGetProperty("growth",out var growth) && growth.GetProperty("ledger").EnumerateArray().Any(f=>f.GetProperty("kind").GetString()=="characterLevelUp")) captured=bytes;
                }
                catch(IOException){}catch(JsonException){}
            }
            await Task.Delay(75);
        }
        await process.WaitForExitAsync();string text=await stdout;string error=await stderr;
        if(process.ExitCode!=0 || captured==null)throw new Exception("preS3 verifier failed: "+text+error);
        File.WriteAllBytes(Path.Combine(output,"pre-s3-account.json"),captured);File.SetUnixFileMode(Path.Combine(output,"pre-s3-account.json"),UnixFileMode.UserRead|UnixFileMode.UserWrite);
        Console.WriteLine(text.Trim());Console.WriteLine("Captured preS3 envelope SHA256 "+GraphJson.Sha256(captured));return 0;
    }
    static async Task Journey(string root,string oldEnvelope)
    {
        var content=PracticeContent.Load(root);var a=new PracticeAccount("growth-check",new byte[32],"short",new List<Dictionary<string,object?>>{GraphJson.Object("charId","sera")});
        byte[] original=File.ReadAllBytes(oldEnvelope);var old=Envelopes.Decode(original,a,content);
        var modes=EquipmentPolicy.Modes(content);Check(modes.WeeklyBoss.Bosses.SequenceEqual(new[]{"b_bell","b_notary","b_garden","b_archivist"}),"four authored weekly bosses");
        var stages=new[]{30,40,50,60,60};
        foreach(string boss in modes.WeeklyBoss.Bosses)for(int diff=1;diff<=5;diff++){var row=modes.WeeklyBoss.Difficulty[diff-1];Check(row.Stage==stages[diff-1]&&row.HpBps==(diff==5?13000:0)&&row.DmgBps==(diff==5?12000:0),"weekly exactstage/scaling "+boss+":"+diff);}
        long boundary=new DateTimeOffset(2026,10,25,20,0,0,TimeSpan.Zero).ToUnixTimeMilliseconds();Check(EquipmentPolicy.WeekAt(boundary-1).Key=="2026-10-19"&&EquipmentPolicy.WeekAt(boundary).Key=="2026-10-26","KST Monday05 exact reset boundary");
        Check(old.Growth!=null && old.Growth.Ledger.All(f=>f.PolicyVersion==GrowthPolicy.Version),"normal Decode accepts actual preS3 envelope under preserved exact old Core");
        var oldReceipts=old.Receipts.ToDictionary(p=>p.Key,p=>(p.Value.Identity,p.Value.Status,p.Value.Body),StringComparer.Ordinal);
        string directory=Path.Combine(Path.GetTempPath(),"mf-equipment-"+Id());Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var persistence=new FileEnvelopePersistence(directory);var store=new PracticeRunStore(content,persistence);var clock=GrowthCommands.Clock;GrowthCommands.Clock=()=>now;
        await persistence.TransactAsync(a,original,_=>new Transition(Reply.Json(200,null),original));
        bool completed=false;
        try
        {
            string firstV3=await StartDungeon("dg_ink",1);
            Check((await Read()).Growth!.Encounters.Single(r=>r.Id==firstV3).PolicyVersion==EquipmentPolicy.Version,"first new owned launch pins v3 without prior equipment action");
            await Fight(firstV3,false);
            await Action("weaponEquip","instanceId","basic:w_basic_device",GraphJson.Object("charId",null));
            var noWeapon=await Read();Check(noWeapon.GrowthCache!.State.S3.Equip["basic:w_basic_device"]==null,"explicit unequip is real");
            await Refused(()=>StartDungeon("dg_ink",1),"NO_ELIGIBLE_WEAPON");
            await Action("weaponEquip","instanceId","basic:w_basic_device",GraphJson.Object("charId","nia"));
            var upgraded=await Read();
            if(GrowthCommands.Account(upgraded).Level<10)
            {
                await Farm("dg_ink",1,"ink",25000);await Farm("dg_credit",1,"credits",20000);await Level("nia",19);
                await DeniedAction("characterAscend","charId","nia",GraphJson.Object(),"ASCENSION_LEVEL_REQUIRED");await Level("nia",20);
                Check(GrowthCommands.Account(await Read()).Level<10,"low-account ascension test uses genuinely earned low AL");
                await DeniedAction("characterAscend","charId","nia",GraphJson.Object(),"ACCOUNT_LEVEL_REQUIRED");upgraded=await Read();
            }
            foreach(var p in oldReceipts)Check(upgraded.Receipts.TryGetValue(p.Key,out var r)&&r.Identity==p.Value.Identity&&r.Status==p.Value.Status&&r.Body==p.Value.Body,"preS3 saved receipt bytes retained "+p.Key);
            Console.WriteLine("EquipmentJourney: actual preS3 receipt preservation "+oldReceipts.Count);
            while(GrowthCommands.Account(upgraded).Level<20){now+=86400000;await Sweep("dg_ink",1,12);upgraded=await Read();}
            Check(GrowthCommands.Account(upgraded).Level>=20,"AL20 earned from real3star sweeps only");
            await Farm("dg_credit",1,"credits",1000000);
            foreach(string cid in Party.Cast<string>())await Level(cid,20);
            await Farm("dg_seal",2,"seal",20);foreach(string cid in Party.Cast<string>())await Action("characterAscend","charId",cid,GraphJson.Object());
            foreach(string cid in Party.Cast<string>())await Level(cid,30);
            await Farm("dg_oil",1,"oil",150000);
            foreach(string weapon in new[]{"w_basic_device","w_basic_melee","w_basic_ritual"})await Action("weaponLevelUp","instanceId","basic:"+weapon,GraphJson.Object("targetLevel",20));
            await Farm("dg_oil",6,"alloy",6);
            foreach(string weapon in new[]{"w_basic_device","w_basic_melee","w_basic_ritual"}){await Action("weaponAscend","instanceId","basic:"+weapon,GraphJson.Object());await Action("weaponLevelUp","instanceId","basic:"+weapon,GraphJson.Object("targetLevel",30));}
            await Farm("dg_polish",1,"polish",300);await Farm("dg_book",1,"book",12);
            foreach(string cid in Party.Cast<string>())
            {
                await Action("gearCraft","charId",cid,GraphJson.Object("slot","armor"));
                if(cid!="rien")await Action("gearCraft","charId",cid,GraphJson.Object("slot","accessory"));
                await Action("skillUp","charId",cid,GraphJson.Object("line","card"));await Action("skillUp","charId",cid,GraphJson.Object("line","ult"));
            }
            for(int i=1;i<=15;i++)await Action("gearEnhance","charId","nia",GraphJson.Object("slot","armor"));
            await DeniedAction("gearEnhance","charId","nia",GraphJson.Object("slot","armor"),"GEAR_CAP");
            var current=await Read();var profile=GrowthCommands.Profile(content,a.Id,current,null);var nia=((IList)profile["characters"]!).Cast<Dictionary<string,object?>>().Single(c=>(string)c["charId"]! == "nia");
            await Refused(()=>{GrowthCommands.Preview(content,current,new GrowthRequest("preview-only",current.Revision,"characterLevelUp",GraphJson.Object("charId","nia","targetLevel",31)));return Task.CompletedTask;},"LEVEL_CAP");
            await Farm("dg_seal",2,"seal",24);await Action("characterAscend","charId","nia",GraphJson.Object());
            current=await Read();var levelPreview=GrowthCommands.Preview(content,current,new GrowthRequest("preview-only",current.Revision,"characterLevelUp",GraphJson.Object("charId","nia","targetLevel",31)));
            var currentProfile=GrowthCommands.Profile(content,a.Id,current,null);var charRow=((IList)currentProfile["characters"]!).Cast<Dictionary<string,object?>>().Single(row=>(string)row["charId"]! == "nia");
            using(var j=JsonDocument.Parse(levelPreview.Body))Check(j.RootElement.GetProperty("before").GetProperty("stats").GetProperty("power").GetInt32()==Convert.ToInt32(((Dictionary<string,object?>)charRow["stats"]!)["power"]),"level preview uses actual level30 equipped weapon");
            await WeeklyProof();
            Console.WriteLine("EquipmentJourney: complete earned build/weekly journey");completed=true;
        }
        finally{GrowthCommands.Clock=clock;if(completed)Directory.Delete(directory,true);else Console.WriteLine("Retained isolated test store "+directory);}

        async Task<AccountEnvelope> Read()=>Envelopes.Decode((await persistence.ReadAsync(a))!,a,content);
        async Task<Reply> Mutate(GrowthRequest q)=>await store.Mutate(a,q.RequestId,q.Identity,(e,r)=>GrowthCommands.Mutate(content,a,e,r,q));
        async Task Action(string kind,string targetKey,string target,Dictionary<string,object?> fields)
        {
            var e=await Read();var args=(Dictionary<string,object?>)GraphJson.Copy(fields)!;args.Add(targetKey,target);var q=new GrowthRequest(Id(),e.Revision,kind,args);
            byte[] before=(await persistence.ReadAsync(a))!;var p=GrowthCommands.EquipmentPreview(content,e,q);Check(Same(before,(await persistence.ReadAsync(a))!),"equipment preview pure");
            using(var j=JsonDocument.Parse(p.Body))Check(j.RootElement.GetProperty("canCommit").GetBoolean(),"earned resources fund "+kind);
            var result=await Mutate(q);var retry=await store.Mutate(a,q.RequestId,q.Identity,(_,_)=>throw new Exception("equipment retry executed"));Check(result.Body==retry.Body,"equipment receipt once");
        }
        async Task DeniedAction(string kind,string targetKey,string target,Dictionary<string,object?> fields,string code)
        {
            var e=await Read();var args=(Dictionary<string,object?>)GraphJson.Copy(fields)!;args.Add(targetKey,target);var q=new GrowthRequest(Id(),e.Revision,kind,args);byte[] before=(await persistence.ReadAsync(a))!;
            await Refused(()=>Mutate(q),code);Check(Same(before,(await persistence.ReadAsync(a))!),"denied "+kind+" preserves all bytes/receipts");
        }
        async Task Level(string cid,int target)
        {var e=await Read();if(e.GrowthCache!.State.Levels.GetValueOrDefault(cid,1)>=target)return;var q=new GrowthRequest(Id(),e.Revision,"characterLevelUp",GraphJson.Object("charId",cid,"targetLevel",target));await Mutate(q);}
        async Task Sweep(string dungeon,int tier,int times)
        {var e=await Read();await Mutate(new GrowthRequest(Id(),e.Revision,"dungeonSweep",GraphJson.Object("dungeonId",dungeon,"tier",tier,"times",times)));}
        async Task<string> StartDungeon(string dungeon,int tier)
        {
            now+=86400000;var e=await Read();var q=new GrowthRequest(Id(),e.Revision,"encounterStart",GraphJson.Object("dungeonId",dungeon,"tier",tier,"party",Party));
            var reply=await store.Mutate(a,q.RequestId,q.Identity,(x,r)=>GrowthCommands.Start(content,a,x,r,q));using var j=JsonDocument.Parse(reply.Body);return j.RootElement.GetProperty("encounterId").GetString()!;
        }
        async Task<string> StartWeekly(string boss,int difficulty,bool practice)
        {
            var e=await Read();var q=new GrowthRequest(Id(),e.Revision,"weeklyBossStart",GraphJson.Object("bossId",boss,"difficulty",difficulty,"practice",practice,"party",Party));
            var reply=await store.Mutate(a,q.RequestId,q.Identity,(x,r)=>GrowthCommands.StartWeekly(content,a,x,r,q));using var j=JsonDocument.Parse(reply.Body);
            Check(j.RootElement.GetProperty("kind").GetString()=="weeklyBoss" && !j.RootElement.GetProperty("state").TryGetProperty("rng",out _),"weekly public discriminator no private RNG");
            return j.RootElement.GetProperty("encounterId").GetString()!;
        }
        async Task<Reply> Exchange(string item,int quantity)
        {
            var e=await Read();var q=new GrowthRequest(Id(),e.Revision,"bossExchange",GraphJson.Object("itemId",item,"quantity",quantity));
            var preview=GrowthCommands.EquipmentPreview(content,e,q);using(var j=JsonDocument.Parse(preview.Body))Check(j.RootElement.GetProperty("canCommit").GetBoolean(),"earned weekly tokens fund exchange");
            var reply=await Mutate(q);var retry=await store.Mutate(a,q.RequestId,q.Identity,(_,_)=>throw new Exception("exchange repeated"));Check(reply.Body==retry.Body,"exchange retry exactly once");return reply;
        }
        async Task WeeklyProof()
        {
            string week=EquipmentPolicy.WeekAt(now).Key;var before=await Read();long tokens=before.GrowthCache!.State.S3.BossToken;long core=before.GrowthCache.State.Resources["core"];int focus=before.GrowthCache.State.Focus;
            foreach(string boss in new[]{"b_bell","b_notary","b_garden"})
            {
                var id=await StartWeekly(boss,1,false);
                if(boss=="b_bell")
                {
                    var active=await Read();string frozen=GraphJson.Digest(active.GrowthCache!.Battles[id].State.ToJsonGraph());int rank=active.GrowthCache.State.S3.Skill.GetValueOrDefault("nia",1);
                    await Action("skillUp","charId","nia",GraphJson.Object("line","card"));var edited=await Read();Check(GraphJson.Digest(edited.GrowthCache!.Battles[id].State.ToJsonGraph())==frozen,"active weekly immutable after purchased skill");
                    Check(edited.GrowthCache.Battles[id].State.Actors.Single(a=>a.Id=="nia").SkillLv==rank,"Core gets base skill once");
                }
                var settlement=await Fight(id,false);
                Check(Equals(settlement["rewarded"],true)&&settlement["reason"]==null,"actual rewarded weekly "+boss);
            }
            var earned=await Read();Check(earned.GrowthCache!.State.S3.Claims[week]==3 && earned.GrowthCache.State.S3.BossToken==tokens+120 && earned.GrowthCache.State.Resources["core"]==core+6,"three shared weekly claims exact120tokens6core");
            Check(earned.GrowthCache.State.Focus==focus,"weekly costs no focus");
            var fourth=await Fight(await StartWeekly("b_bell",1,false),false);Check(Equals(fourth["reason"],"WEEKLY_LIMIT")&&Equals(fourth["rewarded"],false),"fourth actual victory earns0");
            await Exchange("refine_core",2);var bought=await Read();Check(bought.GrowthCache!.State.Resources["refine_core"]==2 && bought.GrowthCache.State.S3.BossToken==0,"120tokens buy2refinement cores");
            await Action("weaponRefine","instanceId","basic:w_basic_device",GraphJson.Object());await Action("weaponRefine","instanceId","basic:w_basic_device",GraphJson.Object());
            var refined=await Read();Check(refined.GrowthCache!.State.Resources["refine_core"]==0 && refined.GrowthCache.State.Resources["core"]==core+6,"Rrefine uses refinement core not ascension core");
            await DeniedAction("weaponRefine","instanceId","basic:w_basic_device",GraphJson.Object(),"INSUFFICIENT_RESOURCES");
            long reset=EquipmentPolicy.WeekAt(now).ResetsAtMs;now=reset-1;string spanning=await StartWeekly("b_bell",1,false);now=reset;
            var newWeekWin=await Fight(spanning,false);Check(Equals(newWeekWin["weekKey"],EquipmentPolicy.WeekAt(reset).Key)&&Equals(newWeekWin["rewarded"],true),"started before reset settles in new week");
            await Fight(await StartWeekly("b_notary",1,false),false);await Fight(await StartWeekly("b_garden",1,false),false);
            await Exchange("polish",20);await Action("gearCraft","charId","rien",GraphJson.Object("slot","accessory"));
            var newWeek=await Read();Check(newWeek.GrowthCache!.State.S3.BossToken==20,"second earned week buys20polish leaving20tokens");
            var practice=await Fight(await StartWeekly("b_archivist",1,true),false);Check(Equals(practice["reason"],"PRACTICE"),"practice actual victory pays0");
            var defeat=await Fight(await StartWeekly("b_bell",5,false),true);Check(Equals(defeat["reason"],"DEFEAT")&&Equals(((Dictionary<string,object?>)defeat["cost"]!)["focus"],0),"weekly actual defeat settles0cost");
            now=reset-1;var rollback=await Fight(await StartWeekly("b_bell",1,false),false);Check(Equals(rollback["reason"],"WEEKLY_LIMIT"),"clock rollback retains old week shared count");
            var snapshot=(await persistence.ReadAsync(a))!;await store.Read(a,(e,r)=>{GrowthCommands.Profile(content,a.Id,e,r);return Reply.Json(200,null);});Check(Same(snapshot,(await persistence.ReadAsync(a))!),"weekly GET no grant/reset/write");
            var final=await Read();foreach(var receipt in oldReceipts)Check(final.Receipts[receipt.Key].Body==receipt.Value.Body,"all preS3 receipt bytes survive full equipment/weekly history");
        }
        async Task Farm(string dungeon,int tier,string material,long target)
        {
            for(int loops=0;loops<100;loops++)
            {
                var e=await Read();if(e.GrowthCache!.State.Resources[material]>=target)return;now+=86400000;
                if(e.GrowthCache.State.Stars.GetValueOrDefault(dungeon+":"+tier)>=3)await Sweep(dungeon,tier,12);
                else {var id=await StartDungeon(dungeon,tier);await Fight(id,false);}
            }
            throw new Exception("earned material loop bound "+dungeon);
        }
        async Task<Dictionary<string,object?>> Fight(string id,bool onlyEnd)
        {
            for(int step=0;step<1500;step++)
            {
                var e=await Read();var battle=e.GrowthCache!.Battles[id];var b=battle.State;
                if(b.Phase!="PLAYER")return e.GrowthCache.Settlements[id];
                string kind="end";var args=GraphJson.Object();string target=b.Enemies.Where(x=>x.Alive).OrderBy(x=>x.Hp).First().Uid;
                if(!onlyEnd)
                {
                    var ult=b.Actors.FirstOrDefault(x=>x.Gauge>=content.Rules.GaugeMax);
                    if(ult!=null){kind="ult";args=GraphJson.Object("actor",ult.Id,"target",target);}
                    else
                    {
                        var plays=b.Zones.Hand.Where(uid=>battle.Check(uid,"now",target)==null).Select(uid=>(Uid:uid,Forecast:battle.Forecast(uid,"now",target))).OrderByDescending(x=>x.Forecast.EnemyHpLoss*10+x.Forecast.BlockDelta*.2+x.Forecast.HpDelta*(b.Hp<b.MaxHp*.75?5:.1)).ToList();
                        if(plays.Count>0){kind="play";args=GraphJson.Object("uid",plays[0].Uid,"mode","now","target",target);}
                    }
                }
                int version=e.Growth!.Encounters.Single(x=>x.Id==id).Journal.Count;string rid=Id();var q=new CommandRequest(rid,version,kind,args,Contracts.EncounterIdentity(id,version,kind,args));
                await store.Mutate(a,q.RequestId,q.Identity,(x,_)=>GrowthCommands.Command(content,a,x,id,q));
            }
            throw new Exception("real encounter bound");
        }
    }
    static async Task Refused(Func<Task> act,string expected)
    {try{await act();throw new Exception("accepted "+expected);}catch(ApiError e){Check(e.Code==expected,"expected "+expected+" got "+e.Code);}catch(RuleException e){Check(e.Code==expected,"Core expected "+expected+" got "+e.Code);}}
}
