namespace TuneLab.Extensions.Voices;

public interface IAutomationValueGetter
{
    double[] GetValue(IReadOnlyList<double> times);
}
