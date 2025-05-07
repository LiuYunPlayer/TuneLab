using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.Linq;
using TuneLab.Audio;
using TuneLab.Configs;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Property;
using TuneLab.Foundation.Utils;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Controllers;
using TuneLab.I18N;
using TuneLab.Utils;

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

        var titleBar = this.FindControl<Grid>("TitleBar") ?? throw new System.InvalidOperationException("TitleBar not found");
        bool UseSystemTitle = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
        if (UseSystemTitle)
        {
            titleBar.Height = 0;
        }

        Content.Background = Style.INTERFACE.ToBrush();

        Settings.Language.Modified.Subscribe(async () => await this.ShowMessage("Tips".Tr(TC.Dialog), "Please restart to apply settings.".Tr(this)), s);

        var listView = new ListView() { Orientation = Avalonia.Layout.Orientation.Vertical, FitWidth = true };
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var comboBox = new ComboBoxController() { Width = 180 };
                comboBox.SetConfig(new(TranslationManager.Languages, new ComboBoxOption { Value = default, DisplayText = "(Default)".Tr(this) }));
                s += comboBox.BindProperty(Settings.Language, false);

                panel.AddDock(comboBox, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Language".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInterger = false };
                slider.SetRange(-24, 24);
                slider.SetDefaultValue(Settings.DefaultSettings.MasterGain);
                s += slider.BindProperty(Settings.MasterGain, true);
                panel.AddDock(slider, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Master Gain (dB)".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var comboBox = new ComboBoxController() { Width = 300 };
                comboBox.SetConfig(new(AudioEngine.GetAllDrivers()));
                s += comboBox.BindProperty(Settings.AudioDriver, false);
                comboBox.Display(AudioEngine.CurrentDriver.Value);

                panel.AddDock(comboBox, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Audio Driver".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var comboBox = new ComboBoxController() { Width = 300 };
                comboBox.SetConfig(new(AudioEngine.GetAllDevices()));
                s += comboBox.BindProperty(Settings.AudioDevice, false);
                comboBox.Display(AudioEngine.CurrentDevice.Value);

                panel.AddDock(comboBox, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Audio Device".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var comboBox = new ComboBoxController() { Width = 180 };
                comboBox.SetConfig(new([32000, 44100, 48000, 96000, 192000], 44100));
                s += comboBox.BindProperty(Settings.SampleRate, false);
                comboBox.Display(AudioEngine.SampleRate.Value.ToString());

                panel.AddDock(comboBox, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Sample Rate".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var comboBox = new ComboBoxController() { Width = 180 };
                comboBox.SetConfig(new([64, 128, 256, 512, 1024, 2048, 4096, 8192], 1024));
                s += comboBox.BindProperty(Settings.BufferSize, false);
                comboBox.Display(AudioEngine.BufferSize.Value.ToString());

                panel.AddDock(comboBox, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Buffer Size".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInterger = false };
                slider.SetRange(0, 1);
                slider.SetDefaultValue(Settings.DefaultSettings.BackgroundImageOpacity);
                s += slider.BindProperty(Settings.BackgroundImageOpacity, true);
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
            s += controller.BindProperty(Settings.BackgroundImagePath, false);
            listView.Content.Children.Add(controller);
        }
        {
            var name = new TextBlock() { Margin = new(24, 12), Text = "Piano Key Samples".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            listView.Content.Children.Add(name);
        }
        {
            var controller = new PathInput() { Margin = new(24, 12), Options = new FolderPickerOpenOptions() };
            s += controller.BindProperty(Settings.PianoKeySamplesPath, false);
            listView.Content.Children.Add(controller);
        }
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInterger = true };
                slider.SetRange(10, 60);
                slider.SetDefaultValue(Settings.DefaultSettings.AutoSaveInterval);
                s += slider.BindProperty(Settings.AutoSaveInterval, false);
                panel.AddDock(slider, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Auto Save Interval (second)".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInterger = true };
                slider.SetRange(1, 60);
                slider.SetDefaultValue(Settings.DefaultSettings.ParameterBoundaryExtension);
                s += slider.BindProperty(Settings.ParameterBoundaryExtension, false);
                panel.AddDock(slider, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Parameter Boundary Extension (tick)".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }
        {
            var panel = new DockPanel() { Margin = new(24, 12) };
            {
                var slider = new SliderController() { Width = 180, IsInterger = true };
                slider.SetRange(-720, 720);
                slider.SetDefaultValue(Settings.DefaultSettings.TrackHueChangeRate);
                s += slider.BindProperty(Settings.TrackHueChangeRate, true);
                panel.AddDock(slider, Dock.Right);
            }
            {
                var name = new TextBlock() { Text = "Track Hue Change Rate (degree/second)".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                panel.AddDock(name);
            }
            listView.Content.Children.Add(panel);
        }
        Content.AddDock(listView);
    }

    readonly DisposableManager s = new();
}
