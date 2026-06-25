namespace TuneLab.SDK;

// GetNextSegment 的返回：插件报给宿主的「下一块大致区间」纯值边界（秒，与音频产物、状态段同一时间系），
// 宿主只用它在多会话间排播放线就近优先级。不精确、不承载 notelist——精确 notelist 由插件在
// SynthesizeNext 里按宿主回传的同一窗口确定性重导出（或 peek 时自缓存于会话字段）。
// 故它不入 SynthesizeNext 入参（避免把插件自报的 hint 原样回灌）。
// 命名：改自旧 SynthesisSegment——「段」已被 IAudioSegment（音频承载段）与 SynthesisStatusSegment
// （UI 状态段）占用，避免三义重叠。
public readonly struct SynthesisRange(double startTime, double endTime)
{
    public double StartTime { get; } = startTime;
    public double EndTime { get; } = endTime;
}
