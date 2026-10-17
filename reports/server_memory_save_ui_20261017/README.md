# 서버 기억 저장 UI 수용 — 2026-10-17

기존 §8.9–8.14 / SAVE-081-11·12의 서버 기억 선택 → 미리보기 → 원자적 확정 → 다음 출발을 Development 전용 Unity 화면에 연결했다. Core·서버 규칙·수치·원문·기존 로컬 원정·장면·의존성·입력 매니페스트는 변경하지 않았다.

## 작성·독립 검토

공식 Claude Code 2.1.285 / Opus5.5 고정 CLI가 별도 worktree에서 전송/DTO/전송 시험과 저장 UI/Unity 시험을 병렬 작성했다. 최초 두 작업 xhigh, 제한된 형식 보완·문구·스크롤 수정 High. 구독 인증/동일 계정/24시간 내 Usage credits·자동 충전 OFF 증거를 실행기가 확인했으며 API/추가 크레딧/다른 모델·재귀 호출은 사용하지 않았다. raw result의 RECEIVED_UNVERIFIED는 작성자 결과이고, 여기의 Codex 독립 검토·시험이 통합 근거다. Editor 쓰기권은 Codex 한 명이다.

- 전송/DTO: PracticeServerSession.PreviewMemoryAsync·PracticeMemoryPreviewView. 기존 pure route/credential handshake/엄격 JSON/불변 복사/서버·기억 버전 문맥을 재사용한다. 미리보기는 requestId/outbox/저장/연출 이벤트를 만들거나 해소하지 않는다.
- 저장 화면: 공개 before/changes·서버 결과 덱/전체 용량·패배 파티 전체 0~1변화·공용 신규 카드 배정·저장/서버 거절/미확정 요청·DONE·기억을 이용한 다음 출발. 긴 목록은 단일 스크롤이다. 서버 error=null인 현재 선택/문맥의 미리보기와 기존 mutation gate가 모두 맞아야 저장한다.
- Root가 실제 공개 응답에서 capacity 한 캐릭터만 지운 재현은 최초 잘못 허용됐다. Claude가 memory/used/capacities의 캐릭터 목록 일치 검사와 세 경계 사례를 추가했다. 독립 probe RED→GREEN, .NET117→120 전부 통과. OVER_CAPACITY 자체는 유효 응답으로 계속 전달한다.
- 실제 아래쪽 변화 선택 후 미리보기 재표시에서 스크롤이 맨 위로 돌아가는 사용성 문제를 발견해 같은 문맥의 오프셋 보존/새 문맥 리셋과 회귀를 추가했다. [최종 전체 Play304/304](play_final.json)와 [실제 승리/용량/스크롤19판정](actual_ui_phase_c.json)을 통과했다. 하단의 선택 행이 실제 뷰포트 안에 전부 남는 것을 경계 좌표로 확인했다.
- '아무것도 보내지 않음' 문구를 pure preview 조회와 저장 미확정의 구별에 맞게 정정했다. 의미/규칙 변경은 없다.

## 자동 검증

| 검사 | 새 실행 결과 |
|---|---|
| 같은 BCL NUnit 전송/DTO | 120/120 |
| Unity 실제 GUI Play | 최초303/303, 스크롤 보완 최종304/304 · skip0 |
| Unity 실제 GUI Edit | 3/3 |
| JS / Python / proposals | 333/333 · 57/57 · 14/14 |
| 문서 / 인계 | 9/9 · 31/31 |
| Console | 게임 오류0 · 기존 Metal memoryless depth 경고2 |

최초 개별 이름 필터는 0개를 수집했으므로 실행 증거로 세지 않는다. 이후 실제 class/assembly 결과를 사용했다. 상세 결과 보존을 위해 시험 동안만 DisableDomainReload를 켰으며 원래 옵션0·원본 두 ProjectSettings 파일 바이트를 복원했다. 자동 fixture 시험은 실제 Core/서버 성공이 아니며 다음 절을 별도로 검증했다.

## 실제 Core HTTP·Editor uGUI

최초 준비는 원래 파티/서버 난수의 정상 명령 경로로 SHOP v58 → DEFEAT v235다. 178 HTTP 시도 중177승인/1형식거절이었고, 상태/seed 주입은 없었다. 오류를 서버/클라이언트 수치 변경으로 우회하지 않았다.

- [선택·거절·미확정 15판정](actual_ui_phase_a.json): 빈 선택·한 변화 허용, 다른 캐릭터 둘의 선택은 실제 Core DEFEAT_KEEP_ONE, 모든 미리보기/거절 전후 전체 서버 envelope 해시 동일. 실제 raycast/Pointer가 변화·선택 비우기·저장 버튼에 도달하고 연타는 거절됐다. 성공200응답을 실제로 절반만 전달하고 연결을 닫자 UI는 성공으로 간주하지 않고 요청을 보관/잠갔다.
- [실제 Play 재시작·같은 요청·다음 출발 28판정](actual_ui_phase_b.json): 원래 옵션0에서 종료/재시작해 [이전 AppDomain 작업이 사라짐](actual_restart_domain.json)을 확인했다. 새 세션은 같은 디스크 pending 본문을 복원했다. 서버 full account는 DONE v236/기억v1, 미확정 잠금은 명시 retry까지 유지했다. retry 전후 서버 전체 파일 해시 동일·실제 두 memory-commit POST의 [본문 해시 동일](actual_proxy_receipt_evidence.json). 다음 CreateRun은 run v0/기억v1이고 저장된 일반 카드16개 각각의 key/id가 새 덱에 정확히 한 번씩 나타나며 수량도16개다.
- UI 검사기의 첫 자동 클릭은 스크롤 직후 culling 갱신보다 빨라 실패했다([원본 보존](actual_ui_phase_a.initial-pointer-harness.json)). 다음 프레임의 실제 raycast 도달을 확인하고 검사기에50ms 화면 갱신 대기만 추가한 뒤 위43판정을 통과했다. 제품 검사를 약화하거나 서버 상태를 고쳐 통과시키지 않았다.

- [승리·용량·스크롤19판정](actual_ui_phase_c.json): 앞에서 저장한 기억v1로 시작한 실제 새 탐사가 VICTORY v142에 도달했다. Nia의 출발 전 덱6장에 앞선 저장 카드가 남아 있었으며 승리2변화는 허용/전체 결과 용량 초과는 실제 Core가 차단했다. 미리보기/거절/스크롤 전후 전체 서버 파일 불변이다. 하단 선택의 실제 좌표는 클릭 때1100→최종944로 정상 콘텐츠 제한이 적용됐고 선택 행은 뷰포트(-470..0) 안(-466..-404)에 전부 남았다. 최초 고정 오프셋 검사 실패([보존](actual_ui_phase_c.initial-fixed-offset-harness.json))는 콘텐츠 높이 변화의 정상 clamp를 고려하지 않은 검사기였다. 새 검사도 맨 위 복귀/선택 행 가림이면 실패하며 제품 검사를 약화하지 않았다. 이 절은 실제 승리 저장 commit을 추가 실행한 것으로 확대하지 않는다. 총 실서버/uGUI62판정.

## 범위와 남은 작업

실제 검증은 macOS Unity Editor와 소유한 loopback Development 계정이다. 물리 마우스·Android/IL2CPP·새 standalone 빌드·실계정 인증/운영 배포·130% 글자는 NOT_RUN. 실제 Core의 승리 선택/용량 초과는 추가 재현했으나 승리 저장 commit·공용 카드 배정 양성 UI는 fixture Play 시험과 구별한다. 저장 직전 보유 부품 붙이기(SAVE-081-12)의 Core/API는 이미 있지만 서버 화면은 다음 작업이다. 배속/필살기/상세 등 전체 게임 완료를 선언하지 않는다. GitHub Private/Draft PR#2를 유지한다.
