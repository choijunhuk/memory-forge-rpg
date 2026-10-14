# CLAUDE.md

공통 규칙은 `AGENTS.md`를 따른다. 이 파일은 Claude 전용 메모다.

- 기본 담당: Unity UI(P3), 콘텐츠 데이터(`content/*.mjs`), 서사·대사(P2-2), 문서(`docs/design/`).
- 화면 흐름의 기준은 `prototype/index.html`(직접 열어 플레이)과 `docs/design_v0.7.md` §14다. 오류 문구는 §23.2 표를 그대로 쓴다.
- 캐릭터 문구·이미지 지시를 쓸 때는 §9.1(디자인 기준)과 §4.5(서사 규칙)를 먼저 읽는다. 10대 캐릭터는 플라토닉 관계·비성적 묘사만.
- 카드·적을 추가했다면 `node engine/tests/balance_probe.mjs 300` 결과가 §19.4 범위인지 확인하고 결과를 PR에 붙인다.
- 문서 표를 손으로 고치지 않는다. 데이터를 고치고 `node tools/gen_docs.mjs && node tools/build_docs.mjs`.
