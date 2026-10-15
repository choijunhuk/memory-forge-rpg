# Opposite-author review — P1-R1B-UNITY-PATCH-REVIEW

- Reviewer: Claude Code, Opus 5.5 (`claude-opus-5-5`), requested effort High, included subscription only. This was a source-only review: no Bash, MCP, subagents or recursive CLI calls.
- Scope: (1) Codex's nullable repair in `core/MemoryForge.Core/Run.Services.cs`; (2) Codex's appended assignment test in `client/MemoryForgeUnity/Assets/Tests/PlayMode/RunProgressionPlayModeTests.cs`.
- Execution: **NOT_RUN** by this reviewer. I did not see a fresh receipt for the Unity Play Mode run or the full 2679/272504 replay, so I am not claiming either one passed.

## Verdict: **APPROVE**

I found no blocking or medium-severity defects. Two low, non-blocking notes are below.

## 1. `Run.Services.cs` — `.Value` → `.GetValueOrDefault()` (11 sites)

I counted 11 uses of `GetValueOrDefault()` in `EventChoose`, at lines 156, 157, 158, 159, 160, 161, 165, 167, 189, 193 and 219. They are the only nullable reads in the file apart from `Present` itself (line 241).

- **Every site is guarded.** Lines 156–161, 167, 189 and 193 sit on the right side of `Present(x) &&` or inside a block guarded by `if (Present(x))`. Line 165 is inside the `Present(fx.MaxHpPct)` block (162–166). Line 219 is the loop bound inside the `Present(fx.CardRemove)` block (217–227).
- **Same behaviour as `.Value`.** `Present(int? v)` is `v.GetValueOrDefault() != 0`. When it returns true, `v.HasValue` is true and `v.Value == v.GetValueOrDefault()`. So the repair returns the same value at every site and can never switch on the default-0 path. Guard order, effect order, the RNG call order (`PartChoices`, `Below`) and the early `return` for `Variation` are all unchanged.
- **No behaviour suppressed.** No guard was removed or weakened, and nothing was turned into a silent default. The `if (fx.n)` truthiness rule (lines 240–241) is unchanged.
- `Assign` (lines 18–29) matches source `assign` (`engine/rules-core.js:1802-1808`):
  - Same lookup: first deck card whose key equals `key`.
  - Same rejection order: `BAD_ASSIGN` (missing card, non-common owner, or charId not in the party) comes before the v2 saved-key check (`REASSIGNMENT_REQUIRES_SEPARATE_FLOW`).
  - The v2 check covers every saved character's rows, so a same-character reassignment is refused too.
  - Same mutation, then `CaptureMemory` only while a `save` is pending.
  - This also matches design §8 line 1221: `Run.assign` is the save-screen reassignment for newly gained common cards.

## 2. `RunProgressionPlayModeTests.cs` — `ActualCommonAssignmentMatchesOriginalJs`

- **Reference matches the JS output.** I compared the embedded `AssignmentReference` (line 92) field by field with `reports/run_services_core_20261016/unity_assignment_actual_js.json`. All 12 hashes (2 initial + 10 after-step), all 10 argument pairs and all 10 error codes are identical:
  - legacy initial `56a6ba86…1973`; step 1 → `fb033004…20c3`; steps 2–5 → `56a6ba86…1973`
  - v2 initial `11a43e74…7cc3`; all 5 steps → `11a43e74…7cc3`
- **The generator is faithful.**
  - `unity_assignment_reference_generator.mjs` uses a fresh `Run.create` for each policy, with the same setup as the Unity test (party sera/harin/nia res 0, seed 1234, short, zones [1], difficulty 100/100; v2 adds runId `unity-assignment` and memoryVersion 0). It then calls the real `run.assign` and hashes `{run: run.s, memory: null}`.
  - No state is injected, and any error that is not a `RuleError` is rethrown. On the C# side, the test only catches `RuleException`, so the two sides handle errors the same way.
  - The generator's own asserts lock in the expected outcomes: legacy reassignment succeeds; v2 refuses saved key `base:sera:4` with `REASSIGNMENT_REQUIRES_SEPARATE_FLOW`, both for harin and for the same character (sera); legacy assignment back to `sera` restores the initial hash; `BAD_ASSIGN` is returned for an owned card (`k1` / `base:sera:0`), the unknown character `unknown`, and the missing key `missing`.
- **Test structure.**
  - Two `TestCase`s with 5 actual `Assign` calls each, and `attempts == 5` is asserted.
  - After every attempt the test checks the error and the full state hash, so a failed call that partly mutated state would be caught.
  - The existing 17-command cycle test (lines 47–89) is unchanged.
- **Scope claims are accurate.** `Content()` builds from the full `ClientBattleContent` battle, starting-common and encounter data. This cycle never enters nodes or events. The file header (lines 1–3) does not claim full Run, UI, services, save, server or device acceptance.

## Non-blocking notes

**N1 — Low — `RunProgressionPlayModeTests.cs:19-31` (`Content()`): v2 memory cost/capacity config is not loaded.**

`MemoryRulesData.Costs`, `CapacityBase` and `AscensionCapacity` stay at their C# defaults (all 0 / empty). For the v2 case, `Run.Create`'s `OVER_CAPACITY` guard (`Run.cs:59-60`) therefore compares 0 > 0. That passes, but only because nothing is loaded.

The asserted outcome is still right: a baseline memory costs 0 against itself under any config, capacity is not part of `run.s`, and `Assign` never reaches `CaptureMemory` here because nothing is pending. So no hash or verdict changes. It does mean this test does not check that the Unity content path supplies the real v2 capacity data.

Fix, when the client memory/save import lands: load the real cost/capacity config. Until then, add one line to the comment at line 29 saying that the v2 capacity config is intentionally not loaded and the `OVER_CAPACITY` guard is not exercised here. This is not a reason to hold integration.

**N2 — Low (coverage, not a defect) — the v2 success path is not exercised in Unity.**

Every v2 starting common card is a saved key, so the cycle never reaches a successful v2 assignment of a card gained during the run. It also never reaches the `PendingKind == "save"` → `CaptureMemory` recapture. Both are source paths (`rules-core.js:1806-1807`). Host vectors may already cover them.

When a later cycle reaches REWARD/SHOP and then a save, consider an actual-runtime case there. This is not required for the selected scope.

## Not covered by this review

- Running the build, tests, Unity Play Mode or the full replay (NOT_RUN).
- Whole-Run UI, server and device acceptance (out of scope).
- Any history of A's pre-repair file beyond the mechanical difference Root described.
