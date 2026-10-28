using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace TuneLab.GUI.Components;

// 右键菜单（Avalonia ContextMenu）里写着组名的分隔线：线中间居中写组名，样子同 DropDownMenu 的带字分隔线。
// 做成 Separator（换掉它的模板）而不是 MenuItem：菜单只把 MenuItem / Separator 当作自己的项，别的控件会被包进一个能悬停、能点的 MenuItem。
internal static class MenuSeparators
{
    public static Separator Labeled(string label) => new()
    {
        Template = new FuncControlTemplate<Separator>((_, _) => DropDownMenu.BuildSeparator(label)),
        Height = double.NaN,
        Margin = new(0),
        Background = Brushes.Transparent,
    };
}
