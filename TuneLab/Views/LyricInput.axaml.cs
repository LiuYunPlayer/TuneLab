using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using System;
using TuneLab.Utils;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using CheckBox = TuneLab.GUI.Components.CheckBox;
using Avalonia.Layout;
using Button = TuneLab.GUI.Components.Button;
using Avalonia.Media;

namespace TuneLab.Views
{
    public partial class LyricInput : Window
    {
        public LyricInput()
        {
            InitializeComponent();
            Focusable = true;
            CanResize = false;
            WindowState = WindowState.Normal;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;

            this.DataContext = this;
            this.Background = Style.INTERFACE.ToBrush();
            TitleBar.Background = Style.BACK.ToBrush();

            var LyricInputBox = new TextInput();
            LyricInputBox.AcceptsReturn = true;
            LyricInputBox.Width = 432;
            LyricInputBox.Height = 163;
            LyricInputBox.Background = Style.BACK.ToBrush();
            LyricInputBox.Padding = new(8, 8);
            LyricInputBox.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top;
            LyricInputBox.Foreground = Style.WHITE.ToBrush();
            LyricInputBox.TextWrapping = TextWrapping.Wrap;
            TextareaBox.Children.Add(LyricInputBox);

            var SkipTenutoLabelPanel = new StackPanel();
            var SkipTenutoCheckBox = new CheckBox();
            SkipTenutoLabelPanel.Orientation = Orientation.Horizontal;
            SkipTenutoLabelPanel.Height = 24;
            SkipTenutoLabelPanel.Children.Add(SkipTenutoCheckBox);
            SkipTenutoLabelPanel.Children.Add(new Label() { Content = "Skip Tenuto", FontSize = 12, Foreground = Style.TEXT_LIGHT.ToBrush(), Margin = new(14, 1) });
            ActionsPanel.Children.Add(SkipTenutoLabelPanel);
            Grid.SetColumn(SkipTenutoLabelPanel, 0);
            var OkButtonPanel = new StackPanel();
            OkButtonPanel.Orientation = Orientation.Horizontal;
            OkButtonPanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            var OkButton = new Button() { Width = 64, Height = 28 };
            OkButton.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { Color = new(255, 96, 96, 192), HoveredColor = new(255, 127, 127, 255) } });
            OkButton.AddContent(new() { Item = new TextItem() { Text = "OK" }, ColorSet = new() { Color = Colors.White } });
            OkButtonPanel.Children.Add(OkButton);
            ActionsPanel.Children.Add(OkButtonPanel);
            Grid.SetColumn(OkButton, 1);
        }
    }
}
