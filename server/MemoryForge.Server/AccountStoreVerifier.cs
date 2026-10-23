// ACCOUNT-PROGRESSION-S0-20261021 (docs/plans/prd-account-progression-20261021.md "S0", contract-account-progression-s1-20261021.md): the
// owner-only, offline, read-only store verifier of the LOCAL DEVELOPMENT practice host. `MemoryForge.Server --verify-store` takes the same
// startup configuration as the host (ContentRoot, PracticeFixture, PracticePort, PracticeBackend with PracticeStore or PracticePostgresFile,
// Development only; Program.cs) and then, instead of listening, reads every configured fixture account's committed envelope once through
// the backend's pure IEnvelopePersistence.ReadAsync. It opens no socket, adds no route, authenticates no request and writes nothing: no
// account, row, revision, receipt, lock or temp file is created or changed, and the file store directory must already exist. It is not a
// migration and never repairs, rewrites or re-binds anything. Each stored account is verified in full:
//  1. the host's own Envelopes.Decode first (size, schema, digest, binding, and the recruitment ledger reconciled through Core with the exact
//     bytes of every ledger receipt); its refusal code is reported unchanged;
//  2. a chronological reconstruction from the fresh version-0 memory store: the archived runs in record order, then the current run, each by
//     real Run.Create from its recorded inputs, whose memoryVersion and startMemory must be exactly the reconstructed store at that point, and
//     every journal entry through RunCommands.Execute against that prefix store. (PracticeRunStore.Replay checks only the current run against
//     the final store, where a save takes Core's own receipt path, so neither archived runs nor the store's history are proven there.) The
//     server's full-state digest (GraphJson.Digest, log and events included; not Core's canonical hash) must match after Create and after
//     every entry, a save must advance the store by exactly one version, an archived run must have finished (DONE) or carry an exactly verified server abandon closure, and the reconstructed
//     store must equal the stored one: decks, version, history and memory receipts;
//  3. every checkpoint's receipt: its identity is recomputed by the host's own Contracts.ParseCreate/ParseCommand/ParseCommit over the
//     recorded request (so a journal entry must be exactly a normalized request), and its body must be byte for byte the reply RunReplies
//     (Program.cs) projects at that checkpoint with the memory version of that moment; a save's receipt must be under its own requestId.
//     Every receipt must be accounted for exactly once, by a non-grant recruitment ledger entry or by one run/create/abandon checkpoint (a grant shares the save receipt), and the envelope revision
//     must equal the receipt count (each committed request adds one receipt and one revision).
// A verified or absent account then gets the read-only capacity diagnostic of the S1 tutorial candidates Nia/Harin/Rien: Core's
// MemoryV2.Cost of the deck a launch would start from (the stored deck, else the baseline) against Core's capacity at the frozen temporary
// tutorial build (Res 0, Asc 0) and at the account's actual owned resonance and ledger-derived ascension, plus a Core Run.Create probe
// of the frozen temporary tutorial party for both useMemory values. Nothing is
// projected or substituted: no raised resonance, no replaced baseline or starting memory. A stored deck above the tutorial capacity is a
// HOLD of the real-account S1 launch, reported before any S1 write. Report lines (stdout, key=value) carry only fixture aliases, fixed codes,
// record positions, counts and memory costs: never a seed, RNG, draw tape, digest, credential, receipt, body or exception text.
// Exit 0: every account verified (or absent) and no hold; 4: verified, with a capacity hold; 1: an account failed; 2: refused before reading
// (Program/startup). Run it against a stopped host or a copy; each account is one committed snapshot. This proves internal consistency of
// what is stored, not authentication of the owner's history (there is no MAC).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MemoryForge.Core;

namespace MemoryForge.Server
{
    /// <summary>A refused verification: a fixed code and a fixed location (record and journal positions only), never exception text.</summary>
    sealed class StoreVerifyFailure : Exception
    {
        public string Code { get; }
        public string Where { get; }
        public StoreVerifyFailure(string code, string where) : base(code) { Code = code; Where = where; }
    }

    /// <summary>What a verified envelope holds; every receipt is either a run receipt (one per checkpoint) or a recruitment receipt.</summary>
    sealed class StoreVerification
    {
        public long Revision, MemoryVersion;
        public int Receipts, RunReceipts, RecruitmentReceipts, GrowthReceipts, Archived, Checkpoints;
        public bool Current;
    }

    static class AccountStoreVerifier
    {
        public const string Flag = "--verify-store";
        public const int Pass = 0, Failed = 1, Hold = 4;
        const string ReplayMismatch = "VERIFY_REPLAY_MISMATCH", MemoryMismatch = "VERIFY_MEMORY_MISMATCH", ReceiptMismatch = "VERIFY_RECEIPT_MISMATCH",
            ReceiptOrphan = "VERIFY_RECEIPT_ORPHAN", RevisionMismatch = "VERIFY_REVISION_MISMATCH", RunDuplicate = "VERIFY_RUN_DUPLICATE", Internal = "VERIFY_INTERNAL";
        // The requestId of a reconstructed create or command request: neither identity includes it (Contracts). A save's identity does, and the
        // save's own recorded requestId is used.
        const string CheckpointRequestId = "verify-store-checkpoint";
        static readonly string ProbeRunId = "pr-" + new string('0', 32);
        /// <summary>The frozen temporary tutorial party (contract, Architect R2 A–F): Level 1, Asc 0, Res 0, skill and ultimate 1, each
        /// character's basic weapon at Lv 1 Refine 1. Temporary use only; it is not ownership.</summary>
        static readonly (string CharId, string WeaponId)[] TutorialParty = { ("nia", "w_basic_device"), ("harin", "w_basic_melee"), ("rien", "w_basic_ritual") };

        /// <summary>Any other spelling of the flag: repeated, with a value, in another case or with another prefix.</summary>
        public static bool Resembles(string arg)
        {
            string name = arg.StartsWith("--", StringComparison.Ordinal) ? arg.Substring(2)
                : arg.StartsWith("-", StringComparison.Ordinal) || arg.StartsWith("/", StringComparison.Ordinal) ? arg.Substring(1) : "";
            int value = name.IndexOf('=');
            return (value < 0 ? name : name.Substring(0, value)).Equals(Flag.Substring(2), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The whole --verify-store run: startup (refusal: 2), then each configured account in fixture order, then the summary.</summary>
        public static async Task<int> RunAsync(IHostEnvironment environment, IConfiguration configuration, TextWriter output, TextWriter error)
        {
            PracticeHost host;
            try { host = await PracticeHost.ConfigureAsync(environment, configuration, readOnly: true); }
            catch (StartupRefusal refusal) { error.WriteLine("MemoryForge verify-store refused to start: " + refusal.Message); return 2; }
            await using var owned = host;
            var accounts = host.Fixture.Accounts;
            output.WriteLine("verify-store start mode=read-only backend=" + host.Backend + " accounts=" + accounts.Count);
            int verified = 0, absent = 0, failed = 0, held = 0;
            foreach (var account in accounts)
            {
                string line = "verify-store account id=" + account.Id + " ";
                byte[]? bytes;
                try { bytes = await host.Persistence.ReadAsync(account); }
                catch (ApiError refusal) { failed++; output.WriteLine(line + "status=FAIL code=" + Code(refusal.Code) + " at=read"); continue; }
                catch (Exception) { failed++; output.WriteLine(line + "status=FAIL code=" + Internal + " at=read"); continue; }
                AccountEnvelope? envelope = null;
                if (bytes == null)
                {
                    absent++;
                    output.WriteLine(line + "status=ABSENT");
                }
                else
                {
                    try
                    {
                        var result = Verify(host.Content, account, bytes, out var decoded);
                        envelope = decoded;
                        verified++;
                        output.WriteLine(line + "status=PASS revision=" + result.Revision + " receipts=" + result.Receipts + " runReceipts=" + result.RunReceipts
                            + " recruitmentReceipts=" + result.RecruitmentReceipts + " growthReceipts=" + result.GrowthReceipts + " runs=" + (result.Archived + (result.Current ? 1 : 0)) + " archived=" + result.Archived
                            + " current=" + (result.Current ? 1 : 0) + " checkpoints=" + result.Checkpoints + " memoryVersion=" + result.MemoryVersion);
                    }
                    catch (StoreVerifyFailure failure) { failed++; output.WriteLine(line + "status=FAIL code=" + Code(failure.Code) + " at=" + failure.Where); continue; }
                    catch (Exception) { failed++; output.WriteLine(line + "status=FAIL code=" + Internal + " at=unexpected"); continue; }
                }
                if (!Capacity(host.Content, account, envelope, output)) held++;
            }
            string status = failed > 0 ? "FAIL" : held > 0 ? "HOLD" : "PASS";
            output.WriteLine("verify-store result status=" + status + " accounts=" + accounts.Count + " verified=" + verified + " absent=" + absent + " failed=" + failed + " held=" + held);
            output.Flush();
            return failed > 0 ? Failed : held > 0 ? Hold : Pass;
        }

        /// <summary>One stored envelope, steps 1–3 of the header. Any failure is a StoreVerifyFailure; nothing is written or repaired.</summary>
        public static StoreVerification Verify(PracticeContent content, PracticeAccount account, byte[] bytes, out AccountEnvelope envelope)
        {
            try
            {
                PracticeRunStore.RequireWithinLimit(bytes.Length);
                envelope = Envelopes.Decode(bytes, account, content);
            }
            catch (ApiError refusal) { throw new StoreVerifyFailure(refusal.Code, "decode"); }
            catch (Exception) { throw new StoreVerifyFailure(Internal, "decode"); }
            return new Reconstruction(content, account, envelope).Reconstruct();
        }

        /// <summary>One decoded envelope's chronological reconstruction: the prefix memory store, the receipts accounted for so far and the
        /// checkpoint count. Locations: archive.{i}.create / archive.{i}.journal.{j} / archive.{i}.end, current.create / current.journal.{j},
        /// memory, receipts, revision.</summary>
        sealed class Reconstruction
        {
            readonly PracticeContent content;
            readonly PracticeAccount account;
            readonly AccountEnvelope envelope;
            readonly HashSet<string> ledger;
            readonly HashSet<string> growth;
            readonly Dictionary<long, string> memoryWitnesses = new Dictionary<long, string>();
            readonly HashSet<string> accounted = new HashSet<string>(StringComparer.Ordinal);
            readonly HashSet<string> runIds = new HashSet<string>(StringComparer.Ordinal);
            // The non-ledger receipts by exact body: a create's or command's requestId is recorded nowhere else, so its checkpoint finds it by
            // its reply (unique: each committed request changed the run version or created a new runId).
            readonly Dictionary<string, List<string>> byBody = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Dictionary<string, object?> memory;
            int checkpoints;

            public Reconstruction(PracticeContent content, PracticeAccount account, AccountEnvelope envelope)
            {
                this.content = content; this.account = account; this.envelope = envelope;
                // Decode already proved each ledger entry's receipt byte for byte (RecruitmentRecord.Validate).
                // A progression grant shares its memory-commit receipt; it is accounted below by that actual save checkpoint.
                ledger = new HashSet<string>(envelope.Recruitment?.Ledger.Where(e => e.Kind != LedgerEntry.Grant).Select(e => e.RequestId) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
                // Normal decode already reconstructs the complete typed growth ledger, every Battle checkpoint and exact receipt.
                growth = new HashSet<string>(GrowthCommands.ReceiptIds(envelope), StringComparer.Ordinal);
                Require(!growth.Overlaps(ledger), ReceiptMismatch, "receipts");
                foreach (var pair in envelope.Receipts)
                {
                    Require(Contracts.IsRequestId(pair.Key), ReceiptMismatch, "receipts");
                    if (ledger.Contains(pair.Key) || growth.Contains(pair.Key)) continue;
                    if (!byBody.TryGetValue(pair.Value.Body, out var ids)) byBody.Add(pair.Value.Body, ids = new List<string>());
                    ids.Add(pair.Key);
                }
                memory = AccountEnvelope.Fresh(account, content).Memory;
                memoryWitnesses.Add(0, GraphJson.Digest(memory["memory"]));
            }

            public StoreVerification Reconstruct()
            {
                for (int i = 0; i < envelope.Archive.Count; i++) Record(envelope.Archive[i], "archive." + i, true);
                if (envelope.Run != null) Record(envelope.Run, "current", false);
                Require(Value(MemoryMismatch, "memory", () => GraphJson.Digest(memory) == GraphJson.Digest(envelope.Memory)), MemoryMismatch, "memory");
                if (envelope.Growth != null)
                    for (int i = 0; i < envelope.Growth.Encounters.Count; i++)
                    {
                        var encounter = envelope.Growth.Encounters[i];
                        Require(Value(MemoryMismatch, "encounter." + i + ".memory", () => memoryWitnesses.TryGetValue(encounter.MemoryVersion, out var digest)
                            && digest == GraphJson.Digest(encounter.Memory)), MemoryMismatch, "encounter." + i + ".memory");
                    }
                Require(!growth.Overlaps(accounted), ReceiptMismatch, "receipts");
                Require(envelope.Receipts.Keys.All(id => ledger.Contains(id) || growth.Contains(id) || accounted.Contains(id)), ReceiptOrphan, "receipts");
                Require(envelope.Revision == envelope.Receipts.Count, RevisionMismatch, "revision");
                return new StoreVerification
                {
                    Revision = envelope.Revision, MemoryVersion = AccountEnvelope.MemoryVersion(envelope.Memory), Receipts = envelope.Receipts.Count,
                    RunReceipts = accounted.Count, RecruitmentReceipts = ledger.Count, GrowthReceipts = growth.Count,
                    Archived = envelope.Archive.Count, Current = envelope.Run != null, Checkpoints = checkpoints
                };
            }

            void Record(RunRecord record, string label, bool archived)
            {
                string at = label + ".create";
                Require(runIds.Add(record.RunId), RunDuplicate, at);
                // Created from the store exactly as it then stood (Program Create: the store's version and a copy of its decks).
                Require(Value(MemoryMismatch, at, () => record.MemoryVersion == AccountEnvelope.MemoryVersion(memory) && GraphJson.Digest(record.StartMemory) == GraphJson.Digest(memory["memory"])),
                    MemoryMismatch, at);
                var run = Value(ReplayMismatch, at, () => record.CreateRun(account, content));
                string digest = PracticeRunStore.StateDigest(run);
                Require(digest == record.CreatedSha256, ReplayMismatch, at);
                string body = Value(ReceiptMismatch, at, () => Reply.Json(200, RunReplies.Created(run, record.RunId, record.MemoryVersion, content.Rules)).Body);
                Unchanged(run, digest, at);
                if (record.Launch == null)
                {
                    string identity = Value(ReceiptMismatch, at, () => Contracts.ParseCreate(Request("requestId", CheckpointRequestId, "useMemory", record.UseMemory)).Identity);
                    ByBody(body, identity, at);
                }
                else
                {
                    var launches = envelope.Progression?.Ledger.Where(f => f.Kind == "launch" && f.RunId == record.RunId).ToList();
                    Require(launches?.Count == 1, ReceiptMismatch, at);
                    var fact = launches![0]; var request = fact.Request;
                    Require(request != null && fact.RequestId == request.RequestId && fact.Revision == request.ExpectedRevision
                        && fact.Revision == record.Launch.Revision && Contracts.IsRequestId(fact.RequestId), ReceiptMismatch, at);
                    ByKey(fact.RequestId, body, request!.Identity, at);
                }
                for (int j = 0; j < record.Journal.Count; j++)
                {
                    var entry = record.Journal[j];
                    at = label + ".journal." + j;
                    string kind = entry.Kind;
                    bool save = kind == Contracts.MemoryCommit;
                    int expected = j;
                    // The recorded request through the host's own parser: its normalized arguments must be the journal's, its identity the receipt's.
                    var (requestId, args, requestIdentity) = Value(ReplayMismatch, at, () =>
                    {
                        if (save)
                        {
                            var commit = Contracts.ParseCommit(Request("requestId", Member(entry.Args, "requestId"), "expectedVersion", expected,
                                "expectedMemoryVersion", Member(entry.Args, "expectedMemoryVersion"), "selectedChangeIds", Member(entry.Args, "selectedChangeIds")), record.RunId);
                            return (commit.RequestId, commit.Args, commit.Identity);
                        }
                        var command = Contracts.ParseCommand(Request("requestId", CheckpointRequestId, "expectedVersion", expected, "kind", kind, "args", entry.Args), record.RunId);
                        return (CheckpointRequestId, command.Args, command.Identity);
                    });
                    Require(Value(ReplayMismatch, at, () => GraphJson.Digest(args) == GraphJson.Digest(entry.Args)), ReplayMismatch, at);
                    long before = AccountEnvelope.MemoryVersion(memory);
                    var outcome = Value(ReplayMismatch, at, () => RunCommands.Execute(run, record.RunId, kind, args, memory));
                    if (outcome.NextMemory != null)
                    {
                        // On the prefix store a save is a genuine commit; Core's own receipt path would return the store unchanged.
                        Require(AccountEnvelope.MemoryVersion(outcome.NextMemory) == checked(before + 1), MemoryMismatch, at);
                        memory = outcome.NextMemory;
                        memoryWitnesses.Add(AccountEnvelope.MemoryVersion(memory), GraphJson.Digest(memory["memory"]));
                    }
                    digest = PracticeRunStore.StateDigest(run);
                    Require(digest == entry.StateSha256, ReplayMismatch, at);
                    long memoryVersion = AccountEnvelope.MemoryVersion(memory);
                    body = Value(ReceiptMismatch, at, () => Reply.Json(200, RunReplies.Executed(run, record.RunId, expected + 1, kind, outcome, memoryVersion, content.Rules)).Body);
                    Unchanged(run, digest, at);
                    if (save) ByKey(requestId, body, requestIdentity, at);
                    else ByBody(body, requestIdentity, at);
                }
                at = label + ".end";
                if (record.End != null)
                {
                    var end = record.End;
                    // Abandon is a server closure, not a Core command or save: prefix memory remains exactly as it stood here.
                    Require(archived && run.State.Phase != "DONE" && end.AtVersion == record.Journal.Count
                        && end.MemoryVersion == AccountEnvelope.MemoryVersion(memory) && end.Revision < envelope.Revision
                        && Contracts.IsRequestId(end.RequestId), ReplayMismatch, at);
                    var request = Value(ReceiptMismatch, at, () => Contracts.ParseAbandon(Request("requestId", end.RequestId,
                        "expectedVersion", end.AtVersion), record.RunId));
                    body = Value(ReceiptMismatch, at, () => Reply.Json(200, GraphJson.Object("runId", record.RunId,
                        "version", checked(end.AtVersion + 1), "memoryVersion", AccountEnvelope.MemoryVersion(memory))).Body);
                    Unchanged(run, digest, at);
                    ByKey(end.RequestId, body, request.Identity, at);
                }
                else if (archived) Require(run.State.Phase == "DONE", ReplayMismatch, at);
            }

            void ByBody(string body, string identity, string at)
            {
                string? id = byBody.TryGetValue(body, out var ids) ? ids.FirstOrDefault(i => !accounted.Contains(i) && Matches(envelope.Receipts[i], identity, body)) : null;
                if (id == null) throw new StoreVerifyFailure(ReceiptMismatch, at);
                accounted.Add(id);
                checkpoints++;
            }
            void ByKey(string requestId, string body, string identity, string at)
            {
                Require(!ledger.Contains(requestId) && !accounted.Contains(requestId) && envelope.Receipts.TryGetValue(requestId, out var receipt) && Matches(receipt, identity, body),
                    ReceiptMismatch, at);
                accounted.Add(requestId);
                checkpoints++;
            }
            static bool Matches(StoredReceipt receipt, string identity, string body) => receipt.Status == 200 && receipt.Identity == identity && receipt.Body == body;
            static void Unchanged(Run run, string digest, string at) => Value(Internal, at, () => { PracticeRunStore.RequireUnchanged(run, digest); return true; });
            static object? Member(Dictionary<string, object?> args, string name) => args.TryGetValue(name, out object? value) ? value : null;
            static byte[] Request(params object?[] members) => GraphJson.Bytes(GraphJson.Object(members), false);
        }

        /// <summary>The read-only S1 capacity diagnostic of a verified or absent account (absent: the fresh store). False when the tutorial
        /// launch must be held: a candidate's starting deck costs more than Core's tutorial capacity, or Core's own Create refuses the frozen
        /// temporary party for either useMemory value (Core checks the starting deck's capacity before choosing the base deck).</summary>
        static bool Capacity(PracticeContent content, PracticeAccount account, AccountEnvelope? envelope, TextWriter output)
        {
            var rules = content.Data.Memory;
            var store = envelope?.Memory ?? AccountEnvelope.Fresh(account, content).Memory;
            var decks = store.TryGetValue("memory", out object? saved) ? saved as IDictionary<string, object?> : null;
            var recruitment = envelope?.Recruitment;
            var equipment = envelope == null ? new EquipmentState() : GrowthCommands.BuildAt(content, envelope, long.MaxValue);
            bool hold = false;
            foreach (var (cid, _) in TutorialParty)
            {
                string line;
                try
                {
                    // Run.Create's starting deck: the stored rows if any, else the baseline, costed against the baseline.
                    var baseline = MemoryV2.Baseline(rules, cid).Select(c => (object?)c.ToJsonGraph()).ToList();
                    object? rows = decks != null && decks.TryGetValue(cid, out object? stored) && stored != null ? stored : baseline;
                    long used = MemoryV2.Cost(rules, cid, rows, baseline);
                    int tutorial = MemoryV2.CapacityOf(rules, new MemoryCapacityEntry { CharId = cid, Res = 0, Asc = 0 });
                    hold |= used > tutorial;
                    int copies = recruitment != null && recruitment.OwnedChars.TryGetValue(cid, out int count) ? count : 0;
                    string ownedPart = "copies=0 res=- asc=- ownedCapacity=- owned=NOT_OWNED";
                    if (recruitment != null && copies > 0)
                    {
                        int res = Gacha.Resonance(recruitment.ToState(), cid);
                        int asc = equipment.Asc.GetValueOrDefault(cid);
                        int capacity = MemoryV2.CapacityOf(rules, new MemoryCapacityEntry { CharId = cid, Res = res, Asc = asc });
                        ownedPart = "copies=" + copies + " res=" + res + " asc=" + asc + " ownedCapacity=" + capacity + " owned=" + (used > capacity ? "OVER_CAPACITY" : "OK");
                    }
                    line = "used=" + used + " tutorialCapacity=" + tutorial + " tutorial=" + (used > tutorial ? "OVER_CAPACITY" : "OK") + " " + ownedPart;
                }
                catch (Exception)
                {
                    hold = true;
                    line = "status=FAIL code=" + Internal;
                }
                output.WriteLine("verify-store capacity id=" + account.Id + " char=" + cid + " " + line);
            }
            string withMemory = Probe(content, store, true), withoutMemory = Probe(content, store, false);
            hold |= withMemory != "OK" || withoutMemory != "OK";
            output.WriteLine("verify-store probe id=" + account.Id + " party=" + string.Join(",", TutorialParty.Select(p => p.CharId)) + " useMemoryTrue=" + withMemory
                + " useMemoryFalse=" + withoutMemory);
            return !hold;
        }

        // Core Run.Create of the frozen temporary party on the stored decks and version, in memory only. Its creation-time memory check runs
        // before the map is generated, so S1's later stage, zones and bosses of the tutorial launch cannot change this answer.
        static string Probe(PracticeContent content, Dictionary<string, object?> store, bool useMemory)
        {
            try
            {
                var setup = new RunSetup
                {
                    Seed = 1, Length = "short", MemoryPolicy = "v2", RunId = ProbeRunId, MemoryVersion = AccountEnvelope.MemoryVersion(store), Memory = GraphJson.Copy(store["memory"]),
                    Difficulty = new RunDifficulty { Hp = 100, Damage = 100 }, UseMemory = useMemory ? (bool?)null : false
                };
                foreach (var (cid, weapon) in TutorialParty)
                    setup.Party.Add(new RunPartyEntry { CharId = cid, Level = 1, Asc = 0, Res = 0, SkillLv = 1, UltLv = 1, WeaponId = weapon, WeaponLevel = 1, Refine = 1 });
                Run.Create(content.Data, setup);
                return "OK";
            }
            catch (RuleException error) { return Code(error.Code); }
            catch (MemoryBoundaryException) { return "COMMAND_REJECTED"; }
            catch (Exception) { return Internal; }
        }

        static T Value<T>(string code, string at, Func<T> step)
        {
            try { return step(); }
            catch (StoreVerifyFailure) { throw; }
            catch (Exception) { throw new StoreVerifyFailure(code, at); }
        }
        static void Require(bool condition, string code, string at)
        {
            if (!condition) throw new StoreVerifyFailure(code, at);
        }
        // Codes are fixed uppercase constants; anything else is never echoed.
        static string Code(string code) => code.Length >= 2 && code.Length <= 48 && code.All(c => c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_') ? code : "REJECTED";
    }
}
