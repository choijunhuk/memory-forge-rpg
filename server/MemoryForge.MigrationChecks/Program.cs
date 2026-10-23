using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MemoryForge.Server;
[assembly:System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
static class MigrationChecks
{
    static int checks;
    static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    static void Refused(Action action,string code){try{action();throw new Exception("accepted "+code);}catch(MigrationRefusal e){Check(e.Code==code,"expected "+code+" got "+e.Code);}}
    static bool ConcurrentLoser(Exception error)=>error is MigrationRefusal{Code:"MIGRATION_CAS_CHANGED"} or ApiError{Status:503,Code:"ACCOUNT_BUSY"};
    const string Pass="verify-store account id=chosen status=PASS revision=2 receipts=2 runReceipts=1 recruitmentReceipts=0 growthReceipts=1 runs=1 archived=1 current=0 checkpoints=1 memoryVersion=1\n";
    const string Summary="verify-store result status=PASS accounts=1 verified=1 absent=0 failed=0 held=0\n";
    static string Diagnostics(bool hold=false)=>string.Concat(new[]{"nia","harin","rien"}.Select(c=>"verify-store capacity id=chosen char="+c+" used="+(hold&&c=="nia"?7:0)+" tutorialCapacity=6 tutorial="+(hold&&c=="nia"?"OVER_CAPACITY":"OK")+" copies=1 res=0 asc=2 ownedCapacity=7 owned=OK\n"))+"verify-store probe id=chosen party=nia,harin,rien useMemoryTrue="+(hold?"OVER_CAPACITY":"OK")+" useMemoryFalse="+(hold?"OVER_CAPACITY":"OK")+"\n";
    static async Task<int> Main(string[] args)
    {
        try
        {
            string? postgresFile=null,postgresCliFile=null,outputDirectory=null;bool cli=false,requireRegistered=false,ownerFileCli=false,ownerPgCli=false;string cliTarget="NOT_RUN";
            for(int i=4;i<args.Length;i++){if(args[i]=="--postgres-file"&&i+1<args.Length)postgresFile=Path.GetFullPath(args[++i]);else if(args[i]=="--postgres-cli-file"&&i+1<args.Length)postgresCliFile=Path.GetFullPath(args[++i]);else if(args[i]=="--out"&&i+1<args.Length)outputDirectory=Path.GetFullPath(args[++i]);else if(args[i]=="--cli")cli=true;else if(args[i]=="--require-registered-target")requireRegistered=true;else throw new Exception("migration check arguments");}
            if((requireRegistered||postgresCliFile!=null)&&!cli)throw new Exception("CLI flags require --cli");
            if(args.Length is >0 and <4)throw new Exception("MigrationChecks ROOT OLD_RUNTIME FIXTURE OLD_SOURCE [--postgres-file FILE] [--out DIR] [--cli]");
            Check(ConcurrentLoser(new MigrationRefusal("MIGRATION_CAS_CHANGED"))&&ConcurrentLoser(new ApiError(503,"ACCOUNT_BUSY")),"only known source-CAS or lock-timeout loser accepted");
            Check(!ConcurrentLoser(new ApiError(500,"ACCOUNT_BUSY"))&&!ConcurrentLoser(new ApiError(503,"STORE_UNAVAILABLE"))&&!ConcurrentLoser(new MigrationRefusal("MIGRATION_RECOVERY_CHANGED"))&&!ConcurrentLoser(new TimeoutException()),"wrong status, storage failure and unknown migration outcomes remain failures");
            var p=OwnerMigrationProof.Parse(0,Pass+Diagnostics()+Summary,"source","chosen","directory");Check(p.Alias=="chosen"&&!p.CapacityHold&&p.Counts["receipts"]==2,"explicit selected history PASS proof");
            Refused(()=>OwnerMigrationProof.Parse(0,"verify-store account id=chosen status=ABSENT\n"+Summary,"source","chosen","directory"),"MIGRATION_OLD_PROOF");
            Refused(()=>OwnerMigrationProof.Parse(0,Pass.Replace("id=chosen","id=other",StringComparison.Ordinal)+Summary,"source","chosen","directory"),"MIGRATION_OLD_PROOF");
            Refused(()=>OwnerMigrationProof.Parse(0,Pass+Pass+Summary,"source","chosen","directory"),"MIGRATION_OLD_PROOF");
            Refused(()=>OwnerMigrationProof.Parse(1,Pass+Summary,"source","chosen","directory"),"MIGRATION_OLD_PROOF");
            Refused(()=>OwnerMigrationProof.Parse(4,Pass+Summary,"source","chosen","directory"),"MIGRATION_OLD_PROOF");
            var held=OwnerMigrationProof.Parse(4,Pass+Diagnostics(true)+Summary.Replace("status=PASS","status=HOLD",StringComparison.Ordinal).Replace("held=0","held=1",StringComparison.Ordinal),"source","chosen","directory");Check(held.CapacityHold,"separate capacity HOLD does not erase explicit history PASS");
            Refused(()=>OwnerMigrationProof.Parse(4,Summary.Replace("status=PASS","status=HOLD",StringComparison.Ordinal).Replace("held=0","held=1",StringComparison.Ordinal),"source","chosen","directory"),"MIGRATION_OLD_PROOF");
            Refused(()=>OwnerMigrationProof.Parse(4,Pass+"verify-store capacity id=chosen char=nia status=FAIL code=VERIFY_INTERNAL\nverify-store probe id=chosen party=nia,harin,rien useMemoryTrue=VERIFY_INTERNAL useMemoryFalse=VERIFY_INTERNAL\n"+Summary.Replace("status=PASS","status=HOLD",StringComparison.Ordinal).Replace("held=0","held=1",StringComparison.Ordinal),"source","chosen","directory"),"MIGRATION_OLD_PROOF");
            string old=OwnerMigrationRegistry.Sources[1].CoreModule,target=new string('a',32);
            var map=new MigrationMap(OwnerMigrationRegistry.Sources[1],target,new string('b',64));
            string text="{\"accountId\":\"chosen\",\"binding\":{\"replay\":\"mf-dev-practice-replay-1\",\"coreModule\":\""+old+"\"},\"run\":{\"launch\":{\"coreModule\":\""+old+"\"}},\"archive\":[{\"launch\":{\"coreModule\":\""+old+"\"}}],\"growth\":{\"encounters\":[{\"coreModule\":\""+old+"\"}]},\"receipts\":{\"id\":{\"body\":\"old literal "+old+"\"}},\"number\":1.00}";
            byte[] Wrapped(string body)=>GraphJson.Bytes(GraphJson.Object("schema",Envelopes.Schema,"sha256",GraphJson.Sha256(Encoding.UTF8.GetBytes(body)),"body",body),false);
            byte[] candidate=OwnerMigrationBindings.Candidate(Wrapped(text),"chosen",map);
            using(var parsed=JsonDocument.Parse(candidate))
            {
                string changed=parsed.RootElement.GetProperty("body").GetString()!;string expected=text.Replace("\"coreModule\":\""+old+"\"","\"coreModule\":\""+target+"\"",StringComparison.Ordinal).Replace("\"replay\":\"mf-dev-practice-replay-1\"","\"replay\":\"mf-dev-practice-replay-2\"",StringComparison.Ordinal);
                Check(changed==expected,"exact known string spans only, numeric spelling and embedded receipt BODY bytes unchanged");
                Check(parsed.RootElement.GetProperty("sha256").GetString()==GraphJson.Sha256(Encoding.UTF8.GetBytes(changed)),"outer checksum follows exact candidate body");
            }
            Refused(()=>OwnerMigrationBindings.Candidate(Wrapped(text),"other",map),"MIGRATION_ACCOUNT_MISMATCH");
            Refused(()=>OwnerMigrationBindings.Candidate(Wrapped(text.Replace("\"number\":1.00","\"surprise\":{\"coreModule\":\""+old+"\"}",StringComparison.Ordinal)),"chosen",map),"MIGRATION_BINDING_PATH");
            Refused(()=>OwnerMigrationBindings.Candidate(Wrapped(text.Replace("\"number\":1.00","\"number\":1,\"number\":2",StringComparison.Ordinal)),"chosen",map),"MIGRATION_SCHEMA");
            Refused(()=>OwnerMigrationBindings.Candidate(Wrapped(text.Replace("\"replay\":\"mf-dev-practice-replay-1\"","\"replay\":\"mf-dev-practice-replay-9\"",StringComparison.Ordinal)),"chosen",map),"MIGRATION_SOURCE_MISMATCH");
            if(args.Length>=4)
            {
                string root=Path.GetFullPath(args[0]),runtime=Path.GetFullPath(args[1]),fixture=Path.GetFullPath(args[2]),envelope=Path.GetFullPath(args[3]);
                byte[] source=File.ReadAllBytes(envelope);using var outer=JsonDocument.Parse(source);using var body=JsonDocument.Parse(outer.RootElement.GetProperty("body").GetString()!);string alias=body.RootElement.GetProperty("accountId").GetString()!;
                if(alias!="growth-check")throw new Exception("migration checks accept synthetic growth-check fixture only");
                string module=OwnerMigrationBindings.Module(source,alias,out string replay);var known=OwnerMigrationRegistry.Sources.Single(s=>s.CoreModule==module);Check(replay==OwnerMigrationRegistry.SourceReplay,"actual source old replay1 remains pinned");
                string backup=Path.Combine(Path.GetTempPath(),"mf-migration-proof-check-"+Guid.NewGuid().ToString("N"));string directory=OwnerMigrationProof.Prepare(backup,root);
                var actual=await OwnerMigrationProof.Prove(source,alias,known,runtime,fixture,root,directory);
                Check(actual.SourceSha256==GraphJson.Sha256(source)&&actual.Counts["revision"]==body.RootElement.GetProperty("revision").GetInt64()&&actual.Counts["growthReceipts"]==993,"real oldS3 process proves exact earned source bytes and all typed record families");
                Check(File.ReadAllBytes(envelope).AsSpan().SequenceEqual(source),"old proof never writes original source");
                string badRuntime=Path.Combine(backup,"tampered-runtime");Directory.CreateDirectory(badRuntime,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
                foreach(string name in known.RuntimeFiles.Keys){File.Copy(Path.Combine(runtime,name),Path.Combine(badRuntime,name));File.SetUnixFileMode(Path.Combine(badRuntime,name),UnixFileMode.UserRead|UnixFileMode.UserWrite);}
                string badCore=Path.Combine(badRuntime,"MemoryForge.Core.dll");byte[] correctCore=File.ReadAllBytes(badCore),badBytes=(byte[])correctCore.Clone();badBytes[0]^=1;File.WriteAllBytes(badCore,badBytes);
                try{await OwnerMigrationProof.Prove(source,alias,known,badRuntime,fixture,root,OwnerMigrationProof.Prepare(backup,root));throw new Exception("untrusted old DLL executed");}catch(MigrationRefusal ex){Check(ex.Code=="MIGRATION_RUNTIME_HASH","tampered old runtime refused before invocation");}
                File.WriteAllBytes(badCore,correctCore);File.SetUnixFileMode(badCore,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead);
                try{await OwnerMigrationProof.Prove(source,alias,known,badRuntime,fixture,root,OwnerMigrationProof.Prepare(backup,root));throw new Exception("unsafe runtime mode accepted");}catch(MigrationRefusal ex){Check(ex.Code=="MIGRATION_PRIVATE_PATH","unsafe old runtime mode refused before invocation");}
                var content=PracticeContent.Load(root);var account=PracticeFixture.Load(fixture,content).Accounts.Single(a=>a.Id==alias);
                if(requireRegistered)_=OwnerMigrationRegistry.Require(content,module);
                var actualMap=new MigrationMap(known,content.CoreModule,GraphJson.Sha256(File.ReadAllBytes(typeof(MemoryForge.Core.Run).Assembly.Location)));
                byte[] actualCandidate=OwnerMigrationBindings.Candidate(source,alias,actualMap);
                var verified=AccountStoreVerifier.Verify(content,account,actualCandidate,out var decoded);OwnerMigrationProof.Match(actual,verified);Check(verified.Revision==actual.Counts["revision"]&&decoded.Growth!.Ledger.Count==994,"actual newCore normal Decode and full verifier preserve complete oldS3 witnesses");
                using(var oldBody=JsonDocument.Parse(outer.RootElement.GetProperty("body").GetString()!))using(var candidateOuter=JsonDocument.Parse(actualCandidate))using(var newBody=JsonDocument.Parse(candidateOuter.RootElement.GetProperty("body").GetString()!))
                    foreach(var receipt in oldBody.RootElement.GetProperty("receipts").EnumerateObject())Check(newBody.RootElement.GetProperty("receipts").GetProperty(receipt.Name).GetRawText()==receipt.Value.GetRawText(),"actual migration keeps every receipt full JSON bytes exact");
                OwnerMigrationProof.Write(Path.Combine(directory,"candidate.envelope.json"),actualCandidate);
                var storeDirectory=Path.Combine(backup,"installed");Directory.CreateDirectory(storeDirectory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);var persistence=new FileEnvelopePersistence(storeDirectory);
                await persistence.TransactAsync(account,source,_=>new Transition(Reply.Json(200,null),source));
                try{await OwnerMigration.CommitAsync(new BusyBeforeWrite(persistence),content,account,source,actualCandidate,actual);throw new Exception("busy migration accepted");}
                catch(ApiError ex)when(ConcurrentLoser(ex)){Check((await persistence.ReadAsync(account))!.AsSpan().SequenceEqual(source),"known lock-timeout loser leaves complete source bytes unchanged");}
                await OwnerMigration.CommitAsync(new AfterCommitFailure(persistence),content,account,source,actualCandidate,actual);
                Check((await persistence.ReadAsync(account))!.AsSpan().SequenceEqual(actualCandidate),"uncertain successful durable file commit resolved as exact target under permanent lock");
                try{await OwnerMigration.CommitAsync(persistence,content,account,source,actualCandidate,actual);throw new Exception("stale source overwrite");}catch(MigrationRefusal e){Check(e.Code=="MIGRATION_CAS_CHANGED","competing/stale migration source CAS refuses");}
                string saved=Path.Combine(directory,"source.envelope.json");File.SetUnixFileMode(saved,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead);
                try{await OwnerMigration.CommitAsync(persistence,content,account,source,actualCandidate,actual);throw new Exception("unsafe backup accepted");}catch(MigrationRefusal e){Check(e.Code=="MIGRATION_PRIVATE_PATH","wrong backup mode refused before write");}finally{File.SetUnixFileMode(saved,UnixFileMode.UserRead|UnixFileMode.UserWrite);}
                byte[] prior=File.ReadAllBytes(saved);File.WriteAllBytes(saved,prior.Concat(new byte[]{32}).ToArray());
                try{await OwnerMigration.CommitAsync(persistence,content,account,source,actualCandidate,actual);throw new Exception("changed backup accepted");}catch(MigrationRefusal e){Check(e.Code=="MIGRATION_BACKUP_HASH","wrong backup hash refused before write");}finally{File.WriteAllBytes(saved,prior);}
                Check((await persistence.ReadAsync(account))!.AsSpan().SequenceEqual(actualCandidate),"failed CAS/backup cases preserve exact proven target");
                if(postgresFile!=null)
                {
                    await using var pg=await PostgresEnvelopePersistence.OpenAsync(PostgresSettings.ConnectionString(postgresFile,root));
                    if(await pg.ReadAsync(account)!=null)throw new Exception("isolated PG migration alias already exists; no overwrite");
                    await pg.TransactAsync(account,source,current=>{if(current!=null)throw new Exception("test PG alias raced");return new Transition(Reply.Json(200,null),source);});
                    var tasks=Enumerable.Range(0,2).Select(_=>Task.Run(async()=>
                    {
                        try{await OwnerMigration.CommitAsync(new AfterCommitFailure(pg),PracticeContent.Load(root),account,source,actualCandidate,actual);return true;}
                        catch(Exception ex)when(ConcurrentLoser(ex)){return false;}
                    })).ToArray();var outcomes=await Task.WhenAll(tasks);
                    Check(outcomes.Count(v=>v)==1,"real PG concurrent sourceCAS allows exactly one migration; uncertain winner resolves target");
                    Check((await pg.ReadAsync(account))!.AsSpan().SequenceEqual(actualCandidate),"real PG exact target bytes after commit/uncertain recovery");
                    var pgVerified=AccountStoreVerifier.Verify(content,account,(await pg.ReadAsync(account))!,out _);OwnerMigrationProof.Match(actual,pgVerified);Check(pgVerified.Revision==actual.Counts["revision"],"real PG final candidate passes complete normal verifier after either known loser outcome");
                    try{await OwnerMigration.CommitAsync(pg,content,account,source,actualCandidate,actual);throw new Exception("PG stale overwrite");}catch(MigrationRefusal ex){Check(ex.Code=="MIGRATION_CAS_CHANGED","real PG repeated stale source cannot overwrite target");}
                }
                if(cli)
                {
                    string cliStore=Path.Combine(backup,"cli-store");Directory.CreateDirectory(cliStore,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);string file=Path.Combine(cliStore,"account-"+alias+".json");File.WriteAllBytes(file,source);File.SetUnixFileMode(file,UnixFileMode.UserRead|UnixFileMode.UserWrite);
                    var dry=await Cli(false);bool registered=OwnerMigrationRegistry.TargetModule==content.CoreModule;
                    cliTarget=registered?"KNOWN":"REFUSED_UNKNOWN";
                    if(!registered){Check(dry.Exit==2&&dry.Text.Contains("MIGRATION_TARGET_UNKNOWN",StringComparison.Ordinal)&&File.ReadAllBytes(file).AsSpan().SequenceEqual(source),"fresh unregistered Core CLI refuses before write");}
                    else
                    {
                        Check(dry.Exit==0&&dry.Text.Contains("status=DRY_RUN_PASS",StringComparison.Ordinal)&&File.ReadAllBytes(file).AsSpan().SequenceEqual(source),"actual owner CLI dry-run completes old/new proofs without store rewrite");
                        var write=await Cli(true);Check(write.Exit==0&&write.Text.Contains("status=MIGRATED",StringComparison.Ordinal)&&File.ReadAllBytes(file).AsSpan().SequenceEqual(actualCandidate),"actual owner CLI single atomic file write installs exact proven candidate");
                        DateTime stamp=File.GetLastWriteTimeUtc(file);var repeated=await Cli(true);Check(repeated.Exit==0&&repeated.Text.Contains("status=ALREADY_MIGRATED",StringComparison.Ordinal)&&File.GetLastWriteTimeUtc(file)==stamp,"actual owner CLI already-target verifies without rewrite");
                        ownerFileCli=true;
                    }
                    if(postgresCliFile!=null)
                    {
                        await using var pgCli=await PostgresEnvelopePersistence.OpenAsync(PostgresSettings.ConnectionString(postgresCliFile,root));
                        if(await pgCli.ReadAsync(account)!=null)throw new Exception("isolated PG CLI alias already exists; no overwrite");await pgCli.TransactAsync(account,source,current=>{if(current!=null)throw new Exception("PG CLI alias raced");return new Transition(Reply.Json(200,null),source);});
                        var pgDry=await Cli(false,postgresCliFile);
                        if(!registered)Check(pgDry.Exit==2&&pgDry.Text.Contains("MIGRATION_TARGET_UNKNOWN",StringComparison.Ordinal)&&(await pgCli.ReadAsync(account))!.AsSpan().SequenceEqual(source),"fresh unknown Core PG CLI refuses without rewrite");
                        else
                        {
                            Check(pgDry.Exit==0&&pgDry.Text.Contains("status=DRY_RUN_PASS",StringComparison.Ordinal)&&(await pgCli.ReadAsync(account))!.AsSpan().SequenceEqual(source),"real owner PG CLI dry-run complete proof no write");
                            var pgWrite=await Cli(true,postgresCliFile);Check(pgWrite.Exit==0&&pgWrite.Text.Contains("status=MIGRATED",StringComparison.Ordinal)&&(await pgCli.ReadAsync(account))!.AsSpan().SequenceEqual(actualCandidate),"real owner PG CLI atomically installs exact proven target");
                            var pgRepeat=await Cli(true,postgresCliFile);Check(pgRepeat.Exit==0&&pgRepeat.Text.Contains("status=ALREADY_MIGRATED",StringComparison.Ordinal)&&(await pgCli.ReadAsync(account))!.AsSpan().SequenceEqual(actualCandidate),"real owner PG CLI repeat normal proof returns already-target");
                            ownerPgCli=true;
                        }
                    }
                    async Task<(int Exit,string Text)> Cli(bool write,string? pgFile=null)
                    {
                        var process=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??Environment.ProcessPath!){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
                        foreach(string a in new[]{Path.Combine(AppContext.BaseDirectory,"MemoryForge.Server.dll"),OwnerMigration.Flag,alias,"--old-runtime",runtime,"--backup-directory",Path.Combine(backup,"cli-backups"),"--MemoryForge:ContentRoot="+root,"--MemoryForge:PracticeFixture="+fixture,"--MemoryForge:PracticePort=0"})process.ArgumentList.Add(a);
                        if(pgFile==null)process.ArgumentList.Add("--MemoryForge:PracticeStore="+cliStore);else{process.ArgumentList.Add("--MemoryForge:PracticeBackend=postgres");process.ArgumentList.Add("--MemoryForge:PracticePostgresFile="+pgFile);}
                        if(write)process.ArgumentList.Add("--write");process.Environment["DOTNET_ENVIRONMENT"]="Development";process.Environment["ASPNETCORE_ENVIRONMENT"]="Development";
                        using var p=Process.Start(process)??throw new Exception("spawn actual migration CLI");var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();return(p.ExitCode,await stdout+await stderr);
                    }
                }
                if(outputDirectory!=null)
                {
                    Directory.CreateDirectory(outputDirectory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);OwnerMigrationProof.Private(outputDirectory,true);OwnerMigrationProof.Write(Path.Combine(outputDirectory,"candidate.envelope.json"),actualCandidate);
                    OwnerMigrationProof.Write(Path.Combine(outputDirectory,"result.json"),GraphJson.Bytes(GraphJson.Object("sourceSha256",actual.SourceSha256,"candidateSha256",GraphJson.Sha256(actualCandidate),"oldCoreModule",module,"newCoreModule",content.CoreModule,"proofDirectory",directory,"postgres",postgresFile!=null,"postgresCli",postgresCliFile!=null,"cli",cli,"cliTarget",cliTarget,"ownerFileCli",ownerFileCli,"ownerPgCli",ownerPgCli,"checks",checks),false));
                }
                Console.WriteLine("MigrationChecks current target core "+content.CoreModule+" (test proof only; production registry remains Root-measured gate)");
                Console.WriteLine("MigrationChecks retained owner-only real old proof "+directory);
            }
            Console.WriteLine("MigrationChecks: "+checks+" passed;scope="+(args.Length>=4?"full-old-new-proof,file"+(postgresFile!=null?",postgres":"")+(cli?",cli":""):"boundary-vectors")+";cliTarget="+cliTarget+";ownerFileCli="+(ownerFileCli?"MIGRATED":"NOT_RUN")+";ownerPgCli="+(ownerPgCli?"MIGRATED":"NOT_RUN"));return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    sealed class BusyBeforeWrite:IEnvelopePersistence
    {
        readonly IEnvelopePersistence real;public BusyBeforeWrite(IEnvelopePersistence real){this.real=real;}
        public Task<byte[]?> ReadAsync(PracticeAccount account)=>real.ReadAsync(account);
        public Task<Reply> TransactAsync(PracticeAccount account,byte[] fresh,Func<byte[]?,Transition> decide)=>throw new ApiError(503,"ACCOUNT_BUSY");
    }
    sealed class AfterCommitFailure:IEnvelopePersistence
    {
        readonly IEnvelopePersistence real;bool first=true;public AfterCommitFailure(IEnvelopePersistence real){this.real=real;}
        public Task<byte[]?> ReadAsync(PracticeAccount account)=>real.ReadAsync(account);
        public async Task<Reply> TransactAsync(PracticeAccount account,byte[] fresh,Func<byte[]?,Transition> decide)
        {var reply=await real.TransactAsync(account,fresh,decide);if(first){first=false;throw new ApiError(503,"STORE_COMMIT_UNKNOWN");}return reply;}
    }
}
