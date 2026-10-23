// P1-PRACTICE-COMMAND-OUTBOX §14.7/§17.2/§17.4: synchronous checks of PracticeCommandOutbox, the Development practice client's one durable
// pending request, over a fresh test-owned temp folder and real files. BCL + NUnit only (no UnityEngine, TestTools, reflection or product
// hook), so the same assertions run on .NET and in Unity's Mono. Failures are real file situations: a lock file held with FileShare.None
// in this process, a directory occupying the record or lock path, a removed folder, and the test's OWN folder/record made read-only or
// unreadable with libc chmod (restored in finally; the tests must run as a normal user, not root). Damaged records are written as raw
// bytes; well-formed edits carry a recomputed checksum from an independent encoder of the documented format that is first proven
// byte-identical to the store's own file. Separate instances race on threads inside this process. Not shown here: another OS process
// (Root's flock matrix), a client process restart, HTTP/server receipts, power loss, Android/IL2CPP or file-mode privacy.
// EscapedUnpairedSurrogatesAreRefused locks an independent review finding as a RED regression until the source is fixed.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Code = MemoryForge.Client.PracticeOutboxException;
using Outbox = MemoryForge.Client.PracticeCommandOutbox;

namespace MemoryForge.Client.Tests
{
    public class PracticeCommandOutboxPlayModeTests
    {
        const string Endpoint = "http://127.0.0.1:5173", Account = "dev-local", FileName = "practice-outbox.json";
        const string RunId = "pr-0123456789abcdef0123456789abcdef";
        // Octal 0700, 0500, 0600 and 0000 for the test's own folder/files.
        const int OwnerAll = 0x1C0, OwnerReadExecute = 0x140, OwnerReadWrite = 0x180, NoAccess = 0;
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        string folder = null!;

        [DllImport("libc", SetLastError = true)]
        static extern int chmod(string path, int mode);
        [DllImport("libc")]
        static extern uint geteuid();

        [SetUp]
        public void CreateOwnedFolder()
        {
            folder = Path.Combine(Path.GetTempPath(), "mf-outbox-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            Mode(folder, OwnerAll);
        }

        [TearDown]
        public void RemoveOwnedFolder()
        {
            if (folder == null || !Directory.Exists(folder)) return;
            // A failed assertion may have left the test's own folder or files without permissions.
            chmod(folder, OwnerAll);
            foreach (string path in Directory.GetFileSystemEntries(folder, "*", SearchOption.AllDirectories))
                chmod(path, Directory.Exists(path) ? OwnerAll : OwnerReadWrite);
            Directory.Delete(folder, true);
        }

        string Record => Path.Combine(folder, FileName);
        string LockFile => Record + Outbox.LockSuffix;
        static string CommandRoute => Outbox.CommandRoute(RunId);
        Outbox Open(string endpoint = Endpoint, string account = Account) => new Outbox(Record, endpoint, account);

        static string Play(string requestId, int expectedVersion, string picks = "[\"c2\",\"c1\"]") =>
            "{\"requestId\":\"" + requestId + "\",\"expectedVersion\":" + expectedVersion.ToString(CultureInfo.InvariantCulture) +
            ",\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"picks\":" + picks + "}}";
        static string NewRun(string requestId) => "{\"requestId\":\"" + requestId + "\",\"useMemory\":true}";
        static string Save(string requestId, int expectedVersion, int memoryVersion) =>
            "{\"requestId\":\"" + requestId + "\",\"expectedVersion\":" + expectedVersion.ToString(CultureInfo.InvariantCulture) +
            ",\"expectedMemoryVersion\":" + memoryVersion.ToString(CultureInfo.InvariantCulture) + ",\"selectedChangeIds\":[\"chg-b\",\"chg-a\"]}";

        [Test]
        public void ExactPrettyUnicodeBodySurvivesInNewObjectsAndCopiesAreDetached()
        {
            // Member order, pretty whitespace, escapes, 2-/3-/4-byte UTF-8, U+FFFD and the ordered picks, byte for byte.
            string body = "{\n  \"requestId\" : \"exact-body-0001\",\r\n  \"expectedVersion\" : 7,\n\t\"kind\":\"play\",\n  \"args\" : {\"uid\":\"c9\", " +
                "\"mode\":\"next\", \"target\":\"\u00e9\uc801\ud83d\ude00\ufffd \\\"q\\\" \\\\ \\/ \\u0041\", \"picks\" : [ \"c3\", \"c1\", \"c2\" ] }\n}\n";
            byte[] utf8 = StrictUtf8.GetBytes(body);
            var staged = Open().Stage(CommandRoute, body);
            Assert.That(staged.Body, Is.EqualTo(body));
            Assert.That(staged.BodyUtf8(), Is.EqualTo(utf8));
            Assert.That(staged.RequestId, Is.EqualTo("exact-body-0001"));
            Assert.That(staged.ExpectedVersion, Is.EqualTo(7));
            Assert.That(staged.Route, Is.EqualTo(CommandRoute));
            Assert.That(staged.Method, Is.EqualTo("POST"));
            Assert.That(staged.Endpoint, Is.EqualTo(Endpoint));
            Assert.That(staged.Account, Is.EqualTo(Account));
            byte[] record = File.ReadAllBytes(Record);
            // The file is exactly the documented canonical encoding (an independent encoder reproduces it).
            Assert.That(record, Is.EqualTo(Envelope(Json(Fields(staged)))));

            var reloaded = Open().Load();
            AssertSame(staged, reloaded);
            Assert.That(reloaded!.BodyUtf8(), Is.EqualTo(utf8));
            byte[] copy = reloaded.BodyUtf8();
            copy[0] = (byte)'[';
            copy[copy.Length - 1] = 0;
            Assert.That(reloaded.BodyUtf8(), Is.EqualTo(utf8), "a returned byte copy is detached");
            Assert.That(Open().Load()!.BodyUtf8(), Is.EqualTo(utf8));
            Assert.That(File.ReadAllBytes(Record), Is.EqualTo(record));
            Open().Confirm(reloaded);

            var create = Open().Stage(Outbox.CreateRunRoute, NewRun("new-run-0001"));
            Assert.That(create.ExpectedVersion, Is.Null);
            AssertSame(create, Open().Load());
            Open().Confirm(create);
            var save = Open().Stage(Outbox.MemoryCommitRoute(RunId), Save("save-commit-01", 12, 3));
            Assert.That(save.ExpectedVersion, Is.EqualTo(12));
            AssertSame(save, Open().Load());
            Open().Confirm(save);
            Assert.That(Open().Load(), Is.Null);
            Assert.That(File.Exists(Record), Is.False);
        }

        [Test]
        public void EqualRestageKeepsTheFileAndConflictingRequestsAreRefusedWithTheOriginalBytes()
        {
            string body = Play("pending-00001", 4, "[\"c1\",\"c2\"]");
            var pending = Open().Stage(CommandRoute, body);
            byte[] bytes = File.ReadAllBytes(Record);
            AssertSame(pending, Open().Stage(CommandRoute, body));
            AssertSame(pending, Open().Stage(CommandRoute, new string(body.ToCharArray())));
            Assert.That(File.ReadAllBytes(Record), Is.EqualTo(bytes), "an equal restage writes nothing");

            // The same requestId with reordered picks, another version, one more whitespace byte or another run is another request.
            Refused(() => Open().Stage(CommandRoute, Play("pending-00001", 4, "[\"c2\",\"c1\"]")), Code.RequestIdReused);
            Refused(() => Open().Stage(CommandRoute, Play("pending-00001", 5, "[\"c1\",\"c2\"]")), Code.RequestIdReused);
            Refused(() => Open().Stage(CommandRoute, body + " "), Code.RequestIdReused);
            Refused(() => Open().Stage(Outbox.CommandRoute("pr-" + new string('f', 32)), body), Code.RequestIdReused);
            Refused(() => Open().Stage(CommandRoute, Play("pending-00002", 4)), Code.PendingOther);
            Refused(() => Open().Stage(Outbox.CreateRunRoute, NewRun("new-run-0002")), Code.PendingOther);
            Refused(() => Open().Stage(Outbox.MemoryCommitRoute(RunId), Save("save-commit-02", 4, 0)), Code.PendingOther);
            Assert.That(File.ReadAllBytes(Record), Is.EqualTo(bytes));
            NoTemps();
            AssertSame(pending, Open().Load());
        }

        [Test]
        public void AnotherEndpointOrAccountNeverSeesReplacesOrClearsTheRecord()
        {
            var pending = Open().Stage(CommandRoute, Play("binding-0001", 2));
            byte[] bytes = File.ReadAllBytes(Record);
            foreach (var other in new[] { Open(endpoint: "http://127.0.0.1:5174"), Open(account: "dev-other") })
            {
                Refused(() => other.Load(), Code.BindingMismatch);
                Refused(() => other.Stage(CommandRoute, pending.Body), Code.BindingMismatch);
                Refused(() => other.Stage(CommandRoute, Play("binding-0002", 2)), Code.BindingMismatch);
                Refused(() => other.Confirm(pending), Code.BindingMismatch);
                Assert.That(File.ReadAllBytes(Record), Is.EqualTo(bytes));
            }
            NoTemps();
            AssertSame(pending, Open().Load());
        }

        [Test]
        public void AStaleConfirmationNeverClearsALaterDifferentRequest()
        {
            Outbox a = Open(), b = Open();
            string first = Play("stale-0001", 0), second = Play("stale-0002", 1);
            var r1 = a.Stage(CommandRoute, first);
            var seen = b.Load()!;
            b.Confirm(seen);
            Assert.That(File.Exists(Record), Is.False);
            Refused(() => a.Confirm(r1), Code.NotCurrent);

            var r2 = b.Stage(CommandRoute, second);
            byte[] bytes = File.ReadAllBytes(Record);
            Refused(() => a.Confirm(r1), Code.NotCurrent);
            Refused(() => b.Confirm(seen), Code.NotCurrent);
            Assert.That(File.ReadAllBytes(Record), Is.EqualTo(bytes), "a stale confirmation kept the later request whole");
            AssertSame(r2, a.Load());
            a.Confirm(a.Load()!);

            // An identical re-stage of an already confirmed request is the same logical server request (same requestId and bytes, so
            // the same server receipt): the original identical expectation may confirm it, and no new stage identity exists.
            var again = b.Stage(CommandRoute, first);
            AssertSame(r1, again);
            a.Confirm(r1);
            Assert.That(b.Load(), Is.Null);
        }

        [Test]
        public void TruncatedBitFlippedOrPaddedRecordsAreRefusedAndKeptWhole()
        {
            var pending = Open().Stage(CommandRoute, Play("damaged-0001", 2));
            byte[] original = File.ReadAllBytes(Record);
            for (int length = 0; length < original.Length; length++)
                RefusedAndKept(original.Take(length).ToArray(), pending, "truncated to " + length, Code.RecordCorrupt);
            foreach (byte mask in new byte[] { 0x01, 0x20, 0x80 })
                for (int at = 0; at < original.Length; at++)
                {
                    byte[] flipped = (byte[])original.Clone();
                    flipped[at] ^= mask;
                    RefusedAndKept(flipped, pending, "byte " + at + " xor " + mask, Code.RecordCorrupt, Code.RecordUnsupported);
                }
            foreach (string tail in new[] { " ", "\n", "\u0000", "x", "{}" })
                RefusedAndKept(original.Concat(StrictUtf8.GetBytes(tail)).ToArray(), pending, "padded", Code.RecordCorrupt);
            RefusedAndKept(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(original).ToArray(), pending, "byte order mark", Code.RecordCorrupt);
            RefusedAndKept(StrictUtf8.GetBytes("garbage"), pending, "garbage", Code.RecordCorrupt);
            File.WriteAllBytes(Record, original);
            AssertSame(pending, Open().Load());
        }

        [Test]
        public void WellFormedRecordsWithUnknownFieldsFormsOrBindingsAreRefusedAndKept()
        {
            var pending = Open().Stage(CommandRoute, Play("record-0001", 2));
            byte[] original = File.ReadAllBytes(Record);
            var fields = Fields(pending);
            string inner = Json(fields);
            Assert.That(Envelope(inner), Is.EqualTo(original), "the independent encoder must reproduce the store's file exactly");

            var cases = new List<(string Name, byte[] Bytes, string Expected)>
            {
                ("unknown inner field", Envelope(Json(fields.Concat(new[] { (Name: "stageId", Value: "x") }))), Code.RecordCorrupt),
                ("missing inner field", Envelope(Json(fields.Where(f => f.Name != "method"))), Code.RecordCorrupt),
                ("duplicate inner field", Envelope(Json(fields.Concat(new[] { (Name: "route", Value: pending.Route) }))), Code.RecordCorrupt),
                ("reordered inner fields", Envelope(Json(fields.Skip(1).Concat(fields.Take(1)))), Code.RecordCorrupt),
                ("non-canonical inner spacing", Envelope(inner.Replace("\",\"account\":", "\", \"account\":")), Code.RecordCorrupt),
                ("non-canonical inner escape", Envelope(inner.Replace("\"POST\"", "\"\\u0050OST\"")), Code.RecordCorrupt),
                ("unknown outer field", Outer(("schema", Outbox.Schema), ("sha256", Sha256(inner)), ("record", inner), ("note", "x")), Code.RecordCorrupt),
                ("missing checksum", Outer(("schema", Outbox.Schema), ("record", inner)), Code.RecordCorrupt),
                ("reordered outer fields", Outer(("sha256", Sha256(inner)), ("schema", Outbox.Schema), ("record", inner)), Code.RecordCorrupt),
                ("uppercase checksum", Outer(("schema", Outbox.Schema), ("sha256", Sha256(inner).ToUpperInvariant()), ("record", inner)), Code.RecordCorrupt),
                ("checksum of other text", Outer(("schema", Outbox.Schema), ("sha256", Sha256(inner + " ")), ("record", inner)), Code.RecordCorrupt),
                ("outer whitespace", StrictUtf8.GetBytes(" " + StrictUtf8.GetString(original)), Code.RecordCorrupt),
                ("record as an object", StrictUtf8.GetBytes("{\"schema\":" + Quote(Outbox.Schema) + ",\"sha256\":" + Quote(Sha256(inner)) + ",\"record\":" + inner + "}"), Code.RecordCorrupt),
                ("unknown schema", Envelope(inner, "mf-dev-practice-outbox-2"), Code.RecordUnsupported),
                ("invalid account", Envelope(Json(With(fields, "account", "Dev-Local"))), Code.RecordCorrupt),
                ("invalid endpoint host", Envelope(Json(With(fields, "endpoint", "http://localhost:5173"))), Code.RecordCorrupt),
                ("endpoint with a path", Envelope(Json(With(fields, "endpoint", "http://127.0.0.1:5173/"))), Code.RecordCorrupt),
                ("other method", Envelope(Json(With(fields, "method", "GET"))), Code.RecordCorrupt),
                ("query route", Envelope(Json(With(fields, "route", Outbox.CreateRunRoute + "/" + RunId + "/battle-query"))), Code.RecordCorrupt),
                ("route that does not fit the body", Envelope(Json(With(fields, "route", Outbox.MemoryCommitRoute(RunId)))), Code.RecordCorrupt),
                ("new-run route with a version body", Envelope(Json(With(fields, "route", Outbox.CreateRunRoute))), Code.RecordCorrupt),
                ("requestId that is not the body's", Envelope(Json(With(fields, "requestId", "record-0002"))), Code.RecordCorrupt),
                ("body with a duplicate member", Envelope(Json(With(fields, "body", "{\"requestId\":\"record-0001\",\"expectedVersion\":2,\"expectedVersion\":2}"))), Code.RecordCorrupt),
                ("body version out of range", Envelope(Json(With(fields, "body", "{\"requestId\":\"record-0001\",\"expectedVersion\":2147483648}"))), Code.RecordCorrupt),
                ("body with a trailing comma", Envelope(Json(With(fields, "body", "{\"requestId\":\"record-0001\",\"expectedVersion\":2,}"))), Code.RecordCorrupt),
                ("empty body", Envelope(Json(With(fields, "body", ""))), Code.RecordCorrupt),
                ("other valid account", Envelope(Json(With(fields, "account", "dev-other"))), Code.BindingMismatch),
                ("other valid endpoint", Envelope(Json(With(fields, "endpoint", "http://127.0.0.1:5174"))), Code.BindingMismatch),
            };
            foreach (var item in cases) RefusedAndKept(item.Bytes, pending, item.Name, item.Expected);

            // A foreign but valid binding is refused only for this binding: its own endpoint/account loads it.
            File.WriteAllBytes(Record, Envelope(Json(With(fields, "account", "dev-other"))));
            Assert.That(Open(account: "dev-other").Load()!.Account, Is.EqualTo("dev-other"));
            File.WriteAllBytes(Record, Envelope(Json(With(fields, "endpoint", "http://127.0.0.1:5174"))));
            Assert.That(Open(endpoint: "http://127.0.0.1:5174").Load()!.Endpoint, Is.EqualTo("http://127.0.0.1:5174"));
            // Restoring the original bytes restores the original request: none of the refusals changed anything else.
            File.WriteAllBytes(Record, original);
            AssertSame(pending, Open().Load());
        }

        [Test]
        public void AStrayTempFileIsNeverPromotedOrTouched()
        {
            var pending = Open().Stage(CommandRoute, Play("sidecar-0001", 1));
            byte[] complete = File.ReadAllBytes(Record);
            string stray = Path.Combine(folder, "." + FileName + "." + Guid.NewGuid().ToString("N") + Outbox.TempSuffix);
            File.WriteAllBytes(stray, complete);
            AssertSame(pending, Open().Load());
            Open().Confirm(pending);
            Assert.That(Open().Load(), Is.Null, "a complete temp file is still not a pending request");
            var next = Open().Stage(Outbox.CreateRunRoute, NewRun("sidecar-0002"));
            AssertSame(next, Open().Load());
            Assert.That(File.ReadAllBytes(stray), Is.EqualTo(complete));
            Assert.That(Directory.GetFiles(folder, ".*" + Outbox.TempSuffix), Is.EqualTo(new[] { stray }));
        }

        [Test]
        public void AHeldPermanentLockIsBusyImmediatelyAndUsableAfterRelease()
        {
            File.WriteAllBytes(LockFile, StrictUtf8.GetBytes("keep"));
            var outbox = Open();
            string body = Play("busy-00001", 3);
            using (new FileStream(LockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                Refused(() => outbox.Load(), Code.Busy);
                Refused(() => outbox.Stage(CommandRoute, body), Code.Busy);
                Refused(() => Open().Stage(Outbox.CreateRunRoute, NewRun("busy-00002")), Code.Busy);
                Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "busy is reported at once, never after a wait");
                Assert.That(File.Exists(Record), Is.False);
                NoTemps();
            }
            var pending = outbox.Stage(CommandRoute, body);
            byte[] bytes = File.ReadAllBytes(Record);
            using (new FileStream(LockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Refused(() => outbox.Confirm(pending), Code.Busy);
                Refused(() => Open().Load(), Code.Busy);
                Refused(() => outbox.Stage(CommandRoute, body), Code.Busy);
            }
            Assert.That(File.ReadAllBytes(Record), Is.EqualTo(bytes));
            AssertSame(pending, outbox.Load());
            outbox.Confirm(pending);
            Assert.That(File.Exists(Record), Is.False);
            Assert.That(File.ReadAllBytes(LockFile), Is.EqualTo(StrictUtf8.GetBytes("keep")), "the permanent lock file is never deleted, replaced or written");
        }

        [Test]
        public void OccupiedPathsAndAMissingFolderFailExplicitlyWithoutFalseSuccess()
        {
            Directory.CreateDirectory(LockFile);
            Refused(() => Open().Load(), Code.LockFailed);
            Refused(() => Open().Stage(CommandRoute, Play("occupied-01", 0)), Code.LockFailed);
            Assert.That(File.Exists(Record), Is.False);
            Assert.That(Directory.Exists(LockFile), Is.True);
            Directory.Delete(LockFile);

            // A pending attempt from another file of the same binding, to confirm against an occupied record path.
            var attempt = new Outbox(Path.Combine(folder, "other.json"), Endpoint, Account).Stage(CommandRoute, Play("occupied-02", 0));
            Directory.CreateDirectory(Record);
            Refused(() => Open().Load(), Code.RecordCorrupt);
            Refused(() => Open().Stage(CommandRoute, Play("occupied-02", 0)), Code.RecordCorrupt);
            Refused(() => Open().Confirm(attempt), Code.RecordCorrupt);
            Assert.That(Directory.Exists(Record), Is.True);
            NoTemps();

            string gone = Path.Combine(folder, "gone");
            Directory.CreateDirectory(gone);
            var lost = new Outbox(Path.Combine(gone, FileName), Endpoint, Account);
            Directory.Delete(gone);
            Refused(() => lost.Load(), Code.LockFailed);
            Refused(() => lost.Stage(CommandRoute, Play("occupied-03", 0)), Code.LockFailed);
            Refused(() => lost.Confirm(attempt), Code.LockFailed);
            Assert.That(Directory.Exists(gone), Is.False, "nothing recreated the missing folder");
            Refused(() => new Outbox(Path.Combine(gone, FileName), Endpoint, Account), Code.InvalidConfiguration);
        }

        [Test]
        public void ReadOnlyOrUnreadableOwnFilesFailExplicitlyAndKeepTheRecord()
        {
            Assert.That(geteuid(), Is.Not.EqualTo(0u), "permission failures need an unprivileged user; run as a normal user, not root");

            // A fresh read-only folder cannot create the permanent lock: an explicit failure, never "nothing pending".
            string fresh = Path.Combine(folder, "fresh");
            Directory.CreateDirectory(fresh);
            var freshOutbox = new Outbox(Path.Combine(fresh, FileName), Endpoint, Account);
            try
            {
                Mode(fresh, OwnerReadExecute);
                Refused(() => freshOutbox.Load(), Code.LockFailed);
                Refused(() => freshOutbox.Stage(CommandRoute, Play("readonly-0000", 0)), Code.LockFailed);
            }
            finally { Mode(fresh, OwnerAll); }
            Assert.That(Directory.GetFileSystemEntries(fresh), Is.Empty);

            // The permanent lock exists and stays usable, the folder is read-only: a valid Stage cannot publish and returns nothing.
            var outbox = Open();
            Assert.That(outbox.Load(), Is.Null);
            Assert.That(File.Exists(LockFile), Is.True);
            string body = Play("readonly-0001", 5);
            try
            {
                Mode(folder, OwnerReadExecute);
                Refused(() => outbox.Stage(CommandRoute, body), Code.WriteFailed);
                Assert.That(outbox.Load(), Is.Null);
                Assert.That(File.Exists(Record), Is.False);
            }
            finally { Mode(folder, OwnerAll); }
            Assert.That(outbox.Load(), Is.Null);
            NoTemps();

            // A pending record in a read-only folder: Confirm reports ClearFailed and the request stays pending and whole.
            var pending = outbox.Stage(CommandRoute, body);
            byte[] bytes = File.ReadAllBytes(Record);
            try
            {
                Mode(folder, OwnerReadExecute);
                Refused(() => outbox.Confirm(pending), Code.ClearFailed);
                AssertSame(pending, outbox.Load());
                AssertSame(pending, outbox.Stage(CommandRoute, body));
                Refused(() => outbox.Stage(CommandRoute, Play("readonly-0002", 6)), Code.PendingOther);
                Assert.That(File.ReadAllBytes(Record), Is.EqualTo(bytes));
            }
            finally { Mode(folder, OwnerAll); }
            Assert.That(File.ReadAllBytes(Record), Is.EqualTo(bytes));
            AssertSame(pending, outbox.Load());
            outbox.Confirm(pending);
            Assert.That(outbox.Load(), Is.Null);

            // An unreadable record: ReadFailed for every operation, never "nothing pending", kept byte for byte.
            var unreadable = outbox.Stage(Outbox.CreateRunRoute, NewRun("readonly-0003"));
            bytes = File.ReadAllBytes(Record);
            try
            {
                Mode(Record, NoAccess);
                Refused(() => outbox.Load(), Code.ReadFailed);
                Refused(() => outbox.Stage(Outbox.CreateRunRoute, NewRun("readonly-0003")), Code.ReadFailed);
                Refused(() => outbox.Stage(CommandRoute, Play("readonly-0004", 0)), Code.ReadFailed);
                Refused(() => outbox.Confirm(unreadable), Code.ReadFailed);
            }
            finally { Mode(Record, OwnerReadWrite); }
            Assert.That(File.ReadAllBytes(Record), Is.EqualTo(bytes));
            AssertSame(unreadable, outbox.Load());
            outbox.Confirm(unreadable);
            Assert.That(outbox.Load(), Is.Null);
            NoTemps();
        }

        [Test]
        public void OnlyTheThreeMutationsWithServerLimitedIdentityAreStaged()
        {
            var outbox = Open();
            string run = Outbox.CreateRunRoute + "/" + RunId;
            StageAndClear(outbox, Outbox.CreateRunRoute, NewRun("abcd-123"));
            string longest = new string('Z', 58) + "a9._:-";
            Assert.That(longest.Length, Is.EqualTo(64));
            StageAndClear(outbox, Outbox.CreateRunRoute, NewRun(longest));
            StageAndClear(outbox, CommandRoute, "{\"requestId\":\"version-zero\",\"expectedVersion\":0,\"kind\":\"end\"}");
            StageAndClear(outbox, CommandRoute, "{\"requestId\":\"version-max1\",\"expectedVersion\":2147483647,\"kind\":\"end\"}");
            StageAndClear(outbox, Outbox.MemoryCommitRoute(RunId),
                "{\"requestId\":\"commit-limit\",\"expectedVersion\":0,\"expectedMemoryVersion\":2147483647,\"selectedChangeIds\":[]}");

            // Queries, reads and anything malformed never enter the store.
            foreach (string route in new[] { run, run + "/battle-query", run + "/memory-preview", "/dev/practice/account", run + "/commands/",
                run + "/commands?retry=1", run + "/commands#x", Outbox.CreateRunRoute + "/", "/dev/practice/runs/pr-0123/commands",
                "/dev/practice/runs/PR-0123456789ABCDEF0123456789ABCDEF/commands", "/dev/practice/runs/pr-0123456789abcdef0123456789abcdeg/commands",
                "/dev/practice/runs/commands", "dev/practice/runs", "http://127.0.0.1:5173/dev/practice/runs", "/dev/practice/runs/../runs", "" })
                Refused(() => outbox.Stage(route, Play("route-00001", 0)), Code.InvalidRequest);
            Refused(() => Outbox.CommandRoute("pr-0123"), Code.InvalidRequest);
            Refused(() => Outbox.MemoryCommitRoute("PR-0123456789ABCDEF0123456789ABCDEF"), Code.InvalidRequest);

            foreach (string id in new[] { "abcd-12", new string('a', 65), "has space1", "slash/0001", "\u00fcnicode01", "plus+0001", "" })
                Refused(() => outbox.Stage(Outbox.CreateRunRoute, NewRun(id)), Code.InvalidRequest);
            Refused(() => outbox.Stage(Outbox.CreateRunRoute, "{\"requestId\":12345678}"), Code.InvalidRequest);
            Refused(() => outbox.Stage(Outbox.CreateRunRoute, "{\"useMemory\":true}"), Code.InvalidRequest);

            foreach (string version in new[] { "-1", "1.0", "1e2", "\"5\"", "2147483648", "99999999999999999999", "null", "true", "[1]" })
            {
                Refused(() => outbox.Stage(CommandRoute, "{\"requestId\":\"version-0001\",\"expectedVersion\":" + version + ",\"kind\":\"end\"}"), Code.InvalidRequest);
                Refused(() => outbox.Stage(Outbox.MemoryCommitRoute(RunId),
                    "{\"requestId\":\"version-0001\",\"expectedVersion\":1,\"expectedMemoryVersion\":" + version + ",\"selectedChangeIds\":[]}"), Code.InvalidRequest);
            }
            Refused(() => outbox.Stage(CommandRoute, "{\"requestId\":\"version-0001\",\"kind\":\"end\"}"), Code.InvalidRequest);
            Refused(() => outbox.Stage(Outbox.MemoryCommitRoute(RunId), "{\"requestId\":\"version-0001\",\"expectedVersion\":1,\"selectedChangeIds\":[]}"), Code.InvalidRequest);
            Refused(() => outbox.Stage(Outbox.CreateRunRoute, "{\"requestId\":\"version-0001\",\"expectedVersion\":0}"), Code.InvalidRequest);

            // Exactly MaxBodyBytes UTF-8 bytes fit, one more does not; multi-byte characters count as bytes.
            string head = "{\"requestId\":\"size-limit\",\"pad\":\"", tail = "\"}";
            int room = Outbox.MaxBodyBytes - head.Length - tail.Length;
            string fits = head + new string('x', room) + tail;
            Assert.That(StrictUtf8.GetByteCount(fits), Is.EqualTo(Outbox.MaxBodyBytes));
            StageAndClear(outbox, Outbox.CreateRunRoute, fits);
            Refused(() => outbox.Stage(Outbox.CreateRunRoute, head + new string('x', room + 1) + tail), Code.InvalidRequest);
            string wide = head + new string('\u00e9', room / 2 + 1) + tail;
            Assert.That(wide.Length, Is.LessThan(Outbox.MaxBodyBytes));
            Assert.That(StrictUtf8.GetByteCount(wide), Is.GreaterThan(Outbox.MaxBodyBytes));
            Refused(() => outbox.Stage(Outbox.CreateRunRoute, wide), Code.InvalidRequest);
            Refused(() => outbox.Stage(Outbox.CreateRunRoute, ""), Code.InvalidRequest);
            Refused(() => outbox.Stage(Outbox.CreateRunRoute, "{\"requestId\":\"raw-surrogate\",\"s\":\"\ud800\"}"), Code.InvalidRequest);

            // Depth: the object and seven nested arrays (8) fit, eight nested arrays (9) do not.
            StageAndClear(outbox, Outbox.CreateRunRoute, "{\"requestId\":\"depth-eight\",\"d\":[[[[[[[1]]]]]]]}");
            Refused(() => outbox.Stage(Outbox.CreateRunRoute, "{\"requestId\":\"depth-nine1\",\"d\":[[[[[[[[1]]]]]]]]}"), Code.InvalidRequest);

            Assert.Throws<ArgumentNullException>(() => outbox.Stage(null!, NewRun("null-route1")));
            Assert.Throws<ArgumentNullException>(() => outbox.Stage(CommandRoute, null!));
            Assert.Throws<ArgumentNullException>(() => outbox.Confirm(null!));
            Assert.That(Directory.GetFileSystemEntries(folder).Select(p => Path.GetFileName(p)), Is.EqualTo(new[] { FileName + Outbox.LockSuffix }),
                "refused requests left no record or temp file");
        }

        [Test]
        public void JsonExtensionsAndMalformedBodiesAreRefusedBeforeAnyFileIsTouched()
        {
            var outbox = Open();
            const string Id = "\"requestId\":\"strict-0001\"";
            string[] bodies =
            {
                "{" + Id + "/*c*/}", "//c\n{" + Id + "}", "{" + Id + "}//c", "{" + Id + ",#c\n\"x\":1}",
                "{'requestId':'strict-0001'}", "{" + Id + ",'x':1}", "{" + Id + ",\"s\":'x'}", "{requestId:\"strict-0001\"}", "{" + Id + ",x:1}",
                "{" + Id + ",}", "{" + Id + ",\"p\":[1,]}", "{," + Id + "}", "{" + Id + ",,\"x\":1}", "{" + Id + ",\"p\":[,1]}", "{" + Id + ",\"x\":}",
                "{" + Id + ",\"n\":0x1F}", "{" + Id + ",\"n\":017}", "{" + Id + ",\"n\":00}", "{" + Id + ",\"n\":NaN}", "{" + Id + ",\"n\":Infinity}",
                "{" + Id + ",\"n\":-Infinity}", "{" + Id + ",\"n\":+1}", "{" + Id + ",\"n\":.5}", "{" + Id + ",\"n\":1.}", "{" + Id + ",\"n\":1e}",
                "{" + Id + ",\"n\":-}", "{" + Id + ",\"n\":undefined}", "{" + Id + ",\"t\":True}", "{" + Id + ",\"t\":nul}", "{" + Id + ",\"t\":truex}",
                "{" + Id + ",\"d\":new Date(1)}",
                "{" + Id + "} {}", "{" + Id + "}x", "{" + Id + "},", "{" + Id + "}{" + Id + "}", "{" + Id + "}]", "{" + Id + ",\"x\":1 2}", "{" + Id + " \"x\":1}",
                "{" + Id + "," + Id + "}", "{" + Id + ",\"\\u0072equestId\":\"strict-0001\"}", "{" + Id + ",\"a\":{\"b\":1,\"b\":2}}",
                "{" + Id + ",\"a\":[{\"b\":1,\"\\u0062\":2}]}",
                "{" + Id + ",\"s\":\"a\tb\"}", "{" + Id + ",\"s\":\"a\nb\"}", "{" + Id + ",\"s\":\"\\x41\"}", "{" + Id + ",\"s\":\"\\u12\"}",
                "{" + Id + ",\"s\":\"\\U0041\"}", "{" + Id, "{" + Id + ",\"s\":\"open}", "{" + Id + ",\"a\":[1}", "{" + Id + ",\"a\":{]}",
                "\ufeff{" + Id + "}", "[{" + Id + "}]", "\"" + Id + "\"", "null", " ",
            };
            foreach (string body in bodies) Refused(() => outbox.Stage(Outbox.CreateRunRoute, body), Code.InvalidRequest);
            Assert.That(Directory.GetFileSystemEntries(folder), Is.Empty, "request validation precedes the lock: nothing was created");
        }

        [Test]
        public void StandardJsonInOpaqueMembersIsAcceptedAndKeptExactly()
        {
            var outbox = Open();
            string[] bodies =
            {
                " \t\r\n{ \"requestId\" : \"standard-0001\" , \"expectedVersion\" : 0 , \"kind\" : \"play\" , \"args\" : { } } \n",
                "{\"requestId\":\"standard-0002\",\"expectedVersion\":1,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"t\":true,\"f\":false,\"n\":null,\"e\":[],\"o\":{}}}",
                "{\"requestId\":\"standard-0003\",\"expectedVersion\":2,\"kind\":\"play\",\"args\":{\"x\":-0.5,\"y\":6.02e23,\"z\":1E-7,\"w\":-1.5E+3,\"v\":0e0,\"u\":10}}",
                "{\"requestId\":\"standard-0004\",\"expectedVersion\":3,\"kind\":\"play\",\"args\":{\"s\":\"\\\" \\\\ \\/ \\b \\f \\n \\r \\t \\u00e9 \\u0000\"}}",
                "{\"requestId\":\"standard-0005\",\"expectedVersion\":4,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"picks\":[\"\\uD83D\\uDE00\",\"\\ud83d\\ude00\",\"\ud83d\ude00\"]}}",
                "{\"requestId\":\"standard-0006\",\"expectedVersion\":5,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"picks\":[\"\ufffd\",\"\\uFFFD\",\"\\\\uD800\",\"\\\\uDC00\"]}}",
                "{\"requestId\":\"standard-0007\",\"expectedVersion\":6,\"kind\":\"play\",\"args\":{\"uid\":\"\uc801\",\"date\":\"2026-10-17T00:00:00Z\",\"\\u006eame\":\"x\"}}",
            };
            foreach (string body in bodies) StageAndClear(outbox, CommandRoute, body);
        }

        [Test]
        public void EscapedUnpairedSurrogatesAreRefused()
        {
            // RED regression of an independent review finding (do not adjust to the source): the lexical pass checks only the four hex
            // digits of a \u escape and Newtonsoft turns an escaped lone UTF-16 surrogate into U+FFFD before the pairing check sees it, so
            // such a body could be staged and reloaded although the contract refuses unpaired surrogates.
            foreach (string escape in new[] { "\\uD800", "\\uDC00", "\\uDBFF\\u0041", "\\uDE00\\uD83D" })
            {
                string file = Path.Combine(folder, "lone-" + Guid.NewGuid().ToString("N") + ".json");
                var outbox = new Outbox(file, Endpoint, Account);
                string body = "{\"requestId\":\"lone-surrogate\",\"expectedVersion\":3,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"picks\":[\"" + escape + "\"]}}";
                Refused(() => outbox.Stage(CommandRoute, body), Code.InvalidRequest);
                Assert.That(File.Exists(file), Is.False, escape + ": a refused body is never recorded");
            }
            // Still valid: a paired escape, a genuine U+FFFD and literal backslash text.
            var valid = Open();
            StageAndClear(valid, CommandRoute, "{\"requestId\":\"paired-0001\",\"expectedVersion\":3,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"picks\":[\"\\uD83D\\uDE00\"]}}");
            StageAndClear(valid, CommandRoute, "{\"requestId\":\"paired-0002\",\"expectedVersion\":3,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"picks\":[\"\ufffd\"]}}");
            StageAndClear(valid, CommandRoute, "{\"requestId\":\"paired-0003\",\"expectedVersion\":3,\"kind\":\"play\",\"args\":{\"uid\":\"c1\",\"picks\":[\"\\\\uD800\"]}}");
        }

        [Test]
        public void TheLargestRecordReloadsAndAnOversizedFileIsRefusedAndKept()
        {
            // Escaped backslashes expand the most: each body byte becomes four record bytes.
            string head = "{\"requestId\":\"largest-body\",\"pad\":\"", tail = "\"}";
            string body = head + string.Concat(Enumerable.Repeat("\\\\", (Outbox.MaxBodyBytes - head.Length - tail.Length) / 2)) + tail;
            Assert.That(StrictUtf8.GetByteCount(body), Is.LessThanOrEqualTo(Outbox.MaxBodyBytes));
            var staged = Open().Stage(Outbox.CreateRunRoute, body);
            Assert.That(new FileInfo(Record).Length, Is.GreaterThan(3L * Outbox.MaxBodyBytes).And.LessThanOrEqualTo(Outbox.MaxRecordBytes));
            AssertSame(staged, Open().Load());
            Open().Confirm(staged);
            RefusedAndKept(Enumerable.Repeat((byte)'a', Outbox.MaxRecordBytes + 1).ToArray(), staged, "oversized", Code.RecordCorrupt);
        }

        [Test]
        public void InvalidConfigurationIsRefusedWithoutTouchingTheFolder()
        {
            foreach (string endpoint in new[] { "http://localhost:5173", "https://127.0.0.1:5173", "http://127.0.0.1:0", "http://127.0.0.1:65536",
                "http://127.0.0.1:05173", "http://127.0.0.1:5173/", "http://127.0.0.1:5173/dev", "http://127.0.0.1:5173?x=1", "http://127.0.0.1:5173#x",
                "http://user:pw@127.0.0.1:5173", "http://127.0.0.1", "http://127.0.0.1:", "http://[::1]:5173", "http://127.0.0.2:5173",
                "http://127.0.0.1:+517", "HTTP://127.0.0.1:5173", " http://127.0.0.1:5173", "" })
                Refused(() => new Outbox(Record, endpoint, Account), Code.InvalidConfiguration);
            foreach (string account in new[] { "d", "Dev-local", "1dev", "-dev", "dev_local", "dev local", new string('a', 33), "" })
                Refused(() => new Outbox(Record, Endpoint, account), Code.InvalidConfiguration);
            Refused(() => new Outbox("relative/" + FileName, Endpoint, Account), Code.InvalidConfiguration);
            Refused(() => new Outbox(Path.Combine(folder, "missing", FileName), Endpoint, Account), Code.InvalidConfiguration);
            Refused(() => new Outbox(folder + Path.DirectorySeparatorChar, Endpoint, Account), Code.InvalidConfiguration);
            Assert.Throws<ArgumentNullException>(() => new Outbox(null!, Endpoint, Account));
            Assert.Throws<ArgumentNullException>(() => new Outbox(Record, null!, Account));
            Assert.Throws<ArgumentNullException>(() => new Outbox(Record, Endpoint, null!));
            Assert.That(Directory.GetFileSystemEntries(folder), Is.Empty, "the constructor creates nothing");

            Assert.That(new Outbox(Record, "http://127.0.0.1:1", "ab").Load(), Is.Null);
            Assert.That(new Outbox(Record, "http://127.0.0.1:65535", new string('a', 32)).Load(), Is.Null);
            Assert.That(Directory.GetFileSystemEntries(folder).Select(p => Path.GetFileName(p)), Is.EqualTo(new[] { FileName + Outbox.LockSuffix }));
        }

        [Test]
        public void SeparateInstancesOnThreadsNeverOverwriteOrStaleClear()
        {
            const int Racers = 4, Rounds = 25;
            var instances = Enumerable.Range(0, Racers).Select(_ => Open()).ToArray();
            for (int round = 0; round < Rounds; round++)
            {
                string tag = "race-" + round.ToString("D2", CultureInfo.InvariantCulture);
                string[] bodies = Enumerable.Range(0, Racers).Select(k => Play(tag + "-" + k.ToString(CultureInfo.InvariantCulture), round)).ToArray();
                object?[] outcomes = Race(Enumerable.Range(0, Racers).Select(k => (Func<object>)(() => instances[k].Stage(CommandRoute, bodies[k]))).ToArray());
                // One holder at a time publishes; every other call is BUSY or sees the winner, and nothing overwrites it.
                var winners = outcomes.OfType<PendingPracticeCommand>().ToList();
                Assert.That(winners.Count, Is.EqualTo(1), tag + ": exactly one stage publishes");
                foreach (object? loser in outcomes.Where(o => !(o is PendingPracticeCommand)))
                {
                    Assert.That(loser, Is.InstanceOf<PracticeOutboxException>(), tag + ": unexpected " + loser);
                    Assert.That(((PracticeOutboxException)loser!).Code, Is.EqualTo(Code.Busy).Or.EqualTo(Code.PendingOther), tag);
                }
                var winner = winners[0];
                byte[] bytes = File.ReadAllBytes(Record);
                AssertSame(winner, Open().Load());

                // Confirming the winner while another instance stages a different request: the new request appears only after the
                // winner was cleared, and the confirmation never clears it.
                string next = Play(tag + "-next", round + 1);
                object?[] pair = Race(() => { instances[round % Racers].Confirm(winner); return "cleared"; },
                    () => instances[(round + 1) % Racers].Stage(CommandRoute, next));
                bool cleared = "cleared".Equals(pair[0]);
                if (!cleared)
                {
                    Assert.That(pair[0], Is.InstanceOf<PracticeOutboxException>(), tag + ": unexpected " + pair[0]);
                    Assert.That(((PracticeOutboxException)pair[0]!).Code, Is.EqualTo(Code.Busy), tag);
                }
                if (pair[1] is PendingPracticeCommand fresh)
                {
                    Assert.That(cleared, Is.True, tag + ": a different request was staged only after the winner was cleared");
                    AssertSame(fresh, Open().Load());
                    Open().Confirm(fresh);
                }
                else
                {
                    Assert.That(pair[1], Is.InstanceOf<PracticeOutboxException>(), tag + ": unexpected " + pair[1]);
                    Assert.That(((PracticeOutboxException)pair[1]!).Code, Is.EqualTo(Code.Busy).Or.EqualTo(Code.PendingOther), tag);
                    if (cleared) Assert.That(Open().Load(), Is.Null, tag);
                    else
                    {
                        Assert.That(File.ReadAllBytes(Record), Is.EqualTo(bytes), tag + ": an uncleared winner is kept whole");
                        Open().Confirm(winner);
                    }
                }
                Assert.That(Open().Load(), Is.Null, tag);
                NoTemps();
            }
        }

        // ---- helpers ----

        static void Refused(TestDelegate action, params string[] codes)
        {
            var error = Assert.Throws<PracticeOutboxException>(action)!;
            Assert.That(codes, Has.Member(error.Code), "refusal code " + error.Code);
        }

        // Writes the bytes as the record, then every operation must refuse with one of the codes and leave them byte for byte.
        void RefusedAndKept(byte[] record, PendingPracticeCommand attempt, string name, params string[] codes)
        {
            File.WriteAllBytes(Record, record);
            var outbox = Open();
            var actions = new TestDelegate[]
            {
                () => outbox.Load(),
                () => outbox.Stage(attempt.Route, attempt.Body),
                () => outbox.Stage(Outbox.CreateRunRoute, NewRun("another-0001")),
                () => outbox.Confirm(attempt),
            };
            foreach (var action in actions)
            {
                var error = Assert.Throws<PracticeOutboxException>(action, name)!;
                Assert.That(codes, Has.Member(error.Code), name + ": " + error.Code);
            }
            Assert.That(File.ReadAllBytes(Record), Is.EqualTo(record), name + ": a refused record is kept byte for byte");
            NoTemps();
        }

        static void StageAndClear(Outbox outbox, string route, string body)
        {
            var staged = outbox.Stage(route, body);
            var reloaded = outbox.Load();
            AssertSame(staged, reloaded);
            Assert.That(reloaded!.Body, Is.EqualTo(body));
            Assert.That(reloaded.BodyUtf8(), Is.EqualTo(StrictUtf8.GetBytes(body)));
            outbox.Confirm(staged);
            Assert.That(outbox.Load(), Is.Null);
        }

        static void AssertSame(PendingPracticeCommand expected, PendingPracticeCommand? actual)
        {
            Assert.That(actual, Is.Not.Null, "no pending request");
            Assert.That(actual!.Endpoint, Is.EqualTo(expected.Endpoint));
            Assert.That(actual.Account, Is.EqualTo(expected.Account));
            Assert.That(actual.Method, Is.EqualTo(expected.Method));
            Assert.That(actual.Route, Is.EqualTo(expected.Route));
            Assert.That(actual.RequestId, Is.EqualTo(expected.RequestId));
            Assert.That(actual.ExpectedVersion, Is.EqualTo(expected.ExpectedVersion));
            Assert.That(actual.Body, Is.EqualTo(expected.Body));
            Assert.That(actual.BodyUtf8(), Is.EqualTo(expected.BodyUtf8()));
        }

        void NoTemps() => Assert.That(Directory.GetFiles(folder, ".*" + Outbox.TempSuffix), Is.Empty, "no temp file is left behind");

        static void Mode(string path, int mode)
        {
            int result = chmod(path, mode);
            Assert.That(result, Is.EqualTo(0), "chmod of the test's own temp path failed, errno " + Marshal.GetLastWin32Error());
        }

        static object?[] Race(params Func<object>[] calls)
        {
            var outcomes = new object?[calls.Length];
            using (var start = new Barrier(calls.Length))
            {
                var threads = new Thread[calls.Length];
                for (int k = 0; k < calls.Length; k++)
                {
                    int index = k;
                    threads[k] = new Thread(() =>
                    {
                        try
                        {
                            if (!start.SignalAndWait(TimeSpan.FromSeconds(10)))
                                throw new TimeoutException("Outbox race participants did not reach the start barrier.");
                            outcomes[index] = calls[index]();
                        }
                        catch (Exception error) { outcomes[index] = error; }
                    });
                    threads[k].Start();
                }
                foreach (var thread in threads) Assert.That(thread.Join(TimeSpan.FromSeconds(30)), Is.True, "a racing outbox call did not finish");
            }
            return outcomes;
        }

        // ---- an independent encoder of the documented record format ----

        static List<(string Name, string Value)> Fields(PendingPracticeCommand command) => new List<(string Name, string Value)>
        {
            ("endpoint", command.Endpoint), ("account", command.Account), ("method", command.Method), ("route", command.Route),
            ("requestId", command.RequestId), ("body", command.Body),
        };

        static List<(string Name, string Value)> With(List<(string Name, string Value)> fields, string name, string value) =>
            fields.Select(f => f.Name == name ? (Name: f.Name, Value: value) : f).ToList();

        static string Json(IEnumerable<(string Name, string Value)> members) =>
            "{" + string.Join(",", members.Select(m => Quote(m.Name) + ":" + Quote(m.Value))) + "}";

        // ASCII: '"' and '\' backslash-escaped, printable ASCII as is, everything else a lowercase \u escape of its UTF-16 unit.
        static string Quote(string text)
        {
            var json = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') json.Append('\\').Append(c);
                else if (c >= ' ' && c <= '~') json.Append(c);
                else json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            }
            return json.Append('"').ToString();
        }

        static byte[] Envelope(string record, string schema = Outbox.Schema) => Outer(("schema", schema), ("sha256", Sha256(record)), ("record", record));

        static byte[] Outer(params (string Name, string Value)[] members) => StrictUtf8.GetBytes(Json(members));

        static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(StrictUtf8.GetBytes(text)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }
}
