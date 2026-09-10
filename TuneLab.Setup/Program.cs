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

        // 带参数启动时先把 stdout/stderr 接回父控制台——GUI 子系统的 exe 默认没有控制台，
        // 用法错与失败原因不接回去就没人看得见（见 ConsoleBridge）。
        if (args.Length > 0 && OperatingSystem.IsWindows())
            ConsoleBridge.AttachIfPossible();

        if (options.Error is { } usageError)
        {
            Console.Error.WriteLine("TuneLab setup: " + usageError);
            Console.Error.WriteLine("Run with -help to see the options.");
            return 2;
        }

        if (options.Help)
        {
            Console.Out.Write(CliOptions.Usage(SetupI18N.SupportedLanguages));
            return 0;
        }

        // 无界面的三档（卸载 / 静默安装 / 更新的文件部分）都不起 Avalonia。
        if (options.Mode is SetupMode.Uninstall or SetupMode.Silent)
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
