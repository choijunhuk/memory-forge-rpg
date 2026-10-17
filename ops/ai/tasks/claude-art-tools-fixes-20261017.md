# 독립 리뷰의 실제 아트 도구 결함만 수정

현재 아트 생성물/내용은 변경하지 않는다. Opus5.5·구독 포함 사용량만 사용하며 재귀 호출·이미지 생성·커밋·푸시 금지. 다른 작업자가 있으므로 책임은 `tools/ai/aiimg.zsh`와 `tools/ai/build_art_review_scene.cs` 두 파일이다. 이전 CI 변경은 유지하며 손대지 않는다. 읽기/편집만 가능한 이번 호출에서는 시험을 실행했다고 하지 않는다. Codex가 기존 독립 fault harness와 실제 Unity MCP로 검증한다.

Claude의 독립 리뷰를 Codex가 비판적으로 검토했다. 다음의 좁은 실제 문제만 고친다:
1. builder가 AssetDatabase만 확인하면 아직 import되지 않은 디스크의 기존 `.unity` 파일을 덮어쓸 수 있다. 모든 loaded dirty scene 거절과 목적지 AssetDatabase guard를 유지하고, Application.dataPath에서 계산한 정확한 목적지 디스크 파일도 import/Save 전에 거절한다. Scene/Prefab/원래 build settings를 바꾸거나 전역 SaveAssets를 추가하지 않는다. 기존 한국어 font 존재 확인도 importer 변경 전으로 옮겨 실패 시 import 수정부터 남기지 않게 한다. Prefab Stage 보호는 실제 데이터 손실 재현이 없으므로 이번 범위를 키우지 않는다.
2. aiimg의 header/file/sips 검사만으로는 truncated PNG를 확실하게 거절하지 못한다. 외부 라이브러리 없이 기존 Python3 표준 라이브러리로 PNG chunk 길이/CRC/IHDR 크기/IEND·trailing/truncation을 검증하고, 기존 sips/file도 필요 범위에서 유지한다. 일반 PNG를 잘못 거절하거나 사용자 이미지 치수를 고정하지 않는다. Python3 존재는 원격 생성 전에 확인한다.
3. 전송과 검증까지 완료된 PNG가 목적지 link 실패 때 임시파일 trap으로 사라지지 않게 한다. 빈/불완전 전송 실패는 기존대로 정리하고, 완료된 유효 PNG의 게시 실패는 보존 경로를 명확하게 알린다. 충돌 시 기존 파일·디렉터리·symlink 미덮어쓰기, exact os.link 게시, 고정 SSH 지문·stdin/stdout·no browser/scp, Preview 및 signal 처리는 유지한다.

새 의존성/범용 엔진/다른 파일 편집은 하지 않는다. 추가된 검증이 필요한 실제 분기와 Codex가 시험할 회귀 조건을 짧게 보고한다. 리뷰의 자동 시작 우려는 채택하지 않는다: 사용자는 wrapper가 필요시 ComfyUI를 켠다고 명시했고 Codex가 실제 wrapper의 시작·자신이 시작한 프로세스만 종료하는 경로를 검토했다. ComfyUI/OpenWebUI 작업은 Disabled로 그대로 둔다. 캐릭터의 선택적 일관성 의견은 기록만 하며 생성은 Codex 담당이다.
