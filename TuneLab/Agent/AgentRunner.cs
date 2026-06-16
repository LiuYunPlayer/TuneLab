using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.SDK;

namespace TuneLab.Agent;

// 一次用户输入处理的结果：最终文本回复 + 本轮（含工具往返多次模型调用合计）的 token 用量（端点未返回则为 null）。
internal readonly record struct AgentTurnResult(string Text, AgentTokenUsage? Usage);

// agent 主循环：把对话历史 + 工具声明发给模型会话，循环执行模型请求的工具并把结果回灌，
// 直到模型不再请求工具，返回最终自然语言回复。provider 无关——只依赖 IAgentModelSession 抽象。
internal sealed class AgentRunner
{
    // 单轮用户输入内允许的工具调用回合上限，防止模型陷入工具循环。
    const int MaxToolRounds = 25;

    public AgentRunner(IAgentModelSession session, IReadOnlyList<IAgentTool> tools, string? systemPrompt = null)
    {
        mSession = session;
        mTools = tools.ToDictionary(t => t.Name);
        mToolSchemas = tools.Select(t => t.ToSchema()).ToList();
        if (!string.IsNullOrEmpty(systemPrompt))
            mMessages.Add(new AgentMessage { Role = AgentRole.System, Content = systemPrompt });
    }

    // 处理一条用户消息，返回模型的最终文本回复 + 本轮 token 用量。对话历史在多次调用间累积（保持上下文）。
    // 一次用户输入内可能有多次模型调用（工具往返），用量为这些调用的合计；任一调用返回了 usage 即非 null。
    public async Task<AgentTurnResult> SendAsync(string userInput, CancellationToken cancellationToken)
    {
        mMessages.Add(new AgentMessage { Role = AgentRole.User, Content = userInput });

        int prompt = 0, completion = 0, total = 0;
        bool hasUsage = false;
        AgentTokenUsage? TurnUsage() => hasUsage
            ? new AgentTokenUsage { PromptTokens = prompt, CompletionTokens = completion, TotalTokens = total }
            : null;

        for (int round = 0; round < MaxToolRounds; round++)
        {
            var reply = await mSession.SendAsync(
                new AgentModelRequest { Messages = mMessages, Tools = mToolSchemas },
                cancellationToken);

            if (reply.Usage is { } u)
            {
                hasUsage = true;
                prompt += u.PromptTokens;
                completion += u.CompletionTokens;
                total += u.TotalTokens;
            }

            mMessages.Add(new AgentMessage
            {
                Role = AgentRole.Assistant,
                Content = reply.Content,
                ToolCalls = reply.ToolCalls.Count > 0 ? reply.ToolCalls : null,
            });

            if (reply.ToolCalls.Count == 0)
                return new AgentTurnResult(reply.Content ?? string.Empty, TurnUsage());

            foreach (var call in reply.ToolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string result;
                if (mTools.TryGetValue(call.Name, out var tool))
                {
                    try { result = await tool.ExecuteAsync(call.ArgumentsJson, cancellationToken); }
                    catch (System.Exception ex) { result = "Error: " + ex.Message; }
                }
                else
                {
                    result = string.Format("Error: unknown tool '{0}'.", call.Name);
                }

                mMessages.Add(new AgentMessage
                {
                    Role = AgentRole.Tool,
                    ToolCallId = call.Id,
                    Content = result,
                });
            }
        }

        return new AgentTurnResult(
            string.Format("Stopped: exceeded {0} tool-call rounds without a final answer.", MaxToolRounds),
            TurnUsage());
    }

    readonly IAgentModelSession mSession;
    readonly Dictionary<string, IAgentTool> mTools;
    readonly List<AgentToolSchema> mToolSchemas;
    readonly List<AgentMessage> mMessages = [];
}
