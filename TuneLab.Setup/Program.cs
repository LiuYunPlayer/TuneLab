using System;
using Avalonia;

namespace TuneLab.Setup;

internal static class Program
{
    // Avalonia 初始化前不要用任何依赖 SynchronizationContext 的 API。
    [STAThread]
    public static int Main(string[] args)
    {
        var options = CliOptions.Parse(args);

        // 卸载：无界面静默执行。
        if (options.Mode == SetupMode.Uninstall)
            return SilentRunner.Run(options);

        // i18n 在 App.OnFrameworkInitializationCompleted 里初始化（需 Avalonia 起来后 AssetLoader 才能读内嵌 toml）。

        // -update：显示可视进度窗（填住主程序退出→覆盖→重启之间的空白，避免像崩溃）。
        if (options.Mode == SetupMode.Update)
        {
            var dir = options.TargetDir ?? Core.ProductInfo.DefaultInstallDir;
            App.MainWindowFactory = () => UpdateRunner.CreateWindow(dir);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Avalonia 设计器与运行时共用的应用构造入口。
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
