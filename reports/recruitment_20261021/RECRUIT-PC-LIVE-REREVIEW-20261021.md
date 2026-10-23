# Repaired live acceptance test review

Model: claude-opus-5-5; effort: high; session: 12f18c53-8d4d-45f9-86cb-1d2f469e1ff7; process exit: 0.

# RECRUIT-PC-LIVE-REREVIEW-20261021: repaired live tests

**Verdict: APPROVE.** I found no blocking defects. The read-only continuation safely completes the persistence proof that the earlier assertion blocked. Nothing in it claims a second pull or manual native pointer acceptance. I changed no files and ran nothing.

## Repaired files
- **Original test, line 67:** it now asserts the single `RecruitmentArrival` that a 1-pull actually shows. That matches `RenderRecruitmentResults`. The revision-4 guard at line 50 still runs before any click, so this test can never spend again on this account.
- **Continuation, `SpentLocalAccountReconnectsToItsPersistedResultAndOwnedRoster` (lines 117-173):**
  - **No mutation path:** it only refreshes over GET, reads the outbox, opens the confirmation and cancels it. It reads the 모집하기 button's state but never presses it. The button is disabled because 0 crystals and 0 tickets can't cover 160. Nothing is staged and no POST can happen.
  - **Persistence is proven:**
    - Each of 2 fresh lobbies starts with an empty credential field and no 1회 button, then re-authenticates.
    - Each checks the exact state: revision 5, free and paid crystals 0, all tickets 0, 22 total pulls, harin=2, rien=1, newest history entry = harin with resonance 1.
    - The second pass's full snapshot JSON must equal the first's (line 151).
    - 최근 결과 shows the real result in the arrival view, and the owned roster matches the server's owned characters.
  - **Safety:** the endpoint is limited to 127.0.0.1 over http. Saved connection preferences are restored and the credential variable is cleared, and nothing funds, resets or changes rates.
- **`RecruitmentReviewPlayModeTests.cs:131-134`:** the 니아/세라 assertions now read only text inside the active `RecruitmentReveal` panel. The cleared children are inactive and excluded. The test still asserts exactly one POST.
- **Recent-result guard (`MemoryForgeLobbyRecruitment.cs:109-111`):** unchanged and still correct.

## Low notes (not blocking)
- **The original test now fails, not skips:** with the metadata pointing at this spent account, `ExistingLocalAccountPullPersistsAcrossLobbyRecreation` fails at line 50 (revision 5 ≠ 4) before any click. It's safe, but it's a red result. Filter it out of runs against this account, or change that guard to `Assert.Ignore("account already spent")`.
- **The continuation's JSON (lines 162-165) has fewer labels than you described:** it has `mutationsInContinuation: 0` but no `priorPullCommittedBy` field and no "Button.onClick, not native pointer" evidence string, unlike the first test's JSON (line 97). The proof itself holds. For an audit trail you can read without the retained failure log, add:
  - an evidence string,
  - a note that the commit came from the earlier run, which failed at the obsolete tile assertion after committing.
