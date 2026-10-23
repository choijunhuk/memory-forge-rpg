# P1-R1B-HOST-EXTENSION — R1b 노드 서비스까지 확장한 실제 Run 호스트 (작성자 보고)

- 작성: 공식 Claude Code Opus 5.5, 요청 effort xhigh, 구독 포함 사용량. 실제 세션·모델·effort 증거는 Root의 CLI 메타데이터를 따른다.
- **작성자 실행: 전부 NOT_RUN.** 이 세션에는 명령 실행 도구가 없다. 컴파일·실행·RED/GREEN은 모두 Root가 한다. 아래 수치는 Root가 준 독립 리터럴이며 작성자가 측정한 값이 아니다.
- A의 Core 서비스 파일은 아직 없다. 아래 합의 시그니처를 기준으로 작성했으므로, A 파일이 들어오기 전까지 이 호스트는 **컴파일되지 않는 것이 정상**이다.
- 편집 파일: `core/MemoryForge.CoreChecks/RunProgressionChecks.cs`, 이 보고서. Core·JS·data·fixture·golden과 다른 CoreChecks 파일은 편집하지 않았다.
- 범위: **R1b 노드 서비스 prefix**까지다. 전체 Run이 아니다. commitMemory, reload Create, 완전 C# Run(0), 서버, Unity, 실기기는 범위 밖이다.

## 1. 어댑터 (`RunCall`)

R1a의 9개 명령은 그대로 두고, gen_traces가 기록한 인자 형태만 바인딩한다. 형태가 다르면 RuleException 창 밖에서 `InvalidDataException`을 던진다. 따라서 호스트 디코딩 오류가 Core 거절로 위장될 수 없다.

| 명령 | 기록 형태 | C# 호출 | 반환 |
|---|---|---|---|
| rest | `[choice]` / `[choice, key]` | `Rest(choice, key 또는 null)` | undefined |
| forgeOffer | `[key]` | `ForgeOffer(key)` | 분리된 `IDictionary<string,object?>` |
| forgeApply | `[partId]` | `ForgeApply(partId)` | undefined |
| forgeRemove | `[key]` | `ForgeRemove(key)` | undefined |
| buy | `[shelf, id]` / `[kind, null, key]` | `Buy(shelf, id)` / `Buy(kind, null, key)` | undefined |
| eventChoose | `[index]` / `[index, key]` | `EventChoose((int)index, key 또는 null)` | undefined |

- `Pending`에는 `commitMemory`만 남았다.
- `Assign`은 gen_traces 어휘에 없다. 호출하지도 집계하지도 않으며, 출력에는 "Assign 0 attempts"로 표시한다.
- RuleError는 code만 비교한다. name·message·detail은 주장하지 않는다.

## 2. 반환·분리 검사

- **forgeOffer 반환**:
  - 기록된 JSON `{key, parts}` 전체를 `Difference`로 비교하고, undefinedPaths 부재도 확인한다.
  - 반환값을 Scribble로 훼손했을 때 사본이 실제로 바뀌는지 positive control로 확인한다.
  - 같은 훼손이 live pending이나 Run에 닿지 않는지 확인한다.
  - void 메서드가 JSON을 반환하거나 그 반대인 경우는 기존 검사가 그대로 FAIL로 잡는다.
- **pending 분리 probe**:
  - 기존에는 사례마다 첫 pending 한 번만 검사했다. 이제 사례마다 (phase, 직전 명령) 쌍이 처음 나올 때마다 검사한다.
  - 예: `FORGE after forgeOffer`(offers 있음), `SHOP after buy`(sold), `REST after rest`, `FORGE after eventChoose`(fromEvent).
  - 실제로 검사한 phase 목록은 출력에 표시한다. 활성 전투 probe와 BattleResponse·BattlePublicView·query 분리 검사는 그대로다.

## 3. 독립 리터럴 (Root 재계산, oracle·JS 불변)

- 사례별 Plan에 6개 값을 고정했다. 순서는 prefix, battle 시도, battle commit, 질의(Create 포함), committed, rejected이며 모두 commitMemory 직전까지다.

  | 사례 | prefix | battle 시도 | battle commit | 질의 | committed | rejected |
  |---|---|---|---|---|---|---|
  | seed1000-short-legacy | 147 | 119 | 87 | 109 | 115 | 32 |
  | seed1000-full-legacy | 245 | 209 | 136 | 152 | 172 | 73 |
  | seed1102-full-legacy | 221 | 178 | 130 | 169 | 173 | 48 |
  | seed1102-full-v2 | 221 | 178 | 130 | 169 | 173 | 48 |
  | seed2654-full-legacy | 1845 | 1790 | 679 | 465 | 732 | 1113 |

- 합계: 2679 / 2474 / 1162 / 1064, committed 1365 / rejected 1314. 구조 합계와 실제 C# 일치 수를 각각 이 값과 대조한다.
- 사례마다 suffix는 정확히 마지막 commitMemory 1개이며, 5개 모두 실행하지 않고 구조만 검사한다.
- fixture 자체 리터럴(Scope, Contract, Totals 2684/1370/1314/1074/0)은 바꾸지 않았다.
- legacy 1000:
  - 분모 272504와 메서드별 18개 리터럴을 소스 prefix 분모와 실제 C# 일치 수 양쪽에 대조한다.
  - 1000개 모두 commitMemory에서 멈춰야 한다. 이전의 "Pending 중 하나에서 멈춤"보다 강한 조건이다.
  - 각 trace의 source input과 시도별 canonical full-state hash 검사, 첫 미지원 명령에서만 멈추는 규칙은 그대로다.
  - 전투 시작·종료, archive, committed·rejected 수는 실제 실행에서 센 진단값이며 고정하지 않는다.

## 4. 변조·위조 probe

- 기존 21개 negative probe(header 13, case 7, 자기 일관 위조 1)와 control은 모두 유지했다.
- 자기 일관 위조를 local function `Forgery(at, label)`로 일반화했다.
  - `Execute`가 첫 분기 전까지 일치한 시도 수를 반환하도록 바꿨다.
  - 위조 probe는 거절 여부뿐 아니라 **C#이 앞선 모든 시도를 맞추고 정확히 위조된 단계에서 멈췄는지**(`reached == at`)도 확인한다. 기존 위조에도 이 조건이 붙는다.
- **신규 위조 1개**: 가장 짧은 사례에서 prefix 안의 첫 committed 노드 서비스(rest/forge*/buy/eventChoose) 직후 상태의 coins를 +1 한다. 해시와 참조는 모두 재계산한다.
  - 구조 검증은 이를 통과해야 한다(control).
  - 실제 C# replay는 해당 서비스를 실행한 직후에 거절해야 한다(probe).
  - 이 사례는 예전 R1a에서 eventChoose #96에 멈췄으므로 committed eventChoose가 존재한다.
- probe는 총 22개다.

## 5. 출력 라벨

- `RunProgressionCore`: "R1b actual C# matched …". 내용은 committed·rejected·battle 수와 리터럴 분모, 명령별 실제 실행 수, pending probe phase다.
- `RunProgressionScope`: "R1b node-service prefixes, not whole Runs". 내용은 suffix 5 commitMemory 미실행, reload 미실행, Assign 0, RuleError code만 비교, full C# runs 0이다.
- `RunTracePrefixReplay`: 실제 committed·rejected 수를 추가했다.

## 6. 가정·위험 (NOT_RUN — Root 확인 필요)

- **시그니처 가정**:
  - `ForgeOffer`는 null이 아닌 `Dictionary<string, object?>`를 반환해야 한다. nullable 반환형이면 `Func<object>` 변환에서 CS8603 경고가 나고 TreatWarningsAsErrors로 실패한다.
  - `EventChoose`의 두 번째 인자는 `string?`로 전달한다. `object?` 시그니처와 `string?` 오버로드 모두에 바인딩된다.
  - `Rest("heal")`은 명시적으로 `null` key를 넘긴다. 이는 JS의 `undefined`와 같다.
- 작성자는 컴파일하지 않았다. switch section 스코프와 패턴 변수는 정적으로만 맞췄다.
- 사례별 리터럴이 실제와 다르면 기준을 낮추지 말고 실제 값을 보고할 것.
- **실행 비용**: oracle prefix가 354에서 2679로 약 7.6배, legacy 시도가 112822에서 272504로 약 2.4배 늘었다. 시도마다 전체 상태 직렬화와 hash가 있으므로 실행 시간이 비례해서 늘어날 수 있다.
- **확인하지 못한 커버리지**: "다섯 사례 prefix에서 6개 서비스가 모두 실행된다"는 검사는 작성자가 사례별 분포를 확인하지 못해 넣지 않았다. 서비스별 비공허성은 legacy 메서드별 리터럴(rest 598, forgeOffer/Apply 567, forgeRemove 357, buy 1413, eventChoose 540)이 보장한다.
