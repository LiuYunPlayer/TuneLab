# 术语表 —— 一个概念一个词

本仓的命名基准。**新增 API / 新写文档前先查这张表**：同一个概念在 SDK、宿主数据层、UI、agent 工具面、
脚本动作面、中英文档里必须用同一个词；发现同义词并存就是缺陷，按这里的裁决收敛。

## 为什么需要它

同义词的代价不是"读着别扭"，而是三条实打实的：

1. **模型会猜错**。脚本动作面与工具面是 LLM 的操作台：它在 `list_sound_sources` 里读到一个词、在脚本 API
   里遇到另一个词，就会猜出既不存在的第三个名字（`lockPhonemes` 与 `pinPitch` 就是这么来的）。
2. **搜不到 context**。改一处功能要 grep 两三个词才能捞全现场，漏掉的那个就是下次的 bug。
3. **用词跑偏会带着理解跑偏**。按用途起的名（"回显"）在用途扩展时会失准，而按事实起的名（"合成参数"）不会。

## 裁决规则

- **SDK 公共面是锚**。`TuneLab.SDK` / `TuneLab.Foundation` 的公共签名已冻结（见 `sdk-api-evolution.md`），
  改不动 ⇒ 其余各面一律向它收敛，反向不可能。
- **按事实命名，不按用途命名**。用途会增加（同一份产物既显示、又能被固定），事实不会变。
- **英文符号面严格唯一**；中文允许一个**上位词**存在，但不得用它指代某个具体类型（见下表"回显"一行）。
- 发现新的同义词对：先判哪个是事实/哪个是用途，再全仓收敛，然后**在这里加一行**。

## 术语

| 概念 | 唯一英文符号 | 中文 | 已废弃 / 不要再用 |
|---|---|---|---|
| 把只读的引擎产物固化成归用户的可编辑数据 | `Lock*`（`LockPhonemes` / `SynthesisLock` / `lockPitch` / `lockAutomation`） | 固定 | `pin`（脚本面 `pinPhonemes` 已改名；数据层残留 `HasPinnedPhonemes` 属历史，勿扩散） |
| 引擎在合成时发布的只读参数曲线（及其声明） | `SynthesizedParameter` / `GetSynthesizedParameterConfigs` / `hasSynthesizedParameter` | 合成参数（轨） | `readback`（符号面已清空） |
| 引擎发布的只读音高曲线 | `SynthesizedPitch` | 合成音高 | — |
| 引擎发布的只读音素 | `SynthesizedPhoneme` / `SynthesizedSyllable` | 合成音素 | — |
| 宿主替插件弹出的提示 / 确认 / 进度 / 输入窗（按钮点击期间、画布实例有效期内） | `IDialogService`（经 `IButtonClickContext.DialogService` / `ICanvasHost.DialogService`；宿主侧 `HostDialogService`） | 弹窗服务 | `GetDialogs` / `IHostDialogs` / 属性名 `Dialogs`（读起来像"取一组对话框窗口"，已改名）；不挂在全局 `ITuneLabContext` 上（只在点击期间 / 画布实例有效期内可用） |
| 向用户索取几个参数的表单（脚本工具与插件按钮共用一个表单窗） | `inputs`（脚本 `getInputConfig` / 插件 `IDialogService.PromptInputs`；窗口 `PropertyFormWindow`） | 输入 | `ShowForm`（描述的是界面形态不是意图）；`RequestInputs` / `AskInputs` |
| 画布矩形的位置与边 | `Rect.X` / `Y` = 位置（起点）；`Left` / `Top` / `Right` / `Bottom` = 四条边 | 位置 / 边 | 不是同义词：两个概念，只因界面坐标原点在左上、y 朝下，X 与 Left、Y 与 Top 才取值相等。说"放在哪"用 X / Y，说"边到哪"用 Left…Bottom |
| 画布绘图里画一个形状：按给的填充与描边画 / 只填充 / 只描边 | `Draw*` / `Fill*` / `Stroke*`（`ICanvasDrawing.DrawGeometry` 与 `CanvasDrawingExtensions`） | 画 / 填充 / 描边 | 用 `Draw*` 专指描边；只描边的叫 `Stroke*`，`DrawLine` / `DrawPolyline` 只因线本无填充才叫 Draw |
| 画布上就地出现、不模态的轻量输入（一段文字 / 从几项里挑一项） | `Popup*`（`ICanvasHost.PopupTextBox` / `PopupMenu`） | 弹出 | `Prompt*`（Prompt 留给模态表单 `PromptInputs`；两者的区别正在形态——就地轻量 vs 模态表单，故此处按形态命名，与上一行"别按形态命名"不矛盾） |
| 插件宣告它此前给出的声明（`Get*Config` 族）已过期、请宿主重新询问 | `Invalidate*`（`InvalidatePart` / `InvalidateNotes`；宿主侧 `DeclarationsInvalidated` / `DeclarationInvalidation`） | 声明失效 | `Notify*Changed`（读成「数据变了」，而数据归宿主、插件改不了）；`*Configs` 后缀（作废范围按对象划，不按 config 划） |
| **上位概念**：引擎产物被显示出来这件事（涵盖音素 / 音高 / 参数三种） | —（无符号，勿造） | 回显 | 不得用"回显"指代 `SynthesizedParameter` 这一具体族 |
| 合成机器类（会话 / 上下文 / 引擎 / 快照） | `VoiceSynthesis*` / `InstrumentSynthesis*` 中缀 | — | 见 `sdk-api-evolution.md` 的命名约定 |
| 引擎对某 note 的音素**表没表态**（"没有音素"也是答案） | `HasPhonemeAnswer`（宿主数据层，显示门控判据） | 表态 | 别拿 `HasPhonemeContent` 当它用 |
| 不开窗口、只跑宿主逻辑的进程 / 工程 | `headless`（`HeadlessHost` / CLI 的 `--headless` / 探测沙箱的可丢弃工程） | 无头 | 「无界面」「无窗口」（`TuneLab.Setup` 的"无界面安装"是另一回事——静默安装，不在此列） |
| 让用户挑一条路径（文件**或**文件夹）的控件及其声明 | `PathPicker`（GUI 控件）/ `PathPickerConfig`、`PathPickerTarget`（SDK） | 路径选择 | `PathInput`（旧控件名，已改名）；`FilePicker*`（不准确——同一个控件也选文件夹，Avalonia 那边 `FilePicker*` / `FolderPicker*` 才是分立两族） |
| 用户在界面上能做的**一次操作**，及其穷尽注册表 | `action`（`EditorAction` / `ActionRegistry` / `ActionKind` / 命令面的 `action list`、`action run`） | 动作 | 别用 `command` 指它（那个词归命令面）；`KeyCommand` 已改名 `KeyBindingEntry`（它现在只是"引用某动作的一条绑定"） |
| 命令面上一条**外部可调条目**（CLI / agent 工具 / MCP 的同一个末端） | `command`（`ICommand` / `CommandRegistry` / `CommandKind`） | 命令 | 「末端动作」（已改口"末端命令"）；别用 `action` 指它 |
| 吸附网格的档位（1/16、三连的 1/12…） | `quantization`（`IQuantization` / `QuantizationBase` / `QuantizationDivision` / 动作 id `quantization.*`） | 量化 | 符号面别写 `snap` / `grid`（模型可见的句子里可以用 "the snap grid" 作解释性同位语，如 `editor status` 那行；**符号与 id 一律 quantization**） |
| 一条动作要作用在哪个成员上（一个动词 × 一个闭集里的成员） | `argument`（`ActionParameter` / `ActionArgument` / `run_action` 的 `argument` 字段），那一批统称**选择器参数** selector parameter | 选择器参数 | 别写 `option` / `value` / `target` 指它；命令自己的入参仍叫 `parameter`（`ParametersJsonSchema`），动作的这一个是 `argument`——两个词分工见 command-surface.md §5.6 |
| 右侧那条可开合的容器 + 它的页签 | `sidebar`（动作 id `sidebar.*` / `editor status` 的 `sidebars` 字段 / `SideTabBar`、`SideBarTab`） | 侧栏 | 「侧边栏」「抽屉」；容器叫**侧栏**，里面显示的那一块叫**面板**（`Part Panel`…），两个词别互换 |
| 属性面板里由插件自己绘制、自己处理鼠标的值字段 | `CanvasConfig` / `ICanvas*`（SDK）；`PluginCanvas`（GUI） | 画布 | 「自定义 UI」「自定义控件」（太宽：插件自己的独立界面是另一回事，见插件文档 §9.3）；`handle`（不给插件原生窗口句柄） |
| 一个逻辑像素对应的物理像素数（随屏幕缩放） | `DevicePixelRatio`（SDK `ICanvasHost`） | 设备像素比 | `RenderScaling`（Avalonia 的叫法，宿主只在翻译层碰它）、`Scale` / `DpiScale`（含糊：没说清是谁比谁） |
| 鼠标 / 键盘事件携带的修饰键集合（跨平台归一：macOS 的 Cmd 记作 Ctrl） | `ModifierKeys`（SDK `CanvasModifierKeys`；GUI `TuneLab.GUI.Input.ModifierKeys`），事件上的属性名 `Modifiers` | 修饰键 | `KeyModifiers`（那是 Avalonia 的物理按键枚举、带 Meta，语义不同；宿主只在翻译层碰它） |
| 插件宣告"正在编辑"（锁住面板不刷新、实例不被换掉；与值无关，与草稿同时起作用） | `BeginEdit` / `EndEdit`（SDK `ICanvasHost`，计数可嵌套）；作用域写法 `EditScope()`（扩展方法） | 编辑状态 / 宣告编辑 | `Lock` / `Hold`（易误解成锁数据）；单独的 `Edit()`（动词，读着像"执行一次编辑"） |
| 返回 `IDisposable`、`Dispose` 即结束的便利写法 | `*Scope()`（如 `EditScope`），做成扩展方法、不进接口 | 作用域写法 | 与原语同名的动词（`Edit()`）；`Defer*`（描述的是宿主那边的效果） |
| 从一组固定候选里挑若干个值（有序、不重复）的字段及其候选 | `TagConfig` / `TagItem`（SDK）；`TagController`（GUI） | 标签 | `MultiSelect*`（「多选」在本仓指同时选中多个音符/片段，撞词）；`ChipConfig`（描述的是界面形态不是语义）；候选别复用 `ComboBoxItem`（两者各自演进） |
| 把一条常驻参数（声源 / 效果器的自动化轨、时间轴画布）从标签栏拿掉（数据照常参与合成） | `ParameterHiding` / `SetHidden` / `IsHidden`；动作 `parameter.hideFromTabBar` / `showInTabBar`；插件声明的默认值 `AutomationConfig.ShownByDefault` / `WithShownByDefault` | 从标签栏隐藏 / 在标签栏显示 | `Visible` / 可见（那是面板里的小眼睛：作背景画不画，按面板记）；`Pin` / 钉选（那是把原本不在的可钉属性放上来，另一类存储） |
| 一段数值区间（只是起点 + 终点，不承载别的含义；单位写在用它的地方） | `Range`（Foundation，属性 `Start` / `End`；**暂缓**，等真有这种用处再加）。与 `System.Range` 撞名，届时插件与宿主一律写别名 `using Range = TuneLab.Foundation.Range;` | 区间 | `Interval`（音乐里是音程）、`TimeRange`（绑死时间）、`NumberRange` 等加修饰的变体、`Span`（撞 `Span<T>`）、`Segment`（已指音频承载段 / 状态段）。**可能不止起止的概念给自己的类型**、别拿 `Range` 替：如 `SynthesisRange`（描述"下一块合成"，以后可能长字段） |

### 三条边界的说明

**`command` vs `action`**：命令面搬家时把裸名 `Command` 判给了命令面（撤销栈那族改叫 `*UndoCommand`，见
`command-surface.md` §2），而快捷键系统那边一直把"用户能做的一件事"叫 `KeyCommand`。动作面（issue #150）
一到，同一个工具面上 command 既指"一条可调命令"又指"一件能做的事"，模型必然猜出第三个名字。裁决：
**command = 命令面的一条条目**（`action run` 自己就是一条 command），**action = 用户在界面上能做的一次操作**
（`transport.play`）。`keybinding` 只管手势，措辞里的 command 已全部改成 action。

**中文「动作面」目前指两样东西**（发现于 issue #150 二期，尚未收敛，**别再产出新用法**）：
`agent-tools.md` / `script-inputs-and-action-surface.md` 里的"单一动作面"说的是**脚本面**（`tl`）——重点在
"编辑工程只有这一条写通道"；而 issue #150 之后的"动作面"指的是 `ActionRegistry` 那一面（用户在界面上
够得着的每一件事）。两者是**不同的轴**：前者管工程数据，后者管应用与编辑器自身的状态。新文档里：
`tl` 那面写**脚本面**、`ActionRegistry` 那面写**动作面**；旧文档的措辞下次动到时顺手改。

**`lock` vs `pin`**：数据层的动作一直叫 `LockPhonemes`，UI 叫"固定笔刷"，只有状态属性 `HasPinnedPhonemes`
和早期脚本面用了 `pin`。脚本面是模型的操作台，同一范式在那里出现两个动词最伤，故统一到 `lock`
（`note.lockPhonemes` / `note.hasLockedPhonemes`）。数据层内部那个 `HasPinnedPhonemes` 未动——它不在动作面上，
改它要扫 UI 与 legacy compat 一大片；但**新代码不要再产出 pin**。

**`synthesized parameter` vs `readback`**：`readback`（回显）说的是"把它显示回来给用户看"——那是用途之一，
而"固定"功能一来就多了第二种用途；更糟的是它字面暗示"读回用户写进去的东西"，可这条曲线恰恰是引擎自己算的
（用户覆盖段只是其中一部分）。`SynthesizedParameter` 命名的是事实，且是 SDK 冻结名。故符号面、模型可见文案
（`list_sound_sources` / `list_effects` 的 `Read-only synthesized parameter tracks`）、脚本 API 全部用它。

中文保留"回显"仅因它是好用的**上位词**：音素留白→回显、音高回显、参数回显都说得通。真正指那族只读参数轨时
写"合成参数轨"。

**授权「天花板」= `ceiling`**（别写 `cap` / `limit` / 「上限」）：命令桥上，实际档位 = 客户端声明与
`Settings.AgentAuthorization` 取更严的一档，那个用户设定就叫**天花板**（`AuthorizationModes.Stricter`
是取更严那一档的判据）。英文面（CLI 提示、设置 description、模型看得到的一切）统一写 `ceiling`；
`--yes` 之类的声明叫 `declared`。「上限」在这个仓库另有主人（`AgentMaxToolResultChars` 那类**数量**上限），
两个概念共用一个词会让人以为档位也能调数值。

**`HasPhonemeAnswer` vs `HasPhonemeContent`**（`INote`，刻意并存的两个近义判据，别互相替换）：
`HasPhonemeContent` = **有没有音素几何**（钉死列表非空，或回填音节的音素数 > 0），用于挑 3-note 布局窗口的
内容邻居——零音素的 note 推挤不到任何人，入窗无意义。`HasPhonemeAnswer` = **引擎表没表态**（钉死，或
`SynthesizedSyllable != null`，哪怕它零音素），用于显示门控——引擎回了"这个 note 没有音素"就是终态答案，
邻居的布局边界已确定、该照常显示；只有引擎**根本没提**这个 note（脏 / 合成中）才是真的未决、邻居才跟着留白。
拿 `HasPhonemeContent` 当门控判据用，就会把"明确无音素"误判成"还没合成"，让一个空 note 连累相邻 note 一起
消失（老引擎的延音符正是这个形态）。三态语义见 `IVoiceSynthesisSession.SynthesizedPhonemes` 的契约注释。
