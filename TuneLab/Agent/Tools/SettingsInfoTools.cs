using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TuneLab.Configs;
using TuneLab.Foundation;
using TuneLab.I18N;
using TuneLab.SDK;

namespace TuneLab.Agent;

// 设置助手（诉求 2「想调设置 → 告诉我在哪调/怎么调，或直接帮我调」）。两件工具都直接读写 SettingsRegistry
// ——设置的声明（键/标签/所在页/控件 config/默认/重启标记/描述）在那里是单一真源，故这里不重复任何一份表：
//  · list_settings 只读枚举（同时给出"在设置窗哪一页的哪一行"，让 agent 能【教用户自己改】）；
//  · set_setting  按键写一项 + 落盘，过 ToolAuthorization 闸门（ReadOnlyAdvice=只建议不改 / Confirm=问 / Auto=改）
//    ——恰好把"告诉在哪调"与"自动调"统一进同一档位。
// 值校验一律按条目声明的 config（滑条范围 / 下拉成员 / 布尔 / 路径存在性），不设第二套判据。

// 列出全部宿主设置：键、标签（含本地化）、所在设置窗页、类型/取值范围或选项、当前值、默认值、是否需重启、agent 能否改。
internal sealed class ListSettingsTool : IAgentTool
{
    public string Name => "list_settings";

    public string Description =>
        "List TuneLab's application settings (the Settings window): each one's key, label, which page it lives on, allowed type/range/options, current value and default. " +
        "Use it to answer \"where/how do I change X\" — tell the user the page and the row label so they can do it themselves — and always before set_setting, to get the exact key and allowed values. " +
        "Read-only. Note these are app-wide preferences, NOT project data (project/track/part settings go through run_script) and NOT plugin parameters (see list_sound_sources / list_effects).";

    public string ParametersJsonSchema => """
        { "type": "object", "properties": {}, "additionalProperties": false }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
        // 选项取现值会跑引擎/字体枚举（AudioEngine / FontManager），与宿主其余引擎操作一致放 UI 线程。
        => await Dispatcher.UIThread.InvokeAsync(Describe);

    static string Describe()
    {
        var sb = new StringBuilder();
        sb.Append(SettingsRegistry.All.Count).Append(" application setting(s). Change one with set_setting(key, value).");
        sb.Append("\nSettings window pages: ").Append(string.Join(", ", Enum.GetValues<SettingTab>().Select(SettingsText.PageLabel)));
        sb.Append("\nFormat: <key> \"<label>\" [page]: <allowed> — current <value>, default <value>");
        sb.Append("\n(\"current\" is the value in the user's settings file; an empty text value means \"use the default\".)");

        foreach (var item in SettingsRegistry.All)
        {
            sb.Append("\n- ").Append(item.Key).Append(' ').Append(SettingsText.LabelText(item));
            sb.Append(" [").Append(item.Tab is { } tab ? SettingsText.PageLabel(tab) : "not in the Settings window").Append("]: ");
            sb.Append(SettingsText.Allowed(item));
            sb.Append(" — current ").Append(ConfigText.FormatValue(item.GetValue()));
            sb.Append(", default ").Append(ConfigText.FormatValue(item.GetDefaultValue()));
            if (item.RestartRequired)
                sb.Append(". Needs a restart to take effect");
            if (!item.AgentWritable)
                sb.Append(". NOT agent-writable — only the user can change it");
            sb.Append('.');
            if (!string.IsNullOrEmpty(item.Description))
                sb.Append("\n  note: ").Append(item.Description);
        }
        return sb.ToString();
    }
}

// 改一项设置 + 落盘。写用户的应用配置（非工程数据、历史记录管理器救不回）→ 过 ToolAuthorization 闸门。
internal sealed class SetSettingTool(Func<AgentAuthorizationRequest, CancellationToken, Task<ScriptAuthDecision>>? confirm = null) : IAgentTool
{
    public string Name => "set_setting";

    public string Description =>
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

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string key;
        JsonElement raw;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            key = doc.RootElement.GetString("key");
            raw = doc.RootElement.Require("value").Clone();   // doc 随 using 释放，元素须先克隆
        }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        key = (key ?? "").Trim();
        var item = SettingsRegistry.All.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            return string.Format("Error: no setting with key \"{0}\". Call list_settings to see the exact keys.", key);
        if (!item.AgentWritable)
            return string.Format("Error: the setting \"{0}\" cannot be changed by the agent — only the user can. {1}Tell the user where to change it themselves.",
                item.Key, string.IsNullOrEmpty(item.Description) ? "" : item.Description + " ");

        // 校验按条目声明的 config 做（选项枚举可能跑引擎/字体枚举 → UI 线程）。
        var (value, error) = await Dispatcher.UIThread.InvokeAsync(() => SettingsText.Normalize(item, raw));
        if (error != null)
            return "Error: " + error;

        if (value.Equals(item.GetValue()))
            return string.Format("The setting \"{0}\" is already {1}. Nothing changed.", item.Key, ConfigText.FormatValue(value));

        // 改用户的应用配置 → 过授权闸门（Auto 直接改 / Confirm 卡片裁决 / ReadOnlyAdvice 不改+建议）。无预览-回退。
        var (proceed, message) = await ToolAuthorization.AuthorizeAsync(
            new AgentAuthorizationRequest(AgentWriteKind.SettingChange, 0, item.Key, ConfigText.FormatValue(value)), confirm, cancellationToken);
        if (!proceed)
            return message;

        var old = item.GetValue();
        bool ok = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!item.TrySetValue(value))
                return false;
            Settings.Save(PathManager.SettingsFilePath);
            return true;
        });
        if (!ok)
            return string.Format("Error: the setting \"{0}\" rejected the value {1}. Nothing changed.", item.Key, ConfigText.FormatValue(value));

        var sb = new StringBuilder(message);
        sb.Append(string.Format("Changed \"{0}\" from {1} to {2} and saved the settings file.",
            item.Key, ConfigText.FormatValue(old), ConfigText.FormatValue(item.GetValue())));
        if (item.RestartRequired)
            sb.Append(" It only takes full effect after the user restarts TuneLab — tell them so.");
        return sb.ToString();
    }
}

// 设置的文本化 + 取值归一化/校验。两工具共用；判据一律来自条目自身的声明（config / DynamicOptions），无第二套表。
internal static class SettingsText
{
    const int MaxListedOptions = 12;   // 选项过多（系统字体数百项）时截断，避免淹没上下文

    // "<英文标签>" 或 "<英文标签> ("<本地化标签>")"（本地化不同时给出，便于 agent 用用户语言指路）。
    public static string LabelText(SettingItem item)
    {
        var localized = item.DisplayLabel;
        return localized == item.Label ? "\"" + item.Label + "\"" : "\"" + item.Label + "\" (\"" + localized + "\")";
    }

    public static string PageLabel(SettingTab tab)
    {
        var localized = tab.ToString().Tr(SettingItem.LabelTranslationContext);
        return localized == tab.ToString() ? tab.ToString() : tab + " (\"" + localized + "\")";
    }

    // 允许值短语：下拉走现取选项（截断 + 标注总数），路径类点明后缀，其余交给共享 ConfigText。
    public static string Allowed(SettingItem item)
    {
        if (item.FilePatterns != null)
            return string.Format("path to an existing file ({0}), or \"\" to clear it", string.Join(", ", item.FilePatterns));
        var options = Options(item);
        if (options == null)
            return ConfigText.Describe(item.Config);

        var listed = options.Count <= MaxListedOptions ? options : options.Take(MaxListedOptions).ToList();
        var text = ConfigText.Describe(ComboBoxConfig.Create(listed));
        return options.Count <= MaxListedOptions
            ? text
            : text + string.Format(" (first {0} of {1} options; pass any valid one)", MaxListedOptions, options.Count);
    }

    // 该条目的下拉选项（运行时选项优先，其次 config 静态项）；非下拉条目返回 null。
    // SettingItem<int> 的下拉项存的是数字的【字符串】形（"44100"，设置窗经 .Select(int.Parse) 桥到 int），
    // 呈现给模型时还原成数字，免得它以为要传字符串。
    static IReadOnlyList<ComboBoxItem>? Options(SettingItem item)
    {
        var options = item.DynamicOptions?.Invoke() ?? (item.Config as ComboBoxConfig)?.Items;
        if (options == null)
            return null;
        if (item is not SettingItem<int>)
            return options;

        var numeric = new List<ComboBoxItem>(options.Count);
        foreach (var o in options)
            numeric.Add(o.Value.ToString(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? (ComboBoxItem)d : o);
        return numeric;
    }

    // JSON 实参 → 该条目能吃的 PropertyValue，并按声明校验。返回 (值, null) 或 (default, 错误文本)。
    // 模型常把数字写成字符串（或反之），故按【条目的值类型】归一化而非照抄 JSON 类型；下拉成员比对用无引号的字面量。
    public static (PropertyValue Value, string? Error) Normalize(SettingItem item, JsonElement raw)
    {
        switch (item)
        {
            case SettingItem<bool>:
            {
                if (JsonScalar.TryBoolean(raw, out var b))
                    return (PropertyValue.Create(b), null);
                return (default, string.Format("the setting \"{0}\" is a boolean; got {1}.", item.Key, JsonScalar.Text(raw)));
            }
            case SettingItem<int> when Options(item) is { } options:
                return FromOptions(item, raw, options, numeric: true);
            case SettingItem<string> when Options(item) is { } options:
                return FromOptions(item, raw, options, numeric: false);
            case SettingItem<int> or SettingItem<double>:
            {
                if (!JsonScalar.TryNumber(raw, out var d))
                    return (default, string.Format("the setting \"{0}\" is a number; got {1}.", item.Key, JsonScalar.Text(raw)));
                if (item.Config is SliderConfig s)
                {
                    double min = s.Scale.ToValue(0), max = s.Scale.ToValue(1);
                    if (d < min || d > max)
                        return (default, string.Format("{0} is out of range for \"{1}\": allowed [{2}, {3}].",
                            ConfigText.FormatNum(d), item.Key, ConfigText.FormatNum(min), ConfigText.FormatNum(max)));
                }
                if (item is SettingItem<int>)
                    d = Math.Round(d);
                return (PropertyValue.Create(d), null);
            }
            case SettingItem<string>:
            {
                if (raw.ValueKind == JsonValueKind.True || raw.ValueKind == JsonValueKind.False)
                    return (default, string.Format("the setting \"{0}\" is text; got a boolean.", item.Key));
                var text = JsonScalar.Text(raw);
                // 路径类设置（FilePatterns 非空）：空串 = 清除；非空必须真实存在，否则那项功能会静默失效。
                if (item.FilePatterns != null && text.Length > 0 && !File.Exists(text))
                    return (default, string.Format("the file \"{0}\" does not exist. \"{1}\" needs an existing file path (pattern {2}), or \"\" to clear it.",
                        text, item.Key, string.Join(", ", item.FilePatterns)));
                return (PropertyValue.Create(text), null);
            }
            default:
                return (default, string.Format("the setting \"{0}\" has an unsupported value type and can't be set here.", item.Key));
        }
    }

    // 下拉：按无引号字面量比对（模型给 44100 或 "44100" 都行；字符串项大小写不符时归到正规写法）。
    static (PropertyValue Value, string? Error) FromOptions(SettingItem item, JsonElement raw, IReadOnlyList<ComboBoxItem> options, bool numeric)
    {
        var given = JsonScalar.Text(raw);
        foreach (var pass in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
        {
            foreach (var o in options)
            {
                if (o.SubItems != null || o.Value.IsNull())
                    continue;
                if (!string.Equals(JsonScalar.Literal(o.Value), given, pass))
                    continue;
                // 值类型决定落进条目的 PropertyValue 形态：int 条目要数字（TrySetValue 走 ToDouble），string 条目要文本。
                if (!numeric)
                    return (o.Value, null);
                return JsonScalar.TryNumber(o.Value, out var d)
                    ? (PropertyValue.Create(d), null)
                    : (default, string.Format("the option {0} of \"{1}\" is not a number.", JsonScalar.Literal(o.Value), item.Key));
            }
        }
        var listed = options.Count <= MaxListedOptions ? options : options.Take(MaxListedOptions).ToList();
        return (default, string.Format("{0} is not an allowed value for \"{1}\": {2}{3}.",
            given.Length == 0 ? "an empty value" : "\"" + given + "\"", item.Key,
            ConfigText.Describe(ComboBoxConfig.Create(listed)),
            options.Count <= MaxListedOptions ? "" : string.Format(" (first {0} of {1})", MaxListedOptions, options.Count)));
    }

}
