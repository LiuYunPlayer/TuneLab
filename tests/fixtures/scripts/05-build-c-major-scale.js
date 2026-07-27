// 从零造一条 C 大调上行音阶：新建轨 + 新建 part + 逐个加音符。
// 不依赖当前是否打开了 part —— 适合空工程上测试。
// 覆盖：tl.currentProject() / project.addTrack(info) / track.addPart(info) / part.addNote(info)
const project = tl.currentProject();
const track = project.addTrack({ name: "C Major Scale" });

const q = tl.ppq;                       // 每个四分音符的 tick 数
// part 几何 = 三个原始字段：pos 是锚点（也是内容坐标原点），起点 = pos + startOffset（默认 0），
// 终点 = pos + endOffset。故"从 0 开始、长 8 拍"就是 pos:0 + endOffset:8q。
const part = track.addPart({ pos: 0, endOffset: 8 * q, name: "scale" });

const degrees = [0, 2, 4, 5, 7, 9, 11, 12]; // C D E F G A B C 相对半音
const lyrics  = ["do","re","mi","fa","sol","la","si","do"];
for (let i = 0; i < degrees.length; i++)
  part.addNote({ pos: i * q, dur: q, pitch: 60 + degrees[i], lyric: lyrics[i] });

print("已生成 " + degrees.length + " 个音符的 C 大调音阶");
