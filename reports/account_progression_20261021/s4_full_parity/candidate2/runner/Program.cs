using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using MemoryForge.Core;

static class Program
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping, IncludeFields=true, PropertyNamingPolicy=JsonNamingPolicy.CamelCase };
    static int states, commands, queries, cases, refusals;
    static object? Graph(object? value) { using var j=JsonDocument.Parse(JsonSerializer.Serialize(value,Json)); return BattleDataLoader.Graph(j.RootElement); }
    static void Same(JsonElement expected, object? value, string label)
    { string? difference=JsonGraphComparison.Difference(BattleDataLoader.Graph(expected),Graph(value),"$",0); if(difference!=null) throw new InvalidDataException(label+": "+difference); }
    static string Text(object? value)=>JsonSerializer.Serialize(value,Json);
    static JsonDocument ReadFixture(string path)
    { using var file=File.OpenRead(path); if(!path.EndsWith(".gz",StringComparison.Ordinal))return JsonDocument.Parse(file); using var zip=new GZipStream(file,CompressionMode.Decompress);return JsonDocument.Parse(zip); }
    static void Full(JsonElement expected,Battle b,string label)
    { Same(expected,b.State.ToJsonGraph(),label); states++; }
    static int N(JsonElement j,string name)=>j.GetProperty(name).GetInt32();
    static BattleSupportSetup Support(JsonElement j)=>new BattleSupportSetup { CharId=j.GetProperty("charId").GetString()!,WeaponId=j.GetProperty("weaponId").GetString()!,
        Res=N(j,"res"),Refine=N(j,"refine"),Power4=N(j,"power4"),Hp=N(j,"hp"),GearBps=N(j,"gearBps"),SkillLv=N(j,"skillLv"),UltLv=N(j,"ultLv") };
    static object? Query(Battle b,JsonElement q)
    {
        var a=q.GetProperty("args").EnumerateArray().ToList(); string method=q.GetProperty("method").GetString()!;
        string first=a.Count==0?"":a[0].GetString()!; string? target=a.Count>1&&a[1].ValueKind==JsonValueKind.String?a[1].GetString():null;
        List<string> Picks(int at)=>a.Count>at?a[at].EnumerateArray().Select(x=>x.GetString()!).ToList():new List<string>();
        return method switch { "supportInfo"=>b.SupportInfo(first,target,Picks(2)),"supportChoices"=>b.SupportChoices(first,Picks(1)),
            "supportForecast"=>b.SupportForecast(first,target,Picks(2)),"publicView"=>b.PublicView(),
            "forecast"=>b.Forecast(first,a[1].GetString()!,a[2].ValueKind==JsonValueKind.Null?null:a[2].GetString(),Picks(3)),
            "effectiveCard"=>b.EffectiveCard(first).ToJsonGraph(),_=>throw new InvalidDataException("unsupported query "+method) };
    }
    static int Main(string[] args)
    {
        if(args.Length!=2)throw new ArgumentException("SupportParity <fixture.json> <result.json>");
        string fixture=Path.GetFullPath(args[0]);using var doc=ReadFixture(fixture);
        var outcomes=new List<object>();
        foreach(var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            string id=row.GetProperty("id").GetString()!;string dataRoot=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fixture)!,row.GetProperty("dataRoot").GetString()!));
            var data=BattleDataLoader.Load(dataRoot);var setup=BattleDataLoader.Setup(row.GetProperty("setup"));
            if(row.GetProperty("setup").TryGetProperty("support",out var source)&&source.ValueKind!=JsonValueKind.Null)setup.Support=Support(source);
            var battle=Battle.Create(data,setup);Full(row.GetProperty("created"),battle,id+" create");cases++;
            foreach(var step in row.GetProperty("steps").EnumerateArray())
            {
                string label=id+" step"+commands;
                if(step.TryGetProperty("query",out var query))
                {
                    string before=Text(battle.State.ToJsonGraph());string? error=null;object? actual=null;
                    try{actual=Query(battle,query);}catch(RuleException e){error=e.Code;}
                    string? expected=step.TryGetProperty("errorCode",out var ec)?ec.GetString():null;
                    if(error!=expected)throw new InvalidDataException(label+" query code "+error+" expected "+expected);
                    if(error==null)Same(step.GetProperty("result"),actual,label+" query result");
                    if(before!=Text(battle.State.ToJsonGraph()))throw new InvalidDataException(label+" query mutated full state");queries++;
                }
                else
                {
                    string? error=null;BattleResponse? result=null;
                    try{result=battle.Command(BattleDataLoader.Request(step.GetProperty("request")));}catch(RuleException e){error=e.Code;}
                    string? expected=step.TryGetProperty("errorCode",out var ec)?ec.GetString():null;
                    if(error!=expected)throw new InvalidDataException(label+" command code "+error+" expected "+expected);
                    if(error==null)Same(step.GetProperty("response"),result!.ToJsonGraph(),label+" response");else refusals++;
                    commands++;
                }
                Full(step.GetProperty("state"),battle,label+" full state");
            }
            outcomes.Add(new { id,state=battle.State.ToJsonGraph() });
        }
        var assembly=typeof(Battle).Assembly;var report=new {status="PASS",cases,commands,queries,refusals,fullStateComparisons=states,
            logAndEvIncluded=true,coreMvid=assembly.ManifestModule.ModuleVersionId.ToString("N"),
            coreDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(),outcomes};
        File.WriteAllText(args[1],Text(report)+"\n");Console.WriteLine(Text(new {report.status,cases,commands,queries,refusals,fullStateComparisons=states,report.coreMvid,report.coreDllSha256}));return 0;
    }
}
