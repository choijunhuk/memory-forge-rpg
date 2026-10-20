# 2026-10-21 user update: shared implementation

The user explicitly changed the previous exclusive development split: Codex and Claude may now share implementation at roughly equal workload. The share is a task-allocation preference, not an enforced token quota or subscription transfer. Codex may implement UI/runtime changes, coordinate bounded official Claude jobs, test and integrate; the older Claude-root-only and Codex-read-only implementation prohibitions are superseded by this direct instruction.

Independent work uses separate worktrees with one owner per file/slice and one declared Unity writer. Meaningful changes receive an independent review before integration. The existing fixed models, subscription-only billing, protected creative originals, expression standards and approval boundaries remain in force. No user-only expression standard is changed by this update.

This followup assigns the home/catalogue and APK diagnosis/guard to Codex, the server forecast transport/display and its tests to Claude, with independent review in the opposite direction. Existing account/economy/ownership state is never invented for UI presentation.

## Current operating contract

- Either declared task owner may plan, coordinate bounded work, implement, test/build, integrate, commit/push, manage PR/CI and record acceptance. The latest preambles in `AGENTS.md`, `CLAUDE.md`, `OWNERSHIP.md` and `docs/AI_WORKFLOW.md` supersede only their older exclusive role/dispatch restrictions; historical contracts stay intact.
- The root owner may call the other official CLI after existing subscription, account, model and billing checks. A called job cannot call either AI again. Separate worktrees and non-overlapping file ownership remain required; no recursive AI loops or concurrent writes to the same file/scene/prefab.
- One declared Codex or Claude task owner holds `ops/ai/local/exclusive-control.lock` for Unity writes. The writer hands off only after checking Editor/test state. `single_writer` remains true.
- Meaningful Claude-authored changes receive independent Codex review; meaningful Codex-authored changes receive independent Claude review. Server, save/memory, economy, auth, migration, netcode and core rules wait for the required opposite reviewer before integration. Review is read-only; the author fixes and reruns verification.
- Creative originals and image content remain Codex-owned. Claude original-write guards, `art/requests/`, imported-copy checks and expression standards (§18.1/§18.6) remain unchanged. The `art/<folder>` delegation restriction concerns creative-original jobs and does not forbid Codex implementation in its assigned worktree.
- Fixed `gpt-6.1-sol` / `claude-opus-5-5`, High/xhigh/Max, included subscriptions only, no API/extra/purchased-credit spending or auto-recharge, existing approval boundaries and GitHub Private remain in force.
- `root_session_controls`, `root_session_audit` and `audit_root_session.py` continue to cover Claude sessions only. They do not audit Codex App/native UI. Keep native Codex execution/test/lock/review evidence separate; no change to the audit script is part of this update.

When applying these changes to a checkout with preexisting local creative-guard additions, merge the role fields and preambles without replacing its `technical_guard` block or §18.6/§18.7. This worktree predates those original-checkout additions; the shared allocation does not remove or authorize bypassing them.

Verification after the role update: handoff **33/33** using the existing `public-query-validation-venv/bin/python`; docs **9/9**; `git diff --check` clean. Snapshot comparisons kept model/billing settings, MCP allow/deny lists, Claude session controls/audit and `creative_ownership` unchanged, plus the entire historical `AI_WORKFLOW.md` body from §1 onward. The handoff run emitted existing PyMuPDF/Swig deprecation warnings. No model, Unity or device invocation was part of these role checks.
