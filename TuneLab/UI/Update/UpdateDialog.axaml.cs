using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;
using Markdown.Avalonia;
using TuneLab.I18N;
using TuneLab.GUI;

namespace TuneLab.UI;

internal partial class UpdateDialog : Window
{
    public enum ButtonType
    {
        Primary,
        Normal
    }

    private Grid titleBar;
    private Label titleLabel;
    private SelectableTextBlock messageTextBlock;
    private MarkdownScrollViewer markDownScrollViewer;

    public UpdateDialog()
    {
        InitializeComponent();
        Focusable = true;
        CanResize = false;
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        this.DataContext = this;
        this.Background = Style.BACK.ToBrush();
        Content.Background = Style.INTERFACE.ToBrush();

        titleBar = this.FindControl<Grid>("TitleBar") ?? throw new InvalidOperationException("TitleBar not found");
        titleLabel = this.FindControl<Label>("TitleLabel") ?? throw new InvalidOperationException("TitleLabel not found");
        messageTextBlock = this.FindControl<SelectableTextBlock>("MessageTextBlock") ?? throw new InvalidOperationException("MessageTextBlock not found");
        markDownScrollViewer = this.FindControl<MarkdownScrollViewer>("MarkDownScrollViewer") ?? throw new InvalidOperationException("MarkDownScrollViewer not found");

        bool UseSystemTitle = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
        if (UseSystemTitle)
        {
            titleBar.Height = 0;
            Height -= 40;
        }

        titleLabel.Content = "Update Available".Tr(TC.Dialog);
    }

    public void SetMessage(string message)
    {
        messageTextBlock.Text = message;
    }

    public void SetMDMessage(string message)
    {
        markDownScrollViewer.Markdown = message;
    }

    public Button AddButton(string text, ButtonType type, double width = 96)
    {
        // 创建按钮控件
        var button = new Button() { MinWidth = width, Height = 40 };

        if (type == ButtonType.Primary)
        {
            button.AddContent(new()
            {
                Item = new BorderItem() { CornerRadius = 6 },
                ColorSet = new() { Color = Style.BUTTON_PRIMARY, HoveredColor = Style.BUTTON_PRIMARY_HOVER }
            });
        }
        else if (type == ButtonType.Normal)
        {
            button.AddContent(new()
            {
                Item = new BorderItem() { CornerRadius = 6 },
                ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER }
            });
        }
        button.AddContent(new()
        {
            Item = new TextItem() { Text = text },
            ColorSet = new() { Color = type == ButtonType.Primary ? Colors.White : Style.LIGHT_WHITE }
        });
        button.Clicked += Close;

        // 创建装载按钮的容器，这里使用 StackPanel 包裹按钮
        // （当该按钮处于左右对齐时，StackPanel 会按容器宽度固定显示）
        var container = new StackPanel()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Height = 40,
            Margin = new Thickness(0)
        };
        container.Children.Add(button);
        ButtonsPanel.Children.Add(container);

        // 更新 ButtonsPanel 的 ColumnDefinitions
        int count = ButtonsPanel.Children.Count;
        ButtonsPanel.ColumnDefinitions.Clear();

        if (count == 1)
        {
            // 只有一个按钮时，居中显示
            ButtonsPanel.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            ButtonsPanel.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            ButtonsPanel.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            // 将唯一的按钮容器放在中间列（索引 1）
            Grid.SetColumn(ButtonsPanel.Children[0], 1);
            (ButtonsPanel.Children[0] as StackPanel).HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        }
        else if (count == 2)
        {
            // 两个按钮时，左侧和右侧贴边
            ButtonsPanel.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });         // 左按钮
            ButtonsPanel.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) }); // 间隔
            ButtonsPanel.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });         // 右按钮

            // 重新分配已有的按钮容器位置
            Grid.SetColumn(ButtonsPanel.Children[0], 0); // 第一个按钮放左侧
            Grid.SetColumn(ButtonsPanel.Children[1], 2); // 第二个按钮放右侧

            // 设置左右的对齐方式（可选，根据你期望的效果）
            (ButtonsPanel.Children[0] as StackPanel).HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            (ButtonsPanel.Children[1] as StackPanel).HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        }
        else if (count >= 3)
        {
            // 当有3个或3个以上按钮时，这里采用常见布局：
            // 左侧按钮（Auto）、中间按钮（*）、右侧按钮（Auto）
            // （如果有多于3个按钮，如何布局需要你根据实际需求做调整）
            ButtonsPanel.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });         // 左侧按钮
            ButtonsPanel.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) }); // 中间按钮
            ButtonsPanel.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });         // 右侧按钮

            // 假设前三个按钮分别放在上述三列
            // 如有多余，可以考虑将后续按钮放在中间列或者采用其他策略
            Grid.SetColumn(ButtonsPanel.Children[0], 0);
            Grid.SetColumn(ButtonsPanel.Children[1], 1);
            Grid.SetColumn(ButtonsPanel.Children[2], 2);

            // 设置各自的对齐方式
            (ButtonsPanel.Children[0] as StackPanel).HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            (ButtonsPanel.Children[1] as StackPanel).HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            (ButtonsPanel.Children[2] as StackPanel).HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        }

        return button;
    }

}

