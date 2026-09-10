using System;
using System.Collections.Generic;
using System.IO;

namespace TuneLab;

// 启动参数的分流。双击工程文件、把文件拖到 exe 上、文件关联、以及第二个实例经命名管道转发过来的参数，
// 全从这一处过。
//
// 【为什么要分流，而不是逐个当工程打开】原先每个参数都直接送去"打开工程"，于是任何不是文件的参数
// （手打的选项、拼错的名字、误传的一串词）都各弹一个"文件打不开"的框——传四个词就是四个框，
// 而用户压根没想打开任何文件。四条判据：
//  ① `-` 开头 = 选项。主程序今天没有自己的命令行选项，故只记日志；将来要加就在这里接。
//  ② `.tlx` = 扩展包，双击即安装，可以一次装多个。
//  ③ 其余视为工程路径，但**只认第一个**——一个窗口一次只开一个工程，后面的如实记为忽略。
//  ④ 那个路径**打不开**时分两种：后缀是我们认得的工程格式（用户双击了一个已被删除或移走的工程）
//     → 照旧交给打开流程去提示，那是他要知道的事；后缀根本不是我们的（"action"、"list"）
//     → 只记日志，不弹框。判据落在 looksLikeProject 这个谓词上（存在 or 后缀认得）。
internal static class StartupArgs
{
    // ProjectPath = 要打开的那一个工程（null = 这批参数里没有）；ExtensionPackages = 要安装的 .tlx；
    // Ignored = 没当成文件的参数（原样留着写日志，故障排查时要看到我们到底收到了什么）。
    internal sealed record Plan(string? ProjectPath, IReadOnlyList<string> ExtensionPackages, IReadOnlyList<string> Ignored);

    // looksLikeProject：这个路径值不值得送去打开（真实实现 = 文件存在，或后缀是已注册的导入格式）。
    // 作参数传入而不是就地读 FormatsManager：分流规则本身不依赖磁盘与插件注册表，那样才测得动。
    public static Plan Split(IReadOnlyList<string> args, Func<string, bool> looksLikeProject)
    {
        string? project = null;
        var packages = new List<string>();
        var ignored = new List<string>();
        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            if (arg[0] == '-')
            {
                ignored.Add(arg);
                continue;
            }

            if (Path.GetExtension(arg).Equals(".tlx", StringComparison.OrdinalIgnoreCase))
            {
                packages.Add(arg);
                continue;
            }

            if (!looksLikeProject(arg))
            {
                ignored.Add(arg);
                continue;
            }

            if (project == null)
                project = arg;
            else
                ignored.Add(arg);
        }
        return new Plan(project, packages, ignored);
    }
}
