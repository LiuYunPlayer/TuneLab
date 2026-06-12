namespace TuneLab.SDK;

// 调度块的纯值边界（秒，与音频产物、状态段同一时间系）：宿主只用它排播放线就近优先级。
// 不携带捕获声明、不是插件对象——快照获取归插件主动（ISynthesisContext.GetSnapshot），
// 插件 peek 时如需为 commit 留信息（分块缓存等）在会话自己的字段里存即可。
public readonly struct SynthesisSegment(double startTime, double endTime)
{
    public double StartTime { get; } = startTime;
    public double EndTime { get; } = endTime;
}
