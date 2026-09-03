using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.GUI.Input;
using TuneLab.Input;

namespace TuneLab.Commands.Handlers;

// 快捷键能力的读那一半。直接读宿主的 Keymap——命令表、生效手势、冲突判定都在那里，这里不复制任何一份
// 判据；手势的文本口径来自共享的 KeybindingText。
//
// 同时给出手势语法与"用户自己在哪改"，好让调用方能教用户动手而不只是代劳。
internal sealed class KeybindingListCommand : ICommand
{
    public string Path => "keybinding list";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "list_keybindings";

    public string Brief => "List bindable commands with their effective shortcuts";

    public string Documentation =>
        "List TuneLab's bindable commands and their keyboard shortcuts: command id, label, area (scope), the effective gesture, whether it is the default or the user's own override, and any conflict. " +
        "Use it to answer \"what is the shortcut for X\" / \"how do I rebind X\" (the Settings window's Keybindings page has a search box — point the user at it), to find a free gesture before set_keybinding, and to check whether a gesture is already taken. " +
        "Scripts saved with save_script appear here as command id \"script:<id>\", so a saved script can be given a shortcut. Read-only.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Optional filter: matches the command id or its label (same as the Keybindings page's search box)." }
          },
          "additionalProperties": false
        }
        """;

    // 命令的 DisplayName 是取译文的闭包，且脚本命令的注册随菜单/目录监视器在主线程发生 → 整段在主线程读，
    // 取一致快照。
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var query = (args.Json.GetStringOrNull("query") ?? "").Trim();
        return CommandResult.Ok(await ctx.OnMainThread(() => BuildData(query)));
    }

    static JsonNode BuildData(string query)
    {
        var all = Keymap.Commands.OrderBy(c => Keymap.OrderOf(c.Id)).ToList();
        var shown = query.Length == 0
            ? all
            : all.Where(c => c.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || c.DisplayName().Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        var commands = new JsonArray();
        foreach (var cmd in shown)
        {
            var conflicts = new JsonArray();
            foreach (var peer in Keymap.SameScopeConflictPeers(cmd.Id))
                conflicts.Add(new JsonObject { ["id"] = peer, ["label"] = KeybindingText.LabelOf(peer) });

            commands.Add(new JsonObject
            {
                ["id"] = cmd.Id,
                ["label"] = cmd.DisplayName(),
                ["scope"] = cmd.Scope.ToString(),
                ["gesture"] = GestureNode(Keymap.Effective(cmd.Id)),    // null = 未绑定
                ["hasOverride"] = Keymap.HasOverride(cmd.Id),
                ["default"] = GestureNode(cmd.DefaultGesture),          // null = 无默认
                ["conflicts"] = conflicts,
            });
        }

        return new JsonObject
        {
            ["total"] = all.Count,                                  // 全部可绑定命令数（不受 query 影响）
            ["query"] = query.Length == 0 ? null : query,
            ["commands"] = commands,
        };
    }

    // 手势的两形都给：token 供调用方原样喂回来（set_keybinding 收的就是它），display 供对用户复述。
    static JsonNode? GestureNode(KeyBinding? binding)
        => binding is not { } b ? null : new JsonObject
        {
            ["token"] = KeyCodec.Serialize(b),      // 可能为 null：该键无法序列化成存储令牌
            ["display"] = KeyCodec.ToDisplay(b),
        };

    // 措辞与搬家前逐字一致（换行同样是 "\n"）。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        int total = obj["total"]!.GetValue<int>();
        if (total == 0)
            return "No bindable commands are registered yet.";

        var query = obj["query"]?.GetValue<string>() ?? "";
        var shown = obj["commands"]!.AsArray();

        var sb = new StringBuilder();
        sb.Append(total).Append(" bindable command(s)");
        if (query.Length != 0)
            sb.Append(", ").Append(shown.Count).Append(" matching \"").Append(query).Append('"');
        sb.Append(". Change one with set_keybinding(id, gesture).");
        sb.Append("\nThe user changes these themselves in the Settings window's Keybindings page (it has a search box and a per-row reset).");
        sb.Append('\n').Append(KeybindingText.GestureSyntax);
        sb.Append("\nAreas (scopes): Global (anywhere), Editor, TrackWindow (arrangement), PianoWindow (piano roll). ")
          .Append("The SAME gesture in DIFFERENT areas is not a conflict — both stay bound and the focused area wins. Two commands in the SAME area is a conflict (only one fires).");
        sb.Append("\nFormat: <id> \"<label>\" [area]: <gesture token> (<as shown to the user>)");

        if (shown.Count == 0)
            sb.Append("\n(no command matches — try a shorter query, or call without one)");

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

            var conflicts = cmd["conflicts"]!.AsArray();
            if (conflicts.Count > 0)
                sb.Append("\n    ").Append("CONFLICT: the same area also binds this gesture to ")
                  .Append(string.Join(", ", conflicts.Select(c =>
                      "\"" + c!["label"]!.GetValue<string>() + "\" (" + c["id"]!.GetValue<string>() + ")")))
                  .Append(" — only one of them fires (the built-in / earliest registered wins). Rebind one of them to fix it.");
        }
        return sb.ToString();
    }

    // 复合形由 KeybindingText 拼，不在这里另写一套。
    static string? GestureText(JsonNode? gesture)
        => gesture is not JsonObject g
            ? null
            : KeybindingText.Gesture(g["token"]?.GetValue<string>(), g["display"]!.GetValue<string>());
}
