# Agent 音源参数 schema（list_sound_sources 第三层）测试用例

覆盖 A 支柱 A4：`list_sound_sources` 加第三层——给 `engine`+`source` 读某 voice/instrument 音源的参数 schema
（part/note/自动化/音素级）。只测本层；引擎/音源枚举（第一、二层）在 AGENT-ENVIRONMENT-AWARENESS，不复测。

## 前置

- Agent 侧栏已连模型；打开任意工程。
- 至少一个**已加载的 voice 引擎**且其下有音源（用 `list_sound_sources`、`list_sound_sources(engine=...)` 先拿到 type id + source id）。
- 若有 instrument 引擎+音源更好（验 instrument 分支，无音素/歌词）。

## 1. voice 音源参数 schema

1. 取某 voice 引擎 type id `E` 与其下音源 id `S`，调 `list_sound_sources({"engine":"E","source":"S"})`。
2. **期望**：`Parameters for voice source "<名>" (S) in engine "<显示名>" (type=E):`，其下按引擎声明列出（有哪组取决于引擎）：
   - `Part properties (N):` 逐字段 `<id>(+标签): <类型/范围/选项>. default <值>`。
   - `Part automation tracks (editable) (M):` 逐轨 `<id>: automation track, range [min,max], default <值>`（或 `piecewise (no baseline)`）。
   - `Read-only readback tracks (K):`（若有回显轨）。
   - `Note properties (P):`（note 级属性，空选中→默认版）。
   - `Phoneme properties`：**多数引擎（含 DiffSinger）此处不会有内容**——phoneme schema 的 slot 来自 note 里真实音素（数据驱动），空 note 拿不到。**期望**如实标注 `Phoneme properties: not available from this static read — this engine declares them per actual phoneme, so they only appear once a note with a real lyric is synthesized ...`，而**非**造假 note 硬凑。只有恰好静态声明 slot schema 的引擎才会列出 `core vowel (slot 0)` 等小节。
   - 末尾附注 `(Schema is at default values; ... Editing these is not yet scriptable.)`。
3. **仅 Init 该引擎**（不 Init 其它）。schema 为「默认值版」——条件化 schema 只呈现默认分支（预期上限）。
4. 音源不暴露任何参数 → **期望** `This source exposes no custom parameters (at default values).` + 附注。

## 2. instrument 音源参数 schema

1. 对某 instrument 引擎+音源调 `list_sound_sources({"engine":"E","source":"S"})`。
2. **期望**：同 voice 但**无 Phoneme 组**（instrument 无音素/歌词），其余 part/note/automation/readback 组照常。

## 3. voiceId 依赖 / 未知 id 保护（关键）

1. `list_sound_sources({"engine":"<voice E>","source":"不存在的 id"})` → **期望**报错 `no source "..." in voice engine "E" (or the engine could not load). Call list_sound_sources with engine="E" ...`——**不**返回一个"空 schema"（因 manager 对未知 id 会静默回退空引擎，本工具用 TryGet*Info 先校验，避免误导性空结果）。
2. 若环境里同一 voice 引擎下**两个音源声明不同参数**：分别读两者 schema → **期望**各自不同（验证 schema 是 voiceId 的函数、按音源而非按引擎）。

## 4. 参数缺失 / 错误输入

1. 只给 `source` 不给 `engine`：`list_sound_sources({"source":"S"})` → **期望**报错 `"source" needs "engine" — a source id belongs to an engine. ...`。
2. `engine` 存在但 `source` 属于另一 kind（如把 instrument 的 source 传给 voice engine）→ 命中"no source in that engine"分支。
3. `engine` 不存在 → `no engine with type id ...`（第二/三层共用的引擎校验）。
4. 某组声明方法抛异常 → 该组标 `(engine failed to declare — <msg>)`，其余组照常（SchemaText 内 try/catch）。

## 5. 端到端语义

1. 问「XX 音源有哪些可调参数？」→ **期望** agent 先 `list_sound_sources`(引擎)→(音源)→(engine+source 读参数)，自然语言汇总，不臆造参数名/范围。
2. 问「帮我把 XX 音源的某参数设成 Y」→ **期望**说明"改音源参数目前不可脚本化"，不假装改了。

## 回归检查（不应被破坏）

- **第一、二层不变**：`list_sound_sources`(无参/仅 engine) 行为与格式与 A2 一致。
- **effect 无回归**：`list_effects` 的参数输出（现共用 `SchemaText.AppendProperties/AppendAutomations`）格式与重构前一致。
- **脚本入参无回归**：`get_script_inputs` 的 config 文本化（共用 `ConfigText`）不变。
- 只读、不落 undo、不受授权闸门；工具总数仍 13（A4 折进 list_sound_sources，未加新工具）。
- schema 求值在 UI 线程；只 Init 目标引擎、合成 context 不建工程/不挂真 part。
