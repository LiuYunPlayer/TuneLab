using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Extensions;
using TuneLab.SDK;

namespace TuneLab.Commands.Handlers;

// 扩展【自身】设置的读那一半（设置窗「扩展」页那些，由插件经 IExtensionSettings 声明）。与应用设置
// （`setting list`）平行的一族，判据同样全来自被操作方的声明——定位/求值/校验都在共用的
// ExtensionSettingsText 里，写那一半（set_extension_setting）与它共用同一份。
//
// 一条命令两种模式（无参列有哪些扩展 / 带 extension 列该扩展的字段），沿用搬家前那一个工具的形状：
// 两模式回答的是同一个问题的两级（"谁有设置" → "它有哪些格"），拆成两条命令只会让调用方多记一个名字。
//
// 【密钥政策（用户 2026-07-26 定）：只读不回灌 + 禁写】——声明为 IsPassword 的字段（API key / 许可证等，
// 走 DPAPI/钥匙串保护）只报 (set)/(not set)，明文既不进模型上下文、也不进结构化结果（CLI/MCP 的消费者
// 同样拿不到）；写那一半一律拒绝、让调用方引导用户自己去设置窗填。理由：把用户密钥经模型上下文送去
// 第三方服务，风险与收益完全不成比例。
internal sealed class ExtensionSettingsCommand : ICommand
{
    public string Path => "extension settings";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "list_extension_settings";

    public string Brief => "List extensions with their own settings, or one's fields";

    public string Documentation =>
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

    // 枚举与 schema 求值都跑插件代码（GetSettingsConfig）→ 主线程，与宿主其余扩展操作一致。
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        var extension = args.Json.GetStringOrNull("extension");
        var packageId = args.Json.GetStringOrNull("packageId");
        return await ctx.OnMainThread(() => string.IsNullOrWhiteSpace(extension)
            ? ListExtensions()
            : DescribeExtension(extension!, packageId));
    }

    static CommandResult ListExtensions()
    {
        var extensions = new JsonArray();
        foreach (var entry in ExtensionSettingsManager.GetEntries())
        {
            var (config, error) = ExtensionSettingsText.SchemaOf(entry);
            extensions.Add(new JsonObject
            {
                ["extensionKey"] = entry.ExtensionKey,
                ["displayName"] = entry.DisplayName,
                ["package"] = ExtensionManager.GetPackageName(entry.PackageId),
                ["packageId"] = entry.PackageId,
                // 某个扩展声明设置时抛错【不影响清单本身】：那是清单里的一格局部事实，其余照常列出，
                // 故留在成功路径上逐条标注（与下面单个扩展模式的处置不同）。
                ["fieldCount"] = error != null ? null : (JsonNode)config!.Properties.Count,
                ["schemaError"] = error,
            });
        }
        return CommandResult.Ok(new JsonObject { ["extensions"] = extensions });
    }

    static CommandResult DescribeExtension(string query, string? packageId)
    {
        var (entry, resolveError) = ExtensionSettingsText.Resolve(query, packageId);
        if (resolveError is { } error)
            return CommandResult.Fail(error.Code, error.Message);

        var (config, schemaError) = ExtensionSettingsText.SchemaOf(entry);
        // 这里 schema 求不出来就没有产物可给（不像清单模式还剩别的条目），故算这次调用失败——
        // CLI 也该为此非零退出，而不是打一行"这个扩展坏了"就当成功。
        if (schemaError != null)
            return CommandResult.Fail("schema_failed", string.Format(
                "the extension \"{0}\" failed to declare its settings — {1}", entry.DisplayName, schemaError));

        var values = ExtensionSettingsManager.Load(entry);
        var fields = new JsonArray();
        foreach (var kv in config!.Properties)
        {
            var key = kv.Key.Id;
            var field = new JsonObject { ["key"] = key };
            var label = kv.Key.DisplayText;
            if (!string.IsNullOrEmpty(label) && label != key)
                field["label"] = label;

            bool hasValue = values.TryGetValue(key, out var value);

            // 密钥字段：只报有没有设过，值一律不出现在结果里（政策，见类头）。
            if (kv.Value is TextBoxConfig { IsPassword: true })
            {
                field["secret"] = true;
                field["secretSet"] = hasValue && value.ToString(out var secret) && secret.Length > 0;
                fields.Add(field);
                continue;
            }

            field["secret"] = false;
            field["allowed"] = ConfigText.Describe(kv.Value);
            // "有没有存过值"与"存的值是什么"分开记：没存过时默认生效，而存过一个空值是另一件事。
            field["hasCurrent"] = hasValue;
            field["current"] = hasValue ? ConfigText.ToJson(value) : null;
            // 分组/复合字段没有 DefaultValue（IValueConfig 才有），故默认值也要标"有没有"。
            field["hasDefault"] = kv.Value is IValueConfig;
            field["default"] = kv.Value is IValueConfig leaf ? ConfigText.ToJson(leaf.DefaultValue) : null;
            fields.Add(field);
        }

        return CommandResult.Ok(new JsonObject
        {
            ["extensionKey"] = entry.ExtensionKey,
            ["displayName"] = entry.DisplayName,
            ["package"] = ExtensionManager.GetPackageName(entry.PackageId),
            ["packageId"] = entry.PackageId,
            ["fields"] = fields,
        });
    }

    // 措辞与搬家前逐字一致。两模式按 fields 在不在分派。
    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;
        return obj["fields"] is JsonArray fields ? RenderFields(obj, fields) : RenderExtensions(obj);
    }

    static string RenderExtensions(JsonObject obj)
    {
        var extensions = obj["extensions"]!.AsArray();
        if (extensions.Count == 0)
            return "No installed extension declares its own settings. (Plugin parameters that belong to a part/note are a different thing — see list_sound_sources / list_effects.)";

        var sb = new StringBuilder();
        sb.Append(extensions.Count).Append(" extension(s) with their own settings:");
        foreach (var node in extensions)
        {
            var item = node!.AsObject();
            sb.Append("\n- ").Append(item["extensionKey"]!.GetValue<string>())
              .Append(" \"").Append(item["displayName"]!.GetValue<string>())
              .Append("\" [package=").Append(item["package"]!.GetValue<string>())
              .Append(", packageId=").Append(item["packageId"]!.GetValue<string>()).Append(']');
            var error = item["schemaError"]?.GetValue<string>();
            sb.Append(error != null ? "  (the extension failed to declare its settings — " + error + ")"
                                    : "  " + item["fieldCount"]!.GetValue<int>() + " field(s)");
        }
        sb.Append("\nPass extension=<id or name> to see one extension's fields. The user edits these in the Settings window's Extensions page.");
        return sb.ToString();
    }

    static string RenderFields(JsonObject obj, JsonArray fields)
    {
        var displayName = obj["displayName"]!.GetValue<string>();
        if (fields.Count == 0)
            return string.Format("\"{0}\" declares no settings fields (at the current values).", displayName);

        var sb = new StringBuilder();
        sb.Append(string.Format("Settings of \"{0}\" ({1}, package \"{2}\"), {3} field(s). Change one with set_extension_setting.",
            displayName, obj["extensionKey"]!.GetValue<string>(), obj["package"]!.GetValue<string>(), fields.Count));
        sb.Append("\nFormat: <key> \"<label>\": <allowed> — current <value>, default <value>");
        foreach (var node in fields)
        {
            var field = node!.AsObject();
            sb.Append("\n- ").Append(field["key"]!.GetValue<string>());
            var label = field["label"]?.GetValue<string>();
            if (label != null)
                sb.Append(" (\"").Append(label).Append("\")");
            sb.Append(": ");

            if (field["secret"]!.GetValue<bool>())
            {
                sb.Append("secret text — ").Append(field["secretSet"]!.GetValue<bool>() ? "currently SET" : "NOT set")
                  .Append(" (value hidden; the agent cannot read or write it — the user must type it in the Settings window's Extensions page)");
                continue;
            }

            sb.Append(field["allowed"]!.GetValue<string>());
            sb.Append(" — current ").Append(field["hasCurrent"]!.GetValue<bool>()
                ? ConfigText.FormatValue(field["current"])
                : "(unset, so the default applies)");
            if (field["hasDefault"]!.GetValue<bool>())
                sb.Append(", default ").Append(ConfigText.FormatValue(field["default"]));
        }
        sb.Append("\n(Fields can appear/disappear depending on other values — re-list after a change if you expect that.)");
        return sb.ToString();
    }
}
