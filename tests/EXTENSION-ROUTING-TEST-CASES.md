# Extension Routing（扩展冲突消解）测试用例

测范围：同一扩展身份（format 扩展名 / voice·effect·agent 引擎 id）被**多个安装包**提供时的并存加载、
活实现解析（用户选择 / 确定性默认）、选择持久化、工程引用路由、缺包回退、设置页按包枚举。
只测本需求受影响范围，不覆盖既有基线（格式往返、加载管线等见各自测试文档）。

实现要点（被测行为）：
- 注册层从「id→单个、先到优先静默丢后者」改为「id→多包并存」；**不同包同身份均加载**，**同一包内重复同身份只留首个**。
- 活实现解析顺序：① 用户在「Extension Routing」矩阵选中且该包已装 → 用它；② 否则**内建(built-in)优先**；③ 再否则 **packageId 序最小**。
- 工程文件**只引身份 id（扩展名）**、不存包 id；加载时按上面解析。选中包缺失 → 回退到该身份任一已装提供者（同默认策略）。
- format 的 **import / export 各算一条可路由身份**，可分别选不同包。
- 选择存进 **app `Settings.json` 的 `ExtensionRouting`**（`"kind:identity" → packageId`），改后**重启生效**（与切语言一致）。

---

## 前置准备（已就绪，用户只需开应用）

夹具已 build + pack。**两组冲突**：

**format 冲突**——两个包**同声明扩展名 `.tlroute`**（不同包 id），各实现 import+export：

| 包目录 | 包 id | UI 显示名（扩展侧栏） | import 产物轨名 | export 标记 |
|---|---|---|---|---|
| `tests/tlx/v1-routeconflict-a.tlx` | `com.tunelab.test.routeconflict.a` | **Route Conflict A** | `Route Conflict — Imported by Package A` | `exportedBy=A` |
| `tests/tlx/v1-routeconflict-b.tlx` | `com.tunelab.test.routeconflict.b` | **Route Conflict B** | `Route Conflict — Imported by Package B` | `exportedBy=B` |

**voice 冲突**——两个包**同声明引擎 id `TLConflictVoice`**（不同包 id），各暴露一个声源、名字标包：

| 包目录 | 包 id | UI 显示名（扩展侧栏） | 声源名（Set Voice 菜单内） |
|---|---|---|---|
| `tests/tlx/v1-voiceconflict-a.tlx` | `com.tunelab.test.voiceconflict.a` | **Voice Conflict A** | `Conflict Voice (Package A)` |
| `tests/tlx/v1-voiceconflict-b.tlx` | `com.tunelab.test.voiceconflict.b` | **Voice Conflict B** | `Conflict Voice (Package B)` |

> voice 夹具的会话不产音频（合成静音）——路由/分组测试只需能区分活实现，不需真实合成。

样例导入文件：`tests/sample-files/sample.tlroute`（导入器忽略内容，恒产出固定样例工程）。

安装：把四个 `.tlx` 各拖进 TuneLab 窗口（或扩展侧栏 Install Extension），**重启**应用。

> 重建夹具（如改了代码）：`dotnet build tests/TestPlugins.slnx -c Debug` 然后 `powershell -File tests/pack-tlx.ps1`。

---

## 用例

### TC1 — 多包同身份均加载（不再先到丢弃）
1. 安装 A、B 两个包并重启。
2. 打开扩展侧边栏。

**预期**：**Route Conflict A** 与 **Route Conflict B** 均显示为已加载（Loaded），均带 `format` 类别徽标；
无任何一个被标为 Skipped/「duplicate」。（旧行为会丢弃后到的同扩展名包——本用例验证已修复。）

### TC2 — 矩阵按类型分组、出现冲突行（且仅冲突行）
1. 装齐 format + voice 两组冲突包并重启。
2. 打开 设置窗口 → **Extension Routing** 页（tab 图标是分叉形，与 Extensions 不同）。

**预期**：
- 行**按插件类型分组**，分组标题（加粗）：**Voice**、**Format**（本组夹具不含 effect/agent，故只这两组）。
- **Voice** 组下：一行身份 `TLConflictVoice`，右侧下拉含 `Voice Conflict A (…a)`、`Voice Conflict B (…b)`。
- **Format** 组下：两行身份 `tlroute`，分别带行内副标签 **Import** / **Export**；下拉含 `Route Conflict A (…a)`、`Route Conflict B (…b)`。
- 包名取自 manifest 的 `name`（非引擎/格式显示名），内建候选显示 **Built-In**。
- 无冲突的内建格式（tlp/acep/…，仅内建一个提供者）**不出现**（只列 >1 提供者的身份）。
- 若未安装任何冲突包，此页显示空态「No conflicting extensions…」。

### TC3 — 确定性默认（未选过 → packageId 序最小）
1. 不在矩阵做任何选择（全新状态）。
2. 导入 `tests/sample-files/sample.tlroute`（拖入或 File→Import）。

**预期**：导入出的轨名为 **`Route Conflict — Imported by Package A`**。
理由：无内建提供者，按 packageId ordinal 最小 → `...routeconflict.a` < `...routeconflict.b` → A 为默认活实现。
矩阵两行的下拉默认也应显示选中 **Route Conflict A**。

### TC4 — 用户选择导入用 B（重启生效）
1. 矩阵中把 **Format (Import) / tlroute** 行选为 **Route Conflict B**。
2. **重启**应用。
3. 再次导入 `sample.tlroute`。

**预期**：导入出的轨名为 **`Route Conflict — Imported by Package B`**。

### TC5 — import / export 各自独立路由
1. 矩阵：**Format (Import)** 行选 **Route Conflict A**；**Format (Export)** 行选 **Route Conflict B**。重启。
2. 导入 `sample.tlroute` → 确认轨名为 **Package A**（TC4 反向验证 import 选择独立）。
3. File→Export，格式选 `tlroute`（显示名 `Route Conflict ...`），保存为 `out.tlroute`。
4. 用文本编辑器打开 `out.tlroute`。

**预期**：导入走 A（轨名 Package A）、导出文件内容为 **`exportedBy=B`**——import 与 export 的活实现互不影响、可各选不同包。

### TC6 — 选择持久化进 Settings.json
1. 完成 TC5 的选择后，打开 `%本机配置目录%/Configs/Settings.json`（与 ExtensionSettings.json 同目录）。

**预期**：存在 `"ExtensionRouting"` 对象，含形如
`"format-import:tlroute": "com.tunelab.test.routeconflict.a"` 与
`"format-export:tlroute": "com.tunelab.test.routeconflict.b"` 的条目。
重开应用后矩阵下拉仍显示上次选择（选择不丢）。

### TC7 — 选中包缺失 → 确定性回退（不崩）
1. 在 TC4 状态（import 选了 **B**）下，卸载 **Route Conflict B** 包，重启。
2. 导入 `sample.tlroute`。

**预期**：不崩溃、不报「格式不支持」；导入出的轨名回退为 **`Route Conflict — Imported by Package A`**
（选中包已不在 → 按默认策略回退到仍在的提供者 A）。矩阵中该行下拉因 B 已卸载只剩 A。

### TC8 — 同一包内重复同身份只留其一（打包错误降级）
> 可选；需手改一个夹具的 manifest.json，把 `classes` 写成两个都实现 IImportFormat 的类、或重复声明同扩展名的两个 `extensions[]` 条目。

**预期**：该包仍加载成功（首个实现生效），日志出现 `... already registered by package ...，duplicate ignored` 的 Warning；不因包内重复而整包失败。

### TC9 — voice 冲突：默认 + 选择（验证 Voice 分组的选择真生效）
1. 装齐两个 voice 冲突包并重启（不在矩阵做选择）。
2. 在轨上右键 → **Set Voice**，展开 `TLConflictVoice` 引擎子菜单。

**预期（默认）**：子菜单里的声源名为 **`Conflict Voice (Package A)`**（未选过 → packageId 序最小 → A 为默认活实现；`...voiceconflict.a` < `...b`）。

3. 设置 → Extension Routing → **Voice** 组的 `TLConflictVoice` 行选 **Voice Conflict B**，**重启**。
4. 再次右键 → Set Voice → 展开 `TLConflictVoice`。

**预期（选 B 后）**：声源名变为 **`Conflict Voice (Package B)`**——voice 的活实现随矩阵选择切换。
（给某 note/part 指定该声源后能正常保存/读取工程；本夹具合成静音，无音频输出属预期。）

---

## 可选 / 旁证

- **内建优先默认**：若某插件包声明了与内建相同的扩展名（如 `acep`），在用户未选择时，**内建实现**仍应为活实现（默认不被插件顶替）。需另备一个声明内建扩展名的夹具方可手测；代码层由 ResolveActive「内建优先」分支保证。
- **effect / agent 冲突**：与 voice/format 共用同一 `ExtensionRouting.ResolveActive` 解析路径，机制等价（已由 TC9 voice + format 两类覆盖）。如需旁证可备两个同 `engine` id 的 effect/agent 夹具，重复 TC3–TC4（矩阵分组标题为 Effect / Agent Model）。
- **设置页按包枚举（连带改动）**：当冲突包各自实现 `IExtensionSettings` 时，设置窗口「Extensions」页应为**每个包的实现各列一段**（按 packageId 分桶、互不串味）。本组 tlroute 夹具未实现设置接口，此点由既有 `PLUGIN-SETTINGS-TEST-CASES.md` + V1.Settings 夹具覆盖。
