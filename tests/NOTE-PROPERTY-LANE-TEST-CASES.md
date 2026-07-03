# 属性参数栏 lane 验证（独立用例）

验证「有界数值 note / phoneme 属性钉选到参数面板显示 + 编辑」链路（`ParameterPinning` 钉选存储（分种类）/ `AutomationKey.NoteLane`/`PhonemeLane` 路由 / `MidiPart.NoteLaneConfigs`+`PhonemeLaneConfigs` / tabbar lane 按钮 / `AutomationRenderer` 渲染与拖写操作）。

## 前置

- 已 build + pack + install `v1-voice`（包名 **V1 Test Voice**）。
- 测试夹具：`V1 Test Voice` 引擎声明了——
  - note 属性 **`tension`**（`SliderConfig`，-1..1，默认 0）；
  - 每个音素的属性 **`accent`**（`SliderConfig`，0..1，默认 0）与 **`Offset (ms)`**（`DraggableNumberBoxConfig`，无界——**无 lane 资格**，作反例）。

## 进入被测面板

1. 打开 TuneLab，新建工程，给 part 选 **V1 Test Voice** 下任一声库（Alice / Bob / Carol）。
2. 画几个音符，**选中至少一个** → 右侧属性栏 **Note** 页签出现 `tension` 滑条。

## 用例：钉选与呈现

| # | 操作 | 期望 |
|---|---|---|
| 1 | 在 `tension` 行**任意位置**右键（标题、滑条、行内空白区皆可） | 弹出菜单，含「在参数栏编辑」一项。 |
| 2 | 点击「在参数栏编辑」 | 底部参数 tabbar 的 voice 组尾部（Volume/Growl 等之后、effect 组之前）出现 **tension** 按钮，带宿主分配的轨色（与 Volume/Growl/回显轨色明显不同）。 |
| 3 | 点亮 tension 按钮（Edit 态） | 参数区按 note 显示：每个音符一段**顶部实色横线**（y=该 note 的 tension 值）+ 其下同色**半透明柱**；相邻音符段间有 1px 缝；左侧上下界显示 `1.00` / `-1.00`。 |
| 4 | 画两个**时间重叠**的音符（前长后短交叠） | 前一个 note 的段在后一 note 起点处截断（宽度去重叠，镜像合成"后盖前"钳位），段间不叠画。 |
| 5 | 侧栏拖 `tension` 滑条改某选中 note 的值 | 参数区该 note 的段**实时**上下移动（松手前即跟随）。 |
| 6 | 未写过值的 note | 段画在默认值高度（0 → 垂直居中）。 |
| 7 | 拖动 note 移动/改长 | lane 段跟随 note 几何实时重绘。 |
| 8 | 把 tension 设为 Visible（非 Edit）态，再激活 Volume | tension 段仍以细线显示（压暗），Volume 曲线为 active 粗线。 |
| 9 | 再次右键侧栏 `tension` 行 | 菜单项变为「从参数栏移除」；点击后 tabbar 按钮与参数区段消失。 |

## 用例：参数栏内编辑（tension 为 active 时）

| # | 操作 | 期望 |
|---|---|---|
| 10 | 在某 note 的段上**主键垂直拖动** | 该 note 的顶线跟随鼠标 y 实时移动；松手后侧栏滑条（选中该 note 时）显示同值。 |
| 11 | 主键**横扫**跨过多个 note | 扫过的每个 note 的值被写为扫到它时的鼠标高度（曲线式擦写）。 |
| 12 | **Ctrl+主键横扫** | 定值模式：锁定按下点的值，扫过的 note 全部写为同一值（批量拉平）。 |
| 13 | 一次拖动（无论扫过多少 note）后 **Ctrl+Z** | 整段拖动作为**一个撤销步**回退全部受影响 note。 |
| 14 | **右键拖动**横扫若干 note | 扫过的 note 属性值被移除、段回到默认值高度；一次扫动一个撤销步。 |
| 15 | 值在拖动中越过量程边界 | 钳在 -1..1 内（顶线不出参数区）。 |
| 16 | Anchor 工具下在 lane 上点击/拖动 | 无锚点、无输入框、不误建数据（lane 无锚点概念）。 |
| 17 | Shift+主键拖 | 仍是范围选区（选区手势优先级不变）。 |

## 用例：phoneme 属性 lane

前置：选中含音素的音符（合成完成后），侧栏 **Phoneme** 面板出现逐音素行（`accent` 滑条 + `Offset (ms)` 数值框）。

| # | 操作 | 期望 |
|---|---|---|
| P1 | 在某音素行的 `accent` 滑条上右键 | 菜单 =「在参数栏编辑」+ 既有「拆分 / 删除」（slot 动作不因钉选菜单而丢失）。 |
| P2 | 在 `Offset (ms)` 行右键 | **无**「在参数栏编辑」项（无界数值无 lane 资格），仍有 拆分/删除。 |
| P3 | 点「在参数栏编辑」 | tabbar voice 组尾部（note lane 之后）出现 **accent** 按钮，轨色与 tension/自动化轨均不同。 |
| P4 | 激活 accent lane | 参数区**逐音素**分段（比 note 段细，段边界与波形带音素分界对齐），段间 1px 缝；未编辑过的音素段在默认值高度（0 → 底部）。 |
| P5 | 在某音素段上主键垂直拖 | 该音素值实时跟随；若该 note 原为合成音素，首次编辑自动钉死（波形带该 note 音素文字变粗体），几何不变。 |
| P6 | Ctrl+主键横扫多个音素 | 扫过音素统一写为锁定值；跨 note 也生效（各 note 各自钉死）。 |
| P7 | 一次拖动后 Ctrl+Z | 一个撤销步：值与"钉死"一并回退（原合成 note 回到非钉死态、文字回常规）。 |
| P8 | 右键横扫 | 仅**钉死音素**的该属性被移除回默认；未钉死音素不被顺手钉死。 |
| P9 | 侧栏音素面板拖 `accent` 滑条 | lane 上对应音素段实时跟随（双向一致）。 |
| P10 | 重合成回填（如改歌词后） | lane 段几何随新音素刷新，无滞留旧段。 |
| P11 | instrument part 上 | 侧栏无音素面板、无 phoneme lane 入口（instrument 无音素，恒空）。 |

## 用例：量程端点描述文本（Min/MaxLabel 大一统：滑条两端 / 默认值滑条 / 参数区上下界同源）

夹具声明：`tension` 双端 Relaxed/Tense、`accent` 仅 Max 端 Strong（验证单端）、`Growl` 自动化 Clean/Growly。

| # | 操作 | 期望 |
|---|---|---|
| L1 | 选中音符看侧栏 `tension` 滑条 | 轨道**正下方**一行淡色小字：左端 **Relaxed**、右端 **Tense**（与轨道两端对齐、不挤占轨道长度；数值框照旧在最右、与轨道同排）。 |
| L2 | 激活 tension lane | 参数区左上显 **Tense**、左下显 **Relaxed**（代替数值上下界）。 |
| L3 | 音素面板 `accent` 滑条 | 轨道下方仅右端显 **Strong**；`tension` 之外未声明 label 的滑条（如设置窗滑条）下方无此行、高度不变。 |
| L4 | 激活 accent lane | 左上 **Strong**、左下回退数值 `0.00`（单端声明另一端走数值）。 |
| L5 | 激活 Growl 自动化轨 | 参数区左上 **Growly**、左下 **Clean**（此前该路径无 producer，首次实测）。 |
| L6 | Part 属性侧栏的 Growl 默认值滑条 | 轨道两端同样显示 Clean / Growly。 |
| L7 | Volume 等未声明 label 的轨/滑条 | 两端无小字、不占位，上下界照旧数值。 |

## 用例：持久化与身份

| # | 操作 | 期望 |
|---|---|---|
| 18 | 钉选后**重启应用**、重开工程 | tension / accent 的 **tab 立即恢复**（tab 存在性跟钉选意图走）。accent 在合成回填完成前是未解析态：面板不画段、无上下界、不可编辑；回填完成（约 1s）后段与量程出现。钉选存 `%AppData%\TuneLab\Configs\ParameterPins.json`（独立文件、键带 note/phoneme scope，**不在** Settings.json / EditorState.json 里）。 |
| 18b | 在 tabbar 的 lane 按钮上**右键** | 菜单「从参数栏移除」，点击即解钉（与侧栏属性行右键对称；引擎不再声明该属性时这是死 tab 的唯一移除口）。 |
| 19 | 钉选后把 part 换成其它声源（如 legacy voice） | tension 按钮消失（id 对不上）；换回 V1 Test Voice 后自动恢复。 |
| 20 | 复制含 tension 值的 note 粘贴 | 新 note 的段在同一高度（属性随 note 序列化携带）。 |

## 已知非目标（本次不验证）

- 用户改 lane 轨色——存储已按「键→色」预留，右键换色后置。
- 条件 config 随 note/音素增删漂移 schema 的即时刷新——lane 集合在管线重建 / part 参数 commit / 钉选变更时重算，note 增删不触发（值本体渲染时活读、无滞后）。
- 同起点和弦兄弟（去重叠后零宽）不出段、lane 内不可选中编辑——按数据序钳位的固有边角。
- phoneme lane 的逐音素异构 config：lane 量程取该 id 的**首见声明**；个别音素未声明该属性时段仍显示默认高度（编辑会写入，引擎自行忽略未声明键）。
