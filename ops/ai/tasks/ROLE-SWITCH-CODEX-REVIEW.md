# Read-only Codex review: Claude root, Codex image/character/review role switch

User instruction (2026-10-17): Claude takes development as root. Codex only does image generation and content edits, final character visual design and read-only independent code review. Applied after SAVE-PARTS commit `0623d61`.

Reviewed diff (uncommitted at review time): `AGENTS.md`, `CLAUDE.md`, `docs/AI_WORKFLOW.md` §17, `docs/START_CLAUDE.md`, `ops/ai/runtime-policy.json`.

Caller: Claude Opus root session (claude-opus-5-5). Callee: official Codex CLI 0.162.1, `codex exec -s read-only -m gpt-6.1-sol -c model_reasoning_effort="high"`, ChatGPT login (`codex login status`), no OpenAI/Codex API key or base URL in the environment. Codex was told not to edit, commit, call agents or generate images.

Checks requested: precedence gaps that would leave Codex acting as orchestrator, any weakening of billing/model/approval/recursion/cross-review/character-expression rules, runtime-policy consistency with `tools/ai/run_claude.py` and `tools/ai/test_handoff.py`, and fidelity to the user instruction. Findings use `claude_autonomy.feedback_classes`.

Local checks before review: `tools/ai/test_handoff.py` 31/31 OK and `npm run test:docs` OK (scratch venv with `tools/ai/requirements.txt` + `tools/audit/requirements-docs.txt`); manifest entries for all five files already differed at HEAD.
