# P1-CARD-FLOW-FAIL-CLOSED — 작성자 보고 (source only)

- 작성: Claude Opus 5.5(`claude-opus-5-5`), 요청 노력 xhigh, 2026-10-16. 실제 노력과 session_id는 Codex 회수 메타데이터를 따른다.
- 변경 파일(허용 3개만): `client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs`, `client/MemoryForgeUnity/Assets/Tests/PlayMode/ExpeditionFailurePlayModeTests.cs`, 이 보고서. Core·digest·기존 시험·씬·리소스·설정·문서는 바꾸지 않았다.
- **검증: 컴파일·Unity Edit/PlayMode·스크린샷 모두 NOT_RUN**(셸·Editor 없음). 정적 대조만 했다: Core의 모든 `RuleException` 발생 지점과 순서(Run.Enter/StartBattle, Battle precheck, Rewards/Services/Memory), `RunState.Copy/ToJsonGraph`, `MemoryJson.Copy`(유한 double 허용), `CanonicalState`(부동소수 거절), A의 `enemy.Pattern ?? data` 경로.

## 수용 경계

- 대상: `Do`를 지나는 **모든 Run 변경 명령**이다. `EnterNode`, 보상·휴식·공방·상점·사건·기록소·부품 적용, `Assign`, `CommitMemory`, 그리고 `SendBattle`의 play·ult·end.
  - 대상이 아닌 것: 순수 질의(예상·검사·미리보기·카드 설명)와 `Depart`의 `Run.Create`. 후자는 성공했을 때만 대입되는 원자적 생성이다.
- 이것은 **로컬 연습 격리**다. 서버 재동기화, 새 엔진 트랜잭션, Core 변경이 아니다. AP·RNG·재지정·pick·저장 의미와 원래 수치는 그대로이며, 참고 묶음의 부분 이익 정책은 들여오지 않았다.

## Main 구현

- **비교 방식:** 명령 직전에 분리 사본 `run.State`와 `Copy(store)`를 잡는다. 예외가 나면 `ToJsonGraph()` 트리와 store를 `JToken.DeepEquals`로 비교한다.
  - `CanonicalState.Hash`는 합법적인 불투명 소수 메타데이터(예: difficulty 필드)를 거절하므로 쓰지 않는다.
  - 성공 경로 비용을 줄이려고 JSON 변환은 실패했을 때만 한다. 그래프는 분리 사본의 순수 투영이므로 비교 결과는 같다.
  - 사본을 만들 수 없으면 명령을 실행하지 않고 `SNAPSHOT_FAILED`로 격리한다. 비교 자체가 실패하면 "바뀜"으로 본다.
- **허용 목록 `ExpectedRejections`:** 변경 전 검사로 던지는 일반 입력 거절 37개다.
  - 지도·전투: `NODE_UNREACHABLE, NO_BATTLE, WRONG_PHASE, INVALID_MODE, NOT_IN_HAND, RESERVE_ONLY, NOT_RESERVABLE, AP_LOW, BAD_TARGET, FIXED_MARK_REQUIRED, SLOT_FULL, DEBT_CAP, LOAN_OVERFLOW, NOT_DEFERRABLE, RESTRAINT_UNAVAILABLE, NO_EMPTY_SLOT, RESERVATION_REQUIRED, PULL_DESTINATION_FULL, OVERLOAD_WOULD_KILL, UNKNOWN_ACTOR, ULT_NOT_READY`
  - 보상·서비스: `BAD_REWARD, BAD_EVOLUTION, NO_PART, BAD_CARD, BAD_PART, BAD_UPGRADE, BAD_CHOICE, CANNOT_HEAL, NOT_FOR_SALE, COINS_LOW, WOULD_KILL, BAD_ASSIGN, REASSIGNMENT_REQUIRES_SEPARATE_FLOW`
  - 기억 선택: `INVALID_SELECTION, DEFEAT_KEEP_ONE, OVER_CAPACITY`
  - **코드가 목록에 있고 Run·store가 그대로일 때만** 기존 토스트를 띄우고 Console 오류는 남기지 않는다.
  - 의도적으로 뺀 것(바뀐 것이 없어도 격리): `*_NOT_IMPLEMENTED`, 불변식·범위, `UNKNOWN_*`(행동 주체 제외), `UNSUPPORTED_*`, `INVALID_COMMAND_ARGS`/요청값, `INVALID_SNAPSHOT`, `VERSION_CONFLICT/REQUEST_CONFLICT/RUN_MISMATCH`, `STALE_VERSION/IDEMPOTENCY_CONFLICT`, RNG·`ZONE_NOT_AUTHORED`, 비RuleException, `MemoryBoundaryException`.
  - `MemoryBoundaryException`은 기존에 INTERNAL 토스트였다.
  - `INVALID_PART`도 뺐다. 기억 store 손상을 알릴 때도 같은 코드를 쓰기 때문이다.
- **격리(`Quarantine`):**
  - 실패 사유(Core 코드 또는 예외 형식)를 기록하고, store를 명령 전 사본으로 되돌린다.
  - 화면 의도를 지운다: picker, 선택 카드·방식·대상, 시트, deck pick, 기억 변화 선택, 카드 설명, 턴 요약.
  - `Debug.LogError`를 1회 남긴다: `[MemoryForge] Practice expedition quarantined after a failed local Run calculation (<사유>); …` + 예외.
  - 재시도·환불·자동 진행·자동 저장·서버 조회는 하지 않는다.
- **상태 노출:** `RecoveryRequired`(읽기 전용).
  - `Blocked()`가 복구 상태를 먼저 확인하므로 S1 게이트를 쓰는 모든 공개 진입점이 `RECOVERY_REQUIRED`로 거절된다. 선택, `StartPractice`, `Depart`, `CommitMemory`, `CloseWithoutSave`, `Abandon`, `ToggleChange`, `Assign`이 포함되고, 모든 `Do` 명령도 공통 게이트로 막힌다.
  - 실패한 Run을 읽는 표시 API(`Show*Info`, `PlayForecast`, `MemoryChanges`, `PreviewSelection`)는 false/null을 돌려준다.
  - `Snapshot`, `SessionMemory`, 버전은 계속 읽을 수 있다.
- **복구 화면:** `Render`가 Header나 일반 화면보다 **먼저** 분기해 `RecoveryView`만 그린다. 실패한 Run은 읽지 않고 store 버전만 쓴다.
  - 제목 "이 연습 탐사를 계속할 수 없어요"
  - 본문 "…이 탐사의 진행과 보상은 저장하지 않고 버려요." / "마지막으로 저장한 기억 덱(기억 버전 N)은 그대로이고, 새 탐사는 그 덱으로 시작해요."
  - 안내 "연습 기억은 이 앱 세션에만 있어요. 앱을 닫으면 사라져요."
  - 버튼은 `RecoveryDiscard` "이 탐사를 버리고 새로 출발" 하나뿐이다.
  - 격리는 OnDisable/OnEnable과 focus/pause resume에서도 유지된다. resume은 화면이 없을 때만 다시 그린다.
- **복구 동작 `DiscardFailedPractice()`:**
  - 실패한 연습 Run 참조만 버린다. Core Abandon·CommitMemory·Close는 부르지 않고, SaveOpen으로 보지 않으며, 부분 보상을 표시하지 않는다.
  - 그다음 기존 `Depart`로 마지막 유효 store에서 출발한다. memory·version·receipts/history와 출발 번호가 이어진다. `StartPractice`는 부르지 않는다.
  - `LastRequestId`와 직전 결과 화면의 `lastSave`는 기존처럼 출발할 때 초기화된다. 저장 영수증 자체는 store에 남는다.
  - 새 출발이 실패하면 격리를 유지하고 오류 문구를 표시한다.

## 시험 (ExpeditionFailurePlayModeTests, 5개, NOT_RUN)

1. **실제 부분 변경 회귀.**
   - 첫 전투에서 턴 종료만 반복해 패배하고 변화 없이 커밋해 버전 1을 실제로 얻는다. 200회 안에 패배하지 않으면 Inconclusive.
   - 다음 출발(`practice-2`) 뒤 시험 전용 결함을 넣는다: 클라이언트 자신의 콘텐츠에서 해당 구역 normal 조우를 모르는 적으로 바꾼다.
   - 실제 `EnterNode` 결과: 위치가 바뀌고 RNG가 소비됐지만 전투는 없고 MAP 상태다. 격리되고 `UNKNOWN_ENEMY` 진단이 남으며 store·버전은 그대로다.
   - 복구 화면: 버튼 1개, 문구 판독, 실패한 Run 정보 미표시.
   - 공개 명령 29개와 선택 5개, 읽기 API가 모두 거절되고 Run·store가 그대로다.
   - disable/enable과 resume에도 격리가 유지된다.
   - 결함을 제거한 뒤 실제 클릭 → `practice-3`이 저장 store와 버전 1에서 `Create`한 결과와 정확히 같다. 첫 노드로 실제 전투에 들어간다.
2. **일반 거절은 그대로 쓸 수 있다.** NODE_UNREACHABLE, NOT_IN_HAND, ULT_NOT_READY, BAD_REWARD, WRONG_PHASE는 격리·오류 로그 없이 거절되고 Run이 그대로다. BAD_TARGET은 살아 있는 적이 2체 이상이고 대상을 고르지 않은 경우에만 확인한다. 이후 턴 종료는 정상이다.
3. **알 수 없는 전투 오류.** 시험 전용으로 이번 전투 적들의 행동 형식을 모르는 값으로 바꾼다. 실제 `end` → `BATTLE_ENEMY_MOVE_NOT_IMPLEMENTED`. Battle이 원자적으로 롤백해 Run이 그대로여도 격리되고 턴 요약은 없다. 결함 제거 후 복구 → `practice-2`, 버전 0.
4. **private `Do` 리플렉션 seam**(출고 코드에 시험용 진입점 없음):
   - 바뀐 것 없는 NOT_IN_HAND → 토스트, 로그 없음.
   - InvalidOperationException → 격리.
   - private `run`으로 실제 `Enter` 뒤 NOT_IN_HAND → 바뀐 것이 감지돼 격리.
   - 매번 복구한 뒤 출발 번호가 이어진다.
5. **소수 메타데이터 비교.** difficulty 불투명 필드 0.1을 가진 실제 Run에서 `CanonicalState.Hash`는 ArgumentException을 던지지만, private `SameRun`은 같은 값을 같다고 보고 1 ulp 차이는 다르다고 본다.

기존 시험은 바꾸지 않았다. 기존 시험이 기대하는 코드(NODE_UNREACHABLE, ULT_NOT_READY, NOT_IN_HAND, BAD_REWARD, 저장 거절 OVER_CAPACITY/DEFEAT_KEEP_ONE, S1 장벽 코드)는 모두 허용 목록에 있거나 `Do` 밖의 로컬 거절이다.

## 남은 위험

- **`Do` 밖의 예외:** 성공한 명령 뒤의 표시 코드(digest·Render 안의 질의)와 `ConfirmCard`의 순수 질의는 `Do` 밖이다. 거기서 비RuleException이 나면 격리 대신 기존처럼 예외가 올라간다.
- **허용 코드의 모호성:** `WRONG_PHASE`처럼 정상 거절과 내부 문제를 함께 뜻하는 허용 코드는 "변경 없음"일 때만 토스트가 된다. 코드 하나로 모든 원인을 구분하지는 못한다.
- **Console 로그:** 실제 결함이 나면 Console에 Error가 1회 남는다. 시험은 `LogAssert.Expect`로 이를 확인한다.
- **비용:** 명령마다 분리 사본 1회(RunState + store)가 든다.
- **시험 도달 여부:** 시험 1의 패배 도달은 확인하지 못했다(Inconclusive 가능). 시험 2의 BAD_TARGET은 조우 구성에 따라 생략된다.
- **범위:** 앱 세션 한정이다. 서버·계정·재접속·실기기는 미구현·NOT_RUN이다.

## Root 검증

1. Editor Test Runner PlayMode에서 `MemoryForge.Client.Tests.ExpeditionFailurePlayModeTests` → `ExpeditionTurnFlowPlayModeTests`, `ExpeditionClientPlayModeTests`, `BattleTurnDigestPlayModeTests` → 전체 PlayMode/EditMode 순으로 실행한다.
2. CLI 예:
   ```
   Unity -batchmode -projectPath client/MemoryForgeUnity -runTests -testPlatform PlayMode -testFilter "MemoryForge.Client.Tests.ExpeditionFailurePlayModeTests" -testResults <path>
   ```
3. Console에서 예상된 진단 외의 오류가 없는지, Inconclusive 사유를 확인한다.
4. 복구 화면 스크린샷(1600×900, 640×360, 글자 130%)과 버튼 대비를 확인한다.