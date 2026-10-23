# A1 independent native source review — initial frozen snapshot

Date: 2026-10-15. Reviewer: Codex native GPT-6.1 Sol child, source-review lane.

**Verdict: REJECT the initial frozen snapshot until findings 1 and 2 are repaired and independently rechecked.** This is a source review, not A1 execution/Unity/Run acceptance. The parent is already coordinating fixes in the worktree; those later bytes are not approved by this document.

## Reviewed identity and scope

The worktree initially matched the five supplied hashes. Once the parent opened that worktree for repairs, review continued against the identical frozen copies in `/Users/choi/Documents/Memory_forge`. These root hashes were checked again before this report. Comparison baseline: root commit `3d33c4cb2e0c7cee14ced2c67c33b8fb55cef267`.

| Root-relative source | SHA-256 reviewed |
|---|---|
| `core/MemoryForge.Core/Battle.cs` | `4fdd520997b1965796ae6218f8c280d550109d8ce7ebacf1f39756b16fd5cd2e` |
| `core/MemoryForge.Core/BattleState.cs` | `bdc58054e3edcccf5fcc5f6ad6f43adb8548ad32c6d97491f50920627ec033c0` |
| `core/MemoryForge.Core/Battle.PlayerActions.cs` | `7bf70486393c40ed15bbb525e3f47fc5d04cdc10837ab3f3a92bb38a30a20f85` |
| `core/MemoryForge.Core/Battle.Effects.cs` | `65808d9af5c149ec6938d86624ac40e18a65644d249cae7f379d3a3f137502e8` |
| `core/MemoryForge.Core/ReservationBook.cs` | `49ca7cec199e4951ac7ecc9075f8238a64977c7e9131e9784c764530f0a4e23e` |

Reference: unchanged `engine/rules-core.js` SHA-256 `1de10fff56242ebfe711d0f8165adf69fd418c423b810c340a9ba0c2400fab0c`, authored `data/game_data.json` SHA-256 `6a09e6dd2d81055738c84951ea5b5ae50c25fd8c9732c8443e81ec1498625688`. Requirements: `P1-CSHARP-PLAYER-ACTIONS-BOUNDARY.md`, `P1-CSHARP-PLAYER-EXECUTION-IMPLEMENTATION.md`, original §5/6/16.2/20.9. Definitions/loader remain the reviewed A0 boundary.

## 1. P1 — discard exhaust follows a stale list after a kill reshuffles it

Location: `core/MemoryForge.Core/Battle.Effects.cs:278-279`.

The implementation stores the discard `List` once before the `exhaustPick` loop. The first `ExhaustCard` can trigger void-shard damage, a marked kill, Kaya/notebook draws and a reshuffle. `Draw` then moves that same list into the draw pile and replaces the discard list. The next iteration exhausts from the old list, which is now the draw pile. The source stores only the zone name and reads the current `s.zones[zone]` each iteration.

Anchors: `engine/rules-core.js:1355-1363` (live zone lookup), `:918-927` (exhaust hooks), `:1144-1152` (marked-kill draws), `:1453-1463` (discard-to-draw list replacement); original §6.3 `exhaustPick` and §5.13/5.15 zone/order rules.

Reproduction uses original data and real commands only:

```json
{
  "party": [{"charId":"sion"},{"charId":"kaya","res":4},{"charId":"nia"}],
  "deck": [{"id":"sion_forget"},{"id":"com_sort"},{"id":"kaya_clue"},{"id":"kaya_clue"},{"id":"kaya_clue"},{"id":"kaya_clue"}],
  "hp":243,"maxHp":243,"enemies":["e_seed","e_seed"],
  "difficulty":{"hp":50},"relics":["void_shard","detective_notebook"],"seed":1
}
```

Execute fresh-version requests with unique rids:

1. `begin {}`
2. `play {uid:"c2", picks:["c3"]}`
3. `play {uid:"c6", target:"e7"}`
4. `play {uid:"c4", target:"e8"}`
5. `play {uid:"c1", picks:["c3"]}`

Before command 5: draw `[]`, hand `[c1,c5]`, discard `[c3,c2,c6,c4]`; both seeds are marked and have HP3.

| After command 5 | Actual reference JS | Initial C# |
|---|---|---|
| Phase | `PLAYER` | `VICTORY` |
| Block | 10 | 0 |
| Draw | `[c6]` | `[]` |
| Exhaust | `[c3]` | `[c3,c6]` |
| Killed marked | 1 | 2 |

The reviewer reproduced the JS result without data/state mutation. The parent separately executed the exact five C# requests: steps 1–4 matched full state/response; step 5 failed at phase and reported the C# column above. Evidence: `exhaust_live_zone_repro.json` and `exhaust_live_zone_red.txt` in this report directory. That C# execution belongs to the parent, not this child.

Minimal repair: resolve `s.Zones.Discard` or `s.Zones.Hand` inside each loop iteration before calling `TakePick`; do not retain a mutable zone list across exhaust hooks. Keep the exact command sequence as a full-state/response regression, including RNG, rewards and diagnostics.

## 2. P2 — eager picks decoding overrides earlier command errors

Location: `core/MemoryForge.Core/Battle.PlayerActions.cs:33-43`, preceding the guards at `:47-64` and `:144-151`.

`PlayArguments`/`UltArguments` evaluate `ArgumentPicks` before entering `Play`/`Ult`. A truthy boolean or non-list object throws `INVALID_COMMAND_ARGS` immediately, so the source phase/mode/hand/AP/target precedence is bypassed. The receipt/stale/candidate shell remains intact; this finding concerns the reported rule error.

Minimal source reproduction: fresh setup with party `sera`, deck `[sera_round]`, enemy `e_moth`, HP/maxHP81, seed0. Send `{rid:"r",v:0,kind:"play",args:{uid:"c1",picks:true}}` before begin. Live JS returns `WRONG_PHASE`; the frozen C# call order necessarily returns `INVALID_COMMAND_ARGS`. After a successful begin, `mode:"bad",picks:true` must return `INVALID_MODE` before interpreting picks. An ult before begin likewise must report `WRONG_PHASE` first.

Anchors: `engine/rules-core.js:453-467`, `:703-721`, `:803-814`; original §5.4's first-failure ordering and §5.15. The reviewer ran the fresh JS error case. The C# conclusion is from the frozen call order; this child did not run C#.

Minimal repair: defer picks validation until the source's preceding guards have executed, while retaining the original immutable argument tree for receipt identity and preserving falsey/default picks behavior. Regressions should compare both the exact error and unchanged complete state/RNG/log/events/version/receipts.

## Other source checks and limits

- Read every changed execution branch against the actual JS: precheck/check, commit/resolve/fizzle/cleanup, ult contexts, debt/reservation shared sequence and due ordering, powers/buffs, all 39 op switch tags, raw/card damage separation, per-hit terminal boundaries, growth/lineage, copies/choices/RNG, required event nulls, state deep copies and the narrow shared reservation-capacity helper.
- A read-only inventory found 39 authored tags and 39 executor tags, with no missing/extra tags. This proves switch coverage only, not effect observability or runtime parity.
- No additional reproducible source defect was found in the reviewed hashes. The single context/executor and approved partial-file separation reuse existing state/helpers; no alternate queue, JS bridge or speculative replacement engine was introduced.
- The original base0 conflict remains unresolved as instructed. Live `sion_forget` base5 is preserved; this review proposes no retune and does not mark the generic clause fulfilled.
- Published numeric/transport limits, proposal-only late actions/phase exclusions and no restoration/full Run acceptance remain explicit. A0 and earlier-unit PASS results do not establish A1 command parity.

This child ran read-only source inspection, source/reference Node probes and the tag inventory. It did not edit product code, build/run C#, execute the full oracle, call Claude/other agents/APIs or operate Unity. Only this review document was added. Re-review patched source hashes, complete fresh actual-command checks, official Opus review and the parent's separate Unity/integration gates before declaring A1 accepted.
