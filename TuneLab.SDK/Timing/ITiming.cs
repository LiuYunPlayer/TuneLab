namespace TuneLab.SDK;

// 工程时间轴的 tick↔秒换算服务（由 tempo 表决定的双射度量）。
// 命名约定：Tick = 乐理位置（单位 tick），Second = 实时位置（单位秒）；
// 单复数表元数——表示单个时间点用单数（ToTick/ToSecond），批量才用复数（ToTicks/ToSeconds）。
public interface ITiming
{
    double ToSecond(double tick);
    double ToTick(double second);

    // 批量版要求输入升序（实现为单次扫描；乱序输入结果未定义）。
    double[] ToSeconds(IReadOnlyList<double> ticks);
    double[] ToTicks(IReadOnlyList<double> seconds);
}
