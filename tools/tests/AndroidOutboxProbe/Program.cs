using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using MemoryForge.Client;
#pragma warning disable CA1416 // This POSIX-only probe is launched by run.sh on Linux/macOS.

static class Probe
{
    const string Endpoint = "http://127.0.0.1:5173", Account = "dev-local";
    const string Body = "{\n  \"requestId\":\"android-fd-proof\",\"useMemory\":true,\"note\":\"한글 \\u0041 😀\"\n}";
    static readonly MethodInfo LockMethod = typeof(PracticeCommandOutbox).GetMethod("Lock", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static int passed;
    [DllImport("outbox_probe")] static extern void mf_reset();
    [DllImport("outbox_probe")] static extern void mf_fault(int operation, int error, int count);
    [DllImport("outbox_probe")] static extern void mf_fault_after(int operation, int error, int count, int before);
    [DllImport("outbox_probe")] static extern int mf_count(int operation);
    [DllImport("outbox_probe")] static extern int mf_premature();
    [DllImport("outbox_probe")] static extern int mf_flags_bad();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static PracticeCommandOutbox Open(string path) => new PracticeCommandOutbox(path, Endpoint, Account);
    static IDisposable Hold(string path)
    {
        try { return (IDisposable)LockMethod.Invoke(Open(path), null)!; }
        catch (TargetInvocationException error) { throw error.InnerException!; }
    }
    static void Publish(string path)
    {
        try { typeof(PracticeCommandOutbox).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Open(path), new object[] { new byte[] { 1, 2, 3 } }); }
        catch (TargetInvocationException error) { throw error.InnerException!; }
    }
    static void Refused(Action action, string code)
    {
        try { action(); throw new Exception("Expected " + code); }
        catch (PracticeOutboxException error) { Check(error.Code == code, "Expected " + code + ", got " + error.Code); }
    }
    static void Test(string name, Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "mf-android-outbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); mf_reset();
        try { action(Path.Combine(directory, "outbox.json")); passed++; Console.WriteLine("PASS " + name); }
        finally { Directory.Delete(directory, true); }
    }
    static void Empty(string path) => Check(!File.Exists(path) && !Directory.GetFiles(Path.GetDirectoryName(path)!, ".*.tmp").Any(), "Failure published a record or left its own temp");
    static int Main(string[] args)
    {
        string nativePath = Environment.GetEnvironmentVariable("MF_OUTBOX_PROBE_NATIVE") ?? throw new Exception("Set MF_OUTBOX_PROBE_NATIVE to the native probe library");
        bool direct = args.Length > 0 && (args[0] == "direct" || args[0] == "hold-direct");
        if (direct && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Direct Android/Linux flags require Linux");
        IntPtr native = NativeLibrary.Load(nativePath);
        NativeLibrary.SetDllImportResolver(typeof(Probe).Assembly, (name, assembly, search) => name == "outbox_probe" || name == "libc" && !direct ? native : IntPtr.Zero);
        if (args.Length == 2 && (args[0] == "hold" || args[0] == "hold-direct"))
        {
            using (Hold(args[1])) { Console.WriteLine("LOCKED"); Console.ReadLine(); }
            return 0;
        }
        try
        {
            Test("exact body, private mode, partial native writes, reload, confirm", path =>
            {
                var staged = Open(path).Stage(PracticeCommandOutbox.CreateRunRoute, Body);
                Check(staged.Body == Body && Open(path).Load()!.Body == Body, "Body changed");
                byte[] stored = File.ReadAllBytes(path);
                Check((File.GetUnixFileMode(path) & (UnixFileMode)0x1ff) == (UnixFileMode)0x180, "Record was not 0600");
                if (!direct) Check(mf_count(4) > 1 && mf_count(5) == 1 && mf_premature() == 0 && mf_flags_bad() == 0, "Native write/privacy/durability sequence failed");
                Check(Open(path).Stage(PracticeCommandOutbox.CreateRunRoute, Body).Body == Body && stored.SequenceEqual(File.ReadAllBytes(path)), "Restage changed record");
                Refused(() => Open(path).Stage(PracticeCommandOutbox.CreateRunRoute, "{\"requestId\":\"different-id\",\"useMemory\":true}"), PracticeOutboxException.PendingOther);
                Open(path).Confirm(staged); Check(Open(path).Load() == null, "Confirm did not clear exact record");
            });
            Test("same-process and separate-process native lock contention", path =>
            {
                using (Hold(path)) Refused(() => Open(path).Load(), PracticeOutboxException.Busy);
                var start = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardInput = true, RedirectStandardError = true };
                start.ArgumentList.Add(typeof(Probe).Assembly.Location); start.ArgumentList.Add(direct ? "hold-direct" : "hold"); start.ArgumentList.Add(path);
                using var child = Process.Start(start)!;
                try
                {
                    Check(child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult() == "LOCKED", "Child could not acquire native lock");
                    Refused(() => Open(path).Load(), PracticeOutboxException.Busy);
                }
                finally { child.StandardInput.WriteLine(); Check(child.WaitForExit(5000), "Child lock did not exit"); }
                Check(child.ExitCode == 0 && Open(path).Load() == null, "Released lock unavailable");
            });
            if (direct) { Console.WriteLine("2/2 direct Linux libc checks passed (no boundary bridge)"); return 0; }
            Test("bounded native EINTR succeeds then fails closed", path =>
            {
                mf_fault(1, 4, 2); Check(Open(path).Load() == null, "Open retry failed");
                mf_fault(2, 4, 2); Check(Open(path).Load() == null, "Lock retry failed");
                mf_fault(2, 4, 20); int before = mf_count(2);
                Refused(() => Open(path).Load(), PracticeOutboxException.LockFailed);
                Check(mf_count(2) - before == 8, "Lock EINTR was not bounded");
            });
            foreach (int operation in new[] { 3, 4, 5, 6 })
                Test("publish fail closed at native operation " + operation, path =>
                {
                    string stray = Path.Combine(Path.GetDirectoryName(path)!, ".unrelated.tmp"); File.WriteAllText(stray, "keep");
                    using (Hold(path))
                    {
                        mf_fault(operation, 5, 1);
                        Refused(() => Publish(path), PracticeOutboxException.WriteFailed);
                        Check(!File.Exists(path) && Directory.GetFiles(Path.GetDirectoryName(path)!, ".*.tmp").SequenceEqual(new[] { stray }), "Failure touched a foreign temp or published");
                        Check(File.ReadAllText(stray) == "keep", "Foreign temp changed");
                        if (operation == 3) Check(mf_count(4) == 0, "Bytes written before chmod succeeded");
                    }
                });
            Test("failed exclusive open preserves existing temp exactly", path =>
            {
                using (Hold(path))
                {
                    mf_fault(1, 17, 1); Refused(() => Publish(path), PracticeOutboxException.WriteFailed);
                    var temps = Directory.GetFiles(Path.GetDirectoryName(path)!, ".*.tmp");
                    Check(temps.Length == 1 && File.ReadAllText(temps[0]) == "foreign-temp" && !File.Exists(path), "Failed open removed or changed an existing temp");
                    Check(mf_count(3) == 0 && mf_count(4) == 0, "Failed open attempted chmod/write");
                }
            });
            Test("ENOSPC and move refusal preserve existing record exactly", path =>
            {
                using (Hold(path))
                {
                    File.WriteAllText(path, "existing-record");
                    mf_fault(4, 28, 1); Refused(() => Publish(path), PracticeOutboxException.WriteFailed);
                    Check(File.ReadAllText(path) == "existing-record" && !Directory.GetFiles(Path.GetDirectoryName(path)!, ".*.tmp").Any(), "ENOSPC changed record or retained own temp");
                    Refused(() => Publish(path), PracticeOutboxException.WriteFailed);
                    Check(File.ReadAllText(path) == "existing-record" && !Directory.GetFiles(Path.GetDirectoryName(path)!, ".*.tmp").Any(), "Move overwrote record or retained own temp");
                }
            });
            foreach (int operation in new[] { 3, 4, 5 })
                Test("bounded temp EINTR at native operation " + operation, path =>
                {
                    using (Hold(path))
                    {
                        mf_fault(operation, 4, 2); Publish(path); File.Delete(path);
                        mf_fault(operation, 4, 20); int before = mf_count(operation);
                        Refused(() => Publish(path), PracticeOutboxException.WriteFailed);
                        Check(mf_count(operation) - before == 8, "Temp operation EINTR was not bounded"); Empty(path);
                    }
                });
            Test("native close EINTR is not retried", path =>
            {
                var held = Hold(path); mf_fault(6, 4, 1); int before = mf_count(6);
                Refused(held.Dispose, PracticeOutboxException.LockFailed);
                Check(mf_count(6) - before == 1, "close retried after EINTR");
                held.Dispose(); Check(mf_count(6) - before == 1, "Dispose closed twice");
                Check(Open(path).Load() == null, "Closed descriptor retained lock");
            });
            Test("lock-close failure after publish keeps exact pending state", path =>
            {
                mf_fault_after(6, 4, 1, 1); // Temp close succeeds; lock close fails after publication.
                Refused(() => Open(path).Stage(PracticeCommandOutbox.CreateRunRoute, Body), PracticeOutboxException.LockFailed);
                byte[] original = File.ReadAllBytes(path);
                Check(Open(path).Load()!.Body == Body && File.ReadAllBytes(path).SequenceEqual(original), "Close failure lost or rewrote committed pending bytes");
                Check(!Directory.GetFiles(Path.GetDirectoryName(path)!, ".*.tmp").Any(), "Published temp retained");
            });
            Console.WriteLine(passed + "/" + passed + " Android-conditional native boundary checks passed"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL after " + passed + " checks: " + error); return 1; }
    }
}
