# Codex 이미지·SSH·Unity 검토 단위의 독립 리뷰

이 작업은 Codex가 작성·생성한 현재 HEAD 2b2d8ca 단위의 반대 AI 검토다. Read/Glob/Grep만 사용하며 코드·이미지 수정·생성·커밋·푸시·다른 AI 호출은 하지 않는다. Opus5.5, 기존 구독 포함 사용량만 허용한다. 다른 Claude의 MCP/CI 작업 및 Codex의 서버 UI 검증과 독립적이다.

핵심 요구는 사용자 승인한 `aiimg`의 안전한 stdin/stdout SSH PNG 전송, 실패·충돌 시 원본 파일 보호, 자동 시작 작업 유지, Codex 생성 이미지 9종의 개발 후보·출처 보존, 별도 Unity 검수 씬이다. 원본 캐릭터·나이·설정·수치·씬·빌드 설정은 바꾸지 않는다. 캐릭터 의견은 자유롭게 제시하되 비성적 캐릭터의 존재나 막연한 약관 추정을 이유로 승인된 명세 삭제/변경을 요구하지 않는다. 구체적 일관성/품질 근거는 제시할 수 있다. 이미지 생성·내용 수정 담당은 Codex다.

## 검토 범위와 근거

- `tools/ai/aiimg.zsh`
- `tools/ai/build_art_review_scene.cs`
- `reports/starter_art_20261017/`의 수용 보고·실제 기록
- `art/generated/starter-20261017/`의 선택 이미지와 `art/provenance.csv`의 이번 14행
- 별도 씬 `client/MemoryForgeUnity/Assets/Scenes/MemoryForgeArtReview.unity`와 이번 임포트 메타데이터

Codex가 이미 발견해 고친 두 문제를 그대로 재발견했다는 이유만으로 결함으로 세지 않는다. 현재 HEAD에서 확인한다:
1. 고정 ED25519 지문·전용 known_hosts·GlobalKnownHostsFile=/dev/null, PNG 검증 및 Python os.link로 정확한 목적지 원자 게시. 기존 파일/디렉터리/심볼릭 링크에 덮어쓰기 금지, 오류·중단 임시파일 정리.
2. 새 씬 builder는 모든 loaded scene의 dirty 여부와 기존 목적지 씬을 import 전에 검사하며 전역 SaveAssets는 호출하지 않는다. inactive additive dirty scene 실제 거절 시험을 통과했다.

원본 design/자료 전체 재탐색이나 범용 개선 제안은 하지 않는다. 저장된 증거를 실기기·최종 마스터·전투 아트 적용 완료로 확대하지 않는다. 실제 도구 실행 불가능은 NOT_RUN이다. 원격 Windows 생성 모델은 로컬 ComfyUI이고 Codex/Claude 대화 모델 fallback과 다른 경로다. 이후 사용자는 이미지 생성을 Codex로 지정했으므로 새 Comfy 생성은 하지 않는다.

## 출력

검토 1회로 영향 큰 실제 문제만 파일:행·재현 조건·최소 수정안을 보고한다. 통합 차단 결함/승인 범위 위반과 선택적 개선을 구분한다. 현 코드에 해당 문제가 없으면 그 사실과 검증 한계를 짧게 보고한다. 이번 작업은 리뷰이므로 테스트·SSH·MCP를 실제 실행했다고 주장하지 않는다.
