using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace TuneLab.Setup.Core;

/// <summary>
/// 命令行入口 <c>tunelab</c>：在安装目录下放一个 <c>CommandLine\tunelab.cmd</c>（转发到
/// TuneLab.Cli.exe），并把**只装着这一个文件的那个目录**加进当前用户的 PATH。
///
/// 为什么要落这个入口：命令行是外部工具（以及用户自己雇的 AI agent）驱动 TuneLab 的那条路，
/// 而"先让用户去翻安装目录、把一长串路径抄给工具"是这条路上最容易劝退人的一步。
///
/// 【为什么不直接把安装目录加进 PATH】那里躺着 Skia / NAudio 一大堆原生 dll。PATH 参与 Windows
/// 的 dll 搜索，把这样一个目录挂上去，会改变【别的进程】加载 dll 的结果——那是能把不相干的程序
/// 弄坏的一类副作用，且极难归因。单独一个目录只暴露一个文件，没有这个问题。
///
/// 全程每用户（HKCU\Environment + 安装目录内），无需管理员，与安装器其余部分一致。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class CommandLineEntry
{
    /// <summary>入口目录（安装目录下的相对路径）。名字进 PATH，故取得像个正经的 bin 目录。</summary>
    public const string DirectoryName = "CommandLine";

    /// <summary>命令名。用户与外部工具敲的就是它。</summary>
    public const string CommandName = "tunelab";

    // 转发脚本。%* 原样传递参数（含引号），exit /b 把退出码带回去——命令行的退出码是对外契约
    // （0 成功 / 1 命令失败 / 2 用法错 / 3 连不上宿主），在这里丢掉就等于让 CI 无从分支。
    static string Script =>
        "@echo off\r\n"
        + "rem TuneLab command line. Run \"" + CommandName + " --help\" for the command tree.\r\n"
        + "\"%~dp0..\\" + ProductInfo.CommandLineExecutableName + "\" %*\r\n"
        + "exit /b %ERRORLEVEL%\r\n";

    /// <summary>
    /// 落入口并（按需）加进 PATH。
    ///
    /// PATH 的处置与快捷方式同一条规矩：更新模式不动它，保留用户当初的选择（他可能自己摘掉了）。
    /// 唯一的例外是**这个入口本次才出现**（从没有它的老版本更新上来）——那不叫"用户摘掉了"，
    /// 而是他还没有过。
    /// </summary>
    public static void Install(string installDir, bool isUpdate)
    {
        string dir = Path.Combine(installDir, DirectoryName);
        string cmd = Path.Combine(dir, CommandName + ".cmd");
        bool existedBefore = File.Exists(cmd);

        Directory.CreateDirectory(dir);
        File.WriteAllText(cmd, Script, new UTF8Encoding(false));

        if (!isUpdate || !existedBefore)
            AddToUserPath(dir);
    }

    /// <summary>把入口目录从 PATH 里摘掉（目录本身随安装目录一起删）。</summary>
    public static void Uninstall(string installDir)
        => RemoveFromUserPath(Path.Combine(installDir, DirectoryName));

    /// <summary>这份安装有没有落过命令行入口。给"该不该告诉用户可以直接敲 tunelab"用。</summary>
    public static bool IsInstalled(string installDir)
        => File.Exists(Path.Combine(installDir, DirectoryName, CommandName + ".cmd"));

    /// <summary>
    /// 把 dir 追加进一份 PATH 文本。已经在里面就返回 null（= 无须改动；重复安装不该越加越长）。
    /// 纯函数：改坏别人的 PATH 是这个文件里唯一真正危险的事，故判据不与注册表纠缠在一起。
    /// </summary>
    internal static string? WithEntry(string path, string dir)
    {
        foreach (var entry in path.Split(';'))
        {
            if (Same(entry, dir))
                return null;
        }
        return path.Length == 0 ? dir : path.TrimEnd(';') + ";" + dir;
    }

    /// <summary>
    /// 把 dir 从一份 PATH 文本里摘掉。不在里面就返回 null。
    /// 【只摘我们自己那一项】其它条目一概原样留下——顺序、空项、%VAR% 写法都不动。
    /// </summary>
    internal static string? WithoutEntry(string path, string dir)
    {
        var kept = new System.Collections.Generic.List<string>();
        bool removed = false;
        foreach (var entry in path.Split(';'))
        {
            if (Same(entry, dir))
            {
                removed = true;
                continue;
            }
            kept.Add(entry);
        }
        return removed ? string.Join(";", kept) : null;
    }

    static void AddToUserPath(string dir) => Edit(path => WithEntry(path, dir));

    static void RemoveFromUserPath(string dir) => Edit(path => WithoutEntry(path, dir));

    // 读-改-写用户 PATH。改动函数返回 null = 无须改动。
    //
    // 【必须走注册表、且不许展开变量】Environment.GetEnvironmentVariable(..., User) 拿到的是
    // **展开后**的值，写回去就把用户 PATH 里的 %USERPROFILE% 之类烧成了字面路径——那是一种
    // 悄无声息地弄坏别人 PATH 的经典方式。故这里用 DoNotExpandEnvironmentNames 读原文，
    // 并保持原来的值类型（REG_EXPAND_SZ 仍写回 REG_EXPAND_SZ）。
    static void Edit(Func<string, string?> change)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Environment", writable: true);
            if (key == null)
                return;

            var kind = RegistryValueKind.ExpandString;
            foreach (var name in key.GetValueNames())
            {
                if (string.Equals(name, "Path", StringComparison.OrdinalIgnoreCase))
                {
                    kind = key.GetValueKind(name);
                    break;
                }
            }

            string current = key.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? string.Empty;
            string? updated = change(current);
            if (updated == null)
                return;

            key.SetValue("Path", updated, kind);
            NotifyEnvironmentChanged();
        }
        catch (Exception)
        {
            // PATH 改不了不该让安装失败：程序装好了、命令行也在，只是得写全路径去调。
        }
    }

    // 广播"环境变量变了"。不广播的话，资源管理器手上那份环境块不刷新，从它起的新终端要等到
    // 重新登录才看得见 tunelab——而那正是用户会当成"根本没装上"的情形。
    static void NotifyEnvironmentChanged()
    {
        const int WM_SETTINGCHANGE = 0x001A;
        const int SMTO_ABORTIFHUNG = 0x0002;
        try { SendMessageTimeout((IntPtr)0xffff, WM_SETTINGCHANGE, IntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 100, out _); }
        catch (Exception) { /* 通知不到只是延迟生效，不值得让安装失败 */ }
    }

    static bool Same(string a, string b)
        => a.Trim().TrimEnd('\\').Equals(b.Trim().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string lParam,
        int flags, int timeout, out IntPtr result);
}
