using System.Threading;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Derivers;

internal enum DerivationTaskState
{
    Queued,    // 已排队、等空槽（全局并发上限见 Settings.MaxParallelDerivationTasks）
    Running,   // 后台 worker 跑 Derive 中
    Failed,    // Derive 抛异常（记录仍持久存在、缓存缺失 => 呈现「已失效」可重跑）
}

// 一次派生的【运行时在飞态】（排队 / 运行中 / 失败）——会话态、不持久。
// 与持久的 DerivationRecordInfo 是同一次派生的两面：记录随工程存（提交即落），任务只承载在飞进度 / 失败信息。
// 成功完成后任务功成身退（移除），该次派生由「记录 + 内容缓存」承载；重开工程只见记录（缓存在则可应用、缺则已失效）。
//
// 与源解耦：提交那一瞬冻结输入（Input）后，源被移动/编辑/删除都不影响本任务结果。落点/裁剪是 apply-side、按当时几何。
internal sealed class DerivationTask
{
    public string EngineId { get; }
    public string EngineDisplayName { get; }
    // 任务专属文案（如「提取为 MIDI」），供任务面板 / 徽标展示。
    public string TaskLabel { get; }
    // 源 part 身份锚（徽标跟随 / 落点解析 / 记录归属用，非位置）。
    public IAudioPart Source { get; }

    public DerivationTaskState State { get; internal set; } = DerivationTaskState.Queued;
    // Running 时 [0,1] 进度；不报进度的引擎恒 0。
    public double Progress { get; internal set; }
    // Running 时可选管线阶段文案；Failed 时错误信息。宿主原样展示。
    public string? Message { get; internal set; }

    // 该次派生对应的持久记录键（= 内容缓存 key，也是 part 记录账本的键）。
    internal string CacheKey { get; }
    // 提交时冻结的位置无关输入快照；进 worker 前持有、跑完即释放（含解码 PCM，及时置 null 省内存）。
    internal FrozenAudioDerivationInput? Input { get; set; }
    // 提交所在的数据线程上下文；worker 完成后 marshal 回它更新任务态。
    internal SynchronizationContext? SyncContext { get; set; }
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
