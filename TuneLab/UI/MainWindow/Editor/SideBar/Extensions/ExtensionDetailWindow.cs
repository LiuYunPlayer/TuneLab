using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;

namespace TuneLab.UI;

// 一个扩展详情的展示数据：全部来自包级元数据（manifest）+ 解析到的 README 正文。
internal sealed class ExtensionDetailInfo
{
    public required string Name;
    public required string Version;
    public string Author = string.Empty;
    public string Summary = string.Empty;          // manifest 的 description（一句话简介）
    public string? IconPath;
    public IReadOnlyList<string> Types = [];
    public required string PackageDir;             // README 相对图片解析的 baseDir
    public string? ReadmeMarkdown;                 // 无 README 时为空 → 显占位
    public string? ReadmePath;                     // README 文件绝对路径（供「用外部编辑器打开」）；无则不显该按钮
    public bool HasSettings;                       // 该插件是否声明了扩展设置（决定是否显示齿轮）
    public bool IsPendingUninstall;                // 打开时该插件是否已处于待卸载态（决定卸载按钮初始态）
}

// 扩展详情窗：点侧栏条目弹出，渲染包级 README（正文完全由作者定义、宿主不解释）。
// 独立【可缩放】窗口（区别于固定尺寸的 Dialog）——长富文本详情需要足够阅读宽度与自由缩放。
// 无边框 + 扩展客户区（沿用 Dialog 的自绘外观），顶栏自绘（拖动 + 关闭），正文区滚动。
internal sealed class ExtensionDetailWindow : Window
{
    // 齿轮 → 跳设置窗对应插件；Uninstall/CancelUninstall → 卸载/撤销。由 provider 接到宿主既有流程并跨视图同步态。
    public event Action? SettingsRequested;
    public event Action? UninstallRequested;
    public event Action? CancelUninstallRequested;

    public ExtensionDetailWindow(ExtensionDetailInfo info)
    {
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

        var closeText = new TextBlock
        {
            Text = "✕", // ✕
            FontSize = 13,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var close = new Border
        {
            Width = 40,
            Height = 40,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Cursor = new Cursor(StandardCursorType.Hand),
            Background = Brushes.Transparent,
            Child = closeText,
        };
        close.PointerEntered += (_, _) => { close.Background = Style.LIGHT_WHITE.Opacity(0.1).ToBrush(); closeText.Foreground = Colors.White.ToBrush(); };
        close.PointerExited += (_, _) => { close.Background = Brushes.Transparent; closeText.Foreground = Style.LIGHT_WHITE.ToBrush(); };
        close.PointerPressed += (_, e) => { e.Handled = true; Close(); };
        bar.Children.Add(close);

        // 顶栏空白处拖动移窗（关闭按钮已 Handled，不触发拖动）。
        bar.PointerPressed += (_, e) =>
        {
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

        if (!string.IsNullOrWhiteSpace(info.Summary))
            info_.Children.Add(new TextBlock { Text = info.Summary, FontSize = 12, Foreground = Style.LIGHT_WHITE.ToBrush(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });

        if (info.Types.Count > 0)
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

    // 方形图标：边长动态取信息列实测高度（「刚好与四排对齐」）。图标自身不驱动 header 高度（初始给个小尺寸，
    // 布局后按 info 高度回设），避免大图标撑爆 header。
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
        // 信息列高度确定后，把图标设成同高的正方形。
        infoColumn.PropertyChanged += (_, e) =>
        {
            if (e.Property != Visual.BoundsProperty)
                return;
            var h = infoColumn.Bounds.Height;
            if (h > 0 && (double.IsNaN(host.Height) || System.Math.Abs(host.Height - h) > 0.5))
            {
                host.Height = h;
                host.Width = h;
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

        // 顶：用系统对 .md 的默认程序打开（用户配的编辑器），带文本编辑器回退。仅有 README 时显示。
        if (!string.IsNullOrEmpty(info.ReadmePath))
        {
            var path = info.ReadmePath;
            var open = TextButton("Open in External Editor".Tr(TC.Dialog), null, () => ProcessHelper.OpenFile(path));
            open.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            col.AddDock(open, Dock.Top);
        }

        // 底：设置（齿轮，有设置时）+ 卸载 一排。
        var bottomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
        };
        if (info.HasSettings)
            bottomRow.Children.Add(TextButton("Settings".Tr(TC.Dialog), Assets.Settings, () => SettingsRequested?.Invoke()));
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

    // 正文：README（相对图片按包目录解析）或「无文档」占位。
    // 横向禁滚（内容按视口宽换行、根治右边界溢出）；纵向隐藏原生条、改挂 app 自制浮层滚动条 OverlayScrollBars。
    Control BuildBody(ExtensionDetailInfo info)
    {
        Control content;
        if (!string.IsNullOrEmpty(info.ReadmeMarkdown))
            content = ChatMarkdownRenderer.Render(info.ReadmeMarkdown, info.PackageDir);
        else
            content = new TextBlock
            {
                Text = "This extension has no documentation.".Tr(TC.Dialog),
                FontSize = 12,
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0),
            };

        // 内容内边距放在【内容自身】的 Margin 上（左右对称），而非 ScrollViewer.Padding——后者实测会让内容宽度
        // 算漏右侧内边距、导致右边界溢出/左右不对称。ScrollViewer 不再加 Padding。
        content.Margin = new Thickness(24, 18);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Style.INTERFACE.ToBrush(),
            Content = content,
        };

        // 自制浮层竖向滚动条（原生已 Hidden）。存字段防 GC；attach 在 host 入可视树时自动完成。
        mScrollBars = new OverlayScrollBars(scroll, horizontal: false, vertical: true);
        return scroll;
    }

    OverlayScrollBars? mScrollBars;
    Border? mUninstallBtn;
    TextBlock? mUninstallText;
    bool mPendingUninstall;
}
