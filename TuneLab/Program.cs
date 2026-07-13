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

        // 异步缓冲日志需在退出 / 崩溃时刷盘，否则丢失未落盘日志。崩溃时顺带记下异常（自动归因到肇事插件）。
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.Shutdown();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log.ErrorAttributed("Unhandled exception", ex);
            else
                Log.Error("Unhandled exception: " + e.ExceptionObject);
            Log.Shutdown();
        };

        // 无人 await 的 Task 抛异常不会崩进程，异常会被 GC 静默吞掉——记下堆栈并标记已观察，避免无声丢失。
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.ErrorAttributed("Unobserved task exception", e.Exception); e.SetObserved(); };

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
        ParameterPinning.Init(PathManager.ParameterPinsFilePath);
        TuneLab.Input.Keymap.Init(PathManager.KeybindingsFilePath);

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
                // 用户自选界面字体优先作默认家族；未选则留空走 Inter（拉丁/西里尔/希腊），非拉丁脚本靠下方回退链兜底。
                DefaultFamilyName = string.IsNullOrWhiteSpace(Settings.InterfaceFontFamily.Value) ? null : Settings.InterfaceFontFamily.Value,
                FontFallbacks = BuildFontFallbacks(Settings.Language.Value),
            });
    }

    // 覆盖全球主要脚本的有序回退链（每 OS 一套，只引用系统已装字体名、不分发任何字体文件）。
    // Skia 后端另有字形级系统回退（SKFontManager.MatchCharacter）作最终兜底；本链的作用是「控制选中哪个字体」——
    // 尤其汉字统一（中/日/韩同码位字形不同）：把与界面语言匹配的 CJK 字体排在链首，避免系统按 OS 语言猜错地区字形。
    static FontFallback[] BuildFontFallbacks(string language)
    {
        // 界面语言对应的首选 CJK（放在 CJK 段最前，解汉字统一歧义）。
        static string[] PreferredCjk(string lang) => lang switch
        {
            "ja-JP" => OperatingSystem.IsWindows() ? ["Yu Gothic UI"] : OperatingSystem.IsMacOS() ? ["Hiragino Sans"] : ["Noto Sans CJK JP"],
            "ko-KR" => OperatingSystem.IsWindows() ? ["Malgun Gothic"] : OperatingSystem.IsMacOS() ? ["Apple SD Gothic Neo"] : ["Noto Sans CJK KR"],
            "zh-TW" or "zh-HK" or "zh-Hant" => OperatingSystem.IsWindows() ? ["Microsoft JhengHei"] : OperatingSystem.IsMacOS() ? ["PingFang TC"] : ["Noto Sans CJK TC"],
            _ => OperatingSystem.IsWindows() ? ["Microsoft YaHei"] : OperatingSystem.IsMacOS() ? ["PingFang SC"] : ["Noto Sans CJK SC"],
        };

        // 各 OS 覆盖「拉丁+其余脚本」的字体名（系统预装；顺序 = 拉丁/西里尔/希腊/阿拉伯/希伯来 → 全部 CJK → 印度系 → 泰）。
        string[] families = OperatingSystem.IsWindows()
            ? ["Segoe UI", "Microsoft YaHei", "Microsoft JhengHei", "Yu Gothic UI", "Malgun Gothic", "Nirmala UI", "Leelawadee UI"]
            : OperatingSystem.IsMacOS()
            ? ["PingFang SC", "PingFang TC", "Hiragino Sans", "Apple SD Gothic Neo"]  // mac 系统回退很全，其余交给 Skia
            : ["Noto Sans", "Noto Sans CJK SC", "Noto Sans CJK TC", "Noto Sans CJK JP", "Noto Sans CJK KR", "Noto Sans Devanagari", "Noto Sans Thai", "Noto Sans Arabic", "Noto Sans Hebrew"];

        // 语言首选 CJK 提到最前、去重，其余按上表顺序补齐。
        var ordered = new List<string>();
        foreach (var name in PreferredCjk(language)) ordered.Add(name);
        foreach (var name in families) if (!ordered.Contains(name)) ordered.Add(name);
        return ordered.Select(name => new FontFallback() { FontFamily = name }).ToArray();
    }
}
