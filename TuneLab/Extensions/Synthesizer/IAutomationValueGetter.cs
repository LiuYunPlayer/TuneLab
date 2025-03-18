using System.Collections.Generic;

namespace TuneLab.Extensions.Synthesizer;

internal interface IAutomationValueGetter
{
    double[] GetValue(IReadOnlyList<double> times);
}
