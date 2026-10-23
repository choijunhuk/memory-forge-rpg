# 01. 조사 결과 — 카드 사용부터 다음 입력까지 (레퍼런스: 카오스 제로 나이트메어)

> 조사일: 2026-10-16 · 대상 버전: 공개 공지 기준 2025-10-22(출시) ~ 2026-10-07
> 이 문서는 **확인된 사실**(§2)과 **아직 확인하지 못한 추정**(§3)만 다룬다. 내 게임을 위한 새 설계는 [03-design-spec.md](03-design-spec.md)에 따로 있다.

## 0. 범위와 방법

**사용한 것**
- 공식 STOVE 커뮤니티의 *알려진 이슈*(17건)·*업데이트 안내*(20건)·게임 가이드 게시판(한/영). 버그 공지는 "의도와 다르게 동작했다"는 기록이라, 정상 동작이 무엇인지 거꾸로 알려 주는 1차 근거로 썼다.
- 개발사 시즌 쇼케이스를 옮긴 언론 기사(Inven Global).
- 서드파티 공략: Prydwen, GameWith, itemlevel. 출시 전후 자료가 섞여 있어서 날짜를 같이 적었다.

**하지 않은 것** (요청과 기존 합의에 따른 경계)
- 설치된 클라이언트(`C:\ProgramData\Smilegate\...`)의 파일·에셋·메모리·네트워크 패킷은 열거나 분석하지 않았다. 디컴파일, 언팩, 후킹, 매크로나 자동화도 쓰지 않았다.
- 데이터마이닝 결과를 모아 둔 사이트는 근거로 쓰지 않았다.
- 공식 *전투 가이드* 영상([게시글](https://page.onstove.com/chaoszeronightmare/en/view/11134224))은 영상이라 내용을 분석하지 못했다. 직접 볼 자료로만 남긴다.
- STOVE 이용약관의 이용자 행동 조항은 "예정된 방식으로만 이용"할 것을 요구하고 정상 동작을 방해·변경하는 행위를 금지한다([약관](https://common.game.onstove.com/terms/preview?gameType=SG&termsType=1&langCode=en)). 그래서 관찰은 **정상 플레이 + 화면 녹화**로 한정했다([02-observation-protocol.md](02-observation-protocol.md)).

**근거 등급**

| 등급 | 뜻 |
|---|---|
| **A** | 공식 공지에 직접 쓰여 있다. 또는 공식 버그 수정 내용에서 의도된 동작이 바로 드러난다. 예: "2씩 감소하던 현상 수정"이면 의도는 1이다. |
| **B** | 서로 다른 서드파티 자료 두 곳 이상이 일치하고, 공식 자료와 충돌하지 않는다. |
| **C** | 출처가 하나뿐이거나, 베타 시기 자료이거나, 표현이 모호하다. 그래서 §3 추정으로 분류했다. |

공지 약칭: **KI-MMDD** = 해당 날짜의 *알려진 이슈* 공지, **UP-MMDD** = *업데이트 안내*. 링크는 §4에 모았다.

---

## 1. 한눈에 보기

```
 [입력] 카드 선택 ─ 타겟팅 카드면 적 지정                     ❓ 드래그 방식은 베타 자료뿐(H-01)
   │  검증 실패 → "카드 사용 조건이 맞지 않습니다" 팝업          ✅ F-05
   ▼
 [커밋] AP 소모 · 카드가 손패를 떠남                            ✅ 비용 지불(F-01) / ❓ 정확한 시점(H-02)
   ▼
 [해결] 연출 + 효과(타격·드로우·생성·발동) → 반응(트리거)들     ✅ F-13, F-20~F-26
   │  ├ 이 사이 다른 카드를 쓰면 '사용 예약'                     ✅ 존재(F-15) / ❓ 일반 규칙인지(H-05)
   │  └ 번뜩임 카드면 3지선다 선택 UI                            ✅ 존재(F-33) / ❓ 등장 시점(H-13)
   ▼
 [카운트] 카드 1회 사용 = 적 행동 카운트 -1                      ✅ F-09 · 에고 스킬은 감소 없음 ✅ F-10
   │  격파(브레이크) 시 해당 적 +1                               ✅ F-27 / ❓ -1과의 선후(H-03)
   ▼
 [인터럽트] 0이 된 적은 플레이어 턴 중에도 즉시 행동              ✅ F-11 / ❓ 카드 후속 효과와의 선후(H-04)
   │  여러 적이 '동시에' 행동하는 경우 있음                      ✅ F-28 / ❓ 순서 규칙(H-11)
   ▼
 [복귀] 다음 입력 가능                                            ❓ 정확한 조건(H-05, H-06)
```

✅ = §2의 확인된 사실 · ❓ = §3의 추정(관찰 테스트로 검증)

---

## 2. 확인된 사실

### 2.1 턴과 자원의 기본값

| ID | 내용 | 등급 | 근거 |
|---|---|---|---|
| F-01 | 기본값: 턴마다 AP 3, 드로우 5장, 손패 최대 10장. 카드마다 AP 비용이 있다. 아이템으로 바뀔 수 있다. | B | Prydwen, itemlevel, GameWith |
| F-02 | 덱 하나를 전투원 3명이 함께 쓴다. 손패에 여러 전투원의 카드가 섞인다. | B | itemlevel(카드 수 계산), GameWith 카드 문구("다른 전투원의 카드가 손패에…") |
| F-03 | 카드 종류는 공격·스킬·강화(Upgrade)다. 강화 카드는 사용하면 그 전투 동안 덱에서 빠지고 효과가 전투 끝까지 남는다. 저주·상태이상 카드도 있다. | B | Prydwen, itemlevel |
| F-04 | 턴이 끝나면 손패는 보존(Retain) 카드만 남기고 버린 더미로 간다. 뽑을 더미가 비면 버린 더미를 섞어 다시 채운다. | B | Prydwen, itemlevel |

### 2.2 입력과 검증

| ID | 내용 | 등급 | 근거 |
|---|---|---|---|
| F-05 | 사용 조건 검증이 있고, 실패하면 '카드 사용 조건이 맞지 않습니다' 팝업이 뜬다. 붕괴 회복 직후 이 팝업이 잘못 떠서 카드를 쓸 수 없던 버그가 2025-11-14에 수정됐다. | A | KI-1112 #8 (KR #6) |
| F-06 | '타겟팅 카드'라는 분류가 공식적으로 있다. 지정 공격과 무작위 공격은 다르게 취급된다. 지정 공격에만 붙어야 할 효과가 무작위 공격에도 붙던 버그가 수정됐다. | A | UP-1224, KI-0114 #19, UP-0909 카드 문구 |
| F-07 | PC에서는 단축키로 카드를 고를 수 있다. 전투 화면에 단축키를 표시하는 옵션도 있다. | A | UP-0408, UP-0819 |
| F-08 | 카드 효과가 자기 자신을 대상으로 잡을 수 있던 버그가 수정됐다. 즉 효과마다 대상으로 고를 수 있는 범위가 정해져 있다. | A | KI-1022 #12 |

### 2.3 해결 중 일어나는 일

| ID | 내용 | 등급 | 근거 |
|---|---|---|---|
| F-20 | 다단 히트와 '연속 타격' 개념이 있다(특정 타수를 연속 타격으로 취급한다는 설명 개선). 연속 공격 카드로 몬스터를 처치하면 피해가 0으로 표시되던 버그가 있었다. | A | UP-0729, KI-1203 #3 |
| F-21 | 피해에는 출처 속성이 붙는다. 공격 카드 피해와 그 밖의 효과 피해, 지정 공격과 무작위 공격, 지속 피해(DoT)와 일반 피해가 구분된다. | A | KI-0114 #17–19, KI-0708 |
| F-22 | 구역 이동이 트리거가 된다. 예: 버린 더미로 이동할 때, 소멸할 때(묘지에 있는 카드가 소멸할 때 포함), "버려질 시 이 카드 발동". 마지막 효과는 원래 카드의 효과와 **동시에** 발동해야 한다고 정리됐다. | A | KI-0114 #20, KI-0819, KI-0429 #3·#51 |
| F-23 | 턴당 발동 횟수 제한(예: 턴당 3회)이 있다. 제한보다 많이 발동하던 버그가 수정됐다. | A | KI-0708 #10 |
| F-24 | 조건을 채울 때까지 반복하는 효과가 있다. 인내가 바닥날 때까지 계속 사용되던 버그가 있었다. | A | KI-1224 #9 |
| F-25 | 한도가 있다. 버린 카드는 최대 200장이고, 카드 생성 횟수에도 제한이 있다. | A | Inven S4 기사(개발사 쇼케이스), KI-0114 #1 |
| F-26 | 인내(Tenacity) 피해는 카드 비용으로 정해진다. 약점 속성 카드도 타겟팅 카드도 아니면 줄어든다. | A | UP-1224 |
| F-13 | '발동(Activate)'과 '사용(Use)'은 서로 다른 개념이다. 손패에서 발동된 카드에만 적용되던 몬스터 지속 효과, 발동 카드에 행동 카운트 추가 감소가 빠지던 문제가 수정됐다. 2026-09에 두 용어의 구분을 명확히 하는 문구 정비가 예고됐다. | A | UP-0909 (KR), Inven S4 |
| F-33 | 번뜩임: 빛나는 카드를 사용하면 확률적으로 둘 중 하나가 일어난다. ① 전투원 고유 카드가 손패에 추가된다(그 턴 비용 0). ② 사용한 카드가 강화되고, 3개 선택지 중 1개를 고른다. 즉 카드 사용 흐름 중간에 선택 UI가 끼어든다. | B | GameWith 번뜩임 가이드, Prydwen |
| F-32 | 전투 시작이나 턴 시작에 카드 선택 스킬이 뜨면, 선택하기 전까지 진행이 멈춘다. 이 지점에서 진행이 막히던 버그가 여러 번 수정됐다. | A | KI-1203 #19·#25 |
| F-31 | 턴 시작 쪽 발동 시점은 '턴 드로우 직후', '턴 시작 시', '매 턴 첫 번째'로 나뉜다. | A | KI-0204 #27, KI-1022 #28 |

### 2.4 적 행동 카운트와 인터럽트

| ID | 내용 | 등급 | 근거 |
|---|---|---|---|
| F-09 | **카드 1회 사용 = 적 행동 카운트 1 감소.** 선택 하위 단계가 있는 카드 하나가 카운트를 2씩 줄이던 것이 버그로 수정됐다. | A | UP-0527 (KR) |
| F-10 | **에고 스킬 사용은 몬스터 행동 카운트를 줄이지 않는 것이 정상이다.** 특정 편성에서 줄어들던 현상이 '이슈'로 공지됐다. | A | KI-0617 #5 (KR) |
| F-11 | 각 적에게 카운트가 있고, 0이 되면 플레이어 턴 중이라도 바로 행동한다. ∞ 표시인 적은 플레이어 턴이 끝난 뒤 행동한다. 보스 행동표에는 행동마다 카운트 값이 따로 있다. | B | Prydwen, itemlevel, GameWith 보스 행동표 |
| F-12 | 카운트의 증감 자체가 다른 효과의 트리거가 된다(예: "행동 카운트가 감소할 때마다…"). 카운트 증가 면역 같은 효과도 있고, "카드 사용 시 모든 적 행동 카운트 +1" 같은 세트 효과도 있다. | A | KI-0708, UP-0527, KI-0617 #7, UP-0318 |
| F-27 | 격파(브레이크): AP +1, 격파한 전투원의 스트레스 회복, 해당 적 행동 카운트 +1. 인내는 다음 턴에 회복된다. 시즌 모드에 '격파 시 AP 획득 불가' 페널티가 따로 있는 것으로 보아, 기본 규칙은 AP 획득이다. | B (AP 획득 자체는 A) | Prydwen, GameWith 격파 가이드, KI-0429 #47 |
| F-28 | 몬스터 둘이 '동시에' 전투 행동을 하는 경우가 있다. 이때 전투 로그가 이상하게 표시되던 문제가 수정됐다. | A | UP-0318 |
| F-29 | 턴이 끝난 뒤의 추가 공격으로 전투에서 이길 수 있다. 이 경우 'N턴 이내 승리' 임무가 인정되지 않던 버그가 수정됐다. | A | KI-1112 #21 (KR #19) |
| F-30 | 플레이어와 몬스터가 동시에 쓰러지는 경우를 따로 처리한다. 이때 결과 화면으로 넘어가지 않던 버그가 수정됐다. | A | UP-0527 (KR) |

### 2.5 연출과 입력 타이밍 (질문의 핵심)

| ID | 내용 | 등급 | 근거 |
|---|---|---|---|
| F-14 | **'카드 흡수' 연출이 재생되는 도중에도 다른 카드를 사용할 수 있었다.** 영문 공지는 Absorb를 대문자로 써서, 일반 사용 연출이 아니라 특정 효과의 연출로 보인다. 이때 게임이 잠깐 멈추던 버그가 2025-11-14에 수정됐다. 입력을 막는 쪽으로 고쳤는지 멈춤만 고쳤는지는 공지만으로 알 수 없다. | A | KI-1112 #10 (KR #8) |
| F-15 | **'사용 예약된 카드' 개념이 있다.** 안식 카드가 발동하는 중에 다른 전투원의 카드를 쓰면 그 카드가 사용 예약 상태로 표시된다(표시가 남던 버그 수정). | A | KI-0408 #11 (KR) |
| F-16 | 연속 입력을 연출 파이프라인이 처리한다. 특정 카드를 연속으로 발동하면 손패가 이상하게 보이던 문제, 나르쟈 카드 직후 다른 전투원 카드를 쓰면 나르쟈 공격 애니메이션이 나오던 문제가 수정됐다. | A | KI-0527 #10, UP-0114 (KR) |
| F-17 | 진행이 연출 완료에 묶인 구간이 있다. 에고 스킬 연출이 멈추면 이후 진행이 불가능했다. | A | KI-0429 #27, KI-0617 #6 |
| F-18 | 수치 갱신을 연출 타이밍에 맞추는 것이 의도다. 운명(Fate) 효과로 카드 비용이 줄 때 "수치가 연출보다 먼저 갱신"되던 것을 버그로 고쳤고, 비용 감소 연출 중 비용 표시 오류도 고쳤다. 둘 다 전투 밖 화면일 수 있다. | A | UP-0527, UP-0708 |
| F-19 | 제거되는 카드는 일반 카드와 사용 연출이 다르다. 둘이 같게 나오던 것이 수정됐다. | A | UP-0617 |

### 2.6 되돌리기·자동 전투·로그·통신

| ID | 내용 | 등급 | 근거 |
|---|---|---|---|
| F-34 | 되돌리기가 두 단계로 있다. 퍼스트 스킬 '마지막 행동 되돌리기'와 '현재 턴 되돌리기'이고, 매일 충전되는 자원을 쓴다. 되돌린 뒤 적 행동 예고가 안 보이거나, 효과 아이콘·수치가 틀리게 표시되거나, 비용 감소가 적용되지 않던 버그가 반복 수정됐다. **복원 후 파생 상태와 화면을 다시 만드는 일이 오류의 원천이었다**는 뜻이다. | A | KI-1112 #28 (KR #26), KI-0429 #29, KI-1203 #21, KI-0114 #7, UP-0729, KI-1022 #32(일일 충전), Prydwen(비용) |
| F-35 | 자동 전투가 있다. 일반 전투는 ON/OFF 토글이고, 카오스 자동 전투는 2026-05-27에 추가됐으며 출격 모드에서는 쓸 수 없다. 별도로 '전술 위임'(빨리감기 포함)이 있다. 자동 전투 중 카드 사용이 잠깐 지연되던 문제, 카드를 쓰지 않고 턴이 끝나던 문제가 수정됐다. | A | UP-0429, UP-0527, UP-0617, UP-0819, UP-0909 |
| F-36 | 전투 로그가 있고, 로그 하단에 현재 턴이 표시된다. | A | UP-1203, KI-1112 #19 |
| F-37 | 대균열 전투에서 높은 중첩의 고통(Agony)을 반복해 터뜨리면 '연결'이 불안정해질 수 있었다. 적어도 이 모드에서는 전투 중에 서버 통신이 있다. | A | KI-1203 #13 |
| F-38 | 미확인 구역(맵 이벤트) 연출에는 배속 기능과 [자동 진행 속도/건너뛰기] 버튼이 있다. 전투 연출 배속은 확인하지 못했다(H-15). | A | UP-1224 (KR), UP-0114, UP-0429 |

---

## 3. 아직 확인하지 못한 추정

신뢰도는 주관적 판단이다. 각 항목에는 검증할 관찰 테스트([02-observation-protocol.md](02-observation-protocol.md))를 붙였다.

| ID | 추정 | 신뢰도 | 판단 근거 | 검증 |
|---|---|---|---|---|
| H-01 | 카드를 화면 아래에서 위로 드래그해 사용하고, 타겟팅 카드는 적을 지정한다. 탭으로 쓰는 방식이 있는지는 모른다. | 높음 | Prydwen 베타 분석(C)과 일반 UI 관례 | T-00 |
| H-02 | AP는 카드 효과를 계산하기 **전에** 빠진다. | 중 | 일반적 구현. 직접 근거 없음 | T-01 |
| H-03 | 행동 카운트 -1은 카드 효과가 해결된 **뒤에** 적용된다. 그래서 효과는 감소 전 카운트를 보고, 같은 카드의 격파 +1이 먼저 반영된다. | 중 | 격파로 행동을 끊는다는 공략 조언(B). 사용 시점 감소도 배제 못 함 | T-02, T-03 |
| H-04 | 카운트가 0이 된 적은 현재 카드의 효과와 그 후속 트리거가 모두 끝난 **뒤에** 행동한다. | 중 | F-15의 예약 구조와 공략의 "카드 사이에 끼어든다"는 묘사 | T-04 |
| H-05 | '사용 예약'은 특정 카드에만 해당하는 게 아니라 연출 중 입력의 일반 동작이다. 예약된 카드는 실행 시점에 다시 검증된다. 예약 가능 장수는 모른다. | 중 | F-14·F-15·F-16을 합쳐 본 추론 | T-05 |
| H-06 | 로직이 연출 시퀀서에 맞춰 단계적으로 진행된다(로직이 먼저 끝까지 계산되고 연출이 따라오는 방식이 아니다). | 낮음~중 | F-17·F-18은 지지하고, 되돌리기·자동 전투의 즉시성은 반대 방향 | T-06 |
| H-07 | 이번 턴에 사용한 카드는 묘지 숫자 옆 '+N'으로 따로 집계되다가 턴 종료 때 버린 더미에 합류한다. 그래서 턴 중 리셔플에서 빠진다. | 중~높음 | itemlevel(C). 반복 콤보를 막는다는 서술과도 맞음 | T-07 |
| H-08 | 숫자 카운트를 가진 적은 턴 종료 때 행동하지 않고 카운트가 다음 턴으로 넘어간다. | 중 | "적이 공격하지 않을 땐 턴을 일찍 끝내라"는 공략 조언, 턴 시작 시 카운트 +2 카드 문구 | T-08 |
| H-09 | 행동한 뒤 카운트는 '다음 행동'의 고유 값으로 다시 설정된다. | 중 | GameWith 보스 행동표의 행동별 카운트 열 | T-08 |
| H-10 | 다단 히트 중 대상이 쓰러지면 남은 타격은 소멸하거나 다른 대상으로 넘어간다. 어느 쪽인지 모른다. | 낮음 | F-20의 '피해 0 표시' 버그는 죽은 대상에게 타격이 계속 갔을 가능성을 시사 | T-09 |
| H-11 | 여러 적의 카운트가 동시에 0이 되면 화면 위치 순서대로 하나씩 행동한다(로그에는 '동시'로 남는다). | 낮음 | F-28 | T-10 |
| H-12 | 'Ultimate' 태그 카드는 효과가 해결된 뒤 턴을 바로 끝낸다. 예약된 카드의 처리 방식은 모른다. 현재 버전에 이 태그가 있는지도 확인이 필요하다. | 낮음 | Roonby·itemlevel(C). GameWith 공용 카드 표에는 없음 | T-11 |
| H-13 | 번뜩임 선택 UI는 카드 효과가 해결된 **뒤에**, 적이 행동하기 전에 뜬다. | 낮음 | 근거 없음(순서 미상) | T-12 |
| H-14 | 효과로 '발동'된 카드는 기본적으로 행동 카운트를 줄이지 않는다. 특정 몬스터 효과만 예외다. | 낮음~중 | F-13, "손패에서 사용되지 않으면 카운트가 추가로 감소"라는 몬스터 문구(GameWith) | T-13 |
| H-15 | 일반 전투에도 연출 배속 설정이 있다. | 낮음 | F-38은 미확인 구역 한정 | T-14 |
| H-16 | 손패가 10장일 때 뽑은 카드는 버린 더미로 간다. | 낮음 | itemlevel은 "드로우가 막힌다"고만 적음 | T-15 |
| H-17 | 일반 전투에서는 카드를 쓸 때마다 서버 응답을 기다리지 않는다. | 낮음 | 되돌리기·자동 전투가 즉시 동작함. 관찰로 확정할 수 없고 통신 조작 실험은 하지 않는다 | — |

> **H-06에 대해:** 레퍼런스가 어느 방식인지와 상관없이, 내 게임은 "로직 즉시 해결 + 연출 따라가기"로 설계했다([03-design-spec.md](03-design-spec.md) ADR-1). 레퍼런스 공지에서 반복된 버그 유형(F-16~F-18, F-34)을 구조적으로 피하기 위해서다.

---

## 4. 출처

**공식(STOVE 커뮤니티)** — 영문 게시글 링크다. 한국어판은 같은 날짜의 KR 게시판에 있다.

- 알려진 이슈: [KI-1022](https://page.onstove.com/chaoszeronightmare/en/view/11136129) · [KI-1112](https://page.onstove.com/chaoszeronightmare/en/view/11241982) ([KR](https://page.onstove.com/chaoszeronightmare/kr/view/11241960)) · [KI-1203](https://page.onstove.com/chaoszeronightmare/en/view/11413037) · [KI-1224](https://page.onstove.com/chaoszeronightmare/en/view/11593691) · [KI-0114](https://page.onstove.com/chaoszeronightmare/en/view/11786965) · [KI-0204](https://page.onstove.com/chaoszeronightmare/en/view/12016276) · [KI-0225](https://page.onstove.com/chaoszeronightmare/en/view/12232948) · [KI-0318](https://page.onstove.com/chaoszeronightmare/en/view/12450725) · [KI-0408](https://page.onstove.com/chaoszeronightmare/en/view/12954555) ([KR](https://page.onstove.com/chaoszeronightmare/kr/view/12954538)) · [KI-0429](https://page.onstove.com/chaoszeronightmare/en/view/13178038) · [KI-0527](https://page.onstove.com/chaoszeronightmare/en/view/13465662) ([KR](https://page.onstove.com/chaoszeronightmare/kr/view/13465648)) · [KI-0617](https://page.onstove.com/chaoszeronightmare/en/view/13683021) ([KR](https://page.onstove.com/chaoszeronightmare/kr/view/13682744)) · [KI-0708](https://page.onstove.com/chaoszeronightmare/en/view/13681966) · [KI-0729](https://page.onstove.com/chaoszeronightmare/en/view/14130164) · [KI-0819](https://page.onstove.com/chaoszeronightmare/en/view/14354339) · [KI-0909](https://page.onstove.com/chaoszeronightmare/en/view/14504414) · [KI-0930](https://page.onstove.com/chaoszeronightmare/en/view/14557087)
- 업데이트 안내: [UP-1112](https://page.onstove.com/chaoszeronightmare/en/view/11202695) · [UP-1203](https://page.onstove.com/chaoszeronightmare/en/view/11402378) · [UP-1224](https://page.onstove.com/chaoszeronightmare/en/view/11585211) ([KR](https://page.onstove.com/chaoszeronightmare/kr/view/11584622)) · [UP-0114](https://page.onstove.com/chaoszeronightmare/en/view/11778007) ([KR](https://page.onstove.com/chaoszeronightmare/kr/view/11778001)) · [UP-0204](https://page.onstove.com/chaoszeronightmare/en/view/12012929) · [UP-0318](https://page.onstove.com/chaoszeronightmare/en/view/12440443) · [UP-0408](https://page.onstove.com/chaoszeronightmare/en/view/12945661) · [UP-0429](https://page.onstove.com/chaoszeronightmare/en/view/13168282) · [UP-0527](https://page.onstove.com/chaoszeronightmare/en/view/13456678) ([KR](https://page.onstove.com/chaoszeronightmare/kr/view/13456659)) · [UP-0617](https://page.onstove.com/chaoszeronightmare/en/view/13671850) · [UP-0708](https://page.onstove.com/chaoszeronightmare/en/view/13895976) · [UP-0729](https://page.onstove.com/chaoszeronightmare/en/view/14123773) · [UP-0819](https://page.onstove.com/chaoszeronightmare/en/view/14344032) · [UP-0909](https://page.onstove.com/chaoszeronightmare/en/view/14502236) ([KR](https://page.onstove.com/chaoszeronightmare/kr/view/14502230)) · [UP-0930](https://page.onstove.com/chaoszeronightmare/en/view/14555091)
- 게임 가이드: [전투 가이드(영상)](https://page.onstove.com/chaoszeronightmare/en/view/11134224)

**개발사 발언 보도**
- [Inven Global — Season 3](https://labs.invenglobal.com/articles/21028/the-time-to-return-is-now-chaos-zero-nightmare-season-3-brings-massive-changes-to-reduce-fatigue) · [Inven Global — Season 4](https://labs.invenglobal.com/articles/24214/chaos-zero-nightmare-season-4-focus-on-long-term-balance-and-major-quality-of-life-improvements)

**서드파티 공략** — 날짜를 함께 봐야 한다.
- [Prydwen — Combat Explained](https://www.prydwen.gg/chaos-zero-nightmare/guides/combat-explained) (최종 수정 2025-09-29, 출시 전)
- [itemlevel — Combat Guide](https://itemlevel.net/chaos-zero-nightmare-combat-guide/) (2025-10-18, 출시 직전 시점의 서술)
- GameWith: [격파(Tenacity/Ravage)](https://gamewith.net/chaoszeronightmare/70884) · [스트레스](https://gamewith.net/chaoszeronightmare/70806) · [에고 스킬](https://gamewith.net/chaoszeronightmare/70809) · [번뜩임](https://gamewith.net/chaoszeronightmare/70805) · [공용 카드 목록](https://gamewith.net/chaoszeronightmare/72943) · [보스 행동표 예시](https://gamewith.net/chaoszeronightmare/72851)
