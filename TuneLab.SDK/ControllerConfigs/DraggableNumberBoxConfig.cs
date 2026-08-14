using TuneLab.Foundation;

namespace TuneLab.SDK;

// 可拖拽数值框：number 控件族里覆盖无界/单界/双界的通用 config（SliderConfig 是其"双有界 + 可视轨道"的特化）。
// 量程语义比 slider 更广——Min/Max 各自可空，分别表达"该侧无界"。构造函数全封，只走静态工厂 + 流式 With
// （与 SliderConfig 同款 ABI 理由：见 SliderConfig）。With 走 Clone：加字段只改声明处，不逐个 With 手抄字段。
public sealed class DraggableNumberBoxConfig : IValueConfig<double>
{
    public double DefaultValue { get; private set; }

    // 边界。各自 null = 该侧无界；Set 时按存在的一侧 clamp。
    public double? Min { get; private set; }
    public double? Max { get; private set; }

    // 像素位移 → 值的映射（手感）。默认每像素 +1；二维组合 / 非线性曲线经 DragResponse.Custom 注入。
    public IDragResponse Response { get; private set; } = DragResponse.Linear(1.0);

    // 吸附步长。null = 连续；非 null = 吸附到该步长的整数倍（整数即 Step = 1）。
    public double? Step { get; private set; }

    // 数值显示/回读。默认 2 位小数；Integer 工厂改为 0 位。
    public INumberFormat Format { get; private set; } = NumberFormat.Decimals(2);

    // 可随机：宿主在数值框右侧给随机入口，点击后在 [Min,Max] 内按均匀分布重取值（如随机种子）。
    // 仅**双有界**（Min 与 Max 皆设）时生效——无界侧上没有均匀分布可取，缺任一侧宿主不给该入口。
    public bool Randomizable { get; private set; }

    private DraggableNumberBoxConfig() { }
    DraggableNumberBoxConfig Clone() => (DraggableNumberBoxConfig)MemberwiseClone();

    // 全无界（最泛用入口）。
    public static DraggableNumberBoxConfig Create(double defaultValue = 0)
        => new() { DefaultValue = defaultValue };

    // 整数：吸附到整数 + 0 位小数显示。仍无界，与"整数滑条"区别在不需要量程。
    public static DraggableNumberBoxConfig Integer(double defaultValue = 0)
        => Create(defaultValue).WithStep(1).WithFormat(NumberFormat.Decimals(0));

    public DraggableNumberBoxConfig WithMin(double min) { var c = Clone(); c.Min = min; return c; }
    public DraggableNumberBoxConfig WithMax(double max) { var c = Clone(); c.Max = max; return c; }
    public DraggableNumberBoxConfig WithRange(double min, double max) { var c = Clone(); c.Min = min; c.Max = max; return c; }
    public DraggableNumberBoxConfig WithResponse(IDragResponse response) { var c = Clone(); c.Response = response; return c; }

    // 便利糖：等价 WithResponse(DragResponse.Linear(sensitivity))。
    public DraggableNumberBoxConfig WithSensitivity(double sensitivity)
        => WithResponse(DragResponse.Linear(sensitivity));

    public DraggableNumberBoxConfig WithStep(double step) { var c = Clone(); c.Step = step; return c; }
    public DraggableNumberBoxConfig WithFormat(INumberFormat format) { var c = Clone(); c.Format = format; return c; }

    // 声明可随机（同 SliderConfig）。须配合 WithRange / WithMin+WithMax——单侧或无界时宿主不给随机入口。
    public DraggableNumberBoxConfig WithRandomizable(bool value = true) { var c = Clone(); c.Randomizable = value; return c; }

    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
