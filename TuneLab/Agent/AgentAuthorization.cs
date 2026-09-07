using System;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Configs;
using TuneLab.Commands;

namespace TuneLab.Agent;

// agent 写操作的授权级别（用户可调，见 docs/script-inputs-and-action-surface.md §3）。只作用于 agent 发起的写；
// 用户手动运行脚本不受此约束。存 Settings（字符串=枚举名），默认 Confirm（安全起步）。
internal enum AgentAuthorization
{
    ReadOnlyAdvice,   // 只读建议：脚本照跑但一律回退、只呈现"会改什么"，从不落地
    Confirm,          // 需确认：预览改动 → 用户确认 → 重跑落地；取消则不动
    Auto,             // 全自动：直接提交
}

internal static class AgentAuthorizationExtensions
{
    public static AgentAuthorization ParseOrDefault(string? value)
        => Enum.TryParse<AgentAuthorization>(value, out var level) ? level : AgentAuthorization.Confirm;

    // 用户此刻设定的档位，翻成命令面的档位。两个消费者共用这一份：侧栏 agent 直接按它放行；
    // 命令桥拿它当【天花板】（外部进程声明得再高也越不过它）。故这个映射只能有一份——
    // 各写一份的下场是"面板上设成只读建议，外部工具照改"。
    public static AuthorizationMode UserMode => ParseOrDefault(Settings.AgentAuthorization.Value) switch
    {
        AgentAuthorization.Auto => AuthorizationMode.Auto,
        AgentAuthorization.ReadOnlyAdvice => AuthorizationMode.ReadOnlyAdvice,
        _ => AuthorizationMode.Confirm,
    };
}

// 侧栏 agent 的授权策略（命令面按入口注入的那一个，见 docs/command-surface.md §5.1）：档位读用户的设置，
// 问用户就是弹那张内联升级卡片。流程与措辞不在这里——它们在命令面，故各入口不可能各说各话。
internal sealed class AgentAuthorizationPolicy(Func<AuthorizationRequest, CancellationToken, Task<AuthorizationDecision>>? confirm) : IAuthorizationPolicy
{
    public AuthorizationMode Mode => AgentAuthorizationExtensions.UserMode;

    // 没有卡片回调（无 UI 的宿主进程）就不能问——命令面据此说"没法问"而不是"用户拒绝"。
    public bool CanAsk => confirm != null;

    public Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken cancellationToken)
        => confirm?.Invoke(request, cancellationToken) ?? Task.FromResult(AuthorizationDecision.Reject);
}
