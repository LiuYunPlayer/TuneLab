// 选中音符升八度（无选中则整段升八度）。
// 覆盖：tl.currentPart() / part.selectedNotes() / part.notes() / 标量字段写 n.pitch
const part = tl.currentPart();
if (!part) throw new Error("先在钢琴窗打开一个 MIDI part");

let notes = part.selectedNotes();
if (notes.length === 0) notes = part.notes();

for (const n of notes) n.pitch += 12;
print("升八度音符数：" + notes.length);
