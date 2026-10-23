# P1-CARD-FLOW-REFERENCE-INDEPENDENT-REVIEW — Claude 독립 판단

- 작성: Claude Opus 5.5(`claude-opus-5-5`), 요청 노력 max, 2026-10-16. 실제 적용된 노력과 session_id는 이 세션에서 조회할 수 없어 Codex 회수 메타데이터를 따른다.
- 범위: 읽기 전용이며 이 파일만 작성했다. 근거는 이 C worktree에 제공된 파일의 정적 판독이다. 셸이 없어 HEAD `7260749` 일치는 직접 확인하지 못했다. **Unity·C#·Node·TS 탐침 실행과 외부 조사는 모두 NOT_RUN.**
- 약어: EC=`client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs`, B=`core/MemoryForge.Core/Battle.cs`, PA=`core/MemoryForge.Core/Battle.PlayerActions.cs`, RP=`core/MemoryForge.Core/Run.Progression.cs`, 설계=`docs/design_v0.8.1.md`. 아래 결함 주장은 모두 클라이언트 동작이나 문서화된 Core 성질이며, Core 버그를 주장하지 않는다.

## 1. 자료 분류와 주의

| 자료 | 성격 | 이번 판단에서의 쓰임 |
|---|---|---|
| 01 §2 F-xx | 다른 게임(카제나)의 공지·서드파티 요약. 나는 외부 확인을 하지 않았고 ADOPTION_REVIEW도 3건 발췌만 대조 | "정해야 할 항목" 체크리스트. MF의 사실·수치 근거로 쓰지 않음 |
| 01 §3 H-01~17 | 미확인 추정 | 근거로 쓰지 않음(H-03/05/06/17 포함) |
| 02 + observation-log.csv | 관찰 계획. CSV는 헤더와 예시 1행(2026-10-17 자리표시)뿐이라 관찰 0건. T-06 판정은 단정적 | 시험 설계 아이디어만 |
| 03 + prototype TS | 단일 hero·퓨즈 게임의 독자 설계. K=2·카드 비행 250ms·타격 180ms·maxOps 500·AP3는 설계/데모 값 | 게이트 어휘(배리어·백로그·확정 사망·선택 가시성)와 실패 유형만 |
| review/*(27 PASS·demo·typecheck·탐침) | 제공자의 과거 Node 22.16 실행 기록 | MF·Unity·기기·네트워크 증거 아님. 재실행 NOT_RUN |

- 가져오지 않는 것: 퓨즈·경직·피니셔·AP3, R-08 타격별 불발/무작위 재지정(MF 기존 규칙은 예약 카드 대상 사망 시 첫 생존 적, 고정 대상은 fizzle: PA:182-184), R-10 리셔플, R-15 부분 확정 중단, R-18/I-07 되돌리기, I-08 자동 전투, R-06/07 범용 반응 큐.
- 채택 후보 대부분은 **이미 승인된 MF 명세**에 있다. 설계 §14.3 "적 행동 중 입력 잠금·피해 팝업", "예약 해결 0.3초/장·건너뛰기", "기록 40줄"; §14.2 동작 줄이기·2.6초 토스트; §14.7 선택≠확정·사본 미리보기·동일 requestId; §16.3 publicView/숨은 정보; §16.4 View+Presenter·서버 `ev` 순서 재생; §16.5 `{requestId, expectedVersion, kind, args}`. 참고 자료의 실익은 새 규칙이 아니라 **시험 어휘와 실패 패턴**이다.
- 묶음 탐침 대응: CF-01(턴 종료 반응 순서)은 MF에 범용 TurnEnding 훅이 없고 종료 순서가 JS와 대조된 `Battle.EndTurn`(B:455-514)이라 해당 없음. CF-03(행동 없이 턴 넘김)은 MF 적이 매 턴 종료에 행동하므로 해당 없음. CF-02는 §3 S2로 다룬다.

## 2. 여섯 우선순위

| 우선순위 | 판단 | 현재 근거 | 실제 남은 공백 |
|---|---|---|---|
| 1. 연출 중 제한적 준비(K) | **Defer**(백로그). 원칙은 연출 도입 시 Modify-then-adopt | 연출 계층이 없다. `SendBattle`(EC:205-216)이 동기 `Run.BattleCommand` 뒤 즉시 전체 `Render`(EC:432-474). 타이머는 토스트뿐(EC:97-108). 선택은 Run에 닿지 않음(`SelectCard/SelectMode/SelectEnemy` EC:167-169, 시험 `BattleSelectionSpendsNothing…`) | 기다릴 연출이 없어 지금 K는 의미가 없다. §14.3상 적 행동·예약 해결은 잠금 구간이라 백로그 대상이 아니다. 플레이어 카드 연출을 넣을 때: 비배리어 구간에는 선택만 허용, 로컬 확정은 backlog<K(시험 시작값 2, 미승인), 서버는 미확인 명령 1개 이하. 대기열은 Core `Reservations`·`TimelineCells`·"예약" 문구와 분리 |
| 2. 선택·적 행동·예약 해결·턴 경계 배리어와 복구 | **Modify-then-adopt → S1·S2** | 선택은 명령 인자로 원자 검증(`Precheck/ValidateOps` PA:53-114, `TakePick` PA:260-275)되므로 해결 도중 선택 대기 상태가 없고 R-14/I-05는 해당 없음. 시트는 전면 차단(`OverlayPanel` EC:1163-1170, 시험 "overlay must intercept"). 적 행동·예약 해결·다음 턴이 한 원자 명령(`EndTurn`→`StartTurn` B:455-514/397-433). 토스트 만료는 눌린 버튼을 재생성하지 않음(EC:102-108, 시험 `NoticeExpiry…`) | (a) §14.3 잠금·표시 미구현. `SendBattle`이 `BattleResponse`를 버리고 토스트만 띄운다. 턴 종료 버튼(EC:621)이 새 턴에 같은 자리에서 즉시 활성이라 두 번 탭하면 두 턴이 끝난다. 그 `end`로 승리하면 같은 영역에 '지도로'(EC:707→`Run.Proceed` RP:223-235)가 겹쳐 두 번째 탭이 남은 보상을 버린다. 카드를 받아 cards가 비면(Run.Rewards.cs:87) 유물·부품 버튼이 카드 자리와 겹치게 올라온다(EC:675-705). (b) `Do`(EC:388-398)는 `RuleException`/`MemoryBoundaryException`만 처리하고 Run은 비트랜잭션이다(RP:3-4; `Enter`가 위치·층·RNG를 바꾼 뒤 `StartBattle`, RP:18-40/57-79). 그래서 계산 오류 뒤 부분 진행 상태로 정상 플레이가 이어지고, 원자적 `end`가 계속 실패하면 토스트만 남고 턴을 넘길 수 없다. 모두 소스 판독이며 재현은 NOT_RUN |
| 3. 죽은 대상·사용한 카드·중복 거절 | **Already**(Core와 화면). 서버 결속은 Defer | `Precheck`의 NOT_IN_HAND·BAD_TARGET·FIXED_MARK_REQUIRED·AP_LOW·SLOT_FULL이 변경 전에 판정된다(PA:53-74). `Battle.Command`는 후보 사본·불변식·STALE_VERSION·IDEMPOTENCY_CONFLICT(B:117-147). 화면은 죽은 대상·사라진 카드를 해제(EC:531-533), 적 버튼은 `Alive`(EC:557), 방식·확정은 `BattleCheck` 게이트(EC:598-608). 표시 지연이 없어 I-04의 "보이지만 죽은" 구간 자체가 없다 | `Run.BattleCommand`가 rid `r{v}-{kind}`를 실시간 버전으로 만든다(RP:44-52, JS 동치 설계; 연습 전투도 `ui-{V}-{kind}`, MemoryForgeBattleClient.cs:99). 그래서 UI는 STALE_VERSION/IDEMPOTENCY에 닿지 못하고 중복 `end`는 두 턴이 된다. 로컬은 S1 게이트, 서버는 렌더한 버전의 expectedVersion과 클라이언트 requestId(§16.5)로 막는다. `TakePick`은 무효 pick을 자동 선택으로 대체하므로(JS 동치) 비동기 경로에서는 pick도 버전에 결속해야 한다 |
| 4. 사용·예약·해결·복제·유물·반격 출처 | **Already**(Core). 표시는 Modify-then-adopt(S1) | `play`(Mode/Cost/Target)·`reserve`(Due/Kind) PA:140/147, `resolved`(Reserved, Fizzled) PA:214, `fizzle` PA:182, `echoCopy` PA:257, `exhaust` PA:242, `dmg/block/hpLoss`의 Source(card·ult·counter·relic·burn·weapon·part…) B:357/617/626/689. Played·잔향·소유자 전환은 사용 시(즉시·예약 공통 PA:130-136), 게이지·부품·성장은 해결 시(예약 포함 PA:187-205), time_stamp·부품 echoCopy는 예약 해결만(PA:188/197-201). 한도는 TurnFlags/BattleFlags/RunFlags/GaugeTurn. JS 전수 대조 기록 있음(CURRENT: 992사례·87,991검사, 턴 356사례) | (a) Unity 기록 `EventText`(EC:1109-1126)가 dmg·resolved·repay·fizzle·echoCopy·status를 "전투 효과 적용"으로 뭉갠다. 10줄만 보이고 전투가 끝나면 사라진다(EC:1014-1022). §14.3은 40줄이다. (b) 트리거→사건→한도 범위 표가 문서로 없다. 소스에서 도출하되 의미는 바꾸지 않는 검토 기준으로 만든다. (c) 적 `def/buff/reflect/blockAll/summon`과 atkdebuff 약화는 이벤트가 없다(B:523-582). 그래서 이벤트만으로는 완전한 연출 대본이 안 된다. 이벤트 추가는 JS+C# 동치 변경이므로 A 작업과 조율한 별도 제안으로 둔다 |
| 5. 배속·건너뛰기 불변 | **Already**(구조). 시험은 S1과 함께 Adopt | 연출이 없고 결과는 Render 전에 확정된다. RNG는 상태 안에 있다(`Below` B:309). 미리보기는 사본에서 계산하고 드로우를 숨긴다(`Battle.Forecast` Battle.Preview.cs:36, CURRENT 미리보기 절) | §14.2 동작 줄이기·§14.3 건너뛰기는 승인됐지만 미구현. S1에서 시간 요소가 생기는 즉시 일반/건너뛰기/동작 줄이기 사이의 Run 지문 동일과 타이머의 Run 무호출을 증명해야 한다 |
| 6. 앱 복귀·서버 공개 상태 재구성 | **Already**(로컬 전체 재렌더). 제품은 **Defer** | 모든 명령과 `OnEnable`이 `run.State`에서 화면 전체를 다시 만든다(EC:91-96/432-474, 시험 disable→enable 동일 지문·단일 뷰). 연습 기억은 세션 전용(EC:89) | 제품 재접속은 NOT IMPLEMENTED. 전송·서버·영수증 영속이 없고(`receipts`는 메모리, B:14) rid도 클라이언트 소유가 아니다. 화면이 비공개 `RunState`(뽑을 더미 순서·Rng 포함, 표시는 장수만)를 직접 읽는다. `PublicView()`(B:716-724)에는 §17 예시의 slots/status/echo가 없고 중도 Run 복원도 없다. 지금은 S1이 공개 투영만 쓰는 선례만 만든다 |

## 3. 구현 slice (최대 2개 — Root가 별도 writer로 구현하고 실제 Editor에서 검증)

### S1 — §14.3 턴 경계 배리어와 전환 직후 입력 안정화 (먼저)
- 담당·경계: Root가 정한 writer 1명. B의 예상 UI와 같은 EC를 고치므로 B 병합 뒤 순차로 진행한다. Core·데이터·씬·프리팹은 바꾸지 않는다. B 소유 예상 코드(`PlayForecast/ForecastLine/ForecastText/ForecastOutcome`, 행동 줄·PickSheet 예상), A의 적 행동, Core 이벤트 추가는 범위 밖이다. 연습 전투 씬(MemoryForgeBattleClient)의 같은 노출은 S1 검토 뒤 따로 정한다.
- 인터페이스: 신규 `Assets/Scripts/BattleTurnDigest.cs`(순수 함수). 입력은 `end` 직전 **공개 투영**(적 uid·이름·의도·HP·방어·생존, 파티 HP·방어, 예약 uid·id·due, 턴), `Run.BattleCommand`가 반환한 `BattleResponse.Events`, 직후 공개 투영이다. 출력은 불변 줄 목록. Run/Battle 호출과 `Zones.Draw` 순서·`Rng` 접근은 금지한다. 모르는 이벤트는 일반 줄로 바꿔 예외를 내지 않는다. EC는 `SendBattle`(직전 투영·응답 보관), Render의 오버레이·게이트, `EventText/LogSheet`(40줄, 전투 종료 뒤에도 마지막 전투 기록 유지)만 고친다.
- 동작: `end` 뒤(승리·패배 포함) 확정된 최종 상태 위에 결과 요약 오버레이를 띄운다. 적별 직전 의도(기존 `IntentText`)와 그 적의 `hit/stunSkip/kill`, Source별 화상·반격·유물 피해, `repay`, 예약 해결(`resolved`의 Reserved·fizzle)을 0.3초/장(승인값)으로 단계 표시하고 턴 시작이나 전투 결과로 끝낸다. 표시 중에는 입력 게이트를 닫는다. "건너뛰기"는 전부 표시, "계속"은 닫기다. 단계 진행은 라벨을 제자리에서 바꾸고 Render하지 않는다(EC:102-108의 교훈). 레이아웃이 바뀐 확정(phase·pending·목록 수 변화) 뒤 짧은 안정화 구간에는 새 화면 클릭을 무동작으로 둔다. 그 길이는 **시험 시작값(미승인)**이며 기기 UX 확인 대상이다. 임시 텍스트 연출로 표시하고 최종 연출로 세지 않는다. 로컬 연습 Run의 확정 결과이므로 '서버 확정'으로 표기하지 않는다.
- 예외·재진입·복구: Core 거절은 요약 없이 기존 토스트로 처리한다. 요약 생성이 실패하면 대체 1줄과 닫기 버튼을 두며, 출구 없는 차단은 금지하고 Run은 바꾸지 않는다. 요약 중 공개 명령이 호출되면 요약을 닫고 Core 명령을 정확히 1회 실행해 기존 "버튼=명령 1회" 계약을 지킨다. 포인터 입력은 오버레이가 막는다. 확정 명령은 picker를 닫으므로 picker가 요약 밑에 남지 않는다. Depart/StartPractice/Abandon/phase 변화/OnDisable은 요약과 안정화를 해제하고, OnEnable은 차단을 복원하지 않는다. 대기·표시를 "예약"이라 부르지 않고 시간표에는 Core 예약만 둔다.
- 인수(Unity PlayMode, 기존 mirror-Run과 실제 EventSystem raycast 방식): ① 턴 종료 지점에 실제 클릭 2회(두 번째는 요약·안정화 중) → mirror와 `end` 정확히 1회로 일치. 닫은 뒤 양성 대조 클릭은 다음 턴을 끝낸다. ② 승리하는 `end`의 두 번째 클릭(턴 종료 하단, 지도로와 겹치는 영역) → Proceed 없음, 보상 보존. 엘리트 보상에서 카드를 받은 지점 재클릭 → TakeRelic 없음(지문=mirror). ③ 같은 시드·명령열을 즉시 건너뛰기/타이머 진행/동작 줄이기로 실행 → 매 명령 뒤 Run 지문 동일, 요약 표시 프레임 동안 Run·RNG 불변. ④ `end`로 패배 → 응답 이벤트로 요약한 뒤 저장 화면, 닫기 전 클릭 무효. ⑤ 예약 2장 이상 해결과 fizzle·반격·화상이 있는 `end` → 모든 줄이 사건이나 직전 의도로 추적되고 시간표=Core 예약. ⑥ 닫기 버튼을 누른 채 단계 진행 → 같은 버튼·캔버스·EventSystem, 떼면 1회 닫힘. 옛 턴 종료를 누른 채 재생성 뒤 떼면 명령 없음. 요약 중 disable/enable과 생성 예외 대체. ⑦ 적 5·예약 다수·글자 130%에서 가독(기존 AssertReadable). 기존 Play92/Edit3 유지. 명령을 잇는 기존 시험이 요약을 명시적으로 닫거나 안정화를 기다리도록 고치는 것은 허용하되 판정 완화는 금지.

### S2 — 계산 오류 fail-closed 복구 상태 (로컬 연습, S1 뒤)
- 근거: 사용자 경계 "계산 오류 뒤 부분 이익과 정상 진행 금지"의 MF 대응이다. Battle은 이미 원자적이고 Run은 아니다(RP:3-4). 제품 원자성은 §16.5/§17.2 서버 트랜잭션의 몫이며 미구현이다. Run 트랜잭션화는 JS+C# 동치 별도 제안으로 둔다.
- 담당·경계: EC의 `Do/Reject/SendBattle/Render`와 복구 시트, 필요하면 신규 순수 분류 파일. Core 무변경. S1과 같은 파일이므로 순차로 진행한다.
- 동작: Run 명령 직전에 `run.State` 사본을 보관하고, 예외가 나면 `CanonicalState.Hash(...ToJsonGraph())`(공개 API, 시험 Fingerprint와 같은 재료)로 비교한다. ⓐ Run이 바뀌었으면 RECOVERY_REQUIRED: 모달을 띄우고 모든 공개 Run 명령은 Run을 호출하지 않고 false를 반환한다. 출구는 Abandon(SaveOpen이면 CloseWithoutSave)이며 세션 기억·버전을 바꾸지 않고 Depart로 이어진다. ⓑ 불변이고 플레이어 입력 코드(기존 오류 문구 카탈로그 기반 허용 목록)면 현행 토스트. ⓒ 불변이지만 무결성·미지 코드이거나 RuleException이 아닌 예외면 "이 행동은 적용되지 않았어요"와 출구를 보인다(`end`면 필수). 자동 재시도와 부분 결과의 성공 표시는 금지하고 `Debug.LogException`은 1회만 남긴다.
- 인수: ① Core의 모든 `RuleException("…")` 코드를 분류표에 명시하고 미지 코드는 fail-closed. ② 시험 전용 콘텐츠 주입으로 Core가 거절하는 적 패시브(현재 `AddEnemy` B:70의 `BATTLE_ENEMY_PASSIVE_NOT_IMPLEMENTED`; A 이식 뒤에도 미지원으로 남는 합성 id 사용)가 든 조우를 만든다 → `EnterNode`가 위치·층·RNG를 바꾼 뒤 실패. 복구 시트가 뜨고 노드·전투 클릭은 Run에 닿지 않으며, mirror로 부분 변경을 기록하고, Abandon 뒤 SessionMemory·버전 불변, 다음 Depart 성공. ③ RuleException이 아닌 예외를 던지는 위임 → 같은 결과. ④ 원자적 전투 실패 → 지문 불변과 출구 표시. 주입 seam은 출고 동작을 바꾸지 않는 조건으로 Root가 설계한다(현재 InternalsVisibleTo 없음).

## 4. 증거 공백 (NOT_RUN / 미구현)
- 이 검토의 실행: Unity·C#·Node·TS 시험과 외부 조사 모두 NOT_RUN. 위 소스 판독만이 근거다.
- 참고 묶음의 27/27·demo·typecheck·탐침 3건은 제공자 기록이며 MF 증거가 아니다.
- 두 번 탭(턴 종료·승리 후 지도로·보상 재배치)과 Run 부분 진행 경로는 고정 좌표와 소스에서 도출했고 Editor·기기에서 재현하지 않았다. 현재 저작 콘텐츠(라이브 20적)에서 Run 무결성 오류를 일으키는 경로는 확인되지 않았다.
- Root가 보고한 Editor Play92/Edit3·CI 성공은 Root의 증거이며 이 문서가 재확인하지 않았다. 물리 마우스 지도→전투 확인은 이전 native 최종 빌드에만 해당한다. 아직 실행하지 않은 새 예상 빌드의 실행·물리 입력은 미확인이다.
- Android/IL2CPP·터치·같은 프레임 멀티터치·48dp·안전 영역·글자 130% 실기기, 앱 일시정지·강제 종료·복귀(연습 기억은 설계상 사라짐): NOT_RUN.
- 서버·계정·네트워크 전송·재접속·영수증 영속·publicView 기반 렌더: NOT IMPLEMENTED.
- 승인된 시간 값은 0.3초/장과 2.6초 토스트뿐이다. 안정화 구간·적 단계 표시 간격·K는 시험값이며 기기 UX 확인이 필요하다.

## 5. ADOPTION_REVIEW와 다르게 본 점
- "선택의 데이터화(pending choice 서버 유지)": 전투에는 불필요하다. MF는 pick을 명령 인자로 원자 검증하며 해결 도중 선택 대기 상태가 없다.
- "K=2 백로그(높음)": Defer. 첫 연출 작업은 §14.3 배리어이고 플레이어 카드 연출은 아직 없다.
- "Sim/Presenter 분리": 이미 승인된 구조(§16.4)다. 새로 확인한 점은 C# 이벤트가 일부 적 행동을 담지 않는다는 것이다.
- CF-02: Battle은 이미 원자적이다. 실제 공백은 비트랜잭션 Run과 그 뒤에도 계속 진행하는 클라이언트다(S2).
- 추가 발견: Run rid가 버전에서 파생된다는 점, 그리고 턴 종료·승리 후 지도로·보상 재배치의 두 번 탭 위험.
