// 从零造一条 C 大调上行音阶：新建轨 + 新建 part + 逐个加音符。
// 不依赖当前是否打开了 part —— 适合空工程上测试。
// 覆盖：tl.currentProject() / project.addTrack(name) / track.addPart({pos,dur,name}) / part.addNote(...)
const project = tl.currentProject();
const track = project.addTrack("C Major Scale");

const q = tl.ppq;                       // 每个四分音符的 tick 数
const part = track.addPart({ pos: 0, dur: 8 * q, name: "scale" });

const degrees = [0, 2, 4, 5, 7, 9, 11, 12]; // C D E F G A B C 相对半音
const lyrics  = ["do","re","mi","fa","sol","la","si","do"];
for (let i = 0; i < degrees.length; i++)
  part.addNote({ pos: i * q, dur: q, pitch: 60 + degrees[i], lyric: lyrics[i] });

print("已生成 " + degrees.length + " 个音符的 C 大调音阶");
