using System.Collections.Generic;
using System.Linq;
using LVoice = TuneLab.Extensions.Voices;
using VVoice = TuneLab.SDK.Voice;
using PStruct = TuneLab.Primitives.DataStructures;
using TuneLab.Hosting.Compat.Legacy.Conversion;

namespace TuneLab.Hosting.Compat.Legacy.Voice;

internal static class VoiceConvert
{
    public static VVoice.SynthesizedPhoneme ToV1(this LVoice.SynthesizedPhoneme p)
        => new() { Symbol = p.Symbol, StartTime = p.StartTime, EndTime = p.EndTime };

    public static LVoice.SynthesizedPhoneme ToLegacy(this VVoice.SynthesizedPhoneme p)
        => new() { Symbol = p.Symbol, StartTime = p.StartTime, EndTime = p.EndTime };

    public static VVoice.VoiceSourceInfo ToV1(this LVoice.VoiceSourceInfo i)
        => new() { Name = i.Name, Description = i.Description };

    // 老 SynthesisResult → V1：
    //   · audio float[] 同类型 → 直接共享引用（零拷贝，handoff 后视为不可变）。
    //   · pitch 逐点拷贝（冷，每结果一次）。
    //   · phonemes 以 note 为键 → 经 LegacyNoteAdapter.Origin 映射回宿主 V1 note（身份保持）。
    public static VVoice.SynthesisResult ToV1(this LVoice.SynthesisResult old)
    {
        var pitch = old.SynthesizedPitch
            .Select(inner => (IReadOnlyList<PStruct.Point>)inner.Select(p => p.ToV1()).ToList())
            .ToList();

        var phonemes = new Dictionary<VVoice.ISynthesisNote, VVoice.SynthesizedPhoneme[]>(ReferenceEqualityComparer.Instance);
        foreach (var kv in old.SynthesizedPhonemes)
        {
            if (kv.Key is LegacyNoteAdapter adapter)
                phonemes[adapter.Origin] = kv.Value.Select(ToV1).ToArray();
        }

        return new VVoice.SynthesisResult(old.StartTime, old.SamplingRate, old.AudioData, pitch, phonemes);
    }
}
