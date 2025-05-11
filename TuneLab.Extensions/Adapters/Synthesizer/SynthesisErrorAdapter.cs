using TuneLab.Extensions.Synthesizer;
using TuneLab.SDK.Base.Synthesizer;

namespace TuneLab.Extensions.Adapters.Synthesizer;

internal static class SynthesisErrorAdapter
{
    public static SynthesisError ToDomain(this SynthesisError_V1 v1)
    {
        return new SynthesisError();
    }
}
