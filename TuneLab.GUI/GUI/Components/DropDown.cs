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

// 自造下拉控件（弃用原生 ComboBox）：触发面（文字 + ▾）+ 自管 Popup 菜单，支持任意层级子菜单。
// 选中以"展平叶子序"的 SelectedIndex 表达（与原 ComboBox 契约一致），显示文本 = 选中叶子文本，否则 PlaceholderText。
// 弹层是共用的 DropDownMenu（分组/分隔线/叶子多时顶部搜索），与列表 + 菜单同一套。
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
        // 回退徽标（默认隐藏）：显示内容并非所存真实值时点亮（如存值不在选项内、按默认项呈现），
        // 提示「此处有隐情，悬浮看详情」——徽标居右贴箭头，不打扰主文本。
        mMarker = new TextBlock()
        {
            Text = "⚠",
            FontSize = 11,
            Margin = new(6, 0, 0, 0),
            Foreground = Style.SYNTHESIS_DEGRADED.ToBrush(),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible = false,
        };
        var dock = new DockPanel() { LastChildFill = true, Margin = new(10, 0) };
        DockPanel.SetDock(arrow, Dock.Right);
        DockPanel.SetDock(mMarker, Dock.Right);
        dock.Children.Add(arrow);
        dock.Children.Add(mMarker);
        dock.Children.Add(mLabel);

        mFace = new Border()
        {
            Background = Style.BACK.ToBrush(),
            CornerRadius = new(4),
            Child = dock,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        mFace.PointerPressed += OnFacePressed;

        mMenu = new DropDownMenu(this);
        mMenu.ItemPicked += Select;

        Children.Add(mFace);
        Children.Add(mMenu);
    }

    public string? PlaceholderText { get => mPlaceholderText; set { mPlaceholderText = value; RefreshLabel(); } }

    // placeholder 专属前景色（null = 常规文字色）。选中真实叶子时恒用常规色，本色只作用于占位态——
    // 供"占位文本承载异常语义"的场景（如 ComboBox 值不在选项内时以警示色呈现字面量）。
    public IBrush? PlaceholderForeground { get => mPlaceholderForeground; set { mPlaceholderForeground = value; RefreshLabel(); } }

    // 警示徽标开关：见 mMarker 注释。由值控制器按显示状态管理（每次 Display 都显式设置，不自清）。
    public bool WarningMarker { get => mMarker.IsVisible; set => mMarker.IsVisible = value; }

    // 触发面背景（默认 Style.BACK）。当宿主背景与之同色、下拉会糊进背景时可覆盖以形成对比
    // （如安装器把语言下拉放在深色内容区上，覆盖成 Style.INTERFACE 让其像一个可见控件）。
    public IBrush? FaceBackground { get => mFace.Background; set => mFace.Background = value; }

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
        if (mMenu.JustClosed)   // light-dismiss 命中触发面时随后的 Press 不重开 → toggle
            return;
        Open();
    }

    public void Open() => mMenu.Open(mTopItems, Bounds.Width);

    void Select(DropDownItem leaf)
    {
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
        bool selected = (uint)mSelectedIndex < (uint)mLeaves.Count;
        mLabel.Text = selected ? mLeaves[mSelectedIndex].Text : (mPlaceholderText ?? string.Empty);
        mLabel.Foreground = selected ? Style.LIGHT_WHITE.ToBrush() : (mPlaceholderForeground ?? Style.LIGHT_WHITE.ToBrush());
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
    readonly DropDownMenu mMenu;
    string? mPlaceholderText;
    IBrush? mPlaceholderForeground;
    readonly TextBlock mMarker;
    IReadOnlyList<DropDownItem> mTopItems = [];
    readonly List<DropDownItem> mLeaves = new();
    int mSelectedIndex = -1;
}
