// 给当前 part 中时值 ≥ 一个四分音符的音符各加一段颤音（覆盖音符后半段）。
// 覆盖：part.addVibrato({...}) / 读 n.pos,n.dur / tl.ppq 阈值判断
const part = tl.currentPart();
if (!part) throw new Error("先在钢琴窗打开一个 MIDI part");

const minDur = tl.ppq; // 一个四分音符
let added = 0;
for (const n of part.notes()) {
  if (n.dur < minDur) continue;
  const half = Math.floor(n.dur / 2);
  part.addVibrato({ pos: n.pos + half, dur: n.dur - half, frequency: 6, amplitude: 0.7 });
  added++;
}
print("已为 " + added + " 个长音符添加颤音");
