#!/usr/bin/env bash
# Runs every check in this repository and writes the evidence to reports/.
# Usage: tools/run_all_checks.sh            (needs node 22+, python 3.11+, dotnet 8+/10, PostgreSQL 16 binaries, Playwright Chromium)
#        SKIP_PG=1 SKIP_UI=1 SKIP_DOTNET=1 tools/run_all_checks.sh   to skip tools that are not installed
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; cd "$ROOT"
mkdir -p reports
declare -A RESULT
step() { local name="$1"; shift; echo "== $name"; if "$@" > "reports/$name.txt" 2>&1; then RESULT[$name]=pass; else RESULT[$name]=FAIL; fi; tail -3 "reports/$name.txt"; }
step build_data          node content/build.mjs data
step gen_vectors         node tools/gen_vectors.mjs
step engine_tests        node --test engine/tests/*.test.mjs
step review_memory       node --test proposals/tests/*.test.cjs
step review_economy      python3 tools/review/economy_audit.py
step fuzz_short          node engine/tests/fuzz_runs.mjs 100 short
step fuzz_full           node engine/tests/fuzz_runs.mjs 40 full
step balance_probe       node engine/tests/balance_probe.mjs 300
if [ -z "${SKIP_DOTNET:-}" ]; then step csharp_checks dotnet run --project core/MemoryForge.CoreChecks -c Release -- "$ROOT"; else RESULT[csharp_checks]=skipped; fi
step economy_verify      python3 tools/verify_economy.py
step python_tests        python3 -m unittest discover -s tests -v
if [ -z "${SKIP_PG:-}" ]; then step schema_check bash tools/check_schema.sh; else RESULT[schema_check]=skipped; fi
step openapi_check       python3 -c "from openapi_spec_validator import validate; from openapi_spec_validator.readers import read_from_filename; d,_=read_from_filename('server/openapi.yaml'); validate(d); print('valid, paths', len(d['paths']))"
step build_prototype     node tools/build_prototype.mjs --no-data
if [ -z "${SKIP_UI:-}" ]; then step ui_smoke node prototype/tests/smoke.mjs 4 --shots; else RESULT[ui_smoke]=skipped; fi
step gen_docs            python3 tools/build_integrated_design.py
step doc_build_run       python3 tools/check_integrated_release.py
step document_contract_tests python3 tools/audit/test_document_contract.py
{
  echo "{"
  echo "  \"generated\": \"$(date -Iseconds)\","
  PGB="$(ls -d /usr/lib/postgresql/*/bin 2>/dev/null | sort -V | tail -1)"; PGV="$( [ -n "$PGB" ] && "$PGB/postgres" --version 2>/dev/null | awk '{print $3}' )"
  echo "  \"environment\": {\"node\": \"$(node --version)\", \"python\": \"$(python3 --version 2>&1 | cut -d' ' -f2)\", \"dotnet\": \"$(dotnet --version 2>/dev/null || echo none)\", \"postgres\": \"${PGV:-none}\"},"
  echo "  \"results\": {"
  first=1; for k in build_data gen_vectors engine_tests review_memory review_economy fuzz_short fuzz_full balance_probe csharp_checks economy_verify python_tests schema_check openapi_check build_prototype ui_smoke gen_docs doc_build_run document_contract_tests; do
    [ $first = 1 ] || echo ","; first=0; printf '    "%s": "%s"' "$k" "${RESULT[$k]:-not-run}"; done
  echo ""; echo "  },"
  echo "  \"not_done\": [\"Complete Unity client/lobby/art/device acceptance\", \"server application code\", \"Unfinished-run restoration and remaining full-design C# acceptance\", \"zones 3-5 content\", \"art/audio\", \"Android device tests\", \"human playtests\", \"load tests\", \"real payments\"]"
  echo "}"
} > reports/delivery_status.json
cat reports/delivery_status.json
for k in "${!RESULT[@]}"; do [ "${RESULT[$k]}" = FAIL ] && exit 1; done; exit 0
