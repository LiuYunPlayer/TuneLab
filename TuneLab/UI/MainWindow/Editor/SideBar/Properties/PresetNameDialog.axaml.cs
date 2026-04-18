using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;

namespace TuneLab.UI;

internal partial class PresetNameDialog : Window
{
    public PresetNameDialog(string initialName = "")
    {
        InitializeComponent();
        Focusable = true;
        CanResize = false;
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;

        TitleLabel.Content = "Preset Name".Tr(TC.Property);
        Title = "Preset".Tr(TC.Property) + " - TuneLab";

        this.Background = Style.BACK.ToBrush();
        TitleLabel.Foreground = Style.TEXT_LIGHT.ToBrush();

        var closeButton = new Button() { Width = 48, Height = 40 }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 0 }, ColorSet = new() { HoveredColor = Colors.White.Opacity(0.2), PressedColor = Colors.White.Opacity(0.2) } })
            .AddContent(new() { Item = new IconItem() { Icon = Assets.WindowClose }, ColorSet = new() { Color = Style.TEXT_LIGHT.Opacity(0.7) } });
        closeButton.Clicked += () => Close(null);
        WindowControl.Children.Add(closeButton);

        var titleBar = this.FindControl<Grid>("TitleBar") ?? throw new System.InvalidOperationException("TitleBar not found");
        bool useSystemTitle = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
        if (useSystemTitle)
        {
            titleBar.Height = 0;
            Height -= 40;
        }

        Content.Background = Style.INTERFACE.ToBrush();

        mNameInput = new TextInput()
        {
            Width = 352,
            Height = 32,
            Background = Style.BACK.ToBrush(),
            Foreground = Style.WHITE.ToBrush(),
        };
        mNameInput.Display(initialName);
        mNameInput.KeyDown += OnNameInputKeyDown;
        InputBox.Children.Add(mNameInput);

        var okButtonPanel = new StackPanel()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        var okButton = new Button() { Width = 64, Height = 28 };
        okButton.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { Color = Style.BUTTON_PRIMARY, HoveredColor = Style.BUTTON_PRIMARY_HOVER } });
        okButton.AddContent(new() { Item = new TextItem() { Text = "OK".Tr(TC.Dialog) }, ColorSet = new() { Color = Colors.White } });
        okButton.Clicked += () => Close(mNameInput.Text.Trim());
        okButtonPanel.Children.Add(okButton);
        ActionsPanel.Children.Add(okButtonPanel);
        Grid.SetColumn(okButtonPanel, 1);

        Opened += (s, e) =>
        {
            mNameInput.Focus();
            mNameInput.SelectAll();
        };
    }

    void OnNameInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Close(mNameInput.Text.Trim());
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(null);
        }
    }

    readonly TextInput mNameInput;
}
