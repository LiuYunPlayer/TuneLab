using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using TuneLab.Foundation;

namespace TuneLab.Configs;

// 宿主设置：值 holder + 元数据 + 磁盘读写全部声明在 SettingsRegistry（单一真源）。本类只做两件事：
//  ① 对外暴露既有的 Settings.Xxx 访问器（委托到注册表条目的 NotifiableProperty，消费者零改动）；
//  ② Init/Save 遍历注册表读写 settings.json（键/类型/顺序与旧格式一致，逐字段兼容）。
// ExtensionRouting 是非通知型扁平映射（改后须重启，走 ExtensionRouting 模块），随本文件读写、不入注册表。
internal static class Settings
{
    // 默认值来源（单一默认源；阶段②重写设置窗后可并入注册表）。
    public static readonly SettingsFile DefaultSettings = new();

    public static NotifiableProperty<string> Language => SettingsRegistry.Language.Property;
    public static NotifiableProperty<string> InterfaceFontFamily => SettingsRegistry.InterfaceFontFamily.Property;
    public static NotifiableProperty<string> AutoScrollTarget => SettingsRegistry.AutoScrollTarget.Property;
    public static NotifiableProperty<double> MasterGain => SettingsRegistry.MasterGain.Property;
    public static NotifiableProperty<string> BackgroundImagePath => SettingsRegistry.BackgroundImagePath.Property;
    public static NotifiableProperty<double> BackgroundImageOpacity => SettingsRegistry.BackgroundImageOpacity.Property;
    public static NotifiableProperty<double> ParameterBoundaryExtension => SettingsRegistry.ParameterBoundaryExtension.Property;
    public static NotifiableProperty<bool> ParameterSyncMode => SettingsRegistry.ParameterSyncMode.Property;
    public static NotifiableProperty<string> PianoKeySamplesPath => SettingsRegistry.PianoKeySamplesPath.Property;
    public static NotifiableProperty<int> AutoSaveInterval => SettingsRegistry.AutoSaveInterval.Property;
    public static NotifiableProperty<int> AutoSaveMaxCount => SettingsRegistry.AutoSaveMaxCount.Property;
    public static NotifiableProperty<int> BufferSize => SettingsRegistry.BufferSize.Property;
    public static NotifiableProperty<int> MaxParallelSynthesisTasks => SettingsRegistry.MaxParallelSynthesisTasks.Property;
    public static NotifiableProperty<int> SampleRate => SettingsRegistry.SampleRate.Property;
    public static NotifiableProperty<string> AudioDriver => SettingsRegistry.AudioDriver.Property;
    public static NotifiableProperty<string> AudioDevice => SettingsRegistry.AudioDevice.Property;
    public static NotifiableProperty<double> TrackHueChangeRate => SettingsRegistry.TrackHueChangeRate.Property;
    public static NotifiableProperty<string> AgentModelProvider => SettingsRegistry.AgentModelProvider.Property;
    public static NotifiableProperty<string> AgentAuthorization => SettingsRegistry.AgentAuthorization.Property;
    public static NotifiableProperty<int> AgentMaxToolResultChars => SettingsRegistry.AgentMaxToolResultChars.Property;
    // 扩展冲突消解的用户选择（routeKey → packageId）；非通知型（改后须重启生效，与切语言一致），存取经 ExtensionRouting。
    public static Dictionary<string, string> ExtensionRouting { get; private set; } = new();

    public static void Init(string path)
    {
        JsonObject? json = null;
        if (File.Exists(path))
        {
            try
            {
                json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            }
            catch (Exception ex)
            {
                Log.Error("Failed to deserialize settings: " + ex);
            }
        }
        json ??= new JsonObject();

        foreach (var item in SettingsRegistry.All)
            item.Load(json);

        ExtensionRouting = ReadRouting(json);
    }

    public static void Save(string path)
    {
        try
        {
            var json = new JsonObject();
            foreach (var item in SettingsRegistry.All)
                item.Save(json);

            var routing = new JsonObject();
            foreach (var kv in ExtensionRouting)
                routing[kv.Key] = JsonValue.Create(kv.Value);
            json["ExtensionRouting"] = routing;

            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(path, json.ToJsonString(JsonSerializerOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings: " + ex);
        }
    }

    static Dictionary<string, string> ReadRouting(JsonObject json)
    {
        var result = new Dictionary<string, string>();
        if (json.TryGetPropertyValue("ExtensionRouting", out var node) && node is JsonObject obj)
        {
            foreach (var kv in obj)
            {
                try
                {
                    if (kv.Value is not null)
                        result[kv.Key] = kv.Value.GetValue<string>();
                }
                catch { }
            }
        }
        return result;
    }

    static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
}
