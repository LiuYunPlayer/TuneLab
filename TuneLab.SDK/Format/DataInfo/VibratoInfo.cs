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
    // 颤音对各 effect 自动化轨的影响振幅：外层键 = effect 实例稳定 id（EffectInfo.Id，锚实例身份而非链内位置，
    // 重排/替换免重映射；删除留孤儿、undo 同 id 重连），内层键 = 该 effect 的轨 id。
    // 与 AffectedAutomations（voice 轨）平行，两个命名空间互不相扰。
    public Map<string, Map<string, double>> AffectedEffectAutomations { get; set; } = new();
}
