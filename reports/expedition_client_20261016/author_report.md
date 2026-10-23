# P1-UNITY-EXPEDITION-CLIENT — author report (Claude Opus 5.5, requested effort xhigh)

- Role: implementation worker called by Codex Root. No Bash/MCP/agents/credentials were used. Nothing was executed.
- session_id: this worker cannot see it. Root records it from the CLI session metadata.
- Overall status: **code written; every runtime check is NOT_RUN.** Nothing here claims Unity Play, scene, device or end-to-end acceptance.

## Changed files (only the four owned files)

| File | Change |
|---|---|
| `core/MemoryForge.Core/Run.BattleQueries.cs` | New: 5 pure query wrappers over the Run's private live `Battle`. |
| `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs` | New `MonoBehaviour`: the separate expedition practice view. |
| `client/MemoryForgeUnity/Assets/Tests/PlayMode/ExpeditionClientPlayModeTests.cs` | New: 6 Play Mode tests. |
| `reports/expedition_client_20261016/author_report.md` | This report. |

No other Core file, existing client script, loader/exporter, test, scene, prefab, data, numeric/formatter code, price or image was touched. Unity still has to generate the `.meta` files on import.

## Core bridge — `Run.BattleQueries.cs` (Root review requested)

```csharp
List<string>  BattleAllowedModes(string uid)                      // Battle.AllowedModes (fresh list)
int           BattleCostOf(string uid, string mode = "now")       // Battle.CostOf
string?       BattleNeedsTarget(string uid)                       // Battle.NeedsTarget
BattleCardDef BattleEffectiveCard(string uid)                     // Battle.EffectiveCard (detached copy)
string?       BattleCheck(string uid, string mode = "now", string? target = null, IReadOnlyList<string>? picks = null)
```

- Each wrapper is a one-line forward to the existing public `Battle` query on `this.battle`. It duplicates no rule and adds no state injection or restore. No mutable `Battle` reference escapes.
- I checked by reading the code that `Check`/`Precheck`/`ValidateOps` (`OverloadCost(peek:true)`)/`CostOf`/`EffectiveDefinition` change nothing. The picks list is copied before it is passed to `Check`.
- Outside a battle, the four value queries throw `RuleException("NO_BATTLE")`, the same as `BattleCommand`. `BattleCheck` keeps `Check`'s error-code contract and returns `"NO_BATTLE"`.
- Card actions still commit only through the existing `Run.BattleCommand`.

## Component — `MemoryForge.Client.MemoryForgeExpeditionClient`

**Setup (Root):** create a new scene, add an empty GameObject, add this component and a camera. The component builds one owned Canvas, `MemoryForgeExpeditionView`, with a `CanvasScaler` (1600×900, match 0.5) and a `GraphicRaycaster`. If the scene has no EventSystem, it creates one inside its own Canvas. It needs the existing resources: `MemoryForge/run-content`, `MemoryForge/battle-content`, `MemoryForge/error-messages` and `Fonts/IBMPlexSansKR-Regular`. Inspector fields are `Seed` (long, default 1234) and `Length` (`"short"`/`"full"`). The lifecycle mirrors `MemoryForgeBattleClient` (`[ExecuteAlways]`, `DontSaveInEditor` in edit mode, safe-area `FitView`, only its own view is removed). The existing battle practice scene and client are unchanged.

**Public read surface:** `Snapshot` (a detached `Run.State`), `LastError`, `Notice`, `SelectedCard/SelectedMode/Target`, `PickerOpen`, `ShowingResult`, `SelectedChangeIds`, `LastRequestId`, `SessionMemoryVersion`, `SessionMemory` (a copy), `MemoryChanges()`, `PreviewSelection()`, and the static `PracticeSetup(...)`, `ChoiceRequests(...)`, `ReferenceParty` and `OwnedCanvas`.

**Commands:** each button calls exactly one of these. The Core command named after the arrow is the only thing that changes state.

| Phase | Methods → Core |
|---|---|
| Departure | `StartPractice()` (new empty session store, then the first departure), `Depart(length?)` → `Run.Create(PracticeSetup(seed, length, runId, storeMemory, storeVersion))`. While an expedition is running, `Depart` is refused with `RUN_IN_PROGRESS`. |
| Map | `EnterNode(id)` → `Run.Enter`. Every node is drawn, but only `Run.Available()` nodes are enabled. Paths are drawn, and the current position and zone/floor/HP/coins/relics/parts/phase are shown. |
| Battle | `SelectCard/SelectMode/SelectEnemy` (local only), `ConfirmCard()`, `PlayCard`, `EndTurn()`, `UseUltimate(actor)`, `PickCard/AutoPick/CancelPick` → `Run.BattleCommand(play/end/ult)`. Mode captions, cost and refusal reasons come from the queries above. The layout is the battle practice's, with its public `TimelineCells`/`IntentText`/`StatusText`/`PartyStatusText` (current and next intent). |
| Reward | `TakeCard`, `SkipCard`, `TakeRelic`, `TakePart`, `Evolve`, `Proceed` |
| Rest | `RestHeal`, `RestUpgrade(key)` via a deck picker that shows the restrictions (weapon / already upgraded), uses remaining, `Proceed` |
| Forge | `ForgeOffer(key)` → the 3 offered parts → `ForgeApply(part)`; `ForgeRemove(key)`; uses; event-variation forge |
| Shop | `Buy(kind,id)` with the exact listings, prices and sold state; `BuyRemoval(key)` (one per shop, its actual `removeCost`) |
| Event | The original name, text and choices (and coin need). A copy/remove choice opens the deck picker and sends explicit keys, so Core's fallback never silently deletes a card. Then `ChooseEvent(index, keys)`. |
| Archive | `Archive("continue" / "return")` |
| Any | `ApplyFoundPart(part,key)` from the deck sheet, deck/log sheets, `Abandon()` (with a confirm sheet, not on the save screen) |
| Terminal | See the v2 memory section below. |

**v2 memory (§8.9–8.14):**
- Labels, kinds, capacities and the before/after decks come from the actual `Run.MemoryChanges()`.
- Each character's column shows the deck before departure, the change toggles, a "결과 용량 x/y" hint per change from the actual `PreviewMemory`, a preview of the resulting deck (marked 새로/바뀜) and the cards that would be dropped.
- `ToggleChange(id)` only accepts an actual change ID (`INVALID_SELECTION` otherwise) and is local only.
- Commit is disabled while the preview has an error, and the Korean reason is shown (`DEFEAT_KEEP_ONE` / `OVER_CAPACITY` with character used/capacity).
- `CommitMemory()` sends `{requestId: practice-save-<runId>-<n>, expectedMemoryVersion: Run.State.MemoryVersion, runId: Run.State.RunId, selectedChangeIds (change-list order)}` to `Run.CommitMemory(request, copy of the session store)`. The returned `state` becomes the session store. A refusal changes nothing and closes that request.
- Other actions: `CloseWithoutSave()`, and common-card `Assign` (an existing Core command).
- The result screen shows the outcome, statistics, saved version, per-character used capacity and the saved decks. `Depart()` then starts the next Run from that saved memory/version, short or full.

**Session-only rule:** there are no PlayerPrefs, file, account or server writes and no asset migration. The status bar, save header and result screen all say that the practice exists only in this session and disappears when the app closes. No currency, price, probability, cost or permanent-asset policy was changed. The reference party (세라·하린·니아, res 0, default weapons) and normal difficulty are fixed for this slice.

## Tests — `ExpeditionClientPlayModeTests` (NOT_RUN)

Each test uses `ClientRunContent.Load()` full content. A mirror `Run` built from the public `PracticeSetup` receives the same public Core commands. The full fingerprint (canonical hash plus the Run log and battle log/events) must match after every step.

1. `FreshPracticeOpensTheActualReachableMapInOneOwnedCanvas` — Create equivalence, v2/version 0/short zone [1], every node drawn and only `Available()` enabled, readable title/HP/session-only notice, one owned EventSystem, disable/enable keeps the same expedition.
2. `PointerOnAReachableNodeEntersTheActualBattleAndAnUnreachableNodeChangesNothing` — `NODE_UNREACHABLE` with no change. A real `RaycastAll` + `pointerClick` on a reachable node equals `Run.Enter`. Battle labels and intents are readable, and hand buttons are in hand order.
3. `BattleSelectionSpendsNothingAndOnlyTheConfirmedPlayReachesTheRun` — selection and render change nothing. The log overlay blocks a click through to Confirm. `ULT_NOT_READY`/`NOT_IN_HAND`/`BAD_REWARD` change nothing. Confirm equals one actual `BattleCommand` (V+1, AP − `CostOf`). Mutating caller snapshots does not reach the Run.
4. `RunBattleQueriesAreDetachedPureReadsOfTheLiveBattle` — `NO_BATTLE` outside a battle. All queries over the hand leave the full state unchanged. `EffectiveCard` is detached and picks are not retained. A play that `Check` accepts costs exactly `CostOf`.
5. `AbandonKeepsTheSessionMemoryAndTheNextDepartureStartsFromIt` — `RUN_IN_PROGRESS`. Abandon writes nothing, and the next Run equals `Create` of the unchanged memory.
6. `ActualCommandsReachTheTerminalMemoryChoiceCommitAndDepartFromTheSavedVersion` — a bounded command driver (at most 4000 public commands, mirror lockstep) runs through map/battle (direct play, the pick overlay + auto, ult, end)/reward/rest/forge/shop/event/archive to VICTORY or DEFEAT. It then checks the change list and preview equal Core's, `INVALID_SELECTION`, and that selection changes nothing. If the preview has an error, commit is disabled, the reason is shown, `CommitMemory` is refused with the same code and nothing changes; then a valid fill is chosen. The commit equals the mirror `CommitMemory` (Run state, session memory, `used`), the readable result reaches version 1, the next departure equals `Create(saved memory, 1)` with `MemoryBefore` = the saved decks, and a second departure is refused.

The driver is a fixed simple order (first legal immediate card, take the first reward, and so on). It is not a strategy, a balance test or a whole-game claim. These tests do not prove manual pointer flow, Console cleanliness, device behavior or scene wiring.

## Verification status

| Item | Status |
|---|---|
| `dotnet build`/tests of Core with the new file (`TreatWarningsAsErrors`) | NOT_RUN (no execution tools). C# 8 only, and I checked the code by reading it. |
| Unity compile/import of the component and tests | NOT_RUN |
| The 6 Play Mode tests above | NOT_RUN |
| Real pointer flow in the new scene, Console, screenshots | NOT_RUN (Root) |
| Android device / safe area / 130% text | NOT_RUN |
| `npm run docs` / `test:docs` | NOT_RUN (no docs changed) |

## Remaining gaps (explicit; none of these is removed from the roadmap)

1. **PARTIAL — card effect text:** there is no C# port of `MF.cardText`. Reward, shop and deck entries show name/owner/rarity/cost/reservation only, which affects player decisions.
2. **PARTIAL — battle damage/block forecast:** the battle practice's replay preview is not in this slice. `Run` exposes no battle setup or command history, so this needs a reviewed Core candidate query. The existing battle practice and its forecast are untouched.
3. **PARTIAL — departure setup:** the party is the fixed reference party with normal difficulty. Length can be chosen on the result screen; the first departure uses the inspector `Length`. Party, weapons, difficulty and the account lobby remain separate work.
4. **PARTIAL — battle detail sheets:** there are no draw/discard/exhaust pile viewers, reserved-card info, enemy pattern/phase info or detailed combat log text. The ultimate uses the currently selected enemy (Core returns `BAD_TARGET` and the toast asks the player to choose one). There is no separate ultimate target mode.
5. **PARTIAL — save helpers:** the original "용량 안에서 순서대로 고르기"/"선택 해제" helpers and a separate "기존 덱 유지" button are missing. A commit with nothing selected already keeps the deck.
6. **NOT_IMPLEMENTED (server stage) — §14 communication failure/retry/pending-request lock, checkpoint, quarantine/migration screens:** these do not apply to an in-process, session-local store and stay with server/save work.
7. **PARTIAL — memory error texts:** Korean texts for v2 codes (`VERSION_CONFLICT`, `INVALID_SELECTION`, …) and `RUN_IN_PROGRESS` are fallbacks inside the component, taken from the prototype `ERR`. `error-messages.json` is outside my ownership, and the resource owner should add them.
8. **NOT_RUN — layout stress:** I have not checked the 10-card hand, 5 reservations, 5 enemies, Yuri+Ravien together or 130% text in this view. The battle area reuses the battle practice coordinates.
9. **Not final art:** this is uGUI boxes and text only, with no generated or modified images. Map visit history (the prototype's `s.visited`) is not tracked; only the current position is.

## Notes for Root review

- Please check `Run.BattleQueries.cs` against `Battle.cs`/`Battle.PlayerActions.cs`: it should be pure forwarding with no rule copy.
- `ChoiceRequests` is a display projection of `prototype/src/ui.js` `choiceRequests` (top-level ops only, like the source). Core still validates picks, and the "auto" button says that Core picks the rest.
- The rest-heal percent label repeats the original HTML text (`30 (+10 old_compass)%`), and shop/event buttons are disabled at the listed price or coin need, as in the HTML. Core still validates every price and effect.
- `PracticeSetup` is public so that tests and Root can rebuild the exact Create input. Run IDs are `practice-<n>-<seed>`, and the n-th departure uses seed `Seed + n − 1`.
