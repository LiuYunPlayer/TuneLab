# Agent effect 感知（只读）测试用例

覆盖 A 支柱 A3：让 agent 枚举 effect（音频效果器）引擎，并读某引擎的参数 schema。单个只读工具 `list_effects`。
只测本切片；不复测音源/插件感知（那在 AGENT-ENVIRONMENT-AWARENESS）。

## 前置

- Agent 侧栏已连模型；打开任意工程。
- 环境里至少装一个 effect 插件引擎（effect 无内建，全来自插件）。若环境无任何 effect 插件，用例 1 验证"空"分支也算通过。
- 已知某 effect 引擎的 `type id`（用例 1 会列出）。

## 1. list_effects —— 引擎层（不给 engine）

1. 让 agent 调 `list_effects`（无参）。
2. **期望**：`Effect engines (N):` 逐条 `"显示名" [type=<type id>, package=<包名>]`；多包提供同 type 显 `multiple: a, b`；末尾提示 `Pass engine=<type id> to see an engine's parameters.`。
3. **不应触发引擎 Init**（列引擎只读注册表）——无明显卡顿/模型加载。
4. 环境无任何 effect 插件 → **期望** `No effect engines are installed. Effects come from plugins ...`，不报错。

## 2. list_effects —— 参数 schema 层（给 engine）

1. 取用例 1 里某 effect 引擎的 `type id`，`list_effects({"engine":"<type>"})`。
2. **期望**：`Effect engine "..." (type=...):`，其下按组列出（有哪组取决于引擎声明）：
   - `Static properties (N):` 逐条 `<参数 id>(+标签): <类型/范围/选项>. default <值>`（number in [min,max] / one of [...] / boolean / text 等）。
   - `Automation parameters (editable tracks) (M):` 逐条 `<轨 id>: automation track, range [min, max], default <值>` 或 `…, piecewise (no baseline)`（分段轨）。
   - `Read-only synthesized parameter tracks (K):`（若引擎有回显轨，如 loudness）同上格式。
3. **仅 Init 该引擎**（不 Init 其它）。schema 是"默认值版"（空 context → 各参数取引擎默认）——条件化 schema 只呈现默认分支，属预期上限。
4. 引擎存在但加载失败（`GetInitedEngine` 返 null）→ **期望** `could not be loaded, so its parameters are unavailable.`，不崩。
5. 引擎某组声明方法抛异常 → **期望**该组标 `(engine failed to declare — <msg>)`，其余组照常输出（不整体失败）。
6. 引擎不暴露任何参数 → **期望** `This effect exposes no parameters (or none at default values).`。

## 3. 错误输入

1. `list_effects({"engine":"nope"})`（不存在的 type）→ **期望** `no effect engine with type id "nope". Call list_effects (no engine)…`。

## 4. 端到端语义（推荐/解释，不编辑）

1. 问 agent「我装了哪些效果器？各有什么参数？」→ **期望**它先 `list_effects`（引擎）再对目标 `list_effects(engine=...)`，用自然语言汇总，**不臆造** 参数名/范围。
2. 问「帮我把某 effect 的某参数调成 X」→ **期望**它说明"读写 effect 链目前不可脚本化/尚未支持"，不假装改了（当前无 tl effect 写通道）。

## 回归检查（不应被破坏）

- `list_effects` **只读**：调用后工程无改动、无撤销项、不受授权闸门影响。
- **共享格式重构无回归**：`list_sound_sources`（引擎层用同一 `EngineCatalog`）与 `get_script_inputs`（config 文本化用同一 `ConfigText`）输出格式与重构前一致（number in [..]/one of [..]/boolean/text、default、last used 等措辞不变）。
- 工具总数 13；effect 参数枚举在 UI 线程执行，Init 慢引擎时不跨线程崩。
