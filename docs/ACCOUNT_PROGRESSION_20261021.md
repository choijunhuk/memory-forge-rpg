# 계정 편성·성장·장비·지원 파트너

상태: **구현·검증 진행 중인 인계 초안**(2026-10-22). 보유 동료 실출격 → 성장 → 장비·주간 보스 → 지원 파트너의 서버·클라이언트 구현과 단계별 시험을 연결했다. 실제 계정의 튜토리얼·1장 기억 저장과 성장 진입, 현재 전체 시험 및 신선한 PC/휴대폰 인증을 확인했다. 휴대폰 요청 보관의 Android 경계 수정과 재검증, 새 커밋의 원격 CI는 진행 중이다. [검증 현황](../reports/account_progression_20261021/README.md)과 [기계 판독 기록](../reports/account_progression_20261021/verification.json)을 함께 본다.

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

개발 로컬 계정은 소유자 CLI의 DRY_RUN_PASS → 백업 → MIGRATED에서 기존 22회 모집·영수증 5건·revision 5를 보존했다. 이후 실제 튜토리얼과 1장을 LIVE 기억 저장으로 완료하고 기본 R 4종의 1회 지급, AL5·니아 레벨2·잉크5,800·크레딧9,900을 확인했다. 성장 던전 dg_ink에 실제 출격해 PLAYER/v0/HP184 상태에 도달했다. 기존 네이티브 PC 실행에서는 일반 모집권7→6·누적 모집22→23의 1회 모집이 성공했고 revision351·영수증351건 전체 검증 PASS와 과거5건의 본문 문자열 보존을 확인했다. 이는 성장 던전 승리나 휴대폰의 저장·경제 여정 완료를 뜻하지 않는다. 개인 계정·인증 정보·원본 저장소·비공개 백업은 Git에 포함하지 않는다.

## 검증과 남은 수용

최신 기록은 Core 19개 보고 그룹(지원 1,065), 서버 3,128 판정, 별도 실제 Core/파일 도메인 실행 Growth192·Equipment54·Progression64 PASS다. Unity PlayMode는 **722 PASS / 0 FAIL / 11 명시적 NOT_RUN(총733)**, EditMode는10 PASS다. 최신 PC·Android 빌드는 오류0이며 경고는 각각14·15다. [S4 독립 비교](../reports/account_progression_20261021/s4_full_parity/candidate2/evidence.json)는 JS/C#144사례·전체 상태2,292개, 이전 JS그래프11,657개·C#체크포인트840개, 카드 설명519개를 보존했다. 서로 다른 검증 축과 중복 집계 수치를 합산하지 않는다.

전체 Node396 PASS 뒤 마지막 미리보기 기준 출처 갱신의 표적1 PASS를 기록했다. 제안14·문서9·Python78·인계33과 새 Playwright 기억 UI13·후반 UI6도 PASS다. 소스 출처 갱신은14개 파일의 SHA메타데이터 **1,016개 값**만 바꾸고 그 외 비압축 기대 바이트를 보존했다. 배포 매니페스트를 재생성하지 않았다.

반대 AI인 공식 Opus의 Core·서버·클라이언트·최종 UI·JS/CI 검토는 검토한 스냅샷에서 PASS다. 후속 `ACCOUNT-S4-FINAL-DELTA-REVIEW-20261022`도 DONE 응답 파서·Android 프록시 수정·CI 시험 안정성 차분에 P1/P2 없이 PASS했다. 검토자가 시험을 실행했다는 뜻은 아니며 실행 근거와 검토 근거를 구분한다.

2026-10-22 휴대폰 재연결 지시는 이전 “휴대폰 제외”를 갱신한다. 설치된 APK131,838,379바이트의 앱 데이터 보존·홈·터치 로그인 진입 뒤 실제 인증도 결정900·일반권6으로 PASS했다. 신선한 PC 실행은 인증·DONE 파싱·HOME AL5/보유3명·성장 AL5를 정상 세션 클릭으로 확인했다. 이후 휴대폰의 OUTBOX_UNSUPPORTED 보호 거절이 저장·경제 동작 수용을 막고 있다. 실제 앱 UID의 네이티브 잠금·0600 권한·프로세스 간 경합 검사는 PASS이며, Android IL2CPP 플랫폼 판별에 한정한 오류 코드 상수 수정의 표적 시험·새 APK·반대 검토는 진행 중이다. 새 APK 식별은 루트 확인 뒤 갱신한다. 네이티브 관찰을 사람 수동 수용으로 표시하거나 호스트 libusb SIGABRT를 게임 코드 충돌로 세지 않는다.

PC 장비/현재 전투 읽기 수용, Android 요청 보관 경계의 표적 시험·재빌드·반대 검토와 휴대폰 저장/경제 수용, 최종 소스/실행기 식별 확정, 커밋·push·PR 및 정확한 SHA의 Linux/PostgreSQL CI가 남아 있다. 로컬 Docker 제약이나 과거 CI PASS를 새 원격 CI 성공으로 대신하지 않으며 전체 완료 상태로 바꾸지 않는다.
