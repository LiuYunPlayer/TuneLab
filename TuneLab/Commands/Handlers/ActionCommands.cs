using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.GUI.Input;
using TuneLab.Input;

namespace TuneLab.Commands.Handlers;

// 动作面的读那一半：**用户在编辑器里够得着的事，外部也够得着**——枚举 ActionRegistry（穷尽面）。
//
// 与 `keybinding list` 的分工：那条命令管【手势】（哪条动作绑了什么键、冲突、怎么重绑），这条管【动作】
// （有哪些、现在能不能跑、跑了会怎样）。两者的 id 是同一套，故一条动作在两边指的是同一件事。
internal sealed class ActionListCommand : ICommand
{
    public string Path => "action list";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "list_actions";

    public string Brief => "List the editor actions that can be triggered";

    public string Documentation =>
        "List the actions of TuneLab's editor — the things a person can do in the window: transport (play/pause, go to start/end), tool selection, view toggles, the clipboard verbs, undo/redo, file actions. "
        + "Each entry gives: id (stable, pass it to run_action), label, kind, whether it can run right now and why not, whether triggering it stops on a dialog someone has to answer, and its keyboard shortcut if it has one. "
        + "\nKinds: appState = changes only the app's/editor's own state, not the project (no authorization needed); projectEdit = edits project data and goes into the undo history; destructive = can discard unsaved work or write files to disk. "
        + "\nThis surface is for application and editor state. To READ or WRITE project data (notes, parameters, phonemes, tracks) use run_script (tl.*) — that is the write channel for the document itself, and it does not depend on where the keyboard focus is. "
        + "\nAsk get_editor_status what the state is right now (playing? which tool? which part?), and list_keybindings about the shortcuts. Read-only.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Optional filter: matches the action id or its label (e.g. \"transport\", \"tool\", \"undo\")." },
            "runnableOnly": { "type": "boolean", "description": "True = list only the actions that can run right now (leaves out the ones with a reason they cannot)." }
          },
          "additionalProperties": false
        }
        """;

    // 动作的 DisplayName 是取译文的闭包，可用性判据读的是实时界面状态，脚本动作的注册随目录监视器在主线程
    // 发生 → 整段在主线程取一致快照。
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var query = (args.Json.GetStringOrNull("query") ?? "").Trim();
        bool runnableOnly = args.Json.GetBoolOrNull("runnableOnly") ?? false;
        return CommandResult.Ok(await ctx.OnMainThread(() => Build(query, runnableOnly, ctx.EditorStatus != null)));
    }

    // hasEditor 的判据：这个进程有没有编辑器（EditorStatus 为 null 就是没有）。没有时表必然是空的，
    // 而"空"有两种截然不同的成因，回话必须分开——真的一条都没注册（等一等也许就有），还是这里永远不会有。
    static JsonNode Build(string query, bool runnableOnly, bool hasEditor)
    {
        var all = ActionRegistry.Actions.OrderBy(a => ActionRegistry.OrderOf(a.Id)).ToList();

        var actions = new JsonArray();
        int runnable = 0;
        foreach (var action in all)
        {
            var unavailable = action.Unavailable?.Invoke();
            if (unavailable == null && action.RunElsewhere == null)
                runnable++;

            if (query.Length != 0
                && !action.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !action.DisplayName().Contains(query, StringComparison.CurrentCultureIgnoreCase))
                continue;
            if (runnableOnly && unavailable != null)
                continue;

            bool bindable = Keymap.TryGet(action.Id, out var binding);
            actions.Add(new JsonObject
            {
                ["id"] = action.Id,
                ["label"] = action.DisplayName(),
                ["kind"] = KindText(action.Kind),
                ["unavailable"] = unavailable,                       // null = 现在跑得动
                ["prompts"] = action.Prompts?.Invoke() ?? false,     // true = 会弹需要人应答的对话框
                ["runElsewhere"] = action.RunElsewhere,              // null = 就从这里跑
                // 手势那一半来自 Keymap：可绑的动作才有（bindable=false 的动作二期会有——进注册表但不值得
                // 占一个手势），改绑走 set_keybinding。
                ["bindable"] = bindable,
                ["scope"] = bindable ? binding.Scope.ToString() : null,
                ["shortcut"] = !bindable || Keymap.Effective(action.Id) is not { } g ? null : new JsonObject
                {
                    ["token"] = KeyCodec.Serialize(g),
                    ["display"] = KeyCodec.ToDisplay(g),
                },
            });
        }

        return new JsonObject
        {
            ["total"] = all.Count,          // 全部动作数（不受 query / runnableOnly 影响）
            ["runnable"] = runnable,        // 其中此刻跑得动的
            ["hasEditor"] = hasEditor,      // false = 这个进程没有编辑器，故 total 恒为 0
            ["query"] = query.Length == 0 ? null : query,
            ["actions"] = actions,
        };
    }

    // 结构化结果里的 kind 用 camelCase 字面量（不是枚举名）：它是给外部消费的契约，与内部枚举怎么改名无关。
    public static string KindText(ActionKind kind) => kind switch
    {
        ActionKind.AppState => "appState",
        ActionKind.ProjectEdit => "projectEdit",
        _ => "destructive",
    };

    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        int total = obj["total"]!.GetValue<int>();
        if (total == 0)
            return obj["hasEditor"]?.GetValue<bool>() == false
                ? EditorStatusText.NoEditor
                : "No actions are registered yet.";

        var query = obj["query"]?.GetValue<string>() ?? "";
        var shown = obj["actions"]!.AsArray();

        var sb = new StringBuilder();
        sb.Append(total).Append(" action(s), ").Append(obj["runnable"]!.GetValue<int>()).Append(" of them runnable right now");
        if (query.Length != 0)
            sb.Append("; ").Append(shown.Count).Append(" matching \"").Append(query).Append('"');
        sb.Append(". Trigger one with run_action(id).");
        sb.Append("\nKinds: appState = only the app's/editor's own state (no authorization needed); projectEdit = edits the project, undo can take it back; destructive = can discard unsaved work or write files.");
        sb.Append("\nProject data (notes, parameters, phonemes, tracks) is edited with run_script (tl.*), not from here.");
        sb.Append("\nFormat: <id> \"<label>\" [kind] <shortcut> — anything you must know before running it");

        if (shown.Count == 0)
            sb.Append("\n(nothing matches — try a shorter query, or call without one)");

        foreach (var node in shown)
        {
            var action = node!.AsObject();
            sb.Append("\n- ").Append(action["id"]!.GetValue<string>())
              .Append(" \"").Append(action["label"]!.GetValue<string>())
              .Append("\" [").Append(action["kind"]!.GetValue<string>()).Append(']');

            if (action["shortcut"] is JsonObject shortcut)
                sb.Append(' ').Append(KeybindingText.Gesture(shortcut["token"]?.GetValue<string>(), shortcut["display"]!.GetValue<string>()));

            if (action["runElsewhere"]?.GetValue<string>() is { } elsewhere)
                sb.Append(" — NOT FROM HERE: ").Append(elsewhere);
            else if (action["unavailable"]?.GetValue<string>() is { } reason)
                sb.Append(" — CANNOT RUN NOW: ").Append(reason);
            else if (action["prompts"]!.GetValue<bool>())
                sb.Append(" — stops on a dialog someone has to answer; run_action refuses it unless you pass allowPrompt = true");
        }
        return sb.ToString();
    }
}

// 动作面的写那一半：按 id 触发一条动作。
//
// 【为什么是一条命令而不是每个动作一条】动作面要穷尽（二期会有几百条），逐动作开一个工具会立刻把工具面
// 撑爆——与脚本层收口工具爆炸是同一个道理。发现走 `action list`。
//
// 四道闸，缺一条就会变成"回报成功、实际什么都没发生"：
//  ① 没有编辑器 → 如实说这里永远做不到（不是"暂时"）；
//  ② 这条动作不该从这里跑（脚本工具）→ 指路 `script run-saved`，不开第二套写通道；
//  ③ 此刻跑不动（撤销栈空、没选中、两个编辑面都没焦点…）→ 回那条判据给出的原因，不去空跑一趟；
//  ④ 会弹需要人应答的模态 → 默认拒绝，要 allowPrompt 显式说"有人在场"。
// 过了闸再按后果分档过授权（AppState 不过闸门、ProjectEdit / Destructive 各一档）。
internal sealed class ActionRunCommand : ICommand
{
    public string Path => "action run";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "run_action";

    public string Brief => "Trigger one editor action by id";

    public string Documentation =>
        "Trigger ONE of TuneLab's editor actions by id (get ids from list_actions). This is how you drive the application and the editor itself: play/pause, jump the playhead, pick a tool, toggle a panel, undo/redo, the clipboard verbs. "
        + "\nThe reply says what the editor's state is afterwards, so a fire-and-forget action (play/pause toggles; picking a tool) does not leave you guessing — same fields as get_editor_status. "
        + "\nIt refuses instead of pretending in four cases, each with the reason: there is no editor in this process; the action is not meant to be run from here (script tools: use run_saved_script, which takes parameters); it cannot run right now (nothing to undo, nothing selected, neither edit surface has keyboard focus — that last one is common, because TuneLab is usually not the foreground window while you work); or it would stop on a dialog a person has to answer — pass allowPrompt = true only if the user is at the machine, because otherwise the action never finishes and that dialog just sits on their screen. "
        + "\nActions of kind projectEdit or destructive need the user's authorization; an appState action does not. To edit project data prefer run_script (tl.*): it works regardless of keyboard focus and reports exactly what it changed.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "id": { "type": "string", "description": "Action id exactly as listed by list_actions (e.g. \"transport.play\", \"tool.pitch\", \"edit.undo\")." },
            "allowPrompt": { "type": "boolean", "description": "True = allow an action that opens a dialog someone must answer (file picker, unsaved-changes confirmation). Only pass it when the user is at the machine; the default refuses those." }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var given = (args.Json.GetString("id") ?? "").Trim();
        bool allowPrompt = args.Json.GetBoolOrNull("allowPrompt") ?? false;

        // 注册表在主线程被增删（脚本动作随目录监视器同步），可用性判据读的是实时界面状态 → id 归一 + 计划
        // 在主线程一次算出。
        bool hasEditor = ctx.EditorStatus != null;
        var (id, plan) = await ctx.OnMainThread(() =>
        {
            var rid = ResolveId(given);
            return (rid, Plan(rid, allowPrompt, hasEditor));
        });
        if (plan.Error is { } error)
            return CommandResult.Fail(error.Code, error.Message);

        var note = string.Empty;
        if (plan.Gate is { } gate)
        {
            var (proceed, message) = await ctx.Authorize(new AuthorizationRequest(gate, 0, plan.Label), cancellationToken);
            if (!proceed)
                return CommandResult.Ok(await ctx.OnMainThread(() => Result(id, plan, "refused", message, ctx)));
            note = message;
        }

        return CommandResult.Ok(await ctx.OnMainThread(() =>
        {
            // 可用性按【触发这一刻】重查：闸门在等用户裁决期间，用户可能已经自己动过界面（撤销栈被清空、
            // 焦点离开了编辑面）。ActionRegistry.Execute 用的就是同一份判据，故它给出原因就等于没跑。
            var reason = ActionRegistry.Execute(id);
            return Result(id, plan, reason == null ? "ran" : "unavailable_meanwhile", reason ?? note, ctx);
        }));
    }

    // id 归一：精确匹配优先，否则忽略大小写找一条（调用方常把 id 大小写写错，没必要为此失败）；
    // 都不中则原样返回、由 Plan 报错。与 `keybinding set` 同一口径。
    static string ResolveId(string id)
    {
        if (id.Length == 0 || ActionRegistry.TryGet(id, out _))
            return id;
        foreach (var action in ActionRegistry.Actions)
            if (string.Equals(action.Id, id, StringComparison.OrdinalIgnoreCase))
                return action.Id;
        return id;
    }

    // 一次触发的计划（主线程一次算出）：要不要过闸门、以及给回报的文案。
    readonly record struct RunPlan(CommandError? Error, string Label, string Kind, bool Prompts, WriteKind? Gate);

    static RunPlan Fail(string code, string message) => new(new CommandError(code, message), "", "", false, null);

    static RunPlan Plan(string id, bool allowPrompt, bool hasEditor)
    {
        if (id.Length == 0)
            return Fail("empty_id", "\"id\" is empty. Call list_actions to see the action ids.");

        // 没有编辑器时注册表必然是空的，于是"按 id 找不到"是【结构性缺席】而不是拼写错误——照常报
        // "没有这个 id"会让调用方以为自己写错了名字而反复试。空 id 不在此列：那是笔误，有没有编辑器都一样。
        if (!ActionRegistry.TryGet(id, out var action))
            return hasEditor
                ? Fail("unknown_id", string.Format("no action with id \"{0}\". Call list_actions to see the ids.", id))
                : Fail("no_editor", EditorStatusText.NoEditor);

        var label = action.DisplayName();
        var kind = ActionListCommand.KindText(action.Kind);

        if (action.RunElsewhere is { } elsewhere)
            return Fail("run_elsewhere", string.Format("\"{0}\" is not triggered from here: {1}.", label, elsewhere));

        if (action.Unavailable?.Invoke() is { } reason)
            return Fail("unavailable", string.Format("\"{0}\" cannot run right now: {1}. Nothing happened.", label, reason));

        bool prompts = action.Prompts?.Invoke() ?? false;
        if (prompts && !allowPrompt)
            return Fail("needs_user_present", string.Format(
                "\"{0}\" stops on a dialog that a person has to answer (a file picker, or a confirmation about unsaved work). With nobody there it never finishes, and the dialog sits on the user's screen until they deal with it. Nothing happened. "
                + "Ask the user to do it in TuneLab, or call again with allowPrompt = true if you know they are at the machine.", label));

        return new RunPlan(null, label, kind, prompts, action.Kind switch
        {
            // 只改应用自身状态的动作不过闸门：用户一眼看得见、自己就能改回来，而每次播放都弹一张确认卡片
            // 会让这个动作面根本没法用（同 Read 命令不过闸门的道理）。
            ActionKind.AppState => null,
            ActionKind.ProjectEdit => WriteKind.EditorAction,
            _ => WriteKind.EditorActionDestructive,
        });
    }

    // 回报：动作的身份 + 结局 + **执行后的编辑器状态**（即发即忘的动作靠它才不瞎，见 IEditorStatusAccess）。
    static JsonNode Result(string id, RunPlan plan, string outcome, string note, CommandContext ctx) => new JsonObject
    {
        ["id"] = id,
        ["label"] = plan.Label,
        ["kind"] = plan.Kind,
        ["outcome"] = outcome,
        ["prompted"] = outcome == "ran" && plan.Prompts,
        ["note"] = string.IsNullOrEmpty(note) ? null : note,
        ["status"] = EditorStatusText.Build(ctx),
    };

    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var label = obj["label"]!.GetValue<string>();
        var outcome = obj["outcome"]!.GetValue<string>();
        var note = obj["note"]?.GetValue<string>() ?? string.Empty;

        if (outcome == "refused")
            return note;   // 闸门给的原话
        if (outcome == "unavailable_meanwhile")
            return string.Format("Nothing happened: \"{0}\" stopped being runnable while waiting for the user — {1}.", label, note);

        var sb = new StringBuilder(note);
        sb.Append(string.Format("Ran \"{0}\" ({1}).", label, obj["id"]!.GetValue<string>()));
        if (obj["prompted"]!.GetValue<bool>())
            sb.Append(" It is now waiting on a dialog in TuneLab: this action does not finish until someone answers it, and until then that dialog sits on the user's screen.");
        if (obj["kind"]!.GetValue<string>() == "projectEdit")
            sb.Append(" This action is part of the project's undo history — the user can step back and forth with undo/redo.");

        // 触发之后的状态紧跟在同一段回报里：调用方不必再单独问一次 get_editor_status。
        if (obj["status"] is JsonObject status)
        {
            sb.Append('\n');
            EditorStatusText.Append(sb, status);
        }
        return sb.ToString();
    }
}
