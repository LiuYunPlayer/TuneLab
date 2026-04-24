using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Utils;
using TuneLab.Configs;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;
using TuneLab.Base.Properties;
using TuneLab.GUI.Controllers;
using Avalonia.Platform.Storage;
using TuneLab.Base.Event;
using TuneLab.Audio;
using TuneLab.Base.Structures;

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

        // Create tab pages
        var tabPages = new List<TabPageInfo>
        {
            new("General", Assets.General, CreateGeneralPage),
            new("Audio", Assets.Audio, CreateAudioPage),
            new("Appearance", Assets.Appearance, CreateAppearancePage),
            new("Editing", Assets.Editing, CreateEditorPage),
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
                comboBox.SetConfig(new(TranslationManager.Languages));
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
                var slider = new SliderController() { Width = 180, IsInterger = true };
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

        return listView;
    }

    private Control CreateAudioPage()
    {
        var listView = new ListView() { Orientation = Avalonia.Layout.Orientation.Vertical, FitWidth = true };

        // Master Gain
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInterger = false };
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
                comboBox.SetConfig(new(AudioEngine.GetAllDrivers()));
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
                comboBox.SetConfig(new(AudioEngine.GetAllDevices()));
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
                comboBox.SetConfig(new(["32000", "44100", "48000", "96000", "192000"]));
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
                comboBox.SetConfig(new(["64", "128", "256", "512", "1024", "2048", "4096", "8192"]));
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

        // Custom Background Image + Opacity
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInterger = false };
                slider.SetRange(0, 1);
                slider.SetDefaultValue(Settings.DefaultSettings.BackgroundImageOpacity);
                slider.Bind(Settings.BackgroundImageOpacity, true, s);
                panel.AddDock(slider, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Opacity".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new(0, 0, 12, 0) };
                panel.AddDock(name, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Custom Background Image".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }
        {
            var controller = new PathInput() { Margin = new(24, 12), Options = new FilePickerOpenOptions() { FileTypeFilter = [FilePickerFileTypes.ImageAll] } };
            controller.Bind(Settings.BackgroundImagePath, false, s);
            listView.Content.Children.Add(controller);
        }

        // Track Hue Change Rate
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInterger = true };
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
                var slider = new SliderController() { Width = 180, IsInterger = true };
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
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
}
