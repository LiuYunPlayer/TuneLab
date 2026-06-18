# 测试：classes 入口协议 + 扩展设置按包分桶

本轮两项改动的独立验收文档（不改动已通过的基线文档）：

1. **manifest `classes` 入口协议**：V1 条目用 `classes`（候选类全名数组）声明入口类，宿主按 `type` 所需接口扫描认领；退役 `class`/`import`/`export` 具名槽（2.0.0 未发布，无兼容包袱，legacy 走独立兼容层不受影响）。
2. **扩展设置按包分桶**：`Configs/ExtensionSettings.json` 顶层按**包 id** 分桶，桶内再按 `kind:extensionId`，根治不同包同 engine/extension id 的设置互相覆盖（串味）。

受影响范围 = 扩展加载（`ExtensionManager`/各 manager）+ 扩展设置存储（`ExtensionSettingsStore`/`ExtensionSettingsManager`/设置窗口/agent 侧栏）。不触及合成、automation、format 往返等已通过基线。

---

## 前置（测试前由助手完成）

- `dotnet build tests/TestPlugins.slnx -c Release` → 各 V1 夹具产物落 `tests/packages/*`。
- 把要装的夹具打成 `.tlx`（zip + 改扩展名，`description.json` 在根）。本轮已把全部 V1 夹具 manifest 改为 `classes`。
- 用户只需：开应用 → 安装 tlx → 按下列用例核对。

涉及夹具与 **UI 显示名**（找的是显示名、非 Type id）：

| 夹具 | 类型 | UI 显示名 | engine/extension id |
|---|---|---|---|
| V1.Voice | voice | V1 Test Voice（声库 Alice/Bob (V1 Test)） | `TLTestVoiceV1` |
| V1.Effect | effect×2 | Gain / Reverse | `TLTestGain` / `TLTestReverse` |
| V1.Format | format | V1 Test Format（.tltest） | `tltest` |
| V1.Suite.Format | format+voice | Suite Format / Suite Voice | `tlsuite` / `TLSuiteVoice` |
| V1.Settings | voice + 设置 | V1 引擎设置演示 | `TLSettingsDemo` |
| Conflict.A / Conflict.B | format | ALC Conflict A/B | `tlconfa` / `tlconfb` |
| V1.NoAssemblies | format（负向） | V1 Missing-Assembly (negative) | `tlnoasm` |

---

## A. classes 入口协议

> 全部用现有夹具即可，无需新建。重点：换成 `classes` 后**加载/注册行为与改造前完全一致**。

- **A1 voice 单类认领**：装 V1.Voice → 扩展侧栏显示 Loaded；新建 voice part 选 V1 Test Voice，声库列出 Alice/Bob，能合成正弦。
  - 验证：`classes:["…TestVoiceEngine"]` 被按 `IVoiceEngine` 认领。
- **A2 effect 一包两引擎**：装 V1.Effect → Gain、Reverse 都注册；part 上各自可挂、可调参。
- **A3 format 双类认领**：装 V1.Format → 导入与导出菜单都出现 `.tltest`；往返一遍。
  - 验证：`classes` 里 importer 类按 `IImportFormat` 认领为导入、exporter 类按 `IExportFormat` 认领为导出（数组里两个类各被对的接口认领）。
- **A4 一包多 extension**：装 V1.Suite.Format → format（Suite Format）与 voice（Suite Voice）都注册，共享 Common.dll 一份。
- **A5 ALC 隔离不回归**：装 Conflict.A + Conflict.B → 两者都 Loaded，各用自己捆绑的 Helper 版本（导入各自扩展名验证）。
- **A6 负向：漏 assembly**：装 V1.NoAssemblies → 侧栏 Failed，tooltip 指出 assembly 缺失（不再扫全文件夹兜底）。
- **A7 负向：classes 无命中**（手工造）：临时把某 voice 夹具的 `classes` 改成一个不实现 `IVoiceEngine` 的类名（或不存在的类名）→ 该条目 Failed，tooltip 形如「no class implementing IVoiceEngine among […]」或「'X' not found」。改回即恢复。
- **A8（可选）一类双接口**：把 V1.Format 的导入/导出合成一个类同时实现 `IImportFormat` + `IExportFormat`、`classes` 只列这一个类 → 导入与导出都注册到该类。验证「同一类被两个接口同时认领」。

---

## B. 扩展设置按包分桶（单包自检）

> 用 V1.Settings 一个夹具即可验证新桶布局端到端可用（含密钥字段）。

- **B1 落盘布局**：装 V1.Settings → 设置 → 扩展页，找到「V1 引擎设置演示」，填普通字段 + 密钥字段 + 开关，关窗保存。
  - 查 `%AppData%/TuneLab/Configs/ExtensionSettings.json`：结构应为
    ```json
    { "com.tunelab.test.v1settings": { "voice:TLSettingsDemo": { /* 普通字段、开关、密钥(Win=DPAPI密文) */ } } }
    ```
    即**顶层是包 id**、其下才是 `voice:TLSettingsDemo`。
- **B2 回喂**：重开应用 → 设置页该扩展的值回显正确（密钥字段解密回显）。
- **B3 密钥安全**：Win 下文件里密钥是 DPAPI 密文非明文；清空密钥再保存 → 文件不写该字段。
- **B4 agent provider**：agent 侧栏设置里配置 OpenAI Compatible（编进宿主的内置）→ 落盘在 `(built-in)` 桶下的 `agent-model:openai-compatible`；功能照常。

---

## C. 跨包同 id 隔离（需第二个同 id 夹具，按需启用）

> 这是本次修复的核心场景，但需要一个与 V1.Settings **engine id 相同（`TLSettingsDemo`）、包 id 不同**的第二个夹具才能真正触发冲突。需要时请助手 scaffold（如 `V1.Settings.Clone`，包 id `com.tunelab.test.v1settings.clone`、同 engine `TLSettingsDemo`、不同 schema/默认值）。

- **C1 先到优先注册**：两包同时安装 → 引擎注册「先到优先」，同 engine id 只有一个生效（与改造前一致；本次不改注册去重策略）。
- **C2 设置不串味**：分别让 A 包、B 包生效（卸/装切换或调整加载序），各自在设置页填不同值并保存。
  - 查 `ExtensionSettings.json`：两包各自一个顶层桶
    ```json
    {
      "com.tunelab.test.v1settings":       { "voice:TLSettingsDemo": { /* A 的值 */ } },
      "com.tunelab.test.v1settings.clone": { "voice:TLSettingsDemo": { /* B 的值 */ } }
    }
    ```
  - 切换生效包后，读到的是**该包自己**的设置，不会读到另一包残留（改造前会读到同 `voice:TLSettingsDemo` 的串味值）。

---

## 回归不变量

- 旧 `Configs/ExtensionSettings.json`（平铺 `kind:id` 键）直接弃用：新键读不到 → 取 schema 默认值，用户重填一次。无需迁移、无崩溃。
- legacy 插件（无 id、走兼容层）不受影响：仍经 `assemblies` 盲扫加载，packageId 为空。
