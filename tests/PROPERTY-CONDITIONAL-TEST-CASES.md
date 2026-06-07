# 条件属性面板（config = f(context)）· 测试用例

> 范围：属性面板的 config 从静态声明改为 **`ObjectConfig = f(context)` 纯函数**，宿主在属性 **commit** 时按当前值重算整棵 config 并 **keyed-diff** 到控件树。
> 只测本次新增的「条件面板」行为：显隐 / 换控件 / 控件参数随值变 / 动态数量控件 / part→note 沿链 / 多选降级 / commit 触发与复用。
> 静态面板的渲染、三态呈现、深层导航等已由 `PROPERTY-TRISTATE-TEST-CASES.md`、`PROPERTY-NAVIGATION-TEST-CASES.md` 覆盖，不在此重跑。

## 前置

- 安装并启用 `v1-suite.tlx`，新建工程，voice 引擎选 **TLSuiteVoice**，声库选 **`[v1-suite] Conditional`**（注意：不是基线测试用的 `[v1-suite] Voice`）。
- 该声库的 Note 面板初始有三项：`mode`(ComboBox: Simple/Advanced)、`letters`(TextBox)、`pick`(ComboBox)。Part 面板有一项：`fromPart`(CheckBox)。
- 合成产静音——只看属性面板行为，不涉及播放。
- 触发时机提醒：**ComboBox / CheckBox 选中即 commit；TextBox 失焦或回车才 commit；Slider 松手才 commit。** 条件面板只在 commit 时重算（拖动 / 输入中途不重算）。
- 画几个音符备用。

## A · 显隐 / 换控件（mode 驱动）

选中**单个**音符。

- [ ] 初始 `mode=Simple`，面板只有 `mode` / `letters` / `pick` 三项。
- [ ] `mode` 切到 **Advanced** → 面板多出 `gain`(Slider) 与 `detail`(TextBox) 两项。
- [ ] 切回 **Simple** → `gain` / `detail` 消失，其余项保持。
- [ ] 反复切换 mode：`mode` / `letters` / `pick` 控件本身不重建、不闪烁，已填的 `letters` 文本保留（同 key 同类型复用控件）。

## B · 控件参数随值变（pick 选项 = letters 逐字符）

- [ ] `letters` 输入 `abc` 回车 → `pick` 下拉的选项变成 a / b / c（内容 + 数量都随 letters）。
- [ ] 改 `letters` 为 `xy` 回车 → `pick` 选项变成 x / y。
- [ ] 清空 `letters` 回车 → `pick` 选项变成 `(empty)`。
- [ ] 输入过程中（未失焦 / 未回车）`pick` **不**实时变化；失焦 / 回车 commit 后才更新（验证 commit 触发，非每次按键）。

## C · 动态数量控件（字母 → 滑条）

- [ ] `letters` 输入 `abc` 回车 → 面板下方出现 **3 个滑条**，标题分别为 a / b / c。
- [ ] 拖动滑条 `b` 改其值并松手 → 值写入；改 `letters` 为 `abcd` 回车 → 多出滑条 d，**a / b / c 的值保留**（按 key 对齐 reconcile，b 仍是你刚设的值）。
- [ ] 改 `letters` 为 `abd` 回车 → 滑条 c 消失，a / b 的值仍保留，d 出现。
- [ ] **key 唯一边界**：`letters` 输入 `aab` 回车 → 只出现 **a、b 两个**滑条（重复的 a 被跳过）。说明：有序、可重复的列表（同字符多次）需要 array，属正交的独立话题，不在本特性内。

## D · commit 触发：编辑 / 拖动中途不重构

- [ ] 在某字母滑条上**按住拖动**（不松手）→ 拖动过程中面板结构不变（不增删控件、不换控件），松手 commit 后才可能重算。
- [ ] 在 `letters` 文本框中**逐字输入**（不失焦）→ 输入过程中 `pick` 选项与字母滑条都不动；失焦 / 回车后一次性更新。
- [ ] 全程不应出现"拖着拖着控件消失 / 焦点跳走 / 文本框被清空"。

## E · part → note 沿链重算

- [ ] 选中 part，Part 面板勾选 **`fromPart`** → 当前选中音符的 Note 面板多出 `partGain`(Slider) 一项（part 值 commit 沿链触发 note 面板重算）。
- [ ] 取消勾选 `fromPart` → `partGain` 从 Note 面板消失。
- [ ] 勾选状态下切换不同音符 → 每个音符的 Note 面板都带 `partGain`（context 的 part 值对所有 note 生效）。

## F · 多选三态降级

选中**多个**音符。

- [ ] 各音符 `letters` **不同**（如①`ab` ②`cd`）→ `letters` 文本框呈多值态；`pick` 退化为 `(empty)`、无字母滑条（letters 合并为 Multiple 哨兵，插件 `GetString` 安全降级到默认空串）。
- [ ] 让各音符 `letters` **相同**（都设为 `ab`）→ `pick` 选项变 a / b，并出现 a / b 两个滑条（合并值确定 → 正常求值）。
- [ ] 各音符 `mode` 一个 Simple 一个 Advanced → `mode` 呈多值态，`gain` / `detail` **不**显示（mode 合并为 Multiple → 降级到默认 Simple 分支）。
- [ ] 多选下编辑 `letters`（设为同值）→ 扇出到所有选中音符；随后字母滑条按该值出现，且对所有音符一致。

## G · 撤销 / 默认值

- [ ] 编辑某字母滑条后 Undo → 该编辑回退，面板结构随之回到编辑前（撤销改了值 → commit 态变化 → 面板重算）。
- [ ] 选 part 的 None preset（Reset to defaults）→ part 属性回默认（`fromPart` 取消勾选），note 面板的 `partGain` 随之消失。
