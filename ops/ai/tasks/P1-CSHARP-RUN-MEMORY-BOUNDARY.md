# P1-CSHARP-RUN-MEMORY-BOUNDARY — 다음 실행 경계

- 상태: **소스 조사·구현 인계만 완료**. 이 문서는 C# Run/기억 저장 구현·명령 재생·복원·서버 수용 증거가 아니다.
- 기준: ROOT `/Users/choi/Documents/Memory_forge`, committed `3d33c4cb2e0c7cee14ced2c67c33b8fb55cef267`. 아래 줄 번호는 이 커밋 기준이다. 진행 중 A1 작업 파일은 조사 근거로 사용하지 않았다.
- 이번 담당/쓰기 범위: Codex 자식, **이 새 문서 한 파일만**. 재위임·다른 AI/AI CLI/API·빌드·시험·데이터 주입·Unity 조작은 수행하지 않았다. 실제 모델 증거는 부모가 보관한 자식 실행 메타데이터를 따른다.
- 실행 계약: `docs/AI_WORKFLOW.md:98-126`; 요구사항: 원본 `07_chapter.md:5-69`, `08_chapter.md:66-120,142-163`, `16_chapter.md:21-35`, `20_chapter.md:74-87`. 게임 내 역할은 탐사 진행으로 얻은 실제 변화만 안정 개체에 적용하고 기존 영구 덱을 보존하는 것이다.

## 1. 다음 구현 순서와 종료 조건

1. **M0 — 순수 기억 규칙부터 독립 구현.** `MemoryV2`의 inspect/baseline/listChanges/cost/applyChanges/apply/recover/migrate와 현행 `Run.capacityOf`를 typed C#으로 옮긴다. JS가 실제 정의로 생성한 입력/결과 및 오류/무변경 대조가 모두 맞으면 이 단위만 완료다. Battle·서버 의존성은 없다.
2. **R0 — 실제 Run 생성·지도.** 전체 setup/state/data DTO, `Run.Create`, 키 발급, 능력치 파생, 지도·도달 가능 노드를 연결한다. JS와 생성 상태 전체·RNG·해시·질의 순수성이 맞으면 완료다. 전투를 끝낸 척하는 상태나 빈 `afterBattle`로 다음 단계에 가지 않는다.
3. **R1 — A1 통합 후 실제 탐사 진행.** 검토·재검증된 Battle의 play/ult/end와 Run의 전투 연결, 보상·노드·기록소·legacy/v2 저장을 연결한다. 실제 `Run.Create → enter(내부 begin) → Battle 명령 → afterBattle → 다음 노드 → 종료 → commitMemory`가 실행되어야 한다. 기존 700 short+300 full 전량의 모든 시도/오류 코드/해시가 일치할 때 전체 legacy 재생 완료다.
4. **M1/R2 — v2와 복원까지 닫기.** 별도 v2 전량 corpus와 아래 보완 사례, 명령 경계에서 저장한 신뢰 스냅샷을 복원한 뒤 남은 **동일 명령** 실행을 검증한다. 기존 덱·최종 비용·요청 이력·영수증 및 미래 RNG가 같을 때 C# Run/Memory 범위를 완료한다. 서버 DB/Unity/실기기 수용은 별도로 남긴다.

각 단위는 개발 → 관련 시험 → 반대 AI 독립 소스 검토 → 수정 → 재검증 → 통합 순서로 진행한다(`AI_WORKFLOW.md:112-114`). 공용 CoreChecks 진입점·기존 A1 파일·Unity 쓰기는 부모가 통합한다. 완료 카운트는 Memory 규칙, Run 생성, 실제 legacy/v2 재생, 복원으로 나눠 보고한다.

## 2. 실제 타입·데이터·파일 경계

다음 이름은 **신규 구현 제안**이다. 현재 존재하거나 승인·완료된 API로 읽지 않는다. 순서 보존 목록 + ordinal ID lookup을 사용하고, 실행은 concrete 메서드/분기로 옮긴다.

| 파일/타입 | 책임과 소스 근거 |
|---|---|
| `core/MemoryForge.Core/MemoryV2.cs` | `MemoryCardSpec`, 캐릭터별 `MemorySnapshot`, `MemoryChange`, preview/apply 결과, migration/store/receipt/history DTO와 순수 연산. 원본 `proposals/memory_v2.cjs:10-117` 재사용 설계; JS 실행기·reflection·새 패키지는 필요 없다. |
| `core/MemoryForge.Core/RunDefinitions.cs` | `RunData`, `RunSetup`, `RunPartyEntry`, 지도/조우/사건 정의와 현행 용량 설정. 기존 Battle 정의를 참조하되 Run 필드를 거기에 임의 축약하지 않는다. |
| `core/MemoryForge.Core/RunState.cs` | 아래 상태·카드·pending DTO, deep copy 및 명시적 `ToJsonGraph`; 이후 신뢰된 전체 상태 복원. 부재 필드와 null을 구분한다. |
| `core/MemoryForge.Core/Run.cs` | Create·지도·질의·실제 메서드들·Battle 연결·종료 캡처·legacy/v2 commit. 원본 `engine/rules-core.js:1518-2081`. |
| `core/MemoryForge.CoreChecks/RunDataLoader.cs`, `MemoryChecks.cs`, `RunReplayChecks.cs` | Host의 기존 System.Text.Json로 typed 정의/명령 로딩, 독립 결과 비교와 전량 재생. Core에는 JSON 패키지를 추가하지 않는다(`MemoryForge.Core.csproj:1-12`). `Program.cs:116-121` 연결은 부모 담당. |
| 새 JS 단위 oracle/시험 + 별도 v2 corpus | 기존 `tools/gen_traces.mjs`·현재 `traces.jsonl.gz`를 재사용한다. 부족한 Memory/복원 사례만 별도 artifact에 추가하고 기존 전량·출처 검사·시험을 유지한다. |

`RunData`는 원본 순서의 cards(`id/name/owner/rar/start/pool/growth/evo`), characters(`id/weapon/resonance`), weapons(`id/card/type/rarity`), parts(`id/name/slot/rarity`), relics(`id/name/rarity`), `startingCommon`, 구역별 `encounters.normal/elite/boss`, events(`id/choices[].need/choices[].fx`), `rules.capacityBase/memoryCost`, `progression.stats.capacity.ascension` 및 기존 성장 설정을 보존한다. 내용과 수치는 `data/game_data.json`에서 읽으며 후반 후보 overlay를 라이브 데이터로 섞지 않는다.

사건은 need.coins와 fx의 coins/hp/heal/maxHp/maxHpPct/nextApBonus/curse/relic/card/part/partRare/variation/growth/cardCopy/cardRemove를 typed 필드로 보존한다(`rules-core.js:1884-1914`). 기존 카드 효과 DSL과 다른 도메인이므로 카드용 op 해석기로 대신 실행하지 않는다.

`RunSetup`은 party, memory, relics?, seed?, length?, zones?, bosses?, stageLevel?, difficulty?와 v2의 memoryPolicy/runId/memoryVersion/useMemory를 받는다(`rules-core.js:1521-1567`). `RunPartyEntry`는 charId/res/weaponId/refine에 level/asc/weaponLevel/armor/accessory/skillLv/ultLv 및 파생 power4/hp/gearBps를 보존한다. `Stats.Derive`용 `GrowthEntry`는 WeaponRarity를 받으므로 weaponId→콘텐츠 rarity 변환이 필요하며 res/asc/identity를 그 타입으로 대체하지 않는다(`Stats.cs:33-47,67-85`). Run은 제공된 파생 전투력/HP를 그대로 신뢰하지 않고 다시 파생한다(`rules-core.js:1548-1550`).

### Run 상태·개체·pending

| 그룹 | 정확한 상태 키/형태 |
|---|---|
| 초기 공통 | schema, seed, rng, length, difficulty, stageLevel, zones, zoneIdx, bosses, zone, floor, pos, map, party, hp, maxHp, coins, deck, relics, partsFound, pending, battle, phase, log, stats, runFlags, offered, result, startMemory, seqCard; 생성 중 hpRef 추가(`:1524-1550`). |
| v2만 | memoryPolicy, runId, memoryVersion, memoryBaseline, memoryBefore; useMemory:false이면 playBefore; 종료 시 memoryAfter; 저장 후 savedMemory. legacy에 이 필드를 null로 채우지 않는다(`:1539-1566,2014-2079`). |
| 실행 중 추가 | battleType(`:1671`), runFlags의 coinsBonus/removals(`:1690-1691,1870`). UI가 붙이는 visited는 엔진 생성 필드가 아니다(`prototype/src/ui.js:1049`). |
| 지도 | `{zone,floors:[노드[]],boss}`; 노드 `{id,f,i,type,next}`는 층/등장 순서, boss는 `{id,type,f,i}`로 next가 처음에는 없다(`:1583-1625`). |
| Run 카드 | key/id/owner + 부재 가능한 assigned/wielder/weapon/plus/mods/growth/from/gained/evolved. 전투 uid/temp/free/token과 영구 key를 합치지 않는다(`:1553-1559,1755,1779,1905`). |
| 통계·플래그·결과 | stats={battles,elites,kills}; 초기 runFlags={envelopeUsed,refusals,nextApBonus}; result=null 또는 `{outcome:defeat|clear|return,floor}`(`:1531-1532,1685,1790-1797`). |
| pending union | reward: coins/cards/relic?/part?/evolutions[{key,options}]; rest: uses; forge: uses/offers{key,parts}?/fromEvent?; shop: cards/parts/relics[{id,price,sold?}]/removeCost(nullable); event: id/done?/choice?; archive; save:defeat; done(`:1643-1646,1693-1697,1789-1799,1828-1862,1900-1914`). |

## 3. 명령 표면과 Battle 의존성

**Run에는 JS `command({rid,v,...})` 셸이 없다.** 원본은 메서드를 직접 호출하며 Run.s에는 v/receipts가 없다. 기존 trace도 Run 명령을 `{scope:'run',kind,args:[순서 있는 positional 인자]}`로 기록한다(`gen_traces.mjs:40-41,76-98,131-132`). 서버의 requestId/state_version은 별도 어댑터 계약이다. 순수 질의를 실행 명령으로 만들거나 조회 때문에 RNG를 소비하지 않는다.

| 실제 메서드 | 인자/역할 | Battle 의존 |
|---|---|---|
| enter | nodeId; 도달 검사 후 종류별 진입(`rules-core.js:1637-1649`) | 전투 노드가 실제 Create+begin을 호출 |
| battleCmd | kind, args; Battle.do → s.battle 교체 → 종료면 afterBattle(`:1673-1699`) | **전체 A1·다음 턴/종료 훅 필요** |
| takeCard / skipCard / takeRelic / takePart / evolve | cardId / 없음 / id / 없음 / key,branchId(`:1751-1781`) | 실제 보상 생성은 전투 종료 필요 |
| rest / forgeOffer / forgeApply / forgeRemove | choice,key? / key / partId / key(`:1809-1843`) | 연산 자체 독립; 정상 노드 도달은 전투 뒤 |
| applyFoundPart / assign | partId,key / key,charId; 저장 화면이면 memoryAfter 재캡처(`:1801-1806,1845-1852`) | 연산 자체 독립 |
| buy / eventChoose | kind,id?,key? / idx,key? (단일 key 또는 key 배열)(`:1864-1914`) | 연산 자체 독립; RNG·실제 노드 경로 보존 |
| proceed / archive | 없음 / return 또는 continue; 정상 종료·구역 전환(`:1783-1799`) | 실보스 승리 필요 |
| commitMemory | legacy selection{cid:index[]} / v2 request,currentStore; 결과 형태가 다름(`:1975-2080`) | 실제 terminal 캡처 뒤 실행 |

질의/내부 helper는 available/nodeById, baseKit/defaultWeapon/nextCardKey/genMap, encounterFor, pendingEvolutions/rarityRoll/cardChoices/relicChoices/partChoices/makeShop, capacityOf, memoryChanges/deckMemorySnapshot/memorySnapshot/captureMemory/previewMemory다. 기존 corpus의 16 Run kind에는 **assign와 preview/migration/중간 restore가 없다**. 이 표의 독립성은 해당 연산을 먼저 구현할 수 있다는 뜻이며, 상태를 주입해 전투·탐사 재생 완료를 주장하는 허용이 아니다.

Battle 연결에서는 Run RNG의 `Next()`로 전투 seed를 먼저 뽑고 그 뒤 조우를 고른다. Battle.create가 deck.key를 무시하므로 **카드 생성 순서대로 key를 연결한 후 begin**을 실행한다(`:1662-1671`). Battle 명령의 새 상태를 Run.s.battle에 매번 연결하고, afterBattle은 HP/runFlags/growth 반영 → 패배 캡처 또는 통계·동전·카드·유물·부품 순서로 RNG를 소비한다(`:1675-1699`). 토큰을 실제 획득 카드로 승격하거나 result/보상을 fixture에서 가져오는 지름길은 이 경계를 검증하지 못한다.

## 4. 기억 v2의 확정 의미와 정확한 처리

확정 기준은 `docs/decisions_v0.8.1.md:18-20`이다. 원본 §8.8/§8.16의 ‘미연결/정상 정책 미결’ 서술은 배포 이력이다. 현재 JS/HTML은 **정상 종료도 before에 선택한 변화만 누적**, 패배는 파티 전체 0~1개, 선택 없음은 기존 덱 보존이다. legacy는 기본 덱 재구성을 별도 정책으로 유지한다. 무료 용량 성장 이동(원본 §8.13)은 미적용 목표이므로 이식 중 현행 `capacityOf`의 승급·공명 보너스를 바꾸지 않는다.

| 표면 | 보존 조건/오류와 순서 |
|---|---|
| MemoryCardSpec / inspect | key/id, 선택 plus/from/mods{c,e}/assigned. 캐릭터 집합, 전역 key 유일성, 고유/배정 공용 소유, token/weapon 배제, bool plus, 실제 성장 원본→허용 evo, 영구 슬롯 부품 검증(`memory_v2.cjs:10-29`). |
| baseline / diff | base:cid:index 키. cid는 UTF-16 ordinal 정렬, before 개체 순서대로 remove→evolve→upgrade→c/e part, 이후 after의 신규 개체 순서대로 add. change ID는 encodeURIComponent(cid/key)의 정확한 URI escaping을 사용(`:31-55`). |
| cost / apply | result 전체와 baseline을 비교한다. 명시 baseline이 있으면 **key로** 원본을 짝짓고, 신규 진화 카드는 원본 희귀도+진화+강화+두 슬롯 부품을 합산한다. apply는 선택 ID 순서로 before 사본에 적용 후 전체 유효성/패배 수/용량을 검사한다(`:57-91`). preview는 error를 반환하고 commit은 거절한다. |
| 기본 덱 플레이 | memoryBefore를 출발 때 유지하고, 이미 진화·제거된 기본 개체는 별도 run 키로 플레이한다. playBefore→after 변화 중 기존 key 변화만 기존에 적용하고 새 key는 완전 add로 남긴다. 선택 없음이 이전 진화를 되돌리지 않는다(`rules-core.js:1553-1566,2035-2047`). |
| 영구 스냅샷 | deckMemorySnapshot은 무기/token/저주를 제외하고 key/id/plus/from/mods/assigned만 추린다. growth·temp·전투 비용/버프는 저장하지 않지만 from은 남긴다. 종료 후 assign/applyFoundPart는 캡처를 갱신한다(`:2018-2047,1806,1852`). |
| migrate | `{schema:'mf-memory-v2',version:0,memory,quarantine,legacyBackup,history:[],receipts:{}}`. cid 정렬, 기존 key 유지 또는 baseline/legacy:cid:index를 결정적으로 부여; 오류/초과 cid는 **전체 원본을 격리**하고 절삭하지 않는다(`memory_v2.cjs:93-115`). 원본 바이트 백업은 Host 책임이다. |

v2 commit의 request는 `{requestId,expectedMemoryVersion,runId,selectedChangeIds}`이고 current store는 서버/저장소에서 읽은 값이다. 처리 순서는 `rules-core.js:2055-2080`을 따른다.

1. 요청/Run ID/current store 검사와 inspect 후, `{runId,expectedMemoryVersion,selectedChangeIds:정렬된 사본}`의 **JSON.stringify 본문**을 만든다. 일반 state hash와 다른 계약이다.
2. current.receipts의 **own key**를 먼저 조회한다. 같은 body면 저장 응답을 복사해 DONE으로 마무리하며, 다른 body는 REQUEST_CONFLICT다. 재시도는 phase/version 검사보다 앞선다.
3. 신규 요청만 terminal/save·기억 버전·출발 before 일치·preview 전체 비용을 검사한다. 오류는 Run/current store를 바꾸지 않는다.
4. current 사본에 파티별 결과를 병합하고 version+1, history{runId,requestId,version,before,memory,selectedChangeIds}, receipt{body,response}를 기록한 뒤 Run을 DONE으로 만든다. history의 selectedChangeIds는 **요청 원순서**, body만 정렬이다. 반환은 `{state,response:{memory,used,version}}`이다. 비파티 기억과 기존 backup/quarantine/history/metadata는 보존한다. 선택 []도 덱만 보존하며 버전/이력은 증가한다.

## 5. 권한·직렬화·현재 caller의 위험

| 경계 | 이식 때 지킬 근거와 의미 |
|---|---|
| Core ↔ 서버 | 소유 계정/캐릭터·seed·레벨/장비·before/after는 서버 신뢰 입력이다(원본 §7.1/§8.9-11). Core가 클라이언트 최종 덱/비용을 받는 저장 API가 되어서는 안 된다. private Run.s/RNG/전투 draw 순서를 공개 응답으로 내보내지 않는다. Run에는 현재 publicView가 없다. |
| 원자성 | JS Run 일반 메서드는 전체 후보/교체 셸이 없으므로 모든 오류 경로의 원자성을 이미 보장한다고 적지 않는다. 새 C# 서버 어댑터는 후보에서 실행 후 성공 시 교체하고 DB 트랜잭션으로 묶는다(원본 `17_chapter.md:23-44`). 유효 명령의 JS 순서/결과를 바꾸는 규칙 수정과 분리한다. |
| 저장 caller | `prototype/src/ui.js:1028-1053`은 최신 v2 store를 다시 읽어 Run을 만든다. `:1055-1097`은 pending DTO→체크포인트→최신 store→clone Run commit→memory 쓰기 성공→live 교체 순서다. Core commit 성공은 영속 저장 성공이 아니다. 쓰기 실패는 pending requestId를 유지하고 live Run/store를 보존한다. localStorage는 DB 잠금·다중 탭 CAS가 아니다. |
| 복원 caller | `ui.js:256-288,1319-1339`은 Run.s/선택/pendingSave/lastSave를 보관하고 `new MF.Run(DATA,snap.run)`으로 복원한다. JS constructor(`rules-core.js:1519`)는 전체 검증을 하지 않으며 Battle receipt는 상태 밖이라 복원 시 새 컨테이너다. C#은 신뢰 저장소의 검증된 snapshot 복원과 네트워크 입력을 분리하고, 실제 남은 명령·영수증 재시도 의미를 별도 검증한다. 앱 종료를 패배로 만들지 않는다. |
| 두 JSON 계약 | CanonicalState는 UTF-16 키 정렬·배열 순서·재귀 log/ev 제외용이다. v2 receipt body에는 선택 ID 정렬만 적용하며, `:2071`의 before 비교는 원본 JSON.stringify의 속성/배열 순서까지 영향을 받는다. generic 정렬 hash로 모두 대체하면 거절 의미가 달라진다. false/부재/null 및 부분 mods의 모양을 보존한다. |
| 개체·순서 | run:runId:seq 키 충돌 회피(`:1569-1573`), 동일 카드 두 개, 같은 key 진화·복제의 새 key, 공용 배정, 영구/임시 부품을 분리한다. 딕셔너리 순회 대신 콘텐츠·개체·제시·선택 순서를 명시한다. 기존 key의 다른 캐릭터 재배정은 별도 흐름 요구 오류다(`memory_v2.cjs:33-45`). |
| 수치 | 지도 난수 분기/짧은 평가 순서와 보상 RNG 순서를 그대로 옮긴다. archive 15%, 회복·상점 가격·보상 확률·초기 동전60은 source 그대로다. 사건 hpk에는 음수 hp/maxHp가 있으므로 JS Math.round의 음수 절반도 맞춰야 한다(`rules-core.js:1887-1893`); 기존 Stats.RoundDiv는 비음수 전용(`Stats.cs:53`). |
| 보존형 이관 | UI는 raw legacy 바이트를 별도 백업한다(`ui.js:176-207`). migration/quarantineCleared 등 Host metadata와 불량 원본을 typed 저장에서 떨어뜨리지 않는다. 전체 콘텐츠/불량 legacy를 정수 전용 CanonicalState로 읽어 소수·알 수 없는 필드를 반올림/삭제하지 않는다. 신뢰 포맷 밖의 복원은 명시적 거절/격리로 남긴다. |
| 아직 서버 접합 안 됨 | OpenAPI `:259-274`는 selection 인덱스 legacy 요청, SQL `memory_deck:129-148`은 캐릭터별 version이며 v2 store는 단일 memory version이다. §17.5의 stable key/버전/이력 접합은 P1-2에서 해결한다. 이 문서로 API/DB 정책을 정하거나 운영 이관하지 않는다. |

정적 조사에서 추가 재현이 필요한 항목: `startBattle`이 nextApBonus를 Battle.runFlags에 복사한 뒤 Run 쪽만 0으로 만들고(`:1664-1666`), afterBattle이 Battle.runFlags를 다시 병합한다(`:1682`). 이벤트의 ‘다음 전투’ 보너스(`:1894`) 수명은 독립 JS 재현 후 판정할 질문이다. 이번 조사에서는 실행·규칙 수정하지 않았으며, C#만 임의 보정해 corpus와 다른 수치를 만들지 않는다(원본 §20.9).

## 6. 재사용과 독립 oracle/수용 설계

기존 `Rng`의 uint 상태/Below/Pick(`Rng.cs:19-84`), `Stats.Derive/StageScale`, `RuleException.Code`, `CanonicalState.Serialize/Hash`, `JsonGraphComparison.Difference`를 재사용한다. JS Stats.combine(`rules-core.js:95`)의 정수 동치만 필요한 경계에 추가한다. `TraceHashChecks`의 정수/중복키/UTF-8/손상 gzip/출처 검사(`:125-221,258-274,336-410`)를 유지·공유하고 serializer/파서 사본을 늘리지 않는다. 현재 해당 파서/reader는 private이므로 새 검사 파일에서 바로 호출할 수 없다. 부모 통합 때 Host 안에서 최소한으로 공용화하거나 기존 검사에 실제 실행부를 연결한다. 기존 Battle의 typed 실행·상태 projection을 연결하며 ReservationBook 등 두 번째 전투 상태를 만들지 않는다.

현재 trace 계약은 `tools/gen_traces.mjs:20-41,67-98,101-123,246-298`이다. 각 scenario의 setup으로 **새 C# Run을 실제 생성**하고, Run positional 명령과 Battle의 정확한 rid/v/kind/args를 실행한다. enter 안의 begin과 Battle 종료 후 afterBattle도 실행한 뒤 `{run:Run.s,memory:v2Store|null}`을 매 시도마다 hash한다. 초기/최종 snapshot을 입력 상태로 주입하지 않는다. 기존 `TraceHashChecks.cs:28-52,180-217`는 snapshot/선언된 envelope 검사이며 실제 명령을 실행하지 않는다.

| oracle/시험 | 완료 조건과 현재 빈틈 |
|---|---|
| 고정 기존 corpus | 700 short+300 full, 기존 시드/소스/273,748명령 기록을 유지하고 모든 해시·성공/거절 코드를 C#에서 재생. 현재 수치는 `CURRENT.md:41,53`의 이력이며 이번에 재실행하지 않았다. 알려진 허용 오류만 수집하고 예상 밖 오류/guard는 fatal로 유지한다. |
| 별도 전량 v2 | 기존 generator의 `--memory-policy v2`로 **별도** 700+300 artifact를 만든다. 실제 빈 store migration→Run→terminal versioned commit, 메모리를 포함한 모든 hash 및 DONE을 비교한다(`gen_traces.mjs:115-118,161-185,301-325`). 기존 legacy corpus를 이 파일로 대체하지 않는다. |
| 범위 보완 단위 | source hash/setup/실제 명령열/반환·오류/전체 전후 상태·hash를 저장하는 최소 보완 oracle. 모든8사건 선택과 key 배열, 보상 공정성·중복/희귀도, 희귀/보스 유물·부품, rest/forge/shop 회수·실패, 다구역 return/continue를 고정 사례로 보강한다. 기존 랜덤 corpus가 모든 분기를 덮는다고 추정하지 않는다. |
| Memory 회귀 | 선택 없음/정상 다중/패배0~1/6+3 초과/원본 대 중복 key 제거/진화만 선택/슬롯 교체/기본 덱 플레이/종료 후 부품/공용 assign/비파티 덱 보존. source는 `memory_v2_integration.test.mjs:52-248`, `memory_v2_edges.test.mjs:16-53`, proposal14개다. |
| 요청·이관·복원 | 동일 ID 재시도/변경 body/선택 순서 변경/`__proto__` 등 own receipt/버전·before 충돌/반환 사본, 결정적 key·원본 격리/백업. 명령 경계에서 직렬화→복원→동일 suffix 실행으로 상태/RNG/저장 응답을 비교한다. 단순 deserialize→hash는 복원 수용이 아니다. |
| 독립성·실패 순수성 | 고정 기대: 기본 비용0, 동일 종류의 원본 제거+새 개체 추가는 비용2(`memory_v2_edges.test.mjs:41-44`), 기존6+새S3/용량6 거절, 새S+강화+희귀부품=6, 선택 없는 before 보존. 거절마다 Run/메모리/RNG/이력/receipt를 함께 대조한다. source/명령/hash/snapshot 변조·누락/중복 ID도 거부한다. |

기존 Memory integration은 `finishForSelection`과 일부 phase/성장/부품 주입으로 **저장 경계를 격리**한다(`:27-30,81-97,159-175`). 이 시험은 보존하되 실탐사 증거로 합산하지 않는다. 새 전체 재생 oracle은 원본 데이터와 실제 Create/명령만 사용하고 종료·보상·성장 상태를 직접 넣지 않는다. pure Memory 테스트의 입력 snapshot은 그 범위의 규칙 증거로만 보고한다.

검증/통합 시 부모는 M0/R0/R1/M1-R2 각 단계의 새 집중 시험, 기존 primitive/canonical/opening/turn/A0/A1, 전체 JS·proposal·Python·docs·handoff 및 필요한 Unity 검사를 실행하고 독립 Opus 검토를 회수한다. 실제 Run 명령 재생이 끝날 때까지 `BattleRunCommandReplay: NOT_IMPLEMENTED`를 snapshot PASS로 대체하지 않는다. 서버 인증·DB locking/rollback·운영 이관은 P1-2의 실제 환경 시험 때 수용한다.

**지금 시작할 다음 작업:** 별도 M0 담당 범위를 `MemoryV2.cs`와 독립 Memory oracle/검사로 고정하고 `proposals/memory_v2.cjs:10-117`의 inspect→baseline→diff/cost→apply/migrate부터 이식한다. A1의 현재 파일을 건드리지 않고 완료 가능한 첫 단위다.
