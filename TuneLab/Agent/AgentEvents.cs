namespace TuneLab.Agent;

// AgentRunner 处理一轮用户输入过程中按序发出的进度事件，供 UI 渲染分步指示（流式文本 + 工具调用过程可见）。
// 事件在后台线程同步发出（不经 SynchronizationContext 异步 Post），UI sink 负责 marshal 到 UI 线程并保持 FIFO 顺序——
// 这样「文本增量」与「工具开始/完成」严格按发生先后到达界面，分步块的前后关系才正确。
internal abstract record AgentEvent;

// 当前助手轮的流式文本增量（模型边生成边回调）。UI 累加到当前文本段，工具开始或本轮结束时封口转 Markdown。
internal sealed record AgentTextDelta(string Delta) : AgentEvent;

// 推理模型的「思考」流式增量（OpenAI 协议的 reasoning_content）。与正文 AgentTextDelta 分流——UI 铺进独立的可折叠
// 思考块，不混入正文。某些推理模型（如 deepseek 推理系）会把大部分乃至全部输出放在 reasoning_content、content 极少或为空。
internal sealed record AgentReasoningDelta(string Delta) : AgentEvent;

// 模型请求调用一个工具：UI 据此封口当前文本段、插入一个工具步骤块（按 Id 关联随后的完成事件）。
internal sealed record AgentToolStarted(string Id, string Name, string ArgumentsJson) : AgentEvent;

// 一个工具执行完成：Result 是回灌给模型的文本（成功=结构化结果，失败=错误说明），IsError 标错（界面用不同状态色）。
internal sealed record AgentToolFinished(string Id, string Name, string Result, bool IsError) : AgentEvent;

// 一次模型调用（一轮）的 token 用量：runner 在该调用返回、执行其请求的工具之前发出，供 UI 在该轮工具块前实时插一行
// per-call 用量（让用户在多次工具往返过程中就看到每次调用的消耗，而非等整轮结束）。仅在该轮请求了工具时发（末轮收尾由脚注合计承载）。
internal sealed record AgentRoundUsage(int PromptTokens, int CompletionTokens, int TotalTokens) : AgentEvent;

// 用户在生成过程中插话：runner 在轮边界（tool 结果已全配对、或模型刚给出无工具的答复）把 pending 文本作为一条 user 消息
// 注入续跑，并发此事件——UI 据此在当前 turn 视图里按时间顺序行内渲染一个用户小气泡（区别于落在 ctx.View 顶部的常规用户气泡）。
internal sealed record AgentUserInterjection(string Text) : AgentEvent;
