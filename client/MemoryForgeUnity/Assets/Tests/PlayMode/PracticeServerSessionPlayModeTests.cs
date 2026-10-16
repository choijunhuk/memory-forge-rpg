// P1-PRACTICE-TRANSPORT §14.7/§17.2/§17.4: PROTOCOL FIXTURE checks of PracticeServerSession and PracticePublicSnapshot. BCL + NUnit only
// (no UnityEngine, TestTools, reflection or source hook), so the same assertions run on .NET and in Unity's Mono. The "server" is a tiny
// test-owned loopback HTTP/1.1 listener (TcpListener on 127.0.0.1, ephemeral port, Connection: close) that answers with fixed PUBLIC JSON
// written here from the documented protocol (PublicView/Program shapes) and with the server's fixed error bodies; it can hold a reply on a
// test-released gate, drop the connection after reading the request, truncate a body or omit its length. Records live in a fresh temp
// folder. These are NOT Core/server acceptance: no real Run, receipt store, restart or fault proxy is involved (Root runs the actual
// unchanged host separately). Every wait is bounded; held replies are released in finally.
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

namespace MemoryForge.Client.Tests
{
    public class PracticeServerSessionPlayModeTests
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
