# Mono parity author report (P1-LATE-ACTIONS-MONO-PARITY)

**Status: source written. Author compile, PlayMode run and Editor Console check are all NOT_RUN.** I have no shell or Editor access, and Root is the only Editor operator.

The only file added is `client/MemoryForgeUnity/Assets/Tests/PlayMode/BattleLateActionsPlayModeTests.cs`. It is test-only:
- It changes no Core, Resources, export, scene, setting or fixture.
- It does not activate the 23 candidate enemies in Run or Unity content.
- It does not change the live 20 enemies, their values or balance.

## Root's actual results before this unit (attributed to Root)

- Full C# late-action checker: 7589/7589 over 81 cases and 473 requests (445 committed, 2 replayed, 26 rejected), with 470 receipt probes and 473 copy probes.
- Preview: 1194 PASS. The 1000-run legacy corpus: PASS.
- Node 22 and Node 25: late test 7/7 each. Full Node 22: 333 PASS.
- Unity Play: 102/102 PASS. **Those existing Unity tests do not execute the candidate late cases.** This new test is what would.

## Test

There is one coroutine test: `[UnityTest, Timeout(900000)] CandidateLateCorpusReplaysThroughMonoCore`. It requires `Application.isPlaying` and yields one frame after each case.

### Load and provenance

1. **Repository root.** The test derives it as `Application.dataPath/../../..` (Assets → MemoryForgeUnity → client → repo), the same derivation as the existing source-comparison test. There is no hardcoded user path.
2. **Reading the fixture.** It reads `engine/tests/vectors/battle_late_actions_vectors.json.gz` through:
   - `System.IO.Compression.GZipStream`;
   - a throwing UTF-8 decoder;
   - Newtonsoft `JToken.ReadFrom` with `DateParseHandling.None`, `DuplicatePropertyNameHandling.Error` and a trailing-content check.

   **This reader is not the strict decoder.** `GZipStream` accepts concatenated members. The strict single-member, CRC and footer contract is still proven only by CoreChecks' `RunCreateChecks.Decode` and the Node `decodeVectors`.
3. **Fixture checks (`ValidateFixture`):**
   - exact top-level members;
   - exact schema, scope and status;
   - the six allowlisted sources (data, engine, memory, canonical, proposal, generator), in order and exactly once. Only the fixed allowlist paths are hashed, so a fixture cannot steer which file is read;
   - fixed counts 81/473/445/2/26 and coverage 23/23/34/1;
   - the six behaviors;
   - overlay provenance `candidate-only`;
   - 23 overlay enemies, 17 test-local enemies (all `t_` IDs) and 81 cases.
4. **Source match.** `ClientBattleContent.Load().SourceSha256` must equal the SHA-256 of `data/game_data.json` and the fixture's data pin. Only after that are the attribute IDs read from that same source file.

### Definitions: the explicit Mono-test bridge

- **Fresh data per case.** Each of the 81 cases gets a new `ClientBattleContent.Load().Battle`. The untouched live definitions come from Resources.
- **Overlay.** The fixture's own `overlay.enemies`, plus `boundaryEnemies` when the case's dataset is `boundary`, are mapped by small test-local translators onto the existing public types:
  - `BattleEnemyDef` (id, name, tier, hp, brk, weak, passive, pattern);
  - `EnemyMoveDef` (t, card, s, who, label, e, d, h, str, b, n, then);
  - `EnemyPhaseDef`, applied with `SetPhaseDescriptor`.

  Absent members stay null. Unknown members throw instead of being skipped. An ID collision with live content throws.
- **Attributes and indexing.** Then `SetAttributeIds(source attributes)` and `Index()`.
- **Mapping check.** `CheckTypedMapping` requires every mapped definition to serialize canonically identical to its source JSON, including absences. The only exception is `zone`, which is named as the one typed limit: Battle never reads it.
- **Resources unchanged.** `AssertLiveUnactivated` runs before and after the corpus. Resources must still hold exactly 20 enemies, with no candidate or `t_` ID, no phase descriptor and no late move type.

### Replay

**Setup mapping.** Only this corpus's legal `Battle.create` inputs map to the public types; any other member fails instead of being skipped:
- `party`: `charId`, optional `gauge` → `BattleActorSetup`;
- `deck`: `id`, optional `assigned` → `BattleCardSpec`;
- `hp`, `maxHp`, `seed`, `relics`, `enemies`;
- optional `difficulty` (`hp`, `dmg`).

**Requests.** Recorded RID, version, kind and args are passed unchanged. Args are converted recursively and in order through the public `BattleArgument.Object/Array/String/Boolean/Integer/Null` factories into `new BattleRequest(rid, v, kind, args)`, then sent to the public `Battle.Command`.

**Rejected steps.** The real `RuleException.Code` must equal the recorded code. The full prestate (including log and events) and the public view must be byte-identical afterward.

**Committed and replayed steps:**
- the response must have exactly `{v, phase, ev}`;
- version, phase and events must equal the oracle;
- the response events hash must match;
- a replay must leave the state unchanged;
- a commit must advance exactly one version.

**Every checkpoint (created plus each step), 554 in total:**
- the actual full state, log, events and public view must equal the recorded JSON in canonical serialization. `CanonicalState` omits `log`/`ev`, so they are appended explicitly;
- the actual state, RNG, events, log and public-view hashes must equal the recorded hashes;
- the recorded state must hash to its own recorded value;
- the public view must hide rng, cards, zones and draw.

Expected JSON is converted to plain graphs **only for comparison**. No snapshot or receipt is ever restored or injected, and no reconstruction constructor is used.

**Failure handling.** Failures are collected, up to 200, with first-difference excerpts. A case stops at its first mismatch, and that stop is itself recorded as a failure. Anything thrown outside the recorded contract is recorded too.

**Positive counters.** The test asserts that 81 actual `Battle.Create` calls and 473 actual requests ran, with 445 committed, 2 replayed and 26 rejected. Validating the fixture alone is not enough to pass. A `Debug.Log` line prints the checks, creates, requests and checkpoint counts.

## Limits (not hidden)

- **No shipping bridge.** Candidate auto-export, Resources loading and activation are not implemented. The phase descriptors and attribute IDs exist only through this test's explicit `SetPhaseDescriptor` and `SetAttributeIds` calls.
- **C#-only rejection codes are not covered here.** CoreChecks covers them locally: `BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED` and `BATTLE_NUMBER_OUT_OF_RANGE`. This test runs only the shared 81-case JS corpus.
- **No detached-copy mutation probes in Mono.** They exist in CoreChecks (473 probes, Root's run).
- **Not device or Android acceptance.** This is Editor PlayMode/Mono only. IL2CPP and device behavior are not covered.

## Root's actual run steps

1. Make sure the checked-in `engine/tests/vectors/battle_late_actions_vectors.json.gz` and `Resources/MemoryForge/battle-content.json` are current; the resource's SourceSha256 must equal `data/game_data.json`.
2. Let Unity compile; check the Console for 0 errors and 0 new warnings from this file.
3. Run the PlayMode suite, including `BattleLateActionsPlayModeTests.CandidateLateCorpusReplaysThroughMonoCore`. Confirm the Debug line shows 81 creates, 473 requests, 445/2/26 and 554 checkpoints, and that the existing 102 tests stay green.
4. If anything fails, return the exact failure lines, which include a first-difference excerpt. Do not change the oracle or expected data.
