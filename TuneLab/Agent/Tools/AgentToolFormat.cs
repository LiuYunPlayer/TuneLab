using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TuneLab.Extensions;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Agent;

// 环境感知只读工具共用的文本化助手：把 SDK 的 config 家族 + 引擎目录转成回灌模型的可读文本。
// 收在一处，让音源(SoundSourceInfoTools) / 插件 / effect(EffectInfoTools) / 脚本入参(SavedScriptSupport)
// 对同一 config 类型给出一致措辞，且新增 config 类型只改这里一处。

// 单个 config → "类型/范围/选项" 短语（不含默认值——默认由调用方按需另取，因来源不同：叶子 config 的 DefaultValue、
// 或用户上次值等）。
internal static class ConfigText
{
    public static string Describe(IControllerConfig config) => config switch
    {
        SliderConfig s => string.Format("number in [{0}, {1}]", FormatNum(s.Scale.ToValue(0)), FormatNum(s.Scale.ToValue(1))),
        DraggableNumberBoxConfig d => "number" + RangeHint(d),
        ComboBoxConfig c => "one of " + Options(c),
        CheckBoxConfig => "boolean (true/false)",
        TextBoxConfig t => t.IsPassword ? "text (masked)" : "text",
        AutomationConfig a => a.IsPiecewise
            ? string.Format("automation track, range [{0}, {1}], piecewise (no baseline)", FormatNum(a.MinValue), FormatNum(a.MaxValue))
            : string.Format("automation track, range [{0}, {1}], default {2}", FormatNum(a.MinValue), FormatNum(a.MaxValue), FormatNum(a.DefaultValue)),
        ObjectConfig => "object (grouped fields)",
        _ => "value",
    };

    static string RangeHint(DraggableNumberBoxConfig d)
    {
        var parts = new StringBuilder();
        if (d.Min is { } min) parts.Append(", min ").Append(FormatNum(min));
        if (d.Max is { } max) parts.Append(", max ").Append(FormatNum(max));
        if (d.Step is { } step) parts.Append(", step ").Append(FormatNum(step));
        return parts.ToString();
    }

    static string Options(ComboBoxConfig c)
    {
        var sb = new StringBuilder("[");
        bool first = true;
        foreach (var item in c.Items)
        {
            if (item.SubItems != null || item.Value.IsNull())
                continue;   // 跳过分组标题 / 分隔线（值为空）
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(FormatValue(item.Value));
            if (!string.IsNullOrEmpty(item.DisplayText) && item.DisplayText != FormatValue(item.Value))
                sb.Append(" (\"").Append(item.DisplayText).Append("\")");
        }
        return sb.Append(']').ToString();
    }

    public static string FormatValue(PropertyValue v)
    {
        if (v.IsNull()) return "(none)";
        if (v.ToBoolean(out var b)) return b ? "true" : "false";
        if (v.ToDouble(out var d)) return FormatNum(d);
        if (v.ToString(out var s)) return "\"" + s + "\"";
        return "(none)";
    }

    public static string FormatNum(double d)
        => d == Math.Floor(d) && !double.IsInfinity(d)
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : d.ToString(CultureInfo.InvariantCulture);
}

// 参数 schema 分组文本化：把引擎声明的一组 config（静态属性 ObjectConfig / 自动化轨 map / 音素 slot map）
// 逐条列成文本。effect 与 voice/instrument 的声明方法返回这几种同型结构，故收在一处共用。
// get 用委托传入（而非直接传结果）：插件求值可能抛错，就地 try/catch 标注该组失败、不拖垮整体回报。
internal static class SchemaText
{
    // ObjectConfig 组（静态属性）→ 逐字段名(+标签)/类型/默认。返回条数（0 = 空组，调用方据总数判「无参数」）。
    public static int AppendProperties(StringBuilder sb, string heading, Func<ObjectConfig> get)
    {
        ObjectConfig config;
        try { config = get(); }
        catch (Exception ex) { sb.Append('\n').Append(heading).Append(": (engine failed to declare — ").Append(ex.Message).Append(')'); return 0; }

        int count = config.Properties.Count;
        if (count == 0) return 0;
        sb.Append('\n').Append(heading).Append(" (").Append(count).Append("):");
        AppendObjectFields(sb, "\n- ", config);
        return count;
    }

    // 自动化轨组（PropertyKey→AutomationConfig）→ 逐轨名(+标签)/范围/默认或分段。返回条数。
    public static int AppendAutomations(StringBuilder sb, string heading, Func<IReadOnlyOrderedMap<PropertyKey, AutomationConfig>> get)
    {
        IReadOnlyOrderedMap<PropertyKey, AutomationConfig> configs;
        try { configs = get(); }
        catch (Exception ex) { sb.Append('\n').Append(heading).Append(": (engine failed to declare — ").Append(ex.Message).Append(')'); return 0; }

        int count = configs.Count;
        if (count == 0) return 0;
        sb.Append('\n').Append(heading).Append(" (").Append(count).Append("):");
        foreach (var kvp in configs)
        {
            sb.Append("\n- ").Append(kvp.Key.Id);
            AppendLabel(sb, kvp.Key);
            sb.Append(": ").Append(ConfigText.Describe(kvp.Value));
        }
        return count;
    }

    // 音素属性组（slot→ObjectConfig，voice 专有）→ 按 slot 角色分小节，各列该 slot 的字段。返回 slot 数。
    // slot 口径：0 = 核心元音、<0 = 引导辅音、>0 = 核后（见 IVoiceSynthesisEngine 音素声明「schema 授给角色而非单个音素」）。
    public static int AppendPhonemes(StringBuilder sb, string heading, Func<IReadOnlyMap<int, ObjectConfig>> get)
    {
        IReadOnlyMap<int, ObjectConfig> configs;
        try { configs = get(); }
        catch (Exception ex) { sb.Append('\n').Append(heading).Append(": (engine failed to declare — ").Append(ex.Message).Append(')'); return 0; }

        int count = configs.Count;
        if (count == 0) return 0;
        sb.Append('\n').Append(heading).Append(" (").Append(count).Append(" slot role(s)):");
        foreach (var kvp in configs)
        {
            int slot = kvp.Key;
            string role = slot == 0 ? "core vowel (slot 0)" : slot < 0 ? "leading consonant (slot " + slot + ")" : "post-core (slot " + slot + ")";
            sb.Append("\n  ").Append(role).Append(':');
            AppendObjectFields(sb, "\n    - ", kvp.Value);
        }
        return count;
    }

    // ObjectConfig 逐字段（bullet 定缩进）：名(+标签)/类型/默认。属性组与音素 slot 组共用。
    static void AppendObjectFields(StringBuilder sb, string bullet, ObjectConfig config)
    {
        foreach (var kvp in config.Properties)
        {
            sb.Append(bullet).Append(kvp.Key.Id);
            AppendLabel(sb, kvp.Key);
            sb.Append(": ").Append(ConfigText.Describe(kvp.Value));
            if (kvp.Value is IValueConfig leaf)
                sb.Append(". default ").Append(ConfigText.FormatValue(leaf.DefaultValue));
        }
    }

    static void AppendLabel(StringBuilder sb, PropertyKey key)
    {
        if (!string.IsNullOrEmpty(key.DisplayText) && key.DisplayText != key.Id)
            sb.Append(" (\"").Append(key.DisplayText).Append("\")");
    }
}

// 引擎目录列表：把某类引擎（voice / instrument / effect）的身份 id / 显示名 / 提供包列成文本。三类的注册表
// API 同型（GetAll*Engines / GetDisplayName / GetProviders），故列表格式收在一处。不触发 Init（只读注册表）。
internal static class EngineCatalog
{
    // 追加 "<Kind> engines (N):" + 逐条 "\"显示名\" [type=<id>, package=<包>]"。空引擎 type=""（voice/instrument 的
    // 无音源回退）跳过；effect 无空引擎、该步为 no-op。多包提供同 type 显 "multiple: a, b"。
    public static void AppendEngineList(StringBuilder sb, string kindLabel, IReadOnlyList<string> engines, Func<string, string> displayName, Func<string, IReadOnlyList<(string PackageId, string DisplayName)>> providers)
    {
        var real = new List<string>();
        foreach (var t in engines)
            if (!string.IsNullOrEmpty(t))
                real.Add(t);

        if (sb.Length > 0) sb.Append('\n');
        sb.Append(kindLabel).Append(" engines (").Append(real.Count).Append("):");
        if (real.Count == 0)
            sb.Append("\n  (none)");
        foreach (var type in real)
        {
            var pkgs = providers(type);
            string pkgLabel;
            if (pkgs.Count == 0)
                pkgLabel = "unknown";
            else if (pkgs.Count == 1)
                pkgLabel = ExtensionManager.GetPackageName(pkgs[0].PackageId);
            else
            {
                var names = new List<string>();
                foreach (var p in pkgs)
                    names.Add(ExtensionManager.GetPackageName(p.PackageId));
                pkgLabel = "multiple: " + string.Join(", ", names);
            }
            sb.Append("\n- \"").Append(displayName(type)).Append("\" [type=").Append(type).Append(", package=").Append(pkgLabel).Append("]");
        }
    }
}
