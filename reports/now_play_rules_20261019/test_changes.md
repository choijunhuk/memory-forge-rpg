# Test and vector changes for the now-play rules (2026-10-19)

Rules: `docs/RULES_NOW_PLAY.md` (user decision 2026-10-19: no time reservation; every card is played now; 보존 `retain` keeps a card in hand as `held`). The engine (`engine/rules-core.js`, `engine/content-modes.js`) and data (`content/`, `data/game_data.json`) were already changed; this report covers only generators (`tools/gen_*.mjs`), tests and regenerated fixtures (`engine/tests/**`) and the regenerated Unity full-run reference JSON.

Rule for every entry: a removed or changed assertion is about a mechanic the spec removed (reservation modes and slots, the reservation ledger, pull/slot ops, enemy seal/delay acting on reservations, rv/ra/rn values, reserve-only modes, collateral's seal) or about a number that changed because the authored data changed; each entry names what now covers the replacement behaviour. Format: `test or scenario — asserted — no longer applies because — replacement covered by`.

## Results

- `node --test engine/tests/*.test.mjs`: 357 tests, 356 pass, 1 fail. The failure is `card_text_reference.test.mjs`, which compares `reports/card_text_20261016/reference.json` with fresh card text. Its fix is `node reports/card_text_20261016/reference_generator.mjs`, which writes a file outside this change's allowed paths, so it was not run.
- Every generator's `--check` passes against its checked-in fixture: battle_late_actions, battle_opening, battle_player_action, battle_player_query, battle_preview, battle_turn, content_modes, memory_rule, run_create, run_json_number, run_memory, run_progression. `traces.jsonl.gz` was regenerated with `node tools/gen_traces.mjs --output engine/tests/vectors/traces.jsonl.gz`, as `reports/cross_language_20261015/README.md` specifies; the stray default output `traces.json` was removed.
- `run_create_vectors.json.gz` records which manifest inputs differ from `MANIFEST_AIO.json` (`inputManifestAudit`). It was regenerated last, after the edits to `engine/tests/engine.test.mjs` and `review_v071.test.mjs`. Any later edit to another manifest-listed file (for example `core/MemoryForge.Core/Gacha.cs`, `Rng.cs`, `Stats.cs` or the csproj files) makes it stale again; rerun `node tools/gen_run_create_vectors.mjs`.
- Fuzz and balance (not retuned):
  - `fuzz_runs.mjs 100 short`: 100 runs, 99 clears, 1 defeat, 575 battles, 2451 turns, 12594 commands, unexpected 0. Before the change (HEAD): 92 clears, 8 defeats.
  - `fuzz_runs.mjs 40 full`: 40 runs, 36 clears, 4 defeats, 378 battles, 1771 turns, 9198 commands, unexpected 0. Before: 26 clears, 14 defeats.
  - `balance_probe.mjs 300`: assessment failed, 3 failed metrics (HEAD: also 3). Random, greedy and planner now give almost the same results, because the planner's edge came from reservation timing. Example: zone-2 boss turns / HP lost go from random 15.5/40.1%, greedy 16.8/50.1%, planner 16.8/50.1% (HEAD: 14.9/41.2%, 16.4/50.8%, 14.3/37.2%).

## Battle vector generators

### tools/gen_battle_player_action_vectors.mjs (fixture battle_player_action_vectors.json.gz: 992 cases / 3870 commands -> 778 / 2964)

Matrix (data-driven, every card is `res: 'n'`):
- card cases: base mode pairs 378 -> 173 (one `now` pair per card). Plus cases unchanged in count (173).
- `card-sera_pull-now` / `plus-sera_pull`: the preparation reserved `sera_round` 'after' so `pull` had a reservation; sera_pull is now `recall`, so it is prepared like com_recall (sera_reload discards a spare first).
- resonance cases: Sera's card sera_timed is played `now` (was 'after'), Yuri's yuri_cocoon `now` (was 'next').
- weapon cases: w_reservedDmg (w_sera, w_star_bow) keep sera_timed over one real turn end and play it held (was: reserve the weapon card / sera_round 'next'); w_growthHeal yuri_cocoon, w_blockUp nia_barrier and the w_none follow-up card are played `now` (were next/after).
- part cases c_echo / c_delay / c_far and all relic cases play sera_round `now` (were next/after).
- matrix witness `card-sera_calc-now` op `slot` removed: sera_calc no longer authors a slot op. Inventory: ops 39 -> 37 (pull, slot removed), authored effect fields 35 -> 30 (rv/ra/rn/next/after removed).

Edges / protocols removed (pure reservation mechanics):
- edge-pull-after-next, edge-pull-immediate-original-kind, edge-pull-multiple-explicit-pick, edge-pull-multiple-invalid-pick — `pull` op removed.
- edge-reservation-dead-retarget — a reserved card retargeting a target that died before its due turn; a now play validates its target at play time (protocol-bad-target covers BAD_TARGET).
- edge-fixed-target-mark-expiry-fizzle — fixed-target fizzle at a later reserved resolution; now FIXED_MARK_REQUIRED is checked at play (protocol-fixed-mark-required) and resolution follows immediately.
- edge-terminal-reservation-conservation — pending reservations returned at a terminal; replaced by edge-void-shard-post-resolve-kill (terminal inside a now play).
- edge-temporary-extra-slot-resolves-after-reset — `slot` op removed.
- edge-after-reservations-use-next-bucket — after/next buckets and SLOT_FULL removed.
- edge-collateral-sealed-slot-expiry — collateral no longer seals; replaced by edge-collateral-debt-shared-sequence and protocol-collateral-debt-cap.
- protocol-reserve-only (RESERVE_ONLY), protocol-reservation-required (RESERVATION_REQUIRED), protocol-pull-destination-full (PULL_DESTINATION_FULL), protocol-slot-full (SLOT_FULL) — unreachable codes.

Edges rewritten to play now (subject kept, numbers re-derived from authored data):
- edge-echo-evolved-lineage: block 19 at the reserved resolution -> 15 = v9 + evolved growth3 x yuri 2 on the now resolution; copy queued with due 2 (was 3); copy half 4 + growth 6 = 10 (unchanged); also asserts pendingCopies [] after the copy is played (copy never re-echoes).
- edge-copy-pick-lineage: copy played `now` (was 'after'): 22 = ceil(9x1.3) + e_guard 4 + growth 6 (was 27 with rv13).
- edge-reserved-void-shard-kill -> edge-void-shard-post-resolve-kill: sera_last now on turn 1: 45 + void_shard 3 kills the 46-HP enemy, VICTORY turn 1, HP 81 (was turn 3, HP 57).
- edge-greenhouse-growth: yuri_cocoon grows on its now resolution (growth op witness); block 14 = 6 + moss 3 + symbiosis 2 + greenhouse 3 (was 16 with rv8).
- edge-free-next-empty-clock: same facts with now plays (empty_clock is consumed by the first card even when free).
- edge-contract-counter-collateral: op collateral witness sealedUntil 2 -> debts[1] = {d2, n1, due 2}, sealedUntil 0, block 16.
- edge-restraint-kept-bonus: the first damage card of the bonus turn (held sera_timed) gets +30%: 26 = 20 x 1.3 (was reserved sera_round 15); adds bonus {avail, turn 2} witness.
- edge-growth-cap-once-and-saturation, edge-growth-once-below-cap (block 10 -> 8 = v6 + growth1 x2), edge-copy-pick-real-grown-root (block 24 -> 27: same-turn dews' moss 6 + ceil(6x1.3) 8 + moss 3 + e_guard 4 + growth 6), edge-echo-resets-earned-growth-upgrade-mods, edge-forged-lineage-does-not-grow: same subjects with now plays.
- edge-sera-r6-follow-up: ult on turn 1 arms seraR6Turn 2; a turn-1 sera_round is not boosted (8); the held sera_timed on turn 2 deals 32 = floor(20 x 1.08 x 1.5); cleared at turn-2 end (was: a reserved round resolving at turn-2 start, 19).
- edge-reserved-break-relics: unheld sera_timed brk 3 + eye 1 = 4; the held copy next turn brk 3 + eye 1 + precision_lens 1 = 5 (was: reserved resolution brk 5).
- edge-foresight-broken-damage: foresight now applies to the now attack: floor(20 x 1.4 x 1.25) = 35 (was rv22 -> 38); the enemy is still stunned when hit (was: stun skipped before due).
- edge-vulnerable-weak-and-gear-math: c_delay (now 보존, no bonus) replaced by c_quick +20%: floor(26 x 177/160 x 1.12 x 1.4734 x 1.25) = 59 (was ceil(22x1.3)=29 with delay 40% -> 75).
- edge-copy-delivery-after-draw-overflow: six held com_capacitor + the 5-card draw fill the hand (one burnCard) before the now-queued echo copy c16 arrives and overflows to discard (was: two after-reserved fieldlab draws).
- edge-debt-reservation-shared-sequence -> edge-collateral-debt-shared-sequence: loan d1 and collateral debt d2 share resSeq 2; both repaid next turn (AP 4-2-1 = 1).
- protocol-ap-low: sera_last now then sera_timed now -> AP_LOW (was after/next).
- protocol-no-empty-slot -> protocol-collateral-debt-cap: loan 2 + collateral 1 + collateral 1 = cap 4; a third collateral -> DEBT_CAP.
- protocol-quick-circuit-reserve-only-has-no-mode -> protocol-quick-circuit-now-only: c_quick no longer removes modes; next/after -> NOT_RESERVABLE, now commits.

Edges added (new mechanics): edge-retain-held-end-turn (keyword and c_delay 보존 kept with held=true, others discarded held=false, play clears held), edge-held-reset-recall-draw (recall and draw return the card unheld; held again at the next turn end), edge-empty-clock-first-card (first now card costs 1 less, second full), edge-sera-r1-round-draw-once (sera_r1 on a now 유리탄, once per turn), edge-held-time-stamp-weapon (w_sera +12% and time_stamp +5 gauge only on the held play).

### tools/gen_battle_preview_vectors.mjs (fixture battle_preview_vectors.json.gz: 91 forecasts / 52 commands -> 80 / 50)

- heal-reservation-slots -> heal-now-only-empty-clock: reservation estimates, RESERVE_ONLY, SLOT_FULL and "empty_clock before the first reservation" replaced by now forecasts (empty_clock discount on the first card in now mode, full costs after it) and NOT_RESERVABLE next/after rejections.
- picks-pull -> picks-recall: sera_pull is recall; picks choose the returned discard (zero forecast outcome).
- reserved-estimates -> now-estimates: now forecasts for newly broken enemy, foresight + broken on an already broken enemy (foresight now applies to every Sera attack), enemy block absorbing damage, zero outcome, AP_LOW.
- reserved-hidden-definition-rng: dropped; its now-mode subjects are covered by hidden-definition (draw / random definitions) and hidden-hook-rng (void_shard exhaust RNG).
- reserved-hidden-draw-hook -> now-hidden-draw-hook: a now kill of a marked enemy is hidden by the notebook hook (hiddenBy rng); the now/next marked-branch comparison was reservation-only.
- added held-retain-forecast: a held sera_timed forecast includes precision_lens break +1 (newlyBroken 1 vs 0 unheld).
- generator coverage asserts: error codes drop RESERVE_ONLY and SLOT_FULL; "visible next/after estimate" witnesses replaced by "every next/after request is NOT_RESERVABLE" and the now-mode witnesses; nextAfterDiffers must be false on the corpus; reachability.notCovered.reservationEstimate = UNREACHABLE.

### tools/gen_battle_late_actions_vectors.mjs (fixture: 81 cases / 473 commands -> 76 / 437)

- silence-free-next, silence-free-instance: now plays; silence's next/after surcharge never applies (costs 1/0/0/1/1 and 1/0/1/1/1); the "surcharge alone makes a reservation AP_LOW" step was removed (reservation-only).
- seal-protected-turn -> seal-no-reservation: sealSlot with nothing to seal marks the following turn, no block, now plays keep their cost.
- seal-fallback-full: dropped (needs a full reservation column).
- seal-already-sealed -> seal-after-collateral-debt: collateral is debt 1, not a seal, so the enemy seal is ordinary (fallback false); NO_EMPTY_SLOT / SLOT_FULL steps removed.
- delay-original-kind -> delay-no-reservation: delayRes with no reservation emits {uid: null, discarded: false} and changes nothing; the now card resolved once at v20.
- delay-overflow-discard, delay-outside-board, delay-pull-clears, delay-seal-capacity: dropped (they need reservations; pull removed).
- eat-empty-discard: the empty discard comes from an all-보존 hand (sera_timed, sera_last) instead of two after reservations.

### tools/gen_content_modes_vectors.mjs (fixture regenerated)

- witness `reserveReserved` (rift_reserve on a reserved resolution) -> `reserveHeld` (rift_reserve on a held card, engine/content-modes.js now reads ctx.held). Reached naturally 3 times by the scenario bots; no scenario changes.

## Battle vector tests

engine/tests/battle_player_action_vectors.test.mjs
- every authored card and all378 base legal modes ... — asserted 378 reservation-mode pairs — next/after modes removed — now asserts 173 now pairs and every card res 'n' (same test).
- whole fixture is complete ... — asserted baseModePairs 378 — same reason — 173.
- pull converts after into next values and debt deferral shares the reservation sequence — asserted edge-pull-after-next res due/kind and its 22 damage, edge-debt-reservation-shared-sequence res order — pull op and reservations removed — renamed "debt deferral repays later and collateral debt1 shares the loan sequence, due next turn" (defer assertions kept; collateral debt d2, shared resSeq, repaid next turn, protocol-collateral-debt-cap DEBT_CAP).
- echo delivery and copyPick ... — asserted at2.block 13+3*2 (rv13 reserved resolution) and step indices with the reservation turn, copy-pick block 17+4+6 — rv removed, echo copy queued on the now resolution — asserts 9+3*2, due 2, copy pendingCopies [] after use, ceil(9*1.3)+4+6.
- growth-once remains observable ... — asserted block 8+2 (rv8), copy block 11+3+4+6 next turn, echo delivered at steps[5] and last request v7 — rv removed, now plays resolve the same turn — 6+2, 2*3+ceil(6*1.3)+3+4+6, steps[4], v6.
- absolute due buckets, temporary slot reset and collateral expiry retain all reservations — removed (slotsTemp, after bucket SLOT_FULL, collateral seal expiry, pull pick reservation lists are all removed mechanics) — replacement: the collateral debt test above and the new retain/held test.
- damage and healing use independently calculated rounding boundaries — asserted 75 from ceil(22*1.3)=29 and c_delay +40% — rv22 and the delay bonus removed — 59 from ceil(20*1.3)=26 and c_quick +20%.
- post-resolution void shard terminal returns every other due/future card exactly once — asserted turn 3 / HP 57 and edge-terminal-reservation-conservation res/discard — reservations removed — renamed "... ends a now play exactly once": turn 1, HP 81, dmg [card 45, relic 3], one end event, exhaust [c1].
- each authored effect source path is lossless ... — asserted 35 effect fields / 295 authored effects — rv/ra/rn/next/after fields and com_anchor's slot op removed — 30 / 294.
- all39 authored ops ... — asserted 39 ops — pull and slot ops removed — 37 ops, asserts pull/slot absent.
- added: retain keeps 보존 cards as held, play/recall/draw clear held, and held keys the reworked relic and weapon bonuses.

engine/tests/battle_preview_vectors.test.mjs
- original-JS regeneration keeps every v1 case ... 91 forecasts / 52 real commands — asserted the reservation case ids, 91/52, v1 slice 70/40, RESERVE_ONLY/SLOT_FULL coverage, the reserved-estimates next/after witness and its lookalike control — reserved estimates unreachable — renamed "... now-play replacements ...": new ids, 80/50, 65/37, no RESERVE_ONLY/SLOT_FULL, no accepted next/after, nextAfterDiffers false on the corpus and its one-point logic checked on synthetic records.
- independent original-source literals ... — asserted reserved estimate literals (D turn1 next rv12, H reservation/after-slot/RESERVE_ONLY/SLOT_FULL, R rv6 estimate, F nested draw reserved, hidden-hook-draw rv10, P pull, E/X/K reserved cases) — reservation removed — now-mode literals derived from authored v/costs (empty_clock 2->1 and 1->0, page 12 HP, foresight 4*1.25*1.25=6, keeper 38-8-8-4=18), next/after = NOT_RESERVABLE, held precision_lens literals; X dropped (covered by F and hidden-hook-rng).
- every forecast re-derives ... — asserted the HTML reserved branch was compared at least once — no reserved forecast is visible — asserts it was compared zero times and the now branch at least once.
- strict shared decoder ... / checked-in fixture ... — asserted 91/52 — 80/50.

engine/tests/battle_late_actions_vectors.test.mjs
- all 23 candidate patterns ... / every checkpoint is reproduced ... / exports pass ... — asserted 81 cases / 473 commands / 26 rejections / 34 edges — five reservation-only edges dropped — 76 / 437 / 20 / 29.
- the six behaviors keep the exact original rules — asserted silence surcharge on next/after (2,…,2), silence AP_LOW on a reservation, seal-protected-turn SLOT_FULL, seal-fallback-full, seal-already-sealed (collateral seal, NO_EMPTY_SLOT), delayRes on reservations (kind kept rv22, overflow discard, outside board, pull clears marker, seal capacity), eat-empty-discard indices — reservations removed and collateral no longer seals — asserts now-play silence costs (no surcharge), seal-no-reservation, seal-after-collateral-debt (ordinary seal, debt 1, AP 3), delay-no-reservation (null event), eat-empty-discard via an all-보존 hand.

engine/tests/content_modes_vectors.test.mjs — unchanged (passes against the regenerated fixture).

## Run-level tests and fixtures

Common cause: time reservation is gone. Every recorded `play` with mode `next`/`after` now throws NOT_RESERVABLE, and rv/ra/rn values, collateral, c_quick, empty_clock, growth and relic slot bonuses all changed. As a result, every gen_traces battle and therefore every Run path diverges from its old recording. Each witness below was re-derived from the current `tools/gen_traces.mjs` `generateScenario` commands. None was hand-forged, and each still exercises the same behaviour.

### Assertion and scenario changes

- engine/tests/run_progression_vectors.test.mjs :: "five fixed gen_traces inputs replay ..." (now "seven ...") — asserted: a 5-case list (seed1102 legacy/v2 = begin-defeat, seed2654 = earned AP) and denominators {cases 5, seeds [1000,1102,2654], legacy 4, v2 1, full 4} — no longer applies because: once reservation was removed, seed1102 full clears with no battle ending inside begin, and seed2654 earns ev_rift only once — replacement covered by: the same test now asserts the 7-case list (1102 legacy/v2 kept as ordinary full exploration, new seed3561 legacy/v2 begin-defeat, seed2992 earned-ap-bonus) and denominators {cases 7, seeds [1000,1102,3561,2992], legacy 5, v2 2, full 6}.
- engine/tests/run_progression_vectors.test.mjs :: "a newly earned AP bonus reaches only the next battle ..." — asserted: seed2654 earns the bonus twice, with eight base-AP battles between the gains; begin-defeat closes for seed1102-full-legacy/v2 — no longer applies because: reservation removal changed those runs (see above) — replacement covered by: the same test with rerun = seed2992-full-legacy (two gains, each spent at AP 6, five base-AP-4 battles between) and the begin-defeat loop over seed3561-full-legacy/v2 (an infinite_circuit turn-start loss of 2 at HP 1). All structural assertions are unchanged.
- engine/tests/run_progression_vectors.test.mjs :: "gzip export is deterministic ..." — asserted: summary.cases === 5 — no longer applies because: the plan grew to 7 when the seeds were swapped — replacement covered by: the same assertion with 7.
- engine/tests/run_node_contracts.test.mjs :: "immutable witnesses: frozen original data, three real full runs ..." (now "frozen current data") — asserted: witness seeds repeatEvent 1000, nextApLifetime 1036, secondShopRemoval 1020, plus the old data hash — no longer applies because: the old prefixes contain reserve-mode plays (NOT_RESERVABLE now) and reservation-dependent outcomes — replacement covered by: the re-derived witnesses 4211 / 3084 / 1386 (current data hash) in the same test.
- engine/tests/run_node_contracts.test.mjs :: "seed1036: ev_rift AP+2 ..." (now "seed3084: ...") — asserted: the seed1036 prefix. Its fixture observations also recorded the pre-fix engine's values: Battle copy 2, refund 2, second battle AP 7. — no longer applies because: the reserve-mode prefix cannot be replayed, and a re-recorded prefix can only show the current engine — replacement covered by: seed3084 (chrono_crown base 5, ev_rift at 171, first battle AP 7 at 173, victory at 183, next battle AP 5 at 189). Every test assertion is kept; only the comments about the old observed values were reworded.
- engine/tests/run_node_contracts.test.mjs :: "seed1020: a shop sells its single card removal once ..." (now "seed1386: ...") — asserted: the last prefix command buys removal of 'k5', and k5 leaves the deck — no longer applies because: reserve-mode prefix — replacement covered by: seed1386, the run's first shop removal of 'k4' at 85 (price 50), with the same NOT_FOR_SALE, no-mutation and later-shop-75 assertions.
- engine/tests/run_node_contracts.test.mjs :: "seed1000: a resolved ev_archive rejects reselection ..." (now "seed4211: ...") — asserted: attempt eventChoose [0,'k2']; witnessed [hp,maxHp,coins,deck] = [149,238,138,19] — no longer applies because: reserve-mode prefix — replacement covered by: seed4211 ev_archive at 62, attempt [0,'k16'], witnessed [161,238,148,19]. The maxHp -5, two removals and BAD_CHOICE assertions are unchanged.
- engine/tests/run_node_contracts.test.mjs :: "seed1000: a key-array first ev_archive choice ..." (now "seed4211: ...") — asserted: the same behaviour on the seed1000 prefix — no longer applies because: reserve-mode prefix — replacement covered by: the seed4211 repeatEvent prefix, with the test body unchanged.
- engine/tests/run_node_contracts.test.mjs :: "continuation witnesses: ... recorded on engine 3ad ..." (now "engine 653d") — asserted: continuation sourceEngine 3ad9751b…; shape nextEvent [1000,full,legacy,119], openingDefeat [1102,full,legacy,221] — no longer applies because: both prefixes were recorded with reserve modes on the old engine — replacement covered by: sourceEngine 653dfec4… (the current engine/rules-core.js), nextEvent [4211,full,legacy,79], openingDefeat [3561,full,legacy,213].
- engine/tests/run_node_contracts.test.mjs :: "seed1102: a battle that ends inside begin ..." (now "seed3561: ...") — asserted: the opening defeat at z2f3n1 on seed1102 — no longer applies because: seed1102 no longer reaches it — replacement covered by: seed3561 enter z2f3n2 (HP 1, infinite_circuit loss 2, DEFEAT inside begin), with the full-state baseline assertions unchanged.
- engine/tests/run_node_contracts.test.mjs :: "seed1000 continuation: repeats of a done ev_archive ..." (now "seed4211 continuation") — asserted: [119, 96, ev_archive, 118, ev_promise] — no longer applies because: reserve-mode prefix — replacement covered by: [79, 62, ev_archive, 78, ev_promise] (ev_promise choice 1).
- engine/tests/run_node_contracts.test.mjs :: "seed2654 packet: each newly earned ev_rift +2 ..." (now "seed2992 packet") — asserted: sourceEngine dae99192…, prefix 1525, rifts at 78/1519, [9,1] battles after each — no longer applies because: the reserve-mode prefix, and seed2654 now earns once — replacement covered by: seed2992, sourceEngine 653dfec4…, prefix 289, rifts at 63/282, [6,1] battles (AP 6 then base 4, then AP 6).
- engine/tests/run_node_contracts.test.mjs :: "seed1102 generated legacy/v2 trace: the opening defeat ..." (now seed3561) — asserted: generateScenario(1102) reproduces the frozen opening defeat — no longer applies because: seed1102 clears now — replacement covered by: generateScenario(3561) legacy and v2, which match the new witness (same shape in both policies; the defeat save offers 8 changes).
- engine/tests/run_node_contracts.test.mjs :: "seed1102: a fixed boss that throws inside startBattle ..." (now seed3561) — asserted: the z1boss entry on the seed1102 prefix — no longer applies because: reserve-mode prefix — replacement covered by: the seed3561 prefix (z1boss entered at 72), with the body unchanged.
- engine/tests/run_memory_export.test.mjs :: "four bases replay real gen_traces prefixes ..." — asserted: both defeat bases are [1102,'full'] — no longer applies because: seed1102 full ends in a clear now, so the defeat bases were NOT_COVERED and the keep-two/preview-two ops were missing — replacement covered by: defeat bases [3561,'full'] (a real DEFEAT save, ≥2 changes), with all 15 scenarios and 88 ops COVERED again.
- engine/tests/trace_export.test.mjs :: "seed 1135 skips a real reserve-only card disabled by c_quick and completes the run" — asserted: allowedModes returns [] for com_overwatch + c_quick, and the policy skips it — no longer applies because: there are no reserve-only cards (all res 'n'), and c_quick is now +20% only (no noReserve) — replacement covered by: "seed 1187 offers c_quick and retained (held) cards only now ...". It asserts that every offer is exactly ['now'], that sera_round+c_quick is offered, that the 보존 card sera_timed is offered with held === true after being kept across an end of turn, that every play is mode 'now', that the run reaches DONE, and that replay is identical.
- engine/tests/unity_run_reference.test.mjs :: "Unity full Run reference ..." — asserted: counts.actualCommands === 592 — no longer applies because: seed1000-short and seed1102 legacy/v2 replay different command sequences without reservation (1102 is now a full clear) — replacement covered by: the same assertion with 768, plus the byte-equal regenerated reference JSON.

### Generator scenario changes

- tools/gen_run_progression_vectors.mjs: the plan went from 5 to 7 cases. seed1102-full-legacy/v2 change role from begin-defeat to ordinary-exploration; their ids are kept because unity_full_run_reference_generator.mjs and RunMemoryFlowPlayModeTests.cs select them by id. Added seed3561-full-legacy/v2 (begin-defeat). seed2654-full-legacy was replaced by seed2992-full-legacy (earned-ap-bonus, two earned and two consumed gains). The scope/help text changed from "five" to "seven".
- tools/gen_run_memory_vectors.mjs: the legacy-defeat and v2-defeat candidates changed from [[1102,'full']] to [[3561,'full']]. This was forced because the defeat scenario can no longer be produced from 1102. Nothing else changed.
- engine/tests/vectors/run_node_contracts.json: every witness was re-derived from current gen_traces commands with the scratch builder scratchpad/work/build_node_contracts.mjs. The seeds are repeatEvent/nextEvent 4211, nextApLifetime 3084, secondShopRemoval 1386, openingDefeat 3561, and reearnedRift 2992. sourceEngine (top level, continuations and reearnedRift) = 653dfec4…, and sourceData = the current data hash.
- reports/run_memory_core_20261016/unity_full_run_reference_generator.mjs: not changed. It has no pinned hash; only its output JSON was regenerated.
- Seed choice: 8000 full legacy seeds (1000–8999) were scanned with the current engine. Begin-defeat appears in only 6 seeds; 3561 is the shortest on a battle node and reaches z1boss first.

### Regenerated fixtures (each equals fresh generator output)

- engine/tests/vectors/run_progression_vectors.json.gz — `node tools/gen_run_progression_vectors.mjs`, then `--check` OK (7 cases, 1899 attempted).
- engine/tests/vectors/run_memory_vectors.json.gz — `node tools/gen_run_memory_vectors.mjs`, then `--check` OK (4/4 bases, 15/15 scenarios, 88 ops, 0 NOT_COVERED).
- engine/tests/vectors/run_node_contracts.json — the scratch builder (above); verified by the 13 replay tests.
- reports/run_memory_core_20261016/unity_full_run_reference.json — the generator, then `--check` OK (768 commands).

### Downstream (not edited; owned by others)

- core/MemoryForge.CoreChecks/RunProgressionChecks.cs:38,43-47 pins the 5 old case ids, seeds and counts. They need to become the 7 new cases. The two-earned/two-consumed check at :207 holds for seed2992.
- core/MemoryForge.CoreChecks/RunProgressionChecks.Memory.cs:48-49 still lists defeat candidate (1102, "full"). It should be (3561, "full").
- The fixtures pin the SHA-256 of engine/rules-core.js, data/game_data.json, tools/gen_traces.mjs and engine/tests/fuzz_runs.mjs. Any later edit to these must be followed by rerunning the generators above, the builder, and the Unity reference generator.

## Direct engine unit tests

All paths are under engine/tests/. "Removed" = test deleted; "changed" = scenario rewritten or assertion value changed.

### Removed tests (10)

- engine.test.mjs :: reservation slots: next 2, after 1, SLOT_FULL, resolve at next turn start with rv — asserted: two next + one after reservation fill the slots, SLOT_FULL, rv damage at turn-2 start, after-reservation still waiting — no longer applies because: reservation modes/slots/rv removed; every card is now-only — replacement covered by: engine.test.mjs :: every card is played now: next/after modes are rejected with NOT_RESERVABLE and nothing is reserved
- late_enemy_actions.test.mjs :: sealSlot ignores reservations due before the protected following-player turn — asserted: sealSlot seals even when next slots are full of reservations due earlier — no longer applies because: players cannot create reservations, sealSlot acts only on slots — replacement covered by: late_enemy_actions.test.mjs :: sealSlot and delayRes leave a held 보존 card in hand, held and playable now (plus the unchanged "sealSlot expires after its protected turn…" test)
- late_enemy_actions.test.mjs :: sealSlot gains enemy block 10 when future base slots are full — asserted: after-slot reservations (cracked_hourglass slot) make sealSlot fall back to block 10 — no longer applies because: reservations and cracked_hourglass's after slot removed (it now draws +2) — replacement covered by: late_enemy_actions.test.mjs :: sealSlot and delayRes leave a held 보존 card…; engine.test.mjs :: draw relics…
- late_enemy_actions.test.mjs :: delayRes shifts the newest reservation due date while preserving its power kind — asserted: delayRes moves res.due +1 and keeps kind — no longer applies because: no reservation can exist — replacement covered by: late_enemy_actions.test.mjs :: sealSlot and delayRes leave a held 보존 card…; delayRes with no reservation leaves RNG and card zones unchanged (unchanged)
- late_enemy_actions.test.mjs :: delayRes discards an overflowing reservation without refund or losing its card — asserted: overflowing delayed reservation is discarded without AP refund — no longer applies because: reservations/slots removed — replacement covered by: same as above
- late_enemy_actions.test.mjs :: delayRes discards a reservation shifted outside the two-turn board — asserted: after reservation delayed twice is discarded — no longer applies because: reservations removed — replacement covered by: same as above
- late_enemy_edges.test.mjs :: an ordinary after reservation is not marked as enemy-delayed when its column rolls forward — asserted: res.delayed flag absent on a normal after reservation — no longer applies because: after mode removed — replacement covered by: late_enemy_edges.test.mjs :: delay preview is pure and matches the actual no-op decision once every card resolves now
- late_enemy_edges.test.mjs :: successful enemy delay carries an explicit marker independently of the value kind — asserted: res.delayed = true after delayRes — no longer applies because: reservations removed — replacement covered by: same as above
- late_enemy_edges.test.mjs :: player pull clears the enemy-delay label when it changes the reservation power kind — asserted: pull op converts after→next and clears delayed — no longer applies because: pull op removed from content (sera_pull is now recall) — replacement covered by: engine.test.mjs :: held is read and cleared on play, and reset when a card is drawn or recalled (recall path)
- post_resolution_end.test.mjs :: post-resolution victory discards queued reservations without using them or granting gauge — asserted: due/future reservations go to discard unused at a post-resolution victory, harin gauge 0 — no longer applies because: no queued reservations exist — replacement covered by: post_resolution_end.test.mjs :: post-resolution void shard victory ends the battle inside the play command (asserts gauge 10 only for the resolved card, exhaust, conservation)

### Changed tests / assertions (23)

- engine.test.mjs :: break: weakness hits empty the gauge → stun, +25% damage taken, +1 AP next turn — asserted: broken hit = 10 (8 × 1.25) — no longer applies because: sera_foresight (+25% vs broken) was reservation-only and now applies to every Sera attack — replacement covered by: same test, now asserts floor(8 × (1 + 0.25) × (1 + brokenDamageBps)) = 12, derived from rules/passive in the test
- engine.test.mjs :: echo-circuit and copy tokens stay cost 0 after the turn they were created — asserted: copy scheduled only when the c_echo card resolves as a reservation (two ends) — no longer applies because: echo copy is now queued on any resolution — replacement covered by: same test, plays now on turn 1, copy due turn 2, free/cost 0 on turns 2 and 3, copy has no circuit
- late_enemy_edges.test.mjs :: delay preview is pure and matches the actual discard decision — asserted: preview of a next reservation in a sealed one-slot board = discard — no longer applies because: reservations/slots removed — replacement covered by: renamed "…matches the actual no-op decision once every card resolves now": after a now play preview stays null, is pure, delayRes emits {uid:null, discarded:false} and leaves zones unchanged
- post_resolution_end.test.mjs :: post-resolution void shard victory stops turn startup before drawing — asserted: an after-reserved sera_last resolving at turn-3 start, void shard kill, no draw, result turn 3 hp 57 — no longer applies because: no turn-start resolution; sera_last is now 보존+exhaust — replacement covered by: renamed "…ends the battle inside the play command": sera_last held from turn 1, played now on turn 2, result {VICTORY, turn 2, hp 81 − reflect 3}, dmg events [card, relic], one end, no turn event, exhausted once, held cleared
- post_resolution_end.test.mjs :: a reservation that already ended the battle emits one end event — asserted: a next-reserved sera_round kills at turn-2 start, one end — no longer applies because: reservations removed — replacement covered by: renamed "a held card that ends the battle emits one end event" (held sera_timed played now on turn 2)
- post_resolution_end.test.mjs :: post-resolution victory cannot become a smudge defeat on a spurious extra turn — asserted: reserved sera_last scenario, result turn 3 hp 2 — no longer applies because: reservation setup removed — replacement covered by: same name; 5-card deck (4 sera_last + smudge), HP 7 − smudge 2 − reflect 3 = 2 with smudge back in hand, victory holds, no smudge loss, end rejected with WRONG_PHASE; assertConserved takes the deck size
- player_lethal_reflect.test.mjs :: reserved fatal reflect stops later hits and returns every pending reservation once — asserted: reserved sera_double fatal reflect at turn start, pending reservations returned once, idempotent replay — no longer applies because: reservations removed — replacement covered by: renamed "held fatal reflect stops later hits, leaves the other held cards untouched and replays idempotently" (c_delay-held sera_double, enemy hp 7 → 3, one dmg, other held cards stay in hand unused, replay equal)
- reflect_ultimate.test.mjs :: immediate and reserved card hits still trigger the unchanged reflect value — asserted: a next-reserved sera_round triggers reflect 3 — no longer applies because: reservations removed — replacement covered by: renamed "immediate and held card hits…" (c_delay-held sera_round played now on turn 2 triggers reflect 3)
- review_v071.test.mjs :: R071-09 echo-delivered evolved card retains lineage / half-power / free — asserted: copy appears after a next reservation and two ends — no longer applies because: next mode removed; copy queued on now resolution — replacement covered by: same test, play now + one end, copy in hand with lineage/echoHalf/cost 0
- content_modes.test.mjs :: rift_reserve: reserved damage +30%, immediate damage −10% — asserted: dmgBonusBps keyed on ctx.reserved next/after — no longer applies because: rift_reserve now keys on ctx.held — replacement covered by: renamed "rift_reserve: held card damage +30%, every other card hit −10%": diff on {held:true}/{held:false}/{reserved:'next'}, then real plays: fresh sera_timed 20 vs floor(20 × 0.9), held sera_timed 20 vs floor(20 × 1.3), derived from data
- battle_turn_vectors.test.mjs :: ordinary turn two clears opening block/counter… — asserted: draw-max case has 5 cards on ordinary turns — no longer applies because: twin_pendulum's slot became +1 draw per turn — replacement covered by: same test, handDraw + 1
- battle_turn_vectors.test.mjs :: burn bypasses seed block… — asserted: notebook-continuing hand 6 at turn 3 — no longer applies because: sera_timed is 보존 and stays in hand — replacement covered by: same test, handDraw 5 + notebook 1 + held sera_timed = 7, and the only held card is sera_timed
- battle_turn_vectors.test.mjs :: curse insertion creates conserved tokens… — asserted: exact smudge instance shape without held — no longer applies because: card instances gained held:false — replacement covered by: same test with held:false in the expected shape
- battle_turn_vectors.test.mjs :: multi-hit death, envelope, curse cleanup and next-turn circuit… — asserted: circuit-fatal turn-2 hand empty — no longer applies because: sera_timed (보존) stays in hand — replacement covered by: same test, hand is exactly [sera_timed, held] (still proves no draw happened)
- battle_turn_vectors.test.mjs :: a fatal hit keeps its own counter and marked reward… — asserted: hand 1 (notebook draw) — no longer applies because: held sera_timed also stays — replacement covered by: same test, hand 2 with sera_timed the only held card
- battle_opening_vectors.test.mjs :: each vector retains complete real CREATED and committed snapshots… — asserted: 15 card fields — no longer applies because: new held field — replacement covered by: same test, 16 fields and held === false on creation
- battle_opening_vectors.test.mjs :: real opening edges cover seeds… — asserted: draw-max opening has 0 burnCard / empty discard — no longer applies because: twin_pendulum adds +1 draw on turn 1 too — replacement covered by: same test, overflow = handDraw 5 + quick_hands 2 + w_clock_core R5 2 + twin_pendulum 1 − handMax 9 = 1 burned card, derived from data
- battle_player_query_vectors.test.mjs :: player query oracle covers all 173 base cards and every 378 authored legal mode pair — asserted: 378 legal (card, mode) pairs and the y/n/o/after mode table — no longer applies because: every card is res 'n' — replacement covered by: renamed "…each one's single legal now mode": all res 'n', pairs = data.cards.length (173)
- battle_player_query_vectors.test.mjs :: combat catalog preserves full cards… — asserted: 39 op tags / 35 op fields — no longer applies because: slot/pull ops and rv/ra/rn/next/after fields removed from data — replacement covered by: same test, 37 / 30 and explicit absence of the removed tags and fields
- battle_player_query_vectors.test.mjs :: auto upgrade uses ceiling13/10… — asserted: rv/ra upgraded values (sera_round rv 16, sera_timed 21/29/39, harin_counter rv 10, aila_bell rv 12) — no longer applies because: rv/ra removed and sera_timed v changed 16→20 — replacement covered by: same test; sera_round v 11, sera_timed ceil(20 × 13/10) = 26 brk 3+2 = 5, harin_counter blk 7, aila_bell ceil(5 × 1.3) + 2 = 9 (derived in comments)
- battle_player_query_vectors.test.mjs :: noReserve leaves reserve-only cards without modes while pure cost queries still return disallowed mode costs — asserted: c_quick (noReserve) leaves sera_timed/sera_last with no modes; empty_clock discounted only next/after — no longer applies because: c_quick lost noReserve, no reserve-only cards, empty_clock now discounts now too — replacement covered by: renamed "c_quick keeps every card playable now and empty_clock discounts the first card in every mode without being consumed by queries" (costs derived from data: cost, cost − 1, max(0, cost + c_lite − 1); queries do not set emptyClockUsed or change AP)
- battle_player_query_vectors.test.mjs :: target requirements include nested status branches… — asserted: aila_bell/wc_choir_bell/com_patch effective eff with rv — no longer applies because: rv removed — replacement covered by: same test without rv
- balance_causes.test.mjs :: analysis rejects altered target bounds and assessment results with unchanged source hashes — asserted: failedMetrics 5 on the 1/normal e_hound probe — no longer applies because: the random policy can no longer pick next/after modes, so it now finishes in 2.0 turns (was 2.5) with 2.1% HP loss (was 5.06%), dropping below the 4% floor — replacement covered by: same test pins failedMetrics 6 and that random winPct is the only passing metric. Note: this is a seeded aggregate simulation, so the value can't be worked out from the authored data by hand; I checked the cause by rerunning the probe with the pre-change data/game_data.json (5 failures, random hpLossPct passing) against the new data (6 failures).

### Added tests (10)

- engine.test.mjs :: every card is played now: next/after modes are rejected with NOT_RESERVABLE and nothing is reserved
- engine.test.mjs :: retain: the 보존 keyword and the c_delay part keep a card in hand as held; other hand cards are discarded
- engine.test.mjs :: held is read and cleared on play, and reset when a card is drawn or recalled (time_stamp +5 observes ctx.held)
- engine.test.mjs :: collateral: block v plus debt 1 due next turn; rejected with DEBT_CAP at the cap
- engine.test.mjs :: empty_clock: the first card of the battle costs 1 less in now mode, once
- engine.test.mjs :: draw relics: twin_pendulum and infinite_circuit +1 draw every turn, cracked_hourglass +2 on turn 1 only
- engine.test.mjs :: grow: a growth card grows when it resolves now, once per card per battle
- engine.test.mjs :: held-keyed bonuses: precision_lens break +1 and w_reservedDmg apply only to a held card
- engine.test.mjs :: sera hooks without reservations: r1 draws on any 유리탄 play once per turn, r6 is +50% on the turn after her ultimate
- late_enemy_actions.test.mjs :: sealSlot and delayRes leave a held 보존 card in hand, held and playable now

Helper change (not a test): engine.test.mjs battle() now also accepts card spec objects (e.g. { id, mods }) in deck.

## Other generator edit

- tools/gen_battle_opening_vectors.mjs: one comment only. It said the strongest opening cannot overflow; it now says twin_pendulum's slot became a draw, so 5 + 1 + 2 + 2 = 10 draws against the cap of 9 burns one card. battle_opening, battle_turn and battle_player_query vectors were regenerated, because they pin that file's hash.

## Engine findings (not fixed, per instructions)

- engine/rules-core.js:1279-1281 (`discard` op) and engine/rules-core.js:924-927 (`exhaustCard`) move a card out of the hand without clearing `inst.held`. RULES_NOW_PLAY §1.2 says held becomes false when the card leaves the hand by play, discard or exhaust. No mechanic reads it there, because draw (rules-core.js:1472) and recall (:1393) reset it, but the saved state differs from the spec. The tests here assert only the post-draw and post-recall values.
- engine/rules-core.js:827 overwrites `battleFlags.seraR6Turn` with `turn + 1` on every Sera ultimate. If Sera uses her ultimate again during the boosted turn T+1, her later attacks that turn lose the +50% (:1032 needs `seraR6Turn === turn`), although §2 says every Sera attack on the turn after an ultimate gets it. This needs 100 gauge twice in consecutive turns, so it is rare.
- Not a contradiction, but noted: the silence late-enemy move only taxes next/after plays (rules-core.js:393), so under now-play it has no effect. sealSlot and delayRes act only on reservations, so they are also inert. These are candidate-only moves (proposals/late_enemies.cjs); no live enemy uses them.

## Downstream owners (not edited here)

- core/MemoryForge.CoreChecks/RunProgressionChecks.cs:38,43-47 still pins the 5 old run-progression case ids, seeds and counts. The fixture now has 7 cases (seed3561 begin-defeat, seed2992 earned AP bonus). RunProgressionChecks.Memory.cs:48-49 should use defeat seed (3561, "full").
- The player-action, preview and late-action fixtures changed their case sets: removed reservation cases, renamed ids, and new held/retain/collateral cases. C# consumers keyed on old ids or counts need to follow.

## Follow-up by the root session (2026-10-19)

- Engine fixes from the test agent's report: held is cleared when an effect discards or exhausts a card; Sera r6 is now `battleFlags.seraR6Active` (set when the boosted turn starts), so a second ultimate on the boosted turn keeps that turn's +50% and arms the next one.
- engine.test.mjs :: sera hooks without reservations — asserted `seraR6Turn` reset to 0 at the boosted turn's end — the reset was the bug above — asserts `seraR6Active` 2 after turn 3 begins (damage 8, 12, 8 unchanged).
- gen_battle_player_action_vectors :: edge-sera-r6-follow-up — `sera-r6-cleared-at-its-turn-end` (seraR6Turn 0) — same reason — `sera-r6-kept-into-its-turn` adds seraR6Active 2; `sera-r6-ends-with-its-turn` asserts turn 3 with seraR6Active still 2.

## Browser prototype (prototype/tests/late_enemy_actions.mjs)

- removed: candidate delayed reservation is labeled from its explicit delayed flag; ordinary after reservation entering the next column has no false delay marker; candidate overfilled delay shows discard and no AP refund in battle log; candidate overfilled delay warns before end turn that the card will be discarded without refund — asserted the delayRes label, after-column rollover and overflow discard on reserved cards — reservations removed, so delayRes has nothing to delay — replacement: "candidate prompter with no reservation logs a harmless empty delay" (kept) and the engine late-action vectors' delay-no-reservation case.
- changed: crowded candidate battle stays usable at desktop, mobile, and 130 percent text — asserted five filled reservation slots and their usable areas — no slots exist now — still asserts five enemies and a ten-card hand are visible, unclipped and pressable at desktop, mobile and 130% text, and that the last hand card can be selected.

## Unity PlayMode (client/MemoryForgeUnity/Assets/Tests/PlayMode)

Format: test — what it asserted — why that no longer holds — what replaces it.

### Pinned fixtures (regenerated, counts follow the generators)

- BattleLateActionsPlayModeTests — 81 cases / 473 commands / 445 committed / 26 rejected, 34 coverage edges — five reservation-only cases left the late-action corpus — 76 / 437 / 415 / 20, 29 edges.
- RunProgressionPlayModeTests — the old JS run hashes — the run fixture was regenerated by reports/run_progression_core_20261016/unity_reference_generator.mjs — the new reference.
- RunMemoryFlowPlayModeTests — 592 actual commands — the bounded driver now plays every card now, so the same runs take more commands — 768.
- BattlePresentationQueryPlayModeTests (mirrors CoreChecks) — the reservation board's seal came from collateral — collateral now gives block and debt 1 — the seal comes from a test enemy's sealSlot, as in CoreChecks EnemySeal; the test card t_seal became t_cheap.
- EffectCandidateCoreWitnessTests — sera_pull requests a reservation (pull), and empty_clock lowers only a reserved card's cost — sera_pull is now RECALL, empty_clock applies to any first card — recall's discard request; the cost literals follow empty_clock.

### Reservation tests replaced by now-play tests

- ExpeditionPickerGuardPlayModeTests.TheSharedCandidateHelperFiltersSourceZoneRepeatsAndGrowthOnCoreBattleStates — sera_pull offers exactly the reserved card — no reservation zone — recall offers the discard pile in order, and a picked card is not offered again.
- BattleClientPlayModeTests.ReservationResolvesOnTheNextTurnAndResetRebuildsOneOwnedView — renamed EndTurnKeepsOnlyRetainedCardsAndResetRebuildsOneOwnedView — next is NOT_RESERVABLE; after the end only 보존 cards are held and kept in hand, and the rest are discarded (checked when no reshuffle happened); the reset part is unchanged.
- BattleClientPlayModeTests.TimelineKeepsEveryReservationAfterSeraUltimateAndLabelsSealedSlots — renamed SeraUltimateDrawsACardAndCollateralBorrowsInsteadOfSealingASlot — Sera's ultimate draws 1 and opens no slot; collateral seals nothing and adds one debt due next turn; the timeline has no card or sealed cell.
- ExpeditionClientPlayModeTests.ActionLineShowsCoresForecastWhileSelectingAndOnlyTheConfirmedPlayMatchesItOnce — at least one reservation forecast in the opening hand — now: zero, and every hand card offers only now.
- ExpeditionClientPlayModeTests.ForecastLabelsOfAuthoredCoreBattlesWarnDefeatAndHideDrawResults — the reflect and nested-draw reservation estimates — now: both requests are NOT_RESERVABLE, and the text is the error, not an estimate. The immediate literals are unchanged.
- ExpeditionClientPlayModeTests.ReservationShowsItsCurrentStateEstimateAndOnlyTheConfirmSchedulesItWithoutApplyingIt — renamed AReservationRequestIsRefusedAndTheViewOffersNoModeChoice — no Mode: button is drawn; choosing a card chooses now; next and after through the client are NOT_RESERVABLE and change nothing.
- ExpeditionClientPlayModeTests.ReservationEstimateLabelsOfAuthoredCoreBattlesKeepCostDueAndCurrentStateSemantics — renamed FormerReservationSourcesPlayNowWithTheirImmediateValues — harin_shield gives block 8 now (state block 8, AP 3); sera_timed has 보존 and deals 20 now at cost 2; their reservations are refused.
- ExpeditionTurnFlowPlayModeTests: ReserveAndEnd became PlayAndEnd (one plain play, then end).
  - ReservationRowsRevealOnTheApprovedTimer… — renamed TheTurnSummaryShowsAtOnceAndDisableOrResumeLeaveTheSameCommittedState — no reservation row and no empty "시간 예약" count; the summary is fully shown at once. The same end, disable/enable and resume keep the same state.
  - AHeldContinueSurvivesTheRevealSteps… — renamed AHeldContinueSurvivesFramesAndItsReleaseClosesTheSummaryOnce — the press is held 10 frames instead of across reveal steps.
  - TimeReservationSlotsBeneathTheSummaryAreOnlyCoresReservations — renamed NoTimeReservationSlotIsDrawnBeneathTheSummary.
- PreparedIntentPlayModeTests.NoSpentCardOrRefusedRequestBecomesAnIntentAndANextRequestTakesNoSlotUntilSent — renamed …AndAPlainRequestIsSentOnce — next and after are refused like an unknown mode (NOT_RESERVABLE, no intent); a plain waiting request is sent once by the next Update.
- OnboardingPlayModeTests.FirstRunCoach… — lesson 1 without battle.mode; lesson 2 is battle.intent then battle.retain (two acknowledgements); the boss lesson is boss.timing only. The capture test stops at battle.retain.

### Mode button removed

- ActionPresentation, ExpeditionClient (2) and PreparedIntent tests — clicked Mode:now — the button is gone — the card click chooses now.
- ActionPresentationPlayModeTests.EveryPaceAndSkip… — the pace label is checked after the play, because the strip with the pace buttons is drawn only while a result plays.

### Witness search widened

- ActionPresentationPlayModeTests.AnActualUltimateIsPresentedAsItsOwnCommittedEvents — seed 1234's short run filled a gauge — battles are shorter, and this run fills none — it tries seeds 1234, then 1-8 in order (each practice destroyed first) until actual commands fill a gauge; the ultimate assertions are unchanged.
  - Measured with a greedy bot, 30 short runs (sera/harin/nia): battles with a ready ultimate went from 20% to 15.5%, ultimates from 211 to 134, and turns per battle from 6.9 to 5.4. This is a balance item for the retune.

### Client text (not tests)

- The turn summary no longer prints "시간 예약 0장 → 0장". The end-turn notice is "적의 행동을 마쳤어요." (expedition) and "적의 행동을 마쳤습니다." (practice).

### Onboarding: the ultimate tip no longer blocks the boss lesson (client change and test)

- Found by OnboardingPlayModeTests.FirstRunCoach…: seed 1234's guided short run fills no gauge, so `battle.ult` (a Core step of lesson 3) never finished. Lesson 3 then never finished, and the boss lesson (Needs 3) never opened.
- Client: `battle.ult` is now `Core = false`. It still shows when the first gauge fills after the break step, but it is not required to finish lesson 3.
- Test: lesson 3 needs `battle.break` and `save`. The run records whether a gauge filled after the break step. If one did, `battle.ult` must be shown and done after `battle.break`; if none did, the tip must not have shown. §14.5's ③-before-④ order is checked with `battle.break`.
- PreparedIntentPlayModeTests.AConfirmDuringTheDisplay… — asserted the drawn timeline equals Core's slot cells — the battle screen no longer draws the reservation rail — asserts no ReservationSlot is drawn.

## Codex review round 1 (session 01a15289-d2cb-71e1-ac58-7e1ecb053499, REQUEST_CHANGES, 5 × P2) and what changed

1. Sera's bonuses skip her ultimate — kept by design: v0.8.1 never applied foresight, seraLock or r6 to the ultimate. The texts now say «세라의 카드 공격» (Sera passive, r6, the seraLock buff text in JS and C#, docs/RULES_NOW_PLAY.md).
2. A winning blow skipped its growth — fixed in both engines: when the effect loop stops on VICTORY, the card's remaining `grow` ops still run.
   - New tests: engine.test.mjs "grow: a winning blow still grows its card"; vector case edge-growth-on-winning-blow (witness grow-on-victory).
   - Pins updated to the regenerated fixtures:
     - BattlePlayerActionChecks: 779 cases, 217 witnesses, 2966 requests / 2940 committed / 1198 Check probes.
     - RunProgressionChecks: the 1000-trace corpus has 227145 attempts and 571 forgeOffer returns, and its per-method counts were updated.
     - run_node_contracts.json sourceData: the data hash after the text edit. Every recorded replay still passes.
3. The 보존 label missed part-granted retain — the client now asks Core: new public `Battle.Retains(uid)` / `Run.BattleRetains(uid)`.
4. The server hand view lacked `held` — PublicView now exports `held` and `retain`, and ServerChecks compares both with the oracle on every served hand card.
5. Home panels stayed live over a moment — `Play()` now closes the sortie and settings panels too.
- RunProgressionPlayModeTests — the embedded Reference — regenerated after the growth fix (unity_reference_generator.mjs) — refreshed from reports/run_progression_core_20261016/unity_reference_actual_js.json.
- ExpeditionClientPlayModeTests (3 literals) — the forecast line began "예상 · 즉시 · " — every card is played now, so the line drops the mode label for now (a refused reservation still names its mode) — "예상 · 행동력 …".
