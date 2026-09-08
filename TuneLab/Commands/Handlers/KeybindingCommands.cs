using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.GUI.Input;
using TuneLab.Input;

namespace TuneLab.Commands.Handlers;

// 快捷键能力的读那一半：哪些【动作】可绑、绑的是什么手势。直接读宿主的 Keymap（可绑条目、生效手势、
// 冲突判定都在那里），动作的显示名去 ActionRegistry 问，这里不复制任何一份判据；手势的文本口径来自
// 共享的 KeybindingText。动作本身有哪些、能不能跑，是 `action list` 的事。
//
// 同时给出手势语法与"用户自己在哪改"，好让调用方能教用户动手而不只是代劳。
internal sealed class KeybindingListCommand : ICommand
{
    public string Path => "keybinding list";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "list_keybindings";

    public string Brief => "List bindable actions with their effective shortcuts";

    public string Documentation =>
        "List TuneLab's bindable actions and their keyboard shortcuts: action id, label, area (scope), the effective gesture, whether it is the default or the user's own override, and any conflict. " +
        "Use it to answer \"what is the shortcut for X\" / \"how do I rebind X\" (the Settings window's Keybindings page has a search box — point the user at it), to find a free gesture before set_keybinding, and to check whether a gesture is already taken. " +
        "Scripts saved with save_script appear here as action id \"script:<id>\", so a saved script can be given a shortcut. Read-only.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Optional filter: matches the action id or its label (same as the Keybindings page's search box)." }
          },
          "additionalProperties": false
        }
        """;

    // 动作的 DisplayName 是取译文的闭包，且脚本动作的注册随菜单/目录监视器在主线程发生 → 整段在主线程读，
    // 取一致快照。
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var query = (args.Json.GetStringOrNull("query") ?? "").Trim();
        // 空表有两种截然不同的成因，回话必须分开（同"没配授权"与"用户拒绝"要分开是同一条道理）：
        // 真的一条都没注册（等一等也许就有），还是【这个进程根本没有编辑器】（等多久都不会有）。
        // 后者的判据现成：EditorState 为 null 就是"没有编辑器态"（见 IEditorStateAccess）。
        return CommandResult.Ok(await ctx.OnMainThread(() => BuildData(query, ctx.EditorState != null)));
    }

    static JsonNode BuildData(string query, bool hasEditor)
    {
        var all = Keymap.Bindings.OrderBy(c => ActionRegistry.OrderOf(c.ActionId)).ToList();
        var shown = query.Length == 0
            ? all
            : all.Where(c => c.ActionId.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || c.DisplayName().Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        var actions = new JsonArray();
        foreach (var cmd in shown)
        {
            var conflicts = KeybindingText.ConflictPeers(cmd.ActionId);

            actions.Add(new JsonObject
            {
                ["id"] = cmd.ActionId,
                ["label"] = cmd.DisplayName(),
                ["scope"] = cmd.Scope.ToString(),
                ["gesture"] = GestureNode(Keymap.Effective(cmd.ActionId)),    // null = 未绑定
                ["hasOverride"] = Keymap.HasOverride(cmd.ActionId),
                ["default"] = GestureNode(cmd.DefaultGesture),          // null = 无默认
                ["conflicts"] = conflicts,
            });
        }

        return new JsonObject
        {
            ["total"] = all.Count,                                  // 全部可绑定动作数（不受 query 影响）
            ["hasEditor"] = hasEditor,                              // false = 这个进程没有编辑器，故 total 恒为 0
            ["query"] = query.Length == 0 ? null : query,
            ["actions"] = actions,
        };
    }

    // 手势的两形都给：token 供调用方原样喂回来（set_keybinding 收的就是它），display 供对用户复述。
    static JsonNode? GestureNode(KeyBinding? binding)
        => binding is not { } b ? null : new JsonObject
        {
            ["token"] = KeyCodec.Serialize(b),      // 可能为 null：该键无法序列化成存储令牌
            ["display"] = KeyCodec.ToDisplay(b),
        };

    // 措辞与搬家前逐字一致（换行同样是 "\n"）。唯一的例外是"没有编辑器"那句：搬家前不存在这种情形
    // （工具只活在开着界面的宿主里），是命令面多出入口之后才有的新事实，故是新增而非改写。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        int total = obj["total"]!.GetValue<int>();
        if (total == 0)
            return obj["hasEditor"]?.GetValue<bool>() == false
                ? "No editor is present in this process, so there are no bindable actions: the action catalog is declared by the editor window as it builds, and shortcuts only mean anything where there are keys to press. Run this against a running TuneLab instead."
                : "No bindable actions are registered yet.";

        var query = obj["query"]?.GetValue<string>() ?? "";
        var shown = obj["actions"]!.AsArray();

        var sb = new StringBuilder();
        sb.Append(total).Append(" bindable action(s)");
        if (query.Length != 0)
            sb.Append(", ").Append(shown.Count).Append(" matching \"").Append(query).Append('"');
        sb.Append(". Change one with set_keybinding(id, gesture).");
        sb.Append("\nThe user changes these themselves in the Settings window's Keybindings page (it has a search box and a per-row reset).");
        sb.Append('\n').Append(KeybindingText.GestureSyntax);
        sb.Append("\nAreas (scopes): Global (anywhere), Editor, TrackWindow (arrangement), PianoWindow (piano roll). ")
          .Append("The SAME gesture in DIFFERENT areas is not a conflict — both stay bound and the focused area wins. Two actions in the SAME area is a conflict (only one fires).");
        sb.Append("\nFormat: <id> \"<label>\" [area]: <gesture token> (<as shown to the user>)");

        if (shown.Count == 0)
            sb.Append("\n(no action matches — try a shorter query, or call without one)");

        foreach (var node in shown)
        {
            var cmd = node!.AsObject();
            sb.Append("\n- ").Append(cmd["id"]!.GetValue<string>())
              .Append(" \"").Append(cmd["label"]!.GetValue<string>())
              .Append("\" [").Append(cmd["scope"]!.GetValue<string>()).Append("]: ");
            sb.Append(GestureText(cmd["gesture"]) ?? "(unbound)");

            var defaultText = GestureText(cmd["default"]);
            sb.Append(cmd["hasOverride"]!.GetValue<bool>()
                ? ", changed by the user (default " + (defaultText ?? "none") + ")"
                : defaultText == null ? ", no default" : ", default");

            KeybindingText.AppendConflicts(sb, cmd["conflicts"]!.AsArray(), "\n    ");
        }
        return sb.ToString();
    }

    // 复合形由 KeybindingText 拼，不在这里另写一套。
    static string? GestureText(JsonNode? gesture)
        => gesture is not JsonObject g
            ? null
            : KeybindingText.Gesture(g["token"]?.GetValue<string>(), g["display"]!.GetValue<string>());
}

// 快捷键的写那一半：绑手势 / 解绑（gesture=""）/ 恢复默认（reset=true）。改用户的应用配置 → 过入口的
// 授权策略。同域冲突默认【拒绝】，要 replaceConflict:true 才夺键（并解除原动作的绑定）——与设置页录制时
// "已被占用，是否改绑"那道确认等价，不让调用方悄悄抢走别的动作的键。
//
// 判据全在 Keymap（可绑条目、生效手势、冲突判定、落盘广播），这里不复制任何一份；手势文本口径来自
// 与 `keybinding list` 共用的 KeybindingText。
internal sealed class KeybindingSetCommand : ICommand
{
    public string Path => "keybinding set";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "set_keybinding";

    public string Brief => "Bind, unbind or reset one action's shortcut";

    public string Documentation =>
        "Bind, unbind or reset ONE action's keyboard shortcut (get the action id and a free gesture from list_keybindings first). Takes effect immediately and is saved. " +
        "Pass `gesture` to bind it, `gesture` as \"\" to unbind, or `reset` = true to restore that action's default. " +
        "If the gesture is already used by another action in the SAME area the call is refused and names that action — pick another gesture, or pass replaceConflict = true to take it over (which unbinds the other action). " +
        "This changes the user's configuration and is not part of the project's undo history, so it needs the user's authorization; if it is refused, tell the user to change it in the Settings window's Keybindings page.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "id": { "type": "string", "description": "Action id exactly as listed by list_keybindings (e.g. \"edit.undo\", \"script:my-tool\")." },
            "gesture": { "type": "string", "description": "The new gesture, e.g. \"ctrl+shift+p\" (\"mod+\" = Ctrl on Windows/Linux, Cmd on macOS). Empty string unbinds the action. Omit when using reset." },
            "reset": { "type": "boolean", "description": "True = restore this action's default shortcut (ignores gesture)." },
            "replaceConflict": { "type": "boolean", "description": "True = if another action in the same area already uses this gesture, unbind that one and take the gesture. Default false (the call is refused instead)." }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var given = (args.Json.GetString("id") ?? "").Trim();
        var gesture = args.Json.GetStringOrNull("gesture");
        bool reset = args.Json.GetBoolOrNull("reset") ?? false;
        bool replaceConflict = args.Json.GetBoolOrNull("replaceConflict") ?? false;

        // 命令表在主线程被增删（脚本命令随菜单/文件监视器同步），故 id 归一 + 计划 + 后续写都在主线程取一致快照。
        bool hasEditor = ctx.EditorState != null;
        var (id, plan) = await ctx.OnMainThread(() =>
        {
            var rid = ResolveId(given);
            return (rid, Plan(rid, gesture, reset, replaceConflict, hasEditor));
        });
        if (plan.Error is { } error)
            return CommandResult.Fail(error.Code, error.Message);
        if (plan.NoOp is { } noOp)
            return CommandResult.Ok(noOp);

        var (proceed, message) = await ctx.Authorize(
            new AuthorizationRequest(WriteKind.KeybindingChange, 0, id, plan.NewGestureText, plan.ConflictLabel), cancellationToken);
        if (!proceed)
            return CommandResult.Ok(new JsonObject
            {
                ["id"] = id,
                ["label"] = plan.Label,
                ["outcome"] = "refused",
                ["action"] = plan.Action,
                ["note"] = message,
            });

        return CommandResult.Ok(await ctx.OnMainThread(() => Apply(id, plan, message)));
    }

    // 动作 id 归一：精确匹配优先，否则忽略大小写找一条（调用方常把 id 大小写写错，没必要为此失败）；都不中则原样返回、由 Plan 报错。
    static string ResolveId(string id)
    {
        if (id.Length == 0 || Keymap.TryGet(id, out _))
            return id;
        foreach (var cmd in Keymap.Bindings)
            if (string.Equals(cmd.ActionId, id, StringComparison.OrdinalIgnoreCase))
                return cmd.ActionId;
        return id;
    }

    // 一次改动的计划（在主线程一次算出）：要写什么、准不准夺键、以及给闸门/回报的文案。
    readonly record struct ChangePlan(CommandError? Error, JsonObject? NoOp, string Label, string Action, bool Reset, KeyBinding? Binding, string NewGestureText, bool Replace, string? ConflictLabel);

    static ChangePlan Fail(string code, string message) => new(new CommandError(code, message), null, "", "", false, null, "", false, null);

    static ChangePlan Unchanged(string id, string label, string action, JsonNode? gesture) => new(null, new JsonObject
    {
        ["id"] = id,
        ["label"] = label,
        ["outcome"] = "unchanged",
        ["action"] = action,
        ["gesture"] = gesture,
    }, label, action, false, null, "", false, null);

    // hasEditor：这个进程有没有编辑器（判据见 keybinding list）。没有编辑器时可绑动作表必然是空的，
    // 于是按 id 找不到是【结构性缺席】而不是拼写错误——照常报"没有这个 id"会让调用方以为自己写错了名字
    // 而反复试。空 id 不在此列：那是调用方的笔误，有没有编辑器都一样。
    static ChangePlan Plan(string id, string? gesture, bool reset, bool replaceConflict, bool hasEditor)
    {
        if (id.Length == 0)
            return Fail("empty_id", "\"id\" is empty. Call list_keybindings to see action ids.");
        if (!Keymap.TryGet(id, out var cmd))
            return hasEditor
                ? Fail("unknown_id", string.Format(
                    "no bindable action with id \"{0}\". Call list_keybindings to see the ids. (A script saved with save_script becomes \"script:<its id>\" once the app has picked the file up — list again if it is not there yet.)", id))
                : Fail("no_editor", "No editor is present in this process, so there are no bindable actions: the action catalog is declared by the editor window as it builds, and shortcuts only mean anything where there are keys to press. Run this against a running TuneLab instead.");

        var label = cmd.DisplayName();
        var effective = Keymap.Effective(id);

        if (reset)
        {
            if (!Keymap.HasOverride(id))
                return Unchanged(id, label, "reset", GestureNode(effective));
            return new ChangePlan(null, null, label, "reset", true, cmd.DefaultGesture,
                cmd.DefaultGesture is { } d ? KeyCodec.ToDisplay(d) : "", false, null);
        }

        // gesture 缺省或空串 = 解绑（与设置页的"解绑"等价；显式 override 成 null，不回落默认）。
        if (string.IsNullOrWhiteSpace(gesture))
        {
            if (effective == null)
                return Unchanged(id, label, "unbind", null);
            return new ChangePlan(null, null, label, "unbind", false, null, "", false, null);
        }

        // 声明式解析：额外接受 "mod+"/"primary+" 别名（解析成本平台的主命令键），落盘仍是物理修饰。
        if (!KeyCodec.TryParseDeclaration(gesture, out var binding))
            return Fail("invalid_gesture", string.Format("\"{0}\" is not a valid gesture. {1}", gesture, KeybindingText.GestureSyntax));
        if (!KeyCodec.IsSupported(binding.Key))
            return Fail("unsupported_key", string.Format("that key cannot be bound. {0}", KeybindingText.GestureSyntax));

        if (effective is { } cur && cur.Equals(binding))
            return Unchanged(id, label, "bind", GestureNode(binding));

        var conflictId = Keymap.FindConflict(id, binding);
        if (conflictId != null && !replaceConflict)
            return Fail("conflict", string.Format(
                "{0} is already used by \"{1}\" (id {2}) in the same area ({3}), so nothing changed. Pick a free gesture (list_keybindings shows what is taken), or call again with replaceConflict = true to take it over — that unbinds \"{1}\".",
                KeybindingText.Gesture(binding), KeybindingText.LabelOf(conflictId), conflictId, cmd.Scope));

        return new ChangePlan(null, null, label, "bind", false, binding, KeyCodec.ToDisplay(binding), replaceConflict,
            conflictId == null ? null : KeybindingText.LabelOf(conflictId));
    }

    // 落地（主线程）：Keymap.Rebind/ResetToDefault 自带落盘 + Changed 广播（菜单与设置页即时刷新）。
    static JsonNode Apply(string id, ChangePlan plan, string note)
    {
        var data = new JsonObject
        {
            ["id"] = id,
            ["label"] = KeybindingText.LabelOf(id),
            ["outcome"] = "applied",
            ["action"] = plan.Action,
            ["note"] = string.IsNullOrEmpty(note) ? null : note,
        };

        if (plan.Reset)
        {
            Keymap.ResetToDefault(id);
            data["gesture"] = GestureNode(Keymap.Effective(id));
        }
        else if (plan.Binding is not { } binding)
        {
            Keymap.Rebind(id, null);
        }
        else
        {
            // 冲突按【落地这一刻】重查：闸门在等用户裁决期间，用户可能已在设置页自己改了绑定。
            // 计划期无冲突而此刻有 → 未获夺键许可，宁可什么都不做（绝不悄悄抢走别的动作的键）。
            var conflictId = Keymap.FindConflict(id, binding);
            if (conflictId != null && !plan.Replace)
            {
                data["outcome"] = "taken_meanwhile";
                data["gesture"] = GestureNode(binding);
                data["conflict"] = new JsonObject { ["id"] = conflictId, ["label"] = KeybindingText.LabelOf(conflictId) };
                return data;
            }
            if (conflictId != null)
                Keymap.Rebind(conflictId, null);   // 夺键：先解除原动作的绑定（与设置页确认后的行为一致）
            Keymap.Rebind(id, binding);
            data["gesture"] = GestureNode(binding);
            data["replaced"] = conflictId == null ? null
                : new JsonObject { ["id"] = conflictId, ["label"] = KeybindingText.LabelOf(conflictId) };
            // 跨域同手势不是冲突（聚焦哪层哪层生效），但如实告知，免得用户以为某个"失灵"。
            data["otherScopeUsers"] = new JsonArray(KeybindingText.OtherScopeUsers(id, binding)
                .Select(c => (JsonNode?)c.DisplayName()).ToArray());
        }

        // 改完之后这条动作还剩什么同域冲突（夺键后通常为空，但同域可能不止两个）。
        data["conflicts"] = KeybindingText.ConflictPeers(id);
        return data;
    }

    // 手势的结构化形：token（可序列化写法）与 display（给人看的字形）两个字段，复合形渲染时再拼。
    static JsonNode? GestureNode(KeyBinding? binding) => binding is not { } b ? null : new JsonObject
    {
        ["token"] = KeyCodec.Serialize(b),
        ["display"] = KeyCodec.ToDisplay(b),
    };

    static string? GestureText(JsonNode? gesture)
        => gesture is not JsonObject g ? null
            : KeybindingText.Gesture(g["token"]?.GetValue<string>(), g["display"]!.GetValue<string>());

    // 措辞与搬家前逐字一致。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var label = obj["label"]!.GetValue<string>();
        var action = obj["action"]!.GetValue<string>();
        var gesture = GestureText(obj["gesture"]);

        switch (obj["outcome"]!.GetValue<string>())
        {
            case "unchanged":
                return action switch
                {
                    "reset" => string.Format("\"{0}\" is already at its default shortcut ({1}). Nothing changed.", label, gesture ?? "none"),
                    "unbind" => string.Format("\"{0}\" has no shortcut already. Nothing changed.", label),
                    _ => string.Format("\"{0}\" is already bound to {1}. Nothing changed.", label, gesture),
                };

            case "refused":
                return obj["note"]!.GetValue<string>();   // 闸门给的原话

            case "taken_meanwhile":
            {
                var conflict = obj["conflict"]!.AsObject();
                return string.Format("Nothing changed: {0} got taken by \"{1}\" (id {2}) while waiting for the user. Pick another gesture, or call again with replaceConflict = true.",
                    gesture, conflict["label"]!.GetValue<string>(), conflict["id"]!.GetValue<string>());
            }
        }

        var sb = new StringBuilder(obj["note"]?.GetValue<string>() ?? string.Empty);
        if (action == "reset")
            sb.Append(string.Format("Reset \"{0}\" to its default shortcut: {1}.", label, gesture ?? "no shortcut"));
        else if (action == "unbind")
            sb.Append(string.Format("Removed the shortcut for \"{0}\".", label));
        else
        {
            sb.Append(string.Format("Bound \"{0}\" to {1}.", label, gesture));
            if (obj["replaced"] is JsonObject replaced)
                sb.Append(string.Format(" \"{0}\" (id {1}) lost that shortcut and is now unbound — tell the user.",
                    replaced["label"]!.GetValue<string>(), replaced["id"]!.GetValue<string>()));
            var others = obj["otherScopeUsers"]!.AsArray();
            if (others.Count > 0)
                sb.Append(string.Format(" Note: {0} also use(s) this gesture in another area — both stay active, the focused area wins.",
                    string.Join(", ", others.Select(c => "\"" + c!.GetValue<string>() + "\""))));
        }
        sb.Append(" Saved; it works right away (no restart).");
        KeybindingText.AppendConflicts(sb, obj["conflicts"]!.AsArray(), " ");
        return sb.ToString();
    }
}
