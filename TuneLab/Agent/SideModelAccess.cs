using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Commands;

namespace TuneLab.Agent;

// agent 入口的旁路模型实现（见 docs/command-surface.md §5.3）：把命令面给的「系统提示 + 一段材料」
// 包成一次性请求发给当前会话的模型。会话没连上时 send 回 null，命令面按"这次没拿到"降级。
//
// 这一层的存在正是为了让命令面不必认识 AgentMessage / AgentRole。
internal sealed class SideModelAccess(Func<IReadOnlyList<AgentMessage>, CancellationToken, Task<string?>> send) : ISideModelAccess
{
    public Task<string?> AskAsync(string systemPrompt, string userContent, CancellationToken cancellationToken)
        => send(
        [
            new AgentMessage { Role = AgentRole.System, Content = systemPrompt },
            new AgentMessage { Role = AgentRole.User, Content = userContent },
        ], cancellationToken);
}
