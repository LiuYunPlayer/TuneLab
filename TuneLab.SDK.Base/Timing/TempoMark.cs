namespace TuneLab.SDK.Base.Timing;

// ITempoMark 的纯值形态：自包含、不可变、可序列化（4 个 double，可直接做跨进程消息体）。
public readonly record struct TempoMark(double Tick, double Seconds, double Bpm, double TicksPerSecond) : ITempoMark;
