using System;
using System.Collections.Generic;
using System.Threading;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.Data.Synthesis;

// 一个 part 的合成管线宿主包装：持有会话级 context + ISynthesisSession（voice 半部），
// 对上提供调度面（peek/dispatch + 并发槽位状态）、音素回填；effect 半部委托给 EffectGraph
// （「effect 实例 × 段」反应式处理器图，自管失效与重处理、跨段并行、链尾按时间混音）。
//
// 线程纪律：除标注外全部成员仅数据线程访问；session.StatusChanged 允许任意线程触发，
// 这里负责 marshal 回数据线程再对外转发。
internal sealed class VoiceSynthesisPipeline : IDisposable
{
    // 状态/产物有更新（已 marshal 到数据线程），宿主 UI 收到直接刷新；区域信息看 GetStatus()。
    public event Action? StatusChanged;

    public ISynthesisSession Session => mSession;
    public bool IsBusy => mIsBusy;

    // 各已完成音频段（工程率，链尾输出 + 波形）；播放/波形按段消费，不再拼整 part 单条 buffer。
    public IReadOnlyList<SynthesizedSegment> SynthesizedSegments => mEffectGraph.SynthesizedSegments;
    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => mSession.SynthesizedPitch;
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mSession.SynthesizedParameters;
    // 某个 effect 的回显曲线（聚合其各段 processor 的回显）；非本管线的 effect 返回空 map。
    public IReadOnlyMap<string, SynthesizedParameter> GetEffectSynthesizedParameters(IEffect effect) => mEffectGraph.GetSynthesizedParameters(effect);
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
        // effect 半部：voice 段经 context.AudioSegmentsChanged 自驱动效果图；产物更新经 onChanged 回调转发 UI；
        // 销毁中最后一个在飞 effect 节点收尾时经 onSettled 回调管线重检销毁（voice/effect 都归才销毁会话）。
        mEffectGraph = new EffectGraph(mPart, mContext, mCancellation.Token,
            onChanged: () =>
            {
                if (!mDisposed)
                    StatusChanged?.Invoke();
            },
            onSettled: TryFinishDispose);
    }

    // —— 调度面（Editor 驱动）——

    // part 界裁窗（纯宿主侧）：peek 与 dispatch 共用，保证两者窗口一致 → 插件确定性分片重导出同一块。
    void ClampToPart(ref double startTime, ref double endTime)
    {
        startTime = Math.Max(startTime, mPart.TempoManager.GetTime(mPart.StartPos));
        endTime = Math.Min(endTime, mPart.TempoManager.GetTime(mPart.EndPos));
    }

    // 窗内"下一块待合成"的廉价 peek；仅会话空闲时有意义。窗口与返回边界为全局秒。
    // 调度窗先与 part 界求交再问插件：part 被裁短后留在界外的 note 不该被合成
    //（呈现端本就按 part 界裁剪音频）；纯宿主侧裁窗，零接口变化。跨界 block 仍整块合成。
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
            Log.Error("GetNextSegment failed: " + ex);
            return null;
        }
    }

    // commit：与 peek 在同一调度 tick 内同步衔接；入参为选中它的那次 peek 的同一窗口（宿主据 ahead/behind
    // 回传），插件按同一窗口确定性重导出同一块。快照由插件在 SynthesizeNext 的同步前缀经
    // context.GetSnapshot 自行拉取。await 返回 = 槽位释放。
    public async void Dispatch(double startTime, double endTime)
    {
        if (mIsBusy || mDisposed)
            return;

        ClampToPart(ref startTime, ref endTime);   // 与 peek 同一裁窗，确保重导出命中同一块

        mIsBusy = true;
        try
        {
            await mSession.SynthesizeNext(startTime, endTime, mCancellation.Token);
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

        // 段产物经 context.AudioSegmentsChanged（Commit 时）已驱动效果图重跑；此处只回填音素 + 刷新。
        WriteBackPhonemes();
        StatusChanged?.Invoke();
    }

    // —— effect 失效入口（数据线程；由 MidiPart 转发）——

    // 链结构变化（增删/重排/启用切换）：效果图据当前真相重排（弃旧建新、复用未变节点）。
    public void OnEffectChainStructureChanged()
    {
        if (mDisposed)
            return;
        mEffectGraph.OnStructureChanged();
    }

    // 工程采样率变了：各段输入按新率重做适配 + 全图重处理。
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

        // 槽位在 await 真正返回时才释放：voice/effect 仍在飞则延迟到其收尾再销毁。
        TryFinishDispose();
    }

    // voice 与 effect 在飞都归后才销毁会话（不在飞时立即）。effect 图自身已请求销毁、在飞节点收尾时自毁。
    void TryFinishDispose()
    {
        if (!mDisposed || mIsBusy || mEffectGraph.IsBusy)
            return;

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
                note.SynthesizedPhonemes = map.TryGetValue(note, out var list) ? list.ToArray() : [];
            }
        }
        catch (Exception ex)
        {
            Log.Error("Write back phonemes failed: " + ex);
        }
    }

    readonly MidiPart mPart;
    readonly SynchronizationContext mSyncContext;
    readonly SynthesisContext mContext;
    readonly ISynthesisSession mSession;
    readonly Action mOnSessionStatusChanged;
    readonly CancellationTokenSource mCancellation = new();
    readonly EffectGraph mEffectGraph;

    bool mIsBusy;
    bool mDisposed;
}
