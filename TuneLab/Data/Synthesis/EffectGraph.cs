using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Extensions.Effect;
using TuneLab.Foundation;
using TuneLab.I18N;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.Data.Synthesis;

// 「effect 实例 × 段」反应式处理器图：voice 交付的每个音频段是一棵下游树的根——每级 effect 对每个输入段
// 配一个厚 IEffectSynthesisSession 会话节点，节点产出分段自由（1→N，如按静音切分的 splitter），
// 各输出段各自成为下一级节点的输入；输入面恒单段（消费单元 = 宿主的失效/调度/身份/错误半径粒度）。
//
// 模型（段间彼此无共享上下文，分别处理后由消费端按时间混音）：
//   · 失效判定权归宿主（SDK 零上报义务）：本段输入重 Commit / 本 effect 参数 settled 变更 /
//     本 effect 自动化变更区间与本段相交 → 保守标该节点 Pending（编辑批量经 part 合成批量收口，
//     BatchEnd 一次性放行）；引擎在 Process 内自比缓存早退去重（不重 Commit 即跳过下游）。
//   · 调度：跨段/跨 part 并行，受 EffectTaskGate（Settings.MaxParallelSynthesisTasks）全局封顶；
//     按播放线就近挑 Pending 节点。树尾各输出段汇为 SynthesizedSegments（消费端按绝对时间混音）。
//   · bypass / 引擎缺失的 effect 不建节点（该级整体 passthrough，输入直接喂下一级 / 树尾）。
//   · 状态图层：树的调度事实（Final/Interim/Degraded）与各活动节点的声称经 CollectDisplaySegments
//     按 z 序交给管线拼装（画家算法，见 SynthesisDisplaySegment）。
//
// 线程纪律：除 processor.Process 内部 offload 的 worker 与 StatusChanged（任意线程触发、此处 marshal）外，
// 全部成员仅数据线程访问（活视图纪律）。
internal sealed class EffectGraph : IDisposable
{
    public EffectGraph(MidiPart part, IAudioSegmentHost segments, CancellationToken cancellation, Action onChanged, Action onSettled)
    {
        mPart = part;
        mSegments = segments;
        mCancellation = cancellation;
        mOnChanged = onChanged;
        mOnSettled = onSettled;
        mSyncContext = SynchronizationContext.Current ?? throw new InvalidOperationException("EffectGraph must be created on the data thread.");
        mPumpCallback = RequestSchedule;
        mOnVoiceSegmentsChanged = RequestSchedule;
        mSegments.AudioSegmentsChanged += mOnVoiceSegmentsChanged;
        RebuildStructureSubscriptions();
        Schedule();
    }

    // 链尾各段（工程率 + 波形）；播放/波形按段消费、按绝对时间混音。原子换引用、跨线程读安全。
    public IReadOnlyList<SynthesizedSegment> SynthesizedSegments => mSynthesizedSegments;

    // 某个 effect 的回显曲线（聚合其所有「effect × 段」节点 processor 的 SynthesizedParameters、按 key 拼接 Segments）。
    // 无该 effect 或无回显时返回空 map。原子换引用、跨线程读安全。
    public IReadOnlyMap<string, SynthesizedParameter> GetSynthesizedParameters(IEffect effect)
        => mEffectReadbacks.TryGetValue(effect, out var map) ? map : EmptyReadback;

    // 仍有在飞 Process（用于管线延迟销毁：voice/effect 都归后才销毁会话与图）。
    public bool IsBusy => mRunningCount > 0;

    // 把 effect 侧的状态带图层按 z 序追加进 output（数据线程；画家算法——底层在前，管线再拼 voice 声称）：
    //   ① 声称完成（Claimed，软绿垫底）：活动会话自报的 Synthesized 段（非最终，会被事实覆盖）；
    //   ② 事实层（每树）：干净完成 → 各树尾段范围 Final（亮绿只能由此产生；1→N 时天然多段）；
    //      定局但有级失败 → Degraded（琥珀，passthrough 降级，范围 = 树已知内容之并）；
    //      在飞/截断 → Interim（软绿，已提交的头段/中间产物之并——重处理期间"当前听到的内容"就在这里）；
    //   ③ 活动声称（输出到 activeClaims，由管线在 voice Pending 之上拼装）：**所有**活动节点各自发声称
    //      （会话自报或宿主按调度事实兜底：输入范围整段 Synthesizing、无进度、点名 effect 与级序）；
    //      按级序降序排好——后画在上 ⇒ 最早级的声称最上层（上游在跑意味着下游必将重跑，
    //      它是该时刻最有信息量的陈述）。
    public void CollectDisplaySegments(List<SynthesisDisplaySegment> output, List<SynthesisDisplaySegment> activeOutput)
    {
        if (mTrees.Count == 0)
            return;

        // ① 声称完成垫底 + 收集 ③ 的条目（分流输出，保证 z 序：Claimed 全部在事实层之下）。
        List<(int Stage, SynthesisDisplaySegment Seg)>? activeClaims = null;
        foreach (var tree in mTrees)
        {
            foreach (var node in tree.Nodes)
            {
                if (!(node.Pending || node.Running || node.Removed))
                    continue;

                var claims = node.Session?.Status;
                if (claims == null || claims.Count == 0)
                {
                    // 兜底：调度事实（该节点在跑/待跑）→ 输入范围整段合成中、无进度。
                    (activeClaims ??= new()).Add((node.StageIndex, new SynthesisDisplaySegment
                    {
                        StartTime = node.Input.StartTime,
                        EndTime = node.Input.EndTime,
                        State = SynthesisDisplayState.Synthesizing,
                        Message = StageMessage(node),
                    }));
                    continue;
                }

                foreach (var claim in claims)
                {
                    if (claim.EndTime <= claim.StartTime)
                        continue;
                    if (claim.Status == SynthesisSegmentStatus.Synthesized)
                    {
                        output.Add(new SynthesisDisplaySegment
                        {
                            StartTime = claim.StartTime,
                            EndTime = claim.EndTime,
                            State = SynthesisDisplayState.Claimed,
                            Message = claim.Message,
                        });
                        continue;
                    }
                    (activeClaims ??= new()).Add((node.StageIndex, new SynthesisDisplaySegment
                    {
                        StartTime = claim.StartTime,
                        EndTime = claim.EndTime,
                        State = claim.Status switch
                        {
                            SynthesisSegmentStatus.Pending => SynthesisDisplayState.Pending,
                            SynthesisSegmentStatus.Failed => SynthesisDisplayState.Failed,
                            _ => SynthesisDisplayState.Synthesizing,
                        },
                        Progress = claim.Progress.Limit(0, 1),
                        Message = string.IsNullOrEmpty(claim.Message) ? StageMessage(node) : claim.Message,
                    }));
                }
            }
        }

        // ② 事实层（每树）。
        foreach (var tree in mTrees)
        {
            bool anyActive = tree.Truncated;
            EffectNode? firstErrored = null;
            foreach (var node in tree.Nodes)
            {
                if (node.Pending || node.Running || node.Removed)
                    anyActive = true;
                if (node.Errored)
                    firstErrored ??= node;
            }

            if (!anyActive && firstErrored == null)
            {
                // 树干净完成：各树尾段范围 = 亮绿事实。
                foreach (var tailInput in tree.TailInputs)
                {
                    if (tailInput.EndTime > tailInput.StartTime)
                        output.Add(new SynthesisDisplaySegment
                        {
                            StartTime = tailInput.StartTime,
                            EndTime = tailInput.EndTime,
                            State = SynthesisDisplayState.Final,
                        });
                }
                continue;
            }

            // 树已知内容之并（头段 + 各节点输入 + 树尾段——覆盖全部已提交中间产物）。
            double start = tree.Head.StartTime;
            double end = tree.Head.EndTime;
            foreach (var node in tree.Nodes)
            {
                start = Math.Min(start, node.Input.StartTime);
                end = Math.Max(end, node.Input.EndTime);
            }
            foreach (var tailInput in tree.TailInputs)
            {
                start = Math.Min(start, tailInput.StartTime);
                end = Math.Max(end, tailInput.EndTime);
            }
            if (end <= start)
                continue;

            if (!anyActive)
            {
                // 定局且有级失败：降级（可播 passthrough，非无声）。
                string message = string.Format("Effect {0} failed, playing unprocessed audio".Tr(TrContext),
                    EffectManager.GetDisplayName(firstErrored!.Effect.Type));
                if (!string.IsNullOrEmpty(firstErrored.ErrorMessage))
                    message += "\n" + firstErrored.ErrorMessage;
                output.Add(new SynthesisDisplaySegment
                {
                    StartTime = start,
                    EndTime = end,
                    State = SynthesisDisplayState.Degraded,
                    Message = message,
                });
            }
            else
            {
                // 在飞/截断：已提交内容待（在）下游处理。
                output.Add(new SynthesisDisplaySegment
                {
                    StartTime = start,
                    EndTime = end,
                    State = SynthesisDisplayState.Interim,
                    Message = "Processing effects".Tr(TrContext),
                });
            }
        }

        // ③ 活动声称分流输出：级序降序（后画在上 ⇒ 最早级最上层）。
        if (activeClaims != null)
        {
            activeClaims.Sort((a, b) => b.Stage.CompareTo(a.Stage));
            foreach (var (_, seg) in activeClaims)
                activeOutput.Add(seg);
        }
    }

    string StageMessage(EffectNode node)
        => string.Format("Processing effect: {0} ({1}/{2})".Tr(TrContext),
            EffectManager.GetDisplayName(node.Effect.Type), node.StageIndex + 1, mTotalStages);

    // 链结构变化（增删/重排/启用切换）：重建启用-effect 订阅 + 重排图（reconcile 自然弃旧建新）。
    public void OnStructureChanged()
    {
        if (mDisposed)
            return;
        RebuildStructureSubscriptions();
        Schedule();
    }

    // 工程采样率变了：各 voice 输入快照按新率重做（清缓存版本强制重采）+ 全图重处理。
    public void OnSampleRateChanged()
    {
        if (mDisposed)
            return;

        // 清各 voice 上游的"已观测源版本"，迫使 reconcile 按新工程率重采快照并标 Input 变。
        foreach (var upstream in mVoiceUpstreams.Values)
            upstream.InvalidateSnapshot();
        // 链上各级输出经其上游 Input 变级联重处理；此处只需驱动一次调度。
        Schedule();
    }

    public void Dispose()
    {
        if (mDisposed)
            return;
        mDisposed = true;

        mSegments.AudioSegmentsChanged -= mOnVoiceSegmentsChanged;
        EffectTaskGate.Unregister(mPumpCallback);
        mStructureSubscriptions?.DisposeAll();

        // 在飞节点收尾时自行销毁（RunNode 见 mDisposed）；非在飞节点立即销毁。
        foreach (var node in new List<EffectNode>(mNodes.Values))
        {
            if (node.Running)
                node.RequestDisposeWhileRunning();
            else
                DisposeAndRemove(node);
        }
        mVoiceUpstreams.Clear();
    }

    // —— 调度（reconcile + pump，循环至稳定、防重入）——

    // 请求一趟调度：已在调度中（如同步完成的 Process 重入、空槽唤醒、reconcile 中补发的 ProcessingRequested）
    // 只置脏让当前循环再跑一遍 reconcile；否则起一趟新调度。
    void RequestSchedule()
    {
        if (mInSchedule)
        {
            mDirty = true;
            return;
        }
        Schedule();
    }

    void Schedule()
    {
        if (mDisposed || mInSchedule)
            return;

        mInSchedule = true;
        try
        {
            // 循环至稳定：同步引擎的 Process 在 Pump 内同步完成 → 置脏 → 本循环下一趟 reconcile 接其输出、
            // 级联拉起下游节点并 pump，整条链一趟内跑完（异步引擎完成时 mInSchedule 已释、自起新调度）。
            int guard = 0;
            do
            {
                mDirty = false;
                Reconcile();
                Pump();
            }
            while (mDirty && !mDisposed && ++guard < 10000);

            if (guard >= 10000)
                Log.Error("EffectGraph schedule did not converge (possible runaway ProcessingRequested).");
        }
        finally
        {
            mInSchedule = false;
        }
        // 链尾产物可能已变（reconcile 重算/节点收尾），统一刷新 UI（重绘幂等，UI 自行节流）。
        mOnChanged();
    }

    // 由当前真相重算图：每个已提交 voice 段是一棵树的根——逐级拉起/复用节点、把每个已提交输出段
    // 接为下一级各节点的输入（1→N：某级可产出多段，如 splitter）；某节点尚无产出则该分支截断
    // （下游节点待其产出后再物化）。顺便记账树结构（供状态图层派生）。
    // 弃不再被任何树需要的节点；最后算树尾段集。
    void Reconcile()
    {
        var active = RecomputeActiveEffects();

        // 树根输入 = 已提交的 voice 段（按工程率快照）。
        var heads = CollectVoiceUpstreams();

        var desired = new HashSet<(IEffect, object)>();
        var trees = new List<Tree>(heads.Count);
        var tail = new List<UpstreamSegment>(heads.Count);
        foreach (var head in heads)
        {
            var tree = new Tree(head);
            var inputs = new List<UpstreamSegment> { head };
            for (int k = 0; k < active.Count; k++)
            {
                var effect = active[k];
                var engine = EffectManager.GetInitedEngine(effect.Type);
                if (engine == null)
                    continue;   // RecomputeActiveEffects 已过滤，理论不至；防御。

                var next = new List<UpstreamSegment>();
                foreach (var input in inputs)
                {
                    var node = GetOrCreateNode(effect, engine, input);
                    node.StageIndex = k;
                    desired.Add((effect, input.SourceKey));
                    tree.Nodes.Add(node);
                    if (node.Removed)
                    {
                        // 旧节点在飞待毁：本分支暂无可信输出（收尾销毁后下轮 reconcile 重建）。
                        tree.Truncated = true;
                        continue;
                    }

                    var outputs = node.RefreshDownstreamInputs();
                    if (outputs.Count == 0)
                        tree.Truncated = true;   // 本节点尚未产出（首跑在跑/待跑）：下游待产出后物化。
                    next.AddRange(outputs);
                }
                inputs = next;
            }
            tree.TailInputs.AddRange(inputs);
            trees.Add(tree);
            // 树尾段 = 已产出分支的尾段（截断分支自然缺席：首跑留白；重跑时旧输出仍已提交、播放陈旧内容）。
            tail.AddRange(inputs);
        }
        mTrees = trees;
        mTotalStages = active.Count;

        // 弃不再被需要的节点（在飞者延迟到收尾销毁）。
        RemoveNodesNotIn(desired);

        // 输入重提交的存活节点：宿主直接标 Pending（失效判定权归宿主；插件侧 Input.Committed 事件
        // 已随快照刷新发出，仅作缓存提示）。新建节点本就 Pending。
        foreach (var node in mNodes.Values)
        {
            if (node.Removed)
                continue;
            int version = node.Input.CommitVersion;
            if (version != node.LastInputVersion)
            {
                node.LastInputVersion = version;
                node.Pending = true;
            }
        }

        BuildSynthesizedSegments(tail);
        BuildEffectReadbacks();
    }

    // 在并发上限内派发 Pending 就绪节点（按播放线就近）；满则登记待空槽重 pump。
    void Pump()
    {
        if (mDisposed)
            return;

        var ready = new List<EffectNode>();
        foreach (var node in mNodes.Values)
        {
            if (!node.Removed && node.Pending && !node.Running && node.Input.CommitVersion > 0 && node.Input.SampleCount > 0)
                ready.Add(node);
        }

        if (ready.Count > 1)
            SortByPlaybackProximity(ready);

        foreach (var node in ready)
        {
            if (!EffectTaskGate.TryAcquire())
            {
                EffectTaskGate.WaitForSlot(mPumpCallback);
                break;
            }
            RunNode(node);
        }
    }

    // 跑一个节点的 Process（让出点）：取消/失败都正常收尾——失败该段 passthrough（输出 = 输入）。
    async void RunNode(EffectNode node)
    {
        node.Pending = false;
        node.Running = true;
        mRunningCount++;
        var linked = CancellationTokenSource.CreateLinkedTokenSource(mCancellation);
        node.Cancellation = linked;

        bool errored = false;
        try
        {
            await node.Session.Process(linked.Token);
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed(string.Format("Effect {0} process failed", node.Effect.Type), ex);
            node.ErrorMessage = ex.Message;   // 供状态带降级文案（Degraded pill 可复制）
            errored = true;
        }
        finally
        {
            node.Errored = errored;     // 先于 Release：唤醒的等待者 reconcile 时即读到正确 passthrough 态
            if (!errored)
                node.ErrorMessage = null;
            node.Running = false;
            node.Cancellation = null;
            linked.Dispose();
            mRunningCount--;
            EffectTaskGate.Release();   // 唤醒等待者（各自重 pump）
        }

        // 图销毁中：销毁本节点；若是最后一个在飞任务则回调管线重检销毁（voice/effect 都归才销毁会话）。
        if (mDisposed)
        {
            DisposeAndRemove(node);
            mOnSettled();
            return;
        }

        // 已被弃（输入消失等）：销毁本节点、不据其输出级联。否则正常收尾据新输出段级联下游。
        if (node.Removed)
            DisposeAndRemove(node);

        // reconcile 据新输出段级联下游 + 刷新链尾 + pump 其余。Process 同步完成时本调用在 Pump 内重入，
        // RequestSchedule 只置脏由当前循环再跑一趟；异步完成时起新调度。
        RequestSchedule();
    }

    // —— 图维护 ——

    List<IEffect> RecomputeActiveEffects()
    {
        var active = new List<IEffect>();
        foreach (var effect in mPart.Effects)
        {
            if (!effect.IsEnabled.Value)
                continue;
            if (EffectManager.GetInitedEngine(effect.Type) == null)
                continue;   // 引擎缺失/Init 失败 = 该级 passthrough。
            active.Add(effect);
        }
        return active;
    }

    // voice 段 → 工程率上游快照（按握柄缓存、CommitVersion 变才重采）；消失的段弃其上游。
    // 身份保持规则：**未提交只是瞬态，不是消失**——Resize/渐进写造成的未提交窗口（可跨异步渲染）里，
    // 上游对象与其旧快照原样保留（下游继续消费上一版已提交内容，节点不重建）；只有段从登记表移除才弃上游。
    List<UpstreamSegment> CollectVoiceUpstreams()
    {
        var present = new HashSet<AudioSegment>();
        var result = new List<UpstreamSegment>();
        foreach (var segment in mSegments.AudioSegments)
        {
            present.Add(segment);
            if (!mVoiceUpstreams.TryGetValue(segment, out var upstream))
            {
                if (!segment.IsCommitted || segment.Samples.Length == 0)
                    continue;   // 尚无已提交内容：不建上游（从未有货的段不占图身份）
                upstream = new UpstreamSegment(segment);
                mVoiceUpstreams.Add(segment, upstream);
            }
            // 工程率快照：源已提交且 CommitVersion 变（或采样率变后失效）才重采新缓冲；
            // 同时取走写入区间账本、缩放到工程率绝对轴传导给下游。
            if (segment.IsCommitted && upstream.SourceVersion != segment.CommitVersion && segment.Samples.Length > 0)
            {
                int engineRate = AudioEngine.SampleRate.Value;
                float[] resampled = segment.SampleRate == engineRate
                    ? (float[])segment.Samples.Clone()
                    : AudioUtils.Resample(segment.Samples, 1, segment.SampleRate, engineRate);
                long offset = (long)Math.Round((double)segment.SampleOffset / segment.SampleRate * engineRate);
                var ranges = ScaleRanges(segment.TakeChangedRanges(), segment.SampleRate, engineRate);
                bool forceWhole = upstream.SourceVersion < 0;   // 首建 / 采样率变更强制失效：内容整体重采，如实整段
                upstream.UpdateCommitted(offset, engineRate, resampled, segment.CommitVersion, ranges, forceWhole);
            }
            if (upstream.CommitVersion > 0)
                result.Add(upstream);
        }

        if (mVoiceUpstreams.Count > present.Count)
        {
            var stale = new List<AudioSegment>();
            foreach (var key in mVoiceUpstreams.Keys)
            {
                if (!present.Contains(key))
                    stale.Add(key);
            }
            foreach (var key in stale)
                mVoiceUpstreams.Remove(key);
        }
        return result;
    }

    // 写入区间账本的采样率换算（native 绝对轴 → 工程率绝对轴；floor/ceil 保守外扩）。
    static List<(long Start, int Count)>? ScaleRanges(List<(long Start, int Count)>? ranges, int srcRate, int dstRate)
    {
        if (ranges == null || srcRate == dstRate || srcRate <= 0)
            return ranges;
        var scaled = new List<(long Start, int Count)>(ranges.Count);
        foreach (var (start, count) in ranges)
        {
            long s = (long)Math.Floor((double)start * dstRate / srcRate);
            long e = (long)Math.Ceiling((double)(start + count) * dstRate / srcRate);
            scaled.Add((s, (int)Math.Min(int.MaxValue, Math.Max(0, e - s))));
        }
        return scaled;
    }

    EffectNode GetOrCreateNode(IEffect effect, IEffectSynthesisEngine engine, UpstreamSegment input)
    {
        var key = (effect, input.SourceKey);
        if (mNodes.TryGetValue(key, out var node))
            return node;

        var context = new EffectContext(mPart, effect, input);
        node = new EffectNode(effect, context, input);
        try
        {
            node.Session = engine.CreateSession(context);
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed(string.Format("Effect {0} create processor failed", effect.Type), ex);
        }
        // 失效判定权归宿主：context 的作用域信号（参数 settled 变更 / 自动化变更区间与本段相交，
        // 批量经合成批量收口）汇到 DirtySink → 标本节点 Pending 并调度。
        context.DirtySink = () => OnNodeDirty(node);
        node.StatusChangedHandler = () => OnProcessorStatusChanged(node);
        node.ParametersChangedHandler = () => OnSessionParametersChanged(node);
        if (node.Session != null)
        {
            node.Session.StatusChanged.Subscribe(node.StatusChangedHandler);
            node.Session.SynthesizedParametersChanged.Subscribe(node.ParametersChangedHandler);
        }
        node.Pending = true;
        node.LastInputVersion = input.CommitVersion;
        mNodes.Add(key, node);
        // Debug 可观测口：节点建/毁昭示 processor 生命周期——inpainting 类 voice（贯穿段就地覆写）编辑期应零建毁，
        // 块式 voice（丢旧建新段）每次重合成伴随一轮建毁。
        Log.Debug(string.Format("Effect node created: {0} x segment@{1:F2}s", effect.Type, input.StartTime));

        // 无 processor（创建失败）→ 该段 passthrough。
        if (node.Session == null)
            node.Errored = true;
        return node;
    }

    void RemoveNodesNotIn(HashSet<(IEffect, object)> desired)
    {
        List<(IEffect, object)>? stale = null;
        foreach (var kv in mNodes)
        {
            if (desired.Contains(kv.Key) || kv.Value.Removed)
                continue;
            (stale ??= new()).Add(kv.Key);
        }
        if (stale == null)
            return;

        foreach (var key in stale)
        {
            var node = mNodes[key];
            if (node.Running)
                node.RequestDisposeWhileRunning();   // 收尾时由 RunNode 销毁
            else
                DisposeAndRemove(node);
        }
    }

    // 节点的作用域信号收口（数据线程；来自其 context 的 DirtySink）：保守标 Pending、调度。
    void OnNodeDirty(EffectNode node)
    {
        if (mDisposed || node.Removed)
            return;
        node.Pending = true;
        RequestSchedule();
    }

    // 处理器状态声称变化（SDK 允许任意线程触发）：marshal 回数据线程驱动 UI 刷新（重绘幂等自节流），
    // 声称内容在图层派生时经 GetStatus 拉取。
    void OnProcessorStatusChanged(EffectNode node)
    {
        mSyncContext.Post(_ =>
        {
            if (mDisposed || node.Removed)
                return;
            mOnChanged();
        }, null);
    }

    // 会话回显更新（SDK 允许任意线程触发；与声称信号分离——进度 tick 不惊动回显重聚合）：
    // marshal 回数据线程重聚合该 effect 的回显并刷新。长耗时引擎中途渐进发布的回显由此即时可见；
    // 仅在 Process 收尾发布的引擎可不触发（Process 归位后 reconcile 本就重聚合一次，兜底时机）。
    void OnSessionParametersChanged(EffectNode node)
    {
        mSyncContext.Post(_ =>
        {
            if (mDisposed || node.Removed)
                return;
            BuildEffectReadbacks();
            mOnChanged();
        }, null);
    }

    void DisposeAndRemove(EffectNode node)
    {
        mNodes.Remove((node.Effect, node.Input.SourceKey));
        node.Dispose();
        Log.Debug(string.Format("Effect node disposed: {0} x segment@{1:F2}s", node.Effect.Type, node.Input.StartTime));
    }

    // 链尾输入段集 → SynthesizedSegments（工程率 + 波形，按上游版本缓存，只重算变化段）。
    void BuildSynthesizedSegments(List<UpstreamSegment> tail)
    {
        var list = new List<SynthesizedSegment>(tail.Count);
        foreach (var upstream in tail)
        {
            var seg = upstream.GetSynthesizedSegment();
            if (seg.Audio.Samples is { Length: > 0 })
                list.Add(seg);
        }
        mSynthesizedSegments = list.ToArray();
    }

    // 各 effect 回显聚合：遍历有 processor 的存活节点，按其 Effect 归组、按 key 把各段 Segments 拼起来，
    // 段按起始秒升序（满足 SynthesizedParameter「按时间升序」契约——节点字典无序，拼接后须排）。
    void BuildEffectReadbacks()
    {
        Dictionary<IEffect, Dictionary<string, List<IReadOnlyList<Point>>>>? acc = null;
        foreach (var node in mNodes.Values)
        {
            if (node.Removed || node.Session == null)
                continue;
            var readback = node.Session.SynthesizedParameters;
            if (readback.Count == 0)
                continue;

            acc ??= new();
            if (!acc.TryGetValue(node.Effect, out var perEffect))
            {
                perEffect = new();
                acc.Add(node.Effect, perEffect);
            }
            foreach (var kv in readback)
            {
                if (kv.Value.Segments.Count == 0)
                    continue;
                if (!perEffect.TryGetValue(kv.Key, out var segments))
                {
                    segments = new();
                    perEffect.Add(kv.Key, segments);
                }
                segments.AddRange(kv.Value.Segments);
            }
        }

        if (acc == null)
        {
            mEffectReadbacks = EmptyEffectReadbacks;
            return;
        }

        var result = new Map<IEffect, IReadOnlyMap<string, SynthesizedParameter>>();
        foreach (var (effect, perEffect) in acc)
        {
            var map = new Map<string, SynthesizedParameter>();
            foreach (var (key, segments) in perEffect)
            {
                segments.Sort((a, b) => FirstX(a).CompareTo(FirstX(b)));
                map.Add(key, new SynthesizedParameter { Segments = segments });
            }
            result.Add(effect, map);
        }
        mEffectReadbacks = result;
    }

    static double FirstX(IReadOnlyList<Point> segment) => segment.Count > 0 ? segment[0].X : double.PositiveInfinity;

    void SortByPlaybackProximity(List<EffectNode> nodes)
    {
        double currentTime = AudioEngine.CurrentTime;
        // 播放线之后的段优先（升序最早开始）；其后是线前的段（降序最晚开始，离播放线最近）。
        nodes.Sort((a, b) =>
        {
            double sa = a.Input.StartTime, sb = b.Input.StartTime;
            bool aheadA = sa >= currentTime, aheadB = sb >= currentTime;
            if (aheadA != aheadB)
                return aheadA ? -1 : 1;
            return aheadA ? sa.CompareTo(sb) : sb.CompareTo(sa);
        });
    }

    // 启用-effect 订阅：启用切换 = 结构变（建/弃该级节点），与增删/重排同走重排。
    void RebuildStructureSubscriptions()
    {
        mStructureSubscriptions?.DisposeAll();
        mStructureSubscriptions = new DisposableManager();
        foreach (var effect in mPart.Effects)
            effect.IsEnabled.Modified.Subscribe(OnStructureChanged, mStructureSubscriptions);
    }

    readonly MidiPart mPart;
    readonly IAudioSegmentHost mSegments;
    readonly CancellationToken mCancellation;
    readonly Action mOnChanged;
    readonly Action mOnSettled;
    readonly SynchronizationContext mSyncContext;
    readonly Action mPumpCallback;
    readonly Action mOnVoiceSegmentsChanged;

    readonly Dictionary<(IEffect Effect, object InputKey), EffectNode> mNodes = new();
    readonly Dictionary<AudioSegment, UpstreamSegment> mVoiceUpstreams = new();
    List<Tree> mTrees = [];
    int mTotalStages;
    DisposableManager? mStructureSubscriptions;

    // 状态带文案翻译上下文（toml [SynthesisStatus] 节）。
    const string TrContext = "SynthesisStatus";

    SynthesizedSegment[] mSynthesizedSegments = [];
    IReadOnlyMap<IEffect, IReadOnlyMap<string, SynthesizedParameter>> mEffectReadbacks = EmptyEffectReadbacks;
    static readonly IReadOnlyMap<IEffect, IReadOnlyMap<string, SynthesizedParameter>> EmptyEffectReadbacks = new Map<IEffect, IReadOnlyMap<string, SynthesizedParameter>>();
    static readonly IReadOnlyMap<string, SynthesizedParameter> EmptyReadback = new Map<string, SynthesizedParameter>();
    int mRunningCount;
    bool mInSchedule;
    bool mDirty;
    bool mDisposed;

    // —— 一棵树的记账（每个已提交 voice 段一棵）：树根 + 已物化的全部节点（各级、可分叉）+ 树尾段集，
    //    供状态图层派生读取。Truncated = 有分支尚无产出（下游节点待其产出后才物化）。——
    sealed class Tree(UpstreamSegment head)
    {
        public UpstreamSegment Head { get; } = head;
        public List<EffectNode> Nodes { get; } = new();
        public List<UpstreamSegment> TailInputs { get; } = new();
        public bool Truncated;
    }

    // —— 单个「effect × 段」节点：处理器 + 上下文 + 调度状态 + 输出段→下游上游 的映射 ——
    sealed class EffectNode
    {
        public IEffect Effect { get; }
        public EffectContext Context { get; }
        public UpstreamSegment Input { get; }
        public IEffectSynthesisSession? Session;
        public Action? StatusChangedHandler;
        public Action? ParametersChangedHandler;

        public bool Pending;
        public bool Running;
        public bool Removed;
        public bool Errored;            // 处理失败/无处理器 → 本段 passthrough（输出 = 输入）
        public string? ErrorMessage;    // 最近一次失败的异常消息（成功后清空；供降级文案）
        public int StageIndex;          // 在启用链中的级序（每轮 reconcile 刷新；声称文案 (k+1)/N 用）
        public int LastInputVersion;
        public CancellationTokenSource? Cancellation;

        public EffectNode(IEffect effect, EffectContext context, UpstreamSegment input)
        {
            Effect = effect;
            Context = context;
            Input = input;
        }

        // 本节点供下游的输入段集：失败/无处理器 → 直传输入（passthrough）；
        // 否则 = 各已提交输出段（包成上游，1→N 自由）；尚未产出 → 空（本分支截断）。
        public IReadOnlyList<UpstreamSegment> RefreshDownstreamInputs()
        {
            if (Errored)
                return mPassthrough ??= new[] { Input };
            return Context.RefreshOutputs();
        }

        public void RequestDisposeWhileRunning()
        {
            Removed = true;
            Cancellation?.Cancel();
        }

        public void Dispose()
        {
            Removed = true;
            if (Session != null)
            {
                if (StatusChangedHandler != null)
                    Session.StatusChanged.Unsubscribe(StatusChangedHandler);
                if (ParametersChangedHandler != null)
                    Session.SynthesizedParametersChanged.Unsubscribe(ParametersChangedHandler);
                try { Session.Dispose(); }
                catch (Exception ex) { Log.ErrorAttributed("Effect processor dispose failed", ex); }
                Session = null;
            }
            Context.Dispose();
        }

        UpstreamSegment[]? mPassthrough;
    }

    // —— 上游音频段（直接实现 SDK 音频面 IEffectSynthesisAudio，经 context.Input 暴露）：
    //    voice 输出或上一级 effect 输出的只读视图。读取为「区间 + 调用方缓冲」copy-out（同步前缀物化）；
    //    宿主存储形态是内部细节（当前 flat 数组，可无缝换分页）。已提交内容为按源 CommitVersion 拷的快照
    //    （重 Commit 换新缓冲）。
    //    本副本是**提交闸门的物化，必须存在**：生产者的段缓冲是就地渐进写（Commit 前的写只供进度/波形），
    //    「已提交内容」要独立于活缓冲就必须有这份拷贝——不可"优化"成上游引用。副本引用也**从不外借**
    //    （Read 是唯一窄门）：这保住将来按脏区间原地增量更新本副本的自由（消整段克隆），外借引用与原地更新不能共存。
    //    同一实例可同时喂多个节点（如同段上的多个第一级 effect），事件共享无碍。——
    sealed class UpstreamSegment : IEffectSynthesisAudio
    {
        // 图键：底层源对象身份（voice 段握柄 / 上一级输出段）——节点按此稳定缓存。
        public object SourceKey { get; }

        public UpstreamSegment(object sourceKey)
        {
            SourceKey = sourceKey;
        }

        public long SampleOffset { get; private set; }
        public int SampleCount => mSamples.Length;
        public int SampleRate { get; private set; }
        public void Read(int offset, Span<float> destination) => mSamples.AsSpan(offset, destination.Length).CopyTo(destination);
        public int CommitVersion { get; private set; }
        // 内容变更的区间账本 (start, count)，start 为**全局采样位置**（绝对轴，SDK 契约；数据线程）：
        // 重 Commit 时按上游实际写入的区间上报（源账本经 TakeChangedRanges 传导），局部重合成引擎据此收窄重算量。
        public IActionEvent<long, int> RangeModified => mRangeModified;
        readonly ActionEvent<long, int> mRangeModified = new();

        public double StartTime => SampleRate > 0 ? (double)SampleOffset / SampleRate : 0;
        public double EndTime => SampleRate > 0 ? StartTime + (double)SampleCount / SampleRate : StartTime;

        // 已观测的源 CommitVersion（CollectVoiceUpstreams / RefreshOutputs 据此判是否需重拷快照）。
        public int SourceVersion { get; private set; } = -1;

        // changedRanges = 源侧取走的写入+几何账本（绝对采样位置、须与本快照同率）。账本是**完备**的
        // （内容变更必经 Write 记账、几何变更经 Resize 对称差记账），故 null = 真无变更（如无写空提交）→ 静默。
        // forceWhole = 强制失效（首建快照 / 工程采样率变更整体重采——每个样本值都变了）→ 如实整段。
        // 区间**不钳段界、原样上报**：越出当前段界的区间 = 被裁剪掉的内容（绝对轴上「从有到无」也是内容
        // 变更）——下游外扩自己的上下文窗后与自身范围求交、自决重算量（边界邻域必须能被如实唤醒）。
        public void UpdateCommitted(long sampleOffset, int sampleRate, float[] snapshot, int sourceVersion, IReadOnlyList<(long Start, int Count)>? changedRanges, bool forceWhole)
        {
            SampleOffset = sampleOffset;
            SampleRate = sampleRate;
            mSamples = snapshot;
            SourceVersion = sourceVersion;
            CommitVersion++;
            mCachedVersion = -1;   // 链尾段缓存失效

            if (forceWhole)
            {
                mRangeModified.Invoke(SampleOffset, snapshot.Length);
                return;
            }
            if (changedRanges == null)
                return;
            foreach (var (start, count) in changedRanges)
            {
                if (count > 0)
                    mRangeModified.Invoke(start, count);
            }
        }

        public void InvalidateSnapshot() => SourceVersion = -1;

        // 链尾段产物（工程率 + 波形），按本上游 CommitVersion 缓存。
        public SynthesizedSegment GetSynthesizedSegment()
        {
            if (mCachedVersion == CommitVersion && mCached.Waveform != null)
                return mCached;

            int engineRate = AudioEngine.SampleRate.Value;
            float[] samples = mSamples;
            int rate = SampleRate;
            double startTime = StartTime;
            if (rate != engineRate && samples.Length > 0)
            {
                samples = AudioUtils.Resample(samples, 1, rate, engineRate);
                rate = engineRate;
            }
            var audio = new MonoAudio(startTime, rate, samples);
            mCached = new SynthesizedSegment(audio, new Waveform(samples));
            mCachedVersion = CommitVersion;
            return mCached;
        }

        float[] mSamples = [];
        int mCachedVersion = -1;
        SynthesizedSegment mCached;
    }

    // —— 输出段握柄（SDK 面 IAudioSegment）：processor 写入并 Commit 的产物；与 voice 段同语义
    //    （写入区间账本 + Resize 身份保持的几何扩展/裁剪）。宿主据其 CommitVersion 把已提交内容拷成
    //    下游上游快照并传导账本。语义整改换新仍 Dispose 即从节点输出集摘除。——
    sealed class OutputSegment : IAudioSegment
    {
        public OutputSegment(EffectContext owner, long sampleOffset, int sampleCount, int sampleRate)
        {
            mOwner = owner;
            SampleOffset = sampleOffset;
            SampleRate = sampleRate;
            mSamples = new float[Math.Max(0, sampleCount)];
        }

        public long SampleOffset { get; private set; }
        public int SampleRate { get; }
        public float[] Samples => mSamples;
        public bool IsCommitted { get; private set; }
        public int CommitVersion { get; private set; }

        public void Write(int offset, ReadOnlySpan<float> samples)
        {
            samples.CopyTo(mSamples.AsSpan(offset));
            WriteRangeLedger.Record(ref mChangedRanges, SampleOffset + offset, samples.Length);
            IsCommitted = false;
        }

        public void Commit()
        {
            IsCommitted = true;
            CommitVersion++;
        }

        // 就地改几何、身份不变（SDK 契约，与 voice 的 AudioSegment.Resize 同语义）：交集内容按全局采样位置
        // 对齐保留，新增区域清零；回未提交态。几何对称差入账（几何变更对下游是上下文变更，如实通知）。
        public void Resize(long sampleOffset, int sampleCount)
        {
            sampleCount = Math.Max(0, sampleCount);
            WriteRangeLedger.RecordSymmetricDifference(ref mChangedRanges, SampleOffset, SampleOffset + mSamples.Length, sampleOffset, sampleOffset + sampleCount);
            var resized = new float[sampleCount];
            long copyStart = Math.Max(SampleOffset, sampleOffset);
            long copyEnd = Math.Min(SampleOffset + mSamples.Length, sampleOffset + sampleCount);
            if (copyEnd > copyStart)
                Array.Copy(mSamples, (int)(copyStart - SampleOffset), resized, (int)(copyStart - sampleOffset), (int)(copyEnd - copyStart));
            mSamples = resized;
            SampleOffset = sampleOffset;
            IsCommitted = false;
        }

        // 取走累积的写入区间账本（绝对采样位置，本段率；下游快照刷新时取走即清）。null = 本轮无记账。
        public List<(long Start, int Count)>? TakeChangedRanges()
        {
            var ranges = mChangedRanges;
            mChangedRanges = null;
            return ranges;
        }

        public void Dispose() => mOwner.RemoveOutput(this);

        readonly EffectContext mOwner;
        float[] mSamples;
        List<(long Start, int Count)>? mChangedRanges;
    }

    // —— IEffectSynthesisContext 宿主实现：绑定「该 effect × 一个上游段」、随节点死。
    //    失效判定权在此落地：作用域信号（参数 settled 变更 / 自动化变更区间与本段相交）经批量收口
    //    汇到 DirtySink（→ 图标 Pending 并调度）；颗粒事件照发给插件，仅作缓存提示。——
    sealed class EffectContext : IEffectSynthesisContext, IDisposable
    {
        public EffectContext(MidiPart part, IEffect effect, UpstreamSegment input)
        {
            mPart = part;
            mEffect = effect;
            mInput = input;
            mBatchSignal = part.SynthesisBatch;

            // 标脏时机经 part 合成批量收口（与 voice 的 VoiceSynthesisContext 对称，避免用 effect.Modified
            // 聚合事件——其在滑条拖拽 merge 中乱发、又不在 merge 收口补发，导致触发与终值错位、滞后一拍）：
            //   · 参数：effect.Properties.Modified（settled，滑条拖拽经 DataObject merge 收到松手才发）——
            //     mProperties 在 re-raise Modified（插件缓存提示）后回调 MarkDirty，顺序确定；
            //   · 自动化：各轨 RangeModified（每步同步发，但绘制操作处于合成批量内 → 收口到 BatchEnd 一次），
            //     与本段时间界相交才标脏（不相交的变更不惊动本节点——通用相交判定归宿主，不再各插件自抄）。
            // 批量中只记 pending、BatchEnd 一次性放行；不在批量则即时放行。
            mProperties = new LivePropertyObject(effect.Properties, MarkDirty);
            mOnBatchEnd = OnBatchEnd;
            mBatchSignal.BatchEnd += mOnBatchEnd;
            WireAutomations();
            WireVibratoTracking();
        }

        // 作用域信号收口后的去处（图在建节点时接线 → 标该节点 Pending + 调度）。
        public Action? DirtySink;

        // 输入音频面：内部 UpstreamSegment 直接实现 SDK 接口（含区间账本 RangeModified），零转发。
        public IEffectSynthesisAudio Input => mInput;

        public IReadOnlyNotifiablePropertyObject Properties => mProperties;

        // 已声明连续 automation 轨只读 map：轨集 = effect 声明的 AutomationConfigs 中非分段者；
        // 代理按 key 缓存、缺则补建；每次读重建 map 以反映当前声明集。
        public IReadOnlyMap<string, ISynthesisAutomation> Automations
        {
            get
            {
                var map = new Map<string, ISynthesisAutomation>();
                foreach (var kvp in mEffect.AutomationConfigs)
                {
                    if (kvp.Value.IsPiecewise)
                        continue;
                    string key = kvp.Key.Id;
                    if (!mAutomationProxies.TryGetValue(key, out var proxy))
                    {
                        proxy = new AutomationProxy(mPart, mEffect, key);
                        mAutomationProxies.Add(key, proxy);
                    }
                    map.Add(key, proxy);
                }
                return map;
            }
        }

        public IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate)
        {
            // 登记表语义（与 voice 同构）：产出分段自由（1→N，如 splitter），各段独立 Write/Commit/Dispose。
            var segment = new OutputSegment(this, sampleOffset, sampleCount, sampleRate);
            mOutputs.Add(segment);
            return segment;
        }

        // 跨线程冻结快照（仅同步前缀；音频不在此列——Input.Read 自物化）。
        public EffectSynthesisSnapshot GetSnapshot(double startTime, double endTime)
            => EffectSynthesisSnapshotFactory.Capture(mPart, mEffect, startTime, endTime);

        // 标脏收口：批量中（如 automation 绘制的 BeginMergeDirty 作用域）只记 pending，BatchEnd 一次性放行；
        // 非批量（如滑条松手时的 Properties.Modified）即时放行。
        void MarkDirty()
        {
            if (mBatchSignal.IsBatching)
            {
                mPendingDirty = true;
                return;
            }
            FlushDirty();
        }

        void OnBatchEnd()
        {
            if (!mPendingDirty)
                return;
            mPendingDirty = false;
            FlushDirty();
        }

        void FlushDirty()
        {
            try { DirtySink?.Invoke(); }
            catch (Exception ex) { Log.ErrorAttributed("Effect dirty sink threw", ex); }
        }

        // 把输出段同步成下游上游集（按各输出段 CommitVersion 重拷快照）。
        // 身份保持规则（与 CollectVoiceUpstreams 对称）：**未提交只是瞬态，不是消失**——Resize/渐进写的
        // 未提交窗口里，已有上游与其旧快照原样保留（下游继续消费上一版、节点不重建）；从未有货的段不进下游。
        internal IReadOnlyList<UpstreamSegment> RefreshOutputs()
        {
            if (mOutputs.Count == 0)
                return Array.Empty<UpstreamSegment>();

            var result = new List<UpstreamSegment>(mOutputs.Count);
            foreach (var output in mOutputs)
            {
                if (!mOutputUpstreams.TryGetValue(output, out var upstream))
                {
                    if (!output.IsCommitted || output.Samples.Length == 0)
                        continue;   // 尚无已提交内容：不建上游
                    upstream = new UpstreamSegment(output);
                    mOutputUpstreams.Add(output, upstream);
                }
                // 同率直传写入账本（输出段与其下游快照同一采样率，无需换算）。
                if (output.IsCommitted && upstream.SourceVersion != output.CommitVersion && output.Samples.Length > 0)
                    upstream.UpdateCommitted(output.SampleOffset, output.SampleRate, (float[])output.Samples.Clone(), output.CommitVersion, output.TakeChangedRanges(), forceWhole: upstream.SourceVersion < 0);
                if (upstream.CommitVersion > 0)
                    result.Add(upstream);
            }
            return result;
        }

        internal void RemoveOutput(OutputSegment output)
        {
            mOutputs.Remove(output);
            mOutputUpstreams.Remove(output);
        }

        void WireAutomations()
        {
            mAutomationSubscriptions?.DisposeAll();
            mAutomationSubscriptions = new DisposableManager();
            // 轨集合变（条件轨显隐）→ 重接 + 视为收口。
            mEffect.Automations.MapModified.Subscribe(OnAutomationMapModified, mAutomationSubscriptions);
            foreach (var kv in mEffect.Automations)
            {
                string id = kv.Key;
                kv.Value.RangeModified.Subscribe(
                    (relStart, relEnd) => NotifyAutomationRange(id, relStart, relEnd), mAutomationSubscriptions);
                // 默认值平移 = 整轨全区间失效（与 voice 对称）。
                kv.Value.DefaultValue.Modified.Subscribe(
                    () => NotifyAutomationRange(id, double.NegativeInfinity, double.PositiveInfinity), mAutomationSubscriptions);
            }
        }

        void OnAutomationMapModified()
        {
            WireAutomations();
            MarkDirty();   // 轨集合变（条件轨显隐）：保守标脏。
        }

        // —— vibrato → effect 自动化的失效接线：颤音（几何/参数/影响表）变化时，若其现在或此前影响本 effect
        //    的某轨（AffectedEffectAutomations 按槽位切片），注入对应轨代理的区间提示并（与本段相交时）标脏。
        //    跟踪「此前影响集」是为覆盖解除关联/删除条目的场景——区间事件不携带变更源，取前后影响集之并保守失效。
        //    包络轨（VibratoEnvelope）变化对受影响轨的精确失效缓后（与 voice 侧同一在案取舍）。 ——

        void WireVibratoTracking()
        {
            foreach (var vibrato in mPart.Vibratos)
                WireVibrato(vibrato);
            ((IMidiPart)mPart).Vibratos.ItemAdded.Subscribe(WireVibrato, mVibratoListSubscriptions);
            ((IMidiPart)mPart).Vibratos.ItemRemoved.Subscribe(UnwireVibrato, mVibratoListSubscriptions);
        }

        void WireVibrato(Vibrato vibrato)
        {
            if (mVibratoSubscriptions.ContainsKey(vibrato))
                return;

            var subscriptions = new DisposableManager();
            // 两路都订：RangeModified = 几何/参数变化（voice/effect 共用），EffectAmplitudesModified =
            // effect 影响表变化（分源事件，只有 effect 链消费——不惊动 voice 正是它存在的意义）。
            vibrato.RangeModified.Subscribe((relStart, relEnd) => OnVibratoRangeModified(vibrato, relStart, relEnd), subscriptions);
            vibrato.EffectAmplitudesModified.Subscribe((relStart, relEnd) => OnVibratoRangeModified(vibrato, relStart, relEnd), subscriptions);
            mVibratoSubscriptions.Add(vibrato, subscriptions);
            var ids = AffectedIds(vibrato);
            mVibratoAffectedIds[vibrato] = ids;
            // 新增即偏移源出现（如粘贴带关联的颤音）：纯增删不自发区间事件，这里补发一次（与 voice 侧对称）。
            if (ids.Count > 0)
                NotifyVibratoRange(ids, vibrato.StartPos(), vibrato.EndPos());
        }

        void UnwireVibrato(Vibrato vibrato)
        {
            if (mVibratoSubscriptions.Remove(vibrato, out var subscriptions))
                subscriptions.DisposeAll();
            // 被删颤音原覆盖区间的偏移随之消失（Detach 后 Pos/Dur 仍可读）。
            if (mVibratoAffectedIds.Remove(vibrato, out var ids) && ids.Count > 0)
                NotifyVibratoRange(ids, vibrato.StartPos(), vibrato.EndPos());
        }

        void OnVibratoRangeModified(Vibrato vibrato, double relStartTick, double relEndTick)
        {
            var now = AffectedIds(vibrato);
            mVibratoAffectedIds.TryGetValue(vibrato, out var last);
            mVibratoAffectedIds[vibrato] = now;
            if (now.Count == 0 && (last == null || last.Count == 0))
                return;

            var union = now;
            if (last != null && last.Count > 0)
            {
                union = new HashSet<string>(now);
                union.UnionWith(last);
            }
            NotifyVibratoRange(union, relStartTick, relEndTick);
        }

        HashSet<string> AffectedIds(Vibrato vibrato)
        {
            var ids = new HashSet<string>();
            foreach (var kvp in vibrato.AffectedEffectAutomations)
            {
                if (kvp.Key.EffectId == mEffect.Id)
                    ids.Add(kvp.Key.Id);
            }
            return ids;
        }

        void NotifyVibratoRange(IEnumerable<string> ids, double relStartTick, double relEndTick)
        {
            double startSecond = RelTickToGlobalSecond(relStartTick);
            double endSecond = RelTickToGlobalSecond(relEndTick);
            foreach (var id in ids)
            {
                if (mAutomationProxies.TryGetValue(id, out var proxy))
                    proxy.NotifyRangeModified(startSecond, endSecond);
            }

            bool intersects = mInput.SampleCount == 0
                || (endSecond >= mInput.StartTime && startSecond <= mInput.EndTime);
            if (intersects)
                MarkDirty();
        }

        // part 相对 tick 区间 → 全局秒，注入对应轨代理的 RangeModified（插件缓存提示），
        // 并做宿主侧相交判定：变更区间与本段时间界相交才标脏（不相交的编辑不惊动本节点）。
        void NotifyAutomationRange(string id, double relStartTick, double relEndTick)
        {
            double startSecond = RelTickToGlobalSecond(relStartTick);
            double endSecond = RelTickToGlobalSecond(relEndTick);
            if (mAutomationProxies.TryGetValue(id, out var proxy))
                proxy.NotifyRangeModified(startSecond, endSecond);

            // 输入未就绪（空段）保守标脏——节点反正过不了 Pump 的就绪闸，Pending 无害。
            bool intersects = mInput.SampleCount == 0
                || (endSecond >= mInput.StartTime && startSecond <= mInput.EndTime);
            if (intersects)
                MarkDirty();
        }

        double RelTickToGlobalSecond(double relTick)
        {
            if (double.IsInfinity(relTick))
                return relTick;
            return mPart.TempoManager.GetTime(relTick + mPart.Pos.Value);
        }

        public void Dispose()
        {
            mBatchSignal.BatchEnd -= mOnBatchEnd;
            mAutomationSubscriptions?.DisposeAll();
            mVibratoListSubscriptions.DisposeAll();
            foreach (var subscriptions in mVibratoSubscriptions.Values)
                subscriptions.DisposeAll();
            mVibratoSubscriptions.Clear();
            mVibratoAffectedIds.Clear();
            mProperties.Dispose();
            mOutputs.Clear();
            mOutputUpstreams.Clear();
        }

        readonly MidiPart mPart;
        readonly IEffect mEffect;
        readonly UpstreamSegment mInput;
        readonly BatchSignal mBatchSignal;
        readonly LivePropertyObject mProperties;
        readonly Action mOnBatchEnd;
        bool mPendingDirty;
        readonly List<OutputSegment> mOutputs = new();
        readonly Dictionary<OutputSegment, UpstreamSegment> mOutputUpstreams = new();
        readonly Dictionary<string, AutomationProxy> mAutomationProxies = new();
        DisposableManager? mAutomationSubscriptions;
        readonly DisposableManager mVibratoListSubscriptions = new();
        readonly Dictionary<Vibrato, DisposableManager> mVibratoSubscriptions = new();
        readonly Dictionary<Vibrato, HashSet<string>> mVibratoAffectedIds = new();
    }

    // —— 该 effect 某条自动化轨的活视图：求值（全局秒 → 全局 tick → part 相对 tick → effect 取值）+ 区间订阅。
    //    取值是**终值口径**（基线/默认 + 该轨的 vibrato 偏移，槽位现场解析）——与 voice 活代理的
    //    GetFinalAutomationValues 同判例；快照侧（EffectSynthesisSnapshotFactory）冻结同一口径。——
    sealed class AutomationProxy(MidiPart part, IEffect effect, string automationID) : ISynthesisAutomation
    {
        public IActionEvent<double, double> RangeModified => mRangeModified;
        readonly ActionEvent<double, double> mRangeModified = new();

        public double[] Evaluate(IReadOnlyList<double> times)
        {
            double pos = part.Pos.Value;
            var ticks = part.TempoManager.GetTicks(times);
            for (int i = 0; i < ticks.Length; i++)
                ticks[i] -= pos;

            int index = ((IMidiPart)part).Effects.IndexOf(effect);
            if (index < 0)
                return effect.GetAutomationValues(ticks, automationID);
            return ((IMidiPart)part).GetFinalAutomationValues(ticks, AutomationKey.Effect(index, automationID));
        }

        internal void NotifyRangeModified(double startSecond, double endSecond)
        {
            try { mRangeModified.Invoke(startSecond, endSecond); }
            catch (Exception ex) { Log.ErrorAttributed("Effect automation range handler threw", ex); }
        }
    }

    // —— 只读属性活视图守卫：借壳 effect.Properties（短命随节点死、读时不跨线程），事件 re-raise 到自身。——
    sealed class LivePropertyObject : IReadOnlyNotifiablePropertyObject, IDisposable
    {
        public IActionEvent WillModify => mWillModify;
        public IActionEvent Modified => mModified;

        // onModified（可选）在 re-raise Modified 之后回调——供 owner 在颗粒脏（订阅 Modified 者）已置后再收口，
        // 顺序确定（不依赖跨接口的订阅注册次序）。仅根节点传入，子节点（Object(key)）不带。
        public LivePropertyObject(IReadOnlyNotifiablePropertyObject source, Action? onModified = null)
        {
            mSource = source;
            mOnWillModify = () => mWillModify.Invoke();
            mOnModified = () =>
            {
                mModified.Invoke();
                onModified?.Invoke();
            };
            mSource.WillModify.Subscribe(mOnWillModify);
            mSource.Modified.Subscribe(mOnModified);
        }

        public IReadOnlyNotifiablePropertyObject Object(string key)
        {
            if (!mChildren.TryGetValue(key, out var child))
            {
                child = new LivePropertyObject(mSource.Object(key));
                mChildren.Add(key, child);
            }
            return child;
        }

        public PropertyValue GetValue(string key, PropertyValue defaultValue) => mSource.GetValue(key, defaultValue);

        public void Dispose()
        {
            mSource.WillModify.Unsubscribe(mOnWillModify);
            mSource.Modified.Unsubscribe(mOnModified);
            foreach (var kv in mChildren)
                kv.Value.Dispose();
            mChildren.Clear();
        }

        readonly IReadOnlyNotifiablePropertyObject mSource;
        readonly Action mOnWillModify;
        readonly Action mOnModified;
        readonly ActionEvent mWillModify = new();
        readonly ActionEvent mModified = new();
        readonly Dictionary<string, LivePropertyObject> mChildren = new();
    }
}
