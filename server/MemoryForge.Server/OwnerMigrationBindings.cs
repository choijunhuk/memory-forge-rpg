using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MemoryForge.Server
{
    sealed class MigrationRefusal:Exception
    {
        public string Code{get;}
        public MigrationRefusal(string code):base(code){Code=code;}
    }
    // Only these implementation-binding string tokens change. All other UTF-8 body bytes, including receipt BODY strings, are copied.
    static class OwnerMigrationBindings
    {
        public static string Module(byte[] bytes,string alias,out string replay)
        {
            byte[] body=Body(bytes);using var d=JsonDocument.Parse(body);var r=d.RootElement;
            if(Envelopes.Text(r,"accountId")!=alias)throw new MigrationRefusal("MIGRATION_ACCOUNT_MISMATCH");
            var binding=r.GetProperty("binding");replay=Envelopes.Text(binding,"replay");return Envelopes.Text(binding,"coreModule");
        }
        static byte[] Body(byte[] bytes)
        {
            PracticeRunStore.RequireWithinLimit(bytes.Length);using var d=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=4});var r=d.RootElement;
            BattleDataLoader.Fields(r,"schema","sha256","body");if(Envelopes.Text(r,"schema")!=Envelopes.Schema)throw new MigrationRefusal("MIGRATION_SCHEMA");
            byte[] body=Encoding.UTF8.GetBytes(Envelopes.Text(r,"body"));if(GraphJson.Sha256(body)!=Envelopes.Hex(r,"sha256"))throw new MigrationRefusal("MIGRATION_CHECKSUM");return body;
        }
        public static byte[] Candidate(byte[] source,string alias,MigrationMap map)
        {
            if(Module(source,alias,out string replay)!=map.Source.CoreModule||replay!=OwnerMigrationRegistry.SourceReplay)throw new MigrationRefusal("MIGRATION_SOURCE_MISMATCH");
            byte[] body=Body(source);var edits=new List<(int Start,int End,string Value)>();var path=new List<string>();
            var reader=new Utf8JsonReader(body,new JsonReaderOptions{MaxDepth=256});if(!reader.Read())throw new MigrationRefusal("MIGRATION_SCHEMA");Visit(ref reader);
            if(edits.Count(e=>e.Value==OwnerMigrationRegistry.TargetReplay)!=1||edits.Count(e=>e.Value==map.TargetModule)<1)throw new MigrationRefusal("MIGRATION_BINDINGS");
            using var output=new MemoryStream();int offset=0;
            foreach(var e in edits.OrderBy(e=>e.Start)){output.Write(body,offset,e.Start-offset);byte[] value=JsonSerializer.SerializeToUtf8Bytes(e.Value);output.Write(value);offset=e.End;}
            output.Write(body,offset,body.Length-offset);string text=Encoding.UTF8.GetString(output.ToArray());
            return GraphJson.Bytes(GraphJson.Object("schema",Envelopes.Schema,"sha256",GraphJson.Sha256(Encoding.UTF8.GetBytes(text)),"body",text),false);

            void Visit(ref Utf8JsonReader r)
            {
                if(r.TokenType==JsonTokenType.StartObject)
                {
                    var keys=new HashSet<string>(StringComparer.Ordinal);
                    while(r.Read()&&r.TokenType!=JsonTokenType.EndObject)
                    {
                        if(r.TokenType!=JsonTokenType.PropertyName)throw new MigrationRefusal("MIGRATION_SCHEMA");string key=r.GetString()!;
                        if(!keys.Add(key))throw new MigrationRefusal("MIGRATION_SCHEMA");path.Add(key);if(!r.Read())throw new MigrationRefusal("MIGRATION_SCHEMA");Visit(ref r);path.RemoveAt(path.Count-1);
                    }
                }
                else if(r.TokenType==JsonTokenType.StartArray)
                {
                    int i=0;while(r.Read()&&r.TokenType!=JsonTokenType.EndArray){path.Add((i++).ToString(System.Globalization.CultureInfo.InvariantCulture));Visit(ref r);path.RemoveAt(path.Count-1);}
                }
                else if(path.Count>0&&(path.Last()=="coreModule"||path.Last()=="replay"))
                {
                    bool global=path.Count==2&&path[0]=="binding";
                    bool launch=path.Count==3&&path[0]=="run"&&path[1]=="launch"||path.Count==4&&path[0]=="archive"&&path[2]=="launch";
                    bool encounter=path.Count==4&&path[0]=="growth"&&path[1]=="encounters";
                    bool module=path.Last()=="coreModule";
                    if(r.TokenType!=JsonTokenType.String||!(global||module&&(launch||encounter)))throw new MigrationRefusal("MIGRATION_BINDING_PATH");
                    string expected=module?map.Source.CoreModule:OwnerMigrationRegistry.SourceReplay;
                    if(r.GetString()!=expected)throw new MigrationRefusal("MIGRATION_SOURCE_MISMATCH");
                    edits.Add((checked((int)r.TokenStartIndex),checked((int)r.BytesConsumed),module?map.TargetModule:OwnerMigrationRegistry.TargetReplay));
                }
            }
        }
    }
}
