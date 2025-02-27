using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;
using TuneLab.SDK.Base;

namespace TuneLab.Extensions.Synthesizer;

internal interface ISynthesisOutput
{
    MonoAudio Audio { get; set; }
    IDictionary<string, IReadOnlyList<IReadOnlyList<Point>>> SynthesizedAutomations { get; }
}
