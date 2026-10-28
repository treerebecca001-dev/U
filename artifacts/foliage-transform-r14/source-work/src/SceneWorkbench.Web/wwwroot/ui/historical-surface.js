// A bounded historical render-state reference, never a ShaderGraph compiler.
export async function historicalPlantState(snapshot, assets, pin) {
  if (!pin || pin.renderFace !== 'Both' || pin.alphaClip !== true || pin.alphaCutoff !== .5 || pin.materialOverride !== false ||
      snapshot.previewKind !== 'saved-properties' || snapshot.shaderPath || snapshot.shaderHash ||
      snapshot.shaderGuid !== pin.guid || snapshot.shaderFileId !== pin.fileId ||
      !snapshot.properties.some(p => p.name === '_Texture' && p.kind === '2D') ||
      !snapshot.properties.some(p => p.name === '_AlphaCutoff')) return null;
  assets.historicalSources ??= new Map();
  const key = pin.source + ':' + pin.sha256;
  if (!assets.historicalSources.has(key)) assets.historicalSources.set(key, (async () => {
    try {
      const bytes = await (await assets.fetch(pin.source)).arrayBuffer();
      const digest = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)), b => b.toString(16).padStart(2, '0')).join('');
      if (digest === pin.sha256) return true;
      assets.warn('歷史植物 ShaderGraph 參考雜湊已改變；雙面設定需要重新驗證，維持一般唯讀近似。');
    } catch (error) { if (error.name !== 'AbortError') assets.warn('歷史植物 ShaderGraph 參考無法讀取，維持一般唯讀近似。'); }
    return false;
  })());
  return await assets.historicalSources.get(key) ? pin : null;
}
