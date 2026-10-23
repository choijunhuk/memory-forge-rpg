# P1-RUN-CLIENT-CONTENT — 실제 Unity Run 콘텐츠 연결 (작성자 보고)

- 작성: 공식 Claude Code Opus 5.5, 요청 effort xhigh, 구독 포함 사용량. 실제 세션·모델·effort 증거는 부모 Codex의 CLI 메타데이터를 따른다.
- 편집 파일(허용 9개 안에서만): `tools/UnityContentExport/Program.cs`, `tools/UnityContentExport/UnityContentExport.csproj`, `core/MemoryForge.CoreChecks/StatsDataLoader.cs`(신규), `core/MemoryForge.CoreChecks/Program.cs`(LoadStats 위임만), `core/MemoryForge.CoreChecks/RunDataLoader.cs`(stats 호출 1곳만), `client/MemoryForgeUnity/Assets/Scripts/ClientRunContent.cs`(신규), `client/MemoryForgeUnity/Assets/Scripts/ClientBattleContent.cs`(`RejectDuplicateMembers`를 `static` → `internal static`으로만 변경), `client/MemoryForgeUnity/Assets/Tests/PlayMode/RunClientContentPlayModeTests.cs`(신규), 이 보고서.
- **작성자 실행: 전부 NOT_RUN.** 이 세션에는 명령 실행 도구가 없어 빌드·export·CoreChecks·Unity Play/Edit 시험·해시 계산을 하지 않았다. PASS를 주장하지 않는다.
- 범위: 원문 §7.5–7.7 이벤트, §8.9–8.14 기억 비용·용량, progression stats를 실제 Unity 런타임으로 옮기는 **콘텐츠 연결만** 다룬다. Run UI, 저장, 서버, 실기기 수용은 범위 밖이다. Core 제품 코드, 기존 시험, 씬·프리팹, 생성 리소스, data, 설정, lock은 편집하지 않았다.

## 1. 공유 로더 추출 (수치 변경 없음)

- `Program.LoadStats` 본문 23줄을 **그대로** `StatsDataLoader.Load(JsonElement)`로 옮겼다. 반올림, 기본값, 필드 순서는 바뀌지 않았다.
- `internal static StatsConfig LoadStats(JsonElement p)`의 시그니처는 유지했고, 이제 `StatsDataLoader.Load`에 위임한다.
- `RunDataLoader`가 바뀐 곳은 `Program.LoadStats` → `StatsDataLoader.Load` 한 곳뿐이다. 이렇게 해야 export 도구(자체 `Program` 보유)에 RunDataLoader를 링크할 수 있다.
- export csproj는 `BattleDataLoader`에 더해 `MemoryV2DataLoader.cs`, `RunDataLoader.cs`, `StatsDataLoader.cs`를 링크한다(복사본 아님).
  - **추가 링크 1개**: `JsonGraphComparison.cs`. `MemoryV2DataLoader.Difference`가 참조하므로 빌드에 필요하다. 파일은 편집하지 않았다(stdlib만 사용).
- 새 패키지 의존성은 없다.

## 2. Export: 기존 battle 출력 유지 + 같은 원본의 sidecar

- 명령은 그대로 `<repoRoot> <battleOutputPath>`이고 새 옵션은 없다. 같은 실행에서 `battleOutputPath`와 같은 폴더에 `run-content.json`을 추가로 쓴다.
- **battle 경로**: `Build`, `Validate`, round trip, 바이트 동일성 검사, 쓰기 코드가 모두 이전과 같다. `mf-unity-battle-content-v1` 바이트는 바뀌지 않아야 한다(Root 확인 대상).
- **sidecar** `mf-unity-run-content-v1` 필드: `Schema`, `DataVersion`, `SourceSha256`, `SourceEventCount`, `Stats`(StatsConfig), `Costs`(MemoryCostConfig), `CapacityBase`, `AscensionCapacity`, `Events`.
  - 값은 실제 `RunDataLoader.Load(root)` 결과에서 가져온다.
  - Battle, encounters, startingCommon, characters는 중복 기록하지 않는다.
- **원본 독립 검사**
  - `SourceEventCount`는 loader 결과 길이가 아니라 원본 `events` 배열 길이에서 가져온다.
  - 원본 SHA-256은 로드 전, 로드 후, 쓰기 직전에 세 번 대조한다.
  - `SameAsBattleContent`: Unity가 battle-content로부터 다시 만드는 부분(Memory.Battle 전체, StartingCommon, Characters 순서와 무기 타입, Encounters)이 호스트 `RunDataLoader` 결과와 필드 단위로 같은지 `Same`으로 비교한다.
- **WeaponXpRatioNum/Den**: `LoadStats`는 `levelling.weaponXpRatio`를 읽지 않아 StatsConfig 초기값 1/2가 남는다. 이 값을 조용히 기본값으로 내보내지 않도록, 원본 0.5와 정확히 같을 때만 export한다. 다르면 FAIL. loader 동작은 바꾸지 않았다.
- **round trip**
  - 쓰기 전에 직렬화 → 역직렬화 → `ValidateRun` → `Same` 전체 비교 → 재직렬화 바이트 동일성을 확인한다.
  - `Same`은 `StatsConfig`에 한해서만 공개 property(읽기·쓰기 가능, 인덱서 없음)를 비교하도록 넓혔다. 다른 타입에 공개 property가 있으면 이전처럼 실패한다.
  - battle 타입에는 property가 없으므로 battle 비교 동작은 같다.
- **쓰기**: 두 출력을 모두 검증한 뒤 각각 원자적으로 교체한다.
  - 두 파일을 묶는 트랜잭션은 아니다. 두 번째 쓰기가 실패하면 Unity가 `DataVersion`/`SourceSha256` 불일치로 거부한다.
  - 콘솔 출력에 sidecar 요약 줄이 하나 추가된다.

## 3. Unity 로더 `ClientRunContent`

- API: `Load()`(새 `ClientBattleContent.Load()` + `Resources/MemoryForge/run-content`), `FromJson(string json, ClientBattleContent battle)`, 상수 `ResourcePath = "MemoryForge/run-content"`, `SchemaId`.
- 기존 battle 설정을 그대로 쓴다: `TypeNameHandling.None`, MaxDepth 128, 모르는 멤버 오류, `CheckAdditionalContent`, 메타데이터 무시, `RejectDuplicateMembers`(주석과 중복 멤버 거부).
- **누락과 타입 엄격성**: Core 타입에는 Json.NET 속성을 붙일 수 없어 `StrictResolver`로 처리했다.
  - 모든 멤버에 `Required.Always`를 적용한다.
  - `RunEventEffects`의 각 키와 `RunEventChoice.RequiredCoins`는 `Required.AllowNull`이다. 키 자체는 반드시 있어야 하고, null은 원본에 그 효과가 없다는 뜻이다.
  - 따라서 비용·stat·효과 키가 빠지면 생성자 기본값이나 0(무료)으로 채우지 않고 거부한다.
  - 정수 필드는 32비트 JSON 정수 토큰만 받는다. `"1"`, `1.5`, `1.0`, 범위 초과는 거부한다. 문자열 필드는 JSON 문자열만 받는다.
- **검증**(exporter `ValidateRun`과 같은 규칙)
  - schema, `DataVersion`/`SourceSha256`이 battle content와 일치해야 한다.
  - Stats: 정수는 0 이상, cap과 분모는 양수, `StageRefWeapon`은 실제 무기 rarity여야 한다.
  - 비용·용량·승급 규칙은 0 이상이어야 한다. 원본의 0은 허용한다.
  - 이벤트: 수가 `SourceEventCount`와 같아야 한다. id는 비어 있지 않고 중복이 없어야 한다. 이름·본문, 선택지(1개 이상, null 불가), 라벨·본문, effects(null 불가)가 있어야 하고, need.coins는 0 이상이어야 한다.
  - 참조: curse는 실제 `owner:"curse"` 카드, card는 존재하는 rarity, relic은 빈 문자열이 아니어야 한다.
  - 경제 규칙은 새로 만들지 않았다.
- **조립**
  - `MemoryRulesData`: battle의 `Battle`, `StartingCommon`(복사본)과 sidecar의 `Costs`, `CapacityBase`, `AscensionCapacity`.
  - `RunData`: 위 Memory, `Stats`, `Events`, 원본 순서의 Characters/Encounters.
  - fallback, 프로토타입 콘텐츠, 디스크 쓰기, 네트워크는 없다. `Load()`는 호출마다 새 인스턴스를 반환한다.
  - `FromJson`은 넘겨받은 `battle.Battle`을 복사하지 않고 그대로 쓴다.

## 4. PlayMode 시험 `RunClientContentPlayModeTests` (NOT_RUN, 44 사례)

1. `PackagedSidecarCarriesEveryOriginalCostCapacityStatAndEvent`
   - 원본 `data/game_data.json` 값을 그대로 옮긴 fixture와 비교한다: rules.capacityBase/memoryCost, progression.stats, levelling, meta.version, 이벤트 8개 전문.
   - 비용 9개는 필드 목록 완전성까지 확인한다. Stats는 property 목록 완전성과 함께 경로별로 비교하고, weaponXpRatio도 확인한다.
   - 이벤트는 `ToJsonGraph`가 원본 이벤트 JSON과 같은지 본다.
   - sidecar 최상위 키 9개의 완전성, 각 섹션과 런타임 값의 DeepEquals, sidecar와 battle content의 SHA·버전 일치를 확인한다.
   - characters/encounters/startingCommon의 출처와 순서를 확인한다.
2. `OriginalMemoryCostsAndCapacityReachTheActualCoreRules`
   - 실제 `MemoryV2.Cost`에서 upgrade 1, +remove 1, +addA 2(`sera_timed`, rarity A)가 원본 값대로 나오는지 본다.
   - `CapacityOf`가 asc 0–5에 대해 6,6,7,7,8,8인지 본다.
3. `ActualRunCreateFromPackagedContentMatchesOriginalJs` (legacy, v2)
   - 실제 `Run.Create`의 초기 해시가 기존 시험에 기록된 JS 결과(`56a6ba86…`, `11a43e74…`)와 같아야 한다.
   - HP와 MaxHP는 243(81×3), phase는 MAP이어야 한다. 상태 주입은 없다.
4. `UnmodifiedPackagedTextLoadsThroughFromJson`: 양성 대조. 재직렬화만으로는 실패하지 않음을 확인한다.
5. `MalformedSidecarFailsClosed` (38 사례): 아래 입력이 모두 `InvalidDataException("run-content: …")`로 실패해야 한다.
   - Stats: 개별 stat, ratio 분모, stageRefWeapon 누락, null Stats.
   - 비용: 누락, null, 문자열, 1.5, 1.0, 범위 초과, 음수, 모르는 키.
   - 용량: CapacityBase, AscensionCapacity, ascension Add 누락.
   - 이벤트: Events 누락, 빈 Events, 이벤트 1개 탈락, SourceEventCount 누락.
   - 선택지: 효과 키 누락(ev_promise 동전 -40), RequiredCoins 누락, null 선택지, 빈 선택지, 모르는 효과, 이벤트 id 중복.
   - 참조: curse, card rarity, stage weapon 미해결.
   - 출처: SHA 불일치·누락, 버전 불일치·누락, schema.
   - 형식: `$type`, 모르는 최상위 멤버, 중복 멤버, 뒤따르는 내용, 주석.
6. `EveryLoadReturnsFreshDataWithoutASharedMutableCache`: 한 결과를 변경해도 다른 결과와 새 `Load()`가 영향받지 않는다.

## 5. Root 확인 요청 / 알려진 사항

- **Root 실행 순서 제안**
  1. CoreChecks 빌드와 전체 실행(LoadStats 추출 회귀 확인, Core202/R0 trace).
  2. export 도구 빌드.
  3. `dotnet run --project tools/UnityContentExport -- <repoRoot> client/MemoryForgeUnity/Assets/Resources/MemoryForge/battle-content.json`
  4. battle-content.json 바이트가 이전과 같은지 확인하고, sidecar의 모든 필드를 실제 호스트 입력과 독립 비교한다.
  5. Editor import → Play/Edit 시험.
- 이 worktree에는 `Assets/Resources`가 없다. 생성 리소스는 Root 담당이다.
- **원본 파일 SHA 고정값**: 작성자가 해시를 계산할 수 없어 시험에 넣지 않았다. 시험은 sidecar SHA와 battle-content SHA의 일치만 확인한다. 실제 파일 해시 대조는 Root의 독립 비교가 맡는다.
- **이벤트 id 중복**: Core `EventChoose`는 "같은 id면 마지막 것" 의미를 따르지만, 원본에는 중복이 없어 export와 Unity 모두 중복을 거부(fail closed)하도록 했다. 원본에 의도된 중복이 생기면 설계 결정이 필요하다.
- **CapacityBase 원천**: 기존 loader가 쓰는 `rules.capacityBase`(6)를 그대로 따른다. `progression.stats.capacity.base`(6)와의 교차검사는 새 규칙이 될 수 있어 넣지 않았다.
- **IL2CPP/Android 위험(범위 밖)**: `StatsConfig` property setter는 Unity 안에서 Json.NET 리플렉션으로만 쓰인다. 높은 stripping 설정에서는 link.xml 등 보존 설정이 필요할 수 있다(설정 파일은 이번 편집권 밖). Editor Play 결과를 실기기 증거로 쓰지 않는다.
- **asmdef**: 이 worktree의 PlayModeTests asmdef는 `MemoryForge.Core`만 참조한다. 그런데 기존 `RunProgressionPlayModeTests`가 이미 `MemoryForge.Client`를 사용하므로, 새 시험도 같은 방식으로 컴파일된다고 가정했다. Root의 Editor 컴파일로 확인이 필요하다.
- **후속 제안(범위 밖)**: 기존 `RunProgressionPlayModeTests.Content()`는 아직 빈 비용과 기본 Stats, 이벤트 없음으로 동작한다. 이후 작업에서 `ClientRunContent.Load()`로 바꾸면 이벤트 노드를 포함한 사례를 검증할 수 있다.
