using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Commands;

// 一次【旁路】模型请求：不带工具声明、不带对话历史、不带主循环的系统提示，把一段材料交给模型、要回正文。
//
// 为什么是接口而不是直接收 agent 那边的委托：命令面不该认识 AgentMessage / AgentRole——那是 agent 会话的
// 形状。这里只暴露"系统提示 + 一段材料 → 正文"，由入口自己去拼它那边的消息（见 TuneLab.Agent.SideModelAccess）。
//
// 【只有内置 agent 入口有】CLI / MCP / headless 一律给 null。用到它的命令必须降级而不是消失
// （见 docs/command-surface.md §5.3）：能用缓存就用缓存，用不上就留空并如实标注，绝不假装有。
internal interface ISideModelAccess
{
    // 失败（未连模型 / 网络 / 限流）时返回 null 或抛异常——调用方两种都要当"这次没拿到"处理。
    Task<string?> AskAsync(string systemPrompt, string userContent, CancellationToken cancellationToken);
}
