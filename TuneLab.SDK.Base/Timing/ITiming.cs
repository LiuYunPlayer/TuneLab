namespace TuneLab.SDK.Base.Timing;

// 工程时间轴的 tick↔秒换算服务（由 tempo 表决定的双射度量）。
// 命名约定：Tick = 乐理位置（单位 tick），Seconds = 实时位置（单位秒）。
public interface ITiming
{
    double ToSeconds(double tick);
    double ToTick(double seconds);

    // 批量版要求输入升序（实现为单次扫描；乱序输入结果未定义）。
    double[] ToSeconds(IReadOnlyList<double> ticks);
    double[] ToTick(IReadOnlyList<double> seconds);
}
