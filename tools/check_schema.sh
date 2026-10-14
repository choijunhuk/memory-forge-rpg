#!/usr/bin/env bash
# Creates a throwaway PostgreSQL cluster, applies server/schema.sql and runs server/tests/schema_checks.sql.
# Requires PostgreSQL 16 binaries (initdb, pg_ctl, psql). Usage: tools/check_schema.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PGBIN="${PGBIN:-$(ls -d /usr/lib/postgresql/*/bin 2>/dev/null | sort -V | tail -1)}"
TMP="$(mktemp -d)"
trap '"$PGBIN/pg_ctl" -D "$TMP/data" -m immediate stop >/dev/null 2>&1 || true; rm -rf "$TMP"' EXIT
RUNAS=""
if [ "$(id -u)" = "0" ]; then id postgres >/dev/null 2>&1 || useradd -m postgres; chown -R postgres "$TMP"; RUNAS="runuser -u postgres --"; fi
$RUNAS "$PGBIN/initdb" -D "$TMP/data" -A trust -U mf >/dev/null
$RUNAS "$PGBIN/pg_ctl" -D "$TMP/data" -o "-k $TMP -p 55432 -c listen_addresses=''" -l "$TMP/log" start >/dev/null
for i in $(seq 1 30); do $RUNAS "$PGBIN/pg_isready" -h "$TMP" -p 55432 >/dev/null 2>&1 && break; sleep 0.3; done
$RUNAS "$PGBIN/psql" -h "$TMP" -p 55432 -U mf -d postgres -v ON_ERROR_STOP=1 -q -c "CREATE DATABASE mf_check" 
# files are fed on stdin so the postgres OS user never needs read access to the repository
$RUNAS "$PGBIN/psql" -h "$TMP" -p 55432 -U mf -d mf_check -v ON_ERROR_STOP=1 -q < "$ROOT/server/schema.sql"
$RUNAS "$PGBIN/psql" -h "$TMP" -p 55432 -U mf -d mf_check -v ON_ERROR_STOP=1 -q -t < "$ROOT/server/tests/schema_checks.sql" | grep -v '^\s*$'
echo "PostgreSQL $("$PGBIN/postgres" --version | awk '{print $3}'): schema applied, checks passed"
