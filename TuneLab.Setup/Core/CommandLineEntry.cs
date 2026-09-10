using System;
using System.IO;
using System.Runtime.Versioning;
using System.Text;

namespace TuneLab.Setup.Core;

/// <summary>
/// 安装目录里的 <c>tunelab</c> 转发入口：一个 .cmd，把参数原样交给同目录的 TuneLab.Cli.exe。
///
/// 【为什么需要它】命令行程序集不能叫 tunelab——它与被引用的 TuneLab.dll 在同一输出目录里会同名
/// （Windows 文件名不区分大小写），故 exe 只能是 TuneLab.Cli.exe。而命令面写给人与模型看的文本里
/// 说的是 <c>tunelab project status</c>（各入口按注册表换名的结果），那个名字必须在用户机器上真的存在，
/// 否则回报里教的调用方式就是一句在他那儿不成立的话。
///
/// 【为什么不动 PATH】那会污染用户的环境变量，而这一步的目的只是"在安装目录里跑得通"。
/// 用户自己把安装目录加进 PATH 之后 <c>tunelab</c> 就随处可用；要不要加是他的决定。
///
/// 【每次安装与更新都重写】它是产品的一部分，不像快捷方式那样只在首次安装时按用户选择创建。
/// 卸载不必单独处理：卸载器删的是整个安装目录。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class CommandLineEntry
{
    public const string FileName = "tunelab.cmd";

    // %~dp0 = 这个脚本所在目录（带尾反斜杠），故安装目录整体移动后依然有效，不写死绝对路径。
    // %* 原样转发全部参数；末行把 exe 的退出码交回调用方——CLI 的 0/1/2/3 是给脚本分支用的
    // （见 docs/command-surface.md §9.2），吞掉它就等于把那套约定作废。
    // 【不带 BOM、行尾 CRLF】cmd.exe 会把 UTF-8 BOM 当第一条命令的一部分而报错。
    const string Script =
        "@echo off\r\n"
        + "rem TuneLab command line. Forwards to TuneLab.Cli.exe next to this file.\r\n"
        + "rem Written by the installer; run \"tunelab --help\" to see what it can do.\r\n"
        + "\"%~dp0TuneLab.Cli.exe\" %*\r\n"
        + "exit /b %ERRORLEVEL%\r\n";

    /// <summary>写入（或覆盖）安装目录下的转发入口。命令行本体缺席时什么也不做。</summary>
    public static void Write(string installDir)
    {
        // 没铺上命令行本体就不留一个指向空处的入口——那比没有更糟（敲下去报的是"找不到文件"）。
        if (!File.Exists(Path.Combine(installDir, "TuneLab.Cli.exe")))
            return;

        File.WriteAllText(Path.Combine(installDir, FileName), Script, new UTF8Encoding(false));
    }
}
