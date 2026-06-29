using TuneLab.Foundation;

namespace TuneLab.SDK;

// 原 NumberConfig（按 UI 控件命名）。构造函数全封，只经静态工厂 + 流式 With 构造——
// 不把"数据形态"放进公共 ABI：日后加旋钮/换标度只需增工厂或 With 方法，绝不破坏已编译插件的调用点。
public sealed class SliderConfig : IValueConfig<double>
{
    public double DefaultValue { get; private init; }

    // 位置↔值映射。量程亦由它定义（MinValue = ToValue(0)、MaxValue = ToValue(1)），故无需单独的 Min/Max 字段。
    // 非 required（required 不容许 private 级 setter）：构造函数已全封，唯一构造路径是下方工厂，必设此值。
    public INormalizedScale Scale { get; private init; } = null!;

    // 数值显示/回读。非空——默认 2 位小数；Integer 工厂改为 0 位。把默认放进 config 即省去可空语义与宿主侧兜底。
    public INumberFormat Format { get; private init; } = NumberFormat.Decimals(2);

    // 可随机：宿主在滑条右侧给随机入口，点击后在标度上按归一化均匀重取值（如随机种子）。
    public bool Randomizable { get; private init; }

    private SliderConfig() { }

    // 连续线性。量程是整数字面量也走这里（int 自动转 double），保持连续——不要为"整数滑条"误用本工厂。
    public static SliderConfig Linear(double defaultValue, double minValue, double maxValue)
        => new() { DefaultValue = defaultValue, Scale = NormalizedScale.Linear(minValue, maxValue) };

    // 整数滑条（取代原 IsInteger）：拖动/输入吸附到整数，默认 0 位小数显示。
    public static SliderConfig Integer(double defaultValue, double minValue, double maxValue)
        => new() { DefaultValue = defaultValue, Scale = NormalizedScale.Integer(minValue, maxValue), Format = NumberFormat.Decimals(0) };

    // 自定义标度入口（对数轴等）。Create 取 .NET 静态工厂惯例（ImmutableArray.Create/Tuple.Create）。
    public static SliderConfig Create(double defaultValue, INormalizedScale scale)
        => new() { DefaultValue = defaultValue, Scale = scale };

    public SliderConfig WithFormat(INumberFormat format)
        => new() { DefaultValue = DefaultValue, Scale = Scale, Format = format, Randomizable = Randomizable };

    public SliderConfig WithRandomizable(bool value = true)
        => new() { DefaultValue = DefaultValue, Scale = Scale, Format = Format, Randomizable = value };

    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
