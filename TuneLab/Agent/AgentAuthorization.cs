using System;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Configs;

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

// agent 一次写请求的种类——决定升级卡片/回报文案。ProjectEdit=工程编辑（走预览-回退，Count=改动数）；
// ScriptDelete/ScriptOverwrite=脚本库【外部文件】的破坏性改动（无预览，Target=脚本名）。历史记录管理器只保工程
// 数据、保不了外部文件，故后者也必须过同一授权闸门。SettingChange=改宿主设置（Target=设置键、NewValue=新值文本）、
// KeybindingChange=改快捷键（Target=命令 id、NewValue=新手势字形，空=解绑；SecondaryTarget=被夺键而解绑的另一命令）、
// RoutingChange=改扩展路由（Target="kind:identity"、NewValue=选中的包显示名）、
// ExtensionSettingChange=改某扩展自己的设置（Target="扩展名 → 字段键"、NewValue=新值文本）：
// 都不是工程数据、历史记录同样救不回，且是"改用户的应用配置"，故与前者同闸门、同样无预览。
internal enum AgentWriteKind { ProjectEdit, ScriptDelete, ScriptOverwrite, SettingChange, KeybindingChange, RoutingChange, ExtensionSettingChange }

// SecondaryTarget=本次改动【顺带影响的另一个对象】（当前只有夺键：被解除绑定的那个命令），供卡片给出知情同意。
internal readonly record struct AgentAuthorizationRequest(AgentWriteKind Kind, int Count, string? Target, string? NewValue = null, string? SecondaryTarget = null)
{
    // 回灌模型的动作短语（英文，嵌进"I did NOT {0}"等句）。
    public string ActionPhrase() => Kind switch
    {
        AgentWriteKind.ScriptDelete => string.Format("delete the saved script \"{0}\"", Target),
        AgentWriteKind.ScriptOverwrite => string.Format("overwrite the existing saved script \"{0}\"", Target),
        AgentWriteKind.SettingChange => string.Format("change the setting \"{0}\" to {1}", Target, NewValue),
        AgentWriteKind.KeybindingChange => string.IsNullOrEmpty(NewValue)
            ? string.Format("remove the shortcut for the command \"{0}\"", Target)
            : string.Format("set the shortcut for the command \"{0}\" to {1}", Target, NewValue),
        AgentWriteKind.RoutingChange => string.Format("make \"{1}\" the provider of \"{0}\"", Target, NewValue),
        AgentWriteKind.ExtensionSettingChange => string.Format("change the extension setting \"{0}\" to {1}", Target, NewValue),
        _ => string.Format("apply {0} change(s) to the project", Count),
    };
}

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
        AgentAuthorizationRequest request,
        Func<AgentAuthorizationRequest, CancellationToken, Task<ScriptAuthDecision>>? confirm,
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
        return (true, decision == ScriptAuthDecision.ApplyAlways
            ? "(The user switched authorization to auto-apply; later actions won't ask.)\n"
            : "");
    }
}
