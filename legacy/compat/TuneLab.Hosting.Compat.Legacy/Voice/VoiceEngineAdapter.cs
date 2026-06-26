using System;
using System.Collections.Generic;
using TuneLab.Hosting.Compat.Legacy.Conversion;
using LVoice = TuneLab.Extensions.Voices;
using VVoice = TuneLab.SDK;
using PStruct = TuneLab.Foundation;

namespace TuneLab.Hosting.Compat.Legacy.Voice;

// 把老 IVoiceEngine 适配成 V1 IVoiceSynthesisEngine。
// Init：V1 面无参（插件自定位），老引擎需要包目录——适配器构造时持有 enginePath、Init 转发，失败抛异常。
// VoiceSourceInfos 每次重建（引擎在 Init 后才填充，避免缓存到空）。
// CreateSession：老声源（每 part 一份、与会话一一对应）经 LegacySessionAdapter 包成 V1 会话。
internal sealed class VoiceEngineAdapter(LVoice.IVoiceEngine legacy, string enginePath) : VVoice.IVoiceSynthesisEngine
{
    public PStruct.IReadOnlyOrderedMap<string, VVoice.VoiceSourceInfo> VoiceSourceInfos
    {
        get
        {
            var map = new PStruct.OrderedMap<string, VVoice.VoiceSourceInfo>();
            foreach (var kv in legacy.VoiceInfos)
                map.Add(kv.Key, new VVoice.VoiceSourceInfo() { Name = kv.Value.Name, Description = kv.Value.Description });
            return map;
        }
    }

    public void Init()
    {
        if (!legacy.Init(enginePath, out var error))
            throw new InvalidOperationException(error ?? "Legacy voice engine init failed.");
    }

    public void Destroy() => legacy.Destroy();

    public VVoice.IVoiceSynthesisSession CreateSession(VVoice.IVoiceSynthesisContext context)
    {
        return new LegacySessionAdapter(legacy.CreateVoiceSource(context.VoiceId), context);
    }

    // —— 声明（V1 要求引擎层、不依赖会话）——
    // 老声源声明是静态的，但取值需一个声源实例：按 voiceId 懒建一份「声明用」声源并缓存其转换后的 config
    //（与 CreateSession 的会话用声源相互独立）。老模型无条件轨/无回显：轨/面板恒定、回显为空，忽略 context 值。
    // voiceId 由声明壳内各 part 的 SourceId 承载（V1 单 part 调用）。
    public PStruct.IReadOnlyOrderedMap<VVoice.PropertyKey, VVoice.AutomationConfig> GetAutomationConfigs(VVoice.IVoiceSynthesisPartPropertyContext context)
        => Decl(SourceIdOf(context)).Automation;
    public PStruct.IReadOnlyOrderedMap<VVoice.PropertyKey, VVoice.AutomationConfig> GetSynthesizedParameterConfigs(VVoice.IVoiceSynthesisPartPropertyContext context)
        => sEmptyConfigs;
    public VVoice.ObjectConfig GetPartPropertyConfig(VVoice.IVoiceSynthesisPartPropertyContext context)
        => new() { Properties = Decl(SourceIdOf(context)).PartProperties };
    public VVoice.ObjectConfig GetNotePropertyConfig(VVoice.IVoiceSynthesisNotePropertyContext context)
        => new() { Properties = Decl(context.Part.VoiceId).NoteProperties };

    static string SourceIdOf(VVoice.IVoiceSynthesisPartPropertyContext context) => context.Parts.Count > 0 ? context.Parts[0].VoiceId : string.Empty;

    Declarations Decl(string voiceId)
    {
        if (!mDeclarations.TryGetValue(voiceId, out var decl))
        {
            var source = legacy.CreateVoiceSource(voiceId);
            decl = new Declarations(
                source.AutomationConfigs.ToV1AutomationMap(),
                source.PartProperties.ToV1ConfigMap(),
                source.NoteProperties.ToV1ConfigMap());
            mDeclarations[voiceId] = decl;
        }
        return decl;
    }

    sealed record Declarations(
        PStruct.IReadOnlyOrderedMap<VVoice.PropertyKey, VVoice.AutomationConfig> Automation,
        PStruct.IReadOnlyOrderedMap<VVoice.PropertyKey, VVoice.IControllerConfig> PartProperties,
        PStruct.IReadOnlyOrderedMap<VVoice.PropertyKey, VVoice.IControllerConfig> NoteProperties);

    readonly Dictionary<string, Declarations> mDeclarations = new();
    static readonly PStruct.OrderedMap<VVoice.PropertyKey, VVoice.AutomationConfig> sEmptyConfigs = new();
}
