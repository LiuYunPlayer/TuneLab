using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;

namespace TuneLab.Commands;

// 一条命令的类别。决定各入口如何标注安全性：
//  · Read    只读，不改任何状态；
//  · Edit    改工程数据 / 应用配置 / 磁盘文件，须过入口的授权策略；
//  · Sandbox 在可丢弃的无头工程里跑，既非只读也不碰用户数据、不过授权闸门
//            （单列而不压进 Edit——压进去会让所有入口的安全标注说谎）。
internal enum CommandKind
{
    Read,
    Edit,
    Sandbox,
}

// 一条【末端动作】：路径 + 参数 schema + 文档 + handler。
//
// 它不含任何入口特有的东西——不知道模型、不知道终端、不知道 UI。环境一律经 CommandContext 注入，
// 因此同一条命令可以被内置 agent、CLI、MCP server、CI 里的无界面进程调用（见 docs/command-surface.md）。
//
// 实现无状态：工程与环境都从 ctx 走，故注册表可以是静态的、不随工程切换重建。
internal interface ICommand
{
    // 命令路径："<group> <verb>"，空格分隔（如 "setting set"）。CLI 的子命令与 MCP 的工具合成都按它切分。
    string Path { get; }

    CommandKind Kind { get; }

    // 一行摘要（≤60 字）：进 MCP 工具描述与 CLI 的一行帮助。
    string Brief { get; }

    // 完整说明：进 MCP/agent 的工具描述与 CLI 的详细帮助。
    string Documentation { get; }

    string ParametersJsonSchema { get; }

    // agent 工具面上的名字。默认由 Path 机械推导，但允许覆盖：现有工具名（get_project_overview /
    // list_settings …）已经写进系统提示与彼此的描述文本（"Call list_settings to see the exact keys"），
    // 且 get_ / list_ / set_ 前缀不是从路径机械可推的——覆盖掉可让模型侧对这次搬家零感知。
    string AgentToolName => Path.Replace(' ', '_');

    Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken);

    // 把结构化结果渲成人类/模型可读文本。渲染刻意不在 handler 里：同一份措辞因此同时出现在
    // CLI stdout 和模型上下文里，不可能分裂。只在成功（Data）时调用；失败走 CommandError.Message。
    string Render(JsonNode? data, CommandArgs args);
}
