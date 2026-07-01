using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using TuneLab.Utils;
using TuneLab.I18N;

namespace TuneLab.GUI;

internal partial class ExportDialog : Window
{
    private Grid titleBar;
    private Label titleLabel;
    private TextBlock messageTextBlock;
    private Border progressBarFill;
    private Grid progressBarContainer;
    private TextBlock statusTextBlock;
    private double mProgress = 0;

    public ExportDialog()
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
        messageTextBlock = this.FindControl<TextBlock>("MessageTextBlock") ?? throw new InvalidOperationException("MessageTextBlock not found");
        progressBarFill = this.FindControl<Border>("ProgressBarFill") ?? throw new InvalidOperationException("ProgressBarFill not found");
        progressBarContainer = this.FindControl<Grid>("ProgressBarContainer") ?? throw new InvalidOperationException("ProgressBarContainer not found");
        statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock") ?? throw new InvalidOperationException("StatusTextBlock not found");

        bool UseSystemTitle = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
        if (UseSystemTitle)
        {
            titleBar.Height = 0;
            Height -= 40;
        }

        titleLabel.Content = "Export".Tr(TC.Dialog);
        messageTextBlock.Text = "Exporting...".Tr(TC.Dialog);

        progressBarContainer.SizeChanged += (s, e) => UpdateProgressBarWidth();
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

    public void SetStatus(string status)
    {
        statusTextBlock.Text = status;
    }

    public void SetProgress(double progress)
    {
        mProgress = Math.Clamp(progress, 0, 1);
        UpdateProgressBarWidth();
    }

    private void UpdateProgressBarWidth()
    {
        var totalWidth = progressBarContainer.Bounds.Width;
        if (totalWidth > 0)
        {
            progressBarFill.Width = totalWidth * mProgress;
        }
    }
}
