# PropertyArray 数据核心测试用例

对应 `docs/effect-migration.md` §三.29 的数据核心 ①②③（值容器 / live-doc / 序列化）+ ④-B-1（live-bind 数组导航层）。
④-B-2（面板控件）/④-C（测试插件夹具）未做，**不在此测**——届时建独立测试文档，不污染本基线。

自动化测试：`tests/TuneLab.Tests/PropertyArrayTests.cs`（xUnit，21 例，全绿）。
运行：`dotnet test tests/TuneLab.Tests/TuneLab.Tests.csproj --filter "FullyQualifiedName~PropertyArrayTests"`

## ① 地板 `PropertyArray` + `PropertyValue` 的 Array 臂

| 用例 | 验证点 |
|---|---|
| `Array_Construction_CopiesFirstLevel` | 构造拷入第一层；构造后改源序列不影响已建数组（值语义） |
| `Array_Empty_IsZeroLength` | `Empty` 单例零长；空数组彼此相等 |
| `Array_DeepEquals_OrderSensitive` | 深相等**顺序敏感**（与 PropertyObject 键集无序相对）；长度不同不等；相等数组同 hash |
| `Array_Nesting_DeepEquals` | 数组套对象、数组套数组的递归深相等 |
| `PropertyValue_ArrayArm_Roundtrips` | `Create`/隐式转换 → `Type==Array`、`IsArray`、`ToArray`、`To<PropertyArray>`、`TypeIs<PropertyArray>` 一致 |
| `PropertyValue_ArrayArm_DistinctFromOtherTypes` | 标量 `IsArray==false`、`ToArray` 失败；array 值 ≠ 标量、≠ object 值 |

## ② live-doc `DataPropertyArray`（= `DataObjectList<DataPropertyValue>`）

| 用例 | 验证点 |
|---|---|
| `DataArray_SetInfoGetInfo_Roundtrips` | `SetInfo`→`GetInfo` 往返全等，含嵌套对象/数组/空数组元素递归触底（规范化 Canonicalize 生效） |
| `DataArray_InsertRemoveSet_ReflectInGetInfo` | `Insert`/`Add`/`SetValue`/`RemoveAt` 正确反映到快照 |
| `DataArray_Insert_UndoRedo_IsElementGranular` | 中插经 `DataDocument` Commit 后 Undo/Redo 逐元素粒度回退/重做（验证选 DataObjectList 而非 DataList 的收益） |

## ③ TLP/CBOR 递归读写

经 `TuneLabProjectCbor.WritePropertyObject`/`ReadPropertyObject`（已改 `internal static`、`InternalsVisibleTo("TuneLab.Tests")` 直调）往返。

| 用例 | 验证点 |
|---|---|
| `Cbor_RoundTrips_ArraysAndNesting` | 标量/嵌套对象/标量数组/对象数组/数组套数组往返全等 |
| `Cbor_PreservesEmptyArray_AsPresentValue` | **present-`[]`**（用户显式清空）不被当空跳过——往返后 key 仍存在、值是长度 0 的数组 |
| `Cbor_PreservesNullElement_PositionInArray` | 数组 null 元素写成 CBOR null 占位、读回 `PropertyValue.Null`，不塌缩位置（数组按位写齐） |

## ④-B-1 live-bind 数组导航层（`IDataPropertyArray`：稳定 token 寻址 / 懒导航 / 结构事件）

元素以**稳定 token** 寻址（token = 元素槽实例身份的惰性赋号，跨增删/undo/redo 不变），token 当 key 复用
`Object`/`Array`/`GetValue`/`SetValue`，面板用现成字段绑定机制原位 live-bind 每个元素、无需感知 index。

| 用例 | 验证点 |
|---|---|
| `Tokens_OrderMatchesElements_AndAreDistinct` | `Tokens` 顺序随列表、互异；`token[i]` 寻址第 i 个元素值 |
| `Token_StableAcrossInsertAndRemove` | 中插/删除后存活元素 token 不变，中插元素拿新 token（keyed-diff 的稳定行键来源） |
| `Token_StableAcrossUndoRedo` | Undo 回退后存活元素 token 复原；Redo 复活同一元素实例 → 同一 token（实例身份稳定） |
| `SetValueByToken_EditsElementInPlace` | 经 token 原位写元素标量值 |
| `ObjectByToken_NavigatesIntoObjectElement` | `Object(token)` 导航进对象元素、读写其子字段回写到快照 |
| `ArrayByToken_NavigatesIntoArrayElement` | `Array(token)` 导航进数组元素、原位增元素 |
| `ObjectArrayNavigation_IsLazy_ReadDoesNotCreate_WriteCreates` | `obj.Array(key)` 懒导航：读不创建（key 不入序列化，保 presence 语义）、写按需建路径 |
| `StructureModified_FiresOnStructuralChange_NotOnValueEdit` | 结构事件只在增删触发；元素值原位编辑不触发 |
| `StaleToken_GetReturnsDefault_SetIsNoOp` | 陈旧 token（元素已删）读退默认值、写 no-op（不抛、不复活） |

## ④-B-2 / ④-C 面板控件 + 真机回归（Array/ListController + 测试夹具）

控件无标题、行内紧凑布局（独立元素渲染器，不复用对象字段「标题+分隔符」排布）；元素按稳定 token keyed-diff（复用行、
不打断编辑/拖动）；越界（seed）位惰性绑定（读默认、首写物化整段）。**真机测试**（GUI 交互，无法单测）。

**前置**：`dotnet build tests/TestPlugins.slnx -c Debug` → `pwsh tests/pack-tlx.ps1` → 把 `tests/tlx/v1-suite.tlx`
拖进 TuneLab 窗口安装。新建 part，属性面板把声库设为 **`[v1-suite] Conditional`**，选中一个音符（note 面板出条件控件）。
在 note 面板的 **letters** 文本框输入若干字母（如 `iian`）以驱动 seed。

| 用例 | 操作 | 验证点 |
|---|---|---|
| seed 显示（List） | letters 设 `iian`、phonemes 从未写 | phonemes 列表显示 4 行（`i` `i` `a` `n`），重复 `i` **不跳过**（对照上方 key-unique 滑条只 3 个 `i/a/n`） |
| seed 物化（List） | 编辑任一 seed 行文本并提交 | 整段 seed 物化为真实元素；行不塌、计数稳定；只产生一个撤销单元 |
| 添加（多候选下拉） | 点列表底部 `+` | 弹菜单 `Phoneme`/`Rest`；选 `Phoneme` 追加空行、选 `Rest` 追加 `-` 行 |
| 可重复 | 连续 `+`→Phoneme 添两行、各输入 `i` | 两个 `i` 并存（列表可重复，区别于对象 key 唯一） |
| 行删除（悬浮） | 鼠标悬浮某行 | 右侧浮现 ✕，单击删该行；非悬浮时 ✕ 隐藏但行宽不抖（Opacity 占位） |
| 原位编辑不丢焦点 | 在某行文本框连续输入 | keyed-diff 复用行，输入不被打断、不失焦 |
| 定长数组 seed（Array） | 观察 `pair`（未写时） | 固定显示 2 行滑条、默认 0.2/0.8；无 `+`、无删除钮 |
| 定长数组物化 | 拖动 `pair` 任一滑条 | 整段物化为 2 真实元素；值落库（首次拖动有一次性重建——见限制） |
| CBOR 往返 | 存盘 → 重开工程 | phonemes（含重复）、pair 的值与顺序原样恢复 |
| undo/redo | 添加/删除/编辑后 Ctrl+Z / Ctrl+Y | 逐元素粒度回退/重做，token 稳定、行不乱 |

## ④-D AddableObjectConfig（变长键控容器）+ 多说话人混音式自动化联动

object 家族的变长兄弟：项有唯一键 + 标签（`PropertyKey.DisplayText`）、按键寻址、`+` 从候选键挑（隐藏已存在键）。
键控访问天生懒（`Object`/`GetValue` 读不建、写物化 + presence），无需 array 的越界 seed 适配。**真机测试**。

**前置**：同上（装 `v1-suite.tlx`、声库 `[v1-suite] Conditional`）。看 **part 属性面板**（不是 note）的 **Mixed Speakers** 项。

| 用例 | 操作 | 验证点 |
|---|---|---|
| 起步为空 | 刚建 part | Mixed Speakers 下只有 `+`、无行 |
| 加键（多候选下拉） | 点 `+` | 弹菜单 Alice/Bob/Carol；选 Alice → 出现 `Alice` 行（仅标题 + 悬浮 ×，presence-only 条目） |
| **自动化联动** | 加 Alice 后看参数区/自动化默认值行 | 自动出现一条 `Alice` 混音自动化曲线（验证 `+ → part 属性物化 → GetAutomationConfigs 重算 → 曲线按钮出现`，即多说话人混音的主诉求） |
| 候选隐藏已存在 | 再点 `+` | 菜单只剩 Bob/Carol（Alice 已隐藏）；选 Bob → `Bob` 行 + `Bob` 自动化 |
| 单候选直加 | 已加 2 个后点 `+` | 只剩 1 候选 → **直接添加**、不弹菜单 |
| 候选耗尽 | 加满 3 个 | `+` 按钮隐藏（无候选） |
| 删键 | 悬浮 `Alice` 行点 × | `Alice` 行消失 + `Alice` 自动化曲线消失 |
| CBOR 往返 | 存盘→重开 | 已选 speaker 集合 + 各自动化原样恢复 |
| undo/redo | 加/删后 Ctrl+Z/Y | 逐键回退/重做，自动化随之增减 |

> 注：speaker 选择存为 part 属性 `speakers`（键控对象），自动化 = `f(该属性)`；曲线随属性变化自动增减的 wiring
> 由宿主既有链承担（`MidiPart.OnPartPropertiesModified → RebuildAutomationConfigs → AutomationConfigsModified`），无需新增。

**已知限制（记录在案）**
- **seeded 滑条首次拖动一次性重建**：seed 位（虚拟行）首次写入物化整段→reconcile 用真实 token 行替换虚拟行，
  对连续提交的滑条会在第一拍重建（值已落、之后顺滑）。离散提交控件（TextBox/ComboBox/CheckBox）无此现象。
- **复合（Object/Array）seed 元素物化前不渲染虚拟行**：仅标量 seed 走虚拟绑定；复合元素的 seed 显示留后续
  （需 app 侧递归 seed 视图）。
- **多选下数组编辑**：`MultipleDataPropertyObject.Array` 现阶段降级（单成员直通、0/多成员空视图），
  多选三态合并方案待本话题收尾后讨论。
- **CBOR 端到端单测**：序列化逻辑本身已由上方 ③ 直调单测覆盖（含空数组/null 元素）；真机往返见本节。
