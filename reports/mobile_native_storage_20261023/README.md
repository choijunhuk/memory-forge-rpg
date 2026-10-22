# macOS 네이티브 계정 저장 연결 보정

실제 Mono PC 플레이어는 첫 계정 연결에서 상대 `libc` 이름을 Unity 플러그인으로 찾다가 멈췄다. macOS Editor 또는 실제 macOS Player에서만 `/usr/lib/libSystem.B.dylib`를 사용한다. Linux/Windows Editor가 macOS를 대상으로 빌드할 때는 기존 경로를 유지한다. 잠금·0600 저장·검증·정확한 요청 ID/본문 재시도는 변경하지 않았다. PC 개발 빌드는 HTTP 허용을 DevelopmentOnly로 명시하여 Android 빌드 설정에 의존하지 않는다.

## 검증과 독립 검토

- Unity Outbox17/17 PASS. 실제 PC Mono에서 libSystem·flock·fchmod를 찾고 연결 실패 UI가 정상 응답했다.
- 격리된351 백업 사본에서 실제 UI 표준 모집1회, 영수증 저장과 요청 파일 정리, 정상 앱 재시작·재인증 후 같은 결과 유지를 확인했다. 티켓6→5·모집23→24·결정900/유료0이며 기존351 영수증·원장·전투/성장·원본 백업을 보존했다.
- 사본 전체 저장 verifier PASS, 검사 전후 바이트·수정 시각 동일. 사본의 새352는 누락된 과거 실제352의 복구와 구분한다. 원래 임시 DEV 서버/저장/인증 파일은 현재 없고351 원본 백업과352 응답/체크섬 증거를 유지한다.
- 공식 Claude Opus5.5 Max, 세션 `0b945635-9312-4281-9805-46aeafb1f557`, process0·is_error=false·modelUsage는 Opus5.5만·구독 계정 일치. 이 저장/HTTP 변경에는 신규 P1/P2가 없다고 검토했다. 전체 UI 검토는 별도 버림 수 표시 P2로 FAIL이므로 이 체크포인트를 UI 최종 승인으로 세지 않는다.

이전 UI 체크포인트bada9d1의 push/PR CI는 모두 PASS다. 이 커밋의 정확한 head 검사는 GitHub checks에서 판정한다. 실기기 재설치·Windows/Linux Unity 플레이어는 이번 범위에서 실행하지 않았다. 개인 저장·자격·원시 로그·캡처는 커밋하지 않는다.
