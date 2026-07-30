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
// 【边界】本文件只承「宿主固定的设置集合」——同一份配置发给任何用户都成立。凡与**用户环境**绑定的
// （装了哪些扩展、用过哪些音源、往参数面板钉了什么）都不在此列：那是使用痕迹，各自独立 JSON
// （ExtensionRouting.json / ExtensionActivation.json / ExtensionSettings.json / ParameterPins.json /
//  RecentSoundSources.json）。判据是"这份数据换台机器还成不成立"，不是"它有没有设置窗 UI"。
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
    public static NotifiableProperty<bool> AutoGeneratePronunciation => SettingsRegistry.AutoGeneratePronunciation.Property;
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
    }

    public static void Save(string path)
    {
        try
        {
            var json = new JsonObject();
            foreach (var item in SettingsRegistry.All)
                item.Save(json);

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

    static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
}
