using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.Data.Synthesis;

// 一个 part 的合成管线宿主包装：持有会话级 context + IVoiceSession（voice 半部），
// 对上提供调度面（peek/dispatch + 并发槽位状态）、音素回填；effect 半部委托给 EffectGraph
// （「effect 实例 × 段」反应式处理器图，自管失效与重处理、跨段并行、链尾按时间混音）。
//
// 线程纪律：除标注外全部成员仅数据线程访问；session.StatusChanged 允许任意线程触发，
// 这里负责 marshal 回数据线程再对外转发。
internal sealed class VoiceSynthesisPipeline : ISynthesisPipeline
{
    // 状态/产物有更新（已 marshal 到数据线程），宿主 UI 收到直接刷新；区域信息看 GetStatus()。任意产物/状态变化都触发（合并信号）。
    public event Action? StatusChanged;
    // 分离产物信号（各自仅对应产物变化时触发）：供精确响应的消费者订阅，避免接到合并的 StatusChanged。
    public event Action? PhonemesChanged;
    public event Action? ParametersChanged;
    public event Action? PitchChanged;

    public IVoiceSynthesisSession Session => mSession;
    public bool IsBusy => mIsBusy;

    // 各已完成音频段（工程率，链尾输出 + 波形）；播放/波形按段消费，不再拼整 part 单条 buffer。
    public IReadOnlyList<SynthesizedSegment> SynthesizedSegments => mEffectGraph.SynthesizedSegments;
    public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch => mSession.SynthesizedPitch.Segments;
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mSession.SynthesizedParameters;
    // 某个 effect 的回显曲线（聚合其各段 processor 的回显）；非本管线的 effect 返回空 map。
    public IReadOnlyMap<string, SynthesizedParameter> GetEffectSynthesizedParameters(IEffect effect) => mEffectGraph.GetSynthesizedParameters(effect);
    // 状态带显示图层（画家算法，底层在前）：声称完成垫底 → 音频事实 → voice Pending（排队声明，
    // 盖过陈旧事实绿、被活动覆盖）→ effect 活动声称 → voice 合成中/失败恒顶。
    // 亮绿 Final 只能来自链尾音频事实（图层来源保证「绿=听到的即最终」）。
    public IReadOnlyList<SynthesisDisplaySegment> GetStatus()
    {
        var layers = new List<SynthesisDisplaySegment>();
        var effectActive = new List<SynthesisDisplaySegment>();
        var claims = mSession.Status;
        SynthesisDisplayLayers.AppendSessionClaims(layers, claims, SessionClaimLayer.ClaimedDone);
        mEffectGraph.CollectDisplaySegments(layers, effectActive);
        SynthesisDisplayLayers.AppendSessionClaims(layers, claims, SessionClaimLayer.Pending);
        layers.AddRange(effectActive);
        SynthesisDisplayLayers.AppendSessionClaims(layers, claims, SessionClaimLayer.Active);
        return layers;
    }

    public VoiceSynthesisPipeline(MidiPart part, string voiceType, string voiceId)
    {
        mPart = part;
        mSyncContext = SynchronizationContext.Current ?? throw new InvalidOperationException("VoiceSynthesisPipeline must be created on the data thread.");
        mContext = new VoiceSynthesisContext(part, voiceId);
        mSession = VoicesManager.CreateSession(voiceType, mContext);
        // 按产物分流订阅 session 信号（各自 marshal 回数据线程）：音素信号才回填 note（WriteBackPhonemes），
        // 参数/音高/状态信号只触发 UI 重读重绘。高频的状态/进度 tick 因此不再带动音素回填。
        // 引擎标脏 / 重分块后不再报告脏块音素，回填把对应 note 置空（留白），无需宿主在数据层单点清除。
        mOnPhonemesChanged = Marshaled(() => { WriteBackPhonemes(); PhonemesChanged?.Invoke(); StatusChanged?.Invoke(); });
        mOnParametersChanged = Marshaled(() => { ParametersChanged?.Invoke(); StatusChanged?.Invoke(); });
        mOnPitchChanged = Marshaled(() => { PitchChanged?.Invoke(); StatusChanged?.Invoke(); });
        mOnStatusChanged = Marshaled(() => StatusChanged?.Invoke());
        mSession.SynthesizedPhonemesChanged.Subscribe(mOnPhonemesChanged);
        mSession.SynthesizedParametersChanged.Subscribe(mOnParametersChanged);
        mSession.SynthesizedPitchChanged.Subscribe(mOnPitchChanged);
        mSession.StatusChanged.Subscribe(mOnStatusChanged);
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
            return mSession.GetNextPendingSynthesisRange(startTime, endTime);
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed("GetNextPendingSynthesisRange failed", ex);
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
            Log.ErrorAttributed("SynthesizeNext threw", ex);
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

    // 把动作 marshal 回数据线程执行（session 出方向事件可任意线程触发）；已销毁则丢弃。
    Action Marshaled(Action action) => () => mSyncContext.Post(_ =>
    {
        if (!mDisposed)
            action();
    }, null);

    void FinishDispose()
    {
        mSession.SynthesizedPhonemesChanged.Unsubscribe(mOnPhonemesChanged);
        mSession.SynthesizedParametersChanged.Unsubscribe(mOnParametersChanged);
        mSession.SynthesizedPitchChanged.Unsubscribe(mOnPitchChanged);
        mSession.StatusChanged.Unsubscribe(mOnStatusChanged);
        try
        {
            mSession.Dispose();
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed("Session dispose failed", ex);
        }
        mCancellation.Dispose();
    }

    // note 的会话延音判定（MidiPart 判定缓存重建时调用；数据线程，插件异常由调用方护栏兜底）。
    // 代理恒存在：NoteProxyList.ProxyOf 对缺失映射兜底补建（不依赖增删事件时序），非 null note
    // 必得代理——不存在"note 已入集合、代理未建"的闪判窗口。
    public bool JudgeContinuation(INote note)
    {
        return mSession.IsContinuation(mContext.ProxyOf(note)!);
    }

    // 合成音素回填到 note（UI 音素显示消费面）：产物已按归属 note 的运行期 id 键，直拷到对应 note（免归组）。
    // 键是 VoiceSynthesisNoteId（宿主发号）；按每 note 当前代理的 Id 反查落回宿主 note；未在产物中的 note 置空（留白）。
    // 回显**如实落账、不做任何校验丢弃**：数据忠实存储，但显示是否读取由延音判定裁决（判定优先级
    // 最高——判定为延续的 note 其音素根本不被读取，违约回显即被忽略、兜底零成本）。回填对引擎世代
    // 零感知：legacy 适配器判定恒 false（老模型无乘客机制），其占位回显自然走普通内容显示。
    void WriteBackPhonemes()
    {
        try
        {
            var map = mSession.SynthesizedPhonemes;
            foreach (var note in mPart.Notes)
            {
                var proxy = mContext.ProxyOf(note);
                if (proxy != null && map.TryGetValue(proxy.Id, out var syllable) && syllable.PhonemeCount() > 0)
                    note.SynthesizedSyllable = syllable;
                else
                    note.SynthesizedSyllable = null;
            }
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed("Write back phonemes failed", ex);
        }
    }

    readonly MidiPart mPart;
    readonly SynchronizationContext mSyncContext;
    readonly VoiceSynthesisContext mContext;
    readonly IVoiceSynthesisSession mSession;
    readonly Action mOnPhonemesChanged;
    readonly Action mOnParametersChanged;
    readonly Action mOnPitchChanged;
    readonly Action mOnStatusChanged;
    readonly CancellationTokenSource mCancellation = new();
    readonly EffectGraph mEffectGraph;

    bool mIsBusy;
    bool mDisposed;
}
