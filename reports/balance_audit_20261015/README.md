# 동일 조건 밸런스 원인 점검 — 2026-10-15

원본 1,320회를 같은 시작 상태로 다시 측정하고, 명세로 입증한 엔진 결함 5개를 고쳤다. 수정 후에도 **목표 이탈 32개(상한 초과 27, 하한 미만 5)가 그대로 남는다.** 승패·턴 수는 모두 같고 8회에서 순 HP 손실만 합계 81 줄었다. 이 결과로 후반 콘텐츠 활성화나 사람 플레이 밸런스 통과를 선언하지 않는다.

## 요구사항과 완료 범위

근거는 [원문 전투 §5](../../docs/source_v0.8.1/05_chapter.md), [적·조우 §10](../../docs/source_v0.8.1/10_chapter.md), [시험·목표 §19.4·19.6](../../docs/source_v0.8.1/19_chapter.md), [인수·협업 §20](../../docs/source_v0.8.1/20_chapter.md), [현재 실행 계약 §10](../../docs/AI_WORKFLOW.md)이다. 게임에서의 역할은 카드/필살기 반사와 다단 타격의 종료 경계를 명세대로 맞추고, 후보 조우의 목표 이탈을 재현 가능한 근거로 분해하는 것이다.

완료 조건은 원본 32개 전수 기록, 같은 시나리오·정책·초기 상태의 비교, 실제 HP 손실과 회복 회계 검증, 명세 위반의 회귀 시험, 반대편 AI 독립 검토와 재검증이다. 계산·보고서 작성은 끝났으며 **최종 교차 검토·통합 여부는 해당 CLI 결과와 [CURRENT](../../docs/CURRENT.md)의 최신 기록으로 별도 판정**한다. 이 보고서 자체는 검토 승인서가 아니다.

엔진 수정 담당 범위는 `engine/rules-core.js`와 다섯 회귀 시험 파일이다. 진단 도구는 `tools/review/balance_causes.mjs`와 그 시험이며 수치를 바꾸지 않고 기존 프로브를 재생하고 정식 입력을 검증한다. 이 보고서 작성 작업은 `README.md`, [metrics.md](metrics.md), [final_effects.json](final_effects.json), [expanded_final_summary.json](expanded_final_summary.json), [expanded40_final.json.gz](expanded40_final.json.gz)만 추가했다. 원문 v0.8.1, 데이터 0.7.0, 가격·확률·재화·나이, 조우와 적 수치, 목표 범위, 정책은 유지했다. 새 의존성도 없다.

## 실제로 입증하고 수정한 결함

| 결함 | 원문·재현 | 수정과 검증 |
|---|---|---|
| 필살기에도 카드 전용 반사 적용 | §5.7·5.8은 카드 피해 때 반사, §5.10은 필살기가 카드가 아니라고 명시. 세라 필살기에 HP 3, 루미 다단 필살기에 HP 15의 잘못된 반사 발생 | `hitEnemy`에 `!ctx.isUlt` 조건. [회귀 시험](../../engine/tests/reflect_ultimate.test.mjs)은 두 필살기 제외와 즉시·예약 카드의 기존 반사 3 유지를 검사 |
| 파티 사망 후 적의 남은 연타·후속 약화 진행 | §5.3·5.14는 매 공격/다단 타격 중간 종료. 쌍둥이 잉크 12×3, HP 5, 반격 2에서 죽은 뒤에도 3회 타격·반격해 적 HP 80→74 | `atk`/`atkdebuff` 매 타격 후 종료 판정. 이제 치명타 1회·해당 반격까지만 적용해 적 HP 78. [회귀 시험](../../engine/tests/enemy_lethal_multihit.test.mjs)은 생존 시 전 타격·약화 유지, 기억 봉투로 한 번 버티는 경계 포함 |
| 치명 카드 반사 후 플레이어의 다음 타격·광역 대상 진행 | §5.4·5.14는 효과 도중 전투 종료. HP 2에서 반사 3을 받은 뒤 다음 타격이 표식 적을 처치해 보상까지 주는 반례 | 카드 피해 내부에서 매 타격/대상 뒤 종료 판정. [회귀 시험](../../engine/tests/player_lethal_reflect.test.mjs)은 즉시·예약·광역·후속 방어 중단, 카드/예약 보존, 멱등 재시도, 생존·기억 봉투 경계와 해당 치명타 자체의 기존 처치 순서 보존을 검사 |
| 예약 해결 뒤 소멸 유물의 처치에서 승리 판정 누락·중복 종료 | §5.3의 만기 예약 처리·§5.14의 종료. 예약 세라 「최후의 탄」이 소멸할 때 `void_shard`가 마지막 적을 처치해도 다음 예약·뽑기가 진행되고, 가짜 다음 턴의 얼룩 피해가 승리를 패배로 바꿀 수 있음 | 만기 예약의 전체 후처리 뒤 `isOver() || checkEnd()`를 확인하고 중복 `finish()` 제거. [후처리 회귀 8개](../../engine/tests/post_resolution_end.test.mjs)는 후속 예약 미사용·게이지 미지급·단일 end·카드 보존·추가 턴 금지를 포함 |
| 치명 반사 직후 균열 회복이 HP 0 파티를 부활 | §5.9는 HP 0 패배이며 예외는 `memory_envelope`. HP 2에서 반사 3으로 0이 된 같은 타격이 균열을 내면 `silver_needle` 회복 3으로 전투를 계속할 수 있음 | 공유 `healParty`에서 HP≤0 회복 차단. [후처리 회귀](../../engine/tests/post_resolution_end.test.mjs)는 치명 반사 후 적 생존/사망, 표식 보상 순서, 정상 생존 회복 3과 기억 봉투가 HP 1로 버틴 뒤 회복되는 경계를 유지 |

앞선 세 결함은 실패 재현 후 수정해 통과시켰다. 반사 수정은 [reflect_red.txt](reflect_red.txt)·[reflect_green.txt](reflect_green.txt), 적 연타 수정은 [multihit_red.txt](multihit_red.txt)·[targeted_green.txt](targeted_green.txt), [추가 후처리 RED](post_resolution_red.txt)는 6실패/2통과, [관련 GREEN](post_resolution_green.txt)은22/22통과다. 마지막 다섯 수정 후 [engine_final.txt](engine_final.txt)의 전체168개가 통과했다. 과거 로그는 삭제하거나 새 결과로 덮어 해석하지 않는다.

이 다섯 결함이 32개 이탈 전체의 원인이라는 증거는 없다. 원본→최종 비교에서 승패·턴은 1,320회 모두 동일하다. 8회 HP 개선은 필살기 반사 제외의 영향과 같고, 적 사망 후 연타 중단은 무명 random/greedy 그룹의 불필요한 이벤트를 줄였다. 카드 반사 종료와 추가 두 후처리 수정도 이 표본의 집계 결과를 바꾸지 않았다. 추가 두 결함의 재현에는 유물이 필요한데 기준 프로브는 유물이 없다. 원래 표본 밖의 회귀 반례에서 불필요한 처치·보상, 승리 뒤 추가 턴, HP 0 부활을 막는 수정이다.


## 교차 검토와 진단 도구의 입력 검증

공식 Claude CLI의 [P1-CORE-AND-RUNNER-REVIEW 수신 기록](../../ops/ai/tasks/P1-CORE-AND-RUNNER-REVIEW.result.json)은 종료 코드 0, `model_usage_keys`가 `claude-opus-5-5` 하나, 요청 노력 Max로 남아 있다. [실제 CLI 세션 메타데이터](../ai_environment_20261015/claude_effort.json)의 effort/perTurnEffort도Max다. 제공자 서버 내부 사고 내용이나 토큰을 조회한 것은 아니다. 이 검토는 읽기 전용이며 검토자 명령·시험은 NOT_RUN이다. 수신 상태 `RECEIVED_UNVERIFIED`와 최종 통합 승인은 구분한다. 이 검토에서 추가 두 후처리 결함, HTML 내장 엔진 갱신 필요, 정식 시나리오 집합 검증 누락을 회수했다. 수정 후 [Opus 최종 교차 검토](../../ops/ai/tasks/P1-FINAL-CROSS-REVIEW.result.json)도 실제 회수했으며 전투·진단을 포함한9개 단위 모두 통합 가능 판정이다. ZIP 보안 경계와 새 공유 Core 연결은 별도 작은 변경 검토로 관리한다.

진단 도구는 다음 두 경계를 검증한다. 첫째, 목표 경계는 `assessTargets`에서 다시 얻고 보고서의 min/max·값·상태·지표 이름·실패 개수·전체 판정을 대조한다. 보고서 안에서 목표만 바꿔 통과시킬 수 없다. 둘째, **공식 CLI는 같은 requestedN·모드로 전체 `runProbe`를 다시 만들어 hashes·method·scenarios를 정확히 비교**한다. 축소 조우 집합, 적 구성과 조우 이름표 불일치, 다른 생성 시드·턴 제한, 위조한 method는 자체 일관성이 있어도 정식 전수 측정으로 받지 않는다. 계산용 `analyze()`가 작은 탐색 보고서를 지원하는 것과 공식 CLI가 전수 입력을 보증하는 것은 별개다. [진단 시험](../../engine/tests/balance_causes.test.mjs)에 정식 base/candidate 통과와 범위·결과·입력 위조 거절이 포함돼 있다.

## 측정 조건과 해석의 경계

44개 조우 × 조우당 10개 시나리오 × random/greedy/planner = **1,320회**다. 원본 인수 `350`과 최종 인수 `440`은 `max(10, floor(N/44))`에 의해 모두 조우당 10회로 산출된다. 어느 인수도 전체 시행 수가 아니다. 파티 3명은 시나리오별로 무작위 선택하되 같은 시나리오는 모든 정책에 같은 파티·덱·적·전투 시드를 쓴다. 생성 시 `setupHash`와 `begin` 직후 전체 `initialStateHash`를 저장해 실제 시작 상태를 대조한다. 수정 전후 440개 시나리오 전체와 1,320개 정책 시행의 해시·전투/정책 시드가 모두 일치했다.

기준은 각 캐릭터 **power4 160·HP 81**, 공명 0, 기본 R 무기 R1, 시작 덱 18장, 유물·장비·강화 없음, 적 HP/피해 100%다. §11.1이 정의한 기준 전투이므로 유효한 회귀 측정이다. 그러나 §19.4의 별도 스토리 원칙인 **스테이지와 같은 계정 레벨·SR 무기·공명 0의 계획 수준 플레이**를 직접 실행한 것은 아니다. §10.4의 스테이지 36/46/56, §11.1의 레벨 배율·SR 패시브·스킬 성장·실제 탐사 보상/성장까지 검사한 결과로 확장하지 않는다.

정책도 최적 플레이어가 아니다. `greedy`는 가장 싼 즉시 사용을 우선하고, `planner`는 적 HP 합이 40을 넘으면 `rv`/`ra`가 있는 카드의 첫 합법 다음 턴 예약을 우선한다. 일반 카드 대상은 살아 있는 적 중 HP가 가장 낮은 적, 고정 대상은 첫 표식 적이며, 턴 시작 사용 가능한 필살기는 첫 살아 있는 적에게 쓴다. 큰 공격 예고·균열 차단·방어 효율을 전략적으로 최적화하지 않는다. 정책 내 같은 시나리오 비교는 짝지었지만 서로 다른 조우의 파티 표본은 같지 않다. 이 한계가 어느 지표를 얼마만큼 일으켰는지는 분리 시험하지 않았다.

평균은 **승리·패배·시간 초과 전부**를 분모로 사용한다. 원본과 최종 모두 1,307승·13패·시간 초과 0이다. HP 손실은 시작 HP−종료 HP의 순손실이며 실제 회복을 뺀다. 진단 도구는 전 시행을 그대로 재생하고 조우별 실제 손실−회복이 순손실과 맞는지 검사한다. 턴은 실제 `s.turn`이다. 다음 턴 시작 예약으로 이긴 경우 새 턴에 속하며 목표에 맞추려고 1을 빼지 않았다. 별도 `processedPlayerTurns`는 반복 처리 횟수이고 상한 60이다.

전체 32행의 범위·수정 전후 측정·편차·상위 조우 기여·손실 원천은 [metrics.md](metrics.md)에 있다. 부호 있는 가중 기여는 집계의 산술 분해이며 적·행동의 독립 인과 효과가 아니다. 하한 5개도 누락하지 않았지만 더 빠른 승리·작은 피해 자체를 엔진 결함으로 보지 않는다. 통과용 강제 피해·지연이나 목표 완화는 하지 않았다.

## 원문 후보 조우의 불일치 — 수치 유지

[§10.3](../../docs/source_v0.8.1/10_chapter.md)의 조우 총 HP 예산과 비교하면 44개 중 다음 **6개**가 어긋난다. 후보 전사/조합의 명세 불일치이며, 현재 라이브 데이터에 이 조우가 활성화됐다는 뜻은 아니다. 단위 적 HP와 조우를 임의 변경하지 않았다.

| 구역 | 후보 조우 | 실제 총 HP | 원문 조우 예산 | 경계 차이 |
|---|---|---|---|---|
| 3 | 저울 기사 `e_scaleknight` | 52 | 60~85 | 하한보다 8 낮음 |
| 3 | 이자 쥐떼 3체 | 54 | 60~85 | 하한보다 6 낮음 |
| 4 | 객석 유령 3체 | 66 | 70~95 | 하한보다 4 낮음 |
| 4 | 무대 장치 + 프롬프터 | 103 | 70~95 | 상한보다 8 높음 |
| 5 | 태엽 파수꾼 + 녹슨 톱니 | 75 | 80~105 | 하한보다 5 낮음 |
| 5 | 녹슨 톱니 3체 | 60 | 80~105 | 하한보다 20 낮음 |

여기에 §10.3의 4/5구역 일반 패턴 길이 3과 §10.4 개별 초안의 1~2단계 패턴, 공증 거울 쌍의 “반사→공격(서로 회복 8)” 문구와 후보의 별도 회복 단계 사이에 해석 차이도 남는다. 후자는 설계 문구가 모호하므로 확정 오류로 고쳐 넣지 않았다. 체력이 낮아도 매 턴 다수 공격이면 HP 손실은 클 수 있으며, 예산 위반과 특정 평균의 인과관계를 이 자료만으로 확정하지 않는다. 무명의 두 HP 막대 170+170=340은 예산과 일치한다.

후반 23종은 `proposals/late_enemies.cjs`의 후보로 유지하고 `content/`, `data/`, 스토리 `authored`를 활성화하지 않았다. 이탈 해소와 C#·서사 등 §20.8·20.9의 나머지 인수 조건도 남는다.

## 표본 확대와 수정 효과를 분리

같은 생성 규칙으로 조우당 40회, **5,280회**를 별도 실행했다. 최종 확장 결과에는 원본 최종 1,320회의 trial 전체가 값까지 같은 부분집합으로 들어 있다. 수정 전 확장본과 최종 확장본도 5,280개 시작 상태가 전부 같고, 승패·턴 변경 0, HP 손실 42회 합계 447 감소다. 둘 다 5,231승·49패·시간 초과 0, 목표 이탈 21개다.

원본 10회 표본의 32개와 확장 40회 표본의 21개 차이는 표본 민감도다. 기존 12개가 범위 안으로 들어오고 **3구역 보스 greedy HP 손실 55.123%**가 상한 55%를 새로 넘었다. 두 표본 크기 모두 수정 전후 이탈 키는 그대로다. 결과가 더 좋아 보이는 확장본으로 원본 검사를 대체하거나 사람 수용을 승인하지 않는다. 신뢰구간·검정력·최적 정책 비교를 계산한 것도 아니다. 상세 키·정확 수치·수정 효과는 [expanded_final_summary.json](expanded_final_summary.json), 원시 데이터는 [expanded40_final.json.gz](expanded40_final.json.gz)를 따른다.

## 검증 증거와 미실행 경계

| 검증 | 관측 결과 | 범위·근거 |
|---|---|---|
| JS 엔진 전체 | 최종168/168 PASS | [engine_final.txt](engine_final.txt), 다섯 전투 수정·진단 경계·트레이스/정렬벡터 시험 포함 |
| 제안 저장 모듈 | 최종14/14 PASS | [proposal_final.txt](proposal_final.txt) |
| Python | 최종48/48 PASS | [python_final.txt](python_final.txt), 실행기20개·출고 목록 경계5개 포함 |
| 원본/최종 1,320회 | 재현·전수 HP 회계 PASS, 밸런스 목표 32개 FAIL | [paired_after.json](paired_after.json), [causes_after.json](causes_after.json) |
| 확장 전/최종 5,280회 | 시작 조건 대조 PASS, 밸런스 목표 21개 FAIL | [expanded_final_summary.json](expanded_final_summary.json) |
| 보고서 독립 검산 | 4개 보고서 708개 집계 행·32개 원인 행 PASS | 평균·승패·p50/p90·판정·가중 기여 합·손실 원천 합·HP 회계·원본 포함·gzip 왕복, [final_effects.json](final_effects.json) |
| HTML 자동 시험·스모크·퍼즈 | 마지막 다섯 수정 후 재실행 PASS | 후반 UI 10, 저장 UI 13, 스모크 723클릭·15화면; 후반 후보100 full·기존100 short/40 full에서 unexpected0. [browser_late_final.txt](browser_late_final.txt), [browser_memory_final.txt](browser_memory_final.txt), [smoke_final.txt](smoke_final.txt), [fuzz_late_final.txt](fuzz_late_final.txt), [fuzz_legacy_final.txt](fuzz_legacy_final.txt). 현재 b1ba35f4 소스·재빌드 HTML로 실행했고 과거 스크린샷은 보존함 |
| 사람 10판·재미/이해도, 최적 봇·레벨/SR 스토리, C# Battle/Run 동등성, 서버/DB 동시성, Unity 전투 플레이, Android 실기기, 실결제·운영 배포 | NOT_RUN — 이 보고서의 수용 범위 아님 | JS·HTML·Python PASS로 대신하지 않음 |

문서·인계 전체 검사는 통합 담당의 현재 로그를 따른다. 이 보고서 작성에서는 산술·파일 무결성 검사만 추가 실행했으며 전체 시험을 중복 실행하지 않았다. 입력 매니페스트의 기준선 검증은 [baseline.json](baseline.json)의 423개 대조·불일치 0 기록을 보존한다. 게임 원문·후보·데이터·기존 프로브·두 입력 매니페스트의 보존과 허용된 코드/지침 변경은 [preservation.json](preservation.json)에 구분했다. Unity 연결·공유 Core Play/로컬 빌드는 [별도 환경 증거](../../docs/AI_ENVIRONMENT.md)이며 전투 밸런스의 Unity 수용과 구분한다. 개발 중 엔진 변경 때문에 입력 매니페스트를 새로 생성하지 않았다.

## 재현과 기록 보존

저장소 루트에서 다음을 실행한다. 출력 이름은 새 파일을 써서 과거 증거를 보존한다.

```sh
node engine/tests/balance_probe.mjs 440 --late --out /tmp/mf-paired-recheck.json
node tools/review/balance_causes.mjs /tmp/mf-paired-recheck.json /tmp/mf-causes-recheck.json
node engine/tests/balance_probe.mjs 1760 --late --out /tmp/mf-expanded-recheck.json
node --test engine/tests/reflect_ultimate.test.mjs engine/tests/enemy_lethal_multihit.test.mjs engine/tests/player_lethal_reflect.test.mjs engine/tests/balance_causes.test.mjs
```

진단 CLI는 엔진·러너·콘텐츠 해시가 다른 과거 보고서를 현재 엔진으로 재생하려 하면 거절한다. 수정 전 기록은 그 소스 기준의 역사 자료이며 [causes_before.json](causes_before.json)에 따로 보존했다. [fix_effects.json](fix_effects.json)·[expanded_after_summary.json](expanded_after_summary.json)·[expanded40_after.json.gz](expanded40_after.json.gz)는 앞선 두 수정 시점의 기록이다. **다섯 전투 수정까지의 최신 비교는 [final_effects.json](final_effects.json)**이다.

| 식별자 | SHA-256 |
|---|---|
| 원본 paired_balance.json | `abbf43f1c7024ae3a0cefb66ae5548bd08bd140e364283f91210bf87617ba136` |
| 최종 paired_after.json | `2fb691c785f5b8fb0842a41795a7fbc5b174a764fed28625a890ca97095c0ce1` |
| 최종 causes_after.json | `22b58ddf575504992c92f85c8a4d41eed93a5942d752e57fa44981fef91ac80e` |
| 최종 engine/rules-core.js | `b1ba35f433197072200825ffa85cac49cf3d4812c4bf02032793fef8491d63eb` |
| 변경 없는 balance_probe.mjs | `44126e4bef3c89646d1d4fd63f2cbbf28948bf783d970f403f4e98f49d8e8410` |
| 진단 balance_causes.mjs | `54189384e106ef7de25cc6dc43fb9e65335ed1c39963c6650ce7be52ba569d32` |
| 변경 없는 data/game_data.json | `6a09e6dd2d81055738c84951ea5b5ae50c25fd8c9732c8443e81ec1498625688` |
| 최종 확장 JSON(비압축) | `67803a3c53947c1a2f56c5951a6804b31b98ed3c717a541ecd207b094ed17dc9` |

원문과 수치의 보존, 실제 CLI 검토의 회수·모델/노력/종료 근거는 통합 기록과 함께 판단한다. 이 문서의 관측·시험·추론·미실행 범위를 합쳐 “모두 완료”로 표시하지 않는다.
