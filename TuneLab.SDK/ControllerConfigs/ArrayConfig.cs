using System.Collections.Generic;

namespace TuneLab.SDK;

// 定长数组：逐 index 声明 element config，允许异型（第 0 位 TextBox、第 1 位 ComboBox 也行——
// 多类型其实更宜用 ObjectConfig，但不阻止）。长度 = Elements.Count、不可增删；值落为 PropertyArray，
// 第 i 个元素由 Elements[i] 渲染并双向绑定到 array[i]。与 ObjectConfig 同为复合型、不实现 IValueConfig：
// 默认数组由宿主递归各位默认值求得。元素缺席 / 存值类型与该位 config 不符 → 退回该位默认值（不做特殊呈现）。
public class ArrayConfig : IControllerConfig
{
    public required IReadOnlyList<IControllerConfig> Elements { get; init; }
}
