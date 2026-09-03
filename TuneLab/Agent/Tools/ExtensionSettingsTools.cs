using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using TuneLab.Commands;
using TuneLab.Extensions;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Agent;

// 扩展【自身】设置（设置窗「扩展」页那些，由插件经 IExtensionSettings 声明、存 ExtensionSettings.json）的
// 写那一半。读那一半已搬进命令面（`extension settings`），定位/求值/校验两边共用
// TuneLab.Commands.ExtensionSettingsText——判据只有一份。
//
// 改一个字段 + 落盘 + 立即回喂给插件。过 ToolAuthorization 闸门。
//
// 【密钥政策（用户 2026-07-26 定）】：声明为 IsPassword 的字段（API key / 许可证等）一律拒写，让 agent
// 引导用户自己去设置窗填。理由：把用户密钥经模型上下文送去第三方服务，风险与收益完全不成比例。
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
        // 共用查找的 message 不带 "Error: " 前缀（前缀是入口的事），故这里自己加。
        var (entry, resolveError) = ExtensionSettingsText.Resolve(extension, packageId);
        if (resolveError is { } lookupError)
            return new FieldPlan("Error: " + lookupError.Message, null, default, "", "", "", default, default);

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
        try { secrets = ExtensionSettingsStore.PasswordKeys(ExtensionSettingsText.SchemaOf(entry, data)); }
        catch (Exception ex) { return string.Format("Error: the extension failed to declare its settings while saving — {0}. Nothing changed.", ex.Message); }

        ExtensionSettingsStore.Save(entry.PackageId, entry.ExtensionKey, data, secrets);
        ExtensionSettingsManager.ApplyOne(entry);   // 立即回喂（实现者抛异常已在内部吞掉并记日志）
        return string.Format(
            "Changed \"{0}\" of \"{1}\" from {2} to {3} and saved it; the extension was handed the new settings immediately. If it seems to have no effect, the engine may only read it when it next starts — tell the user they can restart TuneLab.",
            plan.Key, entry.DisplayName, ConfigText.FormatValue(plan.Old), ConfigText.FormatValue(plan.Value));
    }
}
