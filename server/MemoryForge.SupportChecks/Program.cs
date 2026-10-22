using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MemoryForge.Core;
using MemoryForge.Server;
[assembly:System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
static class SupportChecks
{
    static int checks;
    static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    static string Id()=>Guid.NewGuid().ToString("N");
    static bool Same(byte[] a,byte[] b)=>a.AsSpan().SequenceEqual(b);
    static async Task<int> Main(string[] args)
    {
        try{string? pg=null;bool http=false;for(int i=2;i<args.Length;i++){if(args[i]=="--postgres-file"&&i+1<args.Length)pg=Path.GetFullPath(args[++i]);else if(args[i]=="--http")http=true;else throw new Exception("support check arguments");}await Journey(args[0],args[1],pg,http);Console.WriteLine("SupportChecks: "+checks+" passed (real migrated earned account, actual recruitment, frozen build and Core actions)");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static async Task Journey(string root,string migrated,string? postgresFile,bool http)
    {
        var content=PracticeContent.Load(root);var account=new PracticeAccount("growth-check",new byte[32],"short",new List<Dictionary<string,object?>>{GraphJson.Object("charId","sera")});byte[] initial=File.ReadAllBytes(migrated);
        var start=AccountStoreVerifier.Verify(content,account,initial,out var original);Check(start.GrowthReceipts==993,"full S3-earned prefix normally verified at this exact new Core binding");
        string directory=Path.Combine(Path.GetTempPath(),"mf-support-earned-"+Id());Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        PostgresEnvelopePersistence? postgres=postgresFile==null?null:await PostgresEnvelopePersistence.OpenAsync(PostgresSettings.ConnectionString(postgresFile,root));
        IEnvelopePersistence persistence=postgres??(IEnvelopePersistence)new FileEnvelopePersistence(directory);var store=new PracticeRunStore(content,persistence);
        byte[]? existing=await persistence.ReadAsync(account);if(existing==null)await persistence.TransactAsync(account,initial,current=>{if(current!=null)throw new Exception("test alias raced");return new Transition(Reply.Json(200,null),initial);});else Check(Same(existing,initial),"reuse only exact already-proven isolated migration target; differing existing row untouched");
        Process? process=null;HttpClient? api=null;
        var clock=GrowthCommands.Clock;long now=original.Growth!.Ledger.Last().AtMs+86400000;GrowthCommands.Clock=()=>now;
        try
        {
            if(http)
            {
                string bearer=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));string fixture=Path.Combine(directory,"http-fixture.json");File.WriteAllBytes(fixture,GraphJson.Bytes(GraphJson.Object("schema",PracticeFixture.Schema,"developmentOnly",true,"label","isolated synthetic S4 HTTP proof","accounts",new List<object?>{GraphJson.Object("accountId",account.Id,"credentialSha256",GraphJson.Sha256(Encoding.UTF8.GetBytes(bearer)),"length","short","party",new List<object?>{GraphJson.Object("charId","sera")})}),false));File.SetUnixFileMode(fixture,UnixFileMode.UserRead|UnixFileMode.UserWrite);
                var info=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"/Users/choi/.dotnet/dotnet"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,WorkingDirectory=directory};
                foreach(string arg in new[]{Path.Combine(AppContext.BaseDirectory,"MemoryForge.Server.dll"),"--MemoryForge:ContentRoot="+root,"--MemoryForge:PracticeFixture="+fixture,"--MemoryForge:PracticePort=0"})info.ArgumentList.Add(arg);
                if(postgresFile==null)info.ArgumentList.Add("--MemoryForge:PracticeStore="+directory);else{info.ArgumentList.Add("--MemoryForge:PracticeBackend=postgres");info.ArgumentList.Add("--MemoryForge:PracticePostgresFile="+postgresFile);}
                info.Environment["ASPNETCORE_ENVIRONMENT"]="Development";info.Environment["DOTNET_ENVIRONMENT"]="Development";process=Process.Start(info)??throw new Exception("start real HTTP host");string? url=null;
                for(int line=0;line<30;line++){string? text=await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));if(text==null)break;const string prefix="MemoryForge dev practice server listening on ";if(text.StartsWith(prefix,StringComparison.Ordinal)){url=text.Substring(prefix.Length).Split(' ')[0];break;}}
                if(url==null)throw new Exception("real HTTP host failed startup (private test configuration)");api=new HttpClient{BaseAddress=new Uri(url+"/dev/practice/"),Timeout=TimeSpan.FromSeconds(60)};api.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",bearer);
                using var profile=await api.GetAsync("progression");Check((int)profile.StatusCode==200,"real authenticated current v4 profile HTTP");
            }
            string emptyDirectory=Path.Combine(directory,"uninitialized");Directory.CreateDirectory(emptyDirectory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);var emptyPersistence=new FileEnvelopePersistence(emptyDirectory);var emptyStore=new PracticeRunStore(content,emptyPersistence);var emptyRequest=new GrowthRequest(Id(),0,"supportSelection",GraphJson.Object("charId",null));
            try{await emptyStore.Mutate(account,emptyRequest.RequestId,emptyRequest.Identity,(v,_)=>GrowthCommands.SelectSupport(content,account,v,emptyRequest));throw new Exception("uninitialized selection accepted");}catch(ApiError ex){Check(ex.Code=="TUTORIAL_REQUIRED","selection requires committed progression initialization");}Check(await emptyPersistence.ReadAsync(account)==null,"refused initialization creates no account envelope");
            // Parser refusals precede any store operation.
            Check(Contracts.ParseSupportSelection(GraphJson.Bytes(GraphJson.Object("requestId",Id(),"expectedRevision",0,"charId",null),false)).Args["charId"]==null,"explicit selection null parsed");
            RefusedParse(()=>Contracts.ParseSupportQuery(Encoding.UTF8.GetBytes("{\"expectedVersion\":0,\"caller\":\"nia\",\"mode\":\"next\"}")),"UNKNOWN_FIELD");
            RefusedParse(()=>Contracts.CheckArgs("support",GraphJson.Object("caller","nia","uid","fake")),"UNKNOWN_FIELD");
            await Denied(()=>Select("not_owned"),"NOT_OWNED");await Denied(()=>Select(null),"ALREADY_SELECTED");
            var e=await Read();
            if(e.Recruitment?.LaunchClaimed!=true)
            {string id=Id();var q=new LaunchClaimRequest(id,e.Revision,Contracts.LaunchIdentity(e.Revision));await store.Mutate(account,id,q.Identity,(x,_)=>RecruitmentCommands.ClaimLaunch(content.Recruitment,x,q));}
            (List<string> Party,string Support,List<(string Instance,string Char)> Equipment)? usable=null;
            for(int batch=0;batch<5;batch++)
            {
                e=await Read();usable=Assignment(e);if(usable!=null)break;
                string id=Id();var q=new PullRequest(id,e.Revision,"beginner",10,Contracts.PullIdentity(e.Revision,"beginner",10));
                try{await store.Mutate(account,id,q.Identity,(x,_)=>RecruitmentCommands.Pull(content.Recruitment,x,q));}catch(ApiError ex)when(ex.Code is "CRYSTALS_LOW" or "BEGINNER_LIMIT"){break;}
            }
            e=await Read();usable??=Assignment(e);
            if(usable==null)throw new Exception("This real random acquisition branch has no fourth owned companion with four usable instances; retained honestly, no grant/roll edited");
            var chosen=usable.Value;foreach(var pair in chosen.Equipment)
            {
                e=await Read();var current=GrowthCommands.BuildAt(content,e,long.MaxValue);var w=ProgressionCommands.Weapons(content,e,long.MaxValue).Single(w=>w.Instance==pair.Instance);
                if(current.EquippedBy(w)!=pair.Char)await Equip(pair.Instance,pair.Char);
            }
            string support=chosen.Support;var selected=await Select(support);using(var j=JsonDocument.Parse(selected.Body))Check(j.RootElement.GetProperty("kind").GetString()=="supportSelection"&&j.RootElement.GetProperty("cost").EnumerateObject().Count()==0,"selection v4 exact receipt cost0");
            await Denied(()=>Select(support),"ALREADY_SELECTED");
            if(api!=null){e=await Read();await Post("dungeons/sweep",GraphJson.Object("requestId",Id(),"expectedRevision",e.Revision,"dungeonId","dg_ink","tier",1,"times",1));}
            // Earn caller100 through real defensive plays on the authored weakest tier instead of testing stage60 survival;
            // no seed, gauge, stats or source envelope is changed, and every action still uses the actual backend/Core.
            string battleId=await Start(false,1);e=await Read();var rec=e.Growth!.Encounters.Single(r=>r.Id==battleId);Check(rec.PolicyVersion==SupportPolicy.Version&&rec.Support!.CharId==support&&rec.Support.WeaponInstanceId==chosen.Equipment.Single(p=>p.Char==support).Instance,"new dungeon launch freezes actual selected owned instance/build");
            Check(e.GrowthCache!.Battles[battleId].State.Actors.Count==3&&!e.GrowthCache.Battles[battleId].State.Actors.Any(a=>a.Id==support),"support adds no fourth deployed actor");
            int frozenLevel=Convert.ToInt32(rec.Support!.Build["level"]);string frozen=GraphJson.Digest(e.GrowthCache!.Battles[battleId].State.ToJsonGraph());
            await Level(support,frozenLevel+1);await Select(null);e=await Read();Check(GraphJson.Digest(e.GrowthCache!.Battles[battleId].State.ToJsonGraph())==frozen&&e.Growth!.Encounters.Single(r=>r.Id==battleId).Support!.ToGraph().Count==3,"selection and real purchased level do not alter active raw battle or frozen support");
            string caller=await Ready(battleId);e=await Read();rec=e.Growth!.Encounters.Single(r=>r.Id==battleId);var battle=e.GrowthCache!.Battles[battleId];var state=battle.State;string target=state.Enemies.First(x=>x.Alive).Uid;
            string before=GraphJson.Digest(state.ToJsonGraph());var query=new SupportQueryRequest(rec.Journal.Count,caller,target,new List<string>());var reply=api==null?GrowthCommands.SupportQuery(content,account,e,battleId,query):await Post("encounters/"+battleId+"/support-query",GraphJson.Object("expectedVersion",query.ExpectedVersion,"caller",caller,"target",target,"picks",new List<object?>()));
            Check(GraphJson.Digest(battle.State.ToJsonGraph())==before,"actual support query preserves full Battle/RNG/gauge/Used graph");
            using(var j=JsonDocument.Parse(reply.Body)){var source=j.RootElement.GetProperty("source");Check(source.GetProperty("kind").GetString()=="support"&&source.GetProperty("caller").GetString()==caller&&source.GetProperty("charId").GetString()==support&&source.GetProperty("ready").GetBoolean()&&source.GetProperty("target").GetString()==target&&!source.TryGetProperty("uid",out _),"flat typed query binds caller/frozen partner/request target without fake hand UID");}
            // Projection privacy unit over this real battle: deliberately nonzero hidden outcome must never escape the new query DTO.
            var hidden=battle.SupportForecast(caller,target);hidden.Hidden=true;hidden.EnemyHpLoss=987;hidden.EnemyDamage=987;hidden.NewlyBroken=3;hidden.BlockDelta=987;hidden.HpDelta=987;hidden.Defeat=true;
            var masked=PublicView.SupportQuery("encounterId",battleId,rec.Journal.Count,state.Phase,GraphJson.Object("turn",state.Turn,"battleVersion",state.V,"battlePhase",state.Phase),query,battle.SupportInfo(caller,target),battle.SupportChoices(caller),hidden,state,battle.ReservationSlots(),content.Rules);
            using(var m=JsonDocument.Parse(GraphJson.Text(masked,false))){var f=m.RootElement.GetProperty("forecast");Check(new[]{"enemyHpLoss","enemyDamage","newlyBroken","blockDelta","hpDelta","defeat"}.All(k=>f.GetProperty(k).ValueKind==JsonValueKind.Null)&&f.GetProperty("enemyHpLosses").GetArrayLength()==0,"all six hidden outcome fields are null and per-enemy splits empty");}
            Check(GraphJson.Digest(battle.State.ToJsonGraph())==before,"privacy projection unit never alters real battle state");
            if(battle.SupportNeedsTarget()!=null)await Denied(()=>Command(battleId,"support",GraphJson.Object("caller",caller,"target","not-an-enemy")),"BAD_TARGET");else await Denied(()=>Command(battleId,"support",GraphJson.Object("caller","not-an-actor","target",target)),"UNKNOWN_ACTOR");
            await Denied(()=>Command(battleId,"support",GraphJson.Object("caller",caller,"target",target,"picks",new List<object?>{"not-a-card"})),"PICK_INVALID");
            int version=rec.Journal.Count;int gauge=state.Actors.Single(a=>a.Id==caller).Gauge;string requestId=Id();var actionArgs=GraphJson.Object("caller",caller,"target",target);var action=new CommandRequest(requestId,version,"support",actionArgs,Contracts.EncounterIdentity(battleId,version,"support",actionArgs));
            var wire=GraphJson.Object("requestId",requestId,"expectedVersion",version,"kind","support","args",actionArgs);
            var result=api==null?await store.Mutate(account,requestId,action.Identity,(x,_)=>GrowthCommands.Command(content,account,x,battleId,action)):await Post("encounters/"+battleId+"/commands",wire);byte[] committed=(await persistence.ReadAsync(account))!;
            var retry=api==null?await store.Mutate(account,requestId,action.Identity,(_,_)=>throw new Exception("support retry reexecuted")):await Post("encounters/"+battleId+"/commands",wire);Check(result.Body==retry.Body&&Same(committed,(await persistence.ReadAsync(account))!),"lost support reply returns exact receipt without second use/effect");
            e=await Read();state=e.GrowthCache!.Battles[battleId].State;Check(state.Support!.Used&&state.Actors.Single(a=>a.Id==caller).Gauge==gauge-100,"actual Core spends caller100 and marks one Battle use");
            using(var j=JsonDocument.Parse(result.Body)){var ev=j.RootElement.GetProperty("events").EnumerateArray().Single(x=>x.GetProperty("e").GetString()=="support");Check(ev.GetProperty("actor").GetString()==support&&ev.GetProperty("caller").GetString()==caller,"public support event attributes supporter and distinct caller");}
            using(var j=JsonDocument.Parse(result.Body)){var summary=j.RootElement.GetProperty("state").GetProperty("support");Check(summary.GetProperty("used").GetBoolean()&&summary.GetProperty("charId").GetString()==support&&summary.EnumerateObject().Count()==3,"public support summary is exact used/charId/cost only");}
            if(state.Phase=="PLAYER")await Denied(()=>Command(battleId,"support",GraphJson.Object("caller",caller,"target",target)),"SUPPORT_ALREADY_USED");
            await Finish(battleId);string none=await Start(false);e=await Read();Check(e.GrowthCache!.Battles[none].State.Support==null,"null next selection produces genuinely support-absent new Battle");
            await Denied(()=>api==null?Task.FromResult(GrowthCommands.SupportQuery(content,account,e,none,new SupportQueryRequest(0,chosen.Party[0],null,new List<string>()))):Post("encounters/"+none+"/support-query",GraphJson.Object("expectedVersion",0,"caller",chosen.Party[0])),"SUPPORT_NOT_SELECTED");await Finish(none);
            await Select(support);await Story();string next=await Start(true);e=await Read();var nextRecord=e.Growth!.Encounters.Single(r=>r.Id==next);Check(Convert.ToInt32(nextRecord.Support!.Build["level"])==frozenLevel+1&&!e.GrowthCache!.Battles[next].State.Support!.Used,"next actual weekly Battle uses purchased support level and resets Used");
            var end=AccountStoreVerifier.Verify(content,account,(await persistence.ReadAsync(account))!,out _);Check(end.Revision>start.Revision,"full normal verifier reconstructs S3 prefix plus real S4 selection/recruitment/support history");
            Console.WriteLine("SupportChecks retained earned account "+directory+" support="+support+" party="+string.Join(',',chosen.Party));

            async Task<string> Start(bool weekly,int tier=1)
            {
                var x=await Read();var q=new GrowthRequest(Id(),x.Revision,weekly?"weeklyBossStart":"encounterStart",weekly?GraphJson.Object("bossId","b_bell","difficulty",1,"practice",true,"party",chosen.Party.Cast<object?>().ToList()):GraphJson.Object("dungeonId","dg_ink","tier",tier,"party",chosen.Party.Cast<object?>().ToList()));
                var r=api==null?await store.Mutate(account,q.RequestId,q.Identity,(v,live)=>weekly?GrowthCommands.StartWeekly(content,account,v,live,q):GrowthCommands.Start(content,account,v,live,q)):await Post(weekly?"weekly-bosses":"encounters",Body(q));using var j=JsonDocument.Parse(r.Body);string id=j.RootElement.GetProperty("encounterId").GetString()!;
                if(GrowthCommands.SelectedSupport(x)==null)Check(!j.RootElement.GetProperty("state").TryGetProperty("support",out _),"support-absent public battle omits summary entirely");else{var summary=j.RootElement.GetProperty("state").GetProperty("support");Check(summary.EnumerateObject().Count()==3&&summary.GetProperty("gaugeCost").GetInt32()==100&&!summary.GetProperty("used").GetBoolean(),"new actual public battle has narrow unused support summary");}
                if(api!=null){using var read=await api.GetAsync("encounters/"+id);Check((int)read.StatusCode==200,"real owned encounter GET HTTP");var state=await Read();var live=state.GrowthCache!.Battles[id].State;string uid=live.Zones.Hand[0];await Post("encounters/"+id+"/battle-query",GraphJson.Object("expectedVersion",0,"uid",uid,"mode","now","target",live.Enemies.First(a=>a.Alive).Uid));}return id;
            }
            async Task Story()
            {
                var x=await Read();var q=new ExpeditionRequest(Id(),x.Revision,"story",1,chosen.Party,true);
                var started=api==null?await store.Mutate(account,q.RequestId,q.Identity,(v,run)=>ProgressionCommands.Launch(content,account,v,run,q)):await Post("expeditions",GraphJson.Object("requestId",q.RequestId,"expectedRevision",q.ExpectedRevision,"mode","story","chapter",1,"party",chosen.Party.Cast<object?>().ToList(),"useMemory",true));
                using var j=JsonDocument.Parse(started.Body);string runId=j.RootElement.GetProperty("runId").GetString()!;x=await Read();Check(x.Run!.Launch!.PolicyVersion==SupportPolicy.Version&&x.Run.Launch.Support!.CharId==support&&Convert.ToInt32(x.Run.Launch.Support.Build["level"])==frozenLevel+1,"actual story launch freezes same owned v4 support build");
                for(int step=0;step<50;step++)
                {
                    var move=await store.Read(account,(v,run)=>
                    {
                        var s=run!.State;if(s.Phase=="BATTLE")return Reply.Json(200,GraphJson.Object("done",true));
                        string kind="proceed";var a=GraphJson.Object();
                        if(s.Phase=="MAP"){kind="enter";var nodes=s.Map!.Floors.SelectMany(row=>row).Concat(new[]{s.Map.Boss}).ToList();a["nodeId"]=run.Available().OrderBy(id=>nodes.First(n=>n.Id==id).Type is "normal" or "elite" or "boss"?0:1).First();}
                        else if(s.Phase=="EVENT"){kind="event";a["index"]=(long)(content.Data.Events.Last(ev=>ev.Id==(string)((IDictionary<string,object?>)s.Pending!)["id"]!).Choices.Count-1);}
                        return Reply.Json(200,GraphJson.Object("kind",kind,"args",a));
                    });
                    using var m=JsonDocument.Parse(move.Body);if(m.RootElement.TryGetProperty("done",out _))break;await RunCommand(m.RootElement.GetProperty("kind").GetString()!,(Dictionary<string,object?>)MemoryV2DataLoader.Graph(m.RootElement.GetProperty("args"))!);
                }
                string? runCaller=null,runTarget=null;
                for(int step=0;step<200;step++)
                {
                    var choice=await store.Read(account,(v,run)=>
                    {
                        var b=run!.State.Battle??throw new Exception("story no battle");var caller=b.Actors.FirstOrDefault(a=>a.Gauge>=100);string target=b.Enemies.First(e=>e.Alive).Uid;
                        if(caller!=null)return Reply.Json(200,GraphJson.Object("caller",caller.Id,"target",target));
                        var defensive=b.Zones.Hand.Where(uid=>run.BattleCheck(uid,"now",target)==null).Select(uid=>(Uid:uid,Forecast:run.BattleForecast(uid,"now",target)))
                            .Where(p=>!p.Forecast.Hidden&&p.Forecast.Error==null&&p.Forecast.EnemyHpLoss==0).OrderByDescending(p=>p.Forecast.BlockDelta+p.Forecast.HpDelta).ThenBy(p=>p.Uid,StringComparer.Ordinal).FirstOrDefault();
                        if(defensive.Uid!=null)return Reply.Json(200,GraphJson.Object("kind","play","args",GraphJson.Object("uid",defensive.Uid,"mode","now","target",target)));
                        return Reply.Json(200,GraphJson.Object("kind","end","args",GraphJson.Object()));
                    });
                    using var c=JsonDocument.Parse(choice.Body);if(c.RootElement.TryGetProperty("caller",out var caller)){runCaller=caller.GetString();runTarget=c.RootElement.GetProperty("target").GetString();break;}
                    await RunCommand(c.RootElement.GetProperty("kind").GetString()!,(Dictionary<string,object?>)MemoryV2DataLoader.Graph(c.RootElement.GetProperty("args"))!);
                }
                Check(runCaller!=null,"story caller100 earned by actual non-damaging card plays");x=await Read();int version=x.Run!.Journal.Count;byte[] bytes=(await persistence.ReadAsync(account))!;
                if(api!=null)
                {
                    var queried=await Post("runs/"+runId+"/support-query",GraphJson.Object("expectedVersion",version,"caller",runCaller,"target",runTarget,"picks",new List<object?>()));
                    using var query=JsonDocument.Parse(queried.Body);Check(query.RootElement.GetProperty("phase").GetString()=="BATTLE"&&query.RootElement.GetProperty("context").TryGetProperty("node",out _)&&query.RootElement.GetProperty("source").GetProperty("charId").GetString()==support,"actual Run support-query HTTP has Run context and frozen supporter");
                    Check(Same(bytes,(await persistence.ReadAsync(account))!),"Run support-query HTTP leaves complete store byte-identical");
                }
                var result=await RunCommand("support",GraphJson.Object("caller",runCaller,"target",runTarget));using(var resultJson=JsonDocument.Parse(result.Body))Check(resultJson.RootElement.GetProperty("events").EnumerateArray().Any(ev=>ev.GetProperty("e").GetString()=="support"&&ev.GetProperty("caller").GetString()==runCaller),"actual Run support command forwards real Core event/caller");
                x=await Read();var abandon=new AbandonRequest(Id(),x.Run!.Journal.Count,Contracts.AbandonIdentity(runId,x.Run.Journal.Count));
                if(api==null)await store.Mutate(account,abandon.RequestId,abandon.Identity,(v,live)=>ProgressionCommands.Abandon(v,live,runId,abandon));else await Post("runs/"+runId+"/abandon",GraphJson.Object("requestId",abandon.RequestId,"expectedVersion",abandon.ExpectedVersion));
                async Task<Reply> RunCommand(string kind,Dictionary<string,object?> a)
                {
                    var current=await Read();int v=current.Run!.Journal.Count;string id=Id();var parsed=Contracts.ParseCommand(GraphJson.Bytes(GraphJson.Object("requestId",id,"expectedVersion",v,"kind",kind,"args",a),false),runId);
                    return api==null?await S1Journey.Command(store,account,content,id,parsed.Identity,kind,parsed.Args):await Post("runs/"+runId+"/commands",GraphJson.Object("requestId",id,"expectedVersion",v,"kind",kind,"args",a));
                }
            }
        }
        finally{GrowthCommands.Clock=clock;api?.Dispose();if(process!=null){if(!process.HasExited)process.Kill(entireProcessTree:true);await process.WaitForExitAsync();process.Dispose();}if(postgres!=null)await postgres.DisposeAsync();Console.WriteLine("SupportChecks isolated proof store "+directory+" backend="+(postgresFile==null?"file":"postgres"));}

        Task<AccountEnvelope> Read()=>ReadActual();async Task<AccountEnvelope> ReadActual()=>Envelopes.Decode((await persistence.ReadAsync(account))!,account,content);
        async Task<Reply> Post(string path,object body){using var result=await api!.PostAsync(path,new StringContent(GraphJson.Text(body,false),Encoding.UTF8,"application/json"));string text=await result.Content.ReadAsStringAsync();if((int)result.StatusCode!=200){using var j=JsonDocument.Parse(text);throw new ApiError((int)result.StatusCode,j.RootElement.GetProperty("code").GetString()!);}return new Reply(200,text);}
        Dictionary<string,object?> Body(GrowthRequest q)=>GraphJson.Object("requestId",q.RequestId,"expectedRevision",q.ExpectedRevision).Concat(q.Args).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal);
        async Task<Reply> Select(string? cid){var x=await Read();var q=Contracts.ParseSupportSelection(GraphJson.Bytes(GraphJson.Object("requestId",Id(),"expectedRevision",x.Revision,"charId",cid),false));return api==null?await store.Mutate(account,q.RequestId,q.Identity,(v,_)=>GrowthCommands.SelectSupport(content,account,v,q)):await Post("progression/support",Body(q));}
        async Task Equip(string instance,string cid){var x=await Read();var q=new GrowthRequest(Id(),x.Revision,"weaponEquip",GraphJson.Object("instanceId",instance,"charId",cid));if(api==null)await store.Mutate(account,q.RequestId,q.Identity,(v,live)=>GrowthCommands.Mutate(content,account,v,live,q));else await Post("progression/weapons/"+instance+"/equip",GraphJson.Object("requestId",q.RequestId,"expectedRevision",q.ExpectedRevision,"charId",cid));}
        async Task Level(string cid,int level){var x=await Read();var q=new GrowthRequest(Id(),x.Revision,"characterLevelUp",GraphJson.Object("charId",cid,"targetLevel",level));if(api==null)await store.Mutate(account,q.RequestId,q.Identity,(v,live)=>GrowthCommands.Mutate(content,account,v,live,q));else{byte[] before=(await persistence.ReadAsync(account))!;await Post("progression/characters/"+cid+"/level-preview",GraphJson.Object("expectedRevision",x.Revision,"targetLevel",level));Check(Same(before,(await persistence.ReadAsync(account))!),"real level preview HTTP remains byte-pure");await Post("progression/characters/"+cid+"/level-up",GraphJson.Object("requestId",q.RequestId,"expectedRevision",q.ExpectedRevision,"targetLevel",level));}}
        async Task<Reply> Command(string id,string kind,Dictionary<string,object?> a)
        {var x=await Read();int v=x.Growth!.Encounters.Single(r=>r.Id==id).Journal.Count;var q=new CommandRequest(Id(),v,kind,a,Contracts.EncounterIdentity(id,v,kind,a));return api==null?await store.Mutate(account,q.RequestId,q.Identity,(state,_)=>GrowthCommands.Command(content,account,state,id,q)):await Post("encounters/"+id+"/commands",GraphJson.Object("requestId",q.RequestId,"expectedVersion",v,"kind",kind,"args",a));}
        async Task Denied(Func<Task> act,string code)
        {byte[] before=(await persistence.ReadAsync(account))!;try{await act();throw new Exception("accepted "+code);}catch(ApiError x){Check(x.Code==code,"expected "+code+" got "+x.Code);}catch(RuleException x){Check(x.Code==code,"Core expected "+code+" got "+x.Code);}Check(Same(before,(await persistence.ReadAsync(account))!),"refused "+code+" preserves exact envelope bytes");}
        async Task<string> Ready(string id)
        {for(int step=0;step<200;step++){var x=await Read();var b=x.GrowthCache!.Battles[id].State;if(b.Phase!="PLAYER")throw new Exception("real battle ended before naturally earned support gauge");var a=b.Actors.FirstOrDefault(a=>a.Gauge>=100);if(a!=null)return a.Id;await Bot(id,true);}throw new Exception("natural gauge bound");}
        async Task Finish(string id){for(int step=0;step<1000;step++){var x=await Read();if(x.GrowthCache!.Battles[id].State.Phase!="PLAYER")return;await Bot(id,false);}throw new Exception("real battle completion bound");}
        async Task Bot(string id,bool conserve)
        {
            var x=await Read();var battle=x.GrowthCache!.Battles[id];var b=battle.State;string target=b.Enemies.Where(t=>t.Alive).OrderByDescending(t=>t.Hp).First().Uid;
            var legal=b.Zones.Hand.Where(uid=>battle.Check(uid,"now",target)==null).Select(uid=>(Uid:uid,Forecast:battle.Forecast(uid,"now",target))).ToList();
            // Hidden forecasts contain zero placeholders; they are not proof that a real card deals no damage.
            if(conserve)legal=legal.Where(p=>!p.Forecast.Hidden&&p.Forecast.Error==null&&p.Forecast.EnemyHpLoss==0).ToList();
            if(!conserve){var ult=b.Actors.FirstOrDefault(a=>a.Gauge>=100);if(ult!=null){await Command(id,"ult",GraphJson.Object("actor",ult.Id,"target",target));return;}}
            if(legal.Count==0){await Command(id,"end",GraphJson.Object());return;}var chosen=conserve?legal.OrderByDescending(p=>p.Forecast.BlockDelta+p.Forecast.HpDelta).ThenBy(p=>p.Uid,StringComparer.Ordinal).First():legal.OrderByDescending(p=>p.Forecast.EnemyHpLoss).First();await Command(id,"play",GraphJson.Object("uid",chosen.Uid,"mode","now","target",target));
        }
        (List<string>,string,List<(string,string)>)? Assignment(AccountEnvelope x)
        {
            var chars=ProgressionCommands.Copies(x,false,long.MaxValue).Keys.ToList();var weapons=ProgressionCommands.Weapons(content,x,long.MaxValue);if(chars.Count<4)return null;
            foreach(string fourth in chars.Where(c=>!new[]{"nia","harin","rien"}.Contains(c)))
            {
                var four=new List<string>{"nia","harin","rien",fourth};var used=new HashSet<string>();var assigned=new List<(string,string)>();
                foreach(string cid in four)
                {
                    string type=content.Data.Characters.Single(c=>c.Id==cid).WeaponType;var w=weapons.FirstOrDefault(w=>!used.Contains(w.Instance)&&content.Data.Memory.Battle.Weapons.Single(a=>a.Id==w.Weapon).Type==type);
                    if(w.Instance==null){assigned.Clear();break;}used.Add(w.Instance);assigned.Add((w.Instance,cid));
                }
                if(assigned.Count==4)return(four.Take(3).ToList(),fourth,assigned);
            }
            return null;
        }
    }
    static void RefusedParse(Action act,string code){try{act();throw new Exception("parse accepted");}catch(ApiError e){Check(e.Code==code,"strict support parser fixed code");}}
}
