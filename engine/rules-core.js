/*
 * Memory Forge v0.7 — executable rules reference (battle · run · memory save · gacha).
 * Runs unchanged in Node (require) and in the browser (window.MFCore).
 * Design goals: deterministic (seeded xoshiro128**), command-based, atomic (a failed command
 * changes nothing), integer damage math with a single floor at the end, hidden info separated.
 * This is a P0 reference for porting to the C# battle core — not production server code.
 */
(function (root) {
  'use strict';

  // ───────────────────────── basics ─────────────────────────
  class RuleError extends Error { constructor(code, detail) { super(code); this.code = code; this.detail = detail; } }
  const clone = (o) => JSON.parse(JSON.stringify(o));
  const MemoryV2 = typeof module !== 'undefined' && module.exports ? require('../proposals/memory_v2.cjs') : root.MFMemoryV2;
  const BPS = 10000;

  // xoshiro128** seeded through splitmix32 (128-bit state; seed is chosen by the server, never the client)
  function splitmix32(seed) {
    let a = seed >>> 0;
    return function () {
      a = (a + 0x9e3779b9) >>> 0;
      let t = a ^ (a >>> 16); t = Math.imul(t, 0x21f0aaad);
      t = t ^ (t >>> 15); t = Math.imul(t, 0x735a2d97);
      return (t ^ (t >>> 15)) >>> 0;
    };
  }
  const rotl = (x, k) => ((x << k) | (x >>> (32 - k))) >>> 0;
  const Rng = {
    seed(seed) {
      const sm = splitmix32(seed);
      const s = [sm(), sm(), sm(), sm()];
      if (s.every((x) => x === 0)) s[0] = 1;
      return s;
    },
    next(s) {
      const result = Math.imul(rotl(Math.imul(s[1], 5) >>> 0, 7), 9) >>> 0;
      const t = (s[1] << 9) >>> 0;
      s[2] = (s[2] ^ s[0]) >>> 0; s[3] = (s[3] ^ s[1]) >>> 0;
      s[1] = (s[1] ^ s[2]) >>> 0; s[0] = (s[0] ^ s[3]) >>> 0;
      s[2] = (s[2] ^ t) >>> 0; s[3] = rotl(s[3], 11);
      return result;
    },
    below(s, n) {
      if (!(n > 0 && n <= 0x100000000)) throw new RuleError('RNG_BOUND');
      const limit = 0x100000000 - (0x100000000 % n);
      for (;;) { const x = Rng.next(s); if (x < limit) return x % n; }
    },
    chanceBps(s, bps) { return Rng.below(s, BPS) < bps; },
    shuffle(s, arr) { for (let i = arr.length - 1; i > 0; i--) { const j = Rng.below(s, i + 1); const t = arr[i]; arr[i] = arr[j]; arr[j] = t; } return arr; },
    pick(s, arr) { return arr[Rng.below(s, arr.length)]; },
  };

  // ───────────────────────── data index ─────────────────────────
  function indexData(data) {
    const by = (arr) => Object.fromEntries(arr.map((x) => [x.id, x]));
    if (data && data.__ix) return data;
    return {
      __ix: true, raw: data, rules: data.rules,
      cards: by(data.cards), chars: by(data.characters), weapons: by(data.weapons), parts: by(data.parts),
      relics: by(data.relics), enemies: by(data.enemies), statuses: by(data.statuses), events: by(data.events),
      attrs: by(data.attributes),
    };
  }

  // ───────────────────────── combat stats from account growth ─────────────────────────
  // See content/progression.mjs (stats). Without growth data the reference battle values are used:
  // power4 160 (power 40) and 81 HP per character, which is what the prototype's test mode plays.
  const clampInt = (v, lo, hi) => Math.max(lo, Math.min(hi, Math.floor(Number(v) || 0)));
  const Stats = {
    REFERENCE: { power4: 160, power: 40, hp: 81, gearBps: 0 },
    charPower(cp, lv) { return cp.base + Math.floor(cp.num * lv / cp.den); },
    weaponPower(wp, rarity, wlv) { const w = wp[rarity] || wp.R; return w.base + Math.floor(w.k * wlv / 60); },
    // entry: {charId, level, asc, weaponId, weaponLevel, armor (0..15 or null), accessory (0..15 or null), skillLv, ultLv}
    derive(ix, entry) {
      const st = ix.raw.progression && ix.raw.progression.stats;
      const skillMax = st ? st.skill.maxLevel : 10;
      const skillLv = clampInt(entry.skillLv || 1, 1, skillMax); const ultLv = clampInt(entry.ultLv || 1, 1, skillMax);
      if (!st || entry.level === undefined || entry.level === null) return { ...Stats.REFERENCE, skillLv, ultLv, reference: true };
      const lv = clampInt(entry.level, 1, st.levelCap);
      const w = entry.weaponId ? ix.weapons[entry.weaponId] : null;
      const wl = clampInt(entry.weaponLevel || 1, 1, st.levelCap);
      const power = Stats.charPower(st.charPower, lv) + (w ? Stats.weaponPower(st.weaponPower, w.rarity, wl) : 0);
      const baseHp = st.hpBase + lv; let hp = baseHp;
      const g = st.gear;
      if (entry.armor !== undefined && entry.armor !== null) { const n = clampInt(entry.armor, 0, g.maxEnhance); hp += g.armorFlatBase + n + Math.floor(baseHp * (g.armorBpsBase + g.armorBpsPer * n) / BPS); }
      const gearBps = entry.accessory !== undefined && entry.accessory !== null ? g.accessoryBpsBase + g.accessoryBpsPer * clampInt(entry.accessory, 0, g.maxEnhance) : 0;
      return { power4: 4 * power, power, hp, gearBps, skillLv, ultLv, reference: false };
    },
    // percent multipliers that make an at-level party equal to the reference battle
    stageScale(ix, level) {
      const st = ix.raw.progression.stats; const lv = clampInt(level, 1, st.levelCap);
      const pref = Stats.charPower(st.charPower, lv) + Stats.weaponPower(st.weaponPower, st.stageScale.refWeapon, lv);
      return { hp: Math.round(100 * pref / st.referencePower), dmg: Math.round(100 * (st.hpBase + lv) / st.referenceHpPerChar) };
    },
    combine(a, b) { return { hp: Math.round((a.hp || 100) * (b.hp || 100) / 100), dmg: Math.round((a.dmg || 100) * (b.dmg || 100) / 100) }; },
    xpToNext(ix, lv, kind) { const L = ix.raw.progression.levelling; const x = L.charXp.k * (L.charXp.a + lv + Math.floor(lv * lv / L.charXp.sqDiv)); return kind === 'weapon' ? Math.round(x * L.weaponXpRatio) : x; },
  };

  // ───────────────────────── card text (Korean) ─────────────────────────
  const COUNTER_NAME = { echo: '잔향', exhausted: '소멸한 카드', debt: '부채', growthSum: '덱의 성장치 합', growthCards: '성장 카드 수',
    counter: '반격', missingHp10: '잃은 체력 10', contracts: '이번 턴 계약', tempParts: '붙인 임시 부품', block: '현재 방어',
    lastCount: '그 수', targetBurn: '대상 화상' };
  const ST_NAME = { vul: '취약', weak: '약화', mark: '표식', burn: '화상' };
  const TGT_NAME = { one: '', same: '그 적에게 ', all: '모든 적에게 ', rand: '무작위 적에게 ', fixed: '표식 적에게 ' };
  const COND_TEXT = (c) => ({
    marked: '대상에게 표식이 있으면', burning: '대상이 화상 상태면', broken: '대상이 균열 상태면', debt: '부채가 있으면',
    modded: '이 카드에 개조가 있으면', reserved: '예약으로 해결되면', intentAttack: '대상이 공격할 예정이면',
    echo: `잔향 ${c.n} 이상이면`, hpBelow: `파티 체력 ${c.pct}% 이하면`, targetHpBelow: `대상 체력 ${c.pct}% 이하면`,
    growthInHand: `손에 성장 카드가 ${c.n}장 이상이면`, exhaustedAtLeast: `소멸한 카드가 ${c.n}장 이상이면`, killedThis: '이 카드로 적을 처치하면',
  }[c.c] || c.c);
  const POWER_TEXT = {
    blueprint: (p) => `이번 전투 동안 개조된 카드의 피해·방어 +${p.bps / 100}%`,
    greenhouse: (p) => `이번 전투 동안 성장 카드가 해결될 때 방어 ${p.blk}`,
    bulwark: (p) => `이번 전투 동안 턴이 바뀌어도 방어를 최대 ${p.keep} 유지`,
    rebirth: (p) => `이번 전투 동안 턴 시작 시 회복 ${p.heal}`,
    oblivion: (p) => `이번 전투 동안 카드가 소멸할 때마다 방어 ${p.blk}`,
    casefile: (p) => `이번 전투 동안 표식 적에게 주는 피해 +${p.bps / 100}%`,
    echochamber: () => '이번 전투 동안 잔향이 3이 되면 카드 1장 뽑기(턴당 1회)',
  };
  const BUFF_TEXT = {
    charge: (o) => `다음 공격 카드 피해 +${o.n / 100}%`, seraLock: (o) => `세라의 예약 공격 피해 +${o.n / 100}%`,
    healUp: (o) => `다음 회복 +${o.n / 100}%`, lumiRush: (o) => `이번 턴 루미의 공격 피해 +${o.n / 100}%`,
    counterDouble: () => '반격 피해 2배', overloadFree: () => '이번 턴 과부하 체력 대가 0',
  };
  function scaleText(sc) { if (!sc) return ''; return ` +${sc.k}×${COUNTER_NAME[sc.per] || sc.per}${sc.max !== undefined ? `(최대 +${sc.max})` : ''}`; }
  function vText(op, key = 'v', rkey = 'rv', akey = 'ra') {
    const v = op[key]; const rv = op[rkey]; const ra = op[akey];
    let t = `${v}`;
    if (rv !== undefined && ra !== undefined && ra !== rv) t += ` (예약 ${rv} / 다다음 ${ra})`;
    else if (rv !== undefined && rv !== v) t += ` (예약 ${rv})`;
    else if (ra !== undefined && ra !== v) t += ` (다다음 ${ra})`;
    return t;
  }
  function opText(op, ix) {
    const card = (id) => ix.cards[id]?.name || id;
    switch (op.op) {
      case 'dmg': {
        let hits = op.hits ?? 1; let t = TGT_NAME[op.tgt || 'one'] + '피해 ';
        if (op.v === 0 && op.scale) t += `${COUNTER_NAME[op.scale.per]}×${op.scale.k}${op.scale.max !== undefined ? `(최대 ${op.scale.max})` : ''}`;
        else t += vText(op) + scaleText(op.scale);
        if (op.hitsScale) t += ` ×(${hits}+${COUNTER_NAME[op.hitsScale.per]})회`; else if (hits > 1) t += ` ×${hits}`;
        if (op.pierce) t += `, 관통 ${op.pierce / 100}%`;
        if (op.brk && op.brk !== 1) t += `, 균열 ${op.brk}`;
        if (op.shock) t += `, 충격 ${op.shock}`;
        if (op.mult) t += op.mult.map((m) => `. ${COND_TEXT(m.if)} 피해 ×${m.x}`).join('');
        return t;
      }
      case 'blk': return (op.v === 0 && op.scale ? `방어 ${COUNTER_NAME[op.scale.per]}×${op.scale.k}` : `방어 ${vText(op)}${scaleText(op.scale)}`);
      case 'heal': return `회복 ${vText(op)}${scaleText(op.scale)}${op.overflowBlock ? ', 넘친 회복은 방어로' : ''}`;
      case 'st': return `${TGT_NAME[op.tgt || 'one']}${ST_NAME[op.s]} ${op.n}${op.rn !== undefined ? ` (예약 ${op.rn})` : ''}`;
      case 'buff': return `${(BUFF_TEXT[op.s] || ((o) => o.s))(op)}${op.turns && op.turns > 1 ? ` (${op.turns}턴)` : ''}`;
      case 'counter': return `반격 ${op.n}`;
      case 'draw': return op.scale && !op.n ? `${COUNTER_NAME[op.scale.per]}만큼 카드 뽑기${op.scale.max !== undefined ? `(최대 ${op.scale.max})` : ''}` : `카드 ${op.n}장 뽑기${op.rn !== undefined ? ` (예약 ${op.rn}장)` : ''}`;
      case 'discard': return `카드 ${op.n}장 버리기`;
      case 'ap': return `행동력 +${op.n}`;
      case 'apNext': return `다음 턴 행동력 +${op.n}`;
      case 'gauge': return `${op.who === 'all' ? '모든 캐릭터' : '소유자'} 필살기 게이지 +${op.n}`;
      case 'create': return `손에 「${card(op.card)}」 ${op.n}장 추가`;
      case 'pull': return '예약 1개를 1턴 앞당김(이번 턴에 도착하면 즉시 해결)';
      case 'slot': return [op.next ? `이번 턴 다음 턴 예약 칸 +${op.next}` : '', op.after ? `다다음 턴 예약 칸 +${op.after}` : ''].filter(Boolean).join(', ');
      case 'grow': return '성장';
      case 'growPick': return `손의 성장 카드 1장 성장 +${op.n}`;
      case 'growLowest': return `성장이 가장 낮은 성장 카드 ${op.n}장 성장 +1`;
      case 'growAll': return '모든 성장 카드 성장 +1';
      case 'loan': return `행동력 +${op.n}, 다음 턴 행동력 -${op.n}(부채 ${op.n})`;
      case 'defer': return '다음 턴 부채 1개를 1턴 미루고 금액 +1(재유예 불가)';
      case 'restraint': return '이번 턴 카드를 3장 이하로 쓰면 다음 턴 첫 예약 피해·방어 +30%. 4장째에 위약: 다음 턴 드로우 -1';
      case 'collateral': return `다음 턴 예약 칸 1개 봉인(다음 내 턴 종료까지), 방어 ${op.v}`;
      case 'selfdmg': return `과부하 ${op.v}`;
      case 'cleanse': return '해로운 효과 모두 제거';
      case 'cleanseOne': return `${ST_NAME[op.s] || op.s} 제거`;
      case 'exhaustPick': return `${op.from === 'discard' ? '버린 카드' : '손'}에서 카드 ${op.n}장 소멸`;
      case 'exhaustHand': return '손패를 모두 소멸';
      case 'tune': return `${op.pick === 'all' ? '손의 모든 카드' : op.pick === 'same' ? '그 카드' : op.pick === 'random' ? '손의 무작위 카드 1장' : '손의 카드 1장'}에 임시 회로 「${ix.parts[op.part]?.name || op.part}」 부착`;
      case 'costdown': return `${op.pick === 'all' ? '이번 턴 손패의 모든 카드' : '손의 카드 1장'} 비용 -${op.n}(이번 턴)`;
      case 'recall': return '버린 카드 1장을 손으로';
      case 'copyPick': return '손의 성장 카드 1장의 비용 0 소멸 사본 추가';
      case 'power': return (POWER_TEXT[op.id] || (() => op.id))(op);
      case 'echo': return `잔향 +${op.n}`;
      case 'nextBoost': return `이번 턴 다음에 내는 다른 캐릭터 카드 피해·방어 +${op.bps / 100}%`;
      case 'freeNext': return `이번 턴 다음에 내는 카드 ${op.n}장 비용 0`;
      case 'clearDebt': return '모든 부채 제거';
      case 'breakBonus': return op.scale ? `균열 +${COUNTER_NAME[op.scale.per]}` : `균열 +${op.n}`;
      case 'breakBonusAll': return `모든 적 균열 +${COUNTER_NAME[op.scale?.per] || op.n}`;
      case 'if': return `${COND_TEXT(op.cond)} ${op.then.map((o) => opText(o, ix)).join(', ')}${op.else && op.else.length ? `, 아니면 ${op.else.map((o) => opText(o, ix)).join(', ')}` : ''}`;
      default: return op.op;
    }
  }
  function cardText(def, ix) {
    const parts = def.eff.map((op) => opText(op, ix)).filter(Boolean);
    const kw = [];
    if (def.res === 'o') kw.push('예약 전용'); else if (def.res === 'after') kw.push('다다음 턴 예약 전용');
    else if (def.res === 'n' && !(def.kw || []).includes('contract')) kw.push('즉시 전용');
    if ((def.kw || []).includes('contract')) kw.push('계약');
    if (def.growth) kw.push('성장');
    if ((def.kw || []).includes('exhaust')) kw.push('소멸');
    if (def.uses) kw.push(`전투당 ${def.uses}회`);
    if (def.costIf) kw.push(`${COND_TEXT(def.costIf.cond)} 비용 ${def.costIf.delta}`);
    if (def.flavor && !parts.length) parts.push(def.flavor);
    return (kw.length ? `[${kw.join('·')}] ` : '') + parts.join('. ') + (parts.length ? '.' : '');
  }

  // ───────────────────────── effective card definitions ─────────────────────────
  const UPGRADE_KEYS = { dmg: ['v', 'rv', 'ra'], blk: ['v', 'rv', 'ra'], heal: ['v', 'rv', 'ra'], counter: ['n', 'rn'], collateral: ['v'] };
  function mapOps(ops, fn) { return ops.map((op) => { const o = fn(clone(op)); if (o.then) o.then = mapOps(o.then, fn); if (o.else) o.else = mapOps(o.else, fn); return o; }); }
  function upgradeOps(ops, mult) {
    return mapOps(ops, (o) => {
      const keys = UPGRADE_KEYS[o.op];
      if (keys) for (const k of keys) if (typeof o[k] === 'number' && o[k] > 0) o[k] = Math.ceil(o[k] * mult - 1e-9);
      if (o.op === 'st') { o.n += 1; if (o.rn !== undefined) o.rn += 1; }
      return o;
    });
  }
  // resonance card modifiers: {cards, prop, add}
  function applyCardMods(ops, mods) {
    if (!mods.length) return ops;
    return mapOps(ops, (o) => {
      for (const m of mods) {
        if (m.prop === 'dmg' && o.op === 'dmg') for (const k of ['v', 'rv', 'ra']) if (typeof o[k] === 'number') o[k] += m.add;
        if (m.prop === 'heal' && o.op === 'heal') for (const k of ['v', 'rv', 'ra']) if (typeof o[k] === 'number') o[k] += m.add;
        if (m.prop === 'brk' && o.op === 'dmg') o.brk = (o.brk ?? 1) + m.add;
        if (m.prop === 'n' && (o.op === 'st' || o.op === 'counter')) { o.n += m.add; if (o.rn !== undefined) o.rn += m.add; }
        if (m.prop === 'draw' && o.op === 'draw') o.n += m.add;
        if (m.prop === 'selfdmg' && o.op === 'selfdmg') o.v = Math.max(0, o.v + m.add);
      }
      return o;
    });
  }

  // ───────────────────────── battle ─────────────────────────
  const ENEMY_ATTACK_MOVES = new Set(['atk', 'atkdebuff', 'expose']);

  function makeInst(state, ix, spec, extra = {}) {
    const def = ix.cards[spec.id];
    if (!def) throw new RuleError('UNKNOWN_CARD', spec.id);
    state.seq += 1;
    const uid = `c${state.seq}`;
    state.cards[uid] = {
      uid, id: spec.id, owner: def.owner, wielder: spec.wielder || null, plus: !!spec.plus,
      mods: { c: spec.mods?.c || null, e: spec.mods?.e || null }, temp: [], growth: spec.growth || 0, grown: false,
      used: 0, costMod: 0, token: !!extra.token, free: !!extra.free, assigned: spec.assigned || null, from: spec.from || null,
    };
    return uid;
  }

  class Battle {
    constructor(data, state) { this.ix = indexData(data); this.s = state; this.receipts = {}; }

    // setup: {party:[{charId, res, weaponId, refine, power4}], deck:[spec], relics:[], hp, maxHp, seed, runFlags:{}, enemies:[ids], nextApBonus}
    static create(data, setup) {
      const ix = indexData(data);
      const R = ix.rules;
      if (!setup.party || setup.party.length < 1 || setup.party.length > 3) throw new RuleError('PARTY_SIZE');
      const s = {
        schema: 'mf-battle-0.7', v: 0, turn: 0, phase: 'CREATED', rng: Rng.seed(setup.seed ?? 20261014), seq: 0,
        hp: setup.hp, maxHp: setup.maxHp, block: 0, counter: 0, ap: 0, apNext: setup.nextApBonus || 0, apDrainNext: 0, silenceUntil: 0, breakApTurn: 0,
        pst: { weak: 0 }, buffs: [], debts: [], debtCap: R.debtCap, restraint: { active: false, usedTurn: 0 }, bonus: { turn: 0, avail: false },
        drawPenalty: 0, sealedUntil: 0, slotsBase: { next: R.slots.next, after: R.slots.after }, slotsTemp: { next: 0, after: 0 },
        growthCount: 0, growthCap: R.growthCapPerBattle, played: 0, lastOwner: null, ownersThisTurn: [],
        echo: 0, echoMax: 3, echoActive: false, echoStart: 0, contracts: 0, exhausted: 0, exhaustStart: 0, tempParts: 0,
        freeNext: 0, nextBoost: null, firstAtkDone: false, overloadFreeUsed: false,
        actors: [], enemies: [], cards: {}, zones: { draw: [], hand: [], discard: [], exhaust: [] }, res: [], resSeq: 0,
        relics: [...(setup.relics || [])], powers: [], turnFlags: {}, battleFlags: {}, runFlags: { ...(setup.runFlags || {}) },
        handMax: R.handMax, log: [], ev: [], result: null, killedMarked: 0,
        diff: { hp: (setup.difficulty && setup.difficulty.hp) || 100, dmg: (setup.difficulty && setup.difficulty.dmg) || 100 },
      };
      // actors
      for (const p of setup.party) {
        const ch = ix.chars[p.charId];
        if (!ch) throw new RuleError('UNKNOWN_CHARACTER', p.charId);
        const res = Math.max(0, Math.min(6, p.res || 0));
        const passive = clone(ch.passive);
        const actor = { id: ch.id, name: ch.name, attr: ch.attr, gauge: p.gauge || 0, gaugeTurn: 0, res, power4: p.power4 || 160, vit: p.hp || 81, gearBps: p.gearBps || 0,
          skillLv: Math.max(1, p.skillLv || 1), ultLv: Math.max(1, p.ultLv || 1), passive, weapon: p.weaponId ? { id: p.weaponId, refine: Math.max(1, Math.min(5, p.refine || 1)) } : null,
          cardMods: [], battleStart: [], ultExtra: [], ultReplace: null, hooks: [], capacity: 0 };
        for (const node of ch.resonance.slice(0, res)) Battle.applyResonance(s, actor, node.fx);
        s.actors.push(actor);
      }
      if (s.actors.some((a) => a.id === 'rien')) { s.echoActive = true; s.echoMax = s.actors.find((a) => a.id === 'rien').passive.params.max; }
      if (s.relics.includes('tuning_fork')) { s.echoMax += 1; s.echoStart = Math.max(s.echoStart, 1); }
      if (s.relics.includes('contract_seal')) s.debtCap += 1;
      if (s.relics.includes('cracked_hourglass')) s.slotsBase.after = Math.min(R.slots.afterMax, s.slotsBase.after + 1);
      if (s.relics.includes('twin_pendulum')) { s.slotsBase.next = Math.min(R.slots.nextMax, s.slotsBase.next + 1); s.handMax = 9; }
      if (s.relics.includes('infinite_circuit')) s.slotsBase.next = Math.min(R.slots.nextMax, s.slotsBase.next + 1);
      // deck
      for (const spec of setup.deck) {
        const uid = makeInst(s, ix, spec);
        s.zones.draw.push(uid);
      }
      if (s.relics.includes('eye_of_rift')) s.zones.draw.push(makeInst(s, ix, { id: 'smudge' }, { token: true }));
      // enemies
      for (const eid of setup.enemies) Battle.addEnemy(s, ix, eid);
      const b = new Battle(ix, s);
      b.checkInvariants();
      return b;
    }

    static applyResonance(s, actor, fx) {
      if (!fx) return;
      switch (fx.type) {
        case 'multi': for (const f of fx.list) Battle.applyResonance(s, actor, f); break;
        case 'cardMod': actor.cardMods.push(fx); break;
        case 'passiveParam': actor.passive.params[fx.param] = fx.value; break;
        case 'skillLv': actor.skillLv += fx.delta; break;
        case 'ultLv': actor.ultLv += fx.delta; break;
        case 'capacity': actor.capacity += fx.n; break;
        case 'battleStart': actor.battleStart.push(...fx.eff); break;
        case 'ultExtra': actor.ultExtra.push({ eff: fx.eff, once: !!fx.once }); break;
        case 'ultReplace': actor.ultReplace = fx.eff; break;
        case 'hook': actor.hooks.push(fx); break;
        case 'rule':
          if (fx.key === 'growthCap') s.growthCap = Math.max(s.growthCap, fx.value);
          if (fx.key === 'debtCap') s.debtCap = Math.max(s.debtCap, fx.value);
          if (fx.key === 'echoStart') s.echoStart = Math.max(s.echoStart, fx.value);
          if (fx.key === 'exhaustStart') s.exhaustStart = Math.max(s.exhaustStart, fx.value);
          if (fx.key === 'firstOverloadFree') s.battleFlags.firstOverloadFree = true;
          break;
        default: break;
      }
    }

    static addEnemy(s, ix, eid) {
      const d = ix.enemies[eid];
      if (!d) throw new RuleError('UNKNOWN_ENEMY', eid);
      s.seq += 1;
      const hp = Math.max(1, Math.round(d.hp * ((s.diff && s.diff.hp) || 100) / 100));
      const e = { uid: `e${s.seq}`, id: eid, name: d.name, tier: d.tier, hp, maxHp: hp, block: 0, brkMax: d.brk, brk: d.brk,
        weak: [...d.weak], st: { vul: 0, weak: 0, mark: 0, burn: 0, str: 0, reflect: 0 }, brokenUntil: 0, stunned: false,
        pi: 0, intent: null, next: null, alive: true, passive: d.passive ? clone(d.passive) : null };
      s.enemies.push(e);
      return e;
    }

    // ───── accessors ─────
    get state() { return this.s; }
    actor(id) { return this.s.actors.find((a) => a.id === id) || null; }
    has(rel) { return this.s.relics.includes(rel); }
    alive() { return this.s.enemies.filter((e) => e.alive); }
    enemy(uid) { return this.s.enemies.find((e) => e.uid === uid) || null; }
    ownerChar(inst) { if (this.ix.chars[inst.owner]) return inst.owner; if (inst.owner === 'weapon') return inst.wielder; return null; }
    attrOf(inst) {
      const d = this.ix.cards[inst.id];
      if (d.attr === 'wielder') return this.actor(inst.wielder)?.attr || 'none';
      return d.attr || 'none';
    }
    debtTotal() { return this.s.debts.reduce((a, d) => a + d.n, 0); }
    ev(e, data = {}) { this.s.ev.push({ e, ...data }); this.s.log.push({ t: this.s.turn, e, ...data }); if (this.s.log.length > 400) this.s.log.shift(); }
    weaponPassive(actorId, pid) {
      const a = this.actor(actorId); if (!a || !a.weapon) return null;
      const w = this.ix.weapons[a.weapon.id]; if (!w || w.passive.id !== pid) return null;
      return { value: w.passive.values[a.weapon.refine - 1], alt: w.passive.refineAlt ? w.passive.refineAlt[a.weapon.refine - 1] : 0, scope: w.passive.scope };
    }
    partyWeapon(pid) { // party-scope passive (strongest instance)
      let best = null;
      for (const a of this.s.actors) { const p = this.weaponPassive(a.id, pid); if (p && (!best || p.value > best.value)) best = p; }
      return best;
    }
    passiveOf(id) { const a = this.s.actors.find((x) => x.passive.id === id); return a ? a.passive.params : null; }
    // party averages (rounded like JS Math.round) used for effects without an owning character
    partyP4() { const a = this.s.actors; return Math.round(a.reduce((t, x) => t + x.power4, 0) / Math.max(1, a.length)); }
    partyVit() { const a = this.s.actors; return Math.round(a.reduce((t, x) => t + (x.vit || 81), 0) / Math.max(1, a.length)); }
    // parity scaling for flat numbers: player damage ↔ power (÷160), player block/heal/HP costs ↔ vitality (÷81), enemy flat damage ↔ stage damage %
    scaleAtk(n) { return Math.floor(n * this.partyP4() / 160); }
    scaleVit(n) { return Math.floor(n * this.partyVit() / 81); }
    scaleEnemy(n) { return Math.round(n * ((this.s.diff && this.s.diff.dmg) || 100) / 100); }
    hasHook(actorId, hookId) { const a = this.actor(actorId); return !!a && a.hooks.some((h) => h.id === hookId); }
    buff(s) { return this.s.buffs.find((b) => b.s === s && b.until >= this.s.turn) || null; }

    // effective definition for an instance: upgrade, resonance mods, evolutions handled by id
    effDef(inst) {
      const base = this.ix.cards[inst.id];
      let eff = base.eff;
      if (inst.plus) eff = base.plus?.eff || upgradeOps(eff, this.ix.rules.upgradeMult);
      const oc = this.ownerChar(inst);
      const actor = oc ? this.actor(oc) : null;
      if (actor && actor.cardMods.length && inst.owner === oc) {
        const mods = actor.cardMods.filter((m) => m.cards.includes(inst.id) || m.cards.includes(base.id));
        if (mods.length) eff = applyCardMods(eff, mods);
      }
      let uses = base.uses;
      if (actor) for (const m of actor.cardMods) if (m.prop === 'uses' && m.cards.includes(base.id)) uses = (uses || 0) + m.add;
      return { ...base, eff, uses };
    }
    partsOf(inst) { return [inst.mods.c, inst.mods.e, ...inst.temp].filter(Boolean).map((p) => this.ix.parts[p]).filter(Boolean); }
    isModded(inst) { return this.partsOf(inst).length > 0; }

    costOf(uid, mode) {
      const s = this.s; const inst = s.cards[uid]; const def = this.effDef(inst);
      if (inst.free) return 0; // echo/copy tokens: cost 0 for their whole life (not reset at turn start)
      let cost = def.cost + inst.costMod;
      for (const p of this.partsOf(inst)) if (p.fx.cost) cost += p.fx.cost;
      if (def.costIf && this.cond(def.costIf.cond, { inst })) cost += def.costIf.delta;
      if (mode !== 'now' && this.has('empty_clock') && !s.battleFlags.emptyClockUsed) cost -= 1;
      if (mode !== 'now' && s.silenceUntil > 0 && s.silenceUntil >= s.turn) cost += 1;
      if (s.freeNext > 0) cost = 0;
      return Math.max(0, cost);
    }
    allowedModes(uid) {
      const inst = this.s.cards[uid]; const def = this.effDef(inst);
      if ((def.kw || []).includes('contract')) return ['now'];
      if (this.partsOf(inst).some((p) => p.fx.noReserve)) return def.res === 'n' || def.res === 'y' ? ['now'] : [];
      return { y: ['now', 'next', 'after'], n: ['now'], o: ['next', 'after'], after: ['after'] }[def.res] || ['now'];
    }
    needsTarget(uid) {
      const def = this.effDef(this.s.cards[uid]);
      let need = null;
      const walk = (ops) => { for (const o of ops) { if (o.tgt === 'fixed') need = 'fixed'; else if (o.tgt === 'one' && need !== 'fixed') need = 'one';
        else if ((o.op === 'dmg') && !o.tgt && need === null) need = 'one'; if (o.then) walk(o.then); if (o.else) walk(o.else); } };
      walk(def.eff);
      return need;
    }
    slotCapacity(kind) {
      const s = this.s; const R = this.ix.rules.slots;
      if (kind === 'next') {
        let cap = Math.min(R.nextMax, s.slotsBase.next + s.slotsTemp.next);
        if (s.sealedUntil >= s.turn) cap -= 1;
        return Math.max(0, cap);
      }
      return Math.min(R.afterMax, s.slotsBase.after + s.slotsTemp.after);
    }
    slotsUsed(kind) { const due = this.s.turn + (kind === 'next' ? 1 : 2); return this.s.res.filter((r) => r.due === due).length; }
    freeSlots(kind) { return this.slotCapacity(kind) - this.slotsUsed(kind); }
    nextPlayerSlotCapacity(due) {
      const s = this.s; const turn = s.turn + 1; const R = this.ix.rules.slots;
      // Temporary slots expire before the affected player turn.
      if (due === turn + 1) return Math.max(0, Math.min(R.nextMax, s.slotsBase.next) - (s.sealedUntil >= turn ? 1 : 0));
      return due === turn + 2 ? Math.min(R.afterMax, s.slotsBase.after) : 0;
    }
    previewDelay() {
      const s = this.s; const r = s.res.slice().sort((a, b) => b.order - a.order)[0];
      if (!r) return null;
      const to = r.due + 1;
      return { uid: r.uid, from: r.due, to, kind: r.kind,
        discarded: s.res.filter(x => x !== r && x.due === to).length >= this.nextPlayerSlotCapacity(to) };
    }
    nextTurnDrain() {
      const s = this.s;
      const pending = s.phase === 'PLAYER' ? this.alive().reduce((n, e) => {
        const pat = e.pattern || this.ix.enemies[e.id].pattern; const mv = pat[e.pi % pat.length];
        return n + (!e.stunned && mv.t === 'apDrain' ? mv.n : 0);
      }, 0) : 0;
      return (s.apDrainNext || 0) + pending;
    }
    nextTurnAp() {
      const s = this.s; const R = this.ix.rules;
      const due = s.debts.filter(d => d.due <= s.turn + 1).reduce((total, d) => total + d.n, 0);
      const ap = R.apPerTurn + s.apNext + (this.has('chrono_crown') ? 1 : 0)
        - (s.turn === 0 && this.has('cracked_hourglass') ? 1 : 0) - due - this.nextTurnDrain();
      return Math.max(0, Math.min(R.apMax, ap));
    }

    // ───── commands (atomic + idempotent) ─────
    command(req) {
      const digest = JSON.stringify({ v: req.v, kind: req.kind, args: req.args || {} });
      if (req.rid && Object.hasOwn(this.receipts, req.rid)) {
        if (this.receipts[req.rid].digest !== digest) throw new RuleError('IDEMPOTENCY_CONFLICT');
        return clone(this.receipts[req.rid].resp);
      }
      if (req.v !== this.s.v) throw new RuleError('STALE_VERSION');
      const cand = new Battle(this.ix, clone(this.s));
      cand.s.ev = [];
      const a = req.args || {};
      switch (req.kind) {
        case 'begin': cand.begin(); break;
        case 'play': cand.play(a.uid, a.mode || 'now', a.target || null, a.picks || []); break;
        case 'ult': cand.ult(a.actor, a.target || null, a.picks || []); break;
        case 'end': cand.endTurn(); break;
        default: throw new RuleError('UNKNOWN_COMMAND');
      }
      cand.s.v += 1;
      cand.checkInvariants();
      const resp = { v: cand.s.v, phase: cand.s.phase, ev: cand.s.ev };
      this.s = cand.s;
      if (req.rid) Object.defineProperty(this.receipts, req.rid, { value: { digest, resp: clone(resp) }, enumerable: true, writable: true, configurable: true });
      return resp;
    }
    // convenience for UIs and bots
    do(kind, args = {}) { return this.command({ rid: `r${this.s.v}-${kind}`, v: this.s.v, kind, args }); }

    // ───── turn flow ─────
    begin() {
      const s = this.s;
      if (s.phase !== 'CREATED') throw new RuleError('WRONG_PHASE');
      Rng.shuffle(s.rng, s.zones.draw);
      // battle-start effects (before the first draw)
      if (this.has('bronze_cog')) this.gainBlockRaw(8, 'relic');
      if (this.has('pocket_brazier')) { const t = this.randEnemy(); if (t) this.addStatus(t, 'burn', 4); }
      for (const a of s.actors) {
        const sb = this.weaponPassive(a.id, 'w_startBlock'); if (sb) this.gainBlockRaw(sb.value, 'weapon');
        for (const op of a.battleStart.filter((o) => o.op !== 'tune')) this.execOp(op, this.pseudoCtx(a));
      }
      if (this.has('eternal_seed')) {
        const gs = Object.values(s.cards).filter((c) => this.ix.cards[c.id].growth && c.growth < 3);
        if (gs.length) this.applyGrowth(Rng.pick(s.rng, gs), 1, { source: 'eternal_seed' });
      }
      this.startTurn(true);
    }

    startTurn(first = false) {
      const s = this.s; const R = this.ix.rules;
      const ap = this.nextTurnAp();
      s.turn += 1; s.phase = 'RESOLVING';
      // v0.7.1: battle-start effects belong to turn 1; clear only at subsequent turn boundaries.
      if (!first) {
        const keep = s.powers.filter((p) => p.id === 'bulwark').reduce((m, p) => Math.max(m, p.keep), 0);
        s.block = Math.min(s.block, keep);
        s.counter = 0;
      }
      s.buffs = s.buffs.filter((b) => b.until >= s.turn);
      s.played = 0; s.lastOwner = null; s.ownersThisTurn = []; s.contracts = 0; s.freeNext = 0; s.nextBoost = null;
      s.echo = s.echoActive ? Math.min(s.echoMax, s.echoStart) : 0;
      s.slotsTemp = { next: 0, after: 0 }; s.turnFlags = {}; s.breakApTurn = 0;
      for (const a of s.actors) a.gaugeTurn = 0;
      for (const c of Object.values(s.cards)) c.costMod = 0;
      // action points: base + carried bonus (+relics) - due debts
      s.apNext = 0;
      const due = s.debts.filter((d) => d.due <= s.turn).reduce((a, d) => a + d.n, 0);
      s.debts = s.debts.filter((d) => d.due > s.turn);
      s.apDrainNext = 0;
      s.ap = ap;
      if (due) this.ev('repay', { n: due });
      if (this.has('infinite_circuit')) { this.loseHp(this.scaleVit(2), 'relic'); if (this.checkEnd()) return; }
      // due reservations resolve in (due, order)
      const dueRes = s.res.filter((r) => r.due <= s.turn).sort((a, b) => a.due - b.due || a.order - b.order);
      s.res = s.res.filter((r) => r.due > s.turn);
      for (let i = 0; i < dueRes.length; i++) {
        const r = dueRes[i];
        this.resolveCard(r.uid, { reserved: r.kind, target: r.target, picks: [] });
        if (this.isOver() || this.checkEnd()) { for (const rest of dueRes.slice(i + 1)) s.zones.discard.push(rest.uid); return; }
      }
      s.battleFlags.seraR6Turn = 0;
      // turn-start effects
      for (const p of s.powers) if (p.id === 'rebirth') this.healParty(p.heal, null);
      // draw
      let n = R.handDraw - s.drawPenalty; s.drawPenalty = 0;
      if (first) {
        if (this.has('quick_hands')) n += 2;
        const sd = this.partyWeapon('w_startDraw'); if (sd) n += sd.value;
      }
      this.draw(Math.max(0, n));
      this.deliverCopies();
      if (first) {
        if (this.has('memory_anvil')) for (let k = 0; k < 2; k++) this.tuneRandom('amp');
        for (const a of s.actors) for (const op of a.battleStart.filter((o) => o.op === 'tune')) this.execOp(op, this.pseudoCtx(a));
      }
      this.setIntents();
      s.phase = 'PLAYER';
      this.ev('turn', { turn: s.turn, ap: s.ap });
    }

    setIntents() {
      for (const e of this.alive()) {
        const pat = e.pattern || this.ix.enemies[e.id].pattern;
        if (e.stunned) { e.intent = { t: 'stun', label: '균열: 행동 불가' }; e.next = pat[(e.pi + 1) % pat.length]; }
        else { e.intent = pat[e.pi % pat.length]; e.next = pat[(e.pi + 1) % pat.length]; }
      }
    }

    endTurn() {
      const s = this.s;
      if (s.phase !== 'PLAYER') throw new RuleError('WRONG_PHASE');
      s.phase = 'RESOLVING';
      if (s.restraint.active && s.played <= 3) { s.bonus = { turn: s.turn + 1, avail: true }; this.ev('restraintKept'); }
      else if (s.bonus.turn <= s.turn) s.bonus.avail = false;
      s.restraint.active = false;
      // hand cleanup
      for (const uid of [...s.zones.hand]) if (s.cards[uid].id === 'smudge') this.loseHp(this.scaleEnemy(2), 'smudge');
      s.zones.discard.push(...s.zones.hand); s.zones.hand = [];
      if (this.checkEnd()) return;
      if (s.pst.weak > 0) s.pst.weak -= 1;
      if (s.silenceUntil <= s.turn) s.silenceUntil = 0;
      // enemy phase
      for (const e of [...s.enemies]) {
        if (!e.alive) continue;
        e.block = 0; e.st.reflect = 0;
        const pat = e.pattern || this.ix.enemies[e.id].pattern;
        if (e.stunned) { e.stunned = false; e.pi = (e.pi + 1) % pat.length; this.ev('stunSkip', { uid: e.uid }); continue; }
        const mv = pat[e.pi % pat.length];
        e.pi = (e.pi + 1) % pat.length;
        this.enemyMove(e, mv);
        if (this.checkEnd()) return;
      }
      // round end
      for (const e of s.enemies) {
        if (!e.alive) continue;
        if (e.st.burn > 0) { this.damageEnemyRaw(e, e.st.burn, 'burn'); e.st.burn = Math.max(0, e.st.burn - 1); }
      }
      for (const e of s.enemies) {
        if (!e.alive || !e.passive) continue;
        if (e.passive.id === 'seedHeal') {
          const seeds = this.alive().filter((x) => x.id === 'e_seed').length;
          if (seeds) { const h = e.passive.n * seeds; e.hp = Math.min(e.maxHp, e.hp + h); this.ev('enemyHeal', { uid: e.uid, n: h }); }
        }
      }
      for (const e of s.enemies) {
        if (!e.alive) continue;
        for (const k of ['vul', 'weak', 'mark']) if (e.st[k] > 0) e.st[k] -= 1;
        if (e.brokenUntil && e.brokenUntil <= s.turn) { e.brokenUntil = 0; e.brk = e.brkMax; this.ev('recover', { uid: e.uid }); }
      }
      if (this.checkEnd()) return;
      this.startTurn(false);
    }

    enemyMove(e, mv) {
      const s = this.s;
      switch (mv.t) {
        case 'atk': { const phase = e.phaseIndex; for (let h = 0; h < (mv.h || 1); h++) { this.enemyHit(e, mv.d); if (this.checkEnd() || !e.alive || e.phaseIndex !== phase) return; } break; }
        case 'def': e.block += mv.b; if (mv.str) e.st.str += mv.str; break;
        case 'buff': e.st.str += mv.str; break;
        case 'debuff': s.pst[mv.s] = (s.pst[mv.s] || 0) + mv.n; this.ev('partyDebuff', { s: mv.s, n: mv.n }); break;
        case 'atkdebuff': { const phase = e.phaseIndex; for (let h = 0; h < (mv.h || 1); h++) { this.enemyHit(e, mv.d); if (this.checkEnd() || !e.alive || e.phaseIndex !== phase) return; } s.pst[mv.s] = (s.pst[mv.s] || 0) + mv.n; break; }
        case 'charge': this.ev('charge', { uid: e.uid }); break;
        case 'summon': for (let k = 0; k < mv.n; k++) if (this.alive().length < 5) { const m = Battle.addEnemy(s, this.ix, mv.e); this.setIntentsFor(m); } break;
        case 'heal': { const tgt = this.alive().sort((a, b) => a.hp / a.maxHp - b.hp / b.maxHp)[0]; if (tgt) { tgt.hp = Math.min(tgt.maxHp, tgt.hp + mv.n); this.ev('enemyHeal', { uid: tgt.uid, n: mv.n }); } break; }
        case 'insert': for (let k = 0; k < mv.n; k++) s.zones.discard.push(makeInst(s, this.ix, { id: mv.card }, { token: true })); this.ev('insert', { card: mv.card, n: mv.n }); break;
        case 'reflect': e.st.reflect = mv.n; break;
        case 'expose': e.st.vul += mv.n; this.ev('expose', { uid: e.uid }); if (mv.then) this.enemyMove(e, mv.then); break;
        case 'blockAll': for (const x of this.alive()) x.block += mv.b; break;
        case 'apDrain':
          if (!Number.isInteger(mv.n) || mv.n < 0) throw new RuleError('INVALID_ENEMY_MOVE');
          s.apDrainNext = (s.apDrainNext || 0) + mv.n;
          this.ev('apDrain', { uid: e.uid, n: mv.n }); break;
        case 'sealSlot': {
          const due = s.turn + 2;
          const fallback = s.sealedUntil >= s.turn + 1 || s.res.filter(r => r.due === due).length >= this.nextPlayerSlotCapacity(due);
          if (fallback) e.block += 10; else s.sealedUntil = s.turn + 1;
          this.ev('sealSlot', { uid: e.uid, until: s.sealedUntil, fallback }); break;
        }
        case 'delayRes': {
          const preview = this.previewDelay();
          if (!preview) { this.ev('delayRes', { uid: null, discarded: false }); break; }
          const r = s.res.find(x => x.uid === preview.uid);
          if (preview.discarded) { s.res.splice(s.res.indexOf(r), 1); s.zones.discard.push(r.uid); }
          else { r.due = preview.to; r.delayed = true; }
          this.ev('delayRes', preview); break;
        }
        case 'silence':
          if (mv.n !== undefined && mv.n !== 1) throw new RuleError('INVALID_ENEMY_MOVE');
          s.silenceUntil = Math.max(s.silenceUntil || 0, s.turn + 1);
          this.ev('silence', { uid: e.uid, until: s.silenceUntil }); break;
        case 'eatName': {
          const tokens = s.zones.discard.filter(uid => s.cards[uid].token);
          const pool = tokens.length ? tokens : s.zones.discard;
          const uid = pool.length ? Rng.pick(s.rng, pool) : null;
          if (uid) this.exhaustCard(uid);
          this.ev('eatName', { uid }); break;
        }
        case 'phase': {
          if (e.tier !== 'boss' || e.hp !== 0 || e.phaseIndex) throw new RuleError('WRONG_PHASE');
          if (!Number.isInteger(mv.hp) || mv.hp <= 0 || !Number.isInteger(mv.brk) || mv.brk <= 0 ||
              !Array.isArray(mv.weak) || !mv.weak.length || new Set(mv.weak).size !== mv.weak.length || !mv.weak.every(a => this.ix.raw.attributes.some(x => x.id === a)) ||
              !Array.isArray(mv.pattern) || !mv.pattern.length || mv.pattern.some(m => !m || typeof m.t !== 'string' || m.t === 'phase')) throw new RuleError('INVALID_ENEMY_MOVE');
          e.phaseIndex = 1; e.maxHp = Math.max(1, Math.round(mv.hp * ((s.diff && s.diff.hp) || 100) / 100)); e.hp = e.maxHp;
          e.brkMax = mv.brk; e.brk = mv.brk; e.weak = [...mv.weak]; e.pattern = clone(mv.pattern); e.pi = 0;
          e.block = 0; e.st = { vul: 0, weak: 0, mark: 0, burn: 0, str: 0, reflect: 0 }; e.stunned = false; e.brokenUntil = 0;
          this.setIntentsFor(e); this.ev('phase', { uid: e.uid, index: 1, hp: e.hp, brk: e.brk }); break;
        }
        default: throw new RuleError('UNSUPPORTED_EFFECT', { enemyMove: mv.t });
      }
    }
    setIntentsFor(e) { const pat = e.pattern || this.ix.enemies[e.id].pattern; e.intent = pat[e.pi % pat.length]; e.next = pat[(e.pi + 1) % pat.length]; }

    enemyHit(e, base) {
      const s = this.s;
      let d = Math.round(base * ((s.diff && s.diff.dmg) || 100) / 100) + (e.st.str || 0);
      if (e.st.weak > 0) d = Math.floor(d * (BPS - this.ix.rules.weakBps) / BPS);
      const blocked = Math.min(s.block, d); s.block -= blocked;
      const loss = d - blocked;
      this.ev('hit', { uid: e.uid, d, blocked });
      if (loss > 0) this.loseHp(loss, 'enemy');
      if (s.counter > 0 && e.alive) {
        const dbl = this.buff('counterDouble') ? 2 : 1;
        this.damageEnemyRaw(e, s.counter * dbl, 'counter', true);
      }
    }

    loseHp(n, src) {
      const s = this.s;
      s.hp = Math.max(0, s.hp - n);
      if (s.hp === 0 && this.has('memory_envelope') && !s.runFlags.envelopeUsed) { s.hp = 1; s.runFlags.envelopeUsed = true; this.ev('envelope'); }
      this.ev('hpLoss', { n, src });
    }

    isOver() { return this.s.phase === 'VICTORY' || this.s.phase === 'DEFEAT'; }
    checkEnd() {
      const s = this.s;
      if (this.isOver()) return true;
      if (s.hp <= 0) { s.phase = 'DEFEAT'; this.finish(); return true; }
      if (this.alive().length === 0) { s.phase = 'VICTORY'; this.finish(); return true; }
      return false;
    }
    finish() {
      const s = this.s;
      // preserve card zones: pending reservations go to discard, temporary run-only effects end
      for (const r of s.res) s.zones.discard.push(r.uid);
      s.res = []; s.debts = []; s.restraint.active = false; s.bonus.avail = false;
      s.result = s.result || { phase: s.phase, turn: s.turn, hp: s.hp };
      this.ev('end', { phase: s.phase });
    }

    // ───── play ─────
    // Every rule a play must satisfy before anything is paid. Pure: never mutates state. Returns the resolved target.
    precheck(uid, mode, target, picks) {
      const s = this.s;
      if (s.phase !== 'PLAYER') throw new RuleError('WRONG_PHASE');
      if (!['now', 'next', 'after'].includes(mode)) throw new RuleError('INVALID_MODE');
      if (!s.zones.hand.includes(uid)) throw new RuleError('NOT_IN_HAND');
      const inst = s.cards[uid]; const def = this.effDef(inst);
      if (!this.allowedModes(uid).includes(mode)) throw new RuleError(mode === 'now' ? 'RESERVE_ONLY' : 'NOT_RESERVABLE');
      const cost = this.costOf(uid, mode);
      if (s.ap < cost) throw new RuleError('AP_LOW', { need: cost, have: s.ap });
      const need = this.needsTarget(uid);
      if (need) {
        const alive = this.alive();
        if (!target && alive.length === 1) target = alive[0].uid;
        const t = this.enemy(target);
        if (!t || !t.alive) throw new RuleError('BAD_TARGET');
        if (need === 'fixed' && t.st.mark <= 0) throw new RuleError('FIXED_MARK_REQUIRED');
      } else target = null;
      this.validateOps(def.eff, inst, mode, picks || []);
      if (mode !== 'now' && this.freeSlots(mode) <= 0) throw new RuleError('SLOT_FULL');
      return target;
    }
    // UI/bot helper: null when the play is legal, otherwise the RuleError code. target 'auto' picks any legal enemy.
    check(uid, mode, target, picks) {
      try {
        if (target === 'auto') {
          const need = this.s.cards[uid] ? this.needsTarget(uid) : null;
          const alive = this.alive();
          const t = need === 'fixed' ? alive.find((e) => e.st.mark > 0) : alive[0];
          target = t ? t.uid : null;
        }
        this.precheck(uid, mode, target, picks);
        return null;
      } catch (e) { if (e instanceof RuleError) return e.code; throw e; }
    }
    play(uid, mode, target, picks) {
      const s = this.s;
      target = this.precheck(uid, mode, target, picks);
      const inst = s.cards[uid]; const def = this.effDef(inst);
      const cost = this.costOf(uid, mode);
      // commit
      s.ap -= cost;
      if (s.freeNext > 0) s.freeNext -= 1;
      if (mode !== 'now' && this.has('empty_clock') && !s.battleFlags.emptyClockUsed) s.battleFlags.emptyClockUsed = true;
      s.zones.hand.splice(s.zones.hand.indexOf(uid), 1);
      s.played += 1;
      const oc = this.ownerChar(inst);
      if (oc) {
        if (s.echoActive && s.lastOwner && s.lastOwner !== oc) { s.echo = Math.min(s.echoMax, s.echo + 1); this.echoCheck(); }
        s.lastOwner = oc;
        if (!s.ownersThisTurn.includes(oc)) s.ownersThisTurn.push(oc);
        if (this.has('unfinished_score') && s.ownersThisTurn.length >= 3 && !s.turnFlags.score) { s.turnFlags.score = true; this.draw(1); }
      }
      if ((def.kw || []).includes('contract')) s.contracts += 1;
      if (s.restraint.active && s.played >= 4) { s.restraint.active = false; s.drawPenalty += 1; this.ev('restraintBroken'); }
      this.ev('play', { uid, id: inst.id, mode, cost, target });
      if (mode === 'now') {
        this.resolveCard(uid, { reserved: null, target, picks });
      } else {
        s.resSeq += 1;
        s.res.push({ uid, due: s.turn + (mode === 'next' ? 1 : 2), order: s.resSeq, target, kind: mode });
        this.ev('reserve', { uid, due: s.turn + (mode === 'next' ? 1 : 2), kind: mode });
      }
      this.checkEnd();
    }

    validateOps(ops, inst, mode, picks) {
      const s = this.s;
      for (const op of ops) {
        if (op.op === 'loan') {
          if (this.debtTotal() + op.n > s.debtCap) throw new RuleError('DEBT_CAP');
          if (s.ap - this.costOf(inst.uid, mode) + op.n > this.ix.rules.apMax) throw new RuleError('LOAN_OVERFLOW');
        }
        if (op.op === 'defer') { if (!s.debts.some((d) => d.due === s.turn + 1 && !d.deferred)) throw new RuleError('NOT_DEFERRABLE'); if (this.debtTotal() + 1 > s.debtCap) throw new RuleError('DEBT_CAP'); }
        if (op.op === 'restraint' && (s.played >= 3 || s.restraint.usedTurn === s.turn)) throw new RuleError('RESTRAINT_UNAVAILABLE');
        if (op.op === 'collateral' && (s.sealedUntil >= s.turn || this.freeSlots('next') <= 0)) throw new RuleError('NO_EMPTY_SLOT');
        if (op.op === 'pull' && !s.res.length) throw new RuleError('RESERVATION_REQUIRED');
        if (op.op === 'pull') {
          const r = this.pickReservation(picks);
          if (r && r.due > s.turn + 1 && this.freeSlots('next') <= 0) throw new RuleError('PULL_DESTINATION_FULL');
        }
        if (op.op === 'selfdmg') { const cost = this.overloadCost(op.v, true); if (s.hp - cost < 1) throw new RuleError('OVERLOAD_WOULD_KILL'); }
        if (op.then) this.validateOps(op.then, inst, mode, picks);
      }
    }
    pickReservation(picks) {
      const s = this.s;
      const byPick = picks && picks.length ? s.res.find((r) => r.uid === picks[0]) : null;
      if (byPick) return byPick;
      const after = s.res.filter((r) => r.due > s.turn + 1).sort((a, b) => a.order - b.order);
      if (after.length) return after[0];
      return s.res.slice().sort((a, b) => a.order - b.order)[0] || null;
    }
    overloadCost(v, peek) {
      const s = this.s;
      if (this.buff('overloadFree')) { if (!peek) s.buffs = s.buffs.filter((b) => b.s !== 'overloadFree'); return 0; }
      if (s.battleFlags.firstOverloadFree && !s.overloadFreeUsed) { if (!peek) s.overloadFreeUsed = true; return 0; }
      return this.scaleVit(v); // overload is an HP cost: scales with party vitality
    }

    // ───── ultimates ─────
    ult(actorId, target, picks) {
      const s = this.s;
      if (s.phase !== 'PLAYER') throw new RuleError('WRONG_PHASE');
      const a = this.actor(actorId);
      if (!a) throw new RuleError('UNKNOWN_ACTOR');
      if (a.gauge < this.ix.rules.gaugeMax) throw new RuleError('ULT_NOT_READY');
      const ch = this.ix.chars[a.id];
      const eff = a.ultReplace || ch.ult.eff;
      const needs = eff.some((o) => o.tgt === 'one' || (o.op === 'dmg' && !o.tgt));
      if (needs) { const alive = this.alive(); if (!target && alive.length === 1) target = alive[0].uid; const t = this.enemy(target); if (!t || !t.alive) throw new RuleError('BAD_TARGET'); }
      a.gauge -= this.ix.rules.gaugeMax;
      const ctx = { inst: null, def: null, actor: a, attr: a.attr, reserved: null, target, lastTarget: target, picks: picks || [], isUlt: true, killed: false, lastCount: 0 };
      this.ev('ult', { actor: a.id });
      for (const op of eff) { this.execOp(op, ctx); if (this.checkEnd()) return; }
      for (const x of a.ultExtra) {
        if (x.once) { if (s.battleFlags[`ultOnce_${a.id}`]) continue; s.battleFlags[`ultOnce_${a.id}`] = true; }
        for (const op of x.eff) { this.execOp(op, ctx); if (this.checkEnd()) return; }
      }
      if (this.hasHook(a.id, 'sera_r6')) s.battleFlags.seraR6Turn = s.turn + 1;
      if (this.hasHook(a.id, 'kaya_r6') && ctx.killed) a.gauge = Math.min(this.ix.rules.gaugeMax, a.gauge + 50);
      this.checkEnd();
    }

    pseudoCtx(actor) { return { inst: null, def: null, actor, attr: actor.attr, reserved: null, target: null, lastTarget: null, picks: [], isUlt: false, killed: false, lastCount: 0, pseudo: true }; }

    // ───── card resolution ─────
    resolveCard(uid, opts) {
      const s = this.s;
      const inst = s.cards[uid]; const def = this.effDef(inst);
      const oc = this.ownerChar(inst);
      const actor = oc ? this.actor(oc) : null;
      const ctx = { inst, def, actor, attr: this.attrOf(inst), reserved: opts.reserved, target: opts.target, lastTarget: opts.target,
        picks: [...(opts.picks || [])], isUlt: false, killed: false, lastCount: 0, restraintBps: 0, boostBps: 0, chargeTaken: false, firstDmg: true };
      // fixed-target validation at resolution: dead or unmarked target fizzles (no refund)
      const hasFixed = JSON.stringify(def.eff).includes('"tgt":"fixed"');
      if (hasFixed) {
        const t = this.enemy(opts.target);
        if (!t || !t.alive || t.st.mark <= 0) { this.ev('fizzle', { uid }); this.afterResolve(inst, def, ctx, true); return; }
      } else if (opts.reserved && opts.target) {
        const t = this.enemy(opts.target);
        if (!t || !t.alive) { const alt = this.alive()[0]; ctx.target = alt ? alt.uid : null; ctx.lastTarget = ctx.target; }
      }
      // restraint bonus: first reserved damage/block card on the bonus turn
      if (opts.reserved && s.bonus.avail && s.bonus.turn === s.turn && def.eff.some((o) => o.op === 'dmg' || o.op === 'blk')) { ctx.restraintBps = 3000; s.bonus.avail = false; }
      // rien duet boost: next card of a different character
      if (s.nextBoost && oc && oc !== s.nextBoost.from && !opts.reserved) { ctx.boostBps = s.nextBoost.bps; s.nextBoost = null; }
      // ultimate gauge
      if (actor) this.gainGauge(actor, this.ix.rules.gaugePerCard);
      if (actor && opts.reserved && this.has('time_stamp')) this.gainGauge(actor, 5, true);
      for (const op of def.eff) { this.execOp(op, ctx); if (this.isOver() || this.checkEnd()) break; }
      if (!this.isOver()) {
        // engraving / circuit after-effects
        for (const p of this.partsOf(inst)) {
          if (p.fx.extraBlock) this.gainBlockRaw(p.fx.extraBlock, 'part');
          if (p.fx.drawOnce && !s.turnFlags[`recall_${uid}`]) { s.turnFlags[`recall_${uid}`] = true; this.draw(p.fx.drawOnce); }
          if (p.fx.gauge && actor) this.gainGauge(actor, p.fx.gauge, true);
          // echo circuit: the copy is created at the start of the NEXT turn (no card instance exists until then,
          // so it can never be shuffled into the draw pile)
          if (p.fx.echoCopy && opts.reserved) { s.pendingCopies = s.pendingCopies || []; s.pendingCopies.push({ id: inst.id, wielder: inst.wielder, from: inst.from, due: s.turn + 1 }); }
        }
        // character passives tied to resolution
        if (this.isGrowthInst(inst)) this.onGrowthCardResolved(inst);
        if (opts.reserved && inst.id === 'sera_round' && this.hasHook('sera', 'sera_r1') && !s.turnFlags.seraR1) { s.turnFlags.seraR1 = true; this.draw(1); }
      }
      this.afterResolve(inst, def, ctx, false);
    }

    afterResolve(inst, def, ctx, fizzled) {
      const s = this.s; const uid = inst.uid;
      inst.used += 1;
      const exhaustKw = (def.kw || []).includes('exhaust') || this.partsOf(inst).some((p) => p.fx.exhaust) || inst.echoCopy;
      const usedUp = def.uses && inst.used >= def.uses;
      if (exhaustKw || usedUp) this.exhaustCard(uid);
      else s.zones.discard.push(uid);
      this.ev('resolved', { uid, id: inst.id, reserved: ctx.reserved, fizzled });
    }

    // Growth-family identity is distinct from eligibility to grow again.
    // An evolved card is mature (3) but cannot gain growth or trigger moss merely for being loaded.
    growthRoot(inst) {
      if (!inst) return null;
      const d = this.ix.cards[inst.id];
      if (d && d.growth) return d;
      const root = this.ix.cards[inst.from || ''];
      return root && root.growth && (root.evo || []).includes(inst.id) ? root : null;
    }
    isGrowthInst(inst) { return !!this.growthRoot(inst); }
    effGrowth(inst) {
      if (!this.isGrowthInst(inst)) return 0;
      return this.ix.cards[inst.id].growth ? (inst.growth || 0) : 3;
    }
    applyGrowth(inst, amount, opts = {}) {
      const s = this.s;
      if (!inst || !this.ix.cards[inst.id]?.growth || !Number.isInteger(amount) || amount <= 0) return 0;
      if (opts.capped && s.growthCount >= s.growthCap) return 0;
      if (opts.once && inst.grown) return 0;
      const delta = Math.min(3 - inst.growth, amount);
      if (delta <= 0) return 0;
      inst.growth += delta;
      if (opts.capped) s.growthCount += 1;
      if (opts.once) inst.grown = true;
      this.ev('grow', { uid: inst.uid, growth: inst.growth, src: opts.source || 'card' });
      if (this.has('symbiotic_moss')) this.gainBlockRaw(3, 'moss');
      return delta;
    }

    onGrowthCardResolved(inst) {
      const s = this.s;
      const yuri = this.passiveOf('yuri_symbiosis');
      const g = this.effGrowth(inst);
      if (yuri && g > 0) this.gainBlockRaw(g * yuri.perGrowth, 'yuri');
      for (const p of s.powers) if (p.id === 'greenhouse') this.gainBlockRaw(p.blk, 'greenhouse');
      const gh = this.partyWeapon('w_growthHeal'); if (gh) this.healParty(gh.value, null, true);
    }

    exhaustCard(uid) {
      const s = this.s;
      for (const z of ['hand', 'discard', 'draw']) { const i = s.zones[z].indexOf(uid); if (i >= 0) s.zones[z].splice(i, 1); }
      s.zones.exhaust.push(uid);
      s.exhausted += 1;
      this.ev('exhaust', { uid });
      for (const p of s.powers) if (p.id === 'oblivion') this.gainBlockRaw(p.blk, 'oblivion');
      const eg = this.partyWeapon('w_exhaustGuard'); if (eg) this.gainBlockRaw(eg.value, 'weapon');
      if (this.has('void_shard')) { const t = this.randEnemy(); if (t) this.damageEnemyRaw(t, 3, 'relic'); }
      if (this.has('oblivion_ink') && !s.turnFlags.ink && s.phase === 'PLAYER') { s.turnFlags.ink = true; s.ap = Math.min(this.ix.rules.apMax, s.ap + 1); }
    }

    // ───── effect ops ─────
    value(op, ctx, key) {
      const k = key || 'v';
      let v = op[k];
      if (ctx.reserved === 'next' && op[k === 'v' ? 'rv' : 'r' + k] !== undefined) v = op[k === 'v' ? 'rv' : 'r' + k];
      if (ctx.reserved === 'after') { const ak = k === 'v' ? 'ra' : 'ra' + k; const rk = k === 'v' ? 'rv' : 'r' + k; v = op[ak] !== undefined ? op[ak] : (op[rk] !== undefined ? op[rk] : v); }
      return v ?? 0;
    }
    counterVal(per, ctx) {
      const s = this.s;
      switch (per) {
        case 'echo': return s.echo;
        case 'exhausted': return s.exhausted + s.exhaustStart;
        case 'debt': return this.debtTotal();
        case 'growthSum': return Object.values(s.cards).filter((c) => this.isGrowthInst(c)).reduce((a, c) => a + this.effGrowth(c), 0);
        case 'growthCards': return Object.values(s.cards).filter((c) => this.isGrowthInst(c)).length;
        case 'counter': return s.counter;
        case 'missingHp10': return Math.floor((s.maxHp - s.hp) / 10);
        case 'contracts': return s.contracts;
        case 'tempParts': return s.tempParts;
        case 'block': return s.block;
        case 'lastCount': return ctx.lastCount || 0;
        case 'targetBurn': { const t = this.enemy(ctx.lastTarget); return t ? t.st.burn : 0; }
        default: return 0;
      }
    }
    scaled(base, sc, ctx) { if (!sc) return base; let add = sc.k * this.counterVal(sc.per, ctx); if (sc.max !== undefined) add = Math.min(add, sc.max); return base + add; }

    cond(c, ctx) {
      const s = this.s; const t = ctx && ctx.lastTarget ? this.enemy(ctx.lastTarget) : null;
      switch (c.c) {
        case 'marked': return !!t && t.st.mark > 0;
        case 'burning': return !!t && t.st.burn > 0;
        case 'broken': return !!t && t.brokenUntil > 0;
        case 'debt': return this.debtTotal() > 0;
        case 'modded': return !!ctx.inst && this.isModded(ctx.inst);
        case 'reserved': return !!ctx.reserved;
        case 'intentAttack': return !!t && t.intent && ENEMY_ATTACK_MOVES.has(t.intent.t);
        case 'echo': return s.echo >= c.n;
        case 'hpBelow': return s.hp * 100 <= s.maxHp * c.pct;
        case 'targetHpBelow': return !!t && t.hp * 100 <= t.maxHp * c.pct;
        case 'growthInHand': return s.zones.hand.filter((u) => this.isGrowthInst(s.cards[u])).length >= c.n;
        case 'exhaustedAtLeast': return s.exhausted + s.exhaustStart >= c.n;
        case 'killedThis': return !!ctx.killed;
        default: return false;
      }
    }

    targetsFor(tgt, ctx) {
      const alive = this.alive();
      switch (tgt || 'one') {
        case 'all': return alive;
        case 'same': { const t = this.enemy(ctx.lastTarget); return t && t.alive ? [t] : (alive[0] ? [alive[0]] : []); }
        case 'rand': return alive.length ? [Rng.pick(this.s.rng, alive)] : [];
        case 'fixed': { const t = this.enemy(ctx.target); return t && t.alive && t.st.mark > 0 ? [t] : []; }
        case 'one': default: { const t = this.enemy(ctx.target); if (t && t.alive) return [t]; return alive[0] ? [alive[0]] : []; }
      }
    }

    // additive damage bonus (bps) for a hit
    dmgBonusBps(ctx, enemy) {
      const s = this.s; let add = 0;
      const inst = ctx.inst; const oc = ctx.actor ? ctx.actor.id : null;
      if (inst) {
        for (const p of this.partsOf(inst)) {
          if (p.fx.addBps) add += p.fx.addBps;
          if (p.fx.allBps) add += p.fx.allBps;
          if (ctx.reserved && p.fx.resBps) add += p.fx.resBps;
          if (!ctx.reserved && p.fx.immBps) add += p.fx.immBps;
          if (ctx.reserved === 'after' && p.fx.afterBps) add += p.fx.afterBps;
        }
        if (inst.temp.includes('echoHalf')) add -= 5000;
        if (this.isModded(inst)) {
          const nia = this.passiveOf('nia_handiwork'); if (nia) add += nia.bps;
          for (const p of s.powers) if (p.id === 'blueprint') add += p.bps;
          if (this.has('golden_quill')) add += 1500;
        }
        if (inst.from && this.hasHook('yuri', 'yuri_r4') && inst.owner === 'yuri') add += 1500;
        // weapon (wielder-scope) bonuses
        const wid = oc;
        if (wid) {
          const rd = this.weaponPassive(wid, 'w_reservedDmg'); if (rd && ctx.reserved) add += rd.value;
          const md = this.weaponPassive(wid, 'w_markedDmg'); if (md && enemy && enemy.st.mark > 0) add += md.value;
          const lh = this.weaponPassive(wid, 'w_lowHpDmg'); if (lh && s.hp * 2 <= s.maxHp) add += lh.value;
          const bd = this.weaponPassive(wid, 'w_brokenDmg') || this.weaponPassive(wid, 'w_breakDmg'); if (bd && enemy && enemy.brokenUntil) add += bd.value;
          const na = this.weaponPassive(wid, 'w_none'); if (na) add += na.alt;
          const fa = this.weaponPassive(wid, 'w_firstAtk'); if (fa && !s.firstAtkDone) add += fa.value;
        }
      }
      const dd = this.partyWeapon('w_debtDmg'); if (dd && this.debtTotal() > 0) add += dd.value;
      if (ctx.actor && ctx.actor.gearBps) add += ctx.actor.gearBps; // accessory (장신구)
      add += (ctx.restraintBps || 0) + (ctx.boostBps || 0);
      if (ctx.chargeBps) add += ctx.chargeBps;
      if (oc === 'sera' && ctx.reserved) {
        const p = this.passiveOf('sera_foresight'); if (p && enemy && enemy.brokenUntil) add += p.bps;
        const lock = this.buff('seraLock'); if (lock) add += lock.n;
        if (s.battleFlags.seraR6Turn && s.battleFlags.seraR6Turn === s.turn && s.phase === 'RESOLVING') add += 5000;
      }
      if (oc === 'ravien') { const p = this.passiveOf('ravien_credit'); if (p && this.debtTotal() > 0) add += p.bps; }
      if (oc === 'sion') { const p = this.passiveOf('sion_shelf'); if (p) add += Math.min(p.maxBps, p.perBps * (s.exhausted + s.exhaustStart)); }
      if (oc === 'lumi') {
        const p = this.passiveOf('lumi_critical'); if (p && s.hp * 100 <= s.maxHp * p.pct) add += p.bps;
        const rush = this.buff('lumiRush'); if (rush) add += rush.n;
      }
      for (const p of s.powers) if (p.id === 'casefile' && enemy && enemy.st.mark > 0) add += p.bps;
      if (this.has('ember_ring') && enemy && enemy.st.burn > 0) add += 1000;
      if (ctx.creditLedger) add += 2000;
      return add;
    }

    calcDamage(op, ctx, enemy, baseOverride) {
      const s = this.s; const R = this.ix.rules;
      let base = baseOverride !== undefined ? baseOverride : this.scaled(this.value(op, ctx), op.scale, ctx);
      if (ctx.flatBonus) base += ctx.flatBonus;
      if (base <= 0) return 0;
      const actor = ctx.actor;
      let num = BigInt(base);
      let den = 1n;
      // power factor (reference power 40 => power4 160)
      const p4 = actor ? actor.power4 : this.partyP4();
      num *= BigInt(p4); den *= 160n;
      // skill / ultimate level (+4% per level above 1)
      let lv = 1;
      if (ctx.isUlt && actor) lv = actor.ultLv;
      else if (actor && ctx.inst && ctx.inst.owner === actor.id) lv = actor.skillLv;
      num *= BigInt(BPS + 400 * (lv - 1)); den *= BigInt(BPS);
      const add = this.dmgBonusBps(ctx, enemy);
      num *= BigInt(Math.max(0, BPS + add)); den *= BigInt(BPS);
      if (enemy && enemy.st.vul > 0) { num *= BigInt(BPS + R.vulnerableBps); den *= BigInt(BPS); }
      if (enemy && enemy.brokenUntil) { num *= BigInt(BPS + R.brokenDamageBps); den *= BigInt(BPS); }
      if (s.pst.weak > 0) { num *= BigInt(BPS - R.weakBps); den *= BigInt(BPS); }
      for (const m of op.mult || []) if (this.cond(m.if, { ...ctx, lastTarget: enemy ? enemy.uid : ctx.lastTarget })) num *= BigInt(m.x);
      return Number(num / den);
    }
    calcBlock(op, ctx, rawBase) {
      const s = this.s;
      let base = rawBase !== undefined ? rawBase : this.scaled(this.value(op, ctx), op.scale, ctx);
      if (base <= 0) return 0;
      const actor = ctx.actor; let num = BigInt(base); let den = 1n;
      // defence scales with vitality (character HP / 81), so block keeps pace with stage-scaled enemy damage
      const vit = actor ? actor.vit : this.partyVit(); num *= BigInt(vit); den *= 81n;
      let lv = 1; if (ctx.isUlt && actor) lv = actor.ultLv; else if (actor && ctx.inst && ctx.inst.owner === actor.id) lv = actor.skillLv;
      num *= BigInt(BPS + 400 * (lv - 1)); den *= BigInt(BPS);
      let add = (ctx.restraintBps || 0) + (ctx.boostBps || 0);
      if (actor && actor.gearBps) add += actor.gearBps; // accessory (장신구)
      const inst = ctx.inst;
      if (inst) {
        for (const p of this.partsOf(inst)) {
          if (p.fx.addBps && !p.fx.exhaust) add += p.fx.addBps;
          if (p.fx.allBps) add += p.fx.allBps;
          if (ctx.reserved && p.fx.resBps) add += p.fx.resBps;
          if (!ctx.reserved && p.fx.immBps) add += p.fx.immBps;
          if (ctx.reserved === 'after' && p.fx.afterBps) add += p.fx.afterBps;
        }
        if (inst.temp.includes('echoHalf')) add -= 5000;
        if (this.isModded(inst)) {
          const nia = this.passiveOf('nia_handiwork'); if (nia) add += nia.bps;
          for (const p of s.powers) if (p.id === 'blueprint') add += p.bps;
          if (this.has('golden_quill')) add += 1500;
        }
        if (inst.from && this.hasHook('yuri', 'yuri_r4') && inst.owner === 'yuri') add += 1500;
        if (actor) { const bu = this.weaponPassive(actor.id, 'w_blockUp'); if (bu) add += bu.value; const na = this.weaponPassive(actor.id, 'w_none'); if (na) add += na.alt; }
      }
      num *= BigInt(Math.max(0, BPS + add)); den *= BigInt(BPS);
      return Number(num / den);
    }

    gainBlockRaw(n, src) {
      // card block (calcBlock) and heal overflow are already scaled; flat block from relics/parts/passives/contracts scales with party vitality
      if (src !== 'card' && src !== 'warmth') n = this.scaleVit(n);
      this.s.block += n; if (n) this.ev('block', { n, src });
    }
    gainGauge(actor, n, bypassCap) {
      const R = this.ix.rules;
      const gu = this.weaponPassive(actor.id, 'w_gaugeUp');
      let g = gu ? Math.floor(n * (BPS + gu.value) / BPS) : n;
      if (!bypassCap) g = Math.min(g, R.gaugeTurnCap - actor.gaugeTurn);
      if (g <= 0) return;
      if (!bypassCap) actor.gaugeTurn += g;
      actor.gauge = Math.min(R.gaugeMax, actor.gauge + g);
    }
    healParty(n, op, fromWeapon) {
      const s = this.s;
      if (s.hp <= 0) return 0;
      let amt = (op && op.preScaled) ? n : this.scaleVit(n); // flat heals from powers/relics/weapons scale with party vitality
      const hu = this.partyWeapon('w_healUp'); if (hu && !fromWeapon) amt = Math.floor(amt * (BPS + hu.value) / BPS);
      const before = s.hp; s.hp = Math.min(s.maxHp, s.hp + amt);
      const overflow = amt - (s.hp - before);
      const aila = this.passiveOf('aila_warmth');
      let pct = aila ? aila.pct : 0;
      if (op && op.overflowBlock) pct = Math.max(pct, op.overflowBlock);
      if (op && op.ultAila) pct = 100;
      if (overflow > 0 && pct > 0) this.gainBlockRaw(Math.floor(overflow * pct / 100), 'warmth');
      this.ev('heal', { n: s.hp - before });
      return overflow;
    }
    addStatus(e, k, n) { e.st[k] = (e.st[k] || 0) + n; this.ev('status', { uid: e.uid, s: k, n }); }
    randEnemy() { const a = this.alive(); return a.length ? Rng.pick(this.s.rng, a) : null; }

    damageEnemyRaw(e, n, src, noBreak) {
      n = this.scaleAtk(n); // counter / burn / relic damage scale with party power
      if (!e.alive || n <= 0) return;
      const blocked = src === 'burn' ? 0 : Math.min(e.block, n);
      e.block -= blocked; e.hp = Math.max(0, e.hp - (n - blocked));
      this.ev('dmg', { uid: e.uid, n, blocked, src });
      if (e.hp === 0) this.killEnemy(e, null);
    }
    killEnemy(e, ctx) {
      const s = this.s;
      const phase = this.ix.enemies[e.id].phase;
      if (phase && !e.phaseIndex) {
        if (phase.t !== 'phase') throw new RuleError('INVALID_ENEMY_MOVE');
        this.enemyMove(e, phase); return;
      }
      e.alive = false; this.ev('kill', { uid: e.uid, id: e.id });
      if (ctx) ctx.killed = true;
      if (e.st.mark > 0) {
        s.killedMarked += 1;
        if (this.passiveOf('kaya_deduction') && !s.turnFlags.deduction && s.phase === 'PLAYER') {
          s.turnFlags.deduction = true; s.ap = Math.min(this.ix.rules.apMax, s.ap + 1); this.draw(1);
          if (this.hasHook('kaya', 'kaya_r2')) { const t = this.randEnemy(); if (t) this.addStatus(t, 'burn', 3); }
        }
        if (this.has('detective_notebook')) { this.draw(1); s.runFlags.coinsBonus = (s.runFlags.coinsBonus || 0) + 10; }
      }
    }
    breakEnemy(e) {
      const s = this.s;
      e.brokenUntil = s.turn + 1; e.stunned = true;
      e.intent = { t: 'stun', label: '균열: 행동 불가' };
      if (s.breakApTurn < this.ix.rules.breakApBonusMaxPerTurn) { s.apNext += this.ix.rules.breakApBonus; s.breakApTurn += 1; }
      this.ev('break', { uid: e.uid });
      if (this.has('silver_needle')) this.healParty(3, null);
    }

    hitEnemy(e, amount, opts, ctx) {
      const s = this.s;
      const pierceBps = opts.pierce || 0;
      const pierce = Math.floor(amount * pierceBps / BPS);
      const normal = amount - pierce;
      const blocked = Math.min(e.block, normal);
      e.block -= blocked;
      e.hp = Math.max(0, e.hp - (amount - blocked));
      this.ev('dmg', { uid: e.uid, n: amount, blocked, src: ctx.isUlt ? 'ult' : 'card', card: ctx.inst ? ctx.inst.id : null });
      // reflect
      if (!ctx.isUlt && e.st.reflect > 0) { const r = this.scaleEnemy(e.st.reflect); const b = Math.min(s.block, r); s.block -= b; if (r - b > 0) this.loseHp(r - b, 'reflect'); }
      // break gauge
      if (!e.brokenUntil) {
        const reduce = (e.weak.includes(ctx.attr) ? (opts.brk || 0) : 0) + (opts.shock || 0);
        if (reduce > 0) { e.brk = Math.max(0, e.brk - reduce); if (e.brk === 0) this.breakEnemy(e); }
      }
      // part on-hit statuses
      if (ctx.inst && e.alive) for (const p of this.partsOf(ctx.inst)) {
        if (p.fx.markOnHit) e.st.mark += p.fx.markOnHit;
        if (p.fx.burnOnHit) e.st.burn += p.fx.burnOnHit;
        if (p.fx.weakOnHit) e.st.weak += p.fx.weakOnHit;
      }
      if (e.hp === 0) this.killEnemy(e, ctx);
    }

    brkFor(op, ctx, enemy) {
      let brk = op.brk ?? 1;
      if (ctx.inst) for (const p of this.partsOf(ctx.inst)) if (p.fx.brk) brk += p.fx.brk;
      if (ctx.reserved && this.has('precision_lens')) brk += 1;
      if (this.has('eye_of_rift')) brk += 1;
      if (ctx.attr === 'frost' && this.has('frost_crystal') && !this.s.turnFlags.frostCrystal && enemy && enemy.weak.includes('frost')) { this.s.turnFlags.frostCrystal = true; brk += 1; }
      return brk;
    }

    execOp(op, ctx) {
      const s = this.s; const R = this.ix.rules;
      switch (op.op) {
        case 'dmg': {
          // one-time-per-card consumptions
          if (ctx.firstDmg) {
            ctx.firstDmg = false;
            const ch = this.buff('charge');
            if (ch && !ctx.isUlt) { ctx.chargeBps = ch.n; s.buffs = s.buffs.filter((b) => b !== ch); }
            if (this.has('credit_ledger') && this.debtTotal() > 0 && !s.turnFlags.ledger && !ctx.isUlt) { s.turnFlags.ledger = true; ctx.creditLedger = true; }
            if (ctx.attr === 'volt' && this.has('volt_coil') && !s.turnFlags.voltCoil) { s.turnFlags.voltCoil = true; ctx.flatBonus = 4; }
            if (ctx.inst && ctx.actor) { const ba = this.weaponPassive(ctx.actor.id, 'w_blockOnAtk'); if (ba) this.gainBlockRaw(ba.value, 'weapon'); }
          }
          let hits = op.hits ?? 1;
          if (op.hitsScale) hits = this.scaled(hits, op.hitsScale, ctx);
          const tgt = op.tgt || 'one';
          for (let h = 0; h < hits; h++) {
            const targets = this.targetsFor(tgt, ctx);
            for (const e of targets) {
              if (!e.alive) continue;
              let amount = this.calcDamage(op, ctx, e);
              if (h === 0 && hits >= 2 && ctx.actor) { const mh = this.weaponPassive(ctx.actor.id, 'w_multiHit'); if (mh) amount += mh.value; }
              ctx.lastTarget = e.uid;
              let pierce = op.pierce || 0;
              if (ctx.inst) for (const p of this.partsOf(ctx.inst)) if (p.fx.pierce) pierce = Math.max(pierce, p.fx.pierce);
              this.hitEnemy(e, amount, { pierce, brk: this.brkFor(op, ctx, e), shock: op.shock || 0 }, ctx);
              if (this.checkEnd()) break;
            }
            if (this.isOver() || this.alive().length === 0) break;
          }
          ctx.flatBonus = 0;
          if (ctx.inst && !s.firstAtkDone && this.ix.cards[ctx.inst.id].type === 'atk') s.firstAtkDone = true;
          if (this.alive().length === 0) this.checkEnd();
          break;
        }
        case 'blk': {
          const n = this.calcBlock(op, ctx);
          this.gainBlockRaw(n, 'card');
          const hp = this.passiveOf('harin_stance');
          if (hp && ctx.actor && ctx.actor.id === 'harin' && n > 0) s.counter += hp.n;
          break;
        }
        case 'heal': {
          let n = this.scaled(this.value(op, ctx), op.scale, ctx);
          // heals scale like block: vitality (owner or party average) and skill / ultimate level
          const vit = ctx.actor ? ctx.actor.vit : this.partyVit();
          let lv = 1; if (ctx.isUlt && ctx.actor) lv = ctx.actor.ultLv; else if (ctx.actor && ctx.inst && ctx.inst.owner === ctx.actor.id) lv = ctx.actor.skillLv;
          n = Number((BigInt(Math.max(0, n)) * BigInt(vit) * BigInt(BPS + 400 * (lv - 1))) / (81n * BigInt(BPS)));
          const hu = this.buff('healUp'); if (hu) { n = Math.floor(n * (BPS + hu.n) / BPS); s.buffs = s.buffs.filter((b) => b !== hu); }
          const o2 = { ...op, preScaled: true, ultAila: !!(ctx.isUlt && ctx.actor && this.hasHook(ctx.actor.id, 'aila_r6')) };
          this.healParty(n, o2);
          break;
        }
        case 'st': {
          let n = (ctx.reserved && op.rn !== undefined) ? op.rn : op.n;
          const targets = this.targetsFor(op.tgt || 'one', ctx);
          for (const e of targets) { this.addStatus(e, op.s, n); ctx.lastTarget = e.uid; }
          break;
        }
        case 'buff': {
          const turns = op.turns || 1;
          const existing = s.buffs.find((b) => b.s === op.s);
          const nb = { s: op.s, n: op.n, until: s.turn + turns - 1 };
          if (existing) { existing.n = Math.max(existing.n, op.n); existing.until = Math.max(existing.until, nb.until); } else s.buffs.push(nb);
          break;
        }
        case 'counter': s.counter += (ctx.reserved && op.rn !== undefined) ? op.rn : op.n; break;
        case 'draw': {
          let n = (ctx.reserved && op.rn !== undefined) ? op.rn : op.n;
          n = this.scaled(n, op.scale, ctx);
          this.draw(n);
          break;
        }
        case 'discard': {
          for (let k = 0; k < op.n && s.zones.hand.length; k++) {
            const uid = this.takePick(ctx, s.zones.hand, (u) => true, 'lowestValue');
            if (!uid) break;
            s.zones.hand.splice(s.zones.hand.indexOf(uid), 1); s.zones.discard.push(uid);
          }
          break;
        }
        case 'ap': s.ap = Math.min(R.apMax, s.ap + op.n); break;
        case 'apNext': s.apNext += op.n; break;
        case 'gauge': {
          const who = op.who === 'all' ? s.actors : (ctx.actor ? [ctx.actor] : []);
          for (const a of who) this.gainGauge(a, op.n, true);
          break;
        }
        case 'create': {
          for (let k = 0; k < op.n; k++) {
            const uid = makeInst(s, this.ix, { id: op.card, wielder: ctx.inst ? ctx.inst.wielder : null }, { token: true });
            if (op.zone === 'hand' && s.zones.hand.length < s.handMax) s.zones.hand.push(uid); else s.zones.discard.push(uid);
          }
          break;
        }
        case 'pull': {
          const r = this.pickReservation(ctx.picks);
          if (!r) break;
          if (r.due > s.turn + 1) {
            if (this.freeSlots('next') <= 0) throw new RuleError('PULL_DESTINATION_FULL');
            r.due -= 1; r.kind = 'next'; delete r.delayed; this.ev('pull', { uid: r.uid, due: r.due });
          } else {
            s.res = s.res.filter((x) => x !== r);
            this.ev('pull', { uid: r.uid, due: s.turn });
            this.resolveCard(r.uid, { reserved: r.kind, target: r.target, picks: [] });
          }
          break;
        }
        case 'slot': {
          const RS = R.slots;
          s.slotsTemp.next = Math.max(0, Math.min(RS.nextMax - s.slotsBase.next, s.slotsTemp.next + (op.next || 0)));
          s.slotsTemp.after = Math.max(0, Math.min(RS.afterMax - s.slotsBase.after, s.slotsTemp.after + (op.after || 0)));
          break;
        }
        case 'grow': {
          const inst = ctx.inst;
          if (ctx.reserved) this.applyGrowth(inst, 1, { source: 'reserve', capped: true, once: true });
          break;
        }
        case 'growPick': {
          if (s.growthCount >= s.growthCap) break;
          const uid = this.takePick(ctx, s.zones.hand, (u) => this.ix.cards[s.cards[u].id].growth && s.cards[u].growth < 3, 'first');
          if (uid) this.applyGrowth(s.cards[uid], op.n, { source: 'growPick', capped: true });
          break;
        }
        case 'growLowest': {
          const gs = Object.values(s.cards).filter((c) => this.ix.cards[c.id].growth && c.growth < 3).sort((a, b) => a.growth - b.growth || (a.uid < b.uid ? -1 : 1));
          for (const c of gs.slice(0, op.n)) this.applyGrowth(c, 1, { source: 'growLowest' });
          break;
        }
        case 'growAll': {
          for (const c of Object.values(s.cards)) this.applyGrowth(c, 1, { source: 'growAll' });
          break;
        }
        case 'loan': {
          s.ap = Math.min(R.apMax, s.ap + op.n); s.resSeq += 1;
          s.debts.push({ id: `d${s.resSeq}`, n: op.n, due: s.turn + 1, deferred: false });
          this.ev('loan', { n: op.n });
          break;
        }
        case 'defer': {
          const d = s.debts.find((x) => x.due === s.turn + 1 && !x.deferred);
          if (d) { d.due += 1; d.n += 1; d.deferred = true; this.ev('defer', { id: d.id }); }
          break;
        }
        case 'restraint': s.restraint = { active: true, usedTurn: s.turn }; break;
        case 'collateral': s.sealedUntil = s.turn + 1; this.gainBlockRaw(op.v, 'collateral'); break;
        case 'selfdmg': {
          const cost = this.overloadCost(op.v, false);
          if (cost > 0) {
            this.loseHp(cost, 'overload');
            if (this.has('lightning_rod')) this.gainBlockRaw(Math.floor(cost / 2), 'rod');
          }
          break;
        }
        case 'cleanse': { let n = 0; for (const k of Object.keys(s.pst)) if (s.pst[k] > 0) { n += 1; s.pst[k] = 0; } ctx.lastCount = n; break; }
        case 'cleanseOne': { if (s.pst[op.s] > 0) s.pst[op.s] = 0; break; }
        case 'exhaustPick': {
          const zone = op.from === 'discard' ? 'discard' : 'hand';
          let n = 0;
          for (let k = 0; k < op.n; k++) {
            const uid = this.takePick(ctx, s.zones[zone], () => true, 'lowestValue');
            if (!uid) break;
            this.exhaustCard(uid); n += 1;
          }
          ctx.lastCount = n;
          break;
        }
        case 'exhaustHand': { const hand = [...s.zones.hand]; for (const uid of hand) this.exhaustCard(uid); ctx.lastCount = hand.length; break; }
        case 'tune': {
          const part = op.part;
          if (op.pick === 'all') { for (const uid of s.zones.hand) this.attachTemp(uid, part); }
          else if (op.pick === 'random') this.tuneRandom(part);
          else if (op.pick === 'same') { if (ctx.lastPicked && s.zones.hand.includes(ctx.lastPicked)) this.attachTemp(ctx.lastPicked, part); }
          else { const uid = this.takePick(ctx, s.zones.hand, (u) => !s.cards[u].temp.includes(part) && this.ix.cards[s.cards[u].id].rar !== 'C', 'highestCost'); if (uid) { this.attachTemp(uid, part); ctx.lastPicked = uid; } }
          break;
        }
        case 'costdown': {
          if (op.pick === 'all') { for (const uid of s.zones.hand) s.cards[uid].costMod -= op.n; }
          else { const uid = this.takePick(ctx, s.zones.hand, (u) => this.costOf(u, 'now') > 0, 'highestCost'); if (uid) { s.cards[uid].costMod -= op.n; ctx.lastPicked = uid; } }
          break;
        }
        case 'recall': {
          if (s.zones.hand.length >= s.handMax) break;
          const uid = this.takePick(ctx, s.zones.discard, (u) => this.ix.cards[s.cards[u].id].rar !== 'C', 'highestCost');
          if (uid) { s.zones.discard.splice(s.zones.discard.indexOf(uid), 1); s.zones.hand.push(uid); }
          break;
        }
        case 'copyPick': {
          const uid = this.takePick(ctx, s.zones.hand, (u) => this.isGrowthInst(s.cards[u]), 'first');
          if (uid && s.zones.hand.length < s.handMax) {
            const src = s.cards[uid];
            const cu = makeInst(s, this.ix, { id: src.id, plus: src.plus, mods: src.mods, from: src.from }, { token: true, free: true });
            s.cards[cu].growth = src.growth; s.cards[cu].echoCopy = true;
            s.zones.hand.push(cu);
          }
          break;
        }
        case 'power': {
          const { op: _o, id, ...params } = op;
          const ex = s.powers.find((p) => p.id === id);
          if (ex) { for (const [k, v] of Object.entries(params)) if (typeof v === 'number') ex[k] = (ex[k] || 0) + v; }
          else s.powers.push({ id, ...params });
          this.ev('power', { id });
          break;
        }
        case 'echo': if (s.echoActive) { s.echo = Math.min(s.echoMax, s.echo + op.n); this.echoCheck(); } break;
        case 'nextBoost': s.nextBoost = { bps: op.bps, from: ctx.actor ? ctx.actor.id : null }; break;
        case 'freeNext': s.freeNext = Math.max(s.freeNext, op.n); break;
        case 'clearDebt': ctx.lastCount = this.debtTotal(); s.debts = []; break;
        case 'breakBonus': {
          const n = op.scale ? this.scaled(op.n || 0, op.scale, ctx) : op.n;
          const e = this.enemy(ctx.lastTarget);
          if (e && e.alive && !e.brokenUntil && e.weak.includes(ctx.attr) && n > 0) { e.brk = Math.max(0, e.brk - n); if (e.brk === 0) this.breakEnemy(e); }
          break;
        }
        case 'breakBonusAll': {
          const n = op.scale ? this.scaled(op.n || 0, op.scale, ctx) : (op.n || 0);
          for (const e of this.alive()) if (!e.brokenUntil && e.weak.includes(ctx.attr) && n > 0) { e.brk = Math.max(0, e.brk - n); if (e.brk === 0) this.breakEnemy(e); }
          break;
        }
        case 'if': {
          const branch = this.cond(op.cond, ctx) ? op.then : (op.else || []);
          for (const o of branch) { this.execOp(o, ctx); if (this.isOver()) break; }
          break;
        }
        default: throw new RuleError('UNSUPPORTED_EFFECT', op.op);
      }
    }

    echoCheck() {
      const s = this.s;
      if (s.echo >= 3 && s.powers.some((p) => p.id === 'echochamber') && !s.turnFlags.echoChamber) { s.turnFlags.echoChamber = true; this.draw(1); }
    }
    attachTemp(uid, part) { const c = this.s.cards[uid]; if (!c || c.temp.includes(part)) return; c.temp.push(part); this.s.tempParts += 1; this.ev('tune', { uid, part }); }
    tuneRandom(part) {
      const s = this.s; const cands = s.zones.hand.filter((u) => !s.cards[u].temp.includes(part) && this.ix.cards[s.cards[u].id].rar !== 'C');
      if (cands.length) this.attachTemp(Rng.pick(s.rng, cands), part);
    }
    // choices: the client sends picks in op order; a missing/invalid pick falls back to a deterministic rule
    takePick(ctx, zoneArr, ok, fallback) {
      const s = this.s;
      while (ctx.picks.length) {
        const p = ctx.picks.shift();
        if (zoneArr.includes(p) && ok(p)) return p;
      }
      const cands = zoneArr.filter(ok);
      if (!cands.length) return null;
      if (fallback === 'first') return cands[0];
      const costOf = (u) => this.ix.cards[s.cards[u].id].cost;
      if (fallback === 'highestCost') return cands.slice().sort((a, b) => costOf(b) - costOf(a))[0];
      // lowestValue: curses first, then lowest cost
      return cands.slice().sort((a, b) => (this.ix.cards[s.cards[a].id].rar === 'C' ? -1 : 0) - (this.ix.cards[s.cards[b].id].rar === 'C' ? -1 : 0) || costOf(a) - costOf(b))[0];
    }

    draw(n) {
      const s = this.s;
      for (let k = 0; k < n; k++) {
        if (!s.zones.draw.length) {
          if (!s.zones.discard.length) break;
          s.zones.draw = s.zones.discard; s.zones.discard = [];
          Rng.shuffle(s.rng, s.zones.draw);
          this.ev('reshuffle');
        }
        const uid = s.zones.draw.shift();
        if (s.zones.hand.length < s.handMax) s.zones.hand.push(uid); else { s.zones.discard.push(uid); this.ev('burnCard', { uid }); }
      }
    }
    // echo-circuit copies: created at the start of their due turn, after the normal draw (hand full → discard pile)
    deliverCopies() {
      const s = this.s;
      if (!s.pendingCopies || !s.pendingCopies.length) return;
      const due = s.pendingCopies.filter((x) => x.due <= s.turn);
      s.pendingCopies = s.pendingCopies.filter((x) => x.due > s.turn);
      for (const x of due) {
        const cu = makeInst(s, this.ix, { id: x.id, wielder: x.wielder, from: x.from }, { token: true, free: true });
        s.cards[cu].temp.push('echoHalf'); s.cards[cu].echoCopy = true;
        if (s.zones.hand.length < s.handMax) s.zones.hand.push(cu); else s.zones.discard.push(cu);
        this.ev('echoCopy', { uid: cu, id: x.id });
      }
    }

    // ───── invariants ─────
    checkInvariants() {
      const s = this.s; const R = this.ix.rules;
      if (!(s.hp >= 0 && s.hp <= s.maxHp)) throw new RuleError('STATE_RANGE', 'hp');
      if (!(s.ap >= 0 && s.ap <= R.apMax)) throw new RuleError('STATE_RANGE', 'ap');
      if (s.zones.hand.length > s.handMax) throw new RuleError('STATE_RANGE', 'hand');
      if (this.debtTotal() > s.debtCap) throw new RuleError('DEBT_INVARIANT');
      if (s.growthCount > s.growthCap || s.growthCount < 0) throw new RuleError('GROWTH_INVARIANT');
      for (const c of Object.values(s.cards)) if (!Number.isInteger(c.growth) || c.growth < 0 || c.growth > 3) throw new RuleError('GROWTH_INVARIANT');
      const seen = [...s.zones.draw, ...s.zones.hand, ...s.zones.discard, ...s.zones.exhaust, ...s.res.map((r) => r.uid)];
      const all = Object.keys(s.cards);
      if (seen.length !== all.length || new Set(seen).size !== seen.length || !all.every((u) => seen.includes(u))) throw new RuleError('CARD_ZONE_CONSERVATION');
      for (const a of s.actors) if (a.gauge < 0 || a.gauge > R.gaugeMax) throw new RuleError('GAUGE_RANGE');
      for (const e of s.enemies) if (e.hp < 0 || e.block < 0 || e.brk < 0) throw new RuleError('ENEMY_RANGE');
      for (const kind of ['next', 'after']) {
        const due = s.turn + (kind === 'next' ? 1 : 2);
        const used = s.res.filter((r) => r.due === due).length;
        const capMax = kind === 'next' ? R.slots.nextMax : R.slots.afterMax;
        if (used > capMax) throw new RuleError('CAPACITY_INVARIANT');
      }
    }

    // public view: never includes draw order or RNG state
    publicView() {
      const s = this.s;
      return {
        v: s.v, turn: s.turn, phase: s.phase, hp: s.hp, maxHp: s.maxHp, block: s.block, counter: s.counter, ap: s.ap,
        apNext: s.apNext, apDrainNext: s.apDrainNext || 0, silenceUntil: s.silenceUntil || 0,
        debt: this.debtTotal(), drawCount: s.zones.draw.length, discard: [...s.zones.discard], exhaustCount: s.zones.exhaust.length,
        hand: s.zones.hand.map((u) => ({ ...s.cards[u] })), res: s.res.map((r) => ({ ...r })),
        enemies: s.enemies.map((e) => ({ ...e, st: { ...e.st } })), actors: s.actors.map((a) => ({ id: a.id, gauge: a.gauge })),
      };
    }
  }

  // ───────────────────────── run (exploration) ─────────────────────────
  const NODE_NAME = { battle: '전투', elite: '엘리트', rest: '휴식', forge: '공방', shop: '상점', event: '사건', boss: '보스', archive: '기록소' };

  class Run {
    constructor(data, state) { this.data = data; this.ix = indexData(data); this.s = state; this.battle = state.battle ? new Battle(this.ix, state.battle) : null; }

    // setup: {party:[{charId,res,weaponId,refine}], memory:{charId:[spec]}, relics?, seed, length:'short'|'full'}
    static create(data, setup) {
      const ix = indexData(data);
      const s = {
        schema: 'mf-run-0.7', seed: setup.seed ?? 1, rng: Rng.seed((setup.seed ?? 1) ^ 0x5eed), length: setup.length || 'short',
        difficulty: setup.stageLevel ? Stats.combine(setup.difficulty || {}, Stats.stageScale(ix, setup.stageLevel)) : (setup.difficulty || { hp: 100, dmg: 100 }),
        stageLevel: setup.stageLevel || null,
        // zones visited in order (short = [1], full = [1, 2]; story chapters pass their own list) and optional fixed bosses per zone
        zones: setup.zones || ((setup.length || 'short') === 'full' ? [1, 2] : [1]), zoneIdx: 0, bosses: setup.bosses || null,
        zone: 0, floor: 0, pos: null, map: null, party: clone(setup.party), hp: 0, maxHp: 0, coins: 60, deck: [], relics: [...(setup.relics || [])],
        partsFound: [], pending: null, battle: null, phase: 'MAP', log: [], stats: { battles: 0, elites: 0, kills: 0 },
        runFlags: { envelopeUsed: false, refusals: 0, nextApBonus: 0 }, offered: {}, result: null, startMemory: clone(setup.memory || {}),
        seqCard: 0,
      };
      if (setup.memoryPolicy === 'v2') {
        if (!s.party.length || new Set(s.party.map(p => p.charId)).size !== s.party.length || s.party.some(p => !ix.chars[p.charId])) throw new RuleError('INVALID_SNAPSHOT');
        if (typeof setup.runId !== 'string' || !setup.runId) throw new RuleError('RUN_ID_REQUIRED');
        if (!Number.isInteger(setup.memoryVersion ?? 0) || (setup.memoryVersion ?? 0) < 0) throw new RuleError('INVALID_SNAPSHOT');
        s.schema = 'mf-run-memory-v2'; s.memoryPolicy = 'v2'; s.runId = setup.runId; s.memoryVersion = setup.memoryVersion ?? 0;
        s.memoryBaseline = {}; s.memoryBefore = {};
        for (const p of s.party) {
          s.memoryBaseline[p.charId] = MemoryV2.baseline(data, p.charId);
          s.memoryBefore[p.charId] = clone(setup.memory?.[p.charId] || s.memoryBaseline[p.charId]);
        }
        MemoryV2.inspect(data, s.memoryBefore);
        for (const p of s.party) if (MemoryV2.cost(data, p.charId, s.memoryBefore[p.charId], s.memoryBaseline[p.charId]) > Run.capacityOf(ix, p)) throw new RuleError('OVER_CAPACITY');
      }
      // party max HP = sum of character HP (60 + Lv + armour); reference battle = 81 per character
      for (const p of s.party) { const d = Stats.derive(ix, p); Object.assign(p, { power4: d.power4, hp: d.hp, gearBps: d.gearBps, skillLv: d.skillLv, ultLv: d.ultLv }); }
      s.maxHp = s.party.reduce((a, p) => a + p.hp, 0); s.hp = s.maxHp; s.hpRef = s.maxHp;
      // deck = each character's memory deck (or base kit) + weapon card
      for (const p of s.party) {
        const specs = s.memoryPolicy === 'v2' ? clone(setup.useMemory === false ? s.memoryBaseline[p.charId] : s.memoryBefore[p.charId]) : ((setup.memory && setup.memory[p.charId]) || Run.baseKit(ix, p.charId));
        if (s.memoryPolicy === 'v2' && setup.useMemory === false) for (const sp of specs) {
          if (!s.memoryBefore[p.charId].some(old => old.key === sp.key && old.id === sp.id && !old.from)) sp.key = Run.nextCardKey(s);
        }
        for (const sp of specs) s.deck.push({ ...clone(sp), key: s.memoryPolicy === 'v2' ? sp.key : Run.nextCardKey(s), owner: ix.cards[sp.id].owner, assigned: sp.assigned || (ix.cards[sp.id].owner === 'common' ? p.charId : null) });
        const w = ix.weapons[p.weaponId] || ix.weapons[Run.defaultWeapon(ix, p.charId)];
        s.deck.push({ id: w.card, key: Run.nextCardKey(s), owner: 'weapon', wielder: p.charId, weapon: true });
      }
      for (const p of s.party) s.offered[p.charId] = 0;
      for (const z of s.zones) if (!data.encounters[String(z)]) throw new RuleError('ZONE_NOT_AUTHORED', { zone: z });
      s.zone = s.zones[0];
      s.map = Run.genMap(s, s.zone);
      const run = new Run(data, s);
      if (s.memoryPolicy === 'v2' && setup.useMemory === false) s.playBefore = run.deckMemorySnapshot();
      return run;
    }
    static nextCardKey(s) {
      if (s.memoryPolicy !== 'v2') return `k${++s.seqCard}`;
      let key;
      do { key = `run:${s.runId}:${++s.seqCard}`; } while (s.deck.some(c => c.key === key) || Object.values(s.memoryBefore).some(rows => rows.some(c => c.key === key)));
      return key;
    }
    static baseKit(ix, charId) {
      const ch = ix.chars[charId];
      const starts = ix.raw.cards.filter((c) => c.owner === charId && c.start).map((c) => ({ id: c.id }));
      starts.push({ id: ix.raw.startingCommon[charId], assigned: charId });
      return starts;
    }
    static defaultWeapon(ix, charId) { const t = ix.chars[charId].weapon; return { ranged: 'w_basic_ranged', melee: 'w_basic_melee', device: 'w_basic_device', ritual: 'w_basic_ritual' }[t]; }

    static genMap(s, zone) {
      const floors = 7; const map = { zone, floors: [] };
      for (let f = 0; f < floors; f++) {
        const width = f === 0 ? 3 : 2 + Rng.below(s.rng, 2);
        const row = [];
        for (let i = 0; i < width; i++) row.push({ id: `z${zone}f${f}n${i}`, f, i, type: 'battle', next: [] });
        map.floors.push(row);
      }
      // node types per floor (weights in %). Floors 0, 2 and 4 are always fights, so every path has >= 3 fights + boss.
      const TABLE = {
        3: [['battle', 30], ['elite', 20], ['event', 20], ['rest', 15], ['forge', 15]],
        4: [['battle', 60], ['elite', 40]],
        5: [['battle', 30], ['elite', 15], ['event', 20], ['shop', 20], ['forge', 15]],
      };
      const assign = (f, i) => {
        if (f === 0 || f === 2) return 'battle';
        if (f === 1) return i === 0 && Rng.below(s.rng, 3) === 0 ? 'event' : 'battle';
        if (f === floors - 1) return ['rest', 'forge', 'shop'][i % 3];
        let roll = Rng.below(s.rng, 100);
        for (const [t, w] of TABLE[f]) { if (roll < w) return t; roll -= w; }
        return 'battle';
      };
      for (const row of map.floors) for (const n of row) n.type = assign(n.f, n.i);
      // ensure at least one rest/forge reachable mid-zone and at most 2 elites, none adjacent
      let elites = 0;
      for (const row of map.floors) for (const n of row) { if (n.type === 'elite') { elites += 1; if (elites > 2) n.type = 'battle'; } }
      if (!map.floors[3].some((n) => n.type === 'rest' || n.type === 'forge')) map.floors[3][0].type = 'rest';
      // edges: each node links to 1-2 nodes in the next floor; every next node gets at least one parent
      for (let f = 0; f < floors - 1; f++) {
        const a = map.floors[f]; const b = map.floors[f + 1];
        for (const n of a) {
          const j = Math.min(b.length - 1, Math.round(n.i * (b.length - 1) / Math.max(1, a.length - 1)));
          n.next.push(b[j].id);
          if (Rng.below(s.rng, 2) === 0 && b[j + 1]) n.next.push(b[j + 1].id); else if (b[j - 1] && Rng.below(s.rng, 3) === 0) n.next.push(b[j - 1].id);
        }
        for (const m of b) if (!a.some((n) => n.next.includes(m.id))) { const p = a[Math.min(a.length - 1, m.i)]; p.next.push(m.id); }
        for (const n of a) n.next = [...new Set(n.next)];
        // no consecutive elites on a path
        for (const n of a) if (n.type === 'elite') for (const id of n.next) { const m = b.find((x) => x.id === id); if (m.type === 'elite') m.type = 'battle'; }
      }
      map.boss = { id: `z${zone}boss`, type: 'boss', f: floors, i: 0 };
      for (const n of map.floors[floors - 1]) n.next = [map.boss.id];
      return map;
    }
    nodeById(id) { const m = this.s.map; if (m.boss.id === id) return m.boss; for (const row of m.floors) for (const n of row) if (n.id === id) return n; return null; }
    available() {
      const s = this.s;
      if (s.phase !== 'MAP') return [];
      if (!s.pos) return s.map.floors[0].map((n) => n.id);
      const cur = this.nodeById(s.pos);
      return cur ? cur.next : [];
    }
    log(t) { this.s.log.push(t); if (this.s.log.length > 100) this.s.log.shift(); }

    enter(nodeId) {
      const s = this.s;
      if (!this.available().includes(nodeId)) throw new RuleError('NODE_UNREACHABLE');
      const n = this.nodeById(nodeId); s.pos = nodeId; s.floor = n.f;
      switch (n.type) {
        case 'battle': case 'elite': case 'boss': this.startBattle(n.type); break;
        case 'rest': s.phase = 'REST'; s.pending = { type: 'rest', uses: this.has('forge_ember') ? 2 : 1 }; break;
        case 'forge': s.phase = 'FORGE'; s.pending = { type: 'forge', uses: this.has('forge_ember') ? 2 : 1, offers: null }; break;
        case 'shop': s.phase = 'SHOP'; s.pending = this.makeShop(); break;
        case 'event': { s.phase = 'EVENT'; const ev = Rng.pick(s.rng, this.data.events); s.pending = { type: 'event', id: ev.id }; break; }
        default: break;
      }
      this.log(`${NODE_NAME[n.type]} 진입`);
      if (this.battle && this.battle.isOver()) this.afterBattle(); // begin may end the battle before any player command
    }
    has(r) { return this.s.relics.includes(r); }

    encounterFor(type) {
      const s = this.s; const enc = this.data.encounters[String(s.zone)];
      if (!enc) throw new RuleError('ZONE_NOT_AUTHORED', { zone: s.zone });
      if (type === 'boss' && s.bosses && s.bosses[s.zoneIdx]) return [s.bosses[s.zoneIdx]];
      const pool = enc[type === 'battle' ? 'normal' : type];
      return [...Rng.pick(s.rng, pool)];
    }
    startBattle(type) {
      const s = this.s;
      const deck = s.deck.map((c) => ({ id: c.id, plus: c.plus, mods: c.mods, growth: c.growth || 0, wielder: c.wielder, assigned: c.assigned, from: c.from, key: c.key }));
      const seed = Rng.next(s.rng);
      const b = Battle.create(this.data, { party: s.party, deck, relics: s.relics, hp: s.hp, maxHp: s.maxHp, seed, runFlags: { ...s.runFlags, nextApBonus: 0 }, difficulty: s.difficulty,
        enemies: this.encounterFor(type), nextApBonus: s.runFlags.nextApBonus || 0 });
      s.runFlags.nextApBonus = 0; // one-shot: only this battle's first turn; its runFlags copy carries 0 so afterBattle cannot refund it
      // map battle instances back to deck keys for growth/evolution persistence
      const uids = Object.keys(b.s.cards);
      deck.forEach((d, i) => { b.s.cards[uids[i]].key = d.key; });
      b.do('begin');
      this.battle = b; s.battle = b.s; s.phase = 'BATTLE'; s.battleType = type;
    }
    battleCmd(kind, args) {
      if (!this.battle) throw new RuleError('NO_BATTLE');
      const r = this.battle.do(kind, args);
      this.s.battle = this.battle.s;
      if (this.battle.isOver()) this.afterBattle();
      return r;
    }
    afterBattle() {
      const s = this.s; const bs = this.battle.s;
      s.hp = bs.hp; s.runFlags = { ...s.runFlags, ...bs.runFlags };
      // persist growth from battle instances back to the run deck
      for (const c of Object.values(bs.cards)) if (c.key) { const d = s.deck.find((x) => x.key === c.key); if (d) d.growth = c.growth; }
      if (bs.phase === 'DEFEAT') { s.phase = 'DEFEAT'; s.result = { outcome: 'defeat', floor: s.floor }; this.log('패배'); s.pending = { type: 'save', defeat: true }; this.captureMemory(); this.battle = null; s.battle = null; return; }
      s.stats.battles += 1; s.stats.kills += bs.enemies.filter((e) => !e.alive).length;
      const type = s.battleType;
      if (type === 'elite') s.stats.elites += 1;
      const coinBase = type === 'boss' ? 100 : type === 'elite' ? 50 + Rng.below(s.rng, 16) : 22 + Rng.below(s.rng, 11);
      const coins = coinBase + (this.has('lucky_coin') ? 10 : 0) + (bs.runFlags.coinsBonus || 0);
      s.runFlags.coinsBonus = 0;
      s.coins += coins;
      const reward = { type: 'reward', coins, cards: this.cardChoices(type), relic: null, part: null, evolutions: this.pendingEvolutions() };
      if (type === 'elite') reward.relic = this.relicChoices('elite');
      if (type === 'boss') reward.relic = this.relicChoices('boss');
      if (type === 'battle' && Rng.below(s.rng, 4) === 0) reward.part = this.partChoices(false, 1)[0];
      s.pending = reward; s.phase = 'REWARD';
      this.battle = null; s.battle = null;
    }
    pendingEvolutions() {
      return this.s.deck.filter((c) => { const d = this.ix.cards[c.id]; return d.growth && d.evo && (c.growth || 0) >= 3; }).map((c) => ({ key: c.key, options: this.ix.cards[c.id].evo }));
    }
    rarityRoll(type) {
      const r = Rng.below(this.s.rng, 100);
      if (type === 'boss') return 'S';
      if (type === 'elite') return r < 55 ? 'A' : 'S';
      return r < 58 ? 'B' : r < 92 ? 'A' : 'S';
    }
    cardChoices(type) {
      const s = this.s; const n = this.has('chrono_crown') ? 2 : 3;
      const chars = s.party.map((p) => p.charId);
      // fairness: if offers differ by more than 2, include the least-offered character
      const counts = chars.map((c) => s.offered[c] || 0);
      const need = Math.max(...counts) - Math.min(...counts) > 2 ? chars[counts.indexOf(Math.min(...counts))] : null;
      const picks = []; const used = new Set();
      for (let k = 0; k < n; k++) {
        const isCommon = k === n - 1 && Rng.below(s.rng, 3) === 0;
        const rar = this.rarityRoll(type);
        let pool;
        if (isCommon) pool = this.data.cards.filter((c) => c.owner === 'common' && c.pool !== false && (c.rar === rar || rar === 'S' && c.rar === 'A'));
        else {
          const ch = k === 0 && need ? need : Rng.pick(s.rng, chars);
          pool = this.data.cards.filter((c) => c.owner === ch && c.pool !== false && c.rar === rar);
          if (!pool.length) pool = this.data.cards.filter((c) => c.owner === ch && c.pool !== false);
        }
        pool = pool.filter((c) => !used.has(c.id));
        if (!pool.length) continue;
        const c = Rng.pick(s.rng, pool); used.add(c.id); picks.push(c.id);
        if (this.ix.chars[c.owner]) s.offered[c.owner] = (s.offered[c.owner] || 0) + 1;
      }
      return picks;
    }
    // kind: 'boss' (3 boss relics) · 'elite' (1 of common+rare) · 'shop' (2 distinct common+rare) · 'common' (1 common)
    relicChoices(kind, n) {
      const s = this.s; const owned = new Set(s.relics);
      const rar = kind === 'boss' ? ['boss'] : kind === 'common' ? ['common'] : ['common', 'rare'];
      const pool = this.data.relics.filter((r) => rar.includes(r.rarity) && !owned.has(r.id));
      const want = n ?? (kind === 'boss' ? 3 : kind === 'shop' ? 2 : 1);
      const out = []; const tmp = [...pool];
      for (let k = 0; k < want && tmp.length; k++) out.push(tmp.splice(Rng.below(s.rng, tmp.length), 1)[0].id);
      return out;
    }
    partChoices(rareOnly, n = 3, commonOnly = false) {
      const s = this.s;
      const pool = this.data.parts.filter((p) => p.slot !== 'temp' && (!rareOnly || p.rarity === 'rare') && (!commonOnly || p.rarity === 'common'));
      const tmp = [...pool]; const out = [];
      for (let k = 0; k < n && tmp.length; k++) out.push(tmp.splice(Rng.below(s.rng, tmp.length), 1)[0].id);
      return out;
    }
    // reward actions
    takeCard(cardId) {
      const s = this.s; const p = s.pending;
      if (!p || p.type !== 'reward' || !p.cards.includes(cardId)) throw new RuleError('BAD_REWARD');
      const def = this.ix.cards[cardId];
      s.deck.push({ id: cardId, key: Run.nextCardKey(s), owner: def.owner, assigned: def.owner === 'common' ? s.party[0].charId : null, gained: true });
      p.cards = []; this.log(`카드 획득: ${def.name}`);
    }
    skipCard() {
      const s = this.s; const p = s.pending;
      if (!p || p.type !== 'reward') throw new RuleError('BAD_REWARD');
      if (p.cards.length) {
        s.coins += 15;
        if (this.has('virtue_of_refusal') && s.runFlags.refusals < 4) { s.runFlags.refusals += 1; s.maxHp += 2; s.hp += 2; }
      }
      p.cards = [];
    }
    takeRelic(id) {
      const s = this.s; const p = s.pending;
      if (!p || !p.relic || !p.relic.includes(id)) throw new RuleError('BAD_REWARD');
      s.relics.push(id); p.relic = null; this.log(`유물 획득: ${this.ix.relics[id].name}`);
    }
    takePart() { const s = this.s; const p = s.pending; if (!p || !p.part) throw new RuleError('BAD_REWARD'); s.partsFound.push(p.part); p.part = null; }
    evolve(key, branchId) {
      const s = this.s; const p = s.pending;
      if (!p || !p.evolutions) throw new RuleError('BAD_REWARD');
      const ev = p.evolutions.find((x) => x.key === key);
      if (!ev || !ev.options.includes(branchId)) throw new RuleError('BAD_EVOLUTION');
      const d = s.deck.find((c) => c.key === key);
      d.from = d.id; d.id = branchId; d.evolved = true;
      p.evolutions = p.evolutions.filter((x) => x.key !== key);
      this.log(`진화: ${this.ix.cards[branchId].name}`);
    }
    proceed() {
      const s = this.s;
      if (!['REWARD', 'REST', 'FORGE', 'SHOP', 'EVENT'].includes(s.phase)) throw new RuleError('WRONG_PHASE');
      const n = this.nodeById(s.pos);
      s.pending = null;
      if (n && n.type === 'boss') {
        if (s.zoneIdx < s.zones.length - 1) { s.phase = 'ARCHIVE'; s.pending = { type: 'archive' }; return; }
        s.phase = 'VICTORY'; s.result = { outcome: 'clear', floor: s.floor }; s.pending = { type: 'save', defeat: false }; this.captureMemory(); return;
      }
      s.phase = 'MAP';
    }
    archive(choice) {
      const s = this.s;
      if (s.phase !== 'ARCHIVE') throw new RuleError('WRONG_PHASE');
      if (choice === 'return') { s.phase = 'VICTORY'; s.result = { outcome: 'return', floor: s.floor }; s.pending = { type: 'save', defeat: false }; this.captureMemory(); return; }
      s.hp = Math.min(s.maxHp, s.hp + Math.floor(s.maxHp * 0.15));
      s.zoneIdx += 1; s.zone = s.zones[s.zoneIdx]; s.pos = null; s.floor = 0; s.map = Run.genMap(s, s.zone); s.phase = 'MAP'; s.pending = null;
    }
    assign(key, charId) {
      const d = this.s.deck.find((c) => c.key === key);
      if (!d || d.owner !== 'common' || !this.s.party.some((p) => p.charId === charId)) throw new RuleError('BAD_ASSIGN');
      if (this.s.memoryPolicy === 'v2' && Object.values(this.s.memoryBefore).some(rows => rows.some(c => c.key === key))) throw new RuleError('REASSIGNMENT_REQUIRES_SEPARATE_FLOW');
      d.assigned = charId;
      if (this.s.pending?.type === 'save') this.captureMemory();
    }
    // rest
    rest(choice, key) {
      const s = this.s; const p = s.pending;
      if (s.phase !== 'REST' || !p || p.uses <= 0) throw new RuleError('WRONG_PHASE');
      if (choice === 'heal') {
        if (this.has('memory_anvil')) throw new RuleError('CANNOT_HEAL');
        const pct = 30 + (this.has('old_compass') ? 10 : 0);
        s.hp = Math.min(s.maxHp, s.hp + Math.floor(s.maxHp * pct / 100));
      } else if (choice === 'upgrade') {
        const d = s.deck.find((c) => c.key === key);
        if (!d || d.plus || d.weapon) throw new RuleError('BAD_UPGRADE');
        d.plus = true;
      } else throw new RuleError('BAD_CHOICE');
      p.uses -= 1;
    }
    // forge (공방): variation (random part offers for a chosen card) or remove a card
    forgeOffer(key) {
      const s = this.s; const p = s.pending;
      if (s.phase !== 'FORGE' || !p || p.uses <= 0) throw new RuleError('WRONG_PHASE');
      const d = s.deck.find((c) => c.key === key); if (!d || d.weapon) throw new RuleError('BAD_CARD');
      p.offers = { key, parts: this.partChoices(false, 3) };
      return p.offers;
    }
    forgeApply(partId) {
      const s = this.s; const p = s.pending;
      if (!p || !p.offers || !p.offers.parts.includes(partId)) throw new RuleError('BAD_PART');
      const d = s.deck.find((c) => c.key === p.offers.key);
      const part = this.ix.parts[partId];
      d.mods = { ...(d.mods || {}), [part.slot === 'circuit' ? 'c' : 'e']: partId };
      p.offers = null; p.uses -= 1;
    }
    forgeRemove(key) {
      const s = this.s; const p = s.pending;
      if (s.phase !== 'FORGE' || !p || p.uses <= 0) throw new RuleError('WRONG_PHASE');
      const i = s.deck.findIndex((c) => c.key === key); if (i < 0 || s.deck[i].weapon) throw new RuleError('BAD_CARD');
      s.deck.splice(i, 1); p.uses -= 1;
    }
    applyFoundPart(partId, key) {
      const s = this.s; const i = s.partsFound.indexOf(partId); if (i < 0) throw new RuleError('NO_PART');
      const d = s.deck.find((c) => c.key === key); if (!d || d.weapon) throw new RuleError('BAD_CARD');
      const part = this.ix.parts[partId];
      if (s.memoryPolicy === 'v2' && (!part || !['circuit', 'engraving'].includes(part.slot))) throw new RuleError('INVALID_PART');
      d.mods = { ...(d.mods || {}), [part.slot === 'circuit' ? 'c' : 'e']: partId };
      s.partsFound.splice(i, 1);
      if (s.pending?.type === 'save') this.captureMemory();
    }
    // shop
    makeShop() {
      const s = this.s;
      const chars = s.party.map((p) => p.charId);
      const cards = [];
      for (let k = 0; k < 3; k++) { const ch = Rng.pick(s.rng, chars); const pool = this.data.cards.filter((c) => c.owner === ch && c.pool !== false && !c.start); const c = Rng.pick(s.rng, pool); cards.push({ id: c.id, price: { B: 45, A: 70, S: 110 }[c.rar] || 60 }); }
      const parts = this.partChoices(false, 2).map((id) => ({ id, price: this.ix.parts[id].rarity === 'rare' ? 90 : 60 }));
      const relics = this.relicChoices('shop').map((id) => ({ id, price: this.ix.relics[id].rarity === 'rare' ? 180 : 120 }));
      return { type: 'shop', cards, parts, relics, removeCost: 50 + 25 * (s.runFlags.removals || 0) };
    }
    buy(kind, id, key) {
      const s = this.s; const p = s.pending;
      if (s.phase !== 'SHOP') throw new RuleError('WRONG_PHASE');
      if (kind === 'remove') {
        if (p.removeCost === null) throw new RuleError('NOT_FOR_SALE'); if (s.coins < p.removeCost) throw new RuleError('COINS_LOW'); // one removal per shop
        const i = s.deck.findIndex((c) => c.key === key); if (i < 0 || s.deck[i].weapon) throw new RuleError('BAD_CARD');
        s.coins -= p.removeCost; s.deck.splice(i, 1); s.runFlags.removals = (s.runFlags.removals || 0) + 1; p.removeCost = null; return;
      }
      const list = p[kind]; const item = list && list.find((x) => x.id === id && !x.sold);
      if (!item) throw new RuleError('NOT_FOR_SALE');
      if (s.coins < item.price) throw new RuleError('COINS_LOW');
      s.coins -= item.price; item.sold = true;
      if (kind === 'cards') { const def = this.ix.cards[id]; s.deck.push({ id, key: Run.nextCardKey(s), owner: def.owner, assigned: def.owner === 'common' ? s.party[0].charId : null, gained: true }); }
      if (kind === 'parts') s.partsFound.push(id);
      if (kind === 'relics') s.relics.push(id);
    }
    // event
    eventChoose(idx, key) {
      const s = this.s; const p = s.pending;
      if (s.phase !== 'EVENT') throw new RuleError('WRONG_PHASE');
      const ev = this.ix.events[p.id]; const ch = ev.choices[idx]; if (!ch || p.done) throw new RuleError('BAD_CHOICE'); // an event resolves once
      const fx = ch.fx;
      if (ch.need && ch.need.coins && s.coins < ch.need.coins) throw new RuleError('COINS_LOW');
      const hpk = (v) => Math.round(v * (s.hpRef || 81 * s.party.length) / (81 * s.party.length)); // flat event HP numbers scale with party HP
      if (fx.hp && s.hp + hpk(fx.hp) < 1) throw new RuleError('WOULD_KILL');
      if (fx.coins) s.coins = Math.max(0, s.coins + fx.coins);
      if (fx.hp) s.hp = Math.max(1, s.hp + hpk(fx.hp));
      if (fx.heal) s.hp = Math.min(s.maxHp, s.hp + hpk(fx.heal));
      if (fx.maxHp) { s.maxHp += hpk(fx.maxHp); s.hp = Math.min(s.hp, s.maxHp); }
      if (fx.maxHpPct) { s.maxHp = Math.max(1, Math.floor(s.maxHp * (100 + fx.maxHpPct) / 100)); s.hp = Math.min(s.hp, s.maxHp); }
      if (fx.nextApBonus) s.runFlags.nextApBonus = (s.runFlags.nextApBonus || 0) + fx.nextApBonus;
      if (fx.curse) s.deck.push({ id: fx.curse, key: Run.nextCardKey(s), owner: 'curse', gained: true });
      if (fx.relic) { const r = this.relicChoices(fx.relic === 'common' ? 'common' : 'elite')[0]; if (r) s.relics.push(r); }
      if (fx.card) { const chs = s.party.map((x) => x.charId); const pool = this.data.cards.filter((c) => chs.includes(c.owner) && c.rar === fx.card && c.pool !== false); const c = Rng.pick(s.rng, pool); s.deck.push({ id: c.id, key: Run.nextCardKey(s), owner: c.owner, gained: true }); }
      if (fx.part) s.partsFound.push(...this.partChoices(false, fx.part, true).slice(0, 1));
      if (fx.partRare) s.partsFound.push(...this.partChoices(true, 1));
      if (fx.variation) { s.phase = 'FORGE'; s.pending = { type: 'forge', uses: fx.variation, offers: null, fromEvent: true }; return; }
      if (fx.growth) { const g = s.deck.filter((c) => this.ix.cards[c.id].growth && (c.growth || 0) < 3); if (g.length) { const d = Rng.pick(s.rng, g); d.growth = (d.growth || 0) + 1; } }
      // card choices: key may be one deck key or an array (one per copied/removed card). Missing picks fall back to
      // curses first, then the first non-weapon card (used by bots and by the prototype's auto-pick).
      const keys = Array.isArray(key) ? key : (key ? [key] : []);
      if (fx.cardCopy) { const d = s.deck.find((c) => c.key === keys[0] && !c.weapon) || s.deck.find((c) => !c.weapon); s.deck.push({ ...clone(d), key: Run.nextCardKey(s), gained: true }); }
      if (fx.cardRemove) {
        for (let k = 0; k < fx.cardRemove; k++) {
          let i = keys[k] ? s.deck.findIndex((c) => c.key === keys[k] && !c.weapon) : -1;
          if (i < 0) i = s.deck.findIndex((c) => c.owner === 'curse');
          if (i < 0) i = s.deck.findIndex((c) => !c.weapon);
          if (i >= 0) s.deck.splice(i, 1);
        }
      }
      p.done = true; p.choice = idx;
    }

    // ───── memory save (카제나식 세이브) ─────
    // changes relative to each character's base kit; capacity limits what can be saved.
    static capacityOf(ix, partyEntry) {
      const ch = ix.chars[partyEntry.charId];
      let cap = ix.rules.capacityBase;
      const capRules = ix.raw.progression && ix.raw.progression.stats.capacity;
      if (capRules) for (const a of capRules.ascension) if ((partyEntry.asc || 0) >= a.asc) cap += a.add;
      for (const node of ch.resonance.slice(0, partyEntry.res || 0)) {
        const fxs = node.fx.type === 'multi' ? node.fx.list : [node.fx];
        for (const f of fxs) if (f.type === 'capacity') cap += f.n;
      }
      return cap;
    }
    memoryChanges() {
      if (this.s.memoryPolicy === 'v2') {
        const s = this.s; const after = s.memoryAfter || this.memorySnapshot();
        const changes = MemoryV2.listChanges(this.data, s.memoryBefore, after);
        const out = {};
        for (const p of s.party) {
          const cid = p.charId;
          out[cid] = { capacity: Run.capacityOf(this.ix, p), before: clone(s.memoryBefore[cid]), after: clone(after[cid]),
            usedBefore: MemoryV2.cost(this.data, cid, s.memoryBefore[cid], s.memoryBaseline[cid]),
            changes: changes.filter(ch => ch.cid === cid).map(ch => {
              const card = [...after[cid], ...s.memoryBefore[cid]].find(c => c.key === ch.key);
              const name = this.ix.cards[card.id].name;
              const label = ch.kind === 'add' ? `${name} 추가` : ch.kind === 'remove' ? `${name} 제거` : ch.kind === 'evolve' ? `${name} 진화` : ch.kind === 'upgrade' ? `${name} 강화 변경` : `${name} ${ch.value.slot === 'c' ? '회로' : '각인'} ${ch.value.part ? '교체' : '제거'}`;
              return { ...ch, label };
            }) };
        }
        return out;
      }
      const s = this.s; const ix = this.ix; const C = ix.rules.memoryCost; const out = {};
      for (const p of s.party) {
        const base = Run.baseKit(ix, p.charId);
        const mine = s.deck.filter((c) => !c.weapon && (c.owner === p.charId || (c.owner === 'common' && c.assigned === p.charId)));
        const changes = [];
        const pool = [...mine];
        // match base cards
        for (const b of base) {
          const i = pool.findIndex((c) => c.id === b.id || c.from === b.id);
          if (i < 0) { changes.push({ type: 'remove', id: b.id, cost: C.remove, label: `${ix.cards[b.id].name} 제거` }); continue; }
          const c = pool.splice(i, 1)[0];
          if (c.from === b.id) changes.push({ type: 'evo', key: c.key, id: c.id, from: b.id, cost: C.evolution, label: `${ix.cards[b.id].name} → ${ix.cards[c.id].name}` });
          if (c.plus) changes.push({ type: 'plus', key: c.key, id: c.id, cost: C.upgrade, label: `${ix.cards[c.id].name} 강화(+)` });
          for (const slot of ['c', 'e']) if (c.mods && c.mods[slot]) { const part = ix.parts[c.mods[slot]]; changes.push({ type: 'part', key: c.key, slot, part: part.id, cost: part.rarity === 'rare' ? C.partRare : C.partCommon, label: `${ix.cards[c.id].name}에 「${part.name}」` }); }
        }
        for (const c of pool) {
          if (c.owner === 'curse') continue;
          const def = ix.cards[c.from || c.id];
          const addCost = c.owner === 'common' ? C.addCommon : ({ B: C.addB, A: C.addA, S: C.addS }[def.rar] || C.addA);
          changes.push({ type: 'add', key: c.key, id: c.id, cost: addCost + (c.plus ? C.upgrade : 0) + (c.from ? C.evolution : 0)
            + ['c', 'e'].reduce((a, sl) => a + (c.mods && c.mods[sl] ? (ix.parts[c.mods[sl]].rarity === 'rare' ? C.partRare : C.partCommon) : 0), 0),
          label: `${ix.cards[c.id].name}${c.plus ? '+' : ''} 추가` });
        }
        out[p.charId] = { capacity: Run.capacityOf(ix, p), changes };
      }
      return out;
    }
    // selection: {charId: [indices of changes to keep]} → returns new memory {charId:[spec]}
    commitMemory(selection, currentMemory) {
      if (this.s.memoryPolicy === 'v2') return this.commitMemoryV2(selection, currentMemory);
      const s = this.s; const ix = this.ix;
      if (!s.pending || s.pending.type !== 'save') throw new RuleError('WRONG_PHASE');
      const all = this.memoryChanges();
      const defeat = s.pending.defeat;
      if (!selection || typeof selection !== 'object' || Array.isArray(selection)) throw new RuleError('BAD_CHOICE', 'memory selection must be an object');
      for (const [cid, indices] of Object.entries(selection)) {
        if (!all[cid] || !Array.isArray(indices) || new Set(indices).size !== indices.length ||
            indices.some((i) => !Number.isInteger(i) || i < 0 || i >= all[cid].changes.length)) {
          throw new RuleError('BAD_CHOICE', 'invalid memory selection');
        }
      }
      const totalKept = Object.values(selection).reduce((a, l) => a + l.length, 0);
      if (defeat && totalKept > 1) throw new RuleError('DEFEAT_KEEP_ONE');
      const memory = {};
      for (const p of s.party) {
        const info = all[p.charId]; const keep = new Set(selection[p.charId] || []);
        const cost = [...keep].reduce((a, i) => a + (info.changes[i]?.cost || 0), 0);
        if (cost > info.capacity) throw new RuleError('OVER_CAPACITY', { charId: p.charId, cost, capacity: info.capacity });
        // rebuild: start from base kit, apply kept changes
        const specs = Run.baseKit(ix, p.charId).map((b) => ({ ...b }));
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
      }
      s.savedMemory = memory; s.pending = { type: 'done' }; s.phase = 'DONE';
      return memory;
    }

    deckMemorySnapshot() {
      const out = Object.fromEntries(this.s.party.map(p => [p.charId, []]));
      for (const c of this.s.deck) {
        const def = this.ix.cards[c.id];
        if (!def || c.weapon || c.token || ![...this.s.party.map(p => p.charId), 'common'].includes(def.owner)) continue;
        const cid = def.owner === 'common' ? c.assigned : def.owner;
        if (!out[cid]) throw new RuleError('INVALID_ASSIGNMENT');
        const sp = { key: c.key, id: c.id };
        if (c.plus) sp.plus = true;
        if (c.from) sp.from = c.from;
        if (c.mods) sp.mods = clone(c.mods);
        if (def.owner === 'common') sp.assigned = cid;
        out[cid].push(sp);
      }
      MemoryV2.inspect(this.data, out);
      return out;
    }
    memorySnapshot() {
      const after = this.deckMemorySnapshot(); const s = this.s;
      if (!s.playBefore) return after;
      // Playing the base kit does not itself remove or revert persistent cards.
      const changes = MemoryV2.listChanges(this.data, s.playBefore, after);
      const savedKeys = new Set(Object.values(s.memoryBefore).flatMap(rows => rows.map(c => c.key)));
      const additions = new Map();
      for (const ch of changes) if (!savedKeys.has(ch.key) && ch.kind !== 'remove') additions.set(ch.key, { ...ch, kind: 'add', value: after[ch.cid].find(c => c.key === ch.key) });
      const result = MemoryV2.applyChanges(s.memoryBefore, [...changes.filter(ch => savedKeys.has(ch.key)), ...additions.values()]);
      MemoryV2.inspect(this.data, result);
      return result;
    }
    captureMemory() { if (this.s.memoryPolicy === 'v2') this.s.memoryAfter = this.memorySnapshot(); }
    previewMemory(selectedIds) {
      const s = this.s;
      if (s.memoryPolicy !== 'v2' || s.pending?.type !== 'save') throw new RuleError('WRONG_PHASE');
      const capacities = Object.fromEntries(s.party.map(p => [p.charId, Run.capacityOf(this.ix, p)]));
      return { ...MemoryV2.apply(this.data, s.memoryBefore, s.memoryAfter || this.memorySnapshot(), selectedIds, capacities,
        { defeat: !!s.pending.defeat, baseline: s.memoryBaseline, preview: true }), capacities };
    }
    commitMemoryV2(request, current) {
      const s = this.s;
      if (!request || typeof request.requestId !== 'string' || !request.requestId || !Number.isInteger(request.expectedMemoryVersion) || request.expectedMemoryVersion < 0 ||
          !Array.isArray(request.selectedChangeIds) || request.selectedChangeIds.some(id => typeof id !== 'string') || new Set(request.selectedChangeIds).size !== request.selectedChangeIds.length) throw new RuleError('INVALID_SELECTION');
      if (request.runId !== s.runId) throw new RuleError('RUN_MISMATCH');
      if (!current || current.schema !== 'mf-memory-v2' || !Number.isInteger(current.version) || current.version < 0 || !Array.isArray(current.history) || !current.receipts || typeof current.receipts !== 'object' || Array.isArray(current.receipts)) throw new RuleError('INVALID_SNAPSHOT');
      MemoryV2.inspect(this.data, current.memory);
      const body = JSON.stringify({ runId: request.runId, expectedMemoryVersion: request.expectedMemoryVersion, selectedChangeIds: [...request.selectedChangeIds].sort() });
      const finish = response => { s.savedMemory = clone(response.memory); s.pending = { type: 'done' }; s.phase = 'DONE'; };
      if (Object.hasOwn(current.receipts, request.requestId)) {
        const receipt = current.receipts[request.requestId];
        if (receipt.body !== body) throw new RuleError('REQUEST_CONFLICT');
        const response = clone(receipt.response); finish(response); return { state: clone(current), response };
      }
      if (s.pending?.type !== 'save' || !['VICTORY', 'DEFEAT'].includes(s.phase)) throw new RuleError('WRONG_PHASE');
      if (request.expectedMemoryVersion !== s.memoryVersion || current.version !== request.expectedMemoryVersion) throw new RuleError('VERSION_CONFLICT');
      for (const p of s.party) if (JSON.stringify(current.memory[p.charId] || s.memoryBaseline[p.charId]) !== JSON.stringify(s.memoryBefore[p.charId])) throw new RuleError('VERSION_CONFLICT');
      const result = this.previewMemory(request.selectedChangeIds);
      if (result.error) throw new RuleError(result.error);
      const state = clone(current); state.memory = { ...state.memory, ...result.memory }; state.version += 1;
      MemoryV2.inspect(this.data, state.memory);
      state.history.push({ runId: s.runId, requestId: request.requestId, version: state.version, before: clone(s.memoryBefore), memory: clone(result.memory), selectedChangeIds: [...request.selectedChangeIds] });
      const response = { memory: clone(result.memory), used: result.used, version: state.version };
      state.receipts = { ...state.receipts, [request.requestId]: { body, response: clone(response) } };
      finish(response);
      return { state, response };
    }
  }

  // ───────────────────────── gacha ─────────────────────────
  const Gacha = {
    newState(seed) {
      return { rng: Rng.seed(seed ?? 777), crystals: 0, tickets: { limitedTicket: 0, weaponTicket: 0, standardTicket: 0 }, shards: 0, afterglow: 0,
        pity: { character: { n: 0, guar: false, srN: 0, srGuar: false }, weapon: { n: 0, guar: false, srN: 0, srGuar: false }, standard: { n: 0, srN: 0, total: 0, selectorUsed: false }, beginner: { n: 0, srN: 0, total: 0, done: false, gotSsr: false } },
        owned: { chars: {}, weapons: {} }, history: [], spent: { crystals: 0, pulls: 0 }, ssrLog: [] };
    },
    ssrRate(curve, n) { // n = pulls since last SSR, this pull is n+1
      const k = n + 1;
      if (k >= curve.hard) return BPS;
      if (k >= curve.softStart) return Math.min(BPS, curve.ssrBase + curve.softStep * (k - curve.softStart + 1));
      return curve.ssrBase;
    },
    pull(data, g, bannerId, count) {
      const cfg = data.gacha; const banner = cfg.banners.find((b) => b.id === bannerId);
      if (!banner) throw new RuleError('UNKNOWN_BANNER');
      if (count !== 1 && count !== 10) throw new RuleError('BAD_COUNT');
      if (banner.kind === 'beginner') {
        if (g.pity.beginner.done || (g.pity.beginner.total || 0) + count > cfg.curves.beginner.maxPulls) throw new RuleError('BEGINNER_LIMIT');
        if (count !== 10) throw new RuleError('BEGINNER_TEN_ONLY');
      }
      // payment: tickets first, then crystals
      const ticketCost = banner.kind === 'beginner' ? cfg.curves.beginner.tenPullTickets : count;
      let fromTicket = Math.min(g.tickets[banner.ticket] || 0, ticketCost);
      let crystalCost = (ticketCost - fromTicket) * cfg.costPerPull;
      if (g.crystals < crystalCost) throw new RuleError('CRYSTALS_LOW', { need: crystalCost, have: g.crystals });
      g.tickets[banner.ticket] -= fromTicket; g.crystals -= crystalCost;
      g.spent.crystals += crystalCost + fromTicket * cfg.costPerPull; g.spent.pulls += count;
      const out = [];
      for (let i = 0; i < count; i++) out.push(Gacha.one(data, g, banner));
      g.history.unshift(...out.map((r) => ({ ...r, banner: bannerId })).reverse());
      if (g.history.length > 300) g.history.length = 300;
      if (banner.kind === 'beginner' && (g.pity.beginner.total || 0) >= cfg.curves.beginner.maxPulls) g.pity.beginner.done = true;
      return out;
    },
    one(data, g, banner) {
      const cfg = data.gacha; const s = g.rng;
      const kind = banner.kind === 'weapon' ? 'weapon' : banner.kind === 'character' ? 'character' : banner.kind === 'beginner' ? 'beginner' : 'standard';
      const curve = cfg.curves[kind]; const p = g.pity[kind];
      const ssrBp = Gacha.ssrRate(curve, p.n);
      const roll = Rng.below(s, BPS);
      let rarity;
      if (roll < ssrBp) rarity = 'SSR';
      else if (p.srN + 1 >= curve.srGuarantee) rarity = 'SR';
      else if (roll < ssrBp + curve.srBase) rarity = 'SR';
      else rarity = 'R';
      if (kind === 'beginner' && rarity !== 'SSR' && !p.gotSsr && p.n + 1 >= curve.maxPulls) rarity = 'SSR';
      const result = { rarity, kind: 'item', id: null, featured: false, pityAt: p.n + 1 };
      if (rarity === 'SSR') {
        g.ssrLog.unshift({ banner: banner.id, at: p.n + 1 });
        p.n = 0; p.srN = 0; if (kind === 'beginner') p.gotSsr = true; // an SSR also satisfies "SR or better"
        if (g.ssrLog.length > 50) g.ssrLog.length = 50;
        if (kind === 'character' || kind === 'weapon') {
          const win = p.guar || Rng.below(s, BPS) < curve.featuredSsrBps;
          if (win) { result.id = banner.featuredSsr; result.featured = true; p.guar = false; }
          else { result.id = Rng.pick(s, kind === 'weapon' ? cfg.pools.standardSsrWeapons : cfg.pools.standardSsrChars); p.guar = true; result.lost = true; }
        } else result.id = Rng.pick(s, cfg.pools.standardSsrChars);
        result.kind = kind === 'weapon' ? 'weapon' : 'char';
      } else if (rarity === 'SR') {
        p.n += 1; p.srN = 0;
        if ((kind === 'character' || kind === 'weapon') && (p.srGuar || Rng.below(s, BPS) < curve.featuredSrBps)) {
          result.id = Rng.pick(s, banner.featuredSr); result.featured = true; p.srGuar = false;
          result.kind = kind === 'weapon' ? 'weapon' : 'char';
        } else {
          if (kind === 'character' || kind === 'weapon') p.srGuar = true;
          const poolChars = kind === 'weapon' ? [] : cfg.pools.srChars; const poolW = cfg.pools.srWeapons;
          const pool = [...poolChars.map((x) => ['char', x]), ...poolW.map((x) => ['weapon', x])];
          const [k, id] = Rng.pick(s, pool); result.kind = k; result.id = id;
        }
      } else { p.n += 1; p.srN += 1; result.id = Rng.pick(s, cfg.rItems); }
      if (kind === 'standard' || kind === 'beginner') { g.pity.standard.total += 1; }
      if (kind === 'beginner') p.total = (p.total || 0) + 1;
      // ownership and duplicates
      const D = cfg.duplicates;
      if (result.kind === 'char') {
        const c = g.owned.chars[result.id] || 0; g.owned.chars[result.id] = c + 1;
        result.isNew = c === 0; result.resonance = Math.min(6, c);
        const ssr = rarity === 'SSR';
        // characters: first copy 0, resonance copies (2nd..7th) 10/2, beyond resonance 6 25/5
        result.shards = c === 0 ? 0 : c >= 7 ? (ssr ? D.shardSsrOverflow : D.shardSrOverflow) : (ssr ? D.shardSsr : D.shardSr);
        g.shards += result.shards;
      } else if (result.kind === 'weapon') {
        const c = g.owned.weapons[result.id] || 0; g.owned.weapons[result.id] = c + 1;
        result.isNew = c === 0; result.refine = Math.min(5, c + 1);
        const ssr = rarity === 'SSR';
        result.shards = c >= 5 ? (ssr ? D.shardSsrOverflow : D.shardSrOverflow) : (ssr ? D.shardSsr : D.shardSr);
        g.shards += result.shards;
      } else { g.afterglow += D.afterglowR; result.afterglow = D.afterglowR; }
      return result;
    },
    // standard banner: after 300 standard/beginner pulls, pick one standard SSR character once
    claimSelector(data, g, charId) {
      const cfg = data.gacha; const st = g.pity.standard;
      if (st.selectorUsed) throw new RuleError('SELECTOR_USED');
      if (st.total < cfg.curves.standard.selectorAt) throw new RuleError('SELECTOR_NOT_READY', { have: st.total, need: cfg.curves.standard.selectorAt });
      if (!cfg.pools.standardSsrChars.includes(charId)) throw new RuleError('SELECTOR_BAD_CHOICE');
      st.selectorUsed = true;
      const c = g.owned.chars[charId] || 0; g.owned.chars[charId] = c + 1;
      const D = cfg.duplicates; const shards = c >= 7 ? D.shardSsrOverflow : (c >= 1 ? D.shardSsr : 0);
      g.shards += shards;
      return { rarity: 'SSR', kind: 'char', id: charId, isNew: c === 0, resonance: Math.min(6, c), shards, selector: true };
    },
    resonance(g, charId) { return Math.max(0, Math.min(6, (g.owned.chars[charId] || 0) - 1)); },
    refine(g, wid) { return Math.max(1, Math.min(5, g.owned.weapons[wid] || 1)); },
    // exact statistics of the configured curve (for UI and docs)
    stats(curve, featuredBps) {
      let surv = 1; let mean = 0; const cdf = [];
      for (let k = 1; k <= curve.hard; k++) {
        const p = Gacha.ssrRate(curve, k - 1) / BPS;
        const pk = surv * p; mean += k * pk; surv *= 1 - p; cdf.push(1 - surv);
      }
      const featured = featuredBps ? mean * (2 - featuredBps / BPS) : mean;
      return { mean, consolidated: 1 / mean, featuredMean: featured, worst: featuredBps ? curve.hard * 2 : curve.hard, cdf };
    },
  };

  const MFCore = { RuleError, Rng, indexData, cardText, opText, Battle, Run, Gacha, Stats, MemoryV2, NODE_NAME, upgradeOps };
  if (typeof module !== 'undefined' && module.exports) module.exports = MFCore;
  root.MFCore = MFCore;
})(typeof globalThis !== 'undefined' ? globalThis : this);
