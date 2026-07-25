# Agent 环境感知（只读）测试用例

覆盖 A 支柱首切片：让 agent 枚举宿主已装扩展、读 readme、枚举音源目录。三个只读工具
`list_extensions` / `get_extension_readme` / `list_sound_sources`。只测本切片，不复测编辑面/脚本库。

## 前置

- Agent 侧栏已连模型；打开任意工程。
- 环境里至少装一个 V1 扩展（有 manifest.json + 最好带 README.md）和/或有 legacy 扩展；至少一个已加载的 voice 或 instrument 引擎（含内建）。
- 若环境干净（无外部扩展），用例 1/2 验证"空/仅内建"分支也算通过。

## 1. list_extensions

1. 让 agent 调 `list_extensions`。
2. **期望**：逐条列出已装扩展，每条含 名/`id`/`v版本`/`Generation`(V1|Legacy)/`status`(Loaded|PartiallyLoaded|Skipped|Failed)/`kinds`(format/voice/instrument/effect/agent-model)/作者；加载失败/跳过的条目带 `note:`(错误原因)；带 README 的条目提示可调 `get_extension_readme(...)`。
3. legacy 扩展（无 id）**期望**显 `id=(legacy, no id)`，且 readme 提示用其名而非空 id。
4. 无任何扩展 → **期望**回报"only its built-in capabilities"，不报错。

## 2. get_extension_readme

1. 对用例 1 里某带 README 的扩展，`get_extension_readme("<id>")` → **期望**返回该 README 的 markdown 原文（按当前语言解析 `README.<lang>.md`，无则回退 `README.md`）。
2. 用**显示名**代替 id 调 → **期望**同样命中（匹配 id 优先、其次名，大小写不敏感）。
3. 对无 README 的扩展调 → **期望**"has no README file."。
4. 对不存在的名字调 → **期望**"no installed extension matches ... call list_extensions"。
5. 超长 README（>2 万字符）→ **期望**截断并注明剩余字符数，不淹没上下文。

## 3. list_sound_sources —— 引擎层（不给 engine）

1. `list_sound_sources`（无参）→ **期望**分 `Voice engines (N):` 与 `Instrument engines (M):` 两组，逐条 `"显示名" [type=<type id>, package=<包名>]`；多包提供同 type 显 `multiple: a, b`；末尾提示"Pass engine=<type id> to list an engine's individual sources."。
2. **不应触发引擎 Init**（列引擎只读注册表；用例 4 才 Init）——无明显卡顿/模型加载。
3. 空引擎 `type=""`（无音源回退）**不出现**在列表。
4. `list_sound_sources` 带 `kind:"voice"` → **期望**只列 voice 引擎；`kind:"instrument"` → 只列 instrument；非法 kind → 错误提示。

## 4. list_sound_sources —— 音源层（给 engine）

1. 取用例 3 里某 voice 引擎的 `type id`，`list_sound_sources({"engine":"<type>"})` → **期望** `Sources in voice engine "..." (type=...), K source(s):`，逐条 `<音源 id>  "<Name>" — <Description>`（**仅** Init 该引擎）。
2. instrument 引擎同理。
3. 引擎 id 不存在 → **期望**"no engine with type id ... call list_sound_sources (no engine)"。
4. 引擎存在但加载失败/不可用（`GetAllVoiceInfos` 返 null）→ **期望**"could not be loaded, so its sources are unavailable."，不崩。
5. 传 `engine:""` → **期望**拒绝（提示传真实 type id）。
6. 声库超大（>300 音源）→ **期望**列前 300 条并注明剩余数。

## 5. 端到端语义（推荐/识别，不切换）

1. 问 agent「我有哪些歌声音源可以用？」→ **期望**它先 `list_sound_sources`（引擎）再对目标引擎 `list_sound_sources(engine=...)`，用自然语言汇总推荐，**不臆造** id/名。
2. 问「当前这个 part 用的是什么音源？」→ **期望**它走 `run_script` 读 `part.soundSource()`（这些感知工具不覆盖"当前 part 用什么"）。

## 回归检查（不应被破坏）

- 三工具**只读**：调用后工程无改动、无新撤销项、不受分级授权闸门影响（任何授权档都直接返回，不弹升级卡片）。
- 既有工具（run_script/脚本库/闭环）不受影响；工具总数 12。
- 音源枚举在 UI 线程执行，Init 慢引擎时不跨线程崩、不冻死（与用户打开音源选择器同等开销）。
