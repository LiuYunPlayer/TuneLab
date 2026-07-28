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
    // 卡片上没有设置入口——设置是 per 能力位的，由详情窗各 tab 自己承载（见构造函数里作者行的注释）。
    public event Action? OpenDetailRequested;
    public string ExtensionName { get; }
    public string ExtensionVersion { get; }
    public string ExtensionType { get; }
    public string ExtensionPath { get; }
    public bool IsPendingUninstall { get; private set; }

    public ExtensionItemView(string name, string version, IReadOnlyList<string> types, string author, string description, string? iconPath, string extensionPath, ExtensionLoadStatus status, string? error)
    {
        ExtensionName = name;
        ExtensionVersion = version;
        ExtensionType = string.Join(", ", types);   // 搜索过滤用的合并串；展示时每种 type 各自一枚徽标
        ExtensionPath = extensionPath;

        // Skipped / Failed 下不展示类别徽标——加载失败的包没有"生效的类别"可言，只保留状态徽标。
        // Disabled 例外：它不是故障，那个包依然"是个 voice 插件"，只是用户把它关了——徽标照留才认得出。
        bool showTypeBadge = status is ExtensionLoadStatus.Loaded or ExtensionLoadStatus.PartiallyLoaded or ExtensionLoadStatus.Disabled;

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

        // 作者行（小字，过长截断）。
        // 【卡片上不放启停开关】：卡片只有 100px 上下的高度，右栏已有版本徽标与卸载键——再塞一个开关，
        // 挨着卸载是"两个同尺寸小控件并排、邀请误点"，摆到中间又让右栏变成三层堆叠，两种都挤。
        // 启停是低频操作，开关收进详情窗（header 的包级 + 各 tab 的条目级）即可；卡片只**如实展示状态**：
        // 运行态由状态徽标承载（含「已禁用」），存的选择与运行态不一致时另亮「需重启」。
        // 【卡片上也没有设置齿轮】：扩展设置是 per 能力位的（一个包里两个引擎各存一份），而卡片是包级视图——
        // 包内多个条目都有设置时，一个齿轮只能跳到"首个"，等于拿包级控件冒充某个具体能力的入口。
        // 设置入口同样由详情窗各 tab 自己承载（准确对应那一个能力位）；要总览全部则走设置窗「扩展」页。
        // 用 DockPanel（图标 Dock.Left + 文字填充）：填充的文字受限于剩余宽度，省略号才生效。
        Control? authorRow = null;
        if (!string.IsNullOrWhiteSpace(author))
        {
            const double authorIconSize = 12;   // 配 11px 作者小字的视觉尺寸
            var authorDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

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
            authorRow = authorDock;
        }

        // 底行：类别徽标（每种 type 各一枚）+（非 Loaded 时）状态徽标 +（启停选择与本次运行不符时）需重启徽标（左）
        //      + 卸载按钮（右）。启停开关不在这一行——理由见上面中间行的注释。
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

            // 类别徽标：每种 type 单独一枚（而非逗号拼进一枚）。渲染与否见 showTypeBadge 处的判定与理由。
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
                firstTag = false;
            }
            // 「需重启」徽标：存下来的启停选择与本次运行的实际状态不一致时亮起。缺了它，用户拨完开关
            // 会以为已经生效——已注册的能力撤不回、程序集也卸不掉，唯一诚实的做法是明说要重启。
            mRestartBadge = new Border
            {
                Background = new SolidColorBrush(RestartHintColor, 0.18),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2),
                Margin = firstTag ? new Thickness(0) : new Thickness(6, 0, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                IsVisible = false,
                Child = new TextBlock { Text = "Restart Required".Tr(TC.Dialog), FontSize = 11, Foreground = new SolidColorBrush(RestartHintColor) },
            };
            tagPanel.Children.Add(mRestartBadge);
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
        // 中性灰：Disabled 是用户自己的选择，不是告警——不与故障/跳过共用暖色。
        ExtensionLoadStatus.Disabled => ("Disabled".Tr(TC.Dialog), Color.Parse("#9AA0A6")),
        _ => (string.Empty, Colors.Transparent),
    };

    private static readonly Color RestartHintColor = Color.Parse("#73A9E5");

    // 亮/灭「需重启」徽标（由 provider 比对"存下来的选择"与"本次运行的实际状态"后调用）。
    public void SetRestartRequired(bool required)
    {
        if (mRestartBadge != null)
            mRestartBadge.IsVisible = required;
    }

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
    readonly Border? mRestartBadge;   // 「需重启」徽标（存的启停选择 ≠ 本次运行状态时可见）
}
