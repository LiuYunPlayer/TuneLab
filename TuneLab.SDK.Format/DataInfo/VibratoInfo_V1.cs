using TuneLab.SDK.Base;

namespace TuneLab.SDK.Format.DataInfo;

public class VibratoInfo_V1
{
    public double Pos { get; set; }
    public double Dur { get; set; }
    public double Frequency { get; set; }
    public double Phase { get; set; }
    public double Amplitude { get; set; }
    public double Attack { get; set; }
    public double Release { get; set; }
    public Map_V1<string, double> AffectedAutomations { get; set; } = [];
}
