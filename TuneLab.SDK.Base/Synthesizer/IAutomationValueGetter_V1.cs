namespace TuneLab.SDK.Base;

public interface IAutomationValueGetter_V1
{
    double[] GetValue(IReadOnlyList<double> times);
}
