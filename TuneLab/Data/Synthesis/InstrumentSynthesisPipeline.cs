using System;
using System.Collections.Generic;
using System.Threading;
using TuneLab.Extensions.Instruments;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.Data.Synthesis;

// 一个 instrument part 的合成管线宿主包装：持有会话级 InstrumentSynthesisContext + IInstrumentSession，
// 对上提供调度面（peek/dispatch + 并发槽位）；effect 半部委托给 EffectGraph（与 voice 共用，段是中性单元）。
// 与 VoiceSynthesisPipeline 同构，差异：无音素回填、SynthesizedPitch 恒空（instrument 仅产音频 + 参数回显）。
//
// 线程纪律：除标注外全部成员仅数据线程访问；session 出方向事件允许任意线程触发，这里 marshal 回数据线程再转发。
internal sealed class InstrumentSynthesisPipeline : ISynthesisPipeline
{
    public event Action? StatusChanged;
    // 参数回显信号 instrument 也有，会触发；音素/音高 instrument 无、永不触发（故 CS0067 抑制）。
    public event Action? ParametersChanged;
#pragma warning disable CS0067
    public event Action? PhonemesChanged;
    public event Action? PitchChanged;
#pragma warning restore CS0067

    public bool IsBusy => mIsBusy;

    public IReadOnlyList<SynthesizedSegment> SynthesizedSegments => mEffectGraph.SynthesizedSegments;
    // instrument 无合成音高回显：保持统一面，恒空（绘制端无需分支）。
    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => [];
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mSession.SynthesizedParameters;
    public IReadOnlyMap<string, SynthesizedParameter> GetEffectSynthesizedParameters(IEffect effect) => mEffectGraph.GetSynthesizedParameters(effect);
    // 状态带显示图层（与 voice 管线同构的画家算法组装；z 序见 VoiceSynthesisPipeline.GetStatus）。
    public IReadOnlyList<SynthesisDisplaySegment> GetStatus()
    {
        var layers = new List<SynthesisDisplaySegment>();
        var effectActive = new List<SynthesisDisplaySegment>();
        var claims = mSession.GetStatus();
        SynthesisDisplayLayers.AppendSessionClaims(layers, claims, SessionClaimLayer.ClaimedDone);
        mEffectGraph.CollectDisplaySegments(layers, effectActive);
        SynthesisDisplayLayers.AppendSessionClaims(layers, claims, SessionClaimLayer.Pending);
        layers.AddRange(effectActive);
        SynthesisDisplayLayers.AppendSessionClaims(layers, claims, SessionClaimLayer.Active);
        return layers;
    }

    public InstrumentSynthesisPipeline(MidiPart part, string instrumentType, string instrumentId)
    {
        mPart = part;
        mSyncContext = SynchronizationContext.Current ?? throw new InvalidOperationException("InstrumentSynthesisPipeline must be created on the data thread.");
        mContext = new InstrumentSynthesisContext(part, instrumentId);
        mSession = InstrumentsManager.CreateSession(instrumentType, mContext);
        // 按产物分流订阅 session 信号（各自 marshal 回数据线程）：instrument 只有参数回显与状态/进度，无音素/音高。
        mOnParametersChanged = Marshaled(() => { ParametersChanged?.Invoke(); StatusChanged?.Invoke(); });
        mOnStatusChanged = Marshaled(() => StatusChanged?.Invoke());
        mSession.SynthesizedParametersChanged.Subscribe(mOnParametersChanged);
        mSession.StatusChanged.Subscribe(mOnStatusChanged);
        // effect 半部：instrument 段经 context.AudioSegmentsChanged 自驱动效果图（与 voice 同一套 EffectGraph）。
        mEffectGraph = new EffectGraph(mPart, mContext, mCancellation.Token,
            onChanged: () =>
            {
                if (!mDisposed)
                    StatusChanged?.Invoke();
            },
            onSettled: TryFinishDispose);
    }

    // —— 调度面（Editor 驱动）——

    void ClampToPart(ref double startTime, ref double endTime)
    {
        startTime = Math.Max(startTime, mPart.TempoManager.GetTime(mPart.StartPos));
        endTime = Math.Min(endTime, mPart.TempoManager.GetTime(mPart.EndPos));
    }

    public SynthesisRange? PeekNext(double startTime, double endTime)
    {
        if (mIsBusy || mDisposed)
            return null;

        ClampToPart(ref startTime, ref endTime);
        if (endTime <= startTime)
            return null;

        try
        {
            return mSession.GetNextSegment(startTime, endTime);
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed("Instrument GetNextSegment failed", ex);
            return null;
        }
    }

    public async void Dispatch(double startTime, double endTime)
    {
        if (mIsBusy || mDisposed)
            return;

        ClampToPart(ref startTime, ref endTime);

        mIsBusy = true;
        try
        {
            await mSession.SynthesizeNext(startTime, endTime, mCancellation.Token);
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed("Instrument SynthesizeNext threw", ex);
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

        // 段产物经 context.AudioSegmentsChanged（Commit 时）已驱动效果图重跑；instrument 无音素回填，仅刷新。
        StatusChanged?.Invoke();
    }

    // —— effect 失效入口（数据线程；由 MidiPart 转发）——

    public void OnEffectChainStructureChanged()
    {
        if (mDisposed)
            return;
        mEffectGraph.OnStructureChanged();
    }

    public void OnSampleRateChanged()
    {
        if (mDisposed)
            return;
        mEffectGraph.OnSampleRateChanged();
    }

    public void Dispose()
    {
        if (mDisposed)
            return;
        mDisposed = true;

        mCancellation.Cancel();
        mEffectGraph.Dispose();
        mContext.Dispose();

        TryFinishDispose();
    }

    void TryFinishDispose()
    {
        if (!mDisposed || mIsBusy || mEffectGraph.IsBusy)
            return;

        FinishDispose();
    }

    Action Marshaled(Action action) => () => mSyncContext.Post(_ =>
    {
        if (!mDisposed)
            action();
    }, null);

    void FinishDispose()
    {
        mSession.SynthesizedParametersChanged.Unsubscribe(mOnParametersChanged);
        mSession.StatusChanged.Unsubscribe(mOnStatusChanged);
        try
        {
            mSession.Dispose();
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed("Instrument session dispose failed", ex);
        }
        mCancellation.Dispose();
    }

    readonly MidiPart mPart;
    readonly SynchronizationContext mSyncContext;
    readonly InstrumentSynthesisContext mContext;
    readonly IInstrumentSynthesisSession mSession;
    readonly Action mOnParametersChanged;
    readonly Action mOnStatusChanged;
    readonly CancellationTokenSource mCancellation = new();
    readonly EffectGraph mEffectGraph;

    bool mIsBusy;
    bool mDisposed;
}
