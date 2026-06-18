# 脚本

用一段 JavaScript 读写当前工程，适合批量、循环、条件、计算类编辑。整段脚本运行 = **一次**撤销（Ctrl+Z 一步回退）。全局对象 `tl` 是入口。

## 核心规则

- **句柄**：轨/part/音符用句柄引用，`tl.notes()` 等返回句柄数组，用 `for-of` 遍历。句柄当场 get、当场用，不能写死、不跨次运行。
- **坐标**：位置/时长都是绝对 tick，`tl.ppq` 是每四分音符的 tick 数。
- **音高**：MIDI 数字，60 = C4。
- **调试**：`print(x)` 把内容打到下方输出区。

## 读取

- `tl.tracks()` → 轨数组
- `tl.parts(track)` → part 数组
- `tl.notes(part)` → 音符数组
- `tl.notesInRange(part, 起, 止)` → 区间内音符
- `tl.selectedNotes(part)` → 选中的音符
- `tl.currentPart()` → 钢琴窗当前 part（或 null）
- `tl.playhead()` → 播放线 `{tick, seconds, bar, beat, playing}`
- `tl.snap(tick)` → 吸附到网格
- `tl.tempos()` / `tl.timeSignatures()`
- `tl.parameterIds(part)` → 可编辑参数 id
- `tl.sampleParameter(part, id, 起, 止, 点数)` → 采样曲线

句柄字段：`note.pos / dur / pitch / pitchName / lyric`；`part.name / pos / dur / type / voice / noteCount`；`track.name / mute / solo / gainDb / pan`。

## 写入（不用自己提交）

- 轨：`tl.addTrack(名?)`、`tl.removeTrack(track)`、`tl.setTrack(track, {name?,mute?,solo?,gainDb?,pan?})`
- part：`tl.addPart(track, {pos,dur,name?})`、`tl.removePart(part)`、`tl.setPart(part, {name?,pos?,dur?})`
- 音符：`tl.addNote(part, {pos,dur,pitch,lyric?})`、`tl.setNote(note, {pitch?,pos?,dur?,lyric?})`、`tl.removeNote(note)`
- 曲线：`tl.setPitchLine(part, 起, 止, 点)`、`tl.setAutomation(part, id, 起, 止, 点)`、`tl.addVibrato(part, {start,end,frequency?,amplitude?})`
- 速度/拍号：`tl.setTempo(bpm, atTick?)`、`tl.setTimeSignature(分子, 分母, 第几小节?)`

点的格式：`[{tick, value}]`。

## 示例

当前 part 所有音符升八度：

```js
const part = tl.currentPart();
for (const n of tl.notes(part)) {
  tl.setNote(n, { pitch: n.pitch + 12 });
}
```

## 注意

- 读取结果是普通数组，不是链表（没有 `.first/.next`）。
- 句柄删除后不要再用。
- 出错时，出错前的改动仍会作为一次撤销落地。
