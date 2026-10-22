// IL2CPP FileStream handles are opaque FileHandle pointers. Only native open supplies the fd used here.
#if UNITY_ANDROID && !UNITY_EDITOR
#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MemoryForge.Client
{
    internal sealed class PracticeAndroidFile : IDisposable
    {
        const int ReadWrite = 2, WriteOnly = 1, Create = 0x40, Exclusive = 0x80, CloseOnExec = 0x80000;
        const int ExclusiveLock = 2, NonBlocking = 4, Interrupted = 4, WouldBlock = 11, MaxAttempts = 8;
        const uint OwnerReadWrite = 0x180; // POSIX 0600, even before the first byte is written.
        int fd;
        readonly bool lockFile;

        PracticeAndroidFile(int descriptor, bool isLock) { fd = descriptor; lockFile = isLock; }

        internal static IDisposable Lock(string path)
        {
            try
            {
                var file = new PracticeAndroidFile(Open(path, ReadWrite | Create | CloseOnExec), true);
                try
                {
                    for (int attempt = 0; attempt < MaxAttempts; attempt++)
                    {
                        // Distinct native opens conflict even inside this process; no managed sharing table is needed.
                        if (flock(file.fd, ExclusiveLock | NonBlocking) == 0) return file;
                        int error = Marshal.GetLastWin32Error();
                        if (error == Interrupted) continue;
                        if (error == WouldBlock) throw new PracticeOutboxException(PracticeOutboxException.Busy, "Another outbox operation holds the lock; nothing was changed. Retry later.");
                        throw new IOException("The native outbox lock was refused.");
                    }
                    throw new IOException("The native outbox lock kept being interrupted.");
                }
                catch { file.Dispose(); throw; }
            }
            catch (Exception error) when (MissingNative(error))
            {
                throw new PracticeOutboxException(PracticeOutboxException.Unsupported, "The native flock lock is not available; nothing was read or changed.");
            }
            catch (IOException)
            {
                throw new PracticeOutboxException(PracticeOutboxException.LockFailed, "The outbox lock file could not be opened or locked; nothing was changed.");
            }
        }

        internal static PracticeAndroidFile CreateTemp(string path)
        {
            try { return new PracticeAndroidFile(Open(path, WriteOnly | Create | Exclusive | CloseOnExec), false); }
            catch (Exception error) when (MissingNative(error)) { throw new IOException("Native temp creation is not available."); }
        }

        static int Open(string path, int flags)
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                int descriptor = open(path, flags, OwnerReadWrite);
                if (descriptor >= 0) return descriptor;
                if (Marshal.GetLastWin32Error() != Interrupted) throw new IOException("The native file could not be opened.");
            }
            throw new IOException("Opening the native file kept being interrupted.");
        }

        internal void WriteAndFlush(byte[] bytes)
        {
            try
            {
                Retry(() => fchmod(fd, OwnerReadWrite));
                var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    int offset = 0, interruptions = 0;
                    while (offset < bytes.Length)
                    {
                        long written = write(fd, IntPtr.Add(pinned.AddrOfPinnedObject(), offset), new UIntPtr((uint)(bytes.Length - offset))).ToInt64();
                        if (written < 0 && Marshal.GetLastWin32Error() == Interrupted && ++interruptions < MaxAttempts) continue;
                        if (written <= 0 || written > bytes.Length - offset) throw new IOException("The native temp write did not complete.");
                        offset += (int)written;
                    }
                }
                finally { pinned.Free(); }
                Retry(() => fsync(fd));
            }
            catch (Exception error) when (MissingNative(error)) { throw new IOException("Native private durable writes are not available."); }
        }

        static void Retry(Func<int> operation)
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                if (operation() == 0) return;
                if (Marshal.GetLastWin32Error() != Interrupted) throw new IOException("The native file operation was refused.");
            }
            throw new IOException("The native file operation kept being interrupted.");
        }

        public void Dispose()
        {
            int descriptor = Interlocked.Exchange(ref fd, -1);
            if (descriptor < 0) return;
            // Linux releases the fd even for EINTR: retrying close could close an unrelated reused descriptor.
            try
            {
                if (close(descriptor) != 0) throw new IOException("Closing the native file failed.");
            }
            catch (Exception error) when (error is IOException || MissingNative(error))
            {
                if (lockFile) throw new PracticeOutboxException(PracticeOutboxException.LockFailed, "Closing the outbox lock failed; the pending state must be reloaded.");
                throw new IOException("Closing the native temp failed.");
            }
        }

        static bool MissingNative(Exception error) => error is DllNotFoundException || error is EntryPointNotFoundException || error is MarshalDirectiveException;
        [DllImport("libc", SetLastError = true)] static extern int open(string path, int flags, uint mode);
        [DllImport("libc", SetLastError = true)] static extern int flock(int descriptor, int operation);
        [DllImport("libc", SetLastError = true)] static extern int fchmod(int descriptor, uint mode);
        [DllImport("libc", SetLastError = true)] static extern IntPtr write(int descriptor, IntPtr bytes, UIntPtr count);
        [DllImport("libc", SetLastError = true)] static extern int fsync(int descriptor);
        [DllImport("libc", SetLastError = true)] static extern int close(int descriptor);
    }
}
#endif
