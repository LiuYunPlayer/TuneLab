using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Configs;
using TuneLab.GUI;
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

        TitleLabel.Content = "Settings".Tr(TC.Dialog);

        this.Background = Style.BACK.ToBrush();
        TitleLabel.Foreground = Style.TEXT_LIGHT.ToBrush();

        var closeButton = new GUI.Components.Button() { Width = 48, Height = 40 }
                .AddContent(new() { Item = new BorderItem() { CornerRadius = 0 }, ColorSet = new() { HoveredColor = Colors.White.Opacity(0.2), PressedColor = Colors.White.Opacity(0.2) } })
                .AddContent(new() { Item = new IconItem() { Icon = Assets.WindowClose }, ColorSet = new() { Color = Style.TEXT_LIGHT.Opacity(0.7) } });
        closeButton.Clicked += () =>
        {
            Settings.Save(PathManager.SettingsFilePath);
            Close();
        };

        WindowControl.Children.Add(closeButton);

        Content.Background = Style.INTERFACE.ToBrush();
    }
}
