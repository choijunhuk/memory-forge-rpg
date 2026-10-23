// §6/§14.3/§16.2: exact original JS descriptions for authored and actual effective definitions.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MemoryForge.Core;

static class CardTextChecks
{
    public static int Run(string root)
    {
        int checks = 0, failed = 0;
        void Check(bool ok, string label) { checks++; if (!ok) { failed++; Console.WriteLine("FAIL CardText " + label); } }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "reports/card_text_20261016/reference.json")));
            var source = doc.RootElement; RunCreateChecks.UniqueKeys(source);
            Check(source.GetProperty("schema").GetString() == "mf-card-text-v1" && source.GetProperty("count").GetInt32() == 519, "schema/independent519denominator");
            string[] paths = { "engine/rules-core.js", "data/game_data.json", "reports/card_text_20261016/reference_generator.mjs" };
            var pins = source.GetProperty("sources");
            if (!pins.EnumerateObject().Select(p => p.Name).SequenceEqual(paths)) throw new InvalidDataException("Substituted card-text source allowlist");
            foreach (string path in paths)
            {
                using var sha = SHA256.Create();
                string fingerprint = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(root, path)))).Replace("-", "").ToLowerInvariant();
                Check(pins.GetProperty(path).GetString() == fingerprint, "current source " + path);
            }
            var data = BattleDataLoader.Load(root);
            var reader = typeof(BattleDataLoader).GetMethod("Card", BindingFlags.Static | BindingFlags.NonPublic) ?? throw new MissingMethodException("BattleDataLoader.Card");
            var seen = new HashSet<string>(StringComparer.Ordinal); int authored = 0, plus = 0, resonance = 0;
            foreach (var row in source.GetProperty("rows").EnumerateArray())
            {
                string id = row.GetProperty("id").GetString()!, variant = row.GetProperty("variant").GetString()!;
                Check(seen.Add(id + "/" + variant), "unique " + id + " " + variant);
                var card = (BattleCardDef)reader.Invoke(null, new object[] { row.GetProperty("definition") })!;
                string before = JsonSerializer.Serialize(card.ToJsonGraph());
                string actual = BattleText.Card(card, data), expected = row.GetProperty("text").GetString()!;
                Check(actual == expected, id + " " + variant + (actual == expected ? "" : " C# " + actual + " JS " + expected));
                Check(JsonSerializer.Serialize(card.ToJsonGraph()) == before, id + " formatter leaves definition unchanged");
                if (variant == "authored") authored++; else if (variant == "plus") plus++; else if (variant == "resonance6") resonance++; else Check(false, "unknown variant");
            }
            Check(authored == 173 && plus == 173 && resonance == 173 && seen.Count == 519, "all173cards in all3sourcevariants");
        }
        catch (Exception error) { Check(false, error.GetType().Name + ": " + error.Message); }
        Console.WriteLine($"CardTextChecks: {checks - failed}/{checks} passed; original JS173authored +173actualplus +173actualresonance6, strings/purity");
        return failed;
    }
}
