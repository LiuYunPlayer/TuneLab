using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.Configs;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;
using TuneLab.SDK;
using TuneLab.GUI.Controllers;
using Avalonia.Platform.Storage;
using TuneLab.Audio;
using TuneLab.Extensions;
using ComboBoxItem = TuneLab.SDK.ComboBoxItem;   // 消歧：避开 Avalonia.Controls.ComboBoxItem

namespace TuneLab.UI;

internal partial class SettingsWindow : Window
{
    // 全局单窗：设置改动即时写进 Settings 单例、关窗才落盘，扩展页的编辑更是切 tab / 关窗时统一收口。
    // 多开会让两份实例各持一份中途状态、互相覆盖对方的写入，故一律经此入口开窗——已开则置前。
    public static void Open(Window? owner, string? focusExtensionPackageId = null, string? focusExtensionKey = null)
    {
        if (sInstance is { } opened)
        {
            // 已开着又点了某个能力位的齿轮：不新建，改让现有窗重新定位过去。
            if (!string.IsNullOrEmpty(focusExtensionPackageId))
                opened.FocusExtension(focusExtensionPackageId, focusExtensionKey);

            opened.Activate();
            return;
        }

        var window = new SettingsWindow(focusExtensionPackageId, focusExtensionKey);
        sInstance = window;
        window.Closed += (_, _) => { if (ReferenceEquals(sInstance, window)) sInstance = null; };
        if (owner != null)
            window.Show(owner);
        else
            window.Show();
    }

    public SettingsWindow() : this(null) { }

    // focusExtensionPackageId：非空且该包声明了扩展设置时，开窗即切到「扩展」tab 并滚动到该插件区（详情窗齿轮用）。
    // focusExtensionKey：可选，进一步定到【具体能力位】（形如 "voice:MyEngine"，即 Entry.ExtensionKey）——
    //   设置是 per 能力实现者的，一个包可有多个各存一份；详情窗齿轮已按 tab 归属某个能力，故要能精确落到它。
    //   省略则退回"该包首个有设置的条目"（旧行为）。
    public SettingsWindow(string? focusExtensionPackageId, string? focusExtensionKey = null)
    {
        mFocusExtensionPackageId = focusExtensionPackageId;
        mFocusExtensionKey = focusExtensionKey;
        InitializeComponent();
        Focusable = true;
        CanResize = false;
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        TitleLabel.Content = "Settings".Tr(this);

        this.Background = Style.BACK.ToBrush();
        TitleLabel.Foreground = Style.TEXT_LIGHT.ToBrush();

        var closeButton = new GUI.Components.Button() { Width = 48, Height = 40 }
                .AddContent(new() { Item = new BorderItem() { CornerRadius = 0 }, ColorSet = new() { HoveredColor = Colors.White.Opacity(0.2), PressedColor = Colors.White.Opacity(0.2) } })
                .AddContent(new() { Item = new IconItem() { Icon = Assets.WindowClose }, ColorSet = new() { Color = Style.TEXT_LIGHT.Opacity(0.7) } });
        closeButton.Clicked += () =>
        {
            SaveExtensionSettings();
            Settings.Save(PathManager.SettingsFilePath);
            s.DisposeAll();
            Close();
        };

        WindowControl.Children.Add(closeButton);

        var titleBar = this.FindControl<Grid>("TitleBar") ?? throw new InvalidOperationException("TitleBar not found");
        bool UseSystemTitle = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
        if (UseSystemTitle)
        {
            titleBar.Height = 0;
        }

        // Resolve named controls
        mSidebarBorder = this.FindControl<Border>("SidebarBorder") ?? throw new InvalidOperationException("SidebarBorder not found");
        mContentBorder = this.FindControl<Border>("ContentBorder") ?? throw new InvalidOperationException("ContentBorder not found");
        mSidebarPanel = this.FindControl<StackPanel>("SidebarPanel") ?? throw new InvalidOperationException("SidebarPanel not found");
        mContentPanel = this.FindControl<DockPanel>("ContentPanel") ?? throw new InvalidOperationException("ContentPanel not found");

        // Setup sidebar and content area styling
        mSidebarBorder.Background = Style.BACK.ToBrush();
        mContentBorder.Background = Style.INTERFACE.ToBrush();

        Settings.Language.Modified.Subscribe(async () => await this.ShowMessage("Tips".Tr(TC.Dialog), "Please restart to apply settings.".Tr(this)), s);
        AppFont.Bind(this);   // 设置窗自身也实时反映所选界面字体（改后即时可见，无需重启）

        // Create tab pages
        var tabPages = new List<TabPageInfo>
        {
            new("General", Assets.General, () => CreateTabPage(SettingTab.General)),
            new("Audio", Assets.Audio, () => CreateTabPage(SettingTab.Audio)),
            new("Appearance", Assets.Appearance, () => CreateTabPage(SettingTab.Appearance)),
            new("Editing", Assets.Editing, () => CreateTabPage(SettingTab.Editing)),
            new("Keybindings", Assets.Keyboard, () => new KeymapSettingsPage(this)),
            new("Extensions", Assets.Extensions, CreateExtensionsPage),
            new("Extension Routing", Assets.ExtensionRouting, CreateRoutingPage),
        };

        // Build sidebar tab buttons
        foreach (var tabPage in tabPages)
        {
            var tabButton = CreateTabButton(tabPage);
            mSidebarPanel.Children.Add(tabButton);
            mTabButtons.Add(tabButton);
            mTabPages.Add(tabPage);
        }

        // 默认选首个 tab；但若请求了定位某插件设置且该插件确有设置，则直接切到「扩展」tab 并滚动到它。
        int initialTab = 0;
        if (!string.IsNullOrEmpty(mFocusExtensionPackageId)
            && ExtensionSettingsManager.GetEntries().Any(e => e.PackageId == mFocusExtensionPackageId))
        {
            var idx = mTabPages.FindIndex(p => p.Name == "Extensions");
            if (idx >= 0)
                initialTab = idx;
        }
        if (mTabPages.Count > 0)
            SelectTab(initialTab);

        // 「扩展」页构建时已捕获目标插件的标题控件；布局完成后把它滚到可视区顶部（尽力而为，失败不影响切页）。
        if (mFocusListView != null && mFocusEntryControl != null)
            Avalonia.Threading.Dispatcher.UIThread.Post(ScrollFocusIntoView, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    // 已开着的窗改定位到另一个能力位（详情窗齿轮再次触发）：tab 内容在每次 SelectTab 时整体重建，
    // 故只需换掉目标、清掉上一轮的捕获，再切一次「扩展」页——捕获与滚动沿用构造时那条路径。
    private void FocusExtension(string packageId, string? extensionKey)
    {
        mFocusExtensionPackageId = packageId;
        mFocusExtensionKey = extensionKey;
        mFocusListView = null;
        mFocusEntryControl = null;

        int index = mTabPages.FindIndex(p => p.Name == "Extensions");
        if (index < 0)
            return;

        SelectTab(index);
        if (mFocusListView != null && mFocusEntryControl != null)
            Avalonia.Threading.Dispatcher.UIThread.Post(ScrollFocusIntoView, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    // 把捕获到的目标插件设置区滚动到「扩展」页顶部附近。用自制 ScrollView 的竖轴（BringIntoView 对它不生效）。
    private void ScrollFocusIntoView()
    {
        try
        {
            if (mFocusListView == null || mFocusEntryControl == null)
                return;
            // 目标控件相对内容面板的 Y = 需要的滚动量（内容初始 offset 0，Bounds.Y 即距顶距离）。留 12px 上边距。
            var y = mFocusEntryControl.Bounds.Y;
            if (y > 0)
                mFocusListView.VerticalAxis.ViewOffset = System.Math.Max(0, y - 12);
        }
        catch { }
    }

    private Border CreateTabButton(TabPageInfo tabPage)
    {
        // Outer border for the tab item
        var outerBorder = new Border
        {
            Height = 48,
            Margin = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        // Left accent bar
        var accentBar = new Border
        {
            Width = 3,
            Height = 24,
            CornerRadius = new CornerRadius(1.5),
            Background = Brushes.Transparent,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        };

        // Icon + text panel
        var contentPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
        };

        var iconImage = new Avalonia.Controls.Image
        {
            Width = 20,
            Height = 20,
            Source = tabPage.Icon.GetImage(Style.TEXT_LIGHT),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var label = new TextBlock
        {
            Text = tabPage.Name.Tr(this),
            FontSize = 14,
            Foreground = Style.TEXT_LIGHT.ToBrush(),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };

        contentPanel.Children.Add(iconImage);
        contentPanel.Children.Add(label);

        var grid = new Grid();
        grid.Children.Add(accentBar);
        grid.Children.Add(contentPanel);

        outerBorder.Child = grid;
        outerBorder.Tag = tabPage;

        // Store the accent bar for later highlight toggling
        tabPage.AccentBar = accentBar;
        tabPage.TabBorder = outerBorder;

        outerBorder.PointerPressed += (sender, e) =>
        {
            var index = mTabPages.IndexOf(tabPage);
            if (index >= 0)
            {
                SelectTab(index);
            }
        };

        return outerBorder;
    }

    private void SelectTab(int index)
    {
        if (index < 0 || index >= mTabPages.Count)
            return;

        // 切走前把"扩展"页的编辑统一落盘——内容在每次切 tab 时整体重建，不先存会丢失未存改动
        //（与关窗/Esc 的统一落盘一致）。非扩展页时 mExtensionPages 为空、无副作用。
        SaveExtensionSettings();

        // Update visual state for all tabs
        for (int i = 0; i < mTabPages.Count; i++)
        {
            var page = mTabPages[i];
            bool isSelected = (i == index);

            if (page.AccentBar != null)
            {
                page.AccentBar.Background = isSelected
                    ? Style.HIGH_LIGHT.ToBrush()
                    : Brushes.Transparent;
            }

            if (page.TabBorder != null)
            {
                page.TabBorder.Background = isSelected
                    ? Colors.White.Opacity(0.06).ToBrush()
                    : Brushes.Transparent;
            }
        }

        // Replace the content
        mContentPanel.Children.Clear();
        var content = mTabPages[index].CreateContent();
        mContentPanel.AddDock(content);

        mSelectedIndex = index;
    }

    // 把一页的列表包成「可滚列表 + 贴边滚动条」。设置窗每一页都该经它返回。
    //
    // ① Background 设透明是**必需**的，不是装饰：ListView 底层的 ScrollView 是个 Panel，而 Avalonia 里
    //    `Background = null` 的 Panel **整块区域不参与命中测试**。于是只有落在子控件上的滚轮才会冒泡到
    //    ScrollView，标题右侧那种没有子控件的空白处根本收不到事件、滚不动。透明 ≠ null，透明可命中。
    //    只在需要处设、**不下沉到 ScrollView**：那是共享原语，命中策略该由使用处定；一刀切会把"指针穿透
    //    滚动容器"这个能力从底层锁死。
    // ② ScrollBar 绑 ScrollView 暴露的 VerticalAxis 即可用，且它只有**手柄**参与命中（ICustomHitTest），
    //    故直接铺满叠在列表上也不抢内容事件；内容不超一屏时它自己不画（TryGetThumb 返回 false）。
    // ③ 右 Margin 是**必需**的，不是留白：本窗口开了 ExtendClientAreaToDecorationsHint，客户区延伸进装饰区，
    //    最外侧那圈缩放边框带落在客户区内部、其指针事件被系统拿去缩放窗口，根本进不了视觉树。手柄默认贴着
    //    右边缘画，外侧大半就泡在那条带子里、点不动。用 Margin 而非改 ScrollBar 的 EdgeMargin：绘制与命中
    //    都从 Bounds.Width 推，缩 Bounds 会让两者**一起**内移、不会重新错开。
    private static Control WithScrollBar(ListView listView)
    {
        listView.Background = Brushes.Transparent;

        var panel = new Panel();
        panel.Children.Add(listView);
        panel.Children.Add(new GUI.Components.ScrollBar(listView.VerticalAxis, Avalonia.Layout.Orientation.Vertical)
        {
            Margin = new Thickness(0, 0, WindowEdgeInset, 0),
        });
        return panel;
    }

    // 让贴边元素避开窗口缩放边框带的内缩量（本窗口各页共用，KeymapSettingsPage 也取这里）。
    //
    // 【本值越大，滚动条越靠左】它是右 Margin：变大 = 右侧留白变多 = 手柄内移。
    //
    // 手柄右缘距页面右边界的实际净空 = 本值 + ScrollBar 自己的 EdgeMargin(2)，当前 = 8，正是 Windows
    // 缩放边框带的教科书宽度（SM_CXSIZEFRAME + SM_CXPADDEDBORDER）。实测那条死区约 6px（内缩为 0 时
    // 手柄左半可点、右半不可），故功能下界是本值 ≥ 4；取 6 在观感与余量之间。
    // Windows 边框宽度随 DPI 浮动，但它是**物理**像素、而这里是逻辑像素，缩放越高对应的逻辑宽度越小，
    // 故 100% 缩放是最坏情形。
    internal const double WindowEdgeInset = 6;

    // 按注册表条目自动生成一个设置页：遍历该 tab 的条目、每条一行（路径类两行）。行序 = SettingsRegistry.All 序（= 窗口顺序源）。
    private Control CreateTabPage(SettingTab tab)
    {
        var listView = new ListView() { Orientation = Avalonia.Layout.Orientation.Vertical, FitWidth = true };
        foreach (var item in SettingsRegistry.All)
        {
            if (item.Tab == tab)
                listView.Content.Children.Add(BuildRow(item));
        }
        return WithScrollBar(listView);
    }

    // 一行设置：路径类 = 标签行 + 全宽 PathInput；其余 = [标签 | 控件] 单行。控件双向绑到条目的 NotifiableProperty。
    private Control BuildRow(SettingItem item)
    {
        if (item.FilePatterns != null)
        {
            var wrap = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Vertical };
            wrap.Children.Add(new TextBlock() { Text = item.DisplayLabel + ": ", Margin = new(24, 12), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var picker = new PathInput()
            {
                Margin = new(24, 12),
                Options = new FilePickerOpenOptions() { FileTypeFilter = [new FilePickerFileType(item.FilePickerName ?? "File") { Patterns = item.FilePatterns }] },
            };
            picker.Bind(((SettingItem<string>)item).Property, false, s);
            wrap.Children.Add(picker);
            return wrap;
        }

        var panel = new DockPanel() { Margin = new(24, 12) };
        panel.AddDock(BuildControl(item), Dock.Right);
        panel.AddDock(new TextBlock() { Text = item.DisplayLabel + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        return panel;
    }

    // 按条目的值类型 + config 建对应控件并双向绑到其 NotifiableProperty（ImmediateApply 走 syncWhileModifying）。
    private Control BuildControl(SettingItem item)
    {
        switch (item)
        {
            case SettingItem<bool> b:
            {
                var checkBox = new GUI.Components.CheckBox();
                checkBox.Bind(b.Property, false, s);
                return checkBox;
            }
            case SettingItem<double> d when item.Config is SliderConfig sc:
            {
                var slider = new SliderController() { Width = 180, IsInteger = IsIntegerScale(sc.Scale) };
                slider.SetRange(sc.Scale.ToValue(0), sc.Scale.ToValue(1));
                slider.SetDefaultValue(d.DefaultValue);
                slider.Bind(d.Property, item.ImmediateApply, s);
                return slider;
            }
            case SettingItem<int> i when item.Config is SliderConfig sc:
            {
                var slider = new SliderController() { Width = 180, IsInteger = true };
                slider.SetRange(sc.Scale.ToValue(0), sc.Scale.ToValue(1));
                slider.SetDefaultValue(i.DefaultValue);
                slider.Bind(i.Property, item.ImmediateApply, s);
                return slider;
            }
            case SettingItem<int> i when item.Config is ComboBoxConfig cc:   // SampleRate / BufferSize（字符串项 ↔ int）
            {
                var comboBox = new ComboBoxController() { Width = 180 };
                comboBox.SetConfig(cc);
                comboBox.Select(int.Parse, (int value) => value.ToString()).Bind(i.Property, false, s);
                if (EngineDisplayFor(item.Key) is { } dv)
                    comboBox.Display(dv);
                return comboBox;
            }
            case SettingItem<string> str when item.Config is ComboBoxConfig cc:   // Language / 字体 / 音频驱动·设备
            {
                var comboBox = new ComboBoxController() { Width = (item.Key is "AudioDriver" or "AudioDevice") ? 300 : 180 };
                comboBox.SetConfig(ComboBoxConfig.Create(item.DynamicOptions?.Invoke() ?? cc.Items));
                comboBox.Bind(str.Property, false, s);
                if (EngineDisplayFor(item.Key) is { } dv)
                    comboBox.Display(dv);
                return comboBox;
            }
            default:
                return new TextBlock() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };   // 4 个 tab 内不出现的组合，防御性占位
        }
    }

    // 部分音频下拉在绑定后额外显示 AudioEngine 的实时值（设置值可能与引擎当前值不同）。其余返回 null。
    private static PropertyValue? EngineDisplayFor(string key) => key switch
    {
        "AudioDriver" => PropertyValue.Create(AudioEngine.CurrentDriver.Value),
        "AudioDevice" => PropertyValue.Create(AudioEngine.CurrentDevice.Value),
        "SampleRate" => PropertyValue.Create(AudioEngine.SampleRate.Value.ToString()),
        "BufferSize" => PropertyValue.Create(AudioEngine.BufferSize.Value.ToString()),
        _ => (PropertyValue?)null,
    };

    // 判定滑条是否整数标度（Integer=Rounded(Linear)、私有类型不可判型）：采样几个归一化位，全整即整数标度。
    private static bool IsIntegerScale(INormalizedScale scale)
    {
        foreach (var t in new[] { 0.13, 0.37, 0.61, 0.89 })
        {
            var v = scale.ToValue(t);
            if (Math.Abs(v - Math.Round(v)) > 1e-9)
                return false;
        }
        return true;
    }

    // 「扩展」页：枚举声明了 IExtensionSettings 的 extension（effect/voice…；agent 自有侧边栏设置不在此），
    // 每个 extension 一段「显示名标题 + 配置驱动属性面板」。编辑写进各自独立的 DataPropertyObject，统一在切走/关窗时落盘。
    private Control CreateExtensionsPage()
    {
        var listView = new ListView() { Orientation = Avalonia.Layout.Orientation.Vertical, FitWidth = true };

        var entries = ExtensionSettingsManager.GetEntries();
        if (entries.Count == 0)
        {
            listView.Content.Children.Add(new TextBlock
            {
                Text = "No extensions with settings.".Tr(this),
                Margin = new Thickness(24, 16),
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
            });
            return WithScrollBar(listView);
        }

        foreach (var entry in entries)
        {
            var title = new TextBlock
            {
                Text = entry.DisplayName,
                FontSize = 14,
                Margin = new Thickness(24, 16, 24, 0),
                Foreground = Style.TEXT_LIGHT.ToBrush(),
            };
            listView.Content.Children.Add(title);

            // 记下待定位插件的标题控件 + 所在 ListView，供开窗后滚动到位。
            // 给了 focusExtensionKey 就精确匹配该能力位，否则匹配到该包首个条目（先到先得，故加 == null 守卫）。
            if (!string.IsNullOrEmpty(mFocusExtensionPackageId) && entry.PackageId == mFocusExtensionPackageId
                && (string.IsNullOrEmpty(mFocusExtensionKey) || entry.ExtensionKey == mFocusExtensionKey)
                && mFocusEntryControl == null)
            {
                mFocusListView = listView;
                mFocusEntryControl = title;
            }

            // 设置数据须挂在文档根上（属性面板字段绑定会读 DataObject.Head），每 extension 一份独立 DataDocument。
            var data = new DataPropertyObject(new DataDocument());
            foreach (var kv in ExtensionSettingsManager.Load(entry)) // 已解密密钥
                data.SetValue(kv.Key, kv.Value);
            data.Commit();

            var ctx = new SettingsContext(data);
            var controller = new PropertyObjectController();
            controller.SetConfig(entry.Settings.GetSettingsConfig(ctx), data);
            listView.Content.Children.Add(controller);

            // 动态设置项：值变更后按当前值重算 config 并 diff 到控件树（条件显隐）。reconcile 复用同一数据对象、不丢焦点。
            var captured = entry;
            Action refresh = () => controller.Reconcile(captured.Settings.GetSettingsConfig(ctx));
            data.Modified.Subscribe(refresh);

            mExtensionPages.Add(new ExtensionPage(entry, data, controller, ctx, refresh));
        }
        return WithScrollBar(listView);
    }

    // 「Extension Routing」页：当同一身份（voice/effect/agent 引擎 id、format 扩展名）被多个扩展包提供时，
    // 列出冲突行让用户选用哪个包的实现。行=身份、右侧下拉=各候选包；只列有冲突(>1 提供者)的身份。
    // 选择即写 ExtensionRouting（即时落盘进 Configs/ExtensionRouting.json，不走「扩展」页的批量落盘、
    // 也不在 Settings.json 里——那份只承与用户环境无关的宿主设置）；重启后生效（与切语言一致）。
    private Control CreateRoutingPage()
    {
        var listView = new ListView() { Orientation = Avalonia.Layout.Orientation.Vertical, FitWidth = true };

        var rows = ExtensionRouting.GetConflicts();
        if (rows.Count == 0)
        {
            listView.Content.Children.Add(new TextBlock
            {
                Text = "No conflicting extensions. (Conflicts appear here when multiple packages provide the same engine id or file format.)".Tr(this),
                Margin = new Thickness(24, 16),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
            });
            return WithScrollBar(listView);
        }

        listView.Content.Children.Add(new TextBlock
        {
            Text = "Multiple packages provide the same extension. Choose which one to use. Changes apply after restart.".Tr(this),
            Margin = new Thickness(24, 16, 24, 4),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(),
        });

        // 行按插件类型分组（GetConflicts 已按 kind 顺序产出，format-import/export 相邻同归 Format 组）：
        // kind 变到新组时插一条组标题，行缩进列在组下。
        string? currentGroup = null;
        foreach (var row in rows)
        {
            var group = RouteGroupLabel(row.Kind);
            if (group != currentGroup)
            {
                currentGroup = group;
                listView.Content.Children.Add(new TextBlock
                {
                    Text = group,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(24, 18, 24, 2),
                    Foreground = Style.LIGHT_WHITE.Opacity(0.85).ToBrush(),
                });
            }

            var panel = new DockPanel() { Margin = new(36, 8, 24, 8) };
            {
                var comboBox = new ComboBoxController() { Width = 280 };
                comboBox.SetConfig(ComboBoxConfig.Create(
                    row.Options.Select(o => new ComboBoxItem(PropertyValue.Create(o.PackageId), OptionLabel(o))).ToList<ComboBoxItem>()));
                comboBox.Display(PropertyValue.Create(row.ActivePackageId));
                var routeKey = row.RouteKey;
                comboBox.ValueCommitted.Subscribe(() =>
                {
                    ExtensionRouting.SetSelected(routeKey, comboBox.Value.ToString());
                }, s);
                panel.AddDock(comboBox, Dock.Right);
            }
            {
                var labelPanel = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                labelPanel.Children.Add(new TextBlock
                {
                    Text = row.Identity,
                    FontSize = 14,
                    Foreground = Style.TEXT_LIGHT.ToBrush(),
                });
                // format 行标 import/export 方向（同一扩展名两行靠这区分）；引擎类无方向、不加副标签。
                var direction = RouteDirectionLabel(row.Kind);
                if (!string.IsNullOrEmpty(direction))
                {
                    labelPanel.Children.Add(new TextBlock
                    {
                        Text = direction,
                        FontSize = 11,
                        Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
                    });
                }
                panel.AddDock(labelPanel);
            }
            listView.Content.Children.Add(panel);
        }
        return WithScrollBar(listView);
    }

    // 下拉项文本：候选【包名】（来自 manifest），附包 id 消歧（同名/本地化时）。内建显示 "Built-In"。
    private static string OptionLabel(ExtensionRouting.RouteOption option)
    {
        var packageName = ExtensionManager.GetPackageName(option.PackageId);
        return string.IsNullOrEmpty(option.PackageId) || packageName == option.PackageId
            ? packageName
            : string.Format("{0} ({1})", packageName, option.PackageId);
    }

    // 分组标题（插件类型）：format 的 import/export 同归「Format」一组。
    private string RouteGroupLabel(string kind) => kind switch
    {
        "voice" => "Voice".Tr(this),
        // instrument 是与 voice 平行的插件类型、ExtensionRouting.GetConflicts 同样收集它——此前漏了这一支，
        // 两个包提供同一 instrument 身份时分组标题会落到 _ 兜底、显示未翻译的小写裸 kind。
        "instrument" => "Instrument".Tr(this),
        "effect" => "Effect".Tr(this),
        // 无 agent-model 分支：模型适配器全是内建、一个 type 只有一个实现，从不进冲突矩阵（见 ExtensionRouting）。
        "format-import" or "format-export" => "Format".Tr(this),
        _ => kind,
    };

    // 行内方向副标签：仅 format 有 import/export 之分；引擎类返回空（无副标签）。
    private string RouteDirectionLabel(string kind) => kind switch
    {
        "format-import" => "Import".Tr(this),
        "format-export" => "Export".Tr(this),
        _ => string.Empty,
    };

    // 把当前「扩展」页的全部编辑统一落盘并回喂（密钥按 IsPassword 标出交由存储层加密）。
    // 由切走 tab / 关窗 / Esc 调用；落盘后清空，非扩展页时本就为空、无副作用。
    private void SaveExtensionSettings()
    {
        foreach (var page in mExtensionPages)
        {
            // 密钥集按【当前值算出的】config 取（动态面板下密钥字段可能随值显隐），避免漏标/误标。
            var secrets = ExtensionSettingsStore.PasswordKeys(page.Entry.Settings.GetSettingsConfig(page.Context));
            ExtensionSettingsStore.Save(page.Entry.PackageId, page.Entry.ExtensionKey, page.Data.GetInfo(), secrets);
            ExtensionSettingsManager.ApplyOne(page.Entry); // 立即回喂（重载已落盘值、解密密钥）
            page.Data.Modified.Unsubscribe(page.Refresh);  // 解订阅，再解绑控件 + 归还池化控件（页面随后整体重建）
            page.Controller.ResetConfig();
        }
        mExtensionPages.Clear();
    }

    private sealed record ExtensionPage(ExtensionSettingsManager.Entry Entry, DataPropertyObject Data, PropertyObjectController Controller, SettingsContext Context, Action Refresh);

    // IExtensionSettings.GetSettingsConfig 的求值上下文：返回设置数据对象的当前值快照（动态面板据此重算）。
    private sealed class SettingsContext(DataPropertyObject data) : IExtensionSettingsContext
    {
        public PropertyObject Settings => data.GetInfo();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SaveExtensionSettings();
            Settings.Save(PathManager.SettingsFilePath);
            s.DisposeAll();
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private class TabPageInfo
    {
        public string Name { get; }
        public SvgIcon Icon { get; }
        public Func<Control> CreateContent { get; }
        public Border? AccentBar { get; set; }
        public Border? TabBorder { get; set; }

        public TabPageInfo(string name, SvgIcon icon, Func<Control> createContent)
        {
            Name = name;
            Icon = icon;
            CreateContent = createContent;
        }
    }

    private readonly Border mSidebarBorder;
    private readonly Border mContentBorder;
    private readonly StackPanel mSidebarPanel;
    private readonly DockPanel mContentPanel;
    private readonly List<Border> mTabButtons = new();
    private readonly List<TabPageInfo> mTabPages = new();
    private int mSelectedIndex = -1;
    // 详情窗齿轮请求定位到某插件设置：捕获其标题控件 + 所在 ListView，开窗后滚到位。
    // 非 readonly：窗已开着时再点别处的齿轮，经 FocusExtension 换目标重新定位（见 Open）。
    private string? mFocusExtensionPackageId;
    // 目标能力位的桶键（"kind:extensionId"）；为空表示只定位到包。
    private string? mFocusExtensionKey;
    private ListView? mFocusListView;
    private Control? mFocusEntryControl;
    private readonly DisposableManager s = new();
    // 当前打开的唯一实例（UI 线程独占访问）；关窗时由 Closed 清空。
    private static SettingsWindow? sInstance;
    // 当前「扩展」页各 extension 的实时编辑（切走/关窗时统一落盘后清空）。
    private readonly List<ExtensionPage> mExtensionPages = new();
}
