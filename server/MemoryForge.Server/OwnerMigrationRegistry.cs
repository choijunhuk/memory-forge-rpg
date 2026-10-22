using System;
using System.Collections.Generic;
using System.IO;

namespace MemoryForge.Server
{
    sealed record MigrationSource(string CoreModule,string CoreSha256)
    {
        public Dictionary<string,string> RuntimeFiles => new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["MemoryForge.Server.dll"]="0480bf6aa2153a3b13d0d7d249aa9c66cfe83e592f4f21a5f7ff1f1728d31fb5",
            ["MemoryForge.Core.dll"]=CoreSha256,
            ["Npgsql.dll"]="e2bcafe1660fe4543fe9dfcabed972ec8d32259e5ea8009462ce1de23c7034c2",
            ["MemoryForge.Server.deps.json"]="35a8af7ddb367e45147aec581ce472458b2b32e6b3e5f449c8059052fec79b5c",
            ["MemoryForge.Server.runtimeconfig.json"]="3e4679ceea54e306c1d8c62652a86e4c1bb31511afdbf176a9d84298c4834f38"
        };
    }
    sealed record MigrationMap(MigrationSource Source,string TargetModule,string TargetSha256);
    // Reviewed measured S3 snapshots -> Root's final approved S4 Core binary only. A fresh build in another path is NOT this target.
    // Source equality or an arbitrary CLI MVID is never a substitute for these exact loaded-module and DLL hashes.
    static class OwnerMigrationRegistry
    {
        public const string SourceReplay="mf-dev-practice-replay-1",TargetReplay="mf-dev-practice-replay-2";
        public const string ContentSha256="b6d3543a1d87f0a28e4824e0c1ae45a1eabc676d7abefcd2e57e23b4abc0054a";
        internal static readonly string TargetModule="a1b72b89701e4645a7b2f130ba71e282",TargetCoreSha256="65bc74a134ee00da95c73d91e19fe8b42f855f9c8a81462b8959f9f745b801b8";
        internal static readonly MigrationSource[] Sources=
        {
            new MigrationSource("5fc4f6b654514cb1b6e60438cb895862","3d818b546cfe3860496f3373a038c550479cf63cfa682926805d2f97bb1d9ef6"),
            new MigrationSource("8689e75122ef4dbf90136bd3de742f0f","0de09b4acd4064cb7ddb68a84f24a53a0c094ddf3cca66f00e4ba7623341d294")
        };
        internal static MigrationMap Require(PracticeContent content,string sourceModule)
        {
            var source=Array.Find(Sources,s=>s.CoreModule==sourceModule)??throw new MigrationRefusal("MIGRATION_SOURCE_UNKNOWN");
            if(TargetModule.Length!=32||TargetCoreSha256.Length!=64||content.CoreModule!=TargetModule||PracticeContent.ReplayFormat!=TargetReplay
                ||content.ContentSha256!=ContentSha256||GraphJson.Sha256(File.ReadAllBytes(typeof(MemoryForge.Core.Run).Assembly.Location))!=TargetCoreSha256)
                throw new MigrationRefusal("MIGRATION_TARGET_UNKNOWN");
            return new MigrationMap(source,TargetModule,TargetCoreSha256);
        }
    }
}
