# PC-DESKTOP-REVIEW-20261021

Model: claude-opus-5-5; requested effort: high; session: 12f18c53-8d4d-45f9-86cb-1d2f469e1ff7; process exit: 0. Subscription authentication and separate credits-off attestation checked by the runner.

# PC-DESKTOP-REVIEW-20261021: desktop build and diagnostics

**Verdict: APPROVE.** I found no blocking defects in either file. I changed no files and ran nothing. Reading code can't show that the screen actually renders correctly. Your screenshots are the render evidence, and the click-position problem with native automation is still unresolved, so it doesn't count as manual recruitment acceptance.

## Is the build safe and scoped?
**`MemoryForgeDesktopBuild.cs`**
- **Builds only what's needed:** it builds just the two existing scenes, for macOS only, with only the Development option. That Development flag is what turns on the existing dev-only connection guard (`MemoryForgeLobbyRecruitment.cs:222-228`). Release builds still keep the connect button disabled. No authentication, transport or outbox rule changes.
- **`runInBackground = true` (`:16`) changes nothing in the project:** `ProjectSettings.asset:85` is already `runInBackground: 1`. Android and iOS ignore this setting, so it doesn't affect phone builds.
- **Failures are reported:** a failed build exits with code 1. If `BuildPlayer` throws (for example, the Mac build module isn't installed), batch mode also exits non-zero.

**`MemoryForgeDesktopDiagnostics.cs`**
- **Only runs where intended:** the code is compiled only into Development builds and only starts in the macOS player, so it never runs in the Editor, on Android, or in release builds. In release it's an empty class that nothing ever adds to a scene.
- **What it logs:** one boot line (scene name, focus, background flag, frame number, counts, screen size), focus changes, and one line per mouse click. The click line has the pointer position, the name of the UI object under it, and the input module type.
- **No sensitive data:** object names are at most public content IDs like `Banner:standard`, `Companion:sera` or `RecruitmentSelector:sera`. A click on the credential field logs its name (`RecruitmentCredential`), never what was typed. No account data, wallet values, credential or response bodies are read. Logs go only to the local Player.log.
- **Lifetime:** the object destroys itself after 600 s of real time. It isn't kept across scene loads, so it also goes away when the expedition scene loads.

## Low notes (not blocking)
- **Check the commit:** at the start of this session `ProjectSettings.asset` wasn't modified. Before committing, check its diff so no build-script setting change (including from the Android script) slips in unnoticed.
- **Restore setting:** for tidiness you could save `runInBackground` before the build and restore it in a `finally`. It has no effect today.
- **Expedition not covered:** because the diagnostic object is destroyed on scene change, it records nothing about input in the expedition scene. If you need that evidence, keep it alive across the load (still within the 600 s limit).
- **Pointer coordinates:** the click line logs `Input.mousePosition`. Since automated clicks leave that value unchanged, a log that shows "none" under an automated click proves nothing about manual clicks.
