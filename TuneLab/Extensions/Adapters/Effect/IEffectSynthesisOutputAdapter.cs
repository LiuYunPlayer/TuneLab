using System;
using System.Collections.Generic;
using TuneLab.Extensions.Adapters.DataStructures;
using TuneLab.Extensions.Adapters.Synthesizer;
using TuneLab.Extensions.Effect;
using TuneLab.SDK.Base.DataStructures;
using TuneLab.SDK.Base.Synthesizer;
using TuneLab.SDK.Effect;

namespace TuneLab.Extensions.Adapters.Effect;

internal static class IEffectSynthesisOutputAdapter
{
    public static IEffectSynthesisOutput_V1 ToV1(this IEffectSynthesisOutput domain)
    {
        return new IEffectSynthesisOutput_V1Adapter(domain);
    }

    class IEffectSynthesisOutput_V1Adapter(IEffectSynthesisOutput domain) : IEffectSynthesisOutput_V1
    {
        public MonoAudio_V1 Audio { get => domain.Audio.ToV1(); set => domain.Audio = value.ToDomain(); }
        // FIXME: Implement this
        public IMap_V1<string, IReadOnlyList<IReadOnlyList<Point_V1>>> SynthesizedAutomations => throw new NotImplementedException(); //domain.SynthesizedAutomations.Convert((IReadOnlyList<IReadOnlyList<Point>> lines) => lines.Convert(line => line.Convert(PointAdapter.ToV1))).ToV1();
    }
}
