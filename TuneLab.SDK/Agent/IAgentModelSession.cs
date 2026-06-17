using System;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.SDK;

// 一次 agent 会话：持有到某个模型的连接（含用户配置的端点/密钥/模型名等），驱动多轮对话。
// 与 effect 的 IEffectProcessor 同位——由引擎用"用户确定后的 properties"创建，宿主在会话存活期间复用。
public interface IAgentModelSession : IDisposable
{
    // 把对话历史 + 可用工具发给模型，返回模型一轮回复（文本 + 可能的工具调用）。
    // 失败抛异常，宿主在调用边界处理。
    Task<AgentModelReply> SendAsync(AgentModelRequest request, CancellationToken cancellationToken);

    // 流式重载：生成过程中把文本增量逐段经 onContentDelta 回调（可空），返回值仍是完整一轮回复（含工具调用/usage）。
    // 默认实现回退到上面的非流式版本（不产增量、等整段返回）——不支持流式的适配器无需改动。支持流式者覆盖此方法。
    Task<AgentModelReply> SendAsync(AgentModelRequest request, IProgress<string>? onContentDelta, CancellationToken cancellationToken)
        => SendAsync(request, cancellationToken);
}
