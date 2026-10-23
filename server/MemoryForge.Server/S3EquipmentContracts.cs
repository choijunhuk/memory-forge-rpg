using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
namespace MemoryForge.Server
{
    static partial class Contracts
    {
        // S3 action kinds → (target key, extra body fields). Preview bodies are the same fields without requestId.
        public static readonly Dictionary<string, (string? Target, string[] Fields)> EquipmentActions = new Dictionary<string, (string?, string[])>(System.StringComparer.Ordinal)
        {
            ["characterAscend"] = ("charId", new string[0]), ["skillUp"] = ("charId", new[] { "line" }), ["gearCraft"] = ("charId", new[] { "slot" }), ["gearEnhance"] = ("charId", new[] { "slot" }),
            ["weaponLevelUp"] = ("instanceId", new[] { "targetLevel" }), ["weaponAscend"] = ("instanceId", new string[0]), ["weaponRefine"] = ("instanceId", new string[0]),
            ["weaponEquip"] = ("instanceId", new[] { "charId" }), ["bossExchange"] = (null, new[] { "itemId", "quantity" }),
        };
        public static GrowthRequest ParseEquipment(byte[] bytes, string kind, string? target, bool preview)
        {
            var spec = EquipmentActions[kind]; using var d = Document(bytes);
            var allowed = new List<string> { "expectedRevision" }; if (!preview) allowed.Add("requestId"); allowed.AddRange(spec.Fields);
            var m = Members(d.RootElement, allowed.ToArray()); var args = GraphJson.Object();
            if (spec.Target != null) { if (target == null) throw new ApiError(400, "INVALID_REQUEST"); args.Add(spec.Target, target); }
            foreach (string field in spec.Fields)
            {
                if (field == "targetLevel" || field == "quantity") args.Add(field, Version(m, field));
                else if (kind == "weaponEquip") { if (!m.TryGetValue(field, out var raw)) throw new ApiError(400, "INVALID_REQUEST"); args.Add(field, raw.ValueKind == JsonValueKind.Null ? null : StringValue(raw, "INVALID_REQUEST")); }
                else args.Add(field, Reference(m, field));
            }
            return new GrowthRequest(preview ? "preview-only" : RequestId(m), Revision(m, "expectedRevision"), kind, args);
        }
        // Stored facts keep args with the path target; rebuild the public body and re-run the strict parser.
        public static GrowthRequest StoredEquipment(string kind, string requestId, long revision, Dictionary<string, object?> stored)
        {
            var spec = EquipmentActions[kind]; var raw = (Dictionary<string, object?>)GraphJson.Copy(stored)!; string? target = null;
            if (spec.Target != null) { target = raw[spec.Target] as string ?? throw new System.IO.InvalidDataException("equipment target"); raw.Remove(spec.Target); }
            if (raw.Keys.Any(k => !spec.Fields.Contains(k)) || raw.Count != spec.Fields.Length) throw new System.IO.InvalidDataException("equipment args");
            raw.Add("requestId", requestId); raw.Add("expectedRevision", revision);
            return ParseEquipment(GraphJson.Bytes(raw, false), kind, target, false);
        }
    }
}
