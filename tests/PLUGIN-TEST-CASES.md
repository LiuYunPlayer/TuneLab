# 插件系统测试用例

每个用例：**装什么 → 做什么 → 预期看到什么**。测试人员只需逐条核对能否跑通。

## 准备

```bash
dotnet build TuneLab.sln -c Debug            # 主程序（含 Compat.Legacy 部署）
dotnet build tests/TestPlugins.slnx -c Debug # 测试插件
powershell -File tests/pack-tlx.ps1          # 打成 tests/tlx/*.tlx
```

- **安装**：把 `tests/tlx/<名>.tlx` 拖进 TuneLab 窗口（或扩展侧边栏 → Install Extension）。安装后即时加载。
- **加载状态**：扩展侧边栏查看（Loaded / Skipped / Failed + 原因）。
- **导入素材**：`tests/sample-files/`（按扩展名路由）。

---

## 一、能加载、能用的插件

### 1. v1-format（V1 新接口 · format）
- 装 `v1-format.tlx` → 侧边栏 **Loaded**；导入/导出菜单出现 **`.tltest`**。
- 导入 `sample-files/sample.tltest` → 出 1 轨「**tltest sample (parsed)**」，5 个 note（do re mi fa so，bpm 128）。
- **往返**：导出 `.tltest` 再导入 → 轨/note 一致。

### 2. v1-voice（V1 新接口 · voice）
- 装 `v1-voice.tlx` → **Loaded**；声库列表出现 **Alice / Bob**。
- 新建音轨选 Alice，写几个 note → 合成出声（正弦，音高随 note）。
- **选中一个 note** → note 属性面板出现 **tension**；参数面板出现自定义自动化 **Growl**。

### 3. legacy-format（老接口 · format · 走 Compat）
- 装 `legacy-format.tlx` → **Loaded**（代际 **Legacy**）；菜单出现 **`.tloldfmt`**。
- 导入 `sample-files/sample.tloldfmt` → 出 1 轨「**tloldfmt sample (parsed)**」，3 个 note（sol la ti，bpm 90）。
- **往返**：导出 `.tloldfmt` 再导入 → 一致（验证老↔新边界转换无丢失）。

### 4. legacy-voice（老接口 · voice · 走 Compat）
- 装 `legacy-voice.tlx` → **Loaded**（Legacy）；声库出现 **Carol**。
- 选 Carol，写 note → 合成出声。
- **选中一个 note** → 属性面板出现 **tension**；参数面板出现自定义自动化 **Breathiness**。
  > 该插件的自动化故意命名 Breathiness 而非 Volume——Volume 是宿主保留名，用它会被内置项占用而显示不出。

### 5. v1-suite（一包多插件 + 共享基建）
- 装 `v1-suite.tlx` → **Loaded**；**一个包**同时注册 format **`.tlsuite`** 和声库引擎 **TLSuiteVoice**。
- 导入 `sample-files/sample.tlsuite` → 轨名含 **`[v1-suite]`**（证明用到共享 Common.dll）。
- 声库列表出现 **`[v1-suite] Voice`**；选它、选中 note → 属性面板有 **tension**，参数面板有自定义自动化 **Power**。

### 6. legacy-multi（老包内多个 attribute）
- 装 `legacy-multi.tlx` → **Loaded**（Legacy）；**`.tlm1` + `.tlm2` 两个 format** 和声库引擎 **TLLegacyMultiVoice** 全部注册。
- 导入 `sample.tlm1` → 轨名 **legacy-multi #1**；导入 `sample.tlm2` → 轨名 **legacy-multi #2**。

### 7. ALC 私有依赖版本隔离（v1-conflict-a + v1-conflict-b）
- 两个 `.tlx` 都装 → **两个都 Loaded**。
- 导入 `sample.tlconfa` → 轨名 **ConflictHelper v1.0.0.0 (pkg A)**。
- 导入 `sample.tlconfb` → 轨名 **ConflictHelper v2.0.0.0 (pkg B)**。
- **验证点**：两包各自捆绑了同名不同版的 `ConflictHelper`（v1 / v2），同时加载互不冲突，各看到自己的版本。

---

## 二、按设计被拒/降级的插件

| 装 | 预期状态 | 说明 |
|---|---|---|
| `v1-sdkver-high.tlx` | **Skipped** | 要求的 SDK 版本高于宿主 |
| `v1-platform-mismatch.tlx` | **Skipped** | 平台不匹配 |
| `v1-resource.tlx` | **Loaded** | 无代码资源包，只登记不加载程序集 |
| `v1-no-assemblies.tlx` | **Failed** | format 条目声明了 extension/import 但漏 `assembly` → assembly not found；**主程序不崩** |
| `v1-effect-unsupported.tlx` | **Failed** | type=effect 但声明的程序集 never-loaded.dll 缺失（assembly not found）；**主程序不崩** |
| `v1-bad-manifest.tlx` | **Failed**（或安装时报错） | manifest.json 损坏；**主程序不崩** |

> 通则：任何包加载失败都只跳过该包、在侧边栏/日志反映，绝不崩主程序。可把坏包和正常包一起装，确认正常包照常 Loaded。

---

## 三、暂不可测（功能尚未落地）

- 插件读宿主状态 / 日志注入（ILog / Context 尚未接通）
- 免重启热卸载（collectible，当前走重启式卸载）
- 多版本共存 compat（V1↔V2，尚无 V2）
