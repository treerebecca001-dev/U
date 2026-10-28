import { $, node, fieldError } from './controls.js';
import * as THREE from '../vendor/three.module.js';
import { PreviewSurface } from './preview-surface.js';

const staticNames = ['Contribute GI', 'Occluder Static', 'Batching Static', 'Navigation Static', 'Occludee Static', 'Off Mesh Link Generation', 'Reflection Probe Static'];
export class InspectorPanel {
  constructor(apply, changed, afterShow = () => {}) { this.apply = apply; this.changed = changed; this.afterShow = afterShow; this.item = null; this.readers = new Map(); this.errors = new Map(); this.imageUrl = null; }
  hasDraft() {
    if (!this.item) return false;
    for (const [key, read] of this.readers) if (JSON.stringify(read(false)) !== JSON.stringify(this.item[key])) return true;
    return false;
  }
  edits() {
    const edits = [];
    for (const error of this.errors.values()) error.textContent = '';
    $('inspector').querySelectorAll('[aria-invalid="true"]').forEach((control) => control.setAttribute('aria-invalid', 'false'));
    for (const [field, read] of this.readers) {
      if (JSON.stringify(read(false)) !== JSON.stringify(this.item[field])) {
        const value = read(true);
        edits.push({ id: this.item.id, field, value });
      }
    }
    return edits;
  }
  disposeImage() { if (this.imageUrl) URL.revokeObjectURL(this.imageUrl); this.imageUrl = null; this.modelGeneration = (this.modelGeneration ?? 0) + 1; this.modelSurface?.dispose(); this.modelSurface = null; }
  show(item, layers) {
    this.disposeImage(); this.item = item; this.readers.clear(); this.errors.clear();
    if (!item) { $('inspector').replaceChildren(node('div', { class: 'empty-state', text: '選取場景物件以編輯屬性。' })); $('selection-kind').textContent = '未選取'; return; }
    $('selection-kind').textContent = item.isPrefab ? 'Prefab · 唯讀' : item.editable ? 'GameObject' : '唯讀';
    const body = node('form', { class: 'inspector-content', noValidate: true });
    body.addEventListener('submit', (event) => { event.preventDefault(); if (!event.isComposing) this.apply(); });
    body.addEventListener('input', () => this.changed());
    body.addEventListener('change', () => this.changed());
    // A native text input strips CR/LF on assignment; textarea preserves Unity names without creating a false draft.
    const name = node('textarea', { id: 'object-name', rows: Math.min(4, Math.max(1, item.name.split(/\r?\n/).length)), value: item.name, maxLength: 1024, disabled: !item.editableFields.includes('name'), 'aria-label': '物件名稱' });
    name.style.resize = 'none';
    const active = node('input', { id: 'object-active', type: 'checkbox', checked: item.active, disabled: !item.editableFields.includes('active'), 'aria-label': '啟用物件' });
    body.append(node('div', { class: 'object-heading' }, active, name));
    const nameError = node('p', { id: 'name-error', class: 'field-error' }); body.append(nameError); this.errors.set('name', nameError);
    // Textareas normalize CRLF to LF; keep the exact original value until the user actually edits the field.
    const displayedName = name.value;
    let nameEdited = false;
    name.addEventListener('input', () => { nameEdited = true; name.rows = Math.min(6, Math.max(1, name.value.split('\n').length)); });
    if (item.editableFields.includes('name')) this.readers.set('name', (validate) => {
      const value = !nameEdited && name.value === displayedName ? item.name : name.value;
      if (validate && (!value.trim() || value.length > 1024)) { fieldError(name, '名稱須為 1 至 1024 字元。', nameError); throw new Error('請修正物件名稱。'); }
      return value;
    });
    if (item.editableFields.includes('active')) this.readers.set('active', () => active.checked);
    const layer = node('select', { id: 'object-layer', disabled: !item.editableFields.includes('layer') });
    layers.forEach((label, index) => layer.append(node('option', { value: String(index), text: `${index}: ${label || '（未命名）'}` })));
    layer.value = String(item.layer);
    body.append(node('div', { class: 'field-row' }, node('label', { htmlFor: 'object-layer', text: 'Layer' }), layer));
    if (item.editableFields.includes('layer')) this.readers.set('layer', () => Number(layer.value));
    const statics = node('details', {}, node('summary', { text: 'Static 設定' }));
    const options = node('div', { class: 'static-options' }); const checks = [];
    staticNames.forEach((text, index) => {
      const bit = 2 ** index, check = node('input', { type: 'checkbox', checked: (item.staticFlags & bit) !== 0, disabled: !item.editableFields.includes('staticFlags'), 'aria-label': text });
      checks.push(check); options.append(node('label', {}, check, text));
    });
    statics.append(options); body.append(statics);
    if (item.editableFields.includes('staticFlags')) this.readers.set('staticFlags', () => {
      let value = item.staticFlags;
      checks.forEach((check, index) => { const bit = 2 ** index; value = check.checked ? value | bit : value & ~bit; });
      return value >>> 0;
    });
    const transform = node('section', { class: 'component' }, node('h3', { text: '▾ Transform' }));
    this.vector(transform, item, 'position', 'Position', ['X', 'Y', 'Z']);
    this.vector(transform, item, 'rotation', 'Rotation', ['X', 'Y', 'Z', 'W']);
    transform.append(node('p', { class: 'inspector-help', text: 'Rotation 使用 Unity 原生 Quaternion（X / Y / Z / W）。' }));
    this.vector(transform, item, 'scale', 'Scale', ['X', 'Y', 'Z']); body.append(transform);
    if (!item.editable) body.append(node('p', { class: 'inspector-help', text: '此物件是來源唯讀、Prefab、未知結構或未驗證版本；原始資料保持原樣。' }));
    const actions = node('div', { class: 'inspector-actions' }, node('button', { id: 'apply-inspector', type: 'submit', class: 'primary', disabled: !item.editable, text: '套用屬性' }), node('button', { type: 'button', text: '還原輸入', onClick: () => { this.show(item, layers); this.changed(); } }));
    body.append(actions, node('p', { class: 'inspector-help', text: '套用後可復原；按「儲存」才會寫入 .unity。' }));
    const componentList = node('section', { class: 'component' }, node('h3', { text: '元件與參照' }));
    item.components.filter((name) => name !== 'Transform').forEach((name) => componentList.append(node('p', { class: 'inspector-help', text: `${name} · 保留原文` })));
    componentList.append(node('p', { class: 'identifier', text: `fileID ${item.id}\nGameObject ${item.gameObjectId}` }));
    if (item.meshGuid) componentList.append(node('p', { class: 'identifier', text: `Mesh GUID ${item.meshGuid}\nMesh fileID ${item.meshFileId}` }));
    body.append(componentList); $('inspector').replaceChildren(body); this.afterShow();
  }
  vector(parent, item, field, label, axes) {
    const row = node('div', { class: 'field-row' }, node('span', { text: label }));
    const inputs = [], fields = node('div', { class: 'vector-fields' });
    const error = node('p', { id: `${field}-error`, class: 'field-error' }); this.errors.set(field, error);
    axes.forEach((axis, index) => {
      const id = `${field}-${axis.toLowerCase()}`;
      const input = node('input', { id, type: 'text', inputMode: 'decimal', value: String(item[field][index]), disabled: !item.editableFields.includes(field), spellcheck: false, 'aria-label': `${label} ${axis}`, 'aria-describedby': error.id });
      const axisLabel = node('label', { htmlFor: id, class: `vector-axis axis-${axis.toLowerCase()}` }, axis, input);
      inputs.push(input); fields.append(axisLabel);
    });
    row.append(fields); parent.append(row, error);
    if (item.editableFields.includes(field)) this.readers.set(field, (validate) => {
      const values = inputs.map((input) => input.value.trim() ? Number(input.value) : Number.NaN);
      const invalidIndex = values.findIndex((value) => !Number.isFinite(value) || !Number.isFinite(Math.fround(value)));
      const zeroRotation = field === 'rotation' && values.every((value) => value === 0);
      if (validate && (invalidIndex >= 0 || zeroRotation)) {
        fieldError(inputs[Math.max(invalidIndex, 0)], zeroRotation ? 'Quaternion 不可全部為 0。' : '請輸入有限數值，例如 1.25。', error);
        throw new Error(`${label} 數值無效，輸入仍保留。`);
      }
      if (validate) inputs.forEach((input) => input.setAttribute('aria-invalid', 'false'));
      return values;
    });
  }
  showAsset(info, imageUrl) {
    this.disposeImage(); this.item = null; this.readers.clear(); this.errors.clear(); this.imageUrl = imageUrl;
    $('selection-kind').textContent = '資源 · 唯讀';
    const body = node('div', { class: 'inspector-content' }, node('h3', { text: info.path.split('/').at(-1) }), node('p', { class: 'identifier', text: info.path }), node('p', { class: 'inspector-help', text: `${info.extension || '檔案'} · ${info.bytes.toLocaleString('zh-TW')} bytes` }));
    if (imageUrl) body.append(node('img', { src: imageUrl, alt: info.path, class: 'asset-preview' }));
    if (info.text) body.append(node('pre', { class: 'asset-text', text: info.text }));
    body.append(node('p', { class: 'inspector-help', text: '來源資源檢視；匯入設定目前唯讀。選取 .mat 可調整材質參數與貼圖。' }));
    $('inspector').replaceChildren(body);
  }
  async showModel(info, assets) {
    this.showAsset(info); const generation = this.modelGeneration;
    const host = node('div', { class: 'material-ball', role: 'img', 'aria-label': '來源模型幾何預覽，可拖曳旋轉' });
    const feedback = node('p', { class: 'inspector-help', text: '讀取來源模型幾何…' });
    $('inspector').querySelector('.inspector-content').append(host, feedback);
    try {
      this.modelSurface = new PreviewSurface(host);
      const models = await assets.geometry(info.path, info.meshIds?.[0] ?? '0');
      if (generation !== this.modelGeneration) return;
      let triangles = 0;
      for (const model of models) { const material = new THREE.MeshNormalMaterial(); assets.materials.add(material); this.modelSurface.add(new THREE.Mesh(model.geometry, material)); triangles += (model.geometry.index?.count ?? model.geometry.attributes.position.count) / 3; }
      this.modelSurface.frame(); host.dataset.modelCount = String(models.length);
      feedback.textContent = `${models.length} 個來源網格 · ${Math.round(triangles).toLocaleString('zh-TW')} 三角形。此處檢查幾何；場景材質請由 Renderer 選取。Rig／動畫／匯入設定維持原樣。`;
    } catch (error) { if (generation === this.modelGeneration && error.name !== 'AbortError') feedback.textContent = error.message; }
  }
}
