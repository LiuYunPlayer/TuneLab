using System;
using System.Collections.Generic;
using TuneLab.Extensions.Effect;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions;

// 跨能力类别聚合「声明了扩展级设置（IExtensionSettings）的 extension」，供设置窗口枚举渲染，并把持久值回喂。
// agent 模型 provider 不在此列：它复用存储层 ExtensionSettingsStore，但有自己的侧边栏 UI、不进设置窗口扩展页。
internal static class ExtensionSettingsManager
{
    // 一个可配置 extension：packageId 是来源插件包 id（设置按包分桶用，内建为空）；kind 用于桶内键与展示分组；
    // extensionId 是不可变身份（engine 类即其 engine id）；settings 即其设置接口。
    public readonly record struct Entry(string PackageId, string Kind, string ExtensionId, string DisplayName, IExtensionSettings Settings)
    {
        // 包内桶键（与 SecretStore account 中段一致）："kind:extensionId"；外层再按 PackageId 分桶。
        public string ExtensionKey => Kind + ":" + ExtensionId;
    }

    // 枚举所有声明了设置的 extension。顺序：effect 在前、voice 在后，各按注册序。
    // 【按包枚举】同一身份 id 跨包可有多个实现（冲突消解后均加载），各包实现各有独立设置桶——逐 (packageId, id) 对收集，
    // 而非只取活实现：设置页要让用户为每个已装实现配置（即便它当前不是该身份的活实现）。
    public static IReadOnlyList<Entry> GetEntries()
    {
        var entries = new List<Entry>();
        Collect(entries, "effect", EffectManager.GetAllEffectEngines(), EffectManager.GetProviders, EffectManager.GetExtensionSettings);
        Collect(entries, "voice", VoicesManager.GetAllVoiceEngines(), VoicesManager.GetProviders, VoicesManager.GetExtensionSettings);
        return entries;
    }

    static void Collect(List<Entry> entries, string kind, IReadOnlyList<string> ids,
        Func<string, IReadOnlyList<(string PackageId, string DisplayName)>> getProviders,
        Func<string, string, IExtensionSettings?> getSettings)
    {
        foreach (var id in ids)
        {
            foreach (var (packageId, displayName) in getProviders(id))
            {
                var settings = getSettings(packageId, id);
                if (settings != null)
                    entries.Add(new Entry(packageId, kind, id, displayName, settings));
            }
        }
    }

    // 加载完成后调用一次：把每个 extension 已落盘的设置回喂给它（早于任何 Init/会话）。
    public static void ApplyPersisted()
    {
        foreach (var entry in GetEntries())
            ApplyOne(entry);
    }

    // 把某 extension 已落盘的设置回喂给它（加载后、以及用户保存后复用）。实现者抛异常不波及宿主。
    public static void ApplyOne(Entry entry)
    {
        try
        {
            entry.Settings.ApplySettings(ExtensionSettingsStore.ToPropertyObject(Load(entry)));
        }
        catch (Exception ex)
        {
            Log.Error(string.Format("Extension {0}/{1} ApplySettings failed: {2}", entry.PackageId, entry.ExtensionKey, ex));
        }
    }

    // 读某 extension 已落盘的设置（密钥已解密）。两遍读由存储层按 schema(IsPassword) 处理。
    public static Dictionary<string, PropertyValue> Load(Entry entry)
        => ExtensionSettingsStore.Load(entry.PackageId, entry.ExtensionKey, s => entry.Settings.GetSettingsConfig(new Context(s)));

    // IExtensionSettings.GetSettingsConfig 求值上下文（据当前值算 config / IsPassword 集）。
    sealed class Context(PropertyObject settings) : IExtensionSettingsContext
    {
        public PropertyObject Settings => settings;
    }
}
