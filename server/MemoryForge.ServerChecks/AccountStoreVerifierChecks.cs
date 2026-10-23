// ACCOUNT-PROGRESSION-S0-20261021: the read-only `--verify-store` mode of the real server build (AccountStoreVerifier.cs), run as child
// processes with the suite's own configuration; nothing is injected and every store is only read by the check side (byte, mode and mtime
// snapshots, oracle reads of stored bodies). File suite ("verify-store"):
//  - verify-mode startup refusals (exit 2, never listening, nothing read): a missing store directory (not created), a store inside the
//    repository, the Production environment, and other spellings of the flag;
//  - the histories the earlier scenarios committed through the real API (the main expedition store and the recruitment store) pass;
//  - a fresh store built only through the real API — alpha: the launch claim before any run, run A with a pull between its commands and a
//    terminal save (of the card the bot earned, if any), run B with useMemory false and its save, current run C with a pull between its
//    commands; beta: recruitment only; gamma: nothing — passes with every checkpoint and receipt accounted for (the counts equal the
//    requests this check committed and the stored records) and a capacity report equal to check-side Core over the stored decks and copies;
//  - copies of alpha's envelope with re-digested tampering the normal decode does not examine (archived journals and digests, start
//    memory/version, useMemory, the memory store's history/receipts/version, run receipt identities and bodies, missing, orphan and
//    fabricated receipts with a consistent revision, a revision above the receipt count, a duplicated or removed archived run) fail with
//    their fixed code and record position, the host's own decode refusals (binding, ledger, digest, garbage) are reported unchanged, and
//    unedited re-encodings pass;
//  - on test-local content (memory add costs 7) and a test fixture (Sera/Harin/Nia at Asc 4, Core capacity 8) a Harin or Nia card the bot
//    earns is saved legitimately; the verifier then reports that deck over the tutorial capacity 6 and Core's Create refusing the frozen
//    tutorial party with OVER_CAPACITY for both useMemory values (exit 4, HOLD).
// Every verifier run leaves its store byte-, mode- and mtime-identical with no file created. PostgreSQL mode ("pg-verify-store"): the four
// fixture aliases are read through the backend; three pass, the faulty-content alias is STORE_CONTENT_MISMATCH, and every row is
// byte-identical afterwards. The bot's first win is bounded (as in the expedition scenario): six departures, else an explicit check-bot limit.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MemoryForge.Core;

sealed partial class ServerChecks
{
    const string VerifyFlag = "--verify-store";
    static readonly string[] TutorialCandidates = { "nia", "harin", "rien" };
    static readonly string[] VerifyLineKinds = { "start", "account", "capacity", "probe", "result" };
    static readonly string ZeroHex = new string('0', 64);

    /// <summary>One finished verifier process: its exit code and its report lines (kind and key=value members).</summary>
    sealed record VerifyRun(int Exit, List<(string Kind, Dictionary<string, string> Members)> Lines);
    /// <summary>The fresh alpha history: its committed bytes and body and the request IDs and node the tamper cases address.</summary>
    sealed record VerifyHistory(byte[] Envelope, JsonElement Body, string SaveA, string CreateC, string EndC, string OtherNode);

    async Task VerifyStoreFile()
    {
        await VerifyStoreRefusals();
        await VerifyExistingStores();
        var history = await VerifyStoreHistories();
        await VerifyStoreTamper(history);
        await VerifyStoreCapacityHold();
        completed.Add("verify-store");
    }

    // ───────── verify-mode startup refusals: exit 2 before any account is read, never listening, nothing created ─────────
    async Task VerifyStoreRefusals()
    {
        string missing = Path.Combine(temp, "verify-missing-store");
        await VerifyRefused("a store directory that does not exist", Config(root, missing), new[] { VerifyFlag });
        Check(!Directory.Exists(missing), "verify-store never creates a missing store directory");
        string inside = Path.Combine(root, "ops", "ai", "local", "verify-store-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6)));
        await VerifyRefused("a store inside the repository", Config(root, inside), new[] { VerifyFlag });
        Check(!Directory.Exists(inside), "a refused in-repository verify-store creates no directory");
        string before = StoreSnapshot(store);
        await VerifyRefused("the Production environment", Config(root, store), new[] { VerifyFlag }, "Production");
        foreach (var (what, arguments) in new[] { ("a valued flag", new[] { "--verify-store=true" }), ("a repeated flag", new[] { VerifyFlag, VerifyFlag }),
            ("another case of the flag", new[] { "--Verify-Store" }), ("a slash spelling of the flag", new[] { "/verify-store" }) })
            await VerifyRefused(what, Config(root, store), arguments);
        Same(StoreSnapshot(store), before, "refused verify-store invocations changed nothing of an existing store");
    }
    async Task VerifyRefused(string what, IReadOnlyDictionary<string, string> config, string[] arguments, string environment = "Development")
    {
        var process = Launch("verify-store refusal: " + what, config, environment, arguments: arguments);
        bool exited = await process.WaitExit(TimeSpan.FromSeconds(90));
        if (!exited) process.Kill();
        Check(exited && process.Process.ExitCode == 2 && !process.ListeningSeen && process.Output.Contains("refused to start", StringComparison.Ordinal)
            && !process.Output.Contains("verify-store account", StringComparison.Ordinal), "verify-store with " + what + " is refused (exit 2) before any account is read, without listening");
    }

    // ───────── the histories the earlier scenarios committed through the real API (their hosts are stopped) ─────────
    async Task VerifyExistingStores()
    {
        foreach (var (what, directory) in new[] { ("the main expedition store", store), ("the recruitment store", Path.Combine(temp, "store-recruitment")) })
        {
            string before = StoreSnapshot(directory);
            var run = await RunVerifier(what, Config(root, directory));
            Same(run.Exit, 0, what + ": verify-store passes every account (exit 0)");
            foreach (string id in new[] { "alpha", "beta" })
            {
                var body = ReadBody(AccountFile(directory, id));
                SamePass(run, id, body, what);
                Check(!SameCapacity(run, id, body, root, what), what + ": " + id + "'s saved decks fit the tutorial capacity (each was saved within the fixture party's capacity 6)");
            }
            Check(VerifyText(VerifyLine(run, "account", "gamma"), "status") == "ABSENT" && !File.Exists(AccountFile(directory, "gamma")),
                what + ": an account without an envelope is ABSENT and still has none");
            Check(!SameCapacity(run, "gamma", null, root, what), what + ": an absent account starts from the baseline decks");
            SameVerifyResult(run, "PASS", 3, 2, 1, 0, 0, what);
            Same(StoreSnapshot(directory), before, what + ": verify-store changed no byte, mode or mtime and created no file");
        }
    }

    // ───────── a fresh store built only through the real API, then verified ─────────
    async Task<VerifyHistory> VerifyStoreHistories()
    {
        string directory = Path.Combine(temp, "store-verify"), file = AccountFile(directory, "alpha"), alpha = tokens["alpha"], beta = tokens["beta"];
        var host = await Start("verify-store history host", root, directory);
        var runs = Client(host);
        var recruit = RecruitClient(host);
        long revision = 0;
        int creates = 0, commands = 0, ledger = 0;

        Status(await RecruitLaunch(recruit, alpha, LaunchBody(Id(), revision)), 200, null, "verify-store history: alpha claims the launch reward before any run");
        revision++; ledger++;

        // Run A (useMemory omitted, so true): enter, a pull between its commands, then the bot plays on to a terminal save of its earned card.
        var a = await runs.Post(RunsPath, alpha, Obj(("requestId", Id())));
        Status(a, 200, null, "verify-store history: alpha departs (run A)");
        revision++; creates++;
        string runA = S(a.Json, "runId");
        seeds.Add(ReadBody(file).GetProperty("run").GetProperty("seed").GetInt64());
        var firstFloor = A(a.Json.GetProperty("state"), "available").Select(n => n.GetString()!).ToList();
        Check(firstFloor.Count > 1, "verify-store history: run A offers more than one first-floor node");
        var entered = await Command(runs, alpha, runA, Id(), 0, "enter", new JsonObject { ["nodeId"] = firstFloor[0] });
        Status(entered, 200, null, "verify-store history: run A enters its first node");
        revision++; commands++;
        string pull = PullBody(Id(), revision, "standard", 1);
        var pulled = await RecruitPull(recruit, alpha, pull);
        Status(pulled, 200, null, "verify-store history: a pull between run A's commands");
        revision++; ledger++;
        Same((await RecruitPull(recruit, alpha, pull)).Body, pulled.Body, "verify-store history: the retried pull is its receipt, not another commit");
        var kinds = new List<string>();
        var playedA = await PlayToDefeat(runs, alpha, runA, entered.Json.GetProperty("state"), kinds);
        revision += kinds.Count; commands += kinds.Count;
        var add = playedA.TakenKey == null ? null : AddChange(playedA.State, playedA.TakenKey);
        string saveA = Id();
        var savedA = await Commit(runs, alpha, runA, saveA, I(playedA.State, "version"), 0, add == null ? Array.Empty<string>() : new[] { add.Value.Id });
        Status(savedA, 200, null, "verify-store history: run A's terminal save");
        revision++; commands++;
        Check(I(savedA.Json, "memoryVersion") == 1 && S(savedA.Json.GetProperty("state"), "phase") == "DONE", "verify-store history: run A's save commits memory version 1 and finishes the run");

        // Run B (useMemory false) archives A; its save keeps the saved deck (an empty selection is always allowed).
        var b = await runs.Post(RunsPath, alpha, Obj(("requestId", Id()), ("useMemory", false)));
        Status(b, 200, null, "verify-store history: alpha departs without its memory (run B, useMemory false)");
        revision++; creates++;
        string runB = S(b.Json, "runId");
        seeds.Add(ReadBody(file).GetProperty("run").GetProperty("seed").GetInt64());
        Check(I(b.Json, "memoryVersion") == 1, "verify-store history: run B starts at memory version 1");
        kinds.Clear();
        var playedB = await PlayToDefeat(runs, alpha, runB, b.Json.GetProperty("state"), kinds);
        revision += kinds.Count; commands += kinds.Count;
        Status(await Commit(runs, alpha, runB, Id(), I(playedB.State, "version"), 1), 200, null, "verify-store history: run B's save keeps the saved deck");
        revision++; commands++;

        // Run C stays current: enter, a pull between its commands, one ended turn; a refused departure and a stale command commit nothing.
        string createC = Id();
        var c = await runs.Post(RunsPath, alpha, Obj(("requestId", createC)));
        Status(c, 200, null, "verify-store history: alpha departs again (run C, current)");
        revision++; creates++;
        string runC = S(c.Json, "runId");
        seeds.Add(ReadBody(file).GetProperty("run").GetProperty("seed").GetInt64());
        Check(I(c.Json, "memoryVersion") == 2, "verify-store history: run C starts at memory version 2");
        var enteredC = await Command(runs, alpha, runC, Id(), 0, "enter", new JsonObject { ["nodeId"] = A(c.Json.GetProperty("state"), "available")[0].GetString() });
        Status(enteredC, 200, null, "verify-store history: run C enters its first node");
        revision++; commands++;
        Check(S(enteredC.Json.GetProperty("state"), "phase") == "BATTLE", "verify-store history: run C is in a battle");
        Status(await RecruitPull(recruit, alpha, PullBody(Id(), revision, "standard", 1)), 200, null, "verify-store history: a pull between run C's commands");
        revision++; ledger++;
        string endC = Id();
        Status(await Command(runs, alpha, runC, endC, 1, "end", null), 200, null, "verify-store history: run C ends a turn after the pull");
        revision++; commands++;
        Status(await runs.Post(RunsPath, alpha, Obj(("requestId", Id()))), 409, "RUN_ACTIVE", "verify-store history: a refused departure");
        Status(await Command(runs, alpha, runC, Id(), 1, "end", null), 409, "STALE_VERSION", "verify-store history: a stale command");

        Status(await RecruitLaunch(recruit, beta, LaunchBody(Id(), 0)), 200, null, "verify-store history: beta claims the launch reward");
        Status(await RecruitPull(recruit, beta, PullBody(Id(), 1, "standard", 10)), 200, null, "verify-store history: beta's ten-pull (recruitment only)");
        host.Kill();

        var body = ReadBody(file);
        var archive = A(body, "archive");
        Check(body.GetProperty("revision").GetInt64() == revision && archive.Count == 2 && body.GetProperty("run").ValueKind == JsonValueKind.Object
            && archive[0].GetProperty("useMemory").GetBoolean() && !archive[1].GetProperty("useMemory").GetBoolean(),
            "verify-store history precondition: alpha holds runs A and B archived (B without memory) and run C current, at the committed revision");
        string before = StoreSnapshot(directory);
        var run = await RunVerifier("the fresh histories", Config(root, directory));
        Same(run.Exit, 0, "the fresh histories: verify-store passes (exit 0)");
        SamePass(run, "alpha", body, "the fresh histories");
        var line = VerifyLine(run, "account", "alpha");
        CheckLine(VerifyNumber(line, "revision") == revision && VerifyNumber(line, "checkpoints") == creates + commands && VerifyNumber(line, "runReceipts") == creates + commands
            && VerifyNumber(line, "recruitmentReceipts") == ledger && VerifyNumber(line, "archived") == 2 && VerifyNumber(line, "current") == 1 && VerifyNumber(line, "memoryVersion") == 2, line,
            "the fresh histories: alpha's report accounts for exactly the " + creates + " departures, " + commands + " commands and saves and " + ledger + " recruitment requests this check committed");
        var betaBody = ReadBody(AccountFile(directory, "beta"));
        SamePass(run, "beta", betaBody, "the fresh histories");
        var betaLine = VerifyLine(run, "account", "beta");
        CheckLine(VerifyNumber(betaLine, "runs") == 0 && VerifyNumber(betaLine, "checkpoints") == 0 && VerifyNumber(betaLine, "recruitmentReceipts") == 2
            && VerifyNumber(betaLine, "revision") == 2 && VerifyNumber(betaLine, "memoryVersion") == 0, betaLine,
            "the fresh histories: beta's recruitment-only envelope has two receipts, both recruitment, and no run");
        Check(VerifyText(VerifyLine(run, "account", "gamma"), "status") == "ABSENT", "the fresh histories: gamma committed nothing and is ABSENT");
        foreach (var (id, stored) in new[] { ("alpha", (JsonElement?)body), ("beta", (JsonElement?)betaBody), ("gamma", (JsonElement?)null) })
            Check(!SameCapacity(run, id, stored, root, "the fresh histories"), "the fresh histories: " + id + "'s decks fit the tutorial capacity");
        Check(TutorialCandidates.All(cid => VerifyNumber(VerifyLine(run, "capacity", "alpha", cid), "tutorialCapacity") == 6),
            "the fresh histories: the frozen tutorial build (Res 0, Asc 0) has Core's capacity 6 for Nia, Harin and Rien");
        SameVerifyResult(run, "PASS", 3, 2, 1, 0, 0, "the fresh histories");
        Same(StoreSnapshot(directory), before, "the fresh histories: verify-store changed no byte, mode or mtime and created no file");
        return new VerifyHistory(File.ReadAllBytes(file), body, saveA, createC, endC, firstFloor.First(n => n != firstFloor[0]));
    }

    // ───────── re-digested tampering on a copy of alpha's envelope (the only file in its store directory) ─────────
    async Task VerifyStoreTamper(VerifyHistory history)
    {
        string directory = Path.Combine(temp, "store-verify-tamper"), file = AccountFile(directory, "alpha");
        Directory.CreateDirectory(directory, OwnerDirectory);
        byte[] good = history.Envelope;
        int lastA = A(A(history.Body, "archive")[0], "journal").Count - 1;
        string atSaveA = "archive.0.journal." + lastA.ToString(CultureInfo.InvariantCulture);
        long revision = history.Body.GetProperty("revision").GetInt64();
        static JsonObject Receipts(JsonObject b) => b["receipts"]!.AsObject();
        var cases = new (string What, byte[] Bytes, string Code, string At)[]
        {
            ("an invalid create request ID", Tamper(good, b =>
            {
                var moved = Receipts(b)[history.CreateC]!.DeepClone(); Receipts(b).Remove(history.CreateC); Receipts(b).Add("x", moved);
            }, true), "VERIFY_RECEIPT_MISMATCH", "receipts"),
            ("an invalid ordinary-command request ID", Tamper(good, b =>
            {
                var moved = Receipts(b)[history.EndC]!.DeepClone(); Receipts(b).Remove(history.EndC); Receipts(b).Add("x", moved);
            }, true), "VERIFY_RECEIPT_MISMATCH", "receipts"),
            ("an archived run's first command argument", Tamper(good, b => b["archive"]![0]!["journal"]![0]!["args"]!["nodeId"] = history.OtherNode, true), "VERIFY_REPLAY_MISMATCH", "archive.0.journal.0"),
            ("an archived run's created-state digest", Tamper(good, b => b["archive"]![1]!["createdSha256"] = ZeroHex, true), "VERIFY_REPLAY_MISMATCH", "archive.1.create"),
            ("an archived run's save state digest", Tamper(good, b => b["archive"]![0]!["journal"]![lastA]!["sha256"] = ZeroHex, true), "VERIFY_REPLAY_MISMATCH", atSaveA),
            ("the current run's journal state digest", Tamper(good, b => b["run"]!["journal"]![1]!["sha256"] = ZeroHex, true), "VERIFY_REPLAY_MISMATCH", "current.journal.1"),
            ("an archived run's useMemory flipped", Tamper(good, b => b["archive"]![1]!["useMemory"] = true, true), "VERIFY_REPLAY_MISMATCH", "archive.1.create"),
            ("an archived run's start memory", Tamper(good, b => b["archive"]![1]!["startMemory"] = new JsonObject(), true), "VERIFY_MEMORY_MISMATCH", "archive.1.create"),
            ("an archived run's memory version", Tamper(good, b => b["archive"]![1]!["memoryVersion"] = 0, true), "VERIFY_MEMORY_MISMATCH", "archive.1.create"),
            ("a memory history entry", Tamper(good, b => b["memory"]!["history"]![0]!["selectedChangeIds"]!.AsArray().Add(JsonValue.Create("delta:forged:change:add")), true), "VERIFY_MEMORY_MISMATCH", "memory"),
            ("a memory receipt's response", Tamper(good, b => b["memory"]!["receipts"]!.AsObject().First().Value!["response"]!["version"] = 9, true), "VERIFY_MEMORY_MISMATCH", "memory"),
            ("a raised memory version", Tamper(good, b => b["memory"]!["version"] = 3, true), "VERIFY_MEMORY_MISMATCH", "memory"),
            ("a save receipt's identity", Tamper(good, b => Receipts(b)[history.SaveA]!["identity"] = new string('f', 64), true), "VERIFY_RECEIPT_MISMATCH", atSaveA),
            ("a save receipt's memory version", Tamper(good, b => EditReceipt(b, history.SaveA, r => r["memoryVersion"] = 0), true), "VERIFY_RECEIPT_MISMATCH", atSaveA),
            ("a create receipt's served HP", Tamper(good, b => EditReceipt(b, history.CreateC, r => r["state"]!["hp"] = r["state"]!["hp"]!.GetValue<int>() + 1), true),
                "VERIFY_RECEIPT_MISMATCH", "current.create"),
            ("a missing command receipt (revision lowered to match)", Tamper(good, b => { Receipts(b).Remove(history.EndC); b["revision"] = revision - 1; }, true),
                "VERIFY_RECEIPT_MISMATCH", "current.journal.1"),
            ("an orphan copy of a run receipt (revision raised to match)", Tamper(good, b =>
            {
                Receipts(b).Add("chk-orphan-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)), Receipts(b)[history.CreateC]!.DeepClone());
                b["revision"] = revision + 1;
            }, true), "VERIFY_RECEIPT_ORPHAN", "receipts"),
            ("a fabricated run-shaped receipt (revision raised to match)", Tamper(good, b =>
            {
                Receipts(b).Add(Id(), new JsonObject { ["identity"] = new string('a', 64), ["status"] = 200, ["body"] = "{\"runId\":\"pr-" + new string('0', 32) + "\"}" });
                b["revision"] = revision + 1;
            }, true), "STORE_CORRUPT", "decode"),
            ("a revision above the receipt count", Tamper(good, b => b["revision"] = revision + 1, true), "VERIFY_REVISION_MISMATCH", "revision"),
            ("a duplicated archived run", Tamper(good, b => b["archive"]!.AsArray().Add(b["archive"]![0]!.DeepClone()), true), "STORE_CORRUPT", "decode"),
            ("an archived run removed with its receipts kept", Tamper(good, b => b["archive"]!.AsArray().RemoveAt(0), true), "STORE_CORRUPT", "decode"),
            // The host's own decode refusals come first and are reported unchanged.
            ("other content", Tamper(good, b => b["binding"]!["contentSha256"] = ZeroHex, true), "STORE_CONTENT_MISMATCH", "decode"),
            ("another Core build", Tamper(good, b => b["binding"]!["coreModule"] = new string('0', 32), true), "STORE_IMPLEMENTATION_MISMATCH", "decode"),
            ("another fixture", Tamper(good, b => b["binding"]!["fixtureSha256"] = ZeroHex, true), "STORE_FIXTURE_MISMATCH", "decode"),
            ("an unbacked recruitment balance", Tamper(good, b => b["recruitment"]!["wallet"]!["freeCrystals"] = b["recruitment"]!["wallet"]!["freeCrystals"]!.GetValue<long>() + 160, true),
                "STORE_CORRUPT", "decode"),
            ("a body edit without its digest", Tamper(good, b => b["revision"] = revision + 1, false), "STORE_CORRUPT", "decode"),
            ("garbage bytes", Encoding.UTF8.GetBytes("{not json"), "STORE_CORRUPT", "decode"),
        };
        // Controls: the same re-digest and receipt re-encoding paths without an edit pass, so each refusal below is its edit's.
        foreach (var (what, bytes) in new[] { ("a re-digested but unedited envelope", Tamper(good, b => { }, true)),
            ("an unedited run receipt re-encoding", Tamper(good, b => EditReceipt(b, history.CreateC, r => { }), true)) })
        {
            WritePrivate(file, bytes);
            string before = StoreSnapshot(directory);
            var control = await RunVerifier(what, Config(root, directory));
            Same(control.Exit, 0, what + ": verify-store passes (exit 0)");
            Check(VerifyText(VerifyLine(control, "account", "alpha"), "status") == "PASS", what + ": alpha passes");
            SameVerifyResult(control, "PASS", 3, 1, 2, 0, 0, what);
            Same(StoreSnapshot(directory), before, what + ": verify-store wrote nothing");
        }
        foreach (var item in cases)
        {
            WritePrivate(file, item.Bytes);
            string before = StoreSnapshot(directory);
            var run = await RunVerifier(item.What, Config(root, directory));
            var line = VerifyLine(run, "account", "alpha");
            CheckLine(run.Exit == 1 && VerifyText(line, "status") == "FAIL" && VerifyText(line, "code") == item.Code && VerifyText(line, "at") == item.At
                && !run.Lines.Any(l => l.Kind != "account" && l.Members.TryGetValue("id", out string? id) && id == "alpha"), line,
                item.What + ": verify-store fails alpha with " + item.Code + " at " + item.At + " (exit 1) and reports no capacity for it");
            SameVerifyResult(run, "FAIL", 3, 0, 2, 1, 0, item.What);
            Same(StoreSnapshot(directory), before, item.What + ": the refused envelope is preserved byte for byte and nothing was written");
        }
    }

    // ───────── a legitimately saved deck above the tutorial capacity is a HOLD (test-local content and fixture) ─────────
    async Task VerifyStoreCapacityHold()
    {
        string contentRoot = Path.Combine(temp, "verify-capacity-content");
        Directory.CreateDirectory(Path.Combine(contentRoot, "data"));
        var content = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "data", "game_data.json")))!;
        var costs = content["rules"]!["memoryCost"]!;
        foreach (string add in new[] { "addB", "addA", "addS", "addCommon" }) costs[add] = 7;
        File.WriteAllText(Path.Combine(contentRoot, "data", "game_data.json"), content.ToJsonString());
        string id = "omega", token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        tokens[id] = token;
        var party = new JsonArray();
        foreach (string cid in Party) party.Add(new JsonObject { ["charId"] = cid, ["asc"] = 4 });
        string fixturePath = Path.Combine(temp, "fixture-verify-capacity.json");
        WritePrivate(fixturePath, new JsonObject
        {
            ["schema"] = "mf-dev-practice-fixture-1", ["developmentOnly"] = true,
            ["label"] = "MemoryForge.ServerChecks verify-store capacity fixture: development/test only, Asc 4 reference party, not a production owned collection",
            ["accounts"] = new JsonArray(new JsonObject { ["accountId"] = id, ["credentialSha256"] = Sha256Hex(token), ["length"] = "short", ["party"] = party })
        }.ToJsonString());
        string directory = Path.Combine(temp, "store-verify-capacity"), file = AccountFile(directory, id);
        var config = Config(contentRoot, directory, fixturePath);
        var host = Launch("verify-store capacity host", config);
        if (!await host.WaitListening(TimeSpan.FromSeconds(120)))
        {
            Record(host.Name + " output", host.Output);
            throw new CheckFailure(host.Name + " did not start listening (output in diagnostics.log)");
        }
        var api = Client(host);
        // Reward cards a tutorial candidate owns (Harin or Nia in this party), from the test content's own card definitions.
        var candidateCards = new HashSet<string>(RunDataLoader.Load(contentRoot).Memory.Battle.Cards.Where(card => TutorialCandidates.Contains(card.Owner)).Select(card => card.Id),
            StringComparer.Ordinal);
        string? savedFor = null;
        int memoryVersion = 0;
        for (int attempt = 0; savedFor == null && attempt < 6; attempt++)
        {
            var created = await api.Post(RunsPath, token, Obj(("requestId", Id())));
            Status(created, 200, null, "the capacity scenario departs");
            string runId = S(created.Json, "runId");
            seeds.Add(ReadBody(file).GetProperty("run").GetProperty("seed").GetInt64());
            var played = await PlayToDefeat(api, token, runId, created.Json.GetProperty("state"), new List<string>(), cards => cards.FindIndex(card => candidateCards.Contains(card.GetString()!)));
            var change = played.TakenKey == null ? null : AddChange(played.State, played.TakenKey);
            var saved = await Commit(api, token, runId, Id(), I(played.State, "version"), memoryVersion, change == null ? Array.Empty<string>() : new[] { change.Value.Id });
            Status(saved, 200, null, "the capacity scenario's save within the Asc 4 fixture capacity");
            if (change != null) savedFor = change.Value.Cid;
            memoryVersion++;
        }
        host.Kill();
        if (savedFor == null) throw new CheckFailure("within six departures the check bot did not win a battle to save a Harin or Nia card (a bounded check-bot limit, not an observed server result)");
        var body = ReadBody(file);
        string before = StoreSnapshot(directory);
        var run = await RunVerifier("the capacity scenario", config);
        Same(run.Exit, 4, "the capacity scenario: a verified account whose saved deck exceeds the tutorial capacity is a HOLD (exit 4)");
        SamePass(run, id, body, "the capacity scenario");
        Check(SameCapacity(run, id, body, contentRoot, "the capacity scenario"), "the capacity scenario: check-side Core also finds a candidate's saved deck above the tutorial capacity");
        var line = VerifyLine(run, "capacity", id, savedFor);
        CheckLine(VerifyText(line, "tutorial") == "OVER_CAPACITY" && VerifyNumber(line, "used") >= 7 && VerifyNumber(line, "tutorialCapacity") == 6 && VerifyText(line, "owned") == "NOT_OWNED", line,
            "the capacity scenario: " + savedFor + "'s legitimately saved deck (at least 7, within its Asc 4 capacity) exceeds the tutorial capacity 6; nothing is owned");
        SameVerifyResult(run, "HOLD", 1, 1, 0, 0, 1, "the capacity scenario");
        Same(StoreSnapshot(directory), before, "the capacity scenario: the held account is preserved byte for byte and nothing was written");
    }

    // ───────── PostgreSQL mode, after the recruitment scenario: every fixture alias read through the backend, no row changed ─────────
    public async Task RunPostgresVerifyStore()
    {
        string[] roles = { "alpha", "beta", "gamma", "delta" };
        var rows = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string role in roles) rows[role] = await Stored(Alias(role));
        var run = await RunVerifier("postgres", PostgresConfig(root));
        Same(run.Exit, 1, "postgres: verify-store fails overall (exit 1) because one alias is bound to the faulty content");
        foreach (string role in new[] { "alpha", "beta", "delta" })
        {
            var body = BodyOf(rows[role]);
            SamePass(run, Alias(role), body, "postgres");
            Check(!SameCapacity(run, Alias(role), body, root, "postgres"), "postgres: " + role + "'s saved decks fit the tutorial capacity");
        }
        var gamma = VerifyLine(run, "account", Alias("gamma"));
        CheckLine(VerifyText(gamma, "status") == "FAIL" && VerifyText(gamma, "code") == "STORE_CONTENT_MISMATCH" && VerifyText(gamma, "at") == "decode", gamma,
            "postgres: the faulty-content alias is refused by the host's own decode (STORE_CONTENT_MISMATCH)");
        SameVerifyResult(run, "FAIL", 4, 3, 0, 1, 0, "postgres", "postgres");
        foreach (string role in roles)
        {
            Same(await Stored(Alias(role)), rows[role], "postgres: verify-store left " + role + "'s row byte-identical");
            Check(await Oracle.Rows(Alias(role)) == 1, "postgres: " + role + " still has exactly one row");
        }
        completed.Add("pg-verify-store");
        LeakScan(PostgresScenarios.Concat(new[] { "pg-recruitment", "pg-verify-store" }).ToArray());
    }

    // ───────── report checks ─────────
    // An account's PASS line against its stored body (an oracle read): every run record, journal entry and receipt is counted.
    void SamePass(VerifyRun run, string id, JsonElement body, string what)
    {
        var line = VerifyLine(run, "account", id);
        var archive = A(body, "archive");
        var current = body.GetProperty("run");
        bool active = current.ValueKind == JsonValueKind.Object;
        int checkpoints = archive.Sum(r => 1 + r.GetProperty("journal").GetArrayLength()) + (active ? 1 + current.GetProperty("journal").GetArrayLength() : 0);
        int receipts = body.GetProperty("receipts").EnumerateObject().Count();
        int ledger = body.TryGetProperty("recruitment", out var recruitment) ? recruitment.GetProperty("ledger").GetArrayLength() : 0;
        long revision = body.GetProperty("revision").GetInt64();
        CheckLine(VerifyText(line, "status") == "PASS" && VerifyNumber(line, "revision") == revision && VerifyNumber(line, "receipts") == receipts && receipts == revision
            && VerifyNumber(line, "runReceipts") == checkpoints && VerifyNumber(line, "recruitmentReceipts") == ledger && checkpoints + ledger == receipts
            && VerifyNumber(line, "runs") == archive.Count + (active ? 1 : 0) && VerifyNumber(line, "archived") == archive.Count && VerifyNumber(line, "current") == (active ? 1 : 0)
            && VerifyNumber(line, "checkpoints") == checkpoints && VerifyNumber(line, "memoryVersion") == body.GetProperty("memory").GetProperty("version").GetInt64(), line,
            what + ": " + id + " passes with each of its " + checkpoints + " run checkpoints and " + receipts + " receipts accounted for");
    }

    // The capacity and probe lines against check-side Core over the stored decks and recruited copies (the content's own linked loaders).
    // Returns whether a candidate's starting deck exceeds the tutorial capacity; Core's Create must then refuse both useMemory values.
    bool SameCapacity(VerifyRun run, string id, JsonElement? body, string contentRoot, string what)
    {
        var rules = RunDataLoader.Load(contentRoot).Memory;
        JsonElement? decks = body?.GetProperty("memory").GetProperty("memory");
        JsonElement? owned = body is JsonElement stored && stored.TryGetProperty("recruitment", out var recruitment) ? recruitment.GetProperty("ownedChars") : null;
        bool over = false;
        foreach (string cid in TutorialCandidates)
        {
            var baseline = MemoryV2.Baseline(rules, cid).Select(c => (object?)c.ToJsonGraph()).ToList();
            object? saved = decks is JsonElement d && d.TryGetProperty(cid, out var rows) ? MemoryV2DataLoader.Graph(rows) : baseline;
            long used = MemoryV2.Cost(rules, cid, saved, baseline);
            int tutorial = MemoryV2.CapacityOf(rules, new MemoryCapacityEntry { CharId = cid });
            int copies = owned is JsonElement o && o.TryGetProperty(cid, out var count) ? count.GetInt32() : 0;
            var g = new GachaState();
            if (copies > 0) g.OwnedChars[cid] = copies;
            int res = Gacha.Resonance(g, cid), ownedCapacity = MemoryV2.CapacityOf(rules, new MemoryCapacityEntry { CharId = cid, Res = res });
            over |= used > tutorial;
            var line = VerifyLine(run, "capacity", id, cid);
            CheckLine(VerifyNumber(line, "used") == used && VerifyNumber(line, "tutorialCapacity") == tutorial && VerifyText(line, "tutorial") == (used > tutorial ? "OVER_CAPACITY" : "OK")
                && VerifyNumber(line, "copies") == copies && (copies == 0
                    ? VerifyText(line, "res") == "-" && VerifyText(line, "asc") == "-" && VerifyText(line, "ownedCapacity") == "-" && VerifyText(line, "owned") == "NOT_OWNED"
                    : VerifyNumber(line, "res") == res && VerifyNumber(line, "asc") == 0 && VerifyNumber(line, "ownedCapacity") == ownedCapacity
                        && VerifyText(line, "owned") == (used > ownedCapacity ? "OVER_CAPACITY" : "OK")), line,
                what + ": " + id + "'s " + cid + " capacity report equals check-side Core over the stored deck and copies");
        }
        var probe = VerifyLine(run, "probe", id);
        string expected = over ? "OVER_CAPACITY" : "OK";
        CheckLine(VerifyText(probe, "party") == "nia,harin,rien" && VerifyText(probe, "useMemoryTrue") == expected && VerifyText(probe, "useMemoryFalse") == expected, probe,
            what + ": Core's Create of the frozen tutorial party for " + id + " answers " + expected + " for useMemory true and false");
        return over;
    }

    void SameVerifyResult(VerifyRun run, string status, int accounts, int verified, int absent, int failed, int held, string what, string backend = "file")
    {
        var start = VerifyLine(run, "start", null);
        var result = VerifyLine(run, "result", null);
        CheckLine(VerifyText(start, "mode") == "read-only" && VerifyText(start, "backend") == backend && VerifyNumber(start, "accounts") == accounts
            && run.Lines.Count(l => l.Kind == "account") == accounts && run.Lines[^1].Kind == "result" && VerifyText(result, "status") == status
            && VerifyNumber(result, "accounts") == accounts && VerifyNumber(result, "verified") == verified && VerifyNumber(result, "absent") == absent
            && VerifyNumber(result, "failed") == failed && VerifyNumber(result, "held") == held, result,
            what + ": the report covers all " + accounts + " configured accounts and ends " + status);
    }

    // ───────── verifier process and report helpers ─────────
    async Task<VerifyRun> RunVerifier(string what, IReadOnlyDictionary<string, string> config)
    {
        var process = Launch("verify-store: " + what, config, arguments: new[] { VerifyFlag });
        if (!await process.WaitExit(TimeSpan.FromSeconds(600)))
        {
            process.Kill();
            throw new CheckFailure(what + ": verify-store did not finish within 600 seconds");
        }
        var lines = new List<(string Kind, Dictionary<string, string> Members)>();
        bool clean = true;
        foreach (string raw in process.Output.Split('\n'))
        {
            string text = raw.TrimEnd('\r');
            if (text.Length == 0) continue;
            string[] words = text.Split(' ');
            if (words.Length < 2 || words[0] != "verify-store" || !VerifyLineKinds.Contains(words[1])) { clean = false; continue; }
            var members = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string word in words.Skip(2))
            {
                int separator = word.IndexOf('=');
                if (separator <= 0 || !members.TryAdd(word.Substring(0, separator), word.Substring(separator + 1))) clean = false;
            }
            lines.Add((words[1], members));
        }
        if (!clean) Record(what + ": verify-store output", process.Output);
        Check(clean && !process.ListeningSeen, what + ": verify-store never listens and prints only its key=value report lines");
        return new VerifyRun(process.Process.ExitCode, lines);
    }

    // The single report line of a kind (and account / character): exactly one, else a check failure.
    static Dictionary<string, string> VerifyLine(VerifyRun run, string kind, string? id, string? cid = null)
    {
        var matches = run.Lines.Where(l => l.Kind == kind && (id == null || l.Members.TryGetValue("id", out string? at) && at == id)
            && (cid == null || l.Members.TryGetValue("char", out string? character) && character == cid)).ToList();
        if (matches.Count != 1) throw new CheckFailure("verify-store printed " + matches.Count + " '" + SafeName(kind) + "' report lines where exactly one was expected");
        return matches[0].Members;
    }
    static string VerifyText(Dictionary<string, string> line, string key) =>
        line.TryGetValue(key, out string? value) ? value : throw new CheckFailure("a verify-store report line lacks " + SafeName(key));
    static long VerifyNumber(Dictionary<string, string> line, string key) =>
        long.TryParse(VerifyText(line, key), NumberStyles.None, CultureInfo.InvariantCulture, out long value) ? value : throw new CheckFailure("a verify-store report member is not an integer: " + SafeName(key));
    // The report holds only aliases, codes, positions, counts and costs; on a mismatch the line goes to diagnostics.log as well.
    void CheckLine(bool condition, Dictionary<string, string> line, string what)
    {
        if (!condition) Record(what, string.Join(" ", line.Select(p => p.Key + "=" + p.Value)));
        Check(condition, what);
    }

    // Every entry of a store directory (name, mode, mtime, content digest) plus the directory's own mode and mtime: equal snapshots mean no
    // byte, file, permission or timestamp changed and nothing was created or removed.
    static string StoreSnapshot(string directory)
    {
        var lines = new List<string> { "directory " + File.GetUnixFileMode(directory) + " " + Directory.GetLastWriteTimeUtc(directory).Ticks.ToString(CultureInfo.InvariantCulture) };
        foreach (string path in Directory.EnumerateFileSystemEntries(directory).OrderBy(p => p, StringComparer.Ordinal))
            lines.Add(Path.GetFileName(path) + " " + File.GetUnixFileMode(path) + " " + File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture) + " "
                + (File.Exists(path) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) : "directory"));
        return string.Join("\n", lines);
    }

    // A stored run receipt's reply, edited and re-encoded the way the server writes JSON (relaxed escaping, no indentation).
    static void EditReceipt(JsonObject body, string requestId, Action<JsonNode> edit)
    {
        var receipt = body["receipts"]![requestId]!.AsObject();
        var reply = JsonNode.Parse(receipt["body"]!.GetValue<string>())!;
        edit(reply);
        receipt["body"] = reply.ToJsonString(ServerJson);
    }
}
