# 属性面板「多值 / 无效」三态呈现 · 测试用例

> 独立需求独立文档。本文只覆盖「属性面板控件在 **多值（Multiple）** 与 **无效（Invalid）** 两种态下的呈现与交互」这一改动影响的范围。
> **不涉及** 单选正常编辑/撤销/刷新（live-bind 基线，见 #12，本次未改其正常路径）、effect 自动化、format/voice 合成——无需重跑。

三态定义：
- **Concrete**：单选，或多选各对象该字段**全等** → 显具体值，正常可编辑。
- **Multiple（多值）**：多选 ≥2 且该字段**不完全相等** → 显"多值"占位，编辑即扇出收敛到所有对象。
- **Invalid（无效）**：**未选中任何音符** → 控件在半透明遮罩下呈空态（编辑被遮罩挡，无实义）。

## 准备
1. 构建测试插件 + 打包：
   ```powershell
   dotnet build tests/TestPlugins.slnx -c Debug
   powershell -File tests/pack-tlx.ps1
   ```
2. 装 `tests/tlx/v1-suite.tlx`（拖进窗口或侧边栏 → Install Extension）。
3. 新建工程 → 建一个 MidiPart，voice 选 **Suite shared-infra voice**（`TLSuiteVoice`）。
4. 画**至少 3 个音符**，打开右侧 **Note Properties** 面板。

该测试 voice 的 NoteProperties 专为本测试声明了四类控件各一项 + 一个嵌套对象：
`tension`（Slider）、`accent`（CheckBox）、`label`（TextBox）、`style`（ComboBox：Soft/Normal/Strong）、
`vibrato`（嵌套 ObjectConfig，内含 `depth` Slider + `on` CheckBox）。

---

## A · Multiple（多值）呈现

操作：先各音符设成**不同值**——选中音符 1 把 `tension` 拖到 0.5、`accent` 勾上、`label` 输 `a`、`style` 选 Strong；再单选音符 2 改成另一组值（`tension` 0、`accent` 不勾、`label` 输 `b`、`style` Soft）。然后**框选/全选这些音符**。

- [ ] **Slider（tension）**：滑轨空（thumb 不显），右侧数值标签显 **`—`**（em dash），不是某个具体数。
- [ ] **CheckBox（accent）**：高亮底 + 中间 **dash（短横）**，不是勾、也不是空框。
- [ ] **TextBox（label）**：显灰色占位 **`(Multiple)`**（watermark，非实体文字）。
- [ ] **ComboBox（style）**：未选中任何项，占位文字显 **`(Multiple)`**。

## B · Invalid（无效，无选中）呈现

操作：在 piano 空白处点一下**取消所有音符选中**（Note Properties 面板压一层半透明黑遮罩，但控件仍可见）。

- [ ] **Slider**：滑轨空，数值标签 **留空**（不是 `—`、不是 `-`、不是某数）。
- [ ] **CheckBox**：**空框**（透明底、无勾、无 dash）。
- [ ] **TextBox**：**空**（无文字、无 watermark）。
- [ ] **ComboBox**：未选中、占位 **留空**（不是 `(Null)`、不是 `(Multiple)`）。
- [ ] 遮罩仍在、四个控件都看得见（不是整块被清空/挡死）。

## C · Multiple → 编辑收敛

操作：在 A 的多选多值态下，逐个控件做一次编辑。

- [ ] **Slider**：拖动滑块过程中标签实时显**拖动中的数值**（不是一直 `—`）；松手后所有选中音符的 `tension` 都变成该值，标签显该具体数（脱离多值态）。
- [ ] **CheckBox**：点一下 → 所有选中音符 `accent` 统一为同一勾选态，控件显正常勾/空（脱离 dash）。
- [ ] **TextBox**：聚焦输入 → **从空白起编**（不会先把 `(Multiple)` 当文字编辑）；提交后所有音符 `label` 统一，显该文本。
- [ ] **ComboBox**：选某项 → 所有音符 `style` 统一为该项。
- [ ] 每种编辑都是**一个撤销单元**：按一次 Undo 全部选中音符一起回退。

## E · 嵌套对象内叶子的三态（递归）

> 验证 `IDataPropertyObject` 的 field 仍可继续是 object：嵌套 `ObjectConfig` 不合成独立数据源，靠 `PropertyPath` 深 Key 寻址，叶子三态沿递归组合。

操作：展开 `vibrato` 折叠面板，里面是 `depth`（Slider）+ `on`（CheckBox）。让多个音符的 `vibrato.depth` / `vibrato.on` **不同**（如音符1 depth=0.5/on=勾、音符2 depth=0.2/on=不勾），多选。

- [ ] `vibrato.depth`（嵌套 Slider）：多值 → 空轨 + 标签 `—`；编辑扇出到所有音符。
- [ ] `vibrato.on`（嵌套 CheckBox）：多值 → 高亮底 + dash。
- [ ] 无选中时：嵌套 `depth` 标签留空、`on` 空框（Invalid 同样递归到嵌套叶子）。
- [ ] 把某叶子（如 depth）在多个音符上设成**全等**值 → 嵌套控件显该具体值，不误报多值。

## D · Concrete（全等不误报多值）

操作：选中**值已全等**的多个音符（如把它们 `style` 都设 Normal 后再多选）。

- [ ] 各控件显**该具体值**，不显 `(Multiple)` / `—` / dash。
- [ ] 单选任一音符，控件显其具体值，正常可编辑（基线未回退）。

---

## 备注（设计边界，非缺陷）
- **effect 参数面板 / part 固定属性**恒为单对象，永远只有 Concrete 态，不会出现多值/无效——不在本测试范围。
- 哨兵（Multiple/Invalid）是**面板瞬态**，**不写盘**：保存再打开工程，属性恢复为各音符的具体值（多值态不会被持久化）。可顺手验证：多选多值态下保存 → 重开 → 不报错、值为各自具体值。
