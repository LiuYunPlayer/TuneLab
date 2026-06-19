namespace TuneLab.SDK;

// 「可添加的一种元素类型」候选 = ListConfig 的 + 菜单的一项。刻意独立成类型（而非复用 List<IControllerConfig>）——
// 与 ListConfig.Elements 区分语义：这是「下一个元素可选的若干类型」选择集，不是「后续若干元素」位置序列。
// Template 提供该类型新元素的 seed 默认值（宿主递归解析）+ 渲染配置；Label 是下拉菜单显示的类型名
//（单类型不弹菜单可省）。无隐式转换：C# 不允许以接口为源的用户定义转换，作者显式 new AddableElement(cfg)。
public readonly struct AddableElement(IControllerConfig template, string? label = null)
{
    public IControllerConfig Template { get; } = template;
    public string? Label { get; } = label;
}
