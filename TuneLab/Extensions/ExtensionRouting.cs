using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Configs;
using TuneLab.Extensions.Agent;
using TuneLab.Extensions.Effect;
using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Voices;
using TuneLab.Extensions.Instruments;
using TuneLab.Utils;

namespace TuneLab.Extensions;

// 扩展冲突消解：身份解析策略 + 用户选择存取 + 「Extension Routing」矩阵的冲突聚合，三合一。
// 背景：扩展身份 id（voice/effect/agent 引擎 id、format 扩展名）【跨包可重名】，非全局唯一键——
//   不同安装包可实现相同类型 + 相同 id，真实键为「包 id + 身份 id」联合。冲突包均加载（不再先到丢弃），
//   由用户在设置窗口「Extension Routing」矩阵显式选用哪个包；本类承载该选择的解析与「未选时」的确定性默认。
// 【存法】选择是「身份→包」的扁平小映射、无密钥/无复杂结构，与 AgentModelProvider 同属「用户选择」类，
//   故【直接存进 app Settings.json】的 ExtensionRouting 字典（routeKey="kind:identity" → packageId），不另开配置文件。
// 【工程不存包 id】：工程序列化只引身份 id，加载时按本类的全局选择解析到具体包——保持 id 为唯一契约、工程跨机可移植。
internal static class ExtensionRouting
{
    // routeKey = "kind:identity"（与矩阵的一行对应；format 拆 "format-import" / "format-export" 两条可路由身份）。
    public static string RouteKey(string kind, string identity) => kind + ":" + identity;

    // 用户为某身份选中的包 id；从未选过返回 null（调用方走确定性默认）。
    public static string? GetSelected(string routeKey)
        => Settings.ExtensionRouting.TryGetValue(routeKey, out var packageId) ? packageId : null;

    // 写入/清除某身份的选择（packageId 为 null/空 ⇒ 清除该条，回到默认），即时落盘（低频小数据）。
    public static void SetSelected(string routeKey, string? packageId)
    {
        if (string.IsNullOrEmpty(packageId))
            Settings.ExtensionRouting.Remove(routeKey);
        else
            Settings.ExtensionRouting[routeKey] = packageId;
        Settings.Save(PathManager.SettingsFilePath);
    }

    // 在某身份的所有提供者中解析出【活实现】。providers 内 packageId 互异（同包同 id 已在注册层去重）。
    // 顺序：① 用户选中且该包确在提供者中 → 用它；② 否则确定性默认：内建(built-in)优先；③ 再否则 packageId ordinal 最小。
    // 空集合返回 default(T)（调用方按降级处理）。
    public static T? ResolveActive<T>(string routeKey, IReadOnlyList<T> providers, Func<T, string> packageIdOf)
    {
        if (providers == null || providers.Count == 0)
            return default;

        var selected = GetSelected(routeKey);
        if (!string.IsNullOrEmpty(selected))
        {
            foreach (var p in providers)
                if (packageIdOf(p) == selected)
                    return p;
        }

        // 默认：内建优先，避免插件悄悄顶替 tlp 等内置身份。
        foreach (var p in providers)
            if (packageIdOf(p) == ExtensionManager.BuiltInPackageId)
                return p;

        // 再否则 packageId 序最小（确定性，不依赖目录枚举/加载顺序）。
        return providers.OrderBy(packageIdOf, StringComparer.Ordinal).First();
    }

    // 同上策略但只回活实现的 packageId（供矩阵显示当前生效项）；空集合返回 null。
    public static string? ResolveActivePackageId(string routeKey, IReadOnlyList<string> packageIds)
        => ResolveActive(routeKey, packageIds, p => p);

    // ── 「Extension Routing」矩阵数据源：只列有冲突(>1 提供者)的身份；单提供者无可选、不入矩阵 ──

    // 一个可路由身份的一个候选包。
    public readonly record struct RouteOption(string PackageId, string DisplayName);

    // 一行冲突身份：kind+identity 定位（routeKey 用于读写选择），options 是各包候选，activePackageId 是当前生效项。
    public readonly record struct RouteRow(string Kind, string Identity, string RouteKey, IReadOnlyList<RouteOption> Options, string ActivePackageId);

    // 全部冲突行（按 kind 顺序：voice / effect / agent-model / format-import / format-export，各按身份注册序）。
    public static IReadOnlyList<RouteRow> GetConflicts()
    {
        var rows = new List<RouteRow>();
        Collect(rows, "voice", VoicesManager.GetAllVoiceEngines(), VoicesManager.GetProviders);
        Collect(rows, "instrument", InstrumentsManager.GetAllInstrumentEngines(), InstrumentsManager.GetProviders);
        Collect(rows, "effect", EffectManager.GetAllEffectEngines(), EffectManager.GetProviders);
        Collect(rows, "agent-model", AgentModelManager.GetAllAgentModelEngines(), AgentModelManager.GetProviders);
        Collect(rows, "format-import", FormatsManager.GetAllImportFormats(), FormatsManager.GetImportProviders);
        Collect(rows, "format-export", FormatsManager.GetAllExportFormats(), FormatsManager.GetExportProviders);
        return rows;
    }

    static void Collect(List<RouteRow> rows, string kind, IReadOnlyList<string> identities,
        Func<string, IReadOnlyList<(string PackageId, string DisplayName)>> getProviders)
    {
        foreach (var identity in identities)
        {
            var providers = getProviders(identity);
            if (providers.Count <= 1)
                continue;   // 无冲突：不入矩阵

            var routeKey = RouteKey(kind, identity);
            var options = providers.Select(p => new RouteOption(p.PackageId, p.DisplayName)).ToArray();
            var active = ResolveActivePackageId(routeKey, options.Select(o => o.PackageId).ToArray()) ?? string.Empty;
            rows.Add(new RouteRow(kind, identity, routeKey, options, active));
        }
    }
}
