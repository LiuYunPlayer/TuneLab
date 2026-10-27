# 参数面板多开（设计）

钢琴窗下方从「一个参数面板」改为「可以叠放多个参数面板」：每个面板各自选参数、各自调高度、各自收起，
同时看得见、都能编辑（如一个画音高、一个放混合画布、一个看音量）。底部标签栏仍只有一条，作用于**焦点面板**。

本功能也是"画布钉到参数区"的前置：画布占一个面板时，用户仍要能同时看别的参数。

## 1. 行为（已定）

| 项 | 规则 |
|---|---|
| 面板内容 | 每个面板各有一套「激活 + 可见」参数（即现在单面板的模型，原样复制到每个面板）与一套回显轨显隐 |
| 焦点 | 任一时刻恰有一个焦点面板。在面板里点一下 / 开始编辑即聚焦；新加的面板自动聚焦。收起的面板也能当焦点（点它的标题栏） |
| 焦点标识 | 焦点面板标题栏最左侧一条竖色条，颜色 = 它激活参数的颜色（与底部标签栏里点亮的按钮同色）；参数名白字。非焦点面板无色条、参数名灰字 |
| 底部标签栏 | 只一条，显示并修改焦点面板的「激活 / 可见 / 关」 |
| 高度 | 每个面板的标题栏是它的拖拽把手；拖动只改这一个面板的高度，其余面板高度不变，差额由钢琴窗吸收 |
| 收起 / 展开 | 每个面板各自收起；收起后只留标题栏（显示激活参数名），展开恢复原高度 |
| 整个参数区 | Ctrl+P 与标签栏最左的开关收起 / 恢复**整个参数区**（全部面板）；单个面板的收起在各自标题栏上 |
| 新增 | 标签栏最左侧（钢琴键那一列下方）、整区开关旁放「+」：在焦点面板下方插入一个与焦点面板设置相同的新面板，新面板获得焦点 |
| 关闭 | 右键面板标题栏 →「关闭面板」；只剩一个面板时不出现 |
| 同一参数多处 | 允许同一参数出现在多个面板；编辑的是同一份数据，互相实时同步 |
| 参数暂不存在 | 沿用现有规则：记录保留，显示回落到 Volume，参数回来后自动恢复 |
| 波形 | 全局唯一的独立开关，不属于任何面板；开关只是**显示在最上面那个面板的标题栏上**，波形带贴在面板堆顶上 |
| 撤销 / 已修改 | 面板的增删、高度、收起、换参数、换焦点都**不进撤销**，也**不算工程已修改**（与播放头位置同口径） |

### 1.1 高度：存"想要的高度"，显示时按空间压缩

- 每个面板记的是**想要的高度**（逻辑像素，与 DPI 无关）——与现在内存里的 `mParameterHeight` 同义。
- 显示：可用高度 = 参数层总高 − 各面板标题栏。展开面板想要的高度之和放得下就各按其高显示，余下归钢琴窗；
  放不下就**展开面板按比例一起压缩**（每个保留最小高度）。收起的面板只占标题栏。
- 压缩只改显示、不改记录：窗口变大自动长回来。在压缩状态下拖动，把当前各面板的显示高度一并记成新的想要高度
  （所见即所得，松手不会有别的面板跳变）。
- 只存一套高度，不再分「最大化 / 普通窗口」两套——窗口差异由压缩吸收。`EditorState.json` 里原有的参数栏高度
  保留，只作新建工程（及打开不带布局的工程）时第一个面板的默认高度。

## 2. 持久化：工程文件的 `EditorInfo`

面板布局是工程的一部分：打开工程即还原它的面板。存进 `NativeProjectFile.Editor`（`EditorInfo`，现在只有 `PlayheadPos`），
宿主内部契约、**不改 SDK**；非原生格式（MIDI、格式插件）自然不带。

- 只有 `.tlpx`（CBOR）读写 `EditorInfo`；旧的 `.tlp`（JSON）历来不存 editor 信息，打开得到默认单面板——接受。
- 旧工程没有这一项 = 默认单面板。CBOR 读取对未知键本就跳过，前向兼容。

```
EditorInfo
  PlayheadPos
  ParameterPanels: [ PanelInfo ]      // 自上而下；缺省 = 一个默认面板
  FocusedParameterPanel: int
PanelInfo
  Height: double                      // 想要的高度
  Collapsed: bool
  Active: ParameterRef?               // null = 默认（Volume）
  Visible: [ ParameterRef ]
  VisibleSynthesized: [ ParameterRef ]
ParameterRef
  Source: voice | effect | noteLane | phonemeLane
  Id: string                          // 该来源内的参数 id
  EffectId, EffectIndex, EffectType   // 仅 effect：实例 id + 链内位置 + 类型
```

### 2.1 effect 参数怎么认回来

运行时的路由键 `AutomationKey.Effect(index, id)` 带的是链内下标（交互用，不持久化）；effect 实例另有稳定 GUID
（`Effect.Id`，颤音外键 `EffectAutomationRef` 就用它）。面板布局是整个工程共用的、跨 part 的，所以恢复时：

1. 当前 part 的链里**有这个实例 id** → 跟着它（重排后也准确）；
2. 否则（换了 part，各 part 的实例 id 不同）→ 按**位置 + 类型**：该位置恰是同类型 effect 才用它的同名参数；
3. 都对不上 → 按"参数暂不存在"回落。

## 3. 实现

### 3.1 结构

- 新增 `ParameterPanel`（一块面板 = 标题栏 + 容器 + 渲染器 + 状态）。状态 = 激活键、可见集、回显可见集、想要的高度、收起。
  它实现 `AutomationRenderer.IDependency` / `ParameterTitleBar.IDependency`，原来由 `PianoWindow` 统一实现的
  「单面板」那部分（`mActiveAutomation`、`mVisibleAutomations`、`mVisibleSynthesizedParameters`、`IsAutomationVisible`、
  `SetAutomationVisible`（含可编辑轨变可见时带出配对回显）、激活回落）整体搬进面板。共享的部分（part、刻度轴、`PianoScrollView`、
  `PianoTool`、`SynthesizedParameterConfigs`）仍由 `PianoWindow` 提供。
- `PianoWindow` 持有 `List<ParameterPanel>` 与焦点下标；参数层从「一对标题栏 + 容器」改为 N 对。
- `ParameterTabBar.IDependency` 指向焦点面板：焦点变化时重新同步按钮状态（新增 `FocusedPanelChanged`）。
- 标题栏：左侧加焦点色条 + 激活参数名（收起时也靠它认面板）；波形开关只在最上面的面板显示；回显 chip 照旧在右侧、作用于本面板。
  收起 / 展开入口放在标题栏上（形式见 §5 待定）。
- 布局计算抽成纯函数（输入：参数层高、各面板想要高度与收起状态、标题栏高、最小高度；输出：各面板显示高度），单测覆盖。

### 3.2 "哪一个面板"的改点

| 现在 | 改为 |
|---|---|
| `WaveformBottom` = 视图高 − 一个标题栏 − 一个容器 | 视图高 − 整个面板堆的高度（面板堆顶） |
| `CanRunEditCommand` / `OnKeyDown` 只看一个渲染器是否在操作 | 任一面板的渲染器在操作即视为在操作 |
| `DeleteSelection` / `SelectAllInPiano` 用固定的渲染器（锚点工具 + 悬停） | 用指针悬停的那个面板的渲染器 |
| `PianoScrollView` 点音高区后取消自动化点选中，并刷新那一个渲染器 | 刷新全部面板的渲染器（`PianoScrollView.IDependency` 去掉单个 `AutomationRenderer`，改为"刷新所有参数面板"） |
| `ToggleParameterPanel` 收起 / 恢复一个高度 | 收起 / 恢复整个参数区（各面板高度与收起状态不动） |
| `UpdateHorizontalBarMargin` 按一个面板高度让位 | 按面板堆总高 |
| 窗口最大化 / 还原时在两套高度间切换 | 去掉（只一套高度，靠压缩） |

渲染器内部（各种操作、锚点输入、锁定笔刷、颤音关联、分段轨）只经 `mDependency` 读激活键，换成面板实现后**无需改动**。
剪贴板（区域复制 / 粘贴取全部自动化、与激活无关）不受影响。

### 3.3 工程读写

- 读：`Editor.TryLoadProject` 与崩溃恢复在设置播放头之后还原面板布局；
- 写：`SaveToFile`、另存、`AutoSave`、CLI `project export`（`ProjectCommands`）构造 `EditorInfo` 的地方都带上布局；
- CBOR：`TuneLabProjectCbor.ReadEditorInfo` / `WriteEditorInfo` 增加两个键。

### 3.4 动作面、状态、文档

- 新增动作（终态、可带面板参数）：新增面板、关闭面板、设置面板收起、聚焦面板；`parameter.show/hideSynthesizedTrack`
  作用于焦点面板（可选面板参数）。按钮 / 右键菜单入口在 `docs/action-coverage.md` 认领（`ActionCoverageTests` 会检查）。
- 编辑器状态：`parameterPanelVisible` 保留（= 整个参数区是否展开），另报面板列表（个数、焦点、各自激活参数）。
- ScreenshotBot 改为设置焦点面板的激活参数；用户手册的参数面板一节补多开；改完界面重跑截图。

## 4. 分期

1. **重构不改行为**：抽出 `ParameterPanel`，`PianoWindow` 只持有一个面板，行为与现在完全一致（回归基线）。
2. **多开**：面板堆布局与压缩、焦点与色条、「+」、右键关闭、单面板收起。
3. **持久化**：`EditorInfo` 读写 + effect 参数认回规则；测试覆盖往返与三级匹配。
4. **外围**：动作面、编辑器状态、动作覆盖表、ScreenshotBot、手册与截图。

每期单独提交、各自点验；独立的测试文档 `tests/PARAMETER-PANELS-TEST-CASES.md` 随期补充。

## 5. 细节（已定）

- **单面板收起的入口**：标题栏左侧的折叠三角（与侧栏分组同款，全应用"收起"一种样子）。
- **最小高度**：压缩时每个展开面板至少保留 40。
- **整区收起时点「+」**：先恢复整个参数区，再加面板。
