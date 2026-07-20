using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.SDK;

public class VibratoInfo
{
    // 位置 / 时长，单位 = tick（PPQ 480），相对所属 part 的锚点（PartInfo.Pos）。
    public double Pos { get; set; }
    public double Dur { get; set; }
    // 颤动频率，单位 = Hz（每秒周期数）。
    public double Frequency { get; set; }
    // 初相位，单位 = π（归一化：实际弧度 = Phase × π）。
    public double Phase { get; set; }
    // 峰值幅度，单位随目标自动化轨（音高轨为半音）；对各轨的实际影响量见 AffectedAutomations / AffectedEffectAutomations。
    public double Amplitude { get; set; }
    // 起振 / 收束时长，单位 = 秒。
    public double Attack { get; set; }
    public double Release { get; set; }
    public Map<string, double> AffectedAutomations { get; set; } = new();
    // 颤音对各 effect 自动化轨的影响振幅：外层键 = effect 实例稳定 id（EffectInfo.Id，锚实例身份而非链内位置，
    // 重排/替换免重映射；删除留孤儿、undo 同 id 重连），内层键 = 该 effect 的轨 id。
    // 与 AffectedAutomations（voice 轨）平行，两个命名空间互不相扰。
    public Map<string, Map<string, double>> AffectedEffectAutomations { get; set; } = new();
}
