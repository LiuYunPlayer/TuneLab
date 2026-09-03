using System;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Commands;

namespace TuneLab.Agent;

// agent 入口适配器：把命令面的一条 ICommand 包成模型能调的 IAgentTool（见 docs/command-surface.md §9.1）。
//
// agent 特有的三件事都留在这一层、不渗进命令面：
//  · 工具名用下划线形（沿用搬家前的名字，模型侧零感知）；
//  · 结果回灌 Render(Data) 的文本——模型只吃文本，结构化 Data 是留给 CLI --json 与 MCP structuredContent 的；
//  · 失败回灌 "Error: " + message——前缀由入口加，故 handler 的 message 不带它（CLI 打 stderr 时不会冗余）。
//
// context 取【访问器】而非快照：命令实例无状态、注册表是静态的，工程切换只换 context，工具无须重建。
internal sealed class CommandTool(ICommand command, Func<CommandContext> context) : IAgentTool
{
    public string Name => command.AgentToolName;
    public string Description => command.Documentation;
    public string ParametersJsonSchema => command.ParametersJsonSchema;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        // 参数畸形时 Parse 抛 ArgumentException，由 AgentRunner 转成给模型的错误文本（同其它工具）。
        var args = CommandArgs.Parse(argumentsJson);
        var result = await command.ExecuteAsync(args, context(), cancellationToken);
        return result.IsError
            ? "Error: " + result.Error!.Value.Message
            : command.Render(result.Data, args);
    }
}
