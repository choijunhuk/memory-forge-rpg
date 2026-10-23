# 서버 노드 UI 수용 — 2026-10-17

## 구현과 독립 검토

Claude Opus5.5가 별도 worktree에서 `MemoryForgeServerPracticeClient.cs`와 기존 `PracticeServerSessionPlayModeTests.cs` 두 파일만 확장했다. Codex가 원래 로컬 화면·Run.Services/Progression·서버 계약/PublicView와 비교하고 실제 .NET/Unity에서 검증했다. 새 엔진/서버 명령/수치/콘텐츠 변경은 없다.

- 휴식: 공개 uses·HP를 표시하고 heal/upgrade 및 proceed를 전송한다.
- 공방: 공개 offers의 카드/부품, 기존 변주·제거·진행을 연결한다. 원래 UI처럼 열린 offer의 재추첨/제거는 제공하지 않는다.
- 상점: 공개 가격/판매 여부/동전과1회 제거 비용을 표시하고 기존 buy/proceed만 보낸다. 모든 항목은 스크롤로 접근한다.
- 사건: 원래 event ID의 제목·본문·선택지를 읽는다. 제거는 원래 개수, 복제는1개를 순서 있는 deck keys로 선택한다. 완료된 선택은 재전송하지 않고 선택 완료 후 proceed한다. 사건 변주는 기존 fromEvent 공방으로 이어진다.
- 기록소: 기존 archive continue/return만 보낸다. 기존 기억은 유지하고 패배 시 이번 탐사 변화만 파티 전체 최대1개 추가한다는 안내를 분명히 했다. 승인된 저장 규칙/15% 회복량은 바꾸지 않았다.

미전송 덱 선택은 열린 public context에 묶이고 취소/복귀/다른 기기의 상태 변경에서 폐기한다. 요청 중·미확정 outbox·다른 선택창에서는 새 명령을 보내지 않는다. 승인 전 이득/구매/복제/사용 횟수를 낙관적으로 바꾸지 않고, 유실 응답은 같은 ID/본문으로 재확인한다. 원래 같은 턴 입력 대기와 다음/다다음 턴 시간 예약도 바꾸지 않았다.

## 실제 검증 범위

| 검증 | 결과 | 해석 |
|---|---|---|
| 초기 노드 단위 전체 GUI Play | [253/253, skip0](play_253_detailed.json) | 휴식/공방/상점5개 신규 view 시험 포함 |
| 사건/기록소 포함 전체 GUI Play | [258개 완료·succeeded·관찰 실패0](play_258_job_after_domain_reload.json) | MCP 패키지는 domain reload 뒤 leaf Result를 의도적으로 보존하지 않아 이 영수증에는 passed/skipped 상세 분모가 없다. |
| 최종 실제 view/전송 focused Play | [76/76, skip0](view_76_detailed.json) | 상세 결과 확보를 위해 임시 DisableDomainReload로 실행, 원래 옵션/설정 바이트 복구. 신규10개 UI 시험과 기존66개 전송 시험 포함. |
| GUI Edit | [3/3, skip0](edit_3_detailed.json) | Editor 연결 회귀 |
| 동일 BCL NUnit .NET10 | 83/83 | UNITY 전용 view 시험은 .NET 분모에 포함하지 않는다. |
| 실제 Core HTTP + Unity uGUI 휴식 | [before](rest_button_before.json) → [after](rest_button_after.json), [화면](rest_after.png) | public HTTP37명령으로 실제 REST에 도달한 뒤 뷰 Refresh. 실제 uGUI RestHeal.onClick을 프로그램으로 호출해 v38→39, HP210→243, uses1→0; 이어 실제 Proceed 버튼으로 지도 복귀. |

실제 휴식의 준비 경로는 서버가 선택한 seed의 신규 개발 fixture 계정에서 기존 공개 명령만 사용했다. seed/state 주입은 없다. 캡처 `rest_heal_transition.png`는 호출과 같은 프레임의 전환 관찰이며 확정된 before 스크린샷으로 쓰지 않는다. 새 물리 마우스/Android 확인이 아니다. 최종 Unity 전용 MCP Play/Pause/Resume/Stop은 Claude가 직접 시험하고 Codex가 별도 상태 조회했다([도구 수용](../claude_tools_20261017/README.md)).

## 남은 범위

서버 기억 저장 UI(VICTORY/DEFEAT/DONE), 필살기/상세/배속의 서버 화면, 실제 Core를 통한 모든 사건 분기·공방/상점·보스 후 기록소 양방향 UI 흐름, 실제 계정/앱 전체 복구·Android·새 배포 빌드는 완료 표시하지 않는다. 기존 전체 로컬 원정·서버 영속 저장 구현은 유지한다. 무작위/예약/경제 규칙과 원문 콘텐츠는 변경하지 않았다.
