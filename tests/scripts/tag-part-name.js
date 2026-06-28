// part 右键菜单工具（context=part，编排区命中 part）：给每个选中的 part 名追加 " *"。
// 右键命中 part 时该 part 必被选中，故目标 = selectedParts()（支持多选）。验证：part 工具只在命中 part 的菜单出现。
function getScriptInfo() {
  const zh = tl.language === 'zh-CN';
  return {
    name: zh ? '标记选中 Part' : 'Tag Selected Parts',
    author: 'TuneLab',
    version: '1.0.0',
    context: 'part',
  };
}

function main() {
  const parts = tl.selectedParts();
  for (const p of parts)
    p.name = (p.name || 'Part') + ' *';
  print('tagged ' + parts.length + ' part(s)');
}
