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
}

// Confirm 档下、agent 要写时的用户裁决（内联升级卡片返回）：
//  · ApplyOnce   本次落地，档位不变；
//  · ApplyAlways 本次落地，并把授权切到 Auto（此后不再逐次问）；
//  · Reject      不落地。
internal enum ScriptAuthDecision { ApplyOnce, ApplyAlways, Reject }

// 工程之外的写（删/覆盖脚本库文件、改宿主设置）的授权闸门。与工程写共用 Settings.AgentAuthorization + 同一确认卡片，
// 但【无预览-回退】——这些操作不能像脚本那样试运行再回退，故直接按档决定做/问/不做。
//  · Auto           直接做；
//  · ReadOnlyAdvice 不做、只回报会做什么 + 提示手动或提权；
//  · Confirm        经 confirm 卡片裁决：应用本次/始终允许(切 Auto) 才做、拒绝不做；无 UI 回调则保守不做。
internal static class ToolAuthorization
{
    // 返回 (Proceed, Message)：Proceed=false 时 Message 是回灌模型的"没做/原因"（直接返回它）；
    // Proceed=true 时 Message 是可选前缀（如"已切自动"通知），拼在成功文案前。
    public static async Task<(bool Proceed, string Message)> AuthorizeAsync(
        AuthorizationRequest request,
        Func<AuthorizationRequest, CancellationToken, Task<ScriptAuthDecision>>? confirm,
        CancellationToken cancellationToken)
    {
        var level = AgentAuthorizationExtensions.ParseOrDefault(Settings.AgentAuthorization.Value);
        if (level == AgentAuthorization.Auto)
            return (true, "");
        if (level == AgentAuthorization.ReadOnlyAdvice)
            return (false, string.Format(
                "Authorization is READ-ONLY (advice mode): I did NOT {0}. Do it yourself, or raise agent authorization to Confirm or Auto.", request.ActionPhrase()));
        if (confirm == null)
            return (false, string.Format(
                "Confirmation is required (Confirm mode) but no UI is available to ask, so I did NOT {0}.", request.ActionPhrase()));

        var decision = await confirm(request, cancellationToken);
        if (decision == ScriptAuthDecision.Reject)
            return (false, string.Format("The user chose NOT to allow it, so I did NOT {0}.", request.ActionPhrase()));
        // 前缀必须把「确认这件事本身发生过」说出来：否则 ApplyOnce 的回报与 Auto 档一字不差，模型无从知道
        // 用户被问过、也判不出自己现在是哪个档位 —— 实测表现为反复追问"是不是弹了卡片"。
        return (true, decision == ScriptAuthDecision.ApplyAlways
            ? "(The user was asked to confirm, approved it, and switched authorization to auto-apply; later actions won't ask.)\n"
            : "(The user was asked to confirm and approved this one; authorization stays at Confirm, so the next action will ask again.)\n");
    }
}

// 侧栏 agent 的授权策略（命令面按入口注入的那一个，见 docs/command-surface.md §5.1）：
// 判据与尚未搬家的 agent 工具完全同一份——都走 ToolAuthorization，故两边不可能给出不同的档位行为。
internal sealed class AgentAuthorizationPolicy(Func<AuthorizationRequest, CancellationToken, Task<ScriptAuthDecision>>? confirm) : IAuthorizationPolicy
{
    public Task<(bool Proceed, string Message)> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken)
        => ToolAuthorization.AuthorizeAsync(request, confirm, cancellationToken);
}
