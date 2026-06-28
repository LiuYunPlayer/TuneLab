// 编排区空白泳道右键菜单工具（context=trackContent，轨道容器/泳道）：给该轨所有 part 名追加 " *"。
// 右键空白泳道时该轨必被选中，故目标 = selectedTracks()；操作的是轨道的内容（它的 parts）。
function getScriptInfo() {
  const zh = tl.language === 'zh-CN';
  return {
    name: zh ? '标记轨内所有 Part' : 'Tag All Parts in Track',
    author: 'TuneLab',
    version: '1.0.0',
    context: 'trackContent',
  };
}

function main() {
  let n = 0;
  for (const t of tl.selectedTracks())
    for (const p of t.parts()) { p.name = (p.name || 'Part') + ' *'; n++; }
  print('tagged ' + n + ' part(s)');
}
