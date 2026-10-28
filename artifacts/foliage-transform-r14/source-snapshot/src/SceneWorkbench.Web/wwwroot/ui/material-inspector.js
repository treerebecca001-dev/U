import * as THREE from '../vendor/three.module.js';
import { PreviewSurface } from './preview-surface.js';
import { $, node, fieldError } from './controls.js';

export class MaterialInspector {
  constructor(apply, changed) { this.apply = apply; this.changed = changed; this.snapshot = null; this.readers = []; this.previewGeneration = 0; }
  hasDraft() { return this.readers.some((r) => JSON.stringify(r.read(false)) !== JSON.stringify(r.before)); }
  edits() { return this.readers.filter((r) => JSON.stringify(r.read(false)) !== JSON.stringify(r.before)).map((r) => ({ name: r.name, part: r.part, value: r.read(true) })); }
  close() { this.previewGeneration++; this.showGeneration = (this.showGeneration ?? 0) + 1; this.snapshot = null; this.readers = []; this.surface?.dispose(); this.surface = null; if (this.ball) { this.assets?.releaseMaterial(this.ball.material); this.assets?.geometries.delete(this.ball.geometry); this.ball.geometry.dispose(); this.ball = null; } this.renderer = null; }
  show(snapshot, assets, onBack) {
    this.close(); this.snapshot = snapshot; this.assets = assets; this.readers = [];
    $('selection-kind').textContent = snapshot.isAse ? 'ASE Material' : 'Material';
    const form = node('form', { class: 'inspector-content material-inspector', noValidate: true });
    form.addEventListener('submit', (event) => { event.preventDefault(); if (!event.isComposing) this.apply(); });
    form.append(node('h3', { text: snapshot.name }), node('p', { class: 'identifier', text: snapshot.path }), node('p', { class: 'inspector-help', text: `Shader：${snapshot.shaderName || '未適配'}${snapshot.editable ? '' : ' · 唯讀'}` }));
    const preview = node('div', { class: 'material-ball', role: 'img', 'aria-label': '材質球預覽，可拖曳旋轉' }); this.adapterStatus = node('p', { class: 'inspector-help', text: '準備材質預覽…' });
    form.append(preview, this.adapterStatus, node('p', { class: 'inspector-help', text: snapshot.editable ? '數值即時預覽，貼圖套用後更新。「套用材質」加入復原紀錄，「儲存」寫入原生 .mat。' : '原生參數唯讀。此預覽不執行原 Shader；適配限制列於上方，預覽時間不存入材質。' }));
    const time = node('input', { id: 'material-preview-time', type: 'range', min: '0', max: '30', step: '.1', value: String(assets.previewTime ?? 0), 'aria-label': '材質預覽時間（秒）' });
    const timeLabel = node('span', { class: 'inspector-help', text: `${assets.previewTime ?? 0} 秒` });
    form.append(node('label', { htmlFor: time.id, class: 'inspector-help', text: '預覽時間（不存入材質）' }), time, timeLabel);
    time.addEventListener('input', () => { assets.previewTime = Number(time.value); timeLabel.textContent = `${time.value} 秒`; assets.materials.forEach((m) => { if (m.uniforms?.uPreviewTime) m.uniforms.uPreviewTime.value = assets.previewTime; }); this.renderBall(); });
    const advanced = node('details', { class: 'component' }, node('summary', { text: '進階／隱藏 Shader 參數' }));
    for (const property of snapshot.properties) {
      const section = node('section', { class: 'material-property' }, node('h4', { text: property.label }), node('p', { class: 'identifier', text: property.name }));
      if (['2D', '2DArray', 'Cube', '3D'].includes(property.kind)) {
        const input = node('input', { id: `mat-${property.name}-texture`, type: 'text', value: property.texturePath ?? '', disabled: !property.editable, spellcheck: false });
        const error = node('p', { id: `mat-${property.name}-texture-error`, class: 'field-error' });
        section.append(node('label', { htmlFor: input.id, class: 'inspector-help', text: property.kind === '2D' ? '貼圖路徑（可拖入 Project 貼圖）' : `${property.kind} 來源 · 此維度目前唯讀` }), input, error);
        if (property.editable) {
          this.readers.push({ name: property.name, part: 'texture', before: property.texturePath ?? '', read: (validate) => {
            const path = input.value.trim(); if (validate && path && (!path.startsWith('Assets/') || !/\.(png|jpe?g|webp|tga|bmp)$/i.test(path) || path.split(/[\\/]/).includes('..'))) { fieldError(input, '請輸入 Assets/ 開頭的有效貼圖路徑，或留空移除。', error); throw new Error('貼圖路徑無效，輸入仍保留。'); }
            return path;
          } });
          input.addEventListener('dragover', (event) => { event.preventDefault(); });
          input.addEventListener('drop', (event) => { event.preventDefault(); input.value = event.dataTransfer.getData('text/plain'); input.dispatchEvent(new Event('input', { bubbles: true })); });
          this.numbers(section, property, 'scale', property.scale, ['X', 'Y'], 'Tiling'); this.numbers(section, property, 'offset', property.offset, ['X', 'Y'], 'Offset');
        }
        if (property.texturePath && ['2D', '2DArray'].includes(property.kind)) {
          const image = node('canvas', { class: 'material-thumbnail', width: 96, height: 64, 'aria-label': property.label + ' 貼圖縮圖' }); section.append(image);
          const generation = this.showGeneration;
          assets.texture(property.texturePath).then(({ texture }) => {
            if (generation !== this.showGeneration || !image.isConnected) return;
            if (texture.image?.data) { const temporary = document.createElement('canvas'); temporary.width = texture.image.width; temporary.height = texture.image.height; temporary.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(texture.image.data), texture.image.width, texture.image.height), 0, 0); image.getContext('2d').drawImage(temporary, 0, 0, 96, 64); }
            else image.getContext('2d').drawImage(texture.image, 0, 0, 96, 64);
          }).catch((error) => { if (error.name !== 'AbortError') image.setAttribute('aria-label', error.message); });
        }
      } else this.numbers(section, property, 'value', property.value, property.value.length === 4 ? property.kind === 'Color' ? ['R', 'G', 'B', 'A'] : ['X', 'Y', 'Z', 'W'] : [''], property.kind === 'Int' && !property.integerStorage ? 'Legacy Int（Float32 精確整數）' : property.kind);
      (property.hidden ? advanced : form).append(section);
    }
    if (advanced.children.length > 1) form.append(advanced);
    form.append(node('p', { class: 'inspector-help', text: `Keywords（保留原值）：${snapshot.keywords.join(', ') || '無'}` }));
    form.append(node('div', { class: 'inspector-actions' }, node('button', { id: 'apply-material', type: 'submit', class: 'primary', disabled: !snapshot.editable, text: '套用材質' }), node('button', { type: 'button', text: '還原輸入', onClick: () => { this.show(snapshot, assets, onBack); this.changed(); } })), node('button', { id: 'back-to-scene', type: 'button', text: '返回場景物件', onClick: onBack }));
    form.addEventListener('input', () => { this.changed(); this.updateBall(this.draftSnapshot()); }); form.addEventListener('change', () => { this.changed(); this.updateBall(this.draftSnapshot()); });
    $('inspector').replaceChildren(form); this.mountBall(preview); this.updateBall(snapshot);
  }
  numbers(parent, property, part, values, axes, label) {
    if (!values) return;
    const inputs = [], group = node('div', { class: 'material-number-fields' });
    const error = node('p', { id: `mat-${property.name}-${part}-error`, class: 'field-error' });
    axes.forEach((axis, i) => {
      const input = node('input', { id: `mat-${property.name}-${part}-${axis || 'value'}`, type: 'text', inputMode: 'decimal', value: String(values[i]), disabled: !property.editable, spellcheck: false, 'aria-label': `${property.name} ${part} ${axis}`, 'aria-describedby': error.id });
      inputs.push(input); group.append(node('label', { htmlFor: input.id, class: 'vector-axis' }, axis || label, input));
    });
    parent.append(node('div', { class: 'inspector-help', text: property.min !== null ? `${label} · 建議 ${property.min} ～ ${property.max}` : label }), group, error);
    if (property.editable) this.readers.push({ name: property.name, part, before: values, read: (validate) => {
      const result = inputs.map((input) => input.value.trim() ? Number(input.value) : NaN);
      const invalid = result.findIndex((value) => !Number.isFinite(value) || !Number.isFinite(Math.fround(value)) || ['Int', 'Integer'].includes(property.kind) && (!Number.isInteger(value) || value < -2147483648 || value > 2147483647 || !property.integerStorage && property.kind === 'Int' && Math.fround(value) !== value));
      if (validate && invalid >= 0) { fieldError(inputs[invalid], property.integerStorage ? '請輸入 -2147483648 ～ 2147483647 的整數。' : property.kind === 'Int' ? 'Legacy Int 必須是 Float32 可精確表示的 Int32，請勿使用會被捨入的整數。' : '請輸入有效的有限 Float32 數值。', error); throw new Error('材質參數無效，輸入仍保留。'); }
      if (validate) { error.textContent = ''; inputs.forEach((input) => input.setAttribute('aria-invalid', 'false')); }
      return result;
    } });
  }
  draftSnapshot() {
    const draft = structuredClone(this.snapshot);
    for (const reader of this.readers) {
      const value = reader.read(false); if (Array.isArray(value) && value.some((n) => !Number.isFinite(n) || !Number.isFinite(Math.fround(n)))) return null;
      const property = draft.properties.find((p) => p.name === reader.name);
      if (reader.part !== 'texture') property[reader.part] = value;
    }
    return draft;
  }
  mountBall(host) {
    try { this.surface = new PreviewSurface(host); this.renderer = this.surface.renderer; } catch { this.adapterStatus.textContent = 'WebGL 不可用；仍可編輯材質參數。'; return; }
    const geometry = this.assets.ensureUvs(new THREE.SphereGeometry(1, 40, 24));
    this.ball = new THREE.Mesh(geometry, new THREE.MeshBasicMaterial({ color: 0x888888 })); this.surface.add(this.ball);
  }
  async updateBall(snapshot) {
    if (!snapshot || !this.renderer) return; const generation = ++this.previewGeneration;
    try {
      const material = await this.assets.material(snapshot); if (generation !== this.previewGeneration || !this.renderer) { this.assets.releaseMaterial(material); return; }
      this.assets.releaseMaterial(this.ball.material); this.ball.material = material; this.adapterStatus.textContent = material.userData.adapterStatus ?? 'Shader 未適配，使用實心診斷表面'; this.renderBall();
    } catch (error) { if (generation === this.previewGeneration && error.name !== 'AbortError') this.adapterStatus.textContent = error.message; }
  }
  renderBall() { this.surface?.render(); }
}
