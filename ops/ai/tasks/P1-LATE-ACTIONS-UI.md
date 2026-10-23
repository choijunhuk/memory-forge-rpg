# P1-LATE-ACTIONS-UI

Base:4793fcb; branch claude/late-actions-ui; relativeworktree claude-late-actions/Memory_forge.
User requests six late enemy actions + paired balance, preserve all current design/numbers, subscription only, commit/PR. You are not alone: Codex owns engine/fixtures/docs; native executor owns balance runner and tests. You own ONLY prototype/src/ui.js. Keep everyexisting screen/content. No image generation, Bash, Write, extraagents or recursive AI.

Engine contract implemented in parallel by Codex:
- s.apDrainNext accumulates nonnegative enemy AP drain for NEXT player turn; independent of s.debts/apNext. Startturn subtracts debt+drain and clamps0..APmax, then clears drain.
- s.silenceUntil=absolute next player turn, fixed +1 nonstack only reservation costs; inst.free and freeNext still final0. Expires after that player turn.
- sealSlot matches collateral (sealedUntil), enemy block+10 fallback when active/empty.
- delayRes newest order, due+1 with original kind/power kept, following-player-turn base slot capacity; no slot/beyond two-column horizon ->discard; prepaid AP never refunded.
- eatName token-priority seeded discard, uses exhaustCard hooks.
- enemy definition.phase={t:'phase',hp,brk,weak,pattern}; first lethal boss damage phaseIndex1, sameUID, hp/brk/weak/pattern replace, block/status/stun/broken reset, nooverkillcarry; later player hits continue newbody; killhooks only finaldeath. e.pattern stores activephasepattern, otherwise definition.pattern. Numericprofiles copied into test-only proposals/late_enemies.cjs (NOTliveworlddata/story ready).
Events: apDrain{uid,n}, sealSlot{uid,until,fallback:boolean}, delayRes{uid,from,to,kind,discarded:boolean} (uid reservationcard), eatName{uid|null} (carduid), phase{uid,index:1,hp,brk}, existing exhaust/kill preserved. Core publishes apNext,apDrainNext,silenceUntil in publicView.

Task: add Korean INTENT_TEXT descriptions for all6; show upcoming drain separate from debt and use R.apPerTurn/relic adjustments rather than hardcoded4 in nextAP preview; show active silence +1 reservation badge; phase index on boss and activephasepattern in enemy detail modal; logLine for events, explicit reservation discard/no refund text. Slotcolumn rendering groups by due but note original reservation power/kind when delayed so not suggest a new stronger after effect. Keep controls/escaping/accessibility/currenttheme, noCSSrewrite. Newtypes cost preview already calls engine costOf; don't duplicategameformulas. Don't activate zones3~5 or change content/data/story authored flags. Maintain originals for20 enemies and173 cards.

Read docs/START_CLAUDE.md and relevant§10.4/10.6/UI; edit only ui.js via Edit. Yourengineworktreeold version: implement to contract and report NOT_RUN; Codex will integrateactualcore/rebuild/test. Do notaskforapproval for these authorizedtemporarybattle mechanics. Finishwithchangedareas and limits; root captures sessionid from CLIJSON.
