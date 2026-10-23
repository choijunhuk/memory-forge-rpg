# Legal assigned input guard repair (P1-LATE-CHECKER-ASSIGNED-INPUT-FIX)

The C# late-action consumer stopped before replaying anything. Validation threw on boundary setups that use the unchanged base kits, whose starting commons are `{id, assigned}` cards. This was a bug in the checker. The engine, the input and the oracle were correct.

The repair changes only the guard. **Author build and execution: NOT_RUN** (no shell). Root runs the actual C# replay next.

## Root's actual run (attributed to Root)

- Node 22 and Node 25: 7/7 PASS each.
- .NET build: 0 errors, 0 warnings.
- All original suites PASS, including Preview 1194 and the 1000-run legacy complete corpus.
- The late consumer aborted with 0 actual requests: `InvalidDataException: edge-seal-two-sealers deck injects produced card state` (ValidateFixture, former line 599).

## Cause

The edge-setup deck guard used `RequireKeys(card, {"id"})`. It only allowed a card object with exactly one member, `id`.

The producer's legitimate public setup gives the boss/base-kit edges the unchanged base kits. Their starting commons are `{id:'com_guard', assigned:'sera'}`, `{id:'com_slash', assigned:'nia'}` and `{id:'com_slash', assigned:'harin'}`.

`assigned` is ordinary Battle.create input: `BattleCardSpec.Assigned`, read by `BattleDataLoader.Setup`. Across all 81 cases, every deck card has exactly `id` plus an optional `assigned`.

## Change (`core/MemoryForge.CoreChecks/BattleLateActionsChecks.cs` only)

- **New `DeckCard` guard for edge decks.** A card is accepted only if:
  - it is an object;
  - its `id` is a string;
  - every member is `id`, or `assigned` with a string value.
- **Still rejected:** any other member, such as `uid`, `used`, `token`, `temp`, `growth`, `free` or a whole produced snapshot. The error label is unchanged.
- Duplicate keys were already rejected by the strict unique-key decoder.
- The exact live-matrix comparison for duel, encounter and protocol setups (base kits including `assigned`) is unchanged.
- Nothing else changed. These all stay as they were:
  - source setups, expected states and counts (81 / 473 / 445 / 2 / 26);
  - source pins, strict decoder and candidate isolation;
  - every other validator and assertion;
  - the producer.

## New corruption controls

**Acceptance check.** The three real assigned base-kit cards of `edge-seal-two-sealers` must exist and pass `DeckCard` structurally.

**Tamper cases.** Each one goes through the full `ValidateFixture`, edits the first assigned card of that case, and must be rejected:

| Edit | Field |
|---|---|
| Add `uid` | produced state |
| Add `used` | produced state |
| Add `temp` | produced state |
| Add `token` | produced state |
| Set `assigned` to a non-string | type |
| Remove `id` | missing input |

The earlier control, `growth` injected into the first boundary deck, is kept.

## For Root

Rebuild CoreChecks and rerun the full suite. The late consumer should now reach replay of all 473 requests. If a further failure appears, report the exact diagnostic; do not change expected values.
