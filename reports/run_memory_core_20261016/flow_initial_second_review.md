# Strengthened full-flow test recheck

- Task: P1-CARD-TEXT-AND-FLOW-RECHECK (part 1) · reviewer: Claude Opus 5.5 (High), read-only static review
- Read: `client/MemoryForgeUnity/Assets/Tests/PlayMode/RunMemoryFlowPlayModeTests.cs`, `reports/run_memory_core_20261016/unity_full_run_reference_generator.mjs`, `engine/tests/unity_run_reference.test.mjs`, `core/MemoryForge.Core/RunState.cs:84`, `Run.Progression.cs:78,199,218`
- Execution: **NOT_RUN** (no Bash/MCP in this task). I did not run Node or Unity. Root's run of the strengthened test is the only execution evidence.

## Verdict

**CHANGES_REQUESTED, one concrete defect (R1).** R1 makes the test error out. It does not make it pass falsely. The prior F1–F4 gaps are closed in design, but none of the new checks has run yet, because R1 throws before the first one.

## R1 — Unity `Transcript` throws whenever the Run has no live battle (high: blocks the test)

- **Where:** `RunMemoryFlowPlayModeTests.cs:47-48`: `graph["battle"]?["log"]` and `graph["battle"]?["ev"]`.
- **Why it fails:** `RunState.ToJsonGraph` always emits a `"battle"` key, with a null value when there is no live battle (`RunState.cs:84`, `Battle?.ToJsonGraph()`). `JObject.FromObject` turns that null into a `JValue` of type Null. That is a non-null C# reference, so `?.` does not short-circuit. Newtonsoft's `JToken.this[object]` on a `JValue` throws `InvalidOperationException("Cannot access child value on …JValue")`.
- **When it fires:** `Battle` is null right after `Run.Create` and again after each battle ends (`Run.Progression.cs:199,218`). So it throws at the first initial-transcript check (L66), and at every out-of-battle step, the DONE state and the reload transcript (L125).
- **Fix (test-only, Root's file):** `var battle = graph["battle"] as JObject;` then use `battle?["log"]?.DeepClone() ?? JValue.CreateNull()`, and the same for `ev`. That matches JS `run.s.battle?.log ?? null` (generator L14). If Root's run already passed, re-check that the Unity file it ran matches the readonly copy I reviewed.

## Prior findings, status in design (unverified until R1 is fixed and the test runs)

| Prior | Status | Evidence |
|---|---|---|
| F1 log/ev unchecked | Closed in design | Generator L14/22/49/55 capture `runLog`/`battleLog`/`ev` for initial, every step and reload. Unity L66/116/125 DeepEquals them, and L76 binds query purity on the transcript as well as the hash. |
| F2 returns unchecked | Closed in design | Generator L41–47 clones every actual JS return before the next command, round-trips it through portable JSON and deepEquals it to the fixture `step.result.value`. Unity L110–113 checks the void/json kind for every non-error step and DeepEquals every JSON return (battle `ToJsonGraph`, `ForgeOffer`, `CommitMemory`). Node L11 checks that error steps carry a null result. |
| F3 provenance/freshness | Closed | Envelope carries fixture `sources` and the generator's own SHA-256 (L58–59). The Node test regenerates the reference and byte-compares it (test L10, plus a `--check` mode). Unity asserts the exact three ids (L59) and each terminal outcome (L120). |
| F4 hardcoded counts | Closed | Generator counts are measured (`queries++`, `restarts++`). Node L9 pins 592/4/3, and Unity L127 measures them independently. |

## Residual notes (non-blocking)

- **N1:** The generator copies `outcome` from `source.trace.summary.outcome` and `undefinedPaths` from the fixture without checking either against the live JS run. Both are transitively bound by the matching final `stateHash` and return value, and Unity never reads `undefinedPaths`. Optional: `assert.equal(run.s.result.outcome, value.outcome)`.
- **N2:** The transcripts come from fresh deterministic JS, not from the fixture's recorded `after.jsonHash`. The current checks are correct. Comparing the transcript against that hash would also catch fixture/replay drift in log text.
- **N3:** The node test's `deepEqual(value.selected, selected)` is tautological. The real id binding is Unity L59 plus the throw in `fixture.cases.find`.
- **Scope (unchanged):** this is Editor Play Mode evidence only, not player, IL2CPP or Android device acceptance.
