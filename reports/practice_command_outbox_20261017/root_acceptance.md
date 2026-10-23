# One durable exact pending request — Root acceptance, 2026-10-17 KST

Requirement §14.7/§17.2/§17.4: sending a mutation and losing its response must not create a new request or a duplicate gain after restart. This unit implements the durable pending component used by the next Development Unity transport. It does not wire the existing local practice UI to the server.

`PracticeCommandOutbox` keeps one immutable endpoint/account/route/exact-body/requestId/version record. Stage flushes and publishes it before returning anything sendable; equal retries preserve bytes, conflicts refuse. Load and Confirm use the permanent checked native lock. Unknown/corrupt/binding-mismatched records are preserved and refused. Only an exact record with a successful response validated by its owning transport may be confirmed; timeout/disconnect/503/current-state GET/focus/pause never clear it. The component never reads Core or stores a bearer token. Existing unsent prepared input and actual next/after reservation/AP rules remain separate and unchanged.

## Review, corrections and actual execution

Official Opus authored the component and tests; Codex independently reviewed and executed them. The first Max call was deliberately interrupted by Root after actual Mono→Mono measurements invalidated its FileShare.None-only premise, before source edits. The FAILED/error_during_execution result is preserved and is not a quota/model/billing bypass. Same-session xhigh source/tests/fixes and High's one-line test-gate fix returned exit0. `actual_cli_metadata.json` records actual Opus model and per-turn efforts; included subscription auth/account match and the valid creditsOFF/autoreloadOFF UI attestation preceded every call. No API/credits/other model/paid fallback or image work.

The source correction and test correction received second independent approval (`root_review.md`). Author runtime is NOT_RUN; below are Root executions against the reviewed hashes.

| Check | Result |
|---|---|
| Actual identical NUnit on .NET10 | original16/17 RED → final17/17 GREEN, same17 expectations plus the race readiness guard; netstandard2.1/C#8 builds0warnings/errors |
| Actual Unity6000.0.46f1 Mono | original new fixture16/17 RED; final fullPlay182/182 and Edit3/3, failure0/skip0; original settings restored exactly |
| Actual separate client OS processes + server HTTP |25/25: unread successful create and reservation replies, serverSIGKILL/same-endpoint restart, client re-load, exact same ID/body/receipt, complete durable bytes unchanged on retries, one AP cost/one reservation |
| Checked native locking | actual EditorMono, separate bundledMono6.13 and.NET10 refuse contention, then reacquire after close/SIGKILL; same permanent inode |
| Real failure IO | preview owned-directory0500 prevents sendable publish; confirmed unlink failure preserves pending; external Mono holder blocks component Load/Stage/Confirm; afterSIGKILL correct re-load/confirm. Final identical NUnit also exercises owned permission failures |
| New private records | actual Mac and unprivileged Linux arm64.NET Stage/reload, mode0600; no process umask change |
| Actual old writer→fixed reader | old A0 binary records escaped lone high/low; fixed A1 Load refuses OUTBOX_RECORD_CORRUPT and preserves original bytes on Linux |
| JS/Python/proposals | fresh333/57/14 PASS |
| Docs/handoff | PASS9/13; final adjacent logs, separate from runtime/device acceptance |
| Preservation | original47, all preceding public-query source hashes, protected runner, existing scene/prefab/GUIDs and settings preserved; no input manifest regeneration |

The full Unity suite intentionally produces expected existing LogAssert quarantine/restart diagnostics. Those were preserved and cleared before a fresh live-scene Console check; the clean compilation and live scene are separate from those test messages. A fresh native build, physical-device/Android/IL2CPP and hardware power failure were not checked.

## Scope still pending

No full Unity HTTP sender, public DTO rendering, stale-response application guard, definitive-server-refusal resolution, process-termination recovery of the complete UI or public event history is implemented here. The next transport must validate the exact attempt's response and handle known-not-committed refusals separately from uncertain commits; it must not hydrate a fake Run/RNG or apply an older receipt over a newer state. Only macOS/Linux libc locking was exercised; Android/IL2CPP/Windows are not approved. The directory entry is not fsynced, so the process-crash proof is not hardware power-loss durability. No migration/reset/pruning or old-record chmod was added.

Existing v0.8.1/rules0.7.0 numbers,47protected inputs, original manifests and1320/32 versus5280/21 balance boundaries remain intact. The immediately preceding public-query commit3ec042e has independently verified push/PR CI success (`../public_battle_query_20261017/remote_ci_verified.json`); this unit's new commit CI is checked after push. Production account/auth/economy/schema/deployment are separate.
