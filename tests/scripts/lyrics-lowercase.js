// 钢琴空白右键菜单工具（context=partContent）：把当前 part 全部音符的歌词转小写。
// 目标 = 当前正在编辑的 part 的内容（currentPart）。验证：partContent 工具只在钢琴卷帘空白处的菜单出现。
function getScriptInfo() {
  const zh = tl.language === 'zh-CN';
  return {
    name: zh ? '歌词转小写' : 'Lyrics to Lowercase',
    author: 'TuneLab',
    version: '1.0.0',
    context: 'partContent',
  };
}

function main() {
  const part = tl.currentPart();
  if (!part) { print('no current part'); return; }
  let n = 0;
  for (const note of part.notes()) {
    const lower = (note.lyric || '').toLowerCase();
    if (lower !== note.lyric) { note.lyric = lower; n++; }
  }
  print('lowercased ' + n + ' lyric(s)');
}
