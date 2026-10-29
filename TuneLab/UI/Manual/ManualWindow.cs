using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TuneLab.Animation;
using TuneLab.Docs;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;

namespace TuneLab.UI;

// 应用内用户手册：左侧目录 + 右侧**一整页**渲染正文（含插图）。内容与 docs/user-manual.{文化码}.md
// 同一份（随包发布，见 ManualLibrary），故手册与软件恒同版本。
//
// 正文刻意不按章分页：手册本来就是一整篇，读者顺着往下读时不该在章边界撞墙（下一章要回左侧点一下），
// 上下文（"上一节说的那个参数"）也不该被切断。目录的作用是**跳转**——点一条滚到那一章，反过来滚动
// 时高亮跟着走（scroll-spy），与文档站的通行做法一致。
//
// 窗体外壳（无系统边框 + 自绘顶栏 + 自制浮层滚动条）与扩展详情窗同一范式；markdown 渲染直接复用
// ChatMarkdownRenderer（标题/表格/列表/图片都已支持，图片相对路径按 baseDir 解析、点开可放大）。
internal sealed class ManualWindow : Window
{
    // 同时只留一个手册窗：再次触发菜单/快捷键时把已开的那个提到前面。
    public static void Open(Window? owner)
    {
        if (mInstance != null)
        {
            mInstance.Activate();
            return;
        }

        var window = new ManualWindow();
        mInstance = window;
        window.Closed += (_, _) =>
        {
            // 把整页从这个窗的滚动容器上摘下来，好让下次开窗直接接过去（见 mCachedPage）。
            if (window.mContentScroll != null)
                window.mContentScroll.Content = null;
            if (ReferenceEquals(mInstance, window))
                mInstance = null;
        };
        if (owner != null)
        {
            window.FitToScreen(owner);
            window.Show(owner);
        }
        else
            window.Show();
    }

    ManualWindow()
    {
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 40;
        CanResize = true;
        Width = 1000;
        Height = 720;
        MinWidth = 560;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = "User Manual".Tr(TC.Dialog) + " - TuneLab";
        Background = Style.INTERFACE.ToBrush();
        AppFont.Bind(this);

        mSections = ManualLibrary.Sections;

        var root = new DockPanel();
        root.AddDock(BuildTitleBar(), Dock.Top);
        root.AddDock(new Border { Height = 1, Background = Style.DARK.ToBrush() }, Dock.Top);
        root.AddDock(BuildBody());
        Content = root;

        // 默认高度让目录一屏放下、不出滚动：标题栏 + 分隔线 + 目录上下留白 + 每章一行 + 底部按钮区。
        // 按章数算而不是写死，手册加了章也不会又滚起来。屏幕放不下时开窗前再收（见 FitToScreen）。
        Height = Math.Max(Height, TitleBarHeight + 1 + TocPadding * 2 + mSections.Count * TocRowHeight + mTocFooterHeight + 4);

        mScrollAnimation.ValueChanged += OnScrollAnimationTick;
        if (mPage != null && mPage.Children.Count == 0)
            BuildPage();
        else
            Highlight(0);
    }

    // 顶栏：居中标题 + 右侧关闭键，空白处可拖动移窗（同扩展详情窗）。
    Control BuildTitleBar()
    {
        var bar = new Grid { Height = TitleBarHeight, Background = Style.INTERFACE.ToBrush() };

        bar.Children.Add(new TextBlock
        {
            Text = "User Manual".Tr(TC.Dialog),
            FontWeight = FontWeight.Bold,
            FontSize = 13,
            Foreground = Style.TEXT_LIGHT.ToBrush(),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(48, 0),
            IsHitTestVisible = false,
        });

        var close = new Button { Width = 40, Height = 40 }
            .AddContent(new()
            {
                Item = new BorderItem { CornerRadius = 0 },
                ColorSet = new() { HoveredColor = Style.CLOSE_HOVER, PressedColor = Style.CLOSE_PRESSED },
            })
            .AddContent(new()
            {
                Item = new IconItem { Icon = Assets.WindowClose },
                ColorSet = new() { Color = Style.LIGHT_WHITE, HoveredColor = Style.WHITE, PressedColor = Style.WHITE },
            });
        close.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        close.Clicked += Close;
        bar.Children.Add(close);


        bar.PointerPressed += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, bar))
                return;
            if (e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        return bar;
    }

    // 开窗前：默认高度超出所在屏幕的可用高度时收到屏幕内（小屏、高缩放下目录照样可以滚动）。
    void FitToScreen(Window owner)
    {
        if (owner.Screens.ScreenFromWindow(owner) is not { } screen)
            return;
        double available = screen.WorkingArea.Height / screen.Scaling - ScreenMargin * 2;
        if (Height > available)
            Height = Math.Max(MinHeight, available);
    }

    const double TitleBarHeight = 40;
    const double TocRowHeight = 36;
    const double TocPadding = 8;
    const double ScreenMargin = 24;
    double mTocFooterHeight;

    // 文字按钮：样式同扩展详情页的操作按钮（圆角底色）。撑满所在容器的宽度、文字居中。
    static Control TextButton(string label, Action onClick)
    {
        var btn = new Border
        {
            Background = Style.BUTTON_NORMAL.ToBrush(),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 5),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = Style.LIGHT_WHITE.ToBrush(),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        btn.PointerEntered += (_, _) => btn.Background = Style.BUTTON_NORMAL_HOVER.ToBrush();
        btn.PointerExited += (_, _) => btn.Background = Style.BUTTON_NORMAL.ToBrush();
        btn.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        return btn;
    }

    Control BuildBody()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(260, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        var sidebar = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        grid.Children.Add(sidebar);

        var content = BuildContent();
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    Control BuildSidebar()
    {
        var panel = new DockPanel { Background = Style.BACK.ToBrush() };
        panel.AddDock(new Border { Width = 1, Background = Style.DARK.ToBrush() }, Dock.Right);

        if (mSections.Count == 0)
            return panel;

        var list = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, TocPadding) };
        for (int i = 0; i < mSections.Count; i++)
        {
            int index = i;
            var section = mSections[i];

            var label = new TextBlock
            {
                Text = section.Title,
                FontSize = 13,
                Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 12, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false,
            };
            var accent = new Border
            {
                Width = 3,
                Background = Brushes.Transparent,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                IsHitTestVisible = false,
            };

            var row = new Button { Height = TocRowHeight }
                .AddContent(new()
                {
                    Item = new BorderItem { CornerRadius = 0 },
                    ColorSet = new() { HoveredColor = Colors.White.Opacity(0.05), PressedColor = Colors.White.Opacity(0.08) },
                });
            var layer = new LayerPanel();
            layer.Children.Add(row);
            layer.Children.Add(accent);
            layer.Children.Add(label);
            row.Clicked += () => ScrollToSection(index);

            mRowLabels.Add(label);
            mRowAccents.Add(accent);
            list.Children.Add(layer);
        }

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = list,
        };
        mSidebarScrollBars = new OverlayScrollBars(scroll, horizontal: false, vertical: true);
        // 目录栏底部：用系统默认程序打开随包的手册文件（同扩展详情页的「用外部编辑器打开」），与目录同宽，上方一条分隔线。
        if (ManualLibrary.FilePath is { } manualPath)
        {
            var open = TextButton("Open in External Editor".Tr(TC.Dialog), () => ProcessHelper.OpenFile(manualPath));
            var footer = new DockPanel();
            footer.AddDock(new Border { Height = 1, Background = Style.DARK.ToBrush() }, Dock.Top);
            footer.AddDock(new Border { Padding = new Thickness(12), Child = open });
            panel.AddDock(footer, Dock.Bottom);
            footer.Measure(Size.Infinity);
            mTocFooterHeight = footer.DesiredSize.Height;
        }

        panel.AddDock(scroll);
        return panel;
    }

    Control BuildContent()
    {
        // 整页只建一次，之后各次开窗复用同一份控件树（连已解码的插图一起）：整篇要建几千个控件、
        // 解码二十多张界面截图，实测建页 500ms 上下、大头在解码——每次开窗重来一遍就是每次卡一下。
        // 缓存的是内容，不是窗口：窗口该关就关（留个隐藏窗会让 OnLastWindowClose 判不出"最后一个窗
        // 关了"，主窗关掉进程还活着），外壳每次重建、开销可忽略。
        mPage = mCachedPage;
        if (mPage == null)
        {
            // 内边距放在内容自身的 Margin 上（左右对称）；ScrollViewer.Padding 会让内容算漏右侧内边距。
            mPage = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(28, 20, 28, 60) };
            mCachedPage = mPage;
        }
        else
        {
            mSectionBlocks.AddRange(mCachedBlocks);
        }
        mContentScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Style.INTERFACE.ToBrush(),
            Content = mPage,
            // 右侧留白：浮层滚动条画在这个 ScrollViewer 的右缘，贴着窗口边就会与无边框窗的 resize
            // 命中区重叠——想拖手柄却把窗口拉宽了。让整块内容区离右边界 8px，手柄自然退到安全区内。
            Margin = new Thickness(0, 0, ScrollBarInset, 0),
        };
        mContentScrollBars = new OverlayScrollBars(mContentScroll, horizontal: false, vertical: true);
        // 滚动联动高亮：读者往下读时目录跟着走，不必自己找"我在哪一章"。
        mContentScroll.PropertyChanged += (_, e) =>
        {
            if (e.Property != ScrollViewer.OffsetProperty || mApplyingAnimatedOffset)
                return;
            // 用户自己滚了 → 让位：跳转动画立即作罢（否则它会把画面拽回去，像在跟用户抢方向盘）。
            mScrollAnimating = false;
            SyncHighlightToScroll();
        };
        return mContentScroll;
    }

    // 一次把整篇建好：逐章 = 一个块（章标题 + 该章正文），块本身就是跳转锚点。
    void BuildPage()
    {
        if (mPage == null)
            return;

        if (mSections.Count == 0)
        {
            mPage.Children.Add(new TextBlock
            {
                Text = "The user manual is not bundled with this build.".Tr(TC.Dialog),
                FontSize = 12,
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
                Margin = new Thickness(0, 40, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        if (!ManualLibrary.IsCurrentLanguage)
        {
            // 缺当前界面语言的手册时如实说明用的是哪一版（而不是让读者以为翻译坏了）。
            mPage.Children.Add(new TextBlock
            {
                Text = string.Format("This manual is not available in the current language yet; showing the {0} edition."
                    .Tr(TC.Dialog), ManualLibrary.Language),
                FontSize = 12,
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16),
            });
        }

        for (int i = 0; i < mSections.Count; i++)
        {
            var section = mSections[i];
            var block = new StackPanel { Orientation = Orientation.Vertical };
            block.Children.Add(new TextBlock
            {
                Text = section.Title,
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = Style.TEXT_LIGHT.ToBrush(),
                // 章间留白拉开层级；首章不留上边距（内容根已有）。
                Margin = new Thickness(0, i == 0 ? 0 : 36, 0, 12),
                TextWrapping = TextWrapping.Wrap,
            });
            block.Children.Add(ChatMarkdownRenderer.Render(section.Body, ManualLibrary.BaseDir, OnLink));
            mPage.Children.Add(block);
            mSectionBlocks.Add(block);
        }

        mCachedBlocks.Clear();
        mCachedBlocks.AddRange(mSectionBlocks);
        Highlight(0);
    }

    // 正文里的链接：手册内的 #锚点 跳到本页对应的章（三级标题跳到它所在的章）；指向仓库里其他文档的相对路径
    //（插件开发文档、生成的快捷键表……）不随软件发布，改为在浏览器里打开 GitHub 上的同名页面。
    // 手册源文件里保持相对路径——在 GitHub 上看手册时这些链接本来就能用。其余（http 等）交回系统打开。
    bool OnLink(string url)
    {
        if (url.StartsWith('#'))
        {
            int index = ManualLibrary.IndexOfAnchor(mSections, Uri.UnescapeDataString(url[1..]));
            if (index >= 0)
                ScrollToSection(index);
            return true;
        }
        if (!url.Contains("://", StringComparison.Ordinal) && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            ProcessHelper.OpenUrl(AppInfo.GitHubUrl + "/blob/master/docs/" + url.TrimStart('.', '/'));
            return true;
        }
        return false;
    }

    // 目录点击 → 平滑滚到该章（章标题落在视口顶端）。动画而非瞬移：跨十几章的一跳若是瞬移，
    // 读者失去"往哪个方向走了多远"的线索，回不过神来自己在哪。
    void ScrollToSection(int index)
    {
        if (mContentScroll == null || mPage == null || index < 0 || index >= mSectionBlocks.Count)
            return;

        double y = OffsetOf(index);
        // 末章不足一屏时钳到底部，免得点了最后一条只挪一点点、看着像没反应。
        double max = Math.Max(0, mContentScroll.Extent.Height - mContentScroll.Viewport.Height);
        mScrollTargetY = Math.Min(y, max);

        // 目标高亮当场就给（动画途中会掠过好几章，靠联动去点亮会闪成一片）。
        Highlight(index);

        double from = mContentScroll.Offset.Y;
        if (Math.Abs(mScrollTargetY - from) < 0.5)
            return;

        // 动画分两段，但**一条曲线一次跑完**（两段各跑一次动画会在接缝处留下停顿，也要处理重入）：
        //   ① 长途段：目标前 ScrollRunUp 以外的距离，加速掠过——时长按距离缩放但夹在窄区间内，
        //      故跳得越远掠得越快，总时长不失控；
        //   ② 收尾段：最后 ScrollRunUp 这段，先快后慢减速到位——"从哪个方向来、到了"全靠它交代。
        // 距离不足 ScrollRunUp 时没有①，直接从当前位置减速到位。
        double distance = Math.Abs(mScrollTargetY - from);
        double duration;
        IAnimationCurve curve;
        if (distance <= ScrollRunUp)
        {
            duration = ScrollRunUpMs;
            curve = AnimationCurve.CubicOut;
        }
        else
        {
            double dashMs = Math.Clamp((distance - ScrollRunUp) / ScrollDashSpeed, ScrollDashMsMin, ScrollDashMsMax);
            duration = dashMs + ScrollRunUpMs;
            double p = (distance - ScrollRunUp) / distance;   // 长途段占的位移比例
            double t = dashMs / duration;                     // 它占的时间比例
            curve = new AnimationCurve(x => x <= t
                // 长途段用 QuadIn：从静止加速起飞（匀速会在起手处硬弹一下）。
                ? p * (x / t) * (x / t)
                // 收尾段用 CubicOut：接住长途段的速度再缓缓刹到 0。
                : p + (1 - p) * CubicOutRatio((x - t) / (1 - t)));
        }

        mScrollAnimating = true;
        mScrollAnimation.Value = from;
        mScrollAnimation.SetTo(mScrollTargetY, duration, curve);
    }

    // CubicOut 的比例曲线（与 AnimationCurve.CubicOut 同式）：分段曲线要在自己的段内重用它。
    static double CubicOutRatio(double x) => 1 + (x - 1) * (x - 1) * (x - 1);

    // 动画每帧把值写进 Offset。写入期间压住联动高亮：那是"我们自己滚的"，不是用户在滚。
    void OnScrollAnimationTick()
    {
        if (!mScrollAnimating || mContentScroll == null)
            return;

        mApplyingAnimatedOffset = true;
        mContentScroll.Offset = new Vector(mContentScroll.Offset.X, mScrollAnimation.Value);
        mApplyingAnimatedOffset = false;

        if (Math.Abs(mScrollAnimation.Value - mScrollTargetY) < 0.5)
            mScrollAnimating = false;   // 到位，恢复联动
    }

    // 块在内容坐标系里的纵向位置（= ScrollViewer.Offset 的口径）。内容根的上边距要算进去。
    double OffsetOf(int index)
    {
        if (mPage == null || index < 0 || index >= mSectionBlocks.Count)
            return 0;
        var origin = mSectionBlocks[index].TranslatePoint(default, mPage);
        return (origin?.Y ?? 0) + mPage.Margin.Top;
    }

    // 滚动位置 → 当前所在章：视口顶端往下 24px 处落在哪一章。
    void SyncHighlightToScroll()
    {
        if (mContentScroll == null || mSectionBlocks.Count == 0)
            return;

        double probe = mContentScroll.Offset.Y + 24;
        int current = 0;
        for (int i = 0; i < mSectionBlocks.Count; i++)
        {
            if (OffsetOf(i) <= probe)
                current = i;
            else
                break;
        }
        Highlight(current);
    }

    void Highlight(int index)
    {
        if (index == mHighlighted)
            return;
        mHighlighted = index;
        for (int i = 0; i < mRowLabels.Count; i++)
        {
            bool selected = i == index;
            mRowLabels[i].Foreground = (selected ? Style.WHITE : Style.LIGHT_WHITE.Opacity(0.6)).ToBrush();
            mRowAccents[i].Background = selected ? Style.HIGH_LIGHT.ToBrush() : Brushes.Transparent;
        }
    }

    // 收尾段：目标前这段距离用来减速到位。约小半屏，够看清"从哪来、到了"。
    const double ScrollRunUp = 240;
    // 收尾段时长；比 200ms 短就看不出方向，比 300ms 长就显得拖沓。
    const double ScrollRunUpMs = 240;
    // 长途段（收尾段之外那截）的掠过速度与时长夹界：速度定档 + 时长设上下限，
    // 于是近处不显拖沓、远处也不至于慢慢爬。
    const double ScrollDashSpeed = 12;      // px/ms
    const double ScrollDashMsMin = 90;
    const double ScrollDashMsMax = 180;
    // 内容区离窗口右边界的留白。加上 ScrollBar 自身的 2px 边距 = 手柄外沿距窗口边 6px：
    // 让开无边框窗约 4px 的 resize 命中区即可，留太多反而让滚动条离内容太远、像浮在半空。
    const double ScrollBarInset = 4;

    static ManualWindow? mInstance;
    // 整页控件树与逐章锚点：跨窗复用（见 BuildContent）。手册内容随包不变，故一份就够。
    static StackPanel? mCachedPage;
    static readonly List<Control> mCachedBlocks = new();

    readonly IReadOnlyList<ManualSection> mSections;
    readonly List<TextBlock> mRowLabels = new();
    readonly List<Border> mRowAccents = new();
    readonly List<Control> mSectionBlocks = new();
    readonly AnimationValue mScrollAnimation = new();
    StackPanel? mPage;
    ScrollViewer? mContentScroll;
    int mHighlighted = -1;
    double mScrollTargetY;
    bool mScrollAnimating;
    bool mApplyingAnimatedOffset;
    OverlayScrollBars? mSidebarScrollBars;
    OverlayScrollBars? mContentScrollBars;
}
