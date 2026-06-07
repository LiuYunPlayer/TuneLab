using TuneLab.Primitives.Property;

namespace TuneLab.SDK.Base;

// 原 NumberConfig（按 UI 控件命名）。
public class SliderConfig : IValueConfig<double>
{
    public string? DisplayText { get; init; }
    public double DefaultValue { get; init; } = 0;
    public double MinValue { get; init; } = double.NegativeInfinity;
    public double MaxValue { get; init; } = double.PositiveInfinity;
    public bool IsInterger { get; init; } = false;
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
