// 在当前 part 前 4 小节（4/4）画一条音量渐强自动化线。
// 覆盖：tl.ppq / part.automationIds() 探测可用参数 / part.setAutomation(id, start, end, points)
const part = tl.currentPart();
if (!part) throw new Error("先在钢琴窗打开一个 MIDI part");

const ids = part.automationIds();
const id = ids.includes("Volume") ? "Volume" : ids[0];
if (!id) throw new Error("当前 voice 没有可编辑的自动化参数");

const start = 0, end = 4 * 4 * tl.ppq; // 前 4 小节
part.setAutomation(id, start, end, [
  { tick: start, value: 0.2 },
  { tick: end,   value: 1.0 },
]);
print("已在 [" + start + ", " + end + ") 画 \"" + id + "\" 渐强线");
