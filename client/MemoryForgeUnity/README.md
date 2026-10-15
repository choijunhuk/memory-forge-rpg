# Unity에서 현재 개발 결과 보기

Unity **6000.0.46f1**에서 이 프로젝트를 열고 `Assets/Scenes/MemoryForgeBattle.unity`를 실행한다. 이 씬이 빌드의 첫 씬이며 기존 `SampleScene`과 MCP 연결 확인 씬도 보존한다.

현재 화면은 실제 공유 C# 코어를 조작하는 **전투 연습**이다. 세라·하린·니아의 원본 기본 덱과 실제 조우를 사용한다. 전체 탐사 진행·보상·기억 저장·계정 서버와 연결된 완성 게임은 아니다.

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
