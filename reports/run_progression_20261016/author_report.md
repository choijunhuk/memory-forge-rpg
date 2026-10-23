# P1-R1A-RUN-ORACLE — R1a 실제 탐사 명령 전체 상태 JS oracle (작성자 보고)

- 작성: 공식 Claude Code Opus 5.5, 요청 effort xhigh, 구독 포함 사용량. 실제 세션·모델·effort 증거는 부모 Codex의 CLI 메타데이터를 따른다.
- 편집 파일(허용 범위 3개만): `tools/gen_run_progression_vectors.mjs`, `engine/tests/run_progression_vectors.test.mjs`, 이 보고서.
- **작성자 실행: 전부 NOT_RUN.** 작성자 세션(최초 작성과 P1-R1A-ORACLE-SEMANTIC-FIX 수정 모두)에는 명령 실행 도구가 없어 생성·시험·해시 계산·fixture 쓰기를 하지 않았다. 실행 결과는 아래 "Root 실행 증거"에 있는 Root의 측정뿐이다.

## 0. Root 실행 증거 (작성자 실행 아님)

Root가 자기 환경에서 실제로 실행하고 보고한 값이다. 작성자는 재현하지 않았다.

- **생성**: PASS. 5 사례, 시도 2,684회(확정 1,370, 거절 1,314), 질의 1,074개. gzip 2,000,612 바이트, 비압축 29,905,373 바이트, 4.84초, RSS 약 720 MiB.
- **`--check`**: PASS. 소스·상태·반환값·질의·coverage 기록 전체가 새 실행과 정확히 일치했다. R0 decoder import와 manifest 감사도 문제없이 통과했다.
- **최초 시험**: 4/5 PASS. 실패한 1개는 작성자의 잘못된 해석에서 나왔다. 메시지는 `AP bonus must be earned in seed 2654 and independently in another seed: 2654`였다. 소스나 parser 결함은 없었다.
- **seed 2654 실측 coverage**:
  - 획득: 78번째와 1519번째 시도에서 각각 +2.
  - 소비: 80번째 시도에서 AP6, 1524번째 시도에서 AP6.
  - 그 사이 실제 전투 진입 8회(91·157·228·282·1078·1230·1335·1410번째 시도)는 모두 AP4.
  - 이 시작들에서 Run 쪽 보너스와 battle에 복사된 보너스는 모두 0이었고, chrono_crown·cracked_hourglass 유물은 없었다.
- **수정**: 이 실측에 맞춰 시험 해석만 고쳤다(§3). 생성기·엔진·데이터·fixture는 바꾸지 않았으므로 SHA 변경이 없다. 수정한 시험은 **NOT_RUN**이며 Root가 다시 실행한다.
- 범위: 원문 §7/8/16.2/20.9 P1-1의 실제 `Run.create → enter → Battle → 보상 → 노드 서비스 → archive → 패배/클리어 → 기억 저장`을 다섯 사례로 기록하는 **JS_ORACLE_ONLY** 증거. 기존 1000 trace 해시 corpus, R0 factory/query 행렬과는 별개다. **R1 C# 명령 재생은 0**이며 Unity·DB·실기기 수용과도 무관하다. Core·공유 helper·기존 reader/test는 편집하지 않았다.

## 1. 사례와 실행 방식

| id | 입력 | 역할(생성기가 실제 관측으로 판정) |
|---|---|---|
| `seed1000-short-legacy` | seed 1000, short, legacy | 일반 탐사 |
| `seed1000-full-legacy` | seed 1000, full, legacy | 일반 탐사 |
| `seed1102-full-legacy` | seed 1102, full, legacy | begin 중 패배 |
| `seed1102-full-v2` | seed 1102, full, v2 | begin 중 패배, v2 저장 |
| `seed2654-full-legacy` | seed 2654, full, legacy | 새로 얻은 ev_rift AP 보너스 |

사례마다 다음을 수행한다.

1. 기존 `generateScenario`로 실제 trace를 만들고 `replayScenario`로 해시 재생을 확인한다.
2. 새 `Run.create(trace.setup)`와 기존 `MemoryV2.migrate(data,{},{})` store에서 시작해 trace 명령을 공개 Run 메서드나 `run.battleCmd`로 다시 실행한다. v2 `commitMemory`가 성공하면 반환된 store가 다음 store가 된다.
3. 모든 시도에서 outcome, errorCode, stateHash를 trace와 대조하고 DONE과 finalStateHash까지 확인한다.

trace의 initialState/finalState는 엔진에 복원하지 않는다. 원본 데이터 변형, product 메서드 mock, 기대 상태 주입도 없다. trace가 기록한 RuleError 코드 외의 예외는 모두 생성을 중단시키고 기존 출력을 보존한다.

## 2. 호스트(C# R1)에 영향을 주는 결정

- **파일과 스키마**: 파일은 `engine/tests/vectors/run_progression_vectors.json.gz`, schema는 `mf-run-progression-v1`, status는 `JS_ORACLE_ONLY`다. 최상위 필드는 `schema,status,scope,dataVersion,sources,contract,denominators,coverage,cases` 순서로 고정했다.
- **상태 점(point)**: `{state:{run:Run.s, memory:v2 store|null}, stateHash, jsonHash, undefinedPaths}`.
  - `state`는 log/ev/battle/RNG를 포함한 순서 보존 JSON이다.
  - `stateHash`는 기존 `canonicalJSON/stateHash`(log/ev 제외)이며 trace 해시와 같다.
  - `jsonHash`는 `JSON.stringify` 원문의 SHA-256이다. 키 순서·부재·Unicode 원문을 보존한다.
  - `undefinedPaths`는 값이 undefined인 own 멤버나 배열 항목의 경로다. JSON에서는 멤버가 빠지거나 null이 된다. R0의 전체 fieldShape는 중복이 커서 넣지 않았다.
- **단계(step)**: `{index, command, outcome, errorCode, error, result, before, after, queries}`.
  - `before`는 참조 `{ref, stateHash, jsonHash}`다. ref -1은 `created`, 그 밖에는 `steps[ref].after`를 가리킨다.
  - committed 단계는 전체 `after` 점을 가진다.
  - rejected 단계의 `after`는 `before`와 같은 참조다. 생성 중에 JSON 동일성과 undefinedPaths 동일성을 assert한다. 거절 원자성이 해시로 증명되므로 중복 상태를 저장하지 않았다.
- **반환값**: 호출 직후에 포착한다. `{kind:'undefined'}` 또는 `{kind:'json', value, jsonHash, undefinedPaths}`이며, 대부분의 Run 메서드는 undefined다.
  - battle 명령은 `{v,phase,ev}`, `forgeOffer`는 offers, legacy `commitMemory`는 memory, v2는 `{state,response}`를 반환한다.
  - legacy `commitMemory`의 반환값은 JS에서 `s.savedMemory`와 같은 객체(별칭)다. 계획된 C# 분리 사본과의 비교는 값으로만 한다.
- **오류**: rejected 단계는 `result:null`, `error:{kind:'RuleError', name, code, message, detail}`이다.
  - `detail`도 같은 kind 인코딩을 쓴다(예: AP_LOW `{need,have}`).
  - C#이 detail까지 맞출지, 기존 관례대로 code만 비교할지는 호스트가 정한다.
- **undefined 위치(정적 분석)**: own-undefined는 두 곳에서만 나온다.
  - legacy `savedMemory`의 add 사양(`assigned/mods/from`, `rules-core.js:2003`)
  - 직전 battle 명령이 만든 event의 `fizzled` 등(`afterResolve` 4번째 인자 생략). 다음 명령의 clone에서 사라진다.

  시험이 이 범위를 assert한다. C#은 해당 멤버를 null로 쓰지 말고 생략해야 같은 JSON이 된다.
- **질의(boundary)**: 질의 이름은 `available`, `nodeById`(pos, pos가 null이면 floor 0의 첫 노드), `battle.publicView`(전투 중일 때만)다.
  - 시점: Create 직후, 모든 committed Run 명령 뒤, 새 턴을 시작하거나 전투를 끝낸 battle 명령 뒤.
  - 질의마다 전체 JSON(RNG·log 포함)과 undefinedPaths가 바뀌지 않음을 assert한다.
  - publicView는 실제 소스 API의 반환값일 뿐 안전한 클라이언트 계약으로 인증하지 않았다. fixture 전체 상태는 draw 순서·RNG를 노출하는 비공개 시험 자료다.
- **Battle 영수증**: Run.s 밖의 런타임 상태라 기록하지 않는다. 대신 battle 응답이 result에 남는다.
- **reload**: gen_traces `saveMemory`의 재생성 확인(savedMemory, seed+99, v2는 `${runId}-reload`와 commit 뒤 store version)을 `Run.create`만으로 기록한다(`case.reload`). 다음 Run 연속을 C#에서 맞춰 볼 수 있다.
- **coverage**: 저장된 step만으로 계산하며, check는 이를 다시 계산해 비교한다.
  - `totals{cases,attempted,committed,rejected,queries,csharpCommandReplay:0}`
  - `perCase`: 시도·확정·거절, battle 명령 수, 최종 phase/결과/floor, 방문 구역, 전투 수, 최장 전투 턴, store version
  - `methods`, `rejections`, `phases`(시도 직전 Run phase), `queries`, `terminalOutcomes`
  - `events{beginEndedBattles, apBonusEarned, apBonusConsumed}`
  - 분모 `denominators`는 고정 plan에서 계산한다.
- **소스 고정**: `data/game_data.json`, `engine/rules-core.js`, `proposals/memory_v2.cjs`, `tools/gen_traces.mjs`(trace 정책·canonical hash), `engine/tests/fuzz_runs.mjs`(정책 원본), `tools/gen_run_create_vectors.mjs`(재사용한 strict decoder), 이 생성기. 시각 정보는 없다.
  - Root 대조 기준(작업 지시 기준, 미계산): engine `dae99192bfa137762c1c863efda8207b3851e3a6e7ecf2ff029bed3585a33ab4`, data `6a09e6dd2d81055738c84951ea5b5ae50c25fd8c9732c8443e81ec1498625688`.
- **gzip/strict**: R0의 `decodeVectors`를 그대로 재사용한다.
  - 단일 member, 고정 header(OS 바이트만 가변), CRC/ISIZE, BOM 금지, fatal UTF-8, 중복 키 거절.
  - export는 strict reader 왕복 확인 → 같은 디렉터리의 임시 파일 → 소스 재확인 → rename 순서다.
  - `--check`는 저장 데이터를 구조 검증하고 coverage를 재계산한 뒤, 전체를 새로 생성해 `isDeepStrictEqual`과 JSON 문자열 동등성으로 비교한다. 생략하는 필드는 없다.

## 3. 원본 사건 판정(코드에 넣은 assert, 미실행)

- **새로 얻은 AP 보너스**: 다음 조건을 확인한다.
  - nextApBonus가 늘어나는 곳은 ev_rift 첫 선택(+2)뿐이다.
  - 그 밖의 변화는 전투 시작 enter의 0 초기화뿐이다. 환급이 있으면 실패한다.
  - 모든 전투 시작에서 Run 쪽과 battle 사본은 0이다.
  - 첫 턴 AP는 `clamp(apPerTurn + 이전 보너스 + chrono_crown − cracked_hourglass, 0, apMax)`다.
  - **seed 2654 같은 Run 안의 재획득(수정됨)**: 요구는 같은 seed2654/full Run 안에서 보너스를 두 번 따로 얻는 것이다. 최초 작성의 "다른 seed에서도 얻어야 한다"(`earnedSeeds.size >= 2`)는 잘못된 해석이었으므로 삭제했다. 나머지 네 구성은 ev_rift를 얻을 필요가 없다. 대신 같은 사례에 대해 다음을 정확히 assert한다.
    - 원본 기준값: §5 기본 AP 4와 §7.7 ev_rift 보너스 +2를 데이터에서 읽은 뒤 리터럴과 대조한다.
    - 획득은 정확히 2회이고, 매번 0에서 2가 된다.
    - 소비도 정확히 2회이며 매번 `[carried 2, 첫 턴 AP 6]`이다.
    - 순서는 획득1 < 소비1 < 획득2 < 소비2다.
    - 두 획득 사이의 실제 전투 시작은 소비1 하나와 그 뒤 일반 전투 8회 `[carried 0, AP 4]`뿐이다.
    - 획득2 직후의 첫 전투 시작이 소비2다.
    - 관측된 전투 시작에는 AP 유물이 없다.

    전체 corpus에 걸친 기존 검사(AP 보너스가 바뀌는 위치, 첫 턴 공식, 환급 금지)와 `coverage.events` 일치 검사는 그대로 유지한다.
- **begin 중 패배(1102 legacy·v2 각각)**: 다음 조건을 확인한다.
  - 전투 노드 enter가 한 번 MAP→DEFEAT로 바뀐다.
  - 그 결과 battle null, hp 0, `result{defeat, floor=node.f}`, `pending{save, defeat:true}`이다.
  - log 끝은 `["<노드명> 진입","패배"]`이고 stats는 바뀌지 않는다.
  - v2에서만 memoryAfter가 있다. enter의 반환값은 undefined이고 `available()`은 `[]`다.
  - 그 뒤에는 commitMemory 하나만 committed되며, 선택은 1개 이하다.
- 이 assert들은 현재 소스가 이미 고친 동작(AP 환급, 상점 무료 재제거, done 이벤트 재선택, begin 종료 finalizer)을 결함으로 보존하지 않는다. 이전 경계 문서 Q1의 "환급·누적 보존" 권고는 적용하지 않았다.

## 4. 시험 구성(`node --test`, 5개)

1. **고정 사례·실제 재생**: 다섯 id/seed/길이/정책과 분모, 7개 소스 경로와 현재 파일 SHA-256, 정책 seed 기본식을 확인한다.
   - 새 `generateScenario`와 명령·outcome·code·hash가 같아야 한다.
   - 시험 자체의 독립 재생(Create와 공개 명령만)으로 모든 단계의 전체 JSON·반환값·질의 결과·질의 순수성을 비교한다.
   - DONE, 두 정책의 store와 reload를 확인하고, undefined 위치도 확인한다.
2. **원본 사건**: 위 §3을 단계 데이터에서 독립적으로 판정한 뒤 `coverage.events`와 일치하는지 확인한다.
3. **변조 거절**: 대상은 소스 SHA, status, 추가 필드, 사례 누락·순서, step 누락, 위조 outcome, 일관 위조 errorCode(coverage 재계산으로 거절), stateHash, state, result, final hash, totals다.
   - 해시를 맞춘 query 위조는 새 실제 실행 비교만으로 거절되는지 확인한다.
4. **export**: 다음을 확인한다.
   - 이 프로세스의 생성물과 바이트가 같다.
   - 다른 DEFLATE 인코딩(memLevel 1, windowBits 10)과 OS 바이트를 수용한다.
   - 잘림·CRC·연결 member·BOM·중복 키를 거절한다.
   - 다음 경우 모두 기존 출력과 디렉터리를 보존한다: 예기치 않은 Error, 해당 명령에 허용되지 않은 RuleError, oracle 질의 실패, log만 바꾸는 불순 질의(canonical hash로는 보이지 않음).
5. **저장된 artifact**: `--check`와 같은 strict 비교를 한다. Root가 생성하기 전에는 파일이 없어 실패한다.

## 5. 실행 방법 (Root)

```sh
node tools/gen_run_progression_vectors.mjs --help
node tools/gen_run_progression_vectors.mjs          # fixture 원자적 생성, totals/bytes/sha256/sources 출력
node tools/gen_run_progression_vectors.mjs --check
node --test engine/tests/run_progression_vectors.test.mjs
node --test engine/tests/trace_export.test.mjs      # 기존, 변경 없음
```

## 6. 미검증 가정·위험

- **측정 사실과 정적 가정의 구분**
  - Root 측정으로 확인된 것: 생성과 `--check` PASS, 다섯 사례 모두 DONE 도달, 2654의 두 차례 획득·소비와 그 사이 AP4 전투 8회, 크기·시간·RSS(§0).
  - 작성자의 정적 가정(이번 수정 뒤 미실행):
    - 1102 두 정책에서 begin 패배가 정확히 1회 일어난다.
    - undefined는 `run.savedMemory`와 `run.battle` 아래에서만 나온다.
    - 수정한 2654 assert가 위 실측과 일치한다.

    Root가 보고한 최초 시험 결과는 4/5 PASS였고 실패는 seed 수 assert 하나뿐이었다. 이 assert들은 그 실행에서 다른 실패를 내지 않았다. 다만 수정본은 아직 다시 실행되지 않았다. 틀리면 기대값을 바꿔 통과시키지 말고 보고할 것.
- **크기와 메모리**: Root 측정치는 gzip 약 2.0 MB, 비압축 약 29.9 MB, 생성 1회 4.84초, RSS 약 720 MiB다. 시험은 전체 생성을 최대 3개까지 동시에 메모리에 둔다. 힙이 부족하면 `node --max-old-space-size=6144 --test …`로 재실행할 것.
- **import 부작용**: `decodeVectors`를 쓰려고 `gen_run_create_vectors.mjs`를 import하므로, 그 모듈의 manifest 감사도 로드 시 실행된다. 이 파일은 Root에서 `8c6d764`부터 이미 추적되고 있다. 최초 보고의 "미추적이라 함께 커밋해야 한다"는 작성자 worktree에만 해당하던 서술이었으므로 철회한다. Root의 실제 capture·import·`--check`는 성공했다.
- **R0 decoder 특성**: R0 decoder는 `.gz` 경로의 gzip이 아닌 원시 JSON도 받는다. 기존 reader를 약화하지 않으려고 그대로 두었다.
- **소스 결함**: 정적 검토에서 새 소스 결함은 찾지 못했다. `fizzled: undefined`는 결함이 아니라 JSON 형태에 관한 사실로 기록했다.

## 7. 다음 작업

1. Root가 생성 → `--check` → 시험을 실행하고, 소스 핀과 크기를 확인한 뒤 통합한다.
2. Codex 독립 리뷰를 받는다.
3. C# R1a에서 이 fixture를 대상으로 `Enter/BattleCommand/AfterBattle/보상/Proceed/Archive` 재생 하네스를 만든다. 그때도 전체 C# 재생은 별도로 집계한다.
