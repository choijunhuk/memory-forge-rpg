# ENV-CLAUDE-UNITY-MCP

기존 구독 인증·추가 사용 OFF·계정 일치를 Codex가 로컬에서 검증한 뒤, 공식 Claude Code Opus High가 실제 Unity MCP 읽기 호출을 수행한다.

- Unity instance: `MemoryForgeUnity@4034a4e2fe236d6e`.
- `set_active_instance`로 이 인스턴스를 선택하고 `read_console`의 action get, types error, count 10을 직접 호출한다.
- 승인 도구는 이 두 MCP 도구뿐이다. 파일 읽기·편집, 씬·게임오브젝트·프리팹 변경, Play 실행, 외부 검색, 이미지/asset_gen, 다른 모델·에이전트 호출은 하지 않는다.
- 결과에 실제 도구 이름·인스턴스·콘솔 오류 개수와 연결 실패 사유를 짧게 남긴다. 요청문만 읽고 연결됐다고 주장하지 않는다.
- Editor 쓰기 담당은 Codex가 유지한다. 이 시험은 연결 증거이며 게임 플레이·C# 동등성 검사가 아니다.
