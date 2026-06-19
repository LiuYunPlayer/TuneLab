using TuneLab.Foundation;

namespace TuneLab.SDK;

// 原 NumberConfig（按 UI 控件命名）。
// 量程与默认值 required：滑条没有范围无意义，强制声明让错误在编译期暴露
// （而非静默退化到某个未必合意的默认量程）。
public class SliderConfig : IValueConfig<double>
{
    public required double DefaultValue { get; init; }
    public required double MinValue { get; init; }
    public required double MaxValue { get; init; }
    public bool IsInteger { get; init; } = false;
    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
