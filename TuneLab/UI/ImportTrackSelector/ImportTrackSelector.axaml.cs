using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Media;
using System;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.I18N;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;
using CheckBox = TuneLab.GUI.Components.CheckBox;

namespace TuneLab.UI;
internal partial class ImportTrackSelector : Window
{
    public ImportTrackSelector(double currentBpm, int currentNumerator, int currentDenominator)
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
        
        var titleBar = this.FindControl<Grid>("TitleBar") ?? throw new System.InvalidOperationException("TitleBar not found");
        bool UseSystemTitle = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
	    if(UseSystemTitle){
		    titleBar.Height = 0;
		    Height -= 40;
	    }
     
        Content.Background = Style.INTERFACE.ToBrush();

        mTrackList.Background = Style.BACK.ToBrush();
        mTrackList.SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle;

        var ImportOptionsPanel = new StackPanel()
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
        };
        ImportOptionsPanel.Children.Add(CreateOptionPanel(
            "Align Tempo to Project".Tr(TC.Dialog),
            false,
            out mKeepTempoCheckBox
        ));
        ImportOptionsPanel.Children.Add(CreateOptionPanel(
            "Import Tempo".Tr(TC.Dialog),
            currentBpm != TempoManager.DefaultBpm,
            out mImportTempoCheckBox
        ));
        ImportOptionsPanel.Children.Add(CreateOptionPanel(
            "Import Time Signature".Tr(TC.Dialog),
            currentNumerator != TimeSignatureManager.DefaultNumerator || currentDenominator != TimeSignatureManager.DefaultDenominator,
            out mImportTimeSignatureCheckBox
        ));
        ActionsPanel.Children.Add(ImportOptionsPanel);
        Grid.SetColumn(ImportOptionsPanel, 0);

        var OkButtonPanel = new StackPanel();
        OkButtonPanel.Orientation = Orientation.Horizontal;
        OkButtonPanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        var OkButton = new Button() { Width = 64, Height = 28 };
        OkButton.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { Color = Style.BUTTON_PRIMARY, HoveredColor = Style.BUTTON_PRIMARY_HOVER } });
        OkButton.AddContent(new() { Item = new TextItem() { Text = "OK".Tr(TC.Dialog) }, ColorSet = new() { Color = Colors.White } });
        OkButtonPanel.Children.Add(OkButton);
        ActionsPanel.Children.Add(OkButtonPanel);
        Grid.SetColumn(OkButtonPanel, 1);

        OkButton.Clicked += () => { isOK = true;this.Close(); };
    }

    StackPanel CreateOptionPanel(string label, bool isChecked, out CheckBox checkBox)
    {
        checkBox = new CheckBox() { IsChecked = isChecked };
        var panel = new StackPanel()
        {
            Orientation = Orientation.Horizontal,
            Height = 24,
        };
        panel.Children.Add(checkBox);
        panel.Children.Add(new Label() { Content = label, FontSize = 12, Foreground = Style.TEXT_LIGHT.ToBrush(), Margin = new(14, 1) });
        return panel;
    }

    CheckBox mKeepTempoCheckBox;
    CheckBox mImportTempoCheckBox;
    CheckBox mImportTimeSignatureCheckBox;
    public ListBox TrackList { get => mTrackList; }
    public bool isOK { get; set; } = false;
    public bool IsKeepTempo => mKeepTempoCheckBox.IsChecked == true;
    public bool IsImportTempo => mImportTempoCheckBox.IsChecked == true;
    public bool IsImportTimeSignature => mImportTimeSignatureCheckBox.IsChecked == true;
}
