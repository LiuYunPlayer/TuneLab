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
using TuneLab.Commands;

namespace TuneLab.Agent;

// 设置助手的【写】那一半，尚未搬进命令面（读那一半已是 `setting list`，见
// TuneLab/Commands/Handlers/SettingsCommands.cs）。直接读写 SettingsRegistry——设置的声明
// （键/标签/所在页/控件 config/默认/重启标记/描述）在那里是单一真源，故这里不重复任何一份表。
// 值校验一律按条目声明的 config（滑条范围 / 下拉成员 / 布尔 / 路径存在性），判据来自共享的 SettingsText。

// 改一项设置 + 落盘。写用户的应用配置（非工程数据、历史记录管理器救不回）→ 过 ToolAuthorization 闸门。
internal sealed class SetSettingTool(Func<AuthorizationRequest, CancellationToken, Task<ScriptAuthDecision>>? confirm = null) : IAgentTool
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
            new AuthorizationRequest(WriteKind.SettingChange, 0, item.Key, ConfigText.FormatValue(value)), confirm, cancellationToken);
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
