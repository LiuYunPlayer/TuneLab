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

## 不在本范围（记录在案）

- **CBOR 端到端**（真机存盘/重开整工程）：当前无 UI 消费者能往 part Properties 写入数组（④-B-2/C 未做），故无法走真机产出路径；本文档以 `internal static` 直调单测覆盖了序列化逻辑本身（含空数组/null 元素边界）。控件落地后再补真机往返。
- **多选下数组编辑**：`MultipleDataPropertyObject.Array` 现阶段降级（单成员直通、0/多成员返回空视图），多选三态合并方案待 ④ 收尾后讨论。
- **ArrayConfig/ListConfig/AddableElement、PropertyKey 标签改制、面板渲染**：plane ④-B-2，随真实 UI 消费者落地时单测 + 真机。
