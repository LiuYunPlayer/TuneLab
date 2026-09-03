using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using TuneLab.Configs;
using TuneLab.Foundation;
using TuneLab.I18N;
using TuneLab.SDK;

namespace TuneLab.Commands;

// 设置的文本化 + 取值归一化/校验。两工具共用；判据一律来自条目自身的声明（config / DynamicOptions），无第二套表。
internal static class SettingsText
{
    const int MaxListedOptions = 12;   // 选项过多（系统字体数百项）时截断，避免淹没上下文

    // 设置窗页名的本地化文本。只给【纯本地化标签】：要不要连英文原名一起呈现（"General ("常规")"）
    // 是渲染的事，判据在命令的 Render 里，好让结构化结果里存的是两个独立字段而不是拼好的串。
    // 行标同理，直接用 SettingItem.Label / DisplayLabel 两个字段，无需助手。
    public static string PageLabelText(SettingTab tab) => tab.ToString().Tr(SettingItem.LabelTranslationContext);

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

    // 运行时选项（音频驱动/设备、系统字体、语言）。它们要问引擎和字体管理器，**在引擎尚未初始化的进程里
    // 会抛**（headless / CI 就是这种：AudioEngine.GetAllDrivers 会 NRE）。一组选项取不到不该拖垮整条
    // 命令的回报，故就地兜住、退化成 config 的静态项——同 SchemaText 对插件求值的处置。
    static IReadOnlyList<ComboBoxItem>? DynamicOptions(SettingItem item)
    {
        if (item.DynamicOptions == null)
            return null;
        try { return item.DynamicOptions.Invoke(); }
        catch (Exception ex)
        {
            Log.Warning(string.Format("Dynamic options for setting '{0}' are unavailable: {1}", item.Key, ex.Message));
            return null;
        }
    }

    // 该条目的下拉选项（运行时选项优先，其次 config 静态项）；非下拉条目返回 null。
    // SettingItem<int> 的下拉项存的是数字的【字符串】形（"44100"，设置窗经 .Select(int.Parse) 桥到 int），
    // 呈现给模型时还原成数字，免得它以为要传字符串。
    static IReadOnlyList<ComboBoxItem>? Options(SettingItem item)
    {
        var options = DynamicOptions(item) ?? (item.Config as ComboBoxConfig)?.Items;
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
