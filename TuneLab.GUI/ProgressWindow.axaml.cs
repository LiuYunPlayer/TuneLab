using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using TuneLab.GUI.Components;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;

namespace TuneLab.GUI;

// 安装器与主程序共用的轻量进度窗（下载/更新/重启等）。无标题栏、整窗可拖动，
// 可选的取消按钮位于底部。宿主通过 SetTitle/SetStatus/SetProgress/SetIndeterminate 驱动。
public partial class ProgressWindow : Window
{
    readonly TextBlock mTitle;
    readonly TextBlock mStatus;
    readonly ProgressBar mBar;
    readonly Panel mCancelSlot;
    Button? mCancel;

    /// <summary>取消按钮被点击（仅在调用过 ShowCancel 后可能触发）。</summary>
    public event Action? CancelRequested;

    public ProgressWindow()
    {
        InitializeComponent();
        CanResize = false;
        Topmost = true;
        Background = Style.BACK.ToBrush();

        mTitle = this.FindControl<TextBlock>("TitleText")!;
        mStatus = this.FindControl<TextBlock>("StatusText")!;
        mBar = this.FindControl<ProgressBar>("Bar")!;
        mCancelSlot = this.FindControl<Panel>("CancelSlot")!;

        mTitle.Foreground = Style.TEXT_LIGHT.ToBrush();
        mStatus.Foreground = Style.LIGHT_WHITE.ToBrush();
        mBar.Foreground = Style.HIGH_LIGHT.ToBrush();

        // 整窗可拖动；点在按钮上时不拖，避免吞掉按钮点击。
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null) return;
            BeginMoveDrag(e);
        };
    }

    public void SetTitle(string text) => mTitle.Text = text;
    public void SetStatus(string text) => mStatus.Text = text;

    public void SetProgress(double fraction)
    {
        mBar.IsIndeterminate = false;
        mBar.Value = fraction;
    }

    public void SetIndeterminate() => mBar.IsIndeterminate = true;

    /// <summary>显示底部取消按钮（点击触发 CancelRequested）。</summary>
    public void ShowCancel(string text)
    {
        if (mCancel == null)
        {
            mCancel = new Button { Height = 30, MinWidth = 80 };
            mCancel.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER } });
            mCancel.AddContent(new() { Item = new TextItem() { Text = text }, ColorSet = new() { Color = Style.LIGHT_WHITE } });
            mCancel.Clicked += () => CancelRequested?.Invoke();
            mCancelSlot.Children.Add(mCancel);
        }
        mCancel.IsVisible = true;
    }

    public void SetCancelEnabled(bool enabled)
    {
        if (mCancel != null) mCancel.IsEnabled = enabled;
    }
}
