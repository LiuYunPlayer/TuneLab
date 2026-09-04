using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TuneLab.GUI.Input;
using TuneLab.Input;
using TuneLab.Commands;

namespace TuneLab.Agent;

// 快捷键能力的【写】那一半，尚未搬进命令面（读那一半已是 `keybinding list`，见
// TuneLab/Commands/Handlers/KeybindingCommands.cs）。直接读写宿主的 Keymap——命令表、生效手势、
// 冲突判定、落盘广播都在那里，这里不复制任何一份判据；手势的文本口径来自共享的 KeybindingText。
//
// 与脚本库闭环：save_script 存下的工具脚本会注册成命令 id `script:<稳定 id>`（由脚本目录监视器同步，见
// ScriptToolMenu.SyncKeyCommands），故「帮我写个功能并绑个快捷键」现在能一路做完。

// 改一条绑定：绑手势 / 解绑（gesture="" ）/ 恢复默认（reset=true）。改用户的应用配置 → 过 ToolAuthorization 闸门。
// 同域冲突默认【拒绝】，要 replaceConflict:true 才夺键（并解除原命令的绑定）——与设置页录制时"已被占用，是否改绑"
// 那道确认等价，不让 agent 悄悄抢走别的命令的键。
internal sealed class SetKeybindingTool(Func<AuthorizationRequest, CancellationToken, Task<ScriptAuthDecision>>? confirm = null) : IAgentTool
{
    public string Name => "set_keybinding";

    public string Description =>
        "Bind, unbind or reset ONE command's keyboard shortcut (get the command id and a free gesture from list_keybindings first). Takes effect immediately and is saved. " +
        "Pass `gesture` to bind it, `gesture` as \"\" to unbind, or `reset` = true to restore that command's default. " +
        "If the gesture is already used by another command in the SAME area the call is refused and names that command — pick another gesture, or pass replaceConflict = true to take it over (which unbinds the other command). " +
        "This changes the user's configuration and is not part of the project's undo history, so it needs the user's authorization; if it is refused, tell the user to change it in the Settings window's Keybindings page.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "id": { "type": "string", "description": "Command id exactly as listed by list_keybindings (e.g. \"edit.undo\", \"script:my-tool\")." },
            "gesture": { "type": "string", "description": "The new gesture, e.g. \"ctrl+shift+p\" (\"mod+\" = Ctrl on Windows/Linux, Cmd on macOS). Empty string unbinds the command. Omit when using reset." },
            "reset": { "type": "boolean", "description": "True = restore this command's default shortcut (ignores gesture)." },
            "replaceConflict": { "type": "boolean", "description": "True = if another command in the same area already uses this gesture, unbind that one and take the gesture. Default false (the call is refused instead)." }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string id;
        string? gesture;
        bool reset, replaceConflict;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            id = doc.RootElement.GetString("id");
            gesture = doc.RootElement.GetStringOrNull("gesture");
            reset = doc.RootElement.GetBoolOrNull("reset") ?? false;
            replaceConflict = doc.RootElement.GetBoolOrNull("replaceConflict") ?? false;
        }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        var given = (id ?? "").Trim();
        // 命令表在 UI 线程被增删（脚本命令随菜单/文件监视器同步），故 id 归一 + 计划 + 后续写都在 UI 线程取一致快照。
        var (resolvedId, plan) = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var rid = ResolveId(given);
            return (rid, Plan(rid, gesture, reset, replaceConflict));
        });
        if (plan.Error != null)
            return plan.Error;
        if (plan.NoOp != null)
            return plan.NoOp;

        var (proceed, message) = await ToolAuthorization.AuthorizeAsync(
            new AuthorizationRequest(WriteKind.KeybindingChange, 0, resolvedId, plan.NewGestureText, plan.ConflictLabel), confirm, cancellationToken);
        if (!proceed)
            return message;

        return message + await Dispatcher.UIThread.InvokeAsync(() => Apply(resolvedId, plan));
    }

    // 命令 id 归一：精确匹配优先，否则忽略大小写找一条（模型常把 id 大小写写错，没必要为此失败）；都不中则原样返回、由 Plan 报错。
    static string ResolveId(string id)
    {
        if (id.Length == 0 || Keymap.TryGet(id, out _))
            return id;
        foreach (var cmd in Keymap.Commands)
            if (string.Equals(cmd.Id, id, StringComparison.OrdinalIgnoreCase))
                return cmd.Id;
        return id;
    }

    // 一次改动的计划（在 UI 线程一次算出）：要写什么、准不准夺键、以及给闸门/回报的文案。
    readonly record struct ChangePlan(string? Error, string? NoOp, bool Reset, KeyBinding? Binding, string NewGestureText, bool Replace, string? ConflictLabel);

    static ChangePlan Plan(string id, string? gesture, bool reset, bool replaceConflict)
    {
        if (id.Length == 0)
            return new ChangePlan("Error: \"id\" is empty. Call list_keybindings to see command ids.", null, false, null, "", false, null);
        if (!Keymap.TryGet(id, out var cmd))
            return new ChangePlan(string.Format(
                "Error: no bindable command with id \"{0}\". Call list_keybindings to see the ids. (A script saved with save_script becomes \"script:<its id>\" once the app has picked the file up — list again if it is not there yet.)", id),
                null, false, null, "", false, null);

        var effective = Keymap.Effective(id);

        if (reset)
        {
            if (!Keymap.HasOverride(id))
                return new ChangePlan(null, string.Format("\"{0}\" is already at its default shortcut ({1}). Nothing changed.",
                    cmd.DisplayName(), effective is { } e ? KeybindingText.Gesture(e) : "none"), false, null, "", false, null);
            return new ChangePlan(null, null, true, cmd.DefaultGesture,
                cmd.DefaultGesture is { } d ? KeyCodec.ToDisplay(d) : "", false, null);
        }

        // gesture 缺省或空串 = 解绑（与设置页的"解绑"等价；显式 override 成 null，不回落默认）。
        if (string.IsNullOrWhiteSpace(gesture))
        {
            if (effective == null)
                return new ChangePlan(null, string.Format("\"{0}\" has no shortcut already. Nothing changed.", cmd.DisplayName()), false, null, "", false, null);
            return new ChangePlan(null, null, false, null, "", false, null);
        }

        // 声明式解析：额外接受 "mod+"/"primary+" 别名（解析成本平台的主命令键），落盘仍是物理修饰。
        if (!KeyCodec.TryParseDeclaration(gesture, out var binding))
            return new ChangePlan(string.Format("Error: \"{0}\" is not a valid gesture. {1}", gesture, KeybindingText.GestureSyntax),
                null, false, null, "", false, null);
        if (!KeyCodec.IsSupported(binding.Key))
            return new ChangePlan(string.Format("Error: that key cannot be bound. {0}", KeybindingText.GestureSyntax), null, false, null, "", false, null);

        if (effective is { } cur && cur.Equals(binding))
            return new ChangePlan(null, string.Format("\"{0}\" is already bound to {1}. Nothing changed.", cmd.DisplayName(), KeybindingText.Gesture(binding)),
                false, null, "", false, null);

        var conflictId = Keymap.FindConflict(id, binding);
        if (conflictId != null && !replaceConflict)
            return new ChangePlan(string.Format(
                "Error: {0} is already used by \"{1}\" (id {2}) in the same area ({3}), so nothing changed. Pick a free gesture (list_keybindings shows what is taken), or call again with replaceConflict = true to take it over — that unbinds \"{1}\".",
                KeybindingText.Gesture(binding), KeybindingText.LabelOf(conflictId), conflictId, cmd.Scope),
                null, false, null, "", false, null);

        return new ChangePlan(null, null, false, binding, KeyCodec.ToDisplay(binding), replaceConflict,
            conflictId == null ? null : KeybindingText.LabelOf(conflictId));
    }

    // 落地（UI 线程）：Keymap.Rebind/ResetToDefault 自带落盘 + Changed 广播（菜单与设置页即时刷新）。
    static string Apply(string id, ChangePlan plan)
    {
        var label = KeybindingText.LabelOf(id);
        var sb = new StringBuilder();

        if (plan.Reset)
        {
            Keymap.ResetToDefault(id);
            var now = Keymap.Effective(id);
            sb.Append(string.Format("Reset \"{0}\" to its default shortcut: {1}.", label,
                now is { } g ? KeybindingText.Gesture(g) : "no shortcut"));
        }
        else if (plan.Binding is not { } binding)
        {
            Keymap.Rebind(id, null);
            sb.Append(string.Format("Removed the shortcut for \"{0}\".", label));
        }
        else
        {
            // 冲突按【落地这一刻】重查：闸门在等用户裁决期间，用户可能已在设置页自己改了绑定。
            // 计划期无冲突而此刻有 → 未获夺键许可，宁可什么都不做（绝不悄悄抢走别的命令的键）。
            var conflictId = Keymap.FindConflict(id, binding);
            if (conflictId != null && !plan.Replace)
                return string.Format("Nothing changed: {0} got taken by \"{1}\" (id {2}) while waiting for the user. Pick another gesture, or call again with replaceConflict = true.",
                    KeybindingText.Gesture(binding), KeybindingText.LabelOf(conflictId), conflictId);
            if (conflictId != null)
                Keymap.Rebind(conflictId, null);   // 夺键：先解除原命令（与设置页确认后的行为一致）
            Keymap.Rebind(id, binding);
            sb.Append(string.Format("Bound \"{0}\" to {1}.", label, KeybindingText.Gesture(binding)));
            if (conflictId != null)
                sb.Append(string.Format(" \"{0}\" (id {1}) lost that shortcut and is now unbound — tell the user.", KeybindingText.LabelOf(conflictId), conflictId));
            // 跨域同手势不是冲突（聚焦哪层哪层生效），但如实告知，免得用户以为某个"失灵"。
            var others = KeybindingText.OtherScopeUsers(id, binding);
            if (others.Count > 0)
                sb.Append(string.Format(" Note: {0} also use(s) this gesture in another area — both stay active, the focused area wins.",
                    string.Join(", ", others.Select(c => "\"" + c.DisplayName() + "\""))));
        }
        sb.Append(" Saved; it works right away (no restart).");
        KeybindingText.AppendConflicts(sb, id, " ");
        return sb.ToString();
    }
}

// 快捷键的文本化 + 跨域/冲突查询。两工具共用，判据全来自 Keymap / KeyCodec。
