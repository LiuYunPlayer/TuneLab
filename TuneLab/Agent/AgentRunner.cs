using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.SDK;

namespace TuneLab.Agent;

// 一次用户输入处理的结果：
//  · Text  ——各轮助手自然语言合并的最终文本（用于复制/标题/脚注展示）。
//  · Usage ——本轮（含工具往返多次模型调用）的 token 用量合计（端点未返回则为 null）。
//  · Trajectory ——本轮新增的有序全量消息镜像（assistant 含思考/工具调用/本次用量，tool 含结果/错误标记），
//    供宿主原样落盘并据此重建分步视图、回灌续聊上下文，使「重载 == 实时」。
internal readonly record struct AgentTurnResult(string Text, AgentTokenUsage? Usage, IReadOnlyList<AgentTurnMessage> Trajectory);

// 本轮新增的一条轨迹消息（assistant 或 tool）：镜像 SDK 的 AgentMessage，并附宿主侧的思考全文 / 本次用量 / 错误标记。
// 这些扩展字段不在 AgentMessage 上（思考是输出不回发、用量是按调用计、错误标记是 UI 用），故另立宿主类型承载。
internal sealed class AgentTurnMessage
{
    public required AgentRole Role { get; init; }               // Assistant | Tool
    public string? Content { get; init; }
    public string? Reasoning { get; init; }                     // 仅 Assistant：思考通道全文（可空）
    public IReadOnlyList<AgentToolCall>? ToolCalls { get; init; } // 仅 Assistant：本次请求的工具调用
    public string? ToolCallId { get; init; }                     // 仅 Tool：回指对应 AgentToolCall.Id
    public bool IsError { get; init; }                           // 仅 Tool：结果是否为错误（UI 状态色）
    public AgentTokenUsage? Usage { get; init; }                 // 仅 Assistant：本次模型调用的 token 用量
}

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
    // progress：进度事件回调（可空）——文本增量(AgentTextDelta)透传自会话流式，工具开始/完成(AgentToolStarted/Finished)
    // 由本循环发出，供 UI 按序渲染分步指示。返回的 Text 是各轮助手自然语言的合并（不是仅最后一轮），用于持久化与复制。
    // attachments：本轮用户附带的多模态分片（如图片）。有则构造 Parts（文本 + 图片混排），Content 仍存文本拍平值
    // 供不支持多模态的适配器退化；无则纯文本 Content。
    // takePending：轮边界软插话钩子（可空）。runner 在每个安全边界（本轮 tool 结果已全配对回灌、或模型刚给出无工具的答复）
    // 调用它取用户在生成期间累积的插话文本——非 null 即作为一条 user 消息注入续跑。入队（输入框）与出队（本钩子）都在 UI 线程、
    // 全程无 ConfigureAwait(false)，故无需加锁。注入会重置工具回合预算（用户在主动引导，不应被 MaxToolRounds 砍断）。
    public async Task<AgentTurnResult> SendAsync(string userInput, IProgress<AgentEvent>? progress, CancellationToken cancellationToken, IReadOnlyList<AgentContentPart>? attachments = null, Func<string?>? takePending = null)
    {
        if (attachments is { Count: > 0 })
        {
            var parts = new List<AgentContentPart>();
            if (!string.IsNullOrEmpty(userInput))
                parts.Add(AgentContentPart.OfText(userInput));
            parts.AddRange(attachments);
            mMessages.Add(new AgentMessage { Role = AgentRole.User, Content = userInput, Parts = parts });
        }
        else
        {
            mMessages.Add(new AgentMessage { Role = AgentRole.User, Content = userInput });
        }

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

        // 把会话的字符串文本增量同步包装成 AgentTextDelta 事件转发——与下面的工具事件走同一通道，保证到达 UI 的先后顺序。
        IProgress<string>? deltaSink = progress == null ? null : new SyncProgress<string>(d => progress.Report(new AgentTextDelta(d)));
        // 推理模型的「思考」增量走独立的 AgentReasoningDelta 通道（与正文分流，仍经同一 progress 保持 FIFO 顺序）。
        IProgress<string>? reasoningSink = progress == null ? null : new SyncProgress<string>(d => progress.Report(new AgentReasoningDelta(d)));
        // 各轮助手自然语言，合并为本轮最终文本：根治「多轮叙述只剩最后一轮」——首轮先说后调工具的叙述不再被丢弃。
        var narration = new List<string>();
        // 本轮新增的有序全量轨迹（assistant + tool），供宿主落盘 + 重建分步视图。
        var trajectory = new List<AgentTurnMessage>();

        // 轮边界软插话：取尽 pending 文本，逐条作为 user 消息注入 mMessages + trajectory，并发事件供 UI 行内渲染。
        // 仅在安全边界调用（无未配对 tool_call），返回是否注入了至少一条（用于重置回合预算）。
        bool DrainPending()
        {
            if (takePending == null)
                return false;
            bool any = false;
            while (takePending() is { } pending && !string.IsNullOrEmpty(pending))
            {
                mMessages.Add(new AgentMessage { Role = AgentRole.User, Content = pending });
                trajectory.Add(new AgentTurnMessage { Role = AgentRole.User, Content = pending });
                progress?.Report(new AgentUserInterjection(pending));
                any = true;
            }
            return any;
        }

        int rounds = 0;
        while (rounds < MaxToolRounds)
        {
            rounds++;
            var reply = await mSession.SendAsync(
                new AgentModelRequest { Messages = mMessages, Tools = mToolSchemas },
                deltaSink,
                reasoningSink,
                cancellationToken);

            Accumulate(reply.Usage);

            var toolCalls = reply.ToolCalls.Count > 0 ? reply.ToolCalls : null;
            mMessages.Add(new AgentMessage
            {
                Role = AgentRole.Assistant,
                Content = reply.Content,
                ToolCalls = toolCalls,
            });
            trajectory.Add(new AgentTurnMessage
            {
                Role = AgentRole.Assistant,
                Content = reply.Content,
                Reasoning = reply.Reasoning,
                ToolCalls = toolCalls,
                Usage = reply.Usage,
            });

            if (!string.IsNullOrEmpty(reply.Content))
                narration.Add(reply.Content);

            // 每次模型调用（每轮，含单轮/末轮）返回即上报其用量——UI 据此实时刷新左下角运行 token + 右下角 Context/Session 状态行。
            if (reply.Usage is { } ru)
                progress?.Report(new AgentRoundUsage(ru.PromptTokens, ru.CompletionTokens, ru.TotalTokens));

            if (reply.ToolCalls.Count == 0)
            {
                // 模型已给出无工具的答复（本是收尾点）：若用户此刻有插话，吃掉它、重置回合预算、续跑把插话也答掉；否则真正结束。
                if (DrainPending()) { rounds = 0; continue; }
                return new AgentTurnResult(string.Join("\n\n", narration), TurnUsage(), trajectory);
            }

            foreach (var call in reply.ToolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new AgentToolStarted(call.Id, call.Name, call.ArgumentsJson));
                string result;
                bool isError;
                if (mTools.TryGetValue(call.Name, out var tool))
                {
                    try { result = await tool.ExecuteAsync(call.ArgumentsJson, cancellationToken); isError = false; }
                    catch (System.Exception ex) { result = "Error: " + ex.Message; isError = true; }
                }
                else
                {
                    result = string.Format("Error: unknown tool '{0}'.", call.Name);
                    isError = true;
                }

                progress?.Report(new AgentToolFinished(call.Id, call.Name, result, isError));
                mMessages.Add(new AgentMessage
                {
                    Role = AgentRole.Tool,
                    ToolCallId = call.Id,
                    Content = result,
                });
                trajectory.Add(new AgentTurnMessage
                {
                    Role = AgentRole.Tool,
                    ToolCallId = call.Id,
                    Content = result,
                    IsError = isError,
                });
            }

            // 本轮所有 tool 结果均已配对回灌——安全边界：吃掉用户插话注入续跑（有则重置回合预算）。
            if (DrainPending())
                rounds = 0;
        }

        // 撞上限：再请求一次但不给工具，逼模型用已有进展给出收尾文本——好过整轮作废、空手而归。
        var wrapUp = await mSession.SendAsync(
            new AgentModelRequest { Messages = mMessages, Tools = [] },
            deltaSink,
            reasoningSink,
            cancellationToken);
        Accumulate(wrapUp.Usage);
        mMessages.Add(new AgentMessage { Role = AgentRole.Assistant, Content = wrapUp.Content });
        trajectory.Add(new AgentTurnMessage
        {
            Role = AgentRole.Assistant,
            Content = wrapUp.Content,
            Reasoning = wrapUp.Reasoning,
            Usage = wrapUp.Usage,
        });
        if (!string.IsNullOrEmpty(wrapUp.Content))
            narration.Add(wrapUp.Content);
        return new AgentTurnResult(
            narration.Count > 0
                ? string.Join("\n\n", narration)
                : string.Format("Stopped after {0} tool-call rounds.", MaxToolRounds),
            TurnUsage(),
            trajectory);
    }

    // 同步转发的 IProgress：不经 SynchronizationContext 异步 Post，调用线程直转——保证文本增量与工具事件按发出顺序到达 UI sink。
    sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    readonly IAgentModelSession mSession;
    readonly Dictionary<string, IAgentTool> mTools;
    readonly List<AgentToolSchema> mToolSchemas;
    readonly List<AgentMessage> mMessages = [];
}
