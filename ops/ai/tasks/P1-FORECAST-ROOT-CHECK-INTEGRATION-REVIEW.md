# Opposite review of Root's forecast checker integration and test-only nullability annotations

Official Opus5.5 High, included subscription only. No API/credits/extra/fallback/Bash/MCP/agents/images. Separate A; all code/fixtures readonly. Own only reports/battle_preview_20261016/root_patch_review.md. You are not alone; do not revert other edits. This is bounded to Root's changes, not a new whole-Core review.

OpusBMax/xhigh authored Battle.Forecast/Run.BattleForecast, producer and consumer; Root independently read all543consumer lines, all329producer lines, allNode tests and production source before importing. Root's actual source JS9cases/70forecasts/40commands, Node22/25each6/6PASS, CoreChecksbuild0errors/0warnings. Full C#runtime is running; do not claim it passed.

Review ONLY:
1. Program.cs adds BattlePreviewChecks.Run(root) and its failure result to the existing final && aggregation. All old suites/counts stay registered.
2. BattlePreviewChecks.Run: Root spotted the catalog snapshot used BattleDataLoader.Load(root) while RunCase actually uses a separate RunDataLoader.Load(root). Root adds runDefinitions=Full(Catalog(runData.Memory.Battle)) before execution and checks it after allcases, explicitly scoped to the five catalog lists. This proves the actual Run-owned card/character/weapon/part/relic inputs are unmodified, not a different unused data instance. No expected values or failures relaxed.
3. ExpeditionClientPlayModeTests.cs lines187/189: two null-forgiving annotations only (noticeLabel!.text and AssertReadable(view!,...)) AFTER the actual runtime Assert.That(object!=null&&active) checks. Fresh Unity compile warned CS8602/CS8604 because NUnit assertions do not inform C# flow analysis. No condition/assertion/timer/pointer step removed. Original old-codeRED then87GREEN still preserved; Root will rerun actual suite after this warning-only repair.

Root copied current necessaryfiles and old accepted counter evidence. No code edits. Return APPROVE or precise concrete defect on those changes. ExecutionNOT_RUN by reviewer; no invented PASS. Do not expand into forecast UI integration, policy, base0, physical mouse or a second broad API review.
