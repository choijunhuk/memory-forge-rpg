using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

namespace MemoryForge.Server
{
    // Offline owner operation only. The normal HTTP decoder keeps its strict current binding guards.
    static class OwnerMigration
    {
        public const string Flag="--migrate-account";
        static bool OwnerOption(string arg)=>new[]{Flag,"--old-runtime","--backup-directory","--write"}.Any(name=>arg.Equals(name,StringComparison.OrdinalIgnoreCase)||arg.StartsWith(name+"=",StringComparison.OrdinalIgnoreCase));
        public static bool Requested(string[] args)=>args.Any(OwnerOption);
        public static async Task<int> RunAsync(string[] args,TextWriter output,TextWriter error)
        {
            try
            {
                var special=new Dictionary<string,string>(StringComparer.Ordinal);var hostArgs=new List<string>();bool write=false;
                for(int i=0;i<args.Length;i++)
                {
                    string arg=args[i];
                    if(arg=="--write"){if(write)throw new MigrationRefusal("MIGRATION_ARGUMENTS");write=true;}
                    else if(arg is Flag or "--old-runtime" or "--backup-directory")
                    {if(i+1==args.Length||args[i+1].StartsWith('-')||!special.TryAdd(arg,args[++i]))throw new MigrationRefusal("MIGRATION_ARGUMENTS");}
                    else {if(OwnerOption(arg)||AccountStoreVerifier.Resembles(arg))throw new MigrationRefusal("MIGRATION_ARGUMENTS");hostArgs.Add(arg);}
                }
                if(special.Count!=3)throw new MigrationRefusal("MIGRATION_ARGUMENTS");
                var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=hostArgs.ToArray()});
                await using var host=await PracticeHost.ConfigureAsync(builder.Environment,builder.Configuration,readOnly:true);
                string alias=special[Flag];var account=host.Fixture.Accounts.SingleOrDefault(a=>a.Id==alias)??throw new MigrationRefusal("MIGRATION_ALIAS");
                byte[] source=await host.Persistence.ReadAsync(account)??throw new MigrationRefusal("MIGRATION_ABSENT");
                string module=OwnerMigrationBindings.Module(source,alias,out string replay);
                if(module==host.Content.CoreModule&&replay==PracticeContent.ReplayFormat)
                {
                    _=OwnerMigrationRegistry.Require(host.Content,OwnerMigrationRegistry.Sources[0].CoreModule);AccountStoreVerifier.Verify(host.Content,account,source,out _);
                    output.WriteLine("migration account="+alias+" status=ALREADY_MIGRATED mode="+(write?"write":"dry-run"));return 0;
                }
                var map=OwnerMigrationRegistry.Require(host.Content,module);string directory=OwnerMigrationProof.Prepare(special["--backup-directory"],builder.Configuration["MemoryForge:ContentRoot"]!);
                var proof=await OwnerMigrationProof.Prove(source,alias,map.Source,special["--old-runtime"],builder.Configuration["MemoryForge:PracticeFixture"]!,builder.Configuration["MemoryForge:ContentRoot"]!,directory);
                byte[] candidate=OwnerMigrationBindings.Candidate(source,alias,map);Verify(candidate);OwnerMigrationProof.Write(Path.Combine(directory,"candidate.envelope.json"),candidate);
                if(!write){output.WriteLine("migration account="+alias+" status=DRY_RUN_PASS oldHistory=PASS capacity="+(proof.CapacityHold?"HOLD":"PASS"));return 0;}
                await CommitAsync(host.Persistence,host.Content,account,source,candidate,proof);
                output.WriteLine("migration account="+alias+" status=MIGRATED oldHistory=PASS capacity="+(proof.CapacityHold?"HOLD":"PASS"));return 0;

                void Verify(byte[] bytes){var verified=AccountStoreVerifier.Verify(host.Content,account,bytes,out _);OwnerMigrationProof.Match(proof,verified);}
            }
            catch(MigrationRefusal e){error.WriteLine("migration status=REFUSED code="+e.Code);return 2;}
            catch(StoreVerifyFailure e){error.WriteLine("migration status=REFUSED code="+e.Code);return 2;}
            catch(ApiError e){error.WriteLine("migration status=REFUSED code="+e.Code);return 2;}
            catch(StartupRefusal){error.WriteLine("migration status=REFUSED code=MIGRATION_CONFIGURATION");return 2;}
            catch(Exception){error.WriteLine("migration status=REFUSED code=MIGRATION_FAILED");return 2;}
        }
        internal static async Task CommitAsync(IEnvelopePersistence persistence,PracticeContent content,PracticeAccount account,byte[] source,byte[] candidate,MigrationProof proof)
        {
            if(proof.Alias!=account.Id||GraphJson.Sha256(source)!=proof.SourceSha256)throw new MigrationRefusal("MIGRATION_PROOF_CHANGED");
            OwnerMigrationProof.VerifyBackup(proof,candidate);Verify(candidate);
            try
            {
                await persistence.TransactAsync(account,source,current=>
                {
                    if(current==null||!current.AsSpan().SequenceEqual(source))throw new MigrationRefusal("MIGRATION_CAS_CHANGED");
                    OwnerMigrationProof.VerifyBackup(proof,candidate);Verify(candidate);return new Transition(Reply.Json(200,null),candidate);
                });
            }
            catch(ApiError e)when(e.Code is "STORE_COMMIT_UNKNOWN" or "STORE_WRITE_FAILED" or "STORE_UNAVAILABLE")
            {if(!await Resolve())throw new MigrationRefusal("MIGRATION_NOT_COMMITTED");}
            if(!await Resolve())throw new MigrationRefusal("MIGRATION_RECOVERY_FAILED");
            void Verify(byte[] bytes){var verified=AccountStoreVerifier.Verify(content,account,bytes,out _);OwnerMigrationProof.Match(proof,verified);}
            async Task<bool> Resolve()
            {
                bool installed=false;
                await persistence.TransactAsync(account,source,current=>
                {
                    if(current!=null&&current.AsSpan().SequenceEqual(candidate)){Verify(current);installed=true;}
                    else if(current==null||!current.AsSpan().SequenceEqual(source))throw new MigrationRefusal("MIGRATION_RECOVERY_CHANGED");
                    return new Transition(Reply.Json(200,null),null);
                });return installed;
            }
        }

    }
}
