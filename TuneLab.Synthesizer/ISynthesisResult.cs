using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;

namespace TuneLab.Synthesizer;

public interface ISynthesisResult
{
    Audio Audio { get; set; }
    IDictionary<string, IReadOnlyList<IReadOnlyList<Point>>> SynthesizedAutomations { get; }
}
