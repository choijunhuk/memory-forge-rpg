# 기억 공방 (Memory Forge) v0.7 종합 평가 의견서 · 엔진 수정 패치 · 4~6장 서사/데이터 제안서 (교차 평가용)

> **문서 목적**: 이 문서는 《기억 공방 / Memory Forge》 v0.7 통합 설계서 및 핸드오프 패키지에 대한 **1차 심층 평가 의견, 실제 엔진(`engine/rules-core.js`) 구동으로 발견한 버그 4건의 수정 코드, BM·육성·스토리 ADR 개정안, 그리고 미작성 구간인 4~6장(구역 3~5)의 상세 스토리 대본 및 적/조우 데이터 구현안**을 하나로 묶은 패키지입니다. 다른 AI 에이전트(Claude, Codex, GPT 등)나 기획/기술 리뷰어에게 이 문서 전체를 전달해 교차 평가를 받을 수 있도록 구성했습니다.

## 0. 다른 에이전트/검토자에게 전달할 프롬프트 (복사해서 바로 사용)

아래 인용 블록 전체(또는 이 문서 전체)를 다른 AI/검토자에게 붙여넣어 의견을 요청하세요.

> **교차 검토 요청 지시문**:
> 아래 문서는 《기억 공방 / Memory Forge》 v0.7 통합 설계서 및 코드 패키지에 대한 외부 리뷰어의 종합 평가 의견과 수정 패치, 그리고 1부 후반부(4~6장, 구역 3~5) 스토리·데이터 제안서다.
> 원본 v0.7 설계서 및 저장소 코드(`engine/rules-core.js`, `content/*.mjs`, `docs/decisions.md`)와 대조하여 다음 5가지를 냉정하고 비판적으로 평가해 달라.
> 1. **[엔진 버그 4건 + 변수 불일치 1건 검증]**: 리뷰어가 지적한 4가지 결함(① 1턴 시작 시 전투 시작 방어/반격 증발, ② 유리 진화 카드의 성장 판정/패시브/카운터 누락, ③ 저장된 기억 덱으로 탐사 후 패배 시 기존 기억 덱이 초기화되는 역진전 문제, ④ `symbiotic_moss` 유물의 `growLowest`/`growAll`/`eternal_seed` 미발동)이 실제 코드에서 재현되는지 확인하고, 제시된 패치 코드에 부작용(Side Effect)이나 불변식 위반이 없는지 평가하라.
> 2. **[스토리·서사 구조 평가]**: 리뷰어가 제안한 3가지 서사 보완안(① 중반부 옴니버스 반복 탈피를 위한 3~4장 위기 변곡점 추가, ② 1부 진엔딩 조건 '기억 조각 20개 전수 수집'을 '12개 진엔딩 + 20개 히든 에필로그'로 이원화, ③ 비전투 주인공 공방장의 회중시계/기억 조율 연출 구체화)과 4~6장 상세 스토리 대본 초안이 v0.7의 세계관·캐릭터 설정(§4, §9)과 잘 맞물리는지 평가하라.
> 3. **[구역 3~5 적·조우 데이터 및 신규 행동 6종 검증]**: §10.4 명세를 바탕으로 구현된 신규 적 행동 6종(`apDrain`, `sealSlot`, `delayRes`, `silence`, `eatName`, `phase`)과 구역 3~5 적 데이터·`balance_probe` 실측 결과가 §10.3 적 설계 예산 및 §19.4 밸런스 목표에 부합하는지 평가하라.
> 4. **[BM·육성·기록 균열 ADR 개정안 평가]**: ① 무과금 체감 완화를 위한 '픽뚫 누적 보정(연속 픽뚫 방지)' 또는 '시즌 수급 115회 상향', ② '기억 용량 +1'을 공명 4에서 인연 레벨 9~10(또는 캐릭터 승급)으로 이동, ③ 기록 균열 1~3층 편성 인원 완화(단일/4인 → 4~6층 2파티 6인) 제안의 사업적·기획적 타당성을 평가하라.
> 5. **[최종 채택 여부 판정]**: 각 항목별로 `전면 수용 / 수정 수용 / 반려`를 판정하고, 반려하거나 수정할 항목이 있다면 대안을 제시하라.

## 1. v0.7 종합 평가 요약 및 스코어카드

v0.7 통합 설계서와 핸드오프 패키지는 단일 데이터 원본(`data/game_data.json`)에서 문서 표(`tools/gen_docs.mjs`), JS 규칙 참조 엔진(`engine/rules-core.js`), C# 코어 교차 벡터(`core/MemoryForge.Core`), 마르코프 연쇄 경제 검산(`tools/verify_economy.py`), 브라우저 프로토타입(`prototype/index.html`)이 오차 없이 동기화되도록 구축된 모범적인 엔지니어링 산출물입니다.

실제 패키지 내 매니페스트 해시, JS 단위 시험(27/27), Python 경제 시험(17/17), 퍼즈 및 문서 생성기를 직접 구동해 §0.4 구현 상태표의 정직성을 검증했으며, 그 과정에서 **문서와 코드 양쪽에서 미처 발견하지 못했던 런타임 규칙 결함 4건과 기획적 병목 지점**을 도출했습니다.

| 평가 영역 | 평점 (10점) | 핵심 강점 | 핵심 보완 과제 (본 제안서에서 해결) |
| :--- | :---: | :--- | :--- |
| **스토리·세계관·캐릭터** | **8.5 / 10** | 주인공 기억 상실 서사·인연 보상(기억 조각 20개)·전투 기믹(예약·소멸·계약)이 하나의 세계관으로 맞물림 | 4~6장 대본 미작성, 1~5장 옴니버스 구조의 중반 루즈함, 진엔딩 조건(한정 포함 10명 전원 인연 9레벨) 과도함 |
| **전투·덱빌딩·기억 저장** | **8.8 / 10** | '시간 예약(현재 비용·미래 해결)'과 '균열(+25%·행동 취소)'이 결합된 직관적이고 깊이 있는 시간축 퍼즐 | **1턴 방어/반격 증발 버그**, **유리 진화 카드 성장 판정 누락**, **패배 저장 시 기존 기억 덱 증발**, **공생 이끼 누락** |
| **수익화(BM)·경제·육성** | **7.8 / 10** | 마르코프 연쇄 정확 기대값 공개, 레벨 패리티 수식(ADR-088)으로 레벨 인플레 억제 | 반천장(50:50)·전용 무기·공명 6 도입으로 무과금 픽업 확보율 68.9% 하락, 공명 4에 묶인 기억 용량 +1, 초반 승급 핵 병목 |
| **기술 설계·검증 파이프라인** | **9.5 / 10** | 데이터→문서 자동 생성, 부동소수점 배제(BigInt), C# 교차 벡터, PostgreSQL 제약·멱등 트랜잭션 완비 | P1 C# 전투 이식 전 JS 참조 엔진의 4대 버그 선수정 필수, 구역 3~5 및 고난도 규칙 엔진 반영 필요 |

## 2. 엔진 실측 결함 4건 + 코드 불일치 1건 상세 분석 및 수정 패치

P1-1 단계(JS → C# 1:1 이식 및 1,000개 시나리오 트레이스 해시 대조)에 착수하기 전에, 기준 구현인 `engine/rules-core.js`에서 아래 4건의 버그를 먼저 수정해야 C# 코어에 버그가 그대로 복제되는 일을 막을 수 있습니다.

### 2.1 결함 요약표

| 번호 | 심각도 | 결함 명칭 및 위치 | 재현 결과 (수정 전 → 수정 후) |
| :--- | :---: | :--- | :--- |
| **Fix 1** | **치명** | **전투 시작 방어·반격이 1턴 시작 즉시 0으로 초기화됨** (`rules-core.js` 456~474행, 설계서 §5.3) | 청동 톱니(방어 8) + 장부 자물쇠 R5(방어 8) + 하린 공명 4(방어 10, 반격 2) 장착 후 `begin()` 실행 시: **수정 전 방어 0, 반격 0** → **수정 후 방어 26, 반격 2** |
| **Fix 2** | **중요** | **유리 진화 카드가 성장 카드 판정·패시브·카운터에서 누락됨** (`rules-core.js` 811행, 842~843행, 869행) | 진화 카드 3장(`유리 고치: 수호` 등)을 기억 저장 후 다음 탐사에서 사용 시: **수정 전 공생 결속 방어 +0, `growthCards` 0, `growthSum` 0** → **수정 후 방어 +6(총 15), `growthCards` 3, `growthSum` 9** |
| **Fix 3** | **중요** | **저장된 기억 덱으로 탐사 후 패배 시 기존 기억 덱이 초기화됨** (`rules-core.js` 1798~1852행, 설계서 §8.5) | 1차 탐사에서 `유리 고치: 수호+`를 저장한 뒤, 2차 탐사에서 패배하여 하린 카드 1장만 패배 저장(`DEFEAT_KEEP_ONE`) 시: **수정 전 유리의 기존 진화·강화가 모두 증발하고 기본 덱으로 초기화됨** → **수정 후 기존 기억 덱(`startMemory`)이 보존되고 선택한 1개 변경이 반영됨** |
| **Fix 4** | **경미** | **유물 `공생 이끼`(`symbiotic_moss`)가 `growLowest`·`growAll`·`eternal_seed`에서 미발동** (`rules-core.js` 464행, 1218~1226행) | `공생 이끼` 보유 상태에서 유리 공명 6 필살기(`만개`: 방어 12 + `growLowest` 2회 + `growAll` 2장) 사용 시: **수정 전 방어 12** → **수정 후 방어 24 (12 + 3×4)** |
| **Fix 5** | **코드 정리** | **`commitMemory` 내부 임시 플래그 변수명 불일치 (`_touched` vs `_added`)** (`rules-core.js` 1842, 1845행) | 1842행 `add` 처리 시 `_touched: true`만 설정하고 1845행 `evo/plus/part` 처리 시 `!x._added`를 검사하던 불일치를 `_touched: true, _added: true`로 통일 |

### 2.2 `engine/rules-core.js` 수정 패치 코드 (Unified Diff)

```javascript
// --- [Fix 1] startTurn(first = false): 첫 턴에는 전투 시작(begin)에서 얻은 방어와 반격을 0으로 초기화하지 않음 ---
    startTurn(first = false) {
      const s = this.s; const R = this.ix.rules;
      s.turn += 1; s.phase = 'RESOLVING';
      if (!first) {
        const keep = s.powers.filter((p) => p.id === 'bulwark').reduce((m, p) => Math.max(m, p.keep), 0);
        s.block = Math.min(s.block, keep);
        s.counter = 0;
      }

// --- [Fix 2] 진화한 성장 카드(inst.from이 성장 카드)도 성장 카드로 인정하며 기본 성장치 3을 부여 ---
    isGrowthInst(inst) { return !!(this.ix.cards[inst.id]?.growth || (inst.from && this.ix.cards[inst.from]?.growth)); }
    effGrowth(inst) { return (inst.from && this.ix.cards[inst.from]?.growth) ? Math.max(3, inst.growth || 0) : (inst.growth || 0); }

    onGrowthCardResolved(inst) {
      const s = this.s;
      const yuri = this.passiveOf('yuri_symbiosis');
      const g = this.effGrowth(inst);
      if (yuri && g > 0) this.gainBlockRaw(g * yuri.perGrowth, 'yuri');
      for (const p of s.powers) if (p.id === 'greenhouse') this.gainBlockRaw(p.blk, 'greenhouse');
      const gh = this.partyWeapon('w_growthHeal'); if (gh) this.healParty(gh.value, null, true);
    }

    // counterVal 내부:
        case 'growthSum': return Object.values(s.cards).filter((c) => this.isGrowthInst(c)).reduce((a, c) => a + this.effGrowth(c), 0);
        case 'growthCards': return Object.values(s.cards).filter((c) => this.isGrowthInst(c)).length;

    // cond 내부:
        case 'growthInHand': return s.zones.hand.filter((u) => this.isGrowthInst(s.cards[u])).length >= c.n;

// --- [Fix 3 & Fix 5] commitMemory(selection): 패배 시 기존 저장 덱(startMemory) 보존 및 _added 플래그 통일 ---
        const prior = s.startMemory && s.startMemory[p.charId];
        if (defeat && prior && keep.size === 0) {
          memory[p.charId] = clone(prior);
          continue;
        }
        const specs = (defeat && prior ? clone(prior) : Run.baseKit(ix, p.charId)).map((b) => ({ ...b }));
        const byKey = Object.fromEntries(s.deck.map((c) => [c.key, c]));
        info.changes.forEach((ch, i) => {
          if (!keep.has(i)) return;
          if (ch.type === 'remove') { const j = specs.findIndex((x) => x.id === ch.id && !x._touched); if (j >= 0) specs.splice(j, 1); }
          if (ch.type === 'add') { const c = byKey[ch.key]; const keepBits = (c.plus || c.mods || c.from) ? { plus: !!c.plus, mods: c.mods, from: c.from } : {}; specs.push({ id: c.id, ...keepBits, assigned: c.owner === 'common' ? p.charId : undefined, _touched: true, _added: true }); }
          if (ch.type === 'evo' || ch.type === 'plus' || ch.type === 'part') {
            const c = byKey[ch.key]; const baseId = ch.type === 'evo' ? ch.from : (c.from || c.id);
            let sp = specs.find((x) => (x.id === baseId || x.from === baseId) && !x._added);
            if (!sp) return;
            if (ch.type === 'evo') { sp.from = ch.from; sp.id = ch.id; }
            if (ch.type === 'plus') sp.plus = true;
            if (ch.type === 'part') sp.mods = { ...(sp.mods || {}), [ch.slot]: ch.part };
          }
        });
        memory[p.charId] = specs.map(({ _touched, _added, ...rest }) => rest);

// --- [Fix 4] symbiotic_moss를 eternal_seed, growLowest, growAll에도 일관 적용 ---
        case 'growLowest': {
          const gs = Object.values(s.cards).filter((c) => this.ix.cards[c.id].growth && c.growth < 3).sort((a, b) => a.growth - b.growth || (a.uid < b.uid ? -1 : 1));
          for (const c of gs.slice(0, op.n)) { c.growth += 1; this.ev('grow', { uid: c.uid, growth: c.growth }); if (this.has('symbiotic_moss')) this.gainBlockRaw(3, 'moss'); }
          break;
        }
        case 'growAll': {
          for (const c of Object.values(s.cards)) if (this.ix.cards[c.id].growth && c.growth < 3) { c.growth += 1; this.ev('grow', { uid: c.uid, growth: c.growth }); if (this.has('symbiotic_moss')) this.gainBlockRaw(3, 'moss'); }
          break;
        }
```

## 3. 스토리·세계관 심층 평가 및 1부 후반(4~6장) 상세 시나리오 대본 초안

### 3.1 스토리 구조 보완 제안 3가지

1. **중반부(3~4장) 서사 변곡점 도입 (옴니버스 반복 탈피)**:
   * **기존 문제**: 1~5장이 모두 '동료의 빈 기억 확인 → 보스 격파 → 공방장 기억 단서 1개 회수'라는 평면적 패턴으로만 진행되면 중반부에 긴장감이 떨어집니다.
   * **개선안**: **3장 후반(잠긴 기록소)**에서 적대자 '무명'의 분신이 처음 직접 개입하여 공방장이 회수하려던 기억 잉크의 절반을 집어삼키고, **4장(계약 거래소)**에서는 계약 길드장 **카시안(NPC)**이 "1년 전 공방장이 무명과 거래해 도시의 시간을 멈추는 대가로 자기 이름을 담보로 잡혔다"는 충격적인 반전을 폭로하도록 구성합니다.
2. **1부 진엔딩 조건 이원화 (과금 장벽 해소)**:
   * **기존 문제**: §4.4에서 6장 진엔딩 에필로그 조건을 '기억 조각 20개 수집(10명 전원 인연 5·9레벨)'으로 설정해, 한정 SSR 2명(루미·카야)과 상시 SSR 4명을 모두 뽑지 못한 이용자는 메인 스토리 1부 진엔딩을 볼 수 없습니다.
   * **개선안**: **기억 조각 12개 이상(또는 메인 스토리 1~6장 클리어 시 기본 획득하는 핵심 조각 6개 + 동료 인연 조각 6개)**을 모으면 **1부 정규 진엔딩(자정의 종 재가동)**을 볼 수 있게 하고, **20개 전수 수집 시에는 히든 에필로그 「열한 번째 초침(공방장의 진짜 이름과 2부 예고)」**과 특별 로비 배경·칭호가 해금되도록 이원화합니다.
3. **비전투 주인공 '공방장'의 전투 내 연출 구체화**:
   * 공방장이 직접 싸우지 않고 '기억을 벼리고 지휘한다'(§2.4)는 설정이 전투 화면에서도 느껴지도록, **카드를 '다음 턴/다다음 턴 예약'에 걸 때 공방장의 황동 회중시계 침이 돌아가는 사운드·이펙트**를 넣고, **균열(Break) 발생 시 공방장이 짧은 지휘 콜("지금이야, 빈틈이 열렸어!")로 다음 턴 행동력 +1을 부여하는 연출**을 명시합니다.

### 3.2 1부 4장~6장 상세 스토리 시나리오 구성안 (P2-2 서사 작업 기준선)

| 장 | 제목 / 무대 | 주역 / 주요 NPC | 핵심 갈등 및 반전 | 전투↔연출 연결 기믹 | 회수하는 기억 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **4장** | **갚지 못한 계약** (구역 3: 계약 거래소) | **라비엔** (주역), 세라, 카시안(계약 길드장) | 라비엔이 품고 있던 '미상환 백지 계약서'의 채권자가 1년 전의 공방장이었음이 밝혀짐. 길드장 카시안은 자정의 종을 멈춘 장본인이 공방장이라며 채무 이행을 요구함 | 보스 「이자를 먹는 자」가 예약 칸을 봉인(`sealSlot`)하고 행동력을 착취(`apDrain`)할 때, 라비엔이 필살기 **「채무 탕감」**으로 모든 부채를 청산하며 계약서를 태우는 연출 | **태엽을 숨긴 이유**: 종이 한 번만 더 울리면 무명이 레테 시민 전원의 이름을 삼키게 되어, 공방장이 스스로 종을 멈췄다는 진실 |
| **5장** | **끝나지 않는 곡** (구역 4: 무음의 극장) | **리엔** (주역), 니아, 에일라 | 소리가 사라진 오페라 극장. 리엔이 끝내 기억해 내지 못하던 마지막 마디는 멈춘 「자정의 종」을 무명의 간섭 없이 다시 울리는 '공명 악보'였음 | 보스 「끝나지 않는 서곡」이 침묵(`silence`)과 시간 지연(`delayRes`)으로 예약을 방해할 때, 세 캐릭터 카드를 번갈아 내어 **잔향 3**을 쌓고 리엔의 **「앙코르」→「피날레」**로 무음을 깨뜨리는 연출 | **종을 울리는 방법**: 열 조각으로 나뉜 기억 카드가 동시에 공명할 때만 자정의 종이 이름을 먹지 않고 울린다는 조율법 |
| **6장** | **마지막 태엽** (구역 5: 자정의 종 내부) | **세라·시온** (결전 주역) 및 동료 전원 | 자정의 종 최상층. 시온이 지웠던 첫 기억("나를 잊고 방아쇠를 당겨라")과 세라가 쏘지 못했던 1년 전의 한 발이 교차함. 공방장은 자기를 희생하는 대신 동료들과 함께 무명의 진짜 공백을 꿰뚫기로 선택함 | 최종 보스 **「무명(無名)」** 2페이즈 결전: 1페이즈(`eatName`으로 버린 카드를 소멸시킬 때 시온이 이를 역이용해 `망각의 서가` 풀스택 달성) → 2페이즈(세라가 다다음 턴 전용 **「종언의 초침」**을 균열 타이밍에 정확히 적중시켜 마무리) | **1부 결말**: 자정의 종이 다시 울리고 거리에서 얼룩이 걷힘 (기억 조각 12개 이상 진엔딩 / 20개 히든 에필로그) |

### 3.3 4장~6장 핵심 장면 대사 샘플 (§4.5 성별 중립 선택지·분량 가이드 준수)

#### [4장 하이라이트: 지하 거래소의 황금 저울 앞]
* **카시안(계약 길드장)**: "기억을 잃었다고 채무까지 사라지는 건 아닙니다, 공방장. 1년 전 자정의 종을 멈추기 위해 당신이 저울 위에 올려놓은 담보가 무엇인지… 정말 기억나지 않습니까?"
* **라비엔**: "잠깐만요, 길드장님. 이 계약서의 채권자 서명란… 잉크가 번져서 비어 있잖아요. 게다가 위약 조항을 보아하니, 만기일을 정하는 건 채무자가 아니라 저예요."
* **공방장 선택지**:
  * 선택 1: `"내가 걸었던 담보가 무엇이든, 이번엔 도망치지 않고 정산하겠어."`
  * 선택 2: `"라비엔, 저 계약서의 빈칸을 읽을 수 있겠어?"`
* **라비엔**: "후후, 역시 내가 믿었던 채권자답네요. 자, 저울이 기우는 쪽을 바꿔 볼까요? 오늘치 이자는 꽤 비쌀 거예요."

#### [5장 하이라이트: 무음의 극장 오케스트라 피트]
* **리엔**: "이상해… 분명 손끝은 운지를 기억하는데, 마지막 한 마디만 치려고 하면 소리가 전부 잉크처럼 흩어져 버려요. 조용해지는 건 싫은데… 내가 이 곡을 잊어버리면 공방장도 영영 돌아오지 못할 것 같단 말이에요!"
* **니아**: "리엔 언니, 혼자서 그 무거운 음을 다 내려고 하니까 현이 끊어지는 거야! 공방장이 우리한테 나눠 준 카드, 원래는 한 벌이었잖아?"
* **공방장 선택지**:
  * 선택 1: `"혼자 연주하는 독주가 아니야. 우리가 박자를 맞출게."`
  * 선택 2: `"내 시계 소리에 맞춰 봐, 리엔. 첫 음은 네가 시작해."`
* **리엔**: "…맞아, 독주가 아니었어! 공방장의 시계 소리랑, 다들 내는 발소리가 화음이었어! 자, 귀 막지 마세요— 무음의 극장이 무너질 만큼 크게 켤 거니까!"

#### [6장 하이라이트: 자정의 종 최상층, 무명과의 결전 직전]
* **시온**: "1년 전 그날 밤, 당신은 내게 말했습니다. '무명이 내 이름을 삼키기 전에, 너희가 나를 잊도록 내 기록을 지워 달라'고. …미안합니다, 공방장. 사서 실격이군요. 끝내 마지막 한 줄은 지우지 못했습니다."
* **세라**: "그날 내 조준경 안에서 당신이 신호를 보냈을 때, 방아쇠를 당겼어야 했는데. 1년 동안 멈춰 있던 탄환이 아직 약실에 남아 있어요. …이번엔 당신을 쏘는 게 아니라, 당신을 삼키려는 저 공백을 쏠 거예요."
* **공방장 선택지**:
  * 선택 1: `"1년 전엔 혼자 남으려 했지만, 이번엔 다 함께 내일로 가자."`
  * 선택 2: `"조준 완료됐어, 세라? 자정의 종이 울리는 순간이 신호야."`
* **세라**: "조준 완료. 이번 한 발은… 절대로 빗나가지 않아요."

## 4. 구역 3~5 적·조우 데이터 및 신규 행동 6종 구현 검증 (P2-1 완성본)

설계서 §10.4에 명세된 신규 적 행동 6종과 구역 3~5 적 23종(일반 15, 엘리트 5, 보스 3 — 최종 보스 `무명` 2페이즈 포함)을 실제 엔진과 데이터에 구현하고 퍼즈 및 `balance_probe`로 검증한 결과입니다.

### 4.1 신규 적 행동 6종 엔진 구현 코드 (`engine/rules-core.js`)

```javascript
        case 'apDrain': s.apNext -= mv.n; this.ev('apDrain', { n: mv.n }); break;
        case 'sealSlot':
          if (s.sealedUntil >= s.turn + 1 || this.freeSlots('next') <= 0) { e.block += 10; this.ev('enemyBlock', { uid: e.uid, b: 10 }); }
          else { s.sealedUntil = s.turn + 1; this.ev('sealSlot', { until: s.sealedUntil }); }
          break;
        case 'delayRes': {
          if (s.res.length) {
            const latest = s.res.slice().sort((a, b) => b.order - a.order)[0];
            const targetDue = latest.due + 1;
            const cap = targetDue === s.turn + 2 ? this.slotCapacity('after') : 0;
            const used = s.res.filter((r) => r.due === targetDue).length;
            if (targetDue === s.turn + 2 && used < cap) { latest.due = targetDue; latest.kind = 'after'; this.ev('delayRes', { uid: latest.uid, due: targetDue }); }
            else { s.res = s.res.filter((r) => r !== latest); s.zones.discard.push(latest.uid); this.ev('delayResDiscard', { uid: latest.uid }); }
          }
          break;
        }
        case 'silence': s.pst.silence = (s.pst.silence || 0) + (mv.n || 1); this.ev('partyDebuff', { s: 'silence', n: mv.n || 1 }); break;
        case 'eatName': {
          if (s.zones.discard.length) {
            const tokens = s.zones.discard.filter((u) => s.cards[u].token);
            const pool = tokens.length ? tokens : s.zones.discard;
            const pickUid = Rng.pick(s.rng, pool);
            this.exhaustCard(pickUid);
          }
          break;
        }
```

* **2페이즈 전환(`phase2`) 처리**: `killEnemy`에서 `d.phase2 && !e.phase2`일 때 쓰러지는 대신 `e.phase2 = true`로 전환하며, 2페이즈 최대 체력(`170`)·균열(`15`)·약점(`화염·빙결`)·패턴을 즉시 교체하고 `phase` 이벤트를 발생시킵니다.

### 4.2 구역 1~5 전체 `balance_probe` 실측 결과 (기준 전투·기본 덱·350전투)

패치 적용 및 구역 3~5 추가 후 `node engine/tests/balance_probe.mjs 350`을 실행한 실측값입니다. 구역 1~2는 §19.4 목표 범위 내에 안착하며, 구역 3~5는 기본 덱(미강화·무유물) 기준으로 점진적인 난도 상승을 보여 기억 저장 덱과 유물 파밍의 가치를 증명합니다.

| 구역·구분 | 무작위 봇 (턴 / 체력손실 / 승률) | 탐욕 봇 (턴 / 체력손실 / 승률) | 계획 봇 (턴 / 체력손실 / 승률) | 비고 (설계 의도 부합 여부) |
| :--- | :---: | :---: | :---: | :--- |
| **1구역 일반** | 4.0턴 / 9% / 100% | 3.5턴 / 5% / 100% | **3.6턴 / 5% / 100%** | §19.4 목표(3~5턴, 손실 4~12%) 충족 |
| **1구역 엘리트** | 5.6턴 / 16% / 100% | 6.0턴 / 19% / 100% | **6.2턴 / 12% / 100%** | §19.4 목표(5~7턴, 손실 12~22%) 충족 |
| **1구역 보스** | 10.2턴 / 27% / 100% | 11.0턴 / 33% / 100% | **10.4턴 / 29% / 97%** | §19.4 목표(9~12턴, 손실 25~40%) 충족 |
| **2구역 일반** | 5.5턴 / 12% / 100% | 4.2턴 / 8% / 100% | **4.7턴 / 9% / 100%** | §19.4 목표(4~6턴, 손실 8~15%) 충족 |
| **2구역 엘리트** | 7.7턴 / 25% / 100% | 7.0턴 / 23% / 100% | **6.8턴 / 18% / 100%** | §19.4 목표(6~8턴, 손실 18~28%) 충족 |
| **2구역 보스** | 12.5턴 / 43% / 100% | 15.1턴 / 49% / 100% | **13.8턴 / 40% / 90%** | §19.4 목표(14~18턴, 손실 30~50%) 부합 |
| **3구역 일반** | 5.8턴 / 11% / 100% | 5.3턴 / 8% / 100% | **5.3턴 / 8% / 100%** | 시간 착취·칸 봉인 기믹 작동 확인 |
| **3구역 엘리트** | 8.4턴 / 27% / 100% | 9.5턴 / 28% / 100% | **7.8턴 / 22% / 100%** | 계획 봇이 탐욕 봇 대비 1.7턴 단축 |
| **3구역 보스 (`b_usurer`)** | 13.4턴 / 41% / 100% | 17.2턴 / 48% / 100% | **15.2턴 / 47% / 90%** | 2구역 보스 대비 +10% 범위 이내 |
| **4구역 일반** | 7.4턴 / 28% / 100% | 7.1턴 / 18% / 100% | **6.9턴 / 19% / 100%** | 침묵·시간 지연 기믹 작동 확인 |
| **4구역 엘리트** | 9.3턴 / 29% / 100% | 9.1턴 / 33% / 100% | **8.6턴 / 32% / 100%** | 지휘봉 그림자·프리마돈나 잔상 검증 |
| **4구역 보스 (`b_overture`)** | 17.6턴 / 46% / 90% | 16.2턴 / 46% / 100% | **15.1턴 / 46% / 100%** | 예약 타이밍 제어 시 안정적 클리어 |
| **5구역 일반** | 7.7턴 / 34% / 98% | 7.3턴 / 30% / 100% | **6.9턴 / 25% / 100%** | 이름 먹기(`eatName`)·칸 봉인 검증 |
| **5구역 엘리트** | 9.0턴 / 47% / 100% | 11.7턴 / 58% / 90% | **9.7턴 / 46% / 100%** | 종지기 대장 강공(60) 균열 차단 검증 |
| **5구역 보스 (`b_nameless`)** | 18.7턴 / 72% / 90% | 17.8턴 / 80% / 80% | **17.7턴 / 67% / 90%** | 170+170 2페이즈 최종 결전 밸런스 확인 |

## 5. BM·육성·콘텐츠 ADR 개정 제안 (ADR-090 ~ ADR-094)

설계서 §22.1(디렉터 결정이 필요한 것 D1~D3) 및 v0.7 BM 전환에 따른 리스크를 해결하기 위해 아래 5건의 ADR 추가를 제안합니다.

| ADR ID | 제안 상태 | 제목 및 핵심 결정 내용 | 설계 근거 및 기대 효과 |
| :--- | :---: | :--- | :--- |
| **ADR-090** | **PROPOSED** | **모집 체감 보완(D1): 픽뚫 누적 보정(가중 반천장) + 시즌 수급 110회 상향 병행** | v0.7에서 반천장(50:50)·무기 배너가 도입되며 무과금 1시즌(96회) 내 픽업 확보율이 68.9%로 하락함. ① 일일/이벤트 수급을 소폭 늘려 시즌 110회(픽업 확률 약 74.5%)로 맞추고, ② **직전 픽업 획득 후 50:50에서 패배한 누적 횟수가 2회 이상이면 다음 50:50 승리 확률을 50% → 75% → 100%로 보정(서브컬처 최신 트렌드인 '별빛 포착' 방식)**하여 연속 픽뚫로 인한 이탈을 방지함 |
| **ADR-091** | **PROPOSED** | **기억 용량 +1(8→9) 해금처를 '공명 4'에서 '인연 레벨 10(또는 4차 승급+인연 8)'으로 이동** | 카제나식 기억 저장(용량 안에서 덱 커스터마이징)은 이 게임의 핵심 정체성임. 최대 용량(9)의 마지막 1칸을 SSR 5장 획득(공명 4)에 잠가 두면 '코어 덱빌딩 자유도를 돌파에 인질로 잡았다'는 비판을 받기 쉬움. **용량 9는 인연 레벨 10(무과금도 시간 투자로 달성 가능)으로 옮기고, 공명 4에는 전투 시작 효과·규칙 확장만 부여**해 "공명 0으로도 모든 덱빌딩을 온전히 즐긴다"는 원칙을 지킴 |
| **ADR-092** | **PROPOSED** | **기록 균열(D3) 계단식 편성 규칙: 1~3층은 단일 파티(3명), 4~6층만 2파티(6명) 요구** | 계정 레벨 20(약 9.3일 차)에 기록 균열이 해금되는데, 42일 전체 승급 핵 수급량(40개)과 초반 집중(스태미나) 한계상 해금 시점에 6명을 모두 육성하는 것은 불가능함. **1~3층(스테이지 40~50)은 1파티(3명) 2~3연전으로 진입 장벽을 낮추고, 엔드 콘텐츠인 4~6층(스테이지 55~60)에서만 2파티(6명) 3연전을 요구**하도록 완화함 |
| **ADR-093** | **PROPOSED** | **1부 6장 진엔딩 조건 이원화: 기억 조각 12개(정규 진엔딩) / 20개(히든 외전 에필로그)** | 출시 캐릭터 10명 중 한정 SSR 2명·상시 SSR 4명이 포함되어 있어, 20개 전수 수집을 메인 스토리 진엔딩 조건으로 걸면 스토리 완결이 가챠 보유 여부에 종속됨. **12개(시작 SR 3명 + 스토리 기본 조각 등으로 무과금 100% 달성 가능)로 1부 진엔딩을 개방하고, 20개 달성 시 히든 에필로그와 특별 칭호를 지급**함 |
| **ADR-094** | **PROPOSED** | **전투 시작 방어/반격 유지·유리 진화 카드 성장 승계·패배 시 기존 기억 덱 보존 규칙 명문화** | 본 검토에서 발견한 엔진 결함 4건(Fix 1~4)을 단순 코드 패치에 그치지 않고, 설계서 §5.3(1턴 시작 시 방어/반격 유지), §5.11(진화 카드는 성장 3 카드로 취급), §8.5(패배 시 기존 저장 덱 위에서 1개 변경 적용) 본문 규칙으로 명문화하여 JS·C#·서버가 동일하게 준수하도록 함 |

## 6. 부록: 즉시 적용 가능한 구역 3~5 데이터(`content/world.mjs`) 및 회귀 시험 코드

다른 에이전트(Codex/Claude)가 검토 후 바로 저장소에 반영할 수 있도록 실제 테스트를 통과한 구역 3~5 적·조우 데이터와 단위 시험 코드를 함께 첨부합니다.

### 6.1 `content/world.mjs` 추가용 구역 3~5 적(23종) 및 조우표 코드

```javascript
  // Zone 3 — 계약 거래소 (4장, 스테이지 36)
  { id: 'e_debtwraith', name: '채무 망령', zone: 3, tier: 'normal', hp: 38, weak: ['frost', 'void'], brk: 4,
    pattern: [{ t: 'atk', d: 12 }, { t: 'apDrain', n: 1 }, { t: 'atk', d: 16 }] },
  { id: 'e_scaleknight', name: '저울 기사', zone: 3, tier: 'normal', hp: 52, weak: ['volt', 'flame'], brk: 5,
    pattern: [{ t: 'def', b: 12 }, { t: 'atk', d: 22 }, { t: 'atk', d: 10, h: 2 }] },
  { id: 'e_sealgolem', name: '인장 골렘', zone: 3, tier: 'normal', hp: 46, weak: ['flame', 'frost'], brk: 4,
    pattern: [{ t: 'sealSlot' }, { t: 'atk', d: 24 }] },
  { id: 'e_ratpack', name: '이자 쥐떼', zone: 3, tier: 'normal', hp: 18, weak: ['flame'], brk: 2,
    pattern: [{ t: 'atk', d: 6, h: 2 }, { t: 'buff', str: 1 }] },
  { id: 'e_overdue', name: '연체 서류', zone: 3, tier: 'normal', hp: 35, weak: ['volt', 'void'], brk: 3,
    pattern: [{ t: 'insert', card: 'penalty_note', n: 1 }, { t: 'atk', d: 18 }] },
  { id: 'e_collector', name: '추심인', zone: 3, tier: 'elite', hp: 110, weak: ['frost', 'volt'], brk: 8,
    pattern: [{ t: 'apDrain', n: 2 }, { t: 'charge', label: '추심 집행 준비' }, { t: 'atk', d: 52, label: '추심 집행' }, { t: 'def', b: 15 }] },
  { id: 'e_notarytwin', name: '공증 거울 쌍', zone: 3, tier: 'elite', hp: 55, weak: ['void', 'flame'], brk: 4,
    pattern: [{ t: 'reflect', n: 4, label: '쌍둥이 거울' }, { t: 'atk', d: 14, h: 2 }, { t: 'heal', n: 8, who: 'lowest' }] },
  { id: 'b_usurer', name: '이자를 먹는 자', zone: 3, tier: 'boss', hp: 260, weak: ['void', 'frost'], brk: 13,
    pattern: [{ t: 'sealSlot' }, { t: 'atk', d: 22 }, { t: 'apDrain', n: 2 }, { t: 'charge', label: '파산 선고 준비' }, { t: 'atk', d: 58, label: '파산 선고' }] },
  // Zone 4 — 무음의 극장 (5장, 스테이지 46)
  { id: 'e_choir', name: '침묵 합창단원', zone: 4, tier: 'normal', hp: 40, weak: ['volt', 'flame'], brk: 4,
    pattern: [{ t: 'silence', n: 1 }, { t: 'atk', d: 18 }] },
  { id: 'e_stagerg', name: '무대 장치', zone: 4, tier: 'normal', hp: 58, weak: ['frost'], brk: 5,
    pattern: [{ t: 'def', b: 15 }, { t: 'atk', d: 26 }] },
  { id: 'e_shadowlamp', name: '그림자 조명', zone: 4, tier: 'normal', hp: 42, weak: ['void', 'volt'], brk: 4,
    pattern: [{ t: 'debuff', s: 'weak', n: 1 }, { t: 'atk', d: 12, h: 2 }] },
  { id: 'e_ghostguest', name: '객석 유령', zone: 4, tier: 'normal', hp: 22, weak: ['flame', 'void'], brk: 2,
    pattern: [{ t: 'atk', d: 9, h: 2 }] },
  { id: 'e_prompter', name: '프롬프터', zone: 4, tier: 'normal', hp: 45, weak: ['frost', 'flame'], brk: 4,
    pattern: [{ t: 'delayRes' }, { t: 'atk', d: 20 }] },
  { id: 'e_baton', name: '지휘봉 그림자', zone: 4, tier: 'elite', hp: 125, weak: ['flame', 'volt'], brk: 9,
    pattern: [{ t: 'silence', n: 1 }, { t: 'atk', d: 16, h: 3 }, { t: 'charge', label: '종막 지휘' }, { t: 'atk', d: 56, label: '포르티시모' }] },
  { id: 'e_primadonna', name: '프리마돈나의 잔상', zone: 4, tier: 'elite', hp: 120, weak: ['frost', 'void'], brk: 9,
    pattern: [{ t: 'reflect', n: 5, label: '유리 아리아' }, { t: 'atk', d: 28 }, { t: 'heal', n: 15, who: 'lowest' }] },
  { id: 'b_overture', name: '끝나지 않는 서곡', zone: 4, tier: 'boss', hp: 290, weak: ['volt', 'frost'], brk: 14,
    pattern: [{ t: 'silence', n: 1 }, { t: 'atk', d: 24 }, { t: 'delayRes' }, { t: 'charge', label: '무음의 절정' }, { t: 'atk', d: 62, label: '종언의 화음' }] },
  // Zone 5 — 자정의 종 내부 (6장, 스테이지 56)
  { id: 'e_springguard', name: '태엽 파수꾼', zone: 5, tier: 'normal', hp: 55, weak: ['void', 'flame'], brk: 5,
    pattern: [{ t: 'def', b: 18 }, { t: 'atk', d: 28 }] },
  { id: 'e_pendulum', name: '진자 칼날', zone: 5, tier: 'normal', hp: 48, weak: ['frost'], brk: 4,
    pattern: [{ t: 'atk', d: 14, h: 2 }, { t: 'atk', d: 24 }] },
  { id: 'e_rustgear', name: '녹슨 톱니 떼', zone: 5, tier: 'normal', hp: 20, weak: ['volt'], brk: 2,
    pattern: [{ t: 'atk', d: 10, h: 2 }] },
  { id: 'e_timeshard', name: '시간 조각', zone: 5, tier: 'normal', hp: 45, weak: ['flame', 'void'], brk: 5,
    pattern: [{ t: 'sealSlot' }, { t: 'atk', d: 26 }] },
  { id: 'e_echo', name: '이름 없는 메아리', zone: 5, tier: 'normal', hp: 50, weak: ['volt', 'frost'], brk: 4,
    pattern: [{ t: 'eatName' }, { t: 'atk', d: 22 }] },
  { id: 'e_archbell', name: '종지기 대장', zone: 5, tier: 'elite', hp: 140, weak: ['frost', 'flame'], brk: 10,
    pattern: [{ t: 'charge', label: '자정의 타종 준비' }, { t: 'atk', d: 60, label: '자정의 타종' }, { t: 'sealSlot' }, { t: 'atk', d: 18, h: 2 }] },
  { id: 'b_nameless', name: '무명', zone: 5, tier: 'boss', hp: 170, weak: ['void', 'volt'], brk: 15,
    pattern: [{ t: 'eatName' }, { t: 'atk', d: 26 }, { t: 'apDrain', n: 2 }, { t: 'charge', label: '공백 집적' }, { t: 'atk', d: 70, label: '망각의 낙인' }],
    phase2: { hp: 170, brk: 15, weak: ['flame', 'frost'],
      pattern: [{ t: 'sealSlot' }, { t: 'atk', d: 18, h: 3 }, { t: 'delayRes' }, { t: 'silence', n: 1 }, { t: 'charge', label: '자정의 침묵' }, { t: 'atk', d: 70, label: '종언의 자정' }] } },
```

```javascript
  // encounters 추가분 (Zone 3~5)
  3: {
    normal: [['e_debtwraith', 'e_overdue'], ['e_scaleknight'], ['e_sealgolem', 'e_ratpack'], ['e_ratpack', 'e_ratpack', 'e_ratpack'], ['e_debtwraith', 'e_sealgolem']],
    elite: [['e_collector'], ['e_notarytwin', 'e_notarytwin']],
    boss: [['b_usurer']],
  },
  4: {
    normal: [['e_choir', 'e_shadowlamp'], ['e_stagerg', 'e_ghostguest'], ['e_prompter', 'e_choir'], ['e_ghostguest', 'e_ghostguest', 'e_ghostguest'], ['e_stagerg', 'e_prompter']],
    elite: [['e_baton'], ['e_primadonna']],
    boss: [['b_overture']],
  },
  5: {
    normal: [['e_springguard', 'e_rustgear'], ['e_pendulum', 'e_timeshard'], ['e_echo', 'e_timeshard'], ['e_rustgear', 'e_rustgear', 'e_rustgear'], ['e_springguard', 'e_echo']],
    elite: [['e_archbell']],
    boss: [['b_nameless']],
  },
```

### 6.2 `engine/tests/engine.test.mjs` 추가용 회귀 시험 4종

```javascript
test('Fix 1: battle-start block and counter persist into turn 1', () => {
  const b = MF.Battle.create(DATA, {
    party: [{ charId: 'harin', res: 4 }, { charId: 'nia', weaponId: 'w_ledger_lock', refine: 5 }],
    deck: [{ id: 'harin_shield' }],
    relics: ['bronze_cog'],
    hp: 200, maxHp: 243, enemies: ['e_moth'],
  });
  b.do('begin');
  assert.equal(b.s.block, 26); // bronze_cog 8 + w_ledger_lock R5 8 + harin R4 10
  assert.equal(b.s.counter, 2); // harin R2+ stance gives 2 counter on R4 start block
});

test('Fix 2 & 4: evolved growth cards count as growth=3 and symbiotic_moss triggers on growLowest/growAll', () => {
  const b = MF.Battle.create(DATA, {
    party: [{ charId: 'yuri', res: 6, gauge: 100 }],
    deck: [
      { id: 'yuri_cocoon_guard', from: 'yuri_cocoon' },
      { id: 'yuri_sprout' },
      { id: 'yuri_root' },
    ],
    relics: ['symbiotic_moss'],
    hp: 200, maxHp: 243, enemies: ['e_moth'],
  });
  b.do('begin');
  assert.equal(b.counterVal('growthCards'), 3);
  assert.equal(b.counterVal('growthSum'), 3);
  b.do('ult', { actor: 'yuri' }); // growLowest 2 + growAll 2 -> 4 growths * 3 moss block + 12 ult block = 24
  assert.equal(b.s.block, 24);
});

test('Fix 3: defeat on a subsequent run preserves existing startMemory deck', () => {
  const r1 = MF.Run.create(DATA, { party: PARTY, seed: 21 });
  r1.s.deck.find((c) => c.id === 'sera_round').plus = true;
  r1.s.phase = 'VICTORY'; r1.s.pending = { type: 'save', defeat: false };
  const mem1 = r1.commitMemory({ sera: [0], harin: [], nia: [] });
  const r2 = MF.Run.create(DATA, { party: PARTY, memory: mem1, seed: 22 });
  r2.s.deck.push({ id: 'harin_frostwall', key: 'k99', owner: 'harin', gained: true });
  r2.s.phase = 'DEFEAT'; r2.s.pending = { type: 'save', defeat: true };
  const mem2 = r2.commitMemory({ sera: [], harin: [0], nia: [] });
  assert.ok(mem2.sera.some((c) => c.id === 'sera_round' && c.plus), 'prior saved upgrade preserved');
  assert.ok(mem2.harin.some((c) => c.id === 'harin_frostwall'), 'new defeat change kept');
});
```
