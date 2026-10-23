# P1-R1B-NODE-SERVICES — author report (Claude Code worker)

- Worker: official Claude Code, Opus 5.5, included subscription only. I can't see the effort level or session id from inside the session; Root's CLI record has both.
- **Execution: NOT_RUN.** I had Read/Glob/Grep and two Edit grants only: no Bash, MCP or build. I didn't compile or run CoreChecks, JS, the oracle or Unity. I don't claim a successful compile or any PASS.
- Edited only `core/MemoryForge.Core/Run.Services.cs` (the placeholder is replaced by the full implementation) and this report.
- **No helper gap:** every helper I needed already exists. `BigIntegerRound` and `FieldList` don't exist under those names. I used the existing `Round(BigInteger,int)`, `FloorDiv`, `Number(BigInteger)` and `RunValues.Fields`.

## 1. Public API → source (engine/rules-core.js)

| C# | Source | Notes |
|---|---|---|
| `void Assign(string key, string charId)` | assign 1802-1808 | no phase check |
| `void Rest(string choice, string? key = null)` | rest 1810-1823 | |
| `Dictionary<string,object?> ForgeOffer(string key)` | forgeOffer 1825-1831 | returns a detached copy (`RunValues.Fields`) |
| `void ForgeApply(string partId)` | forgeApply 1832-1839 | no phase/uses check (Q4) |
| `void ForgeRemove(string key)` | forgeRemove 1840-1845 | |
| `void Buy(string kind, string? id = null, string? key = null)` | buy 1865-1880 | |
| `void EventChoose(int index, object? keys = null)` | eventChoose 1882-1916 | `keys`: string or list |

Every command runs inside the existing `Guard`/`Guarded`, so an overflow becomes `RUN_NUMBER_OUT_OF_RANGE`. No command writes a log line, matching the source. No memoryChanges/preview/commit/restore was added, and full C# Run completion stays 0.

## 2. Order and semantics kept

### Assign
1. `BAD_ASSIGN` if the deck card is missing, its owner isn't `common`, or `charId` isn't a party member. Party membership compares the raw `charId` field strictly.
2. v2 only: if any `memoryBefore` row has this key, `REASSIGNMENT_REQUIRES_SEPARATE_FLOW`. This applies even when the card is reassigned to the same character.
3. Set `assigned`.
4. If a save is pending, run `CaptureMemory()`.

### Rest
1. `WRONG_PHASE` if the phase isn't REST, there is no pending, or `uses <= 0`.
2. `heal`:
   - `memory_anvil` → `CANNOT_HEAL`.
   - Otherwise `hp = min(maxHp, hp + floor(maxHp*(30 + 10 with old_compass)/100))`, using exact BigInteger floor.
3. `upgrade`: `BAD_UPGRADE` if the card is missing, already plus, or a weapon. Otherwise set `plus = true`.
4. Any other choice → `BAD_CHOICE`.
5. `uses -= 1`.

### Forge
- **ForgeOffer:** `WRONG_PHASE`, then `BAD_CARD` (missing or weapon), then `offers = {key, parts: PartChoices(false,3)}`. This uses the same RNG/splice order and data order, and does not spend a use.
- **ForgeApply:**
  1. `BAD_PART` unless `pending.offers.parts` contains the part.
  2. Merge `mods` like ApplyFoundPart does: copy the old object, set `c` for circuit or `e` otherwise, keeping key positions.
  3. `offers = null`, then `uses -= 1`. Uses can go negative, as in source: e.g. offer, then forge-remove with the last use, then apply.
  - If the offered card was removed in the meantime, it throws the source TypeError (`Cannot read properties of undefined (reading 'mods')`) before any change.
- **ForgeRemove:** `WRONG_PHASE`, then `BAD_CARD`, then splice and `uses -= 1`. Offers are left untouched.

### Buy
1. `WRONG_PHASE` if the phase isn't SHOP.
2. `remove`:
   1. `removeCost === null` → `NOT_FOR_SALE`, checked before coins and card.
   2. `COINS_LOW`.
   3. `BAD_CARD`.
   4. Pay, splice, `runFlags.removals = (removals || 0) + 1`, set `removeCost = null`. The 50 + 25×removals price from MakeShop is unchanged.
3. Any other `kind`:
   1. Read `p[kind]` and find an unsold listing with that id; none → `NOT_FOR_SALE`.
   2. `COINS_LOW`.
   3. Pay and set `sold = true`.
   4. Grant:
      - cards: `{id,key,owner,assigned(common→party[0]),gained}`
      - parts: append to `partsFound`
      - relics: append to `relics`
   - A truthy non-array `p[kind]` is the source TypeError `list.find is not a function`. That covers `type`, a non-null `removeCost`, and the 12 Object.prototype names.

### EventChoose
1. `WRONG_PHASE`.
2. Event lookup: the last event in data order with that id wins (`Object.fromEntries`).
3. `BAD_CHOICE` for a missing choice (index < 0 or ≥ count) or `p.done`.
4. `need.coins` → `COINS_LOW`.
5. `WOULD_KILL`. Steps 3–5 all run before any effect.
6. Effects, in source order:
   1. coins (`max 0`)
   2. hp (`max 1`)
   3. heal (`min maxHp`)
   4. maxHp (then clamp hp)
   5. maxHpPct (`max(1, floor(maxHp*(100+pct)/100))`, then clamp hp)
   6. nextApBonus (`(x||0)+n`)
   7. curse: `{id,key,owner:'curse',gained}`
   8. relic: `common` → `RelicChoices("common")`, anything else → `"elite"`; push the first pick if any
   9. card: party-owned, rarity matches, `pool !== false`; one `Rng.pick`; `{id,key,owner,gained}`, no `assigned`
   10. part: `PartChoices(false, fx.part, true)`, keep the first
   11. partRare: always exactly one rare draw
   12. variation: open a FORGE pending `{type:'forge',uses,offers:null,fromEvent:true}` and **return before done**
   13. growth: one random growth card below 3 gets +1
   14. cardCopy: `{...clone(d), key: NextCardKey, gained: true}` (new key, field order kept)
   15. cardRemove ×n: pick → first curse → first non-weapon
7. `done = true`, `choice = index`.

**hpk** is `Math.round(v*(hpRef || 81n)/(81n))`, implemented as exact half-up rational rounding with the existing `Round`. It equals JS `Math.round`, including negative halves (-2.5 → -2), whenever `|v*hpRef| < 2^53`, which holds for all authored data. The `party.length` scale and the `hpRef` fallback are kept.

**Keys:** an `IList` is used as-is; a truthy value becomes `[keys]`; anything else becomes `[]`. Comparison is strict: only a string element can match a deck key.

## 3. Limitations (explicit, not policy changes)

- **Non-transactional, as in source.** These keep the effects already applied when they throw:
  - an empty event card pool → `RNG_BOUND`
  - a cardCopy on an all-weapon deck → `MemoryBoundaryException("SyntaxError", "\"undefined\" is not valid JSON")`, thrown before a key is drawn, like `clone(undefined)`
  - a key overflow after a purchase has been paid
- **Outside the typed domain:** a non-integer `pending.uses`, shop `price` or `removeCost` → `RUN_NUMBER_OUT_OF_RANGE`. JS would compare against undefined/NaN instead. None of these can be produced by Run commands.
- **Empty party (event HP scale):** JS would divide by zero. Here it gives `RUN_NUMBER_OUT_OF_RANGE`, but it is unreachable because floor 0 is always a battle.
- **Not modelled:** malformed shop listings or a malformed `offers.parts` in corrupt state. Null items are skipped, and a non-list `parts` gives `BAD_PART`. Neither occurs in Run-produced state.
- **Unreachable source TypeErrors** use the R0 boundary convention: a null pending in SHOP/EVENT, or an unknown event id. No native-message parity is claimed beyond the strings quoted above.
- **Stale comments in read-only files:** the headers of `Run.cs` and `Run.Progression.cs` still say these services are "later R1 units". Root may update them when integrating. I didn't touch them.

## 4. Root verification (all NOT_RUN here)

- **Build:** build with warnings as errors (C# 8, netstandard2.1, Nullable), then rerun all existing suites.
- **Replay:** run the five-case oracle and the 1000 legacy prefixes against Root's stated denominators: 2679 attempts / 2474 battle attempts / 1162 battle commits / 1064 queries before the sole commitMemory, and 272504 attempts before 1000 commitMemory. Report the actual counts separately. Full Run completion stays 0.
- **Targeted pins:**
  - Rest:
    - `old_compass` heal
    - `memory_anvil` → `CANNOT_HEAL` with no use spent
    - `forge_ember` uses = 2
    - `upgrade` on plus/weapon → `BAD_UPGRADE`
  - Forge:
    - ForgeOffer twice: the second replaces the offers and consumes RNG
    - offer → remove → apply gives the TypeError with no change
    - offer → remove (last use) → apply on another card gives uses -1
  - Shop:
    - a second removal → `NOT_FOR_SALE` even with 0 coins
    - removals 50 → 75 across shops
    - a sold card can't be bought again
    - `kind:"type"` → TypeError
  - Event:
    - WOULD_KILL and COINS_LOW leave state unchanged
    - hpk rounding at negative halves
    - variation (no `done`, FORGE `fromEvent`)
    - cardRemove fallback curse → non-weapon
    - cardCopy gets a new key with `gained`
    - growth cap 3
  - Assign:
    - v2 same-character reassignment is refused
    - assign while a save is pending recaptures `memoryAfter`
- **Unity:** PlayMode/EditMode with these sources is NOT_RUN.
