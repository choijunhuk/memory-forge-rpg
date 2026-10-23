using System;
using System.IO;
using System.Text.Json;
namespace MemoryForge.Server
{
    static class SupportPolicy
    {
        public const string Version="account-progression-20261021-v4",Hash="3c16d037a91b3f728f8a0eefa44060104a75a5604f82ae92cd91228fe443a68c";
        static readonly JsonDocument Source=Load();
        static JsonDocument Load()
        {
            using var input=typeof(SupportPolicy).Assembly.GetManifestResourceStream("MemoryForge.Server.AccountPolicyV4.json")??throw new InvalidDataException("support policy");using var output=new MemoryStream();input.CopyTo(output);
            byte[] bytes=output.ToArray();if(GraphJson.Sha256(bytes)!=Hash)throw new InvalidDataException("support policy hash");var doc=JsonDocument.Parse(bytes);var r=doc.RootElement;
            if(r.GetProperty("version").GetString()!=Version||r.GetProperty("baseEquipmentPolicyVersion").GetString()!=EquipmentPolicy.Version||r.GetProperty("baseEquipmentPolicySha256").GetString()!=EquipmentPolicy.Hash
                ||r.GetProperty("mainContentSha256").GetString()!=OwnerMigrationRegistry.ContentSha256||r.GetProperty("support").GetProperty("gaugeCost").GetInt32()!=100)throw new InvalidDataException("support policy source");
            EquipmentPolicy.Verify();return doc;
        }
        public static void Verify(){_=Source.RootElement;}
        public static void RequireContent(PracticeContent content){Verify();if(content.ContentSha256!=OwnerMigrationRegistry.ContentSha256)throw new InvalidDataException("support main content");}
    }
}
