# P1-CARD-FLOW-UNKNOWN-STORE-REVIEW — 반대 작성자 독립 검토

- 검토자: Claude Opus 5.5(`claude-opus-5-5`), 요청 노력 high, 2026-10-16. session_id는 Codex 회수 메타데이터를 따른다.
- 범위: Root의 두 변경. 대상 위치는 `MemoryForgeExpeditionClient.RecoveryView` 778행과 `ExpeditionFailurePlayModeTests.FailedRecoveryRetries…` 228–229행이다.
- 이 파일 외에는 읽기만 했다. **실행 NOT_RUN.** Root의 실제 Unity 결과 receipt를 보기 전에는 PASS라고 말하지 않는다.

## 판정: **APPROVE**

### 1. RecoveryView 문구 (Main)

- **store가 있을 때:** 문자열·위치·크기·색이 이전과 같다. "마지막으로 저장한 기억 덱(기억 버전 N)…" 그대로이며, 기존 첫 시험의 "기억 버전 1" 판정도 그대로 성립한다.
- **store가 null일 때:** 이전에는 `SessionMemoryVersion`이 null을 0으로 바꿔 "기억 버전 0"을 표시했다. 이는 저장소가 없는데 "알려진 버전 0"으로 보이는 잘못된 표시였다. 새 문구 "저장된 기억을 확인할 수 없어 새로 출발할 수 없어요."는 실제 동작과 맞는다. `DiscardFailedPractice`의 사전 확인이 재시도를 거절하고 `StartPractice` 대체 경로로 가지 않기 때문이다.
- **영향 범위:** 표시 전용 삼항식 하나다. Core, 권위, 저장, 격리 진입·해제, `Blocked` 게이트, `DiscardFailedPractice` 경로는 바뀌지 않았다. 복구 화면은 여전히 실패한 Run을 읽지 않고 버튼은 하나다. fail-closed 의미는 유지된다.
- **사소한 점(차단 아님):** store가 없을 때도 "이 탐사를 버리고 새로 출발" 버튼은 남는다. 누르면 안전하게 거절되고 아무것도 초기화하지 않으며, store를 쓸 수 있게 되면 다시 시도할 수 있다. 따라서 문제는 아니다.

### 2. 시험 판정 (FailureTests)

- **추가 위치:** store-null 시도 직후, store 참조를 복구하기 **전**에 두 판정을 더했다.
  - 화면 어디에도 "기억 버전" 문구가 없다. `DiscardFailedPractice`의 오류 notice("기억 저장소를 확인할 수 없어 새로 출발하지 않았어요…")에도 이 말은 없다.
  - unavailable 문구가 그릴 수 있고 잘리지 않는다.
- **판정이 결정적인 이유:** 이 시점에서 `DiscardFailedPractice`의 store-null 경로가 `Render()`를 부른다. `data`는 바로 앞에서 복구됐으므로 전체가 다시 그려지고, 마지막의 `Canvas.ForceUpdateCanvases()`로 글자 생성기도 준비된다. `SingleView`는 활성 화면만 세므로, 같은 프레임에 비활성으로 남은 이전 캔버스는 영향을 주지 않는다.
- **기존 판정 유지:** 격리, 실패한 Run 유지, store null 유지, `StillQuarantined`, 전체 store 비교, mirror-equivalent 재출발, 출발 번호 판정은 모두 그대로다. 삭제되거나 완화된 것이 없고 판정만 더해졌다.

## 남은 확인

Root의 실제 PlayMode 실행에서 `FailedRecoveryRetries…`와 나머지 `ExpeditionFailurePlayModeTests`가 통과하는지는 receipt로 확인해야 한다.