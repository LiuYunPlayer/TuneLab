using Avalonia;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using TuneLab.Configs;
using TuneLab.Foundation.Utils;
using TuneLab.I18N;
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
        // init logger
        Log.SetupLogger(new FileLogger(Path.Combine(PathManager.LogsFolder, "TuneLab_" + DateTime.Now.ToString("yyyy-MM-dd_hh-mm-ss") + ".log")));
        Log.Info("Version: " + AppInfo.Version);

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
            Process.GetCurrentProcess().Kill();
            Process.GetCurrentProcess().WaitForExit();
            return;
        }

        // init setting
        Settings.Init(PathManager.SettingsFilePath);

        // init translation
        TranslationManager.Init(PathManager.TranslationsFolder);
        TranslationManager.CurrentLanguage.Value = TranslationManager.Languages.Contains(Settings.Language.Value) ? Settings.Language : TranslationManager.GetCurrentOSLanguage();
        Settings.Language.Modified.Subscribe(() => TranslationManager.CurrentLanguage.Value = Settings.Language);

        // event loop
        BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

        // exit
        lockFile.Dispose();
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
                FontFallbacks =
                [
                    (Settings.Language.Value == "ja-JP") ?
                        new FontFallback() { FontFamily = "Yu Gothic UI" } :
                        new FontFallback() { FontFamily = "Microsoft YaHei" },
                ]
            });
    }
}
