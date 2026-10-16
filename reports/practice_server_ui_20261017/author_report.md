# P1-PRACTICE-SERVER-UI-20261017 — UI author report (Claude Code, Opus 5.5, requested effort high)

**AUTHOR RUNTIME: NOT_RUN.** Nothing was compiled or run by the author: no Unity Editor, Play mode, pointer test, HTTP run or Console
check in this call. Root owns the Editor, the new scene/wiring, the real Unity Mono HTTP and pointer flow, and the independent review.
This is no Android or physical-device evidence, no production authentication, and not a full network game.

session_id: the same Root-invoked session; not independently visible to the author.

## Files

| File | Change |
|---|---|
| `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeServerPracticeClient.cs` | new: a Development-only, optional server practice view (one MonoBehaviour) |
| `reports/practice_transport_20261017/ui_author_report.md` | this report |

Nothing else changed: the original local expedition client, scenes, prefabs, transport, DTO and tests are untouched. There is no
`[ExecuteAlways]`, no serialized field, no endpoint/credential/settings file and no server start.

## API (for Root)

**Wiring**

- `bool Connect(Func<PracticeServerSession> factory)`: works only at runtime, in the Editor or a development build. It disposes any old
  session, keeps the factory, creates a session and queues **one refresh**. Nothing is created, POSTed or retried. Root injects the test
  factory and, if it wants, disables the original local client itself.

**Read-only state**

| Property | Meaning |
|---|---|
| `Configured`, `Connected`, `Busy`, `CanCommand` | connection and operation state |
| `SessionState` | the session's immutable state |
| `Snapshot` | the session's current snapshot, else the last good one |
| `SnapshotStale` | the drawn snapshot may be behind |
| `Notice` | the Korean notice line |
| `SelectedCard`, `SelectedMode`, `Target` | the local selection |
| `QueryView` | the battle query answer, only when it matches the current selection and context |
| `PickerOpen`, `PickerPicks`, `PickerOptions` | the pick sheet; options are exactly the server's current candidates |
| `HasPreparedIntent`, `PreparedCard`, `PreparedMode`, `PreparedTarget` | the one unsent prepared play |
| `HasPresentation`, `PresentationBarrier`, `PresentationRows`, `PresentationShown` | the action presentation |

**Actions** (each button calls one)

| Method | Effect |
|---|---|
| `Task<bool> RefreshAsync()` | re-reads the server state |
| `RetryAsync()` | the explicit exact retry of the unresolved request |
| `CreateRunAsync()` | new run; offered only without a run or after DONE |
| `EnterNodeAsync(nodeId)` | `enter`; only for a public `available` node |
| `bool SelectCard(uid)`, `SelectMode(mode)`, `SelectEnemy(uid)` | local selection; each starts a fresh pure query |
| `Task<bool> ConfirmAsync()` | opens the server pick sheet, prepares one unsent play, or sends one `play` |
| `PickCard(uid)` | adds one of the server's candidates, then re-queries |
| `ConfirmPicksAsync()` | sends the play once the server sheet is complete |
| `AutoPickAsync()` | sends the play without picks (Core's existing auto) |
| `CancelPick()`, `CancelPrepared()` | drop unsent local choices |
| `EndTurnAsync()` | `end` |
| `SkipPresentation()`, `ContinuePresentation()` | text only; send nothing |

## What the slice does (read scope)

- **View:** its own `MemoryForgeServerPracticeView` canvas (sorting order 10) with the same uGUI construction, palette (`0d1322`…),
  1600×900 scaling and font (`Fonts/IBMPlexSansKR-Regular`) as `MemoryForgeExpeditionClient`. The helpers are copied; nothing existing was
  refactored.
- **Unconfigured:** only a small, non-blocking status "서버 연습 · 연결 안 됨 (개발용)". It reads no data, uses no secret and creates
  no EventSystem.
- **Header:** title, "서버 연습 · 개발용 · 연습 계정", a status line from the session state, Refresh, and either "같은 요청 다시 보내기"
  (when a request is pending) or "새 서버 탐사" (no run, or DONE).
  - Status lines cover: disconnected, outbox unknown, in flight, unresolved request, needs refresh, synced.
  - Player text never shows protocol IDs, JSON, codes or exception text. Server and session codes become fixed Korean notices (Core rule
    codes through the existing `MemoryForge/error-messages` resource).
- **Map:** the public `state.map` floors and boss, the current `position`, and only the `available` nodes enabled.
- **Battle:**
  - **Party:** HP, block, turn, AP, the four pile **counts** and actor names.
  - **Enemies:** HP, block, alive, and the current and next intent as published. Living enemies are clickable as targets.
  - **Reservation board:** the server's `slots.next/after` `due/used/cap/sealed`, plus every public reservation (name and due turn).
  - **Hand:** content display name, plus the server's `cost`.
  - **Action panel:**
    - the selected card's effective name and text from the battle query's `source`;
    - the server's `modes`, `costs[mode]` and `blocked[mode]`, labelled "기본 요청" because they describe the default request only and
      are never enforced;
    - the forecast line from the query (`now` = 예상; next/after = 현재 상태 기준 추정; hidden results stay hidden);
    - Confirm and End.
- **Other run phases** (reward/rest/forge/shop/event/archive/save): a public HP/coins line and a clear notice that this step is a later
  unit and the original local expedition UI remains.

## Boundaries as implemented

- **No game logic in the client.** No `Run`/`BattleState` is built and nothing is seeded. Costs, legality, slots, picks, targets and
  damage are never computed. `BattleTurnDigest` and `BattleEvent` are reused only as plain public DTO text: `ToEvent` maps the
  `PublicView` event member names 1:1. Intent text shows the published base values ("기본 피해"), because the server does not publish a
  final intent damage.
- **Targets:** `target` is the clicked live enemy's uid and is sent only when the card's public `needsTarget` is set. With exactly one
  living enemy and no click, no target is sent; Core's own existing rule then takes that enemy, and the label shows it. With several living
  enemies, Confirm asks for a click. "auto" is never sent, and a dead target is cleared after each server read rather than replaced.
- **Queries:**
  - A query starts after every selection change and every server read, with that exact uid, mode, target and ordered picks. It carries no
    requestId and writes nothing.
  - An answer is applied only when its generation matches and `session.IsCurrent(result)` holds; otherwise it is discarded.
  - Confirm needs the current answer. Its `forecast.error` (the server's verdict on that exact request) is shown and the play is not sent.
- **Pick sheet:** opens only when the server's `choices.requests` ask for picks.
  - It is modal (input blocker) and offers exactly the server's `choices.candidates`. Its progress and request label come from the pure
    query after each pick.
  - "이대로 사용" is enabled only when the server reports every request filled with no forecast error. "자동 선택" sends no picks
    (Core's existing auto). "취소" drops it unsent.
  - It closes when the context changes.
- **One UI operation at a time:** further operations wait or are refused with a notice. Every async path is `async Task` behind
  `Launch`/`Guard`/`Operate`, catches into a fixed Korean notice and runs its continuations on the Unity main thread (no
  `ConfigureAwait(false)` in this component). There is no `async void`.
- **Lifecycle:** disable, focus loss and pause drop the unsent selection, pick sheet, prepared play and presentation, then dispose the
  session; the outbox record of an unresolved request stays. Enable, focus or resume calls the factory and **refreshes only**. The exact
  retry is the explicit button. A late reply from a disposed session is ignored (session identity check).
- **Stale state:** the last good snapshot stays drawn, with the status in red, while there is no session, a refresh failed, a request is
  unresolved or the outbox is unreadable. New commands need `SessionState.CanMutate`, plus no local sheet, prepared play or presentation.
- **Presentation (local prototype timing, not a measurement):** shown only from a Committed result's `PresentableEvents()` together with
  the snapshot shown before the command. An old receipt yields null and nothing is replayed.
  - **Ordinary play:** an inline text strip of the event rows only, revealed at 0.2 s per row (0.3 s for a reservation resolution row,
    the original value), held 0.6 s, then closed. Skip closes it. The next selection and query stay open, and **at most one** Confirm is
    frozen as an unsent prepared play (context, uid, mode, target).
  - **Dispatching the prepared play:** on the Update after the strip closes, it is sent only if the context is unchanged, the session can
    mutate, and the current query matches its uid/mode/target with no forecast error and no picks needed. Otherwise it is dropped with a
    notice. A play that needs picks cannot be prepared.
  - **End turn, a retry, or a battle-ending command:** a full barrier sheet. Every selection and command is locked; "모두 보기" reveals
    every row and "계속" closes it.
  - Skip and Continue never send anything.
- **Never serialized:** the factory, session, credential, outbox, bodies and snapshots are private, non-serialized fields.

## Input-gate fix (P1-SERVER-UI-INPUT-GATE-20261017; author runtime NOT_RUN)

Root's actual Editor run found a gap. While `client.EndTurnAsync()` was still in its owner preflight or POST (`Busy == true`),
`SelectCard(liveHandUid)` returned true. The old `SelectionLocked` ignored `busy` and the session's readiness, and it read the last good
snapshot, so a pending or stale hand could still be selected.

**The fix reuses existing predicates; there is no new policy or framework.**

- **`Ready()`** = `!busy && session != null && session.State.CanMutate`. `CanMutate` already covers: a current snapshot, nothing pending,
  a readable outbox, no refresh due, and no exchange in flight.
- **`SelectionLocked()`** = `!Ready()` OR barrier, pick sheet, prepared play, or no battle in the **session's own** snapshot.
  - Effect: `SelectCard`/`SelectMode`/`SelectEnemy` and the matching buttons refuse during any network operation (query, refresh, retry,
    command), while a request is pending or unknown, when the outbox fails, when the snapshot is stale, and when disconnected.
  - An approved ordinary presentation leaves `Ready()` true, so selection and **one** prepared confirmation stay allowed.
- **`ConfirmAsync`:** checks `Ready()` before it opens a pick sheet, prepares a play or sends.
- **`ConfirmPicksAsync` / `AutoPickAsync`:** check `Ready()` (and that no presentation is open) **before** clearing the sheet, so a
  refused send leaves the sheet open and usable. `PickCard` and `CancelPick` are unchanged.
- **`EndTurnAsync`:** also refuses while a prepared play is waiting. After a skip closes the presentation, the prepared play is dispatched
  on the next Update, and an end can no longer overtake it. It also needs `CanCommand` before `Mutate`/`Operate`.
- **`CreateRunAsync` / `EnterNodeAsync`:** need `CanCommand` before `Mutate`/`Operate`.
- **Unchanged:**
  - `DispatchPrepared` (it clears the waiting play, then checks `CanCommand`);
  - refresh and the explicit retry (not gated by `CanMutate`);
  - Core, target semantics, time reservations, presentation timings, request IDs and visuals.
- **A refused input** shows a fixed Korean notice: "이전 요청을 처리하는 중이에요." / "서버 연습에 연결되어 있지 않아요." /
  "서버 확인이 끝나지 않았어요. …".
- **Expect in pointer automation:** a selection made while that selection's own query is still in flight is now refused until the query
  returns.

## Remaining phases (not done here)

- Reward, rest, forge, shop, event, archive and the v2 memory-commit/save screens over the server. The transport's `CommitMemoryAsync`
  exists but this UI has no button for it.
- Ultimates (`ult`).
- Card detail sheets, the deck and log sheets, and pace (2× / reduced motion) controls.
- Map connector lines.
- The original's in-place label updates while a press is held. This view rebuilds its canvas at the start and end of each operation and
  query, so a press held across a rebuild can be dropped.
- Final intent damage (needs a server-published value).
- Production authentication, endpoint selection, and Android/device acceptance.

## Risks for Root's run

- **Focus and pause in the Editor** (clicking another window) dispose and recreate the session by design. Pointer automation should keep
  the Game view focused.
- **Field shapes are assumed from `PublicView`:** `needsTarget` (string or null), `blocked`/`costs` objects, and an intent `t`. Missing
  members show empty or "-", never invented numbers.
- **Run phase names** `MAP`/`BATTLE`/`DONE` are taken from Core/Program usage; another phase shows the later-unit notice.
