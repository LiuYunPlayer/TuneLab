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
// 对上提供调度面（peek/dispatch + 并发槽位状态）、产物缓存（voice 音频拉取 + 采样率适配）
// 与效果链运行（voice 输出 → 各级 effect → 链尾最终音频）。
//
// 线程纪律：除标注外全部成员仅数据线程访问；session.StatusChanged 允许任意线程触发，
// 这里负责 marshal 回数据线程再对外转发。
//
// 效果链单元（v1）：对 session 的整条 part 音频全量过链——任何区域合成完成后整体重跑。
// 比按片增量粗，但 SVC 类离线模型常为整 part 一段，行为差异有限；按状态段增量过链缓后。
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

        PullProducts();
        RunEffectChain(0);
        StatusChanged?.Invoke();
    }

    // —— effect 链脏（某 effect 参数/启用/自动化变化，或链结构变化）：从该级起重跑，
    //    上游各级输出复用、voice 输出不重拉。 ——
    public void SetEffectChainDirty(int effectIndex)
    {
        if (mDisposed || mNativeAudio == null)
            return;

        RunEffectChain(Math.Max(0, effectIndex));
    }

    // 工程采样率变了：native 音频缓存不变，重做一次工程率适配 + 整链重跑。
    public void OnSampleRateChanged()
    {
        if (mDisposed || mNativeAudio == null)
            return;

        mVoiceAudio = ResampleToEngineRate(mNativeAudio.Value);
        RunEffectChain(0);
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        if (mDisposed)
            return;
        mDisposed = true;

        mCancellation.Cancel();
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

    // —— 产物拉取（数据线程）——
    // 音频时间对齐协议：全局 0 时刻 = 采样点 0；从插件经 context.CreateAudioSegment 交付的各已 Commit
    // 音频段取并集采样范围拼成单条 buffer（整数采样运算无舍入、重叠段混音叠加、空洞留 0）。
    // 提交①保持整 part 一条链：拼好整段后整链跑。
    void PullProducts()
    {
        try
        {
            var segments = mContext.AudioSegments;

            long start = long.MaxValue;
            long end = long.MinValue;
            foreach (var segment in segments)
            {
                if (!segment.IsCommitted || segment.Samples.Length == 0)
                    continue;

                start = Math.Min(start, segment.SampleOffset);
                end = Math.Max(end, segment.SampleOffset + segment.Samples.Length);
            }

            if (end > start)
            {
                int sampleRate = mSession.SampleRate;
                long offset = start;
                int sampleCount = (int)(end - offset);
                var buffer = new float[sampleCount];
                foreach (var segment in segments)
                {
                    if (!segment.IsCommitted || segment.Samples.Length == 0)
                        continue;

                    var samples = segment.Samples;
                    long segOffset = segment.SampleOffset;
                    long from = Math.Max(offset, segOffset);
                    long to = Math.Min(offset + sampleCount, segOffset + samples.Length);
                    for (long i = from; i < to; i++)
                        buffer[i - offset] += samples[i - segOffset];
                }
                mNativeAudio = new MonoAudio((double)offset / sampleRate, sampleRate, buffer);
                mVoiceAudio = ResampleToEngineRate(mNativeAudio.Value);
            }

            WriteBackPhonemes();
        }
        catch (Exception ex)
        {
            Log.Error("Pull synthesis products failed: " + ex);
        }
    }

    // 合成音素回填到 note（UI 音素显示消费面）：扁平时间线按出身 note 归组。
    void WriteBackPhonemes()
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

    static MonoAudio ResampleToEngineRate(MonoAudio native)
    {
        int engineRate = AudioEngine.SampleRate.Value;
        if (native.SampleRate == engineRate)
            return native;

        var resampled = AudioUtils.Resample(native.Samples, 1, native.SampleRate, engineRate);
        return new MonoAudio(native.StartTime, engineRate, resampled);
    }

    // —— 效果链（自旧 piece 管线迁移；输入 = 整条 part 的 voice 音频）——

    // 从 fromStage 级起重跑：保留 [0, fromStage) 各级缓存输出，重算其后各级。
    void RunEffectChain(int fromStage)
    {
        if (mVoiceAudio == null)
            return;

        mChainGeneration++;
        StopEffectTask();

        fromStage = Math.Clamp(fromStage, 0, mStageOutputs.Count);
        if (mStageOutputs.Count > fromStage)
            mStageOutputs.RemoveRange(fromStage, mStageOutputs.Count - fromStage);

        mRunStage = fromStage;
        ProcessNextStage(mChainGeneration);
    }

    // 依次处理链上各级；effect 任务异步完成后推进到下一级。bypass / 引擎缺失 = passthrough。
    void ProcessNextStage(int generation)
    {
        if (generation != mChainGeneration || mVoiceAudio == null)
            return;

        var effects = mPart.Effects;
        if (mRunStage >= effects.Count)
        {
            FinalizeChain();
            return;
        }

        var effect = effects[mRunStage];
        var input = mRunStage == 0 ? mVoiceAudio.Value : mStageOutputs[mRunStage - 1];

        var engine = effect.IsEnabled.Value ? EffectManager.GetInitedEngine(effect.Type) : null;
        if (engine == null)
        {
            mStageOutputs.Add(input);
            mRunStage++;
            ProcessNextStage(generation);
            return;
        }

        IEffectSynthesisTask task;
        try
        {
            var inputObj = new EffectChainInput(input, mPart, effect);
            var output = new EffectChainOutput();
            task = engine.CreateSynthesisTask(inputObj, output);
            int stage = mRunStage;
            task.Complete += () => mSyncContext.Post(_ => OnStageComplete(generation, stage, output), null);
            task.Error += (err) => mSyncContext.Post(_ => OnStageError(generation, stage, err, input), null);
            task.Progress += (p) => mSyncContext.Post(_ => { if (!mDisposed) StatusChanged?.Invoke(); }, null);
        }
        catch (Exception ex)
        {
            Log.Error(string.Format("Effect {0} create task failed: {1}", effect.Type, ex));
            mStageOutputs.Add(input);
            mRunStage++;
            ProcessNextStage(generation);
            return;
        }

        mEffectTask = task;
        task.Start();
    }

    void OnStageComplete(int generation, int stage, EffectChainOutput output)
    {
        if (generation != mChainGeneration || stage != mRunStage || mDisposed)
            return; // 已被新一轮重跑取代，丢弃过期结果

        StopEffectTask();

        var audio = output.Audio;
        if (audio.Samples != null && audio.Samples.Length > 0 && audio.SampleRate != AudioEngine.SampleRate.Value)
        {
            var data = AudioUtils.Resample(audio.Samples, 1, audio.SampleRate, AudioEngine.SampleRate.Value);
            audio = new MonoAudio(audio.StartTime, AudioEngine.SampleRate.Value, data);
        }
        audio.Samples ??= [];

        mStageOutputs.Add(audio);
        mRunStage++;
        ProcessNextStage(generation);
    }

    // effect 出错：记录并 passthrough 该级（不中断整段音频播放，优雅降级）。
    void OnStageError(int generation, int stage, string error, MonoAudio input)
    {
        if (generation != mChainGeneration || stage != mRunStage || mDisposed)
            return;

        StopEffectTask();
        Log.Error(string.Format("Effect stage {0} error: {1}", stage, error));
        mStageOutputs.Add(input);
        mRunStage++;
        ProcessNextStage(generation);
    }

    void FinalizeChain()
    {
        int count = mPart.Effects.Count;
        mFinalAudio = count == 0 ? mVoiceAudio : mStageOutputs[count - 1];
        mWaveform = mFinalAudio is { } final ? new(final.Samples) : null;
        StatusChanged?.Invoke();
    }

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

    readonly MidiPart mPart;
    readonly SynchronizationContext mSyncContext;
    readonly SynthesisContext mContext;
    readonly ISynthesisSession mSession;
    readonly Action mOnSessionStatusChanged;
    readonly CancellationTokenSource mCancellation = new();

    bool mIsBusy;
    bool mDisposed;

    // 音频产物：native = 插件原始率拉取的缓存；voice = 工程率适配后的链头输入；final = 链尾输出。
    MonoAudio? mNativeAudio;
    MonoAudio? mVoiceAudio;
    MonoAudio? mFinalAudio;
    Waveform? mWaveform;

    readonly List<MonoAudio> mStageOutputs = new();
    int mRunStage = 0;
    int mChainGeneration = 0;
    IEffectSynthesisTask? mEffectTask = null;
}
