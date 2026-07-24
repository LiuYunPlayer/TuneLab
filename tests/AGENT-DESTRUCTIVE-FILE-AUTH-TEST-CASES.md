# Agent 破坏性外部文件操作授权测试用例

覆盖：`delete_script`（恒）与 `save_script` 覆盖已存脚本（仅覆盖）纳入分级授权闸门。
动机——历史记录管理器只保工程数据，**保不了外部文件**（脚本库文件删/覆盖不可 Ctrl+Z）。
与工程写共用 `Settings.AgentAuthorization` + 同一确认卡片，但**无预览-回退**（文件不能试运行）。
只测本切片，不复测工程写授权（那在既有 run_script 授权用例）。

## 前置

- Agent 侧栏已连模型；库内先存一个脚本 `demo`（内容随意，最好是工具脚本，便于看菜单增减）。
- 授权档可在对话页 header 胶囊三选一切换。

## 1. delete_script —— 三档行为

对 `delete_script("demo")` 分别在三档：

1. **Auto**：直接删除；回报 `Deleted script "demo".`；库文件消失、菜单项消失。
2. **ReadOnlyAdvice**：**不删**；回报 `Authorization is READ-ONLY … I did NOT delete the saved script "demo". Do it yourself, or raise agent authorization…`；文件仍在。
3. **Confirm**：对话里弹**内联升级卡片**，文案 `The agent wants to delete the saved script "demo". This can't be undone.`：
   - 点「拒绝」→ 回报 `The user chose NOT to allow it, so I did NOT delete …`；文件仍在。
   - 点「应用本次」→ 删除；档位仍 Confirm。
   - 点「始终允许」→ 删除 + 授权胶囊切 **Auto**，回报含 `switched authorization to auto-apply`。

## 2. save_script —— 新建不拦、覆盖才拦

1. 授权 **Confirm**。`save_script("newthing", <code>)`（库内不存在）→ **期望**直接保存、**不弹卡片**（新建是加性、非破坏）。
2. 再 `save_script("newthing", <改过的 code>)`（现已存在）→ **期望**弹卡片 `The agent wants to overwrite the saved script "newthing". This can't be undone.`：
   - 拒绝 → 不覆盖、旧内容保留、回报未覆盖。
   - 应用本次 → 覆盖、回报 `Updated script "newthing". …`。
3. 授权 **ReadOnlyAdvice** + 覆盖已存 → **期望**不覆盖、回报 READ-ONLY 建议、旧内容保留。
4. 授权 **Auto** + 覆盖 → 直接覆盖。

## 3. 预校验先于授权

1. 授权 **Confirm**。`save_script("demo", <声明了 getScriptInfo 但语法/求值出错的 code>)` → **期望**：先回 `getScriptInfo failed to evaluate …`、**不弹授权卡片**、`demo` 原内容不动（不为坏脚本打扰用户裁决）。

## 4. 卡片种类文案正确

- 工程写（run_script 改音符）弹的卡片仍是 `apply N change(s) to the project`。
- 删/覆盖脚本弹的卡片是点名脚本 + `This can't be undone`。
- 三者共用同一套按钮（应用本次/始终允许/拒绝）与"始终允许→切 Auto"语义。

## 回归检查（不应被破坏）

- **读类工具不受闸门**：`read_script`/`list_scripts`/`list_extensions`/`get_extension_readme`/`list_sound_sources`/`get_script_inputs`/`get_project_overview` 在任何授权档都直接返回，**从不弹卡片**。
- **工程写授权不变**：run_script / run_saved_script 的预览-回退 + 三档行为与本次改动前一致（confirm 回调签名换成 `AgentAuthorizationRequest` 但 ProjectEdit 分支语义不变）。
- **取消（点停）**：删/覆盖卡片未裁决时点停 → 卡片切"Stopped"、按拒绝收尾、文件不动。
- **无 UI 兜底**：若无确认回调（理论上非侧栏路径），Confirm 档下删/覆盖保守地不做、回报未做。
