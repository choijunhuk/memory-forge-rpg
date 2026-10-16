# Authoritative practice transport acceptance — 2026-10-17 KST

Requirements: v0.8.1§14.7/§17.2/§17.4. This unit connects the durable one-request component to real Development HTTP and restores a coherent full public read model. It does not hydrate a Run/BattleState, expose private RNG or change game rules, values, target selection or memory policy.

`PracticeServerSession` captures one ephemeral credential for each operation, verifies the configured account first, and binds new mutations to the displayed context. It persists one exact body/ID before POST; uncertain sends remain pending. Successful receipts resolve that exact request and trigger a fresh account/current-run read. Older receipts do not replace a newer or different current run or replay events. Pure battle queries remain distinct from mutations and next/after time reservations.

`PracticePublicSnapshot` preserves the complete public JSON, returns detached copies, validates critical context and rejects malformed/private data. The server journal version, battle.v and account/run memory versions retain separate meanings. `ConfirmNotCommitted` reuses exact-current identity and locking; only the narrowly proven versioned refusal path may call it. See `root_review.md` for the three corrected defects.

| Verification | Fresh result |
|---|---|
| .NET Standard2.1/C#8 source build | 0 warnings,0 errors |
| Identical NUnit on .NET10 | 83/83:66 new transport cases plus17 existing outbox cases |
| Actual Unity6000.0.46f1 Mono Play Mode | 248/248,failed0,skipped0 |
| Actual Unity Edit Mode | 3/3,failed0,skipped0 |
| Real HTTP/process restart | Real Core registered next-turn reservation; proxy dropped its200; serverSIGKILL/same endpoint; a fresh client process retried the same ID/body; exact server envelope unchanged,no duplicate AP/reservation,no old event replay |
| Actual Editor Mono HTTP | Real server query; truncated real200 retained; new session refreshed and retried same ID/body; one reservation/AP cost,unchanged server envelope,no old event replay |
| Real owner-handshake regression | RED staged despite401 before POST → GREEN NotSent,no ID/no record |
| Other checks | Node333,Python57,proposals14,document9,handoff13 |

All results refer to the imported hashes in `source_hashes.json`. Author runtime remains NOT_RUN. The official CLI returned exit0 using only Opus5.5, xhigh for the source and High for the batched concrete fixes/tests; actual scalar effort metadata and subscription/account checks are recorded in `actual_cli_metadata.json`. No paid AI/API/credits/fallback or image generation occurred.

The full Unity suite emits existing expected LogAssert quarantine diagnostics; its captured Console is retained privately. A domain-reload MCP404 was repaired by reinitializing the local MCP session. Temporary test options were restored through the Editor and original settings bytes were checked separately; test messages are not reclassified as clean runtime output. Root's HTTP probe initially omitted a required target and then confused query-only `auto` with a command target; the probe was corrected to choose a public live enemy UID. The game rules were unchanged.

Preservation: original47 protected checksums, original423-entry manifests/same29 historical differences, the protected check runner, Core/server/data and original scene/prefab/settings are retained. Source tests were not weakened. Preceding ed4ebf8's exact push/PR CI is independently green in `../practice_command_outbox_20261017/remote_ci_verified.json`; this unit's new commit CI must be checked after push.

Limits: loopback Development fixture authentication only; chunked replies are refused by the existing fixed-length protocol. This source unit is not full network UI, production authentication/account/economy/migration/deployment, physical-device/Android/IL2CPP acceptance or hardware power-loss durability. A bound, corrupt or permanently ambiguous pending request stays preserved without a generic reset. The dependent visible Unity map/battle/recovery UI is underway as a separate unit.
