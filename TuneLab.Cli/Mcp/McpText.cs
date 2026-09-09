using System.Linq;
using System.Text.RegularExpressions;
using TuneLab.Commands;

namespace TuneLab.Cli.Mcp;

// 命令面的文本里，引用别的命令时用的是 **agent 工具面上的名字**（"Call list_settings to see the exact
// keys"）。那些名字在 MCP 的工具面上【根本不存在】——这里的工具是按 group 合成的，一条命令是
// `setting_read` 的一个 subcommand。照原样打出去，等于让读它的 agent 去调一个 tools/list 里查不到的工具。
//
// 故与 CLI 同一个办法（TuneLab.Cli.CommandText）：打印任何给 agent 看的文本之前，按注册表那张对照表
// 机械换成本入口的叫法。表在注册表、换名在入口——"换成什么样"是入口自己的事。
internal static class McpText
{
    // 长名在前：短名不会先把长名的一部分吃掉。
    static readonly Regex ToolNames = new(@"\b(" + string.Join("|", CommandRegistry.PathsByAgentToolName.Keys
        .OrderByDescending(name => name.Length).Select(Regex.Escape)) + @")\b");

    // 工具名后面常跟着调用式的参数表（"set_setting(key, value)"）。换名之后那不再是一个函数调用，
    // 但把参数名删掉会丢信息，故只加一个空格让它读成附注（同 CLI 的处理）。
    static readonly Regex CallSyntax = new(@"(subcommand ""[a-z-]+"")\(");

    public static string ForMcp(string text)
        => string.IsNullOrEmpty(text) ? text
            : CallSyntax.Replace(ToolNames.Replace(text, m => Rename(m.Value)), "$1 (");

    static string Rename(string agentToolName)
    {
        var path = CommandRegistry.PathsByAgentToolName[agentToolName];
        if (!CommandRegistry.TryGet(path, out var command))
            return agentToolName;

        var tool = McpTools.All.FirstOrDefault(t => t.Commands.Contains(command));
        return tool == null ? agentToolName : tool.Name + " with subcommand \"" + McpTools.Verb(command) + "\"";
    }
}
