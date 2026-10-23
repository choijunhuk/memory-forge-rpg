# PR #2 최신(`271cb45`) 게임 검증 — 2026-10-19

사용자 요청: "푸시 병합은 왜 안하는지랑 게임 검증해줘봐".

## 1. push·병합 상태

- Android 16KB 수정(`c5f529f`)과 카드 설명 높이 수정(`2db3efd`)은 루트가 PR #2 브랜치 `codex/late-actions-balance`에 이미 옮겨 넣고 push했다. 옮긴 커밋은 각각 `30ade24`, `2196e66`이다. PR #2의 CI(`checks`)도 성공이다.
- 두 브랜치를 `271cb45`에 다시 병합해 보았다. 결과 트리가 `271cb45`와 같아서 추가할 커밋이 없었고, 병합 커밋은 버렸다.
- 세션들이 push·병합을 직접 하지 않은 이유는 작업 지시서에 있다. 여러 세션이 같은 PR 브랜치를 쓰므로, 충돌을 막기 위해 통합은 루트 한 곳이 순서대로 한다고 정해 두었다.
- PR #2(`codex/late-actions-balance` → `codex/storage-v2`)는 열려 있고 병합 가능 상태다. 병합은 사용자 결정으로 남긴다.

## 2. 저장소 검사 (`271cb45`, macOS)

`tools/run_all_checks.sh`는 macOS 기본 bash 3.2에 `declare -A`가 없어 바로 실패한다. CI(Linux)와 같은 단계를 하나씩 실행했다.

| 단계 | 결과 |
|---|---|
| build_data, gen_vectors | PASS |
| engine_tests, review_memory, review_economy | PASS |
| fuzz_short (100), fuzz_full (40), balance_probe (300) | PASS |
| csharp_checks (`~/.dotnet` .NET 10) | PASS. 기본 `dotnet`은 SDK 9.0.201이라 `NETSDK1045`로 빌드되지 않는다 |
| economy_verify, python_tests | PASS |
| build_prototype, document_contract_tests | PASS |
| schema_check | NOT_RUN. PostgreSQL 없음 |
| openapi_check, ui_smoke, gen_docs, doc_build_run | NOT_RUN. CI에서 통과 |

## 3. Unity (batchmode, 별도 worktree, 루트 Editor 미접촉)

| 항목 | 결과 |
|---|---|
| EditMode | PASS |
| PlayMode | **PASS** 344개 중 341 통과, 실패 0, 건너뜀 3 |
| Android 개발 APK 빌드 | Succeeded, 149초, errors 0 |
| APK 네이티브 라이브러리 정렬 | 6개 모두 0x4000(16KB) |

## 4. 발견: APK가 620MB로 커졌다

- 크기: `MemoryForge-dev-271cb45.apk` = 620,319,463 bytes. 10월 17일 APK는 47MB였다.
- 빌드 보고 기준 텍스처가 사용자 자산의 99.2%(1.1GB, 압축 전)다. Resources 아래 PNG가 291개다.
- 큰 텍스처 하나가 약 8MB다. 예: `Art/Enemies/enemy_e_lantern.png`, 1254×1254 RGBA, 밉맵 켬.
- 원인 추정: 가로·세로가 4의 배수가 아니다(1254). Android 자동 형식(ETC2)이 적용되지 못해 비압축 RGBA32로 들어갔다.
- 영향:
  - Play 업로드 크기 한도를 넘는다.
  - 다운로드 크기가 커진다.
  - 실기기 메모리 사용이 늘어난다.
- 고칠 방향(별도 작업): Android 텍스처 형식을 ASTC로 지정하거나, 원본을 4의 배수 크기로 받는다. 표시 크기에 맞춰 최대 크기를 낮추고, UI 그림의 밉맵을 끄는 것도 함께 검토한다. 텍스처 설정은 Claude 담당, 원본 크기 변경은 Codex 요청 대상이다.

## 5. 실기기

- 새 APK를 설치하려 했으나 S24 Ultra 연결이 끊겨 있었다(`adb devices` 비어 있음).
- **실기기 설치·실행은 NOT_RUN이다.** USB를 다시 연결하면 이어서 한다.
