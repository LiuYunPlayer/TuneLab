﻿using Avalonia.Controls;
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

        Content.Background = Style.INTERFACE.ToBrush();

        Settings.Language.Modified.Subscribe(async () => await this.ShowMessage("Tips".Tr(TC.Dialog), "Please restart to apply settings.".Tr(this)), s);

        var listView = new ListView() { Orientation = Avalonia.Layout.Orientation.Vertical, FitWidth = true };
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
        {
            var name = new TextBlock() { Margin = new(24, 12), Text = "Piano Key Samples".Tr(this) + ": ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            listView.Content.Children.Add(name);
        }
        {
            var controller = new PathInput() { Margin = new(24, 12), Options = new FolderPickerOpenOptions() };
            controller.Bind(Settings.PianoKeySamplesPath, false, s);
            listView.Content.Children.Add(controller);
        }
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
        Content.AddDock(listView);
    }

    readonly DisposableManager s = new();
}
