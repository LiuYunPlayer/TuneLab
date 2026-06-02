using LVoice = TuneLab.Extensions.Voices;
using VVoice = TuneLab.SDK.Voice;
using PStruct = TuneLab.Primitives.DataStructures;
using TuneLab.Hosting.Compat.Legacy.Conversion;

namespace TuneLab.Hosting.Compat.Legacy.Voice;

// 把老 IVoiceEngine 适配成 V1 IVoiceEngine。Init/Destroy 直转发（enginePath 由宿主 VoicesManager 传入 = 包目录）；
// CreateVoiceSource 返回老 source 的 V1 适配器。VoiceInfos 每次重建（引擎在 Init 后才填充，避免缓存到空）。
internal sealed class VoiceEngineAdapter(LVoice.IVoiceEngine legacy) : VVoice.IVoiceEngine
{
    public PStruct.IReadOnlyOrderedMap<string, VVoice.VoiceSourceInfo> VoiceInfos
    {
        get
        {
            var map = new PStruct.OrderedMap<string, VVoice.VoiceSourceInfo>();
            foreach (var kv in legacy.VoiceInfos)
                map.Add(kv.Key, kv.Value.ToV1());
            return map;
        }
    }

    public bool Init(string enginePath, out string? error) => legacy.Init(enginePath, out error);
    public void Destroy() => legacy.Destroy();
    public VVoice.IVoiceSource CreateVoiceSource(string id) => new VoiceSourceAdapter(legacy.CreateVoiceSource(id));
}
