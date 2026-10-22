# 계정 진행 구현·검증 현황 — 2026-10-22

**상태: 승인된 구현 범위 검증 완료. 상용 배포 승인 기록은 별도다.** 계정 출격 → 성장 → 장비·주간 보스 → 지원 파트너의 구현·실제 서버 여정과 최신 빌드 실기기 검증을 기록했다. 휴대폰 재시작·정상 재인증 후 저장 결과·현재 성장 읽기·홈 인증 복귀도 통과했고 구현 소스 `8d1b8d6`의 [전체 CI](https://github.com/choijunhuk/memory-forge-rpg/actions/runs/39786386457)도 통과했다. [기능·보존 경계](../../docs/ACCOUNT_PROGRESSION_20261021.md), [verification.json](verification.json)을 함께 본다.

| 검증 축 | 최신 근거 | 범위 / 남은 일 |
|---|---|---|
| Core·서버·도메인 | Core19그룹(지원1,065), 서버3,128, Growth192·Equipment54·Progression64 PASS | 실제 Core·격리 파일 근거; Linux/PostgreSQL CI와 구분 |
| Unity | 전체 Play722 PASS / 0 FAIL / 11 명시적 NOT_RUN(733), Edit10 PASS | Android 최종 경계는 별도 실제 Unity Outbox17/17 PASS |
| Android native fd | 호스트14/14 PASS, 구현 Opus Max·최종 시험 xhigh 검토 PASS | Android만 native 열기/잠금/권한/쓰기/동기화/닫기, 비Android 보존; 출처 보충 기록 완료 |
| PC / Android 빌드 | 오류0, 경고14 / 15 | 최신 IL2CPP APK131,848,036바이트 빌드·설치·인증·모집 PASS |
| JS/C# 독립 비교 | 144사례·전체 상태2,292개; 이전 JS그래프11,657·C#체크포인트840 보존 | [동결 C2 근거](s4_full_parity/candidate2/evidence.json), 지원38·카드 설명519 포함 |
| 기대 자료 출처 | 14개 파일·SHA메타데이터1,016개 값만 갱신 | [갱신 기록](s4_full_parity/provenance-update.json); 그 외 비압축 기대 바이트 보존, 매니페스트 재생성 없음 |
| Node·기타 회귀 | Node396+최종미리보기1, 제안14·문서9·Python78·인계33 PASS | 중복 집계하지 않음 |
| Playwright | 기억 UI13·후반 UI6 PASS | 브라우저 UI 근거이며 네이티브와 구분 |
| 개발 계정 진행 | 튜토리얼·1장 LIVE 기억 저장·기본 R4종1회·AL5·니아Lv2 | 잉크5,800·크레딧9,900, dg_ink PLAYER/v0/HP184; 성장 승리는 별도 |
| 최신 PC 네이티브 | 인증/파싱·HOME·성장·장비·HP184·3인 가로 게이지·설정·저장 모집 결과 PASS | 읽기 전용 최신 검증과 이전 실제 모집1회를 구분; 사람 수동 수용 아님 |
| 최신 휴대폰 네이티브 | 인증·정상 모집1회 R연마석/잔광+15 PASS | 일반권6→5·누적23→24·revision351→352; 결정900/유료0 유지 |
| 실제 저장·영수증 | 정상 전체 검증 PASS, 고유352건(run313/모집7/성장32), 기억 버전2 | 기존 항목 정확히 보존; 중복 포함 재귀353은 고유 수가 아님 |
| 휴대폰 파일 관찰 | 물리 잠금0600·확인 뒤 pending/temp 부재 관찰 | 짧게 존재한 요청 기록 모드는 NOT_OBSERVED; 호스트/소스 보장과 구분 |
| Linux CI | `8d1b8d6` push 전체 PASS: native fd14 + 직접 libc2, 파일/HTTP, 고정 등록 실행기 PG16 소유자 CLI/지원 HTTP | Linux 파일 서버3044·PG1692는 로컬3128과 구분; [실행 결과](https://github.com/choijunhuk/memory-forge-rpg/actions/runs/39786386457) |

최신 APK SHA256은 `a8da740897505f73e7e6fd06a1464151f5bd01ae0f4cbad2de261b3b469dea87`다. 코드·시험·원본 기대 자료를 이 문서 작성 과정에서 변경하지 않았다. C2의 [안내](s4_full_parity/candidate2/README.md)와 이전 후보 이력도 보존한다.

Android 구현의 동결 소스는 PracticeCommandOutbox `ca1a1800…` / PracticeAndroidFile `06999e9f…`이며 전체 해시는 verification.json에 있다. 후속 시험 검토의 LOW 출처 누락은 루트가 native.c의 오류 주입 오프셋 차분만 확인하고 보호 불변·동결 사본·보충 영수증을 남겨 처리했다. 요청 기록0600은 실제 휴대폰에서 관찰했다고 주장하지 않는다. 샘플링 시간 초과와 호스트14개 private-mode 시험·소스 보장을 구분한다.

CI 이관 자료는 정상 명령으로 보상을 획득한 **합성 Aila 시험 계정**과 보존된 관리형 실행기 번들이다. 2,135,275바이트, SHA256 `341c279bbd5181e1508231e96a4d1c88e508e99f81db37e022f0265a16675075`; 개인 계정·bearer·Mach-O/apphost가 없다. validate-only/Python PASS를 실제 Linux/PostgreSQL 실행으로 표시하지 않는다. 실제 계정·자격·원본 저장소·비공개 백업·장치 경로는 복사하거나 링크하지 않는다.

10/22 휴대폰 재연결 요청은 이전 제외를 갱신한다. 과거 Unity 번들 ADB34/libusb SIGABRT는 호스트 도구 이력이며 게임 충돌이 아니다. 인증·모집과 휴대폰 재시작 복구는 각각의 관찰 범위로 기록한다. 가격·확률·창작 원본 변경은 없다.

휴대폰의 재시작·현재 성장·홈 복귀는 PASS다. PR #2의 구현 커밋 `8d1b8d6`의 실제 CI 결과를 읽어 `overallComplete=true`를 승인된 구현 범위로 확정했다. 문서 확정 커밋은 제품 소스에 차이를 만들지 않으며 PR 최신 검사로 별도 확인한다. 전용 캐릭터 아트는 일부만 준비됐고 최종 사용자 화질 판정은 별도다. 최신 사설 원본 근거 대신 안전한 집계와 기존 상대 링크만 제공한다.
