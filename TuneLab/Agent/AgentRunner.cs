using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.SDK;

namespace TuneLab.Agent;

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

    // 处理一条用户消息，返回模型的最终文本回复。对话历史在多次调用间累积（保持上下文）。
    public async Task<string> SendAsync(string userInput, CancellationToken cancellationToken)
    {
        mMessages.Add(new AgentMessage { Role = AgentRole.User, Content = userInput });

        for (int round = 0; round < MaxToolRounds; round++)
        {
            var reply = await mSession.SendAsync(
                new AgentModelRequest { Messages = mMessages, Tools = mToolSchemas },
                cancellationToken);

            mMessages.Add(new AgentMessage
            {
                Role = AgentRole.Assistant,
                Content = reply.Content,
                ToolCalls = reply.ToolCalls.Count > 0 ? reply.ToolCalls : null,
            });

            if (reply.ToolCalls.Count == 0)
                return reply.Content ?? string.Empty;

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

        return string.Format("Stopped: exceeded {0} tool-call rounds without a final answer.", MaxToolRounds);
    }

    readonly IAgentModelSession mSession;
    readonly Dictionary<string, IAgentTool> mTools;
    readonly List<AgentToolSchema> mToolSchemas;
    readonly List<AgentMessage> mMessages = [];
}
