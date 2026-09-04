using System;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Commands;

// 一次需要授权的写的种类——决定确认卡片/回报文案。ProjectEdit=工程编辑（走预览-回退，Count=改动数）；
// ScriptDelete/ScriptOverwrite=脚本库【外部文件】的破坏性改动（无预览，Target=脚本名）。历史记录管理器只保工程
// 数据、保不了外部文件，故后者也必须过同一授权闸门。SettingChange=改宿主设置（Target=设置键、NewValue=新值文本）、
// KeybindingChange=改快捷键（Target=命令 id、NewValue=新手势字形，空=解绑；SecondaryTarget=被夺键而解绑的另一命令）、
// RoutingChange=改扩展路由（Target="kind:identity"、NewValue=选中的包显示名）、
// ExtensionActivationChange=启停某扩展包或其中某个能力（整包时 Target=包名；针对单个能力时 Target="kind:identity"、
//   SecondaryTarget=所属包名。NewValue 恒为动词 "enable"/"disable"，文案按它选句式）：
//   与路由同属"改用户的应用配置、重启后生效"，但后果更重——关掉的能力整个消失（工程里引用它的部分会失配），
//   故同样恒过闸门。
// ExtensionSettingChange=改某扩展自己的设置（Target="扩展名 → 字段键"、NewValue=新值文本）：
// 都不是工程数据、历史记录同样救不回，且是"改用户的应用配置"，故与前者同闸门、同样无预览。
// ProjectExport/ProjectExportOverwrite=把工程导出成文件（Target=落地绝对路径、NewValue=格式显示名）：
// 与脚本库不同，导出路径是【任意的】——调用方能往用户磁盘任何地方写，故【恒】过闸门（不像 save_script 只有覆盖才拦）；
// 落到已存路径会替换那个文件、历史记录救不回，故单列 Overwrite 一档让卡片把"替换"说出来（同 ScriptOverwrite 的分档理由）。
internal enum WriteKind { ProjectEdit, ScriptDelete, ScriptOverwrite, SettingChange, KeybindingChange, RoutingChange, ExtensionSettingChange, ProjectExport, ProjectExportOverwrite, ExtensionActivationChange }

// SecondaryTarget=定位/说明本次改动所需的第二个对象：夺键时是【被顺带解绑的那个命令】（供卡片给出知情同意）；
// 启停单个能力时是【它所属的包名】（同一 kind:identity 跨包可并存，不点名包就说不清关的是哪一份）。
internal readonly record struct AuthorizationRequest(WriteKind Kind, int Count, string? Target, string? NewValue = null, string? SecondaryTarget = null)
{
    // 回报里的动作短语（英文，嵌进"I did NOT {0}"等句）。
    public string ActionPhrase() => Kind switch
    {
        WriteKind.ScriptDelete => string.Format("delete the saved script \"{0}\"", Target),
        WriteKind.ScriptOverwrite => string.Format("overwrite the existing saved script \"{0}\"", Target),
        WriteKind.SettingChange => string.Format("change the setting \"{0}\" to {1}", Target, NewValue),
        WriteKind.KeybindingChange => string.IsNullOrEmpty(NewValue)
            ? string.Format("remove the shortcut for the command \"{0}\"", Target)
            : string.Format("set the shortcut for the command \"{0}\" to {1}", Target, NewValue),
        WriteKind.RoutingChange => string.Format("make \"{1}\" the provider of \"{0}\"", Target, NewValue),
        WriteKind.ExtensionSettingChange => string.Format("change the extension setting \"{0}\" to {1}", Target, NewValue),
        WriteKind.ExtensionActivationChange => string.IsNullOrEmpty(SecondaryTarget)
            ? string.Format("{0} the extension \"{1}\"", NewValue, Target)
            : string.Format("{0} the \"{1}\" capability of \"{2}\"", NewValue, Target, SecondaryTarget),
        WriteKind.ProjectExport => string.Format("export the project as {1} to \"{0}\"", Target, NewValue),
        WriteKind.ProjectExportOverwrite => string.Format("export the project as {1} to \"{0}\", replacing the file already there", Target, NewValue),
        _ => string.Format("apply {0} change(s) to the project", Count),
    };
}

// 授权闸门，按入口注入（docs/command-surface.md §5.1）。每个入口用自己的方式回答"这次写做不做"：
//  · 侧栏 agent —— 读 Settings.AgentAuthorization，Confirm 档弹内联卡片；
//  · CLI（attach）—— --yes 全放开 / 默认走 stdin 交互确认（把 ActionPhrase() 打给用户）/ --dry-run 等价只读建议；
//  · MCP —— 靠工具 annotation 让客户端自己问（批准 UI 在客户端，服务端再问一遍是双重询问且没有 UI 可用）；
//  · headless / CI —— 必须显式给；没给就一条 Edit 都不做（见下面 CommandContext 的扩展）。
//
// 形状要容得下将来的「按能力分维度」（工程编辑 / 应用配置 / 磁盘文件）：调用点只传 request，
// 加维度时改的是各入口的策略实现，不是这十来个调用点。
internal interface IAuthorizationPolicy
{
    // 返回 (Proceed, Message)：Proceed=false 时 Message 是给调用方的"没做/原因"（原样回报）；
    // Proceed=true 时 Message 是可选前缀（如"用户被问过并批准了"），拼在成功文案前。
    Task<(bool Proceed, string Message)> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken);
}

internal static class AuthorizationExtensions
{
    // 入口没给策略 → 一条 Edit 都不做，并说清是"这个入口没配授权"而不是"用户拒绝了"——两者的下一步
    // 完全不同（前者去配，后者别再问）。headless/CI 必须显式给策略，静默放开是不能接受的默认值。
    public static Task<(bool Proceed, string Message)> Authorize(this CommandContext ctx, AuthorizationRequest request, CancellationToken cancellationToken)
        => ctx.Authorization?.AuthorizeAsync(request, cancellationToken)
           ?? Task.FromResult((false, string.Format(
               "This entry point has no authorization policy configured, so nothing that writes is allowed here and I did NOT {0}. "
               + "Whoever runs it must grant write authorization explicitly (there is no silent default).", request.ActionPhrase())));
}
