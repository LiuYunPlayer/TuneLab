using Avalonia;
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

internal class PresetNameDialog : Window
{
    public PresetNameDialog(string initialName = "")
    {
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowState = WindowState.Normal;
        Topmost = true;
        Width = 360;
        Height = 176;
        Background = Style.BACK.ToBrush();
        Title = "Preset".Tr(TC.Property) + " - TuneLab";

        var root = new DockPanel();
        var titleBar = new Border()
        {
            Background = Style.INTERFACE.ToBrush(),
            Height = 40,
            Child = new Label()
            {
                Content = "Preset Name".Tr(TC.Property),
                Margin = new Thickness(16, 0),
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = Style.TEXT_LIGHT.ToBrush(),
                FontSize = 14,
            }
        };
        DockPanel.SetDock(titleBar, Dock.Top);
        root.Children.Add(titleBar);

        var content = new StackPanel() { Margin = new(16), Spacing = 12 };
        content.Children.Add(new TextBlock()
        {
            Text = "Preset Name".Tr(TC.Property),
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            FontSize = 12,
        });

        mNameInput = new TextInput()
        {
            Height = 32,
            Foreground = Style.WHITE.ToBrush(),
            Background = Style.BACK.ToBrush(),
        };
        mNameInput.Display(initialName);
        mNameInput.KeyDown += OnNameInputKeyDown;
        content.Children.Add(mNameInput);

        var buttons = new StackPanel()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        buttons.Children.Add(CreateButton("Cancel".Tr(TC.Dialog), false));
        buttons.Children.Add(CreateButton("OK".Tr(TC.Dialog), true));
        content.Children.Add(buttons);

        root.Children.Add(content);
        Content = root;

        Opened += (s, e) =>
        {
            mNameInput.Focus();
            mNameInput.SelectAll();
        };
    }

    Button CreateButton(string text, bool isPrimary)
    {
        var button = new Button() { MinWidth = 96, Height = 36 };
        button.AddContent(new()
        {
            Item = new BorderItem() { CornerRadius = 6 },
            ColorSet = new()
            {
                Color = isPrimary ? Style.BUTTON_PRIMARY : Style.BUTTON_NORMAL,
                HoveredColor = isPrimary ? Style.BUTTON_PRIMARY_HOVER : Style.BUTTON_NORMAL_HOVER,
            }
        });
        button.AddContent(new() { Item = new TextItem() { Text = text }, ColorSet = new() { Color = isPrimary ? Colors.White : Style.LIGHT_WHITE } });
        button.Clicked += () =>
        {
            if (isPrimary)
                Close(mNameInput.Text.Trim());
            else
                Close(null);
        };
        return button;
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
