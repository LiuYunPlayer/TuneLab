namespace TuneLab.SDK;

// 派生变速点（速度检测型才产）：位置说秒。宿主按治理策略决定是否采纳（默认仅可用不落，用户显式勾选才合并）。
public sealed class DerivedTempo
{
    // 变速点位置，单位 = 秒（绝对音频内容时间）。
    public required double Time { get; init; }
    // 该点起的速度，单位 = BPM（每分钟四分音符数）。
    public required double Bpm { get; init; }
}
