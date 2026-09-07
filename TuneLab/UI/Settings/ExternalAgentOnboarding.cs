using System;
using System.IO;
using System.Text;
using TuneLab.Configs;
using TuneLab.I18N;

// 命名空间跟随本目录既有文件用 TuneLab.UI（**不要** TuneLab.UI.Settings：那会在 TuneLab.UI 内部
// 把名字 Settings 解析成命名空间而不是 Configs.Settings 那个类，整片文件当场编译不过）。
namespace TuneLab.UI;

// 给【外部 agent】的接入说明：用户点一下"复制"，把这段话贴进他自己雇的那个 agent 里，那个 agent
// 从此知道这台机器上有个 TuneLab、怎么驱动它、以及哪些事不许它自己做。
//
// 【为什么现生成、不写死一段文案】里面有三样只有此刻才知道的事实：
//  · 命令行的【真实绝对路径】（这份安装装在哪；文件不在就如实说这份安装没有命令行，绝不吐一条无效路径）；
//  · 用户敲 `tunelab` 到底管不管用（安装器落过入口才管用，便携解压的那份没有）；
//  · 命令桥此刻开没开——没开的话最前面要多一段"我得先去开它"，且位置与字样取【用户界面上看到的译文】，
//    否则等于让他去找一个界面上根本不存在的英文标签。
//
// 【口吻是用户在对他的 agent 说话】——这段话是用户当自己的话贴出去的，不是软件在自我介绍。
//
// 【刻意不复述"连不上时怎么办"】那句话 BridgeClient 已经会说，且能分清"桥没开 / 宿主没跑 / 桥不应答"
// 三种情形。这段文案是主动快照，命令行的消息是被动兜底，两层各司其职；抄一遍只会有一天与它对不上。
internal static class ExternalAgentOnboarding
{
    // 命令行可执行文件名（与主程序同目录）。
    static string ExecutableName => OperatingSystem.IsWindows() ? "TuneLab.Cli.exe" : "TuneLab.Cli";

    /// <summary>命令行的绝对路径；这份安装里没有它就返回 null。</summary>
    public static string? ExecutablePath
    {
        get
        {
            var path = Path.Combine(PathManager.ExcutableFolder, ExecutableName);
            return File.Exists(path) ? path : null;
        }
    }

    // 用户能不能直接敲 `tunelab`。安装器会在安装目录下落 CommandLine\tunelab.cmd 并把那个目录加进
    // 用户 PATH（TuneLab.Setup.Core.CommandLineEntry —— 改那边的布局要连这里一起改）；
    // 便携解压出来的那一份没有这一步，故不能一律声称 PATH 里有。
    static bool OnPath => File.Exists(Path.Combine(PathManager.ExcutableFolder, "CommandLine", "tunelab.cmd"));

    /// <summary>
    /// 生成这一刻的接入说明。正文英文（读它的是模型），状态感知的那一段里嵌用户界面上的中文/本地化字样。
    /// </summary>
    public static string Build()
    {
        var sb = new StringBuilder();

        if (ExecutablePath is not { } exe)
        {
            // 便携产物里理论上不该缺，但缺了就必须说清——给一条不存在的路径，那个 agent 会拿它试到放弃。
            sb.AppendLine("TuneLab is a singing voice synthesis editor running on this machine, but THIS install has no command line");
            sb.AppendLine("next to it (" + Path.Combine(PathManager.ExcutableFolder, ExecutableName) + " is missing), so you cannot drive it from a terminal.");
            sb.AppendLine("Reinstall TuneLab with its installer if you need that.");
            return sb.ToString();
        }

        if (!Settings.CommandBridgeEnabled.Value)
        {
            // 桥没开：先说清这件事只有用户做得到，并把他界面上真实的字样告诉他自己（复制出去的文案
            // 也会被他自己再读一遍）。顺带说明"哪些事不用桥"——那正是 agent 可以立刻开始做的事。
            sb.AppendLine("Before you start: TuneLab's command bridge is off right now, so the command line cannot reach the");
            sb.AppendLine("project I have open yet. Only I can turn it on — I'll open " + Translate("Settings")
                + " -> " + Translate("General") + " and tick \""
                + Translate("Command Bridge (let external tools drive TuneLab)") + "\".");
            sb.AppendLine("Until then the documentation commands below still work (they don't need TuneLab to be running at all),");
            sb.AppendLine("so you can read up while I do that.");
            sb.AppendLine();
        }

        sb.AppendLine("TuneLab is a singing voice synthesis editor running on this machine. You can drive the");
        sb.AppendLine("project I currently have open through its command line.");
        sb.AppendLine();
        sb.AppendLine("  Command:  " + exe + (OnPath ? "        (also on PATH as `tunelab`)" : ""));
        sb.AppendLine("  Verify:   tunelab project status");
        sb.AppendLine();
        sb.AppendLine("Start here:");
        sb.AppendLine("  tunelab --help              the full command tree");
        sb.AppendLine("  tunelab --search <regex>    which command mentions this?");
        sb.AppendLine("  tunelab docs script-api     the `tl` scripting API — read this before your first edit");
        sb.AppendLine("  tunelab docs manual         TuneLab's user manual (chapter list, or --query <word> to search)");
        sb.AppendLine("  tunelab app info            which version this is, and where its data and log file live");
        sb.AppendLine();
        sb.AppendLine("Ground rules:");
        sb.AppendLine("- Project edits go through `tunelab script run --code \"...\"`. One script is ONE undoable");
        sb.AppendLine("  change and rolls back entirely on error, so fix and re-run rather than patching halfway.");
        sb.AppendLine("- Anything marked [edit] needs my authorization: --yes does it as far as my own authorization");
        sb.AppendLine("  setting inside TuneLab allows, --dry-run only reports what it would change.");
        sb.AppendLine("- You cannot play or render audio, and you cannot export audio files — that is mine to press.");
        sb.AppendLine("  (Exporting the project file itself you can do, with my authorization.)");
        sb.AppendLine("- If a command says TuneLab is not reachable, show me exactly what it printed.");
        sb.AppendLine();
        // 记忆那一段：只让它记【门】，不让它记门后的东西。命令清单/参数/版本会随版本漂移，记住了
        // 就会自信地调一条不存在的命令；而"有这道门、门在哪、四条铁律"是稳定的。
        sb.AppendLine("If you keep notes across sessions, remember the four rules above and that this command exists");
        sb.AppendLine("(re-check the path when you use it — installs move) — then I won't have to paste this again.");
        sb.AppendLine("Do NOT memorize the command list, their arguments, or which version this is: run `tunelab --help`");
        sb.AppendLine("first each time instead, because those do change between versions. If you have no memory across");
        sb.AppendLine("sessions, ignore this paragraph — I'll paste this again next time.");
        return sb.ToString();
    }

    // 用户界面上真实的字样。这三条都是设置窗上的字，故按设置窗那一段取译文（未翻译的语言下
    // 回退到英文原文，那也正是界面上显示的东西）——写死英文会让用户去找一个界面上不存在的标签。
    static string Translate(string text) => text.Tr("SettingsWindow");
}
