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
    public SettingsWindow()
    {
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
            new("General", Assets.General, CreateGeneralPage),
            new("Audio", Assets.Audio, CreateAudioPage),
            new("Appearance", Assets.Appearance, CreateAppearancePage),
            new("Editing", Assets.Editing, CreateEditorPage),
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

        // Select the first tab by default
        if (mTabPages.Count > 0)
        {
            SelectTab(0);
        }
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

    private Control CreateGeneralPage()
    {
        var listView = new ListView() { Orientation = Avalonia.Layout.Orientation.Vertical, FitWidth = true };

        // Language
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var comboBox = new ComboBoxController() { Width = 180 };
                comboBox.SetConfig(ComboBoxConfig.Create(TranslationManager.Languages.Select(o => new ComboBoxItem(o, TranslationManager.GetDisplayName(o))).ToList()));
                comboBox.Bind(Settings.Language, false, s);
                panel.AddDock(comboBox, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Language".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }

        // Auto Save Interval
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInteger = true };
                slider.SetRange(10, 60);
                slider.SetDefaultValue(Settings.DefaultSettings.AutoSaveInterval);
                slider.Bind(Settings.AutoSaveInterval, false, s);
                panel.AddDock(slider, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Auto Save Interval (second)".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }

        // Auto Save Max Count
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInteger = true };
                slider.SetRange(1, 20);
                slider.SetDefaultValue(Settings.DefaultSettings.AutoSaveMaxCount);
                slider.Bind(Settings.AutoSaveMaxCount, false, s);
                panel.AddDock(slider, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Auto Save Max Count".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }

        // Max Parallel Synthesis Tasks（合成/效果器并行任务数上限；0 = 按 CPU 核数自动）
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInteger = true };
                slider.SetRange(0, 32);
                slider.SetDefaultValue(Settings.DefaultSettings.MaxParallelSynthesisTasks);
                slider.Bind(Settings.MaxParallelSynthesisTasks, false, s);
                panel.AddDock(slider, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Max Parallel Synthesis Tasks (0 = auto)".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }

        return listView;
    }

    private Control CreateAudioPage()
    {
        var listView = new ListView() { Orientation = Avalonia.Layout.Orientation.Vertical, FitWidth = true };

        // Master Gain
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInteger = false };
                slider.SetRange(-24, 24);
                slider.SetDefaultValue(Settings.DefaultSettings.MasterGain);
                slider.Bind(Settings.MasterGain, true, s);
                panel.AddDock(slider, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Master Gain (dB)".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }

        // Audio Driver
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var comboBox = new ComboBoxController() { Width = 300 };
                comboBox.SetConfig(ComboBoxConfig.Create(AudioEngine.GetAllDrivers().Select(o => (ComboBoxItem)o).ToList()));
                comboBox.Bind(Settings.AudioDriver, false, s);
                comboBox.Display(AudioEngine.CurrentDriver.Value);
                panel.AddDock(comboBox, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Audio Driver".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }

        // Audio Device
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var comboBox = new ComboBoxController() { Width = 300 };
                comboBox.SetConfig(ComboBoxConfig.Create(AudioEngine.GetAllDevices().Select(o => (ComboBoxItem)o).ToList()));
                comboBox.Bind(Settings.AudioDevice, false, s);
                comboBox.Display(AudioEngine.CurrentDevice.Value);
                panel.AddDock(comboBox, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Audio Device".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }

        // Sample Rate
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var comboBox = new ComboBoxController() { Width = 180 };
                comboBox.SetConfig(ComboBoxConfig.Create(["32000", "44100", "48000", "96000", "192000"]));
                comboBox.Select(int.Parse, (int value) => { return value.ToString(); }).Bind(Settings.SampleRate, false, s);
                comboBox.Display(AudioEngine.SampleRate.Value.ToString());
                panel.AddDock(comboBox, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Sample Rate".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }

        // Buffer Size
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var comboBox = new ComboBoxController() { Width = 180 };
                comboBox.SetConfig(ComboBoxConfig.Create(["64", "128", "256", "512", "1024", "2048", "4096", "8192"]));
                comboBox.Select(int.Parse, (int value) => { return value.ToString(); }).Bind(Settings.BufferSize, false, s);
                comboBox.Display(AudioEngine.BufferSize.Value.ToString());
                panel.AddDock(comboBox, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Buffer Size".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }

        // Piano Key Samples
        {
            var name = new TextBlock() { Margin = new(24, 12), Text = "Piano Key Samples".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            listView.Content.Children.Add(name);
        }
        {
            var controller = new PathInput() { Margin = new(24, 12), Options = new FolderPickerOpenOptions() };
            controller.Bind(Settings.PianoKeySamplesPath, false, s);
            listView.Content.Children.Add(controller);
        }

        return listView;
    }

    private Control CreateAppearancePage()
    {
        var listView = new ListView() { Orientation = Avalonia.Layout.Orientation.Vertical, FitWidth = true };

        // 界面字体：空值 = 系统默认（Inter + 平台脚本回退链）；其余为本机已装字体家族名。改后须重启（字体在启动时装配）。
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var options = new List<ComboBoxItem> { new(PropertyValue.Create(string.Empty), "System Default".Tr(this)) };
                options.AddRange(FontManager.Current.SystemFonts
                    .Select(f => f.Name).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .Select(n => (ComboBoxItem)n));
                var comboBox = new ComboBoxController() { Width = 180 };
                comboBox.SetConfig(ComboBoxConfig.Create(options));
                comboBox.Bind(Settings.InterfaceFontFamily, false, s);
                panel.AddDock(comboBox, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Interface Font".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }

        // 全局背景图：路径 + 不透明度。不透明度同时作用于声库立绘（立绘优先于背景图显示），与路径解耦、单列一行；
        // 按用户意愿标签仍按背景图措辞（不强调立绘 / 背景之分）。
        {
            var name = new TextBlock() { Text = "Custom Background Image".Tr(this) + ": ", Margin = new(24, 12), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            listView.Content.Children.Add(name);
        }
        {
            var controller = new PathInput() { Margin = new(24, 12), Options = new FilePickerOpenOptions() { FileTypeFilter = [FilePickerFileTypes.ImageAll] } };
            controller.Bind(Settings.BackgroundImagePath, false, s);
            listView.Content.Children.Add(controller);
        }
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInteger = false };
                slider.SetRange(0, 1);
                slider.SetDefaultValue(Settings.DefaultSettings.BackgroundImageOpacity);
                slider.Bind(Settings.BackgroundImageOpacity, true, s);
                panel.AddDock(slider, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Background Image Opacity".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }

        // Track Hue Change Rate
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInteger = true };
                slider.SetRange(-720, 720);
                slider.SetDefaultValue(Settings.DefaultSettings.TrackHueChangeRate);
                slider.Bind(Settings.TrackHueChangeRate, true, s);
                panel.AddDock(slider, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Track Hue Change Rate (degree/second)".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }

        return listView;
    }

    private Control CreateEditorPage()
    {
        var listView = new ListView() { Orientation = Avalonia.Layout.Orientation.Vertical, FitWidth = true };

        // Parameter Boundary Extension
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInteger = true };
                slider.SetRange(1, 60);
                slider.SetDefaultValue(Settings.DefaultSettings.ParameterBoundaryExtension);
                slider.Bind(Settings.ParameterBoundaryExtension, false, s);
                panel.AddDock(slider, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Parameter Boundary Extension (tick)".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }

        // Parameter Sync Mode
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var checkBox = new GUI.Components.CheckBox();
                checkBox.Bind(Settings.ParameterSyncMode, false, s);
                panel.AddDock(checkBox, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Parameter Sync Mode".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }

        return listView;
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
            return listView;
        }

        foreach (var entry in entries)
        {
            listView.Content.Children.Add(new TextBlock
            {
                Text = entry.DisplayName,
                FontSize = 14,
                Margin = new Thickness(24, 16, 24, 0),
                Foreground = Style.TEXT_LIGHT.ToBrush(),
            });

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
        return listView;
    }

    // 「Extension Routing」页：当同一身份（voice/effect/agent 引擎 id、format 扩展名）被多个扩展包提供时，
    // 列出冲突行让用户选用哪个包的实现。行=身份、右侧下拉=各候选包；只列有冲突(>1 提供者)的身份。
    // 选择即写 ExtensionRouting（即时落盘进 app Settings.json，不走「扩展」页的批量落盘）；重启后生效（与切语言一致）。
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
            return listView;
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
        return listView;
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
        "effect" => "Effect".Tr(this),
        "agent-model" => "Agent Model".Tr(this),
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
    private readonly DisposableManager s = new();
    // 当前「扩展」页各 extension 的实时编辑（切走/关窗时统一落盘后清空）。
    private readonly List<ExtensionPage> mExtensionPages = new();
}
