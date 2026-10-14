# Memory Forge v0.8 — 작업 진입점

먼저 AGENTS.md, docs/CURRENT.md, docs/design_v0.8.md, reports/integration_v080/status.json을 읽는다.
이 ZIP은 전체 설계 통합본이지 완성 Unity 게임이 아니다. 현재 실행 참조와 목표 정책을 구분한다.
기존 engine/rules-core.js의 확인된 수정은 보존한다. 독립 proposals/memory_v2.cjs를 실제 Run/HTML에 연결하기 전에는 저장 v2 완료라고 보고하지 않는다.
카드 개체키, 출발 스냅샷, 실제 diff, 전체 최종 비용, 선택 없음 보존을 함께 구현한다. 외부의 before+base 혼합 패치를 복사하지 않는다.
가챠 확률·가격·무료 수급·천장·유료 상품을 임의 변경하지 않는다. 소스 작성/컴파일/단위시험/브라우저/Unity/실기기/서버/실결제를 구분해 보고한다.
작업 전 브랜치 또는 worktree를 만들고 다른 에이전트와 같은 Unity 씬·프리팹을 동시에 쓰지 않는다. 운영 DB·실결제·원격 배포·비밀키 작업을 하지 않는다.

기획·아트는 §9·§15, 전투·저장은 §5·§8, 사업 수치는 §12를 읽는다. 이전 판의 전체 지침은 docs/archive/v0.7.1/AGENTS_before_integration.md에 비교용으로 보관한다.
