// Ordinary target=_blank anchor is an explicit QA-only native focus stimulus.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/tree-input-r13');
const manifestPath = path.join(out, 'native-event-fixture-manifest.json');
const m = JSON.parse(await fs.readFile(manifestPath)), relative = 'src/SceneWorkbench.Web/wwwroot/index.html';
const target = path.join(m.fixtureRoot, relative), before = await fs.readFile(target, 'utf8');
const hash = b => crypto.createHash('sha256').update(b).digest('hex');
const entry = m.copied.find(f => f.path === relative); assert.equal(hash(before), entry.fixtureSha256);
const after = before.replace('<span id="qa-mode-note"', '<a id="qa-native-tab" href="/qa-focus-target.html" target="_blank" rel="noopener">原生新分頁（僅 QA）</a><span id="qa-mode-note"');
assert.notEqual(before, after);
const focusPath = 'src/SceneWorkbench.Web/wwwroot/qa-focus-target.html';
const focusHtml = '<!doctype html><html lang="zh-Hant"><meta charset="utf-8"><title>R13 QA 原生焦點目標</title><h1>R13 QA 原生焦點目標</h1><p>僅供隔離驗證。無編輯器 API 或程式呼叫。</p></html>';
await fs.writeFile(path.join(m.fixtureRoot, focusPath), focusHtml, { flag: 'wx' });
await fs.writeFile(target, after); entry.fixtureSha256 = hash(after);
m.additionalFiles = [{ path: focusPath, sha256: hash(focusHtml) }];
await fs.writeFile(manifestPath, JSON.stringify(m, null, 2) + '\n');
await fs.writeFile(path.join(out, 'fixture-native-tab-addition.json'), JSON.stringify({ time: new Date().toISOString(),
  beforeIndexSha256: hash(before), afterIndexSha256: hash(after), focusPageSha256: hash(focusHtml),
  purpose: 'One bounded native target=_blank attempt; no script, synthetic events or product changes.' }, null, 2) + '\n', { flag: 'wx' });
console.log('QA native link added; no product modules changed.');
