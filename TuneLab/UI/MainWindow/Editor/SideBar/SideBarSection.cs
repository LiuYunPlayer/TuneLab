using Avalonia.Controls;
using Avalonia.Media;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Controllers;
using TuneLab.Utils;

namespace TuneLab.UI;

// 侧栏面板里的分组标题（预设 / 声源 / 属性 / 参数 / 音符 / 音素…）。层级靠亮度与字号与字段标签拉开：
// 字段标签是暗一档的 12px（见 PropertyObjectController.CreateTitle），分组标题是白色 14px（不加粗——粗体与整体字样格格不入）。
// 分组之间的分界线由标题自带（顶边一条）——字段之间不再画线，只有分组边界有线，线才重新成为层级信号。
internal static class SideBarSection
{
    // 给一个可折叠分组装上标题：标签 + 左侧三角（展开朝下、收起朝右）。三角与字段 / 对象的同在标题左侧留白里
    //（见 FieldFold），大小相同、分组的亮一档——同一位置、用亮度区分层级。
    public static void Attach(CollapsiblePanel panel, string text)
    {
        var label = new Label
        {
            Content = text,
            Height = 38,
            FontSize = 14,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Foreground = Style.TEXT_LIGHT.ToBrush(),
            Padding = new(24, 0),
        };
        var chevron = new Chevron(8, Style.LIGHT_WHITE)
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new(FieldFold.GuideX - 2, 0, 0, 0),   // 与字段三角同一条中线（宽 4，中心落在 GuideX）
            IsExpanded = panel.IsExpanded,
        };
        panel.ExpandedChanged += () => chevron.IsExpanded = panel.IsExpanded;

        var row = new Panel();
        row.Children.Add(label);
        row.Children.Add(chevron);
        panel.Title = new Border
        {
            Background = Style.INTERFACE.ToBrush(),
            BorderBrush = Style.BACK.ToBrush(),
            BorderThickness = new(0, 1, 0, 0),
            Child = row,
        };
    }

    // 字段型分组（内容是一列字段）的顶部留白：字段标题行 30、文字居中、上方自带约 7，再补 7 凑成「分组线 → 第一项文字」14，
    // 与「末项收起 → 下一个分组」的 14 对称（那一侧由 FieldFold 的末项底边距补足）。底色同内容区。
    public static Control CreateInset() => new Border { Height = 7, Background = Style.INTERFACE.ToBrush() };
}
