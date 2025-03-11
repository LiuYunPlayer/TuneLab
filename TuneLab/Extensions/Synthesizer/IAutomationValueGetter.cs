using System.Collections.Generic;
using TuneLab.SDK.Base.Synthesizer;

namespace TuneLab.Extensions.Synthesizer;

internal interface IAutomationValueGetter
{
    double[] GetValue(IReadOnlyList<double> times);
}
