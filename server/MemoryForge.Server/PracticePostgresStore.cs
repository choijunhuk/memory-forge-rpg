// P1-POSTGRES-TRANSACTIONAL-PRACTICE §17.2/§17.4/§17.5/§14.7: the explicit PostgreSQL backend of the LOCAL DEVELOPMENT practice host
// (MemoryForge:PracticeBackend=postgres). It stores the exact Envelopes.Encode bytes of each authenticated fixture alias as one opaque bytea
// row in the Development-only dev_practice.account_state table (server/dev_practice_schema.sql, applied by an administrator; this host runs
// no DDL). Every decision — binding, receipts before versions, full-state replay, the isolated command, encode and size guard — is the one
// shared PracticeRunStore callback; this file only adds the transaction around it:
//   BEGIN (READ COMMITTED) → bounded lock/statement/idle timeouts → INSERT a fresh valid envelope ON CONFLICT DO NOTHING → SELECT that one
//   row FOR UPDATE → decide → UPDATE the row → COMMIT → reply.
// Concurrent first mutations serialize on the primary key; anything that fails before COMMIT rolls back, the placeholder insert included,
// so a refused request never creates a row and consumes no requestId, version or memory. A failure during COMMIT is reported as
// 503 STORE_COMMIT_UNKNOWN (never as a rollback): only a retry with the same requestId resolves it. A lock wait over lock_timeout is
// 503 ACCOUNT_BUSY; any other database failure is a fixed 503 STORE_UNAVAILABLE with no connection or server text. There is no fallback to
// the file store. Reads are one SELECT (one atomic row) and never insert. Durability rests on PostgreSQL's commit with fsync and
// synchronous_commit verified on at startup; this slice makes no power-loss or hardware claim of its own.
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using MemoryForge.Core;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace MemoryForge.Server
{
    /// <summary>Startup validation of MemoryForge:PracticePostgresFile: an absolute, owner-only, non-symlink file outside the content root
    /// (repository) that holds only a connection string with exactly one loopback-IP or local Unix-socket host and explicit database,
    /// username and password. Only those keys (plus port and ssl mode) are accepted; logging, error detail and enlistment are forced off and
    /// timeouts are fixed. Refusals are fixed text: no path contents, connection values or secrets.</summary>
    static class PostgresSettings
    {
        const UnixFileMode GroupOrOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        static readonly HashSet<string> AllowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "host", "server", "port", "database", "db", "username", "user name", "userid", "user id", "uid", "password", "pwd", "psw", "ssl mode", "sslmode"
        };

        public static string ConnectionString(string file, string contentRoot)
        {
            string text = ReadPrivateFile(file, contentRoot);
            var raw = new DbConnectionStringBuilder();
            try { raw.ConnectionString = text; }
            catch (Exception) { throw new StartupRefusal("MemoryForge:PracticePostgresFile does not hold a connection string"); }
            foreach (object key in raw.Keys)
                if (!(key is string name) || !AllowedKeys.Contains(name.Trim()))
                    throw new StartupRefusal("the PostgreSQL connection file may only set host, port, database, username, password and ssl mode");
            NpgsqlConnectionStringBuilder builder;
            try { builder = new NpgsqlConnectionStringBuilder(text); }
            catch (Exception) { throw new StartupRefusal("the PostgreSQL connection file is not a valid Npgsql connection string"); }
            string host = builder.Host ?? "";
            if (host.Length == 0 || host.Contains(',')) throw new StartupRefusal("the PostgreSQL connection file must name exactly one database host");
            bool socket = host.StartsWith('/') && Path.IsPathFullyQualified(host);
            bool loopback = IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
            if (!socket && !loopback) throw new StartupRefusal("the PostgreSQL host must be a loopback IP address or a local Unix socket directory");
            if (string.IsNullOrEmpty(builder.Database) || string.IsNullOrEmpty(builder.Username) || string.IsNullOrEmpty(builder.Password))
                throw new StartupRefusal("the PostgreSQL connection file must set database, username and password explicitly");
            builder.IncludeErrorDetail = false;
            builder.LogParameters = false;
            builder.Enlist = false;
            builder.ApplicationName = "memoryforge-dev-practice";
            builder.Timeout = 5;
            builder.CommandTimeout = 30;
            return builder.ConnectionString;
        }

        static string ReadPrivateFile(string file, string contentRoot)
        {
            if (PracticeRunStore.IsInside(file, contentRoot)) throw new StartupRefusal("the PostgreSQL connection file must be outside the content root (repository)");
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists) throw new StartupRefusal("the PostgreSQL connection file does not exist");
                if (info.LinkTarget != null) throw new StartupRefusal("the PostgreSQL connection file must not be a symbolic link");
                if (info.Length > 4096) throw new StartupRefusal("the PostgreSQL connection file is larger than 4 KiB");
                if ((File.GetUnixFileMode(file) & GroupOrOther) != 0) throw new StartupRefusal("the PostgreSQL connection file must not be accessible to group/other (chmod 600)");
                return File.ReadAllText(file).Trim();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { throw new StartupRefusal("the PostgreSQL connection file is not readable"); }
        }
    }

    sealed class PostgresEnvelopePersistence : IEnvelopePersistence, IAsyncDisposable
    {
        public const string MarkerPurpose = "development-practice", MarkerFormat = "mf-dev-practice-pg-1";
        const string VerifySql = "SELECT current_setting('server_version_num'), current_setting('fsync'), current_setting('synchronous_commit'), current_setting('full_page_writes'), "
            + "(SELECT count(*) FROM dev_practice.schema_marker WHERE purpose = $1 AND format = $2), "
            + "has_table_privilege('dev_practice.account_state', 'SELECT'), has_table_privilege('dev_practice.account_state', 'INSERT'), has_table_privilege('dev_practice.account_state', 'UPDATE')";
        const string ReadSql = "SELECT envelope FROM dev_practice.account_state WHERE account_alias = $1";
        const string LimitsSql = "SELECT set_config('lock_timeout', '5s', true), set_config('statement_timeout', '30s', true), set_config('idle_in_transaction_session_timeout', '60s', true)";
        const string InsertSql = "INSERT INTO dev_practice.account_state (account_alias, envelope) VALUES ($1, $2) ON CONFLICT (account_alias) DO NOTHING RETURNING 1";
        const string LockSql = "SELECT envelope FROM dev_practice.account_state WHERE account_alias = $1 FOR UPDATE";
        const string UpdateSql = "UPDATE dev_practice.account_state SET envelope = $2 WHERE account_alias = $1";
        static readonly HashSet<string> DurableSynchronousCommit = new HashSet<string>(StringComparer.Ordinal) { "on", "remote_apply", "remote_write", "local" };
        readonly NpgsqlDataSource source;
        ILogger? log;

        PostgresEnvelopePersistence(NpgsqlDataSource source) { this.source = source; }

        /// <summary>One data source per host, verified before the host listens: PostgreSQL 16, fsync/full_page_writes on, synchronous_commit not
        /// off, the dev_practice schema marker present, and SELECT/INSERT/UPDATE on the account table. Any failure is a fixed refusal.</summary>
        public static async Task<PostgresEnvelopePersistence> OpenAsync(string connectionString)
        {
            NpgsqlDataSource source;
            try { source = NpgsqlDataSource.Create(connectionString); }
            catch (Exception) { throw new StartupRefusal("the PostgreSQL connection settings were not accepted by the driver (no details are shown)"); }
            try
            {
                await Verify(source);
                return new PostgresEnvelopePersistence(source);
            }
            catch
            {
                await source.DisposeAsync();
                throw;
            }
        }

        /// <summary>Store failures are logged as operation, exception type and SQLSTATE only (never messages, SQL, parameters or credentials).</summary>
        public void UseLogger(ILogger logger) => log = logger;

        public ValueTask DisposeAsync() => source.DisposeAsync();

        static async Task Verify(NpgsqlDataSource source)
        {
            int version; string fsync, synchronousCommit, fullPageWrites; long markers; bool canSelect, canInsert, canUpdate;
            try
            {
                await using var connection = await source.OpenConnectionAsync();
                await using var command = Command(connection, null, VerifySql, Text(MarkerPurpose), Text(MarkerFormat));
                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) throw new InvalidDataException("no verification row");
                version = int.Parse(reader.GetString(0), NumberStyles.None, CultureInfo.InvariantCulture);
                fsync = reader.GetString(1); synchronousCommit = reader.GetString(2); fullPageWrites = reader.GetString(3);
                markers = reader.GetInt64(4); canSelect = reader.GetBoolean(5); canInsert = reader.GetBoolean(6); canUpdate = reader.GetBoolean(7);
            }
            catch (Exception)
            {
                throw new StartupRefusal("the PostgreSQL development store is unreachable, refused the credential, or lacks the dev_practice schema (no details are shown)");
            }
            if (version < 160000 || version >= 170000) throw new StartupRefusal("the PostgreSQL development store must be PostgreSQL 16");
            if (fsync != "on" || fullPageWrites != "on" || !DurableSynchronousCommit.Contains(synchronousCommit))
                throw new StartupRefusal("the PostgreSQL development store must run with fsync and full_page_writes on and synchronous_commit not off");
            if (markers != 1) throw new StartupRefusal("the PostgreSQL database lacks the dev_practice schema marker for this format");
            if (!canSelect || !canInsert || !canUpdate) throw new StartupRefusal("the PostgreSQL role needs SELECT, INSERT and UPDATE on dev_practice.account_state");
        }

        /// <summary>One SELECT of one row: an atomic committed snapshot that never inserts or locks.</summary>
        public async Task<byte[]?> ReadAsync(PracticeAccount account)
        {
            try
            {
                await using var connection = await source.OpenConnectionAsync();
                await using var command = Command(connection, null, ReadSql, Text(account.Id));
                await using var reader = await command.ExecuteReaderAsync();
                return await reader.ReadAsync() ? reader.GetFieldValue<byte[]>(0) : null;
            }
            catch (Exception error) when (IsStoreFailure(error))
            {
                Diagnose("read", error);
                throw new ApiError(503, "STORE_UNAVAILABLE");
            }
        }

        public async Task<Reply> TransactAsync(PracticeAccount account, byte[] freshEnvelope, Func<byte[]?, Transition> decide)
        {
            NpgsqlConnection? connection = null;
            NpgsqlTransaction? transaction = null;
            var stage = Stage.Lock;
            try
            {
                connection = await source.OpenConnectionAsync();
                transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                await using (var limits = Command(connection, transaction, LimitsSql)) await limits.ExecuteNonQueryAsync();
                bool inserted;
                await using (var insert = Command(connection, transaction, InsertSql, Text(account.Id), Bytes(freshEnvelope))) inserted = await insert.ExecuteScalarAsync() != null;
                byte[] committed;
                await using (var select = Command(connection, transaction, LockSql, Text(account.Id)))
                await using (var reader = await select.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync()) throw new InvalidOperationException("the locked account row is missing");
                    committed = reader.GetFieldValue<byte[]>(0);
                }
                stage = Stage.Decide;
                // null when this transaction created the row: the same "no envelope yet" the file backend reports for a missing file.
                var transition = decide(inserted ? null : committed);
                // A stored receipt (or any decision without new bytes) commits nothing; disposal below rolls the transaction back.
                if (transition.NewEnvelope == null) return transition.Reply;
                stage = Stage.Write;
                int updated;
                await using (var update = Command(connection, transaction, UpdateSql, Text(account.Id), Bytes(transition.NewEnvelope))) updated = await update.ExecuteNonQueryAsync();
                if (updated != 1) throw new InvalidOperationException("the locked account row was not updated");
                stage = Stage.Commit;
                await transaction.CommitAsync();
                return transition.Reply;
            }
            catch (Exception error) when (stage == Stage.Commit)
            {
                // COMMIT may have landed: never report a rollback. The same requestId resolves it (receipt or a single execution).
                Diagnose("commit", error);
                throw new ApiError(503, "STORE_COMMIT_UNKNOWN");
            }
            catch (PostgresException error) when (stage == Stage.Lock && error.SqlState == "55P03")
            {
                throw new ApiError(503, "ACCOUNT_BUSY");
            }
            catch (Exception error) when (stage != Stage.Decide && IsStoreFailure(error))
            {
                Diagnose(stage == Stage.Lock ? "lock" : "write", error);
                throw new ApiError(503, "STORE_UNAVAILABLE");
            }
            finally
            {
                // Disposing an uncommitted transaction rolls it back (the placeholder insert included); a broken connection rolls back server-side.
                if (transaction != null) { try { await transaction.DisposeAsync(); } catch (Exception error) when (IsStoreFailure(error)) { } }
                if (connection != null) { try { await connection.DisposeAsync(); } catch (Exception error) when (IsStoreFailure(error)) { } }
            }
        }

        enum Stage { Lock, Decide, Write, Commit }

        static bool IsStoreFailure(Exception error) => error is NpgsqlException || error is TimeoutException || error is IOException || error is InvalidOperationException && error.Source == "Npgsql";
        void Diagnose(string operation, Exception error) =>
            log?.LogWarning("PostgreSQL practice store {Operation} failed ({ExceptionType}, SQLSTATE {SqlState})", operation, error.GetType().Name, (error as PostgresException)?.SqlState ?? "none");
        static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, params NpgsqlParameter[] parameters)
        {
            var command = new NpgsqlCommand(sql, connection, transaction);
            foreach (var parameter in parameters) command.Parameters.Add(parameter);
            return command;
        }
        static NpgsqlParameter Text(string value) => new NpgsqlParameter { Value = value, NpgsqlDbType = NpgsqlDbType.Text };
        static NpgsqlParameter Bytes(byte[] value) => new NpgsqlParameter { Value = value, NpgsqlDbType = NpgsqlDbType.Bytea };
    }
}
