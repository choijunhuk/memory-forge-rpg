/*
 * Memory Forge v0.8.1 — content modes reference: §11.5 성장 던전 · §11.6 주간 보스 · §11.7 기록 균열 · §11.13 일일 활동.
 * Pure account-ledger functions (inputs are cloned, never mutated) plus a Battle subclass for waves and rift rules.
 * Mode rule hooks live here. engine/rules-core.js is source-pinned by the JS and C# vector fixtures; S4 owned support is the one
 * explicitly reviewed exception to keeping it byte-identical (no blanket JS parity waiver: old no-support graphs must stay exact).
 * A dungeon/weekly launch may carry one owned non-party support build beside its party (support adds no party member, card or HP).
 * Numbers come from data/game_data.json → progression and data/content_modes.json; items the
 * source leaves open are DECISION_PENDING there and throw CONTENT_NOT_AUTHORED instead of being invented.
 * Account, memory and party rows are server-held state; the client only names content and a party.
 */
(function (root) {
  'use strict';
  const MF = typeof module !== 'undefined' && module.exports ? require('./rules-core.js') : root.MFCore;
  const { Battle, Run, Stats, Rng, RuleError, indexData } = MF;
  const clone = (o) => JSON.parse(JSON.stringify(o));
  const BPS = 10000;
  const DAY = 86400000; const HOUR = 3600000;
  const RULES = ['rift_reserve', 'rift_break', 'rift_ink', 'rift_debt', 'rift_bloom', 'rift_echo'];

  // ───────────────────────── reset clocks (§13.3) ─────────────────────────
  const isoDate = (ms) => new Date(ms).toISOString().slice(0, 10);
  function shifted(modes, nowMs, hour) {
    if (!Number.isFinite(nowMs)) throw new RuleError('INVALID_TIME');
    return nowMs + modes.resets.utcOffsetMinutes * 60000 - hour * HOUR;
  }
  const Time = {
    // KST day that starts at 05:00
    dayKey(modes, nowMs) { return isoDate(shifted(modes, nowMs, modes.resets.dailyHour)); },
    // Monday date of the KST week that starts Monday 05:00
    weekKey(modes, nowMs) {
      const t = shifted(modes, nowMs, modes.resets.weeklyHour);
      return isoDate(t - ((new Date(t).getUTCDay() - modes.resets.weeklyWeekday + 7) % 7) * DAY);
    },
    // §13.3: cycles start on season day 1·15·29 at 11:00 KST. seasonStartDate is the KST calendar date of day 1 (ops config).
    riftCycle(data, modes, seasonStartDate, nowMs) {
      if (typeof seasonStartDate !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(seasonStartDate) || !Number.isFinite(Date.parse(seasonStartDate))) throw new RuleError('INVALID_SEASON');
      if (!Number.isFinite(nowMs)) throw new RuleError('INVALID_TIME');
      const R = data.progression.recordRift; const off = modes.resets.utcOffsetMinutes * 60000;
      const start0 = Date.parse(`${seasonStartDate}T00:00:00Z`) - off + modes.rift.cycleStartHour * HOUR;
      const index = Math.floor((nowMs - start0) / (R.cycleDays * DAY));
      if (!(index >= 0 && index < R.cyclesPerSeason)) throw new RuleError('RIFT_CLOSED');
      const startsAt = start0 + index * R.cycleDays * DAY;
      return { index, key: isoDate(startsAt + off), startsAt, endsAt: startsAt + R.cycleDays * DAY };
    },
  };

  // ───────────────────────── shared setup ─────────────────────────
  function normAccount(acc) {
    if (!acc || !Number.isInteger(acc.level) || acc.level < 1 || !Number.isInteger(acc.stamina) || acc.stamina < 0 ||
      !Array.isArray(acc.owned) || acc.owned.some((c) => typeof c !== 'string')) throw new RuleError('INVALID_ACCOUNT');
    const a = clone(acc);
    a.dungeons = a.dungeons || {}; a.weekly = a.weekly || { key: null, claimed: 0 };
    a.rift = a.rift || { key: null, floors: {} }; a.daily = a.daily || { key: null, counts: {}, claimed: [] };
    return a;
  }

  // party: [{charId, level, weaponId, weaponLevel, armor, accessory, skillLv, ultLv, res, refine}] → derived stats (as Run.create)
  // memory: {charId: [spec]} saved deck (base kit when absent) + each wielder's weapon card. Deck, reservations and gauge start fresh.
  function partySetup(ix, party, memory, owned) {
    if (!Array.isArray(party) || party.length < 1 || party.length > 3) throw new RuleError('PARTY_SIZE');
    if (new Set(party.map((p) => p && p.charId)).size !== party.length) throw new RuleError('PARTY_DUPLICATE');
    const out = clone(party); const deck = [];
    for (const p of out) {
      if (!ix.chars[p.charId]) throw new RuleError('UNKNOWN_CHARACTER', p.charId);
      if (!owned.includes(p.charId)) throw new RuleError('NOT_OWNED', p.charId);
      const d = Stats.derive(ix, p);
      Object.assign(p, { power4: d.power4, hp: d.hp, gearBps: d.gearBps, skillLv: d.skillLv, ultLv: d.ultLv });
      const specs = (memory && memory[p.charId]) || Run.baseKit(ix, p.charId);
      if (!Array.isArray(specs)) throw new RuleError('INVALID_SNAPSHOT');
      for (const sp of specs) {
        const c = sp && ix.cards[sp.id];
        if (!c || ![p.charId, 'common'].includes(c.owner) || sp.token || sp.weapon) throw new RuleError('INVALID_CARD', sp && sp.id);
        deck.push({ id: sp.id, plus: !!sp.plus, mods: sp.mods || null, growth: sp.growth || 0, assigned: c.owner === 'common' ? p.charId : null, from: sp.from || null });
      }
      const w = ix.weapons[p.weaponId] || ix.weapons[Run.defaultWeapon(ix, p.charId)];
      deck.push({ id: w.card, wielder: p.charId });
    }
    return { party: out, deck, maxHp: out.reduce((a, p) => a + p.hp, 0) };
  }

  // stage level scaling (§11.1) combined with a row's extra hpBps/dmgBps (weekly boss 5, rift floor 6)
  function stageDiff(ix, row) {
    const sc = Stats.stageScale(ix, row.stage);
    return row.hpBps || row.dmgBps ? Stats.combine({ hp: (row.hpBps || BPS) / 100, dmg: (row.dmgBps || BPS) / 100 }, sc) : sc;
  }

  // rift_break is a data rule: every enemy break gauge (including boss phases and summons) ×gaugeBps, broken bonus replaced.
  function modeData(data, mode) {
    const raw = data.__ix ? data.raw : data;
    if (!mode || mode.rule !== 'rift_break') return raw;
    const d = clone(raw); const p = mode.params;
    const up = (n) => Math.max(1, Math.round(n * p.gaugeBps / BPS));
    d.rules.brokenDamageBps = p.brokenDamageBps;
    for (const e of d.enemies) { e.brk = up(e.brk); if (e.phase) e.phase.brk = up(e.phase.brk); }
    return d;
  }

  // ───────────────────────── battle with waves and rift rules ─────────────────────────
  // state.mode = {kind, contentId, rule, params, waves (remaining), wave, settled, ...}. Only ModeBattle states carry it.
  class ModeBattle extends Battle {
    static create(data, setup, mode) {
      if (mode.rule && (!RULES.includes(mode.rule) || !mode.params)) throw new RuleError('UNKNOWN_RULE', mode.rule);
      const m = { ...clone(mode), rule: mode.rule || null, params: mode.rule ? clone(mode.params) : null, waves: clone(mode.waves || []), wave: 1, settled: false };
      const base = Battle.create(modeData(data, m), setup);
      const s = base.s; s.mode = m;
      // caps add on top of relic/resonance results, which Battle.create has already applied
      if (m.rule === 'rift_debt') s.debtCap += m.params.debtCapAdd;
      if (m.rule === 'rift_bloom') s.growthCap += m.params.growthCapAdd;
      if (m.rule === 'rift_echo') s.echoMax += m.params.echoMaxAdd;
      const b = new ModeBattle(base.ix, s);
      b.checkInvariants();
      return b;
    }
    // rebuild from persisted state (the rift_break data rule is not part of the state)
    static restore(data, state) { return new ModeBattle(modeData(data, state.mode), state); }

    // Battle#command builds its candidate with `new Battle`; this copy differs only there so the overrides survive commands.
    // content_modes.test.mjs compares both sources so upstream edits to Battle#command fail loudly.
    command(req) {
      const digest = JSON.stringify({ v: req.v, kind: req.kind, args: req.args || {} });
      if (req.rid && Object.hasOwn(this.receipts, req.rid)) {
        if (this.receipts[req.rid].digest !== digest) throw new RuleError('IDEMPOTENCY_CONFLICT');
        return clone(this.receipts[req.rid].resp);
      }
      if (req.v !== this.s.v) throw new RuleError('STALE_VERSION');
      const cand = new this.constructor(this.ix, clone(this.s));
      cand.s.ev = [];
      const a = req.args || {};
      switch (req.kind) {
        case 'begin': cand.begin(); break;
        case 'play': cand.play(a.uid, a.mode || 'now', a.target || null, a.picks || []); break;
        case 'ult': cand.ult(a.actor, a.target || null, a.picks || []); break;
        case 'support': cand.support(a.caller, a.target || null, a.picks || []); break;
        case 'end': cand.endTurn(); break;
        default: throw new RuleError('UNKNOWN_COMMAND');
      }
      cand.s.v += 1;
      cand.checkInvariants();
      cand.checkSupportNumbers();
      const resp = { v: cand.s.v, phase: cand.s.phase, ev: cand.s.ev };
      this.s = cand.s;
      if (req.rid) Object.defineProperty(this.receipts, req.rid, { value: { digest, resp: clone(resp) }, enumerable: true, writable: true, configurable: true });
      return resp;
    }

    rule(id) { return this.s.mode && this.s.mode.rule === id ? this.s.mode.params : null; }

    // §11.5 waves: clearing a wave brings the next one into the same battle (turn count, HP, hand and reservations carry on)
    checkEnd() {
      const s = this.s; const m = s.mode;
      if (m && m.waves.length && !this.isOver() && s.hp > 0 && this.alive().length === 0) {
        const wave = m.waves.shift(); m.wave += 1;
        for (const eid of wave) this.setIntentsFor(Battle.addEnemy(s, this.ix, eid));
        this.ev('wave', { wave: m.wave, left: m.waves.length });
        return false;
      }
      return super.checkEnd();
    }

    // rift_reserve / rift_ink / rift_debt: additive bonuses on party card and ultimate damage
    dmgBonusBps(ctx, enemy) {
      let add = super.dmgBonusBps(ctx, enemy);
      const rs = this.rule('rift_reserve'); if (rs) add += ctx.held ? rs.reservedBps : rs.immediateBps; // RULES_NOW_PLAY: held cards take the reserved bonus
      const ink = this.rule('rift_ink'); if (ink) add += ink.perExhaustBps * this.counterVal('exhausted', ctx);
      const debt = this.rule('rift_debt'); if (debt && this.debtTotal() > 0) add += debt.debtDamageBps;
      return add;
    }
    // rift_debt "모든 피해": party raw damage too (counter, burn, relic all route through scaleAtk), one floor at the end
    scaleAtk(n) {
      const debt = this.rule('rift_debt');
      if (!debt || this.debtTotal() <= 0) return super.scaleAtk(n);
      return Math.floor(n * this.partyP4() * (BPS + debt.debtDamageBps) / (160 * BPS));
    }

    // rift_ink: smudge into the discard pile at every turn start, before reservations and the draw
    startTurn(first = false) {
      const ink = this.rule('rift_ink');
      if (ink) this.enemyMove(null, { t: 'insert', card: 'smudge', n: ink.smudgePerTurn });
      super.startTurn(first);
    }

    // rift_bloom: block when a growth card resolves (raw, like greenhouse)
    onGrowthCardResolved(inst) {
      super.onGrowthCardResolved(inst);
      const bloom = this.rule('rift_bloom'); if (bloom) this.gainBlockRaw(bloom.growthBlock, 'rift');
    }

    // rift_echo: three consecutive character cards from different characters → AP, once per turn. A card without an owner
    // character (common, curse, token) breaks the chain; ultimates are not cards and leave it alone.
    play(uid, mode, target, picks) {
      const inst = this.s.cards[uid]; const oc = inst ? this.ownerChar(inst) : null;
      super.play(uid, mode, target, picks);
      const echo = this.rule('rift_echo');
      if (!echo || this.isOver()) return;
      const f = this.s.turnFlags;
      f.riftChain = oc ? [...(f.riftChain || []), oc].slice(-echo.distinctChain) : [];
      if (!f.riftEcho && f.riftChain.length === echo.distinctChain && new Set(f.riftChain).size === echo.distinctChain) {
        f.riftEcho = true; this.s.ap = Math.min(this.ix.rules.apMax, this.s.ap + echo.apGain); this.ev('riftEcho', { ap: echo.apGain });
      }
    }
  }

  // S4 standalone support: an owned non-party partner's frozen build (Run.supportEntry), separate from the party, deck and HP
  function supportSetup(ix, support, party, owned) {
    if (support === undefined || support === null) return undefined;
    if (support && typeof support === 'object' && !owned.includes(support.charId)) throw new RuleError('NOT_OWNED', support.charId);
    return Run.supportEntry(ix, support, party);
  }
  function startModeBattle(data, account, opts, row, enemies, mode) {
    const ix = indexData(data);
    const t = partySetup(ix, opts.party, opts.memory, account.owned);
    const support = supportSetup(ix, opts.support, t.party, account.owned);
    const b = ModeBattle.create(data, { party: t.party, deck: t.deck, hp: t.maxHp, maxHp: t.maxHp, seed: opts.seed ?? 1, difficulty: stageDiff(ix, row), enemies: enemies[0], support },
      { ...mode, waves: enemies.slice(1) });
    b.do('begin');
    return b;
  }
  function settleable(bs, kind) {
    if (!bs || !bs.mode || bs.mode.kind !== kind) throw new RuleError('WRONG_CONTENT');
    if (bs.mode.settled) throw new RuleError('ALREADY_SETTLED');
    if (bs.phase !== 'VICTORY' && bs.phase !== 'DEFEAT') throw new RuleError('BATTLE_NOT_OVER');
    const out = clone(bs); out.mode.settled = true;
    return out;
  }

  // ───────────────────────── §11.5 growth dungeon ─────────────────────────
  function dungeonRows(data, dungeonId, tier) {
    const P = data.progression;
    const d = P.dungeons.find((x) => x.id === dungeonId); if (!d) throw new RuleError('UNKNOWN_CONTENT', dungeonId);
    const t = P.dungeonTiers.find((x) => x.tier === tier); if (!t) throw new RuleError('UNKNOWN_TIER', tier);
    return { d, t };
  }
  const Dungeon = {
    // one run at a tier: tier-6 yield × tier pct, integer floor (content/progression.mjs)
    reward(data, dungeonId, tier) {
      const { d, t } = dungeonRows(data, dungeonId, tier);
      return Object.fromEntries(Object.entries(d.yield).map(([k, v]) => [k, Math.floor(v * t.pct / 100)]));
    },
    // star 1 victory, star 2 within maxTurns, star 3 party HP ≥ minHpPct (each on top of victory)
    stars(modes, result, maxHp) {
      const st = modes.dungeon.stars;
      if (!result || result.phase !== 'VICTORY') return 0;
      return 1 + (result.turn <= st.maxTurns ? 1 : 0) + (result.hp * 100 >= maxHp * st.minHpPct ? 1 : 0);
    },
    start(data, modes, account, opts) {
      const a = normAccount(account); const { d, t } = dungeonRows(data, opts.dungeonId, opts.tier);
      if (a.level < d.unlock) throw new RuleError('LOCKED');
      if (a.stamina < d.cost) throw new RuleError('NOT_ENOUGH_STAMINA');
      const W = modes.dungeon.waves;
      const waves = W.encounters && W.encounters[d.id] && W.encounters[d.id][String(t.tier)];
      if (!waves) throw new RuleError('CONTENT_NOT_AUTHORED', 'dungeon.waves');
      if (!Array.isArray(waves) || waves.length < W.minWaves || waves.length > W.maxWaves || waves.some((w) => !Array.isArray(w) || !w.length)) throw new RuleError('INVALID_CONTENT', 'dungeon.waves');
      return startModeBattle(data, a, opts, t, waves, { kind: 'dungeon', contentId: d.id, tier: t.tier });
    },
    // victory: spend 집중, pay the tier reward, record best stars. Defeat stays unsettled until defeatStamina is authored.
    settle(data, modes, account, bs) {
      const out = settleable(bs, 'dungeon'); const a = normAccount(account);
      if (bs.phase === 'DEFEAT') throw new RuleError('CONTENT_NOT_AUTHORED', 'dungeon.defeatStamina');
      const { d, t } = dungeonRows(data, bs.mode.contentId, bs.mode.tier);
      if (a.stamina < d.cost) throw new RuleError('NOT_ENOUGH_STAMINA');
      const stars = Dungeon.stars(modes, bs.result, bs.maxHp);
      const key = `${d.id}:${t.tier}`;
      a.stamina -= d.cost; a.dungeons[key] = Math.max(a.dungeons[key] || 0, stars);
      const grant = { ...Dungeon.reward(data, d.id, t.tier), accountXp: d.cost * data.progression.accountLevel.xpSources.staminaSpent };
      return { account: a, battle: out, stars, grant };
    },
    // instant clears after a 3-star clear of that tier: same reward and 집중 per run, at most sweepMax per call
    sweep(data, modes, account, { dungeonId, tier, times }) {
      const a = normAccount(account); const { d, t } = dungeonRows(data, dungeonId, tier); const S = data.progression.dungeonStars;
      if (!Number.isInteger(times) || times < 1 || times > S.sweepMax) throw new RuleError('INVALID_SWEEP');
      if (a.level < d.unlock) throw new RuleError('LOCKED');
      if ((a.dungeons[`${d.id}:${t.tier}`] || 0) < S.sweepUnlock) throw new RuleError('SWEEP_LOCKED');
      if (a.stamina < d.cost * times) throw new RuleError('NOT_ENOUGH_STAMINA');
      a.stamina -= d.cost * times;
      const one = Dungeon.reward(data, d.id, t.tier);
      const grant = Object.fromEntries(Object.entries(one).map(([k, v]) => [k, v * times]));
      grant.accountXp = d.cost * times * data.progression.accountLevel.xpSources.staminaSpent;
      return { account: a, grant };
    },
  };

  // ───────────────────────── §11.6 weekly boss ─────────────────────────
  const WeeklyBoss = {
    // practice: true never pays; otherwise a victory pays while the shared weekly counter is below claimsPerWeek
    start(data, modes, account, opts) {
      const a = normAccount(account); const W = data.progression.weeklyBoss;
      if (a.level < W.unlock) throw new RuleError('LOCKED');
      if (!modes.weeklyBoss.bosses.includes(opts.bossId)) throw new RuleError('UNKNOWN_CONTENT', opts.bossId);
      const row = W.difficulty.find((x) => x.lv === opts.difficulty); if (!row) throw new RuleError('UNKNOWN_TIER', opts.difficulty);
      return startModeBattle(data, a, opts, row, [[opts.bossId]], { kind: 'weekly_boss', contentId: opts.bossId, difficulty: row.lv, practice: !!opts.practice });
    },
    // §11.6: the victory record and the weekly counter are checked together. Every ended battle settles once, so a
    // victory beyond the limit cannot be held back for next week; a defeat never touches the counter.
    settle(data, modes, account, bs, nowMs) {
      const out = settleable(bs, 'weekly_boss'); const a = normAccount(account); const W = data.progression.weeklyBoss;
      const week = Time.weekKey(modes, nowMs);
      if (a.weekly.key !== week) a.weekly = { key: week, claimed: 0 };
      let reason = null;
      if (bs.phase === 'DEFEAT') reason = 'DEFEAT';
      else if (bs.mode.practice) reason = 'PRACTICE';
      else if (a.weekly.claimed >= W.claimsPerWeek) reason = 'WEEKLY_LIMIT';
      out.mode.rewarded = !reason;
      if (reason) return { account: a, battle: out, grant: null, reason };
      a.weekly.claimed += 1;
      return { account: a, battle: out, grant: { ...W.perClaim, accountXp: data.progression.accountLevel.xpSources.weeklyBossClaim }, reason: null };
    },
  };

  // ───────────────────────── §11.7 record rift floor ─────────────────────────
  // Three battles per floor. Each team's HP carries across its battles on the floor; every battle builds a fresh deck,
  // reservations and gauge. Two-team floors fight team order [0, 1, 0].
  class RiftRun {
    constructor(data, modes, state) {
      this.data = data; this.modes = modes; this.ix = indexData(data); this.s = state;
      this.battle = state.battle ? ModeBattle.restore(data, state.battle) : null;
    }

    // opts: {floor, cycle: {index, key} (Time.riftCycle), teams: [party, party?], memory, seed, policy?: 'v0.8.1' | 'target'}
    static create(data, modes, account, opts) {
      const a = normAccount(account); const ix = indexData(data); const R = data.progression.recordRift; const M = modes.rift;
      if (a.level < R.unlock) throw new RuleError('LOCKED');
      const row = R.floors.find((f) => f.floor === opts.floor); if (!row) throw new RuleError('UNKNOWN_TIER', opts.floor);
      const cycle = opts.cycle || {};
      if (!Number.isInteger(cycle.index) || cycle.index < 0 || cycle.index >= R.cyclesPerSeason || typeof cycle.key !== 'string') throw new RuleError('INVALID_CYCLE');
      const cyc = M.rotation.cycles && M.rotation.cycles[cycle.index];
      if (!cyc) throw new RuleError('CONTENT_NOT_AUTHORED', 'rift.rotation');
      const rule = cyc.rules && cyc.rules[row.floor - 1]; const encounters = cyc.encounters && cyc.encounters[row.floor - 1];
      if (!M.modifiers[rule] || !RULES.includes(rule) || !Array.isArray(encounters) || encounters.length !== 3 || encounters.some((e) => !Array.isArray(e) || !e.length)) throw new RuleError('INVALID_CONTENT', 'rift.rotation');
      const teamsByFloor = M.teamsByFloor[opts.policy || M.activeTeamPolicy];
      if (!Array.isArray(teamsByFloor)) throw new RuleError('UNKNOWN_POLICY', opts.policy);
      const need = teamsByFloor[row.floor - 1];
      if (!Array.isArray(opts.teams) || opts.teams.length !== need) throw new RuleError('TEAM_COUNT', need);
      const teams = opts.teams.map((p) => { const t = partySetup(ix, p, opts.memory, a.owned); return { party: t.party, deck: t.deck, hp: t.maxHp, maxHp: t.maxHp }; });
      let starCap = 3;
      if (need === 2) {
        // §11.7 "서로 다른 6명": two full parties
        if (teams.some((t) => t.party.length !== M.distinctCharacters / 2)) throw new RuleError('TEAM_SIZE', M.distinctCharacters / 2);
        const first = new Set(teams[0].party.map((p) => p.charId));
        if (teams[1].party.some((p) => first.has(p.charId))) {
          // §11.7: re-entry from team 1 only when fewer than six characters are owned; the floor then caps at 2 stars
          if (a.owned.length >= M.distinctCharacters) throw new RuleError('PARTY_OVERLAP');
          starCap = M.smallRosterMaxStars;
        }
      }
      const s = {
        schema: 'mf-rift-floor-0.8.1', floor: row.floor, cycleIndex: cycle.index, cycleKey: cycle.key, rule, params: clone(M.modifiers[rule]),
        difficulty: stageDiff(ix, row), encounters: clone(encounters), teams, order: clone(need === 2 ? M.battles.twoTeamOrder : M.battles.oneTeamOrder),
        idx: 0, results: [], starCap, phase: 'READY', rng: Rng.seed(opts.seed ?? 1), battle: null, settled: false,
      };
      return new RiftRun(data, modes, s);
    }

    next() {
      const s = this.s;
      if (s.phase !== 'READY') throw new RuleError('WRONG_PHASE');
      const team = s.teams[s.order[s.idx]];
      const b = ModeBattle.create(this.data, { party: team.party, deck: team.deck, hp: team.hp, maxHp: team.maxHp, seed: Rng.next(s.rng), difficulty: s.difficulty, enemies: s.encounters[s.idx] },
        { kind: 'rift', contentId: `floor:${s.floor}`, battle: s.idx + 1, rule: s.rule, params: s.params });
      b.do('begin');
      this.battle = b; s.battle = b.s; s.phase = 'BATTLE';
      if (b.isOver()) this.afterBattle();
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
      s.teams[s.order[s.idx]].hp = bs.hp;
      s.results.push({ battle: s.idx + 1, team: s.order[s.idx], phase: bs.phase, turn: bs.turn, hp: bs.hp });
      this.battle = null; s.battle = null;
      if (bs.phase === 'DEFEAT') s.phase = 'FAILED';
      else if (s.idx === s.order.length - 1) s.phase = 'CLEARED';
      else { s.idx += 1; s.phase = 'READY'; }
    }

    // star 1 all three battles won, star 2 total turns ≤ maxTotalTurns, star 3 remaining team HP ≥ minHpPct. A cleared floor
    // cannot be scored while either threshold is DECISION_PENDING; a failed floor is 0 stars.
    stars() {
      const s = this.s; const st = this.modes.rift.stars;
      const turns = s.results.reduce((n, r) => n + r.turn, 0);
      const hp = s.teams.reduce((n, t) => n + t.hp, 0); const maxHp = s.teams.reduce((n, t) => n + t.maxHp, 0);
      if (s.phase !== 'CLEARED') return { stars: 0, turns, hp, maxHp };
      if (st.maxTotalTurns == null || st.minHpPct == null) throw new RuleError('CONTENT_NOT_AUTHORED', 'rift.stars');
      const n = 1 + (turns <= st.maxTotalTurns ? 1 : 0) + (hp * 100 >= maxHp * st.minHpPct ? 1 : 0);
      return { stars: Math.min(n, s.starCap), turns, hp, maxHp };
    }
  }
  const Rift = {
    // §11.7: per floor once per cycle, paid by star portions (60/20/20 for stars 1/2/3). A later better clear in the same
    // cycle pays only the portions not yet paid, so a floor never exceeds its reward per cycle.
    settle(data, modes, account, state) {
      if (!state || state.schema !== 'mf-rift-floor-0.8.1') throw new RuleError('WRONG_CONTENT');
      if (state.settled) throw new RuleError('ALREADY_SETTLED');
      if (state.phase !== 'CLEARED' && state.phase !== 'FAILED') throw new RuleError('RIFT_NOT_OVER');
      const a = normAccount(account); const M = modes.rift;
      if (a.rift.key !== state.cycleKey) {
        if (a.rift.key && a.rift.key > state.cycleKey) throw new RuleError('STALE_CYCLE');
        a.rift = { key: state.cycleKey, floors: {} };
      }
      const { stars } = new RiftRun(data, modes, clone(state)).stars();
      const row = data.progression.recordRift.floors.find((f) => f.floor === state.floor);
      const paid = a.rift.floors[state.floor] || 0;
      let crystals = 0;
      for (let i = paid; i < stars; i++) crystals += Math.floor(row.reward * M.starPortionsPct[i] / 100);
      a.rift.floors[state.floor] = Math.max(paid, stars);
      const out = clone(state); out.settled = true;
      return { account: a, rift: out, stars, grant: crystals ? { crystals } : null };
    },
  };

  // ───────────────────────── §11.13 daily activity (100 points) ─────────────────────────
  // v0.8.1 policy: the five §11.11 tasks. target policy: a selectable pool (DECISION_PENDING) where any mix reaches 100.
  const Daily = {
    tasks(data, modes, policy) {
      const D = modes.daily; const p = policy || D.activePolicy;
      if (p === 'v0.8.1') return data.progression.missions.daily.tasks.map((t) => ({ id: t.id, pts: t.pts, ...D.goals[t.id] }));
      if (p === 'target') { if (!D.targetPool.tasks) throw new RuleError('CONTENT_NOT_AUTHORED', 'daily.targetPool'); return clone(D.targetPool.tasks); }
      throw new RuleError('UNKNOWN_POLICY', p);
    },
    open(data, modes, account, nowMs) {
      const a = normAccount(account);
      if (a.level < modes.daily.unlock) throw new RuleError('LOCKED');
      const key = Time.dayKey(modes, nowMs);
      if (a.daily.key !== key) a.daily = { key, counts: {}, claimed: [] };
      return a;
    },
    record(data, modes, account, event, nowMs, policy) {
      const a = Daily.open(data, modes, account, nowMs);
      const events = new Set(Daily.tasks(data, modes, policy).map((t) => t.event));
      if (!event || !events.has(event.type) || !Number.isInteger(event.n) || event.n < 1) throw new RuleError('INVALID_EVENT');
      a.daily.counts[event.type] = (a.daily.counts[event.type] || 0) + event.n;
      return a;
    },
    points(data, modes, account, nowMs, policy) {
      const a = Daily.open(data, modes, account, nowMs);
      const sum = Daily.tasks(data, modes, policy).reduce((n, t) => n + ((a.daily.counts[t.event] || 0) >= t.n ? t.pts : 0), 0);
      return Math.min(modes.daily.pointsCap, sum);
    },
    // milestone rewards from progression.missions.daily; reaching the cap is the day's completion (§11.9 account XP)
    claim(data, modes, account, at, nowMs, policy) {
      const a = Daily.open(data, modes, account, nowMs);
      const ms = data.progression.missions.daily.milestones.find((m) => m.at === at);
      if (!ms) throw new RuleError('UNKNOWN_MILESTONE', at);
      if (a.daily.claimed.includes(at)) throw new RuleError('ALREADY_CLAIMED');
      if (Daily.points(data, modes, a, nowMs, policy) < at) throw new RuleError('NOT_ENOUGH_POINTS');
      a.daily.claimed.push(at);
      const { at: _at, ...grant } = ms;
      if (at === modes.daily.pointsCap) grant.accountXp = data.progression.accountLevel.xpSources.dailyMissionComplete;
      return { account: a, grant };
    },
  };

  const ContentModes = { Time, partySetup, stageDiff, modeData, ModeBattle, Dungeon, WeeklyBoss, RiftRun, Rift, Daily, RULES };
  if (typeof module !== 'undefined' && module.exports) module.exports = ContentModes;
  root.MFContentModes = ContentModes;
})(typeof globalThis !== 'undefined' ? globalThis : this);
