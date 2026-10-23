# S4 independent full parity — frozen JS candidate 2

PASS for the exact JS and C# hashes in evidence.json. Candidate 1 remains separately frozen with the later-discovered opaque-metadata gap; it is not the final approval.

| Proof | Actual coverage |
|---|---|
| C#/JS support | 144 cases, 1,009 commands, 1,139 queries, 297 exact refusals, 2,292 complete graph comparisons |
| Authored support ults | All 10 characters × resonance 0..6 × 2 frozen growth/equipment builds = 140 cases |
| Followups | Source tokens, copy/pending echo/reservation; target/picks and refusal rollback; opaque metadata absent/present |
| Old JS graphs | 11,657 complete graph/RNG/log/event/omission witnesses replayed and matched |
| Old ordinary ults | 140 cases; 700 commands; 2,940 queries (included in old JS graph coverage) |
| Old FUP C# | 840 complete checkpoints, 700 commands, 2,940 queries; old/new witness streams byte-identical |
| CardText | 519 definitions/text rows identical; only engine source SHA differs |
| Other JS checks | 38/38 support tests, including 11 full old corpora; 8/8 baseline artifact/replay tests |

No old expectation was regenerated on disk. New fixtures use production data plus clearly isolated cloned-data token/pick variants. The trusted gauge100 inputs are unit fixtures, not earned-account evidence. Full raw battle graphs retain log/ev and RNG; the legacy trace stateHash strips log/ev and alone cannot prove this claim. Pure query and refusal comparisons include full state; JS also checks receipt-cache equality, and shared commands include exact late retries and responses.

The final FUP Core DLL is a1b72b89701e4645a7b2f130ba71e282 / 65bc74a134ee00da95c73d91e19fe8b42f855f9c8a81462b8959f9f745b801b8. All 61 source files match frozen checkpoint2. Earlier 19-group CoreChecks evidence used the author's e0816b DLL, the same source at a different build path; that evidence is recorded separately.

The remaining author full npm result is 384/396 before source-provenance pin refresh. Root owns that integration and must rerun it. provenance-pin-inventory.json names 14 SHA metadata records across 13 files; only these reviewed source references may change. Preserve semantic case/setup/request/state/log/ev/query/response/receipt/expected-hash content and formatting. Do not regenerate goldens or weaken guards. Dynamic source-hash functions remain tools/gen_*_vectors.mjs:sources(), tools/gen_traces.mjs:20, and the CardText generator's sources map.

Reproduce the packaged C#/JS fixture comparison without compiling Core (requires .NET 10):

```sh
cd /Users/choi/.codex/worktrees/memoryforge-followup/Memory_forge/reports/account_progression_20261021/s4_full_parity/candidate2
DOTNET_ROOT=/Users/choi/.dotnet DOTNET_HOST_PATH=/Users/choi/.dotnet/dotnet /Users/choi/.dotnet/dotnet build runner/SupportParity.csproj --ignore-failed-sources -p:CoreDll="$PWD/runtime/MemoryForge.Core.dll" -p:LoaderRoot="$PWD/loader"
DOTNET_ROOT=/Users/choi/.dotnet DOTNET_HOST_PATH=/Users/choi/.dotnet/dotnet /Users/choi/.dotnet/dotnet runner/bin/Debug/net10.0/SupportParity.dll fixture.json.gz /tmp/memoryforge-s4-independent-rerun.json
```

For fresh JS replay/export, use generate.mjs or compare-old-graphs.mjs with the candidate source root and exact rules-core SHA from js-source-hashes.json. Both fail closed on source mismatch. compare-old-graphs.mjs accepts the preserved baseline directory as its fourth CLI argument. The immutable before-change baseline remains /Users/choi/Documents/Memory_forge/.omx/context/account-progression-s4-baseline-20261021 (all 191 files unchanged). Never run its collector over that directory.

Logs and source manifests are included. Core, server, production JS, existing fixtures, pins and the original baseline were not edited by this lane. Server migration, live account, Unity, phone and CI acceptance are separate evidence.

SHA256SUMS freezes report inputs and outputs. Reproducible harness build directories runner/bin, runner/obj, and the duplicate uncompressed packaged-result.json are excluded from the input manifest.
