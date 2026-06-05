# 属性面板导航式数据模型 · 测试用例

> 范围：属性面板由扁平路径寻址（`PropertyPath` 深 Key）改为逐层导航（`Object(key)` + 单层叶子读写）后，
> **多层嵌套对象**在单选 / 多选下的导航、三态、懒建路径、撤销、preset 是否正确。
> 只测本次重构受影响的范围；单层三态呈现（控件外观、扇出无闪烁等）已由 `PROPERTY-TRISTATE-TEST-CASES.md` 覆盖，不在此重跑。

## 前置

- 安装并启用 `v1-suite.tlx`，新建工程选 voice 引擎 **TLSuiteVoice**（声库 `Suite Voice`）。
- 该 voice 的 Note 属性含 **3 层嵌套对象**：`vibrato` → `depth`(Slider) / `on`(CheckBox) / `lfo`，`lfo` → `rate`(Slider) / `wave`(ComboBox) / `range`，`range` → `min`(Slider) / `max`(Slider)。
- 该 voice 合成产静音——本组只看属性面板行为，不涉及播放。
- 画几个音符，展开 `vibrato` → `lfo` → `range` 各级折叠面板。

## A · 单选深层导航 + 懒建路径

选中**单个**音符（其 `vibrato` 此前从未编辑过，数据里无 `vibrato`）。

- [ ] 面板正常展开三层折叠面板，各叶子显示**默认值**（`rate`=5、`wave`=Sine、`min`=0、`max`=1），不报错。
- [ ] 编辑最深层 `vibrato.lfo.range.min` → 数值正确写入并回显。
- [ ] 此时保存/查看工程数据：仅 `vibrato.lfo.range.min` 这条路径被创建，**不产生**多余的空对象层（懒建只建被写到的路径）。
- [ ] 对一个**完全没碰过** `vibrato` 的音符，仅展开面板浏览、不编辑 → 不应凭空给它写入任何 `vibrato` 数据（展开/绑定不创建、不进撤销）。

## B · 深层撤销为单一单元

- [ ] 编辑 `vibrato.lfo.range.max` 一次 → 按一次 Undo 完整回退该次编辑（含可能随之懒建的中间对象路径），面板回到编辑前状态。
- [ ] Redo 一次 → 恢复该编辑。

## C · 多选深层三态（复合递归）

选中**多个**音符。

- [ ] 让各音符的 `vibrato.lfo.rate` **相同** → 显示该具体值（Concrete）。
- [ ] 让其中至少一个音符的 `vibrato.lfo.rate` **不同** → 显示多值态（Slider 空轨 + 标签 `—`）。
- [ ] 让各音符的 `vibrato.lfo.range.min`（最深层）不同 → 同样正确显示多值态——验证多选复合在 **3 层深**仍正确递归。
- [ ] 编辑深层叶子（如 `vibrato.lfo.wave` 选某项）→ 扇出到**所有**选中音符，统一为该值；过程无中间态闪烁。

## D · 多选中"部分成员缺该嵌套"

构造：音符①已编辑过 `vibrato.lfo.rate=10`；音符②**从未编辑** `vibrato`（数据里无该嵌套）。同时选中①②。

- [ ] `vibrato.lfo.rate` 显示**多值态**（`—`）——因为②缺该嵌套时读出默认值 5，与①的 10 不等。
      （这是导航模型的关键正确性点：缺嵌套的成员经懒视图读默认值仍正确参与三态比较，而非被跳过导致误判全等。）
- [ ] 在多选下编辑 `vibrato.lfo.rate` → ①②都被写入该值（②按需懒建 `vibrato.lfo` 路径）；之后两者一致。
- [ ] 上述多选编辑按一次 Undo 一起回退（同一文档归一个撤销单元）。

## E · preset 往返（深层导航写入）

- [ ] 选中 part，编辑若干深层属性（如 `vibrato.lfo.range.min/max`）后，「Save As」存为 preset。
- [ ] 改动这些属性，再「Apply」该 preset → 深层属性正确恢复到 preset 值（preset 套用经逐层导航写入各层）。
- [ ] 选 None preset（Reset to defaults）→ 所有属性（含深层嵌套）回到 config 默认值。

## F · dirty 触发（改用 Modified 冒泡）

- [ ] 改任意音符属性（含深层嵌套）后，工程进入需重合成状态、播放/导出走新值——验证 note 属性 dirty 改订 `Properties.Modified`（冒泡覆盖全部嵌套写）后仍正确触发。
