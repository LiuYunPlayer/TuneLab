using TuneLab.Foundation;

namespace TuneLab.SDK;

// 时间轴 lane 画布的不可变冻结快照（worker 线程只读）：与活视图 ISynthesisTimelineLaneCanvas 镜像，少了变化通知。
// 用容器而非裸值，理由同 SynthesisAutomationSnapshot（将来加字段是纯加法）。
public sealed class SynthesisTimelineLaneCanvasSnapshot
{
    // 取快照那一刻的值；没设过 = Null。
    public required PropertyValue Value { get; init; }

    // 局部 tick ↔ 全局秒（与活视图同一个原点）。
    public required ITimeConverter TimeConverter { get; init; }
}
