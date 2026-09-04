using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Configs;
using TuneLab.Foundation;
using TuneLab.I18N;

namespace TuneLab.Commands.Handlers;

// 设置助手的读那一半（诉求 2「想调设置 → 告诉我在哪调/怎么调」）。直接读 SettingsRegistry——
// 设置的声明（键/标签/所在页/控件 config/默认/重启标记/描述）在那里是单一真源，故这里不重复任何一份表；
// 取值/校验的措辞判据一律来自共享的 SettingsText。
//
// 同时给出"在设置窗哪一页的哪一行"，好让调用方能【教用户自己改】而不只是代劳。
internal sealed class SettingListCommand : ICommand
{
    public string Path => "setting list";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "list_settings";

    public string Brief => "List app settings with page, allowed values, current and default";

    public string Documentation =>
        "List TuneLab's application settings (the Settings window): each one's key, label, which page it lives on, allowed type/range/options, current value and default. " +
        "Use it to answer \"where/how do I change X\" — tell the user the page and the row label so they can do it themselves — and always before set_setting, to get the exact key and allowed values. " +
        "Read-only. Note these are app-wide preferences, NOT project data (project/track/part settings go through run_script) and NOT plugin parameters (see list_sound_sources / list_effects).";

    public string ParametersJsonSchema => """
        { "type": "object", "properties": {}, "additionalProperties": false }
        """;

    // 选项取现值会跑引擎/字体枚举（AudioEngine / FontManager），故整段在宿主主线程上取。
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
        => CommandResult.Ok(await ctx.OnMainThread(BuildData));

    static JsonNode BuildData()
    {
        var pages = new JsonArray();
        foreach (var tab in Enum.GetValues<SettingTab>())
            pages.Add(new JsonObject { ["name"] = tab.ToString(), ["label"] = SettingsText.PageLabelText(tab) });

        var settings = new JsonArray();
        foreach (var item in SettingsRegistry.All)
        {
            settings.Add(new JsonObject
            {
                ["key"] = item.Key,
                ["label"] = item.Label,                  // 英文翻译键 = 设置窗行标的原文
                ["displayLabel"] = item.DisplayLabel,    // 本地化后的行标（与 label 相同则渲染时不重复给出）
                ["page"] = item.Tab?.ToString(),         // null = 仅存储、不在设置窗渲染
                ["pageLabel"] = item.Tab is { } t ? SettingsText.PageLabelText(t) : null,
                ["allowed"] = SettingsText.Allowed(item),
                ["current"] = ConfigText.ToJson(item.GetValue()),
                ["default"] = ConfigText.ToJson(item.GetDefaultValue()),
                ["restartRequired"] = item.RestartRequired,
                ["agentWritable"] = item.AgentWritable,
                ["description"] = string.IsNullOrEmpty(item.Description) ? null : item.Description,
            });
        }

        return new JsonObject { ["pages"] = pages, ["settings"] = settings };
    }

    // 措辞与搬家前逐字一致。注意换行用 "\n" 而非 AppendLine——搬家前就是这样（与 project status 不同）。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var settings = obj["settings"]!.AsArray();
        var sb = new StringBuilder();
        sb.Append(settings.Count).Append(" application setting(s). Change one with set_setting(key, value).");
        sb.Append("\nSettings window pages: ").Append(string.Join(", ",
            obj["pages"]!.AsArray().Select(p => Qualified(p!["name"]!.GetValue<string>(), p["label"]?.GetValue<string>()))));
        sb.Append("\nFormat: <key> \"<label>\" [page]: <allowed> — current <value>, default <value>");
        sb.Append("\n(\"current\" is the value in the user's settings file; an empty text value means \"use the default\".)");

        foreach (var setting in settings)
        {
            var item = setting!.AsObject();
            sb.Append("\n- ").Append(item["key"]!.GetValue<string>()).Append(' ').Append(LabelText(item));
            var page = item["page"]?.GetValue<string>();
            sb.Append(" [").Append(page == null
                ? "not in the Settings window"
                : Qualified(page, item["pageLabel"]?.GetValue<string>())).Append("]: ");
            sb.Append(item["allowed"]!.GetValue<string>());
            sb.Append(" — current ").Append(ConfigText.FormatValue(item["current"]));
            sb.Append(", default ").Append(ConfigText.FormatValue(item["default"]));
            if (item["restartRequired"]!.GetValue<bool>())
                sb.Append(". Needs a restart to take effect");
            if (!item["agentWritable"]!.GetValue<bool>())
                sb.Append(". NOT agent-writable — only the user can change it");
            sb.Append('.');
            var description = item["description"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(description))
                sb.Append("\n  note: ").Append(description);
        }
        return sb.ToString();
    }

    // "<英文标签>" 或 "<英文标签> ("<本地化标签>")"（本地化不同时才给出，便于用用户的语言指路）。
    static string LabelText(JsonObject item)
    {
        var label = item["label"]!.GetValue<string>();
        var display = item["displayLabel"]?.GetValue<string>();
        return display == label ? "\"" + label + "\"" : "\"" + label + "\" (\"" + display + "\")";
    }

    // 页名同理：本地化与原名一致时不重复。
    static string Qualified(string name, string? localized)
        => localized == null || localized == name ? name : name + " (\"" + localized + "\")";
}

// 设置助手的写那一半：改一项设置 + 落盘。写用户的应用配置（非工程数据、历史记录管理器救不回）
// → 恒过入口的授权策略（见 §5.1）。值校验一律按条目声明的 config，判据与 `setting list` 共用 SettingsText。
//
// **这是第一条 edit 命令**，两处形状对后续九条都成立：
//  · 【授权的三种结局都走成功路径】"用户拒绝"/"只读档"/"这个入口没配授权"都不是命令的故障，而是它
//    如实汇报的结果（applied=false + 原文）。套成 CommandError 会给 agent 侧的回报凭空加一个
//    "Error: " 前缀——搬家前没有，而且会让模型把"用户不让"读成"我调错了"。
//  · 【Data 只回"改了什么"】{key, old, new, outcome}，够 CI 断言；人类文本沿用原措辞。
internal sealed class SettingSetCommand : ICommand
{
    public string Path => "setting set";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "set_setting";

    public string Brief => "Change one application setting and save it";

    public string Documentation =>
        "Change ONE of TuneLab's application settings by key (get keys and allowed values from list_settings first) and save it to the user's settings file. " +
        "The value is validated against that setting's declared type/range/options, so an out-of-range or unknown value changes nothing and reports the allowed values. " +
        "This edits the user's app configuration and is NOT part of the project's undo history, so it needs the user's authorization: depending on their authorization level it may be applied, asked about, or refused — " +
        "if it is refused, tell the user which Settings page and row to change themselves (list_settings gives both). A few settings are not agent-writable (e.g. the agent's own authorization level).";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "key": { "type": "string", "description": "The setting's key exactly as listed by list_settings (e.g. \"MasterGain\")." },
            "value": { "type": ["string", "number", "boolean"], "description": "The new value, matching the setting's declared type/range/options. Numbers may be given as numbers or numeric strings." }
          },
          "required": ["key", "value"],
          "additionalProperties": false
        }
        """;

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var key = (args.Json.GetString("key") ?? "").Trim();
        var raw = args.Json.Require("value").Clone();

        var item = SettingsRegistry.All.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            return CommandResult.Fail("unknown_key", string.Format("no setting with key \"{0}\". Call list_settings to see the exact keys.", key));
        if (!item.AgentWritable)
            return CommandResult.Fail("not_writable", string.Format(
                "the setting \"{0}\" cannot be changed by the agent — only the user can. {1}Tell the user where to change it themselves.",
                item.Key, string.IsNullOrEmpty(item.Description) ? "" : item.Description + " "));

        // 校验按条目声明的 config 做（选项枚举可能跑引擎/字体枚举 → 主线程）。
        var (value, error) = await ctx.OnMainThread(() => SettingsText.Normalize(item, raw));
        if (error != null)
            return CommandResult.Fail("invalid_value", error);

        var old = item.GetValue();
        if (value.Equals(old))
            return CommandResult.Ok(Result(item, "unchanged", old, value, null));

        // 改用户的应用配置 → 过闸门（Auto 直接改 / Confirm 卡片裁决 / 只读档不改+建议）。无预览-回退。
        var (proceed, message) = await ctx.Authorize(new AuthorizationRequest(WriteKind.SettingChange, 0, item.Key, ConfigText.FormatValue(value)), cancellationToken);
        if (!proceed)
            return CommandResult.Ok(Result(item, "refused", old, value, message));

        bool ok = await ctx.OnMainThread(() =>
        {
            if (!item.TrySetValue(value))
                return false;
            Settings.Save(PathManager.SettingsFilePath);
            return true;
        });
        if (!ok)
            return CommandResult.Fail("rejected", string.Format(
                "the setting \"{0}\" rejected the value {1}. Nothing changed.", item.Key, ConfigText.FormatValue(value)));

        return CommandResult.Ok(Result(item, "applied", old, item.GetValue(), message));
    }

    // note 一栏在 applied 时是授权前缀（"用户被问过并批准了"——那件事本身要说出来，否则 Confirm 档的
    // 回报与 Auto 档一字不差），在 refused 时是不做的原因原文。
    static JsonNode Result(SettingItem item, string outcome, PropertyValue old, PropertyValue value, string? note) => new JsonObject
    {
        ["key"] = item.Key,
        ["outcome"] = outcome,
        ["old"] = ConfigText.ToJson(old),
        ["new"] = ConfigText.ToJson(value),
        ["restartRequired"] = item.RestartRequired,
        ["note"] = string.IsNullOrEmpty(note) ? null : note,
    };

    // 措辞与搬家前逐字一致。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var key = obj["key"]!.GetValue<string>();
        var note = obj["note"]?.GetValue<string>() ?? string.Empty;
        switch (obj["outcome"]!.GetValue<string>())
        {
            case "unchanged":
                return string.Format("The setting \"{0}\" is already {1}. Nothing changed.", key, ConfigText.FormatValue(obj["new"]));
            case "refused":
                return note;   // 闸门给的原话（"只读档"/"用户拒绝"/"这个入口没配授权"）
            default:
                var sb = new StringBuilder(note);
                sb.Append(string.Format("Changed \"{0}\" from {1} to {2} and saved the settings file.",
                    key, ConfigText.FormatValue(obj["old"]), ConfigText.FormatValue(obj["new"])));
                if (obj["restartRequired"]!.GetValue<bool>())
                    sb.Append(" It only takes full effect after the user restarts TuneLab — tell them so.");
                return sb.ToString();
        }
    }
}
