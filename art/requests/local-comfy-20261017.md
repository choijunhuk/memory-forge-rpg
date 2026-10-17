# 로컬 ComfyUI 아트 제작 — 2026-10-17 사용자 승인

사용자는 Windows의 기존 ComfyUI를 Codex가 Mac 터미널의 SSH로 호출해 이미지셋을 제작하고, 품질이 부족하면 Codex 구독 이미지 도구로 전환하도록 명시적으로 승인했다. 이미지 생성·내용 수정 담당은 계속 Codex다. 이 승인은 새 유료 서비스·API 크레딧 사용 허가가 아니다.

- 요구사항: v0.8.1 §9 인물 외형과 §15 아트 제작. 기존 10명·나이·의상·무기·수치를 유지한다.
- 역할: 첫 플레이 화면에 필요한 캐릭터 시트를 먼저 검수하고, 실제 고정 참조가 마련된 뒤 표정·키아트·카드·SD를 만든다. 미확정 시트를 승인된 참조라고 부르지 않는다.
- 완료 기준: SSH ED25519 지문 대조, stdin 프롬프트/stdout PNG 전송, PNG 형식·크기 확인, 실패 파일 정리, macOS Preview 열기, 시안의 별도 보관 및 provenance 기록. 최종 PSD·리그·애니메이션·게임 적용은 각각 별도 검증한다.
- 범위: `tools/ai/aiimg.zsh`, `tools/ai/check_aiimg.zsh`, `art/`의 시안·기록, 사용자 Mac의 전용 함수·고정 지문 파일. 기존 게임 코드·데이터·자산을 덮어쓰지 않는다.

## 터미널 사용

이 Mac의 `~/.zshrc`가 `~/.config/aiimg/aiimg.zsh`를 읽는다. 현재 열린 터미널에는 한 번 실행한다.

```zsh
source "$HOME/.config/aiimg/aiimg.zsh"
aiimg "A fox under a blue umbrella"
```

완성된 PNG만 `~/Downloads/ai-YYYYMMDD-HHMMSS.png`로 게시하고 Preview로 연다. 동시 저장 시 숫자 접미사로 기존 파일을 보존한다. SSH가 비밀번호를 요구하거나 연결·생성이 실패하면 오류로 종료한다. 실패한 임시 파일은 정리한다. 새 인증키를 만들거나 비밀번호를 저장하지 않는다.

`~/.ssh/known_hosts_aiimg`는 사용자가 전달한 ED25519 지문과 대조한 전용 신뢰 파일이다. 개인 SSH 주소와 원격 경로는 이 사용자가 요청한 설정이며 다른 개발 환경에 자동 설치하지 않는다. 웹 브라우저·별도 scp를 사용하지 않는다. Windows ComfyUI/OpenWebUI 자동 시작 작업은 계속 비활성 상태로 두고, 생성 명령이 필요할 때 시작하는 로컬 프로세스만 사용한다.

현재 설치는 Mac의 기존 Python3 표준 라이브러리 `os.link`를 사용해 결과를 정확한 파일 경로에 게시한다. 새 Python 패키지를 설치하지 않았다. 시스템 known-hosts 파일은 사용하지 않는다. 첫 생성·선별·실제 Unity 검수 결과는 `reports/starter_art_20261017/README.md`에, 선택된 9장은 `art/generated/starter-20261017/`에 있다.

검사: macOS에서 `zsh -n tools/ai/aiimg.zsh tools/ai/check_aiimg.zsh`, `zsh tools/ai/check_aiimg.zsh`. 후자는 SSH·Preview를 대역으로 바꾼 독립 검사이므로 실제 Windows 생성 성공을 뜻하지 않는다.
