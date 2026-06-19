# PropertyArray 数据核心测试用例

对应 `docs/effect-migration.md` §三.29 的数据核心 ①②③（值容器 / live-doc / 序列化）。
④（ArrayConfig/ListConfig + 控件 + 标签改制）未做，**不在此测**——届时建独立测试文档，不污染本基线。

自动化测试：`tests/TuneLab.Tests/PropertyArrayTests.cs`（xUnit，12 例，全绿）。
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

## 不在本范围（记录在案）

- **CBOR 端到端**（真机存盘/重开整工程）：当前无 UI 消费者能往 part Properties 写入数组（④ 未做），故无法走真机产出路径；本文档以 `internal static` 直调单测覆盖了序列化逻辑本身（含空数组/null 元素边界）。④ 落地后再补真机往返。
- **ArrayConfig/ListConfig/AddableElement、PropertyKey 标签改制、面板渲染**：plane ④，随真实 UI 消费者落地时单测 + 真机。
