# 실제 Unity 전용 MCP 조작 시험

구독 포함 Opus5.5 High만 사용하며 CLI가 주는 unity-write 프로필을 사용한다. Codex가 이 작업에 같은 Unity Editor의 단독 제어를 넘긴다. 전체 문서 재독·긴 기획·파일/이미지/씬 저장·패키지/서버/과금/다른 AI 호출은 하지 않는다. 별도 네이티브 포인터 경로는 호스트 응답 오류로 보류 중이므로 Unity MCP를 통해 실제 Editor를 조작한다.

1. MCP editor/state로 실제 Unity6000.0.46f1, non-batch, MemoryForgeServerPractice 씬, 정지 상태를 확인한다. 현재 상태가 예상과 다르면 멈추고 구체적으로 보고한다(자동으로 다른 씬을 열거나 저장하지 않는다).
2. `execute_code`(method body, C#6)로 `UnityEditor.EditorApplication.isPlaying`, `isPaused`, `UnityEngine.Application.isBatchMode`, `UnityEngine.SceneManagement.SceneManager.GetActiveScene().path`를 반환해 읽는다. 파일/네트워크/비밀 접근·전역 SaveAssets는 하지 않는다.
3. manage_editor로 Play를 시작하고 실제 상태를 다시 확인한다. pause를 켜서 isPaused=true를 확인하고, pause를 다시 풀어 false를 확인한다. 마지막에는 stop으로 처음의 정지 상태로 복구한다. 각 단계의 실제 상태를 읽고, 도구가 접수됐다는 응답만으로 조작 성공을 세지 않는다.
4. Console을 action=get/types=[error]/count=5로 읽는다. Console을 지우지 않는다. 생성/수정한 파일이 없다면 그 사실을 보고한다. 코드 파일은 readonly다.

실제 도구를 쓸 수 없거나 하나가 거부되면 우회하지 않고 그 단계만 정확히 기록한다. 시작한 Play/일시정지는 가능한 한 되돌리고, 성공/실패/미실행을 분리해 짧게 보고한다. 이 결과는 Editor 제어 연결 증거이며 실제 전투 진행·Android·빌드 검증이 아니다.
