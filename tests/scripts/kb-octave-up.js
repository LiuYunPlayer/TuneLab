// 快捷键测试：稳定 id + 建议默认手势（空槽落）。global 工具，声明 id='kb.octaveUp'（重命名文件不丢绑定）、
// defaultGesture='mod+shift+u'（Mac ⌘⇧U / Win Ctrl+Shift+U；该键默认空闲，应自动生效）。作用同 octave-up。
function getScriptInfo() {
  const zh = tl.language === 'zh-CN';
  return {
    name: zh ? 'KB 升八度' : 'KB Octave Up',
    category: zh ? '快捷键测试' : 'Keybinding Test',
    author: 'TuneLab',
    version: '1.0.0',
    context: 'global',
    id: 'kb.octaveUp',
    defaultGesture: 'mod+shift+u',
  };
}

function main() {
  const part = tl.currentPart();
  if (!part) { print('no current part'); return; }
  let n = 0;
  for (const note of part.notes()) { note.pitch += 12; n++; }
  print('raised ' + n + ' note(s) an octave');
}
