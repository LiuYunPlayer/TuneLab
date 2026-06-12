using TuneLab.Primitives.Resources;

namespace TuneLab.SDK;

// 声库目录元数据（IVoiceEngine.VoiceInfos 的值；会话不重复承载这些）。
public struct VoiceSourceInfo
{
    public string Name;
    public string Description;
    public ImageResource? Portrait;   // 可选立绘（显示在钢琴窗）；null = 无
}
