using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Extensions;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;

namespace TuneLab.UI;

// 详情窗正文的一页：对应包内一个 manifest 条目（= 一个能力位）。
// introduction 是条目级、不回退包级 description——作者没写就显占位（见 ExtensionInfo 头注释）。
internal sealed class ExtensionDetailPage
{
    public required string Title;                  // 条目显示名（作 tab 标签）
    public string Kind = string.Empty;             // 类别（tab 标签旁的小徽标）；资源类为其自定 type
    // 该条目占的能力位身份：engine 类一个 engine id；format 是它认的全部后缀。多于一个时在页里列出，
    // 免得用户只看到一个格式名、不知道它管哪些文件。
    public IReadOnlyList<string> Identities = [];
    // Identities 是否为【文件后缀】（format 条目）：是则在页里恒列出来，因为"能打开哪些文件"是用户对一个
    // 格式最需要知道的事；engine 类的身份是内部注册键，不列。
    public bool IdentitiesAreFileSuffixes;
    public string? Markdown;                       // introduction 正文；无则显「无文档」占位
    public string? FilePath;                       // introduction 绝对路径（供「用外部编辑器打开」）；无则不显该按钮
    // 该条目自己的扩展设置桶键（"kind:extensionId"）；非空才在本页显示齿轮。
    // 设置是 per 能力实现者的，所以齿轮归属【页】而非包——一个包里 voice 与 effect 各有各的设置。
    public string? SettingsKey;
    // manifest 原样的 type（小写）。Kind 是首字母大写的**展示**串，而启停键要与加载期同口径，
    // 故另存一份原值——用展示串去拼键会写出一个永远命不中的键。
    public string EntryKind = string.Empty;
    // 本条目能否单独启停：资源类无身份、无法成键，只能随整包关（见 ExtensionActivation.CanDisableEntry）。
    public bool CanDisable;
}

// 一个扩展详情的展示数据：包级元数据（manifest 顶层）+ 逐条目的 introduction 页。
internal sealed class ExtensionDetailInfo
{
    public required string Name;
    public required string Version;
    public string Author = string.Empty;
    // 包级 description（描述包整体）。只在 header 显示——它讲"这个包是什么"，不代表包里任何单个能力，
    // 故不会被搬进各条目页去冒充能力说明。
    public string Description = string.Empty;
    public string? IconPath;
    // 类别徽标：**仅当没有条目页可承载它时**才显示在 header（legacy 包、或 manifest 解析失败的包）。
    // 有条目时每个 tab 已带自己的类别徽标，header 再来一排就是把同一信息说两遍（那排本就是各条目 kind
    // 的去重并集）。侧栏卡片上的徽标始终保留——那里没有 tab。
    public IReadOnlyList<string> Types = [];
    public required string PackageDir;             // introduction 里相对图片解析的 baseDir
    // 启停键的前半（V1 = manifest id，legacy = 目录名）。为空则本窗不提供启停开关。
    public string? PackageId;
    // 逐 manifest 条目一页。**空 = 这个包没有 manifest 条目**：legacy 包（能力由兼容层盲扫发现）或
    // manifest 解析失败的包。此时不显 tab 条（无条目可分），正文按 Generation 给出对应说明。
    public IReadOnlyList<ExtensionDetailPage> Pages = [];
    public bool IsLegacy;                          // 决定"无文档"占位的措辞：legacy 是机制使然，不是作者没写
    public bool IsPendingUninstall;                // 打开时该插件是否已处于待卸载态（决定卸载按钮初始态）
}

// 扩展详情窗：点侧栏条目弹出，逐条目渲染 introduction（正文完全由作者定义、宿主不解释）。
// 独立【可缩放】窗口（区别于固定尺寸的 Dialog）——长富文本详情需要足够阅读宽度与自由缩放。
// 无边框 + 扩展客户区（沿用 Dialog 的自绘外观），顶栏自绘（拖动 + 关闭），正文区滚动。
internal sealed class ExtensionDetailWindow : Window
{
    // 齿轮 → 跳设置窗并定位到【当前页那个能力位】的设置（参数为其桶键 "kind:extensionId"）；
    // Uninstall/CancelUninstall → 卸载/撤销（这两个是包级操作，故留在 header）。
    // 由 provider 接到宿主既有流程并跨视图同步态。
    public event Action<string>? SettingsRequested;
    public event Action? UninstallRequested;
    public event Action? CancelUninstallRequested;
    // 本窗改了启停（包级或条目级，选择已落盘）。provider 据此同步侧栏卡片与「需重启」提示。
    public event Action? ActivationChanged;

    public ExtensionDetailWindow(ExtensionDetailInfo info)
    {
        mPackageId = info.PackageId;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 40;
        CanResize = true;
        Width = 760;
        Height = 680;
        MinWidth = 460;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = info.Name + " - TuneLab";
        // 整窗 INTERFACE 底色（与 item 卡片一致、顶部全覆盖，避免任何 BACK 底色露出）。
        Background = Style.INTERFACE.ToBrush();

        var root = new DockPanel();
        root.AddDock(BuildTitleBar(info.Name), Dock.Top);
        root.AddDock(new Border { Height = 1, Background = Style.DARK.ToBrush() }, Dock.Top);
        root.AddDock(BuildHeader(info), Dock.Top);
        root.AddDock(new Border { Height = 1, Background = Style.DARK.ToBrush() }, Dock.Top);
        root.AddDock(BuildBody(info)); // 填充剩余
        Content = root;
    }

    // 顶栏：可拖动移动窗口 + 居中标题 + 右侧关闭按钮。
    Control BuildTitleBar(string title)
    {
        var bar = new Grid { Height = 40, Background = Style.INTERFACE.ToBrush() };

        bar.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.Bold,
            FontSize = 13,
            Foreground = Style.TEXT_LIGHT.ToBrush(),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(48, 0),
            IsHitTestVisible = false, // 不挡拖动
        });

        // 关闭键改用内置 Button：它的 Clicked 在【抬起且指针仍在键内】才触发（MouseUp + IsClick），
        // 所以按下后拖走可以取消——这才是按钮该有的语义。悬浮红底、白叉，随系统窗口关闭键的惯例。
        // 与设置窗/歌词板的关闭键同一范式（内置 Button + Assets.WindowClose + Clicked）。
        var close = new GUI.Components.Button { Width = 40, Height = 40 }
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

        // 顶栏空白处拖动移窗。内置 Button（Component）刻意【不吞】PointerPressed，故这里必须按事件源过滤：
        // 只有落在顶栏自身上才拖窗——否则按下关闭键就会开始拖动、指针被拖动接管、Clicked 永远不触发。
        // 标题 TextBlock 已 IsHitTestVisible=false，点它时 Source 仍是 bar，照样可拖。
        bar.PointerPressed += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, bar))
                return;
            if (e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        return bar;
    }

    // header：方形图标（高度动态对齐右侧信息列的四排）+ 名称/版本 + 作者(带图标) + 简介 + 类型徽标；右侧操作列。
    Control BuildHeader(ExtensionDetailInfo info)
    {
        var panel = new DockPanel { Margin = new Thickness(24, 20), Background = Style.INTERFACE.ToBrush() };

        // 信息列（决定 header 高度）——先建，供图标按其高度取方形边长。
        var info_ = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

        // 名称 + 版本徽标
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        nameRow.Children.Add(new TextBlock
        {
            Text = info.Name,
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = Style.TEXT_LIGHT.ToBrush(),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });
        // 版本徽标：与卡片一致——BACK 底色（在 INTERFACE 头上呈深色 chip）。
        nameRow.Children.Add(new Border
        {
            Background = Style.BACK.ToBrush(),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 2),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Child = new TextBlock { Text = "v" + info.Version, FontSize = 11, Foreground = Style.LIGHT_WHITE.Opacity(0.7).ToBrush() },
        });
        info_.Children.Add(nameRow);

        // 作者行：前置作者图标（与卡片一致）+ 名字。
        if (!string.IsNullOrWhiteSpace(info.Author))
        {
            var authorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            authorRow.Children.Add(new Image
            {
                Source = Assets.Author.GetImage(Style.LIGHT_WHITE.Opacity(0.6)),
                Width = 12,
                Height = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
            authorRow.Children.Add(new TextBlock { Text = info.Author, FontSize = 11, Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            info_.Children.Add(authorRow);
        }

        // 包级 description（讲整个包）。各能力位自己的一句话摘要在正文各页顶部，不在这里。
        if (!string.IsNullOrWhiteSpace(info.Description))
            info_.Children.Add(new TextBlock { Text = info.Description, FontSize = 12, Foreground = Style.LIGHT_WHITE.ToBrush(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });

        // 类别徽标只在【没有条目页】时落在 header——那时没有 tab 来承载它（legacy 包 / manifest 解析失败）。
        if (info.Pages.Count == 0 && info.Types.Count > 0)
        {
            var tags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var t in info.Types)
                tags.Children.Add(new Border
                {
                    Background = Style.BACK.ToBrush(), // 与卡片一致的深色 chip
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 2),
                    Child = new TextBlock { Text = t, FontSize = 11, Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush() },
                });
            info_.Children.Add(tags);
        }

        panel.AddDock(BuildSquareIcon(info, info_), Dock.Left);
        panel.AddDock(BuildActionPanel(info), Dock.Right);
        panel.Children.Add(info_); // 填充
        return panel;
    }

    // 方形图标：边长跟随信息列实测高度（与文字块等高、上下对齐），但**钳在区间内**。
    // 下限的必要性：信息列排数是数据决定的——legacy 包常常只有"名 + 版本"一排（无作者、无 description），
    // 不钳的话图标会被压成二十几像素、还被 ClipToBounds 裁掉边缘。上限则防超长 description 把图标撑成巨块。
    // 图标自身不驱动 header 高度（初始给个尺寸、布局后按 info 高度回设），避免大图标撑爆 header。
    const double IconSideMin = 56;
    const double IconSideMax = 96;

    Control BuildSquareIcon(ExtensionDetailInfo info, Control infoColumn)
    {
        var inner = ExtensionItemView.CreateIconVisual(info.IconPath, info.Name, 72);
        inner.Width = double.NaN;
        inner.Height = double.NaN;
        inner.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        inner.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        if (inner is Image img)
            img.Stretch = Stretch.Uniform;

        var host = new Border
        {
            Width = 72,
            Height = 72,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            ClipToBounds = true,
            Child = inner,
        };
        // 信息列高度确定后，把图标设成同高的正方形（钳在 [IconSideMin, IconSideMax]，见上方注释）。
        infoColumn.PropertyChanged += (_, e) =>
        {
            if (e.Property != Visual.BoundsProperty)
                return;
            var h = infoColumn.Bounds.Height;
            if (h <= 0)
                return;
            var side = System.Math.Clamp(h, IconSideMin, IconSideMax);
            if (double.IsNaN(host.Height) || System.Math.Abs(host.Height - side) > 0.5)
            {
                host.Height = side;
                host.Width = side;
            }
        };
        return host;
    }

    // 右侧操作列：撑满 header 高度。顶部＝「用外部编辑器打开」；底部＝「设置 + 卸载」一排。各自条件显示。
    Control BuildActionPanel(ExtensionDetailInfo info)
    {
        var col = new DockPanel
        {
            LastChildFill = false, // 让 Top/Bottom 各自贴边、中间留空
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };

        // header 只放【包级】操作。条目级的三个（启停本条目、打开当前页的 introduction 文件、当前能力位的
        // 设置）挪到 tab 条右端——那一行本就是"当前条目"的上下文（见 BuildTabBar）。

        // 顶：包级启停开关 +（存的选择与本次运行不符时的）需重启提示。
        // 整包关掉时连程序集都不加载，是真能省启动时间的那一档；legacy 包与 manifest 坏包更是只有这一档
        // 可关（它们没有条目）。
        if (!string.IsNullOrEmpty(info.PackageId))
        {
            var topRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };

            mRestartHint = new TextBlock
            {
                Text = "Restart Required".Tr(TC.Dialog),
                FontSize = 11,
                Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                IsVisible = false,
            };
            topRow.Children.Add(mRestartHint);

            mPackageStateText = new TextBlock
            {
                FontSize = 12,
                Foreground = Style.LIGHT_WHITE.ToBrush(),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            topRow.Children.Add(mPackageStateText);

            var packageId = info.PackageId;
            // 开关显示【存下来的选择】而非本次运行的状态；不一致由 SetRestartRequired 亮提示，
            // 而不是让开关自己回弹装作没改。形制与侧栏卡片共用一个工厂（关=灰、开=高亮）。
            mPackageSwitch = CreateActivationSwitch(
                !ExtensionActivation.IsPackageDisabled(packageId),
                "Enable or disable this extension. Takes effect after restarting TuneLab.".Tr(TC.Dialog));
            mPackageSwitch.ValueCommitted.Subscribe(() =>
            {
                ExtensionActivation.SetPackageEnabled(packageId, mPackageSwitch.Value);
                SyncPackageStateText();
                // 整包关掉后，条目开关无从单独操作——就地重建当前页的操作区反映这一点。
                if (mCurrentPage != null)
                    SyncPageActions(mCurrentPage);
                ActivationChanged?.Invoke();
            });
            topRow.Children.Add(mPackageSwitch);
            SyncPackageStateText();

            col.AddDock(topRow, Dock.Top);
        }

        // 底：卸载。header 只放【包级】操作——设置是 per 能力实现者的，故齿轮挪到各页内（见 BuildPageContent）。
        var bottomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
        };
        bottomRow.Children.Add(BuildUninstallButton(info.IsPendingUninstall));
        col.AddDock(bottomRow, Dock.Bottom);

        return col;
    }

    // 统一操作按钮：可选前置图标 + 文本，hover 变色，点击回调。右对齐、宽度贴合内容。
    Control TextButton(string label, SvgIcon? icon, Action onClick)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        if (icon != null)
            content.Children.Add(new Image
            {
                Width = 15,
                Height = 15,
                Source = icon.GetImage(Style.LIGHT_WHITE),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });

        var btn = new Border
        {
            Background = Style.BUTTON_NORMAL.ToBrush(),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Child = content,
        };
        btn.PointerEntered += (_, _) => btn.Background = Style.BUTTON_NORMAL_HOVER.ToBrush();
        btn.PointerExited += (_, _) => btn.Background = Style.BUTTON_NORMAL.ToBrush();
        btn.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
        return btn;
    }

    // 卸载按钮（两态，与卡片一致）：正常态点击走卸载流程；待卸载态点击弹「取消卸载」菜单。
    Control BuildUninstallButton(bool pending)
    {
        mUninstallText = new TextBlock { FontSize = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        mUninstallBtn = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Child = mUninstallText,
        };
        mUninstallBtn.PointerEntered += (_, _) => { if (!mPendingUninstall) mUninstallBtn!.Background = Style.BUTTON_NORMAL_HOVER.ToBrush(); };
        mUninstallBtn.PointerExited += (_, _) => { if (!mPendingUninstall) mUninstallBtn!.Background = Style.BUTTON_NORMAL.ToBrush(); };
        mUninstallBtn.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (mPendingUninstall)
            {
                var menu = new ContextMenu();
                menu.Items.Add(new MenuItem().SetName("Cancel Uninstall".Tr(TC.Dialog)).SetAction(() => CancelUninstallRequested?.Invoke()));
                mUninstallBtn!.OpenContextMenu(menu);
            }
            else
            {
                UninstallRequested?.Invoke();
            }
        };
        SetUninstallPending(pending);
        return mUninstallBtn;
    }

    // 启停开关的统一形制（本窗的包级与条目级两处共用；侧栏卡片刻意不放开关，见 ExtensionItemView）。
    // 关掉时 pill 变灰（`UncheckedHighlightColor`）：`Switch` 原本是给"两选一"用的（两半各有图标、
    // 各代表一个有意义的选项），而启停是 on/off 且不放图标——不换色的话两态就只差 pill 在左还是在右，
    // 太容易读反。
    static Switch CreateActivationSwitch(bool enabled, string tooltip)
    {
        var sw = new Switch
        {
            Width = 36,
            Height = 18,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            UncheckedHighlightColor = Style.LIGHT_WHITE.Opacity(0.35),
        };
        sw.OffToolTip = sw.OnToolTip = tooltip;
        sw.Display(enabled);
        return sw;
    }

    // 开关旁的字面状态（Enabled / Disabled）：光看拨到哪一侧不足以让人确信，写出来最省心。
    void SyncPackageStateText()
    {
        if (mPackageStateText == null || mPackageSwitch == null)
            return;
        bool enabled = mPackageSwitch.Value;
        mPackageStateText.Text = enabled ? "Enabled".Tr(TC.Dialog) : "Disabled".Tr(TC.Dialog);
        mPackageStateText.Foreground = (enabled ? Style.LIGHT_WHITE : Style.LIGHT_WHITE.Opacity(0.5)).ToBrush();
    }

    // 由 provider 调用：亮/灭「需重启」提示（存下来的启停选择 ≠ 本次运行的实际状态）。
    public void SetRestartRequired(bool required)
    {
        if (mRestartHint != null)
            mRestartHint.IsVisible = required;
    }

    // 由 provider 在卸载确认/取消后调用，跨视图同步卸载按钮态（Uninstall ↔ Pending Uninstall）。
    public void SetUninstallPending(bool pending)
    {
        mPendingUninstall = pending;
        if (mUninstallBtn == null || mUninstallText == null)
            return;
        if (pending)
        {
            mUninstallBtn.Background = Style.BACK.ToBrush();
            mUninstallText.Text = "Pending Uninstall".Tr(TC.Dialog);
            mUninstallText.Foreground = Style.LIGHT_WHITE.Opacity(0.4).ToBrush();
        }
        else
        {
            mUninstallBtn.Background = Style.BUTTON_NORMAL.ToBrush();
            mUninstallText.Text = "Uninstall".Tr(TC.Dialog);
            mUninstallText.Foreground = Style.LIGHT_WHITE.ToBrush();
        }
    }

    // 正文：逐条目一页 introduction，顶部一行 tab 切换。
    // 【单条目包也显 tab 条】：那个标签是条目自己的显示名 + 类别徽标，与 header 的包名是两回事（包名与条目名
    // 可以不同，多后缀 format 更是只有条目名才说得清它是什么），属有效信息，不因"只有一个"就藏掉。
    // 横向禁滚（内容按视口宽换行、根治右边界溢出）；纵向隐藏原生条、改挂 app 自制浮层滚动条 OverlayScrollBars。
    Control BuildBody(ExtensionDetailInfo info)
    {
        var host = new ContentControl();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Style.INTERFACE.ToBrush(),
            Content = host,
        };

        // 自制浮层竖向滚动条（原生已 Hidden）。存字段防 GC；attach 在 host 入可视树时自动完成。
        mScrollBars = new OverlayScrollBars(scroll, horizontal: false, vertical: true);

        var root = new DockPanel();

        // 没有 manifest 条目（legacy 包 / manifest 解析失败）→ **不建 tab 条**：一个标签写着包名、点了
        // 没反应的 tab 是纯噪音。此时类别徽标已回到 header，正文按 Generation 给出对应说明。
        if (info.Pages.Count == 0)
        {
            host.Content = BuildNoEntryContent(info);
            root.Children.Add(scroll);
            return root;
        }

        root.AddDock(BuildTabBar(info, info.Pages, host, scroll), Dock.Top);
        root.AddDock(new Border { Height = 1, Background = Style.DARK.ToBrush() }, Dock.Top);
        root.Children.Add(scroll); // 填充剩余

        SelectPage(info, info.Pages, host, scroll, 0);
        return root;
    }

    // 无 manifest 条目时的正文：legacy 包要说清"没文档"是机制使然（旧插件不提供 manifest 元数据，宿主是
    // 靠兼容层盲扫出它的能力的），否则用户会以为作者偷懒没写。其余情形（如 manifest 解析失败）用通用文案。
    Control BuildNoEntryContent(ExtensionDetailInfo info)
    {
        var text = info.IsLegacy
            ? "This is a legacy extension. It ships no manifest metadata, so TuneLab discovers its capabilities by scanning — there is no author-provided documentation to show.".Tr(TC.Dialog)
            : "This extension has no documentation.".Tr(TC.Dialog);

        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            MaxWidth = 420,
            Margin = new Thickness(24, 48, 24, 24),
        };
    }

    // tab 条：一行横向标签（不换行；标签多到超宽时横向可滚，保持"一行"形态）。
    // 沿用仓库内手写 tab 的范式（见 SettingsWindow 的侧边 tab）：Border 按钮 + 选中态高亮条，不用 Avalonia
    // TabControl——后者外观与本窗自绘 chrome 不搭。
    Control BuildTabBar(ExtensionDetailInfo info, IReadOnlyList<ExtensionDetailPage> pages, ContentControl host, ScrollViewer scroll)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Background = Style.INTERFACE.ToBrush() };

        for (int i = 0; i < pages.Count; i++)
        {
            int index = i;
            var page = pages[i];

            // 标签与徽标在同一行【竖直居中】：按钮给固定高度、内边距左右对称，内容整体居中——
            // 不用「上 padding 撑、下靠 accent 顶」那种排法，那样文字与徽标的视觉重心会错开（前者含字形
            // 基线留白、后者是精确矩形）。
            var label = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            var text = new TextBlock
            {
                Text = page.Title,
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            label.Children.Add(text);
            // 类别徽标：多条目包里显示名可能重名或都回退成身份 id，靠它区分谁是 voice 谁是 effect。
            if (!string.IsNullOrEmpty(page.Kind))
                label.Children.Add(new Border
                {
                    Background = Style.BACK.ToBrush(),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = page.Kind,
                        FontSize = 10,
                        Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    },
                });

            var accent = new Border { Height = 2, Background = Brushes.Transparent };
            var stack = new DockPanel();
            stack.AddDock(accent, Dock.Bottom);
            stack.Children.Add(label); // 填充剩余高度，label 在其中居中

            var button = new Border
            {
                Height = 38,
                Padding = new Thickness(14, 0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent,
                Child = stack,
            };
            button.PointerPressed += (_, e) => { e.Handled = true; SelectPage(info, pages, host, scroll, index); };

            button.PointerEntered += (_, _) => { if (mSelectedPage != index) text.Foreground = Style.TEXT_LIGHT.ToBrush(); };
            button.PointerExited += (_, _) => { if (mSelectedPage != index) text.Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(); };

            mTabs.Add((text, accent));
            row.Children.Add(button);
        }

        // 标签区可横向滚（标签多到超宽时仍保持"一行"形态）；右端固定放【当前条目】的操作按钮——
        // 它们随 tab 切换而变，放这里既不多占一行、也不会像原来那样让正文顶部左半边空着。
        //
        // 【滚而不显条】横一道滚动条会把本来只有一行的 tab 条视觉上切成两层，比"看不出能滚"更伤观感。
        // 代价是可滚动性没有视觉提示，故挂上仓里统一的平滑滚轮（指数缓动，与下拉/输入框等处同一手感）。
        // horizontalOnly：本条只有横轴可滚，普通滚轮就该驱动它——否则 SmoothWheelScroller 会去看纵轴、
        // 发现无可滚内容而放行，隐藏了条就等于彻底滚不动。
        var tabs = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
            Content = row,
        };
        mTabWheel = new SmoothWheelScroller(tabs, () => tabs, allowHorizontal: true, horizontalOnly: true);

        mPageActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 0, 12, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var bar = new DockPanel { Background = Style.INTERFACE.ToBrush() };
        bar.AddDock(mPageActions, Dock.Right);
        bar.Children.Add(tabs); // 填充剩余
        return bar;
    }

    // 切页：更新 tab 选中态、正文内容（惰性构建并缓存）、外部编辑器按钮，并把滚动位置归零。
    void SelectPage(ExtensionDetailInfo info, IReadOnlyList<ExtensionDetailPage> pages, ContentControl host, ScrollViewer scroll, int index)
    {
        if (index < 0 || index >= pages.Count)
            return;

        mSelectedPage = index;
        for (int i = 0; i < mTabs.Count; i++)
        {
            var (text, accent) = mTabs[i];
            bool selected = i == index;
            // 选中态只用颜色 + 底部高亮条，【不加粗】——加粗会改变文字宽度，切 tab 时整条标签会跟着抖。
            text.Foreground = selected ? Style.TEXT_LIGHT.ToBrush() : Style.LIGHT_WHITE.Opacity(0.6).ToBrush();
            accent.Background = selected ? Style.HIGH_LIGHT.ToBrush() : Brushes.Transparent;
        }

        // 惰性构建 + 缓存：introduction 里可能有大图，没切到的页不渲染；切回来时不重建（保留已加载的图）。
        if (!mPageContents.TryGetValue(index, out var content))
        {
            content = BuildPageContent(info, pages[index]);
            mPageContents[index] = content;
        }
        host.Content = content;
        scroll.Offset = new Vector(0, 0);

        SyncPageActions(pages[index]);
    }

    // tab 条右端的【当前条目】操作：本条目的启停开关 + 设置齿轮（该能力位声明了 IExtensionSettings 才有）
    // + 打开本页的 introduction 文件（该页有文件才有）。三者都随 tab 切换重建。
    void SyncPageActions(ExtensionDetailPage page)
    {
        if (mPageActions == null)
            return;

        mCurrentPage = page;
        mPageActions.Children.Clear();

        // 条目级启停：一包多能力时只关坏的那个（如 suite 包里 effect 崩了、voice 还想留着）。
        if (page.CanDisable && !string.IsNullOrEmpty(mPackageId))
        {
            bool packageOff = ExtensionActivation.IsPackageDisabled(mPackageId);
            var sw = CreateActivationSwitch(
                !packageOff && !ExtensionActivation.IsEntryDisabledSelf(mPackageId, page.EntryKind, page.Identities),
                packageOff
                    // 整包已关：条目开关不该假装可用。锁死并说明原因——比让它可拨、拨完却毫无效果诚实。
                    ? "The whole extension is disabled, so its capabilities cannot be turned on individually.".Tr(TC.Dialog)
                    : "Enable or disable this capability. Takes effect after restarting TuneLab.".Tr(TC.Dialog));
            if (packageOff)
            {
                sw.AllowSwitch += () => false;
            }
            else
            {
                sw.ValueCommitted.Subscribe(() =>
                {
                    ExtensionActivation.SetEntryEnabled(mPackageId, page.EntryKind, page.Identities, sw.Value);
                    ActivationChanged?.Invoke();
                });
            }
            mPageActions.Children.Add(sw);
        }

        if (!string.IsNullOrEmpty(page.SettingsKey))
        {
            var key = page.SettingsKey;
            mPageActions.Children.Add(TextButton("Settings".Tr(TC.Dialog), Assets.Settings, () => SettingsRequested?.Invoke(key)));
        }

        if (!string.IsNullOrEmpty(page.FilePath))
        {
            var path = page.FilePath;
            mPageActions.Children.Add(TextButton("Open in External Editor".Tr(TC.Dialog), null, () => ProcessHelper.OpenFile(path)));
        }
    }

    // 一页正文：format 条目先列它认的文件后缀，其下 introduction 正文（无则「无文档」占位）。
    // 操作按钮（设置齿轮 / 打开 introduction 文件）不在这里，而在 tab 条右端——它们随 tab 变，
    // 摆进正文会独占一行且左半边空着。
    // 页内也没有"一句话摘要"：作者只写 introduction 全文，AI 要的摘要由它自己从全文提炼——
    // 既然不要求作者写，UI 也就无从展示；用户要了解详情，正文就是全文。
    Control BuildPageContent(ExtensionDetailInfo info, ExtensionDetailPage page)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };

        // format 条目【恒列】它认的后缀（一个也列）：显示名往往看不出对应什么文件，而"能打开哪些文件"
        // 正是用户对一个格式最需要知道的事。engine 类不列——那身份是内部注册键，对使用无意义。
        if (page.IdentitiesAreFileSuffixes && page.Identities.Count > 0)
            stack.Children.Add(new TextBlock
            {
                Text = string.Join("  ", page.Identities.Select(i => "." + i)),
                FontSize = 11,
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
            });

        if (!string.IsNullOrEmpty(page.Markdown))
            stack.Children.Add(ChatMarkdownRenderer.Render(page.Markdown, info.PackageDir));
        else
            stack.Children.Add(new TextBlock
            {
                Text = "This extension has no documentation.".Tr(TC.Dialog),
                FontSize = 12,
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0),
            });

        // 内容内边距放在【内容自身】的 Margin 上（左右对称），而非 ScrollViewer.Padding——后者实测会让内容宽度
        // 算漏右侧内边距、导致右边界溢出/左右不对称。ScrollViewer 不再加 Padding。
        stack.Margin = new Thickness(24, 18);
        return stack;
    }

    OverlayScrollBars? mScrollBars;
    Border? mUninstallBtn;
    TextBlock? mUninstallText;
    bool mPendingUninstall;
    StackPanel? mPageActions;   // tab 条右端：当前条目的启停开关 + 设置齿轮 + 打开其 introduction 文件
    ExtensionDetailPage? mCurrentPage;   // 当前页（包级开关拨动后要据此重建条目操作区）
    readonly string? mPackageId;         // 启停键的前半；为空则本窗不提供启停开关
    Switch? mPackageSwitch;
    TextBlock? mPackageStateText;
    TextBlock? mRestartHint;
    SmoothWheelScroller? mTabWheel;      // tab 条的平滑横向滚轮（宿主持有它的事件处理器，此处只为可读）
    readonly List<(TextBlock Text, Border Accent)> mTabs = [];
    readonly Dictionary<int, Control> mPageContents = [];
    int mSelectedPage = -1;
}
