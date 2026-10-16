#!/usr/bin/env bash
# A new disposable native PostgreSQL16 cluster; never the service DB or an existing volume.
# macOS without PG16 can run ServerChecks explicitly against its own Docker test DB instead.
set -euo pipefail
umask 077
MF_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MF_PGBIN="${PGBIN:-/usr/lib/postgresql/16/bin}"
if [ "$(id -u)" = 0 ]; then echo 'Practice PostgreSQL checks require an unprivileged user.' >&2; exit 2; fi
if [ ! -x "$MF_PGBIN/initdb" ] || ! "$MF_PGBIN/postgres" --version | awk '{print $3}' | grep -q '^16\.'; then
  echo 'PostgreSQL16 binaries are required; no database checks were run.' >&2; exit 2
fi
MF_TMP="$(mktemp -d)"
MF_SUCCESS=0
mf_cleanup() {
  "$MF_PGBIN/pg_ctl" -D "$MF_TMP/data" -m immediate stop >/dev/null 2>&1 || true
  if [ "$MF_SUCCESS" = 1 ]; then rm -rf "$MF_TMP"; else
    echo "Private test diagnostics retained in $MF_TMP (may contain test credentials; do not publish)." >&2
  fi
}
trap mf_cleanup EXIT
trap 'echo "Practice PostgreSQL checks failed at line $LINENO; see private diagnostics." >&2' ERR
python3 - "$MF_TMP" "$MF_PGBIN" <<'PY'
from pathlib import Path
import secrets, shlex, socket, sys
directory, binaries = Path(sys.argv[1]), Path(sys.argv[2])
directory.chmod(0o700)
with socket.socket() as available:
    available.bind(('127.0.0.1', 0))
    port = available.getsockname()[1]
admin, runtime = secrets.token_hex(32), secrets.token_hex(32)
files = {
    'port': str(port), 'admin-password': admin,
    'pgpass': f'127.0.0.1:{port}:*:mf_admin:{admin}\n',
    'create-role.sql': f"CREATE ROLE mf_runtime LOGIN PASSWORD '{runtime}';\n",
    'runtime.txt': f'Host=127.0.0.1;Port={port};Database=mf_practice_checks;Username=mf_runtime;Password={runtime};SSL Mode=Disable',
}
for name, text in files.items():
    path = directory / name
    path.write_text(text)
    path.chmod(0o600)
oracle = directory / 'psql-oracle'
oracle.write_text('#!/bin/sh\nexport PGPASSFILE=' + shlex.quote(str(directory / 'pgpass'))
    + '\nexec ' + shlex.quote(str(binaries / 'psql')) + ' -h 127.0.0.1 -p ' + str(port)
    + ' -U mf_admin -d mf_practice_checks "$@"\n')
oracle.chmod(0o700)
PY
MF_PORT="$(cat "$MF_TMP/port")"
"$MF_PGBIN/initdb" -D "$MF_TMP/data" -A scram-sha-256 -U mf_admin --pwfile="$MF_TMP/admin-password" >"$MF_TMP/setup.log" 2>&1
mkdir "$MF_TMP/socket"
"$MF_PGBIN/pg_ctl" -D "$MF_TMP/data" -o "-h 127.0.0.1 -p $MF_PORT -k $MF_TMP/socket -c fsync=on -c synchronous_commit=on -c full_page_writes=on" -l "$MF_TMP/postgres.log" start >>"$MF_TMP/setup.log" 2>&1
PGPASSFILE="$MF_TMP/pgpass" "$MF_PGBIN/psql" -h 127.0.0.1 -p "$MF_PORT" -U mf_admin -d postgres -Xq -v ON_ERROR_STOP=1 -c 'CREATE DATABASE mf_practice_checks' >>"$MF_TMP/setup.log" 2>&1
"$MF_TMP/psql-oracle" -Xq -v ON_ERROR_STOP=1 <"$MF_TMP/create-role.sql" >>"$MF_TMP/setup.log" 2>&1
"$MF_TMP/psql-oracle" -Xq -v ON_ERROR_STOP=1 <"$MF_ROOT/server/schema.sql" >>"$MF_TMP/setup.log" 2>&1
"$MF_TMP/psql-oracle" -Xq -v ON_ERROR_STOP=1 <"$MF_ROOT/server/dev_practice_schema.sql" >>"$MF_TMP/setup.log" 2>&1
"$MF_TMP/psql-oracle" -Xq -v ON_ERROR_STOP=1 >>"$MF_TMP/setup.log" 2>&1 <<'SQL'
GRANT CONNECT ON DATABASE mf_practice_checks TO mf_runtime;
GRANT USAGE ON SCHEMA dev_practice TO mf_runtime;
GRANT SELECT ON dev_practice.schema_marker TO mf_runtime;
GRANT SELECT, INSERT, UPDATE ON dev_practice.account_state TO mf_runtime;
SQL
dotnet run --project "$MF_ROOT/server/MemoryForge.ServerChecks" -- "$MF_ROOT" --postgres-file "$MF_TMP/runtime.txt" --psql "$MF_TMP/psql-oracle"
MF_SUCCESS=1
