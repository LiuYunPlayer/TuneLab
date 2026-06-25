using System.Collections.Generic;

namespace TuneLab.SDK;

// 变长列表：长度随数据，值落为 PropertyArray。
//
// Elements —— 当前已存元素的逐元素 config，插件在 GetXxxConfig 里读 context 传入的 array 现算
//   （长度 = 数据元素数）。宿主不推断元素类型，渲染/绑定一律照插件给的逐元素 config。
//   初始内容(seed)即此：context 该 key 缺席（读到 Invalid）→ 插件按需返回 N 个 element config、
//   其默认值即初始值；用户删空 → 写入「存在的空数组」、key 存在 → 插件读到 count=0 返回空 Elements、
//   不再 seed（presence 判别，非 emptiness——见 IVoicePartPropertyContext「默认 = 字段不存在」）。
//
// AddableElements —— 可添加的元素类型候选集（+ 菜单）。单项：点 + 直接追加该类型默认值；多项：点 + 弹下拉
//   按类型名选再追加。随 f(context) 一并重算，故可依当前状态变化（如达上限时返回空 → + 禁用）。
//
// 与 ObjectConfig/ArrayConfig 同为复合型、不实现 IValueConfig。重排先不做（做时限同类型互换位置）。
public class ListConfig : IControllerConfig
{
    public required IReadOnlyList<IControllerConfig> Elements { get; init; }
    public required IReadOnlyList<AddableElement> AddableElements { get; init; }
}
