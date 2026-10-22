// Isolated actual Core lifecycle + real file persistence. No live accounts, injected victories, fake ownership or fake balances.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MemoryForge.Core;
using MemoryForge.Server;
[assembly: System.Runtime.Versioning.UnsupportedOSPlatform("windows")]

static class ProgressionChecks
{
    static int checks;
    static void Check(bool ok, string text) { checks++; if (!ok) throw new Exception(text); }
    static string Id() => Guid.NewGuid().ToString("N");
    static byte[] Bytes(object? graph) => GraphJson.Bytes(graph, false);
    static async Task Refused(Func<Task> action, string code)
    {
        try { await action(); throw new Exception("accepted " + code); } catch (ApiError e) { Check(e.Code == code, "expected " + code + " got " + e.Code); }
    }
    static async Task<int> Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]), path = Path.Combine(Path.GetTempPath(), "mf-s1-" + Id()); Directory.CreateDirectory(path); File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var content = PracticeContent.Load(root); var account = new PracticeAccount("s1-check", new byte[32], "short", new List<Dictionary<string, object?>> { GraphJson.Object("charId", "sera") });
        var persistence = new FileEnvelopePersistence(path); var store = new PracticeRunStore(content, persistence);
        try
        {
            await CapacityBlocks(content, path);
            Check(await persistence.ReadAsync(account) == null, "unborn file absent");
            await store.Read(account, (e, live) => { Check(e == null && !(bool)ProgressionCommands.Profile(content, account.Id, e, live)["initialized"]!, "pure uninitialized profile"); return Reply.Json(200, null); });
            Check(await persistence.ReadAsync(account) == null, "GET creates nothing");
            foreach (string body in new[] { "{\"requestId\":\"12345678\",\"expectedRevision\":0,\"mode\":\"tutorial\"}", "{\"requestId\":\"12345678\",\"expectedRevision\":0,\"mode\":\"tutorial\",\"useMemory\":null}" })
                await Refused(() => { Contracts.ParseExpedition(Encoding.UTF8.GetBytes(body)); return Task.CompletedTask; }, "INVALID_REQUEST");
            var q = new ExpeditionRequest(Id(), 0, "tutorial", null, null, true);
            Reply launched = await store.Mutate(account, q.RequestId, q.Identity, (e, r) => ProgressionCommands.Launch(content, account, e, r, q));
            string runId = JsonDocument.Parse(launched.Body).RootElement.GetProperty("runId").GetString()!;
            var retry = await store.Mutate(account, q.RequestId, q.Identity, (_, _) => throw new Exception("retry executed")); Check(retry.Body == launched.Body, "launch retry exact");
            await Refused(() => store.Mutate(account, q.RequestId, "wrong", (_, _) => throw new Exception("conflict executed")), "IDEMPOTENCY_CONFLICT");
            byte[] prior = (await persistence.ReadAsync(account))!;
            await Refused(() => store.Mutate(account, Id(), "active", (e, r) => ProgressionCommands.Launch(content, account, e, r, q with { RequestId = Id(), ExpectedRevision = e.Revision })), "RUN_ACTIVE");
            var afterRefusal = (await persistence.ReadAsync(account))!; Check(prior.SequenceEqual(afterRefusal), "active launch no write");
            var abandon = new AbandonRequest(Id(), 0, Contracts.AbandonIdentity(runId, 0));
            var abandoned = await store.Mutate(account, abandon.RequestId, abandon.Identity, (e, r) => ProgressionCommands.Abandon(e, r, runId, abandon));
            using (var j = JsonDocument.Parse(abandoned.Body)) Check(j.RootElement.EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "runId", "version", "memoryVersion" }) && j.RootElement.GetProperty("version").GetInt32() == 1, "exact abandon ACK");
            await store.Read(account, (e, r) => { Check(e!.Run == null && e.Recruitment == null && !e.Progression!.Cleared(0), "abandon grants nothing"); return Reply.Json(200, null); });
            await VerifyStored(content, account, persistence, 1, false, 0, "after abandon");
            var staleRetry = await store.Mutate(account, q.RequestId, q.Identity, (_, _) => throw new Exception("old launch executed")); Check(staleRetry.Body == launched.Body, "closed launch retry cached");
            await Refused(() => store.Mutate(account, Id(), "closed", (e, r) => ProgressionCommands.Abandon(e, r, runId, abandon)), "RUN_NOT_FOUND");
            var second = q with { RequestId = Id(), ExpectedRevision = 2, UseMemory = false };
            await store.Mutate(account, second.RequestId, second.Identity, (e, r) => ProgressionCommands.Launch(content, account, e, r, second));
            Check(await PlayToSave(store, persistence, account, content), "actual temporary tutorial clear");
            var before = Envelopes.Decode((await persistence.ReadAsync(account))!, account, content); var rec = before.Run!;
            string commitId = Id(); var cmArgs = GraphJson.Object("requestId", commitId, "expectedMemoryVersion", rec.MemoryVersion, "selectedChangeIds", new List<object?>());
            string identity = GraphJson.Digest(GraphJson.Object("op", Contracts.MemoryCommit, "runId", rec.RunId, "expectedVersion", rec.Journal.Count, "args", cmArgs));
            var saved = await Command(store, account, content, commitId, identity, Contracts.MemoryCommit, cmArgs);
            var stored = Envelopes.Decode((await persistence.ReadAsync(account))!, account, content);
            Check(stored.Progression!.Account() == (3, 100), "320 XP reaches AL3 +100");
            Check(stored.Recruitment!.OwnedChars.Count == 3 && AccountPolicy.Starters.All(c => stored.Recruitment.OwnedChars[c] == 1), "exact three starters");
            Check(stored.Recruitment.FreeCrystals == 300 && stored.Recruitment.Tickets["standardTicket"] == 5 && stored.Recruitment.Pity.Values.All(p => p.Total == 0), "grant amounts and untouched pity");
            Check(ProgressionCommands.Weapons(content, stored, long.MaxValue).Count == 4, "four actual R instances");
            await VerifyStored(content, account, persistence, 1, true, 1, "after tutorial grant");
            var savedRetry = await store.Mutate(account, commitId, identity, (_, _) => throw new Exception("commit retry executed")); Check(savedRetry.Body == saved.Body, "memory lost reply exact and grant once");
            var story = new ExpeditionRequest(Id(), stored.Revision, "story", 1, new List<string> { "nia", "harin", "rien" }, true);
            await store.Mutate(account, story.RequestId, story.Identity, (e, r) => ProgressionCommands.Launch(content, account, e, r, story));
            var storyStored = Envelopes.Decode((await persistence.ReadAsync(account))!, account, content);
            Check(storyStored.Run!.Launch!.StageLevel == 8 && storyStored.Run.Launch.Party.All(p => (long)Convert.ToInt64(p["level"]) == 1), "story uses stage8 explicit Lv1 and frozen build");
            await store.Read(account, (e, run) => { Check(run!.State.MaxHp == 183 && run.State.Difficulty.Hp == 55, "stage scaling applied once"); return Reply.Json(200, null); });
            Check(await PlayToSave(store, persistence, account, content), "actual owned stage8 story clear");
            var storyBeforeSave = Envelopes.Decode((await persistence.ReadAsync(account))!, account, content); var storyRecord = storyBeforeSave.Run!;
            string storyCommit = Id(); var storyArgs = GraphJson.Object("requestId", storyCommit, "expectedMemoryVersion", storyRecord.MemoryVersion, "selectedChangeIds", new List<object?>());
            string storyIdentity = GraphJson.Digest(GraphJson.Object("op", Contracts.MemoryCommit, "runId", storyRecord.RunId, "expectedVersion", storyRecord.Journal.Count, "args", storyArgs));
            await Command(store, account, content, storyCommit, storyIdentity, Contracts.MemoryCommit, storyArgs);
            var complete = Envelopes.Decode((await persistence.ReadAsync(account))!, account, content);
            Check(complete.Progression!.Account() == (5, 0) && complete.Recruitment!.FreeCrystals == 900 && complete.Recruitment.Tickets["standardTicket"] == 7, "chapter1 once gives AL5 and exact currency");
            Check(complete.Recruitment!.OwnedChars.Values.All(n => n == 1), "chapter1 does not duplicate starters");
            await VerifyStored(content, account, persistence, 2, true, 2, "after story grant");
            foreach (int copies in new[] { 0, 1, 6, 7 })
            {
                var original = new GachaState(); if (copies != 0) original.OwnedChars["nia"] = copies;
                var granted = new GachaState(); if (copies != 0) granted.OwnedChars["nia"] = copies;
                int calls = 0; var cfg = content.Recruitment.Config;
                var core = Gacha.One(cfg, original, content.Recruitment.Active.Single(b => b.Kind == "standard"), bound => calls++ == 0 ? cfg.Curves["standard"].SsrBase : cfg.SrChars.IndexOf("nia"));
                var row = RecruitmentGrants.ApplyState(cfg, granted, 0).Single(x => x.Id == "nia");
                Check(row.IsNew == core.IsNew && row.Resonance == core.Resonance && row.Shards == core.Shards && granted.OwnedChars["nia"] == original.OwnedChars["nia"], "starter SR duplicates match Core at " + copies);
            }
            // Redigested structural tampering cannot manufacture grants or change an immutable stage.
            foreach (var mutate in new Action<AccountEnvelope>[] { e => e.Progression!.Ledger.RemoveAt(e.Progression.Ledger.FindIndex(f => f.Kind == "grant")), e => e.Run!.Launch!.StageLevel = 60,
                e => e.Recruitment!.FreeCrystals++, e => e.Archive[0].End!.MemoryVersion++,
                e => e.Receipts[e.Archive[0].End!.RequestId].Body = GraphJson.Text(GraphJson.Object("runId", e.Archive[0].RunId,
                    "version", 99, "memoryVersion", 0), false) })
            {
                var copy = Envelopes.Decode((await persistence.ReadAsync(account))!, account, content); mutate(copy);
                byte[] tampered = Envelopes.Encode(copy), witness = (byte[])tampered.Clone();
                await Refused(() => { Envelopes.Decode(tampered, account, content); return Task.CompletedTask; }, "STORE_CORRUPT");
                try { AccountStoreVerifier.Verify(content, account, tampered, out _); throw new Exception("verifier accepted tampered S1 proof"); }
                catch (StoreVerifyFailure failure) { Check(failure.Code == "STORE_CORRUPT" && failure.Where == "decode", "S1 tamper fails normal decode before verification"); }
                Check(tampered.SequenceEqual(witness), "verifier leaves rejected bytes untouched");
            }
            // Normal typed decode proves the linked facts; the offline verifier additionally proves every historical memory member.
            foreach (string member in new[] { "history", "receipts" })
            {
                byte[] persisted = (await persistence.ReadAsync(account))!;
                var copy = Envelopes.Decode(persisted, account, content);
                if (member == "history") ((IList)copy.Memory[member]!).RemoveAt(0);
                else ((IDictionary<string, object?>)copy.Memory[member]!).Clear();
                byte[] tampered = Envelopes.Encode(copy), witness = (byte[])tampered.Clone();
                Envelopes.Decode(tampered, account, content);
                try { AccountStoreVerifier.Verify(content, account, tampered, out _); throw new Exception("verifier accepted missing memory " + member); }
                catch (StoreVerifyFailure failure) { Check(failure.Code == "VERIFY_MEMORY_MISMATCH" && failure.Where == "memory", "full chronology rejects missing memory " + member); }
                byte[] after = (await persistence.ReadAsync(account))!;
                Check(tampered.SequenceEqual(witness) && persisted.SequenceEqual(after), "historical memory refusal preserves all bytes");
            }
            Console.WriteLine("ProgressionChecks: " + checks + " passed (isolated file/Core, no real accounts)"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(path, true); }
    }
    static async Task VerifyStored(PracticeContent content, PracticeAccount account, FileEnvelopePersistence persistence, int archived, bool current, long memoryVersion, string label)
    {
        byte[] bytes = (await persistence.ReadAsync(account))!, witness = (byte[])bytes.Clone();
        var result = AccountStoreVerifier.Verify(content, account, bytes, out var decoded);
        Check(result.Archived == archived && result.Current == current && result.MemoryVersion == memoryVersion, label + " verifies complete run and memory chronology");
        Check(result.Revision == decoded.Revision && result.Receipts == decoded.Receipts.Count && result.Revision == result.Receipts,
            label + " verifies exact global revision and receipts");
        Check(result.RunReceipts == result.Receipts && result.RecruitmentReceipts == 0 && result.Checkpoints == result.RunReceipts,
            label + " counts shared grant receipt once as its real memory commit");
        byte[] after = (await persistence.ReadAsync(account))!;
        Check(bytes.SequenceEqual(witness) && bytes.SequenceEqual(after), label + " verification writes nothing");
    }
    static async Task<Reply> Command(PracticeRunStore store, PracticeAccount account, PracticeContent content, string id, string identity, string kind, Dictionary<string, object?> args)
    {
        return await store.Mutate(account, id, identity, (e, run) =>
        {
            var record = e.Run!; var output = RunCommands.Execute(run!, record.RunId, kind, args, e.Memory);
            if (output.NextMemory != null) e.Memory = output.NextMemory;
            record.Journal.Add(new JournalEntry { Kind = kind, Args = args, StateSha256 = PracticeRunStore.StateDigest(run!) });
            if (kind == Contracts.MemoryCommit) ProgressionCommands.AfterMemoryCommit(e, run!, record, id, content);
            return ProgressionCommands.CommandReply(content, record, run!, output, AccountEnvelope.MemoryVersion(e.Memory), kind);
        });
    }
    static async Task<bool> PlayToSave(PracticeRunStore store, FileEnvelopePersistence persistence, PracticeAccount account, PracticeContent content)
    {
        for (int step = 0; step < 1500; step++)
        {
            var choice = await store.Read(account, (e, run) =>
            {
                var s = run!.State; string kind = "proceed"; var args = GraphJson.Object();
                if (s.Phase == "VICTORY" || s.Phase == "DEFEAT") return Reply.Json(200, GraphJson.Object("done", s.Phase == "VICTORY"));
                if (s.Phase == "MAP") { kind = "enter"; args["nodeId"] = run.Available().OrderBy(id => s.Map!.Floors.SelectMany(row => row).Concat(new[] { s.Map.Boss }).First(n => n.Id == id).Type == "rest" ? 0 : 1).First(); }
                else if (s.Phase == "BATTLE")
                {
                    kind = "end"; var b = s.Battle!; string target = b.Enemies.Where(n => n.Alive).OrderBy(n => n.Hp).First().Uid;
                    var actor = b.Actors.FirstOrDefault(a => a.Gauge >= 100);
                    if (actor != null) { kind = "ult"; args = GraphJson.Object("actor", actor.Id, "target", target); }
                    else foreach (string uid in b.Zones.Hand) if (run.BattleCheck(uid, "now", target) == null) { kind = "play"; args = GraphJson.Object("uid", uid, "mode", "now", "target", target); break; }
                }
                else if (s.Phase == "REST" && s.Hp < s.MaxHp && Convert.ToInt64(((IDictionary<string, object?>)s.Pending!)["uses"]) > 0) { kind = "rest"; args["choice"] = "heal"; }
                else if (s.Phase == "EVENT")
                {
                    var pending = (IDictionary<string, object?>)s.Pending!;
                    if (!pending.TryGetValue("done", out var done) || !Equals(done, true))
                    { kind = "event"; args["index"] = (long)(content.Data.Events.Last(x => x.Id == (string)pending["id"]!).Choices.Count - 1); }
                }
                return Reply.Json(200, GraphJson.Object("kind", kind, "args", args));
            });
            using var json = JsonDocument.Parse(choice.Body); if (json.RootElement.TryGetProperty("done", out var done)) return done.GetBoolean();
            string kind = json.RootElement.GetProperty("kind").GetString()!;
            var args = (Dictionary<string, object?>)MemoryV2DataLoader.Graph(json.RootElement.GetProperty("args"))!;
            var record = Envelopes.Decode((await persistence.ReadAsync(account))!, account, content).Run!;
            string identity = GraphJson.Digest(GraphJson.Object("op", "command", "runId", record.RunId, "expectedVersion", record.Journal.Count, "kind", kind, "args", args));
            await Command(store, account, content, Id(), identity, kind, Contracts.CheckArgs(kind, args));
        }
        throw new Exception("battle command bound");
    }
    static async Task CapacityBlocks(PracticeContent content, string path)
    {
        var a = new PracticeAccount("capacity-check", new byte[32], "short", new List<Dictionary<string, object?>> { GraphJson.Object("charId", "nia", "res", 4, "asc", 4) });
        var e = AccountEnvelope.Fresh(a, content);
        var rows = MemoryV2.Baseline(content.Data.Memory, "nia").Select(c => (object?)c.ToJsonGraph()).ToList();
        ((Dictionary<string, object?>)rows[0]!)["plus"] = true;
        rows.AddRange(content.Data.Memory.Battle.Cards.Where(c => c.Owner == "nia" && c.Rarity == "S" && c.Pool != false).Take(2).Select(c => (object?)GraphJson.Object("id", c.Id)));
        e.Memory = MemoryV2.Migrate(content.Data.Memory, GraphJson.Object("nia", rows), GraphJson.Object("nia", 9)).ToJsonGraph();
        Check(((IDictionary<string, object?>)e.Memory["memory"]!).ContainsKey("nia"), "capacity legacy fixture keeps real seven-cost deck");
        var persistence = new FileEnvelopePersistence(path); byte[] source = Envelopes.Encode(e);
        await persistence.TransactAsync(a, source, _ => new Transition(Reply.Json(200, null), source));
        var store = new PracticeRunStore(content, persistence);
        foreach (bool useMemory in new[] { true, false })
        {
            var q = new ExpeditionRequest(Id(), 0, "tutorial", null, null, useMemory);
            await Refused(() => store.Mutate(a, q.RequestId, q.Identity, (candidate, run) => ProgressionCommands.Launch(content, a, candidate, run, q)), "OVER_CAPACITY");
            byte[] after = (await persistence.ReadAsync(a))!; Check(after.SequenceEqual(source), "capacity denial preserves all bytes, both memory modes");
        }
    }

}
