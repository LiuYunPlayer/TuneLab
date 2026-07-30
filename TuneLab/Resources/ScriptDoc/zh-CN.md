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
  - 句柄**仅当次运行有效**（对象无持久 id，关闭软件即失效）：脚本里**绝不要写死一个句柄值**，永远是「当场取、立即用」。被 `removeX` 摘出后句柄仍可读（见下「移动 vs 复制」），只是不可写。
- **坐标一律绝对 tick。** 所有位置/时长都是绝对（全局）tick（`tl.ppq` 取每四分音符的 tick 数，默认 480）——与播放线、小节同一坐标系。你**永不做坐标换算**。
- **音高用 MIDI 数字**，60 = C4（可含小数表示音分）。
- **出错则全部回退。** 脚本中途抛错时，它已做的改动全部撤销（工程保持不变），并返回错误信息——定位后改脚本重跑，不要基于半成品状态打补丁。
- **调试输出。** `print(x)` / `console.log(x)` 的输出会收集并显示在下方输出区。

### info 对象 —— 完整复制，以及带任意字段创建

每个句柄都有 `getInfo()`，返回一个**普通 JS 对象**，装着该对象的**全部信息**（而且是嵌套的：一个 part 的 info 里带着它的音源、音符、音高线、各条自动化曲线、颤音、effect 链、两级属性、音素）。每个父对象的 `addX(info)` 收的正是同一个形状。于是：

- **复制**（全维度保真，一个字段都不丢）：`track.addPart(其它part.getInfo())`、`project.addTrack(其它轨.getInfo())`、`part.addNote(n.getInfo())`
- **创建**（想填哪个字段填哪个）：`part.addNote({pos, dur, pitch, lyric, pronunciation, properties, bodyPhonemes})`

info 是**纯数据**：加进去之前随便改（不进撤销栈、无副作用），同一份 info 也可以反复 `addX` 多次（每次都产生一个**新对象**）。

> ⛔ **绝不要为了"复制"而逐字段手搬**——那样会静默丢掉音源、曲线、effect、属性和音素，看着像成功了，实际只剩骨架。要复制就 `getInfo()` → `addX()`。

info 里没写的字段用**存储默认值**（例如 `name` 是空串），不会替你臆造一个。

> ⚠️ **要把副本放到别处，改句柄、不要改 info**：
> ```js
> const copy = track.addPart(p.getInfo());
> copy.pos += 4 * tl.ppq;      // ✅ 平移整段，内容跟随
> ```
> info 里的 tick **一律绝对**，嵌在 part info 里的音符与曲线点也是。所以只把 `info.pos` 加上一个量，
> 挪动的只是 part 的**窗口**、内容仍停在原来的绝对位置（结果是内容落到窗口外面去了）。
> 「移动整段、内容跟随」是**句柄**上 `part.pos` 的语义，不是改 info 能得到的。

### 移动 vs 复制 —— `removeX` 返回**游离**句柄

`removeX(子对象)` 只是把它从父容器**摘出**，并把句柄**返回**给你；对象仍然活着、仍可读（`getInfo()` 照样用），只是暂时没有父。

- **删除** = 摘出后不插回。
- **移动** = 摘出后 `insertX(子对象)` —— 还是**同一个对象**，所以它身上的音符/曲线/effect/音素跟着一起走，撤销栈里也只是一次移动。

游离句柄是**只读**的：给它的字段赋值会报错，并提示你先插回。

只有 `removeX` 交回东西（同 DOM 的 `parent.removeChild(child)`），故"移动"是一个表达式：`b.insertPart(a.removePart(p))`。`insertX` 不返回——把你刚传进去的句柄回声给你，不带任何信息。

把**不属于该父**的子对象拿去删会**抛错**——那是编程错误、不是查询，所以没有"它原本在不在"这种布尔结果（`Set.delete` 那类返回 `bool` 是因为值型集合里"不存在"是正常结果；父子归属不是）。

只有 **part 能换父**：`track.removePart(p)` 再 `另一条轨.insertPart(p)` 就是**跨轨迁移**。note / 颤音 / effect 归属它被创建时的那个 part，`insertX` 只能把它插回原 part；要弄到别的 part 上请走 info 路：`另一个part.addX(x.getInfo())`。

---

## `tl`（编辑器）

编辑器级的入口——系统常量、当前工程、以及编辑器的临时状态。

| 成员 | 返回 | 说明 |
|---|---|---|
| `tl.ppq` | number | 每四分音符的 tick 数（默认 480）。 |
| `tl.language` | string | 当前界面语言文化码（如 `"zh-CN"` / `"en-US"`）。用来在 `getScriptInfo` 里产出本地化的工具名、或在动作里本地化对话框文案；与工程无关，没打开工程时也能读。 |
| `tl.currentProject()` | `project` | 当前工程（你的数据入口，见下）。 |
| `tl.currentPart()` | `part \| null` | 钢琴窗当前打开编辑的 MIDI part。 |
| `tl.selectedParts()` | `[part]` | 编排区当前选中的 part（跨全部轨道、支持多选）；无选中返回空数组。右键某个 part 时它必被选中，故这是 `part` / `partContent` 类工具脚本的目标入口。 |
| `tl.selectedTracks()` | `[track]` | 当前选中的轨（支持多选）；无选中返回空数组。右键轨道头或空白泳道时该轨必被选中，故这是 `track` / `trackContent` 类工具脚本的目标入口。 |
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
| `project.addTrack(info?, index?)` | `track` | 按 track info 新建一条轨并插到第 `index`（0-based）位；`info` 省略 = 空白轨，`index` 省略 = 追加到末尾。返回其句柄。 |
| `project.insertTrack(track, index?)` | — | 把一条**游离**轨插回第 `index` 位（调序用；保持对象身份）。 |
| `project.removeTrack(track)` | `track` | 把轨从工程摘出，返回其（现已游离的）句柄：不插回 = 删除。 |
| `project.importTracks(path)` | `[track]` | 从文件导入**全部**轨、加法式并进当前工程，返回新加入的轨句柄。`path` = 本地文件路径；格式为 `tlp`/`tlpx`/`mid`/`midi` + 已装的格式插件。各轨含其 part/音符/音源/effect/自动化（音源未装则优雅降级为空源，同 UI 导入）。**时基**：保留当前工程的速度/拍号，各轨按**原始 tick** 落位（对齐小节、不做时基重映射）——最可预期的加法式默认；时基对齐 / 导入文件速度等模式未来可加。文件不存在/格式不支持/解析失败则报错（整脚本回退）。 |
| `project.tempos()` | `[{bpm, tick}]` | 所有速度标记。 |
| `project.timeSignatures()` | `[{numerator, denominator, bar}]` | 所有拍号标记（bar 为 1-based 小节号）。 |
| `project.setTempo(bpm, atTick?)` | — | 设速度；`atTick` 省略则改 tick 0 的基础速度，该处已有标记则改、否则新增。 |
| `project.setTimeSignature(numerator, denominator, atBar?)` | — | 设拍号；`atBar` 为 1-based 小节号（默认 1）。 |
| `project.removeTempo(atTick)` | — | 删掉 `atTick` 处的速度标记（`setTempo` 的对偶）。该处没有标记则**报错**（而非静默 no-op）；工程起点那一个是基准速度、不可删，改它请用 `setTempo`。 |
| `project.removeTimeSignature(atBar)` | — | 删掉第 `atBar`（1-based）小节的拍号标记。规矩同 `removeTempo`。 |

### 导出设置（`project` 上的字段 + `track.exportEnabled` / `track.exportChannels`）

| 字段 | 类型 | 说明 |
|---|---|---|
| `project.exportPath` | string | 导出到哪个目录。 |
| `project.exportFileName` | string | 导出文件名（不含扩展名）。 |
| `project.exportFormat` | string | `"wav"` / `"mp3"` / `"flac"` / `"ogg"`。未知值**报错**（不静默回退）。 |
| `project.exportSampleRate` | number | 采样率 Hz。 |
| `project.exportBitDepth` | number | 位深；仅无损格式（wav/flac）用得上。 |
| `project.exportBitrate` | number | 目标码率 kbps；仅有损格式（mp3/ogg）用得上。 |
| `project.masterExportEnabled` | bool | 是否导出总输出（母线）。 |
| `project.masterExportChannels` | number | 母线声道数：1 = 单声道 / 2 = 立体声。 |
| `track.exportEnabled` | bool | 是否导出该轨。 |
| `track.exportChannels` | number | 该轨声道数：1 / 2。 |

这一族在脚本面，正是为了"跑一段脚本把导出各项设成我的预设"这类**可复用命令**（还能绑快捷键）。

> ⚠️ **它们是「设置项」，不入撤销栈。** 与在导出侧栏里改它们一致：改完按 `Ctrl+Z` **不会**把导出路径退回去
> （撤销栈只装工程数据）。但「整段脚本原子」仍然成立——脚本**出错**或以**预览**方式跑时，宿主会把它们**还原**。
> 另外：这些只是"设置成什么"，真正**写出音频文件**是另一件事（agent 的 `export_project` 工具），脚本面不做。

---

## `track`（轨）

**字段**（裸属性，可读写）：`name`、`isMute`、`isSolo`、`gain`（单位 dB，0 = 原始电平）、`pan`（[-1, 1]）、`asRefer`（是否可被别的音源当参考音轨"听见"）、`color`（十六进制串如 `"#FF8800"`；空串 = 用主题默认色）。

**导出设置**（可读写）：`exportEnabled`（是否导出本轨）、`exportChannels`（1 = 单声道 / 2 = 立体声）。它们是**设置项**，见 `project` 一节的说明——不入撤销栈。

| 方法 | 返回 | 说明 |
|---|---|---|
| `track.getInfo()` | info | 本轨完整快照（纯数据）：`{name, gain, pan, mute, solo, asRefer, color, parts:[part info]}`。喂 `project.addTrack(info)` 即整轨复制。**刻意不含导出开关**——那是设置项、不属于"轨的内容"，故复制出来的轨其导出开关落默认值（要跟随就显式 `dst.exportEnabled = src.exportEnabled`）。 |
| `track.parts()` | `[part]` | 本轨所有 part 句柄（按起点排序）。 |
| `track.addPart(info)` | `part` | 按 part info 在本轨新建一个 part（字段见下节几何 + midi/audio 各自的内容字段），返回其句柄。 |
| `track.insertPart(part)` | — | 把一条**游离** part 插入本轨——目标轨**可以不是它原来那条**，这就是**跨轨迁移**（保持对象身份，音源/音符/曲线/effect/音素整体搬家）。 |
| `track.removePart(part)` | `part` | 把 part 从本轨摘出，返回其（现已游离的）句柄：不插回 = 删除，插到别的轨 = 移动。 |

> `parts` 按起点自动排序，故 `addPart`/`insertPart` 没有 `index` 参数——位置由 `pos` 决定。

---

## `part`

### 几何：三个原始字段可写、三个派生量只读

与数据层同形：

| 字段 | 读写 | 含义 |
|---|---|---|
| `pos` | 读写 | 锚点的绝对 tick，**同时是 part 内一切内容（音符/曲线/颤音）的坐标原点**——所以给 `pos` 赋值 = **平移整段**（内容跟随、长度不变）。 |
| `startOffset` | 读写 | 左边缘相对锚点的有符号偏移：`>0` 前向裁剪，`<0` 前向扩展。 |
| `endOffset` | 读写 | 右边缘相对锚点的有符号偏移（拖右边缘就是改它）。 |
| `startPos` | 只读 | `= pos + startOffset` |
| `endPos` | 只读 | `= pos + endOffset` |
| `dur` | 只读 | `= endOffset - startOffset` |

所以"建一个覆盖 tick 1920..3840 的空 part"写作 `track.addPart({ pos: 1920, endOffset: 1920 })`。

**其它字段**（可读写）：`name`、`gain`（dB，part 级增益，与轨级 gain 叠加）；**只读**：`type`（`"midi"`/`"audio"`）。

| 方法 | 返回 | 说明 |
|---|---|---|
| `part.getInfo()` | info | 本 part 完整快照（纯数据）：`{type, name, pos, startOffset, endOffset, gain, soundSource, notes, vibratos, effects, automations, piecewiseAutomations, pitch, properties}`；audio part 则是 `{type:"audio", name, pos, startOffset, endOffset, path}`。喂 `track.addPart(info)` 即整段复制。 |
| `part.track()` | `track` | 本 part 所属的轨（只读——换轨请用 `removePart` + 另一轨的 `insertPart`）。拿到 `tl.selectedParts()` / `tl.currentPart()` 后靠它向上取轨。 |
| `part.soundSource()` | `{type, id, name, kind, defaultLyric}` | 本 part 的声源信息（只读快照）；`kind` 为 `"voice"` 或 `"instrument"`。仅 MIDI part。 |
| `part.setSoundSource({kind, type, id})` | — | 切换本 part 的音源（`kind` = `"voice"`（默认）或 `"instrument"`；`type`/`id` 取自 `list_sound_sources`）。未知音源会报错而非静默清空；`type`+`id` 皆空则清成无音源。仅 MIDI part。 |
| `part.notes()` | `[note]` | 本 MIDI part 的所有音符句柄。 |
| `part.selectedNotes()` | `[note]` | 钢琴窗中当前选中的音符（无选中返回空数组）。 |
| `part.addNote(info)` | `note` | 按 note info 新增音符：`{pos, dur, pitch, lyric?, pronunciation?, properties?, leadingPhonemes?, bodyPhonemes?, bodyOffset?}`（pos 绝对 tick，pitch 为 MIDI），返回其句柄。 |
| `part.insertNote(note)` | — | 把一个**游离**音符插回本 part（保持身份）。音符归属它被创建时的 part、不能换父，跨 part 请用 `另一个part.addNote(n.getInfo())`。 |
| `part.removeNote(note)` | `note` | 把音符从本 part 摘出，返回其（现已游离的）句柄：不插回 = 删除。 |
| `part.samplePitch(startTick, endTick, samples)` | `[number]` | 在区间上等距采样最终音高曲线（MIDI 标度）。 |
| `part.setPitchLine(startTick, endTick, points)` | — | 清空 `[start, end)` 再落一条音高线；`points = [{tick, value}]`，value 为绝对 MIDI 音高（可含小数）。 |
| `part.clearPitch(startTick, endTick)` | — | 清空一段音高曲线。 |
| `part.automationIds()` | `[string]` | 音源声明的可编辑**连续**自动化轨 id（如 `"Volume"`；有默认基线。不含 pitch，也不含分段轨）。 |
| `part.sampleAutomation(id, startTick, endTick, samples)` | `[number]` | 在区间上等距采样某自动化曲线；`NaN` 表示该处无曲线。 |
| `part.setAutomation(id, startTick, endTick, points, defaultValue?)` | — | 清空再落一条自动化曲线；value 为参数绝对值；轨不存在按需创建，`defaultValue` 可选。 |
| `part.clearAutomation(id, startTick, endTick)` | — | 清空一段自动化曲线。 |
| `part.piecewiseAutomationIds()` | `[string]` | 音源声明的可编辑**分段**自动化轨 id。分段轨没有默认基线，段与段之间是**关断**（无值）——正是音高线那一族。因两族读写口径不同，故 id 分两张表列，取到就能直接用、不会"取到的 id 用起来报错"。 |
| `part.samplePiecewiseAutomation(id, startTick, endTick, samples)` | `[number]` | 等距采样某分段轨。 |
| `part.setPiecewiseAutomationLine(id, startTick, endTick, points)` | — | 清空 `[start, end)` 再落一条分段曲线（形状同 `setPitchLine`）。 |
| `part.clearPiecewiseAutomation(id, startTick, endTick)` | — | 清空一段分段曲线。 |
| `part.vibratos()` | `[vibrato]` | 本 part 的所有颤音句柄。 |
| `part.addVibrato(info)` | `vibrato` | 按 vibrato info 新增颤音（叠加在音高曲线之上）：`{pos, dur, frequency?(6), amplitude?(1), phase?(0), attack?(0.2), release?(0.2), affectedAutomations?, affectedEffectAutomations?}`，返回其句柄。 |
| `part.insertVibrato(vibrato)` | — | 把一个**游离**颤音插回本 part（保持身份）。同音符：不能换父。 |
| `part.removeVibrato(vibrato)` | `vibrato` | 把颤音从本 part 摘出，返回其（现已游离的）句柄。 |
| `part.effects()` | `[effect]` | 本 part 的串行效果链（按处理顺序）。 |
| `part.addEffect(info, index?)` | `effect` | 按 effect info 新建一个效果器并插到链中第 `index`（0-based）位；`index` 省略 = 追加到链尾。`info.type` 必填，且必须是 `list_effects` 里存在的引擎 id（未知类型报错）。返回其句柄。 |
| `part.insertEffect(effect, index?)` | — | 把一个**游离**效果器插回链中第 `index` 位（保持身份，故其自动化曲线与颤音影响表的引用都还连着）。 |
| `part.removeEffect(effect)` | `effect` | 把效果器从链中摘出，返回其（现已游离的）句柄。 |
| `part.moveEffect(effect, index)` | — | 把某效果器移到链中的第 `index`（0-based）位。 |
| `part.getProperty(key)` | 值 | 音源（voice/instrument）声明的某 per-part 参数当前值（`number`/`boolean`/`string`），未设则 `null`。键、取值范围与默认值见 `list_sound_sources`。 |
| `part.setProperty(key, value)` | — | 写一个声明的 per-part 参数（`value` = `number`/`boolean`/`string`）。 |

---

## `note`（音符）

**字段**（裸属性，可读写）：`pos`、`dur`、`pitch`、`lyric`、`pronunciation`；**只读**：`pitchName`（如 `"C4"`）、`hasPinnedPhonemes`（bool）。`pronunciation` 是 voice 的显式发音覆盖——非空则强制该发音，空串 = 无覆盖，歌词原文直达引擎、由引擎自行 G2P（录入歌词时是否自动填入编辑器 G2P 结果，取决于 `AutoGeneratePronunciation` 设置）。`bodyOffset`（秒）可读写（引导/主体结合线相对 note 头的偏移；写会自动钉死音素）。

| 方法 | 返回 | 说明 |
|---|---|---|
| `note.getInfo()` | info | 本音符完整快照（纯数据）：`{pos, dur, pitch, lyric, pronunciation, properties, leadingPhonemes, bodyPhonemes, bodyOffset}`。喂 `part.addNote(info)` 即复制。 |
| `note.part()` | `part` | 本音符所属的 part（只读；数据层就不可改）。`vibrato.part()` / `effect.part()` 同理。 |
| `note.getProperty(key)` | 值 | 音源声明的某 per-note 参数当前值（`number`/`boolean`/`string`），未设则 `null`。键、取值范围与默认值见 `list_sound_sources`。 |
| `note.setProperty(key, value)` | — | 写一个声明的 per-note 参数（`value` = `number`/`boolean`/`string`）。 |
| `note.phonemes()` | `[phoneme]` | 该 note 的音素（引导 ++ 主体，时间序）；未合成前为空。仅 voice part。 |
| `note.addLeadingPhoneme(info)` | `phoneme` | 追加一个音素到**引导**列表末（核前前置辅音）；自动钉死。`info = {symbol, duration?(秒,默认0), stretchWeight?(默认0), properties?}`，`stretchWeight` 0 = 刚性辅音 / >0 = 可伸元音。 |
| `note.addBodyPhoneme(info)` | `phoneme` | 追加一个音素到**主体**列表末（核 + 尾辅音）；参数同上。 |
| `note.removePhoneme(phoneme)` | — | 删除一个音素；自动钉死。音素在数据层没有父指针，故跨 note 搬运走 info 路：`另一个note.addBodyPhoneme(ph.getInfo())` 再 `removePhoneme(ph)`。 |
| `note.pinPhonemes()` | — | 把合成音素固定为可编辑用户数据（幂等；一般首次音素写入时自动发生）。 |
| `note.clearPhonemes()` | — | 清除钉死音素、回到合成产物口径。 |

音素在你编辑前来自引擎（只读）；**首次写入会自动钉死**成可编辑数据（与侧栏面板首次编辑音素完全一致）。

---

## `phoneme`（音素）

`note.phonemes()` 里的一项。**字段**——只读：`leading`（bool；引导 = 核前前置辅音，主体 = 核 + 尾辅音）；可读写：`symbol`、`duration`（秒）、`stretchWeight`（0 = 刚性辅音，>0 = 可伸元音，其时长为派生填充量、布局时忽略）。写任一字段都会自动钉死该 note 的音素。

音素句柄按**位置**定址：增删音素会改变其后音素的下标，结构变更后请重新 `note.phonemes()`。

| 方法 | 返回 | 说明 |
|---|---|---|
| `phoneme.getInfo()` | info | 本音素完整快照（纯数据）：`{symbol, duration, stretchWeight, properties}`（未钉死时 `properties` 为 `null`）。 |
| `phoneme.getProperty(key)` | 值 | voice 声明的某 per-phoneme 参数当前值（`number`/`boolean`/`string`），未设或该 note 尚未钉死则 `null`。键、取值范围见 `list_sound_sources` 的音素 slot。 |
| `phoneme.setProperty(key, value)` | — | 写一个声明的 per-phoneme 参数（`value` = `number`/`boolean`/`string`）；自动钉死。 |

---

## `vibrato`（颤音）

**字段**（裸属性，可读写）：`pos`、`dur`（绝对 tick），`frequency`（Hz）、`amplitude`（半音）、`phase`（单位 = π）、`attack`、`release`（秒）。

| 方法 | 返回 | 说明 |
|---|---|---|
| `vibrato.getInfo()` | info | 本颤音完整快照（纯数据，含两张影响表）：`{pos, dur, frequency, amplitude, phase, attack, release, affectedAutomations, affectedEffectAutomations}`。 |
| `vibrato.affectedAutomations()` | `{轨id: 振幅}` | 本颤音施加到**音源级**参数轨上的振幅表（只读快照）。 |
| `vibrato.affectedEffectAutomations()` | `{effect id: {轨id: 振幅}}` | 施加到**effect 级**参数轨上的振幅表。外层键是 `effect.id`（实例稳定身份、不是链内位置），故重排效果链不会打乱这张表。 |
| `vibrato.setAmplitude(id, amplitude, effect?)` | — | 写一条轨的影响振幅（原本无关联则建立关联）。`effect` 省略 = 音源级轨；传一个 effect 句柄（须与本颤音同 part）= 该 effect 的轨。 |
| `vibrato.removeAmplitude(id, effect?)` | — | 解除一条轨的关联（与 `setAmplitude` 对偶）。 |

---

## `effect`（效果器）

`part.effects()` 里的一项。**字段**——可读写：`isEnabled`（bool；`false` = 旁路）；**只读**：`type`（引擎 id）、`name`（显示名）、`id`（实例稳定 id）、`index`（链中 0-based 位置）。

| 方法 | 返回 | 说明 |
|---|---|---|
| `effect.getInfo()` | info | 本效果器完整快照（纯数据，含参数与自动化曲线）：`{id, type, isEnabled, automations, piecewiseAutomations, properties}`。喂 `part.addEffect(info)` 即复制一个新实例（落到同一条链时 `id` 会重新发号，避免与源撞身份）。 |
| `effect.getProperty(key)` | 值 | 某参数的当前值（`number`/`boolean`/`string`），未设则 `null`。键、取值范围与默认值见 `list_effects`。 |
| `effect.setProperty(key, value)` | — | 写一个参数（`value` = `number`/`boolean`/`string`）。 |
| `effect.automationIds()` | `[string]` | 本 effect 引擎声明的可自动化参数 id（见 `list_effects`）。 |
| `effect.sampleAutomation(id, startTick, endTick, samples)` | `[number]` | 等距采样本 effect 某自动化曲线；`NaN` = 该处无曲线。 |
| `effect.setAutomation(id, startTick, endTick, points, defaultValue?)` | — | 清空 `[start, end)` 再落线（作用于本 effect）；`points = [{tick, value}]`，value = 参数绝对值；轨不存在按需创建，`defaultValue` 可选。形状同 `part.setAutomation`，只是作用域是本 effect。 |
| `effect.clearAutomation(id, startTick, endTick)` | — | 清空本 effect 某自动化曲线的一段。 |
| `effect.piecewiseAutomationIds()` | `[string]` | 本 effect 引擎声明的**分段**参数轨 id（无基线、段间关断）。 |
| `effect.samplePiecewiseAutomation(id, startTick, endTick, samples)` | `[number]` | 等距采样本 effect 某分段轨。 |
| `effect.setPiecewiseAutomationLine(id, startTick, endTick, points)` | — | 清空 `[start, end)` 再落一条分段曲线。 |
| `effect.clearPiecewiseAutomation(id, startTick, endTick)` | — | 清空本 effect 某分段曲线的一段。 |

effect 自动化与 part 级自动化**逐一平行**——同样的绝对 tick `points` 与绝对值语义、同样分连续/分段两族，只是目标从音源换成链中某 effect。

---

## 示例

**把当前 part 所有音符升八度，并在每个音符上方加一个三度和声：**
```js
const part = tl.currentPart();
for (const n of part.notes()) {
  const info = n.getInfo();   // 该音符的完整快照（属性、音素一并带上）
  info.pitch += 4;            // info 是纯数据，随便改
  part.addNote(info);         // 三度和声
  n.pitch += 12;              // 原音升八度
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
const info = project.tracks()[0].getInfo();   // 全维度：音源、曲线、effect、属性、音素…
info.name = "Harmony +8";
const dst = project.addTrack(info);
for (const p of dst.parts())
  for (const n of p.notes()) n.pitch += 12;
```
> 先 `getInfo()` 复制、再改副本。用 `addPart` + `addNote` 逐个重建只会搬 `pos`/`dur`/`pitch`/`lyric`，
> **静默丢掉**音源、音高线、自动化曲线、颤音、effect 链、part/note 属性和音素——看着像成功了，实际只剩音符骨架。

**把第一轨的第一个 part 移到第二轨（移动，不是复制）：**
```js
const [a, b] = tl.currentProject().tracks();
const p = a.parts()[0];
a.removePart(p);   // p 现在游离：可读、不可写
b.insertPart(p);   // 同一个对象，落到 b 轨——音源/曲线/effect/音素全跟着走
```

**把某个 part 整段往后挪两小节（4/4）：**
```js
const p = tl.selectedParts()[0];
p.pos += 2 * 4 * tl.ppq;   // 改锚点即平移整段，内容跟随、长度不变
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

## 工具脚本 —— 存进库、进菜单、绑快捷键

上面那些是**一次性脚本**：写完点 Run 就跑，用完即弃。给脚本加一个 `getScriptInfo()`，它就变成**工具**——存进脚本库后出现在菜单里、可以绑快捷键、可以反复用。

```js
function getScriptInfo() {
  return { name: "加三度和声", context: "note" };
}
function main() {                      // 动作写在 main 里，body 与一次性脚本一模一样
  const p = tl.currentPart();
  for (const n of p.selectedNotes())
    p.addNote({ pos: n.pos, dur: n.dur, pitch: n.pitch + 4, lyric: n.lyric });
}
```

- **没有 `getScriptInfo` 的脚本**只属于 Script 侧栏，永远不进菜单。
- **`main()` 整体是一个可撤销单位**，中途出错则全部回退（与一次性脚本同一规则）。
- 工具名想跟随界面语言，用 `tl.language` 分支：`name: tl.language === 'zh-CN' ? '加三度和声' : 'Add Third Harmony'`。

### `getScriptInfo()` 的字段

| 字段 | 必填 | 说明 |
|---|---|---|
| `name` | ✔ | 菜单里显示的名字。 |
| `context` | | 决定它**出现在哪、作用于什么**，同时决定快捷键在哪个区域生效。默认 `'global'`。见下表。 |
| `id` | | **稳定锚点**，用于记住用户绑的快捷键与设置。可用 `A-Z a-z 0-9 . _ -`。**发布后就别再改**；不写则用文件名当 id——那样一改名，用户绑的快捷键就丢了。 |
| `defaultGesture` | | 建议快捷键，如 `'mod+shift+k'`（`mod` = macOS 的 Cmd / Windows 的 Ctrl，也可直接写 `ctrl`/`cmd`/`alt`/`shift`）。**只在该键位空闲时生效，绝不顶掉内置快捷键**；用户可在设置里改绑。 |

`context` 的取值：

| `context` | 出现在哪 | 作用对象 |
|---|---|---|
| `'global'` | 顶部 Scripts 菜单 | `tl.currentPart()` 或整个工程 |
| `'note'` | 钢琴窗里**右键音符** | `tl.currentPart().selectedNotes()`（被右键的音符必在选中里） |
| `'partContent'` | 钢琴窗**空白处**右键 | `tl.currentPart()` 的内容 |
| `'pianoSelection'` | 钢琴窗**右键范围选区** | `tl.pianoSelection()`（一段 tick 带；无选区时为 `null`） |
| `'part'` | 编排区**右键 part** | `tl.selectedParts()`（可能多个） |
| `'track'` | **右键轨头** | `tl.selectedTracks()`（可能多个） |
| `'trackContent'` | 编排区某轨的**空白泳道**右键 | `tl.selectedTracks()` |
| `'trackSelection'` | 编排区**右键范围选区** | `tl.trackSelection()`（tick × 轨道；无选区时为 `null`） |

快捷键的生效区域跟着 context 走：`piano*` 只在钢琴窗触发，编排区的几个只在编排区，`global` 在编辑器任何地方都行。**用快捷键触发时没有"被右键的那个"**，作用对象就是**当前选中**——所以 `main()` 应该在选中为空时什么都不做。

### `getInputConfig(ctx)` —— 运行前弹表单问参数

再加一个 `getInputConfig`，宿主会在跑 `main` 之前弹一个表单，把填好的值交给 `main(inputs)`：

```js
function getScriptInfo() { return { name: "移调", context: "note" }; }
function getInputConfig(ctx) {
  return { semitones: SliderConfig.integer(12, -24, 24) };   // 键 = 字段标签
}
function main(inputs) {
  for (const n of tl.currentPart().selectedNotes()) n.pitch += inputs.semitones;
}
```

返回值是一个 **`键 → config` 的 map**（不是普通数据），键就是字段在表单里的标签，值用下面的构造器造。

**两个口径别搞混**（最容易踩的一条）：

| | 内容 |
|---|---|
| `getInputConfig` 的 `ctx.values` | **稀疏**——只含用户**改过**的键，没动过的读到 `undefined`。所以这里取值**永远要兜默认**：`const mode = ctx.values.mode ?? 'transpose'` |
| `main` 的 `inputs` | **全量**——你声明过的每个字段都在（用户填的值，或该 config 的默认值）。直接读，不必判断存在性 |

**条件字段**：`getInputConfig` 在**每次改值后都会重新调用**，所以直接按 `ctx.values` 分支增删字段即可：

```js
function getInputConfig(ctx) {
  const mode = ctx.values.mode ?? 'transpose';
  const cfg = { mode: ComboBoxConfig.create(['transpose', 'setPitch']) };
  if (mode === 'transpose') cfg.semitones = SliderConfig.integer(12, -24, 24);
  else                      cfg.targetPitch = SliderConfig.integer(60, 0, 127);
  return cfg;
}
```

**无副作用铁律**：`getInputConfig` 会被反复调用（开窗时 + 每次改值），**只许声明、不许动手**——所有实际改动都放进 `main`。在这里读工程作为依据是可以的（`tl.currentPart()`、`selectedNotes()` 等），改工程不行。

### 输入控件构造器

方法名与控件类型对应，`withX(...)` 返回新的 config，可以链式接。

| 构造器 | 说明 |
|---|---|
| `SliderConfig.linear(默认, min, max)` | 滑条（连续） |
| `SliderConfig.integer(默认, min, max)` | 滑条（整数） |
| `SliderConfig.create(默认, scale)` | 自定义标度的滑条，`scale` 见下 |
| ↳ `.withFormat(fmt)` `.withMinLabel(s)` `.withMaxLabel(s)` `.withRandomizable()` | 数值显示格式 / 两端文字标签 / 允许随机化 |
| `DraggableNumberBoxConfig.create(默认?)` `.integer(默认?)` | 可拖拽数字框 |
| ↳ `.withMin(x)` `.withMax(x)` `.withRange(a,b)` `.withStep(s)` `.withSensitivity(s)` `.withFormat(fmt)` | 范围 / 步进 / 拖拽灵敏度 / 格式 |
| `ComboBoxConfig.create(['a','b'])` 或 `.create()` | 下拉。**默认值给的是「值」本身，不是下标** |
| ↳ `.append(x)` `.appendSeparator(标签?)` `.withDefault('a')` | 追加项 / 分隔线 / 指定默认值 |
| `CheckBoxConfig.create(默认?)` | 勾选框 |
| `TextBoxConfig.create(默认?)` | 文本框 |
| ↳ `.withPassword()` | 密码样式（内容打码） |

**标度与格式**（`SliderConfig.create` / `.withFormat` 的参数）：

| | 说明 |
|---|---|
| `NormalizedScale.linear(min, max)` `.integer(min, max)` | 线性 / 整数标度 |
| `NormalizedScale.rounded(s)` `.floor(s)` `.ceil(s)` | 在已有标度上取整 |
| `NormalizedScale.custom(p => 值, 值 => p)` | **自定义标度**：两个互逆函数，`p` 是 0..1 的位置——用来做对数/指数轴 |
| `NumberFormat.decimals(n)` | 固定小数位 |
| `NumberFormat.custom(v => 字符串, s => 数字或 null)` | **自定义显示/解析**，带单位时用它；解析失败返回 `null` |

自定义的这两个函数在表单打开期间**实时被调用**，所以要保持纯粹且轻量。它们出错或返回非法值时会安全降级，不会把异常抛进界面。

```js
// 频率滑条：20Hz–20kHz 用对数轴，显示带单位
function getInputConfig(ctx) {
  return {
    freq: SliderConfig.create(1000, NormalizedScale.custom(
        p => 20 * Math.pow(1000, p),               // 0..1 → 20..20000
        v => Math.log(v / 20) / Math.log(1000)))   // 反过来
      .withFormat(NumberFormat.custom(
        v => v >= 1000 ? (v / 1000).toFixed(2) + " kHz" : v.toFixed(0) + " Hz",
        s => { const m = /^([\d.]+)\s*k?/i.exec(s.trim()); return m ? parseFloat(m[1]) * (/k/i.test(s) ? 1000 : 1) : null; }))
  };
}
```

---

## 注意事项

- **句柄不可写死、不可跨运行。** 永远当场取、立即用。
- **集合方法返回数组，不是链表。** 用 `for-of` / 下标；没有 `.first` / `.next`。每次调用都是一份新快照——要复用就先存进变量。
- **创建和删除都走父对象**（`track.addPart`/`removePart`、`part.addNote`/`removeNote` …）——没有 `x.remove()`。
- **要复制就 `getInfo()` → `addX(info)`**，别逐字段手搬（会静默丢音源/曲线/effect/属性/音素）。
- **`removeX` 返回的句柄是游离态：可读、不可写。** 给它赋值会报错并提示先插回。只有 part 能换父（跨轨迁移）。
- **改 `pos`/`dur` 可能改变排序。** part/音符/颤音都按起点维持有序——句柄寻址不受影响（仍指向同一对象），但若你同时在迭代该集合，注意你拿的是迭代开始时的数组快照。
- **音符必须在 MIDI part 里。** 从零写旋律时，先 `tl.currentProject().addTrack()`（或选一条轨）、`track.addPart({pos, endOffset})` 建容器，再往它里面 `part.addNote`。
- **出错处理。** 脚本抛错会把信息返回（语法/类型错误通常带行号），并**回退它所做的全部改动**——工程保持不变，定位后改脚本重跑。
