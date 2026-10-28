// Record bounded recovery evidence and current user-session metadata. No restart or editor edit.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/tool-runtime-recovery-r16');
await fs.mkdir(out, { recursive: true });
const hash = data => crypto.createHash('sha256').update(data).digest('hex');
const sessions = await Promise.allSettled([5286, 5406, 5408].map(async port => {
  const base = 'http://127.0.0.1:' + port;
  const html = await (await fetch(base, { signal: AbortSignal.timeout(5000) })).text();
  const token = html.match(/name="workbench-token" content="([a-f0-9]+)"/)?.[1];
  assert.ok(token, port + ' token');
  const response = await fetch(base + '/api/state', { headers: { 'X-Workbench-Token': token, Origin: base }, signal: AbortSignal.timeout(10000) });
  assert.equal(response.status, 200);
  const state = await response.json();
  await fs.writeFile(path.join(out, 'user-' + port + '-state.json'), JSON.stringify({ time: new Date().toISOString(), base, state }), { flag: 'wx' });
  return { port, revision: state.revision, dirty: state.dirty, undo: state.undoCount, redo: state.redoCount, readOnlyProbe: true };
}));
const schemaPath = 'artifacts/tool-runtime-recovery-r16/protocol/ClientRequest.json';
const schema = JSON.parse(await fs.readFile(path.join(root, schemaPath)));
let reload;
function walk(value) {
  if (!value || typeof value !== 'object') return;
  if (value.properties?.method?.enum?.includes('config/mcpServer/reload')) reload = value;
  for (const item of Object.values(value)) if (item && typeof item === 'object') walk(item);
}
walk(schema); assert.equal(reload.properties.params.type, 'null');
const references = ['https://learn.chatgpt.com/docs/extend/mcp', 'https://learn.chatgpt.com/docs/app-server'];
const report = { time: new Date().toISOString(), status: 'UNVERIFIED', issue: 'QA-R15-INFRA-001',
  scope: 'Public recovery/admin paths checked; no supported automated per-thread restart established.',
  findings: {
    executable: { path: 'C:/Users/tree/AppData/Local/OpenAI/Codex/runtimes/cua_node/2fbcd108736f9c20/bin/node_repl.exe',
      sha256: '920c375a068069fb15c22c4f2d647e046d6e3eea5a878a5260ada4dcf2f5f2ce',
      helpOnlyExecuted: true, offeredRecoveryFlag: 'Only --disable-sandbox shown; not used.' },
    currentCli: '0.163.0-alpha.11',
    reload: { schemaPath, schemaSha256: hash(await fs.readFile(path.join(root, schemaPath))), params: reload.properties.params,
      threadOrServerSelector: false, invoked: false, reason: 'Official method queues refresh for loaded threads; no narrow scope demonstrated.' },
    proxy: { command: 'codex app-server proxy', nodeChildAttempt: 'spawn EPERM before admin traffic', directCliAttempt: 'failed to connect; os error 10050',
      defaultSocket: 'C:/Users/tree/.codex/app-server-control/app-server-control.sock', socketExists: false, configurationChanged: false },
    preciseMissingKernelAssetPathEstablished: false,
    oldAndNewTempLayoutsComparedReadOnly: true
  },
  references, sessions: sessions.map((result, i) => ({ port: [5286, 5406, 5408][i], ...result.status === 'fulfilled' ? result.value : { error: String(result.reason) } })),
  handoff: {
    manualStep: 'In desktop Settings > MCP servers, if cua_repl/node_repl is exposed, use Restart for that individual entry only.',
    caveat: 'Public docs establish the Restart UI, not that built-in servers appear there or that this particular failure will be repaired.',
    acceptance: 'Reviewer must obtain a new kernel and browser inventory, then independently retest 60001. Existing root kernel is not recovery evidence.',
    ifEntryAbsent: 'No precise supported per-server operation is established. Do not replace it with a guessed RPC, shared host kill, sandbox/ACL changes, or a full app restart while user memory state must be retained.',
    userCommunicationOwner: 'Existing reviewer chat; no duplicate confirmation request from implementation.'
  },
  changes: { editorSources: false, runtimeAssets: false, securitySettings: false, appRestart: false, sharedProcessesStopped: false, unityStarted: false, packaged: false } };
await fs.writeFile(path.join(out, 'recovery-report.json'), JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ status: report.status, scopedRestartEstablished: false, sessions: report.sessions, changes: report.changes }));
