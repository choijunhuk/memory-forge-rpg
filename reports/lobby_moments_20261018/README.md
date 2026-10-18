# 로비·모먼트 재생기 (2026-10-18)

사용자 지시 2026-10-18(§18)의 Claude 몫: Codex가 만든 캐릭터 모먼트를 보여 줄 로비, 인연·연출 목록, 범용 재생기, UI 움직임.

## 만든 것

- **로비** `MemoryForgeLobby` 씬(빌드 첫 씬): 공방 배경(Codex `bg_zone1_event_workshop_v02`), 동료 한 명, 탭·입장·대기·시간대 반응, 「인연 이야기」·「등장·승리 연출」 목록(미리보기, 자산 준비 수 표시), 「다른 동료」, 「탐사 시작」(페이드 후 `MemoryForgeExpedition`), 움직임 줄이기 설정.
- **재생기** `MemoryForgeMomentPlayer`: [`docs/MOMENTS_FORMAT.md`](../../docs/MOMENTS_FORMAT.md)의 그림·덧그림 슬롯, 프레임 시퀀스, 슬롯 키프레임, 카메라 구도, 배경, 대사 타자·페이지 분할(원문 그대로), 탭·건너뛰기, 장면 뒤 대기 그림 복귀. 없는 그림은 비워 두고 다른 그림으로 바꾸지 않는다. 잘못된 파일은 필드 경로와 함께 거부한다.
- **가져오기** `tools/import_moments.py`: `art/moments/**`를 Unity `Resources/Art/`로 바이트 복사(심볼릭 링크·경로 탈출 거부). `tools/check_art_copies.py`가 원본 해시와 대조한다.
- **UI 움직임** `MemoryForgeUiMotion`: 버튼 눌림, 손패 카드 호버, 탐사 화면 종류가 바뀔 때 짧은 페이드, 로비 등장·퇴장 페이드, 목록·대화창 등장. 움직임 줄이기를 켜면 모두 즉시 전환된다.
- Android 개발 APK 빌드에 로비 씬을 포함했다.

## Codex 창작 원본

`art/moments/`: 성인 6명(세라 24·라비엔 28·하린 26·에일라 23·시온 27·루미 22) × 로비 A·인연 B·승리 C = 18장면, 인연 대본 6편(각 2,000–3,000자). 모두 `draft`(사용자 승인 전). 실제 그림은 세라 로비 전신 3장(투명 PNG)뿐이고 나머지 그림 98개는 계획 상태다. Codex의 정리: [`art/moments/HANDOFF.md`](../../art/moments/HANDOFF.md). 내용은 바꾸지 않고 그대로 재생한다.

## 실제 재생 확인 (Unity Editor Play)

- `lobby_sera.png`: 모먼트 전 컷인 정지 그림 로비.
- `sera_lobby_01_0..6.png` + `sera_lobby_01_log.txt`: 세라 A를 0.3–11.5초에 캡처. 조준경(중간 샷) → 시계 탭 프레임 → 미소 → 바스트 푸시인과 대사 두 줄, 상태 로그 일치.
- 연타 6회는 장면 1회만 시작, 건너뛰기 뒤 대기 그림·카메라·메뉴 복귀, 장면 중에는 메뉴가 숨어 이탈 불가, 건너뛴 뒤 「탐사 시작」→ 탐사 지도(노드 19개, 버튼 눌림 효과 22개) 확인(`expedition_after_lobby.png`).
- 이 캡처들은 재생 중 메뉴를 30%로 흐리던 때의 것이다. 이후 완전히 숨기도록 바꿨다.

## 시험

- PlayMode 전체 329/329(`MomentPlayerPlayModeTests` 13개 포함), EditMode 3/3.
- `python3 tests/test_import_moments.py` 5/5, `npm run test:docs`, `npm run test:handoff`, `node tools/gen_run_create_vectors.mjs --check`, `python3 tools/check_art_copies.py`.

## 리뷰

Codex 읽기 전용 리뷰 5회([기록](../../ops/ai/tasks/LOBBY-MOMENTS-CODEX-REVIEW.result.json)): 결함 11 → 6 → 3 → 1 → **NO_BLOCKING_DEFECT**. 2차에서 실제 재생 캡처를 원본과 비교한 시각 검토는 "의도한 포즈·표정·대사·구도가 보인다"였다. 3차는 슬롯 규칙(새 그림은 프레임 시퀀스만 끝내고 트랙은 잇는다)이 세라 장면의 의도와 맞다고 확인했다. 보류한 선택 의견: 움직이는 카메라 기준점 시험(1차 #12).

## fix5 임포트 (2026-10-19)

Codex fix5(루미 인연 B의 헬멧 두 개 끝 포즈, 에일라 정제 스프라이트 18장)를 `tools/import_moments.py aila lumi`로 바이트 그대로 가져왔다. 열린 Editor에서 `execute_code` 없이 찍도록 Explicit 시험 `LobbyMomentCapturePlayModeTests.CaptureMomentScenes`(출력 폴더는 `Temp/moment_shots.txt`)를 추가했고, 실제 로비 재생으로 두 캐릭터 6장면 134장을 찍었다. `fix5/`에는 각 장면의 시작·끝과 검토가 짚은 장면만 960×540으로 남겼다(`captured.txt`가 전체 목록).

Codex 읽기 전용 시각 검토(`fix5/codex_visual_review.txt`): **NO_BLOCKING_DEFECT**. 루미 끝 장면의 헬멧 두 개와 비어 있는 손 확인, 에일라 쓰기·보여 주기·돌려놓기·서랍 포즈와 등불 위치 확인, 구현 결함 없음. 선택 의견: 에일라의 금장식과 방이 아직 시선을 조금 나눈다(원본 연출 취향).
