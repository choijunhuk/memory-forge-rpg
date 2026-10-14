// Build the single-file browser prototype.
// Usage: node tools/build_prototype.mjs            → rebuilds data/game_data.json, writes prototype/index.html
//        node tools/build_prototype.mjs --no-data  → skips the data rebuild
// The page is fully self-contained except the Google Fonts stylesheet (falls back to system fonts offline).
import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
if (!process.argv.includes('--no-data')) {
  execFileSync(process.execPath, [join(root, 'content', 'build.mjs'), join(root, 'data')], { stdio: 'inherit' });
}
const read = (p) => readFileSync(join(root, p), 'utf8');
const data = JSON.parse(read('data/game_data.json'));
// JSON inside <script type="application/json">: escape "</" and "<!--" so the HTML parser never ends the block early.
const json = JSON.stringify(data).replace(/<\//g, '<\\/').replace(/<!--/g, '<\\!--');
const guardJs = (src, name) => {
  if (/<\/script/i.test(src)) throw new Error(`${name} contains "</script" — split the string literal`);
  return src;
};
const css = read('prototype/src/style.css');
const engine = guardJs(read('engine/rules-core.js'), 'rules-core.js');
const ui = guardJs(read('prototype/src/ui.js'), 'ui.js');

const html = `<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="claude:viewport-fit-sides" content="cover">
<title>기억 공방 v0.7.1 검토용 테스트</title>
<meta name="description" content="Memory Forge v${data.meta.version} 플레이어블 프로토타입 — 기억 덱 로그라이트 전투, 반천장 모집, 공명·재련.">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Hahmlet:wght@400;600;700&family=IBM+Plex+Sans+KR:wght@400;500;600;700&family=Orbit&display=swap">
<style>
${css}
</style>
</head>
<body>
<div id="app" aria-live="polite"><div class="boot">기억 공방을 여는 중…</div></div>
<details style="max-width:1150px;margin:24px auto;padding:16px;color:#b8c5d6;font:14px sans-serif"><summary>v0.7.1 검토판 주의</summary><p>첫 턴 방어·성장 계열·공생 이끼를 수정했습니다. 기본 패배 저장은 v0.7 정책이며 이전 덱의 미선택 변경을 잃을 수 있습니다. 새 저장 v2는 아직 연결하지 않았습니다. 확률·수급·나이는 원본 유지입니다.</p></details>
<noscript><p style="padding:24px">이 프로토타입은 자바스크립트가 필요해요.</p></noscript>
<script id="mf-data" type="application/json">${json}</script>
<script>
${engine}
</script>
<script>
${ui}
</script>
</body>
</html>
`;
writeFileSync(join(root, 'prototype', 'index.html'), html);
console.log(`prototype/index.html ${(html.length / 1024).toFixed(0)} KB (data v${data.meta.version})`);
