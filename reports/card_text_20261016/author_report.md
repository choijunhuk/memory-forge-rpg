# Card effect text — author report

- Task: P1-CARD-TEXT-AND-FLOW-RECHECK (part 2) · author: Claude Opus 5.5 (High)
- Changed: `core/MemoryForge.Core/BattleText.cs` only (new). No rules, data, card definitions, upgrade logic, UI, tests or fixtures were touched.
- Build/equality/purity: **NOT_RUN**. This CLI had no execution, so nothing here is a PASS claim. Root owns the JS-expected fixtures (173 cards plus effective/plus variants), the C# equality and purity checks, and the UI and Unity integration.

## API

```csharp
public static class BattleText
{
    public static string Card(BattleCardDef card, BattleData data); // = cardText(def, ix)
    public static string Op(EffectDef effect, BattleData data);     // = opText(op, ix)
}
```

The brief named the effect type `BattleEffect`, but no type by that name exists. The actual type is `EffectDef` (`BattleDefinitions.cs:62`).

## Source mapping (`engine/rules-core.js`)

| Source | C# |
|---|---|
| `COUNTER_NAME` L100, `ST_NAME` L103, `TGT_NAME` L104 | `CounterName`, `StatusName`, `TargetName` (ordinal dictionaries, same entries) |
| `COND_TEXT` L105 | `Condition(EffectConditionDef?)` |
| `POWER_TEXT` L111 | `Power(EffectDef)`: `blk`→`Block`, `keep`→`Keep`, `heal`→`Heal`, `bps`→`Bps` |
| `BUFF_TEXT` L120 | `Buff(EffectDef)`: `s`→`Status`, `n`→`N` |
| `scaleText` L125, `vText` L126 | `ScaleText`, `ValueText(v, rv, ra)` |
| `opText` L134–188 | `Op`: all 39 op cases, nested `if` then/else, and the `default` fallback that returns the op name |
| `cardText` L189–201 | `Card`: `filter(Boolean)`, keyword order, `·`/`. `/final `.`, `uses`, `costIf`, and `flavor` only when there is no effect text |

## JS semantics reproduced deliberately

- **Truthiness:** `pierce`, `shock`, `overflowBlock`, `next`, `after` and `uses` count only when non-zero. `brk` needs to be non-zero and not 1. `turns` needs to be greater than 1. `draw` uses its scale branch when `n` is absent or 0. `op.v === 0 && op.scale` is checked exactly, not as falsy. `hits ?? 1` is applied.
- **Fallbacks:** Where the source has no `|| fallback`, an unknown or absent key renders `"undefined"`. That covers `TGT_NAME`, `ST_NAME` in `st`, and `COUNTER_NAME` in the dmg/blk zero branch, in hitsScale, and in draw/breakBonus. The `||` fallbacks are kept as written: `scaleText`→`per`, `cleanseOne`→`s`, `breakBonusAll`→`n`, BUFF/POWER→`s`/`id`, COND→`c`.
- **Absent values:** An absent number renders `"undefined"`, and an absent `bps/100` renders `"NaN"`.
- **Name lookups:** For `create` and `tune`, the card or part name is used, falling back to the id when the name is missing or empty. The lookup uses `BattleData`'s index and falls back to a last-wins list scan if the data was not indexed.
- **Numbers:** Integers are formatted with InvariantCulture. `x/100` uses exact integer formatting, which equals JS `Number#toString` for every `int` without any runtime double formatting (so it is Mono-safe).
- **Purity:** Neither method mutates anything, uses RNG or reads actor stats. It reads only the definition it is given, so effective definitions from `Battle.EffectiveCard` and upgraded or resonance copies work the same way.

## Known limitations / divergences

1. **Errors instead of JS TypeErrors.** Where JS throws a TypeError (an `if` without `cond` or `then`, or a multiplier or costIf without a condition), C# throws `ArgumentException`. Null `card`, `effect` or `data` throws `ArgumentNullException`. The loader already rejects these shapes.
2. **Missing op name.** For an absent op name (only possible with a hand-built `EffectDef`), JS returns `undefined`, while `Op` returns `""`. Both of the source's own callers render that as `""`.
3. **Prototype-chain keys.** JS object lookups would also match prototype keys such as `"constructor"` or `"toString"`. The ordinal dictionaries do not. The loader's allow-lists for op/scale/cond/tgt/pick/power ids keep such keys out of real data. `st`/`buff`/`cleanseOne` `s` is not allow-listed by the loader, so unknown statuses rely on the data.
4. **No UI wiring and no tests.** Nothing here calls `BattleText` yet. Root still has to generate the JS expected text and add the C# equality and purity checks.

## Suggested checks for Root (not authored here)

- All 173 cards through `MF.cardText(def, ix)` vs `BattleText.Card`.
- `upgradeOps`-plus and resonance `applyCardMods` effective definitions via `Battle.EffectiveCard`.
- Every `Op` on all nested `then`/`else` entries.
- Hand-built edge definitions: absent/0 `n` on `draw`, `brk` 1, `hits` 1, `rv`==`v`/`ra`!=`rv`, negative `costIf.delta`, an empty part/card name, an unknown `s`/`id`.
- Purity: `ToJsonGraph()` of the definition and data is identical before and after the call.
