using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TuneLab.Foundation;

namespace TuneLab.Extensions;

// 扩展启停：把某个安装包、或包内某个条目「关掉但不卸载」的用户开关。
//
// 【与冲突消解(ExtensionRouting)是两根轴】routing 回答"同一身份有多个实现时该用谁"，启停回答"这份实现
//   要不要参与加载"。后者对没有任何竞争者的独苗同样成立——插件老报错、或加载慢又不常用，都不该被迫卸载；
//   而 routing 对独苗无话可说（它只列冲突行）。
//
// 【两级粒度，各有其不可替代处】
//   包级 —— 覆盖 legacy 包与 manifest 坏包的**唯一**粒度（它们没有 manifest 条目可禁），也是"关掉这个插件"
//            的直觉入口。整包禁用时连程序集都不加载，是真能省启动时间的那一档。
//   条目级 —— 一包多能力时只关坏的那个（如一个包同时提供 voice 与 effect，effect 崩了但 voice 还想用）。
//            程序集是条目级声明的，故禁掉部分条目只跳过注册、不省加载；只有全禁（或包级禁）才跳过程序集。
//
// 【禁用发生在注册之前】被禁的条目根本不进各 manager，于是 routing 矩阵、扩展设置页、音源选择器等
//   一切"从注册表来"的视图自然看不到它——无需在每个下游各写一遍过滤。
//
// 【生效时机】改动即时落盘，但**要重启才生效**（与 routing 一致）：已注册的能力撤不回、已加载的程序集
//   卸不掉，谎称"已生效"比要求用户重启更糟。UI 因此要能区分「存的状态」与「本次运行的状态」。
//
// 【为什么是独立 JSON 而不是 Settings.json】Settings 只承**用户可调项**，且可调项都在设置窗有一行 UI；
//   启停的 UI 全在扩展侧栏（卡片 + 详情窗），设置窗里一格都没有——它属"交互内的宿主记忆"，与
//   RecentSoundSourceManager / ParameterPinning 同类，故自带独立 JSON（Configs/ExtensionActivation.json）。
//   （routing 待在 Settings.json 里并不矛盾：它有设置窗「Extension Routing」那一页。判据是 UI 落在哪，
//   不是"同为用户选择"。）顺带也省得拨一下开关就重写整个 Settings.json。
//
// 【存储形状】packageId → 该包下被禁的 entryKey 列表；PackageWildcard("*") 表示整包。
//   packageId 必须是外层键：同一身份 id 跨包可并存（冲突消解的前提），禁的从来是"某个包的那一份"，
//   不是那个身份本身。按包分组还让"包已不在就清掉"退化成删一个键（见 PruneUnknown）。
//
// 【多身份条目（多后缀 format）】禁用时把它的每个身份都各记一条，判定时**任一命中即算禁用**。
//   于是作者日后增删/重排后缀都不会让用户的禁用悄悄失效——失效方向是"被关掉的坏插件自己回来了"，
//   这是不能接受的一侧；反过来残留一条指向已删后缀的死记录只是无害垃圾。
internal static class ExtensionActivation
{
    // 整包禁用的 entryKey（真实 entryKey 恒为 "kind:identity"，冒号形态与它天然不撞）。
    public const string PackageWildcard = "*";

    // 条目键：与 ExtensionSettingsManager.Entry.ExtensionKey、ExtensionRouting 的 routeKey 同一构型。
    public static string EntryKey(string kind, string identity) => kind + ":" + identity;

    // 启动时载入一次（早于 ExtensionManager.LoadExtensions——加载期就要查这份表）。
    public static void Init(string path)
    {
        Dictionary<string, List<string>>? disabled = null;
        if (File.Exists(path))
        {
            try
            {
                using var stream = File.OpenRead(path);
                disabled = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(stream);
            }
            catch (Exception ex)
            {
                // 读不出来就当"什么都没禁"——宁可多加载几个插件，也不能让一份坏文件把用户挡在门外。
                Log.Error("Failed to deserialize extension activation: " + ex);
            }
        }
        mDisabled = disabled ?? new();
        mPath = path;
    }

    // ── 查询 ──

    public static bool IsPackageDisabled(string? packageId)
        => Contains(packageId, PackageWildcard);

    // 条目【自身】是否被禁（不含包级）。UI 要靠它区分"这个条目被关了"与"整包被关了"——后者下面
    // 条目开关无从操作，得如实说明原因，而不是显示成条目自己被关。
    public static bool IsEntryDisabledSelf(string? packageId, string kind, IReadOnlyList<string> identities)
    {
        if (string.IsNullOrEmpty(kind))
            return false;
        foreach (var identity in identities)
        {
            if (!string.IsNullOrEmpty(identity) && Contains(packageId, EntryKey(kind, identity)))
                return true;   // 任一身份命中即算（见头注释）
        }
        return false;
    }

    // 条目最终是否不该加载：整包被禁 ⇒ 包内一切都不加载；否则看条目自身。
    public static bool IsEntryDisabled(string? packageId, string kind, IReadOnlyList<string> identities)
        => IsPackageDisabled(packageId) || IsEntryDisabledSelf(packageId, kind, identities);

    // 无身份的条目（资源类）无法成键，故不可单独禁用——只能随整包关。UI 据此隐藏其开关，
    // 免得给一个点了没有任何效果的控件。
    public static bool CanDisableEntry(string? packageId, string kind, IReadOnlyList<string> identities)
        => !string.IsNullOrEmpty(packageId) && !string.IsNullOrEmpty(kind) && identities.Count > 0;

    // ── 写入（即时落盘，低频小数据；重启后生效）──

    public static void SetPackageEnabled(string? packageId, bool enabled)
    {
        if (string.IsNullOrEmpty(packageId))
            return;
        if (Apply(packageId!, PackageWildcard, enabled))
            Save();
    }

    // 禁用记下该条目的**全部**身份、启用则把它们全部移除（见头注释的任一命中规则）。
    public static void SetEntryEnabled(string? packageId, string kind, IReadOnlyList<string> identities, bool enabled)
    {
        if (!CanDisableEntry(packageId, kind, identities))
            return;

        bool changed = false;
        foreach (var identity in identities)
        {
            if (!string.IsNullOrEmpty(identity))
                changed |= Apply(packageId!, EntryKey(kind, identity), enabled);
        }
        if (changed)
            Save();
    }

    // 清掉指向【已不在的包】的记录。卸载（乃至用户手工删目录）不会回来收拾自己的禁用记录，
    // 留着就会埋雷：日后重装同一个包会**静默地**装完就是关的，用户找不到原因。
    // 语义即"包没了，对它的选择也就没了；再装回来算新装、默认启用"。
    public static void PruneUnknown(IEnumerable<string?> knownPackageIds)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in knownPackageIds)
            if (!string.IsNullOrEmpty(id))
                known.Add(id!);

        var stale = new List<string>();
        foreach (var packageId in mDisabled.Keys)
            if (!known.Contains(packageId))
                stale.Add(packageId);

        if (stale.Count == 0)
            return;   // 常态：不为一次空操作写盘

        foreach (var packageId in stale)
            mDisabled.Remove(packageId);
        Save();
    }

    static bool Contains(string? packageId, string entryKey)
        => !string.IsNullOrEmpty(packageId)
        && mDisabled.TryGetValue(packageId!, out var entries)
        && entries.Contains(entryKey);

    // 落地一处启停；真的改了才回 true（调用方据此决定要不要写盘）。空包不留空数组。
    static bool Apply(string packageId, string entryKey, bool enabled)
    {
        if (enabled)
        {
            if (!mDisabled.TryGetValue(packageId, out var entries) || !entries.Remove(entryKey))
                return false;
            if (entries.Count == 0)
                mDisabled.Remove(packageId);
            return true;
        }

        if (!mDisabled.TryGetValue(packageId, out var list))
            mDisabled[packageId] = list = [];
        if (list.Contains(entryKey))
            return false;
        list.Add(entryKey);
        return true;
    }

    static void Save()
    {
        try
        {
            var folder = Path.GetDirectoryName(mPath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            File.WriteAllText(mPath, JsonSerializer.Serialize(mDisabled, JsonSerializerOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save extension activation: " + ex);
        }
    }

    static Dictionary<string, List<string>> mDisabled = new();
    static string mPath = string.Empty;
    static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
}
