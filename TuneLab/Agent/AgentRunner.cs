using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Configs;
using TuneLab.SDK;

namespace TuneLab.Agent;

// 一次用户输入处理的结果：
//  · Text  ——各轮助手自然语言合并的最终文本（用于复制/标题/脚注展示）。
//  · Usage ——本轮（含工具往返多次模型调用）的 token 用量合计（端点未返回则为 null）。
//  · Trajectory ——本轮新增的有序全量消息镜像（assistant 含思考/工具调用/本次用量，tool 含结果/错误标记），
//    供宿主原样落盘并据此重建分步视图、回灌续聊上下文，使「重载 == 实时」。
//  · StopNotice ——本轮被宿主自己的护栏截断时的说明（当前只有失控防护一种），非模型输出。
//    宿主据此渲一行提示并落一条 notice 记录；为空则本轮是自然收尾。刻意不塞进 Text——Text 只供复制/标题/脚注，
//    既不参与实时渲染也不落盘，往那里写提示等于写了没人看（这正是它上一版没生效的原因）。
internal readonly record struct AgentTurnResult(string Text, AgentTokenUsage? Usage, IReadOnlyList<AgentTurnMessage> Trajectory, string? StopNotice = null);

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
    // 仅 Assistant：这条回复【没说完就被中止】（用户点停 / 技术失败发生在流式途中），Content 是已收到的半截文本。
    // 它进轨迹（落盘 + 显示，故重载看得见当时说到哪），但【不喂回模型】——见 ReconstructHistory：那次回复作废，
    // 不该让模型以为自己说过这些话（半截话往里塞，续聊时它会当成自己的既有表态）。
    public bool Stopped { get; init; }
}

// agent 主循环：把对话历史 + 工具声明发给模型会话，循环执行模型请求的工具并把结果回灌，
// 直到模型不再请求工具，返回最终自然语言回复。provider 无关——只依赖 IAgentModelSession 抽象。
internal sealed class AgentRunner
{
    // 失控防护（runaway guard）：单轮用户输入内允许的工具调用回合数硬上限。刻意设在高位——它不参与正常流程判断。
    // 全自动流程本就该一直跑下去，回合数与"是否卡住"无关：健康的长任务（逐个跑几十个用例）与病态死循环，
    // 在这个量上无从区分。正常长跑的真实终点是【上下文窗口】——每轮重发全历史、上下文单调增长，撞满即由端点
    // 报错，走失败结局如实留痕（错误文本含 provider 原样响应体，说明是 prompt 过长）。
    // 叫停靠 cancellationToken（用户随时可停），不靠数到 N 自己停。
    const int MaxToolRounds = 1000;

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

    // 本轮（进行中/最近一轮）已构建的有序轨迹。失败/取消时 SendAsync/RetryAsync 抛异常、结果拿不到，
    // OnSend 据此持久化"半截过程"供显示（重载==实时）。
    public IReadOnlyList<AgentTurnMessage> CurrentTrajectory => mCurrentTrajectory ?? (IReadOnlyList<AgentTurnMessage>)[];
    List<AgentTurnMessage>? mCurrentTrajectory;

    // 轨迹刚追加了一条记录。宿主据此增量落盘，使进程在下一步消失（意外关闭 / 崩溃 / 其它）时，已发生的调用不随内存一起没掉——
    // 否则一句话触发的长任务被打断后，会话里只剩用户那一句，既看不出它干到哪、续跑时模型也会因看不见自己做过什么而从头再来。
    //
    // 【必须同步】不走 progress 事件通道：那条通道是 Progress<AgentEvent>（异步 Post 到 UI 线程），而工具在 UI 线程同步执行，
    // 事件排进队列后要等当前工作项让出才被处理——进程若在这个间隙里消失，落盘就没发生，恰好在最需要它的时候失效。
    // 同步回调把间隙消除，成本相同。宿主自己按"已落盘水位"取增量，故这里不带数据（避免事件与轨迹两份真相）。
    public Action? TrajectoryAppended { get; set; }

    // 悬空 tool_call 的合成结果。实时上下文（CloseDanglingToolCalls）与重载重建（ReconstructHistory）共用这一份——
    // 两条路径给模型的话必须一字不差，否则"重开前"与"重开后"的续跑行为又会分家。
    public const string DanglingToolResult =
        "The result of this call was never recorded — the run was interrupted before it returned. "
        + "It may have completed, partially completed, or not run at all. Verify the current state before retrying it, "
        + "especially if it writes files or changes settings.";

    // 给"未闭合"的 tool_call 补一条【结果未知】的合成结果：末尾 assistant 发起了调用却缺配对的 tool 结果
    //（取消落在工具执行中或两次调用之间都会产生）。协议要求每个 tool_call 必须有配对结果，否则端点拒收——
    // 但这里【补】而不是【删】：删掉那次调用，模型就完全不知道自己调过它，续跑时很可能再调一次，对
    // export_project / set_setting / set_keybinding 这类不可撤销的外部副作用就是重复施加。也不编成"成功"
    // 或"失败"：工具可能已完成副作用只是没来得及返回，也可能刚进去就死了，宿主无从判断。如实说不知道。
    // 顺带比旧行为更忠实一点：同一条 assistant 若发起 3 个调用而只有 1 个拿到结果，旧行为连那 1 个真结果
    // 一起删掉，现在保留真结果、只给另外两个补未知。
    public void CloseDanglingToolCalls()
    {
        int i = mMessages.Count - 1;
        var resultIds = new HashSet<string>();
        while (i >= 0 && mMessages[i].Role == AgentRole.Tool)
        {
            if (mMessages[i].ToolCallId is { } id)
                resultIds.Add(id);
            i--;
        }
        if (i < 0 || mMessages[i].Role != AgentRole.Assistant || mMessages[i].ToolCalls is not { Count: > 0 } calls)
            return;

        foreach (var c in calls)
            if (!resultIds.Contains(c.Id))
                mMessages.Add(new AgentMessage { Role = AgentRole.Tool, ToolCallId = c.Id, Content = DanglingToolResult });
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

        return await RunTurnAsync(progress, cancellationToken, takePending);
    }

    // 重试：不追加用户消息，直接对当前上下文（末尾即待重试的那条用户消息 + 已完成轮）续跑。供失败轮的重试按钮用——
    // 从而"重载后也能手动重试之前失败的轮"，且不必靠复述消息（复述会让模型看到两句话）。
    public Task<AgentTurnResult> RetryAsync(IProgress<AgentEvent>? progress, CancellationToken cancellationToken, Func<string?>? takePending = null)
        => RunTurnAsync(progress, cancellationToken, takePending);

    // 一轮的核心循环（发送与重试共用）：不负责追加用户消息，只对当前 mMessages 续跑模型 + 工具往返。
    async Task<AgentTurnResult> RunTurnAsync(IProgress<AgentEvent>? progress, CancellationToken cancellationToken, Func<string?>? takePending)
    {
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

        // 流式增量【边转发边累积】：转发供 UI 实时渲染，累积供"中途被中止"时把已说出的半截留进轨迹。
        // 不累积的话，那段文字只存在于界面上——上下文里没有、磁盘上也没有，重开会话就凭空消失（而灰字"已停止"还在，
        // 等于告诉用户"这里停过"却不给看停在哪），"显示 重载==实时"就破了。
        var partialText = new StringBuilder();
        var partialReasoning = new StringBuilder();
        // 把会话的字符串文本增量同步包装成 AgentTextDelta 事件转发——与下面的工具事件走同一通道，保证到达 UI 的先后顺序。
        IProgress<string> deltaSink = new SyncProgress<string>(d =>
        {
            partialText.Append(d);
            progress?.Report(new AgentTextDelta(d));
        });
        // 推理模型的「思考」增量走独立的 AgentReasoningDelta 通道（与正文分流，仍经同一 progress 保持 FIFO 顺序）。
        IProgress<string> reasoningSink = new SyncProgress<string>(d =>
        {
            partialReasoning.Append(d);
            progress?.Report(new AgentReasoningDelta(d));
        });

        // 各轮助手自然语言，合并为本轮最终文本：根治「多轮叙述只剩最后一轮」——首轮先说后调工具的叙述不再被丢弃。
        var narration = new List<string>();
        // 本轮新增的有序全量轨迹（assistant + tool），供宿主落盘 + 重建分步视图。
        var trajectory = new List<AgentTurnMessage>();
        mCurrentTrajectory = trajectory; // 抛异常时结果拿不到，故同引用暴露给 OnSend 落"半截过程"（见 CurrentTrajectory）

        // 一次模型调用被中止（取消 / 技术失败）时，把已收到的半截文本作为一条 Stopped 记录留进轨迹，然后异常照抛。
        // 只进 trajectory、不进 mMessages：前者是给宿主落盘与显示的真相，后者是喂模型的上下文——这次回复既已作废，
        // 就不该让模型以为自己说过这些（半截话塞回去，续聊时它会当成自己的既有表态）。
        // 落盘后由 ReconstructHistory 按 Stopped 跳过，两边口径因此一致。
        void KeepPartialReply()
        {
            if (partialText.Length == 0 && partialReasoning.Length == 0)
                return;

            trajectory.Add(new AgentTurnMessage
            {
                Role = AgentRole.Assistant,
                Content = partialText.ToString(),
                Reasoning = partialReasoning.Length > 0 ? partialReasoning.ToString() : null,
                Stopped = true,
            });
            partialText.Clear();
            partialReasoning.Clear();
            TrajectoryAppended?.Invoke();
        }

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
                TrajectoryAppended?.Invoke();
                progress?.Report(new AgentUserInterjection(pending));
                any = true;
            }
            return any;
        }

        int rounds = 0;
        while (rounds < MaxToolRounds)
        {
            rounds++;
            AgentModelReply reply;
            try
            {
                reply = await mSession.SendAsync(
                    new AgentModelRequest { Messages = mMessages, Tools = mToolSchemas },
                    deltaSink,
                    reasoningSink,
                    cancellationToken);
            }
            catch
            {
                KeepPartialReply();   // 已说出的半截留进轨迹（不进上下文），异常照抛给宿主收尾
                throw;
            }

            // 完整返回：累积器里的内容已由 reply.Content 完整承载，清掉以免下一次调用被中止时把它一并算进半截。
            partialText.Clear();
            partialReasoning.Clear();
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
            // 先落"要调什么"、再落各自结果（见下）：中途消失就留下悬空 tool_call，其字面意思正是
            // "发起了这个调用、结果不明"——宿主无从判断它成没成，不编造任何一种。
            TrajectoryAppended?.Invoke();

            if (!string.IsNullOrEmpty(reply.Content))
                narration.Add(reply.Content);

            // 每次模型调用（每轮，含单轮/末轮）返回即上报其用量——UI 据此实时刷新左下角运行 token + 右下角 Context/Session 状态行。
            if (reply.Usage is { } ru)
                progress?.Report(new AgentRoundUsage(ru.PromptTokens, ru.CompletionTokens, ru.TotalTokens));

            if (reply.ToolCalls.Count == 0)
            {
                // 模型已给出无工具的答复（本是收尾点）：若用户此刻有插话，吃掉它、重置回合预算、续跑把插话也答掉；否则真正结束。
                if (DrainPending()) { rounds = 0; continue; }
                // 但「没有工具调用」不等于「说完了」：finish_reason=length 表示这段回复是被 Max Tokens 硬截断的
                // （话没说完、该调的工具也没调），静默收尾就成了用户看到的"说一句我来…然后没了"。作失败结局抛出：
                // 已生成的正文留在轨迹里照常显示，末尾给出原因 + [重试]（重试对现有上下文续跑 = 让它接着说）。
                if (string.Equals(reply.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
                    throw new Exception(
                        "The model's reply was cut off by Max Tokens (finish_reason: length), so this turn is incomplete. " +
                        "Raise the Max Tokens setting (0 = no limit) or press Retry to let it continue.");
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
                    // 用户点停不是工具错误：原先它被下面那个 catch 一并吞成 "Error: The operation was canceled." 记进
                    // 上下文与界面——把用户的停止动作说成工具失败，而且让"点停"的表现分裂（停在工具间隙 → 悬空/结果未知，
                    // 停在执行中 → 记成 error）。这里放它穿出去，交给外层按取消收尾，那次调用便统一成悬空（结果未知）。
                    // when 限定只放行【我们这个 token】的取消；工具因自身原因抛 OperationCanceled 仍按错误处理。
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (System.Exception ex) { result = "Error: " + ex.Message; isError = true; }
                }
                else
                {
                    result = string.Format("Error: unknown tool '{0}'.", call.Name);
                    isError = true;
                }

                // 中央兜底：任何工具（含未来新工具）单次结果超上限即截断，防淹没上下文。上限宽默认、可在设置调（见 Settings）。
                // 在此唯一入口 clamp，故展示(progress)与回灌(mMessages/trajectory)一致。
                result = ClampToolResult(result);

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
                TrajectoryAppended?.Invoke();
            }

            // 本轮所有 tool 结果均已配对回灌——安全边界：吃掉用户插话注入续跑（有则重置回合预算）。
            if (DrainPending())
                rounds = 0;
        }

        // 撞上限：再请求一次但不给工具，逼模型用已有进展给出收尾文本——好过整轮作废、空手而归。
        AgentModelReply wrapUp;
        try
        {
            wrapUp = await mSession.SendAsync(
                new AgentModelRequest { Messages = mMessages, Tools = [] },
                deltaSink,
                reasoningSink,
                cancellationToken);
        }
        catch
        {
            KeepPartialReply();
            throw;
        }
        partialText.Clear();
        partialReasoning.Clear();
        Accumulate(wrapUp.Usage);
        mMessages.Add(new AgentMessage { Role = AgentRole.Assistant, Content = wrapUp.Content });
        trajectory.Add(new AgentTurnMessage
        {
            Role = AgentRole.Assistant,
            Content = wrapUp.Content,
            Reasoning = wrapUp.Reasoning,
            Usage = wrapUp.Usage,
        });
        TrajectoryAppended?.Invoke();
        if (!string.IsNullOrEmpty(wrapUp.Content))
            narration.Add(wrapUp.Content);
        // 如实留痕：撞上限这件事经 StopNotice 交给宿主渲染 + 落盘，用户因此看得见"这里被护栏截断了"。
        // （前两版都没真正生效：先是挂在"连一句话都没产出"的兜底分支上永不执行，后是塞进 Text 而 Text 既不渲染也不落盘。）
        return new AgentTurnResult(string.Join("\n\n", narration), TurnUsage(), trajectory,
            string.Format("Stopped after {0} tool-call rounds (runaway guard). What's above is what got done.", MaxToolRounds));
    }

    // 同步转发的 IProgress：不经 SynchronizationContext 异步 Post，调用线程直转——保证文本增量与工具事件按发出顺序到达 UI sink。
    sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    // 中央工具结果上限：单次结果超上限即截断（保留头部 + 明确标记 + 收窄指引），防淹没上下文。
    // 上限宽默认、可在设置调（Settings.AgentMaxToolResultChars；<=0 = 不限）。普通结果远小于此、不受影响，只拦畸形超量。
    static string ClampToolResult(string result)
    {
        int cap = Settings.AgentMaxToolResultChars.Value;
        if (cap <= 0 || result.Length <= cap)
            return result;
        return result.Substring(0, cap) + string.Format(
            "\n\n[... tool result truncated: {0} of {1} characters shown. If this is a list, narrow your query (a filter/engine/source argument) or ask for a smaller subset; if it's one large item, request a specific part. This limit is configurable in Settings.]",
            cap, result.Length);
    }

    readonly IAgentModelSession mSession;
    readonly Dictionary<string, IAgentTool> mTools;
    readonly List<AgentToolSchema> mToolSchemas;
    readonly List<AgentMessage> mMessages = [];
}
