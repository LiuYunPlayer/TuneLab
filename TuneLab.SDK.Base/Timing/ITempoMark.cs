namespace TuneLab.SDK.Base.Timing;

// tempo 表中的一个标记：自该位置起生效的速度，及其预解析的双时间域位置。
// 换算逻辑只依赖此只读视图，宿主活对象与冻结快照实现同一接口、共用同一份算法。
public interface ITempoMark
{
    double Tick { get; }            // 乐理位置（tick）
    double Seconds { get; }         // 实时位置（秒）
    double Bpm { get; }
    double TicksPerSecond { get; }  // 换算系数（= Bpm / 60 × 每四分音符 tick 数）
}
