using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MemoryForge.Core;
using MemoryForge.Server;
static class S1Journey
{
    static string Id() => Guid.NewGuid().ToString("N");
    internal static async Task<Reply> Command(PracticeRunStore store, PracticeAccount account, PracticeContent content, string id, string identity, string kind, Dictionary<string, object?> args)
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
    internal static async Task<bool> PlayToSave(PracticeRunStore store, FileEnvelopePersistence persistence, PracticeAccount account, PracticeContent content)
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
}
