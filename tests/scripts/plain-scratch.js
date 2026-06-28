// 普通脚本（无 getScriptInfo）：验证它【不】出现在任何菜单里（只能在 Script 侧栏手动跑）。
// 整段即动作：把当前 part 第一个音符歌词改成 "hi"。
const part = tl.currentPart();
if (part) {
  const notes = part.notes();
  if (notes.length > 0) notes[0].lyric = 'hi';
}
