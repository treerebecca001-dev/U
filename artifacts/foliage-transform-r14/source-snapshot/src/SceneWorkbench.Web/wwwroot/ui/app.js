import { $, node, status, clearError, fieldError, openDialog, closeDialog, chooseUnsaved, wireSearch, wireTabs, setBusyControls } from './controls.js';
import { InspectorPanel } from './inspector.js';
import { SceneView } from './scene-view.js';
import { AssetPreview } from './asset-preview.js';
import { MaterialInspector } from './material-inspector.js';

const token = document.querySelector('meta[name="workbench-token"]').content;
let state = { revision: 0, items: [], layers: [], scenePaths: [], diagnostics: [], dirty: false }, bootstrap = { samplePath: '', demoPath: '', suggestedCopyPath: '' };
let selectedId = null, busy = false, objectLimit = 300, hierarchyQuery = '', sceneKey = '';
let directory = '', assetRows = [], assetTotal = 0, assetRequest = 0, assetController, imageController;
const logs = [];
const inspector = new InspectorPanel(() => execute(applyDraft), renderControls, () => materialLinks());
const materialInspector = new MaterialInspector(() => execute(applyDraft), renderControls);
let assets, previewGraph, previewGeneration = 0, lastSceneSelection = null;
let sceneLoadPhase = 'idle', sceneLoadPath = '', sceneLoadProject = '';
const hasDraft = () => inspector.hasDraft() || materialInspector.hasDraft();
const view = new SceneView((id) => execute(() => selectObject(id)), (id, field, value) => execute(async () => {
  await applyDraft(); state = await api('/api/scene/edit', { id, field, value }); renderState();
  status(`已${{ position: '移動', rotation: '旋轉', scale: '縮放' }[field]}物件，尚未儲存。`);
}), message => { status(message, 'error'); log(message, 'error'); });
const activateTab = wireTabs(['project-heading', 'console-tab']);

async function api(path, body, signal) {
  const response = await fetch(path, { method: body ? 'POST' : 'GET', headers: { 'X-Workbench-Token': token, ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify({ revision: state.revision, ...body }) : undefined, signal });
  const result = await response.json();
  if (!response.ok) { const error = new Error(result.error ?? `請求失敗（${response.status}）`); error.code = result.code; throw error; }
  return result;
}
function log(message, tone = 'info') { logs.unshift({ message, tone, time: new Date().toLocaleTimeString('zh-TW') }); logs.splice(100); renderConsole(); }
function sceneLoadStatus(phase, path, message) {
  sceneLoadPhase = phase; sceneLoadPath = path ?? ''; sceneLoadProject = state.projectPath ?? '';
  $('scene-load-status').textContent = message; $('scene-load-status').title = path ? `${path}\n${message}` : message;
  $('scene-load-status').dataset.state = phase;
  $('viewport').setAttribute('aria-busy', String(phase === 'loading'));
  $('scene-loading').hidden = phase !== 'loading';
  const progress = $('scene-load-progress'), counts = message.match(/GUID 索引：([\d,]+)\/([\d,]+)/u);
  const current = counts ? Number(counts[1].replaceAll(',', '')) : NaN, total = counts ? Number(counts[2].replaceAll(',', '')) : NaN;
  if (Number.isSafeInteger(current) && Number.isSafeInteger(total) && total > 0 && current >= 0 && current <= total) {
    progress.max = total; progress.value = current;
    $('scene-load-count').textContent = `目前階段：${current.toLocaleString('zh-TW')} / ${total.toLocaleString('zh-TW')} 個資源（${Math.floor(current / total * 100)}%）`;
  } else {
    progress.removeAttribute('value');
    $('scene-load-count').textContent = '正在讀取，請稍候…';
  }
}
async function execute(action) {
  if (busy) return false;
  busy = true; clearError(); renderControls(); $('progress').classList.add('busy'); status('處理中…');
  const timer = setInterval(async () => {
    try { const activity = await api('/api/activity'); if (activity.message) { status(activity.message); if (sceneLoadPhase === 'loading') sceneLoadStatus('loading', sceneLoadPath, `載入 ${sceneLoadPath.split('/').at(-1)} · ${activity.message}`); } } catch { /* The main request reports the network error. */ }
  }, 500);
  try { await action(); return true; }
  catch (error) {
    if (error.name === 'AbortError') return false;
    // Refresh only the shared revision/model; preserve local Inspector inputs after any failed operation.
    try { state = await api('/api/state'); renderState({ preserveInspector: true }); } catch { /* Keep the draft if the local service is unavailable. */ }
    status(error.message, 'error'); log(error.message, 'error'); return false;
  }
  finally { clearInterval(timer); busy = false; $('progress').classList.remove('busy'); renderControls(); }
}
function renderControls() {
  const draft = hasDraft(), hasScene = Boolean(state.scenePath), hasDocument = hasScene || Boolean(state.material);
  $('save').disabled = busy || !state.writable || !hasDocument || Boolean(state.material && !state.material.editable);
  $('undo').disabled = busy || !state.undoCount || draft;
  $('redo').disabled = busy || !state.redoCount || draft;
  $('reload').disabled = busy || !hasDocument;
  $('open-project').disabled = busy; $('create-copy').disabled = busy || !state.projectPath;
  $('sync').disabled = busy; $('scene-select').disabled = busy || !state.projectPath;
  $('load-scene').disabled = busy || !state.projectPath || !$('scene-select').value;
  $('load-scene').setAttribute('aria-busy', String(busy && sceneLoadPhase === 'loading'));
  $('open-demo').disabled = busy; $('open-sample').disabled = busy;
  $('asset-up').disabled = busy || !directory; $('more-assets').disabled = busy; $('more-objects').disabled = busy;
  $('recover').disabled = busy || !state.writable || !state.pendingTransactions?.length || state.dirty || draft;
  $('cancel-operation').hidden = !busy; setBusyControls(busy);
  $('dirty-label').textContent = !hasDocument ? '尚無場景' : draft ? '● 欄位尚未套用' : state.dirty ? `● ${state.material ? '材質' : '場景'}尚未儲存` : '✓ 已儲存';
  $('dirty-label').classList.toggle('dirty', state.dirty || draft);
  if (view.renderer) view.setEditingAllowed(!draft && !busy && !state.material);
  document.title = `${state.dirty || draft ? '● ' : ''}${(state.material?.path ?? state.scenePath)?.split('/').at(-1) ?? 'SceneWorkbench'} · 本機場景編輯器`;
}
function renderState({ preserveInspector = false } = {}) {
  if (sceneLoadProject !== (state.projectPath ?? '')) sceneLoadStatus('idle', '', '選擇 Scene 後會自動載入，也可按「載入 Scene」重新讀取。');
  $('project-label').textContent = state.projectName ?? '尚未開啟專案'; $('project-label').title = state.projectPath ?? '';
  $('scene-path').textContent = state.scenePath ?? state.projectPath ?? '本機開發服務'; $('scene-path').title = state.projectPath ?? '';
  $('welcome').hidden = Boolean(state.scenePath);
  const conflicts = state.diagnostics.filter((d) => d.severity === 'Blocking').length;
  const storageMode = state.writeMode === 'direct' ? '直接編輯 · 直接儲存至所選專案' : state.writeMode === 'copy' ? '工作複本 · 儲存至副本' : '唯讀檢視 · 讀取所選專案';
  if (!state.projectPath) $('compatibility').textContent = '選擇本機 Unity 專案，或開啟示範場景開始編輯。';
  else $('compatibility').replaceChildren(node('strong', { text: storageMode }), node('span', { class: 'identifier project-write-path', text: state.writePath ?? state.projectPath, title: state.writePath ?? state.projectPath }),
    node('span', { text: `Unity ${state.editorVersion} · ${state.writable ? '可編輯已驗證欄位' : '唯讀'}` +
      (conflicts ? ` · ${conflicts} 項版本／資源阻擋診斷，詳見 Console；原生 Unity 相容性未驗收。` : ' · 原生 Unity 相容性尚未驗收。') }));
  $('compatibility').classList.toggle('warning', conflicts > 0 || !state.writable);
  const select = $('scene-select'); select.replaceChildren(node('option', { value: '', text: '選擇 .unity 場景' }));
  state.scenePaths.forEach((path) => select.append(node('option', { value: path, text: path.slice(7) })));
  select.value = sceneLoadPhase === 'error' && state.scenePaths.includes(sceneLoadPath) ? sceneLoadPath : state.scenePath ?? '';
  if (!state.scenePath && !['loading', 'error'].includes(sceneLoadPhase)) sceneLoadStatus('idle', '', '選擇 Scene 後會自動載入，也可按「載入 Scene」重新讀取。');
  if (!state.items.some((item) => item.id === selectedId)) selectedId = null;
  renderHierarchy();
  const nextSceneKey = `${state.projectPath}/${state.scenePath}`;
  if (nextSceneKey !== sceneKey) { view.clearGraph(); previewGraph = null; }
  renderConsole();
  view.setItems(state.items, nextSceneKey !== sceneKey); sceneKey = nextSceneKey; view.select(selectedId);
  if (!preserveInspector) {
    if (state.material) { inspector.disposeImage(); inspector.item = null; inspector.readers.clear(); materialInspector.show(state.material, ensureAssets(), () => execute(backToScene)); }
    else { materialInspector.close(); inspector.show(state.items.find((item) => item.id === selectedId), state.layers); materialLinks(); }
  }
  $('selection-id').textContent = selectedId ? `fileID ${selectedId}` : '未選取物件'; renderControls();
}
function renderHierarchy() {
  const displayNameOf = (item) => item.isPrefab ? previewGraph?.nodes.find((n) => n.parentId === item.id)?.name ?? item.name : item.name;
  const filtered = state.items.filter((item) => `${displayNameOf(item)} ${item.name}`.toLocaleLowerCase('zh-TW').includes(hierarchyQuery.toLocaleLowerCase('zh-TW')));
  $('hierarchy-count').textContent = `${filtered.length.toLocaleString('zh-TW')} 物件`;
  const list = $('hierarchy'); const fragment = document.createDocumentFragment();
  const byId = new Map(state.items.map((item) => [item.id, item]));
  for (const item of filtered.slice(0, objectLimit)) {
    let depth = 0, parent = item.parentId; const visited = new Set([item.id]);
    while (byId.has(parent) && depth < 8 && !visited.has(parent)) { visited.add(parent); depth++; parent = byId.get(parent).parentId; }
    const displayName = displayNameOf(item);
    const button = node('button', { type: 'button', 'aria-pressed': String(item.id === selectedId), 'aria-label': displayName, title: `${displayName}\nfileID ${item.id}`, onClick: () => execute(() => selectObject(item.id)) }, node('span', { class: 'object-icon', 'aria-hidden': 'true', text: item.isPrefab ? '◇' : '▧' }), node('span', { class: 'object-name', text: displayName }), !item.editable ? node('span', { class: 'readonly-icon', text: '唯讀' }) : null);
    button.style.paddingLeft = `${10 + depth * 12}px`; button.dataset.fileId = item.id; fragment.append(button);
  }
  if (!filtered.length) fragment.append(node('div', { class: 'empty-state', text: state.scenePath ? '找不到符合的物件，請清除搜尋。' : '開啟場景後，這裡會顯示物件階層。' }));
  list.replaceChildren(fragment); $('more-objects').hidden = filtered.length <= objectLimit; setBusyControls(busy);
}
async function selectObject(id) { if (state.material && !(await leaveMaterial())) return; await applyDraft(); selectedId = id; lastSceneSelection = id; const item = state.items.find((entry) => entry.id === id); inspector.show(item, state.layers); materialLinks(); view.select(id); renderHierarchy(); $('selection-id').textContent = `fileID ${id}`; renderControls(); status(`已選取 ${item?.name ?? id}`); }
async function applyDraft() {
  if (state.material) {
    const edits = materialInspector.edits(); if (!edits.length) return;
    state = await api('/api/material/edits', { edits });
    await view.updateMaterial(state.material, ensureAssets()); renderState(); status('材質參數已套用；按「儲存」回存原生 .mat。'); return;
  }
  const edits = inspector.edits();
  if (!edits.length) { status('沒有需要套用的變更。'); return; }
  // Validate all local inputs before the first property is sent; each accepted property has its own Undo step.
  for (const edit of edits) state = await api('/api/scene/edit', edit);
  renderState(); status('屬性已套用，按「儲存」寫入場景。');
}
async function save() { await applyDraft(); state = await api('/api/scene/save', {}); renderState(); status(`已儲存原生 ${state.material ? '.mat 材質' : '.unity 場景'}。`, 'success'); log(`已儲存 ${state.material?.path ?? state.scenePath}`, 'success'); }
function ensureAssets() { return assets ??= new AssetPreview(token, (message) => log(message, 'warning')); }
async function loadPreview() {
  const generation = ++previewGeneration; const path = state.scenePath;
  assets?.dispose(); assets = null; previewGraph = null; renderConsole();
  if (!path) return;
  $('scene-mode').textContent = '載入實際資源…';
  sceneLoadStatus('loading', path, `載入 ${path.split('/').at(-1)} · 解析模型、貼圖與材質…`);
  try {
    const graph = await api(`/api/preview?path=${encodeURIComponent(path)}`); if (generation !== previewGeneration) return;
    previewGraph = graph; renderConsole();
    await view.setGraph(graph, ensureAssets(), state.items); if (generation !== previewGeneration) return;
    $('scene-mode').textContent = '模型／材質預覽'; renderState();
    sceneLoadStatus('ready', path, `已載入 ${path.split('/').at(-1)} · ${state.items.length.toLocaleString('zh-TW')} 個物件 · ${graph.nodes.length.toLocaleString('zh-TW')} 個預覽節點`);
    return true;
  } catch (error) {
    if (generation !== previewGeneration) return false;
    $('scene-mode').textContent = '結構預覽 · 資源待重試';
    sceneLoadStatus('partial', path, `已讀取場景結構；預覽未完成：${error.message}。可按「載入 Scene」重試。`);
    status(`場景結構已讀取，預覽未完成：${error.message}`, 'error'); log(error.message, 'warning');
    return false;
  }
}
function materialLinks() {
  if (!selectedId || !previewGraph || state.material) return;
  document.getElementById('renderer-materials')?.remove();
  document.getElementById('terrain-details')?.remove();
  const paths = [...new Set(previewGraph.nodes.filter((node) => node.ownerId === selectedId).flatMap((node) => node.materials).filter(Boolean))];
  if (paths.length) {
    const section = node('section', { id: 'renderer-materials', class: 'component' }, node('h3', { text: 'Renderer 材質' }));
    paths.forEach((path) => section.append(node('button', { type: 'button', class: 'material-link', text: path.split('/').at(-1), title: path, onClick: () => execute(() => openMaterial(path)) })));
    $('inspector').append(section);
  }
  const terrain = view.terrainDetails(selectedId);
  if (terrain) {
    const { data, reference, worldOrigin } = terrain, h = data.heightmap;
    const section = node('section', { id: 'terrain-details', class: 'component' }, node('h3', { text: 'Terrain · 原生高度唯讀' }));
    const line = text => section.append(node('p', { class: 'inspector-help', text }));
    line(data.path); line(`TerrainData fileID ${data.fileId} · ${data.unityVersion}`);
    line(`高度圖：${h.resolution} × ${h.resolution} · ${h.samples.length.toLocaleString('zh-TW')} 樣本`);
    line(`尺寸 XYZ：${h.size.join(' / ')} m`);
    line(`地形本地高度：${h.minimum.toFixed(4)} ～ ${h.maximum.toFixed(4)} m`);
    line(`世界原點 XYZ：${worldOrigin.map(v => v.toFixed(4)).join(' / ')} m`);
    line(`Terrain ${reference.enabled ? '啟用' : '停用'} · 高度繪製 ${reference.drawHeightmap ? '開啟' : '關閉'}`);
    line(`holes／LOD 儲存樣本：${data.holesSamples} / ${data.holesLodSamples}；非空遮罩尚不繪製。`);
    line(`TerrainLayer：${data.layers.length} · alphamap：${data.controls.length}`);
    data.layers.forEach((layer, i) => {
      line(`圖層 ${i}：${layer.path ?? '原專案缺少這一層的地表貼圖設定，所以顯示棋盤格。'}`);
      if (!layer.path) line(`來源識別：${layer.guid} / ${layer.fileId}`);
      if (layer.diffusePath) line(`Diffuse：${layer.diffusePath} · Tiling ${layer.tileSize.join(' / ')}`);
    });
    data.diagnostics.forEach(line);
    line('保留原生高度與權重預覽；雕刻、TerrainLayer 繪製、碰撞模擬與 TerrainData 回寫尚未實作。');
    $('inspector').append(section);
  }
  setBusyControls(busy);
}
async function openMaterial(path) {
  if (!(await mayNavigate())) return;
  state = await api('/api/material/open', { path, discard: true }); lastSceneSelection = selectedId ?? lastSceneSelection; selectedId = null; renderState(); status(state.material.editable ? '已開啟材質，可調整參數與貼圖。' : '已開啟唯讀材質，可檢視原生參數與預覽限制。');
}
async function leaveMaterial() {
  if (!(await mayNavigate())) return false;
  const path = state.material.path;
  state = await api('/api/material/close', { discard: true }); materialInspector.close();
  const original = previewGraph?.materials.find((m) => m.path === path);
  // Re-read after Save/Discard so the scene never retains an unsaved material preview.
  if (state.scenePath) { const graph = await api(`/api/preview?path=${encodeURIComponent(state.scenePath)}`); previewGraph = graph; const current = graph.materials.find((m) => m.path === path) ?? original; if (current) await view.updateMaterial(current, ensureAssets()); }
  return true;
}
async function backToScene() { if (await leaveMaterial()) { selectedId = lastSceneSelection; renderState(); status('已返回場景物件。'); } }
async function mayNavigate() {
  await applyDraft();
  if (!state.dirty) return true;
  const choice = await chooseUnsaved();
  if (choice === 'save') await save();
  return choice !== 'cancel';
}
async function openProject(path, preferredScene, { mode = 'inspect', destination } = {}) {
  if (!(await mayNavigate())) return false;
  state = await api('/api/project/open', { path, discard: true, mode, ...(mode === 'copy' ? { destination } : {}) }); selectedId = null; directory = ''; assetRows = []; $('asset-search').value = ''; document.querySelector('[data-clear="asset-search"]').hidden = true;
  assets?.dispose(); assets = null; materialInspector.close();
  inspector.show(null, state.layers); renderState(); await loadAssets();
  const scene = preferredScene && state.scenePaths.includes(preferredScene) ? preferredScene : state.scenePaths.length === 1 ? state.scenePaths[0] : null;
  if (scene) await openScene(scene);
  status(state.writeMode === 'direct' ? '已直接開啟所選專案，儲存時只備份變更檔案。' : state.writable ? '已開啟工作複本。' : '已開啟來源專案，維持唯讀檢視。');
  return true;
}
async function openScene(path) {
  if (!(await mayNavigate())) { $('scene-select').value = state.scenePath ?? ''; return; }
  sceneLoadStatus('loading', path, `載入 ${path.split('/').at(-1)} · 讀取場景物件…`);
  $('scene-mode').textContent = '載入場景…';
  try {
    state = await api('/api/scene/open', { path, discard: true }); selectedId = null; hierarchyQuery = ''; $('hierarchy-search').value = ''; document.querySelector('[data-clear="hierarchy-search"]').hidden = true; objectLimit = 300; inspector.show(null, state.layers); view.clearGraph(); renderState();
    if (await loadPreview()) status('已讀取場景、模型與材質；可選取物件編輯。');
  } catch (error) {
    sceneLoadStatus('error', path, `載入 ${path.split('/').at(-1)} 失敗：${error.message}`);
    $('scene-mode').textContent = '載入未完成'; throw error;
  }
}
async function loadAssets(append = false) {
  if (!state.projectPath) return;
  assetController?.abort(); assetController = new AbortController(); const generation = ++assetRequest;
  const query = $('asset-search').value, offset = append ? assetRows.length : 0;
  if (!append) { $('assets').replaceChildren(node('div', { class: 'empty-state', text: '讀取資源…' })); $('more-assets').hidden = true; }
  try {
    const result = await api(`/api/assets?directory=${encodeURIComponent(directory)}&query=${encodeURIComponent(query)}&offset=${offset}`, undefined, assetController.signal);
    if (generation !== assetRequest) return;
    assetRows = append ? [...assetRows, ...result.rows] : result.rows; assetTotal = result.total; renderAssets();
  } catch (error) { if (error.name !== 'AbortError' && generation === assetRequest) { $('assets').replaceChildren(node('div', { class: 'empty-state', text: `${error.message} 請重新搜尋或開啟專案。` })); status(error.message, 'error'); } }
}
function renderAssets() {
  const fragment = document.createDocumentFragment();
  for (const row of assetRows) {
    const icon = row.isDirectory ? '▸' : row.kind === 'UNITY' ? '◈' : ['PNG', 'JPG', 'TGA'].includes(row.kind) ? '▧' : '▤';
    fragment.append(node('button', { type: 'button', title: row.path, 'aria-label': row.name, onClick: () => execute(async () => {
      await applyDraft();
      if (row.isDirectory) { directory = row.path; await loadAssets(); return; }
      if (row.kind === 'UNITY') { await openScene(row.path); return; }
      if (row.kind === 'MAT') { await openMaterial(row.path); return; }
      if (state.material && !(await leaveMaterial())) return;
      imageController?.abort(); imageController = new AbortController();
      const info = await api(`/api/asset?path=${encodeURIComponent(row.path)}`, undefined, imageController.signal); let url;
      if (info.extension === '.fbx' || info.meshIds?.length) {
        selectedId = null; materialInspector.close(); await inspector.showModel(info, ensureAssets()); renderHierarchy(); view.select(null); renderControls(); status(`模型幾何：${row.name}`); return;
      }
      if (['.tga', '.bmp'].includes(info.extension)) {
        const { texture } = await ensureAssets().texture(info.path); const canvas = document.createElement('canvas'); canvas.width = texture.image.width; canvas.height = texture.image.height;
        if (texture.image.data) canvas.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(texture.image.data), canvas.width, canvas.height), 0, 0);
        else canvas.getContext('2d').drawImage(texture.image, 0, 0);
        url = URL.createObjectURL(await new Promise((resolve) => canvas.toBlob(resolve)));
      }
      if (info.image) {
        const response = await fetch(`/api/image?path=${encodeURIComponent(row.path)}`, { headers: { 'X-Workbench-Token': token }, signal: imageController.signal });
        if (!response.ok) throw new Error((await response.json()).error); url = URL.createObjectURL(await response.blob());
      }
      selectedId = null; inspector.showAsset(info, url); renderHierarchy(); view.select(null); renderControls(); status(`資源檢視：${row.name}`);
    }) }, node('span', { class: 'object-icon', text: icon, 'aria-hidden': 'true' }), node('span', { class: 'asset-name', text: row.name }), node('span', { class: 'asset-type', text: row.kind })));
  }
  for (const button of fragment.querySelectorAll('button')) {
    const row = assetRows.find((entry) => entry.path === button.title);
    if (row && !row.isDirectory) { button.draggable = true; button.addEventListener('dragstart', (event) => event.dataTransfer.setData('text/plain', row.path)); }
  }
  if (!assetRows.length) fragment.append(node('div', { class: 'empty-state', text: $('asset-search').value ? '找不到符合的資源，請清除搜尋。' : '這個資料夾目前沒有可顯示的資源。' }));
  $('assets').replaceChildren(fragment); $('asset-directory').textContent = directory || 'Assets / Packages'; $('asset-directory').title = directory;
  $('asset-count').textContent = `${assetRows.length.toLocaleString('zh-TW')} / ${assetTotal.toLocaleString('zh-TW')} 項`;
  $('more-assets').hidden = assetRows.length >= assetTotal; $('asset-up').disabled = busy || !directory; setBusyControls(busy);
}
function renderConsole() {
  const diagnostics = state.diagnostics ?? [], previewDiagnostics = previewGraph?.diagnostics ?? [];
  $('diagnostic-count').textContent = (diagnostics.length + previewDiagnostics.length).toLocaleString('zh-TW');
  const rows = diagnostics.map((d) => node('div', { class: `console-entry ${d.severity === 'Blocking' ? 'warning' : ''}` }, node('span', { class: 'console-code', text: `${d.severity} · ${d.code}` }), node('span', { text: d.message }), d.path ? node('p', { class: 'identifier', text: d.path }) : null));
  // Current graph diagnostics outlive the bounded transient asset log.
  rows.push(...previewDiagnostics.map(message => node('div', { class: 'console-entry warning' }, node('span', { class: 'console-code', text: '場景預覽' }), node('span', { text: message }))));
  rows.push(...logs.map((entry) => node('div', { class: `console-entry ${entry.tone}` }, node('span', { class: 'console-code', text: entry.time }), node('span', { text: entry.message }))));
  if (!rows.length) rows.push(node('div', { class: 'empty-state', text: '目前沒有診斷訊息。' })); $('console').replaceChildren(...rows);
}

function updateProjectMode() {
  const useCopy = $('project-use-copy').checked;
  $('project-copy-options').hidden = !useCopy; $('project-copy-path').required = useCopy;
  $('project-mode-help').textContent = useCopy ? '建立完整副本，變更儲存在副本。目的地須為工作區內的新資料夾。' : '直接儲存至所選專案；儲存時只備份變更檔案。';
}
$('project-use-copy').addEventListener('change', updateProjectMode);
$('open-project').addEventListener('click', () => {
  $('project-path').value = state.projectPath ?? bootstrap.samplePath;
  $('project-use-copy').checked = true;
  $('project-copy-path').value = (bootstrap.suggestedProjectCopyPath ?? '').replace(/Project-\d+$/, 'Project-' + Date.now());
  fieldError($('project-path'), '', $('project-path-error')); fieldError($('project-copy-path'), '', $('project-copy-path-error'));
  updateProjectMode(); openDialog($('project-dialog'), $('project-path'));
});
$('project-form').addEventListener('submit', (event) => { event.preventDefault(); if (event.isComposing) return; execute(async () => {
  if (!$('project-path').value.trim()) { fieldError($('project-path'), '請輸入本機專案資料夾路徑。', $('project-path-error')); return; }
  const useCopy = $('project-use-copy').checked, destination = $('project-copy-path').value.trim();
  if (useCopy && !destination) { fieldError($('project-copy-path'), '請輸入新的副本資料夾路徑。', $('project-copy-path-error')); return; }
  try { if (await openProject($('project-path').value.trim(), undefined, { mode: useCopy ? 'copy' : 'direct', destination })) closeDialog($('project-dialog')); }
  catch (error) { fieldError(useCopy ? $('project-copy-path') : $('project-path'), error.message, useCopy ? $('project-copy-path-error') : $('project-path-error')); throw error; }
}); });
$('create-copy').addEventListener('click', () => { $('copy-path').value = bootstrap.suggestedCopyPath; fieldError($('copy-path'), '', $('copy-path-error')); openDialog($('copy-dialog'), $('copy-path')); });
$('copy-form').addEventListener('submit', (event) => { event.preventDefault(); if (event.isComposing) return; execute(async () => {
  if (!$('copy-path').value.trim()) { fieldError($('copy-path'), '請輸入新的工作複本路徑。', $('copy-path-error')); return; }
  if (!(await mayNavigate())) return;
  try { state = await api('/api/project/copy', { destination: $('copy-path').value.trim(), discard: true }); directory = ''; selectedId = null; inspector.show(null, state.layers); closeDialog($('copy-dialog')); renderState(); await loadAssets(); if (state.scenePaths.length === 1) await openScene(state.scenePaths[0]); status('完整工作複本已建立。', 'success'); }
  catch (error) { fieldError($('copy-path'), error.message, $('copy-path-error')); throw error; }
}); });
document.querySelectorAll('[data-close]').forEach((button) => button.addEventListener('click', () => closeDialog($(button.dataset.close))));
$('open-demo').addEventListener('click', () => execute(async () => {
  bootstrap = await api('/api/bootstrap');
  if (bootstrap.demoWorkingExists) { await openProject(bootstrap.suggestedCopyPath, 'Assets/WorkbenchDemo.unity'); return; }
  await openProject(bootstrap.demoPath);
  if (state.projectPath === bootstrap.demoPath && !state.writable) {
    // Create through the same protected copy service, never mark a source folder editable.
    state = await api('/api/project/copy', { destination: bootstrap.suggestedCopyPath }); bootstrap.demoWorkingExists = true; directory = ''; renderState(); await loadAssets(); await openScene(state.scenePaths[0]);
  }
}));
$('open-sample').addEventListener('click', () => execute(() => openProject(bootstrap.samplePath, bootstrap.sampleScene)));
$('scene-select').addEventListener('change', (event) => { const path = event.target.value; renderControls(); if (path) execute(() => openScene(path)); });
$('load-scene').addEventListener('click', () => { const path = $('scene-select').value; if (path) execute(() => openScene(path)); });
$('save').addEventListener('click', () => execute(save));
$('undo').addEventListener('click', () => execute(async () => { state = await api('/api/scene/undo', {}); if (state.material) await view.updateMaterial(state.material, ensureAssets()); renderState(); status('已復原一項屬性。'); }));
$('redo').addEventListener('click', () => execute(async () => { state = await api('/api/scene/redo', {}); if (state.material) await view.updateMaterial(state.material, ensureAssets()); renderState(); status('已重做一項屬性。'); }));
$('reload').addEventListener('click', () => execute(async () => { if (state.material) await openMaterial(state.material.path); else await openScene(state.scenePath); }));
$('sync').addEventListener('click', () => execute(async () => { if (hasDraft()) throw new Error('欄位仍有輸入，請先套用或還原輸入再同步分頁。'); state = await api('/api/state'); renderState(); if (state.scenePath && !previewGraph) await loadPreview(); if (state.material) await view.updateMaterial(state.material, ensureAssets()); await loadAssets(); status('已同步目前文件。'); }));
$('recover').addEventListener('click', () => execute(async () => { state = await api('/api/scene/recover', {}); renderState(); status('已復原中斷存檔並重新讀取場景。'); }));
$('cancel-operation').addEventListener('click', () => api('/api/cancel', {}).catch((error) => status(error.message, 'error')));
$('more-objects').addEventListener('click', () => { objectLimit += 300; renderHierarchy(); });
$('more-assets').addEventListener('click', () => loadAssets(true));
$('asset-up').addEventListener('click', () => { directory = directory.startsWith('Library/PackageCache/') || directory.startsWith('Packages/') ? 'Packages' : directory.includes('/') ? directory.slice(0, directory.lastIndexOf('/')) : ''; loadAssets(); });
wireSearch($('hierarchy-search'), (query) => { hierarchyQuery = query; objectLimit = 300; renderHierarchy(); });
wireSearch($('asset-search'), () => loadAssets(), { remote: true });
document.addEventListener('keydown', (event) => {
  if (event.isComposing || document.querySelector('dialog[open]')) return;
  const editing = event.target.matches('input, textarea, select, [contenteditable]');
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') { event.preventDefault(); if (!$('save').disabled) execute(save); }
  if (!editing && (event.ctrlKey || event.metaKey) && ['z', 'y'].includes(event.key.toLowerCase())) { event.preventDefault(); const button = event.key.toLowerCase() === 'y' || event.shiftKey ? $('redo') : $('undo'); if (!button.disabled) button.click(); }
});
let composing = false;
document.addEventListener('compositionstart', () => { composing = true; });
document.addEventListener('compositionend', () => { composing = false; });
document.addEventListener('submit', (event) => { if (composing) { event.preventDefault(); event.stopImmediatePropagation(); } }, true);
window.addEventListener('beforeunload', (event) => { if (state.dirty || hasDraft()) { event.preventDefault(); event.returnValue = ''; } });

// Literal actions stay disabled until the module has connected their handlers.
document.querySelectorAll('button[data-init-disabled]').forEach((button) => { button.disabled = false; button.removeAttribute('data-init-disabled'); });
await execute(async () => { bootstrap = await api('/api/bootstrap'); state = await api('/api/state'); renderState(); if (state.projectPath) await loadAssets(); if (state.scenePath) await loadPreview(); status('就緒 · 請開啟專案或示範場景。'); });
