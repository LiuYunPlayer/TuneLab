using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TuneLab.GUI;
using TuneLab.Utils;

namespace TuneLab.UI;

// 自定义下拉 Flyout 里的一行：标题填充（过长省略号 + 全名 tooltip）+ 可选右侧 ✕，整行 hover 高亮、点击触发 onClick 并关闭下拉。
// Script 侧栏脚本下拉、Properties 侧栏 preset 下拉共用（与 Agent 会话下拉同构——后者另带"运行中"指示点，单独实现）。
internal static class FlyoutMenuRow
{
    public static Control Build(string text, string? tooltip, Action onClick, Action? onDelete, Flyout flyout)
    {
        var title = new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 220,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Foreground = Colors.White.ToBrush(),
        };
        var dock = new DockPanel();

        if (onDelete != null)
        {
            var del = new TextBlock
            {
                Text = "✕",
                FontSize = 11,
                Margin = new(12, 0, 0, 0),
                Foreground = Style.LIGHT_WHITE.Opacity(0.4).ToBrush(),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            del.PointerEntered += (_, _) => del.Foreground = Colors.IndianRed.ToBrush();
            del.PointerExited += (_, _) => del.Foreground = Style.LIGHT_WHITE.Opacity(0.4).ToBrush();
            // 仅拦掉整行点击（避免触发 onClick）；删除动作放到 PointerReleased——按下时指针仍被 Flyout 捕获、鼠标未松开，
            // 此刻开模态确认窗会被系统"点击激活"吞掉首次悬浮/点击（需点两次）。等松开后再触发。
            del.PointerPressed += (_, e) => e.Handled = true;
            del.PointerReleased += (_, e) => { e.Handled = true; onDelete(); };
            DockPanel.SetDock(del, Dock.Right);
            dock.Children.Add(del);
        }
        dock.Children.Add(title);

        var row = new Border
        {
            Padding = new(10, 6),
            CornerRadius = new(4),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = dock,
        };
        if (!string.IsNullOrEmpty(tooltip))
            ToolTip.SetTip(row, tooltip);
        row.PointerEntered += (_, _) => row.Background = Style.LIGHT_WHITE.Opacity(0.08).ToBrush();
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerPressed += (_, e) => { if (e.Handled) return; onClick(); flyout.Hide(); };
        return row;
    }
}
