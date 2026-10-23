# P1-CARD-FLOW-EXACT-COMPARISON-FIX — 수정 보고 (source only)

- 작성: Claude Opus 5.5(`claude-opus-5-5`), 요청 노력 high, 2026-10-16. session_id·실제 노력은 Codex 회수 메타데이터를 따른다.
- 변경 파일: `MemoryForgeExpeditionClient.cs`(비교 함수만), `ExpeditionFailurePlayModeTests.cs`, 이 보고서.
  - 재시작 수정 코드·시험과 앞선 두 보고서는 이력으로 그대로 두었다.
  - Core·다른 시험·씬·설정은 바꾸지 않았다.
- **검증: 컴파일·Unity NOT_RUN.**
  - 근거 1: Root 실제 결과 `s2_initial_play_result.json` — 5개 중 4 PASS, `FractionalOpaqueMetadataComparesExactlyWhereTheCanonicalHashRefuses` FAIL.
  - 근거 2: Mono 증거 `s2_fractional_comparison_witness.json` — `JToken.DeepEquals({note:0.1},{note:0.10000000000000002})`가 true다. 반면 `double.Equals`는 false이고 직렬화 문자열도 다르다.

## 원인과 수정

- **원인:** `SameGraph`가 `JToken.DeepEquals`에 맡겼는데, Json.NET은 가까운 double을 같다고 본다. 그래서 1 ulp 차이가 "변경 없음"으로 처리돼, 실패한 명령 뒤의 부분 변경을 일반 거절로 놓칠 수 있었다.
- **수정:** `SameGraph` 안에 작은 재귀 `SameToken`을 넣었다.
  - JObject는 멤버 수와 이름별 값을 비교한다. 순서는 따지지 않는다.
  - JArray는 길이와 순서별 항목을 비교한다.
  - **Float JValue는 상자에 든 값 자체의 `Equals`로 정확히 비교한다.**
  - 다른 기본값(정수·문자열·bool·null)은 Json.NET의 보통 비교를 유지한다. 같은 값의 int와 long은 같다.
  - JTokenType이 다르면 바뀐 것으로 본다.
- **하지 않은 것:** 허용 오차, 반올림, 형 변환, `CanonicalState.Hash`, 범용 정규화기는 넣지 않았다.
- **유지되는 것:** 분리 스냅샷, 비교 실패 시 격리, 미지 오류 격리, 변경 없는 일반 거절 경로는 그대로다.

## 시험

- **기존 1 ulp 시험(`SameRun`):** 수정 없이 GREEN이 되어야 한다. 시험을 완화하지 않았다.
- **새 `ComparisonIsOrderFreeForObjectsOrderedForArraysAndExactForNestedDoubles`(작은 명시 그래프):**
  - 멤버 순서가 바뀐 객체 → 같음.
  - 배열 순서가 바뀜 → 다름.
  - 중첩 배열·객체 안의 double 1 ulp 차이 → 다름.
  - 같은 값의 int/long → 같음.
  - 정수와 실수 → 다름.
  - 멤버 추가 → 다름.
  - null/null → 같음.
- **store 전체 비교:** `StorePrint`가 private `store` 그래프 전체(schema·memory·version·history·receipts)를 JSON으로 읽도록 바꿨다. 기존 복구 시험과 재시작 시험의 store 불변 판정은 이제 전체 store를 비교한다.
- **`EarnSave`:** 실제 명령으로 얻은 store에 history 1건과 그 커밋의 receipt(`LastRequestId`)가 있음을 확인한다. 기대 상태는 주입하지 않는다.

## 남은 위험

- **다른 숫자 형:** decimal/float처럼 다른 CLR 형의 실수를 섞으면 "바뀜"으로 본다. 현재 그래프는 double만 쓴다.
- **-0.0과 0.0:** `double.Equals` 기준으로 같다. JSON 직렬화에서도 같은 값이다.
- **비용:** 실패 경로에서만 비교하므로 성공 경로 비용은 그대로다.

## Root 검증

`MemoryForge.Client.Tests.ExpeditionFailurePlayModeTests` 전체(이제 7개: 초기 5 + 재시작 1 + 비교 1)를 실행한다. 이어서 재시작 RED 증거를 재실행하고, 기존 Expedition·digest와 전체 PlayMode/EditMode를 돌린다.