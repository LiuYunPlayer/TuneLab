namespace TuneLab.SDK;

public class TimeSignatureInfo
{
    // 该拍号生效的起始小节序号（0 基，即第一小节 = 0）。
    public int BarIndex { get; set; }
    // 拍号分子 / 分母：每小节拍数 / 以几分音符为一拍（如 3/4 → Numerator=3, Denominator=4）。
    public int Numerator { get; set; }
    public int Denominator { get; set; }
}
