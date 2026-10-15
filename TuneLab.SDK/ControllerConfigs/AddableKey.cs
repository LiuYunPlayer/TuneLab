using System.Collections.Generic;
using System.Linq;

namespace TuneLab.SDK;

// 「可添加的一个命名键」候选 = ExtensibleObjectConfig 的 + 菜单的一项。与 AddableElement（array 的候选）对称，
// 但携 PropertyKey（Id=数据键、DisplayText=菜单/行标签）——键控容器的项有唯一键与标签，区别于 array 项的匿名。
// Template 提供该键加入后渲染并 seed 默认值的 config（宿主递归解析）。键唯一：每候选最多加一次，+ 菜单隐藏已存在的键。
// 分组与分隔线都与 ComboBoxItem 同构：分组项本身不可选、展开为子菜单，子项可再嵌套；分隔线（Separator）任意层级可放。
// 子项全已加入的分组随之隐藏；隐藏后所辖区段空了的分隔线也一并隐藏（宿主处理）。
public readonly struct AddableKey(PropertyKey key, IControllerConfig template)
{
    // 叶子 = 候选键；分组项只用其 DisplayText 作组名，Id 为 null（同 default(PropertyKey)，不对应任何数据键）。
    // Id 声明非空、分组处以 null! 塞入，理由同 Template。
    public PropertyKey Key { get; } = key;
    // 叶子恒非 null。分组项运行时为 null——声明保持非空是为不改已发布签名，读取方须先判 SubItems 再取 Template。
    public IControllerConfig Template { get; } = template;
    // 非 null = 分组项；允许空列表 = 空分组。仅经分组构造函数设置。
    public IReadOnlyList<AddableKey>? SubItems { get; }
    // 分隔线项（Key.DisplayText 为可选居中标签）。internal：插件只造不读（同 ComboBoxItem.IsSeparator）。
    internal bool IsSeparator { get; private init; }

    // 分组构造：组名 + 子项。收 IEnumerable 便于直接传 .Select(...) 结果，内部物化。
    public AddableKey(string displayText, IEnumerable<AddableKey> subItems) : this(new PropertyKey(null!, displayText), null!)   // null!：分组无键 Id 与 Template，见字段注释
    {
        SubItems = subItems as IReadOnlyList<AddableKey> ?? subItems.ToList();
    }

    // 分隔线（可带居中标签）：分段用，顶层与分组子项里都能放。与 ComboBoxItem.Separator 同形。无键 Id、无 Template（同分组）。
    public static AddableKey Separator(string? label = null) => new(new PropertyKey(null!, label), null!) { IsSeparator = true };
}
