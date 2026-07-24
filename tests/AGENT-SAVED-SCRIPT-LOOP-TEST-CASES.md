# Agent 脚本闭环（get_script_inputs / run_saved_script）测试用例

覆盖 E1「全能 agent 闭环」：agent 读某已存脚本的入参 schema/上次值，并按名代跑它（可省入参），
走与 `run_script` 相同的分级授权闸门。只测本切片受影响范围，不复测既有 run_script/save_script/菜单基线
（那些在 `SCRIPT-TOOLS-TEST-CASES.md`）。

## 前置

- 开一个含至少一个 midi part、part 内有若干音符的工程；在钢琴窗选中几个音符。
- Agent 侧栏已连上模型。授权档默认 **Confirm**。
- 库内准备三个脚本（可让 agent 用 save_script 造，或手放进 `%APPDATA%/TuneLab/Scripts`）：
  - **`transpose`**（带入参、条件式）：
    ```js
    function getScriptInfo() { return { name: 'Transpose', context: 'note', id: 'transpose' }; }
    function getInputConfig(ctx) {
      const s = { mode: ComboBoxConfig.create(['transpose', 'setPitch']).withDefault('transpose') };
      if ((ctx.values.mode ?? 'transpose') === 'setPitch') s.targetPitch = SliderConfig.integer(60, 0, 127);
      else s.semitones = SliderConfig.integer(12, -24, 24);
      return s;
    }
    function main(inputs) {
      const notes = tl.currentPart().selectedNotes();
      if ((inputs.mode ?? 'transpose') === 'setPitch') for (const n of notes) n.pitch = inputs.targetPitch;
      else for (const n of notes) n.pitch += inputs.semitones;
    }
    ```
  - **`octave-up`**（工具脚本、无入参）：`getScriptInfo` + `main()`（选中音符 +12），无 `getInputConfig`。
  - **`plain-noop`**（普通脚本、无 getScriptInfo）：整段脚本体即动作（如给第一个音符 +1）。

## 1. list_scripts 标注带入参

1. 让 agent 调 `list_scripts`。
2. **期望**：`transpose` 行标 `[tool "Transpose", context=note] (takes inputs)`；`octave-up` 标 `[tool ...]` 无 `(takes inputs)`；`plain-noop` 标 `[plain]`。

## 2. get_script_inputs：带入参脚本

1. 让 agent 调 `get_script_inputs("transpose")`。
2. **期望**：逐字段列出 `mode`（one of ["transpose", "setPitch"]，default "transpose"）与 `semitones`（number in [-24, 24]，default 12）；
   条件字段按当前值（默认 mode=transpose）呈现 `semitones` 而非 `targetPitch`。
3. 先经**菜单**手动运行 `transpose` 一次并填 `semitones=5`（记入 ScriptInputMemory）。再让 agent 调 `get_script_inputs("transpose")`。
   **期望**：`semitones` 行带 `last used: 5`。

## 3. get_script_inputs：无入参 / 不存在

1. `get_script_inputs("octave-up")` → **期望**回报"takes no inputs"，不报错、不 eval 出假字段。
2. `get_script_inputs("plain-noop")` → **期望**同样"takes no inputs"。
3. `get_script_inputs("nope")` → **期望**"no script named ... call list_scripts"。

## 4. run_saved_script：省入参（用上次/默认）

1. 确保 `transpose` 上次值为 `semitones=5`（用例 2.3 已设）。授权设 **Auto**。
2. 让 agent 调 `run_saved_script("transpose")`（不带 inputs）。
3. **期望**：选中音符 +5（用了用户上次值）；一个可撤销单位（Ctrl+Z 全撤）。
4. **政策校验**：再经菜单打开 `transpose` 入参窗 → **期望初值仍是 5**（agent 代跑**没有**把它自己用的值回写成用户上次值）。

## 5. run_saved_script：agent 给入参覆盖 + 条件字段

1. 授权 **Auto**。让 agent 调 `run_saved_script("transpose", { "mode": "setPitch", "targetPitch": 72 })`。
2. **期望**：schema 依 `mode=setPitch` 重算 → `targetPitch` 生效，选中音符 pitch 全设为 72（而非按 semitones 平移）。
3. 只给部分字段：`run_saved_script("transpose", { "semitones": -12 })`（mode 省略）→ **期望** mode 回落默认 transpose、选中音符 −12。
4. 政策再验：以上两次代跑后，菜单入参窗初值**仍为 5**（不被 agent 的 setPitch/72 或 −12 覆盖）。

## 6. 授权闸门一致性（与 run_script 同闸门）

对 `run_saved_script("transpose", {"semitones": 3})` 分别在三档下验证：

1. **ReadOnlyAdvice**：回报"WOULD apply N edit(s) but NOTHING was changed"，工程无改动、无撤销项。
2. **Confirm**：对话里弹**内联升级卡片**（"apply N change(s)"）；
   - 点「拒绝」→ 回报未应用、工程不变；
   - 点「应用本次」→ 落地一个可撤销单位、档位仍 Confirm；
   - 点「始终允许」→ 落地 + 授权胶囊切到 **Auto**，回报含"switched authorization to auto-apply"。
3. **Auto**：直接落地，回报"Applied N edit(s) as one undoable change"。

## 7. run_saved_script：普通脚本 / 不存在

1. `run_saved_script("plain-noop")`（无 getScriptInfo）→ **期望**正常跑其脚本体、落一个可撤销单位；即便误带 `inputs` 也无害地不生效、**不**触发 getInputConfig eval（用户正拖动编辑时也不因写守卫误报 getInputConfig 失败）。
2. `run_saved_script("nope")` → **期望**"no script named ..."。

## 回归检查（不应被破坏）

- **run_script 仍走同一执行器**：内联 run_script 在三档授权下行为不变（预览/确认/直提交、blocked wait-retry、出错回退文案"All changes were rolled back…"）。
- **菜单/快捷键手动运行不受授权闸门约束**：菜单跑 `transpose` 仍弹入参窗、直接落地（授权只管 agent）。
- **稳定 id 共享**：菜单侧与 agent 侧用同一 `ScriptTools.StableId`——`transpose` 声明了 `id:'transpose'`，菜单填入参写入的 last value 能被 agent 的 get_script_inputs 读到（同一记忆键）。改脚本文件名但保持 `id` 不变，上次值不丢。
