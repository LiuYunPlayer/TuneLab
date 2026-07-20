using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 一条连续自动化轨的存储形。分段轨（无默认基线）不走此型，见 MidiPartInfo/EffectInfo.PiecewiseAutomations。
public class AutomationInfo
{
    // 基线值（曲线未覆盖处的取值），单位 = 该轨值轴单位（由轨的 AutomationConfig 量程定义）。
    public double DefaultValue { get; set; }
    // 曲线锚点，时间序。Point.X = tick（相对 part 锚点），Y = 轨值（该轨值轴单位）。
    public List<Point> Points { get; set; } = new();
}
