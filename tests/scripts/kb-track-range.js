// 快捷键测试 + 新 context：trackSelection（编排区范围选区 tick×轨道）。挂在编排区「命中选区」右键；scope=TrackWindow。
// 作用：给落在选区 tick 段内（起始位于区间）的 part 名各追加 " ~"（凭 tl.trackSelection() 的 startTick/endTick）。空选区则空转。
function getScriptInfo() {
  const zh = tl.language === 'zh-CN';
  return {
    name: zh ? '标记选区内 Part' : 'Tag Parts In Region',
    category: zh ? '快捷键测试' : 'Keybinding Test',
    author: 'TuneLab',
    version: '1.0.0',
    context: 'trackSelection',
    defaultGesture: 'mod+shift+g',
  };
}

function main() {
  const sel = tl.trackSelection();
  if (!sel) { print('no arrangement region selection'); return; }
  let n = 0;
  for (const t of tl.tracks())
    for (const p of t.parts())
      if (p.startPos >= sel.startTick && p.startPos < sel.endTick) { p.name = (p.name || 'Part') + ' ~'; n++; }
  print('tagged ' + n + ' part(s) in ticks ' + sel.startTick + '..' + sel.endTick);
}
