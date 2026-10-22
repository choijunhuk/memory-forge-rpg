# 계정 이관·지원 CI 실패 진단 보강

UI 후속 커밋7bcb69f의 PR CI39860717372는 PostgreSQL 기존1722 판정 통과 뒤 계정 이관/지원 하위 과정에서 실패했다. 하위 stdout/stderr를 비공개로 보관하는 실행기가 종료 코드와 단계까지 버려 실제 실패 위치를 알 수 없었다. 앞선bada9d1/d03648b의 push·PR CI는 모두 통과했고7bcb에서 서버·Core·fixture·이 실행기는 변경하지 않았다. 최초 실패의 구체 원인은 아직 확정하지 않았다.

## 변경

`tools/ai/test_account_support_migration_ci.py`의 모든 하위 호출에14개 고정 단계명을 부여한다. 허용된 정확한 문자열만 받아 공개 실패에는 단계·숫자 종료 코드(음수 포함)·고정 원인 코드만 표시한다. 시간 초과/실행 실패는 종료 코드를 추측하지 않고 unavailable로 표시한다. argv·환경·stdin·예외 원문·로그·증명 JSON·데이터베이스 이름/경로는 공개하지 않는다.

기존 비공개0600 로그, 실제 명령/입력/환경/cwd/timeout, 실행 순서와 모든 DB·이관·CLI·HTTP·번들 검증 판정은 유지했다. 기존 `GateError` 경로를 재사용하고 새 의존성을 추가하지 않았다. 게임·서버·Core·SQL·번들·확률·보상·원본 저장은 변경하지 않았다.

## 검증

- 해당 Python18/18, 전체 Python82/82, diff 공백 검사 PASS. 실제 실패 프로세스의 argv/환경/stdin/stdout/stderr 테스트 문자열은 비공개 로그에만 남고 공개 오류에 없음을 검사한다. 성공 경로 실행 인자와14단계 순서, timeout/OSError, 악성·커스텀 단계 거부, main의 실제 음수 종료 출력을 확인한다. 기존 검사를 삭제하거나 약화하지 않았다.
- 공식 Claude Opus5.5 Max 독립 소스 검토 PASS/P1·P2 없음. [실제 호출 메타데이터와 결과](independent_review.json). SOURCE_ONLY/TESTS_NOT_RUN이며 Codex가 실행을 검증했다.
- SHA256 `341c279bbd5181e1508231e96a4d1c88e508e99f81db37e022f0265a16675075`의 승인된 합성 번들을 검증한 뒤, macOS .NET10.0.401에서 fresh HEAD 파일 이관·지원 HTTP 하위 과정을 직접 실행해 PASS했다.
- 설치된 Docker의 PostgreSQL16.15 이미지로 새 격리 인스턴스와 전용 두 DB를 만들고 등록 런타임의 파일/PG 소유자 CLI 이관과 지원 HTTP 하위 과정도 PASS했다. 실제 자격/계정·기존 DB는 사용하지 않았고 테스트 인스턴스를 종료/제거했다. 새 이미지·프로젝트 의존성을 설치하지 않았다.
- 위 로컬 재현은 macOS 관리 코드와 Docker PostgreSQL 하위 검증이며 **Linux 전체 CI gate 실행으로 취급하지 않는다**. 최초 Linux 실패는 로컬에서 재현되지 않았다. 최종 GitHub head의 전체 CI가 별도 기준이다.

게임 UI 소스는7bcb69f와 같다. 앞선 Unity752 PASS/0 FAIL/11 Explicit NOT_RUN, 표적63/63·Edit10, 최종 Mac/Android 네이티브 수용은 [모바일 보고서](../mobile_layout_20261023/README.md)를 따른다. 개인 저장·비공개 자격·원시 로그는 커밋하지 않는다.
