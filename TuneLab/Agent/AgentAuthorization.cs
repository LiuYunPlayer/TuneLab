using System;

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
