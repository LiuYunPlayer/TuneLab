namespace TuneLab.SDK;

// 派生 part 基类（midi / audio 两支）。产物 part 就是占一段绝对内容秒 [StartTime, EndTime]——直接两字段，
// 不用 DataInfo 的「锚点 Pos + 有符号偏移」裁剪模型（那是音频引用可分离锚点 / 可向前扩展才需要的间接层，产物不需要）。
// 命名与 DerivedNote/DerivedTempo/DerivedTimeSignature 一致。
//
// 坐标系：绝对音频内容秒（采样点 0 = 0 秒），part 内 note/pitch 的时间同此坐标系。
// StartTime 默认 0（内容起点）；EndTime 默认 +∞（终点开放，宿主应用时钳到内容/输入末）——只关心一端的插件只设该端。
// 切分型插件（如按静音段切分产轨内多个不重叠 part）才两端都显式给、界定各 part 窗口。
public abstract class DerivedPart
{
    // part 起点，单位 = 秒（绝对音频内容时间）。默认 0 = 内容起点。
    public double StartTime { get; init; }
    // part 终点，单位 = 秒。默认 +∞ = 终点开放（钳到内容/输入末）。
    public double EndTime { get; init; } = double.PositiveInfinity;
}
