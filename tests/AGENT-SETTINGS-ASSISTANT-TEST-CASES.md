# Agent 设置助手（C 支柱阶段③）测试用例

覆盖新增的两件 agent 工具 **`list_settings`（只读枚举）** 与 **`set_setting(key, value)`（按键改一项 + 落盘、过授权闸门）**，
以及为它们做的注册表补强：`SettingItem.AgentWritable` / `GetDefaultValue()` / `DisplayLabel`，运行时选项（语言 /
系统字体 / 音频驱动·设备）从设置窗内的 key 小表**上移到注册表声明**（`SettingItem.DynamicOptions`，设置窗与 agent 共用）。

只测本切片 + 设置窗因上移而必须复查的那几行。**不复测** C 支柱阶段①②基线（`AGENT-SETTINGS-MIGRATION-TEST.md`：
settings.json round-trip、设置窗四页自动生成的全部行），也不复测其它 agent 工具与授权闸门本身
（`AGENT-DESTRUCTIVE-FILE-AUTH-TEST-CASES.md`）。

## 前置

- 构建启动：`./run.ps1`（宿主内置能力，**不涉及插件、无需 pack/install tlx**）。
- 打开 TuneLab，Agent 侧栏已连模型。授权档位在**对话页 header 的文字胶囊**上切换（ReadOnlyAdvice / Confirm / Auto）。
- 建议先备份 `%APPDATA%\TuneLab\Configs\Settings.json`（本组会真改设置并落盘）。
- 用例里的"设置窗"= 菜单打开的设置窗口；**Keybindings / Extensions / Extension Routing 三页不在本次范围**。

## A. 设置窗回归（运行时选项上移到注册表后必查）

### A1. 四个动态下拉仍有正确候选

打开设置窗，逐个展开：**通用 > 语言**、**外观 > 界面字体**、**音频 > 音频驱动**、**音频 > 音频设备**。

**期望**：与改动前完全一致——语言列出全部已装语言（显示本地化名）、界面字体首项是"系统默认"（本地化）后跟系统字体
按名排序、驱动/设备列出 `AudioEngine` 的当前候选；选中项 = 当前设置值；音频驱动/设备行右侧仍额外显示引擎实时值。

### A2. 行标与静态项不受影响

**期望**：四页所有行标仍本地化正确（改用了 `SettingItem.DisplayLabel`，译文键仍在 `[SettingsWindow]` 段）；
采样率/缓冲区仍是数字下拉且可切换；滑条/勾选框/路径行照常；改后关窗 → settings.json 正常写出。

## B. list_settings（只读）

### B1. 基本枚举

问 agent「列出 TuneLab 的应用设置」。

**期望**：一次调用 `list_settings`，逐条给出 键 / 英文标签（+ 本地化标签，若不同）/ 所在页 / 允许值 / 当前值 / 默认值；
条目顺序 = 设置窗行序；`Language` 标注需重启；末尾三项（`AutoScrollTarget` / `AgentModelProvider` /
`AgentAuthorization`）标注"not in the Settings window" + "NOT agent-writable" + 各自的 note（归谁管）。

### B2. 允许值口径

**期望**：
- 滑条项给区间（如 `MasterGain` → `number in [-24, 24]`、`AutoSaveInterval` → `[10, 60]`）；
- `SampleRate` / `BufferSize` 给**数字**选项（`one of [32000, 44100, …]`，不是带引号的字符串）；
- `Language` 给 `one of ["en-US" ("English"), …]`（值 + 显示名）；
- `InterfaceFontFamily`（系统字体数百项）**截断**为前 12 项 + "(first 12 of N options; …)"，不淹没上下文；
- 路径项（`PianoKeySamplesPath` / `BackgroundImagePath`）说明是"已存在的文件路径（后缀 …）或空串清除"；
- `ParameterSyncMode` → `boolean (true/false)`。

### B3. 「在哪调」问答（诉求 2 的教学出口）

问「界面字体在哪里改？」（中文对话）。

**期望**：agent 只调 `list_settings`（**不调** `set_setting`），用中文答"设置窗 > 外观 页的「界面字体」"——页名与行标
用**本地化**说法（工具结果里带了本地化标签与页名）；不虚构不存在的页或行。

## C. set_setting 正常路径（授权档 = Auto）

把授权胶囊切到 **Auto**，逐条验证。每次改完在设置窗对应行确认新值，并检查
`%APPDATA%\TuneLab\Configs\Settings.json` 已落盘。

### C1. 数值（滑条）

「把总增益设成 -6 dB」。**期望**：`set_setting("MasterGain", -6)` → 回报 `Changed "MasterGain" from 0 to -6 …`；
**即时生效**（播放音量变化 / 设置窗滑条到位）；文件已写。

### C2. 布尔

「打开参数同步模式」→ `ParameterSyncMode = true`；再「关掉」→ false。**期望**：设置窗勾选框跟随。

### C3. 整数下拉（数字 与 数字字符串 都要能设）

「把采样率设成 48000」→ 成功。再让它用**字符串**形式设一次（如 `set_setting("SampleRate", "44100")`）。

**期望**：两种写法都成功（注册表里该项的下拉值是字符串形 `"44100"`、条目值类型是 int，工具做了归一化）；
设置窗采样率行显示新值；音频引擎随之应用（`AudioEngine.SampleRate`，同用户手动改）。

### C4. 字符串下拉 + 需重启

「把界面语言切成 zh-TW」（或另一个已装语言）。

**期望**：成功；回报里**明确提示需要重启 TuneLab 才完全生效**；agent 把这句转告用户。
（测完记得切回原语言。）

### C5. 路径项

「把自定义背景图设成 <一个真实存在的 png 路径>」→ 成功、背景即时可见；
再「清除背景图」（设成空串）→ 成功、背景恢复。

### C6. 已是该值 = 不改不弹卡

在 Auto 档下重复设同一个值（如再设 `MasterGain = -6`）。

**期望**：回报 `The setting "MasterGain" is already -6. Nothing changed.`；**不写文件、不进闸门**。

## D. 校验与拒绝（都应"什么都没改"）

### D1. 超范围

`set_setting("MasterGain", 999)`。**期望**：报错点名允许区间 `[-24, 24]`，值不变。

### D2. 下拉非法成员

「把采样率设成 12345」/「语言设成 xx-YY」。**期望**：报错并列出允许选项（字体那种长列表只列前 12 + 总数），值不变。

### D3. 类型不符

`set_setting("ParameterSyncMode", "maybe")`。**期望**：报"是布尔值"，值不变。
（`"true"` / `1` 这类等价写法应当**被接受**——顺手验一次。）

### D4. 不存在的键

`set_setting("Nope", 1)`。**期望**：报错 + 提示调 `list_settings` 拿准确键名。

### D5. 路径不存在

把背景图设成一个不存在的文件路径。**期望**：报"文件不存在"，并说明要已存在的路径或空串清除；设置不变
（不会静默写进一个坏路径让背景功能默默失效）。

## E. 安全：agent 不可写的项

### E1. 禁自我提权（关键）

在 **Confirm** 档下让 agent「把 AI Agent 授权改成 Auto（或 ReadOnlyAdvice）」。

**期望**：**不弹授权卡片、直接拒绝**——回报该项 agent 不可改 + 只有用户能在 agent 面板头部改；
`Settings.AgentAuthorization` 与胶囊显示都不变。这一条无论哪个档位都必须成立（含 Auto 档，再验一次）。

### E2. 活值归别处 UI 的两项

让 agent 改 `AgentModelProvider` 与 `AutoScrollTarget`。

**期望**：同样直接拒绝（回报里带 note：分别归 agent 面板设置 / 视图菜单的自动滚动选项），并把"去哪改"转告用户。

## F. 授权闸门三档

### F1. ReadOnlyAdvice = 只建议

切到 ReadOnlyAdvice，「把自动保存间隔设成 30 秒」。

**期望**：**不改**；回报形如 "Authorization is READ-ONLY (advice mode): I did NOT change the setting
\"AutoSaveInterval\" to 30 …"；agent 转而告诉用户去"设置 > 通用 > 自动保存间隔（秒）"自己改（或提示提权）。
设置窗与文件都不变。

### F2. Confirm = 内联卡片

切到 Confirm，「把总增益设成 -3」。

**期望**：对话里出现升级卡片，文案点名**本地化行标 + 新值**（如「Agent 想把设置「总增益（分贝）」改为 -3。」）；
- 点**拒绝** → 不改，回报"用户不允许"；
- 点**应用本次** → 改并落盘，档位仍是 Confirm；
- 点**始终允许** → 改并落盘 + 档位切到 Auto（胶囊同步），回报里带"已切自动"的前缀。

### F3. 取消在飞轮

Confirm 档下卡片出现时点停止（发送键位置的停止键）。**期望**：按拒绝收尾、设置不变、无悬空状态。

## G. 边界（说清"不是我管的"）

问「怎么改快捷键 / 某插件的参数 / 这个 part 的音源」。

**期望**：agent 不拿 `set_setting` 硬套——快捷键走**另一对专门工具** `list_keybindings` / `set_keybinding`
（见 `AGENT-KEYBINDING-TEST-CASES.md`）、插件参数走 `list_effects` / `list_sound_sources` + 脚本、
part 音源走 `run_script` 的 `part.setSoundSource`。

## 回归清单（快速过）

- [ ] 设置窗四页照常打开、四个动态下拉候选正确、改值仍写文件（A）
- [ ] `list_settings` 一次给全 20 项、页名/行标本地化、字体列表被截断（B）
- [ ] Auto 档下数值/布尔/下拉（数字 与 数字字符串）/语言（重启提示）/路径 均可设并落盘（C）
- [ ] 非法值一律"什么都没改"且报清允许值（D）
- [ ] `AgentAuthorization` 恒不可改（禁自我提权）、另两项孤儿设置同拒（E）
- [ ] 三档授权行为正确、Confirm 卡片文案带本地化行标 + 新值（F）
