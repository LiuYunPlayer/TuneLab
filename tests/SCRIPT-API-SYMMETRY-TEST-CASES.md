# 脚本 API 对称性整改 —— 测试用例

覆盖 `docs/script-api-symmetry.md` 那一轮整改的**受影响范围**：info 层（`getInfo()` / `addX(info)`）、
游离句柄与跨轨迁移、part 三元组几何、新补的字段与方法、删掉的糖。既有基线测试文档不受影响、无需重跑。

**跑法**：打开 TuneLab → 右侧栏 **Script** → **Code** 面粘贴 → Run（或 `Ctrl+Enter`），看输出区。
每个用例都会 `print` 出判定所需的信息；**每跑完一个用例按 `Ctrl+Z`** 回到干净状态（整段脚本 = 一次撤销）。

**前置**：
- **基础**（用例 1–5、9–12、12b、17–19）：一个**非空工程**——至少一条轨、轨上至少一个 midi part，part 里有几个音符。
- 用例 6（跨轨迁移）需要**至少两条轨**、且第一条轨上有 part；不够就先手动加一条空轨。
- 用例 8 需要**至少两个 midi part**（同轨或跨轨都行）；不够时用例会自己打印"跳过"。
- 用例 13、16（颤音影响表 / 音素）需要 part 挂了一个 **voice 音源**；音素列表的初始内容要**合成过**才有
  （空列表不影响判定——用例 16 判的是新加的两个音素进对了列表）。
- 用例 14 需要该 part 的 effect 链上**已有一个 effect**；空链时用例会打印"跳过"。
- 用例 15（分段自动化轨）需要音源声明了分段轨；**若 `piecewiseAutomationIds()` 返回空数组就跳过该用例**（不算失败）。

---

## A. info 层：完整复制

### 用例 1 —— `note.getInfo()` 往返保真

```js
const part = tl.currentPart();
const n = part.notes()[0];
const before = n.getInfo();
const copy = part.addNote(before);
const after = copy.getInfo();
print("原:   " + JSON.stringify(before));
print("副本: " + JSON.stringify(after));
print("pos/dur/pitch/lyric 一致: " +
  (before.pos === after.pos && before.dur === after.dur &&
   before.pitch === after.pitch && before.lyric === after.lyric));
```

**期望**：两行 JSON **逐字段相同**（含 `pronunciation`、`properties`、`leadingPhonemes`、`bodyPhonemes`、`bodyOffset`），
最后一行 `true`。钢琴窗里出现一个与原音符完全重叠的新音符。

### 用例 2 —— `part.getInfo()` 是纯数据：改它不动工程

```js
const part = tl.currentPart();
const info = part.getInfo();
const posBefore = part.pos;
info.pos += 4 * tl.ppq;          // 只改这份快照
info.name = "info 改过的名字";
print("改 info 后 part.pos 仍是 " + part.pos + "（应等于 " + posBefore + "）");
print("改 info 后 part.name 仍是 \"" + part.name + "\"");
print("info 里的 pos = " + info.pos + "、name = \"" + info.name + "\"");
```

**期望**：`part.pos` / `part.name` **完全没变**（info 是纯数据、不进撤销栈、无副作用），而 info 里是改过的值。
运行后应显示"无改动"（本用例一次写操作都没有，**无需 Ctrl+Z**）。

### 用例 3 —— 整段复制一个 part（全维度保真）

```js
const src = tl.currentPart();
const info = src.getInfo();
info.name = src.name + " 副本";
const copy = src.track().addPart(info);   // part.track() = 向上取所属轨
copy.pos += copy.dur;                     // 平移【在句柄上做】：内容跟随。改 info.pos 只挪窗口、内容不跟随
print("源:   音符 " + src.notes().length + " 个、effect " + src.effects().length +
      " 个、颤音 " + src.vibratos().length + " 个、音源 " + JSON.stringify(src.soundSource()));
print("副本: 音符 " + copy.notes().length + " 个、effect " + copy.effects().length +
      " 个、颤音 " + copy.vibratos().length + " 个、音源 " + JSON.stringify(copy.soundSource()));
print("副本几何: pos=" + copy.pos + " startOffset=" + copy.startOffset + " endOffset=" + copy.endOffset);
```

**期望**：两行统计**完全一致**（音符数、effect 数、颤音数、音源 type/id/kind 都一样）。编排区里出现一个紧接
源 part 之后、内容相同的新 part；打开它应看到同样的音符、音高线、自动化曲线、音素。

### 用例 4 —— 整轨复制

```js
const project = tl.currentProject();
const info = project.tracks()[0].getInfo();
info.name = "整轨副本";
const dst = project.addTrack(info);
const src = project.tracks()[0];
print("源轨 part 数 " + src.parts().length + " / 副本 " + dst.parts().length);
print("源轨 gain/pan/asRefer/color: " + src.gain + " / " + src.pan + " / " + src.asRefer + " / \"" + src.color + "\"");
print("副本 gain/pan/asRefer/color: " + dst.gain + " / " + dst.pan + " / " + dst.asRefer + " / \"" + dst.color + "\"");
print("副本名: \"" + dst.name + "\"");
```

**期望**：part 数相同；`gain/pan/asRefer/color` 四项**逐一相同**；副本名为「整轨副本」，位于轨列表末尾。

### 用例 5 —— `addTrack` 的 index 定位

```js
const project = tl.currentProject();
const before = project.tracks().map(t => t.name);
project.addTrack({ name: "插到最前" }, 0);
print("原顺序: " + JSON.stringify(before));
print("现顺序: " + JSON.stringify(project.tracks().map(t => t.name)));
```

**期望**：新轨出现在**第 0 位**（列表最上方），其余轨顺序不变。

---

## B. 游离句柄与迁移

### 用例 6 —— 跨轨迁移 part（移动，不是复制）

> 需要工程里**至少两条轨**，第一条轨上有 part。不够就先手动加一条空轨。

```js
const [a, b] = tl.currentProject().tracks();
const p = a.parts()[0];
const noteCount = p.notes().length;
const src = JSON.stringify(p.soundSource());
print("迁移前: a 轨 " + a.parts().length + " 个 part、b 轨 " + b.parts().length + " 个");
a.removePart(p);
print("摘出后: a 轨 " + a.parts().length + " 个 part（应少一个）");
print("游离期仍可读: 音符 " + p.notes().length + " 个、音源 " + JSON.stringify(p.soundSource()));
b.insertPart(p);
print("迁移后: a 轨 " + a.parts().length + "、b 轨 " + b.parts().length + " 个 part");
print("内容没丢: 音符 " + p.notes().length + "/" + noteCount +
      "、音源一致 " + (JSON.stringify(p.soundSource()) === src));
```

**期望**：part 从 a 轨消失、出现在 b 轨**同一时间位置**；音符数与音源都没变；游离期的读取正常返回。
`Ctrl+Z` 一步应把它还回 a 轨。

### 用例 7 —— 游离态不可写（错误提示要指路）

```js
const [a] = tl.currentProject().tracks();
const p = a.parts()[0];
a.removePart(p);
try { p.name = "改名试试"; print("❌ 不该走到这里"); }
catch (e) { print("✅ 报错: " + e.message); }
try { p.pos = 0; print("❌ 不该走到这里"); }
catch (e) { print("✅ 报错: " + e.message); }
print("但读取正常: name=\"" + p.name + "\" pos=" + p.pos);
a.insertPart(p);
p.name = "插回后可写了";
print("插回后写成功: \"" + p.name + "\"");
```

**期望**：两次写入都抛错，消息里含 `detached` 与 `track.insertPart(part)` 的指路；读取正常；插回后可写。

### 用例 8 —— 不能跨 part 搬 note（数据层不支持，要给明确错误）

```js
const project = tl.currentProject();
const parts = project.tracks().flatMap(t => t.parts()).filter(p => p.type === "midi");
if (parts.length < 2) { print("跳过：需要至少两个 midi part"); }
else {
  const [p1, p2] = parts;
  const n = p1.notes()[0];
  p1.removeNote(n);
  try { p2.insertNote(n); print("❌ 不该走到这里"); }
  catch (e) { print("✅ 报错: " + e.message); }
  p1.insertNote(n);   // 插回原 part 是允许的
  print("插回原 part 成功，音符数 " + p1.notes().length);
}
```

**期望**：`insertNote` 到别的 part 抛错，消息指路 `otherPart.addNote(note.getInfo())`；插回原 part 成功。

---

## C. part 三元组几何

### 用例 9 —— `pos` 平移整段、内容跟随；`startPos/endPos/dur` 只读派生

```js
const p = tl.currentPart();
const notesBefore = p.notes().map(n => n.pos);
print("移前: pos=" + p.pos + " startOffset=" + p.startOffset + " endOffset=" + p.endOffset +
      " → startPos=" + p.startPos + " endPos=" + p.endPos + " dur=" + p.dur);
p.pos += 4 * tl.ppq;                       // 后移一小节（4/4）
const notesAfter = p.notes().map(n => n.pos);
print("移后: pos=" + p.pos + " startPos=" + p.startPos + " endPos=" + p.endPos + " dur=" + p.dur);
print("dur 没变: " + (p.dur === (p.endOffset - p.startOffset)));
print("音符跟着平移了同样的量: " +
  notesBefore.every((v, i) => Math.abs((notesAfter[i] - v) - 4 * tl.ppq) < 1e-6));
```

**期望**：`pos`/`startPos`/`endPos` 各 +1920，`dur` 不变；**每个音符的绝对 pos 都跟着 +1920**（内容以锚点为原点）；
编排区里整段 part 连内容一起右移一小节。

### 用例 10 —— 左右边缘裁剪（`startOffset` 是原先完全缺失的能力）

```js
const p = tl.currentPart();
print("裁前: startPos=" + p.startPos + " endPos=" + p.endPos + " dur=" + p.dur);
p.startOffset += tl.ppq;      // 拖左边缘：前向裁掉一拍
p.endOffset   -= tl.ppq;      // 拖右边缘：后端裁掉一拍
print("裁后: startPos=" + p.startPos + " endPos=" + p.endPos + " dur=" + p.dur);
print("pos（锚点）没动: " + p.pos);
try { p.dur = 100; print("❌ dur 竟然可写"); }
catch (e) { print("✅ dur 只读: " + e.message); }
```

**期望**：`startPos` +480、`endPos` −480、`dur` −960，**`pos` 不变**；编排区里 part 两端各缩进一拍、**内容不动**
（音符还在原来的时间上，只是两端被裁出可见范围）。给 `dur` 赋值应失败。

---

## D. 新补的成员

### 用例 11 —— track 的 `asRefer` / `color`，part 的 `gain`

```js
const t = tl.currentProject().tracks()[0];
const p = t.parts()[0];
print("改前: asRefer=" + t.asRefer + " color=\"" + t.color + "\"" + (p.type === "midi" ? " part.gain=" + p.gain : ""));
t.asRefer = !t.asRefer;
t.color = "#FF8800";
if (p.type === "midi") p.gain = -3;
print("改后: asRefer=" + t.asRefer + " color=\"" + t.color + "\"" + (p.type === "midi" ? " part.gain=" + p.gain : ""));
```

**期望**：三个值都改成功；**轨头颜色立刻变成橙色**（`#FF8800`）；`Ctrl+Z` 一步全部还原（含颜色）。

### 用例 12 —— 速度 / 拍号标记的删除

```js
const project = tl.currentProject();
project.setTempo(140, 4 * tl.ppq);          // 在第 2 小节加一个变速点
print("加后: " + JSON.stringify(project.tempos()));
project.removeTempo(4 * tl.ppq);
print("删后: " + JSON.stringify(project.tempos()));
try { project.removeTempo(4 * tl.ppq); print("❌ 不该走到这里"); }
catch (e) { print("✅ 该处已无标记，报错: " + e.message); }
try { project.removeTempo(0); print("❌ 基准速度竟然删掉了"); }
catch (e) { print("✅ 首个标记不可删: " + e.message); }
```

**期望**：加了又删回原样；删不存在的标记**报错**（不是静默 no-op）；删 tick 0 的基准速度报错并提示用 `setTempo`。
时间轴上应看到变速点出现又消失。

### 用例 12b —— 导出设置：一键设成预设（并验证它**不**入撤销栈）

> 这一族是"用户会要的可复用命令"的代表：跑一次就把导出各项设成自己的预设。它们是**设置项**，
> 故成功跑完后 `Ctrl+Z` **不会**把它们退回去（与在导出侧栏里改一样）——但脚本**出错**时会还原（见下半）。

```js
const p = tl.currentProject();
const t = p.tracks()[0];
print("改前: " + [p.exportFormat, p.exportSampleRate, p.exportBitDepth, p.masterExportChannels,
                 t.exportEnabled, t.exportChannels].join(" | "));
p.exportPath = "D:/renders";
p.exportFileName = "my_take";
p.exportFormat = "flac";
p.exportSampleRate = 96000;
p.exportBitDepth = 24;
p.masterExportEnabled = true;
p.masterExportChannels = 2;
t.exportEnabled = true;
t.exportChannels = 2;
print("改后: " + [p.exportPath, p.exportFileName, p.exportFormat, p.exportSampleRate, p.exportBitDepth,
                 p.masterExportEnabled, p.masterExportChannels, t.exportEnabled, t.exportChannels].join(" | "));
try { p.exportFormat = "aiff"; print("❌ 未知格式竟然被接受"); }
catch (e) { print("✅ 未知格式报错: " + e.message); }
try { t.exportChannels = 5; print("❌ 声道数 5 竟然被接受"); }
catch (e) { print("✅ 声道数校验: " + e.message); }
```

**期望**：
- 打开右侧栏 **Export** 面，应看到路径/文件名/格式/采样率/位深、母线与该轨的勾选与声道数**全部同步成新值**（FLAC 时"位深"可见、"码率"隐藏）。
- 两处非法值都报错。
- **按 `Ctrl+Z`：导出设置不回退**（这是刻意的——撤销栈只装工程数据）。

再跑一次这段验证"出错会还原"：

```js
const p = tl.currentProject();
print("改前 format=" + p.exportFormat + " path=\"" + p.exportPath + "\"");
p.exportFormat = "mp3";
p.exportPath = "D:/should_not_persist";
throw new Error("故意抛错");
```

**期望**：报错回灌，且 Export 面里的格式与路径**仍是改前的值**——非撤销字段由脚本运行器写前留底、回退时还原。

### 用例 13 —— 颤音影响表

> 需要 part 上有颤音；没有就先在钢琴窗画一个，或先跑 `part.addVibrato({pos: 0, dur: 480})`。

```js
const p = tl.currentPart();
const v = p.vibratos()[0] || p.addVibrato({ pos: p.startPos, dur: tl.ppq });
print("音源级影响表（初始）: " + JSON.stringify(v.affectedAutomations()));
const ids = p.automationIds();
if (ids.length === 0) { print("跳过：该音源没声明连续自动化轨"); }
else {
  v.setAmplitude(ids[0], 0.5);
  print("设 " + ids[0] + " = 0.5 后: " + JSON.stringify(v.affectedAutomations()));
  v.removeAmplitude(ids[0]);
  print("解除关联后: " + JSON.stringify(v.affectedAutomations()));
}
print("effect 级影响表: " + JSON.stringify(v.affectedEffectAutomations()));
print("颤音 info: " + JSON.stringify(v.getInfo()));
```

**期望**：`setAmplitude` 后表里出现 `{"<轨id>": 0.5}`，`removeAmplitude` 后该键消失；`getInfo()` 里带着两张表。

### 用例 14 —— effect：info 创建 + index 定位 + 摘出插回

```js
const p = tl.currentPart();
const names = () => p.effects().map(e => e.name + "@" + e.index).join(", ");
print("链（初始）: [" + names() + "]");
const first = p.effects()[0];
if (!first) { print("跳过：链上还没有 effect，请先在 UI 里加一个"); }
else {
  const copy = p.addEffect(first.getInfo(), 0);          // 复制一份插到链首
  print("复制并插到链首后: [" + names() + "]");
  print("副本 id 与源不同（同链内 id 必须唯一）: " + (copy.id !== first.id));
  p.removeEffect(copy);
  print("摘出后: [" + names() + "]");
  print("游离期仍可读: type=" + copy.type + " enabled=" + copy.isEnabled);
  p.insertEffect(copy, p.effects().length);             // 插回链尾
  print("插回链尾后: [" + names() + "]");
}
```

**期望**：副本插在 index 0；`copy.id !== first.id`（宿主重新发号）；摘出后链短一个、游离期可读；插回后在链尾。
参数栏 / effect 面板应同步反映链的变化。

### 用例 15 —— 分段自动化轨（**若 ids 为空则跳过**）

```js
const p = tl.currentPart();
const ids = p.piecewiseAutomationIds();
print("分段轨 ids: " + JSON.stringify(ids));
print("连续轨 ids: " + JSON.stringify(p.automationIds()));
if (ids.length === 0) { print("跳过：该音源没声明分段轨"); }
else {
  const a = p.startPos, b = a + 2 * tl.ppq;
  p.setPiecewiseAutomationLine(ids[0], a, b, [{tick: a, value: 0}, {tick: b, value: 1}]);
  print("落线后采样: " + JSON.stringify(p.samplePiecewiseAutomation(ids[0], a, b, 5)));
  p.clearPiecewiseAutomation(ids[0], a, b);
  print("清空后采样: " + JSON.stringify(p.samplePiecewiseAutomation(ids[0], a, b, 5)));
}
```

**期望**：两张 id 表**不重叠**（这是整改的重点：以前 `automationIds()` 会把分段轨也列进来、取到却用不了）；
落线后采样是 0→1 的斜坡，清空后回到无值。

### 用例 16 —— 音素双列表拆分

```js
const p = tl.currentPart();
const n = p.notes()[0];
print("钉死状态: " + n.hasLockedPhonemes + "、音素 " + n.phonemes().length + " 个");
print("音素: " + JSON.stringify(n.phonemes().map(ph => ({ symbol: ph.symbol, leading: ph.leading }))));
const lead = n.addLeadingPhoneme({ symbol: "t", duration: 0.05 });
const body = n.addBodyPhoneme({ symbol: "a", stretchWeight: 1 });
print("加了引导 t + 主体 a 后:");
print("  " + JSON.stringify(n.phonemes().map(ph => ({ symbol: ph.symbol, leading: ph.leading }))));
print("  新引导音素 info: " + JSON.stringify(lead.getInfo()));
print("  钉死状态（写入应自动钉死）: " + n.hasLockedPhonemes);
```

**期望**：`t` 进 **leading**（`leading: true`）、`a` 进 **body**（`leading: false`）；写入后 `hasLockedPhonemes`
变 `true`；音素带 / 侧栏音素面板应显示新加的两个音素。**没有 `addPhoneme` 这个方法了**（见用例 17）。

---

## E. 删掉的糖：确认已不存在

### 用例 17 —— 六个入口都应报「不是函数」

```js
const project = tl.currentProject();
const t = project.tracks()[0];
const p = tl.currentPart();
const n = p.notes()[0];
const v = p.vibratos()[0];
const checks = [
  ["track.set",        () => t.set({ name: "x" })],
  ["part.set",         () => p.set({ name: "x" })],
  ["note.set",         () => n.set({ pitch: 60 })],
  ["part.notesInRange",() => p.notesInRange(0, 480)],
  ["part.duplicate",   () => p.duplicate()],
  ["track.duplicate",  () => t.duplicate()],
  ["note.addPhoneme",  () => n.addPhoneme({ symbol: "a" })],
];
if (v) checks.push(["vibrato.set", () => v.set({ dur: 480 })]);
for (const [name, fn] of checks) {
  try { fn(); print("❌ " + name + " 竟然还在"); }
  catch (e) { print("✅ " + name + " 已删: " + e.message); }
}
```

**期望**：每一行都是 ✅，错误信息形如 `... is not a function`。**运行后应显示"无改动"**——一次写操作都没发生
（无需 `Ctrl+Z`）。

---

## F. 回归：既有能力没被打断

### 用例 18 —— 原子回退仍然成立

```js
const p = tl.currentPart();
print("音符数（跑之前）: " + p.notes().length);
for (const n of p.notes()) { const i = n.getInfo(); i.pitch += 4; p.addNote(i); }
throw new Error("故意抛错，检验原子回退");
```

**期望**：报错回灌，且**工程完全没变**——重新看钢琴窗，音符数与音高都还是原样（没有多出的三度和声）。

### 用例 19 —— 夹具脚本仍可跑

把 `tests/fixtures/scripts/05-build-c-major-scale.js` 粘进 Code 面 Run（**空工程也能跑**）。

**期望**：新建一条名为 `C Major Scale` 的轨，其上一个名为 `scale` 的 part 覆盖 `0..8×ppq`，里面 8 个音符
（C D E F G A B C，歌词 do re mi fa sol la si do）。这个脚本已随整改改用 `addTrack({name})` +
`addPart({pos, endOffset})`。

---

## 判定汇总

| # | 用例 | 结果 |
|---|---|---|
| 1 | note info 往返保真 | |
| 2 | info 是纯数据、改它不动工程 | |
| 3 | 整段复制 part（全维度） | |
| 4 | 整轨复制（含 asRefer/color） | |
| 5 | addTrack 的 index 定位 | |
| 6 | 跨轨迁移 part | |
| 7 | 游离态不可写 + 指路 | |
| 8 | 跨 part 搬 note 被拒 | |
| 9 | pos 平移整段、内容跟随 | |
| 10 | startOffset/endOffset 裁剪、派生量只读 | |
| 11 | asRefer / color / part.gain | |
| 12 | 速度标记删除（含两种拒绝） | |
| 12b | 导出设置一键设成预设 + 校验 + 不入撤销栈 + 出错还原 | |
| 13 | 颤音影响表 | |
| 14 | effect info + index + 摘出插回 | |
| 15 | 分段轨与连续轨 id 分表 | |
| 16 | 音素双列表拆分 | |
| 17 | 六个糖入口已删 | |
| 18 | 原子回退 | |
| 19 | 夹具脚本 05 | |
