# 动作面的覆盖率清单

动作注册表（`TuneLab/Input/ActionRegistry.cs`）是「外部够得着」这件事的**定义**。反过来说就有了一条可
机械检查的判据：**界面上一个入口，如果挂着内联处理器、没有任何对应的动作或命令，那它按定义就是外部
够不着的地方**。

这份清单把界面上的每一个入口逐个认领——要么指到一条动作/命令上，要么说清它为什么不该在那儿。
`tests/TuneLab.Tests/ActionCoverageTests.cs` 钉住它：新加一个按钮、改一个入口的绑定、或删掉一条被引用的
动作 id，表对不上就会红。**那不是障碍，是提问**：这个新入口，外部该不该够得着？

设计背景（两层注册表、`action list` / `run_action`、四道闸与授权分档）见
[docs/command-surface.md](command-surface.md) §5.5；手势那一半见 [docs/keybinding-system.md](keybinding-system.md)。

## 1 扫的是什么

四条机械规则（源码文本级，`TuneLab/**/*.cs`，不含 `TuneLab/Input/` 自身）：

| 规则 | 命中的东西 |
|---|---|
| `new MenuItem` 语句里有 `.SetAction(…)` | 菜单项（含右键菜单）。没有 `.SetAction` 的是容器项——点了只展开子菜单，不算入口 |
| 单独一行的 `<变量>.SetAction(…)` | 先声明、后绑定的菜单项或按钮（按该变量的 `new` 类型判种类） |
| `<变量>.Clicked += …` / `.Pressed += …` | 按钮。同一行里带 `AddButton(` 的算**对话框按钮**（模态里的一步，不是独立动作） |
| `<变量>.Switched.Subscribe(…)` | 开关（Toggle / CheckBox / Switch） |

`.SetAction("<id>")` 里的字面量 id 由扫描器**直接读出**，所以「这个入口绑的是哪条动作」不靠人写在表里，
表只负责那些**没有**绑定的入口该怎么算——这正是入口绑定要走
[`ActionBindings.SetAction`](../TuneLab/Input/ActionBindings.cs) 的第三个理由。

**扫不到的三类**（刻意的）：

- **画布上的拖动与按住修饰键**：那是操作态，不是动作（`keybinding-system.md` §0 已划界）。外部的正解是
  带参数的终态动作（"把这个音符移到 X"），不是模拟一次拖拽。
- **自绘的指针按钮**：用 `Border` + `PointerPressed` 手搓出来的按钮（今天只有一处：扩展条目的「卸载」）。
  把 `PointerPressed` 纳入规则会把整片画布交互一起卷进来，得不偿失；那一处的外部通道是
  `extension uninstall` 命令（与它旁边那个「取消卸载」菜单项成对）。
- **`.axaml` 里的 `Click="…"`**：今天是 0 处（界面全部代码构造）。真出现了，这条规则要补。

设置窗里那些设置控件（音频驱动、缓冲区、字体…）也不在规则内：它们是 controller 绑到 `Settings` 上的，
外部由 `setting list` / `setting set` 覆盖。

## 2 裁决词表

| 裁决 | 含义 |
|---|---|
| `action:<id>` | 进了动作注册表。这个入口按下去与快捷键、`run_action` 是**同一件事** |
| `command:<路径>` | 已由一条**带参数**的命令覆盖（`project export`、`script save`…）。动作面不再开一条无参入口，否则就是第二套写通道 |
| `script` | 改的是**工程数据**（随工程保存、进撤销栈）→ 写通道是脚本 API（`tl.*`）。判据见 issue #150 的那张表 |
| `pending:<桶>` | 归属已定，但那条通道**还缺这个能力**。桶见 §4，那就是「今天外部还够不着的地方」的全集 |
| `by-design` | **刻意不从外部驱动**，理由见 §3 |
| `dialog` | 模态框里的一步（确认 / 取消 / 关闭）。它属于某条已经被触发的动作，不是独立的一件事 |
| `internal` | 纯界面内务：点开一个下拉、切侧栏内部视图、禁用的占位项，以及 agent 面板自身（外部驱动它是自指） |
| `todo` | 应该进动作面、但还没进。**今天是 0 条**；这个词留着，好让下一批如实标记进度 |

## 3 刻意不收的（`by-design`）

- **音频导出**（导出混音、单轨导出音频、导出侧栏的导出按钮）：渲染要跑完整合成+混音+编码，期间界面
  必须锁住好几分钟（根因是渲染要求数据全程不变），而「要不要现在把这台机器占住」是用户的人在环决定。
  外部能做的是**把参数备好**——导出设置是工程数据，脚本面可写——最后一下由用户按。这与 `project export`
  拒绝干音频导出是同一条裁决（见 `ProjectExportCommand` 的注释），不要在动作面上绕过它。
- **关闭应用**：未保存确认必须人来答；而且真关掉之后连回报都送不出去（命令桥随进程一起没了）。
- **宿主内部剪贴板**（含"把用户刚复制的那份粘到别处"）：TuneLab 的剪贴板不是系统剪贴板，而是钢琴窗与
  编排区**各自持有的一份 Info 列表**（音符 / 颤音 / 参数曲线 / part）。而"复制这几个音符、粘到那个 part 的
  某个 tick"的**实质**，脚本面读写 info 早就能表达（读 `getInfo()`、往目标 `addNote(info)`）。真正缺的只是
  与"用户手边刚复制的那份"互操作，为它把两个视图的内部字段升级成对外契约不值得——还要额外定清"读出来
  是什么形状、粘贴走不走吸附与边界延展"。故这一族的裁决是 `script`：够得着的是结果，不是那个中间容器。
- **视口（缩放、滚动、视图定位）**：曾被当成「明显的洞」，**否决**。滚轮缩放是以鼠标位置为轴心的连续
  手势（操作态，同上），而外部没有鼠标；更要紧的是视口不改变任何结果，只改变用户此刻在看什么。
  真正有用的那件事是「让用户看到我改了哪里」，它要的是一个 tick / 一个对象作参数——那是带参面的形状，
  与剪贴板、选区写入并列（§4）。同类的成熟远控面同样不暴露视口，这一点可作旁证。
- **播放循环与区间**：不是覆盖率的洞——**TuneLab 今天没有循环播放这个功能**（`AudioEngine` 里没有）。
  动作面是「用户够得着的事」的镜像，不是新功能的入口；要循环得先在界面上有循环。

## 4 后续项（`pending:*` 的去处）

每个桶就是一处「判据已经清楚、通道还没补」的地方。**今天只剩一个**——剪贴板那 23 条改判成
`script`（§3），另外四个桶各自补上了通道：选区写入进了脚本面（`isSelected` / `part.selectNotes` /
`tl.setTrackSelection`），音频导入进了 `track.addPart`（不给 `endOffset` 就按文件时长），
打开工程与扩展装卸各成了命令（`project open` / `extension install` / `uninstall` / `cancel-uninstall`），
而 `selector-params` 那两条（外加侧栏那个钉选菜单项）由 `run_action` 的**选择器参数**接住：
`parameter.showSynthesizedTrack` / `hideSynthesizedTrack` / `pinProperty` / `unpinProperty` 各带一个
成员参数，`list_actions` 报出此刻的合法值（见 command-surface.md §5.5）。

| 桶 | 条数 | 该补在哪 |
|---|---|---|
| `pending:preset` | 5 | part preset 的外部面。设计已定、暂缓，要点钉在 issue #141 |

## 5 逐条认领

一行一个入口。**入口**列是这个文件里的稳定 key（菜单项取显示名字面量、按钮/开关取变量名；同名的按出现
次序加 `#2`），**不带行号**——行号天天变，那样这张表会因为无关改动天天红。

**169 个入口**：`action` 27 · `command` 9 · `script` 73 · `pending` 5 · `dialog` 34 · `internal` 17 · `by-design` 4 · `todo` 0

#### TuneLab/App.axaml.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| (chain) | dialog | `dialog` | 启动期对话框的按钮 |

#### TuneLab/Dialogs/AboutDialog.axaml.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| close | button | `dialog` | 关于框自己的按钮（关闭、外链） |
| b | button | `dialog` | 关于框自己的按钮（关闭、外链） |

#### TuneLab/Dialogs/UpdateDialog.axaml.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| button | button | `dialog` | 更新框自己的按钮 |

#### TuneLab/UI/ImportTrackSelector/ImportTrackSelector.axaml.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| closeButton | button | `dialog` | 导入轨选择框的确认/取消 |
| OkButton | button | `dialog` | 导入轨选择框的确认/取消 |

#### TuneLab/UI/LyricInput/LyricInput.axaml.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| closeButton | button | `dialog` | 歌词输入框的确认/取消 |
| OkButton | button | `dialog` | 歌词输入框的确认/取消 |

#### TuneLab/UI/MainWindow/MainWindow.axaml.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| binimizeButton | button | `action:app.minimize` |  |
| maximizeButton | button | `action:app.maximize` | 按钮是 toggle（最大化↔还原）= app.maximize / app.restoreWindow 这一对终态动作的组合 |
| closeButton | button | `by-design` | 关闭应用：未保存确认必须人来答，且关掉之后连回报都送不出去（命令桥随进程一起没了） |
| (chain) | dialog | `dialog` | 关窗前的保存确认 / 更新提示框的按钮 |
| (chain)#2 | dialog | `dialog` | 关窗前的保存确认 / 更新提示框的按钮 |
| (chain)#3 | dialog | `dialog` | 关窗前的保存确认 / 更新提示框的按钮 |
| (chain)#4 | dialog | `dialog` | 关窗前的保存确认 / 更新提示框的按钮 |

#### TuneLab/UI/MainWindow/Editor/Editor.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| settingsButton | button | `action:app.settings` |  |
| (chain) | dialog | `dialog` | 各确认框的按钮（存回原位覆盖、切工程前保存、更新提示） |
| (chain)#2 | dialog | `dialog` | 各确认框的按钮（存回原位覆盖、切工程前保存、更新提示） |
| (chain)#3 | dialog | `dialog` | 各确认框的按钮（存回原位覆盖、切工程前保存、更新提示） |
| (chain)#4 | dialog | `dialog` | 各确认框的按钮（存回原位覆盖、切工程前保存、更新提示） |
| (chain)#5 | dialog | `dialog` | 各确认框的按钮（存回原位覆盖、切工程前保存、更新提示） |
| New | menu | `action:file.new` |  |
| Open | menu | `action:file.open` |  |
| Save | menu | `action:file.save` |  |
| Save As | menu | `action:file.saveAs` |  |
| Save to Original Location | menu | `action:file.saveToOriginal` |  |
| Add Track | menu | `script` | project.addTrack |
| Import Audio | menu | `script` | track.addPart({type:"audio", path, pos})——不给 endOffset 时长度取音频文件本身的时长 |
| Import Track | menu | `script` | project.importTracks |
| format | menu | `command:project export` | 「导出为<工程格式>」的每一项 = 一个扩展名；那条命令按扩展名选格式，故整族都够得着 |
| Export Mix | menu | `by-design` | 音频导出刻意不从外部驱动（渲染期界面锁住数分钟、占不占机器是人在环决定；见 ProjectExportCommand 的同一条裁决） |
| Undo | menu | `action:edit.undo` |  |
| Redo | menu | `action:edit.redo` |  |
| User Manual | menu | `action:app.manual` |  |
| Open TuneLab Folder | menu | `action:app.openDataFolder` |  |
| Open Log | menu | `action:app.openLog` |  |
| Check for Updates... | menu | `action:app.checkUpdates` |  |
| About TuneLab | menu | `action:app.about` |  |
| mRecentFile.FileName | menu | `command:project open` | 「打开哪个工程」要一个路径参数，那是命令的形状（最近文件这一族本身是动态成员，但每一项都是「打开这个路径」） |

#### TuneLab/UI/MainWindow/Editor/ScriptInputWindow.axaml.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| closeButton | button | `dialog` | 脚本入参窗的确认/取消/重置 |
| resetButton | button | `dialog` | 脚本入参窗的确认/取消/重置 |
| cancelButton | button | `dialog` | 脚本入参窗的确认/取消/重置 |
| okButton | button | `dialog` | 脚本入参窗的确认/取消/重置 |

#### TuneLab/UI/MainWindow/Editor/ScriptToolMenu.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| item | menu | `command:script run-saved` | 菜单项触发的是动态注册的 script:<名> 动作；从外部走这条命令（能传脚本自己的参数、走预览-回退闸门） |

#### TuneLab/UI/MainWindow/Editor/FunctionBar/FunctionBar.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| playButton | toggle | `action:transport.play` | 按钮是 toggle，与 Space 同一条动作；动作面另有 transport.start / transport.pause 一对终态 |
| gotoStartButton | button | `action:transport.gotoStart` |  |
| gotoEndButton | button | `action:transport.gotoEnd` |  |
| toggle | toggle | `action:tool.note` | 5 个工具按钮共用这段构造，写的与 tool.* 动作是同一个 PianoTool 属性 |

#### TuneLab/UI/MainWindow/Editor/PianoWindow/ParameterArea/ParameterTitleBar.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| mWaveformToggle | toggle | `action:view.toggleWaveform` | 开关与动作都是切换：波形带显隐是一件事，人心里也是一件事 |
| toggle | toggle | `action:parameter.showSynthesizedTrack` | 合成参数轨显隐。chip 是 toggle，动作面给**一对终态**（show / hide），成员则是那两条动作的**选择器参数**——随 part 的声源与效果器链而变，逐成员开 id 会爆 |

#### TuneLab/UI/MainWindow/Editor/PianoWindow/ParameterTabBar/ParameterTabBar.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| mPanelToggle | toggle | `action:view.toggleParameterPanel` | 与 Ctrl+P 同一条动作 |
| Remove from Parameter Panel | menu | `action:parameter.unpinProperty` | 解钉（tab 右键）。与侧栏属性右键、与那条动作共用 `ParameterPinning.SetPinned`；解钉哪一个是它的选择器参数 |

#### TuneLab/UI/MainWindow/Editor/PianoWindow/PianoScrollView/PianoScrollViewOperation.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| Copy Selection | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Cut Selection | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Delete Selection | menu | `script` | 范围选区内的音符/参数：脚本面可读 tl.pianoSelection() 再删 |
| Paste | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Paste Notes | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Paste Pitch | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Paste Vibratos | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Paste Automations | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Notes | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Pitch | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Vibratos | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Automations | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| pronunciation | menu | `script` | note.pronunciation（发音候选） |
| Split | menu | `script` | 拆分音符 |
| Split by Phonemes | menu | `script` | 按音素拆分 |
| Lock Phonemes | menu | `script` | part.lockPhonemes |
| Clear Locked Phonemes | menu | `script` | 清固定音素 |
| Copy | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Cut | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Octave Up | menu | `action:note.octaveUp` |  |
| Octave Down | menu | `action:note.octaveDown` |  |
| Move Lyrics Forward | menu | `script` | 整段歌词前移一位 |
| Move Lyrics Backward | menu | `script` | 整段歌词后移一位 |
| Input Lyrics | menu | `script` | 批量写歌词（note.lyric） |
| Remove Overlaps | menu | `script` | 消重叠 |
| Delete | menu | `script` | removeNote |
| Paste#2 | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Copy#2 | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Cut#2 | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Delete#2 | menu | `script` | 删参数区选中的锚点/曲线段 |
| Paste#3 | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |

#### TuneLab/UI/MainWindow/Editor/SideBar/NameInputDialog.axaml.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| closeButton | button | `dialog` | 命名框的确认/取消 |
| okButton | button | `dialog` | 命名框的确认/取消 |

#### TuneLab/UI/MainWindow/Editor/SideBar/SideTabBar.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| toggle | toggle | `action:sidebar.showPart` | 6 个页签共用这段构造；按钮 toggle = sidebar.show* / sidebar.hide 这一对终态动作的组合 |

#### TuneLab/UI/MainWindow/Editor/SideBar/Agent/AgentSideBarContentProvider.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| menuToggle | button | `internal` | agent 面板自身的交互——外部驱动它是自指（外部就是那个 agent） |
| settingsButton | button | `internal` | agent 面板自身的交互——外部驱动它是自指（外部就是那个 agent） |
| mSendButton | button | `internal` | agent 面板自身的交互——外部驱动它是自指（外部就是那个 agent） |
| mStopButton | button | `internal` | agent 面板自身的交互——外部驱动它是自指（外部就是那个 agent） |
| mAttachButton | button | `internal` | agent 面板自身的交互——外部驱动它是自指（外部就是那个 agent） |
| ok | button | `internal` | agent 面板自身的交互——外部驱动它是自指（外部就是那个 agent） |
| back | button | `internal` | agent 面板自身的交互——外部驱动它是自指（外部就是那个 agent） |
| mSubmitButton | button | `internal` | agent 面板自身的交互——外部驱动它是自指（外部就是那个 agent） |

#### TuneLab/UI/MainWindow/Editor/SideBar/Export/ExportSideBarContentProvider.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| exportBtn | button | `by-design` | 音频导出刻意不从外部驱动（同上）；外部能做的是把导出参数备好（那些是工程数据） |
| selectAllBtn | button | `script` | track.exportEnabled |
| deselectAllBtn | button | `script` | track.exportEnabled |
| checkBox | toggle | `script` | track.exportEnabled / project.masterExportEnabled |
| channelSwitch | toggle | `script` | track.exportChannels / project.masterExportChannels |

#### TuneLab/UI/MainWindow/Editor/SideBar/Extensions/ExtensionDetailWindow.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| close | button | `dialog` | 详情窗的关闭 |
| Cancel Uninstall | menu | `command:extension cancel-uninstall` | 装 / 卸 / 撤销卸载三条都有了（装可即时生效，卸与重装受 dll 占用所限只能等重启） |

#### TuneLab/UI/MainWindow/Editor/SideBar/Extensions/ExtensionItemView.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| Cancel Uninstall | menu | `command:extension cancel-uninstall` | 同上 |

#### TuneLab/UI/MainWindow/Editor/SideBar/Extensions/ExtensionSideBarContentProvider.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| (chain) | dialog | `dialog` | 安装确认框的按钮 |
| (chain)#2 | dialog | `dialog` | 安装确认框的按钮 |

#### TuneLab/UI/MainWindow/Editor/SideBar/Properties/EffectsController.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| mAddButton | button | `internal` | 点开效果器类型菜单（真正的入口是菜单项那一行） |
| No effect installed | menu | `internal` | 禁用的占位项，点了什么也不会发生 |
| EffectManager.GetDisplayName | menu | `script` | part.addEffect |
| up | button | `script` | 效果器链重排：part 的 effects 顺序 |
| down | button | `script` | 同上 |
| remove | button | `script` | part.removeEffect |

#### TuneLab/UI/MainWindow/Editor/SideBar/Properties/NotePropertySideBarContentProvider.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| name | menu | `action:parameter.pinProperty` | 属性右键的钉选项（note / phoneme 两 scope 共用这段构造）。同一菜单项按当前钉选态在「在参数栏编辑 / 从参数栏移除」两句间切换，对应 `parameter.pinProperty` / `unpinProperty` 那一对终态动作 |
| Split | menu | `script` | 音素面板的右键项，改的是音素数据 |
| Delete | menu | `script` | 音素面板的右键项，改的是音素数据 |

#### TuneLab/UI/MainWindow/Editor/SideBar/Properties/PartPropertySideBarContentProvider.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| mPresetMoreButton | button | `pending:preset` | part preset 的外部面待定（设计已钉在 issue #141，暂缓） |
| mPresetButton | button | `pending:preset` | 同上 |
| Save As | menu | `pending:preset` | 同上 |
| Save | menu | `pending:preset` | 同上 |
| Rename | menu | `pending:preset` | 同上 |
| confirmButton | button | `dialog` | 覆盖确认框的按钮 |

#### TuneLab/UI/MainWindow/Editor/SideBar/Script/ScriptSideBarContentProvider.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| viewToggle | button | `internal` | 侧栏内部：代码面 / 文档面切换 |
| runButton | button | `command:script run` | 跑编辑器里那段脚本 |
| mMoreButton | button | `internal` | 点开脚本文件菜单（真正的入口是菜单项） |
| mScriptButton | button | `internal` | 点开脚本库下拉（选哪个脚本进编辑器 = 侧栏内部状态） |
| Open | menu | `internal` | 把某个脚本装进侧栏编辑器；脚本文件本身走 script read |
| Import | menu | `internal` | 从磁盘挑一个文件装进编辑器；入库走 script save |
| Save | menu | `command:script save` |  |
| Save As | menu | `command:script save` |  |
| Rename | menu | `command:script save` | 改名 = 换名字存一份 + script delete 旧的 |
| overwriteButton | button | `dialog` | 覆盖确认框的按钮 |
| ok | button | `dialog` | 确认框的按钮 |

#### TuneLab/UI/MainWindow/Editor/TimelineView/TimelineViewOperation.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| Edit Tempo | menu | `script` | 曲速/拍号是工程数据：project.setTempo / setTimeSignature / removeTempo / removeTimeSignature |
| Delete Tempo | menu | `script` | 曲速/拍号是工程数据：project.setTempo / setTimeSignature / removeTempo / removeTimeSignature |
| Edit Time Signature | menu | `script` | 曲速/拍号是工程数据：project.setTempo / setTimeSignature / removeTempo / removeTimeSignature |
| Delete Time Signature | menu | `script` | 曲速/拍号是工程数据：project.setTempo / setTimeSignature / removeTempo / removeTimeSignature |
| Add Time Signature | menu | `script` | 曲速/拍号是工程数据：project.setTempo / setTimeSignature / removeTempo / removeTimeSignature |
| Add Tempo | menu | `script` | 曲速/拍号是工程数据：project.setTempo / setTimeSignature / removeTempo / removeTimeSignature |

#### TuneLab/UI/MainWindow/Editor/TrackWindow/TrackHeadList/TrackHead.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| Export Audio | menu | `by-design` | 单轨音频导出：同「导出混音」的裁决 |
| Move Up | menu | `script` | project.removeTrack + insertTrack（重排保 id） |
| Move Down | menu | `script` | 同上 |
| colorItem | menu | `script` | track.color（12 个色块是同一段构造） |
| As Refer | menu | `script` | track.asRefer |
| Delete | menu | `script` | project.removeTrack |

#### TuneLab/UI/MainWindow/Editor/TrackWindow/TrackScrollView/TrackScrollViewOperation.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| Merge | menu | `script` | 合并 part |
| Copy Selection | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Cut Selection | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Delete Selection | menu | `script` | 范围选区内的 part：脚本面可读 tl.trackSelection() 再删 |
| Paste | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Copy | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Cut | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Rename | menu | `script` | part.name |
| Split | menu | `script` | 拆分 part |
| Merge#2 | menu | `script` | 合并 part |
| info.Name | menu | `script` | 换声源：part.soundSource |
| info.Value.Name | menu | `script` | 换声源：part.soundSource |
| info.Name#2 | menu | `script` | 加效果器：part.addEffect |
| info.Value.Name#2 | menu | `script` | 加效果器：part.addEffect |
| Remove Overlaps | menu | `script` | 消重叠 |
| Delete | menu | `script` | track.removePart |
| Import Audio | menu | `script` | 同「文件 → 导入音频」 |
| Import Track | menu | `script` | project.importTracks |
| Paste#2 | menu | `script` | 复制粘贴的**实质**（复制哪些对象、粘到哪个 part 的哪个 tick）脚本面读写 info 早就能表达；宿主内部剪贴板**刻意不暴露**（见 §3） |
| Delete#2 | menu | `script` | track.removePart |

#### TuneLab/UI/Manual/ManualWindow.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| close | button | `dialog` | 手册窗的关闭 |
| row | button | `internal` | 手册窗内部：跳到某一章（正文由 docs manual 命令给） |

#### TuneLab/UI/Settings/KeymapSettingsPage.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| (chain) | dialog | `dialog` | 快捷键设置页的确认框按钮 |
| (chain)#2 | dialog | `dialog` | 快捷键设置页的确认框按钮 |
| (chain)#3 | dialog | `dialog` | 快捷键设置页的确认框按钮 |

#### TuneLab/UI/Settings/SettingsWindow.ExternalAgent.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| copy | button | `internal` | 把外部 agent 的接入说明复制到剪贴板 |

#### TuneLab/UI/Settings/SettingsWindow.axaml.cs

| 入口 | 种类 | 裁决 | 说明 |
|---|---|---|---|
| closeButton | button | `dialog` | 设置窗的关闭 |
