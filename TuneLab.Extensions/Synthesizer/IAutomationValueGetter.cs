using System.Collections.Generic;

namespace TuneLab.Extensions.Synthesizer;

public interface IAutomationValueGetter
{
    double[] GetValue(IReadOnlyList<double> times);
}
