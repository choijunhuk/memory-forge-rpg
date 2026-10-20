# OWNERSHIP — 누가 무엇을 바꾸는가

## 2026-10-21 최신 우선 — 일반 구현 공동 담당

사용자 직접 지시와 [작업 배분](docs/WORK_ALLOCATION_20261021.md)에 따라 아래 §2의 일반 구현 경로는 **Codex·Claude 공동 담당**이다. 계획·조정·시험·빌드·통합·커밋·push·PR·CI도 양쪽이 맡을 수 있다. §3의 일반 런타임 제어·규칙 구현·생성·바이트 복사 임포트도 선언된 작업 담당자가 수행한다. 창작 내용은 `art/` 원본에서만 바꾸고 복사본 해시 검사를 유지한다.

아래의 Claude 루트 전용 Unity 쓰기·Codex 구현 금지·Codex의 모든 쓰기를 `art/<폴더>`로 한정하는 설명은 이전 역할 계약의 이력이다. `art/<폴더>` 제한은 Claude가 Codex에 맡기는 창작 원본 작업에 계속 적용하며, Codex 자신의 일반 구현 worktree 권한을 제한하지 않는다. Unity는 Codex 또는 Claude 중 선언된 한 담당자만 기존 전역 잠금을 잡고 쓴다. 같은 파일·씬·프리팹 동시 쓰기와 AI 재귀 호출은 금지한다.

§1의 창작 원본은 계속 Codex 소유이며 Claude 원본 쓰기 차단·변경 요청·§18.1·§18.6 표현 기준을 유지한다. 의미 있는 변경은 작성자 반대 AI가 독립 리뷰하고 민감 경로는 해당 리뷰 전 통합하지 않는다. 모델·구독 과금·승인·비공개 경계는 그대로다. Claude 세션 감사는 Codex App/native UI까지 감사하지 않는다.

기준: 사용자 지시 2026-10-18, `docs/AI_WORKFLOW.md` §17·§18. 이 표는 **수정 권한**을 정한다. 읽기·검토·연결은 양쪽 모두 할 수 있다. 사용자의 명시적 결정이 언제나 우선이며, Codex 초안과 사용자 승인 확정본은 구분한다. 나이·가격·확률·재화·영구 자산·운영 배포는 담당과 무관하게 사용자 승인 범위다.

## 1. Codex 소유 — 창작 원본

| 경로 | 내용 | Claude가 할 수 있는 것 |
|---|---|---|
| `art/candidates/<날짜>/codex/**`, `art/generated/**` | 캐릭터·보스·적·배경·UI 그림, 시트, 표정, 프롬프트, 제작 기록 | 검수, 비생성형 리사이즈·압축·슬라이스, Unity 임포트용 복사 |
| `art/moments/<캐릭터id>/**` (예정) | 로비 반응·인연 장면·등장/승리/스킨 연출 제작안, 대사, 표정·모션 지시, 콘티, 음성 대본 | 재생기 연결, 기술 결함 보고 |
| `art/motion/<캐릭터id>/**` (예정) | 리그, 키프레임, 스프라이트 프레임, Animation Clip 원본, Timeline·VFX 원본, 캐릭터 시각 프리팹 원본 | 바이트 그대로 임포트, 전환 조건·블렌딩 설정 |
| `art/requests/**` | 세부 요청서 (Claude도 요청을 쓸 수 있다) | 요청 추가 |
| `art/briefs/**` | 캐릭터 외형·프롬프트 브리프 | 설정 모순·누락 지적 |
| `content/character_notes.mjs`의 대사·인연 어조 문자열 | 캐릭터 대사 | 코드 구조(내보내기·키) 정리만. 문자열은 바꾸지 않는다 |

Codex 쓰기 범위는 실행마다 `codex exec -s workspace-write -C art/<폴더>`로 실제 제한된다(샌드박스). 루트 감사(`tools/ai/audit_root_session.py`)가 그 형태를 검사한다.

## 2. Claude 소유 — 공통 구현

| 경로 | 내용 |
|---|---|
| `client/MemoryForgeUnity/Assets/Scripts/**`, `Assets/Editor/**`, `Assets/Tests/**` | UI/UX, 화면 배치, 입력, 범용 애니메이션·대사·연출 재생기, 상태 전환, 로딩·캐싱 |
| `client/MemoryForgeUnity/Assets/Scenes/**`, `Assets/Prefabs/**` (캐릭터 시각 원본 제외) | 화면 구성, UI 프리팹 |
| `client/MemoryForgeUnity/Packages/**`, `ProjectSettings/**` | 패키지·빌드 설정 (요청 없는 신규 의존성 금지) |
| `engine/**`, `core/**`, `server/**`, `tools/**`, `tests/**`, `prototype/**`, `ops/**` | 규칙·서버·저장·도구·시험·운영 |
| `docs/AI_WORKFLOW.md`, `AGENTS.md`, `CLAUDE.md`, `docs/START_*.md`, `docs/CURRENT.md`, `docs/IMPLEMENTATION_STATUS.md`, `reports/**` | 실행 계약·현황·증거 |

Unity Editor 쓰기는 Claude 루트 세션 하나만 한다(`ops/ai/local/exclusive-control.lock`).

## 3. 함께 쓰는 파일 — 영역별 권한

| 경로 | Codex가 바꾸는 영역 | Claude가 바꾸는 영역 |
|---|---|---|
| `client/MemoryForgeUnity/Assets/Resources/Art/**` | 없음 (원본은 `art/`에서 바꾼다) | `art/` 원본의 바이트 복사본 배치. 내용 수정 금지, `tools/check_art_copies.py`가 원본 해시와 대조 |
| 캐릭터 프리팹 (예정 `Assets/Art/Characters/<id>/`) | 시각 자식(스프라이트·리그·클립·VFX)의 내용, 원본은 `art/motion/` | 루트의 런타임 제어 컴포넌트, 재생기 연결, 정렬·크기 맞춤 |
| 연출 데이터 (예정 `Assets/Resources/MemoryForge/moments/*.json`) | 장면 내용 전부(대사·표정·모션 이름·타이밍·카메라 의도), 원본은 `art/moments/` | 형식(스키마)과 읽는 코드. 원본의 복사만 하고 내용은 바꾸지 않는다 |
| `content/characters.mjs`, `data/*.json`, `Assets/Resources/MemoryForge/battle-content.json` | 없음 (성격·말투 서술 변경은 요청으로) | 규칙·수치 구현과 생성. 나이·이름·성격 서술은 승인된 설계를 따르고 임의로 바꾸지 않는다 |
| `docs/design_v0.8.1.md`, `docs/source_v0.8.1/**` | 창작 절 초안 제안 | 구현 상태 표기. 둘 다 사용자 승인 없이 설계 확정 내용을 바꾸지 않는다 |

## 4. 바꾸면 안 되는 것을 바꿔야 할 때

- 기술 결함이면 창작 의도를 지키며 Claude가 고친다. 대사 축약, 표정·의상·포즈·모션 교체, 크롭·카메라 변경은 `art/requests/`에 변경 요청으로 남기고 Codex에 전달한다.
- 실제 제약(도구 정책, 스토어 요건)은 해당 자산과 이유를 `real_constraint`로 기록한다. 원본을 몰래 고치지 않는다.

## 5. 실제로 강제되는 것과 문서상 약속

| 항목 | 상태 |
|---|---|
| Codex 쓰기 범위 `art/<폴더>` | **강제됨**: 실행별 샌드박스, 감사가 호출 형태 검사 |
| Unity Editor 단일 쓰기 | **부분 강제**: 잠금 파일과 감사. OS 수준 차단은 아니다 |
| Unity 복사본이 원본과 같은지 | **검사됨**: `python3 tools/check_art_copies.py` (원본 해시 대조) |
| Claude가 창작 원본(`art/**`)을 바꾸지 않음 | **문서상 약속 + 사후 확인**: 기술적 차단 없음. `git log -- art/`와 위 해시 검사로 확인 |
| Codex가 구현 코드를 바꾸지 않음 | **강제됨**: 샌드박스 범위 밖 |
