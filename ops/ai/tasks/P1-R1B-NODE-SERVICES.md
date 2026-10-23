# R1b — exact authored Run node services, official Opus xhigh

You are not alone in this repository. Root is the sole Unity Editor writer and is finishing R1a acceptance while you work in isolated A. Do not revert others or touch any file outside ownership. Official Opus5.5 only, included subscription only; no fallback/API/credits/extra usage, no CLI recursion/subagents/Bash/MCP. Root checked the billing path and runner enforces account attestation before inference.

## Requirements and role
Original v0.8.1 §7.5–7.7 rest/forge/shop/events and §8 common card assignment; existing source engine/rules-core.js Run.assign/rest/forgeOffer/forgeApply/forgeRemove/buy/eventChoose (around1815–1922). These services unblock real exploration after battle/rewards. Exact original behavior and all numeric data are fixed, not design choices. Read source sections fully and relevant typed data/helper files. Do not rewrite the design or repeat whole-roadmap planning.

Own ONLY core/MemoryForge.Core/Run.Services.cs and reports/run_progression_core_20261016/services_author_report.md. All existing Core/helpers/state/data/JS/tests are READ ONLY. The new file is a placeholder solely to grant edit permission; replace it with COMPLETE supported commands, no product stubs. If a concrete helper gap requires another file, report it and complete independent work. Root will integrate a narrow followup if necessary.

R1a Core is now real, compiled0errors0warnings, full old suites + host8519 passed. Five-case full-state oracle:354supportedattempts/223queries;1000legacy prefixes112822attempts. Actual UnityPlay27/Edit3 PASS. All five incoming Core files now copied to your worktree. They are untracked in A: git diff alone misses them; inspect files directly. Run partial methods/fields/typed RunEventEffects reuse accepted R0/M0. Run guard catches overflow; use checked operations and helpers ToInt/JsOr/BigIntegerRound/FieldList/KeyEquals/NextCardKey/PartChoices/RelicChoices etc as appropriate. Do not invent their signatures; read them.

## Public API
Rest(string choice,string? key=null) void; ForgeOffer(string key) detached Dictionary<string,object?>; ForgeApply(string partId) void; ForgeRemove(string key) void; Buy(string kind,string? id=null,string? key=null) void; EventChoose(int index,object? keys=null) void (string or JSON string-list picks); Assign(string key,string charId) void. Public data snapshots/returns must not expose live state. Reuse MemoryJson.Copy etc.

## Preserve source order and semantics
- Rest phase/uses guard, memory_anvil CANNOT_HEAL; floor maxHp*(30+old_compass10)/100, upgrade plus/nonweapon, uses--.
- Forge exact source RNG/part order; ForgeOffer does not consume a use; apply checks pending offers only, preserve existing Q4 phase flexibility, merge mods and null offers then uses--; remove source checks.
- Shop repeat removeCost:null -> NOT_FOR_SALE BEFOREcoins/card; first50+25removals unchanged; check/coins/sold/order for cards/parts/relics unchanged.
- Event phase then choice/done BAD_CHOICE then requiredcoins and WOULD_KILL BEFORE effects. Preserve effect order and JS Math.round hpk behavior exactly (hpRef fallback, party scale). Coins/hp/heal/maxHp/maxHpPct/nextAP/curse/relic/card/parts/variation->FORGEreturn/growth/copy/remove in source order. Variation returns BEFORE done. Stable NextCardKey, copied card gets new key/gained. Missing picks fallback curse then firstnonweapon exactly. Event.done/choice after effects. Keep original native-invalid data limitations explicit; do not introduce general transactionality or silently different input policy.
- Assign common+party guard; v2 any memoryBefore key forbids reassignment EVEN same-character, source REASSIGNMENT_REQUIRES_SEPARATE_FLOW; savepending recaptures memory.

Root's independent unchanged-source denominators AFTER this unit: fivecases147/245/221/221/1845=2679attempts/2474battleattempts/1162battlecommits/1064queries before sole commitMemory;1000legacy272504attempts before1000commitMemory commands. These are intended verification denominators, NOT results. Full C#Run completion remains0 until actual commitMemory/return/reload. Do NOT implement memoryChanges/preview/commit/restore stubs or mark full Run complete.

Return a concise author report APIs/source mapping/limitations/NOT_RUN. You have Read/Glob/Grep+two Edit permissions only; Root compiles/tests independently. Write implementation promptly within the call; do not use the30minute timeout for repeated analysis or fill with policy restatement.
