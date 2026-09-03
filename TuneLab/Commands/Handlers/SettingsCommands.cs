using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Configs;

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
