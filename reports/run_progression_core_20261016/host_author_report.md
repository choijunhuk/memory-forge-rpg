# P1-R1A-CSHARP-HOST-CHECKS — 독립 C# R1a 진행 호스트 검사 (작성자 보고)

- 작성: 공식 Claude Code Opus 5.5, 구독 포함 사용량. 1차 호출 xhigh(P1-R1A-CSHARP-HOST-CHECKS, 30분 TIMED_OUT·부분 파일 보존), 2차 호출 High(P1-R1A-HOST-FINISH-CONSUMER, 같은 세션 재개). 실제 세션·모델·effort 증거는 Root의 CLI 메타데이터를 따른다.
- **작성자 실행: 전부 NOT_RUN.** 이 세션에는 명령 실행 도구가 없다. 컴파일·실행·RED/GREEN·Unity는 모두 Root가 한다. 아래 수치는 Root가 준 독립 리터럴이며 작성자가 측정한 값이 아니다.
- 편집 파일: `RunProgressionChecks.cs`(신규 소비자), `RunCreateChecks.cs`·`TraceHashChecks.cs`·`Program.cs`(1차 호출, 이후 읽기 전용), 이 보고서. Core·JS·fixture는 편집하지 않았다.

## 1. 공유 strict reader 재사용 (1차 호출, 변경 없음)

- `RunCreateChecks.Gunzip(byte[], ReadOnlySpan<byte> header)`: 기존 `Decode`의 바이트 디코딩만 분리했다. 단일 member, 단일 DEFLATE가 footer에서 정확히 끝남, native CRC + 명시 ISIZE, BOM 금지, fatal UTF-8. `Decode`는 같은 바이트를 같은 옵션(MaxDepth 256)으로 파싱한다. 고정 헤더 CM8/FLG0/MTIME0/XFL2(OS만 가변)는 그대로다. 기존 115 probe는 손대지 않았다. 바뀐 것은 거절 메시지에서 "run-create" 접두어를 뺀 것뿐이다.
- `UniqueKeys`, `SetupPrint`, `DataPrint`는 `internal`로만 바꿨다. int/소수/undefined 규칙은 그대로다.
- `TraceHashChecks.ReadGzip`: 기존 text-reader 경로를 `Gunzip(compressed, 1f8b0800)`로 바꿨다. 이 corpus의 헤더는 ID/CM8/FLG0만 고정하고 MTIME/XFL/OS는 고정하지 않는다(Node createGzip 기본 수준). Peek 미독 검사와 CrcReadStream footer 대조는 유지했다.
- `ReadSnapshots`에 선택 콜백 `Action<Dictionary<string,object?>>? scenario`를 추가했다. 행의 모든 기존 검사가 통과한 뒤에만 호출된다. 기존 호출부는 바뀌지 않았다.
- **분리 가능한 시험 전용 변경**: `CheckGzipRejections` 안에만 추가했다. 빈 member 리터럴의 native 유효성 control, native GZipStream이 gzip(empty)+gzip(JSONL)을 JSONL로 디코딩한다는 control, 복원 후 단일 member positive, 연결 probe 4개(빈 prefix 1개/2개, 빈 suffix, JSONL 2개; 사유 "gzip"), 후행 바이트 probe 3개(사유 미고정), 미독 JSONL probe 1개. 옛 reader에서 RED가 예상되는 것은 빈 prefix 두 probe뿐이다(Root가 실제 assembly로 hole을 독립 확인함).
- `Program`은 기존 suite 뒤에 `RunProgressionChecks.Run(root)`를 한 번 호출한다. TraceHashChecks의 `NOT_IMPLEMENTED` 문구는 "완전 C# Run 재생 0, prefix 수는 별도 보고"로만 명확히 했다.

## 2. 다섯 사례 full-state oracle 소비자 (`RunProgressionChecks.Oracle`)

- **구조 검증(Core 실행과 분리)**: strict `Decode` + `UniqueKeys`. 최상위 9개 필드는 순서까지 고정이다. schema/`JS_ORACLE_ONLY`/scope는 원문 리터럴로, contract 8개 문장과 denominators도 리터럴로 고정했다. sources는 7개 키를 순서대로 고정하고 모든 path를 allowlist와 먼저 대조한 뒤 **allowlist 상수 경로만** 해시한다. 다섯 사례는 id/seed/length/policy/role을 고정했다.
- 모든 2684 기록을 검사하며, Core가 멈춘 뒤의 suffix도 건너뛰지 않는다.
  - 점: `{run,memory}` 형태, canonical stateHash, 원문 JSON SHA, undefinedPaths(객체 멤버는 부재, 배열 항목은 null).
  - before 참조: ref·해시가 현재 점과 같아야 한다.
  - committed: 반환 kind 규칙(battle/forgeOffer/commitMemory만 JSON), 결과 해시.
  - rejected: RuleError envelope, detail 인코딩, after == before 원문.
  - 질의 경계: Create 직후, 모든 committed Run 명령 뒤, 턴을 바꾸거나 전투를 끝낸 battle 명령 뒤에 available, nodeById(pos 또는 floor0 첫 노드), 전투 중일 때만 publicView가 있어야 한다.
  - battle envelope: rid=`r{v}-{kind}`이고 v는 기록된 live battle v와 같아야 한다.
  - setup은 gen_traces 기본값과 같아야 하며 v2는 runId/version0/useMemory를 확인한다. reload setup은 savedMemory, seed+99, v2 runId/version을 확인한다.
  - final hash/DONE과 trace summary를 확인한다.
  - coverage 전체(totals/perCase/methods/rejections/phases/queries/terminalOutcomes/events)를 기록에서 재계산해 비교한다. totals 리터럴은 5/2684/1370/1314/1074/0이다.
  - role 증거: 1102는 begin-defeat를, 2654는 AP 보너스 획득 2회·소비 2회를 기록해야 한다. 다섯 trace의 policy는 seed 외에 같아야 한다.
- **prefix 분모(구조)**: 각 사례에서 첫 미구현 Run 명령까지의 시도 수, 그중 battle 수, 멈춘 명령을 리터럴 96/85 eventChoose, 96/85 eventChoose, 42/31 forgeRemove, 42/31 forgeRemove, 78/74 eventChoose와 대조한다. 합계 354/306과 prefix 질의 223(Create 포함)을 확인한다.
- **실제 C# 실행**:
  - 사례마다 새 `RunDataLoader.Setup` → `Run.Create`로 시작한다. v2 store는 `MemoryV2.Migrate(data.Memory,{},{})`의 실제 결과이며, setup.memory/memoryVersion이 그 이관과 같은지도 확인한다.
  - 명령은 지원 9종 Run 메서드(`Enter/Evolve/TakeCard/SkipCard/TakeRelic/TakePart/ApplyFoundPart/Proceed/Archive`)와 `BattleCommand(kind, BattleDataLoader.Request(scope 없는 rid/v/kind/args).Args)`다. 호출 전에 live C# battle v와 rid가 기록과 같은지 확인한다. 인자는 RuleException 창 밖에서 먼저 bind하므로 host 디코딩 오류가 Core 거절로 위장될 수 없다.
  - 시도마다 outcome과 RuleError code만 비교한다(JS name/message/detail parity는 주장하지 않는다). 해결된 참조 점과 전체 `{run: State.ToJsonGraph(), memory}`를 `MemoryV2DataLoader.Difference`로 경로별 비교한다(log/ev/battle/RNG/null/부재 포함). 여기에 canonical context hash와 undefined 부재를 더한다. C# 원문 JSON 순서 동일성은 주장하지 않는다.
  - committed 반환: void는 undefined, battle은 `BattleResponse.ToJsonGraph()` 전체를 비교한다.
  - 기록된 질의는 C# `Available/NodeById/BattlePublicView`로 실행해 비교한다. 반환값을 caller가 훼손해도 상태가 그대로인지(순수성·분리) 확인한다.
  - 첫 불일치에서 그 사례를 멈춘다. 오류를 건너뛰고 계속 진행하지 않는다. 비-RuleError 예외는 정확한 형식(MemoryBoundaryException의 JS 이름 포함)으로 FAIL 처리한다.
- **분리 probe(positive control 포함)**: 첫 활성 전투 시점과 첫 pending dictionary 시점에 실행한다. 반환된 State의 hp/rng/log/deck/party·map·runFlags/pending/battle(hp·rng·log·hand·cards·enemies·actors·flags)을 각각 훼손한다. 매번 사본이 실제로 바뀌었는지 확인하고, live Run이 바뀌지 않았는지 확인한다. BattlePublicView와 BattleResponse 훼손도 같은 방식으로 확인한다. setup과 정의(`SetupPrint/DataPrint`) 보존도 확인한다.
- **실행 리터럴**: 일치 354/354, battle 시도 306, battle commit 210, 질의 223.
- **변조 probe**: 헤더 13개를 둔다. 그중 존재하지 않는 경로로 바꾼 source probe는 읽기 전에 거절해야 하며, 파일을 읽으려 하면 다른 예외가 나서 드러난다. 사례 probe는 가장 짧은 사례를 JSON.stringify와 같은 순서 writer로 왕복한 control 위에서 7개를 실행한다.
  - 해시 재계산 없는 상태 수정
  - 거절 단계 재지정
  - 단계 누락
  - 질의 누락
  - 존재하는 멤버를 undefined로 주장
  - battle 반환을 undefined로 위장
  - battle v 변경

  **자기 일관 위조** 1개도 둔다. prefix 안 committed 점의 coins를 바꾸고 해시와 참조를 모두 재계산한다. 구조 검증은 이를 통과해야 하고(control), 실제 C# 실행만 거절해야 한다(probe).
- reload Create는 실행하지 않는다. 입력 memory가 JS commitMemory 산출물이기 때문이다. JS-only suffix(2330 시도 예상)도 실행하지 않는다.

## 3. legacy 1000 prefix 소비자 (`Legacy`)

- `TraceHashChecks.ReadGzip`(strict)와 `ReadSnapshots` 콜백을 재사용한다. manifest의 현재 5개 source pin, 1000 = 700 short/300 full, snapshot 해시는 기존 reader가 검증한다. 콜백은 seed 1000+순번과 legacy policy를 추가로 고정한다.
- 각 trace에서 첫 미구현 Run 명령 전까지의 분모를 kind별로 센다. 새 Create와 초기 hash를 확인한 뒤 같은 RunCall/BattleCall로 실행하며, 매 시도마다 outcome/code/canonical context hash를 비교한다. 불일치가 나면 그 trace를 멈추고 FAIL로 기록한다. 상세 출력은 처음 20개까지만 한다.
- 리터럴 112822 시도를 확인한다(enter 4758, play 88760, end 9873, takeCard 2826, proceed 3759, takePart 842, skipCard 930, applyFoundPart 601, ult 102, takeRelic 237, evolve 133, archive 1). 소스 분모와 실제 일치 수를 둘 다 비교하고, 1000개 prefix가 모두 일치하는지, 모든 trace가 미구현 명령에서 멈추는지(full completion 0) 확인한다. 정의 보존도 확인한다.
- **교차 확인**: fixture의 `seed1000-short-legacy`는 corpus 시나리오 1000과 setup, policy, 초기/최종 hash, 모든 envelope/outcome/code/hash가 같아야 한다.
- 진단 카운터로 전투 시작/종료, archive, stoppedAt{kind}를 출력한다. 이 값들은 고정하지 않는다.

## 4. 출력

`RunProgressionChecks n/n`, `RunProgressionFixture`(구조, Core 실행 아님), `RunProgressionCore`(일치/시도/commit/reject/battle 시도·commit/질의/상태 비교/분리 probe), `RunProgressionStops`(사례별 실행 수와 멈춘 지점), `RunProgressionScope`(JS-only suffix 미실행, reload 미실행, full C# runs 0), `RunProgressionGuards`, `RunTracePrefixReplay`(legacy).

## 5. 가정·위험 (NOT_RUN이므로 Root 확인 필요)

- 컴파일은 하지 않았다(TreatWarningsAsErrors + Nullable). 패턴(`is not`, 튜플, 로컬 함수)과 nullable 흐름은 정적으로 맞췄지만 경고가 날 수 있다.
- fixture 키 순서(step/created/trace/command/setup/party)는 생성기 소스에서 정적으로 도출했다. 다르면 구조 Require가 실패한다. 이때 기준을 낮추지 말고 실제 순서를 보고할 것.
- `Ordered` writer의 왕복 control은 `CanonicalState` 문자열 escape가 JSON.stringify와 같다는 전제에 의존한다.
- legacy 재생은 시도마다 State 복사 + canonical hash(약 11만 회)를 하므로 수십 초가 걸릴 수 있다. traces는 두 번 읽는다(기존 snapshot 검사 + 이 소비자).
- 이 결과는 R1a prefix 증거일 뿐이다. 완전 C# Run 재생, 서버, Unity, 실기기 수용은 0/PENDING이다.
