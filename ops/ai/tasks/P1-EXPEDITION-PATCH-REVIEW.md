# Opposite-author review of Codex's narrow expedition corrections

Use official Opus5.5 High, included subscription only; no API/credits/extra usage/fallback/Bash/MCP/agents/recursive CLI. You are not alone. Own ONLY reports/expedition_client_20261016/root_patch_opposite_review.md. All code/scenes/data/diffs readonly; Root alone writes Editor.

Root independently read your1058line component, query bridge and test code. Actual .NET build0warnings0errors, initial6PlayMode tests:5PASS/1FAIL at overlay raycast testline177. Actual newly created MemoryForgeExpedition scene (old Battle scene preserved) shows the map, real reachable-node pointerclick changes MAP→BATTLE HP243/AP4; real mode pointer changes selection only V1/AP4. Root reproduced overlay failure: immediately rebuildingCanvas in the SAMEFRAME yields0hits; the nextactualframe at the SAMEscreenpoint yields2hits with ExpeditionLogInputBlocker on top, click spendsnothing V1/AP4. Thus test needs a real frame before its second pointer, not removal of interception assertions.

Root made4bounded changes, now copied into A with exact Author→Root.diff files beside report:
1) Added yieldreturnnull after LogButton.onClick before ForceUpdate/raycast; kept all positivecontrol/hits/overlay/no-spend assertions.
2) StartPractice calls actual MemoryV2.Migrate(empty).ToJsonGraph instead of hand-constructing7storefields. Same session-only policy, no account/disk writes.
3) DeckSheet had i<6 cap, making7th+ownedpartunreachable. Replaced cap with a140highscrollingparts viewport, allactualPartsFound items rendered. No Core state injection or inventory change.
4) Forge.offers view now lets player Proceed/leavewithoutapplying a part (sourceRun.ProceedallowsleavingFORGE). PreviouslytheonlyexitwaschoosingpartorwholeRunabandon. AddedbuttonusesCore, no refund ornumericchange. Removed user-facingmemoryversiontechnicalstrings whilekeepingAPIversion/preview/costs.

Initialactualmap1600x900screenshot is readable; visualverdict92stylepass storedbeforeedits. Battlecopy usesoriginalstyles/statushelpers. Currenttests rerunningnow, noPASSyet. Review Codexdiffs for correctness/sideeffects and no lostoriginalrequirements. ReturnAPPROVEorconcretebug/file/line/repro/fix. Your own originalfeaturegaps(cardtext/forecast/lobby/durablestore) remainPARTIAL, notdeleted; don't requirewholegamefortheinitialslice. YourCLIexecutionNOT_RUN. Onlyreportedit, finishpromptly.
