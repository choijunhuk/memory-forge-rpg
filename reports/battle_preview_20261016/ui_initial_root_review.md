# Initial independent view review — changes requested

Codex read the complete author view diff, all new tests and report. Core forecast data/games rules are unchanged. Source-level findings before acceptance:

1. **Forecast hidden under a stale success/error notice:** BattleView computes the selected request's forecast but unconditionally renders any nonempty `notice` first. A successful play leaves a2.6s notice, so a quick next card/mode/target selection can reach ConfirmCard while the required prediction is not shown. The new draw/pick tests explicitly `AwaitNoticeExpiry`, so they avoid this actual usage condition. Repair selected-request visibility without changing timer/game data; prove immediate follow-up intent without waiting.
2. **Reservation wording exceeds the query contract:** `위력은 그때 상태로 정해져요` asserts when power is determined, while this API merely does not predict the future outcome. Use the narrower true statement that the actual reservation result is not previewed/known until resolution. Keep cost/absolute due exact and original reservation/delay rules unchanged.
3. **Preserve Root's third nullability annotation:** the author copied the earlier tests before the final `view!.GetComponentsInChildren` warning-only repair. Retain all three already runtime-guarded annotations on integration; no assertion removed.

All other reviewed direction is appropriate: derive values from the pure DTO, label automatic pick basis, show per-final-candidate result before the existing commit-on-final-pick action, hide draw/random outcomes, and preserve the original practice view. Actual compilation/Play/screenshots of this view patch remain NOT_RUN until repaired and imported. This is not a native-mouse or device acceptance.
