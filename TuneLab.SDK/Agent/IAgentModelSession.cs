using System;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.SDK;

// 一次 agent 会话：持有到某个模型的连接（含用户配置的端点/密钥/模型名等），驱动多轮对话。
// 与 effect 的 IEffectProcessor 同位——由引擎用"用户确定后的 properties"创建，宿主在会话存活期间复用。
public interface IAgentModelSession : IDisposable
{
    // 把对话历史 + 可用工具发给模型，返回模型一轮回复（文本 + 可能的工具调用）。
    // 第一版非流式；后续可加流式重载而不破坏此接口。失败抛异常，宿主在调用边界处理。
    Task<AgentModelReply> SendAsync(AgentModelRequest request, CancellationToken cancellationToken);
}
