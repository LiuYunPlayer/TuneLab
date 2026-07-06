# 脚本 API 手册

用一小段 JavaScript 读取并编辑当前工程——尤其适合**批量、带循环/条件、需要计算**的编辑（"5–8 小节每个音符升八度再加三度和声" = 一个循环，胜过几十次手动操作）。

有两个入口，共享同一套**对象式** API：

- **「Script」右侧栏**：在 **Code** 面输入脚本、点 Run（或 Ctrl+Enter）运行，输出区显示 `print` 与运行结果；在 **Doc** 面就地查阅本手册。
- **内置 AI Agent**：模型自动写脚本调用同一套 API。

全局对象 `tl` 是**编辑器**，工程数据挂在 `tl.currentProject()` 上。轨/part/音符/颤音都是带字段和方法的**对象句柄**。

---

## 核心模型（先读这一节）

- **对象式，两种写法。** 记一条经验法则：
  - **裸属性** = 一个**标量字段**，可读也可写：`n.pitch`、`n.pitch += 12`、`track.isMute = true`。
  - **带括号的方法** = 一次**查询、创建、删除或计算**：`part.notes()`、`track.addPart({...})`、`part.removeNote(n)`。
- **创建和删除都挂在父对象上。** `project.addTrack()` / `removeTrack(track)`、`track.addPart()` / `removePart(part)`、`part.addNote()` / `removeNote(note)`、`part.addVibrato()` / `removeVibrato(vibrato)`。（没有 `x.remove()`——父对象同时负责增和删。）
- **整段脚本运行 = 一个可撤销单位。** 脚本里发生的所有改动合并成一次提交，`Ctrl+Z` 一步全部回退。给字段赋值或调用写方法都**立即生效**，但你**不需要**（也无法）自己提交或保存。
- **句柄当场取、当场用。** 句柄是对一个对象的不透明引用，带可读写的标量字段和方法，但没有 id。
  - 集合方法（`project.tracks()`、`track.parts()`、`part.notes()`、`part.vibratos()`）返回**普通数组**，用 `for-of` 或下标遍历、有 `.length`；每次调用都是一份**新快照**，要反复用就先存进变量。它**不是链表**，没有 `.first` / `.next`。
  - 句柄**仅当次运行有效**（对象无持久 id，关闭软件即失效）：脚本里**绝不要写死一个句柄值**，永远是「当场取、立即用」。删除后的句柄再用会报错。
- **坐标一律绝对 tick。** 所有位置/时长都是绝对（全局）tick（`tl.ppq` 取每四分音符的 tick 数，默认 480）——与播放线、小节同一坐标系。你**永不做坐标换算**。
- **音高用 MIDI 数字**，60 = C4（可含小数表示音分）。
- **出错则全部回退。** 脚本中途抛错时，它已做的改动全部撤销（工程保持不变），并返回错误信息——定位后改脚本重跑，不要基于半成品状态打补丁。
- **调试输出。** `print(x)` / `console.log(x)` 的输出会收集并显示在下方输出区。

---

## `tl`（编辑器）

编辑器级的入口——系统常量、当前工程、以及编辑器的临时状态。

| 成员 | 返回 | 说明 |
|---|---|---|
| `tl.ppq` | number | 每四分音符的 tick 数（默认 480）。 |
| `tl.currentProject()` | `project` | 当前工程（你的数据入口，见下）。 |
| `tl.currentPart()` | `part \| null` | 钢琴窗当前打开编辑的 MIDI part。 |
| `tl.trackSelection()` | `{startTick, endTick, startTrackNumber, endTrackNumber} \| null` | 编排区的**范围选区**——在编排区 Shift+拖 圈出的 tick×轨道矩形；轨道号 1-based、连续区间；无选区时为 `null`。与 `selectedParts`/`selectedNotes`（选中的**对象**）**正交**：它圈的是"一片地方"而非对象，用它批量处理落在区域里的东西。 |
| `tl.pianoSelection()` | `{startTick, endTick} \| null` | 钢琴窗的**范围选区**——在钢琴窗（音符区或参数区）Shift+拖 圈出的 tick 带，限当前 part、贯穿全音高；只有时间维（无轨道、无音高）；无选区时为 `null`。与 `trackSelection()` 独立并存，用它批量处理当前 part 里落在这段时间内的东西。 |
| `tl.playhead()` | `{tick, seconds, bar, beat, playing}` | 播放线位置（bar/beat 为 1-based）。 |
| `tl.snap(tick)` | number | 把绝对 tick 吸附到编辑器网格。 |

---

## `project` —— `tl.currentProject()`

工程数据：轨、速度、拍号。

| 成员 | 返回 | 说明 |
|---|---|---|
| `project.tracks()` | `[track]` | 所有轨道句柄。 |
| `project.addTrack(name?)` | `track` | 在末尾新建一条轨，返回其句柄。 |
| `project.removeTrack(track)` | — | 删除一条轨。 |
| `project.tempos()` | `[{bpm, tick}]` | 所有速度标记。 |
| `project.timeSignatures()` | `[{numerator, denominator, bar}]` | 所有拍号标记（bar 为 1-based 小节号）。 |
| `project.setTempo(bpm, atTick?)` | — | 设速度；`atTick` 省略则改 tick 0 的基础速度，该处已有标记则改、否则新增。 |
| `project.setTimeSignature(numerator, denominator, atBar?)` | — | 设拍号；`atBar` 为 1-based 小节号（默认 1）。 |

---

## `track`（轨）

**字段**（裸属性，可读写）：`name`、`isMute`、`isSolo`、`gain`（单位 dB，0 = 原始电平）、`pan`（[-1, 1]）。

| 方法 | 返回 | 说明 |
|---|---|---|
| `track.parts()` | `[part]` | 本轨所有 part 句柄（按起点排序）。 |
| `track.addPart({startPos, endPos, name?})` | `part` | 在本轨新建空 MIDI part（`startPos`/`endPos` 为绝对 tick 的可见起止），返回其句柄。 |
| `track.removePart(part)` | — | 从本轨删除一个 part。 |
| `track.set({name?, isMute?, isSolo?, gain?, pan?})` | — | 一次改多个字段；`pan` 钳到 [-1, 1]。 |

---

## `part`

**字段**（裸属性，可读写）：`name`、`startPos`、`endPos`（part 可见窗口的绝对 tick 起止）；**只读**：`type`（`"midi"`/`"audio"`）。写 `startPos` = 平移整段（内容跟随、长度不变），写 `endPos` = 缩放右边缘。

| 方法 | 返回 | 说明 |
|---|---|---|
| `part.soundSource()` | `{type, id, name, kind, defaultLyric}` | 本 part 的声源信息（只读快照）；`kind` 为 `"voice"` 或 `"instrument"`。仅 MIDI part。 |
| `part.notes()` | `[note]` | 本 MIDI part 的所有音符句柄。 |
| `part.selectedNotes()` | `[note]` | 钢琴窗中当前选中的音符（无选中返回空数组）。 |
| `part.notesInRange(startTick, endTick)` | `[note]` | 绝对 tick `[start, end)` 内（按起点判定）的音符。 |
| `part.addNote({pos, dur, pitch, lyric?})` | `note` | 新增音符（pos 绝对 tick，pitch 为 MIDI），返回其句柄。 |
| `part.removeNote(note)` | — | 从本 part 删除一个音符。 |
| `part.samplePitch(startTick, endTick, samples)` | `[number]` | 在区间上等距采样最终音高曲线（MIDI 标度）。 |
| `part.setPitchLine(startTick, endTick, points)` | — | 清空 `[start, end)` 再落一条音高线；`points = [{tick, value}]`，value 为绝对 MIDI 音高（可含小数）。 |
| `part.clearPitch(startTick, endTick)` | — | 清空一段音高曲线。 |
| `part.automationIds()` | `[string]` | voice 声明的可编辑自动化轨 id（如 `"Volume"`；不含 pitch）。 |
| `part.sampleAutomation(id, startTick, endTick, samples)` | `[number]` | 在区间上等距采样某自动化曲线；`NaN` 表示该处无曲线。 |
| `part.setAutomation(id, startTick, endTick, points, defaultValue?)` | — | 清空再落一条自动化曲线；value 为参数绝对值；轨不存在按需创建，`defaultValue` 可选。 |
| `part.clearAutomation(id, startTick, endTick)` | — | 清空一段自动化曲线。 |
| `part.vibratos()` | `[vibrato]` | 本 part 的所有颤音句柄。 |
| `part.addVibrato({pos, dur, frequency?, amplitude?, phase?, attack?, release?})` | `vibrato` | 新增颤音（叠加在音高曲线之上，默认 6Hz / 1 半音），返回其句柄。 |
| `part.removeVibrato(vibrato)` | — | 从本 part 删除一个颤音。 |
| `part.set({name?, startPos?, endPos?})` | — | 一次改多个字段（改名/移动/缩放）。 |

---

## `note`（音符）

**字段**（裸属性，可读写）：`pos`、`dur`、`pitch`、`lyric`；**只读**：`pitchName`（如 `"C4"`）。

| 方法 | 返回 | 说明 |
|---|---|---|
| `note.set({pos?, dur?, pitch?, lyric?})` | — | 一次改多个字段（改 pos/dur 只重排一次）。 |

---

## `vibrato`（颤音）

**字段**（裸属性，可读写）：`pos`、`dur`（绝对 tick），`frequency`（Hz）、`amplitude`（半音）、`phase`、`attack`、`release`（秒）。

| 方法 | 返回 | 说明 |
|---|---|---|
| `vibrato.set({pos?, dur?, frequency?, amplitude?, phase?, attack?, release?})` | — | 一次改多个字段。 |

---

## 示例

**把当前 part 所有音符升八度，并在每个音符上方加一个三度和声：**
```js
const part = tl.currentPart();
for (const n of part.notes()) {
  part.addNote({ pos: n.pos, dur: n.dur, pitch: n.pitch + 4, lyric: n.lyric }); // 三度和声
  n.pitch += 12;                                                                 // 原音升八度
}
print("处理了 " + part.notes().length + " 个音符");
```

**只对选中的音符操作（把选中音符时长翻倍）：**
```js
const part = tl.currentPart();
for (const n of part.selectedNotes()) n.dur *= 2;
```

**复制第一轨为新的一轨，整体升八度：**
```js
const project = tl.currentProject();
const src = project.tracks()[0];
const dst = project.addTrack("Harmony +8");
for (const p of src.parts()) {
  const np = dst.addPart({ startPos: p.startPos, endPos: p.endPos, name: p.name });
  for (const n of p.notes())
    np.addNote({ pos: n.pos, dur: n.dur, pitch: n.pitch + 12, lyric: n.lyric });
}
```

**在一段范围内画一条音量渐强曲线：**
```js
const part = tl.currentPart();
const a = 0, b = 4 * 4 * tl.ppq; // 前 4 小节（4/4）
part.setAutomation("Volume", a, b, [{tick: a, value: 0.2}, {tick: b, value: 1.0}]);
```

**删除所有低于 C2 的音符：**
```js
const part = tl.currentPart();
for (const n of part.notes()) if (n.pitch < 36) part.removeNote(n);
```

---

## 注意事项

- **句柄不可写死、不可跨运行。** 永远当场取、立即用。
- **集合方法返回数组，不是链表。** 用 `for-of` / 下标；没有 `.first` / `.next`。每次调用都是一份新快照——要复用就先存进变量。
- **创建和删除都走父对象**（`track.addPart`/`removePart`、`part.addNote`/`removeNote` …）——没有 `x.remove()`。
- **改 `pos`/`dur` 可能改变排序。** part/音符/颤音都按起点维持有序——句柄寻址不受影响（仍指向同一对象），但若你同时在迭代该集合，注意你拿的是迭代开始时的数组快照。
- **音符必须在 MIDI part 里。** 从零写旋律时，先 `tl.currentProject().addTrack()`（或选一条轨）、`track.addPart({...})` 建容器，再往它里面 `part.addNote`。
- **出错处理。** 脚本抛错会把信息返回（语法/类型错误通常带行号），并**回退它所做的全部改动**——工程保持不变，定位后改脚本重跑。
