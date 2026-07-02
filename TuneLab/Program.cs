using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using TuneLab.Foundation;
using TuneLab.Configs;
using TuneLab.Extensions;
using TuneLab.I18N;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // 开发环境（VS Code 集成终端/agent shell 等）会给子进程注入 NoDefaultCurrentDirectoryInExePath=1，
        // 置位后 cmd 不再从工作目录解析可执行文件，会破坏经 cmd 相对路径拉起辅助进程的插件
        // （子进程环境继承自宿主）。清掉它，让任意启动方式下插件子进程环境与桌面双击启动一致；
        // 只影响本进程及子进程，不改系统设置。
        Environment.SetEnvironmentVariable("NoDefaultCurrentDirectoryInExePath", null);

        // init logger
        Log.SetupLogger(new FileLogger(PathManager.LogFilePath));
        Log.Info("Version: " + AppInfo.Version);

        // 异步缓冲日志需在退出 / 崩溃时刷盘，否则丢失未落盘日志。崩溃时顺带记下异常。
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.Shutdown();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { Log.Error("Unhandled exception: " + e.ExceptionObject); Log.Shutdown(); };

        // check if other instance is running
        var lockFile = LockFile.Create(PathManager.LockFilePath);
        if (lockFile == null)
        {
            try
            {
                using var pipeClient = new NamedPipeClientStream(".", "TuneLab", PipeDirection.Out);
                pipeClient.Connect(1000);

                using var writer = new StreamWriter(pipeClient);
                foreach (var arg in args)
                {
                    writer.WriteLine(arg);
                    Log.Info($"Sent arguments to running instance: {arg}");
                }
                writer.Flush();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to send arguments to running instance: {ex}");
            }
            Log.Info("Another instance is running, exiting.");
            Log.Shutdown();   // 硬杀前刷盘：Kill 不触发 ProcessExit，否则上面这些日志会丢。
            Process.GetCurrentProcess().Kill();
            Process.GetCurrentProcess().WaitForExit();
            return;
        }

        // init setting
        Settings.Init(PathManager.SettingsFilePath);
        EditorState.Init(PathManager.EditorStateFilePath);
        RecentSoundSourceManager.Init(PathManager.RecentSoundSourcesFilePath);

        // init translation
        TranslationManager.Init(PathManager.TranslationsFolder);
        TranslationManager.CurrentLanguage.Value = TranslationManager.Languages.Contains(Settings.Language.Value) ? Settings.Language : TranslationManager.GetCurrentOSLanguage();
        Settings.Language.Modified.Subscribe(() => TranslationManager.CurrentLanguage.Value = Settings.Language);

        // 注入插件可读的全局 host context（语言 + 按 ALC 自动前缀的日志器）。须在加载插件之前。
        TuneLabContext.Global = new TuneLabContextGlobal();

        // event loop
        BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

        // exit
        lockFile.Dispose();
        Log.Shutdown();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.SvgImageExtension).Assembly);
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.Svg).Assembly);
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseSkia()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI()
            .With(new FontManagerOptions()
            {
                DefaultFamilyName = Settings.Language.Value == "ja-JP" ? JapaneseUIFontFamilyName() : null,
                FontFallbacks =
                [
                    (Settings.Language.Value == "ja-JP") ?
                        OperatingSystem.IsWindows() ?
                            new FontFallback() { FontFamily = "Yu Gothic UI" } :
                        OperatingSystem.IsMacOS() ?
                            new FontFallback() { FontFamily = JapaneseUIFontFamilyName() } :
                            new FontFallback() { FontFamily = "Noto Sans CJK JP" } :
                        new FontFallback() { FontFamily = "Microsoft YaHei" },
                ]
            });
    }

    static string JapaneseUIFontFamilyName()
    {
        return OperatingSystem.IsWindows() ? "Yu Gothic UI" :
            OperatingSystem.IsMacOS() ? "Hiragino Sans" :
            "Noto Sans CJK JP";
    }
}
