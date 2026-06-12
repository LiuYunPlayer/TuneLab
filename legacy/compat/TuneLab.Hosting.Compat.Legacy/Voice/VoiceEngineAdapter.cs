using System;
using LVoice = TuneLab.Extensions.Voices;
using VVoice = TuneLab.SDK;
using PStruct = TuneLab.Primitives.DataStructures;

namespace TuneLab.Hosting.Compat.Legacy.Voice;

// 把老 IVoiceEngine 适配成 V1 IVoiceEngine。
// Init：V1 面无参（插件自定位），老引擎需要包目录——适配器构造时持有 enginePath、Init 转发，失败抛异常。
// VoiceInfos 每次重建（引擎在 Init 后才填充，避免缓存到空）。
// CreateSession：老声源（每 part 一份、与会话一一对应）经 LegacySessionAdapter 包成 V1 会话。
internal sealed class VoiceEngineAdapter(LVoice.IVoiceEngine legacy, string enginePath) : VVoice.IVoiceEngine
{
    public PStruct.IReadOnlyOrderedMap<string, VVoice.VoiceSourceInfo> VoiceInfos
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

    public VVoice.ISynthesisSession CreateSession(string voiceId, VVoice.ISynthesisContext context)
    {
        return new LegacySessionAdapter(legacy.CreateVoiceSource(voiceId), context);
    }
}
