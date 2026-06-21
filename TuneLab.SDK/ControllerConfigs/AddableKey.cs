namespace TuneLab.SDK;

// 「可添加的一个命名键」候选 = ExtensibleObjectConfig 的 + 菜单的一项。与 AddableElement（array 的候选）对称，
// 但携 PropertyKey（Id=数据键、DisplayText=菜单/行标签）——键控容器的项有唯一键与标签，区别于 array 项的匿名。
// Template 提供该键加入后渲染并 seed 默认值的 config（宿主递归解析）。键唯一：每候选最多加一次，+ 菜单隐藏已存在的键。
public readonly struct AddableKey(PropertyKey key, IControllerConfig template)
{
    public PropertyKey Key { get; } = key;
    public IControllerConfig Template { get; } = template;
}
