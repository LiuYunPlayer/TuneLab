using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Synthesizer;

namespace TuneLab.Extensions.Effect;

public interface IEffectSynthesisTask
{
    event Action<double>? Progress;
    event Action<SynthesisException?>? Finished;

    void Start();
    void Stop();
    void SetDirty(string context, string key);
}
