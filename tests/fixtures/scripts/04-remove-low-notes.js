// 删除当前 part 中所有低于 C2(MIDI 36) 的音符。
// 覆盖：删除走父级 part.removeNote(n) / 条件过滤 / 读只读字段 n.pitchName
const part = tl.currentPart();
if (!part) throw new Error("先在钢琴窗打开一个 MIDI part");

let removed = 0;
for (const n of part.notes()) {
  if (n.pitch < 36) {
    print("删除 " + n.pitchName + " @ " + n.pos);
    part.removeNote(n);
    removed++;
  }
}
print("共删除 " + removed + " 个低音");
