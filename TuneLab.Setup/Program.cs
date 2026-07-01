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

        // 静默模式（供 App 自更新 / 卸载入口调用），不拉起 GUI。
        // 本阶段仅搭交互安装器骨架，静默模式留待自更新专题接入。
        if (options.Mode != SetupMode.Interactive)
            return SilentRunner.Run(options);

        // i18n 需在建窗前就位（向导文案在构造时解析）。
        SetupI18N.Init();
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
