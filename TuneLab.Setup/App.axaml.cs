using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TuneLab.Animation;
using TuneLab.Setup.Views;

namespace TuneLab.Setup;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 驱动 GUI 控件的补间动画（按钮 hover 淡入淡出、勾选框动画）。必须在 UI 线程、
        // 有 SynchronizationContext 时调用（与主程序 App 一致）。
        AnimationManager.SharedManager.Init();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
