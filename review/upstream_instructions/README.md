# 기억 공방 / Memory Forge v0.7

**로그라이트 덱빌딩 서브컬처 RPG의 제작 기준선**: 통합 설계서 + 단일 게임 데이터 + 실행 가능한 규칙 참조(JS) + C# 코어(교차 검증) + 경제 정확 검산 + 서버 스키마·API 명세 + 브라우저 플레이 프로토타입 + AI 에이전트 제작 계획. 완성된 모바일 게임은 아니다.

## 먼저 볼 것

| 파일 | 내용 |
|---|---|
| `docs/Memory_Forge_v0.7_Design.pdf` | 통합 설계서 PDF(97쪽). 원본 마크다운 `docs/design_v0.7.md`(조립본), `docs/design/*.md`(장별 원본) |
| `prototype/index.html` | 브라우저로 여는 플레이 프로토타입(탐사·전투·기억 저장·모집 시뮬레이터). 아트 없음 |
| `docs/decisions.md` | v0.7 결정 기록(ADR) |
| `AGENTS.md`, `CLAUDE.md`, `docs/START_CODEX.md`, `docs/START_CLAUDE.md` | AI 에이전트 작업 규칙과 첫 지시문 |
| `reports/delivery_status.json` | 실제로 실행한 검사와 결과, 하지 않은 것 |

## 빠른 확인

```sh
node content/build.mjs data                         # 데이터 생성(캐릭터 10, 카드 173, 무기 22, 부품 16, 유물 32, 적 20, 사건 8)
node --test engine/tests/engine.test.mjs            # 엔진 단위 시험 27개
node engine/tests/fuzz_runs.mjs 100 short           # 무작위 탐사 100판, unexpected 0
dotnet run --project core/MemoryForge.CoreChecks -c Release -- .   # C# 교차 검사 202/202
python3 -m unittest discover -s tests               # 경제 시험 17개
tools/check_schema.sh                               # PostgreSQL 16 스키마 적용·제약 시험
node tools/build_prototype.mjs && node prototype/tests/smoke.mjs 2  # 프로토타입 빌드·UI 자동 완주
tools/run_all_checks.sh                             # 전부 실행하고 reports/ 갱신
```

요구 도구: Node 22+, Python 3.11+, .NET SDK 10(코어 라이브러리는 netstandard2.1, 검사 프로젝트는 net10.0), PostgreSQL 16 바이너리, Playwright Chromium(`npx playwright install chromium`), API 명세 검증용 `pip install openapi-spec-validator`. 문서 PDF는 `node tools/gen_docs.mjs && node tools/build_docs.mjs`(Chromium 인쇄, 한글 글꼴 Noto Sans CJK KR 필요).

## 구조

```text
content/   작성 원본(.mjs) → data/game_data.json
engine/    rules-core.js(전투·탐사·기억 저장·모집·스탯) + tests(단위·퍼즈·밸런스·교차 벡터)
core/      C# 코어 netstandard2.1 + CoreChecks
server/    schema.sql · openapi.yaml · tests
prototype/ index.html(빌드 결과) · src · tests/smoke.mjs
tools/     build_prototype · gen_vectors · gen_docs · build_docs · verify_economy.py · check_schema.sh · run_all_checks.sh
tests/     Python 경제 시험
docs/      설계서(원본·조립본·PDF) · 결정 · 시작 지시문 · v0.6 보관본
reports/   실행 기록
```

## 상태 (2026-10-14)

구현·시험 완료: JS 규칙 참조, 성장 스탯 모델, C# 코어(RNG·모집·스탯·예약 장부), 경제 정확 검산, DB 스키마(실제 적용), API 명세(검증), 브라우저 프로토타입(자동 완주), 설계서.
미구현: C# 전투·탐사 전체 이식, 서버 애플리케이션 코드, Unity 클라이언트, 구역 3~5 콘텐츠, 아트·사운드, 실결제, 실기기·사람 플레이 테스트. 단계와 완료 조건은 설계서 §20.

등급 목표 15세. 플레이어블 캐릭터는 성인 6명과 10대 4명이며, 10대 캐릭터는 등급과 관계없이 성적으로 대상화하지 않는다(설계서 §9.1).
