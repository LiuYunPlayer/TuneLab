using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TuneLab.SDK;

namespace TuneLab.GUI.Controllers;

// 属性面板里需要知道"这个字段在声明树里哪儿"的宿主钩子：面板持有方在根控制器上设一份，嵌套的控制器（对象、数组 / 列表、
// 键控容器的元素）共享同一份。路径逐段：对象成员 / 键控条目 = 键，数组 / 列表元素 = 下标（按元素当前的位置，调序后跟着变）。
internal sealed class FieldHooks
{
    // 逐字段的右键菜单项（如钉进参数面板）：path = 字段的路径。返回 null / 空 = 不加。菜单在每次右键时现取。
    public Func<IReadOnlyList<string>, IControllerConfig, IReadOnlyList<MenuItem>?>? ItemMenu { get; init; }

    // 时间轴画布在侧栏只有一行摘要 + 编辑按钮：按了就去参数面板编辑它（path = 它的路径）。null = 不显示编辑按钮。
    public Action<IReadOnlyList<string>>? EditTimelineCanvas { get; init; }
}

// 一层控制器在树里的位置：共享的钩子 + 到本层的路径（现算：数组元素的下标随调序变）。
internal readonly record struct FieldPath(FieldHooks? Hooks, Func<IReadOnlyList<string>> Prefix)
{
    public static FieldPath Root(FieldHooks? hooks) => new(hooks, static () => []);

    public FieldPath Child(string segment)
    {
        var prefix = Prefix;
        return new(Hooks, () => [.. prefix(), segment]);
    }

    public FieldPath Child(Func<string> segment)
    {
        var prefix = Prefix;
        return new(Hooks, () => [.. prefix(), segment()]);
    }

    public IReadOnlyList<string> Of(string key) => [.. Prefix(), key];
}
