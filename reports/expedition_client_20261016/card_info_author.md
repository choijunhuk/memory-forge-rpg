# Card effect information UI — P1-EXPEDITION-CARD-INFORMATION (author: Claude Opus 5.5, High)

Spec: design v0.8.1 §6 card information, §14.3 meaningful decisions. Edited only `MemoryForgeExpeditionClient.cs`,
`ExpeditionClientPlayModeTests.cs` and this file. Core/formatter/data/scenes untouched. No Bash, MCP, agents or image work.

## Verification status
- Compile: **NOT_RUN** (no Bash/Unity in this session). Source-read only.
- Play Mode (6 existing + 2 new): **NOT_RUN**. Root must compile, run, screenshot and pointer-test.
- Android/device, 130% text, 10-card hand / 5 enemies: **NOT_RUN**, not claimed.

## New public API (client, read-only — none of it reaches the Run)
- `bool ShowHandCardInfo(string? uid = null)` — default is the selected hand card. Text is `BattleText.Card(run.BattleEffectiveCard(uid), data.Memory.Battle)`, which already includes the upgrade and resonance overlays. Facts show Core's `BattleAllowedModes` and `BattleCostOf` per mode in the current state. Notes list the attached circuit/engraving/temporary parts (each part's authored text), this battle's uses `Used/Uses`, the growth counter, and an explicit line saying damage forecast is not shown yet. Returns false if the uid is not in the live hand.
- `bool ShowCardInfo(string cardId)` — source definition for reward cards, evolution branches and shop cards. Notes list evolution candidates.
- `bool ShowDeckCardInfo(string key)` — source definition of an expedition deck spec. If the card is `+`, the basis line says the text is the **pre-upgrade source** and that the actual upgraded values appear in the in-battle detail. Outside battle no public Core API gives the upgraded definition, so nothing is computed or invented. Notes list fitted parts.
- `void CloseCardInfo()`, `bool CardInfoOpen`, `string? CardInfoText` (exact formatter string), `string? CardInfoBasis`.
- The detail is a snapshot. Any successful Run command (`Do`), `Depart`, `Abandon` or `CloseWithoutSave` clears it, so a stale detail never stays visible.

## UI
- Battle: a `카드 설명` button (`CardInfoButton`, 148×52) sits next to the selected card name. Mode, confirm and end-turn buttons are unchanged and stay reachable after closing. Selecting or reading spends nothing.
- Reward: `카드 설명 보기` (`CardInfo:<id>`, 360×52) under each take button. The panel is now 250 high and the header says a tap on the card takes it. Evolution branches have a separate `설명` (`EvolveInfo:<key>:<branch>`, 82×46) beside each evolve button.
- Shop cards: `카드 설명` (`Info:cards:<id>`, 162×54) above the buy button. It never pays.
- Deck sheet: each entry is a button (`DeckInfo:<key>`, 400×50) that opens the detail. Closing returns to the deck sheet.
- Deck-pick sheet (upgrade/remove/variation/event/part): `고른 카드 설명` (`DeckPickInfo`) for the last picked card. Picks are kept.
- Overlay `CardInfo` (1120×740) uses the existing `OverlayPanel` blocker. It shows the title plus a `닫기` button (`CloseCardInfo`, 136×56), then a scroll body with basis, facts, effect text and notes. Each block is sized to `Text.preferredHeight` with best-fit off and vertical overflow, so long names and nested conditions scroll instead of being truncated. All new touch targets are ≥44.

## New Play Mode tests (fresh `Create`, mirror Run, full fingerprint incl. log/ev/RNG)
1. `HandCardInfoIsCoresTextOfTheEffectiveCardAndOnlyTheLaterExplicitConfirmPlays` — checks every opening-hand card's detail against `BattleText.Card` of the mirror's effective card, and that reading leaves selection and fingerprint unchanged. The real pointer on `CardInfoButton` opens the detail. The text, current cost and forecast-gap labels must be readable (not truncated). A click on the obscured confirm point hits no button and spends nothing. After Close, the real pointer on confirm gives exactly one `BattleCommand("play")` equal to the mirror.
2. `RewardShopAndDeckDetailsAreReadableBeforeTakingOrPayingAndNeverChangeTheRun` — drives actual commands to the first REWARD. For every reward card, the detail equals the source formatter text, it is readable, the take button behind it is intercepted, and the fingerprint is unchanged. Every deck spec's detail equals the source text, and the `강화(+)` basis appears exactly for plus cards. The deck-sheet pointer path is exercised and the deck sheet stays open after Close. Then an explicit `TakeCard` (same as the mirror) runs. The test continues with actual commands, preferring shop nodes. In a SHOP, each card detail is checked the same way and the buy button is intercepted. **If the seed's route ends before a shop, the test issues `Assert.Warn` (not PASS for the shop part).**

## Remaining gaps (explicit, not deleted)
- No upgraded (`+`) definition outside battle: deck/rest-upgrade show source text with a label. A public Core query would be needed (a new API, owned by Core).
- Rest "강화" has no before/after preview; forge part offers show the part text only.
- Reward/shop detail has Close only (no Take/Buy inside the overlay); taking still goes through the separate existing buttons.
- No damage forecast (stated in the detail). Advanced forecast, lobby/account/device gaps stay as before.
- Shop-detail coverage depends on the seed reaching a shop (Warn otherwise).
