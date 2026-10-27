using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;

namespace TuneLab.Commands;

// 按插件声明的 config 校验 / 规范化一个值——非界面入口（脚本 setProperty、extension set-setting）共用这一份口径。
// 界面三处（属性面板 / 设置窗 / effect 面板）共用 PropertyObjectController，天然一致；文字 / JSON 入口以前各写一套按控件
// 类型的分支，口径不一、漏写的类型就拒（或不校验），修一处漏一处。这里收成一处，并有覆盖测试逐个 config 类型核对。
//
// 口径：能校验就校验。宿主看得懂的控件按它的约束校验（数值范围、下拉候选、tag 候选……），复合控件（对象 / 数组 / 列表 /
// 变长键控）递归到每个成员。不是值的 config（按钮、自动化轨）一律拒。
// 输入一律是中性的 PropertyValue：各入口先把自己的输入（JsValue / JsonElement）转成它，报错措辞、自身政策（如扩展设置的
// 密钥字段拒写）也归入口。
//
// 列表 / 变长键控的逐元素 / 逐键 config 是插件按当时的数据现算的：校验要用"写入后的值"重新求出的 config（各入口负责），
// 这样新值长度变了、键变了，拿到的也是与它对应的声明。
internal static class ConfigValues
{
    // 本模块明确处理的全部 config 类型（覆盖测试据此核对 SDK 里的每个 IControllerConfig 实现：新增控件忘了在这里
    // 处理，测试即失败）。NotValues 里的是"有 config 但不是值"的，Check 一律拒并说明原因。
    internal static readonly IReadOnlyList<Type> Values =
    [
        typeof(CheckBoxConfig), typeof(SliderConfig), typeof(DraggableNumberBoxConfig), typeof(ComboBoxConfig), typeof(TagConfig),
        typeof(TextBoxConfig), typeof(PathPickerConfig),
        typeof(ObjectConfig), typeof(ArrayConfig), typeof(ListConfig), typeof(ExtensibleObjectConfig),
    ];
    internal static readonly IReadOnlyList<Type> NotValues = [typeof(ButtonConfig), typeof(AutomationConfig)];

    // 校验 value 能否作为 config 字段的值；合法则给出规范后的值（数字一律 double、下拉 / tag 落成候选的存储值、tag 去重）。
    // path 用于报错点名（如 "voices[2].path"）。
    public static (PropertyValue Value, string? Error) Check(IControllerConfig config, PropertyValue value, string path)
    {
        if (value.IsNull())
            return (default, string.Format("\"{0}\" needs a value.", path));
        if (value.IsMultiple())
            return (default, string.Format("\"{0}\": Multiple is not a value.", path));

        switch (config)
        {
            case CheckBoxConfig:
                return TryBoolean(value, out var b)
                    ? (PropertyValue.Create(b), null)
                    : (default, string.Format("\"{0}\" is a boolean; got {1}.", path, Show(value)));

            case SliderConfig slider:
            {
                if (!JsonScalar.TryNumber(value, out var d) || !double.IsFinite(d))
                    return (default, string.Format("\"{0}\" is a number; got {1}.", path, Show(value)));
                double min = slider.Scale.ToValue(0), max = slider.Scale.ToValue(1);
                if (d < min || d > max)
                    return (default, string.Format("{0} is out of range for \"{1}\": allowed [{2}, {3}].",
                        ConfigText.FormatNum(d), path, ConfigText.FormatNum(min), ConfigText.FormatNum(max)));
                return (PropertyValue.Create(d), null);
            }

            case DraggableNumberBoxConfig number:
            {
                if (!JsonScalar.TryNumber(value, out var d) || !double.IsFinite(d))
                    return (default, string.Format("\"{0}\" is a number; got {1}.", path, Show(value)));
                if (number.Min is { } min && d < min)
                    return (default, string.Format("{0} is below the minimum {1} for \"{2}\".", ConfigText.FormatNum(d), ConfigText.FormatNum(min), path));
                if (number.Max is { } max && d > max)
                    return (default, string.Format("{0} is above the maximum {1} for \"{2}\".", ConfigText.FormatNum(d), ConfigText.FormatNum(max), path));
                return (PropertyValue.Create(d), null);
            }

            // 按候选的【存储值】比对（不认显示文字：描述里两者都列给调用方），先区分大小写、再不区分；值形态照声明原样。
            case ComboBoxConfig combo:
                return Match(value, combo.Items.Leaves().Select(o => o.Value)) is { } option
                    ? (option, null)
                    : (default, string.Format("{0} is not an allowed value for \"{1}\": {2}.", Show(value), path, ConfigText.Describe(combo)));

            // tag 组：数组，逐个按候选比对；重复的只留第一个（有序、不重复，见 TagConfig）。错一个就整条拒绝并点名，
            // 不悄悄丢掉它写其余的。
            case TagConfig tags:
            {
                if (!value.ToArray(out var array))
                    return (default, string.Format("\"{0}\" is a list of tags; give an array such as [\"a\", \"b\"]. Allowed: {1}.", path, ConfigText.Describe(tags)));
                var candidates = tags.Items.Leaves().Select(t => t.Value).ToList();
                var values = new List<PropertyValue>();
                foreach (var element in array)
                {
                    if (Match(element, candidates) is not { } match)
                        return (default, string.Format("{0} is not an allowed tag for \"{1}\": {2}.", Show(element), path, ConfigText.Describe(tags)));
                    if (!values.Contains(match))
                        values.Add(match);
                }
                return (new PropertyArray(values), null);
            }

            // 路径也是文本：宿主不校验存不存在（合法与否只有插件知道，见 PathPickerConfig 注释），与手填文本同路。
            // 数字收成它的字面量（"3"）；布尔、数组、对象不收。
            case TextBoxConfig:
            case PathPickerConfig:
                if (value.ToString(out var text))
                    return (PropertyValue.Create(text), null);
                if (value.ToDouble(out var n))
                    return (PropertyValue.Create(ConfigText.FormatNum(n)), null);
                return (default, string.Format("\"{0}\" is text; got {1}.", path, Show(value)));

            case ObjectConfig obj:
                return CheckObject(value, path, obj.Properties, extra: null);

            // 变长键控：已存的键按 Properties，其余键须是 + 菜单的候选键（按其 Template）。
            case ExtensibleObjectConfig extensible:
                return CheckObject(value, path, extensible.Properties, AddableKeys(extensible.AddableElements));

            // 定长数组：长度不超过声明的位数（缺的位退回该位默认值）。
            case ArrayConfig fixedArray:
            {
                if (!value.ToArray(out var array))
                    return (default, string.Format("\"{0}\" is an array of {1}; got {2}.", path, fixedArray.Elements.Count, Show(value)));
                if (array.Count > fixedArray.Elements.Count)
                    return (default, string.Format("\"{0}\" holds at most {1} element(s); got {2}.", path, fixedArray.Elements.Count, array.Count));
                return CheckElements(array, path, i => [fixedArray.Elements[i]]);
            }

            // 变长列表：逐元素 config 是插件按（写入后的）数据现算的，第 i 个元素按第 i 个 config；超出声明的元素须是某个
            // + 候选类型（任一 Template 接受即可）。
            case ListConfig list:
            {
                if (!value.ToArray(out var array))
                    return (default, string.Format("\"{0}\" is a list; give an array. {1}", path, ConfigText.Describe(list)));
                var templates = AddableTemplates(list.AddableElements).ToList();
                return CheckElements(array, path, i => i < list.Elements.Count ? [list.Elements[i]] : templates);
            }

            case ButtonConfig:
                return (default, string.Format("\"{0}\" is a button, not a value — it can only be clicked in the panel.", path));

            case AutomationConfig:
                return (default, string.Format("\"{0}\" is an automation track, not a property value — edit its curve instead.", path));

            default:
                return (default, string.Format("\"{0}\" is a {1}, which the host can't set from text.", path, config.GetType().Name));
        }
    }

    static (PropertyValue, string?) CheckObject(PropertyValue value, string path,
        IReadOnlyOrderedMap<PropertyKey, IControllerConfig> declared, IReadOnlyDictionary<string, IControllerConfig>? extra)
    {
        if (!value.ToObject(out var obj))
            return (default, string.Format("\"{0}\" is an object (grouped fields); got {1}.", path, Show(value)));
        var map = new Map<string, PropertyValue>();
        foreach (var pair in obj.Map)
        {
            var config = declared.FirstOrDefault(kv => kv.Key.Id == pair.Key)?.Value
                ?? (extra != null && extra.TryGetValue(pair.Key, out var template) ? template : null);
            if (config == null)
            {
                var allowed = declared.Select(kv => kv.Key.Id).Concat(extra?.Keys ?? []).Distinct().ToList();
                return (default, string.Format("\"{0}\" has no field \"{1}\". Its fields are: {2}.", path, pair.Key,
                    allowed.Count == 0 ? "(none)" : string.Join(", ", allowed)));
            }
            var (member, error) = Check(config, pair.Value, path + "." + pair.Key);
            if (error != null)
                return (default, error);
            map.Add(pair.Key, member);
        }
        return (new PropertyObject(map), null);
    }

    static (PropertyValue, string?) CheckElements(PropertyArray array, string path, Func<int, IReadOnlyList<IControllerConfig>> configsAt)
    {
        var values = new List<PropertyValue>(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            var configs = configsAt(i);
            string elementPath = string.Format(CultureInfo.InvariantCulture, "{0}[{1}]", path, i);
            if (configs.Count == 0)
                return (default, string.Format("\"{0}\": the list can't hold another element here.", elementPath));
            string? firstError = null;
            PropertyValue? accepted = null;
            foreach (var config in configs)
            {
                var (element, error) = Check(config, array[i], elementPath);
                if (error == null)
                {
                    accepted = element;
                    break;
                }
                firstError ??= error;
            }
            if (accepted is not { } ok)
                return (default, firstError);
            values.Add(ok);
        }
        return (new PropertyArray(values), null);
    }

    // + 菜单候选（展开分组、略去分隔线）。
    static IEnumerable<IControllerConfig> AddableTemplates(IEnumerable<AddableElement> items)
    {
        foreach (var item in items)
        {
            if (item.SubItems is { } sub)
            {
                foreach (var template in AddableTemplates(sub))
                    yield return template;
            }
            else if (item.Template != null)
            {
                yield return item.Template;
            }
        }
    }

    static Dictionary<string, IControllerConfig> AddableKeys(IEnumerable<AddableKey> items)
    {
        var result = new Dictionary<string, IControllerConfig>();
        void Walk(IEnumerable<AddableKey> list)
        {
            foreach (var item in list)
            {
                if (item.SubItems is { } sub)
                    Walk(sub);
                else if (item.Template != null && item.Key.Id != null)
                    result.TryAdd(item.Key.Id, item.Template);
            }
        }
        Walk(items);
        return result;
    }

    static PropertyValue? Match(PropertyValue given, IEnumerable<PropertyValue> candidates)
    {
        var list = candidates.Where(c => !c.IsNull()).ToList();
        foreach (var candidate in list)
        {
            if (candidate.Equals(given))
                return candidate;
        }
        var text = JsonScalar.Literal(given);
        foreach (var pass in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
        {
            foreach (var candidate in list)
            {
                if (string.Equals(JsonScalar.Literal(candidate), text, pass))
                    return candidate;
            }
        }
        return null;
    }

    // 布尔（含 "true"/"false"/1/0 的宽容写法）。
    static bool TryBoolean(PropertyValue value, out bool result)
    {
        if (value.ToBoolean(out result))
            return true;
        var text = JsonScalar.Literal(value);
        if (bool.TryParse(text, out result))
            return true;
        if (text == "1") { result = true; return true; }
        if (text == "0") { result = false; return true; }
        result = false;
        return false;
    }

    static string Show(PropertyValue value) => ConfigText.FormatValue(value);
}
