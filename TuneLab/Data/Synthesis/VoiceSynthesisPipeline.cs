using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using TuneLab.Audio;
using TuneLab.Extensions.Effect;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.Data.Synthesis;

// 一个 part 的合成管线宿主包装：持有会话级 context + ISynthesisSession，
// 对上提供调度面（peek/dispatch + 并发槽位状态）、音素回填，与按段效果链运行。
//
// 线程纪律：除标注外全部成员仅数据线程访问；session.StatusChanged 允许任意线程触发，
// 这里负责 marshal 回数据线程再对外转发。
//
// 效果链（按段增量，二维失效 段 × 级）：voice 经 context.CreateAudioSegment 交付的每个音频段各自独立过链。
// 每段每级持有一个持久 IEffectProcessor（cache[segment][stage]）：宿主把「自上次以来变了什么」（上游音频 /
// 哪些参数 / 哪条自动化的哪段秒区间）经 IEffectChange 告知处理器，引擎据此做内部级差分复用，宿主无从复制
// 引擎私有失效图。级联靠输出身份：某级重处理后若输出数组引用变了才把下游标脏，没变则下游整段复用、不重跑。
//   · voice 某段重 Commit → 该段 stage0 标「音频变」（保留处理器、引擎复用非音频态）；新段 → 建链全级 Initial。
//   · effect[i] 参数/启用变 → 该级标对应 key/启用脏；自动化变 → 该级标该轨秒区间脏。
//   · 链结构变（增删/重排）→ 各段弃处理器、从 0 重建。
// 段间串行（一次只跑一个 effect 处理，SVC 慢、避免资源爆）。链尾各段末级输出按时间拼成最终音频。
internal sealed class VoiceSynthesisPipeline : IDisposable
{
    // 状态/产物有更新（已 marshal 到数据线程），宿主 UI 收到直接刷新；区域信息看 GetStatus()。
    public event Action? StatusChanged;

    public ISynthesisSession Session => mSession;
    public bool IsBusy => mIsBusy;

    // 各已完成音频段（工程率，链尾输出 + 波形）；播放/波形按段消费，不再拼整 part 单条 buffer。
    public IReadOnlyList<SynthesizedSegment> SynthesizedSegments => mSynthesizedSegments;
    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => mSession.SynthesizedPitch;
    public IReadOnlyList<SynthesisStatusSegment> GetStatus() => mSession.GetStatus();

    public VoiceSynthesisPipeline(MidiPart part, string voiceType, string voiceId)
    {
        mPart = part;
        mSyncContext = SynchronizationContext.Current ?? throw new InvalidOperationException("VoiceSynthesisPipeline must be created on the data thread.");
        mContext = new SynthesisContext(part);
        mSession = VoicesManager.CreateSession(voiceType, voiceId, mContext);
        mOnSessionStatusChanged = () => mSyncContext.Post(_ =>
        {
            if (!mDisposed)
                StatusChanged?.Invoke();
        }, null);
        mSession.StatusChanged += mOnSessionStatusChanged;
        mOnAudioSegmentsChanged = ReconcileAndPump;
        mContext.AudioSegmentsChanged += mOnAudioSegmentsChanged;
        RebuildEffectSubscriptions();
    }

    // —— 调度面（Editor 驱动）——

    // 窗内"下一块待合成"的廉价 peek；仅会话空闲时有意义。窗口与返回边界为全局秒。
    // 调度窗先与 part 界求交再问插件：part 被裁短后留在界外的 note 不该被合成
    //（呈现端本就按 part 界裁剪音频）；纯宿主侧裁窗，零接口变化。跨界 block 仍整块合成。
    public SynthesisSegment? PeekNext(double startTime, double endTime)
    {
        if (mIsBusy || mDisposed)
            return null;

        startTime = Math.Max(startTime, mPart.TempoManager.GetTime(mPart.StartPos));
        endTime = Math.Min(endTime, mPart.TempoManager.GetTime(mPart.EndPos));
        if (endTime <= startTime)
            return null;

        try
        {
            return mSession.GetNextSegment(startTime, endTime);
        }
        catch (Exception ex)
        {
            Log.Error("GetNextSegment failed: " + ex);
            return null;
        }
    }

    // commit：与 peek 在同一调度 tick 内同步衔接；快照由插件在 SynthesizeNext 的同步前缀
    // 经 context.GetSnapshot 自行拉取。await 返回 = 槽位释放。
    public async void Dispatch(SynthesisSegment segment)
    {
        if (mIsBusy || mDisposed)
            return;

        mIsBusy = true;
        var progress = new Progress<double>(_ => { if (!mDisposed) StatusChanged?.Invoke(); });
        try
        {
            await mSession.SynthesizeNext(segment, progress, mCancellation.Token);
        }
        catch (Exception ex)
        {
            // 契约上取消/失败都正常返回；抛出即插件违约，宿主在调用边界 catch 兜底。
            Log.Error("SynthesizeNext threw: " + ex);
        }
        finally
        {
            mIsBusy = false;
        }

        if (mDisposed)
        {
            TryFinishDispose();
            return;
        }

        // 段产物经 context.AudioSegmentsChanged（Commit 时）已驱动各段链重跑；此处只回填音素 + 刷新。
        WriteBackPhonemes();
        StatusChanged?.Invoke();
    }

    // —— effect 失效入口（数据线程；由 MidiPart 转发）——

    // 某 effect 的参数/启用变化：在该 effect 链位标对应脏（参数按 key 细粒度 diff、启用整级重评）。
    // 自动化变化不走这里（经各轨 RangeModified 订阅按秒区间标脏）——此处对纯自动化变化为 no-op。
    public void SetEffectDirty(int index)
    {
        if (mDisposed || index < 0 || index >= mPart.Effects.Count)
            return;

        var effect = mPart.Effects[index];
        var current = effect.Properties.GetInfo();
        var changedKeys = DiffKeys(mLastProperties.GetValueOrDefault(effect), current);
        mLastProperties[effect] = current;

        bool enableChanged = !mLastEnabled.TryGetValue(effect, out var enabled) || enabled != effect.IsEnabled.Value;
        mLastEnabled[effect] = effect.IsEnabled.Value;

        if (changedKeys.Count == 0 && !enableChanged)
            return;

        foreach (var chain in mChains.Values)
        {
            if (index >= chain.Stages.Count)
                continue;
            var stage = chain.Stages[index];
            foreach (var key in changedKeys)
                stage.MarkProperty(key);
            if (enableChanged)
                stage.Dirty = true;
        }
        InvalidateAndPump();
    }

    // 链结构变化（增删/重排）：各段弃所有级处理器、按新链长从 0 重建（voice 输出已缓存、不重跑 voice）。
    public void OnEffectChainStructureChanged()
    {
        if (mDisposed)
            return;

        int count = mPart.Effects.Count;
        foreach (var chain in mChains.Values)
            chain.ResetStages(count);
        RebuildEffectSubscriptions();
        InvalidateAndPump();
    }

    // 工程采样率变了：各段 native 音频按新率重做适配 + 全部从头重跑（清空链缓存后 reconcile 重建）。
    public void OnSampleRateChanged()
    {
        if (mDisposed)
            return;

        mChainGeneration++;
        CancelEffectProcessing();
        mActiveChain = null;
        foreach (var chain in mChains.Values)
            chain.DisposeAll();
        mChains.Clear();
        ReconcileAndPump();
    }

    public void Dispose()
    {
        if (mDisposed)
            return;
        mDisposed = true;

        mCancellation.Cancel();
        mContext.AudioSegmentsChanged -= mOnAudioSegmentsChanged;
        mEffectSubscriptions?.DisposeAll();
        CancelEffectProcessing();
        mContext.Dispose();

        // 槽位在 await 真正返回时才释放：voice/effect 仍在飞则延迟到其收尾再销毁。
        TryFinishDispose();
    }

    // voice 与 effect 在飞都归后才销毁会话 + 各段处理器（不在飞时立即）。
    void TryFinishDispose()
    {
        if (!mDisposed || mIsBusy || mEffectBusy)
            return;

        foreach (var chain in mChains.Values)
            chain.DisposeAll();
        mChains.Clear();
        FinishDispose();
    }

    void FinishDispose()
    {
        mSession.StatusChanged -= mOnSessionStatusChanged;
        try
        {
            mSession.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Session dispose failed: " + ex);
        }
        mCancellation.Dispose();
    }

    // 合成音素回填到 note（UI 音素显示消费面）：扁平时间线按出身 note 归组。
    void WriteBackPhonemes()
    {
        try
        {
            var map = new Dictionary<INote, List<SynthesizedPhoneme>>();
            foreach (var phoneme in mSession.Phonemes)
            {
                if (phoneme.Note is not SynthesisContext.SynthesisNoteProxy proxy)
                    continue;

                if (!map.TryGetValue(proxy.Source, out var list))
                {
                    list = new List<SynthesizedPhoneme>();
                    map.Add(proxy.Source, list);
                }
                list.Add(phoneme);
            }
            foreach (var note in mPart.Notes)
            {
                note.SynthesizedPhonemes = map.TryGetValue(note, out var list) ? list.ToArray() : null;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Write back phonemes failed: " + ex);
        }
    }

    static MonoAudio ResampleToEngineRate(MonoAudio native)
    {
        int engineRate = AudioEngine.SampleRate.Value;
        if (native.SampleRate == engineRate)
            return native;

        var resampled = AudioUtils.Resample(native.Samples, 1, native.SampleRate, engineRate);
        return new MonoAudio(native.StartTime, engineRate, resampled);
    }

    // —— 按段效果链（cache[segment][stage]，段间串行）——

    // 段集 / 段内容变化（context.AudioSegmentsChanged 驱动）→ 同步各段链：新段建链（全级 Initial）、
    // 同握柄重 Commit 换输入 + 标 stage0 音频变（保留处理器）、移除已消失的段（弃其处理器）。命中活动段则中止其在飞处理。
    void ReconcileAndPump()
    {
        if (mDisposed)
            return;

        int nativeRate = mSession.SampleRate;
        int count = mPart.Effects.Count;
        bool invalidateActive = false;
        var present = new HashSet<SynthesisContext.AudioSegment>();
        foreach (var segment in mContext.AudioSegments)
        {
            if (!segment.IsCommitted || segment.Samples.Length == 0)
                continue;

            present.Add(segment);
            if (mChains.TryGetValue(segment, out var chain))
            {
                if (chain.ProcessedVersion == segment.CommitVersion)
                    continue;

                // 同握柄重 Commit：换输入 + 标 stage0 音频变。
                var input = ResampleToEngineRate(new MonoAudio((double)segment.SampleOffset / nativeRate, nativeRate, segment.Samples));
                chain.UpdateInput(input, segment.CommitVersion, count);
                if (ReferenceEquals(mActiveChain, chain))
                    invalidateActive = true;
            }
            else
            {
                var input = ResampleToEngineRate(new MonoAudio((double)segment.SampleOffset / nativeRate, nativeRate, segment.Samples));
                mChains[segment] = new SegmentChain(segment, input, segment.CommitVersion, count);
            }
        }

        if (mChains.Count > present.Count)
        {
            var stale = new List<SynthesisContext.AudioSegment>();
            foreach (var key in mChains.Keys)
            {
                if (!present.Contains(key))
                    stale.Add(key);
            }
            foreach (var key in stale)
            {
                if (ReferenceEquals(mActiveChain, mChains[key]))
                    invalidateActive = true;
                mChains[key].DisposeAll();
                mChains.Remove(key);
            }
        }

        if (invalidateActive)
        {
            mChainGeneration++;
            CancelEffectProcessing();
            mActiveChain = null;
        }
        PumpNext();
    }

    // 选下一个需要处理的段链（段间串行：一次只跑一个）。无则收尾拼装最终音频。
    // 在飞 Process 未归时不另起新段：等其续延归来再 pump——取消是协作式请求，槽位在 await 真正
    // 返回时才释放（不在请求取消时），故同时至多一个 effect 处理在跑，资源始终封顶。
    void PumpNext()
    {
        if (mDisposed || mEffectBusy)
            return;

        foreach (var chain in mChains.Values)
        {
            if (chain.NeedsWork())
            {
                mActiveChain = chain;
                chain.RunStage = 0;
                mChainGeneration++;
                ProcessActiveStage(mChainGeneration);
                return;
            }
        }

        mActiveChain = null;
        FinalizeChain();
    }

    // 处理活动段：从 stage0 推进游标，干净级（输入未变、有缓存输出）复用、脏级才过引擎。
    // 唯一让出点是引擎级的 Process await——其间的失效经 generation + mEffectBusy 处理。某级重处理后
    // 输出引用变了才把下一级标「音频变」（级联），没变则下游复用、不重跑。
    async void ProcessActiveStage(int generation)
    {
        if (generation != mChainGeneration || mDisposed || mActiveChain is not { } chain)
            return;

        var effects = mPart.Effects;
        int count = effects.Count;

        while (chain.RunStage < count && chain.RunStage < chain.Stages.Count)
        {
            int i = chain.RunStage;
            var stage = chain.Stages[i];
            var input = i == 0 ? chain.Input : chain.Stages[i - 1].Output;

            // 干净级：输入未变、输出有效 → 复用，同步推进。
            if (!stage.Dirty && stage.HasOutput)
            {
                chain.RunStage++;
                continue;
            }

            var effect = effects[i];
            var engine = effect.IsEnabled.Value ? EffectManager.GetInitedEngine(effect.Type) : null;
            if (engine == null)
            {
                // bypass / 引擎缺失 = passthrough（同步推进，无 await）。弃处理器、置 Initial（再启用从头）。
                stage.DisposeProcessor();
                stage.Initial = true;
                bool changedPt = CommitStageOutput(stage, input, input.Samples);
                stage.ClearPending();
                if (changedPt && i + 1 < count && i + 1 < chain.Stages.Count)
                    chain.Stages[i + 1].MarkAudio(SegStart(chain), SegEnd(chain));
                chain.RunStage++;
                continue;
            }

            // 脏的引擎级：异步 Process（让出点）。change 携带自上次以来的变化事实。
            var change = new StageChange(stage);
            var output = new EffectChainOutput();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(mCancellation.Token);
            bool errored = false;
            try
            {
                mEffectBusy = true;
                mEffectCancellation = cancellation;
                stage.Processor ??= engine.CreateProcessor();
                var inputObj = new EffectChainInput(input, mPart, effect);
                var progress = new Progress<double>(_ => { if (!mDisposed) StatusChanged?.Invoke(); });
                await stage.Processor.Process(inputObj, output, change, progress, cancellation.Token);
            }
            catch (Exception ex)
            {
                // 错误抛异常即在此 catch → 该级 passthrough；处理器可能已坏，弃之（下次重建 Initial）。
                Log.Error(string.Format("Effect {0} process failed: {1}", effect.Type, ex));
                errored = true;
                stage.DisposeProcessor();
                stage.Initial = true;
            }
            finally
            {
                mEffectBusy = false;
                if (ReferenceEquals(mEffectCancellation, cancellation))
                    mEffectCancellation = null;
                cancellation.Dispose();
            }

            // await 期间被取代 / 销毁：丢弃过期结果（不提交、不清 pending，待重排重算），转去 pump 新活。
            if (generation != mChainGeneration || mDisposed || mActiveChain is not { } current || !ReferenceEquals(current, chain))
            {
                if (mDisposed)
                    TryFinishDispose();
                else
                    PumpNext();
                return;
            }

            bool changed = errored
                ? CommitStageOutput(stage, input, input.Samples)
                : CommitStageOutput(stage, output.Audio, output.Audio.Samples);
            stage.ClearPending();
            if (!errored)
                stage.Initial = false;   // 处理器已成功跑过；下次报增量（出错弃处理器、保持 Initial）
            if (changed && i + 1 < count && i + 1 < chain.Stages.Count)
                chain.Stages[i + 1].MarkAudio(SegStart(chain), SegEnd(chain));
            chain.RunStage++;
        }

        // 本链处理完，换下一段（或收尾）。
        mActiveChain = null;
        PumpNext();
    }

    // 提交某级输出，返回输出是否相对上次变化（按重采样前的引擎输出数组引用为身份键）：
    // 引擎在「无关变化」时返回与上次相同的数组引用 → 不变 → 下游整段复用、不重跑；返回新数组 → 变 → 级联下游。
    bool CommitStageOutput(StageState stage, MonoAudio audio, float[]? rawSamples)
    {
        bool changed = !stage.HasOutput || !ReferenceEquals(rawSamples, stage.RawSamples);
        if (changed)
        {
            var outAudio = audio;
            if (outAudio.Samples != null && outAudio.Samples.Length > 0 && outAudio.SampleRate != AudioEngine.SampleRate.Value)
            {
                var data = AudioUtils.Resample(outAudio.Samples, 1, outAudio.SampleRate, AudioEngine.SampleRate.Value);
                outAudio = new MonoAudio(outAudio.StartTime, AudioEngine.SampleRate.Value, data);
            }
            outAudio.Samples ??= [];
            stage.RawSamples = rawSamples;
            stage.Output = outAudio;
        }
        stage.HasOutput = true;
        return changed;
    }

    static double SegStart(SegmentChain chain) => chain.Input.StartTime;

    static double SegEnd(SegmentChain chain)
    {
        var s = chain.Input.Samples;
        return chain.Input.StartTime + (s == null ? 0 : (double)s.Length / chain.Input.SampleRate);
    }

    // 各段链处理完：收集各段末级输出（无 effect 时即段输入）+ 其波形为段列表（每段波形按 Samples 引用相等
    // 缓存、只重算重跑过的段）。原子换数组引用 → 播放/UI 跨线程读到的恒是完整一份。
    void FinalizeChain()
    {
        int effectCount = mPart.Effects.Count;
        var list = new List<SynthesizedSegment>(mChains.Count);
        foreach (var chain in mChains.Values)
        {
            var seg = chain.Finalize(effectCount);
            if (seg.Audio.Samples is { Length: > 0 })
                list.Add(seg);
        }
        mSynthesizedSegments = list.ToArray();
        StatusChanged?.Invoke();
    }

    // 标脏后保守重排：在飞 Process（任意段）可能命中其链或其下游，统一作废 + 重 pump。
    void InvalidateAndPump()
    {
        mChainGeneration++;
        CancelEffectProcessing();
        mActiveChain = null;
        PumpNext();
    }

    // 请求中止在飞 Process（协作式取消）。不在此 Dispose 处理器——处理器随段/级生命周期持久持有，
    // 由结构重建 / 段移除 / 采样率变 / 管线销毁统一释放，避免双重释放/竞态。无在飞时为 no-op。
    void CancelEffectProcessing()
    {
        mEffectCancellation?.Cancel();
    }

    // 重建 effect 自动化范围订阅（构造 / 结构变 / 自动化轨增删时）：effect 与轨皆少量，整建整拆最省心。
    // 同时重新播种各 effect 的参数/启用基线快照（供后续 SetEffectDirty 的 key 级 diff）。
    void RebuildEffectSubscriptions()
    {
        mEffectSubscriptions?.DisposeAll();
        mEffectSubscriptions = new DisposableManager();
        mLastProperties.Clear();
        mLastEnabled.Clear();

        var effects = mPart.Effects;
        for (int i = 0; i < effects.Count; i++)
        {
            int index = i;
            var effect = effects[i];
            mLastProperties[effect] = effect.Properties.GetInfo();
            mLastEnabled[effect] = effect.IsEnabled.Value;

            // 自动化轨懒加：轨集合变 → 重建订阅。
            effect.Automations.MapModified.Subscribe(RebuildEffectSubscriptions, mEffectSubscriptions);
            foreach (var kv in effect.Automations)
            {
                string automationId = kv.Key;
                kv.Value.RangeModified.Subscribe(
                    (relStart, relEnd) => OnEffectAutomationRangeModified(index, automationId, relStart, relEnd),
                    mEffectSubscriptions);
            }
        }
    }

    // 某 effect 某条自动化轨在某区间被改（part 相对 tick）→ 换算全局秒，标该 effect 链位的该轨脏。
    void OnEffectAutomationRangeModified(int index, string automationId, double relStartTick, double relEndTick)
    {
        if (mDisposed || index < 0 || index >= mPart.Effects.Count)
            return;

        double startSecond = RelTickToGlobalSecond(relStartTick);
        double endSecond = RelTickToGlobalSecond(relEndTick);
        foreach (var chain in mChains.Values)
        {
            if (index >= chain.Stages.Count)
                continue;
            chain.Stages[index].MarkAutomation(automationId, startSecond, endSecond);
        }
        InvalidateAndPump();
    }

    // part 相对 tick → 全局秒（与 effect SDK 面查询轴一致）；±∞（如默认值平移=全区间）原样透传。
    double RelTickToGlobalSecond(double relTick)
    {
        if (double.IsNegativeInfinity(relTick))
            return double.NegativeInfinity;
        if (double.IsPositiveInfinity(relTick))
            return double.PositiveInfinity;
        return mPart.TempoManager.GetTime(relTick + mPart.Pos.Value);
    }

    // 两份参数快照的按 key 差集（含改值 / 新增 / 删除的 key）；map 小、廉价。
    static List<string> DiffKeys(PropertyObject? last, PropertyObject current)
    {
        var result = new List<string>();
        var cur = current.Map;
        if (last == null)
        {
            foreach (var kv in cur)
                result.Add(kv.Key);
            return result;
        }

        var prev = last.Map;
        foreach (var kv in cur)
        {
            if (!prev.TryGetValue(kv.Key, out var pv) || !pv.Equals(kv.Value))
                result.Add(kv.Key);
        }
        foreach (var kv in prev)
        {
            if (!cur.ContainsKey(kv.Key))
                result.Add(kv.Key);
        }
        return result;
    }

    // 一次重处理相对上次的变化事实集（从 StageState 的累积脏构造，按值拷贝以与后续清空解耦）。
    sealed class StageChange : IEffectChange
    {
        public StageChange(StageState stage)
        {
            IsInitial = stage.Initial;
            mAudioChanged = stage.AudioChanged;
            mAudioStart = stage.AudioStart;
            mAudioEnd = stage.AudioEnd;
            mProperties = stage.ChangedProperties.Count == 0 ? Array.Empty<string>() : new List<string>(stage.ChangedProperties);
            mAutomations = stage.ChangedAutomations.Count == 0 ? sEmptyAutomations : new Dictionary<string, (double, double)>(stage.ChangedAutomations);
        }

        public bool IsInitial { get; }
        public bool TryGetAudioChange(out double startTime, out double endTime) { startTime = mAudioStart; endTime = mAudioEnd; return mAudioChanged; }
        public IReadOnlyCollection<string> ChangedProperties => mProperties;
        public IReadOnlyCollection<string> ChangedAutomations => mAutomations.Keys;
        public bool TryGetAutomationChange(string automationId, out double startTime, out double endTime)
        {
            if (mAutomations.TryGetValue(automationId, out var range))
            {
                startTime = range.Item1;
                endTime = range.Item2;
                return true;
            }
            startTime = endTime = 0;
            return false;
        }

        static readonly Dictionary<string, (double, double)> sEmptyAutomations = new();
        readonly bool mAudioChanged;
        readonly double mAudioStart;
        readonly double mAudioEnd;
        readonly IReadOnlyCollection<string> mProperties;
        readonly Dictionary<string, (double, double)> mAutomations;
    }

    // 效果链输入：整段上游音频 + 该 effect 参数快照 + 自动化取值入口。
    class EffectChainInput(MonoAudio audio, MidiPart part, IEffect effect) : IEffectSynthesisInput
    {
        public MonoAudio Audio => audio;
        public Foundation.PropertyObject Properties => effect.Properties.GetInfo();

        public bool TryGetAutomation(string automationId, [MaybeNullWhen(false)][NotNullWhen(true)] out IAutomationEvaluator? automation)
        {
            if (!effect.AutomationConfigs.ContainsKey(automationId))
            {
                automation = null;
                return false;
            }
            automation = new EffectAutomationEvaluator(part, effect, automationId);
            return true;
        }
    }

    class EffectChainOutput : IEffectSynthesisOutput
    {
        public MonoAudio Audio { get; set; }
    }

    // 某个 effect 的某条自动化轨按时间求值（effect SDK 面查询轴 = 全局秒；此处秒 → tick → effect 自动化值）。
    class EffectAutomationEvaluator(MidiPart part, IEffect effect, string automationID) : IAutomationEvaluator
    {
        public double[] Evaluate(IReadOnlyList<double> points)
        {
            double pos = part.Pos.Value;
            var ticks = part.TempoManager.GetTicks(points);
            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i] -= pos;
            }
            return effect.GetAutomationValues(ticks, automationID);
        }
    }

    // 单级的持久处理状态：持久处理器 + 最近输出（+ 重采样前身份键）+ 自上次成功处理以来累积的变化事实。
    sealed class StageState
    {
        public IEffectProcessor? Processor;
        public MonoAudio Output;
        public float[]? RawSamples;   // 重采样前的引擎输出数组（passthrough 时 = 输入数组）；身份键
        public bool HasOutput;
        public bool Initial = true;   // 处理器尚未被调用过（下次 Process 报 IsInitial）

        public bool Dirty;
        public bool AudioChanged;
        public double AudioStart;
        public double AudioEnd;
        public readonly HashSet<string> ChangedProperties = new();
        public readonly Dictionary<string, (double Start, double End)> ChangedAutomations = new();

        public void MarkAudio(double start, double end)
        {
            if (!AudioChanged) { AudioStart = start; AudioEnd = end; AudioChanged = true; }
            else { AudioStart = Math.Min(AudioStart, start); AudioEnd = Math.Max(AudioEnd, end); }
            Dirty = true;
        }

        public void MarkProperty(string key)
        {
            ChangedProperties.Add(key);
            Dirty = true;
        }

        public void MarkAutomation(string id, double start, double end)
        {
            if (ChangedAutomations.TryGetValue(id, out var range))
                ChangedAutomations[id] = (Math.Min(range.Start, start), Math.Max(range.End, end));
            else
                ChangedAutomations[id] = (start, end);
            Dirty = true;
        }

        // 清累积的变化事实（Initial 不在此管理——由处理器创建/弃置/成功处理的落点维护）。
        public void ClearPending()
        {
            Dirty = false;
            AudioChanged = false;
            ChangedProperties.Clear();
            ChangedAutomations.Clear();
        }

        public void DisposeProcessor()
        {
            if (Processor == null)
                return;
            try { Processor.Dispose(); }
            catch (Exception ex) { Log.Error("Effect processor dispose failed: " + ex); }
            Processor = null;
        }
    }

    // 单段的效果链：该段工程率输入 + 各级持久状态（cache[segment][stage]）+ 处理游标 + 建链时的 Commit 版本。
    sealed class SegmentChain
    {
        public SynthesisContext.AudioSegment Segment { get; }
        public MonoAudio Input { get; private set; }
        public int ProcessedVersion { get; private set; }
        public readonly List<StageState> Stages = new();
        public int RunStage;

        public SegmentChain(SynthesisContext.AudioSegment segment, MonoAudio input, int processedVersion, int effectCount)
        {
            Segment = segment;
            Input = input;
            ProcessedVersion = processedVersion;
            ResetStages(effectCount);
        }

        // 结构变 / 新建：弃旧级处理器，按链长建全 Initial 脏级。
        public void ResetStages(int effectCount)
        {
            foreach (var stage in Stages)
                stage.DisposeProcessor();
            Stages.Clear();
            for (int i = 0; i < effectCount; i++)
                Stages.Add(new StageState { Dirty = true, Initial = true });
            RunStage = 0;
        }

        // voice 同握柄重 Commit：换输入 + 标 stage0 音频变（保留处理器，引擎复用非音频态）。
        public void UpdateInput(MonoAudio input, int processedVersion, int effectCount)
        {
            Input = input;
            ProcessedVersion = processedVersion;
            if (Stages.Count != effectCount)
            {
                ResetStages(effectCount);
                return;
            }
            if (Stages.Count > 0)
            {
                double start = Input.StartTime;
                double end = start + (Input.Samples == null ? 0 : (double)Input.Samples.Length / Input.SampleRate);
                Stages[0].MarkAudio(start, end);
            }
        }

        public void DisposeAll()
        {
            foreach (var stage in Stages)
                stage.DisposeProcessor();
        }

        public bool NeedsWork()
        {
            foreach (var stage in Stages)
            {
                if (stage.Dirty)
                    return true;
            }
            return false;
        }

        // 链尾段输出 + 波形：按 final 的 Samples 引用相等缓存——重跑产生新数组才重算波形，未变则复用。
        public SynthesizedSegment Finalize(int effectCount)
        {
            MonoAudio final;
            if (effectCount > 0 && Stages.Count >= effectCount && Stages[effectCount - 1].HasOutput)
                final = Stages[effectCount - 1].Output;
            else
                final = Input;

            if (!ReferenceEquals(final.Samples, mFinalSamples) || mWaveform == null)
            {
                mFinalSamples = final.Samples;
                mFinal = final;
                mWaveform = new Waveform(final.Samples ?? []);
            }
            return new SynthesizedSegment(mFinal, mWaveform);
        }

        float[]? mFinalSamples;
        MonoAudio mFinal;
        Waveform? mWaveform;
    }

    readonly MidiPart mPart;
    readonly SynchronizationContext mSyncContext;
    readonly SynthesisContext mContext;
    readonly ISynthesisSession mSession;
    readonly Action mOnSessionStatusChanged;
    readonly Action mOnAudioSegmentsChanged;
    readonly CancellationTokenSource mCancellation = new();

    bool mIsBusy;
    bool mDisposed;

    // 各已完成音频段（工程率）：链尾输出 + 波形；播放/波形按段消费。原子换引用、跨线程读安全。
    SynthesizedSegment[] mSynthesizedSegments = [];

    // 按段效果链：每段一条独立链缓存（cache[segment][stage]），段间串行（一次一个 effect 处理器）。
    readonly Dictionary<SynthesisContext.AudioSegment, SegmentChain> mChains = new();
    SegmentChain? mActiveChain;
    int mChainGeneration = 0;
    bool mEffectBusy;                             // 在飞 Process 未归（await 未返回）
    CancellationTokenSource? mEffectCancellation; // 在飞 Process 的取消源

    // effect 自动化范围订阅（结构变重建）+ 各 effect 参数/启用基线（供 key 级 diff）。
    DisposableManager? mEffectSubscriptions;
    readonly Dictionary<IEffect, PropertyObject> mLastProperties = new();
    readonly Dictionary<IEffect, bool> mLastEnabled = new();
}
