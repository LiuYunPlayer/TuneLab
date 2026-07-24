using TuneLab.Foundation;

namespace TuneLab.SDK;

// 音高偏差曲线的存储形。分段折线：每段一条 Point 列表，段间为音高关断（无值）。
// Point.X = tick（相对 part 锚点），Y = 音高（MIDI note number，连续可含小数）。
// 与分段自动化轨（PiecewiseAutomationInfo）同为分段折线，但音高是独立概念（值轴 = MIDI note number），
// 故各自命名、互不复用——与合成域 SynthesizedPitch / SynthesizedParameter 的分立命名一致。
public class PitchInfo
{
    public List<List<Point>> Segments { get; set; } = new();
}
