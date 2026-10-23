# P1-REQUIREMENT-STATUS-CURRENT-REVIEW — bounded factual review

Reviewer: official Claude Code Opus5.5 (High), included subscription only. Read-only on code/docs/receipts; only this file edited. Execution: **NOT_RUN** by reviewer (no build/test/docs/handoff executed). Scope: the 22 changed rows in `current_rows.diff` plus the date/`IMPLEMENTED` scope line, checked against `run_memory_core_20261016/root_acceptance.md`, `final_core_checks.txt`, `expedition_client_20261016/root_acceptance.md`, `actual_interaction.json`. No whole-project re-review.

## Verdict: CHANGES_REQUESTED (2 small wording corrections; no status change needed)

Row count verified: 4 top rows + §7.1–7.8 (8) + §8.3/8.4/8.5/8.6/8.9/8.12 (6) + §14.3/14.7 (2) + §16.2/16.4 (2) = 22. §8.13 stays `NOT_IMPLEMENTED`; server/§8.11/§16.5 rows unchanged; forecast is kept as in-development (§14.3, Unity row, review row), not counted.

### Required corrections

1. **§16.4 Unity 클라이언트** — cites `최종macOS빌드` but the remaining-conditions cell omits native input.
   Contradicting evidence: `actual_interaction.json` `native_primary_build.actualNativeMouseBattleTransition: "NOT_CONFIRMED"`, `diagnostic_build.physicalMouse: "NOT_RUN"`, `faultOwner: "NOT_DETERMINED"`; expedition `root_acceptance.md` l.22/35/39 ("Physical native input confirmation … remain separate").
   Smallest fix: append `·네이티브 물리입력(NOT_CONFIRMED)` to the remaining-conditions cell.

2. **§7.2 지도 생성** — `실제Unity표시/도달입력 있음` has no Unity link and does not say which input path was confirmed.
   Contradicting evidence: only the Editor pointer/EventSystem path is confirmed (`actual_interaction.json` `editor_pointer.mapNode: "Node:z1f0n0"` → `BATTLE`). The native map→battle transition is `NOT_CONFIRMED`.
   Smallest fix: `실제Unity표시/Editor포인터 도달입력 있음([원정](../reports/expedition_client_20261016/root_acceptance.md)); 네이티브 물리입력·모든 모바일/최악화면 수용은 별도`.

### Checked and consistent (no change)

- **P1 트레이스 생성기**: 273504/273504 attempts, 1000/1000 DONE, 182528/90976 (`final_core_checks.txt` l.49). Mid-Run restoration is called out as separate. The `VERIFIED(코퍼스)` label is scoped to the corpus.
- **C# 전체 Battle·Run·기억 저장**: Run21902 (l.43), 5 cases/2684/1074 (l.45–47), legacy 273504/1000 DONE (l.49), actions87991 (l.16), pure memory5451 (l.19) all match. The row keeps these pending: typed-number/JS-native diagnostics (l.27), generic base0 UNRESOLVED (l.14/18), mid-Run restoration, account/server, late candidates, device and whole game. The overall status is still PARTIAL.
- **Unity 수직 슬라이스**: 87/3 (`playmode87_pass.json`, expedition l.17), new macOS build and map launch (l.22/37). Native physical input, account persistence, lobby/party, art and forecast are kept as remaining. PARTIAL is correct.
- **반대 AI 최종 검토**: says it is not the whole-game final review, and the new forecast is in progress. Consistent.
- **§7.1** create215677 (l.26). **§7.3–7.7**: `archive 275` and `eventChoose 540` match l.49. Server and physical-input/device items are kept in the remaining-conditions cells.
- **§8.3/8.4/8.5/8.6/8.9/8.12**: the 85 public ops and 873 checks match l.31/33 (85 executed of 88 planned; the row does not claim 88). §8.5 and §8.6 stay PARTIAL. Unity persistence is described as in-session only (expedition l.7; core l.36). No DB, account or server claim is made.
- **§14.3/14.7**: Editor pointer, 87 Play and the 2.6s held-press regression (expedition l.29) match. 130%, 48dp, worst-case density, physical input and device stay pending.
- **§16.2**: PARTIAL, with typed/native, base0, restoration and server/device kept as remaining. Consistent.

### Non-blocking notes (optional, no status impact)

- P1 trace row: the 1000-run corpus is **legacy only** (`RunTracePrefixReplay: legacy scenarios 1000/1000`). v2 is covered by 1 of the 5 full cases plus the memory oracle. Writing `현행 legacy JS1000탐사` would be more exact.
- §7.7/§7.8 drop the old remainders (`플레이어 카드 선택·비용·무기 제외`, `전체 훅·상한`). The copied receipts have no per-event or per-relic coverage witness. The 1000/1000 parity shows the port matches on the paths that were exercised, not that every hook ran. Consider adding `개별 사건선택/32유물 훅 커버리지 증거 별도`, or linking the coverage source if Root has one.
- §8.4 and the other memory rows: the 3 over-capacity probes are `NOT_COVERED` (l.36). The rows don't overclaim, but a short note would prevent a later reader from treating capacity rejection as JS-parity-verified.
- Unity row: the old remainder `서버` was replaced by `실제 계정/영속저장`. Server is still tracked in its own `NOT_IMPLEMENTED` row, so this is fine. Adding `·서버` would keep the wording parallel.
- `final_core_checks.txt` l.5/11/18 still print `BattleRunCommandReplay: NOT_IMPLEMENTED (0 complete C# Run replays …)`. Core `root_acceptance.md` l.36 explains that these lines describe the older isolated suites. A reader who follows the ledger link may still see a contradiction. This is a checker-label issue, not a ledger-row issue.
- `원정87검사` (§7.3) means 87 PlayMode tests, not 87 checks. `87Play` would match the other rows.

The row count was not used as a basis for completion. No game policy, quantities or gates were evaluated or changed.
