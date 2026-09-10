using System;
using System.IO;
using System.Threading;
using TuneLab.Setup.Core;

namespace TuneLab.Setup;

/// <summary>
/// 无界面入口：静默安装（`-silent`）、卸载（"添加或删除程序"）与 App 自更新。
/// 输出经 ConsoleBridge 接回父控制台（有的话），同时把过程与异常写到 %temp%\TuneLab.Setup.log
/// ——双击运行或被 GUI 拉起时没有控制台可写，那份日志是唯一的线索。
/// </summary>
internal static class SilentRunner
{
    static readonly string LogPath = Path.Combine(Path.GetTempPath(), "TuneLab.Setup.log");

    static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}"); } catch { }
    }

    // 语言与向导右上角那个下拉同义：写回主程序设置里的 Language（安装完 TuneLab 首次启动就用它）。
    // 认不出的语言码不静默吞掉——那会让人以为设上了。
    // 【只认不写】校验与写盘拆成两步：用法错要在动任何东西之前就退，而写盘必须等到安装之后（见 Install）。
    static bool TryResolveLanguage(CliOptions options, out string? language)
    {
        language = null;
        if (options.Language is not { Length: > 0 } requested)
            return true;

        var match = SetupI18N.Match(requested);
        if (match == null)
        {
            Console.Error.WriteLine($"TuneLab setup: unknown language \"{requested}\". Known: {string.Join(" ", SetupI18N.SupportedLanguages)}");
            Log($"Unknown language '{requested}'.");
            return false;
        }

        language = match;
        return true;
    }

    /// <summary>
    /// <c>-uninstall</c> 不带目录时卸载哪一份：卸载器自己所在的那一份。
    ///
    /// 注册表里的卸载入口总是显式带目录，正路走不到这里；手敲 <c>TuneLab.Setup.exe -uninstall</c> 的人
    /// 指的显然是手边这一份，而手边这一份未必装在默认目录（<c>-dir</c> 装到别处就不是）。回落到默认目录
    /// 会去动另一份安装。旁边没有主程序（例如自复制到临时目录的那个副本）才退回默认目录。
    /// </summary>
    static string SelfInstallDir()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        return dir is { Length: > 0 } && File.Exists(Path.Combine(dir, ProductInfo.ExecutableName))
            ? dir
            : ProductInfo.DefaultInstallDir;
    }

    static void Report(string message)
    {
        Console.Out.WriteLine(message);
        Log(message);
    }

    static int Install(CliOptions options)
    {
        if (!TryResolveLanguage(options, out var language))
            return 2;

        var installDir = options.TargetDir ?? ProductInfo.DefaultInstallDir;
        var installer = new Installer(new InstallOptions
        {
            InstallDir = installDir,
            // 无人值守：TuneLab 开着就等一小会儿，然后如实报错退出——这里没人会去关它，
            // 一直等等于让调用方的脚本挂死（实测踩过：一条静默安装挂了十分钟，日志停在上一行）。
            WaitForAppExitTimeout = TimeSpan.FromSeconds(20),
            CreateDesktopShortcut = options.DesktopShortcut,
            CreateStartMenuShortcut = options.StartMenuShortcut,
            RegisterFileAssociations = options.FileAssociations,
            LaunchAfterInstall = options.LaunchAfterInstall,
            IsUpdate = false,
        });

        var progress = new Progress<InstallStatus>(s => Log($"{s.Fraction:P0} {s.Message}"));
        installer.InstallAsync(progress, CancellationToken.None).GetAwaiter().GetResult();
        Log("Install done: " + installDir);
        Console.Out.WriteLine("TuneLab installed to " + installDir);

        // 语言写在安装之后，理由与向导那边同一条：安装时 TuneLab 若正开着，它退出时会用自己内存里的
        // 旧语言写一次 Settings.json，把先写进去的覆盖掉。InstallAsync 内部已等到锁释放（那次覆盖已
        // 落盘），此刻再写才压得住。写在前面时 -language 在"装的时候 TuneLab 开着"这一路上静默失效。
        if (language != null)
        {
            UserSettings.WriteLanguage(language);
            Log($"Language set to {language}.");
        }

        // 「立即启动」也照向导来（默认开）。无人值守跑的人给 -launch false 即可。
        if (options.LaunchAfterInstall)
        {
            installer.Launch();
            Log("Launched.");
        }
        return 0;
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
                    // 卸载的每一行结论都同时给控制台和日志：从"添加或删除程序"点进来时没有控制台，
                    // 那份日志是"到底删了什么、留下了什么"的唯一去处。
                    Uninstaller.Run(options.TargetDir ?? SelfInstallDir(), Report);
                    Log("Uninstall done.");
                    return 0;

                // 静默安装：向导那条路的等价物——同一个 Installer、同一批选项，只是选项来自命令行
                // 而不是几个复选框，缺省值与向导初始值一致（见 CliOptions）。
                case SetupMode.Silent:
                    return Install(options);

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
            // 控制台给人话，日志留全文（含堆栈）——两边的读者不同。
            Console.Error.WriteLine("TuneLab setup: " + ex.Message);
            Log("FAILED: " + ex);
            return 1;
        }
    }
}
