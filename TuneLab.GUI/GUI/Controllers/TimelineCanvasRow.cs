using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TuneLab.Foundation;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.SDK;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;

namespace TuneLab.GUI.Controllers;

// 时间轴画布（TimelineCanvasConfig）在侧栏里的一行：字段名（数组元素里没有）+ 摘要 + 最右一个编辑按钮。编辑界面在参数面板里，
// 这一行只标出它在属性树里的位置、给一个入口——按钮交给宿主（FieldHooks.EditTimelineCanvas）去参数面板激活它。
// 摘要：插件在声明时算好的（TimelineCanvasConfig.Summary），没给则按当前值兜底"默认 / 已修改 / 多值"。
// 面板没接 FieldHooks.EditTimelineCanvas = 这里没有时间轴（扩展设置窗、输入表单、尚不支持的声明位置）：这一行明说"此处不支持"，
// 不显示摘要也不给按钮——否则看着像接通了、实际谁也改不了它；同时记一条警告给插件作者（同一路径只记一次）。
internal sealed class TimelineCanvasRow
{
    public Control View => mRow;

    public TimelineCanvasRow(string? title, Func<PropertyValue> rawValue, FieldHooks? hooks, Func<IReadOnlyList<string>> path)
    {
        mRawValue = rawValue;
        mSupported = hooks?.EditTimelineCanvas != null;
        // 一行标准行高（同按钮字段）。行容器不取对象池：要定高，池化的 DockPanel 还被对象字段借去当内容区。
        mRow = new DockPanel() { Height = RowHeight, LastChildFill = true, Margin = new(title == null ? 0 : 24, 0, 8, 0) };

        if (mSupported && hooks?.EditTimelineCanvas is { } edit)
        {
            var button = new Button() { Width = 24, Height = 24, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };
            button.AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { HoveredColor = Style.LIGHT_WHITE.Opacity(0.1), PressedColor = Style.LIGHT_WHITE.Opacity(0.16) } })
                  .AddContent(new() { Item = new IconItem() { Icon = Assets.Editing, Scale = 0.7 }, ColorSet = new() { Color = Style.LIGHT_WHITE } });
            button.Clicked += () => edit(path());
            button.SetupToolTip("Edit in Parameter Panel".Tr("Menu"));
            DockPanel.SetDock(button, Dock.Right);
            mRow.Children.Add(button);
        }

        if (title != null)
        {
            var label = new TextBlock()
            {
                Text = title,
                FontSize = 12,
                Foreground = Style.TEXT_LABEL.ToBrush(),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new(0, 0, 12, 0),
            };
            DockPanel.SetDock(label, Dock.Left);
            mRow.Children.Add(label);
        }

        mSummary = new TextBlock()
        {
            FontSize = 12,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = title == null ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        mRow.Children.Add(mSummary);

        if (!mSupported)
        {
            mSummary.Text = "(Timeline canvases are not supported here)".Tr("PropertyObjectController");
            mSummary.Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush();
            var fieldPath = string.Join("/", path());
            if (mWarned.Add(fieldPath))
                Log.Warning("TimelineCanvasConfig at \"" + fieldPath + "\" is declared where there is no timeline (only note / phoneme property declarations support it); the field is shown as unsupported and cannot be edited.");
        }
    }

    public void Apply(TimelineCanvasConfig config)
    {
        mConfig = config;
        RefreshSummary();
    }

    public void RefreshSummary()
    {
        if (mConfig == null || !mSupported)
            return;
        if (mConfig.Summary is { } summary)
        {
            mSummary.Text = summary;
            return;
        }
        var value = mRawValue();
        mSummary.Text = value.IsMultiple() ? "(Multiple)".Tr("PropertyObjectController")
            : (value.IsNull() || value.Equals(mConfig.DefaultValue) ? "Default" : "Modified").Tr("PropertyObjectController");
    }

    const double RowHeight = 30;

    readonly DockPanel mRow;
    readonly TextBlock mSummary;
    readonly Func<PropertyValue> mRawValue;
    readonly bool mSupported;
    static readonly HashSet<string> mWarned = new();
    TimelineCanvasConfig? mConfig;
}
