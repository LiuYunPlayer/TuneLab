namespace TuneLab.SDK;

public class TempoInfo
{
    // 变速点位置，单位 = tick（PPQ 480），全局时间线绝对位置（非 part 相对）。
    public double Pos { get; set; }
    // 该点起的速度，单位 = BPM（每分钟四分音符数）。
    public double Bpm { get; set; }
}
