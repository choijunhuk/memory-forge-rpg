# 계정 편성·성장·장비·지원 파트너

상태: **승인된 계정 진행 구현·검증 완료**(2026-10-22, 구현 소스 `8d1b8d6`). 보유 동료 실출격 → 성장 → 장비·주간 보스 → 지원 파트너의 서버·클라이언트 구현과 단계별 시험을 연결했다. 실제 계정의 튜토리얼·1장 기억 저장과 성장 진입, 현재 전체 시험 및 최신 빌드 실기기 검증의 인증·모집을 확인했다. 휴대폰 재시작·재인증 뒤 모집 결과 유지와 현재 성장 전투/공방 복귀를 확인했고 구현 소스의 [원격 CI](https://github.com/choijunhuk/memory-forge-rpg/actions/runs/39786386457)도 전체 통과했다. [검증 현황](../reports/account_progression_20261021/README.md)과 [기계 판독 기록](../reports/account_progression_20261021/verification.json)을 함께 본다.

## 사용자 확정 규칙과 구현 범위

| 단계 | 현재 연결한 동작 | 주요 소스 |
|---|---|---|
| S0 기록 보존 | 정상 디코딩 후 과거·현재 탐사, 기억, 원장, 정확한 영수증을 읽기 전용으로 검증 | [전체 검증기](../server/MemoryForge.Server/AccountStoreVerifier.cs) |
| S1 보유 편성·출격 | 계정 프로필, 보유 3인 편성, 튜토리얼·1장 출발, 재접속·포기·홈 복귀 | [계정 진행](../server/MemoryForge.Server/Progression.cs), [로비](../client/MemoryForgeUnity/Assets/Scripts/MemoryForgeLobbyProgression.cs) |
| S2 성장 | 기존 적 1웨이브 던전, 승패 정산, 재료·집중 원장, 동료 레벨업 | [성장 서버](../server/MemoryForge.Server/S2Growth.cs), [성장 화면](../client/MemoryForgeUnity/Assets/Scripts/MemoryForgeGrowthClient.cs) |
| S3 장비·주간 보스 | 승급·스킬, 무기 장착·레벨·승급·정련, 갑주·장신구 제작·강화, 주간 보스의 연습·정산 | [장비](../server/MemoryForge.Server/S3Equipment.cs), [주간 보스](../server/MemoryForge.Server/S3WeeklyBoss.cs), [장비 화면](../client/MemoryForgeUnity/Assets/Scripts/MemoryForgeLobbyEquipment.cs) |
| S4 지원 파트너 | 비출전 보유 동료 선택, 출발 당시 빌드 고정, 지원 조회·대상/선택·필살기·재시도 복구 | [지원 서버](../server/MemoryForge.Server/S4Support.cs), [지원 판정](../core/MemoryForge.Core/Battle.Support.cs), [지원 화면](../client/MemoryForgeUnity/Assets/Scripts/MemoryForgeLobbySupport.cs) |

1. 프롤로그 첫 클리어 계정 XP는 **320**이다. 프롤로그 뒤 AL3, 1장 첫 클리어의 기존 200 XP를 합쳐 누적 520 XP로 AL5가 된다. 프롤로그의 기존 결정 300·일반 모집권 5와 1장의 결정 600·일반 모집권 2도 모집 원장에 연결한다.
2. 서버가 확인한 튜토리얼 첫 기억 저장에 니아·하린·리엔의 시작 보유분과 기본 R 무기 4종 각 1개를 계정당 한 번 연결한다. 재전송·재생·PlayerPrefs가 지급 근거가 되지 않는다.
3. 튜토리얼은 니아·하린·리엔의 레벨 1·승급 0·공명 0·기본 R 빌드를 고정한다. 1장은 AL3에서 실제 보유한 서로 다른 3인과 고정 출발 입력을 사용한다. 현재 프롤로그는 **7층 Run 재사용**이며 §14.5의 대사·연출을 포함한 3전투+보스 온보딩 전체를 구현한 것은 아니다. 자유탐사에 새 배율을 추가하지 않는다.
4. 성장 던전은 기존 적 1웨이브, 패배 시 집중 소모 0이다. 승리 비용·보상·성장 비용은 승인된 기존 표를 사용한다. 기본 R 정련은 단계마다 **제련핵(refine_core) 1개**이며 승급 핵(core)과 구분한다.
5. 지원은 **비출전 보유 동료 1명**이다. 출전 동료가 가득 찬 자기 개별 게이지 **100**을 소비해 파트너의 기존 필살기를 전투당 **1회** 사용한다. 출발 당시 파트너 성장·장비·필살기 공명 변형을 고정한다. 파티 HP·시작 카드·상시 파티 패시브를 추가하지 않으며, 기존 필살기가 만드는 일시 토큰은 출처 스탯을 유지한다. 별도 파트너 모집을 추가하지 않는다.

초심자 40권의 별도 계정 지급 시점은 미정이다. 가격·모집 확률·새 무료 재료·창작 원본은 이번 범위에서 바꾸지 않는다. [PRD](plans/prd-account-progression-20261021.md), [시험 명세](plans/test-spec-account-progression-20261021.md), [S0/S1 계약](plans/contract-account-progression-s1-20261021.md), [S2 계약](plans/contract-account-progression-s2-20261021.md)을 기준으로 삼으며, 이후 단계의 동결 계약과 검토 기록도 루트가 보존한다.

## 저장·재접속·이관 경계

출발 때 실제 보유·공명·장착과 성장 스탯을 고정한다. 이후 모집·성장·장착·지원 선택이 진행 중 탐사의 재생을 바꾸지 않는다. 미확정 요청은 같은 요청 ID·본문으로 복구하고, 이전 영수증을 받은 것만으로 닫힌 탐사를 UI에서 다시 활성화하지 않는다. 포기는 종료 표식을 보관하고 Core 기억 저장을 새로 만들지 않는다. [인증 수명](../client/MemoryForgeUnity/Assets/Scripts/MemoryForgeAccountConnection.cs)과 [요청 보관](../client/MemoryForgeUnity/Assets/Scripts/PracticeCommandOutbox.cs)은 화면 수명과 구분한다.

기억 용량 초과는 useMemory의 두 값 모두 쓰기 없이 거절한다. 기본 덱 재시도로 해결된다고 표시하거나 기억 초기화·투영·임시 공명으로 우회하지 않는다. 실제 계정이 걸리면 해당 계정 수용만 보류한다.

구 v1–v3 정책·원장·영수증은 등록된 버전으로 검증한다. S4 Core 변경은 [소유자 전용 이관](../server/MemoryForge.Server/OwnerMigration.cs)에서 보존한 실제 구 실행기의 정상 디코딩·전체 검증과 신 실행기의 전체 검증을 모두 통과해야 한다. 허용된 MVID·재생 형식 바인딩만 치환하며 과거 영수증의 ID·상태·본문 바이트와 기억/탐사 체크포인트를 보존한다. 비공개 백업과 잠금 안 원본 바이트 비교 후 원자적으로 저장하고 신 실행기로 다시 검증한다. 일반 HTTP 디코더의 엄격한 바인딩 검사는 유지한다. 지원이 없는 기존 Run/Battle 그래프에는 support 멤버 자체를 넣지 않는다.

개발 로컬 계정은 소유자 CLI의 DRY_RUN_PASS → 백업 → MIGRATED에서 기존 22회 모집·영수증 5건·revision 5를 보존했다. 이후 실제 튜토리얼과 1장을 LIVE 기억 저장으로 완료하고 기본 R 4종의 1회 지급, AL5·니아 레벨2·잉크5,800·크레딧9,900을 확인했다. 성장 던전 dg_ink에 실제 출격해 PLAYER/v0/HP184 상태에 도달했다. 기존 네이티브 PC에서 일반 모집권7→6·누적 모집22→23의 1회 모집에 이어 최신 휴대폰에서도 정상 모집1회가 성공했다. 휴대폰 결과는 R연마석·잔광+15이며 일반권6→5·누적23→24·revision351→352, 결정900·유료 결정0은 유지됐다. 고유 영수증352건(run313·모집7·성장32)과 기억 버전2를 정상 전체 검증했고 기존 영수증 항목도 정확히 보존했다. 재귀 구조에서 중복을 포함한353건은 고유 영수증 수로 세지 않는다. 실제 성장 던전 승리를 확인했으며, 휴대폰 프로세스 재시작·정상 재인증 뒤 티켓5장·최근 결과와 HP184 성장 전투·홈 인증 복귀도 확인했다. 개인 계정·인증 정보·원본 저장소·비공개 백업은 Git에 포함하지 않는다.

## 검증과 남은 수용

전체 기록은 Core19개 보고 그룹(지원1,065), 서버3,128, 격리 실제 Core/파일 실행 Growth192·Equipment54·Progression64 PASS다. Unity PlayMode는722 PASS / 0 FAIL / 11 명시적 NOT_RUN(총733), EditMode는10 PASS이며 이후 Android 저장 경계에는 실제 Unity Outbox17/17·호스트14/14 표적 검증을 추가했다. 최신 PC·Android 빌드는 오류0, 경고14·15다. [S4 독립 비교](../reports/account_progression_20261021/s4_full_parity/candidate2/evidence.json)는 JS/C#144사례·전체 상태2,292개, 이전 JS그래프11,657개·C#체크포인트840개와 카드 설명519개를 보존한다. 서로 다른 검증 축과 중복 집계 수치를 합산하지 않는다.

Node396 PASS와 마지막 미리보기 기준 출처 갱신의 표적1 PASS, 제안14·문서9·Python78·인계33·Playwright 기억 UI13/후반 UI6 PASS를 기록했다. 출처 갱신은14개 파일의 SHA메타데이터1,016개 값만 바꾸고 그 외 비압축 기대 바이트를 보존했다. 배포 매니페스트를 재생성하지 않았다.

공식 Opus의 Core·서버·클라이언트·최종 UI·JS/CI 및 DONE 파서·Android 프록시·CI 안정성 검토는 해당 스냅샷에서 PASS다. 최종 Android native fd 구현은 Max, 후속 시험은 xhigh 독립 검토에서 P1/P2 없이 PASS했다. 시험용 native.c의 누락 출처 해시는 루트가 오류 주입 오프셋만 바뀐 차분과 보호 불변을 확인하고 동결 사본·보충 기록으로 보완했다. 검토자가 시험을 실행했다는 뜻은 아니다.

Android에서만 원시 파일 디스크립터로 열기·잠금·권한·쓰기·동기화·닫기를 수행하며 비Android 경로는 유지한다. 최신 IL2CPP APK는131,848,036바이트, SHA256 `a8da740897505f73e7e6fd06a1464151f5bd01ae0f4cbad2de261b3b469dea87`이며 빌드·설치·인증 뒤 실제 모집이 성공했다. 최신 PC는 HOME/성장/장비/현재 전투 HP184·3인 가로 게이지/설정/저장 모집 결과를 네이티브 입력으로 검증했다. 이는 사람 수동 수용과 구분한다.

휴대폰에서 물리 잠금파일0600과 확인 뒤 pending/temp 부재를 관찰했다. 짧게 존재한 요청 기록의 실제 파일 모드는 샘플링 시간 초과로 **NOT_OBSERVED**이며, 호스트14개 시험과 소스의0600 보장으로 구분해 기록한다. 개인 계정·인증 정보·원본 저장소·비공개 백업·장치 경로는 Git에 복사하거나 링크하지 않는다.

구현 커밋 `8d1b8d6`를 PR #2에 push했다. 해당 SHA의 Linux native fd14·시스템 libc 직접2, 새로 빌드한 소스의 파일/HTTP 검증, 등록된 고정 실행기의 PostgreSQL16 소유자 이관·지원 HTTP 및 나머지 CI가 모두 통과했다. 로컬 Docker 제약이나 과거 CI를 새 CI 성공으로 대신하지 않는다. 서버 판정 수는 로컬 파일3128·Linux 파일3044·PostgreSQL1692를 실행별로 구분한다. 문서 확정 커밋은 제품 소스를 바꾸지 않으며 PR의 최신 검사에서 결과를 확인한다. 캐릭터별 전용 아트는 일부만 준비돼 있으며 리엔 등은 기존 대체 아이콘 또는 선택적 초상 빈칸을 사용한다. 최종 사용자 화질 판정은 기록하지 않았다.
