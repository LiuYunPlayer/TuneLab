# TuneLab 脚本 API 手册

TuneLab 内置一个 **JavaScript 脚本引擎**，让你用一小段代码读取并编辑当前工程——尤其适合**批量、带循环/条件、需要计算**的编辑（"5–8 小节每个音符升八度再加三度和声" = 一个循环，胜过几十次手动操作）。

脚本能力是一个**独立模块**，有两个入口：

- **菜单 `Script → Run Script`**：弹出窗口，输入脚本、点 **Run**（或 Ctrl+Enter）运行。
- **内置 AI Agent 的 `run_script` 工具**：模型自动写脚本调用同一套 API。

两个入口共享同一个动作面（全局对象 `tl`）。

---

## 核心模型（先读这一节）

- **整段脚本运行 = 一个可撤销单位。** 脚本里发生的所有改动合并成一次提交，`Ctrl+Z` 一步全部回退。你**不需要**（也无法）自己调用 commit/保存。
- **句柄寻址。** 轨/part/音符都通过**句柄**引用——句柄是对一个对象的不透明引用，带可读数据字段（如 `note.pos`、`note.pitch`），但没有 id。
  - 所有读方法返回的是**普通 JavaScript 数组**，用 `for-of` 或下标遍历，有 `.length`。**它不是链表**，没有 `.first` / `.next` / `.head`。
  - 要改一个对象，把它的句柄传回 `tl.*` 的写方法（如 `tl.setNote(note, {...})`）。
  - 句柄**仅当次运行有效**（对象无持久 id，关闭软件即失效）：脚本里**绝不要写死一个句柄值**，永远是「当场 get、立即用」。删除后的句柄再用会报错。
- **坐标 = 绝对（全局）tick。** 所有位置/时长都是绝对 tick（`tl.ppq` 取每四分音符的 tick 数，默认 480）——与播放线、小节同一坐标系。你**永不做坐标换算**、不用减 part 起点。
- **音高 = MIDI 数字**（60 = C4，可含小数表示音分）。
- **出错也会落地已发生的改动。** 脚本中途抛错时，出错前已发生的改动仍作为一个可撤销单位落地，并把错误信息返回——便于定位后继续或撤销。
- **沙箱。** 引擎不暴露任何 .NET/文件/网络能力（只有标准 ECMAScript）。限制：递归深度 64、语句数 5,000,000、运行 5 秒、内存 64MB（防死循环/失控）。
- **调试输出。** `print(x)` / `console.log(x)` 的输出会被收集并显示（窗口里在结果区，agent 那边回灌给模型）。

---

## 全局对象 `tl`

### 读

| 方法 | 返回 | 说明 |
|---|---|---|
| `tl.ppq` | number | 每四分音符的 tick 数（默认 480）。 |
| `tl.tracks()` | `[track]` | 所有轨道句柄。 |
| `tl.parts(track)` | `[part]` | 某轨的所有 part 句柄（按起点排序）。 |
| `tl.notes(part)` | `[note]` | 某 MIDI part 的所有音符句柄。 |
| `tl.notesInRange(part, startTick, endTick)` | `[note]` | 绝对 tick `[start, end)` 内（按起点判定）的音符。 |
| `tl.currentPart()` | `part \| null` | 钢琴窗当前打开编辑的 MIDI part。 |
| `tl.selectedNotes(part)` | `[note]` | 该 part 内当前被选中的音符（无选中返回空数组）。 |
| `tl.playhead()` | `{tick, seconds, bar, beat, playing}` | 播放线位置（bar/beat 为 1-based）。 |
| `tl.snap(tick)` | number | 把绝对 tick 吸附到钢琴窗当前量化网格。 |
| `tl.tempos()` | `[{bpm, tick}]` | 所有速度标记。 |
| `tl.timeSignatures()` | `[{numerator, denominator, bar}]` | 所有拍号标记（bar 为 1-based 小节号）。 |
| `tl.parameterIds(part)` | `[string]` | 该 part 可编辑的参数 id（含 `"pitch"` 与各自动化轨）。 |
| `tl.sampleParameter(part, id, startTick, endTick, samples)` | `[number]` | 在区间上等距采样某参数曲线；`NaN` 表示该处无曲线。 |

### 轨道（写）

| 方法 | 返回 | 说明 |
|---|---|---|
| `tl.addTrack(name?)` | `track` | 在末尾新建一条轨，返回其句柄。 |
| `tl.removeTrack(track)` | — | 删除一条轨。 |
| `tl.setTrack(track, {name?, mute?, solo?, gainDb?, pan?})` | — | 只改所给字段；`pan` 钳到 [-1, 1]。 |

### Part（写）

| 方法 | 返回 | 说明 |
|---|---|---|
| `tl.addPart(track, {pos, dur, name?})` | `part` | 在某轨新建空 MIDI part（绝对 tick），返回其句柄。 |
| `tl.removePart(part)` | — | 删除一个 part。 |
| `tl.setPart(part, {name?, pos?, dur?})` | — | 改名/移动(pos)/缩放(dur)；只改所给字段。 |

### 音符（写）

| 方法 | 返回 | 说明 |
|---|---|---|
| `tl.addNote(part, {pos, dur, pitch, lyric?})` | `note` | 新增音符（pos 绝对 tick，pitch 为 MIDI），返回其句柄。 |
| `tl.setNote(note, {pitch?, pos?, dur?, lyric?})` | — | 只改所给字段。 |
| `tl.removeNote(note)` | — | 删除一个音符。 |

### 曲线（写）

| 方法 | 返回 | 说明 |
|---|---|---|
| `tl.setPitchLine(part, startTick, endTick, points)` | — | 清空 `[start, end)` 再落一条音高线；`points = [{tick, value}]`，`value` 为绝对 MIDI 音高（可含小数）。 |
| `tl.clearPitch(part, startTick, endTick)` | — | 清空一段音高曲线。 |
| `tl.setAutomation(part, id, startTick, endTick, points, defaultValue?)` | — | 清空再落一条自动化曲线；`value` 为参数绝对值；轨不存在按需创建。 |
| `tl.clearAutomation(part, id, startTick, endTick)` | — | 清空一段自动化曲线。 |
| `tl.addVibrato(part, {start, end, frequency?, amplitude?})` | — | 在 `[start, end)` 叠加颤音（叠加在音高线之上，默认 6Hz / 1 半音）。 |

### 速度 / 拍号（写）

| 方法 | 返回 | 说明 |
|---|---|---|
| `tl.setTempo(bpm, atTick?)` | — | 设速度；`atTick` 省略则改 tick 0 的基础速度，该处已有标记则改、否则新增。 |
| `tl.setTimeSignature(numerator, denominator, atBar?)` | — | 设拍号；`atBar` 为 1-based 小节号（默认 1）。 |

---

## 句柄字段（只读，实时读底层）

- **track**：`name`、`mute`、`solo`、`gainDb`、`pan`、`partCount`
- **part**：`name`、`pos`、`dur`、`isMidi`、`type`（`"midi"`/`"audio"`）、`voice`、`noteCount`
- **note**：`pos`、`dur`、`pitch`、`pitchName`（如 `"C4"`）、`lyric`

改完之后再读句柄字段即见最新值（句柄实时读底层对象）。

---

## 示例

**把当前 part 所有音符升八度，并在每个音符上方加一个三度和声：**
```js
const part = tl.currentPart();
for (const n of tl.notes(part)) {
  tl.addNote(part, { pos: n.pos, dur: n.dur, pitch: n.pitch + 4, lyric: n.lyric }); // 三度和声
  tl.setNote(n, { pitch: n.pitch + 12 });                                            // 原音升八度
}
print("processed " + tl.notes(part).length + " notes");
```

**只对用户选中的音符操作（把选中音符时长翻倍）：**
```js
const part = tl.currentPart();
for (const n of tl.selectedNotes(part)) tl.setNote(n, { dur: n.dur * 2 });
```

**复制第一轨为新的一轨，整体升八度：**
```js
const src = tl.tracks()[0];
const dst = tl.addTrack("Harmony +8");
for (const p of tl.parts(src)) {
  const np = tl.addPart(dst, { pos: p.pos, dur: p.dur, name: p.name });
  for (const n of tl.notes(p))
    tl.addNote(np, { pos: n.pos, dur: n.dur, pitch: n.pitch + 12, lyric: n.lyric });
}
```

**在一段范围内画一条音量渐强曲线：**
```js
const part = tl.currentPart();
const a = 0, b = 4 * 4 * tl.ppq; // 前 4 小节（4/4）
tl.setAutomation(part, "Volume", a, b, [{tick: a, value: 0.2}, {tick: b, value: 1.0}]);
```

---

## 注意事项

- **句柄不可写死、不可跨运行。** 永远当场 `tl.*` get 后立即用。
- **不要当链表遍历。** 读方法返回的是数组，用 `for-of` / 下标；没有 `.first` / `.next`。
- **改 `pos`/`dur` 可能改变排序。** part 按起点排序、音符也按起点维持有序——句柄寻址不受影响（仍指向同一对象），但若你同时在迭代该集合，注意你拿的是迭代开始时的数组快照。
- **音符必须在 MIDI part 里。** 从零写旋律时，先 `tl.addPart(track, {...})` 建容器，再往它里面 `tl.addNote`。
- **出错处理。** 脚本抛错会把信息返回（语法/类型错误通常带行号）；出错前的改动已作为一个可撤销单位落地，可 `Ctrl+Z` 撤销或定位后重跑。

---

> 维护者注：本手册是脚本 API 的权威说明。喂给 LLM 的精简版在 `RunScriptTool.Description`（务必与此同步，且**绝不**用"链表"等会诱导错误遍历的措辞）。实现见 `TuneLab/Scripting/`，分层/扩展说明见 `docs/agent-tools.md` 的 Layer 4 节。
