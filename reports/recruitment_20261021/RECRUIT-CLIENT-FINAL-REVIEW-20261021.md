# RECRUIT-CLIENT-FINAL-REVIEW-20261021

Model: claude-opus-5-5; requested effort: high; session: 12f18c53-8d4d-45f9-86cb-1d2f469e1ff7; process exit: 0. Subscription authentication and separate credits-off attestation checked by the runner.

# RECRUIT-CLIENT-FINAL-REVIEW-20261021: final client review

**Verdict: APPROVE.** I found no blocking defects. This covers the client code only. The server's independent review, the PostgreSQL work and the server contract requirements are still with you; your 21-pull run is a preflight, not that review.

I changed no files and ran no tests, shell, or AI calls. The 12/12 Unity and 148/148 BCL results are yours; I didn't rerun them.

## The two items you asked me to focus on
**Beginner consolidated rate: fixed** (`MemoryForgeLobbyRecruitment.cs:174-196`).
- **Matches Core:** the finite DP over (pulls since SSR, got first SSR) follows Core's rules exactly:
  - It uses Core's `SsrRate`.
  - It applies the same first-SSR cap guard as `Gacha.cs:150`.
  - An SSR resets the count to 0 and sets the got-first-SSR flag; any other result adds one.
- **Window:** it sums expected SSRs over the `cap` pulls and divides by `cap`. The 50-pull window is fully reachable in steps of 10.
- **Standard banner:** still uses Core's mean-pulls renewal rate, which is correct there.
- **Bounds:** with `cap ≤ 1000` the array indices stay in range and the cost is trivial. A beginner cap of 0 is rejected (`RecruitmentPublicSnapshot.cs:92`).
- **Test:** the assertion of 2.1385% matches my own derivation (≈2.1385%).

**Late receipt: fixed.**
- A committed receipt is now kept in memory (`:368`).
- 최근 결과 uses that receipt while the snapshot is stale and belongs to the same account; otherwise it uses `History[0]` (`:109-110`).
- The notice saying the refresh failed is no longer overwritten (`:379`).
- Reconnecting clears the cached receipt (`:251`).
- The new 503-refresh test covers this path.

## Previous P3s
- **Selector wording:** now "선택할 동료: {name}", so the Korean particle problem is gone.
- **Cost rule hidden by the selector button:** the cost rule is now drawn only when there is no selector button, so nothing overlaps. The confirmation screen still shows the cost.

## Non-blocking (P3)
- **Older cached receipt can win:** `:109` prefers the cached receipt whenever `RecruitmentNeedsRefresh` is true. Suppose a refresh succeeded with newer history from another client, and then a failed handshake marks the snapshot stale. 최근 결과 would then show the older cached receipt instead of the newer `History[0]`. This is a rare edge case. Fix: prefer the receipt only when `receipt.Revision > snapshot.Revision`.
- **Beginner fallback when `maxPulls` is missing:** the client falls back to `Hard` for the beginner cap. Core's guard reads `MaxPulls` directly, so a server that omits it would make the client's numbers disagree with Core. Consider making `maxPulls` required for beginner banners in the parser.
- **Desktop build helper is macOS-only:** `MemoryForgeDesktopBuild.cs` targets macOS only (`StandaloneOSX`, `.app`). That's fine for the current PC-only, macOS setup. A Windows build would need its own target.
