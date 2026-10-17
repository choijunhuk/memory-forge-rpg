# Unity에서 현재 개발 결과 보기

Unity **6000.0.46f1**에서 이 프로젝트를 열고 **`Assets/Scenes/MemoryForgeLobby.unity`**를 실행한다. 현재 빌드의 첫 씬이다(2026-10-18). 로비는 공방 배경 위에 동료 한 명과 Codex가 만든 모먼트(`art/moments` → `python3 tools/import_moments.py`)를 재생하고, **탐사 시작**으로 `MemoryForgeExpedition`을 연다. 탐사 연습만 볼 때는 `MemoryForgeExpedition.unity`를 바로 실행해도 된다. 기존 `MemoryForgeBattle`·`SampleScene`·MCP 연결 확인 씬은 보존한다.

현재 첫 화면은 실제 공유 C# Run을 조작하는 **탐사 연습**이다. 세라·하린·니아의 원본 기본 덱과 실제 생성 지도를 사용한다. 지도·전투·보상·휴식·공방·상점·이벤트·기록보관소·종료 기억 선택과 다음 출발을 연결한다. 저장은 이 실행 세션 안에만 있으며 앱을 닫으면 사라진다. 계정·서버·디스크 영속 저장이나 중도 탐사 복원은 아니다.

- 밝은 지도 칸을 눌러 들어간다. 덱과 기록은 상단에서 확인한다.
- 카드·방식·대상을 고른 뒤 `사용 확정`을 누른다. 선택·설명 읽기만으로는 행동력을 쓰지 않는다.
- 방금 행동의 결과 표시 중에는 다음 카드를 골라 `사용 대기`로 한 장만 확정할 수 있다. `미전송 사용 대기 1/1` 동안 AP·카드·시간 예약 칸은 쓰지 않고, 표시가 끝난 뒤 그 요청만 한 번 사용한다. `대기 취소`는 아직 보내지 않은 입력만 취소한다.
- `보통`·`2배속`·`동작 줄이기`·`건너뛰기`는 플레이어 행동 표시만 바꾼다. 다음/다다음 턴의 시간 예약은 별도이며, 대기한 예약 요청은 실제 사용될 때 시간표에 들어간다. 기존 턴 요약의 예약 해결은 정상0.3초/생략 범위다.
- 선택 효과 창·턴 결과·오류 복구에서는 해당 입력 잠금을 따른다. 앱을 벗어나거나 비활성화하면 보내지 않은 사용 대기는 취소되며 복귀 후 자동 실행하지 않는다.
- `카드 설명`에서 현재 효과·각 방식의 비용·부품·성장 정보를 읽는다. 방식을 고르면 행동 안내 줄에서 피해/방어/체력 변화 또는 숨은 결과/예약 시점을 확인한다. 효과 대상 선택창은 자동 선택 기준과 마지막 후보별 예상을 구분한다. 전투 밖의 강화 카드는 원본 설명임을 명시한다. 보상 받기와 상점 구매는 설명 보기와 별도 버튼이다.
- 탐사 종료 후 실제 변화·전체 비용·캐릭터 용량을 보고 남길 변화를 고른다. 다음 출발은 실제 확정 결과를 사용한다. 계정의 영구 자산을 수정하지 않는다.

원정 예측은 즉시 사용과 현재상태 기준 next/after의 방어 포함 피해·체력손실·새 균열·예약 비용/만기를 구분한다. 숨은 드로우/무작위 결과는 감추며 미래 상태의 확정 결과로 표시하지 않는다. 턴 종료 뒤 실제 사건 요약에서 다음 명령을 잠그고 Skip/Continue는 전투 상태를 바꾸지 않는다. 최근40사건과 로컬 복귀를 포함한 실제112Play/3Edit·새macOS시작 근거는 `reports/card_flow_20261016/root_acceptance.md`다. 서버 재접속·디스크/계정 저장·Android/기기는 별도다.

실제 Play/입력·네이티브 빌드의 확인 범위와 남은 항목은 `reports/expedition_client_20261016/`를 따른다. 전체 편성·로비, 최종 아트·음성·접근성 전체와 Android/기기 수용은 후속 범위다.

기존 **전투 연습**은 `Assets/Scenes/MemoryForgeBattle.unity`에서 별도로 실행한다. 아래 전투 연습 조작과 미리보기 기능을 유지한다.

- 손패 카드를 고르고 강조된 적 또는 다른 적을 선택한다.
- `즉시`·`다음 턴`·`다다음 턴`으로 방식을 고르고 비용·사유·미리보기를 확인한 뒤 `사용 확정`을 누른다. 카드·방식·대상 선택만으로는 행동력을 쓰지 않는다.
- `효과 대상`에서 손패·예약·버림 카드의 선택 순서를 정하거나 자동 선택한다.
- `턴 종료`로 적 행동과 다음 턴의 예약 해결을 진행한다. 게이지가 차면 필살기를 사용할 수 있다.
- `다시 시작`은 같은 시드의 연습을 초기화하고, `다른 조우`는 원본 라이브 조우를 순서대로 바꾼다. 계정 자산과 저장 덱을 수정하지 않는다.

`MemoryForgeMcpSmoke.unity`는 과거 연결 확인용 씬이다. 실제 플레이 화면과 구분한다. 최종 캐릭터·적 그림, 전체 편성/탐사 UI, 글자 확대·동작 줄이기 전체 설정과 Android 실기기 수용은 후속 범위다.

콘텐츠는 `data/game_data.json`을 기존 검증 로더로 읽어 전체 typed 정의를 내보낸 것이다. 현재 캐릭터10·카드173·무기22·부품16·유물32·적20·구역2가 포함되며, 출처 SHA와 필드·순서·null 왕복을 검사한다. Unity에는 기존 설치된 Newtonsoft.Json을 사용하고 Core에 JSON 의존성을 추가하지 않는다.

저장소 루트에서 .NET10으로 콘텐츠를 다시 내보낼 수 있다.

```sh
dotnet run --project tools/UnityContentExport --artifacts-path ops/ai/local/client-export -- . client/MemoryForgeUnity/Assets/Resources/MemoryForge/battle-content.json
```

본문 글꼴은 원문 §14.2의 IBM Plex Sans KR이며 `Assets/Resources/Fonts/IBMPlexSansKR-OFL.txt`를 동봉한다. 원문 색상·수치·전체 데이터·기존 설계는 유지한다. 실제 화면·입력 증거와 검증 상태는 `reports/unity_client_20261015/`를 따른다. 자동 시험, Editor 조작, standalone과 Android 수용을 서로 대신하지 않는다.

Original Run definitions are now loaded by `ClientRunContent.Load()` from the same-source `run-content.json` sidecar: all8events, memorycosts/capacities/ascension and fullstats. The existing export command also writes this sidecar next to battle-content.json; its sourceSHA/version must match. Missing or malformed fields fail instead of filling defaults. Actual73Play/3Edit evidence is in reports/run_client_content_20261016. This data bridge does not implement explorationUI, durable save/account/server/device acceptance.
