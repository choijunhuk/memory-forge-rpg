// Browser acceptance for the built single-file prototype. Run after `npm run build:proto`:
// PLAYWRIGHT_MODULE=<installed-playwright-path> node prototype/tests/memory_v2.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createServer } from 'node:http';
import { readFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
let playwright;
try { playwright = require(process.env.PLAYWRIGHT_MODULE || 'playwright'); }
catch { playwright = require('/opt/npm-tools/node_modules/playwright'); }
const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..', '..');
const MF = require(join(root, 'engine', 'rules-core.js'));
const DATA = JSON.parse(readFileSync(join(root, 'data', 'game_data.json'), 'utf8'));
const html = readFileSync(join(root, 'prototype', 'index.html'));
const screenshotDir = join(root, 'reports', 'development_20261015', 'screens');
const copy = x => JSON.parse(JSON.stringify(x));
const party = ['sera', 'harin', 'rien'].map(charId => ({ charId, res: 0 }));
let fixtureId = 0;
const initialMemory = () => Object.fromEntries(party.map(p => [p.charId, MF.MemoryV2.baseline(DATA, p.charId)]));
function record(memory = initialMemory()) {
  return { schema: 'mf-memory-v2', version: 0, memory: copy(memory), quarantine: {}, legacyBackup: {}, history: [], receipts: {} };
}
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

async function open(viewport = { width: 1280, height: 860 }, storage = {}) {
  const context = await browser.newContext({ viewport });
  await context.route(/fonts\.(googleapis|gstatic)\.com/, route => route.abort());
  await context.addInitScript(entries => {
    if (sessionStorage.getItem('__mf_test_bootstrap')) return;
    for (const [key, value] of Object.entries(entries)) localStorage.setItem(key, value);
    sessionStorage.setItem('__mf_test_bootstrap', '1');
  }, storage);
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' && !/ERR_FAILED|net::/.test(message.text())) errors.push(message.text());
  });
  await page.goto(`${origin}/?seed=7000`);
  await page.waitForSelector('.nav');
  return { page, context, errors };
}
async function click(page, selector) { await page.locator(selector).first().click(); }
async function screen(page) { return page.evaluate(() => window.__mf.screen); }
async function storage(page, key) { return page.evaluate(k => localStorage.getItem(k), key); }
async function shot(page, name) {
  mkdirSync(screenshotDir, { recursive: true });
  await page.screenshot({ path: join(screenshotDir, name), fullPage: false });
}
function checkpoint(run, screenName = 'save', selected = null, request = null) {
  return JSON.stringify({ schema: 'mf08-checkpoint', v: 1, run: run.s, screen: screenName, saveSel: selected, pendingSave: request, lastSave: null });
}
function saveFixture(state, mutate, defeat = false, fixtureParty = party) {
  const run = MF.Run.create(DATA, { party: fixtureParty, memory: state.memory, memoryPolicy: 'v2', memoryVersion: state.version,
    runId: `ui-fixture-${++fixtureId}`, seed: 11 });
  mutate(run);
  run.s.phase = defeat ? 'DEFEAT' : 'VICTORY';
  run.s.pending = { type: 'save', defeat };
  run.captureMemory();
  return run;
}
async function openFixture(state, run, viewport) {
  return open(viewport, { mf08_memory: JSON.stringify(state), mf08_checkpoint: checkpoint(run) });
}
async function selectDelta(page, cid, kind, key) {
  const id = await page.evaluate(({ cid, kind, key }) => window.__mf.run.memoryChanges()[cid].changes
    .find(c => c.kind === kind && (!key || c.key === key))?.id, { cid, kind, key });
  assert.ok(id, `missing ${kind} delta for ${cid}`);
  const box = page.locator(`[data-act="saveToggle"][data-cid="${cid}"][data-id="${id}"]`);
  assert.equal(await box.count(), 1, `missing visible toggle ${id}`);
  if (!(await box.isChecked())) await box.check();
  return id;
}

test('legacy migration preserves the exact old bytes in a separate backup', async () => {
  const legacy = { sera: MF.Run.baseKit(MF.indexData(DATA), 'sera') };
  legacy.sera[0].plus = true;
  const raw = JSON.stringify(legacy, null, 2);
  const { page, context, errors } = await open(undefined, { mf07_memory: raw });
  try {
    const current = await storage(page, 'mf08_memory');
    assert.ok(current, 'v2 record should be persisted');
    assert.equal(await storage(page, 'mf07_memory'), raw);
    const keys = await page.evaluate(() => Object.keys(localStorage));
    const backups = await page.evaluate(() => Object.entries(localStorage).filter(([k, v]) => k !== 'mf07_memory' && v === localStorage.getItem('mf07_memory')));
    assert.ok(backups.length >= 1, `raw legacy backup missing among ${keys.join(', ')}`);
    assert.equal(JSON.parse(current).memory.sera[0].plus, true);
    assert.deepEqual(errors, []);
  } finally { await context.close(); }
});

test('visible exploration defeats the party and no-selection save preserves its previous upgrade across reloads', async () => {
  const state = record(); state.memory.sera[0].plus = true;
  const { page, context, errors } = await open(undefined, { mf08_memory: JSON.stringify(state) });
  try {
    await click(page, '.nav [data-to="party"]');
    await click(page, '[data-act="startRun"]');
    assert.equal(await screen(page), 'map');
    const startKey = state.memory.sera[0].key;
    assert.equal(await page.evaluate(key => window.__mf.run.s.deck.find(c => c.key === key)?.plus, startKey), true);
    await click(page, 'g.node.avail');
    assert.equal(await screen(page), 'battle');
    for (let turn = 0; (await screen(page)) === 'battle' && turn < 30; turn++) await click(page, '[data-act="endTurn"]');
    assert.equal(await screen(page), 'save', 'enemy actions should end the battle in defeat');
    assert.equal(await page.evaluate(() => window.__mf.run.s.result?.outcome), 'defeat');
    await page.reload(); await page.waitForSelector('.nav');
    assert.equal(await screen(page), 'save', 'checkpoint should survive reload before commit');
    assert.deepEqual(await page.evaluate(() => window.__mf.saveSel), { sera: [], harin: [], rien: [] });
    await click(page, '[data-act="commitSave"]');
    assert.equal(await screen(page), 'result');
    await page.reload(); await page.waitForSelector('.nav');
    assert.equal(await screen(page), 'result', 'completion should survive reload');
    const saved = JSON.parse(await storage(page, 'mf08_memory'));
    assert.equal(saved.version, 1);
    assert.equal(saved.memory.sera.find(c => c.key === startKey).plus, true);
    assert.deepEqual(errors, []);
  } finally { await context.close(); }
});

test('full-result six plus three selection shows capacity error and disables commit', async () => {
  const state = record();
  state.memory.sera.push({ id: 'sera_phase', key: 'saved-a' }, { id: 'sera_lock', key: 'saved-b' });
  const run = saveFixture(state, r => {
    r.s.phase = 'REWARD'; r.s.pending = { type: 'reward', cards: ['sera_last'], evolutions: [] };
    r.takeCard('sera_last');
  });
  const { page, context, errors } = await openFixture(state, run);
  try {
    await selectDelta(page, 'sera', 'add');
    assert.equal(await page.locator('[data-act="commitSave"]').isDisabled(), true);
    assert.match(await page.locator('body').innerText(), /용량/);
    assert.equal((await page.evaluate(() => window.__mf.run.previewMemory(Object.values(window.__mf.saveSel).flat()))).error, 'OVER_CAPACITY');
    assert.deepEqual(errors, []);
  } finally { await context.close(); }
});

test('two same-kind cards expose separate stable-key toggles for one upgrade', async () => {
  const state = record(); state.memory.sera.push({ id: 'sera_round', key: 'extra-round' });
  const run = saveFixture(state, r => {
    r.s.phase = 'REST'; r.s.pending = { type: 'rest', uses: 1 };
    r.rest('upgrade', 'extra-round');
  });
  const { page, context, errors } = await openFixture(state, run);
  try {
    const selected = await selectDelta(page, 'sera', 'upgrade', 'extra-round');
    assert.deepEqual(await page.evaluate(() => window.__mf.saveSel.sera), [selected]);
    await click(page, '[data-act="commitSave"]');
    const saved = JSON.parse(await storage(page, 'mf08_memory'));
    assert.equal(saved.memory.sera.find(c => c.key === 'extra-round').plus, true);
    assert.equal(saved.memory.sera.find(c => c.key === state.memory.sera[0].key).plus, undefined);
    assert.deepEqual(errors, []);
  } finally { await context.close(); }
});

test('evolution-only selection preserves the card without its unselected upgrade', async () => {
  const state = record();
  delete state.memory.rien;
  state.memory.yuri = MF.MemoryV2.baseline(DATA, 'yuri');
  const run = saveFixture(state, r => {
    const card = r.s.deck.find(c => c.id === 'yuri_cocoon');
    card.growth = 3;
    r.s.phase = 'REWARD'; r.s.pending = { type: 'reward', cards: [], evolutions: r.pendingEvolutions() };
    r.evolve(card.key, 'yuri_cocoon_guard');
    r.s.phase = 'REST'; r.s.pending = { type: 'rest', uses: 1 };
    r.rest('upgrade', card.key);
  }, false, ['sera', 'harin', 'yuri'].map(charId => ({ charId, res: 0 })));
  const { page, context, errors } = await openFixture(state, run);
  try {
    const key = state.memory.yuri.find(c => c.id === 'yuri_cocoon').key;
    const selected = await selectDelta(page, 'yuri', 'evolve', key);
    assert.deepEqual(await page.evaluate(() => window.__mf.saveSel.yuri), [selected]);
    await click(page, '[data-act="commitSave"]');
    const saved = JSON.parse(await storage(page, 'mf08_memory'));
    const evolved = saved.memory.yuri.find(c => c.key === key);
    assert.equal(evolved.id, 'yuri_cocoon_guard');
    assert.equal(evolved.plus, undefined);
    assert.deepEqual(errors, []);
  } finally { await context.close(); }
});

test('storage-write failure leaves live memory and run unchanged for retry after reload', async () => {
  const state = record();
  const run = saveFixture(state, r => {
    r.s.phase = 'REST'; r.s.pending = { type: 'rest', uses: 1 };
    r.rest('upgrade', r.s.deck.find(c => c.id === 'sera_round').key);
  });
  const { page, context } = await openFixture(state, run);
  try {
    await selectDelta(page, 'sera', 'upgrade');
    const before = await page.evaluate(() => ({ memory: JSON.stringify(window.__mf.memoryState), run: JSON.stringify(window.__mf.run.s) }));
    await page.evaluate(() => {
      const original = Storage.prototype.setItem;
      window.__restoreStorageWrite = () => { Storage.prototype.setItem = original; };
      Storage.prototype.setItem = function (key, value) { if (key === 'mf08_memory') throw new DOMException('test quota', 'QuotaExceededError'); return original.call(this, key, value); };
    });
    await click(page, '[data-act="commitSave"]');
    assert.equal(await screen(page), 'save');
    assert.deepEqual(await page.evaluate(() => ({ memory: JSON.stringify(window.__mf.memoryState), run: JSON.stringify(window.__mf.run.s) })), before);
    await page.evaluate(() => window.__restoreStorageWrite());
    const pending = await storage(page, 'mf08_checkpoint');
    const requestId = JSON.parse(pending).pendingSave?.requestId;
    assert.ok(requestId, 'pending request must retain its request ID');
    await page.reload(); await page.waitForSelector('.nav');
    assert.equal(JSON.parse(await storage(page, 'mf08_checkpoint')).pendingSave.requestId, requestId);
    await click(page, '[data-act="saveRetry"]');
    assert.equal(await screen(page), 'result');
    const saved = JSON.parse(await storage(page, 'mf08_memory'));
    assert.equal(saved.version, 1);
  } finally { await context.close(); }
});

test('newer storage version rejects a stale in-progress save', async () => {
  const state = record();
  const run = saveFixture(state, r => {
    r.s.phase = 'REST'; r.s.pending = { type: 'rest', uses: 1 };
    r.rest('upgrade', r.s.deck.find(c => c.id === 'sera_round').key);
  });
  const { page, context } = await openFixture(state, run);
  try {
    await selectDelta(page, 'sera', 'upgrade');
    const newer = copy(state); newer.version = 1;
    await page.evaluate(value => localStorage.setItem('mf08_memory', JSON.stringify(value)), newer);
    await click(page, '[data-act="commitSave"]');
    assert.equal(await screen(page), 'save');
    assert.equal(JSON.parse(await storage(page, 'mf08_memory')).version, 1);
    assert.notEqual(await page.evaluate(() => window.__mf.run.s.phase), 'DONE');
  } finally { await context.close(); }
});

for (const [name, damage] of [
  ['non-array character rows', state => { state.memory.sera = 'corrupt rows'; }],
  ['missing history', state => { delete state.history; }],
  ['missing receipts', state => { delete state.receipts; }],
]) {
  test(`malformed v2 ${name} blocks start and preserves its raw record`, async () => {
    const state = record(); damage(state);
    const raw = JSON.stringify(state);
    const { page, context, errors } = await open(undefined, { mf08_memory: raw });
    try {
      assert.equal(await page.evaluate(() => window.__mf.memStatus.code), 'MEMORY_MALFORMED');
      await click(page, '.nav [data-to="party"]');
      assert.equal(await page.locator('[data-act="startRun"]').isDisabled(), true);
      assert.match(await page.locator('body').innerText(), /기억 기록.*읽을 수 없어요/);
      assert.equal(await storage(page, 'mf08_memory'), raw);
      assert.equal(await page.evaluate(() => window.__mf.run), null);
      assert.deepEqual(errors, []);
    } finally { await context.close(); }
  });
}

test('deleting v2 memory after boot blocks a new run instead of using cached memory', async () => {
  const { page, context, errors } = await open(undefined, { mf08_memory: JSON.stringify(record()) });
  try {
    await click(page, '.nav [data-to="party"]');
    assert.equal(await page.locator('[data-act="startRun"]').isEnabled(), true);
    await page.evaluate(() => localStorage.removeItem('mf08_memory'));
    await click(page, '[data-act="startRun"]');
    assert.equal(await screen(page), 'party');
    assert.equal(await page.evaluate(() => window.__mf.run), null);
    assert.equal(await storage(page, 'mf08_memory'), null);
    assert.match(await page.locator('body').innerText(), /기억 기록.*사라졌어요/);
    assert.deepEqual(errors, []);
  } finally { await context.close(); }
});

test('an oversized but well-formed v2 deck blocks start without truncation or render errors', async () => {
  const state = record();
  state.memory.sera.push({ id: 'sera_phase', key: 'saved-a' }, { id: 'sera_lock', key: 'saved-b' }, { id: 'sera_last', key: 'saved-c' });
  assert.equal(MF.MemoryV2.cost(DATA, 'sera', state.memory.sera), 9);
  const raw = JSON.stringify(state);
  const { page, context, errors } = await open(undefined, { mf08_memory: raw });
  try {
    await click(page, '.nav [data-to="party"]');
    assert.equal(await page.locator('[data-act="startRun"]').isDisabled(), true);
    assert.match(await page.locator('body').innerText(), /용량/);
    assert.equal(await page.evaluate(() => window.__mf.run), null);
    assert.equal(await storage(page, 'mf08_memory'), raw);
    assert.doesNotMatch(await page.locator('body').innerText(), /화면을 그리다 오류/);
    assert.deepEqual(errors, []);
  } finally { await context.close(); }
});

test('memory and save layouts fit desktop, mobile, and 130 percent text', async () => {
  for (const viewport of [{ width: 1280, height: 860 }, { width: 390, height: 844 }]) {
    const state = record();
    const run = saveFixture(state, r => {
      r.s.phase = 'REST'; r.s.pending = { type: 'rest', uses: 1 };
      r.rest('upgrade', r.s.deck.find(c => c.id === 'sera_round').key);
    });
    const { page, context, errors } = await openFixture(state, run, viewport);
    try {
      assert.equal(await screen(page), 'save', 'the screenshot must show the v2 save preview');
      await shot(page, `memory-v2-save-${viewport.width}.png`);
      assert.ok((await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)) <= 1);
      await click(page, '.nav [data-to="settings"]');
      await click(page, '[data-act="scale"][data-v="130"]');
      await click(page, '.nav [data-to="memory"]');
      await shot(page, `memory-v2-130-${viewport.width}.png`);
      assert.ok((await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)) <= 1);
      assert.deepEqual(errors, []);
    } finally { await context.close(); }
  }
});
