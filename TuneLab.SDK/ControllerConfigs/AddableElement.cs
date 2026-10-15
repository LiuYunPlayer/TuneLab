using System.Collections.Generic;
using System.Linq;

namespace TuneLab.SDK;

// 「可添加的一种元素类型」候选 = ListConfig 的 + 菜单的一项。刻意独立成类型（而非复用 List<IControllerConfig>）——
// 与 ListConfig.Elements 区分语义：这是「下一个元素可选的若干类型」选择集，不是「后续若干元素」位置序列。
// Template 提供该类型新元素的 seed 默认值（宿主递归解析）+ 渲染配置；Label 是下拉菜单显示的类型名
//（单类型不弹菜单可省）。无隐式转换：C# 不允许以接口为源的用户定义转换，作者显式 new AddableElement(cfg)。
// 分组与分隔线都与 ComboBoxItem 同构：分组项本身不可选、展开为子菜单，子项可再嵌套；分隔线（Separator）任意层级可放。
// RS0027 不适用：与分组构造首参类型不相交（IControllerConfig vs string），既有调用 new AddableElement(cfg[, label]) 绑定不变。
#pragma warning disable RS0027
public readonly struct AddableElement(IControllerConfig template, string? label = null)
{
    // 叶子恒非 null。分组项运行时为 null（分组不代表任何元素类型）——声明保持非空是为不改已发布签名，
    // 读取方须先判 SubItems 再取 Template。
    public IControllerConfig Template { get; } = template;
    public string? Label { get; } = label;
    // 非 null = 分组项（本身不可选，展开为子菜单，Label 为组名）；允许空列表 = 空分组。仅经分组构造函数设置。
    public IReadOnlyList<AddableElement>? SubItems { get; }
    // 分隔线项（Label 为可选居中标签）。internal：插件只造不读（同 ComboBoxItem.IsSeparator，经 InternalsVisibleTo 给宿主渲染器）。
    internal bool IsSeparator { get; private init; }

    // 分组构造：组名 + 子项（空列表 = 空分组）。收 IEnumerable 便于直接传 .Select(...) 结果，内部物化。
    public AddableElement(string label, IEnumerable<AddableElement> subItems) : this(null!, label)   // null!：分组无 Template，见 Template 注释
    {
        SubItems = subItems as IReadOnlyList<AddableElement> ?? subItems.ToList();
    }

    // 分隔线（可带居中标签）：分段用，顶层与分组子项里都能放。与 ComboBoxItem.Separator 同形。Template 为 null（同分组）。
    public static AddableElement Separator(string? label = null) => new(null!, label) { IsSeparator = true };
}
#pragma warning restore RS0027
