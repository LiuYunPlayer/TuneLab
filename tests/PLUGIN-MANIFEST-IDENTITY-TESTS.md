# 插件身份内联 manifest · 测试用例

本轮改动：插件的具体身份（引擎 type id、格式扩展名、实现类）从代码 attribute 搬进
`description.json`——manifest 成为单一真相，宿主按 `class`/`import`/`export` 精确取类型实例化，
**不再反射扫 attribute**。SDK 的 `[VoiceEngine]`/`[EffectEngine]`/`[AgentModelEngine]`/`[ImportFormat]`/`[ExportFormat]`
已移除；内建格式/声源/agent 模型改为显式代码注册。

本文只覆盖**受本改动影响的范围**；基线加载/合成/Compat 行为见 [PLUGIN-TEST-CASES.md](PLUGIN-TEST-CASES.md)。

## 准备

```bash
dotnet build TuneLab.sln -c Debug
dotnet build tests/TestPlugins.slnx -c Debug
powershell -File tests/pack-tlx.ps1          # → tests/tlx/*.tlx
```

安装：把 `tests/tlx/<名>.tlx` 拖进 TuneLab 窗口；加载状态在扩展侧边栏（Loaded / Skipped / Failed + 原因）。

---

## 一、manifest 身份正确注册（代码无 attribute）

### 1. format：extension + import/export 指向类
- 装 `v1-format.tlx` → **Loaded**；导入/导出菜单出现 **`.tltest`**。
- 导入 `sample-files/sample.tltest` → 出工程；导出 `.tltest` 再导入 → 往返一致。
- **验证点**：`TestImportFormat`/`TestExportFormat` 无 attribute，仅靠 manifest 的 `extension`+`import`+`export` 注册成功。

### 2. voice：engine + class 指向类
- 装 `v1-voice.tlx` → **Loaded**；声库列表出现 **Alice / Bob**（引擎 `TLTestVoiceV1`）。
- 选 Alice 写 note → 合成出声。
- **验证点**：`TestVoiceEngine` 无 attribute，仅靠 manifest 的 `engine`+`class` 注册成功。

### 3. effect：一个程序集多引擎，逐条列
- 装 `v1-effect.tlx` → **Loaded**；effect 选择器同时出现显示名 **Gain** 与 **Reverse**（manifest `name`，非引擎 id）。
- 在一条 part 上挂 Gain（调 gain 滑块）+ Reverse，链串行生效；已添加的 effect 块标题也显示 **Gain**/**Reverse**。
- **验证点**：同一 `V1.Effect.dll` 的两个引擎由 `extensions[]` 两条（同 `assembly`、不同 `engine`/`class`）分别注册，二者都在；工程文件里存的仍是不可变 id `TLTestGain`/`TLTestReverse`。

### 4. suite：一包多插件、跨两个程序集
- 装 `v1-suite.tlx` → **Loaded**；**一个包**同时注册 format **`.tlsuite`** 和声库引擎 **TLSuiteVoice**。
- 导入 `sample.tlsuite` 正常；声库列表出现 suite 声库。
- **验证点**：format 条目用 `extension`+`import`+`export`+`assembly`(V1.Suite.Format.dll)，voice 条目用 `engine`+`class`+`assembly`(V1.Suite.Voice.dll)，共享 Common.dll 仍只加载一份。

### 5. i18n：voice 经 manifest 注册 + manifest 本地化
- 装 `v1-i18n.tlx` → **Loaded**；中文环境侧边栏名为 **V1 多语言演示**、英文 **V1 i18n Demo**；声库引擎 `TLI18NVoice` 可用。
- **引擎显示名翻译**：单插件简写下，顶层 `name`/`localizations` 既是包名也是该引擎显示名。「Set Voice」菜单里该引擎组标题随语言显示 **V1 多语言演示** / **V1 i18n Demo**，而工程存的引擎 id 始终是 `TLI18NVoice`。
  > 想让"包名"与"引擎显示名"各不相同，用 `extensions[]` 多插件形态、给条目单独写 `name`（见 v1-effect/v1-suite）。简写下两者共用同一个 `name` 字段。

---

## 显示名 vs 不可变 id（贯穿验证）

- 引擎/格式的 `engine`/`extension` 是**不可变身份**：写进工程文件、用于注册与路由，改名会让旧工程失配。
- `name`(+`localizations`) 仅供 UI 展示，可改可译，不影响已存工程。
- 抽查：v1-effect 选择器显示 **Gain/Reverse**（`extensions[]` 条目各自的 `name`），但保存工程后在文件里能看到 effect type 为 `TLTestGain`/`TLTestReverse`；v1-format 文件类型显示包名 **V1 Test Format**，扩展名仍是 `.tltest`。
- 内建：openai-compatible 模型在 agent 下拉显示 **OpenAI Compatible**（id 仍 `openai-compatible`）；内建格式显示 **TuneLab Project / MIDI / VOCALOID Project** 等友好名。
- 安装汇总弹窗（拖入 .tlx 后）按**包名**列出，不是引擎名。

### 6. ALC 版本隔离仍成立（format 经 manifest import）
- 装 `v1-conflict-a.tlx` + `v1-conflict-b.tlx` → 两个都 **Loaded**。
- 导入 `sample.tlconfa` → 轨名见 **ConflictHelper v1.0.0.0**；`sample.tlconfb` → **v2.0.0.0**。
- **验证点**：format 身份改由 manifest 提供后，per-plugin ALC 私有依赖隔离不受影响。

---

## 二、内建（无 attribute、显式注册）照常可用

### 7. 内建格式
- 不装任何插件，导入/导出菜单应含内建格式：**tlp / tlpx / acep / ufdata**（进出），**mid / midi / vpr**（仅导入）。
- 打开/保存原生 `.tlp`、`.tlpx` 工程往返正常。

### 8. 内建声源 / agent 模型
- 新建无声源 part 行为正常（空引擎 `""` 仍注册，「Set Voice」里显示 **Built-In**）。
- Agent 侧边栏下拉可选 **OpenAI Compatible**（内建 agent 引擎仍注册；id 仍 `openai-compatible`）。

---

## 三、负向：条目级校验失败优雅降级（主程序不崩）

| 装 | 预期 | 说明 |
|---|---|---|
| `v1-no-assemblies.tlx` | **Failed** | format 条目有 `extension`/`import` 但漏 `assembly` → `assembly '(unspecified)' not found` |
| `v1-effect-unsupported.tlx` | **Failed** | effect 条目 `assembly` 指向不存在的 never-loaded.dll → `assembly 'never-loaded.dll' not found` |

- 把上述坏包与正常包一起装：正常包照常 **Loaded**，坏包只在侧边栏标 **Failed** + tooltip 原因，**绝不崩主程序**。
- 一包多条目时，只失败的条目计 Failed、其余照常注册（部分加载 PartiallyLoaded）。

> 手动可补：把某 V1 包的 `class`/`import` 改成不存在的类名，预期该条目 Failed 且原因为 `class '...' not found`；
> 改成不实现对应接口的类，预期原因为 `does not implement IXxx`。
