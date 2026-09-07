using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab;

internal class AppInfo
{
    // 【认自己的程序集，不认入口程序集】命令行与无头宿主的入口是 TuneLab.Cli，那个程序集没有版本号，
    // 于 GetEntryAssembly() 下会报出 "1.0.0"——而这个数字正是排障时第一个要问的东西，报错了最误导。
    static Assembly Self => typeof(AppInfo).Assembly;

    public static Version Version => Self.GetName().Version!;

    /// <summary>用于展示的版本串（如 "1.6.0"）：优先 AssemblyInformationalVersion（去掉 "+commit"），否则裁到三段。</summary>
    public static string VersionString
    {
        get
        {
            var info = Informational;
            if (!string.IsNullOrEmpty(info))
            {
                int plus = info.IndexOf('+');
                return plus >= 0 ? info[..plus] : info;
            }
            return Version.ToString(3);
        }
    }

    /// <summary>构建号 = AssemblyInformationalVersion 里 "+" 之后那截 commit（CI 出的包才有）；本地构建可能为空。</summary>
    public static string Build
    {
        get
        {
            var info = Informational;
            if (string.IsNullOrEmpty(info))
                return string.Empty;
            int plus = info.IndexOf('+');
            // 只留前 7 位：完整 sha 对人对模型都没有额外信息，而"版本 + 短 sha"正好是报问题要报的那两样。
            return plus < 0 ? string.Empty : info[(plus + 1)..][..Math.Min(7, info.Length - plus - 1)];
        }
    }

    // 出了问题去哪说。两处消费者共用这两个常量（关于窗的链接、`app info` 的回报），故不各写一份字面量。
    public const string ForumUrl = "https://forum.tunelab.app";
    public const string GitHubUrl = "https://github.com/LiuYunPlayer/TuneLab";
    public const string IssuesUrl = GitHubUrl + "/issues";

    static string? Informational => Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
}
