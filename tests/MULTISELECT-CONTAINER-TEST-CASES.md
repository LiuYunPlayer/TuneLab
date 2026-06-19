# 多选下容器属性的三态合并 — 真机测试用例

多选音符时，note 属性面板里的容器控件（数组 `phonemes`/`pair`、变长键控对象）按统一规则合并：

- **数组**按 **index 对齐**：长度取所选音符里最长的那个。**「缺位 = 该位默认值」**：某音符短于该 index 时，该位按元素默认值参与比较——故各音符该位全等给该值、不等给 **Multiple**。这与单选「absent 数组显示 seed 默认」一致：一个音符物化了数组、另一个仍未设时，恰等默认的那些位不会误报 Multiple。
- **对象 / 变长键控对象**按 **key 并集**：合并所有音符出现过的键；某键各音符全有且全等 → 显示该值；不等或缺于部分音符 → Multiple。
- **写「编辑即补齐」**：编辑某位时，短于该位的音符先按各位默认值补齐到该长度、再写入（即把不等长数组调成一样长）。

数据核心已由单测覆盖（`tests/TuneLab.Tests/MultiSelectMergeTests.cs`）；本文件测 GUI 交互真机回归。

**前置**：同 `PROPERTY-ARRAY-TEST-CASES.md`（装 `v1-suite.tlx`、声库 `[v1-suite] Conditional`）。
新建若干音符，在 note 面板 letters 框给不同音符设不同字母以制造不同的 phonemes seed/元素数。

## 数组多选（note 面板 phonemes / pair）

| 用例 | 操作 | 验证点 |
|---|---|---|
| 等长同值 | 选 2 个 phonemes 完全相同的音符 | 各行正常显示该值；编辑某行 → 两音符同步改 |
| 等长不同值 | 选 2 个同长但某位不同的音符 | 不同的那行显示 Multiple（dash/空），同值行正常；改该行 → 两音符都被覆盖成新值 |
| 不等长 | 选 phonemes 长 2 与长 3 的音符 | 显示 3 行（取最长）；第 3 行短音符按默认值参与，故与长音符该位不等 → Multiple；前两行按等长规则三态 |
| 编辑即补齐 | 在「仅长音符有」的第 3 行编辑提交 | 短音符被补齐到长度 3（中间位填默认）后写入该值 → 两音符等长、第 3 位同值 |
| 添加 | 点列表 `+` 选 Phoneme | 所有选中音符各自尾部追加一行（各加一个，不要求对齐绝对 index） |
| 删除 | 悬浮某行点 ✕ | 拥有该 index 的音符删该位；缺该位的音符不受影响 |
| **定长数组 pair（缺位=默认）** | 单独改 A 的第 1 个 pair 值（→A 物化、B 仍未设），再多选 A+B | 改过的第 1 行显示 Multiple；**第 2 行显示共同默认值 0.8（不误报 Multiple）** ← 本轮修复点 |
| undo/redo | 上述增删改后 Ctrl+Z/Y | 一次撤销单元整体回退/重做所有被扇出的音符 |

**已知限制——纯 seed 且 seed 源多值的数组多选显示为空**

夹具里 `phonemes` 从未编辑时按 `letters` 逐字符 seed。多选两 note、letters 不同（如 `jin` / `kin`）时，
合并快照里 `letters` = `Multiple`，`GetNotePropertyConfig` 据此算出的 phonemes 长度为 0 → phonemes 显示空（仅 `+`）。
原因：seed 是 config 产物（`f(letters)`），不是数据，不进数据合并；多选只把数据合并后算一次 config（方案 A），
`f` 读到 `Multiple` 的 letters 无法还原各 note 各自的 seed。**这是预期边界，非数据层 bug**。
要合并编辑：先在任一 note 编辑过一个音素（物化 phonemes），多选即走真实数组合并、正常显示 `[Multiple, 同, 同]`。

## 对象多选（note 面板 vibrato 等嵌套对象）

| 用例 | 操作 | 验证点 |
|---|---|---|
| 子字段三态 | 选 vibrato.depth 不同的音符 | depth 显示 Multiple，其余同值子字段正常；改 depth 扇出所有音符 |
| 深层嵌套 | 选 vibrato.lfo.range.min 不同的音符 | 逐层导航进去，min 显示 Multiple、扇出写正常 |

## 变长键控对象多选（note 面板 `Tags`，候选 Red/Green/Blue）

夹具在 note 面板放了变长键控容器 `Tags`（part 面板的 Mixed Speakers 是单选场景，故另置一个到可多选的 note 面板）。

| 用例 | 操作 | 验证点 |
|---|---|---|
| 键并集 | 给音符 A 加 Red、音符 B 加 Green，多选 A+B | `Tags` 下显示 Red + Green 两行（并集） |
| 公共键 | 给 A、B 都加 Red，多选 | Red 行只显示一次（两音符共有） |
| `+` 加键扇出 | 多选 A+B，点 `+` 选 Blue | A、B 都加上 Blue |
| 删键扇出 | 多选下悬浮某行点 ✕ | 拥有该键的音符都删该键 |
| undo/redo | 加/删后 Ctrl+Z/Y | 逐键回退/重做、扇出到所有选中音符 |
