# P1-FORECAST-ROOT-CHECK-INTEGRATION-REVIEW — opposite review

- Reviewer: Claude Code, Opus 5.5 (`claude-opus-5-5`), requested effort High. Read-only on all code/fixtures; only this file edited.
- Scope: only Root's three changes listed below. No forecast UI integration, policy, base0, physical mouse or broad Core/API review.
- Execution by reviewer: **NOT_RUN** (no Bash/dotnet/Unity in this task). Every statement below comes from reading source. This review does not claim a CoreChecks, C# runtime, Node or Unity PASS.

## Verdict: APPROVE (no concrete defect)

### 1. `core/MemoryForge.CoreChecks/Program.cs:117-129`
- `int previewFailures = BattlePreviewChecks.Run(root);` (`:127`) runs after the ten existing suites. It is in the final return as `&& previewFailures == 0` (`:129`), so a nonzero preview failure count makes the process exit 1.
- Every earlier aggregate is still present and keeps its order: base `failures`, `traceFailures`, `openingFailures`, `turnFailures`, `queryFailures`, `playerFailures`, `memoryFailures`, `runCreateFailures`, `progressionFailures`, `numberFailures`, `textFailures`. No suite was removed or short-circuited, and no expected count was touched in Program.cs.
- Limitation: I could not diff against HEAD without Bash. "Old suites unchanged" therefore rests on the current file contents and on Root's report that the build had 0 errors and 0 warnings.

### 2. `core/MemoryForge.CoreChecks/BattlePreviewChecks.cs:52-59` (Run-owned catalog snapshot)
- The snapshot covers the real input. `RunCase` builds `ExpeditionRun.Create(data, …)` from `runData` (`:56`, `:112`). In Core, `Run.Progression.cs:72` creates the live battle with `Battle.Create(data.Memory.Battle, setup)`. Rewards and services (`Run.Rewards.cs:39-152`, `Run.Services.cs:182`, `Run.cs:43-145`) read the same `data.Memory.Battle` instance (`MemoryV2.cs:19`, `RunDefinitions.cs:140`). So `Catalog(runData.Memory.Battle)` fingerprints the exact object graph that the Run code reads and could mutate. It is not a stand-in instance.
- Order is correct. `runDefinitions` is captured after both loads and before any case runs (`:53`). It is compared once after the loop (`:59`), and the expedition is the only case that uses `runData`. The per-case `data` check (`:57`) is unchanged.
- Nothing was relaxed. The new line only adds a `Check`. If an exception skips it, the outer `catch` still records a failure (`:65`). The `70/40` denominators, the `Expect` literals, `LateRejection` and fixture rejections are all unchanged by this addition.
- The scope is stated correctly. `Catalog` serializes only cards, characters, weapons, parts and relics (`:514-518`). The check label says exactly that. It does not claim enemies, rules, `RunData.Characters`, encounters or events, so it doesn't overclaim.

### 3. `client/MemoryForgeUnity/Assets/Tests/PlayMode/ExpeditionClientPlayModeTests.cs:183-189`
- The file has `#nullable enable` (`:6`). The runtime null tests come first: `Assert.That(view != null && view.activeInHierarchy, Is.True, …)` (`:183`) and `Assert.That(noticeLabel != null && noticeLabel.isActiveAndEnabled, Is.True, …)` (`:186`). Those explicit `!= null` comparisons put both locals into the compiler's maybe-null state. NUnit's `Assert.That` has no `[DoesNotReturnIf]`, so the compiler can't carry the fact forward, which explains the CS8602/CS8604 warnings.
- `noticeLabel!.text` (`:187`) and `AssertReadable(view!, …)` (`:189`, parameter `GameObject view` at `:793`) only suppress the warning. They emit no IL and change no runtime behavior, including Unity's overloaded `==`/fake-null semantics. Because `!` also resets the flow state, the later unannotated uses at `:188` and `:190` don't warn, which matches the report that only these two lines warned.
- Nothing was weakened. The conditions, assertions, the 6 s deadline loop on the real 2.6 s toast timer, the `>= 2.6f` hold assertion and the pointer down/up/click steps (`:165-200`) are all still there.

## Not verified by this review
- Root's CoreChecks build (0 errors/0 warnings), the Node 22/25 6/6 results and the in-progress full C# runtime were all reported by Root, and I did not observe any of them. The full runtime result is still pending. Do not record it as PASS until it finishes.
- I did not see a fresh Unity compile without CS8602/CS8604 or the PlayMode rerun (the earlier evidence was old code RED, then 87 GREEN). Both are still Root's to run.
