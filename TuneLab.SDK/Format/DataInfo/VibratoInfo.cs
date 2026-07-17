using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.SDK;

public class VibratoInfo
{
    public double Pos { get; set; }
    public double Dur { get; set; }
    public double Frequency { get; set; }
    public double Phase { get; set; }
    public double Amplitude { get; set; }
    public double Attack { get; set; }
    public double Release { get; set; }
    public Map<string, double> AffectedAutomations { get; set; } = new();
    // 颤音对各 effect 自动化轨的影响振幅：外层键 = effect 在 part 链中的槽位下标（宿主在链结构变更时同步重映射），
    // 内层键 = 该 effect 的轨 id。与 AffectedAutomations（voice 轨）平行，两个命名空间互不相扰。
    public Map<int, Map<string, double>> AffectedEffectAutomations { get; set; } = new();
}
