using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Derivers;

// deriver 任务的运行时管理器（中央任务面板的权威数据源）：持有全部运行中 / 待应用 / 失败任务，会话态、不持久。
// 提交 = 数据线程冻结输入 + 与源解耦、工程零改动（不压 undo 命令）；完成进待应用态；用户显式「应用」才作
// 一条普通栈顶 undo 命令落地（§5.3–5.4）。位置无关：天然扛住多任务 / part 移动 / part 删除。
internal static class DerivationTaskManager
{
    public static IReadOnlyList<DerivationTask> Tasks => mTasks;
    // 任务列表 / 任一任务状态变化（面板据此重建）。
    public static IActionEvent Changed => mChanged;

    // 提交一个派生任务（数据线程）。物化冻结输入、算缓存键：命中即直接进待应用（跳过模型）；否则 offload worker 跑 Derive。
    // 喂的是源 part 的整段解码内容（位置无关）；裁剪/落点在应用时按当前几何处理。
    public static DerivationTask Submit(IAudioPart source,
        string engineId, string engineDisplayName, string taskLabel, PropertyObject properties)
    {
        var input = FrozenAudioDerivationInput.Create(source, properties, out var contentHash);

        var packageId = DeriversManager.GetActivePackageId(engineId) ?? string.Empty;
        var version = ExtensionManager.PackageVersion(packageId);
        var key = AudioDerivationCacheManager.ComputeKey(contentHash, engineId, version, properties);

        var task = new DerivationTask(engineId, engineDisplayName, taskLabel, source, key);
        mTasks.Add(task);

        // 缓存命中：阶段一结果秒出，直接进待应用，不跑模型。
        if (AudioDerivationCacheManager.TryGet(key, out var cached))
        {
            task.Result = cached;
            task.State = DerivationTaskState.PendingApply;
            mChanged.Invoke();
            return task;
        }

        mChanged.Invoke();

        // 数据线程 dispatcher：捕获当前 SynchronizationContext，worker 完成后 marshal 回它更新任务态。
        var syncContext = SynchronizationContext.Current;
        // 进度：Progress<T> 在此（数据线程）构造，回调自动 post 回本上下文。
        var progress = new Progress<DerivationProgress>(p =>
        {
            task.Progress = p.Progress;
            task.Message = p.Message;
            mChanged.Invoke();
        });

        _ = Task.Run(async () =>
        {
            DerivedResult? result = null;
            Exception? error = null;
            try
            {
                var engine = DeriversManager.GetInitedEngine(engineId);
                if (engine == null)
                    throw new Exception(string.Format("Deriver engine '{0}' is unavailable (init failed or not registered).", engineId));
                result = await engine.Derive(input, progress, task.Cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                result = null;   // 取消当正常结局：返回 null、按取消处理
            }
            catch (Exception ex)
            {
                error = ex;
            }

            Post(syncContext, () => OnCompleted(task, key, result, error));
        });

        return task;
    }

    // 取消运行中任务：请求取消 → Derive 尽力返回 null → 丢弃任务（工程从未被改、无 undo 可虑）。
    public static void Cancel(DerivationTask task)
    {
        if (task.State == DerivationTaskState.Running)
            task.Cancellation.Cancel();
        // 实际移除在 OnCompleted（result==null 分支）；若已完成/失败，Discard 处理。
    }

    // 丢弃任务（待应用 / 失败态）：从列表移除，工程不动（结果仍在缓存、可重触发）。
    public static void Discard(DerivationTask task)
    {
        if (mTasks.Remove(task))
            mChanged.Invoke();
    }

    // 显式应用（数据线程）：结果 marshal 回后已在 task.Result；此处换算 tick、作一条栈顶 undo 命令并入工程。
    // 源 part 仍在工程 → 落其轨之下；已删 → 追加末尾。应用后移除任务（结果留缓存，重触发再产新 part）。
    // 返回落地新轨数（调用方据此提示 no-op / 源已删）。
    public static int Apply(DerivationTask task, IProject project, DerivedResultApplier.Options options)
    {
        if (task.State != DerivationTaskState.PendingApply || task.Result == null)
            return 0;

        var sourceTrack = ResolveSourceTrack(project, task.Source);
        var src = task.Source;
        var tempo = project.TempoManager;
        // 产物时间是「源音频内容秒」（0 = 文件起点 = 源 part 锚点 Pos）。apply 侧两件几何（源已删仍用其最后几何、回退落点）：
        //   anchor = Pos 的工程秒（内容秒 t → 工程秒 = anchor + t）；[cropStart, cropEnd] = 裁剪窗口（内容秒）。
        double anchorSeconds = tempo.GetTime(src.Pos.Value);
        double sourceDuration = src.SampleRate > 0 ? src.SourceSampleCount / (double)src.SampleRate : 0;
        double cropStart = Math.Max(0, tempo.GetTime(src.StartPos()) - anchorSeconds);
        double cropEnd = Math.Min(sourceDuration, tempo.GetTime(src.EndPos()) - anchorSeconds);
        int newTracks = DerivedResultApplier.Apply(project, sourceTrack, anchorSeconds, cropStart, cropEnd, task.Result, options);

        mTasks.Remove(task);
        mChanged.Invoke();
        return newTracks;
    }

    // 源 part 身份落点解析：源仍在工程 → 其当前所在轨；已删 → null（应用侧回退追加末尾）。
    static ITrack? ResolveSourceTrack(IProject project, IAudioPart source)
    {
        foreach (var track in project.Tracks)
            foreach (var part in track.Parts)
                if (ReferenceEquals(part, source))
                    return track;
        return null;
    }

    static void OnCompleted(DerivationTask task, string key, DerivedResult? result, Exception? error)
    {
        if (error != null)
        {
            task.State = DerivationTaskState.Failed;
            task.Message = error.Message;
            Log.ErrorAttributed(string.Format("Deriver engine {0} derive failed", task.EngineId), error);
            mChanged.Invoke();
            return;
        }

        if (result == null)
        {
            // 取消或什么都没产出 → 丢弃任务（工程从未被改）。
            mTasks.Remove(task);
            mChanged.Invoke();
            return;
        }

        AudioDerivationCacheManager.Put(key, result);
        task.Result = result;
        task.Progress = 1;
        task.State = DerivationTaskState.PendingApply;
        mChanged.Invoke();
    }

    static void Post(SynchronizationContext? context, Action action)
    {
        if (context != null)
            context.Post(_ => action(), null);
        else
            action();
    }

    static readonly List<DerivationTask> mTasks = new();
    static readonly ActionEvent mChanged = new();
}
