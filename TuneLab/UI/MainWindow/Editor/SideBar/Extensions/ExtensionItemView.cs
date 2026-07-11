using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Svg.Skia;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TuneLab.Extensions;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;

namespace TuneLab.UI;

internal class ExtensionItemView : Border
{
    public event Action? UninstallRequested;
    public event Action? CancelUninstallRequested;
    // 点整张卡片（避开卸载按钮/菜单等已 Handled 的热区）打开详情窗。
    public event Action? OpenDetailRequested;
    // 点卡片上的齿轮（无文字）→ 打开设置窗对应插件位置。仅在插件声明了扩展设置时显示。
    public event Action? OpenSettingsRequested;
    public string ExtensionName { get; }
    public string ExtensionVersion { get; }
    public string ExtensionType { get; }
    public string ExtensionPath { get; }
    public bool IsPendingUninstall { get; private set; }

    public ExtensionItemView(string name, string version, IReadOnlyList<string> types, string author, string description, string? iconPath, string extensionPath, ExtensionLoadStatus status, string? error, bool hasSettings)
    {
        ExtensionName = name;
        ExtensionVersion = version;
        ExtensionType = string.Join(", ", types);   // 搜索过滤用的合并串；展示时每种 type 各自一枚徽标
        ExtensionPath = extensionPath;

        // Skipped / Failed 下不展示类别徽标——加载失败的包没有"生效的类别"可言，只保留状态徽标。
        bool showTypeBadge = status is ExtensionLoadStatus.Loaded or ExtensionLoadStatus.PartiallyLoaded;

        Background = Style.INTERFACE.ToBrush();
        Padding = new Thickness(12, 10);
        BorderBrush = Style.BACK.ToBrush();
        BorderThickness = new Thickness(0, 0, 0, 1);
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        ClipToBounds = true;

        var mainPanel = new DockPanel();

        // 左侧图标区（64×64）。带图标的包：不画任何打底背景/圆角，直接原样摆放图标
        // （与 VSCode 一致——图标的形状/圆角/透明完全交给作者，宿主不叠加遮罩，避免双重圆角不协调）。
        // 无图标的包：退回深色圆角方块 + 名称首字母占位。
        var iconVisual = CreateIconVisual(iconPath, name, 64.0);
        iconVisual.Margin = new Thickness(0, 0, 12, 0);
        iconVisual.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        mainPanel.AddDock(iconVisual, Dock.Left);

        // Right side: info + action area
        var rightPanel = new DockPanel
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };

        // 作者行（小字，过长截断）+ 右侧设置齿轮（无文字，仅在插件声明了扩展设置时）同一行：作者左、齿轮右。
        // 有作者或有设置任一即建此行；位于版本(上)与卸载(下)之间。
        // 用 DockPanel（齿轮 Dock.Right + 图标 Dock.Left + 文字填充）：填充的文字受限于剩余宽度，省略号才生效。
        Control? authorRow = null;
        if (!string.IsNullOrWhiteSpace(author) || hasSettings)
        {
            const double authorIconSize = 12;   // 配 11px 作者小字的视觉尺寸
            // 行高不固定：由最高子项（齿轮 16）决定，图标/文字在其中居中；无齿轮时回到小字自然高。
            var authorDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

            // 齿轮先 Dock.Right（图标化、透明底白色，hover 变亮——仿编辑器底部设置按钮）。
            if (hasSettings)
            {
                // IconItem 按「图标原生尺寸(24) × Scale」居中绘制；Scale=16/24 → 图标 16px，与 16px 按钮同大。
                var gear = new TuneLab.GUI.Components.Button { Width = 16, Height = 16, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                    .AddContent(new() { Item = new IconItem() { Icon = Assets.Settings, Scale = 16.0 / 24.0 }, ColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5), HoveredColor = Colors.White, PressedColor = Colors.White } });
                gear.Clicked += () => OpenSettingsRequested?.Invoke();
                // 吞掉齿轮上的 Tapped，避免点齿轮时冒泡触发卡片开详情。
                gear.AddHandler(Gestures.TappedEvent, (_, e) => e.Handled = true);
                authorDock.AddDock(gear, Dock.Right);
            }

            if (!string.IsNullOrWhiteSpace(author))
            {
                authorDock.AddDock(new Image
                {
                    Source = Assets.Author.GetImage(Style.LIGHT_WHITE.Opacity(0.6)),
                    Width = authorIconSize,
                    Height = authorIconSize,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                }, Dock.Left);
                // 文字最后加 = 填充中间（LastChildFill），受限剩余宽 → 省略号生效。
                authorDock.AddDock(new TextBlock
                {
                    Text = author,
                    FontSize = 11,
                    Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                });
            }
            authorRow = authorDock;
        }

        // 底行：类别徽标（每种 type 各一枚）+（非 Loaded 时）状态徽标（左）+ 卸载按钮（右）。
        var bottomRow = new DockPanel();
        {
            // 卸载按钮固定在右下角。
            mUninstallBtnText = new TextBlock
            {
                Text = "Uninstall".Tr(TC.Dialog),
                FontSize = 11,
                Foreground = Style.LIGHT_WHITE.ToBrush(),
            };
            mUninstallBtn = new Border
            {
                Background = Style.BUTTON_NORMAL.ToBrush(),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2),
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = mUninstallBtnText,
            };
            mUninstallBtn.PointerEntered += (s, e) => { if (!IsPendingUninstall) mUninstallBtn.Background = Style.BUTTON_NORMAL_HOVER.ToBrush(); };
            mUninstallBtn.PointerExited += (s, e) => { if (!IsPendingUninstall) mUninstallBtn.Background = Style.BUTTON_NORMAL.ToBrush(); };
            mUninstallBtn.PointerPressed += (s, e) =>
            {
                e.Handled = true;
                if (IsPendingUninstall)
                {
                    // 已标记待卸载：点击弹菜单给"取消卸载"反悔入口（防误点）。
                    var menu = new ContextMenu();
                    menu.Items.Add(new MenuItem().SetName("Cancel Uninstall".Tr(TC.Dialog)).SetAction(() => CancelUninstallRequested?.Invoke()));
                    mUninstallBtn.OpenContextMenu(menu);
                }
                else
                {
                    UninstallRequested?.Invoke();
                }
            };
            bottomRow.AddDock(mUninstallBtn, Dock.Right);
            // 吞掉卸载按钮上的 Tapped：卡片开详情走 Gestures.Tapped（与 PointerPressed 是两个事件，
            // 后者 Handled 拦不住前者冒泡），故在按钮处 handle 掉 Tapped，避免点卸载/齿轮时又弹详情。
            mUninstallBtn.AddHandler(Gestures.TappedEvent, (_, e) => e.Handled = true);

            // 类别徽标：每种 type 单独一枚（而非逗号拼进一枚）。
            // Skipped/Failed 不渲染类别、只渲染状态徽标（showTypeBadge 已据此判定）。
            var tagPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            };
            bool firstTag = true;
            if (showTypeBadge)
            {
                foreach (var t in types)
                {
                    tagPanel.Children.Add(new Border
                    {
                        Background = Style.BACK.ToBrush(),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 2),
                        Margin = firstTag ? new Thickness(0) : new Thickness(4, 0, 0, 0),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = t,
                            FontSize = 11,
                            Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(),
                        }
                    });
                    firstTag = false;
                }
            }
            if (status != ExtensionLoadStatus.Loaded)
            {
                var (statusText, statusColor) = StatusBadge(status);
                var statusBadge = new Border
                {
                    Background = new SolidColorBrush(statusColor, 0.18),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 2),
                    Margin = firstTag ? new Thickness(0) : new Thickness(6, 0, 0, 0),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Child = new TextBlock { Text = statusText, FontSize = 11, Foreground = new SolidColorBrush(statusColor) },
                };
                if (!string.IsNullOrEmpty(error))
                    ToolTip.SetTip(statusBadge, error);
                tagPanel.Children.Add(statusBadge);
            }
            bottomRow.AddDock(tagPanel);
        }

        // 作者 + 底行编成一组，整体锚在卡片底部；空隙落在大字名称与本组之间。
        var bottomGroup = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
        };
        if (authorRow != null)
            bottomGroup.Children.Add(authorRow);
        bottomGroup.Children.Add(bottomRow);
        rightPanel.AddDock(bottomGroup, Dock.Bottom);

        // 第 1 行：名称（左，过长省略号）+ 版本徽标（右），锚在顶部。
        var nameRow = new Grid { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        nameRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        {
            var nameBlock = new TextBlock
            {
                Text = name,
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                Foreground = Style.TEXT_LIGHT.ToBrush(),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(nameBlock, 0);
            nameRow.Children.Add(nameBlock);

            var versionBadge = new Border
            {
                Background = Style.BACK.ToBrush(),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 2),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "v" + version,
                    FontSize = 11,
                    Foreground = Style.LIGHT_WHITE.Opacity(0.7).ToBrush(),
                }
            };
            Grid.SetColumn(versionBadge, 1);
            nameRow.Children.Add(versionBadge);
        }
        rightPanel.AddDock(nameRow);
        mainPanel.AddDock(rightPanel);
        Child = mainPanel;

        // 整张卡片的悬浮 tooltip：给出卡片上省略/未展示的完整信息（全名 + 版本 + 作者 + 简介）。
        ToolTip.SetTip(this, BuildTooltip(name, version, author, description));

        // 整卡可点打开详情：悬浮微亮 + 手型提示。卸载/齿轮等操作按钮各自 handle 掉 Tapped（见其定义处），
        // 故点它们不会冒泡成本卡的 Tapped 而误开详情——热区分离。
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerEntered += (_, _) => Background = Style.LIGHT_WHITE.Opacity(0.04).ToBrush();
        PointerExited += (_, _) => Background = Style.INTERFACE.ToBrush();
        AddHandler(Gestures.TappedEvent, (_, _) => OpenDetailRequested?.Invoke());
    }

    // 卡片 hover tooltip 文案：完整名称、版本、作者、简介（缺省项跳过）。
    private static string BuildTooltip(string name, string version, string author, string? description)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(name).Append("\nv").Append(version);
        if (!string.IsNullOrWhiteSpace(author))
            sb.Append(" · ").Append(author);
        if (!string.IsNullOrWhiteSpace(description))
            sb.Append("\n\n").Append(description);
        return sb.ToString();
    }

    // 扩展图标视觉：有可解码图标 → 原样 Image（不叠打底/圆角，形状交给作者）；否则深色圆角方块 + 名称首字母占位。
    // 卡片与详情窗共用（size 不同）；外边距/对齐由调用方按各自布局设置。
    internal static Control CreateIconVisual(string? iconPath, string name, double size)
    {
        var iconImage = TryCreateIconImage(iconPath, size);
        if (iconImage != null)
            return iconImage;

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 8),
            Background = Style.DARK.ToBrush(),
            ClipToBounds = true,
            Child = new TextBlock
            {
                Text = GetIconText(name),
                FontSize = GetIconFontSize(name) * (size / 64.0),
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            }
        };
    }

    // 从包内图标文件构建可渲染的 Image：.svg 走矢量、其余按位图解码。
    // 路径为空 / 文件缺失 / 解码失败 → 返回 null，由调用方退回首字母占位。
    private static Control? TryCreateIconImage(string? iconPath, double size)
    {
        if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
            return null;

        try
        {
            IImage source;
            if (Path.GetExtension(iconPath).Equals(".svg", StringComparison.OrdinalIgnoreCase))
            {
                source = new SvgImage { Source = SvgSource.LoadFromSvg(File.ReadAllText(iconPath)) };
            }
            else
            {
                using var stream = File.OpenRead(iconPath);
                source = new Bitmap(stream);
            }

            return new Image
            {
                // Uniform：完整显示整张图标、不裁切（无打底背景，非方形时多余处透明）。
                Source = source,
                Stretch = Stretch.Uniform,
                Width = size,
                Height = size,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetIconText(string name)
    {
        if (name.Length <= 5)
            return name;

        var words = name.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2)
            return (words[0][..1] + words[1][..1]).ToUpperInvariant();

        return name[..1].ToUpperInvariant();
    }

    private static double GetIconFontSize(string name)
    {
        var text = GetIconText(name);
        if (text.Length <= 1) return 28;
        if (text.Length <= 2) return 22;
        if (text.Length <= 3) return 18;
        if (text.Length <= 4) return 15;
        return 13;
    }

    private static (string, Color) StatusBadge(ExtensionLoadStatus status) => status switch
    {
        ExtensionLoadStatus.Failed => ("Failed".Tr(TC.Dialog), Color.Parse("#E5737C")),
        ExtensionLoadStatus.Skipped => ("Skipped".Tr(TC.Dialog), Color.Parse("#E5C573")),
        ExtensionLoadStatus.PartiallyLoaded => ("Partial".Tr(TC.Dialog), Color.Parse("#E5A573")),
        _ => (string.Empty, Colors.Transparent),
    };

    public void MarkPendingUninstall()
    {
        if (IsPendingUninstall)
            return;

        IsPendingUninstall = true;
        mUninstallBtn.Background = Style.BACK.ToBrush();
        mUninstallBtn.Cursor = new Cursor(StandardCursorType.Hand);
        mUninstallBtnText.Text = "Pending Uninstall".Tr(TC.Dialog);
        mUninstallBtnText.Foreground = Style.LIGHT_WHITE.Opacity(0.4).ToBrush();
    }

    // 撤销待卸载，恢复成可卸载状态。
    public void UnmarkPendingUninstall()
    {
        if (!IsPendingUninstall)
            return;

        IsPendingUninstall = false;
        mUninstallBtn.Background = Style.BUTTON_NORMAL.ToBrush();
        mUninstallBtn.Cursor = new Cursor(StandardCursorType.Hand);
        mUninstallBtnText.Text = "Uninstall".Tr(TC.Dialog);
        mUninstallBtnText.Foreground = Style.LIGHT_WHITE.ToBrush();
    }

    readonly Border mUninstallBtn;
    readonly TextBlock mUninstallBtnText;
}
