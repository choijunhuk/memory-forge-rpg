using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MemoryForge.Core;
using MemoryForge.Server;
[assembly: System.Runtime.Versioning.UnsupportedOSPlatform("windows")]

static class GrowthChecks
{
    static int checks;
    static void Check(bool condition, string label) { checks++; if (!condition) throw new Exception(label); }
    static bool Same(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);
    static string Id() => Guid.NewGuid().ToString("N");
    static string Text(Reply r, string field) { using var j = JsonDocument.Parse(r.Body); return j.RootElement.GetProperty(field).GetString()!; }
    static long now = 1800000000000;
    static async Task<int> Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]); var content = PracticeContent.Load(root); MathChecks(content); if(args.Contains("--math-only",StringComparer.Ordinal)) return 0;
        string directory = Path.Combine(Path.GetTempPath(), "mf-growth-" + Id()); Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var originalClock = GrowthCommands.Clock; GrowthCommands.Clock = () => now;
        var a = new PracticeAccount("growth-check", new byte[32], "short", new List<Dictionary<string, object?>> { GraphJson.Object("charId", "sera") });
        var persistence = new FileEnvelopePersistence(directory); var store = new PracticeRunStore(content, persistence);
        try
        {
            Console.WriteLine("GrowthChecks: earning tutorial + story through actual Run commands");
            await EarnChapter(0); await EarnChapter(1);
            var earned = await Envelope(); Check(earned.Progression!.Account() == (5, 0), "legitimate S1 AL5 bootstrap"); Check(earned.Growth == null, "S1 did not write S2 initialization");
            byte[] original = (await persistence.ReadAsync(a))!;
            var untouched = await store.Read(a, (e, run) => Reply.Json(200, ProgressionCommands.Profile(content, a.Id, e, run)));
            using (var j = JsonDocument.Parse(untouched.Body))
            {
                Check(j.RootElement.GetProperty("focus").GetProperty("value").GetInt32() == 240, "virtual focus240");
                var resources = j.RootElement.GetProperty("resources");
                Check(resources.EnumerateObject().Select(p => p.Name).OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(new[]
                    { "alloy", "book", "boss_token", "core", "credits", "ink", "oil", "polish", "refine_core", "seal" }), "current v4 profile has exactly ten approved resource keys");
                Check(resources.GetProperty("boss_token").GetInt64() == 0, "S1 bootstrap grants no weekly boss tokens");
            }
            Check(Same(original, (await persistence.ReadAsync(a))!), "profile pure before initialization");
            await Refused(() => Start("dg_oil", 1), "LOCKED"); await Refused(() => Start("dg_ink", 0), "UNKNOWN_TIER"); await Refused(() => Start("unknown", 1), "UNKNOWN_CONTENT");
            Check(Same(original, (await persistence.ReadAsync(a))!), "failed first S2 requests initialize nothing");
            string ink = await Start("dg_ink", 1); var active = await Envelope(); string frozen = GraphJson.Digest(active.GrowthCache!.Battles[ink].State.ToJsonGraph());
            await Refused(() => Start("dg_credit", 1), "RUN_ACTIVE");
            var story = new ExpeditionRequest(Id(), active.Revision, "story", 1, new List<string> { "nia", "harin", "rien" }, true);
            await Refused(() => store.Mutate(a, story.RequestId, story.Identity, (e, r) => ProgressionCommands.Launch(content, a, e, r, story)), "RUN_ACTIVE");
            var queryState = active.GrowthCache.Battles[ink].State; string uid = queryState.Zones.Hand.First();
            var query = new BattleQueryRequest(0, uid, "now", queryState.Enemies.First().Uid, new List<string>());
            byte[] queryBefore = (await persistence.ReadAsync(a))!; var queried = await store.Read(a, (e, _) => GrowthCommands.Query(content, a, e, ink, query));
            using (var j = JsonDocument.Parse(queried.Body)) Check(j.RootElement.GetProperty("context").EnumerateObject().Select(x => x.Name).SequenceEqual(new[]{"turn","battleVersion","battlePhase"}) && !j.RootElement.TryGetProperty("runId",out _), "standalone query exact context, no fakeRun");
            Check(Same(queryBefore, (await persistence.ReadAsync(a))!), "encounter query pure");
            await Finish(ink, false);
            var won = await Envelope(); Console.WriteLine("GrowthChecks: real first stars " + won.GrowthCache!.State.Stars["dg_ink:1"]); Check(won.GrowthCache!.State.Resources["ink"] == 6000 && won.GrowthCache.State.Focus == 220, "real ink victory6000 costs20");
            Check(GrowthCommands.Account(won) == (5, 20), "real victory grants20 ALXP");
            string credit = await Start("dg_credit", 1); await Finish(credit, false);
            var wallet = await Envelope(); Check(wallet.GrowthCache!.State.Resources["credits"] == 10000 && wallet.GrowthCache.State.Focus == 200, "real credit victory10000 costs20");
            string next = await Start("dg_ink", 1); var before = await Envelope(); string beforeBattle = GraphJson.Digest(before.GrowthCache!.Battles[next].State.ToJsonGraph());
            var level = new GrowthRequest(Id(), before.Revision, "characterLevelUp", GraphJson.Object("charId", "nia", "targetLevel", 2));
            byte[] bytes = (await persistence.ReadAsync(a))!; var preview = GrowthCommands.Preview(content, before, level);
            Check(Same(bytes, (await persistence.ReadAsync(a))!), "level preview no write");
            using (var j = JsonDocument.Parse(preview.Body)) Check(j.RootElement.GetProperty("cost").GetProperty("ink").GetInt64() == 200 && j.RootElement.GetProperty("after").GetProperty("stats").GetProperty("hp").GetInt32() == 62, "preview independent200ink100credits HP62");
            var leveled = await store.Mutate(a, level.RequestId, level.Identity, (e, r) => GrowthCommands.Mutate(content, a, e, r, level));
            var after = await Envelope(); Check(after.GrowthCache!.State.Resources["ink"] == 5800 && after.GrowthCache.State.Resources["credits"] == 9900, "exact level spend keeps5800ink");
            Check(GraphJson.Digest(after.GrowthCache.Battles[next].State.ToJsonGraph()) == beforeBattle, "level during battle preserves frozen state");
            var retry = await store.Mutate(a, level.RequestId, level.Identity, (_, _) => throw new Exception("retry executed")); Check(retry.Body == leveled.Body, "exact cached growth receipt");
            await Finish(next, false); string improved = await Start("dg_credit", 1); var fresh = await Envelope(); Check(fresh.GrowthCache!.Battles[improved].State.MaxHp == 184, "NEXT actual battle applies Lv2HP184"); await Finish(improved, false);
            string defeat = await Start("dg_ink", 6); int focus = (await Envelope()).GrowthCache!.State.Focus; await Finish(defeat, true); var lost = await Envelope(); Check(lost.GrowthCache!.State.Focus == focus && lost.Growth!.Active == null, "real defeat closes and costs0");
            Check(lost.GrowthCache.Settlements[defeat]["outcome"] as string == "DEFEAT", "defeat uses explicit server fact");
            var sweep = new GrowthRequest(Id(), lost.Revision, "dungeonSweep", GraphJson.Object("dungeonId", "dg_ink", "tier", 1, "times", 2));
            if (lost.GrowthCache.State.Stars["dg_ink:1"] == 3)
            {
                int beforeFocus = lost.GrowthCache.State.Focus; long beforeInk = lost.GrowthCache.State.Resources["ink"];
                var swept = await store.Mutate(a, sweep.RequestId, sweep.Identity, (e,r) => GrowthCommands.Mutate(content,a,e,r,sweep));
                lost = await Envelope(); Check(lost.GrowthCache!.State.Focus == beforeFocus - 40 && lost.GrowthCache.State.Resources["ink"] == beforeInk + 12000, "real unlocked sweep pays per-run floor twice");
                var duplicateSweep = await store.Mutate(a,sweep.RequestId,sweep.Identity,(_,_)=>throw new Exception("sweep repeated")); Check(duplicateSweep.Body==swept.Body,"sweep retry grants once");
            }
            else await Refused(() => store.Mutate(a,sweep.RequestId,sweep.Identity,(e,r)=>GrowthCommands.Mutate(content,a,e,r,sweep)),"SWEEP_LOCKED");
            if (args.Contains("--long", StringComparer.Ordinal))
            {
                Check(lost.GrowthCache!.State.Stars["dg_ink:1"] == 3,"long horizon uses previously earned real3star");
                int sweeps = 0;
                while (GrowthCommands.Account(lost).Level < 20)
                {
                    now = checked(now + 86400000); var q = new GrowthRequest(Id(),lost.Revision,"dungeonSweep",GraphJson.Object("dungeonId","dg_ink","tier",1,"times",12));
                    await store.Mutate(a,q.RequestId,q.Identity,(e,r)=>GrowthCommands.Mutate(content,a,e,r,q)); lost=await Envelope(); sweeps++;
                    Check(lost.GrowthCache!.State.Focus == 0,"earned long sweep uses regenerated240focus");
                    Check(sweeps<30,"bounded earned AL20 horizon");
                }
                Check(GrowthCommands.Account(lost).Level>=20,"AL20 earned only from actual victories and unlocked sweeps");
                Check(lost.Growth!.Ledger.Where(f=>f.Kind=="dungeonSweep").All(f=>(string)f.Request["dungeonId"]! == "dg_ink"),"no fake daily or dungeon completion");
                Console.WriteLine("GrowthChecks: earned AL"+GrowthCommands.Account(lost).Level+" through real3star + "+sweeps+" authorized-clock sweep batches");
            }
            var postGrowth = new ExpeditionRequest(Id(), lost.Revision, "story", 1, new List<string>{"nia","harin","rien"}, true);
            await store.Mutate(a,postGrowth.RequestId,postGrowth.Identity,(e,r)=>ProgressionCommands.Launch(content,a,e,r,postGrowth));
            var postLaunch = await Envelope(); Check(postLaunch.Run!.Launch!.PolicyVersion==SupportPolicy.Version && postLaunch.Run.Launch.PolicyHash==SupportPolicy.Hash,"new post-growth story pins current v4 identity");
            Check(Convert.ToInt32(postLaunch.Run.Launch.Party[0]["level"])==2,"current story uses earned level");
            Check(postLaunch.Archive.Where(r=>r.Launch?.PolicyVersion==AccountPolicy.Version).All(r=>r.Launch!.Party.All(p=>Convert.ToInt32(p["level"])==1)),"all old v1 builds remain exact Lv1");
            var close = new AbandonRequest(Id(),0,Contracts.AbandonIdentity(postLaunch.Run!.RunId,0));
            await store.Mutate(a,close.RequestId,close.Identity,(e,r)=>ProgressionCommands.Abandon(e,r,postLaunch.Run!.RunId,close)); lost=await Envelope();
            var saved = (await persistence.ReadAsync(a))!; var reopened = new PracticeRunStore(content, new FileEnvelopePersistence(directory));
            await reopened.Read(a, (e, _) => { Check(e!.GrowthCache!.State.Resources["ink"] == lost.GrowthCache!.State.Resources["ink"], "restart reconstructs exact wallet"); return Reply.Json(200, null); });
            Check(Same(saved, (await persistence.ReadAsync(a))!), "restart read grants nothing");
            foreach (Action<AccountEnvelope> mutate in new Action<AccountEnvelope>[] {
                e => e.Growth!.Encounters[0].Seed++, e => e.Growth!.Encounters[0].Party[0]["level"] = 60,
                e => e.Growth!.Encounters[0].MemoryVersion++, e => e.Growth!.Encounters[0].PolicyHash = new string('0',64),
                e => e.Growth!.Encounters[0].Journal[0].Sha256 = new string('0',64), e => e.Growth!.Ledger.RemoveAt(e.Growth.Ledger.Count - 1),
                e => e.Receipts[e.Growth!.Ledger.Last().RequestId].Body += " " })
            {
                var candidate = Envelopes.Decode(saved, a, content); mutate(candidate);
                await Refused(() => { Envelopes.Decode(Envelopes.Encode(candidate), a, content); return Task.CompletedTask; }, "STORE_CORRUPT");
            }
            Console.WriteLine("GrowthChecks: " + checks + " passed (real Core, isolated file lifecycle; no live accounts)"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { GrowthCommands.Clock = originalClock; Directory.Delete(directory, true); }

        async Task<AccountEnvelope> Envelope() => Envelopes.Decode((await persistence.ReadAsync(a))!, a, content);
        async Task EarnChapter(int chapter)
        {
            long revision = (await persistence.ReadAsync(a)) is byte[] b ? Envelopes.Decode(b, a, content).Revision : 0;
            var q = new ExpeditionRequest(Id(), revision, chapter == 0 ? "tutorial" : "story", chapter == 0 ? null : chapter, chapter == 0 ? null : new List<string> { "nia", "harin", "rien" }, true);
            await store.Mutate(a, q.RequestId, q.Identity, (e, r) => ProgressionCommands.Launch(content, a, e, r, q));
            Check(await S1Journey.PlayToSave(store, persistence, a, content), "authentic chapter" + chapter + " clear");
            var r = (await Envelope()).Run!; string id = Id(); var cm = GraphJson.Object("requestId", id, "expectedMemoryVersion", r.MemoryVersion, "selectedChangeIds", new List<object?>());
            string identity = GraphJson.Digest(GraphJson.Object("op", Contracts.MemoryCommit, "runId", r.RunId, "expectedVersion", r.Journal.Count, "args", cm));
            await S1Journey.Command(store, a, content, id, identity, Contracts.MemoryCommit, cm);
        }
        async Task<string> Start(string dungeon, int tier)
        {
            var e = await Envelope(); var q = new GrowthRequest(Id(), e.Revision, "encounterStart", GraphJson.Object("dungeonId", dungeon, "tier", tier, "party", new List<object?> { "nia", "harin", "rien" }));
            return Text(await store.Mutate(a, q.RequestId, q.Identity, (x, r) => GrowthCommands.Start(content, a, x, r, q)), "encounterId");
        }
        async Task Finish(string id, bool onlyEnd)
        {
            for (int turn = 0; turn < 800; turn++)
            {
                var e = await Envelope(); var battle = e.GrowthCache!.Battles[id]; var b = battle.State; if (b.Phase != "PLAYER") { Check(b.Phase == (onlyEnd ? "DEFEAT" : "VICTORY"), "actual expected battle outcome"); return; }
                string kind = "end"; var arguments = GraphJson.Object(); string target = b.Enemies.Where(x => x.Alive).OrderBy(x => x.Hp).First().Uid;
                if (!onlyEnd)
                {
                    foreach (string uid in b.Zones.Hand) if (battle.Check(uid, "now", target) == null) { kind = "play"; arguments = GraphJson.Object("uid", uid, "mode", "now", "target", target); break; }
                    if (kind == "end") { var ult = b.Actors.FirstOrDefault(x => x.Gauge >= 100); if (ult != null) { kind = "ult"; arguments = GraphJson.Object("actor", ult.Id, "target", target); } }
                }
                var q = new CommandRequest(Id(), e.Growth!.Encounters.Single(x => x.Id == id).Journal.Count, kind, arguments, ""); q = q with { Identity = Contracts.EncounterIdentity(id, q.ExpectedVersion, kind, arguments) };
                var response = await store.Mutate(a, q.RequestId, q.Identity, (x, _) => GrowthCommands.Command(content, a, x, id, q));
                var replay = await store.Mutate(a, q.RequestId, q.Identity, (_, _) => throw new Exception("command retry executed")); Check(response.Body == replay.Body, "command retry byte-identical");
            }
            throw new Exception("encounter command bound");
        }
    }
    static async Task Refused(Func<Task> action, string code)
    { try { await action(); throw new Exception("accepted " + code); } catch (ApiError e) { Check(e.Code == code, "refused " + code + " got " + e.Code); } catch (RuleException e) { Check(e.Code == code, "Core refused " + code + " got " + e.Code); } }
    static void MathChecks(PracticeContent c)
    {
        var cost = GrowthMath.Cost(c.Data.Stats, 1, 0, 2); Check(cost == (200L,100L), "Lv1->2 independent cost"); Check(GrowthMath.Cost(c.Data.Stats,1,80,2) == (120L,60L), "XPcarry80 exact");
        Check(GrowthPolicy.Normalize(new Dictionary<string,long>{{"ink_m",2}})["ink"] == 10000, "explicit item normalize once");
        var modes = GrowthPolicy.Modes(c);
        var vectors = new Dictionary<string,int[]> { ["dg_ink"] = new[]{6000,9600,13200,16800,20400,24000}, ["dg_credit"] = new[]{10000,16000,22000,28000,34000,40000}, ["dg_oil"] = new[]{3000,4800,6600,8400,10200,12000}, ["dg_seal"] = new[]{0,1,1,2,2,3}, ["dg_book"] = new[]{2,3,4,5,6,8}, ["dg_polish"] = new[]{5,8,11,14,17,20} };
        foreach (var v in vectors) for(int tier=1;tier<=6;tier++) { var reward=ContentModes.Dungeon.Reward(modes,v.Key,tier); string key=v.Key=="dg_credit"?"credits":v.Key.Substring(3); Check(reward[key]==v.Value[tier-1],"independent reward "+v.Key+":"+tier); }
        for(int tier=1;tier<=6;tier++) Check(ContentModes.Dungeon.Reward(modes,"dg_oil",tier)["alloy"]==(tier==6?1:0),"oil alloy floor");
        var focus = new GrowthState { Initialized=true,Focus=239,Anchor=1000 }; focus.At(360999); Check(focus.Focus==239,"focus before6min"); focus.At(361000); Check(focus.Focus==240&&focus.Anchor==361000,"focus exact6min cap");
        focus.Spend(20,400000); Check(focus.Focus==220&&focus.Anchor==400000,"cap spend resets anchor"); focus.At(759999); Check(focus.Focus==220,"residual wait"); focus.At(760000); Check(focus.Focus==221,"regeneration interval"); long anchor=focus.Anchor; focus.At(0); Check(focus.Anchor==anchor,"clock rollback");
        foreach(int overflow in new[]{250,999}) { var s = new GrowthState { Initialized=true,Focus=overflow,Anchor=0 };s.At(99999999);Check(s.Focus==overflow,"overflow preserved"); }
        var residual = new GrowthState { Initialized=true,Focus=220,Anchor=1000 }; residual.At(541000); Check(residual.Focus==221&&residual.Anchor==361000,"fractional interval kept"); residual.Spend(20,541000); residual.At(721000); Check(residual.Focus==202&&residual.Anchor==721000,"below-cap spend keeps unfinished interval");
        var longWait = new GrowthState { Initialized=true,Focus=0,Anchor=1000 }; longWait.At(1000000000); Check(longWait.Focus==240&&longWait.Anchor==1000000000,"long wait caps and discards banked time");
        Check(GrowthMath.Cost(c.Data.Stats,1,0,3)==(440L,220L),"multi target existing curve");
        foreach (int target in new[]{0,1,21}) { try { GrowthMath.Cost(c.Data.Stats,1,0,target); throw new Exception("invalid level accepted"); } catch(ApiError e) { Check(e.Code==(target>20?"LEVEL_CAP":"INVALID_TARGET_LEVEL"),"level target refusal"); } }
        try { GrowthPolicy.Normalize(new Dictionary<string,long>{{"ink_l",long.MaxValue}}); throw new Exception("overflow accepted"); } catch(ApiError e) { Check(e.Code=="RESOURCE_OVERFLOW","normalization checked overflow"); }
        foreach(var body in new[]{"{\"requestId\":\"12345678\",\"expectedRevision\":0,\"dungeonId\":\"dg_ink\",\"tier\":1,\"party\":[\"nia\",\"harin\",\"rien\"],\"seed\":1}","{\"requestId\":\"12345678\",\"expectedRevision\":0,\"targetLevel\":2,\"ink\":0}"})
        { try { if(body.Contains("dungeonId")) Contracts.ParseEncounter(Encoding.UTF8.GetBytes(body)); else Contracts.ParseLevel(Encoding.UTF8.GetBytes(body),"nia",false); throw new Exception("unknown economic field accepted"); } catch(ApiError e) { Check(e.Code=="UNKNOWN_FIELD","strict economic boundary"); } }
        try { GrowthCommands.Preview(c,null,new GrowthRequest("preview-only",0,"characterLevelUp",GraphJson.Object("charId","nia","targetLevel",2))); throw new Exception("fresh preview accepted"); } catch(ApiError e) { Check(e.Code=="TUTORIAL_REQUIRED","fresh preview same tutorial gate as commit"); }
        Console.WriteLine("GrowthChecks: math/reward vectors " + checks + " passed");
    }
}
