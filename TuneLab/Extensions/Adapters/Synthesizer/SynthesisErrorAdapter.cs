using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
