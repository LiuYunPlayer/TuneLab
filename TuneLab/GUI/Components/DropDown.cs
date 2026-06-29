using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using TuneLab.GUI;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

// 下拉选项节点：Children 非 null = 分组（本身不可选，悬浮/点击展开二级菜单，允许空=空子菜单）；
// IsSeparator = 分隔线（不可选，Text 为可选居中标签）；否则为可选叶子（Tag 为选中回传标识）。
internal sealed class DropDownItem
{
    public required string Text { get; init; }
    public object? Tag { get; init; }
    public IReadOnlyList<DropDownItem>? Children { get; init; }
    public bool IsSeparator { get; init; }
    public bool IsGroup => Children is not null;
}

// 自造下拉控件（弃用原生 ComboBox）：触发面（文字 + ▾）+ 自管 Popup 菜单，支持二级子菜单。
// 选中以"展平叶子序"的 SelectedIndex 表达（与原 ComboBox 契约一致），显示文本 = 选中叶子文本，否则 PlaceholderText。
// 弹层为自绘行（复用 FlyoutMenuRow 同款 hover 风格），未来可在菜单顶部加搜索框过滤。
internal class DropDown : Panel
{
    public DropDown()
    {
        mLabel = new TextBlock()
        {
            FontSize = 12,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var arrow = new TextBlock()
        {
            Text = "▾",
            FontSize = 10,
            Margin = new(6, 0, 0, 0),
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var dock = new DockPanel() { LastChildFill = true, Margin = new(10, 0) };
        DockPanel.SetDock(arrow, Dock.Right);
        dock.Children.Add(arrow);
        dock.Children.Add(mLabel);

        mFace = new Border()
        {
            Background = Style.BACK.ToBrush(),
            CornerRadius = new(4),
            Child = dock,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        mFace.PointerPressed += OnFacePressed;

        mPopup = new Popup()
        {
            PlacementTarget = this,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
        };
        mPopup.Closed += (_, _) =>
        {
            CloseSubMenu();
            mJustClosed = true;   // light-dismiss 命中触发面时随后的 Press 不重开 → toggle
            Dispatcher.UIThread.Post(() => mJustClosed = false, DispatcherPriority.Input);
        };

        Children.Add(mFace);
        Children.Add(mPopup);
    }

    public string? PlaceholderText { get => mPlaceholderText; set { mPlaceholderText = value; RefreshLabel(); } }

    public int SelectedIndex
    {
        get => mSelectedIndex;
        set
        {
            int clamped = (uint)value < (uint)mLeaves.Count ? value : -1;
            if (clamped == mSelectedIndex)
                return;
            mSelectedIndex = clamped;
            RefreshLabel();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object? SelectedTag => (uint)mSelectedIndex < (uint)mLeaves.Count ? mLeaves[mSelectedIndex].Tag : null;

    public event EventHandler? SelectionChanged;

    // 重建选项树（递归展平叶子供 SelectedIndex 寻址）。不触发 SelectionChanged；选中态由调用方随后设置。
    public void SetItems(IReadOnlyList<DropDownItem> items)
    {
        mTopItems = items;
        mLeaves.Clear();
        Flatten(items);
        if (mSelectedIndex >= mLeaves.Count)
            mSelectedIndex = -1;
        RefreshLabel();
    }

    void Flatten(IReadOnlyList<DropDownItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
                continue;
            else if (item.IsGroup)
                Flatten(item.Children!);
            else
                mLeaves.Add(item);
        }
    }

    void OnFacePressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (mJustClosed)
            return;
        Open();
    }

    public void Open()
    {
        mPopup.Child = BuildMenu(mTopItems, Bounds.Width);
        mPopup.IsOpen = true;
    }

    // width > 0：固定菜单宽度（根菜单 = 触发面宽，与本体对齐）；<= 0：随内容（子菜单）。
    Control BuildMenu(IReadOnlyList<DropDownItem> items, double width)
    {
        var stack = new StackPanel() { Orientation = Orientation.Vertical };
        foreach (var item in items)
            stack.Children.Add(BuildRow(item));

        var scroll = new ScrollViewer()
        {
            MaxHeight = 420,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = stack,
        };
        var border = new Border()
        {
            Background = Style.BACK.ToBrush(),
            CornerRadius = new(4),
            Padding = new(0, 4),
            BorderThickness = new(1),
            BorderBrush = Style.INTERFACE.ToBrush(),
            Child = scroll,
        };
        if (width > 0)
            border.Width = width;
        else
            border.MinWidth = 140;
        // 屏蔽滚轮穿透：ScrollViewer 能滚则先消费（标 Handled）；滚到尽头未处理者冒泡至此 → 吞掉，不传到后面面板。
        border.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble);
        return border;
    }

    Control BuildRow(DropDownItem item)
    {
        if (item.IsSeparator)
            return BuildSeparator(item.Text);

        var title = new TextBlock()
        {
            Text = item.Text,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
        };
        var dock = new DockPanel() { LastChildFill = true };
        if (item.IsGroup)
        {
            var chevron = new TextBlock() { Text = "▸", FontSize = 11, Margin = new(12, 0, 0, 0), Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            DockPanel.SetDock(chevron, Dock.Right);
            dock.Children.Add(chevron);
        }
        dock.Children.Add(title);

        var row = new Border()
        {
            Padding = new(10, 6),
            CornerRadius = new(4),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = dock,
        };
        row.PointerEntered += (_, _) => row.Background = Style.LIGHT_WHITE.Opacity(0.08).ToBrush();
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;

        if (!item.IsGroup)
        {
            // 进入叶子行时收起任何已展开的子菜单（从分组移到同级叶子）。
            row.PointerEntered += (_, _) => CloseSubMenu();
            row.PointerReleased += (_, e) => { e.Handled = true; Select(item); };
            return row;
        }

        // 分组行：子 Popup 建在主菜单内容树内（与行同根），故 PlacementTarget=row 定位正常、可悬浮到侧边。
        // 负 HorizontalOffset 让子菜单与父菜单轻微重叠（仿右键菜单），消除指针横移时中途踩空隙导致收起。
        var subPopup = new Popup()
        {
            PlacementTarget = row,
            Placement = PlacementMode.RightEdgeAlignedTop,
            HorizontalOffset = -6,
            VerticalOffset = -5,
            IsLightDismissEnabled = false,
            Child = BuildMenu(item.Children!, 0),
        };
        void OpenThis()
        {
            if (!ReferenceEquals(mOpenSub, subPopup))
                CloseSubMenu();
            mOpenSub = subPopup;
            subPopup.IsOpen = true;
        }
        row.PointerEntered += (_, _) => OpenThis();
        row.PointerReleased += (_, e) => { e.Handled = true; OpenThis(); };

        var host = new Panel();
        host.Children.Add(row);
        host.Children.Add(subPopup);
        return host;
    }

    // 分隔线：无标签 = 纯线；有标签 = 左线 + 居中文字 + 右线。不可交互、不计入选中。
    static Control BuildSeparator(string label)
    {
        var line = () => new Border() { Height = 1, Background = Style.LIGHT_WHITE.Opacity(0.15).ToBrush(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        if (string.IsNullOrEmpty(label))
            return new Border() { Height = 1, Margin = new(10, 5), Background = Style.LIGHT_WHITE.Opacity(0.15).ToBrush() };

        var grid = new Grid() { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new(10, 6) };
        var left = line();
        var text = new TextBlock() { Text = label, FontSize = 10, Margin = new(8, 0), Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var right = line();
        Grid.SetColumn(left, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(text);
        grid.Children.Add(right);
        return grid;
    }

    void CloseSubMenu()
    {
        if (mOpenSub == null)
            return;
        mOpenSub.IsOpen = false;
        mOpenSub = null;
    }

    void Select(DropDownItem leaf)
    {
        CloseSubMenu();
        mPopup.IsOpen = false;
        int index = mLeaves.IndexOf(leaf);
        if (index < 0 || index == mSelectedIndex)
        {
            RefreshLabel();
            return;
        }
        mSelectedIndex = index;
        RefreshLabel();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    void RefreshLabel()
    {
        mLabel.Text = (uint)mSelectedIndex < (uint)mLeaves.Count ? mLeaves[mSelectedIndex].Text : (mPlaceholderText ?? string.Empty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        mFace.Measure(availableSize);
        return mFace.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        mFace.Arrange(new Rect(finalSize));
        return finalSize;
    }

    readonly TextBlock mLabel;
    readonly Border mFace;
    readonly Popup mPopup;
    Popup? mOpenSub;
    string? mPlaceholderText;
    IReadOnlyList<DropDownItem> mTopItems = [];
    readonly List<DropDownItem> mLeaves = new();
    int mSelectedIndex = -1;
    bool mJustClosed;
}
