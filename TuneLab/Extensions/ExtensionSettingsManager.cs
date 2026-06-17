using System;
using System.Collections.Generic;
using TuneLab.Extensions.Effect;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions;

// 跨能力类别聚合「声明了扩展级设置（IExtensionSettings）的 extension」，供设置窗口枚举渲染，并把持久值回喂。
// agent 自有侧边栏设置与 AgentSettingsStore，不在此列。
internal static class ExtensionSettingsManager
{
    // 一个可配置 extension：kind 用于持久化键与展示分组；extensionId 是不可变身份（engine 类即其 engine id）；
    // settings 即其设置接口。
    public readonly record struct Entry(string Kind, string ExtensionId, string DisplayName, IExtensionSettings Settings)
    {
        // 持久化键（与 SecretStore account 前缀一致）："kind:extensionId"。
        public string Key => Kind + ":" + ExtensionId;
    }

    // 枚举所有声明了设置的 extension。顺序：effect 在前、voice 在后，各按注册序。
    public static IReadOnlyList<Entry> GetEntries()
    {
        var entries = new List<Entry>();
        Collect(entries, "effect", EffectManager.GetAllEffectEngines(), EffectManager.GetExtensionSettings, EffectManager.GetDisplayName);
        Collect(entries, "voice", VoicesManager.GetAllVoiceEngines(), VoicesManager.GetExtensionSettings, VoicesManager.GetDisplayName);
        return entries;
    }

    static void Collect(List<Entry> entries, string kind, IReadOnlyList<string> ids,
        Func<string, IExtensionSettings?> getSettings, Func<string, string> getDisplayName)
    {
        foreach (var id in ids)
        {
            var settings = getSettings(id);
            if (settings != null)
                entries.Add(new Entry(kind, id, getDisplayName(id), settings));
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
            entry.Settings.ApplySettings(ToPropertyObject(Load(entry)));
        }
        catch (Exception ex)
        {
            Log.Error(string.Format("Extension {0} ApplySettings failed: {1}", entry.Key, ex));
        }
    }

    // 读某 extension 已落盘的设置（密钥已解密）。两遍：先原生读出值供 schema 求值得 IsPassword 集（密钥是否为字段
    // 不依赖其自身值），再据此读取并解密密钥——这样存储层不必存"是否密钥"标记，纯净原生 JSON。
    public static Dictionary<string, PropertyValue> Load(Entry entry)
    {
        var raw = ExtensionSettingsStore.Load(entry.Key, sNoSecrets);
        var secrets = PasswordKeys(entry.Settings.GetSettingsConfig(new Context(ToPropertyObject(raw))));
        return secrets.Count == 0 ? raw : ExtensionSettingsStore.Load(entry.Key, secrets);
    }

    // schema 里标了 IsPassword 的字段键（=须加密/解密的密钥字段）。
    public static HashSet<string> PasswordKeys(ObjectConfig config)
    {
        var set = new HashSet<string>();
        foreach (var kv in config.Properties)
            if (kv.Value is TextBoxConfig tb && tb.IsPassword)
                set.Add(kv.Key);
        return set;
    }

    static readonly IReadOnlySet<string> sNoSecrets = new HashSet<string>();

    static PropertyObject ToPropertyObject(Dictionary<string, PropertyValue> values)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var kv in values)
            map.Add(kv.Key, kv.Value);
        return new PropertyObject(map);
    }

    // IExtensionSettings.GetSettingsConfig 求值上下文（仅用于本管理器内部据当前值算 IsPassword 集）。
    sealed class Context(PropertyObject settings) : IExtensionSettingsContext
    {
        public PropertyObject Settings => settings;
    }
}
