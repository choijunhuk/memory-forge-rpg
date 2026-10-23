# Root unit integration review — P1-ROOT-UNIT-INTEGRATION-REVIEW (Claude, Opus 5.5 High)

**Verdict: APPROVE (source level).** No blocker. Builds, tests and the Editor were NOT_RUN by Claude; this review read the source only.

The scope is the three Root-authored binding and preservation edits. The engine, C's turn barrier and B's forecast units are not re-reviewed
here, and this is not a final-game review.

## 1. `core/MemoryForge.CoreChecks/Program.cs` — APPROVE
- **Registration:** line 128 adds `int lateFailures = BattleLateActionsChecks.Run(root);` after the eleven existing checker calls (lines 117–127). Those calls are unchanged and keep their order: Trace, Opening, Turn, PlayerQuery, PlayerAction, MemoryV2, RunCreate, RunProgression, RunJsonNumber, CardText, BattlePreview.
- **Exit code:** lines 129–130 still require `failures == 0` and every existing `*Failures == 0`, and add `&& lateFailures == 0`. So a late-action failure returns exit code 1, and no earlier term was dropped.
- `BattleLateActionsChecks.Run` returns its `failures` count (`BattleLateActionsChecks.cs:190`), so the int binding is correct.

## 2. `core/MemoryForge.CoreChecks/BattleLateActionsChecks.cs` comment — APPROVE
- **Comment only:** lines 647–648 above `DeckCard` now say this corpus's public `Battle.create` card input is the string id plus the optional `assigned`. Other members are "outside this bounded corpus", and uid/used/token/temp are produced state. The comment no longer claims that `growth` is always produced state, which is accurate, since `growth` is a legal Core Create input in general.
- **Validation unchanged:**
  - The tamper probe at line 713 still injects `growth = 3` into a boundary deck and still requires rejection.
  - The fixed denominator check at line 645 is intact.
  - Root's edit loosens no validator, oracle, source pin or count.
- The runtime guard in `DeckCard` itself is A's repair, as stated, and was not re-reviewed here.

## 3. `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs` — APPROVE
- **Old mapper kept:** the private `EventText(BattleEvent)` mapper is preserved at line 1304, with a comment at line 1303 saying the richer digest now supplies the event log.
  - It has no callers left, so behaviour is unchanged.
  - An unused private method is not a C# compiler warning (only an IDE analyzer suggestion), so the Unity build and the test assemblies are unaffected.
- **Wording change:** the turn-summary sentence at line 1207 now reads "이 연습 세션에서 이미 확정된 결과예요. '계속'을 누르면 다음 행동을 할 수 있어요."
  - It still says the results are local to the practice session, already committed, and that "계속" continues.
  - Only the implementation-detail phrase "임시 텍스트 표시이고" was removed.
  - No PlayMode test asserts the old phrase (searched `Assets/`), so no expectation breaks.
- **No logic touched:** these edits change no turn-barrier logic (C), forecast labels (B), commands, Render, input or layout coordinates.

## Notes
- **Nothing found:** no constraint violation found: original values, candidate non-activation, private repository and the no-paid-path rule are untouched by these edits.
- **Still pending:** acceptance still depends on Root's targeted S1 Main Play tests, which are currently running. This approval does not cover them.
