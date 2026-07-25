using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Configs;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Derivers;

// deriver 运行时管理器（记录模型）：把「一次派生」建模为源 part 上的持久【记录】+ 与之绑定的运行时【在飞态】。
//
// 提交即落记录（键 = 内容缓存 key，提交时即可算）：记录随工程存、非撤销，承载溯源（引擎 / 参数 / 时刻 / 标签）；
// 结果只进本地内容寻址缓存、不入工程。缓存命中 => 记录可应用；缓存缺失（换机 / 淘汰 / 未跑完就存了工程）=> 已失效、源在可重跑。
//
// 任务 = 记录的在飞生命周期：排队→运行中→失败，原地演进；成功完成即移除任务（记录 + 缓存承载之）。
// 全局队列：并发上限 = Settings.MaxParallelDerivationTasks（默认 1，派生模型昂贵）。位置无关：天然扛住多任务 / part 移动。
//
// 提交 / 完成 / 应用 / 删记录全不压 undo 命令——派生是与音乐编辑正交的后台作业（见 undo 对称原则）；删记录只移引用（缓存共享、不删文件）。
internal static class DerivationTaskManager
{
    // 在飞任务（排队 / 运行中 / 失败）；成功完成的任务已移除，其派生由 part 记录 + 缓存承载。
    public static IReadOnlyList<DerivationTask> Tasks => mTasks;
    // 任务列表 / 任一任务状态变化（面板据此重建）。记录账本的增删另经各 part 的 DerivationRecordsChanged。
    public static IActionEvent Changed => mChanged;

    // ── 侧栏 tab 提示点（全局「有可处理 / 新」）──
    // 亮起判据 = 本会话有未查看的新完成（派生刚跑完、变可应用）或有失败任务待处理。打开 Derivation tab 即清「未查看」。
    // 运行中 / 排队不计入（还没得处理）；旧会话遗留的可应用记录也不计入（非「新」）——与徽标各司其职（徽标才逐 part 常驻指示）。
    public static bool HasActionable
    {
        get
        {
            if (mUnseenCompletion)
                return true;
            foreach (var task in mTasks)
                if (task.State == DerivationTaskState.Failed)
                    return true;
            return false;
        }
    }

    // 打开 Derivation tab 时调用：清「未查看的新完成」标志（失败任务另有其常驻性，须显式处理才消）。
    public static void NotifyTabOpened()
    {
        if (mUnseenCompletion)
        {
            mUnseenCompletion = false;
            mChanged.Invoke();
        }
    }

    // 解析一条记录的呈现状态（徽标 / 侧栏共用）：有在飞任务则取其态（排队 / 运行中 / 失败）；
    // 否则缓存命中 = 可应用，缺失 = 已失效（源在可重跑）。out task 供调用方取进度 / 消息。
    public static DerivationRecordStatus ResolveStatus(IAudioPart source, string cacheKey, out DerivationTask? task)
    {
        task = FindTask(source, cacheKey);
        if (task != null)
            return task.State switch
            {
                DerivationTaskState.Queued => DerivationRecordStatus.Queued,
                DerivationTaskState.Running => DerivationRecordStatus.Running,
                _ => DerivationRecordStatus.Failed,
            };
        return AudioDerivationCacheManager.Contains(cacheKey) ? DerivationRecordStatus.Applicable : DerivationRecordStatus.Invalidated;
    }

    // on-part 徽标的聚合态（读 part 记录账本 + 在飞任务 + 缓存，无持久 alloc）：记录数（全部记录）+ 主导态（定色）
    // + 批次进度。主导优先级：失败 > 可应用 > 运行中 > 排队 > 已失效（越需用户注意越优先）。
    //
    // runningProgress（进度填充分数）= 【当前波次批次进度】= (本波已完成数 + Σ运行中进度) / 本波总任务数；无活动波次 => -1。
    // 波次（Wave，逐 part）：提交时 Total++；波内单任务完成不减分母、只把它计入 Done；直到该 part 在飞任务全清才整波归零。
    //   => 一波多任务 = 一条平滑 0→1（完成不回跳），分母不含历史已完成记录（不被挤压），全清后徽标转「完成态满亮底」。
    public static bool TryGetPartBadge(IAudioPart part, out int count, out DerivationRecordStatus dominant, out double runningProgress)
    {
        count = 0;
        dominant = DerivationRecordStatus.Invalidated;
        int best = int.MaxValue;
        double runningSum = 0;
        foreach (var kvp in part.DerivationRecords)
        {
            count++;
            var status = ResolveStatus(part, kvp.Key, out var task);
            if (status == DerivationRecordStatus.Running && task != null)
                runningSum += task.Progress;
            int rank = DominanceRank(status);
            if (rank < best)
            {
                best = rank;
                dominant = status;
            }
        }
        runningProgress = mWaves.TryGetValue(part, out var wave) && wave.Total > 0
            ? (wave.Done + runningSum) / wave.Total
            : -1;
        return count > 0;
    }

    static int DominanceRank(DerivationRecordStatus status) => status switch
    {
        DerivationRecordStatus.Failed => 0,
        DerivationRecordStatus.Applicable => 1,
        DerivationRecordStatus.Running => 2,
        DerivationRecordStatus.Queued => 3,
        _ => 4,   // Invalidated
    };

    static DerivationTask? FindTask(IAudioPart source, string cacheKey)
    {
        foreach (var task in mTasks)
            if (ReferenceEquals(task.Source, source) && task.CacheKey == cacheKey)
                return task;
        return null;
    }

    // 当前并发上限：设置 <1 视为 1（派生默认串行）。
    static int Cap
    {
        get
        {
            int configured = Settings.MaxParallelDerivationTasks.Value;
            return configured > 0 ? configured : 1;
        }
    }

    // 提交一次派生（数据线程）：冻结源内容快照 + 算缓存键 => 即刻给源 part 落持久记录。
    // 缓存已命中：该次派生即刻可应用，不建运行时任务、不跑模型。否则建 Queued 任务入队，由 PumpQueue 择机起跑。
    // 喂的是源 part 的整段解码内容（位置无关）；裁剪 / 落点在应用时按当前几何处理。
    public static void Submit(IAudioPart source,
        string engineId, string engineDisplayName, string taskLabel, PropertyObject properties)
    {
        var input = FrozenAudioDerivationInput.Create(source, properties, out var contentHash);

        var packageId = DeriversManager.GetActivePackageId(engineId) ?? string.Empty;
        var version = ExtensionManager.PackageVersion(packageId);
        var key = AudioDerivationCacheManager.ComputeKey(contentHash, engineId, version, properties);

        // 启动即落持久记录（提交时 key 已可算）——随工程存、非撤销。同键 = 同一次派生的幂等重触发（覆盖刷新时刻/参数）。
        source.AddDerivationRecord(key, new DerivationRecordInfo
        {
            EngineId = engineId,
            EngineDisplayName = engineDisplayName,
            Parameters = properties,
            StartTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Label = taskLabel,
        });

        // 缓存已命中：结果现成，记录即刻可应用，不建任务、不跑模型。
        if (AudioDerivationCacheManager.Contains(key))
        {
            mChanged.Invoke();   // 侧栏据 part 记录呈现「可应用」
            return;
        }

        var task = new DerivationTask(engineId, engineDisplayName, taskLabel, source, key)
        {
            Input = input,
            SyncContext = SynchronizationContext.Current,
        };
        mTasks.Add(task);
        WaveAdd(source);   // 计入本 part 当前波次
        mChanged.Invoke();
        PumpQueue();
    }

    // 起跑排队任务直到占满并发上限（数据线程）。提交后 / 每次任务收尾后调用。
    static void PumpQueue()
    {
        foreach (var task in mTasks)
        {
            if (mRunningCount >= Cap)
                return;
            if (task.State == DerivationTaskState.Queued)
                StartRun(task);
        }
    }

    static void StartRun(DerivationTask task)
    {
        task.State = DerivationTaskState.Running;
        mRunningCount++;
        mChanged.Invoke();

        var input = task.Input!;
        var syncContext = task.SyncContext;
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
                var engine = DeriversManager.GetInitedEngine(task.EngineId);
                if (engine == null)
                    throw new Exception(string.Format("Deriver engine '{0}' is unavailable (init failed or not registered).", task.EngineId));
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

            Post(syncContext, () => OnCompleted(task, result, error));
        });
    }

    // 取消排队 / 运行中任务：请求取消 → Discard（移除任务 + 移除源 part 记录，与提交时的落记录对称回滚）。
    // 排队中（未起跑）直接就地丢弃；运行中请求取消、待 worker 尽力返回 null 后由 OnCompleted 收尾。
    public static void Cancel(DerivationTask task)
    {
        switch (task.State)
        {
            case DerivationTaskState.Queued:
                task.Input = null;
                mTasks.Remove(task);
                WaveDrop(task.Source);
                task.Source.RemoveDerivationRecord(task.CacheKey);
                mChanged.Invoke();
                break;
            case DerivationTaskState.Running:
                task.Cancellation.Cancel();   // 实际移除在 OnCompleted（result==null 分支）
                break;
            case DerivationTaskState.Failed:
                DiscardFailed(task);          // 失败态「取消」即消解该失败任务（记录留成已失效，可重跑）
                break;
        }
    }

    // 消解一个失败任务：仅移除运行时任务，保留持久记录（缓存缺失 => 呈现已失效，源在可重跑）。
    public static void DiscardFailed(DerivationTask task)
    {
        if (task.State == DerivationTaskState.Failed && mTasks.Remove(task))
            mChanged.Invoke();
    }

    // 删除一条持久派生记录（非撤销）：只移除源 part 上的引用；缓存内容寻址、跨 part/工程共享，绝不删缓存文件。
    // 若该记录尚有在飞任务（不应发生——可删记录均为已完成/已失效），一并丢弃对应任务。
    public static void DeleteRecord(IAudioPart source, string cacheKey)
    {
        for (int i = mTasks.Count - 1; i >= 0; i--)
        {
            var task = mTasks[i];
            if (ReferenceEquals(task.Source, source) && task.CacheKey == cacheKey)
            {
                bool wasQueued = task.State == DerivationTaskState.Queued;
                if (task.State == DerivationTaskState.Running)
                    task.Cancellation.Cancel();   // 波次扣除留给其 OnCompleted（result==null）
                task.Input = null;
                mTasks.RemoveAt(i);
                if (wasQueued)
                    WaveDrop(source);             // 排队任务无 OnCompleted，移除后就地扣除（失败态已在失败时扣过）
            }
        }
        source.RemoveDerivationRecord(cacheKey);
        mChanged.Invoke();
    }

    // 应用一条记录（数据线程，可反复）：按 (source, cacheKey) 从内容缓存取结果，换算 tick 并作一条栈顶 undo 命令并入工程。
    // 不移除记录（同一记录可重复应用）。缓存缺失 => 返回 CacheAvailable=false（已失效，调用方提示重跑）。
    public static ApplyResult Apply(IAudioPart source, string cacheKey, IProject project, DerivedResultApplier.Options options)
    {
        if (!AudioDerivationCacheManager.TryGet(cacheKey, out var result))
            return new ApplyResult(false, 0);

        var sourceTrack = ResolveSourceTrack(project, source);
        var tempo = project.TempoManager;
        // 产物时间是「源音频内容秒」（0 = 文件起点 = 源 part 锚点 Pos）。apply 侧两件几何：
        //   anchor = Pos 的工程秒（内容秒 t → 工程秒 = anchor + t）；[cropStart, cropEnd] = 裁剪窗口（内容秒）。
        double anchorSeconds = tempo.GetTime(source.Pos.Value);
        double sourceDuration = source.SampleRate > 0 ? source.SourceSampleCount / (double)source.SampleRate : 0;
        double cropStart = Math.Max(0, tempo.GetTime(source.StartPos()) - anchorSeconds);
        double cropEnd = Math.Min(sourceDuration, tempo.GetTime(source.EndPos()) - anchorSeconds);
        int newTracks = DerivedResultApplier.Apply(project, sourceTrack, anchorSeconds, cropStart, cropEnd, result, options);
        return new ApplyResult(true, newTracks);
    }

    // 源 part 落点解析：源仍在工程 → 其当前所在轨；已删 → null（应用侧回退追加末尾）。
    static ITrack? ResolveSourceTrack(IProject project, IAudioPart source)
    {
        foreach (var track in project.Tracks)
            foreach (var part in track.Parts)
                if (ReferenceEquals(part, source))
                    return track;
        return null;
    }

    static void OnCompleted(DerivationTask task, DerivedResult? result, Exception? error)
    {
        mRunningCount = Math.Max(0, mRunningCount - 1);
        task.Input = null;   // 释放冻结的解码 PCM

        if (error != null)
        {
            task.State = DerivationTaskState.Failed;
            task.Message = error.Message;
            Log.ErrorAttributed(string.Format("Deriver engine {0} derive failed", task.EngineId), error);
            WaveDrop(task.Source);   // 失败不再期待完成，从波次扣除（失败任务留 mTasks 供查看/重试，但不占进度分母）
            // 记录已持久存在（提交时落）；缓存缺失 => 呈现「已失效」。失败任务留在会话列表供查看 / 重触发。
            mChanged.Invoke();
            PumpQueue();
            return;
        }

        if (result == null)
        {
            // 取消：移除任务 + 移除记录（用户显式放弃、与提交对称）。
            mTasks.Remove(task);
            WaveDrop(task.Source);
            task.Source.RemoveDerivationRecord(task.CacheKey);
            mChanged.Invoke();
            PumpQueue();
            return;
        }

        // 成功：只写缓存；记录已存在 => 变为可应用。任务功成身退（记录 + 缓存承载之）。
        AudioDerivationCacheManager.Put(task.CacheKey, result);
        mTasks.Remove(task);
        WaveDone(task.Source);      // 计入本波已完成（波内不减分母；全清才整波归零）
        mUnseenCompletion = true;   // 新完成、待用户查看 => tab 圆点亮起（打开 tab 即清）
        mChanged.Invoke();
        PumpQueue();
    }

    static void Post(SynchronizationContext? context, Action action)
    {
        if (context != null)
            context.Post(_ => action(), null);
        else
            action();
    }

    // ── 逐 part 批次波次（会话态，仅供徽标进度分母；不持久）──
    // Total = 本波累计提交（会跑模型的）任务数，Done = 本波已成功完成数。取消/失败从 Total 扣除（不再期待其完成）。
    sealed class Wave { public int Total; public int Done; }
    static readonly Dictionary<IAudioPart, Wave> mWaves = new();

    static bool HasInflight(IAudioPart part)
    {
        foreach (var task in mTasks)
            if (ReferenceEquals(task.Source, part) && task.State is DerivationTaskState.Running or DerivationTaskState.Queued)
                return true;
        return false;
    }

    static void WaveAdd(IAudioPart part)
    {
        if (!mWaves.TryGetValue(part, out var wave))
            mWaves[part] = wave = new Wave();
        wave.Total++;
    }

    static void WaveDone(IAudioPart part)
    {
        if (!mWaves.TryGetValue(part, out var wave))
            return;
        wave.Done++;
        if (!HasInflight(part))
            mWaves.Remove(part);   // 整波清空 => 归零（徽标转完成态满亮底）
    }

    // 取消 / 失败：该任务不再期待完成，从波次预期里扣除；扣到不再有在飞任务即整波归零。
    static void WaveDrop(IAudioPart part)
    {
        if (!mWaves.TryGetValue(part, out var wave))
            return;
        wave.Total--;
        if (wave.Total <= wave.Done || !HasInflight(part))
            mWaves.Remove(part);
    }

    static int mRunningCount;
    static bool mUnseenCompletion;
    static readonly List<DerivationTask> mTasks = new();
    static readonly ActionEvent mChanged = new();
}

// 一条派生记录的呈现状态（徽标 / 侧栏共用）：在飞三态（排队 / 运行中 / 失败）+ 落地二态（可应用 / 已失效）。
internal enum DerivationRecordStatus
{
    Queued,       // 有在飞任务、等空槽
    Running,      // 有在飞任务、跑 Derive 中
    Failed,       // 有在飞任务、Derive 出错（记录仍在、可重跑）
    Applicable,   // 无在飞任务、缓存命中 => 可应用
    Invalidated,  // 无在飞任务、缓存缺失 => 已失效（源在可重跑）
}

// 应用一条记录的结局：CacheAvailable=false => 缓存缺失（已失效）；否则 NewTrackCount = 落地新轨数。
internal readonly record struct ApplyResult(bool CacheAvailable, int NewTrackCount);
