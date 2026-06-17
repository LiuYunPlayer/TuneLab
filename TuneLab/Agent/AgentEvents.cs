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
