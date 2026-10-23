# P1-R1A-UNITY-TEST-REVIEW — opposite-author review (Claude Code worker)

- Worker: official Claude Code, Opus 5.5, High requested. Subscription only. Effort/session metadata is not visible to me; Root holds the CLI record.
- **Execution: NOT_RUN.** No Bash/MCP/Unity in this session. I did not build, run PlayMode/EditMode/JS, or compute any hash. Root's reported PlayMode 27/27 and EditMode 3/3 results are Root's evidence, not mine.
- Scope: `client/MemoryForgeUnity/Assets/Tests/PlayMode/RunProgressionPlayModeTests.cs` and `reports/run_progression_core_20261016/unity_reference_actual_js.json`, read against the Core sources they call. Everything except this file was read-only.

## Verdict: APPROVE

I found no actionable blocker. The test drives the real public Run API from a fresh `Create` through enter → 13 battle commands → takeCard → takePart → proceed, and checks the full canonical state hash after every step. No expected state is injected or restored.

## What I checked

| Item | Evidence | Result |
|---|---|---|
| Embedded reference = report JSON | All 18 SHA-256 values (initial + 17 steps) extracted from `RunProgressionPlayModeTests.cs:18` and from the JSON. Identical, in the same order. Setup, command args and `final.keys` also match. | OK |
| Hash envelope matches the oracle convention | `Hash` at `:45` is `{run: State.ToJsonGraph(), memory: null}`. This is the legacy snapshot shape required by `TraceHashChecks.cs:227-230`. | OK |
| Hash covers the asserted facts | `RunState.ToJsonGraph` (`RunState.cs:77-92`) includes rng, party (power4/hp fields), hp, coins, deck, pending, battle, phase, log, stats, offered, plus `battleType`/`memoryAfter` once set. | OK |
| Party/stat assumptions | Setup at `:52-54` is sera/harin/nia, res 0, seed 1234, short, legacy, zones [1], difficulty 100/100. Relics are left at the default (empty), matching the reference's `relics: []`. `new StatsConfig()` defaults and the derived power4/hp are pinned by the initial hash `56a6ba86…`, and by Enter's hp 243. Line `:88` confirms Create did not write power4/hp back into the caller's setup. | OK |
| No hidden data mutation | `ClientBattleContent.Load()` deserializes a fresh object from the TextAsset on every call (`ClientBattleContent.cs:50-57`), so there is no shared static cache. Lists are copied into `RunData` (`:27-28`). | OK |
| Detach probe is meaningful | `Run.State` returns `state.Copy()` (`Run.cs:19`). `Copy` clones Rng, Deck and Battle (`RunState.cs:67-71`). The probe at `:79` mutates `Battle.Hp`, `Rng[0]` and `Deck`, and all three are in the hashed graph. A leaked live reference would therefore fail the test; it cannot pass falsely. | OK |
| Battle version / rid | `:63` asserts the live `V` equals the recorded `v` before each command. `Run.BattleCommand` synthesizes `"r"+v+"-"+kind` (`Run.Progression.cs:48`), which equals every recorded `rid` (`r1-play` … `r13-play`). | OK |
| Silent rejection cannot pass | The 18 hashes are pairwise distinct. A rejected command that leaves state unchanged would fail the next hash assertion. | OK |
| Final state | `:84-87`: 17 commands, 13 battle commands, then hp 233, coins 86, battles 1, deck 19, keys k1…k19, phase MAP, Battle null. All match `final` and the last step. | OK |
| Scope honesty | The header (`:1-3`) and the comment at `:29` state that this is not exploration UI, events, save/restore, server or device acceptance. | OK |

## Is this a real §7 / §16.2 cycle?

Yes, within its stated scope. It is a real §7.3 battle node and §7.4 battle reward (card + part) cycle followed by return to the map. It runs on the authored full Battle content and the zone encounter tables, so this is a §16.2 JS→C# parity check on actual runtime commands, not a simulated state. Events (§7.7), relic choice, shop/rest/forge, the archive and run completion are not exercised. The test says so, and this review does not require them for this unit.

## Non-blocking notes (no change required for approval)

1. **Weak public-view probe.** `:80` calls `view.Clear()` on the top-level dictionary. That dictionary is rebuilt on every call and is not part of the hash, so this line proves almost nothing.
   - Optional fix: mutate a nested value, then compare `run.BattlePublicView()` before and after.
2. **Ignored `BattleResponse`.** The return value of `run.BattleCommand(...)` at `:64` is discarded. The distinct-hash chain already catches rejections, so this is optional.
   - Optional fix: assert that the response is OK, which gives a clearer failure message.
3. **`offered` is unasserted.** The reference's `offered` list is never checked directly. It is covered indirectly: `TakeCard("harin_counter")` throws `BAD_REWARD` when the card is not offered, and `pending.cards` is in the hash.
4. **Reference provenance.** No generator script or command for `unity_reference_actual_js.json` is committed, and the JSON carries no source hashes. I cannot verify from source alone that it is the output of an actual JS run; that rests on Root's execution record.
   - Minimal fix, in Root's report: record the exact generating command and the sha256 of `engine/rules-core.js` and the data inputs.
5. **Duplicated reference.** The JSON is embedded as a literal at `:18` and also exists as a report file. They are identical today, but they can drift. If either is regenerated, re-run the 18-hash comparison above.
   - Optional fix: load the reference via a `TextAsset`.

## Ownership

Only this file was edited. The test, the reference, the Core sources and the Unity scene/project were not touched. Root remains the sole Editor writer.
