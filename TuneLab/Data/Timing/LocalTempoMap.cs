using TuneLab.SDK;

namespace TuneLab.Data.Timing;

// ITempoMap 的宿主实现：一份不可变的速度表快照 + 原点（工程 tick）。插件给的局部 tick = 工程 tick − 原点；秒是全局秒。
// 快照不可变，所以这份映射任何线程都能用；速度表改了由给出方换一份新的。
internal sealed class LocalTempoMap(TempoSnapshot snapshot, double origin) : ITempoMap
{
    public double PosToTime(double pos) => snapshot.ToSecond(origin + pos);
    public double TimeToPos(double time) => snapshot.ToTick(time) - origin;
}
