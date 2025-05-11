using TuneLab.Extensions.Synthesizer;
using TuneLab.SDK.Base.Synthesizer;

namespace TuneLab.Extensions.Adapters.Synthesizer;

internal static class MonoAudioAdapter
{
    public static MonoAudio_V1 ToV1(this MonoAudio domain)
    {
        return new MonoAudio_V1
        {
            StartTime = domain.StartTime,
            SampleRate = domain.SampleRate,
            Samples = domain.Samples,
        };
    }

    public static MonoAudio ToDomain(this MonoAudio_V1 v1)
    {
        return new MonoAudio
        {
            StartTime = v1.StartTime,
            SampleRate = v1.SampleRate,
            Samples = v1.Samples,
        };
    }
}
