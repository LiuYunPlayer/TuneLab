using System.Collections.Generic;

namespace TuneLab.SDK;

// agent 与模型之间的对话协议 DTO。放在 SDK 中，使第三方模型适配插件能直接消费/产出这些类型。
// 第一版刻意保持最小：非流式、工具调用以原始 JSON 参数串传递（适配器内部翻译成各家协议字段）。

// 一条消息的角色。Tool 表示工具执行结果（须带 ToolCallId 回指）；Assistant 消息可携带 ToolCalls。
public enum AgentRole
{
    System,
    User,
    Assistant,
    Tool,
}

// 模型请求的一次工具调用：Id 用于把后续 Tool 结果消息关联回来；ArgumentsJson 是模型给出的原始参数 JSON。
public sealed class AgentToolCall
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ArgumentsJson { get; init; }
}

// 一条对话消息。Content 与 ToolCalls 可同时为空/有值，语义由 Role 决定。
public sealed class AgentMessage
{
    public required AgentRole Role { get; init; }
    public string? Content { get; init; }
    public IReadOnlyList<AgentToolCall>? ToolCalls { get; init; }   // 仅 Assistant 角色可能有
    public string? ToolCallId { get; init; }                        // 仅 Tool 角色：回指对应的 AgentToolCall.Id
}

// 一个工具对模型的声明：名称 + 描述 + 参数 JSON Schema 文本。适配器据此填进各家的 tools/functions 字段。
public sealed class AgentToolSchema
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string ParametersJsonSchema { get; init; }
}

// 一次发送的输入：对话历史 + 本轮可用工具。
public sealed class AgentModelRequest
{
    public required IReadOnlyList<AgentMessage> Messages { get; init; }
    public IReadOnlyList<AgentToolSchema> Tools { get; init; } = [];
}

// 模型一轮回复：自然语言文本（可空）+ 它要求调用的工具（可能为空集，空集表示本轮结束）。
public sealed class AgentModelReply
{
    public string? Content { get; init; }
    public IReadOnlyList<AgentToolCall> ToolCalls { get; init; } = [];
}
