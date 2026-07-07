using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using TuneLab.GUI;
using TuneLab.I18N;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;

namespace TuneLab.Dialogs;

// 独立的"关于"窗口。分区式布局，便于后续增补内容（致谢、构建信息、许可等）。
internal partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Background = Style.BACK.ToBrush();

        var content = this.FindControl<ContentControl>("Content")!;
        var titleBar = this.FindControl<Grid>("TitleBar")!;
        var titleLabel = this.FindControl<Label>("TitleLabel")!;
        var closeSlot = this.FindControl<Panel>("CloseSlot")!;
        var versionText = this.FindControl<TextBlock>("VersionText")!;
        var tagline = this.FindControl<TextBlock>("Tagline")!;
        var linksPanel = this.FindControl<StackPanel>("LinksPanel")!;

        content.Background = Style.INTERFACE.ToBrush();

        // Linux 用系统标题栏，藏掉自绘的。
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            titleBar.Height = 0;

        titleLabel.Content = "About TuneLab".Tr(TC.Dialog);
        versionText.Foreground = Style.HIGH_LIGHT.ToBrush();
        versionText.Text = "Version".Tr(TC.Dialog) + " " + AppInfo.VersionString;
        tagline.Foreground = Style.LIGHT_WHITE.ToBrush();
        tagline.Text = "An open, extensible singing voice synthesis editor.".Tr(TC.Dialog);

        // 标题栏关闭按钮
        var close = new Button { Width = 46, Height = 40 };
        close.AddContent(new() { Item = new BorderItem(), ColorSet = new() { Color = Style.TRANSPARENT, HoveredColor = Style.SYNTHESIS_FAILED } });
        close.AddContent(new() { Item = new IconItem() { Icon = Assets.WindowClose }, ColorSet = new() { Color = Style.LIGHT_WHITE, HoveredColor = Colors.White } });
        close.Clicked += Close;
        closeSlot.Children.Add(close);

        // 链接（点击只打开网页、不关闭窗口）
        AddLink(linksPanel, "Forum", "https://forum.tunelab.app");
        AddLink(linksPanel, "GitHub", "https://github.com/LiuYunPlayer/TuneLab");

        // 拖动窗口——点在标题栏内的按钮上时不拖，避免吞掉按钮点击。
        titleBar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null) return;
            BeginMoveDrag(e);
        };
    }

    static void AddLink(StackPanel panel, string text, string url)
    {
        var b = new Button { Height = 32, MinWidth = 84 };
        b.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER } });
        b.AddContent(new() { Item = new TextItem() { Text = text }, ColorSet = new() { Color = Style.LIGHT_WHITE } });
        b.Clicked += () => ProcessHelper.OpenUrl(url);
        panel.Children.Add(b);
    }
}
