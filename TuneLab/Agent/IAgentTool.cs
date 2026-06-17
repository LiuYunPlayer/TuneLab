using System.Threading;
using System.Threading.Tasks;
using TuneLab.SDK;

namespace TuneLab.Agent;

// 一个 agent 工具：对模型的声明（名称/描述/参数 schema）+ 执行入口。
// 执行结果是要回灌给模型的文本——成功时是结构化结果，失败时是错误说明（让模型自行重试/调整）。
internal interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    // 参数的 JSON Schema 文本；适配器把它填进各家的 tool/function 定义。
    string ParametersJsonSchema { get; }

    Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken);

    // 便捷：把工具声明转成发给模型的 schema。
    AgentToolSchema ToSchema() => new()
    {
        Name = Name,
        Description = Description,
        ParametersJsonSchema = ParametersJsonSchema,
    };
}
