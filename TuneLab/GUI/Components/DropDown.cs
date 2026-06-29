using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using TuneLab.GUI;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

// 下拉选项节点：Children 非空 = 分组（本身不可选，悬浮/点击展开二级菜单），否则为可选叶子（Tag 为选中回传标识）。
internal sealed class DropDownItem
{
    public required string Text { get; init; }
    public object? Tag { get; init; }
    public IReadOnlyList<DropDownItem>? Children { get; init; }
    public bool IsGroup => Children is { Count: > 0 };
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

        mSubPopup = new Popup()
        {
            Placement = PlacementMode.RightEdgeAlignedTop,
            IsLightDismissEnabled = false,
        };

        Children.Add(mFace);
        Children.Add(mPopup);
        Children.Add(mSubPopup);
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
            if (item.IsGroup)
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
        mPopup.MinWidth = Bounds.Width;
        mPopup.Child = BuildMenu(mTopItems, root: true);
        mPopup.IsOpen = true;
    }

    Control BuildMenu(IReadOnlyList<DropDownItem> items, bool root)
    {
        var stack = new StackPanel() { Orientation = Orientation.Vertical, MinWidth = root ? Bounds.Width : 160 };
        foreach (var item in items)
            stack.Children.Add(BuildRow(item));

        var scroll = new ScrollViewer()
        {
            MaxHeight = 420,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = stack,
        };
        return new Border()
        {
            Background = Style.INTERFACE.ToBrush(),
            CornerRadius = new(4),
            Padding = new(4),
            BorderThickness = new(1),
            BorderBrush = Style.BACK.ToBrush(),
            Child = scroll,
        };
    }

    Control BuildRow(DropDownItem item)
    {
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
            MinWidth = 120,
            Child = dock,
        };
        row.PointerEntered += (_, _) =>
        {
            row.Background = Style.LIGHT_WHITE.Opacity(0.08).ToBrush();
            if (item.IsGroup)
                OpenSubMenu(row, item.Children!);
            else
                CloseSubMenu();
        };
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            if (item.IsGroup)
                OpenSubMenu(row, item.Children!);
            else
                Select(item);
        };
        return row;
    }

    void OpenSubMenu(Control anchor, IReadOnlyList<DropDownItem> children)
    {
        if (ReferenceEquals(mSubAnchor, anchor) && mSubPopup.IsOpen)
            return;
        mSubAnchor = anchor;
        mSubPopup.PlacementTarget = anchor;
        mSubPopup.Child = BuildMenu(children, root: false);
        mSubPopup.IsOpen = true;
    }

    void CloseSubMenu()
    {
        mSubPopup.IsOpen = false;
        mSubAnchor = null;
    }

    void Select(DropDownItem leaf)
    {
        CloseSubMenu();
        mPopup.IsOpen = false;
        int index = mLeaves.IndexOf(leaf);
        if (index < 0)
            return;
        if (index == mSelectedIndex)
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
    readonly Popup mSubPopup;
    Control? mSubAnchor;
    string? mPlaceholderText;
    IReadOnlyList<DropDownItem> mTopItems = [];
    readonly List<DropDownItem> mLeaves = new();
    int mSelectedIndex = -1;
    bool mJustClosed;
}
