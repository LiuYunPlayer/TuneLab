// 音符右键菜单工具（context=note）：给每个选中音符在上方三度加一个和声音符。
// 右键命中音符时该音符必被选中，故目标 = selectedNotes()。验证：note 工具只在命中音符的菜单出现。
function getScriptInfo() {
  const zh = tl.language === 'zh-CN';
  return {
    name: zh ? '加三度和声' : 'Add Third Harmony',
    author: 'TuneLab',
    version: '1.0.0',
    context: 'note',
  };
}

function main() {
  const part = tl.currentPart();
  if (!part) return;
  const selected = part.selectedNotes();
  for (const n of selected)
    part.addNote({ pos: n.pos, dur: n.dur, pitch: n.pitch + 4, lyric: n.lyric });
  print('added ' + selected.length + ' harmony note(s)');
}
