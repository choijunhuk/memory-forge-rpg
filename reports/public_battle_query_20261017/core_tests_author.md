# P1-PUBLIC-QUERY-CORE-TESTS-20261017 — author report (Claude Opus 5.5, requested effort xhigh)

Status: **AUTHORED, runtime NOT_RUN by the author.** No Bash, dotnet, Unity, MCP or other agent was used. I did not compile or run anything; Root still has to build and run both suites. Source Core, Run and Unity forwarders were read-only; no source fix was needed or made.

## Files written (the only four owned files)
- `core/MemoryForge.CoreChecks/BattlePresentationQueryChecks.cs`: the new suite. It is `static int Run(root)` and returns its failure count.
- `core/MemoryForge.CoreChecks/Program.cs`: one line registers `BattlePresentationQueryChecks.Run(root)`, and its result is added to the exit condition. Every existing suite and its order is unchanged.
- `client/MemoryForgeUnity/Assets/Tests/PlayMode/BattlePresentationQueryPlayModeTests.cs`: 5 `[Test]`s in actual PlayMode/Mono.
- this report.

## Fixture design (both suites, each written independently)
- **TEST-ONLY BattleData variant.** The live content loads unchanged (`BattleDataLoader.Load(root)` / `ClientBattleContent.Load().Battle`). In memory only, the variant then appends:
  - test cards `t_*`, owned by the non-character `t_owner`, so they trigger no gauge, passive or echo;
  - one part, `t_part` ("시험 부품");
  - test enemies: `t_dummy` (charge), `t_delay` (delayRes) and, in Core only, `t_sealer` (sealSlot, charge);
  - `Rules.HandDraw = 10`, so every deck of at most 10 cards is drawn whole.

  Real data files, exports and Resources are not touched.
- Hand **membership** in every expectation is a hand-written literal. Only the order inside the hand comes from the snapshot, because the shuffle decides it and the spec says "zone order".
- Card uids are fixed by deck order: `c1..cN`, then the enemy `e(N+1)` (Create's MakeCard/AddEnemy).
- Every state change goes through real `Battle.Command` begin/play/end. Nothing is injected, and no restore path is used.
- Every query is wrapped in a purity check. The full `State.ToJsonGraph()` (version, RNG, log, events, zones, cards, reservations), `PublicView()` and the definitions print must be identical before and after the query.
- Detachment check: a repeated answer is an equal but separate object; a mutated answer reaches neither the state nor the next answer; the caller's pick list is neither changed nor retained.
- Each scenario ends by replaying every stored receipt. Each must return its original response and execute nothing.

## Cases (Core; the Mono suite covers a subset)
- **Board, temp/seal/expiry**:
  - Turn 0 / CREATED: `SealedUntil 0 >= Turn 0`, so Next is Due 1, Cap 1, Sealed.
  - Turn 1: `slot` gives Temp +1/+1 (Cap 3/2). `collateral` sets SealedUntil 2 (Next Cap 2, Sealed). Two next and two after reservations fill both columns, and a real play then gets `SLOT_FULL`.
  - Turn 2: Temp has expired and the seal still holds. Both `after` reservations are now due Turn+1, so Next shows Used 2 > Cap 1 with Kind "after" kept, and a real play gets `SLOT_FULL`.
  - Turn 3: the seal has expired (Cap 2, Used 1).
  - STATIC_BOUND: the column `Due` field is typed `long`.
- **Delay (real enemy delayRes)**:
  - An `after` reservation delayed from 3 to 4 sits in the After column on turn 2, not Next, with Delayed true and Kind "after".
  - On turn 3, an `after` card and a `next` card are both due 4. Next counts Used 2 across both kinds, with each Kind preserved, and a real play gets `SLOT_FULL`.
  - The delayRes event checks From, To, Kind and Discarded.
- **Enemy seal**: seals the following turn (Cap 1), expires on turn 3, and is reapplied on turn 4 (SealedUntil 4).
- **Hand-source gate**:
  - Each of these gets the same `NOT_IN_HAND`, with every other field at its default, in now, next and an invalid mode: CREATED draw pile, reservation, discard pile, exhaust pile, enemy uid, unknown uid, `""`, and a real draw-pile card during a player turn (11-card deck).
  - A hand card in an invalid mode gets `INVALID_MODE`.
  - TEST-ONLY definition removal after the commands:
    - the discarded card without a definition still gets `NOT_IN_HAND` (membership is checked before the definition is read);
    - a hand card without a definition gets Core's `UNKNOWN_CARD`, or `INVALID_MODE` when the mode is bad;
    - tune keeps that unknown card offered (`[c5]`). Before the removal it was not offered, because it is a curse (`[]`).
- **tune / curse / recall**:
  - tune offers only non-C hand cards without the part. After a real tune attaches `t_part`, that card is dropped from the offer.
  - These picks are refused: the tuned card, a curse, the source itself, and a discard-pile card.
  - One valid pick fills the request (Index 1, Left 0, no candidates). An extra pick gets `PICK_INVALID` with Accepted 1.
  - Without a rule, the zone list is the whole hand minus the source, curses included.
  - recall offers only the non-curse discard card. A curse or hand-card pick is refused.
  - next/after and plain cards ask for no picks; a pick where no request exists gets `PICK_INVALID`.
- **costdown and FreeNext 0/1/2**:

  | FreeNext | Price Core uses | Offered |
  |---|---|---|
  | 0 | `CostOf` | `{c3 (cost 1), c5 (CostIf, unknown)}` |
  | 1 | `CostOfWithoutNextFree` | same set (`CostOf` would be 0) |
  | 2 | `CostOf` (all 0) | `{c5}` only |

  - Direct `CostOf` / `CostOfWithoutNextFree` literals are checked at each step.
  - A non-leading costdown keeps the whole hand.
- **growth / evolution / curse / cap**:
  - growPick offers only a growth definition below growth 3.
  - copyPick offers both growth definitions plus the evolution whose `From` is the growth root. It excludes the same evolution without that provenance.
  - A rule-less growth-only list contains growth definitions only.
  - After 3 real growPick plays: GrowthCount 3, so growPick shows `PICK_GROWTH_CAP` with no candidates and any pick gets `PICK_INVALID`. copyPick is unaffected.
- **Ordered multi-request picks** (discard×2 from hand, then exhaustPick from discard, then pull from reservations):
  - All three requests are checked exactly (Label, From, Count, GrowthOnly, Op, Part, Leading).
  - Accepted/Index/Left/Candidates are checked after 0–5 picks: a repeated pick is dropped, a wrong-zone pick is refused, the pull candidates follow reservation state order, and a 5th pick gets `PICK_INVALID`.
- **Static policy on explicit `BattleState` fixtures**:
  - Price-delegate flag: FreeNext 0, 1, 2, 3 → false, true, false, false; the delegate is called exactly once.
  - Price 0 → false. A refused price (RuleException) → null. CostIf → null, and price is never asked. No price → null.
  - A card missing from the state or the definitions → null, and it stays offered.
  - A non-leading costdown, or a costdown without a price, keeps the zone list without asking the price.
  - tune behaves differently per part (p1 versus p2).
  - copyPick accepts an evolution from a growth root. It refuses one from a non-growth root and one with no provenance.
  - `PickBlocked` blocks only growPick, and only at the cap.
  - discard dedupes cards already picked.
  - `WalkPicks` and `PicksHold` (an explicit auto, extra picks, the source as a pick).
  - `ChoiceRequests`: only `pick: one` asks for a pick, Count is at least 1, an unknown or absent part shows "부품", next/after are empty.
  - `ChoiceRules`: Leading only for the card's first effect.
  - Results are fresh lists, and neither the fixture nor the definitions are written.
- **Mono only**:
  - The client wrappers (`MemoryForgeExpeditionClient.ChoiceRequests/ChoiceRules/PickBlocked/PickCandidates/EffectEligible`) are checked for parity with `BattleChoicePolicy` and with `HandChoices`, both below the growth cap and at it.
  - The Run forwarders on real exported content with `PracticeSetup`:
    - outside a battle they throw `NO_BATTLE`;
    - opening board: Turn 1, Due 2/3, Max 3/2, Temp 0, unsealed, Cap equal to the live base;
    - every non-hand uid gets the exact `NOT_IN_HAND` default;
    - every hand card has Error null and Core's requests; next asks for no picks; an invalid mode gets `INVALID_MODE`;
    - the Run state is unchanged afterwards.

## Limits (not faked as PASS)
- **NOT_COVERED, Turn near Int32.MaxValue.** It is not reachable through commands, and there is no injection API. Only the `long` field type is checked (STATIC_BOUND).
- **NOT_COVERED, a reservation due outside both columns during a player turn ("visible T+3").** It cannot be reached through live commands: delayRes moves a reservation by exactly one turn and discards it beyond the board (`NextPlayerSlotCapacity` is 0 for T+4), and a pull only moves after→next. The doc sentence "a reservation due on another turn is in neither column" therefore has no live witness. The suite instead shows that `Used` counts by exact Due regardless of Kind, including the mixed-kind case.
- The `UNKNOWN_CARD` case and the gate's "before the definition" witness change the TEST-ONLY variant's definitions *after* the commands. This is test-only and never production.
- The Mono Run opening-board check assumes `PracticeSetup(1234, "short")` has no battle-start temporary-slot or seal effect. If it fails there, check that assumption first; Base is compared against the live battle, not a literal.
- Not covered here: Scene/prefab/UI, server query, Android device, real authored cards beyond the Run smoke test.

## Observations for Root (not defects; no source change requested)
- `HandChoices` does not check `AllowedModes`. A now-only card asked in "next" returns no requests and no error. The tests treat this as documented behavior; the UI should rely on `Check`/`AllowedModes` for mode legality.
- With FreeNext ≥ 2, a costdown with a conditional cost stays offered as unknown even though Core's `TakePick` will take no card (`CostOf` is 0 for every card). This matches the documented "not promised outside the known scope" behavior, and the tests assert it.

session_id: not visible to me inside this session; Root should take it from the CLI metadata.

## Delta — P1-PUBLIC-QUERY-PURITY-TEST-FIX-20261017 (same session; test files only; author runtime NOT_RUN)

This delta closes the two proof gaps from the independent review. Every earlier check and case is kept, and none is weakened or skipped.

1. **Receipt map in the purity proof.**
   - New helper `ReceiptMap(Battle)` in Core and Mono. It reads Battle's private `receipts` dictionary through read-only reflection; the field is looked up on each call, so a renamed field fails that scenario rather than the whole suite. It prints the count, then every key with its stored `Identity` and full `Response`, in map order.
   - Core `Pure`: the before/after comparison now also includes `ReceiptMap`, so a query that adds, drops or changes any receipt key or entry fails.
   - Core `Rejects`: also requires the receipt map to be unchanged (a rejected play stores no receipt).
   - Core `Receipts`: the old exact-replay check is kept. Two checks are new:
     - the reflected keys must equal the set of stored rids, which proves the fingerprint reads the real map;
     - the map must be unchanged after the replays.
   - Mono `Fingerprint`, used by the Slots, Choices, Rejects and ReceiptsReplay purity asserts, now appends `ReceiptMap`. `ReceiptsReplay` gained the same rid-set equality assert.
   - No production accessor, restore or injection API is used, and no receipt is ever written.
2. **Detachment checked against the original answer before any further query.**
   - `Slots`, Core and Mono:
     - the probe must be a fresh object at the list, `Next`, `After` and each reservation;
     - after the probe's turn, both columns and every reservation field are mutated, `Print(view)` of the *original* answer is asserted unchanged **before** the next query;
     - it is asserted again after the probe's list is cleared, then the old next-query and state check runs.
   - `Choices`, Core and Mono:
     - the probe must be a fresh object at both lists and each request;
     - after every scalar, the candidates and every request field are mutated, `Print(view)` of the original is asserted unchanged before the next query;
     - Mono also checks it again after a request is added; then the old next-query check runs.

Remaining honest limits are unchanged:
- NOT_COVERED: Turn near Int32.MaxValue (static `long` bound only), and a reservation that is visible at T+3 in a player turn.
- The receipt fingerprint relies on the private field name `receipts` and the `Identity`/`Response` member names. If any of these change, the read throws; it can never pass silently.
- A Core XML comment on price errors is overbroad (Native's note). The source is read-only for this task, so it is not edited here, and it is not a blocker.
