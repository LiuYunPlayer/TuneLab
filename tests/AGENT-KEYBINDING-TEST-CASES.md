# Agent 快捷键能力（D 支柱）测试用例

覆盖新增的两件 agent 工具 **`list_keybindings`（只读枚举 + 手势语法 + 冲突标注）** 与
**`set_keybinding(id, gesture?/reset?/replaceConflict?)`（绑/解绑/恢复默认，过授权闸门）**，
以及配套的 `AgentWriteKind.KeybindingChange`（授权卡片新文案 + `SecondaryTarget` 点名被夺键者）。

只测本切片。**不复测**快捷键系统本身（`KEYBINDING-*` / 设置窗「快捷键」页的录制、重置、冲突芯片等）与
授权闸门基线（`AGENT-DESTRUCTIVE-FILE-AUTH-TEST-CASES.md`）。

## 前置

- 构建启动：`./run.ps1`（宿主内置能力，不涉及插件、无需 pack/install tlx）。
- 打开 TuneLab，Agent 侧栏已连模型；授权档位在对话页 header 胶囊上切换。
- 建议先备份 `%APPDATA%\TuneLab\Configs\Keybindings.json`（本组会真改绑定并落盘）。
- 测完可用设置窗「快捷键」页顶部的**全部重置默认**一键复原。
- 若要测脚本命令绑键（G 组），先让 agent 用 `save_script` 存一个带 `getScriptInfo` 的工具脚本。

## A. list_keybindings（只读）

### A1. 基本枚举

问「TuneLab 有哪些快捷键」。

**期望**：一次调用 `list_keybindings`，头部给出命令总数 + **手势语法说明**（`ctrl+/alt+/shift+/cmd+` + 键令牌，
含 `mod+` 别名说明）+ **作用域语义**（同域=冲突、跨域=按焦点共存）+ 指路「设置窗 > 快捷键页（有搜索框和逐行重置）」；
逐条为 `<id> "<本地化名>" [作用域]: ctrl+z (Ctrl+Z), default`。顺序与设置窗一致（首次注册序，file→edit→… ）。

### A2. 查询过滤

问「撤销的快捷键是什么」。**期望**：agent 带 `query` 调用（或全量后自行定位），答出 `edit.undo` = `Ctrl+Z`，
并能说出它在 `Editor` 作用域。**不要**编造不存在的命令。

### A3. 用户改过的项与无默认项

先在设置窗手动把某命令改成一个别的手势（如 `transport.play` 改成 `ctrl+alt+p`），再问 agent 那个命令的快捷键。

**期望**：报当前手势 + 标注 `changed by the user (default <原默认>)`；未绑定的命令报 `(unbound)`；
无默认手势的命令（脚本命令未声明 defaultGesture 时）标 `no default`。

## B. set_keybinding 正常路径（授权档 = Auto）

每次改完在设置窗「快捷键」页确认那一行，并检查 `%APPDATA%\TuneLab\Configs\Keybindings.json`。

### B1. 绑一个空闲手势

「把『切换参数面板』绑到 Ctrl+Alt+7」（或任意空闲手势）。

**期望**：`set_keybinding("view.toggleParameterPanel", "ctrl+alt+7")` → 回报 `Bound "…" to ctrl+alt+7 (Ctrl+Alt+7). Saved; it works right away`；
**立刻按下该组合就生效**（无需重启）；设置页那一行手势芯片同步、并出现「重置↺」。

### B2. mod+ 别名

让它用 `"mod+alt+8"` 绑另一个命令。**期望**：接受，落盘为**物理修饰**（Windows 上 = `ctrl+alt+8`），
回报里给的是解析后的实际手势。

### B3. 解绑

「把刚才那个快捷键取消掉」→ `gesture: ""`。**期望**：回报 `Removed the shortcut for "…"`；设置页该行显示未绑定；
原组合按下无反应。

### B4. 恢复默认

对 A3 里手动改过的命令说「恢复默认快捷键」→ `reset: true`。

**期望**：回报 `Reset "…" to its default shortcut: <默认>`；设置页那行「重置↺」消失（不再是 override）。

### B5. 幂等（不改不弹卡）

重复绑同一个手势 / 对已是默认的命令再 `reset` / 对未绑定的命令再解绑。

**期望**：分别回报 `already bound to …` / `already at its default …` / `has no shortcut already`，
**都是"Nothing changed"、不写盘、不进闸门**（Confirm 档下也不弹卡片）。

## C. 校验与拒绝（都应"什么都没改"）

### C1. 非法手势

`set_keybinding("edit.copy", "ctrl+shift+喵")` / `"ctrl+"` / `"foo+z"`。

**期望**：报错并**回灌完整手势语法**（模型据此自纠）；绑定不变。

### C2. 不可绑的键

试 `"ctrl+capslock"`（或其它未收录键）。**期望**：报"该键不可绑" + 语法说明；绑定不变。

### C3. 不存在的命令 id

`set_keybinding("nope.nope", "ctrl+9")`。**期望**：报错 + 提示调 `list_keybindings`，并提到脚本命令是
`script:<id>` 且可能要等应用识别到文件。

## D. 同域冲突（关键：不许悄悄夺键）

### D1. 默认拒绝

「把『复制』绑到 Ctrl+Z」（`edit.copy` 与 `edit.undo` 同为 Editor 域）。

**期望**：**拒绝**，回报点名占用者（`"撤销" (id edit.undo)`）+ 所在作用域，并告知两条出路：换手势
或 `replaceConflict = true`（会解除对方绑定）。**此时不弹授权卡片**（连闸门都没进），`edit.undo` 与 `edit.copy` 都不变。

### D2. 显式夺键

在用户明确要求下让 agent 带 `replaceConflict: true` 再来一次。

**期望**：
- **Confirm 档**：卡片文案两句 ——「Agent 想把「复制」的快捷键设为 Ctrl+Z。」+「这会同时解除「撤销」的快捷键绑定。」
  （第二句来自 `SecondaryTarget`，是知情同意的关键）；
- 应用后：`edit.copy` = Ctrl+Z，`edit.undo` **变为未绑定**，回报明确说了「"撤销" 失去该快捷键、现在未绑定 —— 告知用户」；
- 设置页两行都同步；按 Ctrl+Z 触发的是复制（可撤销地验证一下即可，之后**全部重置默认**复原）。

### D3. 持久冲突的如实标注

若当前存在同域撞键（可手改 `Keybindings.json` 造一个，或用 D2 的中间态），问 agent「Ctrl+Z 现在是什么功能」。

**期望**：`list_keybindings` 那两行都带 `CONFLICT: …` 标注、说明只有一个生效（内建/注册序靠前者胜）并建议改绑其一 ——
与设置页红 ⚠ 的口径一致，不隐藏、不误判成"正常"。

### D4. 跨域同手势不算冲突

把某 `PianoWindow` 域命令绑成与某 `Editor` 域命令相同的手势。

**期望**：**允许**（不拒绝），但回报里附一句「另有命令在其它区域也用此手势 —— 两者都有效、以聚焦区域优先」；
`list_keybindings` 不把它标成 CONFLICT。

## E. 授权闸门三档

### E1. ReadOnlyAdvice

切 ReadOnlyAdvice，让它改某个快捷键。**期望**：不改；回报 "I did NOT set the shortcut for the command \"…\" to …"；
agent 转而告诉用户去设置窗「快捷键」页自己改（页名用用户语言）。

### E2. Confirm

三个按钮各验一次：拒绝 → 不改；应用本次 → 改、档位不变；始终允许 → 改 + 档位切 Auto（胶囊同步）。
解绑动作的卡片文案应是「Agent 想解除「…」的快捷键绑定。」（不是"设为"那句）。

## F. 「教用户自己改」（诉求 2 的教学出口）

问「我想把撤销改成别的键，在哪改？」（中文对话）。

**期望**：agent 指路「设置窗 > 快捷键页」，提到那页**有搜索框**、点手势芯片即可录制、有逐行重置；
不擅自调 `set_keybinding`（除非用户说"你帮我改"）。

## G. 闭环：脚本 + 快捷键（诉求 1 的最后一环）

让 agent「写一个把选中音符升八度的工具脚本，存进库，并绑到 Ctrl+Alt+9」。

**期望**：`save_script` 存下 → 该脚本作为命令 `script:<稳定 id>` 出现在 `list_keybindings`
（若刚存下还没出现，agent 应重列一次而不是报错收场）→ `set_keybinding` 绑上 → **按 Ctrl+Alt+9 真能跑那个脚本**；
脚本命令的作用域符合其 context（piano 侧 → PianoWindow、编排侧 → TrackWindow、global → Editor）。

## 回归清单（快速过）

- [ ] `list_keybindings` 给全命令 + 手势语法 + 作用域语义 + 用户改过/无默认/未绑定标注（A）
- [ ] 绑（含 `mod+`）/解绑/恢复默认均生效并落盘、即时可按、幂等不弹卡（B）
- [ ] 非法手势/不可绑键/未知 id 一律"什么都没改"且回灌语法（C）
- [ ] 同域冲突默认拒绝、夺键需显式且卡片点名被解绑者、持久冲突如实标注、跨域不误报（D）
- [ ] 三档授权行为正确、解绑与设为两套卡片文案（E）
- [ ] 会指路设置窗「快捷键」页而非硬改（F）
- [ ] 脚本 → 命令 → 绑键 一路走通（G）
