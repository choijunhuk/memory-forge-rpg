# P1-R1C-UNITY-NUMBER-FIX — author note: exact Number::toString in the memory JSON writer

- Author: official Claude Code, Opus 5.5, included subscription only. Requested effort: max. The applied effort and the session_id are in Root's CLI record; I can't see either from inside the session.
- **Execution: NOT_RUN.** I had Read/Glob/Grep/Edit only, so nothing was compiled or run. Every result below is hand-derived until Root compiles and tests it.
- Edited only `core/MemoryForge.Core/Run.Memory.cs` and this file.
- I couldn't find the oracle/witness copies (`ops/ai/local/run-*`) in this worktree, so I didn't use them.

## Change

- **`JsNumber`** (`Run.Memory.cs:370-383`): the digit search is replaced by one call to the new private `ShortestDigits(x, out digits, out point)` (`:384-420`).
  - The old search used `ToString("E"+p)` and accepted the first precision that round-tripped through `double.TryParse`.
  - Mono's E-format and parse aren't correctly rounded at 16-17 digits. So the first string that "round-trips" can be the wrong neighbour (…998 instead of …997) or the wrong length.
- **Kept as is:**
  - safe-integer fast path, NaN/±Infinity → `null`, ±0 → `0`, the sign
  - the four layout branches: plain `k ≤ n ≤ 21`, fraction `0 < n ≤ 21`, `0.` for `-6 < n ≤ 0`, exponent otherwise
- **Added** `using System.Numerics;`. `BigInteger` is already used in the Battle/Run files; no package was added.
- **Unchanged:** public API, `JsonText`/`AppendJson`/`JsNumber` signatures, accepted inputs, costs, capacities, error codes, member order, string escaping, array/undefined handling.

## Algorithm

`ShortestDigits` uses only `BitConverter.DoubleToInt64Bits`, integer/`BigInteger` arithmetic and `long.ToString`. It makes no runtime double formatting, parsing or math-library call. Even the decimal-exponent estimate is integer arithmetic: `((b+54)·1233 >> 12) + 1`, where 1233/4096 ≈ log10 2.

1. **Bits to x = m·2^e.**
   - Subnormal: m = fraction, e = -1074. Normal: m = fraction | 2^52, e = biased - 1075.
   - Everything is measured in units of 2^(e-2) (the code's `binary`). In those units x = 4m and the upper midpoint is 4m+2.
   - The lower midpoint is 4m-2, or 4m-1 when fraction = 0 and biased > 1. That is a power of two, whose predecessor is half as far away.
   - The least normal (biased 1) stays symmetric: its predecessor, the largest subnormal, is a full 2^-1074 below.
   - At the maximum (m = 2^53-1), 4m+2 is exactly the midpoint to the virtual 2^1024.
2. **Endpoints.** Round-to-nearest-ties-to-even gives a midpoint to the even significand, so both midpoints belong to x only when m is even (`inclusive`).
   - The maximum has odd m, so its 2^1024 midpoint is excluded. It rounds to +∞, which matches ECMAScript's extended-set rule.
3. **Exact ratio.** x/10^q = value·Scale(q)/Unit(q), where:
   - Scale = 10^max(-q,0)·2^max(b,0)
   - Unit = 10^max(q,0)·2^max(-b,0)

   The midpoints use the same ratio, so every test is an exact integer comparison.
4. **Decimal exponent.** Find n with 10^(n-1) ≤ x < 10^n: start from the estimate, then run `while x ≥ 10^n: n++` and `while x < 10^(n-1): n--` exactly.
5. **Search, k = 1..17**, with q = n-k:
   - s = floor(x/10^q), which lies in [10^(k-1), 10^k), with remainder r.
   - The floor candidate s·10^q is inside if it is ≥ the lower midpoint (> when the bounds are exclusive).
   - The ceil candidate (s+1)·10^q exists only when r ≠ 0. It is inside if it is ≤ the upper midpoint (< when exclusive).
   - Stop at the first k where either is inside. If both are, compare 2r with Unit to find the closer one; an exact tie goes to the even s.
   - Output: digits = s and point = len(s) + q, then trailing zeros are trimmed. The point is fixed before trimming, so s+1 = 10^k gives digits "1" and point n+1.

### Why this equals ECMAScript Number::toString

The output is the shortest decimal, then the closest one, with ties to even. This is the spec's Note 2 rule, and it is what V8 and Node print.

- **Shortest.** The decimals that read back as x form one interval around x.
  - If any k-digit decimal (at any exponent) is in that interval, so is the nearest k-digit grid point on the same side of x. Those nearest points are the floor and ceil candidates at the natural n.
  - A k-digit decimal ≥ 10^n means 10^n is inside, and 10^n is the ceil candidate when s+1 = 10^k.
  - A k-digit decimal < 10^(n-1) means 10^(n-1) is inside, and 10^(n-1) ≤ the floor candidate.
  - So checking only s and s+1 at each k finds the minimal k.
- **Closest and ties.** At a fixed k, candidates are spaced 10^q apart. The nearest one inside the interval is s or s+1, at distances r and Unit−r.
  - A real tie needs ulp ≥ 10^q. That happens only at k ≥ 16 in the normal range, e.g. 2^50+0.25.
  - At the minimal k ≥ 2 the chosen s has no trailing zero, since a shorter candidate would otherwise exist. Only k = 1 can give s+1 = 10.
- **The search always ends by k = 17.** The 17-digit spacing 10^(n-17) is at most x·10^-16. The interval is wider than that in every case:
  - symmetric normal: width > x·2^-53 ≈ 1.11e-16·x
  - power-of-two boundary: width = 0.75·x·2^-52
  - subnormal: width > x·2^-52

  Any interval wider than the spacing, even an open one, contains a grid point. So the final `throw` can't be reached.

## Hand-derived expectations (NOT executed; for Root's Node/Unity cross-check)

| bits | expected | path |
|---|---|---|
| `44b52d02c7e14af5` | `9.999999999999997e+22` | k=16; half-width 8388608, exclusive (m odd); floor is 4834176 away, ceil 5165824 → floor |
| `44b52d02c7e14af6` (1e23) | `1e+23` | k=1; the upper midpoint is exactly 10^23 and included (m even) → s+1 = 10 → "1", n = 24 |
| `0000000000000001` | `5e-324` | k=1; floor 4 (r/Unit ≈ 0.94) and ceil 5 both inside → ceil |
| `000fffffffffffff` | `2.225073858507201e-308` | k=16; only ceil inside |
| `0010000000000000` | `2.2250738585072014e-308` | symmetric interval (biased 1) |
| `7fefffffffffffff` | `1.7976931348623157e+308` | k=17 |
| `4310000000000001` (2^50+0.25) | `1125899906842624.2` | k=17 exact tie → even s |
| `3d30000000000000` (2^-44) | `5.684341886080802e-14` | k=16, asymmetric interval: floor (…801) is 4.87e-30 below, outside the 3.16e-30 lower half-width; a symmetric interval would wrongly pick it |
| `3fb999999999999a` (0.1) | `0.1` | k=1 |
| `4340000000000000` (2^53) | `9007199254740992` | above the safe-integer fast path; asymmetric interval [x-0.5, x+1] |

**Public witness.** The request's expectedMemoryVersion is the `44b5…af5` value. It is integral and ≥ 0, so it passes INVALID_SELECTION as before. The body now carries `9.999999999999997e+22`, the same text as JS JSON.stringify. The caller's own receipt should therefore replay to DONE instead of throwing REQUEST_CONFLICT. Root to confirm.

## Outside scope (not edited)

- **`JsKey`** (`Run.Progression.cs:150`, not an allowed file) still formats non-integer or ≥1e21 doubles with `ToString("R")`.
  - Its digits depend on the runtime, and its layout is .NET's ("1E+21", "1E-07"), not JS String().
  - It is used by `BossId` and by `PropertyKey` (`Run.Memory.cs:318`) for deck keys.
  - It is observable only if that text matters, e.g. it collides with a real enemy id or another card key, or reaches an error detail.
  - Root may route it through `JsNumber` (same partial class) as a separate task.
- **Cost.** Safe integers are untouched. Other numbers take about 19 BigInteger rounds at most, on operands of about 1.2k bits at most. Subnormals need about 16 extra exact settle steps.

## Root verification requested (all NOT_RUN here)

- Compile with TreatWarningsAsErrors.
- .NET Core checks, 1000 full runs, forgery tests.
- Unity Mono: the 2088 oracle cases, the public receipt witness, PlayMode.
