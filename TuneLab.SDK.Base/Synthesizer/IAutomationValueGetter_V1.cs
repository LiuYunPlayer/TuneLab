namespace TuneLab.SDK.Base.Synthesizer;

public interface IAutomationValueGetter_V1
{
    double[] GetValue(IReadOnlyList<double> times);
}
