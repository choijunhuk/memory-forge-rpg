# Independent transport review

Author: official Claude Code Opus5.5. Reviewer/runtime owner: Codex. Source and tests match `source_hashes.json`.

The initial draft had three concrete boundary problems. They were corrected together with the regression tests, then reviewed and run again:

1. A generic pre-commit error whitelist was unsafe. Create has no version fence; 400 parsing and some507 load failures precede receipt lookup. Resolution now accepts only an exact versioned commands/memory-commit attempt's409STALE_VERSION or422, with the same credential's immediately preceding account summary naming that current run at a journal version at least the pending expectedVersion. A future-version body remains pending. DONE is allowed for memory commit.
2. A global private-name guard missed battle-specific `zones` and `cards`. Those containers are now refused at their battle location; public pile fields must be counts. Public run zones/reward cards and card text remain intact.
3. A new request was staged before a failed owner handshake even though no POST had occurred. Root reproduced this against the actual server (`preflight_red.json`). The handshake now precedes ID generation/staging, and the same captured credential and handshake are reused for the POST. The actual regression is green (`preflight_green.json`).

Root checked detached public data, request/context binding, one-operation exclusion, exact-current outbox resolution, cancellation/disposal, monotonic refresh and receipt-event suppression. The accepted outbox's lexical checker is reused with internal visibility; its record, native lock, permissions and validation remain the same. No new network framework or dependency was introduced.

Final evidence: identical NUnit83/83 on .NET, actual Unity Play248/248 and Edit3/3, actual server-process restart with a fresh client process, and actual Editor Mono truncation/reconnect/retry. The protocol fixture listener in NUnit does not stand in for those separate real-Core HTTP executions. There are no unresolved blocking findings in this source unit. The optional server UI is the next work unit and is not approved by this review.
