using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Voices;

public interface ISynthesisTask
{
    event Action<SynthesisResult>? Complete;
    event Action<double>? Progress;
    event Action<string>? Error;
    void Start();
    void Suspend();
    void Resume();
    void Stop();
    void SetDirty(string dirtyType);
}
