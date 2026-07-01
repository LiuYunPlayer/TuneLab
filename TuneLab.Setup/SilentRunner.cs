using System;
using System.IO;
using System.Threading;
using TuneLab.Setup.Core;

namespace TuneLab.Setup;

/// <summary>
/// 无界面入口：供"添加或删除程序"（卸载）与 App 自更新（更新）调用。
/// 无控制台，故把过程与异常写到 %temp%\TuneLab.Setup.log 供排查。
/// </summary>
internal static class SilentRunner
{
    static readonly string LogPath = Path.Combine(Path.GetTempPath(), "TuneLab.Setup.log");

    static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}"); } catch { }
    }

    public static int Run(CliOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Silent mode is Windows-only.");
            return 1;
        }

        Log($"--- {options.Mode} target='{options.TargetDir}' ---");
        try
        {
            switch (options.Mode)
            {
                case SetupMode.Uninstall:
                    Uninstaller.Run(options.TargetDir ?? ProductInfo.DefaultInstallDir);
                    Log("Uninstall done.");
                    return 0;

                case SetupMode.Update:
                    var opts = new InstallOptions
                    {
                        InstallDir = options.TargetDir ?? ProductInfo.DefaultInstallDir,
                        LaunchAfterInstall = false,
                        IsUpdate = true,
                    };
                    var installer = new Installer(opts);
                    installer.InstallAsync(null, CancellationToken.None).GetAwaiter().GetResult();
                    Log("Update files applied, launching app.");
                    installer.Launch();
                    Log("Update done.");
                    return 0;

                default:
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Silent operation failed: " + ex);
            Log("FAILED: " + ex);
            return 1;
        }
    }
}
