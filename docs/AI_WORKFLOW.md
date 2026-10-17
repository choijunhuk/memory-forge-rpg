# Claude·Codex 실행·과금·이미지 제작 기준

배포판: 0.8.1-aio.2 / 2026-10-15. **게임 설계 v0.8.1, 실행 규칙 데이터 0.7.0은 유지한다.** 이 파일은 협업 실행 규칙이며 게임 밸런스 변경 문서가 아니다.

## 1. 사용자에게 필요한 시작 동작

새 작업이면 ZIP을 새 폴더에 풀고 `memory-forge-rpg`를 Codex 프로젝트로 연다. 첫 메시지로 루트 `START_HERE.txt`의 전체 내용을 전달하거나 그 파일을 읽고 시작하도록 지시한다. PDF·코드·시제품·프롬프트가 같은 ZIP에 있으므로 다른 ZIP을 추가로 조합하지 않는다.

이미 작업한 저장소라면 새 폴더를 비교용으로 열고 기존 커밋·미커밋 수정·생성 이미지·인증을 보존한 채 필요한 지침만 병합한다. 기존 저장소를 삭제하거나 ZIP을 덮어풀지 않는다. 이전 다운로드는 참고 보관본으로 두어도 되고 개발에 다시 넣지 않아도 된다. 자동 삭제는 수행하지 않는다.

## 2. 최우선 계약

[ALL-IN-ONE 최우선 실행 기준 / 2026-10-15]
이 지침은 사용자 지정인 구독 전용·Claude 자동 호출·Codex 이미지 담당을 실제 실행 단계에 적용한다. 게임 설계·수치·나이 변경 권한은 추가하지 않는다.

- 현재 실행 지침은 AGENTS.md → docs/CURRENT.md → docs/AI_WORKFLOW.md → docs/START_CODEX.md 또는 docs/START_CLAUDE.md다. 게임 명세는 docs/design_v0.8.1.md다. 예전 수동 인계 안내와 이미지 공급자 선택지는 이번 실행 지침으로 대체한다. archive/review의 과거 지침은 현재 명령이 아니다.
- Claude와 Codex 모두 현재 구독의 포함 사용량만 사용한다. API 키, Console, 별도 API 크레딧(무료·프로모션 포함), Usage credits/Extra usage, 기존에 구매해 둔 추가 크레딧의 소모, 자동 충전, 종량제·타 유료 AI 경로를 사용하지 않는다. 게임의 개발/운영 서버 비용은 별도 승인 범위이며 이 지침이 무료 서버를 보장하지 않는다.
- 자동 호출 전에 인증 방식과 초과 과금 설정을 각각 확인한다. Claude의 claude auth status에서 logged-in 여부뿐 아니라 실제 인증 방식이 구독 로그인인지 확인한다. API 키·토큰·프로필·게이트웨이·클라우드 연결의 존재와 유형만 확인하고 비밀값은 추출·복사·출력하지 않는다. API 경로가 섞였으면 해당 자식 프로세스 환경에서만 분리하고 재검증한다. 전역 설정이나 다른 프로젝트의 키를 임의 삭제하지 않는다.
- Claude의 Settings > Usage에서 Usage credits/Extra usage가 비활성화됐다는 증거를 확인한다. 자동 충전 OFF만으로는 기존 잔액 소모를 막지 못한다. Codex도 구독 로그인과 포함 사용량, 구매 크레딧·자동 충전 상태를 따로 확인한다. 로그인이 됐다는 사실만으로 구독 전용 청구가 증명되지는 않는다. 확인 도구가 없으면 필요한 설정 확인만 사용자에게 요청하고, 과금 경로가 불명확한 모델 호출은 하지 않는다. 이미 확인한 조건을 매 작업마다 다시 묻지 않고 계정·환경·설정 변경 시 재검증한다.
- 설치된 CLI의 --help를 먼저 읽는다. 공식 Claude Code CLI만 실행한다. 구독 토큰을 꺼내 외부 SDK나 임의 HTTP 클라이언트에 전달하지 않는다. --bare는 사용하지 않는다. 구독만 쓰기 위한 보조 로그인 제한 설정은 현재 버전의 공식 명세와 기존 설정을 확인한 후 적용하며, 그것을 초과 과금의 완전한 차단 장치라고 부르지 않는다.
- 모든 조건이 확인된 뒤에만 읽기 전용 짧은 claude -p 호출을 시험한다. 존재하는 docs/START_CLAUDE.md와 작업 지시 파일을 입력으로 전달하고 명시적인 작업 디렉터리·브랜치·작업 ID를 사용한다. 모델이 자기 과금 경로를 주장한 문장은 인증 증거가 아니다.
- 사용량 제한이면 해당 에이전트 작업을 보류하고 체크포인트를 남긴다. 구독 한도를 우회하거나 크레딧을 구매하지 않는다. 사용 가능한 다른 에이전트는 기존 역할 범위의 독립 작업만 계속한다. 이미지 생성은 Codex 구독 도구가 없거나 한도가 끝나면 보류한다.
- 자동 실행은 이 시작 지침을 받은 Codex가 사용자 환경에서 실제 CLI를 호출하는 방식이다. 이 ZIP 자체가 백그라운드 서비스를 설치하거나 네 PC 로그인·추가 과금 설정을 바꾼 것은 아니다. 호출과 결과 수신이 확인되기 전에는 연결 완료라고 보고하지 않는다.

## 3. 실제 Claude 자동 호출 절차

1. **설치·실행 환경:** Codex의 터미널이 실행되는 OS/사용자/실제 디렉터리를 확인한다. 데스크톱 앱 로그인과 이 터미널의 Claude Code 로그인은 동일하다고 추정하지 않는다. 로컬·SSH·컨테이너 환경을 구분한다.
2. **비추론 사전 검사:** 설치 CLI 경로, `claude --version`, `claude --help`, `claude auth status`로 인증 유형을 확인한다. API/게이트웨이/클라우드 설정 존재 여부, 프로젝트의 훅과 MCP 명령을 검토한다. 상태 명령 성공은 구독 잔량·추가 과금 OFF·모델 권한까지 입증하지 않는다. [S1–S4]
3. **과금 차단 확인:** 기존 구독 로그인, 추가 사용 비활성, 별도 구매 크레딧 소비 방지 조건을 확인한다. 계정 UI 접근이 없으면 실제 계정 소유자에게 필요한 설정 한 번만 확인받는다. 확인되지 않았는데 짧은 유료 가능 호출로 실험하지 않는다.
4. **작업 공간:** 기존 원격·브랜치·커밋을 확인한다. 원격이 없으면 인증된 계정의 `memory-forge-rpg` 비공개 저장소를 초기 구성하도록 시도한다. 동명 저장소를 덮어쓰지 않는다. Claude용 worktree는 동일 기준 커밋에서 만들며 OS 임시 경로를 하드코딩하지 않는다.
5. **짧은 읽기 시험:** 공식 CLI에서 지원하는 `-p`와 구조화 출력 옵션을 사용한다. 기본권한을 전부 해제하지 말고 첫 시험은 읽기만 허용한다. 저장소 지침과 작은 작업 요청을 표준입력 또는 현재 CLI에서 지원하는 방식으로 전달한다. 원문 요청·시간·작업 디렉터리·출력·종료 상태를 기록한다. 토큰값은 기록하지 않는다. [S2,S3]
6. **작업 배정:** 시험 후에는 범위가 작은 UI/UX·콘텐츠·리뷰 작업을 한 번에 하나씩 맡긴다. 기본 동시 Claude 작업자는 1명이다. 쓰기 작업에는 해당 worktree와 파일 범위의 필요한 권한만 부여한다. 승인 대화가 나타날 수 없는 -p 실행에서 막히면 원인을 보고하고 무제한 권한으로 우회하지 않는다.
7. **회수·검증:** `session_id`, 작업 ID, 기준/결과 커밋, 변경 파일, 검증 기록을 남긴다. 다른 worktree의 파일을 썼다고 자동으로 합쳐졌다고 보지 않는다. Codex가 diff·시험을 확인한 뒤 충돌 없이 병합한다.
8. **반복·중단:** 같은 작업의 후속 요청만 해당 session_id로 이어간다. 시간 제한이나 사용량 한도이면 프로세스·변경 내용을 확인하고 중복 실행 없이 체크포인트를 남긴다. 두 AI가 서로를 다시 호출하는 무한 구조를 만들지 않는다.

이 ZIP은 실제 사용자 컴퓨터에서 Claude·Codex·Unity MCP에 로그인하거나 명령을 실행한 산출물이 아니다. 위 절차를 실행하는 담당은 Codex이며, 연결 성공 여부는 사용자 환경의 실제 결과로 보고한다.

## 4. 작업 인계 파일

`ops/ai/tasks/`에 작업별 요청과 결과를 보관한다. 계정 식별자·토큰·결제정보는 넣지 않는다. 작업 요청에는 task_id, 기준 커밋, 상대 worktree, 허용 파일, 목표, 완료 시험, 의존성을 담는다. 결과에는 session_id, 실제 변경, 시험 로그, 막힌 점을 담는다. 완료되지 않은 작업은 완료 상태로 승격하지 않는다. Unity 편집권은 프로젝트·씬별로 한 명만 소유한다.

## 5. 이미지 생성은 Codex 고정

캐릭터·키아트·카드·배경·적·스킨·UI 그림의 생성, 재생성, 인페인팅과 내용 수정은 Codex만 담당한다. Claude는 아트 브리프, 요청서, 생성물 검수, 비생성형 리사이즈·압축·슬라이스·Unity 임포트를 담당한다. 원본 이미지 파일을 보존한다.

Codex라는 이름의 스킬이라도 별도 유료 이미지 API/크레딧이 필요하면 실행하지 않는다. 현재 구독에 포함된 이미지 기능이 실제로 있는지 확인한다. 없으면 `BLOCKED_IMAGE_SUBSCRIPTION`으로 남기고 독립 작업을 진행한다. Claude·다른 이미지 서비스·로컬 모델로 자동 전환하지 않는다. 실제로 존재하지 않는 참조 이미지를 '승인된 시트'라고 쓰지 않는다.

자산의 source는 `art/briefs/prompts_v0.8.1.json`과 설계 9·15장이다. 디자인 시트 → 표정/키아트 → 카드·배경 순서로 검수한다. `art/provenance.csv`에 사용 프롬프트·참조·도구·결과 경로를 남긴다. 평면 PNG와 레이어 PSD·리그·애니메이션은 별도 산출물이다. 임시 도형은 최종 아트가 아니다.

## 6. 자율성·승인 경계

일반 구현, 회귀시험, 허용된 UI/의상 변주, 브랜치와 작업 분배는 자율 진행한다. 문서의 기존 내용·카드·캐릭터를 작업 편의를 위해 축소하지 않는다. 기획만 반복하지 않고 실제 첫 구현까지 진행한다.

가격·확률·재화 총량·나이·영구 저장 의미를 새로 바꾸거나 외부 유료 서비스, 실결제, 운영 데이터 접근·배포·공개 전환을 하려면 별도 명시적 결정이 필요하다. 이미 목표로 명시된 저장 v2 등의 구현과 새로운 정책 변경을 구별한다. 이미지 담당/구독 전용 조건도 사용자 변경 지시 없이는 바꾸지 않는다.

## 7. 검사·현재 상태

입력 파일을 바꾸기 전에 `npm run verify:manifest`로 `MANIFEST_AIO.json`과 `MANIFEST.sha256`를 대조한다. 기존 v0.8.1 매니페스트는 원본 비교용이다. 새 검사 기록 때문에 원래 체크섬이 달라지면 그 차이를 기록하며, 입력 검사를 통과시키려고 처음부터 체크섬을 재생성하지 않는다.

```sh
npm run verify:manifest
npm test
npm run test:proposals
python3 -m unittest discover -s tests -v
npm run test:docs
npm run test:handoff
```

`npm run docs`는 게임 본문을 재생성하고 최신 실행 안내를 앞에 합친 전체 PDF를 만든다. `docs:game`은 게임 본문만 만드는 명령이다. 완성된 현재 PDF는 루트 `Memory_Forge_Complete_Design.pdf`다. 게임 원본 154쪽은 `docs/Memory_Forge_v0.8.1_Restored_Design.pdf`에 따로 보존되어 있다.

게임의 현행 상태: JS 전투·탐사·legacy 저장·모집/성장 참조와 HTML. 저장 v2는 독립 모듈만 있고 실제 Run/HTML/서버에는 미연결이다. C# Battle/Run 전체, Unity 클라이언트, 서버 애플리케이션, 실아트·음성·운영 결제는 미완성이다. 업적 상세·현행 패스 50레벨 배정·무기 전승의 남은 결정도 계속 작업 목록에 둔다.

현재 검증 결과는 `reports/all_in_one/verification.json`에 한정한다. 과거 PASS를 이번 재실행 결과로 합산하지 않는다. 사용자 구독 로그인·실제 Claude 호출·추가 과금 설정·실기기·운영 서버는 이번 패키지 제작에서 확인하지 않았다.

## 8. 첫 개발 순서

Codex: 기준선 검증 → 구독 확인 후 Claude 읽기 시험 → 저장 v2 안정 개체키/변경목록/전체용량/원자성/이관과 HTML 실제 연결 → 후반 적 행동 → 같은 조건의 밸런스 시험 → C# Battle/Run → 서버.

Claude: 전체 10명 시트와 아트 브리프 → 저장 v2 전후비교 UI/취소/통신 실패 → Codex용 이미지 요청 → 4~6장 서사/후반 콘텐츠 → Unity UI와 생성 리소스 연결. 공유 파일과 Unity Editor 동시 쓰기는 피한다.

## 9. 공식 참고와 적용 한계

아래는 2026-10-15에 확인한 제품 동작 자료다. 게임 수치의 근거가 아니며 계정 설정을 대신 확인해주지 않는다. CLI 버전·계정 화면·조직 정책이 다르면 설치 환경에서 다시 대조한다.

- [S1] Claude Code: Authentication — https://code.claude.com/docs/en/authentication
  구독 로그인과 API/클라우드/프로필 인증을 구분한다. -p 모드에서 API 키가 있으면 사용될 수 있다.
- [S2] Claude Code: Run programmatically — https://code.claude.com/docs/en/headless
  -p 비대화형 실행. --bare는 구독 로그인을 사용하지 않는다. 일반 -p는 훅과 MCP 설정을 읽으므로 실행 전 검토한다.
- [S3] Claude Code: CLI reference — https://code.claude.com/docs/en/cli-reference
  claude auth status의 인증 방식, -p, --resume 및 구조화 출력은 설치 버전의 도움말과 대조한다.
- [S4] Claude: Manage usage credits for paid plans — https://support.claude.com/en/articles/12429409-manage-usage-credits-for-paid-claude-plans
  Usage credits가 켜져 있으면 포함 사용량 소진 후 종량제로 이어질 수 있다. 비활성화와 자동 충전 OFF는 구별한다.
- [S5] OpenAI Codex: Authentication — https://developers.openai.com/codex/auth
  ChatGPT 로그인과 API 키 로그인은 별개 경로다. 인증 파일에는 비밀값이 있으므로 출력하지 않는다.
- [S6] OpenAI: Using Credits for Flexible Usage — https://help.openai.com/en/articles/12642688-using-credits-for-flexible-usage-in-chatgpt-personal-plans
  포함 사용량 소진 후 구매 크레딧이 사용될 수 있으며 자동 충전은 별도다. 포함 한도를 넘는 소비는 이번 프로젝트에서 금지한다.

## 10. 2026-10-15 추가 사용자 지시 — 모델·품질·지속 개발

이 절은 위 배포판 기록과 기존 자동 생성 역할표를 보존하면서, 충돌하는 모델 선택·노력 수준·중단 조건에 우선한다. 게임 본문 v0.8.1, 실행 데이터 0.7.0, 기존 수치·정책·원본 파일을 바꾸는 권한은 추가하지 않는다. 실제 설정과 설치·인증·호출·Unity 연결 증거는 [AI_ENVIRONMENT.md](AI_ENVIRONMENT.md)를 따른다. 지시문에 모델이나 노력을 적은 것만으로 실행 설정이 적용됐다고 보고하지 않는다.

### 모델과 노력 수준

- Codex는 **`gpt-6.1-sol`만**, Claude는 **Opus만** 사용한다. 현재 공식 Claude CLI의 명시 모델은 `claude-opus-5-5`다. Astra·Fable·Luna·그 밖의 모델, 자동 모델 라우팅·fallback·저가 모델 전환은 금지한다. 설치된 역할 프롬프트·스킬이 다른 모델을 지정해도 이 프로젝트에서는 따르지 않는다. Codex 네이티브 자식 작업도 허용 모델을 명시하고 실제 모델 증거를 확인한다.
- 두 계정의 기존 **5배 구독 포함 사용량** 안에서 품질을 우선한다. 토큰·시간 절약을 이유로 모델·검토·시험 수준을 낮추지 않는다. API/Console/별도 구매 크레딧(기존 잔액 포함)/Usage credits·Extra usage/자동 충전/외부 종량제는 계속 금지한다. 한도에 걸린 작업만 체크포인트와 함께 보류하고, 구독 경로가 확인된 독립 작업을 이어간다.
- 기본 노력은 **High**다. Medium 이하를 관성적으로 선택하지 않는다. Codex의 핵심 전투·예약·카드/부품·기억 저장·경제·인증·서버·이관·아키텍처·복잡한 버그·중요 계획·최종 검증에는 **Max를 적극 사용**한다. Claude도 종합 계획·서사·캐릭터 설계·최종 검토에 **Max를 적극 사용**한다. 담당 AI가 위험과 복잡도에 맞춰 자율적으로 선택하며 매번 사용자 승인을 구하지 않는다.
- 설치 버전의 실제 지원값을 먼저 확인한다. Codex 0.162.1은 `model_reasoning_effort = "high"`/`"max"`를 지원한다. Claude Code 2.1.285는 `--model claude-opus-5-5 --effort high` 또는 `--effort max`를 지원한다. Claude의 프로젝트 기본 High와 세션별 Max를 구분하고, `max`를 지원하지 않는 영구 `modelSettings` 필드에 억지로 저장하지 않는다. 적용 상태는 실제 CLI 인자·유효 설정·세션/결과 로그로 확인한다.

### 실행과 독립 검토

- Codex가 같은 환경의 공식 Claude Code CLI를 직접 설치 상태 확인·호출·회수·검증한다. 기존 구독 인증과 추가 과금 OFF 증거를 먼저 확인하고, 승인된 별도 branch/worktree와 파일 범위에서 실행한다. `--bare`, 구독 토큰 추출·SDK/API 재사용, Codex/Claude의 재귀 호출을 금지한다. 실제 `session_id`, 모델, 노력, 종료 상태, 변경 파일과 검증 근거가 없으면 호출·협업 완료로 기록하지 않는다.
- 주요 작업을 시작하기 전에 요구사항 출처와 절, 게임에서의 역할, 완료 조건, 담당·허용 파일을 저장소의 작업 기록에 적는다. 요구사항 상태는 구현·시험·독립 검토·통합·보류를 구분해 추적한다.
- 반복 순서는 **개발 → 관련 시험 → 반대편 AI의 독립 검토 → 수정 → 재검증 → 통합 → 다음 작업**이다. Codex 구현은 Claude가, Claude 구현은 Codex가 원문 요구사항과 실제 diff를 독립적으로 검토한다. 검토자는 근거가 있는 결함·제안만 제시하고 작업 범위를 임의 확장하지 않는다.
- 핵심 결함·명세 위반은 수정하고 재검토하기 전까지 통합을 막는다. 같은 쟁점이 두 차례에도 풀리지 않으면 근거·대안·남은 위험을 기록하고 해당 항목을 보류한 채 독립 작업으로 이동한다. 이 제한이 미해결 결함의 통합을 허용하지 않는다. 자동 시험 PASS를 Unity Play·실기기·서버/DB·결제 수용으로 확대하지 않는다.

### Unity와 이미지

- Unity MCP를 연결 가능한 실제 Editor에서 적극 사용해 Scene·Prefab·UI·Animation·Console·Play 시험·임포트·빌드를 확인한다. 설치·등록만으로 연결 성공을 선언하지 않고 프로젝트 식별·실제 도구 응답·실행 결과를 기록한다. 무료 로컬 도구만 사용하며 유료 `asset_gen` 공급자나 외부 AI 과금으로 전환하지 않는다.
- 같은 Unity Editor의 씬·프리팹 쓰기는 지정된 한 담당자만 수행한다. 다른 작업자는 읽기와 독립 파일 작업만 진행하고, 별도 worktree나 MCP 세션을 동시 쓰기 권한으로 해석하지 않는다.
- 이미지 생성·재생성·내용 수정은 **Codex 고정**이다. Claude는 브리프·검수·비생성형 후처리·리소스 연결을 맡는다. 구독 포함 이미지 도구가 확인되지 않거나 한도이면 해당 자산만 보류하며 다른 모델·공급자로 자동 전환하지 않는다.

### 현재 세션의 지속 개발

P1·P2 또는 개별 작업이 끝났다는 이유로 현재 세션의 개발을 종료하지 않는다. 로드맵에서 의존성이 풀린 다음 작업을 골라 위 반복을 계속한다. 정책 미결정·권한·환경·구독 한도는 해당 작업에만 보류 사유로 기록하고 가능한 독립 작업은 이어간다. 현재 세션 밖의 자동 개발은 약속하지 않는다.

기존 비공개 원격·Git 이력·미커밋 변경·원본 설계와 전체 콘텐츠를 보존한다. 통과를 위해 기능·카드·캐릭터·시험을 삭제하거나 기준을 낮추지 않는다. 가격·확률·재화·나이·영구 자산 의미·실결제·운영 배포의 기존 승인 경계도 유지한다. 배포 당시의 미연결·미호출 설명과 검증 보고서는 이력이며, 현재 진행 상태는 `docs/CURRENT.md`와 작업별 최신 증거로 판정한다.

## 11. 2026-10-15 사용량 배분 — Claude 구현 우선

사용자가 현재 Codex 잔여량 약30%, Opus 약54%와 Opus 금요일 초기화를 알려 주어 새 작업 배분을 조정한다. 이 수치는 사용자 제공 시점의 정보이며 고정 잔량·초기화 일정을 보장하지 않는다. 이전 역할표와 충돌하면 이 절을 우선한다.

- 새 기능 구현·원인 분석·테스트 작성·상세 계획은 **공식 Claude Code Opus CLI에 우선 배정**한다. Codex는 최소한의 작업 경계 설정, Claude 변경의 독립 코드 리뷰, 최종 통합, Unity 확인과 이미지 작업에 집중한다. 이미 시작한 Codex 하위 작업은 정한 범위까지만 완료하며, 새 구현/분석을 여러 Codex 하위 에이전트로 늘리지 않는다.
- Claude가 작성한 의미 있는 변경은 Codex가, Codex가 작성한 변경은 Claude가 독립 리뷰한다. 중요 시스템 검증과 High/Max를 줄이지 않는다. Codex 잔량을 줄이기 위해 검토를 생략하거나 모델을 낮추지 않는다.
- 두 구독의 사용량은 별개다. 이는 작업을 배정하는 운영 정책이며 정확한 토큰 백분율 강제, 구독량 이전 또는 제공자 과금 설정이 아니다. [runtime-policy.json](../ops/ai/runtime-policy.json)의 `work_routing`도 작업 배분 계약이다.
- Claude 호출 전 구독 인증·Usage credits/Extra usage·자동 충전 OFF 확인과 파일/worktree 경계를 유지한다. 한도에 이르면 유료 우회 없이 해당 작업을 보류하고 체크포인트를 보존한다. Codex도 포함 한도 내에서만 독립 작업을 수행한다. 이미지 생성·내용 수정은 계속 Codex 전담이다.

## 12. 2026-10-15 추론 수준 자율 선택 — Extra High 포함

최신 사용자 지시에 따라 추론 수준을 High/Max 두 단계로 제한하지 않는다. 기본은 High이며, **High·Extra High(`xhigh`)·Max**를 작업 난이도·위험·기존 증거와 실행 결과에 맞춰 자율 선택한다. 단순 시험 작성/명확한 반복 구현은 High, 깊은 분석/복잡한 구현은 xhigh 또는 Max, 가장 어려운 문제/중대한 최종 검증에는 Max를 적극 활용한다. 이는 예시이며 특정 작업을 항상 같은 수준으로 고정하는 규칙이 아니다. 토큰 절약을 위한 불필요한 하향 대신 실제 코드 작성·독립 검증에 추론을 사용한다.

로컬 Codex 모델 카탈로그의 GPT-6.1 Sol과 [공식 Claude Code 지원표](https://code.claude.com/docs/en/model-config#adjust-effort-level)의 Opus5.5 모두 xhigh를 지원한다. Claude2.1.285 실행기의 `--effort` 허용 목록에 xhigh를 추가하고 동일 환경 변수/명시 CLI 인자/결과 기록을 통과시킨다. 구독 인증·계정 일치·과금 OFF·파일 경계·모델 고정·반환 검증은 유지한다. 기본 High 설정 파일을 바꾸는 것과 이미 실행 중인 턴의 실제 수준은 구분한다. 실제 xhigh 적용은 해당 세션 메타데이터를 확인한 뒤 보고한다.

이 절이 이전의 두 단계로 읽힐 수 있는 설명에 우선한다. Medium 이하를 관성적 절약 수단으로 쓰지 않는 품질 원칙과 Claude 구현 우선 배분, 양방향 교차 리뷰·Codex 이미지 담당·단일 Editor 쓰기 정책은 유지한다.

## 13. 2026-10-17 사용자 승인 — 로컬 ComfyUI와 품질에 따른 전환

사용자는 Codex가 자신의 기존 Windows ComfyUI를 Mac 터미널의 SSH로 호출해 이미지셋을 만들도록 명시적으로 승인했다. 이는 §5의 로컬 모델 자동 대체 금지에 대한 이 사용자 환경의 허용 경로다. 생성·내용 수정 담당은 계속 Codex이며, ComfyUI 시안이 제작 기준에 미달하면 Codex의 구독 포함 이미지 도구를 사용한다. 새 유료 서비스·API·추가 크레딧·자동 충전은 허용하지 않는다.

전용 ED25519 지문을 확인한 SSH에서 프롬프트를 stdin으로 보내고 PNG를 stdout으로 받는다. `aiimg "프롬프트"`는 Mac의 `~/Downloads/ai-날짜시간.png`를 만들고 검증 후 Preview로 연다. 실패한 임시 파일은 제거하고 기존 결과는 덮어쓰지 않는다. 웹 브라우저·별도 scp를 쓰지 않으며 Windows ComfyUI/OpenWebUI 자동 시작 작업을 다시 활성화하지 않는다. 실제 생성·파일 형식·크기와 시안 품질, Unity 적용 여부는 각각 구분한다. 구체 실행 범위와 독립 검사는 [제작 기록](../art/requests/local-comfy-20261017.md)을 따른다.

## 14. 2026-10-17 Claude 고정 MCP 프로필 — Unity·화면 조작

최신 사용자 지시: 구현·개발 도구 설치와 활용은 공식 Claude Opus CLI가, 이미지 생성·지시·독립 검증·통합은 Codex가 맡는다. Claude의 MCP는 이 절의 고정 프로필로만 연다. 코드·설정·모의 시험은 작성했지만 시험 실행과 Claude 세션의 실제 MCP 연결·조작은 NOT_RUN이다.

- **선택:** `tools/ai/run_claude.py --mcp-profile`은 `none`(기본)·`unity-read`·`unity-write`·`desktop`만 받는다. `none`은 이전과 같은 빈 strict MCP·Read/Glob/Grep/명시 Edit·JSON 결과다. 다른 이름·URL·명령 인자는 실행 전에 거절한다. 서버·허용/금지 도구는 실행기 상수와 [runtime-policy.json](../ops/ai/runtime-policy.json) `claude_mcp_profiles`에 고정하고 `tools/ai/test_handoff.py`가 대조한다. 목록 변경은 반대 AI 리뷰를 거친다.
- **로드 범위:** 프로필 서버만 `--strict-mcp-config`로 넘기고 `ENABLE_CLAUDEAI_MCP_SERVERS=false`를 둔다. 사용자·프로젝트 `.mcp.json`·claude.ai 클라우드 MCP는 읽지 않고 버전·인증 확인은 빈 MCP로 한다. 구독 인증·계정 일치·과금 UI 증거·Opus/노력·결과 모델 검사·재귀 금지는 모든 프로필에 그대로 적용된다.
- **권한:** `dontAsk`와 정확한 `--allowedTools`만 자동 허용하고 금지 도구는 `--disallowedTools`로 모델 문맥에서 뺀다. 서버 단위·와일드카드 허용은 쓰지 않는다.
- **Unity:** 기존 무료 로컬 `http://127.0.0.1:8080/mcp`만 쓴다. `unity-read`는 읽기·진단·MCP 리소스 읽기이며 Codex가 Editor writer인 동안에도 쓸 수 있다. `unity-write`는 Codex가 그 작업에 Editor 쓰기를 넘긴 경우에만 쓰고 실행기가 `ops/ai/local/exclusive-control.lock`을 배타적으로 잡는다. 잠금이 있는 동안 다른 작업·Codex는 같은 Editor를 쓰지 않는다. Editor 쓰기는 worktree가 아니라 Editor가 연 루트 프로젝트에 반영되므로 Codex가 루트 diff·Console·Play를 독립 확인한다. 유료 `asset_gen`, 클라이언트 권한을 우회하는 서버 측 일괄/사용자 도구, 임의 메뉴 실행, 패키지 변경, C#·셰이더 소스 편집 도구는 제외한다(소스는 worktree 편집 경계로).
- **화면:** Codex 전역 computer-use 명령은 설정된 상대 경로에 실행 파일이 없어 재사용하지 않았다. `desktop`은 MIT `@steipete/peekaboo@4.9.5`를 `npx -y ... mcp`로 띄운다. 같은 잠금을 공유한다(GUI로 Editor를 조작할 수 있으므로). 실행 전 macOS·`npx`·`~/.peekaboo/credentials`/`config.json` 부재(내용 미열람)를 확인하고 AI 공급자·Peekaboo 환경변수와 API 키·토큰류를 자식 환경에서 뺀다. 도구 구성은 §16의 실제 목록 교정을 따른다.
- **증거:** MCP 작업은 `stream-json`으로 받아 CLI init의 서버 상태·노출 도구, 실제 MCP 호출·오류, 권한 거부, `apiKeySource`를 결과에 남긴다. 프로필 밖 서버·도구, 금지 도구 노출, 화면 호출의 AI 분석 인자, `apiKeySource`가 `none`이 아니면 FAILED다. 서버 연결과 허용 도구 성공 응답이 함께 있어야 `connection_observed=true`이며 이것도 Editor/화면 결과의 독립 확인을 대신하지 않는다.
- **한계:** CLI 권한은 MCP 도구 이름 단위라 인자(`read_console` clear, `manage_asset` 경로, 캡처 도구의 선택적 AI 인자)를 막지 못하고 사후 감사만 한다. `desktop`은 사용자 GUI 전체를 조작할 수 있어 셸 금지·파일 경계를 GUI로 우회할 수 있으므로 작업 지시에 대상 앱·창을 적고 Codex가 호출 기록을 검토한다. 환경변수 제거·파일 부재 검사는 키체인 등 다른 저장소나 로컬 모델 사용을 막았다는 증명이 아니다. `npx`는 정확한 버전만 고정하고 의존성 무결성은 고정하지 않는다. OS 샌드박스·서버 측 과금 차단이 아니다.
- **보류:** 실제 도구 목록이 허용 도구 인자로 AI 분석·유료 경로를 노출하면 해당 도구/프로필만 보류하고 Unity 연결과 독립 작업은 계속한다.

## 15. 2026-10-17 ImagineArt 무료 크레딧 예외

사용자는 Codex가 필요할 때 ImagineArt 플러그인을 **무료 크레딧으로만** 쓰도록 명시 허용했다. ImagineArt에만 적용되는 예외로 §5의 '별도 크레딧이 필요한 도구 미실행' 규칙보다 우선한다. 기존 구매·유료 잔액, 자동 충전, 추가 결제는 금지다. 생성 전에 Codex가 무료 잔액과 실제 차감 경로를 구분해 확인하고, 확인할 수 없거나 유료 잔액이 먼저/함께 차감될 수 있으면 해당 생성만 보류한다. 사용 도구·무료 잔액 확인 근거·결과를 `art/provenance.csv`에 남긴다.

이미지 생성·재생성·내용 수정은 계속 Codex 담당이다. Claude MCP 프로필에는 ImagineArt나 다른 이미지 생성 도구를 넣지 않는다. Codex/Claude의 일반 추론은 기존 구독 포함 사용량만 쓰며 §2의 API 크레딧(무료·프로모션 포함) 금지는 추론 경로에 그대로 적용된다. 모든 대화 모델 고정(`gpt-6.1-sol`, `claude-opus-5-5`)과 GitHub Private 정책을 유지한다.

## 16. 2026-10-17 최신 우선 — Claude 자율성·의견·실행 권한

이 절은 §3의 '기본 동시 Claude 작업자 1명'과 §14의 초기 화면 도구 초안에 우선한다. Opus5.5만, 구독 포함 사용량·고정 계정 확인·fallback 없음·재귀 AI 호출 금지는 그대로다.

- **활동량·병렬:** 서로 독립인 구현은 별도 branch/worktree에서 Claude 작업을 병렬로 진행한다. 허용 파일은 작업끼리 겹치지 않게 배정하고, 실행기는 같은 worktree의 두 번째 작업을 `ops/ai/local/worktree-*.lock`으로 거절한다. Unity Editor/화면 제어 잠금은 계속 전역 하나다. Codex는 작업 경계·지시·계획·독립 검증·통합의 품질을 유지하고 이미지 생성·내용 수정을 전담한다.
- **의견과 자율 구현:** 캐릭터 외형의 최종 디자인은 Codex가 맡지만 Claude는 캐릭터를 포함한 모든 분야에 자유롭게 의견을 낸다. 코드·게임 시스템·UI/UX·스토리·성능·테스트·아키텍처는 승인된 설계 범위에서 Claude가 자율 판단·구현하며, 의견마다 승인을 받거나 같은 내용을 반복 검토하지 않는다. 결과에는 구체적 구현 결함·설계 불일치·실제 제약과 선택 의견을 구분해 적고 Codex가 채택 여부를 판단한다. 선택 의견만으로 통합을 막지 않는다.
- **승인된 표현 보존:** 이미 승인된 비성적 캐릭터의 나이·존재·외형·서브컬처 표현을 포괄적인 약관 추정만으로 삭제·축소·변경하지 않는다. 구체 조항·실제 심사 결과 같은 근거가 있으면 실제 제약으로 Codex에 올린다. 안전·권한·과금 경계를 우회하라는 뜻이 아니며 가격·확률·재화·영구 자산·나이 정책 변경과 운영 배포의 승인 경계는 그대로다.
- **시험 명령:** 실행기의 `--allow-test NAME`(반복 가능)은 고정 목록(`node`=`npm test`, `proposals`, `memory-ui`, `late-ui`, `python`=`python3 -m unittest discover -s tests -v`, `docs`, `handoff`, `manifest`)만 받는다. 지정된 명령만 정확한 `Bash(명령)` 규칙으로 허용하고 나머지 셸은 `dontAsk`로 거부되며, 모델/API(`claude`·`codex`·`npx`), 네트워크(`curl`·`wget`·`ssh`·`gh`), 비밀 조회(`security`), 삭제·배포·Git 파괴 명령은 명시 금지 규칙도 둔다. 실제 Bash 호출은 stream으로 `runs`/`not_run`에 기록하고 목록 밖 명령이 성공하면 FAILED다. 시험은 저장소 코드(편집 범위 파일 포함)를 사용자 권한으로 실행하므로 OS 샌드박스가 아니며 `test:docs`는 자체 보고서 파일을 쓴다. Codex가 `git status`와 재실행으로 확인한다. 범용 실행 엔진으로 넓히지 않는다.
- **화면 도구 교정:** Codex가 provider 환경변수 없이 Peekaboo 4.9.5를 로컬 stdio로 띄워 initialize/tools-list만 확인했다(serverInfo peekaboo-mcp 4.9.5, 추론·화면 조작 없음). `list`·`hotkey`는 없고 `app`/`window`의 list 동작이 발견 경로다. `image`는 AI 입력이 없는 스크린샷, `see` OCR은 로컬 Apple Vision이다. `desktop`은 실제 26개 중 무료 native 21개(앱·창·메뉴·Dock·대화상자·Space·캡처·AX 검사/검증·클릭·입력·키·스크롤·드래그·선택·값 설정·의미 동작·대기)를 작업 대상 앱·창에 한해 허용하고 `analyze`·`agent`(AI 공급자), `clipboard`·`paste`(비밀 가능 클립보드), `browser`(대상 밖 웹 이동)를 금지한다. 도구 설명의 기본 provider 문구는 실제 호출 증거가 아니다. 계정·청구·비밀·키체인·터미널을 GUI로 우회 조작하지 않는다.
- **실제 노출 기준 보강(2026-10-17):** 첫 실제 `unity-read` init에 허용 목록 밖 같은 서버 도구 17개(`generate_image`·`generate_model`·`generate_audio`·`import_model` 등 포함)가 남았다. 이 이름들은 두 Unity 프로필에서 정확한 deny로 제외하고, 같은 서버의 미허용 도구가 문맥에 노출되면 기록만 하지 않고 FAILED로 처리한다. 새 도구가 필요하면 무료 여부와 쓰기 성격을 확인해 별도로 배정한다. 정확한 `Bash(명령)` 규칙 밖의 읽기 전용 셸이 CLI에서 자동 허용될 수 있음을 관찰했으므로 실행기 감사 결과로 판정한다. 상세 기록은 [AI_ENVIRONMENT.md](AI_ENVIRONMENT.md)다.
- **Unity `execute_code`:** 무료 Editor C# method-body 도구를 `unity-write`에서만 허용한다(설치·검수·실행 확인용). 임의 코드 실행이므로 파일 범위를 OS 수준으로 강제한다고 주장하지 않는다. 기존 Editor·파일 보존, 유료 asset/AI 생성·다른 모델 호출 금지를 유지한다. 실제 Unity tools/list와 CLI 노출·도구 호출은 다음 실험에서 검증하며 설정만으로 연결됐다고 쓰지 않는다.

## 17. 2026-10-17 최신 우선 — Claude 루트, Codex는 이미지·캐릭터 외형·읽기 전용 리뷰

사용자 지시(2026-10-17): 개발은 Claude가 맡고 Codex는 이미지·캐릭터 외형·읽기 전용 리뷰만 맡는다. Codex를 주축으로 쓴 이유는 디자인이었으며, 개발 판단은 Claude에 둔다. 이 절은 역할과 호출 방향에 한해 §1–§4·§8, §10의 「실행과 독립 검토」, §11·§14·§16의 Codex 담당 서술, `AGENTS.md` 보호 2의 「초기 구성은 Codex만」(초기 구성 완료), `README.md`·`docs/AI_ENVIRONMENT.md`·`docs/START_CODEX.md`·`START_HERE.txt`의 Codex 오케스트레이터·Claude 호출 서술에 우선한다. 그 서술들은 전환 전 이력으로 보존한다. 구독 전용·과금 차단(§2), 모델 고정(`gpt-6.1-sol`, `claude-opus-5-5`), 노력 선택(§12), 이미지 공급 경로(§5·§13·§15), 승인된 캐릭터 표현 보존(§16), 가격·확률·재화·영구 자산·나이 정책·운영 배포 승인 경계, GitHub Private, 설계·수치 보호는 그대로다.

- **Claude = 루트.** 작업 선택·요구사항 기록·계획, 구현·원인 분석·테스트 작성과 실행, Unity Editor 단일 쓰기(Scene·Prefab·Play 시험·임포트·빌드), 통합·커밋·push·PR·CI 확인, 원격·브랜치·PR 관리, 수용 기록과 `docs/CURRENT.md` 갱신, 의견 채택 판단을 맡는다. 실제 셸과 Unity MCP가 있는 세션에서 작업하고 자기 변경을 직접 컴파일·실행해 검증한다. 셸 없이 작성만 하고 검증을 다른 AI에 넘기는 방식을 기본으로 쓰지 않는다. 병렬 구현은 Claude 세션·하위 작업을 별도 branch/worktree와 겹치지 않는 파일로 나눈다.
- **Codex = 세 가지 전담.** ① 이미지 생성·재생성·인페인팅·내용 수정, ② 캐릭터 외형 최종 디자인, ③ 읽기 전용 독립 코드 리뷰. 작업 배정·지시·계획, 구현, 통합·커밋·push, Unity Editor 쓰기는 하지 않는다. 전환 시점(SAVE-PARTS 커밋 `0623d61` 이후)에 이미 진행 중이던 Codex 루트 단위는 이미 작성·검증된 변경의 커밋·CI 확인과 인계 기록까지만 마친다. 새 구현·수정 범위 확장·새 Claude 호출은 하지 않으며, 그 안에 끝나지 않으면 체크포인트를 남기고 Claude에 넘긴다. 새 Codex 루트 작업은 열지 않는다.
- **호출 방향.** Claude가 구독 인증·과금 차단을 확인한 뒤 공식 Codex CLI를 호출한다(이미지 요청, 리뷰 요청). Codex는 Claude를 호출하지 않으며 재귀 호출 금지는 그대로다. `tools/ai/run_claude.py`와 그 MCP 프로필·`--allow-test`·셸 금지 규칙·잠금·감사는 그 실행기로 띄운 이력 작업에만 적용하며, 기존 증거와 재현용으로 보존하되 새 작업 배정에 쓰지 않는다.
- **루트 세션 운영.** Claude 루트는 이 저장소 루트를 작업 디렉터리로 연 공식 Claude Code 구독 로그인 세션(Desktop 또는 대화형 CLI)이다. `docs/AI_ENVIRONMENT.md`의 계정 일치·Usage credits/Extra usage·자동 충전 OFF 확인 규칙을 그대로 따른다.
  1. **시작 확인(값은 출력하지 않고 존재·유형만):** 세션 시작 때와 계정·환경·설정이 바뀐 뒤 `python3 tools/ai/audit_root_session.py --start-check`를 실행한다. 이 명령이 다음을 확인하고 `ROOT_START_CHECK` 한 줄을 남긴다. `claude auth status`가 `authMethod: claude.ai`·`apiProvider: firstParty`인지, ANTHROPIC·OPENAI·CODEX·AWS·GOOGLE·GEMINI·AZURE·VERTEX 계열의 API 키·인증 토큰 환경 변수와 `CLAUDE_CODE_USE_BEDROCK`·`CLAUDE_CODE_USE_VERTEX`·`CLAUDE_CODE_USE_FOUNDRY`·`OPENAI_BASE_URL`이 없는지, `ANTHROPIC_BASE_URL`이 없거나 호스트가 `anthropic.com`인지 본다. 프로젝트 `.claude/settings.json`의 Opus 고정·`forceLoginMethod: claudeai`·fallback 없음, deny 목록 적용, `codex login status`의 ChatGPT 로그인도 함께 본다. 설정 파일이 넣은 환경 변수도 세션의 셸 환경에 들어오므로 같이 검사된다. 하나라도 다르면 추론 작업을 멈추고 보고한다.
  2. **도구 제한:** 루트 세션은 이미지·모델·오디오 생성, 유료 asset 경로, 패키지 변경 MCP 도구를 쓰지 않는다. 자격 증명 파일 읽기(`~/.codex/auth.json`, `~/.claude/.credentials.json`, 키체인)와 `security find-*-password`·`dump-keychain`·`printenv`·`env`(`/usr/bin/` 전체 경로 형태 포함)·`claude --bare`도 실행 전에 막는다. 환경 변수 이름 확인은 시작 확인이 대신한다. 기준 목록은 runtime-policy `work_routing.root_denied_tools`와 `root_denied_permissions`다. 이 기기에서는 `.claude/settings.local.json`의 `permissions.deny`로 강제하고, 시작 확인이 그 적용을 검사한다. 이력 실행기가 `.claude/settings.json`의 추가 키를 거부하므로 local 파일을 쓰며, 이 파일은 Git에 포함되지 않으니 다른 기기에서는 같은 목록으로 다시 만든다. 같은 파일의 `permissions.allow`에 `mcp__unityMCP`를 두어 Unity MCP 호출마다 묻지 않게 한다. deny는 allow보다 우선한다. 권한 모드는 사용자가 Desktop에서 고른 `default`·`acceptEdits`·`auto`·`bypassPermissions` 중 하나다. 감사 보고서가 실제 모드를 기록하며, `bypassPermissions`에서도 deny 규칙은 그대로 적용된다. 다만 `bypassPermissions`에서는 그 밖의 Bash 명령이 묻지 않고 실행된다. 막는 장치는 접두어 deny 규칙(복합·간접 형태로는 우회 가능), main force-push를 막는 PreToolUse 훅, 사후 감사뿐이다. `default`·`acceptEdits` 모드나 OS 샌드박스가 더 강하며, 어느 모드를 쓸지는 사용자가 정한다. 쓸 수 있는 MCP 서버는 `work_routing.root_allowed_mcp_servers`뿐이다. 목록 밖 서버는 노출만 돼 있으면 경고이고, 호출하면 위반이다. 그 밖의 권한은 하드 레일(시험 삭제·기대값 조작 금지, main force-push 금지, 비밀값 출력 금지, 요청 없는 의존성·유료 서비스·시스템 설정 변경 금지, 승인 경계)로 제한한다.
  3. **Unity 잠금:** Editor에 쓰기 전에(Scene·Prefab·Play 시험·`execute_code`·임포트·빌드, computer-use로 Unity 창을 클릭·입력하는 경우 포함) 이력 실행기와 같은 `ops/ai/local/exclusive-control.lock`을 `python3 -c "import sys;sys.path.insert(0,'tools/ai');import run_claude as r;r.acquire_lock(r.CONTROL_LOCK,{'run_id':'claude-root-<session_id>'},'Editor busy')"`로 잡는다. 이미 있으면 쓰지 않고 소유자를 확인한다. 쓰기가 끝나면 같은 방식의 `r.release_lock(r.ROOT/r.CONTROL_LOCK,'claude-root-<session_id>')`로 자기 잠금만 푼다. 세션이 중단돼 남은 잠금은 Editor 상태를 확인한 뒤에만 지운다.
  4. **Codex 호출:** `codex login status`가 ChatGPT 로그인이고 OpenAI/Codex API 키·base URL 환경 변수가 없을 때만 호출한다. 리뷰는 `codex exec -s read-only -m gpt-6.1-sol -c model_reasoning_effort=...`로 실행한다. 이미지 작업은 §5·§13·§15 경로를 따르며, CLI로 맡길 때는 `-s workspace-write -C art/<폴더>`로 쓰기 범위를 `art/` 아래 한 폴더로 제한한다.
  5. **감사:** 각 단위를 커밋·수용하기 전에 `python3 tools/ai/audit_root_session.py --out reports/<단위>/root_session_audit.json`을 실행한다. 이 명령은 현재 세션(`CLAUDE_CODE_SESSION_ID`)의 JSONL과 하위 에이전트 기록을 runtime-policy와 대조한다.
     - 대조 항목: 모델 고정, deny 도구 호출·노출, 허용 목록 밖 MCP 서버 호출, Agent 모델 재정의, 잠금 없는 Unity 쓰기·GUI 입력, 규칙에 어긋난 Codex 호출(`command codex exec`·`-s read-only`·`-m gpt-6.1-sol` 필수), 비밀 조회·환경 값 덤프·`--bare`·main force-push, 통과한 `ROOT_START_CHECK` 기록 여부.
     - 종료 코드 1(위반 또는 증거 누락)이면 커밋·수용을 막는다.
     - 예외는 `--waive TOOL_USE_ID=사유`로만 남긴다. 이 예외는 보고서와 수용 기록에 그대로 드러난다.
     - 수용 기록에는 루트 `session_id`, 감사 결과, Unity 잠금 획득·해제, Codex 호출의 thread ID·모델·노력·종료 상태를 남긴다.
     - 원본 JSONL은 로컬(`~/.claude/projects/`)에 두고 Git에 넣지 않는다.
  6. **결정(2026-10-17, 사용자 선택 (가)):** Desktop 루트 세션을 유지하고 위 감사로 수용을 막는다. 전용 CLI 래퍼는 쓰지 않는다. 이 감사는 실행 환경을 강제하지 않는다. 기록된 호출을 사후에 대조하는 제한된 증거이며, 이력 실행기의 정제된 실행과 같지 않다.
     - **보는 것 (셸):** 셸 입력은 shlex로 나눠 실제 프로그램과 인자를 본다.
       - 따옴표로 감싼 구분자의 heredoc 본문(`<<'EOF'`)만 그대로의 데이터로 본다.
       - 따옴표 없는 heredoc 본문과 따옴표 안 낱말은 `$(...)`·백틱 치환을 찾아 다시 검사한다.
       - `sh|bash|zsh -c`, `eval`, 셸에 넘기는 heredoc은 안쪽을 재귀로 검사한다.
       - 나눌 수 없는 입력이 민감한 프로그램을 언급하면 통과시키지 않고 막는다.
     - **보는 것 (그 밖):**
       - 코드를 실행하지만 입력을 읽을 수 없는 도구(`interact_with_process`, `browser_run_code_unsafe`, `python_repl`)와 대상 앱이 없는 전체 화면 입력은 막는다.
       - 하위 에이전트의 보호 작업(Unity 쓰기, Codex 실행, git commit/push)은 막는다.
       - 잠금과 시작 확인은 루트 세션이 문서의 정확한 형태로 단독 실행하고 그 결과가 기록된 뒤에만 인정한다.
       - 설정·인증 변경 뒤에는 시작 확인을 다시 통과해야 한다.
     - **확인하지 못하는 것:** 첫 도구 목록, MCP 하위 프로세스의 환경, 실제 추론 프로세스 환경과 확인한 셸 환경의 일치, 인터프리터 코드의 의미(`python -c`, Python heredoc, `node -e` 안에서 환경 변수를 읽는 경우 등), 계정 화면의 Usage credits OFF. 보고서의 `unverified`에 늘 적혀 있다. 수용 기록에는 "감사 미검증"으로 옮겨 적되 수용을 막지는 않는다. 이 감사는 실수로 생긴 위반을 잡는 장치다. 감사를 일부러 피하려는 세션까지 막는다고 주장하지 않는다.
     - 리뷰 이력은 `ops/ai/tasks/ROLE-SWITCH-CODEX-REVIEW.result.json`이다.
- **리뷰 계약.** 의미 있는 Claude 변경은 통합 전에 Codex의 읽기 전용 리뷰를 받는다. Codex는 파일을 수정·커밋하지 않고 `implementation_defect`·`design_mismatch`·`real_constraint`·`optional_opinion`으로 구분해 반환한다. 채택·수정·재검증은 Claude가 한다. 핵심 결함은 해결·재리뷰 전 통합하지 않고, 두 차례 미해결 규칙(§10)은 그대로다. 서버·저장/기억·경제·인증·이관·넷코드·핵심 판정 로직은 Codex 리뷰 없이 통합하지 않는다. 그 밖의 변경은 Codex 한도·장애 때 별도 컨텍스트의 Claude 리뷰로 통합할 수 있으며, 그 사실을 기록하고 Codex 리뷰를 이후에 받는다.
- **시각 디자인 전체는 Codex (사용자 지시 2026-10-17·18).**
  - Codex가 정하는 것: 이미지 자산, 그 자산을 화면 어디에 어떤 크기·순서·투명도로 놓을지, 화면의 색·톤·장식 같은 보이는 모습 전체. 무엇을 어떤 순서·방법·프롬프트·도구(승인된 §5·§13·§15 경로 안)로 만들지도 포함한다.
  - Codex가 남기는 것: 이미지와 디자인 명세(`art/` 아래). 세부 요청서가 필요하면 Codex가 `art/requests/`에 직접 쓴다.
  - Claude가 하는 것: 게임에 필요한 것(화면, 규칙상 표시해야 할 정보, §15.3 규격, 저장 폴더, 파일 이름 규칙)을 넘긴다. Codex 명세를 Unity에 그대로 구현하고, 기능·규칙·입력·성능·시험을 맡는다. 시각 디자인을 스스로 정하지 않는다.
  - 이 항목은 `CLAUDE.md`의 예전 "Claude 기본 담당 UI/UX"보다 우선한다. 생성물 검수와 비생성형 리사이즈·압축·슬라이스·Unity 임포트·화면 연결은 Claude가 한다. 얼굴·의상·포즈 등 내용 변경은 Codex 수정 요청으로 돌린다. Claude는 캐릭터에 의견을 낼 수 있지만 외형 최종안은 Codex가 정한다.
- **증거.** Codex 호출도 실제 세션·모델·노력·종료 상태·결과를 `ops/ai/tasks/`에 남긴다. 실제 호출 결과가 없으면 리뷰·생성 완료로 기록하지 않는다. 기계 판독용 배분은 [runtime-policy.json](../ops/ai/runtime-policy.json) `work_routing`이다.
