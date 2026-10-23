# P1-SERVER-REWARD-UI-20261017 — author report (Claude Code, Opus 5.5, requested effort high)

**AUTHOR RUNTIME: NOT_RUN.** Nothing was compiled or run by the author: no Editor, Play mode, uGUI raycast, Core HTTP or Console check.
Root owns the scene, the actual battle win, the before/after version and no-duplicate assertions, the regression run and the opposite
review. This is no claim of a full network game; only the battle reward step is added.

session_id: the same Root-invoked session (`cfcd60b9-d701-4abf-874b-b6915e50765e` per Root's prompt); not independently visible to the
author.

## Change

There is one file in scope: `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeServerPracticeClient.cs`. It is a bounded extension of
the same optional Development view; there is no new framework, partial class or simulation.

- **Render:** `run.Phase == "REWARD"` with `state.pending.type == "reward"` now draws `RewardView()`. The other phases keep the honest
  later-unit notice; its text no longer lists 보상.
- **Content texts (display only), loaded in `LoadNames`** from the existing content: relic name/text, part name/text, and card text
  (Core's `BattleText.Card` of the base definition, in a try/catch).
- **Unchanged:** `Ready`, `CanCommand`, `Mutate`, `Handle`, the lifecycle, picker, presentation, barrier, timings, targets and request IDs.

## API and button names

| Public method | Command sent | Local precondition (before any request) | Button name |
|---|---|---|---|
| `Task<bool> TakeCardAsync(string id)` | `takeCard {id}` | `id` is in the published `pending.cards` | `RewardCard:<id>` |
| `Task<bool> SkipCardAsync()` | `skipCard {}` | `pending.cards` is non-empty | `SkipCard` |
| `Task<bool> TakeRelicAsync(string id)` | `takeRelic {id}` | `id` is in the published `pending.relic` list | `Relic:<id>` |
| `Task<bool> TakePartAsync()` | `takePart {}` | `pending.part` is published | `TakePart` |
| `Task<bool> EvolveAsync(string key, string branchId)` | `evolve {key, branchId}` | the pair is in the published `pending.evolutions[{key, options}]` | `Evolve:<key>:<branchId>` |
| `Task<bool> ProceedAsync()` | `proceed {}` | none beyond the gate (Core decides) | `Proceed` |

The read-only `bool ShowingReward` is true while the session's current snapshot shows the public pending reward.

**Every reward action goes through `RewardAction`:**

1. **`CanCommand` first.** This covers busy, the session's `CanMutate` (pending/unknown/stale/outbox/disconnected), no barrier, no pick
   sheet and no prepared play. A refusal shows a Korean notice and sends nothing.
2. **The published choice is checked** against the **session's current** snapshot, which must be in the REWARD phase.
3. **Exactly one existing command** goes through `Mutate`. That means the same owner preflight, the outbox staged immediately before the
   POST, exact retry on uncertainty, and `Handle`'s re-read.

There is no presentation for reward commands, no auto take and no auto proceed. Repeated clicks while busy cannot create another request,
because `CanCommand` is false and the buttons are non-interactable. A claimed choice disappears only because the committed and re-read
state no longer publishes it; there is no optimistic local deletion.

## Screen

- **Header:** "전투 보상 · 보상 동전 N · 보유 동전 M (서버 기록)". Both values are the published `pending.coins` and `state.coins`; the
  screen makes no claim about when coins are charged or awarded. The explicit "다음으로 진행" (`Proceed`) button sits in the header.
- **Evolution rows:** the source card's name comes from the public `state.deck` key→id lookup; each published branch is a button named
  after its card.
- **Reward cards:** name plus content text; plus "카드 받지 않기". The local client's "(동전 +15)" claim is deliberately not repeated,
  because the number is Core's.
- **Relics:** name plus text.
- **Part:** name plus text, with "가져가기".
- **Empty or already claimed:** "남은 카드/유물/부품 보상이 없어요." Nothing is invented.
- **Barrier:** a battle-ending presentation barrier still draws above REWARD (Render order), and `CanCommand` is false until "계속".

## Remaining phases (not done here)

- Rest, forge, shop, event and archive.
- The v2 memory save/commit screens.
- Ultimates; card detail, deck and log sheets; pace controls; map connector lines.
- Production authentication and endpoint selection; Android/device acceptance.

## Layout correction (P1-SERVER-REWARD-LAYOUT-20261017; author runtime NOT_RUN)

The earlier "about three evolution rows" limit was a real gap. growthCap can be 4 (content/characters.mjs, applied in Battle.cs), and
`Run.Rewards.PendingEvolutions` offers every qualifying deck entry. Extra evolution rows could therefore push the card, relic and part
controls below 900.

- **Fixed header:** the header ("RewardCoins", with the coins label and `Proceed`) stays fixed at y 100.
- **Scrolled body:** every reward row now sits inside a vertical scroll "Reward" (viewport x 32, y 166, 1536×670, ending at y 836 above
  the notice line), built with the same `RectMask2D` + `ScrollRect` (Clamped, sensitivity 25) code copied from
  `MemoryForgeExpeditionClient.Scroll`.
- **Content height:** exactly `published evolution rows × 80 + 204 (cards) + 132 (relics) + 72 (part)`. There is no clamp, row cap or
  synthetic data. When it exceeds 670 the content scrolls vertically, so every published control stays reachable. Horizontal scrolling
  stays off because the width is the viewport's.
- **Unchanged:** row contents, button names, Korean labels, font, palette, `CanCommand`/`RewardAction`/`Mutate` gates, and every
  economy/Core/target value.

## Risks for Root's run

- **Not exercised by the author at runtime:** scroll geometry, pointer take inside the scroll, and the 4+-row, relic, part and evolution
  cases. Root's current earned reward has 3 cards, coins 30, relic/part null and `evolutions: []`, so the evolution, relic and part rows
  are not yet runtime-verified.
- **Evolution options are not scrolled horizontally:** they are laid out at x 600 + k×300 (width 284). Three options fit in 1536; a
  fourth branch option per row would run past the right edge. The author did not check the maximum branch count per card in the content.
- **Proceed with unclaimed rewards** is sent if the player presses it; whether that is allowed is Core's verdict. A 422 refusal clears
  the request and is shown as a notice.
