# Autonomous reasoning effort / Extra High

GPT-6.1 Sol local model catalogue and the official Opus5.5 support table both include xhigh. The project default remains High, while High/xhigh/Max are chosen autonomously by complexity/risk/observed results. No live Codex-turn switch is claimed from a config edit.

The official Opus CLI runner now permits xhigh; explicit --effort, child environment and requested receipt retain the same value. Subscription/UI-account matching, credits/auto-recharge OFF, Opus-only result checks, no fallback, recursion and worktree/file boundaries are unchanged. One newly added full-path regression first failed at argument parsing, then all21 runner tests,49 Python tests, docs9 and handoff13 passed. Actual Opus session metadata records effort=xhigh/perTurnEffort=xhigh. An independent Opus xhigh review approved this routing scope; its separate M0 source finding remains blocked pending repair.

Source support: https://code.claude.com/docs/en/model-config#adjust-effort-level
