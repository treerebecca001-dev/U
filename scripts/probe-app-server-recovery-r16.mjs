// Read-only official app-server administrative status probe. Never calls a browser/input tool.
import fs from 'node:fs/promises';
import path from 'node:path';
import { spawn } from 'node:child_process';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/tool-runtime-recovery-r16');
await fs.mkdir(out, { recursive: true });
const executable = 'C:/Users/tree/AppData/Local/OpenAI/Codex/bin/6257d9e8f63c0f2b/codex.exe';
const child = spawn(executable, ['app-server', 'proxy'], { cwd: root, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
const report = { time: new Date().toISOString(), command: 'codex app-server proxy',
  officialReference: 'https://learn.chatgpt.com/docs/app-server',
  requestedMethod: 'mcpServerStatus/list', requestedThread: '01a15dfd-76e2-7b73-9a02-ce1d6139e231',
  configurationChanged: false, serverRestarted: false, browserToolCalled: false, responses: [], stderr: '' };
let pending = '', finished = false;
const send = row => child.stdin.write(JSON.stringify(row) + '\n');
let finish;
const done = new Promise(resolve => { finish = resolve; });
child.stderr.on('data', bytes => { if (report.stderr.length < 6000) report.stderr += bytes.toString().slice(0, 6000 - report.stderr.length); });
child.stdout.on('data', bytes => {
  pending += bytes;
  while (pending.includes('\n')) {
    const index = pending.indexOf('\n'), line = pending.slice(0, index); pending = pending.slice(index + 1);
    if (!line.trim()) continue;
    let row; try { row = JSON.parse(line); } catch { continue; }
    report.responses.push(row);
    if (row.id === 1 && row.result) {
      send({ method: 'initialized', params: {} });
      send({ id: 2, method: 'mcpServerStatus/list', params: { threadId: report.requestedThread, serverName: 'cua_repl', detail: 'toolsAndAuthOnly', limit: 1 } });
    }
    if (row.id === 2 || row.id === 1 && row.error) finish();
  }
});
child.on('error', error => { report.error = String(error); finish(); });
child.on('exit', (code, signal) => { report.exitCode = code; report.signal = signal; finish(); });
send({ id: 1, method: 'initialize', params: { clientInfo: { name: 'scene-workbench-runtime-diagnostic', version: 'r16' } } });
const timer = setTimeout(() => { report.timedOut = true; finish(); }, 10000);
await done; clearTimeout(timer);
if (child.exitCode === null && child.signalCode === null) {
  // Close only the proxy process created by this script, never an existing server.
  child.stdin.end(); child.kill(); report.ownedProxyClosed = true;
}
await fs.writeFile(path.join(out, 'supported-admin-status.json'), JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ responses: report.responses.length, requestedMethodReached: report.responses.some(r => r.id === 2),
  stderr: report.stderr, error: report.error, timedOut: report.timedOut ?? false, serverRestarted: false }));
