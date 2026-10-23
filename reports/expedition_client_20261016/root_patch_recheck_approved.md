# Final narrow UI patch recheck — P1-EXPEDITION-PATCH-RECHECK

- Reviewer: Claude Code (Opus 5.5, High), included subscription only. Read-only: no Bash, no MCP, no agents. This is the second review of the same issue. The scope is the test predicate at line 357 plus the 4 Root patches I already approved.
- Tests and build by this reviewer: NOT_RUN. Root's actual run of 7 tests (6 UI tests and the strengthened full-flow test) is in progress. This review grants no PASS.

## Verdict: APPROVE

### Predicate at `ExpeditionClientPlayModeTests.cs:357`
`t.StartsWith(savedLine, StringComparison.Ordinal) && t.Contains("용량")`
- **It matches a label that really exists.** `MemoryForgeExpeditionClient.cs:764–766` builds a single label in this order:
  - `"기존 덱 유지"` or `"변화 N개 저장"`
  - then `" · " + CharacterName(cid) + " 용량 " + used`, when `used` is not null

  Both conditions therefore check the same Text element, which `AssertReadable` tests one element at a time.
- **"용량" always appears after a real commit, including when nothing is selected.**
  - `lastSave` is the `response` from the commit (client:251).
  - The response always includes `used` (Run.Memory.cs:291).
  - `MemoryV2.Apply` adds one `used` entry for every character in the result memory (MemoryV2.cs:308–313). The result memory is built from `before`, which holds the party members.

  So the `used` dictionary is never empty for a party, and the `chosen.Count == 0` branch ("기존 덱 유지 · … 용량 …") also passes. There is no flaky or empty branch.
- **No needed check was lost.** The predicate checks more about what the player reads than `StartsWith` alone would.
  - Version 1 is still checked through real state: `SessionMemoryVersion == 1` (351), `next.MemoryVersion == 1` (363), and the `Create` fingerprint from the saved memory and version (364).
  - The checks that the Core result, the store memory hash and the committed `used` equal the preview's `used` are unchanged (350, 352, 353).
- **The removed "기억 버전" label stays consistent with the design.** No section of `design_v0.8.1.md` requires the player to see the version; §14 at line 1270 treats it as a server lock value.

### The 4 Root production patches
- They are unchanged from the versions I approved in `root_patch_opposite_review.md`: overlay one-frame yield, `Migrate`-based session store, scrolling `FoundParts`, Forge offers leave button and removal of the version text. No new findings.
- The overlay fix waits one real frame and keeps all of its assertions: the positive control, non-empty hits, the non-Button top hit and the no-spend fingerprint. This matches Root's repro in the real scene: 0 hits in the same frame, 2 hits on the next frame with `ExpeditionLogInputBlocker` on top, and AP4/V1 unchanged.

## Remaining blockers
- None for this predicate or the 4 patches. Root should record the outcome of the 7-test run as actual PASS/FAIL.
- The non-blocking notes from the first review still stand: the HTML reference has no Forge offers leave button, a paid 60-coin variation is given up with no refund if the player leaves (wording note only), and the test's mirror store is still built by hand. The original PARTIAL features (card text, forecast, lobby, durable store) are unchanged.
