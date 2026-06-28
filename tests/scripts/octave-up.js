// 全局工具 + 本地化显示名：出现在顶部「Scripts」菜单（分组 Pitch / 音高）。
// 把当前 part 的全部音符升一个八度。验证：双模式(main 被调)、category 分组、tl.language 本地化。
function getScriptInfo() {
  const zh = tl.language === 'zh-CN';
  return {
    name: zh ? '全部升八度' : 'Octave Up (All)',
    category: zh ? '音高' : 'Pitch',
    author: 'TuneLab',
    version: '1.0.0',
    context: 'global',
  };
}

function main() {
  const part = tl.currentPart();
  if (!part) { print('no current part'); return; }
  let n = 0;
  for (const note of part.notes()) { note.pitch += 12; n++; }
  print('raised ' + n + ' note(s) an octave');
}
