# Forge return ownership probe — P1-R1B-FORGE-RETURN-PROBE (author)

- Author: Claude Code, Opus 5.5 (`claude-opus-5-5`), requested effort High, included subscription only. This was an edit-only session: no Bash, MCP, agents or recursive CLI calls.
- Edited: `core/MemoryForge.CoreChecks/RunProgressionChecks.cs` (+20 lines), and this report.
- Not touched: Core, data, generators, fixtures, Unity, and the R1a/R1b APIs.
- Execution: **NOT_RUN**. I did not compile or run anything; Root's build and full rerun are the evidence.

## Gap

None of the five full-state cases calls `forgeOffer`. The legacy replay ran all 567 `forgeOffer` attempts but threw away the value `call()` returned. So the return's value and the fact that it is a detached copy (the detachment branch of `Return()`) were never checked in the actual runtime.

## Change (at the existing `LegacyTrace` call point)

1. `string? code = null; object? returned = null;` … `try { returned = call(); }`. The return is now captured. Nothing else in the dispatch changed.
2. The probe runs only after the existing outcome/code/canonical-context-hash comparison against the unchanged JS record has matched, and after the existing `legacyMatched/Executed/Committed/Rejected` bookkeeping. It applies to every **committed** `forgeOffer`:
   - `live = Full(Context(after, null))` is a complete fingerprint of the live Run, including log.
   - The return must be an `IDictionary<string, object?>`; otherwise `LegacyMismatch` is recorded and the case ends.
   - `MemoryV2DataLoader.Difference(Get(after.Pending, "offers"), offers)` must be null. This is a full-value comparison of `{key, parts}` against `pending.offers`, which the JS whole-state hash checked just above has already pinned.
   - Positive control: `Scribble(offers)` must change `Full(offers)`.
   - `Full(Context(run.State, null))` must still equal `live`.
   - If any of these fails, `LegacyMismatch(...)` is recorded and the method returns, ending the case. A failure is never skipped silently. Otherwise `legacyOfferProbes++`.
3. A new literal `LegacyOfferReturns = 567` sits next to the unchanged `LegacyAttempts = 272504`. `Legacy()` adds `Check(legacyOfferProbes == LegacyOfferReturns, …)`.
4. The `RunTracePrefixReplay` line now also prints `forgeOffer return value/ownership probes N/567`.
5. These are unchanged: the 272504 denominator, the per-method literals, `full completions 0` / `full C# runs 0`, all existing strict, probe and full-state checks and counters, and `detachedProbes`. The legacy count is kept separate from `detachedProbes`, so the oracle report line keeps its existing meaning.

## Points for Root to verify

- **567 assumes every legacy `forgeOffer` commits.** That figure is from Root's own count. If some recorded `forgeOffer` attempts are rejected, `legacyOfferProbes` will be lower than 567 and the new check fails, even though no probe failed. In that case the literal should become the committed count from the corpus, not a looser bound.
- **The order is deliberate.** The probe runs after `legacyMatched++`, so command parity is still counted for that attempt. A probe failure still ends the prefix, so `legacyPrefixes` and the probe total also fail.
- **What the live comparison does and doesn't prove.** It uses a fresh `run.State` against a fingerprint taken from the snapshot `after`, before the scribble. If the return aliased the live `pending.offers`, the check fails. If it aliased only the detached `after` snapshot, that is not a live leak and is correctly not flagged.
- **Suggested RED controls for Root:**
  - (a) Temporarily make `ForgeOffer` return `offers` uncopied: the probe should fail on "reached the live pending or Run".
  - (b) Temporarily change one part in the returned copy before returning: the value comparison should fail.
  - Restore the code after each control.
