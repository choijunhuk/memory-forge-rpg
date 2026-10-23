# 원본 32개 목표 이탈: 수치와 관측 근거

판정 집합은 원본 1,320회와 전투 결함 5개 수정 후 최종 1,320회에서 같다. 최신 엔진·진단 도구·원시 보고서 해시는 [final_effects.json](final_effects.json)에 있다. 아래는 원본 순서의 **32행 전부**다. 측정·원인 분해 원본은 [paired_after.json](paired_after.json), [causes_after.json](causes_after.json)이며, `failed` 배열의 0번 원소가 표의 1번이다. 비교 전 수치는 [기존 paired_balance.json](../late_actions_20261015/paired_balance.json)에서 읽었다.

범위의 출처는 [원문 §19.4](../../docs/source_v0.8.1/19_chapter.md)와 [§20.9](../../docs/source_v0.8.1/20_chapter.md)다. 3~5구역은 2구역의 하한을 유지하고 상한만 턴 ×1.1, HP 손실 +5%p로 계산한다. 승률 하한은 random에만 적용하며 승률 이탈은 0개다. 아래 표의 HP 수치와 편차·기여 단위는 %p, 턴은 턴이다. 표시는 소수 셋째 자리까지 반올림하며 실제 판정은 JSON 원값을 사용한다.

**기여는 인과 효과가 아니다.** 각 조우의 `가중치 × (조우 평균 − 상한)` 또는 `가중치 × (하한 − 조우 평균)`을 큰 순서로 두 개까지 적었다. 모든 조우의 부호 있는 기여를 합하면 전체 편차가 된다. 음수는 이탈을 상쇄하며, 두 개만의 합은 전체 편차와 다를 수 있다. 조우 간 파티 표본은 다르므로 특정 적·행동을 제거한 효과로 해석할 수 없다.

`손실`은 방어 후 실제 HP 감소의 조우 10회 합계다. `회복`도 실제 증가량만 합산한다. 이 둘의 차이가 순손실이며, `손실/회복` 열은 첫 기여 조우의 기록이다. `행동` 열의 예약·행동 불가·새 적 행동 횟수도 관측량이다. 반사량에는 카드만 남고 필살기 반사량은 모든 최종 추적에서 0이다. 마지막 예시 하나는 전체 집계의 분모가 아니며 원인 증명이 아니다.

| 번호 | 구역/유형/정책·지표 | 원문 범위 | 전 → 후 | 방향·경계 편차 | 상위 조우 기여(부호 유지) | 첫 조우 실제 손실/회복 또는 행동 관측 | 대표 시나리오(예시) |
|---|---|---|---|---|---|---|---|
| 1 | `1/normal/greedy` HP 손실 | 4~12 (§19.4) | 3.762 | 하한 미만 0.238 | `e_hound` +0.36 (평균 1.481, 가중치 0.143)<br>`e_cog+e_page` +0.33 (평균 1.687, 가중치 0.143) | 적 공격 33 + 과부하 6 = 39 HP / 회복 3 HP | `1/normal/e_moth+e_moth/0` |
| 2 | `1/normal/planner` HP 손실 | 4~12 (§19.4) | 3.898 | 하한 미만 0.102 | `e_hound` +0.36 (평균 1.481, 가중치 0.143)<br>`e_cog+e_page` +0.33 (평균 1.687, 가중치 0.143) | 적 공격 33 + 과부하 6 = 39 HP / 회복 3 HP | `1/normal/e_moth+e_moth/0` |
| 3 | `2/boss/planner` 턴 | 14~18 (§19.4) | 13.1 | 하한 미만 0.9 | `b_archivist` +0.9 (평균 13.1, 가중치 1) | 지금 350, 예약 210, 행동 불가 10 | `2/boss/b_archivist/3` |
| 4 | `3/normal/greedy` HP 손실 | 8~20 (§20.9) | 7.053 | 하한 미만 0.947 | `e_debtwraith+e_overdue` +0.711 (평균 4.444, 가중치 0.2)<br>`e_scaleknight` +0.571 (평균 5.144, 가중치 0.2) | 적 공격 143 + 과부하 9 = 152 HP / 회복 44 HP | `3/normal/e_debtwraith+e_overdue/5` |
| 5 | `3/normal/planner` HP 손실 | 8~20 (§20.9) | 7.44 | 하한 미만 0.56 | `e_scaleknight` +1.024 (평균 2.881, 가중치 0.2)<br>`e_debtwraith+e_sealgolem` +0.333 (평균 6.337, 가중치 0.2) | 적 공격 83 + 과부하 3 = 86 HP / 회복 16 HP | `3/normal/e_scaleknight/3` |
| 6 | `3/elite/random` 턴 | 6~8.8 (§20.9) | 8.85 | 상한 초과 0.05 | `e_collector` +0.35 (평균 9.5, 가중치 0.5)<br>`e_notarytwin+e_notarytwin` −0.3 (평균 8.2, 가중치 0.5) | 지금 221, 예약 152, 행동 불가 4; apDrain 24 | `3/elite/e_collector/2` |
| 7 | `3/elite/greedy` 턴 | 6~8.8 (§20.9) | 8.85 | 상한 초과 0.05 | `e_notarytwin+e_notarytwin` +0.25 (평균 9.3, 가중치 0.5)<br>`e_collector` −0.2 (평균 8.4, 가중치 0.5) | 지금 410, 예약 0, 행동 불가 13 | `3/elite/e_notarytwin+e_notarytwin/6` |
| 8 | `4/normal/random` 턴 | 4~6.6 (§20.9) | 6.88 | 상한 초과 0.28 | `e_stagerg+e_prompter` +0.58 (평균 9.5, 가중치 0.2)<br>`e_stagerg+e_ghostguest` +0.12 (평균 7.2, 가중치 0.2) | 지금 268, 예약 145, 행동 불가 5; delayRes 17 | `4/normal/e_stagerg+e_prompter/4` |
| 9 | `4/normal/random` HP 손실 | 8~20 (§20.9) | 20.733 | 상한 초과 0.733 | `e_ghostguest+e_ghostguest+e_ghostguest` +2.749 (평균 33.745, 가중치 0.2)<br>`e_stagerg+e_prompter` +0.337 (평균 21.687, 가중치 0.2) | 과부하 9 + 적 공격 857 = 866 HP / 회복 46 HP | `4/normal/e_ghostguest+e_ghostguest+e_ghostguest/8` |
| 10 | `4/normal/greedy` 턴 | 4~6.6 (§20.9) | 7 | 상한 초과 0.4 | `e_stagerg+e_prompter` +0.76 (평균 10.4, 가중치 0.2)<br>`e_stagerg+e_ghostguest` +0.24 (평균 7.8, 가중치 0.2) | 지금 469, 예약 0, 행동 불가 9; delayRes 17 | `4/normal/e_stagerg+e_prompter/4` |
| 11 | `4/elite/random` 턴 | 6~8.8 (§20.9) | 9.35 | 상한 초과 0.55 | `e_primadonna` +0.55 (평균 9.9, 가중치 0.5)<br>`e_baton` +0 (평균 8.8, 가중치 0.5) | 지금 234, 예약 160, 행동 불가 2 | `4/elite/e_primadonna/9` |
| 12 | `4/elite/random` HP 손실 | 18~33 (§20.9) | 36.934 → 36.523 | 상한 초과 3.523 | `e_baton` +8.953 (평균 50.905, 가중치 0.5)<br>`e_primadonna` −5.43 (평균 22.14, 가중치 0.5) | 적 공격 1323 + 과부하 15 = 1338 HP / 회복 101 HP | `4/elite/e_baton/8` |
| 13 | `4/elite/greedy` 턴 | 6~8.8 (§20.9) | 9.75 | 상한 초과 0.95 | `e_primadonna` +1.15 (평균 11.1, 가중치 0.5)<br>`e_baton` −0.2 (평균 8.4, 가중치 0.5) | 지금 472, 예약 2, 행동 불가 6 | `4/elite/e_primadonna/9` |
| 14 | `4/elite/greedy` HP 손실 | 18~33 (§20.9) | 38.004 → 37.49 | 상한 초과 4.49 | `e_baton` +7.615 (평균 48.23, 가중치 0.5)<br>`e_primadonna` −3.126 (평균 26.749, 가중치 0.5) | 적 공격 1226 + 과부하 18 = 1244 HP / 회복 72 HP | `4/elite/e_baton/4` |
| 15 | `4/elite/planner` 턴 | 6~8.8 (§20.9) | 8.9 | 상한 초과 0.1 | `e_primadonna` +0.45 (평균 9.7, 가중치 0.5)<br>`e_baton` −0.35 (평균 8.1, 가중치 0.5) | 지금 278, 예약 136, 행동 불가 6 | `4/elite/e_primadonna/9` |
| 16 | `4/boss/random` HP 손실 | 30~55 (§20.9) | 59.835 | 상한 초과 4.835 | `b_overture` +4.835 (평균 59.835, 가중치 1) | 적 공격 1803 + 과부하 6 = 1809 HP / 회복 355 HP | `4/boss/b_overture/3` |
| 17 | `5/normal/random` 턴 | 4~6.6 (§20.9) | 6.84 | 상한 초과 0.24 | `e_springguard+e_echo` +0.26 (평균 7.9, 가중치 0.2)<br>`e_springguard+e_rustgear` +0.2 (평균 7.6, 가중치 0.2) | 지금 215, 예약 135, 행동 불가 7; eatName 15 | `5/normal/e_springguard+e_rustgear/8` |
| 18 | `5/normal/random` HP 손실 | 8~20 (§20.9) | 30.387 | 상한 초과 10.387 | `e_pendulum+e_timeshard` +5.062 (평균 45.309, 가중치 0.2)<br>`e_rustgear+e_rustgear+e_rustgear` +4.099 (평균 40.494, 가중치 0.2) | 적 공격 1229 + 과부하 15 = 1244 HP / 회복 143 HP | `5/normal/e_rustgear+e_rustgear+e_rustgear/5` |
| 19 | `5/normal/greedy` 턴 | 4~6.6 (§20.9) | 7.04 | 상한 초과 0.44 | `e_springguard+e_echo` +0.32 (평균 8.2, 가중치 0.2)<br>`e_springguard+e_rustgear` +0.24 (평균 7.8, 가중치 0.2) | 지금 362, 예약 0, 행동 불가 8; eatName 10 | `5/normal/e_springguard+e_echo/1` |
| 20 | `5/normal/greedy` HP 손실 | 8~20 (§20.9) | 27.226 | 상한 초과 7.226 | `e_pendulum+e_timeshard` +5.695 (평균 48.477, 가중치 0.2)<br>`e_rustgear+e_rustgear+e_rustgear` +1.811 (평균 29.053, 가중치 0.2) | 적 공격 1292 + 과부하 12 = 1304 HP / 회복 126 HP | `5/normal/e_pendulum+e_timeshard/4` |
| 21 | `5/normal/planner` HP 손실 | 8~20 (§20.9) | 24.107 | 상한 초과 4.107 | `e_pendulum+e_timeshard` +3.704 (평균 38.519, 가중치 0.2)<br>`e_rustgear+e_rustgear+e_rustgear` +2.609 (평균 33.045, 가중치 0.2) | 적 공격 1124 + 과부하 12 = 1136 HP / 회복 200 HP | `5/normal/e_rustgear+e_rustgear+e_rustgear/5` |
| 22 | `5/elite/random` 턴 | 6~8.8 (§20.9) | 9 | 상한 초과 0.2 | `e_archbell` +0.2 (평균 9, 가중치 1) | 지금 256, 예약 140, 행동 불가 5; sealSlot 15 | `5/elite/e_archbell/1` |
| 23 | `5/elite/random` HP 손실 | 18~33 (§20.9) | 41.44 | 상한 초과 8.44 | `e_archbell` +8.44 (평균 41.44, 가중치 1) | 적 공격 1132 + 과부하 6 = 1138 HP / 회복 131 HP | `5/elite/e_archbell/1` |
| 24 | `5/elite/greedy` 턴 | 6~8.8 (§20.9) | 8.9 | 상한 초과 0.1 | `e_archbell` +0.1 (평균 8.9, 가중치 1) | 지금 401, 예약 0, 행동 불가 5; sealSlot 16 | `5/elite/e_archbell/2` |
| 25 | `5/elite/greedy` HP 손실 | 18~33 (§20.9) | 43.333 | 상한 초과 10.333 | `e_archbell` +10.333 (평균 43.333, 가중치 1) | 적 공격 1167 + 과부하 9 = 1176 HP / 회복 123 HP | `5/elite/e_archbell/8` |
| 26 | `5/elite/planner` HP 손실 | 18~33 (§20.9) | 41.399 | 상한 초과 8.399 | `e_archbell` +8.399 (평균 41.399, 가중치 1) | 적 공격 1094 + 과부하 6 = 1100 HP / 회복 94 HP | `5/elite/e_archbell/4` |
| 27 | `5/boss/random` 턴 | 14~19.8 (§20.9) | 20.5 | 상한 초과 0.7 | `b_nameless` +0.7 (평균 20.5, 가중치 1) | 지금 526, 예약 322, 행동 불가 7; apDrain 20, sealSlot 20, delayRes 14, silence 13, eatName 23, phase 10 | `5/boss/b_nameless/2` |
| 28 | `5/boss/random` HP 손실 | 30~55 (§20.9) | 73.457 | 상한 초과 18.457 | `b_nameless` +18.457 (평균 73.457, 가중치 1) | 적 공격 2328 + 과부하 9 = 2337 HP / 회복 552 HP | `5/boss/b_nameless/8` |
| 29 | `5/boss/greedy` 턴 | 14~19.8 (§20.9) | 21.3 | 상한 초과 1.5 | `b_nameless` +1.5 (평균 21.3, 가중치 1) | 지금 899, 예약 6, 행동 불가 6; apDrain 21, sealSlot 19, delayRes 14, silence 15, eatName 24, phase 10 | `5/boss/b_nameless/8` |
| 30 | `5/boss/greedy` HP 손실 | 30~55 (§20.9) | 81.399 | 상한 초과 26.399 | `b_nameless` +26.399 (평균 81.399, 가중치 1) | 적 공격 2377 + 과부하 15 = 2392 HP / 회복 414 HP | `5/boss/b_nameless/2` |
| 31 | `5/boss/planner` 턴 | 14~19.8 (§20.9) | 19.9 | 상한 초과 0.1 | `b_nameless` +0.1 (평균 19.9, 가중치 1) | 지금 496, 예약 279, 행동 불가 8; apDrain 19, sealSlot 19, delayRes 13, silence 13, eatName 22, phase 10 | `5/boss/b_nameless/2` |
| 32 | `5/boss/planner` HP 손실 | 30~55 (§20.9) | 71.687 | 상한 초과 16.687 | `b_nameless` +16.687 (평균 71.687, 가중치 1) | 적 공격 2220 + 과부하 12 = 2232 HP / 회복 490 HP | `5/boss/b_nameless/1` |

## 표에서 확인되는 점

- 하한 미만은 1구역 일반 greedy/planner HP, 2구역 보스 planner 턴, 3구역 일반 greedy/planner HP의 5개다. 더 빨리 이기거나 덜 다친 결과이며 그 자체로 엔진 오류가 아니다. 하한을 지키기 위한 강제 피해·지연을 넣지 않는다.
- 3구역 엘리트 두 정책의 턴 초과는 각각 0.05턴이다. collector와 notarytwin의 부호가 반대이며 단일 최악 조우만 전체 결과처럼 제시하면 안 된다.
- 4구역 일반의 긴 전투는 체력 103인 `e_stagerg+e_prompter`가 가장 큰 산술 기여다. 이 조합은 원문 조우 HP 예산 70~95를 넘지만, 조합 교체나 적 체력 변경의 분리된 효과는 시험하지 않았다.
- 4구역 엘리트의 HP 손실은 `e_baton`의 적 공격 합계가 크게 관측된다. `e_primadonna`의 회복·반사 조우는 턴에 큰 기여를 보인다. 필살기 반사 수정 후에도 두 현상이 남는다.
- 5구역 일반 HP 초과의 큰 기여는 `e_pendulum+e_timeshard`와 녹슨 톱니 3체다. 첫 조합의 적 공격은 random 1,229, greedy 1,292, planner 1,124 HP이며, 원인별 상세에는 각각 진자 칼날 943/928/828 HP와 시간 조각 286/364/296 HP가 구분된다. 새 봉인 행동만의 인과 효과로 돌릴 근거는 없다.
- 5구역 엘리트는 단일 종지기 대장 조우라 정책별 전체 평균이 그 조우 평균이다. 공격 60 등 원문 패턴을 유지했으며 실질적인 적 공격 손실이 random 1,132 / greedy 1,167 / planner 1,094 HP로 관측된다.
- 무명은 10회에 random 9승/1패, greedy 8승/2패, planner 8승/2패다. 모든 패배를 평균에 포함한다. planner 19.9턴·HP 손실 71.687%는 상한 19.8턴·55%를 계속 넘는다. 두 페이즈 총 HP 340 자체는 §10.3과 일치한다.

이 관측은 재현 가능한 후속 조사 위치를 지정한다. 수치 조정의 적정량, 사람의 난도·재미, 최적 정책의 성과는 여기서 확정하지 않는다.
