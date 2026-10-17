# 담당·수정 권한 정리 (2026-10-18)

사용자 지시 「창작 방향·담당 권한·구현 원칙 통합 지시」를 실행 계약에 반영했다.

- `docs/AI_WORKFLOW.md` §18: 캐릭터 매력 방향, Codex 창작 원본, Claude 공통 구현, 문제 분류, 흐름.
- `OWNERSHIP.md`: 경로별 수정 권한, 공유 파일의 영역 구분, 실제 강제와 문서상 약속의 구분.
- `AGENTS.md`·`CLAUDE.md`·`docs/START_CLAUDE.md`·`README.md`: 2026-10-18 최신 우선 절. 과거 절은 이력으로 남겼다.
- `ops/ai/runtime-policy.json` `work_routing.codex_responsibilities`·`codex_must_not`·`creative_ownership`, `claude_autonomy.codex_keeps`. 정책 시험(`tools/ai/test_handoff.py`)을 새 정책에 맞췄다.
- `tools/check_art_copies.py`: Unity가 읽는 그림이 `art/` 원본의 바이트 복사본인지 SHA-256으로 대조한다. CI에 연결했다. 복사본 1바이트를 바꾸면 `NO_MATCHING_ORIGINAL`로 실패하는 것을 확인했다.
- `docs/MOMENTS_FORMAT.md`: Codex가 쓰고 Claude가 재생하는 모먼트 데이터 형식.
- `art/requests/character-moments-20261018.md`: 성인 6명의 빠진 매력 표현 정리와 Codex 창작 요청.

검증: `npm run test:handoff` 33/33, `npm run test:docs`, `node tools/gen_run_create_vectors.mjs --check`, `python3 tools/check_art_copies.py` 17/17. 루트 감사는 `root_session_audit.json`.
