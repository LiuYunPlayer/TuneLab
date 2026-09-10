using System;
using TuneLab.Configs;

namespace TuneLab.Commands;

// 授权卡片上给**用户**看的那句话。
//
// 与 AuthorizationRequest.ActionPhrase() 分立，因为读者不同：那一句是给**模型**看的（嵌进
// "I did NOT {0}" 之类的回报里，恒英文）；这一句是用户点"允许/拒绝"时唯一的依据，要本地化、
// 要摆出完整路径、要把不可撤销说出来。
//
// 【为什么从界面里抽出来】它原先是 AgentSideBarContentProvider 里一个内联的 switch，于是
// 2.1.0 开发期出过这样的事：WriteKind 从 9 档涨到 23 档，而那个 switch 只有 9 个 case，新增的
// 14 档全部落进 `_` 兜底——而它们传的 Count 都是 0，用户看到的是"Agent 想对工程应用 0 处改动"，
// 底下三个按钮是 允许一次 / 始终允许 / 拒绝。也就是说：覆盖用户的工程文件、丢弃当前文档去开另一个、
// 删预设、卸扩展、往任意路径渲染音频，全都以那句话请求同意，点"始终允许"还会把整个闸门抬到 Auto。
// 抽出来是为了能给它上一条封条（EveryWriteKindHasItsOwnSentence）——那条测试遍历 WriteKind 的每个值，
// 少写一句就红。
internal static class AuthorizationCardText
{
    // tr：把英文原文换成界面语言的译文。界面传 s => s.Tr(面板)（词条仍归 [AgentSideBarContentProvider]
    // 那一节，既有译文不动）；测试传恒等函数。
    public static string For(AuthorizationRequest request, Func<string, string> tr)
        => Localized(request, tr) ?? Fallback(request);

    // 逐档的本地化句子；**没写的档返回 null**。
    // 【为什么不让封条去比对兜底文案】有几档（ProjectEdit / ExtensionSettingChange / PresetRename）的
    // 正确句子与兜底逐字相同——ActionPhrase 本来就是那句话——于是"文案==兜底"会把写好的档误判成漏写。
    // 返回 null 是结构上的判定，不受措辞巧合影响。
    public static string? Localized(AuthorizationRequest request, Func<string, string> tr)
    {
        string target = request.Target ?? "";
        string value = request.NewValue ?? "";

        return request.Kind switch
        {
            // 工程编辑：改动数就是这句话的全部内容（撤销栈救得回，故不必吓人）。
            WriteKind.ProjectEdit => string.Format(tr("The agent wants to apply {0} change(s) to the project."), request.Count),

            WriteKind.ScriptDelete => string.Format(tr("The agent wants to delete the saved script \"{0}\". This can't be undone."), target),
            WriteKind.ScriptOverwrite => string.Format(tr("The agent wants to overwrite the saved script \"{0}\". This can't be undone."), target),
            WriteKind.SettingChange => string.Format(tr("The agent wants to change the setting \"{0}\" to {1}."), SettingDisplayLabel(request.Target), value),

            // 快捷键：绑/解绑两句；夺键时再补一句点名被解绑的命令（知情同意）。命令名用本地化显示名，模型侧才用 id。
            WriteKind.KeybindingChange => (string.IsNullOrEmpty(request.NewValue)
                    ? string.Format(tr("The agent wants to remove the shortcut for \"{0}\"."), KeybindingText.LabelOf(target))
                    : string.Format(tr("The agent wants to set the shortcut for \"{0}\" to {1}."), KeybindingText.LabelOf(target), value))
                + (string.IsNullOrEmpty(request.SecondaryTarget) ? "" :
                    " " + string.Format(tr("This also unbinds the shortcut of \"{0}\"."), request.SecondaryTarget)),

            WriteKind.RoutingChange => string.Format(tr("The agent wants \"{1}\" to be the package that provides \"{0}\" (takes effect after a restart)."), target, value),
            WriteKind.ExtensionSettingChange => string.Format(tr("The agent wants to change the extension setting \"{0}\" to {1}."), target, value),

            // 启停：整包与单个能力两种口径，开与关又各一句——四句都写全，不拿"设为 enable/disable"这种
            // 机器味的通用句糊过去（关掉一个能力等于让它从本次运行里消失，用户得一眼看懂关的是什么）。
            WriteKind.ExtensionActivationChange => string.IsNullOrEmpty(request.SecondaryTarget)
                ? (value == "enable"
                    ? string.Format(tr("The agent wants to enable the extension \"{0}\" (takes effect after a restart)."), target)
                    : string.Format(tr("The agent wants to disable the extension \"{0}\" (takes effect after a restart)."), target))
                : (value == "enable"
                    ? string.Format(tr("The agent wants to enable the \"{0}\" capability of \"{1}\" (takes effect after a restart)."), target, request.SecondaryTarget)
                    : string.Format(tr("The agent wants to disable the \"{0}\" capability of \"{1}\" (takes effect after a restart)."), target, request.SecondaryTarget)),

            // 装 / 卸 / 撤销卸载：三件事的下一步完全不同（装立刻生效、卸要重启、撤销卸载是"别卸了"），
            // 故三句分开。
            WriteKind.ExtensionInstall => string.Format(tr("The agent wants to install the extension from:\n{0}"), target),
            WriteKind.ExtensionUninstall => value == "cancel"
                ? string.Format(tr("The agent wants to keep the extension \"{0}\" after all, taking back its pending uninstall."), target)
                : string.Format(tr("The agent wants to uninstall the extension \"{0}\". Its files are removed when TuneLab restarts."), target),

            // 【写盘那一族：卡片必须摆出完整落地路径】路径是任意的，用户只有看到它才能判断这一下写到哪。
            // 覆盖一律另起一句，别把"替换掉已有文件"混在同一句里说轻了。
            WriteKind.ProjectExport => string.Format(tr("The agent wants to export the project as {1} to:\n{0}"), target, value),
            WriteKind.ProjectExportOverwrite => string.Format(tr("The agent wants to export the project as {1} to:\n{0}"), target, value)
                + "\n" + tr("A file already exists there and will be replaced. This can't be undone."),

            // 保存：与导出的分野必须说出来——export 只写一份副本，这几条是真的保存（改写那个文件、
            // 把工程的保存路径挪过去、清掉未保存标记），撤销栈救不回其中任何一件。
            WriteKind.ProjectSave => string.Format(tr("The agent wants to save the project back to its own file:\n{0}"), target)
                + "\n" + tr("This really saves — it overwrites that file. This can't be undone."),
            WriteKind.ProjectSaveAs => string.Format(tr("The agent wants to save the project as:\n{0}"), target)
                + "\n" + tr("From then on that is the file the project saves to."),
            WriteKind.ProjectSaveAsOverwrite => string.Format(tr("The agent wants to save the project as:\n{0}"), target)
                + "\n" + tr("From then on that is the file the project saves to.")
                + "\n" + tr("A file already exists there and will be replaced. This can't be undone."),

            // 渲染音频：这一条还要占住机器几分钟，那句代价必须在卡片上——用户是据它决定现在放不放行的。
            WriteKind.ProjectExportAudio => string.Format(tr("The agent wants to render the project's audio to:\n{0}"), target)
                + "\n" + tr("This runs the full synthesis and mixdown first, which can take several minutes. TuneLab is unusable until it finishes."),
            WriteKind.ProjectExportAudioOverwrite => string.Format(tr("The agent wants to render the project's audio to:\n{0}"), target)
                + "\n" + tr("This runs the full synthesis and mixdown first, which can take several minutes. TuneLab is unusable until it finishes.")
                + "\n" + tr("A file already exists there and will be replaced. This can't be undone."),

            // 换工程：丢掉的是用户此刻开着的那份文档，连同它的撤销历史。
            WriteKind.ProjectOpen => string.Format(tr("The agent wants to open this project, closing the one you have open now:\n{0}"), target)
                + "\n" + tr("The undo history of the current project goes with it."),

            // preset：改的是这个 part 怎么唱，不是它唱什么——这句要说出来，否则用户会以为内容被动了。
            WriteKind.PresetApply => string.IsNullOrEmpty(request.Target)
                ? string.Format(tr("The agent wants to reset {0} to its sound source's defaults."), request.SecondaryTarget)
                : string.Format(tr("The agent wants to apply the preset \"{0}\" to {1}."), target, request.SecondaryTarget),
            WriteKind.PresetOverwrite => string.Format(tr("The agent wants to replace the existing preset \"{0}\". This can't be undone."), target),
            WriteKind.PresetDelete => string.Format(tr("The agent wants to delete the preset \"{0}\". This can't be undone."), target),
            WriteKind.PresetRename => string.Format(tr("The agent wants to rename the preset \"{0}\" to \"{1}\"."), target, value),

            // 编辑器动作：卡片要点名是哪一条（本地化显示名），"跑一下某个动作"说了等于没说。
            WriteKind.EditorAction => string.Format(tr("The agent wants to run the editor action \"{0}\"."), ActionLabel(request)),
            WriteKind.EditorActionDestructive => string.Format(tr("The agent wants to run the editor action \"{0}\", which discards work that the undo history cannot bring back."), ActionLabel(request)),

            _ => null,
        };
    }

    // 兜底：没有写本地化句子的档，至少给出**正确**的英文——那正是给模型看的那一句。
    // 【为什么不是一句通用话】从前这里是 "apply {Count} change(s)"，而那些档传的 Count 都是 0，
    // 于是用户看到"想对工程应用 0 处改动"：既不正确、也没有信息，而他正要据此点"允许"。
    // 英文但正确，好过本地化但撒谎。封条盯着这里：正常情况下任何一档都不该走到这。
    public static string Fallback(AuthorizationRequest request)
        => "The agent wants to " + request.ActionPhrase() + ".";

    // 动作 id → 用户看见的本地化显示名（查不到就退回 id）。
    static string ActionLabel(AuthorizationRequest request)
    {
        var label = KeybindingText.LabelOf(request.Target ?? "");
        return string.IsNullOrEmpty(label) ? request.Target ?? "" : label;
    }

    // 设置键 → 用户看见的本地化行标（找不到则退回键本身，如注册表条目已改名）。
    static string SettingDisplayLabel(string? key)
    {
        foreach (var item in SettingsRegistry.All)
            if (item.Key == key)
                return item.DisplayLabel;
        return key ?? "";
    }
}
