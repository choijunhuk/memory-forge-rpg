using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
namespace MemoryForge.Server
{
    sealed record GrowthRequest(string RequestId, long ExpectedRevision, string Kind, Dictionary<string, object?> Args)
    {
        public string Identity => GraphJson.Digest(GraphJson.Object("op", Kind, "expectedRevision", ExpectedRevision, "args", Args));
        public Dictionary<string, object?> ToGraph() => GraphJson.Object("requestId", RequestId, "expectedRevision", ExpectedRevision, "args", Args);
    }
    static partial class Contracts
    {
        public static GrowthRequest ParseEncounter(byte[] bytes)
        {
            using var d = Document(bytes); var m = Members(d.RootElement, "requestId", "expectedRevision", "dungeonId", "tier", "party");
            string id = RequestId(m), dungeon = Reference(m, "dungeonId"); long revision = Revision(m, "expectedRevision"); int tier = Version(m, "tier");
            if (!m.TryGetValue("party", out var p) || p.ValueKind != JsonValueKind.Array || p.GetArrayLength() > 32) throw new ApiError(400, "INVALID_REQUEST");
            return new GrowthRequest(id, revision, "encounterStart", GraphJson.Object("dungeonId", dungeon, "tier", tier, "party", p.EnumerateArray().Select(x => (object?)StringValue(x, "INVALID_REQUEST")).ToList()));
        }
        public static CommandRequest ParseEncounterCommand(byte[] bytes, string id)
        {
            var q = ParseCommand(bytes, id); if (q.Kind != "play" && q.Kind != "ult" && q.Kind != "end"&&q.Kind!="support") throw new ApiError(400, "UNSUPPORTED_COMMAND");
            return q with { Identity = EncounterIdentity(id, q.ExpectedVersion, q.Kind, q.Args) };
        }
        public static string EncounterIdentity(string id, int version, string kind, Dictionary<string, object?> args) => GraphJson.Digest(GraphJson.Object("op", "encounterCommand", "encounterId", id, "expectedVersion", version, "kind", kind, "args", args));
        public static GrowthRequest ParseSweep(byte[] bytes)
        {
            using var d = Document(bytes); var m = Members(d.RootElement, "requestId", "expectedRevision", "dungeonId", "tier", "times");
            return new GrowthRequest(RequestId(m), Revision(m, "expectedRevision"), "dungeonSweep", GraphJson.Object("dungeonId", Reference(m, "dungeonId"), "tier", Version(m, "tier"), "times", Version(m, "times")));
        }
        public static GrowthRequest ParseLevel(byte[] bytes, string character, bool preview)
        {
            using var d = Document(bytes); var m = preview ? Members(d.RootElement, "expectedRevision", "targetLevel") : Members(d.RootElement, "requestId", "expectedRevision", "targetLevel");
            return new GrowthRequest(preview ? "preview-only" : RequestId(m), Revision(m, "expectedRevision"), "characterLevelUp", GraphJson.Object("charId", character, "targetLevel", Version(m, "targetLevel")));
        }
    }
}
