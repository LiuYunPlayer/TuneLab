namespace TuneLab.SDK.Base.Timing;

// tempo 表条目的最小真值：自该乐理位置起生效的速度。
// 实时位置（秒）与换算系数都是它的派生值，由 TempoSnapshot 构造时一次推导，不进公共契约——
// 派生值若进接口，"须与 (Tick, Bpm) 积分一致"的不变量无法由类型系统强制，实现者算错即静默坏换算。
public readonly record struct TempoMark(double Tick, double Bpm);
