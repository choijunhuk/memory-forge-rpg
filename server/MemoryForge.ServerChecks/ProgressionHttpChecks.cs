// S1 HTTP acceptance: real child host, untouched content and test-only credentials/store. A removed live memory-commit grant hook,
// a fake temporary-party ownership grant, a duplicate grant on retry, or mutable launch inputs must fail these checks.
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
    const string ProgressionPath = "dev/practice/progression", ExpeditionsPath = "dev/practice/expeditions";
    readonly ResponseCapture progressionHttpCapture = new ResponseCapture();

    Api ProgressionHttpClient(ServerProcess host) => new Api(host.Port, progressionHttpCapture);
    static string ExpeditionBody(string id, long revision, string mode, IEnumerable<string>? party = null)
    {
        var body = new JsonObject { ["requestId"] = id, ["expectedRevision"] = revision, ["mode"] = mode,
            ["useMemory"] = true, ["chapter"] = mode == "tutorial" ? 0 : 1 };
        if (party != null) body["party"] = Ids(party);
        return body.ToJsonString();
    }

    async Task<JsonElement> HttpProfile(Api api, string token)
    {
        var result = await api.Get(ProgressionPath, token);
        Status(result, 200, null, "progression HTTP profile");
        return result.Json;
    }

    async Task ProgressionHttpFile()
    {
        string directory = Path.Combine(temp, "store-progression-http"), file = AccountFile(directory, "alpha"), token = tokens["alpha"];
        var host = await Start("progression HTTP", root, directory);
        var api = ProgressionHttpClient(host);
        var empty = await HttpProfile(api, token);
        Check(!empty.GetProperty("initialized").GetBoolean() && N64(empty, "revision") == 0
            && A(empty, "characters").Count == 0 && A(empty, "weapons").Count == 0, "an HTTP read starts uninitialized without fake owned characters or weapons");
        Check(!Directory.EnumerateFileSystemEntries(directory).Any(), "the uninitialized HTTP profile creates no account or lock file");
        Status(await api.Get(ProgressionPath, null), 401, "UNAUTHORIZED", "progression HTTP requires authentication");
        var invalid = JsonNode.Parse(ExpeditionBody(Id(), 0, "tutorial"))!.AsObject();
        invalid["power4"] = 9999;
        Status(await api.Post(ExpeditionsPath, token, invalid.ToJsonString()), 400, "UNKNOWN_FIELD", "an HTTP expedition refuses forged stats");
        Status(await api.Post(ExpeditionsPath, token, ExpeditionBody(Id(), 0, "story", TutorialCandidates)), 422, "TUTORIAL_REQUIRED", "owned HTTP departure needs a verified tutorial");
        Check(!File.Exists(file), "all pre-tutorial HTTP refusals preserve the absent account");

        // Start and abandon a genuine tutorial before any clear: temporary use must never award ownership or resources.
        string openingId = Id(), openingBody = ExpeditionBody(openingId, 0, "tutorial");
        var opening = await api.Post(ExpeditionsPath, token, openingBody);
        Status(opening, 200, null, "HTTP tutorial departure");
        string abandoned = S(opening.Json, "runId");
        RememberProgressionSeed(file);
        var launch = ReadBody(file).GetProperty("run").GetProperty("launch");
        Check(A(launch, "party").Select(p => S(p, "charId")).SequenceEqual(TutorialCandidates)
            && A(launch, "party").All(p => I(p, "level") == 1 && I(p, "asc") == 0 && I(p, "res") == 0 && I(p, "skillLv") == 1 && I(p, "ultLv") == 1),
            "the real HTTP tutorial records its fixed temporary Level1 party");
        var beforeAbandon = await HttpProfile(api, token);
        Check(A(beforeAbandon, "characters").Count == 0 && A(beforeAbandon, "weapons").Count == 0
            && !beforeAbandon.GetProperty("tutorial").GetProperty("completed").GetBoolean(), "starting the temporary tutorial grants no owned collection");
        byte[] beforeRefusal = File.ReadAllBytes(file);
        Status(await api.Post(ExpeditionsPath, token, ExpeditionBody(Id(), N64(beforeAbandon, "revision"), "tutorial")), 409, "RUN_ACTIVE", "a second HTTP expedition is refused");
        Same(File.ReadAllBytes(file), beforeRefusal, "an active-run refusal changes no HTTP account bytes");
        string abandonId = Id(), abandonBody = Obj(("requestId", abandonId), ("expectedVersion", 0));
        var ended = await api.Post(RunsPath + "/" + abandoned + "/abandon", token, abandonBody);
        Status(ended, 200, null, "HTTP tutorial abandon");
        Check(ended.Json.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(new[] { "memoryVersion", "runId", "version" }) && I(ended.Json, "version") == 1 && I(ended.Json, "memoryVersion") == 0,
            "HTTP abandon has exactly the acknowledged run/version/memoryVersion fields");
        Status(await api.Get(RunsPath + "/" + abandoned, token), 404, "RUN_NOT_FOUND", "HTTP abandoned run is no longer current");
        Status(await Command(api, token, abandoned, Id(), 0, "end", new JsonObject()), 404, "RUN_NOT_FOUND", "new HTTP commands cannot reactivate an abandoned run");
        var account = await api.Get(AccountPath, token);
        Status(account, 200, null, "HTTP account after abandon");
        Check(account.Json.GetProperty("run").ValueKind == JsonValueKind.Null, "HTTP account read confirms current run null");
        var afterAbandon = await HttpProfile(api, token);
        Check(A(afterAbandon, "characters").Count == 0 && A(afterAbandon, "weapons").Count == 0 && I(afterAbandon.GetProperty("account"), "level") == 1,
            "an unsaved abandoned tutorial pays no starter or account-XP grant");
        byte[] abandonedBytes = File.ReadAllBytes(file);
        Same((await api.Post(RunsPath + "/" + abandoned + "/abandon", token, abandonBody)).Body, ended.Body, "HTTP abandon exact retry returns its receipt");
        Same((await api.Post(ExpeditionsPath, token, openingBody)).Body, opening.Body, "an old HTTP create receipt does not reopen its abandoned run");
        Same(File.ReadAllBytes(file), abandonedBytes, "HTTP retries do not mutate the abandoned account");

        // Neither a test-only Core callback nor a fabricated terminal state can satisfy this live HTTP clear-and-save path.
        var tutorial = await ClearProgressionHttp(api, token, file, "tutorial", null);
        var trained = await HttpProfile(api, token);
        Check(I(trained.GetProperty("account"), "level") == 3 && I(trained.GetProperty("account"), "xp") == 100
            && trained.GetProperty("tutorial").GetProperty("completed").GetBoolean(), "the LIVE HTTP memory-commit hook awards exactly320 tutorial account XP");
        Check(A(trained, "characters").Select(c => S(c, "charId")).OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(TutorialCandidates.OrderBy(x => x, StringComparer.Ordinal)) && A(trained, "characters").All(c => I(c, "copies") == 1),
            "the LIVE tutorial grant owns exactly the three starter copies");
        Check(A(trained, "weapons").Select(w => S(w, "weaponId")).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[]
            { "w_basic_device", "w_basic_melee", "w_basic_ranged", "w_basic_ritual" }), "the LIVE tutorial grants exactly four distinct basic R instances");
        var recruitApi = RecruitClient(host);
        var recruit = await RecruitRead(recruitApi, token);
        Status(recruit, 200, null, "HTTP recruitment after the tutorial grant");
        Check(N64(recruit.Json.GetProperty("wallet"), "freeCrystals") == 300 && I(recruit.Json.GetProperty("wallet").GetProperty("tickets"), "standardTicket") == 5,
            "the LIVE tutorial grants only its approved300crystals and5tickets");
        byte[] tutorialBytes = File.ReadAllBytes(file);
        Same((await api.Post(tutorial.Route, token, tutorial.Body)).Body, tutorial.Reply.Body, "HTTP tutorial save retry preserves the exact committed reply");
        Status(await api.Post(ExpeditionsPath, token, ExpeditionBody(Id(), N64(trained, "revision"), "tutorial")), 422, "TUTORIAL_DONE", "a completed HTTP tutorial cannot grant again");
        Same(File.ReadAllBytes(file), tutorialBytes, "save retry and refused tutorial re-entry award nothing twice");
        foreach (var refusedParty in new[] { new[] { "nia", "harin" }, new[] { "nia", "nia", "harin" }, new[] { "nia", "harin", "sera" } })
        {
            string code = refusedParty.Length != 3 ? "PARTY_SIZE" : refusedParty.Distinct(StringComparer.Ordinal).Count() != 3 ? "PARTY_DUPLICATE" : "NOT_OWNED";
            Status(await api.Post(ExpeditionsPath, token, ExpeditionBody(Id(), N64(trained, "revision"), "story", refusedParty)), 422, code, "HTTP owned-party boundary " + code);
            Same(File.ReadAllBytes(file), tutorialBytes, "an invalid owned HTTP party preserves all account bytes");
        }

        string[] ordered = { "rien", "nia", "harin" };
        var story = await ClearProgressionHttp(api, token, file, "story", ordered);
        var grown = await HttpProfile(api, token);
        Check(I(grown.GetProperty("account"), "level") == 5 && I(grown.GetProperty("account"), "xp") == 0,
            "the real HTTP chapter1 first clear adds200XP and reaches AL5");
        Check(A(grown, "lastParty").Select(c => c.GetString()).SequenceEqual(ordered), "the HTTP owned story retains the explicit party order");
        Check(A(grown, "characters").All(c => I(c, "copies") == 1) && A(grown, "weapons").Count == 4, "chapter1 does not grant the tutorial collection again");
        recruit = await RecruitRead(recruitApi, token);
        Status(recruit, 200, null, "HTTP recruitment after chapter1");
        Check(N64(recruit.Json.GetProperty("wallet"), "freeCrystals") == 900 && I(recruit.Json.GetProperty("wallet").GetProperty("tickets"), "standardTicket") == 7,
            "chapter1 adds its exact600crystals and2tickets");

        var next = await api.Post(ExpeditionsPath, token, ExpeditionBody(Id(), N64(grown, "revision"), "story", ordered));
        Status(next, 200, null, "next HTTP owned departure");
        string nextId = S(next.Json, "runId"); RememberProgressionSeed(file);
        var record = ReadBody(file).GetProperty("run");
        string frozen = Raw(record, "launch"), beforePullRun = Raw(next.Json, "state");
        var nextLaunch = record.GetProperty("launch");
        Check(A(nextLaunch, "party").Select(p => S(p, "charId")).SequenceEqual(ordered)
            && I(nextLaunch, "stageLevel") == 8 && I(nextLaunch.GetProperty("difficulty"), "hp") == 130 && I(nextLaunch.GetProperty("difficulty"), "dmg") == 110
            && IsPresetPin(nextLaunch) && IsPresetPin(LaunchFactOf(ReadBody(file), nextId)),
            "the HTTP owned launch pins party order, stage8 and the registered phase2 preset's 130/110 in both its Run record and its launch fact");
        var known = await RecruitRead(recruitApi, token);
        Status(known, 200, null, "HTTP recruitment revision before later pull");
        var pulled = await RecruitPull(recruitApi, token, PullBody(Id(), N64(known.Json, "revision"), "standard", 1));
        Status(pulled, 200, null, "a legitimate later HTTP recruitment pull");
        var afterPull = await api.Get(RunsPath + "/" + nextId, token);
        Status(afterPull, 200, null, "HTTP run after mutable account recruitment");
        Same(Raw(afterPull.Json, "state"), beforePullRun, "later recruitment cannot rewrite the active HTTP run");
        Same(Raw(ReadBody(file).GetProperty("run"), "launch"), frozen, "later recruitment preserves immutable launch inputs");
        host.Kill();
        host = await Start("progression HTTP after restart", root, directory); api = ProgressionHttpClient(host);
        var restored = await api.Get(RunsPath + "/" + nextId, token);
        Status(restored, 200, null, "HTTP owned run after process restart");
        Same(Raw(restored.Json, "state"), beforePullRun, "restart replays the exact frozen owned HTTP run");
        Same((await api.Post(story.Route, token, story.Body)).Body, story.Reply.Body, "chapter1 save receipt survives later recruitment and process restart");
        var final = await HttpProfile(api, token);
        Check(I(final.GetProperty("account"), "level") == 5 && I(final.GetProperty("account"), "xp") == 0,
            "receipt replay and process restart never duplicate chapter XP");
        host.Kill();
        ProgressionHttpLeakScan();
        completed.Add("progression-http");
    }

    sealed record ProgressionHttpSave(string Route, string Body, Result Reply);
    async Task<ProgressionHttpSave> ClearProgressionHttp(Api api, string token, string file, string mode, string[]? party)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var profile = await HttpProfile(api, token);
            var start = await api.Post(ExpeditionsPath, token, ExpeditionBody(Id(), N64(profile, "revision"), mode, party));
            Status(start, 200, null, "real HTTP " + mode + " attempt");
            string runId = S(start.Json, "runId"); RememberProgressionSeed(file);
            var played = await PlayToDefeat(api, token, runId, start.Json.GetProperty("state"), new List<string>(), keepWinning: true);
            var terminal = played.State;
            string requestId = Id();
            string body = new JsonObject { ["requestId"] = requestId, ["expectedVersion"] = I(terminal, "version"),
                ["expectedMemoryVersion"] = I(terminal, "memoryVersion"), ["selectedChangeIds"] = new JsonArray() }.ToJsonString();
            string route = RunsPath + "/" + runId + "/memory-commit";
            var save = await api.Post(route, token, body);
            Status(save, 200, null, "real HTTP terminal memory commit");
            if (S(terminal, "phase") == "VICTORY" && S(terminal.GetProperty("result"), "outcome") == "clear")
                return new ProgressionHttpSave(route, body, save);
            Check(I((await HttpProfile(api, token)).GetProperty("account"), "level") == (mode == "tutorial" ? 1 : 3),
                "a genuine defeated HTTP attempt pays no first-clear XP");
        }
        throw new CheckFailure("the public HTTP check bot could not clear " + mode + " within12 genuine attempts");
    }

    void RememberProgressionSeed(string file) => seeds.Add(ReadBody(file).GetProperty("run").GetProperty("seed").GetInt64());

    void ProgressionHttpLeakScan()
    {
        var secrets = new Secrets(tokens.Values.ToArray(), seeds.Where(s => s >= 1000000).Select(s => s.ToString(CultureInfo.InvariantCulture)).ToArray());
        Check(ProgressionHttpLeak("{\"revision\":1,\"account\":{\"level\":3}}", secrets) == null
            && ProgressionHttpLeak("{\"account\":{\"revision\":1}}", secrets) != null
            && ProgressionHttpLeak("{\"revision\":\"1\"}", secrets) != null
            && ProgressionHttpLeak("{\"ledger\":[]}", secrets) != null
            && ProgressionHttpLeak("{\"rng\":[1,2,3,4]}", secrets) != null,
            "HTTP profile privacy allows only an integer ROOT revision and retains nested/private checks");
        int scanned = 0;
        foreach (string body in progressionHttpCapture.Bodies)
        {
            string? leak = ProgressionHttpLeak(body, secrets);
            if (leak != null) { Record("progression HTTP response leak at " + leak, body); throw new CheckFailure("private progression HTTP response (path in diagnostics.log)"); }
            scanned++;
        }
        Check(scanned > 0 && scanned == progressionHttpCapture.Started, "every progression HTTP response was captured and checked without broad private-field exemptions");
    }
    static string? ProgressionHttpLeak(string body, Secrets secrets)
    {
        if (secrets.Tokens.Any(t => body.Contains(t, StringComparison.Ordinal))) return "credential text";
        JsonNode? node;
        try { node = JsonNode.Parse(body); }
        catch (JsonException) { return "non-JSON"; }
        if (node is JsonObject reply && !WithoutRevision(reply)) return "$.revision";
        string normalized = node?.ToJsonString() ?? "null";
        string? common = ResponseLeak(normalized, secrets);
        if (common != null) return common;
        using var document = JsonDocument.Parse(normalized);
        return PrivateProgressionHttpMember(document.RootElement, "$");
    }
    static string? PrivateProgressionHttpMember(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                string at = path + "." + SafeName(property.Name);
                if (new[] { "ledger", "draws", "tape", "debit", "grant", "expectedRevision", "coreModule", "contentSha256" }.Contains(property.Name, StringComparer.Ordinal)) return at;
                string? nested = PrivateProgressionHttpMember(property.Value, at);
                if (nested != null) return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var item in element.EnumerateArray())
            {
                string? nested = PrivateProgressionHttpMember(item, path + "[" + index++ + "]");
                if (nested != null) return nested;
            }
        }
        return null;
    }
}
