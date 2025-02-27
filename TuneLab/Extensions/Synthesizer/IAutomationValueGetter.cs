using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Base;

namespace TuneLab.Extensions.Synthesizer;

internal interface IAutomationValueGetter : IAutomationValueGetter_V1
{
    double[] GetValue(IReadOnlyList<double> times);

    // V1 Adapter
    double[] IAutomationValueGetter_V1.GetValue(IReadOnlyList<double> times) => GetValue(times);
}
