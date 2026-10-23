using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MemoryForge.Server
{
    sealed record MigrationProof(string SourceSha256,string Alias,string Directory,bool CapacityHold,Dictionary<string,long> Counts,string? AttestationSha256=null);
    static class OwnerMigrationProof
    {
        const UnixFileMode PrivateFile=UnixFileMode.UserRead|UnixFileMode.UserWrite;
        const UnixFileMode PrivateDirectory=PrivateFile|UnixFileMode.UserExecute;
        public static void Private(string path,bool directory)
        {
            FileSystemInfo info=directory?new DirectoryInfo(path):new FileInfo(path);
            if(!info.Exists||info.LinkTarget!=null||File.GetUnixFileMode(path)!=(directory?PrivateDirectory:PrivateFile))throw new MigrationRefusal("MIGRATION_PRIVATE_PATH");
        }
        public static string Prepare(string parent,string contentRoot)
        {
            if(!Path.IsPathFullyQualified(parent)||PracticeRunStore.IsInside(parent,contentRoot))throw new MigrationRefusal("MIGRATION_BACKUP_PATH");
            if(!System.IO.Directory.Exists(parent))System.IO.Directory.CreateDirectory(parent,PrivateDirectory);Private(parent,true);
            string directory=Path.Combine(parent,"migration-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(directory,PrivateDirectory);return directory;
        }
        public static void Write(string path,byte[] bytes)
        {
            using(var file=new FileStream(path,new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None,UnixCreateMode=PrivateFile})){file.Write(bytes);file.Flush(true);}
            Private(path,false);if(!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))throw new MigrationRefusal("MIGRATION_BACKUP_HASH");
        }
        public static async Task<MigrationProof> Prove(byte[] source,string alias,MigrationSource known,string runtime,string fixture,string contentRoot,string directory)
        {
            if(!Path.IsPathFullyQualified(runtime))throw new MigrationRefusal("MIGRATION_RUNTIME_PATH");Private(runtime,true);
            string copiedRuntime=Path.Combine(directory,"old-runtime"),store=Path.Combine(directory,"old-store"),content=Path.Combine(directory,"old-content");
            System.IO.Directory.CreateDirectory(copiedRuntime,PrivateDirectory);System.IO.Directory.CreateDirectory(store,PrivateDirectory);System.IO.Directory.CreateDirectory(content,PrivateDirectory);System.IO.Directory.CreateDirectory(Path.Combine(content,"data"),PrivateDirectory);
            foreach(var p in known.RuntimeFiles)
            {
                string file=Path.Combine(runtime,p.Key);Private(file,false);byte[] bytes=File.ReadAllBytes(file);
                if(GraphJson.Sha256(bytes)!=p.Value)throw new MigrationRefusal("MIGRATION_RUNTIME_HASH");Write(Path.Combine(copiedRuntime,p.Key),bytes);
            }
            byte[] data=File.ReadAllBytes(Path.Combine(contentRoot,"data","game_data.json"));
            if(GraphJson.Sha256(data)!=OwnerMigrationRegistry.ContentSha256)throw new MigrationRefusal("MIGRATION_CONTENT_HASH");Write(Path.Combine(content,"data","game_data.json"),data);
            Private(fixture,false);byte[] fixtureBytes=File.ReadAllBytes(fixture);string fixtureCopy=Path.Combine(directory,"fixture.json");Write(fixtureCopy,fixtureBytes);
            Write(Path.Combine(store,"account-"+alias+".json"),source);Write(Path.Combine(directory,"source.envelope.json"),source);
            string dotnet=Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??Environment.ProcessPath??"";
            if(!Path.IsPathFullyQualified(dotnet)||Path.GetFileName(dotnet)!="dotnet"||!File.Exists(dotnet))throw new MigrationRefusal("MIGRATION_DOTNET_HOST");
            var start=new ProcessStartInfo(dotnet){WorkingDirectory=directory,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
            foreach(string arg in new[]{Path.Combine(copiedRuntime,"MemoryForge.Server.dll"),"--verify-store","--MemoryForge:ContentRoot="+content,"--MemoryForge:PracticeFixture="+fixtureCopy,"--MemoryForge:PracticeStore="+store,"--MemoryForge:PracticePort=0"})start.ArgumentList.Add(arg);
            start.Environment.Clear();start.Environment["DOTNET_ENVIRONMENT"]="Development";start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";start.Environment["TMPDIR"]=directory;
            using var process=Process.Start(start)??throw new MigrationRefusal("MIGRATION_OLD_PROCESS");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5));}catch(TimeoutException){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();throw new MigrationRefusal("MIGRATION_OLD_TIMEOUT");}
            string text=await stdout,error=await stderr;
            if(error.Length!=0||!File.ReadAllBytes(Path.Combine(store,"account-"+alias+".json")).AsSpan().SequenceEqual(source)||!File.ReadAllBytes(fixtureCopy).AsSpan().SequenceEqual(fixtureBytes))throw new MigrationRefusal("MIGRATION_OLD_PROOF");
            var proof=Parse(process.ExitCode,text,GraphJson.Sha256(source),alias,directory);
            byte[] attestation=GraphJson.Bytes(GraphJson.Object("schema","mf-owner-migration-proof-1","alias",alias,"sourceSha256",proof.SourceSha256,
                "oldCoreModule",known.CoreModule,"oldRuntimeFiles",known.RuntimeFiles.ToDictionary(p=>p.Key,p=>(object?)p.Value,StringComparer.Ordinal),"fixtureSha256",GraphJson.Sha256(fixtureBytes),
                "contentSha256",OwnerMigrationRegistry.ContentSha256,"historyStatus","PASS","capacityHold",proof.CapacityHold,"counts",proof.Counts.ToDictionary(p=>p.Key,p=>(object?)p.Value,StringComparer.Ordinal),"verifierOutputSha256",GraphJson.Sha256(System.Text.Encoding.UTF8.GetBytes(text))),false);
            Write(Path.Combine(directory,"old-attestation.json"),attestation);
            return proof with{AttestationSha256=GraphJson.Sha256(attestation)};
        }
        public static void VerifyBackup(MigrationProof proof,byte[] candidate)
        {
            Private(proof.Directory,true);
            void FileHash(string relative,string sha)
            {string path=Path.Combine(proof.Directory,relative);Private(path,false);if(GraphJson.Sha256(File.ReadAllBytes(path))!=sha)throw new MigrationRefusal("MIGRATION_BACKUP_HASH");}
            FileHash("source.envelope.json",proof.SourceSha256);FileHash("old-store/account-"+proof.Alias+".json",proof.SourceSha256);
            FileHash("candidate.envelope.json",GraphJson.Sha256(candidate));FileHash("old-attestation.json",proof.AttestationSha256??throw new MigrationRefusal("MIGRATION_OLD_PROOF"));
            using var d=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(proof.Directory,"old-attestation.json")));var a=d.RootElement;
            foreach(var p in a.GetProperty("oldRuntimeFiles").EnumerateObject())FileHash("old-runtime/"+p.Name,p.Value.GetString()!);
            FileHash("fixture.json",a.GetProperty("fixtureSha256").GetString()!);FileHash("old-content/data/game_data.json",OwnerMigrationRegistry.ContentSha256);
        }
        internal static MigrationProof Parse(int exit,string text,string sourceSha,string alias,string directory)
        {
            string[] lines=text.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
            string prefix="verify-store account id="+alias+" ";var selected=lines.Where(s=>s.StartsWith(prefix,StringComparison.Ordinal)).ToList();
            if(selected.Count!=1||!selected[0].StartsWith(prefix+"status=PASS ",StringComparison.Ordinal)||exit!=0&&exit!=4)throw new MigrationRefusal("MIGRATION_OLD_PROOF");
            var summary=lines.Where(s=>s.StartsWith("verify-store result ",StringComparison.Ordinal)).ToList();
            if(summary.Count!=1)throw new MigrationRefusal("MIGRATION_OLD_PROOF");var values=Fields(summary[0]);
            if(values.GetValueOrDefault("failed")!="0"||values.GetValueOrDefault("verified")!="1"||values.GetValueOrDefault("status")!=(exit==4?"HOLD":"PASS")
                ||exit==4&&(!int.TryParse(values.GetValueOrDefault("held"),out int held)||held<1)||exit==0&&values.GetValueOrDefault("held")!="0")throw new MigrationRefusal("MIGRATION_OLD_PROOF");
            bool capacityHold=false;var capacities=lines.Where(s=>s.StartsWith("verify-store capacity id="+alias+" ",StringComparison.Ordinal)).Select(Fields).ToList();
            if(capacities.Count!=3)throw new MigrationRefusal("MIGRATION_OLD_PROOF");
            foreach(string cid in new[]{"nia","harin","rien"})
            {
                var rows=capacities.Where(v=>v.GetValueOrDefault("char")==cid).ToList();if(rows.Count!=1)throw new MigrationRefusal("MIGRATION_OLD_PROOF");var row=rows[0];
                if(row.ContainsKey("status")||row.ContainsKey("code")||!long.TryParse(row.GetValueOrDefault("used"),NumberStyles.None,CultureInfo.InvariantCulture,out long used)
                    ||!long.TryParse(row.GetValueOrDefault("tutorialCapacity"),NumberStyles.None,CultureInfo.InvariantCulture,out long cap)||row.GetValueOrDefault("tutorial")!=(used>cap?"OVER_CAPACITY":"OK"))throw new MigrationRefusal("MIGRATION_OLD_PROOF");
                capacityHold|=used>cap;
            }
            var probes=lines.Where(s=>s.StartsWith("verify-store probe id="+alias+" ",StringComparison.Ordinal)).Select(Fields).ToList();
            if(probes.Count!=1||probes[0].GetValueOrDefault("party")!="nia,harin,rien")throw new MigrationRefusal("MIGRATION_OLD_PROOF");
            foreach(string key in new[]{"useMemoryTrue","useMemoryFalse"})
            {string? code=probes[0].GetValueOrDefault(key);if(code is not ("OK" or "OVER_CAPACITY"))throw new MigrationRefusal("MIGRATION_OLD_PROOF");capacityHold|=code=="OVER_CAPACITY";}
            if(capacityHold!=(exit==4)||lines.Any(s=>s.StartsWith("verify-store capacity ",StringComparison.Ordinal)&&Fields(s).ContainsKey("code")))throw new MigrationRefusal("MIGRATION_OLD_PROOF");
            var account=Fields(selected[0]);var counts=new Dictionary<string,long>(StringComparer.Ordinal);
            foreach(string key in new[]{"revision","receipts","runReceipts","recruitmentReceipts","growthReceipts","runs","archived","current","checkpoints","memoryVersion"})
                if(long.TryParse(account.GetValueOrDefault(key),NumberStyles.None,CultureInfo.InvariantCulture,out long value))counts.Add(key,value);else throw new MigrationRefusal("MIGRATION_OLD_PROOF");
            return new MigrationProof(sourceSha,alias,directory,exit==4,counts);
        }
        static Dictionary<string,string> Fields(string line)
        {
            var result=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(string piece in line.Split(' ',StringSplitOptions.RemoveEmptyEntries)){int at=piece.IndexOf('=');if(at>0&&!result.TryAdd(piece.Substring(0,at),piece.Substring(at+1)))throw new MigrationRefusal("MIGRATION_OLD_PROOF");}return result;
        }
        public static void Match(MigrationProof old,StoreVerification current)
        {
            var values=new Dictionary<string,long>{{"revision",current.Revision},{"receipts",current.Receipts},{"runReceipts",current.RunReceipts},{"recruitmentReceipts",current.RecruitmentReceipts},{"growthReceipts",current.GrowthReceipts},
                {"runs",current.Archived+(current.Current?1:0)},{"archived",current.Archived},{"current",current.Current?1:0},{"checkpoints",current.Checkpoints},{"memoryVersion",current.MemoryVersion}};
            if(values.Any(p=>old.Counts[p.Key]!=p.Value))throw new MigrationRefusal("MIGRATION_WITNESS_MISMATCH");
        }
    }
}
