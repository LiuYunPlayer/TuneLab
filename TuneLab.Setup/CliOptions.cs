using System;

namespace TuneLab.Setup;

internal enum SetupMode
{
    /// <summary>默认：带向导界面的首次安装。</summary>
    Interactive,
    /// <summary>静默安装/更新（无界面），供 App 自更新调用。</summary>
    Update,
    /// <summary>静默卸载，供"添加或删除程序"入口调用。</summary>
    Uninstall,
}

/// <summary>
/// 命令行解析结果。约定：
///   (无参)                → 交互安装
///   -update  &lt;targetDir&gt; → 静默更新到指定目录
///   -uninstall &lt;targetDir&gt; → 静默卸载指定目录
///   -silent               → 交互模式下也不弹窗（配合 -dir 走默认流程）
///   -dir &lt;path&gt;           → 覆盖安装目录
/// </summary>
internal sealed class CliOptions
{
    public SetupMode Mode { get; private set; } = SetupMode.Interactive;
    public string? TargetDir { get; private set; }
    public bool Silent { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var result = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
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
                    result.Silent = true;
                    break;
                case "-dir":
                    if (i + 1 < args.Length)
                        result.TargetDir = args[++i];
                    break;
            }
        }
        return result;
    }
}
