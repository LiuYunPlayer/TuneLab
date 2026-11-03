namespace TuneLab.SDK;

// 速度映射（宿主实现）：局部 tick ↔ 全局秒。不可变、任何线程都能用。
//
// tick 相对这份映射的原点（谁给出映射谁定原点：画布 lane 的是所属音符的起点），局部 tick 可以为负；秒是全局秒，
// 与插件拿到的其余时间（音符 StartTime、自动化轨求值、音频段）同一口径。速度表改动后宿主给出的是另一份映射，旧的这份不变。
public interface ITempoMap
{
    double PosToTime(double pos);
    double TimeToPos(double time);
}
