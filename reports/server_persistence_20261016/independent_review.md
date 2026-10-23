# Independent Codex reviews of Claude-authored source

Read-only Sol6.1 Max review, separate from Root execution and Claude authorship. No reviewer test/Editor execution or file edits were performed.

## Projection and identity

Frozen PublicView `b759b0db21dd`, Contracts `cbbc3e961629`, Content `16a73f796609`: no definite public-data leak or normalized request-identity defect found. Projection excludes seed/RNG/draw order/pattern/index/passive/private stores/receipts/credentials; costs/modes/checks use real Core queries. Identities include the normalized operation, runId, expectedVersion and arguments, preserving list order and rejecting duplicate/unknown fields. The fixture supplies trusted party/seed inputs, never a client state.

Limits identified: raw slot values; pile counts without manual candidates; default-without-picks blocked checks; content-bound event display; move projection depth4 (current authored maximum2). These are not full network recovery acceptance. Final Contracts changes only add the safe fixed 507 message; other reviewed projection/content hashes are unchanged.

## Check-suite review and re-review

First frozen checks `071094b54059`: arbitrary response-count failure; raw failure diagnostics could re-expose secrets; seed strings/private structured logs escaped the oracle; nonempty later-save restart was not guaranteed. Two-process fresh-ID races, durable receipt ordering and the genuinely partial Core failure probe were present. The defensive first-action fallback conflict assumption was a future robustness point, not a current trusted-party defect.

Fixed frozen checks **`a0a09181b399235ace27919e460add0f97192231bbba64287405f54b350cb8b3`**, equal to the imported source: all concrete findings resolved. Required scenarios and complete response capture replace the incidental count. Main/Finish/Same/Status use safe metadata; raw diagnostics stay private0600 and planted markers test the formatting and detectors. The earned-card save must be SIGKILLed and served by a new process with its original receipt. No removed/weakened important existing assertion was found.

Remaining limits: check-side independent replay covers selected public values, not entire private RNG/log/events equivalence; privacy scans cover known values/patterns. Root's separate observed results are in `root_acceptance.md`; the reviewer does not claim to have executed them.
