# P1-CARD-TEXT-SEED-ROUTE-RECHECK — narrow read-only recheck

Reviewer: Claude Code Opus 5.5 (High), read-only source inspection in worktree B. No Bash, MCP, agents or images were used.
Execution: **NOT_RUN** by this reviewer. The seed1234 RED receipt, the seed1235 route probe, 86/86 Play and 3/3 Edit are all Root's results.
The earlier APPROVE of the card-text oracle, checker and strict SHOP guard (`root_oracle_review.md`) still stands.

## 1. Seed change — APPROVE

`Assets/Tests/PlayMode/ExpeditionClientPlayModeTests.cs`:
- **The client and the mirror get the same start.**
  - `Open(Opening result, long seed = Seed)` (lines 43-52) sets `client.Seed = seed` on the host while it is still disabled, so this happens before the real `OnEnable`, `StartPractice` and `Depart` run.
  - On the first departure `departures == 0`, so the client runs `Run.Create` with seed `Seed + 0` and runId `"practice-1-" + seed` (`MemoryForgeExpeditionClient.cs:126-128`).
  - The mirror is created with `PracticeSetup(seed, "short", "practice-1-" + seed, {}, 0)` (line 51). That is the same seed, runId, empty memory and version 0, so the two runs start identically. No state is injected.
- **Other tests are unaffected:** every other caller uses `Open(opening)`, which keeps the default 1234 (lines 58, 103, 147, 263, 287, 377). Their `"practice-1-" + Seed` and `Seed + 1` expectations therefore stay valid.
- **Only this test changes:** `RewardShopAndDeckDetailsAreReadableBeforeTakingOrPayingAndNeverChangeTheRun` uses `Open(opening, 1235)` (line 448).
  - The only other edit is the REWARD failure message, now naming seed 1235 (line 454).
  - The strict `Phase == "SHOP"` assertion, the reading/text checks, the pointer-interception checks, the no-spend fingerprints and the step bounds are all unchanged.
- **The route itself:** that seed1235's real route reaches a SHOP is Root's finding; I can't confirm it from source alone. The strict assertion enforces it at run time, so a wrong seed fails the test instead of skipping coverage.

## 2. EditorBuildSettings — NOT VERIFIABLE in B (scope/preservation finding)

- **The change isn't in B:** `client/MemoryForgeUnity/ProjectSettings/EditorBuildSettings.asset` lists only `MemoryForgeBattle.unity` (guid e174e70…, enabled 1) and `SampleScene.unity` (guid 8c9cfa2…, enabled 1). There is no `MemoryForgeExpedition.unity` entry.
- **The scene files aren't in B:** `Assets/Scenes/` has `MemoryForgeMcpSmoke.unity` and `SampleScene.unity` with their `.meta` files. Neither `MemoryForgeExpedition.unity` nor `MemoryForgeBattle.unity` is there.
- **What I can confirm:** in B the two existing entries are unchanged and still enabled.
- **What I can't confirm:** that the expedition scene is listed first. That change exists only in Root's working copy.
- **What to do at integration:**
  - Commit `Assets/Scenes/MemoryForgeExpedition.unity` together with its `.meta`. The guid in that `.meta` must match the new EditorBuildSettings entry.
  - Keep the Battle and Sample entries and their `enabled` values unchanged.
  - If `MemoryForgeBattle.unity` and its `.meta` aren't tracked either, include them. Otherwise a fresh native build references scenes that don't exist.
  - Once that commit reaches B, this item needs a re-check.

## Verdict
- **Seed change: APPROVE.**
- **Build-scene config:** no defect found in what is present, but Root's change isn't in B, so it isn't verified. It needs re-checking once the scene asset and its `.meta` are committed with it.
