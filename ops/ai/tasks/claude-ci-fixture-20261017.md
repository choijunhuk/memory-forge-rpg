# CI 실패 원인 독립 검토와 최소 CI 로그 개선

사용자는 Claude Opus의 실제 구현·분석 활동량을 높이고 병렬 작업을 요청했다. 이 작업은 다른 Claude의 서버 UI 및 MCP 실행기 작업과 독립적이다. 기존 구독 포함 사용량만, Opus5.5만, 재귀 AI 호출·API·추가 과금·이미지 생성·커밋·푸시 금지. 다른 작업자가 있으므로 책임 파일 밖의 변경을 되돌리지 않는다.

## 확인된 입력과 책임

기준 HEAD 2b2d8ca. PR #2 CI run 39003768213에서 engine_tests와 csharp_checks가 실패했다. Codex의 독립적인 로컬 재현도 같은 원인이다:

- `npm test`: 333개 중 332 PASS, `engine/tests/run_create_vectors.test.mjs:179`의 `checkFixture`에서 fresh Run factory state/shape/JSON/query/error data 불일치.
- .NET10 CoreChecks: 다른 모든 검사 통과, RunCreate에서 `Run-create input manifest audit differs from the recomputed repository audit`.
- 이번 아트 작업은 `art/provenance.csv`에 14행을 추가했다. 원본 MANIFEST_AIO.json/MANIFEST.sha256는 재생성하지 않았다. 감사의 preExistingChangedPaths가 기존 29에서 30으로 늘었다. 이것이 유일 원인인지 원본 generator/checker를 읽고 독립적으로 판단한다.
- 현재 workflow는 실패 시 `tools/run_all_checks.sh`의 마지막 세 줄만 출력해서 상세 원인이 CI에 남지 않는다. 실행기 자체는 보호 파일이며 수정 금지.

수정 책임은 `.github/workflows/ci.yml` 한 파일이다. 문서/데이터/엔진/수치/manifest/보호 실행기 수정 금지. 범위가 부족하면 결과에 정확한 필요 파일·이유를 보고한다.

## 실제 작업과 완료 기준

1. `tools/gen_run_create_vectors.mjs`, JS fixture test, C# RunCreateChecks와 기존 gzip oracle을 비교한다. strict audit를 약화하지 않는다. generator·원본 게임 결과 변경 없이 기존 export로 검증 자료를 다시 생성하는 것이 맞는지 판단하고 정확한 명령·검증 기준을 제시한다. 현재 도구는 텍스트 편집만 가능하므로 gzip 갱신은 Codex가 기존 generator를 실행하는 통합 단계로 맡긴다.
2. workflow에 의존성을 추가하지 않는 최소 failure-only 진단 단계를 실제 구현한다. 기존 engine/csharp 검사 결과 텍스트(예: reports/engine_tests.txt, reports/csharp_checks.txt)가 존재하면 실패 로그에 보여 주고, 원래 실패 상태를 덮어쓰지 않는다. 개인 계정·실행 환경·비밀이 들어갈 수 있는 광범위 로그 수집은 하지 않는다. 기존 검사·순서·분모·실패 기준은 유지한다.
3. 결과에는 원인, workflow 변경, 실제 실행 못 한 항목, 다음 실행할 좁은 검사와 최종 gzip 검증을 짧게 보고한다. 반복적인 전체 저장소 조사·장문의 기획·새 범용 프레임워크는 하지 않는다.

Read/Glob/Grep/Edit 사용 가능. 테스트를 실행하지 않았으면 NOT_RUN이다. root의 다른 작업이 manifest 추적 문서를 바꾸면 최종 source 변경 뒤 fixture를 갱신해야 하며 여러 번 큰 fixture를 재생성할 필요는 없다.
