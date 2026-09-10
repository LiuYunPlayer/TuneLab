using System;
using System.Collections.Generic;
using System.Text;

namespace TuneLab.Setup;

internal enum SetupMode
{
    /// <summary>默认：带向导界面的首次安装。</summary>
    Interactive,
    /// <summary>无界面安装（`-silent`）：向导上能选的每一项都有对应参数，默认值与向导一致。</summary>
    Silent,
    /// <summary>静默更新（无界面），供 App 自更新调用。</summary>
    Update,
    /// <summary>静默卸载，供"添加或删除程序"入口调用。</summary>
    Uninstall,
}

/// <summary>
/// 命令行解析结果。
///
/// 【口径】命令行是**向导的完整镜像**：向导上能勾的每一项这里都有对应参数，且**缺省值与向导的初始值
/// 一致**（都建快捷方式、关联文件类型、装完启动）。少一项就会逼着自动化的人回去点界面，而默认值两边
/// 不一致则会让"照着界面理解的行为"在脚本里变成另一回事。
///
/// 约定：
///   (无参)                      → 交互安装（向导）
///   -silent [选项…]             → 无界面安装
///   -update &lt;targetDir&gt;   → 静默更新到指定目录（App 自更新用：不重建快捷方式，装完拉起应用）
///   -uninstall &lt;targetDir&gt; → 静默卸载指定目录
///   -help                       → 打印用法
/// </summary>
internal sealed class CliOptions
{
    public SetupMode Mode { get; private set; } = SetupMode.Interactive;
    public string? TargetDir { get; private set; }
    public bool Help { get; private set; }

    // 向导上那四个复选框，缺省与向导初始值一致（见 Views/MainWindow.axaml.cs 的 BuildOptions）。
    public bool DesktopShortcut { get; private set; } = true;
    public bool StartMenuShortcut { get; private set; } = true;
    public bool FileAssociations { get; private set; } = true;
    public bool LaunchAfterInstall { get; private set; } = true;

    /// <summary>向导右上角那个语言下拉的等价物：写回主程序设置里的 Language。null = 不动。</summary>
    public string? Language { get; private set; }

    /// <summary>用法错的原因（英文，供命令行打印）。null = 解析成功。</summary>
    public string? Error { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var result = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i].ToLowerInvariant();
            switch (arg)
            {
                case "-update":
                    result.Mode = SetupMode.Update;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        result.TargetDir = args[++i];
                    break;
                case "-uninstall":
                    result.Mode = SetupMode.Uninstall;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        result.TargetDir = args[++i];
                    break;
                case "-silent":
                    // 卸载/更新本就无界面，故 -silent 只把【交互安装】切成无界面的那一档。
                    if (result.Mode == SetupMode.Interactive)
                        result.Mode = SetupMode.Silent;
                    break;
                case "-dir":
                    if (!TryTakeValue(args, ref i, arg, out var dir, result))
                        return result;
                    result.TargetDir = dir;
                    break;
                case "-desktop-shortcut":
                    if (!TryTakeBool(args, ref i, arg, out var desktop, result))
                        return result;
                    result.DesktopShortcut = desktop;
                    break;
                case "-start-menu-shortcut":
                    if (!TryTakeBool(args, ref i, arg, out var startMenu, result))
                        return result;
                    result.StartMenuShortcut = startMenu;
                    break;
                case "-file-assoc":
                    if (!TryTakeBool(args, ref i, arg, out var assoc, result))
                        return result;
                    result.FileAssociations = assoc;
                    break;
                case "-launch":
                    if (!TryTakeBool(args, ref i, arg, out var launch, result))
                        return result;
                    result.LaunchAfterInstall = launch;
                    break;
                case "-language":
                    if (!TryTakeValue(args, ref i, arg, out var language, result))
                        return result;
                    result.Language = language;
                    break;
                case "-help":
                case "--help":
                case "-h":
                case "/?":
                    result.Help = true;
                    break;
                default:
                    // 认不出的参数【不静默忽略】：被忽略的那个参数正是调用方以为生效了的东西。
                    result.Error = $"unknown option \"{args[i]}\".";
                    return result;
            }
        }
        return result;
    }

    static bool TryTakeValue(string[] args, ref int i, string name, out string value, CliOptions result)
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
        {
            value = string.Empty;
            result.Error = $"{name} needs a value.";
            return false;
        }
        value = args[++i];
        return true;
    }

    // 布尔取值式而不是 -no-xxx 开关式：这些参数镜像的是向导上的复选框，写 true/false 与那个勾一一对应，
    // 且脚本里读得出意图（"这次特意不建桌面快捷方式"），不必记住哪些有 -no- 前缀。
    static bool TryTakeBool(string[] args, ref int i, string name, out bool value, CliOptions result)
    {
        value = false;
        if (!TryTakeValue(args, ref i, name, out var raw, result))
            return false;

        switch (raw.ToLowerInvariant())
        {
            case "true" or "yes" or "1" or "on":
                value = true;
                return true;
            case "false" or "no" or "0" or "off":
                value = false;
                return true;
            default:
                result.Error = $"{name} takes true or false, not \"{raw}\".";
                return false;
        }
    }

    public static string Usage(IReadOnlyList<string> languages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TuneLab installer.");
        sb.AppendLine();
        sb.AppendLine("  (no arguments)              install with the wizard");
        sb.AppendLine("  -silent [options]           install without a window; the options below mirror the wizard");
        sb.AppendLine("  -uninstall <dir>            uninstall the copy installed in <dir>");
        sb.AppendLine("  -help                       this text");
        sb.AppendLine();
        sb.AppendLine("Options for -silent (defaults are what the wizard starts with):");
        sb.AppendLine("  -dir <path>                 where to install                    (default: the per-user Programs folder)");
        sb.AppendLine("  -desktop-shortcut <bool>    create a desktop shortcut            (default: true)");
        sb.AppendLine("  -start-menu-shortcut <bool> create a Start Menu shortcut         (default: true)");
        sb.AppendLine("  -file-assoc <bool>          associate .tlpx / .tlp / .tlx        (default: true)");
        sb.AppendLine("  -launch <bool>              start TuneLab when the install ends  (default: true)");
        sb.AppendLine("  -language <code>            the language TuneLab starts in       (default: leave it as it is)");
        sb.AppendLine("                              " + string.Join(" ", languages));
        sb.AppendLine();
        sb.AppendLine("Exit codes: 0 ok, 1 the install failed, 2 wrong usage.");
        sb.AppendLine("A silent run also writes what it did to %temp%\\TuneLab.Setup.log.");
        return sb.ToString();
    }
}
