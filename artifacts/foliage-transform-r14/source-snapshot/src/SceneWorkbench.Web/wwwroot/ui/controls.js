export const $ = (id) => document.getElementById(id);
export function node(tag, attrs = {}, ...children) {
  const element = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (key.startsWith('on')) element.addEventListener(key.slice(2).toLowerCase(), value);
    else if (key === 'class') element.className = value;
    else if (key === 'text') element.textContent = value;
    else if (key in element && !key.startsWith('aria')) element[key] = value;
    else element.setAttribute(key, value);
  }
  element.append(...children.filter((child) => child !== null && child !== undefined));
  return element;
}
export function status(message, tone = 'info') {
  $('progress').textContent = message;
  $('progress').dataset.tone = tone;
  if (tone === 'error') $('operation-error').textContent = message;
}
export function clearError() { $('operation-error').textContent = ''; }
export function fieldError(input, message, target) {
  input.setAttribute('aria-invalid', message ? 'true' : 'false');
  input.setAttribute('aria-describedby', target.id);
  target.textContent = message;
  if (message) {
    if (input.disabled) input.setAttribute('data-focus-after-busy', 'true');
    else { input.focus(); input.scrollIntoView({ block: 'nearest' }); }
  }
}
export function openDialog(dialog, initialFocus) {
  const previous = document.activeElement;
  dialog.addEventListener('close', () => { if (previous?.isConnected) previous.focus(); }, { once: true });
  dialog.showModal();
  (initialFocus ?? dialog.querySelector('input, button'))?.focus();
}
export function closeDialog(dialog) { if (dialog.open) dialog.close(); }
export function setBusyControls(busy) {
  for (const control of document.querySelectorAll('#inspector input, #inspector textarea, #inspector select, #inspector button, #hierarchy button, #assets button, dialog form input, dialog form button[type="submit"]')) {
    if (busy) {
      if (!control.hasAttribute('data-before-busy-disabled')) control.setAttribute('data-before-busy-disabled', String(control.disabled));
      control.disabled = true;
    } else if (control.hasAttribute('data-before-busy-disabled')) {
      control.disabled = control.getAttribute('data-before-busy-disabled') === 'true';
      control.removeAttribute('data-before-busy-disabled');
    }
  }
  if (!busy) {
    const target = document.querySelector('[data-focus-after-busy]');
    if (target && !target.disabled) { target.removeAttribute('data-focus-after-busy'); target.focus(); target.scrollIntoView({ block: 'nearest' }); }
  }
}
export function chooseUnsaved() {
  const dialog = $('unsaved-dialog');
  return new Promise((resolve) => {
    let result = 'cancel';
    const click = (event) => {
      const choices = { 'unsaved-cancel': 'cancel', 'unsaved-discard': 'discard', 'unsaved-save': 'save' };
      if (choices[event.target.id]) { result = choices[event.target.id]; dialog.close(); }
    };
    dialog.addEventListener('click', click);
    dialog.addEventListener('close', () => { dialog.removeEventListener('click', click); resolve(result); }, { once: true });
    openDialog(dialog, $('unsaved-cancel'));
  });
}
export function wireSearch(input, onSearch, { remote = false } = {}) {
  const clear = document.querySelector(`[data-clear="${input.id}"]`);
  let composing = false, timer;
  const update = (immediate = false) => {
    clear.hidden = input.value.length === 0;
    clearTimeout(timer);
    if (composing) return;
    if (remote && !immediate && input.value) timer = setTimeout(() => onSearch(input.value), 300);
    else onSearch(input.value);
  };
  input.addEventListener('compositionstart', () => { composing = true; clearTimeout(timer); });
  input.addEventListener('compositionend', () => { composing = false; update(); });
  input.addEventListener('input', () => update());
  input.addEventListener('keydown', (event) => { if (event.key === 'Enter' && !event.isComposing && !composing) { event.preventDefault(); update(true); } });
  clear.addEventListener('click', () => { input.value = ''; update(true); input.focus(); });
}
export function wireTabs(ids) {
  function activate(id) {
    for (const tabId of ids) {
      const tab = $(tabId), selected = tabId === id;
      tab.setAttribute('aria-selected', String(selected)); tab.tabIndex = selected ? 0 : -1;
      $(tab.getAttribute('aria-controls')).hidden = !selected;
    }
  }
  for (const id of ids) {
    $(id).addEventListener('click', () => activate(id));
    $(id).addEventListener('keydown', (event) => {
      if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      const index = ids.indexOf(id), delta = event.key === 'ArrowLeft' ? -1 : 1;
      const target = event.key === 'Home' ? ids[0] : event.key === 'End' ? ids.at(-1) : ids[(index + delta + ids.length) % ids.length];
      activate(target); $(target).focus();
    });
  }
  return activate;
}
