namespace TuneLab.SDK.Base.Timing;

// 时间点的双域表示：同一时刻的乐理位置（tick）与实时位置（秒）由宿主按 tempo 表解析后一次给齐，
// 引擎免于自行换算（只需秒的引擎直读 Seconds，需要精确 tick 时钟的直读 Tick）。
// 命名约定：Tick = 乐理位置（单位 tick），Seconds = 实时位置（单位秒）。
public readonly struct Position(double tick, double seconds)
{
    public double Tick { get; } = tick;
    public double Seconds { get; } = seconds;

    // 仅诊断用途：格式不构成契约，但显式写全避免被误解析（"s"易歧义）。
    public override string ToString() => $"(tick: {Tick}, seconds: {Seconds})";
}
