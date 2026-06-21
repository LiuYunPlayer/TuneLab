using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 变长键控容器：值落为 PropertyObject（不是 array）。对标 ListConfig 之于 ArrayConfig——
// ObjectConfig 是定 key（声明即全部、不可增减），本类是变 key（用户从 + 菜单挑键加入、可删）。
//
// Properties —— 当前已存键的逐键 config，插件读 context 现算（同 ObjectConfig 形，长度 = 数据键数）。
//   presence/seed：context 该容器键缺席（从未写）→ 插件按需返回若干键当 seed（其默认值即初始值）；
//   present → 按实际键集返回；用户删到空 → 写「存在的空对象」、不删整个容器键 → 插件返回空 Properties、不再 seed。
//
// AddableElements —— 可添加的命名键候选集（+ 菜单）。每项 AddableKey 携 PropertyKey（Id+标签）+ Template config。
//   键唯一：每候选最多加一次，宿主在 + 菜单隐藏已存在的键；随 f(context) 重算（全加完/达上限 → 空 → + 禁用）。
//
// 与 ObjectConfig/ArrayConfig/ListConfig 同为复合型、不实现 IValueConfig（默认值由宿主递归各键默认值求得）。
// 「需要逐项标签」即「键控」的信号：标签走 PropertyKey.DisplayText、config 仍不带 DisplayText。
public class ExtensibleObjectConfig : IControllerConfig
{
    public required IReadOnlyOrderedMap<PropertyKey, IControllerConfig> Properties { get; init; }
    public required IReadOnlyList<AddableKey> AddableElements { get; init; }
}
