// Assembles docs/design/*.md (+ generated tables) into docs/design_v0.7.md and renders the PDF with Chromium.
// Usage: node tools/build_docs.mjs [--no-pdf]
// Run node tools/gen_docs.mjs first. Writes reports/doc_build.txt (lint + page count).
import { readFileSync, writeFileSync, readdirSync, mkdirSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const require = createRequire(import.meta.url);
const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const SRC = join(root, 'docs', 'design');
const req = (name) => { try { return require(name); } catch { return require(`/opt/npm-tools/node_modules/${name}`); } };

// 1) assemble with includes
const parts = readdirSync(SRC).filter((f) => /^\d\d_.*\.md$/.test(f)).sort();
const expand = (text) => text.replace(/<!-- include: (gen\/[\w.]+) -->/g, (_, p) => readFileSync(join(SRC, p), 'utf8').trim());
let md = parts.map((f) => expand(readFileSync(join(SRC, f), 'utf8')).trim()).join('\n\n');

// 2) lint: Hangul glued to a digit in prose (outside tables / code), the v0.6 spacing defect
const lint = [];
let inCode = false;
md.split('\n').forEach((line, i) => {
  if (line.startsWith('```')) { inCode = !inCode; return; }
  if (inCode || line.startsWith('|') || line.startsWith('#')) return;
  const stripped = line.replace(/`[^`]*`/g, '');
  for (const m of stripped.matchAll(/[가-힣][0-9]/g)) lint.push(`L${i + 1}: …${stripped.slice(Math.max(0, m.index - 12), m.index + 14)}…`);
});

// 3) table of contents (chapters = H1) with explicit anchors (work in GitHub markdown, marked and the PDF)
const toc = [];
let n = 0;
md = md.split('\n').map((line) => {
  const m = /^# (.+)$/.exec(line);
  if (!m) return line;
  const id = `ch${n++}`;
  toc.push({ text: m[1], id });
  return `# <a id="${id}"></a>${m[1]}`;
}).join('\n');
const tocMd = '## 목차\n\n' + toc.slice(1).map((t) => `- [${t.text}](#${t.id})`).join('\n') + '\n\n';
const firstH2 = md.indexOf('\n## ');
md = md.slice(0, firstH2 + 1) + tocMd + md.slice(firstH2 + 1);
writeFileSync(join(root, 'docs', 'design_v0.7.md'), md + '\n');

// 4) HTML + PDF
const lines = [`parts ${parts.length}`, `markdown chars ${md.length}`, `lint (Hangul+digit in prose) ${lint.length}`, ...lint.slice(0, 50)];
if (!process.argv.includes('--no-pdf')) {
  const { marked } = req('marked');
  const body = marked.parse(md, { gfm: true });
  const css = `
  @page { size: A4; margin: 16mm 14mm 18mm 14mm; }
  :root { --ink:#16213a; --muted:#5b667e; --line:#c9d1e3; --brass:#9a6b16; --memory:#0f8c7e; }
  html { font-family: "Noto Sans CJK KR", "Noto Sans KR", sans-serif; font-size: 9.6pt; color: var(--ink); line-height: 1.55; word-break: keep-all; overflow-wrap: anywhere; }
  body { margin: 0; }
  h1 { font-size: 19pt; border-bottom: 2.5px solid var(--brass); padding-bottom: 4px; margin: 0 0 10px; page-break-before: always; color: #0d1322; }
  h1:first-of-type { page-break-before: avoid; font-size: 24pt; }
  h2 { font-size: 13.5pt; color: var(--memory); margin: 18px 0 6px; page-break-after: avoid; }
  h3 { font-size: 11.5pt; margin: 14px 0 4px; page-break-after: avoid; }
  h4 { font-size: 10.5pt; margin: 10px 0 4px; }
  p, li { orphans: 2; widows: 2; }
  table { border-collapse: collapse; width: 100%; margin: 6px 0 10px; font-size: 8.2pt; page-break-inside: auto; }
  tr { page-break-inside: avoid; }
  th, td { border: 1px solid var(--line); padding: 3px 5px; vertical-align: top; text-align: left; }
  th { background: #eef2fa; }
  code { font-family: "Noto Sans Mono CJK KR", "DejaVu Sans Mono", monospace; font-size: 8.4pt; background: #f2f4f9; padding: 0 2px; border-radius: 2px; }
  pre { background: #f5f7fb; border: 1px solid var(--line); padding: 7px 9px; white-space: pre-wrap; font-size: 7.8pt; line-height: 1.4; page-break-inside: avoid; }
  pre code { background: none; padding: 0; }
  pre:has(code.language-wire) { white-space: pre; font-size: 6.9pt; line-height: 1.35; overflow: hidden; }
  blockquote { margin: 8px 0; padding: 6px 10px; border-left: 3px solid var(--brass); background: #fbf7ee; }
  a { color: inherit; text-decoration: none; }
  ul { padding-left: 18px; }`;
  const html = `<!doctype html><html lang="ko"><head><meta charset="utf-8"><title>Memory Forge v0.7 설계서</title><style>${css}</style></head><body>${body}</body></html>`;
  const htmlPath = join(root, 'docs', 'design_v0.7.html');
  writeFileSync(htmlPath, html);
  const { chromium } = req('playwright');
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.goto(pathToFileURL(htmlPath).href, { waitUntil: 'load' });
  const pdfPath = join(root, 'docs', 'Memory_Forge_v0.7_Design.pdf');
  await page.pdf({ path: pdfPath, format: 'A4', printBackground: true, displayHeaderFooter: true,
    headerTemplate: '<div style="font-size:7pt;color:#7a849a;width:100%;padding:0 14mm;font-family:Noto Sans CJK KR,sans-serif">기억 공방 / Memory Forge v0.7 통합 설계서</div>',
    footerTemplate: '<div style="font-size:7pt;color:#7a849a;width:100%;text-align:center;font-family:Noto Sans CJK KR,sans-serif"><span class="pageNumber"></span> / <span class="totalPages"></span></div>',
    margin: { top: '16mm', bottom: '18mm', left: '14mm', right: '14mm' } });
  await browser.close();
  const pdf = readFileSync(pdfPath);
  const pages = (pdf.toString('latin1').match(/\/Type\s*\/Page[^s]/g) || []).length;
  lines.push(`pdf ${pdfPath.replace(root + '/', '')} ${(pdf.length / 1024).toFixed(0)} KB, ${pages} pages`);
}
mkdirSync(join(root, 'reports'), { recursive: true });
writeFileSync(join(root, 'reports', 'doc_build.txt'), lines.join('\n') + '\n');
console.log(lines.slice(0, 3).join('\n') + (lint.length ? `\n${lint.slice(0, 12).join('\n')}` : '') + `\n${lines[lines.length - 1]}`);
