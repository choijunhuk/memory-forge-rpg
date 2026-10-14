## v0.7.1 검토 우선순위

- 시작 전에 `review/Review_v0.7.1.md`, `review/DECISIONS_PROPOSED.md`, `reports/delivery_status.json`을 읽는다.
- 첫 턴·성장·이끼와 입력 검사 수정은 현재 엔진/새 회귀 시험이 원본의 모순 부분을 대체한다.
- 패배 저장 v2, 확률·수급, 나이·핵심 서사 변경은 PROPOSED다. 승인 없이 운영 정책으로 바꾸지 않는다.
- 아트 신규 시안은 `review/ART_DIRECTION_v0.7.1.md`를 사용한다. 원본의 일률적인 ‘보호자 역할만/모든 일상 동작 제한’은 확정 제약으로 확대하지 않는다. 미성년 인물의 성적 묘사는 만들지 않으며 평범한 의상·비성적 유대·액션을 일괄 제한하지 않는다.
- `review/baseline`과 `review/sources/REJECTED_*`는 재현 자료다. 게임 런타임에 임포트하지 않는다.
- 외부 리뷰의 구역3~5 완성·밸런스 수치는 재현 전 주장으로 취급한다. 원본 러너는 구역1~2만 검사한다.

# Memory Forge (기억 공방) — 공통 작업 지침 (AI 에이전트·사람 공통)

## 기준과 권위
사용자(디렉터) 승인 > `docs/decisions.md` > `docs/design_v0.7.md`(원본 `docs/design/`) > `content/*.mjs` → `data/game_data.json` > `engine/rules-core.js` > C#·서버·클라이언트 구현.
수치는 데이터가 기준이다. 문서 표는 `tools/gen_docs.mjs`가 데이터에서 만든다. 문서와 엔진이 다르면 고르지 말고 `docs/decisions.md`에 질문/ADR 초안을 남긴다.

## 처음 할 일
1. `README.md`의 "빠른 확인" 명령을 실행해 현재 상태를 재현한다.
2. `docs/design_v0.7.md` §0.4 구현 상태표, §20 단계별 계획에서 맡은 단계를 찾는다.
3. 그 단계의 "완료 조건"을 인수 시험으로 삼는다.

## 변경 규칙
- 규칙·데이터를 바꾸면 같은 PR에서: `node content/build.mjs data` → `node tools/gen_vectors.mjs` → C# 반영 → `node tools/gen_docs.mjs && node tools/build_docs.mjs` → 시험 전부.
- 새 효과 op·적 행동·오류 코드는 엔진(JS)·C#·문서 생성기 표·시험을 함께 추가한다(빠지면 문서 생성이 실패한다).
- 부동소수점 금지(규칙 계산), 반올림은 명세 그대로, 컬렉션 순회 순서 고정.
- 확률·가격·재화 지급량·저장 호환·보안 경계·상용 일정 변경은 ADR 초안과 영향표를 먼저 작성하고 사람 승인을 받는다.

## 하지 말 것
운영 배포, 실결제, 비밀 키 열람, 운영 DB 접근, 원격 저장소 공개 전환. 모집에 전투용 시드 RNG 사용. 클라이언트에 RNG 상태·뽑을 순서 전송.
10대 캐릭터(니아·유리·리엔·카야)를 성적으로 대상화하는 이미지·포즈·구도·대사·프롬프트 작성(등급과 무관, `docs/design_v0.7.md` §9.1).

## 분업
Codex: C# 코어·서버·자동 시험·도구. Claude: Unity UI·콘텐츠 데이터·문구·서사·문서. 작성자가 아닌 쪽이 교차 검토.
git worktree로 브랜치를 분리하고, 같은 Unity 씬·프리팹·ProjectSettings는 동시에 한 명만 편집한다.

## 완료 보고
실행한 명령, 결과, 시드, 바꾼 규칙, 하지 않은 것을 적는다. "소스 작성 / 컴파일 / 단위 시험 / Unity Play Mode / Android 실기기"는 서로 다른 상태다.
JS 통과를 C# 통과로, 자동 완주를 재미 검증으로 바꾸어 적지 않는다. 시험 수를 부풀리지 않는다.

## 필수 명령
```sh
node content/build.mjs data
node --test engine/tests/*.test.mjs
node engine/tests/fuzz_runs.mjs 100 short
dotnet run --project core/MemoryForge.CoreChecks -c Release -- .
python3 -m unittest discover -s tests
tools/check_schema.sh
node tools/build_prototype.mjs && node prototype/tests/smoke.mjs 2
# 전부 + reports/ 갱신
tools/run_all_checks.sh
```
