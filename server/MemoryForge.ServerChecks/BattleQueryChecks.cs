// P1-PUBLIC-BATTLE-QUERY §17.4/§14.7/§14.3: POST /dev/practice/runs/{runId}/battle-query of a real host process over real HTTP. Every 200
// reply is compared member by member with the existing check-side Core oracle (CheckSideRun: Run.Create from the stored inputs plus the
// stored journal, assign entries through Run.Assign; nothing is injected or restored). Every query, accepted or refused, must leave the
// committed envelope byte-identical (same full SHA-256), the served run view unchanged and create no account file/row. Only legitimate
// commands (enter, play, end, ult, assign, takeCard, proceed and, after a lost battle, an unchanged save and a new departure) move the
// run between the query rounds, and at least one round answers a server journal version apart from battle.v. The body is
// backend-neutral (stored/unborn delegates): the file suite runs it as "battle-query", the PostgreSQL mode as "pg-battle-query", both on
// alpha's current departure, and every reply stays in the final complete-capture privacy scan.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MemoryForge.Core;

sealed partial class ServerChecks
{
    static readonly string[] QueryMembers = { "runId", "version", "phase", "context", "source", "slots", "forecast", "choices" };
    static readonly string[] ForecastMembers =
        { "error", "mode", "cost", "due", "hidden", "enemyHpLoss", "enemyDamage", "newlyBroken", "blockDelta", "hpDelta", "defeat", "enemyHpLosses" };
    static readonly string[] EnemyHpLossMembers = { "uid", "hpLoss" };

    static string QueryPath(string runId) => RunsPath + "/" + runId + "/battle-query";
    static Task<Result> Query(Api api, string? token, string runId, int version, string uid, string? mode = null, string? target = null, IReadOnlyList<string>? picks = null)
    {
        var body = new JsonObject { ["expectedVersion"] = version, ["uid"] = uid };
        if (mode != null) body["mode"] = mode;
        if (target != null) body["target"] = target;
        if (picks != null) body["picks"] = Ids(picks);
        return api.Post(QueryPath(runId), token, body.ToJsonString());
    }

    // File suite: alpha's current departure (version 0, on the map) on the main host, after the ownership and write-failure scenarios.
    // gamma never committed anything on this store, so its file must stay absent.
    async Task BattleQuery(ServerProcess server)
    {
        string file = AccountFile(store, "alpha"), unborn = AccountFile(store, "gamma");
        await BattleQueryScenario(Client(server), tokens["alpha"], tokens["beta"], tokens["gamma"], () => Task.FromResult(File.ReadAllBytes(file)),
            () => Task.FromResult(!File.Exists(unborn)), "");
        completed.Add("battle-query");
    }

    // token owns alphaRunId (a fresh departure at version 0 on the map); otherToken owns another run; unbornToken's account has nothing
    // stored. stored() reads the committed envelope bytes; unborn() is true while unbornToken's account has no file/row.
    async Task BattleQueryScenario(Api api, string token, string otherToken, string unbornToken, Func<Task<byte[]>> stored, Func<Task<bool>> unborn, string label)
    {
        string runId = alphaRunId, path = QueryPath(runId);
        var content = RunDataLoader.Load(root).Memory.Battle;
        byte[] before = await stored();
        var record = BodyOf(before).GetProperty("run");
        Check(S(record, "runId") == runId && record.GetProperty("journal").GetArrayLength() == 0, "precondition: the battle query run is the account's fresh current departure" + label);

        // ── refused on the map: credential, ownership, no file, no battle, version and the strict body. Nothing may be written. ──
        Status(await Query(api, null, runId, 0, "c1"), 401, "UNAUTHORIZED", "an anonymous battle query" + label);
        Status(await Query(api, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24)), runId, 0, "c1"), 401, "UNAUTHORIZED", "a battle query with an unknown credential" + label);
        Status(await Query(api, otherToken, runId, 0, "c1"), 404, "RUN_NOT_FOUND", "another account's battle query" + label);
        Status(await Query(api, unbornToken, runId, 0, "c1"), 404, "RUN_NOT_FOUND", "a battle query from an account without a run" + label);
        Status(await Query(api, unbornToken, "pr-" + new string('0', 32), 0, "c1"), 404, "RUN_NOT_FOUND", "a battle query for an unknown run" + label);
        Status(await api.Post(RunsPath + "/not-a-run/battle-query", token, "{\"expectedVersion\":0,\"uid\":\"c1\"}"), 404, "RUN_NOT_FOUND", "a battle query for a malformed run ID" + label);
        Check(await unborn(), "refused battle queries created no account file or row" + label);
        Status(await Query(api, token, runId, 0, "c1"), 422, "NO_BATTLE", "a battle query on the map (wrong phase)" + label);
        Status(await Query(api, token, runId, 1, "c1"), 409, "STALE_VERSION", "a battle query at a version not yet committed" + label);
        string valid = "{\"expectedVersion\":0,\"uid\":\"c1\"";
        foreach (var (what, body, code) in new (string, string, string)[]
        {
            ("malformed JSON", "not json", "INVALID_JSON"),
            ("a non-object body", "[]", "INVALID_REQUEST"),
            ("a requestId", valid + ",\"requestId\":\"" + Id() + "\"}", "UNKNOWN_FIELD"),
            ("a client seed", valid + ",\"seed\":7}", "UNKNOWN_FIELD"),
            ("a client state", valid + ",\"state\":{}}", "UNKNOWN_FIELD"),
            ("a client battle version", valid + ",\"v\":1}", "UNKNOWN_FIELD"),
            ("an arbitrary operation", valid + ",\"kind\":\"end\"}", "UNKNOWN_FIELD"),
            ("a duplicate member", valid + ",\"uid\":\"c2\"}", "DUPLICATE_FIELD"),
            ("no expectedVersion", "{\"uid\":\"c1\"}", "INVALID_REQUEST"),
            ("a fractional expectedVersion", "{\"expectedVersion\":0.5,\"uid\":\"c1\"}", "INVALID_REQUEST"),
            ("a negative expectedVersion", "{\"expectedVersion\":-1,\"uid\":\"c1\"}", "INVALID_REQUEST"),
            ("a string expectedVersion", "{\"expectedVersion\":\"0\",\"uid\":\"c1\"}", "INVALID_REQUEST"),
            ("no uid", "{\"expectedVersion\":0}", "INVALID_ARGS"),
            ("a numeric uid", "{\"expectedVersion\":0,\"uid\":7}", "INVALID_ARGS"),
            ("an empty uid", "{\"expectedVersion\":0,\"uid\":\"\"}", "INVALID_ARGS"),
            ("an unknown mode", valid + ",\"mode\":\"sideways\"}", "INVALID_ARGS"),
            ("a boolean target", valid + ",\"target\":true}", "INVALID_ARGS"),
            ("a nested pick list", valid + ",\"picks\":[[\"c2\"]]}", "INVALID_ARGS"),
            ("an object pick list", valid + ",\"picks\":{\"0\":\"c2\"}}", "INVALID_ARGS"),
            ("a null pick", valid + ",\"picks\":[null]}", "INVALID_ARGS"),
            ("65 picks", valid + ",\"picks\":[" + string.Join(",", Enumerable.Repeat("\"c2\"", 65)) + "]}", "INVALID_ARGS"),
        })
            Status(await api.Post(path, token, body), 400, code, "a battle query with " + what + label);
        Status(await api.Post(path, token, valid + "}", "text/plain"), 415, "UNSUPPORTED_MEDIA_TYPE", "a battle query that is not JSON" + label);
        Status(await api.Get(path, token), 405, "METHOD_NOT_ALLOWED", "GET on the battle query" + label);
        Same(await stored(), before, "refused battle queries left the stored envelope byte-identical" + label);
        Check(await unborn(), "refused battle queries still created no account file or row" + label);

        // ── v2 assignment boundary on the map. Run.Assign has no phase check, so a legitimate assignment advances the server journal
        // version without touching battle.v. Each common deck key is tried on its own fresh oracle (never on the host): in v2 the keys of
        // the starting memory, the base common cards included, are refused; only a common card gained in this run can be assigned. ──
        var probes = AssignProbe(record);
        Check(probes.Count > 0 && probes.All(p => p.Refusal == null || p.Refusal == "REASSIGNMENT_REQUIRES_SEPARATE_FLOW"),
            "every common deck card of the departure is assignable or a v2 starting-memory key" + label);
        var savedKey = probes.FirstOrDefault(p => p.Refusal != null);
        if (savedKey.Key != null)
        {
            Status(await Command(api, token, runId, Id(), 0, "assign", Assignment(savedKey.Key, savedKey.CharId)), 422, "REASSIGNMENT_REQUIRES_SEPARATE_FLOW",
                "the host refuses to reassign a saved common key, as Core does" + label);
            Same(await stored(), before, "the refused assignment wrote nothing" + label);
        }
        var assignable = probes.Where(p => p.Refusal == null).ToList();

        // ── a real battle: every hand card and mode against the oracle, then two legitimate plays and another full round ──
        var map = await api.Get(RunsPath + "/" + runId, token);
        Status(map, 200, null, "the run read on the map" + label);
        var entered = await Command(api, token, runId, Id(), 0, "enter", new JsonObject { ["nodeId"] = A(map.Json.GetProperty("state"), "available")[0].GetString() });
        Status(entered, 200, null, "enter a battle for the battle queries" + label);
        int version = 1;
        Check(I(entered.Json, "version") == version && S(entered.Json.GetProperty("state"), "phase") == "BATTLE"
            && S(entered.Json.GetProperty("state").GetProperty("battle"), "phase") == "PLAYER", "the query run is in a player turn at version 1" + label);
        bool assigned = false, distinct = false;
        if (assignable.Count > 0) { version = await AssignInBattle(api, token, runId, version, assignable[0], label); assigned = true; }
        var oracle = await QueryAllSources(api, token, otherToken, stored, unborn, content, runId, version, true, label);
        distinct |= oracle.State.Battle!.V != version;

        // A reservation (when a hand card may legally reserve) and an immediate play put cards outside the hand, so their zones are
        // probed too. Choices come from the oracle's own forecast; nothing is forced.
        foreach (string[] modes in new[] { new[] { "next", "after" }, new[] { "now" } })
        {
            var b = oracle.State.Battle;
            if (b == null) break;
            string? aimAt = b.Enemies.FirstOrDefault(e => e.Alive)?.Uid;
            (string Uid, string Mode, string? Aim)? choice = null;
            foreach (string uid in b.Zones.Hand)
                foreach (string mode in modes)
                {
                    string? aim = oracle.BattleNeedsTarget(uid) == null ? null : aimAt;
                    if (choice == null && oracle.BattleForecast(uid, mode, aim).Error == null) choice = (uid, mode, aim);
                }
            if (choice == null) continue;
            var args = new JsonObject { ["uid"] = choice.Value.Uid, ["mode"] = choice.Value.Mode };
            if (choice.Value.Aim != null) args["target"] = choice.Value.Aim;
            var played = await Command(api, token, runId, Id(), version, "play", args);
            Status(played, 200, null, "a legitimate " + choice.Value.Mode + " play between battle query rounds" + label);
            Check(I(played.Json, "version") == ++version, "the battle queries consumed no run version: the play commits the next one" + label);
            oracle = QueryOracle(BodyOf(await stored()).GetProperty("run"));
            var afterPlay = oracle.State.Battle;
            Check(afterPlay == null || !afterPlay.Zones.Hand.Contains(choice.Value.Uid), "the played card left the hand" + label);
        }
        if (oracle.State.Battle != null)
        {
            var second = await QueryAllSources(api, token, otherToken, stored, unborn, content, runId, version, false, label);
            distinct |= second.State.Battle!.V != version;
        }
        else Status(await Query(api, token, runId, version, "c1"), 422, "NO_BATTLE", "a battle query after the battle ended" + label);

        // ── the server journal version apart from battle.v: in a run's first battle they coincide (enter ↔ begin) unless an assignment
        // was possible, so legitimate progression reaches a later battle (and assigns a common card gained on the way when one exists). ──
        if (!distinct)
        {
            (runId, version) = await LaterBattle(api, token, runId, version, stored, label);
            if (!assigned)
            {
                var gained = AssignProbe(BodyOf(await stored()).GetProperty("run")).Where(p => p.Refusal == null).ToList();
                if (gained.Count > 0) { version = await AssignInBattle(api, token, runId, version, gained[0], label); assigned = true; }
            }
            var later = await QueryAllSources(api, token, otherToken, stored, unborn, content, runId, version, true, label);
            int battleVersion = later.State.Battle!.V;
            Check(battleVersion < version, "a later battle's query round ran at a server journal version above its battle.v" + label);
            distinct = battleVersion != version;
        }
        Check(distinct, "a real query round answered a server journal version different from battle.v, each matched to its own source" + label);
    }

    // The v2 assignment boundary of a stored run record: every common deck card's key with a party member other than its current one,
    // and Run.Assign's own refusal code when a fresh check-side oracle of that record refuses it (null: accepted). Never touches the host.
    List<(string Key, string CharId, string? Refusal)> AssignProbe(JsonElement record)
    {
        var state = QueryOracle(record).State;
        var party = state.Party.Select(p => p.CharId).ToList();
        var result = new List<(string Key, string CharId, string? Refusal)>();
        foreach (var card in state.Deck.Where(c => c.Owner == "common" && c.Key != null))
        {
            string key = card.Key!, charId = party.FirstOrDefault(id => id != card.Assigned) ?? party[0];
            string? refusal = null;
            try { QueryOracle(record).Assign(key, charId); }
            catch (RuleException error) { refusal = error.Code; }
            result.Add((key, charId, refusal));
        }
        return result;
    }
    static JsonObject Assignment(string key, string charId) => new JsonObject { ["key"] = key, ["charId"] = charId };

    // One legitimate assign inside the current battle: the server journal version advances by one while the served battle (battle.v
    // included) stays exactly as it was and the deck card carries its new assignment.
    async Task<int> AssignInBattle(Api api, string token, string runId, int version, (string Key, string CharId, string? Refusal) card, string label)
    {
        var read = await api.Get(RunsPath + "/" + runId, token);
        Status(read, 200, null, "the run read before an assignment" + label);
        var reply = await Command(api, token, runId, Id(), version, "assign", Assignment(card.Key, card.CharId));
        Status(reply, 200, null, "a legitimate common-card assignment inside the battle" + label);
        var before = read.Json.GetProperty("state"); var after = reply.Json.GetProperty("state");
        Check(I(reply.Json, "version") == version + 1 && I(after.GetProperty("battle"), "v") == I(before.GetProperty("battle"), "v") && Raw(after, "battle") == Raw(before, "battle")
            && A(after, "deck").Any(c => c.TryGetProperty("key", out var key) && key.GetString() == card.Key && c.TryGetProperty("assigned", out var to) && to.GetString() == card.CharId),
            "the assignment advanced the server journal version only: battle.v and the battle view are unchanged" + label);
        return version + 1;
    }

    // Legitimate progression to a later battle with the existing greedy check bot (Choose): it finishes the current battle; a victory takes
    // the first offered card (else proceeds) and enters the next battle node (non-battle nodes proceed); a lost battle is saved unchanged
    // and the account departs again (each new seed joins the privacy scan). Bounded like the expedition's own check-bot limit. Returns the
    // run and its committed version once a later battle (battle.v below the server journal version) waits for the player.
    async Task<(string RunId, int Version)> LaterBattle(Api api, string token, string runId, int version, Func<Task<byte[]>> stored, string label)
    {
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        var last = await api.Get(RunsPath + "/" + runId, token);
        Status(last, 200, null, "the run read before moving on to a later battle" + label);
        var reply = last.Json; var state = reply.GetProperty("state");
        string lastKind = "";
        int departures = 0;
        for (int guard = 0; guard < 3000; guard++)
        {
            string phase = S(state, "phase");
            if (phase == "BATTLE" && S(state.GetProperty("battle"), "phase") == "PLAYER" && I(state.GetProperty("battle"), "v") < version) return (runId, version);
            (string Kind, JsonObject Args, string Key) command;
            switch (phase)
            {
                case "BATTLE":
                    var battle = state.GetProperty("battle");
                    command = S(battle, "phase") == "PLAYER" ? Choose(battle, rejected, version) ?? ("end", new JsonObject(), "end") : ("end", new JsonObject(), "end");
                    break;
                case "REWARD":
                    var cards = A(state.GetProperty("pending"), "cards");
                    command = lastKind != "takeCard" && cards.Count > 0 ? ("takeCard", new JsonObject { ["id"] = cards[0].GetString() }, "takeCard") : ("proceed", new JsonObject(), "proceed");
                    break;
                case "MAP":
                    var types = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var row in state.GetProperty("map").GetProperty("floors").EnumerateArray()) foreach (var node in row.EnumerateArray()) types[S(node, "id")] = S(node, "type");
                    var options = A(state, "available").Select(n => n.GetString()!).ToList();
                    string next = options.FirstOrDefault(id => types.TryGetValue(id, out var type) && type == "battle")
                        ?? options.FirstOrDefault(id => types.TryGetValue(id, out var type) && type == "elite") ?? options[0];
                    command = ("enter", new JsonObject { ["nodeId"] = next }, "enter");
                    break;
                case "REST": case "FORGE": case "SHOP": case "EVENT": command = ("proceed", new JsonObject(), "proceed"); break;
                case "ARCHIVE": command = ("archive", new JsonObject { ["choice"] = "return" }, "archive"); break;
                case "DEFEAT":
                    if (++departures > 3) throw new CheckFailure("within four departures the check bot did not reach a later battle (a bounded check-bot limit, not an observed server result)" + label);
                    var saved = await Commit(api, token, runId, Id(), version, I(reply, "memoryVersion"));
                    Status(saved, 200, null, "the unchanged save of a lost battle before departing again" + label);
                    var departed = await api.Post(RunsPath, token, Obj(("requestId", Id())));
                    Status(departed, 200, null, "a new departure after a lost battle" + label);
                    runId = S(departed.Json, "runId"); alphaRunId = runId; version = 0; rejected.Clear(); lastKind = "";
                    seeds.Add(BodyOf(await stored()).GetProperty("run").GetProperty("seed").GetInt64());
                    reply = departed.Json; state = reply.GetProperty("state");
                    continue;
                default:
                    Record("unexpected run phase", phase);
                    throw new CheckFailure("the run reached a phase the battle-query progression does not handle (value in diagnostics.log)" + label);
            }
            var result = await Command(api, token, runId, Id(), version, command.Kind, command.Args);
            if (result.Status == 422 && (command.Kind == "play" || command.Kind == "ult")) { rejected.Add(version.ToString(CultureInfo.InvariantCulture) + ":" + command.Key); continue; }
            Status(result, 200, null, "battle-query progression " + command.Kind + label);
            Check(I(result.Json, "version") == version + 1, "each progression command commits exactly the next server version" + label);
            version++; reply = result.Json; state = reply.GetProperty("state"); lastKind = command.Kind;
        }
        throw new CheckFailure("the battle-query progression did not reach a later battle within 3000 commands" + label);
    }

    // The check-side oracle of a stored run record that may hold assign entries: the existing CheckSideRun creates the Run from the same
    // stored inputs with an empty journal, then each stored entry is dispatched in order (assign through Run.Assign, the rest through the
    // existing CoreExecute). Nothing is injected.
    Run QueryOracle(JsonElement record)
    {
        var created = JsonNode.Parse(record.GetRawText())!.AsObject();
        created["journal"] = new JsonArray();
        using var document = JsonDocument.Parse(created.ToJsonString());
        var core = CheckSideRun(document.RootElement);
        foreach (var entry in record.GetProperty("journal").EnumerateArray())
        {
            string kind = S(entry, "kind"); var args = entry.GetProperty("args");
            if (kind == "assign") core.Assign(S(args, "key"), S(args, "charId"));
            else CoreExecute(core, kind, args);
        }
        return core;
    }

    // One full query round at the committed version: the served view and every hand card in every mode (an enemy-target card also
    // now at every other living enemy; plus explicit picks for pick cards) against the oracle, a repeat, the fixed NOT_IN_HAND for every instance outside the hand and for no card, stale and foreign
    // queries, and byte/SHA-256 purity of the stored envelope and the served view. Returns the oracle of that version.
    async Task<Run> QueryAllSources(Api api, string token, string otherToken, Func<Task<byte[]>> stored, Func<Task<bool>> unborn, BattleData content,
        string runId, int version, bool requireDraw, string label)
    {
        byte[] committed = await stored();
        string sha = Convert.ToHexStringLower(SHA256.HashData(committed));
        var record = BodyOf(committed).GetProperty("run");
        Check(record.GetProperty("journal").GetArrayLength() == version, "the stored journal is at the queried server version" + label);
        var core = QueryOracle(record);
        var cs = core.State; var cb = cs.Battle ?? throw new CheckFailure("the check-side oracle has no battle" + label);
        var view = await api.Get(RunsPath + "/" + runId, token);
        Status(view, 200, null, "the run read before a battle query round" + label);
        var served = view.Json.GetProperty("state").GetProperty("battle");
        Check(I(view.Json, "version") == version && I(served, "v") == cb.V && I(served, "turn") == cb.Turn && A(served, "hand").Select(c => S(c, "uid")).SequenceEqual(cb.Zones.Hand),
            "precondition: the oracle is the served battle" + label);
        // RULES_NOW_PLAY: each served hand card carries the oracle's held (kept over a turn end) and retain (stays at the next end) flags.
        foreach (var card in A(served, "hand"))
            Check(card.GetProperty("held").GetBoolean() == cb.Cards[S(card, "uid")!].Held && card.GetProperty("retain").GetBoolean() == core.BattleRetains(S(card, "uid")!),
                "the served hand card's held and retain flags are the oracle's" + label);
        SameSlots(served.GetProperty("slots"), core, cb, "the run view's computed slots" + label);

        string? target = cb.Enemies.FirstOrDefault(e => e.Alive)?.Uid;
        foreach (string uid in cb.Zones.Hand)
        {
            string? aim = core.BattleNeedsTarget(uid) == null ? null : target;
            foreach (string mode in new[] { "now", "next", "after" })
            {
                var reply = await Query(api, token, runId, version, uid, mode, aim);
                Status(reply, 200, null, "the battle query of a hand card (" + mode + ")" + label);
                SameQuery(reply.Json, core, content, runId, version, uid, mode, aim, Array.Empty<string>(), "hand card " + mode + label);
            }
            // Every other living enemy as the immediate play's target too, so each per-enemy HP loss entry is matched to the enemy it
            // names, not only to the first one.
            if (aim != null)
                foreach (string other in cb.Enemies.Where(e => e.Alive && e.Uid != aim).Select(e => e.Uid))
                {
                    var reply = await Query(api, token, runId, version, uid, "now", other);
                    Status(reply, 200, null, "the battle query of a hand card at another living enemy" + label);
                    SameQuery(reply.Json, core, content, runId, version, uid, "now", other, Array.Empty<string>(), "hand card now at another living enemy" + label);
                }
            var sheet = core.BattleHandChoices(uid);
            if (sheet.Requests.Count == 0) continue;
            // An explicit first candidate, then the source itself, which is never on its own sheet (source exclusion).
            foreach (var picks in new[] { sheet.Candidates.Take(1).ToArray(), new[] { uid } })
            {
                var reply = await Query(api, token, runId, version, uid, "now", aim, picks);
                Status(reply, 200, null, "the battle query of a pick card with explicit picks" + label);
                SameQuery(reply.Json, core, content, runId, version, uid, "now", aim, picks, "pick card with explicit picks" + label);
                if (picks.Length == 1 && picks[0] == uid)
                    Check(OptionalText(reply.Json.GetProperty("choices"), "error") == BattleChoicePolicy.PickInvalid && I(reply.Json.GetProperty("choices"), "accepted") == 0,
                        "the source is never a pick of its own sheet" + label);
            }
        }
        string first = cb.Zones.Hand[0];
        var once = await Query(api, token, runId, version, first);
        var twice = await Query(api, token, runId, version, first);
        Status(once, 200, null, "a battle query with the default mode" + label);
        Same(twice.Body, once.Body, "the same battle query answers the same bytes" + label);

        // Hand-source gate: draw, discard, exhaust, reservation and enemy uids answer exactly like no card, before anything of them is read.
        var missing = await Query(api, token, runId, version, "c9999");
        Status(missing, 422, "NOT_IN_HAND", "a battle query for no card" + label);
        if (requireDraw) Check(cb.Zones.Draw.Count > 0, "precondition: the first-turn draw pile holds hidden card instances" + label);
        var outside = cb.Zones.Draw.Concat(cb.Zones.Discard).Concat(cb.Zones.Exhaust).Concat(cb.Reservations.Select(r => r.Uid)).Concat(cb.Enemies.Select(e => e.Uid)).ToList();
        foreach (string uid in outside)
        {
            var reply = await Query(api, token, runId, version, uid, "after", target, new[] { first });
            Status(reply, 422, "NOT_IN_HAND", "a battle query for an instance outside the hand" + label);
            Same(reply.Body, missing.Body, "an instance outside the hand answers exactly like no card" + label);
        }

        Status(await Query(api, token, runId, version - 1, first), 409, "STALE_VERSION", "a battle query at an earlier server version" + label);
        Status(await Query(api, token, runId, version + 1, first), 409, "STALE_VERSION", "a battle query at a later server version" + label);
        Status(await Query(api, otherToken, runId, version, first), 404, "RUN_NOT_FOUND", "another account's query of this battle" + label);
        Check(await unborn(), "battle queries created no account file or row" + label);

        byte[] after = await stored();
        Same(after, committed, "every battle query of the round left the stored envelope byte-identical" + label);
        Same(Convert.ToHexStringLower(SHA256.HashData(after)), sha, "the stored envelope's full SHA-256 is unchanged" + label);
        var reread = await api.Get(RunsPath + "/" + runId, token);
        Same(reread.Body, view.Body, "the run read after the battle queries is byte-identical to the read before them" + label);
        return core;
    }

    // One 200 reply against the oracle: exactly the allowlisted members; server journal version, node, turn and battle version; the
    // source and request; the computed slots; Core's forecast of exactly this request with its per-enemy HP loss split; Core's pick
    // sheet and its candidates.
    void SameQuery(JsonElement reply, Run core, BattleData content, string runId, int version, string uid, string mode, string? target,
        IReadOnlyList<string> picks, string what)
    {
        var cs = core.State; var b = cs.Battle!;
        Check(reply.EnumerateObject().Select(p => p.Name).SequenceEqual(QueryMembers), what + ": only the allowlisted reply members");
        Check(S(reply, "runId") == runId && I(reply, "version") == version && S(reply, "phase") == cs.Phase, what + ": run and server journal version");
        var context = reply.GetProperty("context");
        Check(S(context, "node") == cs.Position && I(context, "turn") == b.Turn && I(context, "battleVersion") == b.V && S(context, "battlePhase") == b.Phase,
            what + ": node, turn and battle version");
        var effective = core.BattleEffectiveCard(uid);
        var source = reply.GetProperty("source");
        Check(S(source, "uid") == uid && S(source, "id") == b.Cards[uid].Id && S(source, "name") == effective.Name && S(source, "text") == BattleText.Card(effective, content)
            && S(source, "mode") == mode && OptionalText(source, "target") == target && A(source, "picks").Select(p => p.GetString()).SequenceEqual(picks),
            what + ": the requested source, its effective name and BattleText");
        SameSlots(reply.GetProperty("slots"), core, b, what + ": slots");

        var f = core.BattleForecast(uid, mode, target, picks);
        var forecast = reply.GetProperty("forecast");
        Check(forecast.EnumerateObject().Select(p => p.Name).SequenceEqual(ForecastMembers), what + ": only the allowlisted forecast members");
        Check(OptionalText(forecast, "error") == f.Error && S(forecast, "mode") == f.Mode && I(forecast, "cost") == f.Cost && OptionalInt(forecast, "due") == f.Due
            && forecast.GetProperty("hidden").GetBoolean() == f.Hidden && I(forecast, "enemyHpLoss") == f.EnemyHpLoss && I(forecast, "enemyDamage") == f.EnemyDamage
            && I(forecast, "newlyBroken") == f.NewlyBroken && I(forecast, "blockDelta") == f.BlockDelta && I(forecast, "hpDelta") == f.HpDelta
            && forecast.GetProperty("defeat").GetBoolean() == f.Defeat, what + ": Core's forecast of exactly this request");
        // Core's per-enemy split of exactly this request, entry by entry in battle order: only {uid, hpLoss}, each a positive loss of a
        // distinct enemy of this battle, summing to enemyHpLoss, and empty with an error or a hidden outcome (no draw or random result).
        var split = A(forecast, "enemyHpLosses");
        var enemies = new HashSet<string>(b.Enemies.Select(e => e.Uid), StringComparer.Ordinal);
        Check(split.Count == f.EnemyHpLosses.Count && split.Zip(f.EnemyHpLosses).All(p => p.First.ValueKind == JsonValueKind.Object
                && p.First.EnumerateObject().Select(m => m.Name).SequenceEqual(EnemyHpLossMembers) && S(p.First, "uid") == p.Second.Uid && I(p.First, "hpLoss") == p.Second.HpLoss),
            what + ": Core's per-enemy HP loss of exactly this request, in battle order");
        Check(split.All(e => I(e, "hpLoss") > 0 && enemies.Contains(S(e, "uid"))) && split.Select(e => S(e, "uid")).Distinct(StringComparer.Ordinal).Count() == split.Count
            && split.Sum(e => (long)I(e, "hpLoss")) == I(forecast, "enemyHpLoss") && (split.Count == 0 || (f.Error == null && !f.Hidden)),
            what + ": each split entry is a positive loss of a distinct enemy of this battle, the entries sum to enemyHpLoss, none with an error or hidden");

        var c = core.BattleHandChoices(uid, mode, picks);
        var choices = reply.GetProperty("choices");
        var requests = A(choices, "requests");
        Check(OptionalText(choices, "error") == c.Error && requests.Count == c.Requests.Count && requests.Zip(c.Requests).All(p => S(p.First, "label") == p.Second.Label
                && S(p.First, "from") == p.Second.From && I(p.First, "count") == p.Second.Count && p.First.GetProperty("growthOnly").GetBoolean() == p.Second.GrowthOnly
                && S(p.First, "op") == p.Second.Op && OptionalText(p.First, "part") == p.Second.Part && p.First.GetProperty("leading").GetBoolean() == p.Second.Leading)
            && I(choices, "accepted") == c.Accepted && I(choices, "index") == c.Index && I(choices, "left") == c.Left && OptionalText(choices, "blocked") == c.Blocked,
            what + ": Core's pick sheet");
        var candidates = A(choices, "candidates");
        string? zone = c.Index < c.Requests.Count ? c.Requests[c.Index].From : null;
        var open = new HashSet<string>(b.Zones.Hand.Concat(b.Zones.Discard).Concat(b.Reservations.Select(r => r.Uid)), StringComparer.Ordinal);
        Check(candidates.Select(x => S(x, "uid")).SequenceEqual(c.Candidates) && candidates.All(x => open.Contains(S(x, "uid")) && S(x, "id") == b.Cards[S(x, "uid")].Id
                && OptionalText(x, "zone") == zone && zone != null && x.GetProperty("plus").GetBoolean() == b.Cards[S(x, "uid")].Plus && I(x, "growth") == b.Cards[S(x, "uid")].Growth),
            what + ": Core's candidates in order, each a public hand/discard/reservation card with its request's zone");
    }

    // Served slot members against the oracle's Run.BattleReservationSlots and the oracle state: raw base/temp/max and sealedUntil, computed
    // cap/used/sealed/due, and used as the count of reservations due exactly on the column's turn.
    void SameSlots(JsonElement served, Run core, BattleState b, string what)
    {
        var slots = core.BattleReservationSlots();
        foreach (var (name, column, baseSlots, tempSlots) in new (string, BattleSlotColumn, int, int)[]
            { ("next", slots.Next, b.SlotsBase.Next, b.SlotsTemp.Next), ("after", slots.After, b.SlotsBase.After, b.SlotsTemp.After) })
        {
            var c = served.GetProperty(name);
            Check(I(c, "base") == baseSlots && I(c, "temp") == tempSlots && I(c, "max") == column.Max && I(c, "cap") == column.Cap && I(c, "used") == column.Used
                && c.GetProperty("sealed").GetBoolean() == column.Sealed && c.GetProperty("due").GetInt64() == column.Due && column.Due == (long)b.Turn + (name == "next" ? 1 : 2)
                && column.Used == b.Reservations.Count(r => r.Due == column.Due), what + " (" + name + ")");
        }
        Check(I(served, "sealedUntil") == b.SealedUntil, what + " (sealedUntil)");
    }

    static string? OptionalText(JsonElement element, string name) => element.GetProperty(name).ValueKind == JsonValueKind.Null ? null : element.GetProperty(name).GetString();
    static int? OptionalInt(JsonElement element, string name) => element.GetProperty(name).ValueKind == JsonValueKind.Null ? (int?)null : element.GetProperty(name).GetInt32();
}
