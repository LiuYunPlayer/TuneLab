using System.Threading;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Derivers;

internal enum DerivationTaskState
{
    Running,       // 后台 worker 跑 Derive 中
    PendingApply,  // 已完成、结果待应用（工程仍零改动）
    Failed,        // Derive 抛异常
}

// 一个运行中 / 待应用 / 失败的派生任务——宿主运行时任务管理器持有，会话态、不持久（关工程即弃；结果已进内容缓存、可重触发）。
// 与源解耦：提交那一瞬冻结输入后，源被移动/编辑/删除都不影响本任务结果（无「陈旧」概念）。
internal sealed class DerivationTask
{
    public string EngineId { get; }
    public string EngineDisplayName { get; }
    // 任务专属文案（如「提取为 MIDI」），供任务面板 / 徽标展示。
    public string TaskLabel { get; }
    // 源 part 身份锚（徽标跟随 / 落点解析用，非位置）；源被删则仍可应用（回退落点）。
    public IAudioPart Source { get; }

    public DerivationTaskState State { get; internal set; } = DerivationTaskState.Running;
    // Running 时 [0,1] 进度；不报进度的引擎恒 0。
    public double Progress { get; internal set; }
    // Running 时可选管线阶段文案；Failed 时错误信息。宿主原样展示。
    public string? Message { get; internal set; }
    // 完成后的派生产物（PendingApply 时非空）。
    public DerivedResult? Result { get; internal set; }

    internal string CacheKey { get; }
    internal CancellationTokenSource Cancellation { get; } = new();

    internal DerivationTask(string engineId, string engineDisplayName, string taskLabel, IAudioPart source, string cacheKey)
    {
        EngineId = engineId;
        EngineDisplayName = engineDisplayName;
        TaskLabel = taskLabel;
        Source = source;
        CacheKey = cacheKey;
    }
}
