# First independent review — not yet accepted

This records Codex's review of the initial Claude implementation in its separate worktree. The source was not imported into Root or committed as an accepted server. Original data, Core, production SQL/OpenAPI and protected runner remain unchanged.

Requirements: original §17.2 transactions/receipts, §17.4 public projection, §17.5 v2, §14.7 recovery, §20.9 P1-2. This is the first local Development-only practice slice, not production authentication/PostgreSQL or Unity network recovery.

## Changes required before integration

1. **High — endpoint configuration reload bypasses the startup restriction.** `Program.cs` rejects initial `Kestrel:Endpoints` but keeps the framework's reloadable endpoint loader. Root launched the real built host from a temporary directory with an empty appsettings file, then added an endpoint on a second **loopback-only** port. That socket became reachable. No public/wildcard address was used in this probe. Replace/disable the default reloadable endpoint configuration and add a real configuration-reload regression. Startup address inspection alone is insufficient. [Microsoft's endpoint documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0) describes reload and replacing the default loader.
2. **Medium — privacy-suite success depends on an arbitrary number of responses.** The actual checks reached 221 assertions and scanned all 190 captured responses without a leak exception, then failed solely at `Check(scanned > 200)`. Retain every recursive key/value/credential/log assertion; assert meaningful coverage rather than an incidental command/response count. Do not claim an observed leak from this failure.
3. **Medium — write/read size guards disagree.** `Load` refuses envelopes above 512 MiB while `Persist` can commit one above that threshold, making an accepted account unreadable on its next request. Refuse before committing at the same explicit operational limit, preserving the original file and unconsumed request, or resolve the inconsistency without silently pruning history.
4. **Documentation — power-loss guarantees exceed evidence.** A data-file flush and rename are implemented; parent-directory fsync and macOS F_FULLFSYNC are not. Tests cover process termination/restart. Do not promise a complete historical envelope after arbitrary power/storage failure, or deterministic re-execution of a lost create receipt: creation chooses a fresh seed/runId. Preserve the distinction and require checkpoint inspection/recovery for power-loss ambiguity.
5. **Privacy-check diagnostics/oracle.** Raw response assertions and process tails can print the very values a failing privacy check detects. Use safe metadata and private diagnostics. Extend seed-string and structured private-log detection; the initial oracle would accept a seed encoded as a string or an RNG object in a log. These are checker defects, not evidence that the present host leaks them.
6. **Nonempty save restart coverage.** When only a later bounded departure earns the first reward card, that save also needs a fresh-process deck and original receipt check. The initial suite restarts the first save, which can be empty, then checks later earned cards in the same process.

## Actual preliminary evidence

- Host and BCL checks each compiled with zero warnings/errors.
- The separate Root HTTP probe passed 20 assertions before reproducing item 1: authentication, ownership, strict inputs, real Core create/enter, rejected/stale command immutability, exact durable receipts and new-process public reconstruction. See `preview_http_boundary.json`.
- The authored real-process suite reached its last privacy stage: actual gameplay/v2 save, SIGKILL/restart, cross-process version races, write-failure rollback, genuine partial Core Enter rollback and corrupt/mismatched-store preservation passed before item 2 stopped the suite. This is **not** a full suite PASS.
- An independent Sol Max review of frozen PublicView/Contracts/Content found no definite privacy or normalized request-identity defect. It did not run HTTP/storage tests.

## Explicit remaining scope

The initial public DTO supplies raw slot base/temp/max/seal fields, pile counts and default-without-picks card checks. Calculated slot occupancy/capacity, discard/exhaust selection candidates, manual effect alternatives and complete network UI recovery remain follow-up work. Do not interpret a default blocked result as rejection of every manual selection. Event display relies on the content-bound client. The existing local Unity practice client has no server transport in this unit.

PostgreSQL, real account ownership/authentication/collection/economy APIs, production deployment, migration and physical-device acceptance remain separate. Existing balance targets and unresolved misses are unaffected.
