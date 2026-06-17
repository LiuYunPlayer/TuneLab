using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;

namespace TuneLab.GUI;

internal partial class Dialog : Window
{
    public enum ButtonType
    {
        Primary,
        Normal
    }

    private Grid titleBar;
    private Label titleLabel;
    private SelectableTextBlock messageTextBlock;

    double mBaseHeight;
    double mMessageHeight;

    public Dialog()
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
	
        bool UseSystemTitle = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
	    if(UseSystemTitle){
		    titleBar.Height = 0;
		    Height -= 40;
	    }

        // 默认窗高（消息区 108 基线）作为下限——消息短时不缩小。
        mBaseHeight = Height;

        messageTextBlock.SizeChanged += (s, e) => {
            mMessageHeight = e.NewSize.Height;
            if (e.NewSize.Height > 108)
                MessageStackPanel.Margin = new Thickness(12, 16);
            AdjustHeight();
        };
        // 首次 SizeChanged 多发生在 Show 之前（此时拿不到所在屏幕），故 Opened 后按真实屏幕再封顶一次。
        Opened += (s, e) => AdjustHeight();
    }

    // 按消息高度撑高窗口，但**封顶到所在屏幕可用高的 85%**；超出部分由消息区 ScrollViewer 滚动，
    // 保证底部按钮行（固定 56 行）永远在屏幕内、不被挤出。
    void AdjustHeight()
    {
        double chrome = titleBar.Height + 56 /* 按钮行 */ + 32 /* 上下留白 */;
        double desired = mMessageHeight + chrome;

        double maxHeight = 600;
        var screen = Screens?.ScreenFromVisual(this);
        if (screen != null)
            maxHeight = screen.WorkingArea.Height / screen.Scaling * 0.85;

        Height = Math.Min(Math.Max(desired, mBaseHeight), Math.Max(mBaseHeight, maxHeight));
    }

    public void SetTitle(string title)
    {
        titleLabel.Content = title;
        Title = title + " - TuneLab";
    }

    public void SetMessage(string message)
    {
        messageTextBlock.Text = message;
    }

    public void SetTextAlignment(TextAlignment alignment)
    {
        messageTextBlock.TextAlignment = alignment;
    }

    public Button AddButton(string text, ButtonType type)
    {
        ButtonsPanel.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
        var button = new Button() { MinWidth = 96, Height = 40 };

        if (type == ButtonType.Primary)
            button.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { Color = Style.BUTTON_PRIMARY, HoveredColor = Style.BUTTON_PRIMARY_HOVER } });

        if (type == ButtonType.Normal)
            button.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER } });

        button.AddContent(new() { Item = new TextItem() { Text = text }, ColorSet = new() { Color = type == ButtonType.Primary ? Colors.White : Style.LIGHT_WHITE } });

        button.Clicked += Close;
        var buttonStack = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Children = { button }, Height = 40, Margin = new(0) };

        Grid.SetColumn(buttonStack, ButtonsPanel.ColumnDefinitions.Count - 1);
        ButtonsPanel.Children.Add(buttonStack);

        return button;
    }
}
