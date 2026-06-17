using System;
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

    // history：加载已存会话时回填的先前对话（用户/助手文本），追加在 system prompt 之后，让续聊带上下文。
    public AgentRunner(IAgentModelSession session, IReadOnlyList<IAgentTool> tools, string? systemPrompt = null, IEnumerable<AgentMessage>? history = null)
    {
        mSession = session;
        mTools = tools.ToDictionary(t => t.Name);
        mToolSchemas = tools.Select(t => t.ToSchema()).ToList();
        if (!string.IsNullOrEmpty(systemPrompt))
            mMessages.Add(new AgentMessage { Role = AgentRole.System, Content = systemPrompt });
        if (history != null)
            mMessages.AddRange(history);
    }

    // 处理一条用户消息，返回模型的最终文本回复 + 本轮 token 用量。对话历史在多次调用间累积（保持上下文）。
    // 一次用户输入内可能有多次模型调用（工具往返），用量为这些调用的合计；任一调用返回了 usage 即非 null。
    // onContentDelta：流式文本增量回调（可空），透传给会话；不支持流式的会话默认等整段返回、不产增量。
    public async Task<AgentTurnResult> SendAsync(string userInput, IProgress<string>? onContentDelta, CancellationToken cancellationToken)
    {
        mMessages.Add(new AgentMessage { Role = AgentRole.User, Content = userInput });

        int prompt = 0, completion = 0, total = 0;
        bool hasUsage = false;
        AgentTokenUsage? TurnUsage() => hasUsage
            ? new AgentTokenUsage { PromptTokens = prompt, CompletionTokens = completion, TotalTokens = total }
            : null;
        void Accumulate(AgentTokenUsage? u)
        {
            if (u is not { } x)
                return;
            hasUsage = true;
            prompt += x.PromptTokens;
            completion += x.CompletionTokens;
            total += x.TotalTokens;
        }

        for (int round = 0; round < MaxToolRounds; round++)
        {
            var reply = await mSession.SendAsync(
                new AgentModelRequest { Messages = mMessages, Tools = mToolSchemas },
                onContentDelta,
                cancellationToken);

            Accumulate(reply.Usage);

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

        // 撞上限：再请求一次但不给工具，逼模型用已有进展给出收尾文本——好过整轮作废、空手而归。
        var wrapUp = await mSession.SendAsync(
            new AgentModelRequest { Messages = mMessages, Tools = [] },
            onContentDelta,
            cancellationToken);
        Accumulate(wrapUp.Usage);
        mMessages.Add(new AgentMessage { Role = AgentRole.Assistant, Content = wrapUp.Content });
        return new AgentTurnResult(
            string.IsNullOrEmpty(wrapUp.Content)
                ? string.Format("Stopped after {0} tool-call rounds.", MaxToolRounds)
                : wrapUp.Content,
            TurnUsage());
    }

    readonly IAgentModelSession mSession;
    readonly Dictionary<string, IAgentTool> mTools;
    readonly List<AgentToolSchema> mToolSchemas;
    readonly List<AgentMessage> mMessages = [];
}
