using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Media;
using System;
using TuneLab.GUI;
using TuneLab.I18N;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;
using CheckBox = TuneLab.GUI.Components.CheckBox;

namespace TuneLab.Views;
internal partial class TrackSelector : Window
{
    public TrackSelector()
    {
        InitializeComponent();
        Focusable = true;
        CanResize = false;
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        TitleLabel.Content = "Select Track".Tr(TC.Dialog);
        SelectorActionLabel.Content = "Select the track to import:".Tr(TC.Dialog);

        this.Background = Style.BACK.ToBrush();
        TitleLabel.Foreground = Style.TEXT_LIGHT.ToBrush();

        var closeButton = new Button() { Width = 48, Height = 40 }
                .AddContent(new() { Item = new BorderItem() { CornerRadius = 0 }, ColorSet = new() { HoveredColor = Colors.White.Opacity(0.2), PressedColor = Colors.White.Opacity(0.2) } })
                .AddContent(new() { Item = new IconItem() { Icon = Assets.WindowClose }, ColorSet = new() { Color = Style.TEXT_LIGHT.Opacity(0.7) } });
        closeButton.Clicked += () => Close();

        WindowControl.Children.Add(closeButton);

        Content.Background = Style.INTERFACE.ToBrush();

        mTrackList.Background = Style.BACK.ToBrush();
        mTrackList.SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle;


        var KeepTempoPanel = new StackPanel();
        mKeepTempoCheckBox = new CheckBox();
        KeepTempoPanel.Orientation = Orientation.Horizontal;
        KeepTempoPanel.Height = 24;
        KeepTempoPanel.Children.Add(mKeepTempoCheckBox);
        KeepTempoPanel.Children.Add(new Label() { Content = "Keep Tempo".Tr(TC.Dialog), FontSize = 12, Foreground = Style.TEXT_LIGHT.ToBrush(), Margin = new(14, 1) });
        ActionsPanel.Children.Add(KeepTempoPanel);
        Grid.SetColumn(KeepTempoPanel, 0);

        var OkButtonPanel = new StackPanel();
        OkButtonPanel.Orientation = Orientation.Horizontal;
        OkButtonPanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        var OkButton = new Button() { Width = 64, Height = 28 };
        OkButton.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { Color = Style.BUTTON_PRIMARY, HoveredColor = Style.BUTTON_PRIMARY_HOVER } });
        OkButton.AddContent(new() { Item = new TextItem() { Text = "OK".Tr(TC.Dialog) }, ColorSet = new() { Color = Colors.White } });
        OkButtonPanel.Children.Add(OkButton);
        ActionsPanel.Children.Add(OkButtonPanel);
        Grid.SetColumn(OkButton, 1);

        OkButton.Clicked += () => { isOK = true;this.Close(); };
    }

    CheckBox mKeepTempoCheckBox;
    public ListBox TrackList { get => mTrackList; }
    public bool isOK { get; set; } = false;
    public bool isKeepTempo { get => mKeepTempoCheckBox.IsChecked==null?false:(bool)mKeepTempoCheckBox.IsChecked; }
}
