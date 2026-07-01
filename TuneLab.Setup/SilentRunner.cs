using System;
using System.Threading;
using TuneLab.Setup.Core;

namespace TuneLab.Setup;

/// <summary>
/// 无界面入口：供"添加或删除程序"（卸载）与 App 自更新（更新）调用。
/// 更新模式在本阶段仅作最简静默安装占位，完整自更新握手留待专题接入。
/// </summary>
internal static class SilentRunner
{
    public static int Run(CliOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Silent mode is Windows-only.");
            return 1;
        }

        try
        {
            switch (options.Mode)
            {
                case SetupMode.Uninstall:
                    Uninstaller.Run(options.TargetDir ?? ProductInfo.DefaultInstallDir);
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
                    installer.Launch();
                    return 0;

                default:
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Silent operation failed: " + ex);
            return 1;
        }
    }
}
