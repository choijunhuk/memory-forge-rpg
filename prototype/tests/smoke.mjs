// Headless UI smoke test for prototype/index.html.
// Usage: node prototype/tests/smoke.mjs [runs=2] [--shots]
// Clicks through every screen, plays full runs only through the visible UI (no engine shortcuts),
// and fails on any page error, console error, or "화면을 그리다 오류" render fallback.
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { mkdirSync, readFileSync } from 'node:fs';

const require = createRequire(import.meta.url);
let playwright;
try { playwright = require(process.env.PLAYWRIGHT_MODULE || 'playwright'); } catch { playwright = require('/opt/npm-tools/node_modules/playwright'); }
const here = dirname(fileURLToPath(import.meta.url));
const page_url = process.env.MF_PROTO_URL || pathToFileURL(join(here, '..', 'index.html')).href;
const RUNS = Number(process.argv[2] || 2);
// deterministic clicker: seeded PRNG (mulberry32) + ?seed= for the page's run seeds → identical outcomes on every run
let rs = 20261014 >>> 0;
const rand = () => { rs = (rs + 0x6d2b79f5) >>> 0; let t = rs; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
const SHOTS = process.argv.includes('--shots');
const shotDir = join(here, 'shots');
if (SHOTS) mkdirSync(shotDir, { recursive: true });

const browser = await playwright.chromium.launch(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE } : {});
const errors = [];
const stats = { clicks: 0, battles: 0, turns: 0, plays: 0, picks: 0, runs: [], screens: new Set() };

async function newPage(viewport) {
  const ctx = await browser.newContext({ viewport });
  await ctx.route(/fonts\.(googleapis|gstatic)\.com/, (r) => r.abort());
  const page = await ctx.newPage();
  page.on('pageerror', (e) => errors.push(`pageerror: ${e.message}\n${e.stack || ''}`));
  page.on('console', (m) => { if (m.type() === 'error' && !/ERR_FAILED|net::/.test(m.text())) errors.push(`console: ${m.text()}`); });
  const testSeed = 7000 + Math.floor(rand() * 1000);
  if (process.env.MF_INLINE_HTML === '1') {
    // In-memory document loading for browser environments that disallow local URLs.
    // Only the test seed declaration is replaced; game rules and UI logic are unchanged.
    const html = readFileSync(join(here, '..', 'index.html'), 'utf8').replace(/const URL_SEED = [^\n]+/, `const URL_SEED = ${testSeed};`);
    await page.setContent(html, { waitUntil: 'load' });
  } else await page.goto(`${page_url}?seed=${testSeed}`);
  await page.waitForSelector('.nav');
  return page;
}
async function check(page, where) {
  const bad = await page.locator('text=화면을 그리다 오류').count();
  if (bad) errors.push(`render fallback at ${where}: ${await page.locator('.wrap .panel').first().innerText()}`);
  const scr = await page.evaluate(() => window.__mf.screen);
  stats.screens.add(scr);
  const ov = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  if (ov > 1) errors.push(`horizontal overflow ${ov}px on ${scr} at ${where} (viewport ${page.viewportSize().width})`);
  if (errors.length) throw new Error(`errors at ${where}:\n${errors.join('\n')}`);
}
async function click(page, sel, where) {
  await page.locator(sel).first().click();
  stats.clicks++;
  await check(page, where || sel);
}
const shot = async (page, name) => { if (SHOTS) await page.screenshot({ path: join(shotDir, `${name}.png`), fullPage: false }); };

// ── 1) static screens ─────────────────────────────────────────
for (const vp of [{ width: 1280, height: 860 }, { width: 390, height: 844 }]) {
  const page = await newPage(vp);
  const vshot = (pg, name) => shot(pg, `${name}-${vp.width}`);
  await vshot(page, 'home');
  await click(page, '.nav [data-to="party"]'); await vshot(page, 'party');
  for (const to of ['party', 'gacha', 'codex', 'memory', 'settings', 'home']) await click(page, `.nav [data-to="${to}"]`, `nav ${to}`);
  await click(page, '.nav [data-act="help"]', 'help'); await click(page, '.modal [data-act="closeModal"]', 'help close');
  await click(page, '.nav [data-to="codex"]');
  for (const id of await page.evaluate(() => JSON.parse(document.getElementById('mf-data').textContent).characters.map((c) => c.id))) await click(page, `[data-act="cchar"][data-id="${id}"]`, `codex ${id}`);
  await vshot(page, 'codex');
  for (const t of ['weapons', 'relics', 'enemies', 'chars']) await click(page, `[data-act="ctab"][data-v="${t}"]`, `codex tab ${t}`);
  // gacha: every banner, 1 + 10 pulls, top-ups, exchange
  await click(page, '.nav [data-to="gacha"]');
  await click(page, '[data-act="season"]', 'season income');
  await click(page, '[data-act="addCrystal"][data-n="16000"]');
  const banners = await page.evaluate(() => JSON.parse(document.getElementById('mf-data').textContent).gacha.banners.map((b) => b.id));
  for (const id of banners) {
    await click(page, `[data-act="gtab"][data-id="${id}"]`, `gacha tab ${id}`);
    const one = page.locator('[data-act="pull"][data-n="1"]');
    if (await one.isEnabled()) await click(page, '[data-act="pull"][data-n="1"]', `pull1 ${id}`);
    for (let k = 0; k < 3; k++) {
      const ten = page.locator('[data-act="pull"][data-n="10"]');
      if (!(await ten.isEnabled())) break;
      await click(page, '[data-act="pull"][data-n="10"]', `pull10 ${id}`);
      const n = await page.locator('.results .res').count();
      if (n !== 10) throw new Error(`banner ${id}: expected 10 results, got ${n}`);
    }
  }
  await vshot(page, 'gacha');
  const g = await page.evaluate(() => ({ pulls: window.__mf.gacha.spent.pulls, chars: Object.keys(window.__mf.gacha.owned.chars).length, weapons: Object.keys(window.__mf.gacha.owned.weapons).length }));
  console.log('gacha ok', JSON.stringify(g));
  await click(page, '[data-act="exchange"]', 'exchange');
  await click(page, '.nav [data-to="settings"]');
  for (const v of ['115', '130', '100']) await click(page, `[data-act="scale"][data-v="${v}"]`);
  await page.context().close();
}

// ── 2) full runs through the UI ────────────────────────────────
async function playRun(viewport, runIdx, opts) {
  const page = await newPage(viewport);
  await click(page, '.nav [data-to="party"]');
  // pick a party: clear then choose three
  const want = opts.party;
  for (const cur of await page.evaluate(() => window.__mf.party.chars.slice())) await click(page, `[data-act="pickChar"][data-id="${cur}"]`);
  for (const id of want) await click(page, `[data-act="pickChar"][data-id="${id}"]`);
  await click(page, `[data-act="len"][data-v="${opts.length}"]`);
  await click(page, `[data-act="diff"][data-v="${opts.diff}"]`);
  // weapon select (signature where available)
  for (const id of want) {
    const sel = page.locator(`select[data-id="${id}"]`);
    const values = await sel.locator('option').evaluateAll((os) => os.map((o) => o.value));
    await sel.selectOption(values[values.length - 1]); await check(page, 'weapon select');
  }
  await click(page, '[data-act="startRun"]');
  let guard = 0; let shotBattle = false; let shotReward = false; let shotMap = false;
  while (guard++ < 2500) {
    const scr = await page.evaluate(() => window.__mf.screen);
    if (scr !== 'battle' && await page.locator('.modal').count()) {
      if (await page.locator('.modal [data-act="deckPick"]').count()) { await click(page, '.modal [data-act="deckPick"]', 'deck pick'); continue; }
      await click(page, '.modal [data-act="closeModal"]', 'close stray modal'); continue;
    }
    if (scr === 'map') {
      if (!shotMap && SHOTS) { await shot(page, `map-${viewport.width}`); shotMap = true; }
      // prefer node types whose screen has not been exercised yet (shop, forge, rest, event …), else random
      const labels = await page.locator('g.node.avail').evaluateAll((gs) => gs.map((g) => g.getAttribute('aria-label')));
      const SCREEN = { '전투': 'battle', '엘리트': 'battle', '보스': 'battle', '휴식': 'rest', '공방': 'forge', '상점': 'shop', '사건': 'event' };
      let idx = labels.findIndex((l) => !stats.screens.has(SCREEN[l]));
      if (idx < 0) idx = Math.floor(rand() * labels.length);
      await page.locator('g.node.avail').nth(idx).click(); stats.clicks++; await check(page, 'map node');
      continue;
    }
    if (scr === 'battle') {
      const st = await page.evaluate(() => { const b = window.__mf.run.battle; return b ? { phase: b.s.phase, turn: b.s.turn, ap: b.s.ap } : null; });
      if (!st) continue;
      if (st.turn === 1 && !shotBattle && SHOTS) { await shot(page, `battle-${viewport.width}`); shotBattle = true; }
      // modal picker open? resolve it
      if (await page.locator('.modal [data-act="pick"]').count()) { await click(page, '.modal [data-act="pick"]', 'pick'); stats.picks++; continue; }
      if (await page.locator('.modal [data-act="pickAuto"]').count()) { await click(page, '.modal [data-act="pickAuto"]', 'pickAuto'); continue; }
      if (await page.locator('.modal').count()) { await click(page, '.modal [data-act="closeModal"]', 'close modal'); continue; }
      // ultimate ready?
      const ult = page.locator('[data-act="ult"]:not([disabled])');
      if (await ult.count()) {
        await ult.first().click(); stats.clicks++; await check(page, 'ult');
        if (await page.evaluate(() => !!(window.__mf.sel && window.__mf.sel.ult))) await click(page, '.enemy:not(.dead)', 'ult target');
        continue;
      }
      // try playable cards in hand order, random mode among enabled ones
      const hand = page.locator('.hand .card:not(.cant)');
      const n = await hand.count();
      let played = false;
      for (let i = 0; i < n && !played; i++) {
        await hand.nth(i).click(); stats.clicks++; await check(page, 'select card');
        if (await page.locator('.action .pill.time').filter({ hasText: '대상을 고르세요' }).count()) {
          const tg = page.locator('.enemy.targetable');
          if (await tg.count()) { await tg.nth(Math.floor(rand() * (await tg.count()))).click(); stats.clicks++; await check(page, 'target'); }
        }
        const modes = page.locator('.action [data-act="mode"]:not([disabled])');
        const m = await modes.count();
        if (m) {
          const before = await page.evaluate(() => window.__mf.run.battle ? window.__mf.run.battle.s.zones.hand.length : -1);
          await modes.nth(Math.floor(rand() * m)).click(); stats.clicks++; await check(page, 'mode');
          stats.plays++; played = true; void before;
        } else {
          await click(page, '.action [data-act="cancel"]', 'cancel');
        }
      }
      if (!played && (await page.evaluate(() => window.__mf.screen)) === 'battle' && !(await page.locator('.modal').count())) {
        await click(page, '[data-act="endTurn"]', 'end turn'); stats.turns++;
        if ((await page.evaluate(() => window.__mf.screen)) !== 'battle') stats.battles++;
      }
      continue;
    }
    if (scr === 'reward') {
      if (!shotReward && SHOTS) { await shot(page, `reward-${viewport.width}`); shotReward = true; }
      if (await page.locator('[data-act="evolve"]').count()) { await click(page, '[data-act="evolve"]', 'evolve'); continue; }
      if (await page.locator('[data-act="takeRelic"]').count()) { await click(page, '[data-act="takeRelic"]', 'relic'); continue; }
      if (await page.locator('[data-act="takePart"]').count()) { await click(page, '[data-act="takePart"]', 'part'); continue; }
      if (await page.locator('[data-act="takeCard"]').count()) { await click(page, rand() < 0.8 ? '[data-act="takeCard"]' : '[data-act="skipCard"]', 'card reward'); continue; }
      await click(page, '[data-act="proceed"]', 'reward proceed'); continue;
    }
    if (scr === 'rest') {
      const heal = page.locator('[data-act="restHeal"]:not([disabled])');
      const up = page.locator('[data-act="restUpgrade"]:not([disabled])');
      if (await heal.count() && rand() < 0.5) { await click(page, '[data-act="restHeal"]', 'rest heal'); continue; }
      if (await up.count()) { await click(page, '[data-act="restUpgrade"]', 'rest upgrade'); await click(page, '.modal [data-act="deckPick"]', 'upgrade pick'); continue; }
      await click(page, '[data-act="proceed"]', 'rest leave'); continue;
    }
    if (scr === 'forge') {
      if (await page.locator('[data-act="forgeApply"]').count()) { await click(page, '[data-act="forgeApply"]', 'forge apply'); continue; }
      const v = page.locator('[data-act="forgeVar"]:not([disabled])');
      if (await v.count()) { await click(page, '[data-act="forgeVar"]', 'forge var'); await click(page, '.modal [data-act="deckPick"]', 'forge pick'); continue; }
      await click(page, '[data-act="proceed"]', 'forge leave'); continue;
    }
    if (scr === 'shop') {
      const buy = page.locator('[data-act="buy"]:not([disabled])');
      if (await buy.count()) { await buy.first().click(); stats.clicks++; await check(page, 'buy'); continue; }
      if (await page.locator('[data-act="shopRemove"]:not([disabled])').count() && rand() < 0.5) { await click(page, '[data-act="shopRemove"]', 'shop remove'); await click(page, '.modal [data-act="deckPick"]', 'remove pick'); continue; }
      await click(page, '[data-act="proceed"]', 'shop leave'); continue;
    }
    if (scr === 'event') {
      const ch = page.locator('[data-act="evChoice"]:not([disabled])');
      if (await ch.count()) { await ch.nth(Math.floor(rand() * (await ch.count()))).click(); stats.clicks++; await check(page, 'event choice'); continue; }
      await click(page, '[data-act="proceed"]', 'event leave'); continue;
    }
    if (scr === 'archive') { await click(page, `[data-act="archive"][data-v="${opts.archive}"]`, 'archive'); continue; }
    if (scr === 'save') {
      if (SHOTS) await shot(page, `save-${runIdx}`);
      // exercise the toggles, then commit
      const boxes = page.locator('[data-act="saveToggle"]');
      if (await boxes.count()) { await boxes.first().click(); await check(page, 'toggle'); await boxes.first().click(); await check(page, 'toggle back'); }
      const assign = page.locator('select[data-act="assign"]');
      if (await assign.count()) { const vals = await assign.first().locator('option').evaluateAll((os) => os.map((o) => o.value)); await assign.first().selectOption(vals[vals.length - 1]); await check(page, 'assign'); }
      await click(page, '[data-act="commitSave"]', 'commit save');
      if ((await page.evaluate(() => window.__mf.screen)) === 'save') {
        // over capacity or defeat-keep-one: deselect everything except the first and retry
        await page.evaluate(() => { const s = window.__mf.saveSel; let kept = false; for (const k of Object.keys(s)) { s[k] = kept ? [] : s[k].slice(0, 1); if (s[k].length) kept = true; } });
        await click(page, '[data-act="commitSave"]', 'commit save retry');
        if ((await page.evaluate(() => window.__mf.screen)) === 'save') await click(page, '[data-act="skipSave"]', 'skip save');
      }
      continue;
    }
    if (scr === 'result') {
      const r = await page.evaluate(() => ({ outcome: window.__mf.run.s.result && window.__mf.run.s.result.outcome, hp: window.__mf.run.s.hp, battles: window.__mf.run.s.stats.battles, memory: Object.keys(window.__mf.memory) }));
      if (SHOTS) await shot(page, `result-${runIdx}`);
      stats.runs.push({ viewport: viewport.width, ...opts, ...r });
      await click(page, '.nav [data-to="memory"]', 'memory after run');
      if (SHOTS) await shot(page, `memory-${runIdx}`);
      break;
    }
    throw new Error(`unknown screen ${scr}`);
  }
  if (guard >= 2500) {
    const dbg = await page.evaluate(() => { const a = window.__mf; const b = a.run && a.run.battle; return { screen: a.screen, phase: a.run && a.run.s.phase, toast: a.toast, sel: a.sel, target: a.target, modal: a.modal && a.modal.type, battle: b && { phase: b.s.phase, turn: b.s.turn, ap: b.s.ap, hand: b.s.zones.hand.map((u) => b.s.cards[u].id), enemies: b.s.enemies.map((e) => [e.id, e.hp, e.alive]), actors: b.s.actors.map((x) => [x.id, x.gauge]) } }; });
    console.log(JSON.stringify(dbg, null, 1));
    console.log((await page.locator('.action').innerHTML().catch(() => '')).slice(0, 1500));
    throw new Error('run did not finish within guard');
  }
  await page.context().close();
}

const parties = [['sera', 'harin', 'rien'], ['ravien', 'yuri', 'lumi'], ['sion', 'kaya', 'nia'], ['aila', 'ravien', 'lumi']];
for (let i = 0; i < RUNS; i++) {
  const vp = i % 2 ? { width: 390, height: 844 } : { width: 1280, height: 860 };
  await playRun(vp, i, { party: parties[i % parties.length], length: i % 3 === 2 ? 'full' : 'short', diff: i % 4 === 3 ? 'hard' : 'normal', archive: i % 2 ? 'return' : 'continue' });
}
await browser.close();
console.log(JSON.stringify({ ...stats, screens: [...stats.screens] }, null, 1));
console.log(errors.length ? `FAIL ${errors.length}` : 'SMOKE OK');
process.exit(errors.length ? 1 : 0);
