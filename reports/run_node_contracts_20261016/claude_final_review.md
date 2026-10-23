# Independent final review: Root-authored terminal guard and added regressions

- Task: `P1-RUN-FINAL-COVERAGE-AND-REVIEW` (Claude Code worker, Opus 5.5; xhigh was requested, but this worker did not check the effort setting itself.)
- Scope: I read the source and edited only `engine/tests/run_node_contracts.test.mjs` and this file. Engine, fixture, generator and data were read-only.
- Execution: **NOT_RUN.** This session had no Bash, so I ran no test and recomputed no SHA. The engine SHA `dae99192…ab4` is Root's claim. I reviewed the working-tree `engine/rules-core.js` as I found it.
- session_id: this worker cannot see it. Root has it in the CLI metadata.

## 1. Verdict: APPROVE (no actionable blocker)

Reviewed source: `engine/rules-core.js:1637-1651` (`enter`), `:1661-1673` (`startBattle`), `:1674-1680` (`battleCmd`), `:1681-1700` (`afterBattle`).

```js
this.log(`${NODE_NAME[n.type]} 진입`);
if (this.battle && this.battle.isOver()) this.afterBattle(); // :1650
```

Evidence:

- **Callers.** In the engine, `startBattle` is called only from `Run.enter` (`:1642`). `afterBattle` is called only from `enter` (`:1650`) and `battleCmd` (`:1678`). `tools/gen_traces.mjs` dispatches only public Run methods plus `battleCmd` (`runMethods`, `:40-41`, `:76-90`), and `startBattle` is not among them. `prototype/index.html:2051-2093`, `review/baseline/**` and `review/sources/**` hold separate copies of the engine and do not call this one.
- **The guard can only fire for a battle that the same `enter` just started.**
  - `enter` requires `available()`, which is empty outside `MAP` (`:1630`).
  - Both `afterBattle` exits clear `this.battle` and `s.battle` (`:1686`, `:1699`). `startBattle` sets phase `BATTLE`.
  - So no public-command path reaches `MAP` with a live `this.battle`, and rest/forge/shop/event entries cannot trigger it.
  - The only way in would be an inconsistent raw snapshot (`MAP` plus a non-null `s.battle`). Snapshot restore policy is out of scope, and no legitimate save had that shape before or after the fix.
- **One finalizer run.** `isOver()` only reads the phase (`:684`), so `begin` is not run again. Once `afterBattle` runs, `this.battle` is null, so a later `battleCmd` returns `NO_BATTLE` and cannot finalize a second time.
- **Matches the spec.**
  - §5.3 step 5 says `infinite_circuit` costs 2 HP and the battle ends if the party falls. §5.14 says HP 0 is a defeat.
  - §7.6 and §8.5 say a defeat leads to a save that keeps at most one change (`DEFEAT_KEEP_ONE`). v2 follows the §8.11 order through `captureMemory` → `commitMemoryV2`.
  - Defeat draws no Run RNG and pays no reward or stats (`:1686` returns before `:1687`).
  - The log reads `… 진입`, then `패배`.
  - The pending `nextApBonus` was already zeroed in the Run and in the battle copy before `begin` (`:1665-1667`), so finalizing cannot refund it.
- **Placement is correct on the error path; my earlier proposal was not.** If `Battle.create` throws inside `startBattle`, the line after the `switch` never runs, so the original "no entry log on setup failure" behaviour is kept. My earlier proposal (move the log before the `switch` / finalize inside `startBattle`) would have added a `… 진입` line on that failure. Root was right not to copy it. The new third test pins this.
- **A battle won inside `begin` takes the same path.** That path is the existing victory branch (coins, rewards and `REWARD`, with Run RNG drawn in the same order as `battleCmd`). It is consistent, but no real witness covers it (see §3).

## 2. Non-blocking notes (P3, no change made)

1. **Error type for the missing boss differs from the brief.** The brief says `bosses:['missing']` throws a native TypeError. The source actually throws `RuleError('UNKNOWN_ENEMY', 'missing')` from `Battle.addEnemy` (`:322-324`), inside `Battle.create`, before `begin`. The new test asserts that real error.
   - This is a pre-existing case where a RuleError leaves state changed: `pos`, `floor` and one Run RNG draw (`:1640`, `:1664`). The test's `attempt()` helper, and the generator's rule that legal rejections leave no mutation, would both flag it.
   - The generator treats `UNKNOWN_ENEMY` as fatal (it is not in `legalRejections`), so the corpora are not affected.
   - This matches the partial-mutation note in `ops/ai/tasks/P1-CSHARP-RUN-PROGRESSION-BOUNDARY.md:66`. The guard did not introduce it.
2. **Generator help text.** `tools/gen_traces.mjs:314` says "enter includes begin". It could add "and the Run finalizer when begin ends the battle", so the C# consumer copies the order: entry log, then the finalizer.
3. **C# R1.** C# `Run.Enter` must keep the same order. The C# Run is not implemented yet, so no parity is claimed.
4. **HTML prototype.** `prototype/index.html:2051-2093` has its own inline Run. It still lacks this guard and the battle-copy `nextApBonus: 0` fix. HTML functional-reference results differ from `engine/rules-core.js` for these cases.
5. **UI/UX.** `prototype/src/ui.js:336` sends `DEFEAT` straight to the save screen. After the guard, a defeat inside `begin` goes from the map to the save screen without showing the battle. The cause (`infinite_circuit` −2, HP 1 → 0) lives only in the finalized Battle's `ev`, and that Battle is discarded. Keeping a defeat-cause summary would be a UI decision; I am not proposing it as an engine change.

## 3. Added regressions (the 9 existing tests are unchanged)

All are **NOT_RUN** here. Every one starts from real `Run.create`, uses only public commands or legitimate generator/setup options, and does not patch live fields.

| # | Test | What it pins |
|---|---|---|
| 10 | `seed2654 packet …` | Pins the packet's provenance: engine `dae…`, seed 2654 full legacy, 1525 commands, every outcome reproduced.<br>Base AP is `rules.apPerTurn` (4). `ev_rift` choice 0 is the only `nextApBonus` source in the data.<br>Two distinct pendings (identity checked against their own node entries) each resolve once. Every repeated choice and the recorded command return `BAD_CHOICE`, and the full Run JSON is unchanged, checked per rejection and against a plain replay.<br>Across all 1525 commands the Run flag changes exactly `[[78,0,2],[80,2,0],[1519,0,2],[1524,2,0]]`.<br>The battle entries after each event are listed completely and match the observations: AP6 first, then eight battles at AP4; AP6 again after the second event. The flag in the Run, the flag in the battle copy, and `apNext` are all 0. Neither `chrono_crown` nor `cracked_hourglass` is held. |
| 11, 12 | `seed1102 generated legacy/v2 trace …` | Real `generateScenario` → `replayScenario` final state. The data SHA equals the frozen `sourceData`.<br>The last command pair is enter then `commitMemory`. Last node `z2f3n1`. Command kinds and outcomes match the frozen witness; for legacy, the setup and all 221 steps match exactly.<br>My live replay matches the generator's state hashes before the final entry, after it, and at the end.<br>Before the final entry: `MAP`, HP 1, `infinite_circuit` held. The finalizer runs once, on a battle that lost 2 HP to a relic before drawing (turn 1, empty hand).<br>After the entry: `DEFEAT`, HP 0, a defeat result, the save pending, no battle left, and the log ends entry then `패배`. v2 has `memoryAfter`; legacy does not.<br>Selecting two changes is refused (`DEFEAT_KEEP_ONE`) with no change to the run. The recorded save then reaches `DONE`.<br>**v2:** the input store is unchanged; the result store goes version 0 → 1 with one history entry and one receipt. `listChanges(before, stored)` equals the 0 or 1 selected change.<br>**Legacy:** no store, final memory `null`. |
| 13 | `seed1102: a fixed boss that throws …` | Replays the frozen prefix up to the first `z1boss` with only `bosses:['missing']` added. `enter` throws `UNKNOWN_ENEMY`, the log is byte-identical, and the run is still `MAP` with no battle. It deliberately does not assert that `pos`, `floor` or RNG roll back, because they never did. |

## 4. Remaining gaps (not covered)

- No real witness for a battle **won** inside `begin`.
- Saves already deadlocked by the original defect (`BATTLE` phase with a finished battle) are still not recoverable through public commands. Raw-snapshot restore policy is not addressed.
- No C# Run or server/account semantics.
- The atomicity of `enter` when setup fails (§2.1) is unchanged.
- The `UNKNOWN_ENEMY` behaviour is pinned only with the existing setup option. No validation policy was added.

## Root receipt and reference correction

The reviewed A worktree HTML copy was historical. Root had already rebuilt its own prototype/index.html from the exact dae99192 engine; an exact engine-substring assertion confirms all four guards are embedded. A's stale inline-engine note is not a current Root blocker. Source-specific UNKNOWN_ENEMY and infinite_circuit corrections are accepted. Root is executing the13tests and final full checks; the worker's NOT_RUN statements remain accurate.
