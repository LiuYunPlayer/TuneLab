using System;
using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Settings;

// 扩展设置（IExtensionSettings）演示夹具：一个 voice 引擎，仅用于验证「扩展级设置」全链路——
//   ① 宿主在 设置 > 扩展 渲染 GetSettingsConfig() 出的 schema；
//   ② 保存时普通字段明文落盘、IsPassword 字段加密落盘（Win=DPAPI 内联 / mac+linux=OS 凭据库）；
//   ③ 重启后宿主经 ApplySettings 把持久值回喂（此处打日志为可观测点）。
// 不参与实际合成（CreateSession 抛异常）——设置链路全程不触发 Init/合成。
// DisplayText 由插件自译（演示自带极简词典，未收录词回退原文）。

static class L
{
    static readonly Dictionary<string, Dictionary<string, string>> Dict = new()
    {
        ["zh-CN"] = new()
        {
            ["Model Path"] = "模型路径",
            ["API Key"] = "API 密钥",
            ["Use GPU"] = "使用 GPU",
            ["GPU Device"] = "GPU 设备",
        },
    };

    public static string Tr(string en)
    {
        var lang = TuneLabContext.Global.Language;
        return Dict.TryGetValue(lang, out var m) && m.TryGetValue(en, out var v) ? v : en;
    }
}

public sealed class SettingsVoiceEngine : IVoiceEngine, IExtensionSettings
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => sEmpty;

    public void Init() { }
    public void Destroy() { }

    // 仅设置演示，不参与实际合成。
    public ISynthesisSession CreateSession(string voiceId, ISynthesisContext context)
        => throw new NotSupportedException("V1.Settings is a settings-only demo fixture.");

    // 声明面（全空）：本夹具不暴露轨/面板，仅验证扩展设置链路。
    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IPartPropertyContext context) => sEmptyAutomations;
    public IReadOnlyOrderedMap<string, AutomationConfig> GetSynthesizedParameterConfigs(IPartPropertyContext context) => sEmptyAutomations;
    public ObjectConfig GetPartPropertyConfig(IPartPropertyContext context) => sEmptyConfig;
    public ObjectConfig GetNotePropertyConfig(INotePropertyContext context) => sEmptyConfig;

    // 设置 schema：普通文本（模型路径）+ 密钥（API Key：掩码显示 + 加密落盘）+ 一个开关。
    // 动态演示：勾选「使用 GPU」后才暴露「GPU 设备」字段（据 context 当前值条件显隐）。
    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
    {
        var props = new OrderedMap<string, IControllerConfig>();
        props.Add("model_path", new TextBoxConfig { DisplayText = L.Tr("Model Path"), DefaultValue = string.Empty });
        props.Add("api_key", new TextBoxConfig { DisplayText = L.Tr("API Key"), DefaultValue = string.Empty, IsPassword = true });
        props.Add("use_gpu", new CheckBoxConfig { DisplayText = L.Tr("Use GPU"), DefaultValue = false });
        if (context.Settings.GetBool("use_gpu", false))
            props.Add("gpu_device", new TextBoxConfig { DisplayText = L.Tr("GPU Device"), DefaultValue = string.Empty });
        return new ObjectConfig { Properties = props };
    }

    // 回喂可观测点：把收到的设置打日志，供测试核对"重启后引擎确实收到了持久化的值"。密钥只记录是否非空、不打印明文。
    public void ApplySettings(PropertyObject settings)
    {
        var path = settings.GetString("model_path", string.Empty);
        var hasKey = !string.IsNullOrEmpty(settings.GetString("api_key", string.Empty));
        var gpu = settings.GetBool("use_gpu", false);
        TuneLabContext.Global.GetLogger().Info(string.Format(
            "[V1.Settings] ApplySettings: model_path='{0}', api_key={1}, use_gpu={2}",
            path, hasKey ? "<set>" : "<empty>", gpu));
    }

    static readonly OrderedMap<string, VoiceSourceInfo> sEmpty = new();
    static readonly OrderedMap<string, AutomationConfig> sEmptyAutomations = new();
    static readonly ObjectConfig sEmptyConfig = new() { Properties = new OrderedMap<string, IControllerConfig>() };
}
