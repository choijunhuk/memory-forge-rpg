// Candidate-only browser acceptance. Run after npm run build:proto.
// PLAYWRIGHT_MODULE=<installed playwright> PLAYWRIGHT_CHROMIUM_EXECUTABLE=<browser> node --test prototype/tests/late_enemy_actions.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createServer } from 'node:http';
import { readFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const playwright = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..', '..');
const MF = require(join(root, 'engine', 'rules-core.js'));
const BASE = JSON.parse(readFileSync(join(root, 'data', 'game_data.json'), 'utf8'));
const DATA = require(join(root, 'proposals', 'late_enemies.cjs')).withLateEnemies(BASE);
const source = readFileSync(join(root, 'prototype', 'index.html'), 'utf8');
const html = source.replace(/(<script id="mf-data" type="application\/json">).*?(<\/script>)/s,
  (_, open, close) => open + JSON.stringify(DATA) + close);
assert.notEqual(html, source, 'built prototype must have an mf-data JSON script');
const shots = join(root, 'reports', 'late_actions_20261015', 'screens');
const copy = x => JSON.parse(JSON.stringify(x));
const party = ['sera', 'harin', 'rien'].map(charId => ({ charId, res: 0 }));
const memory = Object.fromEntries(party.map(p => [p.charId, MF.MemoryV2.baseline(DATA, p.charId)]));
const memoryRecord = { schema: 'mf-memory-v2', version: 0, memory, quarantine: {}, legacyBackup: {}, history: [], receipts: {} };
let fixtureId = 0;

const server = createServer((req, res) => {
  if (new URL(req.url, 'http://127.0.0.1').pathname !== '/') { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
  res.end(html);
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;
const browser = await playwright.chromium.launch(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE
  ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE } : {});
test.after(async () => { await browser.close(); await new Promise(resolve => server.close(resolve)); });

function fixture(enemies, { deck = Array(16).fill('harin_shield'), relics = [], mutate } = {}) {
  const run = MF.Run.create(DATA, { party, memory, memoryPolicy: 'v2', memoryVersion: 0,
    runId: `late-candidate-ui-${++fixtureId}`, seed: 11 });
  const battle = MF.Battle.create(DATA, { party, deck: deck.map(id => ({ id })), relics,
    hp: 600, maxHp: 600, seed: 91, enemies });
  battle.do('begin');
  if (mutate) mutate(battle);
  run.s.phase = 'BATTLE'; run.s.hp = battle.s.hp; run.s.maxHp = battle.s.maxHp;
  run.s.battle = battle.s;
  return JSON.stringify({ schema: 'mf08-checkpoint', v: 1, screen: 'battle', run: run.s,
    saveSel: null, pendingSave: null, lastSave: null });
}

async function open(checkpoint, { width = 1280, height = 860, scale = 100 } = {}) {
  const context = await browser.newContext({ viewport: { width, height } });
  await context.route(/fonts\.(googleapis|gstatic)\.com/, route => route.abort());
  await context.addInitScript(({ checkpoint, record, scale }) => {
    localStorage.setItem('mf08_memory', record);
    localStorage.setItem('mf08_checkpoint', checkpoint);
    localStorage.setItem('mf07_settings', JSON.stringify({ scale, allOwned: true }));
  }, { checkpoint, record: JSON.stringify(memoryRecord), scale });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  page.on('console', m => { if (m.type() === 'error' && !/ERR_FAILED|net::/.test(m.text())) errors.push(m.text()); });
  await page.goto(`${origin}/?seed=7000`);
  await page.waitForSelector('.nav');
  assert.equal(await page.evaluate(() => window.__mf.screen), 'battle');
  return { page, context, errors };
}

async function shot(page, name) {
  mkdirSync(shots, { recursive: true });
  await page.screenshot({ path: join(shots, `${name}.png`), fullPage: false });
}
async function clean(page, errors) {
  assert.deepEqual(errors, []);
  assert.equal(await page.locator('text=화면을 그리다 오류').count(), 0);
  assert.doesNotMatch(await page.locator('body').innerText(), /undefined/);
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'document overflows horizontally');
}
const state = page => page.evaluate(() => window.__mf.run.battle.s);

test('candidate five-enemy end turn shows AP drain, silence, seal and eat effects', async () => {
  const ck = fixture(['e_collector', 'e_choir', 'e_timeshard', 'e_prompter', 'e_echo']);
  const { page, context, errors } = await open(ck);
  try {
    await shot(page, 'candidate-five-before');
    assert.equal(await page.locator('.enemy').count(), 5);
    assert.equal(await page.locator('.enemy .intent svg').count(), 5, 'each candidate intent has a defined icon');
    await page.locator('[data-act="endTurn"]').click();
    await page.locator('[data-act="toggleLog"]').click();
    await shot(page, 'candidate-five-after');
    assert.equal((await state(page)).ap, 2);
    assert.match(await page.locator('.battle').innerText(), /침묵: 이번 턴 예약 비용 \+1/);
    assert.match(await page.locator('.battle').innerText(), /봉인/);
    assert.match(await page.locator('.log').innerText(), /시간 착취/);
    assert.match(await page.locator('.log').innerText(), /이름 먹기/);
    await clean(page, errors);
  } finally { await context.close(); }
});

test('candidate collector previews AP after its pending drain and a due debt', async () => {
  const ck = fixture(['e_collector'], { mutate: b => b.s.debts.push({ n: 1, due: b.s.turn + 1 }) });
  const { page, context, errors } = await open(ck);
  try {
    await shot(page, 'candidate-ap-preview-before');
    assert.match(await page.locator('.battle').innerText(), /예상 다음 턴 행동력 1/);
    await page.locator('[data-act="endTurn"]').click();
    assert.equal((await state(page)).ap, 1);
    await clean(page, errors);
  } finally { await context.close(); }
});

test('stunned candidate collector is excluded from the pre-action AP drain preview', async () => {
  const ck = fixture(['e_collector'], { mutate: b => {
    b.s.debts.push({ n: 1, due: b.s.turn + 1 });
    b.alive()[0].stunned = true;
    b.setIntents();
  } });
  const { page, context, errors } = await open(ck);
  try {
    await shot(page, 'candidate-ap-preview-stunned');
    assert.match(await page.locator('.battle').innerText(), /예상 다음 턴 행동력 3/);
    await page.locator('[data-act="endTurn"]').click();
    assert.equal((await state(page)).ap, 3);
    await clean(page, errors);
  } finally { await context.close(); }
});

test('candidate prompter with no reservation logs a harmless empty delay', async () => {
  const { page, context, errors } = await open(fixture(['e_prompter']));
  try {
    await page.locator('[data-act="endTurn"]').click();
    await page.locator('[data-act="toggleLog"]').click();
    await shot(page, 'candidate-empty-delay');
    assert.match(await page.locator('.log').innerText(), /시간 지연.*예약.*없/);
    await clean(page, errors);
  } finally { await context.close(); }
});

test('candidate Nameless phase two displays its new weakness and pattern in detail', async () => {
  const ck = fixture(['b_nameless'], { deck: Array(16).fill('sera_round'), mutate: b => { b.alive()[0].hp = 4; } });
  const { page, context, errors } = await open(ck);
  try {
    await shot(page, 'candidate-phase-before');
    await page.locator('.hand [data-act="card"]').first().click();
    await page.locator('[data-act="mode"][data-mode="now"]').click();
    await shot(page, 'candidate-phase-after');
    assert.equal((await state(page)).enemies[0].phaseIndex, 1);
    await page.locator('.enemy').first().click();
    await shot(page, 'candidate-phase-detail');
    assert.match(await page.locator('.modal').innerText(), /2페이즈.*약점.*화염.*빙결/s);
    assert.match(await page.locator('.modal').innerText(), /칸 봉인/);
    await clean(page, errors);
  } finally { await context.close(); }
});

test('crowded candidate battle stays usable at desktop, mobile, and 130 percent text', async () => {
  const ck = fixture(['e_collector', 'e_choir', 'e_timeshard', 'e_prompter', 'e_echo'],
    { deck: Array(30).fill('harin_shield'), relics: ['infinite_circuit', 'cracked_hourglass'], mutate: b => {
      // fixture setup: a full ten-card hand against five enemies (RULES_NOW_PLAY: no reservation slots to fill)
      while (b.s.zones.hand.length < 10) b.s.zones.hand.push(b.s.zones.draw.pop());
      b.checkInvariants();
    } });
  for (const [name, opts] of [['desktop', { width: 1280, height: 860 }], ['mobile', { width: 390, height: 844 }],
    ['mobile-130', { width: 390, height: 844, scale: 130 }]]) {
    const { page, context, errors } = await open(ck, opts);
    try {
      await shot(page, `candidate-crowded-${name}`);
      assert.equal(await page.locator('.enemy').count(), 5);
      assert.equal(await page.locator('.hand .card').count(), 10);
      for (const sel of ['[data-act="endTurn"]', '[data-act="toggleLog"]', '.hand .card']) {
        const item = page.locator(sel).first();
        assert.equal(await item.isVisible(), true, `${name}: ${sel} hidden`);
        await item.scrollIntoViewIfNeeded();
        const box = await item.boundingBox();
        assert.ok(box && box.width > 0 && box.x >= -1 && box.x + box.width <= opts.width + 1, `${name}: ${sel} clipped`);
      }
      for (const sel of ['.enemy']) {
        const items = page.locator(sel);
        for (let i = 0; i < 5; i++) {
          const item = items.nth(i);
          await item.scrollIntoViewIfNeeded();
          await item.evaluate(el => {
            const box = el.getBoundingClientRect();
            const dock = document.querySelector('.dock').getBoundingClientRect().top;
            if (box.bottom > dock) window.scrollBy(0, box.bottom - dock + 12);
          });
          const access = await item.evaluate(el => {
            const box = el.getBoundingClientRect();
            const nav = document.querySelector('.nav').getBoundingClientRect().bottom;
            const dock = document.querySelector('.dock').getBoundingClientRect().top;
            const top = Math.max(box.top, nav); const bottom = Math.min(box.bottom, dock);
            const x = box.left + box.width / 2; const y = (top + bottom) / 2;
            return { visible: bottom - top, need: Math.min(44, box.height / 2),
              hit: bottom > top && el.contains(document.elementFromPoint(x, y)), box: box.toJSON(), nav, dock };
          });
          assert.ok(access.visible >= access.need && access.hit,
            `${name}: ${sel}[${i}] has no usable area between nav/dock: ${JSON.stringify(access)}`);
        }
      }
      if (name === 'mobile-130') await shot(page, 'candidate-crowded-mobile-130-scrolled');
      const last = page.locator('.hand [data-act="card"]').last();
      await last.scrollIntoViewIfNeeded();
      await last.click();
      assert.equal(await page.locator('.action [data-act="mode"]').count() > 0, true, `${name}: last hand card cannot be selected`);
      await clean(page, errors);
    } finally { await context.close(); }
  }
});
