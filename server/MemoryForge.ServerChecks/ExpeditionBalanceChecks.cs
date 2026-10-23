// Phase2 launch balance acceptance (server/MemoryForge.Server/ExpeditionBalancePolicy.cs): a real child host on a fresh owner-only store
// with the test-only fixture and credentials; no real account path is read or written.
//   - The registered preset file has its reviewed SHA-256 and binds the unchanged data/game_data.json.
//   - The tutorial keeps the legacy launch and fact shapes and 100/100. A fresh owned story launch pins exactly the preset in its Run record
//     and, independently, in its launch fact, and its Run starts at 130/110: the check-side Core oracle reproduces the host's created
//     digest at 130/110 (and not at 100/100) and the first battle's enemy maxHp.
//   - A true pre-change 100/100 v4 story record (no member in the record or the fact; rebuilt by that same oracle, whose fidelity the
//     previous step proved) stays valid beside new preset records, through abandon, further commands and a process restart; every old
//     receipt string and archived record is unchanged.
//   - A lost-reply retry makes no second launch. A self-consistent 100/100 downgrade of a preset Run record whose fact keeps the pin is
//     refused (current and archived, version 0 and played), as are null, foreign, incomplete, extra, changed, stripped and retrofitted
//     presets in records or facts, with the stored bytes untouched; the public request cannot choose a preset or a difficulty.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MemoryForge.Core;

sealed partial class ServerChecks
{
    // Independent literals: the check does not reference the host assembly.
    const string BalancePolicyId = "expedition-balance-20261023-v1";
    const string BalancePolicyHash = "da8a49e5a31a46e303ec3456b078d8c7c76ce8072e467f43ca847cc293f729d4";
    const string BalanceContentSha256 = "b6d3543a1d87f0a28e4824e0c1ae45a1eabc676d7abefcd2e57e23b4abc0054a";
    const string BalanceSupportPolicyVersion = "account-progression-20261021-v4";
    static readonly JsonWriterOptions BalanceDigestWriter = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    readonly ResponseCapture expeditionBalanceCapture = new ResponseCapture();

    Api ExpeditionBalanceClient(ServerProcess host) => new Api(host.Port, expeditionBalanceCapture);

    async Task ExpeditionBalanceFile()
    {
        BalancePolicySource();
        string directory = Path.Combine(temp, "store-expedition-balance"), file = AccountFile(directory, "gamma"), token = tokens["gamma"];
        var host = await Start("expedition balance", root, directory);
        var api = ExpeditionBalanceClient(host);

        // The tutorial is untouched by the preset: its Run records and ledger facts keep the exact legacy shapes.
        var tutorial = await ClearProgressionHttp(api, token, file, "tutorial", null);
        var tutorials = AllRecords(file).Where(r => S(r.GetProperty("launch"), "mode") == "tutorial").ToList();
        Check(tutorials.Count > 0 && tutorials.All(r => IsLegacyLaunch(r.GetProperty("launch"))),
            "every tutorial launch keeps the legacy shape without a preset and 100/100");
        var tutorialFacts = A(ReadBody(file).GetProperty("progression"), "ledger");
        Check(tutorialFacts.Where(f => S(f, "kind") == "launch").All(f => MemberNames(f).SequenceEqual(LegacyV1LaunchFact))
            && tutorialFacts.Where(f => S(f, "kind") == "grant").All(f => MemberNames(f).SequenceEqual(LegacyV1GrantFact))
            && tutorialFacts.Any(f => S(f, "kind") == "grant"), "every tutorial launch and grant fact keeps the exact legacy member set and order");

        // A fresh owned story departure pins exactly the preset in its Run record and its launch fact; a lost-reply retry makes no second launch.
        var profile = await HttpProfile(api, token);
        string firstBody = ExpeditionBody(Id(), N64(profile, "revision"), "story", TutorialCandidates);
        var first = await api.Post(ExpeditionsPath, token, firstBody);
        Status(first, 200, null, "a fresh owned story departure");
        string firstRunId = S(first.Json, "runId");
        RememberProgressionSeed(file);
        var stamped = ReadBody(file).GetProperty("run");
        CheckPresetPins(file, firstRunId, "the first story departure");
        byte[] launched = File.ReadAllBytes(file);
        int launches = LaunchFactCount(file), archived = ReadBody(file).GetProperty("archive").GetArrayLength();
        Same((await api.Post(ExpeditionsPath, token, firstBody)).Body, first.Body, "a lost-reply launch retry returns the original receipt");
        Same(File.ReadAllBytes(file), launched, "the launch retry writes nothing");
        Check(LaunchFactCount(file) == launches && ReadBody(file).GetProperty("archive").GetArrayLength() == archived
            && S(ReadBody(file).GetProperty("run"), "runId") == firstRunId, "the launch retry makes no second launch, archive entry or Run");

        // The check-side Core oracle must reproduce the host's own created state at the preset's numbers before it is trusted below.
        string presetDigest = BalanceStateDigest(BalanceLaunchRun(stamped, 130, 110));
        string legacyDigest = BalanceStateDigest(BalanceLaunchRun(stamped, 100, 100));
        Check(presetDigest == S(stamped, "createdSha256"), "the host created the story Run with the preset's 130/110 (check-side Core oracle)");
        Check(legacyDigest != presetDigest, "a 100/100 Run.Create of the same departure differs, so the preset is not inert");

        // Rebuild exactly what the pre-change host stored for this same departure: neither the Run record nor the launch fact has the member,
        // 100/100 and the record's own created digest. The version-0 creation reply is unchanged because the public map view has no
        // difficulty-dependent member, so the stored receipt string stays.
        byte[] legacy = Tamper(launched, body =>
        {
            var launch = body["run"]!["launch"]!.AsObject();
            launch.Remove("balancePolicy");
            launch["difficulty"] = new JsonObject { ["hp"] = 100, ["dmg"] = 100 };
            body["run"]!["createdSha256"] = legacyDigest;
            FactNode(body, firstRunId).Remove("balancePolicy");
        }, true);
        host.Kill();
        WritePrivate(file, legacy);
        host = await Start("expedition balance with a pre-change story record", root, directory);
        api = ExpeditionBalanceClient(host);
        Status(await api.Get(AccountPath, token), 200, null, "a pre-change 100/100 v4 story record is still accepted");
        var legacyRead = await api.Get(RunsPath + "/" + firstRunId, token);
        Status(legacyRead, 200, null, "the pre-change story Run replays");
        Same(Raw(legacyRead.Json, "state"), Raw(first.Json, "state"), "the pre-change record serves the same version-0 view");
        Same((await api.Post(ExpeditionsPath, token, firstBody)).Body, first.Body, "the pre-change creation receipt string is preserved");
        Same(File.ReadAllBytes(file), legacy, "reads and the receipt retry leave the pre-change record byte-identical");
        string legacyEndBody = Obj(("requestId", Id()), ("expectedVersion", 0));
        var legacyEnd = await api.Post(RunsPath + "/" + firstRunId + "/abandon", token, legacyEndBody);
        Status(legacyEnd, 200, null, "the pre-change story Run is abandoned through the public route");
        string legacyRecord = Raw(ArchivedRecord(file, firstRunId), null);
        Check(IsLegacyLaunch(ArchivedRecord(file, firstRunId).GetProperty("launch")), "the archived pre-change story launch keeps no preset and 100/100");
        Check(MemberNames(LaunchFactOf(ReadBody(file), firstRunId)).SequenceEqual(LegacyV4LaunchFact), "the pre-change story launch fact keeps the exact legacy v4 shape");

        // A preset departure abandoned at version 0: no journal, so only its launch fact can tell its preset from a 100/100 record.
        profile = await HttpProfile(api, token);
        string zeroBody = ExpeditionBody(Id(), N64(profile, "revision"), "story", TutorialCandidates);
        var zero = await api.Post(ExpeditionsPath, token, zeroBody);
        Status(zero, 200, null, "a preset departure that is abandoned unplayed");
        string zeroRunId = S(zero.Json, "runId");
        RememberProgressionSeed(file);
        CheckPresetPins(file, zeroRunId, "the unplayed preset departure");
        string zeroEndBody = Obj(("requestId", Id()), ("expectedVersion", 0));
        var zeroEnd = await api.Post(RunsPath + "/" + zeroRunId + "/abandon", token, zeroEndBody);
        Status(zeroEnd, 200, null, "the unplayed preset Run is abandoned at version 0");
        var earlyReceipts = StoredReceipts(file);

        // A new preset departure beside the archived legacy one: the first battle uses the preset's enemy HP.
        profile = await HttpProfile(api, token);
        string playedBody = ExpeditionBody(Id(), N64(profile, "revision"), "story", TutorialCandidates);
        var played = await api.Post(ExpeditionsPath, token, playedBody);
        Status(played, 200, null, "a new story departure after the pre-change record");
        string playedRunId = S(played.Json, "runId");
        RememberProgressionSeed(file);
        var playedRecord = ReadBody(file).GetProperty("run");
        CheckPresetPins(file, playedRunId, "the departure after the pre-change record");
        string node = A(played.Json.GetProperty("state"), "available")[0].GetString()!;
        var entered = await Command(api, token, playedRunId, Id(), 0, "enter", new JsonObject { ["nodeId"] = node });
        Status(entered, 200, null, "the preset Run enters its first node");
        Check(S(entered.Json.GetProperty("state"), "phase") == "BATTLE", "the first node of the preset Run is a battle");
        var served = A(entered.Json.GetProperty("state").GetProperty("battle"), "enemies").Select(e => e.GetProperty("maxHp").GetInt64()).ToList();
        var presetCore = BalanceLaunchRun(playedRecord, 130, 110);
        presetCore.Enter(node);
        var legacyCore = BalanceLaunchRun(playedRecord, 100, 100);
        legacyCore.Enter(node);
        Check(served.SequenceEqual(CoreEnemyMaxHp(presetCore)) && served.Sum() > CoreEnemyMaxHp(legacyCore).Sum(),
            "the served first battle has the preset's enemy maxHp, above the 100/100 Run's");
        var turn = await Command(api, token, playedRunId, Id(), 1, "end", null);
        Status(turn, 200, null, "the preset Run commits an enemy turn");
        string playedEndBody = Obj(("requestId", Id()), ("expectedVersion", 2));
        var playedEnd = await api.Post(RunsPath + "/" + playedRunId + "/abandon", token, playedEndBody);
        Status(playedEnd, 200, null, "the played preset Run is abandoned with its journal");
        string playedArchive = Raw(ArchivedRecord(file, playedRunId), null);

        // The current Run: one more preset departure, kept at version 0 (no journal) for the downgrade regression below.
        profile = await HttpProfile(api, token);
        string currentBody = ExpeditionBody(Id(), N64(profile, "revision"), "story", TutorialCandidates);
        var current = await api.Post(ExpeditionsPath, token, currentBody);
        Status(current, 200, null, "the current story departure");
        string currentRunId = S(current.Json, "runId");
        RememberProgressionSeed(file);
        CheckPresetPins(file, currentRunId, "the current departure");

        // 100/100 created digests of the three preset records, for downgrades that keep the launch fact's pin.
        var downgradeDigests = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [currentRunId] = BalanceStateDigest(BalanceLaunchRun(ReadBody(file).GetProperty("run"), 100, 100)),
            [zeroRunId] = BalanceStateDigest(BalanceLaunchRun(ArchivedRecord(file, zeroRunId), 100, 100)),
            [playedRunId] = BalanceStateDigest(BalanceLaunchRun(ArchivedRecord(file, playedRunId), 100, 100)),
        };

        // The public request cannot choose a preset or a difficulty.
        byte[] good = File.ReadAllBytes(file);
        foreach (var (field, value) in new (string, JsonNode)[]
        {
            ("difficulty", new JsonObject { ["hp"] = 130, ["dmg"] = 110 }),
            ("balancePolicy", new JsonObject { ["id"] = BalancePolicyId, ["sha256"] = BalancePolicyHash }),
        })
        {
            var forged = JsonNode.Parse(ExpeditionBody(Id(), N64(profile, "revision"), "story", TutorialCandidates))!.AsObject();
            forged[field] = value;
            Status(await api.Post(ExpeditionsPath, token, forged.ToJsonString()), 400, "UNKNOWN_FIELD", "a public expedition request cannot set " + field);
            Same(File.ReadAllBytes(file), good, "a refused public " + field + " changes no account bytes");
        }

        // Mixed archive after a process restart: legacy and preset records replay, and every earlier receipt string is unchanged.
        host.Kill();
        host = await Start("expedition balance after restart", root, directory);
        api = ExpeditionBalanceClient(host);
        var restored = await api.Get(RunsPath + "/" + currentRunId, token);
        Status(restored, 200, null, "the current preset Run after a restart");
        Same(Raw(restored.Json, "state"), Raw(current.Json, "state"), "the restart replays the exact current preset Run");
        Same((await api.Post(tutorial.Route, token, tutorial.Body)).Body, tutorial.Reply.Body, "the tutorial save receipt is unchanged");
        Same((await api.Post(ExpeditionsPath, token, firstBody)).Body, first.Body, "the pre-change launch receipt is unchanged");
        Same((await api.Post(RunsPath + "/" + firstRunId + "/abandon", token, legacyEndBody)).Body, legacyEnd.Body, "the pre-change abandon receipt is unchanged");
        Same((await api.Post(ExpeditionsPath, token, zeroBody)).Body, zero.Body, "the unplayed preset launch receipt is unchanged");
        Same((await api.Post(RunsPath + "/" + zeroRunId + "/abandon", token, zeroEndBody)).Body, zeroEnd.Body, "the unplayed preset abandon receipt is unchanged");
        Same((await api.Post(ExpeditionsPath, token, playedBody)).Body, played.Body, "the preset launch receipt is unchanged");
        Same((await api.Post(RunsPath + "/" + playedRunId + "/abandon", token, playedEndBody)).Body, playedEnd.Body, "the preset abandon receipt is unchanged");
        Same((await api.Post(ExpeditionsPath, token, currentBody)).Body, current.Body, "the current launch receipt is unchanged");
        Same(File.ReadAllBytes(file), good, "restart reads and receipt retries write nothing");
        Same(Raw(ArchivedRecord(file, firstRunId), null), legacyRecord, "the archived pre-change record is byte-identical after later launches and a restart");
        Same(Raw(ArchivedRecord(file, playedRunId), null), playedArchive, "the archived preset record and its journal are unchanged");
        var laterReceipts = StoredReceipts(file);
        Check(earlyReceipts.All(p => laterReceipts.TryGetValue(p.Key, out string? raw) && raw == p.Value), "every earlier stored receipt is unchanged");
        Check(ArchivedRecord(file, playedRunId).GetProperty("journal").GetArrayLength() == 2
            && ArchivedRecord(file, playedRunId).GetProperty("launch").TryGetProperty("balancePolicy", out _),
            "the archive mixes the pre-change record with a played preset record");

        // Every malformed, foreign, stripped or retrofitted preset is refused and the stored bytes are preserved.
        foreach (var item in BalanceTamperCases(good, firstRunId, zeroRunId, playedRunId, currentRunId, S(tutorials[0], "runId"), downgradeDigests))
        {
            WritePrivate(file, item.Bytes);
            Status(await api.Get(AccountPath, token), 500, "STORE_CORRUPT", item.What + ": account read");
            Status(await api.Post(ExpeditionsPath, token, ExpeditionBody(Id(), N64(profile, "revision"), "story", TutorialCandidates)), 500, "STORE_CORRUPT",
                item.What + ": departure");
            Same(File.ReadAllBytes(file), item.Bytes, item.What + ": the refused envelope is preserved byte for byte");
        }
        WritePrivate(file, good);
        Status(await api.Get(AccountPath, token), 200, null, "the restored mixed history loads again");
        host.Kill();

        // The offline verifier accepts the same mixed history and writes nothing.
        string before = StoreSnapshot(directory);
        var verified = await RunVerifier("the mixed legacy/preset launch history", Config(root, directory));
        Same(verified.Exit, 0, "--verify-store passes the mixed legacy/preset launch history");
        Check(VerifyText(VerifyLine(verified, "account", "gamma"), "status") == "PASS", "the mixed history account verifies");
        SameVerifyResult(verified, "PASS", 3, 1, 2, 0, 0, "the mixed legacy/preset launch history");
        Same(StoreSnapshot(directory), before, "verify-store changes no byte, mode or mtime of the mixed history");
        ExpeditionBalanceLeakScan();
        completed.Add("expedition-balance");
    }

    // The registered file is the reviewed one and binds the content this suite serves.
    void BalancePolicySource()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "server", "MemoryForge.Server", "Policies", BalancePolicyId + ".json"));
        Check(Convert.ToHexStringLower(SHA256.HashData(bytes)) == BalancePolicyHash, "the registered preset file has its reviewed SHA-256");
        using var document = JsonDocument.Parse(bytes);
        var policy = document.RootElement;
        var difficulty = policy.GetProperty("difficulty");
        Check(policy.EnumerateObject().Count() == 4 && S(policy, "version") == BalancePolicyId && S(policy, "mainContentSha256") == BalanceContentSha256
            && A(policy, "modes").Select(m => m.GetString()).SequenceEqual(new[] { "story" })
            && difficulty.EnumerateObject().Count() == 2 && I(difficulty, "hp") == 130 && I(difficulty, "dmg") == 110,
            "the preset is story-only 130/110 on its named content");
        string content = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "data", "game_data.json"))));
        Check(content == BalanceContentSha256, "the preset binds the unchanged data/game_data.json");
    }

    // Exact legacy fact shapes (Progression.cs ProgressionFact.ToGraph without the optional trailing member).
    static readonly string[] LegacyV1LaunchFact = { "kind", "requestId", "runId", "policyVersion", "revision", "request" };
    static readonly string[] LegacyV1GrantFact = { "kind", "requestId", "runId", "policyVersion", "revision", "chapter", "journalIndex", "stateSha256", "grant" };
    static readonly string[] LegacyV4LaunchFact = { "kind", "requestId", "runId", "policyVersion", "revision", "policyHash", "request" };

    // The Run record (current or archived) and its launch fact both pin exactly the registered preset; the Run starts at 130/110.
    void CheckPresetPins(string file, string runId, string what)
    {
        var body = ReadBody(file);
        var record = body.GetProperty("run").ValueKind == JsonValueKind.Object && S(body.GetProperty("run"), "runId") == runId
            ? body.GetProperty("run") : A(body, "archive").Single(r => S(r, "runId") == runId);
        var launch = record.GetProperty("launch");
        var fact = LaunchFactOf(body, runId);
        Check(S(launch, "mode") == "story" && S(launch, "policyVersion") == BalanceSupportPolicyVersion && LaunchDifficulty(launch) == (130, 110) && IsPresetPin(launch),
            what + ": the v4 story Run record pins exactly the registered preset and 130/110");
        Check(MemberNames(fact).SequenceEqual(LegacyV4LaunchFact.Append("balancePolicy")) && IsPresetPin(fact),
            what + ": its launch fact independently pins the same preset as one trailing member");
    }

    static bool IsPresetPin(JsonElement holder) => holder.TryGetProperty("balancePolicy", out var policy) && policy.ValueKind == JsonValueKind.Object
        && MemberNames(policy).SequenceEqual(new[] { "id", "sha256" }) && S(policy, "id") == BalancePolicyId && S(policy, "sha256") == BalancePolicyHash
        && holder.EnumerateObject().Last().Name == "balancePolicy";
    static List<string> MemberNames(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToList();
    static JsonElement LaunchFactOf(JsonElement body, string runId) =>
        A(body.GetProperty("progression"), "ledger").Single(f => S(f, "kind") == "launch" && S(f, "runId") == runId);
    static JsonObject FactNode(JsonObject body, string runId) => body["progression"]!["ledger"]!.AsArray().Select(f => f!.AsObject())
        .Single(f => f["kind"]!.GetValue<string>() == "launch" && f["runId"]!.GetValue<string>() == runId);

    static bool IsLegacyLaunch(JsonElement launch) => !launch.TryGetProperty("balancePolicy", out _) && LaunchDifficulty(launch) == (100, 100);
    static (int Hp, int Damage) LaunchDifficulty(JsonElement launch) => (I(launch.GetProperty("difficulty"), "hp"), I(launch.GetProperty("difficulty"), "dmg"));
    static int LaunchFactCount(string file) => A(ReadBody(file).GetProperty("progression"), "ledger").Count(f => S(f, "kind") == "launch");

    static List<JsonElement> AllRecords(string file)
    {
        var body = ReadBody(file);
        var records = A(body, "archive");
        if (body.GetProperty("run").ValueKind == JsonValueKind.Object) records.Add(body.GetProperty("run"));
        return records;
    }

    static JsonElement ArchivedRecord(string file, string runId) => A(ReadBody(file), "archive").Single(r => S(r, "runId") == runId);

    static Dictionary<string, string> StoredReceipts(string file) =>
        ReadBody(file).GetProperty("receipts").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText(), StringComparer.Ordinal);

    // Re-digested edits of one stored history. Each must fail the host's decode (STORE_CORRUPT) without any rewrite. downgrade: each
    // preset record's 100/100 created digest from the check-side oracle.
    static List<(string What, byte[] Bytes)> BalanceTamperCases(byte[] good, string legacyRunId, string zeroRunId, string playedRunId, string currentRunId,
        string tutorialRunId, IReadOnlyDictionary<string, string> downgrade)
    {
        JsonObject CurrentLaunch(JsonObject body) => body["run"]!["launch"]!.AsObject();
        JsonObject Archived(JsonObject body, string runId) => body["archive"]!.AsArray().Select(r => r!.AsObject()).Single(r => r["runId"]!.GetValue<string>() == runId);
        JsonObject RecordNode(JsonObject body, string runId) => runId == currentRunId ? body["run"]!.AsObject() : Archived(body, runId);
        JsonObject Pin() => new JsonObject { ["id"] = BalancePolicyId, ["sha256"] = BalancePolicyHash };
        void Retrofit(JsonObject record)
        {
            var launch = record["launch"]!.AsObject();
            launch["difficulty"] = new JsonObject { ["hp"] = 130, ["dmg"] = 110 };
            launch["balancePolicy"] = Pin();
        }
        void Strip(JsonObject launch, bool resetDifficulty)
        {
            launch.Remove("balancePolicy");
            if (resetDifficulty) launch["difficulty"] = new JsonObject { ["hp"] = 100, ["dmg"] = 100 };
        }
        // The Codex-review regression: a complete, self-consistent 100/100 Run record (recomputed created digest) whose launch fact still pins
        // the preset. For the version-0 current and archived records nothing but the fact pin can notice it.
        void Downgrade(JsonObject body, string runId)
        {
            var record = RecordNode(body, runId);
            Strip(record["launch"]!.AsObject(), true);
            record["createdSha256"] = downgrade[runId];
        }
        JsonObject GrantFact(JsonObject body) => body["progression"]!["ledger"]!.AsArray().Select(f => f!.AsObject()).First(f => f["kind"]!.GetValue<string>() == "grant");
        return new List<(string What, byte[] Bytes)>
        {
            ("a current version-0 Run downgraded to 100/100 with its created digest while its fact keeps the preset", Tamper(good, b => Downgrade(b, currentRunId), true)),
            ("an archived version-0 Run downgraded to 100/100 with its created digest while its fact keeps the preset", Tamper(good, b => Downgrade(b, zeroRunId), true)),
            ("an archived played Run downgraded to 100/100 with its created digest while its fact keeps the preset", Tamper(good, b => Downgrade(b, playedRunId), true)),
            ("a fact preset removed while the Run record keeps it", Tamper(good, b => FactNode(b, currentRunId).Remove("balancePolicy"), true)),
            ("an unknown fact preset version", Tamper(good, b => FactNode(b, currentRunId)["balancePolicy"]!["id"] = "expedition-balance-20261023-v2", true)),
            ("an unknown fact preset hash", Tamper(good, b => FactNode(b, currentRunId)["balancePolicy"]!["sha256"] = new string('0', 64), true)),
            ("an extra fact preset member", Tamper(good, b => FactNode(b, currentRunId)["balancePolicy"]!["dmg"] = 110, true)),
            ("a null fact preset", Tamper(good, b => FactNode(b, currentRunId)["balancePolicy"] = null, true)),
            ("a fact preset on the pre-change story fact", Tamper(good, b => FactNode(b, legacyRunId)["balancePolicy"] = Pin(), true)),
            ("a fact preset on a tutorial launch fact", Tamper(good, b => FactNode(b, tutorialRunId)["balancePolicy"] = Pin(), true)),
            ("a fact preset on a grant fact", Tamper(good, b => GrantFact(b)["balancePolicy"] = Pin(), true)),
            ("an unknown preset version", Tamper(good, b => CurrentLaunch(b)["balancePolicy"]!["id"] = "expedition-balance-20261023-v2", true)),
            ("an unknown preset hash", Tamper(good, b => CurrentLaunch(b)["balancePolicy"]!["sha256"] = new string('0', 64), true)),
            ("an incomplete preset", Tamper(good, b => CurrentLaunch(b)["balancePolicy"]!.AsObject().Remove("sha256"), true)),
            ("an extra preset member", Tamper(good, b => CurrentLaunch(b)["balancePolicy"]!["hp"] = 130, true)),
            ("a null preset", Tamper(good, b => CurrentLaunch(b)["balancePolicy"] = null, true)),
            ("a preset name instead of its object", Tamper(good, b => CurrentLaunch(b)["balancePolicy"] = BalancePolicyId, true)),
            ("a changed preset HP multiplier", Tamper(good, b => CurrentLaunch(b)["difficulty"]!["hp"] = 140, true)),
            ("a changed preset damage multiplier", Tamper(good, b => CurrentLaunch(b)["difficulty"]!["dmg"] = 100, true)),
            ("a removed preset", Tamper(good, b => Strip(CurrentLaunch(b), false), true)),
            ("a removed preset with 100/100 difficulty", Tamper(good, b => Strip(CurrentLaunch(b), true), true)),
            ("an archived played preset stripped to 100/100", Tamper(good, b => Strip(Archived(b, playedRunId)["launch"]!.AsObject(), true), true)),
            ("a preset retrofitted onto the pre-change story record", Tamper(good, b => Retrofit(Archived(b, legacyRunId)), true)),
            ("a preset on a tutorial record", Tamper(good, b => Retrofit(Archived(b, tutorialRunId)), true)),
        };
    }

    // The check-side Core oracle of one stored launch record: the host's Run.Create inputs (Progression.cs ExpeditionLaunch.Setup) rebuilt from
    // the stored record with an explicit difficulty. No host code is involved and nothing is injected.
    Run BalanceLaunchRun(JsonElement record, int hp, int damage)
    {
        var launch = record.GetProperty("launch");
        var setup = new RunSetup
        {
            Seed = record.GetProperty("seed").GetInt64(), RunId = S(record, "runId"), MemoryVersion = record.GetProperty("memoryVersion").GetInt64(),
            Memory = MemoryV2DataLoader.Graph(record.GetProperty("startMemory")), MemoryPolicy = "v2",
            UseMemory = launch.GetProperty("useMemory").GetBoolean() ? (bool?)null : false, Length = "short",
            Zones = new List<int> { 1 }, Bosses = new List<object?> { "b_bell" }, StageLevel = I(launch, "stageLevel"),
            Difficulty = new RunDifficulty { Hp = hp, Damage = damage }
        };
        foreach (var member in A(launch, "party")) setup.Party.Add(RunPartyEntry.FromJsonGraph(MemoryV2DataLoader.Graph(member)));
        if (launch.TryGetProperty("support", out var support)) setup.Support = RunPartyEntry.FromJsonGraph(MemoryV2DataLoader.Graph(support.GetProperty("build")));
        return Run.Create(RunDataLoader.Load(root), setup);
    }

    static List<long> CoreEnemyMaxHp(Run run)
    {
        var battle = (IDictionary<string, object?>)run.State.ToJsonGraph()["battle"]!;
        return ((IList)battle["enemies"]!).Cast<IDictionary<string, object?>>().Select(e => Convert.ToInt64(e["maxHp"], CultureInfo.InvariantCulture)).ToList();
    }

    // The host's state digest (PracticeRunStore.StateDigest: SHA-256 of the ordinal-sorted JSON graph, relaxed escaping), rewritten here.
    static string BalanceStateDigest(Run run)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, BalanceDigestWriter)) WriteSortedGraph(writer, run.State.ToJsonGraph(), 0);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    static void WriteSortedGraph(Utf8JsonWriter writer, object? value, int depth)
    {
        if (depth > 128) throw new CheckFailure("a Core state graph nests deeper than 128");
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string text: writer.WriteStringValue(text); break;
            case bool flag: writer.WriteBooleanValue(flag); break;
            case int number: writer.WriteNumberValue(number); break;
            case uint number: writer.WriteNumberValue(number); break;
            case long number: writer.WriteNumberValue(number); break;
            case double number when double.IsFinite(number): writer.WriteNumberValue(number); break;
            case IDictionary<string, object?> members:
                writer.WriteStartObject();
                foreach (var pair in members.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(pair.Key);
                    WriteSortedGraph(writer, pair.Value, depth + 1);
                }
                writer.WriteEndObject();
                break;
            case IList list:
                writer.WriteStartArray();
                foreach (object? item in list) WriteSortedGraph(writer, item, depth + 1);
                writer.WriteEndArray();
                break;
            default: throw new CheckFailure("a Core state graph holds an unsupported value");
        }
    }

    void ExpeditionBalanceLeakScan()
    {
        var secrets = new Secrets(tokens.Values.ToArray(), seeds.Where(s => s >= 1000000).Select(s => s.ToString(CultureInfo.InvariantCulture)).ToArray());
        int scanned = 0;
        foreach (string body in expeditionBalanceCapture.Bodies)
        {
            string? leak = ProgressionHttpLeak(body, secrets);
            if (leak != null)
            {
                Record("expedition balance response leak at " + leak, body);
                throw new CheckFailure("private expedition balance response (path in diagnostics.log)");
            }
            scanned++;
        }
        Check(scanned > 0 && scanned == expeditionBalanceCapture.Started, "every expedition balance response was captured and checked; no preset or launch member is public");
    }
}
