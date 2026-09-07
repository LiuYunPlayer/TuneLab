using System.Collections.Generic;
using System.Linq;
using TuneLab.Commands.Handlers;

namespace TuneLab.Commands;

// 命令面的【唯一真源】：宿主级静态注册表，不依赖 UI、不依赖工程。
//
// 三个入口都由它自省出自己的表面——agent 的工具声明、CLI 的命令树与离线 --help、MCP 的 tools/list——
// 故它们结构上不可能漂移，不需要额外的一致性测试（单栈的红利）。
//
// 命令实例无状态（工程与环境全走 CommandContext），因此这张表建一次就够：不随工程切换重建
// （对比现状：AgentSideBarContentProvider.SetProject() 每次换工程 new 一遍全部工具）。
internal static class CommandRegistry
{
    // 声明序 = CLI 帮助与 MCP 描述里的呈现序，故按 group 分组、组内 read 在前。
    public static IReadOnlyList<ICommand> All { get; } = new ICommand[]
    {
        new AppInfoCommand(),
        new ProjectStatusCommand(),
        new ProjectExportCommand(),
        new ScriptListCommand(),
        new ScriptReadCommand(),
        new ScriptInputsCommand(),
        new ScriptSaveCommand(),
        new ScriptDeleteCommand(),
        new ScriptRunCommand(),
        new ScriptRunSavedCommand(),
        new SandboxRunCommand(),
        new DocsScriptApiCommand(),
        new DocsManualCommand(),
        new SettingListCommand(),
        new SettingSetCommand(),
        new KeybindingListCommand(),
        new KeybindingSetCommand(),
        new SoundSourceListCommand(),
        new EffectListCommand(),
        new ExtensionListCommand(),
        new ExtensionRoutingCommand(),
        new ExtensionSetRoutingCommand(),
        new ExtensionIntroductionCommand(),
        new ExtensionSettingsCommand(),
        new ExtensionSetSettingCommand(),
        new ExtensionEnableCommand(),
    };

    // agent 工具名 → 命令路径。命令面的文本是给模型写的，里面引用别的动作时用的是 agent 工具面上的
    // 名字（"Call list_settings to see the exact keys"）——那些名字在命令行/MCP 上根本不存在。故各入口
    // 打印文本前按这张表换成自己的叫法（CLI：TuneLab.Cli.CommandText）。
    // 【表在这里、换名在入口】因为"换成什么样"是入口自己的事（CLI 是 "tunelab setting list"、
    // MCP 是工具名 + subcommand），而"哪些名字要换"只能由注册表说。
    public static IReadOnlyDictionary<string, string> PathsByAgentToolName { get; }
        = All.ToDictionary(c => c.AgentToolName, c => c.Path);

    public static bool TryGet(string path, out ICommand command)
    {
        foreach (var c in All)
        {
            if (c.Path == path)
            {
                command = c;
                return true;
            }
        }
        command = null!;
        return false;
    }

    // 命令的第一个路径段（group）。CLI 的第一层子命令、MCP 的 group × {read, edit} 合成都按它分桶。
    public static string GroupOf(ICommand command)
    {
        int i = command.Path.IndexOf(' ');
        return i < 0 ? command.Path : command.Path.Substring(0, i);
    }

    public static IEnumerable<string> Groups => All.Select(GroupOf).Distinct();
}
