# 저장되지 않은 상속 요청 ID의 오인 수정

원문 v0.8.1 §5.15는 실제 저장된 요청만 재사용하도록 규정한다. 기존 Battle의 일반 객체 조회는 `__proto__`·`constructor` 등 Object.prototype의12개 이름을 첫 요청부터 기존 영수증으로 잘못 판정했다. 엔진 두 줄을 수정해 own 속성만 조회하고 `Object.defineProperty`로 안전하게 저장한다. 모든 일반 대입 속성 설정은 true로 유지하며, 원래 영수증 객체·처리 비용·truthy rid·digest·버전/원자성 순서를 유지한다.

[RED](red.txt)는 일반2 PASS/상속24 FAIL이며 [최종 GREEN](green.txt)은26/26이다. [중간 spread 시도](intermediate_spread_green.txt)는 회귀 통과 후 root 리뷰에서 전체 맵 복사 비용·객체 교체 때문에 거절됐다. 이 로그를 최종 구현 검증으로 바꾸지 않는다. 공식 [Opus Max 독립 리뷰](../../ops/ai/tasks/P1-BATTLE-RECEIPT-KEYS-REVIEW.result.json)는 최종 own lookup/defineProperty를 **APPROVE**했다. 리뷰어는 직접 시험하거나 gzip을 열지 않았다.

- [JS204](node_tests.txt), [집중36](focused_tests.txt), [C#202+87+2000+13+2220/267명령](csharp_checks.txt) PASS.
- [실제 재생성1,000탐험](trace_export.json) 후 [273,748명령·275,748해시 재생/전수 비교](trace_replay_comparison.json): 기존 전체 입력·명령·상태·해시·결과와 동일하다. 전체 C# Run 명령 재생은 여전히0개다.
- [전투 시작267 재생성](opening_export.json)·[전체 기록 비교](opening_comparison.json)도 동일하다. 두 corpus 모두 바이트를 실제 재생성했으며, 출처 해시만 수동 변경하지 않았다.
- [동일 조건1,320회 재측정](paired_balance.json.gz)과 [전수 비교](balance_comparison.json): 통계·시나리오·각 전투·목표 판정까지 동일하며 **32개 이탈 유지**. 바뀐 것은 엔진 출처 해시뿐이다. 원래 수치와 목표는 유지한다.
- [생성 HTML](prototype_source.json)은 정확히 같은 엔진을 포함한다. [저장UI13](memory_ui.txt)·[후반UI10](late_ui.txt) PASS. [새 스크린샷21개](screens.json)를 별도 보관하고 과거 경로의 원본을 복원·바이트 검증했다.
- 제안14·Python48·문서9·인계13 PASS, 원본 데이터·게임 본문·아트·PDF·입력 매니페스트는 [보존표](preservation.json)를 따른다. 입력 매니페스트는 재생성하지 않았다.

이 수정은 JS 요청 처리 단위다. 별도 worktree의 다음 C# end/적 행동 구현, 새 Unity·실기기·서버·전체 Battle/Run 수용을 완료로 표시하지 않는다. 원래 전투 시작 커밋 b98e0f2의 [push/PR CI 두 개](opening_commit_ci.json)는 해당 head의 성공이며 후속 커밋의 CI로 재사용하지 않는다.

동일 조건 전체 측정 원문은 손실 없는 gzip으로 보관하고 원시 바이트 SHA256·압축/복원 일치를 [파일 기록](balance_artifact.json)에 남겼다. 과거 JSON을 축약하거나 덮어쓰지 않았다.
