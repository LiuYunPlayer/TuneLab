using System;
using System.Linq;
using System.Text.Json;
using TuneLab.Extensions;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Commands;

// 扩展【自身】设置（设置窗「扩展」页那些，由插件经 IExtensionSettings 声明、存 ExtensionSettings.json）的
// 定位 / schema 求值 / 取值校验。读（`extension settings`）与写（set_extension_setting）共用一份：
// 两者认的写法、报的措辞必须完全一致——一处认裸 id、另一处只认 "kind:id"，调用方就会在两条命令间试错。
//
// 判据全来自被操作方的声明：schema 取自插件的 GetSettingsConfig（值的函数、可动态显隐），
// 校验只看该字段自己的 config（扩展字段没有静态类型，config 就是唯一真源——与宿主应用设置那边按 CLR 类型判不同）。
internal static class ExtensionSettingsText
{
    // 按 "kind:id" / 裸 id / 显示名定位（可用 packageId 消歧；同 id 跨包并存时不猜、要求点明）。
    // 错误的 message 不带 "Error: " 前缀——前缀由入口加（agent 入口加、CLI 打 stderr 不冗余）。
    public static (ExtensionSettingsManager.Entry Entry, CommandError? Error) Resolve(string query, string? packageId)
    {
        var entries = ExtensionSettingsManager.GetEntries();
        if (entries.Count == 0)
            return (default, new CommandError("no_extension_settings", "no installed extension declares its own settings."));

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
            return (default, new CommandError("not_found", string.Format(
                "no extension with settings matches \"{0}\"{1}. Call list_extension_settings to see the available ones.",
                query, packageId.Length == 0 ? "" : " in package \"" + packageId + "\"")));
        if (matches.Count > 1)
            return (default, new CommandError("ambiguous", string.Format(
                "\"{0}\" is provided by more than one package ({1}). Pass packageId to say which one.",
                query, string.Join(", ", matches.Select(m => m.PackageId)))));
        return (matches[0], null);
    }

    // 插件声明的 schema（按当刻已存值求值——config 可随值动态显隐）。插件抛错就地捕获、如实回报，不拖垮命令。
    public static (ObjectConfig? Config, string? Error) SchemaOf(ExtensionSettingsManager.Entry entry)
    {
        try
        {
            return (SchemaOf(entry, ExtensionSettingsStore.ToPropertyObject(ExtensionSettingsManager.Load(entry))), null);
        }
        catch (Exception ex) { return (null, ex.Message); }
    }

    // 按【给定的一组值】求值：写入落盘时要用改后的值重算一遍（动态面板下密钥集会随之变，见落地路径），
    // 故求值这一步单列一份、不吞异常。求值上下文只在这里造——与设置窗、ExtensionSettingsManager 各自的
    // 私有实现同形（给插件看当刻值快照）。
    public static ObjectConfig SchemaOf(ExtensionSettingsManager.Entry entry, PropertyObject values)
        => entry.Settings.GetSettingsConfig(new Context(values));

    sealed class Context(PropertyObject values) : IExtensionSettingsContext
    {
        public PropertyObject Settings => values;
    }

    // JSON 实参 → 该字段能吃的 PropertyValue，判据【只看字段自己的 config】。
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

            // 路径也是文本：宿主不校验存不存在（合法与否只有插件知道，见 PathPickerConfig 注释），与手填文本同路。
            case TextBoxConfig:
            case PathPickerConfig:
                return raw.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? (default, string.Format("\"{0}\" is text; got a boolean.", key))
                    : (PropertyValue.Create(JsonScalar.Text(raw)), null);

            default:
                return (default, string.Format("\"{0}\" is a grouped/complex field that can't be set here — ask the user to edit it in the Settings window's Extensions page.", key));
        }
    }
}
