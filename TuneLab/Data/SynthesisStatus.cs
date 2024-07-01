using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Data;

internal enum SynthesisStatus
{
    NotSynthesized,
    Synthesizing,
    SynthesisFailed,
    SynthesisSucceeded,
    SynthesisHalfSuccessed
}
