namespace TuneLab.SDK;

// 派生拍号（拍号检测型才产）：以物理秒锚点定位（无 tempo/grid 即无「小节」概念，故不能用小节序号）。
// 宿主装检测速度图后据它把秒换算到 tick 小节。
public sealed class DerivedTimeSignature
{
    // 该拍号生效位置，单位 = 秒（绝对音频内容时间）。
    public required double Time { get; init; }
    // 拍号分子 / 分母（如 3/4 → Numerator=3, Denominator=4）。
    public required int Numerator { get; init; }
    public required int Denominator { get; init; }
}
