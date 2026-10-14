/* Memory Forge v0.7 browser prototype UI. Uses window.MFCore (engine) and the embedded game data. */
(function () {
  'use strict';
  const DATA = JSON.parse(document.getElementById('mf-data').textContent);
  const MF = window.MFCore;
  const IX = MF.indexData(DATA);
  const root = document.getElementById('app');

  // ───────── storage (per-viewer convenience only) ─────────
  const store = {
    get(k, d) { try { const v = window.localStorage.getItem(k); return v ? JSON.parse(v) : d; } catch (e) { return d; } },
    set(k, v) { try { window.localStorage.setItem(k, JSON.stringify(v)); } catch (e) { /* storage unavailable */ } },
    del(k) { try { window.localStorage.removeItem(k); } catch (e) { /* ignore */ } },
  };
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const ATTR = Object.fromEntries(DATA.attributes.map((a) => [a.id, a]));
  const WTYPE = Object.fromEntries(DATA.weaponTypes.map((w) => [w.id, w.name]));
  const RAR_NAME = { B: '기본', A: '고급', S: '희귀', T: '토큰', C: '저주' };
  const START_INCOME = (DATA.gacha.seasonIncome && DATA.gacha.seasonIncome.f2p) || { crystals: 13920, limitedTicket: 9, standardTicket: 12 };
  // ?seed=123 makes runs reproducible (tests, bug reports); otherwise the clock seeds each run
  const URL_SEED = (() => { try { const v = new URLSearchParams(window.location.search).get('seed'); return v && /^\d{1,9}$/.test(v) ? Number(v) : null; } catch (e) { return null; } })();
  let runCounter = 0;

  const ICON = {
    volt: '<svg class="ico" viewBox="0 0 16 16"><path d="M9 1 3 9h4l-1 6 6-8H8z"/></svg>',
    flame: '<svg class="ico" viewBox="0 0 16 16"><path d="M8 1c1 3 4 4 4 8a4 4 0 0 1-8 0c0-2 1-3 2-4 0 2 1 3 2 3-1-3 0-5 0-7z"/></svg>',
    frost: '<svg class="ico" viewBox="0 0 16 16"><path d="M7.3 1h1.4v3l2-1.2.7 1.2-2.7 1.6V7h1.6l1.6-2.7 1.2.7L12 7.3h3v1.4h-3l1.2 2-1.2.7L10.4 8.7H8.7v1.6l2.7 1.6-.7 1.2-2-1.2v3H7.3v-3l-2 1.2-.7-1.2 2.7-1.6V8.7H5.6L4 11.4l-1.2-.7 1.2-2H1V7.3h3l-1.2-2L4 4.6 5.6 7h1.7V5.6L4.6 4l.7-1.2 2 1.2z"/></svg>',
    void: '<svg class="ico" viewBox="0 0 16 16"><path d="M8 1a7 7 0 1 0 0 14A7 7 0 0 0 8 1zm0 3a4 4 0 1 1 0 8 4 4 0 0 1 0-8z"/></svg>',
    none: '<svg class="ico" viewBox="0 0 16 16"><circle cx="8" cy="8" r="4"/></svg>',
    sword: '<svg class="ico" viewBox="0 0 16 16"><path d="M13 1h2v2L7 11l1 1-1 1-1-1-2 2-1-1 2-2-1-1 1-1 1 1z"/></svg>',
    shield: '<svg class="ico" viewBox="0 0 16 16"><path d="M8 1 2 3v5c0 3.5 2.6 6 6 7 3.4-1 6-3.5 6-7V3z"/></svg>',
    up: '<svg class="ico" viewBox="0 0 16 16"><path d="M8 2 3 8h3v6h4V8h3z"/></svg>',
    down: '<svg class="ico" viewBox="0 0 16 16"><path d="M8 14 3 8h3V2h4v6h3z"/></svg>',
    hourglass: '<svg class="ico" viewBox="0 0 16 16"><path d="M3 1h10v2c0 2-2 3.5-3.5 5C11 9.5 13 11 13 13v2H3v-2c0-2 2-3.5 3.5-5C5 6.5 3 5 3 3z"/></svg>',
    plus: '<svg class="ico" viewBox="0 0 16 16"><path d="M6 2h4v4h4v4h-4v4H6v-4H2V6h4z"/></svg>',
    eye: '<svg class="ico" viewBox="0 0 16 16"><path d="M8 3C4 3 1 8 1 8s3 5 7 5 7-5 7-5-3-5-7-5zm0 2.5A2.5 2.5 0 1 1 8 10.5 2.5 2.5 0 0 1 8 5.5z"/></svg>',
    zz: '<svg class="ico" viewBox="0 0 16 16"><path d="M2 3h6v1.5L4.5 9H8v1.5H2V9l3.5-4.5H2zm7 4h5v1.2l-3 3.6h3V13H9v-1.2l3-3.6H9z"/></svg>',
    ink: '<svg class="ico" viewBox="0 0 16 16"><path d="M8 1s-5 6-5 9a5 5 0 0 0 10 0c0-3-5-9-5-9z"/></svg>',
    mirror: '<svg class="ico" viewBox="0 0 16 16"><path d="M8 1a5 6 0 1 0 0 12A5 6 0 0 0 8 1zm-2 3 4 5-1 1-4-5z"/></svg>',
  };
  const attrIcon = (a) => ICON[a] || ICON.none;

  // ───────── app state ─────────
  const app = {
    screen: 'home', run: null, sel: null, target: null, picker: null, modal: null, toast: null, toastTimer: 0,
    gacha: store.get('mf07_gacha', null) || MF.Gacha.newState(20261014),
    memory: store.get('mf07_memory', {}),
    settings: Object.assign({ scale: 100, allOwned: true }, store.get('mf07_settings', {})),
    party: Object.assign({ chars: ['sera', 'harin', 'rien'], weapons: {}, length: 'short', difficulty: 'normal', useMemory: true }, store.get('mf07_party', {})),
    gachaTab: 'limited_lumi', lastPull: [], codexChar: 'sera', codexTab: 'chars', saveSel: null, lastDmg: {}, logOpen: false,
  };
  if (!app.gacha.crystals && !app.gacha.spent.pulls) { app.gacha.crystals = 16000; app.gacha.tickets.limitedTicket = 5; }
  const saveGacha = () => store.set('mf07_gacha', app.gacha);
  const saveMemory = () => store.set('mf07_memory', app.memory);
  const saveSettings = () => store.set('mf07_settings', app.settings);
  const saveParty = () => store.set('mf07_party', app.party);
  function toast(msg) { app.toast = msg; clearTimeout(app.toastTimer); app.toastTimer = setTimeout(() => { app.toast = null; render(); }, 2600); }

  const ERR = {
    AP_LOW: '행동력이 부족해요.', SLOT_FULL: '그 턴의 예약 칸이 가득 찼어요.', BAD_TARGET: '대상을 먼저 고르세요.', FIXED_MARK_REQUIRED: '표식이 있는 적만 고를 수 있어요.',
    NOT_RESERVABLE: '이 카드는 예약할 수 없어요.', RESERVE_ONLY: '이 카드는 예약 전용이에요.', DEBT_CAP: '부채 상한을 넘어요.', LOAN_OVERFLOW: '행동력 최대치(9)를 넘어요.',
    NOT_DEFERRABLE: '다음 턴에 갚을 부채가 없어요.', RESTRAINT_UNAVAILABLE: '이번 턴엔 절제 계약을 맺을 수 없어요(이미 3장 사용 또는 이미 계약).', NO_EMPTY_SLOT: '봉인할 빈 다음 턴 칸이 없어요.',
    RESERVATION_REQUIRED: '앞당길 예약이 없어요.', PULL_DESTINATION_FULL: '다음 턴 칸이 가득 차서 앞당길 수 없어요.', OVERLOAD_WOULD_KILL: '과부하로 쓰러질 수 있어 사용할 수 없어요.',
    ULT_NOT_READY: '필살기 게이지가 부족해요.', CRYSTALS_LOW: '결정이 부족해요.', COINS_LOW: '동전이 부족해요.', CANNOT_HEAL: '기억의 모루 때문에 회복할 수 없어요.',
    WOULD_KILL: '체력이 부족해 고를 수 없어요.', BEGINNER_LIMIT: '초심자 모집은 50회까지예요.', OVER_CAPACITY: '기억 용량을 넘었어요.', DEFEAT_KEEP_ONE: '패배 시에는 변경 1개만 남길 수 있어요.',
    BEGINNER_TEN_ONLY: '초심자 모집은 10회 단위만 가능해요.', BAD_COUNT: '1회 또는 10회만 모집할 수 있어요.', ZONE_NOT_AUTHORED: '아직 열리지 않은 구역이에요.', WRONG_PHASE: '지금은 할 수 없는 행동이에요.',
  };
  const errMsg = (e) => ERR[e.code] || e.message || String(e);

  // ───────── helpers ─────────
  const charOf = (id) => IX.chars[id];
  const owned = (id) => (app.gacha.owned.chars[id] || 0) > 0;
  const starter = (id) => !!(charOf(id) && charOf(id).starter);
  const usable = (id) => app.settings.allOwned || owned(id) || starter(id);
  const resOf = (id) => MF.Gacha.resonance(app.gacha, id);
  function weaponsFor(charId) {
    const t = charOf(charId).weapon;
    return DATA.weapons.filter((w) => w.type === t && (w.rarity === 'R' || (app.gacha.owned.weapons[w.id] || 0) > 0 || app.settings.allOwned && w.signature === charId));
  }
  function pv(v) { return v >= 100 ? v / 100 : v; }
  function passiveText(w, r) {
    const vals = w.passive.refineAlt || w.passive.values;
    return w.passive.text.replace('{v}', r ? String(pv(vals[Math.min(5, Math.max(1, r)) - 1])) : vals.map(pv).join('/'));
  }
  function emblem(ch, size) { return `<span class="emblem attr-${ch.attr}" style="${size ? `width:${size}px;height:${size}px` : ''}">${esc(ch.name[0])}</span>`; }
  function ownerLabel(def, inst) {
    if (IX.chars[def.owner]) return IX.chars[def.owner].name;
    if (def.owner === 'weapon') return inst && inst.wielder ? `${IX.chars[inst.wielder].name}·무기` : '무기';
    if (def.owner === 'common') return '공용';
    return '저주';
  }
  function attrOfDef(def, inst) { if (def.attr === 'wielder') return inst && inst.wielder ? IX.chars[inst.wielder].attr : 'none'; return def.attr || 'none'; }
  function partNames(inst) { return [inst && inst.mods && inst.mods.c, inst && inst.mods && inst.mods.e, ...((inst && inst.temp) || [])].filter((p) => p && IX.parts[p]).map((p) => IX.parts[p].name); }
  function effDefFor(inst) {
    // evolved / upgraded text without a battle context
    const base = IX.cards[inst.id];
    if (!inst.plus) return base;
    return { ...base, eff: base.plus?.eff || MF.upgradeOps(base.eff, IX.rules.upgradeMult) };
  }
  function cardFace(inst, o = {}) {
    const def = o.def || effDefFor(inst);
    const attr = attrOfDef(def, inst);
    const cost = o.cost !== undefined ? o.cost : def.cost;
    const parts = partNames(inst);
    const tags = [];
    if (inst.plus) tags.push('<span class="pill brass">강화+</span>');
    if (IX.cards[inst.id].growth || (inst.from && IX.cards[inst.from] && IX.cards[inst.from].growth)) tags.push(`<span class="pill">성장 ${inst.growth || 0}/3</span>`);
    for (const p of parts) tags.push(`<span class="pill time">${esc(p)}</span>`);
    return `<button class="card attr-${attr} ${o.cls || ''}" ${o.act ? `data-act="${o.act}"` : ''} data-uid="${esc(inst.uid || '')}" data-key="${esc(inst.key || '')}" data-id="${esc(def.id)}" aria-label="${esc(def.name)}">
      <span class="cost num ${cost === 0 ? 'free' : ''}">${cost}</span>
      <span class="cn">${esc(def.name)}${inst.plus ? '+' : ''}</span>
      <span class="owner">${attrIcon(attr)}<b>${esc(ownerLabel(def, inst))}</b>· ${RAR_NAME[def.rar] || ''}</span>
      <span class="tx">${esc(MF.cardText(def, IX))}</span>
      <span class="ft">${tags.join('')}</span>
    </button>`;
  }

  // ───────── navigation & render ─────────
  const NAV = [['home', '공방'], ['party', '탐사'], ['gacha', '모집'], ['codex', '도감'], ['memory', '기억 덱'], ['settings', '설정']];
  function render() {
    document.documentElement.style.setProperty('--scale', String(app.settings.scale / 100));
    const inRun = app.run && !['DONE'].includes(app.run.s.phase);
    const nav = `<nav class="nav"><div class="nav-in"><span class="brand">기억 공방</span>${NAV.map(([k, l]) =>
      `<button data-act="nav" data-to="${k}" class="${(app.screen === k || (k === 'party' && isRunScreen())) ? 'on' : ''}">${l}${k === 'party' && inRun ? ' ●' : ''}</button>`).join('')}
      <button data-act="help">규칙</button></div></nav>`;
    let body = '';
    try { body = view(); } catch (e) { console.error(e); body = `<div class="wrap"><div class="panel">화면을 그리다 오류가 났어요: ${esc(e.message)}</div></div>`; }
    root.innerHTML = nav + body + (app.modal ? modalView() : '') + (app.toast ? `<div class="toast" role="status">${esc(app.toast)}</div>` : '');
  }
  function isRunScreen() { return ['map', 'battle', 'reward', 'rest', 'forge', 'shop', 'event', 'archive', 'save', 'result'].includes(app.screen); }
  function view() {
    switch (app.screen) {
      case 'home': return viewHome();
      case 'party': return viewParty();
      case 'gacha': return viewGacha();
      case 'codex': return viewCodex();
      case 'memory': return viewMemory();
      case 'settings': return viewSettings();
      default: return viewRun();
    }
  }
  function syncRunScreen() {
    const s = app.run.s;
    app.screen = { MAP: 'map', BATTLE: 'battle', REWARD: 'reward', REST: 'rest', FORGE: 'forge', SHOP: 'shop', EVENT: 'event', ARCHIVE: 'archive', VICTORY: 'save', DEFEAT: 'save', DONE: 'result' }[s.phase] || 'map';
  }

  // ───────── home ─────────
  function viewHome() {
    const dial = `<svg class="dial" viewBox="0 0 200 224" aria-hidden="true">
      <circle cx="100" cy="100" r="92" fill="none" stroke="#2f3d63" stroke-width="2"/>
      <circle cx="100" cy="100" r="74" fill="none" stroke="#62d6c6" stroke-width="1.5" stroke-dasharray="3 7"/>
      ${Array.from({ length: 12 }, (_, i) => { const a = i * Math.PI / 6; return `<line x1="${100 + 82 * Math.sin(a)}" y1="${100 - 82 * Math.cos(a)}" x2="${100 + 92 * Math.sin(a)}" y2="${100 - 92 * Math.cos(a)}" stroke="#e2b45f" stroke-width="${i % 3 === 0 ? 3 : 1.5}"/>`; }).join('')}
      <line x1="100" y1="100" x2="100" y2="38" stroke="#e2b45f" stroke-width="4" stroke-linecap="round"/>
      <line x1="100" y1="100" x2="146" y2="118" stroke="#62d6c6" stroke-width="3" stroke-linecap="round"/>
      <circle cx="100" cy="100" r="6" fill="#e2b45f"/>
      <text x="100" y="218" text-anchor="middle" fill="#98a5c0" font-size="11" font-family="IBM Plex Sans KR, sans-serif">다음 턴 · 다다음 턴</text></svg>`;
    return `<div class="wrap">
      <section class="hero">
        <div>
          <div class="eyebrow">P0 플레이 테스트 · v0.7</div>
          <h1>기억 공방<small>MEMORY FORGE</small></h1>
          <p>카드를 지금 쓰거나, 비용을 먼저 내고 다음 턴·다다음 턴에 터뜨리세요. 적의 균열 게이지를 깨서 행동을 끊고, 탐사에서 키운 덱을 기억으로 저장해 다음 탐사에 가져갑니다.</p>
          <div class="row"><button class="btn primary" data-act="nav" data-to="party">탐사 시작</button><button class="btn" data-act="nav" data-to="gacha">모집 시뮬레이터</button><button class="btn" data-act="help">규칙 1분 요약</button></div>
          ${app.run && app.run.s.phase !== 'DONE' ? `<p class="muted" style="margin-top:10px">진행 중인 탐사가 있어요. <button class="btn small time" data-act="resume">이어 하기</button></p>` : ''}
        </div>
        ${dial}
      </section>
      <div class="feature-list">
        <div class="panel"><h3>시간 예약</h3><p>비용은 지금, 효과는 다음 턴 시작에. 예약하면 수치가 커지고, 다다음 턴 칸도 있어요.</p></div>
        <div class="panel"><h3>균열</h3><p>약점 속성으로 균열 게이지를 0으로 만들면 적의 다음 행동이 취소되고 받는 피해가 25% 늘어요.</p></div>
        <div class="panel"><h3>기억 저장</h3><p>탐사를 마치면 바뀐 덱을 캐릭터별 기억 용량 안에서 저장해요. 패배하면 1개만 남아요.</p></div>
        <div class="panel"><h3>모집</h3><p>반천장 80·풀천장 160, 무기 75%·풀천장 140. 중복은 공명(1~6)으로 성능이 올라 탐사에 반영돼요.</p></div>
      </div>
    </div>`;
  }

  // ───────── party select ─────────
  function viewParty() {
    if (app.run && app.run.s.phase !== 'DONE' && !app.partyForce) { syncRunScreen(); return view(); }
    const p = app.party;
    const chars = DATA.characters;
    const chosen = p.chars.filter((c) => usable(c));
    const cards = chars.map((ch) => {
      const i = chosen.indexOf(ch.id);
      const res = resOf(ch.id);
      const lock = !usable(ch.id);
      return `<button class="char attr-${ch.attr} ${i >= 0 ? 'on' : ''}" data-act="pickChar" data-id="${ch.id}" ${lock ? 'disabled' : ''}>
        ${i >= 0 ? `<span class="order">${i + 1}</span>` : ''}
        <span class="row" style="gap:8px">${emblem(ch, 44)}<span><span class="nm">${esc(ch.name)}</span><br><span class="role">${esc(ch.role)}</span></span></span>
        <span class="tags"><span class="pill rar-${ch.rarity}" style="color:var(--rar)">${ch.rarity}</span><span class="pill">${attrIcon(ch.attr)} ${ATTR[ch.attr].name}</span><span class="pill">${WTYPE[ch.weapon]}</span>${res ? `<span class="pill brass">공명 ${res}</span>` : ''}${ch.pool === 'limited' ? '<span class="pill warn">한정</span>' : ''}</span>
        <span class="role">${esc(ch.archetype)} · ${ch.tier === 'teen' ? '10대' : '성인'}</span>
      </button>`;
    }).join('');
    const weaponRows = chosen.map((cid) => {
      const ch = charOf(cid); const list = weaponsFor(cid); const cur = p.weapons[cid] && list.some((w) => w.id === p.weapons[cid]) ? p.weapons[cid] : list.find((w) => w.rarity === 'R').id;
      const mem = app.memory[cid];
      return `<div class="item"><h4>${esc(ch.name)} <span class="muted">· ${WTYPE[ch.weapon]}</span></h4>
        <label class="muted" for="w-${cid}">무기</label>
        <select id="w-${cid}" data-act="weapon" data-id="${cid}">${list.map((w) => `<option value="${w.id}" ${w.id === cur ? 'selected' : ''}>${esc(w.name)} (${w.rarity}${w.rarity !== 'R' ? ` R${MF.Gacha.refine(app.gacha, w.id)}` : ''})</option>`).join('')}</select>
        <p>${mem ? `저장된 기억 덱 사용 가능(${mem.length}장)` : '기본 기억 덱(시작 카드 4장 + 공용 1장)'}</p></div>`;
    }).join('');
    return `<div class="wrap grid">
      <div class="row" style="justify-content:space-between"><div><div class="eyebrow">편성</div><h2>캐릭터 3명을 고르세요</h2></div>
        <div class="row"><span class="muted">${app.settings.allOwned ? '테스트 모드: 전원 사용 가능' : '보유 캐릭터만'}</span></div></div>
      <div class="char-grid">${cards}</div>
      ${chosen.length ? `<div class="setup-row">${weaponRows}</div>` : ''}
      <div class="panel grid">
        <div class="row"><span class="muted">탐사 길이</span><span class="seg"><button data-act="len" data-v="short" class="${p.length === 'short' ? 'on' : ''}">짧게(1구역)</button><button data-act="len" data-v="full" class="${p.length === 'full' ? 'on' : ''}">정규(2구역)</button></span>
          <span class="muted">난이도</span><span class="seg"><button data-act="diff" data-v="normal" class="${p.difficulty === 'normal' ? 'on' : ''}">보통</button><button data-act="diff" data-v="hard" class="${p.difficulty === 'hard' ? 'on' : ''}">어려움</button></span>
          <span class="muted">덱</span><span class="seg"><button data-act="usemem" data-v="1" class="${p.useMemory ? 'on' : ''}">저장된 기억</button><button data-act="usemem" data-v="0" class="${!p.useMemory ? 'on' : ''}">기본 덱</button></span></div>
        <div class="row"><button class="btn primary" data-act="startRun" ${chosen.length === 3 ? '' : 'disabled'}>탐사 시작</button><span class="muted">${chosen.length}/3 선택 · 짧게는 약 15분, 정규는 약 30분</span></div>
      </div></div>`;
  }

  // ───────── run screens ─────────
  function runBar() {
    const s = app.run.s;
    return `<div class="panel runbar">
      <div class="hpbar"><div class="lbl"><span>파티 체력</span><span class="num">${s.hp} / ${s.maxHp}</span></div><div class="track"><div class="fill" style="width:${Math.round(100 * s.hp / s.maxHp)}%"></div></div></div>
      <span class="pill brass">동전 <b class="num">${s.coins}</b></span>
      <span class="pill">구역 ${s.zone} · ${s.floor + 1}층</span>
      <button class="btn small" data-act="deck">덱 ${s.deck.length}장</button>
      ${s.partsFound.length ? `<button class="btn small time" data-act="parts">부품 ${s.partsFound.length}개</button>` : ''}
      <div class="relic-row">${s.relics.map((r) => `<span class="relic" title="${esc(IX.relics[r].text)}">${esc(IX.relics[r].name)}</span>`).join('')}</div>
    </div>`;
  }
  function viewRun() {
    if (!app.run) { app.screen = 'party'; return viewParty(); }
    syncRunScreen();
    switch (app.screen) {
      case 'map': return `<div class="wrap grid">${runBar()}${viewMap()}</div>`;
      case 'battle': return `<div class="wrap">${viewBattle()}</div>`;
      case 'reward': return `<div class="wrap grid">${runBar()}${viewReward()}</div>`;
      case 'rest': return `<div class="wrap grid">${runBar()}${viewRest()}</div>`;
      case 'forge': return `<div class="wrap grid">${runBar()}${viewForge()}</div>`;
      case 'shop': return `<div class="wrap grid">${runBar()}${viewShop()}</div>`;
      case 'event': return `<div class="wrap grid">${runBar()}${viewEvent()}</div>`;
      case 'archive': return `<div class="wrap grid">${runBar()}${viewArchive()}</div>`;
      case 'save': return `<div class="wrap grid">${viewSave()}</div>`;
      case 'result': return `<div class="wrap grid">${viewResult()}</div>`;
      default: return '';
    }
  }

  const NODE_LABEL = { battle: '전투', elite: '엘리트', rest: '휴식', forge: '공방', shop: '상점', event: '사건', boss: '보스' };
  function viewMap() {
    const run = app.run; const m = run.s.map; const avail = new Set(run.available());
    const W = 320; const rowH = 62; const floors = m.floors.length; const H = (floors + 1) * rowH + 30;
    const pos = {};
    m.floors.forEach((row, f) => row.forEach((n, i) => { pos[n.id] = { x: (W / (row.length + 1)) * (i + 1), y: H - 30 - f * rowH }; }));
    pos[m.boss.id] = { x: W / 2, y: H - 30 - floors * rowH };
    const visited = new Set(run.s.visited || []);
    let edges = '';
    for (const row of m.floors) for (const n of row) for (const nx of n.next) {
      const a = pos[n.id]; const b = pos[nx];
      edges += `<line class="edge ${visited.has(n.id) && (visited.has(nx) || run.s.pos === nx) ? 'walked' : ''}" x1="${a.x}" y1="${a.y}" x2="${b.x}" y2="${b.y}"/>`;
    }
    const nodeSvg = (n) => {
      const p = pos[n.id]; const cls = ['node', avail.has(n.id) ? 'avail' : '', run.s.pos === n.id ? 'cur' : '', visited.has(n.id) ? 'past' : ''].join(' ');
      const r = n.type === 'boss' ? 22 : 17;
      return `<g class="${cls}" ${avail.has(n.id) ? `data-act="node" data-id="${n.id}" tabindex="0" role="button" aria-label="${NODE_LABEL[n.type]}"` : ''}>
        <circle cx="${p.x}" cy="${p.y}" r="${r}"/><text x="${p.x}" y="${p.y + 4}">${NODE_LABEL[n.type]}</text></g>`;
    };
    let nodes = '';
    for (const row of m.floors) for (const n of row) nodes += nodeSvg(n);
    nodes += nodeSvg(m.boss);
    return `<div class="panel map"><div class="row" style="justify-content:space-between"><h2>${run.s.zone === 1 ? '멈춘 시계탑 거리' : '잠긴 기록소'}</h2><span class="muted">밝게 빛나는 칸을 눌러 이동</span></div>
      <div class="map-wrap"><svg viewBox="0 0 ${W} ${H}" role="img" aria-label="탐사 지도">${edges}${nodes}</svg></div>
      <div class="legend"><span>엘리트: 유물 보상</span><span>휴식: 회복 또는 강화</span><span>공방: 카드 변주 또는 제거</span><span>상점: 카드·부품·유물</span></div></div>`;
  }

  // ───────── battle ─────────
  const INTENT_TEXT = (mv, e) => {
    if (!mv) return { cls: 'other', ico: ICON.eye, t: '-' };
    const dm = (d) => { const s = app.run ? app.run.battle.s : null; const mul = s ? s.diff.dmg : 100; return Math.round(d * mul / 100) + ((e && e.st.str) || 0); };
    switch (mv.t) {
      case 'atk': return { cls: 'atk', ico: ICON.sword, t: `${mv.label ? mv.label + ' ' : ''}${dm(mv.d)}${mv.h > 1 ? `×${mv.h}` : ''}` };
      case 'atkdebuff': return { cls: 'atk', ico: ICON.sword, t: `${mv.label ? mv.label + ' ' : ''}${dm(mv.d)}${mv.h > 1 ? `×${mv.h}` : ''} + 약화` };
      case 'def': return { cls: 'def', ico: ICON.shield, t: `방어 ${mv.b}${mv.str ? ` · 힘 +${mv.str}` : ''}` };
      case 'buff': return { cls: 'other', ico: ICON.up, t: `힘 +${mv.str}` };
      case 'debuff': return { cls: 'other', ico: ICON.down, t: `파티 약화 ${mv.n}` };
      case 'charge': return { cls: 'other', ico: ICON.hourglass, t: mv.label || '힘을 모은다' };
      case 'summon': return { cls: 'other', ico: ICON.plus, t: mv.label || '소환' };
      case 'heal': return { cls: 'def', ico: ICON.plus, t: `아군 회복 ${mv.n}` };
      case 'insert': return { cls: 'other', ico: ICON.ink, t: `얼룩 ${mv.n}장 섞기` };
      case 'reflect': return { cls: 'other', ico: ICON.mirror, t: `반사 ${mv.n}` };
      case 'expose': return { cls: 'atk', ico: ICON.eye, t: `${mv.label || '약점 노출'} → ${dm(mv.then.d)}` };
      case 'blockAll': return { cls: 'def', ico: ICON.shield, t: `전원 방어 ${mv.b}` };
      case 'stun': return { cls: 'stun', ico: ICON.zz, t: '균열: 행동 불가' };
      default: return { cls: 'other', ico: ICON.eye, t: mv.t };
    }
  };
  function enemyCard(b, e, targetMode) {
    const it = INTENT_TEXT(e.intent, e); const nx = e.next && !(e.intent && e.intent.t === 'stun') ? INTENT_TEXT(e.next, e) : (e.next ? INTENT_TEXT(e.next, e) : null);
    const targetable = targetMode && e.alive && (targetMode !== 'fixed' || e.st.mark > 0);
    const st = [];
    if (e.block) st.push(`<span class="pill">${ICON.shield} 방어 ${e.block}</span>`);
    if (e.brokenUntil) st.push('<span class="pill brass">균열 +25%</span>');
    for (const [k, nm] of [['vul', '취약'], ['weak', '약화'], ['mark', '표식'], ['burn', '화상'], ['str', '힘'], ['reflect', '반사']]) if (e.st[k]) st.push(`<span class="pill ${k === 'mark' ? 'time' : ''}">${nm} ${e.st[k]}</span>`);
    const pop = app.lastDmg[e.uid] ? `<span class="dmg-pop">-${app.lastDmg[e.uid]}</span>` : '';
    return `<button class="enemy ${targetable ? 'targetable' : ''} ${app.target === e.uid ? 'target' : ''} ${e.alive ? '' : 'dead'} ${e.brokenUntil ? 'broken' : ''}" data-act="enemy" data-uid="${e.uid}" ${e.alive ? '' : 'disabled'}>
      ${pop}<span class="nm"><span>${esc(e.name)}</span><span class="num">${e.hp}/${e.maxHp}</span></span>
      <span class="hp"><i style="width:${Math.round(100 * e.hp / e.maxHp)}%"></i></span>
      <span class="brk" title="균열 게이지">${Array.from({ length: Math.min(e.brkMax, 14) }, (_, i) => `<i class="${i < Math.round(e.brk * Math.min(e.brkMax, 14) / e.brkMax) ? 'on' : ''}"></i>`).join('')}</span>
      <span class="weak-icons">약점 ${e.weak.map((a) => `<span class="attr-${a}">${attrIcon(a)}</span>`).join('')} <span class="num">균열 ${e.brk}/${e.brkMax}</span></span>
      ${e.alive ? `<span class="intent ${it.cls}">${it.ico} ${esc(it.t)}</span>${nx ? `<span class="intent-next">다음: ${esc(nx.t)}</span>` : ''}` : '<span class="muted">처치</span>'}
      <span class="status-chips">${st.join('')}</span>
    </button>`;
  }
  function slotsView(b, kind) {
    const s = b.s; const due = s.turn + (kind === 'next' ? 1 : 2);
    const list = s.res.filter((r) => r.due === due).sort((a, c) => a.order - c.order);
    const capMax = kind === 'next' ? IX.rules.slots.nextMax : IX.rules.slots.afterMax;
    const cap = b.slotCapacity(kind);
    const sealed = kind === 'next' && s.sealedUntil >= s.turn ? 1 : 0;
    const cells = [];
    for (let i = 0; i < 3; i++) {
      if (i < list.length) { const inst = s.cards[list[i].uid]; const def = IX.cards[inst.id]; cells.push(`<button class="slot filled attr-${attrOfDef(def, inst)}" data-act="resInfo" data-uid="${inst.uid}">${esc(def.name)}${inst.plus ? '+' : ''}</button>`); }
      else if (i < cap) cells.push('<span class="slot">빈 칸</span>');
      else if (i < cap + sealed) cells.push('<span class="slot sealed">봉인</span>');
      else if (i < capMax) cells.push('<span class="slot none"></span>');
      else cells.push('<span class="slot none"></span>');
    }
    return `<div class="tl-col"><h4><span>${kind === 'next' ? '다음 턴' : '다다음 턴'}</span><span class="num">${list.length}/${cap}</span></h4><div class="slots">${cells.join('')}</div></div>`;
  }
  function viewBattle() {
    const b = app.run.battle; const s = b.s;
    const selCard = app.sel && app.sel.uid && s.zones.hand.includes(app.sel.uid) ? app.sel.uid : null;
    const ultSel = app.sel && app.sel.ult ? app.sel.ult : null;
    const need = selCard ? b.needsTarget(selCard) : (ultSel ? 'one' : null);
    const debt = b.debtTotal();
    const dueNext = s.debts.filter((d) => d.due === s.turn + 1).reduce((a, d) => a + d.n, 0);
    const pips = Array.from({ length: Math.max(4, s.ap) }, (_, i) => `<span class="pip ${i < s.ap ? 'on' : ''}"></span>`).join('');
    const buffs = s.buffs.filter((x) => x.until >= s.turn).map((x) => `<span class="pill time">${esc(({ charge: '충전', seraLock: '미래 확정', healUp: '기도문', lumiRush: '한계 돌파', counterDouble: '반격 2배', overloadFree: '과부하 무료' })[x.s] || x.s)}</span>`).join('');
    const chips = [
      s.block ? `<span class="pill">${ICON.shield} 방어 <b class="num">${s.block}</b></span>` : '',
      s.counter ? `<span class="pill">반격 ${s.counter}</span>` : '',
      debt ? `<span class="pill warn">부채 ${debt} · 다음 턴 행동력 ${Math.max(0, 4 + s.apNext - dueNext)}</span>` : (s.apNext ? `<span class="pill time">다음 턴 행동력 +${s.apNext}</span>` : ''),
      s.echoActive ? `<span class="pill brass">잔향 ${s.echo}/${s.echoMax}</span>` : '',
      s.pst.weak ? `<span class="pill warn">파티 약화 ${s.pst.weak}</span>` : '',
      s.restraint.active ? '<span class="pill brass">절제 계약 중</span>' : '',
      s.bonus.avail && s.bonus.turn === s.turn + 1 ? '<span class="pill time">다음 턴 첫 예약 +30%</span>' : '',
      s.freeNext ? `<span class="pill time">다음 ${s.freeNext}장 비용 0</span>` : '',
      buffs,
    ].join('');
    const party = s.actors.map((a) => {
      const ch = charOf(a.id);
      return `<div class="actor">${emblem(ch, 34)}<div style="min-width:0"><b>${esc(ch.name)}</b>${a.res ? ` <span class="muted">공명 ${a.res}</span>` : ''}<div class="gauge"><i style="width:${a.gauge}%"></i></div></div>
        <button class="btn ult ${a.gauge >= 100 ? 'ready' : ''} ${ultSel === a.id ? 'sel' : ''}" data-act="ult" data-actor="${a.id}" ${a.gauge >= 100 && s.phase === 'PLAYER' ? '' : 'disabled'} title="${esc(ch.ult.text)}">${esc(ch.ult.name)}</button></div>`;
    }).join('');
    const enemies = s.enemies.filter((e) => e.alive || app.lastDmg[e.uid]).map((e) => enemyCard(b, e, need)).join('');
    const hand = s.zones.hand.map((uid) => {
      const inst = s.cards[uid]; const def = b.effDef(inst); const cost = b.costOf(uid, 'now');
      const can = s.phase === 'PLAYER' && b.allowedModes(uid).some((m) => b.check(uid, m, 'auto') === null);
      return cardFace(inst, { def, cost, act: 'card', cls: `${selCard === uid ? 'sel' : ''} ${can ? '' : 'cant'}` });
    }).join('') || '<span class="muted">손패가 없어요.</span>';
    let action = '';
    if (selCard) {
      const inst = s.cards[selCard]; const def = b.effDef(inst);
      const modes = b.allowedModes(selCard);
      const tgtReady = !need || app.target || b.alive().length === 1;
      const labels = { now: '지금 사용', next: '다음 턴 예약', after: '다다음 턴 예약' };
      const btns = ['now', 'next', 'after'].map((m) => {
        if (!modes.includes(m)) return '';
        const cost = b.costOf(selCard, m);
        const code = tgtReady ? b.check(selCard, m, app.target) : 'BAD_TARGET';
        const note = code ? (code === 'BAD_TARGET' ? '대상을 고르세요' : errMsg({ code })) : preview(selCard, m);
        return `<button class="btn ${m === 'now' ? 'primary' : 'time'}" data-act="mode" data-mode="${m}" ${code ? 'disabled' : ''}>${labels[m]} · 비용 ${cost}<small>${esc(note)}</small></button>`;
      }).join('');
      action = `<div class="action"><div class="desc"><b>${esc(def.name)}${inst.plus ? '+' : ''}</b> <span class="muted">(${esc(ownerLabel(def, inst))})</span> — ${esc(MF.cardText(def, IX))}${partNames(inst).length ? ` <span class="pill time">${esc(partNames(inst).join(', '))}</span>` : ''}</div>
        ${need && !tgtReady ? `<div class="pill time">${need === 'fixed' ? '표식이 있는 적' : '적'}을 눌러 대상을 고르세요</div>` : ''}
        <div class="modes">${btns}<button class="btn" data-act="cancel">취소</button></div></div>`;
    } else if (ultSel) {
      action = `<div class="action"><div class="desc"><b>${esc(charOf(ultSel).ult.name)}</b> — ${esc(charOf(ultSel).ult.text)}</div><div class="pill time">적을 눌러 대상을 고르세요</div><div class="modes"><button class="btn" data-act="cancel">취소</button></div></div>`;
    } else {
      action = `<div class="action idle"><div class="desc muted">카드를 누르면 지금 사용/예약을 고를 수 있어요. 예약은 비용을 지금 내고 다음 턴 시작에 해결돼요.</div></div>`;
    }
    const logLines = s.log.slice(-40).map(logLine).filter(Boolean).reverse().map((t) => `<div>${esc(t)}</div>`).join('');
    return `<div class="battle">
      <div class="panel bt-top">
        <div class="grid" style="gap:6px">
          <div class="hpbar"><div class="lbl"><span>파티 체력 · ${s.turn}턴</span><span class="num">${s.hp} / ${s.maxHp}</span></div><div class="track"><div class="fill" style="width:${Math.round(100 * s.hp / s.maxHp)}%"></div></div></div>
          <div class="status-chips">${chips}</div>
        </div>
        <div class="grid side" style="gap:6px;justify-items:end">
          <div class="ap" aria-label="행동력 ${s.ap}">${pips}<span class="val">${s.ap}</span></div>
          <div class="row"><button class="btn small" data-act="pile" data-z="draw">뽑을 ${s.zones.draw.length}</button><button class="btn small" data-act="pile" data-z="discard">버림 ${s.zones.discard.length}</button><button class="btn small" data-act="pile" data-z="exhaust">소멸 ${s.zones.exhaust.length}</button></div>
        </div>
      </div>
      <div class="field"><div class="party">${party}</div><div class="enemies">${enemies}</div></div>
      <div class="timeline">${slotsView(b, 'next')}${slotsView(b, 'after')}</div>
      ${app.logOpen ? `<div class="log">${logLines}</div>` : ''}
      <div class="dock">
        ${action}
        <div class="hand" role="list" aria-label="손패">${hand}</div>
        <div class="row dock-row"><button class="btn small" data-act="toggleLog">${app.logOpen ? '기록 닫기' : '전투 기록'}</button><span class="muted dock-ap">행동력 <b class="num">${s.ap}</b></span><button class="btn primary" data-act="endTurn" ${s.phase === 'PLAYER' ? '' : 'disabled'}>턴 종료</button></div>
      </div>
    </div>`;
  }
  function nameOfUid(uid) { const b = app.run && app.run.battle; if (!b) return ''; const c = b.s.cards[uid]; if (c) return IX.cards[c.id].name; const e = b.s.enemies.find((x) => x.uid === uid); return e ? e.name : ''; }
  function logLine(l) {
    switch (l.e) {
      case 'turn': return `— ${l.turn}턴 시작 (행동력 ${l.ap})`;
      case 'play': return `${nameOfUid(l.uid)} ${l.mode === 'now' ? '사용' : l.mode === 'next' ? '다음 턴 예약' : '다다음 턴 예약'} (비용 ${l.cost})`;
      case 'dmg': return `${nameOfUid(l.uid)}에게 ${l.n} 피해${l.blocked ? `(방어 ${l.blocked})` : ''}${l.src === 'burn' ? ' [화상]' : l.src === 'counter' ? ' [반격]' : ''}`;
      case 'hit': return `${nameOfUid(l.uid)}의 공격 ${l.d}${l.blocked ? ` (방어로 ${l.blocked} 막음)` : ''}`;
      case 'break': return `★ ${nameOfUid(l.uid)} 균열! 다음 행동 취소, 받는 피해 +25%, 다음 턴 행동력 +1`;
      case 'kill': return `${nameOfUid(l.uid)} 처치`;
      case 'fizzle': return `${nameOfUid(l.uid)} 불발(대상 없음/표식 없음)`;
      case 'grow': return `${nameOfUid(l.uid)} 성장 ${l.growth}/3`;
      case 'loan': return `행동력 +${l.n} 차입`;
      case 'repay': return `부채 ${l.n} 상환`;
      case 'restraintBroken': return '절제 계약 위약: 다음 턴 드로우 -1';
      case 'restraintKept': return '절제 계약 성립: 다음 턴 첫 예약 +30%';
      case 'stunSkip': return `${nameOfUid(l.uid)}은(는) 균열로 행동하지 못했다`;
      case 'ult': return `필살기: ${charOf(l.actor).ult.name}`;
      case 'reshuffle': return '버린 카드를 섞어 뽑을 더미로';
      case 'envelope': return '기억의 봉투가 쓰러짐을 막았다';
      case 'insert': return `얼룩 ${l.n}장이 버린 더미에 섞였다`;
      case 'end': return l.phase === 'VICTORY' ? '승리!' : '패배...';
      default: return '';
    }
  }
  // preview: dry-run on a cloned state so every modifier is included
  function preview(uid, mode) {
    const b = app.run.battle; const s = b.s;
    try {
      const c = new MF.Battle(IX, JSON.parse(JSON.stringify(s)));
      const target = app.target || (c.alive()[0] ? c.alive()[0].uid : null);
      const before = { hp: c.s.hp, block: c.s.block, e: Object.fromEntries(c.s.enemies.map((e) => [e.uid, e.hp + e.block])) };
      if (mode === 'now') c.play(uid, 'now', target, []);
      else { c.s.zones.hand.splice(c.s.zones.hand.indexOf(uid), 1); c.s.phase = 'RESOLVING'; c.resolveCard(uid, { reserved: mode, target, picks: [] }); }
      const parts = [];
      let dmg = 0; for (const e of c.s.enemies) { const was = before.e[e.uid]; if (was !== undefined) dmg += Math.max(0, was - (e.hp + e.block)); }
      if (dmg) parts.push(`피해 ${dmg}`);
      if (c.s.block - before.block > 0) parts.push(`방어 +${c.s.block - before.block}`);
      if (c.s.hp - before.hp > 0) parts.push(`회복 ${c.s.hp - before.hp}`);
      if (c.s.hp - before.hp < 0) parts.push(`체력 ${c.s.hp - before.hp}`);
      const brokeN = c.s.enemies.filter((e) => e.brokenUntil && !s.enemies.find((x) => x.uid === e.uid).brokenUntil).length;
      if (brokeN) parts.push('균열!');
      return (parts.join(' · ') || '효과 적용') + (mode !== 'now' ? ' (현재 상태 기준 예상)' : '');
    } catch (e) { return ERR[e.code] || '사용 불가'; }
  }
  // choice requests for immediate plays
  function choiceRequests(uid, mode) {
    if (mode !== 'now') return [];
    const b = app.run.battle; const s = b.s; const def = b.effDef(s.cards[uid]); const reqs = [];
    for (const op of def.eff) {
      if (op.op === 'exhaustPick') reqs.push({ label: op.from === 'discard' ? '버린 카드에서 소멸할 카드' : '손에서 소멸할 카드', from: op.from === 'discard' ? 'discard' : 'hand', n: op.n });
      if (op.op === 'discard') reqs.push({ label: '버릴 카드', from: 'hand', n: op.n });
      if (op.op === 'tune' && op.pick === 'one') reqs.push({ label: `「${IX.parts[op.part].name}」을 붙일 카드`, from: 'hand', n: 1 });
      if (op.op === 'costdown' && op.pick === 'one') reqs.push({ label: '비용을 낮출 카드', from: 'hand', n: 1 });
      if (op.op === 'recall') reqs.push({ label: '손으로 가져올 카드', from: 'discard', n: 1 });
      if (op.op === 'growPick') reqs.push({ label: '성장시킬 카드', from: 'hand', n: 1, filter: 'growth' });
      if (op.op === 'copyPick') reqs.push({ label: '사본을 만들 성장 카드', from: 'hand', n: 1, filter: 'growth' });
      if (op.op === 'pull') reqs.push({ label: '앞당길 예약', from: 'res', n: 1 });
    }
    return reqs;
  }
  function pickerOptions(req, uid) {
    const b = app.run.battle; const s = b.s;
    let list = req.from === 'hand' ? s.zones.hand.filter((u) => u !== uid) : req.from === 'discard' ? [...s.zones.discard] : s.res.map((r) => r.uid);
    if (req.filter === 'growth') list = list.filter((u) => IX.cards[s.cards[u].id].growth);
    list = list.filter((u) => !(app.picker.picks || []).includes(u));
    return list;
  }
  function sendPlay(uid, mode, picks) {
    const b = app.run.battle;
    const before = Object.fromEntries(b.s.enemies.map((e) => [e.uid, e.hp]));
    try {
      app.run.battleCmd('play', { uid, mode, target: app.target, picks: picks || [] });
      afterBattleCmd(before);
    } catch (e) { toast(errMsg(e)); }
    app.sel = null; app.target = null; app.picker = null; if (app.modal && app.modal.type === 'picker') app.modal = null;
    render();
  }
  function afterBattleCmd(before) {
    app.lastDmg = {};
    const b = app.run.battle;
    if (b && before) for (const e of b.s.enemies) if (before[e.uid] !== undefined && before[e.uid] > e.hp) app.lastDmg[e.uid] = before[e.uid] - e.hp;
    if (!app.run.battle) { syncRunScreen(); }
  }

  // ───────── reward / rest / forge / shop / event / archive ─────────
  function viewReward() {
    const s = app.run.s; const p = s.pending;
    const evo = (p.evolutions || []).map((ev) => {
      const d = s.deck.find((c) => c.key === ev.key);
      return `<div class="panel grid"><div class="eyebrow">진화 가능</div><h3>${esc(IX.cards[d.id].name)} — 성장 3 달성</h3>
        <div class="choice-grid">${ev.options.map((o) => cardFace({ id: o, key: ev.key }, { act: 'evolve', cls: '' }).replace('data-act="evolve"', `data-act="evolve" data-branch="${o}"`)).join('')}</div>
        <p class="muted">진화는 이번 탐사 동안 유지되고, 기억 저장 때 용량 2를 써서 남길 수 있어요.</p></div>`;
    }).join('');
    return `<div class="grid">
      <div class="panel"><div class="eyebrow">전투 보상</div><h2>동전 +${p.coins}</h2></div>
      ${evo}
      ${p.cards && p.cards.length ? `<div class="panel grid"><h3>카드 1장을 덱에 추가</h3><div class="choice-grid">${p.cards.map((id) => cardFace({ id }, { act: 'takeCard' })).join('')}</div><div class="row"><button class="btn" data-act="skipCard">받지 않기 (동전 +15)</button></div></div>` : ''}
      ${p.relic && p.relic.length ? `<div class="panel grid"><h3>유물 선택</h3><div class="choice-grid">${p.relic.map((r) => `<button class="item" data-act="takeRelic" data-id="${r}"><h4>${esc(IX.relics[r].name)}</h4><p>${esc(IX.relics[r].text)}</p></button>`).join('')}</div></div>` : ''}
      ${p.part ? `<div class="panel row"><span>부품 발견: <b>${esc(IX.parts[p.part].name)}</b> — ${esc(IX.parts[p.part].text)}</span><button class="btn time" data-act="takePart">가져가기</button></div>` : ''}
      <div class="row"><button class="btn primary" data-act="proceed">지도로</button></div></div>`;
  }
  function viewRest() {
    const s = app.run.s; const p = s.pending;
    return `<div class="panel grid"><div class="eyebrow">휴식</div><h2>잠깐 쉬어 간다</h2><p class="muted">남은 이용: ${p.uses}회</p>
      <div class="row"><button class="btn primary" data-act="restHeal" ${p.uses > 0 ? '' : 'disabled'}>회복 (최대 체력의 ${30 + (s.relics.includes('old_compass') ? 10 : 0)}%)</button>
      <button class="btn" data-act="restUpgrade" ${p.uses > 0 ? '' : 'disabled'}>카드 강화(+)</button><button class="btn" data-act="proceed">떠나기</button></div></div>`;
  }
  function viewForge() {
    const s = app.run.s; const p = s.pending;
    if (p.offers) {
      const d = s.deck.find((c) => c.key === p.offers.key);
      return `<div class="panel grid"><div class="eyebrow">카드 변주</div><h2>${esc(IX.cards[d.id].name)}에 붙일 부품</h2>
        <div class="choice-grid">${p.offers.parts.map((id) => `<button class="item" data-act="forgeApply" data-id="${id}"><h4>${esc(IX.parts[id].name)} <span class="pill">${IX.parts[id].slot === 'circuit' ? '시간 회로' : '보조 각인'}</span></h4><p>${esc(IX.parts[id].text)}</p></button>`).join('')}</div></div>`;
    }
    return `<div class="panel grid"><div class="eyebrow">공방</div><h2>카드를 다듬는다</h2><p class="muted">남은 이용: ${p.uses}회. 변주는 카드 1장에 무작위 부품 3개 중 하나를 붙여요(회로·각인 각 1칸).</p>
      <div class="row"><button class="btn primary" data-act="forgeVar" ${p.uses > 0 ? '' : 'disabled'}>카드 변주</button><button class="btn" data-act="forgeRemove" ${p.uses > 0 ? '' : 'disabled'}>카드 제거</button><button class="btn" data-act="proceed">떠나기</button></div></div>`;
  }
  function viewShop() {
    const s = app.run.s; const p = s.pending;
    const it = (kind, x, title, text, inner) => `<div class="item"><h4>${title}</h4>${inner || `<p>${esc(text)}</p>`}<button class="btn small ${x.sold ? '' : 'primary'}" data-act="buy" data-kind="${kind}" data-id="${x.id}" ${x.sold || s.coins < x.price ? 'disabled' : ''}>${x.sold ? '구매함' : `동전 ${x.price}`}</button></div>`;
    return `<div class="panel grid"><div class="eyebrow">상점</div><h2>떠돌이 기억 상인</h2>
      <h3>카드</h3><div class="choice-grid">${p.cards.map((x) => it('cards', x, esc(IX.cards[x.id].name), '', cardFace({ id: x.id }, {}))).join('')}</div>
      <h3>부품</h3><div class="choice-grid">${p.parts.map((x) => it('parts', x, esc(IX.parts[x.id].name), IX.parts[x.id].text)).join('')}</div>
      <h3>유물</h3><div class="choice-grid">${p.relics.map((x) => it('relics', x, esc(IX.relics[x.id].name), IX.relics[x.id].text)).join('')}</div>
      <div class="row">${p.removeCost ? `<button class="btn" data-act="shopRemove" ${s.coins >= p.removeCost ? '' : 'disabled'}>카드 제거 (동전 ${p.removeCost})</button>` : '<span class="muted">제거 서비스 이용 완료</span>'}<button class="btn primary" data-act="proceed">떠나기</button></div></div>`;
  }
  function viewEvent() {
    const s = app.run.s; const p = s.pending; const ev = IX.events[p.id];
    if (p.done) return `<div class="panel grid"><div class="eyebrow">사건</div><h2>${esc(ev.name)}</h2><p>${esc(ev.choices[p.choice].label)} — ${esc(ev.choices[p.choice].text)}</p><div class="row"><button class="btn primary" data-act="proceed">지도로</button></div></div>`;
    return `<div class="panel grid"><div class="eyebrow">사건</div><h2>${esc(ev.name)}</h2><p>${esc(ev.text)}</p>
      <div class="grid">${ev.choices.map((c, i) => `<button class="btn" style="justify-content:flex-start;text-align:left" data-act="evChoice" data-i="${i}" ${(c.need && c.need.coins && s.coins < c.need.coins) ? 'disabled' : ''}><b>${esc(c.label)}</b>&nbsp;<span class="muted">${esc(c.text)}</span></button>`).join('')}</div></div>`;
  }
  function viewArchive() {
    return `<div class="panel grid"><div class="eyebrow">기록소</div><h2>첫 구역의 기록을 확정했다</h2><p>여기서 돌아가면 지금까지의 덱을 기억으로 저장할 수 있어요. 계속 가면 체력 15%를 회복하고 다음 구역으로 갑니다(패배하면 변경 1개만 남아요).</p>
      <div class="row"><button class="btn primary" data-act="archive" data-v="continue">계속 탐사</button><button class="btn" data-act="archive" data-v="return">귀환하고 저장</button></div></div>`;
  }
  function viewSave() {
    const run = app.run; const s = run.s; const info = run.memoryChanges(); const defeat = s.pending && s.pending.defeat;
    if (!app.saveSel) {
      app.saveSel = {};
      for (const [cid, inf] of Object.entries(info)) { app.saveSel[cid] = []; let used = 0; if (!defeat) inf.changes.forEach((c, i) => { if (used + c.cost <= inf.capacity) { app.saveSel[cid].push(i); used += c.cost; } }); }
    }
    const total = Object.values(app.saveSel).reduce((a, l) => a + l.length, 0);
    const blocks = Object.entries(info).map(([cid, inf]) => {
      const ch = charOf(cid); const sel = new Set(app.saveSel[cid]);
      const used = [...sel].reduce((a, i) => a + inf.changes[i].cost, 0);
      return `<div class="panel grid"><div class="row" style="justify-content:space-between"><h3>${emblem(ch, 26)} ${esc(ch.name)}</h3><span class="pill ${used > inf.capacity ? 'warn' : 'brass'}">기억 용량 ${used}/${inf.capacity}</span></div>
        ${inf.changes.length ? inf.changes.map((c, i) => `<label class="row" style="justify-content:space-between;border-bottom:1px solid var(--line);padding:4px 0"><span><input type="checkbox" id="sv-${cid}-${i}" data-act="saveToggle" data-cid="${cid}" data-i="${i}" ${sel.has(i) ? 'checked' : ''}> ${esc(c.label)}</span><span class="num muted">용량 ${c.cost}</span></label>`).join('') : '<p class="muted">바뀐 점이 없어요.</p>'}
      </div>`;
    }).join('');
    const commons = s.deck.filter((c) => c.owner === 'common' && c.gained);
    const assign = commons.length ? `<div class="panel grid"><h3>공용 카드는 누구의 기억에 넣을까요?</h3>${commons.map((c) => `<div class="row"><span>${esc(IX.cards[c.id].name)}</span><select id="as-${c.key}" data-act="assign" data-key="${c.key}">${s.party.map((p) => `<option value="${p.charId}" ${c.assigned === p.charId ? 'selected' : ''}>${esc(charOf(p.charId).name)}</option>`).join('')}</select></div>`).join('')}</div>` : '';
    return `<div class="panel"><div class="eyebrow">${defeat ? '패배 · 잔존 기억' : '탐사 종료 · 기억 저장'}</div><h2>${defeat ? '하나만 남길 수 있어요' : '이번 탐사의 덱을 기억으로 남기세요'}</h2>
      <p class="muted">${defeat ? '패배하면 이번 탐사에서 바뀐 것 중 1개만 저장돼요.' : '캐릭터마다 기억 용량 안에서 저장해요. 저장한 덱은 다음 탐사의 시작 덱이 되고, 주간 보스·기록 균열에서도 써요.'} 선택: ${total}개</p></div>
      ${assign}${blocks}<div class="row"><button class="btn primary" data-act="commitSave">저장하고 끝내기</button><button class="btn" data-act="skipSave">저장하지 않기</button></div>`;
  }
  function viewResult() {
    const s = app.run.s; const r = s.result || {};
    return `<div class="panel grid"><div class="eyebrow">탐사 결과</div><h2>${r.outcome === 'defeat' ? '패배' : r.outcome === 'return' ? '기록소 귀환' : '탐사 완주'}</h2>
      <dl class="kv"><dt>전투</dt><dd>${s.stats.battles}회 (엘리트 ${s.stats.elites})</dd><dt>처치</dt><dd>${s.stats.kills}</dd><dt>남은 체력</dt><dd>${s.hp}/${s.maxHp}</dd><dt>유물</dt><dd>${s.relics.map((x) => esc(IX.relics[x].name)).join(', ') || '-'}</dd></dl>
      <div class="row"><button class="btn primary" data-act="newRun">다시 탐사</button><button class="btn" data-act="nav" data-to="memory">기억 덱 보기</button><button class="btn" data-act="nav" data-to="gacha">모집하러 가기</button></div></div>`;
  }

  // ───────── gacha ─────────
  function viewGacha() {
    const g = app.gacha; const cfg = DATA.gacha;
    const tabs = cfg.banners.map((bn) => `<button class="btn small ${app.gachaTab === bn.id ? 'primary' : ''}" data-act="gtab" data-id="${bn.id}">${esc(bn.name)}</button>`).join('');
    const bn = cfg.banners.find((x) => x.id === app.gachaTab);
    const kind = bn.kind === 'weapon' ? 'weapon' : bn.kind === 'character' ? 'character' : bn.kind === 'beginner' ? 'beginner' : 'standard';
    const curve = cfg.curves[kind]; const p = g.pity[kind];
    const st = MF.Gacha.stats(curve, curve.featuredSsrBps);
    let artName = '공방의 문'; let artSub = '상시 SSR: 세라·유리·라비엔·시온'; let attr = 'void';
    if (bn.kind === 'character') { const ch = charOf(bn.featuredSsr); artName = ch.name; artSub = `${ch.role} · 픽업 SR ${bn.featuredSr.map((x) => charOf(x).name).join('·')}`; attr = ch.attr; }
    if (bn.kind === 'weapon') { const w = IX.weapons[bn.featuredSsr]; artName = w.name; artSub = `${charOf(w.signature).name} 전용 무기 · ${w.passive.name}`; attr = charOf(w.signature).attr; }
    if (bn.kind === 'beginner') { artName = '첫 공방 모집'; artSub = '50회 안에 SSR 1명 확정 · 10회 = 모집권 8장'; }
    const softPct = Math.round(100 * (curve.softStart - 1) / curve.hard);
    const pityTxt = bn.kind === 'character' ? (p.guar ? '<b class="brass">다음 SSR은 픽업 확정(풀천장 상태)</b>' : '다음 SSR은 50% 확률로 픽업(반천장)')
      : bn.kind === 'weapon' ? (p.guar ? '<b>다음 SSR은 전용 무기 확정</b>' : '다음 SSR은 75% 확률로 전용 무기') : bn.kind === 'beginner' ? (p.gotSsr ? `SSR 획득 완료 · 남은 ${Math.max(0, curve.maxPulls - (p.total || 0))}회` : `${Math.max(0, curve.maxPulls - (p.total || 0))}회 안에 SSR 확정`) : `상시 300회 누적 시 SSR 1명 선택(${g.pity.standard.total}/300)`;
    const ticket = bn.ticket; const tk = g.tickets[ticket] || 0;
    const tname = { limitedTicket: '한정 모집권', weaponTicket: '무기 모집권', standardTicket: '상시 모집권' }[ticket];
    const results = app.lastPull.map((r) => {
      const name = r.kind === 'char' ? charOf(r.id).name : r.kind === 'weapon' ? IX.weapons[r.id].name : r.id;
      const tag = r.kind === 'char' ? (r.isNew ? 'NEW' : `공명 ${r.resonance}`) : r.kind === 'weapon' ? (r.isNew ? 'NEW' : `정련 R${r.refine}`) : `잔광 +${r.afterglow}`;
      return `<div class="res rar-${r.rarity}"><span class="t" style="color:var(--rar)">${r.rarity}</span><span>${esc(name)}</span><span class="muted">${esc(tag)}${r.lost ? ' · 픽뚫' : ''}${r.featured && r.rarity === 'SSR' ? ' · 픽업' : ''}</span></div>`;
    }).join('');
    const invChars = Object.entries(g.owned.chars).sort((a, b) => b[1] - a[1]).map(([id, n]) => `<div class="item"><h4>${emblem(charOf(id), 24)} ${esc(charOf(id).name)}</h4><p>${charOf(id).rarity} · 공명 ${Math.min(6, n - 1)}${n > 7 ? ` (+${n - 7} 초과)` : ''}</p></div>`).join('') || '<p class="muted">아직 없음</p>';
    const invW = Object.entries(g.owned.weapons).map(([id, n]) => `<div class="item"><h4>${esc(IX.weapons[id].name)}</h4><p>${IX.weapons[id].rarity} · 정련 R${Math.min(5, n)}</p></div>`).join('') || '<p class="muted">아직 없음</p>';
    const ssrHist = g.ssrLog.slice(0, 8).map((x) => `${x.at}회`).join(', ');
    const pulls = (n) => {
      const cost = bn.kind === 'beginner' ? 8 : n; const fromT = Math.min(tk, cost); const cry = (cost - fromT) * cfg.costPerPull;
      return `<button class="btn ${n === 10 ? 'primary' : ''}" data-act="pull" data-n="${n}" ${(bn.kind === 'beginner' && (p.done || n !== 10)) ? 'disabled' : ''}>${n}회 모집<small style="display:block;font-size:.78em">${fromT ? `${tname} ${fromT}장` : ''}${fromT && cry ? ' + ' : ''}${cry ? `결정 ${cry.toLocaleString()}` : ''}</small></button>`;
    };
    return `<div class="wrap grid">
      <div class="row" style="justify-content:space-between"><div><div class="eyebrow">모집 시뮬레이터</div><h2>반천장·공명·무기 모집</h2></div>
        <div class="wallet"><span class="pill brass">결정 <b class="num">${g.crystals.toLocaleString()}</b></span><span class="pill">한정권 ${g.tickets.limitedTicket}</span><span class="pill">무기권 ${g.tickets.weaponTicket}</span><span class="pill">상시권 ${g.tickets.standardTicket}</span><span class="pill">기억 결정체 ${g.shards}</span><span class="pill">잔광 ${g.afterglow}</span></div></div>
      <div class="banner-tabs">${tabs}</div>
      <div class="banner">
        <div class="banner-art attr-${attr}"><span class="ring"></span><div class="eyebrow">${bn.kind === 'character' ? '한정 캐릭터' : bn.kind === 'weapon' ? '무기' : bn.kind === 'beginner' ? '초심자' : '상시'}</div><div class="big">${esc(artName)}</div><div class="sub">${esc(artSub)}</div></div>
        <div class="panel pity">
          <div class="row" style="justify-content:space-between"><b>천장 ${p.n}/${curve.hard}</b><span class="muted">${curve.softStart}회부터 확률 상승</span></div>
          <div class="bar"><i style="width:${Math.min(100, Math.round(100 * p.n / curve.hard))}%"></i><span class="soft" style="left:${softPct}%"></span></div>
          <div>${pityTxt}</div>
          <div class="muted">SSR 기본 ${(curve.ssrBase / 100).toFixed(1)}% · 평균 ${st.mean.toFixed(1)}회 · ${bn.kind === 'character' ? `픽업 평균 ${st.featuredMean.toFixed(1)}회 · 최대 ${st.worst}회` : bn.kind === 'weapon' ? `전용 무기 평균 ${st.featuredMean.toFixed(1)}회 · 최대 ${st.worst}회` : `최대 ${curve.hard}회`} · SR 10회 보장</div>
          <div class="row">${pulls(1)}${pulls(10)}</div>
          <div class="row"><button class="btn small" data-act="addCrystal" data-n="1600">결정 +1,600</button><button class="btn small" data-act="addCrystal" data-n="16000">결정 +16,000</button><button class="btn small time" data-act="season">42일 무과금 수급 받기</button><button class="btn small" data-act="exchange">결정체 5 → ${bn.kind === 'weapon' ? '무기권' : '한정권'}</button></div>
          ${ssrHist ? `<div class="muted">최근 SSR: ${ssrHist}</div>` : ''}
        </div>
      </div>
      ${app.lastPull.length ? `<div class="panel"><h3>결과</h3><div class="results">${results}</div></div>` : ''}
      <div class="panel grid"><h3>보유 캐릭터 <span class="muted">(공명은 탐사에 바로 반영)</span></h3><div class="inv">${invChars}</div><h3>보유 무기</h3><div class="inv">${invW}</div>
      <div class="muted">총 ${g.spent.pulls}회 · 결정 환산 ${g.spent.crystals.toLocaleString()} 사용</div></div>
    </div>`;
  }

  // ───────── codex ─────────
  function viewCodex() {
    const tabs = `<div class="seg"><button data-act="ctab" data-v="chars" class="${app.codexTab === 'chars' ? 'on' : ''}">캐릭터</button><button data-act="ctab" data-v="weapons" class="${app.codexTab === 'weapons' ? 'on' : ''}">무기</button><button data-act="ctab" data-v="relics" class="${app.codexTab === 'relics' ? 'on' : ''}">유물·부품</button><button data-act="ctab" data-v="enemies" class="${app.codexTab === 'enemies' ? 'on' : ''}">적</button></div>`;
    let body = '';
    if (app.codexTab === 'chars') {
      const ch = charOf(app.codexChar); const res = resOf(ch.id);
      const kit = DATA.cards.filter((c) => c.owner === ch.id && c.pool !== false);
      const evo = DATA.cards.filter((c) => c.owner === ch.id && c.pool === false && c.rar !== 'T');
      const sig = DATA.weapons.find((w) => w.signature === ch.id);
      const row = (c) => `<tr><td><b>${esc(c.name)}</b>${c.start ? ' <span class="pill time">시작</span>' : ''}</td><td>${RAR_NAME[c.rar]}</td><td class="num">${c.cost}</td><td>${esc(MF.cardText(c, IX))}</td></tr>`;
      body = `<div class="codex"><div class="list">${DATA.characters.map((c) => `<button data-act="cchar" data-id="${c.id}" class="${c.id === ch.id ? 'on' : ''}">${emblem(c, 28)}<span>${esc(c.name)}<br><span class="muted" style="font-size:.8em">${c.rarity} · ${ATTR[c.attr].name}</span></span></button>`).join('')}</div>
        <div class="grid"><div class="panel grid"><div class="row">${emblem(ch, 56)}<div><h2>${esc(ch.name)} <span class="muted" style="font-size:.6em">${ch.en}</span></h2><div class="row"><span class="pill rar-${ch.rarity}" style="color:var(--rar)">${ch.rarity}${ch.pool === 'limited' ? ' 한정' : ''}</span><span class="pill">${attrIcon(ch.attr)} ${ATTR[ch.attr].name}</span><span class="pill">${WTYPE[ch.weapon]}</span><span class="pill">${esc(ch.role)}</span><span class="pill">${ch.age}세 · ${ch.tier === 'teen' ? '10대' : '성인'}</span></div></div></div>
          <p>${esc(ch.oneLine)}</p><dl class="kv"><dt>성격</dt><dd>${esc(ch.personality)}</dd><dt>이야기</dt><dd>${esc(ch.storyHook)}</dd><dt>패시브</dt><dd><b>${esc(ch.passive.name)}</b> — ${esc(ch.passive.text)}</dd><dt>필살기</dt><dd><b>${esc(ch.ult.name)}</b> — ${esc(ch.ult.text)}</dd>${sig ? `<dt>전용 무기</dt><dd><b>${esc(sig.name)}</b> — ${esc(sig.passive.name)}: ${esc(passiveText(sig))} <span class="muted">(R1→R5)</span></dd>` : ''}</dl></div>
          <div class="panel"><h3>공명 (중복 획득) <span class="muted">현재 ${res}</span></h3><ol style="margin:6px 0 0;padding-left:20px">${ch.resonance.map((r) => `<li style="${r.n <= res ? 'color:var(--brass)' : ''}">${esc(r.text)}</li>`).join('')}</ol></div>
          <div class="panel"><h3>고유 카드 12장</h3><div class="tbl-wrap"><table class="cards"><thead><tr><th>이름</th><th>등급</th><th>비용</th><th>효과</th></tr></thead><tbody>${kit.map(row).join('')}</tbody></table></div>
          ${evo.length ? `<h3 style="margin-top:12px">진화 분기</h3><div class="tbl-wrap"><table class="cards"><tbody>${evo.map(row).join('')}</tbody></table></div>` : ''}</div></div></div>`;
    } else if (app.codexTab === 'weapons') {
      body = `<div class="panel tbl-wrap"><table class="cards"><thead><tr><th>무기</th><th>종류</th><th>등급</th><th>패시브(R1→R5)</th><th>무기 카드</th></tr></thead><tbody>${DATA.weapons.map((w) => `<tr><td><b>${esc(w.name)}</b>${w.signature ? `<br><span class="muted">${esc(charOf(w.signature).name)} 전용</span>` : ''}</td><td>${WTYPE[w.type]}</td><td>${w.rarity}</td><td>${esc(w.passive.name)}: ${esc(passiveText(w))}</td><td>${esc(IX.cards[w.card].name)} — ${esc(MF.cardText(IX.cards[w.card], IX))}</td></tr>`).join('')}</tbody></table></div>`;
    } else if (app.codexTab === 'relics') {
      body = `<div class="panel tbl-wrap"><table class="cards"><thead><tr><th>유물</th><th>등급</th><th>효과</th></tr></thead><tbody>${DATA.relics.map((r) => `<tr><td><b>${esc(r.name)}</b></td><td>${({ common: '일반', rare: '희귀', boss: '보스' })[r.rarity]}</td><td>${esc(r.text)}</td></tr>`).join('')}</tbody></table>
        <h3 style="margin-top:14px">부품(카드 개조)</h3><table class="cards"><tbody>${DATA.parts.map((p) => `<tr><td><b>${esc(p.name)}</b></td><td>${({ temp: '임시', circuit: '시간 회로', engraving: '보조 각인' })[p.slot]}</td><td>${esc(p.text)}</td></tr>`).join('')}</tbody></table></div>`;
    } else {
      body = `<div class="panel tbl-wrap"><table class="cards"><thead><tr><th>적</th><th>구분</th><th>체력·균열</th><th>약점</th><th>행동 순환</th></tr></thead><tbody>${DATA.enemies.map((e) => `<tr><td><b>${esc(e.name)}</b></td><td>${({ normal: '일반', elite: '엘리트', boss: '보스', minion: '소환' })[e.tier]} · ${e.zone}구역</td><td class="num">${e.hp} · ${e.brk}</td><td>${e.weak.map((a) => ATTR[a].name).join(', ')}</td><td>${e.pattern.map((m) => esc(INTENT_TEXT(m, null).t)).join(' → ')}</td></tr>`).join('')}</tbody></table></div>`;
    }
    return `<div class="wrap grid"><div class="row" style="justify-content:space-between"><h2>도감</h2>${tabs}</div>${body}</div>`;
  }

  // ───────── memory & settings ─────────
  function viewMemory() {
    const rows = DATA.characters.map((ch) => {
      const mem = app.memory[ch.id];
      const specs = mem || MF.Run.baseKit(IX, ch.id);
      let cost = 0; let cap = 0;
      try { const r = MF.Run.create(DATA, { party: [{ charId: ch.id, res: resOf(ch.id) }], memory: mem ? { [ch.id]: mem } : undefined, seed: 1 }); const inf = r.memoryChanges()[ch.id]; cost = inf.changes.reduce((a, c) => a + c.cost, 0); cap = inf.capacity; } catch (e) { /* ignore */ }
      return `<div class="panel grid"><div class="row" style="justify-content:space-between"><h3>${emblem(ch, 26)} ${esc(ch.name)}</h3><span class="pill ${mem ? 'brass' : ''}">${mem ? `저장됨 · 용량 ${cost}/${cap}` : '기본 덱'}</span></div>
        <div class="row">${specs.map((sp) => `<span class="pill">${esc(IX.cards[sp.id].name)}${sp.plus ? '+' : ''}${sp.mods && (sp.mods.c || sp.mods.e) ? ' ⚙' : ''}</span>`).join('')}</div>
        ${mem ? `<div class="row"><button class="btn small danger" data-act="clearMem" data-id="${ch.id}">기본 덱으로 되돌리기</button></div>` : ''}</div>`;
    }).join('');
    return `<div class="wrap grid"><div><div class="eyebrow">기억 덱</div><h2>캐릭터별 저장된 덱</h2><p class="muted">탐사를 끝내면 바뀐 카드·강화·부품·진화를 기억 용량 안에서 저장해요. 공명 4단계마다 용량 +1.</p></div><div class="grid" style="grid-template-columns:repeat(auto-fill,minmax(280px,1fr))">${rows}</div></div>`;
  }
  function viewSettings() {
    const s = app.settings;
    return `<div class="wrap grid"><div class="panel grid"><h2>설정</h2>
      <div class="row"><span>글자 크기</span><span class="seg">${[100, 115, 130].map((v) => `<button data-act="scale" data-v="${v}" class="${s.scale === v ? 'on' : ''}">${v}%</button>`).join('')}</span></div>
      <div class="row"><span>테스트 모드(모든 캐릭터 사용)</span><span class="seg"><button data-act="allOwned" data-v="1" class="${s.allOwned ? 'on' : ''}">켜기</button><button data-act="allOwned" data-v="0" class="${!s.allOwned ? 'on' : ''}">끄기</button></span></div>
      <div class="row"><button class="btn danger" data-act="resetGacha">모집 기록 초기화</button><button class="btn danger" data-act="resetMemory">기억 덱 초기화</button><button class="btn danger" data-act="abandon" ${app.run ? '' : 'disabled'}>진행 중 탐사 포기</button></div>
      <p class="muted">저장은 이 브라우저에만 남아요. 이 페이지는 규칙 검증용 시제품이며 실제 게임 아트·연출은 들어 있지 않아요.</p></div></div>`;
  }

  // ───────── modals ─────────
  function modalView() {
    const m = app.modal; let inner = '';
    if (m.type === 'help') {
      inner = `<h2>규칙 1분 요약</h2><div class="grid" style="gap:6px">
        <p><b>행동력</b> 매 턴 4(최대 9). 손패 5장, 최대 10장. 남은 손패는 턴 종료 시 버려져요.</p>
        <p><b>예약</b> 비용을 지금 내고 다음 턴(칸 2) 또는 다다음 턴(칸 1) 시작에 해결. 예약 수치가 적힌 카드는 더 강해져요. 대상이 죽으면 일반 공격은 다른 적에게, <b>표식 고정</b> 카드는 불발.</p>
        <p><b>균열</b> 적의 약점 속성 피해로 균열 게이지가 깎여요. 0이 되면 그 적의 다음 행동이 취소되고, 다음 플레이어 턴 끝까지 받는 피해 +25%, 다음 턴 행동력 +1. 예약 공격을 균열 타이밍에 맞추세요.</p>
        <p><b>적 의도</b> 이번 행동과 다음 행동이 모두 보여요. 충전 뒤에는 큰 공격이 와요.</p>
        <p><b>필살기</b> 자기 카드가 해결될 때 게이지 +10(턴당 최대 30). 100이면 사용(무료).</p>
        <p><b>라비엔 계약</b> 선불=행동력 빌리기(다음 턴 상환), 절제=3장 이하로 버티면 다음 턴 첫 예약 +30%, 담보=다음 턴 칸 봉인+방어, 유예=상환 1턴 연기(+1).</p>
        <p><b>유리 성장</b> 성장 카드가 예약으로 해결되면 성장 +1. 3이면 전투 보상에서 진화 분기 선택.</p>
        <p><b>리엔 잔향</b> 직전과 다른 캐릭터 카드를 낼 때마다 잔향 +1(최대 3). 리엔 카드가 잔향만큼 강해져요.</p>
        <p><b>루미 과부하</b> 파티 체력을 내고 강한 효과. 체력 50% 이하에서 더 강해져요.</p>
        <p><b>공방·변주</b> 카드에 시간 회로/보조 각인을 1개씩 붙여요. <b>기억 저장</b> 탐사 종료 시 바뀐 덱을 용량 안에서 저장(패배 시 1개).</p></div>`;
    } else if (m.type === 'pile') {
      const b = app.run.battle; const z = m.z;
      const list = z === 'draw' ? [...b.s.zones.draw].sort() : b.s.zones[z];
      inner = `<h2>${({ draw: '뽑을 카드(순서는 비공개)', discard: '버린 카드', exhaust: '소멸한 카드' })[z]} ${list.length}장</h2><div class="pick-grid">${list.map((u) => cardFace(b.s.cards[u], { def: b.effDef(b.s.cards[u]) })).join('') || '<p class="muted">없음</p>'}</div>`;
    } else if (m.type === 'deck') {
      const s = app.run.s;
      const ok = (c) => !m.pick || (!c.weapon && !(m.pick.kind === 'upgrade' && c.plus) && !(m.pick.keys || []).includes(c.key));
      inner = `<h2>덱 ${s.deck.length}장</h2>${m.pick ? `<p class="pill time">${esc(m.pick.label)}${m.pick.kind === 'upgrade' ? ' · 이미 강화한 카드와 무기 카드는 고를 수 없어요' : ' · 무기 카드는 고를 수 없어요'}</p>` : ''}<div class="pick-grid">${s.deck.map((c) => cardFace(c, { act: m.pick && ok(c) ? 'deckPick' : '', cls: m.pick && !ok(c) ? 'cant' : '' })).join('')}</div>`;
    } else if (m.type === 'parts') {
      const s = app.run.s;
      inner = `<h2>보유 부품</h2><p class="muted">부품을 고른 뒤 붙일 카드를 고르세요. 회로·각인은 카드마다 1칸씩, 같은 칸에 붙이면 교체돼요.</p><div class="grid">${s.partsFound.map((id, i) => `<button class="item" data-act="usePart" data-id="${id}"><h4>${esc(IX.parts[id].name)} <span class="pill">${IX.parts[id].slot === 'circuit' ? '시간 회로' : '보조 각인'}</span></h4><p>${esc(IX.parts[id].text)}</p></button>`).join('')}</div>`;
    } else if (m.type === 'picker') {
      const pk = app.picker; const req = pk.reqs[pk.idx]; const b = app.run.battle;
      const opts = pickerOptions(req, pk.uid);
      inner = `<h2>${esc(req.label)} 고르기 (${pk.picks.length + 1}/${pk.reqs.reduce((a, r) => a + r.n, 0)})</h2><div class="pick-grid">${opts.map((u) => cardFace(b.s.cards[u], { def: b.effDef(b.s.cards[u]), act: 'pick' })).join('') || '<p class="muted">고를 카드가 없어요.</p>'}</div><div class="row"><button class="btn" data-act="pickAuto">자동으로 고르기</button><button class="btn" data-act="closeModal">취소</button></div>`;
    } else if (m.type === 'info') {
      inner = m.html;
    }
    return `<div class="modal-bg" data-act="bgClose"><div class="modal" role="dialog" aria-modal="true">${inner}<div class="row" style="justify-content:flex-end"><button class="btn" data-act="closeModal">닫기</button></div></div></div>`;
  }

  // ───────── actions ─────────
  function startRun() {
    const p = app.party;
    const party = p.chars.filter(usable).slice(0, 3).map((cid) => {
      const list = weaponsFor(cid); const wid = p.weapons[cid] && list.some((w) => w.id === p.weapons[cid]) ? p.weapons[cid] : list.find((w) => w.rarity === 'R').id;
      return { charId: cid, res: resOf(cid), weaponId: wid, refine: IX.weapons[wid].rarity === 'R' ? 1 : MF.Gacha.refine(app.gacha, wid) };
    });
    const memory = {};
    if (p.useMemory) for (const x of party) if (app.memory[x.charId]) memory[x.charId] = app.memory[x.charId];
    const diff = p.difficulty === 'hard' ? { hp: 120, dmg: 125 } : { hp: 100, dmg: 100 };
    runCounter += 1;
    const seed = URL_SEED !== null ? URL_SEED + runCounter : ((Date.now() & 0x7fffffff) || 1);
    app.run = MF.Run.create(DATA, { party, memory, seed, length: p.length, difficulty: diff });
    app.run.s.visited = [];
    app.saveSel = null; app.sel = null; app.target = null; app.partyForce = false;
    syncRunScreen();
  }
  function act(el, ev) {
    const a = el.dataset.act; const d = el.dataset;
    const run = app.run;
    switch (a) {
      case 'nav': app.screen = d.to; if (d.to === 'party') app.partyForce = false; app.modal = null; break;
      case 'resume': syncRunScreen(); break;
      case 'help': app.modal = { type: 'help' }; break;
      case 'closeModal': app.modal = null; app.picker = null; break;
      case 'bgClose': if (ev.target === el) { app.modal = null; app.picker = null; } else return; break;
      case 'pickChar': {
        const id = d.id; const list = app.party.chars.filter(usable);
        const i = list.indexOf(id);
        if (i >= 0) list.splice(i, 1); else { if (list.length >= 3) list.shift(); list.push(id); }
        app.party.chars = list; saveParty(); break;
      }
      case 'len': app.party.length = d.v; saveParty(); break;
      case 'diff': app.party.difficulty = d.v; saveParty(); break;
      case 'usemem': app.party.useMemory = d.v === '1'; saveParty(); break;
      case 'startRun': startRun(); break;
      case 'node': try { run.enter(d.id); run.s.visited.push(d.id); app.lastDmg = {}; syncRunScreen(); } catch (e) { toast(errMsg(e)); } break;
      case 'card': {
        if (app.sel && app.sel.uid === d.uid) { app.sel = null; app.target = null; break; }
        app.sel = { uid: d.uid };
        const need = run.battle.needsTarget(d.uid); const alive = run.battle.alive();
        if (!need) app.target = null;
        else if (alive.length === 1) app.target = alive[0].uid;
        else if (need === 'fixed') { const m = alive.filter((e) => e.st.mark > 0); app.target = m.length === 1 ? m[0].uid : (app.target && m.some((e) => e.uid === app.target) ? app.target : null); }
        break;
      }
      case 'enemy': {
        const e = run.battle.enemy(d.uid);
        if (app.sel && app.sel.ult) {
          const before = Object.fromEntries(run.battle.s.enemies.map((x) => [x.uid, x.hp]));
          try { run.battleCmd('ult', { actor: app.sel.ult, target: d.uid }); afterBattleCmd(before); } catch (err) { toast(errMsg(err)); }
          app.sel = null; break;
        }
        if (app.sel && app.sel.uid) {
          const need = run.battle.needsTarget(app.sel.uid);
          if (need === 'fixed' && e.st.mark <= 0) { toast('표식이 있는 적만 고를 수 있어요.'); break; }
          app.target = d.uid; break;
        }
        const def = IX.enemies[e.id];
        app.modal = { type: 'info', html: `<h2>${esc(e.name)}</h2><p>체력 ${e.hp}/${e.maxHp} · 균열 ${e.brk}/${e.brkMax} · 약점 ${e.weak.map((x) => ATTR[x].name).join(', ')}</p><p><b>행동 순환</b>: ${def.pattern.map((m) => esc(INTENT_TEXT(m, e).t)).join(' → ')}</p>` };
        break;
      }
      case 'cancel': app.sel = null; app.target = null; break;
      case 'mode': {
        const uid = app.sel.uid; const reqs = choiceRequests(uid, d.mode);
        if (reqs.length) { app.picker = { uid, mode: d.mode, reqs, idx: 0, picks: [], left: reqs[0].n }; app.modal = { type: 'picker' }; break; }
        sendPlay(uid, d.mode, []); return;
      }
      case 'pick': case 'pickAuto': {
        const pk = app.picker; if (!pk) break;
        if (a === 'pick') pk.picks.push(d.uid);
        pk.left -= 1;
        if (pk.left <= 0 || a === 'pickAuto') { pk.idx += 1; if (pk.idx < pk.reqs.length) pk.left = pk.reqs[pk.idx].n; }
        if (pk.idx >= pk.reqs.length || (a === 'pickAuto')) { sendPlay(pk.uid, pk.mode, pk.picks); return; }
        break;
      }
      case 'ult': {
        const a2 = d.actor; const ch = charOf(a2); const b = run.battle;
        const eff = b.actor(a2).ultReplace || ch.ult.eff;
        const needs = eff.some((o) => o.tgt === 'one' || (o.op === 'dmg' && !o.tgt));
        if (needs && b.alive().length > 1) { app.sel = { ult: a2 }; app.target = null; break; }
        const before = Object.fromEntries(b.s.enemies.map((x) => [x.uid, x.hp]));
        try { run.battleCmd('ult', { actor: a2, target: b.alive()[0] ? b.alive()[0].uid : null }); afterBattleCmd(before); } catch (err) { toast(errMsg(err)); }
        break;
      }
      case 'endTurn': {
        const before = Object.fromEntries(run.battle.s.enemies.map((x) => [x.uid, x.hp]));
        try { run.battleCmd('end'); afterBattleCmd(before); } catch (err) { toast(errMsg(err)); }
        app.sel = null; app.target = null; break;
      }
      case 'toggleLog': app.logOpen = !app.logOpen; break;
      case 'pile': app.modal = { type: 'pile', z: d.z }; break;
      case 'resInfo': { const b = run.battle; const inst = b.s.cards[d.uid]; app.modal = { type: 'info', html: `<h2>예약된 카드</h2><div class="pick-grid">${cardFace(inst, { def: b.effDef(inst) })}</div>` }; break; }
      case 'deck': app.modal = { type: 'deck' }; break;
      case 'parts': app.modal = { type: 'parts' }; break;
      case 'usePart': app.modal = { type: 'deck', pick: { label: `「${IX.parts[d.id].name}」을 붙일 카드를 고르세요`, kind: 'part', part: d.id } }; break;
      case 'deckPick': {
        const pk = app.modal.pick; const key = d.key;
        try {
          if (pk.kind === 'part') run.applyFoundPart(pk.part, key);
          if (pk.kind === 'upgrade') run.rest('upgrade', key);
          if (pk.kind === 'forgeVar') { run.forgeOffer(key); }
          if (pk.kind === 'forgeRemove') run.forgeRemove(key);
          if (pk.kind === 'shopRemove') run.buy('remove', null, key);
          if (pk.kind === 'event') {
            pk.keys.push(key);
            if (pk.keys.length < pk.need && run.s.deck.filter((c) => !c.weapon && !pk.keys.includes(c.key)).length) { pk.label = `제거할 카드를 고르세요 (${pk.keys.length + 1}/${pk.need})`; break; }
            run.eventChoose(pk.idx, pk.keys); syncRunScreen();
          }
          app.modal = null;
        } catch (err) { toast(errMsg(err)); }
        break;
      }
      case 'takeCard': try { run.takeCard(d.id); } catch (err) { toast(errMsg(err)); } break;
      case 'skipCard': run.skipCard(); break;
      case 'takeRelic': run.takeRelic(d.id); break;
      case 'takePart': run.takePart(); break;
      case 'evolve': try { run.evolve(d.key, d.branch); toast(`${IX.cards[d.branch].name}(으)로 진화!`); } catch (err) { toast(errMsg(err)); } break;
      case 'proceed': run.proceed(); syncRunScreen(); break;
      case 'restHeal': try { run.rest('heal'); } catch (err) { toast(errMsg(err)); } break;
      case 'restUpgrade': app.modal = { type: 'deck', pick: { label: '강화할 카드를 고르세요(수치 약 30% 상승)', kind: 'upgrade' } }; break;
      case 'forgeVar': app.modal = { type: 'deck', pick: { label: '변주할 카드를 고르세요', kind: 'forgeVar' } }; break;
      case 'forgeRemove': app.modal = { type: 'deck', pick: { label: '제거할 카드를 고르세요', kind: 'forgeRemove' } }; break;
      case 'forgeApply': run.forgeApply(d.id); break;
      case 'buy': try { run.buy(d.kind, d.id); } catch (err) { toast(errMsg(err)); } break;
      case 'shopRemove': app.modal = { type: 'deck', pick: { label: '제거할 카드를 고르세요', kind: 'shopRemove' } }; break;
      case 'evChoice': {
        try {
          const ev = IX.events[run.s.pending.id]; const fx = ev.choices[Number(d.i)].fx;
          const n = fx.cardCopy ? 1 : (fx.cardRemove || 0);
          if (n) { app.modal = { type: 'deck', pick: { label: fx.cardCopy ? '복제할 카드를 고르세요' : `제거할 카드를 고르세요 (${n}장)`, kind: 'event', idx: Number(d.i), need: n, keys: [] } }; break; }
          run.eventChoose(Number(d.i));
          syncRunScreen();
        } catch (err) { toast(errMsg(err)); }
        break;
      }
      case 'archive': run.archive(d.v); syncRunScreen(); break;
      case 'saveToggle': {
        const set = new Set(app.saveSel[d.cid]); const i = Number(d.i);
        if (set.has(i)) set.delete(i); else set.add(i);
        app.saveSel[d.cid] = [...set]; break;
      }
      case 'commitSave': {
        try { const mem = run.commitMemory(app.saveSel); Object.assign(app.memory, mem); saveMemory(); toast('기억을 저장했어요.'); app.saveSel = null; syncRunScreen(); } catch (err) { toast(errMsg(err)); }
        break;
      }
      case 'skipSave': run.s.pending = { type: 'done' }; run.s.phase = 'DONE'; app.saveSel = null; syncRunScreen(); break;
      case 'newRun': app.run = null; app.screen = 'party'; break;
      case 'gtab': app.gachaTab = d.id; app.lastPull = []; break;
      case 'pull': {
        try { app.lastPull = MF.Gacha.pull(DATA, app.gacha, app.gachaTab, Number(d.n)); saveGacha(); } catch (err) { toast(errMsg(err)); }
        break;
      }
      case 'addCrystal': app.gacha.crystals += Number(d.n); saveGacha(); break;
      case 'season': {
        app.gacha.crystals += START_INCOME.crystals; app.gacha.tickets.limitedTicket += START_INCOME.limitedTicket || 0; app.gacha.tickets.standardTicket += START_INCOME.standardTicket || 0;
        saveGacha(); toast(`42일 무과금 수급: 결정 ${START_INCOME.crystals.toLocaleString()} + 한정권 ${START_INCOME.limitedTicket}`); break;
      }
      case 'exchange': {
        const bn = DATA.gacha.banners.find((x) => x.id === app.gachaTab); const key = bn.kind === 'weapon' ? 'weaponTicket' : 'limitedTicket';
        if (app.gacha.shards < 5) { toast('기억 결정체가 5개 필요해요.'); break; }
        app.gacha.shards -= 5; app.gacha.tickets[key] += 1; saveGacha(); break;
      }
      case 'ctab': app.codexTab = d.v; break;
      case 'cchar': app.codexChar = d.id; break;
      case 'clearMem': delete app.memory[d.id]; saveMemory(); break;
      case 'scale': app.settings.scale = Number(d.v); saveSettings(); break;
      case 'allOwned': app.settings.allOwned = d.v === '1'; saveSettings(); break;
      case 'resetGacha': app.gacha = MF.Gacha.newState(20261014); app.gacha.crystals = 16000; app.gacha.tickets.limitedTicket = 5; saveGacha(); toast('모집 기록을 초기화했어요.'); break;
      case 'resetMemory': app.memory = {}; saveMemory(); toast('기억 덱을 초기화했어요.'); break;
      case 'abandon': app.run = null; app.screen = 'home'; break;
      default: return;
    }
    render();
  }
  document.addEventListener('click', (ev) => {
    const el = ev.target.closest('[data-act]');
    if (!el || el.disabled) return;
    if (el.tagName === 'SELECT' || el.tagName === 'INPUT') return;
    act(el, ev);
  });
  document.addEventListener('change', (ev) => {
    const el = ev.target;
    if (el.dataset.act === 'weapon') { app.party.weapons[el.dataset.id] = el.value; saveParty(); render(); }
    if (el.dataset.act === 'assign') { try { app.run.assign(el.dataset.key, el.value); app.saveSel = null; } catch (e) { toast(errMsg(e)); } render(); }
    if (el.dataset.act === 'saveToggle') act(el, ev);
  });
  document.addEventListener('keydown', (ev) => {
    if (ev.key === 'Escape') { if (app.modal) { app.modal = null; app.picker = null; render(); } else if (app.sel) { app.sel = null; app.target = null; render(); } }
    if ((ev.key === 'Enter' || ev.key === ' ') && ev.target.matches('g[data-act]')) { ev.preventDefault(); act(ev.target, ev); }
  });

  // hot-reload friendly boot (keeps an in-progress run across republishes when the viewer supports it)
  function start(data) {
    try { if (data && data.run) { app.run = new MF.Run(DATA, data.run); if (data.screen) app.screen = data.screen; } } catch (e) { /* ignore stale snapshot */ }
    render();
  }
  try { if (window.claude && window.claude.hot && window.claude.hot.snapshot) window.claude.hot.snapshot(() => ({ run: app.run ? app.run.s : null, screen: app.screen })); } catch (e) { /* optional */ }
  if (window.claude && window.claude.hot && window.claude.hot.ready) window.claude.hot.ready(start); else start((window.claude && window.claude.hot && window.claude.hot.data) || {});
  window.__mf = app;
})();
