using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TuneLab.Animation;
using TuneLab.Setup.Views;

namespace TuneLab.Setup;

public partial class App : Application
{
    // 由 Program 按运行模式设定：交互安装用向导，-update 用进度窗。默认向导。
    public static Func<Window>? MainWindowFactory;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // i18n 必须在此初始化：此时 Avalonia 已就绪，AssetLoader 才能读内嵌的翻译 toml
        // （放在 Program.Main 里、Avalonia 起来前调用会静默失败、整个安装器退回英文）。
        SetupI18N.Init();

        // 驱动 GUI 控件的补间动画（按钮 hover 淡入淡出、勾选框动画）。必须在 UI 线程、
        // 有 SynchronizationContext 时调用（与主程序 App 一致）。
        AnimationManager.SharedManager.Init();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = MainWindowFactory?.Invoke() ?? new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
