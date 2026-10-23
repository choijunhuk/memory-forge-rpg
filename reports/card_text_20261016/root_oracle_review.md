# P1-CARD-TEXT-ORACLE-AND-COVERAGE-REVIEW — independent read-only review

Reviewer: Claude Code Opus 5.5 (High), read-only source inspection. No Bash, MCP, agents or images were used.
Execution: **NOT_RUN** by this reviewer. The node test, CoreChecks and Unity results all come from Root. The Unity 8-test run had not finished when this review was written, so this report does not claim it passed.

## Verdict: APPROVE

I found no specific correctness or coverage defects in the Root-authored changes under review.

## What was checked

### `reports/card_text_20261016/reference_generator.mjs`, `reference.json`, `engine/tests/card_text_reference.test.mjs`
- **Source of the oracle:** every row's text comes from `MF.cardText(def, ix)` in the unchanged `engine/rules-core.js:189-201`, using `ix = indexData(data/game_data.json)`. No C#-derived value appears anywhere in the generator.
- **Authored rows:** the generator loops over every entry in `data.cards`. `assert.equal(rows.length, 519)` (line 25) only holds if there are exactly 173 cards × 3 variants.
- **Plus and resonance6 rows use real effective definitions:**
  - Each comes from `Battle.create(...)` followed by `battle.effDef(...)`, not from hand-applied modifiers.
  - `Object.values(battle.s.cards)[0]` is always the single deck card. With no relics, `create` (`rules-core.js:286-292`) calls `makeInst` only for deck specs. `eye_of_rift` is never present, and enemies go to `s.enemies`, not `s.cards`.
  - `effDef` (`rules-core.js:370-383`) returns a new object (`{...base, eff, uses}`). `upgradeOps` and `applyCardMods` work on clones through `mapOps`. So the authored `definition: card` objects are not changed before serialization.
- **Owner and wielder setup:** character cards use their owner, weapon cards use the character holding that weapon type, and common cards are assigned to the owner (or Sera). This matches the gating in `ownerChar`/`effDef`, so resonance6 rows apply the owner's `cardMods` exactly as live play does.
- **Pins and freshness:** SHA-256 is taken over the raw bytes of `engine/rules-core.js`, `data/game_data.json` and the generator itself. The node test regenerates everything, including the pins, and requires a byte-identical match with `reference.json`. It also requires 173 unique IDs for each variant. This test is part of `npm test` through `engine/tests/*.test.mjs`.

### `core/MemoryForge.CoreChecks/CardTextChecks.cs` and the `Program.cs:126-128` integration
- **Duplicate keys and counts:** duplicate JSON keys are rejected (`UniqueKeys`). The schema must be `mf-card-text-v1` with `count == 519`, and the final check requires 173/173/173 with 519 unique `id/variant` pairs. An empty or truncated file therefore fails.
- **Source allowlist:** the order must match exactly (`SequenceEqual`), and a substituted file throws, which counts as a FAIL. Each pin is compared against the current file bytes.
- **Definition loading:** every row's definition goes through the strict loader `BattleDataLoader.Card` (`BattleDataLoader.cs:85-97`), which rejects unknown fields. Text is compared ordinally with the JS string, and the expected/actual pair is printed on failure.
- **Purity:** the card's `ToJsonGraph()` must be identical before and after `BattleText.Card`. `BattleText` holds only `static readonly` lookup dictionaries (`BattleText.cs:12-22`), and nothing injects game state.
- **Failure propagation:** `textFailures` is included in the process exit code. Exceptions are caught and counted as failures, not swallowed.
- **No changes to source data, game data or numbers.**

### `ExpeditionClientPlayModeTests.cs:510-543` (SHOP guard)
- `Assert.That(mirror.State.Phase, Is.EqualTo("SHOP"), ...)` at line 520 now fails hard when the bounded route never reaches a shop, instead of silently skipping purchase coverage. This is correct.
- **The shop loop always has items to check:** `MakeShop` (`Run.Progression.cs:158-172`) always lists exactly 3 cards, so the per-card loop always runs.
- **Per-card assertions kept:** text equality, the fingerprint staying the same after reading, readability, the detail panel intercepting the Buy hit-point, and no payment from a click behind the panel.
- I compared only this guard. I did not diff the other assertions of the existing 8 tests.

## Non-blocking notes (no repair required for this task)
1. **Combined plus + resonance6 is not in the oracle.** No row combines both, as in `applyCardMods(upgradeOps(...))`. The reported scope (173 authored / 173 plus / 173 resonance6) is accurate as stated. The combination uses the same op shapes, so the risk to text is only numeric. It could be added later as a fourth variant if wanted.
2. **The C# check trusts `reference.json` rows.** It pins only the sources, and the node test is what guarantees that the rows were freshly generated. Both gates need to run: `npm test` and CoreChecks.
3. **Optional hardening:** `assert.equal(definition.id, card.id)` in the generator's variant loop would make the `[0]` selection self-checking. It is correct today, as verified above.
