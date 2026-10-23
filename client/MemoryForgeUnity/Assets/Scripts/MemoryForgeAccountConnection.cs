#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MemoryForge.Client
{
    /// <summary>App-memory authentication owner. Views own their sessions; disposing a view never clears this owner's credential.</summary>
    public sealed class MemoryForgeAccountConnection : IDisposable
    {
        static readonly object currentSync = new object();
        readonly object sync = new object();
        readonly List<WeakReference<PracticeServerSession>> sessions = new List<WeakReference<PracticeServerSession>>();
        string credential;
        bool disposed;

        public static MemoryForgeAccountConnection? Current { get; private set; }
        public string Endpoint { get; }
        public string Account { get; }
        public string OutboxPath { get; }
        public bool IsAuthenticated { get { lock (sync) return !disposed; } }

        public MemoryForgeAccountConnection(string endpoint, string account, string credential, string outboxPath)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (account == null) throw new ArgumentNullException(nameof(account));
            if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var origin) || origin.Scheme != "http" || origin.Host != "127.0.0.1"
                || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
                throw new ArgumentException("The development endpoint must be http://127.0.0.1:<port>.", nameof(endpoint));
            Endpoint = "http://127.0.0.1:" + origin.Port.ToString(CultureInfo.InvariantCulture);
            Account = account.Trim();
            if (credential == null || credential.Length < 16 || credential.Length > 512)
                throw new ArgumentException("A valid in-memory development credential is required.", nameof(credential));
            foreach (char character in credential) if (character < '!' || character > '~')
                throw new ArgumentException("A valid in-memory development credential is required.", nameof(credential));
            // Existing outbox validation owns origin, account, path and parent-directory constraints; this writes nothing.
            _ = new PracticeCommandOutbox(outboxPath, Endpoint, Account);
            OutboxPath = System.IO.Path.GetFullPath(outboxPath);
            this.credential = credential;
        }

        public static void SetCurrent(MemoryForgeAccountConnection? owner)
        {
            MemoryForgeAccountConnection? previous;
            lock (currentSync)
            {
                if (ReferenceEquals(Current, owner)) return;
                if (owner != null && !owner.IsAuthenticated) throw new ObjectDisposedException(nameof(MemoryForgeAccountConnection));
                previous = Current; Current = owner;
            }
            previous?.Logout();
        }

        public PracticeServerSession CreateSession()
        {
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(MemoryForgeAccountConnection));
                sessions.RemoveAll(reference => !reference.TryGetTarget(out _));
                var session = new PracticeServerSession(Endpoint, Account, ReadCredential, new PracticeCommandOutbox(OutboxPath, Endpoint, Account));
                sessions.Add(new WeakReference<PracticeServerSession>(session));
                return session;
            }
        }

        string ReadCredential() { lock (sync) return disposed ? "" : credential; }

        public void Logout()
        {
            var active = new List<PracticeServerSession>();
            lock (sync)
            {
                if (disposed) return;
                disposed = true; credential = "";
                foreach (var reference in sessions) if (reference.TryGetTarget(out var session)) active.Add(session);
                sessions.Clear();
            }
            lock (currentSync) if (ReferenceEquals(Current, this)) Current = null;
            foreach (var session in active) session.Dispose();
        }

        public void Dispose() => Logout();
    }
}
