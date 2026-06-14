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
// 效果链（按段增量）：voice 经 context.CreateAudioSegment 交付的每个音频段各自独立过链
// （cache[segment][stage]）——段重 Commit（或被新握柄替换）只重跑该段的链、其余段缓存复用；
// effect 参数/启用/自动化变化则各段从该级重跑、链结构变化各段从 0。段间串行（一次一个 effect 任务，
// SVC 慢、避免资源爆）。链尾各段末级输出按时间拼成最终音频。段失效由 context.AudioSegmentsChanged 驱动。
internal sealed class VoiceSynthesisPipeline : IDisposable
{
    // 状态/产物有更新（已 marshal 到数据线程），宿主 UI 收到直接刷新；区域信息看 GetStatus()。
    public event Action? StatusChanged;

    public ISynthesisSession Session => mSession;
    public bool IsBusy => mIsBusy;

    // 链尾最终音频（工程采样率；无 effect 时即 voice 输出）。null = 尚无任何已合成音频。
    public MonoAudio? SynthesizedAudio => mFinalAudio;
    public Waveform? Waveform => mWaveform;
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
            FinishDispose();
            return;
        }

        // 段产物经 context.AudioSegmentsChanged（Commit 时）已驱动各段链重跑；此处只回填音素 + 刷新。
        WriteBackPhonemes();
        StatusChanged?.Invoke();
    }

    // —— effect 链脏（某 effect 参数/启用/自动化变化，或链结构变化）：各段从该级起重跑，
    //    每段上游各级缓存复用、voice 段音频不重拉。 ——
    public void SetEffectChainDirty(int effectIndex)
    {
        if (mDisposed)
            return;

        int from = Math.Max(0, effectIndex);
        foreach (var chain in mChains.Values)
        {
            if (chain.StageOutputs.Count > from)
                chain.StageOutputs.RemoveRange(from, chain.StageOutputs.Count - from);
            if (chain.RunStage > from)
                chain.RunStage = from;
        }

        // 活动段也受影响（所有段从 from 重跑）：中止在飞任务、从头排。
        mChainGeneration++;
        StopEffectTask();
        mActiveChain = null;
        PumpNext();
    }

    // 工程采样率变了：各段 native 音频按新率重做适配 + 全部从头重跑（清空链缓存后 reconcile 重建）。
    public void OnSampleRateChanged()
    {
        if (mDisposed)
            return;

        mChainGeneration++;
        StopEffectTask();
        mActiveChain = null;
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
        StopEffectTask();
        mContext.Dispose();

        // 槽位在 await 真正返回时才释放：合成中则延迟到 Dispatch 的收尾再销毁会话。
        if (!mIsBusy)
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

    // 段集 / 段内容变化（context.AudioSegmentsChanged 驱动）→ 同步各段链：新增/重建脏段（重 Commit
    // 版本变 = 重建），移除已销毁/已变未提交的段；再驱动串行运行器。命中正在处理的活动段则中止其任务。
    void ReconcileAndPump()
    {
        if (mDisposed)
            return;

        int nativeRate = mSession.SampleRate;
        var present = new HashSet<SynthesisContext.AudioSegment>();
        foreach (var segment in mContext.AudioSegments)
        {
            if (!segment.IsCommitted || segment.Samples.Length == 0)
                continue;

            present.Add(segment);
            if (mChains.TryGetValue(segment, out var existing) && existing.ProcessedVersion == segment.CommitVersion)
                continue;

            // 新段或同握柄重 Commit：重建该段链（native → 工程率重采样，缓存清零、从 stage 0 起）。
            if (mActiveChain != null && mActiveChain.Segment == segment)
            {
                mChainGeneration++;
                StopEffectTask();
                mActiveChain = null;
            }
            var input = ResampleToEngineRate(new MonoAudio((double)segment.SampleOffset / nativeRate, nativeRate, segment.Samples));
            mChains[segment] = new SegmentChain(segment, input, segment.CommitVersion);
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
                if (mActiveChain != null && mActiveChain.Segment == key)
                {
                    mChainGeneration++;
                    StopEffectTask();
                    mActiveChain = null;
                }
                mChains.Remove(key);
            }
        }

        if (mActiveChain == null)
            PumpNext();
    }

    // 选下一个需要处理的段链（段间串行：一次只跑一个）。无则收尾拼装最终音频。
    void PumpNext()
    {
        if (mDisposed)
            return;

        int effectCount = mPart.Effects.Count;
        foreach (var chain in mChains.Values)
        {
            if (chain.RunStage < effectCount)
            {
                mActiveChain = chain;
                mChainGeneration++;
                ProcessActiveStage(mChainGeneration);
                return;
            }
        }

        mActiveChain = null;
        FinalizeChain();
    }

    // 处理活动段当前级；effect 任务异步完成后推进下一级，段处理完则换下一段。bypass / 引擎缺失 = passthrough。
    void ProcessActiveStage(int generation)
    {
        if (generation != mChainGeneration || mDisposed || mActiveChain is not { } chain)
            return;

        var effects = mPart.Effects;
        if (chain.RunStage >= effects.Count)
        {
            mActiveChain = null;
            PumpNext();
            return;
        }

        var effect = effects[chain.RunStage];
        var input = chain.RunStage == 0 ? chain.Input : chain.StageOutputs[chain.RunStage - 1];

        var engine = effect.IsEnabled.Value ? EffectManager.GetInitedEngine(effect.Type) : null;
        if (engine == null)
        {
            chain.StageOutputs.Add(input);
            chain.RunStage++;
            ProcessActiveStage(generation);
            return;
        }

        IEffectSynthesisTask task;
        try
        {
            var inputObj = new EffectChainInput(input, mPart, effect);
            var output = new EffectChainOutput();
            task = engine.CreateSynthesisTask(inputObj, output);
            task.Complete += () => mSyncContext.Post(_ => OnStageComplete(generation, output), null);
            task.Error += (err) => mSyncContext.Post(_ => OnStageError(generation, err, input), null);
            task.Progress += (p) => mSyncContext.Post(_ => { if (!mDisposed) StatusChanged?.Invoke(); }, null);
        }
        catch (Exception ex)
        {
            Log.Error(string.Format("Effect {0} create task failed: {1}", effect.Type, ex));
            chain.StageOutputs.Add(input);
            chain.RunStage++;
            ProcessActiveStage(generation);
            return;
        }

        mEffectTask = task;
        task.Start();
    }

    void OnStageComplete(int generation, EffectChainOutput output)
    {
        if (generation != mChainGeneration || mDisposed || mActiveChain is not { } chain)
            return; // 已被新一轮重跑取代，丢弃过期结果

        StopEffectTask();

        var audio = output.Audio;
        if (audio.Samples != null && audio.Samples.Length > 0 && audio.SampleRate != AudioEngine.SampleRate.Value)
        {
            var data = AudioUtils.Resample(audio.Samples, 1, audio.SampleRate, AudioEngine.SampleRate.Value);
            audio = new MonoAudio(audio.StartTime, AudioEngine.SampleRate.Value, data);
        }
        audio.Samples ??= [];

        chain.StageOutputs.Add(audio);
        chain.RunStage++;
        ProcessActiveStage(generation);
    }

    // effect 出错：记录并 passthrough 该级（不中断该段音频播放，优雅降级）。
    void OnStageError(int generation, string error, MonoAudio input)
    {
        if (generation != mChainGeneration || mDisposed || mActiveChain is not { } chain)
            return;

        StopEffectTask();
        Log.Error(string.Format("Effect stage {0} error: {1}", chain.RunStage, error));
        chain.StageOutputs.Add(input);
        chain.RunStage++;
        ProcessActiveStage(generation);
    }

    // 各段链处理完：各段末级输出（无 effect 时即段输入）按时间拼成单条最终音频（工程率，空洞留 0、重叠混音）。
    void FinalizeChain()
    {
        int effectCount = mPart.Effects.Count;
        int engineRate = AudioEngine.SampleRate.Value;

        long start = long.MaxValue;
        long end = long.MinValue;
        foreach (var chain in mChains.Values)
        {
            var final = FinalOf(chain, effectCount);
            if (final.Samples is not { Length: > 0 } samples)
                continue;
            long offset = (long)(final.StartTime * engineRate);
            start = Math.Min(start, offset);
            end = Math.Max(end, offset + samples.Length);
        }

        if (end > start)
        {
            long baseOffset = start;
            int sampleCount = (int)(end - baseOffset);
            var buffer = new float[sampleCount];
            foreach (var chain in mChains.Values)
            {
                var final = FinalOf(chain, effectCount);
                if (final.Samples is not { Length: > 0 } samples)
                    continue;
                long offset = (long)(final.StartTime * engineRate);
                long from = Math.Max(baseOffset, offset);
                long to = Math.Min(baseOffset + sampleCount, offset + samples.Length);
                for (long i = from; i < to; i++)
                    buffer[i - baseOffset] += samples[i - offset];
            }
            mFinalAudio = new MonoAudio((double)baseOffset / engineRate, engineRate, buffer);
            mWaveform = new(buffer);
        }
        else
        {
            mFinalAudio = null;
            mWaveform = null;
        }

        StatusChanged?.Invoke();
    }

    // 段的链尾输出：无 effect 时即段输入；否则末级缓存（PumpNext 仅在所有段处理完才收尾，故缓存已齐）。
    static MonoAudio FinalOf(SegmentChain chain, int effectCount)
        => effectCount == 0 ? chain.Input : chain.StageOutputs[effectCount - 1];

    void StopEffectTask()
    {
        if (mEffectTask == null)
            return;

        mEffectTask.Stop();
        (mEffectTask as IDisposable)?.Dispose();
        mEffectTask = null;
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

    // 单段的效果链状态：该段工程率输入 + 各级缓存输出 + 下一待处理级 + 建链时的 Commit 版本。
    // 不变量：StageOutputs.Count == RunStage（每级算完即追加，从 fromStage 重跑前截断到 fromStage）。
    sealed class SegmentChain(SynthesisContext.AudioSegment segment, MonoAudio input, int processedVersion)
    {
        public SynthesisContext.AudioSegment Segment => segment;
        public MonoAudio Input => input;
        public int ProcessedVersion => processedVersion;
        public readonly List<MonoAudio> StageOutputs = new();
        public int RunStage;
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

    // 链尾最终音频（各段末级输出按时间拼接，工程率）。
    MonoAudio? mFinalAudio;
    Waveform? mWaveform;

    // 按段效果链：每段一条独立链缓存（cache[segment][stage]），段间串行（一次一个 effect 任务）。
    readonly Dictionary<SynthesisContext.AudioSegment, SegmentChain> mChains = new();
    SegmentChain? mActiveChain;
    int mChainGeneration = 0;
    IEffectSynthesisTask? mEffectTask = null;
}
