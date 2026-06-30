using TuneLab.Foundation;

namespace TuneLab.SDK;

// 可拖拽数值框：number 控件族里覆盖无界/单界/双界的通用 config（SliderConfig 是其"双有界 + 可视轨道"的特化）。
// 量程语义比 slider 更广——Min/Max 各自可空，分别表达"该侧无界"。构造函数全封，只走静态工厂 + 流式 With
// （与 SliderConfig 同款 ABI 理由：见 SliderConfig）。
public sealed class DraggableNumberBoxConfig : IValueConfig<double>
{
    public double DefaultValue { get; private init; }

    // 边界。各自 null = 该侧无界；Set 时按存在的一侧 clamp。
    public double? Min { get; private init; }
    public double? Max { get; private init; }

    // 像素位移 → 值的映射（手感）。默认每像素 +1；二维组合 / 非线性曲线经 DragResponse.Custom 注入。
    public IDragResponse Response { get; private init; } = DragResponse.Linear(1.0);

    // 吸附步长。null = 连续；非 null = 吸附到该步长的整数倍（整数即 Step = 1）。
    public double? Step { get; private init; }

    // 数值显示/回读。默认 2 位小数；Integer 工厂改为 0 位。
    public INumberFormat Format { get; private init; } = NumberFormat.Decimals(2);

    private DraggableNumberBoxConfig() { }

    // 全无界（最泛用入口）。
    public static DraggableNumberBoxConfig Create(double defaultValue = 0)
        => new() { DefaultValue = defaultValue };

    // 整数：吸附到整数 + 0 位小数显示。仍无界，与"整数滑条"区别在不需要量程。
    public static DraggableNumberBoxConfig Integer(double defaultValue = 0)
        => Create(defaultValue).WithStep(1).WithFormat(NumberFormat.Decimals(0));

    public DraggableNumberBoxConfig WithMin(double min)
        => new() { DefaultValue = DefaultValue, Min = min, Max = Max, Response = Response, Step = Step, Format = Format };

    public DraggableNumberBoxConfig WithMax(double max)
        => new() { DefaultValue = DefaultValue, Min = Min, Max = max, Response = Response, Step = Step, Format = Format };

    public DraggableNumberBoxConfig WithRange(double min, double max)
        => new() { DefaultValue = DefaultValue, Min = min, Max = max, Response = Response, Step = Step, Format = Format };

    public DraggableNumberBoxConfig WithResponse(IDragResponse response)
        => new() { DefaultValue = DefaultValue, Min = Min, Max = Max, Response = response, Step = Step, Format = Format };

    // 便利糖：等价 WithResponse(DragResponse.Linear(sensitivity))。
    public DraggableNumberBoxConfig WithSensitivity(double sensitivity)
        => WithResponse(DragResponse.Linear(sensitivity));

    public DraggableNumberBoxConfig WithStep(double step)
        => new() { DefaultValue = DefaultValue, Min = Min, Max = Max, Response = Response, Step = step, Format = Format };

    public DraggableNumberBoxConfig WithFormat(INumberFormat format)
        => new() { DefaultValue = DefaultValue, Min = Min, Max = Max, Response = Response, Step = Step, Format = format };

    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
