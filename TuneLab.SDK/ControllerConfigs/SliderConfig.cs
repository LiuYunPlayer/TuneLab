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

    // 声明该值可随机：宿主会在滑条右侧给出随机入口，触发后在量程内重新取值（如随机种子）。
    // 配合大数（整数量程可能很大）时数值标签会自动按内容加宽。
    // 注意：值底层以 double 存储，整数仅在 [-2^53, 2^53] 内精确；随机取值会钳到该区间，
    // 超出部分不参与抽取（避免大整数被量化导致无法精确回存，如种子复现失真）。
    public bool Randomizable { get; init; } = false;

    PropertyValue IValueConfig.DefaultValue => PropertyValue.Create(DefaultValue);
}
