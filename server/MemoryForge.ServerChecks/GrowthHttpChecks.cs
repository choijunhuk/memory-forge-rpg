// S2 HTTP acceptance: real tutorial/story bootstrap and standalone Core battles, not injected wallet/AL/HP/victory fixtures.
// Missing terminal settlement, double spending, mutable launch stats, leaked proof fields or an incomplete offline verifier must fail.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

sealed partial class ServerChecks
{
    const string EncountersPath = "dev/practice/encounters", SweepPath = "dev/practice/dungeons/sweep";
    const string NiaLevelPath = "dev/practice/progression/characters/nia/level-up", NiaPreviewPath = "dev/practice/progression/characters/nia/level-preview";
    static readonly string[] GrowthResourceIds = { "credits", "ink", "oil", "seal", "alloy", "book", "polish", "core", "refine_core" };
    readonly ResponseCapture growthHttpCapture = new ResponseCapture();
    Api GrowthHttpClient(ServerProcess host) => new Api(host.Port, growthHttpCapture);
    static string EncounterBody(string request, long revision, string dungeon, int tier) => new JsonObject
    { ["requestId"] = request, ["expectedRevision"] = revision, ["dungeonId"] = dungeon, ["tier"] = tier, ["party"] = Ids(TutorialCandidates) }.ToJsonString();
    static string LevelBody(string request, long revision, int target) => new JsonObject
    { ["requestId"] = request, ["expectedRevision"] = revision, ["targetLevel"] = target }.ToJsonString();
    static string SweepBody(string request, long revision, int tier, int times) => new JsonObject
    { ["requestId"] = request, ["expectedRevision"] = revision, ["dungeonId"] = "dg_ink", ["tier"] = tier, ["times"] = times }.ToJsonString();

    async Task GrowthHttpFile()
    {
        string directory = Path.Combine(temp, "store-growth-http"), file = AccountFile(directory, "alpha"), token = tokens["alpha"];
        var host = await Start("growth HTTP", root, directory);
        var api = GrowthHttpClient(host);
        var fresh = await HttpProfile(api, token);
        Check(I(fresh.GetProperty("focus"), "value") == 240 && fresh.GetProperty("resources").EnumerateObject().All(p => p.Value.GetInt64() == 0),
            "fresh HTTP growth reads display initial240focus and actual zero materials");
        Check(!Directory.EnumerateFileSystemEntries(directory).Any(), "fresh growth HTTP reads create no files or initialization");
        await ClearProgressionHttp(api, token, file, "tutorial", null);
        await ClearProgressionHttp(api, token, file, "story", TutorialCandidates);
        var profile = await HttpProfile(api, token);
        Check(I(profile.GetProperty("account"), "level") == 5 && I(profile.GetProperty("account"), "xp") == 0,
            "the growth HTTP account earns AL5 through actual tutorial and chapter1 clears");
        byte[] preGrowth = File.ReadAllBytes(file);
        await HttpProfile(api, token);
        Same(File.ReadAllBytes(file), preGrowth, "HTTP focus/resource profile is pure before first growth action");
        Check(!ReadBody(file).TryGetProperty("growth", out _), "S1 rewards never initialize or fabricate S2 resources");
        foreach (var (dungeon, tier, code) in new[] { ("dg_oil", 1, "LOCKED"), ("dg_ink", 0, "UNKNOWN_TIER"), ("not-authored", 1, "UNKNOWN_CONTENT") })
        {
            Status(await api.Post(EncountersPath, token, EncounterBody(Id(), N64(profile, "revision"), dungeon, tier)), 422, code, "growth HTTP boundary " + code);
            Same(File.ReadAllBytes(file), preGrowth, "refused growth starts do not initialize or mutate the account");
        }
        foreach (string field in new[] { "seed", "focus", "memory", "capacity", "hp" })
        {
            var forged = JsonNode.Parse(EncounterBody(Id(), N64(profile, "revision"), "dg_ink", 1))!.AsObject(); forged[field] = 9999;
            Status(await api.Post(EncountersPath, token, forged.ToJsonString()), 400, "UNKNOWN_FIELD", "growth HTTP refuses supplied " + field);
            Same(File.ReadAllBytes(file), preGrowth, "forged launch values never change the account");
        }

        var ink = await StartGrowthHttp(api, token, file, "dg_ink", 1);
        string inkId = S(ink.Json, "encounterId");
        Check(!ink.Json.TryGetProperty("runId", out _) && I(ink.Json.GetProperty("state"), "maxHp") == 183,
            "real growth HTTP starts a standalone three-Level1 Battle, without a fake Run wrapper");
        profile = await HttpProfile(api, token);
        byte[] activeBytes = File.ReadAllBytes(file);
        Status(await api.Post(EncountersPath, token, EncounterBody(Id(), N64(profile, "revision"), "dg_credit", 1)), 409, "RUN_ACTIVE", "only one HTTP encounter may be active");
        Status(await api.Post(ExpeditionsPath, token, ExpeditionBody(Id(), N64(profile, "revision"), "story", TutorialCandidates)), 409, "RUN_ACTIVE", "an HTTP Run cannot start alongside an encounter");
        Status(await api.Post(SweepPath, token, SweepBody(Id(), N64(profile, "revision"), 1, 1)), 409, "RUN_ACTIVE", "sweep cannot spend focus reserved by the active encounter");
        Same(File.ReadAllBytes(file), activeBytes, "all concurrent-active HTTP refusals preserve the account");
        var hand = A(ink.Json.GetProperty("state"), "hand");
        var query = await api.Post(EncountersPath + "/" + inkId + "/battle-query", token, new JsonObject
        { ["expectedVersion"] = 0, ["uid"] = S(hand[0], "uid"), ["mode"] = "now", ["target"] = "auto", ["picks"] = new JsonArray() }.ToJsonString());
        Status(query, 200, null, "HTTP standalone battle query");
        Check(query.Json.TryGetProperty("encounterId", out _) && !query.Json.TryGetProperty("runId", out _), "HTTP growth queries retain standalone context");
        Same(File.ReadAllBytes(file), activeBytes, "HTTP encounter query spends and commits nothing");
        var inkWin = await FinishGrowthHttp(api, token, ink, false);
        CheckSettlement(inkWin.Reply, "VICTORY", 20, 6000, 0, 20);
        profile = await HttpProfile(api, token);
        Check(Resource(profile, "ink") == 6000 && Resource(profile, "credits") == 0 && I(profile.GetProperty("focus"), "value") == 220
            && I(profile.GetProperty("account"), "xp") == 20, "real ink HTTP victory pays6000ink and20ALXP for20focus");
        var credit = await StartGrowthHttp(api, token, file, "dg_credit", 1);
        var creditWin = await FinishGrowthHttp(api, token, credit, false);
        CheckSettlement(creditWin.Reply, "VICTORY", 20, 0, 10000, 20);
        profile = await HttpProfile(api, token);
        Check(Resource(profile, "credits") == 10000 && I(profile.GetProperty("focus"), "value") == 200 && I(profile.GetProperty("account"), "xp") == 40,
            "real credit HTTP victory pays10000credits and20ALXP for20focus");

        var frozen = await StartGrowthHttp(api, token, file, "dg_ink", 1);
        string frozenId = S(frozen.Json, "encounterId"), frozenPublic = Raw(frozen.Json, "state");
        string frozenHash = LastEncounterHash(file);
        profile = await HttpProfile(api, token); long levelRevision = N64(profile, "revision");
        byte[] beforePreview = File.ReadAllBytes(file);
        var preview = await api.Post(NiaPreviewPath, token, new JsonObject { ["expectedRevision"] = levelRevision, ["targetLevel"] = 2 }.ToJsonString());
        Status(preview, 200, null, "HTTP character level preview");
        Check(N64(preview.Json.GetProperty("cost"), "ink") == 200 && N64(preview.Json.GetProperty("cost"), "credits") == 100
            && I(preview.Json.GetProperty("before").GetProperty("stats"), "hp") == 61 && I(preview.Json.GetProperty("after").GetProperty("stats"), "hp") == 62,
            "HTTP Lv1-to2 preview derives independent200ink100credits and61-to62HP");
        Same(File.ReadAllBytes(file), beforePreview, "HTTP growth preview is a pure read");
        Status(await api.Post(NiaLevelPath, token, LevelBody(Id(), levelRevision, 21)), 422, "LEVEL_CAP", "HTTP level purchase respects ascension0cap20");
        Same(File.ReadAllBytes(file), beforePreview, "over-cap HTTP growth consumes no resources");
        string levelBody = LevelBody(Id(), levelRevision, 2);
        var level = await api.Post(NiaLevelPath, token, levelBody);
        Status(level, 200, null, "real HTTP character level purchase during an active battle");
        profile = await HttpProfile(api, token);
        Check(Resource(profile, "ink") == 5800 && Resource(profile, "credits") == 9900, "HTTP level-up preserves all unused ink and exact credit cost");
        var unchanged = await api.Get(EncountersPath + "/" + frozenId, token);
        Status(unchanged, 200, null, "HTTP active battle after account level-up");
        Same(Raw(unchanged.Json, "state"), frozenPublic, "leveling a companion does not rewrite active HTTP combat state");
        Same(LastEncounterHash(file), frozenHash, "level-up preserves the active encounter's raw creation witness");
        byte[] leveledBytes = File.ReadAllBytes(file);
        Same((await api.Post(NiaLevelPath, token, levelBody)).Body, level.Body, "HTTP level exact retry returns the receipt without spending again");
        Status(await api.Post(NiaLevelPath, token, LevelBody(Id(), levelRevision, 2)), 409, "STALE_REVISION", "a new level request cannot reuse an obsolete account revision");
        Same(File.ReadAllBytes(file), leveledBytes, "cached level retry and stale new ID change no account bytes");
        await FinishGrowthHttp(api, token, frozen, false);
        var next = await StartGrowthHttp(api, token, file, "dg_credit", 1);
        Check(I(next.Json.GetProperty("state"), "maxHp") == 184, "the NEXT real HTTP Battle uses Nia's earned Level2 and184partyHP");
        await FinishGrowthHttp(api, token, next, false);

        var losing = await StartGrowthHttp(api, token, file, "dg_ink", 6);
        profile = await HttpProfile(api, token);
        string beforeLossResources = Raw(profile, "resources"), beforeLossAccount = Raw(profile, "account");
        int beforeLossFocus = I(profile.GetProperty("focus"), "value");
        var loss = await FinishGrowthHttp(api, token, losing, true);
        CheckSettlement(loss.Reply, "DEFEAT", 0, 0, 0, 0);
        profile = await HttpProfile(api, token);
        Same(Raw(profile, "resources"), beforeLossResources, "actual tier6 HTTP defeat grants no resources");
        Same(Raw(profile, "account"), beforeLossAccount, "actual tier6 HTTP defeat grants no account XP");
        Check(I(profile.GetProperty("focus"), "value") == beforeLossFocus && profile.GetProperty("activeEncounter").ValueKind == JsonValueKind.Null,
            "actual tier6 HTTP defeat costs0focus and closes its encounter");

        // Earn this exact tier's sweep right through actual winning Battle commands, never by editing bestStars.
        for (int attempt = 0; GrowthStars(profile, 1) < 3 && attempt < 3; attempt++)
        {
            var attemptStart = await StartGrowthHttp(api, token, file, "dg_ink", 1);
            await FinishGrowthHttp(api, token, attemptStart, false);
            profile = await HttpProfile(api, token);
        }
        Check(GrowthStars(profile, 1) == 3, "actual HTTP ink tier1 victory earns3stars and its sweep right");
        byte[] beforeSweepRefusal = File.ReadAllBytes(file);
        Status(await api.Post(SweepPath, token, SweepBody(Id(), N64(profile, "revision"), 2, 1)), 422, "SWEEP_LOCKED", "tier1stars never unlock another tier's HTTP sweep");
        Status(await api.Post(SweepPath, token, SweepBody(Id(), N64(profile, "revision"), 1, 13)), 422, "INVALID_SWEEP", "HTTP sweep obeys the maximum12count");
        Same(File.ReadAllBytes(file), beforeSweepRefusal, "invalid HTTP sweeps change neither focus nor grants");
        long beforeInk = Resource(profile, "ink"), beforeCredits = Resource(profile, "credits"), revision = N64(profile, "revision");
        int beforeSweepFocus = I(profile.GetProperty("focus"), "value"), beforeSweepXp = I(profile.GetProperty("account"), "xp");
        var second = GrowthHttpClient(host);
        string[] sweepBodies = { SweepBody(Id(), revision, 1, 1), SweepBody(Id(), revision, 1, 1) };
        var competing = await Task.WhenAll(api.Post(SweepPath, token, sweepBodies[0]), second.Post(SweepPath, token, sweepBodies[1]));
        Check(competing.Count(r => r.Status == 200) == 1 && competing.Count(r => r.Status == 409 && r.Code == "STALE_REVISION") == 1,
            "two real HTTP clients competing at one revision commit exactly one sweep");
        int winner = Array.FindIndex(competing, r => r.Status == 200);
        var swept = competing[winner];
        profile = await HttpProfile(api, token);
        Check(Resource(profile, "ink") == beforeInk + 6000 && Resource(profile, "credits") == beforeCredits
            && I(profile.GetProperty("focus"), "value") == beforeSweepFocus - 20 && I(profile.GetProperty("account"), "xp") == beforeSweepXp + 20
            && N64(profile, "revision") == revision + 1, "the concurrent HTTP sweep has one exact reward,20focus debit and account revision");
        byte[] sweptBytes = File.ReadAllBytes(file);
        Same((await api.Post(SweepPath, token, sweepBodies[winner])).Body, swept.Body, "HTTP sweep retry grants once and returns identical bytes");
        Same(File.ReadAllBytes(file), sweptBytes, "HTTP sweep retry writes nothing");

        host.Kill(); host = await Start("growth HTTP after restart", root, directory); api = GrowthHttpClient(host);
        Same((await api.Post(NiaLevelPath, token, levelBody)).Body, level.Body, "the old HTTP level receipt remains byte-identical after further battles and restart");
        Same((await api.Post(inkWin.Route, token, inkWin.Body)).Body, inkWin.Reply.Body, "the winning-command settlement receipt survives process restart exactly");
        Same((await api.Post(SweepPath, token, sweepBodies[winner])).Body, swept.Body, "the sweep receipt survives process restart exactly");
        var restored = await HttpProfile(api, token);
        Same(Raw(restored, "resources"), Raw(profile, "resources"), "process restart reconstructs the exact earned HTTP resource wallet");
        Same(Raw(restored, "account"), Raw(profile, "account"), "process restart reconstructs exact earned HTTP account XP");
        Same(File.ReadAllBytes(file), sweptBytes, "restart reads and cached HTTP receipts grant and write nothing");
        host.Kill();
        string beforeVerify = StoreSnapshot(directory);
        var verified = await RunVerifier("the earned S2 HTTP histories", Config(root, directory));
        Same(verified.Exit, 0, "--verify-store passes actual HTTP S1+S2 histories");
        SameVerifyResult(verified, "PASS", 3, 1, 2, 0, 0, "the earned S2 HTTP histories");
        var report = VerifyLine(verified, "account", "alpha");
        int growthReceipts = A(ReadBody(file).GetProperty("growth"), "ledger").Count(f => S(f, "kind") != "growthInitialize");
        CheckLine(VerifyNumber(report, "growthReceipts") == growthReceipts && growthReceipts > 0, report,
            "the offline verifier accounts for EVERY typed growth HTTP receipt");
        Same(StoreSnapshot(directory), beforeVerify, "S2 --verify-store changes no bytes, permissions, mtimes or files");
        GrowthHttpLeakScan();
        completed.Add("growth-http");
    }

    static long Resource(JsonElement profile, string id) => N64(profile.GetProperty("resources"), id);
    static int GrowthStars(JsonElement profile, int tier) => I(A(A(profile, "dungeons").Single(d => S(d, "id") == "dg_ink"), "tiers").Single(t => I(t, "tier") == tier), "bestStars");
    static string LastEncounterHash(string file) => S(A(ReadBody(file).GetProperty("growth"), "encounters").Last(), "createdSha256");
    async Task<Result> StartGrowthHttp(Api api, string token, string file, string dungeon, int tier)
    {
        var profile = await HttpProfile(api, token);
        var result = await api.Post(EncountersPath, token, EncounterBody(Id(), N64(profile, "revision"), dungeon, tier));
        Status(result, 200, null, "real HTTP dungeon start");
        seeds.Add(A(ReadBody(file).GetProperty("growth"), "encounters").Last().GetProperty("seed").GetInt64());
        return result;
    }
    async Task<ProgressionHttpSave> FinishGrowthHttp(Api api, string token, Result started, bool onlyEnd)
    {
        string id = S(started.Json, "encounterId"), route = EncountersPath + "/" + id + "/commands";
        var current = started; var rejected = new HashSet<string>(StringComparer.Ordinal);
        for (int guard = 0; guard < 300; guard++)
        {
            var state = current.Json.GetProperty("state"); int version = I(current.Json, "version");
            var command = onlyEnd ? (Kind: "end", Args: new JsonObject(), Key: "end") : Choose(state, rejected, version) ?? ("end", new JsonObject(), "end");
            string body = new JsonObject { ["requestId"] = Id(), ["expectedVersion"] = version, ["kind"] = command.Kind, ["args"] = command.Args }.ToJsonString();
            var response = await api.Post(route, token, body);
            if (response.Status == 422 && (command.Kind == "play" || command.Kind == "ult")) { rejected.Add(version.ToString(CultureInfo.InvariantCulture) + ":" + command.Key); continue; }
            Status(response, 200, null, "real HTTP standalone " + command.Kind);
            current = response;
            if (S(current.Json, "phase") == "VICTORY" || S(current.Json, "phase") == "DEFEAT")
            {
                Check(S(current.Json, "phase") == (onlyEnd ? "DEFEAT" : "VICTORY"), "actual HTTP standalone battle reaches the expected real outcome");
                return new ProgressionHttpSave(route, body, response);
            }
        }
        throw new CheckFailure("the real HTTP encounter did not finish within300 commands");
    }
    void CheckSettlement(Result result, string outcome, int focus, long ink, long credits, int xp)
    {
        var s = result.Json.GetProperty("settlement");
        var grant = s.GetProperty("grant");
        Check(S(s, "outcome") == outcome && I(s.GetProperty("cost"), "focus") == focus && N64(grant.GetProperty("resources"), "ink") == ink
            && N64(grant.GetProperty("resources"), "credits") == credits && I(grant, "accountXp") == xp,
            "the terminal HTTP command atomically acknowledges exact outcome,focus,resources and account XP");
    }

    void GrowthHttpLeakScan()
    {
        var secrets = new Secrets(tokens.Values.ToArray(), seeds.Where(s => s >= 1000000).Select(s => s.ToString(CultureInfo.InvariantCulture)).ToArray());
        Check(GrowthHttpLeak("{\"revision\":1,\"profile\":{\"revision\":2}}", secrets) == null
            && GrowthHttpLeak("{\"state\":{\"revision\":2}}", secrets) != null
            && GrowthHttpLeak("{\"other\":{\"serverNowMs\":1}}", secrets) != null
            && GrowthHttpLeak("{\"settlement\":{\"grant\":{\"rng\":[1]}}}", secrets) != null,
            "the growth HTTP privacy scan restricts explicit profile revision,clock and grant exceptions");
        int scanned = 0;
        foreach (string body in growthHttpCapture.Bodies)
        {
            string? leak = GrowthHttpLeak(body, secrets);
            if (leak != null) { Record("growth HTTP private response at " + leak, body); throw new CheckFailure("private growth HTTP response (path in diagnostics.log)"); }
            scanned++;
        }
        Check(scanned > 0 && scanned == growthHttpCapture.Started, "every growth HTTP response was captured and strictly checked");
    }
    static string? GrowthHttpLeak(string body, Secrets secrets)
    {
        if (secrets.Tokens.Any(t => body.Contains(t, StringComparison.Ordinal))) return "credential text";
        JsonNode? node;
        try { node = JsonNode.Parse(body); } catch (JsonException) { return "non-JSON"; }
        if (node is JsonObject root)
        {
            if (!WithoutRevision(root)) return "$.revision";
            if (root["profile"] is JsonObject profile && !WithoutRevision(profile)) return "$.profile.revision";
        }
        string normalized = node?.ToJsonString() ?? "null";
        string? common = ResponseLeak(normalized, secrets); if (common != null) return common;
        using var document = JsonDocument.Parse(normalized);
        return GrowthHttpContextLeak(document.RootElement, "$");
    }
    static string? GrowthHttpContextLeak(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                string at = path + "." + SafeName(property.Name);
                if (new[] { "ledger", "draws", "tape", "debit", "expectedRevision", "coreModule", "contentSha256", "policyHash", "accountAtStart" }.Contains(property.Name, StringComparer.Ordinal)) return at;
                if ((property.Name == "serverNowMs" || property.Name == "nextRegenAtMs") && path != "$.focus" && path != "$.profile.focus") return at;
                if (property.Name == "grant")
                {
                    if (path != "$.settlement" && path != "$") return at;
                    var g = property.Value;
                    if (g.ValueKind != JsonValueKind.Object || !g.EnumerateObject().Select(p => p.Name).OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(new[] { "accountXp", "resources" })
                        || !g.GetProperty("accountXp").TryGetInt64(out long xp) || xp < 0 || g.GetProperty("resources").ValueKind != JsonValueKind.Object
                        || !g.GetProperty("resources").EnumerateObject().Select(p => p.Name).OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(GrowthResourceIds.OrderBy(p => p, StringComparer.Ordinal))
                        || g.GetProperty("resources").EnumerateObject().Any(p => !p.Value.TryGetInt64(out long value) || value < 0)) return at;
                }
                string? nested = GrowthHttpContextLeak(property.Value, at); if (nested != null) return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int i = 0; foreach (var item in element.EnumerateArray()) { string? nested = GrowthHttpContextLeak(item, path + "[" + i++ + "]"); if (nested != null) return nested; }
        }
        return null;
    }
}
