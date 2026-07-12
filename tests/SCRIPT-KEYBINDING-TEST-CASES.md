# 脚本快捷键（Script Keybinding）测试用例

对应设计：`docs/keybinding-system.md §6`、`docs/script-tools-design.md §3`。本批只测**脚本命令接入快捷键系统**
新增范围（稳定 id / 建议默认手势 / context→scope 收窄 / 两个选区 context / 跨域提示 / TrackWindow 分发唤醒 /
增删同步），**不重测**键位基线（内建命令分发 parity、录制/冲突改派、override 往返落盘——见键位系统自身测试）。

## 前置

- 把 `tests/scripts/kb-*.js` 六个样例拷到 `%APPDATA%\TuneLab\Scripts\`：`kb-octave-up.js` / `kb-save-clash.js` /
  `kb-dupe-a.js` / `kb-dupe-b.js` / `kb-piano-range.js` / `kb-track-range.js`。启动调试版应用。
- 准备一个含至少一条 MIDI part、part 内有若干音符的工程；在钢琴窗打开该 part。
- 设置页「快捷键」标签的 **Scripts** 组里能看到脚本命令（显示的是 `getScriptInfo().name` **显示名**、非文件名）。
- 手势显示按平台：`mod` = Windows 的 `Ctrl+` / Mac 的 `⌘`。下表以 Windows 记。

| 文件 | 显示名（zh-CN） | context | 声明 id | 建议手势(Win) |
|---|---|---|---|---|
| kb-octave-up.js | KB 升八度 | global | `kb.octaveUp` | Ctrl+Shift+U |
| kb-save-clash.js | KB 撞保存键 | global | （无，用文件名） | Ctrl+S（撞内建） |
| kb-dupe-a.js | KB 重复 id A | global | `kb.dupe` | （无） |
| kb-dupe-b.js | KB 重复 id B | global | `kb.dupe` | （无） |
| kb-piano-range.js | 选区内音符升半音 | pianoSelection | （无，用文件名） | Ctrl+Shift+N |
| kb-track-range.js | 标记选区内 Part | trackSelection | （无，用文件名） | Ctrl+Shift+G |

---

## 1. 建议默认手势自动生效（空槽）

1. 设置页 Scripts 组，「KB 升八度」行手势应显示 **Ctrl+Shift+U**（未占用的键，自动采用声明的 defaultGesture）。
2. 焦点在编辑器（钢琴窗），按 **Ctrl+Shift+U** → 当前 part 全部音符升八度；Ctrl+Z 撤销。
3. **期望**：无需手动绑定即可触发（out-of-box）。

## 2. 稳定 id：重命名文件不丢绑定

1. 在设置页把「KB 升八度」改绑到 **Ctrl+Alt+U**（点手势芯片录制）。
2. 关闭应用，把 `kb-octave-up.js` 重命名为 `kb-octave-up-renamed.js`，重启。
3. **期望**：「KB 升八度」行仍显示 **Ctrl+Alt+U**（绑定锚在声明 id `kb.octaveUp`、与文件名无关）。按键仍生效。
4. 对照：对**无声明 id** 的 `kb-piano-range.js` 做同样重命名 → 其自定义绑定会丢（锚在文件名）。

## 3. 建议默认手势撞内建：确定性取胜 + 持久红警示（不夺键、不隐藏）

1. 「KB 撞保存键」声明 defaultGesture=`mod+s`，与内建 **保存**(File ▸ Save) 同为 Editor 域 Ctrl+S。
2. **期望**：冲突**双方都标红**——「KB 撞保存键」芯片 **「⚠ Ctrl+S」全红**，tooltip「与《保存》冲突；《保存》当前生效」；
   File 组的**内建《保存》**同样 **「⚠ Ctrl+S」全红**，tooltip「与《KB 撞保存键》冲突；此命令当前生效」（不再是静默"未绑定"）。
3. 按 **Ctrl+S** → 触发**保存**（内建注册序最小、恒胜），**不**触发脚本——脚本抢不走内建键。
4. **持久**：关闭再打开设置页，红警示仍在（非绑定当时一次性）。
5. 消解：给「KB 撞保存键」手动录别的键，或清除它——红警示消失。

## 4. 声明 id 碰撞：忠实降级回文件名

1. `kb-dupe-a.js` / `kb-dupe-b.js` 都声明 `id='kb.dupe'`。
2. **期望**：设置页 Scripts 组里**两行都在**（「KB 重复 id A」「KB 重复 id B」），可各自独立绑不同键、互不覆盖；
   日志有降级告警（id declared by more than one script → 回落文件名 `script:kb-dupe-a` / `script:kb-dupe-b`）。

## 5. 作用域 = 焦点子树（context→scope）

1. **global（Editor 域，编辑器内哪都可达）**：「KB 升八度」Ctrl+Shift+U 在钢琴窗焦点、编排区焦点下**都**触发。
2. **pianoSelection（PianoWindow 域）**：见 §6；其快捷键只在钢琴窗焦点时触发，编排区焦点时不响应。
3. **trackSelection（TrackWindow 域，本系统新唤醒的分发）**：见 §7；其快捷键只在**编排区焦点**时触发。
   - 验证 TrackWindow 分发确实被唤醒：先点一下编排区（夺焦），按 Ctrl+Shift+G（无选区时 main 空转、不报错即证分发到达）。
4. **不越界**：编排侧脚本键在钢琴窗焦点时不触发、反之亦然（内层子树遮蔽，互不串）。

## 6. 新 context：pianoSelection（钢琴窗选区右键 + 快捷键）

1. 在钢琴窗拖出一个**范围选区**（tick 带）。右键**落在选区带内** → 菜单底部分隔线下有 **选区内音符升半音**。
   点击 → 选区时间段内的音符各升半音；Ctrl+Z 撤销。
2. 右键**落在空白/音符**（非选区带）→ **不应**出现该项（只在命中选区带的分支挂）。
3. 快捷键：有选区时，钢琴窗焦点按 **Ctrl+Shift+N** → 同上效果（目标取 live 的 `tl.pianoSelection()`）。
4. **无选区**时按 Ctrl+Shift+N → main 空转（打印无选区、工程不变），不报错。

## 7. 新 context：trackSelection（编排区选区右键 + 快捷键）

1. 在编排区拖出一个**范围选区**（tick×轨道）。右键**落在选区内** → 菜单里有 **标记选区内 Part**。
   点击 → 选区 tick 段内起始的 part 名各追加 " ~"；Ctrl+Z 撤销。
2. 快捷键：有选区时，编排区焦点按 **Ctrl+Shift+G** → 同上效果（目标取 live 的 `tl.trackSelection()`）。
3. 无选区时按 Ctrl+Shift+G → 空转，不报错。

## 8. 跨域同手势：提示 + 持久弱标（不阻止、非冲突）

1. 设置页把「KB 升八度」（Editor 域）改绑到 **Ctrl+Shift+N**（该键是 pianoSelection「选区内音符升半音」的默认，
   但那是 **PianoWindow** 域，不同域）。
2. **绑定当时**：弹一个**单按钮**提示（说明该手势在另一区域也在用、以聚焦区优先），点 OK 后**绑定照样完成**——不拦截。
3. **持久**：关闭再打开设置页，「KB 升八度」芯片显示 **「⚠ Ctrl+Shift+N」全黄**，tooltip（悬停芯片）「另在《钢琴窗》也绑定…」（黄=共用提示，非红错误）。
4. 运行验证焦点遮蔽：钢琴窗焦点按 Ctrl+Shift+N → 触发**选区内音符升半音**（内层 PianoWindow 胜）；编排区焦点按
   Ctrl+Shift+N → 触发 **KB 升八度**（冒泡到 Editor）。
5. 对照同域**交互**冲突：把两个 **global** 脚本（都 Editor 域）绑到同一键 → 走**确认改派**（解绑原命令），当场消解。

## 10. 同域冲突的持久红警示（预防覆盖不到的路径）

同域冲突不止来自交互绑定（那条已被确认改派挡住），还来自**多脚本声明同默认**与**手改 Keymap.json**——这两类必须
被检测并持久展示，不能只靠绑定时那一下。

1. **多脚本同默认**：见 §3（kb-save-clash 撞内建 Save，同 Editor 域）——两行芯片都显示该键、冲突方 **「⚠ 键」全红**、
   tooltip 指明谁生效；关开设置页仍在。
2. **手改 JSON**：关闭应用，编辑 `%APPDATA%\TuneLab\Configs\Keymap.json`，手动给两个 **同域** 命令写上同一手势
   （如给 `script:kb.octaveUp` 和另一 global 脚本都写 `"ctrl+shift+j"`）。重启、开设置页。
   - **期望**：两行芯片都显示 **「⚠ Ctrl+Shift+J」全红**、tooltip「与《…》冲突；《…》生效」（生效者=注册序小者）。
   - 按 Ctrl+Shift+J（编辑器焦点）→ 触发**注册序小**的那个，确定不随机。
   - 任一行清除/改键 → 红警示消失。

## 9. 增删同步 + 孤儿 override 保留

1. 给「KB 升八度」绑一个自定义键。关闭应用，删除 `kb-octave-up.js`（或重命名的那个），重启。
2. **期望**：设置页 Scripts 组不再有「KB 升八度」行；`Keymap.json` 里其 `script:kb.octaveUp` 条目**静默保留**（不报错、不触发）。
3. 把脚本放回，重启 → 该行**复活**，自定义绑定仍在。

---

## 回归校验（本次改动波及、须确认未坏）

- 既有 6 个 SCRIPT-TOOLS 样例的**菜单行为**不变（`SCRIPT-TOOLS-TEST-CASES.md` 基线）：note/part/track/trackContent/
  partContent/global 各就各位、右键即选中、原子回退照旧。
- 编排区选区右键原有的 **Copy/Cut/Delete Selection、Paste**（`edit.*` 焦点路由）不变；新脚本项只是追加在其后。
- 钢琴窗选区右键原有的选区操作族不变；新脚本项追加在末。
