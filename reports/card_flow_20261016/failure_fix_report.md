# P1-CARD-FLOW-FAILURE-RESTART-FIX — 수정 보고 (source only)

- 작성: Claude Opus 5.5(`claude-opus-5-5`), 요청 노력 high, 2026-10-16. session_id·실제 노력은 Codex 회수 메타데이터를 따른다.
- 변경: `MemoryForgeExpeditionClient.cs`의 `DiscardFailedPractice`, `ExpeditionFailurePlayModeTests.cs`, 이 보고서. 원래 작성자 보고(`failure_author_report.md`)는 이력으로 두고 손대지 않았다. Core·씬·설정·다른 시험은 바꾸지 않았다.
- **검증: 컴파일·Unity 시험 NOT_RUN.** 근거는 Root의 실제 receipt `s2_restart_failure_red.json`(quarantinedBefore true → 재시작 NullReferenceException 발생 → quarantinedAfter false, run 없음)과 소스 판독이다.

## 원인

`DiscardFailedPractice`가 `Depart` 전에 `run`과 `recovery`를 비웠다. 그래서 `Run.Create`의 비RuleException이 그대로 밖으로 빠져나가 격리가 해제되고 Run이 사라졌다. 이후 `OnEnable`이 `StartPractice`를 불러 세션 기억을 초기화할 수 있었다. `Depart`의 `data/store == null` 경로도 `StartPractice`로 넘어가 기억을 지울 수 있었다. 앞선 보고서의 "새 출발 실패 시 격리 유지"는 RuleException에만 맞는 말이었다.

## 수정 (좁게)

- **사전 확인:** `data` 또는 `store`가 없으면 재시도하지 않는다. 격리를 유지하고 안내 문구만 표시한다. `Depart`의 `StartPractice` 대체 경로로 내려가지 않는다.
- **원상 복구:**
  - 재시도 전에 실패한 Run, 격리 사유, `closedKind`, store 참조, `departures`를 보관한다.
  - `Depart`를 try/catch로 감싼다.
  - 거절(RuleException → 기존 Reject)이든 예외든 `ok && run != null`이 아니면 모두 원상 복구하고 다시 그린다.
- **결과 범위:**
  - 성공한 재시도만 출발 번호를 1 올린다.
  - 저장·Core abandon/close는 부르지 않는다.
  - 예외가 나면 `Debug.LogError`를 재시도 1회당 1번 남기고, 복구 화면에 "새 탐사를 시작하지 못했어요…"를 띄운다.
  - 차단된 일반 명령은 여전히 로그를 남기지 않는다.
  - 화면은 읽기 전용 복구 화면 그대로다.

## 시험 변경 (ExpeditionFailurePlayModeTests)

- **`EarnSave`:** `Assert.Inconclusive`를 실제 실패 단정(`Phase == DEFEAT`)으로 바꿨다. 고정 seed는 수용 증거다.
- **일반 거절 시험:** BAD_TARGET을 필수로 만들었다. 다음을 모두 단정한 뒤 실행한다.
  - 살아 있는 적이 2체 이상이다.
  - 대상이 선택되지 않았다.
  - mirror가 view의 Run과 정확히 같다.
  - 대상 하나가 필요한 합법 카드가 손패에 있다.
  - 기존 판정은 모두 유지했다.
- **새 시험 `FailedRecoveryRetriesKeepTheQuarantineAndTheEarnedSaveUntilARepairedRetryDepartsOnce`:**
  1. 버전 1을 실제로 얻은 뒤, 실제 부분 Enter로 격리한다.
  2. 시험 전용 `data.Memory = null`로 실제 `Run.Create` 예외를 일으킨다. 실제 클릭과 공개 호출 각 1회 모두 예외가 밖으로 나가지 않는다. 격리·실패한 Run·store/버전이 유지되고 새 Run은 없다. 진단은 1회씩이다.
  3. 화면은 버튼 1개, 모든 공개 명령이 거절되며 그때 로그가 없다.
  4. disable/enable과 resume 뒤에도 같다.
  5. private `data`/`store`를 리플렉션으로 null로 만들면 재시도가 거절된다. 격리가 유지되고 store는 초기화되지 않는다.
  6. 내용을 복구한 뒤 실제 클릭 → `practice-3`. 저장 store와 버전 1에서 `Create`한 결과와 정확히 같다. 실패한 재시도가 번호를 소비하지 않았고 store도 그대로다.
- 기대 Run/store를 주입하지 않았고 출고 코드에 시험용 진입점도 없다. 리플렉션은 시험 쪽 seam으로만 쓴다.

## 남은 위험

- 재시도 중 `Depart` 안의 `Render`가 예외를 내면 새 Run을 버리고 실패로 처리한다. fail-closed 쪽 선택이다.
- 시험 1과 새 시험의 패배 도달은 내가 실행해 확인하지 않았다. 이제 도달하지 못하면 FAIL로 나타난다.

## Root 검증

Editor PlayMode에서 `MemoryForge.Client.Tests.ExpeditionFailurePlayModeTests`(6개)를 실행한 뒤 기존 Expedition/digest 시험과 전체 PlayMode/EditMode를 실행한다. Console에서 예상된 격리·재시작 진단 외의 오류가 없는지 확인한다.