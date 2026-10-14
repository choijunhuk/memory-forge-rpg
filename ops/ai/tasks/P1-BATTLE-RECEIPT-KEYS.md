# P1-BATTLE-RECEIPT-KEYS

- 요구: 원문 §5.15의 저장된 rid에 대한 멱등·원자성. 상속 Object.prototype 속성은 저장된 요청이 아니다. 게임 수치·내용 변경 없음.
- 역할: C# opening 검토에서 찾은 JS 참조 엔진 결함을 좁게 수정한다.
- 완료: 12개 상속 이름과 일반 대조에서 첫 요청·own 영수증/JSON 저장·버전 진행 후 재시도·내용 충돌 우선순위·응답 분리·실패 rid 재사용·prototype 보존을 검증한다. 26개 집중 시험 RED는 일반2 PASS/상속24 FAIL이다.
- 소유: 별도 codex/csharp-battle-opening worktree의 Sol61 Max가 새 `engine/tests/battle_receipt_keys.test.mjs`와 로컬 RED 로그만 작성했다. 엔진 쓰기는 현재 C# opening 검토·통합 뒤 별도 배정한다. root가 반대 Opus Max 리뷰·검증·통합·PR2를 소유한다.
- 수정 후보: Battle.command의 own lookup + 계산 속성 spread 저장. 기존 memory-v2 경로와 같은 규칙을 재사용한다. engine source hash 변경 후 opening267 및 전체1000 traces를 실제 재생성/재검증하고, 과거 balance/corpus의 원래 출처를 덮어쓰지 않는다.
- 상태: REPRODUCED_RED; 엔진은 아직 변경하지 않음.
