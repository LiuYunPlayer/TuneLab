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
        new ProjectStatusCommand(),
        new ProjectExportCommand(),
        new ScriptListCommand(),
        new ScriptReadCommand(),
        new ScriptInputsCommand(),
        new ScriptSaveCommand(),
        new ScriptDeleteCommand(),
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
