using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Animation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Utils;

namespace TuneLab.UI;

// 并排承载多个侧栏面（每面一个 SideBar），自右向左按打开顺序排：最先打开的贴着图标栏，后开的依次往左。
//
// 【宽度的两层】存储宽度 = 用户拖出来的、随 EditorState 持久化；显示宽度 = 本轮布局实际给的。
// 放得下时两者相等；窗口缩窄放不下时先把各面按比例压向最小宽，最小宽也放不下就从最后打开的起临时隐藏——
// 这些都只动显示宽度，不改存储宽度、也不改打开集合，窗口放宽后自动恢复原样（不替用户关面板）。
//
// 【可用宽度】= 本面板拿到的测量宽度（编辑器扣掉图标栏后的剩余）− 编辑区最小宽。新开一面要求剩余预算
// 至少放得下一个最小宽，否则不让开（CanOpen，图标栏据此置灰）。
internal class SideBarStrip : Panel
{
    public const double MinPanelWidth = 240;
    public const double MaxPanelWidth = 640;
    public const double DefaultPanelWidth = 320;
    // 编辑区（编排区 + 钢琴窗）至少留这么宽：再窄钢琴窗的键盘 + 参数区就挤得没法用了。
    public const double MinContentWidth = 400;

    // 布局结果变了（可用宽度 / 显示宽度 / 显隐）：可开性可能随之变，图标栏据此刷新。
    public event Action? LayoutChanged;
    // 用户拖完一次宽度：存储宽度变了，宿主据此落盘。
    public event Action? WidthsCommitted;

    public IReadOnlyDictionary<SideBarTab, double> Widths => mWidths;

    public SideBarStrip()
    {
        ZIndex = 1;   // 拖拽手柄要向左探出、压住内容区那条缝（见 CreateResizer）
    }

    public void SetWidths(IEnumerable<KeyValuePair<SideBarTab, double>> widths)
    {
        mWidths.Clear();
        foreach (var (tab, width) in widths)
            mWidths[tab] = Math.Clamp(width, MinPanelWidth, MaxPanelWidth);
        InvalidateMeasure();
    }

    // 每个页签一个 SideBar 实例，首次用到时建、关掉后缓存（内容控件、滚动位置、Agent 对话都留着）。
    public SideBar Get(SideBarTab tab)
    {
        if (!mColumns.TryGetValue(tab, out var column))
        {
            column = new Column(new SideBar(), CreateResizer(tab));
            mColumns.Add(tab, column);
        }
        return column.SideBar;
    }

    // 换成新的打开集合（有序）：新出现的面先按剩余预算收窄存储宽度——「放得下才开」的那个宽度，
    // 而不是挤压已开的面给它腾地方。
    public void SetOpenTabs(IReadOnlyList<SideBarTab> tabs)
    {
        foreach (var tab in tabs)
        {
            if (mOpen.Contains(tab) || mBudget is not { } budget)
                continue;
            double used = mDisplayed.Sum(item => item.Width);
            double room = budget - used;
            if (room >= MinPanelWidth && WidthOf(tab) > room)
                mWidths[tab] = room;
        }

        mOpen.Clear();
        mOpen.AddRange(tabs);

        Children.Clear();
        foreach (var tab in mOpen)
            Children.Add(Get(tab));
        // 手柄后加，压在各面之上（命中优先）；最左那条还要探出到内容区。
        foreach (var tab in mOpen)
            Children.Add(mColumns[tab].Resizer);
        InvalidateMeasure();
    }

    // 已开的面不问；布局还没跑过（启动早期）不拦。临时隐藏着的面存在即说明已经放不下。
    public bool CanOpen(SideBarTab tab)
    {
        if (mOpen.Contains(tab) || mBudget is not { } budget)
            return true;
        if (mDisplayed.Count < mOpen.Count)
            return false;
        return budget - mDisplayed.Sum(item => item.Width) >= MinPanelWidth;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double budget = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - MinContentWidth);
        var displayed = ComputeLayout(budget);
        bool changed = mBudget != budget || !displayed.SequenceEqual(mDisplayed);
        mBudget = budget;
        mDisplayed = displayed;

        var visible = displayed.Select(item => item.Tab).ToHashSet();
        foreach (var tab in mOpen)
        {
            var column = mColumns[tab];
            column.SideBar.IsVisible = visible.Contains(tab);
            column.Resizer.IsVisible = visible.Contains(tab);
        }

        double height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        foreach (var (tab, width) in displayed)
        {
            mColumns[tab].SideBar.Measure(new Size(width, availableSize.Height));
            mColumns[tab].Resizer.Measure(new Size(ResizerWidth, availableSize.Height));
        }

        if (changed)
            Dispatcher.UIThread.Post(() => LayoutChanged?.Invoke());
        return new Size(displayed.Sum(item => item.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = finalSize.Width;
        foreach (var (tab, width) in mDisplayed)
        {
            x -= width;
            mColumns[tab].SideBar.Arrange(new Rect(x, 0, width, finalSize.Height));
            // 手柄跨缝 ±4px：左探 4px 压到左邻（或内容区），右留 4px 在本面。
            mColumns[tab].Resizer.Arrange(new Rect(x - ResizerWidth / 2, 0, ResizerWidth, finalSize.Height));
        }
        return finalSize;
    }

    // 显示宽度：先按打开顺序从末尾丢掉放不下最小宽的面，余下的超出预算就按「各自高出最小宽的部分」等比压缩。
    List<(SideBarTab Tab, double Width)> ComputeLayout(double budget)
    {
        int count = mOpen.Count;
        while (count > 0 && count * MinPanelWidth > budget)
            count--;

        var widths = mOpen.Take(count).Select(WidthOf).ToList();
        double sum = widths.Sum();
        if (sum > budget)
        {
            double slack = sum - count * MinPanelWidth;
            double factor = slack <= 0 ? 0 : (budget - count * MinPanelWidth) / slack;
            for (int i = 0; i < widths.Count; i++)
                widths[i] = MinPanelWidth + (widths[i] - MinPanelWidth) * factor;
        }

        return mOpen.Take(count).Zip(widths, (tab, width) => (tab, Math.Floor(width))).ToList();
    }

    double WidthOf(SideBarTab tab) => mWidths.TryGetValue(tab, out var width) ? width : DefaultPanelWidth;

    // 拖拽手柄（跨缝 8px 命中区、2px 可见高亮）：悬浮约 300ms 后显色、拖动中保持，移开/松手即隐。
    // 拖出来的宽度写进存储宽度，上限同时受「别把别的面挤出去」约束（预算 − 其余面的显示宽度）。
    Border CreateResizer(SideBarTab tab)
    {
        var resizer = new Border()
        {
            Width = ResizerWidth,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
        };
        var line = new Border() { Width = 4, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        resizer.Child = line;

        var highlight = Style.HIGH_LIGHT;
        var clear = new Color(0, highlight.R, highlight.G, highlight.B);
        var lineBrush = new SolidColorBrush(clear);
        line.Background = lineBrush;
        var lineColor = new AnimationColor() { Value = clear };
        lineColor.ValueChanged += () => lineBrush.Color = lineColor.Value;
        void ShowLine() => lineColor.SetTo(highlight, 130, AnimationCurve.QuadOut);
        void HideLine() => lineColor.SetTo(clear, 130, AnimationCurve.QuadOut);
        var hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        hoverTimer.Tick += (_, _) => { hoverTimer.Stop(); ShowLine(); };

        bool resizing = false;
        double startX = 0, startWidth = 0, maxWidth = MaxPanelWidth;
        resizer.PointerEntered += (_, _) => hoverTimer.Start();
        resizer.PointerExited += (_, _) => { hoverTimer.Stop(); if (!resizing) HideLine(); };
        resizer.PointerPressed += (_, e) =>
        {
            resizing = true;
            startX = PointerX(e);
            startWidth = mDisplayed.FirstOrDefault(item => item.Tab == tab).Width;
            double others = mDisplayed.Where(item => item.Tab != tab).Sum(item => item.Width);
            maxWidth = mBudget is { } budget ? Math.Min(MaxPanelWidth, budget - others) : MaxPanelWidth;
            hoverTimer.Stop();
            ShowLine();
            e.Pointer.Capture(resizer);
        };
        resizer.PointerMoved += (_, e) =>
        {
            if (!resizing)
                return;
            double dx = PointerX(e) - startX;
            mWidths[tab] = Math.Clamp(startWidth - dx, MinPanelWidth, Math.Max(MinPanelWidth, maxWidth));
            InvalidateMeasure();
        };
        resizer.PointerReleased += (_, e) =>
        {
            if (!resizing)
                return;
            resizing = false;
            e.Pointer.Capture(null);
            if (!resizer.IsPointerOver)
                HideLine();
            WidthsCommitted?.Invoke();
        };
        return resizer;
    }

    // 指针位置取父级（编辑器）坐标：本面板右缘贴着图标栏不动、左缘随宽度变，按自身坐标取位移会随拖动漂移。
    double PointerX(PointerEventArgs e) => e.GetPosition(Parent as Visual ?? this).X;

    const double ResizerWidth = 8;

    readonly record struct Column(SideBar SideBar, Border Resizer);

    readonly Dictionary<SideBarTab, Column> mColumns = [];
    readonly Dictionary<SideBarTab, double> mWidths = [];
    readonly List<SideBarTab> mOpen = [];
    List<(SideBarTab Tab, double Width)> mDisplayed = [];
    double? mBudget;
}
