using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TuneLab.Extensions.Effect;
using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Voices;
using TuneLab.Extensions.Instruments;
using TuneLab.Foundation;

namespace TuneLab.Extensions;

// 扩展冲突消解：身份解析策略 + 用户选择存取 + 「Extension Routing」矩阵的冲突聚合，三合一。
// 背景：扩展身份 id（voice/effect/agent 引擎 id、format 扩展名）【跨包可重名】，非全局唯一键——
//   不同安装包可实现相同类型 + 相同 id，真实键为「包 id + 身份 id」联合。冲突包均加载（不再先到丢弃），
//   由用户在设置窗口「Extension Routing」矩阵显式选用哪个包；本类承载该选择的解析与「未选时」的确定性默认。
// 【存法】独立 JSON（Configs/ExtensionRouting.json），routeKey="kind:identity" → packageId 的扁平映射。
//   **不进 Settings.json**：Settings 承的是「宿主固定的设置集合」——同一份发给任何用户都成立；而本选择的
//   键与值都是**这台机器上装了哪些包**的函数（换台机器整份都无意义），属用户使用留下的痕迹，与
//   ParameterPinning / RecentSoundSourceManager / ExtensionActivation 同类。
//   （"它在设置窗有一页"不构成留在 Settings 的理由：判据是这份数据是否与用户环境绑定，不是它有没有 UI。）
// 【工程不存包 id】：工程序列化只引身份 id，加载时按本类的全局选择解析到具体包——保持 id 为唯一契约、工程跨机可移植。
internal static class ExtensionRouting
{
    // 启动时载入一次（早于 ExtensionManager.LoadExtensions——注册期就要按它解析活实现）。
    public static void Init(string path)
    {
        Dictionary<string, string>? routing = null;
        if (File.Exists(path))
        {
            try
            {
                using var stream = File.OpenRead(path);
                routing = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            }
            catch (Exception ex)
            {
                // 读不出来就当"没做过任何选择"：全部身份回落确定性默认（内建优先），而不是让一份坏文件挡住加载。
                Log.Error("Failed to deserialize extension routing: " + ex);
            }
        }
        mRouting = routing ?? new();
        mPath = path;
    }

    // routeKey = "kind:identity"（与矩阵的一行对应；format 拆 "format-import" / "format-export" 两条可路由身份）。
    public static string RouteKey(string kind, string identity) => kind + ":" + identity;

    // 用户为某身份选中的包 id；从未选过返回 null（调用方走确定性默认）。
    public static string? GetSelected(string routeKey)
        => mRouting.TryGetValue(routeKey, out var packageId) ? packageId : null;

    // 写入/清除某身份的选择（packageId 为 null/空 ⇒ 清除该条，回到默认），即时落盘（低频小数据）。
    public static void SetSelected(string routeKey, string? packageId)
    {
        if (string.IsNullOrEmpty(packageId))
            mRouting.Remove(routeKey);
        else
            mRouting[routeKey] = packageId;
        Save();
    }

    static void Save()
    {
        try
        {
            var folder = Path.GetDirectoryName(mPath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            File.WriteAllText(mPath, JsonSerializer.Serialize(mRouting, JsonSerializerOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save extension routing: " + ex);
        }
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

    // 全部冲突行（按 kind 顺序：voice / instrument / effect / format-import / format-export，各按身份注册序）。
    public static IReadOnlyList<RouteRow> GetConflicts()
    {
        var rows = new List<RouteRow>();
        Collect(rows, "voice", VoicesManager.GetAllVoiceEngines(), VoicesManager.GetProviders);
        Collect(rows, "instrument", InstrumentsManager.GetAllInstrumentEngines(), InstrumentsManager.GetProviders);
        Collect(rows, "effect", EffectManager.GetAllEffectEngines(), EffectManager.GetProviders);
        // 【agent-model 不在此列】冲突消解存在的理由是"多个互不知情的第三方包实现了同一身份"，
        // 而模型适配器不开放为插件类型、全部编进宿主：同一份源码里两个适配器撞同一个 type id 是宿主
        // 自己的编码错误，该在注册处报出来，不该摆到用户面前让他"选一个"。
        // （voice/instrument/effect/format 的内建实现仍参与——它们会与第三方包撞身份，那才需要裁决。）
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

    static Dictionary<string, string> mRouting = new();
    static string mPath = string.Empty;
    static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
}
