using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TuneLab.Extensions;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Agent;

// 扩展【自身】的设置（设置窗「扩展」页那些，由插件经 IExtensionSettings 声明、存 ExtensionSettings.json）。
// 与 list_settings/set_setting（宿主应用设置）平行的一对，判据同样全来自被操作方的声明：schema 取自插件的
// GetSettingsConfig（值的函数、可动态显隐），读写走 ExtensionSettingsManager/Store，写后 ApplyOne 立即回喂。
//
// 【密钥政策（用户 2026-07-26 定）：只读不回灌 + 禁写】——声明为 IsPassword 的字段（API key / 许可证等，
// 走 DPAPI/钥匙串保护）：list 只报 (set)/(not set)、【绝不把明文喂进模型上下文】；set 一律拒绝、让 agent 引导
// 用户自己去设置窗填。理由：把用户密钥经模型上下文送去第三方服务，风险与收益完全不成比例。
internal sealed class ListExtensionSettingsTool : IAgentTool
{
    public string Name => "list_extension_settings";

    public string Description =>
        "List the extensions that have their OWN settings (the Settings window's Extensions page) and, for one of them, its fields: id, label, type/range/options, default and current value. " +
        "WITHOUT `extension`: lists which extensions declare settings. WITH `extension` = an id or name from that list: lists that extension's fields. " +
        "These are the plugin's own options (e.g. a model path, a device choice) — NOT the app settings (list_settings) and NOT a plugin's per-part/note parameters (list_sound_sources / list_effects). " +
        "Secret fields (API keys, licences) are only reported as set/not set — their values are never exposed and the agent cannot write them. Read-only.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "extension": { "type": "string", "description": "Optional: the extension's id (\"<kind>:<id>\" or just the id) or display name, from a prior no-argument call." },
            "packageId": { "type": "string", "description": "Optional: needed only when two installed packages provide the same extension id (the list shows the packageIds)." }
          },
          "additionalProperties": false
        }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string? extension, packageId;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            extension = doc.RootElement.GetStringOrNull("extension");
            packageId = doc.RootElement.GetStringOrNull("packageId");
        }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        // 枚举与 schema 求值都跑插件代码（GetSettingsConfig）→ UI 线程，与宿主其余扩展操作一致。
        return await Dispatcher.UIThread.InvokeAsync(() =>
            string.IsNullOrWhiteSpace(extension) ? ListEntries() : DescribeEntry(extension!, packageId));
    }

    static string ListEntries()
    {
        var entries = ExtensionSettingsManager.GetEntries();
        if (entries.Count == 0)
            return "No installed extension declares its own settings. (Plugin parameters that belong to a part/note are a different thing — see list_sound_sources / list_effects.)";

        var sb = new StringBuilder();
        sb.Append(entries.Count).Append(" extension(s) with their own settings:");
        foreach (var entry in entries)
        {
            sb.Append("\n- ").Append(entry.ExtensionKey).Append(" \"").Append(entry.DisplayName)
              .Append("\" [package=").Append(ExtensionManager.GetPackageName(entry.PackageId)).Append(", packageId=").Append(entry.PackageId).Append(']');
            var (config, error) = ExtensionSettingsText.SchemaOf(entry);
            sb.Append(error != null ? "  (the extension failed to declare its settings — " + error + ")"
                                    : "  " + config!.Properties.Count + " field(s)");
        }
        sb.Append("\nPass extension=<id or name> to see one extension's fields. The user edits these in the Settings window's Extensions page.");
        return sb.ToString();
    }

    static string DescribeEntry(string query, string? packageId)
    {
        var (entry, error) = ExtensionSettingsText.Resolve(query, packageId);
        if (error != null)
            return error;

        var (config, schemaError) = ExtensionSettingsText.SchemaOf(entry);
        if (schemaError != null)
            return string.Format("Error: the extension \"{0}\" failed to declare its settings — {1}", entry.DisplayName, schemaError);
        if (config!.Properties.Count == 0)
            return string.Format("\"{0}\" declares no settings fields (at the current values).", entry.DisplayName);

        var values = ExtensionSettingsManager.Load(entry);
        var sb = new StringBuilder();
        sb.Append(string.Format("Settings of \"{0}\" ({1}, package \"{2}\"), {3} field(s). Change one with set_extension_setting.",
            entry.DisplayName, entry.ExtensionKey, ExtensionManager.GetPackageName(entry.PackageId), config.Properties.Count));
        sb.Append("\nFormat: <key> \"<label>\": <allowed> — current <value>, default <value>");
        foreach (var kv in config.Properties)
        {
            var key = kv.Key.Id;
            sb.Append("\n- ").Append(key);
            if (!string.IsNullOrEmpty(kv.Key.DisplayText) && kv.Key.DisplayText != key)
                sb.Append(" (\"").Append(kv.Key.DisplayText).Append("\")");
            sb.Append(": ");

            // 密钥字段：只报有没有设过，绝不回灌值，并点明 agent 不可写（政策，见文件头）。
            if (kv.Value is TextBoxConfig { IsPassword: true })
            {
                bool set = values.TryGetValue(key, out var secret) && secret.ToString(out var s) && s.Length > 0;
                sb.Append("secret text — ").Append(set ? "currently SET" : "NOT set")
                  .Append(" (value hidden; the agent cannot read or write it — the user must type it in the Settings window's Extensions page)");
                continue;
            }

            sb.Append(ConfigText.Describe(kv.Value));
            sb.Append(" — current ").Append(values.TryGetValue(key, out var v)
                ? ConfigText.FormatValue(v)
                : "(unset, so the default applies)");
            if (kv.Value is IValueConfig leaf)
                sb.Append(", default ").Append(ConfigText.FormatValue(leaf.DefaultValue));
        }
        sb.Append("\n(Fields can appear/disappear depending on other values — re-list after a change if you expect that.)");
        return sb.ToString();
    }
}

// 改一个扩展设置字段 + 落盘 + 立即回喂给插件。过 ToolAuthorization 闸门；密钥字段一律拒写。
internal sealed class SetExtensionSettingTool(Func<AgentAuthorizationRequest, CancellationToken, Task<ScriptAuthDecision>>? confirm = null) : IAgentTool
{
    public string Name => "set_extension_setting";

    public string Description =>
        "Change ONE field of an extension's own settings (get the extension, field keys and allowed values from list_extension_settings first) and save it; the extension is handed the new settings right away. " +
        "The value is validated against the field's declared type/range/options. " +
        "Secret fields (API keys, licences) CANNOT be set by the agent — tell the user to type those in the Settings window's Extensions page themselves. " +
        "Needs the user's authorization; if refused, point them at that page. Some engines only pick a setting up when they next start, so mention a restart if a change seems to have no effect.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "extension": { "type": "string", "description": "The extension's id (\"<kind>:<id>\" or just the id) or display name, as listed by list_extension_settings." },
            "key": { "type": "string", "description": "The field key exactly as listed." },
            "value": { "type": ["string", "number", "boolean"], "description": "The new value, matching the field's declared type/range/options." },
            "packageId": { "type": "string", "description": "Optional: needed only when two packages provide the same extension id." }
          },
          "required": ["extension", "key", "value"],
          "additionalProperties": false
        }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string extension, key;
        string? packageId;
        JsonElement raw;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            extension = doc.RootElement.GetString("extension");
            key = doc.RootElement.GetString("key");
            packageId = doc.RootElement.GetStringOrNull("packageId");
            raw = doc.RootElement.Require("value").Clone();
        }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        extension = (extension ?? "").Trim();
        key = (key ?? "").Trim();

        var plan = await Dispatcher.UIThread.InvokeAsync(() => Plan(extension, key, packageId, raw));
        if (plan.Error != null)
            return plan.Error;
        if (plan.NoOp != null)
            return plan.NoOp;

        var (proceed, message) = await ToolAuthorization.AuthorizeAsync(
            new AgentAuthorizationRequest(AgentWriteKind.ExtensionSettingChange, 0, plan.Target, ConfigText.FormatValue(plan.Value)), confirm, cancellationToken);
        if (!proceed)
            return message;

        return message + await Dispatcher.UIThread.InvokeAsync(() => Apply(plan));
    }

    readonly record struct FieldPlan(string? Error, string? NoOp, ExtensionSettingsManager.Entry Entry, string Key, string Label, string Target, PropertyValue Value, PropertyValue Old);

    static FieldPlan Plan(string extension, string key, string? packageId, JsonElement raw)
    {
        var (entry, resolveError) = ExtensionSettingsText.Resolve(extension, packageId);
        if (resolveError != null)
            return new FieldPlan(resolveError, null, default, "", "", "", default, default);

        var (config, schemaError) = ExtensionSettingsText.SchemaOf(entry);
        if (schemaError != null)
            return new FieldPlan(string.Format("Error: the extension \"{0}\" failed to declare its settings — {1}", entry.DisplayName, schemaError),
                null, default, "", "", "", default, default);

        // 字段按 id 找（大小写宽容，归一到声明写法）。
        PropertyKey? found = null;
        IControllerConfig? fieldConfig = null;
        foreach (var kv in config!.Properties)
        {
            if (!string.Equals(kv.Key.Id, key, StringComparison.OrdinalIgnoreCase))
                continue;
            found = kv.Key;
            fieldConfig = kv.Value;
            break;
        }
        if (found == null)
            return new FieldPlan(string.Format("Error: \"{0}\" has no settings field \"{1}\". Its fields are: {2}. (Call list_extension_settings for types and ranges.)",
                entry.DisplayName, key, string.Join(", ", config.Properties.Select(p => p.Key.Id))),
                null, default, "", "", "", default, default);

        var fieldKey = found.Value.Id;
        var label = string.IsNullOrEmpty(found.Value.DisplayText) ? fieldKey : found.Value.DisplayText!;
        var target = entry.DisplayName + " → " + fieldKey;

        // 密钥字段：拒写（政策，见文件头）。
        if (fieldConfig is TextBoxConfig { IsPassword: true })
            return new FieldPlan(string.Format(
                "Error: \"{0}\" is a secret field (API key / licence), which the agent is not allowed to set. Ask the user to enter it themselves in the Settings window's Extensions page, under \"{1}\".",
                fieldKey, entry.DisplayName), null, default, "", "", "", default, default);

        var (value, valueError) = ExtensionSettingsText.Normalize(fieldConfig!, raw, fieldKey);
        if (valueError != null)
            return new FieldPlan("Error: " + valueError, null, default, "", "", "", default, default);

        var values = ExtensionSettingsManager.Load(entry);
        var old = values.TryGetValue(fieldKey, out var cur) ? cur
            : fieldConfig is IValueConfig leaf ? leaf.DefaultValue : PropertyValue.Null;
        if (value.Equals(old))
            return new FieldPlan(null, string.Format("\"{0}\" of \"{1}\" is already {2}. Nothing changed.", fieldKey, entry.DisplayName, ConfigText.FormatValue(value)),
                default, "", "", "", default, default);

        return new FieldPlan(null, null, entry, fieldKey, label, target, value, old);
    }

    // 落地（UI 线程）：读全量已存值 → 改一格 → 按【改后值算出的】schema 取密钥集 → 落盘 → ApplyOne 立即回喂。
    // 与设置窗关页时的保存路径完全一致（含密钥集按当前值重算，避免动态面板下漏标/误标）。
    static string Apply(FieldPlan plan)
    {
        var entry = plan.Entry;
        var values = ExtensionSettingsManager.Load(entry);
        values[plan.Key] = plan.Value;
        var data = ExtensionSettingsStore.ToPropertyObject(values);

        HashSet<string> secrets;
        try { secrets = ExtensionSettingsStore.PasswordKeys(entry.Settings.GetSettingsConfig(new AgentSettingsContext(data))); }
        catch (Exception ex) { return string.Format("Error: the extension failed to declare its settings while saving — {0}. Nothing changed.", ex.Message); }

        ExtensionSettingsStore.Save(entry.PackageId, entry.ExtensionKey, data, secrets);
        ExtensionSettingsManager.ApplyOne(entry);   // 立即回喂（实现者抛异常已在内部吞掉并记日志）
        return string.Format(
            "Changed \"{0}\" of \"{1}\" from {2} to {3} and saved it; the extension was handed the new settings immediately. If it seems to have no effect, the engine may only read it when it next starts — tell the user they can restart TuneLab.",
            plan.Key, entry.DisplayName, ConfigText.FormatValue(plan.Old), ConfigText.FormatValue(plan.Value));
    }
}

// GetSettingsConfig 的求值上下文（对齐设置窗的私有 SettingsContext）：给插件看当刻值快照，动态面板据此重算。
internal sealed class AgentSettingsContext(PropertyObject values) : IExtensionSettingsContext
{
    public PropertyObject Settings => values;
}

// 扩展设置的定位 / schema 求值 / 取值校验。两工具共用。
internal static class ExtensionSettingsText
{
    // 按 "kind:id" / 裸 id / 显示名定位（可用 packageId 消歧；同 id 跨包并存时不猜、要求点明）。
    public static (ExtensionSettingsManager.Entry Entry, string? Error) Resolve(string query, string? packageId)
    {
        var entries = ExtensionSettingsManager.GetEntries();
        if (entries.Count == 0)
            return (default, "Error: no installed extension declares its own settings.");

        query = query.Trim();
        packageId = (packageId ?? "").Trim();
        var matches = entries.Where(e =>
                string.Equals(e.ExtensionKey, query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.ExtensionId, query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.DisplayName, query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (packageId.Length != 0)
            matches = matches.Where(e => string.Equals(e.PackageId, packageId, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 0)
            return (default, string.Format("Error: no extension with settings matches \"{0}\"{1}. Call list_extension_settings to see the available ones.",
                query, packageId.Length == 0 ? "" : " in package \"" + packageId + "\""));
        if (matches.Count > 1)
            return (default, string.Format("Error: \"{0}\" is provided by more than one package ({1}). Pass packageId to say which one.",
                query, string.Join(", ", matches.Select(m => m.PackageId))));
        return (matches[0], null);
    }

    // 插件声明的 schema（按当刻已存值求值——config 可随值动态显隐）。插件抛错就地捕获、如实回报，不拖垮工具。
    public static (ObjectConfig? Config, string? Error) SchemaOf(ExtensionSettingsManager.Entry entry)
    {
        try
        {
            var values = ExtensionSettingsStore.ToPropertyObject(ExtensionSettingsManager.Load(entry));
            return (entry.Settings.GetSettingsConfig(new AgentSettingsContext(values)), null);
        }
        catch (Exception ex) { return (null, ex.Message); }
    }

    // JSON 实参 → 该字段能吃的 PropertyValue，判据【只看字段自己的 config】（与宿主应用设置那边按 CLR 类型判不同：
    // 扩展字段没有静态类型，config 就是唯一真源）。
    public static (PropertyValue Value, string? Error) Normalize(IControllerConfig config, JsonElement raw, string key)
    {
        switch (config)
        {
            case CheckBoxConfig:
                return JsonScalar.TryBoolean(raw, out var b)
                    ? (PropertyValue.Create(b), null)
                    : (default, string.Format("\"{0}\" is a boolean; got {1}.", key, JsonScalar.Text(raw)));

            case SliderConfig slider:
            {
                if (!JsonScalar.TryNumber(raw, out var d))
                    return (default, string.Format("\"{0}\" is a number; got {1}.", key, JsonScalar.Text(raw)));
                double min = slider.Scale.ToValue(0), max = slider.Scale.ToValue(1);
                if (d < min || d > max)
                    return (default, string.Format("{0} is out of range for \"{1}\": allowed [{2}, {3}].",
                        ConfigText.FormatNum(d), key, ConfigText.FormatNum(min), ConfigText.FormatNum(max)));
                return (PropertyValue.Create(d), null);
            }

            case DraggableNumberBoxConfig number:
            {
                if (!JsonScalar.TryNumber(raw, out var d))
                    return (default, string.Format("\"{0}\" is a number; got {1}.", key, JsonScalar.Text(raw)));
                if (number.Min is { } min && d < min)
                    return (default, string.Format("{0} is below the minimum {1} for \"{2}\".", ConfigText.FormatNum(d), ConfigText.FormatNum(min), key));
                if (number.Max is { } max && d > max)
                    return (default, string.Format("{0} is above the maximum {1} for \"{2}\".", ConfigText.FormatNum(d), ConfigText.FormatNum(max), key));
                return (PropertyValue.Create(d), null);
            }

            case ComboBoxConfig combo:
            {
                var given = JsonScalar.Text(raw);
                foreach (var pass in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
                    foreach (var option in combo.Items)
                    {
                        if (option.SubItems != null || option.Value.IsNull())
                            continue;
                        if (string.Equals(JsonScalar.Literal(option.Value), given, pass))
                            return (option.Value, null);   // 值形态照声明原样（可能是文本/数字/布尔）
                    }
                return (default, string.Format("\"{0}\" is not an allowed value for \"{1}\": {2}.", given, key, ConfigText.Describe(combo)));
            }

            case TextBoxConfig:
                return raw.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? (default, string.Format("\"{0}\" is text; got a boolean.", key))
                    : (PropertyValue.Create(JsonScalar.Text(raw)), null);

            default:
                return (default, string.Format("\"{0}\" is a grouped/complex field that can't be set here — ask the user to edit it in the Settings window's Extensions page.", key));
        }
    }
}
