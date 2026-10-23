# P1-R1C-RUN-MEMORY — author report (Claude Code worker)

- Worker: official Claude Code, Opus 5.5, included subscription only. Requested effort: max. I can't see the applied effort or the session id from inside the session; Root's CLI record has both.
- **Execution: NOT_RUN.** I had Read/Glob/Grep/Edit only. I ran no Bash, MCP, build, CoreChecks, JS, oracle or Unity. I don't claim a successful compile or any PASS.
- Edited only these files:
  - `core/MemoryForge.Core/Run.Memory.cs`: the placeholder is replaced by the full implementation.
  - `core/MemoryForge.Core/RunState.cs`: `SavedMemory` field, deep copy and conditional `savedMemory` JSON only.
  - This report.
- **No helper gap.** I reused existing helpers: `Guarded`, `PendingKind`, `BaseKit`, `TerminalMemory`, `CardDefinition`, `PartDefinition`, `JsKey`, `ObjectPrototypeMembers`, `MemoryJson.*`, `RunValues.*`, `CanonicalState.AppendString`, and M0 `MemoryV2.ListChanges/Cost/Apply/Inspect/CapacityOf`.

## 0. One-time review of Codex's Run.Services.cs patch — verdict: APPROVE

I checked all 11 `.GetValueOrDefault()` reads, at lines 156, 157, 158, 159, 160, 161, 165, 167, 189, 193 and 219. Each one is inside an unchanged `Present(x)` guard:
- a `&&` short-circuit (156, 157), or
- an `if` body (158-167, 189, 191-194, 217-227).

`Present(v)` is `v.GetValueOrDefault() != 0`, so it implies `HasValue`. Under each guard, `GetValueOrDefault() == Value`, and the old `.Value` could never have thrown there. The patch therefore changes no value, branch, exception path or loop bound; `CardRemove` is re-read per iteration exactly as before. No `.Value` read is left in the file, and nothing else differs. I didn't re-review the rest of the file, as instructed.

## 1. Public API → source (engine/rules-core.js)

| C# | Source | Returns |
|---|---|---|
| `Dictionary<string,object?> MemoryChanges()` | memoryChanges 1931-1975 | legacy `{cid:{capacity,changes}}`; v2 `{cid:{capacity,before,after,usedBefore,changes}}` |
| `Dictionary<string,object?> PreviewMemory(object? selectedIds)` | previewMemory 2049-2055 | `{memory,used,error,capacities}` |
| `Dictionary<string,object?> CommitMemory(object? selection, object? currentMemory = null)` | commitMemory 1977-2017, commitMemoryV2 2056-2082 | legacy: the new memory `{cid:[spec]}`; v2: `{state,response}` |

- All three run inside `Guarded`, so a checked overflow becomes `RUN_NUMBER_OUT_OF_RANGE`.
- Every returned graph is freshly built or deep-copied. `savedMemory` is a separate copy. Nothing returned aliases the deck, the snapshots, the input store or the caller's request.
- Both queries are pure: no RNG and no state write. They use the live `TerminalMemory()` only when `memoryAfter` is absent.
- `RunState.SavedMemory` (`object?`) is null and absent from JSON until a successful commit. `Copy()` deep-copies it, and `ToJsonGraph()` emits `savedMemory` last.

## 2. Semantics kept

### MemoryChanges
**Legacy:**
- The pool is the non-weapon cards where `owner===cid`, or where the card is common and `assigned===cid`.
- Each base card matches the first pool card with `id===b.id || from===b.id`. This is strict `===` through `MemoryJson.Equal`, so an undefined `b.id` matches a card without `from`, exactly as in the source.
- An unmatched base card gives `remove`. A matched card gives, in this order:
  1. `evo` (`from===b.id`)
  2. `plus`
  3. `part` c, then `part` e
- Then each remaining non-curse card gives `add`.
- The `add` cost is:
  - `addCommon` for a common card; otherwise `{B,A,S}[rar of from||id] || addA` (a zero cost also falls back to addA)
  - plus `upgrade`, `evolution`, and both part slots.
- Labels, member order and undefined omission match the source, e.g. `X → Y` and `X에 「P」`.
- A repeated party character overwrites its entry in place.

**v2:**
- `listChanges(memoryBefore, memoryAfter || snapshot)`.
- Per party: `capacity`, `before` copy, `after` copy, and `usedBefore = cost(before, baseline)`.
- Changes are filtered by cid, in M0 order. Each gets the source label, using the card name found by key in after first, then in before (`추가/제거/진화/강화 변경/회로|각인 교체|제거`).

**Capacity:** `MemoryV2.CapacityOf` with the party's own `res ?? 0` and `asc ?? 0`, the same rule as Create. No caller-supplied capacity is used.

### PreviewMemory
- `WRONG_PHASE` unless the policy is v2 and `pending.type==='save'`.
- Then M0 `Apply(before, after, ids, capacities, {defeat:!!pending.defeat, baseline, preview:true})`, with `capacities` appended to the result.

### Legacy CommitMemory
Checks, in source order:
1. The pending-save guard (`WRONG_PHASE`).
2. `all = memoryChanges()`.
3. `BAD_CHOICE` unless the selection is a dictionary.
4. Each entry, in JS own-key order (`MemoryJson.Entries`), is checked in this order:
   1. known cid
   2. an `IList`
   3. SameValueZero-unique
   4. each element an integer, `≥0` and `<changes.length`
5. `DEFEAT_KEEP_ONE` when `defeat` and the total count is above 1.
6. Per party: `OVER_CAPACITY` (`Σ cost > capacity`), then the rebuild.

The rebuild:
- Start from fresh base-kit specs.
- Apply the kept changes in change order:
  - `remove`: the first untouched spec with `id===ch.id`.
  - `add`: `{id, [plus:!!plus, mods, from] if any is truthy, assigned:cid if the card is common}`, with `_touched` and `_added` set.
  - `evo/plus/part`: the first non-added spec with `id===baseId || from===baseId`. This keeps the source quirk where a second same-base evo rewrites the first spec.
    - For `evo`, `baseId` is `ch.from`; otherwise it is `byKey[key].from || .id`.
    - `byKey` is the later deck card per `String(key)`, as in `Object.fromEntries`.
    - `evo` sets `from`, then `id`. `plus` sets true. `part` merges a new `mods` with `[slot]=part`.
- Omitted vs null `mods`/`from` and `plus:false` keepBits are preserved. The marks never reach the output.
- The Run is finished only after every party is validated and rebuilt: `savedMemory`, then `{type:'done'}`, then `DONE`. The legacy commit has no VICTORY/DEFEAT check, as in the source.

### v2 CommitMemory
1. `INVALID_SELECTION` unless:
   - the request is a dictionary,
   - `requestId` is a non-empty string,
   - `expectedMemoryVersion` is an integer `≥0`,
   - `selectedChangeIds` is an array of unique strings.
2. `RUN_MISMATCH`.
3. `INVALID_SNAPSHOT` unless the store is a dictionary with schema `mf-memory-v2`, an integer `version ≥0`, a list `history` and a non-list object `receipts`.
4. `MemoryV2.Inspect(store.memory)`. A missing memory is passed as null, so it is INVALID_SNAPSHOT like `inspect(undefined)`.
5. Build the body: `JSON.stringify({runId, expectedMemoryVersion, selectedChangeIds: ordinal-sorted copy})`.
6. **Own-receipt replay comes before any phase/version check.**
   - A different body → `REQUEST_CONFLICT`.
   - Otherwise:
     - copy `receipt.response`
     - finish the Run: `savedMemory = copy(response.memory)`, done, DONE
     - return `{state: store copy, response}`.
   - There is no increment and no history entry.
7. New commit:
   1. `WRONG_PHASE` unless the phase is VICTORY/DEFEAT with a save pending.
   2. `VERSION_CONFLICT` for the request vs Run version, then the store vs request version.
   3. Per party, `VERSION_CONFLICT` when `JSON.stringify(store.memory[cid] || baseline[cid]) !== JSON.stringify(memoryBefore[cid])`. This is order-sensitive text, not unordered equality.
   4. `PreviewCore`; a non-null `error` becomes that RuleException.
   5. On the store copy:
      - merge the party memory (existing keys keep their position)
      - `version = memoryVersion+1`
      - `Inspect` the merged memory
      - append history `{runId, requestId, version, before, memory, selectedChangeIds (request order)}`
      - build response `{memory, used, version}`
      - add the receipt `{body, response copy}`
   6. Finish, and return `{state, response}`.

The input store and request are never mutated. **The JSON text** (`JsonText`) is a source-JSON.stringify writer:
- JS own-key order (array-index keys first), undefined omitted, JS string escaping (`CanonicalState.AppendString`).
- Safe integers are written exactly. Any other finite number uses shortest round-trip digits in the JS `Number#toString` layout.
- I didn't use `CanonicalState.Serialize`, because it sorts keys, drops `log`/`ev` and rejects doubles.

## 3. Known boundaries (explicit, not policy changes)

- **Object.prototype keys (legacy):** like the source plain-object `all[cid]`, a selection key such as `constructor` or `__proto__` (the existing 12-name set) behaves this way:
  - with `[]` it is accepted and ignored;
  - its first non-negative integer index is the source TypeError;
  - a non-integer or negative index is `BAD_CHOICE`.
- **Corrupt receipts only, never produced by commits:**
  - a `null` receipt is a TypeError;
  - a missing `response` or `response.memory` is the `clone(undefined)` SyntaxError;
  - a `null` response is a TypeError.
  - All of these throw before any change.
  - A receipt whose `response.memory` is literally `null` finishes with `SavedMemory == null`, so `savedMemory` is absent from the C# JSON. JS would store `savedMemory:null`. Representing that would need a presence flag outside the agreed single field.
- **Typed limit:** a new store version above 2^53−1 throws `RUN_NUMBER_OUT_OF_RANGE` before any change. JS would write 2^53, which CanonicalState and C# Create already refuse. Costs use checked `int` like the rest of Run.
- **Number text:** this only matters for non-integer or unsafe metadata numbers inside memory rows. Both sides of each comparison use the same writer, so equality is exact. The body only contains a validated integer. The exact JS digits for such metadata rely on correctly rounded `"E16"` formatting in the runtime.
- **Detachment differs from source aliasing, as instructed:** the JS legacy commit returns the very object it stores and shares deck `mods` objects. C# stores and returns separate deep copies.
- **Replay before the phase check is source behavior:** a matching own receipt finishes even a non-terminal Run, and a live battle is left as it is, as in the source.
- **Selection inputs are JSON graphs:** a dictionary of `IList`, and a request of strings and integers. A typed `Dictionary<string, List<int>>` is not an `IDictionary<string, object?>` and is refused as `BAD_CHOICE`/`INVALID_SELECTION`.
- No disk, server, account, migration or restore writes. Prices, rates, capacities, costs and defeat rules are unchanged. Error details (`OVER_CAPACITY {charId,cost,capacity}`) and native JS messages are not claimed.

## 4. Observations outside my ownership (no change made)

- Stale header comments still describe memory commit as a later unit, in `Run.cs:1-2`, `Run.Progression.cs:3-4,253` and `Run.Services.cs:4`. Root may update them when integrating.
- M0 `MemoryV2.Cost` maps the rarity cost without the source `|| C.addA` zero fallback (`memory_v2.cjs:63`). This makes no difference with the current data, where `addB/addA/addS` are 1/2/3. It would matter only if a rarity cost were configured as 0. The legacy path here keeps the fallback.

## 5. Root verification (all NOT_RUN here)

- **Build:** C# 8, netstandard2.1, Nullable, warnings as errors. Then rerun all existing suites.
- **Actual replay:** the 2679 full-state attempts and the 272504 legacy prefixes; the 1000 legacy and v2 runs to a complete `commitMemory`, then a fresh `Create` reload from `savedMemory` (v2 with the new store version); Unity. Report the actual counts separately.
- **Targeted pins:**
  - Legacy:
    - `engine.test.mjs:192-215`: capacity 6, `OVER_CAPACITY`, plus+add rebuild with 6 rows, defeat `DEFEAT_KEEP_ONE`
    - `review_v071.test.mjs:30`: six invalid selections with no mutation
    - the `{constructor:[]}` and `{constructor:[0]}` edges
    - keepBits `mods:null`/`plus:false`
    - double evo on the same base
  - v2:
    - `memory_v2_integration`/`_edges`: replay with the same id, a changed body (`REQUEST_CONFLICT`), a reordered selection (same body), `__proto__` own receipt
    - `VERSION_CONFLICT` for the Run version, the store version and a reordered before row
    - preview `DEFEAT_KEEP_ONE`/`OVER_CAPACITY`
    - non-party memory, history, receipts and metadata preserved
    - input store unchanged
    - `response.memory` deep-equal to `savedMemory`, with no aliasing
  - Hash: `{run, memory}` after the commit for `seed1102` legacy and v2 (`run_node_contracts.test.mjs:371-445`).
