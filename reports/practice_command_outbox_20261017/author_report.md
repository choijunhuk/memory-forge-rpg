# P1-PRACTICE-COMMAND-OUTBOX-20261017 — author report (Claude Code, Opus 5.5)

Source continuation: P1-PRACTICE-OUTBOX-SOURCE-20261017 (xhigh, same session). Root interrupted the earlier Max call because Mono
measurements showed that `FileShare.None` alone does not exclude a second process. That call had written only a superseded plan
into this report, which is replaced below.

## Plan (source-only call)

- **Role and scope:** §14.7/§17.2/§17.4, boundary step 4. A sent mutation's exact requestId and body must survive a client close and
  be retried, never regenerated. This call writes only `PracticeCommandOutbox.cs` and this report. Tests, transport and UI belong to
  the next dependent calls.
- **API:** the immutable `PendingPracticeCommand`, plus `PracticeCommandOutbox(filePath, endpoint, account)` with `Load()`,
  `Stage(route, exactJsonBody)` and `Confirm(expectedPending)`. It accepts only the three POST Development mutation routes. The
  requestId and expectedVersion are read from the exact body under the server limits.
- **Validation and record:** Newtonsoft walks the body to check shape, depth, duplicates and trailing content, after a small lexical
  pass that refuses what Newtonsoft tolerates (comments, single quotes, octal/hex, NaN, trailing commas). The closed canonical-ASCII
  envelope is `{schema, sha256, record}`, bound to the endpoint and account. A file that does not exactly re-encode is refused and
  left as it is.
- **Lock and publish:** a permanent sibling `.lock` is opened with `FileShare.None` for same-process exclusion, plus a checked native
  `flock(fd, LOCK_EX|LOCK_NB)` held for each whole Load/Stage/Confirm. EINTR retries are bounded; EAGAIN/EWOULDBLOCK (11 on Linux,
  35 on macOS) means busy; any other failure or unsupported platform fails closed. Publishing writes a private same-directory temp
  file, calls `Flush(true)`, then makes a non-overwriting `File.Move`.

## Delivered (source only)

`client/MemoryForgeUnity/Assets/Scripts/PracticeCommandOutbox.cs` uses `MemoryForge.Client` with BCL, Newtonsoft and libc
`flock` only. It has no UnityEngine dependency, and nothing calls it yet, so the existing practice UI is unchanged. The NUnit
file was not touched (it is still empty) and is left to the next call.

### API

- `PracticeCommandOutbox(string filePath, string endpoint, string account)`
  - `filePath`: an absolute record path in an existing directory. It is explicit, trusted configuration; nothing is created or
    locked in the constructor.
  - `endpoint`: exactly `http://127.0.0.1:<1..65535>`, with no credentials, path, query, fragment or leading zero.
  - `account`: the fixture alias rule from the server, 2..32 characters of `[a-z0-9-]` starting with a letter.
  - Invalid arguments throw `OUTBOX_INVALID_CONFIGURATION`; null arguments throw `ArgumentNullException`.
  - Properties: `Endpoint`, `Account`.
- `PendingPracticeCommand? Load()`
  - Runs under the lock.
  - Returns null only when no record file exists.
  - A corrupt record, a record with another binding, or an unreadable store throws. It never reads as "nothing pending".
- `PendingPracticeCommand Stage(string route, string exactJsonBody)`
  - Returns only after the record is durably published.
  - Staging the same endpoint, account, route and exact body again returns the pending record unchanged.
  - The same requestId with another route or body throws `OUTBOX_REQUEST_ID_REUSED`; any other request throws
    `OUTBOX_PENDING_OTHER`. In both cases the record is kept.
- `void Confirm(PendingPracticeCommand expectedPending)`
  - Under the lock, deletes the record only when it is exactly the current request (endpoint, account, route, body).
  - Otherwise throws `OUTBOX_NOT_CURRENT`. A failed delete throws `OUTBOX_CLEAR_FAILED`.
- Route helpers: `PracticeCommandOutbox.CreateRunRoute`, `CommandRoute(runId)`, `MemoryCommitRoute(runId)`. Run IDs must be `pr-`
  plus 32 lowercase hex digits.
- `PendingPracticeCommand` (sealed, get-only): `Endpoint`, `Account`, `Method` (always `POST`), `Route`, `Body` (the exact string),
  `RequestId`, `ExpectedVersion` (`int?`, null for a new run), and `BodyUtf8()` (a fresh copy of the exact bytes on every call).
- `PracticeOutboxException.Code` values:
  - Configuration and requests: `INVALID_CONFIGURATION`, `INVALID_REQUEST`.
  - Lock: `BUSY`, `LOCK_FAILED`, `UNSUPPORTED_PLATFORM`.
  - Pending conflicts: `PENDING_OTHER`, `REQUEST_ID_REUSED`, `BINDING_MISMATCH`.
  - Record state: `RECORD_CORRUPT`, `RECORD_UNSUPPORTED`.
  - File I/O: `READ_FAILED`, `WRITE_FAILED`, `NOT_CURRENT`, `CLEAR_FAILED`.
  - Every code carries the `OUTBOX_` prefix. Messages are fixed text with no body, path or secret.
- Public constants: `Schema`, `Method`, `CreateRunRoute`, `MaxBodyBytes = 65536`, `MaxBodyDepth = 8`, `MaxRecordBytes = 1 MiB`,
  `LockSuffix = ".lock"`, `TempSuffix = ".tmp"`. Temp files are named `.<record file name>.<32 hex>.tmp` in the record's directory.

### Invariants

**Accepted requests**
- Routes: only the three POST mutations. Queries, GET routes, `battle-query`, `memory-preview`, trailing slashes, query strings and
  malformed run IDs are `OUTBOX_INVALID_REQUEST`.
- All input validation happens before the lock, so a refused request creates no file at all.
- Body:
  - 1..65536 bytes of strict UTF-8. A lone surrogate is refused.
  - One strict JSON object, at most 8 levels deep.
  - Duplicate member names are refused at every level, compared after unescaping.
  - Content after the object is refused.
  - Lexically refused: comments, single quotes, unquoted names, BOM, leading zeros, hex/octal, a leading `+`, NaN/Infinity, empty or
    trailing commas, raw control characters, bad escapes, unpaired surrogates.
- Members checked:
  - `requestId`: a string of 8..64 characters from `[A-Za-z0-9._:-]`.
  - On `commands` and `memory-commit`, `expectedVersion` is a JSON integer in 0..2147483647.
  - On `memory-commit`, `expectedMemoryVersion` must also be in that range.
  - A create body must not carry `expectedVersion`.
  - Everything else (kind, args, picks, prices, rules) is left to the server.
- The body string is stored and returned unchanged, including whitespace, member order and the order of picks. It is never
  rebuilt or normalized.

**Record file**
- The format is closed: outer `{schema, sha256, record}` and inner `{endpoint, account, method, route, requestId, body}`.
- The stored requestId is cross-checked against the body.
- Every field is re-validated on load, and the file must re-encode byte for byte.
- An unknown schema is `OUTBOX_RECORD_UNSUPPORTED`. Anything else wrong (truncated, corrupt, unknown field, non-canonical, a
  directory at the path, over 1 MiB) is `OUTBOX_RECORD_CORRUPT`.
- A different endpoint or account is `OUTBOX_BINDING_MISMATCH`.
- In every refusal case the file is kept untouched: no reset, no quarantine, no delete.
- No credential, seed, RNG or Core state is ever written.

**Lock**
- Every Load/Stage/Confirm opens the permanent `<file>.lock` (`OpenOrCreate`, `ReadWrite`, `FileShare.None`) and calls `flock(fd, 6)`
  on that same descriptor through `DllImport("libc", SetLastError = true)`.
- The `SafeFileHandle` is add-ref'd around the native call. The FileStream, and with it the flock, is held until the whole operation
  ends and then disposed.
- `EINTR` (4) is retried at most 8 times. errno 35 on macOS or 11 on Linux means `OUTBOX_BUSY` immediately, with no wait or spin.
  Same-process `FileShare.None` contention (an `IOException`) is also `OUTBOX_BUSY`.
- Fail-closed cases, all before the record is read or touched:
  - a platform other than macOS or Linux, a missing libc or a missing entry point: `OUTBOX_UNSUPPORTED_PLATFORM`;
  - any other errno, an invalid descriptor, a directory at the lock path, or a missing directory or access failure:
    `OUTBOX_LOCK_FAILED`.
- The lock file is never deleted, replaced, truncated or written. `FileStream.Lock` is not used.

**Publish**
- A unique `CreateNew` temp file is written, then `Flush(true)`, then closed, then `File.Move(temp, path)` without overwrite. On
  failure the component deletes only its own temp file and throws `OUTBOX_WRITE_FAILED`.
- `Stage` decodes its own encoded bytes before publishing. If they do not read back as exactly the same request, nothing is written.
- Stray temp files are never read or promoted.

### Caller contract (for Root's transport/UI unit)

- Call `Stage` only for a fully built mutation that is being sent now. Unsent prepared intents, queries and real reservations/AP
  stay outside this component.
- Send `BodyUtf8()` to `Endpoint + Route` with the bearer token for `Account`. The token comes from the transport's own
  configuration; the outbox stores no credential.
- After a timeout, a disconnect, a 503 (including `STORE_COMMIT_UNKNOWN`), a restart or a lost focus, call `Load` and re-send the
  same record. Never mint a new requestId and never rebuild the body.
- Call `Confirm` only after validating a successful response to a send of exactly this request. A current-state GET is not an
  acknowledgement.
- After `OUTBOX_WRITE_FAILED` or `OUTBOX_CLEAR_FAILED`, call `Load` to learn the true state.

## Limits / honest gaps

- **Runtime NOT_RUN by the author.** There was no compile, no .NET run, no Mono run, no NUnit, no process probe and no HTTP probe.
  The source was written against the .NET Standard 2.1 / C# 9 surface and the Newtonsoft 13 `JsonTextReader` API, but it has not
  been compiled. Root compiles it, runs the real I/O, process, HTTP and Mono checks, and reviews it.
- **Tests:** the NUnit file is still empty. Cases to cover in the next call:
  - The exact body (UTF-8, ordered picks) survives into a new instance.
  - An equal restage changes nothing.
  - Conflict, other requestId, other account and other endpoint are refused with the bytes kept.
  - Stale or not-current `Confirm` is refused.
  - Truncation, bit flips, unknown fields and bad bindings, each with a recomputed checksum, are refused and kept.
  - A stray temp file is ignored.
  - A held lock gives `OUTBOX_BUSY` (in-process through `FileShare`; cross-process `flock` needs Root's process probe).
  - An occupied record or lock path, and a missing directory, fail without false success.
  - The strict JSON and server-limit boundaries hold.
  - Two instances racing on threads never overwrite or stale-clear each other.
  - Not testable without injection or permission changes: an unlink or rename that fails itself.
- **Platforms:**
  - Only macOS and Linux libc `flock` are attempted; the macOS path is the one Root measured.
  - Windows is refused (`OUTBOX_UNSUPPORTED_PLATFORM`). Linux is untested; .NET may need `libc.so.6` resolution there, and a load
    failure fails closed.
  - No Android, IL2CPP or Windows acceptance is claimed. On Android, Mono may report Linux, so that path is not accepted.
  - Network file systems without `flock` are not supported.
- **Atomicity:** `File.Move` without overwrite is check-then-rename in current .NET and in Mono. It is safe only because every
  writer holds the lock; a non-cooperating writer is out of scope.
- **Durability:** a crash of this process is covered (the record is absent or complete). Power loss is not: the directory is not
  fsynced. Temp files left by a crash stay in the directory; no cleanup or retention policy was approved.
- **Changes from the earlier draft:**
  - There is no `stageId`. Identity is endpoint, account, route and exact body, as specified. Re-staging an identical, already
    confirmed request is the same logical request (same requestId, same server receipt), so an older `Confirm` of an identical
    request clears it.
  - `Load` now takes the lock as specified, so it can return `OUTBOX_BUSY`.
  - Busy is reported immediately; the constructor has no wait parameter.
- **Open decision for the transport unit:** under this contract, a definitive server rejection (400/404/409/422/507) keeps the
  record pending, because only a validated success may `Confirm`. That blocks new requests until it is resolved. Resolving it
  (for example, an explicit user-visible discard based on the server's statement that nothing was committed) needs a reviewed
  decision. This component provides no drop and no re-ID.
- **File permissions:** the record and temp files get the process umask. .NET Standard 2.1 has no `UnixCreateMode`. No secret is
  stored.

Session: `82687d1f-cd97-479c-87fa-ed21193a6961`. Agents, Bash, MCP, images, commits and other models were not used.

## Delta — test call P1-PRACTICE-OUTBOX-TESTS-20261017 (xhigh, same session)

This call wrote `client/MemoryForgeUnity/Assets/Tests/PlayMode/PracticeCommandOutboxPlayModeTests.cs` and appended this section. The
source stayed frozen and unedited (Root compiled the exact version, SHA-256 `a0e8c50b…8f59`). This is an xhigh call; nothing here
claims to have run at Max. The earlier "Tests" bullet in the Limits section is replaced by the list below.

**Test properties:**
- Synchronous `[Test]` methods using only BCL and NUnit 3.5. No UnityEngine, TestTools, `[UnityTest]`, reflection or product hook.
- Each test gets its own fresh temp folder (chmod 0700). TearDown restores the test's own modes and deletes only that folder.
- Two libc imports are used only on the test's own paths: `chmod`, and `geteuid` for an explicit "do not run as root" assertion. No
  global permission change.
- Damaged records are written as raw bytes. Well-formed edits come from an independent test-side encoder of the documented format,
  which the test first proves byte-identical to the store's own file.
- Every refusal check confirms that Load, Stage of the same request, Stage of another request, and Confirm all refuse. It then checks
  the file is byte-identical and no temp file remains.

**Tests:**
1. `ExactPrettyUnicodeBodySurvivesInNewObjectsAndCopiesAreDetached` — the exact body survives into new objects:
   - Pretty CRLF/tab whitespace, member order, escapes, 2/3/4-byte UTF-8 and U+FFFD, and the ordered picks are kept byte for byte.
   - All properties match, and mutating a `BodyUtf8` copy changes nothing.
   - The file equals the independent canonical encoding.
   - Create (null `ExpectedVersion`) and memory-commit records also survive.
2. `EqualRestageKeepsTheFileAndConflictingRequestsAreRefusedWithTheOriginalBytes` — an equal restage leaves the file unchanged.
   `REQUEST_ID_REUSED` for the same requestId with reordered picks, another version, one extra whitespace byte, or another run route.
   `PENDING_OTHER` for a different command, new run or save.
3. `AnotherEndpointOrAccountNeverSeesReplacesOrClearsTheRecord` — `BINDING_MISMATCH` for Load, Stage and Confirm; bytes kept.
4. `AStaleConfirmationNeverClearsALaterDifferentRequest`:
   - After A's request is cleared and a different B is staged, A's stale Confirm is `NOT_CURRENT` and B is kept.
   - An identical re-stage of an already confirmed request is the same logical request; the original identical expectation
     confirms it.
5. `TruncatedBitFlippedOrPaddedRecordsAreRefusedAndKeptWhole`:
   - Every truncation length, including empty, is refused.
   - Every byte flipped with 0x01, 0x20 and 0x80 is refused (`CORRUPT`, or `UNSUPPORTED` when the flip only changes the schema value).
   - Trailing space, newline, NUL, `x` or `{}`, a leading BOM, and garbage are all refused.
6. `WellFormedRecordsWithUnknownFieldsFormsOrBindingsAreRefusedAndKept` — 28 cases with recomputed checksums:
   - Inner fields: unknown, missing, duplicate, reordered.
   - Non-canonical forms: inner spacing, `POST`.
   - Outer envelope: unknown field, missing checksum, reordered fields, uppercase or mismatched checksum, leading whitespace, record
     given as an object.
   - Unknown schema (`UNSUPPORTED`).
   - Bad values: invalid account, invalid endpoint host or path, method `GET`, query route, a route that does not fit the body, a
     new-run route with a version body.
   - Protocol identity: a requestId that is not the body's.
   - Bad bodies: duplicate member, version out of range, trailing comma, empty.
   - Another valid account or endpoint is `BINDING_MISMATCH`, and that binding's own instance loads the record.
7. `AStrayTempFileIsNeverPromotedOrTouched`.
8. `AHeldPermanentLockIsBusyImmediatelyAndUsableAfterRelease`:
   - With the lock file held under `FileShare.None` in this process, Load, Stage and Confirm are `BUSY` at once (under 5 s).
   - After release, the lock is usable again.
   - The lock file's marker bytes are unchanged.
9. `OccupiedPathsAndAMissingFolderFailExplicitlyWithoutFalseSuccess`:
   - A directory at the lock path gives `LOCK_FAILED`.
   - A directory at the record path gives `RECORD_CORRUPT` for Load, Stage and Confirm.
   - A folder removed after construction gives `LOCK_FAILED` and is not recreated.
   - A missing folder at construction gives `INVALID_CONFIGURATION`.
10. `ReadOnlyOrUnreadableOwnFilesFailExplicitlyAndKeepTheRecord` (unprivileged user only):
    - A fresh 0500 folder gives `LOCK_FAILED` and creates nothing.
    - With the permanent lock already present and the folder at 0500, Stage gives `WRITE_FAILED` and Load stays null.
    - A pending record in a 0500 folder: Confirm gives `CLEAR_FAILED`, the record is kept, the same request restages without a write,
      and another request is `PENDING_OTHER`.
    - A record at mode 0000: Load, both Stages and Confirm give `READ_FAILED`.
    - Every mode is restored in `finally`, after which Load and Confirm work.
11. `OnlyTheThreeMutationsWithServerLimitedIdentityAreStaged`:
    - Routes: the three mutations are accepted; GET, `battle-query`, `memory-preview`, `account`, slash, query, fragment, case,
      length and traversal variants are refused, and the route helpers refuse bad run IDs.
    - requestId: 8 and 64 characters accepted; 7, 65, illegal characters, non-string and missing refused.
    - Versions: 0 and Int32Max accepted; -1, `1.0`, `1e2`, `"5"`, overflow, a big integer, null, bool and arrays refused, both for
      `expectedVersion` and `expectedMemoryVersion`. Missing versions are refused. A new run carrying `expectedVersion` is refused.
    - Size: exactly 65536 UTF-8 bytes accepted; +1 byte, multi-byte overflow, empty and a raw lone surrogate refused.
    - Depth: 8 accepted, 9 refused.
    - Null arguments throw.
    - Refused requests leave only the lock file behind.
12. `JsonExtensionsAndMalformedBodiesAreRefusedBeforeAnyFileIsTouched` — 56 bodies:
    - Comments, `#`, single quotes, unquoted names.
    - Trailing, leading, double and empty commas; missing value or comma.
    - Hex, octal and leading zeros; NaN and ±Infinity; `+`, `.5`, `1.`, `1e`, `-` alone; `undefined`, `True`, `nul`, `truex`,
      `new Date`.
    - A trailing value or extra brackets.
    - Duplicates: plain, escaped name and nested (object and array of objects).
    - Strings: raw tab/newline, bad escapes (`\x`, short `\u`, `\U`), unterminated strings and containers.
    - BOM, a top-level array, string, null or whitespace.
    - The folder stays empty, because validation runs before the lock.
13. `StandardJsonInOpaqueMembersIsAcceptedAndKeptExactly` — accepted and kept exactly:
    - Standard whitespace.
    - Literals and empty containers.
    - Signed, exponent and zero-exponent numbers.
    - All JSON escapes including `\u0000`.
    - Paired surrogate escapes (upper and lower case) and raw emoji.
    - Raw and escaped U+FFFD; literal backslash text `\\uD800` and `\\uDC00`.
    - Date-like text; an escaped but non-duplicate name.
14. `EscapedUnpairedSurrogatesAreRefused` — **RED regression on purpose:**
    - The independent review finding: an escaped lone `\uD800`, `\uDC00`, `\uDBFFA` or `\uDE00\uD83D` in a valid
      `play`/`picks` body on a valid `CommandRoute` must be `INVALID_REQUEST` with no record file.
    - The paired escape, a genuine U+FFFD and the literal `\\uD800` must stay valid.
    - The source is expected to FAIL this test until it is fixed.
15. `TheLargestRecordReloadsAndAnOversizedFileIsRefusedAndKept`:
    - A body of escaped backslashes at the 64 KiB limit makes a record over 3×64 KiB, still within 1 MiB, and it reloads.
    - A 1 MiB + 1 byte file is `RECORD_CORRUPT` and kept.
16. `InvalidConfigurationIsRefusedWithoutTouchingTheFolder`:
    - 18 bad endpoints are refused (host, scheme, port 0 and 65536, leading zero, path, query, fragment, credentials, IPv6, other
      loopback, sign, case, whitespace, empty).
    - 8 bad accounts, a relative path, a missing folder and a directory path are refused; null arguments throw.
    - The constructor creates nothing.
    - Boundary-valid values are accepted: port 1 and 65535, account length 2 and 32.
17. `SeparateInstancesOnThreadsNeverOverwriteOrStaleClear` — 25 rounds:
    - 4 instances race to Stage different requests behind a barrier.
    - Exactly one publishes. Each loser is `BUSY` or `PENDING_OTHER`, and the file holds the winner whole.
    - Then the winner's Confirm races another instance's Stage of a different request. A new request appears only after the winner
      was cleared; a Confirm that loses the race is `BUSY` and the winner stays whole. The confirmation never clears the newcomer.
    - Each round ends empty with no temp file.

**Defects and risks for Root's review:**
- **Known RED (test 14):**
  - The cause: `StringEnd` checks only the four hex digits, and Newtonsoft replaces escaped lone surrogates with U+FFFD before
    `Paired` runs.
  - Suggested narrow fix (not applied): in the lexical pass, require a `\uD800-DBFF` escape to be followed immediately by a
    `\uDC00-DFFF` escape, and refuse a low-surrogate escape that is not.
  - The same weakness also lets such escapes into a record's body. Decode reuses the same check, so the fix covers both paths.
- **Minor:** any `IOException` other than a sharing conflict while opening the lock file (missing folder, access and
  directory-at-path are already handled) is reported as `OUTBOX_BUSY`. It fails closed with nothing changed, but the code could
  mislead.
- **For the privacy probe:** the record, temp and lock files take the process umask (typically 0644). No secret is stored.
- **Test assumptions Root should confirm on both runtimes:**
  - The same-process `FileShare.None` hold is refused by .NET's internal flock and by Mono's share table.
  - EACCES on a 0500 folder, or on a 0000 file, surfaces as `UnauthorizedAccessException`/`IOException`.
  - libc `chmod` with an `int` mode marshals correctly to macOS's 16-bit `mode_t`.

**Not shown by these tests:**
- Two instances are not an OS process restart, and threads in one process are not interprocess `flock`. Root owns the process
  matrix and the client-restart probe.
- No HTTP, server receipt, SIGKILL or exactly-once AP/reservation behavior.
- No power loss, Android, IL2CPP or file-mode privacy.
- A failing rename inside `File.Move` itself has no ordinary trigger. A failing temp create (test 10) and a failing unlink (test 10)
  are covered.

**Author runtime:** NOT_RUN, meaning no compile, no .NET run and no Unity Mono run of the tests. Root compiles them against NUnit 3.5
on .NET 10 and runs them in the actual Editor Mono. Test 14 is expected to be RED against the frozen source.

## Delta — boundary fix P1-PRACTICE-OUTBOX-BOUNDARY-FIX-20261017 (2026-10-17, xhigh, same session)

**Context reported by Root (Root's runs, not mine):** source A0 compiled cleanly. On .NET 10, NUnit passed 16 of 17; only
`EscapedUnpairedSurrogatesAreRefused` failed, as intended (`root_nunit_red.txt`). Root also found the published record had mode 0644.
Root's process, HTTP, lock, write-failure and Linux runs passed, but they do not cover the two fixes below. The earlier A0 history
above stays as it was. The NUnit file (`c1ec620f…bec9`) was not edited, and no other file was touched.

Only `PracticeCommandOutbox.cs` changed. Public API, record format, identity, lock protocol, grammar, size and depth limits,
duplicate and canonical checks, and exact-body handling are all unchanged.

**1. Surrogate pairing is checked on the original text (`StringEnd`).**
- **Rule:** while scanning each JSON string, `StringEnd` decodes the UTF-16 unit of every raw character and every escape. A high
  surrogate must be followed immediately by a low surrogate. A low surrogate without a preceding high one, a high one followed by
  anything else, or a high one at the closing quote is a `FormatException` ("unpaired surrogate in a JSON string").
- **Why here:** this runs before Newtonsoft, which turns escaped lone surrogates into U+FFFD. `Paired` stays as a second check.
- **Still valid:** the pair `😀` (either case), raw emoji, a genuine U+FFFD (raw or `�`), and literal backslash text
  such as `\\uD800`. Nothing is blocked by substring and U+FFFD is not refused.
- **Refused:** `\uD800`, `\uDC00`, `\uDBFFA`, `\uDE00\uD83D`.
- **Effect on Stage:** `INVALID_REQUEST`, nothing written.
- **Effect on records:** Decode runs the same lexical pass on the outer envelope, the inner record and the body. An already-persisted
  record carrying such an escape now fails Load, Stage and Confirm with `OUTBOX_RECORD_CORRUPT`, and its bytes are kept.

**2. The new temp file is made owner-only before any byte is written (`Publish` → `RestrictToOwner`).**
- **Order:** create the temp file (`CreateNew`, `FileShare.None`), mark it created, call checked `fchmod(fd, 0x180u)` (POSIX 0600,
  written in hex because a C# `0600` literal would be decimal 600), then write, `Flush(true)`, and move without overwrite. The rename
  keeps the mode, so a newly published record is 0600.
- **Descriptor handling:** the descriptor is kept alive with `DangerousAddRef`/`DangerousRelease` around the native call.
- **Failure handling:**
  - EINTR (4) is retried up to 8 times.
  - Any other errno, an unusable descriptor, or a missing libc/`fchmod` (`DllNotFound`, `EntryPointNotFound`, `MarshalDirective`)
    raises an `IOException`.
  - `Publish` then deletes only its own temp file, which is still empty, and throws `OUTBOX_WRITE_FAILED`. Nothing is returned to send.
- **Untouched:** the process umask, existing pending or corrupt records, and the lock file keep their modes; nothing is chmodded,
  reset or migrated.
- **Platform:** `fchmod` is imported from libc like `flock`, and `Lock()` already restricts operations to macOS and Linux.

**Limits and caller notes:**
- **Brief visibility window:** between `CreateNew` and `fchmod`, the new temp file exists, still empty, with the umask mode (typically
  0644). A process of another user that opens it in that instant would keep its descriptor and could read the bytes written
  afterwards, because permissions are checked at open time.
  - Fully closing that window needs create-with-mode (`open` with mode 0600, or `UnixCreateMode`). The .NET Standard 2.1 BCL cannot do
    this, so it was left as is rather than adding a native open path.
  - Mitigations: the temp name has 128 random bits, and the store directory is trusted Development configuration that should itself be
    owner-only.
- **Lock file mode:** it still follows the umask. It holds no data.
- **Older records:** records written before this change (0644) are neither re-moded nor rewritten. The next Stage after a Confirm
  writes a 0600 record.
- **Unchanged open decisions:** a definitive server rejection still leaves the request pending; there is no drop and no re-ID.
  Transport and UI belong to the next scope.

**Author runtime:** NOT_RUN — no compile, no NUnit, no Mono and no mode probe in this call.

Expected results, all for Root to verify:
- `EscapedUnpairedSurrogatesAreRefused` RED turns GREEN.
- The other 16 tests stay GREEN.
- A new record is 0600 on macOS and Linux, and the source sets the mode before `Write`.
- The full Mono run of all 17, Play, an independent re-review and a hash-matched integration are still required.

## Delta — race gate P1-PRACTICE-OUTBOX-RACE-GATE-20261017 (2026-10-17, high, same session)

This is a test-only change. The source was not edited (SHA-256 `4a62c02d…41c1`).

- **Problem (found in an independent review):** the `Race` helper ignored the `false` that `Barrier.SignalAndWait(10 s)` returns on a
  timeout. On a timeout the callbacks would then run one after another instead of concurrently, and could falsely pass the "exactly
  one winner, the rest `PENDING_OTHER`" assertions.
- **Fix:** a timeout now throws
  `TimeoutException("Outbox race participants did not reach the start barrier.")`. The existing `catch` records that exception as the
  participant's outcome, and the unchanged outcome assertions then fail.
- **Unchanged:** all 17 tests and their expected values, the Unicode RED regression and valid positives, the owned-permission tests,
  the timeouts, the barrier itself, and the API. No assertion was weakened and nothing was skipped.
- **Author runtime:** NOT_RUN. Root rebuilds and runs the same tests on .NET and Mono.
