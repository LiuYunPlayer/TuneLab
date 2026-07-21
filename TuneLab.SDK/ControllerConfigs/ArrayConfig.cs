using System.Collections.Generic;

namespace TuneLab.SDK;

// 定长数组：逐 index 声明 element config，允许异型（第 0 位 TextBox、第 1 位 ComboBox 也行——
// 多类型其实更宜用 ObjectConfig，但不阻止）。长度 = Elements.Count、不可增删；值落为 PropertyArray，
// 第 i 个元素由 Elements[i] 渲染并双向绑定到 array[i]。与 ObjectConfig 同为复合型、不实现 IValueConfig：
// 默认数组由宿主递归各位默认值求得。元素缺席 / 存值类型与该位 config 不符 → 退回该位默认值（不做特殊呈现）。
// 构造函数全封，只走静态工厂。构造即拷贝传入序列（[.. elements]）：值语义由构造保证，与调用方构造后对原
// list 的任何改动无关（对齐 PropertyObject 纪律；元素 config 各自构造时已封好自己那层，逐层归纳封死整树）。
public sealed class ArrayConfig : IControllerConfig
{
    public IReadOnlyList<IControllerConfig> Elements { get; private init; } = null!;

    private ArrayConfig() { }

    public static ArrayConfig Create(IReadOnlyList<IControllerConfig> elements)
        => new() { Elements = [.. elements] };
}
