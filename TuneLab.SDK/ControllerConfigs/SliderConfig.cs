using TuneLab.Foundation;

namespace TuneLab.SDK;

// 原 NumberConfig（按 UI 控件命名）。构造函数全封，只经静态工厂 + 流式 With 构造——
// 不把"数据形态"放进公共 ABI：日后加旋钮/换标度只需增工厂或 With 方法，绝不破坏已编译插件的调用点。
// With 走 Clone（对齐 AutomationConfig）：加字段只改声明处，不逐个 With 手抄字段。
public sealed class SliderConfig : IValueConfig<double>
{
    public double DefaultValue { get; private set; }

    // 位置↔值映射。量程亦由它定义（MinValue = ToValue(0)、MaxValue = ToValue(1)），故无需单独的 Min/Max 字段。
    public INormalizedScale Scale { get; private set; } = null!;

    // 数值显示/回读。非空——默认 2 位小数；Integer 工厂改为 0 位。把默认放进 config 即省去可空语义与宿主侧兜底。
    public INumberFormat Format { get; private set; } = NumberFormat.Decimals(2);

    // 可随机：宿主在滑条右侧给随机入口，点击后在标度上按归一化均匀重取值（如随机种子）。
    public bool Randomizable { get; private set; }

    // 量程两端的描述文本（如 min="Soft"、max="Hard"）：显示在滑条两端；该属性上参数面板（lane）时同文本作上下界。
    // 可选、由插件按当前语言自译、可只设一端——与 AutomationConfig 的同名字段同一语义。
    public string? MinLabel { get; private set; }
    public string? MaxLabel { get; private set; }

    private SliderConfig() { }
    SliderConfig Clone() => (SliderConfig)MemberwiseClone();

    // 连续线性。量程是整数字面量也走这里（int 自动转 double），保持连续——不要为"整数滑条"误用本工厂。
    public static SliderConfig Linear(double defaultValue, double minValue, double maxValue)
        => new() { DefaultValue = defaultValue, Scale = NormalizedScale.Linear(minValue, maxValue) };

    // 整数滑条（取代原 IsInteger）：拖动/输入吸附到整数，默认 0 位小数显示。
    public static SliderConfig Integer(double defaultValue, double minValue, double maxValue)
        => new() { DefaultValue = defaultValue, Scale = NormalizedScale.Integer(minValue, maxValue), Format = NumberFormat.Decimals(0) };

    // 自定义标度入口（对数轴等）。Create 取 .NET 静态工厂惯例（ImmutableArray.Create/Tuple.Create）。
    public static SliderConfig Create(double defaultValue, INormalizedScale scale)
        => new() { DefaultValue = defaultValue, Scale = scale };

    public SliderConfig WithFormat(INumberFormat format) { var c = Clone(); c.Format = format; return c; }
    public SliderConfig WithRandomizable(bool value = true) { var c = Clone(); c.Randomizable = value; return c; }
    // 量程两端描述文本（插件自译，可只设一端）。
    public SliderConfig WithMinLabel(string label) { var c = Clone(); c.MinLabel = label; return c; }
    public SliderConfig WithMaxLabel(string label) { var c = Clone(); c.MaxLabel = label; return c; }

    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
