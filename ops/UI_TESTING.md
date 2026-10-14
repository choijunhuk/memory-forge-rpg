# UI 자동 시험 로딩 방식

일반 환경: `npm install`, Playwright Chromium 설치 후 `node prototype/tests/smoke.mjs 4 --shots`.

지원 환경 변수:
- `PLAYWRIGHT_MODULE`: 설치된 Playwright 패키지 경로(선택).
- `PLAYWRIGHT_CHROMIUM_EXECUTABLE`: 시스템 Chromium 실행 파일(선택).
- `MF_PROTO_URL`: 테스트 HTTP URL(선택, 기본 file URL).
- `MF_INLINE_HTML=1`: file/HTTP 탐색이 불가능한 검사 환경에서 빌드 HTML을 page.setContent로 로드. 시드는 하네스에서 주입.

인메모리 방식은 파일/HTTP 호스팅·원점별 localStorage·네트워크/서비스워커를 검증하지 않는다. 실제 배포용 로딩을 대신할 수 없다. 이번 UI 통과는 이 방식이며 실제 화면상 상단 체력 겹침 검토가 남아 있다.
