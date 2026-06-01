using TuneLab.Primitives.Property;

namespace TuneLab.SDK.Base;

// 原 NumberConfig（§三.12 按 UI 控件改名）。
public class SliderConfig(double defaultValue = 0, double minValue = double.NegativeInfinity, double maxValue = double.PositiveInfinity, bool isInterger = false) : IValueConfig<double>
{
    public double DefaultValue { get; set; } = defaultValue;
    public double MinValue { get; set; } = minValue;
    public double MaxValue { get; set; } = maxValue;
    public bool IsInterger { get; set; } = isInterger;
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
