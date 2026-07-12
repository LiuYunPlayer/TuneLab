// 快捷键测试 + 新 context：pianoSelection（钢琴窗范围选区）。挂在钢琴卷帘「命中选区带」右键；scope=PianoWindow。
// 作用：把选区 tick 段内起始的音符各 +1 半音（凭 tl.pianoSelection() 的 startTick/endTick）。空选区则空转。
function getScriptInfo() {
  const zh = tl.language === 'zh-CN';
  return {
    name: zh ? '选区内音符升半音' : 'Nudge Notes In Range',
    category: zh ? '快捷键测试' : 'Keybinding Test',
    author: 'TuneLab',
    version: '1.0.0',
    context: 'pianoSelection',
    defaultGesture: 'mod+shift+n',
  };
}

function main() {
  const sel = tl.pianoSelection();
  if (!sel) { print('no piano range selection'); return; }
  const part = tl.currentPart();
  if (!part) return;
  const notes = part.notesInRange(sel.startTick, sel.endTick);
  for (const note of notes) note.pitch += 1;
  print('nudged ' + notes.length + ' note(s) in ticks ' + sel.startTick + '..' + sel.endTick);
}
