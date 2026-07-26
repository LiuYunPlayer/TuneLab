# Agent 扩展设置测试用例

覆盖新增的 **`list_extension_settings`（只读枚举扩展自己的设置 + 字段 schema/当前值）** 与
**`set_extension_setting(extension, key, value)`（改一格 + 落盘 + 立即回喂，过授权闸门）**，
以及**密钥政策：只读不回灌 + 禁写**（`IsPassword` 字段只报 set/not set、拒绝代写）。

只测本切片。**不复测**扩展设置系统本身（`PLUGIN-SETTINGS-TEST-CASES.md` / `EXTENSION-CLASSES-AND-SETTINGS-ISOLATION-TEST.md`：
设置窗扩展页渲染、按包分桶隔离、密钥加密存储等）与其它 agent 工具。

## 前置

**夹具已为本组扩过（首轮 6 项未覆盖就是被夹具限制卡住的，现已补齐）**，需要重新部署一次：

1. **关闭 TuneLab**；
2. 我已 build + pack 好，直接装：
   `pwsh tests/install-tlx.ps1 V1.Settings V1.Instrument V1.VoiceConflict.A V1.VoiceConflict.B`
3. `./run.ps1`。

- **主夹具**：`V1 Engine Settings Demo`（包 id `com.tunelab.test.v1settings`，engine id `TLSettingsDemo`，
  源码 `tests/plugins/V1.Settings`，**voice** 类）。设置窗「扩展」页显示名 = 「V1 Engine Settings Demo」
  （agent 的 `extension` 参数填显示名、`voice:TLSettingsDemo`、或裸 id 都行）。字段（照代码，勿凭记忆）：
  - `model_path` "Model Path" — 普通文本；
  - `api_key` "API Key" — **密钥**（`WithPassword()`）；
  - `use_gpu` "Use GPU" — 勾选框，默认 false；
  - `gpu_device` "GPU Device" — 普通文本，**仅当 `use_gpu`=true 才存在**（动态 schema）；
  - `threads` "Threads" — **整数滑条 1~16**，默认 4（← 新增，覆盖 D1 超范围）；
  - `precision` "Precision" — **下拉 fp32/fp16/int8**（← 新增，覆盖 D2 非法成员）；
  - `advanced` "Advanced" — **分组字段**（内含 `verbose` 勾选框）（← 新增，覆盖 D6 复杂字段）。
- **instrument 夹具**：`V1 Test Instrument` 现在也声明了扩展设置（`tuning_hz` 滑条 415~466，默认 440）
  → 覆盖 E2（验证"instrument 类此前完全没被收集"那个修复）。
- **同 id 跨包夹具**：`Voice Conflict A` / `B` 两个包**共用同一个 voice 引擎 id**（`TLConflictVoice`），
  现在两边各声明了一个 `variant` 字段 → 覆盖 A4（必须按 `packageId` 消歧）。
- 打开 TuneLab，Agent 侧栏已连模型；授权档位在对话页 header 胶囊上切。
- 备份 `%APPDATA%\TuneLab\Configs\ExtensionSettings.json`（本组会真改并落盘）。
- 先在设置窗「扩展」页看一眼该插件的字段（名称/类型/当前值），作为对照基准。

## A. list_extension_settings（只读）

### A1. 列出有设置的扩展

问「哪些插件有自己的设置？」

**期望**：`list_extension_settings`（无参）列出 `kind:extensionId`、显示名、来源包（含 packageId）、字段数；
末尾指路「用户可在设置窗『扩展』页编辑」。没有任何插件声明设置时明说，并**区分清**「插件自己的设置」
与「插件的 part/note 参数」（后者走 list_sound_sources / list_effects）。

### A2. 列某扩展的字段

问「V1.Settings 有哪些设置项？」

**期望**：列出 `model_path`（text）、`api_key`（密钥，见 A3）、`use_gpu`（boolean，默认 false）、
`threads`（`number in [1, 16]`，默认 4）、`precision`（`one of ["fp32", "fp16", "int8"]`）、
`advanced`（`object (grouped fields)`），各带键(+标签)/类型/当前值/默认值；未设过的字段标
`(unset, so the default applies)`；与设置窗「扩展」页所见一致。末尾提示字段可能随其它值动态显隐。

### A2b. 动态字段随值出现（本夹具专有）

`use_gpu` 还是 false 时列一次 → **不应出现** `gpu_device`。让 agent 把 `use_gpu` 设成 true（B2）后再列一次
→ **`gpu_device` 出现**。这验证 schema 是"当前值的函数"、每次现求而非缓存。

### A3. 密钥字段只报有无（关键，`api_key`）

**期望**：密钥字段一行形如 `secret text — currently SET / NOT set (value hidden; the agent cannot read or write it …)`；
**回灌里绝不出现明文**（展开那次工具结果亲自确认）。先在设置窗填一个值、再问一次 → 应从 `NOT set` 变 `currently SET`，
但**仍不显示内容**。

### A4. 同 id 跨包并存要消歧

`Voice Conflict A` / `B` 两包共用 voice 引擎 id `TLConflictVoice`、现在**各声明了一份设置**，故 A1 里
`voice:TLConflictVoice` 应出现**两条**（packageId 分别是 `com.tunelab.test.voiceconflict.a` / `.b`）。

问「Conflict Voice 的设置是什么」（只给 id、不给包）。

**期望**：**不猜**——报错说明该 id 由多个包提供、要求传 `packageId` 并列出两个候选；带 `packageId` 后正常列出
（A 包的 `variant` 标签是 "Variant (Package A)"、B 包是 "(Package B)"，一眼看出读的是哪个包的桶）。
再对**其中一个包**写 `variant`（Auto 档）→ 只有那个包的桶被改，另一个包的值不动
（`ExtensionSettings.json` 里两个 packageId 桶各自独立）。

## B. set_extension_setting 正常路径（授权档 = Auto）

每次改完在设置窗「扩展」页确认那一行，并检查 `ExtensionSettings.json` 的
`root[<packageId>][<kind:extensionId>]` 桶。

### B1. 文本字段（`model_path`）

「把 V1.Settings 的模型路径设成 D:\models\foo」。

**期望**：成功，回报 `Changed "model_path" of "…" from … to … and saved it; the extension was handed the new settings immediately`
+ 「引擎可能只在下次启动时读取」的提醒；设置窗那行显示新值；日志出现 `[V1.Settings] ApplySettings: model_path='D:\models\foo', …`
（**立即回喂**的可观测点，插件自己会打）。

### B2. 布尔字段（`use_gpu`）

「打开 V1.Settings 的 Use GPU」→ true；再关掉 → false。**期望**：都成功；设置窗勾选框跟随；
日志的 `use_gpu` 跟着变；开启后 `gpu_device` 字段出现（见 A2b）。

### B3. 动态字段（`gpu_device`）

在 `use_gpu=true` 时设 `gpu_device`。**期望**：成功。随后把 `use_gpu` 设回 false → 该字段从 schema 消失，
此时再设 `gpu_device` 应**报"没有这个字段"**（schema 是当刻值的函数，不该允许写不存在的字段）。

### B4. 幂等

重复设同一个值。**期望**：`already …. Nothing changed.`，**不写盘、不弹卡**。

### B5. 其它字段不被破坏（重要）

先在设置窗把 `api_key` 填上一个值，再让 agent 依次改 `model_path` / `use_gpu`，然后检查
`ExtensionSettings.json` 该桶：**先前设过的其它字段全都还在**
（写路径是"读全量 → 改一格 → 整桶重写"，若有丢字段即为 bug）；**密钥字段也仍在**（Load 解密 → Save 重新加密的往返），
回到设置窗看密钥仍是"已设置"状态、且插件仍能用它。

## C. 密钥禁写（政策底线）

### C1. 直接拒绝

「把 V1.Settings 的 API key 设成 sk-xxxx」。

**期望**：**直接拒绝、不弹授权卡片**（连闸门都没进）；回报说明这是密钥字段、agent 不允许代写，
并让用户自己去设置窗「扩展」页填；`ExtensionSettings.json` 与该密钥**都不变**（尤其不能被清空）。
Auto 档下也必须拒绝 —— 复验一次。

### C2. 不因拒绝而泄露

**期望**：拒绝的回报里同样**不含**任何已存密钥的明文或片段。

## D. 校验与拒绝（都应"什么都没改"）

- **D1 超范围**：`threads` 设成 99（或 0） → 报错点名允许区间 `[1, 16]`，值不变；设 8 应成功（正例对照）。
  另：`"8"`（数字字符串）也应被接受。
- **D2 下拉非法成员**：`precision` 设成 `fp8` → 报错并列出 `["fp32", "fp16", "int8"]`，值不变；设 `fp16` 成功。
  大小写写错（`FP16`）应被**宽容接受**（归一到声明写法）。
- **D3 类型不符**：给 `use_gpu` 传 `"maybe"` → 报"是布尔"；（`"true"` / `1` 应被**接受**，顺手验一次）。
- **D4 不存在的字段键**：如 `"modelpath"` → 报错并列出真实字段键。
  注意大小写写错（`Model_Path`）应被**宽容接受**（归一到声明写法）。
- **D5 不存在的扩展** → 报错 + 提示先 `list_extension_settings`。
- **D6 分组字段**：设 `advanced` → 明说这是分组/复杂字段、不能在这里设，引导用户去设置窗「扩展」页；
  设 `verbose`（组内子字段）也应报"没有这个字段"（本工具只认顶层字段，不做路径寻址）。

## E. 授权闸门

- **ReadOnlyAdvice**：不改；回报 "I did NOT change the extension setting \"<扩展名 → 字段>\" to …"；
  agent 转而指路设置窗「扩展」页。
- **Confirm**：卡片文案「Agent 想把扩展设置「<扩展名> → <字段键>」改为 <值>。」；
  拒绝→不改；应用本次→改、档位不变；始终允许→改 + 切 Auto。

## E2. 顺手修的宿主漏洞：instrument 的扩展设置此前完全没被收集

`ExtensionSettingsManager.GetEntries()` 原先只收 effect + voice，漏了 instrument ——
声明了 `IExtensionSettings` 的 instrument 插件**既不在设置窗「扩展」页出现、也拿不到 `ApplyPersisted` 回喂**。
本切片补了一行。

**验证**（`V1 Test Instrument` 现已声明 `tuning_hz` 滑条 415~466、默认 440）：
- 设置窗「扩展」页**出现**该 instrument 插件的设置区（此前完全没有）；
- `list_extension_settings` 列出它（`instrument:<id>`）、可读可写；
- 改一个值 → 重启 → 插件的 `ApplySettings` 收到持久值（打日志观察）；
- `ExtensionSettings.json` 出现 `instrument:<id>` 桶（此前从未写过，无迁移问题）；
- **回归**：effect / voice 的设置区与已存值**一切照旧**（顺序上 instrument 追加在后）。

## F. 边界：agent 不配置自己的模型连接

问「把 agent 的 API key / 模型 provider 改成 X」。

**期望**：`list_extension_settings` **看不到** `agent-model:*`（它不在设置窗扩展页、不在 `GetEntries()` 里），
`set_setting` 也拒改 `AgentModelProvider`（`AgentWritable=false`）；agent 应引导用户去 **agent 侧栏的设置（齿轮）**
自己配置，而不是硬试或编造工具。

## 回归清单（快速过）

- [ ] 列扩展/列字段与设置窗一致，未设字段标 unset；动态字段随值出现/消失（A1/A2/A2b）
- [ ] 密钥字段只报 SET/NOT set，明文从不出现在上下文（A3/C2）
- [ ] 文本/布尔/动态字段均可设并落盘 + 日志见立即回喂、幂等不弹卡（B1–B4）
- [ ] 改一格不破坏其它字段与已存密钥（B5）
- [ ] 密钥字段一律拒写、不弹卡、不清空（C1）
- [ ] 超范围/下拉非法/类型不符/未知字段/未知扩展/分组字段 一律"什么都没改"（D1–D6 现已全可测）
- [ ] 同 id 跨包必须按 packageId 消歧、两个包的桶互不串味（A4）
- [ ] 三档授权行为正确、卡片文案点名"扩展名 → 字段"（E）
- [ ] instrument 的扩展设置现在能被收集（设置窗出现 + agent 可读写 + 重启回喂），且 effect/voice 照旧（E2）
- [ ] agent-model 的连接设置对这对工具不可见、引导去侧栏（F）
