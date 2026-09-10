using System;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Commands;

// 一次需要授权的写的种类——决定确认卡片/回报文案。ProjectEdit=工程编辑（走预览-回退，Count=改动数）；
// ScriptDelete/ScriptOverwrite=脚本库【外部文件】的破坏性改动（无预览，Target=脚本名）。历史记录管理器只保工程
// 数据、保不了外部文件，故后者也必须过同一授权闸门。SettingChange=改宿主设置（Target=设置键、NewValue=新值文本）、
// KeybindingChange=改快捷键（Target=动作 id、NewValue=新手势字形，空=解绑；SecondaryTarget=被夺键而解绑的另一动作）、
// RoutingChange=改扩展路由（Target="kind:identity"、NewValue=选中的包显示名）、
// ExtensionActivationChange=启停某扩展包或其中某个能力（整包时 Target=包名；针对单个能力时 Target="kind:identity"、
//   SecondaryTarget=所属包名。NewValue 恒为动词 "enable"/"disable"，文案按它选句式）：
//   与路由同属"改用户的应用配置、重启后生效"，但后果更重——关掉的能力整个消失（工程里引用它的部分会失配），
//   故同样恒过闸门。
// ExtensionSettingChange=改某扩展自己的设置（Target="扩展名 → 字段键"、NewValue=新值文本）：
// 都不是工程数据、历史记录同样救不回，且是"改用户的应用配置"，故与前者同闸门、同样无预览。
// EditorAction/EditorActionDestructive=从命令面触发一条编辑器动作（Target=动作显示名，NewValue=带选择器参数的
// 动作作用在哪个成员上、无参动作留空）：动作没法像脚本那样
// 先跑一遍预览"会改多少"（它就是一次界面操作），故走一问一答而非预览-裁决。分两档的理由同 ProjectExport 的
// 分档：进撤销栈的（剪贴板动词、移调）Ctrl+Z 能救回，而新建/打开/保存可能丢掉未保存的工作或改写磁盘上的
// 文件——后果不同就必须让卡片说出不同的话。只改应用自身状态的动作（播放、切工具、开合面板）**不过闸门**，
// 故这里没有它们的档：见 ActionKind.AppState。
// PresetApply=把一条 part preset 用在某个 part 上（Target=preset 名，null=恢复声源默认；SecondaryTarget=part 定位）：
// 它写的是工程数据、且是**一个撤销步**，故与 EditorAction 同档次的一问一答（没有预览可跑——preset 不是脚本，
// 它就是一次赋值）。措辞要说清"改的是声音不是内容"：曲线/音符/音素一个都不动。
// PresetOverwrite=存 preset 时替换掉同名的那一条（Target=名字）：新建一条不过闸门（同 save_script——
// 加一个文件是加东西），替换掉一条等于删掉用户已有的东西，故只有这一支拦。
// PresetDelete=删一条 preset（Target=名字）、PresetRename=改名（Target=旧名、NewValue=新名）：
// 都动用户配置目录里的文件，历史记录救不回，故恒过闸门（同 ScriptDelete）。
// ProjectExport/ProjectExportOverwrite=把工程导出成文件（Target=落地绝对路径、NewValue=格式显示名）：
// 与脚本库不同，导出路径是【任意的】——调用方能往用户磁盘任何地方写，故【恒】过闸门（不像 save_script 只有覆盖才拦）；
// 落到已存路径会替换那个文件、历史记录救不回，故单列 Overwrite 一档让卡片把"替换"说出来（同 ScriptOverwrite 的分档理由）。
internal enum WriteKind { ProjectEdit, ScriptDelete, ScriptOverwrite, SettingChange, KeybindingChange, RoutingChange, ExtensionSettingChange, ProjectExport, ProjectExportOverwrite, ExtensionActivationChange, EditorAction, EditorActionDestructive, ProjectOpen, ExtensionInstall, ExtensionUninstall, PresetApply, PresetOverwrite, PresetDelete, PresetRename }

// SecondaryTarget=定位/说明本次改动所需的第二个对象：夺键时是【被顺带解绑的那个动作】（供卡片给出知情同意）；
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
            ? string.Format("remove the shortcut for the action \"{0}\"", Target)
            : string.Format("set the shortcut for the action \"{0}\" to {1}", Target, NewValue),
        WriteKind.RoutingChange => string.Format("make \"{1}\" the provider of \"{0}\"", Target, NewValue),
        WriteKind.ExtensionSettingChange => string.Format("change the extension setting \"{0}\" to {1}", Target, NewValue),
        WriteKind.ExtensionActivationChange => string.IsNullOrEmpty(SecondaryTarget)
            ? string.Format("{0} the extension \"{1}\"", NewValue, Target)
            : string.Format("{0} the \"{1}\" capability of \"{2}\"", NewValue, Target, SecondaryTarget),
        WriteKind.EditorAction => string.IsNullOrEmpty(NewValue)
            ? string.Format("run the editor action \"{0}\" (it edits the project and goes into the undo history)", Target)
            : string.Format("run the editor action \"{0}\" on \"{1}\" (it edits the project and goes into the undo history)", Target, NewValue),
        WriteKind.EditorActionDestructive => string.IsNullOrEmpty(NewValue)
            ? string.Format("run the editor action \"{0}\" (it can discard unsaved work or write files to disk, and the undo history cannot bring that back)", Target)
            : string.Format("run the editor action \"{0}\" on \"{1}\" (it can discard unsaved work or write files to disk, and the undo history cannot bring that back)", Target, NewValue),
        WriteKind.PresetApply => string.IsNullOrEmpty(Target)
            ? string.Format("reset {0} to its sound source's defaults (it changes how that part sounds, not what it sings — undo takes it back)", SecondaryTarget)
            : string.Format("apply the preset \"{0}\" to {1} (it changes how that part sounds, not what it sings — undo takes it back)", Target, SecondaryTarget),
        WriteKind.PresetOverwrite => string.Format("replace the existing preset \"{0}\"", Target),
        WriteKind.PresetDelete => string.Format("delete the preset \"{0}\" (the file is gone for good; the undo history does not cover it)", Target),
        WriteKind.PresetRename => string.Format("rename the preset \"{0}\" to \"{1}\"", Target, NewValue),
        WriteKind.ExtensionInstall => string.Format(
            "install the extension \"{0}\" from \"{1}\" (it unpacks third-party code into the extensions folder and loads it right now)", Target, NewValue),
        WriteKind.ExtensionUninstall => NewValue == "uninstall"
            ? string.Format("uninstall the extension \"{0}\" (its files are deleted the next time TuneLab starts)", Target)
            : string.Format("keep the extension \"{0}\" after all (take back its pending uninstall)", Target),
        WriteKind.ProjectOpen => string.Format(
            "open the project \"{0}\", closing the one the user has open right now (its undo history goes with it)", Target),
        WriteKind.ProjectExport => string.Format("export the project as {1} to \"{0}\"", Target, NewValue),
        WriteKind.ProjectExportOverwrite => string.Format("export the project as {1} to \"{0}\", replacing the file already there", Target, NewValue),
        _ => string.Format("apply {0} change(s) to the project", Count),
    };
}

// 这个入口当前放行到什么程度。工程编辑要据它决定"要不要先跑一遍预览"，故策略不能只有一问一答。
internal enum AuthorizationMode
{
    ReadOnlyAdvice,   // 只读建议：照跑但一律回退、只呈现"会改什么"，从不落地
    Confirm,          // 需确认：先问用户，允许了才落地
    Auto,             // 全自动：直接落地
}

// 两个档位取更严的那一档。用处：入口自己声明的档位要被【用户设定】压顶（见 BridgeAuthorizationPolicy）。
// 【不比较枚举数值】——那把"谁更严"押在成员的书写顺序上，将来插一档就可能静默改变判据，
// 而这个判据错一次的后果是外部进程拿到了用户没给的权限。
internal static class AuthorizationModes
{
    public static AuthorizationMode Stricter(AuthorizationMode a, AuthorizationMode b)
        => a == AuthorizationMode.ReadOnlyAdvice || b == AuthorizationMode.ReadOnlyAdvice ? AuthorizationMode.ReadOnlyAdvice
            : a == AuthorizationMode.Confirm || b == AuthorizationMode.Confirm ? AuthorizationMode.Confirm
            : AuthorizationMode.Auto;
}

// Confirm 档下用户的裁决：ApplyOnce 本次落地；ApplyAlways 本次落地并把档位切到 Auto（此后不再逐次问）；
// Reject 不落地。切档由策略实现方自己完成（命令只据裁决决定做不做，以及回报里怎么说）。
internal enum AuthorizationDecision { ApplyOnce, ApplyAlways, Reject }

// 授权闸门，按入口注入（docs/command-surface.md §5.1）。每个入口用自己的方式回答"这次写做不做"：
//  · 侧栏 agent —— 读 Settings.AgentAuthorization，Confirm 档弹内联卡片；
//  · CLI（attach）—— --yes 全放开 / 默认走 stdin 交互确认（把 ActionPhrase() 打给用户）/ --dry-run 等价只读建议；
//  · MCP —— 靠工具 annotation 让客户端自己问（批准 UI 在客户端，服务端再问一遍是双重询问且没有 UI 可用）；
//    这两个都经命令桥进来，故声明什么档位都还要被【用户设定】压顶一次（BridgeAuthorizationPolicy）——
//    外部进程不可能比用户给自家侧栏 agent 的权限更大；
//  · headless / CI —— 必须显式给；没给就一条 Edit 都不做（见下面 CommandContext 的扩展）。
//
// 入口只答三个【原语】：档位、能不能问、问一次。两类写的完整流程与措辞都由命令面拼——
//  · 一问一答（改设置/改键位/删文件…）：下面的默认实现 AuthorizeAsync；
//  · 预览-裁决（工程编辑，先跑一遍看会改多少）：ScriptWriteExecutor。
// 这样十来条 edit 命令的说辞不可能各入口一个样，将来加"按能力分维度"也只改各入口的策略实现。
internal interface IAuthorizationPolicy
{
    AuthorizationMode Mode { get; }

    // 能不能问用户。Confirm 档下要区分"用户拒绝了"与"这里根本没法问"——两句话的下一步完全不同。
    bool CanAsk { get; }

    // Confirm 档下把这次写呈现给用户并等裁决。CanAsk 为 false 时不会被调用。
    Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken cancellationToken);

    // 一问一答那类写的标准流程：Auto 直接做 / 只读档不做只建议 / Confirm 问一次。
    // 返回 (Proceed, Message)：Proceed=false 时 Message 是给调用方的"没做/原因"（原样回报）；
    // Proceed=true 时 Message 是可选前缀（如"用户被问过并批准了"），拼在成功文案前。
    async Task<(bool Proceed, string Message)> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken)
    {
        if (Mode == AuthorizationMode.Auto)
            return (true, "");
        if (Mode == AuthorizationMode.ReadOnlyAdvice)
            return (false, string.Format(
                "Authorization is READ-ONLY (advice mode): I did NOT {0}. Do it yourself, or raise agent authorization to Confirm or Auto.", request.ActionPhrase()));
        if (!CanAsk)
            return (false, string.Format(
                "Confirmation is required (Confirm mode) but no UI is available to ask, so I did NOT {0}.", request.ActionPhrase()));

        var decision = await AskAsync(request, cancellationToken);
        if (decision == AuthorizationDecision.Reject)
            return (false, string.Format("The user chose NOT to allow it, so I did NOT {0}.", request.ActionPhrase()));
        // 前缀必须把「确认这件事本身发生过」说出来：否则 ApplyOnce 的回报与 Auto 档一字不差，调用方无从知道
        // 用户被问过、也判不出自己现在是哪个档位 —— 实测表现为模型反复追问"是不是弹了卡片"。
        return (true, decision == AuthorizationDecision.ApplyAlways
            ? "(The user was asked to confirm, approved it, and switched authorization to auto-apply; later actions won't ask.)\n"
            : "(The user was asked to confirm and approved this one; authorization stays at Confirm, so the next action will ask again.)\n");
    }
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
