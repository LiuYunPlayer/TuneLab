using TuneLab.Foundation;

namespace TuneLab.SDK;

// 一条分段自动化轨的存储形（无默认基线，区别于连续轨 AutomationInfo）：分段折线，每段一条 Point 列表。
// Point.X = tick（相对 part 锚点），Y = 轨值（该轨值轴单位，由轨的 AutomationConfig 量程定义）。
// 声源/effect 声明的可编辑分段曲线（即 AutomationConfig.DefaultValue 为 NaN 的轨）走此型。
public class PiecewiseAutomationInfo
{
    public List<List<Point>> Segments { get; set; } = new();
}
