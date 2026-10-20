// P1-PRACTICE-TRANSPORT §14.7/§17.2/§17.4: PROTOCOL FIXTURE checks of PracticeServerSession and PracticePublicSnapshot. BCL + NUnit only
// (no UnityEngine, TestTools, reflection or source hook), so the same assertions run on .NET and in Unity's Mono. The "server" is a tiny
// test-owned loopback HTTP/1.1 listener (TcpListener on 127.0.0.1, ephemeral port, Connection: close) that answers with fixed PUBLIC JSON
// written here from the documented protocol (PublicView/Program shapes) and with the server's fixed error bodies; it can hold a reply on a
// test-released gate, drop the connection after reading the request, truncate a body or omit its length. Records live in a fresh temp
// folder. These are NOT Core/server acceptance: no real Run, receipt store, restart or fault proxy is involved (Root runs the actual
// unchanged host separately). Every wait is bounded; held replies are released in finally.
// The memory preview checks (P1-SERVER-MEMORY-PREVIEW §8.12/§14.7) answer from fixture decks, costs and verdicts: the client must pass
// them on and judge nothing, so they are protocol data, not Core's memory rules. The class is partial for the save screen's view checks.
// The node-service, event and archive view checks at the end (P1-SERVER-SERVICES-UI, P1-SERVER-EVENT-ARCHIVE-UI) drive the real
// MemoryForgeServerPracticeClient over the same fixture and need UnityEngine, so they compile only inside Unity (#if UNITY_5_3_OR_NEWER);
// the .NET run of this file keeps the BCL-only checks above.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Codes = MemoryForge.Client.PracticeSessionCodes;
using MutationStatus = MemoryForge.Client.PracticeMutationStatus;
#if UNITY_5_3_OR_NEWER
using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using Purpose = MemoryForge.Client.MemoryForgeServerPracticeClient.DeckPurpose;
#endif

namespace MemoryForge.Client.Tests
{
    public partial class PracticeServerSessionPlayModeTests
    {
        const string Account = "dev-local", OtherAccount = "dev-other", AccountRoute = "/dev/practice/account";
        const string Credential = "fixture-credential-0001", RotatedCredential = "fixture-credential-0002";
        const string RunA = "pr-0123456789abcdef0123456789abcdef", RunB = "pr-fedcba9876543210fedcba9876543210";
        static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        string folder = null!;
        FixtureHost host = null!;
        World world = null!;
        Func<string> source = null!;
        Func<Seen, Reply> post = null!;
        readonly List<PracticeServerSession> sessions = new List<PracticeServerSession>();

        [SetUp]
        public void StartFixture()
        {
            folder = Path.Combine(Path.GetTempPath(), "mf-session-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            world = new World();
            source = () => Credential;
            post = request => Error(500, "INTERNAL_ERROR");
            host = new FixtureHost(Serve);
        }

        [TearDown]
        public void StopFixture()
        {
#if UNITY_5_3_OR_NEWER
            // The view first: disabling it disposes its session (an unresolved request stays in the outbox) before the fixture stops.
            if (viewObject != null) { viewObject.SetActive(false); UnityEngine.Object.Destroy(viewObject); viewObject = null; }
#endif
            foreach (var session in sessions) session.Dispose();
            sessions.Clear();
            host?.Dispose();
            if (folder != null && Directory.Exists(folder)) Directory.Delete(folder, true);
            Assert.That(host?.Fault, Is.Null, "a fixture handler failed");
        }

        string RecordPath => Path.Combine(folder, "outbox.json");
        PracticeCommandOutbox Outbox() => new PracticeCommandOutbox(RecordPath, host.Endpoint, Account);
        PracticeServerSession Session(TimeSpan? timeout = null)
        {
            var session = new PracticeServerSession(host.Endpoint, Account, () => source(), Outbox(), timeout ?? TimeSpan.FromSeconds(10));
            sessions.Add(session);
            return session;
        }
        PracticeSnapshot Refreshed(PracticeServerSession session)
        {
            var result = Run(() => session.RefreshAsync());
            Assert.That(result.Code, Is.EqualTo(Codes.Ok));
            return result.Snapshot!;
        }

        // ---- owner, credential and preflight ----

        [Test]
        public void RefreshReadsTheOwnerFirstThenTheCurrentRunAsDetachedCompletePublicJson()
        {
            var session = Session();
            var snapshot = Refreshed(session);
            var seen = host.Seen;
            Assert.That(seen.Select(s => s.Method + " " + s.Path), Is.EqualTo(new[] { "GET " + AccountRoute, "GET /dev/practice/runs/" + RunA }));
            Assert.That(seen.All(s => s.Authorization == "Bearer " + Credential), Is.True);
            var context = snapshot.Context;
            Assert.That(context.Owner, Is.EqualTo(Account));
            Assert.That(context.RunId, Is.EqualTo(RunA));
            // The server journal version and battle.v are distinct members with distinct values.
            Assert.That(context.ServerVersion, Is.EqualTo(3));
            Assert.That(context.BattleVersion, Is.EqualTo(7));
            Assert.That(context.Turn, Is.EqualTo(2));
            Assert.That(context.Node, Is.EqualTo("z1-f2-n0"));
            Assert.That(context.MemoryVersion, Is.EqualTo(2));
            Assert.That(snapshot.Run!.RunMemoryVersion, Is.EqualTo(1));
            Assert.That(snapshot.Run.AccountMemoryVersion, Is.EqualTo(2));
            // The complete public reply is kept, unknown public members and public lookalike names included.
            Assert.That(JToken.DeepEquals(snapshot.Run.ToJObject(), RunJson(world, 3)), Is.True);
            Assert.That(JToken.DeepEquals(snapshot.Account.ToJObject(), AccountJson(world)), Is.True);
            var state = snapshot.Run.StateJObject();
            Assert.That(state["zones"], Is.InstanceOf<JArray>());
            Assert.That(state["futurePublic"]!["note"]!.Value<string>(), Is.EqualTo("kept"));
            Assert.That(state["battle"]!["hand"]![1]!["token"]!.Value<bool>(), Is.True);
            // Copies are detached.
            state["hp"] = 1;
            state["battle"]!["piles"]!["draw"] = 99;
            Assert.That(snapshot.Run.StateJObject()["hp"]!.Value<int>(), Is.EqualTo(40));
            Assert.That(snapshot.Run.StateJObject()["battle"]!["piles"]!["draw"]!.Value<int>(), Is.EqualTo(12));
            Assert.That(session.State.CanMutate, Is.True);
        }

        [Test]
        public void PreflightFailuresStageNothingAndPostNothing()
        {
            var session = Session();
            var context = Refreshed(session).Context;
            int before = host.Seen.Length;

            source = () => throw new InvalidOperationException("fixture callback failure");
            Assert.That(Run(() => session.CommandAsync(context, "end", null)).Code, Is.EqualTo(Codes.CredentialUnavailable));
            Assert.That(host.Seen.Length, Is.EqualTo(before), "no request without a usable credential");
            source = () => "short";
            Assert.That(Run(() => session.CommandAsync(context, "end", null)).Code, Is.EqualTo(Codes.CredentialUnavailable));
            source = () => Credential;

            world.AccountFailure = Error(401, "UNAUTHORIZED");
            var unauthorized = Run(() => session.CommandAsync(context, "end", null));
            Assert.That(unauthorized.Status, Is.EqualTo(MutationStatus.NotSent));
            Assert.That(unauthorized.Code, Is.EqualTo("UNAUTHORIZED"));
            Assert.That(unauthorized.HttpStatus, Is.EqualTo(401));
            world.AccountFailure = null;

            world.AccountId = OtherAccount;
            Assert.That(Run(() => session.CommandAsync(context, "end", null)).Code, Is.EqualTo(PracticeProtocolException.WrongAccount));
            world.AccountId = Account;

            // The server moved past the acting context: refresh required, nothing staged, and the stale screen stays blocked.
            world.Version = 4;
            var moved = Run(() => session.CommandAsync(context, "end", null));
            Assert.That(moved.Status, Is.EqualTo(MutationStatus.NotSent));
            Assert.That(moved.Code, Is.EqualTo(Codes.RefreshRequired));
            Assert.That(session.State.NeedsRefresh, Is.True);
            Assert.That(session.State.Snapshot!.Context, Is.EqualTo(context));
            int handshakes = host.Seen.Length;
            Assert.That(Run(() => session.CommandAsync(context, "end", null)).Code, Is.EqualTo(Codes.RefreshRequired));
            Assert.That(host.Seen.Length, Is.EqualTo(handshakes), "a stale screen does not even reach the handshake");

            Assert.That(host.Posts, Is.Empty);
            Assert.That(File.Exists(RecordPath), Is.False);
            Assert.That(Outbox().Load(), Is.Null);

            // A refresh that names another owner applies nothing and keeps the last good snapshot, marked stale.
            world.AccountId = OtherAccount;
            var wrong = Run(() => session.RefreshAsync());
            Assert.That(wrong.Code, Is.EqualTo(PracticeProtocolException.WrongAccount));
            Assert.That(wrong.Snapshot, Is.Null);
            Assert.That(session.State.Snapshot!.Context, Is.EqualTo(context));
            Assert.That(session.State.NeedsRefresh, Is.True);
        }

        [Test]
        public void OneOperationKeepsTheCredentialItCapturedWhileTheCallbackRotates()
        {
            var session = Session();
            var context = Refreshed(session).Context;
            int calls = 0;
            source = () => Interlocked.Increment(ref calls) == 1 ? Credential : RotatedCredential;
            post = request => { world.Version = 4; return Reply.Json(200, RunJson(world, 4, Events())); };
            int start = host.Seen.Length;
            var result = Run(() => session.CommandAsync(context, "end", null));
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed));
            var operation = host.Seen.Skip(start).ToArray();
            // Handshake, POST and the re-read (account + run) all carry the one captured credential.
            Assert.That(operation.Select(s => s.Method + " " + s.Path), Is.EqualTo(new[]
                { "GET " + AccountRoute, "POST /dev/practice/runs/" + RunA + "/commands", "GET " + AccountRoute, "GET /dev/practice/runs/" + RunA }));
            Assert.That(operation.All(s => s.Authorization == "Bearer " + Credential), Is.True);
            Assert.That(calls, Is.EqualTo(1));
            start = host.Seen.Length;
            Refreshed(session);
            Assert.That(host.Seen.Skip(start).All(s => s.Authorization == "Bearer " + RotatedCredential), Is.True);
        }

        // ---- staging, exact bytes, busy and retry ----

        [Test]
        public void StagesBeforeTheHeldPostSendsTheCopiedOrderedArgsAndRefusesASecondCall()
        {
            var session = Session();
            var context = Refreshed(session).Context;
            var posted = new TaskCompletionSource<Seen>();
            var release = new TaskCompletionSource<bool>();
            post = request =>
            {
                world.Version = 4;
                posted.TrySetResult(request);
                return new Reply(200, Utf8(RunJson(world, 4, Events()))) { Hold = release.Task };
            };
            try
            {
                var args = new JObject { ["uid"] = "h2", ["mode"] = "next", ["picks"] = new JArray("c3", "c1", "c2") };
                var running = session.CommandAsync(context, "play", args);
                // The caller's object changes after the call; the request was copied once, in order.
                args["uid"] = "changed";
                ((JArray)args["picks"]!)[0] = "mutated";
                ((JArray)args["picks"]!).Add("c9");
                Assert.That(posted.Task.Wait(Bound), Is.True);
                var sent = posted.Task.Result;
                var staged = Outbox().Load();
                Assert.That(staged, Is.Not.Null, "the record exists while its POST is still unanswered");
                Assert.That(staged!.Body, Is.EqualTo(sent.Text));
                Assert.That(sent.Body, Is.EqualTo(staged.BodyUtf8()));
                Assert.That(sent.ContentType, Does.StartWith("application/json"));
                var body = JObject.Parse(sent.Text);
                Assert.That(body.Properties().Select(p => p.Name), Is.EqualTo(new[] { "requestId", "expectedVersion", "kind", "args" }));
                Assert.That(Guid.TryParseExact(body["requestId"]!.Value<string>()!, "D", out _), Is.True);
                Assert.That(body["requestId"]!.Value<string>(), Is.EqualTo(staged.RequestId));
                Assert.That(body["expectedVersion"]!.Value<int>(), Is.EqualTo(3));
                Assert.That(JToken.DeepEquals(body["args"], new JObject { ["uid"] = "h2", ["mode"] = "next", ["picks"] = new JArray("c3", "c1", "c2") }), Is.True);
                Assert.That(session.State.InFlight, Is.True);
                Assert.That(session.State.Pending!.Body, Is.EqualTo(sent.Text));
                Assert.That(session.State.CanMutate, Is.False);
                // One operation at a time: a second call is refused at once, nothing queued.
                Assert.That(Run(() => session.CommandAsync(context, "end", null)).Code, Is.EqualTo(Codes.Busy));
                Assert.That(Run(() => session.RefreshAsync()).Code, Is.EqualTo(Codes.Busy));
                release.TrySetResult(true);
                var result = Wait(running);
                Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed));
                Assert.That(result.Receipt!.ServerVersion, Is.EqualTo(4));
                Assert.That(result.Snapshot!.Context.ServerVersion, Is.EqualTo(4));
                Assert.That(result.PresentableEvents()!.Count, Is.EqualTo(1), "the shown state was exactly the one before this command");
                Assert.That(Outbox().Load(), Is.Null);
                Assert.That(session.State.Pending, Is.Null);
                Assert.That(session.State.CanMutate, Is.True);
                Assert.That(host.Posts.Length, Is.EqualTo(1));
            }
            finally { release.TrySetResult(true); }
        }

        [Test]
        public void AnUnreadCommittedReplyIsRetriedByteForByteByANewSessionWithoutReplayOrRollback()
        {
            var first = Session();
            var context = Refreshed(first).Context;
            byte[] storedReceipt = Array.Empty<byte>();
            post = request =>
            {
                // The server commits and its reply never arrives.
                world.Version = 4;
                storedReceipt = Utf8(RunJson(world, 4, Events()));
                return new Reply(200, storedReceipt) { Drop = true };
            };
            var lost = Run(() => first.CommandAsync(context, "end", null));
            Assert.That(lost.Status, Is.EqualTo(MutationStatus.Pending));
            Assert.That(lost.Code, Is.EqualTo(Codes.NetworkFailed));
            var record = Outbox().Load();
            Assert.That(record!.Body, Is.EqualTo(host.Posts[0].Text));
            var blocked = Run(() => first.CommandAsync(context, "end", null));
            Assert.That(blocked.Code, Is.EqualTo(Codes.PendingUnresolved));
            Assert.That(blocked.Request!.Body, Is.EqualTo(record.Body));
            Assert.That(host.Posts.Length, Is.EqualTo(1));
            first.Dispose();
            Assert.That(Outbox().Load()!.Body, Is.EqualTo(record.Body), "Dispose never clears");

            // Another device moved the run further; a new session reads that, then retries exactly the stored request.
            world.Version = 6;
            var second = Session();
            Assert.That(Refreshed(second).Context.ServerVersion, Is.EqualTo(6));
            Assert.That(second.State.Pending!.Body, Is.EqualTo(record.Body));
            post = request => new Reply(200, storedReceipt);
            var retried = Run(() => second.RetryPendingAsync());
            Assert.That(host.Posts.Length, Is.EqualTo(2));
            Assert.That(host.Posts[1].Body, Is.EqualTo(host.Posts[0].Body));
            Assert.That(host.Posts[1].Path, Is.EqualTo(host.Posts[0].Path));
            Assert.That(retried.Status, Is.EqualTo(MutationStatus.Committed));
            Assert.That(retried.Receipt!.ServerVersion, Is.EqualTo(4));
            Assert.That(retried.Snapshot!.Context.ServerVersion, Is.EqualTo(6), "an old receipt never rolls the run back");
            Assert.That(retried.PresentableEvents(), Is.Null, "already represented events are not replayed");
            Assert.That(Outbox().Load(), Is.Null);
            Assert.That(Run(() => second.RetryPendingAsync()).Code, Is.EqualTo(Codes.NothingPending));
        }

        [Test]
        public void AFailedReReadAfterACommitKeepsTheLastGoodSnapshotButBlocksNewMutations()
        {
            var session = Session();
            var context = Refreshed(session).Context;
            post = request =>
            {
                world.Version = 4;
                world.AccountFailure = Error(503, "ACCOUNT_BUSY");
                return Reply.Json(200, RunJson(world, 4, Events()));
            };
            var result = Run(() => session.CommandAsync(context, "end", null));
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Committed));
            Assert.That(result.RefreshCode, Is.EqualTo("ACCOUNT_BUSY"));
            Assert.That(result.Snapshot, Is.Null);
            Assert.That(result.PresentableEvents(), Is.Null);
            Assert.That(Outbox().Load(), Is.Null, "the validated receipt confirmed the request");
            Assert.That(session.State.NeedsRefresh, Is.True);
            Assert.That(session.State.Snapshot!.Context.ServerVersion, Is.EqualTo(3));
            int seen = host.Seen.Length;
            Assert.That(Run(() => session.CommandAsync(context, "end", null)).Code, Is.EqualTo(Codes.RefreshRequired));
            Assert.That(host.Seen.Length, Is.EqualTo(seen));
            world.AccountFailure = null;
            Assert.That(Refreshed(session).Context.ServerVersion, Is.EqualTo(4));
            Assert.That(session.State.NeedsRefresh, Is.False);
        }

        // ---- uncertain ends keep the exact request ----

        [TestCase("drop", Codes.NetworkFailed)]
        [TestCase("truncate", Codes.ResponseTruncated)]
        [TestCase("no-length", PracticeProtocolException.Invalid)]
        [TestCase("text-plain", PracticeProtocolException.Invalid)]
        [TestCase("503-commit-unknown", "STORE_COMMIT_UNKNOWN")]
        [TestCase("503-busy", "ACCOUNT_BUSY")]
        [TestCase("500-corrupt", "STORE_CORRUPT")]
        [TestCase("401", "UNAUTHORIZED")]
        [TestCase("409-idempotency", "IDEMPOTENCY_CONFLICT")]
        [TestCase("409-binding", "STORE_IMPLEMENTATION_MISMATCH")]
        [TestCase("400-unsafe-code", PracticeProtocolException.Invalid)]
        [TestCase("200-wrong-version", PracticeProtocolException.Mismatch)]
        [TestCase("200-other-run", PracticeProtocolException.Mismatch)]
        [TestCase("200-hidden-battle", PracticeProtocolException.PrivateField)]
        public void UncertainEndsKeepTheExactRequestPending(string mode, string expectedCode)
        {
            var session = Session();
            var context = Refreshed(session).Context;
            post = request => Failure(mode);
            var result = Run(() => session.CommandAsync(context, "end", null));
            Assert.That(result.Status, Is.EqualTo(MutationStatus.Pending));
            Assert.That(result.Code, Is.EqualTo(expectedCode));
            var record = Outbox().Load();
            Assert.That(record, Is.Not.Null);
            Assert.That(record!.Body, Is.EqualTo(host.Posts.Single().Text));
            Assert.That(result.Request!.Body, Is.EqualTo(record.Body));
            Assert.That(session.State.Pending!.Body, Is.EqualTo(record.Body));
            Assert.That(session.State.CanMutate, Is.False);
            Assert.That(Run(() => session.CommandAsync(context, "end", null)).Code, Is.EqualTo(Codes.PendingUnresolved));
            // A current-state refresh is never an acknowledgement.
            Refreshed(session);
            Assert.That(Outbox().Load()!.Body, Is.EqualTo(record.Body));
            Assert.That(host.Posts.Length, Is.EqualTo(1));
        }

        [Test]
        public void CancellationTimeoutAndDisposeDuringAHeldPostKeepItPending()
        {
            var session = Session();
            var context = Refreshed(session).Context;
            var posted = new SemaphoreSlim(0);
            var release = new TaskCompletionSource<bool>();
            post = request => { posted.Release(); return new Reply(200, Utf8(RunJson(world, 4, Events()))) { Hold = release.Task }; };
            try
            {
                using (var cancel = new CancellationTokenSource())
                {
                    var running = session.CommandAsync(context, "end", null, cancel.Token);
                    Assert.That(posted.Wait(Bound), Is.True);
                    cancel.Cancel();
                    var canceled = Wait(running);
                    Assert.That(canceled.Status, Is.EqualTo(MutationStatus.Pending));
                    Assert.That(canceled.Code, Is.EqualTo(Codes.Canceled));
                }
                string body = Outbox().Load()!.Body;

                var quick = Session(TimeSpan.FromSeconds(1));
                var timedOut = Run(() => quick.RetryPendingAsync());
                Assert.That(posted.Wait(Bound), Is.True);
                Assert.That(timedOut.Code, Is.EqualTo(Codes.Timeout));
                Assert.That(Outbox().Load()!.Body, Is.EqualTo(body));

                var disposing = session.RetryPendingAsync();
                Assert.That(posted.Wait(Bound), Is.True);
                session.Dispose();
                var disposed = Wait(disposing);
                Assert.That(disposed.Status, Is.EqualTo(MutationStatus.Pending));
                Assert.That(disposed.Code, Is.EqualTo(Codes.Canceled));
                Assert.That(Outbox().Load()!.Body, Is.EqualTo(body));
                Assert.That(Run(() => session.RefreshAsync()).Code, Is.EqualTo(Codes.Disposed));
                Assert.That(host.Posts.Length, Is.EqualTo(3));
                Assert.That(host.Posts.All(p => p.Text == body), Is.True);
            }
            finally { release.TrySetResult(true); }
        }

        // ---- the narrow refusal whitelist ----

        // route: commands | commit | create. run: the handshake's current run (same/other/none); offset: its journal version minus the
        // request's expectedVersion (5).
        [TestCase("commands", 409, "STALE_VERSION", "same", 1, true)]
        [TestCase("commands", 409, "STALE_VERSION", "same", 0, true)]
        [TestCase("commands", 422, "SLOT_FULL", "same", 0, true)]
        [TestCase("commit", 422, "COMMAND_REJECTED", "same", 0, true)]
        [TestCase("commit", 409, "STALE_VERSION", "same", 2, true)]
        [TestCase("commands", 409, "STALE_VERSION", "same", -1, false)]
        [TestCase("commands", 422, "SLOT_FULL", "same", -2, false)]
        [TestCase("commands", 422, "SLOT_FULL", "other", 0, false)]
        [TestCase("commands", 409, "STALE_VERSION", "none", 0, false)]
        [TestCase("commands", 400, "INVALID_ARGS", "same", 0, false)]
        [TestCase("commands", 404, "RUN_NOT_FOUND", "same", 0, false)]
        [TestCase("commands", 507, "STORE_TOO_LARGE", "same", 0, false)]
        [TestCase("commands", 409, "IDEMPOTENCY_CONFLICT", "same", 0, false)]
        [TestCase("commands", 409, "STORE_FIXTURE_MISMATCH", "same", 0, false)]
        [TestCase("commands", 403, "HOST_NOT_ALLOWED", "same", 0, false)]
        [TestCase("commands", 418, "TEAPOT", "same", 0, false)]
        [TestCase("create", 400, "INVALID_REQUEST", "same", 0, false)]
        [TestCase("create", 409, "RUN_ACTIVE", "same", 0, false)]
        [TestCase("create", 422, "COMMAND_REJECTED", "same", 0, false)]
        [TestCase("create", 507, "STORE_TOO_LARGE", "none", 0, false)]
        public void OnlyProvenVersionedRefusalsClearTheRequest(string route, int status, string code, string run, int offset, bool clears)
        {
            const int expected = 5;
            world.RunId = run == "none" ? null : run == "other" ? RunB : RunA;
            world.Version = expected + offset;
            var staged = Outbox().Stage(Route(route), Body(route, "refusal-0001", expected));
            post = request => Error(status, code);
            var result = Run(() => Session().RetryPendingAsync());
            Assert.That(host.Posts.Single().Text, Is.EqualTo(staged.Body));
            Assert.That(result.Code, Is.EqualTo(code));
            Assert.That(result.HttpStatus, Is.EqualTo(status));
            if (clears)
            {
                Assert.That(result.Status, Is.EqualTo(MutationStatus.NotCommitted));
                Assert.That(Outbox().Load(), Is.Null);
                Assert.That(result.Snapshot, Is.Not.Null, "the account and run are re-read after the resolution");
            }
            else
            {
                Assert.That(result.Status, Is.EqualTo(MutationStatus.Pending));
                Assert.That(Outbox().Load()!.Body, Is.EqualTo(staged.Body));
            }
        }

        [Test]
        public void AFutureVersionRefusalStaysPendingEvenAfterALaterReadCatchesUp()
        {
            var session = Session();
            world.Version = 4;
            var staged = Outbox().Stage(PracticeCommandOutbox.CommandRoute(RunA), Body("commands", "future-0001", 9));
            post = request => { world.Version = 10; return Error(409, "STALE_VERSION"); };
            var first = Run(() => session.RetryPendingAsync());
            Assert.That(first.Status, Is.EqualTo(MutationStatus.Pending));
            // The later read shows version 10 >= 9, but it is not the proof of the earlier refusal; nothing is resolved by it.
            Assert.That(Refreshed(session).Context.ServerVersion, Is.EqualTo(10));
            Assert.That(Outbox().Load()!.Body, Is.EqualTo(staged.Body));
            // Only a new attempt whose own preceding handshake shows version >= 9 can be cleared by its own refusal.
            var second = Run(() => session.RetryPendingAsync());
            Assert.That(second.Status, Is.EqualTo(MutationStatus.NotCommitted));
            Assert.That(Outbox().Load(), Is.Null);
            Assert.That(host.Posts.Length, Is.EqualTo(2));
        }

        [Test]
        public void ADoneRunSaveSendsTheContextMemoryVersionAndACoreRefusalClearsIt()
        {
            world.Phase = "DONE";
            world.InBattle = false;
            var session = Session();
            var context = Refreshed(session).Context;
            Assert.That(context.BattleVersion, Is.Null);
            post = request => Error(422, "MEMORY_VERSION_CONFLICT");
            var result = Run(() => session.CommitMemoryAsync(context, new[] { "chg-b", "chg-a" }));
            Assert.That(result.Status, Is.EqualTo(MutationStatus.NotCommitted));
            var sent = host.Posts.Single();
            Assert.That(sent.Path, Is.EqualTo(PracticeCommandOutbox.MemoryCommitRoute(RunA)));
            var body = JObject.Parse(sent.Text);
            Assert.That(body.Properties().Select(p => p.Name), Is.EqualTo(new[] { "requestId", "expectedVersion", "expectedMemoryVersion", "selectedChangeIds" }));
            Assert.That(body["expectedVersion"]!.Value<int>(), Is.EqualTo(3));
            Assert.That(body["expectedMemoryVersion"]!.Value<int>(), Is.EqualTo(2));
            Assert.That(body["selectedChangeIds"]!.Values<string>(), Is.EqualTo(new[] { "chg-b", "chg-a" }));
            Assert.That(Outbox().Load(), Is.Null);
        }

        [Test]
        public void ConfirmNotCommittedClearsOnlyTheExactCurrentRequest()
        {
            string route = PracticeCommandOutbox.CommandRoute(RunA);
            var outbox = Outbox();
            var current = outbox.Stage(route, Body("commands", "confirm-not-0001", 5));
            var other = new PracticeCommandOutbox(Path.Combine(folder, "other.json"), host.Endpoint, Account).Stage(route, Body("commands", "confirm-not-0002", 5));
            var refused = Assert.Throws<PracticeOutboxException>(() => outbox.ConfirmNotCommitted(other));
            Assert.That(refused.Code, Is.EqualTo(PracticeOutboxException.NotCurrent));
            Assert.That(outbox.Load()!.Body, Is.EqualTo(current.Body));
            outbox.ConfirmNotCommitted(current);
            Assert.That(outbox.Load(), Is.Null);
            Assert.That(Assert.Throws<PracticeOutboxException>(() => outbox.ConfirmNotCommitted(current)).Code, Is.EqualTo(PracticeOutboxException.NotCurrent));
        }

        // ---- snapshots: coherence, rollback and run adoption ----

        [Test]
        public void SnapshotsAreCoherentMonotonicAndAdoptOnlyANewerRun()
        {
            world.RunId = null;
            var session = Session();
            var empty = Refreshed(session);
            Assert.That(empty.Run, Is.Null);
            Assert.That(empty.Context.RunId, Is.Null);
            Assert.That(empty.Context.ServerVersion, Is.Null);
            Assert.That(host.Seen.Select(s => s.Path), Is.EqualTo(new[] { AccountRoute }), "no run read without a current run");

            world.RunId = RunA;
            Assert.That(Refreshed(session).Context.ServerVersion, Is.EqualTo(3));

            // The run read disagrees with the account summary: nothing applied, last good kept but stale.
            world.RunVersionOverride = 4;
            Assert.That(Run(() => session.RefreshAsync()).Code, Is.EqualTo(Codes.RefreshRaced));
            Assert.That(session.State.Snapshot!.Context.ServerVersion, Is.EqualTo(3));
            Assert.That(session.State.NeedsRefresh, Is.True);
            world.RunVersionOverride = null;

            world.Version = 2;
            Assert.That(Run(() => session.RefreshAsync()).Code, Is.EqualTo(Codes.RollbackRefused));
            world.Version = 3;
            world.MemoryVersion = 1;
            Assert.That(Run(() => session.RefreshAsync()).Code, Is.EqualTo(Codes.RollbackRefused));
            world.MemoryVersion = 2;
            world.RunId = RunB;
            Assert.That(Run(() => session.RefreshAsync()).Code, Is.EqualTo(Codes.RollbackRefused), "another run without archiving the shown one");
            Assert.That(session.State.Snapshot!.Context.RunId, Is.EqualTo(RunA));

            world.Finished = 1;
            world.Version = 0;
            var adopted = Refreshed(session);
            Assert.That(adopted.Context.RunId, Is.EqualTo(RunB));
            Assert.That(adopted.Context.ServerVersion, Is.EqualTo(0));
            Assert.That(session.State.NeedsRefresh, Is.False);
        }

        // ---- the public DTO boundary ----

        [TestCase("battle-zones", PracticeProtocolException.PrivateField)]
        [TestCase("battle-cards", PracticeProtocolException.PrivateField)]
        [TestCase("pile-list", PracticeProtocolException.Invalid)]
        [TestCase("no-piles", PracticeProtocolException.Invalid)]
        [TestCase("enemy-pattern", PracticeProtocolException.PrivateField)]
        [TestCase("nested-RNG", PracticeProtocolException.PrivateField)]
        [TestCase("state-seed", PracticeProtocolException.PrivateField)]
        [TestCase("no-position", PracticeProtocolException.Invalid)]
        [TestCase("battle-v-text", PracticeProtocolException.Invalid)]
        [TestCase("state-version", PracticeProtocolException.Mismatch)]
        [TestCase("other-run", PracticeProtocolException.Mismatch)]
        public void HiddenBattleContainersPrivateNamesAndMissingContextAreRefused(string change, string expectedCode)
        {
            var json = RunJson(world, 3);
            var state = (JObject)json["state"]!;
            var battle = (JObject)state["battle"]!;
            string? expectedRun = RunA;
            switch (change)
            {
                case "battle-zones": battle["zones"] = new JObject { ["draw"] = new JArray("hidden-uid") }; break;
                case "battle-cards": battle["cards"] = new JObject { ["hidden-uid"] = new JObject { ["id"] = "strike" } }; break;
                case "pile-list": battle["piles"]!["draw"] = new JArray("hidden-uid"); break;
                case "no-piles": battle.Remove("piles"); break;
                case "enemy-pattern": battle["enemies"]![0]!["pattern"] = new JArray(); break;
                case "nested-RNG": battle["actors"]![0]!["RNG"] = 1; break;
                case "state-seed": state["seed"] = 12345; break;
                case "no-position": state.Remove("position"); break;
                case "battle-v-text": battle["v"] = "7"; break;
                case "state-version": state["version"] = 4; break;
                case "other-run": expectedRun = RunB; break;
            }
            var refused = Assert.Throws<PracticeProtocolException>(() => PracticeRunView.Parse(Utf8(json), expectedRun));
            Assert.That(refused.Code, Is.EqualTo(expectedCode));
        }

        [TestCase("duplicate")]
        [TestCase("trailing")]
        [TestCase("comment")]
        [TestCase("bom")]
        [TestCase("nan")]
        [TestCase("lone-surrogate")]
        [TestCase("array")]
        [TestCase("empty")]
        public void NonStrictReplyTextIsRefused(string change)
        {
            string text = RunJson(world, 3).ToString(Formatting.None);
            byte[] bytes;
            switch (change)
            {
                case "duplicate": bytes = Utf8Text("{\"runId\":\"" + RunA + "\"," + text.Substring(1)); break;
                case "trailing": bytes = Utf8Text(text + " {}"); break;
                case "comment": bytes = Utf8Text("/*c*/" + text); break;
                case "bom": bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8Text(text)).ToArray(); break;
                case "nan": bytes = Utf8Text(text.Replace("\"hp\":40", "\"hp\":NaN")); break;
                case "lone-surrogate": bytes = Utf8Text(text.Replace("\"note\":\"kept\"", "\"note\":\"\\ud800\"")); break;
                case "array": bytes = Utf8Text("[" + text + "]"); break;
                default: bytes = Array.Empty<byte>(); break;
            }
            Assert.That(Assert.Throws<PracticeProtocolException>(() => PracticeRunView.Parse(bytes, RunA)).Code, Is.EqualTo(PracticeProtocolException.Invalid));
        }

        [Test]
        public void PublicLookalikesParseAndTheCompleteReplyIsKept()
        {
            // state.zones, reward card lists, a token card, reservation order, pile counts, "seed" inside text and an unknown public member.
            var json = RunJson(world, 3, Events());
            var view = PracticeRunView.Parse(Utf8(json), RunA);
            Assert.That(JToken.DeepEquals(view.ToJObject(), json), Is.True);
            Assert.That(view.EventsJArray()!.Count, Is.EqualTo(1));
            Assert.That(view.BattleVersion, Is.EqualTo(7));
            Assert.That(view.ServerVersion, Is.EqualTo(3));
            Assert.That(view.StateJObject()["log"]![0]!.Value<string>(), Does.Contain("seed"));
            var copy = view.EventsJArray()!;
            copy.Clear();
            Assert.That(view.EventsJArray()!.Count, Is.EqualTo(1));
            var account = PracticeAccountView.Parse(Utf8(AccountJson(world)), Account);
            Assert.That(account.Run!.ServerVersion, Is.EqualTo(3));
            Assert.That(Assert.Throws<PracticeProtocolException>(() => PracticeAccountView.Parse(Utf8(AccountJson(world)), OtherAccount)).Code,
                Is.EqualTo(PracticeProtocolException.WrongAccount));
        }

        // ---- the pure battle query ----

        [Test]
        public void TheBattleQueryIsPureAndBoundToItsContextAndExactRequest()
        {
            var session = Session();
            var snapshot = Refreshed(session);
            var context = snapshot.Context;
            // The hand card's default next-mode request is blocked; that is no verdict on an explicit pull, so the query is still asked.
            Assert.That(snapshot.Run!.StateJObject()["battle"]!["hand"]![1]!["blocked"]!["next"]!.Value<string>(), Is.EqualTo("PULL_DESTINATION_FULL"));
            bool reverse = false;
            post = request =>
            {
                var asked = JObject.Parse(request.Text);
                var picks = asked["picks"]!.Values<string>().Select(pick => pick!).ToList();
                if (reverse) picks.Reverse();
                return Reply.Json(200, QueryJson(world, asked["uid"]!.Value<string>()!, asked["mode"]!.Value<string>()!, null, picks));
            };
            var answer = Run(() => session.BattleQueryAsync(context, "h2", "next", null, new[] { "r1" }));
            Assert.That(answer.Code, Is.EqualTo(Codes.Ok));
            var sent = host.Posts.Single();
            Assert.That(sent.Path, Is.EqualTo("/dev/practice/runs/" + RunA + "/battle-query"));
            Assert.That(sent.Text, Is.EqualTo("{\"expectedVersion\":3,\"uid\":\"h2\",\"mode\":\"next\",\"picks\":[\"r1\"]}"));
            Assert.That(answer.View!.ForecastError, Is.Null);
            Assert.That(answer.View.Picks, Is.EqualTo(new[] { "r1" }));
            Assert.That(answer.View.Context, Is.EqualTo(context));
            Assert.That(session.IsCurrent(answer), Is.True);
            Assert.That(File.Exists(RecordPath), Is.False, "a query writes no record");
            Assert.That(session.State.Snapshot, Is.SameAs(snapshot), "a query never replaces the snapshot");

            reverse = true;
            Assert.That(Run(() => session.BattleQueryAsync(context, "h2", "next", null, new[] { "r1", "h1" })).Code, Is.EqualTo(PracticeProtocolException.Mismatch));
            post = request => Error(409, "STALE_VERSION");
            Assert.That(Run(() => session.BattleQueryAsync(context, "h2", "next", null, new[] { "r1" })).Code, Is.EqualTo("STALE_VERSION"));
            Assert.That(session.State.NeedsRefresh, Is.True);

            world.Version = 4;
            world.BattleV = 8;
            Refreshed(session);
            Assert.That(session.IsCurrent(answer), Is.False, "an answer for an older context is stale");
            int posts = host.Posts.Length;
            Assert.That(Run(() => session.BattleQueryAsync(context, "h2", "next", null, new[] { "r1" })).Code, Is.EqualTo(Codes.ContextStale));
            Assert.That(host.Posts.Length, Is.EqualTo(posts));
            Assert.That(File.Exists(RecordPath), Is.False);
        }

        // SERVER-ENEMY-PREVIEW: forecast.enemyHpLosses (PublicView's copy of Core's BattlePlayForecast.EnemyHpLosses) is passed on exactly
        // as sent, detached, and only in Core's shape; a reply without it (an older server) is still accepted. The losses are protocol data.
        [Test]
        public void TheBattleQueryPassesCoresPerEnemyHpLossOnAsSentRefusesAnyOtherShapeAndAcceptsAnOlderReplyWithoutIt()
        {
            var session = Session();
            var context = Refreshed(session).Context;
            JToken? split = null, hidden = false;
            string? error = null;
            bool withSplit = false;
            post = request =>
            {
                var asked = JObject.Parse(request.Text);
                var reply = QueryJson(world, asked["uid"]!.Value<string>()!, asked["mode"]!.Value<string>()!, null, asked["picks"]!.Values<string>().Select(p => p!));
                var forecast = (JObject)reply["forecast"]!;
                forecast["error"] = error == null ? JValue.CreateNull() : (JToken)error;
                if (hidden == null) forecast.Remove("hidden");
                else forecast["hidden"] = hidden.DeepClone();
                if (withSplit) forecast["enemyHpLosses"] = split?.DeepClone() ?? JValue.CreateNull();
                return Reply.Json(200, reply);
            };
            PracticeBattleQueryResult Ask() => Run(() => session.BattleQueryAsync(context, "h2", "next", null, new[] { "r1" }));
            static JObject Loss(string uid, JToken hpLoss) => new JObject { ["uid"] = uid, ["hpLoss"] = hpLoss };

            // An older server without the member: accepted, with no split (nothing is derived from enemyHpLoss).
            var older = Ask();
            Assert.That(older.Code, Is.EqualTo(Codes.Ok));
            Assert.That(older.View!.EnemyHpLosses, Is.Empty);
            Assert.That(older.View.ForecastHidden, Is.False);

            // Several enemies losing different amounts: exactly the server's entries in the server's order.
            withSplit = true;
            split = new JArray(Loss("e3", 12), Loss("e1", 8), Loss("e2", 3));
            var answer = Ask();
            Assert.That(answer.Code, Is.EqualTo(Codes.Ok));
            var view = answer.View!;
            Assert.That(view.EnemyHpLosses, Is.EqualTo(new[] { ("e3", 12), ("e1", 8), ("e2", 3) }));
            // Detached both ways: a reply copy, the typed list and the view's JSON cannot change one another.
            var copy = view.ToJObject();
            copy["forecast"]!["enemyHpLosses"]![0]!["hpLoss"] = 99;
            Assert.That(view.EnemyHpLosses[0], Is.EqualTo(("e3", 12)));
            Assert.That(view.ToJObject()["forecast"]!["enemyHpLosses"]![0]!["hpLoss"]!.Value<int>(), Is.EqualTo(12));
            Assert.Throws<NotSupportedException>(() => ((IList<(string Uid, int HpLoss)>)view.EnemyHpLosses)[0] = ("e3", 1));
            Assert.That(view.EnemyHpLosses[0], Is.EqualTo(("e3", 12)));
            Assert.That(session.IsCurrent(answer), Is.True);

            // Empty with an error or a hidden outcome, as Core keeps it: accepted.
            split = new JArray(); error = "NOT_ENOUGH_AP";
            var refusedPlay = Ask();
            Assert.That(refusedPlay.Code, Is.EqualTo(Codes.Ok));
            Assert.That((refusedPlay.View!.ForecastError, refusedPlay.View.EnemyHpLosses.Count), Is.EqualTo(("NOT_ENOUGH_AP", 0)));
            error = null; hidden = true;
            var hiddenAnswer = Ask();
            Assert.That(hiddenAnswer.Code, Is.EqualTo(Codes.Ok));
            Assert.That((hiddenAnswer.View!.ForecastHidden, hiddenAnswer.View.EnemyHpLosses.Count), Is.EqualTo((true, 0)));

            // Any other shape refuses the whole reply (no view, nothing to draw).
            void Rejects(string what, JToken? bad, string? badError = null, bool badHidden = false, bool noHidden = false)
            {
                split = bad; error = badError; hidden = noHidden ? null : (JToken)badHidden;
                var refused = Ask();
                Assert.That(refused.Code, Is.EqualTo(PracticeProtocolException.Invalid), what);
                Assert.That(refused.View, Is.Null, what);
            }
            Rejects("null", null);
            Rejects("an object", new JObject { ["e1"] = 3 });
            Rejects("a number entry", new JArray(3));
            Rejects("an extra member", new JArray(new JObject { ["uid"] = "e1", ["hpLoss"] = 3, ["hp"] = 9 }));
            Rejects("no hpLoss", new JArray(new JObject { ["uid"] = "e1" }));
            Rejects("no uid", new JArray(new JObject { ["hpLoss"] = 3 }));
            Rejects("a zero loss", new JArray(Loss("e1", 0)));
            Rejects("a negative loss", new JArray(Loss("e1", -1)));
            Rejects("a fractional loss", new JArray(Loss("e1", 1.5)));
            Rejects("a text loss", new JArray(Loss("e1", "3")));
            Rejects("a loss above int", new JArray(Loss("e1", 2147483648L)));
            Rejects("an empty uid", new JArray(Loss("", 3)));
            Rejects("a numeric uid", new JArray(new JObject { ["uid"] = 1, ["hpLoss"] = 3 }));
            Rejects("an enemy named twice", new JArray(Loss("e1", 3), Loss("e1", 4)));
            Rejects("a loss with an error", new JArray(Loss("e1", 3)), "NOT_ENOUGH_AP");
            Rejects("a loss with a hidden outcome", new JArray(Loss("e1", 3)), null, true);
            Rejects("a split without hidden", new JArray(Loss("e1", 3)), null, false, true);
            split = new JArray(); error = null; hidden = JValue.CreateString("false");
            var textHidden = Ask();
            Assert.That(textHidden.Code, Is.EqualTo(PracticeProtocolException.Invalid), "a split with a text hidden");

            // A private member name inside an entry refuses the reply as private.
            split = new JArray(new JObject { ["uid"] = "e1", ["hpLoss"] = 3, ["seed"] = 1 }); hidden = false;
            Assert.That(Ask().Code, Is.EqualTo(PracticeProtocolException.PrivateField));

            Assert.That(session.State.NeedsRefresh, Is.False, "a malformed answer is not a newer state");
            Assert.That(File.Exists(RecordPath), Is.False, "a query writes no record");
        }

        // ---- the pure memory preview (P1-SERVER-MEMORY-PREVIEW §8.12/§14.7/§17.4) ----

        [Test]
        public void TheMemoryPreviewIsPureAndKeepsTheFullServerReplyOfExactlyItsSelection()
        {
            MemorySaveWorld(defeat: false);
            var session = Session();
            var snapshot = Refreshed(session);
            var context = snapshot.Context;
            var replies = new List<JObject>();
            post = request =>
            {
                var reply = MemoryPreviewJson(world, SelectedIds(request));
                lock (replies) replies.Add(reply);
                return Reply.Json(200, reply);
            };
            int start = host.Seen.Length;
            // No selection keeps the saved decks; that answer is the server's too.
            var kept = Run(() => session.PreviewMemoryAsync(context, NoSelection));
            Assert.That(kept.Code, Is.EqualTo(Codes.Ok));
            Assert.That(kept.HttpStatus, Is.EqualTo(200));
            var operation = host.Seen.Skip(start).ToArray();
            Assert.That(operation.Select(s => s.Method + " " + s.Path), Is.EqualTo(new[] { "GET " + AccountRoute, "POST /dev/practice/runs/" + RunA + "/memory-preview" }));
            Assert.That(operation.All(s => s.Authorization == "Bearer " + Credential), Is.True);
            Assert.That(operation[1].ContentType, Does.StartWith("application/json"));
            Assert.That(operation[1].Text, Is.EqualTo("{\"selectedChangeIds\":[]}"), "only the selection: no requestId, version or state");
            Assert.That(kept.Context, Is.EqualTo(context));
            Assert.That(kept.View!.Context, Is.EqualTo(context));
            Assert.That(kept.View.SelectedChangeIds, Is.Empty);
            Assert.That(kept.View.Error, Is.Null);
            Assert.That(JToken.DeepEquals(kept.View.ToJObject(), replies[0]), Is.True);

            // One selected change: every party deck as it would result, the costs and the capacities, exactly as the server sent them.
            var added = Run(() => session.PreviewMemoryAsync(context, new[] { ChangeAddK9 }));
            Assert.That(added.Code, Is.EqualTo(Codes.Ok));
            Assert.That(host.Posts.Last().Text, Is.EqualTo("{\"selectedChangeIds\":[\"" + ChangeAddK9 + "\"]}"));
            Assert.That(added.View!.SelectedChangeIds, Is.EqualTo(new[] { ChangeAddK9 }));
            Assert.That(added.View.Error, Is.Null);
            var full = added.View.ToJObject();
            Assert.That(JToken.DeepEquals(full, replies[1]), Is.True);
            Assert.That(full.Properties().Select(p => p.Name), Is.EqualTo(new[] { "runId", "version", "preview" }));
            Assert.That(((JObject)full["preview"]!).Properties().Select(p => p.Name), Is.EqualTo(new[] { "memory", "used", "error", "capacities" }));
            Assert.That(PreviewKeys(added.View, "sera"), Is.EqualTo(new[] { "k1", "k2", "k-common", "k9" }));
            Assert.That(PreviewKeys(added.View, "mira"), Is.EqualTo(new[] { "m1" }), "the whole party's decks, not only the changed one");
            // Copies are detached.
            full["preview"]!["used"]!["sera"] = 0;
            ((JArray)full["preview"]!["memory"]!["sera"]!).Clear();
            Assert.That(JToken.DeepEquals(added.View.ToJObject(), replies[1]), Is.True);

            // Pure: no record or pending request, the same snapshot, nothing due, still free to act; only previews were posted.
            Assert.That(File.Exists(RecordPath), Is.False);
            Assert.That(session.State.Pending, Is.Null);
            Assert.That(session.State.Snapshot, Is.SameAs(snapshot));
            Assert.That(session.State.NeedsRefresh, Is.False);
            Assert.That(session.State.CanMutate, Is.True);
            Assert.That(host.Posts.All(p => p.Path == "/dev/practice/runs/" + RunA + "/memory-preview"), Is.True);
            Assert.That(session.IsCurrent(kept) && session.IsCurrent(added), Is.True);
        }

        [Test]
        public void APreviewVerdictOrRefusalIsTheServersAndChangesNothing()
        {
            MemorySaveWorld(defeat: true);
            var session = Session();
            var snapshot = Refreshed(session);
            var context = snapshot.Context;
            // Two new changes after a defeat: Core's verdict arrives in the answer; the client neither counts nor prices the selection.
            post = request => Reply.Json(200, MemoryPreviewJson(world, SelectedIds(request), "DEFEAT_KEEP_ONE"));
            var two = Run(() => session.PreviewMemoryAsync(context, new[] { ChangeAddK9, ChangeUpgradeK1 }));
            Assert.That(two.Code, Is.EqualTo(Codes.Ok));
            Assert.That(two.View!.Error, Is.EqualTo("DEFEAT_KEEP_ONE"));
            Assert.That(two.View.SelectedChangeIds, Is.EqualTo(new[] { ChangeAddK9, ChangeUpgradeK1 }));
            Assert.That(host.Posts.Last().Text, Is.EqualTo("{\"selectedChangeIds\":[\"" + ChangeAddK9 + "\",\"" + ChangeUpgradeK1 + "\"]}"));
            // A cost above its capacity is a legal answer carrying Core's verdict, not a malformed one.
            post = request =>
            {
                var reply = MemoryPreviewJson(world, SelectedIds(request), "OVER_CAPACITY");
                reply["preview"]!["capacities"]!["sera"] = 1;
                return Reply.Json(200, reply);
            };
            var over = Run(() => session.PreviewMemoryAsync(context, new[] { ChangeAddK9 }));
            Assert.That(over.Code, Is.EqualTo(Codes.Ok));
            Assert.That(over.View!.Error, Is.EqualTo("OVER_CAPACITY"));
            Assert.That(over.View.ToJObject()["preview"]!["capacities"]!["sera"]!.Value<int>(), Is.EqualTo(1));

            // A change the server does not offer: its refusal, and no answer to show.
            post = request => Error(422, "INVALID_SELECTION");
            var unknown = Run(() => session.PreviewMemoryAsync(context, new[] { "delta:none:none:add" }));
            Assert.That(unknown.Code, Is.EqualTo("INVALID_SELECTION"));
            Assert.That(unknown.HttpStatus, Is.EqualTo(422));
            Assert.That(unknown.View, Is.Null);
            Assert.That(session.State.NeedsRefresh, Is.False);

            // A lost answer is only an unanswered question: nothing is pending, and asking again is a new pure preview.
            post = request => new Reply(200, Utf8(MemoryPreviewJson(world, SelectedIds(request)))) { Drop = true };
            var lost = Run(() => session.PreviewMemoryAsync(context, new[] { ChangeAddK9 }));
            Assert.That(lost.Code, Is.EqualTo(Codes.NetworkFailed));
            Assert.That(lost.View, Is.Null);
            Assert.That(session.State.NeedsRefresh, Is.False);
            post = request => Reply.Json(200, MemoryPreviewJson(world, SelectedIds(request)));
            Assert.That(Run(() => session.PreviewMemoryAsync(context, new[] { ChangeAddK9 })).Code, Is.EqualTo(Codes.Ok));

            // A null ID is refused before anything is sent.
            int seen = host.Seen.Length;
            var invalid = Run(() => session.PreviewMemoryAsync(context, new[] { ChangeAddK9, null! }));
            Assert.That(invalid.Code, Is.EqualTo(Codes.InvalidCommand));
            Assert.That(invalid.View, Is.Null);
            Assert.That(host.Seen.Length, Is.EqualTo(seen));

            // Nothing above wrote a record, staged a request, replaced the snapshot or blocked a save.
            Assert.That(File.Exists(RecordPath), Is.False);
            Assert.That(session.State.Pending, Is.Null);
            Assert.That(session.State.Snapshot, Is.SameAs(snapshot));
            Assert.That(session.State.CanMutate, Is.True);

            // WRONG_PHASE although the shown run is on its save screen at this very version: the shown state is not the server's.
            post = request => Error(422, "WRONG_PHASE");
            var phase = Run(() => session.PreviewMemoryAsync(context, NoSelection));
            Assert.That(phase.Code, Is.EqualTo("WRONG_PHASE"));
            Assert.That(phase.View, Is.Null);
            Assert.That(session.State.NeedsRefresh, Is.True);
            Assert.That(Run(() => session.CommitMemoryAsync(context, NoSelection)).Code, Is.EqualTo(Codes.RefreshRequired), "a refused preview never leads into a save");
            Assert.That(host.Posts.All(p => p.Path.EndsWith("/memory-preview", StringComparison.Ordinal)), Is.True);
            Assert.That(File.Exists(RecordPath), Is.False);
        }

        [Test]
        public void APreviewIsAskedOnlyOnTheShownSaveScreenOfAServerThatStillShowsIt()
        {
            // No current run: nothing to preview, nothing asked.
            world.RunId = null;
            var empty = Session();
            var none = Refreshed(empty).Context;
            int asked = host.Seen.Length;
            Assert.That(Run(() => empty.PreviewMemoryAsync(none, NoSelection)).Code, Is.EqualTo(Codes.NoRun));
            Assert.That(host.Seen.Length, Is.EqualTo(asked));
            world.RunId = RunA;

            MemorySaveWorld(defeat: true);
            var session = Session();
            var context = Refreshed(session).Context;
            Assert.That(Run(() => Session().PreviewMemoryAsync(context, NoSelection)).Code, Is.EqualTo(Codes.NoSnapshot));
            post = request => Reply.Json(200, MemoryPreviewJson(world, SelectedIds(request)));

            // The route has no version, so the owner handshake must show exactly the asking context, its saved memory version included;
            // otherwise nothing is posted and the shown state is due a refresh.
            foreach (string moved in new[] { "memory", "version", "run" })
            {
                if (moved == "memory") world.MemoryVersion = 3;
                if (moved == "version") world.Version = 4;
                if (moved == "run") world.RunId = RunB;
                var refused = Run(() => session.PreviewMemoryAsync(context, NoSelection));
                Assert.That(refused.Code, Is.EqualTo(Codes.RefreshRequired), moved);
                Assert.That(refused.HttpStatus, Is.EqualTo(200));
                Assert.That(refused.View, Is.Null);
                Assert.That(session.State.NeedsRefresh, Is.True, moved);
                // A stale screen does not even reach the handshake.
                int handshakes = host.Seen.Length;
                Assert.That(Run(() => session.PreviewMemoryAsync(context, NoSelection)).Code, Is.EqualTo(Codes.RefreshRequired));
                Assert.That(host.Seen.Length, Is.EqualTo(handshakes));
                world.MemoryVersion = 2;
                world.Version = 3;
                world.RunId = RunA;
                Assert.That(Refreshed(session).Context, Is.EqualTo(context));
            }
            world.AccountId = OtherAccount;
            Assert.That(Run(() => session.PreviewMemoryAsync(context, NoSelection)).Code, Is.EqualTo(PracticeProtocolException.WrongAccount));
            world.AccountId = Account;
            Assert.That(host.Posts, Is.Empty);

            // The server moved on between the handshake and the answer: an answer for another version is refused and the shown save
            // screen is due a refresh.
            post = request => { world.Version = 4; return Reply.Json(200, MemoryPreviewJson(world, SelectedIds(request))); };
            var raced = Run(() => session.PreviewMemoryAsync(context, NoSelection));
            Assert.That(raced.Code, Is.EqualTo(PracticeProtocolException.Mismatch));
            Assert.That(raced.HttpStatus, Is.EqualTo(200));
            Assert.That(raced.View, Is.Null);
            Assert.That(session.State.NeedsRefresh, Is.True);
            Assert.That(host.Posts.Length, Is.EqualTo(1));

            // The refresh adopts the newer state; the older context is then stale, refused locally, and its answers are no longer current.
            post = request => Reply.Json(200, MemoryPreviewJson(world, SelectedIds(request)));
            var newer = Refreshed(session).Context;
            Assert.That(newer.ServerVersion, Is.EqualTo(4));
            var current = Run(() => session.PreviewMemoryAsync(newer, NoSelection));
            Assert.That(current.Code, Is.EqualTo(Codes.Ok));
            Assert.That(session.IsCurrent(current), Is.True);
            Assert.That(session.IsCurrent(raced), Is.False);
            int posts = host.Posts.Length;
            Assert.That(Run(() => session.PreviewMemoryAsync(context, NoSelection)).Code, Is.EqualTo(Codes.ContextStale));
            Assert.That(host.Posts.Length, Is.EqualTo(posts));

            // The run was replaced on the server: refresh before anyone acts.
            post = request => Error(404, "RUN_NOT_FOUND");
            var gone = Run(() => session.PreviewMemoryAsync(newer, NoSelection));
            Assert.That(gone.Code, Is.EqualTo("RUN_NOT_FOUND"));
            Assert.That(gone.HttpStatus, Is.EqualTo(404));
            Assert.That(gone.View, Is.Null);
            Assert.That(session.State.NeedsRefresh, Is.True);

            // Once the run has left its save screen, the shown state offers nothing to preview: nothing is asked.
            world.Version = 5;
            world.Phase = "DONE";
            world.Extra!["pending"] = new JObject { ["type"] = "done" };
            var done = Refreshed(session).Context;
            int requests = host.Seen.Length;
            Assert.That(Run(() => session.PreviewMemoryAsync(done, NoSelection)).Code, Is.EqualTo(Codes.NoSave));
            Assert.That(host.Seen.Length, Is.EqualTo(requests));
            Assert.That(File.Exists(RecordPath), Is.False);
        }

        [Test]
        public void APreviewLeavesAnUnresolvedSaveExactlyPendingAndNeverUnblocksANewOne()
        {
            MemorySaveWorld(defeat: false);
            var session = Session();
            var context = Refreshed(session).Context;
            // The save's reply is lost and the server did not commit it; here its outcome is unknown, so it stays pending.
            post = request => request.Path.EndsWith("/memory-commit", StringComparison.Ordinal)
                ? new Reply(200, Utf8(RunJson(world, 4))) { Drop = true }
                : Reply.Json(200, MemoryPreviewJson(world, SelectedIds(request)));
            var save = Run(() => session.CommitMemoryAsync(context, new[] { ChangeAddK9 }));
            Assert.That(save.Status, Is.EqualTo(MutationStatus.Pending));
            var record = Outbox().Load()!;
            byte[] stored = File.ReadAllBytes(RecordPath);

            // The server still shows the save screen, so the pure preview is answered; it neither resolves, re-stages nor clears the save.
            var preview = Run(() => session.PreviewMemoryAsync(context, new[] { ChangeAddK9 }));
            Assert.That(preview.Code, Is.EqualTo(Codes.Ok));
            Assert.That(preview.View!.SelectedChangeIds, Is.EqualTo(new[] { ChangeAddK9 }));
            Assert.That(File.ReadAllBytes(RecordPath), Is.EqualTo(stored));
            Assert.That(session.State.Pending!.Body, Is.EqualTo(record.Body));
            Assert.That(session.State.CanMutate, Is.False);
            var blocked = Run(() => session.CommitMemoryAsync(context, new[] { ChangeAddK9 }));
            Assert.That(blocked.Code, Is.EqualTo(Codes.PendingUnresolved), "a preview never unblocks a new save");
            Assert.That(blocked.Request!.Body, Is.EqualTo(record.Body));
            Assert.That(host.Posts.Select(p => p.Path), Is.EqualTo(new[] { PracticeCommandOutbox.MemoryCommitRoute(RunA), "/dev/practice/runs/" + RunA + "/memory-preview" }));

            // Only the exact retry resolves it: the same bytes again.
            post = request => Error(503, "ACCOUNT_BUSY");
            var retried = Run(() => session.RetryPendingAsync());
            Assert.That(retried.Status, Is.EqualTo(MutationStatus.Pending));
            Assert.That(host.Posts.Length, Is.EqualTo(3));
            Assert.That(host.Posts[2].Body, Is.EqualTo(host.Posts[0].Body));
            Assert.That(Outbox().Load()!.Body, Is.EqualTo(record.Body));
        }

        [TestCase("duplicate-member", PracticeProtocolException.Invalid)]
        [TestCase("trailing", PracticeProtocolException.Invalid)]
        [TestCase("private-member", PracticeProtocolException.PrivateField)]
        [TestCase("private-spec-member", PracticeProtocolException.PrivateField)]
        [TestCase("fraction", PracticeProtocolException.Invalid)]
        [TestCase("negative", PracticeProtocolException.Invalid)]
        [TestCase("beyond-int32", PracticeProtocolException.Invalid)]
        [TestCase("number-text", PracticeProtocolException.Invalid)]
        [TestCase("no-memory", PracticeProtocolException.Invalid)]
        [TestCase("no-used", PracticeProtocolException.Invalid)]
        [TestCase("no-error", PracticeProtocolException.Invalid)]
        [TestCase("no-capacities", PracticeProtocolException.Invalid)]
        [TestCase("error-number", PracticeProtocolException.Invalid)]
        [TestCase("error-prose", PracticeProtocolException.Invalid)]
        [TestCase("deck-object", PracticeProtocolException.Invalid)]
        [TestCase("row-null", PracticeProtocolException.Invalid)]
        [TestCase("row-no-key", PracticeProtocolException.Invalid)]
        [TestCase("row-empty-key", PracticeProtocolException.Invalid)]
        [TestCase("row-repeated-key", PracticeProtocolException.Invalid)]
        [TestCase("row-no-id", PracticeProtocolException.Invalid)]
        [TestCase("row-run-member", PracticeProtocolException.Invalid)]
        [TestCase("row-plus-number", PracticeProtocolException.Invalid)]
        [TestCase("row-mods-list", PracticeProtocolException.Invalid)]
        [TestCase("row-mods-slot", PracticeProtocolException.Invalid)]
        [TestCase("used-missing-deck", PracticeProtocolException.Invalid)]
        [TestCase("capacities-missing-deck", PracticeProtocolException.Invalid)]
        [TestCase("capacities-empty", PracticeProtocolException.Invalid)]
        [TestCase("capacities-foreign-deck", PracticeProtocolException.Invalid)]
        [TestCase("preview-array", PracticeProtocolException.Invalid)]
        [TestCase("version-text", PracticeProtocolException.Invalid)]
        [TestCase("other-run", PracticeProtocolException.Mismatch)]
        [TestCase("other-version", PracticeProtocolException.Mismatch)]
        public void MalformedPrivateOrForeignPreviewRepliesAreRefusedWhole(string change, string expectedCode)
        {
            MemorySaveWorld(defeat: false);
            var session = Session();
            var snapshot = Refreshed(session);
            var json = MemoryPreviewJson(world, new[] { ChangeAddK9 });
            var preview = (JObject)json["preview"]!;
            var sera = (JArray)preview["memory"]!["sera"]!;
            var first = (JObject)sera[0];
            byte[]? raw = null;
            switch (change)
            {
                case "duplicate-member": raw = Utf8Text(json.ToString(Formatting.None).Replace("\"preview\":{", "\"preview\":{\"error\":null,")); break;
                case "trailing": raw = Utf8Text(json.ToString(Formatting.None) + " {}"); break;
                case "private-member": preview["requestId"] = "preview-0001"; break;
                case "private-spec-member": first["seed"] = 7; break;
                case "fraction": preview["used"]!["sera"] = 1.5; break;
                case "negative": preview["capacities"]!["sera"] = -1; break;
                case "beyond-int32": preview["used"]!["sera"] = 2147483648L; break;
                case "number-text": preview["capacities"]!["sera"] = "6"; break;
                case "no-memory": preview.Remove("memory"); break;
                case "no-used": preview.Remove("used"); break;
                case "no-error": preview.Remove("error"); break;
                case "no-capacities": preview.Remove("capacities"); break;
                case "error-number": preview["error"] = 1; break;
                case "error-prose": preview["error"] = "Over capacity, sorry."; break;
                case "deck-object": preview["memory"]!["mira"] = new JObject(); break;
                case "row-null": sera.Add(JValue.CreateNull()); break;
                case "row-no-key": first.Remove("key"); break;
                case "row-empty-key": first["key"] = ""; break;
                case "row-repeated-key": preview["memory"]!["mira"]![0]!["key"] = "k1"; break;
                case "row-no-id": first.Remove("id"); break;
                case "row-run-member": first["owner"] = "sera"; break;
                case "row-plus-number": first["plus"] = 1; break;
                case "row-mods-list": first["mods"] = new JArray("p-b"); break;
                case "row-mods-slot": first["mods"] = new JObject { ["x"] = "p-b" }; break;
                case "used-missing-deck": ((JObject)preview["used"]!).Remove("mira"); break;
                case "capacities-missing-deck": ((JObject)preview["capacities"]!).Remove("sera"); break;
                case "capacities-empty": preview["capacities"] = new JObject(); break;
                case "capacities-foreign-deck":
                    ((JObject)preview["capacities"]!).Remove("sera");
                    preview["capacities"]!["nia"] = 6;
                    break;
                case "preview-array": json["preview"] = new JArray(); break;
                case "version-text": json["version"] = "3"; break;
                case "other-run": json["runId"] = RunB; break;
                case "other-version": json["version"] = 4; break;
                default: Assert.Fail("unknown case " + change); break;
            }
            post = request => raw != null ? new Reply(200, raw) : Reply.Json(200, json);
            var result = Run(() => session.PreviewMemoryAsync(snapshot.Context, new[] { ChangeAddK9 }));
            Assert.That(result.Code, Is.EqualTo(expectedCode));
            Assert.That(result.HttpStatus, Is.EqualTo(200));
            Assert.That(result.View, Is.Null);
            Assert.That(session.State.Snapshot, Is.SameAs(snapshot));
            Assert.That(session.State.NeedsRefresh, Is.EqualTo(expectedCode == PracticeProtocolException.Mismatch), "only another run or version means the server moved");
            Assert.That(File.Exists(RecordPath), Is.False);
            // The same bytes are refused the same way when parsed directly.
            byte[] bytes = raw ?? Utf8(json);
            Assert.That(Assert.Throws<PracticeProtocolException>(() => PracticeMemoryPreviewView.Parse(bytes, snapshot.Context, new[] { ChangeAddK9 })).Code,
                Is.EqualTo(expectedCode));
        }

        [Test]
        public void APreviewKeepsTheSelectionItWasAskedForAgainstLaterCallerChangesAndNewerSelections()
        {
            MemorySaveWorld(defeat: false);
            var session = Session();
            var context = Refreshed(session).Context;
            var posted = new TaskCompletionSource<Seen>();
            var release = new TaskCompletionSource<bool>();
            post = request =>
            {
                posted.TrySetResult(request);
                return new Reply(200, Utf8(MemoryPreviewJson(world, SelectedIds(request)))) { Hold = release.Task };
            };
            try
            {
                var selection = new List<string> { ChangeAddK9 };
                var asking = session.PreviewMemoryAsync(context, selection);
                // The caller's list changes while the answer is outstanding; the request and its answer keep the copy taken at the call.
                selection[0] = ChangeUpgradeK1;
                selection.Add("delta:sera:k2:remove");
                Assert.That(posted.Task.Wait(Bound), Is.True);
                Assert.That(posted.Task.Result.Text, Is.EqualTo("{\"selectedChangeIds\":[\"" + ChangeAddK9 + "\"]}"));
                // One operation at a time: a second preview is refused at once, nothing queued.
                Assert.That(Run(() => session.PreviewMemoryAsync(context, new[] { ChangeUpgradeK1 })).Code, Is.EqualTo(Codes.Busy));
                release.TrySetResult(true);
                var first = Wait(asking);
                Assert.That(first.Code, Is.EqualTo(Codes.Ok));
                Assert.That(first.View!.SelectedChangeIds, Is.EqualTo(new[] { ChangeAddK9 }));
                Assert.That(((ICollection<string>)first.View.SelectedChangeIds).IsReadOnly, Is.True);
                selection.Clear();
                Assert.That(first.View.SelectedChangeIds, Is.EqualTo(new[] { ChangeAddK9 }));

                // A newer selection at the same server version: both answers belong to the current context, so only the selection each
                // one carries tells the screen which answers what it shows now.
                post = request => Reply.Json(200, MemoryPreviewJson(world, SelectedIds(request)));
                var second = Run(() => session.PreviewMemoryAsync(context, new[] { ChangeUpgradeK1 }));
                Assert.That(second.Code, Is.EqualTo(Codes.Ok));
                Assert.That(second.View!.SelectedChangeIds, Is.EqualTo(new[] { ChangeUpgradeK1 }));
                Assert.That(second.View.Context, Is.EqualTo(first.View.Context));
                Assert.That(session.IsCurrent(first) && session.IsCurrent(second), Is.True);
                Assert.That(PreviewKeys(first.View, "sera"), Does.Contain("k9"));
                Assert.That(PreviewKeys(second.View, "sera"), Does.Not.Contain("k9"));
                Assert.That(host.Posts.Length, Is.EqualTo(2));
                Assert.That(File.Exists(RecordPath), Is.False);
            }
            finally { release.TrySetResult(true); }
        }

#if UNITY_5_3_OR_NEWER
        // ---- the server practice view's rest/forge/shop services (Unity only: the real MemoryForgeServerPracticeClient) ----
        // P1-SERVER-SERVICES-UI §7.3/§14.3/§14.7/§17.4. The fixture plays the server: a commit moves the world to the next version and the
        // receipt and every later read show that state. These check the exact command body, one send while its reply is held, the lock
        // while a request is uncertain, an unchanged screen after a refusal, only the committed server state after a commit, and that a
        // deck selection is never reused after cancel, progress or reconnect. Fixture ids and prices are protocol data, not content.

        GameObject? viewObject;

        [UnityTest]
        public IEnumerator AShopBuyIsSentOnceWhileHeldAndOnlyTheCommittedServerStateIsShown()
        {
            Node("SHOP", Shop(), coins: 100);
            var view = View();
            yield return Settled(view);
            Assert.That(view.ShowingService, Is.EqualTo("shop"));
            var posted = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();
            post = request =>
            {
                posted.TrySetResult(true);
                return Commit(state => { state["coins"] = 30; Listing(state, "cards", 0)["sold"] = true; }, release.Task);
            };
            try
            {
                var buying = view.BuyAsync("cards", "c-shop");
                yield return Until(() => posted.Task.IsCompleted, "the buy is posted");
                Assert.That(view.Busy, Is.True);
                Assert.That(view.CanCommand, Is.False);
                // A second click, another listing, leaving and the removal sheet are refused at once: nothing is queued or sent.
                Assert.That(Refused(view.BuyAsync("cards", "c-shop")), Is.True);
                Assert.That(Refused(view.BuyAsync("parts", "p-shop")), Is.True);
                Assert.That(Refused(view.ProceedAsync()), Is.True);
                Assert.That(view.OpenDeckPick(Purpose.ShopRemove), Is.False);
                // Nothing is shown as bought before the server answers.
                Assert.That(Shown(view)["coins"]!.Value<int>(), Is.EqualTo(100));
                Assert.That(Listing(Shown(view), "cards", 0)["sold"]!.Value<bool>(), Is.False);
                release.TrySetResult(true);
                yield return Until(() => buying.IsCompleted, "the buy resolves");
                Assert.That(buying.Result, Is.True);
            }
            finally { release.TrySetResult(true); }
            Sent(host.Posts.Single(), "buy", 3, new JObject { ["kind"] = "cards", ["id"] = "c-shop" });
            Assert.That(Outbox().Load(), Is.Null);
            Assert.That(view.Snapshot!.Context.ServerVersion, Is.EqualTo(4));
            Assert.That(view.SnapshotStale, Is.False);
            Assert.That(Shown(view)["coins"]!.Value<int>(), Is.EqualTo(30));
            Assert.That(Listing(Shown(view), "cards", 0)["sold"]!.Value<bool>(), Is.True);
            // The sold listing is no longer offered, and an unlisted id or kind never was: nothing more is sent.
            Assert.That(Refused(view.BuyAsync("cards", "c-shop")), Is.True);
            Assert.That(Refused(view.BuyAsync("cards", "c-missing")), Is.True);
            Assert.That(Refused(view.BuyAsync("remove", "c-dear")), Is.True);
            Assert.That(host.Posts.Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ACoreRefusalChangesNothingShownAndTheShopRemovalSendsOnlyTheConfirmedKey()
        {
            Node("SHOP", Shop(), coins: 60);
            var view = View();
            yield return Settled(view);
            // c-dear costs more than the shown coins: its button stays disabled, but COINS_LOW is Core's, so the explicit call is sent.
            post = request => Error(422, "COINS_LOW");
            var refused = view.BuyAsync("cards", "c-dear");
            yield return Until(() => refused.IsCompleted, "the refused buy resolves");
            Assert.That(refused.Result, Is.False);
            Sent(host.Posts.Single(), "buy", 3, new JObject { ["kind"] = "cards", ["id"] = "c-dear" });
            Assert.That(Outbox().Load(), Is.Null, "a proven Core refusal clears the request");
            Assert.That(view.Notice, Does.Contain("반영되지 않았어요"));
            Assert.That(view.Snapshot!.Context.ServerVersion, Is.EqualTo(3));
            Assert.That(Shown(view)["coins"]!.Value<int>(), Is.EqualTo(60));
            Assert.That(Listing(Shown(view), "cards", 1)["sold"]!.Value<bool>(), Is.False);
            Assert.That(view.CanCommand, Is.True);

            // Removal: the sheet refuses weapon or unknown cards, blocks every other command and sends nothing until confirmed.
            Assert.That(view.OpenDeckPick(Purpose.ShopRemove), Is.True);
            Assert.That(view.CanCommand, Is.False);
            Assert.That(Refused(view.ProceedAsync()), Is.True);
            Assert.That(view.PickDeckCard("k3"), Is.False, "a weapon card");
            Assert.That(view.PickDeckCard("k-missing"), Is.False);
            Assert.That(view.PickDeckCard("k2"), Is.True);
            Assert.That(view.DeckPickKey, Is.EqualTo("k2"));
            Assert.That(host.Posts.Length, Is.EqualTo(1));
            post = request => Commit(state => { state["coins"] = 10; state["pending"]!["removeCost"] = null; Remove(state, "k2"); });
            var removing = view.ConfirmDeckPickAsync();
            Assert.That(view.DeckPickOpen, Is.False, "the sheet is gone once sent");
            yield return Until(() => removing.IsCompleted, "the removal resolves");
            Assert.That(removing.Result, Is.True);
            Assert.That(host.Posts.Length, Is.EqualTo(2));
            Sent(host.Posts[1], "buy", 3, new JObject { ["kind"] = "remove", ["key"] = "k2" });
            Assert.That(view.Snapshot!.Context.ServerVersion, Is.EqualTo(4));
            Assert.That(Shown(view)["pending"]!["removeCost"]!.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(Shown(view)["deck"]!.Select(c => c["key"]!.Value<string>()), Is.EqualTo(new[] { "k1", "k3" }));
            Assert.That(view.OpenDeckPick(Purpose.ShopRemove), Is.False, "one removal per shop, as published");
            Assert.That(host.Posts.Length, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator AnUncertainServiceSendLocksEveryCommandShowsNoGainAndRetriesTheExactBytes()
        {
            Node("REST", Rest(1), coins: 12, hp: 20);
            var view = View();
            yield return Settled(view);
            byte[] receipt = Array.Empty<byte>();
            // The server commits the heal and its reply never arrives.
            post = request =>
            {
                receipt = Commit(state => { state["hp"] = 35; state["pending"]!["uses"] = 0; }).Body;
                return new Reply(200, receipt) { Drop = true };
            };
            var healing = view.RestHealAsync();
            yield return Until(() => healing.IsCompleted, "the lost reply resolves");
            Assert.That(healing.Result, Is.False);
            Sent(host.Posts.Single(), "rest", 3, new JObject { ["choice"] = "heal" });
            Assert.That(Outbox().Load()!.Body, Is.EqualTo(host.Posts[0].Text));
            Assert.That(view.SessionState!.Pending, Is.Not.Null);
            // No gain is shown while unresolved: the last good state, marked stale.
            Assert.That(view.SnapshotStale, Is.True);
            Assert.That(Shown(view)["hp"]!.Value<int>(), Is.EqualTo(20));
            Assert.That(Shown(view)["pending"]!["uses"]!.Value<int>(), Is.EqualTo(1));
            // Every new command is refused before anything is sent.
            Assert.That(view.CanCommand, Is.False);
            Assert.That(Refused(view.RestHealAsync()), Is.True);
            Assert.That(view.OpenDeckPick(Purpose.Upgrade), Is.False);
            Assert.That(Refused(view.ProceedAsync()), Is.True);
            Assert.That(host.Posts.Length, Is.EqualTo(1));

            // The explicit retry sends the stored bytes again and then shows the server's committed state.
            post = request => new Reply(200, receipt);
            var retry = view.RetryAsync();
            yield return Until(() => retry.IsCompleted, "the retry resolves");
            Assert.That(retry.Result, Is.True);
            Assert.That(host.Posts.Length, Is.EqualTo(2));
            Assert.That(host.Posts[1].Body, Is.EqualTo(host.Posts[0].Body));
            Assert.That(Outbox().Load(), Is.Null);
            Assert.That(view.Snapshot!.Context.ServerVersion, Is.EqualTo(4));
            Assert.That(Shown(view)["hp"]!.Value<int>(), Is.EqualTo(35));
            Assert.That(Shown(view)["pending"]!["uses"]!.Value<int>(), Is.EqualTo(0));
            // The used rest offers neither choice any more.
            Assert.That(Refused(view.RestHealAsync()), Is.True);
            Assert.That(view.OpenDeckPick(Purpose.Upgrade), Is.False);
            Assert.That(host.Posts.Length, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator ARefusedHealChangesNothingAndADeckSelectionIsNeverReusedAfterCancelProgressOrReconnect()
        {
            Node("REST", Rest(1), coins: 12, hp: 20);
            world.Extra!["relics"] = new JArray("memory_anvil");
            var view = View();
            yield return Settled(view);
            // Whether healing is allowed is Core's (CANNOT_HEAL): nothing changes and the rest stays usable.
            post = request => Error(422, "CANNOT_HEAL");
            var heal = view.RestHealAsync();
            yield return Until(() => heal.IsCompleted, "the refused heal resolves");
            Assert.That(heal.Result, Is.False);
            Sent(host.Posts.Single(), "rest", 3, new JObject { ["choice"] = "heal" });
            Assert.That(Outbox().Load(), Is.Null);
            Assert.That(view.Snapshot!.Context.ServerVersion, Is.EqualTo(3));
            Assert.That(Shown(view)["hp"]!.Value<int>(), Is.EqualTo(20));
            Assert.That(Shown(view)["pending"]!["uses"]!.Value<int>(), Is.EqualTo(1));
            Assert.That(view.CanCommand, Is.True);

            // Cancel: nothing is sent and a reopened sheet starts empty.
            Assert.That(view.OpenDeckPick(Purpose.Upgrade), Is.True);
            Assert.That(view.PickDeckCard("k2"), Is.False, "already upgraded");
            Assert.That(view.PickDeckCard("k3"), Is.False, "a weapon card");
            Assert.That(view.PickDeckCard("k1"), Is.True);
            view.CancelDeckPick();
            Assert.That(view.DeckPickOpen, Is.False);
            Assert.That(Refused(view.ConfirmDeckPickAsync()), Is.True);
            Assert.That(view.OpenDeckPick(Purpose.Upgrade), Is.True);
            Assert.That(view.DeckPickKey, Is.Null);

            // Progress elsewhere: another device moved the run on; the refresh closes the sheet bound to the older context, unsent.
            Assert.That(view.PickDeckCard("k1"), Is.True);
            world.Version = 4;
            var refresh = view.RefreshAsync();
            yield return Until(() => refresh.IsCompleted, "the refresh resolves");
            Assert.That(view.Snapshot!.Context.ServerVersion, Is.EqualTo(4));
            Assert.That(view.DeckPickOpen, Is.False);
            Assert.That(Refused(view.ConfirmDeckPickAsync()), Is.True);

            // Reconnect: leaving drops the unsent sheet with the session, and the new session starts without it.
            Assert.That(view.OpenDeckPick(Purpose.Upgrade), Is.True);
            Assert.That(view.PickDeckCard("k1"), Is.True);
            view.enabled = false;
            Assert.That(view.DeckPickOpen, Is.False);
            view.enabled = true;
            yield return Settled(view);
            Assert.That(view.DeckPickOpen, Is.False);
            Assert.That(view.DeckPickKey, Is.Null);
            Assert.That(Refused(view.ConfirmDeckPickAsync()), Is.True);
            Assert.That(host.Posts.Length, Is.EqualTo(1), "no selection above was ever sent");

            // A fresh choice is sent once at the current version; the used rest then offers only leaving.
            Assert.That(view.OpenDeckPick(Purpose.Upgrade), Is.True);
            Assert.That(view.PickDeckCard("k1"), Is.True);
            post = request => Commit(state => { state["pending"]!["uses"] = 0; state["deck"]![0]!["plus"] = true; });
            var upgrade = view.ConfirmDeckPickAsync();
            yield return Until(() => upgrade.IsCompleted, "the upgrade resolves");
            Assert.That(upgrade.Result, Is.True);
            Sent(host.Posts[1], "rest", 4, new JObject { ["choice"] = "upgrade", ["key"] = "k1" });
            Assert.That(Shown(view)["deck"]![0]!["plus"]!.Value<bool>(), Is.True);
            Assert.That(view.OpenDeckPick(Purpose.Upgrade), Is.False);
            Assert.That(Refused(view.RestHealAsync()), Is.True);

            // Leaving is the existing proceed; the next phase is the server's.
            post = request => Commit(state => state["pending"] = null, phase: "MAP");
            var leaving = view.ProceedAsync();
            yield return Until(() => leaving.IsCompleted, "proceed resolves");
            Assert.That(leaving.Result, Is.True);
            Sent(host.Posts[2], "proceed", 5, new JObject());
            Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("MAP"));
            Assert.That(view.ShowingService, Is.Null);
            Assert.That(Refused(view.ProceedAsync()), Is.True, "nothing to leave on the map");
            Assert.That(host.Posts.Length, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator AForgeVariationSendsTheChosenCardThenOnlyAPublishedPartAndNeverReRolls()
        {
            Node("FORGE", new JObject { ["type"] = "forge", ["uses"] = 1, ["offers"] = null }, coins: 12);
            var view = View();
            yield return Settled(view);
            Assert.That(view.ShowingService, Is.EqualTo("forge"));
            Assert.That(Refused(view.ForgeApplyAsync("p-a")), Is.True, "no offer is open");

            Assert.That(view.OpenDeckPick(Purpose.ForgeOffer), Is.True);
            Assert.That(view.PickDeckCard("k3"), Is.False, "a weapon card");
            Assert.That(view.PickDeckCard("k2"), Is.True, "an upgraded card may still take a part");
            post = request => Commit(state => state["pending"]!["offers"] = new JObject { ["key"] = "k2", ["parts"] = new JArray("p-a", "p-b", "p-c") });
            var offering = view.ConfirmDeckPickAsync();
            yield return Until(() => offering.IsCompleted, "the variation offer resolves");
            Assert.That(offering.Result, Is.True);
            Sent(host.Posts.Single(), "forgeOffer", 3, new JObject { ["key"] = "k2" });
            Assert.That(Shown(view)["pending"]!["offers"]!["parts"]!.Values<string>(), Is.EqualTo(new[] { "p-a", "p-b", "p-c" }));
            Assert.That(Shown(view)["pending"]!["uses"]!.Value<int>(), Is.EqualTo(1), "an offer spends no use");

            // While the offer is open: no second offer (re-roll) or removal, and only a published part is sent.
            Assert.That(view.OpenDeckPick(Purpose.ForgeOffer), Is.False);
            Assert.That(view.OpenDeckPick(Purpose.ForgeRemove), Is.False);
            Assert.That(Refused(view.ForgeApplyAsync("p-z")), Is.True);
            Assert.That(host.Posts.Length, Is.EqualTo(1));
            post = request => Commit(state =>
            {
                state["pending"]!["offers"] = null;
                state["pending"]!["uses"] = 0;
                state["deck"]![1]!["mods"] = new JObject { ["c"] = "p-b", ["e"] = null };
            });
            var applying = view.ForgeApplyAsync("p-b");
            yield return Until(() => applying.IsCompleted, "the part resolves");
            Assert.That(applying.Result, Is.True);
            Sent(host.Posts[1], "forgeApply", 4, new JObject { ["partId"] = "p-b" });
            Assert.That(Shown(view)["deck"]![1]!["mods"]!["c"]!.Value<string>(), Is.EqualTo("p-b"));
            Assert.That(Refused(view.ForgeApplyAsync("p-b")), Is.True, "the offer is closed");
            Assert.That(view.OpenDeckPick(Purpose.ForgeRemove), Is.False, "no use left");
            Assert.That(host.Posts.Length, Is.EqualTo(2));
        }

        // ---- events and the archive (P1-SERVER-EVENT-ARCHIVE-UI §7.5/§7.7) ----
        // The event ids are the real run content's (the view draws and counts picks from it); the committed states are what Core's
        // EventChoose/Archive publish for that choice with this fixture's party (hpRef 0: event hp values apply as authored).

        [UnityTest]
        public IEnumerator AnEventChoiceIsSentOnceAsItsIndexAndAMadeChoiceIsNeverChosenAgain()
        {
            // ev_busker: 0 노래를 청한다 (need 25), 1 같이 쉰다 (heal 15), 2 지나친다.
            Node("EVENT", Event("ev_busker"), coins: 12, hp: 20);
            var view = View();
            yield return Settled(view);
            Assert.That(Refused(view.ProceedAsync()), Is.True, "leaving waits for a choice, as in the original");
            Assert.That(Refused(view.ChooseEventAsync(3)), Is.True, "no such choice in the content");
            Assert.That(Refused(view.ChooseEventAsync(-1)), Is.True);
            Assert.That(host.Posts, Is.Empty);
            var posted = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();
            post = request =>
            {
                posted.TrySetResult(true);
                return Commit(state => { state["hp"] = 35; state["pending"]!["done"] = true; state["pending"]!["choice"] = 1; }, release.Task);
            };
            try
            {
                var resting = view.ChooseEventAsync(1);
                yield return Until(() => posted.Task.IsCompleted, "the choice is posted");
                Assert.That(view.Busy, Is.True);
                // Another choice, the same one again and leaving are refused at once; nothing is shown as gained yet.
                Assert.That(Refused(view.ChooseEventAsync(2)), Is.True);
                Assert.That(Refused(view.ChooseEventAsync(1)), Is.True);
                Assert.That(Refused(view.ProceedAsync()), Is.True);
                Assert.That(Shown(view)["hp"]!.Value<int>(), Is.EqualTo(20));
                Assert.That(Shown(view)["pending"]!["done"], Is.Null);
                release.TrySetResult(true);
                yield return Until(() => resting.IsCompleted, "the choice resolves");
                Assert.That(resting.Result, Is.True);
            }
            finally { release.TrySetResult(true); }
            Sent(host.Posts.Single(), "event", 3, new JObject { ["index"] = 1 });
            Assert.That(Shown(view)["hp"]!.Value<int>(), Is.EqualTo(35));
            Assert.That(Shown(view)["pending"]!["choice"]!.Value<int>(), Is.EqualTo(1));
            // The event resolves once: every choice is refused before sending; leaving is now the explicit proceed.
            Assert.That(Refused(view.ChooseEventAsync(0)), Is.True);
            Assert.That(Refused(view.ChooseEventAsync(1)), Is.True);
            Assert.That(host.Posts.Length, Is.EqualTo(1));
            post = request => Commit(state => state["pending"] = null, phase: "MAP");
            var leaving = view.ProceedAsync();
            yield return Until(() => leaving.IsCompleted, "proceed resolves");
            Assert.That(leaving.Result, Is.True);
            Sent(host.Posts[1], "proceed", 4, new JObject());
            Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("MAP"));
        }

        [UnityTest]
        public IEnumerator AnEventRemovalAsksItsAuthoredCountAndNeverSendsACanceledOrOutdatedSelection()
        {
            // ev_archive: 0 정리한다 (maxHp -5, cardRemove 2), 1 한 권 빌린다, 2 지나친다.
            Node("EVENT", Event("ev_archive"), coins: 12, hp: 20);
            var view = View();
            yield return Settled(view);
            var opening = view.ChooseEventAsync(0);
            Assert.That(opening.IsCompleted && !opening.Result, Is.True, "a removal first opens the deck sheet");
            Assert.That(view.DeckPickOpen, Is.True);
            Assert.That(view.DeckPickPurpose, Is.EqualTo(Purpose.EventCards));
            Assert.That(view.DeckPickRequired, Is.EqualTo(2));
            Assert.That(view.CanCommand, Is.False, "the open sheet blocks every other command");
            Assert.That(Refused(view.ChooseEventAsync(2)), Is.True);
            Assert.That(view.PickDeckCard("k3"), Is.False, "a weapon card");
            Assert.That(view.PickDeckCard("k1"), Is.True);
            Assert.That(Refused(view.ConfirmDeckPickAsync()), Is.True, "one of two");
            Assert.That(view.DeckPickOpen, Is.True, "a refused send leaves the sheet open");
            Assert.That(view.PickDeckCard("k2"), Is.True);
            Assert.That(view.DeckPickKeys, Is.EqualTo(new[] { "k1", "k2" }));

            // Cancel sends nothing; the reopened sheet starts empty.
            view.CancelDeckPick();
            Assert.That(view.DeckPickOpen, Is.False);
            view.ChooseEventAsync(0);
            Assert.That(view.DeckPickKeys, Is.Empty);

            // Another device moved the run on: the refresh closes the sheet of the older context, unsent.
            Assert.That(view.PickDeckCard("k1"), Is.True);
            Assert.That(view.PickDeckCard("k2"), Is.True);
            world.Version = 4;
            var refresh = view.RefreshAsync();
            yield return Until(() => refresh.IsCompleted, "the refresh resolves");
            Assert.That(view.DeckPickOpen, Is.False);
            Assert.That(Refused(view.ConfirmDeckPickAsync()), Is.True);
            Assert.That(host.Posts, Is.Empty);

            // A fresh choice sends the index and both keys once, in the order chosen.
            view.ChooseEventAsync(0);
            Assert.That(view.PickDeckCard("k2"), Is.True);
            Assert.That(view.PickDeckCard("k1"), Is.True);
            post = request => Commit(state =>
            {
                state["maxHp"] = 45;
                Remove(state, "k1");
                Remove(state, "k2");
                state["pending"]!["done"] = true;
                state["pending"]!["choice"] = 0;
            });
            var removing = view.ConfirmDeckPickAsync();
            Assert.That(view.DeckPickOpen, Is.False);
            yield return Until(() => removing.IsCompleted, "the removal resolves");
            Assert.That(removing.Result, Is.True);
            Sent(host.Posts.Single(), "event", 4, new JObject { ["index"] = 0, ["keys"] = new JArray("k2", "k1") });
            Assert.That(Shown(view)["deck"]!.Select(c => c["key"]!.Value<string>()), Is.EqualTo(new[] { "k3" }));
            Assert.That(Refused(view.ChooseEventAsync(0)), Is.True, "the event resolves once");
            Assert.That(host.Posts.Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AnUncertainEventCopyLocksEveryCommandShowsNoCopyAndRetriesTheExactBytes()
        {
            // ev_mirror: 0 한 조각을 집는다 (hp -6, cardCopy 1), 1 조각을 묻는다 (need 30, cardRemove 1), 2 지나친다.
            Node("EVENT", Event("ev_mirror"), coins: 12, hp: 20);
            var view = View();
            yield return Settled(view);
            view.ChooseEventAsync(0);
            Assert.That(view.DeckPickRequired, Is.EqualTo(1));
            Assert.That(view.PickDeckCard("k1"), Is.True);
            Assert.That(view.PickDeckCard("k2"), Is.True, "a one-card sheet replaces its choice");
            Assert.That(view.DeckPickKeys, Is.EqualTo(new[] { "k2" }));
            byte[] receipt = Array.Empty<byte>();
            // The server commits the copy and its reply never arrives.
            post = request =>
            {
                receipt = Commit(state =>
                {
                    state["hp"] = 14;
                    ((JArray)state["deck"]!).Add(new JObject { ["key"] = "k4", ["id"] = "guard", ["owner"] = "sera", ["plus"] = true, ["gained"] = true });
                    state["pending"]!["done"] = true;
                    state["pending"]!["choice"] = 0;
                }).Body;
                return new Reply(200, receipt) { Drop = true };
            };
            var copying = view.ConfirmDeckPickAsync();
            yield return Until(() => copying.IsCompleted, "the lost reply resolves");
            Assert.That(copying.Result, Is.False);
            Sent(host.Posts.Single(), "event", 3, new JObject { ["index"] = 0, ["keys"] = new JArray("k2") });
            Assert.That(view.SessionState!.Pending, Is.Not.Null);
            Assert.That(view.SnapshotStale, Is.True);
            Assert.That(((JArray)Shown(view)["deck"]!).Count, Is.EqualTo(3), "no copy is shown while unresolved");
            Assert.That(Shown(view)["hp"]!.Value<int>(), Is.EqualTo(20));
            Assert.That(Refused(view.ChooseEventAsync(2)), Is.True);
            Assert.That(Refused(view.ChooseEventAsync(0)), Is.True);
            Assert.That(Refused(view.ProceedAsync()), Is.True);
            Assert.That(view.DeckPickOpen, Is.False);
            Assert.That(host.Posts.Length, Is.EqualTo(1));

            post = request => new Reply(200, receipt);
            var retry = view.RetryAsync();
            yield return Until(() => retry.IsCompleted, "the retry resolves");
            Assert.That(retry.Result, Is.True);
            Assert.That(host.Posts.Length, Is.EqualTo(2));
            Assert.That(host.Posts[1].Body, Is.EqualTo(host.Posts[0].Body));
            Assert.That(Outbox().Load(), Is.Null);
            Assert.That(((JArray)Shown(view)["deck"]!).Count, Is.EqualTo(4));
            Assert.That(Shown(view)["hp"]!.Value<int>(), Is.EqualTo(14));
            Assert.That(Refused(view.ChooseEventAsync(2)), Is.True, "resolved once");
        }

        [UnityTest]
        public IEnumerator TheArchiveSendsOnlyItsExistingChoicesOnceAndIsNotChosenAgainAfterContinuing()
        {
            Node("ARCHIVE", new JObject { ["type"] = "archive" }, coins: 12, hp: 20);
            var view = View();
            yield return Settled(view);
            Assert.That(Refused(view.ArchiveAsync("abandon")), Is.True, "only continue or return");
            Assert.That(Refused(view.ProceedAsync()), Is.True, "the archive is left by its own choice");
            Assert.That(host.Posts, Is.Empty);
            var posted = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();
            // Continue: Core heals floor(maxHp * 15 / 100) = 7 and opens the next zone's map.
            post = request =>
            {
                posted.TrySetResult(true);
                return Commit(state => { state["hp"] = 27; state["zoneIndex"] = 1; state["zone"] = 2; state["floor"] = 0; state["position"] = null; state["pending"] = null; },
                    release.Task, "MAP");
            };
            try
            {
                var continuing = view.ArchiveAsync("continue");
                yield return Until(() => posted.Task.IsCompleted, "the archive choice is posted");
                Assert.That(Refused(view.ArchiveAsync("return")), Is.True);
                Assert.That(Refused(view.ArchiveAsync("continue")), Is.True);
                Assert.That(Shown(view)["hp"]!.Value<int>(), Is.EqualTo(20));
                release.TrySetResult(true);
                yield return Until(() => continuing.IsCompleted, "the archive choice resolves");
                Assert.That(continuing.Result, Is.True);
            }
            finally { release.TrySetResult(true); }
            Sent(host.Posts.Single(), "archive", 3, new JObject { ["choice"] = "continue" });
            Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("MAP"));
            Assert.That(Shown(view)["hp"]!.Value<int>(), Is.EqualTo(27));
            Assert.That(Refused(view.ArchiveAsync("return")), Is.True, "the archive was already handled");
            Assert.That(host.Posts.Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ReturningFromTheArchiveShowsTheServersSaveStateWhichStaysALaterUnit()
        {
            Node("ARCHIVE", new JObject { ["type"] = "archive" }, coins: 12, hp: 20);
            var view = View();
            yield return Settled(view);
            post = request => Commit(state =>
            {
                state["pending"] = new JObject { ["type"] = "save", ["defeat"] = false };
                state["result"] = new JObject { ["outcome"] = "return", ["floor"] = 2 };
            }, phase: "VICTORY");
            var returning = view.ArchiveAsync("return");
            yield return Until(() => returning.IsCompleted, "the return resolves");
            Assert.That(returning.Result, Is.True);
            Sent(host.Posts.Single(), "archive", 3, new JObject { ["choice"] = "return" });
            Assert.That(view.Snapshot!.Run!.Phase, Is.EqualTo("VICTORY"));
            // No save screen here yet: nothing on this phase sends anything.
            Assert.That(Refused(view.ArchiveAsync("return")), Is.True);
            Assert.That(Refused(view.ProceedAsync()), Is.True);
            Assert.That(Refused(view.ChooseEventAsync(0)), Is.True);
            Assert.That(host.Posts.Length, Is.EqualTo(1));
        }

        MemoryForgeServerPracticeClient View()
        {
            viewObject = new GameObject("ServerPracticeServicesTest");
            var view = viewObject.AddComponent<MemoryForgeServerPracticeClient>();
            Assert.That(view.Connect(() => Session()), Is.True);
            return view;
        }

        // A node-service run: PublicView's state members for the phase, its pending and a three-card deck.
        void Node(string phase, JObject pending, int coins, int hp = 40)
        {
            world.Phase = phase;
            world.InBattle = false;
            world.Extra = new JObject { ["hp"] = hp, ["coins"] = coins, ["pending"] = pending, ["deck"] = Deck(), ["relics"] = new JArray(), ["partsFound"] = new JArray() };
        }

        // The fixture server commits: the world moves to the next version with the change applied, and the receipt is that run.
        Reply Commit(Action<JObject> change, Task? hold = null, string? phase = null)
        {
            world.Version++;
            if (phase != null) world.Phase = phase;
            change(world.Extra!);
            return new Reply(200, Utf8(RunJson(world, world.Version))) { Hold = hold };
        }

        static JObject Rest(int uses) => new JObject { ["type"] = "rest", ["uses"] = uses };
        // PublicView's unresolved event pending: {type, id}; done and choice appear only once Core resolves it.
        static JObject Event(string id) => new JObject { ["type"] = "event", ["id"] = id };
        static JObject Shop() => new JObject
        {
            ["type"] = "shop", ["cards"] = new JArray(Listed("c-shop", 70), Listed("c-dear", 110)), ["parts"] = new JArray(Listed("p-shop", 60)),
            ["relics"] = new JArray(Listed("r-shop", 120)), ["removeCost"] = 50
        };
        static JObject Listed(string id, int price) => new JObject { ["id"] = id, ["price"] = price, ["sold"] = false };
        // k1 a plain card, k2 an upgraded one, k3 a weapon card (PublicView deck members).
        static JArray Deck() => new JArray(
            new JObject { ["key"] = "k1", ["id"] = "strike", ["owner"] = "sera", ["plus"] = false },
            new JObject { ["key"] = "k2", ["id"] = "guard", ["owner"] = "sera", ["plus"] = true },
            new JObject { ["key"] = "k3", ["id"] = "blade", ["owner"] = "sera", ["wielder"] = "sera", ["weapon"] = true });
        static void Remove(JObject state, string key) =>
            state["deck"] = new JArray(((JArray)state["deck"]!).Where(card => card["key"]!.Value<string>() != key));
        static JObject Listing(JObject state, string kind, int index) => (JObject)state["pending"]![kind]![index]!;
        static JObject Shown(MemoryForgeServerPracticeClient view) => view.Snapshot!.Run!.StateJObject();
        static bool Refused(Task<bool> task) => task.IsCompleted && !task.Result;

        static void Sent(Seen seen, string kind, int expectedVersion, JObject args)
        {
            Assert.That(seen.Path, Is.EqualTo(PracticeCommandOutbox.CommandRoute(RunA)));
            var body = JObject.Parse(seen.Text);
            Assert.That(body["kind"]!.Value<string>(), Is.EqualTo(kind));
            Assert.That(body["expectedVersion"]!.Value<int>(), Is.EqualTo(expectedVersion));
            Assert.That(JToken.DeepEquals(body["args"], args), Is.True, "args " + body["args"]!.ToString(Formatting.None));
        }

        static IEnumerator Settled(MemoryForgeServerPracticeClient view) =>
            Until(() => !view.Busy && view.SessionState?.Snapshot != null && view.SessionState.CanMutate, "the view's refresh settles");
        static IEnumerator Until(Func<bool> done, string what)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!done())
            {
                Assert.That(watch.Elapsed, Is.LessThan(Bound), what + " within the bound");
                yield return null;
            }
        }
#endif

        // ---- fixture server and protocol fixtures ----

        sealed class World
        {
            public string AccountId = Account;
            public string? RunId = RunA;
            public int Version = 3, MemoryVersion = 2, Finished, BattleV = 7, Turn = 2;
            public int? RunVersionOverride;
            public string Phase = "BATTLE", Node = "z1-f2-n0";
            public bool InBattle = true;
            public Reply? AccountFailure;
            /// <summary>Public run-state members that replace or add to the default state (the node-service fixtures); null keeps the default.</summary>
            public JObject? Extra;
        }

        Reply Serve(Seen request)
        {
            if (request.Authorization != "Bearer " + Credential && request.Authorization != "Bearer " + RotatedCredential) return Error(401, "UNAUTHORIZED");
            if (request.Method == "GET" && request.Path == AccountRoute) return world.AccountFailure ?? Reply.Json(200, AccountJson(world));
            if (request.Method == "GET")
                return world.RunId != null && request.Path == "/dev/practice/runs/" + world.RunId
                    ? Reply.Json(200, RunJson(world, world.RunVersionOverride ?? world.Version)) : Error(404, "RUN_NOT_FOUND");
            return post(request);
        }

        Reply Failure(string mode)
        {
            byte[] receipt = Utf8(RunJson(world, 4, Events()));
            switch (mode)
            {
                case "drop": return new Reply(200, receipt) { Drop = true };
                case "truncate": return new Reply(200, receipt) { DeclaredLength = receipt.Length + 16 };
                case "no-length": return new Reply(200, receipt) { OmitLength = true };
                case "text-plain": return new Reply(200, receipt) { ContentType = "text/plain; charset=utf-8" };
                case "503-commit-unknown": return Error(503, "STORE_COMMIT_UNKNOWN");
                case "503-busy": return Error(503, "ACCOUNT_BUSY");
                case "500-corrupt": return Error(500, "STORE_CORRUPT");
                case "401": return Error(401, "UNAUTHORIZED");
                case "409-idempotency": return Error(409, "IDEMPOTENCY_CONFLICT");
                case "409-binding": return Error(409, "STORE_IMPLEMENTATION_MISMATCH");
                case "400-unsafe-code": return Reply.Json(400, new JObject { ["code"] = "not a code", ["message"] = "fixture" });
                case "200-wrong-version": return Reply.Json(200, RunJson(world, 9, Events()));
                case "200-other-run":
                {
                    var other = RunJson(world, 4, Events());
                    other["runId"] = RunB;
                    other["state"]!["runId"] = RunB;
                    return Reply.Json(200, other);
                }
                default:
                {
                    var hidden = RunJson(world, 4, Events());
                    hidden["state"]!["battle"]!["zones"] = new JObject { ["draw"] = new JArray("hidden-uid") };
                    return Reply.Json(200, hidden);
                }
            }
        }

        static string Route(string route) => route == "create" ? PracticeCommandOutbox.CreateRunRoute
            : route == "commit" ? PracticeCommandOutbox.MemoryCommitRoute(RunA) : PracticeCommandOutbox.CommandRoute(RunA);
        static string Body(string route, string requestId, int expectedVersion)
        {
            string version = expectedVersion.ToString(CultureInfo.InvariantCulture);
            if (route == "create") return "{\"requestId\":\"" + requestId + "\",\"useMemory\":true}";
            if (route == "commit")
                return "{\"requestId\":\"" + requestId + "\",\"expectedVersion\":" + version + ",\"expectedMemoryVersion\":2,\"selectedChangeIds\":[\"chg-a\"]}";
            return "{\"requestId\":\"" + requestId + "\",\"expectedVersion\":" + version + ",\"kind\":\"end\",\"args\":{}}";
        }

        // GET /dev/practice/account (PublicView.Account).
        static JObject AccountJson(World w) => new JObject
        {
            ["accountId"] = w.AccountId,
            ["memory"] = new JObject
            {
                ["version"] = w.MemoryVersion,
                ["memory"] = new JObject { ["sera"] = new JArray(new JObject { ["key"] = "k1", ["id"] = "strike", ["plus"] = 0 }) },
                ["history"] = new JArray()
            },
            ["run"] = w.RunId == null ? (JToken)JValue.CreateNull() : new JObject { ["runId"] = w.RunId, ["version"] = w.Version, ["phase"] = w.Phase },
            ["finishedRuns"] = w.Finished
        };

        // A run reply (GET run / create / command receipt): {runId, version, phase, memoryVersion, events?, state} with PublicView.Run's state.
        static JObject RunJson(World w, int version, JArray? events = null)
        {
            var state = new JObject
            {
                ["runId"] = w.RunId, ["version"] = version, ["phase"] = w.Phase, ["memoryPolicy"] = "v2", ["memoryVersion"] = 1, ["length"] = "short",
                ["zones"] = new JArray(1, 2, 3), ["zoneIndex"] = 0, ["zone"] = 1, ["floor"] = 2, ["position"] = w.Node, ["hp"] = 40, ["maxHp"] = 50,
                ["coins"] = 12, ["party"] = new JArray(new JObject { ["charId"] = "sera", ["res"] = 0 }),
                ["pending"] = new JObject { ["type"] = "reward", ["coins"] = 5, ["cards"] = new JArray("c-a", "c-b") },
                ["log"] = new JArray("The seed of doubt grows."), ["futurePublic"] = new JObject { ["note"] = "kept" },
                ["battle"] = w.InBattle ? (JToken)Battle(w) : JValue.CreateNull()
            };
            if (w.Extra != null) foreach (var member in w.Extra.Properties()) state[member.Name] = member.Value.DeepClone();
            var reply = new JObject { ["runId"] = w.RunId, ["version"] = version, ["phase"] = w.Phase, ["memoryVersion"] = w.MemoryVersion };
            if (events != null) reply["events"] = events;
            reply["state"] = state;
            return reply;
        }

        static JObject Battle(World w) => new JObject
        {
            ["v"] = w.BattleV, ["phase"] = "PLAYER", ["turn"] = w.Turn, ["hp"] = 40, ["maxHp"] = 50, ["ap"] = 3,
            ["piles"] = new JObject { ["draw"] = 12, ["hand"] = 2, ["discard"] = 3, ["exhaust"] = 0 },
            ["hand"] = new JArray(
                new JObject { ["uid"] = "h1", ["id"] = "strike", ["token"] = false, ["modes"] = new JArray("now"), ["costs"] = new JObject { ["now"] = 1 },
                    ["blocked"] = new JObject { ["now"] = JValue.CreateNull() } },
                new JObject { ["uid"] = "h2", ["id"] = "pull", ["token"] = true, ["modes"] = new JArray("now", "next"), ["costs"] = new JObject { ["now"] = 1, ["next"] = 1 },
                    ["blocked"] = new JObject { ["now"] = JValue.CreateNull(), ["next"] = "PULL_DESTINATION_FULL" } }),
            ["reservations"] = new JArray(new JObject { ["uid"] = "r1", ["id"] = "guard", ["due"] = 3, ["order"] = 1, ["kind"] = "next" }),
            ["actors"] = new JArray(new JObject { ["id"] = "sera", ["name"] = "Sera", ["res"] = 0 }),
            ["enemies"] = new JArray(new JObject { ["uid"] = "e1", ["id"] = "slime", ["hp"] = 20, ["intent"] = new JObject { ["t"] = "atk", ["d"] = 5 }, ["next"] = JValue.CreateNull() })
        };

        static JArray Events() => new JArray(new JObject { ["e"] = "play", ["uid"] = "h2", ["ap"] = 1 });

        // POST battle-query reply (PublicView.BattleQuery): the run/context it belongs to and the echoed request.
        static JObject QueryJson(World w, string uid, string mode, string? target, IEnumerable<string> picks) => new JObject
        {
            ["runId"] = w.RunId, ["version"] = w.Version, ["phase"] = w.Phase,
            ["context"] = new JObject { ["node"] = w.Node, ["turn"] = w.Turn, ["battleVersion"] = w.BattleV, ["battlePhase"] = "PLAYER" },
            ["source"] = new JObject { ["uid"] = uid, ["id"] = "pull", ["name"] = "Pull", ["text"] = "Pull a reserved card.", ["mode"] = mode,
                ["target"] = target == null ? (JToken)JValue.CreateNull() : target, ["picks"] = new JArray(picks.Cast<object>().ToArray()) },
            ["slots"] = new JObject { ["next"] = new JObject { ["cap"] = 1, ["used"] = 1 }, ["after"] = new JObject { ["cap"] = 1, ["used"] = 0 }, ["sealedUntil"] = 0 },
            ["forecast"] = new JObject { ["error"] = JValue.CreateNull(), ["mode"] = mode, ["cost"] = 1, ["due"] = JValue.CreateNull(), ["hidden"] = false },
            ["choices"] = new JObject { ["error"] = JValue.CreateNull(), ["requests"] = new JArray(), ["accepted"] = 0, ["index"] = 0, ["left"] = 0,
                ["blocked"] = JValue.CreateNull(), ["candidates"] = new JArray() }
        };

        // P1-SERVER-MEMORY-PREVIEW: a two-member party on its terminal save screen, out of battle (PublicView's save pending and the v2
        // memory changes of §8.12). Change ids, keys, costs and capacities are protocol data, not content or Core's memory rules.
        const string ChangeAddK9 = "delta:sera:k9:add", ChangeUpgradeK1 = "delta:sera:k1:upgrade";
        static readonly string[] NoSelection = Array.Empty<string>();

        void MemorySaveWorld(bool defeat)
        {
            world.Phase = defeat ? "DEFEAT" : "VICTORY";
            world.InBattle = false;
            world.Extra = new JObject
            {
                ["party"] = new JArray(new JObject { ["charId"] = "sera", ["res"] = 0 }, new JObject { ["charId"] = "mira", ["res"] = 0 }),
                ["pending"] = new JObject { ["type"] = "save", ["defeat"] = defeat },
                ["memory"] = new JObject
                {
                    ["sera"] = new JObject
                    {
                        ["capacity"] = 6, ["usedBefore"] = 2, ["before"] = new JArray(), ["after"] = new JArray(),
                        ["changes"] = new JArray(
                            new JObject { ["id"] = ChangeAddK9, ["cid"] = "sera", ["key"] = "k9", ["kind"] = "add", ["label"] = "fixture add" },
                            new JObject { ["id"] = ChangeUpgradeK1, ["cid"] = "sera", ["key"] = "k1", ["kind"] = "upgrade", ["label"] = "fixture upgrade", ["value"] = true })
                    },
                    ["mira"] = new JObject { ["capacity"] = 5, ["usedBefore"] = 0, ["before"] = new JArray(), ["after"] = new JArray(), ["changes"] = new JArray() }
                }
            };
        }

        // POST memory-preview reply (PublicView.Preview): the full resulting deck of every party member, each deck's used cost, the
        // capacities and Core's verdict. No selection answers the saved decks; a selected add appends its card, an upgrade marks it.
        static JObject MemoryPreviewJson(World w, IEnumerable<string> selected, string? error = null)
        {
            var sera = new JArray(
                new JObject { ["key"] = "k1", ["id"] = "strike" },
                new JObject { ["key"] = "k2", ["id"] = "guard_plus", ["from"] = "guard", ["plus"] = true,
                    ["mods"] = new JObject { ["c"] = "p-b", ["e"] = JValue.CreateNull() } },
                new JObject { ["key"] = "k-common", ["id"] = "focus", ["assigned"] = "sera" });
            int cost = 2;
            foreach (string id in selected)
            {
                string[] parts = id.Split(':');
                if (parts.Length != 4) continue;
                if (parts[3] == "add") { sera.Add(new JObject { ["key"] = parts[2], ["id"] = "pull" }); cost += 2; }
                if (parts[3] == "upgrade") { sera.First(card => card["key"]!.Value<string>() == parts[2])["plus"] = true; cost += 1; }
            }
            return new JObject
            {
                ["runId"] = w.RunId, ["version"] = w.Version,
                ["preview"] = new JObject
                {
                    ["memory"] = new JObject { ["sera"] = sera, ["mira"] = new JArray(new JObject { ["key"] = "m1", ["id"] = "spark", ["mods"] = JValue.CreateNull() }) },
                    ["used"] = new JObject { ["sera"] = cost, ["mira"] = 0 },
                    ["error"] = error == null ? (JToken)JValue.CreateNull() : error,
                    ["capacities"] = new JObject { ["sera"] = 6, ["mira"] = 5 }
                }
            };
        }

        static string[] SelectedIds(Seen request) => JObject.Parse(request.Text)["selectedChangeIds"]!.Values<string>().Select(id => id!).ToArray();
        static string[] PreviewKeys(PracticeMemoryPreviewView view, string cid) =>
            view.ToJObject()["preview"]!["memory"]![cid]!.Select(card => card["key"]!.Value<string>()!).ToArray();

        static Reply Error(int status, string code) => Reply.Json(status, new JObject { ["code"] = code, ["message"] = "fixture" });
        static byte[] Utf8(JToken json) => Utf8Text(json.ToString(Formatting.None));
        static byte[] Utf8Text(string text) => StrictUtf8.GetBytes(text);

        static T Run<T>(Func<Task<T>> call) => Wait(Task.Run(call));
        static T Wait<T>(Task<T> task)
        {
            Assert.That(task.Wait(Bound), Is.True, "the call did not finish within the bound");
            return task.Result;
        }

        sealed class Seen
        {
            public Seen(string method, string path, string? authorization, string? contentType, byte[] body)
            {
                Method = method; Path = path; Authorization = authorization; ContentType = contentType; Body = body;
            }
            public string Method { get; }
            public string Path { get; }
            public string? Authorization { get; }
            public string? ContentType { get; }
            public byte[] Body { get; }
            public string Text => StrictUtf8.GetString(Body);
        }

        sealed class Reply
        {
            public Reply(int status, byte[] body) { Status = status; Body = body; }
            public static Reply Json(int status, JToken json) => new Reply(status, Utf8(json));
            public int Status { get; }
            public byte[] Body { get; }
            public string ContentType = "application/json; charset=utf-8";
            public long? DeclaredLength;
            public bool OmitLength, Drop;
            /// <summary>The reply is written only once this completes (bounded).</summary>
            public Task? Hold;
        }

        /// <summary>A test-owned HTTP/1.1 responder on 127.0.0.1 (ephemeral port): reads one request per connection, records it, and writes
        /// the handler's reply with Connection: close (or drops/truncates it as told).</summary>
        sealed class FixtureHost : IDisposable
        {
            readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            readonly Func<Seen, Reply> handler;
            readonly List<Seen> seen = new List<Seen>();
            readonly List<Task> connections = new List<Task>();
            readonly Task accepting;

            public FixtureHost(Func<Seen, Reply> handler)
            {
                this.handler = handler;
                listener.Start();
                Endpoint = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
                accepting = Task.Run(Accept);
            }

            public string Endpoint { get; }
            public Exception? Fault { get; private set; }
            public Seen[] Seen { get { lock (seen) return seen.ToArray(); } }
            public Seen[] Posts => Seen.Where(s => s.Method == "POST").ToArray();

            async Task Accept()
            {
                while (true)
                {
                    TcpClient client;
                    try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                    catch (Exception error) when (error is ObjectDisposedException || error is SocketException || error is InvalidOperationException) { return; }
                    lock (connections) connections.Add(Task.Run(() => Serve(client)));
                }
            }

            void Serve(TcpClient client)
            {
                using (client)
                {
                    try
                    {
                        client.ReceiveTimeout = client.SendTimeout = (int)Bound.TotalMilliseconds;
                        var stream = client.GetStream();
                        var request = Read(stream);
                        if (request == null) return;
                        lock (seen) seen.Add(request);
                        Reply reply;
                        try { reply = handler(request); }
                        catch (Exception error) { Fault = error; return; }
                        if (reply.Hold != null && !reply.Hold.Wait(Bound)) return;
                        if (reply.Drop) return;
                        var head = new StringBuilder("HTTP/1.1 ").Append(reply.Status.ToString(CultureInfo.InvariantCulture)).Append(" Fixture\r\n");
                        head.Append("Content-Type: ").Append(reply.ContentType).Append("\r\n");
                        if (!reply.OmitLength) head.Append("Content-Length: ").Append((reply.DeclaredLength ?? reply.Body.Length).ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                        head.Append("Connection: close\r\n\r\n");
                        byte[] bytes = Encoding.ASCII.GetBytes(head.ToString());
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Write(reply.Body, 0, reply.Body.Length);
                        stream.Flush();
                        client.Client.Shutdown(SocketShutdown.Send);
                    }
                    catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException) { }
                }
            }

            static Seen? Read(Stream stream)
            {
                var head = new MemoryStream();
                int matched = 0;
                while (matched < 4)
                {
                    int b = stream.ReadByte();
                    if (b < 0 || head.Length > 65536) return null;
                    head.WriteByte((byte)b);
                    matched = b == (matched % 2 == 0 ? '\r' : '\n') ? matched + 1 : b == '\r' ? 1 : 0;
                }
                string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                string[] start = lines[0].Split(' ');
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < lines.Length; i++)
                {
                    int colon = lines[i].IndexOf(':');
                    if (colon > 0) headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
                }
                int length = headers.TryGetValue("Content-Length", out string? declared) ? int.Parse(declared, CultureInfo.InvariantCulture) : 0;
                var body = new byte[length];
                for (int total = 0; total < length;)
                {
                    int read = stream.Read(body, total, length - total);
                    if (read == 0) return null;
                    total += read;
                }
                headers.TryGetValue("Authorization", out string? authorization);
                headers.TryGetValue("Content-Type", out string? contentType);
                return new Seen(start[0], start[1], authorization, contentType, body);
            }

            public void Dispose()
            {
                listener.Stop();
                accepting.Wait(Bound);
                Task[] open;
                lock (connections) open = connections.ToArray();
                Task.WaitAll(open, Bound);
            }
        }
    }
}
