import { $, node } from './controls.js';

const tools = { translate: ['position', '移動'], rotate: ['rotation', '旋轉'], scale: ['scale', '縮放'] };
const steps = { translate: [.1, .5, 1, 5], rotate: [5, 15, 30, 45, 90], scale: [.1, .25, .5, 1] };

// The viewport mirrors Unity's Z axis. Rotation is a quaternion, never Euler angles.
export function nativeTransformValue(object, field) {
  if (field === 'position') return [object.position.x, object.position.y, -object.position.z];
  if (field === 'rotation') return [-object.quaternion.x, -object.quaternion.y, object.quaternion.z, object.quaternion.w];
  return object.scale.toArray();
}

export class TransformTools {
  constructor(gizmo, { commit, changed, failed, cancelled = () => {} }) {
    this.gizmo = gizmo; this.commit = commit; this.changed = changed; this.failed = failed;
    this.cancelled = cancelled;
    this.tool = 'translate'; this.space = 'world'; this.allowed = true;
    this.gestureTrace = [];
    this.snapSteps = { translate: 1, rotate: 15, scale: .1 };
    // Passive DOM-backed observations keep a bounded chronology of the last gesture.
    // They never synthesize input or change preview/document values.
    for (const type of ['pointerdown', 'pointermove', 'pointerup']) gizmo.domElement.addEventListener(type, event => {
      this.pointerEvent = event;
      if (type === 'pointerup' && event.pointerId === this.gesturePointerId) {
        this.recordGesture('pointerup', event); this.gesturePointerId = null;
      }
    }, true);
    for (const tool of Object.keys(tools)) $(tool).addEventListener('click', () => this.setTool(tool));
    $('view-tool').addEventListener('click', () => this.setTool(null));
    $('transform-space').addEventListener('change', () => { this.space = $('transform-space').value; this.sync(); });
    $('snap-toggle').addEventListener('change', () => this.sync());
    $('snap-step').addEventListener('change', () => { if (this.tool) this.snapSteps[this.tool] = Number($('snap-step').value); this.sync(); });
    gizmo.addEventListener('mouseDown', () => {
      const object = gizmo.object;
      if (!this.allowed || !object || !this.tool) return;
      this.drag = { object, field: tools[this.tool][0], before: nativeTransformValue(object, tools[this.tool][0]),
        position: object.position.clone(), quaternion: object.quaternion.clone(), scale: object.scale.clone() };
      this.gestureContext = { id: object.userData.id, field: this.drag.field, before: this.drag.before.slice() };
      this.gesturePointerId = this.pointerEvent?.pointerId;
      this.gestureTrace = []; this.recordGesture('begin', this.pointerEvent);
      this.sync();
    });
    gizmo.addEventListener('objectChange', () => { if (this.drag) this.recordGesture('preview', this.pointerEvent); });
    gizmo.addEventListener('mouseUp', () => this.finish());
    gizmo.addEventListener('dragging-changed', () => this.sync());
    document.addEventListener('keydown', event => {
      if (event.isComposing || event.keyCode === 229 || document.querySelector('dialog[open]')) return;
      if (event.key === 'Escape' && this.drag) { event.preventDefault(); this.cancel('escape', event); return; }
      if (event.target?.closest?.('input, textarea, select, [contenteditable]')) return;
      if (event.ctrlKey || event.metaKey || event.altKey || event.shiftKey || event.repeat || !this.allowed || this.drag) return;
      const key = event.key.toLowerCase();
      if (['q', 'w', 'e', 'r'].includes(key)) { event.preventDefault(); this.setTool({ q: null, w: 'translate', e: 'rotate', r: 'scale' }[key]); }
    });
    gizmo.domElement.addEventListener('pointercancel', event => this.cancel('pointercancel', event));
    window.addEventListener('blur', event => this.cancel('blur', event));
    document.addEventListener('visibilitychange', event => { if (document.hidden) this.cancel('visibilitychange', event); });
    this.sync();
  }
  setTool(tool) {
    if (!this.allowed || this.drag || this.gizmo.dragging) return;
    this.tool = tool; this.sync();
  }
  select(object) { if (this.drag && object !== this.object) this.cancel(); this.object = object; this.sync(); }
  setAllowed(allowed) { if (!allowed) this.cancel(); this.allowed = allowed; this.sync(); }
  sync() {
    const { gizmo, object, tool } = this;
    const field = tool && tools[tool][0], dragging = Boolean(this.drag || gizmo.dragging);
    for (const id of ['view-tool', ...Object.keys(tools)]) {
      $(id).setAttribute('aria-pressed', String(id === (tool ?? 'view-tool')));
      $(id).disabled = !this.allowed || dragging;
    }
    $('transform-space').disabled = !this.allowed || dragging || tool === 'scale' || !tool;
    $('transform-space').value = tool === 'scale' ? 'local' : this.space;
    $('snap-toggle').disabled = !this.allowed || dragging || !tool;
    const picker = $('snap-step');
    picker.disabled = !this.allowed || dragging || !tool;
    if (picker.dataset.tool !== (tool ?? '')) {
      picker.dataset.tool = tool ?? '';
      picker.replaceChildren(...(steps[tool] ?? steps.translate).map(value => node('option', {
        value: String(value), text: `${value}${tool === 'rotate' ? '°' : tool === 'scale' ? ' Scale' : ' 單位'}`
      })));
    }
    picker.value = String(this.snapSteps[tool] ?? 1);
    if (tool) gizmo.setMode(tool);
    gizmo.setSpace(tool === 'scale' ? 'local' : this.space);
    const snapping = $('snap-toggle').checked;
    gizmo.setTranslationSnap(snapping ? this.snapSteps.translate : null);
    gizmo.setRotationSnap(snapping ? this.snapSteps.rotate * Math.PI / 180 : null);
    gizmo.setScaleSnap(snapping ? this.snapSteps.scale : null);
    // A singular parent cannot be inverted by TransformControls. Numeric fields remain available.
    object?.updateWorldMatrix(true, false);
    const determinant = object?.matrixWorld.determinant() ?? 1;
    const safeParent = Number.isFinite(determinant) && Math.abs(determinant) > 1e-12;
    const editable = this.allowed && tool && object?.userData.editable &&
      object.userData.editableFields?.includes(field) && safeParent;
    if (!dragging) { gizmo.detach(); if (editable) gizmo.attach(object); }
    const reason = !this.allowed ? '請先完成目前作業，或套用／還原 Inspector 輸入。' :
      !object ? '選取物件後可操作；W 移動 · E 旋轉 · R 縮放 · Q 檢視。' :
      !tool ? '檢視工具；拖曳旋轉視角，W／E／R 切換物件工具。' :
      !object.userData.editable || !object.userData.editableFields?.includes(field) ? '此物件的 Transform 唯讀；請選取可編輯的獨立物件。' :
      !safeParent ? '物件或父層縮放過小，無法拖曳操作；請由 Inspector 修改數值。' :
      `${tools[tool][1]} · ${tool === 'scale' ? '區域座標' : this.space === 'world' ? '世界座標' : '區域座標'}${snapping ? ` · 吸附 ${picker.selectedOptions[0]?.textContent}` : ''} · Esc 取消拖曳`;
    $('transform-status').textContent = reason;
    Object.assign($('viewport').dataset, { transformTool: tool ?? 'view', transformSpace: gizmo.space,
      transformSnap: String(snapping), transformEditable: String(Boolean(editable)), transformDragging: String(Boolean(this.drag)) });
  }
  recordGesture(phase, event, value) {
    const context = this.gestureContext; if (!context) return;
    value ??= this.drag ? nativeTransformValue(this.drag.object, this.drag.field) : this.gestureValue;
    this.gestureValue = value?.slice();
    const entry = { phase, at: performance.now(), ...context, value: this.gestureValue,
      active: Boolean(this.drag), changed: Boolean(value?.some((v, index) => Math.abs(v - context.before[index]) > 1e-10)),
      event: event ? { type: event.type, trusted: event.isTrusted, buttons: event.buttons ?? null, key: event.key ?? null,
        pointerId: event.pointerId ?? null, x: event.clientX ?? null, y: event.clientY ?? null, at: event.timeStamp } : null };
    if (phase === 'preview' && this.gestureTrace.at(-1)?.phase === 'preview') this.gestureTrace[this.gestureTrace.length - 1] = entry;
    else this.gestureTrace.push(entry);
    this.gestureTrace = this.gestureTrace.slice(-12);
    Object.assign($('viewport').dataset, { transformDragging: String(Boolean(this.drag)), transformDragChanged: String(entry.changed),
      transformGesture: JSON.stringify(this.gestureTrace) });
  }
  cancel(reason = 'context', event) {
    const drag = this.drag; if (!drag) return;
    this.recordGesture('cancel-request:' + reason, event);
    this.drag = null;
    drag.object.position.copy(drag.position); drag.object.quaternion.copy(drag.quaternion); drag.object.scale.copy(drag.scale);
    this.cancelled();
    this.gizmo.dragging = false; this.gizmo.axis = null; this.changed(); this.sync();
    this.recordGesture('cancelled:' + reason, event, nativeTransformValue(drag.object, drag.field));
    if (reason === 'pointercancel') this.gesturePointerId = null;
  }
  finish() {
    const drag = this.drag; this.drag = null; if (!drag) return;
    const value = nativeTransformValue(drag.object, drag.field);
    if (!value.every(v => Number.isFinite(v) && Number.isFinite(Math.fround(v))) ||
      (drag.field === 'rotation' && value.every(v => v === 0))) {
      this.drag = drag; this.cancel(); this.failed('操作結果超出可儲存範圍，已還原此次拖曳。'); return;
    }
    if (value.every((v, index) => Math.abs(v - drag.before[index]) <= 1e-10)) { this.recordGesture('unchanged', this.pointerEvent, value); return; }
    // One completed drag produces one existing scene command. Saving remains explicit.
    this.recordGesture('commit', this.pointerEvent, value);
    this.commit(drag.object.userData.id, drag.field, value);
  }
}
