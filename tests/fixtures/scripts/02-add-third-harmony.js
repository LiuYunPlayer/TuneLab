// 给当前 part 每个音符上方加一个大三度和声（沿用原歌词）。
// 覆盖：part.notes() 快照 / part.addNote({...}) 在父级创建子对象 / 读 n.pos,n.dur,n.pitch,n.lyric
const part = tl.currentPart();
if (!part) throw new Error("先在钢琴窗打开一个 MIDI part");

// 先取快照再循环：addNote 会改变 notes() 的下次返回，但快照数组不受影响。
const original = part.notes();
for (const n of original)
  part.addNote({ pos: n.pos, dur: n.dur, pitch: n.pitch + 4, lyric: n.lyric });

print("原音符 " + original.length + " 个，已各加一个三度和声");
