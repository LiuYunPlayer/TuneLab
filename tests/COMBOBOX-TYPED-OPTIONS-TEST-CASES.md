# ComboBox 任意基础类型 + DisplayText（值/显示分离）· 测试用例

> 范围：`ComboBoxConfig` 的选项从纯 `string` 升级为 `ComboBoxOption`——选项值可为任意基础类型（统一存进单一 `PropertyValue`），并带独立 `DisplayText`（界面显示文本 ≠ 底层存储值）；默认值是「值」而非「索引」。
> 只测本次升级的新行为：值/显示分离、typed 值存取、默认值语义、多选三态、以及对既有 ComboBox 用法的回归。
> 属性面板的三态呈现、reconcile 复用等已由 `PROPERTY-TRISTATE-TEST-CASES.md`、`PROPERTY-CONDITIONAL-TEST-CASES.md` 覆盖，不在此重跑。

## 前置

- 安装并启用 `v1-suite.tlx`，新建工程，voice 引擎选 **TLSuiteVoice**，声库选 **`[v1-suite] Voice`**（基线声库，**不是** `[v1-suite] Conditional`）。
- 该声库 Note 面板含新增项 **`quality`**（ComboBox）：选项值是整数 **0 / 1 / 2**，但界面显示为 **Low / Mid / High**（DisplayText），默认 **Mid**（值=1）。同面板的 `style`（Soft/Normal/Strong，纯字符串选项）作回归对照。
- 合成产静音——只看属性面板行为，不涉及播放。
- 触发提醒：**ComboBox 选中即 commit。**
- 画几个音符备用。

## A · DisplayText：显示文本 ≠ 存储值

选中**单个**音符。

- [ ] `quality` 下拉**显示项**为 `Low` / `Mid` / `High`（不是 `0` / `1` / `2`）。
- [ ] 新选中的音符 `quality` 初始显示 **Mid**（默认值=值 1，非索引；若是索引语义会错显成 index 1 对应项——此处恰好也是 Mid，故再看 B 项的非平凡默认）。
- [ ] 展开下拉，三项文本依次 Low / Mid / High，顺序与声明一致。

## B · typed 值存取 + 持久化（存的是值、不是显示文本）

- [ ] `quality` 选 **High** → 保存工程 → 关闭并重新打开该工程 → `quality` 仍正确显示 **High**（底层存的是 int 值 2，重载按值反查高亮，而非存了 "High" 字面量）。
- [ ] 用文本编辑器打开保存的工程文件，确认该字段存的是**数字 2**（或 2.0），不是字符串 "High"。
- [ ] 选 **Low** → 撤销（Ctrl+Z）→ 回到上一个值；重做（Ctrl+Y）→ 回到 Low。

## C · 默认值=值语义（非索引）

- [ ] 新画一个音符并选中 → `quality` 初始为 **Mid**（构造时默认值传的是「值 1」，对应 Mid 项）。
- [ ] `style`（对照项）新音符初始为 **Normal**（默认值传的是字符串 "Normal"，非索引）。

## D · 多选三态

- [ ] 选中两个音符，把其中一个的 `quality` 设为 **Low**、另一个设为 **High**，再框选这两个 → `quality` 显示占位 **(Multiple)**、不选中任何项。
- [ ] 此时在 `quality` 选 **Mid** → 两个音符的 `quality` 都写成 Mid（再分别单选确认）。
- [ ] 选中两个 `quality` 同为 High 的音符 → 显示 **High**（全等给该值，非 Multiple）。

## E · 回归（既有 ComboBox 用法不受影响）

- [ ] `style`（字符串选项 ComboBox）选择、显示、多选 (Multiple) 均正常。
- [ ] 属性侧栏 **Preset** 下拉：增删预设、选择、应用均正常。
- [ ] 顶部 **量化** 下拉（FunctionBar）：切换量化档位，音符吸附按所选档位生效（按选中位置联动业务值）。
- [ ] 设置窗口：**语言** 下拉切换生效；**采样率 / 缓冲区大小** 下拉（字符串桥到 int 设置）切换生效。
