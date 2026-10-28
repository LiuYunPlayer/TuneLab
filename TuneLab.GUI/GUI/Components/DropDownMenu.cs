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
using System.Linq;
using TuneLab.GUI;
using TuneLab.I18N;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

// 菜单节点：Children 非 null = 分组（本身不可选，悬浮/点击展开子菜单，允许空=空子菜单）；
// IsSeparator = 分隔线（不可选，Text 为可选居中标签）；否则为可选叶子（Tag 为选中回传标识）。
internal sealed class DropDownItem
{
    public required string Text { get; init; }
    public object? Tag { get; init; }
    public IReadOnlyList<DropDownItem>? Children { get; init; }
    public bool IsSeparator { get; init; }
    public bool IsGroup => Children is not null;
}

// 菜单树弹层：任意层级子菜单 + 叶子多时顶部带搜索框。凡「从一棵选项树里挑一项」的地方共用本弹层
//（DropDown 的下拉、列表/键控容器的 + 菜单），故分组、分隔线、搜索、滚动在各处观感与行为一致。
// 本身是零尺寸 Panel，只为把弹层挂进视觉树：调用方把它加进自己的子树，弹层锚定 placementTarget。
internal sealed class DropDownMenu : Panel
{
    // 叶子数达到此值才显示搜索框——短列表一眼可见，搜索框只是噪音。
    public const int SearchThreshold = 10;

    public event Action<DropDownItem>? ItemPicked;
    public event Action? Closed;

    public bool IsOpen => mPopup.IsOpen;

    // 弹层的描边（含各层子菜单）；null = 默认（侧栏上弹出：深色弹层压在更亮的面板上，边线只需一点点）。
    // 画布上弹出时底下是插件自己画的东西、什么颜色都可能，故传一条与深色底反差大的亮边，弹层的边界总看得清。
    public IBrush? OutlineBrush { get; init; }

    // 刚被 light-dismiss 关掉：锚点控件据此忽略紧随其后的同一次按下，使「再点一次锚点」表现为收起而非重开。
    public bool JustClosed { get; private set; }

    public DropDownMenu(Control placementTarget)
    {
        IsHitTestVisible = false;
        mPopup = new Popup()
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
        };
        mPopup.Closed += (_, _) =>
        {
            CloseFrom(0);   // 收起整条子菜单链（各层子菜单是独立 Popup 窗口，须逐个关）
            mSearchBox = null;
            JustClosed = true;
            Dispatcher.UIThread.Post(() => JustClosed = false, DispatcherPriority.Input);
            Closed?.Invoke();
            EndHold();
        };
        Children.Add(mPopup);
    }

    // width > 0：根菜单固定宽度（如与下拉触发面对齐）；<= 0：随内容。
    // anchor：在锚点控件内的哪块矩形下方弹出（锚点控件坐标；如画布上的某段文字）；null = 整个锚点控件。
    public void Open(IReadOnlyList<DropDownItem> items, double width, Rect? anchor = null)
    {
        CloseFrom(0);   // 清空可能残留的链（防御：正常已在 Closed 里清过）
        mTopItems = items;
        mMatches.Clear();
        mHighlight = -1;
        mPopup.Child = BuildRoot(width);
        mPopup.PlacementRect = anchor;
        // 菜单打开期间算所在窗口"编辑中"（见 EditGate）：面板这时若按新声明刷新，锚点控件（下拉框、+ 按钮）可能被换掉，
        // 弹层就挂在一个已经不在的控件上，选了也落不到数据里。选定或关闭（含点别处收起）即结束。
        mHold ??= EditGate.Of(this)?.BeginEdit();
        mPopup.IsOpen = true;
        if (mSearchBox != null)
            Dispatcher.UIThread.Post(() => mSearchBox?.Focus(), DispatcherPriority.Loaded);
    }

    public void Close() => mPopup.IsOpen = false;

    void EndHold()
    {
        mHold?.Dispose();
        mHold = null;
    }

    // 打开中离开界面（面板被拆、窗口关闭）：结束登记，不让窗口一直挂着"编辑中"。
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        EndHold();
    }

    IDisposable? mHold;   // 打开期间在所在窗口 EditGate 上的登记

    Control BuildRoot(double width)
    {
        mRootStack = new StackPanel() { Orientation = Orientation.Vertical };
        FillTree(mRootStack, mTopItems, depth: 0);
        var scrollHost = BuildScrollHost(mRootStack, out _);

        Control content = scrollHost;
        bool searchable = CountLeaves(mTopItems) >= SearchThreshold;
        if (searchable)
        {
            mSearchBox = new TextInput()
            {
                Height = 26,
                Margin = new(6, 2, 6, 4),
                Background = Style.INTERFACE.ToBrush(),
                Watermark = "Search".Tr(this),
            };
            mSearchBox.TextChanged.Subscribe(OnQueryChanged);
            mSearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
            var dock = new DockPanel() { LastChildFill = true };
            DockPanel.SetDock(mSearchBox, Dock.Top);
            dock.Children.Add(mSearchBox);
            dock.Children.Add(scrollHost);
            content = dock;
        }

        var border = MakeMenuBorder(content);
        if (width > 0)
            border.Width = width;
        else
            border.MinWidth = searchable ? 220 : 140;   // 可搜索时给足宽度，过滤时宽度不随结果抖动太多
        return border;
    }

    // 子菜单：不带搜索（搜索作用于整棵树、只在根层）。
    Control BuildSubMenu(IReadOnlyList<DropDownItem> items, int depth)
    {
        var stack = new StackPanel() { Orientation = Orientation.Vertical };
        FillTree(stack, items, depth);
        var border = MakeMenuBorder(BuildScrollHost(stack, out _));
        border.MinWidth = 140;
        return border;
    }

    Border MakeMenuBorder(Control content)
    {
        var border = new Border()
        {
            Background = Style.BACK.ToBrush(),
            CornerRadius = new(4),
            Padding = new(0, 4),
            BorderThickness = new(1),
            BorderBrush = OutlineBrush ?? Style.INTERFACE.ToBrush(),
            Child = content,
        };
        // 屏蔽滚轮穿透：ScrollViewer 能滚则先消费（标 Handled）；滚到尽头未处理者冒泡至此 → 吞掉，不传到后面面板。
        border.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble);
        return border;
    }

    static Control BuildScrollHost(Control stack, out ScrollViewer scroll)
    {
        scroll = new ScrollViewer()
        {
            MaxHeight = 420,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,   // 原生竖条隐藏，改叠自造条
            Content = stack,
        };
        // 用自造 ScrollBar 替换原生竖条（与全局观感一致）。直接叠在视觉树里（非 AdornerLayer）：ICustomHitTest 令
        // 只有手柄可命中——悬到手柄上时本条即 pointer-over、显默认箭头；其余穿透到下方选项行（手型），故不再"手柄上仍是手型"。
        var bar = new ScrollBar(new ScrollViewerVerticalAxis(scroll), Orientation.Vertical);
        var scrollHost = new Panel();
        scrollHost.Children.Add(scroll);
        scrollHost.Children.Add(bar);
        // 平滑滚轮（与全局观感一致）：原生逐格跳滚换成缓动。scroll 经隧道事件订阅持有它 → 随菜单树共存亡。
        var captured = scroll;
        _ = new SmoothWheelScroller(captured, () => captured);
        return scrollHost;
    }

    void FillTree(StackPanel stack, IReadOnlyList<DropDownItem> items, int depth)
    {
        foreach (var item in items)
            stack.Children.Add(BuildRow(item, depth));
    }

    // depth：本菜单在子菜单链中的层深（根 = 0，其子菜单 = 1…）。行按此深度管理「本层已展开的子菜单」，
    // 故移进子菜单是进入更深一层、不动本层及祖先，任意层级都不会把自己关掉（浮得过去）。
    Control BuildRow(DropDownItem item, int depth)
    {
        if (item.IsSeparator)
            return BuildSeparator(item.Text);

        // 空分组（如引擎已加载但无音源）退化为一级项：无箭头、不可选、点了无效（仿右键菜单里的空引擎项）。
        bool expandable = item.IsGroup && item.Children!.Count > 0;
        var row = MakeRow(item.Text, null, expandable, clickable: !item.IsGroup || expandable);

        if (expandable)
        {
            // 分组行：子 Popup 建在主菜单内容树内（与行同根），故 PlacementTarget=row 定位正常、可悬浮到侧边。
            // 负 HorizontalOffset 让子菜单与父菜单轻微重叠（仿右键菜单），消除指针横移时中途踩空隙导致收起。
            // 子菜单是更深一层（depth+1），拥有自己的展开状态，故其内的行不会误关本行这层。
            var subPopup = new Popup()
            {
                PlacementTarget = row,
                Placement = PlacementMode.RightEdgeAlignedTop,
                HorizontalOffset = -6,
                VerticalOffset = -5,
                IsLightDismissEnabled = false,
                Child = BuildSubMenu(item.Children!, depth + 1),
            };
            // 悬停 / 点击本组：收起本层其余已展开的兄弟子菜单（及更深残留），再展开自己。
            row.PointerEntered += (_, _) => OpenAt(depth, subPopup);
            row.PointerReleased += (_, e) => { e.Handled = true; OpenAt(depth, subPopup); };

            var host = new Panel();
            host.Children.Add(row);
            host.Children.Add(subPopup);
            return host;
        }

        // 叶子 / 空分组行：enter 时收起本层已展开的子菜单（含更深层）。任意层级同理——只动本层及更深、不碰祖先。
        row.PointerEntered += (_, _) => CloseFrom(depth);

        // 叶子可选；空分组 no-op（不挂选择）。
        if (!item.IsGroup)
            row.PointerReleased += (_, e) => { e.Handled = true; Pick(item); };

        return row;
    }

    // 一行：标题（可带灰色路径前缀，搜索结果用）+ 可选右箭头。hover 高亮由行自管。
    static Border MakeRow(string text, string? pathPrefix, bool chevron, bool clickable)
    {
        var title = new TextBlock()
        {
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
        };
        if (string.IsNullOrEmpty(pathPrefix))
        {
            title.Text = text;
        }
        else
        {
            title.Inlines = new Avalonia.Controls.Documents.InlineCollection
            {
                new Avalonia.Controls.Documents.Run(pathPrefix + "  ›  ") { Foreground = Style.LIGHT_WHITE.Opacity(0.45).ToBrush() },
                new Avalonia.Controls.Documents.Run(text),
            };
        }
        var dock = new DockPanel() { LastChildFill = true };
        if (chevron)
        {
            var arrow = new TextBlock() { Text = "▸", FontSize = 11, Margin = new(12, 0, 0, 0), Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            DockPanel.SetDock(arrow, Dock.Right);
            dock.Children.Add(arrow);
        }
        dock.Children.Add(title);

        var row = new Border()
        {
            Padding = new(10, 6),
            CornerRadius = new(4),
            Background = Brushes.Transparent,
            Cursor = new Cursor(clickable ? StandardCursorType.Hand : StandardCursorType.Arrow),
            Child = dock,
        };
        row.PointerEntered += (_, _) => row.Background = HoverBrush;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    // 分隔线：无标签 = 纯线；有标签 = 左线 + 居中文字 + 右线。不可交互、不计入选中。
    // 右键菜单的带组名分隔线（MenuSeparators.Labeled）也用它，两种菜单的分组观感一致。
    internal static Control BuildSeparator(string label)
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

    // ---- 搜索 ----
    // 有查询时根层改为「匹配叶子的扁平列表」（分组展平、分隔线略去），每行带灰色分组路径前缀；清空查询即复原树。
    // 匹配：查询按空白切成若干词，每个词都须（不区分大小写）出现在「分组路径 + 叶子文本」里——故可用组名收窄（如「滤波 低」）。
    // 键盘：↑/↓ 移动高亮、Enter 选高亮项（缺省第一项）、Esc 关闭。

    void OnQueryChanged()
    {
        if (mRootStack == null || mSearchBox == null)
            return;

        CloseFrom(0);
        mRootStack.Children.Clear();
        mMatches.Clear();
        mHighlight = -1;

        var terms = mSearchBox.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0)
        {
            FillTree(mRootStack, mTopItems, depth: 0);
            return;
        }

        foreach (var (leaf, path) in EnumerateLeaves(mTopItems, string.Empty))
        {
            var haystack = path.Length == 0 ? leaf.Text : path + " " + leaf.Text;
            if (!terms.All(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase)))
                continue;

            var captured = leaf;
            var row = MakeRow(leaf.Text, path, chevron: false, clickable: true);
            int index = mMatches.Count;
            row.PointerEntered += (_, _) => SetHighlight(index);
            row.PointerReleased += (_, e) => { e.Handled = true; Pick(captured); };
            mMatches.Add((leaf, row));
            mRootStack.Children.Add(row);
        }

        if (mMatches.Count == 0)
        {
            mRootStack.Children.Add(new TextBlock()
            {
                Text = "No matches".Tr(this),
                FontSize = 12,
                Margin = new(10, 6),
                Foreground = Style.LIGHT_WHITE.Opacity(0.45).ToBrush(),
            });
            return;
        }
        SetHighlight(0);
    }

    void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (mMatches.Count > 0)
                    SetHighlight((mHighlight + 1) % mMatches.Count);
                e.Handled = true;
                break;
            case Key.Up:
                if (mMatches.Count > 0)
                    SetHighlight((mHighlight - 1 + mMatches.Count) % mMatches.Count);
                e.Handled = true;
                break;
            case Key.Enter:
                if ((uint)mHighlight < (uint)mMatches.Count)
                    Pick(mMatches[mHighlight].Item);
                e.Handled = true;   // 拦下 TextInput 的「回车失焦」
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    void SetHighlight(int index)
    {
        if ((uint)mHighlight < (uint)mMatches.Count)
            mMatches[mHighlight].Row.Background = Brushes.Transparent;
        mHighlight = index;
        if ((uint)index < (uint)mMatches.Count)
        {
            var row = mMatches[index].Row;
            row.Background = HoverBrush;
            row.BringIntoView();
        }
    }

    static IEnumerable<(DropDownItem Leaf, string Path)> EnumerateLeaves(IReadOnlyList<DropDownItem> items, string path)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
                continue;
            if (item.IsGroup)
            {
                var childPath = path.Length == 0 ? item.Text : path + " › " + item.Text;
                foreach (var leaf in EnumerateLeaves(item.Children!, childPath))
                    yield return leaf;
            }
            else
            {
                yield return (item, path);
            }
        }
    }

    static int CountLeaves(IReadOnlyList<DropDownItem> items)
    {
        int count = 0;
        foreach (var item in items)
        {
            if (item.IsSeparator)
                continue;
            count += item.IsGroup ? CountLeaves(item.Children!) : 1;
        }
        return count;
    }

    // ---- 子菜单链 ----
    // mChain[d] = 从「层深 d 的菜单」展开出的那个子 Popup（每层至多一个，因为每层只停留在一个菜单里）。
    // 收起层深 >= depth 的全部子菜单（本层已展开的兄弟 + 其下所有更深层）。逆序关，逐个 IsOpen=false（各是独立 Popup 窗口）。
    void CloseFrom(int depth)
    {
        for (int i = mChain.Count - 1; i >= depth; i--)
        {
            mChain[i].IsOpen = false;
            mChain.RemoveAt(i);
        }
    }

    // 在层深 depth 展开子菜单 sub：先收起本层原有兄弟（及更深），再压入本层。
    // 能悬到 depth 层的行，其祖先链 [0..depth-1] 必在展开态，故 CloseFrom 后 mChain.Count 恰为 depth。
    void OpenAt(int depth, Popup sub)
    {
        CloseFrom(depth);
        if (mChain.Count != depth)
            return;   // 祖先链意外不完整（不应发生）——防御性跳过，不制造错位
        mChain.Add(sub);
        sub.IsOpen = true;
    }

    void Pick(DropDownItem leaf)
    {
        CloseFrom(0);
        mPopup.IsOpen = false;
        ItemPicked?.Invoke(leaf);
    }

    protected override Size MeasureOverride(Size availableSize) => default;
    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    static IBrush HoverBrush => Style.LIGHT_WHITE.Opacity(0.08).ToBrush();

    // 把菜单弹层的原生 ScrollViewer 适配成竖向 IScrollAxis 供自造 ScrollBar 驱动：Offset 即滚、Viewport/Extent 为视口/内容，
    // 偏移或尺寸变即通知条重绘。ScrollViewer 经事件订阅持有本适配器、本适配器与条互持 → 随菜单树共存亡，无需另存引用。
    sealed class ScrollViewerVerticalAxis : IScrollAxis
    {
        public ScrollViewerVerticalAxis(ScrollViewer sv)
        {
            mScrollViewer = sv;
            mScrollViewer.ScrollChanged += (_, _) => AxisChanged?.Invoke();
            mScrollViewer.SizeChanged += (_, _) => AxisChanged?.Invoke();
        }

        public event Action? AxisChanged;
        public double ViewLength { get => mScrollViewer.Viewport.Height; set { } }
        public double ContentLength => mScrollViewer.Extent.Height;
        public double ViewOffset
        {
            get => mScrollViewer.Offset.Y;
            set => mScrollViewer.Offset = new Vector(mScrollViewer.Offset.X, value);
        }

        readonly ScrollViewer mScrollViewer;
    }

    readonly Popup mPopup;
    readonly List<Popup> mChain = new();   // 当前展开的子菜单链（按层深索引，见 CloseFrom/OpenAt）
    IReadOnlyList<DropDownItem> mTopItems = [];
    StackPanel? mRootStack;
    TextInput? mSearchBox;
    readonly List<(DropDownItem Item, Border Row)> mMatches = new();
    int mHighlight = -1;
}
