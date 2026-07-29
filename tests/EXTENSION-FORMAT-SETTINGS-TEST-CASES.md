# 测试：format 接入扩展设置 + manifest 拆 type + 入口类退回标量

本轮改动的独立验收文档（不改动已通过的基线文档；被本轮取代的基线段落已就地加了 ⚠️ 标注）。

起点是「让 format 也能声明 `IExtensionSettings`」，但做的过程中连带解掉了 manifest 声明面的两个结构性缺口，故一并落地。

## 一、format 接入扩展设置

`format` 与三种引擎的结构差异是全部难点：**引擎注册时就 `new` 出长驻实例**，manager 直接 `engine as IExtensionSettings`；**format 注册的是工厂**，每次导入、每次导出现 `new`。

- **schema** 由一个惰性长驻**探测实例**回答（`GetSettingsConfig` 按 SDK 契约是纯函数、Init 前可调、只依赖传入的 context，故与"哪一个实例"无关）。探测实例不参与任何导入导出。
- **取值**在工厂 `new` 完**立即** `ApplySettings`，包在注册处一处覆盖全部调用点——四个 `(De)Serialize` 入口都经工厂。

## 二、manifest 拆 type：`format` / `format-import` / `format-export`

`format-import` / `format-export` 早就是路由 kind（`ExtensionRouting`、agent 工具的 kind 枚举、设置窗分组都在用），**只有 manifest 的 `type` 把两者捏在一起**，再由宿主在注册层展开。本轮让声明面追上这个早已存在的身份空间：

| `type` | 语义 | 桶键 |
|---|---|---|
| `format` | **紧凑形态**：一个类兼做读写 = 一个条目 = 一份说明 = **一个**设置桶 | `format:<后缀拼接>` |
| `format-import` | 单向条目，独立的实现/说明/设置 | `format-import:<后缀拼接>` |
| `format-export` | 同上 | `format-export:<后缀拼接>` |

**写法即语义**：合写 = 一份实现一份设置；拆写 = 两份实现各自独立。`format` **不是**"写两个条目的语法糖"。

配套：`import-suffixes` / `export-suffixes`（仅 `format` 可用，`suffixes` 的非空真子集）就地收窄某一侧——「读 `.mid` 和 `.midi`、只写 `.midi`」这种别名不对称，**不该**靠拆条目解决（那会把一份实现劈成两份说明两份设置）。方向子集**不进桶键**。

**方向从推断变契约**：过去方向靠"扫到哪个接口"反推，现在由 `type` 声明，类没实现声明的方向即**加载错误**。于是桶键成了 manifest 文本的纯函数——给类补个接口不会悄悄换桶、清空用户设置。

## 三、入口类 `classes` 数组 → `class` 标量

数组存在的唯一理由是"让一个 format 条目容纳导入类 + 导出类"，方向拆型后这个理由消失。而"宿主扫候选挑一个"本身就与"方向是声明不是推断"相悖，故一并收敛成：**一个条目 = 一个实现类 = 一份 introduction = 一份设置**，manifest 说什么就是什么，不匹配即报错。

## 桶键口径

`<type>:<全部后缀按 manifest 声明序拼接>`，分隔符 **`|`**（如 `format:mid|midi`）。

选 `|` 是因为它是 **Windows 文件名禁用字符**——能在 Windows 上存在的文件，扩展名不可能含它，于是拼接天然单射，不需要转义、也不需要向"扩展名"这个不属于我们的命名空间强加规矩。同一条原理早已在用：键的 `kind:identity` 靠 `:` 也是禁用字符才一直没出事。macOS/Linux 允许 `|`，故这条保证是 Windows 侧的；真撞上也只是**同一个包内**两个条目共用一份设置（外层还有 packageId 分桶，不可能跨包串味），故不为这个概率写校验。

**换桶不迁移**：`suffixes` 一变即全新桶，旧值原样残留但不再读取。改方向子集**不换桶**。

---

受影响范围 = 扩展加载与注册（`ExtensionManager` / `ExtensionInfo` / `FormatsManager` / `LegacyCompatLoader`）+ 扩展设置聚合（`ExtensionSettingsManager`）+ 设置窗扩展页 + 扩展侧栏详情窗齿轮 + 两个 agent 工具 + 全部 V1 夹具 manifest。**不触及**合成、automation、工程往返等已通过基线。

---

## 前置（测试前由助手完成）

- `dotnet build TuneLab.sln -c Debug`
- `dotnet build tests/TestPlugins.slnx -c Debug -t:Rebuild`（V1.AsymFormat 与 V1.MultiSuffix 本轮已补进 slnx，此前它们不在里面、全量构建刷不到）
- `pwsh tests/pack-tlx.ps1`
- `pwsh tests/install-tlx.ps1 v1-format v1-multisuffix v1-asymformat v1-suite v1-routeconflict-a v1-routeconflict-b v1-conflict-a v1-conflict-b v1-settings`（**装前须关闭 TuneLab**）
- 导入用样例文件：`tests/sample-files/` 下的 `sample.tltest`、`sample.mtest`、`sample.mtst`、`sample.asym`、`sample.asymx`

用户只需：开应用 → 按下列用例核对。

### 涉及夹具与 **UI 显示名**（设置窗/侧栏里找的是显示名，不是后缀或类名）

| 夹具 | 形态 | UI 显示名 | 桶键 |
|---|---|---|---|
| **V1.MultiSuffix** | 紧凑形态，一个类，带设置 | V1 Test Multi-Suffix | `format:mtest\|mtst` |
| **V1.Format** | **拆写形态**，两个类，**各带设置** | V1 Test Format (Import) / (Export) | `format-import:tltest` / `format-export:tltest` |
| **V1.AsymFormat** | 紧凑形态 + `export-suffixes`，无设置 | V1 Asymmetric Format | 无（未声明设置） |
| **V1.Suite.Format** | 拆写形态，无设置 | Suite Format (Import) / (Export) | 无 |
| Conflict.A / B | 单向 `format-import`，无设置 | ALC Conflict A / B | 无 |
| V1.Settings | voice 引擎带设置（不回归对照组） | V1 引擎设置演示 | `voice:TLSettingsDemo` |

### 设置字段

**V1.MultiSuffix**（一个桶）

| 键 | 标签 | 类型 | 默认 | 可观测处 |
|---|---|---|---|---|
| `track_name` | Track Name | 文本 | `Multi Suffix Track` | 导入后的**轨名** |
| `note_count` | Note Count | 整数滑条 1–8 | `2` | 导入后的 **note 个数** |
| `licence` | Licence Key | 密钥（掩码） | 空 | 导出文本里的 `licence=<set>/<empty>`（**只报有无**） |

**V1.Format**（两个桶，字段刻意完全不同——串味的话一眼可见）

| 条目 | 键 | 标签 | 类型 | 默认 | 可观测处 |
|---|---|---|---|---|---|
| Import | `fallback_track_name` | Fallback Track Name | 文本 | `V1 Test Track` | 导入**空/坏** `.tltest` 时的轨名 |
| Export | `indent` | Indent Output | 开关 | 开 | 导出文件是多行缩进还是挤成一行 |

落盘位置：`%APPDATA%\TuneLab\Configs\ExtensionSettings.json`。

---

## A. 桶：一个条目一份设置

- **A1 紧凑形态只有一行**：设置窗 →「扩展」页 → **V1 Test Multi-Suffix** 只占 **一行**（不是 `.mtest` 一行、`.mtst` 一行），三个字段齐全。
- **A2 落盘只有一个键**：改点值 → 关窗 → 看 `ExtensionSettings.json`：`com.tunelab.test.v1multisuffix` 桶下**只有** `"format:mtest|mtst"` 一个键。
  - 反例（不该看到）：`"format:mtest"` 与 `"format:mtst"` 两个键内容重复。
- **A3 拆写形态是两行两桶**：**V1 Test Format (Import)** 与 **(Export)** 各占一行，字段各不相同。落盘后 `com.tunelab.test.v1format` 桶下有 `"format-import:tltest"` 与 `"format-export:tltest"` **两个**键，各存各的字段。
  - **重点**：这正是拆写的意义——两份实现各自可配置。合写形态下不可能做到（只有一个桶）。
- **A4 两个桶互不串味**：把 Import 的 `fallback_track_name` 改成 `Renamed`，Export 的 `indent` 关掉 → 两个 JSON 桶各自只含自己的键，没有对方的字段。
- **A5 无设置的 format 不出现**：V1 Asymmetric Format、Suite Format、ALC Conflict A/B 都**不在**「扩展」页——设置是 opt-in 的。
- **A6 密钥不落明文**：V1 Test Multi-Suffix 的 Licence Key 填 `abc123` → 保存 → `ExtensionSettings.json` 里该字段是一长串 DPAPI 密文（macOS 下是空串、真值进钥匙串），**搜不到 `abc123`**；重开设置窗显示为掩码且非空。
  - 同时验证 SecretStore 的 account 中段用的是条目键（`…:format:mtest|mtst:licence`）。

## B. 回喂：现 new 的实例确实拿到了设置

- **B1 紧凑形态，改轨名 → 导入**：Track Name 改 `Renamed By Settings` → 保存 → 导入 `sample.mtest` → 新轨名为它。
- **B2 同一份设置作用于另一个后缀**：紧接着导入 `sample.mtst` → 轨名相同。两个后缀共用一个桶。
- **B3 note 个数**：Note Count 改 `5` → 导入 `sample.mtest` → part 里 **5 个 note**。
- **B4 拆写形态，导入侧**：V1 Test Format (Import) 的 Fallback Track Name 改成 `Import Side` → 导入一个**空的** `.tltest`（新建空文件改后缀即可）→ 轨名为 `Import Side`。
- **B5 拆写形态，导出侧**：V1 Test Format (Export) 的 Indent Output **关掉** → 导出 `.tltest` → 用文本编辑器打开：JSON **挤成一行**；打开重新导出对比多行版本。
  - **重点**：导入侧那份设置对导出毫无影响，反之亦然。
- **B6 导出摘要带设置**：V1 Test Multi-Suffix 导出 `.mtest` → 文件形如 `multi-suffix test export; tracks=N; track_name=…; note_count=…; licence=<set>`，密钥只写 `<set>`/`<empty>`。
- **B7 日志：每造一个实例响一次**：`[V1.MultiSuffix] ApplySettings: …` / `[V1.Format/import] …` / `[V1.Format/export] …` 应在 ① 启动加载后各一条（宿主 `ApplyPersisted` 灌给探测实例）；② **每次导入**一条；③ **每次导出**一条。
  - **重点**：它是"每造一个实例响一次"，不是"每改一次设置响一次"——这是 format 与引擎的核心差异，文档也是这么承诺的。
- **B8 立即生效、无需重启**：改完直接导入即生效。

## C. 方向子集（`export-suffixes`）

- **C1 菜单不对称**：装着 V1.AsymFormat → **导入**菜单里 `.asym` 与 `.asymx` 都在；**导出**菜单里**只有** `.asymx`。
- **C2 两个后缀都能导入**：`sample.asym` 与 `sample.asymx` 都能导入，产出相同（轨名 `Asym Track`、2 个 note）。
- **C3 路由页粒度**：设置窗「Extension Routing」→ 若无别的包提供这些后缀则该页无冲突行（正常）。要看粒度用 `.tlroute`（见 E5）。
- **C4 负向：空子集**（手工造）：关闭 TuneLab，把已装 V1.AsymFormat 的 manifest 改成 `"export-suffixes": []` → 重开 → 该条目 **Failed**，tooltip 指出空数组非法、要单向请用 `format-import`/`format-export`。改回即恢复。
- **C5 负向：子集越界**（手工造）：改成 `"export-suffixes": ["nope"]` → Failed，tooltip 指出它不在 `suffixes` 里。
- **C6 负向：死后缀**（手工造）：改成 `"suffixes": ["asym","asymx","dead"], "export-suffixes": ["asymx"], "import-suffixes": ["asym","asymx"]` → Failed，tooltip 指出 `dead` 两个方向都不认、什么都不会注册。
- **C7 子集不换桶**（可选，需要一个带设置的紧凑格式）：给 V1.MultiSuffix 临时加 `"export-suffixes": ["mtst"]` → 重启 → 设置**原样还在**（桶键仍是 `format:mtest|mtst`），导出菜单只剩 `.mtst`。改回后设置依旧在。

## D. 拆 type 与单类契约的负向用例

> 都靠手工改**已装**的 manifest 造，测完改回。改前关闭 TuneLab。

- **D1 `format` 要求同一个类双接口**：把 V1.Format 的两个条目并成一个 `"type": "format"` 条目、`class` 填导入类 → 该条目 **Failed**，tooltip 形如 `class '…TestImportFormat' does not implement IExportFormat`。
  - **重点**：`class` 是标量，所以"两个类塞进一个 format 条目"根本无法表达——这个形态在语法层就没了，不需要额外规则去禁。
- **D2 声明的方向类没实现**：把 V1.Format 的 Import 条目 `type` 改成 `format-export` → Failed，同样是 `does not implement IExportFormat`。
  - **重点**：过去这会**静默降级**成"只注册导入"（方向靠扫接口反推），现在是显式错误。
- **D3 单向 type 用方向子集**：给 Conflict.A（`format-import`）加 `"export-suffixes": ["tlconfa"]` → Failed，tooltip 指出方向子集仅 `format` 可用。
- **D4 `class` 找不到 / 不实现接口**：把某 voice 夹具的 `class` 改成不存在的类名 → Failed，tooltip 形如 `class 'X' not found in the declared assembly`；改成一个存在但不实现引擎接口的类 → `class 'X' does not implement IVoiceSynthesisEngine`。
  - **重点**：错误消息现在指名道姓，不再是"[A, B, C] 里没有实现 X 的"。

## E. 不回归（本轮重构了注册路径与全部夹具 manifest，这几条必看）

- **E1 内建工程格式**：`.tlp` / `.tlpx` 新建→保存→重开，工程内容完好。
- **E2 内建 MIDI**：导入一个 `.mid`；导出 `.mid` 与 `.midi` 都在菜单里、都能导出并被读回。内建 MIDI 仍是紧凑形态双向、两个后缀对称。
- **E3 V1 拆写 format 往返**：导入 `sample.tltest`（真解析：轨「tltest sample (parsed)」、bpm 128、5 note）→ 导出 `.tltest` → 再导入，轨/note 一致。
- **E4 Legacy format**：导入 `sample.tloldfmt` 仍走 Compat 真解析（轨「tloldfmt sample (parsed)」、bpm 90、3 note）；导出方向也在。
  - **重点**：legacy 的 importer / exporter 是分两次推来的，本轮改成各自注册成 `format-import` / `format-export` 条目——两个方向都得在。
- **E5 路由粒度没被收窄**：装 Route Conflict A + B → 设置窗「Extension Routing」→ Format 组下 `tlroute` 两行（Import / Export 副标签），各含 A/B 两个候选；把 Import 选 A、Export 选 B，重启后导入产出「Package A」、导出写 `exportedBy=B`。
- **E6 ALC 隔离不回归**：Conflict.A + Conflict.B 都 Loaded，导入各自扩展名，轨名里的 Helper 版本号各不相同。
- **E7 详情窗齿轮归位**：
  - V1 Test Multi-Suffix：**一个 tab**（页内列 `.mtest .mtst`），有齿轮，点击跳到设置窗对应行并滚动定位。
  - V1 Test Format：**两个 tab**（Import / Export），**两页都有齿轮**，各自跳到自己那一行。
  - **重点**：多后缀条目的齿轮要靠拼接键匹配，逐后缀去查会全部落空。
- **E8 停用后齿轮消失**：把 V1 Test Multi-Suffix 停用 → 重启 → 详情窗该页无齿轮、「扩展」页也没有它那一行；重新启用 + 重启 → 齿轮与设置行回来，**之前填的值还在**。
- **E9 引擎类设置不回归**：V1.Settings（voice）那一行照旧渲染、动态显隐（勾 Use GPU 才出 GPU Device）、密钥加密、重启回喂日志照旧。
  - **重点**：format 是**追加**在 effect/voice/instrument 之后收集的，不该扰动既有次序。
- **E10 一包多条目仍正常**：V1 Test Suite → 侧栏徽标含 Format-Import / Format-Export / Voice；`.tlsuite` 导入导出都在；`TLSuiteVoice` 在音源选择器里。
- **E11 agent 侧**：
  - 「列出扩展自己的设置」→ 清单里有 `format:mtest|mtst`、`format-import:tltest`、`format-export:tltest` 三行（外加 voice 那些）。
  - 让 agent 把 `note_count` 设成 3（过授权闸门）→ 导入 `sample.mtest` 得 3 个 note，无需重启。
  - 让 agent 设 `licence` → 拒绝并引导用户去设置窗；`list` 里只报 `currently SET` / `NOT set`。

---

## 判定

A、B、C1–C3、E 全绿即通过。C4–C6 与 D 组是**负向用例**，看到 Failed + 指名道姓的 tooltip 才算对。C7 可选。E11 依赖 agent 模型可用，不可用时跳过并注明。
