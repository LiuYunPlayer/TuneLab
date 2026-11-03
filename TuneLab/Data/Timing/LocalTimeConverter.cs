using System.Collections.Generic;
using TuneLab.SDK;

namespace TuneLab.Data.Timing;

// ITimeConverter 的宿主实现：一份不可变的速度表快照 + 原点（工程 tick）。插件给的局部 tick = 工程 tick − 原点；秒是全局秒。
// 快照不可变，所以这份换算器任何线程都能用；速度表改了由给出方换一份新的。批量版沿用快照的单次扫描（输入须升序）。
internal sealed class LocalTimeConverter(TempoSnapshot snapshot, double origin) : ITimeConverter
{
    public double[] ToSeconds(IReadOnlyList<double> ticks)
    {
        var global = new double[ticks.Count];
        for (int i = 0; i < global.Length; i++)
            global[i] = origin + ticks[i];
        return snapshot.ToSeconds(global);
    }

    public double[] ToTicks(IReadOnlyList<double> seconds)
    {
        var ticks = snapshot.ToTicks(seconds);
        for (int i = 0; i < ticks.Length; i++)
            ticks[i] -= origin;
        return ticks;
    }
}
