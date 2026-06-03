# 插件系统测试用例与预期表现

> 针对插件系统已落地的功能（SDK 分层 / Compat.Legacy 老插件兼容 / 扩展加载机制），提供**可手动加载验证**的测试插件包 + 预期表现清单。
> effect 真实实现尚未落地，故只覆盖"识别即跳过"；详见文末「待落地」。

## 如何构建与部署

```bash
dotnet build tests/TestPlugins.slnx -c Debug
```

构建产物落 `tests/packages/<包名>/`（每个 = 一个插件包文件夹：`description.json` + dll）。**manifest-only 变体**（无需构建的纯 `description.json` 包）在 `tests/manifest-variants/<包名>/`。

> ⚠️ **注意：拖「文件夹」进窗口不会安装**。App 的安装单位是 `.tlx`（= zip，根目录含 `description.json`）——`Editor.OnDrop` 只认 `.tlx` 文件。部署二选一：

**方式 A：打成 .tlx 安装（真实用户路径，即时加载，并顺带覆盖 install 流程测试）**
```powershell
pwsh tests/pack-tlx.ps1            # 产物 tests/tlx/*.tlx
```
逐个把 `tests/tlx/<包>.tlx` **文件**拖进 TuneLab 窗口，或扩展侧边栏「Install Extension」选择。安装后即时加载、无需重启。

**方式 B：拷文件夹进扩展目录 + 重启（dev 简便，不经 install 流程）**
把包文件夹拷进 `%AppData%/TuneLab/Extensions/<包名>/`（Windows），重启 App → 启动扫描 `LoadExtensions()` 逐文件夹加载。

加载状态在**扩展侧边栏**查看（Loaded / PartiallyLoaded / Skipped / Failed + 原因）。format 扩展名出现在导入/导出菜单；voice 引擎出现在音轨的引擎/声库选择。

> 提示：`v1-bad-manifest` 用方式 B 测最直接（方式 A 安装时若 `.tlx` 内 description.json 解析报错可能在安装阶段就报错，亦是有效的优雅降级表现）。

---

## 一、核心：每种类型 × 新老接口

### 1. `v1-format` — V1 format（新接口）
- **覆盖**：`IImportFormat`/`IExportFormat`（`TuneLab.SDK.Format`）+ DataInfo 往返；单插件简写 manifest（顶层字段 + `assemblies`）。
- **预期**：侧边栏 **Loaded**；导入/导出菜单出现 **`.tltest`**。
- **验证**：
  1. 导入任意 `.tltest` 文件（空文件即可）→ 出现 1 轨 4 note（do/re/mi/fa，pitch 60/62/64/65）。
  2. 对当前工程导出 `.tltest` → 再导入 → 轨/note（pos/dur/pitch/lyric）保持一致（往返保真）。

### 2. `v1-voice` — V1 voice（新接口）
- **覆盖**：`IVoiceEngine`/`IVoiceSource`/`ISynthesisData`/`ISynthesisTask`/`SynthesisResult`；多声库；Segment；合成事件；**SynthesizedPhonemes 按 note 键**。
- **预期**：**Loaded**；引擎 `TLTestVoiceV1`，声库 **Alice / Bob** 出现在声库列表。
- **验证**：
  1. 新建音轨选 Alice/Bob，写几个 note → 合成成功（进度→完成），听到/看到正弦波形（每 note 音高对应频率）。
  2. note 上出现 phoneme（symbol = lyric，未填则 "la"）→ 验证 phoneme 按 note 正确归位。

### 3. `legacy-format` — Legacy format（老接口，走 Compat）
- **覆盖**：老 `IImportFormat`/`IExportFormat`（`TuneLab.Extensions.Formats`，链接冻结 `TuneLab.Base` v1.0.0.0）；经 Compat.Legacy 适配；老 schema manifest（**无 `id`** → 判为 Legacy）；Base↔Primitives DataInfo 往返。
- **预期**：侧边栏 **Loaded**（**Generation=Legacy**）；导入/导出出现 **`.tloldfmt`**。
- **验证**：
  1. 导入 `.tloldfmt`（空文件）→ 出现 1 轨 3 note（sol/la/ti，pitch 67/69/71，bpm 100）。
  2. 导出 `.tloldfmt` → 再导入 → 数据一致（验证老→新→老边界深拷贝无丢失）。

### 4. `legacy-voice` — Legacy voice（老接口，走 Compat）
- **覆盖**：老 voice 全接口经 Compat 适配；**Config 家族跨代转换**（`NumberConfig`→`SliderConfig`、`AutomationConfig`→`AutomationConfig`）；note 身份缓存 + phoneme 映射；**无 `description.json`** → Legacy 纯扫描发现。
- **预期**：**Loaded**（Legacy）；引擎 `TLTestVoiceLegacy`，声库 **Carol**；note 属性面板出现 `tension`（来自老 `NumberConfig`），自动化出现 `Volume`（来自老 `AutomationConfig`）。
- **验证**：
  1. 选 Carol，写 note → 合成成功，正弦音 + phoneme 归位。
  2. note 属性面板可见 `tension`（范围 -1~1）；参数面板可见 `Volume` 自动化（-60~12，蓝色）→ 验证 config 转换。

### 5. `v1-suite` — 一包多插件 + 共享基建
- **覆盖**：`extensions[]`（format + voice 同包）；共享 `V1.Suite.Common.dll`**只分发一份、同 ALC 只加载一份**；包级公共字段。
- **预期**：**Loaded**，**一个包**同时注册 format **`.tlsuite`** + voice 引擎 **`TLSuiteVoice`**（声库 `[v1-suite] Voice`）。
- **验证**：
  1. 导入 `.tlsuite` → 轨名 `[v1-suite] Format`（证明用到共享 Common）。
  2. 声库列表出现 `[v1-suite] Voice`。两个插件共用包内同一个 `V1.Suite.Common.dll`。

---

## 二、特性变体

| 包 | 位置 | 覆盖 | 预期表现 |
|---|---|---|---|
| `v1-no-assemblies` | packages/ | manifest 省略 `assemblies` → 扫全部 dll | **Loaded**；`.tlnoasm` 可用（导入出现 "scanned…" 轨） |
| `legacy-multi` | packages/ | Legacy 一包多插件（扫多 attribute） | **Loaded**(Legacy)；`.tlm1`+`.tlm2` 两个 format + 引擎 `TLLegacyMultiVoice` 全注册 |
| `v1-sdkver-high` | manifest-variants/ | sdk-version 兼容门 | **Skipped**，原因含 "requires SDK 99.0, host provides 1.0" |
| `v1-platform-mismatch` | manifest-variants/ | platforms 过滤 | **Skipped**（platform not available） |
| `v1-resource` | manifest-variants/ | 无代码资源包（type=voicebank） | **Loaded**，不加载任何程序集（仅登记） |
| `v1-effect` | manifest-variants/ | effect 识别但暂不支持 | **Skipped** + 日志 "effect extensions are not supported…"，不崩主程序 |
| `v1-bad-manifest` | manifest-variants/ | 坏 description.json | **Failed**，原因 "Invalid description.json…"，不崩主程序 |

### ALC 私有依赖版本冲突（杀手锏）

| 包 | 覆盖 | 预期表现 |
|---|---|---|
| `v1-conflict-a` | 捆绑 `ConflictHelper` **v1.0.0.0** | **Loaded**；导入 `.tlconfa` → 轨名 `ConflictHelper v1.0.0.0 (pkg A)` |
| `v1-conflict-b` | 捆绑 `ConflictHelper` **v2.0.0.0**（同名不同版） | **Loaded**；导入 `.tlconfb` → 轨名 `ConflictHelper v2.0.0.0 (pkg B)` |

- **关键预期**：A、B **同时加载成功、互不冲突**，各自看到自己捆绑的 `ConflictHelper` 版本（v1 / v2）。这验证 per-plugin ALC 把同名不同版的第三方依赖隔离开（per-plugin ALC 隔离的核心动机）。
- **失败信号**（若 ALC 隔离没生效）：第二个包加载失败、或两包都报出同一个版本、或类型加载异常。

---

## 三、优雅降级总则（贯穿所有用例）

任何包加载失败（坏 manifest、缺 dll、类型实例化抛异常、原生依赖缺失）都应：**只跳过该插件/包、在侧边栏与日志反映、绝不崩溃主程序**。可把 `v1-bad-manifest` 与其它正常包**一起**放进扩展目录，确认正常包照常 Loaded、坏包 Failed、主程序正常启动。

---

## 四、待落地（插件系统尚未完备，记录在此，后面补测试）

以下测试用例当前**无法编写/验证**，待对应功能落地后补：

1. **真实 effect 插件**（import/process/参数）—— 待 `SDK.Effect` 接口形状定义。当前仅能测「`type=effect` 被识别即跳过」（`v1-effect`）。
2. **`ILog` / `ITuneLabContext` 注入**测试（插件读宿主状态、打 tag 日志）—— 待注入接通（当前 managers 仍无参构造，未注入 context）。
3. **collectible 热卸载**测试（免重启卸载/更新、释放 dll 文件锁、ALC 真正卸载）—— 待 collectible 触发条件落地（当前非 collectible，卸载走重启式 `ExtensionInstaller.exe`）。对应可加的断言：卸载后弱引用被回收、文件锁释放。
4. **多版本共存（V1↔V2）compat**测试 —— 待出现 V2 / `Compat.V1`。
5. **原生依赖（ONNX 等）真实 voice 引擎**端到端 —— 需要真实捆绑原生运行时的样例；当前 voice 测试用纯托管正弦合成覆盖接口与边界穿越，未覆盖 `LoadUnmanagedDll` 原生解析路径的真机行为。

---

## 附：测试插件清单（源码位置）

```
tests/
  TestPlugins.slnx                 # 构建所有需编译的测试插件
  plugins/
    V1.Format/            → packages/v1-format          (V1 format)
    V1.Voice/             → packages/v1-voice           (V1 voice)
    Legacy.Format/        → packages/legacy-format      (Legacy format, 无 id)
    Legacy.Voice/         → packages/legacy-voice       (Legacy voice, 无 manifest)
    V1.Suite.Common/                                    (共享基建 dll)
    V1.Suite.Format/      → packages/v1-suite           (一包多插件 format 入口 + manifest)
    V1.Suite.Voice/       → packages/v1-suite           (一包多插件 voice 入口)
    V1.NoAssemblies/      → packages/v1-no-assemblies   (省略 assemblies)
    Legacy.Multi/         → packages/legacy-multi       (Legacy 一包多 attribute)
    Conflict.Helper.V1/                                 (ConflictHelper v1.0.0.0)
    Conflict.Helper.V2/                                 (ConflictHelper v2.0.0.0)
    Conflict.A/           → packages/v1-conflict-a      (捆绑 Helper v1)
    Conflict.B/           → packages/v1-conflict-b      (捆绑 Helper v2)
  manifest-variants/                                    # 无需构建，直接拖用
    v1-sdkver-high/ v1-platform-mismatch/ v1-resource/ v1-effect/ v1-bad-manifest/
```
