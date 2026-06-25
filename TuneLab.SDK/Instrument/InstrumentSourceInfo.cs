using TuneLab.Foundation;

namespace TuneLab.SDK;

// 音源目录元数据（IInstrumentEngine.InstrumentSourceInfos 的值；会话不重复承载这些）。
public struct InstrumentSourceInfo
{
    public string Name;
    public string Description;
    public ImageResource? Portrait;   // 可选立绘（显示在钢琴窗）；null = 无
}
