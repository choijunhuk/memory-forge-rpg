# 기억 공방 / Memory Forge — ALL-IN-ONE

**이 ZIP 하나로 시작한다.** 배포판 0.8.1-aio.2 / 게임 설계 v0.8.1 / 실행 데이터 0.7.0.

## 할 일 하나

Codex에서 이 `memory-forge-rpg` 폴더를 열고 `START_HERE.txt`를 읽고 시작하라고 지시한다. Codex가 설치·인증·권한을 확인한 뒤 구독으로 로그인된 Claude Code CLI를 직접 호출하도록 통합했다. 사용자 환경에서 실제 호출을 시험한 것은 아니며, 막히면 필요한 확인만 보고하도록 했다.

- 전체 PDF: `Memory_Forge_Complete_Design.pdf`
- 모든 시작/운영 기준: `docs/AI_WORKFLOW.md`
- 코드·데이터·HTML: 기존 각 폴더에 전부 포함
- 이번 검증과 원본 대조: `reports/all_in_one/`

**기존 구독 포함 사용량만. API·추가 사용 크레딧·종량제·자동 충전 금지. 이미지 생성은 Codex 고정.** 추가 과금 설정과 구독 인증을 따로 확인해야 하며 이 문서가 계정 설정을 바꾸는 것은 아니다.

이미 작업한 Git 저장소가 있으면 기존 작업을 삭제하거나 ZIP으로 덮어쓰지 않는다. 새 ZIP은 비교용으로 풀고 지침과 필요한 파일만 병합한다. 이전 다운로드/프롬프트는 개발 입력으로 다시 조합하지 않아도 된다.

## 현재 게임 상태

JS 전투·탐사·legacy 저장·모집/성장 참조와 HTML이 있다. 저장 v2는 독립 모듈이며 Run/HTML/서버에 미연결이다. C# Battle/Run 전체, Unity, 서버 애플리케이션, 실아트/음성/운영 결제는 미완성이다. 원본 게임 23장·카드·캐릭터·수치는 그대로 보존했다.

## 검사·빌드

```sh
npm run verify:manifest
npm test
npm run test:proposals
python3 -m unittest discover -s tests -v
npm run test:docs
npm run test:handoff
```

`npm run docs`는 기존 게임 본문 생성과 최신 실행 안내 병합을 함께 수행한다. 로컬 한국어 글꼴·PDF 라이브러리가 필요하며 글꼴 바이너리는 배포하지 않는다. `docs:game`은 게임 본문만, `docs:legacy`는 과거 참고본만 생성한다. 기존 검증 로그는 이력이고 현재 실행 증거와 혼동하지 않는다.

GitHub 원격 비공개 생성은 Codex 시작 지시문에 포함되어 있지만, 이 패키지를 만드는 과정에서 실제 원격 저장소를 만든 것은 아니다.
