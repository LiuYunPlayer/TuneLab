using System.Collections.Generic;
using TuneLab.Extensions.Synthesizer;
using TuneLab.SDK.Base.Synthesizer;

namespace TuneLab.Extensions.Adapters.Synthesizer;

internal static class IAutomationValueGetterAdapter
{
    public static IAutomationValueGetter_V1 ToV1(this IAutomationValueGetter domain)
    {
        return new IAutomationValueGetter_V1Adapter(domain);
    }

    class IAutomationValueGetter_V1Adapter(IAutomationValueGetter domain) : IAutomationValueGetter_V1
    {
        public double[] GetValue(IReadOnlyList<double> times)
        {
            return domain.GetValue(times);
        }
    }
}
