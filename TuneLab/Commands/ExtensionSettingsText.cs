using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TuneLab.Extensions;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;
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
    // 都不中时再认包 id：`extension list` 最先展示的就是它，调用方最自然拿它来指；包里只有一个带设置的
    // extension 时无歧义，多个则列出各自的 "kind:id" 要求点名。按包 id 定位成功时另给一句 Hint：这只是兜底，
    // 包里一旦多出第二个带设置的 extension 就不再成立，要让调用方下次改用 "kind:id"。
    // 错误的 message 不带 "Error: " 前缀——前缀由入口加（agent 入口加、CLI 打 stderr 不冗余）。
    public static (ExtensionSettingsManager.Entry Entry, CommandError? Error, string? Hint) Resolve(string query, string? packageId)
    {
        var entries = ExtensionSettingsManager.GetEntries();
        if (entries.Count == 0)
            return (default, new CommandError("no_extension_settings", "no installed extension declares its own settings."), null);

        query = query.Trim();
        packageId = (packageId ?? "").Trim();
        var matches = entries.Where(e =>
                string.Equals(e.ExtensionKey, query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.ExtensionId, query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.DisplayName, query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        bool byPackageId = false;
        if (matches.Count == 0)
        {
            matches = entries.Where(e => string.Equals(e.PackageId, query, StringComparison.OrdinalIgnoreCase)).ToList();
            byPackageId = matches.Count > 0;
        }
        if (packageId.Length != 0)
            matches = matches.Where(e => string.Equals(e.PackageId, packageId, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 0)
            return (default, new CommandError("not_found", string.Format(
                "no extension with settings matches \"{0}\"{1}. Call list_extension_settings to see the available ones.",
                query, packageId.Length == 0 ? "" : " in package \"" + packageId + "\"")), null);
        if (matches.Count > 1 && matches.All(m => string.Equals(m.PackageId, matches[0].PackageId, StringComparison.OrdinalIgnoreCase)))
            return (default, new CommandError("ambiguous", string.Format(
                "the package \"{0}\" has more than one extension with settings ({1}). Name one of them.",
                matches[0].PackageId, string.Join(", ", matches.Select(m => m.ExtensionKey)))), null);
        if (matches.Count > 1)
            return (default, new CommandError("ambiguous", string.Format(
                "\"{0}\" is provided by more than one package ({1}). Pass packageId to say which one.",
                query, string.Join(", ", matches.Select(m => m.PackageId)))), null);

        var hint = byPackageId ? string.Format(
            "Note: \"{0}\" is a package id, not an extension; it was taken to mean that package's only extension with settings, {1}. "
            + "Pass extension=\"{1}\" next time — a package id stops working as soon as the package has more than one extension with settings.",
            query, matches[0].ExtensionKey) : null;
        return (matches[0], null, hint);
    }

    // 结果里带着 Resolve 给的 Hint 时，渲染成末尾单独一行（两条命令共用，措辞只在 Resolve 一处）。
    public static string HintSuffix(JsonObject data)
        => data["hint"]?.GetValue<string>() is { } hint ? "\n" + hint : string.Empty;

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

    // JSON 实参 → 该字段能吃的 PropertyValue：转成中性值后交给 ConfigValues.Check——与脚本 setProperty 同一份口径
    //（叶子按约束校验、复合字段整体写入并递归校验）。判据【只看字段自己的 config】；调用方须给出按写入后的值求出的 config。
    public static (PropertyValue Value, string? Error) Normalize(IControllerConfig config, JsonElement raw, string key)
        => ConfigValues.Check(config, ConfigText.ToPropertyValue(JsonNode.Parse(raw.GetRawText())), key);
}
